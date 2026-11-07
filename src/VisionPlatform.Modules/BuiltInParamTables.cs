using System;
using System.Collections.Generic;
using VisionPlatform.Flow;

namespace VisionPlatform.Modules
{
    /// <summary>
    /// 内置模块的声明式参数表（从宿主 <c>NodeParamSchema</c> 原样迁入）。
    /// <para>
    /// 单元格只描述"有哪些字段、什么类型、默认值、范围、提示"，界面由通用
    /// <c>ParamListControl</c> 渲染 —— 因此新增模块不需要写任何界面代码。
    /// </para>
    /// </summary>
    internal static class BuiltInParamTables
    {
        /// <summary>按类型取参数表；无参数表返回 null。</summary>
        public static NodeParamTable Create(FlowNodeType type)
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
                case FlowNodeType.LineIntersect:   return LineIntersect();
                case FlowNodeType.EdgePair:        return EdgePair();
                case FlowNodeType.PointPointMeasure: return PointPointMeasure();
                case FlowNodeType.PointLineMeasure:  return PointLineMeasure();
                case FlowNodeType.LineLineMeasure:   return LineLineMeasure();
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

        /// <summary>
        /// 交点卡尺：两组扫描线各找一条直线，再求延长线交点。
        /// <para>
        /// 两个 Geometry 端子（Line1 / Line2）<strong>优先</strong>：连了就直读上游直线，
        /// 对应那一组扫描线参数不生效（面板里仍显示，方便同一节点两种用法随时切换）。
        /// 未连的那一侧用本组参数内联找边，因此"两条都接上游"时图像都可以不接。
        /// </para>
        /// </summary>
        private static NodeParamTable LineIntersect()
        {
            var t = new NodeParamTable { Type = FlowNodeType.LineIntersect, Title = "交点卡尺" };

            // 组标题刻意短：面板头部约 10 个字就截断，长括号说明会被吃掉，
            // 完整规则放在 FooterNote 与端子 Hint 里（"内联"= 未接端子时才用这组扫描线）。
            t.Fields.Add(Group("直线1（内联）"));
            AddCaliperLineFields(t, "Line1", 100, 100, 300, 100);

            t.Fields.Add(Group("直线2（内联）"));
            AddCaliperLineFields(t, "Line2", 200, 50, 200, 250);

            t.Fields.Add(Group("公共找边参数"));
            t.Fields.Add(Int("FilterHalfWidth", "滤波半宽", 0, 20, "2", "一维均值滤波，抗噪", true));
            t.Fields.Add(Enum("SelectMode", "选边策略", "0",
                new[] { "最强", "最前", "最后" }, new[] { "0", "1", "2" }));
            t.Fields.Add(Int("MinEdgeCount", "最少边缘点", 0, 200, "3"));
            t.Fields.Add(Bool("RejectOutliers", "剔除离群点", "1", "首轮拟合后按残差剔除一轮"));
            t.Fields.Add(Dbl("OutlierTolerance", "剔除阈值(px)", 0, 100, 2, 0.5m, "2", "", true));

            t.Fields.Add(Group("判定（0 = 不限）"));
            t.Fields.Add(Dbl("MaxRms", "直线度上限", 0, 1000, 2, 0.5m, "0", "两条线取较大者"));

            t.Fields.Add(Note("输出：交点坐标（接标定转换）· 交点 · 夹角 · 直线度"));
            t.FooterNote = "「直线1 / 直线2」端子连上游「直线卡尺 / 直线拟合」时优先用上游直线；"
                         + "未连的一侧用本组扫描线现场找边（此时必须接图像）。";
            return t;
        }

