using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace VisionPlatform.Flow
{
    /// <summary>
    /// 节点类型的"运行控制"特征（宿主与模块包共用，避免两处各写一份判断）。
    /// </summary>
    /// <remarks>
    /// 【为什么放在 Framework】同一个判断有两个消费方：模块包用它决定"要不要给参数表加 TimeoutMs"，
    /// 宿主用它决定"这个节点的耗时要不要按超时判"。两处各写一份，迟早出现
    /// "界面上有这个参数、运行时不判"（或反过来），而这种不一致最难自查。
    /// </remarks>
    public static class FlowNodeTraits
    {
        /// <summary>
        /// 该节点类型是否带「算法超时」（<c>TimeoutMs</c>，默认 500ms）。
        /// </summary>
        /// <remarks>
        /// 以下 4 类排除，各有明确理由，<strong>不是"还没做"</strong>：
        /// <list type="bullet">
        /// <item>图像源 —— 自带的 <c>TimeoutMs</c> 是"等一帧"的超时（3000），语义不同，不能混；</item>
        /// <item>发送 / 接收数据 —— 同上，是通讯应答超时；</item>
        /// <item>结果输出 —— 只做字符串汇总，微秒级。</item>
        /// </list>
        /// <para>
        /// 【脚本已纳入】它也是最需要限时的地方（用户代码可能死循环）。虽然同步调用无法中途打断，
        /// 但"跑完按耗时判 NG"仍然有值 —— 至少不会把一帧 8 秒的脚本判成 OK 往下游传。
        /// </para>
        /// </remarks>
        public static bool HasAlgoTimeout(FlowNodeType type)
        {
            switch (type)
            {
                case FlowNodeType.ImageSource:
                case FlowNodeType.SendData:
                case FlowNodeType.ReceiveData:
                case FlowNodeType.ResultOutput:
                    return false;
                default:
                    return true;
            }
        }
    }

    /// <summary>
    /// 流程节点类型（仅内存使用；序列化时以字符串形式存入 <see cref="FlowNode.Type"/>）。
    /// </summary>
    /// <remarks>
    /// <strong>只能在枚举末尾追加</strong>：节点类型以「枚举名的字符串」持久化在
    /// appconfig.json 中，改动既有成员的顺序或名字会让旧流程解析失败。
    /// 前 8 项为本期之前的既有类型，后 4 项为本期追加。
    /// 未知类型在运行时按「跳过」处理，不会让整条流程加载失败。
    /// </remarks>
    public enum FlowNodeType
    {
        ImageSource,
        Preprocess,
        ShapeMatch,
        Blob,
        Sharpness,
        FitCircle,
        FitLine,
        ResultOutput,

        // ===== 本期追加（追加在末尾，保证既有序号与字符串不变）=====
        /// <summary>模板匹配（NCC，原生 va_match）：仅支持平移，不支持旋转 / 缩放。</summary>
        TemplateMatch,

        /// <summary>位置修正：输出 dX / dY / dAngle / dScale 并写入执行期 RunTransform。</summary>
        PositionCorrect,

        /// <summary>直线卡尺（原生 va_line_caliper）：投影找边 + 直线拟合 + 直线度 RMS。</summary>
        LineCaliper,

        /// <summary>圆卡尺（原生 va_circle_caliper）：径向投影找边 + 圆拟合 + 圆度 RMS。</summary>
        CircleCaliper,

        // ===== 本期追加（同样加在末尾，保证既有序号与字符串不变）=====

        /// <summary>图像滤波（原生 va_filter）：平滑 / 锐化 + 可选形态学，支持灰度与彩色两种链路。</summary>
        ImageFilter,

        /// <summary>颜色提取（原生 va_color_extract）：BGR / HSV 阈值分割 → 连通域统计。</summary>
        ColorExtractor,

        // ===== 本期追加（追加在末尾，保证既有序号与字符串不变）=====

        /// <summary>目标识别（OpenVINO YOLO，原生 va_det_*）：深度学习目标检测，输出检测框（类别 / 分数 / 坐标）。</summary>
        ObjectDetect,

        // ===== 本期追加（追加在末尾，保证既有序号与字符串不变）=====

        /// <summary>字符识别（传统算法 OCR，原生 va_ocr_*）：字符分割 + 模板匹配，模板为「一个字符一张图、文件名即标签」。</summary>
        Ocr,

        // ===== 本期追加（追加在末尾，保证既有序号与字符串不变）=====

        /// <summary>发送数据：按名称绑定「通讯」界面里已配置的一条通讯，发送固定文本 / 格式化文本 / 写 Modbus 寄存器。</summary>
        SendData,

        /// <summary>接收数据：按名称绑定「通讯」界面里已配置的一条通讯，读取最近一条文本或读 Modbus 保持寄存器。</summary>
        ReceiveData,

        /// <summary>
        /// 脚本节点：用 C# 方法体读取<strong>所有上游节点的输出</strong>（图像尺寸 / 位姿 / 数量 / 分数 / 文本 / 几何数），
        /// 计算结果写回结果区。<c>api</c> 是 <see cref="ScriptApi"/> 实例，<c>return</c> 的字符串即结果摘要。
        /// </summary>
        Script,

        /// <summary>深度学习：目标语义分割（YOLO26-seg，OpenVINO 推理）。输出每个目标的框 + 掩膜面积。</summary>
        DlSeg,

        /// <summary>深度学习：目标姿态估计（YOLO26-pose），输出框 + 关键点。</summary>
        DlPose,

        /// <summary>深度学习：深度估计，输出 0~1 归一化深度图。</summary>
        DlDepth,

        /// <summary>
        /// 深度学习 OCR（PaddleOCR）：DB 文本检测 + CRNN 识别，两阶段。
        /// <para>与传统 OCR（模板匹配，<see cref="Ocr"/>）是两套东西，互不影响。</para>
        /// </summary>
        OcrDl,

        /// <summary>标定转换：像素坐标 → 机械坐标（吃 N 点标定 / 旋转中心）。</summary>
        CalibTransform,

        /// <summary>畸变矫正：用相机内参去畸变（吃畸变矫正记录）。</summary>
        DistortionCorrect,

        // ===== 本期追加（测量与交点类，同样加在末尾，保证既有序号与字符串不变）=====

        /// <summary>
        /// 交点卡尺（双直线卡尺找交点）：两条直线卡尺各自找线 → 求延长线交点。
        /// <para>两条线可来自上游「直线卡尺」的 Geometry 输出，也可由本节点内置的两组扫描线现场找边。</para>
        /// </summary>
        LineIntersect,

        /// <summary>边缘对查找：沿扫描线法向一维取样，按极性把「一升一降」配成对，输出线宽。</summary>
        EdgePair,

        /// <summary>点点测量：两点距离 / 方向角 / 中点。</summary>
        PointPointMeasure,

        /// <summary>点直线测量：点到直线的垂距 / 垂足。</summary>
        PointLineMeasure,

        /// <summary>线线测量：两直线夹角 / 交点 / 平行间距。</summary>
        LineLineMeasure,

        // ===== 本期追加（同上，加在末尾）=====

        /// <summary>
        /// 条码 / 二维码识别（zxing-cpp）：不需要模板，一次可识别多个码，
        /// 输出文本、码制、四角点与旋转框。支持一维码（Code128/39/93/Codabar/EAN/UPC/ITF/DataBar）
        /// 与二维码（QR/MicroQR/DataMatrix/Aztec/PDF417/MaxiCode）。
        /// </summary>
        BarcodeRead,

        // ===== 本期追加（3D：点云，加在末尾）=====

        /// <summary>
        /// 读取点云：从 PLY / PCD 文件（单文件或文件夹轮播）或深度图（内参反投影）产出点云。
        /// <para>点云随快照直达显示视图 —— 与 2D 共用同一个 ImageViewer，进入 3D 模式后
        /// 拖动旋转 / 滚轮缩放 / 双击复位。</para>
        /// </summary>
        PointCloudSource,

        // ===== 本期追加（无监督缺陷检测，加在末尾）=====

        /// <summary>
        /// 无监督缺陷检测（PaDiM / EfficientAD，OpenVINO 推理）：
        /// 只用 OK 样本建模型，运行时输出**异常热图 + 缺陷参数**（最大分 / 面积占比 / 缺陷数 / 外接框）。
        /// <para>输入：图像 + 位置修正（可选）；输出：热图 + 缺陷参数。</para>
        /// </summary>
        AnomalyDetect,
}
}
