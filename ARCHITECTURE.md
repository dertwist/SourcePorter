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
│  │  └─ Validation/            Asset validator: missing-file / read-error checks.
│  │                            (Source2Resource, VpkIndex, GameInfo, AssetValidator)
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
- **Concurrency:** the dependency phase runs model import (`cs_mdl_import`) and
  material compile (`resourcecompiler`) in parallel via `Parallel.ForEachAsync`,
  bounded by `ImportOptions.MaxParallelism` (default 4; 1 = sequential). The 2-UV
  model-compile pass stays sequential — it mutates the shared 2-UV list and shared
  `.vmat` files. Exposed as CLI `--threads N` and a GUI Threads spinner.
- [`BspDecompiler`](src/SourcePorter.Core/Toolchain/BspDecompiler.cs) — the
  `.bsp` bridge. Valve ships no Source 1 decompiler, so when the input is a
  `.bsp` the GUI/CLI first decompile it to a `.vmf` (via the bundled BSPSource)
  and then feed that into the normal import path above. BSPSource is shipped as a
  **single self-contained `bspsrc.exe`** under `tools/bspsrc/` (its own bundled
  JRE — no system Java needed); `ResolveExe` finds it next to the app. BSPSource
  exits 0 even on a per-file failure, so the missing-output check — not the exit
  code — is the real gate. See §4b for how that exe is built. Two fix-ups run
  after decompile: `PatchVmfHeader` prepends the `versioninfo`/`visgroups`/
  `viewsettings` preamble BSPSource omits (without it `source1import` rejects the
  vmf with *"CVMFtoVMAP: Missing a required top-level key"*); and, when requested,
  `--unpack_embedded` extracts the BSP's packed materials/models so the addon is
  self-contained (returned via `BspDecompileResult.UnpackDir`).
- [`MapStaging`](src/SourcePorter.Core/Toolchain/MapStaging.cs) — the source-map
  stager shared by the GUI and CLI. `source1import` requires the map at
  `<contentdir>\maps\<name>.vmf` (it derives the content dir + map name by
  splitting at `maps\`; see `Cs2Install.TryParseSourceMap`). `StageVmf` leaves a
  correctly-structured `.vmf` in place but copies a loose one into a fresh per-map
  temp content root (`%TEMP%\SourcePorter\<map>\maps\`). `StageBspAsync`
  decompiles a `.bsp` into that temp root and adopts BSPSource's unpack dir (which
  already holds `materials\`/`models\`/`maps\`) as the content root, then drops
  the decompiled `.vmf` under its `maps\`. This keeps the user's own folders
  untouched and gives each `.bsp` a single self-contained content root.

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

> **Sections 5, 6, and 8 describe planned modules beyond the importer** (the
> guide's pre/post-import fix-ups and packaging). They are on the roadmap and not
> yet built. **Section 7 (asset validation) IS implemented.**

## 5. File formats *(planned)*

### Source 1 `.vmf` (`Vmf/`)

`.vmf` is Valve KeyValues v1 (brace-delimited, text). We ship a small,
purpose-built reader/writer — we only need entity/output/side traversal and
faithful round-tripping, not a full editor. Pre-import analysis and stripper
application operate on this tree. Round-tripping must preserve unknown keys.

### Source 2 `.vmap` (`Vmap/`)

The uncompiled `.vmap` (in `content/`) is **KeyValues3 (KV3)**. We use
ValveResourceFormat's KV3 reader/writer to load the entity lump, apply
PostImport/Entities fix-ups (remove `(null)` params, rewrite classnames, add
outputs, delete origin meshes), and write it back. Compiled `.vmap_c`/`.vpk`
artifacts are read-only inputs for the asset audit (e.g. inspecting
`maps/lightmaps/irradiance.vtex_c`, comparing against base CS2 assets).

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

Checks a compiled addon's `.vmap_c` / `.vmdl_c` / `.vmat_c` resources for
**missing files** and **read errors**. It runs **automatically at the end of a
successful import** (over the GUI's CS2 directory + output addon), reusing that
run's cancellation token so Cancel aborts it too — there is no separate menu
action.

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
- [`AssetValidator`](src/SourcePorter.Core/Validation/AssetValidator.cs) — for
  each resource, reads its RERL and checks every reference (with `_c` appended)
  resolves in a loose dir or VPK; reports `MissingReference` / `ReadError` in a
  `ValidationReport`.

Verified end-to-end on real addons (e.g. a clean addon: 11 base archives
mounted, 15 resources, 43 references, 0 missing).

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
  with the **Configs Editor** window under `Tools` and the **Reference** window
  under `Help`; asset validation (§7) is no longer a menu action — it runs
  automatically after each import;
- an **input form** (top): **CS2 Directory**, **Source Map** (`.vmf`), **Output
  Addon**, the BSP/skip-deps option checkboxes (with the importer's
  mutual-exclusion), and **Import** / **Cancel** buttons;
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
  to a multi-resolution `app.ico` and wired as the project's `ApplicationIcon` and
  the window/taskbar icon — places the **Source 2 "S²" mark** on a dark Source 2
  Viewer tile (dark `App`/`AppMiddle` gradient, `Border` stroke). The glyphs are
  lightened from the brand gray so they read on the dark tile, and the signature
  orange swoosh is kept.

---

## 10. Settings & persistence

[`AppSettings`](src/SourcePorter.App/AppSettings.cs) persists the GUI inputs
(CS2 directory, source map, output addon, the three import options) to
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
