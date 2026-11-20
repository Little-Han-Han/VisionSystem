using System;
using System.Collections.Generic;
using System.Windows.Forms;
using VisionPlatform.Core;
using VisionPlatform.Recipe;
using RecipeModel = VisionPlatform.Recipe.Recipe;

namespace VisionPlatform.UI
{
    /// <summary>配方管理：新建 / 加载 / 保存 / 删除，并编辑基本信息。</summary>
    public partial class RecipeForm : Form
    {
        private CheckedListBox clbFlows;

        public RecipeForm()
        {
            InitializeComponent();

            RefreshList();
            ShowCurrent();
            RecipeStore.CurrentChanged += (s, e) => ShowCurrent();

            BuildFlowBinding();
            UiStyle.ApplyForm(this);

            // 自绘深色标题栏：与顶部工具栏同色，系统原生标题栏不再出现
            UiTitleBar.Attach(this);

            // 语义色：保存=绿，加载=蓝，删除=红；其余中性
            btnSave.Kind = UiButtonKind.Start;
            btnLoad.Kind = UiButtonKind.Grab;
            btnDelete.Kind = UiButtonKind.Stop;
        }

        private void RefreshList()
        {
            listRecipes.BeginUpdate();
            listRecipes.Items.Clear();

            List<string> names = RecipeStore.List();
            foreach (string n in names) listRecipes.Items.Add(n);

            listRecipes.EndUpdate();
        }

        private void OnRefresh(object sender, EventArgs e) { RefreshList(); }

        private void OnSelectionChanged(object sender, EventArgs e)
        {
            string name = listRecipes.SelectedItem as string;
            if (!string.IsNullOrEmpty(name)) txtName.Text = name;
        }

        private void OnNew(object sender, EventArgs e)
        {
            string name = string.IsNullOrWhiteSpace(txtName.Text)
                ? ("Recipe-" + DateTime.Now.ToString("HHmmss"))
                : txtName.Text.Trim();

            RecipeModel r = RecipeModel.CreateDefault(name);
            r.Product = txtProduct.Text;
            r.Note = txtNote.Text;
            r.CameraId = string.IsNullOrWhiteSpace(txtCamera.Text) ? "SIM-0" : txtCamera.Text.Trim();

            RecipeStore.SetCurrent(r);
            RefreshList();
            Logger.Info("新建配方：" + name);
            MessageBox.Show(this, "已新建配方（尚未写入磁盘），点「保存」写入。");
        }

        private void OnLoad(object sender, EventArgs e)
        {
            string name = listRecipes.SelectedItem as string;
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show(this, "请先在左侧选中一个配方。");
                return;
            }

            var r = RecipeStore.Load(name);
            if (!r.Success) { MessageBox.Show(this, r.Message); return; }

            // 工程包：方案目录里若有 comm.json / flows.json，问一次是否把通讯与流程一起切过来。
            // 默认不静默覆盖 —— 有人只想换算法参数，不想动正在跑的流程。
            ProjectBundle.Contents bundle = ProjectBundle.Inspect(name);
            if (bundle.HasProject)
            {
                string q = "该方案包含工程配置：" + Environment.NewLine + bundle.ToString()
                    + Environment.NewLine + Environment.NewLine
                    + "是否一并应用到当前工程？" + Environment.NewLine
                    + "（会替换当前通讯与流程；导入前自动备份到方案目录的 " + ProjectBundle.BackupFolder + "）";

                if (MessageBox.Show(this, q, "应用工程配置",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    OpResult imp = ProjectBundle.Import(name);
                    MessageBox.Show(this, imp.Message);
                    if (!imp.Success) return;

                    // 流程列表变了，勾选区要按新配置重建
                    ShowCurrent();
                }
            }

            Logger.Info("加载配方：" + name);
        }

        /// <summary>「绑定流程」列表框的条目：显示流程名，回存流程 Id。</summary>
        private sealed class FlowItem
        {
            public readonly string Id;
            public readonly string Name;

            public FlowItem(string id, string name) { Id = id ?? ""; Name = name ?? ""; }

            public override string ToString()
            {
                return string.IsNullOrEmpty(Name) ? Id : Name + "  (" + Id.Substring(0, Math.Min(8, Id.Length)) + ")";
            }
        }

