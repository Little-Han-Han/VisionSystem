using System;
using System.Collections.Generic;

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

        /// <summary>枚举（ComboBox 下拉，存选项的 Value）。</summary>
        Enum,

        /// <summary>文件路径（TextBox + 「浏览」按钮）。</summary>
        File,

        /// <summary>分组标题（不对应参数）。</summary>
        Group,

        /// <summary>只读提示文本（不对应参数）。</summary>
        Note
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

        /// <summary>枚举选项（Kind=Enum 时有效）。</summary>
        public List<NodeParamOption> Options { get; set; }

        /// <summary>提示文本（可空）。</summary>
        public string Hint { get; set; }

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

        private static NodeParamField Group(string title)
        {
            return new NodeParamField { Kind = NodeParamKind.Group, Label = title };
        }

        private static NodeParamField Note(string text)
        {
            return new NodeParamField { Kind = NodeParamKind.Note, Label = text };
        }

        private static NodeParamField Int(string key, string label, decimal min, decimal max, string def, string hint = "")
        {
            return new NodeParamField
            {
                Key = key, Label = label, Kind = NodeParamKind.Int,
                Min = min, Max = max, Decimals = 0, Increment = 1m,
                Default = def, Hint = hint
            };
        }

        private static NodeParamField Dbl(string key, string label, decimal min, decimal max,
                                          int decimals, decimal inc, string def, string hint = "")
        {
            return new NodeParamField
            {
                Key = key, Label = label, Kind = NodeParamKind.Double,
                Min = min, Max = max, Decimals = decimals, Increment = inc,
                Default = def, Hint = hint
            };
        }

        private static NodeParamField Bool(string key, string label, string def, string hint = "")
        {
            return new NodeParamField
            {
                Key = key, Label = label, Kind = NodeParamKind.Bool, Default = def, Hint = hint
            };
        }

        private static NodeParamField Text(string key, string label, string def, string hint = "")
        {
            return new NodeParamField
            {
                Key = key, Label = label, Kind = NodeParamKind.Text, Default = def, Hint = hint
            };
        }

        private static NodeParamField File(string key, string label, string hint = "")
        {
            return new NodeParamField
            {
                Key = key, Label = label, Kind = NodeParamKind.File, Default = "", Hint = hint
            };
        }

        private static NodeParamField Enum(string key, string label, string def,
                                           string[] texts, string[] values, string hint = "")
        {
            var f = new NodeParamField
            {
                Key = key, Label = label, Kind = NodeParamKind.Enum, Default = def, Hint = hint
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
                case FlowNodeType.FitCircle:       return Fit("圆拟合");
                case FlowNodeType.FitLine:         return Fit("直线拟合");
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
                new[] { "流程绑定相机", "本地图片文件" }, new[] { "Camera", "File" }));
            t.Fields.Add(File("FilePath", "图片路径", "仅「本地图片文件」时生效；留空则运行期提示"));
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

        private static NodeParamTable PositionCorrect()
        {
            var t = new NodeParamTable { Type = FlowNodeType.PositionCorrect, Title = "位置修正" };
            t.Fields.Add(Group("基准来源"));
            t.Fields.Add(Enum("RefSource", "基准", "Auto",
                new[] { "自动（上游匹配结果）", "手动示教" }, new[] { "Auto", "Manual" }));
            t.Fields.Add(Int("RefX", "基准 X", -100000, 100000, "0", "仅手动示教时生效"));
            t.Fields.Add(Int("RefY", "基准 Y", -100000, 100000, "0", "仅手动示教时生效"));
            t.Fields.Add(Dbl("RefAngle", "基准角(°)", -360, 360, 2, 0.5m, "0", "仅手动示教时生效"));
            t.Fields.Add(Bool("EnableAngle", "启用旋转修正", "1", "关闭则只平移不旋转"));

            t.Fields.Add(Group("超差上限（0 = 不限）"));
            t.Fields.Add(Dbl("MaxShiftX", "X 偏移上限(px)", 0, 100000, 1, 1m, "0"));
            t.Fields.Add(Dbl("MaxShiftY", "Y 偏移上限(px)", 0, 100000, 1, 1m, "0"));
            t.Fields.Add(Dbl("MaxAngle", "角度上限(°)", 0, 360, 2, 0.5m, "0"));
            t.Fields.Add(Dbl("MaxScaleDev", "缩放偏差上限", 0, 10, 3, 0.05m, "0"));

            t.FooterNote = "自动基准在运行期首次取得上游匹配结果后锁定，之后保持不变（等同示教一次）；"
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

            t.Fields.Add(Group("训练"));
            t.Fields.Add(Dbl("Contrast", "对比度", 0, 255, 1, 1m, "30"));
            t.Fields.Add(Int("MaxFeatures", "最多特征点", 10, 100000, "1500"));
            t.Fields.Add(Dbl("MinFeatureDist", "最小间距", 0, 100, 1, 0.5m, "2"));

            t.Fields.Add(Group("搜索区域"));
            t.Fields.Add(Int("SearchX", "X", 0, 100000, "0"));
            t.Fields.Add(Int("SearchY", "Y", 0, 100000, "0"));
            t.Fields.Add(Int("SearchW", "宽", 0, 100000, "0"));
            t.Fields.Add(Int("SearchH", "高", 0, 100000, "0"));

            t.Fields.Add(Group("匹配"));
            t.Fields.Add(Dbl("AngleStart", "起始角(°)", -360, 360, 2, 1m, "-10"));
            t.Fields.Add(Dbl("AngleEnd", "结束角(°)", -360, 360, 2, 1m, "10"));
            t.Fields.Add(Dbl("AngleStep", "角度步长(°)", 0, 360, 3, 0.5m, "1"));
            t.Fields.Add(Dbl("ScaleMin", "最小缩放", 0, 10, 4, 0.1m, "1"));
            t.Fields.Add(Dbl("ScaleMax", "最大缩放", 0, 10, 4, 0.1m, "1"));
            t.Fields.Add(Dbl("ScaleStep", "缩放步长", 0, 10, 4, 0.05m, "0", "0 = 不搜索缩放"));
            t.Fields.Add(Int("PyramidLevels", "金字塔层数", 1, 8, "4"));
            t.Fields.Add(Dbl("MinScore", "最小得分", 0, 1, 4, 0.05m, "0.7"));
            t.Fields.Add(Int("MaxMatches", "最多目标数", 1, 1000, "1"));
            t.Fields.Add(Dbl("MaxOverlap", "重叠阈值", 0, 1, 3, 0.05m, "0.5"));
            t.Fields.Add(Dbl("Greediness", "贪婪系数", 0, 1, 3, 0.05m, "0.9", "0 = 关闭启发式（最慢最稳）"));
            t.Fields.Add(Bool("Subpixel", "亚像素精化", "1"));
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

            t.Fields.Add(Group("搜索 ROI（宽或高为 0 = 整图）"));
            t.Fields.Add(Int("SearchX", "X", 0, 100000, "0"));
            t.Fields.Add(Int("SearchY", "Y", 0, 100000, "0"));
            t.Fields.Add(Int("SearchW", "宽", 0, 100000, "0"));
            t.Fields.Add(Int("SearchH", "高", 0, 100000, "0"));

            t.Fields.Add(Group("判定"));
            t.Fields.Add(Dbl("MinScore", "最小得分", 0, 1, 3, 0.05m, "0.8"));
            t.Fields.Add(Int("MaxMatches", "最多目标数", 1, 1000, "1"));
            t.Fields.Add(Bool("Subpixel", "亚像素精化", "1"));

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
            t.Fields.Add(Dbl("MaxArea", "最大面积", 0, 100000000, 0, 10m, "0"));
            t.Fields.Add(Dbl("MinWidth", "最小宽", 0, 100000, 0, 1m, "0"));
            t.Fields.Add(Dbl("MinHeight", "最小高", 0, 100000, 0, 1m, "0"));
            t.Fields.Add(Dbl("MinCircularity", "最小圆度", 0, 1, 3, 0.05m, "0"));
            t.Fields.Add(Note("圆度＝面积/外接矩形面积（圆≈0.785，矩形≈1.0）。"));
            t.Fields.Add(Int("MaxCount", "最多保留", 0, 1000, "50"));
            t.Fields.Add(Enum("SortBy", "排序", "Area", new[] { "面积降序" }, new[] { "Area" }));

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
            t.Fields.Add(Int("FilterHalfWidth", "滤波半宽", 0, 20, "2", "一维均值滤波，抗噪"));
            t.Fields.Add(Enum("SelectMode", "选边策略", "0",
                new[] { "最强", "最前", "最后" }, new[] { "0", "1", "2" }));
            t.Fields.Add(Int("MinEdgeCount", "最少边缘点", 0, 200, "3"));

            t.Fields.Add(Group("离群剔除"));
            t.Fields.Add(Bool("RejectOutliers", "剔除离群点", "1", "首轮拟合后按残差剔除一轮"));
            t.Fields.Add(Dbl("OutlierTolerance", "剔除阈值(px)", 0, 100, 2, 0.5m, "2"));

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
            t.Fields.Add(Int("FilterHalfWidth", "滤波半宽", 0, 20, "2"));
            t.Fields.Add(Enum("SelectMode", "选边策略", "0",
                new[] { "最强", "最前", "最后" }, new[] { "0", "1", "2" }));
            t.Fields.Add(Int("MinEdgeCount", "最少边缘点", 0, 360, "5"));

            t.Fields.Add(Group("离群剔除"));
            t.Fields.Add(Bool("RejectOutliers", "剔除离群点", "1"));
            t.Fields.Add(Dbl("OutlierTolerance", "剔除阈值(px)", 0, 100, 2, 0.5m, "2"));

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
    }
}
