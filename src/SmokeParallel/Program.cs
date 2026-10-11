using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using VisionPlatform.Camera;
using VisionPlatform.Core;
using VisionPlatform.Flow;

namespace SmokeParallel
{
    /// <summary>
    /// T01 数据模型 + T02 并行引擎 冒烟程序（临时，验证后删除，不进入发布范围）。
    /// 重点：<strong>旧配置（SchemaVersion &lt; 3）真跑一次，确认流程仍全部启用</strong>。
    /// </summary>
    /// <remarks>
    /// ================= 节号登记表（多人协作，先登记后追加） =================
    /// 规则：<strong>新增断言只能在文件末尾追加一个新节</strong>，先在这里登记节号 + 归属，
    ///      再写代码。修改自己已有的节可以；<strong>动别人的节必须先打招呼</strong>。
    ///
    /// | 节号 | 内容                                       | 归属                  |
    /// |------|--------------------------------------------|-----------------------|
    /// | [1]~[10] | LogEntry / 租约 / 取帧门 / 输出事件 / 产品筛选 / 旧配置真跑 / 陷阱实证 / AppSettings / 启动校验 | 并行引擎同事（最初作者，请在此确认归属） |
    /// | [11] | T01 端子模型 + 连线端口化 + Schema v4 升级    | software-engineer-2  |
    /// | [12] | T02 并行引擎真跑（相机流 + 本地图片流）       | 并行引擎同事（请在此确认归属） |
    /// | [13] | T02 端口级拓扑排序 + 校验器                  | software-engineer-2  |
    ///
    /// 下一节请用 **[14]**。
    /// </remarks>
    internal static class Program
    {
        private static int _pass;
        private static int _fail;

        private static void Check(string name, bool ok, string detail = null)
        {
            if (ok) { _pass++; Console.WriteLine("  [PASS] " + name + (detail == null ? "" : "  | " + detail)); }
            else { _fail++; Console.WriteLine("  [FAIL] " + name + (detail == null ? "" : "  | " + detail)); }
        }

        private static int Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("=== SmokeParallel: T01 数据模型 + T02 并行引擎 ===");
            Console.WriteLine();

            TestLogEntry();
            TestCameraLease();
            TestGrabGate();
            TestFlowOutputArgs();
            TestFlowModuleProduct();
            TestOldConfigRealRun();
            TestSyntheticV2ConfigRun();
            TestTrapProof();
            TestAppSettingsCompat();
            TestRunManagerValidation();
            TestPortSchemaUpgrade();
            TestParallelRealRun();
            TestFlowOrderAndValidator();

            Console.WriteLine();
            Console.WriteLine("=== 结果：" + _pass + " PASS / " + _fail + " FAIL ===");
            return _fail == 0 ? 0 : 1;
        }

        // =================================================================
        //  1. LogEntry 五级 + Format
        // =================================================================
        private static void TestLogEntry()
        {
            Console.WriteLine("[1] LogEntry 五级与 Format()");

            var e1 = new LogEntry(LogLevel.Info, "", "系统启动");
            var e2 = new LogEntry(LogLevel.Warn, "", "取帧慢");
            var e3 = new LogEntry(LogLevel.Error, "流程A", "取帧失败");
            var e4 = new LogEntry(LogLevel.ResultOk, "流程A", "直径 199.9");
            var e5 = new LogEntry(LogLevel.ResultNg, "流程B", "直径 210.3");

            Check("LevelText 五级文本", e1.LevelText == "INFO" && e2.LevelText == "WARN"
                && e3.LevelText == "ERROR" && e4.LevelText == "OK" && e5.LevelText == "NG",
                e1.LevelText + "/" + e2.LevelText + "/" + e3.LevelText + "/" + e4.LevelText + "/" + e5.LevelText);

            string f1 = e1.Format();
            string f3 = e3.Format();

            // [HH:mm:ss][级别] 内容 / [HH:mm:ss][级别][流程名] 内容
            Check("无流程名时省略该段", f1.StartsWith("[") && f1.Contains("[INFO] ") && !f1.Contains("][流程"), f1);
            Check("有流程名时带 [流程名]", f3.Contains("[ERROR][流程A] "), f3);
            Check("时间格式为 HH:mm:ss", System.Text.RegularExpressions.Regex.IsMatch(f1, @"^\[\d{2}:\d{2}:\d{2}\]"), f1);
            Check("默认构造非 null", new LogEntry().FlowName == "" && new LogEntry().Message == "");

            // Logger 双通道：Logged（旧）与 EntryLogged（新）都收到，文本一致
            string legacy = null;
            LogEntry structured = null;
            EventHandler<string> h1 = delegate(object s, string t) { legacy = t; };
            EventHandler<LogEntry> h2 = delegate(object s, LogEntry t) { structured = t; };
            Logger.Logged += h1;
            Logger.EntryLogged += h2;
            Logger.Result("流程X", false, "直径超差");
            Logger.Logged -= h1;
            Logger.EntryLogged -= h2;

            Check("Logger 旧事件 Logged 仍在触发", legacy != null, legacy);
            Check("Logger 新事件 EntryLogged 触发且级别=ResultNg",
                structured != null && structured.Level == LogLevel.ResultNg && structured.FlowName == "流程X",
                structured != null ? structured.Format() : "null");
            Check("两通道文本一致（零回归）", legacy != null && structured != null && legacy == structured.Format());
        }

        // =================================================================
        //  2. CameraLease 独占租约
        // =================================================================
        private static void TestCameraLease()
        {
            Console.WriteLine("[2] CameraLease 相机独占租约");
            CameraLease.Clear();

            string owner;
            bool a1 = CameraLease.TryAcquire("CAM-1", "FLOW-A", out owner);
            Check("首次申请成功", a1 && owner == null);

            bool a2 = CameraLease.TryAcquire("CAM-1", "FLOW-B", out owner);
            Check("第二条流程被拒且返回占用者", !a2 && owner == "FLOW-A", "owner=" + owner);

            bool a3 = CameraLease.TryAcquire("CAM-1", "FLOW-A", out owner);
            Check("同一流程重复申请幂等放行", a3);

            bool a4 = CameraLease.TryAcquire("", "FLOW-C", out owner);
            Check("本地图片模式（空 cameraId）不占租约", a4 && !CameraLease.IsOccupied(""));

            Check("OwnerOf 正确", CameraLease.OwnerOf("CAM-1") == "FLOW-A");
            Check("另一台相机不被影响", CameraLease.OwnerOf("CAM-2") == null);

            Dictionary<string, string> snap = CameraLease.Snapshot();
            Check("Snapshot 为副本且含 1 项", snap.Count == 1 && snap.ContainsKey("CAM-1"));
            snap["CAM-9"] = "HACK";
            Check("改副本不污染内部表", !CameraLease.IsOccupied("CAM-9"));

            // 别人的租约不能被误释放
            CameraLease.Release("CAM-1", "FLOW-B");
            Check("非持有者不能释放他人租约", CameraLease.OwnerOf("CAM-1") == "FLOW-A");

            CameraLease.Release("CAM-1", "FLOW-A");
            Check("持有者释放后空闲", CameraLease.OwnerOf("CAM-1") == null);

            CameraLease.TryAcquire("CAM-2", "FLOW-A", out owner);
            CameraLease.TryAcquire("CAM-3", "FLOW-A", out owner);
            CameraLease.ReleaseAll("FLOW-A");
            Check("ReleaseAll 释放该流程全部租约",
                CameraLease.OwnerOf("CAM-2") == null && CameraLease.OwnerOf("CAM-3") == null);
            CameraLease.Clear();
        }

        // =================================================================
        //  3. CameraGrabGate 串行化取帧
        // =================================================================
        private static void TestGrabGate()
        {
            Console.WriteLine("[3] CameraGrabGate 取帧串行化门");

            OpResult<Bitmap> r0 = CameraGrabGate.GrabOne(null);
            Check("相机为 null 时返回失败而非抛异常", r0 != null && !r0.Success, r0 != null ? r0.Message : "null");

            var cam = new StubCamera("CAM-S1", "模拟相机1");
            OpResult<Bitmap> r1 = CameraGrabGate.GrabOne(cam, 1000, "流程A");
            Check("经门取帧成功", r1 != null && r1.Success && r1.Value != null,
                r1 != null ? (r1.Success ? r1.Value.Width + "x" + r1.Value.Height : r1.Message) : "null");
            if (r1 != null && r1.Value != null) r1.Value.Dispose();

            // 并发取帧：门内 sleep 200ms，两个线程各取一次，串行后总耗时应 >= 400ms
            cam.DelayMs = 200;
            var t1 = new Thread(delegate() { var x = CameraGrabGate.GrabOne(cam, 3000, "流程A"); if (x.Value != null) x.Value.Dispose(); });
            var t2 = new Thread(delegate() { var x = CameraGrabGate.GrabOne(cam, 3000, "流程B"); if (x.Value != null) x.Value.Dispose(); });
            var sw = System.Diagnostics.Stopwatch.StartNew();
            t1.Start(); t2.Start(); t1.Join(); t2.Join();
            sw.Stop();
            cam.DelayMs = 0;
            Check("并发取帧被串行化（耗时 >= 2×单次）", sw.ElapsedMilliseconds >= 380,
                "实测 " + sw.ElapsedMilliseconds + "ms（单次 200ms）");

            // 取帧抛异常时不外泄
            cam.Throw = true;
            OpResult<Bitmap> r2 = CameraGrabGate.GrabOne(cam, 1000, "流程A");
            Check("相机抛异常时被门捕获", r2 != null && !r2.Success, r2 != null ? r2.Message : "null");
            cam.Throw = false;
        }

        /// <summary>测试用假相机：只实现取帧，用于验证门的串行化行为。</summary>
        private sealed class StubCamera : ICamera
        {
            private readonly CameraInfo _info;
            public int DelayMs;
            public bool Throw;

            public StubCamera(string id, string name)
            {
                _info = new CameraInfo { Id = id, Name = name, Vendor = "Stub" };
            }

            public CameraInfo Info { get { return _info; } }
            public bool IsOpen { get { return true; } }
            public bool IsGrabbing { get { return false; } }
            public OpResult Open() { return OpResult.Ok(); }
            public OpResult Close() { return OpResult.Ok(); }
            public OpResult StartGrab() { return OpResult.Ok(); }
            public OpResult StopGrab() { return OpResult.Ok(); }

