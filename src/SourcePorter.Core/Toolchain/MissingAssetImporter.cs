using SourcePorter.Core.Domain;
using SourcePorter.Core.Validation;

namespace SourcePorter.Core.Toolchain;

/// <summary>Outcome of a <see cref="MissingAssetImporter.RepairAsync"/> pass.</summary>
public sealed class MissingAssetRepairReport
{
    /// <summary>Un-imported material/model count in the report the repair started from.</summary>
    public int InitialMissing { get; set; }

    /// <summary>How many import→re-validate rounds actually ran.</summary>
    public int Rounds { get; set; }

    /// <summary>Distinct models the repair attempted to import across all rounds.</summary>
    public int ModelsImported { get; set; }

    /// <summary>Distinct materials the repair attempted to import across all rounds.</summary>
    public int MaterialsImported { get; set; }

    /// <summary>The validation report after the final round (the authoritative end state).</summary>
    public ValidationReport FinalReport { get; set; } = new();

    /// <summary>Un-imported material/model count still remaining (couldn't be sourced from S1).</summary>
    public int StillMissing => FinalReport.MissingImportCount;

    /// <summary>How many of the initially-missing materials/models the repair resolved.</summary>
    public int Resolved => Math.Max(0, InitialMissing - StillMissing);
}

/// <summary>
/// Re-imports the materials/models the validator reports as un-imported
/// (<see cref="AssetIssueKind.MissingImport"/>) — the transitive dependencies the main
/// import misses (a model's gib/breakpiece children, a skybox material referenced only by
/// a lighting prefab, …). It maps each missing <c>.vmdl</c>/<c>.vmat</c> back to its
/// Source 1 source (<c>.mdl</c>/<c>.vmt</c>), drives
/// <see cref="MapImportService.ImportSpecificAssetsAsync"/>, then re-validates — looping
/// to a fixpoint because a freshly-imported model can reveal its own missing children.
/// Anything that still won't resolve (no Source 1 source) is left in
/// <see cref="MissingAssetRepairReport.FinalReport"/> and reported honestly, never faked.
///
/// Detection stays in <see cref="AssetValidator"/> (no toolchain coupling); this is the
/// separate remediation step that consumes its report.
/// </summary>
public sealed class MissingAssetImporter(MapImportService service, Cs2Install cs2)
{
    /// <summary>Progress banners (per-round summaries). Tool output flows via the service's own log.</summary>
    public event Action<string>? OnLog;

    /// <summary>
    /// Repairs <paramref name="initialReport"/>'s un-imported materials/models against
    /// <paramref name="project"/>, looping at most <paramref name="maxRounds"/> times.
    /// </summary>
    public async Task<MissingAssetRepairReport> RepairAsync(
        PortProject project, ValidationReport initialReport, int maxRounds = 4, CancellationToken ct = default)
    {
        var result = new MissingAssetRepairReport
        {
            InitialMissing = initialReport.MissingImportCount,
            FinalReport = initialReport,
        };

        // Track every S1 source we've already tried so an asset with no source (which keeps
        // reappearing in each report) is attempted once, not every round — this is also what
        // guarantees the loop terminates.
        var attempted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var report = initialReport;

        for (var round = 1; round <= maxRounds; round++)
        {
            ct.ThrowIfCancellationRequested();

            var (models, materials) = PlanFromReport(report);
            models = models.Where(m => attempted.Add("m|" + m)).ToList();
            materials = materials.Where(m => attempted.Add("t|" + m)).ToList();
            if (models.Count == 0 && materials.Count == 0)
                break;

            OnLog?.Invoke(
                $"Repair round {round}: importing {models.Count} model(s) and {materials.Count} material(s) the importer missed…");

            await service.ImportSpecificAssetsAsync(project, models, materials, ct);
            result.ModelsImported += models.Count;
            result.MaterialsImported += materials.Count;
            result.Rounds = round;

            // Re-validate to confirm what resolved and surface any deeper transitive deps.
            report = new AssetValidator(cs2, project.AddonName).Validate(null, ct);
            result.FinalReport = report;

            OnLog?.Invoke(
                $"Repair round {round} complete: {report.MissingImportCount} material/model(s) still un-imported.");
        }

        return result;
    }

    /// <summary>
    /// Maps a report's un-imported (<see cref="AssetIssueKind.MissingImport"/>) materials and
    /// models back to the Source 1 source paths <c>cs_mdl_import</c>/<c>source1import</c>
    /// consume — <c>.vmdl</c>→<c>.mdl</c>, <c>.vmat</c>→<c>.vmt</c>, same relative path.
    /// Pure and side-effect-free so it can be unit-tested without a CS2 install.
    /// </summary>
    public static (List<string> Models, List<string> Materials) PlanFromReport(ValidationReport report)
    {
        var models = new List<string>();
        var materials = new List<string>();
        foreach (var issue in report.Issues)
        {
            if (issue.Kind != AssetIssueKind.MissingImport || issue.ReferencePath is null)
                continue;

            var path = issue.ReferencePath;
            if (path.EndsWith(".vmdl", StringComparison.OrdinalIgnoreCase))
                models.Add(path[..^5] + ".mdl");
            else if (path.EndsWith(".vmat", StringComparison.OrdinalIgnoreCase))
                materials.Add(path[..^5] + ".vmt");
        }
        return (models, materials);
    }
}
