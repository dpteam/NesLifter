// NesLifter branding asset generator.
//
// Draws the project logo, mark and application icon with GDI+ and writes:
//   assets/logo.png            horizontal logo (README hero)
//   assets/logo-mark.png       square mark, 512 px
//   assets/icon.png            square mark, 512 px (PNG copy of the icon)
//   assets/icon.ico            multi-resolution Windows icon
//   assets/social-preview.png  GitHub social preview card (1280x640)
//
// Run from the repository root:
//   dotnet run --project tools/Branding
//
// The vector sources (assets/*.svg) are maintained by hand and must stay in
// sync with the geometry below.

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace NesLifter.Branding;

internal static class Program
{
    private static readonly Color InkTop = FromHex("#1B2436");
    private static readonly Color InkBottom = FromHex("#070A11");
    private static readonly Color Border = FromHex("#2C3D5C");
    private static readonly Color Grid = FromHex("#8FB3E0");
    private static readonly Color Cream = FromHex("#F3ECDD");
    private static readonly Color CreamShade = FromHex("#CFC5AF");
    private static readonly Color Red = FromHex("#FF4D5A");
    private static readonly Color RedDeep = FromHex("#B81F31");
    private static readonly Color Gold = FromHex("#D9A23A");
    private static readonly Color GoldDeep = FromHex("#8E661C");
    private static readonly Color Muted = FromHex("#93A7C4");
    private static readonly Color Glow = FromHex("#FF4D5A");

    private static int Main()
    {
        string root = FindRepositoryRoot();
        string assets = Path.Combine(root, "assets");
        Directory.CreateDirectory(assets);

        SavePng(DrawMark(512), Path.Combine(assets, "logo-mark.png"));
        SavePng(DrawMark(512), Path.Combine(assets, "icon.png"));
        SavePng(DrawLogo(1240, 400), Path.Combine(assets, "logo.png"));
        SavePng(DrawSocial(1280, 640), Path.Combine(assets, "social-preview.png"));

        int[] iconSizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        WriteIco(Path.Combine(assets, "icon.ico"), iconSizes);

        Console.WriteLine("Branding assets written to " + assets);
        return 0;
    }

    // ---------------------------------------------------------------------
    // Geometry
    // ---------------------------------------------------------------------

    private static Bitmap DrawMark(int size)
    {
        Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using Graphics g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;

        float s = size / 512f;
        g.ScaleTransform(s, s);

        DrawBadge(g, 512f);
        DrawCircuit(g);
        DrawCartridge(g);

        return bmp;
    }

    private static void DrawBadge(Graphics g, float size)
    {
        using GraphicsPath path = RoundedRect(new RectangleF(8, 8, size - 16, size - 16), size * 0.22f);
        using LinearGradientBrush bg = new LinearGradientBrush(
            new PointF(0, 0), new PointF(size, size), InkTop, InkBottom);
        g.FillPath(bg, path);

        using Pen border = new Pen(Border, size * 0.011f);
        g.DrawPath(border, path);

        // Subtle 8-bit dot grid.
        using SolidBrush dot = new SolidBrush(Color.FromArgb(16, Grid));
        float step = size / 16f;
        for (int y = 1; y < 16; y++)
        {
            for (int x = 1; x < 16; x++)
            {
                float px = x * step, py = y * step;
                if (px < 60 || px > size - 60 || py < 60 || py > size - 60)
                    continue;
                g.FillRectangle(dot, px - 1.5f, py - 1.5f, 3f, 3f);
            }
        }

        // Top highlight.
        using GraphicsPath shine = RoundedRect(new RectangleF(8, 8, size - 16, size * 0.34f), size * 0.22f);
        using LinearGradientBrush shineBrush = new LinearGradientBrush(
            new PointF(0, 8), new PointF(0, size * 0.34f),
            Color.FromArgb(26, 255, 255, 255), Color.FromArgb(0, 255, 255, 255));
        g.FillPath(shineBrush, shine);
    }

