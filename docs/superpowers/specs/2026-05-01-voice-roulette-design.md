# Voice Roulette Mod for Slay the Spire 2 — Design Spec

**Date:** 2026-05-01
**Target Game:** Slay the Spire 2 (Early Access, v0.99+)
**Platform:** macOS (Apple Silicon primary), Windows/Linux secondary
**Engine:** Godot 4.5.1 .NET / C# / .NET 9

---

## 1. Goal

Provide a Dota 2-style radial chat wheel for STS2's 4-player co-op, where selected lines are spoken aloud in-game using ByteDance Doubao (火山引擎) TTS, synthesized on demand and synchronized to teammates.

## 2. Non-Goals

- No native voice chat (mic capture). This is preset-text → TTS only.
- No bundled API key. Users provide their own Doubao key.
- No matchmaking/lobby additions. Uses STS2's existing co-op session.
- No AI-generated dynamic lines based on game state (v1 keeps text static).

## 3. User-Facing Behavior

### 3.1 Interaction (mirrors Dota 2)

1. Player holds bound key (default `V`).
2. 8-sector radial wheel appears centered on cursor.
3. Mouse direction selects sector; visual highlight follows.
4. Releasing the key triggers the selected line.
5. Center button cycles between pages: **Common / Character / Custom** (3 pages × 8 slots = 24 lines).
6. Pressing `Esc` while wheel is open cancels.

### 3.2 Playback

- Selected line is broadcast as `{text, voiceType, senderSlot}` to teammates.
- Each client locally checks cache → plays cached MP3 if hit.
- On cache miss, each client streams from Doubao WSS independently and caches.
- Floating text bubble appears above sender's portrait for 3 seconds with the line text.
- If TTS fails (no network/no key), bubble still shows; audio silently skipped.

### 3.3 Cooldown & Anti-Spam

- 1.5s per-player cooldown between sends.
- 5 sends per rolling 60s window per player.
- Mute-per-player option in mod settings (local-only).

## 4. Architecture

```
┌──────────────────────────────────────────────────────────┐
│ STS2 Process (Godot 4.5 .NET)                            │
│                                                          │
│  ┌──────────────────────────────────────────────────┐   │
│  │ VoiceRoulette Mod (DLL + PCK)                    │   │
│  │                                                  │   │
│  │  InputCapture ──► WheelUI ──► SelectionEvent    │   │
│  │                                      │           │   │
│  │                                      ▼           │   │
│  │  ConfigStore ──► LineRegistry ──► Dispatcher    │   │
│  │                                      │           │   │
│  │                          ┌───────────┴────────┐ │   │
│  │                          ▼                    ▼ │   │
│  │                    NetSync             TTSPipeline│  │
│  │                  (Steam P2P)          ┌────────┐│   │
│  │                          │            │ Cache  ││   │
│  │                          ▼            ├────────┤│   │
│  │                    Remote handler ──► │DoubaoWS││   │
│  │                                       └───┬────┘│   │
│  │                                           ▼     │   │
│  │                                    AudioPlayer  │   │
│  └──────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────┘
```

## 5. Modules

### 5.1 InputCapture
- **Purpose:** Detect hold/release of bound key, sample cursor delta during hold.
- **Interface:** Emits `WheelOpened`, `SectorChanged(idx)`, `WheelReleased(idx | cancelled)`.
- **Dependencies:** Godot `_Input(InputEvent)`. Hooked via Harmony on the input dispatch path discovered in `sts2.dll` (or a top-level Node added via BaseLib).

### 5.2 WheelUI
- **Purpose:** Render the radial menu and selection feedback.
- **Implementation:** Single Godot `CanvasLayer` scene with a `Control` root, 8 child `TextureButton` arranged at 45° increments, central page-toggle button, label below highlighted sector showing full text.
- **Interface:** `Show(page, lines[])`, `Hide()`, `SetSelected(idx)`.
- **Dependencies:** Sprites loaded from mod's PCK.

### 5.3 ConfigStore
- **Purpose:** Persist user settings.
- **File:** `<mod_dir>/config.json`
- **Schema:**
  ```json
  {
    "hotkey": "V",
    "doubao": { "apiKey": "", "endpoint": "wss://ai-gateway.vei.volces.com/v1/realtime?model=doubao-tts" },
    "voiceMap": {
      "ironclad": "zh_male_xiongdi_moon_bigtts",
      "silent":   "zh_female_kailangjiejie_moon_bigtts",
      "regent":   "...",
      "necrobinder": "...",
      "defect":   "..."
    },
    "pages": {
      "common":    [ {"text": "好牌！", "id": "good_card"}, ... 8 entries ],
      "character": "<auto-derived from class>",
      "custom":    [ {"text": "", "id": "slot_1"}, ... 8 entries ]
    },
    "audio":   { "volume": 0.8, "muted": [] },
    "cooldown":{ "perSend": 1.5, "windowMax": 5 }
  }
  ```
- **Interface:** `Load()`, `Save()`, `Get<T>(path)`, `Set<T>(path, val)`.

### 5.4 LineRegistry
- **Purpose:** Resolve `(page, sectorIdx, character)` → `(text, voiceType)`.
- **Interface:** `Resolve(page, idx, char) -> Line`.
- **Source:** ConfigStore + built-in defaults from `lines.default.json`.

### 5.5 Dispatcher
- **Purpose:** Apply cooldown, fan out to local audio + network.
- **Logic:**
  ```
  on SelectionEvent(line):
    if cooldown.exceeded(localPlayerId): showWarning(); return
    cooldown.record()
    NetSync.broadcast(line, localPlayerId)
    handleLocal(line, localPlayerId)  // also plays for sender
  ```

