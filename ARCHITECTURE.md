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
| Source 2 assets    | **ValveResourceFormat** (NuGet)     | Reads/writes VPK, KV3, VMAP/VMDL/VMAT, textures, sounds. Same library that powers Source 2 Viewer. |
| Tests              | **xUnit**                           | Standard, fast, good CLI story. |
| Platform           | **Windows 10/11 x64**               | `source1import.exe`, `resourcecompiler.exe`, `cs_mdl_import.exe`, `vbsp.exe`, `vpk.exe` are all Win64. |

### Version constraint (important)

The newest ValveResourceFormat builds (≥ 16.x) require the **.NET 10 SDK**. We
are pinned to **`ValveResourceFormat` 15.0.4937**, the last release targeting
`net9.0`, because only the .NET 9 SDK is installed. When the .NET 10 SDK is
available, bump both the `TargetFramework` and the package version together.
This is the single point of coupling — see
[`SourcePorter.Core.csproj`](src/SourcePorter.Core/SourcePorter.Core.csproj).

---

## 2. Solution layout

```
SourcePorter.sln
├─ src/
│  ├─ SourcePorter.Core/        Class library — all porting logic, no UI.
│  │  ├─ Domain/                PortProject, ImportOptions, value types.
│  │  ├─ Pipeline/              Stage definitions + the pipeline engine.
│  │  ├─ Toolchain/             1:1 port of import_map_community.py + utlc.py.
│  │  │                         (ProcessRunner, ValveToolLocator, RefsFile,
│  │  │                          ImportPaths, MapImportService)
│  │  ├─ Vmf/        (planned)  Source 1 .vmf (KeyValues1) read/analyse/fix.
│  │  ├─ Vmap/       (planned)  Source 2 .vmap (KV3) read/fix via VRF.
│  │  ├─ Entities/   (planned)  Data-driven S1→S2 entity remap rules.
│  │  ├─ Assets/     (planned)  Material/model audit, dedupe, 2UV handling.
│  │  ├─ Packaging/  (planned)  gameinfo.gi whitelist, .vpk packing.
│  │  ├─ Stripper/   (planned)  Stripper .cfg parser + .vmf applier.
│  │  └─ Config/     (planned)  Settings + recent-project persistence.
│  └─ SourcePorter.App/         WinForms front-end.
│     ├─ Program.cs             Entry point.
│     ├─ MainForm.cs            Shell: stage nav + work area + console.
│     ├─ Theme/                 Source 2 Viewer dark theme.
│     └─ Views/      (planned)  One UserControl per pipeline stage.
└─ tests/
   └─ SourcePorter.Core.Tests/  xUnit tests for the Core library.
```

**Hard rule: `SourcePorter.Core` never references WinForms.** All file parsing,
process orchestration, and rules live in Core and are unit-testable headlessly.
The UI is a thin shell that binds to Core and renders progress. This is what
keeps the most fragile logic (entity remapping, VMAP rewriting) testable.

Shared MSBuild settings live in
[`Directory.Build.props`](Directory.Build.props); the SDK is pinned in
[`global.json`](global.json).

---

## 3. The porting pipeline

The whole app is organised around an ordered list of stages
([`PortStage`](src/SourcePorter.Core/Pipeline/PortStage.cs)), each mapping to a
section of the porting guide and to one screen in the UI. Every stage declares
whether each of its actions is **automated** (SourcePorter does it),
**assisted** (SourcePorter prepares/validates, user confirms), or **guided**
(must be done in Hammer; surfaced as a checklist item with instructions).

