using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using VisionPlatform.Flow;
using VisionPlatform.UI;

namespace SmokeUi
{
    /// <summary>
    /// 界面主题冒烟：只构造窗体、不 Show，验证两件事 ——
    /// ① 每个窗体都能构造成功（主题递归遍历不会把控件树改崩）；
    /// ② 主题真的生效（不再残留系统浅色控件）。
    /// </summary>
    internal static class Program
    {
        private static int _pass;
        private static int _fail;

        private static void Check(string name, bool ok, string detail = null)
        {
            if (ok) { _pass++; Console.WriteLine("  [PASS] " + name + (detail == null ? "" : "  | " + detail)); }
            else { _fail++; Console.WriteLine("  [FAIL] " + name + (detail == null ? "" : "  | " + detail)); }
        }

        [STAThread]
        private static int Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("=== SmokeUi: 界面主题统一与控件重建 ===");
            Console.WriteLine();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            TestForm("AlgoForm", () => new AlgoForm());
            TestForm("CameraForm", () => new CameraForm());
            TestForm("SettingsForm", () => new SettingsForm());
            TestForm("RecipeForm", () => new RecipeForm());
            TestForm("CommForm", () => new CommForm());
            TestForm("LoginForm", () => new LoginForm());
            TestForm("FlowForm", () => new FlowForm());
            TestForm("MainForm", () => new MainForm());

            TestUiButton();
            TestUiGrid();
            TestToolbarRenderer();
            TestFlowRoiRun();
            TestCaliperSchemaContract();

            Console.WriteLine();
            Console.WriteLine("=== PASS " + _pass + " / FAIL " + _fail + " ===");
            return _fail == 0 ? 0 : 1;
        }

        private static void TestForm(string name, Func<Form> factory)
        {
            Form f = null;
            try
            {
                f = factory();
            }
            catch (Exception ex)
            {
                Check(name + " 构造", false, ex.GetType().Name + ": " + ex.Message);
                if (f != null) f.Dispose();
                return;
            }

            Check(name + " 构造", true);

            // 主题是否套上（MainForm 用 ColorAppBg；其余同色）
            if (f.BackColor != UiTheme.ColorAppBg)
                Check(name + " 窗体底色", false, f.BackColor.ToString());
            else
                Check(name + " 窗体底色", true, f.BackColor.ToString());

            // 递归扫描：不应再出现"系统浅色"的容器 / 输入控件
            List<string> light = new List<string>();
            ScanLight(f, light);
            if (light.Count > 0)
            {
                Check(name + " 无残留浅色控件", false,
                      light.Count + " 个：" + string.Join("; ", light.GetRange(0, Math.Min(4, light.Count))));
            }
            else
            {
                Check(name + " 无残留浅色控件", true);
            }

            f.Dispose();
        }

        private static void ScanLight(Control root, List<string> hits)
        {
            foreach (Control c in root.Controls)
            {
                // 自绘控件不适用（它们自己管配色）
                if (c is UiButton) continue;
                string ns = c.GetType().Namespace;
                if (ns == "VisionSystem.Controls") continue;

                if (c is Panel || c is GroupBox || c is TextBox || c is ComboBox ||
                    c is NumericUpDown || c is ListBox || c is Label)
                {
                    if (c.BackColor.IsKnownColor)
                    {
                        KnownColor k = c.BackColor.ToKnownColor();
                        if (k == KnownColor.Control || k == KnownColor.Window || k == KnownColor.ControlLight)
                            hits.Add(c.GetType().Name + "#" + c.Name);
                    }
                }

                if (c.HasChildren) ScanLight(c, hits);
            }
        }

        private static void TestUiButton()
        {
            Console.WriteLine();
            Console.WriteLine("-- UiButton 语义色 --");

            UiButton b = new UiButton();
            b.Kind = UiButtonKind.Start;
            Check("Start → 绿", b.FillColor == UiTheme.ColorOk);
            b.Kind = UiButtonKind.Pause;
            Check("Pause → 橙", b.FillColor == UiTheme.ColorWarn);
            b.Kind = UiButtonKind.Stop;
            Check("Stop → 红", b.FillColor == UiTheme.ColorNg);
            b.Kind = UiButtonKind.Grab;
            Check("Grab → 蓝", b.FillColor == UiTheme.ColorRunning);
            b.Kind = UiButtonKind.Reset;
            Check("Reset → 中性", b.FillColor == UiTheme.ColorNeutral);
            b.Dispose();
        }

