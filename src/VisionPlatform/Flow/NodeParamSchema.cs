using System;
using System.Collections.Generic;
using VisionPlatform.Communication;

namespace VisionPlatform.Flow
{
    /// <summary>参数控件类型。</summary>
    public enum NodeParamKind
    {
        /// <summary>整数（NumericUpDown，Decimals=0）。</summary>
        Int,

        /// <summary>浮点数（NumericUpDown）。</summary>
        Double,

        /// <summary>布尔（CheckBox，存 1 / 0）。</summary>
        Bool,

        /// <summary>自由文本（TextBox）。</summary>
        Text,

        /// <summary>代码/多行文本（等宽字体多行 TextBox，高度较大）。</summary>
        Code,

        /// <summary>枚举（ComboBox 下拉，存选项的 Value）。</summary>
        Enum,

        /// <summary>文件路径（TextBox + 「浏览」按钮）。</summary>
        File,

        /// <summary>
        /// 动作行（一个按钮，<strong>不对应参数值</strong>）：点击由
        /// <c>NodeParamPanel.ActionInvoked</c> 把 <see cref="NodeParamField.Key"/> 抛给调用方。
        /// </summary>
        Action,

        /// <summary>分组标题（不对应参数）。</summary>
        Group,

        /// <summary>只读提示文本（不对应参数）。</summary>
        Note,

        /// <summary>
        /// 目录选择行（TextBox + 「浏览…」+「打开」）。
        /// <strong>末尾追加</strong>：枚举值向前兼容，已有序列化值不会错位。
        /// </summary>
        Folder
    }

    /// <summary>枚举选项：显示为 <see cref="NodeParamOption.Text"/>，落盘为 <see cref="NodeParamOption.Value"/>。</summary>
    public sealed class NodeParamOption
    {
        /// <summary>界面显示文本。</summary>
        public string Text { get; set; }

        /// <summary>实际存入 <see cref="ParamItem.Value"/> 的字符串。</summary>
        public string Value { get; set; }

        public NodeParamOption() { Text = ""; Value = ""; }

        public NodeParamOption(string text, string value)
        {
            Text = text;
            Value = value;
        }

        public override string ToString() { return Text; }
    }

    /// <summary>单个参数字段的声明。</summary>
    public sealed class NodeParamField
    {
        /// <summary>参数键（与 <see cref="FlowModuleInfo.CreateDefaultParams"/> 里的 Key 严格一致）。</summary>
        public string Key { get; set; }

        /// <summary>标签文本。</summary>
        public string Label { get; set; }

        /// <summary>控件类型。</summary>
        public NodeParamKind Kind { get; set; }

        /// <summary>默认值（字符串形态，InvariantCulture）。</summary>
        public string Default { get; set; }

        /// <summary>最小值（数值型）。</summary>
        public decimal Min { get; set; }

        /// <summary>最大值（数值型）。</summary>
        public decimal Max { get; set; }

        /// <summary>小数位数（0 = 整数）。</summary>
        public int Decimals { get; set; }

        /// <summary>步进。</summary>
        public decimal Increment { get; set; }

        /// <summary>控件高度（仅 Code 这类多行控件用；0 = 默认单行高）。</summary>
        public int Height { get; set; }

        /// <summary>枚举选项（Kind=Enum 时有效）。</summary>
        public List<NodeParamOption> Options { get; set; }

        /// <summary>提示文本（可空）。</summary>
        public string Hint { get; set; }

        /// <summary>
        /// 是否为「不常用」参数：true 时 <c>NodeParamPanel</c> 把它收进默认折叠的
        /// 「高级参数」区，避免参数多的节点（形状匹配 / 模板匹配 / Blob / 卡尺）一打开就是一长条。
        /// <para>它<strong>只影响界面归组</strong>，与 Key、默认值、取值范围、落盘完全无关，
        /// 折叠状态下参数照常读写。</para>
        /// </summary>
        public bool Advanced { get; set; }

        /// <summary>
        /// 禁用原因（空 / null = 可用）。目前只对 <see cref="NodeParamKind.Action"/> 有意义：
        /// 非空时按钮置灰不可点，tooltip 显示这段原因而不是 <see cref="Hint"/>。
        /// <para>
        /// 为什么要有它而不是"干脆不加这个 Action 行"：功能尚未实现时，
        /// <strong>看得见但明显不可用</strong>远好于"看得见、点得动、点了没反应"——
        /// 后者会被当成 bug 反馈，而且会和"上一版真的坏过的那个按钮"混在一起。
        /// </para>
        /// </summary>
        public string DisabledReason { get; set; }

        /// <summary>是否禁用（<see cref="DisabledReason"/> 非空即禁用）。</summary>
        public bool IsDisabled
        {
            get { return !string.IsNullOrEmpty(DisabledReason); }
        }

        public NodeParamField()
        {
            Key = "";
            Label = "";
            Kind = NodeParamKind.Int;
            Default = "0";
            Min = 0m;
            Max = 100000m;
            Decimals = 0;
            Increment = 1m;
            Options = new List<NodeParamOption>();
            Hint = "";
            DisabledReason = "";
        }

        /// <summary>是否为「不对应参数」的展示行（分组标题 / 提示文本）。</summary>
        public bool IsDecorative
        {
            get { return Kind == NodeParamKind.Group || Kind == NodeParamKind.Note; }
        }
    }

    /// <summary>一个节点类型的完整参数表。</summary>
    public sealed class NodeParamTable
    {
        /// <summary>节点类型。</summary>
        public FlowNodeType Type { get; set; }

        /// <summary>面板标题（一般取模块显示名）。</summary>
        public string Title { get; set; }

        /// <summary>字段列表（顺序即界面顺序）。</summary>
        public List<NodeParamField> Fields { get; set; }

        /// <summary>面板底部固定提示（如算法限制说明）。</summary>
        public string FooterNote { get; set; }

        public NodeParamTable()
        {
            Title = "";
            Fields = new List<NodeParamField>();
            FooterNote = "";
        }
    }

    /// <summary>
    /// 各类节点的<strong>声明式</strong>参数表：字段名 / 标签 / 控件类型 / 默认值 / 取值范围 / 提示文本。
    /// 界面由 <c>VisionPlatform.UI.NodeParamPanel</c> 按表动态生成，无需为每个节点手写面板。
    /// </summary>
    /// <remarks>
    /// 【Key 稳定性】这里的 Key 与 <see cref="FlowModuleInfo.CreateDefaultParams"/> 必须逐字一致；
    /// 改动既有 Key 会让旧配置里已保存的参数读不出来（PRD C3 向下兼容）。
    /// ShapeMatch 的 23 个 Key 系从旧 <c>ShapeMatchNodePanel</c> 原样迁入。
    /// </remarks>
    public static class NodeParamSchema
    {
        // ---------------------- 便捷构造 ----------------------

        // ⚠ 8 个辅助的 advanced 都是「可选参数且排在最后」：既有调用点不传即 false，
        //   逐字保持原有的 Key / 顺序 / 默认值，只追加标记。

        private static NodeParamField Group(string title, bool advanced = false)
        {
            return new NodeParamField { Kind = NodeParamKind.Group, Label = title, Advanced = advanced };
        }

        private static NodeParamField Note(string text, bool advanced = false)
        {
            return new NodeParamField { Kind = NodeParamKind.Note, Label = text, Advanced = advanced };
        }

