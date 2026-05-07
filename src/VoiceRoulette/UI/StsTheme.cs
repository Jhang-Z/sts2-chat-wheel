using Godot;

namespace VoiceRoulette.UI;

// Centralized design tokens — see design.md.
//
// Values mirror MegaCrit.Sts2.Core.Helpers.StsColors so the mod's overlay UI
// reads as part of the game rather than a transplant. We hard-code the hex
// values here (rather than reflecting at runtime) because:
//   1. Avoids a runtime dependency that could break on game updates
//   2. Makes the palette greppable and easy to tweak
//   3. Survives the mod loading before the game's static ctors run
public static class StsTheme
{
    // ── Core palette (StsColors verbatim) ────────────────────────────────────
    public static readonly Color Cream             = new("FFF6E2");   // primary text
    public static readonly Color CreamHalf         = new("FFF6E280"); // 50% cream
    public static readonly Color Gold              = new("EFC851");   // accent
    public static readonly Color Aqua              = new("2AEBBE");   // interactive / highlight
    public static readonly Color Red               = new("FF5555");
    public static readonly Color Orange            = new("FFA518");
    public static readonly Color Pink              = new("FF78A0");
    public static readonly Color Purple            = new("EE82EE");
    public static readonly Color Blue              = new("87CEEB");
    public static readonly Color DarkBlue          = new("67AEEB");
    public static readonly Color Green             = new("7FFF00");
    public static readonly Color MerchantBlue      = new("516ACF");

    // ── Backgrounds / surfaces ───────────────────────────────────────────────
    public static readonly Color ScreenBackdrop    = new("000000CC");  // 80% black overlay
    public static readonly Color NinetyBlack       = new("000000E5");
    public static readonly Color HalfBlack         = new("0000007F");
    public static readonly Color QuarterBlack      = new("0000003F");
    public static readonly Color PathDot           = new("241F1A");    // deep brown
    public static readonly Color BossNodeWood      = new("7D6A55");    // wood brown
    public static readonly Color LegendNavy        = new("2B3152");    // deep navy
    public static readonly Color ExhaustGray       = new("191919");

    // ── Greys ────────────────────────────────────────────────────────────────
    public static readonly Color LightGray         = new("BFBFBF");    // captions
    public static readonly Color Gray              = new("7F7F7F");    // disabled
    public static readonly Color DisabledGray      = new("5E5E5E");

    // ── Card title outlines (used as category color palette) ────────────────
    public static readonly Color RarityCommon      = new("4D4B40");
    public static readonly Color RarityUncommon    = new("005C75");
    public static readonly Color RarityRare        = new("6B4B00");
    public static readonly Color RarityCurse       = new("550B9E");
    public static readonly Color RarityQuest       = new("7E3E15");
    public static readonly Color RarityStatus      = new("4F522F");
    public static readonly Color RaritySpecial     = new("1B6131");

    // ── Two distinct surface palettes ─────────────────────────────────────────
    // StS2 actually uses TWO visual languages: warm brown for combat/dungeon
    // (cards, paths, encounters) and cool navy for menus/settings/popups.
    // We expose both so each component can pick the right one. Settings UI uses
    // MenuXxx; in-combat overlays (wheel, bubble) use PanelXxx (warm).

    // ── Combat / dungeon palette (warm brown) ────────────────────────────────
    public static readonly Color PanelBg           = new("17120E");    // near-black warm
    public static readonly Color PanelBgAlt        = new("241F1A");    // = PathDot
    public static readonly Color PanelBgHover      = new("3D332A");    // warmer brown lift
    public static readonly Color PanelBorder       = new("4D4B40");    // RarityCommon
    public static readonly Color PanelBorderHi     = new("EFC851");    // gold
    public static readonly Color InputBg           = new("0F0C09");    // deepest, warm
    public static readonly Color InputBorder       = new("4D4B40");

    // ── Menu / settings palette (warm earthy parchment-and-ink) ──────────────
    // Mirrors the user's design mock: very dark warm-gray background with
    // muted brass accents and parchment-cream text. This deliberately avoids
    // the in-game settings screen's navy+cyan look — the chat-wheel feature
    // belongs visually in the "adventure" register, not the "options menu"
    // register. Numbers chosen to read like ink-on-vellum at a glance.
    public static readonly Color MenuBg            = new("181613");    // very dark warm
    public static readonly Color MenuBgAlt         = new("221F1A");    // panel inset
    public static readonly Color MenuBgHover       = new("3D362C");    // hover lift
    public static readonly Color MenuBgSlate       = new("2C2820");    // banner / section
    public static readonly Color MenuBorder        = new("5C4F38");    // muted gold-brown
    public static readonly Color MenuDivider       = new("5C4F3880");
    public static readonly Color MenuAccent        = new("C9A551");    // muted brass (titles, ◀▶)
    public static readonly Color MenuAccentSoft    = new("8A7E62");    // dim parchment for captions
    public static readonly Color MenuText          = new("C4B89C");    // parchment cream (primary text)
    public static readonly Color MenuTextDim       = new("8A7E62");
    public static readonly Color MenuInputBg       = new("11100E");
    public static readonly Color MenuRedAccent     = new("A4332C");    // 保存 button accent

