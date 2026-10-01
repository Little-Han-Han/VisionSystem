using System;
using System.Collections.Generic;
using System.Drawing;
using VisionPlatform.Native;
using VisionSystem.Controls.Geometry;
using VisionSystem.Controls.Rendering;

namespace VisionPlatform.UI
{
    /// <summary>
    /// 模板特征图层：把形状模型的全部边缘点（含梯度方向）作为<strong>一个整体图形</strong>绘制，
    /// 相当于叠加在图像上的独立图层，用于查看「模板特征结构」。
    /// 不参与选中与编辑，避免和 ROI / 结果框的拖拽交互冲突。
    /// </summary>
    public sealed class ModelFeatureLayer : IGeometryShape
    {
        /// <summary>单个图层最多绘制的特征点数，避免超大模板拖慢重绘。</summary>
        private const int MaxDraw = 6000;

        private readonly List<ShapeModelPoint> _points;
        private PointF _center;
        private readonly RectangleF _bounds;

        /// <param name="points">特征点（坐标相对模板中心）。</param>
        /// <param name="center">模板中心在图像坐标系中的位置。</param>
        public ModelFeatureLayer(IEnumerable<ShapeModelPoint> points, PointF center)
        {
            _points = (points != null)
                ? new List<ShapeModelPoint>(points)
                : new List<ShapeModelPoint>();

            _center = center;

            Name = "模板特征";
            StrokeColor = Color.Orange;
            LineWidth = 1f;
            Visible = true;
            IsEditable = false;   // 只读图层：不进选中、不出控制点
            IsFilled = false;
            IsSelected = false;
            Tag = "model-features";

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            foreach (ShapeModelPoint p in _points)
            {
                float x = (float)p.X;
                float y = (float)p.Y;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }

            if (_points.Count == 0)
                _bounds = new RectangleF(center.X, center.Y, 0f, 0f);
            else
                _bounds = RectangleF.FromLTRB(center.X + minX, center.Y + minY,
                                              center.X + maxX, center.Y + maxY);
        }

        /// <summary>图层包含的特征点数。</summary>
        public int Count { get { return _points.Count; } }

        public string Name { get; set; }
        public object Tag { get; set; }
        public Color StrokeColor { get; set; }
        public float LineWidth { get; set; }
        public bool Visible { get; set; }
        public bool IsSelected { get; set; }
        public bool IsFilled { get; set; }
        public Color FillColor { get; set; }
        public bool IsEditable { get; set; }

        public RectangleF Bounds { get { return _bounds; } }

        public void Draw(Graphics g, Viewport vp, RenderOptions options)
        {
            if (!Visible || g == null || vp == null || _points.Count == 0) return;

            float zoom = vp.Zoom;
            if (zoom < 0.0001f) zoom = 0.0001f;

            // 梯度方向短线按屏幕像素固定长度，缩放时不随图像放大
            const float tickScreen = 5f;
            float tickImg = tickScreen / zoom;

            int n = Math.Min(_points.Count, MaxDraw);

            using (Pen pen = new Pen(StrokeColor, Math.Max(LineWidth, 0.5f)))
            using (Brush brush = new SolidBrush(StrokeColor))
            {
                for (int i = 0; i < n; i++)
                {
                    ShapeModelPoint p = _points[i];

                    float ix = _center.X + (float)p.X;
                    float iy = _center.Y + (float)p.Y;

                    PointF p0 = vp.ImageToControl(ix, iy);
                    PointF p1 = vp.ImageToControl(ix + (float)p.Dx * tickImg,
                                                  iy + (float)p.Dy * tickImg);

                    // 梯度方向短线
                    g.DrawLine(pen, p0, p1);

                    // 特征点位置（小方块，比圆点更快）
                    g.FillRectangle(brush, p0.X - 0.75f, p0.Y - 0.75f, 1.5f, 1.5f);
                }
            }
        }

        // 只读图层：不参与交互
        public bool HitTest(PointF imagePt, float tolerance) { return false; }

        public void Translate(float dx, float dy)
        {
            _center.X += dx;
            _center.Y += dy;
        }

        public IList<ShapeHandle> GetHandles()
        {
            return new List<ShapeHandle>(0);
        }

        public void SetHandlePosition(int handleIndex, PointF imagePt)
        {
            // 只读图层，忽略
        }

        public IGeometryShape Clone()
        {
            return new ModelFeatureLayer(_points, _center);
        }

        public string ToGeometryString()
        {
            return string.Format("模板特征 {0} 点，中心 ({1:F1},{2:F1})",
                _points.Count, _center.X, _center.Y);
        }
    }
}
