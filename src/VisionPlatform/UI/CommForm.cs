using System;
using System.Text;
using System.Windows.Forms;
using VisionPlatform.Communication;
using VisionPlatform.Core;

namespace VisionPlatform.UI
{
    /// <summary>
    /// 通讯管理窗体（VisionMaster 风格）：左侧是可<strong>新增 / 删除 / 复制 / 重命名</strong>的
    /// 通讯列表，右侧是选中通讯的属性编辑与收发测试，底部是日志。
    /// <para>
    /// 【与改造前的区别】改造前是三个写死的 GroupBox（TCP 客户端 / TCP 服务端 / Modbus），
    /// 没有名称、不能增删、配置也不落盘。现在通讯条数由用户决定，
    /// 每条有唯一名称与协议类型，配置写入 <see cref="AppConfig.Comm"/>，
    /// 运行时由 <see cref="CommRegistry"/> 持有活连接对象，流程图侧按名称绑定。
    /// </para>
    /// <para>
    /// 【不持有连接】窗体本身不再 new 连接对象，全部通过 <see cref="CommRegistry"/> 按名称操作，
    /// 因此窗体关闭不会掉线，流程节点与窗体看到的是同一条链路。
    /// </para>
    /// </summary>
    public partial class CommForm : Form
    {
        /// <summary>当前正在编辑的通讯名（空 = 没有选中任何一条）。</summary>
        private string _currentName = "";

        /// <summary>正在回填界面 / 重建列表，用于抑制由此引发的保存与选中事件。</summary>
        private bool _loading;

        /// <summary>注册表变更回调（用于 OnFormClosing 精确退订，避免非模态窗体反复开关后泄漏）。</summary>
        private EventHandler _registryHandler;

        public CommForm()
        {
            InitializeComponent();
            InitGrid();

            UiStyle.ApplyForm(this);
            ApplySemanticColors();

            WireEvents();

            _registryHandler = OnRegistryChanged;
            CommRegistry.Changed += _registryHandler;

            // 建好注册表并按当前配置同步一次
            CommRegistry.Reload();
            RefreshGrid();

            System.Collections.Generic.IReadOnlyList<string> names = CommRegistry.Names();
            if (names.Count > 0) LoadSelected(names[0]);

            Log("就绪。每条通讯可自定义名称，流程节点按名称绑定；协议均为自行实现，零第三方依赖。");
        }

        // ---------------------- 初始化 ----------------------

