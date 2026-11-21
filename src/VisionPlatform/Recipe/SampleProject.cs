using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using VisionPlatform.Core;
using VisionPlatform.Flow;
using VisionPlatform.Vision;

namespace VisionPlatform.Recipe
{
    /// <summary>
    /// 内置示例工程：一套可直接加载、可直接改的「外部触发 + 精定位 + 结果回传」方案。
    /// <para>
    /// 【为什么用代码生成而不是仓库里放一份 JSON】示例的价值在于"格式一定对"。
    /// 手写 JSON 只要漏一个 <c>DataMember</c>、写错一个枚举字符串，加载时就退化成默认值，
    /// 而界面上看不出来（参数面板显示的是默认值，不是报错）。
    /// 这里全部用真实类型构造后走 <see cref="Json.SaveToFile{T}"/>，
    /// 与软件自己保存配置走同一条序列化路径 —— 生成的包必然能被自己读回来。
    /// </para>
    /// <para>
    /// 【现场怎么用】生成后打开「配方」页 → 选中该方案 → 加载 →
    /// 在弹出的「是否应用工程配置」里选「是」，通讯与流程会一起切换；
    /// 然后只改两处：通讯端点地址、模板 ROI。
    /// </para>
    /// </summary>
    public static class SampleProject
    {
        /// <summary>默认方案名（同时是方案目录名）。</summary>
        public const string DefaultName = "Demo_PrecisionLocate";

        /// <summary>示例流程的固定 Id（写进工程包后便于通讯任务按 Id 绑定）。</summary>
        public const string FlowId = "demo-precision-locate";

        /// <summary>在软件根目录（程序所在目录）生成示例方案。</summary>
        public static OpResult Create(string recipeName = DefaultName)
        {
            return Create(AppPaths.Root, recipeName);
        }

        /// <summary>
        /// 在指定根目录下生成示例方案：<c>&lt;root&gt;\Recipes\&lt;方案名&gt;\</c>。
        /// </summary>
        /// <param name="rootDir">程序根目录（含 Recipes 的那一层）。</param>
        /// <param name="recipeName">方案名；空则用 <see cref="DefaultName"/>。</param>
        public static OpResult Create(string rootDir, string recipeName)
        {
            if (string.IsNullOrWhiteSpace(rootDir)) return OpResult.Fail("根目录为空");
            if (string.IsNullOrWhiteSpace(recipeName)) recipeName = DefaultName;

            try
            {
                string dir = Path.Combine(rootDir, "Recipes", recipeName);
                Directory.CreateDirectory(dir);

                FlowDefinition flow = BuildFlow();
                CommSettings comm = BuildComm();
                Recipe recipe = BuildRecipe(recipeName, flow.Id);

                Json.SaveToFile(Path.Combine(dir, "recipe.json"), recipe);
                Json.SaveToFile(Path.Combine(dir, ProjectBundle.CommFile), comm);
                Json.SaveToFile(Path.Combine(dir, ProjectBundle.FlowsFile), new FlowSettings
                {
                    SchemaVersion = FlowSettings.CurrentSchemaVersion,
                    ActiveFlowId = flow.Id,
                    Flows = new List<FlowDefinition> { flow }
                });

                // 资产子目录先建好：现场把模板图片 / 字符库 / 模型拷进来即可，不用自己记路径
                foreach (string sub in RecipeStore.RecipeSubDirs)
                    Directory.CreateDirectory(Path.Combine(dir, sub));
                Directory.CreateDirectory(Path.Combine(dir, ProjectBundle.TemplateFolder, flow.Id));

                File.WriteAllText(Path.Combine(dir, "方案说明.md"), BuildReadme(recipe, comm, flow), Encoding.UTF8);

                Logger.Info("已生成示例工程：" + dir);
                return OpResult.Ok("示例工程已生成：" + dir);
            }
            catch (Exception ex)
            {
                return OpResult.Fail("生成示例工程失败：" + ex.Message);
            }
        }

        // =====================================================================
        //  配方头
        // =====================================================================

        private static Recipe BuildRecipe(string name, string flowId)
        {
            return new Recipe
            {
                Name = name,
                Product = "DEMO-PL",
                Note = "示例工程：外部信号触发 → 模板匹配 → 位置修正 → 形状匹配精定位 → 脚本拼坐标 → 结果回传。"
                     + "直接加载即可运行（绑定 SIM 相机）；现场只需改通讯地址与模板 ROI。",
                CameraId = "SIM-0",
                Modified = DateTime.Now,
                FlowIds = new List<string> { flowId },
                Inspection = new InspectionConfig()
            };
        }

