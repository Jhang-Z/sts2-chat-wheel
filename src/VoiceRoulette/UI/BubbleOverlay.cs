using Godot;
using System;
using System.Collections.Generic;

namespace VoiceRoulette.UI;

// Floating speech bubbles that anchor to multiplayer player rows.
// Each frame we walk the scene tree to find NMultiplayerPlayerState nodes
// (STS2's per-player upper-left rows) and anchor each player's bubble there.
// Falls back to static positions if those nodes aren't in the tree
// (singleplayer / out-of-combat / lobby).
public sealed partial class BubbleOverlay : CanvasLayer
{
    private const float DisplaySeconds = 3.0f;
    private const float FadeSeconds = 0.5f;
    private const float BubblePaddingX = 18f;
    private const float BubblePaddingY = 10f;
    private const float BubbleMinWidth = 140f;

    // Class names probed from sts2.dll. Match by short type name to be patch-resilient.
    private const string PlayerStateClassName = "NMultiplayerPlayerState";
    private const string CreatureClassName = "NCreature";

    // Static fallback layout when no player nodes are found (lobby / pre-combat).
    private const float FallbackAnchorX = 220f;
    private const float FallbackAnchorY = 90f;
    private const float FallbackRowH = 38f;

    // Per-slot horizontal stagger so bubbles don't pile on top of each other
    // even if rows happen to be very close vertically.
    private static readonly float[] HorizontalStagger = { 0f, 30f, 60f, 90f };

    // Per-slot border colors (also used for connector triangle)
    private static readonly Color[] SlotBorders =
    {
        new("4ea3ff"), new("5fcf7c"), new("f0a235"), new("c97df0"),
    };

    private static readonly Color Parchment = new("e8d4a8");      // mockup parchment
    private static readonly Color WoodFrame = new("3a2818");      // dark brown wood
    private static readonly Color Ink = new("2a1d10");
    private static readonly Color SpeakerIconBg = new("d4a937");

    private readonly Dictionary<byte, ActiveBubble> _bubblesPerSlot = new();
    private SceneTree? _tree;

    // Cache of resolved player-row world positions, refreshed each frame.
    private readonly Dictionary<int, Vector2> _playerRowPositions = new();

    private sealed class ActiveBubble
    {
        public required Panel Panel;
        public required Label Text;
        public required Polygon2D Tail;
        public required Vector2 Size;
        public byte Slot;
        public double SpawnTime;
    }

    public void StartPolling()
    {
        _tree = (SceneTree)Engine.GetMainLoop();
        _tree.ProcessFrame += OnTick;
        Layer = 100;
        GD.Print("[VR][Bubble] StartPolling");
    }

    public void Show(string text, byte senderSlot)
    {
        if (string.IsNullOrEmpty(text)) return;

        // Refresh anchor BEFORE building the bubble. Otherwise the first frame
        // uses the empty _playerRowPositions map → static top-left fallback →
        // bubble flickers in the corner before OnTick relocates it next frame.
        RefreshPlayerRowPositions();

        // Replace existing bubble for this slot.
        if (_bubblesPerSlot.TryGetValue(senderSlot, out var existing))
        {
            existing.Panel.QueueFree();
            existing.Tail.QueueFree();
        }

        var bubble = BuildBubble(text, senderSlot);
        // Apply anchor immediately so the panel is born at the right place.
        var anchor = ResolveAnchor(senderSlot);
        bubble.Panel.Position = anchor + new Vector2(14, -bubble.Size.Y / 2f);
        bubble.Tail.Position = anchor;

        AddChild(bubble.Panel);
        AddChild(bubble.Tail);
        _bubblesPerSlot[senderSlot] = bubble;
        GD.Print($"[VR][Bubble] showing '{text}' for slot {senderSlot} at {anchor}");
    }

