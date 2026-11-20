using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using VisionPlatform.Core;
using VisionPlatform.Framework.Communication;

namespace VisionPlatform.Communication
{
    /// <summary>
    /// 通讯统一门面：点位读写、消息发送、等待信号、状态查询。
    /// <para>
    /// 【与旧 CommRegistry 的关系】内置 TCP / Modbus 仍由 <see cref="CommRegistry"/> 持有连接，
    /// 本类在其上做点位映射与发送队列；未知协议（OPC UA 等）由 <c>Communication\*.dll</c>
    /// 提供的 <see cref="ICommChannel"/> 承载。旧 API 与旧节点完全不受影响。
    /// </para>
    /// <para>【线程】所有方法可在流程线程 / 任务线程 / UI 线程调用；每个端点一条发送队列。</para>
    /// </summary>
    public static class CommHub
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, Runtime> Endpoints =
            new Dictionary<string, Runtime>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> Order = new List<string>();

        private static readonly Dictionary<string, CommPointConfig> Points =
            new Dictionary<string, CommPointConfig>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, CommMessageConfig> Messages =
            new Dictionary<string, CommMessageConfig>(StringComparer.OrdinalIgnoreCase);

        private static bool _initialized;

        /// <summary>自动连接失败后的冷却截止时间（端点名 → 时间），见 <see cref="AutoConnect"/>。</summary>
        private static readonly Dictionary<string, DateTime> AutoConnectBlock =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        /// <summary>自动连接失败后的冷却时长（毫秒）。与 CommRegistry 的自动重连冷却同数量级。</summary>
        private const int AutoConnectBackoffMs = 5000;

        /// <summary>配置 / 连接状态 / 点位变化时触发（后台线程，UI 自行 Invoke）。</summary>
        public static event EventHandler Changed;

        // =====================================================================
        //  生命周期
        // =====================================================================

        public static void Initialize()
        {
            EnsureInitialized();
        }

        public static void Reload()
        {
            EnsureInitialized();

            lock (Sync) { Rebuild(false); }
            RaiseChanged();
        }

        public static void Shutdown()
        {
            if (!_initialized) return;

            lock (Sync)
            {
                foreach (Runtime rt in Endpoints.Values) rt.Dispose();
                Endpoints.Clear();
                Order.Clear();
                Points.Clear();
                Messages.Clear();
                try { ConfigStore.Changed -= OnConfigChanged; } catch (Exception) { }
                _initialized = false;
            }
        }

        /// <summary>全部端点名称（含插件协议端点），按配置顺序。</summary>
        public static IReadOnlyList<string> Names()
        {
            EnsureInitialized();
            lock (Sync) { return new List<string>(Order); }
        }

        public static CommEndpointConfig Config(string name)
        {
            EnsureInitialized();
            Runtime rt = Find(name);
            return rt != null ? rt.Config.Clone() : null;
        }

        public static bool IsConnected(string name)
        {
            EnsureInitialized();
            Runtime rt = Find(name);
            if (rt == null) return false;
            if (rt.BuiltIn) return CommRegistry.IsConnected(name);
            return rt.Plugin != null && rt.Plugin.IsConnected;
        }

        public static string StatusText(string name)
        {
            EnsureInitialized();
            Runtime rt = Find(name);
            if (rt == null) return "不存在";
            if (!rt.Config.Enabled) return "已停用";

            if (rt.BuiltIn) return CommRegistry.StatusText(name);
            if (rt.Plugin == null) return "协议插件未安装（" + rt.Config.Kind + "）";
            return rt.Plugin.IsConnected ? "已连接" : (rt.Plugin.StatusText ?? "未连接");
        }

        public static OpResult Connect(string name)
        {
            EnsureInitialized();
            Runtime rt = Find(name);
            if (rt == null) return OpResult.Fail("没有名为「{0}」的通讯", name ?? "");
            if (!rt.Config.Enabled) return OpResult.Fail("通讯「{0}」已停用", rt.Config.Name);

            OpResult r;
            if (rt.BuiltIn)
            {
                r = CommRegistry.Connect(name);
            }
            else if (rt.Plugin == null)
            {
                r = OpResult.Fail("通讯「{0}」的协议插件未安装（{1}）", rt.Config.Name, rt.Config.Kind);
            }
            else
            {
                CommResult cr = rt.Plugin.Connect();
                r = cr.Success ? OpResult.Ok() : OpResult.Fail(cr.Message);
            }

            RaiseChanged();
            return r;
        }

