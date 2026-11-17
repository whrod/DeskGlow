using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;
using Microsoft.Win32;

namespace DeskGlow;

internal sealed class Workspace
{
    internal sealed record Entry(string App, string Title, string? Url, string Display,
        int X, int Y, int Width, int Height, bool Maximized, bool Administrator)
    {
        public override string ToString() =>
            $"{App}{(Administrator ? " · 관리자" : "")} | {Display} | {Title}";
    }

    private sealed record Window(nint Handle, string App, string Title, bool Administrator);
    internal sealed record Preset(string Id, string Name, Entry[] Entries);
    public const int MaxPresets = 24;
    private readonly string path;
    private readonly string catalogPath;
    private readonly object catalogLock = new();
    private string? activePreset;
    private int restoring;
    private string status = "";
    public string Status => Volatile.Read(ref status);

    public Workspace(string? file = null)
    {
        path = file ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DeskGlow", "workspace.json");
        catalogPath = Path.Combine(Path.GetDirectoryName(path)!, "workspaces.json");
    }

    public Preset[] LoadPresets()
    {
        lock (catalogLock)
        {
            if (File.Exists(catalogPath))
                return JsonSerializer.Deserialize<Preset[]>(File.ReadAllText(catalogPath)) ?? [];
            var legacy = File.Exists(path) ? JsonSerializer.Deserialize<Entry[]>(File.ReadAllText(path)) : null;
            return legacy is { Length: > 0 } ? [new Preset("workspace", "기존 작업 배치", legacy)] : [];
        }
    }

