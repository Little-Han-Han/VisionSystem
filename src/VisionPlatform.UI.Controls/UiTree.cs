using System;
using System.Drawing;
using System.Windows.Forms;

namespace VisionPlatform.UI
{
    /// <summary>
    /// 树（<see cref="TreeView"/>）主题化：把默认的系统浅色树换成 <see cref="UiTheme"/> 的深色工程配色。
    /// 写法仿照既有的 <see cref="UiGrid"/>（静态工具类 + 一个 <c>Apply</c> 入口）。
    /// <para>
    /// 【为什么需要它】对齐参考图（Cognex VisionPro QuickBuild）后会出现<strong>两棵</strong>树：
    /// ① 左侧工具箱（分类 = 根节点，模块 = 叶子，见 §11.2）；② 左侧「输入 / 输出」端子面板（§11.3）。
    /// 而 <see cref="UiStyle"/> 原先没有 <c>TreeView</c> 分支，不补的话这两棵树会是系统浅色，
    /// 在深色工程界面里非常刺眼。
    /// </para>
    /// <para>
    /// 【绘制策略】用 <see cref="TreeViewDrawMode.OwnerDrawText"/>，<strong>不用</strong>
    /// <c>OwnerDrawAll</c>：<c>+/-</c> 折叠按钮与层级连线仍由系统（comctl32）绘制，
    /// 我们只接管「背景 + 文本」，改动最小、风险最低。<c>OwnerDrawAll</c> 要自己画折叠按钮，本轮不选。
    /// </para>
    /// <para>
    /// ⚠️【已知限制 —— 不是 bug，别当 bug 报】<c>TreeView</c> 的<strong>滚动条仍是系统浅色</strong>：
    /// WinForms 不提供原生深色滚动条，<c>TreeView</c> 的绘制由 comctl32 托管，
    /// 在「零第三方依赖」约束下无法改（本项目已在用的 <see cref="ListBox"/> 属同一类固有限制）。
    /// 真要解决只能整控件自绘（滚动条也自己画），本轮<strong>不做</strong>（见 §11.7 N16）。
    /// </para>
    /// <para>
    /// 【逐行上色】<see cref="OnDrawNode"/> 优先读 <c>e.Node.ForeColor</c>，
    /// 因此调用方直接 <c>node.ForeColor = ...</c> 即可逐行上色，<strong>不需要</strong> TreeNode 子类
    /// （端子面板按端子 Kind 上色就靠这条）。
    /// </para>
    /// </summary>
    public static class UiTree
    {
        /// <summary>套用深色工程风格树外观。可重复调用（幂等）。</summary>
        public static void Apply(TreeView tree)
        {
            Apply(tree, null);
        }

        /// <summary>
        /// 带字体覆盖的 Apply：<paramref name="font"/> 非 null 时用它，否则默认 10pt。
        /// <para>脚本编辑器变量树只有 ~400px 宽，10pt 下「显示名（类型）= 值」会截尾，显式传 8.5pt。</para>
        /// </summary>
        public static void Apply(TreeView tree, Font font)
        {
            if (tree == null) return;

            tree.BackColor = UiTheme.ColorTreeBg;        // 随主题：深色 #181820 / 浅色 #FFF（不能用恒深的图像区底色）
            tree.ForeColor = UiTheme.ColorTextPrimary;   // #DCE0E8
            tree.LineColor = UiTheme.ColorDivider;       // #46464E，层级连线
            tree.BorderStyle = BorderStyle.None;         // 边框交给外层容器，避免双线
            // 字体比正文大一号（10pt，正文 FontBody 为 9pt）：解决树节点文字偏小、显得拥挤的问题。
            // 显式写死字体名 + 字号，保证工具箱 / 端子两棵树观感一致。
            tree.Font = font ?? new Font(UiTheme.FamilyBody, 10f);

            // 交互：整行选中（点空白处也能选中）、失焦后仍保持高亮
            tree.FullRowSelect = true;
            tree.HideSelection = false;
            tree.HotTracking = false;                    // 系统热跟踪是浅色下划线，关掉

            // 层级观感：显示根连线 / 连线 / +/- 按钮
            tree.ShowRootLines = true;
            tree.ShowLines = true;
            tree.ShowPlusMinus = true;
            tree.Indent = 16;

            // ★ OwnerDrawText：只接管文本与背景，+/- 与连线仍由系统画
            tree.DrawMode = TreeViewDrawMode.OwnerDrawText;
            // ★ ItemHeight 只在 owner-draw 模式下生效，必须与 DrawMode 同时设置；
            //   固定像素值不随 DPI 缩放，而 10pt 字体的高会随 DPI 变大 —— 125%/150% 缩放下
            //   文字比行高 -> 节点垂直被裁（「节点显示不全」）。改用 Font.Height（屏幕像素，
            //   自适应 DPI）+ 8 padding，任何缩放比下行高都包得住文字。
            tree.ItemHeight = tree.Font.Height + 8;

            // 幂等：先减后加，重复 Apply 不会叠加事件处理器
            tree.DrawNode -= OnDrawNode;
            tree.DrawNode += OnDrawNode;
        }

