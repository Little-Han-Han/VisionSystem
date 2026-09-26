using System;
using System.Drawing;

namespace VisionSystem.Controls.Rendering
{
    /// <summary>
    /// 视口：负责「图像像素坐标」与「控件屏幕坐标」之间的双向映射，
    /// 并维护缩放（Zoom）与平移（Offset）状态。
    /// </summary>
    /// <remarks>
    /// 变换公式（单向）：
    /// <c>control = image * Zoom + Offset</c>
    /// <c>image   = (control - Offset) / Zoom</c>
    /// </remarks>
    public sealed class Viewport
    {
        /// <summary>允许的最小缩放倍率。</summary>
        public const float MinZoom = 0.01f;

        /// <summary>允许的最大缩放倍率。</summary>
        public const float MaxZoom = 256f;

        private float _zoom = 1f;
        private float _offsetX;
        private float _offsetY;
        private Size _imageSize = Size.Empty;

        /// <summary>创建默认视口（1:1，未平移）。</summary>
        public Viewport()
        {
        }

        /// <summary>当前显示的图像尺寸（像素）。</summary>
        public Size ImageSize
        {
            get { return _imageSize; }
            set { _imageSize = value; }
        }

        /// <summary>缩放倍率（1 = 图像像素与屏幕像素 1:1）。</summary>
        public float Zoom
        {
            get { return _zoom; }
            set { _zoom = Clamp(value, MinZoom, MaxZoom); }
        }

        /// <summary>水平平移量（控件坐标，像素）。</summary>
        public float OffsetX
        {
            get { return _offsetX; }
            set { _offsetX = value; }
        }

        /// <summary>垂直平移量（控件坐标，像素）。</summary>
        public float OffsetY
        {
            get { return _offsetY; }
            set { _offsetY = value; }
        }

        /// <summary>以 <see cref="PointF"/> 形式获取或设置平移量。</summary>
        public PointF Offset
        {
            get { return new PointF(_offsetX, _offsetY); }
            set { _offsetX = value.X; _offsetY = value.Y; }
        }

        /// <summary>图像左上角在控件中的位置（控件坐标）。</summary>
        public PointF ImageOrigin
        {
            get { return new PointF(_offsetX, _offsetY); }
        }

        /// <summary>图像坐标 → 控件坐标。</summary>
        public PointF ImageToControl(float imageX, float imageY)
        {
            return new PointF(imageX * _zoom + _offsetX, imageY * _zoom + _offsetY);
        }

        /// <summary>图像坐标 → 控件坐标。</summary>
        public PointF ImageToControl(PointF p)
        {
            return ImageToControl(p.X, p.Y);
        }

        /// <summary>控件坐标 → 图像坐标。</summary>
        public PointF ControlToImage(float controlX, float controlY)
        {
            return new PointF((controlX - _offsetX) / _zoom, (controlY - _offsetY) / _zoom);
        }

        /// <summary>控件坐标 → 图像坐标。</summary>
        public PointF ControlToImage(PointF p)
        {
            return ControlToImage(p.X, p.Y);
        }

        /// <summary>图像坐标矩形 → 控件坐标矩形。</summary>
        public RectangleF ImageToControl(RectangleF r)
        {
            PointF tl = ImageToControl(r.X, r.Y);
            return new RectangleF(tl.X, tl.Y, r.Width * _zoom, r.Height * _zoom);
        }

        /// <summary>控件坐标矩形 → 图像坐标矩形。</summary>
        public RectangleF ControlToImage(RectangleF r)
        {
            PointF tl = ControlToImage(r.X, r.Y);
            return new RectangleF(tl.X, tl.Y, r.Width / _zoom, r.Height / _zoom);
        }

        /// <summary>图像坐标下的长度 → 控件坐标下的长度。</summary>
        public float ScaleLength(float imageLength)
        {
            return imageLength * _zoom;
        }

        /// <summary>控件坐标下的长度 → 图像坐标下的长度（用于把屏幕容差换算成图像容差）。</summary>
        public float UnscaleLength(float controlLength)
        {
            return controlLength / _zoom;
        }

