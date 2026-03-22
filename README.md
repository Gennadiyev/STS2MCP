# Slay The Spire 2 - MCP Server

A mod for [**Slay the Spire 2**](https://store.steampowered.com/app/2868840/Slay_the_Spire_2/) that lets AI agents play the game. Exposes game state and actions via a localhost REST API, with an optional MCP server for Claude Desktop / Claude Code integration.

Singleplayer and multiplayer (co-op) supported. Tested against STS2 `v0.99.1`.

> [!warning]
> This mod allows external programs to read and control your game via a localhost API. Use at your own risk with runs you care less about.

> [!caution]
> Multiplayer support is in **beta** — expect bugs. Any multiplayer issues encountered with this mod installed are very likely caused by the mod, not the game. Please disable the mod and verify the issue persists before reporting bugs to the STS2 developers.

## For Players

### Install

1. Copy `STS2_MCP.dll` and `STS2_MCP.json` to `<game_install>/mods/`
2. Launch the game and enable mods in settings (a consent dialog appears on first launch)
3. The mod starts an HTTP server on `localhost:15526` automatically

### Connect to Claude

Requires [Python 3.11+](https://www.python.org/) and [uv](https://docs.astral.sh/uv/).

**Claude Code** — add to your project's `.mcp.json`:

```json
{
  "mcpServers": {
    "sts2": {
      "command": "uv",
      "args": ["run", "--directory", "/path/to/STS2_MCP/mcp", "python", "server.py"]
    }
  }
}
```

**Claude Desktop** — add to `claude_desktop_config.json` with the same config as above.

The MCP server accepts `--host` and `--port` flags if you need non-default settings.

Full tool reference: [mcp/README.md](./mcp/README.md) | Raw HTTP API: [docs/raw_api.md](./docs/raw_api.md)

## For Developers

### Build & Install

Requires [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

**PowerShell** (recommended):

```powershell
# Pass game path directly:
.\build.ps1 -GameDir "D:\SteamLibrary\steamapps\common\Slay the Spire 2"

# Or set it once and forget:
$env:STS2_GAME_DIR = "D:\SteamLibrary\steamapps\common\Slay the Spire 2"
.\build.ps1
```

**Bash** (from repo root):

```bash
./build.sh
```

Both scripts build `STS2_MCP.dll` into `out/STS2_MCP/`. Copy it along with the manifest JSON to `<game_install>/mods/` to install:

```
out/STS2_MCP/STS2_MCP.dll           ->  <game_install>/mods/STS2_MCP.dll
STS2_MCP/mod_manifest.json          ->  <game_install>/mods/STS2_MCP.json
```

### Features

**Singleplayer** — full coverage of all game screens:

Combat (play cards, use potions, end turn, in-combat card selection), rewards (claim, pick/skip cards), map navigation (full DAG with lookahead), rest sites, shop, events & ancients, card selection overlays (transform, upgrade, remove), relic selection, treasure rooms, keyword glossary across all entities.

**Multiplayer (beta)** — all singleplayer features plus:

End-turn voting (submit/undo), map node voting, shared event voting, treasure relic bidding, all-players state summary, per-player ready/vote tracking. Endpoints are mutually guarded (singleplayer endpoint rejects multiplayer runs and vice versa).

## License

MIT