        private static NodeParamField Int(string key, string label, decimal min, decimal max,
                                          string def, string hint = "", bool advanced = false)
        {
            return new NodeParamField
            {
                Key = key, Label = label, Kind = NodeParamKind.Int,
                Min = min, Max = max, Decimals = 0, Increment = 1m,
                Default = def, Hint = hint, Advanced = advanced
            };
        }

        private static NodeParamField Dbl(string key, string label, decimal min, decimal max,
                                          int decimals, decimal inc, string def,
                                          string hint = "", bool advanced = false)
        {
            return new NodeParamField
            {
                Key = key, Label = label, Kind = NodeParamKind.Double,
                Min = min, Max = max, Decimals = decimals, Increment = inc,
                Default = def, Hint = hint, Advanced = advanced
            };
        }

        private static NodeParamField Bool(string key, string label, string def,
                                           string hint = "", bool advanced = false)
        {
            return new NodeParamField
            {
                Key = key, Label = label, Kind = NodeParamKind.Bool, Default = def,
                Hint = hint, Advanced = advanced
            };
        }

        private static NodeParamField Text(string key, string label, string def,
                                           string hint = "", bool advanced = false)
        {
            return new NodeParamField
            {
                Key = key, Label = label, Kind = NodeParamKind.Text, Default = def,
                Hint = hint, Advanced = advanced
            };
        }

        private static NodeParamField File(string key, string label,
                                           string hint = "", bool advanced = false)
        {
            return new NodeParamField
            {
                Key = key, Label = label, Kind = NodeParamKind.File, Default = "",
                Hint = hint, Advanced = advanced
            };
        }

        /// <summary>
        /// 动作行：面板渲染成一个按钮，点击抛 <c>NodeParamPanel.ActionInvoked</c>（参数即 <paramref name="key"/>）。
        /// <para>动作行<strong>不落盘</strong>：它没有参数值，因此
        /// <see cref="FlowModuleInfo.CreateDefaultParams"/> 里<strong>不要</strong>为它补键 ——
        /// 补了就会在配置文件里留下一串永远为空的死键。</para>
        /// </summary>
        /// <summary>
        /// 目录选择行：与 <see cref="NodeParamKind.File"/> 的区别只在语义与浏览方式
        /// （文件用 <c>OpenFileDialog</c> 且过滤器写死图片格式，目录场景会被误导）。
        /// </summary>
        /// <summary>多行代码框（等宽字体）。</summary>
        private static NodeParamField Code(string key, string label, string def, int height = 160,
                                           bool advanced = false)
        {
            return new NodeParamField
            {
                Key = key, Label = label, Kind = NodeParamKind.Code, Default = def, Height = height,
                Hint = "", Advanced = advanced
            };
        }

        private static NodeParamField Folder(string key, string label, string def, string hint = "")
        {
            return new NodeParamField
            {
                Key = key, Label = label, Kind = NodeParamKind.Folder, Default = def, Hint = hint
            };
        }

        private static NodeParamField Action(string key, string label,
                                            string hint = "", bool advanced = false)
        {
            return new NodeParamField
            {
                Key = key, Label = label, Kind = NodeParamKind.Action, Default = "",
                Hint = hint, Advanced = advanced
            };
        }

        /// <summary>
        /// 动作行（<strong>禁用态</strong>）：功能尚未实现时用这个重载，按钮置灰、tooltip 显示原因。
        /// <para>第 4 参刻意是 <see cref="string"/> 而不是加一个可选的 bool ——
        /// 与上一个重载靠类型区分，调用方写 3 个参数时不会产生重载歧义。</para>
        /// </summary>
        private static NodeParamField Action(string key, string label,
                                            string hint, string disabledReason)
        {
            return new NodeParamField
            {
                Key = key, Label = label, Kind = NodeParamKind.Action, Default = "",
                Hint = hint, DisabledReason = disabledReason
            };
        }

        private static NodeParamField Enum(string key, string label, string def,
                                           string[] texts, string[] values,
                                           string hint = "", bool advanced = false)
        {
            var f = new NodeParamField
            {
                Key = key, Label = label, Kind = NodeParamKind.Enum, Default = def,
                Hint = hint, Advanced = advanced
            };
            for (int i = 0; i < texts.Length; i++)
                f.Options.Add(new NodeParamOption(texts[i], i < values.Length ? values[i] : texts[i]));
            return f;
        }

        // ---------------------- 各节点参数表 ----------------------

        /// <summary>按节点类型取参数表；无参数表的类型返回 null。</summary>
        public static NodeParamTable For(FlowNodeType type)
        {
            switch (type)
            {
                case FlowNodeType.ImageSource:     return ImageSource();
                case FlowNodeType.Preprocess:      return Preprocess();
                case FlowNodeType.PositionCorrect: return PositionCorrect();
                case FlowNodeType.ShapeMatch:      return ShapeMatch();
                case FlowNodeType.TemplateMatch:   return TemplateMatch();
                case FlowNodeType.Blob:            return Blob();
                case FlowNodeType.LineCaliper:     return LineCaliper();
                case FlowNodeType.CircleCaliper:   return CircleCaliper();
                case FlowNodeType.Sharpness:       return Sharpness();
                case FlowNodeType.CalibTransform:  return CalibTransformSchema();
                case FlowNodeType.DistortionCorrect: return DistortionCorrectSchema();
                case FlowNodeType.FitCircle:       return Fit("圆拟合");
                case FlowNodeType.FitLine:         return Fit("直线拟合");
                case FlowNodeType.ObjectDetect:    return ObjectDetect();
                case FlowNodeType.Ocr:             return Ocr();
                case FlowNodeType.SendData:        return SendData();
                case FlowNodeType.ReceiveData:     return ReceiveData();
                case FlowNodeType.Script:          return Script();
                case FlowNodeType.DlSeg:           return DlTable(FlowNodeType.DlSeg, "目标语义分割");
                case FlowNodeType.DlPose:          return DlTable(FlowNodeType.DlPose, "目标姿态估计");
                case FlowNodeType.DlDepth:         return DlTable(FlowNodeType.DlDepth, "目标深度估计");
                case FlowNodeType.OcrDl:           return OcrDlTable();
                default:                           return null;
            }
        }

        /// <summary>该类型是否有参数表（决定面板显示参数还是空态）。</summary>
        public static bool HasSchema(FlowNodeType type)
        {
            return For(type) != null;
        }

        /// <summary>取字段的默认值（找不到返回空串）。</summary>
        public static string DefaultOf(FlowNodeType type, string key)
        {
            NodeParamTable t = For(type);
            if (t == null || string.IsNullOrEmpty(key)) return "";

            foreach (NodeParamField f in t.Fields)
            {
                if (f != null && string.Equals(f.Key, key, StringComparison.Ordinal))
                    return f.Default;
            }
            return "";
        }

        // ---------------------- 12 类节点 ----------------------