| Stage          | Guide § | Key actions | Mode |
| -------------- | ------- | ----------- | ---- |
| **Project**    | 1.2.1   | Configure 4 paths + addon/map names; validate tool presence. | Auto |
| **Stripper**   | 1.1     | Parse stripper `.cfg`, apply add/remove/modify to the `.vmf`, tag changed ents `"strippered" "1"`. | Auto |
| **PreImport**  | 1.2.1   | Scan `.vmf` for: broken/wildcard/reserved (`!self`…) outputs; `HINT`/`SKIP` textures on func ents; `func_wall`/`func_wall_toggle`; surf ramps → `func_brush`; HDR 2D skybox. Report + offer fixes. | Assisted |
| **Import**     | 1.2     | Run `source1import` → `cs_mdl_import` → `resourcecompiler`, re-run vmf import for deps, copy `.vmap` to `maps/`. Live captured log. | Auto |
| **PostImport** | 1.2.3   | Strip `(null)` output params; delete duplicate origin meshes (keep `env_sky`); flag static-overlay orientation/scale; flag `func_wall` dup meshes. | Assisted |
| **Entities**   | 1.3–1.4 | Apply S1→S2 remap rules (`path_particle_rope`→`_clientside`, `env_fire`/`env_spark`→particle, `func_water`, `prop_door_rotating` `SetUnbreakable`, random-teleport rebuild, `trigger_push`/`trigger_gravity` quirks). Report unfixables. | Assisted |
| **Assets**     | 1.8, 2.4| Parse `_refs.txt`/`_mdl_lst.txt`; detect missing textures (the *fatal* error, §-1.2); flag duplicates vs base CS2 VPK; flag 2-UV materials needing `F_FORCE_UV2`; list foliage losing wind sway. | Assisted |
| **Polish**     | 1.6–1.17| Checklist: cubemap/light-probe volumes, soundevents (`soundevents_addon.vsndevts`), minimap (`cs_minimap_boundaries`), nav (`point_nav_walkable`), loading screen, ZR `zr_toggle_respawn`. | Guided |
| **Package**    | 1.18    | Read/edit `gameinfo.gi` whitelist; walk addon `game/` folder; exclude `.los`/unused; build `.vpk`; hand off to Workshop Manager. | Auto/Guided |

### Pipeline engine

A stage is a sequence of steps. The engine (`Pipeline/`) runs steps with:

- **Progress + cancellation** via `IProgress<T>` and `CancellationToken`.
- **Structured logging** — every step emits log lines (and tool subprocess
  output) to a shared sink the UI renders as the dark console.
- **Idempotency where possible** — re-running PostImport/Entities on an
  already-fixed `.vmap` is a no-op, so the user can iterate.
- **Backups** — any step that rewrites a user file first copies it to
  `*.bak` (the guide repeatedly warns to "save a copy of the imported .vmap").

Steps are independent objects (`IPipelineStep` with a `StepResult`), so new
guide fix-ups are added as new steps without touching the engine.

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
  resolves the five tool paths from a CS2 install root and exposes the list the
  Project stage validates before anything runs.
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
  `Force2UVsIfRequired` ported faithfully; non-zero tool exits abort with
  `ImportToolException` (mirroring `utlc.Error`).

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

**Environment & known workarounds:**

