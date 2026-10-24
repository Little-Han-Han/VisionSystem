using System;
using System.Collections.Generic;
using VisionPlatform.Core;

namespace VisionPlatform.Camera
{
    /// <summary>
    /// 相机管理器：负责枚举设备、打开/关闭相机实例、维护当前相机。
    /// 上层只通过它拿 <see cref="ICamera"/>，不感知厂商差异。
    /// </summary>
    public sealed class CameraManager
    {
        private static readonly CameraManager _instance = new CameraManager();

        public static CameraManager Instance { get { return _instance; } }

        private readonly object _sync = new object();
        private readonly Dictionary<string, ICamera> _opened = new Dictionary<string, ICamera>();

        /// <summary>最近一次 <see cref="Enumerate"/> 的结果快照（从未枚举过则为 null）。</summary>
        private List<CameraInfo> _lastEnumerated;

        private CameraManager() { }

        /// <summary>当前选中的相机（可能为 null）。</summary>
        public ICamera Current { get; private set; }

        /// <summary>所有已打开的相机，按打开顺序排列（供多视图 / 同步采集使用）。</summary>
        public List<ICamera> OpenedCameras
        {
            get
            {
                lock (_sync)
                {
                    return new List<ICamera>(_opened.Values);
                }
            }
        }

        /// <summary>
        /// 最近一次 <see cref="Enumerate"/> 的结果快照（从未枚举过为 null，返回的是副本）。
        /// <para>【用途】启动期 Splash 的 <see cref="OpenAll"/> 内部已经枚举过一次；主窗体首帧补刷
        /// 相机下拉时复用这份结果即可，<b>不必在 UI 线程再同步枚举一次</b> —— 本机实测
        /// DirectShow 枚举单台相机就要 ≈1.1 秒，同步跑会把主窗体首帧冻住。</para>
        /// <para>需要「真实重新枚举」的地方（用户点「刷新」、相机管理器变更）仍直接调
        /// <see cref="Enumerate"/>，它会顺带刷新本快照。</para>
        /// </summary>
        public List<CameraInfo> LastEnumerated
        {
            get
            {
                lock (_sync)
                {
                    return _lastEnumerated == null ? null : new List<CameraInfo>(_lastEnumerated);
                }
            }
        }

        /// <summary>当前相机变化时触发。</summary>
        public event EventHandler CurrentChanged;

        /// <summary>枚举所有可用相机（本机摄像头 → 模拟相机 → 海康，取决于硬件/SDK）。</summary>
        public List<CameraInfo> Enumerate()
        {
            var list = new List<CameraInfo>();

            // 本机摄像头（USB / 内置摄像头），无设备时为空
            try
            {
                list.AddRange(LocalCamera.Enumerate());
            }
            catch (Exception ex)
            {
                Logger.Warn("枚举本机摄像头失败：" + ex.Message);
            }

            // 模拟相机：无硬件时的兜底设备
            var sim = new SimCamera();
            list.Add(sim.Info);

            // 海康相机
            try
            {
                list.AddRange(HikCamera.Enumerate());
            }
            catch (Exception ex)
            {
                Logger.Warn("枚举海康相机失败：" + ex.Message);
            }

            // 快照一份供 LastEnumerated 复用（启动期 Splash 已付过枚举代价，主窗体首帧不必再付一次）
            lock (_sync)
            {
                _lastEnumerated = new List<CameraInfo>(list);
            }

            return list;
        }

        /// <summary>按 Id 打开相机并设为当前相机。</summary>
        public OpResult<ICamera> Open(string id)
        {
            if (string.IsNullOrEmpty(id)) return OpResult<ICamera>.Fail("相机 Id 为空");

            lock (_sync)
            {
                ICamera existing;
                if (_opened.TryGetValue(id, out existing))
                {
                    SetCurrent(existing);
                    return OpResult<ICamera>.Ok(existing);
                }

                ICamera camera = CreateById(id);
                if (camera == null)
                    return OpResult<ICamera>.Fail("未知相机：" + id);

                OpResult r = camera.Open();
                if (!r.Success)
                {
                    camera.Dispose();
                    return OpResult<ICamera>.Fail(r.Message);
                }

                _opened[id] = camera;
                SetCurrent(camera);
                Logger.Info("相机已打开：" + id);
                return OpResult<ICamera>.Ok(camera);
            }
        }

