# Architecture

This document describes how SourcePorter is structured and the design decisions
behind it. Read it before adding or refactoring a subsystem, and keep it updated
when the architecture changes. For the concept, see [overview.md](overview.md);
for the build order, see [ROADMAP.md](ROADMAP.md).

---

## 1. Technology stack

| Concern            | Choice                              | Rationale |
| ------------------ | ----------------------------------- | --------- |
| Language / runtime | C# on **.NET 9** (`net9.0`)         | Matches the installed SDK (9.0.300). |
| UI                 | **WinForms** (`net9.0-windows`)     | Lets us mirror the Source 2 Viewer (ValveResourceFormat GUI) dark theme 1:1; the orchestrated tools are Windows-only anyway. |
| VPK reading        | **ValvePak** 4.0.0.142 (NuGet)      | Reads CS2 `.vpk` archives to resolve whether referenced files exist. |
| KeyValues reading  | **ValveKeyValue** 0.20.0.417 (NuGet)| Reads KV1/KV3 config files (`gameinfo.gi`, `addoninfo.txt`). |
| `.vmap` Datamodel  | **KeyValues2** 0.8.0 (NuGet)        | Reads/writes the uncompiled `.vmap` DMX graph for the post-import `Vmap/` tools (the same serializer VRF uses). |
| Tests              | **xUnit**                           | Standard, fast, good CLI story. |
| Platform           | **Windows 10/11 x64**               | `source1import.exe`, `resourcecompiler.exe`, `cs_mdl_import.exe`, `vbsp.exe`, `vpk.exe` are all Win64. |