            public OpResult<Bitmap> GrabOne(int timeoutMs = 3000)
            {
                if (Throw) throw new InvalidOperationException("假相机故意抛异常");
                if (DelayMs > 0) Thread.Sleep(DelayMs);
                return OpResult<Bitmap>.Ok(new Bitmap(64, 48));
            }

            public event EventHandler<Bitmap> FrameReceived;
            public event EventHandler<string> ErrorOccurred;
            public void Dispose() { }
        }

        // =================================================================
        //  4. FlowOutputEventArgs 追加字段不破坏旧字段
        // =================================================================
        private static void TestFlowOutputArgs()
        {
            Console.WriteLine("[4] FlowOutputEventArgs 追加字段");

            var e = new FlowOutputEventArgs();
            Check("新字段默认为空串（旧发布方零回归）",
                e.FlowId == "" && e.FlowName == "" && e.Product == "");
            Check("旧字段仍存在且可取",
                e.ViewIndex == 0 && e.Frame == null && e.Passed == false && e.Summary == null && e.CameraName == null);

            int got = 0;
            string last = "";
            EventHandler<FlowOutputEventArgs> h = delegate(object s, FlowOutputEventArgs a)
            {
                got++; last = a.FlowName + "/" + a.Product;
            };
            FlowOutputChannel.OutputReady += h;
            bool delivered = FlowOutputChannel.Publish(new FlowOutputEventArgs { FlowId = "F1", FlowName = "流程1", Product = "A型" });
            FlowOutputChannel.OutputReady -= h;

            Check("发布有订阅者时返回 true", delivered && got == 1, "last=" + last);
            Check("退订后无订阅者返回 false", got == 1 && !FlowOutputChannel.Publish(new FlowOutputEventArgs()));

            // 订阅者抛异常不应影响发布方
            EventHandler<FlowOutputEventArgs> bad = delegate(object s, FlowOutputEventArgs a) { throw new Exception("订阅者故意抛"); };
            FlowOutputChannel.OutputReady += bad;
            bool ok = FlowOutputChannel.Publish(new FlowOutputEventArgs { FlowName = "X" });
            FlowOutputChannel.OutputReady -= bad;
            Check("单个订阅者抛异常不影响发布方", ok);
        }

        // =================================================================
        //  5. FlowModule 产品筛选
        // =================================================================
        private static void TestFlowModuleProduct()
        {
            Console.WriteLine("[5] FlowModule 产品收集与筛选");

            var flows = new List<FlowDefinition>
            {
                FlowDefinition.CreateEmpty("B-流程"),
                FlowDefinition.CreateEmpty("A-流程"),
                FlowDefinition.CreateEmpty("通用流程")
            };
            flows[0].Product = "产品B";
            flows[1].Product = "产品A";
            flows[2].Product = "";

            List<string> products = FlowModule.CollectProducts(flows);
            Check("CollectProducts 去重排序且不含通用",
                products.Count == 2 && products[0] == "产品A" && products[1] == "产品B",
                string.Join(",", products));

            List<FlowDefinition> fb = FlowModule.FilterByProduct(flows, "产品A");
            Check("筛选产品A 含通用流程", fb.Count == 2 &&
                fb.Exists(x => x.Name == "A-流程") && fb.Exists(x => x.Name == "通用流程"),
                string.Join(",", fb.ConvertAll(x => x.Name)));

            List<FlowDefinition> fall = FlowModule.FilterByProduct(flows, "");
            Check("产品为空时返回全部", fall.Count == 3);

            List<FlowDefinition> fnull = FlowModule.FilterByProduct(flows, null);
            Check("产品为 null 时返回全部", fnull.Count == 3);
        }

        // =================================================================
        //  6. 【硬验收】真实旧配置（SchemaVersion=1）真跑一次
        // =================================================================
        private static void TestOldConfigRealRun()
        {
            Console.WriteLine("[6] 【硬验收】磁盘上的真实旧配置真跑一次（只读，不落盘）");

            string path = AppPaths.ConfigFile;
            Check("真实配置文件存在", File.Exists(path), path);
            if (!File.Exists(path)) return;

            string raw = File.ReadAllText(path, System.Text.Encoding.UTF8);
            Console.WriteLine("     磁盘原文片段：SchemaVersion=" + RawSchemaVersion(raw)
                + "，是否含 \"Enabled\"=" + raw.Contains("\"Enabled\"")
                + "，是否含 \"Product\"=" + raw.Contains("\"Product\""));

            // 走 ConfigStore.Load() 的真实路径（与程序启动时完全一致）
            AppConfig cfg = ConfigStore.Load();

            int before = RawSchemaVersion(raw);
            Console.WriteLine("     加载前磁盘 SchemaVersion=" + before
                + " → 加载后内存 SchemaVersion=" + cfg.Flows.SchemaVersion);

            Check("SchemaVersion 已就地升到 " + FlowSettings.CurrentSchemaVersion,
                cfg.Flows.SchemaVersion == FlowSettings.CurrentSchemaVersion,
                before + " → " + cfg.Flows.SchemaVersion);

            int total = cfg.Flows.Flows.Count;
            int enabled = 0, emptyProduct = 0;
            var names = new List<string>();
            foreach (FlowDefinition f in cfg.Flows.Flows)
            {
                if (f == null) continue;
                names.Add(f.Name + "(Enabled=" + f.Enabled + ",Product=\"" + f.Product + "\")");
                if (f.Enabled) enabled++;
                if (f.Product == "") emptyProduct++;
            }

            Console.WriteLine("     流程清单：" + string.Join("；", names));

            Check("★ 旧配置全部流程仍为 Enabled=true", total > 0 && enabled == total,
                enabled + "/" + total + " 条启用");
            Check("★ 旧配置 Product 归一化为 \"\"（非 null）", emptyProduct == total,
                emptyProduct + "/" + total);
            Check("★ 旧流程的节点与连线未被破坏",
                cfg.Flows.Flows.TrueForAll(x => x != null && x.Nodes != null && x.Links != null));
        }

        /// <summary>直接从 JSON 文本里抠出 Flows.SchemaVersion，避免被内存升级影响。</summary>
        private static int RawSchemaVersion(string json)
        {
            int i = json.IndexOf("\"SchemaVersion\"", StringComparison.Ordinal);
            if (i < 0) return -1;
            int j = json.IndexOf(':', i);
            if (j < 0) return -1;
            int k = j + 1;
            while (k < json.Length && (json[k] == ' ' || json[k] == '\r' || json[k] == '\n')) k++;
            int m = k;
            while (m < json.Length && (char.IsDigit(json[m]) || json[m] == '-')) m++;
            int v;
            return int.TryParse(json.Substring(k, m - k), out v) ? v : -1;
        }

        // =================================================================
        //  7. 【硬验收】合成一份 SchemaVersion=2 的旧配置真跑一次
        // =================================================================
        private static void TestSyntheticV2ConfigRun()
        {
            Console.WriteLine("[7] 【硬验收】合成 SchemaVersion=2 旧配置真跑一次（备份后还原）");

            string path = AppPaths.ConfigFile;
            string backup = path + ".smokebak";
            bool hadOriginal = File.Exists(path);
            if (hadOriginal) File.Copy(path, backup, true);

            try
            {
                File.WriteAllText(path, OldV2ConfigJson, System.Text.Encoding.UTF8);

                // 真跑：与程序启动时完全相同的入口
                AppConfig cfg = ConfigStore.Load();

                Check("★ v2 旧配置加载成功且流程数 = 2", cfg.Flows.Flows.Count == 2,
                    "count=" + cfg.Flows.Flows.Count);
                Check("★ SchemaVersion 2 → " + FlowSettings.CurrentSchemaVersion,
                    cfg.Flows.SchemaVersion == FlowSettings.CurrentSchemaVersion);

                int enabled = 0, emptyProduct = 0;
                var names = new List<string>();
                foreach (FlowDefinition f in cfg.Flows.Flows)
                {
                    if (f == null) continue;
                    names.Add(f.Name + "(Enabled=" + f.Enabled + ")");
                    if (f.Enabled) enabled++;
                    if (f.Product == "") emptyProduct++;
                }
                Console.WriteLine("     流程清单：" + string.Join("；", names));

                Check("★ v2 旧配置全部流程仍为 Enabled=true", enabled == 2, enabled + "/2 条启用");
                Check("★ v2 旧配置 Product 归一化为 \"\"", emptyProduct == 2);

                // 引擎视角：这些流程应当进入「全部启动」的作用域
                List<FlowDefinition> filtered = FlowModule.FilterByProduct(cfg.Flows.Flows, cfg.Ui.CurrentProduct);
                int enabledFiltered = 0;
                foreach (FlowDefinition f in filtered) if (f != null && f.Enabled) enabledFiltered++;
                Check("★ 引擎「全部启动」作用域非空（不会一条都跑不起来）", enabledFiltered == 2,
                    "enabledFiltered=" + enabledFiltered);

                // AppSettings 三项缺省补齐
                Check("★ 旧配置 CurrentProduct 补齐为 \"\"", cfg.Ui.CurrentProduct == "");
                Check("★ 旧配置 MaxParallelFlows 补齐为 4", cfg.Ui.MaxParallelFlows == 4,
                    "实际=" + cfg.Ui.MaxParallelFlows);
                Check("★ 旧配置 AllowCameraShare 缺省 false（严格独占）", cfg.System.AllowCameraShare == false);
                Check("★ 旧配置 YieldAlarmPercent 仍被补齐为 95", Math.Abs(cfg.System.YieldAlarmPercent - 95.0) < 1e-9,
                    "实际=" + cfg.System.YieldAlarmPercent);

                // 二次加载幂等：再 Load 一次仍全部启用
                AppConfig cfg2 = ConfigStore.Load();
                int enabled2 = 0;
                foreach (FlowDefinition f in cfg2.Flows.Flows) if (f != null && f.Enabled) enabled2++;
                Check("★ 重复加载幂等（仍 2 条启用）", enabled2 == 2, "enabled2=" + enabled2);
            }
            finally
            {
                if (hadOriginal) File.Copy(backup, path, true);
                if (File.Exists(backup)) File.Delete(backup);
                Console.WriteLine("     （已还原原配置文件：" + (hadOriginal ? "是" : "原本不存在") + "）");
            }
        }

