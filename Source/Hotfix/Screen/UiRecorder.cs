using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using UnityStubs.Gui;
using Verse;

namespace Hotfix;

public static class UiRecorder
{
    private static readonly List<UiWindow> Windows = [];
    private static readonly List<(Rect Clip, int[] Path)> Groups = [];
    private static readonly HashSet<(Vector2 At, float Width, string Text)> Labels = [];
    private static TaskCompletionSource<List<UiWindow>>? _pending;
    private static UiWindow _target = null!;
    private static UiWindow _screen = null!;
    private static IGuiBackend? _backend;
    private static Type? _proxy;
    private static Dictionary<Texture, string>? _defIcons;
    private static int _groupCount;
    private static int _muted;
    private static bool _patched;
    private static bool _recording;

    public static Task<List<UiWindow>> Capture()
    {
        if (!_patched)
        {
            _patched = true;
            Patch();
        }
        _pending ??= new TaskCompletionSource<List<UiWindow>>(TaskCreationOptions.RunContinuationsAsynchronously);
        return _pending.Task;
    }

    #region Frame

    private static void Patch()
    {
        var harmony = HotfixMod.Harmony;
        harmony.Patch(AccessTools.Method(typeof(Root), nameof(Root.OnGUI)), new HarmonyMethod(typeof(UiRecorder), nameof(Root_OnGUI_Prefix)) { priority = Priority.First });
        harmony.Patch(AccessTools.Method(typeof(Window), nameof(Window.InnerWindowOnGUI)), new HarmonyMethod(typeof(UiRecorder), nameof(Window_InnerWindowOnGUI_Prefix)),
            new HarmonyMethod(typeof(UiRecorder), nameof(Window_InnerWindowOnGUI_Postfix)));
        harmony.Patch(AccessTools.Method(typeof(GUI), "ButtonImpl"), new HarmonyMethod(typeof(UiRecorder), nameof(Button)));
        harmony.Patch(AccessTools.Method(typeof(GUI), "TextFieldImpl"), new HarmonyMethod(typeof(UiRecorder), nameof(GUI_TextFieldImpl_Prefix)));
        Control(harmony, nameof(Widgets.ButtonInvisibleDraggable), nameof(Button));
        harmony.Patch(AccessTools.Method(typeof(ThingOverlays), nameof(ThingOverlays.ThingOverlaysOnGUI)), new HarmonyMethod(typeof(UiRecorder), nameof(Mute)),
            new HarmonyMethod(typeof(UiRecorder), nameof(Unmute)));
        harmony.Patch(AccessTools.Method(typeof(Command), nameof(Command.GizmoOnGUIInt)), new HarmonyMethod(typeof(UiRecorder), nameof(Command_GizmoOnGUIInt_Prefix)),
            new HarmonyMethod(typeof(UiRecorder), nameof(Unmute)));
        MethodInfo[] regions =
        [
            AccessTools.Method(typeof(GizmoGridDrawer), nameof(GizmoGridDrawer.DrawGizmoGrid)),
            AccessTools.Method(typeof(GlobalControls), nameof(GlobalControls.GlobalControlsOnGUI)),
            AccessTools.Method(typeof(ResourceReadout), nameof(ResourceReadout.ResourceReadoutOnGUI)),
            AccessTools.Method(typeof(ColonistBar), nameof(ColonistBar.ColonistBarOnGUI)),
            AccessTools.Method(typeof(AlertsReadout), nameof(AlertsReadout.AlertsReadoutOnGUI)),
            AccessTools.Method(typeof(LetterStack), nameof(LetterStack.LettersOnGUI)),
            AccessTools.Method(typeof(MainButtonsRoot), nameof(MainButtonsRoot.MainButtonsOnGUI)),
        ];
        foreach (var region in regions)
            harmony.Patch(region, new HarmonyMethod(typeof(UiRecorder), nameof(RegionBegin)), new HarmonyMethod(typeof(UiRecorder), nameof(RegionEnd)));
        Control(harmony, nameof(Widgets.CheckboxLabeled), nameof(Widgets_CheckboxLabeled_Prefix));
        // 좌표를 받는 쪽 하나만. 나머지 Checkbox는 이쪽을 부른다
        if (AccessTools.Method(typeof(Widgets), nameof(Widgets.Checkbox), [typeof(float), typeof(float), typeof(bool).MakeByRefType(),
                typeof(float), typeof(bool), typeof(bool), typeof(Texture2D), typeof(Texture2D)]) is { } checkbox)
            harmony.Patch(checkbox, new HarmonyMethod(typeof(UiRecorder), nameof(Widgets_Checkbox_Prefix)));

        static void Control(Harmony harmony, string name, string prefix)
        {
            // Rect로 시작하는 오버로드 전부
            foreach (var method in AccessTools.GetDeclaredMethods(typeof(Widgets))
                         .Where(method => method.Name == name && !method.IsGenericMethod && method.GetParameters().FirstOrDefault()?.ParameterType == typeof(Rect)))
                harmony.Patch(method, new HarmonyMethod(typeof(UiRecorder), prefix));
        }
    }

