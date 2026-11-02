using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using VisionPlatform.Calibration;
using VisionPlatform.Camera;
using VisionPlatform.Communication;
using VisionPlatform.Core;

namespace VisionPlatform.UI
{
    /// <summary>
    /// 相机标定窗体（左侧参数 / 右侧图像预览，风格沿用 <see cref="FlowForm"/> 的分栏做法）。
    /// <para>
    /// 【本轮范围】只做 <b>N 点标定</b>：图像像素坐标 ↔ 机械坐标 的最小二乘相似变换
    /// （旋转 + 尺度 + 平移），纯 C# 解算（<see cref="CalibSolver"/>），不依赖 C++ 算法库。
    /// 相机畸变矫正与旋转中心标定<b>本轮不做</b>，参数区只放一个「下一轮实现」占位标签
    /// （不放会崩的假 UI）。
    /// </para>
    /// <para>
    /// 【多记录】同一台相机支持多条标定记录（名称可改），由
    /// <see cref="CalibSettings.CurrentByCamera"/> 指定「当前生效」的一条；
    /// 记录整体落在 <c>AppConfig.Calibrations</c>，随 <see cref="ConfigStore"/> 持久化。
    /// </para>
    /// <para>
    /// 【不持有连接】通讯读写走 <see cref="CommRegistry"/> 的公开 API（经
    /// <see cref="CommQuery"/> 拼装成一问一答），相机取帧走 <see cref="CameraManager"/>，
    /// 窗体关闭不会打断主界面或流程已建立的链路。
    /// </para>
    /// <para>
    /// 【为什么没有重写 OnResize】左栏四个分区由 <see cref="TableLayoutPanel"/> 按比例分配，
    /// 图像区靠 Dock 填充 —— 全程不需要手写尺寸计算，也就没有
    /// 「构造期设 Height 触发 OnResize、此时字段还没 new」的空引用风险
    /// （项目铁律 4 的反面：能不做 OnResize 就不做）。
    /// </para>
    /// </summary>
    public partial class CalibForm : Form
    {
        /// <summary>「读取坐标」指令模板的默认值（现场最常见的位置查询指令）。</summary>
        private const string DefaultCommand = "POS?";

        /// <summary>实时预览的刷新间隔（毫秒）。</summary>
        private const int LiveIntervalMs = 250;

        /// <summary>实时预览取帧超时（毫秒）：短超时保证 UI 不卡。</summary>
        private const int LiveGrabTimeoutMs = 1500;

        /// <summary>手动触发取帧超时（毫秒）。</summary>
        private const int ManualGrabTimeoutMs = 3000;

        /// <summary>等待通讯回复的超时（毫秒）。</summary>
        private const int CommReplyTimeoutMs = 2000;

        /// <summary>N 点表格的列数（像素X / 像素Y / 机械X / 机械Y）。</summary>
        private const int PointColumnCount = 4;

        /// <summary>实时预览定时器（Tick 在 UI 线程；未开启实时时停止）。</summary>
        private Timer _liveTimer;

        /// <summary>抓帧 / 读坐标的重入保护：慢相机上按住手动触发不会叠出一堆取帧线程。</summary>
        private bool _busy;

        /// <summary>正在回填界面（抑制由此引发的选中 / 保存事件）。</summary>
        private bool _loading;
        private DataGridView gridRecords;
        private UiButton btnRotationCenter;

        /// <summary>标定配置段（与 <see cref="ConfigStore"/> 里的对象同一个引用）。</summary>
        private CalibSettings _settings;

        /// <summary>当前选中的相机 Id。</summary>
        private string _cameraId = "";

        /// <summary>当前选中的标定记录 Id（空 = 未选中）。</summary>
        private string _currentRecordId = "";

        /// <summary>已解析到的相机实例缓存（避免每次取帧都走 CameraManager.Open 触发全量重枚举）。</summary>
        private ICamera _openedCamera;

        /// <summary>
        /// 各记录已录入的点（记录 Id → 点集合）。仅存在于本次会话内存里：
        /// 记录被切走再切回来不会丢失正在录入的点，但<b>不落盘</b>
        /// （配置里只保存解算结果，避免把原始采样点写进 appconfig.json）。
        /// </summary>
        private readonly Dictionary<string, List<CalibPoint>> _pointCache =
            new Dictionary<string, List<CalibPoint>>(StringComparer.Ordinal);

        public CalibForm()
        {
            InitializeComponent();

            // 整窗体统一深色主题（必须在 InitializeComponent 之后、语义色之前）
            BuildRecordGrid();
            BuildRotationCenterButton();
            UiStyle.ApplyForm(this);

            // 自绘深色标题栏（与其余子窗体一致；幂等、内部已 try/catch）
            UiTitleBar.Attach(this);

            InitRecordList();
            InitPointGrid();
            InitLiveTimer();

            LoadSettings();
            RefreshCameraCombo();
            RefreshCommCombo();
            RefreshRecordList();

            ApplySemanticColors();
            UpdateButtons();

            Log("就绪：选择相机 → 录入 N 点对应关系 → 「计算」得出 RMS / 偏移 / 精度。");

            // 程序化 Dispose（不走 Close）时 OnFormClosed 不触发，定时器必须在这里一并释放
            this.Disposed += OnFormDisposed;
        }

        // ==================== 初始化 ====================

        /// <summary>标定列表外观（<see cref="UiStyle"/> 不处理 ListView，这里手工对齐主题）。</summary>
        private void InitRecordList()
        {
        }

        /// <summary>N 点表格的四列。</summary>
        private void InitPointGrid()
        {
            gridPoints.Columns.Clear();

            gridPoints.Columns.Add(NewPointColumn("colPixelX", "像素X (px)"));
            gridPoints.Columns.Add(NewPointColumn("colPixelY", "像素Y (px)"));
            gridPoints.Columns.Add(NewPointColumn("colMachineX", "机械X (mm)"));
            gridPoints.Columns.Add(NewPointColumn("colMachineY", "机械Y (mm)"));

            gridPoints.AllowUserToAddRows = false;
        }