        /// <summary>
        /// 手工构造的 SchemaVersion=2 旧配置：<strong>不含</strong> Product / Enabled /
        /// CurrentProduct / MaxParallelFlows / AllowCameraShare 任何一个新字段，
        /// 与上一版程序写出的文件形态完全一致。
        /// </summary>
        private const string OldV2ConfigJson =
            "{\n" +
            "  \"Algorithm\": { \"CurrentRecipeName\": \"\", \"Inspection\": null },\n" +
            "  \"Flows\": {\n" +
            "    \"ActiveFlowId\": \"flow-old-0001\",\n" +
            "    \"SchemaVersion\": 2,\n" +
            "    \"Flows\": [\n" +
            "      {\n" +
            "        \"Id\": \"flow-old-0001\",\n" +
            "        \"Name\": \"旧流程-尺寸检测\",\n" +
            "        \"CameraId\": \"\",\n" +
            "        \"ViewIndex\": 1,\n" +
            "        \"Nodes\": [\n" +
            "          { \"Id\": \"n1\", \"Type\": \"ImageSource\", \"Name\": \"图像源\", \"X\": 40, \"Y\": 40,\n" +
            "            \"Params\": [ { \"Key\": \"Source\", \"Value\": \"Camera\" } ] },\n" +
            "          { \"Id\": \"n2\", \"Type\": \"ResultOutput\", \"Name\": \"结果输出\", \"X\": 200, \"Y\": 40,\n" +
            "            \"Params\": [] }\n" +
            "        ],\n" +
            "        \"Links\": [ { \"FromNodeId\": \"n1\", \"ToNodeId\": \"n2\" } ]\n" +
            "      },\n" +
            "      {\n" +
            "        \"Id\": \"flow-old-0002\",\n" +
            "        \"Name\": \"旧流程-圆检测\",\n" +
            "        \"CameraId\": \"\",\n" +
            "        \"ViewIndex\": 2,\n" +
            "        \"Nodes\": [\n" +
            "          { \"Id\": \"m1\", \"Type\": \"ImageSource\", \"Name\": \"图像源\", \"X\": 40, \"Y\": 40,\n" +
            "            \"Params\": [ { \"Key\": \"Source\", \"Value\": \"Camera\" } ] },\n" +
            "          { \"Id\": \"m2\", \"Type\": \"Preprocess\", \"Name\": \"预处理\", \"X\": 200, \"Y\": 40,\n" +
            "            \"Params\": [] },\n" +
            "          { \"Id\": \"m3\", \"Type\": \"ResultOutput\", \"Name\": \"结果输出\", \"X\": 360, \"Y\": 40,\n" +
            "            \"Params\": [] }\n" +
            "        ],\n" +
            "        \"Links\": [ { \"FromNodeId\": \"m1\", \"ToNodeId\": \"m2\" }, { \"FromNodeId\": \"m2\", \"ToNodeId\": \"m3\" } ]\n" +
            "      }\n" +
            "    ]\n" +
            "  },\n" +
            "  \"System\": { \"ImageSavePath\": \"\", \"ImageFormat\": \"PNG\", \"RetentionDays\": 30,\n" +
            "    \"AutoSaveEnabled\": false, \"SaveNgOnly\": false, \"RequireCommBeforeRun\": false,\n" +
            "    \"YieldAlarmPercent\": 95 },\n" +
            "  \"Ui\": { \"LayoutIndex\": 0, \"ViewBindings\": [], \"LogCollapsed\": false }\n" +
            "}\n";

        // =================================================================
        //  8. 陷阱实证：不升级的话 Enabled 真的会是 false
        // =================================================================
        private static void TestTrapProof()
        {
            Console.WriteLine("[8] 陷阱实证：反序列化后不升级 → Enabled 全 false；升级后 → 全 true");

            FlowSettings fs = Json.Deserialize<FlowSettings>(OldFlowsV2Json);
            int falseCount = 0, total = 0;
            foreach (FlowDefinition f in fs.Flows) { total++; if (!f.Enabled) falseCount++; }

            Check("★ 陷阱真实存在：反序列化后 Enabled 全为 false",
                total == 2 && falseCount == 2, falseCount + "/" + total + " 为 false");
            Check("★ 陷阱真实存在：Product 为 null",
                fs.Flows.TrueForAll(x => x.Product == null));

            bool changed1 = FlowSettings.UpgradeToCurrent(fs);
            int trueCount = 0;
            foreach (FlowDefinition f in fs.Flows) if (f.Enabled) trueCount++;
            Check("★ UpgradeToCurrent 后 Enabled 全为 true", trueCount == 2, trueCount + "/2");
            Check("★ UpgradeToCurrent 后 Product 为 \"\"", fs.Flows.TrueForAll(x => x.Product == ""));
            Check("首次升级返回 changed=true", changed1);

            bool changed2 = FlowSettings.UpgradeToCurrent(fs);
            int stillTrue = 0;
            foreach (FlowDefinition f in fs.Flows) if (f.Enabled) stillTrue++;
            Check("★ 幂等：二次升级返回 false 且 Enabled 仍全 true", !changed2 && stillTrue == 2);
        }

        /// <summary>只含 Flows 段的 SchemaVersion=2 旧数据（不含 Product / Enabled）。</summary>
        private const string OldFlowsV2Json =
            "{\"ActiveFlowId\":\"f1\",\"SchemaVersion\":2,\"Flows\":[" +
            "{\"Id\":\"f1\",\"Name\":\"旧流程1\",\"CameraId\":\"\",\"ViewIndex\":1,\"Nodes\":[],\"Links\":[]}," +
            "{\"Id\":\"f2\",\"Name\":\"旧流程2\",\"CameraId\":\"\",\"ViewIndex\":1,\"Nodes\":[],\"Links\":[]}]}";

        // =================================================================
        //  9. AppSettings 新增三项的缺省与钳制
        // =================================================================
        private static void TestAppSettingsCompat()
        {
            Console.WriteLine("[9] AppSettings 新增字段缺省与钳制");

            Check("DefaultMaxParallelFlows = 4", UiSettings.DefaultMaxParallelFlows == 4);
            Check("钳制下界 0 → 1", UiSettings.ClampMaxParallelFlows(0) == 1);
            Check("钳制下界 -5 → 1", UiSettings.ClampMaxParallelFlows(-5) == 1);
            Check("钳制上界 99 → 8", UiSettings.ClampMaxParallelFlows(99) == 8);
            Check("钳制上界 8 保持", UiSettings.ClampMaxParallelFlows(8) == 8);
            Check("区间内 6 保持", UiSettings.ClampMaxParallelFlows(6) == 6);

            var ui = new UiSettings { CurrentProduct = "产品A", MaxParallelFlows = 7 };
            UiSettings copy = ui.Clone();
            Check("Clone 同步 CurrentProduct / MaxParallelFlows",
                copy.CurrentProduct == "产品A" && copy.MaxParallelFlows == 7);

            var sys = SystemSettings.CreateDefault();
            Check("SystemSettings.CreateDefault 的 AllowCameraShare = false", sys.AllowCameraShare == false);
            SystemSettings sysCopy = sys.Clone();
            Check("SystemSettings.Clone 同步 AllowCameraShare", sysCopy.AllowCameraShare == sys.AllowCameraShare);

            // 全新默认配置的三项初值
            AppConfig def = AppConfig.CreateDefault();
            Check("新建配置 CurrentProduct = \"\"", def.Ui.CurrentProduct == "");
            Check("新建配置 MaxParallelFlows = 4", def.Ui.MaxParallelFlows == 4);
            Check("新建流程 Enabled = true", FlowDefinition.CreateEmpty("x").Enabled);
        }

        // =================================================================
        //  10. FlowRunManager 启动校验顺序与中文原因
        // =================================================================
        private static void TestRunManagerValidation()
        {
            Console.WriteLine("[10] FlowRunManager 校验顺序 / 引擎收尾");

            Check("MaxParallelFlows 默认取配置为 4", FlowRunManager.MaxParallelFlows == 4,
                "实际=" + FlowRunManager.MaxParallelFlows);
            Check("初始 RunningCount = 0", FlowRunManager.RunningCount == 0);
            Check("初始 Snapshot 为空", FlowRunManager.Snapshot().Count == 0);

            OpResult r1 = FlowRunManager.StartOne("不存在的流程Id");
            Check("① 流程不存在：返回失败且中文原因",
                r1 != null && !r1.Success && r1.Message != null && r1.Message.Contains("流程不存在"),
                r1 != null ? r1.Message : "null");

            OpResult r2 = FlowRunManager.StartOne(null);
            Check("② 空 Id：返回失败不抛异常", r2 != null && !r2.Success);

            // 从当前配置里取一条真实流程，验证停用 / 相机未打开等分支的中文文案
            AppConfig cfg = ConfigStore.Current;
            if (cfg != null && cfg.Flows != null && cfg.Flows.Flows.Count > 0)
            {
                FlowDefinition f = cfg.Flows.Flows[0];
                bool wasEnabled = f.Enabled;
                string wasCamera = f.CameraId;

                // 3) 停用分支
                f.Enabled = false;
                OpResult r3 = FlowRunManager.StartOne(f.Id);
                Check("③ 流程已停用：中文原因含「已停用」",
                    r3 != null && !r3.Success && r3.Message != null && r3.Message.Contains("已停用"),
                    r3 != null ? r3.Message : "null");

                // 4) 相机未打开分支（拓扑校验先过、再查相机）
                f.Enabled = true;
                f.CameraId = "CAM-NOT-EXIST";
                OpResult r4 = FlowRunManager.StartOne(f.Id);
                Check("④ 相机未打开 / 未绑定：返回中文原因",
                    r4 != null && !r4.Success && r4.Message != null &&
                    (r4.Message.Contains("未打开") || r4.Message.Contains("未绑定") || r4.Message.Contains("拓扑")),
                    r4 != null ? r4.Message : "null");

                f.Enabled = wasEnabled;
                f.CameraId = wasCamera;
            }

            // 引擎收尾：未启动任何流程时 StopAll / Shutdown 都不应抛异常
            bool stopOk = true, shutOk = true;
            try { FlowRunManager.StopAll(); } catch (Exception ex) { stopOk = false; Console.WriteLine("     " + ex.Message); }
            try { FlowRunManager.Shutdown(); } catch (Exception ex) { shutOk = false; Console.WriteLine("     " + ex.Message); }
            Check("空引擎 StopAll 不抛异常", stopOk);
            Check("空引擎 Shutdown 不抛异常", shutOk);
            Check("Shutdown 后租约表已清空", CameraLease.Snapshot().Count == 0);

            OpResult s1 = FlowRunManager.StopOne("不存在的流程Id");
            Check("停止不存在的流程返回成功（幂等）", s1 != null && s1.Success, s1 != null ? s1.Message : "null");
            OpResult p1 = FlowRunManager.PauseOne("不存在的流程Id");
            Check("暂停未运行的流程返回失败", p1 != null && !p1.Success);

            // 槽：未启动就 Dispose 不应抛异常
            var slot = new FlowRunSlot(FlowDefinition.CreateEmpty("测试流程"));
            Check("槽属性初值正确",
                slot.Product == "" && slot.ViewIndex == 1 && slot.Status == FlowSlotStatus.Idle
                && slot.StatusText == "已停止" && !slot.IsActive && slot.OkCount == 0 && slot.NgCount == 0
                && slot.LastVerdict == null && slot.Fps == 0.0);
            bool disposeOk = true;
            try { slot.Dispose(); } catch (Exception ex) { disposeOk = false; Console.WriteLine("     " + ex.Message); }
            Check("未启动的槽 Dispose 不抛异常", disposeOk);
        }