        private static void TestUiGrid()
        {
            Console.WriteLine();
            Console.WriteLine("-- UiGrid 表格 --");

            DataGridView g = new DataGridView();
            UiGrid.Apply(g);

            Check("关闭系统表头配色", !g.EnableHeadersVisualStyles);
            Check("表头高 34", g.ColumnHeadersHeight == 34, g.ColumnHeadersHeight.ToString());
            Check("行高 28", g.RowTemplate.Height == 28, g.RowTemplate.Height.ToString());
            Check("表头底色取自主题", g.ColumnHeadersDefaultCellStyle.BackColor == UiTheme.ColorHeaderBg);
            Check("选中态为强调色", g.DefaultCellStyle.SelectionBackColor == UiTheme.ColorAccent);
            Check("隔行换色", g.AlternatingRowsDefaultCellStyle.BackColor == UiTheme.ColorCardBg);

            g.Dispose();
        }

        private static void TestToolbarRenderer()
        {
            Console.WriteLine();
            Console.WriteLine("-- UiToolStripRenderer 语义胶囊 --");

            using (ToolStrip strip = new ToolStrip())
            {
                ToolStripButton start = new ToolStripButton("启动运行");
                ToolStripButton plain = new ToolStripButton("设置");
                strip.Items.Add(start);
                strip.Items.Add(plain);

                UiToolStripRenderer.SetChipKind(start, UiButtonKind.Start);

                Check("语义色写入 Tag", (start.Tag is UiButtonKind) && (UiButtonKind)start.Tag == UiButtonKind.Start);
                Check("未指定者 Tag 为空", plain.Tag == null);

                UiToolStripRenderer r = new UiToolStripRenderer();
                strip.Renderer = r;
                Check("渲染器可挂载", strip.Renderer is UiToolStripRenderer);
            }
        }

        // =====================================================================
        //  复现「画 ROI 报错」与「单独运行节点」：驱动预览面板 ROI 公开 API +
        //  12 类节点的 ROI 参数组扫描 + FlowRunner 单节点执行
        // =====================================================================
        private static void TestFlowRoiRun()
        {
            Console.WriteLine();
            Console.WriteLine("-- FlowPreviewPanel ROI 与 FlowRunner 单节点 --");

            string dir = Path.Combine(Path.GetTempPath(), "VisionSystem.SmokeUi");
            Directory.CreateDirectory(dir);
            string png = Path.Combine(dir, "roi_probe.png");

            // 合成 320x240 测试图
            using (Bitmap bmp = new Bitmap(320, 240, PixelFormat.Format24bppRgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Black);
                    using (SolidBrush w = new SolidBrush(Color.White))
                        g.FillRectangle(w, 80, 60, 160, 120);
                }
                bmp.Save(png, ImageFormat.Png);
            }

            // ① 预览面板：载图 → 进 ROI 拖框 → 取消，全程不抛异常
            try
            {
                using (FlowPreviewPanel panel = new FlowPreviewPanel())
                {
                    string err;
                    Bitmap loaded = FlowPreviewPanel.LoadImageFromFile(png, out err);
                    Check("读图成功", loaded != null, err);
                    if (loaded != null)
                    {
                        panel.ShowStaticImage(loaded, "探针");
                        Check("预览 HasImage", panel.HasImage);
                        panel.BeginRoiPick(new RectangleF(), "画 ROI：探针");
                        Check("进入 ROI 拖框", panel.IsRoiPicking);
                        panel.CancelRoiPick();
                        Check("取消 ROI 拖框", !panel.IsRoiPicking);
                    }
                }
            }
            catch (Exception ex)
            {
                Check("预览面板 ROI 流程", false, ex.GetType().Name + ": " + ex.Message);
            }

            // ② 12 类节点逐一扫描 ROI 参数组（复刻 FindRoiGroups 的核心逻辑，验证不抛异常）
            var types = (FlowNodeType[])Enum.GetValues(typeof(FlowNodeType));
            int roiCapable = 0;
            try
            {
                foreach (FlowNodeType t in types)
                {
                    NodeParamTable table = NodeParamSchema.For(t);
                    if (table == null || table.Fields == null) continue;

                    foreach (NodeParamField f in table.Fields)
                    {
                        if (f == null || string.IsNullOrEmpty(f.Key)) continue;
                        if (f.Key == "TemplateX" || f.Key == "RoiX" || f.Key == "SearchX")
                            roiCapable++;
                    }
                }
                Check("12 类节点 ROI 参数组扫描", true, roiCapable + " 个 ROI 参数入口");
            }
            catch (Exception ex)
            {
                Check("12 类节点 ROI 参数组扫描", false, ex.GetType().Name + ": " + ex.Message);
            }