        /// <summary>给"复合卡尺"节点补一组扫描线参数（键名 = <paramref name="prefix"/> + X1/Y1/X2/Y2…）。</summary>
        private static void AddCaliperLineFields(NodeParamTable t, string prefix,
                                                 int x1, int y1, int x2, int y2)
        {
            t.Fields.Add(Dbl(prefix + "X1", "起点 X", -100000, 100000, 1, 1m,
                x1.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            t.Fields.Add(Dbl(prefix + "Y1", "起点 Y", -100000, 100000, 1, 1m,
                y1.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            t.Fields.Add(Dbl(prefix + "X2", "终点 X", -100000, 100000, 1, 1m,
                x2.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            t.Fields.Add(Dbl(prefix + "Y2", "终点 Y", -100000, 100000, 1, 1m,
                y2.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            t.Fields.Add(Int(prefix + "CaliperCount", "卡尺个数", 1, 200, "10"));
            t.Fields.Add(Dbl(prefix + "ProjectionLength", "投影长度(px)", 1, 500, 1, 1m, "20"));
            t.Fields.Add(Enum(prefix + "Polarity", "极性", "0",
                new[] { "Any（不过滤）", "由暗到亮", "由亮到暗" }, new[] { "0", "1", "2" }));
            t.Fields.Add(Dbl(prefix + "EdgeThreshold", "边缘阈值", 0, 255, 1, 1m, "30", "灰度跳变幅值"));
        }

        /// <summary>边缘对查找（线宽）：沿扫描线法向找"一升一降"两条边，测对宽。</summary>
        private static NodeParamTable EdgePair()
        {
            var t = new NodeParamTable { Type = FlowNodeType.EdgePair, Title = "边缘对查找" };

            t.Fields.Add(Group("扫描线段（图像坐标）"));
            t.Fields.Add(Dbl("X1", "起点 X", -100000, 100000, 1, 1m, "100"));
            t.Fields.Add(Dbl("Y1", "起点 Y", -100000, 100000, 1, 1m, "100"));
            t.Fields.Add(Dbl("X2", "终点 X", -100000, 100000, 1, 1m, "300"));
            t.Fields.Add(Dbl("Y2", "终点 Y", -100000, 100000, 1, 1m, "100"));

            t.Fields.Add(Group("找边"));
            t.Fields.Add(Int("CaliperCount", "卡尺个数", 1, 200, "10"));
            t.Fields.Add(Dbl("ProjectionLength", "投影长度(px)", 2, 2000, 1, 1m, "30",
                "每格沿法向的取样长度，要大于被测线宽"));
            t.Fields.Add(Dbl("EdgeThreshold", "边缘阈值", 0, 255, 1, 1m, "20", "梯度幅值（灰度/px）"));
            t.Fields.Add(Int("FilterHalfWidth", "滤波半宽", 0, 20, "2", "一维均值滤波，抗噪", true));
            t.Fields.Add(Enum("PairPolarity", "极性", "0",
                new[] { "亮线（暗→亮→暗）", "暗线（亮→暗→亮）" }, new[] { "0", "1" }));

            t.Fields.Add(Group("宽度过滤与选对（0 = 不限）"));
            t.Fields.Add(Dbl("MinWidth", "宽度下限(px)", 0, 100000, 2, 0.5m, "0", "逐对过滤"));
            t.Fields.Add(Dbl("MaxWidth", "宽度上限(px)", 0, 100000, 2, 0.5m, "0", "逐对过滤"));
            t.Fields.Add(Enum("SelectMode", "选对策略", "0",
                new[] { "最强", "最宽", "最窄", "最前" }, new[] { "0", "1", "2", "3" },
                "决定「中心 / 坐标」取哪一对；「宽度」始终是全部有效对的均值"));

            t.Fields.Add(Group("判定（0 = 不限）"));
            t.Fields.Add(Int("ExpectMin", "对数下限", 0, 1000, "0"));
            t.Fields.Add(Int("ExpectMax", "对数上限", 0, 1000, "0"));
            t.Fields.Add(Dbl("WidthMin", "宽度下限(判定)", 0, 100000, 2, 0.5m, "0", "对均值的判定，与上面的逐对过滤不同"));
            t.Fields.Add(Dbl("WidthMax", "宽度上限(判定)", 0, 100000, 2, 0.5m, "0"));

            t.Fields.Add(Note("输出：宽度（均值）· 对数 · 中心 · 坐标（可接标定转换）"));
            t.FooterNote = "对宽沿「扫描线法线」方向测量；扫描线应与被测线大致垂直。";
            return t;
        }

        /// <summary>点点测量：两个坐标端子 → 距离 / 方向角 / ΔX / ΔY / 中点。</summary>
        private static NodeParamTable PointPointMeasure()
        {
            var t = new NodeParamTable { Type = FlowNodeType.PointPointMeasure, Title = "点点测量" };

            t.Fields.Add(Group("判定（0 = 不限）"));
            t.Fields.Add(Dbl("DistMin", "距离下限(px)", 0, 1000000, 2, 0.5m, "0"));
            t.Fields.Add(Dbl("DistMax", "距离上限(px)", 0, 1000000, 2, 0.5m, "0"));

            t.Fields.Add(Note("输入：点1 / 点2 两个坐标端子（可接匹配中心 / Blob 中心 / 交点 / 中点）"));
            t.Fields.Add(Note("输出：距离 · 角度 · ΔX · ΔY · 中点（中点可接标定转换）"));
            return t;
        }

        /// <summary>点直线测量：点到直线的垂距 / 垂足。</summary>
        private static NodeParamTable PointLineMeasure()
        {
            var t = new NodeParamTable { Type = FlowNodeType.PointLineMeasure, Title = "点直线测量" };

            t.Fields.Add(Group("判定（0 = 不限）"));
            t.Fields.Add(Dbl("DistMin", "垂距下限(px)", 0, 1000000, 2, 0.5m, "0"));
            t.Fields.Add(Dbl("DistMax", "垂距上限(px)", 0, 1000000, 2, 0.5m, "0"));

            t.Fields.Add(Note("输入：点（坐标端子）+ 直线（几何端子，接「直线卡尺 / 直线拟合」）"));
            t.Fields.Add(Note("输出：垂距 · 垂足 · 线角 · 垂足坐标（可接标定转换）"));
            return t;
        }

        /// <summary>线线测量：两直线夹角 / 交点 / 平行间距。</summary>
        private static NodeParamTable LineLineMeasure()
        {
            var t = new NodeParamTable { Type = FlowNodeType.LineLineMeasure, Title = "线线测量" };

            t.Fields.Add(Group("平行判定"));
            t.Fields.Add(Dbl("ParallelTol", "平行容差(°)", 0, 90, 2, 0.1m, "0.5",
                "夹角小于该值即视为平行：只输出「间距」，不输出交点"));

            t.Fields.Add(Group("判定（0 = 不限）"));
            t.Fields.Add(Dbl("AngleMin", "夹角下限(°)", 0, 90, 2, 0.5m, "0"));
            t.Fields.Add(Dbl("AngleMax", "夹角上限(°)", 0, 90, 2, 0.5m, "0"));
            t.Fields.Add(Dbl("DistMin", "间距下限(px)", 0, 1000000, 2, 0.5m, "0", "仅平行时有意义"));
            t.Fields.Add(Dbl("DistMax", "间距上限(px)", 0, 1000000, 2, 0.5m, "0"));

            t.Fields.Add(Note("输入：两条直线（几何端子）　输出：夹角 · 交点 · 间距 · 交点坐标"));
            return t;
        }

        /// <summary>标定转换：像素 → 机械坐标。</summary>
        private static NodeParamTable CalibTransformSchema()
        {
            var t = new NodeParamTable { Type = FlowNodeType.CalibTransform, Title = "标定转换（像素→机械）" };

            t.Fields.Add(Group("N 点标定（像素 → 机械）"));
            t.Fields.Add(new NodeParamField
            {
                Key = "CalibRecordId",
                Label = "标定记录",
                Kind = NodeParamKind.Enum,
                Default = "",
                Options = new List<NodeParamOption>(),
                OptionsSource = "calib:NPoint",
                Hint = "只列「N 点标定」记录；选「自动」即用第一条。不用 N 点转换可选「不使用」"
            });

            t.Fields.Add(Group("旋转中心（回转 / 极坐标）"));
            t.Fields.Add(new NodeParamField
            {
                Key = "RotCenterId",
                Label = "旋转中心",
                Kind = NodeParamKind.Enum,
                Default = "",
                Options = new List<NodeParamOption>(),
                OptionsSource = "calib:RotationCenter",
                Hint = "只列「旋转中心」记录；下拉项末尾带该记录的圆心/半径/RMS，选中即用"
            });

            t.Fields.Add(Group("输入（未接端子时用这里的定点）"));
            NodeParamField sx = Dbl("SourceX", "坐标 X (px)", -100000m, 100000m, 3, 1m, "0",
                "要转换的像素点；接了「坐标」端子时以端子为准，本项置灰");
            sx.LiveSource = "coordX";
            t.Fields.Add(sx);

            NodeParamField sy = Dbl("SourceY", "坐标 Y (px)", -100000m, 100000m, 3, 1m, "0", "");
            sy.LiveSource = "coordY";
            t.Fields.Add(sy);

            t.Fields.Add(Group("距离换算（像素 → mm）"));
            t.Fields.Add(Enum("ScaleSource", "比例来源", "Calib",
                new[] { "由标定推导", "手填 mm/px" }, new[] { "Calib", "Manual" },
                "由标定推导 = 取 N 点标定尺度项（√|det A|）；手填 = 用下面的数值"));
            t.Fields.Add(Dbl("MmPerPx", "比例 (mm/px)", 0m, 10000m, 6, 0.0001m, "0",
                "仅「手填」时生效；0 = 未知（此时「机械距离」按原像素值输出，摘要会注明未标定）", true));

            t.Fields.Add(Group("旋转（仅旋转中心类型生效）"));
            t.Fields.Add(Bool("ApplyRotation", "绕回转中心旋转", "0",
                "勾上则把坐标绕旋转中心转一个角度（模拟工件转位后的位置）"));
            t.Fields.Add(Dbl("RotateAngle", "旋转角 (°)", -360m, 360m, 3, 1m, "0", "逆时针为正"));

            t.Fields.Add(Note("输入端子：坐标（X/Y）· 距离（px）· 角度（px 方向角）——三者可分别接测量节点"));
            t.Fields.Add(Note("输出端子：机械坐标 · 机械距离 · 机械角度 · 比例（mm/px）"));
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
                OptionsSource = "calib:Distortion",
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
        /// 「绑定通讯」下拉：本表只声明 <see cref="NodeParamField.OptionsSource"/> = <c>comm</c>，
        /// 真正的选项由宿主的 <c>IParamOptionProvider</c> 在每次渲染时从通讯服务取。
        /// <para>这样模块包不引用通讯/配置服务，参数表可以独立复用。</para>
        /// </summary>
        private static NodeParamField CommNameField()
        {
            var f = Enum("CommName", "绑定通讯", "",
                         new string[0], new string[0],
                         "在「通讯」界面中配置的连接；列表每次打开面板时刷新");

            // 选项由宿主在渲染时按 OptionsSource 填充（模块包不引用通讯服务）
            f.OptionsSource = "comm";
            return f;
        }
    }
}
