using SourcePorter.Core.Domain;

namespace SourcePorter.Core.Validation;

/// <summary>The kind of problem found while validating an addon's assets.</summary>
public enum AssetIssueKind
{
    /// <summary>A content file references a prefab <c>.vmap</c> that doesn't exist in the addon.</summary>
    MissingPrefab,

    /// <summary>
    /// A material/model a content file references was never imported — absent from the
    /// addon content (<c>.vmat</c>/<c>.vmdl</c>) and from base CS2 (compiled <c>_c</c>).
    /// This is what makes a ported map load with missing textures/props.
    /// </summary>
    MissingImport,

    /// <summary>A model references a mesh source (<c>.dmx</c>/<c>.fbx</c>/…) that doesn't exist — it can't be recompiled.</summary>
    MissingSource,

    /// <summary>A compiled <c>_c</c> resource references a <c>_c</c> file that doesn't resolve (loose or in any VPK).</summary>
    MissingReference,

    /// <summary>A resource could not be read/parsed.</summary>
    ReadError,
}

/// <summary>A single validation finding.</summary>
/// <param name="Kind">What went wrong.</param>
/// <param name="Source">The content file being checked (relative to its addon dir).</param>
/// <param name="Detail">The missing reference name, or the error message.</param>
/// <param name="ReferencePath">
/// The bare asset path the issue is about (e.g. <c>models/foo.vmdl</c>), without the
/// human-readable suffix in <paramref name="Detail"/>. Set for reference issues so a
/// repair pass can map it back to a Source 1 source without parsing <paramref name="Detail"/>;
/// null for <see cref="AssetIssueKind.ReadError"/>.
/// </param>
public sealed record AssetIssue(AssetIssueKind Kind, string Source, string Detail, string? ReferencePath = null);

/// <summary>The result of validating an addon.</summary>
public sealed class ValidationReport
{
    public string AddonTitle { get; set; } = "";

    /// <summary>Content files (<c>.vmap</c>/<c>.vmdl</c>/<c>.vmat</c>) scanned for dependencies.</summary>
    public int ContentFilesScanned { get; set; }

    /// <summary>Distinct content dependencies checked.</summary>
    public int ReferencesChecked { get; set; }

    /// <summary>Compiled <c>_c</c> resources scanned (the secondary pass).</summary>
    public int CompiledResourcesScanned { get; set; }

    public int ArchivesMounted { get; set; }
    public List<AssetIssue> Issues { get; } = [];

    public bool HasIssues => Issues.Count > 0;
    public int MissingPrefabCount => Issues.Count(i => i.Kind == AssetIssueKind.MissingPrefab);
    public int MissingImportCount => Issues.Count(i => i.Kind == AssetIssueKind.MissingImport);
    public int MissingImportModels => Issues.Count(i => i.Kind == AssetIssueKind.MissingImport && i.Detail.Contains("(model"));
    public int MissingImportMaterials => Issues.Count(i => i.Kind == AssetIssueKind.MissingImport && i.Detail.Contains("(material"));
    public int MissingSourceCount => Issues.Count(i => i.Kind == AssetIssueKind.MissingSource);
    public int MissingReferenceCount => Issues.Count(i => i.Kind == AssetIssueKind.MissingReference);
    public int ErrorCount => Issues.Count(i => i.Kind == AssetIssueKind.ReadError);
}

/// <summary>
/// Validates a CS2 addon by checking that every dependency its <b>content</b> files
/// reference actually resolves. For each <c>.vmap</c>/<c>.vmdl</c>/<c>.vmat</c> in the
/// addon's <c>content\</c> tree it extracts the referenced prefab <c>.vmap</c>s,
/// materials, models, and mesh sources (see <see cref="ContentReferenceScanner"/>) and
/// verifies each exists — either imported into the addon or shipped in base CS2. This
/// catches the real failure mode (*"the map loads with missing textures/props"*) and
/// works without anything being compiled. As a secondary pass it also walks the compiled
/// <c>game\</c> <c>_c</c> resources' RERL when they exist.
/// </summary>
public sealed class AssetValidator(Cs2Install cs2, string addon)
{
    /// <summary>Content file types whose embedded references we scan.</summary>
    private static readonly string[] ContentGlobs = ["*.vmap", "*.vmdl", "*.vmat"];

    /// <summary>Compiled resource extensions whose RERL references we walk (secondary).</summary>
    private static readonly string[] ResourceGlobs = ["*.vmap_c", "*.vmdl_c", "*.vmat_c"];