    // ── Semantic for buttons ─────────────────────────────────────────────────
    public static readonly Color BtnBg             = new("241F1A");
    public static readonly Color BtnBgHover        = new("3D332A");
    public static readonly Color BtnPrimaryBg      = new("A4332C");    // dimmed Red
    public static readonly Color BtnPrimaryHover   = new("C84A40");

    // ── Emotion → category color mapping (uses rarity palette as reference) ─
    public static Color EmotionDot(string apiValue) => apiValue switch
    {
        "happy"        => Gold,
        "angry"        => Red,
        "sad"          => DarkBlue,
        "sorry"        => Purple,
        "novel_dialog" => Cream,
        _              => Gray,
    };

    // ── Geometry (consistent across components) ──────────────────────────────
    public const int RadiusSmall  = 6;
    public const int RadiusMid    = 10;
    public const int RadiusLarge  = 14;

    public const int BorderThin   = 1;
    public const int BorderHi     = 2;

    // ── Type sizes (StS2 uses generous sizing for readability at 1080p+) ─────
    public const int FontH1       = 28;
    public const int FontH2       = 20;
    public const int FontBody     = 17;
    public const int FontCaption  = 14;
    public const int FontTiny     = 12;

    // ── Stylebox factories ───────────────────────────────────────────────────

    public static StyleBoxFlat Panel(int radius = RadiusLarge, int borderW = BorderHi)
    {
        return new StyleBoxFlat
        {
            BgColor = PanelBg, BorderColor = PanelBorder,
            BorderWidthLeft = borderW, BorderWidthRight = borderW,
            BorderWidthTop = borderW, BorderWidthBottom = borderW,
            CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
            CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius,
            ShadowColor = ScreenBackdrop, ShadowSize = 18,
        };
    }

    public static StyleBoxFlat Section(int radius = RadiusMid)
    {
        return new StyleBoxFlat
        {
            BgColor = PanelBgAlt, BorderColor = PanelBorder,
            BorderWidthLeft = BorderThin, BorderWidthRight = BorderThin,
            BorderWidthTop = BorderThin, BorderWidthBottom = BorderThin,
            CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
            CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius,
            ContentMarginLeft = 14, ContentMarginRight = 14,
            ContentMarginTop = 10, ContentMarginBottom = 10,
        };
    }

    public static StyleBoxFlat Row(bool selected, int radius = 8)
    {
        return new StyleBoxFlat
        {
            BgColor = selected ? PanelBgHover : PanelBgAlt,
            BorderColor = selected ? Gold : PanelBorder,
            BorderWidthLeft = selected ? 2 : 1,
            BorderWidthRight = selected ? 2 : 1,
            BorderWidthTop = selected ? 2 : 1,
            BorderWidthBottom = selected ? 2 : 1,
            CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
            CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius,
            ContentMarginLeft = 14, ContentMarginRight = 14,
            ContentMarginTop = 8, ContentMarginBottom = 8,
        };
    }

    public static StyleBoxFlat Input(bool focused = false, int radius = RadiusSmall)
    {
        return new StyleBoxFlat
        {
            BgColor = InputBg,
            BorderColor = focused ? Gold : InputBorder,
            BorderWidthLeft = BorderThin, BorderWidthRight = BorderThin,
            BorderWidthTop = BorderThin, BorderWidthBottom = BorderThin,
            CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
            CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius,
            ContentMarginLeft = 10, ContentMarginRight = 10,
            ContentMarginTop = 6, ContentMarginBottom = 6,
        };
    }

    public static StyleBoxFlat Button(Color bg, Color border, int radius = RadiusSmall)
    {
        return new StyleBoxFlat
        {
            BgColor = bg, BorderColor = border,
            BorderWidthLeft = BorderThin, BorderWidthRight = BorderThin,
            BorderWidthTop = BorderThin, BorderWidthBottom = BorderThin,
            CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
            CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius,
            ContentMarginLeft = 12, ContentMarginRight = 12,
            ContentMarginTop = 6, ContentMarginBottom = 6,
        };
    }

    public static StyleBoxFlat Tab(bool active)
    {
        return new StyleBoxFlat
        {
            BgColor = active ? PanelBgHover : PanelBgAlt,
            BorderColor = active ? Gold : PanelBorder,
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1,
            BorderWidthBottom = active ? 3 : 1,  // chunky bottom rule when active
            CornerRadiusBottomLeft = 0, CornerRadiusBottomRight = 0,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
            ContentMarginLeft = 14, ContentMarginRight = 14,
            ContentMarginTop = 6, ContentMarginBottom = 6,
        };
    }
}
