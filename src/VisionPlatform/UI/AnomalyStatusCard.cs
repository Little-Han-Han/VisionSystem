using System;
using System.Drawing;
using System.Windows.Forms;
using VisionPlatform.Framework.Modules;

namespace VisionPlatform.UI
{
    /// <summary>
    /// 参数面板顶部的「异常检测模型」状态卡片。
    /// <para>
    /// 与 <see cref="OcrTemplateCard"/> 同一套视觉常量与交互约定，但数据源是
    /// <see cref="AnomalyModelCatalog"/>（固定目录扫描），因此单独一个类：
    /// 硬合并会变成"两套状态机的并集"，改一处必然影响另一处。
    /// </para>
    /// <para>
    /// 卡片只报"现在挂的是哪个模型、它是什么状态"，具体训练/标注在
    /// <see cref="AnomalyTrainForm"/> 里做 —— 参数区一行高 100px 放不下样本列表。
    /// </para>
    /// </summary>
    public sealed class AnomalyStatusCard : Panel
    {
        /// <summary>卡片高度。</summary>
        public const int CardHeight = 96;

        private const int TextLeft = 16;
        private const int ButtonTop = 62;
        private const int ButtonH = 26;
        private const int BtnMainW = 112;
        private const int BtnSmallW = 64;
        private const int BtnGap = 6;

        private readonly Label _lblState;
        private readonly Label _lblDetail;
        private readonly Label _lblPath;
        private readonly UiButton _btnManage;
        private readonly UiButton _btnReload;
        private readonly UiButton _btnOpen;

        private readonly ToolTip _tip;

        /// <summary>点「样本 / 训练…」。</summary>
        public event EventHandler ManageRequested;

        /// <summary>点「刷新」。</summary>
        public event EventHandler RefreshRequested;

        /// <summary>点「打开目录」。</summary>
        public event EventHandler OpenFolderRequested;

        public AnomalyStatusCard()
        {
            Dock = DockStyle.Top;
            Height = CardHeight;
            MinimumSize = new Size(120, CardHeight);
            Padding = new Padding(0);

            _lblState = MakeLabel(UiTheme.FontBody, true);
            _lblState.Location = new Point(TextLeft, 6);

            _lblDetail = MakeLabel(UiTheme.FontSmall, false);
            _lblDetail.Location = new Point(TextLeft, 26);

            _lblPath = MakeLabel(UiTheme.FontSmall, false);
            _lblPath.Location = new Point(TextLeft, 44);

            _btnManage = MakeButton("样本 / 训练…", UiButtonKind.Grab, BtnMainW);
            _btnManage.Click += delegate { Raise(ManageRequested); };

            _btnReload = MakeButton("刷新", UiButtonKind.Neutral, BtnSmallW);
            _btnReload.Click += delegate { Raise(RefreshRequested); };

            _btnOpen = MakeButton("打开目录", UiButtonKind.Neutral, BtnSmallW + 16);
            _btnOpen.Click += delegate { Raise(OpenFolderRequested); };

            Controls.AddRange(new Control[] { _lblState, _lblDetail, _lblPath, _btnManage, _btnReload, _btnOpen });

            _tip = new ToolTip();
            _tip.SetToolTip(_btnManage, "加载多张 OK / NG 图片，逐张标注后训练 PaDiM 模型，或验证现有模型");
            _tip.SetToolTip(_btnReload, "重新扫描 Models\\Anomaly 目录");
            _tip.SetToolTip(_btnOpen, "在资源管理器中打开模型目录");

            UiTheme.Changed += OnThemeChanged;
            ApplyTheme();
            SetState("未选择模型", UiTheme.ColorTextSecondary, "—", "—");
        }

        private static Label MakeLabel(Font font, bool bold)
        {
            var l = new Label();
            l.AutoSize = false;
            l.Height = bold ? 18 : 16;
            l.Font = bold ? new Font(font, FontStyle.Bold) : font;
            l.ForeColor = UiTheme.ColorTextSecondary;
            l.BackColor = Color.Transparent;
            l.Text = "";
            l.AutoEllipsis = true;
            return l;
        }

        private static UiButton MakeButton(string text, UiButtonKind kind, int width)
        {
            var b = new UiButton();
            b.Text = text;
            b.Kind = kind;
            b.Width = width;
            b.Height = ButtonH;
            return b;
        }

        private static void Raise(EventHandler h)
        {
            if (h != null) h(null, EventArgs.Empty);
        }

        /// <summary>
        /// 按参数里的模型名刷新卡片。modelName 为空 / 找不到模型时显示"未选择"。
        /// </summary>
        public void Bind(string modelName)
        {
            string name = (modelName ?? "").Trim();

            if (name.Length == 0)
            {
                var all = AnomalyModelCatalog.List();
                if (all.Count == 0)
                {
                    SetState("还没有模型", UiTheme.ColorWarn,
                             "点「样本 / 训练…」加载 OK 样本建一个（PaDiM 只需 OK 样本）",
                             AnomalyModelCatalog.RootDir);
                    return;
                }

                SetState("未指定（运行期用第一个）", UiTheme.ColorWarn,
                         "目录里有 " + all.Count + " 个模型：" + all[0].Name + " …",
                         AnomalyModelCatalog.RootDir);
                return;
            }

            AnomalyModelInfo info = AnomalyModelCatalog.Describe(name);
            if (info == null)
            {
                SetState("模型不存在", UiTheme.ColorNg, "目录里找不到 " + name + "\\model.json",
                         AnomalyModelCatalog.Resolve(name));
                return;
            }

            SetState(info.KindText + " · " + info.Name,
                     info.Kind == AnomalyModelCatalog.KindPaDiM ? UiTheme.ColorOk : UiTheme.ColorAccent,
                     info.Summary, info.Dir);
        }

        private void SetState(string state, Color color, string detail, string path)
        {
            _lblState.Text = state ?? "";
            _lblState.ForeColor = color;
            _lblDetail.Text = detail ?? "";
            _lblPath.Text = path ?? "";
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            ApplyTheme();
        }

        private void ApplyTheme()
        {
            BackColor = UiTheme.ColorCardBg;
            ForeColor = UiTheme.ColorTextPrimary;
            _lblDetail.ForeColor = UiTheme.ColorTextSecondary;
            _lblPath.ForeColor = UiTheme.ColorTextSecondary;
            ApplyLayout();
        }

        private void ApplyLayout()
        {
            // 构造期设置 Dock / Height 会先触发一次 OnResize，那时子控件还没 new 出来
            // （字段是 readonly，构造顺序在 base 之后）→ 必须先判空，否则构造直接抛 NRE。
            if (_lblState == null || _lblDetail == null || _lblPath == null) return;
            if (_btnManage == null || _btnReload == null || _btnOpen == null) return;

            // 构造期可能还没算好宽度：宽度为 0 时按设计宽占位，避免整行文字被摆到 (0,0)
            int w = Width > 0 ? Width : MinimumSize.Width;

            _lblState.Width = Math.Max(60, w - TextLeft - 12);
            _lblDetail.Width = Math.Max(60, w - TextLeft - 12);
            _lblPath.Width = Math.Max(60, w - TextLeft - 12);

            _btnManage.Location = new Point(TextLeft, ButtonTop);
            _btnReload.Location = new Point(_btnManage.Right + BtnGap, ButtonTop);
            _btnOpen.Location = new Point(_btnReload.Right + BtnGap, ButtonTop);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyLayout();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                UiTheme.Changed -= OnThemeChanged;
                if (_tip != null) _tip.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