    private static void DrawCircuit(Graphics g)
    {
        using Pen trace = new Pen(Color.FromArgb(90, 0x2F, 0x44, 0x66), 3f);
        using Pen accent = new Pen(Color.FromArgb(190, Glow), 3f);
        using SolidBrush node = new SolidBrush(Color.FromArgb(150, 0x44, 0x60, 0x8C));
        using SolidBrush nodeAccent = new SolidBrush(Glow);

        // Left bus.
        g.DrawLine(trace, 40, 176, 116, 176);
        g.DrawLine(trace, 40, 176, 40, 120);
        g.FillRectangle(node, 36, 114, 8, 8);

        g.DrawLine(trace, 40, 232, 92, 232);
        g.DrawLine(trace, 92, 232, 92, 300);
        g.DrawLine(trace, 92, 300, 130, 300);
        g.FillRectangle(node, 126, 296, 8, 8);

        // Right bus with the "signal" accent.
        g.DrawLine(trace, 472, 168, 400, 168);
        g.DrawLine(trace, 472, 168, 472, 96);
        g.FillRectangle(node, 468, 90, 8, 8);

        g.DrawLine(accent, 472, 336, 408, 336);
        g.DrawLine(accent, 408, 336, 408, 372);
        g.FillRectangle(nodeAccent, 404, 368, 8, 8);

        // Bottom data bus.
        g.DrawLine(trace, 148, 470, 148, 452);
        g.DrawLine(trace, 148, 452, 364, 452);
        g.DrawLine(trace, 364, 452, 364, 470);
        g.FillRectangle(node, 144, 466, 8, 8);
        g.FillRectangle(node, 360, 466, 8, 8);
    }

    private static void DrawCartridge(Graphics g)
    {
        float x = 150, y = 104, w = 212, h = 300;
        float shoulder = 30, tabLeft = 58, tabRight = 154;

        using GraphicsPath body = new GraphicsPath();
        body.AddLine(x, y + shoulder, x + tabLeft, y + shoulder);
        body.AddLine(x + tabLeft, y + shoulder, x + tabLeft, y + 6);
        body.AddLine(x + tabLeft, y + 6, x + tabRight, y + 6);
        body.AddLine(x + tabRight, y + 6, x + tabRight, y + shoulder);
        body.AddLine(x + tabRight, y + shoulder, x + w, y + shoulder);
        body.AddLine(x + w, y + shoulder, x + w, y + h);
        body.AddLine(x + w, y + h, x, y + h);
        body.CloseFigure();

        // Soft glow behind the cartridge.
        using GraphicsPath glowPath = new GraphicsPath();
        glowPath.AddEllipse(x - 26, y + 20, w + 52, h + 30);
        using PathGradientBrush glow = new PathGradientBrush(glowPath)
        {
            CenterColor = Color.FromArgb(58, Glow),
            SurroundColors = new[] { Color.FromArgb(0, Glow) }
        };
        g.FillPath(glow, glowPath);

        // Cartridge body.
        using LinearGradientBrush bodyBrush = new LinearGradientBrush(
            new PointF(x, y), new PointF(x + w, y + h), FromHex("#FFF9EC"), CreamShade);
        g.FillPath(bodyBrush, body);

        using Pen outline = new Pen(FromHex("#8C8168"), 4f);
        g.DrawPath(outline, body);

        // Moulded ridge.
        using Pen ridge = new Pen(Color.FromArgb(90, 0x7A, 0x6F, 0x58), 3f);
        g.DrawLine(ridge, x + 14, y + 46, x + w - 14, y + 46);

        // Label.
        RectangleF label = new RectangleF(x + 24, y + 62, w - 48, 122);
        using GraphicsPath labelPath = RoundedRect(label, 9f);
        using LinearGradientBrush labelBrush = new LinearGradientBrush(
            new PointF(label.Left, label.Top), new PointF(label.Right, label.Bottom), Red, RedDeep);
        g.FillPath(labelBrush, labelPath);
        using Pen labelEdge = new Pen(Color.FromArgb(120, 255, 255, 255), 2.5f);
        g.DrawPath(labelEdge, labelPath);

        DrawBolt(g, label);

        // Edge connector contacts.
        float contactW = 17, contactGap = 12;
        float total = 5 * contactW + 4 * contactGap;
        float cx = x + (w - total) / 2f;
        for (int i = 0; i < 5; i++)
        {
            RectangleF contact = new RectangleF(cx + i * (contactW + contactGap), y + h - 42, contactW, 26);
            using LinearGradientBrush contactBrush = new LinearGradientBrush(
                contact, Gold, GoldDeep, LinearGradientMode.Vertical);
            g.FillRectangle(contactBrush, contact);
            using Pen contactEdge = new Pen(Color.FromArgb(120, 0x5C, 0x40, 0x0E), 1.5f);
            g.DrawRectangle(contactEdge, contact.X, contact.Y, contact.Width, contact.Height);
        }

        // Status LED.
        using SolidBrush ledGlow = new SolidBrush(Color.FromArgb(60, 0x5A, 0xFF, 0x9A));
        g.FillEllipse(ledGlow, x + w - 40, y + h - 86, 24, 24);
        using SolidBrush led = new SolidBrush(FromHex("#5AFF9A"));
        g.FillEllipse(led, x + w - 34, y + h - 80, 12, 12);
    }

