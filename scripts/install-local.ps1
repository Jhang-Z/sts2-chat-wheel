# Build the mod and install it to your local StS2 mods folder (Windows).
#
# Usage:  .\scripts\install-local.ps1
# Requires: .NET 9 SDK, .\lib\*.dll already populated (run copy-libs.ps1 first)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$modId = "sts2_chat_wheel"

# Resolve the StS2 mods folder. Standard Steam path; $env:STS2_MODS_DIR overrides.
$candidates = @(
    "C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods",
    "C:\Program Files\Steam\steamapps\common\Slay the Spire 2\mods",
    "D:\Steam\steamapps\common\Slay the Spire 2\mods",
    "E:\Steam\steamapps\common\Slay the Spire 2\mods"
)
if ($env:STS2_MODS_DIR) { $candidates = @($env:STS2_MODS_DIR) + $candidates }

$modsDir = $null
foreach ($d in $candidates) {
    # Even if mods/ doesn't exist yet, the parent (the game install) should.
    $parent = Split-Path $d -Parent
    if (Test-Path $parent) { $modsDir = $d; break }
}
if (-not $modsDir) {
    Write-Host "❌ Could not find Slay the Spire 2 mods folder." -ForegroundColor Red
    Write-Host '   Set $env:STS2_MODS_DIR="D:\path\to\Slay the Spire 2\mods" and re-run.'
    exit 1
}

$dest = Join-Path $modsDir $modId
Write-Host "📦 Installing to: $dest" -ForegroundColor Cyan

# Build (Release)
& dotnet build "$root\src\VoiceRoulette\VoiceRoulette.csproj" -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Stage
New-Item -ItemType Directory -Force -Path $dest | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $dest "data") | Out-Null

Copy-Item -Force `
    -Path "$root\src\VoiceRoulette\bin\Release\net9.0\VoiceRoulette.dll" `
    -Destination "$dest\$modId.dll"
Copy-Item -Force `
    -Path "$root\src\VoiceRoulette\manifest.json" `
    -Destination "$dest\manifest.json"
Copy-Item -Force `
    -Path "$root\lines.default.json" `
    -Destination "$dest\data\lines.default.jsonc"

# v0.2: prerendered audio + textures dropped (runtime-drawn UI, on-demand TTS).
Remove-Item -Recurse -Force "$dest\prerendered" -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force "$dest\textures" -ErrorAction SilentlyContinue

Write-Host "✅ Installed." -ForegroundColor Green
Get-ChildItem $dest | Format-Table Name, Length
