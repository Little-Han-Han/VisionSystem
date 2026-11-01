using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;

namespace VisionPlatform.Native
{
    /// <summary>
    /// 深度学习推理（OpenVINO）：语义分割 / 姿态估计 / 深度估计。
    /// <para>
    /// 与 <see cref="VisionAlgo"/> 里 `va_det_*`（目标检测）是并列的三套原生入口，
    /// 预处理与 NMS 在 C++ 侧共用，C# 只看结果。
    /// 字符串（模型路径 / 设备名）按 UTF-8 字节传，避免默认 ANSI 封送破坏中文路径。
    /// </para>
    /// </summary>
    public static class NativeDl
    {
        private const string Dll = "VisionAlgo.Native.dll";

        /// <summary>原生返回码：尺寸不匹配（结果比缓冲区多，前 N 个仍然有效）。</summary>
        private const int ErrSize = 3;

        /// <summary>任务编号（与 C 端 VA_DL_TASK_* 一致）。</summary>
        public const int TaskSeg = 1;
        public const int TaskPose = 2;
        public const int TaskDepth = 3;

        /// <summary>推理后端：OpenVINO（当前唯一可用）。</summary>
        public const int BackendOpenVino = 0;
        /// <summary>推理后端：CUDA（预留，未实现）。</summary>
        public const int BackendCuda = 1;

        [StructLayout(LayoutKind.Sequential)]
        public struct VA_DlBox
        {
            public float x, y, w, h;
            public int class_id;
            public float score;
            public int mask_area;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VA_DlKeyPoint
        {
            public float x, y, score;
        }

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        private static extern int va_dl_seg_create(IntPtr handle, byte[] model_path, byte[] device);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        private static extern int va_dl_seg_run(IntPtr handle, ref VA_Image img,
                                                float score_thresh, float iou_thresh,
                                                [In, Out] VA_DlBox[] out_boxes,
                                                int max_count, out int out_count);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        private static extern int va_dl_pose_create(IntPtr handle, byte[] model_path, byte[] device);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        private static extern int va_dl_pose_run(IntPtr handle, ref VA_Image img,
                                                 float score_thresh, float iou_thresh,
                                                 [In, Out] VA_DlBox[] out_boxes,
                                                 int max_count, out int out_count,
                                                 [In, Out] VA_DlKeyPoint[] out_kps,
                                                 int kps_capacity,
                                                 out int out_kps_per_box, out int out_kps_written);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        private static extern int va_dl_pose_keypoints(IntPtr handle, out int out_k);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        private static extern int va_dl_depth_create(IntPtr handle, byte[] model_path, byte[] device);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        private static extern int va_dl_depth_run(IntPtr handle, ref VA_Image img,
                                                  [In, Out] float[] out_map, int width, int height,
                                                  out float out_min, out float out_max);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        private static extern int va_dl_clear(IntPtr handle, int task);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        private static extern int va_dl_backend_available(int backend);

        /// <summary>该后端是否可用（CUDA 现在恒为 false，UI 据此只列可用项）。</summary>
        public static bool BackendAvailable(int backend)
        {
            try { return va_dl_backend_available(backend) != 0; }
            catch (Exception) { return false; }
        }

        /// <summary>一个深度学习目标（分割 / 姿态共用）。</summary>
        public sealed class DlTarget
        {
            public double X, Y, Width, Height;
            public int ClassId;
            public double Score;
            /// <summary>分割专用：掩膜像素数（框内 sigmoid(coef·proto) &gt; 0.5 的个数）。</summary>
            public int MaskArea;
            /// <summary>姿态专用：关键点（原图像素坐标）。</summary>
            public List<PointF> KeyPoints = new List<PointF>();
            /// <summary>姿态专用：每个关键点的置信度。</summary>
            public List<float> KeyPointScores = new List<float>();
        }

        private static byte[] Utf8(string s)
        {
            return System.Text.Encoding.UTF8.GetBytes(s ?? "");
        }

