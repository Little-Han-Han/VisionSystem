using System;
using System.Collections.Generic;
using System.Threading;
using VisionPlatform.Core;

namespace VisionPlatform.Communication
{
    /// <summary>
    /// 通讯端点注册表（静态，仿 <c>FlowOutputChannel</c> / <c>RecipeStore</c>）：
    /// 按 <see cref="AppConfig.Comm"/> 里的配置持有<strong>活的连接对象</strong>
    /// （<see cref="TcpClientSession"/> / <see cref="TcpServerSession"/> / <see cref="ModbusTcpClient"/>），
    /// 通讯窗体与流程节点都按<strong>名称</strong>取用，两侧互不持有引用。
    /// <para>
    /// 【为什么要有它】改造前只有三个固定的 bool（<see cref="CommState"/>），既没有名称
    /// 也不持有连接，流程侧根本拿不到连接对象。现在用户可自定义任意多条同名不同协议的通讯，
    /// 注册表就是「名称 → 活连接」的唯一权威映射。
    /// </para>
    /// <para>
    /// 【线程安全】<see cref="UiSettings.MaxParallelFlows"/> 默认 4，多条流程线程会并行调用本类，
    /// 所有共享状态一律 <c>lock (Sync)</c>（共享知识 29.2，写法同 FlowOutputChannel）。
    /// 连接对象的 <c>Received</c> / <c>StatusChanged</c> 事件由后台线程抛出，
    /// 订阅者若要更新界面须自行 Invoke。
    /// </para>
    /// <para>
    /// 【与 CommState 的关系】本类在连接状态变化时同步刷新 CommState 的三个 bool，
    /// 使 MainForm 的运行前校验（HasActiveChannel）与 SettingsForm 的状态文本
    /// 一行都不用改就继续工作。
    /// </para>
    /// </summary>
    /// <remarks>
    /// <para>═══ 后续「发送数据 / 接收数据」流程节点的用法（本轮不实现节点，只留 API）═══</para>
    /// <para>
    /// 1. 节点参数里保存一个通讯名称（取自 <see cref="Names"/>，下拉框让用户选）；
    /// </para>
    /// <para>
    /// 2. 发送数据节点：
    /// <c>CommRegistry.SendText(name, text)</c> —— TCP 客户端走 Send，TCP 服务端走 Broadcast，
    /// Modbus 返回失败（Modbus 只支持 <see cref="ModbusWrite"/> / <see cref="ModbusRead"/>）；
    /// </para>
    /// <para>
    /// 3. 接收数据节点：<c>CommRegistry.LastReceived(name)</c> 取最近一条收到的文本（无则空串）；
    /// 若要「有新数据才往下走」，订阅 <see cref="Changed"/> 并在回调里比较上次取到的文本；
    /// </para>
    /// <para>
    /// 4. Modbus 读写：<c>ModbusRead(name, addr, count)</c> / <c>ModbusWrite(name, addr, value)</c>，
    /// 未连接时会自动按配置连接一次；
    /// </para>
    /// <para>
    /// 5. <see cref="Changed"/> 在「配置变更」与「连接状态变更」两种情况下都会触发，
    /// 流程图侧订阅它即可实时刷新可选通讯列表。
    /// </para>
    /// </remarks>
    public static class CommRegistry
    {
        /// <summary>订阅 / 退订 / 读写共享状态的统一锁。</summary>
        private static readonly object Sync = new object();

        /// <summary>
        /// 自动连接失败后的冷却时间（毫秒）。
        /// <para>
        /// 【为什么必须有】PLC 掉线时，边沿轮询（100ms 一次）、心跳（1s 一次）、流程节点
        /// 都会走到"未连接 → 自动连接"。TCP 连不上一个不可达地址要等满超时（默认 2s），
        /// 不加冷却就是<strong>每秒多次 × 每次 2 秒</strong>的连接风暴。
        /// 冷却期内自动连接直接快速失败，重连频率被压到约 1 次 / 5 秒。
        /// </para>
        /// <para>【与手动连接的关系】界面上的"连接"按钮走 <see cref="Connect"/>，不受冷却限制 ——
        /// 用户点了按钮就该立刻尝试。</para>
        /// </summary>
        private const int AutoConnectBackoffMs = 5000;

        /// <summary>名称 → 活连接（名称比较忽略大小写，与配置侧一致）。</summary>
        private static readonly Dictionary<string, Channel> Channels =
            new Dictionary<string, Channel>(StringComparer.OrdinalIgnoreCase);

