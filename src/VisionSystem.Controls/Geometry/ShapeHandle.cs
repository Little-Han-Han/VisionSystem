using System.Drawing;

namespace VisionSystem.Controls.Geometry
{
    /// <summary>控制点（手柄）的语义类型。</summary>
    public enum HandleKind
    {
        /// <summary>改变图形尺寸或半径。</summary>
        Resize,

        /// <summary>绕中心旋转。</summary>
        Rotate,

        /// <summary>整体平移（部分图形把中心点作为移动手柄）。</summary>
        Move
    }

    /// <summary>
    /// 图形上的可拖拽控制点。位置使用<strong>图像坐标</strong>，
    /// 由控件在绘制时换算到屏幕坐标。
    /// </summary>
    public sealed class ShapeHandle
    {
        /// <summary>控制点在图像坐标系中的位置。</summary>
        public PointF Position { get; set; }

        /// <summary>控制点语义。</summary>
        public HandleKind Kind { get; set; }

        /// <summary>在同一图形内的序号，用于 <see cref="IGeometryShape.SetHandlePosition"/>。</summary>
        public int Index { get; set; }

        /// <summary>附加数据（例如标识控制点位于哪条边）。</summary>
        public object Tag { get; set; }

        /// <summary>创建一个控制点。</summary>
        public ShapeHandle(PointF position, HandleKind kind, int index, object tag = null)
        {
            Position = position;
            Kind = kind;
            Index = index;
            Tag = tag;
        }
    }
}