    public ValidationReport Validate(Action<string>? log = null, CancellationToken ct = default)
    {
        var report = new ValidationReport();
        var contentDir = cs2.ContentAddonDir(addon);
        var gameDir = Path.Combine(cs2.GameDir, "csgo_addons", addon);

        var hasContent = Directory.Exists(contentDir);
        var hasGame = Directory.Exists(gameDir);
        if (!hasContent && !hasGame)
        {
            report.Issues.Add(new AssetIssue(AssetIssueKind.ReadError, addon,
                $"Addon not found under {contentDir} or {gameDir}. Import the map first."));
            return report;
        }

        report.AddonTitle =
            AddonInfo.ReadTitle(Path.Combine(gameDir, "addoninfo.txt")) ??
            AddonInfo.ReadTitle(Path.Combine(contentDir, "addoninfo.txt")) ?? addon;
        log?.Invoke($"Validating addon '{report.AddonTitle}' (content files + dependencies).");

        using var index = new VpkIndex();
        if (hasGame)
            index.AddLooseRoot(gameDir);
        foreach (var dir in GameInfo.ReadSearchDirs(Path.Combine(cs2.S2GameInfoDir, "gameinfo.gi")))
        {
            foreach (var vpk in SafeEnumerate(Path.Combine(cs2.GameDir, dir), "*_dir.vpk", recursive: false))
                index.MountVpk(vpk);
        }
        report.ArchivesMounted = index.PackageCount;
        log?.Invoke($"Mounted {index.PackageCount} base archive(s).");

        if (hasContent)
            ValidateContentReferences(report, contentDir, index, ct);
        if (hasGame)
            ValidateCompiledReferences(report, gameDir, index, ct);

        log?.Invoke(
            $"Scanned {report.ContentFilesScanned} content file(s) ({report.ReferencesChecked} dependency refs) " +
            $"and {report.CompiledResourcesScanned} compiled resource(s): " +
            $"{report.MissingPrefabCount} missing prefab(s), {report.MissingImportCount} not imported, " +
            $"{report.MissingSourceCount} missing source(s), {report.MissingReferenceCount} missing compiled dep(s), " +
            $"{report.ErrorCount} unreadable.");
        return report;
    }

    // --- content files: every prefab/material/model/mesh-source they reference must resolve ---
    private static void ValidateContentReferences(ValidationReport report, string contentDir, VpkIndex index, CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var glob in ContentGlobs)
        {
            foreach (var file in SafeEnumerate(contentDir, glob, recursive: true))
            {
                ct.ThrowIfCancellationRequested();
                report.ContentFilesScanned++;
                var rel = Path.GetRelativePath(contentDir, file).Replace('\\', '/');

                IReadOnlyList<ContentReferenceScanner.Reference> references;
                try
                {
                    references = ContentReferenceScanner.Scan(file);
                }
                catch (Exception ex)
                {
                    report.Issues.Add(new AssetIssue(AssetIssueKind.ReadError, rel, ex.Message));
                    continue;
                }

                foreach (var reference in references)
                {
                    ct.ThrowIfCancellationRequested();

                    // A content file naturally references itself or siblings already on disk;
                    // dedupe globally so each distinct missing dependency is reported once.
                    if (!seen.Add($"{reference.Kind}|{reference.Path}"))
                        continue;
                    report.ReferencesChecked++;

                    if (ReferenceExists(reference, contentDir, index))
                        continue;

                    report.Issues.Add(reference.Kind switch
                    {
                        ContentReferenceScanner.ReferenceKind.PrefabMap =>
                            new AssetIssue(AssetIssueKind.MissingPrefab, rel, $"{reference.Path} (prefab map)", reference.Path),
                        ContentReferenceScanner.ReferenceKind.Material =>
                            new AssetIssue(AssetIssueKind.MissingImport, rel, $"{reference.Path} (material not imported)", reference.Path),
                        ContentReferenceScanner.ReferenceKind.Model =>
                            new AssetIssue(AssetIssueKind.MissingImport, rel, $"{reference.Path} (model not imported)", reference.Path),
                        _ =>
                            new AssetIssue(AssetIssueKind.MissingSource, rel, $"{reference.Path} (mesh source)", reference.Path),
                    });
                }
            }
        }
    }

    // A prefab map and a mesh source only ever exist loose in the content tree. A material
    // or model may be imported into the addon (a .vmat/.vmdl source) OR ship in base CS2
    // (a compiled _c in a VPK / the loose game dir).
    private static bool ReferenceExists(ContentReferenceScanner.Reference reference, string contentDir, VpkIndex index)
    {
        var onDisk = Path.Combine(contentDir, reference.Path.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(onDisk))
            return true;

        return reference.Kind is ContentReferenceScanner.ReferenceKind.Material or ContentReferenceScanner.ReferenceKind.Model
            && index.Exists(reference.Path + "_c");
    }

    // --- compiled _c resources: walk each RERL reference (secondary, when compiled) ---
    private static void ValidateCompiledReferences(ValidationReport report, string gameDir, VpkIndex index, CancellationToken ct)
    {
        foreach (var glob in ResourceGlobs)
        {
            foreach (var file in SafeEnumerate(gameDir, glob, recursive: true))
            {
                ct.ThrowIfCancellationRequested();
                report.CompiledResourcesScanned++;
                var rel = Path.GetRelativePath(gameDir, file).Replace('\\', '/');

                IReadOnlyList<string> references;
                try
                {
                    using var fs = File.OpenRead(file);
                    references = Source2Resource.ReadExternalReferences(fs);
                }
                catch (Exception ex)
                {
                    report.Issues.Add(new AssetIssue(AssetIssueKind.ReadError, rel, ex.Message));
                    continue;
                }

                foreach (var reference in references)
                {
                    ct.ThrowIfCancellationRequested();
                    var compiled = reference.EndsWith("_c", StringComparison.Ordinal) ? reference : reference + "_c";
                    if (!index.Exists(compiled))
                        report.Issues.Add(new AssetIssue(AssetIssueKind.MissingReference, rel, reference));
                }
            }
        }
    }

    private static IEnumerable<string> SafeEnumerate(string dir, string pattern, bool recursive)
    {
        if (!Directory.Exists(dir))
            return [];
        try
        {
            return Directory.EnumerateFiles(dir, pattern,
                recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return [];
        }
    }
}
