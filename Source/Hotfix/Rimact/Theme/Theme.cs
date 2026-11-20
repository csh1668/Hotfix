using UnityEngine;

namespace Rimact;

/// <summary>Design tokens for the components.</summary>
/// <example><code>ThemeProvider(Theme.Default with { Primary = new Color(0.45f, 0.7f, 0.9f) }, new App())</code></example>
internal sealed record Theme
{
    public static readonly Theme Default = new();
    public static readonly Context<Theme> Context = new(Default, "Theme");

    private static Color Hex(int rgb, float a = 1f) =>
        new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, a);

    // A property, not a field: Default above is built before later static fields are set
    private static Color VanillaYellow => new(1f, 0.94f, 0.5f);

    #region Minerals
    public Color Gold { get; init; } = Hex(0xE4B54F);
    public Color Jade { get; init; } = Hex(0x46C190);
    public Color Plasteel { get; init; } = Hex(0x4FB3DA);
    public Color Copper { get; init; } = Hex(0xE88B45);
    public Color Ruby { get; init; } = Hex(0xE2546C);
    public Color Amethyst { get; init; } = Hex(0xA784E6);
    public Color Uranium { get; init; } = Hex(0xA3CF5B);
    public Color Silver { get; init; } = Hex(0xC9D1D6);
    public Color[] Minerals => [Gold, Jade, Plasteel, Copper, Ruby, Amethyst, Uranium, Silver];

    #endregion

    #region Surfaces
    /// <summary>Wells, inputs and tracks.</summary>
    public Color Sunken { get; init; } = Hex(0x0F1113);
    public Color Background { get; init; } = Hex(0x15191D);
    /// <summary>Cards and panels.</summary>
    public Color Surface { get; init; } = Hex(0x2A2B2C);
    /// <summary>Buttons and floating panels.</summary>
    public Color SurfaceRaised { get; init; } = Hex(0x363636);
    /// <summary>Gradient from the top over raised surfaces. Clear by default.</summary>
    public Color SurfaceSheen { get; init; } = new(1f, 1f, 1f, 0f);

    #endregion

    #region Lines
    public Color Border { get; init; } = Hex(0x4D4D4D);
    public Color BorderStrong { get; init; } = Hex(0x878787);
    /// <summary>Around a window, the color of the vanilla window border.</summary>
    public Color WindowBorder { get; init; } = Hex(0x616C7A);
    /// <summary>1px line along the top of raised surfaces. Clear by default.</summary>
    public Color TopEdge { get; init; } = new(1f, 1f, 1f, 0f);

    #endregion

    #region Text
    public Color Foreground { get; init; } = Color.white;
    public Color SecondaryForeground { get; init; } = Hex(0xCCCCCC);
    public Color MutedForeground { get; init; } = Hex(0x999999);
    public Color Link { get; init; } = new(0.8f, 0.85f, 1f);
    /// <summary>Hovered link text.</summary>
    public Color Hover { get; init; } = Color.yellow;

    #endregion

    #region Accent and semantic
    public Color Primary { get; init; } = Hex(0x6A512E);
    public Color PrimaryForeground { get; init; } = Color.white;
    /// <summary>Selection marks: checks, switch and slider fills, the tab underline, the selected-row bar.</summary>
    public Color Indicator { get; init; } = VanillaYellow;
    public Color IndicatorForeground { get; init; } = Hex(0x1E1608);
    public Color Destructive { get; init; } = Hex(0xE2546C);
    public Color Success { get; init; } = Hex(0x46C190);
    public Color Warning { get; init; } = Hex(0xE88B45);
    public Color Info { get; init; } = Hex(0x4FB3DA);

    #endregion

    #region State overlays
    public Color HoverOverlay { get; init; } = new(1f, 1f, 1f, 0.1f);
    public Color PressedOverlay { get; init; } = new(0f, 0f, 0f, 0.2f);
    public Color SelectedOverlay { get; init; } = new(1f, 0.94f, 0.5f, 0.18f);

    #endregion

    #region Aliases
    public Color Card { get; init; } = Hex(0x2A2B2C);
    public Color CardBorder { get; init; } = Hex(0x878787);
    /// <summary>Faint fill for striped rows.</summary>
    public Color Muted { get; init; } = new(1f, 1f, 1f, 0.03f);
    /// <summary>Subtle hover fill.</summary>
    public Color Accent { get; init; } = new(1f, 1f, 1f, 0.1f);
    public Color Input { get; init; } = Hex(0x0F1113);
    public Color Ring { get; init; } = VanillaYellow;
    /// <summary>Default progress fill.</summary>
    public Color Bar { get; init; } = Hex(0x4FB3DA);
    public Color PrimaryBorder { get; init; } = Hex(0x8C6D42);
    public Color Secondary { get; init; } = Hex(0x363636);
    public Color SecondaryBorder { get; init; } = Hex(0x5E5E5E);

    #endregion

    #region Shape and elevation
    /// <summary>Corner radii, 0 by default like vanilla. Raise them to round every component.</summary>
    public float RadiusSm { get; init; }
    public float Radius { get; init; }
    public float RadiusLg { get; init; }
    /// <summary>1px outline around small bevelled parts: wells, bars, swatches, floating panels.</summary>
    public Color Outline { get; init; } = new(0.004f, 0.004f, 0.004f, 0.95f);
    /// <summary>For buttons. BevelWidthSm is for cards and panels, flat by default.</summary>
    public float BevelWidth { get; init; } = 3f;
    public float BevelWidthSm { get; init; }
    /// <summary>Face color multiplier for the lit (top, right) bevel side.</summary>
    public float BevelLight { get; init; } = 1.2f;
    /// <summary>Vanilla textures for default buttons, select triggers, checkboxes and sliders. On by default.</summary>
    public bool VanillaParts { get; init; } = true;
    /// <summary>Face color multiplier for the shaded (left, bottom) bevel side.</summary>
    public float BevelDark { get; init; } = 0.75f;
    public Color ShadowColor { get; init; } = new(0f, 0f, 0f, 0.5f);
    public float ShadowSm { get; init; } = 4f;
    public float ShadowLg { get; init; } = 10f;

    #endregion

    #region Type
    public FontSize Font { get; init; } = FontSize.Small;
    public float TextCaption { get; init; } = 11f;
    public float TextTitle { get; init; } = 15f;
    public float TextHeading { get; init; } = 19f;

    #endregion

    #region Metrics
    public float Unit { get; init; } = 4f;
    public float ControlHeightSm { get; init; } = 24f;
    public float ControlHeight { get; init; } = 30f;
    public float ControlHeightLg { get; init; } = 36f;
    public float RowHeight { get; init; } = 28f;
    public float CheckboxSize { get; init; } = 24f;
    public float RadioSize { get; init; } = 16f;

    #endregion

    public float Space(float n) => n * Unit;

    public Bevel Raised(Color face, float width) => new(Scale(face, BevelLight), Scale(face, BevelDark), width);

    /// <summary>Flipped bevel for a recessed well (field, track). Uses fixed steps, not BevelLight and BevelDark.</summary>
    public Bevel Recessed(Color face, float width) => new Bevel(Lighten(face, 0.07f), Scale(face, 0.45f), width).Inverted;

    public static Color Scale(Color c, float k) => new(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), c.a);

    public static Color WithAlpha(Color c, float a) => new(c.r, c.g, c.b, a);

    public static Color Lighten(Color c, float amount) =>
        new(Mathf.Clamp01(c.r + amount), Mathf.Clamp01(c.g + amount), Mathf.Clamp01(c.b + amount), c.a);
}