        /// <summary>造一列等宽数值列（四列 FillWeight 相同，缩放时同步变宽）。</summary>
        private static DataGridViewTextBoxColumn NewPointColumn(string name, string header)
        {
            return new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                FillWeight = 25F,
                MinimumWidth = 72,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                ValueType = typeof(string)
            };
        }

        /// <summary>实时预览定时器（不 Start，由「实时」勾选框控制）。</summary>
        private void InitLiveTimer()
        {
            _liveTimer = new Timer();
            _liveTimer.Interval = LiveIntervalMs;
            _liveTimer.Tick += OnLiveTick;
        }

        /// <summary>
        /// 取标定配置段。旧配置文件没有 Calibrations 段时<b>就地补默认</b>
        /// （只在内存里补，不在这里写盘：打开窗体不该产生副作用）。
        /// </summary>
        private void LoadSettings()
        {
            CalibSettings settings = ConfigStore.Current.Calibrations;
            if (settings == null)
            {
                settings = CalibSettings.CreateDefault();
                ConfigStore.Current.Calibrations = settings;
            }

            CalibSettings.Normalize(settings);
            _settings = settings;
        }

        /// <summary>语义色：读取/新建=蓝，计算/设为当前=绿，删除=红，其余中性。</summary>
        private void ApplySemanticColors()
        {
            btnGrabFrame.Kind = UiButtonKind.Grab;
            btnRefreshCam.Kind = UiButtonKind.Neutral;

            btnNewRecord.Kind = UiButtonKind.Grab;
            btnRenameRecord.Kind = UiButtonKind.Neutral;
            btnDeleteRecord.Kind = UiButtonKind.Stop;
            btnSetCurrent.Kind = UiButtonKind.Start;

            btnReadCoord.Kind = UiButtonKind.Grab;
            btnAddPoint.Kind = UiButtonKind.Neutral;
            btnDelPoint.Kind = UiButtonKind.Neutral;
            btnCalc.Kind = UiButtonKind.Start;

            // 说明性文字统一用次文本色 + 小字（严禁 FontBadgeSmall：那是 14pt 大徽标字体）
            lblCamera.ForeColor = UiTheme.ColorTextSecondary;
            lblComm.ForeColor = UiTheme.ColorTextSecondary;
            lblCmd.ForeColor = UiTheme.ColorTextSecondary;
            lblStatus.ForeColor = UiTheme.ColorTextSecondary;
            lblStatus.Font = UiTheme.FontSmall;
            lblTodo.ForeColor = UiTheme.ColorTextSecondary;
            lblTodo.Font = UiTheme.FontSmall;

            // 结果区：等宽字体 + 卡片底色（与日志区同一套只读观感）
            UiTheme.StyleReadonlyBox(txtResult);

            txtCmd.Text = DefaultCommand;

            imageViewer.OwnsImage = true;          // 抓到的 Bitmap 交给控件托管，换图时自动 Dispose
            imageViewer.AutoFitOnResize = true;

            if (_settings != null && _settings.Records.Count == 0)
                Log("当前相机还没有标定记录：点「新建」或直接录入点后点「计算」会自动创建一条。");
        }

        // ==================== 相机 ====================

        /// <summary>
        /// 填充相机下拉。优先复用 <see cref="CameraManager.LastEnumerated"/> 的缓存
        /// （DirectShow 枚举单台就要 ≈1.1 s，构造期绝不能在 UI 线程重新枚举）。
        /// </summary>
        private void RefreshCameraCombo()
        {
            string keepId = _cameraId;

            _loading = true;
            try
            {
                cboCamera.BeginUpdate();
                cboCamera.Items.Clear();

                List<CameraInfo> infos = CameraManager.Instance.LastEnumerated;
                if (infos == null) infos = CameraManager.Instance.Enumerate();

                for (int i = 0; i < infos.Count; i++)
                {
                    CameraInfo info = infos[i];
                    if (info == null || string.IsNullOrEmpty(info.Id)) continue;
                    cboCamera.Items.Add(info);
                }

                int select = -1;
                for (int i = 0; i < cboCamera.Items.Count; i++)
                {
                    CameraInfo item = cboCamera.Items[i] as CameraInfo;
                    if (item != null && string.Equals(item.Id, keepId, StringComparison.Ordinal)) { select = i; break; }
                }
                if (select < 0 && cboCamera.Items.Count > 0) select = 0;

                if (select >= 0) cboCamera.SelectedIndex = select;
            }
            finally
            {
                cboCamera.EndUpdate();
                _loading = false;
            }

            SelectCamera(SelectedCameraId);
        }

        /// <summary>当前下拉选中的相机 Id（无选中为空串）。</summary>
        private string SelectedCameraId
        {
            get
            {
                CameraInfo info = cboCamera.SelectedItem as CameraInfo;
                return info != null ? (info.Id ?? "") : "";
            }
        }

        /// <summary>切换相机：清掉相机实例缓存，按新相机重刷记录列表。</summary>
        private void SelectCamera(string cameraId)
        {
            _cameraId = cameraId ?? "";
            _openedCamera = null;
            _currentRecordId = "";
            RefreshRecordList();

            if (string.IsNullOrEmpty(_cameraId)) Log("未选择相机。");
            else Log("已选择相机：" + _cameraId);
        }

        /// <summary>「刷新」：真实重新枚举一次（用户明确点了才付这个代价）。</summary>
        private void OnRefreshCameras(object sender, EventArgs e)
        {
            try
            {
                CameraManager.Instance.Enumerate();
            }
            catch (Exception ex)
            {
                Logger.Warn("标定窗体刷新相机列表失败：" + ex.Message);
            }

            RefreshCameraCombo();
            UpdateButtons();
            Log("相机列表已刷新，共 " + cboCamera.Items.Count.ToString(CultureInfo.InvariantCulture) + " 台。");
        }

