# Contributing to STS2 MCP

Thanks for the interest. This is a small, experimental project, and pull requests, bug reports, and ideas are all welcome. This document is the short version of how to land a change without friction.

By participating, you agree to follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## What we're looking for

- **Bug fixes.** Reproducible reports with a referenced issue or a clear in-game repro.
- **Features and new API endpoints.** New state fields, new actions, new screens — please [open a Discussion or issue first](#discussion-first-for-non-trivial-changes) so we can align on scope before you write the PR.
- **Docs.** README, the `docs/raw-*.md` reference, fixes to copy or examples. Doc-only PRs don't need a build.

### What's better as a Discussion than a PR

- **AI gameplay strategy** — the `CLAUDE.md` / `GUIDE.md` strategy content is opinionated and curated. Please [start a Discussion](https://github.com/Gennadiyev/STS2MCP/discussions) instead of opening a PR. Good ideas get folded in.

## Discussion-first for non-trivial changes

If your PR adds a new endpoint, a new state field, a new action, or otherwise changes the API surface, please open a [Discussion](https://github.com/Gennadiyev/STS2MCP/discussions) or an issue first. Two reasons:

1. The state/action surface is consumed by external clients (MCP, downstream forks like TwitchPlaysSTS2, custom scripts). Coordinated changes are easier than rolled-back ones.
2. Some ideas are already on a list and would conflict with planned work.

Small, obvious fixes (typos, null guards, doc clarifications) don't need this — just send the PR.

## Before you open a PR

Run through this checklist for any non-trivial change:

- [ ] **Clean build.** `./build.ps1 -GameDir "<your game path>"` (or `dotnet build` on macOS/Linux — see [README](README.md#build-instructions-for-macos)) returns zero warnings, zero errors.
- [ ] **Manual in-game smoke test.** Launch the game with the mod installed and exercise the affected feature path. Most of this codebase can't be unit-tested — your manual check is the test.
- [ ] **Tested against the latest stable game version.** The version we currently target is listed in the README's "Tested against" line. If a new game patch broke something, that's a separate issue worth filing.
- [ ] **Docs updated.** If you added or changed an API field/action, update `docs/raw-full.md` and `docs/raw-simplified.md` in the same PR. The MCP server docstrings (`mcp/server.py`) often need a touch too.

## Workflow

1. **Fork and branch.** External contributors fork the repo and work on a topic branch. Maintainers handle merges to `main`.
2. **Keep PRs focused.** One logical change per PR. If you find an unrelated thing to fix, that's a second PR.
3. **Reference issues.** If your PR fixes an issue, write `Fixes #N` (or `Refs #N` for partial work) in the description.
4. **Squash on merge.** Default merge strategy is squash. Your branch's commits don't need to be polished — your PR title and description do.
5. **Commit messages.** Write them for humans. A clear sentence about what changed and why beats a `type(scope):` prefix every time. No required convention — just make it readable.

## The Python MCP server (`mcp/server.py`)

In scope for contributions. **Architectural rule: the Python layer is a thin pass-through to the C# HTTP API. No game logic lives in Python.**

Concretely, that means:

- Each MCP tool maps to one HTTP request against `localhost:15526`.
- Schema validation, defaults, and small ergonomic transforms (e.g. parameter coercion) belong in Python.
- Anything that reads game state, mutates game state, or interprets game rules belongs in C#.

When in doubt, put it in C#. The Python server is replaceable; the mod is not.

## Reporting bugs

Open a [GitHub issue](https://github.com/Gennadiyev/STS2MCP/issues) using the bug report template. Helpful details:

- Game version (the README explains how to find it)
- Mod version (`curl http://localhost:15526/` returns it)
- OS and .NET version
- Repro steps that don't depend on a specific run state if possible
- Relevant console / log output

For multiplayer issues, please verify the bug **also happens with the mod disabled** before reporting to the STS2 developers (see the README's caution box).

## Setup

Build instructions, install paths (Windows/macOS/Linux), and MCP client config all live in the [README](README.md). They're not duplicated here so there's only one source of truth.

## License

This project is MIT-licensed. By submitting a contribution you agree your work is offered under the same license. There is no separate CLA to sign.
