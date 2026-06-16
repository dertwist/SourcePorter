# Roadmap

SourcePorter's core is a **self-contained importer** that runs Valve's
`source1import` toolchain like `import_scripts`. That importer is the product;
the guide's post-import work is layered on later as optional tools. Guide §
references point at the S2ZE Map Porting Guide.

Status legend: ✅ done · 🔨 in progress · ⬜ not started

---

## Phase 0 — Foundation ✅

- ✅ Solution: `Core` (net9.0) + `App` (net9.0-windows WinForms) + xUnit tests.
- ✅ ValveResourceFormat pinned (15.0.4937, the last net9.0 build).
- ✅ Source 2 Viewer theme ported **1:1** from VRF `Themer.cs`
  (exact palette + `Application.SetColorMode` + `DarkToolStripRenderer`).
- ✅ Source 2 Viewer SVG icons embedded + rendered via Svg.Skia (`Themer.GetIcon`).
- ✅ VS Code tasks (build/rebuild/restore/clean/run/test/publish) + launch.
- ✅ `.gitignore`, `.editorconfig`, `global.json`, `Directory.Build.props`.

---

## Phase 1 — The importer ✅

The focused GUI that mirrors `import_scripts`.

- ✅ Import orchestration ported **1:1** from `import_map_community.py` +
  `utlc.py` (`RefsFile`, `ImportPaths`, `MapImportService`,
  `ProcessRunner`, `ValveToolLocator`).
- ✅ Valve's `source1import_*.txt` configs bundled (`tools/import_scripts/`),
  shipped beside the exe and used as the importer working dir.
- ✅ GUI: CS2 directory, source `.vmf`, output addon, BSP/skip-deps options,
  **Import**/**Cancel**, live colour-coded console; inputs persisted
  (`AppSettings`).
- ✅ `Cs2Install` derives the four importer paths from the three inputs.
- ✅ **Reference** window (field help + guide links) and **Configs Editor**
  window (edit the bundled `source1import_*.txt`).

**Acceptance:** SourcePorter imports a real CS:GO map to a CS2 addon, output
streamed to the console, matching the Python tool. *(pending a real end-to-end
run on a CS2 install — wiring and arg construction verified.)*

---

## Phase 2 — Importer hardening ⬜

- ⬜ Real end-to-end run against a CS2 install; reconcile any arg/path
  differences with the Python tool.
- ⬜ Auto-detect the `vpk.signatures` read failure and offer the rename
  workaround/undo (§-1.1).
- ⬜ Pre-flight `ErrorMaterialIsFatalError` warning (§-1.2).
- ⬜ Save each run's full log to a file; confirmation prompt before overwriting
  existing addon content.
- ⬜ Tests: `Cs2Install` path/source-map derivation; orchestration vs a fake tool.

---

## Phase 3 — Asset validation 🔨

Check a compiled addon for errors / missing files. Built on **ValvePak** +
**ValveKeyValue** + a small VRF-derived RERL reader (`Validation/`).

- ✅ Read `.vmap_c`/`.vmdl_c`/`.vmat_c` external references (RERL) and verify each
  resolves in the addon or a mounted base VPK; report missing / unreadable.
  **Tools → Validate Addon**; verified end-to-end on real addons.
- ⬜ Dedupe vs base CS2 (Overridden/Read-Only, §2.4); 2-UV `F_FORCE_UV2` checks;
  foliage wind-sway flags; parse `mat_print_error_materials` (§-1.2).
- ⬜ A dedicated results window (grouped by resource, jump-to-file) instead of the
  console dump.

---

## Future — optional post-import tools ⬜

The guide's later fix-ups, layered on **only if** we grow beyond importer +
validation. Each is a separate, opt-in tool — never silent automation of manual
Hammer steps.

- ⬜ **VMF pre-import analysis** (§1.1–1.2.1): stripper `.cfg` application;
  scan for broken/reserved outputs, `HINT`/`SKIP`, `func_wall`, surf ramps.
- ⬜ **Post-import `.vmap` fix-ups** (§1.2.3): strip `(null)` params, delete
  origin meshes (the uncompiled `.vmap` is DMX — needs a Datamodel reader).
- ⬜ **Entity remapping** (§1.3–1.4): data-driven S1→S2 rules + report.
- ⬜ **Packaging** (§1.18): `gameinfo.gi` whitelist edit, `.los`/unused exclusion,
  `.vpk` build.

---

## Cross-cutting

- Keep `Core` free of WinForms; keep logic unit-testable.
- Never commit real maps or game assets.
- Update [ARCHITECTURE.md](ARCHITECTURE.md) when a subsystem's shape changes.
- Re-pin VRF + `TargetFramework` to net10 once the .NET 10 SDK is installed.