        public static void Disconnect(string name)
        {
            EnsureInitialized();
            Runtime rt = Find(name);
            if (rt == null) return;

            if (rt.BuiltIn) CommRegistry.Disconnect(name);
            else if (rt.Plugin != null) rt.Plugin.Disconnect();

            RaiseChanged();
        }

        // =====================================================================
        //  文本 / 帧发送
        // =====================================================================

        /// <summary>发送文本（同步等待入队结果）；内置 TCP 与插件均可。</summary>
        public static OpResult SendText(string name, string text, int priority = CommSendQueue.PriorityNormal)
        {
            EnsureInitialized();
            Runtime rt = Find(name);
            if (rt == null) return OpResult.Fail("没有名为「{0}」的通讯", name ?? "");
            if (!rt.Config.Enabled) return OpResult.Fail("通讯「{0}」已停用", rt.Config.Name);
            if (!IsConnected(name))
            {
                // 自动路径：带冷却（这里由发送队列 / 心跳 / 任务线程高频到达）
                OpResult cr = AutoConnect(rt);
                if (!cr.Success) return cr;
            }

            if (rt.BuiltIn)
            {
                return ToOp(rt.Queue.Run(
                    delegate { return ToComm(CommRegistry.SendText(name, text)); },
                    priority, TimeoutOf(rt)));
            }

            if (rt.Plugin == null)
                return OpResult.Fail("通讯「{0}」的协议插件未安装（{1}）", rt.Config.Name, rt.Config.Kind);

            var frame = CommFrame.FromText(text);
            return ToOp(rt.Queue.Run(delegate { return rt.Plugin.Send(frame); }, priority, TimeoutOf(rt)));
        }

        /// <summary>发送一帧原始数据（插件协议用；内置 TCP 只支持文本）。</summary>
        public static OpResult SendFrame(string name, CommFrame frame, int priority = CommSendQueue.PriorityNormal)
        {
            EnsureInitialized();
            Runtime rt = Find(name);
            if (rt == null) return OpResult.Fail("没有名为「{0}」的通讯", name ?? "");

            if (!rt.Config.Enabled) return OpResult.Fail("通讯「{0}」已停用", rt.Config.Name);
            if (!IsConnected(name))
            {
                OpResult cr = AutoConnect(rt);
                if (!cr.Success) return cr;
            }

            if (rt.BuiltIn)
            {
                if (frame == null || !frame.IsText)
                    return OpResult.Fail("通讯「{0}」是内置 TCP/Modbus，只支持文本帧", rt.Config.Name);
                return SendText(name, frame.Text, priority);
            }

            if (rt.Plugin == null)
                return OpResult.Fail("通讯「{0}」的协议插件未安装（{1}）", rt.Config.Name, rt.Config.Kind);

            return ToOp(rt.Queue.Run(delegate { return rt.Plugin.Send(frame); }, priority, TimeoutOf(rt)));
        }

        /// <summary>最近收到的一条文本（内置 TCP）；插件协议请用点位读取。</summary>
        public static string LastReceived(string name)
        {
            EnsureInitialized();
            Runtime rt = Find(name);
            if (rt == null) return "";
            if (rt.BuiltIn) return CommRegistry.LastReceived(name);
            return rt.LastText ?? "";
        }

        // =====================================================================
        //  点位
        // =====================================================================

        public static IReadOnlyList<string> PointNames()
        {
            EnsureInitialized();
            lock (Sync) { return new List<string>(Points.Keys); }
        }

        public static CommPointConfig PointConfig(string name)
        {
            EnsureInitialized();
            lock (Sync)
            {
                CommPointConfig p;
                return Points.TryGetValue(name ?? "", out p) ? p.Clone() : null;
            }
        }

        public static OpResult<CommValue> ReadPoint(string pointName)
        {
            EnsureInitialized();

            CommPointConfig pc;
            lock (Sync) { Points.TryGetValue(pointName ?? "", out pc); }
            if (pc == null) return OpResult<CommValue>.Fail("没有名为「" + (pointName ?? "") + "」的点位");

            return ReadPointCore(pc);
        }

