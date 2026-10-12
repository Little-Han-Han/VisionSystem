using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using VisionPlatform.Camera;
using VisionPlatform.Core;
using VisionSystem.Controls;

namespace VisionPlatform.UI
{
    /// <summary>相机管理：枚举、打开/关闭、连续采集与单帧抓取，并在右侧预览。</summary>
    public partial class CameraForm : Form
    {
        private ImageViewer _viewer;
        private ICamera _camera;

        // 覆盖式投递：后台线程只保留「最新一帧」，避免 BeginInvoke 消息队列积压 Bitmap
        private Bitmap _pendingFrame;
        private int _pendingPosted;    // 0 = 队列中无待显示帧；1 = 已投递一帧待处理
        private bool _viewerFitted;    // 首个实时帧做一次 FitToWindow，确保看到完整画面

        public CameraForm()
        {
            InitializeComponent();

            CreateViewer();
            RefreshList();

            UiStyle.ApplyForm(this);

            // 语义色：打开/开始采集=绿，关闭/停止=红，单帧=蓝，刷新=中性
            btnOpen.Kind = UiButtonKind.Start;
            btnStart.Kind = UiButtonKind.Start;
            btnClose.Kind = UiButtonKind.Stop;
            btnStop.Kind = UiButtonKind.Stop;
            btnGrab.Kind = UiButtonKind.Grab;
        }

        private void CreateViewer()
        {
            _viewer = new ImageViewer();
            _viewer.Dock = DockStyle.Fill;
            _viewer.ShowHud = true;
            _viewer.Options.ShowCrosshair = true;

            panelRight.Controls.Add(_viewer);
            panelRight.Controls.SetChildIndex(_viewer, 0);
        }

        private CameraInfo SelectedInfo
        {
            get { return listCameras.SelectedItem as CameraInfo; }
        }

        private void RefreshList()
        {
            listCameras.BeginUpdate();
            listCameras.Items.Clear();

            List<CameraInfo> infos = CameraManager.Instance.Enumerate();

            bool hasUsb = false;
            foreach (CameraInfo info in infos)
            {
                listCameras.Items.Add(info);
                if (info != null && !string.IsNullOrEmpty(info.Vendor) &&
                    info.Vendor.StartsWith("USB", StringComparison.OrdinalIgnoreCase))
                    hasUsb = true;
            }

            if (listCameras.Items.Count > 0 && listCameras.SelectedIndex < 0)
                listCameras.SelectedIndex = 0;

            listCameras.EndUpdate();
            UpdateButtons();

            // C2：一台 USB 相机都没枚举到时，把原生给出的原因展示出来，
            //     不再让用户面对「只剩模拟相机」却不知为何（占用 / 未连接 / 驱动）。
            if (!hasUsb && _camera == null)
            {
                string reason = LocalCamera.LastEnumReason;
                lblStatus.Text = string.IsNullOrEmpty(reason)
                    ? "未检测到 USB 相机：可能被其他程序（视频会议 / 监控 / 相机应用）占用，或驱动不可用。"
                    : ("未检测到 USB 相机：" + reason);
            }

            Logger.Info("相机列表已刷新：" + infos.Count + " 个");
        }

        private void UpdateButtons()
        {
            bool has = (_camera != null);
            btnOpen.Enabled = (SelectedInfo != null) && !has;
            btnClose.Enabled = has;
            btnStart.Enabled = has && !_camera.IsGrabbing;
            btnStop.Enabled = has && _camera.IsGrabbing;
            btnGrab.Enabled = has;

            if (has)
            {
                string res = string.IsNullOrEmpty(_camera.Info.Model) ? "默认" : _camera.Info.Model;
                lblStatus.Text = string.Format("{0}{1}来源：{2}    分辨率：{3}{1}{4}",
                    _camera.Info, Environment.NewLine,
                    SourceName(_camera.Info.Vendor), res,
                    _camera.IsGrabbing ? "采集中…" : "已停止");
            }
            else
            {
                lblStatus.Text = (SelectedInfo != null) ? ("已选择：" + SelectedInfo) : "未选择相机";
            }
        }

