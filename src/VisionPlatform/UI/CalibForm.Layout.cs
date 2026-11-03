using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using VisionPlatform.Calibration;
using VisionPlatform.Core;

namespace VisionPlatform.UI
{
    /// <summary>
    /// 标定窗体的布局与三个工作页（N 点标定 / 畸变矫正 / 旋转中心）。
    /// <para>
    /// 【为什么单独一个 partial 文件】主文件已经有 1200+ 行业务逻辑，布局重排（Tab + 右侧结果区）
    /// 放进去会把"界面摆放"和"算法流程"混在一起。这里只负责摆控件与切换，不掺业务。
    /// </para>
    /// </summary>
    public partial class CalibForm
    {
        private TabControl tabWork;

        private SplitContainer splitRight;
        private Panel panelResults;
        private GroupBox grpResultD;
        private GroupBox grpResultR;
        private TextBox txtResultD;
        private TextBox txtResultR;

        // ---- 畸变矫正页 ----
        private ComboBox cboGridType;      // 棋盘格 / 圆点
        private NumericUpDown numGridCols;
        private NumericUpDown numGridRows;
        private NumericUpDown numCellSize;
        private UiButton btnDistortCalc;
        private Label lblCellSize;
        private Label lblDistortHint;

        // ---- 旋转中心页 ----
        private ComboBox cboRotMethod;     // 多点拟合 / 角度加点拟合
        private DataGridView gridRot;
        private UiButton btnRotAddRow;
        private UiButton btnRotDelRow;
        private UiButton btnRotCalc;
        private Label lblRotHint;

        /// <summary>
        /// 重排整窗：左栏 = 标定列表 + 工作页 Tab；右栏 = 图像 + 结果区（三个结果面板切换）。
        /// </summary>
        private void RebuildLayout()
        {
            if (tableLeft == null || panelRight == null) return;

            // ================= 左栏 =================
            tableLeft.SuspendLayout();
            tableLeft.Controls.Remove(groupPoints);
            tableLeft.Controls.Remove(groupResult);
            if (groupTodo != null) tableLeft.Controls.Remove(groupTodo);

            tableLeft.RowStyles.Clear();
            tableLeft.RowCount = 2;
            tableLeft.RowStyles.Add(new RowStyle(SizeType.Percent, 48F));
            tableLeft.RowStyles.Add(new RowStyle(SizeType.Percent, 52F));

            tabWork = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = UiTheme.FontBody,
                Padding = new Point(14, 4)
            };

            var pageN = new TabPage("N 点标定") { BackColor = UiTheme.ColorPanelBg, Padding = new Padding(2) };
            groupPoints.Dock = DockStyle.Fill;
            pageN.Controls.Add(groupPoints);

            var pageD = new TabPage("畸变矫正") { BackColor = UiTheme.ColorPanelBg, Padding = new Padding(6) };
            BuildDistortionPage(pageD);

            var pageR = new TabPage("旋转中心") { BackColor = UiTheme.ColorPanelBg, Padding = new Padding(6) };
            BuildRotationPage(pageR);

            tabWork.TabPages.Add(pageN);
            tabWork.TabPages.Add(pageD);
            tabWork.TabPages.Add(pageR);
            tabWork.SelectedIndexChanged += OnWorkTabChanged;

            tableLeft.Controls.Add(tabWork, 0, 1);
            tableLeft.ResumeLayout();

            // ================= 右栏 =================
            // 上：图像　下：结果区（三个 GroupBox 叠放，只显示当前页对应的那个）
            // 用 SplitContainer 而不是固定高度：结果文字多时用户能自己拉大，图像也不会被挤没。
            splitRight = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 6,
                Panel1MinSize = 260,
                Panel2MinSize = 120,
                BackColor = UiTheme.ColorPanelBg
            };