        // =====================================================================
        //  通讯：一条 Modbus（PLC）+ 一条 TCP 服务端（MES）
        // =====================================================================

        private static CommSettings BuildComm()
        {
            var comm = new CommSettings { SchemaVersion = CommSettings.CurrentSchemaVersion };

            // ---- 链路 ----
            // Host 是**占位地址**：装到现场必须改成 PLC 的真实 IP（或本机模拟从站的 127.0.0.1）。
            // TimeoutMs 给 3000 而不是 1000：首次握手可能撞上交换机 STP / ARP 老化，
            // 1s 会把"慢但能通"误判成"连不上"；连不上的真实原因应由日志里的
            // 「超时」/「积极拒绝」两种文案区分，而不是靠缩短超时时间。
            comm.Endpoints.Add(new CommEndpointConfig
            {
                Name = "PLC",
                Kind = CommKind.ModbusTcp,
                Host = "192.168.1.10",
                Port = 502,
                SlaveId = 1,
                Enabled = true,
                AutoConnect = true,
                Framing = "None",
                TimeoutMs = 3000,
                ReconnectMs = 3000
            });

            comm.Endpoints.Add(new CommEndpointConfig
            {
                Name = "MES",
                Kind = CommKind.TcpServer,
                Host = "0.0.0.0",
                Port = 9100,
                SlaveId = 1,
                Enabled = true,
                AutoConnect = true,
                Framing = "None",
                TimeoutMs = 1000,
                ReconnectMs = 3000
            });

            // ---- 点位：协议无关的名称 → Modbus 保持寄存器地址 ----
            AddPoint(comm, "Trigger", "0", "Read", "Bool", 1, "上位机写 1 → 触发一次检测（边沿触发）");
            AddPoint(comm, "Heartbeat", "1", "Write", "UInt16", 1, "心跳计数；示例任务每秒写 1 次固定值 1");
            AddPoint(comm, "Busy", "10", "Write", "Bool", 1, "流程执行中");
            AddPoint(comm, "Ready", "11", "Write", "Bool", 1, "流程空闲可接料");
            AddPoint(comm, "Fault", "12", "Write", "Bool", 1, "流程报错 / 相机掉线");
            AddPoint(comm, "ResultOk", "20", "Write", "Bool", 1, "本次判定 OK");
            AddPoint(comm, "ResultNg", "21", "Write", "Bool", 1, "本次判定 NG");
            AddPoint(comm, "PosX", "30", "Write", "Int32", 2, "精定位 X（像素；经标定后可换机械坐标）");
            AddPoint(comm, "PosY", "32", "Write", "Int32", 2, "精定位 Y");
            AddPoint(comm, "PosAngle", "34", "Write", "Float", 2, "精定位角度（度，ABCD 字序）");

            // ---- 消息模板：发给 MES 的文本 ----
            // 注意：{Flow1.xxx} 是 CommTemplate 的占位符；JSON 自身的 { 后面不是标识符，不会被误替换。
            comm.Messages.Add(new CommMessageConfig
            {
                Name = "ResultJson",
                Endpoint = "MES",
                Mode = "Text",
                Format = "{\"ok\":{Flow1.Passed},\"ng\":{Flow1.NgCount},\"ms\":{Flow1.ElapsedMs},"
                       + "\"result\":\"{Flow1.Summary}\",\"t\":\"{Time}\"}",
                Terminator = "\\r\\n"
            });

            comm.Messages.Add(new CommMessageConfig
            {
                Name = "HeartbeatText",
                Endpoint = "MES",
                Mode = "Text",
                Format = "{\"hb\":\"{Time}\"}",
                Terminator = "\\r\\n"
            });

            // ---- 常驻任务 ----
            var heartbeat = new CommJobConfig
            {
                Name = "心跳",
                Enabled = true,
                Trigger = "Timer",
                IntervalMs = 1000,
                Flows = new List<string>(),
                Mode = "Latest",
                WindowMs = 500,
                Point = "",
                Expect = "true",
                Actions = new List<CommActionConfig>()
            };
            heartbeat.Actions.Add(new CommActionConfig
            {
                Kind = "SetPoint", Target = "PLC.Heartbeat", Value = "1", TimeoutMs = 500
            });
            heartbeat.Actions.Add(new CommActionConfig
            {
                Kind = "SendMessage", Target = "HeartbeatText", TimeoutMs = 500
            });
            comm.Jobs.Add(heartbeat);

            var result = new CommJobConfig
            {
                Name = "结果回传",
                Enabled = true,
                Trigger = "ResultGroup",
                IntervalMs = 1000,
                Flows = new List<string> { FlowId },
                Mode = "Latest",
                WindowMs = 500,
                Point = "",
                Expect = "true",
                Actions = new List<CommActionConfig>()
            };
            result.Actions.Add(new CommActionConfig
            {
                Kind = "SendMessage", Target = "ResultJson", TimeoutMs = 1000
            });
            comm.Jobs.Add(result);

            var trigger = new CommJobConfig
            {
                Name = "外部触发",
                Enabled = true,
                Trigger = "PointEdge",
                IntervalMs = 50,
                Flows = new List<string> { FlowId },
                Mode = "Latest",
                WindowMs = 500,
                Point = "Trigger",
                // 多流程分区：第 i 条流程读 Trigger + i*100（触发位 0 / 100 / 200 …）。
                // 只配这一条任务即可驱动全部流程，新增流程只要在「流程管理」里排好序。
                FlowStride = 100,
                Expect = "true",
                Actions = new List<CommActionConfig>()
            };
            trigger.Actions.Add(new CommActionConfig
            {
                Kind = "StartFlow", Target = FlowId, TimeoutMs = 1000
            });
            comm.Jobs.Add(trigger);

            // ---- 流程状态绑定：Busy / Ready / Fault 自动写点位 ----
            comm.FlowBindings.Add(new CommFlowBindingConfig
            {
                Flow = FlowId,
                Endpoint = "PLC",
                BusyPoint = "Busy",
                ReadyPoint = "Ready",
                FaultPoint = "Fault",
                Enabled = true
            });

            return comm;
        }