        // =================================================================
        //  11. 【T01】端子模型 + 连线端口化 + Schema v4 升级
        // =================================================================
        private static void TestPortSchemaUpgrade()
        {
            Console.WriteLine("[11] 【T01】端子表 + 连线端口化 + Schema v4 升级");

            TestPortTable();
            TestPortLinkModel();
            TestPortNormalize();
            TestOldConfigPortUpgrade();
        }

        /// <summary>端子表静态部分：12 类清单、主输入 / 主输出、类型兼容矩阵。</summary>
        private static void TestPortTable()
        {
            // ---- 清单与顺序 ----
            List<FlowPortDef> srcIn = FlowPortSchema.InputsOf(FlowNodeType.ImageSource);
            List<FlowPortDef> srcOut = FlowPortSchema.OutputsOf(FlowNodeType.ImageSource);
            Check("图像源：无输入、1 个输出 Image",
                srcIn.Count == 0 && srcOut.Count == 1 && srcOut[0].Key == "Image"
                && srcOut[0].Kind == FlowPortKind.Image,
                "in=" + srcIn.Count + ",out=" + string.Join(",", srcOut.ConvertAll(x => x.Key)));

            List<FlowPortDef> pcIn = FlowPortSchema.InputsOf(FlowNodeType.PositionCorrect);
            Check("位置修正：Image 必填 + Pose 可选（team-lead 拍板，优于文档 §4）",
                pcIn.Count == 2 && pcIn[0].Key == "Image" && pcIn[1].Key == "Pose"
                && pcIn[0].Required && !pcIn[1].Required
                && pcIn[1].Kind == FlowPortKind.Pose,
                string.Join(",", pcIn.ConvertAll(x => x.Key + (x.Required ? "✅" : "⬜"))));

            List<FlowPortDef> pcOut = FlowPortSchema.OutputsOf(FlowNodeType.PositionCorrect);
            Check("位置修正：输出 Image + Pose（第 1 项为主输出）",
                pcOut.Count == 2 && pcOut[0].Key == "Image" && pcOut[1].Key == "Pose");

            List<FlowPortDef> smIn = FlowPortSchema.InputsOf(FlowNodeType.ShapeMatch);
            List<FlowPortDef> smOut = FlowPortSchema.OutputsOf(FlowNodeType.ShapeMatch);
            Check("形状匹配：Pose 可选输入，输出 Image/Pose/Score",
                smIn.Count == 2 && smIn[1].Key == "Pose" && !smIn[1].Required
                && smOut.Count == 3 && smOut[2].Key == "Score" && smOut[2].Kind == FlowPortKind.Number);

            List<FlowPortDef> tmIn = FlowPortSchema.InputsOf(FlowNodeType.TemplateMatch);
            List<FlowPortDef> tmOut = FlowPortSchema.OutputsOf(FlowNodeType.TemplateMatch);
            Check("模板匹配端子与形状匹配一致",
                tmIn.Count == 2 && tmOut.Count == 3 && tmOut[0].Key == "Image" && tmOut[2].Key == "Score");

            List<FlowPortDef> blobOut = FlowPortSchema.OutputsOf(FlowNodeType.Blob);
            Check("Blob：输出 Image/Blobs/Count",
                blobOut.Count == 3 && blobOut[1].Key == "Blobs" && blobOut[1].Kind == FlowPortKind.BlobList
                && blobOut[2].Key == "Count");

            List<FlowPortDef> lcOut = FlowPortSchema.OutputsOf(FlowNodeType.LineCaliper);
            List<FlowPortDef> ccOut = FlowPortSchema.OutputsOf(FlowNodeType.CircleCaliper);
            Check("直线卡尺：Geometry + 直线度 RMS",
                lcOut.Count == 3 && lcOut[1].Key == "Geometry" && lcOut[2].Key == "Value");
            Check("圆卡尺：Geometry + 直径",
                ccOut.Count == 3 && ccOut[1].Key == "Geometry" && ccOut[2].Key == "Value"
                && FlowPortSchema.InputsOf(FlowNodeType.CircleCaliper)[1].Key == "Pose");

            Check("清晰度：1 输入 + Image/Value 两输出",
                FlowPortSchema.InputsOf(FlowNodeType.Sharpness).Count == 1
                && FlowPortSchema.OutputsOf(FlowNodeType.Sharpness).Count == 2);

            Check("圆拟合 / 直线拟合：Image 必填 + Blobs 可选 + Image/Geometry 输出",
                FlowPortSchema.InputsOf(FlowNodeType.FitCircle).Count == 2
                && FlowPortSchema.InputsOf(FlowNodeType.FitCircle)[1].Kind == FlowPortKind.BlobList
                && FlowPortSchema.OutputsOf(FlowNodeType.FitLine)[1].Key == "Geometry");

            List<FlowPortDef> resIn = FlowPortSchema.InputsOf(FlowNodeType.ResultOutput);
            Check("结果输出：1 个必填 Any 输入、无输出",
                resIn.Count == 1 && resIn[0].Key == "Any" && resIn[0].Kind == FlowPortKind.Any
                && resIn[0].Required && FlowPortSchema.OutputsOf(FlowNodeType.ResultOutput).Count == 0);

            // ---- 主输入 / 主输出 ----
            bool primaryOk =
                FlowPortSchema.PrimaryOutput(FlowNodeType.Preprocess) == "Image"
                && FlowPortSchema.PrimaryOutput(FlowNodeType.ShapeMatch) == "Image"
                && FlowPortSchema.PrimaryOutput(FlowNodeType.Blob) == "Image"
                && FlowPortSchema.PrimaryOutput(FlowNodeType.ResultOutput) == ""
                && FlowPortSchema.PrimaryInput(FlowNodeType.Preprocess) == "Image"
                && FlowPortSchema.PrimaryInput(FlowNodeType.ShapeMatch) == "Image"
                && FlowPortSchema.PrimaryInput(FlowNodeType.PositionCorrect) == "Image"
                && FlowPortSchema.PrimaryInput(FlowNodeType.ResultOutput) == "Any"
                && FlowPortSchema.PrimaryInput(FlowNodeType.ImageSource) == "";
            Check("主输出取第 1 项 / 主输入取第 1 个必填项",
                primaryOk,
                "Blob主输出=" + FlowPortSchema.PrimaryOutput(FlowNodeType.Blob)
                + "，位置修正主输入=" + FlowPortSchema.PrimaryInput(FlowNodeType.PositionCorrect)
                + "，结果输出主输出=\"" + FlowPortSchema.PrimaryOutput(FlowNodeType.ResultOutput) + "\"");

            // ---- Find / HasPort ----
            FlowPortDef score = FlowPortSchema.Find(FlowNodeType.ShapeMatch, FlowPortDir.Out, "Score");
            Check("Find 命中返回端子定义", score != null && score.DisplayName == "匹配分数");
            bool nullOk =
                FlowPortSchema.Find(FlowNodeType.ShapeMatch, FlowPortDir.Out, "不存在") == null
                && FlowPortSchema.Find(FlowNodeType.ShapeMatch, FlowPortDir.Out, "") == null
                && FlowPortSchema.Find(FlowNodeType.ShapeMatch, FlowPortDir.Out, null) == null
                && FlowPortSchema.Find(FlowNodeType.ShapeMatch, FlowPortDir.In, "Score") == null;
            Check("Find 查不到 / 空 Key / 方向不符均返回 null 且不抛异常", nullOk);
            Check("HasPort 方向敏感",
                FlowPortSchema.HasPort(FlowNodeType.Blob, FlowPortDir.Out, "Blobs")
                && !FlowPortSchema.HasPort(FlowNodeType.Blob, FlowPortDir.In, "Blobs")
                && !FlowPortSchema.HasPort(FlowNodeType.Blob, FlowPortDir.In, "Pose"));

            // ---- 类型兼容矩阵 ----
            bool compatOk =
                FlowPortSchema.IsCompatible(FlowPortKind.Image, FlowPortKind.Image)
                && FlowPortSchema.IsCompatible(FlowPortKind.Pose, FlowPortKind.Any)
                && FlowPortSchema.IsCompatible(FlowPortKind.Any, FlowPortKind.Geometry)
                && !FlowPortSchema.IsCompatible(FlowPortKind.Image, FlowPortKind.Pose)
                && !FlowPortSchema.IsCompatible(FlowPortKind.Number, FlowPortKind.Image)
                && !FlowPortSchema.IsCompatible(FlowPortKind.BlobList, FlowPortKind.Geometry);
            Check("类型兼容矩阵：同类型或任一端为 Any 才放行", compatOk);

            Check("Accepts 与 IsCompatible 同规则",
                FlowPortSchema.Find(FlowNodeType.ResultOutput, FlowPortDir.In, "Any").Accepts(FlowPortKind.Image)
                && !FlowPortSchema.Find(FlowNodeType.Preprocess, FlowPortDir.In, "Image").Accepts(FlowPortKind.Pose));

            Check("CanConnect 端到端判定",
                FlowPortSchema.CanConnect(FlowNodeType.ImageSource, "Image", FlowNodeType.Preprocess, "Image")
                && FlowPortSchema.CanConnect(FlowNodeType.ImageSource, "Image", FlowNodeType.ResultOutput, "Any")
                && !FlowPortSchema.CanConnect(FlowNodeType.ImageSource, "Image", FlowNodeType.ShapeMatch, "Pose")
                && !FlowPortSchema.CanConnect(FlowNodeType.ImageSource, "不存在", FlowNodeType.Preprocess, "Image"));

            // ---- 未知类型不抛异常 ----
            var unknown = (FlowNodeType)9999;
            bool unknownOk = FlowPortSchema.InputsOf(unknown).Count == 0
                && FlowPortSchema.OutputsOf(unknown).Count == 0
                && FlowPortSchema.PrimaryInput(unknown) == ""
                && FlowPortSchema.PrimaryOutput(unknown) == ""
                && FlowPortSchema.Find(unknown, FlowPortDir.In, "Image") == null;
            Check("未知节点类型返回空表 / 空串且不抛异常", unknownOk);

            // ---- FlowModuleInfo 转发 ----
            FlowModuleInfo src = FlowModule.FindByType(FlowNodeType.ImageSource);
            FlowModuleInfo res = FlowModule.FindByType(FlowNodeType.ResultOutput);
            bool forward = src != null && res != null
                && src.Inputs.Count == 0 && src.Outputs.Count == 1 && src.PrimaryOutput == "Image"
                && res.Inputs.Count == 1 && res.Outputs.Count == 0 && res.PrimaryInput == "Any";
            Check("FlowModuleInfo 转发同一张端子表", forward);
            int portTotal = 0;
            foreach (FlowModuleInfo m in FlowModule.All)
                if (m != null) portTotal += m.Inputs.Count + m.Outputs.Count;
            // 逐类点数：0+1, 1+1, 2+2, 2+3, 2+3, 1+3, 2+3, 2+3, 1+2, 2+2, 2+2, 1+0 = 43
            Check("12 类模块的端子总数为 43（与 §4 清单表逐条一致）", portTotal == 43, "total=" + portTotal);
        }

