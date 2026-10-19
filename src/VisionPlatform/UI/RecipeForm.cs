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
        public RecipeForm()
        {
            InitializeComponent();

            RefreshList();
            ShowCurrent();
            RecipeStore.CurrentChanged += (s, e) => ShowCurrent();

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
            Logger.Info("加载配方：" + name);
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
                return;
            }

            txtName.Text = r.Name;
            txtProduct.Text = r.Product;
            txtNote.Text = r.Note;
            txtCamera.Text = r.CameraId;

            lblInfo.Text = string.Format(
                "当前配方：{0}" + Environment.NewLine +
                "产品：{1}" + Environment.NewLine +
                "相机：{2}" + Environment.NewLine +
                "修改时间：{3:yyyy-MM-dd HH:mm:ss}",
                r.Name, r.Product, r.CameraId, r.Modified);
        }
    }
}