        private static byte[] Utf8Z(string s)
        {
            byte[] raw = Utf8(s);
            byte[] z = new byte[raw.Length + 1];
            Array.Copy(raw, z, raw.Length);
            return z;
        }

        /// <summary>加载分割模型。</summary>
        public static int SegCreate(IntPtr handle, string modelPath, string device)
        {
            return va_dl_seg_create(handle, Utf8Z(modelPath), Utf8Z(device));
        }

        /// <summary>分割推理：返回目标列表；err 非空表示失败原因。</summary>
        public static List<DlTarget> SegRun(IntPtr handle, ref VA_Image img,
                                            float scoreThresh, float iouThresh, int maxCount,
                                            out string err)
        {
            err = "";
            var list = new List<DlTarget>();
            if (maxCount <= 0) maxCount = 100;

            var buf = new VA_DlBox[maxCount];
            int count;
            int rc = va_dl_seg_run(handle, ref img, scoreThresh, iouThresh, buf, maxCount, out count);

            if (rc != 0 && rc != ErrSize)
            {
                err = "原生返回码 " + rc;
                return list;
            }

            for (int i = 0; i < count; i++)
            {
                list.Add(new DlTarget
                {
                    X = buf[i].x, Y = buf[i].y,
                    Width = buf[i].w, Height = buf[i].h,
                    ClassId = buf[i].class_id,
                    Score = buf[i].score,
                    MaskArea = buf[i].mask_area
                });
            }
            return list;
        }

        /// <summary>加载姿态模型。</summary>
        public static int PoseCreate(IntPtr handle, string modelPath, string device)
        {
            return va_dl_pose_create(handle, Utf8Z(modelPath), Utf8Z(device));
        }

        /// <summary>取关键点数（COCO 人体为 17）。</summary>
        public static int PoseKeypoints(IntPtr handle)
        {
            int k;
            return va_dl_pose_keypoints(handle, out k) == 0 ? k : 0;
        }

        /// <summary>姿态推理：每个目标带关键点。</summary>
        public static List<DlTarget> PoseRun(IntPtr handle, ref VA_Image img,
                                             float scoreThresh, float iouThresh, int maxCount,
                                             out string err)
        {
            err = "";
            var list = new List<DlTarget>();
            if (maxCount <= 0) maxCount = 100;

            var boxes = new VA_DlBox[maxCount];
            int count;
            int kpsPerBox = 0, kpsWritten = 0;

            var kps = new VA_DlKeyPoint[maxCount * 64];   // 最多 64 个关键点/目标
            int rc = va_dl_pose_run(handle, ref img, scoreThresh, iouThresh, boxes, maxCount, out count,
                                    kps, kps.Length, out kpsPerBox, out kpsWritten);

            if (rc != 0 && rc != ErrSize)
            {
                err = "原生返回码 " + rc;
                return list;
            }

            int offset = 0;
            for (int i = 0; i < count; i++)
            {
                var t = new DlTarget
                {
                    X = boxes[i].x, Y = boxes[i].y,
                    Width = boxes[i].w, Height = boxes[i].h,
                    ClassId = boxes[i].class_id,
                    Score = boxes[i].score
                };

                if (kpsPerBox > 0 && offset + kpsPerBox <= kpsWritten)
                {
                    for (int j = 0; j < kpsPerBox; j++)
                    {
                        t.KeyPoints.Add(new PointF(kps[offset + j].x, kps[offset + j].y));
                        t.KeyPointScores.Add(kps[offset + j].score);
                    }
                    offset += kpsPerBox;
                }
                list.Add(t);
            }
            return list;
        }

        /// <summary>加载深度模型。</summary>
        public static int DepthCreate(IntPtr handle, string modelPath, string device)
        {
            return va_dl_depth_create(handle, Utf8Z(modelPath), Utf8Z(device));
        }