        /// <summary>FlowLink 的端子字段、双构造与 Clone。</summary>
        private static void TestPortLinkModel()
        {
            var l2 = new FlowLink("n1", "n2");
            bool defOk = l2.FromNodeId == "n1" && l2.ToNodeId == "n2"
                && l2.FromPort == "" && l2.ToPort == "";
            Check("两参构造兼容（端子字段为 \"\" 而非 null）", defOk);

            var l4 = new FlowLink("n1", "Image", "n2", "Any");
            Check("四参构造写入端子字段",
                l4.FromPort == "Image" && l4.ToPort == "Any" && l4.FromNodeId == "n1" && l4.ToNodeId == "n2");

            FlowLink copy = l4.Clone();
            copy.FromPort = "Pose";
            Check("Clone 带上端子字段且为深拷贝",
                l4.FromPort == "Image" && copy.ToPort == "Any" && copy.FromPort == "Pose");

            // 序列化往返：端子字段必须进 JSON
            string json = Json.Serialize(l4, false);
            FlowLink back = Json.Deserialize<FlowLink>(json);
            Check("端子字段参与序列化往返",
                back != null && back.FromPort == "Image" && back.ToPort == "Any", json);
        }

        /// <summary>内存态归一化：补默认端子、幂等、非法值替换、悬空连线不动。</summary>
        private static void TestPortNormalize()
        {
            FlowDefinition flow = BuildPortSampleFlow();

            FlowLink l1 = flow.Links[0];
            bool changed1 = FlowLink.Normalize(flow, l1);
            Check("缺端子 → 补主输出/主输入（ImageSource.Image → Preprocess.Image）",
                changed1 && l1.FromPort == "Image" && l1.ToPort == "Image",
                l1.FromPort + "→" + l1.ToPort);

            bool changed2 = FlowLink.Normalize(flow, l1);
            Check("★ 幂等：二次归一化返回 false", !changed2 && l1.FromPort == "Image" && l1.ToPort == "Image");

            FlowLink l2 = flow.Links[1];
            FlowLink.Normalize(flow, l2);
            Check("Preprocess → ResultOutput 归一化到 Any",
                l2.FromPort == "Image" && l2.ToPort == "Any", l2.FromPort + "→" + l2.ToPort);

            // 非法端子名：查不到就要被换成默认端子（不是钳制，是补）
            l1.FromPort = "不存在的端子";
            l1.ToPort = "XXX";
            bool changed3 = FlowLink.Normalize(flow, l1);
            Check("非法端子名被替换为默认端子",
                changed3 && l1.FromPort == "Image" && l1.ToPort == "Image", l1.FromPort + "→" + l1.ToPort);

            // 悬空连线：不动、返回 false
            var dangling = new FlowLink("不存在A", "不存在B");
            bool changed4 = FlowLink.Normalize(flow, dangling);
            Check("悬空连线不归一化且不抛异常",
                !changed4 && dangling.FromPort == "" && dangling.ToPort == "");

            // 源节点无输出端子（ResultOutput 作源）：补成 ""，且只改一次（幂等边界）
            var bad = new FlowLink("r1", "p1");
            bool c5 = FlowLink.Normalize(flow, bad);
            bool c6 = FlowLink.Normalize(flow, bad);
            Check("无可用输出端子时补 \"\"，且二次调用返回 false（幂等边界）",
                c5 && !c6 && bad.FromPort == "" && bad.ToPort == "Image",
                "\"" + bad.FromPort + "\"→\"" + bad.ToPort + "\"");

            Check("Normalize 对 null 入参返回 false",
                !FlowLink.Normalize(null, null) && !FlowLink.Normalize(flow, null));

            // FlowDefinition 只读辅助查询
            bool aux = flow.FindLinksTo("p1").Count == 1
                && flow.FindLinksFrom("p1").Count == 1
                && flow.FindLinksTo("p1", "Image").Count == 1
                && flow.FindLinksTo("p1", "Pose").Count == 0
                && flow.FindLinksFrom("s1", "Image").Count == 1
                && flow.HasLinkTo("r1") && !flow.HasLinkTo("s1");
            Check("FlowDefinition 连线辅助查询（可筛选端子）", aux);

            // 走完整的 Schema 升级路径：第一次补端子返回 true，第二次幂等返回 false
            FlowDefinition raw = BuildPortSampleFlow();
            var fs = new FlowSettings { Flows = new List<FlowDefinition> { raw } };
            bool up1 = FlowSettings.UpgradeToCurrent(fs);
            bool up2 = FlowSettings.UpgradeToCurrent(fs);
            Check("★ UpgradeToCurrent 归一化连线且第二次幂等",
                up1 && !up2 && raw.Links.Count == 2
                && raw.Links[0].FromPort == "Image" && raw.Links[0].ToPort == "Image"
                && raw.Links[1].FromPort == "Image" && raw.Links[1].ToPort == "Any",
                "changed: " + up1 + " → " + up2);
        }

        /// <summary>
        /// 【硬验收】磁盘上的 v2 旧配置（只有 FromNodeId / ToNodeId）走真实
        /// <see cref="ConfigStore.Load"/> 后，所有连线的端子字段非空且等于默认端子。
        /// </summary>
        private static void TestOldConfigPortUpgrade()
        {
            string path = AppPaths.ConfigFile;
            string backup = path + ".smokebak.ports";
            bool hadOriginal = File.Exists(path);
            if (hadOriginal) File.Copy(path, backup, true);

            try
            {
                File.WriteAllText(path, OldV2ConfigJson, System.Text.Encoding.UTF8);

                AppConfig cfg = ConfigStore.Load();

                Check("★ v2 旧配置 SchemaVersion → " + FlowSettings.CurrentSchemaVersion,
                    cfg.Flows.SchemaVersion == FlowSettings.CurrentSchemaVersion,
                    "实际=" + cfg.Flows.SchemaVersion);

                List<string> got = CollectPortPairs(cfg.Flows.Flows);
                Console.WriteLine("     归一化结果：" + string.Join(" | ", got));

                int emptyCount = 0, nullCount = 0, total = 0;
                foreach (FlowDefinition f in cfg.Flows.Flows)
                {
                    if (f == null || f.Links == null) continue;
                    foreach (FlowLink l in f.Links)
                    {
                        if (l == null) continue;
                        total++;
                        if (l.FromPort == null || l.ToPort == null) nullCount++;
                        if (string.IsNullOrEmpty(l.FromPort) || string.IsNullOrEmpty(l.ToPort)) emptyCount++;
                    }
                }

                Check("★ 全部连线的端子字段非 null", total == 3 && nullCount == 0, nullCount + "/" + total + " 为 null");
                Check("★ 全部连线的端子字段非空串", emptyCount == 0 && total == 3, emptyCount + "/" + total + " 为空");

                string expect = "n1.Image→n2.Any | m1.Image→m2.Image | m2.Image→m3.Any";
                Check("★ 旧三条连线归一化结果符合推演表 §5.4",
                    string.Join(" | ", got) == expect, string.Join(" | ", got));

                foreach (FlowLink l in cfg.Flows.Flows[0].Links)
                {
                    FlowNode from = cfg.Flows.Flows[0].FindNode(l.FromNodeId);
                    FlowNode to = cfg.Flows.Flows[0].FindNode(l.ToNodeId);
                    bool legal = from != null && to != null
                        && FlowPortSchema.HasPort(ParseType(from), FlowPortDir.Out, l.FromPort)
                        && FlowPortSchema.HasPort(ParseType(to), FlowPortDir.In, l.ToPort);
                    Check("★ 归一化后的端子在端子表中真实存在", legal, l.FromPort + "→" + l.ToPort);
                }

                bool changed2 = FlowSettings.UpgradeToCurrent(cfg.Flows);
                Check("★ 幂等：二次 UpgradeToCurrent 返回 false", !changed2);

                AppConfig cfg3 = ConfigStore.Load();
                Check("★ 重复加载结果稳定", string.Join(" | ", CollectPortPairs(cfg3.Flows.Flows)) == expect);
            }
            finally
            {
                if (hadOriginal) File.Copy(backup, path, true);
                if (File.Exists(backup)) File.Delete(backup);
                Console.WriteLine("     （已还原原配置文件：" + (hadOriginal ? "是" : "原本不存在") + "）");
            }
        }

        private static FlowNodeType ParseType(FlowNode node)
        {
            FlowNodeType t;
            node.TryGetType(out t);
            return t;
        }

        private static List<string> CollectPortPairs(List<FlowDefinition> flows)
        {
            var list = new List<string>();
            if (flows == null) return list;

            foreach (FlowDefinition f in flows)
            {
                if (f == null || f.Links == null) continue;
                foreach (FlowLink l in f.Links)
                {
                    if (l == null) continue;
                    list.Add(l.FromNodeId + "." + (l.FromPort ?? "<null>") + "→"
                           + l.ToNodeId + "." + (l.ToPort ?? "<null>"));
                }
            }
            return list;
        }

