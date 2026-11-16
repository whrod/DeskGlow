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
    private readonly NotifyIcon tray = new() { Text = "DeskGlow" };
    private readonly Icon appIcon;
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

    public Dashboard(int port, string code, Workspace workspace, ButtonNames buttonNames, Func<long> lastRequest, Action<bool> setEnabled)
    {
        this.port = port;
        this.code = code;
        this.lastRequest = lastRequest;
        Text = "DeskGlow";
        using (var iconStream = typeof(Dashboard).Assembly.GetManifestResourceStream("DeskGlow.Icon")!)
        using (var icon = new Icon(iconStream))
            appIcon = (Icon)icon.Clone();
        Icon = appIcon;
        tray.Icon = appIcon;
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
        var namesTab = new ButtonNamesTab(buttonNames);
        tabs.TabPages.Add(namesTab);
        tabs.TabPages.Add(new DeveloperTab());
        tabs.TabPages.Add(CreateHelpTab());
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
            if (tabs.SelectedTab == namesTab) namesTab.RefreshNames();
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

    private static TabPage CreateHelpTab()
    {
        var tab = new TabPage("도움말") { BackColor = Color.White, Padding = new Padding(14) };
        tab.Controls.Add(new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
            BorderStyle = BorderStyle.None, BackColor = Color.White, ScrollBars = ScrollBars.Vertical,
            Text = """
                연결과 QR
                갤럭시와 PC를 같은 Wi-Fi에 연결하세요. Windows 연결 탭에서 PC 주소를 선택하고, 갤럭시에서 연결 설정 → QR로 연결을 누릅니다. 수동 연결에는 주소와 코드 복사를 사용하세요. PC IP가 바뀌면 QR을 다시 스캔합니다.

                연결 상태와 트레이
                앱 상단에서 PC 연결 여부를 확인할 수 있습니다. Windows 창의 X는 창만 숨깁니다. 트레이 아이콘을 더블클릭하면 창을 열고, 우클릭하면 PC 제어를 켜거나 끄거나 DeskGlow를 종료할 수 있습니다.

                작업 배치 만들기
                Windows의 작업 배치 → 새 배치에서 이름을 입력합니다. 현재 창 가져오기를 누르고 원하는 창을 체크한 뒤 저장합니다. 기존 배치는 목록에서 선택해 이름과 창을 수정하거나 삭제할 수 있습니다. 갤럭시에 배치별 버튼이 표시되기까지 최대 약 10초가 걸립니다.

                창 복원
                배치 이름 버튼은 저장된 창을 모두 복원합니다. Chrome은 창마다 활성 탭의 웹 주소를 저장합니다. Chrome 버튼은 마지막으로 요청한 배치의 Chrome 창만 복원하며, 처음에는 목록의 첫 번째 배치를 사용합니다. ChatGPT는 Windows의 ChatGPT/Codex 앱을 엽니다. 관리자 PowerShell을 복원할 때는 PC에서 UAC를 승인하세요.

                버튼 배치와 볼륨
                갤럭시에서 버튼을 길게 누른 채 다른 슬롯에 놓으세요. 버튼이 있는 자리에 놓으면 두 위치가 바뀝니다. 오른쪽 슬라이더를 드래그한 뒤 손을 떼면 PC 볼륨이 적용됩니다. 아래 버튼으로 음소거를 전환합니다.

                화면 꾸미기
                갤럭시에서 꾸미기를 누르세요. 배경을 눌러 사진을 선택하거나 기본 배경으로 돌아갈 수 있습니다. 버튼이나 빈 슬롯을 누르면 이미지를 선택하거나 지울 수 있습니다. 완료를 누르면 버튼이 다시 PC 명령을 실행합니다.

                버튼 이름
                Windows의 버튼 이름 탭이나 갤럭시의 꾸미기 → 버튼 이름 바꾸기에서 이름을 변경할 수 있습니다. 이름은 연결된 PC에 저장되고 양쪽 앱에 반영됩니다.

                절전과 화면 유지
                갤럭시에서 PC 절전을 누르면 바로 절전을 요청합니다. PC를 다시 쓰려면 연결된 키보드의 키를 누르거나 본체 전원 버튼을 짧게 누르세요. 갤럭시에서 DeskGlow가 화면에 떠 있는 동안에는 화면이 자동으로 꺼지지 않습니다.

                창 모두 닫기
                갤럭시에서 창 모두 닫기를 누르고 확인하세요. 저장 확인이 뜨면 PC에서 처리하세요. 관리자 PowerShell을 닫으려면 PC에서 UAC를 승인하세요. DeskGlow는 트레이에서 계속 실행됩니다.

                개발용 · 무선 APK 설치
                갤럭시의 개발자 옵션에서 무선 디버깅을 켭니다. 최초 한 번 PC의 adb pair로 페어링하세요. 이후 개발용 탭에서 기기 검색을 누릅니다. 기기가 안 보이면 갤럭시 무선 디버깅 메인 화면의 IP 주소와 연결 포트를 입력하세요. APK를 확인하고 설치 / 업데이트를 누릅니다.
                """.ReplaceLineEndings()
        });
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
        if (disposing) appIcon?.Dispose();
    }
}
