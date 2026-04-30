# Voice Roulette — Slay the Spire 2 Mod

Dota 2-style radial chat wheel with Doubao TTS for STS2 co-op.

## Install (macOS)

1. Download `voice_roulette-X.Y.Z.zip` from Releases.
2. Unzip into `~/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS/mods/`.
3. Right-click the game executable → Get Info → check **Open using Rosetta**.
4. Launch via Finder (NOT Steam button) and enable the mod.

## Doubao API Key

1. Get a key at https://www.volcengine.com/.
2. Open `mods/voice_roulette/config.json` (created on first launch).
3. Set `doubao.apiKey` to your key.

## Usage

- Hold `V` to open the wheel.
- Move mouse to a sector.
- Release `V` to send + speak.
- `Esc` while open cancels.

## Customize

Edit `mods/voice_roulette/config.json`:
- `hotkey`: Change the bound key.
- `pages.custom[i].text`: Fill the 8 custom slots with your own text.
- `defaultVoice`: Swap voice (see Doubao voice list).

## Build from source

```bash
git clone <repo>
cd voice-roulette
./scripts/install-local.sh
```

## Known v0.1 Limitations

- Single shared voice across all 5 characters (per-character voice planned for v0.2).
- Text bubble above the sender's portrait is not yet rendered (audio-only).
- Page toggle (Common / Character / Custom) is not yet wired — only the Common page is active.
- API key abuse is the user's responsibility — never share `config.json`.
