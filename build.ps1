<#
.SYNOPSIS
    Builds the STS2_MCP mod DLL.

.DESCRIPTION
    Compiles STS2_MCP.dll against the game's assemblies. Does NOT install
    the mod — copy the output files to the game's mods/ directory yourself.

.PARAMETER GameDir
    Path to the Slay the Spire 2 installation directory.
    Falls back to the STS2_GAME_DIR environment variable if not specified.

.PARAMETER Configuration
    Build configuration (default: Release).

.EXAMPLE
    .\build.ps1 -GameDir "D:\SteamLibrary\steamapps\common\Slay the Spire 2"
    .\build.ps1  # uses $env:STS2_GAME_DIR
#>
param(
    [string]$GameDir,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

# --- Resolve game directory ---
if (-not $GameDir) {
    $GameDir = $env:STS2_GAME_DIR
}
if (-not $GameDir) {
    Write-Host @"
ERROR: Game directory not specified.

Provide it via parameter or environment variable:
  .\build.ps1 -GameDir "D:\SteamLibrary\steamapps\common\Slay the Spire 2"

Or set it once in your PowerShell profile:
  `$env:STS2_GAME_DIR = "D:\SteamLibrary\steamapps\common\Slay the Spire 2"
"@ -ForegroundColor Red
    exit 1
}

$dllDir = Join-Path $GameDir "data_sts2_windows_x86_64"
if (-not (Test-Path (Join-Path $dllDir "sts2.dll"))) {
    Write-Host "ERROR: Could not find sts2.dll in '$dllDir'." -ForegroundColor Red
    Write-Host "Make sure -GameDir points to the Slay the Spire 2 installation root." -ForegroundColor Red
    exit 1
}

# --- Check prerequisites ---
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host @"
ERROR: 'dotnet' not found.

Install the .NET 9 SDK from:
  https://dotnet.microsoft.com/download/dotnet/9.0
"@ -ForegroundColor Red
    exit 1
}

# --- Build ---
$scriptDir = $PSScriptRoot
$project = Join-Path $scriptDir "STS2_MCP.csproj"
$outDir = Join-Path $scriptDir ".." "out" "STS2_MCP"

Write-Host "=== Building STS2_MCP ($Configuration) ===" -ForegroundColor Cyan
Write-Host "Game directory : $GameDir"
Write-Host "Output         : $outDir"
Write-Host ""

dotnet build $project -c $Configuration -o $outDir -p:STS2GameDir="$GameDir"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "=== Build succeeded ===" -ForegroundColor Green
Write-Host "To install, copy these files to <game_install>/mods/:"
Write-Host "  $outDir\STS2_MCP.dll"
Write-Host "  $scriptDir\mod_manifest.json  ->  mods\STS2_MCP.json"