    private ActiveBubble BuildBubble(string text, byte senderSlot)
    {
        // Per-slot accent color shown as a small ribbon in the corner only;
        // the bubble itself uses the unified mockup palette (parchment + wood).
        var accent = SlotBorders[senderSlot % SlotBorders.Length];

        var label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.AddThemeColorOverride("font_color", Ink);
        label.AddThemeFontSizeOverride("font_size", 18);

        // Reserve room on the right for a speaker badge.
        const float speakerBadgeWidth = 28f;
        var width = MathF.Max(BubbleMinWidth, text.Length * 18f + BubblePaddingX * 2 + speakerBadgeWidth);
        var height = 30f + BubblePaddingY * 2;
        label.Size = new Vector2(width - BubblePaddingX * 2 - speakerBadgeWidth, height - BubblePaddingY * 2);
        label.Position = new Vector2(BubblePaddingX, BubblePaddingY);

        var panel = new Panel { Size = new Vector2(width, height) };
        var style = new StyleBoxFlat
        {
            BgColor = Parchment,
            BorderColor = WoodFrame,
            CornerRadiusBottomLeft = 10,
            CornerRadiusBottomRight = 10,
            CornerRadiusTopLeft = 10,
            CornerRadiusTopRight = 10,
            BorderWidthLeft = 3,
            BorderWidthRight = 3,
            BorderWidthTop = 3,
            BorderWidthBottom = 3,
            ShadowColor = new Color(0, 0, 0, 0.55f),
            ShadowSize = 6,
            ShadowOffset = new Vector2(0, 3),
        };
        panel.AddThemeStyleboxOverride("panel", style);
        panel.AddChild(label);

        // Slot-color accent ribbon along the top border (subtle player identity)
        var ribbon = new ColorRect
        {
            Color = accent,
            Size = new Vector2(width - 14, 3),
            Position = new Vector2(7, 4),
        };
        panel.AddChild(ribbon);

        // Speaker icon badge inside bubble on the right (matches mockup #2)
        var speaker = new Label
        {
            Text = "🔊",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Size = new Vector2(speakerBadgeWidth, height - 12),
            Position = new Vector2(width - speakerBadgeWidth - 6, 6),
        };
        speaker.AddThemeColorOverride("font_color", SpeakerIconBg);
        speaker.AddThemeFontSizeOverride("font_size", 16);
        panel.AddChild(speaker);

        // Down-pointing tail (parchment color matches bubble fill)
        var tail = new Polygon2D
        {
            Color = Parchment,
            Polygon = new Vector2[]
            {
                new(-9, -1), new(9, -1), new(0, 12)
            },
        };

        return new ActiveBubble
        {
            Panel = panel,
            Text = label,
            Tail = tail,
            Size = new Vector2(width, height),
            Slot = senderSlot,
            SpawnTime = Time.GetTicksMsec() / 1000.0,
        };
    }

    private void OnTick()
    {
        if (_tree == null) return;

        var now = Time.GetTicksMsec() / 1000.0;

        // 1. Refresh player row anchors via scene-tree walk (cheap; only runs while bubbles active).
        if (_bubblesPerSlot.Count > 0)
            RefreshPlayerRowPositions();

        // 2. Update each active bubble: position + alpha; remove expired.
        var toRemove = new List<byte>();
        foreach (var kv in _bubblesPerSlot)
        {
            var b = kv.Value;
            var age = now - b.SpawnTime;
            if (age >= DisplaySeconds)
            {
                b.Panel.QueueFree();
                b.Tail.QueueFree();
                toRemove.Add(kv.Key);
                continue;
            }

            var anchor = ResolveAnchor(b.Slot);
            b.Panel.Position = anchor + new Vector2(14, -b.Size.Y / 2f);
            b.Tail.Position = anchor;

            float alpha = 1f;
            var fadeStart = DisplaySeconds - FadeSeconds;
            if (age >= fadeStart)
                alpha = 1f - (float)((age - fadeStart) / FadeSeconds);
            b.Panel.Modulate = new Color(1, 1, 1, alpha);
            b.Tail.Modulate = new Color(1, 1, 1, alpha);
        }

        foreach (var slot in toRemove)
            _bubblesPerSlot.Remove(slot);
    }

