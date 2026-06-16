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

    /// <summary>Compile imported asset sources (materials/models/refs) to _c — off by default for speed.</summary>
    public bool CompileAssets { get; set; }

    /// <summary>Collapse the toolchain's verbose per-asset output into concise lines — on by default.</summary>
    public bool CompactLog { get; set; } = true;

    /// <summary>"VMF" or "BSP" — the source map input type.</summary>
    public string InputMode { get; set; } = "VMF";

    /// <summary>Max concurrent tool processes for the dependency phase.</summary>
    public int Threads { get; set; } = 4;

    /// <summary>Compile the imported .vmap to .vmap_c after import (slow lighting bake).</summary>
    public bool CompileMap { get; set; }

    /// <summary>Unpack a .bsp's embedded materials/models when decompiling it.</summary>
    public bool UnpackEmbedded { get; set; } = true;

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