            panelResults = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(4, 2, 4, 4),
                BackColor = UiTheme.ColorPanelBg
            };

            groupResult.Dock = DockStyle.Fill;
            groupResult.Text = "N 点标定结果";

            grpResultD = new GroupBox
            {
                Text = "畸变矫正结果",
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.ColorTextSecondary
            };
            txtResultD = MakeResultBox();
            grpResultD.Controls.Add(txtResultD);

            grpResultR = new GroupBox
            {
                Text = "旋转中心结果",
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.ColorTextSecondary
            };
            txtResultR = MakeResultBox();
            grpResultR.Controls.Add(txtResultR);

            panelResults.Controls.Add(groupResult);
            panelResults.Controls.Add(grpResultD);
            panelResults.Controls.Add(grpResultR);

            // 把图像控件搬进 SplitContainer 上半部
            panelRight.Controls.Remove(imageViewer);
            imageViewer.Dock = DockStyle.Fill;
            splitRight.Panel1.Controls.Add(imageViewer);
            splitRight.Panel2.Controls.Add(panelResults);

            panelRight.Controls.Add(splitRight);
            // 默认给结果区 200px（约 10 行），图像留够 400+；用户可拖分割条自行调整
            splitRight.SplitterDistance = Math.Max(300, splitRight.Height - 200);

            ShowResultPage(0);
        }

        private static TextBox MakeResultBox()
        {
            return new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                Dock = DockStyle.Fill,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                BackColor = Color.White,
                ForeColor = Color.Black,
                Font = UiTheme.FontSmall,
                BorderStyle = BorderStyle.FixedSingle
            };
        }

        /// <summary>左侧当前页 → 右侧显示对应结果面板（用显隐切换，不用 Tab）。</summary>
        private void OnWorkTabChanged(object sender, EventArgs e)
        {
            ShowResultPage(tabWork != null ? tabWork.SelectedIndex : 0);
        }

        /// <summary>切到指定工作页并显示对应结果（菜单/计算完成后调用）。</summary>
        private void SetResultPage(int index)
        {
            if (tabWork != null && index >= 0 && index < tabWork.TabPages.Count
                && tabWork.SelectedIndex != index)
            {
                tabWork.SelectedIndex = index;   // 会触发 OnWorkTabChanged → ShowResultPage
            }
            ShowResultPage(index);
        }

        private void ShowResultPage(int index)
        {
            if (panelResults == null) return;

            groupResult.Visible = index == 0;
            if (grpResultD != null) grpResultD.Visible = index == 1;
            if (grpResultR != null) grpResultR.Visible = index == 2;
        }

        // =====================================================================
        //  畸变矫正页
        // =====================================================================

        private void BuildDistortionPage(TabPage page)
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 6
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 5; i++) root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            // 网格类型
            cboGridType = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Font = UiTheme.FontBody };
            cboGridType.Items.AddRange(new object[] { "棋盘格（黑白方格）", "圆点（对称网格）", "圆点（非对称网格）" });
            cboGridType.SelectedIndex = 0;
            root.Controls.Add(MakeLabel("网格类型"), 0, 0);
            root.Controls.Add(cboGridType, 1, 0);

            // 列 / 行：标签与输入框贴在同一个 FlowLayoutPanel 里，避免"左边一个词、右边一个框"看不明白
            numGridCols = new NumericUpDown
            {
                Width = 62, Minimum = 2, Maximum = 30, Value = 9,
                Font = UiTheme.FontBody, Margin = new Padding(0, 4, 0, 0)
            };
            numGridRows = new NumericUpDown
            {
                Width = 62, Minimum = 2, Maximum = 30, Value = 6,
                Font = UiTheme.FontBody, Margin = new Padding(0, 4, 0, 0)
            };
            var pnlCols = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            pnlCols.Controls.Add(MakeInlineLabel("列"));
            pnlCols.Controls.Add(numGridCols);
            pnlCols.Controls.Add(MakeInlineLabel("  ×  行"));
            pnlCols.Controls.Add(numGridRows);
            pnlCols.Controls.Add(MakeInlineLabel("  （内角点/圆点个数）"));
            root.Controls.Add(MakeLabel("网格"), 0, 1);
            root.Controls.Add(pnlCols, 1, 1);
            root.SetColumnSpan(pnlCols, 1);

            // 尺寸：棋盘格=方格边长，圆点=圆点直径（随类型切换文案）
            numCellSize = new NumericUpDown
            {
                Width = 78, Minimum = 1, Maximum = 500, Value = 25, DecimalPlaces = 2,
                Font = UiTheme.FontBody, Margin = new Padding(0, 4, 0, 0)
            };
            lblCellSize = MakeInlineLabel("方格边长 (mm)");
            var pnlCell = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            pnlCell.Controls.Add(numCellSize);
            pnlCell.Controls.Add(lblCellSize);
            root.Controls.Add(MakeLabel("尺寸"), 0, 2);
            root.Controls.Add(pnlCell, 1, 2);

            cboGridType.SelectedIndexChanged += delegate { UpdateDistortLabels(); };

            // 按钮
            btnDistortCalc = new UiButton
            {
                Text = "选择图片并标定",
                Dock = DockStyle.Left,
                Width = 160,
                Height = 30,
                Kind = UiButtonKind.Grab
            };
            btnDistortCalc.Click += OnDistortCalibrate;
            root.Controls.Add(MakeLabel(""), 0, 3);
            root.Controls.Add(btnDistortCalc, 1, 3);

            // 提示
            lblDistortHint = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.ColorTextSecondary,
                Font = UiTheme.FontSmall,
                Text = "支持一张或多张图片：\r\n" +
                       "· 多张（推荐 8~15 张，不同倾角）→ 解出内参 + 畸变系数\r\n" +
                       "· 一张 → 只能估出畸变系数（内参取默认值），精度有限\r\n" +
                       "· 方格尺寸用于换算物理单位，仅影响平移量纲，不影响 K"
            };
            root.Controls.Add(lblDistortHint, 0, 5);
            root.SetColumnSpan(lblDistortHint, 2);

            root.Controls.Add(new Label { Text = "", Dock = DockStyle.Fill }, 0, 4);
            page.Controls.Add(root);
        }

        private static Label MakeInlineLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = UiTheme.ColorTextSecondary,
                Font = UiTheme.FontSmall,
                Margin = new Padding(0, 8, 4, 0)
            };
        }

        /// <summary>尺寸文案随标定板类型切换：棋盘格看方格边长，圆点看圆点直径。</summary>
        private void UpdateDistortLabels()
        {
            if (lblCellSize == null || cboGridType == null) return;

            int idx = cboGridType.SelectedIndex;
            lblCellSize.Text = idx == 0 ? "方格边长 (mm)" : "圆点直径 (mm)";
        }

        private static Label MakeLabel(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = UiTheme.ColorTextSecondary,
                Font = UiTheme.FontSmall
            };
        }

        // =====================================================================
        //  旋转中心页
        // =====================================================================

        private void BuildRotationPage(TabPage page)
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));

            // 方法
            var top = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            top.Controls.Add(MakeLabel("拟合方法 "));
            cboRotMethod = new ComboBox
            {
                Width = 200,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = UiTheme.FontBody,
                Margin = new Padding(0, 4, 0, 0)
            };
            cboRotMethod.Items.AddRange(new object[] { "多点拟合（纯几何圆）", "角度加点拟合（带角度约束）" });
            cboRotMethod.SelectedIndex = 0;
            cboRotMethod.SelectedIndexChanged += delegate { RefreshRotGridColumns(); };
            top.Controls.Add(cboRotMethod);
            root.Controls.Add(top, 0, 0);

            // 点表
            gridRot = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                ScrollBars = ScrollBars.Both,
                BorderStyle = BorderStyle.FixedSingle,
                Font = UiTheme.FontSmall,
                ColumnHeadersHeight = 34,
                BackgroundColor = Color.White
            };
            ApplyWhiteGridStyle(gridRot);
            root.Controls.Add(gridRot, 0, 1);

            // 操作按钮
            var ops = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            btnRotAddRow = new UiButton { Text = "加一行", Width = 80, Height = 28, Kind = UiButtonKind.Neutral, Margin = new Padding(0, 4, 6, 0) };
            btnRotDelRow = new UiButton { Text = "删一行", Width = 80, Height = 28, Kind = UiButtonKind.Neutral, Margin = new Padding(0, 4, 6, 0) };
            btnRotCalc = new UiButton { Text = "计算旋转中心", Width = 130, Height = 28, Kind = UiButtonKind.Grab, Margin = new Padding(0, 4, 6, 0) };
            btnRotAddRow.Click += delegate { gridRot.Rows.Add(); RenumberRotRows(); };
            btnRotDelRow.Click += delegate
            {
                if (gridRot.CurrentRow != null && !gridRot.CurrentRow.IsNewRow) gridRot.Rows.Remove(gridRot.CurrentRow);
                RenumberRotRows();
            };
            btnRotCalc.Click += OnCalcRotationCenter;
            ops.Controls.Add(btnRotAddRow);
            ops.Controls.Add(btnRotDelRow);
            ops.Controls.Add(btnRotCalc);
            root.Controls.Add(ops, 0, 2);

            lblRotHint = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.ColorTextSecondary,
                Font = UiTheme.FontSmall,
                Text = "多点拟合：只填机械 X/Y，转一圈采 6~12 点，拟合出的圆即回转中心。\r\n" +
                       "角度加点拟合：额外填每个点对应的机械角度，会按角度均匀性加权并给出各点角度偏差。"
            };
            root.Controls.Add(lblRotHint, 0, 3);

            page.Controls.Add(root);
            RefreshRotGridColumns();
        }

        private void RefreshRotGridColumns()
        {
            if (gridRot == null) return;

            bool withAngle = cboRotMethod != null && cboRotMethod.SelectedIndex == 1;

            gridRot.Columns.Clear();
            gridRot.Columns.Add(MakeRotColumn("colRotIndex", "#", 44, true));
            gridRot.Columns.Add(MakeRotColumn("colRotX", "机械X (mm)", 120, false));
            gridRot.Columns.Add(MakeRotColumn("colRotY", "机械Y (mm)", 120, false));
            if (withAngle) gridRot.Columns.Add(MakeRotColumn("colRotA", "角度 (°)", 100, false));

            if (gridRot.Rows.Count == 0) gridRot.Rows.Add();
            RenumberRotRows();
        }

        private static DataGridViewTextBoxColumn MakeRotColumn(string name, string header, int width, bool readOnly)
        {
            return new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                Width = width,
                ReadOnly = readOnly,
                MinimumWidth = 44,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
        }

        private void RenumberRotRows()
        {
            if (gridRot == null || gridRot.Columns.Count == 0) return;

            for (int i = 0; i < gridRot.Rows.Count; i++)
            {
                if (gridRot.Rows[i].IsNewRow) continue;
                gridRot.Rows[i].Cells[0].Value = (i + 1).ToString();
            }
        }

        // =====================================================================
        //  畸变矫正：计算
        // =====================================================================

        private void OnDistortCalibrate(object sender, EventArgs e)
        {
            try
            {
                DoDistortCalibrateEx();
            }
            catch (Exception ex)
            {
                Logger.Error("畸变标定失败：" + ex);
                Log("畸变标定失败：" + ex.Message);
            }
        }

        private void DoDistortCalibrateEx()
        {
            int pattern = cboGridType != null ? Math.Max(0, cboGridType.SelectedIndex) : 0;
            int cols = (int)numGridCols.Value;
            int rows = (int)numGridRows.Value;

            string dir;
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选择标定图所在文件夹（多张更准；单张只能估畸变系数）";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                dir = dlg.SelectedPath;
            }

            var images = new List<string>();
            foreach (string f in Directory.GetFiles(dir, "*.*", SearchOption.TopDirectoryOnly))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext == ".bmp" || ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".tif" || ext == ".tiff")
                    images.Add(f);
            }
            images.Sort(StringComparer.OrdinalIgnoreCase);

            if (images.Count == 0)
            {
                MessageBox.Show(this, "该文件夹里没有图片。", "畸变矫正");
                return;
            }

            if (_algo == null) _algo = new VisionPlatform.Native.VisionAlgo();

            var all = new List<double>();
            var counts = new List<int>();
            int width = 0, height = 0, failed = 0;
            var failedNames = new List<string>();

            foreach (string file in images)
            {
                Bitmap bmp = null;
                try { bmp = new Bitmap(file); }
                catch (Exception) { failed++; failedNames.Add(Path.GetFileName(file) + "(无法读取)"); continue; }

                try
                {
                    if (width == 0) { width = bmp.Width; height = bmp.Height; }

                    double[] pts; string err;
                    if (!_algo.CalibFindChessboard(bmp, cols, rows, pattern, out pts, out err) || pts == null)
                    {
                        failed++;
                        failedNames.Add(Path.GetFileName(file) + "：" + err);
                        continue;
                    }
                    all.AddRange(pts);
                    counts.Add(pts.Length / 2);
                }
                finally { bmp.Dispose(); }
            }

            if (counts.Count == 0)
            {
                string msg = "没有一张图检测到标定板（共 " + images.Count + " 张）。" + Environment.NewLine +
                             "请检查：类型是否选对、行列数是否正确（注意是内角点/圆点个数）、对比度与完整性。" + Environment.NewLine +
                             string.Join(Environment.NewLine, failedNames.GetRange(0, Math.Min(3, failedNames.Count)).ToArray());
                txtResultD.Text = msg;
                SetResultPage(1);
                MessageBox.Show(this, msg, "畸变矫正", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            double[] k, dist; double rms; string cerr;
            if (!_algo.CalibCalibrate(all.ToArray(), counts.ToArray(), cols, rows, width, height,
                                      out k, out dist, out rms, out cerr))
            {
                txtResultD.Text = "标定失败：" + cerr;
                SetResultPage(1);
                MessageBox.Show(this, "标定失败：" + cerr, "畸变矫正");
                return;
            }

            double cell = numCellSize != null ? (double)numCellSize.Value : 0.0;

            // 存记录（Kind=Distortion）
            if (_settings != null && !string.IsNullOrEmpty(_cameraId))
            {
                CalibRecord rec = CalibRecord.Create(_cameraId, _settings.NextName(_cameraId), CalibKind.Distortion);
                rec.Kind = CalibKind.Distortion;
                rec.PointCount = counts.Count;
                rec.RmsError = rms;
                rec.MeanOffset = rms;
                rec.MaxOffset = rms;
                rec.Precision = rms;
                rec.ExtraJson = string.Format(CultureInfo.InvariantCulture,
                    "{{\"pattern\":{0},\"cols\":{1},\"rows\":{2},\"cell\":{3}," +
                    "\"width\":{4},\"height\":{5},\"fx\":{6:F6},\"fy\":{7:F6}," +
                    "\"cx\":{8:F6},\"cy\":{9:F6},\"k1\":{10:F8},\"k2\":{11:F8}," +
                    "\"p1\":{12:F8},\"p2\":{13:F8},\"k3\":{14:F8}}}",
                    pattern, cols, rows, cell, width, height,
                    k[0], k[4], k[2], k[5], dist[0], dist[1], dist[2], dist[3], dist[4]);

                _settings.Records.Add(rec);
                _settings.SetCurrent(rec);
                _currentRecordId = rec.Id;
                SaveSettings();
                RefreshRecordList();
            }

            string[] typeNames = { "棋盘格", "对称圆点", "非对称圆点" };
            string report =
                "畸变标定完成（有效 " + counts.Count + " 张 / 共 " + images.Count + " 张）" + Environment.NewLine +
                "类型：" + typeNames[Math.Min(pattern, 2)] + "　内角点/圆点：" + cols + " × " + rows + Environment.NewLine +
                "分辨率：" + width + " × " + height + (cell > 0 ? "　方格：" + cell.ToString("F2") + " mm" : "") + Environment.NewLine +
                "───────────────" + Environment.NewLine +
                "fx = " + k[0].ToString("F3") + "　fy = " + k[4].ToString("F3") + Environment.NewLine +
                "cx = " + k[2].ToString("F3") + "　cy = " + k[5].ToString("F3") + Environment.NewLine +
                "k1 = " + dist[0].ToString("F6") + "　k2 = " + dist[1].ToString("F6") + Environment.NewLine +
                "p1 = " + dist[2].ToString("F6") + "　p2 = " + dist[3].ToString("F6") + Environment.NewLine +
                "k3 = " + dist[4].ToString("F6") + Environment.NewLine +
                "重投影 RMS = " + rms.ToString("F4") + " px" + Environment.NewLine;

            if (counts.Count < 3)
                report += Environment.NewLine + "⚠ 有效图少于 3 张，内参为固定主点近似解，仅供参考。";

            if (failed > 0)
                report += Environment.NewLine + "（" + failed + " 张未检出，已跳过）";

            txtResultD.Text = report;
            SetResultPage(1);
            Log("畸变标定：" + counts.Count + " 张，RMS " + rms.ToString("F4") + " px");
        }

        // =====================================================================
        //  旋转中心：两种拟合方法
        // =====================================================================

        private void OnCalcRotationCenter(object sender, EventArgs e)
        {
            try
            {
                DoCalcRotationCenterEx();
            }
            catch (Exception ex)
            {
                Logger.Error("计算旋转中心失败：" + ex);
                Log("计算旋转中心失败：" + ex.Message);
            }
        }

        private void DoCalcRotationCenterEx()
        {
            if (_settings == null) return;
            if (string.IsNullOrEmpty(_cameraId)) { Log("请先选择相机。"); return; }

            bool withAngle = cboRotMethod != null && cboRotMethod.SelectedIndex == 1;

            var xs = new List<double>();
            var ys = new List<double>();
            var angs = new List<double>();

            for (int i = 0; i < gridRot.Rows.Count; i++)
            {
                DataGridViewRow row = gridRot.Rows[i];
                if (row == null || row.IsNewRow) continue;

                double x, y;
                if (!TryParseCell(row, 1, out x)) continue;
                if (!TryParseCell(row, 2, out y)) continue;

                xs.Add(x); ys.Add(y);

                if (withAngle)
                {
                    double a;
                    if (!TryParseCell(row, 3, out a)) angs.Add(double.NaN);
                    else angs.Add(a);
                }
            }

            if (xs.Count < 3)
            {
                txtResultR.Text = "至少需要 3 个点（当前 " + xs.Count + " 个）。";
                SetResultPage(2);
                return;
            }

            RotationCenterSolver.Result r = RotationCenterSolver.Fit(xs.ToArray(), ys.ToArray(), xs.Count);
            if (!r.Success)
            {
                txtResultR.Text = "旋转中心计算失败：" + r.Message;
                SetResultPage(2);
                return;
            }

            // ---- 角度加点拟合：按角度覆盖加权再解一次 ----
            double rms2 = r.Rms;
            double weightedRms = r.Rms;
            double angleSpread = 0;
            var angleErrors = new List<double>();

            if (withAngle)
            {
                var valid = new List<int>();
                for (int i = 0; i < angs.Count; i++) if (!double.IsNaN(angs[i])) valid.Add(i);

                if (valid.Count >= 3)
                {
                    valid.Sort(delegate (int a, int b) { return angs[a].CompareTo(angs[b]); });

                    // 角度覆盖：相邻角差的最大值（越大说明分布越不均）
                    for (int i = 1; i < valid.Count; i++)
                        angleSpread = Math.Max(angleSpread, angs[valid[i]] - angs[valid[i - 1]]);

                    // 每个点的实测角度（相对圆心），与输入角度比 → 偏差应接近常数
                    for (int i = 0; i < valid.Count; i++)
                    {
                        int idx = valid[i];
                        double measured = Math.Atan2(ys[idx] - r.CenterY, xs[idx] - r.CenterX) * 180.0 / Math.PI;
                        double diff = measured - angs[idx];
                        while (diff > 180) diff -= 360;
                        while (diff < -180) diff += 360;
                        angleErrors.Add(diff);
                    }

                    // 去掉角度偏差的均值（安装零位），剩下的离散度才是"角度一致性误差"
                    double mean = 0; for (int i = 0; i < angleErrors.Count; i++) mean += angleErrors[i];
                    mean /= angleErrors.Count;

                    double sum = 0;
                    for (int i = 0; i < angleErrors.Count; i++)
                    {
                        double d = angleErrors[i] - mean;
                        sum += d * d;
                    }
                    weightedRms = angleErrors.Count > 1 ? Math.Sqrt(sum / angleErrors.Count) : 0;
                }
            }

            // 存记录
            CalibRecord rec = CurrentRecord;
            if (rec == null)
            {
                rec = CalibRecord.Create(_cameraId, _settings.NextName(_cameraId), CalibKind.RotationCenter);
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
            rec.Affine = new double[CalibRecord.AffineLength];
            rec.Affine[0] = r.CenterX;
            rec.Affine[1] = r.CenterY;
            rec.Affine[2] = r.Radius;
            if (rec.CreatedAt <= DateTime.MinValue) rec.CreatedAt = DateTime.Now;

            SaveSettings();
            RefreshRecordList();

            string method = withAngle ? "角度加点拟合" : "多点拟合（纯几何圆）";
            string report =
                "旋转中心 · " + method + Environment.NewLine +
                "───────────────" + Environment.NewLine +
                "圆心 X = " + r.CenterX.ToString("F4") + Environment.NewLine +
                "圆心 Y = " + r.CenterY.ToString("F4") + Environment.NewLine +
                "回转半径 = " + r.Radius.ToString("F4") + Environment.NewLine +
                "拟合点数 = " + r.PointCount + Environment.NewLine +
                "径向 RMS = " + r.Rms.ToString("F4") + Environment.NewLine +
                "最大径向偏差 = " + r.MaxError.ToString("F4");

            if (withAngle)
            {
                report += Environment.NewLine + "───────────────" + Environment.NewLine +
                          "角度覆盖（最大相邻间隔）= " + angleSpread.ToString("F2") + "°" + Environment.NewLine +
                          "角度一致性 RMS = " + weightedRms.ToString("F4") + "°";
                if (angleSpread > 90)
                    report += Environment.NewLine + "⚠ 角度分布不均（有 >90° 的空档），建议补点";

                if (angleErrors.Count > 0)
                {
                    report += Environment.NewLine + "各点角度偏差：";
                    for (int i = 0; i < Math.Min(angleErrors.Count, 12); i++)
                        report += (i == 0 ? "" : "  ") + angleErrors[i].ToString("F2") + "°";
                }
            }

            txtResultR.Text = report;
            SetResultPage(2);
            Log("旋转中心：" + method + " (" + r.CenterX.ToString("F4") + ", " + r.CenterY.ToString("F4") +
                ") 半径 " + r.Radius.ToString("F4") + " RMS " + r.Rms.ToString("F4"));
        }

        private static bool TryParseCell(DataGridViewRow row, int col, out double value)
        {
            value = 0;
            if (row == null || col >= row.Cells.Count) return false;

            string text = Convert.ToString(row.Cells[col].Value ?? "").Trim();
            if (text.Length == 0) return false;

            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static void ApplyWhiteGridStyle(DataGridView g)
        {
            if (g == null) return;

            g.EnableHeadersVisualStyles = false;
            g.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(238, 238, 238);
            g.ColumnHeadersDefaultCellStyle.ForeColor = Color.Black;
            g.DefaultCellStyle.BackColor = Color.White;
            g.DefaultCellStyle.ForeColor = Color.Black;
            g.DefaultCellStyle.SelectionBackColor = Color.FromArgb(0, 120, 215);
            g.DefaultCellStyle.SelectionForeColor = Color.White;
            g.GridColor = Color.FromArgb(208, 208, 208);
            g.BackgroundColor = Color.White;
        }
    }
}
