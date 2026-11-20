using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using UnityStubs.Gui;
using Verse;
using Verse.Sound;

namespace Rimact;

// Rects come in host space, the GUI coords current at RimactHost.Draw. PushClip (GUI.BeginGroup) moves the
// origin, so Local() subtracts it. Every draw sets the globals it uses and RimactHost restores them.
[StaticConstructorOnStartup]
internal sealed class ImguiBackend : IBackend
{
    public static readonly ImguiBackend Instance = new();

    private readonly Stack<Vector2> _origins = new();
    private readonly Stack<float> _alphas = new();
    private readonly Stack<(Vector2 origin, float alpha, int clips, int alphas)> _frames = new();
    private Vector2 _origin;
    private float _alpha = 1f;

    private static Texture2D? s_radioOff, s_warning;
    private static TabRecord? s_tabDrawer;
    private static TabRecord? s_tabDrawerSelected;

    // Saves the outer state so hosts can nest, e.g. a host inside a Native leaf
    public void BeginFrame()
    {
        _frames.Push((_origin, _alpha, _origins.Count, _alphas.Count));
        _origin = Vector2.zero;
        _alpha = 1f;
    }

    // Closes every group opened in this frame. Unbalanced groups break IMGUI
    public void EndFrame()
    {
        if (_frames.Count == 0) return;
        var f = _frames.Pop();
        while (_origins.Count > f.clips) PopClip();
        while (_alphas.Count > f.alphas) _alphas.Pop();
        _origin = f.origin;
        _alpha = f.alpha;
    }

    private Rect Local(Rect r) => new(r.x - _origin.x, r.y - _origin.y, r.width, r.height);

    // First thing in every draw method. Magenta globals expose a draw that doesn't set what it uses
    private static void Poison()
    {
        if (!RimactDiagnostics.PoisonGuiState) return;
        GUI.color = RimactDiagnostics.Poison;
        GUI.backgroundColor = RimactDiagnostics.Poison;
    }

    private static readonly HashSet<string> s_reported = new();

    private static void ReportLeftovers(GuiSnapshot before, GuiSnapshot after, string? owner)
    {
        var changed = GuiSnapshot.Changes(before, after);
        if (changed.Count == 0) return;
        string where = owner ?? "a native leaf";
        string msg = $"[Rimact] {where} left {string.Join(", ", changed)} changed after drawing. Rimact restored it; restore it in the leaf (Temporary scopes help) to avoid surprises outside Rimact.";
        if (s_reported.Add(msg)) Log.Warning(msg);
    }

    private Color A(Color c) => _alpha >= 1f ? c : new Color(c.r, c.g, c.b, c.a * _alpha);

    private static GameFont Font(FontSize f) => f switch
    {
        FontSize.Tiny => GameFont.Tiny,
        FontSize.Medium => GameFont.Medium,
        _ => GameFont.Small,
    };

    private static TextAnchor Anchor(TextAlign a) => a switch
    {
        TextAlign.UpperLeft => TextAnchor.UpperLeft,
        TextAlign.UpperCenter => TextAnchor.UpperCenter,
        TextAlign.UpperRight => TextAnchor.UpperRight,
        TextAlign.MiddleCenter => TextAnchor.MiddleCenter,
        TextAlign.MiddleRight => TextAnchor.MiddleRight,
        TextAlign.LowerLeft => TextAnchor.LowerLeft,
        TextAlign.LowerCenter => TextAnchor.LowerCenter,
        TextAlign.LowerRight => TextAnchor.LowerRight,
        _ => TextAnchor.MiddleLeft,
    };

    #region Measurement

    // Depends only on (text, font, size, bold, wrap width), so it's shared across frames and hosts
    private const int MeasureCacheLimit = 4096;
    private static readonly Dictionary<(string, FontSize, int, bool, float), Vector2> s_measured = new();

    private static bool TryCached((string, FontSize, int, bool, float) key, out Vector2 size) => s_measured.TryGetValue(key, out size);