        /// <summary>
        /// 按地址偏移读同一个点位（多流程分区用）。
        /// <para>【为什么需要】产线上多条流程共用一套点位定义，只是寄存器区段不同：
        /// 第 i 条流程 = <c>基址 + i × 步长</c>。没有这个入口就得给每条流程复制一份点位，
        /// 改一次地址要改 N 处，现场必然漏改。</para>
        /// <para>只支持 Modbus 保持寄存器（TCP 文本链路没有"地址"概念）。</para>
        /// </summary>
        public static OpResult<CommValue> ReadPointAt(string pointName, int addressOffset)
        {
            EnsureInitialized();

            CommPointConfig pc;
            lock (Sync) { Points.TryGetValue(pointName ?? "", out pc); }
            if (pc == null) return OpResult<CommValue>.Fail("没有名为「" + (pointName ?? "") + "」的点位");

            if (addressOffset == 0) return ReadPointCore(pc);

            ushort baseAddr;
            string area, error;
            if (!CommPointMapper.TryParseModbusAddress(pc.Address, out baseAddr, out area, out error))
                return OpResult<CommValue>.Fail("点位「" + pc.Name + "」地址无法偏移：" + error);

            int shifted = baseAddr + addressOffset;
            if (shifted < 0 || shifted > 65535)
                return OpResult<CommValue>.Fail(string.Format(
                    "点位「{0}」偏移后地址越界（{1} + {2} = {3}，需在 0~65535）",
                    pc.Name, baseAddr, addressOffset, shifted));

            CommPointConfig clone = pc.Clone();
            clone.Address = area + ":" + shifted.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return ReadPointCore(clone);
        }

        private static OpResult<CommValue> ReadPointCore(CommPointConfig pc)
        {
            if (pc == null) return OpResult<CommValue>.Fail("点位不存在");
            if (CommPointConfig.NormalizeDirection(pc.Direction) == "Write")
                return OpResult<CommValue>.Fail("点位「" + pc.Name + "」是只写点位");

            Runtime rt = Find(pc.Endpoint);
            if (rt == null) return OpResult<CommValue>.Fail("点位「" + pc.Name + "」绑定的通讯「" + pc.Endpoint + "」不存在");
            if (!rt.Config.Enabled) return OpResult<CommValue>.Fail("通讯「" + rt.Config.Name + "」已停用");
            if (!IsConnected(rt.Config.Name))
            {
                // 自动路径：带冷却。ReadPoint 被边沿轮询每 100ms 调一次，绝不能每次都真连
                OpResult cr = AutoConnect(rt);
                if (!cr.Success) return OpResult<CommValue>.Fail(cr.Message);
            }

            CommPointOptions po = CommPointMapper.ToOptions(pc);
            OpResult<CommValue> result;

            if (rt.BuiltIn)
            {
                result = RunRead(rt, delegate { return ReadBuiltIn(rt, po); });
            }
            else if (rt.Plugin == null)
            {
                result = OpResult<CommValue>.Fail("通讯「" + rt.Config.Name + "」的协议插件未安装（" + rt.Config.Kind + "）");
            }
            else
            {
                result = RunRead(rt, delegate { return rt.Plugin.Read(po); });
            }

            if (!result.Success && !string.IsNullOrEmpty(pc.Default))
                return OpResult<CommValue>.Ok(CommValue.Parse(pc.Default, po.Type), "使用默认值");

            return result;
        }

