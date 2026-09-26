using System;
using System.Collections.Generic;
using System.Drawing;
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

        public CameraForm()
        {
            InitializeComponent();

            CreateViewer();
            RefreshList();
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
            foreach (CameraInfo info in infos)
                listCameras.Items.Add(info);

            if (listCameras.Items.Count > 0 && listCameras.SelectedIndex < 0)
                listCameras.SelectedIndex = 0;

            listCameras.EndUpdate();
            UpdateButtons();
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

            lblStatus.Text = has
                ? (_camera.Info + Environment.NewLine + (_camera.IsGrabbing ? "采集中…" : "已停止"))
                : (SelectedInfo != null ? ("已选择：" + SelectedInfo) : "未选择相机");
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
            _camera.ErrorOccurred += (s, msg) => Logger.Error("相机错误：" + msg);

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

        /// <summary>采集线程回调：切回 UI 线程显示。</summary>
        private void OnFrame(object sender, Bitmap frame)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                if (frame != null) frame.Dispose();
                return;
            }

            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    try { _viewer.SetImage(frame, false); }
                    catch (Exception ex) { Logger.Warn("显示帧失败：" + ex.Message); }
                });
            }
            catch (Exception)
            {
                if (frame != null) frame.Dispose();
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
        }
    }
}