        private static NodeParamTable ImageSource()
        {
            var t = new NodeParamTable { Type = FlowNodeType.ImageSource, Title = "图像源" };
            t.Fields.Add(Group("数据源"));
            t.Fields.Add(Enum("Source", "取图方式", "Camera",
                new[] { "流程绑定相机", "本地图片文件", "本地图片文件夹" }, new[] { "Camera", "File", "Folder" }));
            t.Fields.Add(File("FilePath", "图片路径", "仅「本地图片文件」时生效；留空则运行期提示"));
            t.Fields.Add(Folder("FolderPath", "图片文件夹", "",
                "仅「本地图片文件夹」时生效：按文件名顺序逐张取，每次运行 / 每个周期换下一张，到末尾回到第一张。"
                + "结果区会显示「第 i/N 张 · 文件名」"));
            t.Fields.Add(Int("FolderHoldMs", "每张停留(ms)", 0, 60000, "800",
                "0 = 每个周期换一张；>0 = 同一张至少停留这么久（连续运行时便于观察）"));
            t.Fields.Add(Bool("FolderPersist", "记住播放位置", "0",
                "关闭：位置只在本次软件运行内递增（重启回到第一张）；开启：退出后仍从上次的位置继续"));
            t.Fields.Add(Int("TimeoutMs", "抓帧超时(ms)", 100, 60000, "3000"));

            t.Fields.Add(Group("ROI 裁剪（宽或高为 0 = 全图）"));
            t.Fields.Add(Int("RoiX", "X", -100000, 100000, "0"));
            t.Fields.Add(Int("RoiY", "Y", -100000, 100000, "0"));
            t.Fields.Add(Int("RoiW", "宽", 0, 100000, "0"));
            t.Fields.Add(Int("RoiH", "高", 0, 100000, "0"));

            t.Fields.Add(Group("几何变换"));
            t.Fields.Add(Enum("Rotate", "旋转", "0",
                new[] { "0°", "90°", "180°", "270°" }, new[] { "0", "90", "180", "270" }));
            t.Fields.Add(Bool("Mirror", "水平镜像", "0"));

            t.FooterNote = "裁剪后的图像即为下游节点的工作帧；节点里的 ROI 坐标仍按原始图像填写。";
            return t;
        }

        private static NodeParamTable Preprocess()
        {
            var t = new NodeParamTable { Type = FlowNodeType.Preprocess, Title = "预处理" };
            t.Fields.Add(Group("灰度与滤波"));
            t.Fields.Add(Bool("ToGray", "转灰度", "1"));
            t.Fields.Add(Int("BlurKsize", "高斯核边长", 0, 31, "3", "奇数；< 3 表示不模糊"));

            t.Fields.Add(Group("二值化"));
            t.Fields.Add(Int("Threshold", "阈值", -1, 255, "128", "< 0 表示不二值化"));
            t.Fields.Add(Bool("Invert", "结果反转", "0"));

            t.Fields.Add(Group("形态学"));
            t.Fields.Add(Enum("MorphOp", "运算", "0",
                new[] { "无", "开运算", "闭运算" }, new[] { "0", "1", "2" }));
            t.Fields.Add(Int("MorphKsize", "核边长", 0, 31, "3", "奇数"));
            return t;
        }

        /// <summary>脚本节点的参数表。</summary>
        private static NodeParamTable Script()
        {
            var t = new NodeParamTable { Type = FlowNodeType.Script, Title = "脚本" };

            // 只有这一个入口：代码与输出变量都在编辑器里维护。
            // 参数表里**不放** Source / OutDefs（它们只是存储键，不在这里编辑），
            // 也不放任何说明文字 —— 用户明确要求"高级参数全部删掉"。
            t.Fields.Add(Action("EditScript", "编辑脚本…", ""));
            return t;
        }

        /// <summary>
        /// 深度学习节点的参数表（三个任务共用，只有阈值行按任务取舍）。
        /// 模型路径留空时运行期自动取 models&lt;任务&gt;\ 下第一个 .onnx。
        /// </summary>
        private static NodeParamTable DlTable(FlowNodeType type, string title)
        {
            var t = new NodeParamTable { Type = type, Title = title };

            t.Fields.Add(Group("模型"));
            t.Fields.Add(File("ModelPath", "模型文件(.onnx)",
                "留空则自动使用 models\\" + DlFolder(type) + "\\ 下的第一个 .onnx；"
                + "同目录可放 <模型名>.txt（每行一个类别名）"));
            t.Fields.Add(Enum("Backend", "推理后端", "OpenVINO",
                new[] { "OpenVINO", "CUDA（预留）" }, new[] { "OpenVINO", "CUDA" },
                "CUDA 接口已预留、尚未实现；选它会在运行期给出明确提示"));
            t.Fields.Add(Enum("Device", "推理设备", "CPU",
                new[] { "CPU", "GPU", "NPU" }, new[] { "CPU", "GPU", "NPU" },
                "OpenVINO 设备名；GPU/NPU 需对应的设备插件"));

            if (type != FlowNodeType.DlDepth)
            {
                t.Fields.Add(Group("检测参数"));
                t.Fields.Add(Dbl("ScoreThresh", "置信度阈值", 0, 1, 2, 0.05m, "0.25", "低于该分数丢弃"));
                t.Fields.Add(Dbl("IouThresh", "NMS IoU 阈值", 0, 1, 2, 0.05m, "0.45", "同类框重叠超此值合并"));
                t.Fields.Add(Int("MaxCount", "最多目标数", 1, 1000, "100"));
                t.Fields.Add(Int("MinCount", "最少目标数", 0, 1000, "0", "少于此数判 NG；0 = 不判"));
            }

            t.FooterNote = type == FlowNodeType.DlSeg
                ? "输出每个目标的框 + 掩膜面积（缺陷检测可在此基础上按面积/数量判定）"
                : type == FlowNodeType.DlPose
                    ? "输出每个目标的框 + 关键点（关键点以 4px 小方块叠加显示）"
                    : "深度图按 0~1 归一化输出，同时给出原始值域（Min/Max）";
            return t;
        }

        /// <summary>
        /// 深度学习 OCR（PaddleOCR）：两个模型 + 字典。
        /// 三个路径都留空时，运行期自动找 models/ocr 下的 det.onnx / rec.onnx / ppocr_keys.txt。
        /// </summary>
        private static NodeParamTable OcrDlTable()
        {
            var t = new NodeParamTable { Type = FlowNodeType.OcrDl, Title = "OCR(深度学习)" };

            t.Fields.Add(Group("模型（留空自动用 models\\ocr\\）"));
            t.Fields.Add(File("DetModel", "检测模型(.onnx)（可选）",
                "PaddleOCR DB 检测模型；留空用 models\\ocr\\det.onnx，若目录里没有则退化为「整图识别」（只用一个 rec 模型）"));
            t.Fields.Add(File("RecModel", "识别模型(.onnx)（必填）", "PaddleOCR 识别模型；留空用 models\\ocr\\rec.onnx"));
            t.Fields.Add(File("DictPath", "字符字典(.txt)（必填）", "ppocr_keys.txt；留空用 models\\ocr\\ppocr_keys.txt"));
            t.Fields.Add(Enum("Backend", "推理后端", "OpenVINO",
                new[] { "OpenVINO", "CUDA（预留）" }, new[] { "OpenVINO", "CUDA" }, ""));
            t.Fields.Add(Enum("Device", "推理设备", "CPU",
                new[] { "CPU", "GPU", "NPU" }, new[] { "CPU", "GPU", "NPU" }, ""));

            t.Fields.Add(Group("识别参数"));
            t.Fields.Add(Dbl("BoxThresh", "文本框阈值", 0, 1, 2, 0.05m, "0.30", "概率图二值化阈值"));
            t.Fields.Add(Dbl("UnclipRatio", "外扩比例", 0.5m, 5m, 1, 0.1m, "1.5", "文本框按短边外扩，越大框越松"));
            t.Fields.Add(Int("MaxLines", "最多文本行", 1, 1000, "100"));
            t.Fields.Add(Int("MinLines", "最少文本行", 0, 1000, "0", "少于此数判 NG；0 = 不判"));

            t.FooterNote = "与「识别」分类里的传统 OCR（模板匹配）是两套东西："
                         + "这里给的是自然场景/多字体文本，不需要建字符模板。";
            return t;
        }