        /// <summary>
        /// 自绘节点：背景（选中 / 普通）+ 文本（优先节点自己的 ForeColor）。
        /// </summary>
        private static void OnDrawNode(object sender, DrawTreeNodeEventArgs e)
        {
            if (e.Node == null) return;

            TreeView tree = sender as TreeView;
            Color back = (tree != null) ? tree.BackColor : UiTheme.ColorTreeBg;

            bool selected = (e.Node.IsSelected);
            bool focused = (tree != null) && tree.Focused;

            // 选中态：有焦点用强调色；失焦（HideSelection=false 仍要能看到）用次一级的卡片色
            if (selected)
                back = focused ? UiTheme.ColorAccent : UiTheme.ColorCardBg;

            using (var brush = new SolidBrush(back))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }

            // 文本色：优先节点自身的 ForeColor（未显式设置时为 Color.Empty），
            // 这样「逐行上色」只需 node.ForeColor = ...，无需 TreeNode 子类。
            Color fore = e.Node.ForeColor;
            if (fore.IsEmpty) fore = (tree != null) ? tree.ForeColor : UiTheme.ColorTextPrimary;
            if (selected && focused) fore = UiTheme.ColorTextOnFill;   // 强调色底上用白字

            if (string.IsNullOrEmpty(e.Node.Text)) return;

            // 【为什么不用 e.Bounds.Width】OwnerDrawText 模式下 e.Bounds 是 comctl 按
            // "插入节点时的字体与控件宽度"缓存的文本矩形 —— 建树通常发生在 Apply/布局之前，
            // 之后字体变大或控件加宽，旧矩形比实际文本窄，DrawText + EndEllipsis 会把
            // 每一行都截出 "..."（脚本编辑器变量树实测：372px 客户区里 149px 的行也被截）。
            // 这里改画到"从文本起点到客户区右缘"，只有真的放不下才出省略号。
            TreeView tv = sender as TreeView;
            int textLeft = e.Bounds.Left + 2;

            // 节点图标：约定 <see cref="TreeNode.ImageKey"/> 存 <c>ui-xxx</c> 图标名
            // （不设 ImageList —— 那是系统绘制路径，与 OwnerDrawText 混用会双倍缩进）。
            // 工具箱的分类节点就靠这个显示小图标。
            if (!string.IsNullOrEmpty(e.Node.ImageKey))
            {
                Image icon = UiIcons.Get(e.Node.ImageKey, 16);
                if (icon != null)
                {
                    int iconY = e.Bounds.Top + (e.Bounds.Height - icon.Height) / 2;
                    e.Graphics.DrawImage(icon, new Rectangle(textLeft, iconY, icon.Width, icon.Height));
                    textLeft += icon.Width + 4;
                }
            }
            int availWidth = (tv != null)
                ? tv.ClientSize.Width - textLeft - 2
                : Math.Max(0, e.Bounds.Width - 4);

            Rectangle textRect = new Rectangle(
                textLeft,
                e.Bounds.Top,
                Math.Max(0, availWidth),
                e.Bounds.Height);

            TextRenderer.DrawText(e.Graphics, e.Node.Text, e.Node.NodeFont ?? ((tree != null) ? tree.Font : UiTheme.FontBody),
                textRect, fore,
                TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }
    }
}
