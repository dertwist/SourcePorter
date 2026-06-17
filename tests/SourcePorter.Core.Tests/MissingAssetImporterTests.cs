using SourcePorter.Core.Toolchain;
using SourcePorter.Core.Validation;

namespace SourcePorter.Core.Tests;

public class MissingAssetImporterTests
{
    [Fact]
    public void PlanFromReport_maps_un_imported_models_and_materials_to_source1_paths()
    {
        var report = new ValidationReport();
        report.Issues.Add(new AssetIssue(AssetIssueKind.MissingImport,
            "models/props_interiors/styrofoam_cups.vmdl",
            "models/props_interiors/styrofoam_cups_p1.vmdl (model not imported)",
            "models/props_interiors/styrofoam_cups_p1.vmdl"));
        report.Issues.Add(new AssetIssue(AssetIssueKind.MissingImport,
            "maps/prefabs/x_lighting_prefab.vmap",
            "materials/skybox/sky_csgo_night02.vmat (material not imported)",
            "materials/skybox/sky_csgo_night02.vmat"));

        var (models, materials) = MissingAssetImporter.PlanFromReport(report);

        Assert.Equal(["models/props_interiors/styrofoam_cups_p1.mdl"], models);
        Assert.Equal(["materials/skybox/sky_csgo_night02.vmt"], materials);
    }

    [Fact]
    public void PlanFromReport_ignores_non_import_issues_and_null_reference_paths()
    {
        var report = new ValidationReport();
        // A missing prefab map and a mesh source are not re-importable by cs_mdl_import /
        // source1import -usefilelist, so the plan must skip them.
        report.Issues.Add(new AssetIssue(AssetIssueKind.MissingPrefab,
            "maps/m.vmap", "maps/prefabs/m_nav.vmap (prefab map)", "maps/prefabs/m_nav.vmap"));
        report.Issues.Add(new AssetIssue(AssetIssueKind.MissingSource,
            "models/a.vmdl", "models/a.dmx (mesh source)", "models/a.dmx"));
        report.Issues.Add(new AssetIssue(AssetIssueKind.ReadError, "x.vmap", "boom"));

        var (models, materials) = MissingAssetImporter.PlanFromReport(report);

        Assert.Empty(models);
        Assert.Empty(materials);
    }
}
