using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace VisionPlatform.UI
{
    /// <summary>
    /// 界面图标：按名称取程序集内嵌的 PNG（源文件在 <c>assets\icons\ui\png\</c>）。
    /// <para>
    /// 【为什么嵌入而不是复制到输出目录】图标是编译期资源 —— 嵌进 exe/DLL 后单文件部署、
    /// 不会被误删，也不会像 assets 目录那样在 bin 下出现"第二份副本"。
    /// 代价是改图标要重新编译，对工业软件可接受（图标不是现场可配项）。
    /// </para>
    /// <para>
    /// 命名约定：<c>ui-&lt;分组&gt;-&lt;名称&gt;</c>（与 <c>docs/设计/界面图标清单.md</c> 一致），
    /// 每个图标有 16 / 24 / 32 三档，按需求尺寸取最接近的一档（不做缩放，保证像素清晰）。
    /// </para>
    /// </summary>
    public static class UiIcons
    {
        private static readonly Dictionary<string, Image> _cache =
            new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);

        private static readonly object _gate = new object();

        /// <summary>
        /// 取图标；不存在时返回 null（界面按"无图标"降级，不抛异常）。
        /// </summary>
        /// <param name="name">图标名，如 <c>ui-run-start</c>。</param>
        /// <param name="size">期望尺寸：≤16→16、≤24→24、其余→32。</param>
        public static Image Get(string name, int size = 16)
        {
            if (string.IsNullOrEmpty(name)) return null;

            int actual = size <= 16 ? 16 : (size <= 24 ? 24 : 32);
            string key = name + "-" + actual;

            lock (_gate)
            {
                Image hit;
                if (_cache.TryGetValue(key, out hit)) return hit;
            }

            string resource = "uiicons." + name + "-" + actual + ".png";
            try
            {
                Assembly asm = typeof(UiIcons).Assembly;
                using (Stream stream = asm.GetManifestResourceStream(resource))
                {
                    if (stream == null) return null;

                    // Image.FromStream 要求流在图像生命周期内有效；
                    // 这里复制成独立 Bitmap 后可立即释放流（避免长期占用资源句柄）。
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        ms.Position = 0;
                        using (Image temp = Image.FromStream(ms))
                        {
                            var copy = new Bitmap(temp);
                            lock (_gate) _cache[key] = copy;
                            return copy;
                        }
                    }
                }
            }
            catch (Exception)
            {
                return null;   // 图标缺失不应该影响功能
            }
        }
    }
}