        private void InitGrid()
        {
            gridEndpoints.Columns.Clear();

            gridEndpoints.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colName",
                HeaderText = "名称",
                FillWeight = 30,
                MinimumWidth = 90
            });
            gridEndpoints.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colKind",
                HeaderText = "类型",
                FillWeight = 18,
                MinimumWidth = 70
            });
            gridEndpoints.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colTarget",
                HeaderText = "目标",
                FillWeight = 30,
                MinimumWidth = 90
            });
            gridEndpoints.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colStatus",
                HeaderText = "状态",
                FillWeight = 22,
                MinimumWidth = 80
            });

            gridEndpoints.ReadOnly = true;
        }

        private void ApplySemanticColors()
        {
            // 语义色：连接/监听=绿，断开/停止/删除=红，发送/读取/写入/新增/测试=蓝
            btnAdd.Kind = UiButtonKind.Grab;
            btnCopy.Kind = UiButtonKind.Neutral;
            btnDelete.Kind = UiButtonKind.Stop;

            btnSave.Kind = UiButtonKind.Start;
            btnConnect.Kind = UiButtonKind.Start;
            btnDisconnect.Kind = UiButtonKind.Stop;
            btnTest.Kind = UiButtonKind.Grab;

            btnSend.Kind = UiButtonKind.Grab;
            btnBroadcast.Kind = UiButtonKind.Grab;
            btnStartListen.Kind = UiButtonKind.Start;
            btnStopListen.Kind = UiButtonKind.Stop;
            btnMbRead.Kind = UiButtonKind.Grab;
            btnMbWrite.Kind = UiButtonKind.Grab;

            UiTheme.StyleGroupTitle(lblListTitle);
            UiTheme.StyleReadonlyBox(listLog);
        }

        private void WireEvents()
        {
            btnAdd.Click += OnAdd;
            btnCopy.Click += OnCopy;
            btnDelete.Click += OnDelete;

            btnSave.Click += OnSave;
            btnConnect.Click += OnConnect;
            btnDisconnect.Click += OnDisconnect;
            btnTest.Click += OnTestUser;

            btnSend.Click += OnClientSend;
            btnBroadcast.Click += OnServerBroadcast;
            btnStartListen.Click += OnServerStart;
            btnStopListen.Click += OnServerStop;
            btnMbRead.Click += OnModbusRead;
            btnMbWrite.Click += OnModbusWrite;

            cboKind.SelectedIndexChanged += OnKindChanged;
            chkEnabled.CheckedChanged += OnEnabledChanged;
            chkAutoConnect.CheckedChanged += OnAutoConnectChanged;

            gridEndpoints.SelectionChanged += OnGridSelectionChanged;
        }

        // ---------------------- 列表 ----------------------

        private void RefreshGrid()
        {
            _loading = true;
            try
            {
                gridEndpoints.Rows.Clear();

                foreach (string name in CommRegistry.Names())
                {
                    CommEndpointConfig cfg = CommRegistry.Config(name);
                    if (cfg == null) continue;

                    gridEndpoints.Rows.Add(
                        cfg.Name,
                        cfg.KindDisplay,
                        cfg.TargetText,
                        CommRegistry.StatusText(name));
                }

                SelectRow(_currentName);
            }
            finally
            {
                _loading = false;
            }
        }

        private void SelectRow(string name)
        {
            if (string.IsNullOrEmpty(name)) return;

            foreach (DataGridViewRow row in gridEndpoints.Rows)
            {
                object v = row.Cells[0].Value;
                if (v != null && string.Equals(v.ToString(), name, StringComparison.OrdinalIgnoreCase))
                {
                    row.Selected = true;
                    if (gridEndpoints.Columns.Count > 0) gridEndpoints.CurrentCell = row.Cells[0];
                    return;
                }
            }
        }

        private void OnGridSelectionChanged(object sender, EventArgs e)
        {
            if (_loading) return;
            if (gridEndpoints.CurrentRow == null) return;

            object v = gridEndpoints.CurrentRow.Cells[0].Value;
            string name = v == null ? "" : v.ToString();
            if (name.Length == 0) return;

            LoadSelected(name);
        }

        /// <summary>把某条通讯的配置回填到右侧编辑区。</summary>
        private void LoadSelected(string name)
        {
            CommEndpointConfig cfg = CommRegistry.Config(name);
            if (cfg == null) return;

            _loading = true;
            try
            {
                _currentName = cfg.Name;
                txtName.Text = cfg.Name;
                cboKind.SelectedIndex = KindIndex(cfg.Kind);
                txtHost.Text = cfg.Host;
                numPort.Value = cfg.Port;
                numSlaveId.Value = cfg.SlaveId;
                chkEnabled.Checked = cfg.Enabled;
                chkAutoConnect.Checked = cfg.AutoConnect;
            }
            finally
            {
                _loading = false;
            }

            ApplyKindVisibility();
        }

        private void ApplyKindVisibility()
        {
            string kind = SelectedKind();

            // 每种协议只显示自己那一栏操作区（三个面板位置重叠，靠 Visible 互斥）
            panelTcpClient.Visible = kind == CommKind.TcpClient;
            panelTcpServer.Visible = kind == CommKind.TcpServer;
            panelModbus.Visible = kind == CommKind.ModbusTcp;

            // 服务端监听本地端口，不需要目标主机；从站号只有 Modbus 用得上
            lblHost.Enabled = kind != CommKind.TcpServer;
            txtHost.Enabled = kind != CommKind.TcpServer;
            lblSlaveId.Enabled = kind == CommKind.ModbusTcp;
            numSlaveId.Enabled = kind == CommKind.ModbusTcp;
        }

        private static int KindIndex(string kind)
        {
            for (int i = 0; i < CommKind.All.Length; i++)
            {
                if (CommKind.All[i] == CommKind.Normalize(kind)) return i;
            }
            return 0;
        }

        private string SelectedKind()
        {
            int idx = cboKind.SelectedIndex;
            if (idx < 0 || idx >= CommKind.All.Length) return CommKind.TcpClient;
            return CommKind.All[idx];
        }

        // ---------------------- 增删改 ----------------------

        private void OnAdd(object sender, EventArgs e)
        {
            CommSettings comm = ConfigStore.Current.Comm;
            CommEndpointConfig ep = CommEndpointConfig.Create(
                comm.MakeUniqueName(CommKind.Display(CommKind.TcpClient)), CommKind.TcpClient);

            comm.Endpoints.Add(ep);
            Persist();

            LoadSelected(ep.Name);
            RefreshGrid();
            Log("已新增通讯「" + ep.Name + "」");
        }

        private void OnCopy(object sender, EventArgs e)
        {
            CommSettings comm = ConfigStore.Current.Comm;
            CommEndpointConfig src = comm.Find(_currentName);
            if (src == null) { Log("请先选中一条通讯"); return; }

            CommEndpointConfig copy = src.Clone();
            copy.Name = comm.MakeUniqueName(src.Name);
            comm.Endpoints.Add(copy);

            Persist();

            LoadSelected(copy.Name);
            RefreshGrid();
            Log("已复制为「" + copy.Name + "」");
        }

        private void OnDelete(object sender, EventArgs e)
        {
            CommSettings comm = ConfigStore.Current.Comm;
            CommEndpointConfig target = comm.Find(_currentName);
            if (target == null) { Log("请先选中一条通讯"); return; }

            string name = target.Name;
            comm.Endpoints.Remove(target);

            // 端点没了 → 对应链路必须一起作废，否则注册表里会留一条看不见的连接
            CommRegistry.Disconnect(name);
            Persist();

            System.Collections.Generic.IReadOnlyList<string> names = CommRegistry.Names();
            _currentName = names.Count > 0 ? names[0] : "";
            if (_currentName.Length > 0) LoadSelected(_currentName);
            RefreshGrid();

            Log("已删除通讯「" + name + "」");
        }

        private void OnSave(object sender, EventArgs e)
        {
            if (ApplyEdits(true)) Log("已保存「" + _currentName + "」，流程图侧已同步");
        }

        /// <summary>
        /// 把编辑区的值写回配置。
        /// </summary>
        /// <param name="allowRename">是否允许改名（保存按钮为 true；连接等即时操作为 false）。</param>
        private bool ApplyEdits(bool allowRename)
        {
            CommSettings comm = ConfigStore.Current.Comm;
            CommEndpointConfig target = comm.Find(_currentName);
            if (target == null) { Log("请先选中一条通讯"); return false; }

            string newName = txtName.Text.Trim();
            if (newName.Length == 0) { Log("名称不能为空"); return false; }

            if (!allowRename && !string.Equals(newName, _currentName, StringComparison.OrdinalIgnoreCase))
            {
                Log("名称已修改，请先点「保存」再操作");
                return false;
            }

            if (allowRename && comm.NameExists(newName, _currentName))
            {
                Log("名称「" + newName + "」已存在");
                return false;
            }

            target.Name = newName;
            target.Kind = SelectedKind();
            target.Host = txtHost.Text.Trim();
            target.Port = (int)numPort.Value;
            target.SlaveId = (int)numSlaveId.Value;
            target.Enabled = chkEnabled.Checked;
            target.AutoConnect = chkAutoConnect.Checked;
            target.Normalize();

            if (!string.Equals(newName, _currentName, StringComparison.OrdinalIgnoreCase))
            {
                // 改名 = 旧名称的链路作废
                CommRegistry.Disconnect(_currentName);
                _currentName = newName;
            }

            Persist();
            return true;
        }

        /// <summary>
        /// 落盘并同步：ConfigStore.Save() 会自动写文件并广播 Changed，
        /// CommRegistry 订阅了该事件，因此「通讯改完同步给流程图」不需要额外接线。
        /// 这里再显式 Reload 一次，保证注册表尚未订阅时也能立即生效。
        /// </summary>
        private void Persist()
        {
            ConfigStore.Save();
            CommRegistry.Reload();
        }

        // ---------------------- 属性变更 ----------------------

        private void OnKindChanged(object sender, EventArgs e)
        {
            if (_loading) return;

            ApplyKindVisibility();

            // 切换协议后端口默认值随之改变（Modbus 502，其余 9000），并即时落盘
            string kind = SelectedKind();
            numPort.Value = CommKind.DefaultPort(kind);

            if (_currentName.Length > 0) ApplyEdits(false);
        }

        private void OnEnabledChanged(object sender, EventArgs e)
        {
            if (_loading) return;
            if (_currentName.Length == 0) return;

            ApplyEdits(false);

            if (!chkEnabled.Checked) CommRegistry.Disconnect(_currentName);
        }

        private void OnAutoConnectChanged(object sender, EventArgs e)
        {
            if (_loading) return;
            if (_currentName.Length == 0) return;

            ApplyEdits(false);
        }

        // ---------------------- 连接 / 断开 / 测试 ----------------------

        private void OnConnect(object sender, EventArgs e)
        {
            if (_currentName.Length == 0) { Log("请先选中一条通讯"); return; }
            if (!ApplyEdits(false)) return;

            OpResult r = CommRegistry.Connect(_currentName);
            Log("「" + _currentName + "」" + r.Message);
            RefreshGrid();
        }

        private void OnDisconnect(object sender, EventArgs e)
        {
            if (_currentName.Length == 0) { Log("请先选中一条通讯"); return; }

            CommRegistry.Disconnect(_currentName);
            Log("「" + _currentName + "」已断开");
            RefreshGrid();
        }

        /// <summary>
        /// 测试：用编辑区里<strong>当前</strong>的参数做一次临时连接，成功即断开，
        /// 既不落盘也不影响注册表里的正式链路。
        /// </summary>
        private void OnTestUser(object sender, EventArgs e)
        {
            string kind = SelectedKind();
            string host = txtHost.Text.Trim();
            int port = (int)numPort.Value;

            OpResult r;
            if (kind == CommKind.TcpServer)
            {
                TcpServerSession s = new TcpServerSession();
                r = s.Start(port);
                s.Stop();
            }
            else if (kind == CommKind.ModbusTcp)
            {
                ModbusTcpClient m = new ModbusTcpClient();
                m.SlaveId = (byte)numSlaveId.Value;
                r = m.Connect(host, port);
                m.Disconnect();
            }
            else
            {
                TcpClientSession c = new TcpClientSession();
                r = c.Connect(host, port);
                c.Disconnect();
            }

            Log("测试 " + CommKind.Display(kind) + "：" + r.Message);
        }

        // ---------------------- TCP 客户端 / 服务端 ----------------------

        private void OnClientSend(object sender, EventArgs e)
        {
            if (_currentName.Length == 0) { Log("请先选中一条通讯"); return; }

            OpResult r = CommRegistry.SendText(_currentName, txtSend.Text);
            Log(r.Success ? ("「" + _currentName + "」已发送：" + txtSend.Text) : r.Message);
        }

        private void OnServerStart(object sender, EventArgs e)
        {
            if (_currentName.Length == 0) { Log("请先选中一条通讯"); return; }
            if (!ApplyEdits(false)) return;

            OpResult r = CommRegistry.Connect(_currentName);
            Log("「" + _currentName + "」" + r.Message);
            RefreshGrid();
        }

        private void OnServerStop(object sender, EventArgs e)
        {
            if (_currentName.Length == 0) { Log("请先选中一条通讯"); return; }

            CommRegistry.Disconnect(_currentName);
            Log("「" + _currentName + "」已停止监听");
            RefreshGrid();
        }

        private void OnServerBroadcast(object sender, EventArgs e)
        {
            if (_currentName.Length == 0) { Log("请先选中一条通讯"); return; }

            OpResult r = CommRegistry.SendText(_currentName, txtSrvSend.Text);
            Log(r.Success ? ("「" + _currentName + "」" + r.Message) : r.Message);
        }

        // ---------------------- Modbus ----------------------

        private void OnModbusRead(object sender, EventArgs e)
        {
            if (_currentName.Length == 0) { Log("请先选中一条通讯"); return; }

            OpResult<ushort[]> r = CommRegistry.ModbusRead(
                _currentName, (ushort)numAddr.Value, (ushort)numCount.Value);

            if (!r.Success) { Log("读取失败：" + r.Message); return; }

            StringBuilder sb = new StringBuilder();
            sb.Append("「").Append(_currentName).Append("」读取 ").Append(r.Value.Length).Append(" 个寄存器：");
            foreach (ushort v in r.Value) sb.Append(v).Append(' ');

            Log(sb.ToString());
        }

        private void OnModbusWrite(object sender, EventArgs e)
        {
            if (_currentName.Length == 0) { Log("请先选中一条通讯"); return; }

            OpResult r = CommRegistry.ModbusWrite(_currentName, (ushort)numAddr.Value, (ushort)numValue.Value);
            Log(r.Success
                ? string.Format("「{0}」已写入地址 {1} = {2}", _currentName, numAddr.Value, numValue.Value)
                : ("写入失败：" + r.Message));
        }

        // ---------------------- 收尾 ----------------------

        /// <summary>注册表事件由后台线程抛出，界面更新必须回到 UI 线程。</summary>
        private void OnRegistryChanged(object sender, EventArgs e)
        {
            Ui(RefreshGrid);
        }

        private void Ui(Action action)
        {
            if (IsDisposed || !IsHandleCreated) return;

            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (!IsDisposed) action();
                });
            }
            catch (Exception)
            {
                // 窗体正在关闭，忽略
            }
        }

        private void Log(string text)
        {
            string line = string.Format("{0:HH:mm:ss}  {1}", DateTime.Now, text);
            listLog.Items.Add(line);
            if (listLog.Items.Count > 500) listLog.Items.RemoveAt(0);
            listLog.TopIndex = listLog.Items.Count - 1;
            Logger.Info("[Comm] " + text);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 只退订事件：连接对象归 CommRegistry 全局持有，窗体关闭不应掉线
            if (_registryHandler != null)
            {
                CommRegistry.Changed -= _registryHandler;
                _registryHandler = null;
            }

            base.OnFormClosing(e);
        }
    }
}