        private static void AddPoint(CommSettings comm, string name, string address,
                                     string direction, string type, int length, string desc)
        {
            comm.Points.Add(new CommPointConfig
            {
                Name = name,
                Endpoint = "PLC",
                Address = address,
                Direction = direction,
                Type = type,
                Length = length,
                WordOrder = "ABCD",
                Scale = 1.0,
                Offset = 0.0,
                Default = "",
                Description = desc
            });
        }

        // =====================================================================
        //  流程：6 个节点 + 5 条连线
        // =====================================================================

        private static FlowDefinition BuildFlow()
        {
            var flow = new FlowDefinition
            {
                Id = FlowId,
                Name = "精定位检测线",
                CameraId = "SIM-0",
                ViewIndex = 1,
                Product = "DEMO-PL",
                Enabled = true,
                // 示例取 Continue：即使某步 NG 也要跑完，才能把坐标一起回传出去
                NgMode = FlowDefinition.NgModeContinue,
                FrameTimeoutMs = 2000,
                Nodes = new List<FlowNode>(),
                Links = new List<FlowLink>()
            };

            float y = 140f;
            FlowNode img = Node("n01", FlowNodeType.ImageSource, "01 图像采集", 70f, y);
            img.Set("Source", "Camera");
            img.Set("TriggerMode", "Hardware");      // 外部硬触发；无相机时改 Continuous 用 SIM 跑
            img.Set("TimeoutMs", 3000);

            FlowNode tpl = Node("n02", FlowNodeType.TemplateMatch, "02 模板匹配(粗定位)", 290f, y);
            tpl.Set("MinScore", 0.70);
            tpl.Set("MaxMatches", 1);
            tpl.Set("AngleStart", -5);
            tpl.Set("AngleEnd", 5);
            tpl.Set("AngleStep", 1);
            tpl.Set("PyramidLevels", 3);

            FlowNode pos = Node("n03", FlowNodeType.PositionCorrect, "03 位置修正", 510f, y);
            pos.Set("RefSource", "Auto");
            pos.Set("EnableAngle", true);
            pos.Set("Follow", "Roi");                // 下游 ROI 跟着工件走，图像不重采样
            pos.Set("MaxShiftX", 200);
            pos.Set("MaxShiftY", 200);
            pos.Set("MaxAngle", 15);

            FlowNode sha = Node("n04", FlowNodeType.ShapeMatch, "04 形状匹配(精定位)", 730f, y);
            sha.Set("MinScore", 0.70);
            sha.Set("MaxMatches", 1);
            sha.Set("AngleStart", -3);
            sha.Set("AngleEnd", 3);
            sha.Set("AngleStep", 0.5);
            sha.Set("PyramidLevels", 4);
            sha.Set("Subpixel", true);

            FlowNode scr = Node("n05", FlowNodeType.Script, "05 输出脚本", 950f, y);
            scr.Set("Source", CoordinateScript);
            scr.Set("OutDefs", "PosX,PosY,PosA");

            FlowNode snd = Node("n06", FlowNodeType.SendData, "06 输出信号", 1170f, y);
            snd.Set("CommName", "MES");
            snd.Set("AutoConnect", true);
            snd.Set("DataMode", "Format");
            // {OkNg} = OK/NG，{Summary} = 上游摘要（脚本节点已把坐标拼在摘要最前面）
            snd.Set("Format", "{OkNg},{Summary}");

            flow.Nodes.Add(img);
            flow.Nodes.Add(tpl);
            flow.Nodes.Add(pos);
            flow.Nodes.Add(sha);
            flow.Nodes.Add(scr);
            flow.Nodes.Add(snd);

            // 端子留空：加载时 FlowSettings.UpgradeToCurrent 会按节点类型补主输入/主输出，
            // 手写端子 Key 反而容易写错（写错会被判为非法端子再换回默认）。
            flow.Links.Add(new FlowLink(img.Id, tpl.Id));
            flow.Links.Add(new FlowLink(tpl.Id, pos.Id));
            flow.Links.Add(new FlowLink(pos.Id, sha.Id));
            flow.Links.Add(new FlowLink(sha.Id, scr.Id));
            flow.Links.Add(new FlowLink(scr.Id, snd.Id));

            return flow;
        }

