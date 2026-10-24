// ConfigMap.cs - the configuration EEPROM layout used by Compx mice of the "A9" generation,
// verified on the Svive Flow 7. Scalar values are stored as pairs [value, 0x55 - value].
//
//   0x0000  system row   [polling code, chk, stage count, chk, active stage, chk, ...]
//   0x000A  lift-off     [code, chk]
//   0x000C  DPI table    8 stages x [x, y, high bits, chk]
//   0x002C  stage colors 8 stages x [r, g, b, chk]
//   0x00A9  advanced     [debounce ms, chk, motion sync, chk, sleep (10 s units), chk, ?, chk, ripple, chk]
using System;
using System.Collections.Generic;

namespace CompxTray.Device
{
    public static class ConfigMap
    {
        public const int AddrSystem = 0x0000;
        public const int AddrLiftOff = 0x000A;
        public const int AddrDpi = 0x000C;
        public const int AddrColors = 0x002C;
        public const int AddrAdvanced = 0x00A9;

        // Polling rate codes: code -> Hz
        static readonly int[,] PollingCodes = { { 0x08, 125 }, { 0x04, 250 }, { 0x02, 500 }, { 0x01, 1000 }, { 0x10, 2000 }, { 0x20, 4000 }, { 0x40, 8000 } };

        public static readonly int[] SleepOptions = { 30, 60, 120, 180, 300, 600, 1200, 1800 };
        public static readonly int[] DebounceOptions = { 0, 1, 2, 4, 6, 8, 10, 12, 15 };
        public const int MaxStages = 6;
        public const int DpiStep = 50;

        public static bool PairOk(byte[] row, int offset)
        {
            return row != null && row.Length >= offset + 2 && ((row[offset] + row[offset + 1]) & 0xFF) == 0x55;
        }

        public static byte[] Pair(int value) { return new byte[] { (byte)value, (byte)((0x55 - value) & 0xFF) }; }

        public static int PollingHz(int code)
        {
            for (int i = 0; i < PollingCodes.GetLength(0); i++) if (PollingCodes[i, 0] == code) return PollingCodes[i, 1];
            return -1;
        }

        public static int PollingCode(int hz)
        {
            for (int i = 0; i < PollingCodes.GetLength(0); i++) if (PollingCodes[i, 1] == hz) return PollingCodes[i, 0];
            return -1;
        }

        /// DPI stage in 50-DPI steps: count = dpi/50 - 1, low byte per axis, two high bits per axis in the third byte.
        public static byte[] PackDpi(int dpi)
        {
            int count = dpi / DpiStep - 1;
            byte low = (byte)(count & 0xFF);
            int high = ((count >> 8) & 3) << 2;
            byte mode = (byte)((high << 4) | high);
            byte chk = (byte)((0x55 - (low + low + mode)) & 0xFF);
            return new byte[] { low, low, mode, chk };
        }

        /// Returns the X-axis DPI of a stage, or -1 if its checksum is invalid.
        public static int UnpackDpi(byte[] table, int offset)
        {
            if (((table[offset] + table[offset + 1] + table[offset + 2] + table[offset + 3]) & 0xFF) != 0x55) return -1;
            int count = table[offset] | (((table[offset + 2] >> 2) & 3) << 8);
            int dpi = (count + 1) * DpiStep;
            return (table[offset + 2] & 1) != 0 ? dpi * 2 : dpi;
        }

        /// Returns a stage colour as 0xRRGGBB, or -1 if invalid.
        public static int UnpackColor(byte[] table, int offset)
        {
            if (((table[offset] + table[offset + 1] + table[offset + 2] + table[offset + 3]) & 0xFF) != 0x55) return -1;
            return (table[offset] << 16) | (table[offset + 1] << 8) | table[offset + 2];
        }

        // ---------- Diagnostics (read-only), used by tools/read-settings.ps1 ----------
        public static List<string> Diagnose(CompxDevice d)
        {
            var o = new List<string>();
            o.Add("Device: " + d.Name);
            var cmds = new KeyValuePair<string, byte>[] {
                new KeyValuePair<string, byte>("online (0x03)", CompxDevice.CmdOnline),
                new KeyValuePair<string, byte>("battery (0x04)", CompxDevice.CmdBattery),
                new KeyValuePair<string, byte>("CID/MID (0x10)", CompxDevice.CmdCidMid),
                new KeyValuePair<string, byte>("mouse fw (0x12)", CompxDevice.CmdMouseVersion),
                new KeyValuePair<string, byte>("receiver fw (0x1D)", CompxDevice.CmdReceiverVersion) };
            foreach (var c in cmds)
            {
                var r = d.Command(c.Value, 600);
                o.Add(string.Format("{0,-20} {1}", c.Key, r == null ? "no answer" : CompxDevice.Hex(r)));
            }
            o.Add("");
            o.Add("EEPROM 0x0000-0x00FF:");
            for (int row = 0; row < 32; row++)
            {
                var data = d.ReadEeprom(row * 8, 8);
                o.Add(string.Format("  {0:X4}: {1}", row * 8, data == null ? "-- no answer --" : CompxDevice.Hex(data)));
            }
            o.Add("");
            try
            {
                var cfg = MouseConfig.Read(d);
                o.Add("Decoded with the A9 map:");
                o.Add("  polling      " + cfg.PollingHz + " Hz");
                o.Add("  DPI stages   " + cfg.StageCount + " (active index " + cfg.ActiveStage + ")");
                for (int i = 0; i < cfg.StageCount; i++) o.Add("    stage " + (i + 1) + "    " + cfg.Dpi[i] + " DPI, colour #" + cfg.Colors[i].ToString("X6"));
                o.Add("  lift-off     code " + cfg.LiftOffCode);
                o.Add("  debounce     " + cfg.DebounceMs + " ms");
                o.Add("  motion sync  " + cfg.MotionSync);
                o.Add("  sleep        " + cfg.SleepSeconds + " s");
            }
            catch (Exception ex) { o.Add("Could not decode with the A9 map: " + ex.Message); }
            return o;
        }
    }

    /// Models whose configuration map has been verified on real hardware.
    public class MouseModel
    {
        public ushort Vid, Pid;
        public string Brand, Name;
        public bool SettingsVerified;
        public int[] PollingRates;
        public int MinDpi, MaxDpi;

        public string DisplayName { get { return (Brand + " " + Name).Trim(); } }

        static readonly List<MouseModel> Known = new List<MouseModel>
        {
            new MouseModel { Vid = 0x373B, Pid = 0x11B3, Brand = "Svive", Name = "Flow 7", SettingsVerified = true,
                             PollingRates = new[] { 125, 250, 500, 1000 }, MinDpi = 100, MaxDpi = 30000 },
        };

        public static MouseModel Find(ushort vid, ushort pid, string product)
        {
            foreach (var m in Known) if (m.Vid == vid && m.Pid == pid) return m;
            return new MouseModel
            {
                Vid = vid, Pid = pid, Brand = "", Name = string.IsNullOrEmpty(product) ? "Compx mouse" : product,
                SettingsVerified = false, PollingRates = new[] { 125, 250, 500, 1000 }, MinDpi = 100, MaxDpi = 26000
            };
        }
    }
}
