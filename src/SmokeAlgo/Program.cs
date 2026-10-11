using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using VisionPlatform.Camera;
using VisionPlatform.Core;
using VisionPlatform.Flow;
using VisionPlatform.Native;

namespace SmokeAlgo
{
    internal static class Program
    {
        // =====================================================================
        //  几何真值表：合成图的每一笔都由这里的数决定，量测精度据此判定
        // =====================================================================
        private const int ImageWidth = 640;
        private const int ImageHeight = 480;

        // 圆：圆心 (320,240)，半径 100 → 直径真值 200
        private const int TruthCircleCx = ImageWidth / 2;      // 320
        private const int TruthCircleCy = ImageHeight / 2;     // 240
        private const int TruthCircleR = 100;
        private const double TruthCircleDiameter = TruthCircleR * 2.0;   // 200.0

        // 水平白色直线（横穿整幅，占 12 行）：上边缘 y=94，下边缘 y=106，总长 640
        private const double TruthLineUpEdge = 94.0;
        private const double TruthLineBottomEdge = 106.0;
        private const double TruthLineLengthPx = ImageWidth;             // 640

        // 5 个 80x80 白色方块 → 个数 5，单块面积 6400
        private const double TruthBlockAreaPx = 6400.0;
        private const int TruthBlockCount = 5;

        // 合成图的已知位移（drift）：整幅图相对原图平移 (+40, +30)
        private const double TruthDriftDxPx = 40.0;
        private const double TruthDriftDyPx = 30.0;
        private const double TruthDraggedCircleCxPx = TruthCircleCx + TruthDriftDxPx;  // 360
        private const double TruthDraggedCircleCyPx = TruthCircleCy + TruthDriftDyPx;  // 270

        // =====================================================================
        //  断言容差（全部写进报告）
        // =====================================================================
        private const double TolerancePositionPx = 2.0;   // 位置 / 直径 / 半径
        private const double ToleranceLengthPx = 3.0;     // 长度
        private const double ToleranceAngleDeg = 0.5;     // 角度（°）
        private const double ToleranceAreaPercent = 0.05; // 面积相对误差上限 5%

        private static int _pass;
        private static int _fail;
        private static string _dir;

        private static int Main()
        {
            try { Console.OutputEncoding = new UTF8Encoding(false); }
            catch (IOException) { }

            Console.WriteLine("=== SmokeAlgo：12 类流程节点算法体检 ===");
            Console.WriteLine();

            _dir = Path.Combine(Path.GetTempPath(), "VisionSystem.SmokeAlgo");
            Directory.CreateDirectory(_dir);
            Console.WriteLine("临时图片目录：" + _dir);

            GenerateImages();

            int nativeVersion;
            try { nativeVersion = VisionAlgo.LibraryVersion; }
            catch (Exception ex)
            {
                Console.WriteLine("VisionAlgo.Native.dll 无法加载：" + ex.Message);
                nativeVersion = -1;
            }
            Check("VisionAlgo.Native.dll 加载成功", nativeVersion > 0, "version=" + nativeVersion);

            if (nativeVersion > 0)
            {
                TestImageSource();
                TestPreprocess();
                TestShapeMatch();
                TestTemplateMatchNcc();
                TestBlob();
                TestLineCaliper();
                TestCircleCaliper();
                TestSharpness();
                TestFitCircle();
                TestFitLine();
                TestPositionCorrect();
                TestResultOutput();
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine("!! VisionAlgo.Native 不可用：全部 12 类节点体检跳过。");
                Console.WriteLine("!! 请把 VisionAlgo.Native.dll 与 opencv_world455(d).dll 复制到本程序所在目录。");
            }

            Console.WriteLine();
            Console.WriteLine("=== PASS " + _pass + " / FAIL " + _fail + " ===");
            return _fail == 0 ? 0 : 1;
        }

        // =====================================================================
        //  断言 / 工具
        // =====================================================================

        private static void Check(string name, bool ok, string detail)
        {
            if (ok) { _pass++; Console.WriteLine("  [PASS] " + name + (detail.Length > 0 ? "  | " + detail : "")); }
            else { _fail++; Console.WriteLine("  [FAIL] " + name + (detail.Length > 0 ? "  | " + detail : "")); }
        }

        private static void Info(string text)
        {
            Console.WriteLine("        · " + text);
        }

