using Godot;
using System.Collections.Generic;

namespace VoiceRoulette.UI;

/// <summary>
/// Renders a transient down-pointing arrow at a world position when someone
/// places a tactical "look here" marker. Auto-fades out after ~2s.
/// </summary>
public sealed partial class MarkerOverlay : CanvasLayer
{
    private const float DisplaySeconds = 2.0f;
    private const float ArrowOffsetY   = 70f;        // float arrow this far above the target
    private const float ArrowSize      = 36f;

    private static readonly Color FillColor    = StsTheme.MenuAccent;
    private static readonly Color OutlineColor = new("00000099");

    private SceneTree? _tree;
    private readonly List<MarkerInstance> _active = new();

    public void Start()
    {
        _tree = (SceneTree)Engine.GetMainLoop();
        Layer = 110;  // above bubble overlay (100), below settings (250)
        ProcessMode = ProcessModeEnum.Always;
        SetProcess(true);
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
                new Vector2(0, ArrowSize),                              // tip (downward)
                new Vector2(-ArrowSize * 0.6f, 0),                      // top-left
                new Vector2(ArrowSize * 0.6f, 0),                       // top-right
            },
            Color = FillColor,
            Position = anchor,
        };

        // Black outline drawn underneath the fill for readability against any bg.
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

        _active.Add(new MarkerInstance
        {
            Fill = fill,
            Outline = outline,
            Born = Time.GetTicksMsec() / 1000.0,
        });

        GD.Print($"[VR][Marker] showing at world {worldPos}");
    }

    public override void _Process(double delta)
    {
        if (_active.Count == 0) return;
        var now = Time.GetTicksMsec() / 1000.0;
        for (var i = _active.Count - 1; i >= 0; i--)
        {
            var m = _active[i];
            var age = now - m.Born;
            if (age >= DisplaySeconds)
            {
                if (GodotObject.IsInstanceValid(m.Fill))    m.Fill.QueueFree();
                if (GodotObject.IsInstanceValid(m.Outline)) m.Outline.QueueFree();
                _active.RemoveAt(i);
                continue;
            }
            // Pulse: scale 1.0 → 1.2 → 1.0 over the lifetime; fade out last 0.5s.
            var t = (float)(age / DisplaySeconds);
            var scale = 1f + 0.2f * Mathf.Sin(t * Mathf.Pi);
            var alpha = age > DisplaySeconds - 0.5
                ? (float)((DisplaySeconds - age) / 0.5)
                : 1f;

            if (GodotObject.IsInstanceValid(m.Fill))
            {
                m.Fill.Scale = new Vector2(scale, scale);
                m.Fill.Modulate = new Color(1, 1, 1, alpha);
            }
            if (GodotObject.IsInstanceValid(m.Outline))
            {
                m.Outline.Scale = new Vector2(scale, scale);
                m.Outline.Modulate = new Color(1, 1, 1, alpha);
            }
        }
    }

    private sealed class MarkerInstance
    {
        public required Polygon2D Fill;
        public required Line2D Outline;
        public required double Born;
    }
}
