using System;
using System.Collections.Generic;

namespace VisionPlatform.Camera
{
    /// <summary>
    /// 相机运行期租约登记表（<c>cameraId → 占用它的 flowId</c>），FR-MC-03。
    ///
    /// 【设计要点】
    /// - **纯运行期**，不持久化；进程重启即清空。
    /// - **平行注册表**：不改动 <see cref="CameraManager"/>，避免影响既有相机管理链路（零回归）。
    /// - <c>cameraId</c> 为空（本地图片模式）不占租约，直接放行。
    /// - 所有读写加锁；<see cref="Snapshot"/> 返回副本，调用方不可修改内部表。
    /// </summary>
    public static class CameraLease
    {
        private static readonly object Sync = new object();

        /// <summary>cameraId → 占用者 flowId。</summary>
        private static readonly Dictionary<string, string> Owners = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// 尝试为 <paramref name="flowId"/> 申请 <paramref name="cameraId"/> 的独占租约。
        /// </summary>
        /// <param name="cameraId">相机 Id；空 = 本地图片模式，直接放行。</param>
        /// <param name="flowId">申请者流程 Id。</param>
        /// <param name="ownerFlowId">被拒绝时返回当前占用者；成功时为 null。</param>
        /// <returns>true = 申请成功（或无需租约）；false = 已被其它流程占用。</returns>
        public static bool TryAcquire(string cameraId, string flowId, out string ownerFlowId)
        {
            ownerFlowId = null;

            // 本地图片模式：不涉及硬件，不占租约
            if (string.IsNullOrEmpty(cameraId)) return true;
            if (string.IsNullOrEmpty(flowId)) return false;

            lock (Sync)
            {
                string owner;
                if (Owners.TryGetValue(cameraId, out owner))
                {
                    // 同一流程重复申请视为持有（幂等）
                    if (string.Equals(owner, flowId, StringComparison.Ordinal)) return true;

                    ownerFlowId = owner;
                    return false;
                }

                Owners[cameraId] = flowId;
                return true;
            }
        }

        /// <summary>释放 <paramref name="flowId"/> 对 <paramref name="cameraId"/> 的租约（只释放自己持有的）。</summary>
        public static void Release(string cameraId, string flowId)
        {
            if (string.IsNullOrEmpty(cameraId)) return;

            lock (Sync)
            {
                string owner;
                if (!Owners.TryGetValue(cameraId, out owner)) return;
                if (!string.Equals(owner, flowId, StringComparison.Ordinal)) return;

                Owners.Remove(cameraId);
            }
        }

        /// <summary>释放某条流程持有的<strong>全部</strong>租约（停止 / 关窗时调用）。</summary>
        public static void ReleaseAll(string flowId)
        {
            if (string.IsNullOrEmpty(flowId)) return;

            lock (Sync)
            {
                var victims = new List<string>();
                foreach (KeyValuePair<string, string> kv in Owners)
                {
                    if (string.Equals(kv.Value, flowId, StringComparison.Ordinal)) victims.Add(kv.Key);
                }

                foreach (string id in victims) Owners.Remove(id);
            }
        }

        /// <summary>查询某相机的占用者；null = 空闲。</summary>
        public static string OwnerOf(string cameraId)
        {
            if (string.IsNullOrEmpty(cameraId)) return null;

            lock (Sync)
            {
                string owner;
                return Owners.TryGetValue(cameraId, out owner) ? owner : null;
            }
        }

        /// <summary>某相机是否已被占用。</summary>
        public static bool IsOccupied(string cameraId)
        {
            return OwnerOf(cameraId) != null;
        }

        /// <summary>当前租约表快照（副本）。</summary>
        public static Dictionary<string, string> Snapshot()
        {
            lock (Sync)
            {
                return new Dictionary<string, string>(Owners, StringComparer.Ordinal);
            }
        }

        /// <summary>
        /// 强制清空全部租约。仅供关窗 / 引擎 <c>Shutdown</c> 使用（此时各槽线程已 Join 完毕）。
        /// </summary>
        public static void Clear()
        {
            lock (Sync) { Owners.Clear(); }
        }
    }
}