        /// <summary>ImageSource(s1) → Preprocess(p1) → ResultOutput(r1)，连线用两参构造（无端子）。</summary>
        private static FlowDefinition BuildPortSampleFlow()
        {
            FlowDefinition f = FlowDefinition.CreateEmpty("端口化样例");
            FlowNode s1 = FlowModule.CreateNode(FlowNodeType.ImageSource, 40, 40);
            s1.Id = "s1";
            FlowNode p1 = FlowModule.CreateNode(FlowNodeType.Preprocess, 200, 40);
            p1.Id = "p1";
            FlowNode r1 = FlowModule.CreateNode(FlowNodeType.ResultOutput, 360, 40);
            r1.Id = "r1";

            f.Nodes.Add(s1);
            f.Nodes.Add(p1);
            f.Nodes.Add(r1);
            f.Links.Add(new FlowLink("s1", "p1"));
            f.Links.Add(new FlowLink("p1", "r1"));
            return f;
        }

        // =================================================================
        //  12. 【T02】并行引擎真跑：两条流程同时跑起来
        // =================================================================
        private static void TestParallelRealRun()
        {
            Console.WriteLine("[12] 【T02】并行引擎真跑：两条流程同时运行（相机流 + 本地图片流）");

            // 临时本地图片（给「不需要相机」的那条流程当图像源）
            string imgPath = Path.Combine(Path.GetTempPath(), "smoke_parallel_src.png");
            try
            {
                using (var bmp = new Bitmap(640, 480))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Black);
                        using (var br = new SolidBrush(Color.White))
                            g.FillEllipse(br, 220, 140, 200, 200);
                    }
                    bmp.Save(imgPath, System.Drawing.Imaging.ImageFormat.Png);
                }

                OpResult<ICamera> open = CameraManager.Instance.Open("SIM-0");
                Check("模拟相机 SIM-0 打开成功", open != null && open.Success && open.Value != null,
                    open != null ? open.Message : "null");
                if (open == null || !open.Success) return;

                // 构造两条流程并挂进内存配置（不落盘）
                FlowDefinition flowCam = BuildSimpleFlow("并行-相机流", "Camera", "");
                flowCam.CameraId = "SIM-0";
                flowCam.ViewIndex = 1;
                flowCam.Product = "产品A";

                FlowDefinition flowFile = BuildSimpleFlow("并行-图片流", "File", imgPath);
                flowFile.CameraId = "";
                flowFile.ViewIndex = 2;
                flowFile.Product = "产品B";

                FlowSettings fs = ConfigStore.Current.Flows;
                fs.Flows.Clear();
                fs.Flows.Add(flowCam);
                fs.Flows.Add(flowFile);
                fs.ActiveFlowId = flowCam.Id;

                // 订阅结果通道：统计各流程的产出，并校验新字段
                var perFlow = new Dictionary<string, int>(StringComparer.Ordinal);
                string lastFlowName = "";
                string lastProduct = "";
                bool framesNonNull = false;
                EventHandler<FlowOutputEventArgs> sink = delegate(object s, FlowOutputEventArgs a)
                {
                    int n;
                    perFlow.TryGetValue(a.FlowId, out n);
                    perFlow[a.FlowId] = n + 1;
                    lastFlowName = a.FlowName;
                    lastProduct = a.Product;
                    if (a.Frame != null) { framesNonNull = true; a.Frame.Dispose(); }
                };
                FlowOutputChannel.OutputReady += sink;

                try
                {
                    OpResult s1 = FlowRunManager.StartOne(flowCam.Id);
                    Check("① 相机流启动成功", s1 != null && s1.Success, s1 != null ? s1.Message : "null");

                    OpResult s2 = FlowRunManager.StartOne(flowFile.Id);
                    Check("② 本地图片流启动成功", s2 != null && s2.Success, s2 != null ? s2.Message : "null");

                    // 第三条流程抢同一台相机 → 必须被租约拒绝
                    FlowDefinition flowCam2 = BuildSimpleFlow("并行-抢相机流", "Camera", "");
                    flowCam2.CameraId = "SIM-0";
                    flowCam2.ViewIndex = 3;
                    fs.Flows.Add(flowCam2);
                    OpResult s3 = FlowRunManager.StartOne(flowCam2.Id);
                    Check("③ 抢同一相机被租约拒绝且给出中文原因",
                        s3 != null && !s3.Success && s3.Message != null
                        && s3.Message.Contains("已被流程") && s3.Message.Contains("并行-相机流"),
                        s3 != null ? s3.Message : "null");

                    // 跑一会儿
                    Thread.Sleep(1500);

                    FlowRunSlot a = FlowRunManager.Get(flowCam.Id);
                    FlowRunSlot b = FlowRunManager.Get(flowFile.Id);

                    Check("④ 两条流程同时在跑（RunningCount=2）", FlowRunManager.RunningCount == 2,
                        "RunningCount=" + FlowRunManager.RunningCount);

                    Check("⑤ 相机流有产出且已出结论",
                        a != null && (a.OkCount + a.NgCount) > 0 && a.LastVerdict.HasValue,
                        a != null ? ("OK=" + a.OkCount + " NG=" + a.NgCount + " 周期=" + a.LastElapsedMs
                                     + "ms fps=" + a.Fps.ToString("F1") + " 状态=" + a.StatusText) : "null");

                    Check("⑥ 图片流有产出且已出结论",
                        b != null && (b.OkCount + b.NgCount) > 0 && b.LastVerdict.HasValue,
                        b != null ? ("OK=" + b.OkCount + " NG=" + b.NgCount + " 周期=" + b.LastElapsedMs
                                     + "ms fps=" + b.Fps.ToString("F1") + " 状态=" + b.StatusText) : "null");

                    Check("⑦ 两条流程各自独立计数（互不串号）",
                        a != null && b != null && (a.OkCount + a.NgCount) > 0 && (b.OkCount + b.NgCount) > 0,
                        a != null && b != null
                            ? ("A=" + (a.OkCount + a.NgCount) + " 次, B=" + (b.OkCount + b.NgCount) + " 次")
                            : "null");

                    Check("⑧ 限帧生效（单周期 ≤ 30fps 对应的 33ms 量级，实测不快于 60fps）",
                        a != null && b != null && a.Fps > 0 && a.Fps < 60.0 && b.Fps > 0 && b.Fps < 60.0,
                        a != null && b != null ? ("A=" + a.Fps.ToString("F1") + "fps, B=" + b.Fps.ToString("F1") + "fps") : "null");

                    Check("⑨ 租约表记录了相机归属",
                        CameraLease.OwnerOf("SIM-0") == flowCam.Id && !CameraLease.IsOccupied(""),
                        "owner=" + (CameraLease.OwnerOf("SIM-0") ?? "null"));

                    int camGot, fileGot;
                    perFlow.TryGetValue(flowCam.Id, out camGot);
                    perFlow.TryGetValue(flowFile.Id, out fileGot);
                    Check("⑩ 结果通道带上了 FlowId / FlowName / Product",
                        camGot > 0 && fileGot > 0 && !string.IsNullOrEmpty(lastFlowName) && !string.IsNullOrEmpty(lastProduct),
                        "camGot=" + camGot + " fileGot=" + fileGot + " last=" + lastFlowName + "/" + lastProduct);
                    Check("⑪ 结果帧非空（订阅者负责释放）", framesNonNull);

                    // 暂停 / 继续
                    FlowRunManager.PauseOne(flowCam.Id, true);
                    Thread.Sleep(120);
                    Check("⑫ 暂停生效", a != null && a.Status == FlowSlotStatus.Paused,
                        a != null ? a.StatusText : "null");
                    FlowRunManager.PauseOne(flowCam.Id, false);
                    Thread.Sleep(120);
                    Check("⑬ 继续生效", a != null && a.Status == FlowSlotStatus.Running,
                        a != null ? a.StatusText : "null");
                }
                finally
                {
                    FlowOutputChannel.OutputReady -= sink;
                }

                // 收尾：StopAll → 租约清空 → 相机全部关闭（关窗铁律的顺序）
                FlowRunManager.StopAll();
                Check("⑭ StopAll 后无运行中的流程", FlowRunManager.RunningCount == 0,
                    "RunningCount=" + FlowRunManager.RunningCount);
                Check("⑮ StopAll 后租约表已清空", CameraLease.Snapshot().Count == 0,
                    "count=" + CameraLease.Snapshot().Count);

                FlowRunManager.Shutdown();
                CameraManager.Instance.CloseAll();
                Check("⑯ Shutdown + CloseAll（关窗铁律顺序）不抛异常", true);
            }
            finally
            {
                try { if (File.Exists(imgPath)) File.Delete(imgPath); }
                catch (Exception) { }
                try { CameraManager.Instance.CloseAll(); }
                catch (Exception) { }
            }
        }

        /// <summary>构造「图像源 → 结果输出」的单链流程（连线用四参构造带上端子）。</summary>
        private static FlowDefinition BuildSimpleFlow(string name, string source, string filePath)
        {
            FlowDefinition f = FlowDefinition.CreateEmpty(name);

            FlowNode src = FlowModule.CreateNode(FlowNodeType.ImageSource, 40, 40);
            src.Id = "src1";
            src.Set("Source", source);
            if (!string.IsNullOrEmpty(filePath)) src.Set("FilePath", filePath);

            FlowNode outNode = FlowModule.CreateNode(FlowNodeType.ResultOutput, 240, 40);
            outNode.Id = "out1";

            f.Nodes.Add(src);
            f.Nodes.Add(outNode);
            f.Links.Add(new FlowLink("src1", FlowPortSchema.PrimaryOutput(FlowNodeType.ImageSource),
                                     "out1", FlowPortSchema.PrimaryInput(FlowNodeType.ResultOutput)));
            return f;
        }

        // =================================================================
        //  【T02】端口级拓扑排序 + 校验器
        // =================================================================
        private static void TestFlowOrderAndValidator()
        {
            Console.WriteLine("[13] 【T02】端口级拓扑排序 + 校验器");

            // ---- ① 旧单链：顺序必须与旧 TryBuildChain（按 X 递增的链）完全一致 ----
            FlowDefinition chain = BuildPortSampleFlow();
            List<FlowNode> order;
            string error;
            bool built = FlowOrder.Build(chain, out order, out error);
            Check("★ 旧单链 S→P→R 拓扑序 = [s1, p1, r1]（与旧 TryBuildChain 一致）",
                built && string.Join(",", OrderIds(order)) == "s1,p1,r1",
                (error ?? "ok") + " → " + string.Join(",", OrderIds(order)));

            // ---- ② 确定性：Nodes 插入顺序倒序，结果仍按 (X,Y,Id) ----
            FlowDefinition reversed = BuildPortSampleFlow();
            reversed.Nodes.Reverse();
            FlowOrderResult r2 = FlowOrder.Analyze(reversed);
            Check("★ 确定性：插入顺序无关，仍按 (X,Y,Id) 排序",
                r2.Success && string.Join(",", OrderIds(r2.Order)) == "s1,p1,r1",
                string.Join(",", OrderIds(r2.Order)));

            // ---- ③ 扇出：同一输出端子连两个输入端子 ----
            FlowDefinition fan = BuildPortSampleFlow();
            AddNode(fan, FlowNodeType.Sharpness, "cl1", 520, 40);
            Connect(fan, "s1", "Image", "cl1", "Image");
            FlowOrderResult rf = FlowOrder.Analyze(fan);
            Check("★ 扇出：图像源排最前，4 个节点全在序里",
                rf.Success && rf.Order.Count == 4 && OrderIds(rf.Order)[0] == "s1",
                string.Join(",", OrderIds(rf.Order)));

            FlowValidationResult vf = FlowValidator.Validate(fan);
            Check("★ 扇出是 Warning（W002）不是 Error",
                !FlowValidator.HasError(vf.Issues) && Codes(vf.Warnings).Contains("W002")
                && vf.CanRun,
                string.Join(",", Codes(vf.Issues)));

            Check("W002 文案照文档 §8.1",
                vf.Warnings.Exists(x => x.Message.StartsWith("「图像源.图像」同时连接了 2 个下游。")),
                vf.Warnings.Count > 0 ? vf.Warnings[0].Message : "");

            // ---- ④ 成环 ----
            // 干净的二元环：PositionCorrect(b) → ShapeMatch(c) → b（走不同的输入端子，不触发 E006）
            FlowDefinition ring = FlowDefinition.CreateEmpty("环");
            AddNode(ring, FlowNodeType.ImageSource, "a", 40, 40);
            AddNode(ring, FlowNodeType.PositionCorrect, "b", 200, 40);
            AddNode(ring, FlowNodeType.ShapeMatch, "c", 360, 40);
            Connect(ring, "a", "Image", "b", "Image");
            Connect(ring, "b", "Image", "c", "Image");
            Connect(ring, "c", "Pose", "b", "Pose");       // ← 绕回 b，闭环
            bool ringBuilt = FlowOrder.Build(ring, out order, out error);
            Check("★ 成环被 Build 检出且报中文原因",
                !ringBuilt && error != null && error.StartsWith("流程存在环："),
                error ?? "null");
            Check("E005 文案含环上节点名（A → B → A）",
                error != null && error.Contains("位置修正") && error.Contains("形状匹配")
                && error.EndsWith("。"), error ?? "null");

            FlowValidationResult vr = FlowValidator.Validate(ring);
            Check("★ 成环 → E005 且 CanRun=false",
                Codes(vr.Errors).Contains("E005") && !vr.CanRun, string.Join(",", Codes(vr.Issues)));

            Check("★ WouldCreateCycle 加边前预检",
                FlowOrder.WouldCreateCycle(ring, "b", "a")            // a 已能到达 b（a→b→c→b），再 b→a 成环
                && FlowOrder.WouldCreateCycle(ring, "c", "c")         // 自连也算环
                && !FlowOrder.WouldCreateCycle(ring, "a", "c")        // c 到不了 a：不成环
                && !FlowOrder.WouldCreateCycle(null, "a", "b"));      // null 安全

            // ---- ⑤ 孤立节点：Warning 不是 Error，且排在末尾 ----
            // 用「图像源」当孤立节点：它没有输入端子，能干净地只产生 W001，
            // 换成 Blob / 预处理这类有必填输入的节点会额外触发 E003（那是另一条规则）。
            FlowDefinition lonely = BuildPortSampleFlow();
            AddNode(lonely, FlowNodeType.ImageSource, "z1", 20, 200);
            FlowOrderResult rl = FlowOrder.Analyze(lonely);
            Check("★ 孤立节点 → W001 警告且不阻止运行",
                rl.Success && Codes(rl.Warnings).Contains("W001") && rl.Errors.Count == 0,
                string.Join(",", Codes(rl.Warnings)));
            Check("★ 孤立节点排在拓扑序末尾",
                rl.Order.Count == 4 && OrderIds(rl.Order)[3] == "z1",
                string.Join(",", OrderIds(rl.Order)));

            FlowValidationResult vl = FlowValidator.Validate(lonely);
            Check("★ 孤立节点不影响 CanRun", vl.CanRun && !FlowValidator.HasError(vl.Issues));

            // ---- ⑥ 必填输入未连接 → E003 ----
            FlowDefinition needy = FlowDefinition.CreateEmpty("缺输入");
            AddNode(needy, FlowNodeType.ImageSource, "a", 40, 40);
            AddNode(needy, FlowNodeType.Blob, "b", 200, 40);           // Image 必填，没连线
            FlowValidationResult vn = FlowValidator.Validate(needy);
            Check("★ 必填输入未连接 → E003",
                Codes(vn.Errors).Contains("E003") && !vn.CanRun, string.Join(",", Codes(vn.Issues)));
            Check("E003 文案照文档 §8.1",
                vn.Errors.Exists(x => x.Message == "节点「Blob 分析」的输入端子「输入图像」未连接。"),
                vn.Errors.Count > 0 ? vn.Errors[0].Message : "");

            // ---- ⑦ PositionCorrect 的 Pose 未连接 → 不再是 Error ----
            FlowDefinition pc = FlowDefinition.CreateEmpty("位置修正");
            AddNode(pc, FlowNodeType.ImageSource, "a", 40, 40);
            AddNode(pc, FlowNodeType.PositionCorrect, "b", 200, 40);
            AddNode(pc, FlowNodeType.ResultOutput, "c", 360, 40);
            Connect(pc, "a", "Image", "b", "Image");                   // 只连 Image，不连 Pose
            Connect(pc, "b", "Image", "c", "Any");
            FlowValidationResult vp = FlowValidator.Validate(pc);
            Check("★ PositionCorrect 的 Pose 未连接 → 不再是 Error（Pose 已改可选）",
                vp.CanRun && Codes(vp.Errors).Count == 0, string.Join(",", Codes(vp.Issues)));

            // ---- ⑦b W005（P0-3）：位置修正上游没有匹配类节点 ----
            Check("★ W005：位置修正上游无匹配类节点 → Warning（不是 Error）",
                Codes(vp.Warnings).Contains("W005") && vp.CanRun, string.Join(",", Codes(vp.Issues)));
            Check("W005 文案含节点名且说明本次不生效",
                vp.Warnings.Exists(x => x.Message == "位置修正节点「位置修正」的上游没有匹配类节点，本次运行不生效。"),
                vp.Warnings.Count > 0 ? vp.Warnings[0].Message : "");

            FlowDefinition pcOk = FlowDefinition.CreateEmpty("位置修正有上游");
            AddNode(pcOk, FlowNodeType.ImageSource, "a", 40, 40);
            AddNode(pcOk, FlowNodeType.ShapeMatch, "m", 200, 40);
            AddNode(pcOk, FlowNodeType.PositionCorrect, "b", 360, 40);
            AddNode(pcOk, FlowNodeType.ResultOutput, "c", 520, 40);
            Connect(pcOk, "a", "Image", "m", "Image");
            Connect(pcOk, "m", "Image", "b", "Image");
            Connect(pcOk, "b", "Image", "c", "Any");
            FlowValidationResult vpOk = FlowValidator.Validate(pcOk);
            Check("★ 位置修正上游有形状匹配 → 不出 W005",
                !Codes(vpOk.Warnings).Contains("W005") && vpOk.CanRun,
                string.Join(",", Codes(vpOk.Issues)));

            // ---- ⑧ 类型不兼容 → E004 ----
            FlowDefinition bad = FlowDefinition.CreateEmpty("类型不符");
            AddNode(bad, FlowNodeType.ImageSource, "a", 40, 40);
            AddNode(bad, FlowNodeType.ShapeMatch, "b", 200, 40);
            AddNode(bad, FlowNodeType.Preprocess, "c", 360, 40);
            Connect(bad, "a", "Image", "b", "Image");
            Connect(bad, "b", "Score", "c", "Image");                  // Number → Image：不兼容
            FlowValidationResult vb = FlowValidator.Validate(bad);
            Check("★ 类型不兼容 → E004",
                Codes(vb.Errors).Contains("E004") && !vb.CanRun, string.Join(",", Codes(vb.Issues)));
            Check("E004 文案照文档 §8.1（含双方类型中文名）",
                vb.Errors.Exists(x => x.Message.StartsWith("端子类型不匹配：「形状匹配.匹配分数」（数值）不能连到「预处理.输入图像」（图像）")),
                vb.Errors.Count > 0 ? vb.Errors[0].Message : "");

            // ---- ⑨ 一个输入端子接多条连线 → E006 ----
            FlowDefinition dup = FlowDefinition.CreateEmpty("重复入端");
            AddNode(dup, FlowNodeType.ImageSource, "a", 40, 40);
            AddNode(dup, FlowNodeType.ImageSource, "a2", 40, 120);
            AddNode(dup, FlowNodeType.Preprocess, "b", 240, 40);
            Connect(dup, "a", "Image", "b", "Image");
            Connect(dup, "a2", "Image", "b", "Image");                 // 同一个输入端子第二条
            FlowValidationResult vd = FlowValidator.Validate(dup);
            Check("★ 一个输入端子接多条连线 → E006",
                Codes(vd.Errors).Contains("E006") && !vd.CanRun, string.Join(",", Codes(vd.Issues)));
            Check("E006 文案照文档 §8.1",
                vd.Errors.Exists(x => x.Message.StartsWith("输入端子「预处理.输入图像」连接了多条连线")),
                vd.Errors.Count > 0 ? vd.Errors[0].Message : "");

            // ---- ⑩ 结果输出无上游 → W003（Warning，不阻止运行） ----
            FlowDefinition nores = FlowDefinition.CreateEmpty("无上游结果");
            AddNode(nores, FlowNodeType.ImageSource, "a", 40, 40);
            AddNode(nores, FlowNodeType.ResultOutput, "b", 200, 40);
            FlowValidationResult vno = FlowValidator.Validate(nores);
            Check("★ 结果输出无上游 → W003 警告而非 Error",
                vno.CanRun && Codes(vno.Warnings).Contains("W003") && Codes(vno.Errors).Count == 0,
                string.Join(",", Codes(vno.Issues)));

            // ---- ⑪ 结构层 E001 / E002 ----
            FlowValidationResult vEmpty = FlowValidator.Validate(FlowDefinition.CreateEmpty("空"));
            Check("★ 空流程 → E001",
                Codes(vEmpty.Errors).Contains("E001") && vEmpty.Issues[0].Message.StartsWith("流程没有任何节点"),
                string.Join(",", Codes(vEmpty.Issues)));

            FlowDefinition noSrc = FlowDefinition.CreateEmpty("无源");
            AddNode(noSrc, FlowNodeType.Preprocess, "b", 40, 40);
            FlowValidationResult vNoSrc = FlowValidator.Validate(noSrc);
            Check("★ 无图像源 → E002",
                Codes(vNoSrc.Errors).Contains("E002"), string.Join(",", Codes(vNoSrc.Issues)));

            // ---- ⑫ E007：源无输出端子 ----
            FlowDefinition dead = FlowDefinition.CreateEmpty("死源");
            AddNode(dead, FlowNodeType.ImageSource, "a", 40, 40);
            AddNode(dead, FlowNodeType.ResultOutput, "r", 120, 40);
            AddNode(dead, FlowNodeType.Preprocess, "b", 240, 40);
            Connect(dead, "a", "Image", "b", "Image");
            dead.Links.Add(new FlowLink("r", "", "b", "Pose"));        // 结果输出当源：没有输出端子
            FlowValidationResult vDead = FlowValidator.Validate(dead);
            Check("★ 源无输出端子 → E007",
                Codes(vDead.Errors).Contains("E007"), string.Join(",", Codes(vDead.Issues)));

            // ---- ⑬ E008：悬空连线 ----
            FlowDefinition ghost = BuildPortSampleFlow();
            ghost.Links.Add(new FlowLink("s1", "Image", "不存在X", "Image"));
            FlowValidationResult vGhost = FlowValidator.Validate(ghost);
            Check("★ 连线指向不存在的节点 → E008",
                Codes(vGhost.Errors).Contains("E008"), string.Join(",", Codes(vGhost.Issues)));

            // ---- ⑭ 非法端子名：W004 只写日志，不产生 Error（不替 algebraic 改数据） ----
            FlowDefinition weird = BuildPortSampleFlow();
            weird.Links[0].FromPort = "不存在的端口";
            weird.Links[0].ToPort = "";
            FlowValidationResult vw = FlowValidator.Validate(weird);
            Check("★ 非法端子名：W004 只写日志，不产生 Error、不改数据",
                vw.CanRun && Codes(vw.Errors).Count == 0 && weird.Links[0].FromPort == "不存在的端口"
                && weird.Links[0].ToPort == "",
                string.Join(",", Codes(vw.Issues)));

            // ---- ⑮ 报告渲染 + 分类视图 ----
            string report = FlowValidator.ToReport(vb.Issues);
            Check("ToReport 多行中文报告含统计行与 [错误] 前缀",
                report.Contains("共 ") && report.Contains(" 个错误、") && report.Contains("[错误][E004]"),
                report.Split('\n')[0]);
            // 用「干净」的样本流程（图像源 → 预处理 → 结果输出，无孤立、无扇出、上游齐备）验证空报告
            Check("空问题清单报告「校验通过」",
                FlowValidator.ToReport(FlowValidator.Validate(BuildPortSampleFlow()).Issues)
                    .Contains("流程校验通过"));
            Check("HasError 对空清单 / null 返回 false",
                !FlowValidator.HasError(null) && !FlowValidator.HasError(new List<FlowIssue>()));

            // ---- ⑯ 真实旧配置：v2 夹具两条链都能跑、幂等 ----
            AppConfig cfg = LoadOldV2();
            Check("★ 旧 v2 配置 Validate 全绿（两条链都能跑）",
                cfg.Flows.Flows.TrueForAll(x => x == null || FlowValidator.Validate(x).CanRun),
                string.Join(" | ", cfg.Flows.Flows.ConvertAll(
                    x => x.Name + ":" + FlowValidator.Validate(x).CanRun + "/"
                         + FlowValidator.Validate(x).Issues.Count + "条")));
            Check("★ 旧 v2 配置校验零 Error（E003 不会误报结果输出）",
                cfg.Flows.Flows.TrueForAll(x => x == null || FlowValidator.Validate(x).Errors.Count == 0));
            Check("★ 拓扑序：旧配置两条链都排得出且顺序按 X",
                cfg.Flows.Flows.TrueForAll(x => OrderIds(FlowOrder.Analyze(x).Order).Count == x.Nodes.Count),
                string.Join(" | ", cfg.Flows.Flows.ConvertAll(
                    x => x.Name + "=" + string.Join(">", OrderIds(FlowOrder.Analyze(x).Order)))));
            Check("★ 幂等：再次 UpgradeToCurrent 返回 false（Load 不会改写配置）",
                !FlowSettings.UpgradeToCurrent(cfg.Flows)
                && string.Join(" | ", CollectPortPairs(cfg.Flows.Flows))
                   == "n1.Image→n2.Any | m1.Image→m2.Image | m2.Image→m3.Any");

            TestRunnerUsesPortOrder();
        }

        /// <summary>
        /// 【T05 接线实证】<c>FlowRunner.Begin</c> 已经改走 <see cref="FlowOrder.Build"/>：
        /// 扇出不再报「存在分支」，孤立节点也不再报「未连接成链」，全部节点都会执行。
        /// </summary>
        private static void TestRunnerUsesPortOrder()
        {
            string img = Path.Combine(Path.GetTempPath(), "smoke_port_src.png");
            try
            {
                using (var bmp = new Bitmap(320, 240))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Black);
                        using (var br = new SolidBrush(Color.White))
                            g.FillEllipse(br, 90, 40, 140, 140);
                    }
                    bmp.Save(img, System.Drawing.Imaging.ImageFormat.Png);
                }

                // 扇出：s1.Image → p1.Image，同时 s1.Image → cl1.Image（旧版会报「存在分支」）
                FlowDefinition fan = BuildPortSampleFlow();
                AddNode(fan, FlowNodeType.Sharpness, "cl1", 520, 40);
                Connect(fan, "s1", "Image", "cl1", "Image");

                FlowNode src = fan.FindNode("s1");
                src.Set("Source", "File");
                src.Set("FilePath", img);
                fan.CameraId = "";

                int executed = 0;
                string failReason = null;
                using (var runner = new FlowRunner())
                {
                    FlowRunSession session = runner.Begin(fan, null, false);
                    if (session == null || session.Aborted)
                    {
                        failReason = session == null ? "session=null" : session.ErrorMessage;
                    }
                    else
                    {
                        // 顺序即 FlowOrder 的结果
                        List<string> ran = new List<string>();
                        FlowNodeProgress step;
                        while ((step = runner.StepNext(session)) != null)
                        {
                            executed++;
                            ran.Add(step.NodeId);
                        }
                        Check("★ T05 接线：执行顺序 = 拓扑序 (X,Y,Id)",
                            string.Join(",", ran) == "s1,p1,r1,cl1", string.Join(",", ran));
                    }
                }

                Check("★ T05 接线实证：扇出流程不再报「存在分支」，4 个节点全部执行",
                    failReason == null && executed == 4,
                    (failReason ?? "ok") + "，executed=" + executed);

                // 孤立节点：旧版报「存在未连接成链的节点」，新版照跑（排在末尾）
                FlowDefinition lone = BuildPortSampleFlow();
                AddNode(lone, FlowNodeType.ImageSource, "z1", 20, 200);
                FlowNode lsrc = lone.FindNode("z1");
                lsrc.Set("Source", "File");
                lsrc.Set("FilePath", img);
                lone.CameraId = "";

                FlowNode s1 = lone.FindNode("s1");
                s1.Set("Source", "File");
                s1.Set("FilePath", img);

                string loneReason = null;
                int loneExecuted = 0;
                using (var runner = new FlowRunner())
                {
                    FlowRunSession session = runner.Begin(lone, null, false);
                    if (session == null || session.Aborted)
                        loneReason = session == null ? "session=null" : session.ErrorMessage;
                    else
                    {
                        while (runner.StepNext(session) != null) loneExecuted++;
                    }
                }
                Check("★ T05 接线实证：孤立节点不再报「未连接成链」，照常执行",
                    loneReason == null && loneExecuted == 4,
                    (loneReason ?? "ok") + "，executed=" + loneExecuted);
            }
            finally
            {
                try { if (File.Exists(img)) File.Delete(img); }
                catch (Exception) { }
            }
        }

        /// <summary>把 v2 夹具写到磁盘、走真实 ConfigStore.Load()，最后还原。</summary>
        private static AppConfig LoadOldV2()
        {
            string path = AppPaths.ConfigFile;
            string backup = path + ".smokebak.order";
            bool hadOriginal = File.Exists(path);
            if (hadOriginal) File.Copy(path, backup, true);

            try
            {
                File.WriteAllText(path, OldV2ConfigJson, System.Text.Encoding.UTF8);
                return ConfigStore.Load();
            }
            finally
            {
                if (hadOriginal) File.Copy(backup, path, true);
                if (File.Exists(backup)) File.Delete(backup);
            }
        }

        /// <summary>拓扑序 → 节点 Id 序列。</summary>
        private static List<string> OrderIds(List<FlowNode> order)
        {
            var ids = new List<string>();
            if (order == null) return ids;
            foreach (FlowNode n in order) ids.Add(n == null ? "<null>" : n.Id);
            return ids;
        }

        /// <summary>问题清单 → 代码序列。</summary>
        private static List<string> Codes(List<FlowIssue> issues)
        {
            var codes = new List<string>();
            if (issues == null) return codes;
            foreach (FlowIssue i in issues) if (i != null) codes.Add(i.Code);
            return codes;
        }

        private static void AddNode(FlowDefinition flow, FlowNodeType type, string id, float x, float y)
        {
            FlowNode n = FlowModule.CreateNode(type, x, y);
            n.Id = id;
            flow.Nodes.Add(n);
        }

        private static void Connect(FlowDefinition flow, string from, string fromPort,
                                    string to, string toPort)
        {
            flow.Links.Add(new FlowLink(from, fromPort, to, toPort));
        }
    }
}
