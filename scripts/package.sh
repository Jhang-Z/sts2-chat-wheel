#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VER=$(grep '"version"' "$ROOT/src/VoiceRoulette/manifest.json" | head -1 | cut -d'"' -f4)
OUT="$ROOT/dist/voice_roulette-$VER"

export PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH"
export DOTNET_ROOT="/opt/homebrew/opt/dotnet@9/libexec"

dotnet build "$ROOT/src/VoiceRoulette/VoiceRoulette.csproj" -c Release
rm -rf "$OUT" && mkdir -p "$OUT"
cp "$ROOT/src/VoiceRoulette/bin/Release/net9.0/VoiceRoulette.dll" "$OUT/"
cp "$ROOT/src/VoiceRoulette/manifest.json" "$OUT/"
cp "$ROOT/lines.default.json" "$OUT/"
[ -f "$ROOT/src/VoiceRoulette/bin/Release/net9.0/MessagePack.dll" ] && cp "$ROOT/src/VoiceRoulette/bin/Release/net9.0/MessagePack.dll" "$OUT/"
[ -d "$ROOT/assets/prerendered" ] && cp -R "$ROOT/assets/prerendered" "$OUT/"
(cd "$ROOT/dist" && zip -r "voice_roulette-$VER.zip" "voice_roulette-$VER")
echo "Built $ROOT/dist/voice_roulette-$VER.zip"
