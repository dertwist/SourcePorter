# SourcePorter — Overview

**SourcePorter** is a Windows desktop application (C# / WinForms) that guides and
automates porting **Source 1 maps** (Counter-Strike: Global Offensive, and by
extension Counter-Strike: Source) into **Source 2** (Counter-Strike 2).

At its core it is a **themed front-end over Valve's `import_map_community`
toolchain** — it drives `source1import` exactly as the `import_scripts` shipped
with CS2 do, but with a real UI, a live console, and bundled configs so it runs
standalone. The broader S2ZE porting workflow (post-import fix-ups, asset audit,
packaging) is layered on later as optional tools.

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

SourcePorter replaces the console invocation with a single window: set the CS2
directory, the source `.vmf`, and the output addon, press **Import**, and watch
the full toolchain run with captured, colour-coded output — styled after the
**Source 2 Viewer**. A Reference window explains the fields and links the guide's
tools; a Configs Editor edits Valve's bundled `source1import_*.txt` lists.

## What it is and is not

- **It is** a faithful, themed runner for Valve's import toolchain — same command
  sequence and configs as `import_scripts`, just usable.
- **It is not** a replacement for Hammer. The guide's post-import fix-ups,
  asset audit, and packaging are deliberately out of scope for the importer;
  they're optional future tools, never silent automation of manual Hammer work.

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