        public static OpResult WritePoint(string pointName, CommValue value)
        {
            EnsureInitialized();

            CommPointConfig pc;
            lock (Sync) { Points.TryGetValue(pointName ?? "", out pc); }
            if (pc == null) return OpResult.Fail("没有名为「{0}」的点位", pointName ?? "");
            if (CommPointConfig.NormalizeDirection(pc.Direction) == "Read")
                return OpResult.Fail("点位「{0}」是只读点位", pc.Name);

            Runtime rt = Find(pc.Endpoint);
            if (rt == null) return OpResult.Fail("点位「{0}」绑定的通讯「{1}」不存在", pc.Name, pc.Endpoint);
            if (!rt.Config.Enabled) return OpResult.Fail("通讯「{0}」已停用", rt.Config.Name);
            if (!IsConnected(rt.Config.Name))
            {
                OpResult cr = AutoConnect(rt);
                if (!cr.Success) return cr;
            }

            CommPointOptions po = CommPointMapper.ToOptions(pc);

            if (rt.BuiltIn)
            {
                return ToOp(rt.Queue.Run(delegate { return WriteBuiltIn(rt, po, value); },
                                         CommSendQueue.PriorityHigh, TimeoutOf(rt)));
            }

            if (rt.Plugin == null)
                return OpResult.Fail("通讯「{0}」的协议插件未安装（{1}）", rt.Config.Name, rt.Config.Kind);

            return ToOp(rt.Queue.Run(delegate { return rt.Plugin.Write(po, value); },
                                     CommSendQueue.PriorityHigh, TimeoutOf(rt)));
        }

        public static OpResult SetPoint(string pointName, string value)
        {
            CommPointConfig pc = PointConfig(pointName);
            if (pc == null) return OpResult.Fail("没有名为「{0}」的点位", pointName ?? "");
            return WritePoint(pointName, CommValue.Parse(value, CommPointMapper.ParseType(pc.Type)));
        }

        /// <summary>
        /// 等待点位满足期望值。expected 为空表示"读到任意值即成功"；
        /// String 类型用包含匹配（适合 <c>ACK</c> 这类回复），其余类型精确匹配。
        /// </summary>
        public static OpResult WaitPoint(string pointName, string expected, int timeoutMs)
        {
            CommPointConfig pc = PointConfig(pointName);
            if (pc == null) return OpResult.Fail("没有名为「{0}」的点位", pointName ?? "");

            var sw = Stopwatch.StartNew();
            int timeout = timeoutMs > 0 ? timeoutMs : 1000;
            CommValue last = null;

            while (sw.ElapsedMilliseconds <= timeout)
            {
                OpResult<CommValue> r = ReadPoint(pointName);
                if (r.Success && r.Value != null)
                {
                    last = r.Value;
                    if (Matches(pc, r.Value, expected)) return OpResult.Ok();
                }

                Thread.Sleep(20);
            }

            return OpResult.Fail("等待点位「{0}」超时（期望 {1}，实际 {2}）",
                pointName, expected ?? "任意", last != null ? last.ToText() : "无数据");
        }

        // =====================================================================
        //  消息
        // =====================================================================

        public static IReadOnlyList<string> MessageNames()
        {
            EnsureInitialized();
            lock (Sync) { return new List<string>(Messages.Keys); }
        }

        /// <summary>按模板发送消息；context 提供 {Flow.Passed} / {Time} 等占位符。</summary>
        public static OpResult SendMessage(string messageName, IDictionary<string, string> context)
        {
            EnsureInitialized();

            CommMessageConfig msg;
            lock (Sync) { Messages.TryGetValue(messageName ?? "", out msg); }
            if (msg == null) return OpResult.Fail("没有名为「{0}」的消息模板", messageName ?? "");
            if (string.IsNullOrEmpty(msg.Endpoint)) return OpResult.Fail("消息「{0}」未绑定通讯端点", msg.Name);

            string body = CommTemplate.Render(msg.Format, context);

            if (string.Equals(msg.Mode, "Points", StringComparison.OrdinalIgnoreCase))
                return WriteMessagePoints(body, context);

            body += CommTemplate.Unescape(msg.Terminator);
            return SendText(msg.Endpoint, body, CommSendQueue.PriorityHigh);
        }

        private static OpResult WriteMessagePoints(string body, IDictionary<string, string> context)
        {
            if (string.IsNullOrEmpty(body)) return OpResult.Fail("消息内容为空");

            string[] parts = body.Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                string s = part.Trim();
                if (s.Length == 0) continue;

                int eq = s.IndexOf('=');
                if (eq <= 0) return OpResult.Fail("点位写入格式错误：" + s + "（应为 Point=value）");

                string name = s.Substring(0, eq).Trim();
                string value = s.Substring(eq + 1).Trim();
                value = CommTemplate.Render(value, context);

                OpResult r = SetPoint(name, value);
                if (!r.Success) return r;
            }

