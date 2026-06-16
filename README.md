# SourcePorter

A Windows desktop tool that **guides and automates porting Source 1 maps
(CS:GO / CS:S) to Source 2 (Counter-Strike 2)**.

SourcePorter wraps Valve's `import_map_community` toolchain and the
[ValveResourceFormat](https://github.com/ValveResourceFormat/ValveResourceFormat)
library in one themed, observable pipeline, and automates the fix-ups from the
community **S2ZE Map Porting Guide** — replacing a loose set of Python scripts,
log-less console windows, and manual Hammer chores with a single staged
application styled after the [Source 2 Viewer](https://s2v.app/).

> **Status:** Phase 0 (foundation) complete — buildable, themed skeleton.
> See [ROADMAP.md](ROADMAP.md) for what ships next.

## What it does (when complete)

Configure a project once, then walk the stages:

**Project → Stripper → Pre-import → Import → Post-import → Entities → Assets →
Polish → Package**

Each stage either does the work for you, prepares it for one-click confirmation,
or surfaces the guide's instructions as a checklist for the steps that genuinely
need Hammer. The import runs with full captured logs; known fatal foot-guns
(missing-material crashes, the `vpk.signatures` read bug) are detected and
handled. See [overview.md](overview.md) and [ARCHITECTURE.md](ARCHITECTURE.md).

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
src/SourcePorter.Core   Headless porting logic (no UI) — pipeline, toolchain, formats.
src/SourcePorter.App     WinForms front-end + Source 2 Viewer theme.
tests/…                  xUnit tests for Core.
```

`SourcePorter.Core` never references WinForms, so the fragile logic (entity
remapping, VMAP rewriting, orchestration) stays unit-testable.

## Notes

- ValveResourceFormat is pinned to **15.0.4937** (the last `net9.0` build);
  newer releases need the .NET 10 SDK. See
  [ARCHITECTURE.md §1](ARCHITECTURE.md).
- **Never** commit real maps or game assets — they are git-ignored, and tests
  use synthetic fixtures only.
- Contributing? Read [AGENTS.md](AGENTS.md) first.

## Credits & licences

- [ValveResourceFormat / Source 2 Viewer](https://github.com/ValveResourceFormat/ValveResourceFormat)
  (MIT) — Source 2 asset library, the dark theme (`Themer.cs`, ported 1:1), and
  the UI icons (`GUI/Icons/*.svg`, embedded under `src/SourcePorter.App/Icons/`).
- The S2ZE community and the Map Porting Guide for the porting workflow this
  tool automates.
