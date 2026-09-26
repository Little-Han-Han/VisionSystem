using System.Collections.Generic;
using System.Drawing;
using VisionSystem.Controls.Rendering;

namespace VisionSystem.Controls.Geometry
{
    /// <summary>
    /// 几何图形统一接口。所有坐标均以<strong>图像像素坐标</strong>为准，
    /// 绘制时由 <see cref="Viewport"/> 换算到屏幕坐标。
    /// </summary>
    public interface IGeometryShape
    {
        /// <summary>图形名称（可显示在列表或标签中）。</summary>
        string Name { get; set; }

        /// <summary>附加业务数据。</summary>
        object Tag { get; set; }

        /// <summary>线条颜色。</summary>
        Color StrokeColor { get; set; }

        /// <summary>线宽，单位为<strong>屏幕像素</strong>（不随缩放变粗）。</summary>
        float LineWidth { get; set; }

        /// <summary>是否可见。</summary>
        bool Visible { get; set; }

        /// <summary>是否被选中（选中后绘制控制点）。</summary>
        bool IsSelected { get; set; }

        /// <summary>是否填充内部（同时决定命中区域是「内部」还是「边缘」）。</summary>
        bool IsFilled { get; set; }

        /// <summary>填充色（半透明为宜）。</summary>
        Color FillColor { get; set; }

        /// <summary>是否允许鼠标选中/编辑。</summary>
        bool IsEditable { get; set; }

        /// <summary>图形在图像坐标系下的包围盒。</summary>
        RectangleF Bounds { get; }

        /// <summary>绘制图形。</summary>
        /// <param name="g">目标 <see cref="Graphics"/>（未做任何变换，需自行换算坐标）。</param>
        /// <param name="vp">当前视口。</param>
        /// <param name="options">渲染选项。</param>
        void Draw(Graphics g, Viewport vp, RenderOptions options);

        /// <summary>命中测试：判断图像坐标点是否落在图形的可交互区域内。</summary>
        /// <param name="imagePt">待测点（图像坐标）。</param>
        /// <param name="tolerance">容差（图像坐标单位，已由屏幕像素换算而来）。</param>
        bool HitTest(PointF imagePt, float tolerance);

        /// <summary>整体平移。</summary>
        void Translate(float dx, float dy);

        /// <summary>获取当前所有控制点。</summary>
        IList<ShapeHandle> GetHandles();

        /// <summary>拖拽某个控制点到新位置，图形据此更新自身几何。</summary>
        /// <param name="handleIndex">控制点序号，取自 <see cref="ShapeHandle.Index"/>。</param>
        /// <param name="imagePt">控制点的新位置（图像坐标）。</param>
        void SetHandlePosition(int handleIndex, PointF imagePt);

        /// <summary>深拷贝。</summary>
        IGeometryShape Clone();

        /// <summary>以易读形式输出几何参数（用于状态栏/日志）。</summary>
        string ToGeometryString();
    }
}
