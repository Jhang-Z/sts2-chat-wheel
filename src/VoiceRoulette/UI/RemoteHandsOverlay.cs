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
            // Anchor to the HP bar inside the player widget — the widget's
            // own right edge has empty padding that leaves a gap.
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
        // Hide the strip until we have a portrait to anchor against (only
        // multiplayer mode has NMultiplayerPlayerState widgets).
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

// Custom mini-card. NCard.Create has too many implicit dependencies on
// the game's hand-display pipeline (NCardHolder, NHandCardHolder, etc.)
// to render correctly outside of it, and Godot's SubViewport doesn't
// reliably isolate rendering when embedded in a Control hierarchy under
// a CanvasLayer. So we build a faithful-enough mini using the card's
// own Title/Type/Portrait/EnergyCost — readable at a glance, never
// shows "Broken Card" placeholders.
internal sealed partial class RemoteCardView : PanelContainer
{
    public const float FootprintW = 56f;
    public const float FootprintH = 78f;

    private TextureRect? _portrait;
    private Label? _title;
    private Label? _cost;
    private Label? _typeLabel;

    public RemoteCardView()
    {
        CustomMinimumSize = new Vector2(FootprintW, FootprintH);
        MouseFilter = MouseFilterEnum.Ignore;
        Build();
    }

    private void Build()
    {
        var sb = new StyleBoxFlat
        {
            BgColor = new Color("1B1612"),
            BorderColor = new Color("4D4B40"),
            BorderWidthLeft = 1, BorderWidthRight = 1,
            BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
            CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
        };
        AddThemeStyleboxOverride("panel", sb);

        var v = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        v.AddThemeConstantOverride("separation", 0);
        AddChild(v);

        // Header: cost orb + title
        var header = new Control { CustomMinimumSize = new Vector2(0, 13) };
        v.AddChild(header);

        _cost = new Label
        {
            Text = "?",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(13, 13),
            Position = new Vector2(1, 0),
        };
        _cost.AddThemeColorOverride("font_color", new Color("FFFFFF"));
        _cost.AddThemeColorOverride("font_outline_color", new Color("000000"));
        _cost.AddThemeConstantOverride("outline_size", 3);
        _cost.AddThemeFontSizeOverride("font_size", 9);
        header.AddChild(_cost);

        _title = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ClipText = true,
            Position = new Vector2(14, 0),
            Size = new Vector2(FootprintW - 16, 13),
        };
        _title.AddThemeColorOverride("font_color", StsTheme.Cream);
        _title.AddThemeFontSizeOverride("font_size", 8);
        header.AddChild(_title);

        // Portrait — the actual card art. Most cards are visually
        // distinguishable by art alone.
        _portrait = new TextureRect
        {
            CustomMinimumSize = new Vector2(FootprintW - 4, 50),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        var portraitWrap = new MarginContainer();
        portraitWrap.AddThemeConstantOverride("margin_left", 2);
        portraitWrap.AddThemeConstantOverride("margin_right", 2);
        portraitWrap.AddChild(_portrait);
        v.AddChild(portraitWrap);

        // Type label (color-coded border + tag)
        _typeLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(0, 12),
        };
        _typeLabel.AddThemeColorOverride("font_color", StsTheme.Cream);
        _typeLabel.AddThemeFontSizeOverride("font_size", 8);
        v.AddChild(_typeLabel);
    }

    public void SetCard(CardModel card)
    {
        if (_title == null || _cost == null || _portrait == null || _typeLabel == null) return;

        _title.Text = card.Title ?? "?";

        int cost = 0;
        bool costsX = false;
        try
        {
            var ec = card.EnergyCost;
            if (ec != null) { cost = ec.GetResolved(); costsX = ec.CostsX; }
        }
        catch { }
        _cost.Text = costsX ? "X" : cost.ToString();

        try { _portrait.Texture = card.Portrait; } catch { _portrait.Texture = null; }

        var (typeText, typeColor) = card.Type switch
        {
            CardType.Attack => ("攻击",  new Color("A4332C")),
            CardType.Skill  => ("技能",  new Color("4F6FAA")),
            CardType.Power  => ("能力",  new Color("4F8E5C")),
            CardType.Status => ("状态",  new Color("8A7E62")),
            CardType.Curse  => ("诅咒",  new Color("550B9E")),
            CardType.Quest  => ("任务",  new Color("7E3E15")),
            _               => ("",       new Color("4D4B40")),
        };
        _typeLabel.Text = typeText;

        if (GetThemeStylebox("panel") is StyleBoxFlat sbf)
        {
            sbf.BorderColor = typeColor;
            sbf.BorderWidthTop = 3;
        }
    }
}
