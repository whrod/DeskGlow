using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace DeskGlow;

internal sealed class DeveloperTab : TabPage
{
    private readonly TextBox adb = new() { Width = 365 };
    private readonly TextBox endpoint = new() { Width = 205, PlaceholderText = "갤럭시 IP:연결 포트" };
    private readonly ComboBox devices = new() { Width = 315, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox apk = new() { Width = 365, ReadOnly = true };
    private readonly Label installed = new() { AutoSize = true };
    private readonly TextBox output = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly TableLayoutPanel panel = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(14) };
    private string? builds;
    private bool selectedApkManually;

    public DeveloperTab() : base("개발용")
    {
        BackColor = Color.White;
        for (var row = 0; row < 6; row++)
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label
        {
            Text = "갤럭시의 무선 디버깅을 켜고 같은 Wi-Fi에 연결하세요.",
            AutoSize = true
        }, 0, 0);
        panel.Controls.Add(Row(adb, Button("ADB 선택", () => PickFile(adb, "ADB|adb.exe"))), 0, 1);
        panel.Controls.Add(Row(endpoint, Button("연결", () => RunAsync(ConnectAsync)),
            Button("기기 검색", () => RunAsync(() => RefreshDevicesAsync()))), 0, 2);
        panel.Controls.Add(Row(devices, installed), 0, 3);
        panel.Controls.Add(Row(apk, Button("APK 선택", () => PickFile(apk, "Android APK|*.apk"))), 0, 4);
        panel.Controls.Add(Row(Button("설치 / 업데이트", () => RunAsync(InstallAsync))), 0, 5);
        panel.Controls.Add(output, 0, 6);
        Controls.Add(panel);

        devices.SelectionChangeCommitted += (_, _) => RunAsync(UpdateVersionAsync);
        output.Text = "기기 검색을 누르거나 갤럭시 무선 디버깅 메인 화면의 연결 주소를 입력하세요.";
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder != null; folder = folder.Parent)
        {
            var bundled = Path.Combine(folder.FullName, ".android-sdk", "platform-tools", "adb.exe");
            if (!File.Exists(bundled)) continue;
            adb.Text = bundled;
            builds = Path.Combine(folder.FullName, "android", "app", "build", "outputs", "dist");
            break;
        }
        SelectLatestBuild();
        Enter += (_, _) => SelectLatestBuild();
    }

    private void SelectLatestBuild()
    {
        if (selectedApkManually || !Directory.Exists(builds)) return;
        apk.Text = Directory.EnumerateFiles(builds!, "DeskGlow-Android-*.apk")
            .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() ?? "";
        apk.SelectionStart = apk.TextLength;
        apk.ScrollToCaret();
    }

    private async Task ConnectAsync()
    {
        var target = endpoint.Text.Trim();
        var parts = target.Split(':');
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var address) ||
            address.AddressFamily != AddressFamily.InterNetwork ||
            !ushort.TryParse(parts[1], out var port) || port == 0)
            throw new InvalidOperationException("무선 디버깅 메인 화면의 IPv4 주소와 연결 포트를 입력하세요.");
        var response = await AdbAsync("connect", target);
        await RefreshDevicesAsync(target);
        if (devices.SelectedItem as string != target)
            throw new InvalidOperationException(response + Environment.NewLine + "갤럭시에서 이 PC의 페어링 상태를 확인하세요.");
        output.Text = response;
    }

    private async Task RefreshDevicesAsync(string? preferred = null)
    {
        SelectLatestBuild();
        var listing = await AdbAsync("devices");
        var previous = preferred ?? devices.SelectedItem as string;
        var wireless = listing.Split('\n').Select(line =>
                line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length >= 2 && parts[1] == "device" &&
                (parts[0].Contains(':') || parts[0].Contains("_adb-tls-connect._tcp")))
            .Select(parts => parts[0]).ToArray();
        devices.Items.Clear();
        devices.Items.AddRange(wireless);
        devices.SelectedIndex = Array.IndexOf(wireless, previous);
        if (devices.SelectedIndex < 0 && preferred == null && wireless.Length == 1)
            devices.SelectedIndex = 0;
        output.Text = wireless.Length == 0 ? "연결된 무선 기기가 없습니다. 갤럭시 연결 주소를 입력하세요." : "무선 기기를 선택하고 APK를 설치하세요.";
        await UpdateVersionAsync();
    }

    private async Task UpdateVersionAsync()
    {
        installed.Text = "";
        if (devices.SelectedItem is not string device) return;
        var details = await AdbAsync("-s", device, "shell", "dumpsys", "package", "app.deskglow");
        var version = Regex.Match(details, @"\bversionName=(\S+)");
        installed.Text = version.Success ? $"설치됨: v{version.Groups[1].Value}" : "DeskGlow 미설치";
    }

    private async Task InstallAsync()
    {
        if (devices.SelectedItem is not string device)
            throw new InvalidOperationException("먼저 갤럭시를 연결하고 선택하세요.");
        if (!File.Exists(apk.Text) || !apk.Text.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("설치할 APK 파일을 선택하세요.");
        output.Text = $"설치 중 · {Path.GetFileName(apk.Text)}";
        output.Text = await AdbAsync("-s", device, "install", "-r", apk.Text);
        await UpdateVersionAsync();
    }

    private async Task<string> AdbAsync(params string[] args)
    {
        if (!File.Exists(adb.Text))
            throw new InvalidOperationException("Android SDK의 platform-tools 폴더에서 adb.exe를 선택하세요.");
        var start = new ProcessStartInfo(adb.Text)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("ADB를 시작하지 못했습니다.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill();
            throw new InvalidOperationException("ADB 응답 시간이 초과되었습니다.");
        }
        var result = string.Join(Environment.NewLine, (await stdout).Trim(), (await stderr).Trim()).Trim();
        if (process.ExitCode != 0) throw new InvalidOperationException(result);
        return result;
    }

    private async void RunAsync(Func<Task> action)
    {
        panel.Enabled = false;
        try { await action(); }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            if (!IsDisposed) output.Text = error.Message;
        }
        finally { if (!IsDisposed) panel.Enabled = true; }
    }

    private void PickFile(TextBox target, string filter)
    {
        using var dialog = new OpenFileDialog { Filter = filter, FileName = target.Text };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        target.Text = dialog.FileName;
        if (target == apk) selectedApkManually = true;
    }

    private static Button Button(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += (_, _) => action();
        return button;
    }

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        row.Controls.AddRange(controls);
        return row;
    }
}
