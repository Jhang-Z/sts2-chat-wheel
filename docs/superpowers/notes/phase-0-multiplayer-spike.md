# Phase 0 Spike — Multiplayer API Discovery

**Date:** 2026-05-01
**Goal:** Decide whether `NetSync` hooks into STS2's co-op message bus or talks Steam P2P directly. (Spec risk R1)

---

## Step 1: Game DLL Location

The game is a **Godot 4.5 / .NET 9** project. There is no `Assembly-CSharp.dll`. The primary managed assembly is:

```
/Users/jhang-z/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/
  SlayTheSpire2.app/Contents/Resources/data_sts2_macos_arm64/sts2.dll
```

Other notable DLLs in the same directory:
- `GodotSharp.dll` — Godot .NET bindings
- `Steamworks.NET.dll` — Steam API wrapper (present and shipped with the game)
- `MonoMod.Backports.dll`, `MonoMod.ILHelpers.dll` — patching support
- `0Harmony.dll` — Harmony patcher (confirming mods can patch game code)

---

## Step 2: ilspycmd Installation

`dotnet` SDK is not installed on this machine (no `dotnet` binary found anywhere). `ilspycmd` could not be installed. Decompilation was performed using `strings` extraction on `sts2.dll`, which reveals all public type names, method names, and embedded source paths. This is sufficient to characterize the API surface.

---

## Step 3: Multiplayer Namespaces Found

The following namespaces exist in `sts2.dll`:

```
MegaCrit.Sts2.Core.Multiplayer
MegaCrit.Sts2.Core.Multiplayer.Connection
MegaCrit.Sts2.Core.Multiplayer.Game
MegaCrit.Sts2.Core.Multiplayer.Game.Lobby
MegaCrit.Sts2.Core.Multiplayer.Game.PeerInput
MegaCrit.Sts2.Core.Multiplayer.Messages
MegaCrit.Sts2.Core.Multiplayer.Messages.Game
MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Checksums
MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Flavor
MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Sync
MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby
MegaCrit.Sts2.Core.Multiplayer.Quality
MegaCrit.Sts2.Core.Multiplayer.Replay
MegaCrit.Sts2.Core.Multiplayer.Serialization
MegaCrit.Sts2.Core.Multiplayer.Transport
MegaCrit.Sts2.Core.Multiplayer.Transport.ENet
MegaCrit.Sts2.Core.Multiplayer.Transport.Steam
```

Files referenced in embedded source paths:
```
res://src/Core/Nodes/Multiplayer/NGenericPopup.cs
res://src/Core/Nodes/Multiplayer/NRemoteLobbyPlayer.cs
res://src/Core/Nodes/Multiplayer/NRemoteMouseCursor.cs
res://src/Core/Nodes/Multiplayer/NInvitePlayersButton.cs
res://src/Core/Nodes/Multiplayer/NMultiplayerCardIntent.cs
res://src/Core/Nodes/Multiplayer/NMultiplayerPlayerState.cs
res://src/Core/Nodes/CommonUi/NMultiplayerVoteContainer.cs
res://src/Core/Nodes/Debug/Multiplayer/NMultiplayerTest.cs
```

---

## Step 4: Message Dispatch Pattern Analysis

### Q1: Is there a typed event bus or RPC method any mod can hook?

**Yes — there is a typed message handler registration system.** Key discovered names:

