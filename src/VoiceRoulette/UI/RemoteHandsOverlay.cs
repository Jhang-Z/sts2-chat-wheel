// Live overlay showing each remote (teammate) player's current hand of
// cards next to their player-portrait widget on the left side of the
// screen during combat. Each card is a custom-rendered mini-card built
// directly from CardModel data (cost / type / portrait / title) — we
// deliberately avoid NCard.Create because NCard goes through a NodePool
// that's tightly coupled to the game's own hand-display lifecycle, so
// rendering multiple cards from the same pool outside the game's
// expected use causes broken descriptions ("If you can read this, there
// is a bug.") for all but the first card.
//
// Layout: each remote player's strip is anchored to the right edge of
// their NMultiplayerPlayerState widget. Updated every frame so the
// strip follows the portrait as the player-state container animates.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;

namespace VoiceRoulette.UI;

public sealed partial class RemoteHandsOverlay : CanvasLayer
{
    private const int LayerIndex = 90;
    // Cards are full NCard renders scaled down — the constants here describe
    // the scaled footprint we reserve in the layout.
    private const float CardWidth = RemoteCardView.FootprintW;
    private const float CardHeight = RemoteCardView.FootprintH;
    private const float CardSpacing = 2f;
    private const float StripGapFromPortrait = 6f;
    private const double RefreshIntervalSec = 0.25;

    private SceneTree? _tree;
    private Control? _root;
    private double _nextRefreshSec;
    private ulong? _localNetIdCache;

    private sealed class PlayerStrip
    {
        public Control Container = null!;             // anchored next to the player widget
        public HBoxContainer CardRow = null!;
        public NMultiplayerPlayerState? PortraitNode; // tracked widget
        public ulong NetId;
        public string LastFingerprint = "";
        // Per-card view nodes for animation (subtle bob).
        public List<RemoteCardView> Views = new();
    }

    private readonly Dictionary<ulong, PlayerStrip> _strips = new();

