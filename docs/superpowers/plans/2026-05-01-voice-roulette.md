# Voice Roulette Mod Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a Dota 2-style radial chat wheel mod for Slay the Spire 2 that uses Doubao TTS for voice synthesis and broadcasts spoken lines to co-op teammates.

**Architecture:** C# class library targeting .NET 9, loaded by STS2 (Godot 4.5.1 .NET) via BaseLib-StS2's `[ModInitializer]`. Pure-logic modules (config, cooldown, cache, TTS event parser) are unit-tested with xUnit on the host. Godot scene + audio + input + multiplayer paths are verified manually in-game.

**Tech Stack:** C# / .NET 9, Godot 4.5.1 .NET, Harmony 2, BaseLib-StS2 v3.x, Steamworks.NET, MessagePack-CSharp, System.Net.WebSockets, xUnit, FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-05-01-voice-roulette-design.md`

---

## Conventions

- All paths are relative to repo root: `/Users/jhang-z/Developer/Plugins/Voice Roulette/`
- TDD cycle for testable modules: red → green → commit. UI/Audio/Input/Net modules use a "scaffold → manual verify → commit" cycle because Godot runtime can't be unit-tested from the host.
- Commit after every passing task. Use Conventional Commits.
- Test command: `dotnet test tests/VoiceRoulette.Tests/VoiceRoulette.Tests.csproj`
- Build command: `dotnet build src/VoiceRoulette/VoiceRoulette.csproj -c Release`
- After every task: `dotnet build` must succeed, `dotnet test` must be green.

---

## Task 0: Phase 0 Spike — Multiplayer API Discovery

**Goal:** Decide whether `NetSync` hooks into STS2's co-op message bus or talks Steam P2P directly. Spec risk R1.

**Files:**
- Create: `docs/superpowers/notes/phase-0-multiplayer-spike.md`

- [ ] **Step 1: Locate the STS2 game DLL on this machine**

Run:
```bash
ls "/Users/jhang-z/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS/" | head -20
```
Expected: see `SlayTheSpire2`, `mods/`, and `.pck`/`.dll` files. Identify the path of `sts2.dll` (or whatever the main managed assembly is named).

- [ ] **Step 2: Install ILSpyCmd**

Run:
```bash
dotnet tool install -g ilspycmd
```
Expected: tool installed.

- [ ] **Step 3: Dump the multiplayer namespace**

Run (substitute the actual DLL path from Step 1):
```bash
ilspycmd "<path-to-sts2.dll>" -p -o /tmp/sts2-decomp
grep -rln "Multiplayer\|Coop\|Lobby\|Steam" /tmp/sts2-decomp | head -30
```
Expected: a list of files referencing co-op infrastructure.

- [ ] **Step 4: Identify the message dispatch pattern**

Open the top 5 files from Step 3 and answer in the notes doc:
1. Is there a typed event bus or RPC method any mod can hook?
2. What is the signature of "send a message to all players"?
3. Does BaseLib-StS2 already expose a wrapper? Check `https://alchyr.github.io/BaseLib-Wiki/`.

- [ ] **Step 5: Write the decision**

Create `docs/superpowers/notes/phase-0-multiplayer-spike.md` with:
- Discovered API surface (paste actual class/method names).
- Decision: PRIMARY (hook STS2 bus) or FALLBACK (Steamworks.NET P2P).
- For whichever path is chosen, a 5-line code skeleton showing how `NetSync.Broadcast(byte[])` will be implemented.

- [ ] **Step 6: Commit**

```bash
cd "/Users/jhang-z/Developer/Plugins/Voice Roulette"
git add docs/superpowers/notes/phase-0-multiplayer-spike.md
git commit -m "spike: phase 0 multiplayer API discovery"
```

---

## Task 1: Project Skeleton

**Goal:** Create the solution, mod project, test project, and verify a no-op build.

**Files:**
- Create: `VoiceRoulette.sln`
- Create: `src/VoiceRoulette/VoiceRoulette.csproj`
- Create: `src/VoiceRoulette/Plugin.cs`
- Create: `src/VoiceRoulette/manifest.json`
- Create: `tests/VoiceRoulette.Tests/VoiceRoulette.Tests.csproj`
- Create: `tests/VoiceRoulette.Tests/SmokeTest.cs`
- Create: `.gitignore`
- Create: `lib/.gitkeep`

- [ ] **Step 1: Create .gitignore**

```gitignore
bin/
obj/
*.user
.vs/
.idea/
cache/
lib/*.dll
!lib/.gitkeep
```

- [ ] **Step 2: Create solution + projects**

```bash
cd "/Users/jhang-z/Developer/Plugins/Voice Roulette"
dotnet new sln -n VoiceRoulette
dotnet new classlib -n VoiceRoulette -o src/VoiceRoulette -f net9.0
dotnet new xunit   -n VoiceRoulette.Tests -o tests/VoiceRoulette.Tests -f net9.0
dotnet sln add src/VoiceRoulette/VoiceRoulette.csproj
dotnet sln add tests/VoiceRoulette.Tests/VoiceRoulette.Tests.csproj
dotnet add tests/VoiceRoulette.Tests/VoiceRoulette.Tests.csproj reference src/VoiceRoulette/VoiceRoulette.csproj
dotnet add tests/VoiceRoulette.Tests/VoiceRoulette.Tests.csproj package FluentAssertions
mkdir -p lib && touch lib/.gitkeep
rm src/VoiceRoulette/Class1.cs tests/VoiceRoulette.Tests/UnitTest1.cs
```

- [ ] **Step 3: Configure VoiceRoulette.csproj**

Replace `src/VoiceRoulette/VoiceRoulette.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <AssemblyName>VoiceRoulette</AssemblyName>
    <RootNamespace>VoiceRoulette</RootNamespace>
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="MessagePack" Version="2.5.187" />
    <!-- Game-supplied references; populate lib/ from the game install. -->
    <Reference Include="GodotSharp" HintPath="../../lib/GodotSharp.dll" Private="false" />
    <Reference Include="0Harmony"   HintPath="../../lib/0Harmony.dll"   Private="false" />
    <Reference Include="sts2"       HintPath="../../lib/sts2.dll"       Private="false" />
    <PackageReference Include="Alchyr.Sts2.BaseLib" Version="*" />
  </ItemGroup>
  <ItemGroup>
    <None Include="manifest.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Create manifest.json**

`src/VoiceRoulette/manifest.json`:
```json
{
  "id": "voice_roulette",
  "name": "Voice Roulette",
  "author": "jhang-z",
  "description": "Dota 2-style radial chat wheel with Doubao TTS for STS2 co-op.",
  "version": "0.1.0",
  "has_pck": true,
  "has_dll": true,
  "dependencies": ["alchyr.baselib"],
  "affects_gameplay": false
}
```

- [ ] **Step 5: Create Plugin.cs entry point**

`src/VoiceRoulette/Plugin.cs`:
```csharp
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace VoiceRoulette;

public static class Plugin
{
    public const string Id = "voice_roulette";
    public const string Version = "0.1.0";

    [ModInitializer("Initialize")]
    public static void Initialize()
    {
        Log.Info($"[{Id}] {Version} loaded");
    }

    public static void Unload()
    {
        Log.Info($"[{Id}] unloaded");
    }
}
```

- [ ] **Step 6: Create smoke test**

`tests/VoiceRoulette.Tests/SmokeTest.cs`:
```csharp
using FluentAssertions;
using Xunit;

namespace VoiceRoulette.Tests;

public class SmokeTest
{
    [Fact]
    public void PluginVersion_IsNotEmpty()
    {
        Plugin.Version.Should().NotBeNullOrWhiteSpace();
    }
}
```

- [ ] **Step 7: Stage game DLLs locally**

Copy required DLLs from the game install into `lib/` so the build succeeds. The exact path of `sts2.dll` was identified in Task 0 Step 1.

```bash
GAME="/Users/jhang-z/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS"
cp "$GAME/sts2.dll" lib/        # path may differ - adjust per Task 0 findings
cp "$GAME/0Harmony.dll" lib/
cp "$GAME/GodotSharp.dll" lib/
ls lib/
```
Expected: three DLLs present (plus `.gitkeep`).

- [ ] **Step 8: Build + test**

```bash
dotnet build && dotnet test
```
Expected: build succeeds, 1 test passes.

> If `Plugin.cs` fails to compile because `MegaCrit.Sts2.Core.Modding` is unknown, the lib/ DLLs are wrong — re-check Task 0 findings.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: project skeleton with .NET 9 + xUnit + manifest"
```