        private static string DlFolder(FlowNodeType type)
        {
            return type == FlowNodeType.DlSeg ? "seg" : type == FlowNodeType.DlPose ? "pose" : "depth";
        }

        private static NodeParamTable PositionCorrect()
        {
            var t = new NodeParamTable { Type = FlowNodeType.PositionCorrect, Title = "位置修正" };
            t.Fields.Add(Group("基准来源"));
            t.Fields.Add(Enum("RefSource", "基准", "Auto",
                new[] { "自动（上游匹配结果）", "手动示教" }, new[] { "Auto", "Manual" }));
            t.Fields.Add(Dbl("RefX", "基准 X", -100000, 100000, 1, 1m, "0",
                "自动：首次运行自动填入；也可手改（改完即视为手动基准）"));
            t.Fields.Add(Dbl("RefY", "基准 Y", -100000, 100000, 1, 1m, "0", "同上"));
            t.Fields.Add(Dbl("RefAngle", "基准角(°)", -360, 360, 2, 0.5m, "0", "同上"));
            t.Fields.Add(Bool("EnableAngle", "启用旋转修正", "1", "关闭则只平移不旋转"));

            // 【基准自动填入】Auto 模式下首次运行把当前位姿写回下面三个字段（RefX/RefY/RefAngle），
            // 面板上就能看到"基准到底是多少"，也可以事后手改。RefSet=1 表示已示教。
            t.Fields.Add(Action("RetractBaseline", "重新示教基准",
                "清掉已示教基准，下一次运行会重新以当前位姿为准"));
            t.Fields.Add(Bool("RefSet", "基准已示教", "0", "自动维护：首次运行写入基准后置 1", true));

            t.Fields.Add(Group("下游 ROI 如何跟随"));
            t.Fields.Add(Enum("Follow", "跟随方式", "Roi",
                new[] { "ROI 偏移（默认，推荐）", "整图对齐（重采样）" }, new[] { "Roi", "Warp" },
                "ROI 偏移：下游节点自己的 ROI 按 dX/dY/dA 移动，图像不动 —— 框会跟着工件跑；"
                + "整图对齐：把偏差烘进像素（图像被拉回基准姿态），下游 ROI 保持原坐标，但会重采样整幅图"));
            t.Fields.Add(Note("默认「ROI 偏移」：位置修正只算出 (dX, dY, dA)，下游每个节点的 ROI "
                + "都按这个差值映射后再取图 —— 这样卡尺/OCR 的框在画面上会明显跟着工件移动。"
                + "「整图对齐」适合必须看「对齐后的图像」的场合（如灰度比对），代价是整幅重采样且有损。"));

            t.Fields.Add(Group("超差上限（0 = 不限）"));
            t.Fields.Add(Dbl("MaxShiftX", "X 偏移上限(px)", 0, 100000, 1, 1m, "0"));
            t.Fields.Add(Dbl("MaxShiftY", "Y 偏移上限(px)", 0, 100000, 1, 1m, "0"));
            t.Fields.Add(Dbl("MaxAngle", "角度上限(°)", 0, 360, 2, 0.5m, "0"));
            t.Fields.Add(Dbl("MaxScaleDev", "缩放偏差上限", 0, 10, 3, 0.05m, "0"));

            t.FooterNote = "自动基准在**首次运行**时示教并写回本节点，之后一直用它比较（重新示教请把「基准已示教」置 0）；"
                         + "换型号请在流程界面重新运行或清除基准缓存。修正结果只存在于运行期，不会写回节点参数。";
            return t;
        }

        /// <summary>形状匹配：23 个 Key 从旧 ShapeMatchNodePanel 原样迁入。</summary>
        private static NodeParamTable ShapeMatch()
        {
            var t = new NodeParamTable { Type = FlowNodeType.ShapeMatch, Title = "形状匹配" };

            t.Fields.Add(Group("模板区域"));
            t.Fields.Add(Int("TemplateX", "X", 0, 100000, "0"));
            t.Fields.Add(Int("TemplateY", "Y", 0, 100000, "0"));
            t.Fields.Add(Int("TemplateW", "宽", 0, 100000, "0"));
            t.Fields.Add(Int("TemplateH", "高", 0, 100000, "0"));
            t.Fields.Add(Note("0 = 整图；工件会移动时必须框选模板区域，否则匹配会漂移失败。"));

            t.Fields.Add(Group("训练"));
            t.Fields.Add(Dbl("Contrast", "对比度", 0, 255, 1, 1m, "30"));
            t.Fields.Add(Int("MaxFeatures", "最多特征点", 10, 100000, "1500", "", true));
            t.Fields.Add(Dbl("MinFeatureDist", "最小间距", 0, 100, 1, 0.5m, "2", "", true));

            t.Fields.Add(Group("搜索区域"));
            t.Fields.Add(Int("SearchX", "X", 0, 100000, "0"));
            t.Fields.Add(Int("SearchY", "Y", 0, 100000, "0"));
            t.Fields.Add(Int("SearchW", "宽", 0, 100000, "0"));
            t.Fields.Add(Int("SearchH", "高", 0, 100000, "0"));

            t.Fields.Add(Group("匹配"));
            t.Fields.Add(Dbl("AngleStart", "起始角(°)", -360, 360, 2, 1m, "-10"));
            t.Fields.Add(Dbl("AngleEnd", "结束角(°)", -360, 360, 2, 1m, "10"));
            t.Fields.Add(Dbl("AngleStep", "角度步长(°)", 0, 360, 3, 0.5m, "1", "", true));
            t.Fields.Add(Dbl("ScaleMin", "最小缩放", 0, 10, 4, 0.1m, "1"));
            t.Fields.Add(Dbl("ScaleMax", "最大缩放", 0, 10, 4, 0.1m, "1"));
            t.Fields.Add(Dbl("ScaleStep", "缩放步长", 0, 10, 4, 0.05m, "0", "0 = 不搜索缩放", true));
            t.Fields.Add(Int("PyramidLevels", "金字塔层数", 1, 8, "4", "", true));
            t.Fields.Add(Dbl("MinScore", "最小得分", 0, 1, 4, 0.05m, "0.7"));
            t.Fields.Add(Int("MaxMatches", "最多目标数", 1, 1000, "1"));
            t.Fields.Add(Dbl("MaxOverlap", "重叠阈值", 0, 1, 3, 0.05m, "0.5", "", true));
            t.Fields.Add(Dbl("Greediness", "贪婪系数", 0, 1, 3, 0.05m, "0.9", "0 = 关闭启发式（最慢最稳）", true));
            t.Fields.Add(Bool("Subpixel", "亚像素精化", "1", "", true));

            // 这一组刻意只有一行 Action：模板状态的唯一真源是 T03 的模板状态卡片。
            // 这里如果再加一行 Note 显示状态，就成了两处显示同一状态，刷新时机一旦不同步
            // 就会互相矛盾（卡片「已就绪」、Note 还写着「见上方卡片」），故不设。
            t.Fields.Add(Group("模板"));
            t.Fields.Add(Action("CreateTemplate", "创建模板…", "打开模板制作界面（取图 / 画 ROI / 训练 / 保存）"));
            return t;
        }

