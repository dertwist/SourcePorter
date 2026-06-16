using System.Text.Json;

namespace SourcePorter.App;

/// <summary>
/// Persisted GUI inputs — the SourcePorter equivalent of Valve's
/// <c>import_map_community_gui_cfg.json</c>. Stored per-user under
/// <c>%APPDATA%\SourcePorter\settings.json</c>.
/// </summary>
public sealed class AppSettings
{
    public string Cs2Directory { get; set; } = "";
    public string SourceMap { get; set; } = "";
    public string OutputAddon { get; set; } = "";
    public bool UseBsp { get; set; } = true;
    public bool UseBspNoMergeInstances { get; set; }
    public bool SkipDeps { get; set; }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private static string Path =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SourcePorter", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(Path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path)) ?? new AppSettings();
        }
        catch
        {
            // Corrupt/unreadable settings → start fresh.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, Options));
        }
        catch
        {
            // Non-fatal: settings just won't persist this session.
        }
    }
}