        /// <summary>相机厂商 → 中文来源名（列表前缀由 CameraInfo.ToString 的 [Vendor] 统一呈现）。</summary>
        private static string SourceName(string vendor)
        {
            if (string.IsNullOrEmpty(vendor)) return "未知";
            if (vendor.StartsWith("USB", StringComparison.OrdinalIgnoreCase)) return "本机摄像头";
            if (vendor.StartsWith("Sim", StringComparison.OrdinalIgnoreCase)) return "模拟相机";
            if (vendor.StartsWith("Hik", StringComparison.OrdinalIgnoreCase)) return "海康相机";
            return vendor;
        }

        private void OnRefresh(object sender, EventArgs e) { RefreshList(); }

        private void OnSelectionChanged(object sender, EventArgs e) { UpdateButtons(); }

        private void OnOpen(object sender, EventArgs e)
        {
            CameraInfo info = SelectedInfo;
            if (info == null) { MessageBox.Show(this, "请先选择一个相机。"); return; }

            var r = CameraManager.Instance.Open(info.Id);
            if (!r.Success)
            {
                MessageBox.Show(this, r.Message, "打开失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                lblStatus.Text = "打开失败：" + r.Message;
                return;
            }

            _camera = r.Value;
            _camera.FrameReceived += OnFrame;
            _viewerFitted = false;
            _camera.ErrorOccurred += (s, msg) =>
            {
                Logger.Error("相机错误：" + msg);
                if (!IsDisposed && IsHandleCreated)
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (!IsDisposed && IsHandleCreated)
                            lblStatus.Text = "相机错误：" + msg;
                    });
                }
            };

            UpdateButtons();
        }

        private void OnClose(object sender, EventArgs e)
        {
            if (_camera == null) return;

            string id = _camera.Info.Id;
            _camera.FrameReceived -= OnFrame;
            CameraManager.Instance.Close(id);
            _camera = null;
            UpdateButtons();
        }

        private void OnStart(object sender, EventArgs e)
        {
            if (_camera == null) return;
            var r = _camera.StartGrab();
            if (!r.Success)
                MessageBox.Show(this, r.Message, "启动采集失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            UpdateButtons();
        }

        private void OnStop(object sender, EventArgs e)
        {
            if (_camera == null) return;
            _camera.StopGrab();
            UpdateButtons();
        }

        private void OnGrab(object sender, EventArgs e)
        {
            if (_camera == null) { MessageBox.Show(this, "请先打开相机。"); return; }

            var r = _camera.GrabOne();
            if (!r.Success)
            {
                MessageBox.Show(this, r.Message, "取帧失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _viewer.SetImage(r.Value, false);
            _viewer.FitToWindow();
        }

        /// <summary>
        /// 采集线程回调：覆盖式投递到 UI 线程显示。
        /// 只保留最新一帧，队列中已有待显示帧时直接丢弃本帧 → 防止高帧率下 Bitmap 积压。
        /// </summary>
        private void OnFrame(object sender, Bitmap frame)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                if (frame != null) frame.Dispose();
                return;
            }

            // 用最新帧替换待显示帧（旧帧立即释放）
            Bitmap old = Interlocked.Exchange(ref _pendingFrame, frame);
            if (old != null) old.Dispose();

            // 队列里已有一帧待处理：丢弃本帧
            if (Interlocked.Exchange(ref _pendingPosted, 1) == 1) return;

            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    Bitmap bmp = Interlocked.Exchange(ref _pendingFrame, null);
                    Interlocked.Exchange(ref _pendingPosted, 0);

                    if (bmp == null) return;
                    if (IsDisposed || !IsHandleCreated) { bmp.Dispose(); return; }

                    try {
                        _viewer.SetImage(bmp, false);
                        if (!_viewerFitted) { _viewer.FitToWindow(); _viewerFitted = true; }
                    }
                    catch (Exception ex) { bmp.Dispose(); Logger.Warn("显示帧失败：" + ex.Message); }
                });
            }
            catch (Exception)
            {
                // 窗口已销毁：清空投递状态并释放待显示帧
                Interlocked.Exchange(ref _pendingPosted, 0);
                Bitmap bmp = Interlocked.Exchange(ref _pendingFrame, null);
                if (bmp != null) bmp.Dispose();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (_camera != null)
            {
                _camera.FrameReceived -= OnFrame;
                _camera = null;
            }

            // 释放可能残留的待显示帧，避免泄漏
            Bitmap bmp = Interlocked.Exchange(ref _pendingFrame, null);
            Interlocked.Exchange(ref _pendingPosted, 0);
            if (bmp != null) bmp.Dispose();
        }
    }
}
