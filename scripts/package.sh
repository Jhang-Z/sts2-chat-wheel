#!/usr/bin/env bash
# Build the mod and package it as a distributable zip in ./dist/.
#
# Usage:  ./scripts/package.sh
# Output: dist/sts2_chat_wheel-X.Y.Z.zip — users drop the inner folder
#         straight into their Slay the Spire 2 mods folder.

set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
MOD_ID="sts2_chat_wheel"
VER=$(grep '"version"' "$ROOT/src/VoiceRoulette/manifest.json" | head -1 | cut -d'"' -f4)
STAGE="$ROOT/dist/$MOD_ID"
OUT_ZIP="$ROOT/dist/${MOD_ID}-${VER}.zip"

# Optional: macOS Homebrew dotnet@9 path. Skip if dotnet is already on PATH.
if [ -x "/opt/homebrew/opt/dotnet@9/bin/dotnet" ]; then
    export PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH"
    export DOTNET_ROOT="/opt/homebrew/opt/dotnet@9/libexec"
fi

dotnet build "$ROOT/src/VoiceRoulette/VoiceRoulette.csproj" -c Release
rm -rf "$STAGE" "$OUT_ZIP"
mkdir -p "$STAGE/data"

# Game resolves DLL by mod id (lowercase), not the assembly name.
cp "$ROOT/src/VoiceRoulette/bin/Release/net9.0/VoiceRoulette.dll" "$STAGE/$MOD_ID.dll"
cp "$ROOT/src/VoiceRoulette/manifest.json" "$STAGE/"
# .jsonc extension avoids the game's mod-manifest scanner (only picks up .json).
# This is a reference doc — the real config lives at data/config.jsonc and
# gets generated on first launch.
cp "$ROOT/lines.default.json" "$STAGE/data/lines.default.jsonc"

# Zip so the top-level entry is sts2_chat_wheel/ — users extract and drop
# the whole folder into their mods directory, no renaming needed.
(cd "$ROOT/dist" && zip -r "${MOD_ID}-${VER}.zip" "$MOD_ID")
rm -rf "$STAGE"
echo "✅ Built $OUT_ZIP"
