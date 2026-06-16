# bspsrc-launcher

Build-only helper that packages **BSPSource** (the Source 1 `.bsp` → `.vmf`
decompiler, https://github.com/ata4/bspsrc) into a single self-contained
`bspsrc.exe`. The committed artifact lives at
[`../bspsrc/bspsrc.exe`](../bspsrc/bspsrc.exe); this project is how it is
regenerated. It is intentionally **not** part of `SourcePorter.sln`.

## How it works

BSPSource is a Java app shipped as a *jlink runtime image* (its own JRE plus the
app modules). [`Program.cs`](Program.cs) embeds the entire image, extracts it to a
per-user cache on first run (`%LOCALAPPDATA%\SourcePorter\bspsrc\<stamp>\`), and
forwards every argument and stdio stream to the BSPSource CLI through the bundled
JRE — so the result is one file, needs no system Java, and behaves exactly like
the original `bspsrc` command. `SourcePorter.Core`'s `BspDecompiler` runs it.

## Rebuilding / updating BSPSource

1. Download the latest **`bspsrc-windows.zip`** from
   [BSPSource releases](https://github.com/ata4/bspsrc/releases) and save it here
   as `bspsrc-runtime.zip` (git-ignored — it is the build input, not committed):

   ```powershell
   Copy-Item <downloaded>\bspsrc-windows.zip tools\bspsrc-launcher\bspsrc-runtime.zip
   ```

2. Bump `<Version>` in [`bspsrc-launcher.csproj`](bspsrc-launcher.csproj) to match
   the new BSPSource version (it is the cache key, so the new image re-extracts
   cleanly on the user's machine).

3. Publish and copy the single exe into place:

   ```powershell
   dotnet publish tools\bspsrc-launcher -c Release -r win-x64 -o tools\bspsrc-launcher\out
   Copy-Item tools\bspsrc-launcher\out\bspsrc.exe tools\bspsrc\bspsrc.exe -Force
   ```

4. Commit the refreshed `tools/bspsrc/bspsrc.exe`.

The embedded module entry point is
`info.ata4.bspsrc.app/info.ata4.bspsrc.app.src.BspSourceLauncher`; passing
arguments runs the CLI (no args would launch the GUI).