- Set `VALVE_NO_AUTO_P4=1` (the script's `SaveEnv`/`RestoreEnv`) so the P4 libs
  run disconnected.
- Surface the **`vpk.signatures` workaround** (guide §-1.1): if `source1import`
  fails to read the CS:GO `.vpk`, prompt to rename
  `game/bin/win64/vpk.signatures`. SourcePorter detects the failure and offers
  to do (and later undo) this rather than leaving the user stuck.
- Detect the **fatal missing-material** condition (§-1.2) up front by checking
  `ErrorMaterialIsFatalError` in `gameinfo.gi` and warning before a port that
  would crash on a live server.

---

## 5. File formats

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

## 7. Asset audit (`Assets/`)

- **Missing textures** — parse the per-model `_refs.txt`/`_new_refs.txt`,
  cross-check compiled outputs, and (optionally) parse the output of
  `mat_print_error_materials` to find `[Error Resource]` lines. This is the
  fatal-crash guard from §-1.2.
- **Duplicate vs base CS2** — open the base CS2 `pak_dir.vpk` with VRF and flag
  imported materials/models that already exist natively (the "Overridden /
  Read-Only" duplicates of §2.4) so the user can delete bloat.
- **2-UV materials** — replicate `Force2UVsIfRequired`: inspect `meshinfo.txt`
  `numuvs == 2` and ensure `F_FORCE_UV2` in the `.vmat`; maintain the
  `source1import_2uvmateriallist.txt` equivalent.
- **Foliage wind sway** — flag imported foliage models that lose `env_wind`
  support (§2.4.2) for replacement.

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
([`MainForm`](src/SourcePorter.App/MainForm.cs)) hosts:

- a **stage navigator** (left) bound to `PortStage`,
- a **work area** (center) that swaps in one `UserControl` per stage,
- a **dark console** (bottom-right) subscribed to the pipeline's log sink,
- a **status bar**.

Each stage view is a self-contained `UserControl` that talks to Core services
through interfaces (a light MVP split — views hold no porting logic). Long
operations run on a background task; the runner marshals
`ProcessRunner.OnOutput` back to the UI thread for the console. The Project
stage's fields mirror Valve's importer GUI (s1 gameinfo dir, s1 content dir, s2
gameinfo dir, addon name, map name) so existing porters feel at home.

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
- [`StageInfo`](src/SourcePorter.App/StageInfo.cs) maps each `PortStage` to its
  icon (`Project`→FolderMap, `Import`→Decompile, `Package`→VPKCreate, …); the
  left navigator is a themed `TreeView` with those icons, and the console header
  reuses `Log`/`ClearLog`.
- The VRF brand logo is intentionally **not** used as SourcePorter's identity.

---

## 10. Settings & persistence (`Config/`)

- **Per-project**: `sourceporter.json` saved next to the addon — the 4 paths,
  addon/map names, import options, and per-stage completion state. This is the
  evolution of Valve's `import_map_community_gui_cfg.json`.
- **Per-machine**: `sourceporter.local.json` (git-ignored) for the user's
  CS:GO/CS2 install roots and recent projects.
- Serialised with `System.Text.Json`. No registry use.

---

## 11. Safety & conventions

- **Never overwrite without a backup.** Any file rewrite produces a `.bak`
  first. Destructive packing operates on a copy of the addon folder.
- **Surface, don't hide, manual steps.** Guided stages render the guide's
  instructions and a checkbox; they never silently pass.
- **Respect `-insecure`.** When an action requires modifying game files
  (`gameinfo.gi`, `vpk.signatures`, FGD patches), the UI reminds the user that
  CS2 must be launched with `-insecure` and that changes are reversible.
- **Honour the import tool's "are you sure" gate** rather than blindly
  overwriting addon content.
- **Nullable reference types on**, file-scoped namespaces, `var` for built-ins —
  enforced by [`.editorconfig`](.editorconfig).

---

## 12. Testing

- Core is headless and unit-tested: `.vmf` parse/round-trip, stripper
  application, entity-rule matching, gameinfo whitelist parse, 2-UV detection.
- Fixtures are tiny synthetic `.vmf`/KV3 snippets, **not** real copyrighted
  maps. Real maps are never committed (see [`.gitignore`](.gitignore)).
- Process orchestration is tested against a fake tool (an `echo`-style stub) so
  we verify argument construction and output capture without a CS2 install.

---

## 13. Out of scope / non-goals

- Editing geometry, lighting, or cubemap placement — that is Hammer's job.
- Decompiling Source 1 `.bsp` (use BSPSource) or `.mdl` (use Crowbar). These are
  upstream of SourcePorter and only referenced as guided steps.
- Cross-platform support — gated entirely by Valve's Windows-only tools.
- Particle authoring — the guide's particle work is Particle-Editor territory.
