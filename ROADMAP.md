# Roadmap

The full guided pipeline, built in phases. Each phase is independently useful
and shippable. Stage names refer to
[`PortStage`](src/SourcePorter.Core/Pipeline/PortStage.cs); guide § references
point at the S2ZE Map Porting Guide.

Status legend: ✅ done · 🔨 in progress · ⬜ not started

---

## Phase 0 — Foundation ✅

Repository, solution skeleton, theme, and tooling so every later phase has a
green build to land on.

- ✅ Solution: `Core` (net9.0) + `App` (net9.0-windows WinForms) + xUnit tests.
- ✅ ValveResourceFormat pinned (15.0.4937, the last net9.0 build).
- ✅ Source 2 Viewer theme ported **1:1** from VRF `Themer.cs`
  (exact palette + `Application.SetColorMode` + `DarkToolStripRenderer`).
- ✅ Source 2 Viewer SVG icons embedded + rendered via Svg.Skia
  (`Themer.GetIcon`); stage navigator + console use them.
- ✅ `MainForm` shell: stage navigator + work area + dark console + status bar.
- ✅ `ProcessRunner` (streaming, cancellable) and `ValveToolLocator`.
- ✅ Import orchestration ported **1:1** from `import_map_community.py` +
  `utlc.py` (`RefsFile`, `ImportPaths`, `MapImportService`) — not yet UI-wired.
- ✅ VS Code tasks (build/rebuild/restore/clean/run/test/publish) + launch.
- ✅ `.gitignore`, `.editorconfig`, `global.json`, `Directory.Build.props`.

**Acceptance:** `dotnet build` and `dotnet test` are green; the app launches
themed. *(met)*

---

## Phase 1 — Project setup & tool validation ⬜

Make the **Project** stage real.

- ⬜ `Config/`: load/save `sourceporter.json` + per-machine `sourceporter.local.json`.
- ⬜ Project view: the five path/name fields (mirroring Valve's importer GUI)
  with folder pickers and inline validation.
- ⬜ Validate the five Valve tools via `ValveToolLocator`; show a clear
  red/green checklist.
- ⬜ Pre-flight checks: `gameinfo.gi` `ErrorMaterialIsFatalError` warning (§-1.2)
  and `vpk.signatures` detection (§-1.1).
- ⬜ Recent-projects list.

**Acceptance:** a valid project can be created, persisted, reopened, and its
toolchain verified — no import yet.

---

## Phase 2 — Import orchestration ⬜

Make the **Import** stage real — the heart of the app.

- ⬜ `Pipeline/` engine: steps, `StepResult`, progress, cancellation, log sink.
- ✅ Re-implement `import_map_community.py` end-to-end using `ProcessRunner`
  (vmf→vmap, MDL strip, model import + 2-UV force, ref import, recompile,
  re-import, copy to `maps/`). Set `VALVE_NO_AUTO_P4=1`. *(in `MapImportService`)*
- ⬜ Wire `MapImportService` to the Import view: bind its output to the console,
  surface progress/cancellation, run a real map end-to-end.
- ⬜ Import options UI (`-usebsp` / `-usebsp_nomergeinstances` / `-skipdeps`)
  with the same mutual-exclusion as the original GUI.
- ⬜ Live dark console bound to subprocess output; full run saved to a log file.
- ⬜ Auto-detect the `vpk.signatures` read failure and offer the rename
  workaround (and undo).

**Acceptance:** SourcePorter imports a real CS:GO map to a CS2 addon with
captured logs, matching the Python tool's output.

---

## Phase 3 — VMF pre-import analysis ⬜

Make **Stripper** + **PreImport** real (operate on `.vmf` before importing).

- ⬜ `Vmf/`: KeyValues1 reader/writer with faithful round-trip.
- ⬜ `Stripper/`: parse stripper `.cfg`, apply add/remove/modify, tag changed
  entities `"strippered" "1"` (§1.1).
- ⬜ PreImport scanners (§1.2.1): broken/wildcard/reserved (`!self`…) outputs;
  `HINT`/`SKIP` textures on func ents; `func_wall`/`func_wall_toggle`; surf
  ramps; HDR 2D skybox.
- ⬜ One-click fixes where safe (surf ramps → `func_brush`, `HINT`/`SKIP` →
  `NODRAW`) with `.bak` backups; report the rest.

**Acceptance:** a stripper-dependent map is fixed up and validated pre-import,
with a clear report of remaining manual work.

---

## Phase 4 — Post-import .vmap fix-ups & entity remapping ⬜

Make **PostImport** + **Entities** real (operate on the imported `.vmap`).

- ⬜ `Vmap/`: load/save KV3 entity lump via VRF, with backups.
- ⬜ PostImport (§1.2.3): strip `(null)` output params; delete duplicate origin
  meshes (preserve `env_sky`); flag overlay orientation/scale & `func_wall`
  duplicates.
- ⬜ `Entities/`: embedded JSON remap ruleset + engine (rename/replace/addoutput/
  flag) covering §1.3–1.4; report unfixables with guide links.

**Acceptance:** an imported `.vmap` opens in S2 Hammer with no `(null)` params,
no stray origin meshes, and known entities remapped.

---

## Phase 5 — Asset audit ⬜

Make the **Assets** stage real.

- ⬜ Parse `_refs.txt`/`_mdl_lst.txt`; detect missing textures (the fatal error,
  §-1.2).
- ⬜ Open base CS2 `pak_dir.vpk` via VRF; flag imported assets duplicating
  native ones (§2.4).
- ⬜ Replicate 2-UV `F_FORCE_UV2` detection/maintenance.
- ⬜ Flag foliage models that lose `env_wind` sway.

**Acceptance:** a per-map report of missing textures, removable duplicates, and
2-UV/foliage issues.

---

## Phase 6 — Polish checklist & packaging ⬜

Make **Polish** + **Package** real.

- ⬜ Polish: interactive checklist for cubemaps/light probes, soundevents,
  minimap, nav, loading screen, ZR `zr_toggle_respawn` (§1.6–1.17), each with
  guide text and completion state saved in the project.
- ⬜ `Packaging/`: parse/edit `gameinfo.gi` whitelist; copy addon, exclude
  `.los`/unused, build `.vpk` (vpk.exe or VRF); link to Workshop Manager.

**Acceptance:** a finished addon is packed to a clean `.vpk` ready for the
Workshop.

---

## Phase 7 — Quality & release ⬜

- ⬜ Broaden unit coverage (vmf round-trip, rules, whitelist, 2-UV).
- ⬜ App icon + branding; single-file `win-x64` publish via the `publish` task.
- ⬜ Sample synthetic fixtures and an end-to-end smoke test against a fake tool.
- ⬜ User-facing docs / quick-start.

---

## Cross-cutting (every phase)

- Keep `Core` free of WinForms; keep logic unit-testable.
- Back up before overwriting; never commit real maps or game assets.
- Update [ARCHITECTURE.md](ARCHITECTURE.md) when a subsystem's shape changes.
- Re-pin VRF + `TargetFramework` to net10 once the .NET 10 SDK is installed.