            // ③ FlowRunner：单节点链（图像源 + 圆卡尺）整链与单步各跑一次
            try
            {
                using (FlowRunner runner = new FlowRunner())
                {
                    FlowNode src = FlowModule.CreateNode(FlowNodeType.ImageSource, 0f, 0f);
                    src.Set("Source", "File");
                    src.Set("FilePath", png);
                    FlowNode cal = FlowModule.CreateNode(FlowNodeType.CircleCaliper, 200f, 0f);

                    var f = FlowDefinition.CreateEmpty("roi-run");
                    f.Nodes.Add(src);
                    f.Nodes.Add(cal);
                    f.Links.Add(new FlowLink(src.Id, cal.Id));

                    FlowRunSession s = runner.Begin(f, null, true);
                    Check("单节点链 Begin", !s.Aborted, s.ErrorMessage);
                    if (!s.Aborted)
                    {
                        FlowNodeProgress p = runner.StepNext(s);   // 第 1 个节点：图像源
                        Check("单步推进节点 1", p != null && p.Status == FlowNodeStatus.Ok,
                              p != null ? p.Summary : "null");
                        s.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Check("FlowRunner 单节点运行", false, ex.GetType().Name + ": " + ex.Message);
            }

            // ④ FlowRunController.RunTo：跑到中间节点即停，不跑下游
            try
            {
                using (FlowRunner runner = new FlowRunner())
                using (FlowRunController ctl = new FlowRunController(runner))
                {
                    FlowNode src = FlowModule.CreateNode(FlowNodeType.ImageSource, 0f, 0f);
                    src.Set("Source", "File");
                    src.Set("FilePath", png);
                    FlowNode pre = FlowModule.CreateNode(FlowNodeType.Preprocess, 200f, 0f);
                    FlowNode blob = FlowModule.CreateNode(FlowNodeType.Blob, 400f, 0f);
                    var f = FlowDefinition.CreateEmpty("run-to");
                    f.Nodes.Add(src); f.Nodes.Add(pre); f.Nodes.Add(blob);
                    f.Links.Add(new FlowLink(src.Id, pre.Id));
                    f.Links.Add(new FlowLink(pre.Id, blob.Id));

                    int maxIndex = 0;
                    using (var done = new ManualResetEvent(false))
                    {
                        ctl.ProgressReady += (s, e) =>
                        {
                            if (e != null && e.Snapshot != null && !e.Snapshot.IsChainEnd)
                                maxIndex = Math.Max(maxIndex, e.Snapshot.Index);
                        };
                        ctl.Completed += (s, e) => done.Set();

                        ctl.RunTo(f, null, pre.Id);   // 只跑到第 2 个节点 Preprocess
                        done.WaitOne(5000);
                    }
                    Check("RunTo 停在目标节点（≤2，不跑下游）", maxIndex > 0 && maxIndex <= 2,
                          "maxIndex=" + maxIndex);
                }
            }
            catch (Exception ex)
            {
                    Check("FlowRunController.RunTo", false, ex.GetType().Name + ": " + ex.Message);
            }
        }

        // =====================================================================
        //  卡尺参数表契约：直线卡尺必须有 X1/Y1/X2/Y2，圆卡尺必须有
        //  Cx/Cy/InnerRadius/OuterRadius（预览区画几何按这些键回写）。
        // =====================================================================
        private static void TestCaliperSchemaContract()
        {
            Console.WriteLine();
            Console.WriteLine("-- NodeParamSchema 卡尺字段契约 --");

            NodeParamTable line = NodeParamSchema.For(FlowNodeType.LineCaliper);
            NodeParamTable circle = NodeParamSchema.For(FlowNodeType.CircleCaliper);

            bool ok = HasKeys(line, "X1", "Y1", "X2", "Y2")
                   && HasKeys(circle, "Cx", "Cy", "InnerRadius", "OuterRadius");

            string detail = string.Format("LineCaliper={0} CircleCaliper={1}",
                line != null ? "ok" : "null", circle != null ? "ok" : "null");

            Check("卡尺字段契约（LineCaliper:X1/Y1/X2/Y2；CircleCaliper:Cx/Cy/InnerRadius/OuterRadius）", ok, detail);
        }

        private static bool HasKeys(NodeParamTable table, params string[] keys)
        {
            if (table == null || table.Fields == null) return false;
            foreach (string k in keys)
                if (!table.Fields.Exists(x => x != null && string.Equals(x.Key, k, StringComparison.Ordinal)))
                    return false;
            return true;
        }
    }
}
