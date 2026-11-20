using System;

namespace VisionSystem.Controls.PointCloud
{
    /// <summary>
    /// 点云数据（XYZ 顶点 + 可选 RGB 颜色）。
    /// <para>
    /// 【所有权约定】数组是托管内存，谁最后持有引用谁负责丢弃 —— 不像 <see cref="System.Drawing.Bitmap"/>
    /// 需要显式 Dispose；管线里按「帧所有权移交」的方式只做引用移交，无释放钩子。
    /// </para>
    /// </summary>
    public sealed class PointCloudData
    {
        private float[] _xyz;   // 3 * Count，x0 y0 z0 x1 y1 z1 ...
        private byte[] _rgb;    // 3 * Count 或 null（null = 无颜色，显示时按高度伪彩）

        /// <summary>创建点云。xyz 长度必须是 count*3；rgb 为 null 或 count*3。</summary>
        public PointCloudData(float[] xyz, int count, byte[] rgb)
        {
            if (xyz == null) throw new ArgumentNullException("xyz");
            if (xyz.Length < count * 3) throw new ArgumentException("xyz 数组长度小于 count*3", "xyz");
            if (rgb != null && rgb.Length < count * 3) throw new ArgumentException("rgb 数组长度小于 count*3", "rgb");

            _xyz = xyz;
            _rgb = rgb;
            Count = count;
            RecomputeBounds();
        }

        /// <summary>点数。</summary>
        public int Count { get; private set; }

        /// <summary>顶点数组（3 * Count：x,y,z 交错）。只读使用，勿改内容。</summary>
        public float[] Xyz { get { return _xyz; } }

        /// <summary>颜色数组（3 * Count，RGB 0~255；无颜色时为 null）。</summary>
        public byte[] Rgb { get { return _rgb; } }

        /// <summary>有颜色数据。</summary>
        public bool HasColor { get { return _rgb != null; } }

        // ---------------------- 包围盒 ----------------------

        public float XMin { get; private set; }
        public float XMax { get; private set; }
        public float YMin { get; private set; }
        public float YMax { get; private set; }
        public float ZMin { get; private set; }
        public float ZMax { get; private set; }

        /// <summary>对角线长度（视角自适应用）；空云返回 0。</summary>
        public float Diagonal
        {
            get
            {
                float dx = XMax - XMin, dy = YMax - YMin, dz = ZMax - ZMin;
                return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            }
        }

        /// <summary>包围盒中心（X/Y/Z 三个值）。</summary>
        public void GetCenter(out float cx, out float cy, out float cz)
        {
            cx = (XMin + XMax) * 0.5f;
            cy = (YMin + YMax) * 0.5f;
            cz = (ZMin + ZMax) * 0.5f;
        }

        /// <summary>扫描顶点重算包围盒（构造时已调过，数据被外部修改后才需要再调）。</summary>
        public void RecomputeBounds()
        {
            if (Count <= 0)
            {
                XMin = XMax = YMin = YMax = ZMin = ZMax = 0f;
                return;
            }

            float xmin = float.MaxValue, xmax = float.MinValue;
            float ymin = float.MaxValue, ymax = float.MinValue;
            float zmin = float.MaxValue, zmax = float.MinValue;

            float[] a = _xyz;
            int n = Count * 3;
            for (int i = 0; i < n; i += 3)
            {
                float x = a[i], y = a[i + 1], z = a[i + 2];
                if (x < xmin) xmin = x;
                if (x > xmax) xmax = x;
                if (y < ymin) ymin = y;
                if (y > ymax) ymax = y;
                if (z < zmin) zmin = z;
                if (z > zmax) zmax = z;
            }

            // 现场偶尔混入 NaN（传感器坏点）：把它们挡在包围盒外，避免视野算成空
            if (xmin > xmax || ymin > ymax || zmin > zmax)
            {
                XMin = XMax = YMin = YMax = ZMin = ZMax = 0f;
                return;
            }

            XMin = xmin; XMax = xmax;
            YMin = ymin; YMax = ymax;
            ZMin = zmin; ZMax = zmax;
        }

        /// <summary>深拷贝（测试 / 复制场景用）。</summary>
        public PointCloudData Clone()
        {
            float[] xyz = new float[Count * 3];
            Array.Copy(_xyz, xyz, xyz.Length);
            byte[] rgb = null;
            if (_rgb != null)
            {
                rgb = new byte[Count * 3];
                Array.Copy(_rgb, rgb, rgb.Length);
            }
            return new PointCloudData(xyz, Count, rgb);
        }
    }
}