        /// <summary>
        /// 深度推理：返回 0~1 归一化深度图（尺寸 = 传入图尺寸），并给出原始值域。
        /// </summary>
        public static float[] DepthRun(IntPtr handle, ref VA_Image img, int width, int height,
                                       out float min, out float max, out string err)
        {
            err = "";
            min = 0f; max = 0f;
            if (width <= 0 || height <= 0)
            {
                err = "图像尺寸非法";
                return null;
            }

            var map = new float[width * height];
            int rc = va_dl_depth_run(handle, ref img, map, width, height, out min, out max);
            if (rc != 0)
            {
                err = "原生返回码 " + rc;
                return null;
            }
            return map;
        }

        /// <summary>释放指定任务的模型。</summary>
        public static int Clear(IntPtr handle, int task)
        {
            return va_dl_clear(handle, task);
        }

        // ---------------------- 深度学习 OCR（PaddleOCR）---------------------

        [StructLayout(LayoutKind.Sequential)]
        public struct VA_OcrDlLine
        {
            public float x, y, w, h;
            public float score;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 192)]
            public byte[] text;
        }

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        private static extern int va_ocrdl_create(IntPtr handle, byte[] det_model, byte[] rec_model,
                                                  byte[] dict_path, byte[] device);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        private static extern int va_ocrdl_run(IntPtr handle, ref VA_Image img,
                                               float box_thresh, float unclip_ratio,
                                               [In, Out] VA_OcrDlLine[] out_lines,
                                               int max_count, out int out_count,
                                               byte[] out_text, int text_cap, out int out_text_len);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        private static extern int va_ocrdl_clear(IntPtr handle);

        /// <summary>一行深度学习 OCR 结果。</summary>
        public sealed class OcrDlLine
        {
            public double X, Y, Width, Height;
            public double Score;
            public string Text = "";
        }

        /// <summary>加载 PaddleOCR 两个模型 + 字典。</summary>
        public static int OcrDlCreate(IntPtr handle, string detModel, string recModel,
                                      string dictPath, string device)
        {
            return va_ocrdl_create(handle, Utf8Z(detModel), Utf8Z(recModel),
                                   Utf8Z(dictPath), Utf8Z(device));
        }

        /// <summary>OCR 推理：返回每行文本 + 整段文本。</summary>
        public static List<OcrDlLine> OcrDlRun(IntPtr handle, ref VA_Image img,
                                               float boxThresh, float unclipRatio, int maxCount,
                                               out string fullText, out string err)
        {
            fullText = "";
            err = "";
            var list = new List<OcrDlLine>();
            if (maxCount <= 0) maxCount = 200;

            var buf = new VA_OcrDlLine[maxCount];
            for (int i = 0; i < buf.Length; i++) buf[i].text = new byte[192];

            byte[] text = new byte[4096];
            int count, textLen;

            int rc = va_ocrdl_run(handle, ref img, boxThresh, unclipRatio, buf, maxCount,
                                  out count, text, text.Length, out textLen);

            if (rc != 0 && rc != ErrSize)
            {
                err = "原生返回码 " + rc;
                return list;
            }

            for (int i = 0; i < count; i++)
            {
                list.Add(new OcrDlLine
                {
                    X = buf[i].x, Y = buf[i].y,
                    Width = buf[i].w, Height = buf[i].h,
                    Score = buf[i].score,
                    Text = Utf8Str(buf[i].text)
                });
            }

            if (textLen > 0) fullText = System.Text.Encoding.UTF8.GetString(text, 0, textLen);
            return list;
        }

        /// <summary>释放 OCR-DL 的两个模型。</summary>
        public static int OcrDlClear(IntPtr handle)
        {
            return va_ocrdl_clear(handle);
        }

        private static string Utf8Str(byte[] raw)
        {
            if (raw == null) return "";
            int n = Array.IndexOf(raw, (byte)0);
            if (n < 0) n = raw.Length;
            return System.Text.Encoding.UTF8.GetString(raw, 0, n);
        }
    }
}
