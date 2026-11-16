namespace DeskGlow;

internal sealed class ButtonNamesTab : TabPage
{
    private readonly ButtonNames names;
    private readonly ComboBox buttons = new() { Width = 280, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox newName = new() { Width = 240, MaxLength = 32 };
    private readonly Label feedback = new() { AutoSize = true };
    private ButtonNames.ActionButton[] shown = [];

    public ButtonNamesTab(ButtonNames names) : base("버튼 이름")
    {
        this.names = names;
        BackColor = Color.White;
        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, Padding = new Padding(15)
        };
        layout.Controls.Add(new Label { Text = "수정한 이름은 연결된 갤럭시에 표시됩니다.", AutoSize = true });
        layout.Controls.Add(buttons);
        layout.Controls.Add(newName);
        var actions = new FlowLayoutPanel { AutoSize = true };
        var save = new Button { Text = "이름 저장", AutoSize = true };
        var reset = new Button { Text = "기본 이름", AutoSize = true };
        actions.Controls.AddRange([save, reset]);
        layout.Controls.Add(actions);
        layout.Controls.Add(feedback);
        Controls.Add(layout);
        buttons.SelectionChangeCommitted += (_, _) => ShowName();
        save.Click += (_, _) => Save(newName.Text);
        reset.Click += (_, _) => Save("");
        Enter += (_, _) => RefreshNames();
    }

    public void RefreshNames(bool force = false)
    {
        if (!force && newName.Focused) return;
        try
        {
            var current = names.List();
            if (!force && shown.SequenceEqual(current)) return;
            var selected = (buttons.SelectedItem as ButtonNames.ActionButton)?.Id;
            shown = current;
            buttons.Items.Clear();
            buttons.Items.AddRange(current);
            var index = Array.FindIndex(current, item => item.Id == selected);
            if (current.Length > 0) buttons.SelectedIndex = Math.Max(0, index);
            ShowName();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            feedback.Text = "버튼 목록을 읽지 못했습니다.";
        }
    }

    private void ShowName()
    {
        newName.Text = (buttons.SelectedItem as ButtonNames.ActionButton)?.Name ?? "";
        feedback.Text = "";
    }

    private void Save(string value)
    {
        if (buttons.SelectedItem is not ButtonNames.ActionButton button) return;
        try
        {
            names.Save(button.Id, value);
            RefreshNames(force: true);
            feedback.Text = "저장했습니다.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
        {
            feedback.Text = error.Message;
        }
    }
}