        private static NodeParamTable TemplateMatch()
        {
            var t = new NodeParamTable { Type = FlowNodeType.TemplateMatch, Title = "模板匹配(NCC)" };

            t.Fields.Add(Group("训练 ROI（宽或高为 0 = 整图）"));
            t.Fields.Add(Int("TemplateX", "X", 0, 100000, "0"));
            t.Fields.Add(Int("TemplateY", "Y", 0, 100000, "0"));
            t.Fields.Add(Int("TemplateW", "宽", 0, 100000, "0"));
            t.Fields.Add(Int("TemplateH", "高", 0, 100000, "0"));
            t.Fields.Add(Note("0 = 整图；工件会移动时必须框选模板区域，否则匹配会漂移失败。"));

            t.Fields.Add(Group("搜索 ROI（宽或高为 0 = 整图）"));
            t.Fields.Add(Int("SearchX", "X", 0, 100000, "0"));
            t.Fields.Add(Int("SearchY", "Y", 0, 100000, "0"));
            t.Fields.Add(Int("SearchW", "宽", 0, 100000, "0"));
            t.Fields.Add(Int("SearchH", "高", 0, 100000, "0"));

            t.Fields.Add(Group("判定"));
            t.Fields.Add(Dbl("MinScore", "最小得分", 0, 1, 3, 0.05m, "0.8"));
            t.Fields.Add(Int("MaxMatches", "最多目标数", 1, 1000, "1"));
            t.Fields.Add(Bool("Subpixel", "亚像素精化", "1", "", true));

            // 同上：模板状态由 T03 的卡片独占显示，这里只留动作行。
            t.Fields.Add(Group("模板"));
            t.Fields.Add(Action("CreateTemplate", "创建模板…", "打开模板制作界面（取图 / 画 ROI / 训练 / 保存）"));

            t.FooterNote = "仅支持平移匹配，不支持旋转/缩放；有旋转请用形状匹配。";
            return t;
        }

        private static NodeParamTable Blob()
        {
            var t = new NodeParamTable { Type = FlowNodeType.Blob, Title = "Blob 分析" };

            t.Fields.Add(Group("二值化"));
            t.Fields.Add(Enum("BinMode", "方式", "Inherit",
                new[] { "继承上游", "自动（Otsu）", "手动阈值" },
                new[] { "Inherit", "Otsu", "Manual" }));
            t.Fields.Add(Int("Threshold", "手动阈值", 0, 255, "128", "仅「手动阈值」时生效"));
            t.Fields.Add(Enum("Polarity", "统计对象", "Bright",
                new[] { "亮目标", "暗目标" }, new[] { "Bright", "Dark" }));

            t.Fields.Add(Group("ROI（宽或高为 0 = 整图）"));
            t.Fields.Add(Int("RoiX", "X", 0, 100000, "0"));
            t.Fields.Add(Int("RoiY", "Y", 0, 100000, "0"));
            t.Fields.Add(Int("RoiW", "宽", 0, 100000, "0"));
            t.Fields.Add(Int("RoiH", "高", 0, 100000, "0"));

            t.Fields.Add(Group("过滤（0 = 不限）"));
            t.Fields.Add(Dbl("MinArea", "最小面积", 0, 100000000, 0, 10m, "100"));
            t.Fields.Add(Dbl("MaxArea", "最大面积", 0, 100000000, 0, 10m, "0", "", true));
            t.Fields.Add(Dbl("MinWidth", "最小宽", 0, 100000, 0, 1m, "0", "", true));
            t.Fields.Add(Dbl("MinHeight", "最小高", 0, 100000, 0, 1m, "0", "", true));
            t.Fields.Add(Dbl("MinCircularity", "最小圆度", 0, 1, 3, 0.05m, "0"));
            t.Fields.Add(Note("圆度＝面积/外接矩形面积（圆≈0.785，矩形≈1.0）。"));
            t.Fields.Add(Int("MaxCount", "最多保留", 0, 1000, "50"));
            t.Fields.Add(Enum("SortBy", "排序", "Area", new[] { "面积降序" }, new[] { "Area" }, "", true));

            t.Fields.Add(Group("判定（0 = 不限）"));
            t.Fields.Add(Int("ExpectMin", "期望个数下限", 0, 1000, "0"));
            t.Fields.Add(Int("ExpectMax", "期望个数上限", 0, 1000, "0"));
            t.Fields.Add(Dbl("AreaMin", "单个面积下限", 0, 100000000, 0, 10m, "0"));
            t.Fields.Add(Dbl("AreaMax", "单个面积上限", 0, 100000000, 0, 10m, "0"));
            return t;
        }

        private static NodeParamTable LineCaliper()
        {
            var t = new NodeParamTable { Type = FlowNodeType.LineCaliper, Title = "直线卡尺" };

            t.Fields.Add(Group("扫描线段（图像坐标）"));
            t.Fields.Add(Dbl("X1", "起点 X", -100000, 100000, 1, 1m, "100"));
            t.Fields.Add(Dbl("Y1", "起点 Y", -100000, 100000, 1, 1m, "100"));
            t.Fields.Add(Dbl("X2", "终点 X", -100000, 100000, 1, 1m, "300"));
            t.Fields.Add(Dbl("Y2", "终点 Y", -100000, 100000, 1, 1m, "100"));

            t.Fields.Add(Group("找边"));
            t.Fields.Add(Int("CaliperCount", "卡尺个数", 1, 200, "10"));
            t.Fields.Add(Dbl("ProjectionLength", "投影长度(px)", 1, 500, 1, 1m, "20"));
            t.Fields.Add(Enum("Polarity", "极性", "0",
                new[] { "Any（不过滤）", "由暗到亮", "由亮到暗" }, new[] { "0", "1", "2" }));
            t.Fields.Add(Dbl("EdgeThreshold", "边缘阈值", 0, 255, 1, 1m, "30", "灰度跳变幅值"));
            t.Fields.Add(Int("FilterHalfWidth", "滤波半宽", 0, 20, "2", "一维均值滤波，抗噪", true));
            t.Fields.Add(Enum("SelectMode", "选边策略", "0",
                new[] { "最强", "最前", "最后" }, new[] { "0", "1", "2" }));
            t.Fields.Add(Int("MinEdgeCount", "最少边缘点", 0, 200, "3"));

            t.Fields.Add(Group("离群剔除"));
            t.Fields.Add(Bool("RejectOutliers", "剔除离群点", "1", "首轮拟合后按残差剔除一轮"));
            t.Fields.Add(Dbl("OutlierTolerance", "剔除阈值(px)", 0, 100, 2, 0.5m, "2", "", true));

            t.Fields.Add(Group("判定（0 = 不限）"));
            t.Fields.Add(Dbl("LengthMin", "长度下限", 0, 1000000, 1, 1m, "0"));
            t.Fields.Add(Dbl("LengthMax", "长度上限", 0, 1000000, 1, 1m, "0"));
            t.Fields.Add(Dbl("AngleMin", "角度下限(°)", -360, 360, 2, 0.5m, "0"));
            t.Fields.Add(Dbl("AngleMax", "角度上限(°)", -360, 360, 2, 0.5m, "0"));
            t.Fields.Add(Dbl("MaxRms", "直线度上限", 0, 1000, 2, 0.5m, "0"));
            return t;
        }

