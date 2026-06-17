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
    /// Decompiles <paramref name="bspPath"/> to <paramref name="outputVmfPath"/>. When
    /// <paramref name="unpackEmbedded"/> is set, the BSP's embedded files (custom
    /// materials/models/etc. the mapper packed into the map) are extracted too, so the
    /// imported addon is self-contained. Returns the written <c>.vmf</c> and the unpack
    /// directory (if any) — see <see cref="BspDecompileResult"/>.
    /// </summary>
    public async Task<BspDecompileResult> DecompileAsync(
        string bspPath, string outputVmfPath, bool unpackEmbedded = false, CancellationToken ct = default)
    {
        if (!File.Exists(bspPath))
            throw new FileNotFoundException("BSP not found.", bspPath);

        var exe = ResolveExe(bspsrcLocation)
            ?? throw new FileNotFoundException(
                "bspsrc.exe not found. It ships under tools/bspsrc/ next to the app; " +
                "rebuild it from tools/bspsrc-launcher, or set its path in settings.");

        Directory.CreateDirectory(Path.GetDirectoryName(outputVmfPath)!);

        // `bspsrc [OPTIONS] <bsp>...` with `-o <file>` as the .vmf destination.
        // `--unpack_embedded` additionally extracts the BSP's packed files; BSPSource's
        // 'smart' unpack (on by default) skips the vbsp-generated, engine-only junk.
        var unpack = unpackEmbedded ? "--unpack_embedded " : "";
        var args = $"{unpack}-o \"{outputVmfPath}\" \"{bspPath}\"";
        OnLog?.Invoke($"Decompiling {Path.GetFileName(bspPath)} with BSPSource" +
                      (unpackEmbedded ? " (unpacking embedded content)…" : "…"));

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

        // BSPSource unpacks embedded files into a sibling dir named after the output
        // .vmf (e.g. `<out-dir>\<map>\materials\…`), which is itself a content root.
        string? unpackDir = null;
        if (unpackEmbedded)
        {
            var candidate = Path.Combine(
                Path.GetDirectoryName(outputVmfPath)!,
                Path.GetFileNameWithoutExtension(outputVmfPath));
            if (Directory.Exists(candidate))
                unpackDir = candidate;
            else
                OnLog?.Invoke("No embedded files unpacked (map packs no custom content).");
        }

        VmfNormalizer.EnsureImportableHeader(outputVmfPath, m => OnLog?.Invoke(m));
        VmfNormalizer.EnsureDisplacementOffsets(outputVmfPath, m => OnLog?.Invoke(m));
        // The content root source1import will read materials from is the unpack dir (or, when
        // nothing was unpacked, the .vmf's own dir). Strip color_correction entities whose .raw
        // isn't there — source1import access-violates on an unresolvable color-correction file.
        VmfNormalizer.EnsureNoUnresolvableColorCorrection(
            outputVmfPath, unpackDir ?? Path.GetDirectoryName(outputVmfPath)!, m => OnLog?.Invoke(m));

        return new BspDecompileResult(outputVmfPath, unpackDir);
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

/// <summary>
/// Outcome of a <see cref="BspDecompiler.DecompileAsync"/> run: the path of the
/// written <c>.vmf</c> and, when embedded files were unpacked, the directory they
/// were extracted into (a ready-made content root with <c>materials\</c>,
/// <c>models\</c>, … ). <see cref="UnpackDir"/> is null when nothing was unpacked.
/// </summary>
public sealed record BspDecompileResult(string VmfPath, string? UnpackDir);
