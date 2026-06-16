# Agent Guidelines

Rules for any AI coding agent (Claude, Codex, Gemini, ChatGPT, Copilot, etc.)
working in this repository. Read this file before committing or creating
branches.

## Project overview

**SourcePorter** is a Windows desktop application (C# / WinForms, .NET 9) that
guides and automates porting **Source 1 maps** (CS:GO / CS:S) to **Source 2**
(Counter-Strike 2). It wraps Valve's `import_map_community` toolchain
(`source1import`, `cs_mdl_import`, `resourcecompiler`, `vbsp`, `vpk`) and the
[ValveResourceFormat](https://github.com/ValveResourceFormat/ValveResourceFormat)
library in a single themed, observable pipeline, and automates the fix-ups
described in the community S2ZE Map Porting Guide. Read the full concept before
touching the pipeline — see [overview.md](overview.md).

@overview.md

## Architecture

The solution layout, the staged porting pipeline, the toolchain orchestration,
file-format handling (VMF / VMAP-KV3 via VRF), entity remapping, asset auditing,
packaging, the WinForms UI, and the conventions that keep the codebase safe
(Core has no UI dependency, back up before overwrite, surface manual steps) are
documented in [ARCHITECTURE.md](ARCHITECTURE.md). Read it before adding or
refactoring a subsystem, and keep it updated when the architecture changes. The
phased build order is in [ROADMAP.md](ROADMAP.md).

@ARCHITECTURE.md

## Build, run, test

From the repo root (or use the VS Code tasks of the same name):

- Build: `dotnet build SourcePorter.sln`
- Run the app: `dotnet run --project src/SourcePorter.App`
- Test: `dotnet test SourcePorter.sln`
- Publish (win-x64, self-contained): the `publish` VS Code task.

Conventions are enforced by [`.editorconfig`](.editorconfig): nullable reference
types on, file-scoped namespaces, `var` for built-in types. **`SourcePorter.Core`
must never reference WinForms** — keep porting logic headless and testable.

**Never commit real maps, VPKs, or game content.** Tests use tiny synthetic
fixtures only; real assets are git-ignored.

## Git contributors

- Do **not** add AI agents as authors or co-authors of any commit.
- Never append a `Co-Authored-By:` trailer (or any equivalent) referencing
  Claude, Codex, Gemini, ChatGPT, Copilot, or any other AI tool.
- Do not mention AI tools anywhere in commit messages.
- Every commit is authored solely by the human developer. Leave the author and
  committer set to the project's own git identity.

## Branches

- Use clean, descriptive names that reflect the actual work.
- No random, throwaway, or auto-generated names.
- Never include the name of any AI agent (`claude`, `codex`, `gemini`,
  `chatgpt`, `copilot`, …) anywhere in a branch name.
- Follow the convention `<type>/<short-description>`, where `<type>` is one of
  `feature`, `fix`, `chore`, `refactor`, or `docs`.
  Examples: `feature/import-orchestration`, `fix/vmap-null-params`.
