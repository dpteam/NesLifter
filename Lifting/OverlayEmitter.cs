using System;
using System.Text;

namespace NesLifter.Lifting;

/// <summary>
/// Emits the in-game overlay: a NES-styled panel drawn over the game window,
/// toggled with F2.
///
/// Design decisions that matter:
///
/// * The overlay draws at *window* resolution, not framebuffer resolution.
///   Drawing at 256x240 and scaling up makes text unreadable, which is the
///   mistake the old debug OSD made.
///
/// * Tab 0 is a passive monitor: it shows CPU/PPU/APU state and does *not*
///   steal input, so you can watch the game while playing. Every other tab is a
///   settings panel and locks game input while it is focused, because otherwise
///   pressing an arrow key to change a setting would also move the player.
///
/// * Settings write straight into the runtime (volume, scale, throttle) so there
///   is no separate "apply" step and no chance of the UI and the engine
///   disagreeing about the current value.
/// </summary>
public sealed class OverlayEmitter
{
    private readonly StringBuilder _sb = new();

    public string Emit()
    {
        _sb.Clear();

        Raw("""
            /// <summary>
            /// In-game overlay: monitoring plus settings, toggled with F2.
            /// </summary>
            static class Overlay
            {
                public static bool Visible;

                /// <summary>0 = Monitor, 1 = Video, 2 = Audio, 3 = Input, 4 = Debug, 5 = Log.</summary>
                public static int Tab;

                static readonly string[] TabNames = { "Monitor", "Video", "Audio", "Input", "Debug", "Log" };

                /// <summary>Selected row inside the current tab.</summary>
                public static int Row;

                /// <summary>
                /// True when the focused tab consumes game input.
                ///
                /// The monitor tab is passive on purpose: you should be able to
                /// watch the numbers while still playing. Settings tabs take input
                /// so arrow keys change values instead of moving the player.
                /// </summary>
                public static bool LocksInput
                {
                    get { return Visible && Tab != 0; }
                }

                // ---- settings (written straight into the runtime) ----

                public static int Scale = 2;
                public static bool ShowFps = true;
                public static bool SpriteLimit = true;

                /// <summary>Proxies the mixer's own volume so there is one value.</summary>
                public static int Volume
                {
                    get { return Audio.Volume; }
                    set { Audio.Volume = value; }
                }

                static readonly int[] ScaleChoices = { 1, 2, 3, 4, 5, 6 };

                public static void Toggle()
                {
                    Visible = !Visible;

                    if (!Visible)
                    {
                        // Releasing every held button avoids a stuck d-pad when the
                        // overlay closes while a direction is pressed.
                        Runtime.Joy1Buttons = 0;
                    }
                }

                public static void NextTab(int delta)
                {
                    Tab = (Tab + delta + TabNames.Length) % TabNames.Length;
                    Row = 0;
                }

                /// <summary>
                /// Handles one key press. Returns true when the overlay consumed it,
                /// so the form knows not to forward it to the emulated pad.
                /// </summary>
                public static bool HandleKey(System.Windows.Forms.Keys key)
                {
                    if (key == System.Windows.Forms.Keys.F2)
                    {
                        Toggle();
                        return true;
                    }

                    if (!Visible) return false;

                    switch (key)
                    {
                        case System.Windows.Forms.Keys.Tab:
                            NextTab(1);
                            return true;

                        case System.Windows.Forms.Keys.Up:
                            Row = Math.Max(0, Row - 1);
                            return LocksInput;

                        case System.Windows.Forms.Keys.Down:
                            {
                                int count = RowCount();

                                // The Monitor tab has no rows. Without this guard the
                                // index would drift to -1 and then point at nothing
                                // when a settings tab is opened.
                                Row = count == 0 ? 0 : Math.Min(count - 1, Row + 1);
                                return LocksInput;
                            }

                        case System.Windows.Forms.Keys.Left:
                            Change(-1);
                            return LocksInput;

                        case System.Windows.Forms.Keys.Right:
                        case System.Windows.Forms.Keys.Enter:
                            Change(1);
                            return LocksInput;
                    }

                    return LocksInput;
                }

                static int RowCount()
                {
                    switch (Tab)
                    {
                        case 1: return 4;   // Video
                        case 2: return 2;   // Audio
                        case 3: return 1;   // Input
                        case 4: return 3;   // Debug
                        case 5: return 1;   // Log
                        default: return 0;  // Monitor has no rows
                    }
                }

                static void Change(int direction)
                {
                    switch (Tab)
                    {
                        case 1:
                            if (Row == 0)
                            {
                                int at = Array.IndexOf(ScaleChoices, Scale);
                                if (at < 0) at = 1;
                                at = (at + direction + ScaleChoices.Length) % ScaleChoices.Length;
                                Scale = ScaleChoices[at];
                                Runtime.RequestScale(Scale);
                            }
                            else if (Row == 1) ShowFps = !ShowFps;
                            else if (Row == 2) SpriteLimit = !SpriteLimit;
                            else if (Row == 3) Runtime.ThrottleEnabled = !Runtime.ThrottleEnabled;
                            break;

                        case 2:
                            if (Row == 0) Volume = Math.Max(0, Math.Min(100, Volume + direction * 10));
                            else if (Row == 1) Runtime.MuteAll = !Runtime.MuteAll;
                            break;

                        case 4:
                            if (Row == 0) Runtime.ForceInterpreter = !Runtime.ForceInterpreter;
                            else if (Row == 1) Runtime.TraceEnabled = !Runtime.TraceEnabled;
                            else if (Row == 2) Runtime.TrapCount = 0;
                            break;
                    }
                }

                // =========================================================
                // Drawing
                // =========================================================

                /// <summary>
                /// Draws the overlay over an already-scaled frame.
                ///
                /// Everything is laid out in window pixels so text stays crisp at
                /// any integer scale.
                /// </summary>
                public static void Draw(System.Drawing.Graphics g, int width, int height)
                {
                    if (!Visible) return;

                    const int Pad = 10;
                    const int LineHeight = 16;

                    int panelW = Math.Min(width - Pad * 2, 460);
                    int panelH = Math.Min(height - Pad * 2, 320);
                    int x = (width - panelW) / 2;
                    int y = (height - panelH) / 2;

                    using (System.Drawing.SolidBrush bg = new System.Drawing.SolidBrush(
                        System.Drawing.Color.FromArgb(238, 12, 16, 26)))
                    using (System.Drawing.Pen border = new System.Drawing.Pen(
                        System.Drawing.Color.FromArgb(255, 255, 77, 90), 2f))
                    {
                        g.FillRectangle(bg, x, y, panelW, panelH);
                        g.DrawRectangle(border, x, y, panelW - 1, panelH - 1);
                    }

                    using System.Drawing.Font titleFont = new System.Drawing.Font(
                        "Consolas", 10f, System.Drawing.FontStyle.Bold);
                    using System.Drawing.Font bodyFont = new System.Drawing.Font(
                        "Consolas", 9f, System.Drawing.FontStyle.Regular);

                    // The tab strip has to fit inside the panel. At 1x scale the
                    // panel is only 236 px wide, which is not enough for six labels
                    // at 10pt, so shrink the tab font until they fit instead of
                    // letting the last tab run off the panel.
                    System.Drawing.Font tabFont = titleFont;
                    bool tabFontOwned = false;

                    for (float size = 10f; size >= 6f; size -= 1f)
                    {
                        float total = 0f;

                        for (int i = 0; i < TabNames.Length; i++)
                            total += g.MeasureString(TabNames[i], tabFont).Width + 14f;

                        if (total <= panelW - 16) break;

                        if (tabFontOwned) tabFont.Dispose();
                        tabFont = new System.Drawing.Font("Consolas", size, System.Drawing.FontStyle.Bold);
                        tabFontOwned = true;
                    }

                    using System.Drawing.SolidBrush ink = new System.Drawing.SolidBrush(
                        System.Drawing.Color.FromArgb(255, 243, 236, 221));
                    using System.Drawing.SolidBrush accent = new System.Drawing.SolidBrush(
                        System.Drawing.Color.FromArgb(255, 255, 77, 90));
                    using System.Drawing.SolidBrush muted = new System.Drawing.SolidBrush(
                        System.Drawing.Color.FromArgb(255, 147, 167, 196));
                    using System.Drawing.SolidBrush gold = new System.Drawing.SolidBrush(
                        System.Drawing.Color.FromArgb(255, 217, 162, 58));
                    using System.Drawing.SolidBrush shade = new System.Drawing.SolidBrush(
                        System.Drawing.Color.FromArgb(255, 12, 16, 26));

                    // ---- tab strip ----
                    int tx = x + 8;

                    for (int i = 0; i < TabNames.Length; i++)
                    {
                        bool active = i == Tab;
                        string label = TabNames[i];
                        System.Drawing.SizeF size = g.MeasureString(label, tabFont);

                        if (active)
                        {
                            using (System.Drawing.SolidBrush tabBg = new System.Drawing.SolidBrush(
                                System.Drawing.Color.FromArgb(255, 255, 77, 90)))
                            {
                                g.FillRectangle(tabBg, tx - 4, y + 6, size.Width + 8, LineHeight + 4);
                            }

                            g.DrawString(label, tabFont, shade, tx, y + 8);
                        }
                        else
                        {
                            g.DrawString(label, tabFont, muted, tx, y + 8);
                        }

                        tx += (int)size.Width + 14;
                    }

                    using (System.Drawing.Pen rule = new System.Drawing.Pen(
                        System.Drawing.Color.FromArgb(255, 44, 61, 92), 1f))
                    {
                        g.DrawLine(rule, x + 8, y + 30, x + panelW - 8, y + 30);
                    }

                    int cy = y + 40;

                    switch (Tab)
                    {
                        case 0: DrawMonitor(g, bodyFont, ink, accent, muted, gold, x + 12, ref cy, LineHeight); break;
                        case 1: DrawVideo(g, bodyFont, ink, accent, muted, gold, x + 12, ref cy, LineHeight); break;
                        case 2: DrawAudio(g, bodyFont, ink, accent, muted, gold, x + 12, ref cy, LineHeight); break;
                        case 3: DrawInput(g, bodyFont, ink, accent, muted, gold, x + 12, ref cy, LineHeight); break;
                        case 4: DrawDebug(g, bodyFont, ink, accent, muted, gold, x + 12, ref cy, LineHeight); break;
                        case 5: DrawLog(g, bodyFont, ink, accent, muted, gold, x + 12, ref cy, LineHeight); break;
                    }

                    // ---- footer ----
                    string footer = LocksInput
                        ? "TAB switch   arrows change   F2 close"
                        : "TAB switch   F2 close   (game input active)";

                    g.DrawString(footer, bodyFont, muted, x + 12, y + panelH - 20);
                }

                static void DrawMonitor(System.Drawing.Graphics g, System.Drawing.Font f,
                    System.Drawing.Brush ink, System.Drawing.Brush accent, System.Drawing.Brush muted,
                    System.Drawing.Brush gold, int x, ref int y, int lh)
                {
                    Row2(g, f, x, ref y, lh, "FPS", Fps().ToString("0.0"), ink, gold);
                    Row2(g, f, x, ref y, lh, "Frame", Ppu.FrameCount.ToString(), ink, gold);
                    Row2(g, f, x, ref y, lh, "CPU", "A=" + Runtime.A.ToString("X2") + " X=" + Runtime.X.ToString("X2") + " Y=" + Runtime.Y.ToString("X2") + " SP=" + Runtime.SP.ToString("X2"), muted, ink);
                    Row2(g, f, x, ref y, lh, "PC", "$" + Runtime.LastPC.ToString("X4"), muted, ink);
                    Row2(g, f, x, ref y, lh, "Status", "$" + Runtime.P.ToString("X2"), muted, ink);
                    Row2(g, f, x, ref y, lh, "PPU", "ctrl=$" + Ppu.Ctrl.ToString("X2") + " mask=$" + Ppu.Mask.ToString("X2"), muted, ink);
                    Row2(g, f, x, ref y, lh, "Scroll", Ppu.ScrollX0 + "," + Ppu.ScrollY0 + " nt" + Ppu.BaseNt0, muted, ink);
                    Row2(g, f, x, ref y, lh, "Mapper", Runtime.MapperName, muted, ink);

                    string snd = "sq1=" + (Apu.Sq1On() ? "on " : "off")
                               + " sq2=" + (Apu.Sq2On() ? "on " : "off")
                               + " tri=" + (Apu.TriOn() ? "on " : "off")
                               + " noi=" + (Apu.NoiOn() ? "on " : "off");

                    Row2(g, f, x, ref y, lh, "APU", snd, muted, ink);
                    Row2(g, f, x, ref y, lh, "Traps", Runtime.TrapCount.ToString(), muted,
                        Runtime.TrapCount > 0 ? accent : ink);
                }

                static void DrawVideo(System.Drawing.Graphics g, System.Drawing.Font f,
                    System.Drawing.Brush ink, System.Drawing.Brush accent, System.Drawing.Brush muted,
                    System.Drawing.Brush gold, int x, ref int y, int lh)
                {
                    Option(g, f, x, ref y, lh, 0, "Scale", Scale + "x", ink, accent, muted, gold);
                    Option(g, f, x, ref y, lh, 1, "Show FPS", YesNo(ShowFps), ink, accent, muted, gold);
                    Option(g, f, x, ref y, lh, 2, "8 sprite limit", YesNo(SpriteLimit), ink, accent, muted, gold);
                    Option(g, f, x, ref y, lh, 3, "Frame limiter", YesNo(Runtime.ThrottleEnabled), ink, accent, muted, gold);
                }

                static void DrawAudio(System.Drawing.Graphics g, System.Drawing.Font f,
                    System.Drawing.Brush ink, System.Drawing.Brush accent, System.Drawing.Brush muted,
                    System.Drawing.Brush gold, int x, ref int y, int lh)
                {
                    Option(g, f, x, ref y, lh, 0, "Volume", Volume + "%", ink, accent, muted, gold);
                    Option(g, f, x, ref y, lh, 1, "Mute", YesNo(Runtime.MuteAll), ink, accent, muted, gold);

                    y += 6;
                    g.DrawString(Audio.IsOpen ? "device: open" : "device: closed (headless?)", f, muted, x, y);
                    y += lh;
                }

                static void DrawInput(System.Drawing.Graphics g, System.Drawing.Font f,
                    System.Drawing.Brush ink, System.Drawing.Brush accent, System.Drawing.Brush muted,
                    System.Drawing.Brush gold, int x, ref int y, int lh)
                {
                    g.DrawString("Player 1 buttons", f, gold, x, y); y += lh;
                    g.DrawString("A=X  B=Z  Select=Back  Start=Enter", f, ink, x, y); y += lh;
                    g.DrawString("D-pad = arrow keys", f, ink, x, y); y += lh;
                    y += 4;
                    g.DrawString("Live: $" + Runtime.Joy1Buttons.ToString("X2"), f, muted, x, y); y += lh;
                }

                static void DrawDebug(System.Drawing.Graphics g, System.Drawing.Font f,
                    System.Drawing.Brush ink, System.Drawing.Brush accent, System.Drawing.Brush muted,
                    System.Drawing.Brush gold, int x, ref int y, int lh)
                {
                    Option(g, f, x, ref y, lh, 0, "Force interpreter", YesNo(Runtime.ForceInterpreter), ink, accent, muted, gold);
                    Option(g, f, x, ref y, lh, 1, "Trace instructions", YesNo(Runtime.TraceEnabled), ink, accent, muted, gold);
                    Option(g, f, x, ref y, lh, 2, "Clear trap count", "press", ink, accent, muted, gold);

                    y += 6;

                    if (!string.IsNullOrEmpty(Runtime.LastError))
                    {
                        string msg = Runtime.LastError;
                        if (msg.Length > 52) msg = msg.Substring(0, 52) + "...";
                        g.DrawString(msg, f, accent, x, y);
                        y += lh;
                    }
                    else
                    {
                        g.DrawString("no trap", f, muted, x, y);
                        y += lh;
                    }
                }

                static void DrawLog(System.Drawing.Graphics g, System.Drawing.Font f,
                    System.Drawing.Brush ink, System.Drawing.Brush accent, System.Drawing.Brush muted,
                    System.Drawing.Brush gold, int x, ref int y, int lh)
                {
                    System.Collections.Generic.List<string> lines = Runtime.RecentLog(14);

                    if (lines.Count == 0)
                    {
                        g.DrawString("(empty)", f, muted, x, y);
                        return;
                    }

                    foreach (string line in lines)
                    {
                        string text = line.Length > 56 ? line.Substring(0, 56) : line;
                        g.DrawString(text, f, ink, x, y);
                        y += lh;
                    }
                }

                // ---- drawing helpers ----

                static void Row2(System.Drawing.Graphics g, System.Drawing.Font f, int x, ref int y, int lh,
                    string label, string value, System.Drawing.Brush labelBrush, System.Drawing.Brush valueBrush)
                {
                    g.DrawString(label, f, labelBrush, x, y);
                    g.DrawString(value, f, valueBrush, x + 90, y);
                    y += lh;
                }

                static void Option(System.Drawing.Graphics g, System.Drawing.Font f, int x, ref int y, int lh,
                    int index, string label, string value,
                    System.Drawing.Brush ink, System.Drawing.Brush accent, System.Drawing.Brush muted,
                    System.Drawing.Brush gold)
                {
                    bool selected = Row == index;

                    if (selected)
                    {
                        using (System.Drawing.SolidBrush sel = new System.Drawing.SolidBrush(
                            System.Drawing.Color.FromArgb(70, 255, 77, 90)))
                        {
                            g.FillRectangle(sel, x - 6, y - 1, 400, lh);
                        }

                        g.DrawString(">", f, accent, x - 14, y);
                    }

                    g.DrawString(label, f, selected ? ink : muted, x, y);
                    g.DrawString(value, f, selected ? gold : ink, x + 200, y);
                    y += lh;
                }

                static string YesNo(bool value)
                {
                    return value ? "on" : "off";
                }

                static double Fps()
                {
                    return Runtime.MeasuredFps;
                }
            }

            """);

        return _sb.ToString();
    }

    private string Raw(string text)
    {
        _sb.Append(text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine));
        _sb.AppendLine();
        return string.Empty;
    }
}
