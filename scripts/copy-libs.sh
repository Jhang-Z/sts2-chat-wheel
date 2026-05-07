#!/usr/bin/env bash
# Copies the three game DLLs the mod project links against from a local
# Slay the Spire 2 install into ./lib/. Required before building from source.
#
# Usage:  ./scripts/copy-libs.sh
#
# The DLLs are NOT redistributable (they're MegaCrit's). The .gitignore keeps
# them out of git; each developer's machine grabs its own copy from their
# own legally-purchased install.

set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"

# Try common Steam install paths in order; the first one that has all three
# DLLs wins. macOS / Linux / Windows-Steam-on-Mac (rare) all covered.
CANDIDATES=(
  # macOS — Steam default
  "$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/Resources/data_sts2_macos_arm64"
  "$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/Resources/data_sts2_macos_x86_64"
  # Linux — Steam default
  "$HOME/.steam/steam/steamapps/common/Slay the Spire 2/data_sts2_linux_x86_64"
  "$HOME/.local/share/Steam/steamapps/common/Slay the Spire 2/data_sts2_linux_x86_64"
)

NEEDED=("GodotSharp.dll" "sts2.dll" "0Harmony.dll")

found_dir=""
for d in "${CANDIDATES[@]}"; do
    if [ -d "$d" ]; then
        ok=1
        for f in "${NEEDED[@]}"; do
            if [ ! -f "$d/$f" ]; then ok=0; break; fi
        done
        if [ "$ok" = "1" ]; then found_dir="$d"; break; fi
    fi
done

if [ -z "$found_dir" ]; then
    echo "❌ Could not auto-find Slay the Spire 2 install."
    echo "   Either install via Steam, or set GAME_DIR explicitly:"
    echo "     GAME_DIR=/path/to/data_sts2_macos_arm64 ./scripts/copy-libs.sh"
    if [ -n "${GAME_DIR:-}" ]; then
        found_dir="$GAME_DIR"
        echo "   Trying GAME_DIR=$GAME_DIR ..."
    else
        exit 1
    fi
fi

echo "📦 Found game DLLs at: $found_dir"
mkdir -p "$ROOT/lib"
for f in "${NEEDED[@]}"; do
    cp -v "$found_dir/$f" "$ROOT/lib/$f"
done
echo "✅ Copied ${#NEEDED[@]} DLLs to $ROOT/lib/. You can now build the mod."
