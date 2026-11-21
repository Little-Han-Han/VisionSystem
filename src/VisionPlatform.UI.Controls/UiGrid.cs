using System;
using System.Drawing;
using System.Windows.Forms;

namespace VisionPlatform.UI
{
    /// <summary>
    /// 表格（<see cref="DataGridView"/>）主题化：统一表头、行、选中、网格线配色。
    /// 默认 WinForms 表格是浅色系统配色，放在深色工程界面里非常突兀，故全部重涂。
    /// 零第三方依赖，颜色取自 <see cref="UiTheme"/>。
    /// </summary>
    public static class UiGrid
    {
        /// <summary>套用深色工程风格表格外观。</summary>
        public static void Apply(DataGridView grid)
        {
            if (grid == null) return;

            grid.EnableHeadersVisualStyles = false;   // 关掉系统蓝，否则下面配色不生效
            grid.BorderStyle = BorderStyle.None;
            grid.BackgroundColor = UiTheme.ColorPanelBg;
            grid.GridColor = UiTheme.ColorDivider;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToResizeRows = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

            // 表头：深色底 + 次要色小标题
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.EnableResizing;
            grid.ColumnHeadersHeight = 34;
            grid.ColumnHeadersDefaultCellStyle.BackColor = UiTheme.ColorHeaderBg;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = UiTheme.ColorTextSecondary;
            grid.ColumnHeadersDefaultCellStyle.Font = UiTheme.FontGroupTitle;
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = UiTheme.ColorHeaderBg;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = UiTheme.ColorTextSecondary;
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 6, 0);

            // 数据行
            // 【必须用 ColorCanvasBg 而不是 ColorViewBg】后者在浅色模式恒为 #181820（图像区
            // 刻意恒深）—— 表格是文本控件，深底配浅色主题的深色文字会"一行白一行黑、字全黑
            // 看不见"。ColorCanvasBg 随主题（深 #181820 / 浅 #EDEEF1），配 ColorTextPrimary
            // 在两种主题下都是高对比。
            grid.RowTemplate.Height = 28;
            grid.DefaultCellStyle.BackColor = UiTheme.ColorCanvasBg;
            grid.DefaultCellStyle.ForeColor = UiTheme.ColorTextPrimary;
            grid.DefaultCellStyle.Font = UiTheme.FontBody;
            grid.DefaultCellStyle.SelectionBackColor = UiTheme.ColorAccent;
            grid.DefaultCellStyle.SelectionForeColor = Color.White;
            grid.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);

            // 隔行换色，长时间读数不易串行。
            // 交替行用 CardBg（深 #34343E / 浅 #F7F7FA），与默认行 CanvasBg
            //（深 #181820 / 浅 #EDEEF1）在两种主题下都保持"同色系、可区分"。
            grid.AlternatingRowsDefaultCellStyle.BackColor = UiTheme.ColorCardBg;
            grid.AlternatingRowsDefaultCellStyle.ForeColor = UiTheme.ColorTextPrimary;
            grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = UiTheme.ColorAccent;
            grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = Color.White;

            // 网格线 / 空白区：随主题，否则浅色下会残留深色网格把表格切成一块块
            grid.GridColor = UiTheme.ColorDivider;
            grid.BackgroundColor = UiTheme.ColorPanelBg;
        }
    }
}
