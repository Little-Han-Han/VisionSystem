using System;
using System.Drawing;
using VisionPlatform.Core;

namespace VisionPlatform.Camera
{
    /// <summary>
    /// 相机统一接口。上层业务流程只依赖本接口，
    /// 换相机厂商时只需新增一个实现类，无需改动算法与界面。
    /// </summary>
    public interface ICamera : IDisposable
    {
        /// <summary>相机描述。</summary>
        CameraInfo Info { get; }

        bool IsOpen { get; }
        bool IsGrabbing { get; }

        /// <summary>打开相机。</summary>
        OpResult Open();

        /// <summary>关闭相机。</summary>
        OpResult Close();

        /// <summary>开始连续采集（触发 <see cref="FrameReceived"/>）。</summary>
        OpResult StartGrab();

        /// <summary>停止连续采集。</summary>
        OpResult StopGrab();

        /// <summary>软触发取一帧（超时返回失败）。</summary>
        OpResult<Bitmap> GrabOne(int timeoutMs = 100000);

        /// <summary>连续采集模式下收到新帧。</summary>
        event EventHandler<Bitmap> FrameReceived;

        /// <summary>相机异常。</summary>
        event EventHandler<string> ErrorOccurred;
    }
}
