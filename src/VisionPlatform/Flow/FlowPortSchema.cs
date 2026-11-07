using System;
using System.Collections.Generic;

namespace VisionPlatform.Flow
{
    /// <summary>
    /// 端子的数据/语义类型：决定两个端子之间能不能连线（见 <see cref="FlowPortSchema.IsCompatible"/>）。
    /// </summary>
    /// <remarks>
    /// <strong>只能在枚举末尾追加</strong>：本枚举只在内存中用于判定，但新增成员会改变既有
    /// 「类型兼容矩阵」的形状，追加时请同步更新 docs\架构\增量架构设计.md §4（端口化改造）
    /// 与 §7（公共约定：端子模型）。
    /// </remarks>
    public enum FlowPortKind
    {
        /// <summary>图像帧（各算法块的主体数据流）。</summary>
        Image,

        /// <summary>位姿：平移 + 旋转 + 缩放（对应运行期 <c>FlowRunTransform</c> 的端子化镜像）。</summary>
        Pose,

        /// <summary>标量数值（分数 / 个数 / RMS / 直径等）。</summary>
        Number,

        /// <summary>Blob 列表。</summary>
        BlobList,

        /// <summary>几何量（直线 / 圆）。</summary>
        Geometry,

        /// <summary>通配：接受一切，也能连向一切（目前只出现在「结果输出」的输入端）。</summary>
        Any,

        /// <summary>
        /// 文本摘要（一行结论文本）。
        /// 追加在末尾，不改既有成员的序号；两个按 Kind 分支的 switch
        /// （<c>FlowValidator.KindName</c> / <c>FlowForm.PortKindColor</c>）都有 default 分支兜底。
        /// </summary>
        Text,

        /// <summary>像素坐标点 (x, y)。</summary>
        Point,

        /// <summary>角度（度）。</summary>
        Angle,

        /// <summary>布尔判定（OK / NG）。</summary>
        Bool,

        /// <summary>矩形（x, y, w, h）。</summary>
        Rect
    }

    /// <summary>端子方向。</summary>
    public enum FlowPortDir
    {
        /// <summary>输入端子（画在节点左侧）。</summary>
        In,

        /// <summary>输出端子（画在节点右侧）。</summary>
        Out
    }

    /// <summary>
    /// 单个端子的声明：稳定 Key + 中文显示名 + 数据类型 + 是否必填。
    /// 实例由 <see cref="FlowPortSchema"/> 统一构造，调用方只读。
    /// </summary>
    public sealed class FlowPortDef
    {
        /// <summary>稳定标识（英文，落盘进 <c>FlowLink.FromPort</c> / <c>ToPort</c>；一旦发布不得改名）。</summary>
        public string Key { get; private set; }

        /// <summary>界面显示名（中文，画在节点两侧；可随时改）。</summary>
        public string DisplayName { get; private set; }

        /// <summary>数据类型，决定连线兼容性。</summary>
        public FlowPortKind Kind { get; private set; }

        /// <summary>是否必填（true 的端子未连接时校验器报 E003）。</summary>
        public bool Required { get; private set; }

        /// <summary>鼠标悬停提示；可空。</summary>
        public string Hint { get; private set; }

        /// <summary>
        /// 是否允许<strong>多条线</strong>同时接入（扇入）。
        /// <para>
        /// 默认 false：输入端子语义上是"唯一数据源"，再连一条就替换旧的（换数据源）。
        /// 汇总类端子（脚本的 Any、结果输出的 Any/Image）要允许并行分支同时接进来 ——
        /// 这不是"换源"，而是"多路汇入同一处"。
        /// </para>
        /// </summary>
        public bool MultiIn { get; private set; }

        public FlowPortDef(string key, string displayName, FlowPortKind kind, bool required,
                           string hint = "", bool multiIn = false)
        {
            Key = key ?? "";
            DisplayName = displayName ?? "";
            Kind = kind;
            Required = required;
            Hint = hint ?? "";
            MultiIn = multiIn;
        }

