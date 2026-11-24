using System;
using System.Collections.Generic;
using VisionPlatform.Flow;

namespace VisionPlatform.Modules
{
    /// <summary>
    /// 内置模块的端子表（从宿主 <c>FlowPortSchema</c> 原样迁入）。
    /// <para>端子是算法契约的一部分（数据能接到哪里），因此随模块一起发布。</para>
    /// </summary>
    internal static class BuiltInPorts
    {
        private static readonly Dictionary<FlowNodeType, List<FlowPortDef>> _inputs
            = new Dictionary<FlowNodeType, List<FlowPortDef>>();

        private static readonly Dictionary<FlowNodeType, List<FlowPortDef>> _outputs
            = new Dictionary<FlowNodeType, List<FlowPortDef>>();

        static BuiltInPorts()
        {
            Register(FlowNodeType.ImageSource,
                new List<FlowPortDef>(),
                new List<FlowPortDef> { P("Image", "图像", FlowPortKind.Image, false) });

            // 读取点云：链首节点（不吃图），输出点云 + 点数。显示视图按输出自动进 3D 模式。
            Register(FlowNodeType.PointCloudSource,
                new List<FlowPortDef>(),
                new List<FlowPortDef>
                {
                    P("PointCloud", "点云", FlowPortKind.PointCloud, false),
                    P("Count", "点数", FlowPortKind.Number, false)
                });

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
                new List<FlowPortDef>
                {
                    P("Image", "图像", FlowPortKind.Image, true),
                    // 位置修正输入：把上游定位节点（模板匹配 / 形状匹配 / 位置修正）的位姿接进来，
                    // ROI 随工件移动。不接则沿用帧级修正常量（见 RunBlob 的 ApplyCorrection）。
                    P("Pose", "位置修正", FlowPortKind.Pose, false)
                },
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

            // 无监督缺陷检测：图像 + 位置修正进，热图 + 缺陷参数出。
            // 【为什么位置修正要单列一个端子】异常检测对"工件位置"极敏感（逐位置比对），
            // 上游定位结果接进来后 ROI 跟着工件走，是这类算法能上产线的前提。
            Register(FlowNodeType.AnomalyDetect,
                new List<FlowPortDef>
                {
                    P("Image", "图像", FlowPortKind.Image, true),
                    P("Pose", "位置修正", FlowPortKind.Pose, false,
                      "可选：接上游匹配 / 位置修正的位姿，检测区域随工件移动")
                },
                new List<FlowPortDef>
                {
                    P("Image", "热图", FlowPortKind.Image, false),
                    P("Score", "最大异常分", FlowPortKind.Number, false),
                    P("Area", "面积占比", FlowPortKind.Number, false),
                    P("Count", "缺陷数", FlowPortKind.Number, false),
                    P("Box", "最大缺陷框", FlowPortKind.Rect, false),
                    P("Point", "最大分点位", FlowPortKind.Point, false)
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

            // 条码 / 二维码：图像进，图像 + 个数 + 文本 + 坐标出（坐标由 EnsureCoordOutput 补齐）
            Register(FlowNodeType.BarcodeRead,
                new List<FlowPortDef>
                {
                    P("Image", "图像", FlowPortKind.Image, true),
                    P("Pose", "位姿", FlowPortKind.Pose, false,
                      "可选：位置修正由链内全局变换统一应用；此处连线用于显式表达依赖关系")
                },
                new List<FlowPortDef>
                {
                    P("Image", "图像", FlowPortKind.Image, false),
                    P("Count", "码数", FlowPortKind.Number, false),
                    P("Text",  "首个码文本", FlowPortKind.Text, false)
                });

            // ---- 发送数据 / 接收数据 ----
            // 【端子修正】通讯节点不产图也不吃图：旧版把"可选图像"挂在输入输出上，
            // 现场会误以为输出信号节点该接图像链（用户反馈：输出信号的输入不应该是图像）。
            // 现在输入 / 输出全部走文本：
            //   SendData    输入 Text = 上游文本（脚本 / OCR 文本接入，留空则只用参数内容），
            //               输出 Text = 实际发出的内容；
            //   ReceiveData 无输入端子（链首节点），输出 Text = 收到的文本 + Number = 长度。
            // 老工程里连在旧 "Image" 端子上的连线，加载时由 FlowLink.Normalize
            // 按新端子表自动改挂到主输入 / 主输出（Text），不会悬空。

            Register(FlowNodeType.SendData,
                new List<FlowPortDef> { P("Text", "输入内容", FlowPortKind.Text, false) },
                new List<FlowPortDef> { P("Text", "发送内容", FlowPortKind.Text, false) });

            Register(FlowNodeType.ReceiveData,
                new List<FlowPortDef>(),
                new List<FlowPortDef>
                {
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
            EnsureCoordOutput(FlowNodeType.BarcodeRead,   "首个码中心");

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


        // ---------------------- 查询 ----------------------

        public static List<FlowPortDef> Inputs(FlowNodeType type)
        {
            List<FlowPortDef> list;
            if (_inputs.TryGetValue(type, out list) && list != null) return Clone(list);
            return new List<FlowPortDef>();
        }

        public static List<FlowPortDef> Outputs(FlowNodeType type)
        {
            List<FlowPortDef> list;
            if (_outputs.TryGetValue(type, out list) && list != null) return Clone(list);
            return new List<FlowPortDef>();
        }
    }
}
