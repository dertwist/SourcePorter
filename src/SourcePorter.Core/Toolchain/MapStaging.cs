using SourcePorter.Core.Domain;

namespace SourcePorter.Core.Toolchain;

/// <summary>
/// Stages a source map into a clean temp content root that satisfies
/// <c>source1import</c>'s required <c>&lt;contentdir&gt;\maps\&lt;name&gt;.vmf</c>
/// layout. This lets users point at a loose <c>.vmf</c> (or a <c>.bsp</c> sitting
/// anywhere) without us writing into their own folders, and gives a self-contained
/// content root for a decompiled <c>.bsp</c>'s unpacked materials/models.
/// <para>
/// The returned <c>.vmf</c> path always lives under a <c>maps\</c> folder, so
/// <see cref="Cs2Install.TryParseSourceMap"/> can split it into the content dir and
/// map name the importer needs.
/// </para>
/// </summary>
public static class MapStaging
{
    /// <summary>Parent of every per-map staging dir: <c>%TEMP%\SourcePorter</c>.</summary>
    public static string StagingRoot => Path.Combine(Path.GetTempPath(), "SourcePorter");

    /// <summary>
    /// Ensures <paramref name="vmfPath"/> is usable as a source map. A correctly
    /// structured map that <c>source1import</c> already accepts (under a <c>maps\</c>
    /// folder, with the required header) is used in place — preserving its sibling
    /// materials/models content root. Anything else (a loose <c>.vmf</c>, or a
    /// decompiled one missing the <c>versioninfo</c> preamble) is copied into a fresh
    /// temp content root and fixed up there, so the user's own file is never mutated.
    /// Returns the path to feed <see cref="Cs2Install.BuildProject"/>.
    /// </summary>
    public static string StageVmf(string vmfPath, Action<string>? log = null)
    {
        var needsHeader = !VmfNormalizer.HasImportableHeader(vmfPath);
        if (!needsHeader && Cs2Install.TryParseSourceMap(vmfPath, out _, out _))
            return vmfPath;

        var mapName = Path.GetFileNameWithoutExtension(vmfPath);
        var maps = Path.Combine(FreshRoot(mapName), "maps");
        Directory.CreateDirectory(maps);
        var dest = Path.Combine(maps, mapName + ".vmf");
        File.Copy(vmfPath, dest, overwrite: true);
        if (needsHeader)
            VmfNormalizer.EnsureImportableHeader(dest, log);
        return dest;
    }

    /// <summary>
    /// Decompiles <paramref name="bspPath"/> into a fresh temp content root and,
    /// when <paramref name="unpackEmbedded"/> is set, extracts its embedded
    /// materials/models/etc. alongside, then places the decompiled <c>.vmf</c> under
    /// <c>maps\</c> in that same root. Returns the staged <c>.vmf</c> path.
    /// </summary>
    public static async Task<string> StageBspAsync(
        BspDecompiler decompiler, string bspPath, bool unpackEmbedded, CancellationToken ct = default)
    {
        var mapName = Path.GetFileNameWithoutExtension(bspPath);
        var root = FreshRoot(mapName);

        // Decompile the .vmf at the root level. When unpacking, BSPSource extracts the
        // embedded files into "<root>\<mapName>\" (a content root of its own), so we
        // adopt that as the content root and drop the .vmf into its maps\ folder.
        var rawVmf = Path.Combine(root, mapName + ".vmf");
        var result = await decompiler.DecompileAsync(bspPath, rawVmf, unpackEmbedded, ct);

        var contentRoot = result.UnpackDir ?? root;
        var maps = Path.Combine(contentRoot, "maps");
        Directory.CreateDirectory(maps);
        var stagedVmf = Path.Combine(maps, mapName + ".vmf");
        File.Move(result.VmfPath, stagedVmf, overwrite: true);
        return stagedVmf;
    }

    // A clean, predictable per-map dir under the staging root (cleared if it already
    // exists, so a re-run never inherits stale content from a previous attempt).
    private static string FreshRoot(string mapName)
    {
        var root = Path.Combine(StagingRoot, mapName);
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
        Directory.CreateDirectory(root);
        return root;
    }
}