    private static void Root_OnGUI_Prefix()
    {
        // 창의 내용은 엔진이 OnGUI가 끝난 뒤에 그린다. 그래서 그리는 패스의 기록은 다음 OnGUI가 시작될 때 끝낸다
        if (_recording)
        {
            End();
            return;
        }
        if (_pending is null || Event.current.type != EventType.Repaint || GuiBackend.Current is not { } backend)
            return;
        Windows.Clear();
        Groups.Clear();
        Labels.Clear();
        Groups.Add((new Rect(0f, 0f, Screen.width, Screen.height), []));
        _groupCount = 0;
        _muted = 0;
        _screen = _target = new UiWindow("", "", Groups[0].Clip, []);
        _backend = backend;
        // 엔진의 인터페이스가 바뀌어도 따라가도록, 감싸는 형식은 실행 중에 인터페이스에서 만든다
        _proxy ??= ProxyType();
        var proxy = (IGuiBackend)Activator.CreateInstance(_proxy)!;
        _proxy.GetField("Inner")!.SetValue(proxy, backend);
        GuiBackend.Current = proxy;
        _recording = true;
    }

    private static void End()
    {
        _recording = false;
        GuiBackend.Current = _backend;
        Windows.Add(_screen);
        var pending = _pending!;
        _pending = null;
        pending.TrySetResult(Windows.Where(window => window.Items.Count > 0).ToList());
    }

    private static void Window_InnerWindowOnGUI_Prefix(Window __instance)
    {
        // 화면 요소를 담는 임시 창은 따로 묶지 않고, 자기 채팅 창은 적지 않는다
        if (!_recording || __instance is ImmediateWindow)
            return;
        _target = new UiWindow(__instance.GetType().Name, __instance.optionalTitle ?? "", __instance.windowRect, []);
        if (!ChatOverlay.Owns(__instance))
            Windows.Add(_target);
    }

    private static void Window_InnerWindowOnGUI_Postfix()
    {
        if (_recording)
            _target = _screen;
    }

    #endregion

    #region Records

    private static void Add(string role, Rect local, string text, bool check = false)
    {
        if (_muted > 0)
            return;
        var (clip, path) = Groups[^1];
        // 보이는 부분만 남긴다. 스크롤 영역 밖으로 밀려나 보이지 않는 것은 빠진다
        var box = Intersect(GUIUtility.GUIToScreenRect(local), clip);
        if (box.width > 0f && box.height > 0f)
            _target.Items.Add(new UiItem(role, box, text, check, path));
    }

    private static Rect Intersect(Rect a, Rect b) =>
        Rect.MinMaxRect(Mathf.Max(a.xMin, b.xMin), Mathf.Max(a.yMin, b.yMin), Mathf.Min(a.xMax, b.xMax), Mathf.Min(a.yMax, b.yMax));

    private static void RegionBegin()
    {
        if (_recording)
            Groups.Add((Groups[^1].Clip, [.. Groups[^1].Path, ++_groupCount]));
    }

    private static void RegionEnd()
    {
        if (_recording)
            OnPop();
    }

    private static void Button(Rect __0)
    {
        if (_recording)
            Add("button", __0, "");
    }

    private static void Command_GizmoOnGUIInt_Prefix(Command __instance, Rect __0)
    {
        if (_recording)
            Add("button", __0, __instance.LabelCap ?? "");
        _muted++;
    }

    private static void Mute() => _muted++;

    private static void Unmute() => _muted--;

    private static void Widgets_CheckboxLabeled_Prefix(Rect __0, bool __2)
    {
        if (_recording)
            Add("checkbox", __0, "", __2);
    }

    private static void Widgets_Checkbox_Prefix(float __0, float __1, bool __2, float __3)
    {
        if (_recording)
            Add("checkbox", new Rect(__0, __1, __3, __3), "", __2);
    }

    private static void GUI_TextFieldImpl_Prefix(Rect __0, string __1)
    {
        if (_recording)
            Add("textbox", __0, __1 ?? "");
    }

