using System;
using HarmonyLib;
using Rimact;
using UnityEngine;
using Verse;

namespace Hotfix;

[StaticConstructorOnStartup]
internal static class ChatOverlay
{
    private static readonly RimactHost Host = new(new ChatUI(ChatRuntime.Sessions, Close));
    private static readonly ChatWindow Window = new();
    private static Rect _rect;
    private static bool _open;
    private static bool _hidden;
    private static bool _focusInput;

    static ChatOverlay()
    {
        HotfixMod.Harmony.Patch(AccessTools.Method(typeof(Root), nameof(Root.OnGUI)),
            postfix: new HarmonyMethod(typeof(ChatOverlay), nameof(Root_OnGUI_Postfix)));
    }

    private static bool InputFocused =>
        GUI.GetNameOfFocusedControl() is Composer.InputName or ChatHistory.SearchName or QuestionForm.OtherName;

    // 화면을 찍는 동안 잠깐 숨긴다. 다시 보일 때 입력란에 포커스를 돌려준다
    public static bool Hidden
    {
        get => _hidden;
        set
        {
            _hidden = value;
            _focusInput |= !value;
        }
    }
    public static int DrawnFrame { get; private set; }

    // 닫혀 있으면 연다
    public static void Show()
    {
        if (!_open)
            Toggle();
        Hidden = false;
    }

    public static bool Owns(Window window) => window == Window;

    private static void Close()
    {
        _open = false;
        Find.WindowStack?.TryRemove(Window, false);
        if (Scribe.mode == LoadSaveMode.Inactive)
            HotfixMod.Settings.Write();
    }

    private static void Root_OnGUI_Postfix(Root __instance)
    {
        try
        {
            if (__instance.destroyed)
                return;
            if (ToggleKeyPressed())
            {
                Event.current.Use();
                Toggle();
            }
            if (!_open)
                return;

            var stack = __instance.uiRoot?.windows;
            if (Hidden)
                stack?.TryRemove(Window, false);
            else if (stack is null || LongEventHandler.ShouldWaitForEvent)
                DrawWithoutWindow();
            else if (!stack.IsOpen(Window))
                stack.Add(Window);
        }
        catch (Exception e)
        {
            Logger.Error($"Chat overlay failed: {e}", onceKey: nameof(ChatOverlay));
        }
    }

    // KeyBindingDef.KeyDownEvent는 창 스택이 없는 로딩 화면에서 NRE
    private static bool ToggleKeyPressed()
    {
        var e = Event.current;
        return e.type == EventType.KeyDown && e.keyCode != KeyCode.None
            && KeyPrefs.KeyPrefsData.keyPrefs.TryGetValue(HotfixDefOf.Hotfix_ToggleChat, out var keys)
            && (e.keyCode == keys.keyBindingA || e.keyCode == keys.keyBindingB);
    }

    private static void Toggle()
    {
        if (_open)
        {
            Close();
            return;
        }
        if (_rect.width <= 0f)
            _rect = new Rect(UI.screenWidth - 480f, 60f, 460f, Mathf.Min(700f, UI.screenHeight - 120f));
        _open = true;
        _focusInput = true;
        ChatRuntime.WarmUp();
    }

    private static void DrawWithoutWindow()
    {
        var e = Event.current;
        var rect = OnScreen(_rect);
        if (e.type == EventType.KeyDown)
        {
            if (e.keyCode is KeyCode.Return or KeyCode.KeypadEnter)
                OnAccept();
            else if (e.keyCode == KeyCode.Escape)
                OnCancel();
        }
        if (e.type == EventType.MouseDown && !rect.Contains(e.mousePosition))
            Unfocus();
        Draw(rect);
    }

    // Enter와 Esc는 입력란에 포커스가 있을 때만 가져온다. 무엇을 할지는 ChatUI가 정한다
    private static void OnAccept()
    {
        if (InputFocused && !Event.current.shift && Host.Core.Accept())
            Event.current.Use();
    }

    // 열린 목록을 닫거나 응답을 중단하고, 둘 다 아니면 입력란에서 포커스를 뗀다
    private static void OnCancel()
    {
        if (!InputFocused)
            return;
        if (!Host.Core.Cancel())
            GUI.FocusControl(null);
        Event.current.Use();
    }

    private static void Unfocus()
    {
        if (InputFocused)
            GUI.FocusControl(null);
    }

    private static void Draw(Rect rect)
    {
        Host.Draw(rect);
        DrawnFrame = Time.frameCount;
        if (!_focusInput)
            return;
        _focusInput = false;
        GUI.FocusControl(Composer.InputName);
    }

    private static Rect OnScreen(Rect rect)
    {
        rect.width = Mathf.Min(rect.width, UI.screenWidth);
        rect.height = Mathf.Min(rect.height, UI.screenHeight);
        rect.x = Mathf.Clamp(rect.x, 0f, UI.screenWidth - rect.width);
        rect.y = Mathf.Clamp(rect.y, 0f, UI.screenHeight - rect.height);
        return rect;
    }

    private sealed class ChatWindow : Window
    {
        public ChatWindow()
        {
            layer = WindowLayer.Super;
            doWindowBackground = false;
            closeOnAccept = false;
            closeOnCancel = false;
            preventCameraMotion = false;
            resizeable = true;
            resizer = new WindowResizer { minWindowSize = new Vector2(320f, 280f) };
            soundAppear = null;
            soundClose = null;
        }

        protected override float Margin => 0f;

        protected override void SetInitialSizeAndPosition() => windowRect = OnScreen(_rect);

        public override void WindowOnGUI()
        {
            // 입력란에 포커스가 있을 때만 Enter와 Esc를 가져오고, 아니면 아래 창으로 넘긴다
            forceCatchAcceptAndCancelEventEvenIfUnfocused = InputFocused;
            base.WindowOnGUI();
            _rect = windowRect;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Draw(inRect);
            GUI.DragWindow(new Rect(0f, 0f, inRect.width, ChatUI.HeaderHeight));
        }

        public override void OnAcceptKeyPressed() => OnAccept();

        public override void OnCancelKeyPressed() => OnCancel();

        public override void Notify_ClickOutsideWindow()
        {
            base.Notify_ClickOutsideWindow();
            Unfocus();
        }
    }
}