A read-only clone of [ValveResourceFormat](https://github.com/ValveResourceFormat/ValveResourceFormat)
lives under `reference/` (git-ignored) — studied to learn the resource/RERL
binary format; not a build dependency.

### Version constraint (important)

`ValveKeyValue` ≥ 0.21 and newer `ValvePak` builds require the **.NET 10 SDK**,
and only the .NET 9 SDK is installed. We pin **ValveKeyValue 0.20.0.417** (the
last net8.0 build) and **ValvePak 4.0.0.142**. When the .NET 10 SDK is available,
bump these together with the `TargetFramework`. See
[`SourcePorter.Core.csproj`](src/SourcePorter.Core/SourcePorter.Core.csproj).

**KeyValues2** 0.8.0 (the `.vmap` Datamodel reader/writer) is **not** affected by
that constraint: 0.8.0 targets **net9.0**, so it works on the current TFM without
the net10 SDK.

---

## 2. Solution layout

```
SourcePorter.sln
├─ src/
│  ├─ SourcePorter.Core/        Class library — all porting logic, no UI.
│  │  ├─ Domain/                PortProject, ImportOptions, Cs2Install,
│  │  │                         Cs2InstallLocator (registry-based auto-detect).
│  │  ├─ Toolchain/             1:1 port of import_map_community.py + utlc.py,
│  │  │                         plus the bundled-BSPSource .bsp→.vmf bridge.
│  │  │                         (ProcessRunner, ValveToolLocator, RefsFile,
│  │  │                          ImportPaths, MapImportService, BspDecompiler)
│  │  ├─ Validation/            Asset validator: missing-file / read-error checks.
│  │  │                         (Source2Resource, VpkIndex, GameInfo, AssetValidator)
│  │  ├─ Vmap/                   Post-import .vmap (DMX/KV3) tools via KeyValues2:
│  │  │                         collapse prefabs + skybox template.
│  │  │                         (VmapDocument, VmapPrefabCollapser, VmapSkyboxTemplate,
│  │  │                          VmapBackup, PostImportVmapTools)
│  │  └─ Materials/              Non-binary VMT→VMAT converter (port of kristiker/
│  │                            source1import) + a VTF header reader for texture
│  │                            dimensions. (VmtFile, VmtToVmatConverter, VmatDocument,
│  │                             MaterialValues, ShaderMapping, VtfHeader, MaterialConverter)
│  └─ SourcePorter.App/         WinForms front-end.
│     ├─ Program.cs             Entry point + theme init.
│     ├─ MainForm.cs            Importer shell: inputs + console + menu.
│     ├─ ReferenceForm.cs       Fields & tools reference window.
│     ├─ ConfigsEditorForm.cs   Editor for the bundled source1import_*.txt configs.
│     ├─ AppSettings.cs         Persisted GUI inputs (cfg-json equivalent).
│     ├─ app.svg / app.ico      Source-2-flavored app icon (SVG source + built .ico).
│     ├─ Icons/                 Embedded Source 2 Viewer SVG icons.
│     └─ Theme/                 Source 2 Viewer theme (Themer.cs) + SVG helpers.
├─ tools/
│  ├─ import_scripts/           Valve's source1import configs + scripts (bundled).
│  ├─ bspsrc/                   Bundled BSPSource .bsp→.vmf decompiler (single bspsrc.exe).
│  └─ bspsrc-launcher/          Build-only helper that packs BSPSource into that exe.
└─ tests/
   └─ SourcePorter.Core.Tests/  xUnit tests for the Core library.
```

**Hard rule: `SourcePorter.Core` never references WinForms.** All file parsing
and process orchestration live in Core and are unit-testable headlessly. The UI
is a thin shell that binds to Core and renders the console output.

Shared MSBuild settings live in
[`Directory.Build.props`](Directory.Build.props); the SDK is pinned in
[`global.json`](global.json).

---

## 3. What it does

SourcePorter is a focused, self-contained **importer** that runs Valve's
`source1import` toolchain exactly as the `import_scripts` do. The GUI asks for
three things — a **CS2 install directory**, a **source `.vmf`**, and an **output
addon** — and streams the toolchain output to a console.

[`Cs2Install`](src/SourcePorter.Core/Domain/Cs2Install.cs) derives everything
else from those inputs (the S1/S2 gameinfo dirs from the install root; the S1
content dir + map name by splitting the `.vmf` path at its `maps\` segment),
then builds a [`PortProject`](src/SourcePorter.Core/Domain/PortProject.cs) that
[`MapImportService`](src/SourcePorter.Core/Toolchain/MapImportService.cs)
consumes. The exact command sequence is in §4.

So the GUI rarely has to ask for the install at all,
[`Cs2InstallLocator`](src/SourcePorter.Core/Domain/Cs2InstallLocator.cs)
auto-detects it the way Steam itself does: read the Steam path from the registry
(`HKCU\Software\Valve\Steam\SteamPath`, falling back to the `WOW6432Node`
machine key), parse `steamapps\libraryfolders.vdf` (via `ValveKeyValue`) to find
the library that owns app **730**, and return the first
`…\common\Counter-Strike Global Offensive` that passes `Cs2Install.IsValid`. It is
best-effort and never throws; `MainForm` calls it on first run when the saved CS2
directory is empty. (Registry access on the non-`-windows` `net9.0` TFM is why
Core references the `Microsoft.Win32.Registry` compat package.)

The post-import fix-ups, asset audit, and packaging described in the S2ZE guide
are **deliberately out of scope for the importer itself**. They remain optional
future tools on the [roadmap](ROADMAP.md); SourcePorter never fakes or silently
skips manual Hammer work.

---

## 4. Toolchain orchestration (`Toolchain/`)

This is a **1:1 C# port** of Valve's `import_map_community.py` and its
`utils/utlc.py` helpers, replacing `os.system(...)` + `subprocess` and the manual
`| tee output.txt` trick with a managed, cancellable process runner. The command
strings, argument order, derived paths, and model/material/2-UV handling all
match the Python.

- [`ProcessRunner`](src/SourcePorter.Core/Toolchain/ProcessRunner.cs) — starts a
  child process, streams **stdout and stderr line-by-line** through an event,
  captures everything to a `StringBuilder`, supports cancellation
  (kills the process tree), and lets us set environment variables. This is the
  logging story the guide asks for (§1.2.2.1), built in.
- [`ValveToolLocator`](src/SourcePorter.Core/Toolchain/ValveToolLocator.cs) —
  resolves the five tool paths under the CS2 install (constructed by
  `Cs2Install.Tools`); the importer validates the install before a run.
- [`RefsFile`](src/SourcePorter.Core/Toolchain/RefsFile.cs) — port of the
  `utlc.py` list helpers (`ReadTextFile`, `RefsStringFromList`,
  `ListStringFromRefs`, `SplitMdlFromRefs`, `EnsureFileWritable`) that read/write
  the `importfilelist { "file" "…" }` format `source1import` uses.
- [`ImportPaths`](src/SourcePorter.Core/Toolchain/ImportPaths.cs) — reproduces
  the exact addon game/content path derivation and the `instances`→`prefabs`
  swap from the top of the Python script.
- [`MapImportService`](src/SourcePorter.Core/Toolchain/MapImportService.cs) — the
  orchestration: `ImportAsync` mirrors `main()`, with `StripMDLsFromRefs`,
  `ImportAndCompileMapMDLs`, `ImportAndCompileMapRefs`, `ForceUV2ForVMAT`, and
  `Force2UVsIfRequired` ported faithfully. Per-asset tool failures are warnings;
  only the map import is fatal (`ImportToolException`). `CompileMapAsync` compiles
  the imported `.vmap` to `.vmap_c`.
- **Terrain / `-usebsp` space fix.** `-usebsp` makes `source1import` shell out to
  `vbsp.exe -prepfors2` to clean up Source 1 geometry (notably displacements/terrain),
  but it passes the content path to `vbsp` **unquoted** — so the space in the install
  path (*"Counter-Strike Global Offensive"*) splits the argument, `vbsp` prints usage,
  never runs, and the map falls back to broken vmf-only geometry (`VBSP FAILED, using
  vmf only for S2 geo`). When `-usebsp`/`-usebsp_nomergeinstances` is on and the content
  dir contains a space, `ImportAsync` hands `source1import` the **8.3 short path** of
  that dir ([`ShortPath`](src/SourcePorter.Core/Toolchain/ShortPath.cs), Win32
  `GetShortPathName`) so the path survives intact down to `vbsp`. If no 8.3 name exists
  (disabled on the volume) it warns instead of silently producing flat terrain.
- **`-usebsp` crash recovery (geometry-mode cascade).** On some maps `source1import`'s
  `-usebsp` work **access-violates** (`0xC0000005`, exit `-1073741819`) *after* the `.vmap`
  is written but *before* the refs list — so the dependency import would find no refs. There
  are two distinct crash sites: the **instance-merge** step (e.g. de_grind) and the **vbsp
  geometry-cleanup** step that reads back `-prepfors2`'s BSP (e.g. de_gracia). `RunMapImportAsync`
  detects that exit code (`ImportToolException.ExitCode`) and degrades the BSP mode one step at
  a time (`NextFallback`): **`-usebsp` (merge) → `-usebsp_nomergeinstances` (no merge) →
  vmf-only (no `-usebsp`)**, reusing the mode that finally imports for the step-5 re-import so
  it doesn't recrash. Dropping merge costs nothing on a flat/decompiled map (every
  `func_instance` is inlined by BSPSource); dropping `-usebsp` entirely loses only vbsp's
  brush-geo cleanup — **terrain still imports** because
  [`VmfNormalizer.EnsureDisplacementOffsets`](src/SourcePorter.Core/Toolchain/VmfNormalizer.cs)
  repaired the displacements. Proactively, the GUI and CLI route **decompiled-BSP** input to
  `-usebsp_nomergeinstances` up front (skipping the merge crash), and the cascade is the safety
  net for the vbsp-geo crash and any other map that trips it. Only a vmf-only crash (nothing
  safer left) stays fatal. **Caveat:** `source1import` shells out to `./bin/vbsp.exe` *relative
  to its cwd*, so vbsp only actually runs when the import_scripts cwd contains `bin/vbsp.exe`
  (the CS2 install's `game\csgo\import_scripts` does; the app's bundled config copy does not).
  This is why the GUI/CLI run with the **CS2 install's** import_scripts as cwd — otherwise
  `-usebsp` silently no-ops to vmf-only geo.
- **Importing BSP-packed custom content
  ([`ImportGameInfo`](src/SourcePorter.Core/Toolchain/ImportGameInfo.cs)).** A decompiled `.bsp`'s
  embedded `materials\`/`models\` are unpacked into the staged content root, but the dependency
  phase originally only searched the CS:GO **game** dir, so the map's custom (e.g. `de_coastal\…`)
  assets imported as **zero** — only stock ones. Two asymmetries in Valve's tools caused it:
  `cs_mdl_import` resolves a model relative to its single `-i` root, and `source1import` imports a
  material only from the **game** search path (it *finds* a custom `.vmt` via `-src1contentdir`
  but resolves the `.vtf` via the game path and fails with a bare `*** Error Importing`). The fix
  exposes the unpacked content to source1import as a *game* path: for a **staged** content root
  (never the user's own folder — gated by `ImportGameInfo.IsStagedContentDir`), `PrepareDepsGameDir`
  drops a custom `gameinfo.txt` there (so `|gameinfo_path|.` makes it a `Game` search path, with
  `SteamAppId 730` + the real csgo dir keeping the base assets mounted) and uses it as the deps
  `-src1gameinfodir`; the model pass picks `-i` per model (content root if the `.mdl` is there,
  else CS:GO). source1import then prompts *"gameinfo.txt isn't in csgo … continue? ('y')"*, so the
  deps imports feed `"y"` on stdin (`ProcessRunner` `standardInput` + `ConfirmIfNotCsgo`).
  Verified end-to-end on de_gracia: 0 → 71 custom materials and 0 → 248 custom models imported,
  un-imported deps 390 → 37. The same path is reused by the `MissingAssetImporter` repair.
- **Custom brush-UV scale — fixed after import, not during (`Vmap/VmapBrushUvFixer`, §5).** A
  decompiled BSP's custom brush materials (e.g. `de_coastal/*`) import with the wrong UV scale,
  because source1import reads a material's texture dimensions (to convert Source 1 texel UVs to
  Source 2) only from its **game** filesystem, and the map's custom `.vmt` live in the staged
  content root, not on csgo's path (`GetMappingDimensionsForVMT: can't open …de_coastal/*.vmt`).
  Two attempts to feed the dimensions *during* import via `-src1gameinfodir` both failed (see
  [memory](../memory): source1import only resolves the input map / runs vbsp when the game dir is
  the real csgo dir). So the main import always uses the real csgo dir and the scale is corrected
  **deterministically afterwards** by [`VmapBrushUvFixer`](src/SourcePorter.Core/Vmap/VmapBrushUvFixer.cs):
  source1import bakes every unreadable material's faces at a fixed **16×16-default** texture size,
  so the fixer reads the real `.vtf` dimensions from the staged content, corrects each affected
  face's `textureScale` (× `realDim/16`) and **recomputes** its baked `texcoord` from the corrected
  mapping (the formula Hammer uses; see §5). It runs only for
  BSP imports — where the staged content holds *only* the BSP's custom materials, so stock faces
  (which source1import read correctly) are never touched. The **Import missing assets** repair
  (Tools menu) **re-stages the current source map** when one is set
  (`MainForm.StageCurrentSourceAsync`, shared with the import) so it can re-import custom BSP assets
  too; genuinely sourceless assets (e.g. break-model children absent from the BSP unpack and the
  CS:GO install) stay honestly reported as missing.
- **Console compaction ([`LogCompactor`](src/SourcePorter.Core/Toolchain/LogCompactor.cs),
  `ImportOptions.CompactLog`, default on).** The toolchain emits a whole block per asset
  (a VTF property dump, several `+- Wrote file …tga` lines, `ProcessTexture` notices,
  search-path spam). `MapImportService` routes every log line — its own banners and the
  tools' stdout/stderr — through a single stateful compactor (guarded by a lock, since
  output streams from several tools at once) that folds each block into one
  `Ported foo.vmat` / `Ported foo.vmdl` line and collapses runs of identical lines into
  `… (repeated N more times)`. Warnings, errors, leaks and the map-import banner always
  pass through. Exposed as the GUI **Compact log** checkbox and CLI `--verbose` (opt-out).
- **Compile gate (`ImportOptions.CompileAssets`, default off — a speed lever).**
  The dependency phase always *imports* materials/models (`cs_mdl_import` +
  `source1import -usefilelist`) so the addon `content\` tree is fully populated,
  but the three per-asset `resourcecompiler` passes (materials, models, the refs
  filelist) only run when `CompileAssets` is set. Off ⇒ a fast "import sources
  now, compile later in Hammer" port; the cheap `F_FORCE_UV2` `.vmat` source
  patching still runs so a later compile is correct, and the second `source1import`
  re-run still runs (it picks up the *imported* material sources). This is finer
  than `SkipDeps` (which skips importing deps entirely) and orthogonal to the
  main-map `CompileMapAsync` (CLI-only `--compile`; the GUI has no map-compile
  toggle). Exposed as the GUI **Compile Assets** checkbox and CLI
  `--no-compile-assets` (CLI defaults it *on* so its automatic validation has
  `_c` files to check).
- **Concurrency:** the dependency phase runs model import (`cs_mdl_import`) and
  material compile (`resourcecompiler`) in parallel via `Parallel.ForEachAsync`,
  bounded by `ImportOptions.MaxParallelism` (default: all logical processors minus
  one, min 1; 1 = sequential). The 2-UV model-compile pass stays sequential — it
  mutates the shared 2-UV list and shared `.vmat` files. Tunable via the CLI
  `--threads N` flag (same cores-minus-one default); the GUI has no thread control
  and always uses the default.
- [`BspDecompiler`](src/SourcePorter.Core/Toolchain/BspDecompiler.cs) — the
  `.bsp` bridge. Valve ships no Source 1 decompiler, so when the input is a
  `.bsp` the GUI/CLI first decompile it to a `.vmf` (via the bundled BSPSource)
  and then feed that into the normal import path above. BSPSource is shipped as a
  **single self-contained `bspsrc.exe`** under `tools/bspsrc/` (its own bundled
  JRE — no system Java needed); `ResolveExe` finds it next to the app. BSPSource
  exits 0 even on a per-file failure, so the missing-output check — not the exit
  code — is the real gate. See §4b for how that exe is built. After decompile it
  runs `VmfNormalizer.EnsureImportableHeader` (below) and, when requested, extracts
  the BSP's embedded content so the addon is self-contained (returned via
  `BspDecompileResult.UnpackDir`).
- **Full BSP unpack (`BspPakfile`).** SourcePorter does **not** use BSPSource's
  `--unpack_embedded`: its "smart" unpack drops vbsp-generated materials — notably the
  `materials/maps/<map>/…_wvt_patch.vmt` worldvertextransition patches the map's brush faces
  actually reference. Instead `BspDecompiler` reads the BSP's pakfile lump directly
  ([`BspPakfile`](src/SourcePorter.Core/Toolchain/BspPakfile.cs), LUMP_PAKFILE = a ZIP) and
  `ExtractAll` writes **every** packed file to the unpack dir. Verified on de_gracia: 6264 embedded
  files (316 `.vmt`, 542 `.vtf`) extracted in full, vs the subset BSPSource's filtered unpack wrote.
  The same reader powers the missing-asset repair's BSP-embedded source search (§7a).
- [`VmfNormalizer`](src/SourcePorter.Core/Toolchain/VmfNormalizer.cs) — minimal,
  lossless `.vmf` fix-ups that make a decompiled map importable **without** the
  manual "open and re-save in Hammer" step. A decompiled `.vmf` starts straight at
  `world`, omitting the `versioninfo`/`visgroups`/`viewsettings` preamble Hammer
  writes; `source1import` then fails with *"CVMFtoVMAP: Missing a required
  top-level key"*. `EnsureImportableHeader` prepends that preamble if absent
  (idempotent; leaves a file that already has it byte-for-byte unchanged).
  Verified end-to-end: with the preamble the map and all its dependency refs
  import. Hammer's geometry re-save and the Hammer++ `*_plus` blocks are **not**
  required. This is the single source of truth shared by both staging paths.
  `EnsureDisplacementOffsets` is the second fix-up: BSPSource drops the
  `offsets`/`offset_normals` subkeys from decompiled `dispinfo` displacements (they
  are zero/default in the compiled BSP), but `source1import` requires them — without
  them it logs *"Found a displacement missing a needed subkey"* and **discards the
  displacement, so terrain disappears**. It injects neutral defaults (zero offsets,
  `0 0 1` offset normals) sized to each displacement's power; the terrain shape (from
  the present `normals`+`distances`) is unchanged. Idempotent — a normal Hammer `.vmf`
  already has the subkeys, so it's a no-op there. Applied to the temp/decompiled copy
  only, never the user's original. (Verified on a real decompiled map: 181/181
  displacements repaired.)
  `EnsureNoUnresolvableColorCorrection` is the third fix-up: a decompiled map keeps its
  `color_correction` entity pointing at a Source 1 lookup `.raw`
  (e.g. `materials/correction/cc_coastal.raw`) that BSPSource does **not** unpack, and
  `source1import` **access-violates** (*"RelativePathToFullPath failed"*, exit
  `-1073741819`) when it tries to import that missing file — after the `.vmap` is written
  but before the refs list, so the entire dependency import is lost. It removes only the
  `color_correction` entities whose `.raw` is absent from the (unpacked) content root;
  one whose file *is* present is left untouched, so color correction survives when
  BSPSource did unpack it. Run from `BspDecompiler` against the unpack dir (it needs the
  content root, unlike the other two text-only fix-ups). (Verified end-to-end on a real
  decompiled map: with the unresolvable `color_correction` stripped, the map imports and
  writes its refs instead of crashing.)
- [`MapStaging`](src/SourcePorter.Core/Toolchain/MapStaging.cs) — the source-map
  stager shared by the GUI and CLI. `source1import` requires the map at
  `<contentdir>\maps\<name>.vmf` (it derives the content dir + map name by
  splitting at `maps\`; see `Cs2Install.TryParseSourceMap`). `StageVmf` leaves a
  correctly-structured, already-importable `.vmf` in place; anything else (a loose
  `.vmf`, or one missing the preamble) is copied into a fresh per-map temp content
  root (`%TEMP%\SourcePorter\<map>\maps\`) and run through `VmfNormalizer` there —
  so the user's own file is never mutated. `StageBspAsync` decompiles a `.bsp`
  into that temp root and adopts BSPSource's unpack dir (which already holds
  `materials\`/`models\`/`maps\`) as the content root, then drops the decompiled
  `.vmf` under its `maps\`. This keeps the user's folders untouched and gives each
  `.bsp` a single self-contained content root.

The import sequence we reproduce, in order:

1. `source1import -retail -nop4 -nop4sync [-usebsp|-usebsp_nomergeinstances] -src1gameinfodir <s1> -src1contentdir <s1content> -s2addon <addon> -game csgo maps\<map>.vmf`
2. Strip MDLs out of `<map>_prefab_refs.txt` → `_mdl_lst.txt` + `_new_refs.txt`.
3. For each model: `cs_mdl_import -nop4 -i <s1> -o <s2content> <model>`, collect
   material refs, force `F_FORCE_UV2` on 2-UV materials, then
   `resourcecompiler` each `.vmat`/`.vmdl`.
4. Import non-model refs: `source1import … -usefilelist <new_refs>` then
   `resourcecompiler -f -filelist <compiled refs>`.
5. Re-run step 1 so the vmf picks up the now-compiled dependencies.
6. Copy the main `.vmap` from `content/.../maps` to `game/csgo/maps`.

**Working directory & configs.** The importer runs with its working directory
set to the **bundled** `import_scripts/` folder (Valve's `source1import_*.txt`
exclusion/material lists, copied from CS2 into `tools/import_scripts/` and shipped
next to the exe — see §4a). `source1import` resolves those lists from the cwd,
and the appended `source1import_2uvmateriallist.txt` lives there too, so the run
behaves exactly like the original scripts. The **Configs Editor** window edits
these files in place.

**Environment & known workarounds:**

- ✅ Set `VALVE_NO_AUTO_P4=1` (the script's `SaveEnv`/`RestoreEnv`) so the P4
  libs run disconnected.
- ⬜ *(planned)* **`vpk.signatures` workaround** (guide §-1.1): detect the
  CS:GO `.vpk` read failure and offer to rename `game/bin/win64/vpk.signatures`
  (and undo it).
- ⬜ *(planned)* Detect the **fatal missing-material** condition (§-1.2) by
  checking `ErrorMaterialIsFatalError` in `gameinfo.gi` before a run.

### 4a. Bundled configs

`tools/import_scripts/` holds copies of Valve's importer configs and scripts
(`source1import_*.txt`, the `.py` sources, `utlc.py`). The `.txt` configs are
`CopyToOutputDirectory` content, landing in `import_scripts/` beside the exe;
[`AppPaths.ImportScriptsDir`](src/SourcePorter.App/AppPaths.cs) points the
importer there. The `.py` files are kept for reference (we reimplemented them in
C#) and are not used at runtime. Valve's `bin/` binaries are **not** copied —
they're resolved from the user's CS2 install via `ValveToolLocator`.

### 4b. Bundled BSPSource (`tools/bspsrc/`)

[`BspDecompiler`](src/SourcePorter.Core/Toolchain/BspDecompiler.cs) shells out to
**BSPSource** ([ata4/bspsrc](https://github.com/ata4/bspsrc), GPL-3.0), a Java
`.bsp`→`.vmf` decompiler. We deliberately **do not reimplement** it — we bundle
the upstream tool. Because BSPSource ships as a *jlink runtime image* (its own JRE
+ app modules, ~160 files), [`tools/bspsrc-launcher`](tools/bspsrc-launcher) folds
that whole image into one self-contained `bspsrc.exe`: a tiny .NET stub embeds the
image, extracts it to a per-user cache on first run, and forwards all args/stdio to
the BSPSource CLI through the bundled JRE. The committed deliverable is the single
`tools/bspsrc/bspsrc.exe`, copied next to the app/CLI as `CopyToOutputDirectory`
content. The launcher project is **not** in `SourcePorter.sln` (build-only); the
upstream `bspsrc-windows.zip` build input is git-ignored. To refresh BSPSource, see
[`tools/bspsrc-launcher/README.md`](tools/bspsrc-launcher/README.md).

---

> **Sections 6 and 8 describe planned modules beyond the importer** (the guide's
> entity remapping and packaging). They are on the roadmap and not yet built.
> **Section 7 (asset validation) IS implemented**, and **§5's `Vmap/` post-import
> tools (collapse prefabs, skybox template) ARE implemented** — the remaining §5
> entity/PostImport fix-ups are not.

## 5. File formats *(partially implemented)*

### Source 1 `.vmf` (`Vmf/`)

`.vmf` is Valve KeyValues v1 (brace-delimited, text). We ship a small,
purpose-built reader/writer — we only need entity/output/side traversal and
faithful round-tripping, not a full editor. Pre-import analysis and stripper
application operate on this tree. Round-tripping must preserve unknown keys.

### Source 2 `.vmap` (`Vmap/`) — partially implemented

The uncompiled `.vmap` (in `content/`) is a **Datamodel/DMX graph** (the imported
main map is text KeyValues2; its sub-maps are binary DMX). We read and write it via
the **KeyValues2** package (`Datamodel.Load`/`Save` — the same serializer VRF uses),
operating on the generic `Datamodel.Element` graph rather than porting VRF's typed
`CMap*` classes. [`VmapDocument`](src/SourcePorter.Core/Vmap/VmapDocument.cs) wraps a
loaded map (its `world` element + `children` node array) and re-saves preserving the
file's original encoding+version so CS2/Hammer still read it.

Two **post-import tools** run automatically after an import when their checkbox/flag
is set (orchestrated by the GUI/CLI like the validator, via the
[`PostImportVmapTools`](src/SourcePorter.Core/Vmap/PostImportVmapTools.cs) façade):

- [`VmapPrefabCollapser`](src/SourcePorter.Core/Vmap/VmapPrefabCollapser.cs) —
  **Collapse prefabs**: merges the map's prefab/sub-map references (the auto-split
  gameplay / environment / lighting / cubemap `.vmap`s, and any `CMapPrefab` node)
  into the root map's world via the library's cross-document `ImportElement`, then
  removes the reference. The sub-map files are **kept on disk** (just unreferenced).
- [`VmapSkyboxTemplate`](src/SourcePorter.Core/Vmap/VmapSkyboxTemplate.cs) —
  **Skybox template**: scaffolds the Source 2 3D-skybox setup — an empty
  `<map>_sky.vmap` flagged as a skybox (`mapUsageType = "skybox"`) plus a
  `skybox_reference` entity at `0 0 0` in the main map pointing at it via
  `targetmapname`. Idempotent (won't clobber an existing `_sky.vmap` or add a second
  reference). The user fills the sky geometry in Hammer.

Both back up the main `.vmap` before overwriting it (`VmapBackup`, §11).

A third tool runs **automatically after a BSP import** (not a checkbox — it's a correctness fix,
like validation):

- [`VmapBrushUvFixer`](src/SourcePorter.Core/Vmap/VmapBrushUvFixer.cs) — **Brush UV scale fix**:
  walks every `.vmap` in the addon (main + prefab sub-maps) and corrects the UV mapping of brush
  faces whose material source1import couldn't read. Those `CDmePolygonMesh` faces are baked at a
  fixed **16×16** texture size (source1import's fallback when `GetMappingDimensionsForVMT` fails),
  so the fixer reads the real `.vtf` dimensions from the staged content (`VmtFile` → `$basetexture`
  → `VtfHeader`) and, per face: (1) corrects `textureScale` back to the true S1 scale (× `realDim/16`),
  then (2) **recomputes** each corner's baked `texcoord` from the corrected mapping the same way
  Hammer does — `u = (dot(P, axisU.xyz)/scale.u + axisU.w)/textureWidth`, `v` likewise (`P` = vertex
  position via the half-edge streams). The texcoord is *recomputed, not scaled*: the renderer uses
  the baked texcoords directly, and the per-face offset (`axis.w/dim`) and slope (`1/(scale·dim)`)
  transform differently, so scaling the 16-default texcoords cannot reproduce the right value.
  **Confirmed against a Hammer-corrected `.vmap`** (the user nudged + reverted UVs so Hammer re-baked
  them): the formula reproduces Hammer's texcoords exactly (`blend_roofing_tile_01`,
  `(11.7148/0.125 + 97.82)/1024 = 0.18705`). Only materials whose `.vtf` is in the staged content
  are touched — i.e. exactly the BSP's custom materials — so stock faces are never affected. The
  texture axes are already correct (dimension-independent) and left as-is. Loads each map via
  `VmapDocument.LoadInMemory` (binary DMX `.vmap`s load deferred and would lock the file otherwise),
  backs up before overwriting, and guards against double-application with a `<vmap>.spuvfix` sidecar
  marker (the `textureScale` step isn't idempotent; a re-import rewrites the `.vmap` newer than the
  marker so the fix re-runs on fresh data). GUI: runs automatically for BSP imports; CLI:
  `--no-uv-fix` to opt out.

**Still planned** here: the guide's entity/PostImport fix-ups (remove `(null)`
params, rewrite classnames, add outputs, delete origin meshes — see §6). Compiled
`.vmap_c`/`.vpk` artifacts remain read-only inputs for the asset audit.

### 5a. Non-binary material conversion (`Materials/`) — implemented

A **C# port of the VMT→VMAT parameter mapping** from
[kristiker/source1import](https://github.com/kristiker/source1import) (MIT) — a standalone
Python reimplementation of Valve's importer. The point is a material path that does **not**
shell out to `source1import.exe`: it gives the missing-asset repair (§7a) a binary-free option
and is the basis for fixing the custom brush-material scale the toolchain defaults (§4, "Custom-
material scale" limitation).

- [`VmtFile`](src/SourcePorter.Core/Materials/VmtFile.cs) — parses a Source 1 `.vmt` (KV1, via
  `ValveKeyValue`) into a case-insensitive scalar bag, with the same normalisations as the Python
  `VMT` class (`$bumpmap`→`$normalmap`, `sdk_`/`_dx9`/`_hdr` shader-name stripping, `%compileclip`
  expansion, and `patch` resolution: load `include`, apply `replace`/`insert`).
- [`ShaderMapping`](src/SourcePorter.Core/Materials/ShaderMapping.cs) — `shaderDict` +
  `chooseShader()` with **every build-branch conditional collapsed to SourcePorter's target**
  (CS2 with `CSGO_EXPORT_TOUCHSTONE`, i.e. the Source-1-ported `csgo_*` shaders that match what
  Valve's own `source1import` emits — `csgo_lightmappedgeneric`, `csgo_vertexlitgeneric`, …).
- [`VmtToVmatConverter`](src/SourcePorter.Core/Materials/VmtToVmatConverter.cs) — the
  `vmt_to_vmat_pre()` tables + `convertVmtToVmat()` loop. Ported faithfully: the `features`,
  `textures`, `transform` (the scale/offset that drives **correct UV scaling**), `settings`, and
  `SystemAttributes` groups, with the value transforms (`fixVector`, `float_val`, `bool_val`,
  `mapped_val`, `uniform_vec2`, the `TexTransform` legacy-matrix parser) in
  [`MaterialValues`](src/SourcePorter.Core/Materials/MaterialValues.cs). Output is built by
  [`VmatDocument`](src/SourcePorter.Core/Materials/VmatDocument.cs) (a `Layer0 { }` block).
  **Known limitation:** the `channeled_masks` group (extracting one channel of a texture into a new
  image — e.g. roughness from base-color alpha) needs per-pixel work and is **not** ported; those
  entries are skipped rather than emitting a wrong whole-texture reference. Texture-group masks that
  already point at a separate mask texture (`$envmapmask`, `$ao`, `$phongmask`, …) **are** emitted.
  Normal maps are flagged `legacy_source1_inverted_normal` rather than re-encoding pixels.
- [`VtfHeader`](src/SourcePorter.Core/Materials/VtfHeader.cs) — a tiny `.vtf` header reader that
  recovers a texture's pixel **dimensions** without decoding the image body. This is the building
  block for correcting custom brush-material scale: Valve's `source1import` defaults the mapping
  size when it can't open a custom `.vmt`/`.vtf` (`GetMappingDimensionsForVMT: can't open …`);
  reading the real dimensions with no binary lets a future `.vmap` UV-rescale pass use the true
  size. (The converter itself is content-agnostic; the .vmap geometry rescale that consumes these
  dimensions is not yet built.)
- [`MaterialConverter`](src/SourcePorter.Core/Materials/MaterialConverter.cs) — file-level façade
  (`.vmt` path → `.vmat` text/file, with content-root include resolution and base-texture
  dimension lookup).
- [`ErrorVmatRemapper`](src/SourcePorter.Core/Materials/ErrorVmatRemapper.cs) — repairs `.vmat`
  files Valve's `source1import` left as `shader "error.vfx"` (it does that for shaders it can't map,
  e.g. `Water`). Those files embed the original material verbatim in a `legacy_import { }` block, so
  the remapper re-runs the converter over that embedded VMT to get the correct shader
  (`csgo_water`, …) and parameters, preserving the `legacy_import` block. No toolchain, and no need
  to locate the source `.vmt` — it's already in the file. Runs automatically after every import
  (before validation) and in the missing-asset repair.

**Tool-material detection is path-based** (ported faithfully from `is_tool_material`): a material is
a tool material only when it lives under `materials/tools/` with a `tools*` filename
(`VmtFile.IsToolMaterialPath`). It is **not** "has a `%compile*` key" — real surfaces like water
carry `%compilewater`/`%tooltexture` yet must map to `csgo_water`, not the `generic` tools shader.

**Relationship to the binary repair path.** The toolchain (`source1import.exe`/`cs_mdl_import.exe`)
remains the **primary** importer in the §7a repair, because it also transcodes the `.vtf` textures to
`.tga` — something this module does **not** do (it only reads VTF *headers*, not pixels). The
`Materials/` converter is therefore the **non-binary complement**: it produces correct `.vmat`
metadata (shaders, flags, UV scale/offset, surface props) without a CS2 toolchain, and `VtfHeader`
supplies the true texture dimensions that Valve's tools default when they can't open a custom
`.vmt`/`.vtf`. The brush-material UV-scale fix currently shipped via the csgo-named junction over the
staged content (§4) stays the production path; this converter is the foundation for a future fully
non-binary import/repair (it would need a VTF→TGA transcoder — e.g. bundling `vtf2tga` like BSPSource
in §4b — before it can stand alone).

---

## 6. Entity remapping (`Entities/`)

The S1→S2 entity rules (guide §1.3–1.4) are **data, not code** — an embedded
JSON ruleset, so contributors can extend it without recompiling. A rule
declares a match (classname, optional key/value predicate) and an action:

- `Rename` classname (e.g. `path_particle_rope` → `path_particle_rope_clientside`).
- `Replace` with a different entity + key remap.
- `AddOutput` (e.g. `prop_door_rotating` → `SetUnbreakable`).
- `Flag` as "no automated fix — manual action required" with the guide note
  (e.g. `player_speedmod`, `game_ui`, `point_viewcontrol_multiplayer`).

The engine applies matching rules to the parsed `.vmap` entity lump and produces
a report of what changed and what the user must still do by hand. Each rule
carries a guide §reference so the UI can link back to the rationale.

---

## 7. Asset validation (`Validation/`) — implemented

Validation is **content-driven**: it checks that every dependency the addon's
**content files** reference actually resolves. It runs **automatically at the end of
every import** (over the GUI's CS2 directory + output addon), reusing that run's
cancellation token so Cancel aborts it too — there is no separate menu action.

The primary pass scans each `.vmap` / `.vmdl` / `.vmat` in the addon's `content\` tree
and extracts the asset paths it references — prefab `.vmap`s, materials, models, and
model mesh sources — then verifies each:

- **Prefab `.vmap`s** (a map's `map_asset_references` — the cubemap/nav/lighting/
  gameplay/environment sub-maps) must exist in the addon content.
- **Materials / models** must be **imported into the addon** (a `.vmat`/`.vmdl` source)
  **or present in base CS2** (a compiled `_c` in a VPK); anything that is neither is the
  real failure — *"the map loads with missing textures/props"*.
- **Mesh sources** (`.dmx` / `.fbx` / `.smd` / `.obj`) a model references must exist
  loose, else it can't be recompiled.

This works without anything being compiled (it reads uncompiled content, not `_c`
files), so it is **not** gated on **Compile Assets**. A secondary pass still walks the
compiled `game\` `_c` resources' RERL when they exist. (A previous version validated
only the compiled `game\` tree, so an import with dependencies skipped — 0 `_c` files —
falsely reported "passed" while the map was missing every material/model it needs; the
content scan is what fixes that.)

The content files are read **byte-safely** because a main `.vmap` is text (KV2) but its
prefab `.vmap`s are binary DMX — CS2 stores the referenced paths as plain strings in
both, so a single scan handles both encodings (verified against a real 34 MB binary
prefab: 357 model + 193 material refs extracted cleanly).

How it works (informed by studying VRF under `reference/`):

- [`Source2Resource`](src/SourcePorter.Core/Validation/Source2Resource.cs) — a
  minimal reader for the Source 2 resource container that extracts the **RERL**
  block: the authoritative list of files a compiled resource depends on. Binary
  layout mirrors VRF's `Resource` header + `ResourceExtRefList.Read`.
  (ValveKeyValue can't parse this container, and the uncompiled `.vmap` is DMX,
  not KV — so this small reader fills the gap.)
- [`VpkIndex`](src/SourcePorter.Core/Validation/VpkIndex.cs) — uses **ValvePak**
  to mount base CS2 `*_dir.vpk` archives plus the addon's loose game dir, and
  answers `Exists(path)`.
- [`GameInfo`](src/SourcePorter.Core/Validation/GameInfo.cs) /
  [`AddonInfo`](src/SourcePorter.Core/Validation/AddonInfo.cs) — use
  **ValveKeyValue** to read `gameinfo.gi` (which archives to mount) and
  `addoninfo.txt` (the addon title).
- [`ContentReferenceScanner`](src/SourcePorter.Core/Validation/ContentReferenceScanner.cs)
  — byte-safe extractor that pulls the prefab-map / material / model / mesh-source
  paths out of a content `.vmap`/`.vmdl`/`.vmat` (text **or** binary), classifying each
  by its asset root + extension.
- [`AssetValidator`](src/SourcePorter.Core/Validation/AssetValidator.cs) — runs the
  content-reference pass and the secondary compiled-RERL pass, reporting
  `MissingPrefab` / `MissingImport` / `MissingSource` / `MissingReference` / `ReadError`
  in a `ValidationReport`. Each reference issue carries the bare asset path
  (`AssetIssue.ReferencePath`) so a remediation pass can act on it without re-parsing
  the human-readable detail. **Detection only — it never touches the toolchain.**
- [`AddonStats`](src/SourcePorter.Core/Validation/AddonStats.cs) — not validation, but
  shown alongside it: walks the content + compiled trees and reports the addon size and
  `.vmat` / `.vmdl` / `.vmap` / texture / `_c` counts at the end of an import.

Verified end-to-end on real addons (e.g. a clean addon: 11 base archives
mounted, 15 resources, 43 references, 0 missing).

### 7a. Repairing missed imports (`MissingAssetImporter`)

Validation regularly finds `MissingImport` materials/models that are **transitive
dependencies** the main import never enumerated: a model's gib/breakpiece children
(`styrofoam_cups_p1.vmdl`, `table_picnic_break01.vmdl`, …) referenced *inside* the
parent `.vmdl`, or a skybox material referenced only by a lighting prefab. These are
stock CS:GO assets — their Source 1 sources exist — so they *can* be re-imported.

[`MissingAssetImporter`](src/SourcePorter.Core/Toolchain/MissingAssetImporter.cs) is
the **separate remediation step** (kept out of `AssetValidator`, which stays
detect-only and toolchain-free). It maps each missing `.vmdl`/`.vmat` back to its
Source 1 `.mdl`/`.vmt` (`PlanFromReport`, pure/unit-tested), drives
[`MapImportService.ImportSpecificAssetsAsync`](src/SourcePorter.Core/Toolchain/MapImportService.cs)
— which reuses the dependency-phase model/material passes (`cs_mdl_import`, the
material filelist `source1import`, the `F_FORCE_UV2` 2-UV fix, optional
`resourcecompiler` per `ImportOptions.CompileAssets`) — then **re-validates and loops
to a fixpoint**, because a freshly-imported model can reveal its own missing children.
An `attempted` set ensures each source is tried once (an asset with no S1 source stays
missing and is reported honestly, never faked); that also bounds the loop, with a
`maxRounds` cap as backstop.

**Source search (CS:GO VPKs + custom content + BSP embedded).** Before firing the toolchain at a
mapped source, the repair confirms the file actually exists via
[`S1SourceLocator`](src/SourcePorter.Core/Toolchain/S1SourceLocator.cs), which searches the stock
CS:GO `*_dir.vpk` archives (mounted with `ValvePak` via the shared `VpkIndex`) **and** the map's
**custom content**: the staged content root (a decompiled BSP's unpacked `materials\`/`models\`),
any extra content folders (e.g. the original `.bsp`'s own directory), and the original `.bsp`'s
**embedded pakfile read directly** ([`BspPakfile`](src/SourcePorter.Core/Toolchain/BspPakfile.cs) —
LUMP_PAKFILE, a ZIP) so a file BSPSource didn't unpack is still found. A custom asset that exists
**only** in the BSP pakfile (not loose on disk) is **extracted into the staged content root** first
— the `.mdl` with its `.vvd`/`.vtx`/`.phy` siblings, the `.vmt` with the `.vtf` textures it
references — so `cs_mdl_import`/`source1import` (which read from disk) can consume it. Custom content
wins over stock (a map's own `de_coastal\…` overrides the base game), matching Valve's search-path
resolution. Only sources that resolve are imported; the
repair tallies how many came from VPKs vs custom content (`SourcedFromCsgoVpk` /
`SourcedFromCustomContent`) and collects the genuinely-sourceless ones in `Unsourced` — reported
honestly rather than firing the tools at files that don't exist (which would just log
`*** Error Importing`). This is also why the GUI re-stages the current source map for the repair
(`MainForm.StageCurrentSourceAsync`): so `S1ContentDir` points at the same custom-content root the
import used.

**Per-group gameinfo routing.** Stock and custom sources must import through *different* gameinfo
dirs, so the repair calls `ImportSpecificAssetsAsync` once per group: **stock** (CS:GO VPK) assets
with `stockOnly: true` → the real `csgo` gameinfo dir, which is what mounts `pak01_dir.vpk`;
**custom** (BSP-unpacked) assets → the staged content root's custom gameinfo (`PrepareDepsGameDir`).
Routing stock assets through the staged custom gameinfo (as an earlier version did for *all* groups)
makes `source1import` resolve nothing and log `*** Found no files matching specification …` — even
though the `.vmt` exists in a mounted VPK. The `S1SourceLocator` classification is exactly what
selects the right dir per asset.

**Tool materials → non-binary fallback.** `source1import` **blacklists tool materials**
(`tools/toolsclip_*`, …) and refuses to import them (`Removing blacklisted file from import …`,
then `*** Found no files matching specifications`). So the repair pulls tool materials out of both
source groups (`ExtractToolMaterials` — gated on a `materials/tools/` path or `%compile*` keys in the
`.vmt`) and converts them with the **non-binary** [`MaterialConverter`](src/SourcePorter.Core/Materials/MaterialConverter.cs)
(§5a) instead: it reads the `.vmt` straight from the VPK (`S1SourceLocator.TryReadText` →
`VpkIndex.TryReadBytes`) and writes a correct, texture-free `generic.vfx` tools `.vmat` into the addon
content tree (`MaterialsConvertedNonBinary`). This is the first production use of the `Materials/`
converter, and the reason it doesn't need a VTF→TGA transcoder here — tool materials have no textures.

It is a **manual, on-demand step**, not part of the import:
the GUI exposes it under **Tools → Import missing assets** (validates the current
CS2 directory + output addon, then repairs any `MissingImport` issues), and the CLI as
the `--repair` flag (which runs after the post-import validation). It never runs
automatically as part of an import.

**Planned extensions:** dedupe vs base CS2 (Overridden/Read-Only, §2.4), 2-UV
`F_FORCE_UV2` checks, foliage wind-sway flags, and parsing
`mat_print_error_materials` output (§-1.2).

---

## 8. Packaging (`Packaging/`)

- Parse the `gameinfo.gi` packing whitelist (include/exclude paths, §1.18.1) and
  let the user toggle additions (e.g. widen `panorama/images/overheadmaps` to
  `panorama` for loading-screen assets).
- Walk the addon `game/` folder, **exclude `.los`** and unused compiled assets,
  and either shell out to `vpk.exe` or write the archive directly via VRF's
  package support.
- The Workshop upload itself is done by Valve's Workshop Manager inside the CS2
  tools; SourcePorter prepares the addon and links out (guided step).

---

## 9. UI architecture (`SourcePorter.App`)

WinForms with a deliberately thin code-behind. The shell
([`MainForm`](src/SourcePorter.App/MainForm.cs)) is a single importer screen:

- a **menu** (`File`, `Tools`, `Help`) — themed with `DarkToolStripRenderer` —
  with **Import missing assets** (§7a) and the **Configs Editor** window under
  `Tools`, and the **Reference** window under `Help`; asset validation (§7) is no
  longer a menu action — it runs automatically after each import (always, since the
  model-source pass works without any compiled `_c` files), followed by the **addon
  statistics** summary (size + `.vmat`/`.vmdl`/`.vmap`/texture counts);
- an **input form** (top): **CS2 Directory**, **Source Map** (`.vmf`), **Output
  Addon**, the BSP / skip-deps / **Compile Assets** / **Compact log** option
  checkboxes (with the importer's mutual-exclusion), and **Import** / **Cancel**
  buttons (the dependency phase always uses all logical processors minus one — there
  is no GUI thread control);
- a **dark console** (fill) the import output streams into;
- a **status bar**.

The Import button validates the inputs via
[`Cs2Install`](src/SourcePorter.Core/Domain/Cs2Install.cs), builds a
`PortProject`, then runs `MapImportService.ImportAsync` on a background task with
a `CancellationTokenSource`. `MapImportService.OnLog` is marshalled back to the
UI thread (`Control.BeginInvoke`) and colour-coded in the console (errors red,
banners muted). Auxiliary windows:

- [`ReferenceForm`](src/SourcePorter.App/ReferenceForm.cs) — field help (from the
  original importer tooltips) + the guide's useful links/tools, with clickable URLs.
- [`ConfigsEditorForm`](src/SourcePorter.App/ConfigsEditorForm.cs) — lists and
  edits the bundled `source1import_*.txt` configs in the working dir.

### Theme (`Theme/Themer.cs`)

A **1:1 port of the Source 2 Viewer's `GUI/Utils/Themer.cs`** (ValveResourceFormat,
MIT) — not an approximation:

- Exact dark palette: `App (22,25,32)`, `AppMiddle (34,39,51)`,
  `AppSoft (44,49,61)`, `Border (51,57,74)`, `Contrast White`,
  `ContrastSoft (158,159,164)`, `HoverAccent (0,66,151)`, `Accent (99,161,255)`.
  The light palette and `AppTheme`/`ThemeColors` shape are carried over verbatim.
- `Themer.InitializeTheme()` resolves the theme and calls .NET 9's
  `Application.SetColorMode(...)` — this is how the OS chrome (title bar,
  scrollbars, context menus) is themed, exactly as the original does. Called
  once in `Program.Main` before any form is shown.
- `Themer.ApplyTheme(form)` → `ThemeControl` → `ThemeControlInternal` recurses
  the control tree with the same per-control-type styling as upstream.
- `DarkToolStripRenderer` + `CustomColorTable` theme menus/toolbars, ported
  verbatim. `UnstyledPanel` is the opt-out marker (matches VRF).

Branches for VRF-only custom controls (CodeTextBox, BetterListView, the custom
control box, …) are omitted because SourcePorter doesn't ship those types.
**When extending the theme, copy from the actual Source 2 Viewer rather than
inventing colours or styles.**

#### Icons

The UI uses the Source 2 Viewer's own SVG icons (`GUI/Icons/*.svg` in VRF, MIT),
rendered exactly as upstream does:

- The relevant icons are embedded under
  [`src/SourcePorter.App/Icons/`](src/SourcePorter.App/Icons) as
  `EmbeddedResource`s.
- `Themer.SvgToSkiaBitmap` / `GetSvgBitmap` / `GetIcon` (ported from VRF's
  Themer) rasterise them at the requested size with **Svg.Skia 5.0.0** (the same
  package VRF uses) — DPI-aware via `AdjustForDPI`.
- Icons are used on the **Import** button (`Decompile`), the menu items
  (`Settings` on Configs Editor, `Find` on Reference), and the console header
  (`Log`, `ClearLog`).
- The VRF brand logo is intentionally **not** used as SourcePorter's identity.
  SourcePorter's app icon — [`app.svg`](src/SourcePorter.App/app.svg), rasterised
  to a multi-resolution `app.ico` (16–256 px) and wired as the project's
  `ApplicationIcon` and the window/taskbar icon — is an original composition on a
  dark Source 2 Viewer tile (dark `App`/`AppMiddle` gradient, `Border` stroke, a
  faint blue radial glow): a white **settings gear** (the import/automation
  toolchain) over a bright **Source 2-blue swoosh** with a white **"2"**
  (Counter-Strike 2). It is not Valve's logo. The same `docs/icon.png` (the 256-px
  render) is shown in the README. **`app.svg` is the single source of truth; both
  `app.ico` and `docs/icon.png` are rendered from it.** To regenerate after editing
  `app.svg`, rasterise with Svg.Skia — the package the app already references — at
  16/24/32/48/64/128/256 px, pack the frames into the `.ico`, and write the 256-px
  frame to `docs/icon.png`. There is no checked-in generator (it's a throwaway
  tool); don't hand-edit `app.ico` or `docs/icon.png`.

---

## 10. Settings & persistence

[`AppSettings`](src/SourcePorter.App/AppSettings.cs) persists the GUI inputs
(CS2 directory, source map, output addon, the import-option checkboxes, and the
thread count) to
`%APPDATA%\SourcePorter\settings.json` — the SourcePorter equivalent of Valve's
`import_map_community_gui_cfg.json`. Loaded on start; saved on field/option
change, on browse, before each import, **and** on close — so it survives even if
the process is killed (when `FormClosing` never fires). Serialised with
`System.Text.Json`; no registry use; corrupt files fall back to defaults.

---

## 11. Safety & conventions

- **Importing overwrites addon content**, exactly as the original tool does
  (which prompts "are you sure" first). SourcePorter currently skips that console
  gate because the user explicitly pressed Import; a confirmation prompt is a
  planned addition.
- **`source1import_2uvmateriallist.txt` is appended at runtime** in the bundled
  (output) configs copy, never the repo source.
- **Respect `-insecure`.** Any future feature that modifies game files
  (`gameinfo.gi`, `vpk.signatures`, FGD patches) must remind the user to launch
  CS2 with `-insecure` and keep the change reversible.
- **Nullable reference types on**, file-scoped namespaces, `var` for built-ins —
  enforced by [`.editorconfig`](.editorconfig).

---

## 12. Testing

- Core is headless and unit-tested. Current coverage: the `RefsFile`
  `importfilelist` round-trip and MDL split (the ported `utlc.py` helpers), and
  `Source2Resource` RERL parsing against a synthetic resource (CI-safe, no real
  `_c` files needed).
- Planned: `Cs2Install` source-map/path derivation, and process orchestration
  against a fake tool stub to verify argument construction and output capture
  without a CS2 install.
- Fixtures are tiny synthetic snippets, **not** real copyrighted maps. Real maps
  are never committed (see [`.gitignore`](.gitignore)).

---

## 13. Out of scope / non-goals

- Editing geometry, lighting, or cubemap placement — that is Hammer's job.
- **Reimplementing** a `.bsp` or `.mdl` decompiler. We don't rewrite BSPSource —
  we **bundle** the upstream tool and drive it for `.bsp` input (see §4b).
  `.mdl` decompiling (Crowbar) stays a guided, upstream step.
- Cross-platform support — gated entirely by Valve's Windows-only tools.
- Particle authoring — the guide's particle work is Particle-Editor territory.
