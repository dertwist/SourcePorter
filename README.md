# SourcePorter

A Windows desktop tool for **porting Source 1 maps (CS:GO / CS:S) to Source 2
(Counter-Strike 2)**.

SourcePorter is a self-contained, themed front-end over Valve's
`import_map_community` toolchain — it drives `source1import` exactly as the
`import_scripts` shipped with CS2 do, but with a proper UI, a live console, and
the [Source 2 Viewer](https://s2v.app/) look. Valve's importer configs are
bundled, so it works standalone.

> **Status:** importer working (builds, themed, runs); pending a real
> end-to-end run on a CS2 install. See [ROADMAP.md](ROADMAP.md).

## What it does

Point it at three things and press **Import**:

- **CS2 Directory** — your Counter-Strike 2 install root.
- **Source Map** — the Source 1 `.vmf` to port.
- **Output Addon** — the target CS2 addon name.

It derives the rest, runs the full `source1import` → `cs_mdl_import` →
`resourcecompiler` sequence (with the 2-UV `F_FORCE_UV2` handling and re-import
pass), and streams the output to the console. Two helper windows:

- **Reference** — what each field means + the porting guide's useful links/tools.
- **Configs Editor** — edit Valve's bundled `source1import_*.txt` config lists.

**Tools → Validate Addon** checks the compiled addon's `.vmap_c`/`.vmdl_c`/
`.vmat_c` for missing referenced files — reading external references via a small
RERL reader, resolving them across the base VPKs with
[ValvePak](https://www.nuget.org/packages/ValvePak), and reading `gameinfo.gi`
with [ValveKeyValue](https://www.nuget.org/packages/ValveKeyValue).

See [overview.md](overview.md) and [ARCHITECTURE.md](ARCHITECTURE.md).

## Prerequisites

- Windows 10/11 (x64) — Valve's import tools are Windows-only.
- [.NET SDK 9.0](https://dotnet.microsoft.com/download) (pinned in `global.json`).
- Counter-Strike 2 installed with the Workshop Tools (provides
  `source1import.exe`, `cs_mdl_import.exe`, `resourcecompiler.exe`, `vpk.exe`),
  and a CS:GO install for source content.

## Build & run

```sh
dotnet build SourcePorter.sln              # build everything
dotnet run --project src/SourcePorter.App  # launch the app
dotnet test SourcePorter.sln               # run unit tests
```

In VS Code, the same operations are available as tasks
(`build`, `run`, `test`, `publish`, …) — `build` is the default build task.

## Project layout

```
src/SourcePorter.Core   Headless import logic (no UI) — toolchain port + Cs2Install.
src/SourcePorter.App     WinForms front-end + Source 2 Viewer theme/icons.
tools/import_scripts     Valve's source1import configs + scripts (bundled).
tests/…                  xUnit tests for Core.
```

`SourcePorter.Core` never references WinForms, so the import orchestration stays
unit-testable.

## Notes

- `ValveKeyValue` is pinned to **0.20.0.417** and `ValvePak` to **4.0.0.142**
  (the newest net8-compatible builds; later ones need the .NET 10 SDK). See
  [ARCHITECTURE.md §1](ARCHITECTURE.md).
- A read-only clone of ValveResourceFormat sits in `reference/` (git-ignored),
  used to learn the resource/RERL format — not a build dependency.
- **Never** commit real maps or game assets — they are git-ignored, and tests
  use synthetic fixtures only.
- Contributing? Read [AGENTS.md](AGENTS.md) first.

## Credits & licences

- [ValveResourceFormat / Source 2 Viewer](https://github.com/ValveResourceFormat/ValveResourceFormat)
  (MIT) — the dark theme (`Themer.cs`, ported 1:1), the UI icons
  (`GUI/Icons/*.svg`), and the resource/RERL format the validator reads.
- [ValvePak](https://www.nuget.org/packages/ValvePak) (MIT) — VPK reading.
- [ValveKeyValue](https://www.nuget.org/packages/ValveKeyValue) (MIT) — KV reading.
- The S2ZE community and the Map Porting Guide for the porting workflow this
  tool automates.
