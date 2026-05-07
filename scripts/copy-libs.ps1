# Copies the three game DLLs the mod project links against from a local
# Slay the Spire 2 install into .\lib\. Required before building from source.
#
# Usage:  .\scripts\copy-libs.ps1
#
# The DLLs are NOT redistributable (they're MegaCrit's). .gitignore keeps
# them out of git; each developer's machine grabs its own copy from their
# legally-purchased install.

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

# Common Steam install paths on Windows. First one with all 3 DLLs wins.
$candidates = @(
    "C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64",
    "C:\Program Files\Steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64",
    "D:\Steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64",
    "E:\Steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64"
)

$needed = @("GodotSharp.dll", "sts2.dll", "0Harmony.dll")

# Allow GAME_DIR env var override
if ($env:GAME_DIR) {
    $candidates = @($env:GAME_DIR) + $candidates
}

$foundDir = $null
foreach ($d in $candidates) {
    if (Test-Path $d) {
        $allThere = $true
        foreach ($f in $needed) {
            if (-not (Test-Path (Join-Path $d $f))) { $allThere = $false; break }
        }
        if ($allThere) { $foundDir = $d; break }
    }
}

if (-not $foundDir) {
    Write-Host "❌ Could not auto-find Slay the Spire 2 install." -ForegroundColor Red
    Write-Host "   Either install via Steam, or set GAME_DIR explicitly:"
    Write-Host '     $env:GAME_DIR="D:\Path\To\data_sts2_windows_x86_64"'
    Write-Host '     .\scripts\copy-libs.ps1'
    exit 1
}

Write-Host "📦 Found game DLLs at: $foundDir" -ForegroundColor Cyan
$libDir = Join-Path $root "lib"
New-Item -ItemType Directory -Force -Path $libDir | Out-Null
foreach ($f in $needed) {
    Copy-Item -Path (Join-Path $foundDir $f) -Destination (Join-Path $libDir $f) -Force
    Write-Host "  -> $f"
}
Write-Host "✅ Copied $($needed.Count) DLLs to $libDir. You can now build the mod." -ForegroundColor Green
