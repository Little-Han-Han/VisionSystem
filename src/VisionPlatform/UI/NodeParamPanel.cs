using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using VisionPlatform.Calibration;
using VisionPlatform.Communication;
using VisionPlatform.Core;
using VisionPlatform.Flow;

namespace VisionPlatform.UI
{
    /// <summary>
    /// 流程节点参数面板（宿主适配器）。
    /// <para>
    /// 【重构后职责】只做三件事：把 <see cref="FlowNode"/> 适配成
    /// <see cref="IParamValueStore"/>、把业务下拉（通讯 / 标定）适配成
    /// <see cref="IParamOptionProvider"/>、把面板事件转发给流程窗体。
    /// 参数界面本身已下沉到可复用控件 <see cref="ParamListControl"/>。
    /// </para>
    /// <para>
    /// 【为什么不再有 switch】参数表的差异由模块包声明，动态选项由 OptionsSource 声明，
    /// 运行期实时值由 LiveSource 声明 —— 本文件不认识任何具体节点类型。
    /// </para>
    /// </summary>
    public sealed class NodeParamPanel : UserControl, IParamValueStore, IParamOptionProvider
    {
        private readonly ParamListControl _list = new ParamListControl();
        private FlowNode _node;

        /// <summary>
        /// 运行时坐标来源：由流程窗体注入，参数面板据此把「坐标 X/Y」显示成上游真正传进来的值。
        /// <para>没有连线/没跑过时返回 null，输入框回落到参数里存的定点。</para>
        /// </summary>
        public static Func<FlowNode, PointF?> RuntimeCoordProvider;

        /// <summary>参数被用户修改时触发（FlowForm 据此保存配置）。</summary>
        public event EventHandler ParamsChanged;

        /// <summary>动作行被点击时触发，参数是字段 Key。</summary>
        public event Action<string> ActionInvoked;

        public NodeParamPanel()
        {
            _list.Dock = DockStyle.Fill;
            _list.OptionProvider = this;
            Controls.Add(_list);

            // 把通用控件的事件转成面板自己的事件：外部订阅 / 退订语义与重构前一致。
            _list.ParamsChanged += delegate(object s, EventArgs e)
            {
                EventHandler h = ParamsChanged;
                if (h != null) h(this, e);
            };
            _list.ActionInvoked += delegate(string key)
            {
                Action<string> h = ActionInvoked;
                if (h != null) h(key);
            };

            BackColor = UiTheme.ColorPanelBg;
            _list.ApplyTheme();
        }

        /// <summary>当前绑定的节点（未绑定时为 null）。</summary>
        public FlowNode Node { get { return _node; } }

        /// <summary>当前正在编辑的节点（与 <see cref="Node"/> 同义，保留旧调用点）。</summary>
        public FlowNode CurrentNode { get { return _node; } }

        /// <summary>绑定要编辑的节点；null / 未知类型 / 无参数表时显示空态。</summary>
        public void SetNode(FlowNode node)
        {
            _node = null;

            if (node == null)
            {
                _list.ShowEmpty("请选择节点");
                return;
            }

            FlowNodeType type;
            if (!node.TryGetType(out type))
            {
                _list.ShowEmpty("未知节点类型：" + node.Type);
                return;
            }

            NodeParamTable table = NodeParamSchema.For(type);
            if (table == null || table.Fields == null || table.Fields.Count == 0)
            {
                _list.ShowEmpty("该节点没有可配置参数");
                return;
            }

            _node = node;
            _list.SetModel(table, this, node.Name);
        }

        /// <summary>在参数区顶部插入横幅控件（所有权转移给面板）。</summary>
        /// <summary>外部改了节点参数后，把面板上的值重新读一遍（不重建控件）。</summary>
        public void ReloadValues()
        {
            if (_list != null) _list.ReloadValues();
        }

        public void SetBanner(Control banner)
        {
            _list.SetBanner(banner);
        }

        /// <summary>套用主题（切换深浅色时由 UiThemeManager 调用）。</summary>
        internal void ApplyTheme()
        {
            BackColor = UiTheme.ColorPanelBg;
            _list.ApplyTheme();
        }

        // =====================================================================
        //  IParamValueStore（FlowNode 适配）
        // =====================================================================

        bool IParamValueStore.Has(string key)
        {
            return _node != null && _node.FindParam(key) != null;
        }

        string IParamValueStore.Get(string key)
        {
            return _node != null ? _node.GetString(key) : "";
        }

        void IParamValueStore.SetInt(string key, int value) { if (_node != null) _node.Set(key, value); }
        void IParamValueStore.SetDouble(string key, double value) { if (_node != null) _node.Set(key, value); }
        void IParamValueStore.SetBool(string key, bool value) { if (_node != null) _node.Set(key, value); }
        void IParamValueStore.SetText(string key, string value) { if (_node != null) _node.Set(key, value); }

