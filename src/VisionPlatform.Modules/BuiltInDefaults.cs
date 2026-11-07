using System;
using System.Collections.Generic;
using VisionPlatform.Flow;

namespace VisionPlatform.Modules
{
    /// <summary>
    /// 内置模块的默认参数（从宿主 <c>FlowModuleInfo.CreateDefaultParams</c> 原样迁入）。
    /// <para>
    /// 【为什么保留 switch】默认值本身就是"每个模块各不相同的数据"，放在模块包内是合理的；
    /// 关键是它不再让宿主的目录 / 界面代码随模块增加而修改。
    /// </para>
    /// </summary>
    internal static class BuiltInDefaults
    {
        public static List<ParamItem> Create(FlowNodeType type)
        {
            var list = new List<ParamItem>();

            switch (type)
            {
                case FlowNodeType.ImageSource:
                    // 数据源：Camera = 流程绑定相机；File = 本地图片文件；Folder = 本地图片文件夹（轮播）
                    Add(list, "Source", "Camera");
                    Add(list, "FilePath", "");
                    Add(list, "FolderPath", "");
                    Add(list, "FolderPersist", "0");   // 0 = 只在本次运行内递增，1 = 落盘记住位置
                    Add(list, "TimeoutMs", "3000");
                    // 相机采集参数（预留）：当前只保存到流程配置，不下发相机；
                    // 接入厂商 SDK 后按这些 Key 读取即可，无需迁移旧配置。
                    Add(list, "Exposure", "10000");          // 曝光，微秒
                    Add(list, "Gain", "0");                  // 增益，dB
                    Add(list, "TriggerMode", "Continuous");  // Continuous / Software / Hardware
                    Add(list, "TriggerSource", "Line0");     // Line0 / Line1 / Software
                    Add(list, "TriggerDelay", "0");          // 触发延时，微秒
                    // ROI 裁剪：宽或高为 0 = 全图
                    Add(list, "RoiX", "0");
                    Add(list, "RoiY", "0");
                    Add(list, "RoiW", "0");
                    Add(list, "RoiH", "0");
                    // 旋转 0 / 90 / 180 / 270，水平镜像 0/1
                    Add(list, "Rotate", "0");
                    Add(list, "Mirror", "0");
                    break;

                case FlowNodeType.Preprocess:
                    Add(list, "ToGray", "1");
                    Add(list, "BlurKsize", "3");
                    Add(list, "Threshold", "128");
                    Add(list, "Invert", "0");
                    Add(list, "MorphOp", "0");
                    Add(list, "MorphKsize", "3");
                    break;

                case FlowNodeType.CalibTransform:
                    // 标定转换：像素 → 机械。CalibRecordId 为空时用该相机的「当前」标定
                    Add(list, "CalibRecordId", "");
                    Add(list, "SourceX", "0");        // 没有上游坐标时用手填的像素点
                    Add(list, "SourceY", "0");
                    Add(list, "ApplyRotation", "0");  // 1 = 再把结果绕旋转中心转 RotateAngle 度
                    Add(list, "RotateAngle", "0");
                    // 距离换算比例来源：Calib = 由 N 点标定的尺度项推导；Manual = 手填 mm/px
                    Add(list, "ScaleSource", "Calib");
                    Add(list, "MmPerPx", "0");        // 0 = 未知（此时距离原样按像素输出）
                    break;

                case FlowNodeType.DistortionCorrect:
                    // 畸变矫正：只连「畸变矫正」类型的记录
                    Add(list, "CalibRecordId", "");
                    break;

                case FlowNodeType.PositionCorrect:
                    // 基准来源：Auto = 取上游最近匹配结果的第 1 个目标；Manual = 手动示教位姿
                    Add(list, "RefSource", "Auto");
                    Add(list, "RefX", "0");
                    Add(list, "RefY", "0");
                    Add(list, "RefAngle", "0");
                    Add(list, "EnableAngle", "1");     // 0 = 只平移不旋转
                    // 超差上限：0 = 不限
                    Add(list, "MaxShiftX", "0");
                    Add(list, "MaxShiftY", "0");
                    Add(list, "MaxAngle", "0");
                    Add(list, "MaxScaleDev", "0");
                    break;

                case FlowNodeType.ShapeMatch:
                    // 模板 ROI（宽高 0 = 整图）
                    Add(list, "TemplateX", "0");
                    Add(list, "TemplateY", "0");
                    Add(list, "TemplateW", "0");
                    Add(list, "TemplateH", "0");
                    // 训练参数
                    Add(list, "Contrast", "30");
                    Add(list, "MaxFeatures", "1500");
                    Add(list, "MinFeatureDist", "2");
                    // 搜索参数
                    Add(list, "SearchX", "0");
                    Add(list, "SearchY", "0");
                    Add(list, "SearchW", "0");
                    Add(list, "SearchH", "0");
                    Add(list, "AngleStart", "-10");
                    Add(list, "AngleEnd", "10");
                    Add(list, "AngleStep", "1");
                    Add(list, "ScaleMin", "1");
                    Add(list, "ScaleMax", "1");
                    Add(list, "ScaleStep", "0");
                    Add(list, "PyramidLevels", "4");
                    Add(list, "MinScore", "0.7");
                    Add(list, "MaxMatches", "1");
                    Add(list, "MaxOverlap", "0.5");
                    Add(list, "Subpixel", "1");
                    Add(list, "Greediness", "0.9");
                    // 「创建模板…」是参数面板上的动作行（NodeParamKind.Action，Key=CreateTemplate），
                    // 点击后由 FlowForm 弹窗处理，没有参数值，故此处刻意不补键。
                    break;

                case FlowNodeType.TemplateMatch:
                    // NCC（归一化灰度相关）：仅支持平移，不支持旋转 / 缩放
                    Add(list, "TemplateX", "0");
                    Add(list, "TemplateY", "0");
                    Add(list, "TemplateW", "0");
                    Add(list, "TemplateH", "0");
                    Add(list, "SearchX", "0");
                    Add(list, "SearchY", "0");
                    Add(list, "SearchW", "0");
                    Add(list, "SearchH", "0");
                    Add(list, "MinScore", "0.8");
                    Add(list, "MaxMatches", "1");
                    Add(list, "Subpixel", "1");
                    // 同 ShapeMatch：「创建模板…」是动作行，不落盘、不补键。
                    break;

                case FlowNodeType.Blob:
                    // 二值化方式：Inherit = 继承上游处理结果；Otsu = 自动阈值；Manual = 手动阈值
                    Add(list, "BinMode", "Inherit");
                    Add(list, "Threshold", "128");
                    // 极性：Bright = 亮底上的暗目标取反前统计白；Dark = 统计暗目标
                    Add(list, "Polarity", "Bright");
                    Add(list, "MinArea", "100");
                    Add(list, "MaxArea", "0");     // 0 = 不限
                    Add(list, "MinWidth", "0");
                    Add(list, "MinHeight", "0");
                    Add(list, "MinCircularity", "0");   // 0 = 不筛（填充度口径，见执行器注释）
                    Add(list, "MaxCount", "50");
                    Add(list, "SortBy", "Area");
                    // 判定：期望个数范围 + 单个面积范围（0 = 不限）
                    Add(list, "ExpectMin", "0");
                    Add(list, "ExpectMax", "0");
                    Add(list, "AreaMin", "0");
                    Add(list, "AreaMax", "0");
                    break;

                case FlowNodeType.LineCaliper:
                    // 扫描线段（ROI，图像坐标）
                    Add(list, "X1", "100");
                    Add(list, "Y1", "100");
                    Add(list, "X2", "300");
                    Add(list, "Y2", "100");
                    Add(list, "CaliperCount", "10");
                    Add(list, "ProjectionLength", "20");
                    Add(list, "Polarity", "0");          // 0=Any 1=DarkToLight 2=LightToDark
                    Add(list, "EdgeThreshold", "30");
                    Add(list, "FilterHalfWidth", "2");
                    Add(list, "SelectMode", "0");        // 0=Strongest 1=First 2=Last
                    Add(list, "MinEdgeCount", "3");
                    Add(list, "RejectOutliers", "1");
                    Add(list, "OutlierTolerance", "2");
                    // 判定（0 = 不限）
                    Add(list, "LengthMin", "0");
                    Add(list, "LengthMax", "0");
                    Add(list, "AngleMin", "0");
                    Add(list, "AngleMax", "0");
                    Add(list, "MaxRms", "0");
                    break;

                case FlowNodeType.CircleCaliper:
                    // 环形 ROI（图像坐标）
                    Add(list, "Cx", "320");
                    Add(list, "Cy", "240");
                    Add(list, "InnerRadius", "80");
                    Add(list, "OuterRadius", "120");
                    Add(list, "StartAngle", "0");
                    Add(list, "AngleSpan", "360");
                    Add(list, "CaliperCount", "20");
                    Add(list, "ProjectionLength", "20");
                    Add(list, "Polarity", "0");
                    Add(list, "EdgeThreshold", "30");
                    Add(list, "FilterHalfWidth", "2");
                    Add(list, "SelectMode", "0");
                    Add(list, "MinEdgeCount", "5");
                    Add(list, "RejectOutliers", "1");
                    Add(list, "OutlierTolerance", "2");
                    // 判定（0 = 不限）
                    Add(list, "DiameterMin", "0");
                    Add(list, "DiameterMax", "0");
                    Add(list, "RoundnessMax", "0");
                    Add(list, "CenterToleranceX", "0");
                    Add(list, "CenterToleranceY", "0");
                    break;

                case FlowNodeType.Sharpness:
                    Add(list, "MinSharpness", "100");
                    break;

                case FlowNodeType.FitCircle:
                case FlowNodeType.FitLine:
                    // 最小轮廓面积过滤（0 = 不限；按拟合结果的等效面积做下限判定）
                    Add(list, "MinContourArea", "0");
                    // 是否在预览上绘制拟合结果
                    Add(list, "DrawShape", "1");
                    break;

                case FlowNodeType.ResultOutput:
                    break;

                case FlowNodeType.ObjectDetect:
                    // OpenVINO YOLO 模型路径（.onnx；Ultralytics 导出，输出 [1,4+nc,anchors]）
                    Add(list, "ModelPath", "models/yolo11n.onnx");
                    // 推理设备：CPU / GPU（GPU 需 OpenVINO 带对应插件）
                    Add(list, "Device", "CPU");
                    // 类别置信度阈值（0~1）
                    Add(list, "ScoreThresh", "0.25");
                    // NMS IoU 阈值（0~1）
                    Add(list, "IouThresh", "0.45");
                    // 最多保留的目标数
                    Add(list, "MaxCount", "100");
                    break;

                case FlowNodeType.Ocr:
                    // 字符模板目录（相对程序基目录，即 bin/x64/Release/OCRModel）
                    // 约定：每个字符一张灰度图，文件名（去扩展名）即标签，如 A.png / 7.png / 中.png
                    Add(list, "TemplateDir", "OCRModel");
                    // 二值化阈值（-1 = Otsu 自适应）
                    Add(list, "Threshold", "-1");
                    // 极性反转（0 = 自动判定极性（推荐）；1 = 强制反色）
                    Add(list, "Invert", "0");
                    // 字符高度下限 / 上限（像素，0 = 不限制）
                    Add(list, "MinCharH", "0");
                    Add(list, "MaxCharH", "0");
                    // 字符宽高比下限 / 上限（0 = 不限制）
                    Add(list, "MinAspect", "0");
                    Add(list, "MaxAspect", "0");
                    // 同行相邻字符最大间隙（像素）
                    Add(list, "MaxGap", "3");
                    // 归一化尺寸
                    Add(list, "NormW", "16");
                    Add(list, "NormH", "24");
                    // 匹配接受阈值（0~1）
                    Add(list, "AcceptScore", "0.6");
                    // 最多输出的字符数
                    Add(list, "MaxCount", "200");
                    // ROI（宽或高为 0 = 整帧）；角度绕 ROI 中心、顺时针为正，由执行器做仿射矫正
                    Add(list, "RoiX", "0");
                    Add(list, "RoiY", "0");
                    Add(list, "RoiW", "0");
                    Add(list, "RoiH", "0");
                    Add(list, "RoiAngle", "0");
                    // 角度跟随位置修正：θ 取「实测相对基准的偏差角」，关闭则只用 RoiAngle
                    Add(list, "FollowPose", "1");
                    // 结果显示（ShowBoxes 默认由 1 改为 0：默认少画框，老配置里的显式 1 不受影响）
                    Add(list, "ShowRegionBox", "1");
                    Add(list, "ShowStringBox", "1");
                    Add(list, "ShowBoxes", "0");
                    break;

                case FlowNodeType.SendData:
                    // 绑定的通讯名称（取自「通讯」界面的配置，下拉动态填充 NodeParamSchema）
                    Add(list, "CommName", "");
                    // 数据模式：FixedText = 固定文本；Format = 格式化模板；ModbusWrite = 写单个保持寄存器
                    Add(list, "DataMode", "FixedText");
                    // 固定文本内容
                    Add(list, "Text", "");
                    // 格式化模板：{Summary} 上游摘要 / {OkNg} OK·NG / {Time} 时间
                    Add(list, "Format", "{OkNg},{Summary}");
                    // Modbus 写寄存器（FC06）
                    Add(list, "MbAddress", "0");
                    Add(list, "MbValue", "0");
                    // 未连接时自动连接一次
                    Add(list, "AutoConnect", "1");
                    break;

                case FlowNodeType.ReceiveData:
                    // 绑定的通讯名称
                    Add(list, "CommName", "");
                    // 接收模式：TcpText = 取最近一条收到的文本；ModbusRead = 读保持寄存器（FC03）
                    Add(list, "Mode", "TcpText");
                    // 等待数据的超时（毫秒；0 = 不等待，只看当前有没有）
                    Add(list, "TimeoutMs", "2000");
                    // Modbus 读寄存器起始地址与数量（1~125）
                    Add(list, "MbAddress", "0");
                    Add(list, "MbCount", "1");
                    // 未连接时自动连接一次
                    Add(list, "AutoConnect", "1");
                    break;

                // ================= 本期新增：交点卡尺 =================
                // 两条扫描线各自是"直线卡尺"的 ROI；Line1/Line2 端子连了上游时，对应那组参数不生效。
                case FlowNodeType.LineIntersect:
                    Add(list, "Line1X1", "100");
                    Add(list, "Line1Y1", "100");
                    Add(list, "Line1X2", "300");
                    Add(list, "Line1Y2", "100");
                    Add(list, "Line1CaliperCount", "10");
                    Add(list, "Line1ProjectionLength", "20");
                    Add(list, "Line1Polarity", "0");
                    Add(list, "Line1EdgeThreshold", "30");

                    Add(list, "Line2X1", "200");
                    Add(list, "Line2Y1", "50");
                    Add(list, "Line2X2", "200");
                    Add(list, "Line2Y2", "250");
                    Add(list, "Line2CaliperCount", "10");
                    Add(list, "Line2ProjectionLength", "20");
                    Add(list, "Line2Polarity", "0");
                    Add(list, "Line2EdgeThreshold", "30");

                    Add(list, "FilterHalfWidth", "2");
                    Add(list, "SelectMode", "0");
                    Add(list, "MinEdgeCount", "3");
                    Add(list, "RejectOutliers", "1");
                    Add(list, "OutlierTolerance", "2");
                    Add(list, "MaxRms", "0");
                    break;

                // ================= 本期新增：边缘对查找 =================
                case FlowNodeType.EdgePair:
                    Add(list, "X1", "100");
                    Add(list, "Y1", "100");
                    Add(list, "X2", "300");
                    Add(list, "Y2", "100");
                    Add(list, "CaliperCount", "10");
                    Add(list, "ProjectionLength", "30");
                    Add(list, "EdgeThreshold", "20");
                    Add(list, "FilterHalfWidth", "2");
                    Add(list, "PairPolarity", "0");     // 0=亮线 1=暗线
                    Add(list, "MinWidth", "0");
                    Add(list, "MaxWidth", "0");
                    Add(list, "SelectMode", "0");       // 0=最强 1=最宽 2=最窄 3=最前
                    Add(list, "ExpectMin", "0");
                    Add(list, "ExpectMax", "0");
                    Add(list, "WidthMin", "0");
                    Add(list, "WidthMax", "0");
                    break;

                // ================= 本期新增：点点 / 点直线 / 线线测量 =================
                // 三者都只有"判定上下限"一类参数：几何来源走端子，不在参数里重复配。
                case FlowNodeType.PointPointMeasure:
                    Add(list, "DistMin", "0");
                    Add(list, "DistMax", "0");
                    break;

                case FlowNodeType.PointLineMeasure:
                    Add(list, "DistMin", "0");
                    Add(list, "DistMax", "0");
                    break;

                case FlowNodeType.LineLineMeasure:
                    Add(list, "ParallelTol", "0.5");    // 夹角小于该值视为平行（度）
                    Add(list, "AngleMin", "0");
                    Add(list, "AngleMax", "0");
                    Add(list, "DistMin", "0");
                    Add(list, "DistMax", "0");
                    break;
            }

            return list;
        }

        /// <summary>
        /// 只给"新建节点"用的覆盖值：老配置升级不能受影响
        /// （OCR 的模板目录就是典型 —— 改了会让老配置的模板全部"消失"）。
        /// </summary>
        public static Dictionary<string, string> NewNodeOverrides(FlowNodeType type)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (type == FlowNodeType.Ocr) map["TemplateDir"] = "OCRModel";
            return map;
        }

        private static void Add(List<ParamItem> list, string key, string value)
        {
            list.Add(new ParamItem(key, value));
        }
    }
}