---

## Task 2: ConfigStore (TDD)

**Goal:** JSON-backed configuration with typed get/set, robust to missing fields.

**Files:**
- Create: `src/VoiceRoulette/Config/ConfigStore.cs`
- Create: `src/VoiceRoulette/Config/ConfigSchema.cs`
- Create: `tests/VoiceRoulette.Tests/Config/ConfigStoreTests.cs`

- [ ] **Step 1: Write failing tests**

`tests/VoiceRoulette.Tests/Config/ConfigStoreTests.cs`:
```csharp
using System.IO;
using FluentAssertions;
using VoiceRoulette.Config;
using Xunit;

namespace VoiceRoulette.Tests.Config;

public class ConfigStoreTests
{
    private string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"vr-{System.Guid.NewGuid():N}.json");

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var store = ConfigStore.Load(TempPath());
        store.Schema.Hotkey.Should().Be("V");
        store.Schema.Cooldown.PerSend.Should().Be(1.5);
        store.Schema.Cooldown.WindowMax.Should().Be(5);
        store.Schema.DefaultVoice.Should().Be("zh_female_kailangjiejie_moon_bigtts");
    }

    [Fact]
    public void SaveThenLoad_Roundtrips()
    {
        var path = TempPath();
        var store = ConfigStore.Load(path);
        store.Schema.Hotkey = "B";
        store.Save();

        var reloaded = ConfigStore.Load(path);
        reloaded.Schema.Hotkey.Should().Be("B");
    }

    [Fact]
    public void Load_PartialFile_FillsMissingWithDefaults()
    {
        var path = TempPath();
        File.WriteAllText(path, "{ \"hotkey\": \"X\" }");
        var store = ConfigStore.Load(path);
        store.Schema.Hotkey.Should().Be("X");
        store.Schema.Cooldown.PerSend.Should().Be(1.5);  // default preserved
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter ConfigStoreTests
```
Expected: build fails (`ConfigStore` not defined).

- [ ] **Step 3: Implement ConfigSchema**

`src/VoiceRoulette/Config/ConfigSchema.cs`:
```csharp
using System.Collections.Generic;

namespace VoiceRoulette.Config;

public sealed class ConfigSchema
{
    public string Hotkey { get; set; } = "V";
    public DoubaoConfig Doubao { get; set; } = new();
    public string DefaultVoice { get; set; } = "zh_female_kailangjiejie_moon_bigtts";
    public Dictionary<string, string> VoiceMap { get; set; } = new()
    {
        ["ironclad"] = "$default",
        ["silent"] = "$default",
        ["regent"] = "$default",
        ["necrobinder"] = "$default",
        ["defect"] = "$default",
    };
    public PagesConfig Pages { get; set; } = PagesConfig.Defaults();
    public AudioConfig Audio { get; set; } = new();
    public CooldownConfig Cooldown { get; set; } = new();
}

public sealed class DoubaoConfig
{
    public string ApiKey { get; set; } = "";
    public string Endpoint { get; set; } =
        "wss://ai-gateway.vei.volces.com/v1/realtime?model=doubao-tts";
}

public sealed class CooldownConfig
{
    public double PerSend { get; set; } = 1.5;
    public int WindowMax { get; set; } = 5;
}

public sealed class AudioConfig
{
    public double Volume { get; set; } = 0.8;
    public List<byte> Muted { get; set; } = new();
}

public sealed class PagesConfig
{
    public List<LineEntry> Common { get; set; } = new();
    public List<LineEntry> Custom { get; set; } = new();

    public static PagesConfig Defaults()
    {
        string[] common =
        {
            "好牌！", "打精英怪！", "去休息点", "继续推进",
            "等一下", "撤退！", "我来挡", "干得漂亮！"
        };
        var p = new PagesConfig();
        for (int i = 0; i < 8; i++)
        {
            p.Common.Add(new LineEntry { Id = $"common_{i}", Text = common[i] });
            p.Custom.Add(new LineEntry { Id = $"custom_{i}", Text = "" });
        }
        return p;
    }
}

public sealed class LineEntry
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
}
```

- [ ] **Step 4: Implement ConfigStore**

`src/VoiceRoulette/Config/ConfigStore.cs`:
```csharp
using System.IO;
using System.Text.Json;

namespace VoiceRoulette.Config;

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public ConfigSchema Schema { get; private set; }
    public string Path { get; }

    private ConfigStore(string path, ConfigSchema schema)
    {
        Path = path;
        Schema = schema;
    }

    public static ConfigStore Load(string path)
    {
        if (!File.Exists(path))
            return new ConfigStore(path, new ConfigSchema());

        var json = File.ReadAllText(path);
        ConfigSchema schema;
        try
        {
            schema = JsonSerializer.Deserialize<ConfigSchema>(json, Options)
                     ?? new ConfigSchema();
        }
        catch (JsonException)
        {
            schema = new ConfigSchema();
        }
        return new ConfigStore(path, schema);
    }

    public void Save()
    {
        var dir = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(Path, JsonSerializer.Serialize(Schema, Options));
    }
}
```

- [ ] **Step 5: Run tests to verify pass**

```bash
dotnet test --filter ConfigStoreTests
```
Expected: 3 tests pass.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(config): JSON config store with default fallback"
```

---

## Task 3: LineRegistry (TDD)

**Goal:** Resolve `(page, sectorIdx, character)` → `(text, voiceType)` from config.

**Files:**
- Create: `src/VoiceRoulette/Lines/Line.cs`
- Create: `src/VoiceRoulette/Lines/LineRegistry.cs`
- Create: `tests/VoiceRoulette.Tests/Lines/LineRegistryTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/VoiceRoulette.Tests/Lines/LineRegistryTests.cs
using FluentAssertions;
using VoiceRoulette.Config;
using VoiceRoulette.Lines;
using Xunit;

namespace VoiceRoulette.Tests.Lines;

public class LineRegistryTests
{
    private static LineRegistry Build()
    {
        var schema = new ConfigSchema();
        return new LineRegistry(schema);
    }

    [Fact]
    public void Resolve_CommonPage_ReturnsExpectedText()
    {
        var line = Build().Resolve(WheelPage.Common, sectorIdx: 0, character: "ironclad");
        line.Text.Should().Be("好牌！");
        line.Voice.Should().Be("zh_female_kailangjiejie_moon_bigtts");
    }

    [Fact]
    public void Resolve_VoiceMapDollarDefault_ResolvesToDefaultVoice()
    {
        var schema = new ConfigSchema { DefaultVoice = "voice_x" };
        var reg = new LineRegistry(schema);
        reg.Resolve(WheelPage.Common, 3, "silent").Voice.Should().Be("voice_x");
    }

