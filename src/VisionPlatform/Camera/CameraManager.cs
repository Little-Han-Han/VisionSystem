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

        /// <summary>当前相机变化时触发。</summary>
        public event EventHandler CurrentChanged;

        /// <summary>枚举所有可用相机（模拟相机始终存在，海康取决于 SDK）。</summary>
        public List<CameraInfo> Enumerate()
        {
            var list = new List<CameraInfo>();

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
