# SourcePorter — Overview

**SourcePorter** is a Windows desktop application (C# / WinForms) that guides and
automates porting **Source 1 maps** (Counter-Strike: Global Offensive, and by
extension Counter-Strike: Source) into **Source 2** (Counter-Strike 2).

It is built around the workflow documented in the community
**S2ZE — Map Porting Guide** and Valve's own `import_map_community` toolchain,
wrapping them in a single themed, observable, resumable pipeline instead of a
loose collection of Python scripts, console windows, and manual Hammer steps.

## Why it exists

Porting a CS:GO map to CS2 today means:

- Running Valve's `import_map_community.py` (a thin orchestrator over
  `source1import.exe`, `cs_mdl_import.exe`, `resourcecompiler.exe`, and
  `vbsp.exe`) from a console, with **no logging** and a 9,999-line terminal cap.
- Hand-applying dozens of fix-ups described across an 80-page guide:
  removing `(null)` output parameters, deleting duplicate origin meshes,
  remapping entities that no longer exist in Source 2, fixing overlay
  orientation, and so on.
- Auditing materials and models for missing textures (which, since a December
  2023 update, are a **fatal** error that crashes live servers).
- Editing `gameinfo.gi` whitelists and packing the addon into a `.vpk` for the
  Steam Workshop.

SourcePorter turns that into a staged tool: configure the project once, watch
each step run with full captured logs, review and one-click-apply the
guide's known fix-ups, audit assets against the base CS2 archives, and pack the
result — all in an interface styled after the **Source 2 Viewer**.

## What it is and is not

- **It is** an orchestrator, analyzer, and fix-up assistant. It drives Valve's
  official import tools, reads and rewrites the resulting files, and surfaces a
  checklist of everything the guide says to do.
- **It is not** a replacement for Hammer. Steps that genuinely require the
  Source 2 Hammer editor (placing cubemap volumes, collapsing prefabs, building
  lighting) are surfaced as guided, checkable instructions — not silently
  skipped and not faked.

## Audience

Map porters working on CS2 (the Source 2 Zombie Escape community and adjacent
custom-gamemode mappers). Users are assumed to already know S1 Hammer, S2
Hammer, and the importer basics — SourcePorter removes the tedium and the
foot-guns, not the need to understand mapping.

## Reference material

- The guide itself (kept out of the repo; it is a third-party document).
- Valve's `import_scripts/` shipped with CS2 — the canonical behaviour
  SourcePorter's import orchestrator mirrors.
- [ValveResourceFormat / Source 2 Viewer](https://github.com/ValveResourceFormat/ValveResourceFormat)
  — the library used to read/write Source 2 assets, and the visual theme
  reference. Web build: <https://s2v.app/>.

See [ARCHITECTURE.md](ARCHITECTURE.md) for how the code is organised and
[ROADMAP.md](ROADMAP.md) for the phased build plan.