    [Fact]
    public void Resolve_OutOfRangeSector_Throws()
    {
        var act = () => Build().Resolve(WheelPage.Common, 99, "ironclad");
        act.Should().Throw<System.ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Resolve_CustomEmptySlot_ReturnsEmptyTextAllowingDispatcherToSkip()
    {
        Build().Resolve(WheelPage.Custom, 0, "ironclad").Text.Should().Be("");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter LineRegistryTests
```
Expected: build fails — types not defined.

- [ ] **Step 3: Implement Line + LineRegistry**

`src/VoiceRoulette/Lines/Line.cs`:
```csharp
namespace VoiceRoulette.Lines;

public enum WheelPage { Common, Character, Custom }

public readonly record struct Line(string Id, string Text, string Voice);
```

`src/VoiceRoulette/Lines/LineRegistry.cs`:
```csharp
using System;
using System.Collections.Generic;
using VoiceRoulette.Config;

namespace VoiceRoulette.Lines;

public sealed class LineRegistry
{
    private readonly ConfigSchema _schema;

    public LineRegistry(ConfigSchema schema) => _schema = schema;

    public Line Resolve(WheelPage page, int sectorIdx, string character)
    {
        var entries = page switch
        {
            WheelPage.Common => _schema.Pages.Common,
            WheelPage.Custom => _schema.Pages.Custom,
            WheelPage.Character => GetCharacterLines(character),
            _ => throw new ArgumentOutOfRangeException(nameof(page))
        };

        if (sectorIdx < 0 || sectorIdx >= entries.Count)
            throw new ArgumentOutOfRangeException(nameof(sectorIdx));

        var entry = entries[sectorIdx];
        var voice = ResolveVoice(character);
        return new Line(entry.Id, entry.Text, voice);
    }

    private string ResolveVoice(string character)
    {
        if (_schema.VoiceMap.TryGetValue(character, out var v) && v != "$default")
            return v;
        return _schema.DefaultVoice;
    }

    private static List<LineEntry> GetCharacterLines(string character)
    {
        // v1: character page mirrors common until per-character lines are authored
        return PagesConfig.Defaults().Common;
    }
}
```

- [ ] **Step 4: Run tests to verify pass**

```bash
dotnet test --filter LineRegistryTests
```
Expected: 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(lines): line registry with default voice fallback"
```

---

## Task 4: Cooldown (TDD)

**Goal:** Per-player rate limiter (per-send + sliding window).

**Files:**
- Create: `src/VoiceRoulette/Dispatch/Cooldown.cs`
- Create: `tests/VoiceRoulette.Tests/Dispatch/CooldownTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/VoiceRoulette.Tests/Dispatch/CooldownTests.cs
using FluentAssertions;
using VoiceRoulette.Dispatch;
using Xunit;

namespace VoiceRoulette.Tests.Dispatch;

public class CooldownTests
{
    [Fact]
    public void FirstSend_Allowed()
    {
        var c = new Cooldown(perSendSeconds: 1.5, windowMax: 5, windowSeconds: 60);
        c.TryRecord(playerId: 1, nowSeconds: 0).Should().BeTrue();
    }

    [Fact]
    public void WithinPerSend_Blocked()
    {
        var c = new Cooldown(1.5, 5, 60);
        c.TryRecord(1, 0).Should().BeTrue();
        c.TryRecord(1, 1.0).Should().BeFalse();
    }

    [Fact]
    public void AfterPerSend_Allowed()
    {
        var c = new Cooldown(1.5, 5, 60);
        c.TryRecord(1, 0).Should().BeTrue();
        c.TryRecord(1, 1.6).Should().BeTrue();
    }

    [Fact]
    public void WindowCap_Enforced()
    {
        var c = new Cooldown(0.0, windowMax: 3, windowSeconds: 60);
        c.TryRecord(1, 0).Should().BeTrue();
        c.TryRecord(1, 1).Should().BeTrue();
        c.TryRecord(1, 2).Should().BeTrue();
        c.TryRecord(1, 3).Should().BeFalse();
    }

    [Fact]
    public void WindowCap_SlidesOff()
    {
        var c = new Cooldown(0.0, windowMax: 2, windowSeconds: 10);
        c.TryRecord(1, 0).Should().BeTrue();
        c.TryRecord(1, 1).Should().BeTrue();
        c.TryRecord(1, 2).Should().BeFalse();
        c.TryRecord(1, 11).Should().BeTrue(); // first send (t=0) aged out
    }

    [Fact]
    public void DifferentPlayers_Independent()
    {
        var c = new Cooldown(1.5, 5, 60);
        c.TryRecord(1, 0).Should().BeTrue();
        c.TryRecord(2, 0).Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter CooldownTests
```
Expected: build fails.

- [ ] **Step 3: Implement Cooldown**

`src/VoiceRoulette/Dispatch/Cooldown.cs`:
```csharp
using System.Collections.Generic;

namespace VoiceRoulette.Dispatch;

public sealed class Cooldown
{
    private readonly double _perSend;
    private readonly int _windowMax;
    private readonly double _windowSeconds;
    private readonly Dictionary<byte, Queue<double>> _history = new();

    public Cooldown(double perSendSeconds, int windowMax, double windowSeconds = 60.0)
    {
        _perSend = perSendSeconds;
        _windowMax = windowMax;
        _windowSeconds = windowSeconds;
    }

    public bool TryRecord(byte playerId, double nowSeconds)
    {
        if (!_history.TryGetValue(playerId, out var q))
        {
            q = new Queue<double>();
            _history[playerId] = q;
        }

        // Evict aged entries.
        while (q.Count > 0 && nowSeconds - q.Peek() >= _windowSeconds)
            q.Dequeue();

        // Per-send check.
        if (q.Count > 0 && nowSeconds - LastOf(q) < _perSend) return false;

        // Window cap.
        if (q.Count >= _windowMax) return false;

        q.Enqueue(nowSeconds);
        return true;
    }

    private static double LastOf(Queue<double> q)
    {
        double last = 0;
        foreach (var v in q) last = v;
        return last;
    }
}
```

- [ ] **Step 4: Run tests to verify pass**

```bash
dotnet test --filter CooldownTests
```
Expected: 6 tests pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(dispatch): cooldown with per-send and sliding window limits"
```

---

## Task 5: AudioCache (TDD)

**Goal:** Content-hashed file cache with LRU eviction at 100 MB.

**Files:**
- Create: `src/VoiceRoulette/TTS/AudioCache.cs`
- Create: `tests/VoiceRoulette.Tests/TTS/AudioCacheTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/VoiceRoulette.Tests/TTS/AudioCacheTests.cs
using System.IO;
using System.Text;
using FluentAssertions;
using VoiceRoulette.TTS;
using Xunit;

namespace VoiceRoulette.Tests.TTS;

public class AudioCacheTests : System.IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"vr-cache-{System.Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Key_IsStableForSameInputs()
    {
        AudioCache.Key("hi", "v1").Should().Be(AudioCache.Key("hi", "v1"));
        AudioCache.Key("hi", "v1").Should().NotBe(AudioCache.Key("hi", "v2"));
    }

    [Fact]
    public void TryGet_Miss_ReturnsFalse()
    {
        var c = new AudioCache(_dir, maxBytes: 1_000_000);
        c.TryGet(AudioCache.Key("nope", "v"), out var path).Should().BeFalse();
        path.Should().BeNull();
    }

    [Fact]
    public void Put_ThenGet_ReturnsPath()
    {
        var c = new AudioCache(_dir, maxBytes: 1_000_000);
        var key = AudioCache.Key("hello", "v");
        c.Put(key, Encoding.UTF8.GetBytes("audio-bytes"));
        c.TryGet(key, out var path).Should().BeTrue();
        File.ReadAllText(path!).Should().Be("audio-bytes");
    }

    [Fact]
    public void Put_OverCapacity_EvictsOldest()
    {
        var c = new AudioCache(_dir, maxBytes: 200);
        c.Put(AudioCache.Key("a", "v"), new byte[100]);
        System.Threading.Thread.Sleep(10);
        c.Put(AudioCache.Key("b", "v"), new byte[100]);
        System.Threading.Thread.Sleep(10);
        c.Put(AudioCache.Key("c", "v"), new byte[100]); // forces eviction

        c.TryGet(AudioCache.Key("a", "v"), out _).Should().BeFalse();
        c.TryGet(AudioCache.Key("b", "v"), out _).Should().BeTrue();
        c.TryGet(AudioCache.Key("c", "v"), out _).Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter AudioCacheTests
```
Expected: build fails.

- [ ] **Step 3: Implement AudioCache**

`src/VoiceRoulette/TTS/AudioCache.cs`:
```csharp
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace VoiceRoulette.TTS;

public sealed class AudioCache
{
    private const string FormatVersion = "v1";

    private readonly string _dir;
    private readonly long _maxBytes;
    private readonly object _gate = new();

    public AudioCache(string dir, long maxBytes)
    {
        _dir = dir;
        _maxBytes = maxBytes;
        Directory.CreateDirectory(_dir);
    }

    public static string Key(string text, string voice)
    {
        var input = $"{text}|{voice}|{FormatVersion}";
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public bool TryGet(string key, out string? path)
    {
        var p = PathFor(key);
        if (File.Exists(p))
        {
            File.SetLastAccessTimeUtc(p, DateTime.UtcNow);
            path = p;
            return true;
        }
        path = null;
        return false;
    }

    public void Put(string key, byte[] bytes)
    {
        lock (_gate)
        {
            File.WriteAllBytes(PathFor(key), bytes);
            EvictIfOverCapacity();
        }
    }

    private string PathFor(string key) => Path.Combine(_dir, $"{key}.mp3");

    private void EvictIfOverCapacity()
    {
        var files = new DirectoryInfo(_dir).GetFiles("*.mp3")
            .OrderBy(f => f.LastAccessTimeUtc)
            .ToList();
        long total = files.Sum(f => f.Length);
        var i = 0;
        while (total > _maxBytes && i < files.Count)
        {
            total -= files[i].Length;
            files[i].Delete();
            i++;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify pass**

```bash
dotnet test --filter AudioCacheTests
```
Expected: 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(tts): audio cache with LRU eviction"
```

---

## Task 6: DoubaoClient — Event Parser (TDD)

**Goal:** Pure function that parses Doubao Realtime API JSON events into typed records. No network in this task.

**Files:**
- Create: `src/VoiceRoulette/TTS/DoubaoEvents.cs`
- Create: `src/VoiceRoulette/TTS/DoubaoEventParser.cs`
- Create: `tests/VoiceRoulette.Tests/TTS/DoubaoEventParserTests.cs`
- Create: `tests/VoiceRoulette.Tests/TTS/Fixtures/audio_delta.json`
- Create: `tests/VoiceRoulette.Tests/TTS/Fixtures/audio_done.json`
- Create: `tests/VoiceRoulette.Tests/TTS/Fixtures/error.json`

- [ ] **Step 1: Create fixtures**

`tests/VoiceRoulette.Tests/TTS/Fixtures/audio_delta.json`:
```json
{ "type": "response.audio.delta", "delta": "AAECAwQF" }
```

`tests/VoiceRoulette.Tests/TTS/Fixtures/audio_done.json`:
```json
{ "type": "response.audio.done" }
```

`tests/VoiceRoulette.Tests/TTS/Fixtures/error.json`:
```json
{ "type": "error", "error": { "code": "invalid_api_key", "message": "bad key" } }
```

In `tests/VoiceRoulette.Tests/VoiceRoulette.Tests.csproj`, add inside an `<ItemGroup>`:
```xml
<None Update="TTS/Fixtures/*.json">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</None>
```

- [ ] **Step 2: Write failing tests**

```csharp
// tests/VoiceRoulette.Tests/TTS/DoubaoEventParserTests.cs
using System.IO;
using FluentAssertions;
using VoiceRoulette.TTS;
using Xunit;

namespace VoiceRoulette.Tests.TTS;

public class DoubaoEventParserTests
{
    private static string Read(string n) => File.ReadAllText(Path.Combine("TTS", "Fixtures", n));

    [Fact]
    public void Parse_AudioDelta_ReturnsBytes()
    {
        var ev = DoubaoEventParser.Parse(Read("audio_delta.json"));
        ev.Should().BeOfType<AudioDelta>();
        ((AudioDelta)ev).Bytes.Should().Equal(new byte[] { 0, 1, 2, 3, 4, 5 });
    }

    [Fact]
    public void Parse_AudioDone_ReturnsDoneMarker()
    {
        var ev = DoubaoEventParser.Parse(Read("audio_done.json"));
        ev.Should().BeOfType<AudioDone>();
    }

    [Fact]
    public void Parse_Error_CapturesCodeAndMessage()
    {
        var ev = DoubaoEventParser.Parse(Read("error.json"));
        var err = ev.Should().BeOfType<DoubaoError>().Subject;
        err.Code.Should().Be("invalid_api_key");
        err.Message.Should().Be("bad key");
    }

    [Fact]
    public void Parse_UnknownType_ReturnsUnknown()
    {
        var ev = DoubaoEventParser.Parse("{\"type\":\"weird.event\"}");
        ev.Should().BeOfType<UnknownEvent>();
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

```bash
dotnet test --filter DoubaoEventParserTests
```
Expected: build fails.

- [ ] **Step 4: Implement events + parser**

`src/VoiceRoulette/TTS/DoubaoEvents.cs`:
```csharp
namespace VoiceRoulette.TTS;

public abstract record DoubaoEvent;
public sealed record AudioDelta(byte[] Bytes) : DoubaoEvent;
public sealed record AudioDone : DoubaoEvent;
public sealed record DoubaoError(string Code, string Message) : DoubaoEvent;
public sealed record UnknownEvent(string Type) : DoubaoEvent;
```

`src/VoiceRoulette/TTS/DoubaoEventParser.cs`:
```csharp
using System;
using System.Text.Json;

namespace VoiceRoulette.TTS;

public static class DoubaoEventParser
{
    public static DoubaoEvent Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var type = root.GetProperty("type").GetString() ?? "";

        return type switch
        {
            "response.audio.delta" => new AudioDelta(
                Convert.FromBase64String(root.GetProperty("delta").GetString() ?? "")),
            "response.audio.done"  => new AudioDone(),
            "error" => new DoubaoError(
                root.GetProperty("error").GetProperty("code").GetString() ?? "",
                root.GetProperty("error").GetProperty("message").GetString() ?? ""),
            _ => new UnknownEvent(type),
        };
    }
}
```

- [ ] **Step 5: Run tests to verify pass**

```bash
dotnet test --filter DoubaoEventParserTests
```
Expected: 4 tests pass.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(tts): parse Doubao realtime events into typed records"
```

---

## Task 7: DoubaoClient — WebSocket Connection

**Goal:** Async WebSocket client that connects, sends a text request, and yields parsed events. Smoke test against real WSS gated by env var.

**Files:**
- Create: `src/VoiceRoulette/TTS/DoubaoClient.cs`
- Create: `tests/VoiceRoulette.Tests/TTS/DoubaoClientSmokeTests.cs`

- [ ] **Step 1: Implement DoubaoClient**

`src/VoiceRoulette/TTS/DoubaoClient.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceRoulette.TTS;

public sealed class DoubaoClient : IAsyncDisposable
{
    private readonly Uri _endpoint;
    private readonly string _apiKey;
    private readonly ClientWebSocket _ws = new();

    public DoubaoClient(string endpoint, string apiKey)
    {
        _endpoint = new Uri(endpoint);
        _apiKey = apiKey;
        _ws.Options.SetRequestHeader("Authorization", $"Bearer {apiKey}");
    }

    public async IAsyncEnumerable<DoubaoEvent> SynthesizeAsync(
        string text, string voiceType,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await _ws.ConnectAsync(_endpoint, ct).ConfigureAwait(false);
        await SendJson(new
        {
            type = "tts_session.update",
            session = new
            {
                voice_type = voiceType,
                output_audio_format = new { type = "mp3", sample_rate = 24000 }
            }
        }, ct);
        await SendJson(new { type = "input_text.append", text }, ct);
        await SendJson(new { type = "input_text.done" }, ct);

        var buf = new byte[16 * 1024];
        var sb = new StringBuilder();
        while (_ws.State == WebSocketState.Open)
        {
            var res = await _ws.ReceiveAsync(buf, ct).ConfigureAwait(false);
            if (res.MessageType == WebSocketMessageType.Close) yield break;
            sb.Append(Encoding.UTF8.GetString(buf, 0, res.Count));
            if (!res.EndOfMessage) continue;

            var msg = sb.ToString();
            sb.Clear();
            var ev = DoubaoEventParser.Parse(msg);
            yield return ev;
            if (ev is AudioDone or DoubaoError) yield break;
        }
    }

    private Task SendJson(object payload, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        return _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_ws.State == WebSocketState.Open)
            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", default);
        _ws.Dispose();
    }
}
```

- [ ] **Step 2: Add smoke test (skipped without API key)**

```csharp
// tests/VoiceRoulette.Tests/TTS/DoubaoClientSmokeTests.cs
using System;
using System.Threading;
using FluentAssertions;
using VoiceRoulette.TTS;
using Xunit;

namespace VoiceRoulette.Tests.TTS;

public class DoubaoClientSmokeTests
{
    [Fact]
    public async System.Threading.Tasks.Task RealApi_ReturnsAudioThenDone()
    {
        var key = Environment.GetEnvironmentVariable("DOUBAO_API_KEY");
        if (string.IsNullOrEmpty(key)) return; // skip without secret

        await using var client = new DoubaoClient(
            "wss://ai-gateway.vei.volces.com/v1/realtime?model=doubao-tts", key);

        var gotDelta = false;
        var gotDone  = false;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await foreach (var ev in client.SynthesizeAsync(
            "你好", "zh_female_kailangjiejie_moon_bigtts", cts.Token))
        {
            if (ev is AudioDelta d) { d.Bytes.Length.Should().BeGreaterThan(0); gotDelta = true; }
            if (ev is AudioDone)    { gotDone = true; }
            if (ev is DoubaoError e) throw new System.Exception($"{e.Code}: {e.Message}");
        }

        gotDelta.Should().BeTrue();
        gotDone.Should().BeTrue();
    }
}
```

- [ ] **Step 3: Run tests**

```bash
dotnet test --filter DoubaoClient
```
Expected: smoke test silently skips (no key in env) — pass count unchanged. If `DOUBAO_API_KEY` is set, it runs and passes.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(tts): Doubao realtime WebSocket client"
```

---

## Task 8: TTSPipeline (TDD with mocks)

**Goal:** Compose AudioCache + DoubaoClient. Cache hit → return cached path. Cache miss → call client, accumulate bytes, write to cache, return path.

**Files:**
- Create: `src/VoiceRoulette/TTS/ITTSBackend.cs`
- Create: `src/VoiceRoulette/TTS/TTSPipeline.cs`
- Create: `tests/VoiceRoulette.Tests/TTS/TTSPipelineTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/VoiceRoulette.Tests/TTS/TTSPipelineTests.cs
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using VoiceRoulette.TTS;
using Xunit;

namespace VoiceRoulette.Tests.TTS;

public class TTSPipelineTests : System.IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"vr-pipe-{System.Guid.NewGuid():N}");
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private sealed class FakeBackend : ITTSBackend
    {
        private readonly byte[][] _chunks;
        public int CallCount { get; private set; }
        public FakeBackend(params byte[][] chunks) => _chunks = chunks;
        public async IAsyncEnumerable<DoubaoEvent> SynthesizeAsync(
            string text, string voice,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            CallCount++;
            foreach (var c in _chunks) { yield return new AudioDelta(c); await Task.Yield(); }
            yield return new AudioDone();
        }
    }

    [Fact]
    public async Task Synthesize_CacheMiss_WritesAndReturnsPath()
    {
        var cache = new AudioCache(_dir, 1_000_000);
        var backend = new FakeBackend(new byte[]{1,2}, new byte[]{3,4});
        var pipe = new TTSPipeline(backend, cache);

        var path = await pipe.SynthesizeToFileAsync("hi", "v1");
        path.Should().NotBeNull();
        File.ReadAllBytes(path!).Should().Equal(new byte[]{1,2,3,4});
        backend.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Synthesize_CacheHit_SkipsBackend()
    {
        var cache = new AudioCache(_dir, 1_000_000);
        var backend = new FakeBackend(new byte[]{9});
        var pipe = new TTSPipeline(backend, cache);

        await pipe.SynthesizeToFileAsync("hi", "v1");
        await pipe.SynthesizeToFileAsync("hi", "v1");
        backend.CallCount.Should().Be(1);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter TTSPipelineTests
```
Expected: build fails.

- [ ] **Step 3: Implement interface + pipeline**

`src/VoiceRoulette/TTS/ITTSBackend.cs`:
```csharp
using System.Collections.Generic;
using System.Threading;

namespace VoiceRoulette.TTS;

public interface ITTSBackend
{
    IAsyncEnumerable<DoubaoEvent> SynthesizeAsync(string text, string voice, CancellationToken ct = default);
}
```

Add `: ITTSBackend` to `DoubaoClient` (no method changes needed — signature already matches).

`src/VoiceRoulette/TTS/TTSPipeline.cs`:
```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceRoulette.TTS;

public sealed class TTSPipeline
{
    private readonly ITTSBackend _backend;
    private readonly AudioCache _cache;

    public TTSPipeline(ITTSBackend backend, AudioCache cache)
    {
        _backend = backend;
        _cache = cache;
    }

    public async Task<string?> SynthesizeToFileAsync(string text, string voice, CancellationToken ct = default)
    {
        var key = AudioCache.Key(text, voice);
        if (_cache.TryGet(key, out var hit)) return hit;

        using var ms = new MemoryStream();
        await foreach (var ev in _backend.SynthesizeAsync(text, voice, ct))
        {
            switch (ev)
            {
                case AudioDelta d: ms.Write(d.Bytes); break;
                case DoubaoError e: throw new TTSException($"{e.Code}: {e.Message}");
                case AudioDone: break;
            }
        }
        _cache.Put(key, ms.ToArray());
        _cache.TryGet(key, out var path);
        return path;
    }
}

public sealed class TTSException : System.Exception
{
    public TTSException(string msg) : base(msg) { }
}
```

- [ ] **Step 4: Run tests to verify pass**

```bash
dotnet test --filter TTSPipelineTests
```
Expected: 2 tests pass; full test suite green.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(tts): pipeline composing cache + backend"
```

---

## Task 9: Wire Format & NetSync (TDD for serialization)

**Goal:** Implement message serialization and a thin `INetSync` interface. The actual transport (Steam P2P or STS2 bus) is bound in Task 13 based on Task 0's spike outcome.

**Files:**
- Create: `src/VoiceRoulette/Net/WireMessage.cs`
- Create: `src/VoiceRoulette/Net/INetSync.cs`
- Create: `tests/VoiceRoulette.Tests/Net/WireMessageTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/VoiceRoulette.Tests/Net/WireMessageTests.cs
using FluentAssertions;
using VoiceRoulette.Net;
using Xunit;

namespace VoiceRoulette.Tests.Net;

public class WireMessageTests
{
    [Fact]
    public void Roundtrip_PreservesAllFields()
    {
        var msg = new WireMessage(1, 3, "你好", "v_default", 1714560000UL);
        var bytes = WireMessage.Serialize(msg);
        var back  = WireMessage.Deserialize(bytes);
        back.Should().Be(msg);
    }

    [Fact]
    public void Deserialize_VersionMismatch_ReturnsNull()
    {
        var msg = new WireMessage(99, 0, "x", "y", 0);
        var bytes = WireMessage.Serialize(msg);
        WireMessage.Deserialize(bytes).Should().BeNull();
    }

    [Fact]
    public void Serialize_Size_FitsInP2PFrame()
    {
        var msg = new WireMessage(1, 0, new string('字', 100), "voice_xyz", 1UL);
        WireMessage.Serialize(msg).Length.Should().BeLessThan(1200);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter WireMessageTests
```
Expected: build fails.

- [ ] **Step 3: Implement WireMessage**

`src/VoiceRoulette/Net/WireMessage.cs`:
```csharp
using MessagePack;

namespace VoiceRoulette.Net;

[MessagePackObject]
public sealed record WireMessage(
    [property: Key(0)] byte Version,
    [property: Key(1)] byte Sender,
    [property: Key(2)] string Text,
    [property: Key(3)] string Voice,
    [property: Key(4)] ulong Timestamp)
{
    public const byte CurrentVersion = 1;

    public static byte[] Serialize(WireMessage m) => MessagePackSerializer.Serialize(m);

    public static WireMessage? Deserialize(byte[] bytes)
    {
        try
        {
            var m = MessagePackSerializer.Deserialize<WireMessage>(bytes);
            return m.Version == CurrentVersion ? m : null;
        }
        catch (MessagePackSerializationException) { return null; }
    }
}
```

`src/VoiceRoulette/Net/INetSync.cs`:
```csharp
using System;

namespace VoiceRoulette.Net;

public interface INetSync
{
    void Broadcast(WireMessage msg);
    event Action<WireMessage>? LineReceived;
}
```

- [ ] **Step 4: Run tests to verify pass**

```bash
dotnet test --filter WireMessageTests
```
Expected: 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(net): wire message + INetSync interface"
```

---

## Task 10: Dispatcher (TDD with mocks)

**Goal:** Apply cooldown, broadcast, and trigger local audio path. Pure orchestration — fully testable.

**Files:**
- Create: `src/VoiceRoulette/Dispatch/IAudioOutput.cs`
- Create: `src/VoiceRoulette/Dispatch/IClock.cs`
- Create: `src/VoiceRoulette/Dispatch/Dispatcher.cs`
- Create: `tests/VoiceRoulette.Tests/Dispatch/DispatcherTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/VoiceRoulette.Tests/Dispatch/DispatcherTests.cs
using System.Collections.Generic;
using FluentAssertions;
using VoiceRoulette.Dispatch;
using VoiceRoulette.Lines;
using VoiceRoulette.Net;
using Xunit;

namespace VoiceRoulette.Tests.Dispatch;

public class DispatcherTests
{
    private sealed class FakeClock : IClock { public double Now { get; set; } public double NowSeconds() => Now; }
    private sealed class FakeNet : INetSync
    {
        public List<WireMessage> Sent = new();
        public void Broadcast(WireMessage m) => Sent.Add(m);
        public event System.Action<WireMessage>? LineReceived;
        public void Receive(WireMessage m) => LineReceived?.Invoke(m);
    }
    private sealed class FakeAudio : IAudioOutput
    {
        public List<(byte slot, string text, string voice)> Played = new();
        public void Play(byte slot, string text, string voice) => Played.Add((slot, text, voice));
    }

    [Fact]
    public void Send_AllowedByCooldown_BroadcastsAndPlaysLocal()
    {
        var net = new FakeNet(); var audio = new FakeAudio(); var clock = new FakeClock();
        var d = new Dispatcher(localSlot: 2, new Cooldown(1.5, 5), net, audio, clock);

        d.Send(new Line("id", "hi", "v"));

        net.Sent.Should().HaveCount(1);
        net.Sent[0].Sender.Should().Be(2);
        audio.Played.Should().ContainSingle().Which.text.Should().Be("hi");
    }

    [Fact]
    public void Send_BlockedByCooldown_DoesNothing()
    {
        var net = new FakeNet(); var audio = new FakeAudio(); var clock = new FakeClock();
        var d = new Dispatcher(2, new Cooldown(1.5, 5), net, audio, clock);

        d.Send(new Line("id", "a", "v"));
        clock.Now = 0.5;
        d.Send(new Line("id", "b", "v"));

        net.Sent.Should().HaveCount(1);
        audio.Played.Should().HaveCount(1);
    }

    [Fact]
    public void Receive_PlaysRemoteWithSenderSlot()
    {
        var net = new FakeNet(); var audio = new FakeAudio(); var clock = new FakeClock();
        var d = new Dispatcher(2, new Cooldown(1.5, 5), net, audio, clock);

        net.Receive(new WireMessage(1, 5, "x", "v", 0));

        audio.Played.Should().ContainSingle().Which.slot.Should().Be(5);
    }

    [Fact]
    public void Receive_FromSelf_Ignored()
    {
        var net = new FakeNet(); var audio = new FakeAudio(); var clock = new FakeClock();
        var d = new Dispatcher(2, new Cooldown(1.5, 5), net, audio, clock);

        net.Receive(new WireMessage(1, 2, "x", "v", 0));

        audio.Played.Should().BeEmpty();
    }

    [Fact]
    public void Send_EmptyText_Skipped()
    {
        var net = new FakeNet(); var audio = new FakeAudio(); var clock = new FakeClock();
        var d = new Dispatcher(2, new Cooldown(1.5, 5), net, audio, clock);

        d.Send(new Line("id", "", "v"));

        net.Sent.Should().BeEmpty();
        audio.Played.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter DispatcherTests
```
Expected: build fails.

- [ ] **Step 3: Implement Dispatcher**

`src/VoiceRoulette/Dispatch/IAudioOutput.cs`:
```csharp
namespace VoiceRoulette.Dispatch;

public interface IAudioOutput
{
    void Play(byte senderSlot, string text, string voice);
}
```

`src/VoiceRoulette/Dispatch/IClock.cs`:
```csharp
namespace VoiceRoulette.Dispatch;

public interface IClock
{
    double NowSeconds();
}
```

`src/VoiceRoulette/Dispatch/Dispatcher.cs`:
```csharp
using VoiceRoulette.Lines;
using VoiceRoulette.Net;

namespace VoiceRoulette.Dispatch;

public sealed class Dispatcher
{
    private readonly byte _localSlot;
    private readonly Cooldown _cooldown;
    private readonly INetSync _net;
    private readonly IAudioOutput _audio;
    private readonly IClock _clock;

    public Dispatcher(byte localSlot, Cooldown cooldown, INetSync net, IAudioOutput audio, IClock clock)
    {
        _localSlot = localSlot;
        _cooldown = cooldown;
        _net = net;
        _audio = audio;
        _clock = clock;
        _net.LineReceived += OnReceived;
    }

    public void Send(Line line)
    {
        if (string.IsNullOrEmpty(line.Text)) return;
        if (!_cooldown.TryRecord(_localSlot, _clock.NowSeconds())) return;

        var msg = new WireMessage(
            WireMessage.CurrentVersion, _localSlot, line.Text, line.Voice,
            (ulong)System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        _net.Broadcast(msg);
        _audio.Play(_localSlot, line.Text, line.Voice);
    }

    private void OnReceived(WireMessage m)
    {
        if (m.Sender == _localSlot) return; // self-echo guard
        _audio.Play(m.Sender, m.Text, m.Voice);
    }
}
```

- [ ] **Step 4: Run tests to verify pass**

```bash
dotnet test --filter DispatcherTests
```
Expected: 5 tests pass; whole suite green.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(dispatch): orchestrator wiring cooldown, net, and audio"
```

---

## Task 11: AudioPlayer (Godot — manual verify)

**Goal:** Implement `IAudioOutput` against Godot's audio system. Plays a file or streams TTS on demand.

**Files:**
- Create: `src/VoiceRoulette/Audio/AudioPlayer.cs`

- [ ] **Step 1: Implement AudioPlayer**

`src/VoiceRoulette/Audio/AudioPlayer.cs`:
```csharp
using System.Threading.Tasks;
using Godot;
using VoiceRoulette.Dispatch;
using VoiceRoulette.TTS;

namespace VoiceRoulette.Audio;

public sealed partial class AudioPlayer : Node, IAudioOutput
{
    private readonly TTSPipeline _tts;
    private readonly AudioStreamPlayer[] _slots;
    private const string BusName = "VoiceRoulette";

    public AudioPlayer(TTSPipeline tts, int maxSlots = 4)
    {
        _tts = tts;
        EnsureBus();
        _slots = new AudioStreamPlayer[maxSlots];
        for (int i = 0; i < maxSlots; i++)
        {
            var p = new AudioStreamPlayer { Bus = BusName };
            AddChild(p);
            _slots[i] = p;
        }
    }

    private static void EnsureBus()
    {
        if (AudioServer.GetBusIndex(BusName) >= 0) return;
        AudioServer.AddBus();
        AudioServer.SetBusName(AudioServer.BusCount - 1, BusName);
    }

    public void Play(byte senderSlot, string text, string voice)
    {
        _ = PlayAsync(senderSlot, text, voice);
    }

    private async Task PlayAsync(byte senderSlot, string text, string voice)
    {
        var path = await _tts.SynthesizeToFileAsync(text, voice);
        if (path == null) return;
        var stream = AudioStreamMP3.LoadFromFile(path); // Godot 4.5 helper
        var player = _slots[senderSlot % _slots.Length];
        player.Stream = stream;
        player.Play();
    }
}
```

> If `AudioStreamMP3.LoadFromFile` is not available in this Godot version, replace with manual `new AudioStreamMP3 { Data = File.ReadAllBytes(path) }`.

- [ ] **Step 2: Build (no tests; Godot types only resolve in-game)**

```bash
dotnet build
```
Expected: build succeeds.

- [ ] **Step 3: Manual verification deferred**

Audio playback is verified end-to-end in Task 15's manual test plan, since the Godot scene needs to be running.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(audio): Godot audio player with per-slot streams"
```

---

## Task 12: WheelUI Scene + Script (Godot — manual verify)

**Goal:** 8-sector radial menu controlled by `Show`/`Hide`/`SetSelected`.

**Files:**
- Create: `src/VoiceRoulette/UI/WheelUI.cs`
- Create: `src/VoiceRoulette/UI/WheelScene.tscn` (text-format Godot scene)

- [ ] **Step 1: Implement WheelUI**

`src/VoiceRoulette/UI/WheelUI.cs`:
```csharp
using Godot;
using System;
using System.Collections.Generic;

namespace VoiceRoulette.UI;

public sealed partial class WheelUI : CanvasLayer
{
    private const int Sectors = 8;
    private readonly Label[] _labels = new Label[Sectors];
    private Label? _hint;
    private int _selected = -1;
    private List<string> _texts = new();

    public override void _Ready()
    {
        var center = new Vector2(GetViewport().GetVisibleRect().Size.X / 2,
                                 GetViewport().GetVisibleRect().Size.Y / 2);
        const float radius = 180f;
        for (int i = 0; i < Sectors; i++)
        {
            var angle = -Mathf.Pi / 2 + i * (2 * Mathf.Pi / Sectors);
            var lbl = new Label { Position = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius };
            AddChild(lbl);
            _labels[i] = lbl;
        }
        _hint = new Label { Position = center + new Vector2(-100, radius + 40) };
        AddChild(_hint);
        Visible = false;
    }

    public void Show(IList<string> lineTexts)
    {
        _texts = new List<string>(lineTexts);
        for (int i = 0; i < Sectors; i++)
            _labels[i].Text = i < _texts.Count ? _texts[i] : "";
        _selected = -1;
        Visible = true;
    }

    public new void Hide() { Visible = false; _selected = -1; }

    public void SetSelectedFromMouse(Vector2 mouseDelta)
    {
        if (mouseDelta.LengthSquared() < 25) { Highlight(-1); return; } // dead zone
        var angle = Mathf.Atan2(mouseDelta.Y, mouseDelta.X) + Mathf.Pi / 2 + Mathf.Pi / Sectors;
        if (angle < 0) angle += 2 * Mathf.Pi;
        var idx = (int)(angle / (2 * Mathf.Pi / Sectors)) % Sectors;
        Highlight(idx);
    }

    public int SelectedIndex => _selected;

    private void Highlight(int idx)
    {
        if (idx == _selected) return;
        if (_selected >= 0) _labels[_selected].Modulate = Colors.White;
        if (idx >= 0) _labels[idx].Modulate = Colors.Yellow;
        _selected = idx;
        if (_hint != null) _hint.Text = idx >= 0 && idx < _texts.Count ? _texts[idx] : "";
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build
```
Expected: succeeds.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat(ui): radial 8-sector wheel with cursor-direction selection"
```

---

## Task 13: InputCapture (Godot)

**Goal:** Detect hold/release of bound key, feed mouse delta to WheelUI.

**Files:**
- Create: `src/VoiceRoulette/Input/InputCapture.cs`

- [ ] **Step 1: Implement InputCapture**

`src/VoiceRoulette/Input/InputCapture.cs`:
```csharp
using Godot;
using System;
using VoiceRoulette.UI;

namespace VoiceRoulette.Input;

public sealed partial class InputCapture : Node
{
    private readonly Key _hotkey;
    private readonly WheelUI _wheel;
    private bool _held;
    private Vector2 _origin;

    public event Action<int>? Released; // sector idx, or -1 if cancelled

    public InputCapture(Key hotkey, WheelUI wheel)
    {
        _hotkey = hotkey;
        _wheel = wheel;
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is InputEventKey k && k.Keycode == _hotkey)
        {
            if (k.Pressed && !_held)
            {
                _held = true;
                _origin = GetViewport().GetMousePosition();
                _wheel.Show(GetCurrentLineTexts());
            }
            else if (!k.Pressed && _held)
            {
                _held = false;
                Released?.Invoke(_wheel.SelectedIndex);
                _wheel.Hide();
            }
        }
        else if (ev is InputEventKey esc && esc.Pressed && esc.Keycode == Key.Escape && _held)
        {
            _held = false;
            Released?.Invoke(-1);
            _wheel.Hide();
        }
        else if (ev is InputEventMouseMotion mm && _held)
        {
            _wheel.SetSelectedFromMouse(GetViewport().GetMousePosition() - _origin);
        }
    }

    // Replaced by binding to LineRegistry in Plugin.cs wiring (Task 15).
    private string[] GetCurrentLineTexts() => Array.Empty<string>();
}
```

- [ ] **Step 2: Build**

```bash
dotnet build
```
Expected: succeeds.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat(input): hotkey hold/release and mouse-direction capture"
```

---

## Task 14: NetSync Implementation (depends on Task 0)

**Goal:** Implement `INetSync` against the transport chosen in Task 0's spike.

**Files:**
- Create: `src/VoiceRoulette/Net/SteamP2PNetSync.cs` (if FALLBACK chosen)
- Create: `src/VoiceRoulette/Net/Sts2BusNetSync.cs` (if PRIMARY chosen)

Pick exactly one of the two sub-paths below per the spike result. Delete the other file before commit.

### 14a — If PRIMARY (STS2 message bus)

- [ ] **Step 1: Implement Sts2BusNetSync**

Use the exact reflection / hook signatures captured in `phase-0-multiplayer-spike.md`. Skeleton:

```csharp
using System;
using HarmonyLib;
using VoiceRoulette.Net;

namespace VoiceRoulette.Net;

public sealed class Sts2BusNetSync : INetSync
{
    public event Action<WireMessage>? LineReceived;

    public Sts2BusNetSync()
    {
        // Subscribe to the discovered STS2 mod-message channel.
        // e.g. MegaCrit.Sts2.Multiplayer.MessageBus.Register("voice_roulette", OnRaw);
    }

    public void Broadcast(WireMessage msg)
    {
        var bytes = WireMessage.Serialize(msg);
        // MegaCrit.Sts2.Multiplayer.MessageBus.Send("voice_roulette", bytes);
    }

    private void OnRaw(byte[] bytes)
    {
        var msg = WireMessage.Deserialize(bytes);
        if (msg != null) LineReceived?.Invoke(msg);
    }
}
```

### 14b — If FALLBACK (Steamworks.NET)

- [ ] **Step 1: Add Steamworks.NET reference**

Add to `VoiceRoulette.csproj`:
```xml
<Reference Include="Steamworks.NET" HintPath="../../lib/Steamworks.NET.dll" Private="false" />
```
Copy `Steamworks.NET.dll` from the game install to `lib/`.

- [ ] **Step 2: Implement SteamP2PNetSync**

```csharp
using System;
using Steamworks;
using VoiceRoulette.Net;

namespace VoiceRoulette.Net;

public sealed class SteamP2PNetSync : INetSync
{
    private const int ChannelId = 0xFE; // arbitrary mod channel
    public event Action<WireMessage>? LineReceived;

    public void Broadcast(WireMessage msg)
    {
        var bytes = WireMessage.Serialize(msg);
        foreach (var lobbyMember in EnumerateLobbyMembers())
            SteamNetworking.SendP2PPacket(lobbyMember, bytes, (uint)bytes.Length,
                EP2PSend.k_EP2PSendReliable, ChannelId);
    }

    public void Tick()
    {
        while (SteamNetworking.IsP2PPacketAvailable(out var size, ChannelId))
        {
            var buf = new byte[size];
            if (SteamNetworking.ReadP2PPacket(buf, size, out _, out _, ChannelId))
            {
                var msg = WireMessage.Deserialize(buf);
                if (msg != null) LineReceived?.Invoke(msg);
            }
        }
    }

    private static System.Collections.Generic.IEnumerable<CSteamID> EnumerateLobbyMembers()
    {
        // Use the active lobby ID discovered from STS2 game state.
        yield break;
    }
}
```

- [ ] **Step 3: Build**

```bash
dotnet build
```
Expected: succeeds.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(net): NetSync implementation per phase-0 spike"
```

---

## Task 15: Plugin Wiring + Manual In-Game Verification

**Goal:** Wire all modules together in `Plugin.Initialize`, run the mod in STS2, and verify the manual test plan from spec §8.

**Files:**
- Modify: `src/VoiceRoulette/Plugin.cs`
- Create: `scripts/install-local.sh`
- Create: `lines.default.json`
- Create: `assets/.gitkeep`

- [ ] **Step 1: Replace Plugin.cs with full wiring**

```csharp
using System.IO;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using VoiceRoulette.Audio;
using VoiceRoulette.Config;
using VoiceRoulette.Dispatch;
using VoiceRoulette.Input;
using VoiceRoulette.Lines;
using VoiceRoulette.Net;
using VoiceRoulette.TTS;
using VoiceRoulette.UI;

namespace VoiceRoulette;

public static class Plugin
{
    public const string Id = "voice_roulette";
    public const string Version = "0.1.0";

    private static ConfigStore? _config;
    private static AudioPlayer? _audio;
    private static InputCapture? _input;
    private static Dispatcher? _dispatcher;

    [ModInitializer("Initialize")]
    public static void Initialize()
    {
        var modDir = ModEnvironment.GetModDirectory(Id); // BaseLib helper; adjust per actual API
        _config = ConfigStore.Load(Path.Combine(modDir, "config.json"));

        var registry = new LineRegistry(_config.Schema);
        var cache = new AudioCache(Path.Combine(modDir, "cache"), maxBytes: 100 * 1024 * 1024);
        var doubao = new DoubaoClient(_config.Schema.Doubao.Endpoint, _config.Schema.Doubao.ApiKey);
        var pipeline = new TTSPipeline(doubao, cache);

        var sceneRoot = (SceneTree)Engine.GetMainLoop();
        var wheel = new WheelUI();
        sceneRoot.Root.AddChild(wheel);

        _audio = new AudioPlayer(pipeline);
        sceneRoot.Root.AddChild(_audio);

        var net = new SteamP2PNetSync(); // or Sts2BusNetSync per Task 14 choice
        _dispatcher = new Dispatcher(
            localSlot: SessionInfo.GetLocalSlot(), // BaseLib helper; adjust
            new Cooldown(_config.Schema.Cooldown.PerSend, _config.Schema.Cooldown.WindowMax),
            net, _audio, new GodotClock());

        _input = new InputCapture(Key.V, wheel);
        sceneRoot.Root.AddChild(_input);
        _input.Released += idx =>
        {
            if (idx < 0) return;
            var line = registry.Resolve(WheelPage.Common, idx, character: SessionInfo.GetLocalCharacter());
            _dispatcher!.Send(line);
        };

        Log.Info($"[{Id}] {Version} initialized");
    }

    public static void Unload() => _config?.Save();

    private sealed class GodotClock : IClock
    {
        public double NowSeconds() => Time.GetTicksMsec() / 1000.0;
    }
}
```

> `ModEnvironment` and `SessionInfo` reflect helpers that exist in BaseLib-StS2 — substitute the actual class names from the BaseLib wiki. If BaseLib does not yet expose the local-slot helper, fall back to reflection on STS2 multiplayer state.

- [ ] **Step 2: Create lines.default.json**

`lines.default.json`:
```json
{
  "common": [
    { "id": "good_card",  "text": "好牌！" },
    { "id": "fight_elite","text": "打精英怪！" },
    { "id": "rest_now",   "text": "去休息点" },
    { "id": "press_on",   "text": "继续推进" },
    { "id": "wait",       "text": "等一下" },
    { "id": "retreat",    "text": "撤退！" },
    { "id": "tank",       "text": "我来挡" },
    { "id": "nice",       "text": "干得漂亮！" }
  ]
}
```

- [ ] **Step 3: Create install-local.sh**

`scripts/install-local.sh`:
```bash
#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
GAME_MODS="$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS/mods"
DEST="$GAME_MODS/voice_roulette"

dotnet build "$ROOT/src/VoiceRoulette/VoiceRoulette.csproj" -c Release
mkdir -p "$DEST"
cp "$ROOT/src/VoiceRoulette/bin/Release/net9.0/VoiceRoulette.dll" "$DEST/"
cp "$ROOT/src/VoiceRoulette/manifest.json" "$DEST/"
cp "$ROOT/lines.default.json" "$DEST/"
echo "Installed to $DEST"
```

```bash
chmod +x scripts/install-local.sh
```

- [ ] **Step 4: Build, install, launch the game**

```bash
./scripts/install-local.sh
```

Then:
1. Edit `~/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS/mods/voice_roulette/config.json` and paste a valid `doubao.apiKey`.
2. Launch STS2 via Finder (right-click → Get Info → Open with Rosetta first if needed; per spec R5).
3. Enable the mod in the launcher's "Load with mods" panel.

- [ ] **Step 5: Manual test plan**

Run each scenario; record pass/fail in a new file `docs/superpowers/notes/v0.1-manual-tests.md`.

| # | Scenario | Expected |
|---|----------|----------|
| 1 | Solo run, hold V | Wheel opens centered on cursor |
| 2 | Move mouse N/NE/E/.../NW | Highlight follows cursor direction |
| 3 | Release on sector 0 (好牌！) | Audio plays within 1s (cache miss) or instantly (cache hit) |
| 4 | Repeat sector 0 immediately | Blocked by 1.5s cooldown — no audio |
| 5 | Press V then Esc | Wheel closes, no audio |
| 6 | Spam 6 sectors in 60s | 6th send blocked by window cap |
| 7 | Disable wifi mid-stream | First send: text bubble shows, audio silently skipped |
| 8 | 2-player co-op with friend | Both clients hear each other's lines |
| 9 | Restart game | Cached MP3s persist; previously-spoken lines play instantly |

- [ ] **Step 6: Fix any failures**

For each failed scenario, file a note, fix the bug, re-run unit tests + manual scenario.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: wire all modules and ship v0.1 manual test plan"
```

---

## Task 16: Packaging + README

**Goal:** Produce a single distributable ZIP and a user-facing README.

**Files:**
- Create: `scripts/package.sh`
- Create: `README.md`

- [ ] **Step 1: Write package.sh**

`scripts/package.sh`:
```bash
#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VER=$(grep '"version"' "$ROOT/src/VoiceRoulette/manifest.json" | head -1 | cut -d'"' -f4)
OUT="$ROOT/dist/voice_roulette-$VER"

dotnet build "$ROOT/src/VoiceRoulette/VoiceRoulette.csproj" -c Release
rm -rf "$OUT" && mkdir -p "$OUT"
cp "$ROOT/src/VoiceRoulette/bin/Release/net9.0/VoiceRoulette.dll" "$OUT/"
cp "$ROOT/src/VoiceRoulette/manifest.json" "$OUT/"
cp "$ROOT/lines.default.json" "$OUT/"
[ -d "$ROOT/assets/prerendered" ] && cp -R "$ROOT/assets/prerendered" "$OUT/"
(cd "$ROOT/dist" && zip -r "voice_roulette-$VER.zip" "voice_roulette-$VER")
echo "Built $ROOT/dist/voice_roulette-$VER.zip"
```

```bash
chmod +x scripts/package.sh
```

- [ ] **Step 2: Write README.md**

```markdown
# Voice Roulette — Slay the Spire 2 Mod

Dota 2-style radial chat wheel with Doubao TTS for STS2 co-op.

## Install (macOS)

1. Download `voice_roulette-X.Y.Z.zip` from Releases.
2. Unzip into `~/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS/mods/`.
3. Right-click the game executable → Get Info → check **Open using Rosetta**.
4. Launch via Finder (NOT Steam button) and enable the mod.

## Doubao API Key

1. Get a key at https://www.volcengine.com/.
2. Open `mods/voice_roulette/config.json`.
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
```

- [ ] **Step 3: Build a release ZIP locally**

```bash
./scripts/package.sh
ls dist/
```
Expected: a ZIP file appears.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "chore: packaging script and user README"
```

---

## Known Spec Deferrals (v0.1 → v0.2)

These spec items are intentionally not implemented in the 16 tasks above. Adding them would require additional tasks; defer until v0.1 is shipped and validated:

- **Page toggle (Common / Character / Custom)** — spec §3.1 step 5. Task 12's WheelUI only renders one page; Plugin wiring hardcodes `WheelPage.Common`. Add a center-button click handler + page state in WheelUI.
- **Floating text bubble above sender's portrait** — spec §3.2. Audio-only in v0.1. Add a `BubbleOverlay` Node that subscribes to `IAudioOutput.Play` events.
- **Per-player mute toggle** — spec §3.3. Schema field `audio.muted` exists but is unused in `Dispatcher.OnReceived`.
- **Pre-rendered offline MP3s shipped in PCK** — spec §5.7. Cache directory is empty on first install; first call to each preset line requires network.

## Final Verification

- [ ] `dotnet test` reports all tests passing.
- [ ] `./scripts/package.sh` produces a ZIP.
- [ ] `./scripts/install-local.sh` puts the DLL in the game mod folder.
- [ ] Manual test plan in Task 15 fully passes.
- [ ] `git log --oneline` shows ~16 task commits with conventional-commit messages.
