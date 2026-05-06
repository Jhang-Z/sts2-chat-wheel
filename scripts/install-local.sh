#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
GAME_MODS="$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS/mods"
MOD_ID="sts2_chat_wheel"
DEST="$GAME_MODS/$MOD_ID"

export PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH"
export DOTNET_ROOT="/opt/homebrew/opt/dotnet@9/libexec"

dotnet build "$ROOT/src/VoiceRoulette/VoiceRoulette.csproj" -c Release
mkdir -p "$DEST"
# Game resolves DLL as <mod_id>.dll (lowercase), not the assembly name.
cp "$ROOT/src/VoiceRoulette/bin/Release/net9.0/VoiceRoulette.dll" "$DEST/$MOD_ID.dll"
cp "$ROOT/src/VoiceRoulette/manifest.json" "$DEST/"
# lines.default.json goes in a subdirectory so the game doesn't mistake it for a manifest.
mkdir -p "$DEST/data"
# .jsonc extension avoids the game's mod-manifest scanner (only picks up .json)
cp "$ROOT/lines.default.json" "$DEST/data/lines.default.jsonc"

# Optional dependencies that ship inside our build output
for dep in MessagePack.dll MessagePack.Annotations.dll; do
    if [ -f "$ROOT/src/VoiceRoulette/bin/Release/net9.0/$dep" ]; then
        cp "$ROOT/src/VoiceRoulette/bin/Release/net9.0/$dep" "$DEST/"
    fi
done

# Prerendered placeholder audio (macOS Tingting TTS, no API key required)
if [ -d "$ROOT/assets/prerendered" ]; then
    mkdir -p "$DEST/prerendered"
    cp "$ROOT/assets/prerendered/"*.mp3 "$DEST/prerendered/"
fi

# UI textures (parchment cards, center hub, outer ring, bubble bg)
if [ -d "$ROOT/assets/textures" ]; then
    mkdir -p "$DEST/textures"
    cp "$ROOT/assets/textures/"*.png "$DEST/textures/"
fi

echo "Installed to $DEST"
ls -lh "$DEST"
