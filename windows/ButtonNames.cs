using System.Text.Json;

namespace DeskGlow;

internal sealed class ButtonNames(Workspace workspace)
{
    internal sealed record ActionButton(string Id, string DefaultName, string Name)
    {
        public override string ToString() => Name == DefaultName ? Name : $"{Name}  ({DefaultName})";
    }

    private static readonly (string Id, string Name)[] BuiltIns =
    [
        ("chatgpt", "ChatGPT"), ("chrome", "Chrome"), ("site", "Google 열기"),
        ("sleep", "PC 절전"), ("close-windows", "창 모두 닫기")
    ];
    private readonly string path = Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "DeskGlow", "button-names.json");
    private readonly object sync = new();

    public ActionButton[] List()
    {
        var defaults = Defaults();
        lock (sync)
        {
            var names = Read();
            return defaults.Select(action => new ActionButton(action.Id, action.Name,
                names.GetValueOrDefault(action.Id, action.Name))).ToArray();
        }
    }

    public void Save(string id, string name)
    {
        if (!Defaults().Any(action => action.Id == id))
            throw new InvalidOperationException("이 버튼을 찾을 수 없습니다.");
        name = name.Trim();
        if (name.Length > 32 || name.Any(char.IsControl))
            throw new InvalidOperationException("버튼 이름은 줄바꿈 없이 최대 32자입니다.");
        lock (sync)
        {
            var names = Read();
            if (name.Length == 0) names.Remove(id);
            else names[id] = name;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(names));
                File.Move(temp, path, overwrite: true);
            }
            finally { File.Delete(temp); }
        }
    }

    private (string Id, string Name)[] Defaults() =>
        [.. BuiltIns, .. workspace.LoadPresets().Select(preset => (preset.Id, preset.Name))];

    private Dictionary<string, string> Read() => File.Exists(path)
        ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [] : [];
}
