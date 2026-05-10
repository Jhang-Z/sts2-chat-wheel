// Live overlay showing each remote (teammate) player's current hand of
// cards at the top of the screen during combat. Rendered using the game's
// own NCard.Create(cardModel, ModelVisibility.Visible) factory so the
// cards look identical to the local player's hand at the bottom.
//
// Implementation notes:
//   • Polls every 250ms — fast enough to feel real-time, light enough to
//     not matter. We compare hand-card identity hashes per player to
//     decide whether to rebuild that player's row.
//   • Skips the local player (we see our own hand below).
//   • Hides itself when there's no active combat / no remote players.
//   • Cards scaled to ~0.35× of their natural size so 5+ cards fit
//     comfortably across the screen for each teammate.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace VoiceRoulette.UI;

public sealed partial class RemoteHandsOverlay : CanvasLayer
{
    private const int LayerIndex = 90;       // above game world, below modals
    private const float CardScale = 0.35f;
    private const float CardSpacing = 6f;
    private const float RowSpacing = 8f;
    private const int   TopMarginPx = 60;    // sit below the top bar
    private const double RefreshIntervalSec = 0.25;

    private SceneTree? _tree;
    private Control? _root;
    private VBoxContainer? _rows;
    private double _nextRefreshSec;
    private ulong? _localNetIdCache;

    // Per-remote-player state — lets us only rebuild that player's row when
    // their hand has actually changed. Key = NetId.
    private sealed class PlayerRowState
    {
        public HBoxContainer Row = null!;
        public Label NameLabel = null!;
        public HBoxContainer CardStrip = null!;
        public string LastHandFingerprint = "";
    }
    private readonly Dictionary<ulong, PlayerRowState> _rowsByNetId = new();

    public void Start()
    {
        _tree = (SceneTree)Engine.GetMainLoop();
        Layer = LayerIndex;
        BuildShell();
        _tree.ProcessFrame += OnTick;
        GD.Print("[VR][RemoteHands] started");
    }

    public override void _ExitTree()
    {
        if (_tree != null) _tree.ProcessFrame -= OnTick;
    }

