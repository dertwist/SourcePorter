using SourcePorter.Core.Domain;
using SourcePorter.Core.Toolchain;
using SourcePorter.Core.Validation;

// Headless harness for porting + validating maps — used to test imports in bulk.
//
//   port     <cs2dir> <sourceMap.vmf|.bsp> <addon> [--no-bsp] [--no-unpack] [--compile] [--no-compile-assets] [--verbose]
//   validate <cs2dir> <addon>
//   batch    <cs2dir> <mapsDir> [--limit N] [--no-bsp] [--no-unpack] [--compile] [--no-compile-assets] [--verbose]
//
// Console output is compacted by default (one "Ported X" line per asset); pass
// --verbose for the raw, unfiltered toolchain output.
//
// By default the map .vmap_c compile (slow lighting bake) is SKIPPED — import +
// validate-dependencies only, which is what answers "any missing files?".
// Pass --compile to also compile the map (surfaces map-level skybox/particle gaps).
// Dependency asset compile (materials/models -> _c) is ON by default here so the
// validator has _c files to check; pass --no-compile-assets for a faster import-only
// pass (validation then reports nothing, since there is nothing compiled to scan).

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: port|validate|batch …");
    return 2;
}

try
{
    return args[0].ToLowerInvariant() switch
    {
        "port" => await Port(args[1], args[2], args[3], NoBsp(args), args.Contains("--compile"), CompileAssets(args), !NoUnpack(args), Threads(args), Compact(args)),
        "validate" => Validate(args[1], args[2]),
        "batch" => await Batch(args[1], args[2], Limit(args), NoBsp(args), args.Contains("--compile"), CompileAssets(args), !NoUnpack(args), Threads(args), Compact(args)),
        _ => Fail($"unknown command '{args[0]}'"),
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    return 1;
}

static bool NoBsp(string[] a) => a.Contains("--no-bsp");
static bool NoUnpack(string[] a) => a.Contains("--no-unpack");
static bool CompileAssets(string[] a) => !a.Contains("--no-compile-assets");
static bool Compact(string[] a) => !a.Contains("--verbose");
static int Limit(string[] a)
{
    var i = Array.IndexOf(a, "--limit");
    return i >= 0 && i + 1 < a.Length && int.TryParse(a[i + 1], out var n) ? n : int.MaxValue;
}
static int Threads(string[] a)
{
    var i = Array.IndexOf(a, "--threads");
    return i >= 0 && i + 1 < a.Length && int.TryParse(a[i + 1], out var n) && n >= 1 ? n : 4;
}
static int Fail(string m) { Console.Error.WriteLine(m); return 2; }

static async Task<int> Port(string cs2Dir, string sourceMap, string addon, bool noBsp, bool compile, bool compileAssets, bool unpack, int threads, bool compact)
{
    var cs2 = new Cs2Install(cs2Dir);
    if (!cs2.IsValid(out var err)) { Console.Error.WriteLine(err); return 1; }

    // The decompiler forwards its own output via OnLog and the import service via OnLog
    // (compacted when enabled), so we subscribe to those — not runner.OnOutput — to keep
    // each tool line printed exactly once.
    var runner = new ProcessRunner();

    string vmf;
    if (sourceMap.EndsWith(".bsp", StringComparison.OrdinalIgnoreCase) && !noBsp)
    {
        var decompiler = new BspDecompiler(runner);
        decompiler.OnLog += Console.WriteLine;
        vmf = await MapStaging.StageBspAsync(decompiler, sourceMap, unpack);
    }
    else
    {
        vmf = MapStaging.StageVmf(sourceMap, Console.WriteLine);
    }

    var project = cs2.BuildProject(vmf, addon, new ImportOptions { MaxParallelism = threads, CompileAssets = compileAssets, CompactLog = compact });
    var service = new MapImportService(cs2.Tools, runner, ResolveImportScriptsDir(cs2));
    service.OnLog += Console.WriteLine;

    Console.WriteLine($"=== IMPORT {project.MapName} -> {addon} ===");
    await service.ImportAsync(project);

    if (compile)
    {
        Console.WriteLine($"=== COMPILE {project.MapName} ===");
        await service.CompileMapAsync(project);
    }

    foreach (var line in AddonStats.Collect(cs2.ContentAddonDir(addon), Path.Combine(cs2.GameDir, "csgo_addons", addon)).Format())
        Console.WriteLine(line);

    return Validate(cs2Dir, addon);
}

static int Validate(string cs2Dir, string addon)
{
    var cs2 = new Cs2Install(cs2Dir);
    var report = new AssetValidator(cs2, addon).Validate(Console.WriteLine);

    foreach (var issue in report.Issues.Take(40))
        Console.WriteLine($"  [{issue.Kind}] {issue.Source} -> {issue.Detail}");
    if (report.Issues.Count > 40)
        Console.WriteLine($"  … and {report.Issues.Count - 40} more.");

    Console.WriteLine($"=== {addon}: contentFiles={report.ContentFilesScanned} refs={report.ReferencesChecked} " +
                      $"missingPrefabs={report.MissingPrefabCount} notImported={report.MissingImportCount} " +
                      $"(mats={report.MissingImportMaterials} models={report.MissingImportModels}) " +
                      $"missingSources={report.MissingSourceCount} compiled={report.CompiledResourcesScanned} " +
                      $"missingCompiledDeps={report.MissingReferenceCount} readErrors={report.ErrorCount} ===");
    return report.HasIssues ? 1 : 0;
}

static async Task<int> Batch(string cs2Dir, string mapsDir, int limit, bool noBsp, bool compile, bool compileAssets, bool unpack, int threads, bool compact)
{
    var maps = Directory.EnumerateFiles(mapsDir, "*.vmf").Take(limit).ToList();
    Console.WriteLine($"Batch porting {maps.Count} map(s) from {mapsDir} (compile={compile}, compileAssets={compileAssets}, threads={threads})");

    var results = new List<string>();
    foreach (var map in maps)
    {
        var name = Path.GetFileNameWithoutExtension(map);
        var addon = $"{name}_test";
        try
        {
            var code = await Port(cs2Dir, map, addon, noBsp, compile, compileAssets, unpack, threads, compact);
            results.Add($"{name}: {(code == 0 ? "CLEAN" : "ISSUES")}");
        }
        catch (Exception ex)
        {
            results.Add($"{name}: FAILED ({ex.Message})");
        }
    }

    Console.WriteLine("\n===== BATCH SUMMARY =====");
    results.ForEach(Console.WriteLine);
    return results.All(r => r.EndsWith("CLEAN")) ? 0 : 1;
}

// source1import needs its real home dir as cwd (config lists + ./bin/vbsp.exe).
static string ResolveImportScriptsDir(Cs2Install cs2) =>
    Directory.Exists(cs2.ImportScriptsDir) ? cs2.ImportScriptsDir : AppContext.BaseDirectory;