        private void OnCameraSelectionChanged(object sender, EventArgs e)
        {
            if (_loading) return;
            SelectCamera(SelectedCameraId);
            UpdateButtons();
        }

        // ==================== 标定记录 ====================

        /// <summary>按当前相机重刷列表；尽量保持原选中项，「当前生效」那条带 ★ 前缀。</summary>
        private void RefreshRecordList()
        {
            if (_settings == null) return;

            string keep = _currentRecordId;
            int select = -1;

            // 【必须】清空/重选会触发 SelectedIndexChanged → SyncFromSelection → 又去刷列表，
            // 用户点「新建」时直接递归到 StackOverflow（进程崩溃）。这里用 _loading 切断。
            _loading = true;
            try
            {
                if (gridRecords == null) return;

                gridRecords.Rows.Clear();

                List<CalibRecord> records = _settings.RecordsFor(_cameraId);
                string currentId = _settings.CurrentIdFor(_cameraId);

                for (int i = 0; i < records.Count; i++)
                {
                    CalibRecord rec = records[i];
                    bool isCurrent = string.Equals(rec.Id, currentId, StringComparison.Ordinal);

                    int idx = gridRecords.Rows.Add(
                        (isCurrent ? "★ " : "") + (i + 1).ToString(CultureInfo.InvariantCulture),
                        rec.Name ?? "",
                        rec.KindDisplay,
                        rec.HasResult ? rec.RmsError.ToString("F4", CultureInfo.InvariantCulture) : "-",
                        rec.CreatedAt > DateTime.MinValue
                            ? rec.CreatedAt.ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                            : "-",
                        rec.CameraId ?? "",
                        string.IsNullOrEmpty(rec.FilePath) ? "（未落盘）" : rec.FilePath);

                    gridRecords.Rows[idx].Tag = rec.Id;
                    if (string.Equals(rec.Id, keep, StringComparison.Ordinal)) select = idx;
                }

                if (select < 0 && gridRecords.Rows.Count > 0) select = 0;

                gridRecords.ClearSelection();
                if (select >= 0 && select < gridRecords.Rows.Count)
                {
                    gridRecords.Rows[select].Selected = true;
                    gridRecords.CurrentCell = gridRecords.Rows[select].Cells[1];
                }
            }
            finally
            {
                _loading = false;
            }

            // BeginUpdate 期间选中事件可能不触发，这里显式同步一次界面状态
            SyncFromSelection();
        }

        /// <summary>把列表选中项同步到「当前记录 / 点表 / 结果区」。</summary>
        private void SyncFromSelection()
        {
            DataGridViewRow row = (gridRecords != null && gridRecords.SelectedRows.Count > 0)
                ? gridRecords.SelectedRows[0] : null;
            string id = (row != null && row.Tag is string) ? (string)row.Tag : "";

            bool changed = !string.Equals(id, _currentRecordId, StringComparison.Ordinal);
            if (!changed)
            {
                UpdateButtons();
                return;
            }

            // 切走之前先把正在录入的点存进会话缓存，切回来还能继续
            CacheCurrentGridPoints();

            _currentRecordId = id;
            LoadPointsIntoGrid(id);
            ShowRecordResult(_settings != null ? _settings.Find(id) : null);
            UpdateButtons();
        }

        private void OnRecordSelectionChanged(object sender, EventArgs e)
        {
            if (_loading) return;
            SyncFromSelection();
        }

        /// <summary>
        /// 标定记录表：序号 / 名称（可双击改）/ 类型 / RMS / 时间 / 相机 / 路径。
        /// <para>
        /// 【为什么换掉 ListView】ListView 不能原地编辑、列宽不合适时无法横向滚动查看，
        /// 用户看不到"路径"这种长字段。DataGridView 两样都支持。
        /// 原来那个 ListView 在 Designer 里，这里直接把它从容器移除（不留孤儿控件）。
        /// </para>
        /// </summary>
        private void BuildRecordGrid()
        {
            if (listRecords != null)
            {
                listRecords.Visible = false;
                if (listRecords.Parent != null) listRecords.Parent.Controls.Remove(listRecords);
            }

            gridRecords = new DataGridView
            {
                Dock = DockStyle.Fill,
                Name = "gridRecords",
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                ScrollBars = ScrollBars.Both,          // 列放不下时能左右拖
                BorderStyle = BorderStyle.FixedSingle,
                BackgroundColor = UiTheme.ColorViewBg,
                GridColor = UiTheme.ColorCardBg,
                Font = UiTheme.FontSmall,
                ColumnHeadersHeight = 30
            };

            gridRecords.DefaultCellStyle.BackColor = UiTheme.ColorViewBg;
            gridRecords.DefaultCellStyle.ForeColor = UiTheme.ColorTextPrimary;
            gridRecords.DefaultCellStyle.SelectionBackColor = UiTheme.ColorHeaderBg;
            gridRecords.DefaultCellStyle.SelectionForeColor = UiTheme.ColorTextPrimary;
            gridRecords.ColumnHeadersDefaultCellStyle.BackColor = UiTheme.ColorHeaderBg;
            gridRecords.ColumnHeadersDefaultCellStyle.ForeColor = UiTheme.ColorTextSecondary;
            gridRecords.EnableHeadersVisualStyles = false;

            AddRecordColumn("#", 46, true);
            AddRecordColumn("名称", 150, false);      // 只有名称可编辑
            AddRecordColumn("类型", 78, true);
            AddRecordColumn("RMS", 70, true);
            AddRecordColumn("时间", 132, true);
            AddRecordColumn("相机", 96, true);
            AddRecordColumn("路径", 320, true);

            gridRecords.SelectionChanged += OnRecordGridSelectionChanged;
            gridRecords.CellEndEdit += OnRecordGridCellEndEdit;

            if (listRecords != null && listRecords.Parent != null)
                listRecords.Parent.Controls.Add(gridRecords);
        }