    private static void DrawBolt(Graphics g, RectangleF label)
    {
        // Classic lightning bolt polygon. Pixel rows were tried first but at
        // icon sizes the stair-steps read as an "S", so the glyph is vector.
        float boxW = label.Width * 0.34f;
        float boxH = label.Height * 0.74f;
        float x0 = label.Left + (label.Width - boxW) / 2f;
        float y0 = label.Top + (label.Height - boxH) / 2f;

        PointF[] unit =
        {
            new PointF(0.60f, 0.00f),
            new PointF(0.18f, 0.56f),
            new PointF(0.46f, 0.56f),
            new PointF(0.34f, 1.00f),
            new PointF(0.84f, 0.40f),
            new PointF(0.54f, 0.40f)
        };

        PointF[] points = new PointF[unit.Length];
        for (int i = 0; i < unit.Length; i++)
            points[i] = new PointF(x0 + unit[i].X * boxW, y0 + unit[i].Y * boxH);

        using SolidBrush shadow = new SolidBrush(Color.FromArgb(80, 0, 0, 0));
        PointF[] shadowPoints = new PointF[points.Length];
        for (int i = 0; i < points.Length; i++)
            shadowPoints[i] = new PointF(points[i].X + 2.5f, points[i].Y + 2.5f);

        g.FillPolygon(shadow, shadowPoints);

        using SolidBrush ink = new SolidBrush(Cream);
        g.FillPolygon(ink, points);
    }

    private static Bitmap DrawLogo(int width, int height)
    {
        Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using Graphics g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);

        int markSize = (int)(height * 0.90f);
        using (Bitmap mark = DrawMark(markSize))
        {
            g.DrawImage(mark, 6, (height - markSize) / 2);
        }

        float textLeft = markSize + 48;
        float scale = height / 400f;

        using FontFamily family = PickFamily();
        using Font title = new Font(family, 152f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        using Font subtitle = new Font(family, 36f * scale, FontStyle.Regular, GraphicsUnit.Pixel);

        StringFormat format = StringFormat.GenericTypographic;
        format.FormatFlags |= StringFormatFlags.NoWrap;
        format.Alignment = StringAlignment.Near;

        const string first = "Nes";
        const string second = "Lifter";

        SizeF firstSize = g.MeasureString(first, title, PointF.Empty, format);
        SizeF secondSize = g.MeasureString(second, title, PointF.Empty, format);

        float titleTop = height * 0.15f;

        using SolidBrush cream = new SolidBrush(Cream);
        using SolidBrush red = new SolidBrush(Red);

        g.DrawString(first, title, cream, new PointF(textLeft, titleTop), format);
        g.DrawString(second, title, red, new PointF(textLeft + firstSize.Width, titleTop), format);

        float underlineY = titleTop + firstSize.Height * 0.95f;
        float underlineW = firstSize.Width + secondSize.Width;
        using Pen underline = new Pen(FromHex("#33496B"), Math.Max(2f, 4f * scale));
        g.DrawLine(underline, textLeft + 4, underlineY, textLeft + underlineW - 4, underlineY);

        using SolidBrush muted = new SolidBrush(Muted);
        const string tagline = "STATIC NES  \u2192  .NET RECOMPILER";
        g.DrawString(tagline, subtitle, muted, new PointF(textLeft + 6, underlineY + 26 * scale), format);

        return bmp;
    }

