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
        /// 把 Blob 标签图（0=背景，k=第 k 个连通域，1 起）画成彩色半透明叠加层。
        /// <para>与 <see cref="SegMask"/> 的区别：标签是 int（连通域数可超过 255），
        /// 且不透明度稍高 —— 现场要看清"每块区域的边界在哪"，太淡会糊成一片。</para>
        /// </summary>
        /// <param name="offsetX">标签图左上角在整帧中的 X（ROI 裁剪时 = 裁剪框左上角）。</param>
        /// <param name="offsetY">标签图左上角在整帧中的 Y。</param>
        public static Bitmap BlobMask(int[] labels, int mapW, int mapH, int width, int height,
                                      int offsetX = 0, int offsetY = 0)
        {
            if (labels == null || mapW <= 0 || mapH <= 0 || width <= 0 || height <= 0) return null;
            if (labels.Length < mapW * mapH) return null;

            var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, width, height),
                                           ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                var buf = new byte[data.Stride * height];

                for (int y = 0; y < height; y++)
                {
                    // 先换算到标签图坐标；落在偏移区外直接跳过（ROI 外的像素不着色）
                    int my = y - offsetY;
                    if (my < 0 || my >= mapH) continue;
                    int row = my * mapW;
                    int dstRow = y * data.Stride;

                    for (int x = 0; x < width; x++)
                    {
                        int mx = x - offsetX;
                        if (mx < 0 || mx >= mapW) continue;

                        int id = labels[row + mx];
                        if (id <= 0) continue;

                        Color c = ColorOf(id - 1);
                        int off = dstRow + x * 4;
                        buf[off + 0] = c.B;         // BGRA
                        buf[off + 1] = c.G;
                        buf[off + 2] = c.R;
                        buf[off + 3] = 125;
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
        /// 把异常检测的「相对阈值倍数」热图渲染成伪彩叠加层，只覆盖 ROI 那块，其余区域透明。
        /// </summary>
        /// <param name="rel">异常分（1.0 = 阈值线），长度 = mapW × mapH。</param>
        /// <param name="offX">ROI 左上角在整帧中的 X。</param>
        /// <param name="offY">ROI 左上角在整帧中的 Y。</param>
        /// <remarks>
        /// 【两条硬约束 —— 现场反馈"整块产品被标红、图都看不见了"】
        /// <list type="number">
        /// <item><b>透明度上限 ~47%</b>：叠加层只是"提示哪里不对"，不是"把图换成热图"。
        /// 旧实现 alpha 到 2 倍阈值就冲到 235/255（92%），缺陷区域是一块实心红，纹理全被盖掉。</item>
        /// <item><b>0.9 倍阈值以下完全不上色</b>：0.9 是"擦线"预警，1.0 才是判定线。
        /// 正常纹理本来就在 0.3~0.8 之间晃，给它上色只会把整块产品糊成一层蓝紫，
        /// 真正的缺陷反而看不出来。<b>色相同样按倍数铺开</b>：1.0 倍是冷色、
        /// 2.0 倍黄绿、3.0 倍及以上才是红 —— 红只属于"明确超阈值"的地方。</item>
        /// </list>
        /// </remarks>
        public static Bitmap AnomalyHeat(float[] rel, int mapW, int mapH,
                                         int frameW, int frameH, int offX, int offY)
        {
            if (rel == null || mapW <= 0 || mapH <= 0 || frameW <= 0 || frameH <= 0) return null;
            if (rel.Length < mapW * mapH) return null;

            // 着色窗口：0.9 倍（刚擦线，几乎不着色）→ 3.0 倍（满色）
            const float vStart = 0.90f;
            const float vSpan = 2.10f;
            const int alphaMax = 120;        // ≈47%：再高就盖住底图了

            var bmp = new Bitmap(frameW, frameH, PixelFormat.Format32bppArgb);
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, frameW, frameH),
                                           ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                var buf = new byte[data.Stride * frameH];

                for (int y = 0; y < mapH; y++)
                {
                    int fy = offY + y;
                    if (fy < 0 || fy >= frameH) continue;

                    int row = y * mapW;
                    int dstRow = fy * data.Stride;

                    for (int x = 0; x < mapW; x++)
                    {
                        int fx = offX + x;
                        if (fx < 0 || fx >= frameW) continue;

                        float v = rel[row + x];
                        if (v < vStart) continue;          // 正常波动不上色
                        if (v > 3f) v = 3f;

                        float t = (v - vStart) / vSpan;
                        if (t < 0f) t = 0f;
                        if (t > 1f) t = 1f;

                        // 透明度：24(9%) → 120(47%)，1.8 次幂让"刚擦线"的区域几乎看不出来
                        int alpha = 24 + (int)(Math.Pow(t, 1.8) * (alphaMax - 24));
                        if (alpha > alphaMax) alpha = alphaMax;

                        // 蓝 → 青 → 绿 → 黄 → 红（按 t 铺开，只有 t 接近 1 才是红）
                        int r, g, b;
                        if (t < 0.25f)      { float k = t / 0.25f;           r = 0;               g = (int)(200 * k);       b = 255; }
                        else if (t < 0.5f)  { float k = (t - 0.25f) / 0.25f; r = 0;               g = 200;                  b = (int)(255 * (1 - k)); }
                        else if (t < 0.75f) { float k = (t - 0.5f) / 0.25f;  r = (int)(255 * k);  g = 200;                  b = 0; }
                        else                { float k = (t - 0.75f) / 0.25f; r = 255;              g = (int)(200 * (1 - k)); b = 0; }

                        int off = dstRow + fx * 4;
                        buf[off + 0] = (byte)b;
                        buf[off + 1] = (byte)g;
                        buf[off + 2] = (byte)r;
                        buf[off + 3] = (byte)alpha;
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
        public static readonly int[,] CocoSkeleton =        {
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