        /// <summary>图像整体在控件中所占的矩形（控件坐标）。</summary>
        public RectangleF GetImageRectInControl()
        {
            PointF tl = ImageToControl(0f, 0f);
            return new RectangleF(tl.X, tl.Y, _imageSize.Width * _zoom, _imageSize.Height * _zoom);
        }

        /// <summary>计算控件可视区域对应的图像坐标矩形（可能超出图像边界，调用方需自行裁剪）。</summary>
        public RectangleF GetVisibleImageRect(Size controlSize)
        {
            PointF tl = ControlToImage(0f, 0f);
            PointF br = ControlToImage(controlSize.Width, controlSize.Height);
            return RectangleF.FromLTRB(tl.X, tl.Y, br.X, br.Y);
        }

        /// <summary>
        /// 以控件中的某个锚点为不动点进行缩放——滚轮缩放的「指哪放大哪」效果。
        /// </summary>
        /// <param name="anchorControl">锚点（控件坐标），通常是鼠标位置。</param>
        /// <param name="factor">缩放因子，&gt;1 放大，&lt;1 缩小。</param>
        public void ZoomAt(PointF anchorControl, float factor)
        {
            if (factor <= 0f) return;
            float newZoom = Clamp(_zoom * factor, MinZoom, MaxZoom);
            if (Math.Abs(newZoom - _zoom) < 1e-7f) return;

            // 锚点在缩放前对应的图像坐标，缩放后必须仍落在同一屏幕位置。
            PointF anchorImage = ControlToImage(anchorControl);
            _zoom = newZoom;
            _offsetX = anchorControl.X - anchorImage.X * _zoom;
            _offsetY = anchorControl.Y - anchorImage.Y * _zoom;
        }

        /// <summary>把缩放设为指定值，并保持锚点不动。</summary>
        public void SetZoomAt(float zoom, PointF anchorControl)
        {
            float target = Clamp(zoom, MinZoom, MaxZoom);
            ZoomAt(anchorControl, target / _zoom);
        }

        /// <summary>把图像居中放置到控件中（不改变缩放）。</summary>
        public void CenterImage(Size controlSize)
        {
            _offsetX = (controlSize.Width - _imageSize.Width * _zoom) * 0.5f;
            _offsetY = (controlSize.Height - _imageSize.Height * _zoom) * 0.5f;
        }

        /// <summary>缩放至完整可见（适应窗口），并居中。</summary>
        /// <param name="controlSize">控件尺寸。</param>
        /// <param name="marginRatio">四周留白比例，0.98 表示留 2% 边距。</param>
        public void Fit(Size controlSize, float marginRatio = 0.98f)
        {
            if (_imageSize.Width <= 0 || _imageSize.Height <= 0) return;
            if (controlSize.Width <= 0 || controlSize.Height <= 0) return;

            float zx = controlSize.Width / (float)_imageSize.Width;
            float zy = controlSize.Height / (float)_imageSize.Height;
            _zoom = Clamp(Math.Min(zx, zy) * marginRatio, MinZoom, MaxZoom);
            CenterImage(controlSize);
        }

        /// <summary>恢复到 1:1 并以控件中心为锚点。</summary>
        public void ResetZoom(Size controlSize)
        {
            PointF center = new PointF(controlSize.Width * 0.5f, controlSize.Height * 0.5f);
            SetZoomAt(1f, center);
        }

        /// <summary>把图像坐标点滚动到控件中心。</summary>
        public void CenterOn(PointF imagePoint, Size controlSize)
        {
            _offsetX = controlSize.Width * 0.5f - imagePoint.X * _zoom;
            _offsetY = controlSize.Height * 0.5f - imagePoint.Y * _zoom;
        }

        /// <summary>重置为初始状态（1:1、无平移）。</summary>
        public void Reset()
        {
            _zoom = 1f;
            _offsetX = 0f;
            _offsetY = 0f;
        }

        private static float Clamp(float v, float lo, float hi)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return lo;
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }
    }
}
