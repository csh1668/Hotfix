using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Verse;
using DdsAlphaMode = Waylith.Graphics.Textures.DdsAlphaMode;
using DdsFile = Waylith.Graphics.Textures.DdsFile;
using LoadedTextureFormat = Waylith.Rendering.LoadedTextureFormat;
using TexturePixelData = Waylith.Graphics.Textures.TexturePixelData;

namespace Hotfix;

internal static class ImageFile
{
    public const int MaxEdge = 1600;
    private const int Quality = 80;
    private const int Backdrop = 128;

    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".psd", ".dds"];

    public static bool IsImage(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static ChatImage Save(byte[] pixels, int width, int height, string folder)
    {
        var jpeg = ImageConversion.EncodeArrayToJPG(pixels, GraphicsFormat.R8G8B8A8_UNorm, (uint)width, (uint)height, (uint)(width * 4), Quality);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{DateTime.Now:HHmmss-fff}-{Guid.NewGuid().ToString("N")[..4]}.jpg");
        File.WriteAllBytes(path, jpeg);
        return new ChatImage(path, "image/jpeg");
    }

    public static async Task<(ChatImage Image, int Width, int Height, bool Transparent)> Load(string path, string folder)
    {
        var (pixels, width, height, premultiplied) = await Decode(path);
        return await Task.Run(() => Finish(pixels, width, height, premultiplied, folder));
    }

    public static async Task<Texture2D> LoadTexture(string path, int maxEdge)
    {
        var source = await Decode(path);
        var (pixels, width, height) = await Task.Run(() => Images.Fit(source.Pixels, source.Width, source.Height, maxEdge));
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.SetPixelData(pixels, 0);
        texture.Apply(false);
        return texture;
    }

    private static async Task<(byte[] Pixels, int Width, int Height, bool Premultiplied)> Decode(string path)
    {
        Texture2D? texture = null;
        try
        {
            return await Task.Run(() =>
            {
                if (ZipFiles.TrySplit(path, out var zip, out var inside) && ZipFiles.Find(zip, inside) is { IsTexture: true } packed)
                    return Packed(ZipFiles.ReadTexture(zip, packed));

                var data = ToolFiles.ReadAllBytes(path);
                if (Path.GetExtension(path).Equals(".dds", StringComparison.OrdinalIgnoreCase))
                {
                    texture = Dds(data);
                }
                else
                {
                    texture = new Texture2D(2, 2);
                    if (!texture.LoadImage(data))
                        throw new InvalidDataException("The image could not be decoded.");
                }
                return (MemoryMarshal.AsBytes(texture.GetPixels32().AsSpan()).ToArray(), texture.width, texture.height, false);
            });
        }
        finally
        {
            if (texture is not null)
                UnityEngine.Object.Destroy(texture);
        }
    }

    private static (byte[], int, int, bool) Packed(byte[] dds)
    {
        if (!DdsFile.TryDecode(dds, out var loaded, out var alpha))
            throw new InvalidDataException("The packed texture could not be decoded.");
        if (loaded.Format is not (LoadedTextureFormat.Rgba32 or LoadedTextureFormat.Dxt1 or LoadedTextureFormat.Bc7
            or LoadedTextureFormat.Bc4 or LoadedTextureFormat.Bc5))
            throw new NotSupportedException($"A packed texture in the {loaded.Format} format cannot be shown.");
        var data = new TexturePixelData(loaded.Width, loaded.Height, loaded.Format, false, loaded.MipCount, false);
        data.Replace(loaded.Width, loaded.Height, loaded.Format, loaded.Levels, loaded.MipCount, false);
        data.Swizzle = loaded.Swizzle;
        var pixels = Images.FlipRows(MemoryMarshal.AsBytes(data.GetPixels32().AsSpan()).ToArray(), loaded.Width, loaded.Height);
        return (pixels, loaded.Width, loaded.Height, alpha == DdsAlphaMode.Premultiplied);
    }

    private static (ChatImage, int, int, bool) Finish(byte[] source, int sourceWidth, int sourceHeight, bool premultiplied, string folder)
    {
        var (pixels, width, height) = Images.Fit(source, sourceWidth, sourceHeight, MaxEdge);
        var transparent = false;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var alpha = pixels[i + 3];
            if (alpha == 255)
                continue;
            transparent = true;
            for (var c = 0; c < 3; c++)
                pixels[i + c] = (byte)Math.Min(255, (pixels[i + c] * (premultiplied ? 255 : alpha) + Backdrop * (255 - alpha)) / 255);
            pixels[i + 3] = 255;
        }
        return (Save(pixels, width, height, folder), sourceWidth, sourceHeight, transparent);
    }

    private static Texture2D Dds(byte[] data)
    {
        if (data.Length < 128)
            throw new InvalidDataException("The file is too short to be a DDS texture.");
        var header = MemoryMarshal.Read<DdsHeader>(data);
        if (header.Magic != DdsHeader.RequiredMagic || header.Size != DdsHeader.RequiredSize)
            throw new InvalidDataException("The file is not a DDS texture.");
        var format = header.PixelFormat.ToTextureFormat();
        var body = data[(format == TextureFormat.BC7 ? 148 : 128)..];
        if (header.PixelFormat.IsBgr888 && !header.PixelFormat.IsCompressed)
        {
            var step = (int)header.PixelFormat.RGBBitCount / 8;
            for (var i = 0; i + 2 < body.Length; i += step)
                (body[i], body[i + 2]) = (body[i + 2], body[i]);
        }
        var mips = (header.Flags & DdsHeaderFlags.MipMapCount) != 0 && header.MipMapCount > 1 ? (int)header.MipMapCount : 1;
        var texture = new Texture2D((int)header.Width, (int)header.Height, format, mips, false, true);
        texture.LoadRawTextureData(body);
        return texture;
    }
}
