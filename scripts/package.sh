#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
MOD_ID="sts2_chat_wheel"
VER=$(grep '"version"' "$ROOT/src/VoiceRoulette/manifest.json" | head -1 | cut -d'"' -f4)
STAGE="$ROOT/dist/$MOD_ID"
OUT_ZIP="$ROOT/dist/${MOD_ID}-${VER}.zip"

export PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH"
export DOTNET_ROOT="/opt/homebrew/opt/dotnet@9/libexec"

dotnet build "$ROOT/src/VoiceRoulette/VoiceRoulette.csproj" -c Release
rm -rf "$STAGE" "$OUT_ZIP"
mkdir -p "$STAGE/data" "$STAGE/prerendered"

# Game expects DLL filename to match mod id.
cp "$ROOT/src/VoiceRoulette/bin/Release/net9.0/VoiceRoulette.dll" "$STAGE/$MOD_ID.dll"
cp "$ROOT/src/VoiceRoulette/manifest.json" "$STAGE/"
cp "$ROOT/lines.default.json" "$STAGE/data/lines.default.jsonc"

if [ -d "$ROOT/assets/prerendered" ]; then
    cp "$ROOT/assets/prerendered/"*.mp3 "$STAGE/prerendered/"
fi

(cd "$ROOT/dist" && zip -r "${MOD_ID}-${VER}.zip" "$MOD_ID")
rm -rf "$STAGE"
echo "Built $OUT_ZIP"