            return OpResult.Ok();
        }

        /// <summary>把"队列的 CommResult + 本地捕获的泛型值"合成带值结果（队列本身只认非泛型结果）。</summary>
        private static OpResult<CommValue> RunRead(Runtime rt, Func<CommResult<CommValue>> read)
        {
            CommValue value = null;
            string message = "";

            CommResult r = rt.Queue.Run(delegate
            {
                CommResult<CommValue> rr = read();
                message = rr.Message;
                if (rr.Success) value = rr.Value;
                return rr.Success ? CommResult.Ok(message) : CommResult.Fail(message);
            }, CommSendQueue.PriorityNormal, TimeoutOf(rt));

            if (r.Success) return OpResult<CommValue>.Ok(value, message);
            return OpResult<CommValue>.Fail(r.Message);
        }

        // =====================================================================
        //  内置协议的点位读写
        // =====================================================================

        private static CommResult<CommValue> ReadBuiltIn(Runtime rt, CommPointOptions po)
        {
            string kind = rt.Config.Kind;

            if (kind == CommKind.ModbusTcp)
            {
                ushort addr;
                string area, error;
                if (!CommPointMapper.TryParseModbusAddress(po.Address, out addr, out area, out error))
                    return CommResult<CommValue>.Fail(error);

                if (area != "HR" && area != "IR")
                    return CommResult<CommValue>.Fail("当前 Modbus 驱动只支持 HR / IR 寄存器（地址：" + po.Address + "）");

                ModbusDataType type = po.Type == CommPointType.Bool
                    ? ModbusDataType.UInt16
                    : CommPointMapper.ToModbusType(po.Type);

                int length = po.Type == CommPointType.String ? Math.Max(1, po.Length) : 0;
                OpResult<string> r = CommRegistry.ModbusReadAs(
                    rt.Config.Name, type, addr, length, CommPointMapper.ParseWordOrder(po.WordOrder));

                if (!r.Success) return CommResult<CommValue>.Fail(r.Message);

                if (po.Type == CommPointType.Bool)
                {
                    double raw;
                    double.TryParse(r.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out raw);
                    return CommResult<CommValue>.Ok(CommValue.FromBool(raw != 0), "");
                }

                return CommResult<CommValue>.Ok(CommPointMapper.Decode(po, r.Value), "");
            }

            // TCP 客户端 / 服务端：点位读 = 最近一条文本
            string text = CommRegistry.LastReceived(rt.Config.Name) ?? "";
            if (string.IsNullOrEmpty(text))
                return CommResult<CommValue>.Fail("通讯「" + rt.Config.Name + "」尚未收到数据");

            if (po.Type == CommPointType.String)
                return CommResult<CommValue>.Ok(CommValue.FromText(text), "");

            return CommResult<CommValue>.Ok(CommPointMapper.Decode(po, text), "");
        }

        private static CommResult WriteBuiltIn(Runtime rt, CommPointOptions po, CommValue value)
        {
            if (value == null) return CommResult.Fail("写入值为空");
            string kind = rt.Config.Kind;

            if (kind == CommKind.ModbusTcp)
            {
                ushort addr;
                string area, error;
                if (!CommPointMapper.TryParseModbusAddress(po.Address, out addr, out area, out error))
                    return CommResult.Fail(error);

                if (area != "HR")
                    return CommResult.Fail("当前 Modbus 驱动只支持 HR 保持寄存器写入（地址：" + po.Address + "）");

                ModbusDataType type = po.Type == CommPointType.Bool
                    ? ModbusDataType.UInt16
                    : CommPointMapper.ToModbusType(po.Type);

                string text = CommPointMapper.Encode(po, value);
                OpResult r = CommRegistry.ModbusWriteAs(
                    rt.Config.Name, type, addr, text, CommPointMapper.ParseWordOrder(po.WordOrder));

                return r.Success ? CommResult.Ok("") : CommResult.Fail(r.Message);
            }

            OpResult sr = CommRegistry.SendText(rt.Config.Name, value.ToText());
            return sr.Success ? CommResult.Ok("") : CommResult.Fail(sr.Message);
        }

        private static bool Matches(CommPointConfig pc, CommValue actual, string expected)
        {
            if (string.IsNullOrEmpty(expected)) return true;

            CommPointType type = CommPointMapper.ParseType(pc.Type);

            if (type == CommPointType.String)
            {
                string a = actual.Text ?? "";
                return a.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            if (type == CommPointType.Bool)
            {
                bool want = expected == "1" || string.Equals(expected, "true", StringComparison.OrdinalIgnoreCase);
                return actual.AsBool() == want;
            }

            double wantNum, gotNum;
            if (!double.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out wantNum)) return false;
            gotNum = actual.AsDouble();
            return Math.Abs(wantNum - gotNum) < 1e-6;
        }

        // =====================================================================
        //  内部：端点重建 / 事件
        // =====================================================================

        private sealed class Runtime : IDisposable
        {
            public CommEndpointConfig Config;
            public bool BuiltIn;
            public ICommChannel Plugin;
            public CommSendQueue Queue;
            public string LastText = "";

            public void Dispose()
            {
                try { if (Queue != null) Queue.Dispose(); } catch (Exception) { }
                try { if (Plugin != null) Plugin.Dispose(); } catch (Exception) { }
                Queue = null;
                Plugin = null;
            }
        }

        private static void EnsureInitialized()
        {
            if (_initialized) return;

            lock (Sync)
            {
                if (_initialized) return;

                _initialized = true;
                CommProtocolRegistry.Initialize();
                ConfigStore.Changed += OnConfigChanged;

                Rebuild(true);
            }
        }

        private static void OnConfigChanged(object sender, EventArgs e)
        {
            Reload();
        }

        /// <param name="allowAutoConnect">仅首次初始化执行 AutoConnect；配置保存不打断现有连接。</param>
        private static void Rebuild(bool allowAutoConnect)
        {
            CommSettings settings = ConfigStore.Current.Comm;
            if (settings == null) return;
            CommSettings.UpgradeToCurrent(settings);

            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var newOrder = new List<string>();

            lock (Sync)
            {
                Points.Clear();
                foreach (CommPointConfig p in settings.Points)
                {
                    if (p == null || string.IsNullOrEmpty(p.Name)) continue;
                    Points[p.Name] = p;
                }

                Messages.Clear();
                foreach (CommMessageConfig m in settings.Messages)
                {
                    if (m == null || string.IsNullOrEmpty(m.Name)) continue;
                    Messages[m.Name] = m;
                }

                foreach (CommEndpointConfig cfg in settings.Endpoints)
                {
                    if (cfg == null) continue;
                    cfg.Normalize();
                    if (cfg.Name.Length == 0 || keep.Contains(cfg.Name)) continue;

                    keep.Add(cfg.Name);
                    newOrder.Add(cfg.Name);

                    Runtime rt;
                    if (Endpoints.TryGetValue(cfg.Name, out rt) && SameTarget(rt.Config, cfg))
                    {
                        rt.Config = cfg.Clone();
                        continue;
                    }

                    if (rt != null) { rt.Dispose(); Endpoints.Remove(cfg.Name); }

                    rt = CreateRuntime(cfg);
                    Endpoints[cfg.Name] = rt;

                    // 内置端点的 AutoConnect 由 CommRegistry 首次初始化统一处理；
                    // 这里只负责插件协议，避免同一条 TCP 被连两次。
                    // 且只在运行态才自动连：软件刚打开不该偷偷连外部设备。
                    if (allowAutoConnect && CommRuntime.IsActive && cfg.Enabled && cfg.AutoConnect && !rt.BuiltIn)
                        ConnectRuntime(rt);
                }

                var dead = new List<string>();
                foreach (string key in Endpoints.Keys)
                    if (!keep.Contains(key)) dead.Add(key);

                foreach (string key in dead)
                {
                    Endpoints[key].Dispose();
                    Endpoints.Remove(key);
                }

                Order.Clear();
                Order.AddRange(newOrder);
            }
        }

        private static Runtime CreateRuntime(CommEndpointConfig cfg)
        {
            var rt = new Runtime
            {
                Config = cfg.Clone(),
                BuiltIn = CommKind.IsBuiltIn(cfg.Kind),
                Queue = new CommSendQueue(cfg.Name)
            };

            if (!rt.BuiltIn)
            {
                ICommProtocolFactory factory = CommProtocolRegistry.Find(cfg.Kind);
                if (factory != null)
                {
                    try { rt.Plugin = factory.Create(CommPointMapper.ToOptions(cfg)); }
                    catch (Exception ex)
                    {
                        Logger.Warn("[comm] 创建协议通道失败 " + cfg.Name + "：" + ex.Message);
                    }
                }
            }

            return rt;
        }

        private static void ConnectRuntime(Runtime rt)
        {
            try
            {
                if (rt.BuiltIn) CommRegistry.Connect(rt.Config.Name);
                else if (rt.Plugin != null) rt.Plugin.Connect();
            }
            catch (Exception ex)
            {
                Logger.Warn("[comm] 自动连接失败 " + rt.Config.Name + "：" + ex.Message);
            }
        }

        private static bool SameTarget(CommEndpointConfig a, CommEndpointConfig b)
        {
            if (a == null || b == null) return false;
            return string.Equals(a.Kind, b.Kind, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase)
                   && a.Port == b.Port
                   && a.SlaveId == b.SlaveId;
        }

        private static Runtime Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            lock (Sync)
            {
                Runtime rt;
                return Endpoints.TryGetValue(name, out rt) ? rt : null;
            }
        }

        /// <summary>
        /// 自动连接（带冷却）：后台轮询 / 心跳 / 流程节点发现"未连接"时的唯一入口。
        /// <para>
        /// 【为什么需要】PLC 掉线时，边沿轮询（100ms）、心跳（1s）、结果推送都在走
        /// "未连接 → 连接"。对不可达地址每次连接都要等满超时，不加冷却就是连接风暴；
        /// 冷却期内直接快速失败，把重连频率压到约 1 次 / 5 秒。手动连接（界面按钮）不走这里。
        /// </para>
        /// </summary>
        private static OpResult AutoConnect(Runtime rt)
        {
            if (rt == null) return OpResult.Fail("通讯不存在");

            // 闸门：软件不在运行态（未点「运行」/ 已暂停 / 已停止）时不自动连接
            if (!CommRuntime.IsActive)
                return OpResult.Fail("软件未处于运行状态，暂不自动连接（点主界面「运行」后才会建立通讯）");

            string name = rt.Config.Name;

            lock (Sync)
            {
                DateTime until;
                if (AutoConnectBlock.TryGetValue(name, out until) && DateTime.Now < until)
                    return OpResult.Fail("通讯「" + name + "」自动连接冷却中（"
                                         + (int)(until - DateTime.Now).TotalMilliseconds + "ms 后重试）");
            }

            OpResult r;
            if (rt.BuiltIn)
            {
                r = CommRegistry.ConnectAuto(name);
            }
            else if (rt.Plugin == null)
            {
                r = OpResult.Fail("通讯「{0}」的协议插件未安装（{1}）", name, rt.Config.Kind);
            }
            else
            {
                CommResult cr = rt.Plugin.Connect();
                r = cr.Success ? OpResult.Ok() : OpResult.Fail(cr.Message);
            }

            lock (Sync)
            {
                if (r.Success) AutoConnectBlock.Remove(name);
                else AutoConnectBlock[name] = DateTime.Now.AddMilliseconds(AutoConnectBackoffMs);
            }

            return r;
        }

        private static int TimeoutOf(Runtime rt)
        {
            int ms = rt != null && rt.Config != null ? rt.Config.TimeoutMs : 1000;
            return ms > 0 ? ms : 1000;
        }

        private static CommResult ToComm(OpResult r)
        {
            return r.Success ? CommResult.Ok(r.Message) : CommResult.Fail(r.Message);
        }

        private static OpResult ToOp(CommResult r)
        {
            if (r == null) return OpResult.Fail("通讯失败");
            return r.Success ? OpResult.Ok() : OpResult.Fail(r.Message);
        }

        private static OpResult<CommValue> ToOpValue(CommResult<CommValue> r)
        {
            if (r == null) return OpResult<CommValue>.Fail("通讯失败");
            return r.Success ? OpResult<CommValue>.Ok(r.Value, r.Message) : OpResult<CommValue>.Fail(r.Message);
        }

        private static void RaiseChanged()
        {
            EventHandler h = Changed;
            if (h == null) return;
            try { h(null, EventArgs.Empty); }
            catch (Exception ex) { Logger.Warn("CommHub.Changed 订阅者异常：" + ex.Message); }
        }
    }
}
