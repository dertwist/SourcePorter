namespace SourcePorter.Core.Domain;

/// <summary>
/// Flags passed through to <c>source1import</c>, mirroring the switches exposed
/// by Valve's <c>import_map_community.py</c> GUI.
/// </summary>
public sealed class ImportOptions
{
    /// <summary>
    /// Generate and use a BSP on import (runs the map through <c>vbsp</c> for clean
    /// geometry). Highly recommended. Mutually exclusive with
    /// <see cref="UseBspNoMergeInstances"/>.
    /// </summary>
    public bool UseBsp { get; set; } = true;

    /// <summary>Like <see cref="UseBsp"/> but keeps func_instance hierarchy (no merge).</summary>
    public bool UseBspNoMergeInstances { get; set; }

    /// <summary>Only produce the .vmap(s); skip importing/compiling dependencies.</summary>
    public bool SkipDeps { get; set; }

    /// <summary>Tee the toolchain console output to a log file (guide §1.2.2.1).</summary>
    public bool WriteLog { get; set; } = true;
}
