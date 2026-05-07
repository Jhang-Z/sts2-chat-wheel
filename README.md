# StS2 Chat Wheel

Dota 2-style radial chat wheel with Doubao TTS for Slay the Spire 2 co-op. Hold a hotkey, pick one of 8 lines, and your character speaks it aloud — with multiple emotion presets per line — in front of your teammates.

> **English / [中文](#中文)**

---

## ✨ Features

- 🎯 **Hold `Y`** to open an 8-sector radial wheel and broadcast a voice line to teammates
- 🗣️ **Doubao TTS 2.0** with 5 emotion presets (正常 / 开心 / 愤怒 / 无奈 / 委屈) and 5 voice characters (爽快思思 / 猪八戒 / 熊二 / 猴哥 / 海绵宝宝)
- 💬 **In-game speech bubbles** rendered with the game's own `NSpeechBubbleVfx` so they match StS2's visual language exactly
- 📚 **Editable phrase library** — 36 presets shipped, plus custom add/delete in-game
- 🃏 **Auto status pings**:
  - `Cmd+Click` (Mac) / `Ctrl+Click` (Windows) on a potion or buff → "我有【XXX】"
  - At turn start, if your hand has a Vulnerable / Weak card → auto-broadcast "我有【易伤】牌"
- 🎵 **Smart caching** — every (text, voice, emotion) tuple cached locally; same line never hits Doubao twice
- 🔧 **Live settings** — rebind hotkeys, swap voices, edit phrases, all without leaving the game

---

## 📦 For Players (Just Want to Use It)

### Step 1 — Find your mods folder

| OS | Path |
|---|---|
| macOS | `~/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS/mods/` |
| Windows | `C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods\` |

> **Tip**: Steam → right-click the game → Manage → Browse local files, then navigate to `mods/` (create it if missing).

### Step 2 — Install

1. Download `sts2_chat_wheel-0.2.0.zip` from the [Releases](../../releases) page
2. Unzip it — you'll get a folder named `sts2_chat_wheel`
3. Move the entire `sts2_chat_wheel` folder into your `mods/` folder

The result should look like:

```
mods/
└── sts2_chat_wheel/
    ├── manifest.json
    ├── sts2_chat_wheel.dll
    └── data/
        └── lines.default.jsonc
```

### Step 3 — macOS only: enable Rosetta

Right-click `SlayTheSpire2` in Finder → Get Info → check **Open using Rosetta**.

### Step 4 — Launch and enable

Launch the game, open the Mod menu, and enable **StS2 Chat Wheel**.

### Step 5 — Configure Doubao TTS (required for voice)

Without an API key the chat wheel still works as **text-only** bubbles. To enable voice:

1. Sign up at [volcengine.com](https://www.volcengine.com/) → activate **豆包语音合成模型 2.0** (Doubao TTS 2.0)
2. Authorize the voices you want to use under **音色库** (e.g. 爽快思思)
3. Copy your API Key from **API Key 管理** in the new console
4. Launch the game once so the mod creates `mods/sts2_chat_wheel/data/config.jsonc`
5. Open that file and paste the Key into `doubao.apiKey`
6. Restart the game

> The first time you say a line, the mod calls Doubao to synthesize it (~500ms). All subsequent uses of that exact (text, voice, emotion) tuple play instantly from local cache — Doubao isn't called again.

---

## 🎮 Usage

| Action | Effect |
|---|---|
| **Hold `Y`** | Open the chat wheel |
| Move mouse to a sector | Highlight that line; full text shows below the hub |
| **Release `Y`** | Send + speak the line to all teammates |
| `Esc` while holding `Y` | Cancel without sending |
| **Press `;`** | Open settings (rebindable to `N`, `;`, etc. in-game) |
| `Cmd/Ctrl + Click` a potion/buff | Announce "我有【火焰药水】" or "我处于【力量2】的状态" |

### Settings Page (press `;`)

- **轮盘配置 tab**: Click any of the 8 mini-wheel positions on the left to select it. Then either:
  - Edit text/voice/emotion in the editor below, OR
  - **Click any phrase from the 语音库 on the right → that phrase swaps with the selected slot**
- **设置 tab**: Rebind hotkeys, switch voice character

Hotkeys stop firing while a text input has focus, so you can safely type `Y` or `;` into a textbox without opening anything.

---

## ⚙️ Customization

`mods/sts2_chat_wheel/data/config.jsonc` (auto-generated on first launch):

| Key | Description | Example |
|---|---|---|
| `hotkey` | Key to open the wheel | `"Y"` |
| `settingsHotkey` | Key to open settings | `"Semicolon"` |
| `defaultVoice` | Doubao voice ID | `"zh_female_shuangkuaisisi_uranus_bigtts"` |
| `doubao.apiKey` | Your Volcengine API Key | `"abc123..."` |
| `doubao.resourceId` | Doubao service tier | `"seed-tts-2.0"` |
| `cooldown.perSend` | Seconds between sends | `0.5` |
| `lines[i].text` | Wheel sector text | `"好牌！"` |
| `lines[i].emotion` | Emotion (`null` = no voice) | `"happy"` / `"angry"` / `"sad"` / `"sorry"` / `"novel_dialog"` |
| `library[i]` | Phrase library entries | (managed in the settings UI) |

---

## 🛠️ For Developers (Build from Source)

### Prerequisites

- **.NET 9 SDK** ([download](https://dotnet.microsoft.com/download/dotnet/9.0))
- A working installation of **Slay the Spire 2** (the build needs to reference 3 of the game's DLLs, which can't be redistributed)

### Build steps

```bash
git clone <this-repo>
cd sts2-chat-wheel

# Step 1: Copy the game's runtime DLLs into ./lib/
# (auto-detects standard Steam paths on macOS / Linux / Windows)
./scripts/copy-libs.sh           # macOS / Linux
.\scripts\copy-libs.ps1          # Windows PowerShell

# Step 2: Build + install to your local mods folder
./scripts/install-local.sh       # macOS / Linux
.\scripts\install-local.ps1      # Windows PowerShell

# Or, package a distributable zip → dist/
./scripts/package.sh             # macOS / Linux
.\scripts\package.ps1            # Windows PowerShell
```

### If the auto-detect fails

```bash
# macOS / Linux
GAME_DIR="/path/to/data_sts2_macos_arm64" ./scripts/copy-libs.sh
STS2_MODS_DIR="/path/to/Slay the Spire 2/mods" ./scripts/install-local.sh
```

```powershell
# Windows PowerShell
$env:GAME_DIR="D:\path\to\data_sts2_windows_x86_64"
$env:STS2_MODS_DIR="D:\path\to\Slay the Spire 2\mods"
.\scripts\install-local.ps1
```

### Run tests

```bash
dotnet test tests/VoiceRoulette.Tests/VoiceRoulette.Tests.csproj
```

> Tests that touch `Godot.GD.*` are marked `Skip` because GodotSharp's native bindings only exist inside the running game. Pure-logic tests (config, cooldown, audio cache key, wire-message serialization, line registry, parser) all run normally — expect ~25 passing, ~7 skipped.

### Project structure

```
sts2-chat-wheel/
├── src/VoiceRoulette/         # mod source (net9.0, AssemblyName=VoiceRoulette)
│   ├── Plugin.cs              # entry-point + dependency wiring
│   ├── UI/                    # WheelUI / SettingsScreen / BubbleOverlay / StsTheme / StsFonts
│   ├── TTS/                   # DoubaoClient (V3 binary protocol) + AudioCache + TTSPipeline
│   ├── Audio/                 # MP3 playback via Godot AudioStreamPlayer
│   ├── Combat/HandAnalyzer.cs # turn-start hand scan for 易伤 / 虚弱 cards
│   ├── Input/                 # InputCapture (Y / ; hotkeys) + StatusPinger (Cmd+Click)
│   ├── Lines/                 # Line model + LineLibrary preset phrases
│   ├── Net/                   # WireMessage + Sts2BusNetSync (multiplayer)
│   ├── Dispatch/              # Cooldown + Dispatcher
│   └── Config/                # ConfigSchema + ConfigStore (config.jsonc)
├── tests/VoiceRoulette.Tests/ # xunit + FluentAssertions
├── lib/                       # game DLLs go here (gitignored — run copy-libs)
├── scripts/                   # copy-libs / install-local / package, both .sh and .ps1
└── design.md                  # visual-design tokens reference (StsColors palette, fonts)
```

---

## 📝 License

Source code: MIT-style (re-use freely, no warranty).
Game DLLs in `lib/` are NOT redistributed — each developer pulls their own copy from their legally-purchased Slay the Spire 2 install.

## 🐛 Known limitations

- 海绵宝宝 (`liangsangmengzai`) voice ID is included in the picker but verify it's authorized in your Volcengine 音色库 first, or you'll get a `45000000` permission error
- TTS uses your Volcengine quota (free tier ships ~20K characters — plenty for testing); production runs cost a few yuan per few thousand sends
- Multiplayer broadcast rides on the in-game RPC bus; if the bus shape changes in a future game patch, only local audio + bubble continue to work until the mod is updated

---

## 中文

道塔风格的 8 扇区聊天轮盘，配豆包 TTS（5 种情感、5 个角色）和原生游戏内气泡。按住 `Y` 选词条松开发送，队友能听到声音也能看到气泡。

完整文档见上方英文版——命令和路径在两边都通用。

简短安装：
1. 从 Releases 下载 `sts2_chat_wheel-0.2.0.zip`，解压后整个 `sts2_chat_wheel` 文件夹放进游戏的 `mods/` 目录
2. macOS 用户右键游戏 → 显示简介 → 勾选「使用 Rosetta 打开」
3. 启动游戏 → Mod 菜单启用
4. 想要 TTS 的话：去 [火山引擎](https://www.volcengine.com/) 拿 API Key 填到 `mods/sts2_chat_wheel/data/config.jsonc` 的 `doubao.apiKey`
5. 按 `Y` 试试
