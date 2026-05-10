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
        public Control Container = null!;
        public HBoxContainer CardRow = null!;
        public NMultiplayerPlayerState? PortraitNode;
        public ulong NetId;
        public string LastFingerprint = "";
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

        // Layout strategy C: horizontally CENTER each strip, stack strips
        // vertically from top down with enough margin to clear the top HUD.
        var viewportSize = _tree?.Root?.GetVisibleRect().Size ?? new Vector2(1920, 1080);
        const float TopMargin = 130f;       // clear the top HUD bar (deck/gear/etc)
        const float VerticalGap = 6f;

        // Stable order: sort strips by NetId so they don't reshuffle each
        // refresh as the dictionary's enumeration order changes.
        var ordered = _strips.Values
            .Where(s => s.PortraitNode != null && GodotObject.IsInstanceValid(s.PortraitNode))
            .OrderBy(s => s.NetId)
            .ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            var st = ordered[i];
            // Strip width = label + gap + num cards × (cardW + spacing).
            var n = st.Views.Count;
            if (n <= 0) { st.Container.Visible = false; continue; }
            st.Container.Visible = true;
            var stripWidth = n * (CardWidth + CardSpacing) - CardSpacing;

            var x = (viewportSize.X - stripWidth) * 0.5f;
            var y = TopMargin + i * (CardHeight + VerticalGap);
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
            if (localId is ulong me && nid == me) continue;
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
                // CRITICAL: add to scene tree FIRST, then SetCard. Until view
                // is in the tree, its child SubViewport isn't either, so any
                // NCard we add inside fails its IsNodeReady checks (_titleLabel
                // stays null, UpdateVisuals early-returns, descriptions stay
                // at the .tscn defaults — "Broken Card", "If you can read this").
                st.CardRow.AddChild(view);
                view.SetCard(card);
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

// Card view: instantiates card.tscn, adds to tree (so _Ready runs and
// fields populate), runs the bind sequence, then applies Control.Scale.
// NCard's true native size is 300×422 (not 180×270 — read from
// NCard.cctor IL). With ScaleFactor 0.187 we get a clean ~56×78 visual.
internal sealed partial class RemoteCardView : Control
{
    public const int   NativeW = 300;
    public const int   NativeH = 422;
    public const float ScaleFactor = 0.24f;
    public const float FootprintW = NativeW * ScaleFactor;   // 72
    public const float FootprintH = NativeH * ScaleFactor;   // ~101

    private NCard? _card;

    public RemoteCardView()
    {
        CustomMinimumSize = new Vector2(FootprintW, FootprintH);
        MouseFilter = MouseFilterEnum.Ignore;
        // No ClipContents — with the correct scale the visual fits inside
        // our footprint naturally. ClipContents was hiding parts because
        // we previously used the wrong native dimensions.
        ClipContents = false;
    }

    public void SetCard(CardModel card)
    {
        if (_card != null && GodotObject.IsInstanceValid(_card)) _card.QueueFree();
        _card = null;

        try
        {
            var packed = ResourceLoader.Load<PackedScene>("res://scenes/cards/card.tscn");
            if (packed == null)
            {
                GD.PrintErr("[VR][RemoteHands] card.tscn not loadable");
                return;
            }
            var nc = packed.Instantiate<NCard>();
            if (nc == null) return;

            AddChild(nc);   // _Ready fires here (wrapper already in tree)

            // Apply scale BEFORE binding model — we want the bind sequence
            // to operate on a stably-sized control. PivotOffset=(0,0)
            // anchors the scale at the top-left so the visible bounds
            // align with our footprint.
            nc.PivotOffset = Vector2.Zero;
            nc.Scale = new Vector2(ScaleFactor, ScaleFactor);

            // Mirror NCardHolder.ReassignToCard ordering.
            nc.Visibility = ModelVisibility.Visible;
            nc.Model = card;
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
            }
            catch (Exception ex) { GD.Print($"[VR][RemoteHands] UpdateVisuals: {ex.Message}"); }

            _card = nc;
        }
        catch (Exception ex) { GD.PrintErr($"[VR][RemoteHands] card instantiation fail: {ex.Message}"); }
    }
}