        private static void CheckNear(string name, double actual, double truth, double tol,
                                      string unit, string label)
        {
            double dev = Math.Abs(actual - truth);
            string detail = string.Format(CultureInfo.InvariantCulture,
                "{0}：真值 {1}{3} / 实测 {2}{3} / 偏差 {4}{3}（容差 {5}{3}）",
                label, F(truth), F(actual), unit, F(dev), tol.ToString(CultureInfo.InvariantCulture));
            Check(name, dev <= tol, detail);
        }

        private static string F(double v)
        {
            return double.IsNaN(v) ? "NaN" : v.ToString("F2", CultureInfo.InvariantCulture);
        }

        private static bool ParsePattern<T>(string input, string pattern, out T p0)
        {
            p0 = default(T);
            if (string.IsNullOrEmpty(input)) return false;
            Match m = Regex.Match(input, pattern);
            if (!m.Success) return false;
            try
            {
                p0 = (T)Convert.ChangeType(m.Groups[1].Value, typeof(T), CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception) { return false; }
        }

        private static bool ParsePattern<T>(string input, string pattern, out T p0, out T p1)
        {
            p0 = default(T);
            p1 = default(T);
            if (string.IsNullOrEmpty(input)) return false;
            Match m = Regex.Match(input, pattern);
            if (!m.Success) return false;
            try
            {
                p0 = (T)Convert.ChangeType(m.Groups[1].Value, typeof(T), CultureInfo.InvariantCulture);
                p1 = (T)Convert.ChangeType(m.Groups[2].Value, typeof(T), CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception) { return false; }
        }

        // =====================================================================
        //  流程构造与执行
        // =====================================================================

        private static FlowNode Node(FlowNodeType type, params object[] keyValuePairs)
        {
            FlowNode node = FlowModule.CreateNode(type, 0f, 0f);
            for (int i = 0; i + 1 < keyValuePairs.Length; i += 2)
            {
                string key = (string)keyValuePairs[i];
                object v = keyValuePairs[i + 1];
                if (v is double) node.Set(key, (double)v);
                else if (v is int) node.Set(key, (int)v);
                else if (v is bool) node.Set(key, (bool)v);
                else node.Set(key, Convert.ToString(v, CultureInfo.InvariantCulture));
            }
            return node;
        }

        private static FlowDefinition Flow(params FlowNode[] nodes)
        {
            FlowDefinition definition = FlowDefinition.CreateEmpty("体检流程");
            for (int i = 0; i < nodes.Length; i++) definition.Nodes.Add(nodes[i]);
            for (int i = 1; i < nodes.Length; i++)
                definition.Links.Add(new FlowLink(nodes[i - 1].Id, nodes[i].Id));
            return definition;
        }

        private static FlowNode Source(string imageFile)
        {
            return Node(FlowNodeType.ImageSource, "Source", "File", "FilePath", imageFile, "TimeoutMs", 3000);
        }

        private sealed class RunBundle
        {
            public List<FlowNodeProgress> Plan = new List<FlowNodeProgress>();
            public FlowRunResult FlowRunResult;

            public string Sum(int position)
            {
                if (Plan == null || position < 0 || position >= Plan.Count) return "";
                return Plan[position].Summary ?? "";
            }

            public FlowNodeStatus Status(int position)
            {
                if (Plan == null || position < 0 || position >= Plan.Count) return FlowNodeStatus.Pending;
                return Plan[position].Status;
            }
        }

        private static RunBundle Run(FlowDefinition definition, ICamera camera = null)
        {
            RunBundle bundle = new RunBundle();
            try
            {
                using (FlowRunner runner = new FlowRunner())
                {
                    bundle.FlowRunResult = runner.Run(definition, camera);
                    if (bundle.FlowRunResult != null && bundle.FlowRunResult.Progress != null)
                        bundle.Plan = bundle.FlowRunResult.Progress;
                }
            }
            catch (Exception ex)
            {
                Check("整链执行未抛异常", false, ex.GetType().Name + ": " + ex.Message);
                return bundle;
            }
            if (bundle.FlowRunResult != null && bundle.FlowRunResult.Success)
                Check("整链执行成功", true, bundle.FlowRunResult.Summary);
            else
                Check("整链执行成功", false,
                      bundle.FlowRunResult == null ? "无结果"
                          : bundle.FlowRunResult.Message + " / " + bundle.FlowRunResult.Summary);
            return bundle;
        }

        // =====================================================================
        //  T01 图像源
        // =====================================================================

        private static void TestImageSource()
        {
            Console.WriteLine();
            Console.WriteLine("== T01 ImageSource 图像源 ==");
            string png = CombinedPng();

            // ① Source=File：帧尺寸必须等于合成图尺寸
            RunBundle p = Run(Flow(Source(png), Node(FlowNodeType.ResultOutput)));
            Check("T01 Source=File 取图成功", p.Status(0) == FlowNodeStatus.Ok, p.Sum(0));
            int width, height;
            bool parsed = ParsePattern(p.Sum(0), @"取图\s+(\d+)\s*x\s*(\d+)", out width, out height);
            Check("T01 帧宽 = 640", parsed && width == ImageWidth, "实测 " + width);
            Check("T01 帧高 = 480", parsed && height == ImageHeight, "实测 " + height);

            // ② ROI 裁剪：裁剪矩形会被严格遵循
            int cropW = 500, cropH = 400;
            RunBundle c = Run(Flow(Node(FlowNodeType.ImageSource, "Source", "File", "FilePath", png,
                                        "TimeoutMs", 3000, "RoiX", 0, "RoiY", 0,
                                        "RoiW", cropW, "RoiH", cropH),
                                   Node(FlowNodeType.ResultOutput)));
            Check("T01 ROI 裁剪取图成功", c.Status(0) == FlowNodeStatus.Ok, c.Sum(0));
            int gotW, gotH;
            bool cropParsed = ParsePattern(c.Sum(0), @"取图\s+(\d+)\s*x\s*(\d+)", out gotW, out gotH);
            Check("T01 ROI 裁剪宽度 = 500", cropParsed && gotW == cropW, "实测 " + gotW);
            Check("T01 ROI 裁剪高度 = 400", cropParsed && gotH == cropH, "实测 " + gotH);

            // ③ Source=Camera：用合成帧相机提供同一张图，等价于 ①
            using (TruthCamera cam = new TruthCamera(png))
            {
                cam.Open();   // TruthCamera.GrabOne 检查 _opened，未 Open 会返回「相机未打开」
                RunBundle r = Run(Flow(Node(FlowNodeType.ImageSource, "Source", "Camera", "TimeoutMs", 3000),
                                       Node(FlowNodeType.ResultOutput)), cam);
                Check("T01 Source=Camera 取图成功（合成帧相机）", r.Status(0) == FlowNodeStatus.Ok, r.Sum(0));
                int camW, camH;
                bool camParsed = ParsePattern(r.Sum(0), @"取图\s+(\d+)\s*x\s*(\d+)", out camW, out camH);
                Check("T01 Source=Camera 帧宽 = 640", camParsed && camW == ImageWidth, "实测 " + camW);
                Check("T01 Source=Camera 帧高 = 480", camParsed && camH == ImageHeight, "实测 " + camH);
            }

            // ④ 文件不存在：必须给出可读报错，不能崩
            RunBundle bad = Run(Flow(Source(Path.Combine(_dir, "__no_such__.png")),
                                     Node(FlowNodeType.ResultOutput)));
            Check("T01 文件不存在时判 NG 而非崩", bad.Status(0) == FlowNodeStatus.Ng, bad.Sum(0));
        }

        // =====================================================================
        //  T02 预处理
        // =====================================================================

        private static void TestPreprocess()
        {
            Console.WriteLine();
            Console.WriteLine("== T02 Preprocess 预处理 ==");
            string png = CombinedPng();

            RunBundle p = Run(Flow(Source(png), Node(FlowNodeType.Preprocess),
                                   Node(FlowNodeType.ResultOutput)));
            Check("T02 预处理节点执行成功", p.Status(1) == FlowNodeStatus.Ok, p.Sum(1));

            using (Bitmap raw = LoadBitmap(png))
            using (VisionAlgo algo = new VisionAlgo())
            {
                OpResult<Bitmap> r = algo.Preprocess(raw, PreprocessParam.Default());
                Check("T02 VisionAlgo.Preprocess 调用成功", r.Success, r.Message);
                if (r.Success && r.Value != null)
                {
                    using (Bitmap gray = r.Value)
                    {
                        Check("T02 输出宽 = 640", gray.Width == ImageWidth, "实测 " + gray.Width);
                        Check("T02 输出高 = 480", gray.Height == ImageHeight, "实测 " + gray.Height);
                        Check("T02 输出像素格式 = Format8bppIndexed（8bpp 灰度）",
                              gray.PixelFormat == PixelFormat.Format8bppIndexed, gray.PixelFormat.ToString());
                    }
                }
            }
        }

        // =====================================================================
        //  T03 形状匹配
        // =====================================================================

        private static void TestShapeMatch()
        {
            Console.WriteLine();
            Console.WriteLine("== T03 ShapeMatch 形状匹配 ==");
            string png = CombinedPng();

            RunBundle p = Run(Flow(Source(png), Node(FlowNodeType.ShapeMatch),
                                   Node(FlowNodeType.ResultOutput)));
            Info("形状匹配节点结果：[" + p.Sum(1) + "]");

            using (Bitmap raw = LoadBitmap(png))
            using (Bitmap other = LoadBitmap(DriftedPng()))
            using (VisionAlgo algo = new VisionAlgo())
            {
                OpResult<ShapeModelInfo> train = algo.CreateShapeModel(raw, new Rectangle(0, 0, 0, 0), 30.0);
                Check("T03 VisionAlgo.CreateShapeModel 调用成功", train.Success, train.Message);
                if (!train.Success) return;

                ShapeParam param = ShapeParam.Default();
                OpResult<List<ShapeMatchResult>> found = algo.FindShapeModel(raw, param, new Rectangle(0, 0, 0, 0));
                Check("T03 VisionAlgo.FindShapeModel 调用成功", found.Success, found.Message);
                if (!found.Success || found.Value == null || found.Value.Count == 0) return;

                ShapeMatchResult self = found.Value[0];
                OpResult<List<ShapeMatchResult>> moved = algo.FindShapeModel(other, param, new Rectangle(0, 0, 0, 0));
                if (!moved.Success || moved.Value == null || moved.Value.Count == 0) return;

                ShapeMatchResult flowed = moved.Value[0];
                CheckNear("T03 平移前后位姿 X 之差", flowed.Cx - self.Cx, TruthDriftDxPx,
                          TolerancePositionPx, "px", "ΔX");
                CheckNear("T03 平移前后位姿 Y 之差", flowed.Cy - self.Cy, TruthDriftDyPx,
                          TolerancePositionPx, "px", "ΔY");
            }
        }

        // =====================================================================
        //  T04 模板匹配 NCC
        // =====================================================================

        private static void TestTemplateMatchNcc()
        {
            Console.WriteLine();
            Console.WriteLine("== T04 TemplateMatch 模板匹配(NCC) ==");
            string png = CombinedPng();

            RunBundle p = Run(Flow(Source(png), Node(FlowNodeType.TemplateMatch),
                                   Node(FlowNodeType.ResultOutput)));
            Check("T04 模板匹配节点执行成功", p.Status(1) == FlowNodeStatus.Ok, p.Sum(1));
            Check("T04 模板匹配摘要非空", p.Sum(1).Length > 0, p.Sum(1));
        }

        // =====================================================================
        //  T05 Blob 分析
        // =====================================================================

        private static void TestBlob()
        {
            Console.WriteLine();
            Console.WriteLine("== T05 Blob 分析 ==");
            RunBundle p = Run(Flow(Source(BlobPng()), Node(FlowNodeType.Blob),
                                   Node(FlowNodeType.ResultOutput)));
            Check("T05 Blob 节点执行成功", p.Status(1) == FlowNodeStatus.Ok, p.Sum(1));

            using (Bitmap raw = LoadBitmap(BlobPng()))
            using (VisionAlgo algo = new VisionAlgo())
            {
                OpResult<List<BlobResult>> r = algo.AnalyzeBlobs(raw, 100, 0, 256);
                Check("T05 VisionAlgo.AnalyzeBlobs 调用成功", r.Success, r.Message);
                if (!r.Success || r.Value == null) return;

                Check("T05 检测到的方块个数 = 5", r.Value.Count == TruthBlockCount, "实测 " + r.Value.Count);

                double worstDeviation = 0.0;
                string worstAreaDetail = "";
                foreach (BlobResult b in r.Value)
                {
                    double deviation = Math.Abs(b.Area - TruthBlockAreaPx) / TruthBlockAreaPx;
                    if (deviation > worstDeviation)
                    {
                        worstDeviation = deviation;
                        worstAreaDetail = string.Format(CultureInfo.InvariantCulture,
                            "面积 {0:F0} / 期望 {1:F0}（相对偏差 {2:P2}）", b.Area, TruthBlockAreaPx, deviation);
                    }
                }
                Check("T05 单块面积与真值 6400 的偏差全部 <= 5%", worstDeviation <= ToleranceAreaPercent,
                      worstAreaDetail);
            }
        }

        // =====================================================================
        //  T06 直线卡尺
        // =====================================================================

        private static void TestLineCaliper()
        {
            Console.WriteLine();
            Console.WriteLine("== T06 LineCaliper 直线卡尺 ==");
            Info("直线卡尺默认 ROI：(100,100)-(300,100)，默认投影长度 20");

            RunBundle p = Run(Flow(Source(LineOnlyPng()), Node(FlowNodeType.LineCaliper),
                                   Node(FlowNodeType.ResultOutput)));
            Check("T06 直线卡尺节点执行成功", p.Status(1) == FlowNodeStatus.Ok, p.Sum(1));
        }

        // =====================================================================
        //  T07 圆卡尺
        // =====================================================================

        private static void TestCircleCaliper()
        {
            Console.WriteLine();
            Console.WriteLine("== T07 CircleCaliper 圆卡尺 ==");
            Info("圆卡尺默认 ROI：中心 (320,240)，内半径 80，外半径 120");

            RunBundle p = Run(Flow(Source(CircleOnlyPng()), Node(FlowNodeType.CircleCaliper),
                                   Node(FlowNodeType.ResultOutput)));
            Check("T07 圆卡尺节点执行成功", p.Status(1) == FlowNodeStatus.Ok, p.Sum(1));
            double diameter;
            bool parsed = ParsePattern(p.Sum(1), @"直径\s+(-?\d+(?:\.\d+)?)", out diameter);
            CheckNear("T07 实测直径", parsed ? diameter : double.NaN, TruthCircleDiameter,
                      TolerancePositionPx, "px", "直径");
        }

        // =====================================================================
        //  T08 清晰度
        // =====================================================================

        private static void TestSharpness()
        {
            Console.WriteLine();
            Console.WriteLine("== T08 Sharpness 清晰度 ==");
            Info("清晰度默认值 MinSharpness=100；Score 越大越清晰");

            RunBundle sharp = Run(Flow(Source(CombinedPng()), Node(FlowNodeType.Sharpness),
                                       Node(FlowNodeType.ResultOutput)));
            Check("T08 清晰图（有多个硬边缘）判 OK", sharp.Status(1) == FlowNodeStatus.Ok, sharp.Sum(1));
            double sharpScore;
            bool sharpParsed = ParsePattern(sharp.Sum(1), @"清晰度\s+(-?\d+(?:\.\d+)?)", out sharpScore);

            RunBundle flat = Run(Flow(Source(GradientPng()), Node(FlowNodeType.Sharpness),
                                      Node(FlowNodeType.ResultOutput)));
            double flatScore;
            bool flatParsed = ParsePattern(flat.Sum(1), @"清晰度\s+(-?\d+(?:\.\d+)?)", out flatScore);
            Check("T08 平滑渐变图得分低于默认下限 100", flatParsed && flatScore < 100.0,
                  "实测 " + F(flatScore));
            Check("T08 清晰图得分 > 渐变图得分",
                  sharpParsed && flatParsed && sharpScore > flatScore,
                  "清晰图 " + F(sharpScore) + " vs 渐变图 " + F(flatScore));
        }

        // =====================================================================
        //  T09 圆拟合
        // =====================================================================

        private static void TestFitCircle()
        {
            Console.WriteLine();
            Console.WriteLine("== T09 FitCircle 圆拟合 ==");
            Info("圆拟合：对最大轮廓做最小外接圆拟合；合成图为 (320,240) 半径 100 的实心圆");

            RunBundle p = Run(Flow(Source(CircleOnlyPng()), Node(FlowNodeType.FitCircle),
                                   Node(FlowNodeType.ResultOutput)));
            Check("T09 圆拟合节点执行成功", p.Status(1) == FlowNodeStatus.Ok, p.Sum(1));
            Check("T09 圆拟合摘要非空", p.Sum(1).Length > 0, p.Sum(1));
        }

        // =====================================================================
        //  T10 直线拟合
        // =====================================================================

        private static void TestFitLine()
        {
            Console.WriteLine();
            Console.WriteLine("== T10 FitLine 直线拟合 ==");
            Info("直线拟合：对最大轮廓做 cv::fitLine(DIST_L2)；合成图为横穿整幅的水平直线");

            RunBundle p = Run(Flow(Source(LineOnlyPng()), Node(FlowNodeType.FitLine),
                                   Node(FlowNodeType.ResultOutput)));
            Check("T10 直线拟合节点执行成功", p.Status(1) == FlowNodeStatus.Ok, p.Sum(1));
            Check("T10 直线拟合摘要非空", p.Sum(1).Length > 0, p.Sum(1));
        }

        // =====================================================================
        //  T11 位置修正
        // =====================================================================

        private static void TestPositionCorrect()
        {
            Console.WriteLine();
            Console.WriteLine("== T11 PositionCorrect 位置修正 ==");
            Info("位置修正：默认 RefSource=Auto，取上游最近一个匹配结果的第 1 个目标作基准");

            // ① 上游有匹配结果：应判 OK，且 dX/dY 为 0（基准即当前帧位姿）
            RunBundle p = Run(Flow(Source(CombinedPng()), Node(FlowNodeType.ShapeMatch),
                                   Node(FlowNodeType.PositionCorrect), Node(FlowNodeType.ResultOutput)));
            Check("T11 上游有定位结果时判 OK", p.Status(2) == FlowNodeStatus.Ok, p.Sum(2));
            double dx, dy;
            bool parsed = ParsePattern(p.Sum(2), @"dX=(-?\d+(?:\.\d+)?)\s+dY=(-?\d+(?:\.\d+)?)", out dx, out dy);
            CheckNear("T11 dX", parsed ? dx : double.NaN, 0.0, TolerancePositionPx, "px", "dX");
            CheckNear("T11 dY", parsed ? dy : double.NaN, 0.0, TolerancePositionPx, "px", "dY");

            // ② 上游没有定位结果：按「Pose 输入端子为可选」的裁决，应【跳过修正而不判 NG】，
            //    但必须给出可读原因（摘要含「上游无可用定位结果」）。这是与早期 PRD FR-ALG-08
            //    「判 NG」不同的、经确认后的新行为。
            RunBundle noPose = Run(Flow(Source(CombinedPng()), Node(FlowNodeType.PositionCorrect),
                                        Node(FlowNodeType.ResultOutput)));
            Check("T11 上游无定位结果时跳过修正（不判 NG）", noPose.Status(1) != FlowNodeStatus.Ng, noPose.Sum(1));
            Check("T11 摘要含「上游无可用定位结果」", noPose.Sum(1).Contains("上游无可用定位结果"),
                  noPose.Sum(1));
        }

        // =====================================================================
        //  T12 结果输出
        // =====================================================================

        private static void TestResultOutput()
        {
            Console.WriteLine();
            Console.WriteLine("== T12 ResultOutput 结果输出 ==");
            Info("结果输出：汇总全链判定，有任一节点 NG 则整体 NG");

            // ① 全通过 → 汇总 OK
            RunBundle ok = Run(Flow(Source(CircleOnlyPng()), Node(FlowNodeType.FitCircle),
                                    Node(FlowNodeType.ResultOutput)));
            Check("T12 全通过时结果输出判 OK", ok.Status(2) == FlowNodeStatus.Ok, ok.Sum(2));

            // ② 上游 NG → 结果输出必须点名 NG 节点，整链结论为 NG
            RunBundle ng = Run(Flow(Source(GradientPng()), Node(FlowNodeType.Sharpness),
                                    Node(FlowNodeType.ResultOutput)));
            Check("T12 上游 NG 时结果输出判 NG", ng.Status(2) == FlowNodeStatus.Ng, ng.Sum(2));
            Check("T12 摘要点名失败节点（清晰度）", ng.Sum(2).Contains("清晰度"), ng.Sum(2));
            Check("T12 整链结论 Passed=false",
                  ng.FlowRunResult != null && !ng.FlowRunResult.Passed,
                  ng.FlowRunResult == null ? "无结果" : "Passed=" + ng.FlowRunResult.Passed);
        }

        // =====================================================================
        //  合成帧相机（Source=Camera 用）
        // =====================================================================

        private sealed class TruthCamera : ICamera
        {
            private readonly string _path;
            private bool _opened;
            private bool _grabbing;

            public TruthCamera(string path)
            {
                _path = path;
                Info = new CameraInfo
                {
                    Id = "SMOKE-TRUTH",
                    Name = "合成帧相机",
                    Vendor = "Smoke",
                    Serial = "SMOKE-TRUTH",
                    Model = "TruthFrame"
                };
            }

            public CameraInfo Info { get; private set; }
            public bool IsOpen { get { return _opened; } }
            public bool IsGrabbing { get { return _grabbing; } }

            public event EventHandler<Bitmap> FrameReceived;
            public event EventHandler<string> ErrorOccurred;

            public OpResult Open() { _opened = true; return OpResult.Ok(); }

            public OpResult Close()
            {
                _grabbing = false;
                _opened = false;
                return OpResult.Ok();
            }

            public OpResult StartGrab() { _grabbing = true; return OpResult.Ok(); }
            public OpResult StopGrab() { _grabbing = false; return OpResult.Ok(); }

            public OpResult<Bitmap> GrabOne(int timeoutMs = 3000)
            {
                if (!_opened) return OpResult<Bitmap>.Fail("相机未打开");
                try { return OpResult<Bitmap>.Ok(LoadBitmap(_path)); }
                catch (Exception ex) { return OpResult<Bitmap>.Fail("取帧失败：" + ex.Message); }
            }

            public void Dispose() { Close(); }
        }

        // =====================================================================
        //  合成图生成（纯 System.Drawing，不依赖任何外部图片文件）
        // =====================================================================

        private static string CombinedPng() { return Path.Combine(_dir, "smoke_composite.png"); }
        private static string DriftedPng() { return Path.Combine(_dir, "smoke_composite_drifted.png"); }
        private static string BlobPng() { return Path.Combine(_dir, "smoke_blob.png"); }
        private static string LineOnlyPng() { return Path.Combine(_dir, "smoke_line.png"); }
        private static string CircleOnlyPng() { return Path.Combine(_dir, "smoke_circle.png"); }
        private static string GradientPng() { return Path.Combine(_dir, "smoke_gradient.png"); }

        private static Bitmap LoadBitmap(string path)
        {
            using (Image probe = Image.FromFile(path))
                return new Bitmap(probe);
        }

        private static Bitmap NewCanvas()
        {
            Bitmap bmp = new Bitmap(ImageWidth, ImageHeight, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(bmp)) g.Clear(Color.Black);
            return bmp;
        }

        /// <summary>按像素精确填色：不经过 GDI+ 的插值与抗锯齿，保证边缘位置就是真值。</summary>
        private static void FillRectRaw(Bitmap bmp, int x0, int y0, int x1, int y1, int gray)
        {
            if (x1 > bmp.Width) x1 = bmp.Width;
            if (y1 > bmp.Height) y1 = bmp.Height;
            BitmapData d = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                                        ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
            try
            {
                int stride = d.Stride;                       // 有符号步长（GDI+ 可能是自底向上）
                int xBytes = (x1 - x0) * 3;
                byte[] seg = new byte[xBytes];
                for (int x = x0; x < x1; x++)
                {
                    int o = (x - x0) * 3;
                    seg[o] = (byte)gray;
                    seg[o + 1] = (byte)gray;
                    seg[o + 2] = (byte)gray;
                }
                // 只写 [x0,x1) 这一段，绝不覆盖同行的其他像素。
                // 旧实现是「整行拷贝」：同一行内并排的方块会被后画的方块整行抹零，
                // 导致 5 个方块只存活最后画的 2 个（实测原生 Blob 分析本身是正确的）。
                IntPtr baseRow = d.Scan0;
                for (int y = y0; y < y1; y++)
                    Marshal.Copy(seg, 0, IntPtr.Add(baseRow, y * stride + x0 * 3), xBytes);
            }
            finally { bmp.UnlockBits(d); }
        }

        /// <summary>白色水平直线：占 12 行，上边缘 y=94，下边缘 y=106（中心 y=100）。</summary>
        private static void DrawHorizontalLine(Bitmap bmp, int cy)
        {
            FillRectRaw(bmp, 0, cy - 6, ImageWidth, cy + 6, 255);
        }

        /// <summary>白色竖直直线：占 12 列，右边缘 x = leftX + 12。</summary>
        private static void DrawVerticalLine(Bitmap bmp, int leftX)
        {
            FillRectRaw(bmp, leftX, 0, leftX + 12, ImageHeight, 255);
        }

        private static void DrawCircleOutline(Bitmap bmp, int cx, int cy, int r)
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen pen = new Pen(Color.White, 1))
                    g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
            }
        }

