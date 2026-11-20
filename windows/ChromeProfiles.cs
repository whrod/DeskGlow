using System.Text.Json;

namespace DeskGlow;

internal static class ChromeProfiles
{
    internal sealed record Profile(string? Directory, string Name)
    {
        public override string ToString() => Name;
    }

    internal static Profile[] List()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Google", "Chrome", "User Data", "Local State");
        if (!File.Exists(path)) return [];
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var json = JsonDocument.Parse(file);
        return json.RootElement.GetProperty("profile").GetProperty("info_cache").EnumerateObject()
            .Select(item => new Profile(item.Name,
                item.Value.GetProperty("name").GetString() + " · " +
                (item.Value.TryGetProperty("user_name", out var email) ? email.GetString() : "")))
            .OrderBy(profile => profile.Name).ToArray();
    }
}