        /// <summary>本端子是否接受来自 <paramref name="source"/> 类型的数据。</summary>
        /// <remarks>
        /// 规则：<c>源类型 == 自身类型 || 源为 Any || 自身为 Any</c>。
        /// <para>
        /// <strong>唯一例外：点 ↔ 位姿互通</strong>。本库的「统一坐标」约定下，
        /// 位姿 = 点 + 角度/尺度（<c>Coord</c> 输出就是 Pose），两者在"一个 X/Y 位置"上语义重叠：
        /// 若判成不兼容，用户把「交点」(Point) 接到「标定转换.坐标」(Pose) 会被拒，
        /// 只能靠"碰巧同名"的端口才连得上。故允许双向连通，但仍<strong>拒绝</strong>图像 / 数值 / 列表。
        /// </para>
        /// </remarks>
        public bool Accepts(FlowPortKind source)
        {
            if (source == Kind || source == FlowPortKind.Any || Kind == FlowPortKind.Any) return true;
            if (Kind == FlowPortKind.Pose && source == FlowPortKind.Point) return true;
            if (Kind == FlowPortKind.Point && source == FlowPortKind.Pose) return true;
            return false;
        }

        public override string ToString()
        {
            return DisplayName;
        }
    }

    /// <summary>
    /// 各类节点的<strong>声明式</strong>端子表：输入 / 输出端子清单 + 类型兼容判定。
    /// 画布（绘制与拉线）、校验器（必填与类型）、执行器（按端子取输入写输出）
    /// 三方共用本表——<strong>任何一处都不许硬编码端子名</strong>。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="FlowModule"/>（有哪些块）、<see cref="NodeParamSchema"/>（块里填什么）并列，
    /// 三者互不重复：本表只管「块外连什么」。
    /// <para>
    /// <strong>约定</strong>：<see cref="OutputsOf"/> 的第 1 项是「主输出」，
    /// <see cref="InputsOf"/> 中第 1 个必填项（无必填则第 1 项）是「主输入」——
    /// 旧配置（只有 FromNodeId / ToNodeId）归一化时就取这两个，
    /// 这样历史上「整块对整块」的语义被无损继承。
    /// </para>
    /// <para>
    /// 所有查询对未知节点类型一律返回空列表 / 空串，<strong>不抛异常</strong>。
    /// </para>
    /// </remarks>
    public static class FlowPortSchema
    {
        private static readonly Dictionary<FlowNodeType, List<FlowPortDef>> _inputs
            = new Dictionary<FlowNodeType, List<FlowPortDef>>();

        private static readonly Dictionary<FlowNodeType, List<FlowPortDef>> _outputs
            = new Dictionary<FlowNodeType, List<FlowPortDef>>();

