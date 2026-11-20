using System;

namespace Hotfix;

internal static class Images
{
    public static byte[] FlipRows(byte[] pixels, int width, int height)
    {
        var row = width * 4;
        var flipped = new byte[pixels.Length];
        for (var y = 0; y < height; y++)
            Array.Copy(pixels, y * row, flipped, (height - 1 - y) * row, row);
        return flipped;
    }

    public static (byte[] Pixels, int Width, int Height) Fit(byte[] pixels, int width, int height, int maxEdge)
    {
        var scale = (double)maxEdge / Math.Max(width, height);
        if (scale >= 1)
            return (pixels, width, height);
        var w = Math.Max(1, (int)Math.Round(width * scale));
        var h = Math.Max(1, (int)Math.Round(height * scale));
        var result = new byte[w * h * 4];
        Span<int> sum = stackalloc int[4];
        for (var y = 0; y < h; y++)
        {
            var top = y * height / h;
            var bottom = Math.Max(top + 1, (y + 1) * height / h);
            for (var x = 0; x < w; x++)
            {
                var left = x * width / w;
                var right = Math.Max(left + 1, (x + 1) * width / w);
                sum.Clear();
                for (var sy = top; sy < bottom; sy++)
                {
                    for (var sx = left; sx < right; sx++)
                    {
                        var at = (sy * width + sx) * 4;
                        for (var c = 0; c < 4; c++)
                            sum[c] += pixels[at + c];
                    }
                }
                var count = (bottom - top) * (right - left);
                for (var c = 0; c < 4; c++)
                    result[(y * w + x) * 4 + c] = (byte)(sum[c] / count);
            }
        }
        return (result, w, h);
    }
}
