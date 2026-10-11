namespace VisionPlatform.UI
{
    partial class NodeParamPanel
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();

            this.lblTitle = new System.Windows.Forms.Label();
            this.body = new System.Windows.Forms.Panel();
            this.empty = new System.Windows.Forms.Label();
            this.toolTip = new System.Windows.Forms.ToolTip(this.components);

            this.SuspendLayout();

            // lblTitle：节点名 / 模块名（分区标题样式）
            this.lblTitle.Dock = System.Windows.Forms.DockStyle.Top;
            this.lblTitle.Height = 26;
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblTitle.Text = "节点参数";

            // body：按参数表动态生成的滚动区
            this.body.Dock = System.Windows.Forms.DockStyle.Fill;
            this.body.AutoScroll = true;
            this.body.Name = "body";

            // empty：未选中节点 / 该节点无参数时的空态
            this.empty.Dock = System.Windows.Forms.DockStyle.Fill;
            this.empty.Name = "empty";
            this.empty.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.empty.Text = "请选择节点";

            this.Controls.Add(this.body);
            this.Controls.Add(this.empty);
            this.Controls.Add(this.lblTitle);

            this.Name = "NodeParamPanel";
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.Panel body;
        private System.Windows.Forms.Label empty;
        private System.Windows.Forms.ToolTip toolTip;
    }
}