### 5.6 NetSync
- **Purpose:** Send line metadata to other players.
- **Primary path:** Hook into STS2's existing co-op message bus via reflection on `MegaCrit.Sts2.Multiplayer.*`. Investigation needed before lock-in.
- **Fallback path:** Direct Steam P2P via Steamworks.NET on a custom channel ID 0xVR (advertised at session start; ignored by clients without the mod).
- **Wire format (MessagePack):**
  ```
  { v: 1, sender: u8, text: string, voice: string, ts: u64 }
  ```
- **Interface:** `Broadcast(line)`, event `LineReceived(senderSlot, line)`.

### 5.7 TTSPipeline
- **Cache:** SHA1(`text|voice|version`) → file in `<mod_dir>/cache/*.mp3`. LRU evicts >100 MB. Preset lines pre-rendered and shipped in PCK so first-run works offline.
- **Doubao client:** WebSocket, follows event protocol:
  1. Connect with `Authorization: Bearer <key>`.
  2. Send `tts_session.update` with `voice_type`, `format=mp3`, `sample_rate=24000`.
  3. Send `input_text.append` then `input_text.done`.
  4. Receive `response.audio.delta` chunks → write to ring buffer + spill to file.
  5. On `response.audio.done` finalize cache file.
- **Stream → play:** Audio chunks fed into `AudioStreamGenerator` for ≤600 ms first-byte playback; final concatenated MP3 written to cache.
- **Errors:** Auth → notify user once, disable TTS for session. Network → silently skip, keep text bubble.

### 5.8 AudioPlayer
- One `AudioStreamPlayer` per remote slot to allow overlap, on a dedicated bus `VoiceRoulette` so user can mix volume independently.

## 6. Data Flow (happy path)

```
local press V ──► WheelUI.Show
local release at sector 3
   │
   ▼
LineRegistry.Resolve → Line{text:"打精英怪", voice:"zh_male_xiongdi"}
   │
   ▼
Dispatcher (cooldown ok)
 ├─► NetSync.Broadcast (≤200 bytes)
 │     │
 │     ▼
 │   each remote: handleLocal()
 │
 └─► local handleLocal()
        ├─ cache hit? ──► AudioPlayer.Play(file)
        └─ miss ──► TTSPipeline.Stream
                     ├─ first chunk ≤600ms ──► AudioStreamGenerator
                     └─ on done ──► cache file
```

## 7. File Layout

```
VoiceRoulette/
├── VoiceRoulette.csproj
├── manifest.json                # mod metadata
├── Plugin.cs                    # [ModInitializer] entry
├── src/
│   ├── Input/InputCapture.cs
│   ├── UI/WheelUI.cs
│   ├── UI/WheelScene.tscn
│   ├── Config/ConfigStore.cs
│   ├── Lines/LineRegistry.cs
│   ├── Dispatch/Dispatcher.cs
│   ├── Dispatch/Cooldown.cs
│   ├── Net/NetSync.cs
│   ├── Net/SteamP2PChannel.cs
│   ├── TTS/TTSPipeline.cs
│   ├── TTS/DoubaoClient.cs
│   ├── TTS/AudioCache.cs
│   └── Audio/AudioPlayer.cs
├── assets/
│   ├── wheel/                   # sector sprites
│   └── prerendered/             # offline-fallback MP3s
├── lines.default.json
└── tests/                       # xUnit, run on host (not in-game)
```

## 8. Testing Strategy

- **Unit (xUnit, host):** `Cooldown`, `LineRegistry`, `AudioCache` LRU eviction, `DoubaoClient` event parsing against fixtures.
- **Integration (host):** `DoubaoClient` smoke test against real WSS gated by `DOUBAO_API_KEY` env var; skipped in CI without secret.
- **Manual in-game:**
  1. Solo run → wheel opens / closes / cancels / selects each sector.
  2. Cache miss → first-byte ≤1s; cache hit → instant.
  3. 2-player co-op (Steam friend) → line appears for both.
  4. Spam test → cooldown + window-cap enforced.
  5. Network kill mid-stream → graceful fallback, no crash.

## 9. Build & Distribution

- `dotnet build -c Release` → DLL.
- Godot CLI export → PCK with assets.
- Single ZIP with `manifest.json + dll + pck + lines.default.json + assets/prerendered/`.
- Drop into `~/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS/mods/`.

## 10. Risks & Open Questions

| # | Risk | Mitigation |
|---|------|-----------|
| R1 | STS2 multiplayer API not yet documented | Spike phase 0: probe `MegaCrit.Sts2.Multiplayer` via ILSpy; fall back to direct Steam P2P |
| R2 | EA hotfixes break Harmony patches | Patch the smallest possible surface; pin BaseLib version; isolate game-coupled code in NetSync |
| R3 | Doubao key abuse if shared | Never bundle a key; clear UI warning; per-user storage only |
| R4 | TTS latency >1s on slow networks | Pre-render preset lines; show bubble immediately on selection regardless of audio |
| R5 | Apple Silicon / Rosetta launch quirks | Document the "Open with Rosetta" + Finder-launch workaround in README |

### Open Questions for Phase 0 Spike
- Does BaseLib-StS2 already expose a co-op message hook?
- Can the wheel UI overlay safely on top of STS2's existing canvas layers without z-order fighting?
- Steamworks.NET version compatibility with the bundled Steam SDK in STS2.

## 11. Out-of-Scope (future)

- AI-driven contextual lines ("low HP! help!" auto-triggered)
- Per-card / per-relic reaction lines
- Local mic→STT→TTS for actual voice input
- Voice line marketplace / community packs
