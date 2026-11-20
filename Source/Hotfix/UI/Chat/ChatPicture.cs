using System;
using System.Threading;
using System.Threading.Tasks;
using Rimact;
using UnityEngine;
using Verse;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Hotfix;

// 채팅에 작게 보여 주는 그림. 누르면 그림만 있는 창이 뜬다. Label은 그림을 읽지 못했을 때 대신 보여 주는 글
internal sealed record ChatPicture(string Path, string? Label = null) : UIComponent
{
    // 화면에 그리는 크기의 한도
    private const float MaxWidth = 200f;
    private const float MaxHeight = 120f;
    // UI 배율이 높아도 흐려지지 않게 그리는 크기의 두 배까지 읽는다
    private const int MaxEdge = 400;

    protected internal override Node Build()
    {
        var t = UseTheme();
        var path = Path;
        var texture = UseAsync(ct => Load(path, ct), path).Value;
        UseEffect(() => () =>
        {
            if (texture is not null)
                UnityEngine.Object.Destroy(texture);
        }, texture);

        if (texture is null)
        {
            return Row(
                Image(Glyph.Image).Size(14f, 14f).Tint(t.MutedForeground),
                Text(Label is { Length: > 0 } ? Markdown.Literal(Label) : "Hotfix_ChatImage".Translate().ToString())
                    .TextSize(t.TextCaption).Color(t.MutedForeground)
            ).Gap(t.Space(1)).Align(Align.Center);
        }
        var scale = Mathf.Min(1f, MaxWidth / texture.width, MaxHeight / texture.height);
        return Row(Image(texture).Size(texture.width * scale, texture.height * scale))
            .Padding(1f, 1f).Border(t.Border).HoverOverlay(t.HoverOverlay).OnClick(() => ImageWindow.Open(path)).AlignSelf(Align.Start);
    }

    private static async Task<Texture2D?> Load(string path, CancellationToken ct)
    {
        var texture = await ImageFile.LoadTexture(path, MaxEdge);
        if (!ct.IsCancellationRequested)
            return texture;
        // 읽는 동안 화면에서 빠졌다
        UnityEngine.Object.Destroy(texture);
        return null;
    }
}

// 그림 하나만 보여 주는 창
internal sealed class ImageWindow : Window
{
    // 작은 그림도 이만큼은 키워서 보여 준다
    private const float MinSize = 240f;
    private const float ScreenMargin = 40f;

    private readonly Texture2D _texture;

    private ImageWindow(Texture2D texture)
    {
        _texture = texture;
        // 채팅 창보다 위에 뜬다
        layer = WindowLayer.Super;
        doCloseX = true;
        closeOnClickedOutside = true;
        absorbInputAroundWindow = true;
    }

    public static async void Open(string path)
    {
        try
        {
            Find.WindowStack.Add(new ImageWindow(await ImageFile.LoadTexture(path, Mathf.Max(Screen.width, Screen.height))));
        }
        catch (Exception e)
        {
            Logger.Warning($"Failed to open the image: {e.Message}");
        }
    }

    public override Vector2 InitialSize
    {
        get
        {
            // 화면 픽셀과 1:1로 보여 주되 화면을 넘으면 줄인다
            var size = new Vector2(_texture.width, _texture.height) / Prefs.UIScale;
            var room = new Vector2(UI.screenWidth, UI.screenHeight) - Vector2.one * (ScreenMargin + Margin) * 2f;
            size *= Mathf.Min(1f, room.x / size.x, room.y / size.y);
            return Vector2.Max(size, Vector2.one * MinSize) + Vector2.one * Margin * 2f;
        }
    }

    public override void DoWindowContents(Rect inRect) => GUI.DrawTexture(inRect, _texture, ScaleMode.ScaleToFit);

    public override void PostClose()
    {
        base.PostClose();
        UnityEngine.Object.Destroy(_texture);
    }
}
