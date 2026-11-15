using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace NesLifter.Imaging;

/// <summary>
/// Writes multi-resolution Windows <c>.ico</c> files.
///
/// Windows picks the closest size from the icon directory, so a usable icon
/// needs at least 16, 32, 48 and 256. Sizes of 128 and above are stored as PNG
/// (which Windows Vista and later accept inside an ICO), smaller sizes as
/// classic 32-bpp DIBs for maximum compatibility with older shells and with
/// third-party tools that read the icon.
/// </summary>
public static class IcoWriter
{
    /// <summary>The size set written by default: every size Explorer asks for.</summary>
    public static readonly int[] DefaultSizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };

    /// <summary>
    /// Writes <paramref name="image"/> as an ICO with <paramref name="sizes"/>.
    /// Non-square images are letterboxed onto a transparent square rather than
    /// stretched, so aspect ratio survives.
    /// </summary>
    public static void Write(string imagePath, string icoPath, int[]? sizes = null)
    {
        using Image source = Image.FromFile(imagePath);
        Write(source, icoPath, sizes);
    }

    /// <summary>Writes <paramref name="source"/> as an ICO.</summary>
    public static void Write(Image source, string icoPath, int[]? sizes = null)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        sizes ??= DefaultSizes;

        List<(int Size, byte[] Data)> frames = new List<(int, byte[])>();

        foreach (int size in sizes)
        {
            using Bitmap square = Square(source, size);
            byte[] data = size >= 128 ? EncodePng(square) : EncodeDib(square);
            frames.Add((size, data));
        }

        WriteFrames(icoPath, frames);
    }

    /// <summary>
    /// Writes an ICO from raw 32-bpp BGRA pixels, as produced by the PPU
    /// framebuffer. Avoids a round-trip through GDI+ in the generated game.
    /// </summary>
    public static void WriteFromBgra(string icoPath, byte[] bgra, int width, int height, int[]? sizes = null)
    {
        if (bgra == null)
            throw new ArgumentNullException(nameof(bgra));

        if (bgra.Length < width * height * 4)
            throw new ArgumentException("Framebuffer is smaller than width*height*4.", nameof(bgra));

        sizes ??= DefaultSizes;

        List<(int Size, byte[] Data)> frames = new List<(int, byte[])>();

        foreach (int size in sizes)
        {
            using Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);

            // Nearest-neighbour: the source is pixel art, smoothing it would
            // destroy the look at 16x16.
            for (int y = 0; y < size; y++)
            {
                int sy = y * height / size;

                for (int x = 0; x < size; x++)
                {
                    int sx = x * width / size;
                    int src = (sy * width + sx) * 4;

                    Color c = Color.FromArgb(bgra[src + 3], bgra[src + 2], bgra[src + 1], bgra[src]);

                    // A fully transparent source pixel means "no frame yet";
                    // substitute the backdrop so the icon is never blank.
                    if (c.A == 0)
                        c = Color.Black;

                    bmp.SetPixel(x, y, Color.FromArgb(255, c.R, c.G, c.B));
                }
            }

            byte[] data = size >= 128 ? EncodePng(bmp) : EncodeDib(bmp);
            frames.Add((size, data));
        }

        WriteFrames(icoPath, frames);
    }

    /// <summary>Scales an arbitrary image onto a transparent square.</summary>
    public static Bitmap Square(Image source, int size)
    {
        Bitmap result = new Bitmap(size, size, PixelFormat.Format32bppArgb);

        using (Graphics g = Graphics.FromImage(result))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = size >= 64
                ? InterpolationMode.HighQualityBicubic
                : InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;

            float scale = Math.Min((float)size / source.Width, (float)size / source.Height);
            int w = Math.Max(1, (int)(source.Width * scale));
            int h = Math.Max(1, (int)(source.Height * scale));

            g.DrawImage(source, (size - w) / 2, (size - h) / 2, w, h);
        }

        return result;
    }

    private static void WriteFrames(string icoPath, List<(int Size, byte[] Data)> frames)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(icoPath));

        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using FileStream fs = new FileStream(icoPath, FileMode.Create, FileAccess.Write);
        using BinaryWriter bw = new BinaryWriter(fs);

        bw.Write((ushort)0);                 // reserved
        bw.Write((ushort)1);                 // type: icon
        bw.Write((ushort)frames.Count);

        int offset = 6 + 16 * frames.Count;

        foreach ((int size, byte[] data) in frames)
        {
            // 256 is encoded as 0 in the directory entry.
            bw.Write((byte)(size >= 256 ? 0 : size));
            bw.Write((byte)(size >= 256 ? 0 : size));
            bw.Write((byte)0);               // palette entries
            bw.Write((byte)0);               // reserved
            bw.Write((ushort)1);             // colour planes
            bw.Write((ushort)32);            // bits per pixel
            bw.Write(data.Length);
            bw.Write(offset);
            offset += data.Length;
        }

        foreach ((_, byte[] data) in frames)
            bw.Write(data);
    }

    private static byte[] EncodePng(Bitmap bmp)
    {
        using MemoryStream ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static byte[] EncodeDib(Bitmap bmp)
    {
        int w = bmp.Width;
        int h = bmp.Height;
        int xorSize = w * h * 4;
        int andStride = ((w + 31) / 32) * 4;
        int andSize = andStride * h;

        using MemoryStream ms = new MemoryStream();
        using BinaryWriter bw = new BinaryWriter(ms);

        bw.Write(40);                 // biSize
        bw.Write(w);                  // biWidth
        bw.Write(h * 2);              // biHeight: XOR + AND masks
        bw.Write((ushort)1);          // biPlanes
        bw.Write((ushort)32);         // biBitCount
        bw.Write(0);                  // biCompression: BI_RGB
        bw.Write(xorSize + andSize);  // biSizeImage
        bw.Write(0);                  // biXPelsPerMeter
        bw.Write(0);                  // biYPelsPerMeter
        bw.Write(0);                  // biClrUsed
        bw.Write(0);                  // biClrImportant

        BitmapData data = bmp.LockBits(
            new Rectangle(0, 0, w, h),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            byte[] row = new byte[w * 4];

            // DIB rows are stored bottom-up.
            for (int y = h - 1; y >= 0; y--)
            {
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                bw.Write(row);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        bw.Write(new byte[andSize]);
        bw.Flush();

        return ms.ToArray();
    }
}
