using System.Numerics;
using Datamodel;
using SourcePorter.Core.Domain;
using SourcePorter.Core.Vmap;
using DM = Datamodel.Datamodel;

namespace SourcePorter.Core.Tests;

/// <summary>
/// Exercises the post-import <c>.vmap</c> tools against synthetic vmaps built with the
/// real KeyValues2 serializer (no real maps): prefab collapse and the skybox template.
/// </summary>
public class VmapToolsTests
{
    // ---- Collapse ----

    [Fact]
    public void Collapse_merges_submap_children_removes_reference_and_tracks_merged_file()
    {
        var root = NewTempDir();
        try
        {
            var mapsDir = Path.Combine(root, "maps");
            var mainPath = Path.Combine(mapsDir, "ze_main.vmap");
            var subPath = Path.Combine(mapsDir, "ze_main_gameplay.vmap");

            WriteMap(mainPath, (dm, kids) =>
            {
                kids.Add(El(dm, "CMapEntity"));
                var prefab = El(dm, "CMapPrefab");
                prefab["targetMapPath"] = "maps/ze_main_gameplay.vmap";
                kids.Add(prefab);
            });
            WriteMap(subPath, (dm, kids) =>
            {
                kids.Add(El(dm, "CMapMesh"));
                kids.Add(El(dm, "CMapEntity"));
            });

            var doc = VmapDocument.Load(mainPath);
            var result = VmapPrefabCollapser.Collapse(doc, root);
            doc.Save();

            Assert.Equal(1, result.ReferencesMerged);
            Assert.Equal(2, result.NodesMoved);
            Assert.Single(result.MergedFiles);
            Assert.Equal(subPath, result.MergedFiles[0], StringComparer.OrdinalIgnoreCase);

            var reloaded = VmapDocument.Load(mainPath);
            Assert.Equal(3, reloaded.WorldChildren.Count); // original entity + 2 merged
            Assert.DoesNotContain(reloaded.WorldChildren, n => n.ClassName == "CMapPrefab");
            Assert.Contains(reloaded.WorldChildren, n => n.ClassName == "CMapMesh");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Collapse_with_no_references_is_a_noop()
    {
        var root = NewTempDir();
        try
        {
            var mainPath = Path.Combine(root, "maps", "flat.vmap");
            WriteMap(mainPath, (dm, kids) => kids.Add(El(dm, "CMapEntity")));

            var result = VmapPrefabCollapser.Collapse(VmapDocument.Load(mainPath), root);

            Assert.False(result.DidAnything);
            Assert.Equal(0, result.ReferencesMerged);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // ---- Skybox template ----

    [Fact]
    public void Skybox_creates_skybox_map_and_adds_reference_to_main()
    {
        var root = NewTempDir();
        try
        {
            var mapsDir = Path.Combine(root, "maps");
            var mainPath = Path.Combine(mapsDir, "de_main.vmap");
            WriteMap(mainPath, (dm, kids) => kids.Add(El(dm, "CMapEntity")));

            var result = VmapSkyboxTemplate.Create(mainPath);

            Assert.True(result.SkyboxCreated);
            Assert.True(result.ReferenceAdded);

            var skyPath = Path.Combine(mapsDir, "de_main_sky.vmap");
            Assert.True(File.Exists(skyPath));

            // The skybox map is flagged as a skybox and has an empty world.
            var sky = VmapDocument.Load(skyPath);
            Assert.Equal("skybox", sky.World["mapUsageType"] as string);
            Assert.Empty(sky.WorldChildren);

            // The main map gained a skybox_reference at 0 0 0 pointing at the skybox map.
            var main = VmapDocument.Load(mainPath);
            var skyRef = main.WorldChildren.FirstOrDefault(n => Classname(n) == "skybox_reference");
            Assert.NotNull(skyRef);
            Assert.Equal(Vector3.Zero, Assert.IsType<Vector3>(skyRef["origin"]));
            var props = Assert.IsType<Element>(skyRef["entity_properties"]);
            Assert.Equal("maps/de_main_sky.vmap", props["targetmapname"] as string);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Skybox_is_idempotent_on_second_run()
    {
        var root = NewTempDir();
        try
        {
            var mainPath = Path.Combine(root, "maps", "de_idem.vmap");
            WriteMap(mainPath, (_, _) => { });

            var first = VmapSkyboxTemplate.Create(mainPath);
            Assert.True(first.SkyboxCreated);
            Assert.True(first.ReferenceAdded);

            var second = VmapSkyboxTemplate.Create(mainPath);
            Assert.False(second.SkyboxCreated);  // sky file already there
            Assert.False(second.ReferenceAdded); // reference already there

            var main = VmapDocument.Load(mainPath);
            Assert.Equal(1, main.WorldChildren.Count(n => Classname(n) == "skybox_reference"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // ---- Encoding round-trip ----

    [Fact]
    public void Save_preserves_the_files_encoding_and_version()
    {
        var root = NewTempDir();
        try
        {
            var path = Path.Combine(root, "maps", "enc.vmap");
            WriteMap(path, (_, _) => { });

            var doc = VmapDocument.Load(path);
            Assert.Equal("keyvalues2", doc.Model.Encoding);
            doc.WorldChildren.Add(El(doc.Model, "CMapEntity"));
            doc.Save();

            var again = VmapDocument.Load(path);
            Assert.Equal("keyvalues2", again.Model.Encoding);
            Assert.Equal(4, again.Model.EncodingVersion);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // ---- Facade (path resolution from install + addon + map name) ----

    [Fact]
    public void PostImportVmapTools_collapse_resolves_the_addon_main_vmap()
    {
        var installRoot = NewTempDir();
        try
        {
            const string addon = "ze_addon";
            const string map = "ze_facade";
            var cs2 = new Cs2Install(installRoot);
            var contentMaps = Path.Combine(cs2.ContentAddonDir(addon), "maps");
            var mainPath = Path.Combine(contentMaps, map + ".vmap");
            var subPath = Path.Combine(contentMaps, map + "_env.vmap");

            WriteMap(mainPath, (dm, kids) =>
            {
                var prefab = El(dm, "CMapPrefab");
                prefab["targetMapPath"] = $"maps/{map}_env.vmap";
                kids.Add(prefab);
            });
            WriteMap(subPath, (dm, kids) => kids.Add(El(dm, "CMapMesh")));

            var result = PostImportVmapTools.CollapsePrefabs(cs2, addon, map);

            Assert.Equal(1, result.ReferencesMerged);
            var reloaded = VmapDocument.Load(mainPath);
            Assert.DoesNotContain(reloaded.WorldChildren, n => n.ClassName == "CMapPrefab");
            Assert.Contains(reloaded.WorldChildren, n => n.ClassName == "CMapMesh");
            Assert.False(File.Exists(subPath), "Merged sub-map file should be deleted");
            Assert.Empty(Directory.GetFiles(contentMaps, "*.bak")); // backup cleaned up
        }
        finally { Directory.Delete(installRoot, recursive: true); }
    }

    // ---- helpers ----

    private static Element El(DM dm, string className) => new(dm, "", null, className);

    private static string? Classname(Element node) =>
        node.ContainsKey("entity_properties") && node["entity_properties"] is Element props
            && props.ContainsKey("classname") && props["classname"] is string classname
            ? classname
            : null;

    private static void WriteMap(string path, Action<DM, ElementArray> addChildren)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var dm = new DM("vmap", 29);
        var root = El(dm, "CMapRootElement");
        dm.Root = root;
        var world = El(dm, "CMapWorld");
        root["world"] = world;
        var children = new ElementArray();
        world["children"] = children;
        addChildren(dm, children);
        dm.Save(path, "keyvalues2", 4);
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sp_vmap_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
