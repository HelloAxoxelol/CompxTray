// Palette.cs - the colours of the Windows version, for the Avalonia app.
using Avalonia.Media;

namespace CompxTray.App
{
    static class Palette
    {
        public static readonly Color Bg = Hex(0x0D0F13);
        public static readonly Color Surface = Hex(0x15181E);
        public static readonly Color Surface2 = Hex(0x1E222A);
        public static readonly Color Border = Hex(0x252A33);
        public static readonly Color Text = Hex(0xECEFF4);
        public static readonly Color Dim = Hex(0x8A93A3);
        public static readonly Color Faint = Hex(0x5A6272);
        public static readonly Color Accent = Hex(0x1EC8FF);
        public static readonly Color OnAccent = Hex(0x041018);
        public static readonly Color Good = Hex(0x35D07F);
        public static readonly Color Warn = Hex(0xF4B740);
        public static readonly Color Bad = Hex(0xFF5D62);

        public static Color Hex(int rgb) { return Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb); }

        public static IBrush Brush(Color c) { return new SolidColorBrush(c); }

        public static Color LevelColor(int percent, bool charging)
        {
            if (charging) return Accent;
            if (percent <= 20) return Bad;
            if (percent <= 50) return Warn;
            return Good;
        }

        /// HSL lightness, as System.Drawing.Color.GetBrightness on Windows.
        public static double Brightness(Color c)
        {
            int max = System.Math.Max(c.R, System.Math.Max(c.G, c.B)), min = System.Math.Min(c.R, System.Math.Min(c.G, c.B));
            return (max + min) / 510.0;
        }
    }
}
