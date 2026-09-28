using System.IO;
using System.Text.Json;

namespace FlipNoteChrome.Core.Storage;

public sealed class AppSettings
{
    public string Modifier { get; set; } = "Alt"; // Alt, Ctrl, Win
    public string Click { get; set; } = "Left"; // Left, Middle, Double
    public string Hotkey { get; set; } = "Ctrl+Alt+F";
    public int AnimationMs { get; set; } = 350;
    public string FontFamily { get; set; } = "Segoe UI";
    public double FontSize { get; set; } = 14;
    public string StoragePath { get; set; } = "";
    public bool AutoStart { get; set; } = false;
    public bool AlwaysOnTop { get; set; } = true;
    public bool UseTrue3D { get; set; } = true;
    public List<string> ExcludeList { get; set; } = [];

    private static string FilePath
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlipNoteChrome");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "settings.json");
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
