using Godot;

namespace VoiceRoulette.UI;

/// <summary>
/// Renders a transient down-pointing arrow at a world position when someone
/// places a tactical "look here" marker. Each marker self-cleans via a
/// SceneTree timer + Tween — no per-frame _Process polling (which had a
/// reliability issue: previous implementation accumulated stale arrows).
/// </summary>
public sealed partial class MarkerOverlay : CanvasLayer
{
    private const float DisplaySeconds = 2.0f;
    private const float ArrowOffsetY   = 70f;
    private const float ArrowSize      = 36f;

    private static readonly Color FillColor    = StsTheme.MenuAccent;
    private static readonly Color OutlineColor = new("00000099");

    private SceneTree? _tree;

    public void Start()
    {
        _tree = (SceneTree)Engine.GetMainLoop();
        Layer = 110;
    }

    /// <summary>Place a marker at the given world-space point.</summary>
    public void Show(Vector2 worldPos)
    {
        if (_tree == null) return;

        var anchor = worldPos - new Vector2(0, ArrowOffsetY);

        // Down-pointing triangle, tip toward the target.
        var fill = new Polygon2D
        {
            Polygon = new[]
            {
                new Vector2(0, ArrowSize),
                new Vector2(-ArrowSize * 0.6f, 0),
                new Vector2(ArrowSize * 0.6f, 0),
            },
            Color = FillColor,
            Position = anchor,
        };

        var outline = new Line2D
        {
            DefaultColor = OutlineColor,
            Width = 3f,
            Antialiased = true,
            Closed = true,
        };
        outline.AddPoint(new Vector2(0, ArrowSize));
        outline.AddPoint(new Vector2(-ArrowSize * 0.6f, 0));
        outline.AddPoint(new Vector2(ArrowSize * 0.6f, 0));
        outline.Position = anchor;

        AddChild(outline);
        AddChild(fill);

        // Pulse + fade out via Tween — runs on the SceneTree's tween scheduler,
        // independent of any _Process callback. Auto-frees nodes on completion.
        var tween = fill.CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(fill, "scale", new Vector2(1.3f, 1.3f), DisplaySeconds * 0.5f);
        tween.TweenProperty(outline, "scale", new Vector2(1.3f, 1.3f), DisplaySeconds * 0.5f);
        tween.Chain().TweenProperty(fill, "modulate:a", 0f, 0.5f);
        tween.Parallel().TweenProperty(outline, "modulate:a", 0f, 0.5f);

        // Hard cleanup — even if Tween glitches, this guarantees the node
        // disappears within DisplaySeconds + small buffer.
        var cleanupTimer = _tree.CreateTimer(DisplaySeconds + 0.2);
        cleanupTimer.Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(fill))    fill.QueueFree();
            if (GodotObject.IsInstanceValid(outline)) outline.QueueFree();
        };

        GD.Print($"[VR][Marker] showing at world {worldPos}");
    }
}