        private static void GenerateComposite(string path, int driftDx, int driftDy)
        {
            using (Bitmap bmp = NewCanvas())
            {
                // 整幅复合图（十字 + 圆）都按 drift 平移，与顶部真值表注释一致：
                // 「合成图的已知位移：整幅图相对原图平移 (+40, +30)」。
                // 这样 T03 用整幅图训练 + 搜索时，才能真的检验「形状匹配能跟踪平移目标」。
                DrawHorizontalLine(bmp, 100 + driftDy);
                DrawVerticalLine(bmp, 300 + driftDx);
                DrawCircleOutline(bmp, TruthCircleCx + driftDx, TruthCircleCy + driftDy, TruthCircleR);
                bmp.Save(path, ImageFormat.Png);
            }
        }

        private static void GenerateBlob(string path)
        {
            using (Bitmap bmp = NewCanvas())
            {
                FillRectRaw(bmp, 40, 40, 120, 120, 255);
                FillRectRaw(bmp, 240, 40, 320, 120, 255);
                FillRectRaw(bmp, 440, 40, 520, 120, 255);
                FillRectRaw(bmp, 40, 300, 120, 380, 255);
                FillRectRaw(bmp, 240, 300, 320, 380, 255);
                bmp.Save(path, ImageFormat.Png);
            }
        }

