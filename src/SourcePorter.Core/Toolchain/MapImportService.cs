using System.Text;
using System.Text.RegularExpressions;
using SourcePorter.Core.Domain;

namespace SourcePorter.Core.Toolchain;

/// <summary>
/// C# port of Valve's <c>import_scripts/import_map_community.py</c>. Drives
/// <c>source1import</c>, <c>cs_mdl_import</c>, and <c>resourcecompiler</c> in the
/// same order, with the same arguments and the same model/material/2-UV handling
/// — only the console-input gate and the <c>| tee</c> logging are replaced
/// (confirmation is handled by the UI; output is captured via
/// <see cref="ProcessRunner"/>). See ARCHITECTURE.md §"Toolchain orchestration".
/// </summary>
public sealed partial class MapImportService
{
    private readonly ValveToolLocator _tools;
    private readonly ProcessRunner _runner;
    private readonly string _workingDir;
    private readonly string _global2UvListPath;

    /// <summary>Status/header lines (mirrors the Python <c>print_I</c> banners).</summary>
    public event Action<string>? OnLog;

    /// <param name="tools">Resolves the Valve tool executables.</param>
    /// <param name="runner">Process runner (output is forwarded to <see cref="OnLog"/>).</param>
    /// <param name="importScriptsDir">
    /// The <c>import_scripts</c> directory — the working directory the tools run in
    /// and where <c>source1import_2uvmateriallist.txt</c> lives. Defaults to CWD,
    /// matching the Python.
    /// </param>
    public MapImportService(ValveToolLocator tools, ProcessRunner runner, string? importScriptsDir = null)
    {
        _tools = tools;
        _runner = runner;
        _workingDir = importScriptsDir ?? Environment.CurrentDirectory;
        _global2UvListPath = Path.Combine(_workingDir, "source1import_2uvmateriallist.txt");
        _runner.OnOutput += line => OnLog?.Invoke(line.Text);
    }

