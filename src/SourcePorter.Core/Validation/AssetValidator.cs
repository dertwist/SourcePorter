using SourcePorter.Core.Domain;

namespace SourcePorter.Core.Validation;

/// <summary>The kind of problem found while validating an addon's assets.</summary>
public enum AssetIssueKind
{
    /// <summary>A resource references a file that doesn't resolve (loose or in any VPK).</summary>
    MissingReference,

    /// <summary>A resource could not be read/parsed.</summary>
    ReadError,
}

/// <summary>A single validation finding.</summary>
/// <param name="Kind">What went wrong.</param>
/// <param name="Source">The resource being checked (relative to the addon game dir).</param>
/// <param name="Detail">The missing reference name, or the error message.</param>
public sealed record AssetIssue(AssetIssueKind Kind, string Source, string Detail);

/// <summary>The result of validating an addon.</summary>
public sealed class ValidationReport
{
    public string AddonTitle { get; set; } = "";
    public int ResourcesScanned { get; set; }
    public int ReferencesChecked { get; set; }
    public int ArchivesMounted { get; set; }
    public List<AssetIssue> Issues { get; } = [];

    public bool HasIssues => Issues.Count > 0;
    public int MissingCount => Issues.Count(i => i.Kind == AssetIssueKind.MissingReference);
    public int ErrorCount => Issues.Count(i => i.Kind == AssetIssueKind.ReadError);
}

/// <summary>
/// Validates a compiled CS2 addon: scans its <c>.vmap_c</c> / <c>.vmdl_c</c> /
/// <c>.vmat_c</c> resources, reads each one's external references (RERL), and
/// checks every reference resolves to a file — either loose in the addon or in a
/// mounted base VPK. Surfaces missing files and unreadable resources.
/// </summary>
public sealed class AssetValidator(Cs2Install cs2, string addon)
{
    /// <summary>Resource extensions whose references we walk.</summary>
    private static readonly string[] ResourceGlobs = ["*.vmap_c", "*.vmdl_c", "*.vmat_c"];

    public ValidationReport Validate(Action<string>? log = null, CancellationToken ct = default)
    {
        var report = new ValidationReport();
        var addonGameDir = Path.Combine(cs2.GameDir, "csgo_addons", addon);

        if (!Directory.Exists(addonGameDir))
        {
            report.Issues.Add(new AssetIssue(AssetIssueKind.ReadError, addon,
                $"Compiled addon directory not found: {addonGameDir}. Import (and compile) the map first."));
            return report;
        }

        report.AddonTitle = AddonInfo.ReadTitle(Path.Combine(addonGameDir, "addoninfo.txt")) ?? addon;
        log?.Invoke($"Validating addon '{report.AddonTitle}'.");

        using var index = new VpkIndex();
        index.AddLooseRoot(addonGameDir);
        foreach (var dir in GameInfo.ReadSearchDirs(Path.Combine(cs2.S2GameInfoDir, "gameinfo.gi")))
        {
            foreach (var vpk in SafeEnumerate(Path.Combine(cs2.GameDir, dir), "*_dir.vpk", recursive: false))
                index.MountVpk(vpk);
        }
        report.ArchivesMounted = index.PackageCount;
        log?.Invoke($"Mounted {index.PackageCount} base archive(s).");

        foreach (var glob in ResourceGlobs)
        {
            foreach (var file in SafeEnumerate(addonGameDir, glob, recursive: true))
            {
                ct.ThrowIfCancellationRequested();
                report.ResourcesScanned++;
                var rel = Path.GetRelativePath(addonGameDir, file).Replace('\\', '/');

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
                    report.ReferencesChecked++;

                    var compiled = reference.EndsWith("_c", StringComparison.Ordinal) ? reference : reference + "_c";
                    if (!index.Exists(compiled))
                        report.Issues.Add(new AssetIssue(AssetIssueKind.MissingReference, rel, reference));
                }
            }
        }

        log?.Invoke(
            $"Scanned {report.ResourcesScanned} resources, checked {report.ReferencesChecked} references, " +
            $"{report.MissingCount} missing, {report.ErrorCount} unreadable.");
        return report;
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
