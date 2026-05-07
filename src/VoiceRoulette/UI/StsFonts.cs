using Godot;

namespace VoiceRoulette.UI;

// Loads StS2's actual fonts so our overlay UI matches the game.
//
// The game ships these in the .pck and they're accessible via res:// at runtime
// (the mod runs inside the game's Godot instance, so the resource filesystem is
// shared). If loading fails (e.g. game updates and moves them), we fall back
// silently to Godot's default font — better than crashing.
//
// Usage:
//   StsFonts.ApplyTo(label, FontWeight.Body);
//   StsFonts.ApplyTo(button, FontWeight.Body);
public static class StsFonts
{
    public enum FontWeight { Body, Bold }

    private const string PathBody = "res://fonts/zhs/SourceHanSerifSC-Medium.otf";
    private const string PathBold = "res://fonts/zhs/SourceHanSerifSC-Bold.otf";

    private static Font? _body;
    private static Font? _bold;
    private static bool _loaded;

    public static Font? Body { get { EnsureLoaded(); return _body; } }
    public static Font? Bold { get { EnsureLoaded(); return _bold; } }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        _body = TryLoad(PathBody);
        _bold = TryLoad(PathBold);
        var ok = (_body != null ? 1 : 0) + (_bold != null ? 1 : 0);
        GD.Print($"[VR][Fonts] loaded {ok}/2 — body={_body != null} bold={_bold != null}");
    }

    private static Font? TryLoad(string resPath)
    {
        try
        {
            if (!ResourceLoader.Exists(resPath, "Font")) return null;
            return ResourceLoader.Load<Font>(resPath);
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[VR][Fonts] failed to load {resPath}: {ex.Message}");
            return null;
        }
    }

    /// Apply the chosen weight as a theme override on the control's "font" override.
    /// Works for Label, Button, LineEdit, OptionButton, CheckButton, etc. — all
    /// inherit the same font theme override key in Godot.
    public static void ApplyTo(Control c, FontWeight weight = FontWeight.Body)
    {
        var f = weight == FontWeight.Bold ? Bold : Body;
        if (f == null) return;
        c.AddThemeFontOverride("font", f);
    }
}
