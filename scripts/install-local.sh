#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
GAME_MODS="$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS/mods"
MOD_ID="sts2_chat_wheel"
DEST="$GAME_MODS/$MOD_ID"

# Optional: macOS Homebrew dotnet@9 path. Skip if dotnet is already on PATH.
if [ -x "/opt/homebrew/opt/dotnet@9/bin/dotnet" ]; then
    export PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH"
    export DOTNET_ROOT="/opt/homebrew/opt/dotnet@9/libexec"
fi

dotnet build "$ROOT/src/VoiceRoulette/VoiceRoulette.csproj" -c Release
mkdir -p "$DEST"
# Game resolves DLL as <mod_id>.dll (lowercase), not the assembly name.
cp "$ROOT/src/VoiceRoulette/bin/Release/net9.0/VoiceRoulette.dll" "$DEST/$MOD_ID.dll"
cp "$ROOT/src/VoiceRoulette/manifest.json" "$DEST/"
# lines.default.jsonc is a reference doc; real config lives at data/config.jsonc.
# .jsonc extension avoids the game's mod-manifest scanner (only picks up .json).
mkdir -p "$DEST/data"
cp "$ROOT/lines.default.json" "$DEST/data/lines.default.jsonc"

# v0.2: prerendered audio dropped (voice changed to shuangkuaisisi; emotion is now
# part of the cache key so old prerenders would never hit). Doubao synthesizes on
# first use, then caches per (text, voice, emotion) tuple under cache/.
rm -rf "$DEST/prerendered" || true

# v0.2: WheelUI is fully runtime-drawn (no textures); drop legacy texture dir.
rm -rf "$DEST/textures" || true

echo "Installed to $DEST"
ls -lh "$DEST"