    // 아래는 감싼 백엔드가 원래 호출보다 먼저 부른다
    public static void OnDrawText(Rect rect, string text, int fontIndex, float fontSize, FontStyle fontStyle, TextAnchor anchor, bool wordWrap)
    {
        if (string.IsNullOrWhiteSpace(text) || !Labels.Add((GUIUtility.GUIToScreenPoint(new Vector2(rect.x, rect.y)), rect.width, text)))
            return;
        // 넘겨받은 영역은 글보다 훨씬 클 수 있어서, 글이 실제로 놓이는 자리를 구한다
        var size = _backend!.MeasureText(fontIndex, fontSize, fontStyle, text, wordWrap ? rect.width : -1f);
        var width = Mathf.Min(size.x, rect.width);
        var height = Mathf.Min(size.y, rect.height);
        Add("text", new Rect(rect.x + (rect.width - width) * ((int)anchor % 3) / 2f, rect.y + (rect.height - height) * ((int)anchor / 3) / 2f, width, height), text);
    }

    // 아이콘만 한 크기로 통째로 그린, 이름 있는 텍스처만 적는다. 색만 칠하는 1픽셀짜리와 그림의 일부만 잘라 그리는 틀 조각은 뺀다
    public static void OnDrawTexture(Rect rect, Texture image)
    {
        if (image == null || image.width <= 4 || image.height <= 4 || rect.width is < 8f or > 96f || rect.height is < 8f or > 96f)
            return;
        var name = IconName(image);
        if (!string.IsNullOrEmpty(name))
            Add("image", rect, name);
    }

    public static void OnDrawTexturePart(Rect rect, Texture image, Rect texCoords)
    {
        if (Mathf.Abs(texCoords.width) >= 0.99f && Mathf.Abs(texCoords.height) >= 0.99f)
            OnDrawTexture(rect, image);
    }

    // 물건의 아이콘이면 그 물건의 이름, 아니면 텍스처 파일의 이름
    private static string IconName(Texture image)
    {
        if (_defIcons is null)
        {
            _defIcons = [];
            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def.uiIcon != null && def.uiIcon != BaseContent.BadTex)
                    _defIcons.TryAdd(def.uiIcon, def.LabelCap);
            }
        }
        return _defIcons.TryGetValue(image, out var label) ? label : image.name;
    }

    public static void OnPush(Rect rect)
    {
        var (clip, path) = Groups[^1];
        Groups.Add((Intersect(GUIUtility.GUIToScreenRect(rect), clip), [.. path, ++_groupCount]));
    }

    public static void OnPop()
    {
        if (Groups.Count > 1)
            Groups.RemoveAt(Groups.Count - 1);
    }

    #endregion

    #region Proxy

    private static readonly Dictionary<string, string> Hooks = new()
    {
        ["DrawText"] = nameof(OnDrawText),
        ["DrawTexture"] = nameof(OnDrawTexture),
        ["DrawTextureWithTexCoords"] = nameof(OnDrawTexturePart),
        ["DrawTextureWithMaterial"] = nameof(OnDrawTexturePart),
        ["PushGroup"] = nameof(OnPush),
        ["PushScrollView"] = nameof(OnPush),
        ["BeginWindowContent"] = nameof(OnPush),
        ["PopGroup"] = nameof(OnPop),
        ["EndWindowContent"] = nameof(OnPop),
    };

    // IGuiBackend의 모든 멤버를 Inner에게 그대로 넘기는 형식. Hooks에 있는 멤버는 넘기기 전에 위의 함수를 먼저 부른다
    private static Type ProxyType()
    {
        var module = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("HotfixGuiProxy"), AssemblyBuilderAccess.Run).DefineDynamicModule("Main");
        var type = module.DefineType("GuiBackendProxy", TypeAttributes.Public | TypeAttributes.Sealed, typeof(object), [typeof(IGuiBackend)]);
        var inner = type.DefineField("Inner", typeof(IGuiBackend), FieldAttributes.Public);
        foreach (var method in typeof(IGuiBackend).GetMethods())
        {
            var parameters = method.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
            var body = type.DefineMethod(method.Name,
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
                method.ReturnType, parameters);
            var il = body.GetILGenerator();
            if (Hooks.TryGetValue(method.Name, out var name))
            {
                var hook = typeof(UiRecorder).GetMethod(name)!;
                for (var i = 0; i < hook.GetParameters().Length; i++)
                    il.Emit(OpCodes.Ldarg, i + 1);
                il.Emit(OpCodes.Call, hook);
            }
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, inner);
            for (var i = 0; i < parameters.Length; i++)
                il.Emit(OpCodes.Ldarg, i + 1);
            il.Emit(OpCodes.Callvirt, method);
            il.Emit(OpCodes.Ret);
            type.DefineMethodOverride(body, method);
        }
        return type.CreateType();
    }

    #endregion
}