        private static NodeParamTable CircleCaliper()
        {
            var t = new NodeParamTable { Type = FlowNodeType.CircleCaliper, Title = "圆卡尺" };

            t.Fields.Add(Group("环形 ROI（图像坐标）"));
            t.Fields.Add(Dbl("Cx", "圆心 X", -100000, 100000, 1, 1m, "320"));
            t.Fields.Add(Dbl("Cy", "圆心 Y", -100000, 100000, 1, 1m, "240"));
            t.Fields.Add(Dbl("InnerRadius", "内径", 0, 100000, 1, 1m, "80"));
            t.Fields.Add(Dbl("OuterRadius", "外径", 0, 100000, 1, 1m, "120",
                "小于或等于内径时按「内径 + 投影长度」处理"));
            t.Fields.Add(Dbl("StartAngle", "起始角(°)", -720, 720, 1, 1m, "0"));
            t.Fields.Add(Dbl("AngleSpan", "角度跨度(°)", 0, 360, 1, 1m, "360", "0 或 >360 视为整圆"));

            t.Fields.Add(Group("找边"));
            t.Fields.Add(Int("CaliperCount", "卡尺个数", 1, 360, "20"));
            t.Fields.Add(Dbl("ProjectionLength", "投影长度(px)", 1, 500, 1, 1m, "20"));
            t.Fields.Add(Enum("Polarity", "极性", "0",
                new[] { "Any（不过滤）", "由暗到亮", "由亮到暗" }, new[] { "0", "1", "2" }));
            t.Fields.Add(Dbl("EdgeThreshold", "边缘阈值", 0, 255, 1, 1m, "30"));
            t.Fields.Add(Int("FilterHalfWidth", "滤波半宽", 0, 20, "2", "", true));
            t.Fields.Add(Enum("SelectMode", "选边策略", "0",
                new[] { "最强", "最前", "最后" }, new[] { "0", "1", "2" }));
            t.Fields.Add(Int("MinEdgeCount", "最少边缘点", 0, 360, "5"));

            t.Fields.Add(Group("离群剔除"));
            t.Fields.Add(Bool("RejectOutliers", "剔除离群点", "1"));
            t.Fields.Add(Dbl("OutlierTolerance", "剔除阈值(px)", 0, 100, 2, 0.5m, "2", "", true));

            t.Fields.Add(Group("判定（0 = 不限）"));
            t.Fields.Add(Dbl("DiameterMin", "直径下限", 0, 1000000, 1, 1m, "0"));
            t.Fields.Add(Dbl("DiameterMax", "直径上限", 0, 1000000, 1, 1m, "0"));
            t.Fields.Add(Dbl("RoundnessMax", "圆度上限(RMS)", 0, 1000, 2, 0.5m, "0"));
            t.Fields.Add(Dbl("CenterToleranceX", "圆心 X 允差", 0, 100000, 1, 1m, "0"));
            t.Fields.Add(Dbl("CenterToleranceY", "圆心 Y 允差", 0, 100000, 1, 1m, "0"));
            return t;
        }

        private static NodeParamTable Sharpness()
        {
            var t = new NodeParamTable { Type = FlowNodeType.Sharpness, Title = "清晰度" };
            t.Fields.Add(Group("判定"));
            t.Fields.Add(Dbl("MinSharpness", "最小清晰度", 0, 1000000, 1, 10m, "100",
                "拉普拉斯方差，越大越清晰"));
            return t;
        }

        /// <summary>标定转换：像素 → 机械坐标。</summary>
        private static NodeParamTable CalibTransformSchema()
        {
            var t = new NodeParamTable { Type = FlowNodeType.CalibTransform, Title = "标定转换（像素→机械）" };

            t.Fields.Add(Group("标定数据"));
            t.Fields.Add(new NodeParamField
            {
                Key = "CalibRecordId",
                Label = "标定记录",
                Kind = NodeParamKind.Enum,
                Default = "",
                Options = new List<NodeParamOption>(),
                Hint = "下拉里只列「N 点标定」与「旋转中心」；选「自动」即用该类型第一条"
            });

            t.Fields.Add(Group("输入（像素坐标）"));
            t.Fields.Add(Dbl("SourceX", "坐标 X (px)", -100000m, 100000m, 3, 1m, "0",
                "要转换的像素点；接上游测量节点时用它输出的坐标"));
            t.Fields.Add(Dbl("SourceY", "坐标 Y (px)", -100000m, 100000m, 3, 1m, "0", ""));

            t.Fields.Add(Group("旋转（仅旋转中心类型生效）"));
            t.Fields.Add(Bool("ApplyRotation", "绕回转中心旋转", "0",
                "勾上则把坐标绕旋转中心转一个角度（模拟工件转位后的位置）"));
            t.Fields.Add(Dbl("RotateAngle", "旋转角 (°)", -360m, 360m, 3, 1m, "0", "逆时针为正"));

            t.Fields.Add(Note("输出：机械坐标写入节点指标的 X/Y（下游结果输出、发送数据可直接引用）"));
            return t;
        }

        /// <summary>畸变矫正：用相机内参把图拉正。</summary>
        private static NodeParamTable DistortionCorrectSchema()
        {
            var t = new NodeParamTable { Type = FlowNodeType.DistortionCorrect, Title = "畸变矫正（去畸变）" };

            t.Fields.Add(Group("标定数据"));
            t.Fields.Add(new NodeParamField
            {
                Key = "CalibRecordId",
                Label = "标定记录",
                Kind = NodeParamKind.Enum,
                Default = "",
                Options = new List<NodeParamOption>(),
                Hint = "下拉里只列「畸变矫正」类型的记录；选「自动」即用第一条"
            });

            t.Fields.Add(Note("输入：上一节点的图像　输出：去畸变后的图像（替换当前帧，供下游继续处理）"));
            t.Fields.Add(Note("需要先在「相机标定 → 畸变矫正」里标定过并设为当前"));
            return t;
        }

        private static NodeParamTable Fit(string title)
        {
            var t = new NodeParamTable { Title = title };
            t.Type = string.Equals(title, "圆拟合") ? FlowNodeType.FitCircle : FlowNodeType.FitLine;

            t.Fields.Add(Group("过滤（0 = 不限）"));
            t.Fields.Add(Dbl("MinContourArea", "最小轮廓面积", 0, 100000000, 0, 10m, "0"));

            t.Fields.Add(Group("显示"));
            t.Fields.Add(Bool("DrawShape", "绘制拟合结果", "1"));
            return t;
        }

