using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace VisionPlatform.Flow
{
    /// <summary>
    /// 深度学习节点的"像素级效果图"生成：分割掩膜上色、深度伪彩。
    /// <para>
    /// 这些效果矢量层画不出来（掩膜是逐像素的、深度是整幅渐变），所以走位图叠加通道
    /// （<see cref="FlowNodeProgress.Overlay"/>），由预览面板与帧合成后显示。
    /// </para>
    /// </summary>
    public static class NodeOverlayFactory
    {
        /// <summary>目标着色盘（半透明填充，够用且区分度高）。</summary>
        private static readonly Color[] Palette =
        {
            Color.FromArgb(255, 80, 80),      // 红
            Color.FromArgb(80, 170, 255),     // 蓝
            Color.FromArgb(110, 220, 120),    // 绿
            Color.FromArgb(255, 190, 70),     // 橙
            Color.FromArgb(200, 130, 255),    // 紫
            Color.FromArgb(90, 225, 220),     // 青
            Color.FromArgb(255, 130, 200),    // 粉
            Color.FromArgb(190, 210, 110)     // 黄绿
        };

        /// <summary>取第 i 个目标的颜色（循环取）。</summary>
        public static Color ColorOf(int index)
        {
            int n = Palette.Length;
            return Palette[((index % n) + n) % n];
        }

        /// <summary>
        /// 把分割标签图（0=背景，i=第 i 个目标，1 起）画成彩色半透明叠加层。
        /// </summary>
        /// <param name="labels">标签图，长度 = mapW × mapH。</param>
        /// <param name="targetCount">目标数（用于配色与合法性校验）。</param>
        public static Bitmap SegMask(byte[] labels, int mapW, int mapH, int width, int height, int targetCount)
        {
            if (labels == null || mapW <= 0 || mapH <= 0 || width <= 0 || height <= 0) return null;

            var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, width, height),
                                           ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                var buf = new byte[data.Stride * height];

                // 指标图按比例映射到输出尺寸（双线性会糊边界，用最近邻更锐）
                for (int y = 0; y < height; y++)
                {
                    int sy = (int)((long)y * mapH / height);
                    if (sy >= mapH) sy = mapH - 1;

                    for (int x = 0; x < width; x++)
                    {
                        int sx = (int)((long)x * mapW / width);
                        if (sx >= mapW) sx = mapW - 1;

                        int id = labels[sy * mapW + sx];
                        if (id <= 0) continue;
                        if (targetCount > 0 && id > targetCount) continue;

                        Color c = ColorOf(id - 1);
                        int off = y * data.Stride + x * 4;
                        buf[off + 0] = c.B;         // BGRA
                        buf[off + 1] = c.G;
                        buf[off + 2] = c.R;
                        buf[off + 3] = 105;         // 半透明：能看清底图纹理
                    }
                }

                Marshal.Copy(buf, 0, data.Scan0, buf.Length);
            }
            finally
            {
                bmp.UnlockBits(data);
            }
            return bmp;
        }

        /// <summary>
        /// 把 0~1 的深度图渲染成伪彩叠加层（近=暖色，远=冷色）。
        /// </summary>
        public static Bitmap DepthHeat(float[] map, int mapW, int mapH, int width, int height)
        {
            if (map == null || mapW <= 0 || mapH <= 0 || width <= 0 || height <= 0) return null;

            var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, width, height),
                                           ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                var buf = new byte[data.Stride * height];

                for (int y = 0; y < height; y++)
                {
                    int sy = (int)((long)y * mapH / height);
                    if (sy >= mapH) sy = mapH - 1;
                    int row = sy * mapW;
                    int dstRow = y * data.Stride;

                    for (int x = 0; x < width; x++)
                    {
                        int sx = (int)((long)x * mapW / width);
                        if (sx >= mapW) sx = mapW - 1;

                        float v = map[row + sx];
                        if (v < 0f) v = 0f;
                        if (v > 1f) v = 1f;

                        // 蓝(远) → 青 → 绿 → 黄 → 红(近)
                        int r, g, b;
                        if (v < 0.25f)      { float t = v / 0.25f;          r = 0;   g = (int)(255 * t);       b = 255; }
                        else if (v < 0.5f)  { float t = (v - 0.25f) / 0.25f; r = 0;   g = 255;                  b = (int)(255 * (1 - t)); }
                        else if (v < 0.75f) { float t = (v - 0.5f) / 0.25f;  r = (int)(255 * t); g = 255;       b = 0; }
                        else                { float t = (v - 0.75f) / 0.25f; r = 255; g = (int)(255 * (1 - t)); b = 0; }

                        int off = dstRow + x * 4;
                        buf[off + 0] = (byte)b;
                        buf[off + 1] = (byte)g;
                        buf[off + 2] = (byte)r;
                        buf[off + 3] = 150;
                    }
                }

                Marshal.Copy(buf, 0, data.Scan0, buf.Length);
            }
            finally
            {
                bmp.UnlockBits(data);
            }
            return bmp;
        }

        /// <summary>
        /// COCO-17 人体骨架连接（关键点索引对）。姿态模型不是 17 点时返回空表 ——
        /// 不认识的关键点只画点、不连线，避免画出错误的骨架。
        /// </summary>
        public static readonly int[,] CocoSkeleton =
        {
            {15,13},{13,11},{16,14},{14,12},{11,12},{5,11},{6,12},{5,6},
            {5,7},{7,9},{6,8},{8,10},{1,2},{0,1},{0,2},{1,3},{2,4},{3,5},{4,6},{0,5},{0,6}
        };

        /// <summary>关键点数正好 17 时才认为是 COCO 人体骨架。</summary>
        public static bool IsCoco17(int keypoints)
        {
            return keypoints == 17;
        }
    }
}
