using System.Text.Json;

namespace DeskGlow;

internal sealed class WorkspaceTab : TabPage
{
    private readonly Workspace workspace;
    private readonly CheckedListBox windows = new() { Dock = DockStyle.Fill, CheckOnClick = true };
    private readonly Label feedback = new() { AutoSize = true, MaximumSize = new Size(550, 0) };
    private readonly Label result = new() { AutoSize = true, MaximumSize = new Size(550, 0) };
    private readonly Button capture = new() { Text = "현재 창 가져오기", AutoSize = true };
    private readonly Button save = new() { Text = "체크한 창 저장", AutoSize = true, Enabled = false };

    public WorkspaceTab(Workspace workspace) : base("작업 배치")
    {
        this.workspace = workspace;
        BackColor = Color.White;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(14) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            Text = "Chrome의 활성 탭, ChatGPT/Codex, PowerShell 창을 저장합니다.\n관리자 PowerShell을 복원하려면 PC에서 UAC를 승인하세요.",
            AutoSize = true
        }, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        var restore = new Button { Text = "저장한 배치 열기", AutoSize = true };
        actions.Controls.AddRange([capture, save, restore]);
        layout.Controls.Add(actions, 0, 1);
        layout.Controls.Add(windows, 0, 2);
        layout.Controls.Add(feedback, 0, 3);
        layout.Controls.Add(result, 0, 4);
        Controls.Add(layout);
        capture.Click += async (_, _) => await CaptureAsync();
        save.Click += (_, _) => Save();
        restore.Click += (_, _) =>
        {
            feedback.Text = workspace.Request("workspace") switch
            {
                202 => "저장한 배치를 복원하고 있습니다.",
                409 => "이미 다른 요청을 처리 중입니다.",
                _ => "읽을 수 있는 저장 배치가 없습니다."
            };
            RefreshStatus();
        };
        try
        {
            var saved = workspace.Load();
            SetEntries(saved);
            feedback.Text = saved.Length == 0 ? "현재 창을 가져와 저장하세요." : $"저장된 창 {saved.Length}개";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            feedback.Text = "저장된 배치를 읽지 못했습니다. 현재 창을 다시 저장하세요.";
        }
    }

    public void RefreshStatus() => result.Text = workspace.Status;

    private async Task CaptureAsync()
    {
        capture.Enabled = save.Enabled = false;
        feedback.Text = "열린 창을 확인하는 중…";
        try
        {
            var snapshot = await Task.Run(workspace.Capture);
            if (IsDisposed) return;
            SetEntries(snapshot.Entries);
            feedback.Text = $"창 {snapshot.Entries.Length}개를 찾았습니다. 저장할 창을 체크하세요.";
            if (snapshot.MissingAddresses > 0)
                feedback.Text += $" Chrome 창 {snapshot.MissingAddresses}개는 주소를 읽지 못했습니다.";
        }
        catch (Exception error)
        {
            if (!IsDisposed) feedback.Text = $"창을 가져오지 못했습니다. {error.Message}";
        }
        finally
        {
            if (!IsDisposed)
            {
                capture.Enabled = true;
                save.Enabled = windows.Items.Count > 0;
            }
        }
    }

    private void SetEntries(Workspace.Entry[] entries)
    {
        windows.Items.Clear();
        foreach (var entry in entries) windows.Items.Add(entry, true);
        save.Enabled = entries.Length > 0;
    }

    private void Save()
    {
        var selected = windows.CheckedItems.Cast<Workspace.Entry>().ToArray();
        if (selected.Length == 0)
        {
            feedback.Text = "저장할 창을 하나 이상 체크하세요.";
            return;
        }
        try
        {
            workspace.Save(selected);
            feedback.Text = $"창 {selected.Length}개를 저장했습니다. 갤럭시에서 작업 배치를 누르세요.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            feedback.Text = $"저장하지 못했습니다. {error.Message}";
        }
    }
}