        private static void GenerateLineOnly(string path)
        {
            using (Bitmap bmp = NewCanvas())
            {
                DrawHorizontalLine(bmp, 100);
                bmp.Save(path, ImageFormat.Png);
            }
        }

        private static void GenerateCircleOnly(string path)
        {
            using (Bitmap bmp = NewCanvas())
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush brush = new SolidBrush(Color.White))
                    g.FillEllipse(brush, TruthCircleCx - TruthCircleR, TruthCircleCy - TruthCircleR,
                                  TruthCircleR * 2, TruthCircleR * 2);
                bmp.Save(path, ImageFormat.Png);
            }
        }

        private static void GenerateGradient(string path)
        {
            using (Bitmap bmp = NewCanvas())
            {
                BitmapData d = bmp.LockBits(new Rectangle(0, 0, ImageWidth, ImageHeight),
                                            ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
                try
                {
                    int stride = Math.Abs(d.Stride);
                    byte[] row = new byte[stride];
                    for (int x = 0; x < ImageWidth; x++)
                    {
                        byte v = (byte)(x * 255 / (ImageWidth - 1));
                        row[x * 3] = v; row[x * 3 + 1] = v; row[x * 3 + 2] = v;
                    }
                    for (int y = 0; y < ImageHeight; y++)
                        Marshal.Copy(row, 0, IntPtr.Add(d.Scan0, y * d.Stride), stride);
                }
                finally { bmp.UnlockBits(d); }
                bmp.Save(path, ImageFormat.Png);
            }
        }

        private static void GenerateImages()
        {
            Console.WriteLine("-- 合成图（flows 性审查）：每张图的几何真值见 Program.cs 顶部真值表 --");
            GenerateComposite(CombinedPng(), 0, 0);
            GenerateComposite(DriftedPng(), (int)TruthDriftDxPx, (int)TruthDriftDyPx);
            GenerateBlob(BlobPng());
            GenerateLineOnly(LineOnlyPng());
            GenerateCircleOnly(CircleOnlyPng());
            GenerateGradient(GradientPng());
            Console.WriteLine("   composite / composite_drifted / blob / line / circle / gradient 已生成");
        }
    }
}
