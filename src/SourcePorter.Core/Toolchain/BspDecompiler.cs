namespace SourcePorter.Core.Toolchain;

/// <summary>
/// Decompiles a Source 1 <c>.bsp</c> back to a <c>.vmf</c> using
/// <a href="https://github.com/ata4/bspsrc/">BSPSource</a> (a Java tool) so it can
/// then go through the normal VMF import path. Requires Java on PATH and
/// <c>bspsrc.jar</c> available (it is not bundled — large third-party binary).
/// </summary>
public sealed class BspDecompiler(ProcessRunner runner, string? bspsrcLocation = null)
{
    public event Action<string>? OnLog;

    /// <summary>
    /// Decompiles <paramref name="bspPath"/> to <paramref name="outputVmfPath"/>.
    /// </summary>
    public async Task DecompileAsync(string bspPath, string outputVmfPath, CancellationToken ct = default)
    {
        if (!File.Exists(bspPath))
            throw new FileNotFoundException("BSP not found.", bspPath);

        var jar = ResolveJar(bspsrcLocation)
            ?? throw new FileNotFoundException(
                "bspsrc.jar not found. Download BSPSource (https://github.com/ata4/bspsrc/releases) " +
                "and place bspsrc.jar under tools/bspsrc/ (or set its path in settings).");

        Directory.CreateDirectory(Path.GetDirectoryName(outputVmfPath)!);

        var args = $"-jar \"{jar}\" -o \"{outputVmfPath}\" \"{bspPath}\"";
        OnLog?.Invoke($"Decompiling {Path.GetFileName(bspPath)} with BSPSource…");

        void Forward(ProcessLine line) => OnLog?.Invoke(line.Text);
        runner.OnOutput += Forward;
        try
        {
            var exit = await runner.RunAsync("java", args, Path.GetDirectoryName(jar), null, null, ct);
            if (exit != 0)
                throw new InvalidOperationException($"BSPSource failed (exit {exit}).");
        }
        finally
        {
            runner.OnOutput -= Forward;
        }

        if (!File.Exists(outputVmfPath))
            throw new InvalidOperationException($"BSPSource did not produce {outputVmfPath}.");
    }

    /// <summary>Locates <c>bspsrc.jar</c> from an explicit path/dir, else <c>tools/bspsrc/</c> by the exe.</summary>
    public static string? ResolveJar(string? location)
    {
        if (!string.IsNullOrWhiteSpace(location))
        {
            if (File.Exists(location))
                return location;
            var inDir = Path.Combine(location, "bspsrc.jar");
            if (File.Exists(inDir))
                return inDir;
        }

        var bundled = Path.Combine(AppContext.BaseDirectory, "tools", "bspsrc", "bspsrc.jar");
        return File.Exists(bundled) ? bundled : null;
    }
}