    private void BuildShell()
    {
        _root = new Control
        {
            AnchorLeft = 0, AnchorTop = 0, AnchorRight = 1, AnchorBottom = 0,
            OffsetLeft = 0, OffsetTop = TopMarginPx,
            OffsetRight = 0, OffsetBottom = 240,  // generous; rows pack inside
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(_root);

        _rows = new VBoxContainer
        {
            AnchorLeft = 0.5f, AnchorTop = 0,
            AnchorRight = 0.5f, AnchorBottom = 0,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.End,
        };
        _rows.AddThemeConstantOverride("separation", (int)RowSpacing);
        _root.AddChild(_rows);
        _root.Visible = false;
    }

    private void OnTick()
    {
        var nowSec = Time.GetTicksMsec() / 1000.0;
        if (nowSec < _nextRefreshSec) return;
        _nextRefreshSec = nowSec + RefreshIntervalSec;
        try { Refresh(); }
        catch (Exception ex) { GD.PrintErr($"[VR][RemoteHands] refresh: {ex.Message}"); }
    }

    private void Refresh()
    {
        var (state, players) = TryGetRunStatePlayers();
        if (state == null || players == null || players.Count == 0)
        {
            HideAll();
            return;
        }

        // Resolve local NetId once per session — used to skip self.
        _localNetIdCache ??= TryGetLocalNetId();
        var localId = _localNetIdCache;

        // Build the set of remote NetIds visible this tick.
        var seenNetIds = new HashSet<ulong>();
        foreach (var player in players)
        {
            if (player == null) continue;
            ulong nid;
            try
            {
                if (player.GetType().GetProperty("NetId")?.GetValue(player) is not ulong v) continue;
                nid = v;
            }
            catch { continue; }
            if (localId is ulong me && nid == me) continue;  // skip self
            seenNetIds.Add(nid);

            var pcs = ReadObj(player, "PlayerCombatState");
            if (pcs is null) continue;  // not in combat / no combat state yet
            var hand = ReadObj(pcs, "Hand");
            if (hand is null) continue;
            if (ReadObj(hand, "Cards") is not System.Collections.IEnumerable cardsEnum) continue;

            var cards = new List<CardModel>();
            foreach (var c in cardsEnum) if (c is CardModel cm) cards.Add(cm);

            UpdatePlayerRow(nid, player, cards);
        }

        // Drop rows for players no longer present (left game / dead / etc).
        var stale = _rowsByNetId.Keys.Where(k => !seenNetIds.Contains(k)).ToList();
        foreach (var k in stale)
        {
            if (_rowsByNetId.TryGetValue(k, out var st))
            {
                if (GodotObject.IsInstanceValid(st.Row)) st.Row.QueueFree();
                _rowsByNetId.Remove(k);
            }
        }

        // Show/hide root depending on whether we have any remote rows.
        if (_root != null) _root.Visible = _rowsByNetId.Count > 0;
    }

    private void HideAll()
    {
        if (_root != null) _root.Visible = false;
        // Keep the row state so we don't churn when combat resumes — but
        // free them if combat genuinely ended (no players at all).
        // Cheap heuristic: just leave them; Refresh's prune step handles it.
    }

    private void UpdatePlayerRow(ulong netId, object player, List<CardModel> cards)
    {
        var fingerprint = FingerprintHand(cards);

        if (!_rowsByNetId.TryGetValue(netId, out var st))
        {
            // First time seeing this player — build their row.
            var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
            row.AddThemeConstantOverride("separation", 12);

            var name = new Label { CustomMinimumSize = new Vector2(120, 0) };
            name.AddThemeColorOverride("font_color", StsTheme.Cream);
            name.AddThemeFontSizeOverride("font_size", StsTheme.FontBody);
            name.HorizontalAlignment = HorizontalAlignment.Right;

            var strip = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Begin };
            strip.AddThemeConstantOverride("separation", (int)CardSpacing);

            row.AddChild(name);
            row.AddChild(strip);
            _rows!.AddChild(row);

            st = new PlayerRowState { Row = row, NameLabel = name, CardStrip = strip };
            _rowsByNetId[netId] = st;
        }

        // Update player name (could change in some edge case; cheap to refresh).
        st.NameLabel.Text = ResolvePlayerDisplayName(netId, player) + "：";

        // Skip card-strip rebuild if hand hasn't changed.
        if (st.LastHandFingerprint == fingerprint) return;
        st.LastHandFingerprint = fingerprint;

        // Rebuild the card strip from scratch — simpler than diffing and
        // hands are small (≤10 cards typically).
        foreach (var child in st.CardStrip.GetChildren())
            child.QueueFree();

        foreach (var card in cards)
        {
            try
            {
                // Render an isolated CLONE of the CardModel — NCard appears to
                // bind one-to-one with its CardModel for description rendering,
                // so showing the same canonical instance twice (e.g. 3× Strike)
                // results in only the first NCard rendering correctly; the
                // others fall back to "If you can read this, there is a bug."
                // Deep-cloning via the game's own ToSerializable/FromSerializable
                // round-trip gives each NCard its own independent model.
                var renderModel = TryDeepCloneCardModel(card) ?? card;

                var nc = NCard.Create(renderModel, ModelVisibility.Visible);
                if (nc == null) continue;
                nc.Scale = new Vector2(CardScale, CardScale);
                var wrap = new Control
                {
                    CustomMinimumSize = new Vector2(180 * CardScale, 270 * CardScale),
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
                wrap.AddChild(nc);
                st.CardStrip.AddChild(wrap);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[VR][RemoteHands] NCard.Create failed: {ex.Message}");
            }
        }
    }

    private static CardModel? TryDeepCloneCardModel(CardModel src)
    {
        try
        {
            var t = src.GetType();
            // Prefer ToSerializable + FromSerializable for the strongest
            // independence — same path the game uses to load cards from save.
            var toSer = t.GetMethod("ToSerializable", BindingFlags.Public | BindingFlags.Instance);
            var ser = toSer?.Invoke(src, null);
            if (ser != null)
            {
                var fromSer = t.GetMethod("FromSerializable", BindingFlags.Public | BindingFlags.Static);
                var copy = fromSer?.Invoke(null, new[] { ser });
                if (copy is CardModel cm) return cm;
            }
        }
        catch (Exception ex) { GD.Print($"[VR][RemoteHands] deep clone (ser) failed: {ex.Message}"); }

        try
        {
            // Fallback: CreateClone — runs the in-game clone path, which
            // sets CloneOf and can fail outside an active run, but is worth
            // a try.
            var createClone = src.GetType().GetMethod("CreateClone", BindingFlags.Public | BindingFlags.Instance);
            if (createClone?.Invoke(src, null) is CardModel cm) return cm;
        }
        catch (Exception ex) { GD.Print($"[VR][RemoteHands] CreateClone failed: {ex.Message}"); }

        return null;
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private static string FingerprintHand(List<CardModel> cards)
    {
        // Identity = ID list joined. CardModel doesn't have a stable Id property
        // in our probe but its hash codes will differ across replays — for our
        // change-detection use case, hash codes are plenty.
        var sb = new System.Text.StringBuilder(cards.Count * 12);
        foreach (var c in cards) sb.Append(c.GetHashCode()).Append('|');
        return sb.ToString();
    }

    private static (object? state, System.Collections.IList? players) TryGetRunStatePlayers()
    {
        var rmType = Type.GetType("MegaCrit.Sts2.Core.Runs.RunManager, sts2");
        var rm = rmType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        if (rm == null) return (null, null);
        var stateProp = rmType?.GetProperty("State", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var state = stateProp?.GetValue(rm);
        if (state == null) return (null, null);
        var players = state.GetType().GetProperty("Players")?.GetValue(state) as System.Collections.IList;
        return (state, players);
    }

    private static ulong? TryGetLocalNetId()
    {
        try
        {
            var rmType = Type.GetType("MegaCrit.Sts2.Core.Runs.RunManager, sts2");
            var rm = rmType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var ns = rm?.GetType().GetProperty("NetService")?.GetValue(rm);
            if (ns?.GetType().GetProperty("NetId")?.GetValue(ns) is ulong id) return id;
        }
        catch { }
        return null;
    }

    private static string ResolvePlayerDisplayName(ulong netId, object playerObj)
    {
        // Try Player.Name first; fall back to platform-resolved name; then NetId.
        try
        {
            if (playerObj.GetType().GetProperty("Name")?.GetValue(playerObj) is string n && !string.IsNullOrWhiteSpace(n))
                return n;
        }
        catch { }
        try
        {
            var puType = Type.GetType("MegaCrit.Sts2.Core.Platform.PlatformUtil, sts2");
            var getName = puType?.GetMethod("GetPlayerName", BindingFlags.Public | BindingFlags.Static);
            // GetPlayerName(PlatformType, ulong)
            if (getName != null)
            {
                var platTypeT = Type.GetType("MegaCrit.Sts2.Core.Platform.PlatformType, sts2");
                if (platTypeT != null)
                {
                    var steam = Enum.Parse(platTypeT, "Steam");
                    var name = getName.Invoke(null, new object?[] { steam, netId }) as string;
                    if (!string.IsNullOrWhiteSpace(name)) return name!;
                }
            }
        }
        catch { }
        return $"P{netId & 0xFFFF}";
    }

    private static object? ReadObj(object obj, string propName)
    {
        try { return obj.GetType().GetProperty(propName)?.GetValue(obj); }
        catch { return null; }
    }
}