    private static Bitmap DrawSocial(int width, int height)
    {
        Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using Graphics g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        using (LinearGradientBrush bg = new LinearGradientBrush(
            new PointF(0, 0), new PointF(width, height), InkTop, InkBottom))
        {
            g.FillRectangle(bg, 0, 0, width, height);
        }

        // Grid.
        using SolidBrush dot = new SolidBrush(Color.FromArgb(14, Grid));
        for (int y = 24; y < height; y += 32)
        {
            for (int x = 24; x < width; x += 32)
                g.FillRectangle(dot, x, y, 3, 3);
        }

        int markSize = 380;
        using (Bitmap mark = DrawMark(markSize))
        {
            g.DrawImage(mark, 96, (height - markSize) / 2);
        }

        using FontFamily family = PickFamily();
        using Font title = new Font(family, 112f, FontStyle.Bold, GraphicsUnit.Pixel);
        using Font subtitle = new Font(family, 34f, FontStyle.Regular, GraphicsUnit.Pixel);

        StringFormat format = StringFormat.GenericTypographic;
        format.FormatFlags |= StringFormatFlags.NoWrap;

        using SolidBrush cream = new SolidBrush(Cream);
        using SolidBrush red = new SolidBrush(Red);
        using SolidBrush muted = new SolidBrush(Muted);

        float x0 = 540;
        SizeF first = g.MeasureString("Nes", title, PointF.Empty, format);
        g.DrawString("Nes", title, cream, new PointF(x0, 168), format);
        g.DrawString("Lifter", title, red, new PointF(x0 + first.Width, 168), format);

        using Pen underline = new Pen(FromHex("#33496B"), 4f);
        g.DrawLine(underline, x0 + 4, 296, x0 + first.Width + g.MeasureString("Lifter", title, PointF.Empty, format).Width - 4, 296);

        g.DrawString("Static NES \u2192 .NET recompiler", subtitle, muted, new PointF(x0 + 4, 322), format);
        using Font small = new Font(family, 26f, FontStyle.Regular, GraphicsUnit.Pixel);
        g.DrawString("6502 in, readable C# out", small, muted, new PointF(x0 + 4, 378), format);

        return bmp;
    }

    private static FontFamily PickFamily()
    {
        string[] candidates =
        {
            "Segoe UI Variable Display",
            "Segoe UI",
            "Inter",
            "Arial",
            "Tahoma"
        };

        foreach (string name in candidates)
        {
            try
            {
                FontFamily family = new FontFamily(name);
                if (family.IsStyleAvailable(FontStyle.Bold))
                    return family;
                family.Dispose();
            }
            catch
            {
                // Font is not installed, try the next one.
            }
        }

        return FontFamily.GenericSansSerif;
    }

    // ---------------------------------------------------------------------
    // Encoding helpers
    // ---------------------------------------------------------------------

    private static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        float d = radius * 2;
        GraphicsPath path = new GraphicsPath();
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void SavePng(Bitmap bmp, string path)
    {
        using (bmp)
        {
            bmp.Save(path, ImageFormat.Png);
        }

        Console.WriteLine("  " + Path.GetFileName(path));
    }

    private static void WriteIco(string path, int[] sizes)
    {
        var images = new List<(int Size, byte[] Data)>();

        foreach (int size in sizes)
        {
            using Bitmap bmp = DrawMark(size);
            byte[] data = size >= 128 ? EncodePng(bmp) : EncodeDib(bmp);
            images.Add((size, data));
        }

        using FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using BinaryWriter bw = new BinaryWriter(fs);

        bw.Write((ushort)0);          // reserved
        bw.Write((ushort)1);          // type: icon
        bw.Write((ushort)images.Count);

        int offset = 6 + 16 * images.Count;

        foreach ((int size, byte[] data) in images)
        {
            bw.Write((byte)(size >= 256 ? 0 : size));
            bw.Write((byte)(size >= 256 ? 0 : size));
            bw.Write((byte)0);        // palette size
            bw.Write((byte)0);        // reserved
            bw.Write((ushort)1);      // color planes
            bw.Write((ushort)32);     // bits per pixel
            bw.Write(data.Length);
            bw.Write(offset);
            offset += data.Length;
        }

        foreach ((_, byte[] data) in images)
            bw.Write(data);

        Console.WriteLine("  " + Path.GetFileName(path));
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
        bw.Write(h * 2);              // biHeight (XOR + AND)
        bw.Write((ushort)1);          // biPlanes
        bw.Write((ushort)32);         // biBitCount
        bw.Write(0);                  // biCompression (BI_RGB)
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

    private static Color FromHex(string hex)
    {
        return ColorTranslator.FromHtml(hex);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "NesLifter.csproj")))
                return dir.FullName;

            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
