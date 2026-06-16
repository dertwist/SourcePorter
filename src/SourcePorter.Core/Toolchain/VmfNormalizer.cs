namespace SourcePorter.Core.Toolchain;

/// <summary>
/// Minimal, lossless fix-ups that make a Source 1 <c>.vmf</c> importable by
/// <c>source1import</c> without the manual "open and re-save in Hammer" step.
/// <para>
/// A decompiled <c>.vmf</c> (from BSPSource) starts straight at the <c>world</c>
/// block, omitting the <c>versioninfo</c>/<c>visgroups</c>/<c>viewsettings</c>
/// preamble Hammer always writes. <c>source1import</c>'s <c>CVMFtoVMAP</c> then
/// fails with <i>"Missing a required top-level key"</i>. Prepending that preamble
/// is the whole fix — verified end-to-end against <c>source1import</c> (the map
/// and all its dependency refs import once the header is present). The Hammer++
/// <c>palette_plus</c>/<c>light_plus</c>/… extras and Hammer's geometry re-save are
/// <b>not</b> required.
/// </para>
/// </summary>
public static class VmfNormalizer
{
    // The preamble Hammer writes ahead of `world`. source1import only needs these
    // three blocks (values mirror a default Hammer save).
    private const string HeaderPreamble =
        "versioninfo\r\n{\r\n\"editorversion\" \"400\"\r\n\"editorbuild\" \"6412\"\r\n" +
        "\"mapversion\" \"1\"\r\n\"formatversion\" \"100\"\r\n\"prefab\" \"0\"\r\n}\r\n" +
        "visgroups\r\n{\r\n}\r\nviewsettings\r\n{\r\n\"bSnapToGrid\" \"1\"\r\n" +
        "\"bShowGrid\" \"1\"\r\n\"bShowLogicalGrid\" \"0\"\r\n\"nGridSpacing\" \"64\"\r\n" +
        "\"bShow3DGrid\" \"0\"\r\n}\r\n";

    /// <summary>
    /// True if <paramref name="vmfPath"/> already opens with the <c>versioninfo</c>
    /// block — i.e. it does not need the preamble fix-up. Reads only the file head.
    /// </summary>
    public static bool HasImportableHeader(string vmfPath)
    {
        using var reader = new StreamReader(vmfPath);
        var buffer = new char[256];
        var read = reader.Read(buffer, 0, buffer.Length);
        return new string(buffer, 0, read)
            .TrimStart()
            .StartsWith("versioninfo", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Prepends the required preamble to <paramref name="vmfPath"/> if it is absent,
    /// so <c>source1import</c> accepts the map. Idempotent and lossless: a file that
    /// already has the header is left byte-for-byte unchanged. Returns true if the
    /// file was modified.
    /// </summary>
    public static bool EnsureImportableHeader(string vmfPath, Action<string>? log = null)
    {
        if (HasImportableHeader(vmfPath))
            return false;

        File.WriteAllText(vmfPath, HeaderPreamble + File.ReadAllText(vmfPath));
        log?.Invoke("Added the versioninfo/visgroups/viewsettings preamble a decompiled VMF omits " +
                    "(this replaces the manual open-and-save in Hammer).");
        return true;
    }
}
