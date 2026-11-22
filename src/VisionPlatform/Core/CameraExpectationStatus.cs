using System;
using System.Collections.Generic;
using System.Text;
using VisionPlatform.Camera;
using VisionPlatform.Flow;

namespace VisionPlatform.Core
{
    /// <summary>一台"被流程使用"的相机及其连接状态（供状态栏画彩色标签）。</summary>
    public sealed class CameraExpectationState
    {
        /// <summary>相机 Id（与流程里的 <c>CameraId</c> 一致，如 <c>USB-0</c>）。</summary>
        public string Name { get; set; }

        /// <summary>相机显示名（如"本机摄像头 0"），用于 tooltip。</summary>
        public string Detail { get; set; }

        /// <summary>序列号（有就放进 tooltip）。</summary>
        public string Serial { get; set; }

        /// <summary>是否已连接（已打开）。</summary>
        public bool Loaded { get; set; }
    }

    /// <summary>
    /// 流程相机连接状态：**按配方里实际用到相机的流程**统计，而不是按配置的相机清单。
    /// <para>
    /// 【为什么按流程而不是按配置】同一台相机可以被多条流程复用，读图片 / 读点云的流程
    /// 压根不需要相机。现场反馈：4 条流程只有 1 条接真实相机，底部就只该出现 1 个相机标签 ——
    /// 按配置数量显示 4 个会让人误以为"接了 4 台相机"。
    /// </para>
    /// <para>
    /// 过滤规则：流程启用 && <see cref="FlowRunner.NeedsCamera"/> 为 true（读图片/点云为 false）
    /// && <c>CameraId</c> 非空 && 不是已停用的模拟相机（SIM-*）。
    /// </para>
    /// </summary>
    public static class CameraExpectationStatus
    {
        /// <summary>逐台状态（按流程顺序、去重）。</summary>
        public static List<CameraExpectationState> GetStates()
        {
            var result = new List<CameraExpectationState>();
            List<string> ids = CollectFlowCameraIds();
            if (ids.Count == 0) return result;

            List<ICamera> opened;
            try
            {
                opened = new List<ICamera>(CameraManager.Instance.OpenedCameras);
            }
            catch (Exception)
            {
                opened = new List<ICamera>();
            }

            foreach (string id in ids)
            {
                ICamera cam = FindOpened(opened, id);
                result.Add(new CameraExpectationState
                {
                    Name = id,
                    Detail = (cam != null && cam.Info != null) ? cam.Info.Name : "",
                    Serial = (cam != null && cam.Info != null) ? cam.Info.Serial : "",
                    Loaded = cam != null
                });
            }
            return result;
        }

        /// <summary>
        /// 收集配方里"需要相机"的流程用到的 CameraId（去重、保持流程顺序）。
        /// </summary>
        public static List<string> CollectFlowCameraIds()
        {
            var ids = new List<string>();
            try
            {
                AppConfig cfg = ConfigStore.Current;
                if (cfg == null || cfg.Flows == null || cfg.Flows.Flows == null) return ids;

                foreach (FlowDefinition flow in cfg.Flows.Flows)
                {
                    if (flow == null || !flow.Enabled) continue;
                    if (!FlowRunner.NeedsCamera(flow)) continue;        // 读图片 / 读点云：与相机无关

                    string id = (flow.CameraId ?? "").Trim();
                    if (id.Length == 0) continue;

                    // 模拟相机已停用：老配方里的 SIM-0 是历史残留，不该显示成"未连接"
                    if (id.StartsWith("SIM", StringComparison.OrdinalIgnoreCase)) continue;

                    bool exists = false;
                    foreach (string known in ids)
                    {
                        if (string.Equals(known, id, StringComparison.OrdinalIgnoreCase)) { exists = true; break; }
                    }
                    if (!exists) ids.Add(id);
                }
            }
            catch (Exception)
            {
                // 配置不可读时返回已收集部分
            }
            return ids;
        }

        /// <summary>纯文本形式（日志 / 排障用）：<c>USB-0 已连接 | HIK-1 未连接</c>。</summary>
        public static string BuildStatusText()
        {
            List<CameraExpectationState> states = GetStates();
            if (states.Count == 0) return "";

            var parts = new List<string>(states.Count);
            foreach (CameraExpectationState st in states)
                parts.Add(st.Name + (st.Loaded ? " 已连接" : " 未连接"));
            return string.Join(" | ", parts.ToArray());
        }

        /// <summary>是否有"流程要用但没连上"的相机（可用于状态栏整体告警色）。</summary>
        public static bool HasMissing()
        {
            foreach (CameraExpectationState st in GetStates())
            {
                if (!st.Loaded) return true;
            }
            return false;
        }

        /// <summary>在已打开相机里按 Id 找（大小写不敏感）。</summary>
        private static ICamera FindOpened(List<ICamera> opened, string id)
        {
            foreach (ICamera cam in opened)
            {
                if (cam == null || cam.Info == null) continue;
                if (string.Equals(cam.Info.Id, id, StringComparison.OrdinalIgnoreCase)) return cam;
            }
            return null;
        }

        /// <summary>给「相机管理」/日志用的可读清单。</summary>
        public static string DescribeAll()
        {
            List<CameraExpectationState> states = GetStates();
            if (states.Count == 0) return "配方里没有需要相机的流程";

            var sb = new StringBuilder();
            foreach (CameraExpectationState st in states)
            {
                sb.Append(st.Name);
                if (!string.IsNullOrEmpty(st.Detail)) sb.Append("（").Append(st.Detail).Append("）");
                sb.Append("：").Append(st.Loaded ? "已连接" : "未连接");
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd();
        }
    }
}
