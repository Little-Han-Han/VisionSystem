using System;
using System.Collections.Generic;
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

        /// <summary>连接（服务端即启动监听）指定的一条通讯。</summary>
        public static OpResult Connect(string name)
        {
            EnsureInitialized();

            Channel ch = Find(name);
            if (ch == null) return OpResult.Fail("没有名为「{0}」的通讯", name ?? "");
            if (!ch.Config.Enabled) return OpResult.Fail("通讯「{0}」已停用", ch.Config.Name);

            OpResult r;
            lock (Sync) { r = ConnectInternal(ch); }

            RaiseChanged();
            return r;
        }

        /// <summary>断开（服务端即停止监听）指定的一条通讯。</summary>
        public static void Disconnect(string name)
        {
            EnsureInitialized();

            Channel ch = Find(name);
            if (ch == null) return;

            lock (Sync)
            {
                switch (ch.Config.Kind)
                {
                    case CommKind.TcpServer:  ch.Server.Stop();      break;
                    case CommKind.ModbusTcp:  ch.Modbus.Disconnect(); break;
                    default:                  ch.Client.Disconnect(); break;
                }

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

            switch (ch.Config.Kind)
            {
                case CommKind.TcpServer:  return ch.Server.Broadcast(text);
                case CommKind.ModbusTcp:  return OpResult.Fail("通讯「{0}」是 Modbus TCP，不支持文本发送", ch.Config.Name);
                default:                  return ch.Client.Send(text);
            }
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

                    keep.Add(cfg.Name);
                    newOrder.Add(cfg.Name);

                    Channel ch;
                    if (Channels.TryGetValue(cfg.Name, out ch) && SameTarget(ch.Config, cfg))
                    {
                        ch.Config = cfg;      // 参数没变 → 复用连接，绝不打断已建立的链路
                        continue;
                    }

                    if (ch != null) { ch.Dispose(); Channels.Remove(cfg.Name); }

                    ch = new Channel(cfg);
                    Channels[cfg.Name] = ch;

                    if (allowAutoConnect && cfg.Enabled && cfg.AutoConnect) ConnectInternal(ch);
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

        private static OpResult ConnectInternal(Channel ch)
        {
            CommEndpointConfig cfg = ch.Config;
            OpResult r;

            switch (cfg.Kind)
            {
                case CommKind.TcpServer:
                    r = ch.Server.Start(cfg.Port);
                    break;

                case CommKind.ModbusTcp:
                    ch.Modbus.SlaveId = (byte)cfg.SlaveId;
                    r = ch.Modbus.Connect(cfg.Host, cfg.Port);
                    break;

                default:
                    r = ch.Client.Connect(cfg.Host, cfg.Port);
                    break;
            }

            if (r.Success) CommState.ClearError();
            else CommState.SetError(cfg.Name + "：" + r.Message);

            UpdateCommState();
            return r;
        }

        private static bool EnsureModbusConnected(string name, Channel ch)
        {
            if (ch.Modbus != null && ch.Modbus.IsConnected) return true;

            OpResult r = Connect(name);
            return r.Success;
        }

        /// <summary>
        /// 把「有没有连上」汇总回 <see cref="CommState"/> 的三个 bool：
        /// 任意一条对应类型的通讯连上即为 true。
        /// 这样 MainForm 的运行前校验与 SettingsForm 的状态文本无需任何改动。
        /// </summary>
        private static void UpdateCommState()
        {
            bool tcpClient = false, tcpServer = false, modbus = false;

            foreach (Channel ch in Channels.Values)
            {
                switch (ch.Config.Kind)
                {
                    case CommKind.TcpServer: if (ch.IsConnected) tcpServer = true; break;
                    case CommKind.ModbusTcp: if (ch.IsConnected) modbus = true;    break;
                    default:                 if (ch.IsConnected) tcpClient = true; break;
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

            public Channel(CommEndpointConfig cfg)
            {
                Config = cfg;

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
                    switch (Config.Kind)
                    {
                        case CommKind.TcpServer: return Server != null && Server.IsRunning;
                        case CommKind.ModbusTcp: return Modbus != null && Modbus.IsConnected;
                        default:                 return Client != null && Client.IsConnected;
                    }
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
