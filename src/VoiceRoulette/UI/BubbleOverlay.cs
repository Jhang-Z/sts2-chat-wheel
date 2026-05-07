using Godot;
using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;
using VoiceRoulette.Net;

namespace VoiceRoulette.UI;

// Speech bubbles using the game's own NSpeechBubbleVfx component.
//
// Why: previously this class drew its own cream/parchment panel + tail Polygon,
// which never quite matched the game's look. NSpeechBubbleVfx (the same
// component used for in-game character barks) gives us the authentic StS2
// appearance for free — including the wavy bubble shape, the proper drop
// shadow, the player-color HSV tint, and the correct "Left/Right" tail
// orientation depending on which side of the screen the speaker is on.
//
// We just need to:
//   1. Find each player's screen position (from the scene tree as before)
//   2. Decide which side the speaker is on (Left/Right based on x position)
//   3. Pick a VfxColor per player slot
//   4. Call NSpeechBubbleVfx.Create(text, side, globalPos, seconds, color)
//   5. Track active bubbles so we can dismiss the previous one for a slot
//      when a new message comes in.
public sealed partial class BubbleOverlay : CanvasLayer
{
    private const float DisplaySeconds = 2.0f;

    private const string PlayerStateClassName = "NMultiplayerPlayerState";
    private const string CreatureClassName = "NCreature";
    private const float FallbackAnchorX = 220f;
    private const float FallbackAnchorY = 90f;
    private const float FallbackRowH = 38f;

    // Per-slot color tint applied to the bubble. Maps to VfxColor enum values
    // available in StsColors palette.
    private static readonly VfxColor[] SlotColors =
    {
        VfxColor.Cyan, VfxColor.Green, VfxColor.Orange, VfxColor.Purple,
    };

    private readonly Dictionary<byte, NSpeechBubbleVfx> _activePerSlot = new();
    private readonly Dictionary<int, Vector2> _playerRowPositions = new();
    private SceneTree? _tree;
    private bool _loggedAnchorOnce;

    public void StartPolling()
    {
        _tree = (SceneTree)Engine.GetMainLoop();
        Layer = 100;
        GD.Print("[VR][Bubble] StartPolling (using NSpeechBubbleVfx)");
    }

    public void Show(string text, byte senderSlot, bool hasVoice = false)
    {
        // hasVoice is no longer needed in the visual — the game's bubble has no
        // per-bubble speaker indicator. Keep the parameter for API compat.
        _ = hasVoice;
        if (string.IsNullOrEmpty(text) || _tree == null) return;

        // Dismiss the previous bubble for this slot, if any, so messages don't
        // pile on the same character.
        if (_activePerSlot.TryGetValue(senderSlot, out var existing) && IsValid(existing))
        {
            try { existing.QueueFree(); } catch { }
        }

        RefreshPlayerRowPositions();
        var anchor = ResolveAnchor(senderSlot);
        var viewport = _tree.Root.GetViewport().GetVisibleRect().Size;
        var side = anchor.X < viewport.X * 0.5f ? DialogueSide.Right : DialogueSide.Left;
        var color = SlotColors[senderSlot % SlotColors.Length];

        NSpeechBubbleVfx? bubble = null;
        try
        {
            bubble = NSpeechBubbleVfx.Create(text, side, anchor, DisplaySeconds, color);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[VR][Bubble] NSpeechBubbleVfx.Create failed: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        if (bubble == null)
        {
            GD.PrintErr("[VR][Bubble] NSpeechBubbleVfx.Create returned null");
            return;
        }

        // Diagnose: did Create add it to a scene tree? What's its position?
        GD.Print($"[VR][Bubble] post-Create: inTree={bubble.IsInsideTree()} parent={bubble.GetParent()?.GetType().Name ?? "null"} visible={bubble.Visible} globalPos={bubble.GlobalPosition} size={bubble.Size}");

        // If Create didn't auto-add to scene, attach to root so it renders.
        if (!bubble.IsInsideTree())
        {
            _tree.Root.AddChild(bubble);
            bubble.GlobalPosition = anchor;
            GD.Print($"[VR][Bubble] manually attached to root, set globalPos={anchor}");
        }

        _activePerSlot[senderSlot] = bubble;
        GD.Print($"[VR][Bubble] showing '{text}' for slot {senderSlot} side={side} color={color} at {anchor}");
    }

    private static bool IsValid(GodotObject obj)
    {
        return GodotObject.IsInstanceValid(obj);
    }

    // -------------------------------------------------------------------------
    // Anchor resolution (unchanged from the previous custom bubble)
    // -------------------------------------------------------------------------

    private Vector2 ResolveAnchor(byte slot)
    {
        // 1) Best: this slot's creature on the battlefield → bubble above its head.
        var creaturePos = PlayerSlotResolver.TryGetCreaturePositionForSlot(slot);
        if (creaturePos is Vector2 cpos)
            return cpos + new Vector2(0, -200);

        // 2) Fallback: this slot's UI portrait row (lobby, map, shop, etc.)
        var portraitPos = PlayerSlotResolver.TryGetPortraitPositionForSlot(slot);
        if (portraitPos is Vector2 ppos)
            return ppos;

        // 3) Last-resort: hardcoded layout for the top-left HUD column.
        if (_playerRowPositions.TryGetValue(slot, out var live)) return live;
        return new Vector2(FallbackAnchorX, FallbackAnchorY + slot * FallbackRowH);
    }

    private void RefreshPlayerRowPositions()
    {
        if (_tree?.Root == null) return;
        _playerRowPositions.Clear();

        // Heuristic position guess kept around as a final fallback when neither
        // the per-slot creature nor portrait can be found via the resolver
        // (e.g. very early in a session before MultiplayerPlayerStateContainer
        // is populated). Most scenes go through ResolveAnchor's resolver path.
        var rows = new List<(Vector2 pos, float width)>();
        var creatures = new List<Vector2>();
        WalkTree(_tree.Root, rows, creatures);

        var viewport = _tree.Root.GetViewport().GetVisibleRect().Size;

        if (creatures.Count > 0)
        {
            var leftCreatures = new List<Vector2>();
            foreach (var c in creatures)
                if (c.X > 50f && c.X < viewport.X * 0.55f) leftCreatures.Add(c);

            leftCreatures.Sort((a, b) => a.X.CompareTo(b.X));
            for (int i = 0; i < leftCreatures.Count && i < 4; i++)
                _playerRowPositions[i] = leftCreatures[i] + new Vector2(0, -200);

            if (leftCreatures.Count > 0)
            {
                LogOnce($"heuristic: {leftCreatures.Count} left creatures (used as last-resort)");
                return;
            }
        }

        if (rows.Count > 0)
        {
            rows.Sort((a, b) => a.pos.Y.CompareTo(b.pos.Y));
            for (int i = 0; i < rows.Count && i < 4; i++)
            {
                var (pos, width) = rows[i];
                _playerRowPositions[i] = new Vector2(pos.X + width + 8, pos.Y + 14);
            }
            LogOnce($"heuristic: {rows.Count} player rows (used as last-resort)");
            return;
        }

        var typicalPlayerHead = new Vector2(viewport.X * 0.22f, viewport.Y * 0.40f);
        for (int i = 0; i < 4; i++)
            _playerRowPositions[i] = typicalPlayerHead + new Vector2(i * 220f, 0);
        LogOnce($"heuristic: viewport fallback at {typicalPlayerHead}");
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