    private Vector2 ResolveAnchor(byte slot)
    {
        var stagger = HorizontalStagger[slot % HorizontalStagger.Length];

        if (_playerRowPositions.TryGetValue(slot, out var live))
        {
            // Right edge of the player row + stagger to avoid overlap.
            return new Vector2(live.X + stagger, live.Y);
        }

        // Fallback: static stack anchored to upper-left.
        return new Vector2(
            FallbackAnchorX + stagger,
            FallbackAnchorY + slot * FallbackRowH);
    }

    /// <summary>
    /// Walk the scene tree once per tick (only when bubbles are visible),
    /// find all nodes whose type short-name == "NMultiplayerPlayerState",
    /// sort them top-to-bottom, and assign positions to slots 0..N.
    /// </summary>
    private bool _loggedAnchorOnce;

    private void RefreshPlayerRowPositions()
    {
        if (_tree?.Root == null) return;
        _playerRowPositions.Clear();

        var rows = new List<(Vector2 pos, float width)>();
        var creatures = new List<Vector2>();
        WalkTree(_tree.Root, rows, creatures);

        var viewport = _tree.Root.GetViewport().GetVisibleRect().Size;

        // 1. Multiplayer rows
        if (rows.Count > 0)
        {
            rows.Sort((a, b) => a.pos.Y.CompareTo(b.pos.Y));
            for (int i = 0; i < rows.Count && i < 4; i++)
            {
                var (pos, width) = rows[i];
                _playerRowPositions[i] = new Vector2(pos.X + width + 8, pos.Y + 14);
            }
            LogOnce($"using player rows: {rows.Count} found");
            return;
        }

        // 2. NCreature on left half of screen
        if (creatures.Count > 0)
        {
            var leftCreatures = new List<Vector2>();
            foreach (var c in creatures)
                if (c.X > 50f && c.X < viewport.X * 0.55f) leftCreatures.Add(c);

            leftCreatures.Sort((a, b) => a.X.CompareTo(b.X));
            for (int i = 0; i < leftCreatures.Count && i < 4; i++)
            {
                // Anchor above the creature sprite — STS2 sprites are ~250px tall,
                // origin is feet, so we go up ~200px to land near the head.
                _playerRowPositions[i] = leftCreatures[i] + new Vector2(0, -200);
            }
            if (leftCreatures.Count > 0)
            {
                LogOnce($"using creature anchor: {leftCreatures.Count} on left, total found {creatures.Count}");
                return;
            }
        }

        // 3. Tertiary fallback: hardcoded above typical player sprite area
        // STS2 player character sprite typically renders around (vx*0.22, vy*0.55).
        var typicalPlayerHead = new Vector2(viewport.X * 0.22f, viewport.Y * 0.40f);
        for (int i = 0; i < 4; i++)
            _playerRowPositions[i] = typicalPlayerHead + new Vector2(i * 220f, 0);
        LogOnce($"using viewport fallback at {typicalPlayerHead} (rows={rows.Count}, creatures={creatures.Count})");
    }

    private void LogOnce(string msg)
    {
        if (_loggedAnchorOnce) return;
        _loggedAnchorOnce = true;
        GD.Print($"[VR][Bubble] anchor: {msg}");
    }

    private static void WalkTree(Node n, List<(Vector2, float)> rows, List<Vector2> creatures)
    {
        var typeName = n.GetType().Name;

        if (typeName == PlayerStateClassName)
        {
            if (n is Node2D n2d) rows.Add((n2d.GlobalPosition, 100f));
            else if (n is Control ctrl) rows.Add((ctrl.GlobalPosition, ctrl.Size.X));
            return;
        }

        // Match anything Creature-like (NCreature, NCreatureVisuals, etc.)
        // and only count it if it is currently visible.
        if (typeName.Contains(CreatureClassName))
        {
            if (n is Node2D n2d && n2d.IsVisibleInTree())
                creatures.Add(n2d.GlobalPosition);
            else if (n is Control ctrl && ctrl.IsVisibleInTree())
                creatures.Add(ctrl.GlobalPosition + ctrl.Size / 2f);
        }

        var children = n.GetChildren();
        for (int i = 0; i < children.Count; i++)
            WalkTree(children[i], rows, creatures);
    }
}