    private static Vector2 Cache((string, FontSize, int, bool, float) key, Vector2 size)
    {
        if (s_measured.Count >= MeasureCacheLimit) s_measured.Clear();
        s_measured[key] = size;
        return size;
    }

    public Vector2 MeasureText(string text, FontSize font, float wrapWidth)
    {
        var key = (text ?? "", font, 0, false, wrapWidth);
        return TryCached(key, out var hit) ? hit : Cache(key, MeasureUncached(text, font, wrapWidth));
    }

    private static Vector2 MeasureUncached(string? text, FontSize font, float wrapWidth)
    {
        Text.Font = Font(font);
        if (string.IsNullOrEmpty(text)) return new Vector2(0, Text.LineHeight);
        Text.WordWrap = false;
        var s = Text.CalcSize(text);
        s.x += TrailingSpaces(text, x => Text.CalcSize(x).x);
        if (wrapWidth > 0 && s.x > wrapWidth)
        {
            Text.WordWrap = true;
            s = new Vector2(wrapWidth, Text.CalcHeight(text, wrapWidth));
        }
        return s;
    }

    // 1.7's text layout leaves trailing spaces out of the width. Without this, flowing words run together
    private static float TrailingSpaces(string text, Func<string, float> width)
    {
        int n = 0;
        while (n < text.Length && text[text.Length - 1 - n] == ' ') n++;
        if (n == 0) return 0f;
        float x = width("x");
        if (width("x ") > x) return 0f; // the layout already counts them
        return Mathf.Max(0f, width("x" + new string(' ', n) + "x") - width("xx"));
    }

    public Vector2 MeasureText(string text, TextStyle style, float wrapWidth)
    {
        var st = StyleFor(style);
        if (st is null) return MeasureText(text, style.Font, wrapWidth);
        var key = (text ?? "", style.Font, Mathf.RoundToInt(style.Size), style.Bold, wrapWidth);
        if (TryCached(key, out var hit)) return hit;
        return Cache(key, MeasureStyled(st, text, wrapWidth));
    }

    private static Vector2 MeasureStyled(GUIStyle st, string? text, float wrapWidth)
    {
        if (string.IsNullOrEmpty(text)) return new Vector2(0, st.lineHeight);
        st.wordWrap = false;
        s_content.text = text;
        var s = st.CalcSize(s_content);
        s.x += TrailingSpaces(text, x =>
        {
            s_content.text = x;
            return st.CalcSize(s_content).x;
        });
        s_content.text = text;
        if (wrapWidth > 0 && s.x > wrapWidth)
        {
            st.wordWrap = true;
            s = new Vector2(wrapWidth, st.CalcHeight(s_content, wrapWidth));
        }
        return s;
    }

    private static readonly GUIContent s_content = new();
    private static readonly Dictionary<(FontSize, int, bool), GUIStyle> s_styles = new();

    // Copy of the vanilla label style with size and bold applied. null = plain font
    private static GUIStyle? StyleFor(TextStyle style)
    {
        if (style.Size <= 0f && !style.Bold) return null;
        var key = (style.Font, Mathf.RoundToInt(style.Size), style.Bold);
        if (s_styles.TryGetValue(key, out var st)) return st;
        st = new GUIStyle(Text.fontStyles[(int)Font(style.Font)]);
        if (key.Item2 > 0) st.fontSize = key.Item2;
        if (style.Bold) st.fontStyle = FontStyle.Bold;
        s_styles[key] = st;
        return st;
    }

    // Vanilla's string.Truncate isn't cached and drops one char per CalcSize. A long line costs hundreds of
    // measurements every repaint
    private const int TruncateCacheLimit = 4096;
    private static readonly Dictionary<(string, FontSize, int, bool, int), string> s_truncated = new();

    private static string Truncated(string text, TextStyle style, GUIStyle st, float width)
    {
        int w = Mathf.FloorToInt(width);
        var key = (text, style.Font, Mathf.RoundToInt(style.Size), style.Bold, w);
        if (s_truncated.TryGetValue(key, out var hit)) return hit;
        var result = TruncateTo(st, text, w);
        if (s_truncated.Count >= TruncateCacheLimit) s_truncated.Clear();
        s_truncated[key] = result;
        return result;
    }