        /// <summary>
        /// 枚举并<strong>打开全部</strong>相机（启动时调用，需求：软件启动时所有相机全部打开）。
        /// <para>逐台 try/catch：某台打不开只记 WARN，绝不影响其它相机，也绝不阻断启动。</para>
        /// <para><see cref="Open"/> 对已打开的相机是幂等的（直接返回已有实例），
        /// 因此重复调用不会重复打开、不会泄漏句柄。</para>
        /// </summary>
        /// <returns>本次成功打开的相机台数（已打开的也计入）。</returns>
        public int OpenAll()
        {
            List<CameraInfo> list;
            try
            {
                list = Enumerate();
            }
            catch (Exception ex)
            {
                Logger.Warn("启动打开相机失败（枚举异常）：" + ex.Message);
                return 0;
            }

            if (list == null || list.Count == 0)
            {
                Logger.Warn("启动打开相机：未枚举到任何相机");
                return 0;
            }

            int opened = 0;
            foreach (CameraInfo info in list)
            {
                if (info == null || string.IsNullOrEmpty(info.Id)) continue;

                try
                {
                    OpResult<ICamera> r = Open(info.Id);
                    if (r != null && r.Success) opened++;
                    else Logger.Warn("启动打开相机失败 " + info.Id + "：" + (r != null ? r.Message : "未知原因"));
                }
                catch (Exception ex)
                {
                    Logger.Warn("启动打开相机异常 " + info.Id + "：" + ex.Message);
                }
            }

            return opened;
        }

        /// <summary>关闭指定相机。</summary>
        public OpResult Close(string id)
        {
            lock (_sync)
            {
                ICamera camera;
                if (!_opened.TryGetValue(id, out camera)) return OpResult.Ok("未打开");

                camera.Close();
                camera.Dispose();
                _opened.Remove(id);

                if (ReferenceEquals(Current, camera))
                    SetCurrent(null);

                Logger.Info("相机已关闭：" + id);
                return OpResult.Ok();
            }
        }

        /// <summary>关闭全部相机。</summary>
        public void CloseAll()
        {
            lock (_sync)
            {
                foreach (KeyValuePair<string, ICamera> kv in _opened)
                {
                    try { kv.Value.Close(); kv.Value.Dispose(); }
                    catch (Exception ex) { Logger.Warn("关闭相机异常：" + ex.Message); }
                }
                _opened.Clear();
                SetCurrent(null);
            }
        }

        private ICamera CreateById(string id)
        {
            if (id.StartsWith("SIM", StringComparison.OrdinalIgnoreCase))
                return new SimCamera();

            // 本机摄像头：Id 形如 "USB-1"，后缀即设备序号
            if (id.StartsWith("USB", StringComparison.OrdinalIgnoreCase))
            {
                int index = 0;
                int dash = id.IndexOf('-');
                if (dash >= 0) int.TryParse(id.Substring(dash + 1), out index);
                return new LocalCamera(index);
            }

            if (id.StartsWith("HIK", StringComparison.OrdinalIgnoreCase))
                return new HikCamera(new CameraInfo { Id = id, Vendor = "Hik" });

            // 海康序列号通常是纯数字或字母数字混合，未识别前缀时按海康处理
            return new HikCamera(new CameraInfo { Id = id, Vendor = "Hik" });
        }

        private void SetCurrent(ICamera camera)
        {
            Current = camera;
            EventHandler handler = CurrentChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }
    }
}