        private static FlowNode Node(string id, FlowNodeType type, string name, float x, float y)
        {
            return new FlowNode
            {
                Id = id,
                Type = type.ToString(),
                Name = name,
                X = x,
                Y = y,
                Params = new List<ParamItem>()
            };
        }

        /// <summary>
        /// 脚本节点的方法体：把精定位位姿拼成「X=…,Y=…,A=…」写在摘要最前面，
        /// 下游 SendData 用 <c>{Summary}</c> 就能把坐标随结果一起发出去。
        /// </summary>
        private const string CoordinateScript =
            "var m = api.Nodes.FindLast(n => n.Type == \"ShapeMatch\");\n" +
            "if (m == null) m = api.Nodes.FindLast(n => n.HasPose);\n" +
            "if (m == null) return \"未取得位姿\";\n" +
            "api.Set(\"PosX\", m.Cx);\n" +
            "api.Set(\"PosY\", m.Cy);\n" +
            "api.Set(\"PosA\", m.Angle);\n" +
            "return string.Format(\"X={0:F2},Y={1:F2},A={2:F2},Score={3:F3}\", m.Cx, m.Cy, m.Angle, m.TopScore);";

        // =====================================================================
        //  方案说明（随包交付，现场不用另外找文档）
        // =====================================================================