        /// <summary>目标识别（OpenVINO YOLO）：模型路径 / 设备 / 阈值 / 最大目标数。</summary>
        private static NodeParamTable ObjectDetect()
        {
            var t = new NodeParamTable { Type = FlowNodeType.ObjectDetect, Title = "目标识别" };

            t.Fields.Add(Group("模型"));
            t.Fields.Add(File("ModelPath", "模型文件(.onnx)",
                "Ultralytics 导出的 YOLO ONNX；同目录可放 &lt;模型名&gt;.txt（每行一个类别名）自定义标签，缺省自动回退 COCO-80 / class_N"));
            t.Fields.Add(Enum("Device", "推理设备", "CPU",
                new[] { "CPU", "GPU" }, new[] { "CPU", "GPU" },
                "GPU 需 OpenVINO 带对应设备插件；留空或 CPU 走 CPU 推理"));

            t.Fields.Add(Group("检测参数"));
            t.Fields.Add(Dbl("ScoreThresh", "置信度阈值", 0, 1, 2, 0.05m, "0.25", "低于该分数的框被丢弃"));
            t.Fields.Add(Dbl("IouThresh", "NMS IoU 阈值", 0, 1, 2, 0.05m, "0.45", "同类框重叠度高于该值则合并"));
            t.Fields.Add(Int("MaxCount", "最多目标数", 1, 1000, "100"));

            t.FooterNote = "输入需为彩色图像（直接接图像源之后效果最佳）；检测框以叠加层显示在画布上，"
                         + "输出图像原样向下传递，可串联 Blob / 卡尺等下游节点。";
            return t;
        }

        /// <summary>字符识别（传统算法 OCR，原生 va_ocr_*）的参数表。</summary>
        private static NodeParamTable Ocr()
        {
            var t = new NodeParamTable { Type = FlowNodeType.Ocr, Title = "字符识别(OCR)" };

            t.Fields.Add(Group("模板"));
            // ⚠️ 这里刻意不用 File：NodeParamPanel 的文件浏览过滤器写死成图片格式，选目录会被误导。
            //    模板目录是「相对程序基目录」的名字或相对路径，故用 Folder（浏览 = 选文件夹）。
            t.Fields.Add(Folder("TemplateDir", "模板目录", "OCRModel",
                "相对程序运行目录（bin/x64/Release）的文件夹名，默认 OCRModel；留空按 OCRModel 处理"));
            t.Fields.Add(Note("约定：一个模板 = 一个字符（一个灰度图 = 一个字符），文件名（不含扩展名）即标签，"
                + "如 A.png、7.png、中.png。"
                + "⚠️ 多字符模板（如把「N3O」框成一张图）永远匹配不上任何单个字符框，"
                + "表现为「能分割、但全是 ? 且置信度 0.00」—— 请逐个字符框选入库。"
                + "⚠️ 字符方向必须与现场一致（旋转/倒置的字符请先把图摆正再框选）。"
                + "同一字符的多份样本用同名不同扩展名（A.png + A.bmp），每字符最多 6 份。"
                + "目录为空时只做字符分割、不输出文本。"));
            // 两个动作的语义由宿主（FlowForm）解释；ManageOcrTemplates 打开字符模板库窗体。
            t.Fields.Add(Action("ManageOcrTemplates", "管理模板库…",
                "打开字符模板库：查看 / 框选入库 / 删除 / 迁移（改动立即落盘并对下次运行生效）"));
            t.Fields.Add(Action("OcrRescanTemplates", "重新扫描",
                "模板目录被外部改动后强制重扫并刷新状态卡（不重跑流程也能看到最新字符数）"));

            t.Fields.Add(Group("分割参数"));
            t.Fields.Add(Int("Threshold", "二值化阈值", -1, 255, "-1",
                "-1 = Otsu 自适应；光照均匀时改固定阈值（如 128）更稳"));
            t.Fields.Add(Bool("Invert", "极性反转", "0",
                "0 = 自动判定极性（推荐，白底黑字 / 黑底白字都能正确分割）；1 = 强制反色"));
            t.Fields.Add(Dbl("MinCharH", "字符最小高度", 0, 10000, 0, 1m, "0", "像素；0 = 不限制（用于滤掉噪声点）", true));
            t.Fields.Add(Dbl("MaxCharH", "字符最大高度", 0, 10000, 0, 1m, "0", "像素；0 = 不限制（用于滤掉大块污渍）", true));
            t.Fields.Add(Dbl("MinAspect", "最小宽高比", 0, 100, 2, 0.05m, "0", "w/h；0 = 不限制", true));
            t.Fields.Add(Dbl("MaxAspect", "最大宽高比", 0, 100, 2, 0.05m, "0", "w/h；0 = 不限制", true));
            t.Fields.Add(Int("MaxGap", "同行最大间隙", -1, 1000, "3",
                "相邻字符合并的最大水平间隙（像素）；<0 按 3 处理"));

            t.Fields.Add(Group("识别参数"));
            t.Fields.Add(Int("NormW", "归一化宽", 0, 256, "16", "<=0 按 16；同一套模板必须全程一致", true));
            t.Fields.Add(Int("NormH", "归一化高", 0, 256, "24", "<=0 按 24；同一套模板必须全程一致", true));
            t.Fields.Add(Dbl("AcceptScore", "接受阈值", 0, 1, 2, 0.05m, "0.6",
                "低于该置信度的字符输出 '?'；调低易误识、调高易漏识"));
            t.Fields.Add(Int("MaxCount", "最多字符数", 1, 2000, "200", "超出部分被截断"));

            t.Fields.Add(Group("ROI（宽或高为 0 = 整帧）"));
            // RoiX/Y/W/H 是【真的生效】的：FlowRunner.ResolveRoi 已在读、轴对齐裁剪已工作，
            // 所以这四项必须保持可用 —— 顺手把它们也置灰会把唯一能用的功能关掉。
            t.Fields.Add(Int("RoiX", "X", 0, 100000, "0"));
            t.Fields.Add(Int("RoiY", "Y", 0, 100000, "0"));
            t.Fields.Add(Int("RoiW", "宽", 0, 100000, "0"));
            t.Fields.Add(Int("RoiH", "高", 0, 100000, "0"));

            // 【禁用中 → T04 启用】RoiAngle 目前没有被 RunOcr 读取，填了不生效。
            // 这比死按钮更糟：用户会反复试、反复怀疑自己填错，而界面没有任何反馈。
            // 注意：画布顶条「画 ROI」在 T02 起就能给 OCR 画旋转矩形并回写本键，所以这个值
            // 可能"看起来填进去了"却不参与识别 —— 文案必须点明这一点（否则用户会以为自己画错了）。
            // T04 接通「RoiAngle → 仿射矫正」后，删掉下面这一行 DisabledReason 即恢复可编辑。
            var roiAngle = Dbl("RoiAngle", "角度(°)", -180, 180, 2, 0.5m, "0", "绕 ROI 中心；顺时针为正");
            roiAngle.DisabledReason = "旋转 ROI 将在后续版本提供；当前 OCR 运行期只按轴对齐裁剪，ROI 角度不参与识别";
            t.Fields.Add(roiAngle);

            // 【禁用中 → T04 启用】框选 / 预览的入口同样在 T04。
            // 刻意保留按钮但置灰：看得见、明显不可用 > 看得见、点得动、点了没反应
            //（后者会被当 bug 反馈，而且容易和上一版真的坏过的顶条「画 ROI」混在一起）。
            // T04 落地后：删掉第 4 个实参（或传空字符串）即恢复可用。
            t.Fields.Add(Action("RoiPick", "框选 ROI",
                "在画布上拖拽画出带角度的矩形 ROI（画完可拖旋转手柄）",
                "该功能将在 OCR 旋转 ROI 版本提供（当前请先用顶条的「画 ROI」框轴对齐 ROI）"));
            t.Fields.Add(Action("RoiPreview", "预览矫正图",
                "按当前角度把 ROI 仿射矫正成正向图",
                "该功能将在 OCR 旋转 ROI 版本提供"));

            t.Fields.Add(Group("方向跟随"));
            // 【禁用中 → T04 启用】角度跟随位置修正尚未实现，勾了不生效。
            // T04 实现 θ = 实测偏差角后，删掉这一行 DisabledReason 即恢复可勾选。
            // 【联动】置灰期间 FlowValidator 的 W007（"开了角度跟随但上游没有位置修正"）会自动停报
            // —— 校验器不是写死"OCR 不报"，而是调 IsFieldDisabled(Ocr, "FollowPose") 问这里的
            // DisabledReason。所以去掉这一行之后，W007 会自己恢复，不用再去改校验器。
            var followPose = Bool("FollowPose", "角度跟随位置修正", "1",
                "θ = 实测相对基准的偏差角；关闭则只用配置角度");
            followPose.DisabledReason = "角度跟随位置修正将在后续版本提供，当前 ROI 角度固定为 0";
            t.Fields.Add(followPose);

            t.Fields.Add(Group("结果显示"));
            // 区域框 = OCR 作用区域，默认开（让用户始终看得见"识别的是哪一块"）。
            // 【本期已接通】FlowRunner.RunOcr 的 ⑥ 段按本开关产出 OcrRegionBox 到 AuxShapes，
            // 所以这里不再有 DisabledReason —— 置灰会让唯一说明"它在看哪块"的东西消失。
            t.Fields.Add(Bool("ShowRegionBox", "显示区域框(OCR 作用区域)", "1"));
            // 合并字符串框（默认开）：一行一个框 + 整行文本。这是"结果更清楚"的主力，
            // 取代了"把一行 30 个字符框全打开"的做法（那是噪声不是清晰）。
            t.Fields.Add(Bool("ShowStringBox", "显示合并字符串框", "1", "按行归并：一行一个框 + 该行识别文本"));
            // ⚠️ 默认值由 "1" 改为 "0"：默认少画一堆框，现场看预览更干净。
            //    老配置里存的是显式 "1"，读到的就是 "1"（见 NodeParamPanel.LoadValues 的「键缺失才用默认」口径），不受影响。
            t.Fields.Add(Bool("ShowBoxes", "显示逐字符框", "0", "默认关（老配置为显式 1，不受影响）"));

            t.FooterNote = "模板约定：每个字符一张灰度图放进模板目录，文件名即标签（A.png / 7.png，"
                         + "支持中文标签如 中.png）；目录为空时只做字符分割、不输出文本。"
                         + "输入图像内部自动转灰度，输出图像原样向下传递。";
            return t;
        }

