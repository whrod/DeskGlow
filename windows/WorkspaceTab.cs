using System.Text.Json;

namespace DeskGlow;

internal sealed class WorkspaceTab : TabPage
{
    private readonly Workspace workspace;
    private readonly ComboBox presets = new() { Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox name = new() { Width = 215, MaxLength = 32, PlaceholderText = "작업 배치 이름" };
    private readonly CheckedListBox windows = new() { Dock = DockStyle.Fill, CheckOnClick = true };
    private readonly Label feedback = new() { AutoSize = true, MaximumSize = new Size(550, 0) };
    private readonly Label result = new() { AutoSize = true, MaximumSize = new Size(550, 0) };
    private readonly Button capture = new() { Text = "현재 창 가져오기", AutoSize = true };
    private readonly Button save = new() { Text = "저장", AutoSize = true };
    private readonly Button create = new() { Text = "새 배치", AutoSize = true };
    private readonly Button restore = new() { Text = "열기", AutoSize = true };
    private readonly Button delete = new() { Text = "삭제", AutoSize = true };
    private string? editingId;
    private bool busy;

    public WorkspaceTab(Workspace workspace) : base("작업 배치")
    {
        this.workspace = workspace;
        BackColor = Color.White;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(14) };
        for (var row = 0; row < 3; row++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = "저장한 작업 배치는 갤럭시에 이름별 버튼으로 표시됩니다.", AutoSize = true }, 0, 0);
        layout.Controls.Add(Row(presets, create, restore, delete), 0, 1);
        layout.Controls.Add(Row(name, capture, save), 0, 2);
        layout.Controls.Add(windows, 0, 3);
        layout.Controls.Add(feedback, 0, 4);
        layout.Controls.Add(result, 0, 5);
        Controls.Add(layout);

        presets.SelectionChangeCommitted += (_, _) => OpenPreset(presets.SelectedItem as Workspace.Preset);
        create.Click += (_, _) => { presets.SelectedIndex = -1; OpenPreset(null); name.Focus(); };
        capture.Click += async (_, _) => await CaptureAsync();
        save.Click += (_, _) => Save();
        delete.Click += (_, _) => Delete();
        restore.Click += (_, _) =>
        {
            if (editingId is null) return;
            feedback.Text = workspace.Request(editingId) switch
            {
                202 => "저장한 배치를 복원하고 있습니다.",
                409 => "이미 다른 요청을 처리 중입니다.",
                _ => "읽을 수 있는 저장 배치가 없습니다."
            };
            RefreshStatus();
        };
        TryEdit(() => Reload(null, selectFirst: true));
    }

    public void RefreshStatus() => result.Text = workspace.Status;

    private void Reload(string? id, bool selectFirst = false)
    {
        var saved = workspace.LoadPresets();
        presets.DisplayMember = nameof(Workspace.Preset.Name);
        presets.Items.Clear();
        presets.Items.AddRange(saved);
        var selected = Array.FindIndex(saved, preset => preset.Id == id);
        presets.SelectedIndex = selected < 0 && selectFirst && saved.Length > 0 ? 0 : selected;
        OpenPreset(presets.SelectedItem as Workspace.Preset);
    }

    private void OpenPreset(Workspace.Preset? preset)
    {
        editingId = preset?.Id;
        name.Text = preset?.Name ?? "";
        SetEntries(preset?.Entries ?? []);
        feedback.Text = preset is null ? "이름을 입력하고 현재 창을 가져오세요." : $"저장된 창 {preset.Entries.Length}개";
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        presets.Enabled = create.Enabled = capture.Enabled = !busy;
        save.Enabled = !busy && windows.Items.Count > 0;
        restore.Enabled = delete.Enabled = !busy && editingId is not null;
    }

    private async Task CaptureAsync()
    {
        busy = true;
        UpdateButtons();
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
            busy = false;
            if (!IsDisposed) UpdateButtons();
        }
    }

    private void SetEntries(Workspace.Entry[] entries)
    {
        windows.Items.Clear();
        foreach (var entry in entries) windows.Items.Add(entry, true);
    }

    private void Save() => TryEdit(() =>
    {
        var selected = windows.CheckedItems.Cast<Workspace.Entry>().ToArray();
        var saved = workspace.SavePreset(editingId, name.Text, selected);
        Reload(saved.Id);
        feedback.Text = $"‘{saved.Name}’ 저장 완료 · 창 {selected.Length}개";
    });

    private void Delete()
    {
        if (editingId is not string id || MessageBox.Show(FindForm(), $"‘{name.Text}’ 작업 배치를 삭제할까요?",
            "DeskGlow", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        TryEdit(() => { workspace.DeletePreset(id); Reload(null, selectFirst: true); });
    }

    private void TryEdit(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            feedback.Text = error.Message;
        }
    }

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        row.Controls.AddRange(controls);
        return row;
    }
}