    public Preset SavePreset(string? id, string name, Entry[] entries)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 32 || entries.Length == 0)
            throw new InvalidOperationException("이름을 1~32자로 입력하고 창을 하나 이상 선택하세요.");
        lock (catalogLock)
        {
            var presets = LoadPresets().ToList();
            if (presets.Any(preset => preset.Id != id && string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("같은 이름의 작업 배치가 있습니다.");
            var index = presets.FindIndex(preset => preset.Id == id);
            if (index < 0 && presets.Count >= MaxPresets)
                throw new InvalidOperationException($"작업 배치는 최대 {MaxPresets}개까지 저장할 수 있습니다.");
            var saved = new Preset(id ?? $"workspace-{Guid.NewGuid():N}", name, entries);
            if (index < 0) presets.Add(saved);
            else presets[index] = saved;
            WritePresets(presets);
            return saved;
        }
    }

    public void DeletePreset(string id)
    {
        lock (catalogLock)
            WritePresets(LoadPresets().Where(preset => preset.Id != id).ToArray());
    }

    private void WritePresets(IEnumerable<Preset> presets)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(catalogPath)!);
        var temporary = catalogPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(presets));
            File.Move(temporary, catalogPath, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    public (Entry[] Entries, int MissingAddresses) Capture()
    {
        var entries = new List<Entry>();
        var missing = 0;
        foreach (var window in FindWindows())
        {
            if (IsIconic(window.Handle) || !GetWindowRect(window.Handle, out var bounds))
                continue;
            var url = window.App == "Chrome" ? ReadChromeAddress(window.Handle) : null;
            if (window.App == "Chrome" && url is null)
            {
                missing++;
                continue;
            }
            var screen = Screen.FromHandle(window.Handle);
            entries.Add(new Entry(window.App, window.Title, url, screen.DeviceName,
                bounds.Left - screen.WorkingArea.Left, bounds.Top - screen.WorkingArea.Top,
                bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, IsZoomed(window.Handle), window.Administrator));
        }
        return (entries.ToArray(), missing);
    }

    public int Request(string action)
    {
        if (Interlocked.CompareExchange(ref restoring, 1, 0) != 0) return 409;
        Entry[] entries;
        try
        {
            var presets = action == "chatgpt" ? [] : LoadPresets();
            var selected = action switch
            {
                "chatgpt" => null,
                "chrome" => presets.FirstOrDefault(preset => preset.Id == activePreset) ?? presets.FirstOrDefault(),
                "workspace" => presets.FirstOrDefault(preset => preset.Id == action) ?? presets.FirstOrDefault(),
                _ => presets.FirstOrDefault(preset => preset.Id == action)
            };
            entries = selected?.Entries.Where(entry => action != "chrome" || entry.App == "Chrome").ToArray() ?? [];
            if (action is not ("chrome" or "chatgpt") && entries.Length > 0)
                activePreset = selected?.Id;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            Volatile.Write(ref status, "저장된 배치를 읽지 못했습니다. Windows에서 다시 저장하세요.");
            Interlocked.Exchange(ref restoring, 0);
            return 422;
        }
        if (action != "chatgpt" && entries.Length == 0)
        {
            Interlocked.Exchange(ref restoring, 0);
            return 404;
        }

        Volatile.Write(ref status, action == "chatgpt" ? "ChatGPT 여는 중…" : "창 복원 중…");
        _ = Task.Run(async () =>
        {
            try
            {
                if (action == "chatgpt")
                {
                    var existing = FindWindows().FirstOrDefault(window => window.App == "ChatGPT");
                    if (existing is not null)
                    {
                        if (IsIconic(existing.Handle)) ShowWindowAsync(existing.Handle, 9);
                        if (!SetForegroundWindow(existing.Handle)) LaunchChatGpt();
                    }
                    else
                        LaunchChatGpt();
                    Volatile.Write(ref status, "ChatGPT 열기 요청 완료");
                }
                else
                    await RestoreAsync(entries);
            }
            catch (Exception error)
            {
                Volatile.Write(ref status, $"실행 실패 · {error.Message}");
            }
            finally { Interlocked.Exchange(ref restoring, 0); }
        });
        return 202;
    }

    private async Task RestoreAsync(Entry[] entries)
    {
        var used = new HashSet<nint>();
        var failures = new List<string>();
        var mixedChromeWindows = 0;
        var chromePrepared = false;
        foreach (var entry in entries)
        {
            try
            {
                var available = FindWindows();
                var matching = available.Where(window => window.App == entry.App &&
                    window.Administrator == entry.Administrator && !used.Contains(window.Handle));
                var target = entry.App == "Chrome"
                    ? matching.Where(window => SameChromePage(ReadChromeAddress(window.Handle), entry.Url))
                        .OrderBy(window => ReadChromeTabCount(window.Handle) != 1)
                        .ThenBy(window => Screen.FromHandle(window.Handle).DeviceName != entry.Display)
                        .FirstOrDefault()
                    : matching.FirstOrDefault(window => window.Title == entry.Title);
                if (target is null && entry.App != "Chrome")
                    target = matching.FirstOrDefault();
                if (entry.App == "PowerShell" && entry.Administrator)
                {
                    if (target is not null) used.Add(target.Handle);
                    await RestoreElevatedWindowAsync(target?.Handle ?? 0, entry);
                    continue;
                }
                if (target is null && entry.App == "Chrome" && !chromePrepared)
                {
                    chromePrepared = true;
                    if (!available.Any(window => window.App == "Chrome"))
                    {
                        await PrepareChromeAsync();
                        available = FindWindows();
                        target = available.FirstOrDefault(window => window.App == "Chrome" &&
                            !used.Contains(window.Handle) && SameChromePage(ReadChromeAddress(window.Handle), entry.Url));
                    }
                }
                if (target is null)
                {
                    var existing = available.Select(window => window.Handle).ToHashSet();
                    Launch(entry);
                    for (var attempt = 0; attempt < 30 && target is null; attempt++)
                    {
                        await Task.Delay(200);
                        target = FindWindows().FirstOrDefault(window => window.App == entry.App &&
                            window.Administrator == entry.Administrator && !existing.Contains(window.Handle) &&
                            (entry.App != "Chrome" || SameChromePage(ReadChromeAddress(window.Handle), entry.Url)));
                    }
                    if (target is null) throw new InvalidOperationException("새 창을 찾지 못했습니다.");
                }
                used.Add(target.Handle);
                await PlaceAsync(target.Handle, entry);
                if (entry.App == "Chrome" && ReadChromeTabCount(target.Handle) > 1)
                    mixedChromeWindows++;
            }
            catch (Exception error)
            {
                failures.Add($"{entry.App} · {error.Message}");
            }
        }
        var result = failures.Count == 0 ? $"창 {entries.Length}개 복원 완료" :
            $"창 {entries.Length - failures.Count}/{entries.Length}개 복원 · {string.Join(" / ", failures)}";
        if (mixedChromeWindows > 0)
            result += $" · Chrome 창 {mixedChromeWindows}개에 추가 탭이 있습니다. 필요한 탭만 남겨 주세요.";
        Volatile.Write(ref status, result);
    }

    private static async Task PrepareChromeAsync()
    {
        // A cold launch with URLs can merge them with Chrome's restored session tabs.
        var start = new ProcessStartInfo(FindChrome()) { UseShellExecute = true };
        start.ArgumentList.Add("--no-startup-window");
        using var process = Process.Start(start);
        if (process is null) return;
        try { process.WaitForInputIdle(3000); }
        catch (InvalidOperationException) { }
        await Task.Delay(300);
    }

    private static async Task PlaceAsync(nint handle, Entry entry)
    {
        var screen = Screen.AllScreens.FirstOrDefault(screen => screen.DeviceName == entry.Display)
            ?? Screen.PrimaryScreen!;
        var area = screen.WorkingArea;
        // Win32 frame bounds can extend a few pixels beyond a snapped window's visible edges.
        const int frame = 12;
        var width = Math.Clamp(entry.Width, 1, area.Width + 2 * frame);
        var height = Math.Clamp(entry.Height, 1, area.Height + 2 * frame);
        var x = area.Left + Math.Clamp(entry.X, -frame, area.Width + frame - width);
        var y = area.Top + Math.Clamp(entry.Y, -frame, area.Height + frame - height);
        if (IsIconic(handle) || IsZoomed(handle))
        {
            ShowWindowAsync(handle, 9);
            for (var attempt = 0; attempt < 10 && (IsIconic(handle) || IsZoomed(handle)); attempt++)
                await Task.Delay(50);
        }
        if (entry.Maximized)
        {
            MoveWindow(handle, x, y, width, height, raise: true);
            for (var attempt = 0; attempt < 10; attempt++)
            {
                await Task.Delay(100);
                if (Screen.FromHandle(handle).DeviceName != screen.DeviceName)
                {
                    MoveWindow(handle, x, y, width, height, raise: false);
                    continue;
                }
                if (IsZoomed(handle)) return;
                ShowWindowAsync(handle, 3);
            }
            throw new InvalidOperationException("저장된 모니터에서 창을 최대화하지 못했습니다.");
        }

        // Chrome may resize again when it processes a DPI change on the destination monitor.
        MoveWindow(handle, x, y, width, height, raise: true);
        var stable = 0;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            await Task.Delay(150);
            if (GetWindowRect(handle, out var actual) &&
                Math.Abs(actual.Left - x) <= 4 && Math.Abs(actual.Top - y) <= 4 &&
                Math.Abs(actual.Right - x - width) <= 4 && Math.Abs(actual.Bottom - y - height) <= 4)
            {
                if (++stable == 2) return;
                continue;
            }
            stable = 0;
            MoveWindow(handle, x, y, width, height, raise: false);
        }
        throw new InvalidOperationException("창이 저장된 위치 또는 크기를 유지하지 못했습니다.");
    }

    private static void MoveWindow(nint handle, int x, int y, int width, int height, bool raise)
    {
        const uint noActivate = 0x0010;
        const uint noZOrder = 0x0004;
        if (!SetWindowPos(handle, 0, x, y, width, height, noActivate | (raise ? 0 : noZOrder)))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private static bool SameChromePage(string? current, string? saved)
    {
        if (current is null || saved is null) return false;
        if (string.Equals(current, saved, StringComparison.Ordinal)) return true;
        if (!Uri.TryCreate(current, UriKind.Absolute, out var first) ||
            !Uri.TryCreate(saved, UriKind.Absolute, out var second))
            return false;
        return IsGoogleHome(first) && IsGoogleHome(second) ||
            IsYouTubeHome(first) && IsYouTubeHome(second);
    }

    private static bool IsGoogleHome(Uri uri) =>
        uri.Host is "google.com" or "www.google.com" && uri.AbsolutePath is "/" or "/webhp" &&
        uri.Fragment.Length == 0 && !Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query).ContainsKey("q");

    private static bool IsYouTubeHome(Uri uri) =>
        uri.Host is "youtube.com" or "www.youtube.com" && uri.AbsolutePath == "/" &&
        uri.Query.Length == 0 && uri.Fragment.Length == 0;

    private static async Task RestoreElevatedWindowAsync(nint handle, Entry entry)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("DeskGlow 실행 경로를 찾지 못했습니다.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas" };
        foreach (var argument in new[] { "--restore-admin-powershell", handle.ToInt64().ToString(CultureInfo.InvariantCulture),
            entry.Display, entry.X.ToString(CultureInfo.InvariantCulture), entry.Y.ToString(CultureInfo.InvariantCulture),
            entry.Width.ToString(CultureInfo.InvariantCulture), entry.Height.ToString(CultureInfo.InvariantCulture),
            entry.Maximized ? "1" : "0" })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("관리자 PowerShell 복원 도우미를 실행하지 못했습니다.");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException("관리자 PowerShell 창을 복원하지 못했습니다.");
    }

    internal static async Task RestoreAdministratorPowerShellAsync(string[] args)
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator) ||
            args.Length != 8 || !long.TryParse(args[1], out var savedHandle) ||
            !int.TryParse(args[3], out var x) || !int.TryParse(args[4], out var y) ||
            !int.TryParse(args[5], out var width) || !int.TryParse(args[6], out var height) ||
            width <= 0 || height <= 0 || args[7] is not ("0" or "1"))
            throw new InvalidOperationException("관리자 PowerShell 복원 요청을 확인할 수 없습니다.");

        var existing = FindWindows();
        var target = existing.FirstOrDefault(window => window.Handle == (nint)savedHandle &&
            window.App == "PowerShell" && window.Administrator);
        if (target is null)
        {
            LaunchPowerShell();
            var before = existing.Select(window => window.Handle).ToHashSet();
            for (var attempt = 0; attempt < 30 && target is null; attempt++)
            {
                await Task.Delay(200);
                target = FindWindows().FirstOrDefault(window => window.App == "PowerShell" &&
                    window.Administrator && !before.Contains(window.Handle));
            }
        }
        if (target is null) throw new InvalidOperationException("관리자 PowerShell 창을 찾지 못했습니다.");
        await PlaceAsync(target.Handle, new Entry("PowerShell", "", null, args[2], x, y,
            width, height, args[7] == "1", Administrator: true));
    }

    private static void LaunchPowerShell()
    {
        var executable = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoExit");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("$Host.UI.RawUI.BackgroundColor = 'Black'; $Host.UI.RawUI.ForegroundColor = 'Gray'; Clear-Host");
        using var process = Process.Start(start);
    }

    private static void Launch(Entry entry)
    {
        switch (entry.App)
        {
            case "Chrome" when NormalizeUrl(entry.Url) is string url:
                var executable = FindChrome();
                LaunchProcess(executable, "--new-window", url);
                break;
            case "ChatGPT":
                LaunchChatGpt();
                break;
            case "PowerShell":
                LaunchPowerShell();
                break;
            default:
                throw new InvalidOperationException("지원하지 않는 창입니다.");
        }
    }

    private static void LaunchChatGpt()
    {
        // Prefer the installed desktop package on this PC; the standalone package uses the older family name.
        var packageRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
        var family = Directory.Exists(Path.Combine(packageRoot, "OpenAI.Codex_2p2nqsd0c76g0"))
            ? "OpenAI.Codex_2p2nqsd0c76g0" : "OpenAI.ChatGPT-Desktop_2p2nqsd0c76g0";
        LaunchProcess("explorer.exe", $"shell:AppsFolder\\{family}!App");
    }

    private static string FindChrome()
    {
        const string key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe";
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var path = root.OpenSubKey(key);
            if (path?.GetValue(null) is string executable && File.Exists(executable))
                return executable;
        }
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles,
            Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.LocalApplicationData })
        {
            var executable = Path.Combine(Environment.GetFolderPath(folder), "Google", "Chrome", "Application", "chrome.exe");
            if (File.Exists(executable)) return executable;
        }
        throw new InvalidOperationException("Chrome 설치를 찾지 못했습니다.");
    }

    private static void LaunchProcess(string executable, params string[] args)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = true };
        foreach (var argument in args) start.ArgumentList.Add(argument);
        using var process = Process.Start(start);
    }

    private static List<Window> FindWindows()
    {
        var result = new List<Window>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle) || GetWindow(handle, 4) != 0 ||
                (DwmGetWindowAttribute(handle, 14, out var cloaked, sizeof(int)) == 0 && cloaked != 0))
                return true;
            var title = new StringBuilder(1024);
            if (GetWindowText(handle, title, title.Capacity) == 0) return true;
            var name = title.ToString();
            GetWindowThreadProcessId(handle, out var processId);
            try
            {
                using var process = Process.GetProcessById((int)processId);
                var app = process.ProcessName.ToLowerInvariant() switch
                {
                    "chrome" => "Chrome",
                    "chatgpt" or "codex" => "ChatGPT",
                    "windowsterminal" or "openconsole" or "conhost" or "powershell" or "pwsh"
                        when name.Contains("PowerShell", StringComparison.OrdinalIgnoreCase) => "PowerShell",
                    _ => null
                };
                if (app is not null)
                    result.Add(new Window(handle, app, name, app == "PowerShell" &&
                        (name.Contains("관리자") || name.Contains("Administrator", StringComparison.OrdinalIgnoreCase))));
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception) { }
            return true;
        }, 0);
        return result;
    }

    internal static nint[] FindClosableWindows()
    {
        const int extendedStyle = -20;
        const int toolWindow = 0x80;
        const int appWindow = 0x40000;
        const int noActivate = 0x08000000;
        const uint owner = 4;
        var candidates = new Dictionary<nint, bool>();
        var shell = GetShellWindow();

        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle) || handle == shell ||
                (DwmGetWindowAttribute(handle, 14, out var cloaked, sizeof(int)) == 0 && cloaked != 0))
                return true;

            var style = GetWindowLong(handle, extendedStyle);
            bool explicitlyListed = (style & appWindow) != 0;
            if (!explicitlyListed && ((style & (toolWindow | noActivate)) != 0 || GetWindowTextLength(handle) == 0))
                return true;

            GetWindowThreadProcessId(handle, out var pid);
            if (pid == Environment.ProcessId) return true;

            var className = new StringBuilder(128);
            GetClassName(handle, className, className.Capacity);
            if (className.ToString() is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd"))
                candidates.Add(handle, explicitlyListed);
            return true;
        }, 0);

        // An owned window normally shares its owner's Alt+Tab entry. Keep it when
        // it has its own entry or when no visible application window owns that group.
        var result = new List<nint>();
        foreach (var (handle, explicitlyListed) in candidates)
        {
            var parent = explicitlyListed ? 0 : GetWindow(handle, owner);
            while (parent != 0 && !candidates.ContainsKey(parent))
                parent = GetWindow(parent, owner);
            if (parent == 0) result.Add(handle);
        }
        return result.ToArray();
    }

    internal static (int Requested, int Blocked, nint[] AdministratorPowerShell) CloseApplicationWindows()
    {
        const uint wmClose = 0x0010;
        var windows = FindClosableWindows();
        var administrator = FindWindows().Where(window => window.App == "PowerShell" && window.Administrator)
            .Select(window => window.Handle).ToHashSet();
        var awaitingApproval = new List<nint>();
        var requested = 0;
        foreach (var handle in windows)
        {
            if (PostMessage(handle, wmClose, 0, 0))
                requested++;
            else if (Marshal.GetLastWin32Error() == 5 && administrator.Contains(handle))
                awaitingApproval.Add(handle);
        }
        return (requested, windows.Length - requested - awaitingApproval.Count, awaitingApproval.ToArray());
    }

    internal static async Task CloseElevatedPowerShellAsync(nint[] handles)
    {
        if (handles.Length == 0) return;
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("DeskGlow 실행 경로를 찾지 못했습니다.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas" };
        start.ArgumentList.Add("--close-admin-powershell");
        foreach (var handle in handles)
            start.ArgumentList.Add(handle.ToInt64().ToString(CultureInfo.InvariantCulture));
        try
        {
            using var process = Process.Start(start);
            if (process is not null) await process.WaitForExitAsync();
        }
        catch (Win32Exception error) when (error.NativeErrorCode == 1223)
        {
            // The user dismissed the Windows approval prompt.
        }
    }

    internal static void CloseAdministratorPowerShell(string[] args)
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new InvalidOperationException("관리자 PowerShell을 닫으려면 Windows 승인이 필요합니다.");
        var targets = args.Skip(1).Select(arg => long.TryParse(arg, NumberStyles.None, CultureInfo.InvariantCulture,
            out var handle) ? (nint)handle : 0).ToHashSet();
        var eligible = FindClosableWindows().ToHashSet();
        foreach (var window in FindWindows())
            if (window.App == "PowerShell" && window.Administrator && targets.Contains(window.Handle) &&
                eligible.Contains(window.Handle))
                PostMessage(window.Handle, 0x0010, 0, 0);
    }

    private static string? ReadChromeAddress(nint handle)
    {
        try
        {
            var root = AutomationElement.FromHandle(handle);
            var edit = root.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                new OrCondition(
                    new PropertyCondition(AutomationElement.ClassNameProperty, "OmniboxViewViews"),
                    new PropertyCondition(AutomationElement.AutomationIdProperty, "addressEditBox"))));
            if (edit?.TryGetCurrentPattern(ValuePattern.Pattern, out var value) == true)
                return NormalizeUrl(((ValuePattern)value).Current.Value);
        }
        catch (Exception error) when (error is COMException or InvalidOperationException or ArgumentException) { }
        return null;
    }

    private static int ReadChromeTabCount(nint handle)
    {
        try
        {
            var root = AutomationElement.FromHandle(handle);
            var tabs = root.FindFirst(TreeScope.Descendants, new PropertyCondition(
                AutomationElement.ClassNameProperty, "TabContainerImpl"));
            return tabs?.FindAll(TreeScope.Children, new PropertyCondition(
                AutomationElement.ControlTypeProperty, ControlType.TabItem)).Count ?? 0;
        }
        catch (Exception error) when (error is COMException or InvalidOperationException or ArgumentException)
        {
            return 0;
        }
    }

    private static string? NormalizeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (!text.Contains("://", StringComparison.Ordinal) && text.Contains('.') && !text.Any(char.IsWhiteSpace))
            text = "https://" + text;
        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" &&
            uri.Host.Length > 0 && uri.UserInfo.Length == 0 ? uri.AbsoluteUri : null;
    }

    private delegate bool EnumerateWindow(nint handle, nint data);
    [StructLayout(LayoutKind.Sequential)]
    private struct Bounds { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumerateWindow callback, nint data);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint handle);
    [DllImport("user32.dll")] private static extern nint GetShellWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowLong(nint handle, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(nint handle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint handle, StringBuilder name, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool PostMessage(nint handle, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint handle);
    [DllImport("user32.dll")] private static extern bool IsZoomed(nint handle);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint handle, uint command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint handle, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint handle, out uint process);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint handle, out Bounds rect);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(nint handle, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint handle);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint handle, int attribute, out int value, int size);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint handle, nint after, int x, int y, int width, int height, uint flags);
}
