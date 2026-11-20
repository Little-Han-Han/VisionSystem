using System;
using System.Threading;
using VisionPlatform.Core;

namespace VisionPlatform.Communication
{
    /// <summary>
    /// 通讯运行闸门：<strong>连接 / 心跳 / 触发轮询 / Busy·Ready 写入只在「运行态」发生</strong>。
    /// <para>
    /// 【为什么要这道闸】改造前通讯任务引擎在软件启动时就全速跑：心跳每 1s 写点位、边沿轮询每 100ms
    /// 读触发位、状态绑定每 100ms 写 Busy/Ready。用户停在主界面做维护（甚至还没登录）时，
    /// 这些线程仍在刷通讯、刷日志、抢 CPU，且 PLC 掉线时还会持续制造连接风暴。
    /// 现在统一由主界面「运行 / 暂停 / 停止」驱动：
    /// 运行 → <see cref="Activate"/>；暂停或停止 → <see cref="Suspend"/>。
    /// </para>
    /// <para>
    /// 【暂停的语义】只停"刷"，<strong>不断开已建立的连接</strong>：
    /// 继续运行不必重连，PLC / MES 侧也看不到链路抖动。
    /// </para>
    /// <para>
    /// 【手动连接不受闸门限制】「通讯」界面上的连接按钮是调试入口，任何时候点了都会立刻尝试；
    /// 闸门只管"自动"这一路（自动连接、自动重连、常驻任务）。
    /// </para>
    /// </summary>
    public static class CommRuntime
    {
        private static volatile bool _active;

        /// <summary>是否处于运行态（主界面点「运行」后为 true）。</summary>
        public static bool IsActive
        {
            get { return _active; }
        }

        /// <summary>
        /// 进入运行态：建立通讯对象、启动常驻任务循环，并在后台把所有
        /// 「启用 + 自动连接」的端点连上（不阻塞调用线程 —— 调用点通常是 UI 线程）。
        /// <para>可重入：已在运行态时直接返回，不会重复连接。</para>
        /// </summary>
        /// <param name="connectAll">是否顺带把自动连接的端点连上（测试可传 false 只开闸）。</param>
        public static void Activate(bool connectAll = true)
        {
            if (_active) return;

            // 【顺序要紧】先建对象再开闸：Rebuild 的首次自动连接看到 IsActive=true 就会在
            // 调用线程上同步连（Modbus 连不上要等 2s），那会把点「运行」的 UI 冻住。
            try { CommHub.Initialize(); }
            catch (Exception ex) { Logger.Warn("[comm] 运行态初始化通讯失败：" + ex.Message); }

            try { CommJobEngine.Initialize(); }
            catch (Exception ex) { Logger.Warn("[comm] 运行态初始化常驻任务失败：" + ex.Message); }

            // 按「当前」配置重载任务表：停机期间改过通讯 / 任务配置时，运行必须用新的一套，
            // 否则常驻任务还在按上次运行的旧快照跑（Initialize 是幂等的，不会自己重载）。
            try { CommJobEngine.Reload(); }
            catch (Exception ex) { Logger.Warn("[comm] 运行态重载任务失败：" + ex.Message); }

            _active = true;

            try { CommJobEngine.OnActivated(); }
            catch (Exception ex) { Logger.Warn("[comm] 运行态启动任务失败：" + ex.Message); }

            if (connectAll)
                ThreadPool.QueueUserWorkItem(delegate { ConnectAllAuto(); });

            Logger.Info("[comm] 进入运行态：自动连接 / 心跳 / 轮询 / 状态写入已启用");
        }

        /// <summary>
        /// 退出运行态（暂停或停止）：常驻任务循环挂起、自动连接与自动重连被拒绝；
        /// 已建立的连接保活，不主动断开。
        /// </summary>
        public static void Suspend()
        {
            if (!_active) return;

            _active = false;
            Logger.Info("[comm] 退出运行态：自动连接 / 心跳 / 轮询 / 状态写入已暂停（连接保活）");
        }

        /// <summary>把「启用 + 自动连接 + 尚未连接」的端点连一遍；失败只记日志，不弹窗。</summary>
        private static void ConnectAllAuto()
        {
            try
            {
                foreach (string name in CommHub.Names())
                {
                    if (!_active) return;                  // 连到一半被暂停：立刻收手

                    CommEndpointConfig cfg = CommHub.Config(name);
                    if (cfg == null || !cfg.Enabled || !cfg.AutoConnect) continue;
                    if (CommHub.IsConnected(name)) continue;

                    OpResult r = CommHub.Connect(name);
                    if (!r.Success) Logger.Warn("[comm] 运行态自动连接失败 " + name + "：" + r.Message);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("[comm] 运行态批量连接异常：" + ex.Message);
            }
        }
    }
}
