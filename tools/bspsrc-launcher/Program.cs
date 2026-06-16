// bspsrc.exe — a single self-contained launcher for the bundled BSPSource tool.
//
// BSPSource (https://github.com/ata4/bspsrc) is a Java application. Valve ships no
// Source 1 .bsp decompiler, so SourcePorter bundles BSPSource to turn a .bsp back
// into a .vmf before the normal import path. To keep it one committed file (and to
// avoid requiring system Java), the whole BSPSource Windows runtime image — its own
// JRE plus the app modules — is embedded in this exe.
//
// On first run the image is extracted to a per-user cache keyed by the embedded
// content, and reused thereafter. Every command-line argument is forwarded verbatim
// to the BSPSource CLI through the bundled JRE, stdio is inherited (so a parent
// process can capture it), and BSPSource's exit code is returned unchanged.
//
// CLI surface (see `bspsrc.exe -h`): `bspsrc [OPTIONS] <bsp>...`, with
// `-o, --output=<path>` to set the .vmf destination.

using System.IO.Compression;
using System.Reflection;
using System.Diagnostics;

const string ResourceName = "bspsrc.runtime.zip";
const string ModuleEntry = "info.ata4.bspsrc.app/info.ata4.bspsrc.app.src.BspSourceLauncher";

var asm = Assembly.GetExecutingAssembly();
using var runtime = asm.GetManifestResourceStream(ResourceName)
    ?? throw new InvalidOperationException("Embedded BSPSource runtime image is missing.");

// Cache key changes whenever the embedded image does (version + byte length), so a
// rebuilt exe re-extracts cleanly instead of running a stale runtime.
var version = asm.GetName().Version?.ToString() ?? "0";
var stamp = $"{version}-{runtime.Length}";
var cacheDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "SourcePorter", "bspsrc", stamp);
var javaExe = Path.Combine(cacheDir, "bin", "java.exe");
var marker = Path.Combine(cacheDir, ".ready");

if (!File.Exists(marker) || !File.Exists(javaExe))
    Extract(runtime, cacheDir, marker, stamp);

var psi = new ProcessStartInfo(javaExe) { UseShellExecute = false };
psi.ArgumentList.Add("-m");
psi.ArgumentList.Add(ModuleEntry);
foreach (var arg in args)
    psi.ArgumentList.Add(arg);

using var proc = Process.Start(psi)
    ?? throw new InvalidOperationException("Failed to start the bundled JRE.");
proc.WaitForExit();
return proc.ExitCode;

// Extract to a sibling temp dir first, then swap it into place, so an interrupted
// extraction never leaves a half-populated cache that still looks "ready".
static void Extract(Stream runtime, string cacheDir, string marker, string stamp)
{
    var parent = Directory.GetParent(cacheDir)!.FullName;
    Directory.CreateDirectory(parent);
    var temp = Path.Combine(parent, $".tmp-{Guid.NewGuid():N}");
    Directory.CreateDirectory(temp);
    try
    {
        using (var zip = new ZipArchive(runtime, ZipArchiveMode.Read))
            zip.ExtractToDirectory(temp, overwriteFiles: true);

        if (Directory.Exists(cacheDir))
            Directory.Delete(cacheDir, recursive: true);
        Directory.Move(temp, cacheDir);
        File.WriteAllText(marker, stamp);
    }
    finally
    {
        if (Directory.Exists(temp))
            try { Directory.Delete(temp, recursive: true); } catch { /* best effort cleanup */ }
    }
}