    /// <summary>
    /// Runs the full import. <paramref name="project"/> supplies the four paths,
    /// addon, map name, and <see cref="ImportOptions"/>.
    /// </summary>
    public async Task ImportAsync(PortProject project, CancellationToken ct = default)
    {
        var s1Game = project.S1GameInfoDir;
        var s1Content = project.S1ContentDir;
        var addon = project.AddonName;
        var originalMapName = project.MapName;

        var paths = new ImportPaths(project.S2GameInfoDir, addon, originalMapName);

        // VALVE_NO_AUTO_P4=1 so the p4 libs run disconnected (utlc.SaveEnv).
        var env = new Dictionary<string, string> { ["VALVE_NO_AUTO_P4"] = "1" };

        // --- import vmf -> vmap (built once with the ORIGINAL map name, reused for the re-import) ---
        var importArgs = BuildMapImportArgs(project, s1Game, s1Content, addon, originalMapName);
        await RunAsync(_tools.Source1Import, "source1import.exe", importArgs, env, ct);

        // replace 'instance' paths with 'prefab'
        paths.SwitchInstancesToPrefabs();

        if (!project.Import.SkipDeps)
        {
            // We strip out models as they go through the new importer last.
            StripMdlsFromRefs(paths.PrefabRefs);

            // now import mdls (as modeldoc), and their materials
            await ImportAndCompileMapMdlsAsync(paths.PrefabMdlList, paths, s1Game, addon, env, ct);

            // import refs (excluding mdls)
            await ImportAndCompileMapRefsAsync(paths.PrefabNewRefs, paths, s1Game, addon, env, ct);

            // quick import vmf again, now that dependencies (materials especially) are compiled
            await RunAsync(_tools.Source1Import, "source1import.exe", importArgs, env, ct);
        }

        // explicit copy of main .vmap to game maps if not already there (it can only be compiled from there)
        if (!File.Exists(paths.ContentMainVmap) && File.Exists(paths.ImportedMainVmap))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(paths.ContentMainVmap)!);
            File.Copy(paths.ImportedMainVmap, paths.ContentMainVmap, overwrite: true);
        }
    }

    private static string BuildMapImportArgs(PortProject p, string s1Game, string s1Content, string addon, string mapName)
    {
        var bsp = p.Import.UseBsp ? "-usebsp" : "";
        var noMerge = p.Import.UseBspNoMergeInstances ? " -usebsp_nomergeinstances" : "";
        return $"-retail -nop4 -nop4sync {bsp}{noMerge} -src1gameinfodir \"{s1Game}\" -src1contentdir \"{s1Content}\" -s2addon \"{addon}\" -game csgo maps\\{mapName}.vmf";
    }

    // ---- StripMDLsFromRefs ----
    private static void StripMdlsFromRefs(string refsPath)
    {
        if (!File.Exists(refsPath))
            return;

        var refs = RefsFile.ReadTextFile(refsPath);
        var (mdls, others) = RefsFile.SplitMdlFromRefs(refs);

        var mdlListPath = refsPath.Replace("_refs.txt", "_mdl_lst.txt");
        RefsFile.EnsureFileWritable(mdlListPath);
        File.WriteAllLines(mdlListPath, mdls);

        var newRefsPath = refsPath.Replace("_refs.txt", "_new_refs.txt");
        RefsFile.EnsureFileWritable(newRefsPath);
        File.WriteAllText(newRefsPath, RefsFile.RefsStringFromList(others));
    }

    // ---- ImportAndCompileMapMDLs ----
    private async Task ImportAndCompileMapMdlsAsync(
        string mdlListPath, ImportPaths paths, string s1Game, string addon,
        IReadOnlyDictionary<string, string> env, CancellationToken ct)
    {
        if (!File.Exists(mdlListPath))
            return;

        var mdlFiles = RefsFile.ReadTextFile(mdlListPath);
        if (mdlFiles.Count < 1)
        {
            OnLog?.Invoke("No MDLs to import");
            return;
        }

        var s2Content = paths.S2ContentCsgoImported;
        var mdlMtls = new HashSet<string>(StringComparer.Ordinal);
        var extraOptions = "";

        // Import each model, collect its material refs.
        foreach (var entry in mdlFiles)
        {
            if (entry.StartsWith('-'))
            {
                extraOptions = entry is "-" or "-nooptions" ? "" : entry;
                continue;
            }

            var mdlFile = entry.Replace('/', '\\');
            var refsName = Path.Combine(s2Content, mdlFile.Replace(".mdl", "_refs.txt"));

            var importArgs = $"-nop4 {extraOptions} -i \"{s1Game}\" -o \"{s2Content}\" \"{mdlFile}\"".Replace("  ", " ");
            await RunAsync(_tools.ModelImporter, "cs_mdl_import.exe", importArgs, env, ct);

            if (File.Exists(refsName))
            {
                var refs = RefsFile.ReadTextFile(refsName);
                foreach (var mtl in RefsFile.ListStringFromRefs(refs).Split('\n'))
                    mdlMtls.Add(mtl);
            }
        }

        // Import the materials used by the models in one filelist.
        var mtlListPath = mdlListPath.Replace("mdl_lst", "mtl_lst");
        RefsFile.EnsureFileWritable(mtlListPath);
        File.WriteAllText(mtlListPath, RefsFile.RefsStringFromList(mdlMtls));

        var importRefsArgs = $"-retail -nop4 -nop4sync -src1gameinfodir \"{s1Game}\" -s2addon {addon} -game csgo -usefilelist \"{mtlListPath}\"";
        await RunAsync(_tools.Source1Import, "source1import.exe", importRefsArgs, env, ct);

        // Load the global list of materials we've already forced UV2 on, and re-apply the flag.
        var global2Uv = new HashSet<string>(StringComparer.Ordinal);
        if (File.Exists(_global2UvListPath))
        {
            foreach (var mtl in RefsFile.ReadTextFile(_global2UvListPath))
            {
                global2Uv.Add(mtl);
                ForceUv2ForVmat(s2Content, mtl);
            }
        }
        RefsFile.EnsureFileWritable(_global2UvListPath);
        using var global2UvWriter = new StreamWriter(_global2UvListPath, append: true);

        // Compile materials explicitly (model compile alone misses some).
        foreach (var mtl in mdlMtls)
        {
            if (mtl.Length == 0 || mtl.StartsWith('-'))
                continue;

            var vmat = Path.Combine(s2Content, mtl.Replace('/', '\\').Replace(".vmt", ".vmat"));
            await RunAsync(_tools.ResourceCompiler, "resourcecompiler.exe",
                $"-retail -nop4 -game csgo \"{vmat}\"", env, ct);
        }

        // Compile models, force-compiling those whose materials need UV2.
        foreach (var entry in mdlFiles)
        {
            if (entry.StartsWith('-'))
                continue;

            var mdlFile = entry.Replace('/', '\\');
            var vmdl = Path.Combine(s2Content, mdlFile.Replace(".mdl", ".vmdl"));
            if (!File.Exists(vmdl))
                continue;

            var refsName = Path.Combine(s2Content, mdlFile.Replace(".mdl", "_refs.txt"));
            var force = Force2UvsIfRequired(s2Content, refsName, global2Uv, global2UvWriter);

            var f = force ? "-f " : "";
            await RunAsync(_tools.ResourceCompiler, "resourcecompiler.exe",
                $"-retail -nop4 {f}-game csgo \"{vmdl}\"", env, ct);
        }
    }

    // ---- ImportAndCompileMapRefs ----
    private async Task ImportAndCompileMapRefsAsync(
        string refsFile, ImportPaths paths, string s1Game, string addon,
        IReadOnlyDictionary<string, string> env, CancellationToken ct)
    {
        if (!File.Exists(refsFile))
            return;

        var importArgs = $"-retail -nop4 -nop4sync -src1gameinfodir \"{s1Game}\" -s2addon {addon} -game csgo -usefilelist \"{refsFile}\"";
        await RunAsync(_tools.Source1Import, "source1import.exe", importArgs, env, ct);

        var s2Content = paths.S2ContentCsgoImported;
        var flat = RefsFile.ListStringFromRefs(RefsFile.ReadTextFile(refsFile)).Split('\n');

        var sb = new StringBuilder();
        foreach (var raw in flat)
        {
            if (raw.Length == 0)
                continue;
            var line = raw.Replace(".vmt", ".vmat").Replace(" ", "_");
            sb.Append(Path.Combine(s2Content, line.Replace('/', '\\'))).Append('\n');
        }

        var tmpFile = paths.PrefabCompileNewRefs;
        RefsFile.EnsureFileWritable(tmpFile);
        File.WriteAllText(tmpFile, sb.ToString());

        await RunAsync(_tools.ResourceCompiler, "resourcecompiler.exe",
            $"-retail -nop4 -game csgo -f -filelist \"{tmpFile}\"", env, ct);
    }

    // ---- ForceUV2ForVMAT: ensure the vmat has F_FORCE_UV2 right after its "shader" line ----
    private static void ForceUv2ForVmat(string s2Content, string mtlFile)
    {
        var vmat = Path.Combine(s2Content, mtlFile.Replace(".vmt", ".vmat"));
        if (!File.Exists(vmat))
            return;

        var lines = File.ReadAllLines(vmat).ToList();
        RefsFile.EnsureFileWritable(vmat);

        for (var i = 0; i < lines.Count; i++)
        {
            var txt = lines[i].Trim().ToLowerInvariant();
            if (!txt.StartsWith("\"shader\"", StringComparison.Ordinal))
                continue;

            // line + 1 is safe: there is always at least one more line after "Shader" "x.vfx".
            var next = lines[i + 1].Trim().Replace("\t", "");
            if (!next.StartsWith("\"F_FORCE_UV2\"", StringComparison.Ordinal))
                lines.Insert(i + 1, "\t\"F_FORCE_UV2\" \"1\"");
            break;
        }

        File.WriteAllLines(vmat, lines);
    }

    // ---- Force2UVsIfRequired: returns true if any material in this model is 2-UV ----
    private static bool Force2UvsIfRequired(string s2Content, string refsName, HashSet<string> global2Uv, TextWriter global2UvWriter)
    {
        var meshInfoPath = refsName.Replace("_refs.txt", "_refs/mesh/meshinfo.txt").Replace('/', '\\');
        if (!File.Exists(meshInfoPath) || !File.Exists(refsName))
            return false;

        var numUvs = ParseNumUvs(File.ReadAllText(meshInfoPath));
        var b2Uv = false;
        var updated = new HashSet<string>(StringComparer.Ordinal);

        foreach (var mtl in RefsFile.ListStringFromRefs(RefsFile.ReadTextFile(refsName)).Split('\n'))
        {
            if (mtl.Length == 0 || !updated.Add(mtl))
                continue;

            if (global2Uv.Contains(mtl))
            {
                b2Uv = true;
            }
            else if (numUvs == 2)
            {
                b2Uv = true;
                global2UvWriter.WriteLine(mtl);
                global2Uv.Add(mtl);
                ForceUv2ForVmat(s2Content, mtl);
            }
        }

        return b2Uv;
    }

    /// <summary>Pulls <c>numuvs</c> out of the model's <c>meshinfo.txt</c> (a Python dict literal).</summary>
    private static int ParseNumUvs(string meshInfo)
    {
        var m = NumUvsRegex().Match(meshInfo);
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    // ---- RunCommand: log the banner, run, throw on non-zero exit (utlc.RunCommand/Error) ----
    private async Task RunAsync(string toolPath, string fallbackExe, string arguments,
        IReadOnlyDictionary<string, string> env, CancellationToken ct)
    {
        var exe = File.Exists(toolPath) ? toolPath : fallbackExe;
        var display = $"{Path.GetFileName(exe)} {arguments}";

        OnLog?.Invoke("--------------------------------");
        OnLog?.Invoke("- Running Command: " + display);
        OnLog?.Invoke("--------------------------------");

        var exitCode = await _runner.RunAsync(exe, arguments, _workingDir, env, captured: null, ct);
        if (exitCode != 0)
            throw new ImportToolException($"Error running:\n>>>{display}\nAborting (exit {exitCode})");
    }

    [GeneratedRegex(@"[""']?numuvs[""']?\s*[:=]\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex NumUvsRegex();
}

/// <summary>Thrown when a Valve tool exits non-zero (mirrors <c>utlc.Error</c>'s abort).</summary>
public sealed class ImportToolException(string message) : Exception(message);