        private void AddRecordColumn(string header, int width, bool readOnly)
        {
            var col = new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                Width = width,
                ReadOnly = readOnly,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                Resizable = DataGridViewTriState.True
            };
            if (readOnly) col.DefaultCellStyle.ForeColor = UiTheme.ColorTextSecondary;
            gridRecords.Columns.Add(col);
        }

        private void OnRecordGridSelectionChanged(object sender, EventArgs e)
        {
            if (_loading) return;
            SyncFromSelection();
        }

        /// <summary>双击名称列改名：直接落盘，不用再点「重命名」。</summary>
        private void OnRecordGridCellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (_loading || e.RowIndex < 0 || e.ColumnIndex != 1) return;
            if (gridRecords.Rows[e.RowIndex].Tag == null) return;

            string id = (string)gridRecords.Rows[e.RowIndex].Tag;
            string newName = Convert.ToString(gridRecords.Rows[e.RowIndex].Cells[1].Value ?? "").Trim();

            CalibRecord rec = _settings != null ? _settings.Find(id) : null;
            if (rec == null) return;

            if (string.IsNullOrEmpty(newName))
            {
                Log("名称不能为空，已还原。");
                RefreshRecordList();
                return;
            }
            if (string.Equals(rec.Name, newName, StringComparison.Ordinal)) return;

            rec.Name = newName;
            SaveSettings();
            RefreshRecordList();
            Log("已重命名为：" + newName);
        }

        /// <summary>
        /// 「旋转中心」按钮：放在「计算」旁边，用点表里的<strong>机械 X/Y</strong> 拟合回转中心。
        /// <para>
        /// 复用同一张点表：N 点标定用 4 列（像素+机械），旋转中心只用机械两列。
        /// 这样不用再挤一个表出来，采点方式对现场也更一致（转一个角度采一个点，直接填机械坐标）。
        /// </para>
        /// </summary>
        private void BuildRotationCenterButton()
        {
            if (btnCalc == null || btnCalc.Parent == null) return;

            btnRotationCenter = new UiButton
            {
                Text = "旋转中心",
                Location = new System.Drawing.Point(btnCalc.Left + btnCalc.Width + 8, btnCalc.Top),
                Size = new System.Drawing.Size(96, btnCalc.Height),
                Kind = UiButtonKind.Grab
            };
            btnRotationCenter.Click += OnCalcRotationCenter;

            btnCalc.Parent.Controls.Add(btnRotationCenter);
        }

        private void OnCalcRotationCenter(object sender, EventArgs e)
        {
            try
            {
                DoCalcRotationCenter();
            }
            catch (Exception ex)
            {
                Logger.Error("计算旋转中心失败：" + ex);
                Log("计算旋转中心失败：" + ex.Message);
            }
        }

        private void DoCalcRotationCenter()
        {
            if (_settings == null) return;
            if (string.IsNullOrEmpty(_cameraId)) { Log("请先选择相机。"); return; }

            // 取点表里"机械 X/Y"两列（第 3、4 列），空行跳过
            var xs = new List<double>();
            var ys = new List<double>();

            for (int i = 0; i < gridPoints.Rows.Count; i++)
            {
                string sx = Convert.ToString(gridPoints.Rows[i].Cells[2].Value ?? "").Trim();
                string sy = Convert.ToString(gridPoints.Rows[i].Cells[3].Value ?? "").Trim();
                if (sx.Length == 0 || sy.Length == 0) continue;

                double x, y;
                if (!double.TryParse(sx, System.Globalization.NumberStyles.Float,
                                     System.Globalization.CultureInfo.InvariantCulture, out x)) continue;
                if (!double.TryParse(sy, System.Globalization.NumberStyles.Float,
                                     System.Globalization.CultureInfo.InvariantCulture, out y)) continue;

                xs.Add(x); ys.Add(y);
            }

            RotationCenterSolver.Result r = RotationCenterSolver.Fit(xs.ToArray(), ys.ToArray(), xs.Count);
            if (!r.Success)
            {
                Log("旋转中心：" + r.Message);
                txtResult.Text = "旋转中心计算失败：" + Environment.NewLine + r.Message;
                return;
            }

            CalibRecord rec = CurrentRecord;
            if (rec == null)
            {
                rec = CalibRecord.Create(_cameraId, _settings.NextName(_cameraId), CalibKind.RotationCenter);
                rec.Kind = CalibKind.RotationCenter;
                _settings.Records.Add(rec);
                _settings.SetCurrent(rec);
                _currentRecordId = rec.Id;
            }

            rec.Kind = CalibKind.RotationCenter;
            rec.PointCount = r.PointCount;
            rec.RmsError = r.Rms;
            rec.MaxOffset = r.MaxError;
            rec.MeanOffset = r.Rms;
            rec.Precision = r.Rms;
            // 复用 Affine 前三个槽：圆心 X、圆心 Y、半径
            rec.Affine = new double[CalibRecord.AffineLength];
            rec.Affine[0] = r.CenterX;
            rec.Affine[1] = r.CenterY;
            rec.Affine[2] = r.Radius;
            if (rec.CreatedAt <= DateTime.MinValue) rec.CreatedAt = DateTime.Now;

            SaveSettings();
            RefreshRecordList();

            txtResult.Text = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "旋转中心（{0} 个点）" + Environment.NewLine +
                "圆心 X = {1:F4}" + Environment.NewLine +
                "圆心 Y = {2:F4}" + Environment.NewLine +
                "回转半径 = {3:F4}" + Environment.NewLine +
                "残差 RMS = {4:F4}" + Environment.NewLine +
                "最大偏差 = {5:F4}",
                r.PointCount, r.CenterX, r.CenterY, r.Radius, r.Rms, r.MaxError);

            Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "旋转中心：({0:F4}, {1:F4}) 半径 {2:F4} RMS {3:F4}",
                r.CenterX, r.CenterY, r.Radius, r.Rms));
        }

        private void OnNewRecord(object sender, EventArgs e)
        {
            try
            {
                DoNewRecord();
            }
            catch (Exception ex)
            {
                Logger.Error("新建标定记录失败：" + ex);
                Log("新建失败：" + ex.Message);
            }
        }

        private void DoNewRecord()
        {
            if (_settings == null) return;
            if (string.IsNullOrEmpty(_cameraId))
            {
                Log("请先选择相机，再新建标定记录。");
                return;
            }

            CalibRecord rec = CalibRecord.Create(_cameraId, _settings.NextName(_cameraId), CalibKind.NPoint);
            _settings.Records.Add(rec);
            _settings.SetCurrent(rec);          // 新建即设为当前，省一步
            _currentRecordId = rec.Id;

            SaveSettings();
            RefreshRecordList();
            Log("已新建标定记录：" + rec.Name + "（已设为当前）");
        }

        private void OnRenameRecord(object sender, EventArgs e)
        {
            CalibRecord rec = CurrentRecord;
            if (rec == null)
            {
                Log("请先在列表里选中一条标定记录。");
                return;
            }

            string newName = PromptText("重命名标定", "新的标定名称：", rec.Name);
            if (newName == null) return;                 // 取消

            if (newName.Length == 0)
            {
                Log("名称不能为空，未做修改。");
                return;
            }

            if (string.Equals(newName, rec.Name, StringComparison.Ordinal))
            {
                Log("名称未变化。");
                return;
            }

            rec.Name = newName;
            SaveSettings();
            RefreshRecordList();
            Log("已重命名为：" + newName);
        }

        private void OnDeleteRecord(object sender, EventArgs e)
        {
            CalibRecord rec = CurrentRecord;
            if (rec == null || _settings == null)
            {
                Log("请先在列表里选中一条标定记录。");
                return;
            }

            DialogResult confirm = MessageBox.Show(this,
                "确定删除标定记录「" + rec.Name + "」？" + Environment.NewLine + "该操作不可撤销。",
                "删除标定", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes) return;

            CalibStore.Delete(rec);          // 记录删了，它的标定文件也别留
            _settings.RemoveRecord(rec.Id);
            _pointCache.Remove(rec.Id);
            _currentRecordId = "";

            SaveSettings();
            RefreshRecordList();
            Log("已删除标定记录：" + rec.Name);
        }

        private void OnSetCurrentRecord(object sender, EventArgs e)
        {
            CalibRecord rec = CurrentRecord;
            if (rec == null || _settings == null)
            {
                Log("请先在列表里选中一条标定记录。");
                return;
            }

            _settings.SetCurrent(rec);
            SaveSettings();
            RefreshRecordList();
            Log("已把「" + rec.Name + "」设为相机 " + _cameraId + " 的当前标定。");
        }

        /// <summary>当前选中的记录（未选中返回 null）。</summary>
        private CalibRecord CurrentRecord
        {
            get { return _settings != null ? _settings.Find(_currentRecordId) : null; }
        }

        /// <summary>没有选中记录时自动建一条（用户直接录点就点「计算」也不至于白填）。</summary>
        private CalibRecord EnsureCurrentRecord()
        {
            CalibRecord rec = CurrentRecord;
            if (rec != null) return rec;

            if (_settings == null || string.IsNullOrEmpty(_cameraId)) return null;

            rec = CalibRecord.Create(_cameraId, _settings.NextName(_cameraId), CalibKind.NPoint);
            _settings.Records.Add(rec);
            _settings.SetCurrent(rec);
            _currentRecordId = rec.Id;
            return rec;
        }

        /// <summary>把标定配置段写回全局配置并落盘（<see cref="ConfigStore.Save()"/> 内部已 try/catch）。</summary>
        private void SaveSettings()
        {
            if (_settings == null) return;

            CalibSettings.Normalize(_settings);

            // 每条标定单独落一个文件：appconfig 里只留索引与相对路径，
            // 单条标定能整个拷给别人，标定变多也不撑大主配置。
            if (_settings.Records != null)
            {
                for (int i = 0; i < _settings.Records.Count; i++)
                {
                    CalibRecord rec = _settings.Records[i];
                    if (rec == null || !rec.HasResult) continue;

                    OpResult wr = CalibStore.Save(rec);
                    if (!wr.Success) Log("标定文件保存失败：" + wr.Message);
                }
            }

            AppConfig cfg = ConfigStore.Current;
            cfg.Calibrations = _settings;
            ConfigStore.Save();
        }

        // ==================== 点表 ====================

        /// <summary>把某条记录已录入的点回填进表格；没有点就留一行空白供输入。</summary>
        private void LoadPointsIntoGrid(string recordId)
        {
            List<CalibPoint> points = null;
            if (!string.IsNullOrEmpty(recordId)) _pointCache.TryGetValue(recordId, out points);

            gridPoints.Rows.Clear();

            if (points != null)
            {
                for (int i = 0; i < points.Count; i++)
                {
                    gridPoints.Rows.Add(
                        FormatNumber(points[i].PixelX),
                        FormatNumber(points[i].PixelY),
                        FormatNumber(points[i].MachineX),
                        FormatNumber(points[i].MachineY));
                }
            }

            if (gridPoints.Rows.Count == 0) gridPoints.Rows.Add();
        }

        /// <summary>把表格里当前录入的点存进会话缓存（切记录时调用，只存有效行）。</summary>
        private void CacheCurrentGridPoints()
        {
            if (string.IsNullOrEmpty(_currentRecordId)) return;

            List<CalibPoint> points;
            int skipped;
            ReadPointsFromGrid(out points, out skipped);

            if (points.Count > 0) _pointCache[_currentRecordId] = points;
            else _pointCache.Remove(_currentRecordId);
        }

        /// <summary>
        /// 读表格里的点：四个单元格都能解析成数值才算一行；整行为空则静默跳过；
        /// 填了一半的行计入 <paramref name="skipped"/> 由调用方提示用户。
        /// </summary>
        private void ReadPointsFromGrid(out List<CalibPoint> points, out int skipped)
        {
            points = new List<CalibPoint>();
            skipped = 0;

            for (int i = 0; i < gridPoints.Rows.Count; i++)
            {
                DataGridViewRow row = gridPoints.Rows[i];
                if (row == null || row.IsNewRow) continue;

                string[] texts = new string[PointColumnCount];
                bool allEmpty = true;
                for (int c = 0; c < PointColumnCount; c++)
                {
                    texts[c] = CellText(row, c);
                    if (texts[c].Length > 0) allEmpty = false;
                }
                if (allEmpty) continue;

                // 四个单元格逐个解析（不用 && 链：短路会让后面的 out 变量"未赋值"编译报错）
                double pixelX = 0.0, pixelY = 0.0, machineX = 0.0, machineY = 0.0;
                bool okPixelX = TryParseNumber(texts[0], out pixelX);
                bool okPixelY = TryParseNumber(texts[1], out pixelY);
                bool okMachineX = TryParseNumber(texts[2], out machineX);
                bool okMachineY = TryParseNumber(texts[3], out machineY);

                if (!(okPixelX && okPixelY && okMachineX && okMachineY)) { skipped++; continue; }

                CalibPoint point = new CalibPoint(pixelX, pixelY, machineX, machineY);
                if (!point.IsValid) { skipped++; continue; }

                points.Add(point);
            }
        }

        private static string CellText(DataGridViewRow row, int columnIndex)
        {
            if (row == null || columnIndex < 0 || columnIndex >= row.Cells.Count) return "";

            object value = row.Cells[columnIndex].Value;
            if (value == null) return "";

            string text = value as string;
            if (text == null)
            {
                IFormattable formattable = value as IFormattable;
                text = formattable != null
                    ? formattable.ToString(null, CultureInfo.CurrentCulture)
                    : Convert.ToString(value, CultureInfo.CurrentCulture);
            }
            return (text ?? "").Trim();
        }

        private static bool TryParseNumber(string text, out double value)
        {
            value = 0.0;
            if (string.IsNullOrEmpty(text)) return false;

            string trimmed = text.Trim();
            if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return true;
            return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private void OnAddPointRow(object sender, EventArgs e)
        {
            gridPoints.Rows.Add();
            Log("已添加一行。");
        }

        private void OnDeletePointRow(object sender, EventArgs e)
        {
            DataGridViewRow row = gridPoints.CurrentRow;
            if (row == null || row.IsNewRow)
            {
                Log("请先选中要删除的行。");
                return;
            }

            gridPoints.Rows.Remove(row);
            if (gridPoints.Rows.Count == 0) gridPoints.Rows.Add();
            Log("已删除一行。");
        }

        // ==================== 计算 ====================

        private void OnCalculate(object sender, EventArgs e)
        {
            if (_settings == null) return;

            List<CalibPoint> points;
            int skipped;
            ReadPointsFromGrid(out points, out skipped);

            if (points.Count < CalibSolver.MinPointCount)
            {
                string message = "至少需要 " + CalibSolver.MinPointCount.ToString(CultureInfo.InvariantCulture)
                                 + " 个有效点才能解算（当前 " + points.Count.ToString(CultureInfo.InvariantCulture) + " 个）。";
                Log(message);
                MessageBox.Show(this, message + Environment.NewLine + Environment.NewLine
                    + "每行需同时填写：像素X / 像素Y / 机械X / 机械Y；空白行会被自动忽略。",
                    "N 点标定", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            CalibResult result = CalibSolver.Solve(points);
            if (result == null || !result.Success)
            {
                string reason = (result != null && !string.IsNullOrEmpty(result.Message))
                    ? result.Message : "解算失败（未知原因）";
                Log(reason);
                MessageBox.Show(this, reason, "N 点标定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            CalibRecord record = EnsureCurrentRecord();
            if (record == null)
            {
                Log("没有可写入的标定记录：请先选择相机。");
                return;
            }

            // 结果写入记录（畸变 / 旋转中心字段本轮不填，保持原值）
            record.Kind = CalibKind.NPoint;
            record.RmsError = result.RmsError;
            record.MeanOffset = result.MeanOffset;
            record.MaxOffset = result.MaxOffset;
            record.Precision = result.Precision;
            record.PointCount = result.PointCount;
            record.Affine = result.ToAffine6();
            if (record.CreatedAt == DateTime.MinValue) record.CreatedAt = DateTime.Now;

            _pointCache[record.Id] = new List<CalibPoint>(points);

            SaveSettings();
            RefreshRecordList();

            txtResult.Text = result.Describe();

            string skippedText = skipped > 0
                ? "（已忽略 " + skipped.ToString(CultureInfo.InvariantCulture) + " 行未填完整的数据）"
                : "";
            Log("解算完成：RMS = " + result.RmsError.ToString("F4", CultureInfo.InvariantCulture)
                + " mm，最大偏移 = " + result.MaxOffset.ToString("F4", CultureInfo.InvariantCulture)
                + " mm，点数 = " + result.PointCount.ToString(CultureInfo.InvariantCulture) + skippedText);
            UpdateButtons();
        }

        /// <summary>把记录里已保存的结果显示到结果区（切换记录时调用）。</summary>
        private void ShowRecordResult(CalibRecord record)
        {
            if (record == null)
            {
                txtResult.Text = "（未选择标定记录）";
                return;
            }

            if (!record.HasResult)
            {
                txtResult.Text = "「" + (record.Name ?? "") + "」尚未解算。" + Environment.NewLine
                                 + "在左侧点表填写 N 点对应关系后点「计算」。";
                return;
            }

            // 用记录里的 6 个参数反推旋转角 / 尺度，复用同一套报告格式，避免两处格式化不一致
            double a = record.Affine[0];
            double b = record.Affine[2];

            CalibResult report = new CalibResult();
            report.Success = true;
            report.Message = "已保存的结果";
            report.A11 = record.Affine[0];
            report.A12 = record.Affine[1];
            report.A21 = record.Affine[2];
            report.A22 = record.Affine[3];
            report.Tx = record.Affine[4];
            report.Ty = record.Affine[5];
            report.ScaleMmPerPx = Math.Sqrt(a * a + b * b);
            report.RotationDeg = Math.Atan2(b, a) * 180.0 / Math.PI;
            report.RmsError = record.RmsError;
            report.MeanOffset = record.MeanOffset;
            report.MaxOffset = record.MaxOffset;
            report.Precision = record.Precision;
            report.PointCount = record.PointCount;

            txtResult.Text = report.Describe();
        }

        // ==================== 通讯取点 ====================

        /// <summary>
        /// 填充通讯下拉：只读配置里的名称（刻意不碰 <see cref="CommRegistry.Names"/>，
        /// 因为它的首次调用会触发 AutoConnect，打开标定窗不该顺手去连现场设备）。
        /// </summary>
        private void RefreshCommCombo()
        {
            cboComm.Items.Clear();

            CommSettings settings = ConfigStore.Current.Comm;
            if (settings != null && settings.Endpoints != null)
            {
                for (int i = 0; i < settings.Endpoints.Count; i++)
                {
                    CommEndpointConfig endpoint = settings.Endpoints[i];
                    if (endpoint == null || string.IsNullOrEmpty(endpoint.Name)) continue;
                    if (cboComm.Items.Contains(endpoint.Name)) continue;
                    cboComm.Items.Add(endpoint.Name);
                }
            }

            if (cboComm.Items.Count > 0) cboComm.SelectedIndex = 0;
        }

        private void OnReadCoord(object sender, EventArgs e)
        {
            if (_busy) return;

            string name = cboComm.SelectedItem as string;
            if (string.IsNullOrEmpty(name))
            {
                string message = "还没有配置任何通讯。请先去「通讯」页新增一条（TCP / Modbus TCP）。";
                Log(message);
                MessageBox.Show(this, message, "读取机械坐标", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string command = (txtCmd.Text ?? "").Trim();
            if (command.Length == 0)
            {
                command = DefaultCommand;
                txtCmd.Text = command;
            }

            _busy = true;
            try
            {
                OpResult<string> reply = CommQuery.SendAndWaitReply(name, command, CommReplyTimeoutMs);
                if (reply == null || !reply.Success)
                {
                    string reason = reply != null ? reply.Message : "未知错误";
                    Log("读取坐标失败：" + reason);
                    MessageBox.Show(this, reason, "读取机械坐标", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                CalibCoord coord;
                string error;
                if (!CalibCoordReply.TryParse(reply.Value, out coord, out error))
                {
                    Log("解析坐标失败：" + error);
                    MessageBox.Show(this,
                        error + Environment.NewLine + Environment.NewLine
                        + "可在「指令」框里改指令模板，使返回内容是 X,Y 或 X,Y,U 形式的数值。",
                        "读取机械坐标", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // 填进当前行的机械坐标两列（没有当前行就先加一行）
                DataGridViewRow row = gridPoints.CurrentRow;
                if (row == null || row.IsNewRow)
                {
                    int index = gridPoints.Rows.Add();
                    row = gridPoints.Rows[index];
                }

                row.Cells[2].Value = FormatNumber(coord.X);
                row.Cells[3].Value = FormatNumber(coord.Y);

                if (row.Cells[2].OwningColumn != null)
                    gridPoints.CurrentCell = row.Cells[2];

                Log("已读取 " + name + " 的坐标：" + coord.Display + "（像素坐标请手工填写另一侧）");
            }
            catch (Exception ex)
            {
                Log("读取坐标异常：" + ex.Message);
                MessageBox.Show(this, ex.Message, "读取机械坐标", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _busy = false;
            }
        }

        // ==================== 取帧 / 预览 ====================

        private void OnToggleLive(object sender, EventArgs e)
        {
            if (_liveTimer == null) return;

            if (chkLive.Checked)
            {
                if (string.IsNullOrEmpty(_cameraId))
                {
                    Log("请先选择相机再开启实时预览。");
                    chkLive.Checked = false;
                    return;
                }

                _liveTimer.Start();
                GrabOnce(LiveGrabTimeoutMs);
            }
            else
            {
                _liveTimer.Stop();
                Log("实时预览已关闭。");
            }
        }

        private void OnLiveTick(object sender, EventArgs e)
        {
            GrabOnce(LiveGrabTimeoutMs);
        }

        private void OnManualGrab(object sender, EventArgs e)
        {
            GrabOnce(ManualGrabTimeoutMs);
        }

        /// <summary>
        /// 抓一帧显示到右侧。失败只写状态栏 / 日志（实时模式下绝不弹窗，
        /// 否则相机没接会每秒弹一个框）。
        /// </summary>
        private void GrabOnce(int timeoutMs)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                string error;
                ICamera camera = ResolveCamera(out error);
                if (camera == null)
                {
                    Log("取帧失败：" + error);
                    if (chkLive.Checked) chkLive.Checked = false;   // 连不上就别死循环
                    return;
                }

                OpResult<Bitmap> frame = camera.GrabOne(timeoutMs);
                if (frame == null || !frame.Success || frame.Value == null)
                {
                    string reason = (frame != null && !string.IsNullOrEmpty(frame.Message))
                        ? frame.Message : "取帧返回空图像";
                    Log("取帧失败：" + reason);
                    return;
                }

                // ImageViewer.OwnsImage = true：控件接管这张图并在换图时自动 Dispose
                imageViewer.SetImage(frame.Value, true);
                Log("已取帧 " + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                Log("取帧异常：" + ex.Message);
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>
        /// 取当前选中的相机实例。优先复用 <see cref="CameraManager.OpenedCameras"/> 里已打开的实例：
        /// <see cref="CameraManager.Open"/> 每次都会把相机设为 Current 并抛 CurrentChanged，
        /// 主界面会因此重新枚举一次相机列表（≈1.1 s），取帧路径绝不能走那条路。
        /// </summary>
        private ICamera ResolveCamera(out string error)
        {
            error = "";

            string cameraId = SelectedCameraId;
            if (string.IsNullOrEmpty(cameraId))
            {
                error = "请先在上方选择相机";
                return null;
            }

            if (_openedCamera != null && _openedCamera.IsOpen && _openedCamera.Info != null
                && string.Equals(_openedCamera.Info.Id, cameraId, StringComparison.Ordinal))
            {
                return _openedCamera;
            }

            ICamera existing = FindOpenedCamera(cameraId);
            if (existing != null)
            {
                _openedCamera = existing;
                return existing;
            }

            OpResult<ICamera> opened = CameraManager.Instance.Open(cameraId);
            if (opened == null || !opened.Success || opened.Value == null)
            {
                error = (opened != null && !string.IsNullOrEmpty(opened.Message))
                    ? opened.Message : "打开相机失败";
                _openedCamera = null;
                return null;
            }

            _openedCamera = opened.Value;
            return _openedCamera;
        }

        /// <summary>在已打开相机里按 Id 找实例（找不到返回 null，不产生任何副作用）。</summary>
        private static ICamera FindOpenedCamera(string cameraId)
        {
            List<ICamera> opened = CameraManager.Instance.OpenedCameras;
            for (int i = 0; i < opened.Count; i++)
            {
                ICamera camera = opened[i];
                if (camera == null || camera.Info == null) continue;
                if (string.Equals(camera.Info.Id, cameraId, StringComparison.Ordinal)) return camera;
            }
            return null;
        }

        // ==================== 界面状态 ====================

        /// <summary>按「有没有相机 / 有没有选中记录 / 有没有配置通讯」统一刷按钮可用态。</summary>
        private void UpdateButtons()
        {
            bool hasCamera = !string.IsNullOrEmpty(_cameraId);
            CalibRecord record = CurrentRecord;
            bool hasRecord = record != null;
            bool isCurrentRecord = hasRecord && _settings != null
                                   && string.Equals(_settings.CurrentIdFor(_cameraId), record.Id, StringComparison.Ordinal);

            btnRefreshCam.Enabled = true;
            chkLive.Enabled = hasCamera;
            btnGrabFrame.Enabled = hasCamera;

            btnNewRecord.Enabled = hasCamera;
            btnRenameRecord.Enabled = hasRecord;
            btnDeleteRecord.Enabled = hasRecord;
            btnSetCurrent.Enabled = hasRecord && !isCurrentRecord;

            btnAddPoint.Enabled = true;
            btnDelPoint.Enabled = true;
            btnCalc.Enabled = hasCamera;
            btnReadCoord.Enabled = hasCamera && cboComm.Items.Count > 0;
        }

        /// <summary>状态栏 + 日志：所有操作反馈都走这里，绝不静默。</summary>
        private void Log(string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            if (lblStatus != null && !lblStatus.IsDisposed) lblStatus.Text = message;
            Logger.Info("[标定] " + message);
        }

        // ==================== 生命周期 ====================

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            DisposeLiveTimer();
            base.OnFormClosed(e);
        }

        private void OnFormDisposed(object sender, EventArgs e)
        {
            DisposeLiveTimer();
        }

        /// <summary>停掉并释放实时预览定时器（幂等：Close 与 Dispose 两条路径都会走到）。</summary>
        private void DisposeLiveTimer()
        {
            if (_liveTimer == null) return;

            try
            {
                _liveTimer.Stop();
                _liveTimer.Tick -= OnLiveTick;
                _liveTimer.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Warn("释放标定窗体定时器失败：" + ex.Message);
            }
            _liveTimer = null;

            if (chkLive != null && !chkLive.IsDisposed) chkLive.Checked = false;
        }

        // ==================== 小工具 ====================

        /// <summary>
        /// 轻量文本输入对话框（项目里没有 InputBox，且不能为了一个改名去引 Microsoft.VisualBasic）。
        /// 返回 null 表示用户取消；返回空串表示用户确认了空输入（由调用方决定怎么处理）。
        /// </summary>
        private string PromptText(string title, string caption, string initial)
        {
            using (Form dialog = new Form())
            {
                dialog.Text = title ?? "";
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.MinimizeBox = false;
                dialog.MaximizeBox = false;
                dialog.ShowInTaskbar = false;
                dialog.ClientSize = new Size(368, 130);

                Label label = new Label();
                label.Text = caption ?? "";
                label.AutoSize = false;
                label.Location = new Point(12, 12);
                label.Size = new Size(344, 22);
                label.TextAlign = ContentAlignment.MiddleLeft;

                TextBox input = new TextBox();
                input.Text = initial ?? "";
                input.Location = new Point(12, 40);
                input.Size = new Size(344, 25);

                UiButton ok = new UiButton();
                ok.Text = "确定";
                ok.Kind = UiButtonKind.Start;
                ok.Location = new Point(166, 82);
                ok.Size = new Size(96, 30);
                ok.DialogResult = DialogResult.OK;

                UiButton cancel = new UiButton();
                cancel.Text = "取消";
                cancel.Kind = UiButtonKind.Neutral;
                cancel.Location = new Point(270, 82);
                cancel.Size = new Size(86, 30);
                cancel.DialogResult = DialogResult.Cancel;

                dialog.Controls.Add(label);
                dialog.Controls.Add(input);
                dialog.Controls.Add(ok);
                dialog.Controls.Add(cancel);
                dialog.AcceptButton = ok;
                dialog.CancelButton = cancel;

                UiStyle.ApplyForm(dialog);

                input.SelectAll();
                input.Focus();

                DialogResult result = dialog.ShowDialog(this);
                if (result != DialogResult.OK) return null;
                return (input.Text ?? "").Trim();
            }
        }
    }
}
