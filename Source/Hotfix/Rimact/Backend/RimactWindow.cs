using System;
using Rimact.Components;
using UnityEngine;
using Verse;

namespace Rimact;

/// <summary>Window whose contents are a Rimact tree. Closing it unmounts the tree.</summary>
/// <remarks>Inside a component, use Dialog instead. Enter and Esc go to the content first (UseAcceptKey, UseCancelKey).</remarks>
internal sealed class RimactWindow : Window, IWindowHandle
{
    private const float Padding = 18f, TitleHeight = 30f, ScreenMargin = 20f;

    private readonly WindowRequest _request;
    private WindowOptions _options;
    private Vector2 _appliedSize;
    private bool _closed;

    public RimactWindow(Node root, WindowOptions? options = null)
        : this(new WindowRequest(new Host(root, ImguiBackend.Instance), options ?? new WindowOptions())) { }

    internal RimactWindow(WindowRequest request)
    {
        _request = request;
        _options = request.Options;
        Host = new RimactHost(request.Content) { CloseRequested = () => Close() };
        doWindowBackground = false;
        // DoWindowContents draws it, above the chrome
        doCloseX = false;
        closeOnAccept = false;
        closeOnCancel = false;
        // Vanilla gives Enter and Esc to the topmost window that asks. Ask, so a window below never gets them
        forceCatchAcceptAndCancelEventEvenIfUnfocused = true;
        onlyOneOfTypeAllowed = false;
        ApplyFlags();
    }

    /// <example><code>RimactWindow.Open(new MergePanel(remote), new WindowOptions { Title = "Merge", Size = new(1000, 700) });</code></example>
    public static RimactWindow Open(Node content, WindowOptions? options = null)
    {
        var w = new RimactWindow(content, options);
        Find.WindowStack.Add(w);
        return w;
    }

    /// <summary>Confirm/cancel window sized to its text. Both buttons close it.</summary>
    /// <example><code>RimactWindow.Confirm("Delete preset?", "This cannot be undone.", DeletePreset, destructive: true);</code></example>
    public static RimactWindow Confirm(string title, string description, Action? onConfirm, bool destructive = false,
        string? confirmLabel = null, string? cancelLabel = null) =>
        Open(new AlertContent(title, description)
        {
            OnConfirm = onConfirm,
            Destructive = destructive,
            ConfirmLabel = confirmLabel,
            CancelLabel = cancelLabel,
        }, AlertContent.WindowOptions);

    public RimactHost Host { get; }

    public WindowOptions Options => _options;

    /// <summary>A size change keeps the window's top edge and horizontal center.</summary>
    public void Update(WindowOptions options)
    {
        _options = options;
        ApplyFlags();
    }

    private void ApplyFlags()
    {
        absorbInputAroundWindow = _options.Modal;
        draggable = _options.Draggable;
        closeOnClickedOutside = _options.CloseOnClickedOutside;
        forcePause = _options.Pause;
    }

    #region Size

    public override Vector2 InitialSize => DesiredSize();

    protected override float Margin => 0f;

    private float ChromeHeight => Padding * 2 + (string.IsNullOrEmpty(_options.Title) ? 0f : TitleHeight);

    private Vector2 DesiredSize()
    {
        var size = _options.Size;
        if (_options.AutoHeight)
        {
            // Measuring text sets Text.Font and Text.WordWrap, and this runs outside the host's draw
            using var gui = Temporary.Gui();
            float content = _request.Content.ContentHeight(Mathf.Max(0f, size.x - Padding * 2));
            size.y = Mathf.Min(content + ChromeHeight, UI.screenHeight - ScreenMargin * 2);
        }
        return new Vector2(Mathf.Round(size.x), Mathf.Round(size.y));
    }

    protected override void SetInitialSizeAndPosition()
    {
        base.SetInitialSizeAndPosition();
        _appliedSize = windowRect.size;
    }

    public override void WindowOnGUI()
    {
        // Before GUI.Window, or the rect it returns overwrites the new size
        var size = DesiredSize();
        if (size != _appliedSize)
        {
            _appliedSize = size;
            var r = new Rect(windowRect.center.x - size.x / 2f, windowRect.y, size.x, size.y);
            r.x = Mathf.Clamp(r.x, 0f, Mathf.Max(0f, UI.screenWidth - r.width));
            r.y = Mathf.Clamp(r.y, 0f, Mathf.Max(0f, UI.screenHeight - r.height));
            windowRect = r.Rounded();
        }
        base.WindowOnGUI();
    }

    #endregion

    #region Drawing

    public override void DoWindowContents(Rect inRect)
    {
        if (Event.current.type == EventType.Repaint) DrawChrome(inRect);
        var content = inRect.ContractedBy(Padding);
        if (!string.IsNullOrEmpty(_options.Title)) content.yMin += TitleHeight;
        Host.Draw(content);
        if (_options.CloseButton && Widgets.CloseButtonFor(inRect)) Close();
    }

    private void DrawChrome(Rect r)
    {
        var t = Theme.Default;
        var b = ImguiBackend.Instance;
        using var gui = Temporary.Gui();
        b.BeginFrame();
        try
        {
            b.FillRect(r, t.Background);
            var bevel = t.Raised(t.Background, t.BevelWidthSm);
            b.DrawBevel(r.ContractedBy(1f), bevel.Light, bevel.Dark, bevel.Width);
            b.DrawBorder(r, t.WindowBorder, 1f);
            if (!string.IsNullOrEmpty(_options.Title))
                b.DrawText(new Rect(r.x + Padding, r.y + 12f, r.width - Padding * 2 - 24f, TitleHeight), _options.Title!,
                    new TextStyle(FontSize.Small, TextAlign.MiddleLeft, false, true, t.TextTitle, true), t.Foreground);
        }
        finally
        {
            b.EndFrame();
        }
    }

    #endregion

    #region Keys

    public override void OnAcceptKeyPressed()
    {
        if (!ImguiBackend.Instance.TextAreaFocused && Host.Core.Accept()) Event.current.Use();
    }

    public override void OnCancelKeyPressed()
    {
        if (Host.Core.Cancel()) Event.current.Use();
    }

    #endregion

    #region Lifetime

    void IWindowHandle.Close()
    {
        if (!_closed) Close(doCloseSound: false);
    }

    public override void PostClose()
    {
        base.PostClose();
        if (_closed) return;
        _closed = true;
        Host.Dispose();
        _request.OnClosed?.Invoke();
    }

    #endregion
}