```
INetMessage                   — marker interface for all messages
INetMessageSubtypes           — subtypes registry
MessageHandlerDelegate`1      — typed generic delegate
AnonymizedMessageHandlerDelegate
RegisterMessageHandler        — registers a typed handler
UnregisterMessageHandler      — removes a handler
TypeAndMessageHandlers        — registry container
```

The pattern is: implement `INetMessage`, register a handler with `RegisterMessageHandler`, and the bus delivers messages to all registered handlers. This is structurally a typed pub/sub bus.

### Q2: What is the signature of "send a message to all players"?

The bus exposes these send methods (names extracted verbatim from binary):

```csharp
SendMessage(...)               // targeted single peer
SendMessageToAll(...)          // broadcast to all connected peers
SendMessageToAllHandlers(...)  // deliver to all local registered handlers
SendMessageToClient(...)       // host → specific client
SendMessageToClientInternal(...)
SendMessageToConnection(...)   // low-level connection-scoped send
SendMessageToHost(...)         // client → host
BroadcastMessage(...)          // alias/variant of SendMessageToAll
```

The **primary broadcast method is `SendMessageToAll`** (or `BroadcastMessage`). The host/client topology is explicit: there is a `SteamHost` / `ENetHost` and `SteamClient` / `ENetClient` duality.

#### Synchronizer classes observed (all in `MegaCrit.Sts2.Core.Multiplayer.Game`):

```
ActionQueueSynchronizer
ActChangeSynchronizer
CombatStateSynchronizer
EventSynchronizer
FlavorSynchronizer
InputSynchronizer
MapSelectionSynchronizer
OneOffSynchronizer
PeerInputSynchronizer
PlayerChoiceSynchronizer
ReactionSynchronizer       ← most relevant to Voice Roulette
RestSiteSynchronizer
RewardSynchronizer
TestSynchronizer
TreasureRoomRelicSynchronizer
```

**`ReactionSynchronizer`** already handles the `HandleReactionMessage` flow, which is structurally identical to what Voice Roulette needs (player → all peers: "I played voice clip N").

#### Message types relevant to Voice Roulette:

```
HandleReactionMessage
HandleMapPingMessage         // similar broadcast pattern
HandleFlavorSynchronizer     // game flavor/cosmetic messages
```

### Q3: Does BaseLib-StS2 already expose a wrapper?

**No.** The BaseLib Wiki (https://alchyr.github.io/BaseLib-Wiki/) was fetched successfully. The Hooks and Mechanics page lists only gameplay hooks (`IHealAmountModifier`, `IMaxHandSizeModifier`, `IHealthBarForecastSource`) and keyword/variable utilities. There is no mention of multiplayer message bus wrappers, custom `INetMessage` registration helpers, or `SendMessageToAll` abstractions. BaseLib does not expose a multiplayer API layer.

### Q4: Transport layer — Steam vs ENet?

The game ships **two transports**:
- `MegaCrit.Sts2.Core.Multiplayer.Transport.Steam` — `SteamHost`, `SteamClient`, `SteamClientConnectionInitializer`. Uses `SteamNetworkingSockets` (Steam's relay/P2P layer), not raw `SendP2PPacket`. Steam lobby join flow: `ConnectToLobby`.
- `MegaCrit.Sts2.Core.Multiplayer.Transport.ENet` — `ENetHost`, `ENetClient`, `ENetClientConnectionInitializer`. Used for LAN/local play.

The `INetMessage` + `RegisterMessageHandler` + `SendMessageToAll` API sits **above** both transports. A mod that uses this layer is transport-agnostic and works whether the session uses Steam relay or ENet.

---

## Step 5: Decision

### Decision: **PRIMARY — Hook STS2 message bus**

**Rationale:**

STS2 already ships a well-structured typed message bus (`INetMessage` / `RegisterMessageHandler` / `SendMessageToAll`) that sits above both Steam and ENet transports. The existing `ReactionSynchronizer` + `HandleReactionMessage` pattern is exactly the precedent Voice Roulette needs. Hooking the game bus means:

1. Zero new network connections to establish.
2. The message piggybacks on the session's existing Steam relay — no NAT issues, no raw P2P socket management.
3. Mod co-existence is natural: any other mod doing the same is independent because each registers its own `INetMessage` subtype.
4. The FALLBACK (raw `SteamNetworkingSockets` P2P channel via `Steamworks.NET.dll`) is only needed if the internal bus turns out to be inaccessible to mod code at runtime (e.g., internal visibility on key types). That risk is noted below.

### Code skeleton for `NetSync.Broadcast(byte[])`

```csharp
// VoiceRouletteMessage.cs
// Implements STS2's INetMessage to carry a voice-wheel selection.
using MegaCrit.Sts2.Core.Multiplayer.Messages;

public sealed class VoiceRouletteMessage : INetMessage {
    public byte VoiceId { get; init; }
    public int SourcePeerId { get; init; }
}

// NetSync.cs
// Registered once on mod load; torn down on session end.
using MegaCrit.Sts2.Core.Multiplayer;

public static class NetSync {
    private static IMultiplayerContext? _ctx;

    public static void Register(IMultiplayerContext ctx) {
        _ctx = ctx;
        ctx.RegisterMessageHandler<VoiceRouletteMessage>(OnVoiceRouletteReceived);
    }

    public static void Broadcast(byte voiceId) =>
        _ctx?.SendMessageToAll(new VoiceRouletteMessage {
            VoiceId = voiceId,
            SourcePeerId = _ctx.LocalPeerId,
        });

    private static void OnVoiceRouletteReceived(VoiceRouletteMessage msg) =>
        VoiceWheelUi.ShowRemoteTrigger(msg.SourcePeerId, msg.VoiceId);
}
```

> **Note:** `IMultiplayerContext` is the assumed name for the context object that exposes `RegisterMessageHandler` and `SendMessageToAll`. The exact type name was not recoverable from string extraction alone — it will need to be confirmed with a full decompile (once `dotnet` SDK is installed) or by inspecting the public API of the `MegaCrit.Sts2.Core.Multiplayer` namespace at runtime.

---

## Concerns / Open Risks

1. **Internal visibility.** `INetMessage`, `RegisterMessageHandler`, and `SendMessageToAll` are confirmed present in the binary but their access modifiers are unknown from string extraction alone. If they are `internal`, the mod must either use Harmony to access them or fall back to Steamworks.NET P2P.

2. **dotnet SDK not installed.** Full ILSpy decompilation was not possible. Install `dotnet` SDK and run `ilspycmd sts2.dll -p -o /tmp/sts2-decomp` to confirm method signatures and access modifiers before Task 14.

3. **`IMultiplayerContext` name unconfirmed.** The exact type that exposes `RegisterMessageHandler` is inferred from surrounding names but not directly confirmed. Task 14 must resolve this before writing production code.

4. **BaseLib has no multiplayer wrapper.** The mod will need to reference `sts2.dll` directly (as a NuGet or project reference to the game DLL). This is standard for STS2 mods per the mod template.

5. **ENet vs Steam session.** Voice Roulette sessions will use Steam relay in online play. The bus layer is transport-agnostic so this is not a blocking concern, but it should be tested in both modes.