        /// <summary>名称列表，顺序与 <see cref="CommSettings.Endpoints"/> 一致。</summary>
        private static readonly List<string> Order = new List<string>();

        private static bool _initialized;

        /// <summary>
        /// 配置或连接状态发生变化（新增 / 删除 / 改名 / 改参数 / 连上 / 断开 / 收到数据）。
        /// 由后台线程抛出，订阅者更新界面须自行 Invoke。
        /// </summary>
        public static event EventHandler Changed;

        /// <summary>
        /// 按 <see cref="ConfigStore.Current"/> 的通讯配置重建连接对象。
        /// 参数相同的端点<strong>复用原有连接</strong>（绝不打断已建立的链路），
        /// 只有端点被删除或参数改变时才销毁重建。
        /// </summary>
        public static void Reload()
        {
            EnsureInitialized();

            lock (Sync) { Rebuild(false); }

            RaiseChanged();
        }

        /// <summary>全部通讯名称（按配置顺序）。</summary>
        public static IReadOnlyList<string> Names()
        {
            EnsureInitialized();

            lock (Sync) { return new List<string>(Order); }
        }

        /// <summary>取某条通讯的配置副本；未找到返回 null（改配置请走 ConfigStore）。</summary>
        public static CommEndpointConfig Config(string name)
        {
            EnsureInitialized();

            Channel ch = Find(name);
            return ch != null ? ch.Config.Clone() : null;
        }

        /// <summary>该通讯是否已处于可用状态（服务端指是否正在监听）。</summary>
        public static bool IsConnected(string name)
        {
            EnsureInitialized();

            Channel ch = Find(name);
            return ch != null && ch.IsConnected;
        }

        /// <summary>界面 / 日志用的状态文本（已连接 / 监听中 N 客户端 / 未连接 / 已停用 / 不存在）。</summary>
        public static string StatusText(string name)
        {
            EnsureInitialized();

            Channel ch = Find(name);
            if (ch == null) return "不存在";
            if (!ch.Config.Enabled) return "已停用";

            if (!ch.IsConnected) return ch.Config.Kind == CommKind.TcpServer ? "未启动" : "未连接";

            if (ch.Config.Kind == CommKind.TcpServer)
                return "监听中（" + ch.Server.ClientCount + " 客户端）";

            return "已连接";
        }

        /// <summary>连接（服务端即启动监听）指定的一条通讯。手动语义：立即尝试，不受自动重连冷却限制。</summary>
        public static OpResult Connect(string name)
        {
            EnsureInitialized();

            Channel ch = Find(name);
            if (ch == null) return OpResult.Fail("没有名为「{0}」的通讯", name ?? "");
            if (!ch.Config.Enabled) return OpResult.Fail("通讯「{0}」已停用", ch.Config.Name);

            return ConnectChannel(ch);
        }

        /// <summary>
        /// 自动连接（供后台轮询 / 心跳 / 流程节点使用）：带失败冷却。
        /// <para>
        /// 【为什么单独一个入口】"没连上就自动连"这件事在边沿轮询、心跳、流程节点里每秒发生多次；
        /// 直接复用 <see cref="Connect"/> 会对不可达地址形成连接风暴（每次都要等满 TCP 超时），
        /// 且历史上连接是在全局锁内做的，风暴期间 UI 查通讯状态全部被锁卡住 —— 这正是
        /// "加载通讯方案后所有界面切换都卡顿"的根因。冷却让重试频率降到约 1 次 / 5 秒。
        /// </para>
        /// </summary>
        public static OpResult ConnectAuto(string name)
        {
            EnsureInitialized();

            // 闸门：软件不在运行态时不自动连接（连接统一由主界面「运行」触发）
            if (!CommRuntime.IsActive)
                return OpResult.Fail("软件未处于运行状态，暂不自动连接（点主界面「运行」后才会建立通讯）");

            Channel ch = Find(name);
            if (ch == null) return OpResult.Fail("没有名为「{0}」的通讯", name ?? "");
            if (!ch.Config.Enabled) return OpResult.Fail("通讯「{0}」已停用", ch.Config.Name);
            if (ch.IsConnected) return OpResult.Ok();

            DateTime now = DateTime.Now;
            lock (Sync)
            {
                if (now < ch.NextAutoConnect)
                    return OpResult.Fail("通讯「" + ch.Config.Name + "」自动连接冷却中（"
                                         + (int)(ch.NextAutoConnect - now).TotalMilliseconds + "ms 后重试）");
            }

            return ConnectChannel(ch);
        }