        private static string BuildReadme(Recipe recipe, CommSettings comm, FlowDefinition flow)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# " + recipe.Name + " — 示例工程说明");
            sb.AppendLine();
            sb.AppendLine("> 由软件内置示例生成器产出（`SampleProject.Create`）。");
            sb.AppendLine("> 加载方式：「配方」页选中本方案 → 加载 → 提示「是否应用工程配置」时选**是**。");
            sb.AppendLine();
            sb.AppendLine("## 一、这个方案做什么");
            sb.AppendLine();
            sb.AppendLine("外部信号触发一次检测，流程跑完后把 OK/NG 与精定位坐标回传给上位机。");
            sb.AppendLine();
            sb.AppendLine("| 环节 | 实现 |");
            sb.AppendLine("|---|---|");
            sb.AppendLine("| 触发 | PLC 写 `Trigger=1` → 常驻任务「外部触发」（PointEdge）**触发流程跑一轮**（不是启动连续循环） |");
            sb.AppendLine("| 心跳 | 常驻任务「心跳」每 1000 ms 写 `PLC.Heartbeat` 并发一条 MES 文本 |");
            sb.AppendLine("| 结果 | 常驻任务「结果回传」（ResultGroup）把 JSON 发给 MES |");
            sb.AppendLine("| 坐标 | 流程内脚本节点拼进摘要，随 `{Summary}` 一起发出 |");
            sb.AppendLine("| 状态 | FlowBindings 自动写 Busy / Ready / Fault |");
            sb.AppendLine();
            sb.AppendLine("## 二、流程节点");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine("01 图像采集 → 02 模板匹配(粗定位) → 03 位置修正 → 04 形状匹配(精定位) → 05 输出脚本 → 06 输出信号");
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("| 节点 | 类型 | 关键参数 |");
            sb.AppendLine("|---|---|---|");
            foreach (FlowNode n in flow.Nodes)
                sb.AppendLine("| " + n.Name + " | `" + n.Type + "` | " + KeyParams(n) + " |");
            sb.AppendLine();
            sb.AppendLine("- **NG 策略 = Continue**：某步 NG 也要跑完，否则坐标拿不到。");
            sb.AppendLine("- **整帧超时 = 2000 ms**：超时则剩余节点记 Skipped 且整帧判 NG。");
            sb.AppendLine("- 图像源默认 `Hardware`（外部硬触发）。无相机调试时改成 `Continuous`，绑定 SIM 相机即可跑。");
            sb.AppendLine("- **06 输出信号**：输入 / 输出都是文本（不接图像）。输入接上游文本，输出是实际发出的内容。");
            sb.AppendLine("  **发送目标与「发送方式」必须配对**（配错会整帧 NG，且对端一个字节都收不到）：");
            sb.AppendLine("  - 目标 = `MES`（TCP 客户端 / 服务端）→ 方式选「固定文本」或「格式文本」（本示例就是这种）；");
            sb.AppendLine("  - 目标 = `PLC`（Modbus TCP）→ 方式**必须**选「Modbus 写寄存器」：");
            sb.AppendLine("    单个固定值用「寄存器地址 + 写入值」；");
            sb.AppendLine("    要写坐标 / 角度 / 结论就用**批量写**，例如");
            sb.AppendLine("    `OkNg:20:UInt16; PosX:30:Int32; PosY:32:Int32; PosA:34:Float32`");
            sb.AppendLine("    （源名取脚本节点的命名输出，`OkNg` 是内置源 = 本帧结论 OK=1/NG=0；Int32 / Float32 各占 2 个寄存器）。");
            sb.AppendLine("    把端口 `502` 填进「寄存器地址」是常见笔误（502 是端口号，不是寄存器号）。");
            sb.AppendLine("  `MES` 还没有客户端接入时，节点判 **Warn（黄色，不判 NG）**，摘要写「未发出（没有客户端连接），不判 NG」");
            sb.AppendLine("  —— 没发出去是链路条件问题，不是检测结论；要看到真的发出，需 MES 侧（或调试工具）作为 TCP 客户端连到 9100。");
            sb.AppendLine("- **运行态才动通讯**：自动连接 / 心跳 / 触发轮询 / Busy·Ready 写入只在主界面点「运行」后进行；");
            sb.AppendLine("  点「暂停」或「停止」后全部挂起（已建立的连接保活、不重连），维护期间不会空转刷线程。");
            sb.AppendLine("- **触发式运行（默认）**：软件启动 → 点「运行」→ 建链并让流程线程停在「等待触发」，");
            sb.AppendLine("  **不会自己空跑**；PLC 写 `Trigger=1`（上升沿）→ 只跑**一轮** → 回结果 → 回到等待下一轮。");
            sb.AppendLine("  上一轮还没跑完又来的触发会被**丢弃**（不排队，避免把过期结果回给 PLC）。");
            sb.AppendLine("  调试时可在「系统设置 → 运行模式」切成「连续运行」，此时流程会按帧率自己循环，仅用于离线验证。");
            sb.AppendLine();
            sb.AppendLine("## 三、通讯配置");
            sb.AppendLine();
            sb.AppendLine("### 链路");
            sb.AppendLine();
            sb.AppendLine("| 名称 | 协议 | 目标 | 用途 |");
            sb.AppendLine("|---|---|---|---|");
            foreach (CommEndpointConfig ep in comm.Endpoints)
                sb.AppendLine("| " + ep.Name + " | `" + ep.Kind + "` | " + ep.Host + ":" + ep.Port + " | "
                              + (ep.Name == "PLC" ? "触发 / 心跳 / 状态 / 结果标志位" : "结果 JSON 文本") + " |");
            sb.AppendLine();
            sb.AppendLine("### 点位（Modbus 保持寄存器）");
            sb.AppendLine();
            sb.AppendLine("| 点位 | 地址 | 方向 | 类型 | 说明 |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (CommPointConfig p in comm.Points)
                sb.AppendLine("| `" + p.Name + "` | " + p.Address + " | " + p.Direction + " | " + p.Type + " | " + p.Description + " |");
            sb.AppendLine();
            sb.AppendLine("### 消息模板");
            sb.AppendLine();
            foreach (CommMessageConfig m in comm.Messages)
            {
                sb.AppendLine("- `" + m.Name + "` → " + m.Endpoint + "：" + m.Format);
            }
            sb.AppendLine();
            sb.AppendLine("可用占位符：`{Flow1.Passed}` `{Flow1.OkNg}` `{Flow1.Summary}` `{Flow1.ElapsedMs}` "
                          + "`{Flow1.OkCount}` `{Flow1.NgCount}` `{Flow1.Yield}` `{Flow1.Fps}` "
                          + "`{OkCount}` `{NgCount}` `{Yield}` `{CycleId}` `{Time}` `{Date}`。");
            sb.AppendLine();
            sb.AppendLine("## 四、现场只改两处");
            sb.AppendLine();
            sb.AppendLine("1. **通讯地址（必须改）**：`PLC` 的 IP / 端口 / 从站号。示例里的 `192.168.1.10:502` 是**占位地址**，");
            sb.AppendLine("   现场不改就是「Modbus 连接超时」；手边没有 PLC 时先改 `127.0.0.1` 配合本机模拟从站跑通（见第五节）。");
            sb.AppendLine("   `MES` 监听端口 9100 一般不用改。");
            sb.AppendLine("2. **模板 ROI**：`02 模板匹配` 的 Template* / Search*，`04 形状匹配` 的 Template* / Search*；");
            sb.AppendLine("   再点各自面板上的「创建模板…」训练一次。");
            sb.AppendLine();
            sb.AppendLine("## 四之二、多流程怎么分区（一条任务驱动 N 条流程）");
            sb.AppendLine();
            sb.AppendLine("现场只需配**第 0 条流程**的点位，其余按序号自动偏移，规则固定为「基址 + 序号 × 步长」：");
            sb.AppendLine();
            sb.AppendLine("| 位置 | 参数 | 值 | 含义 |");
            sb.AppendLine("|---|---|---|---|");
            sb.AppendLine("| 通讯 → 任务 → 外部触发 | 多流程步长 | `100` | 第 i 条流程读 `Trigger + i×100` → HR:0 / HR:100 / HR:200 |");
            sb.AppendLine("| 流程 → 发送数据 | 多流程步长 | `100` | 第 i 条流程写 `MbMap 基址 + i×100` → 20/120/220 |");
            sb.AppendLine();
            sb.AppendLine("**序号就是「流程编排 → 流程管理」列表里的行号（从 0 开始）**，用 ▲▼ 可以上下调整；");
            sb.AppendLine("调整后 PLC 侧地址会跟着变，改完请核对一遍。");
            sb.AppendLine();
            sb.AppendLine("本机模拟从站已按同一约定实现：`p 0` 触发第 1 条、`p 1` 触发第 2 条、`p 2` 触发第 3 条，");
            sb.AppendLine("并会按 100 的区间分组打印每条流程写回来的寄存器（`flow 1 -> HR[120]=1 …`）。");
            sb.AppendLine();
            sb.AppendLine("## 五、Modbus 连不上怎么查（按日志原文对号入座）");
            sb.AppendLine();
            sb.AppendLine("| 日志 / 摘要原文 | 含义 | 处理 |");
            sb.AppendLine("|---|---|---|");
            sb.AppendLine("| `Modbus 连接超时：ip:502（3s 内无响应）` | TCP 握手**没有任何回应**：该 IP 上没有设备、不在同一网段，或中间防火墙丢包 | ① 在**装软件这台电脑**上 `ping <PLC-IP>`；② `arp -a` 看能否解析出该 IP 的 MAC；③ 确认 PLC 已上电并**单独使能了 Modbus TCP 服务**（很多 PLC 要在组态里开启以太网 / Modbus 服务器并允许远程访问）|");
            sb.AppendLine("| `Modbus 连接失败：由于目标计算机积极拒绝…（IP 可达但端口未监听）` | IP 通，但该端口没人监听 | 端口号填错，或 PLC 侧 Modbus 服务未启动 / 不是 502 |");
            sb.AppendLine("| `从站返回异常：功能码 0x03，异常码 0x02（非法数据地址…）` | **已经连上**，但寄存器地址超出 PLC 映射范围 | 按 PLC 地址表改 `HR:n`（PLC 侧有的从 0 计、有的从 40001 计，差 1 或差 40001 都可能）|");
            sb.AppendLine("| 节点摘要里的 `未连接 / 正在连接中 / 冷却中` | 运行态下链路暂时未就绪 | 不判 NG，等自动重连（`ReconnectMs` 3000）|");
            sb.AppendLine();
            sb.AppendLine("**没有 PLC 也能先把链路跑通**：仓库 `tools/comm/modbus_slave_sim.py` 是零依赖的本机 Modbus 从站模拟器");
            sb.AppendLine("（支持 0x01/0x03/0x05/0x06/0x10，全 ASCII 输出，不需要装任何库）：");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine("python tools\\modbus_slave_sim.py --port 502 --seed");
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("然后把 `PLC` 的**主机改成 `127.0.0.1`**、端口 502 → 点「运行」：心跳任务每秒写 HR[1]，");
            sb.AppendLine("模拟器窗口会逐条打印 `FC6  write HR[1] = 1`。看到这些打印就说明软件侧 Modbus 客户端完全正常，");
            sb.AppendLine("连不上时剩下的原因一定是 IP / 网段 / 防火墙 / PLC 侧服务未启用。");
            sb.AppendLine();
            sb.AppendLine("> 端口 502 被占用或绑定失败时改用 `--port 15020`，同时把端点端口改成 15020。");
            sb.AppendLine();
            sb.AppendLine("## 六、已知限制（先说清楚，免得现场踩）");
            sb.AppendLine();
            sb.AppendLine("- **坐标只走文本链路**：`SendMessage` 的占位符不含坐标，坐标来自脚本节点写进摘要的");
            sb.AppendLine("  `X=…,Y=…,A=…`。要把坐标写进 Modbus 寄存器，当前 `SendData` 的 ModbusWrite 只能写固定值，");
            sb.AppendLine("  需要 MES 侧解析文本后写入，或后续扩展「点位写入支持取上游输出」。");
            sb.AppendLine("- **心跳是固定值**：`SetPoint` 不支持 0/1 翻转，示例每秒写 1。PLC 侧若要边沿心跳，");
            sb.AppendLine("  请在 PLC 程序里按「收到周期报文」判活，或改用脚本节点计数。");
            sb.AppendLine("- **OCRModel / Models 不在包内**：体积可能上百 MB，`Export` 默认只带 `Templates`。");
            sb.AppendLine("  换机时把这两个目录一起拷过去即可。");
            sb.AppendLine();
            sb.AppendLine("## 七、目录内容");
            sb.AppendLine();
            sb.AppendLine("| 文件/目录 | 内容 |");
            sb.AppendLine("|---|---|");
            sb.AppendLine("| `recipe.json` | 算法参数 / 相机 / 绑定流程 |");
            sb.AppendLine("| `comm.json` | 端点 / 点位 / 消息 / 任务 / 流程状态绑定 |");
            sb.AppendLine("| `flows.json` | 流程节点 / 连线 / 绑定 |");
            sb.AppendLine("| `Templates/` | 模板资产（按 FlowId/NodeId 分层） |");
            sb.AppendLine("| `OCRModel/` `Models/` `Images/` | 字符库 / 模型 / 采集图的落点 |");
            sb.AppendLine("| `_backup/` | 导入前的原工程备份（自动生成） |");
            return sb.ToString();
        }

        private static string KeyParams(FlowNode n)
        {
            if (n.Params == null || n.Params.Count == 0) return "—";

            var parts = new List<string>();
            foreach (ParamItem p in n.Params)
            {
                if (p == null) continue;
                if (string.Equals(p.Key, "Source", StringComparison.Ordinal)
                    && string.Equals(n.Type, "Script", StringComparison.Ordinal))
                {
                    parts.Add("Source=（见 flows.json）");
                    continue;
                }
                string v = p.Value ?? "";
                if (v.Length > 24) v = v.Substring(0, 24) + "…";
                parts.Add("`" + p.Key + "=" + v + "`");
                if (parts.Count >= 4) break;
            }
            return string.Join(" ", parts.ToArray());
        }
    }
}
