using System;
using System.Threading;
using VisionPlatform.Core;

namespace VisionPlatform.Flow
{
    /// <summary>
    /// 循环运行的限帧策略：把「目标帧率」换算成最小周期。
    /// <para>
    /// 【为什么不再写死 33ms】旧版所有循环都被硬编码为 ≤30fps；只要算法快于 33ms，
    /// 就会被人工 Sleep 压到 30fps，看起来"比 Halcon 慢"。
    /// 现在由 <see cref="UiSettings.MaxFlowFps"/> 控制：0 = 不限制，30/60 = 主动限帧。
    /// </para>
    /// </summary>
    internal static class FlowRateLimit
    {
        /// <summary>读取配置并换算最小周期（ms）。0 = 不限制。</summary>
        public static int ResolveMinCycleMs()
        {
            try
            {
                AppConfig cfg = ConfigStore.Current;
                int fps = (cfg != null && cfg.Ui != null) ? cfg.Ui.MaxFlowFps : 0;
                if (fps <= 0) return 0;
                if (fps > UiSettings.MaxFlowFpsLimit) fps = UiSettings.MaxFlowFpsLimit;
                return (int)Math.Ceiling(1000.0 / fps);
            }
            catch (Exception)
            {
                // 配置不可用时按"不限制"处理，绝不因为读配置失败把流程压慢
                return 0;
            }
        }

        /// <summary>按最小周期补等待。0 = 只让出一次时间片，不做固定睡眠。</summary>
        public static void Wait(int elapsedMs, int minCycleMs)
        {
            if (minCycleMs <= 0)
            {
                Thread.Yield();
                return;
            }

            int wait = minCycleMs - elapsedMs;
            if (wait <= 0) return;

            try { Thread.Sleep(wait); }
            catch (Exception) { }
        }
    }
}