        /// <summary>发送数据节点：按名称绑定一条通讯，发固定文本 / 格式化文本 / 写 Modbus 寄存器。</summary>
        private static NodeParamTable SendData()
        {
            var t = new NodeParamTable { Type = FlowNodeType.SendData, Title = "发送数据" };

            t.Fields.Add(Group("绑定通讯"));
            t.Fields.Add(CommNameField());
            t.Fields.Add(Bool("AutoConnect", "未连接时自动连接", "1",
                "关闭则通讯未连接时直接判 NG，不尝试连接"));

            t.Fields.Add(Group("发送内容"));
            t.Fields.Add(Enum("DataMode", "数据模式", "FixedText",
                new[] { "固定文本", "格式化", "Modbus 写寄存器" },
                new[] { "FixedText", "Format", "ModbusWrite" }));
            t.Fields.Add(Text("Text", "固定文本", "", "「固定文本」模式发送的内容，可填任意字符串"));
            t.Fields.Add(Text("Format", "格式化模板", "{OkNg},{Summary}",
                "「格式化」模式的模板：{Summary} 上游摘要、{OkNg} OK/NG、{Time} 时间；取不到时占位符原样保留"));

            t.Fields.Add(Group("Modbus 写寄存器（FC06）"));
            t.Fields.Add(Int("MbAddress", "寄存器地址", 0, 65535, "0"));
            t.Fields.Add(Int("MbValue", "写入值", 0, 65535, "0"));

            t.FooterNote = "通讯在「通讯」界面里创建与命名，这里按名称绑定，改名后需重新选择一次。"
                         + "TCP 服务端是广播给所有已接入客户端；Modbus 仅支持 03/06/10 功能码，不支持线圈。"
                         + "发送结果写在节点摘要里（格式：发送数据：{通讯名} ← {内容}）。";
            return t;
        }

        /// <summary>接收数据节点：按名称绑定一条通讯，取最近一条文本或读 Modbus 保持寄存器。</summary>
        private static NodeParamTable ReceiveData()
        {
            var t = new NodeParamTable { Type = FlowNodeType.ReceiveData, Title = "接收数据" };

            t.Fields.Add(Group("绑定通讯"));
            t.Fields.Add(CommNameField());
            t.Fields.Add(Bool("AutoConnect", "未连接时自动连接", "1"));

            t.Fields.Add(Group("接收方式"));
            t.Fields.Add(Enum("Mode", "模式", "TcpText",
                new[] { "TCP 接收文本", "Modbus 读寄存器" },
                new[] { "TcpText", "ModbusRead" }));
            t.Fields.Add(Int("TimeoutMs", "等待超时(ms)", 0, 60000, "2000",
                "TCP 模式下为空时轮询等待的时长；0 = 不等待，只看当前有没有"));

            t.Fields.Add(Group("Modbus 读寄存器（FC03）"));
            t.Fields.Add(Int("MbAddress", "起始地址", 0, 65535, "0"));
            t.Fields.Add(Int("MbCount", "寄存器数量", 1, 125, "1", "FC03 单次最多 125 个"));

            t.FooterNote = "通讯在「通讯」界面里创建与命名，这里按名称绑定。"
                         + "TCP 取的是该通讯最近收到的一条文本；超时未收到数据判为「跳过」并在摘要里写明，不判 NG。"
                         + "Modbus 读到的寄存器会拼成「100, 200, 300」这样的文本；仅支持 03/06/10 功能码。";
            return t;
        }

        /// <summary>
        /// 「绑定通讯」下拉：<strong>运行时</strong>按 <see cref="CommRegistry.Names"/>() 填充。
        /// <para>
        /// <see cref="For"/> 每次调用都新建一张 <see cref="NodeParamTable"/>，而
        /// <see cref="NodeParamField.Options"/> 是可写列表，所以每次打开参数面板拿到的都是最新的通讯列表，
        /// 不需要额外挂钩 <see cref="CommRegistry.Changed"/>（<c>NodeParamPanel.Build</c> 的 Enum 分支才读 Options）。
        /// </para>
        /// </summary>
        private static NodeParamField CommNameField()
        {
            var f = Enum("CommName", "绑定通讯", "",
                         new string[0], new string[0],
                         "在「通讯」界面中配置的连接；列表每次打开面板时刷新");

            f.Options.Clear();
            foreach (string n in CommRegistry.Names())
            {
                if (!string.IsNullOrEmpty(n)) f.Options.Add(new NodeParamOption(n, n));
            }
            if (f.Options.Count == 0)
                f.Options.Add(new NodeParamOption("<未配置，请先到通讯界面添加>", ""));

            f.Default = f.Options[0].Value;
            return f;
        }
    }
}