        /// <summary>断开（服务端即停止监听）指定的一条通讯。</summary>
        public static void Disconnect(string name)
        {
            EnsureInitialized();

            Channel ch = Find(name);
            if (ch == null) return;

            lock (Sync)
            {
                if (ch.Server != null) ch.Server.Stop();
                else if (ch.Modbus != null) ch.Modbus.Disconnect();
                else if (ch.Client != null) ch.Client.Disconnect();

                UpdateCommState();
            }

            RaiseChanged();
        }

        /// <summary>
        /// 发送文本：TCP 客户端走 Send，TCP 服务端走 Broadcast（广播给所有已接入客户端）。
        /// Modbus 不是文本协议，返回失败。
        /// </summary>
        public static OpResult SendText(string name, string text)
        {
            EnsureInitialized();

            Channel ch = Find(name);
            if (ch == null) return OpResult.Fail("没有名为「{0}」的通讯", name ?? "");
            if (!ch.Config.Enabled) return OpResult.Fail("通讯「{0}」已停用", ch.Config.Name);

            if (ch.Server != null) return ch.Server.Broadcast(text);
            if (ch.Modbus != null) return OpResult.Fail("通讯「{0}」是 Modbus TCP，不支持文本发送", ch.Config.Name);
            if (ch.Client != null) return ch.Client.Send(text);
            return OpResult.Fail("通讯「{0}」通道未创建", ch.Config.Name);
        }

        /// <summary>最近收到的一条文本；从未收到返回空串。</summary>
        public static string LastReceived(string name)
        {
            EnsureInitialized();

            Channel ch = Find(name);
            if (ch == null) return "";

            lock (Sync) { return ch.LastReceived ?? ""; }
        }

        /// <summary>Modbus 读保持寄存器（FC03）；未连接时会自动按配置连接一次。</summary>
        public static OpResult<ushort[]> ModbusRead(string name, ushort address, ushort count)
        {
            EnsureInitialized();

            Channel ch = Find(name);
            if (ch == null) return OpResult<ushort[]>.Fail("没有名为「" + (name ?? "") + "」的通讯");
            if (ch.Config.Kind != CommKind.ModbusTcp)
                return OpResult<ushort[]>.Fail("通讯「" + ch.Config.Name + "」不是 Modbus TCP");

            if (!EnsureModbusConnected(name, ch))
                return OpResult<ushort[]>.Fail("通讯「" + ch.Config.Name + "」未连接");

            return ch.Modbus.ReadHoldingRegisters(address, count);
        }

        /// <summary>Modbus 写单个寄存器（FC06）；未连接时会自动按配置连接一次。</summary>
        public static OpResult ModbusWrite(string name, ushort address, ushort value)
        {
            EnsureInitialized();

            Channel ch = Find(name);
            if (ch == null) return OpResult.Fail("没有名为「" + (name ?? "") + "」的通讯");
            if (ch.Config.Kind != CommKind.ModbusTcp)
                return OpResult.Fail("通讯「" + ch.Config.Name + "」不是 Modbus TCP");

            if (!EnsureModbusConnected(name, ch))
                return OpResult.Fail("通讯「" + ch.Config.Name + "」未连接");

            return ch.Modbus.WriteSingleRegister(address, value);
        }

        // ==================== Modbus 多数据类型读写（e.g. 12.5 / hello / true） ====================

        /// <summary>
        /// 按数据类型读保持寄存器并解码成可显示字符串（<see cref="ModbusTcpClient.ReadAs"/> 的转发）。
        /// 未连接时会自动按配置连接一次。
        /// </summary>
        /// <param name="name">通讯名称（见 <see cref="Names"/>）。</param>
        /// <param name="type">目标数据类型（Int16/UInt16/Int32/UInt32/Float32/Float64/String/Bool）。</param>
        /// <param name="address">起始寄存器地址。</param>
        /// <param name="length">String 时为字符数；其余类型为寄存器个数（≤0 表示只取单个值）。</param>
        /// <param name="order">字序，默认 ABCD。</param>
        /// <returns>成功带可显示字符串；失败消息可直接显示给用户。</returns>
        public static OpResult<string> ModbusReadAs(
            string name, ModbusDataType type, ushort address, int length,
            ModbusWordOrder order = ModbusWordOrder.ABCD)
        {
            EnsureInitialized();

            Channel ch = Find(name);
            if (ch == null) return OpResult<string>.Fail("没有名为「" + (name ?? "") + "」的通讯");
            if (ch.Config.Kind != CommKind.ModbusTcp)
                return OpResult<string>.Fail("通讯「" + ch.Config.Name + "」不是 Modbus TCP");

            if (!EnsureModbusConnected(name, ch))
                return OpResult<string>.Fail("通讯「" + ch.Config.Name + "」未连接");

            return ch.Modbus.ReadAs(type, address, length, order);
        }

