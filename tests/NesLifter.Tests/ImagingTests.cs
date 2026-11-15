using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using NesLifter.Imaging;
using Xunit;

namespace NesLifter.Tests;

public class IcoWriterTests
{
    private static string TempPath(string extension)
    {
        string dir = Path.Combine(Path.GetTempPath(), "neslifter-tests");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, Guid.NewGuid().ToString("N") + extension);
    }

    [Fact]
    public void WritesAllRequestedSizes()
    {
        string ico = TempPath(".ico");

        try
        {
            using (Bitmap bmp = new Bitmap(64, 64))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                    g.Clear(Color.CornflowerBlue);

                IcoWriter.Write(bmp, ico);
            }

            byte[] bytes = File.ReadAllBytes(ico);

            Assert.Equal(0, bytes[0]);
            Assert.Equal(0, bytes[1]);
            Assert.Equal(1, bytes[2]);
            Assert.Equal(0, bytes[3]);

            int count = bytes[4] | (bytes[5] << 8);
            Assert.Equal(IcoWriter.DefaultSizes.Length, count);

            for (int i = 0; i < count; i++)
            {
                int entry = 6 + i * 16;
                int width = bytes[entry] == 0 ? 256 : bytes[entry];
                int height = bytes[entry + 1] == 0 ? 256 : bytes[entry + 1];
                int size = BitConverter.ToInt32(bytes, entry + 8);
                int offset = BitConverter.ToInt32(bytes, entry + 12);

                Assert.Equal(IcoWriter.DefaultSizes[i], width);
                Assert.Equal(width, height);
                Assert.True(size > 0, "frame " + i + " has no data");
                Assert.InRange(offset + size, 1, bytes.Length);
            }
        }
        finally
        {
            File.Delete(ico);
        }
    }

    [Fact]
    public void NonSquareSourceIsLetterboxedNotStretched()
    {
        string ico = TempPath(".ico");

        try
        {
            // A 200x100 source: on a square canvas the top and bottom rows must
            // stay transparent rather than being stretched.
            using (Bitmap bmp = new Bitmap(200, 100))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                    g.Clear(Color.Red);

                IcoWriter.Write(bmp, ico, new[] { 64 });
            }

            byte[] bytes = File.ReadAllBytes(ico);
            int size = BitConverter.ToInt32(bytes, 6 + 8);
            int offset = BitConverter.ToInt32(bytes, 6 + 12);

            using Bitmap decoded = DecodeDib(bytes, offset, size);
            Assert.Equal(64, decoded.Width);
            Assert.Equal(64, decoded.Height);

            // Top-left corner is outside the letterboxed content.
            Assert.Equal(0, decoded.GetPixel(1, 1).A);

            // The middle is the scaled image.
            Assert.Equal(255, decoded.GetPixel(32, 32).A);
        }
        finally
        {
            File.Delete(ico);
        }
    }

    [Fact]
    public void WritesFromBgraFramebuffer()
    {
        string ico = TempPath(".ico");

        try
        {
            const int w = 256;
            const int h = 240;
            byte[] bgra = new byte[w * h * 4];

            // Half the frame green, half blue, opaque.
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4;
                    bgra[i + 0] = (byte)(x < w / 2 ? 0 : 255);   // B
                    bgra[i + 1] = (byte)(x < w / 2 ? 255 : 0);   // G
                    bgra[i + 2] = 0;                             // R
                    bgra[i + 3] = 255;
                }
            }

            IcoWriter.WriteFromBgra(ico, bgra, w, h, new[] { 32 });

            byte[] bytes = File.ReadAllBytes(ico);
            Assert.True(bytes.Length > 100);
            Assert.Equal(1, bytes[4] | (bytes[5] << 8));
        }
        finally
        {
            File.Delete(ico);
        }
    }

    [Fact]
    public void RejectsTooSmallFramebuffer()
    {
        string ico = TempPath(".ico");

        try
        {
            byte[] tooSmall = new byte[16];

            Assert.Throws<ArgumentException>(() =>
                IcoWriter.WriteFromBgra(ico, tooSmall, 256, 240));
        }
        finally
        {
            if (File.Exists(ico))
                File.Delete(ico);
        }
    }

    private static Bitmap DecodeDib(byte[] ico, int offset, int size)
    {
        // Parse the BITMAPINFOHEADER written by EncodeDib.
        int headerSize = BitConverter.ToInt32(ico, offset);
        int width = BitConverter.ToInt32(ico, offset + 4);
        int height = BitConverter.ToInt32(ico, offset + 8) / 2;
        int bpp = BitConverter.ToUInt16(ico, offset + 14);

        Assert.Equal(40, headerSize);
        Assert.Equal(32, bpp);

        Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // Rows are bottom-up.
                int row = height - 1 - y;
                int i = offset + headerSize + (row * width + x) * 4;

                bmp.SetPixel(x, y, Color.FromArgb(
                    ico[i + 3], ico[i + 2], ico[i + 1], ico[i]));
            }
        }

        return bmp;
    }
}

public class PeIconTests
{
    [Fact]
    public void ReportsMissingExecutableInsteadOfThrowing()
    {
        bool ok = PeIcon.Apply(
            Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid().ToString("N") + ".exe"),
            Path.Combine(Path.GetTempPath(), "missing.ico"),
            out string error);

        Assert.False(ok);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void ReportsMissingIconInsteadOfThrowing()
    {
        string exe = Path.Combine(Path.GetTempPath(), "neslifter-tests", Guid.NewGuid().ToString("N") + ".exe");
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
        File.WriteAllBytes(exe, new byte[64]);

        try
        {
            bool ok = PeIcon.Apply(exe, Path.Combine(Path.GetTempPath(), "nope.ico"), out string error);

            Assert.False(ok);
            Assert.NotEmpty(error);
        }
        finally
        {
            File.Delete(exe);
        }
    }
}