    public void Start()
    {
        _tree = (SceneTree)Engine.GetMainLoop();
        Layer = LayerIndex;
        _root = new Control
        {
            AnchorLeft = 0, AnchorTop = 0, AnchorRight = 1, AnchorBottom = 1,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(_root);
        _tree.ProcessFrame += OnTick;
        GD.Print("[VR][RemoteHands] started");
    }

    public override void _ExitTree()
    {
        if (_tree != null) _tree.ProcessFrame -= OnTick;
    }

    private void OnTick()
    {
        // Position update every frame so cards follow portrait animations.
        UpdateStripPositions();

        var nowSec = Time.GetTicksMsec() / 1000.0;
        if (nowSec < _nextRefreshSec) return;
        _nextRefreshSec = nowSec + RefreshIntervalSec;
        try { Refresh(); } catch (Exception ex) { GD.PrintErr($"[VR][RemoteHands] refresh: {ex.Message}"); }
    }

    private void UpdateStripPositions()
    {
        if (_root == null) return;
        foreach (var st in _strips.Values)
        {
            if (st.PortraitNode == null || !GodotObject.IsInstanceValid(st.PortraitNode))
                continue;
            // Anchor to the HP bar inside the player widget, not the widget
            // itself — the widget extends well past the visible HP bar with
            // empty right padding, leaving an unwanted gap.
            var anchorRect = TryGetHealthBarRect(st.PortraitNode) ?? st.PortraitNode.GetGlobalRect();
            var x = anchorRect.Position.X + anchorRect.Size.X + StripGapFromPortrait;
            var y = anchorRect.Position.Y + anchorRect.Size.Y * 0.5f - CardHeight * 0.5f;
            st.Container.Position = new Vector2(x, y);
        }
    }

    private static Rect2? TryGetHealthBarRect(NMultiplayerPlayerState widget)
    {
        try
        {
            var hb = widget.GetType().GetField("_healthBar",
                BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(widget) as Control;
            if (hb == null || !GodotObject.IsInstanceValid(hb)) return null;

            // Prefer NHealthBar.HpBarContainer (the actual visible bar) — the
            // NHealthBar root is often anchor-stretched to a wider rect than
            // what the user sees, leaving cards positioned far too right.
            var prop = hb.GetType().GetProperty("HpBarContainer");
            if (prop?.GetValue(hb) is Control c && GodotObject.IsInstanceValid(c))
                return c.GetGlobalRect();
            return hb.GetGlobalRect();
        }
        catch { }
        return null;
    }

    private void Refresh()
    {
        var (state, players) = TryGetRunStatePlayers();
        if (state == null || players == null || players.Count == 0)
        {
            HideAll();
            return;
        }

        _localNetIdCache ??= TryGetLocalNetId();
        var localId = _localNetIdCache;

        // Index portraits by Player.NetId so we can pair each remote player's
        // hand with its on-screen widget.
        var portraitByNetId = IndexPortraitsByNetId();

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
            // Note: local player INTENTIONALLY included for visual testing.
            // Toggle this back to skip-self when satisfied with layout:
            //   if (localId is ulong me && nid == me) continue;
            seenNetIds.Add(nid);

            var pcs = ReadObj(player, "PlayerCombatState");
            if (pcs is null) continue;
            var hand = ReadObj(pcs, "Hand");
            if (hand is null) continue;
            if (ReadObj(hand, "Cards") is not System.Collections.IEnumerable cardsEnum) continue;

            var cards = new List<CardModel>();
            foreach (var c in cardsEnum) if (c is CardModel cm) cards.Add(cm);

            UpdateStrip(nid, cards, portraitByNetId);
        }

        // Drop strips for players no longer present.
        var stale = _strips.Keys.Where(k => !seenNetIds.Contains(k)).ToList();
        foreach (var k in stale) RemoveStrip(k);
    }

    private void HideAll()
    {
        foreach (var k in _strips.Keys.ToList()) RemoveStrip(k);
    }

    private void RemoveStrip(ulong netId)
    {
        if (!_strips.TryGetValue(netId, out var st)) return;
        if (GodotObject.IsInstanceValid(st.Container)) st.Container.QueueFree();
        _strips.Remove(netId);
    }

    private void UpdateStrip(ulong netId, List<CardModel> cards, Dictionary<ulong, NMultiplayerPlayerState> portraitByNetId)
    {
        if (!_strips.TryGetValue(netId, out var st))
        {
            st = new PlayerStrip { NetId = netId };
            st.Container = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
            st.CardRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            st.CardRow.AddThemeConstantOverride("separation", (int)CardSpacing);
            st.Container.AddChild(st.CardRow);
            _root!.AddChild(st.Container);
            _strips[netId] = st;
        }

        portraitByNetId.TryGetValue(netId, out var portrait);
        st.PortraitNode = portrait;
        // Hide the strip until we have a portrait to anchor against.
        st.Container.Visible = portrait != null;

        var fingerprint = FingerprintHand(cards);
        if (st.LastFingerprint == fingerprint) return;
        st.LastFingerprint = fingerprint;

        // Rebuild card row from scratch — cheap; hands are small.
        foreach (var child in st.CardRow.GetChildren()) child.QueueFree();
        st.Views.Clear();

        foreach (var card in cards)
        {
            try
            {
                var view = new RemoteCardView();
                view.SetCard(card);
                st.CardRow.AddChild(view);
                st.Views.Add(view);
            }
            catch (Exception ex) { GD.PrintErr($"[VR][RemoteHands] card view fail: {ex.Message}"); }
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static string FingerprintHand(List<CardModel> cards)
    {
        var sb = new System.Text.StringBuilder(cards.Count * 16);
        foreach (var c in cards)
        {
            // Use Title as the identity key plus instance hash for upgrades/dupes.
            sb.Append(c.Title).Append(':').Append(c.GetHashCode()).Append('|');
        }
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

    private Dictionary<ulong, NMultiplayerPlayerState> IndexPortraitsByNetId()
    {
        var dict = new Dictionary<ulong, NMultiplayerPlayerState>();
        try
        {
            Walk(_tree!.Root);
        }
        catch (Exception ex) { GD.Print($"[VR][RemoteHands] portrait scan: {ex.Message}"); }
        return dict;

        void Walk(Node n)
        {
            if (n is NMultiplayerPlayerState state)
            {
                var player = state.Player;
                if (player != null)
                {
                    try
                    {
                        if (player.GetType().GetProperty("NetId")?.GetValue(player) is ulong nid)
                            dict[nid] = state;
                    }
                    catch { }
                }
            }
            foreach (var c in n.GetChildren()) Walk(c);
        }
    }

    private static object? ReadObj(object obj, string propName)
    {
        try { return obj.GetType().GetProperty(propName)?.GetValue(obj); }
        catch { return null; }
    }
}

// Card view that uses the game's own NCard scene rendered into a SubViewport,
// then displayed in a TextureRect at our desired smaller size. This achieves
// faithful card rendering (cost / art / name / type / description with
// keyword highlights) at any scale — Control.Scale on NCard directly
// doesn't work because NCard's anchor-stretched children ignore it.
internal sealed partial class RemoteCardView : Control
{
    public const float FootprintW = 84f;
    public const float FootprintH = 126f;
    public const int   NativeW = 180;
    public const int   NativeH = 270;

    private SubViewport? _viewport;
    private TextureRect? _display;
    private NCard? _card;

    public RemoteCardView()
    {
        CustomMinimumSize = new Vector2(FootprintW, FootprintH);
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = false;
        Build();
    }

    private void Build()
    {
        // Off-screen viewport — renders NCard at native resolution.
        _viewport = new SubViewport
        {
            Size = new Vector2I(NativeW, NativeH),
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            HandleInputLocally = false,
        };
        AddChild(_viewport);

        // Display the viewport's render at our footprint size — automatic
        // scaling. ViewportTexture stays live; updates as NCard repaints.
        _display = new TextureRect
        {
            Texture = _viewport.GetTexture(),
            CustomMinimumSize = new Vector2(FootprintW, FootprintH),
            Size = new Vector2(FootprintW, FootprintH),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_display);
    }

    public void SetCard(CardModel card)
    {
        if (_card != null && GodotObject.IsInstanceValid(_card)) _card.QueueFree();
        _card = null;
        if (_viewport == null) return;

        try
        {
            // Bypass NodePool: instantiate card.tscn fresh. Each NCard is
            // a clean instance whose _Ready fires the moment we add it to
            // our SubViewport. This avoids the pool's stale parent / stale
            // model state issues entirely.
            var packed = ResourceLoader.Load<PackedScene>("res://scenes/cards/card.tscn");
            if (packed == null)
            {
                GD.PrintErr("[VR][RemoteHands] card.tscn not loadable");
                return;
            }
            var nc = packed.Instantiate<NCard>();
            if (nc == null) return;

            GD.Print($"[VR][DBG] card.Title='{card.Title}' card.Type={card.Type} hasModel={(nc.Model!=null)} pre-add parent={nc.GetParent()?.Name ?? "null"}");
            _viewport.AddChild(nc);   // _Ready fires here (fresh instance)
            GD.Print($"[VR][DBG] post-AddChild parent={nc.GetParent()?.Name ?? "null"} insideTree={nc.IsInsideTree()} ready={nc.IsNodeReady()}");

            // Now go through the full bind sequence in the same order
            // NCardHolder.ReassignToCard uses.
            nc.Visibility = ModelVisibility.Visible;
            nc.Model = card;
            GD.Print($"[VR][DBG] post-set Model: nc.Model.Title='{nc.Model?.Title ?? "null"}' visibility={nc.Visibility}");
            try
            {
                var owner = card.GetType().GetProperty("Owner")?.GetValue(card);
                var creature = owner?.GetType().GetProperty("Creature")?.GetValue(owner);
                nc.SetPreviewTarget(creature as MegaCrit.Sts2.Core.Entities.Creatures.Creature);
            }
            catch (Exception ex) { GD.Print($"[VR][RemoteHands] SetPreviewTarget: {ex.Message}"); }

            try
            {
                nc.UpdateVisuals(MegaCrit.Sts2.Core.Entities.Cards.PileType.Hand,
                                 MegaCrit.Sts2.Core.Entities.Cards.CardPreviewMode.Normal);
                // Read back the actual rendered text via reflection
                var f = typeof(NCard).GetField("_titleLabel", BindingFlags.NonPublic | BindingFlags.Instance);
                var titleLabel = f?.GetValue(nc) as Godot.Label;
                var descField = typeof(NCard).GetField("_descriptionLabel", BindingFlags.NonPublic | BindingFlags.Instance);
                var descLabel = descField?.GetValue(nc);
                var descTextProp = descLabel?.GetType().GetProperty("Text");
                var descText = descTextProp?.GetValue(descLabel) as string;
                GD.Print($"[VR][DBG] post-UpdateVisuals: titleLabel.Text='{titleLabel?.Text ?? "null"}' descText='{(descText?.Length>30?descText[..30]+"...":descText)}'");
                GD.Print($"[VR][DBG]   nc.GlobalPosition={nc.GlobalPosition} parent.GlobalPosition={(nc.GetParent() as Control)?.GlobalPosition}");
            }
            catch (Exception ex) { GD.Print($"[VR][RemoteHands] UpdateVisuals: {ex.Message}"); }

            _card = nc;
        }
        catch (Exception ex) { GD.PrintErr($"[VR][RemoteHands] card instantiation fail: {ex.Message}\n{ex.StackTrace}"); }
    }
}
