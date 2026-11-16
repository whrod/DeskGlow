using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using QRCoder;

namespace DeskGlow;

internal sealed class Dashboard : Form
{
    private readonly int port;
    private readonly string code;
    private readonly Func<long> lastRequest;
    private readonly Label status = new() { AutoSize = true, Font = new Font("Malgun Gothic", 14, FontStyle.Bold) };
    private readonly NotifyIcon tray = new() { Icon = SystemIcons.Application, Text = "DeskGlow" };
    private Control pairing = null!;
    private bool showPairing;
    private bool enabled = true;
    private bool exiting;
    private readonly ComboBox addresses = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250 };
    private readonly PictureBox picture = new()
    {
        Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.CenterImage, BackColor = Color.White
    };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 2000 };

    public Dashboard(int port, string code, Workspace workspace, Func<long> lastRequest, Action<bool> setEnabled)
    {
        this.port = port;
        this.code = code;
        this.lastRequest = lastRequest;
        Text = "DeskGlow";
        Font = new Font("Malgun Gothic", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(690, 610);
        MinimumSize = new Size(600, 610);
        BackColor = Color.FromArgb(239, 243, 249);

        var root = Column(3, 18);
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(new Label
        {
            Text = "DeskGlow", AutoSize = true, Font = new Font("Segoe UI", 24, FontStyle.Bold),
            ForeColor = Color.FromArgb(22, 38, 64)
        }, 0, 0);
        status.Margin = new Padding(3, 4, 3, 16);
        root.Controls.Add(status, 0, 1);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(CreateConnectionTab());
        var workspaceTab = new WorkspaceTab(workspace);
        tabs.TabPages.Add(workspaceTab);
        tabs.TabPages.Add(new DeveloperTab());
        tabs.TabPages.Add(CreateFeatureTab("기능 추가", "전원 제어 · PC 절전",
            "갤럭시에서 PC 절전을 누르고, 연결된 키보드로 깨우세요."));
        var menu = new ContextMenuStrip();
        menu.Items.Add("DeskGlow 열기", null, (_, _) => OpenWindow());
        var toggle = new ToolStripMenuItem("PC 제어 끄기");
        toggle.Click += (_, _) =>
        {
            enabled = !enabled;
            setEnabled(enabled);
            toggle.Text = enabled ? "PC 제어 끄기" : "PC 제어 켜기";
            RefreshStatus();
        };
        menu.Items.Add(toggle);
        menu.Items.Add("DeskGlow 종료", null, (_, _) => { exiting = true; Close(); });
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => OpenWindow();
        tray.Visible = true;
        FormClosing += (_, e) =>
        {
            if (exiting || e.CloseReason is CloseReason.WindowsShutDown or CloseReason.ApplicationExitCall)
                return;
            e.Cancel = true;
            Hide();
        };
        root.Controls.Add(tabs, 0, 2);
        Controls.Add(root);

        addresses.SelectedIndexChanged += (_, _) => UpdateQr();
        timer.Tick += (_, _) =>
        {
            RefreshStatus();
            workspaceTab.RefreshStatus();
        };
        RefreshStatus();
        timer.Start();
    }

    private TabPage CreateConnectionTab()
    {
        var tab = new TabPage("연결") { BackColor = Color.White };
        var wrapper = Column(2, 0);
        wrapper.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        wrapper.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var add = new Button { Text = "QR 표시 / 숨기기", AutoSize = true };
        add.Click += (_, _) => { showPairing = !pairing.Visible; RefreshStatus(); };
        wrapper.Controls.Add(add, 0, 0);
        var layout = Column(4, 14);
        pairing = layout;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            Text = "갤럭시에서 QR로 연결을 누르고 아래 QR을 스캔하세요.",
            AutoSize = true, Margin = new Padding(3, 0, 3, 8)
        }, 0, 0);

        var row = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        row.Controls.Add(new Label { Text = "PC 주소", AutoSize = true, Margin = new Padding(3, 7, 6, 0) });
        row.Controls.Add(addresses);
        var copy = new Button { Text = "주소와 코드 복사", AutoSize = true };
        copy.Click += (_, _) =>
        {
            if (addresses.SelectedItem is string address)
            {
                try { Clipboard.SetText(address + Environment.NewLine + code); }
                catch (System.Runtime.InteropServices.ExternalException)
                {
                    MessageBox.Show(this, "클립보드가 사용 중입니다. 잠시 후 다시 시도하세요.", "DeskGlow");
                }
            }
        };
        row.Controls.Add(copy);
        layout.Controls.Add(row, 0, 1);
        layout.Controls.Add(picture, 0, 2);
        layout.Controls.Add(new Label
        {
            Text = "같은 Wi-Fi에 연결하세요. 창을 닫아도 트레이에서 계속 실행됩니다.",
            AutoSize = true
        }, 0, 3);
        wrapper.Controls.Add(layout, 0, 1);
        tab.Controls.Add(wrapper);
        return tab;
    }

    private void RefreshStatus()
    {
        var current = NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up &&
                adapter.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .OrderByDescending(adapter => adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
            .Where(entry => entry.Address.AddressFamily == AddressFamily.InterNetwork &&
                !IPAddress.IsLoopback(entry.Address))
            .Select(entry => $"http://{entry.Address}:{port}")
            .Distinct()
            .ToArray();
        if (!addresses.Items.Cast<string>().SequenceEqual(current))
        {
            var selected = addresses.SelectedItem as string;
            addresses.Items.Clear();
            addresses.Items.AddRange(current);
            if (current.Length > 0)
                addresses.SelectedIndex = selected != null && current.Contains(selected)
                    ? Array.IndexOf(current, selected) : 0;
            else
                UpdateQr();
        }

        var ticks = lastRequest();
        var connected = enabled && current.Length > 0 && ticks != 0 &&
            DateTime.UtcNow.Ticks - ticks < TimeSpan.FromSeconds(35).Ticks;
        status.Text = !enabled ? "●  PC 제어 꺼짐" : connected ? "●  갤럭시 연결됨" :
            current.Length == 0 ? "●  네트워크 연결 필요" : "●  갤럭시 연결 대기";
        status.ForeColor = connected ? Color.FromArgb(18, 112, 64) : Color.FromArgb(77, 88, 105);
        pairing.Visible = enabled && (!connected || showPairing);
        tray.Text = "DeskGlow · " + status.Text[3..];
    }

    private void OpenWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void UpdateQr()
    {
        var previous = picture.Image;
        picture.Image = null;
        previous?.Dispose();
        if (addresses.SelectedItem is not string address) return;
        using var data = QRCodeGenerator.GenerateQrCode($"deskglow:{address}#{code}", QRCodeGenerator.ECCLevel.M);
        using var renderer = new PngByteQRCode(data);
        using var stream = new MemoryStream(renderer.GetGraphic(8));
        using var image = Image.FromStream(stream);
        picture.Image = new Bitmap(image);
    }

    private static TableLayoutPanel Column(int rows, int padding) => new()
    {
        Dock = DockStyle.Fill, ColumnCount = 1, RowCount = rows, Padding = new Padding(padding)
    };

    private static TabPage CreateFeatureTab(string title, string state, string description)
    {
        var tab = new TabPage(title) { BackColor = Color.White };
        var content = Column(2, 18);
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.Controls.Add(new Label { Text = state, AutoSize = true }, 0, 0);
        content.Controls.Add(new Label { Text = description, Dock = DockStyle.Fill }, 0, 1);
        tab.Controls.Add(content);
        return tab;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            tray.Visible = false;
            tray.ContextMenuStrip?.Dispose();
            tray.Dispose();
            timer.Dispose();
            picture.Image?.Dispose();
        }
        base.Dispose(disposing);
    }
}
