// MouseConfig.cs - reads, changes and restores mouse settings. Every write is verified by reading it back.
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace CompxTray.Device
{
    public class MouseConfig
    {
        public int PollingHz;
        public int StageCount;
        public int ActiveStage;          // as stored by the mouse (zero-based on the Flow 7)
        public int[] Dpi = new int[8];
        public int[] Colors = new int[8];
        public int LiftOffCode;
        public int DebounceMs;
        public bool MotionSync;
        public int SleepSeconds;

        // Raw rows, needed to preserve unknown fields and for backups.
        public byte[] SystemRow;   // 0x0000, 10 bytes
        public byte[] LiftOffRow;  // 0x000A, 2 bytes
        public byte[] DpiTable;    // 0x000C, 32 bytes
        public byte[] AdvancedRow; // 0x00A9, 10 bytes

        public MouseConfig Clone()
        {
            var c = (MouseConfig)MemberwiseClone();
            c.Dpi = (int[])Dpi.Clone();
            c.Colors = (int[])Colors.Clone();
            c.SystemRow = (byte[])SystemRow.Clone();
            c.LiftOffRow = (byte[])LiftOffRow.Clone();
            c.DpiTable = (byte[])DpiTable.Clone();
            c.AdvancedRow = (byte[])AdvancedRow.Clone();
            return c;
        }

        public bool SameSettings(MouseConfig o)
        {
            if (o == null) return false;
            if (PollingHz != o.PollingHz || StageCount != o.StageCount || LiftOffCode != o.LiftOffCode
                || DebounceMs != o.DebounceMs || MotionSync != o.MotionSync || SleepSeconds != o.SleepSeconds) return false;
            for (int i = 0; i < ConfigMap.MaxStages; i++) if (Dpi[i] != o.Dpi[i]) return false;
            return true;
        }

        /// Sample settings for demo mode (no mouse needed).
        public static MouseConfig Demo()
        {
            var c = new MouseConfig();
            c.PollingHz = 1000; c.StageCount = 4; c.ActiveStage = 2;
            c.Dpi = new[] { 400, 800, 1600, 3200, 6400, 6400, 6400, 6400 };
            c.Colors = new[] { 0xFF2D2D, 0x2DFF6E, 0x2D7BFF, 0xFF2DD4, 0xFFC02D, 0x2DF0FF, 0xFFFFFF, 0xFFFFFF };
            c.LiftOffCode = 1; c.DebounceMs = 8; c.MotionSync = true; c.SleepSeconds = 60;
            c.SystemRow = new byte[10]; c.LiftOffRow = new byte[2]; c.DpiTable = new byte[32]; c.AdvancedRow = new byte[10];
            return c;
        }

        // ---------- Read ----------
        /// Reads everything. Throws with a readable message if the mouse does not answer or the memory looks unfamiliar.
        public static MouseConfig Read(CompxDevice d)
        {
            var c = new MouseConfig();
            c.SystemRow = Need(d.ReadEeprom(ConfigMap.AddrSystem, 10));
            c.LiftOffRow = Need(d.ReadEeprom(ConfigMap.AddrLiftOff, 2));
            c.DpiTable = ReadBlock(d, ConfigMap.AddrDpi, 32);
            var colors = ReadBlock(d, ConfigMap.AddrColors, 32);
            c.AdvancedRow = Need(d.ReadEeprom(ConfigMap.AddrAdvanced, 10));
            c.Decode(colors);
            return c;
        }

        static byte[] ReadBlock(CompxDevice d, int address, int len)
        {
            var block = new byte[len];
            for (int i = 0; i < len; i += 8) Array.Copy(Need(d.ReadEeprom(address + i, 8)), 0, block, i, 8);
            return block;
        }

        static byte[] Need(byte[] data)
        {
            if (data == null) throw new Exception("The mouse is not answering. Move it to wake it up and try again.");
            return data;
        }

        void Decode(byte[] colors)
        {
            var s = SystemRow;
            if (!ConfigMap.PairOk(s, 0) || !ConfigMap.PairOk(s, 2) || !ConfigMap.PairOk(s, 4))
                throw new Exception("Unrecognised system memory. Stopping to be safe.");
            PollingHz = ConfigMap.PollingHz(s[0]);
            if (PollingHz < 0) throw new Exception("Unknown polling code 0x" + s[0].ToString("X2") + ". Stopping to be safe.");
            StageCount = s[2];
            ActiveStage = s[4];
            if (StageCount < 1 || StageCount > 8) throw new Exception("Unexpected number of DPI stages (" + StageCount + ").");
            for (int i = 0; i < 8; i++)
            {
                Dpi[i] = ConfigMap.UnpackDpi(DpiTable, i * 4);
                Colors[i] = ConfigMap.UnpackColor(colors, i * 4);
            }
            for (int i = 0; i < StageCount; i++)
                if (Dpi[i] < 0) throw new Exception("DPI stage " + (i + 1) + " has an invalid checksum.");
            LiftOffCode = ConfigMap.PairOk(LiftOffRow, 0) ? LiftOffRow[0] : -1;

            var a = AdvancedRow;
            if (!ConfigMap.PairOk(a, 0) || !ConfigMap.PairOk(a, 2) || !ConfigMap.PairOk(a, 4))
                throw new Exception("Unrecognised advanced settings. Stopping to be safe.");
            DebounceMs = a[0];
            MotionSync = a[2] == 1;
            SleepSeconds = a[4] * 10;
        }

        // ---------- Write ----------
        /// Writes only what changed. Returns one line per change, starting with "OK" or "FAILED".
        public static List<string> Apply(CompxDevice d, MouseConfig old, MouseConfig neu)
        {
            if (ConfigMap.PollingCode(neu.PollingHz) < 0) throw new Exception("Invalid polling rate.");
            if (neu.SleepSeconds < 10 || neu.SleepSeconds > 2550) throw new Exception("Invalid sleep time.");
            if (neu.DebounceMs < 0 || neu.DebounceMs > 20) throw new Exception("Invalid debounce time.");
            if (neu.StageCount < 1 || neu.StageCount > ConfigMap.MaxStages) throw new Exception("Invalid number of DPI stages.");

            var log = new List<string>();

            if (neu.PollingHz != old.PollingHz)
                Write(d, log, "Polling rate " + neu.PollingHz + " Hz", ConfigMap.AddrSystem, ConfigMap.Pair(ConfigMap.PollingCode(neu.PollingHz)));

            // DPI values first, so new stages hold valid values before the count grows.
            for (int i = 0; i < ConfigMap.MaxStages; i++)
                if (neu.Dpi[i] != old.Dpi[i] && neu.Dpi[i] > 0)
                    Write(d, log, "DPI stage " + (i + 1) + ": " + neu.Dpi[i], ConfigMap.AddrDpi + i * 4, ConfigMap.PackDpi(neu.Dpi[i]));

            if (neu.StageCount != old.StageCount)
                Write(d, log, "DPI stages: " + neu.StageCount, ConfigMap.AddrSystem + 2, ConfigMap.Pair(neu.StageCount));

            if (neu.LiftOffCode != old.LiftOffCode && neu.LiftOffCode > 0)
                Write(d, log, "Lift-off: " + neu.LiftOffCode, ConfigMap.AddrLiftOff, ConfigMap.Pair(neu.LiftOffCode));

            if (neu.DebounceMs != old.DebounceMs || neu.MotionSync != old.MotionSync || neu.SleepSeconds != old.SleepSeconds)
            {
                var row = (byte[])old.AdvancedRow.Clone();
                Array.Copy(ConfigMap.Pair(neu.DebounceMs), 0, row, 0, 2);
                Array.Copy(ConfigMap.Pair(neu.MotionSync ? 1 : 0), 0, row, 2, 2);
                Array.Copy(ConfigMap.Pair(neu.SleepSeconds / 10), 0, row, 4, 2);
                var parts = new List<string>();
                if (neu.DebounceMs != old.DebounceMs) parts.Add("debounce " + neu.DebounceMs + " ms");
                if (neu.MotionSync != old.MotionSync) parts.Add("motion sync " + (neu.MotionSync ? "on" : "off"));
                if (neu.SleepSeconds != old.SleepSeconds) parts.Add("sleep " + FormatSleep(neu.SleepSeconds));
                Write(d, log, string.Join(", ", parts.ToArray()), ConfigMap.AddrAdvanced, row);
            }
            return log;
        }

        static void Write(CompxDevice d, List<string> log, string what, int address, byte[] data)
        {
            d.WriteEeprom(address, data);
            Thread.Sleep(60);
            var back = d.ReadEeprom(address, data.Length);
            bool same = back != null;
            if (same) for (int i = 0; i < data.Length; i++) if (back[i] != data[i]) same = false;
            log.Add((same ? "OK      " : "FAILED  ") + what + (same ? "" : back == null ? " (no answer)" : " (mouse kept " + CompxDevice.Hex(back) + ")"));
        }

        // ---------- Backup and restore ----------
        public string ToBackupText(string deviceName)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# CompxTray backup of the original settings - " + deviceName + " - " + DateTime.Now.ToString("s"));
            sb.AppendLine("0000 " + CompxDevice.Hex(SystemRow));
            sb.AppendLine("000A " + CompxDevice.Hex(LiftOffRow));
            sb.AppendLine("000C " + CompxDevice.Hex(DpiTable));
            sb.AppendLine("00A9 " + CompxDevice.Hex(AdvancedRow));
            return sb.ToString();
        }

        public static Dictionary<int, byte[]> ParseBackup(string text)
        {
            var rows = new Dictionary<int, byte[]>();
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                int addr = Convert.ToInt32(parts[0], 16);
                var data = new byte[parts.Length - 1];
                for (int i = 1; i < parts.Length; i++) data[i - 1] = Convert.ToByte(parts[i], 16);
                rows[addr] = data;
            }
            return rows;
        }

        /// Writes the backed-up settings back (only the bytes that hold settings).
        public static List<string> Restore(CompxDevice d, string backupText)
        {
            var rows = ParseBackup(backupText);
            var log = new List<string>();
            byte[] sys, lod, dpi, adv;
            if (!rows.TryGetValue(0x0000, out sys) || !rows.TryGetValue(0x000A, out lod) || !rows.TryGetValue(0x000C, out dpi) || !rows.TryGetValue(0x00A9, out adv)
                || sys.Length < 6 || lod.Length != 2 || dpi.Length != 32 || adv.Length != 10)
                throw new Exception("The backup file is incomplete.");
            var sys6 = new byte[6]; Array.Copy(sys, sys6, 6);
            Write(d, log, "Polling rate and DPI stages", 0x0000, sys6);
            Write(d, log, "Lift-off", 0x000A, lod);
            for (int i = 0; i < 4; i++)
            {
                var chunk = new byte[8]; Array.Copy(dpi, i * 8, chunk, 0, 8);
                Write(d, log, "DPI table part " + (i + 1), 0x000C + i * 8, chunk);
            }
            Write(d, log, "Debounce, motion sync, sleep", 0x00A9, adv);
            return log;
        }

        public static string FormatSleep(int seconds)
        {
            if (seconds < 60) return seconds + " s";
            if (seconds % 60 == 0) return (seconds / 60) + " min";
            return (seconds / 60) + " min " + (seconds % 60) + " s";
        }
    }
}