        /// <summary>
        /// 把文本按数据类型编码后写入保持寄存器（<see cref="ModbusTcpClient.WriteAs"/> 的转发）。
        /// 未连接时会自动按配置连接一次。
        /// </summary>
        /// <param name="name">通讯名称（见 <see cref="Names"/>）。</param>
        /// <param name="type">目标数据类型。</param>
        /// <param name="address">起始寄存器地址。</param>
        /// <param name="text">用户输入文本，编码规则见 <see cref="ModbusTcpClient.WriteAs"/>。</param>
        /// <param name="order">字序，默认 ABCD。</param>
        /// <returns>失败消息可直接显示给用户。</returns>
        public static OpResult ModbusWriteAs(
            string name, ModbusDataType type, ushort address, string text,
            ModbusWordOrder order = ModbusWordOrder.ABCD)
        {
            EnsureInitialized();

            Channel ch = Find(name);
            if (ch == null) return OpResult.Fail("没有名为「" + (name ?? "") + "」的通讯");
            if (ch.Config.Kind != CommKind.ModbusTcp)
                return OpResult.Fail("通讯「" + ch.Config.Name + "」不是 Modbus TCP");

            if (!EnsureModbusConnected(name, ch))
                return OpResult.Fail("通讯「" + ch.Config.Name + "」未连接");

            return ch.Modbus.WriteAs(type, address, text, order);
        }

        /// <summary>程序退出时断开全部通讯（不写配置、不抛事件给已销毁的订阅者）。</summary>
        public static void Shutdown()
        {
            if (!_initialized) return;

            lock (Sync)
            {
                foreach (Channel ch in Channels.Values) ch.Dispose();
                Channels.Clear();
                Order.Clear();
                UpdateCommState();
            }
        }

        // ==================== 内部实现 ====================

        /// <summary>首次使用时订阅配置变更并完成一次重建（<see cref="CommState"/> 之外的唯一入口）。</summary>
        private static void EnsureInitialized()
        {
            if (_initialized) return;

            lock (Sync)
            {
                if (_initialized) return;

                // 配置一保存就自动重建 —— 这正是「通讯改完同步给流程图」的现成钩子
                ConfigStore.Changed += OnConfigStoreChanged;
                _initialized = true;

                Rebuild(true);   // 只有首次（= 软件启动）才执行 AutoConnect
            }
        }

        private static void OnConfigStoreChanged(object sender, EventArgs e)
        {
            Reload();
        }

        /// <summary>按配置重建：复用、新建、销毁。</summary>
        /// <param name="allowAutoConnect">是否执行 AutoConnect（仅首次为 true）。</param>
        private static void Rebuild(bool allowAutoConnect)
        {
            CommSettings settings = ConfigStore.Current.Comm;

            HashSet<string> keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> newOrder = new List<string>();

            if (settings != null && settings.Endpoints != null)
            {
                foreach (CommEndpointConfig cfg in settings.Endpoints)
                {
                    if (cfg == null) continue;

                    cfg.Normalize();
                    if (cfg.Name.Length == 0) continue;
                    if (keep.Contains(cfg.Name)) continue;   // 重名配置只保留第一条

                    // 未知协议（OPC UA 等）由 CommHub 的协议插件管理，旧注册表不能按 TCP 去连，
                    // 否则插件端点会被静默当成 TCP 客户端反复重连。
                    if (!CommKind.IsBuiltIn(cfg.Kind)) continue;

                    keep.Add(cfg.Name);
                    newOrder.Add(cfg.Name);

                    Channel ch;
                    if (Channels.TryGetValue(cfg.Name, out ch) && SameTarget(ch.Config, cfg))
                    {
                        ch.Config = cfg.Clone();   // 参数没变 → 复用连接，但只保存快照
                        continue;
                    }

                    if (ch != null) { ch.Dispose(); Channels.Remove(cfg.Name); }

                    ch = new Channel(cfg);
                    Channels[cfg.Name] = ch;

                    // AutoConnect 只在「首次初始化 + 处于运行态」时执行：
                    // 软件刚打开时（未点运行）不该偷偷连 PLC / 起监听，连接统一由 CommRuntime 触发。
                    if (allowAutoConnect && CommRuntime.IsActive && cfg.Enabled && cfg.AutoConnect)
                        ConnectInternal(ch);
                }
            }

            List<string> dead = new List<string>();
            foreach (string key in Channels.Keys)
            {
                if (!keep.Contains(key)) dead.Add(key);
            }
            foreach (string key in dead)
            {
                Channels[key].Dispose();
                Channels.Remove(key);
            }

            Order.Clear();
            Order.AddRange(newOrder);

            UpdateCommState();
        }