    // Binary search for the longest prefix that fits with "..."
    private static string TruncateTo(GUIStyle st, string text, float width)
    {
        s_content.text = text;
        if (st.CalcSize(s_content).x <= width) return text;
        if (RichTags.Has(text)) return TruncateTagged(st, text, width);
        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            s_content.text = text.Substring(0, mid) + "...";
            if (st.CalcSize(s_content).x <= width) lo = mid;
            else hi = mid - 1;
        }
        return text.Substring(0, lo).TrimEnd() + "...";
    }

    // Counts visible chars so a cut never splits or drops a color tag
    private static string TruncateTagged(GUIStyle st, string text, float width)
    {
        int lo = 0, hi = RichTags.VisibleLength(text);
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            s_content.text = RichTags.Cut(text, mid, "...");
            if (st.CalcSize(s_content).x <= width) lo = mid;
            else hi = mid - 1;
        }
        return RichTags.Cut(text, lo, "...");
    }

    #endregion

    #region Drawing

    public void FillRect(Rect r, Color c)
    {
        Poison();
        GUI.color = A(c);
        GUI.DrawTexture(Local(r), BaseContent.WhiteTex);
    }

    public void DrawFocusRing(Rect r, Color c) => DrawBorder(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), c, 2f);

    public void DrawBorder(Rect r, Color c, float thickness)
    {
        Poison();
        GUI.color = A(c);
        Widgets.DrawBox(Local(r), Mathf.Max(1, Mathf.RoundToInt(thickness)));
    }

    public void DrawText(Rect r, string text, TextStyle style, Color color)
    {
        Poison();
        var st = StyleFor(style);
        if (st != null)
        {
            var sr = Local(r);
            st.alignment = Anchor(style.Align);
            st.wordWrap = style.Wrap;
            GUI.color = A(color);
            GUI.Label(sr, style.Truncate && !style.Wrap ? Truncated(text, style, st, sr.width) : text, st);
            return;
        }
        Text.Font = Font(style.Font);
        Text.Anchor = Anchor(style.Align);
        Text.WordWrap = style.Wrap;
        GUI.color = A(color);
        var lr = Local(r);
        if (style.Truncate && !style.Wrap && !string.IsNullOrEmpty(text)) text = Truncated(text, style, Text.fontStyles[(int)Text.Font], lr.width);
        Widgets.Label(lr, text);
    }

    public void DrawIcon(Rect r, Icon icon, Color tint)
    {
        Poison();
        if (icon.IsGame)
        {
            DrawGameIcon(Local(r), icon, tint.a * _alpha);
            return;
        }
        var tex = icon.Texture ?? Resolve(icon.Builtin);
        if (tex is null) return;
        GUI.color = A(tint);
        GUI.DrawTexture(Local(r), tex, ScaleMode.ScaleToFit);
    }

    // Vanilla's widgets, in the icon's own colors
    private static void DrawGameIcon(Rect r, Icon icon, float alpha)
    {
        GUI.color = new Color(1f, 1f, 1f, alpha);
        if (icon.Thing is Pawn pawn && icon.Portrait)
        {
            // Each size is its own render texture. Round it or a resizing container re-renders the pawn every frame
            var size = new Vector2(Mathf.Round(r.width), Mathf.Round(r.height));
            if (size.x >= 1f && size.y >= 1f) GUI.DrawTexture(r, PortraitsCache.Get(pawn, size, Rot4.South), ScaleMode.ScaleToFit);
            return;
        }
        if (icon.Thing is { } thing) Widgets.ThingIcon(r, thing, alpha);
        else if (icon.Def is { } def) Widgets.DefIcon(r, def, icon.Stuff, alpha: alpha);
    }

    private static Texture2D? Resolve(BuiltinIcon b) => b switch
    {
        BuiltinIcon.CheckboxOn => Widgets.CheckboxOnTex,
        BuiltinIcon.CheckboxOff => Widgets.CheckboxOffTex,
        BuiltinIcon.CheckboxPartial => Widgets.CheckboxPartialTex,
        BuiltinIcon.RadioOn => Widgets.RadioButOnTex,
        BuiltinIcon.RadioOff => s_radioOff ??= ContentFinder<Texture2D>.Get("UI/Widgets/RadioButOff"),
        BuiltinIcon.Close => TexButton.CloseXSmall,
        BuiltinIcon.Plus => TexButton.Plus,
        BuiltinIcon.Minus => TexButton.Minus,
        BuiltinIcon.Search => TexButton.Search,
        BuiltinIcon.ChevronDown => TexButton.Collapse,
        BuiltinIcon.ChevronRight => TexButton.Reveal,
        BuiltinIcon.Info => TexButton.Info,
        BuiltinIcon.Warning => s_warning ??= ContentFinder<Texture2D>.Get("UI/Widgets/Warning"),
        _ => null,
    };

    public void DrawVanilla(Rect r, VanillaPart part, InteractionState state)
    {
        Poison();
        var lr = Local(r);
        GUI.color = A(Color.white);
        bool hovered = (state & InteractionState.Hovered) != 0;
        bool pressed = (state & InteractionState.Pressed) != 0;
        switch (part)
        {
            case VanillaPart.Button:
                // Same atlases as Widgets.DrawButtonGraphic, but the core supplies the mouse state
                Widgets.DrawAtlas(lr, pressed ? Widgets.ButtonBGAtlasClick : hovered ? ButtonBGAtlasMouseover : Widgets.ButtonBGAtlas);
                break;
            case VanillaPart.ButtonSubtle:
                Widgets.DrawAtlas(lr, Widgets.ButtonSubtleAtlas);
                if (hovered) Widgets.DrawHighlight(lr);
                break;
            case VanillaPart.Tab:
            case VanillaPart.TabSelected:
                // An unlabeled TabRecord draws just the background
                var tab = part == VanillaPart.TabSelected
                    ? s_tabDrawerSelected ??= new TabRecord("", null, true)
                    : s_tabDrawer ??= new TabRecord("", null, false);
                tab.Draw(lr);
                break;
            case VanillaPart.Highlight:
                Widgets.DrawHighlight(lr);
                break;
            case VanillaPart.LightHighlight:
                Widgets.DrawLightHighlight(lr);
                break;
            case VanillaPart.MenuSection:
                Widgets.DrawMenuSection(lr);
                break;
            case VanillaPart.WindowBackground:
                Widgets.DrawWindowBackground(lr);
                break;
            case VanillaPart.SliderRail:
                Widgets.DrawAtlas(lr, SliderRail);
                break;
            case VanillaPart.SliderHandle:
                GUI.DrawTexture(lr, SliderHandle);
                break;
        }
    }

    #endregion

    #region Shapes

    private static bool s_shapesBroken;
    private readonly List<PaintVertex> _tris = new();
    private GuiVertex[] _verts = new GuiVertex[384];

    private static IGuiBackend? Shapes => s_shapesBroken ? null : GuiBackend.Current;

    private static void ShapesFailed(Exception e)
    {
        if (s_shapesBroken) return;
        s_shapesBroken = true;
        Log.Warning("[Rimact] GUI backend shapes are unavailable; falling back to flat drawing. " + e.Message);
    }

    public void FillRounded(Rect r, Color c, float radius)
    {
        Poison();
        FillRoundedRaw(Local(r), c, radius);
    }

    // gr is in current GUI coords, not host space
    private void FillRoundedRaw(Rect gr, Color c, float radius)
    {
        if (radius > 0f && Shapes is { } g)
        {
            try
            {
                g.FillRectRounded(gr, A(c), radius);
                return;
            }
            catch (Exception e)
            {
                ShapesFailed(e);
            }
        }
        GUI.color = A(c);
        GUI.DrawTexture(gr, BaseContent.WhiteTex);
    }

    public void StrokeRounded(Rect r, Color c, float thickness, float radius)
    {
        Poison();
        StrokeRoundedRaw(Local(r), c, thickness, radius);
    }

    private void StrokeRoundedRaw(Rect gr, Color c, float thickness, float radius)
    {
        _tris.Clear();
        Geometry.StrokeRounded(gr, radius, thickness, c, _tris);
        if (DrawTris()) return;
        GUI.color = A(c);
        Widgets.DrawBox(gr, Mathf.Max(1, Mathf.RoundToInt(thickness)));
    }

    public void FillGradient(Rect r, Color from, Color to, GradientDirection direction)
    {
        Poison();
        _tris.Clear();
        Geometry.Gradient(Local(r), from, to, direction, _tris);
        if (DrawTris()) return;
        GUI.color = A(Color.Lerp(from, to, 0.5f));
        GUI.DrawTexture(Local(r), BaseContent.WhiteTex);
    }

    public void DrawShadow(Rect r, float radius, Shadow shadow)
    {
        Poison();
        _tris.Clear();
        Geometry.Shadow(Local(r), radius, shadow, _tris);
        DrawTris(); // decorative, skipped without the GUI backend
    }

    public void DrawLine(Vector2 a, Vector2 b, Color c, float thickness)
    {
        Poison();
        _tris.Clear();
        Geometry.Line(a - _origin, b - _origin, thickness, c, _tris);
        if (DrawTris()) return;
        if (Mathf.Approximately(a.x, b.x) || Mathf.Approximately(a.y, b.y))
        {
            GUI.color = A(c);
            var min = Vector2.Min(a, b) - _origin;
            var max = Vector2.Max(a, b) - _origin;
            GUI.DrawTexture(new Rect(min.x, min.y, Mathf.Max(thickness, max.x - min.x), Mathf.Max(thickness, max.y - min.y)), BaseContent.WhiteTex);
        }
    }

    public void DrawBevel(Rect r, Color light, Color dark, float width)
    {
        Poison();
        DrawBevelRaw(Local(r), light, dark, width);
    }

    private void DrawBevelRaw(Rect gr, Color light, Color dark, float width)
    {
        _tris.Clear();
        Geometry.Bevel(gr, light, dark, width, _tris);
        if (DrawTris()) return;
        // Fallback: plain bands, no mitres
        float w = Mathf.Min(width, Mathf.Min(gr.width, gr.height) / 2f);
        GUI.color = A(light);
        GUI.DrawTexture(new Rect(gr.x, gr.y, gr.width, w), BaseContent.WhiteTex);
        GUI.DrawTexture(new Rect(gr.xMax - w, gr.y, w, gr.height), BaseContent.WhiteTex);
        GUI.color = A(dark);
        GUI.DrawTexture(new Rect(gr.x, gr.yMax - w, gr.width - w, w), BaseContent.WhiteTex);
        GUI.DrawTexture(new Rect(gr.x, gr.y + w, w, gr.height - w * 2), BaseContent.WhiteTex);
    }

    public void FillTriangles(IReadOnlyList<Vector2> vertices, Color c)
    {
        Poison();
        _tris.Clear();
        int n = vertices.Count - vertices.Count % 3;
        for (int i = 0; i < n; i++) _tris.Add(new PaintVertex(vertices[i] - _origin, c));
        DrawTris(); // decorative, skipped without the GUI backend
    }

    public void DrawStripes(Rect r, Color c, float spacing, float width, float phase)
    {
        Poison();
        _tris.Clear();
        Geometry.Stripes(Local(r), spacing, width, phase, c, _tris);
        DrawTris(); // decorative, skipped without the GUI backend
    }

    // _tris are already in GUI coords. false if the backend can't draw them
    private bool DrawTris()
    {
        if (_tris.Count == 0) return true;
        if (Shapes is not { } g) return false;
        if (_verts.Length < _tris.Count) _verts = new GuiVertex[Mathf.NextPowerOfTwo(_tris.Count)];
        for (int i = 0; i < _tris.Count; i++)
        {
            var v = _tris[i];
            _verts[i] = new GuiVertex { position = v.Pos, color = A(v.Color) };
        }
        try
        {
            g.DrawPrimitives(4, new ReadOnlySpan<GuiVertex>(_verts, 0, _tris.Count), null!, false); // 4 = triangle list
            return true;
        }
        catch (Exception e)
        {
            ShapesFailed(e);
            return false;
        }
    }

    // Same paths as the private textures in Widgets
    private static Texture2D? s_buttonMouseover, s_sliderRail, s_sliderHandle;
    private static Texture2D ButtonBGAtlasMouseover => s_buttonMouseover ??= ContentFinder<Texture2D>.Get("UI/Widgets/ButtonBGMouseover");
    private static Texture2D SliderRail => s_sliderRail ??= ContentFinder<Texture2D>.Get("UI/Buttons/SliderRail");
    private static Texture2D SliderHandle => s_sliderHandle ??= ContentFinder<Texture2D>.Get("UI/Buttons/SliderHandle");

    #endregion

    #region State stacks

    public void PushClip(Rect r)
    {
        GUI.BeginGroup(Local(r));
        _origins.Push(_origin);
        _origin = r.position;
    }

    public void PopClip()
    {
        if (_origins.Count == 0) return;
        GUI.EndGroup();
        _origin = _origins.Pop();
    }

    public void PushAlpha(float a)
    {
        _alphas.Push(_alpha);
        _alpha *= a;
    }

    public void PopAlpha()
    {
        if (_alphas.Count > 0) _alpha = _alphas.Pop();
    }

    #endregion

    #region Misc

    public void Tooltip(Rect r, string text) => TooltipHandler.TipRegion(Local(r), text);

    public void PlaySound(UiSound sound)
    {
        var def = sound switch
        {
            UiSound.Click => SoundDefOf.Click,
            UiSound.Reject => SoundDefOf.ClickReject,
            UiSound.CheckboxOn => SoundDefOf.Checkbox_TurnedOn,
            UiSound.CheckboxOff => SoundDefOf.Checkbox_TurnedOff,
            UiSound.TabSelect => SoundDefOf.RowTabSelect,
            UiSound.MenuOpen => SoundDefOf.FloatMenu_Open,
            UiSound.Tick => SoundDefOf.Tick_High,
            UiSound.DragSlider => SoundDefOf.DragSlider,
            _ => null,
        };
        def?.PlayOneShotOnCamera();
    }

    public bool Native(Rect r, Rect clip, Action<Rect> draw, bool enabled, string? owner = null)
    {
        var e = Event.current;
        var type = e.type;
        bool wasUsed = type == EventType.Used;
        // 1.7 text fields ignore GUI.enabled and take any click. So a disabled leaf (e.g. under a popover) sees
        // mouse events as Ignore. It still runs, to keep IMGUI control IDs in step
        bool hide = !enabled && type is EventType.MouseDown or EventType.MouseUp or EventType.MouseDrag or EventType.ScrollWheel;
        if (hide) e.type = EventType.Ignore;
        // Own clip group, coords relative to the clip origin, vanilla default globals
        PushClip(clip);
        try
        {
            using var gui = Temporary.Enabled(GUI.enabled && enabled)
                .Color(A(Color.white)).Background(Color.white)
                .Font(GameFont.Small).Anchor(TextAnchor.UpperLeft).Wrap(true);
            var before = RimactDiagnostics.CheckGuiState ? GuiSnapshot.Capture() : default;
            draw(Local(r));
            if (RimactDiagnostics.CheckGuiState) ReportLeftovers(before, GuiSnapshot.Capture(), owner);
        }
        finally
        {
            PopClip();
            if (hide && e.type == EventType.Ignore) e.type = type;
        }
        return !wasUsed && e.type == EventType.Used;
    }

    public string TextField(Rect r, string text, bool multiline, string? controlName, FieldChrome? chrome = null)
    {
        Poison();
        text ??= "";
        if (multiline && controlName != null && GUI.GetNameOfFocusedControl() == controlName) _focusedArea = controlName;
        if (chrome is null)
        {
            if (controlName != null) GUI.SetNextControlName(controlName);
            return multiline ? Widgets.TextArea(r, text) : Widgets.TextField(r, text);
        }
        // r is already in the Native leaf's group coords, so no Local()
        if (Event.current.type == EventType.Repaint)
        {
            bool focused = controlName != null && GUI.GetNameOfFocusedControl() == controlName;
            FillRoundedRaw(r, chrome.Bg, chrome.Radius);
            if (chrome.Bevel is { } bevel && r.width > 2f && r.height > 2f)
                DrawBevelRaw(new Rect(r.x + 1f, r.y + 1f, r.width - 2f, r.height - 2f), bevel.Light, bevel.Dark, bevel.Width);
            StrokeRoundedRaw(r, focused ? chrome.FocusBorder : chrome.Border, 1f, chrome.Radius);
        }
        float padY = multiline ? 3f : 0f;
        var inner = new Rect(r.x + chrome.PadX, r.y + padY, Mathf.Max(0f, r.width - chrome.PadX * 2), Mathf.Max(0f, r.height - padY * 2));
        // Keep vanilla's style: in 1.7 a text area whose style has no background is read-only.
        // A clear GUI.backgroundColor hides its box. GUI.color tints the text and the chrome above may have changed it
        using var gui = Temporary.Background(Color.clear).Color(A(Color.white));
        if (controlName != null) GUI.SetNextControlName(controlName);
        return multiline ? GUI.TextArea(inner, text, Text.textAreaStyles[1]) : GUI.TextField(inner, text, Text.textFieldStyles[1]);
    }

    private string? _focusedArea;

    // Focus is in a multiline TextField, where Enter types a newline
    public bool TextAreaFocused => _focusedArea != null && GUIUtility.keyboardControl != 0 && GUI.GetNameOfFocusedControl() == _focusedArea;

    public void FocusControl(string controlName) => GUI.FocusControl(controlName);

    public string? FocusedControl
    {
        get
        {
            var name = GUI.GetNameOfFocusedControl();
            return string.IsNullOrEmpty(name) ? null : name;
        }
    }

    public void ClearFocus()
    {
        GUI.FocusControl(null);
        GUIUtility.keyboardControl = 0;
    }

    public void OpenMenu(IReadOnlyList<MenuItem> items)
    {
        var options = new List<FloatMenuOption>(items.Count);
        foreach (var it in items)
        {
            var action = it.Disabled ? null : it.OnSelect;
            var opt = it.Icon switch
            {
                { Def: ThingDef thingDef } => new FloatMenuOption(it.Label, action, thingDef),
                { Thing: { } thing } => new FloatMenuOption(it.Label, action, thing, Color.white),
                { Texture: { } tex } => new FloatMenuOption(it.Label, action, tex, Color.white),
                { Builtin: not BuiltinIcon.None } i when Resolve(i.Builtin) is { } tex => new FloatMenuOption(it.Label, action, tex, Color.white),
                _ => new FloatMenuOption(it.Label, action),
            };
            if (it.Tooltip != null) opt.tooltip = new TipSignal(it.Tooltip);
            options.Add(opt);
        }
        if (options.Count == 0) options.Add(new FloatMenuOption("-", null));
        Find.WindowStack.Add(new FloatMenu(options));
    }

    public IWindowHandle OpenWindow(WindowRequest request)
    {
        var w = new RimactWindow(request);
        Find.WindowStack.Add(w);
        return w;
    }

    public void LogError(string message) => Log.Error(message);

    public bool IsMouseHeld(int button) => UnityEngine.Input.GetMouseButton(button);

    public float RealTime => Time.realtimeSinceStartup;
    public int FrameId => Time.frameCount;

    #endregion
}
