namespace SourcePorter.Core.Toolchain;

/// <summary>
/// Decompiles a Source 1 <c>.bsp</c> back to a <c>.vmf</c> using
/// <a href="https://github.com/ata4/bspsrc/">BSPSource</a> so it can then go through
/// the normal VMF import path. BSPSource is bundled as a single self-contained
/// <c>bspsrc.exe</c> (it carries its own Java runtime), shipped under
/// <c>tools/bspsrc/</c> next to the app — no system Java is required.
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

        var exe = ResolveExe(bspsrcLocation)
            ?? throw new FileNotFoundException(
                "bspsrc.exe not found. It ships under tools/bspsrc/ next to the app; " +
                "rebuild it from tools/bspsrc-launcher, or set its path in settings.");

        Directory.CreateDirectory(Path.GetDirectoryName(outputVmfPath)!);

        // `bspsrc [OPTIONS] <bsp>...` with `-o <file>` as the .vmf destination.
        var args = $"-o \"{outputVmfPath}\" \"{bspPath}\"";
        OnLog?.Invoke($"Decompiling {Path.GetFileName(bspPath)} with BSPSource…");

        void Forward(ProcessLine line) => OnLog?.Invoke(line.Text);
        runner.OnOutput += Forward;
        try
        {
            // BSPSource exits 0 even when a file fails (it logs the error instead), so
            // the missing-output check below — not the exit code — is the real gate.
            var exit = await runner.RunAsync(exe, args, Path.GetDirectoryName(exe), null, null, ct);
            if (exit != 0)
                throw new InvalidOperationException($"BSPSource failed (exit {exit}).");
        }
        finally
        {
            runner.OnOutput -= Forward;
        }

        if (!File.Exists(outputVmfPath))
            throw new InvalidOperationException(
                $"BSPSource did not produce {outputVmfPath} — see the log above for the cause.");
    }

    /// <summary>Locates <c>bspsrc.exe</c> from an explicit path/dir, else <c>tools/bspsrc/</c> by the exe.</summary>
    public static string? ResolveExe(string? location)
    {
        if (!string.IsNullOrWhiteSpace(location))
        {
            if (File.Exists(location))
                return location;
            var inDir = Path.Combine(location, "bspsrc.exe");
            if (File.Exists(inDir))
                return inDir;
        }

        var bundled = Path.Combine(AppContext.BaseDirectory, "tools", "bspsrc", "bspsrc.exe");
        return File.Exists(bundled) ? bundled : null;
    }
}
