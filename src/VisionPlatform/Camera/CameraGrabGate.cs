using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using VisionPlatform.Core;

namespace VisionPlatform.Camera
{
    /// <summary>
    /// 取帧串行化门（FR-MC-04）：<strong>按 cameraId 加锁</strong>，禁止并发调用同一原生句柄。
    ///
    /// 【为什么必须有】多流程并行后，两条流程可能同时 <c>GrabOne</c> 同一台相机；
    /// 相机 SDK 的原生句柄通常不是线程安全的，并发取帧会直接崩在厂商 DLL 里。
    /// 因此流程取帧、视图预览取帧（<c>SyncGrabCore</c>/<c>OnGrabOnce</c>）、
    /// <c>FlowForm</c> 单步取帧三处统一走本门，<strong>禁止直接 camera.GrabOne()</strong>（共享知识 28）。
    ///
    /// 【不抛异常】等待锁超过 500 ms 只记 WARN，不抛异常——取帧慢不应该让整条流程崩掉。
    /// </summary>
    public static class CameraGrabGate
    {
        /// <summary>记 WARN 的锁等待阈值（ms）。</summary>
        public const int WarnWaitMs = 500;

        /// <summary>默认取帧超时（ms）。</summary>
        public const int DefaultTimeoutMs = 3000;

        private static readonly object MapSync = new object();

        /// <summary>cameraId → 该相机专用的锁对象。</summary>
        private static readonly Dictionary<string, object> Gates = new Dictionary<string, object>(StringComparer.Ordinal);

        /// <summary>
        /// 经门取一帧。内部按相机 Id 串行化后调用 <see cref="ICamera.GrabOne"/>。
        /// </summary>
        /// <param name="camera">目标相机；null 返回失败（不抛异常）。</param>
        /// <param name="timeoutMs">取帧超时（ms）。</param>
        /// <param name="flowName">归属流程名（仅用于 WARN 文案）；可为空。</param>
        public static OpResult<Bitmap> GrabOne(ICamera camera, int timeoutMs = DefaultTimeoutMs, string flowName = "")
        {
            if (camera == null) return OpResult<Bitmap>.Fail("相机为空");

            if (camera.Info == null || string.IsNullOrEmpty(camera.Info.Id))
                return OpResult<Bitmap>.Fail("相机信息缺失，无法取帧");

            object gate = GateFor(camera.Info.Id);

            var wait = Stopwatch.StartNew();
            bool taken = false;
            try
            {
                System.Threading.Monitor.TryEnter(gate, timeoutMs, ref taken);
                wait.Stop();

                if (!taken)
                {
                    Warn(flowName, camera, (int)wait.ElapsedMilliseconds, true);
                    return OpResult<Bitmap>.Fail(
                        string.Format("相机 {0} 取帧等待超时（{1} ms），可能存在并发取帧",
                            camera.Info.Name, (int)wait.ElapsedMilliseconds));
                }

                if (wait.ElapsedMilliseconds > WarnWaitMs)
                    Warn(flowName, camera, (int)wait.ElapsedMilliseconds, false);

                return camera.GrabOne(timeoutMs);
            }
            catch (Exception ex)
            {
                return OpResult<Bitmap>.Fail("取帧异常：" + ex.Message);
            }
            finally
            {
                if (taken) System.Threading.Monitor.Exit(gate);
            }
        }

        /// <summary>取（或创建）某相机专用的锁对象。</summary>
        private static object GateFor(string cameraId)
        {
            lock (MapSync)
            {
                object gate;
                if (Gates.TryGetValue(cameraId, out gate)) return gate;

                gate = new object();
                Gates[cameraId] = gate;
                return gate;
            }
        }

        private static void Warn(string flowName, ICamera camera, int waitedMs, bool timeout)
        {
            string name = (camera != null && camera.Info != null) ? camera.Info.Name : "?";

            string text = timeout
                ? string.Format("相机 {0} 取帧等待 {1} ms 后超时，可能存在并发取帧", name, waitedMs)
                : string.Format("相机 {0} 取帧等待 {1} ms，可能存在并发取帧", name, waitedMs);

            if (string.IsNullOrEmpty(flowName)) Logger.Warn(text);
            else Logger.Warn(flowName, text);
        }
    }
}