        /// <summary>决定能否复用连接的四个参数（名字已在字典里比对过）。</summary>
        private static bool SameTarget(CommEndpointConfig a, CommEndpointConfig b)
        {
            if (a == null || b == null) return false;
            return a.Kind == b.Kind && a.Host == b.Host && a.Port == b.Port && a.SlaveId == b.SlaveId;
        }

        private static Channel Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            lock (Sync)
            {
                Channel ch;
                return Channels.TryGetValue(name, out ch) ? ch : null;
            }
        }

        /// <summary>
        /// 真正执行连接：<strong>网络 I/O 在全局锁外</strong>，用通道自身的 Interlocked 标志防并发。
        /// <para>
        /// 【为什么不能在锁里连】TCP/Modbus 连不上要等满超时（秒级）。若持 <see cref="Sync"/> 连接，
        /// 这几秒内 UI 的 Names() / StatusText() / IsConnected()（开窗体、开参数面板都要调）全部排队，
        /// 表现就是"界面打开切换卡顿"。锁只保护字典查找与状态汇总，绝不跨越网络调用。
        /// </para>
        /// </summary>
        private static OpResult ConnectChannel(Channel ch)
        {
            if (Interlocked.CompareExchange(ref ch.Connecting, 1, 0) != 0)
                return OpResult.Fail("通讯「" + ch.Config.Name + "」正在连接中");

            OpResult r;
            try
            {
                r = ConnectInternal(ch);
            }
            finally
            {
                Interlocked.Exchange(ref ch.Connecting, 0);
            }

            if (!r.Success)
            {
                // 失败才进冷却；成功则清掉（掉线恢复后下一次自动连接立刻放行）
                lock (Sync) { ch.NextAutoConnect = DateTime.Now.AddMilliseconds(AutoConnectBackoffMs); }
            }
            else
            {
                lock (Sync) { ch.NextAutoConnect = DateTime.MinValue; }
            }

            RaiseChanged();
            return r;
        }

        private static OpResult ConnectInternal(Channel ch)
        {
            CommEndpointConfig cfg = ch.Config;
            OpResult r;

            // 按通道实际持有的对象分发：配置快照与对象类型永远一致，
            // 避免“配置已改成服务端、通道还是客户端”时走错分支。
            if (ch.Server != null)
            {
                r = ch.Server.Start(cfg.Port);
            }
            else if (ch.Modbus != null)
            {
                ch.Modbus.SlaveId = (byte)cfg.SlaveId;
                r = ch.Modbus.Connect(cfg.Host, cfg.Port);
            }
            else if (ch.Client != null)
            {
                r = ch.Client.Connect(cfg.Host, cfg.Port);
            }
            else
            {
                return OpResult.Fail("通讯「" + cfg.Name + "」通道未创建");
            }

            if (r.Success) CommState.ClearError();
            else CommState.SetError(cfg.Name + "：" + r.Message);

            UpdateCommState();
            return r;
        }

        private static bool EnsureModbusConnected(string name, Channel ch)
        {
            if (ch.Modbus != null && ch.Modbus.IsConnected) return true;

            // 自动路径：带冷却。ModbusRead/WriteAs 由后台轮询与流程节点高频调用，
            // 不能每次都对不可达地址做一次秒级连接。
            OpResult r = ConnectAuto(name);
            return r.Success;
        }

