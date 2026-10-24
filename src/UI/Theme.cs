// Theme.cs - colours, fonts, DPI scaling and drawing helpers for the dark UI.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CompxTray.UI
{
    public static class Theme
    {
        public static readonly Color Bg = Hex(0x0D0F13);
        public static readonly Color Surface = Hex(0x15181E);
        public static readonly Color Surface2 = Hex(0x1E222A);
        public static readonly Color Surface3 = Hex(0x282D37);
        public static readonly Color Border = Hex(0x252A33);
        public static readonly Color Text = Hex(0xECEFF4);
        public static readonly Color Dim = Hex(0x8A93A3);
        public static readonly Color Faint = Hex(0x5A6272);
        public static readonly Color Accent = Hex(0x1EC8FF);
        public static readonly Color AccentHover = Hex(0x5AD8FF);
        public static readonly Color AccentDark = Hex(0x0B5470);
        public static readonly Color OnAccent = Hex(0x041018);
        public static readonly Color Good = Hex(0x35D07F);
        public static readonly Color Warn = Hex(0xF4B740);
        public static readonly Color Bad = Hex(0xFF5D62);

        public static readonly Font Body = new Font("Segoe UI", 9.75f);
        public static readonly Font Small = new Font("Segoe UI", 8.75f);
        public static readonly Font Caption = new Font("Segoe UI Semibold", 8.25f);
        public static readonly Font Strong = new Font("Segoe UI Semibold", 10f);
        public static readonly Font Button = new Font("Segoe UI Semibold", 8.75f);
        public static readonly Font Tab = new Font("Segoe UI Semibold", 9.5f);
        public static readonly Font Title = new Font("Segoe UI Semibold", 19f);
        public static readonly Font Big = new Font("Segoe UI Light", 26f);
        public static readonly Font Medium = new Font("Segoe UI Light", 16f);

        public static float Scale = 1f;
        public static int S(int v) { return (int)Math.Round(v * Scale); }
        public static float Sf(float v) { return v * Scale; }

        public static void InitScale()
        {
            try { using (var g = Graphics.FromHwnd(IntPtr.Zero)) Scale = g.DpiX / 96f; } catch { Scale = 1f; }
        }

        public static Color Hex(int rgb) { return Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF); }

        public static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static Color LevelColor(int percent, bool charging)
        {
            if (charging) return Accent;
            if (percent <= 20) return Bad;
            if (percent <= 50) return Warn;
            return Good;
        }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Color c, RectangleF r, float radius)
        {
            using (var p = Round(r, radius)) using (var b = new SolidBrush(c)) g.FillPath(b, p);
        }

        public static void StrokeRound(Graphics g, Color c, RectangleF r, float radius, float width)
        {
            using (var p = Round(r, radius)) using (var pen = new Pen(c, width)) g.DrawPath(pen, p);
        }

        public static void Prepare(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }

        public static void DrawText(Graphics g, string text, Font font, Color color, Rectangle r, TextFormatFlags flags)
        {
            TextRenderer.DrawText(g, text, font, r, color, flags | TextFormatFlags.NoPadding);
        }

        public static Size Measure(string text, Font font)
        {
            return TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
        }

        // ---------- Dark title bar (Windows 10 1809+ / Windows 11) ----------
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public static void DarkTitleBar(IntPtr hwnd)
        {
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(hwnd, 20, ref on, 4) != 0) DwmSetWindowAttribute(hwnd, 19, ref on, 4);
                int caption = 0x00130F0D; // COLORREF (BGR) of Bg, Windows 11 only
                DwmSetWindowAttribute(hwnd, 35, ref caption, 4);
            }
            catch { }
        }
    }

    // ---------- Dark tray menu ----------
    public class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Dim;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.Dim;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            var r = e.ImageRectangle;
            using (var pen = new Pen(Theme.Accent, Math.Max(1.6f, Theme.Sf(1.8f))))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                g.DrawLines(pen, new[] {
                    new PointF(r.Left + r.Width * 0.2f, r.Top + r.Height * 0.52f),
                    new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.74f),
                    new PointF(r.Left + r.Width * 0.82f, r.Top + r.Height * 0.28f) });
            }
        }
    }

    class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.Surface; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Surface; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Surface; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Surface; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color MenuItemBorder { get { return Theme.Surface3; } }
        public override Color MenuItemSelected { get { return Theme.Surface3; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.Surface3; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.Surface3; } }
        public override Color MenuItemPressedGradientBegin { get { return Theme.Surface2; } }
        public override Color MenuItemPressedGradientEnd { get { return Theme.Surface2; } }
        public override Color SeparatorDark { get { return Theme.Border; } }
        public override Color SeparatorLight { get { return Theme.Surface; } }
        public override Color CheckBackground { get { return Theme.Surface; } }
        public override Color CheckSelectedBackground { get { return Theme.Surface3; } }
        public override Color CheckPressedBackground { get { return Theme.Surface3; } }
    }
}
