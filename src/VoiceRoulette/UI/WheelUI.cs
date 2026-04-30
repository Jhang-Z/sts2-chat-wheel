using Godot;
using System.Collections.Generic;

namespace VoiceRoulette.UI;

public sealed partial class WheelUI : CanvasLayer
{
    private const int SectorCount = 8;
    private const float WheelRadius = 180f;
    private const float MouseDeadZoneSquared = 25f;

    private readonly Label[] _labels = new Label[SectorCount];
    private Label? _hint;
    private int _selected = -1;
    private List<string> _texts = new();

    public int SelectedIndex => _selected;

    public override void _Ready()
    {
        var viewportSize = GetViewport().GetVisibleRect().Size;
        var center = new Vector2(viewportSize.X / 2f, viewportSize.Y / 2f);

        for (int i = 0; i < SectorCount; i++)
        {
            var angle = -Mathf.Pi / 2f + i * (2f * Mathf.Pi / SectorCount);
            var offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * WheelRadius;
            var lbl = new Label { Position = center + offset };
            AddChild(lbl);
            _labels[i] = lbl;
        }

        _hint = new Label { Position = center + new Vector2(-100f, WheelRadius + 40f) };
        AddChild(_hint);

        Visible = false;
    }

    public void OpenWheel(IList<string> lineTexts)
    {
        _texts = new List<string>(lineTexts);
        for (int i = 0; i < SectorCount; i++)
            _labels[i].Text = i < _texts.Count ? _texts[i] : "";
        _selected = -1;
        Visible = true;
    }

    public void CloseWheel()
    {
        Visible = false;
        _selected = -1;
    }

    public void SetSelectedFromMouse(Vector2 mouseDelta)
    {
        if (mouseDelta.LengthSquared() < MouseDeadZoneSquared)
        {
            Highlight(-1);
            return;
        }

        var angle = Mathf.Atan2(mouseDelta.Y, mouseDelta.X) + Mathf.Pi / 2f + Mathf.Pi / SectorCount;
        if (angle < 0) angle += 2f * Mathf.Pi;
        var idx = (int)(angle / (2f * Mathf.Pi / SectorCount)) % SectorCount;
        Highlight(idx);
    }

    private void Highlight(int idx)
    {
        if (idx == _selected) return;
        if (_selected >= 0) _labels[_selected].Modulate = Colors.White;
        if (idx >= 0) _labels[idx].Modulate = Colors.Yellow;
        _selected = idx;
        if (_hint != null)
            _hint.Text = idx >= 0 && idx < _texts.Count ? _texts[idx] : "";
    }
}
