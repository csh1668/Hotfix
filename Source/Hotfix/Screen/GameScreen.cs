using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityStubs.Rendering;
using Verse;

namespace Hotfix;

internal static class GameScreen
{
    public static Task<ChatImage> Capture(string folder)
    {
        var done = new TaskCompletionSource<ChatImage>(TaskCreationOptions.RunContinuationsAsynchronously);
        Current.Root.StartCoroutine(Run(folder, done));
        return done.Task;
    }

    private static IEnumerator Run(string folder, TaskCompletionSource<ChatImage> done)
    {
        ChatOverlay.Hidden = true;
        do
            yield return new WaitForEndOfFrame();
        while (ChatOverlay.DrawnFrame == Time.frameCount);
        try
        {
            if (ScreenCaptureBackend.Current is not { } backend || !backend.TryReadScreen(out var pixels, out var width, out var height))
                throw new InvalidOperationException("The screen could not be read on this device.");
            (pixels, width, height) = Images.Fit(pixels, width, height, ImageFile.MaxEdge);
            done.SetResult(ImageFile.Save(Images.FlipRows(pixels, width, height), width, height, folder));
        }
        catch (Exception e)
        {
            done.SetException(e);
        }
        finally
        {
            ChatOverlay.Hidden = false;
        }
    }
}
