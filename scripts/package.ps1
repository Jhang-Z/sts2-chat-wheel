# Build the mod and package it as a distributable zip in .\dist\.
#
# Usage:  .\scripts\package.ps1
# Output: dist\sts2_chat_wheel-X.Y.Z.zip — users drop the inner folder
#         straight into their Slay the Spire 2 mods folder.

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$modId = "sts2_chat_wheel"

# Read version from manifest.json (single source of truth)
$manifest = Get-Content -Raw "$root\src\VoiceRoulette\manifest.json" | ConvertFrom-Json
$ver = $manifest.version
$stage = "$root\dist\$modId"
$outZip = "$root\dist\$modId-$ver.zip"

& dotnet build "$root\src\VoiceRoulette\VoiceRoulette.csproj" -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (Test-Path $stage)  { Remove-Item -Recurse -Force $stage }
if (Test-Path $outZip) { Remove-Item -Force $outZip }
New-Item -ItemType Directory -Force -Path "$stage\data" | Out-Null

Copy-Item "$root\src\VoiceRoulette\bin\Release\net9.0\VoiceRoulette.dll" "$stage\$modId.dll"
Copy-Item "$root\src\VoiceRoulette\manifest.json" "$stage\"
Copy-Item "$root\lines.default.json" "$stage\data\lines.default.jsonc"

# Zip with the inner sts2_chat_wheel\ folder at the top level.
Push-Location "$root\dist"
try {
    Compress-Archive -Path $modId -DestinationPath "$modId-$ver.zip" -Force
} finally {
    Pop-Location
}
Remove-Item -Recurse -Force $stage

Write-Host "✅ Built $outZip" -ForegroundColor Green
