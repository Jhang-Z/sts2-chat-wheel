#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
GAME_MODS="$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS/mods"
DEST="$GAME_MODS/voice_roulette"

export PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH"
export DOTNET_ROOT="/opt/homebrew/opt/dotnet@9/libexec"

dotnet build "$ROOT/src/VoiceRoulette/VoiceRoulette.csproj" -c Release
mkdir -p "$DEST"
cp "$ROOT/src/VoiceRoulette/bin/Release/net9.0/VoiceRoulette.dll" "$DEST/"
cp "$ROOT/src/VoiceRoulette/manifest.json" "$DEST/"
cp "$ROOT/lines.default.json" "$DEST/"

# Optional dependencies that ship inside our build output
for dep in MessagePack.dll; do
    if [ -f "$ROOT/src/VoiceRoulette/bin/Release/net9.0/$dep" ]; then
        cp "$ROOT/src/VoiceRoulette/bin/Release/net9.0/$dep" "$DEST/"
    fi
done

echo "Installed to $DEST"
ls -lh "$DEST"
