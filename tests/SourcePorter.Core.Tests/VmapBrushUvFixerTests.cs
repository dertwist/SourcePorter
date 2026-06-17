using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using Datamodel;
using SourcePorter.Core.Vmap;
using Xunit;

namespace SourcePorter.Core.Tests;

/// <summary>
/// Validates <see cref="VmapBrushUvFixer"/> against a Hammer-corrected ground-truth <c>.vmap</c>
/// (<c>ported_fixed.vmap</c> — the user nudged + reverted UVs in Source 2 Hammer, which re-baked
/// the correct texcoords). Skipped in CI (the real files are git-ignored / machine-specific).
/// </summary>
public class VmapBrushUvFixerTests
{
    private const string Fixed =
        @"E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive\content\csgo_addons\de_gracia_test\maps\ported_fixed.vmap";
    private const string StagedContent =
        @"C:\Users\admin\AppData\Local\Temp\SourcePorter\de_gracia\de_gracia";

    // The user-corrected material whose faces Hammer re-baked (1024px basetexture).
    private const string GroundTruthMat = "blend_roofing_tile_01";
    private const float RealDim = 1024f;

    [Fact]
    public void Recomputes_texcoords_to_match_hammer_ground_truth()
    {
        if (!File.Exists(Fixed) || !Directory.Exists(StagedContent))
            return; // fixtures absent (CI) — skip.

        var work = Path.Combine(Path.GetTempPath(), "uvfix_" + Guid.NewGuid().ToString("N"), "maps");
        Directory.CreateDirectory(work);
        var test = Path.Combine(work, "test.vmap");
        File.Copy(Fixed, test);
        try
        {
            // Ground truth = Hammer's correct texcoords for the corrected material's faces.
            var groundTruth = CollectGroundTruthTexcoords(test);
            Assert.NotEmpty(groundTruth);

            // Simulate a fresh (broken) import: un-correct textureScale (× 16/realDim) and zero the
            // texcoords of those faces. The fixer must restore both from the mapping alone.
            CorruptGroundTruthFaces(test);

            var result = VmapBrushUvFixer.FixAddon(work, StagedContent);
            Assert.True(result.DidAnything);

            var afterFix = CollectGroundTruthTexcoords(test);
            Assert.Equal(groundTruth.Count, afterFix.Count);
            for (var i = 0; i < groundTruth.Count; i++)
            {
                Assert.Equal(groundTruth[i].X, afterFix[i].X, 2);
                Assert.Equal(groundTruth[i].Y, afterFix[i].Y, 2);
            }
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(work)!, recursive: true);
        }
    }

    // --- helpers: walk the mesh graph and act on faces using the ground-truth material ---

    private static List<Vector2> CollectGroundTruthTexcoords(string path)
    {
        var result = new List<Vector2>();
        ForEachGroundTruthFace(VmapDocument.LoadInMemory(path), (texcoord, _, _, _, fvIndices) =>
        {
            foreach (var fv in fvIndices) result.Add(texcoord[fv]);
        });
        return result;
    }

    private static void CorruptGroundTruthFaces(string path)
    {
        var doc = VmapDocument.LoadInMemory(path);
        ForEachGroundTruthFace(doc, (texcoord, texScale, faceIndex, scaleList, fvIndices) =>
        {
            var ts = scaleList[faceIndex];
            scaleList[faceIndex] = new Vector2(ts.X * 16f / RealDim, ts.Y * 16f / RealDim);
            foreach (var fv in fvIndices) texcoord[fv] = Vector2.Zero;
        });
        doc.Save();
    }

    private delegate void FaceAction(
        IList<Vector2> texcoord, IList<Vector2> texScale, int faceIndex, IList<Vector2> scaleList, List<int> fvIndices);

    private static void ForEachGroundTruthFace(VmapDocument doc, FaceAction action)
    {
        var seen = new HashSet<Element>();
        void Walk(Element? e)
        {
            if (e is null || !seen.Add(e)) return;
            if (e.ClassName == "CDmePolygonMesh") TryMesh(e, action);
            foreach (var kv in e)
            {
                if (kv.Value is Element c) Walk(c);
                else if (kv.Value is ElementArray a) foreach (var x in a) Walk(x);
            }
        }
        Walk(doc.Model.Root);
    }

    private static void TryMesh(Element mesh, FaceAction action)
    {
        if (mesh["materials"] is not StringArray mats) return;
        var fS = (ElementArray)((Element)mesh["faceData"]!)["streams"]!;
        var matIndex = Data<int>(fS, "materialindex");
        var texScale = Data<Vector2>(fS, "textureScale");
        var texcoord = Data<Vector2>((ElementArray)((Element)mesh["faceVertexData"]!)["streams"]!, "texcoord");
        if (matIndex is null || texScale is null || texcoord is null) return;
        var faceEdge = Ints(mesh, "faceEdgeIndices");
        var edgeNext = Ints(mesh, "edgeNextIndices");
        var edgeVData = Ints(mesh, "edgeVertexDataIndices");

        for (var f = 0; f < matIndex.Count; f++)
        {
            if ((mats[matIndex[f]] ?? "").Contains(GroundTruthMat) != true) continue;
            var fvs = new List<int>();
            int e0 = faceEdge[f], e = e0, g = 0;
            do { fvs.Add(edgeVData[e]); e = edgeNext[e]; } while (e != e0 && ++g < 32);
            action(texcoord, texScale, f, texScale, fvs);
        }
    }

    private static IList<T>? Data<T>(ElementArray streams, string std)
    {
        foreach (var s in streams)
            if (s != null && (s.ContainsKey("standardAttributeName") ? s["standardAttributeName"] as string : null) == std)
                return s["data"] as IList<T>;
        return null;
    }
    private static List<int> Ints(Element e, string key)
    { var l = new List<int>(); if (e[key] is IEnumerable en) foreach (var i in en) l.Add(Convert.ToInt32(i)); return l; }
}
