using System;
using System.Text;
using System.Windows.Forms;
using VisionPlatform.Communication;
using VisionPlatform.Core;

namespace VisionPlatform.UI
{
    /// <summary>通讯测试窗体：TCP 客户端、TCP 服务端、Modbus TCP。</summary>
    public partial class CommForm : Form
    {
        private readonly TcpClientSession _client = new TcpClientSession();
        private readonly TcpServerSession _server = new TcpServerSession();
        private readonly ModbusTcpClient _modbus = new ModbusTcpClient();

        public CommForm()
        {
            InitializeComponent();
            WireEvents();
            Log("就绪。TCP 与 Modbus 协议均为自行实现，无第三方依赖。");

            UiStyle.ApplyForm(this);

            // 语义色：连接/监听=绿，断开/停止监听=红，发送/读取/写入=蓝
            btnClientConnect.Kind = UiButtonKind.Start;
            btnListen.Kind = UiButtonKind.Start;
            btnMbConnect.Kind = UiButtonKind.Start;
            btnClientDisconnect.Kind = UiButtonKind.Stop;
            btnStopListen.Kind = UiButtonKind.Stop;
            btnSend.Kind = UiButtonKind.Grab;
            btnBroadcast.Kind = UiButtonKind.Grab;
            btnMbRead.Kind = UiButtonKind.Grab;
            btnMbWrite.Kind = UiButtonKind.Grab;
        }

        private void WireEvents()
        {
            _client.Received += (s, t) => Ui(() => Log("客户端收到：" + t));
            _client.StatusChanged += (s, t) => Ui(() => Log("客户端：" + t));
            _server.Received += (s, t) => Ui(() => Log("服务端收到：" + t));
            _server.StatusChanged += (s, t) => Ui(() => Log("服务端：" + t));
        }

        private void Ui(Action action)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke((MethodInvoker)delegate { action(); }); }
            catch (Exception) { }
        }

        private void Log(string text)
        {
            string line = string.Format("{0:HH:mm:ss}  {1}", DateTime.Now, text);
            listLog.Items.Add(line);
            if (listLog.Items.Count > 500) listLog.Items.RemoveAt(0);
            listLog.TopIndex = listLog.Items.Count - 1;
            Logger.Info("[Comm] " + text);
        }

        // ---------- TCP 客户端 ----------
        private void OnClientConnect(object sender, EventArgs e)
        {
            var r = _client.Connect(txtHost.Text, (int)numClientPort.Value);

            // 同步到全局通讯状态，供主界面判断"通讯是否正常"
            CommState.TcpClientConnected = r.Success;
            if (r.Success) CommState.ClearError(); else CommState.SetError(r.Message);

            Log(r.Message);
        }

        private void OnClientDisconnect(object sender, EventArgs e)
        {
            _client.Disconnect();
            CommState.TcpClientConnected = false;
            Log("客户端已断开");
        }

        private void OnClientSend(object sender, EventArgs e)
        {
            var r = _client.Send(txtSend.Text);
            Log(r.Success ? ("已发送：" + txtSend.Text) : r.Message);
        }

        // ---------- TCP 服务端 ----------
        private void OnServerStart(object sender, EventArgs e)
        {
            var r = _server.Start((int)numServerPort.Value);
            CommState.TcpServerRunning = r.Success;
            Log(r.Message);
        }

        private void OnServerStop(object sender, EventArgs e)
        {
            _server.Stop();
            CommState.TcpServerRunning = false;
            Log("服务端已停止");
        }

        private void OnServerBroadcast(object sender, EventArgs e)
        {
            Log(_server.Broadcast(txtSend.Text).Message);
        }

        // ---------- Modbus ----------
        private void OnModbusConnect(object sender, EventArgs e)
        {
            var r = _modbus.Connect(txtMbHost.Text, (int)numMbPort.Value);

            CommState.ModbusConnected = r.Success;
            if (r.Success) CommState.ClearError(); else CommState.SetError(r.Message);

            Log("Modbus " + r.Message);
        }

        private void OnModbusRead(object sender, EventArgs e)
        {
            if (!_modbus.IsConnected)
            {
                var c = _modbus.Connect(txtMbHost.Text, (int)numMbPort.Value);
                CommState.ModbusConnected = c.Success;
                if (!c.Success) { Log("Modbus " + c.Message); return; }
            }

            var r = _modbus.ReadHoldingRegisters((ushort)numAddr.Value, (ushort)numCount.Value);
            if (!r.Success) { Log("读取失败：" + r.Message); return; }

            var sb = new StringBuilder();
            sb.Append("读取 ").Append(r.Value.Length).Append(" 个寄存器：");
            foreach (ushort v in r.Value) sb.Append(v).Append(' ');
            Log(sb.ToString());
        }

        private void OnModbusWrite(object sender, EventArgs e)
        {
            if (!_modbus.IsConnected)
            {
                var c = _modbus.Connect(txtMbHost.Text, (int)numMbPort.Value);
                CommState.ModbusConnected = c.Success;
                if (!c.Success) { Log("Modbus " + c.Message); return; }
            }

            var r = _modbus.WriteSingleRegister((ushort)numAddr.Value, (ushort)numValue.Value);
            Log(r.Success
                ? string.Format("已写入地址 {0} = {1}", numAddr.Value, numValue.Value)
                : ("写入失败：" + r.Message));
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            _client.Dispose();
            _server.Dispose();
            _modbus.Dispose();
        }
    }
}