        /// <summary>
        /// 右侧底部：勾选该方案使用哪些流程。
        /// <para>
        /// 有了这层绑定，模板/字符库/模型才能按「方案 → 流程 → 节点」归档；
        /// 不勾选（空）时资产仍走全局目录，与升级前行为一致。
        /// </para>
        /// </summary>
        private void BuildFlowBinding()
        {
            var grp = new GroupBox
            {
                Text = "绑定流程（该方案用哪些流程）",
                Dock = DockStyle.Bottom,
                Height = 150,
                ForeColor = UiTheme.ColorTextSecondary,
                Padding = new Padding(8, 6, 8, 8)
            };

            clbFlows = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                CheckOnClick = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = UiTheme.ColorPanelBg,
                ForeColor = UiTheme.ColorTextPrimary,
                Font = UiTheme.FontSmall,
                IntegralHeight = false
            };

            grp.Controls.Add(clbFlows);
            panelRight.Controls.Add(grp);
        }

        /// <summary>把配置里的全部流程填进勾选列表，并按配方当前绑定勾上。</summary>
        private void ReloadFlowChoices(RecipeModel recipe)
        {
            if (clbFlows == null) return;

            clbFlows.ItemCheck -= OnFlowItemCheck;
            clbFlows.Items.Clear();

            List<VisionPlatform.Flow.FlowDefinition> flows = null;
            try { flows = ConfigStore.Current.Flows.Flows; }
            catch (Exception) { }

            if (flows != null)
            {
                for (int i = 0; i < flows.Count; i++)
                {
                    VisionPlatform.Flow.FlowDefinition f = flows[i];
                    if (f == null) continue;

                    bool bound = recipe != null && recipe.FlowIds != null
                                 && recipe.FlowIds.Contains(f.Id);
                    clbFlows.Items.Add(new FlowItem(f.Id, f.Name), bound);
                }
            }

            clbFlows.ItemCheck += OnFlowItemCheck;
        }

        private void OnFlowItemCheck(object sender, ItemCheckEventArgs e)
        {
            // 勾选状态变化本身不需要做什么：保存时统一读 CheckedItems
        }

        private void OnSave(object sender, EventArgs e)
        {
            RecipeModel r = RecipeStore.Current;
            if (r == null)
            {
                MessageBox.Show(this, "当前没有配方，请先新建或加载。");
                return;
            }

            r.Name = txtName.Text.Trim();
            r.Product = txtProduct.Text;
            r.Note = txtNote.Text;
            r.CameraId = txtCamera.Text;

            // 绑定流程：勾了哪些就存哪些 Id
            r.FlowIds = new List<string>();
            if (clbFlows != null)
            {
                for (int i = 0; i < clbFlows.CheckedItems.Count; i++)
                {
                    FlowItem item = clbFlows.CheckedItems[i] as FlowItem;
                    if (item != null && !string.IsNullOrEmpty(item.Id)) r.FlowIds.Add(item.Id);
                }
            }

            if (string.IsNullOrEmpty(r.Name))
            {
                MessageBox.Show(this, "配方名称不能为空。");
                return;
            }

            var res = RecipeStore.Save(r);
            MessageBox.Show(this, res.Message);
            RefreshList();
        }

        private void OnDelete(object sender, EventArgs e)
        {
            string name = listRecipes.SelectedItem as string;
            if (string.IsNullOrEmpty(name)) { MessageBox.Show(this, "请先选中要删除的配方。"); return; }

            if (MessageBox.Show(this, "确定删除配方「" + name + "」？", "确认",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            var r = RecipeStore.Delete(name);
            if (!r.Success) { MessageBox.Show(this, r.Message); return; }

            RefreshList();
            Logger.Info("删除配方：" + name);
        }

        private void ShowCurrent()
        {
            RecipeModel r = RecipeStore.Current;
            if (r == null)
            {
                lblInfo.Text = "当前配方：无";
                ReloadFlowChoices(null);
                return;
            }

            txtName.Text = r.Name;
            txtProduct.Text = r.Product;
            txtNote.Text = r.Note;
            txtCamera.Text = r.CameraId;

            ReloadFlowChoices(r);

            lblInfo.Text = string.Format(
                "当前配方：{0}" + Environment.NewLine +
                "产品：{1}" + Environment.NewLine +
                "相机：{2}" + Environment.NewLine +
                "修改时间：{3:yyyy-MM-dd HH:mm:ss}",
                r.Name, r.Product, r.CameraId, r.Modified);
        }
    }
}