        static FlowPortSchema()
        {
            Register(FlowNodeType.ImageSource,
                new List<FlowPortDef>(),
                new List<FlowPortDef> { P("Image", "图像", FlowPortKind.Image, false) });

            Register(FlowNodeType.Preprocess,
                new List<FlowPortDef> { P("Image", "图像", FlowPortKind.Image, true) },
                new List<FlowPortDef> { P("Image", "图像", FlowPortKind.Image, false) });

            Register(FlowNodeType.PositionCorrect,
                new List<FlowPortDef>
                {
                    P("Image", "图像", FlowPortKind.Image, true),
                    // 【可选，不要改成必填】历史语义：位置修正在 RefSource=Auto 模式下会自动取
                    // 上游最近的匹配位姿作为基准，用户只连一条 Image 线就能跑。
                    // 若标成必填，所有现存含位置修正的流程都会报 E003「输入端子未连接」——
                    // 那是我们自己制造的回归（team-lead 2026 拍板：本条优于架构文档 §4 表）。
                    P("Pose", "输入位姿", FlowPortKind.Pose, false,
                      "可选：未连接时按 Auto 语义取上游最近匹配位姿（历史行为）")
                },
                new List<FlowPortDef>
                {
                    P("Image", "图像", FlowPortKind.Image, false),
                    P("Pose", "修正位姿", FlowPortKind.Pose, false)
                });

            List<FlowPortDef> matchIn = new List<FlowPortDef>
            {
                P("Image", "图像", FlowPortKind.Image, true),
                P("Pose", "搜索基准", FlowPortKind.Pose, false)
            };
            List<FlowPortDef> matchOut = new List<FlowPortDef>
            {
                P("Image", "图像", FlowPortKind.Image, false),
                P("Pose", "匹配位姿", FlowPortKind.Pose, false),
                P("Score", "匹配分数", FlowPortKind.Number, false)
            };
            Register(FlowNodeType.ShapeMatch, matchIn, matchOut);
            Register(FlowNodeType.TemplateMatch,
                Clone(matchIn), Clone(matchOut));

            Register(FlowNodeType.Blob,
                new List<FlowPortDef> { P("Image", "图像", FlowPortKind.Image, true) },
                new List<FlowPortDef>
                {
                    P("Image", "图像", FlowPortKind.Image, false),
                    P("Blobs", "列表", FlowPortKind.BlobList, false),
                    P("Count", "数量", FlowPortKind.Number, false)
                });

            Register(FlowNodeType.LineCaliper,
                new List<FlowPortDef>
                {
                    P("Image", "图像", FlowPortKind.Image, true),
                    P("Pose", "位姿", FlowPortKind.Pose, false)
                },
                new List<FlowPortDef>
                {
                    P("Image", "图像", FlowPortKind.Image, false),
                    P("Geometry", "直线", FlowPortKind.Geometry, false),
                    P("Value", "RMS", FlowPortKind.Number, false)
                });

            Register(FlowNodeType.CircleCaliper,
                new List<FlowPortDef>
                {
                    P("Image", "图像", FlowPortKind.Image, true),
                    P("Pose", "位姿", FlowPortKind.Pose, false)
                },
                new List<FlowPortDef>
                {
                    P("Image", "图像", FlowPortKind.Image, false),
                    P("Geometry", "圆", FlowPortKind.Geometry, false),
                    P("Value", "直径", FlowPortKind.Number, false)
                });

            Register(FlowNodeType.Sharpness,
                new List<FlowPortDef> { P("Image", "图像", FlowPortKind.Image, true) },
                new List<FlowPortDef>
                {
                    P("Image", "图像", FlowPortKind.Image, false),
                    P("Value", "清晰度评分", FlowPortKind.Number, false)
                });

            Register(FlowNodeType.FitCircle,
                new List<FlowPortDef>
                {
                    P("Image", "图像", FlowPortKind.Image, true),
                    P("Blobs", "列表", FlowPortKind.BlobList, false)
                },
                new List<FlowPortDef>
                {
                    P("Image", "图像", FlowPortKind.Image, false),
                    P("Geometry", "圆", FlowPortKind.Geometry, false)
                });

            Register(FlowNodeType.FitLine,
                new List<FlowPortDef>
                {
                    P("Image", "图像", FlowPortKind.Image, true),
                    P("Blobs", "列表", FlowPortKind.BlobList, false)
                },
                new List<FlowPortDef>
                {
                    P("Image", "图像", FlowPortKind.Image, false),
                    P("Geometry", "直线", FlowPortKind.Geometry, false)
                });

            // 脚本节点：图像可选（有些脚本只做数据计算），另可接任意数据表达"我依赖它"
            Register(FlowNodeType.Script,
                new List<FlowPortDef>
                {
                    P("Image", "图像", FlowPortKind.Image, false),
                    P("Any", "任意输入", FlowPortKind.Any, false, "", true)   // 汇总：允许多路汇入
                },
                new List<FlowPortDef>
                {
                    P("Image", "图像", FlowPortKind.Image, false),
                    P("Text", "脚本结果", FlowPortKind.Text, false)
                });

            // 深度学习：图像进、图像+指标出（与目标识别同一套端口风格）
            Register(FlowNodeType.DlSeg,
                new List<FlowPortDef>
                {
                    P("Image", "图像", FlowPortKind.Image, true),
                    P("Any", "任意输入", FlowPortKind.Any, false, "", true)
                },
                new List<FlowPortDef>
                {
                    P("Image", "图像", FlowPortKind.Image, false),
                    P("Count", "目标数", FlowPortKind.Number, false),
                    P("Score", "最高分", FlowPortKind.Number, false)
                });

            Register(FlowNodeType.DlPose,
                new List<FlowPortDef>
                {
                    P("Image", "图像", FlowPortKind.Image, true),
                    P("Any", "任意输入", FlowPortKind.Any, false, "", true)
                },
                new List<FlowPortDef>
                {
                    P("Image", "图像", FlowPortKind.Image, false),
                    P("Count", "目标数", FlowPortKind.Number, false),
                    P("KeyPoints", "关键点", FlowPortKind.Number, false)
                });

            Register(FlowNodeType.DlDepth,
                new List<FlowPortDef>
                {
                    P("Image", "图像", FlowPortKind.Image, true)
                },
                new List<FlowPortDef>
                {
                    P("Image", "图像", FlowPortKind.Image, false),
                    P("Min", "最小值", FlowPortKind.Number, false),
                    P("Max", "最大值", FlowPortKind.Number, false)
                });

            // 深度学习 OCR：图像进，文本 + 行数出
            Register(FlowNodeType.OcrDl,
                new List<FlowPortDef>
                {
                    P("Image", "图像", FlowPortKind.Image, true),
                    P("Any", "任意输入", FlowPortKind.Any, false, "", true)
                },
                new List<FlowPortDef>
                {
                    P("Image", "图像", FlowPortKind.Image, false),
                    P("Text", "文本", FlowPortKind.Text, false),
                    P("Count", "行数", FlowPortKind.Number, false)
                });

            Register(FlowNodeType.ResultOutput,
                new List<FlowPortDef> { P("Any", "任意输入", FlowPortKind.Any, true, "", true) },
                new List<FlowPortDef>());

            // ---- 本期新增：图像滤波 / 颜色提取 ----
            // 端子数据与执行器暂不按端子读写（本库的执行器是按节点类型取工作帧的），
            // 这里的端子表只用于画布绘制、连线兼容性判定与校验器。

            Register(FlowNodeType.ImageFilter,
                new List<FlowPortDef> { P("Image", "图像", FlowPortKind.Image, true) },
                new List<FlowPortDef>
                {
                    P("Image", "图像", FlowPortKind.Image, false),
                    P("Text", "摘要", FlowPortKind.Text, false)
                });

            Register(FlowNodeType.ColorExtractor,
                new List<FlowPortDef> { P("Image", "图像", FlowPortKind.Image, true) },
                new List<FlowPortDef>
                {
                    P("Image", "掩膜", FlowPortKind.Image, false),
                    P("Blobs", "命中区域", FlowPortKind.BlobList, false),
                    P("Ratio", "面积占比", FlowPortKind.Number, false),
                    P("Text", "摘要", FlowPortKind.Text, false)
                });

            // ---- 本期新增：目标识别（OpenVINO YOLO）----
            // 输入：彩色图像；输出：原图透传 + 检测数量 + 检测摘要文本。
            // 检测框以叠加层（FlowShapes）画在画布上，不单独走端子数据（与位置修正同策略：
            // 复杂结构用叠加层 + 全局状态表达，端子只承载标量 / 图像 / 文本）。

            Register(FlowNodeType.ObjectDetect,
                new List<FlowPortDef> { P("Image", "图像", FlowPortKind.Image, true) },
                new List<FlowPortDef>
                {
                    P("Image", "图像", FlowPortKind.Image, false),
                    P("Count", "检测数量", FlowPortKind.Number, false),
                    P("Text", "摘要", FlowPortKind.Text, false)
                });

            // ---- 本期新增：字符识别（传统算法 OCR）----
            // 输入：图像（内部转灰度）；输出：原图透传 + 字符数量 + 识别文本。
            // 字符框以叠加层（FlowShapes）画在画布上，端子只承载标量 / 图像 / 文本。

            // 【Pose 端子：与卡尺/匹配保持一致，可选】连上它只是"把定位结果显式接进来"；
            // 真正的位置修正走链内全局变换（FlowRunSession.Transform），所以不连也能被修正 ——
            // 这点必须写清楚，否则用户会以为"没连 Pose 就没修正"。
            Register(FlowNodeType.Ocr,
                new List<FlowPortDef>
                {
                    P("Image", "图像", FlowPortKind.Image, true),
                    P("Pose", "位姿", FlowPortKind.Pose, false,
                      "可选：位置修正由链内全局变换统一应用；此处连线用于显式表达依赖关系")
                },
                new List<FlowPortDef>
                {
                    P("Image", "图像", FlowPortKind.Image, false),
                    P("Count", "字符数", FlowPortKind.Number, false),
                    P("Text",  "识别文本", FlowPortKind.Text,  false)
                });

            // ---- 本期新增：发送数据 / 接收数据 ----
            // 输入图像一律「不必填」：这两个节点不依赖图像也能工作（纯通讯场景可独立成链），
            // 连了就原样向下透传。输出靠 Text 端子承载收发的文本内容。

            Register(FlowNodeType.SendData,
                new List<FlowPortDef> { P("Image", "图像", FlowPortKind.Image, false) },
                new List<FlowPortDef>
                {
                    P("Image", "图像", FlowPortKind.Image, false),
                    P("Text",  "发送内容", FlowPortKind.Text,  false)
                });

            Register(FlowNodeType.ReceiveData,
                new List<FlowPortDef> { P("Image", "图像", FlowPortKind.Image, false) },
                new List<FlowPortDef>
                {
                    P("Image",  "输出图像", FlowPortKind.Image, false),
                    P("Text",   "接收内容", FlowPortKind.Text,  false),
                    P("Number", "长度", FlowPortKind.Number, false)
                });

            // ---- 标定转换：吃坐标 / 距离 / 角度，吐机械坐标 / 机械距离 / 机械角度 ----
            // 【本版改动】原来只有一个 Coord 输入：像素距离、角度这类"测量结果"根本没有入口，
            // 现场只能先用脚本算一遍再传，等于绕开了标定。现在把三类量都摆成端子：
            //   Coord    = 点（X/Y）→ 走 6 参数仿射 / 旋转中心
            //   Distance = 像素距离   → 乘「mm/px」（由 N 点标定的尺度项推导，或手填）
            //   Angle    = 像素角度   → 叠加标定旋转角（+ 回转角）
            // 三个输入彼此独立：只接距离也能换算，不接坐标就不输出坐标。
            Register(FlowNodeType.CalibTransform,
                new List<FlowPortDef>
                {
                    P("Coord", "坐标", FlowPortKind.Pose, false,
                      "接「匹配位姿 / 区域中心 / 圆心 / 交点 / 中点」等 X/Y；不接则用参数里手填的定点坐标"),
                    P("Distance", "距离", FlowPortKind.Number, false,
                      "接测量节点的「距离 / 宽度」（像素），按标定比例换算成 mm"),
                    P("Angle", "角度", FlowPortKind.Angle, false,
                      "接测量节点的「角度」（像素），叠加标定的旋转角后输出机械角")
                },
                new List<FlowPortDef>
                {
                    // 【显示名刻意短】四个输出并排排在 180px 宽的卡片下沿，
                    // 每个只有 ~34px：4 个汉字会被省略号截断（当时就是这么发现的）。
                    // 语义不靠名字靠 Hint 与摘要，"坐标"就是"机械坐标"。
                    P("Coord", "坐标", FlowPortKind.Pose, false,
                      "转换后的机械 X/Y；下游「结果输出」「发送数据」可直接连"),
                    P("Distance", "距离", FlowPortKind.Number, false,
                      "像素距离 × 比例（mm）；标定缺尺度时原样输出像素值"),
                    P("Angle", "角度", FlowPortKind.Angle, false,
                      "像素角度 + 标定旋转角（+ 回转角）"),
                    P("Scale", "比例", FlowPortKind.Number, false,
                      "本次使用的 mm/px（0 = 未标定 / 未填）")
                });

            // ---- 本期新增：交点卡尺（双直线卡尺找交点）----
            // 两个 Geometry 输入可选：连了就"用上游直线卡尺的结果求交"，
            // 没连就用节点内置的两组扫描线现场找边。Image 因此也不必填（纯计算场景没有图也能跑）。
            Register(FlowNodeType.LineIntersect,
                new List<FlowPortDef>
                {
                    P("Image", "图像", FlowPortKind.Image, false,
                      "内联卡尺要用图；两条直线都由上游 Geometry 提供时可完全不接"),
                    P("Line1", "直线1", FlowPortKind.Geometry, false,
                      "接「直线卡尺 / 直线拟合」的 Geometry；不接则用参数里「直线1」的扫描线现场找边"),
                    P("Line2", "直线2", FlowPortKind.Geometry, false,
                      "接「直线卡尺 / 直线拟合」的 Geometry；不接则用参数里「直线2」的扫描线现场找边")
                },
                new List<FlowPortDef>
                {
                    P("Coord", "坐标", FlowPortKind.Pose, false,
                      "交点 X/Y，可直接接「标定转换.坐标」或其它测量节点"),
                    P("Intersection", "交点", FlowPortKind.Point, false),
                    P("Angle", "夹角", FlowPortKind.Angle, false, "两直线夹角（0~90°）"),
                    P("Rms", "RMS", FlowPortKind.Number, false, "两条线直线度的较大者（上游直线时为 0）")
                });

            // ---- 本期新增：边缘对查找（线宽）----
            Register(FlowNodeType.EdgePair,
                new List<FlowPortDef>
                {
                    P("Image", "图像", FlowPortKind.Image, true)
                },
                new List<FlowPortDef>
                {
                    P("Width", "宽度", FlowPortKind.Number, false, "所有有效边缘对的对宽均值（像素）"),
                    P("Count", "对数", FlowPortKind.Number, false),
                    P("Center", "中心", FlowPortKind.Point, false, "主对（按选对策略）的中点"),
                    P("Coord", "坐标", FlowPortKind.Pose, false, "主对中点，可直接接「标定转换.坐标」")
                });

            // ---- 本期新增：点点 / 点直线 / 线线测量 ----
            // 点类输入端子的 Kind 用 Point：与 Pose（Coord）双向兼容，
            // 因此「匹配位姿 / 区域中心」这类既有 X/Y 输出可以直接接进来。
            Register(FlowNodeType.PointPointMeasure,
                new List<FlowPortDef>
                {
                    P("Point1", "点1", FlowPortKind.Point, true, "接坐标输出（Coord / 中心 / 交点…）"),
                    P("Point2", "点2", FlowPortKind.Point, true, "接坐标输出（Coord / 中心 / 交点…）")
                },
                new List<FlowPortDef>
                {
                    P("Distance", "距离", FlowPortKind.Number, false, "两点距离（像素）"),
                    P("Angle", "角度", FlowPortKind.Angle, false, "点1→点2 的方向角（度）"),
                    P("Dx", "ΔX", FlowPortKind.Number, false),
                    P("Dy", "ΔY", FlowPortKind.Number, false),
                    P("Coord", "中点", FlowPortKind.Pose, false, "两点中点，可直接接「标定转换.坐标」")
                });

            Register(FlowNodeType.PointLineMeasure,
                new List<FlowPortDef>
                {
                    P("Point", "点", FlowPortKind.Point, true),
                    P("Line", "直线", FlowPortKind.Geometry, true,
                      "接「直线卡尺 / 直线拟合」的 Geometry")
                },
                new List<FlowPortDef>
                {
                    P("Distance", "垂距", FlowPortKind.Number, false, "点到直线的垂直距离（像素）"),
                    P("Foot", "垂足", FlowPortKind.Point, false),
                    P("Angle", "线角", FlowPortKind.Angle, false, "所测直线的方向角（度）"),
                    P("Coord", "坐标", FlowPortKind.Pose, false, "垂足 X/Y，可直接接「标定转换.坐标」")
                });

            Register(FlowNodeType.LineLineMeasure,
                new List<FlowPortDef>
                {
                    P("Line1", "直线1", FlowPortKind.Geometry, true),
                    P("Line2", "直线2", FlowPortKind.Geometry, true)
                },
                new List<FlowPortDef>
                {
                    P("Angle", "夹角", FlowPortKind.Angle, false, "两直线夹角（0~90°，平行时为 0）"),
                    P("Intersection", "交点", FlowPortKind.Point, false, "平行时不输出"),
                    P("Distance", "间距", FlowPortKind.Number, false, "平行时的线间距（像素）；不平行时不适用"),
                    P("Coord", "坐标", FlowPortKind.Pose, false,
                      "交点 X/Y；两条线平行（夹角小于容差）时不输出，此时只有「间距」有效")
                });

            // ---- 统一坐标输出：这些节点的 X/Y/R 可以直接接到「标定转换」 ----
            EnsureCoordOutput(FlowNodeType.PositionCorrect, "修正位姿");
            EnsureCoordOutput(FlowNodeType.ShapeMatch,    "匹配中心");
            EnsureCoordOutput(FlowNodeType.TemplateMatch, "匹配中心");
            EnsureCoordOutput(FlowNodeType.Blob,          "区域中心");
            EnsureCoordOutput(FlowNodeType.CircleCaliper, "圆心");
            EnsureCoordOutput(FlowNodeType.FitCircle,     "圆心");
            EnsureCoordOutput(FlowNodeType.FitLine,       "线中点");
            EnsureCoordOutput(FlowNodeType.ObjectDetect,  "目标中心");
            EnsureCoordOutput(FlowNodeType.Ocr,           "首字符中心");

            // ---- 畸变矫正：吃图像，吐矫正后的图像 ----
            Register(FlowNodeType.DistortionCorrect,
                new List<FlowPortDef>
                {
                    P("Image", "图像", FlowPortKind.Image, true, "接上一节点的图像")
                },
                new List<FlowPortDef>
                {
                    P("Image", "图像", FlowPortKind.Image, false, "去畸变后的图，可继续接下游处理节点")
                });
        }

