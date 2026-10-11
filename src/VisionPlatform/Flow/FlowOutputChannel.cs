using System;
using System.Collections.Generic;
using System.Drawing;
using VisionSystem.Controls.Geometry;

namespace VisionPlatform.Flow
{
    /// <summary>
    /// 流程运行结果事件参数（经静态通道回传主界面）。
    /// <para>
    /// <strong>兼容性约定</strong>：既有 6 个字段（<see cref="ViewIndex"/> / <see cref="Frame"/> /
    /// <see cref="Shapes"/> / <see cref="Passed"/> / <see cref="Summary"/> / <see cref="CameraName"/>）
    /// 一字不动，MainForm 既有订阅零回归；本轮只在末尾<strong>追加</strong> FlowId / FlowName / Product。
    /// </para>
    /// </summary>
    public sealed class FlowOutputEventArgs : EventArgs
    {
        /// <summary>目标显示视图序号（从 1 开始）。</summary>
        public int ViewIndex { get; set; }

        /// <summary>要显示的图像（<strong>所有权移交订阅者</strong>，订阅者用完后负责释放）。</summary>
        public Bitmap Frame { get; set; }

        /// <summary>叠加图形（图像坐标）。</summary>
        public List<IGeometryShape> Shapes { get; set; }

        /// <summary>总体结论。</summary>
        public bool Passed { get; set; }

        /// <summary>结论摘要文本。</summary>
        public string Summary { get; set; }

        /// <summary>相机名（用于视图标题）。</summary>
        public string CameraName { get; set; }

        // ===== 本轮追加（FR-PAR-04）：多流程并行时用于区分结果归属 =====

        /// <summary>产出该结果的流程 Id；"" = 未指明（兼容旧发布方）。</summary>
        public string FlowId { get; set; }

        /// <summary>流程名（流程名四处一致的单一真值源之一，FR-UI2-03）。</summary>
        public string FlowName { get; set; }

        /// <summary>流程所属产品型号；"" = 通用 / 未分类。</summary>
        public string Product { get; set; }

        /// <summary>初始化三个新字段为空串，保证旧发布方构造出的实例不会出现 null。</summary>
        public FlowOutputEventArgs()
        {
            FlowId = "";
            FlowName = "";
            Product = "";
        }
    }

    /// <summary>
    /// 流程结果回传通道（静态，仿 CommState / RecipeStore）：
    /// FlowForm / 并行引擎发布，MainForm 订阅 → 解耦，且不破坏 MainForm.ShowChild 的泛型无参构造约定。
    /// <para>
    /// 并行下会有多条流程线程同时发布，因此订阅 / 退订 / 发布全部加锁（共享知识 29.2）。
    /// </para>
    /// </summary>
    public static class FlowOutputChannel
    {
        /// <summary>订阅/退订/发布的统一锁（C10）。</summary>
        private static readonly object Sync = new object();

        private static event EventHandler<FlowOutputEventArgs> _outputReady;

        /// <summary>有流程结果产出时触发（线程安全）。</summary>
        public static event EventHandler<FlowOutputEventArgs> OutputReady
        {
            add { lock (Sync) { _outputReady += value; } }
            remove { lock (Sync) { _outputReady -= value; } }
        }

        /// <summary>
        /// 发布一次流程结果。无订阅者时静默丢弃（图像由发布方在无订阅者时自行处理）。
        /// </summary>
        /// <returns>是否有订阅者接收（false 时调用方应自行释放 <see cref="FlowOutputEventArgs.Frame"/>）。</returns>
        public static bool Publish(FlowOutputEventArgs e)
        {
            EventHandler<FlowOutputEventArgs> handler;
            lock (Sync) { handler = _outputReady; }

            if (handler == null) return false;

            try
            {
                handler(null, e);
                return true;
            }
            catch (Exception)
            {
                // 单个订阅者抛异常不应影响其它订阅者与发布方
                return true;
            }
        }
    }
}
