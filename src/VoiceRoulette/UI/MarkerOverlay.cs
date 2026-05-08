using Godot;

namespace VoiceRoulette.UI;

/// <summary>
/// "集火" focus marker — when a target is designated, four arrows fly in
/// from four diagonal directions toward the target, slow to a stop just
/// outside it, then fade. The convergence motion visually says "all of
/// us point at THIS one".
///
/// Lifetime per click:
///   • 0.0 – 0.35s : arrows fly inward from outer ring → inner ring
///   • 0.35 – 1.0s : arrows hold position
///   • 1.0 – 1.4s  : arrows fade out
/// Total ~1.4s, then nodes are queue-freed.
/// </summary>
public sealed partial class MarkerOverlay : CanvasLayer
{
    // Animation timings
    private const float FlyInDuration  = 0.35f;
    private const float HoldDuration   = 0.65f;
    private const float FadeOutDuration = 0.4f;

    // Geometry
    private const float OuterRadius = 240f;   // where each arrow spawns from
    private const float InnerRadius = 95f;    // where each arrow stops, just outside the body
    private const float ArrowLength = 44f;    // tip-to-tail
    private const float ArrowHalfWidth = 14f;

    private static readonly Color FillColor    = StsTheme.MenuRedAccent;  // attacking-style red
    private static readonly Color OutlineColor = new("00000099");

    // Diagonal angles (radians, screen space — +Y is down).
    // Upper-right, lower-right, lower-left, upper-left.
    private static readonly float[] Angles =
    {
        -Mathf.Pi / 4f,
         Mathf.Pi / 4f,
         3f * Mathf.Pi / 4f,
        -3f * Mathf.Pi / 4f,
    };

    private SceneTree? _tree;

    public void Start()
    {
        _tree = (SceneTree)Engine.GetMainLoop();
        Layer = 110;
    }

    /// <summary>Place a converge-arrows marker centered at the given world point.</summary>
    public void Show(Vector2 target)
    {
        if (_tree == null) return;
        foreach (var angle in Angles) SpawnArrow(target, angle);
        GD.Print($"[VR][Marker] focus-fire converge at {target}");
    }

    private void SpawnArrow(Vector2 target, float angle)
    {
        var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        var spawnPos = target + dir * OuterRadius;
        var landPos  = target + dir * InnerRadius;

        // Arrow points toward the target — rotation aligns the polygon's
        // natural -Y (tip-up) axis with the inward direction (-dir).
        // Inward direction angle = angle + π. Rotation needed for a tip-up
        // polygon to face that direction = (angle + π) + π/2.
        var rotation = angle + Mathf.Pi * 1.5f;

        var fill = new Polygon2D
        {
            Polygon = new[]
            {
                new Vector2(0, -ArrowLength * 0.5f),                  // tip
                new Vector2(-ArrowHalfWidth, ArrowLength * 0.5f),    // tail-left
                new Vector2(ArrowHalfWidth, ArrowLength * 0.5f),    // tail-right
            },
            Color = FillColor,
            Position = spawnPos,
            Rotation = rotation,
            ZIndex = 100,
        };

        var outline = new Line2D
        {
            DefaultColor = OutlineColor,
            Width = 2.5f,
            Antialiased = true,
            Closed = true,
            Position = spawnPos,
            Rotation = rotation,
            ZIndex = 99,
        };
        outline.AddPoint(new Vector2(0, -ArrowLength * 0.5f));
        outline.AddPoint(new Vector2(-ArrowHalfWidth, ArrowLength * 0.5f));
        outline.AddPoint(new Vector2(ArrowHalfWidth, ArrowLength * 0.5f));

        AddChild(outline);
        AddChild(fill);

        // Single tween chain: fly-in → hold → fade-out, on the fill node.
        // Sync the outline by tweening it in parallel branches.
        var t = fill.CreateTween();
        // Phase 1: fly in (ease-out cubic gives a punchy slowdown).
        t.TweenProperty(fill, "position", landPos, FlyInDuration)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        t.Parallel().TweenProperty(outline, "position", landPos, FlyInDuration)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);

        // Phase 2: hold (just delay).
        t.TweenInterval(HoldDuration);

        // Phase 3: fade out.
        t.TweenProperty(fill, "modulate:a", 0f, FadeOutDuration);
        t.Parallel().TweenProperty(outline, "modulate:a", 0f, FadeOutDuration);

        // Hard cleanup safety net.
        var totalLife = FlyInDuration + HoldDuration + FadeOutDuration + 0.1f;
        var timer = _tree!.CreateTimer(totalLife);
        timer.Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(fill))    fill.QueueFree();
            if (GodotObject.IsInstanceValid(outline)) outline.QueueFree();
        };
    }
}
