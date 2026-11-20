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
    /// <para>
    /// 【TCP 客户端 / 服务端已整合】<see cref="tabProtocol"/> 只有两页（TCP 通信 / Modbus TCP），
    /// TCP 页同屏给出「客户端发送」与「服务端广播 + 监听」两组，按「角色」
    /// （<see cref="cboRole"/>）只改 Enabled，不再靠切换分页来切换协议。
    /// 协议类型由 <see cref="SelectedKind"/> 显式判定，<strong>不再按分页下标映射</strong>
    /// <see cref="CommKind.All"/>，避免删页后错位。
    /// </para>
    /// <para>
    /// 【接收显示】<see cref="txtRecv"/> 是 TCP 与 Modbus 共用的只读多行区，
    /// 由 <see cref="CommRegistry.LastReceived"/> 驱动，带时间戳、限 300 行、150 ms 合流刷新，
    /// 防止后台高频回传逐条刷 UI 把主线程淹没。
    /// </para>
    /// </summary>
    public partial class CommForm : Form
    {
        /// <summary>tabProtocol 里「TCP 通信」页的下标。</summary>
        private const int TabIndexTcp = 0;

        /// <summary>tabProtocol 里「Modbus TCP」页的下标。</summary>
        private const int TabIndexModbus = 1;

        /// <summary>接收区最多保留的行数，超出丢弃最旧的（防止无限增长拖垮界面）。</summary>
        private const int MaxRecvLines = 300;

        /// <summary>接收区合流刷新间隔（毫秒）：高频回传时最多每 150 ms 刷一次界面。</summary>
        private const int RecvFlushIntervalMs = 150;

        /// <summary>当前正在编辑的通讯名（空 = 没有选中任何一条）。</summary>
        private string _currentName = "";

        /// <summary>正在回填界面 / 重建列表，用于抑制由此引发的保存与选中事件。</summary>
        private bool _loading;

        /// <summary>注册表变更回调（用于 OnFormClosing 精确退订，避免非模态窗体反复开关后泄漏）。</summary>
        private EventHandler _registryHandler;

        /// <summary>接收区待刷新的文本缓冲（后台事件只入队，UI 线程定时批量刷）。</summary>
        private readonly StringBuilder _recvPending = new StringBuilder();

        /// <summary>接收区合流刷新定时器（WinForms Timer，Tick 在 UI 线程）。</summary>
        private Timer _recvTimer;

        /// <summary>上一次已显示的收到文本：注册表事件也会因连接状态变化而触发，用它去重。</summary>
        private string _lastRecvText = "";

        public CommForm()
        {
            InitializeComponent();
            InitGrid();

            UiStyle.ApplyForm(this);
            ApplySemanticColors();

            // 自绘深色标题栏：与顶部工具栏同色，系统原生标题栏不再出现
            UiTitleBar.Attach(this);

            InitRoleCombo();
            InitModbusCombos();
            InitRecvTimer();

            WireEvents();

            // 通讯点位 / 任务 / 流程 Busy-Ready 绑定（独立窗体，避免把主配置界面撑爆）
            var btnAutomation = new UiButton();
            btnAutomation.Kind = UiButtonKind.Neutral;
            btnAutomation.Text = "点位/任务…";
            btnAutomation.Size = new System.Drawing.Size(96, 32);
            btnAutomation.Location = new System.Drawing.Point(312, 8);
            btnAutomation.Click += delegate
            {
                using (var f = new CommAutomationForm())
                {
                    f.ShowDialog(this);
                }

                CommRegistry.Reload();
                RefreshGrid();
            };
            panelListBtns.Controls.Add(btnAutomation);

            _registryHandler = OnRegistryChanged;
            CommRegistry.Changed += _registryHandler;

            // 建好注册表并按当前配置同步一次
            CommRegistry.Reload();
            RefreshGrid();

            System.Collections.Generic.IReadOnlyList<string> names = CommRegistry.Names();
            if (names.Count > 0) LoadSelected(names[0]);

            Log("就绪。每条通讯可自定义名称，流程节点按名称绑定；协议均为自行实现，零第三方依赖。");
            Log("TCP 客户端与服务端已整合在同一页，用「角色」切换；Modbus 支持字符串/整形/浮点/双精度/布尔。");

            LayoutOps();
        }

        // 收发区（上，tabProtocol）与 接收区（下，panelRecv）保持 1:1 等高；窗体缩放时同步
        private void LayoutOps()
        {
            if (groupOps == null || tabProtocol == null || panelRecv == null) return;
            int h = groupOps.DisplayRectangle.Height;
            if (h <= 0) return;
            tabProtocol.Height = Math.Max(80, h / 2);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutOps();
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

            // 接收区：等宽字体 + 卡片底色（与日志区同一套只读观感）
            UiTheme.StyleReadonlyBox(txtRecv);
            lblRecv.ForeColor = UiTheme.ColorTextSecondary;
            lblMbTip.ForeColor = UiTheme.ColorTextSecondary;
        }

        /// <summary>填充「角色」下拉：下标 0 = TCP 客户端，1 = TCP 服务端（与 CommKind 解耦）。</summary>
        private void InitRoleCombo()
        {
            cboRole.Items.Clear();
            cboRole.Items.Add(CommKind.Display(CommKind.TcpClient));
            cboRole.Items.Add(CommKind.Display(CommKind.TcpServer));
            cboRole.SelectedIndex = 0;
        }

        /// <summary>
        /// 填充 Modbus 的「数据类型」与「字序」下拉。
        /// 显示文案直接取 <see cref="ModbusData.DisplayNameOf"/> / <see cref="ModbusData.OrderTextOf"/>，
        /// 取值按下标回查同一个数组，保证顺序永远一致。
        /// </summary>
        private void InitModbusCombos()
        {
            ModbusDataType[] types = ModbusData.AllTypes();
            cboMbType.Items.Clear();
            foreach (ModbusDataType t in types) cboMbType.Items.Add(ModbusData.DisplayNameOf(t));

            int def = Array.IndexOf(types, ModbusDataType.UInt16);
            cboMbType.SelectedIndex = def >= 0 ? def : 0;

            ModbusWordOrder[] orders = ModbusData.AllWordOrders();
            cboMbOrder.Items.Clear();
            foreach (ModbusWordOrder o in orders) cboMbOrder.Items.Add(ModbusData.OrderTextOf(o));
            cboMbOrder.SelectedIndex = 0;

            ApplyModbusTypeUi();
        }

        /// <summary>接收区合流定时器：挂到 components 上，随窗体一起释放。</summary>
        private void InitRecvTimer()
        {
            _recvTimer = new Timer();
            _recvTimer.Interval = RecvFlushIntervalMs;
            _recvTimer.Tick += OnRecvFlushTick;
            components.Add(_recvTimer);
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

            tabProtocol.SelectedIndexChanged += OnKindChanged;
            cboRole.SelectedIndexChanged += OnRoleChanged;
            cboMbType.SelectedIndexChanged += OnMbTypeChanged;
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
                ApplyKindToUi(cfg.Kind);
                txtHost.Text = cfg.Host;
                numPort.Value = cfg.Port;
                numSlaveId.Value = cfg.SlaveId;
                chkEnabled.Checked = cfg.Enabled;
                chkAutoConnect.Checked = cfg.AutoConnect;

                // 换了一条通讯，接收去重基准要跟着换，否则新通讯的第一条可能被误判为重复
                _lastRecvText = "";
            }
            finally
            {
                _loading = false;
            }

            ApplyKindVisibility();
        }

        /// <summary>
        /// 按协议类型回填分页与角色（替代原来的 KindIndex + 按下标取 CommKind.All）。
        /// </summary>
        /// <param name="kind"><see cref="CommKind"/> 常量。</param>
        private void ApplyKindToUi(string kind)
        {
            string k = CommKind.Normalize(kind);

            if (k == CommKind.ModbusTcp)
            {
                tabProtocol.SelectedIndex = TabIndexModbus;
                return;
            }

            tabProtocol.SelectedIndex = TabIndexTcp;
            cboRole.SelectedIndex = k == CommKind.TcpServer ? 1 : 0;
        }

        /// <summary>
        /// 按当前界面状态决定两组的启用 / 禁用：只改 Enabled，不改 Visible
        /// （沿用既有约定，避免切换时布局跳动）。
        /// </summary>
        private void ApplyKindVisibility()
        {
            string kind = SelectedKind();
            bool isServer = kind == CommKind.TcpServer;
            bool isClient = kind == CommKind.TcpClient;
            bool isModbus = kind == CommKind.ModbusTcp;

            // 服务端监听本地端口，不需要目标主机；从站号只有 Modbus 用得上
            lblHost.Enabled = !isServer;
            txtHost.Enabled = !isServer;
            lblSlaveId.Enabled = isModbus;
            numSlaveId.Enabled = isModbus;

            // 角色只在 TCP 页有意义
            lblRole.Enabled = !isModbus;
            cboRole.Enabled = !isModbus;

            // TCP 页内两组：客户端（发送）与服务端（广播 + 监听），按角色互斥启用
            lblSend.Enabled = isClient;
            txtSend.Enabled = isClient;
            btnSend.Enabled = isClient;

            lblSrvSend.Enabled = isServer;
            txtSrvSend.Enabled = isServer;
            btnBroadcast.Enabled = isServer;
            btnStartListen.Enabled = isServer;
            btnStopListen.Enabled = isServer;
        }

        /// <summary>
        /// 当前界面表达的协议类型：<strong>显式判定</strong>，不再用分页下标映射
        /// <see cref="CommKind.All"/>（分页从 3 页变 2 页后下标会错位）。
        /// </summary>
        private string SelectedKind()
        {
            if (tabProtocol.SelectedIndex == TabIndexModbus) return CommKind.ModbusTcp;

            bool isServer = cboRole.SelectedIndex == 1;
            return isServer ? CommKind.TcpServer : CommKind.TcpClient;
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

        /// <summary>
        /// 切分页：只刷新启用状态。
        /// <para>【与旧实现的差别】旧实现会把端口强制改成默认端口（Modbus 502 / 其它 9000）
        /// 并立刻 ApplyEdits 落盘 —— 用户手填的端口被悄悄改掉，属于缺陷，这里已去掉。
        /// 默认端口只在 <see cref="CommEndpointConfig.Create"/> 新建端点时填一次。</para>
        /// </summary>
        private void OnKindChanged(object sender, EventArgs e)
        {
            if (_loading) return;

            ApplyKindVisibility();
        }

        /// <summary>切「角色」（TCP 客户端 / 服务端）：同样只刷新启用状态，不改端口、不落盘。</summary>
        private void OnRoleChanged(object sender, EventArgs e)
        {
            if (_loading) return;

            ApplyKindVisibility();
            Log("角色已切换为「" + CommKind.Display(SelectedKind()) + "」，点「保存」或「连接」后生效");
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

        // ---------------------- TCP 客户端 / 服务端（已整合在同一页） ----------------------

        private void OnClientSend(object sender, EventArgs e)
        {
            if (_currentName.Length == 0) { Log("请先选中一条通讯"); return; }
            if (SelectedKind() == CommKind.ModbusTcp) { Log("Modbus TCP 不支持文本发送，请用「Modbus TCP」页读写寄存器"); return; }
            if (!SyncKindIfChanged()) return;

            OpResult r = CommRegistry.SendText(_currentName, txtSend.Text);
            Log(r.Success ? ("「" + _currentName + "」已发送：" + txtSend.Text) : r.Message);
        }

        private void OnServerStart(object sender, EventArgs e)
        {
            if (_currentName.Length == 0) { Log("请先选中一条通讯"); return; }

            // 与「连接」一致：先把编辑区的参数落盘，保证按界面上的端口启动监听
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
            if (!SyncKindIfChanged()) return;

            OpResult r = CommRegistry.SendText(_currentName, txtSrvSend.Text);
            Log(r.Success ? ("「" + _currentName + "」" + r.Message) : r.Message);
        }

        /// <summary>
        /// 界面上的协议类型与实际保存的不一致时（例如刚切到服务端 / Modbus 还没保存），
        /// 先把类型同步落盘再动作，免得底层按旧类型分发。
        /// </summary>
        private bool SyncKindIfChanged()
        {
            CommEndpointConfig cfg = CommRegistry.Config(_currentName);
            if (cfg == null) return true;
            if (CommKind.Normalize(cfg.Kind) == SelectedKind()) return true;

            return ApplyEdits(false);
        }

        // ---------------------- Modbus ----------------------

        /// <summary>当前选中的数据类型（按下标回查，与下拉填充顺序严格一致）。</summary>
        private ModbusDataType SelectedMbType()
        {
            ModbusDataType[] all = ModbusData.AllTypes();
            int i = cboMbType.SelectedIndex;
            return (i >= 0 && i < all.Length) ? all[i] : ModbusDataType.UInt16;
        }

        /// <summary>当前选中的字序（按下标回查，与下拉填充顺序严格一致）。</summary>
        private ModbusWordOrder SelectedMbOrder()
        {
            ModbusWordOrder[] all = ModbusData.AllWordOrders();
            int i = cboMbOrder.SelectedIndex;
            return (i >= 0 && i < all.Length) ? all[i] : ModbusWordOrder.ABCD;
        }

        /// <summary>
        /// 数据类型改变后同步「数量」的含义与上限：
        /// String 是字符数（上限 250），其余类型是寄存器个数（上限 125），
        /// 并把默认值填成单个值所需的寄存器数，避免选了 Float32 却只读 1 个寄存器而报长度不足。
        /// </summary>
        private void ApplyModbusTypeUi()
        {
            ModbusDataType type = SelectedMbType();
            bool isString = type == ModbusDataType.String;

            lblCount.Text = isString ? "数量（字符）：" : "数量（寄存器）：";

            // 先放宽上限再收紧下限，最后才设默认值，避免 NumericUpDown 因越界抛异常
            numCount.Maximum = isString ? ModbusData.MaxStringChars : ModbusData.MaxReadRegisters;
            numCount.Minimum = 1;

            if (!_loading)
            {
                int unit = isString ? 10 : Math.Max(1, ModbusData.RegisterCountOf(type, 1));
                decimal next = unit;
                if (next > numCount.Maximum) next = numCount.Maximum;
                if (numCount.Value != next) numCount.Value = next;
            }

            lblMbTip.Text = isString
                ? "地址从 0 开始（非 40001 编号）；String 按字符计，写值最长 246 字符"
                : "地址从 0 开始（非 40001 编号）；" + ModbusData.DisplayNameOf(type).Split('（')[0]
                  + " 每值占 " + Math.Max(1, ModbusData.RegisterCountOf(type, 1)) + " 个寄存器";
        }

        private void OnMbTypeChanged(object sender, EventArgs e)
        {
            if (_loading) return;

            ApplyModbusTypeUi();
        }

        private void OnModbusRead(object sender, EventArgs e)
        {
            if (_currentName.Length == 0) { Log("请先选中一条通讯"); return; }
            if (!SyncKindIfChanged()) return;

            ModbusDataType type = SelectedMbType();
            ModbusWordOrder order = SelectedMbOrder();
            ushort address = (ushort)numAddr.Value;
            int length = (int)numCount.Value;

            OpResult<string> r = CommRegistry.ModbusReadAs(_currentName, type, address, length, order);

            if (!r.Success)
            {
                Log("读取失败：" + r.Message);
                return;
            }

            string text = r.Value == null ? "" : r.Value;
            Log(string.Format("「{0}」读取 {1} 地址 {2} = {3}",
                _currentName, ModbusData.DisplayNameOf(type), address, text));

            // 读到的值也进接收区，用户不必再去日志里翻
            PushRecvLine(string.Format("{0:HH:mm:ss}  << {1} @{2} = {3}",
                DateTime.Now, ModbusData.DisplayNameOf(type), address, text));
            _recvTimer.Start();
        }

        private void OnModbusWrite(object sender, EventArgs e)
        {
            if (_currentName.Length == 0) { Log("请先选中一条通讯"); return; }
            if (!SyncKindIfChanged()) return;

            ModbusDataType type = SelectedMbType();
            ModbusWordOrder order = SelectedMbOrder();
            ushort address = (ushort)numAddr.Value;
            string text = txtValue.Text == null ? "" : txtValue.Text;

            OpResult r = CommRegistry.ModbusWriteAs(_currentName, type, address, text, order);

            Log(r.Success
                ? string.Format("「{0}」已写入 {1} 地址 {2} = {3}",
                    _currentName, ModbusData.DisplayNameOf(type), address, text)
                : ("写入失败：" + r.Message));
        }

        // ---------------------- 接收显示 ----------------------

        /// <summary>
        /// 注册表事件由后台线程抛出，界面更新必须回到 UI 线程。
        /// 这里同时把「最近收到的一条」搬进接收区。
        /// </summary>
        private void OnRegistryChanged(object sender, EventArgs e)
        {
            Ui(delegate
            {
                RefreshGrid();
                PumpRecv();
            });
        }

        /// <summary>
        /// 取 <see cref="CommRegistry.LastReceived"/> 的当前值并入队（UI 线程执行）。
        /// 注册表事件也会因连接状态变化触发，故用 <see cref="_lastRecvText"/> 去重。
        /// </summary>
        private void PumpRecv()
        {
            if (_currentName.Length == 0) return;

            string text = CommRegistry.LastReceived(_currentName);
            if (string.IsNullOrEmpty(text)) return;
            if (string.Equals(text, _lastRecvText, StringComparison.Ordinal)) return;

            _lastRecvText = text;
            PushRecvLine(string.Format("{0:HH:mm:ss}  << {1}", DateTime.Now, text));
            _recvTimer.Start();
        }

        /// <summary>把一行文本放进待刷缓冲，并启动合流定时器（不直接刷界面）。</summary>
        private void PushRecvLine(string line)
        {
            lock (_recvPending)
            {
                _recvPending.Append(line).Append('\n');
            }
        }

        private void OnRecvFlushTick(object sender, EventArgs e)
        {
            _recvTimer.Stop();
            FlushRecv();
        }

        /// <summary>
        /// 把缓冲一次性写进接收区。高频回传时多条合一次，
        /// 界面刷新频率被钳在 150 ms 一次，不会淹没 UI 线程。
        /// </summary>
        private void FlushRecv()
        {
            string chunk;
            lock (_recvPending)
            {
                if (_recvPending.Length == 0) return;
                chunk = _recvPending.ToString();
                _recvPending.Length = 0;
            }

            if (IsDisposed || txtRecv == null || !txtRecv.IsHandleCreated) return;

            txtRecv.AppendText(chunk);
            TrimRecvLines();

            txtRecv.SelectionStart = txtRecv.TextLength;
            txtRecv.ScrollToCaret();
        }

        /// <summary>接收区超过 <see cref="MaxRecvLines"/> 行时丢弃最旧的。</summary>
        private void TrimRecvLines()
        {
            string[] lines = txtRecv.Lines;
            if (lines.Length <= MaxRecvLines) return;

            string[] keep = new string[MaxRecvLines];
            Array.Copy(lines, lines.Length - MaxRecvLines, keep, 0, MaxRecvLines);
            txtRecv.Lines = keep;
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

            if (_recvTimer != null) _recvTimer.Stop();

            base.OnFormClosing(e);
        }
    }
}