        bool IParamValueStore.TryGetLive(string source, out string value, out bool readOnly, out string tooltip)
        {
            value = "";
            readOnly = true;
            tooltip = "运行时取上游连线传入的值（不受参数里填的数影响）";

            if (_node == null || RuntimeCoordProvider == null) return false;
            if (!string.Equals(source, "coordX", StringComparison.Ordinal)
                && !string.Equals(source, "coordY", StringComparison.Ordinal)) return false;

            PointF? coord = RuntimeCoordProvider(_node);
            if (!coord.HasValue) return false;

            double v = string.Equals(source, "coordX", StringComparison.Ordinal) ? coord.Value.X : coord.Value.Y;
            value = v.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        // =====================================================================
        //  IParamOptionProvider（业务下拉适配）
        // =====================================================================

        List<NodeParamOption> IParamOptionProvider.GetOptions(NodeParamField field)
        {
            if (field == null) return null;

            string src = field.OptionsSource ?? "";
            if (src.Length == 0) return null;

            if (string.Equals(src, "comm", StringComparison.OrdinalIgnoreCase))
                return BuildCommOptions();

            if (src.StartsWith("calib:", StringComparison.OrdinalIgnoreCase))
                return BuildCalibOptions(src.Substring("calib:".Length));

            // 模块包自带的动态选项（如异常检测的模型列表）：宿主只按 Key 取，不认识具体业务。
            // 这条分支必须留在最后 —— comm / calib 是历史 Key，行为不能变。
            List<NodeParamOption> registered = Framework.Modules.ParamOptionSources.Build(src);
            if (registered != null) return registered;

            return null;
        }

        /// <summary>「绑定通讯」下拉：按 <see cref="CommRegistry.Names"/>() 本次刷新。</summary>
        private static List<NodeParamOption> BuildCommOptions()
        {
            var list = new List<NodeParamOption>();
            foreach (string n in CommRegistry.Names())
            {
                if (!string.IsNullOrEmpty(n)) list.Add(new NodeParamOption(n, n));
            }
            if (list.Count == 0)
                list.Add(new NodeParamOption("<未配置，请先到通讯界面添加>", ""));
            return list;
        }

        /// <summary>
        /// 标定记录下拉：按 <paramref name="kind"/>（NPoint / RotationCenter / Distortion）过滤。
        /// 第一项固定「自动（该类型第一条）」，用户不需要记 Id。
        /// </summary>
        private static List<NodeParamOption> BuildCalibOptions(string kind)
        {
            bool wantRotCenter = string.Equals(kind, "RotationCenter", StringComparison.OrdinalIgnoreCase);
            bool wantDistort = string.Equals(kind, "Distortion", StringComparison.OrdinalIgnoreCase);

            var list = new List<NodeParamOption>();
            list.Add(new NodeParamOption(wantRotCenter ? "不使用旋转中心" : "自动（用该类型第一条记录）", ""));

            int before = list.Count;

            try
            {
                CalibSettings all = ConfigStore.Current.Calibrations;
                if (all == null || all.Records == null) return list;

                for (int i = 0; i < all.Records.Count; i++)
                {
                    CalibRecord r = all.Records[i];
                    if (r == null) continue;

                    bool ok;
                    if (wantDistort) ok = r.Kind == CalibKind.Distortion;
                    else if (wantRotCenter) ok = r.Kind == CalibKind.RotationCenter;
                    else ok = r.Kind == CalibKind.NPoint;
                    if (!ok) continue;

                    string label = r.Name + "（" + r.KindDisplay + "）";
                    if (r.HasResult)
                    {
                        if (wantRotCenter)
                        {
                            label += string.Format(CultureInfo.InvariantCulture,
                                "  圆心({0:F2},{1:F2}) R={2:F2} RMS={3:F4}",
                                r.Affine != null && r.Affine.Length > 1 ? r.Affine[0] : 0,
                                r.Affine != null && r.Affine.Length > 1 ? r.Affine[1] : 0,
                                r.Affine != null && r.Affine.Length > 2 ? r.Affine[2] : 0,
                                r.RmsError);
                        }
                        else
                        {
                            label += "  RMS " + r.RmsError.ToString("F3", CultureInfo.InvariantCulture);
                        }
                    }
                    list.Add(new NodeParamOption(label, r.Id));
                }

                if (list.Count == before)
                {
                    string kindName = wantDistort ? "畸变矫正" : (wantRotCenter ? "旋转中心" : "N 点标定");
                    list.Add(new NodeParamOption("（还没有" + kindName + "记录：请到「相机标定」对应页算一条）", ""));
                }
            }
            catch (Exception) { }

            return list;
        }
    }
}