        /// <summary>
        /// 把「有没有连上」汇总回 <see cref="CommState"/> 的三个 bool：
        /// 任意一条对应类型的通讯连上即为 true。
        /// 这样 MainForm 的运行前校验与 SettingsForm 的状态文本无需任何改动。
        /// </summary>
        /// <remarks>自己加锁（C# lock 可重入，既有的锁内调用点不受影响）——
        /// 现在 <see cref="ConnectInternal"/> 在全局锁外执行，也会走到这里。</remarks>
        private static void UpdateCommState()
        {
            bool tcpClient = false, tcpServer = false, modbus = false;

            lock (Sync)
            {
                foreach (Channel ch in Channels.Values)
                {
                    if (!ch.IsConnected) continue;
                    if (ch.Server != null) tcpServer = true;
                    else if (ch.Modbus != null) modbus = true;
                    else if (ch.Client != null) tcpClient = true;
                }
            }

            CommState.TcpClientConnected = tcpClient;
            CommState.TcpServerRunning = tcpServer;
            CommState.ModbusConnected = modbus;
        }

        /// <summary>把 Changed 抛给订阅者；单个订阅者抛异常不影响其它订阅者。</summary>
        private static void RaiseChanged()
        {
            EventHandler handler = Changed;
            if (handler == null) return;

            try { handler(null, EventArgs.Empty); }
            catch (Exception ex) { Logger.Warn("CommRegistry.Changed 订阅者异常：" + ex.Message); }
        }

        /// <summary>一条通讯 = 一份配置 + 一个（且仅一个）活连接对象。</summary>
        private sealed class Channel
        {
            public CommEndpointConfig Config;
            public TcpClientSession Client;
            public TcpServerSession Server;
            public ModbusTcpClient Modbus;

            /// <summary>最近收到的一条文本（后台线程写入，读取须加锁）。</summary>
            public string LastReceived = "";

            /// <summary>
            /// 连接互斥标志（Interlocked）：同一通道同时只允许一个连接动作。
            /// 连接在全局锁外执行后，靠它防止"边沿轮询 + 心跳"同时对同一通道发起连接。
            /// </summary>
            public int Connecting;

            /// <summary>自动连接冷却截止时间（<see cref="CommRegistry.AutoConnectBackoffMs"/>）。</summary>
            public DateTime NextAutoConnect = DateTime.MinValue;

            public Channel(CommEndpointConfig cfg)
            {
                // 必须存快照：配置对象会被界面就地修改（例如客户端改成服务端），
                // 若 Channel 直接引用它，Disconnect/Send 会按“新 Kind”去用“旧对象”，
                // 典型表现就是 ch.Server 为 null 的 NullReferenceException。
                Config = cfg != null ? cfg.Clone() : new CommEndpointConfig();

                switch (cfg.Kind)
                {
                    case CommKind.TcpServer:
                        Server = new TcpServerSession();
                        Server.Received += (s, t) => OnReceived(t);
                        Server.StatusChanged += (s, t) => OnStatus(t);
                        break;

                    case CommKind.ModbusTcp:
                        Modbus = new ModbusTcpClient();
                        Modbus.SlaveId = (byte)cfg.SlaveId;
                        break;

                    default:
                        Client = new TcpClientSession();
                        Client.Received += (s, t) => OnReceived(t);
                        Client.StatusChanged += (s, t) => OnStatus(t);
                        break;
                }
            }

            public bool IsConnected
            {
                get
                {
                    if (Server != null) return Server.IsRunning;
                    if (Modbus != null) return Modbus.IsConnected;
                    if (Client != null) return Client.IsConnected;
                    return false;
                }
            }

            /// <summary>收到数据：后台线程调用，先加锁写值，再抛事件（抛事件不持锁）。</summary>
            private void OnReceived(string text)
            {
                string received;
                lock (Sync) { LastReceived = text ?? ""; received = LastReceived; }

                Logger.Info("[" + Config.Name + "] 收到：" + received);
                RaiseChanged();
            }

            /// <summary>状态变化：刷新汇总状态，让界面上的状态列与主界面状态栏跟着变。</summary>
            private void OnStatus(string text)
            {
                if (!string.IsNullOrEmpty(text) && text.IndexOf("断开") >= 0)
                {
                    lock (Sync) { UpdateCommState(); }
                }

                RaiseChanged();
            }

            public void Dispose()
            {
                if (Client != null) Client.Dispose();
                if (Server != null) Server.Dispose();
                if (Modbus != null) Modbus.Dispose();
            }
        }
    }
}