        // ---------------------- 注册与构造器 ----------------------

        private static FlowPortDef P(string key, string displayName, FlowPortKind kind, bool required,
                                     string hint = "", bool multiIn = false)
        {
            return new FlowPortDef(key, displayName, kind, required, hint, multiIn);

        }

        /// <summary>
        /// 给节点补一个统一的「坐标」输出端子（幂等）。
        /// <para>
        /// 为什么统一叫 Coord：模板匹配、Blob、圆卡尺、圆拟合的输出语义都是"一个点（可选半径/角度）"，
        /// 如果每个节点各叫各的（TopCx / CircleCenter / MatchPose…），标定转换就得写一堆特判。
        /// 统一成 Coord 后，下游只认这一个端子，节点换不换都不影响接线。
        /// </para>
        /// </summary>
        private static void EnsureCoordOutput(FlowNodeType type, string displayName)
        {
            List<FlowPortDef> list;
            if (!_outputs.TryGetValue(type, out list) || list == null) return;

            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i].Key, "Coord", StringComparison.Ordinal)) return;

            list.Add(P("Coord", displayName, FlowPortKind.Pose, false,
                       "X / Y / R（R = 半径或角度），可直接接到标定转换"));
        }

        /// <summary>登记一个节点类型的输入 / 输出端子表（静态构造期调用一次）。</summary>
        private static void Register(FlowNodeType type, List<FlowPortDef> inputs, List<FlowPortDef> outputs)
        {
            _inputs[type] = inputs ?? new List<FlowPortDef>();
            _outputs[type] = outputs ?? new List<FlowPortDef>();
        }

        private static List<FlowPortDef> Clone(List<FlowPortDef> src)
        {
            var list = new List<FlowPortDef>();
            foreach (FlowPortDef d in src) list.Add(d);
            return list;
        }

        // ---------------------- 查询 API ----------------------

        /// <summary>该类型的输入端子列表（顺序即画布自上而下的显示顺序）；未知类型返回空列表。</summary>
        public static List<FlowPortDef> InputsOf(FlowNodeType type)
        {
            List<FlowPortDef> list;
            if (_inputs.TryGetValue(type, out list) && list != null) return Clone(list);
            return new List<FlowPortDef>();
        }

        /// <summary>
        /// 该类型的输出端子列表；未知类型返回空列表。
        /// <strong>第 1 项即「主输出」</strong>（<see cref="PrimaryOutput"/>）。
        /// </summary>
        public static List<FlowPortDef> OutputsOf(FlowNodeType type)
        {
            List<FlowPortDef> list;
            if (_outputs.TryGetValue(type, out list) && list != null) return Clone(list);
            return new List<FlowPortDef>();
        }

        /// <summary>按方向取端子列表；未知类型或未知方向返回空列表。</summary>
        public static List<FlowPortDef> PortsOf(FlowNodeType type, FlowPortDir dir)
        {
            return (dir == FlowPortDir.In) ? InputsOf(type) : OutputsOf(type);
        }

        /// <summary>
        /// 按方向 + Key 查端子；找不到（含 key 为 null / 空 / 类型未知）返回 null。
        /// </summary>
        public static FlowPortDef Find(FlowNodeType type, FlowPortDir dir, string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            List<FlowPortDef> list = PortsOf(type, dir);
            foreach (FlowPortDef d in list)
            {
                if (d != null && string.Equals(d.Key, key, StringComparison.Ordinal)) return d;
            }
            return null;
        }

        /// <summary>该类型在该方向上是否存在指定 Key 的端子。</summary>
        public static bool HasPort(FlowNodeType type, FlowPortDir dir, string key)
        {
            return Find(type, dir, key) != null;
        }

        /// <summary>
        /// 主输出 Key（第 1 个输出端子）；无输出端子时返回 <c>""</c>。
        /// 旧连线归一化（<c>FlowLink.Normalize</c>）用它补 <c>FromPort</c>。
        /// </summary>
        public static string PrimaryOutput(FlowNodeType type)
        {
            List<FlowPortDef> outs = OutputsOf(type);
            return (outs.Count > 0 && outs[0] != null) ? outs[0].Key : "";
        }

        /// <summary>
        /// 主输入 Key：第 1 个<strong>必填</strong>输入；没有必填项则取第 1 个输入；无输入端子时返回 <c>""</c>。
        /// 旧连线归一化用它补 <c>ToPort</c>。
        /// </summary>
        public static string PrimaryInput(FlowNodeType type)
        {
            List<FlowPortDef> ins = InputsOf(type);
            if (ins.Count == 0) return "";

            foreach (FlowPortDef d in ins)
            {
                if (d != null && d.Required) return d.Key;
            }
            return (ins[0] != null) ? ins[0].Key : "";
        }

        /// <summary>
        /// 两个端子的类型是否可连。规则：<c>from == to || from == Any || to == Any</c>，
        /// 另加「点 ↔ 位姿互通」这一个例外（与 <see cref="FlowPortDef.Accepts"/> 同规则）。
        /// </summary>
        public static bool IsCompatible(FlowPortKind from, FlowPortKind to)
        {
            if (from == to || from == FlowPortKind.Any || to == FlowPortKind.Any) return true;
            if (to == FlowPortKind.Pose && from == FlowPortKind.Point) return true;
            if (to == FlowPortKind.Point && from == FlowPortKind.Pose) return true;
            return false;
        }

        /// <summary>连线合法性判定：源输出端子 → 目标输入端子。</summary>
        public static bool CanConnect(FlowNodeType fromType, string fromPort,
                                      FlowNodeType toType, string toPort)
        {
            FlowPortDef src = Find(fromType, FlowPortDir.Out, fromPort);
            FlowPortDef dst = Find(toType, FlowPortDir.In, toPort);
            if (src == null || dst == null) return false;
            return dst.Accepts(src.Kind);
        }

        // ---------------------- 文档别名（与 §4.2 骨架同名，转发到上面的主 API） ----------------------

        /// <summary>别名：<see cref="InputsOf"/>（架构文档 §4.2 的命名）。</summary>
        public static List<FlowPortDef> Inputs(FlowNodeType type) { return InputsOf(type); }

        /// <summary>别名：<see cref="OutputsOf"/>（架构文档 §4.2 的命名）。</summary>
        public static List<FlowPortDef> Outputs(FlowNodeType type) { return OutputsOf(type); }

        /// <summary>别名：<see cref="PrimaryOutput"/>（架构文档 §4.2 的命名）。</summary>
        public static string DefaultOutputKey(FlowNodeType type) { return PrimaryOutput(type); }

        /// <summary>别名：<see cref="PrimaryInput"/>（架构文档 §4.2 的命名）。</summary>
        public static string DefaultInputKey(FlowNodeType type) { return PrimaryInput(type); }
    }
}
