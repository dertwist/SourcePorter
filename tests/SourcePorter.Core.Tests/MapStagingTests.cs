using SourcePorter.Core.Domain;
using SourcePorter.Core.Toolchain;

namespace SourcePorter.Core.Tests;

public class MapStagingTests
{
    [Fact]
    public void StageVmf_passes_through_a_map_already_under_a_maps_folder()
    {
        // Already correctly structured — no copy, returned verbatim.
        var inPlace = Path.Combine("C:", "content", "maps", "ze_example.vmf");

        var staged = MapStaging.StageVmf(inPlace);

        Assert.Equal(inPlace, staged);
    }

    [Fact]
    public void StageVmf_copies_a_loose_map_into_a_temp_maps_folder()
    {
        var mapName = "sp_test_" + Guid.NewGuid().ToString("N");
        var loose = Path.Combine(Path.GetTempPath(), mapName + ".vmf");
        File.WriteAllText(loose, "world\n{\n}\n");
        try
        {
            var staged = MapStaging.StageVmf(loose);

            Assert.NotEqual(loose, staged);
            Assert.True(File.Exists(staged), "staged .vmf should exist");
            // It now satisfies source1import's content-dir / map-name split.
            Assert.True(Cs2Install.TryParseSourceMap(staged, out _, out var parsedName));
            Assert.Equal(mapName, parsedName);
        }
        finally
        {
            File.Delete(loose);
            var stagingDir = Path.Combine(MapStaging.StagingRoot, mapName);
            if (Directory.Exists(stagingDir))
                Directory.Delete(stagingDir, recursive: true);
        }
    }
}
