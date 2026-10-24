// CompxDevice.cs - the configuration protocol of Compx-based wireless mice, spoken through the receiver's vendor HID interface.
//
// Frame (17 bytes incl. report ID), sent as output report 0x08, answered on input report 0x08:
//   [0]=0x08 [1]=command [2]=status [3..4]=address (big-endian) [5]=length [6..15]=data [16]=checksum
//   checksum: the sum of all 17 bytes is 0x55 (mod 256)
// Commands: 0x03 online, 0x04 battery, 0x07 write EEPROM, 0x08 read EEPROM, 0x10 CID/MID,
//           0x12 mouse firmware, 0x1D receiver firmware.
//
// Written for C# 5 so it compiles with the csc.exe that ships with Windows (.NET Framework 4.x).
// The HID access itself lives in HidChannels: WinHid.cs on Windows, cross/Core/HidSharpChannel.cs on macOS and Linux.
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace CompxTray.Device
{
    public class MouseStatus
    {
        public bool Ok;
        public bool ReceiverFound;
        public int Percent;
        public bool Charging;
        public int MilliVolts;
        public int StatusByte;
        public string Message = "";
        public string DeviceName = "";
        public DateTime Time = DateTime.Now;
    }

    /// An open configuration interface of a receiver, provided by the platform's HidChannels.
    public interface IHidChannel : IDisposable
    {
        string ProductName { get; }
        ushort Vid { get; }
        ushort Pid { get; }
        /// Sends one output report. On failure, error says why.
        bool Write(byte[] packet, out string error);
        /// Returns the next input report (report ID first), or null on timeout.
        byte[] Read(int timeoutMs);
    }

    public sealed class CompxDevice : IDisposable
    {
        public const byte ReportId = 0x08;
        public const int PacketLen = 17;
        public const byte CmdOnline = 0x03;
        public const byte CmdBattery = 0x04;
        public const byte CmdWriteEeprom = 0x07;
        public const byte CmdReadEeprom = 0x08;
        public const byte CmdCidMid = 0x10;
        public const byte CmdMouseVersion = 0x12;
        public const byte CmdReceiverVersion = 0x1D;

        // Compx uses these vendor IDs for its own and white-label receivers.
        public static readonly ushort[] VendorIds = { 0x373B, 0x3554 };

        readonly IHidChannel channel;

        public string ProductName { get; private set; }
        public ushort Vid { get { return channel.Vid; } }
        public ushort Pid { get { return channel.Pid; } }
        public string Name { get { return string.Format("{0} ({1:X4}:{2:X4})", ProductName, Vid, Pid); } }

        /// Raw traffic, useful for diagnostics.
        public readonly List<string> Log = new List<string>();

        public CompxDevice(IHidChannel channel)
        {
            this.channel = channel;
            ProductName = channel.ProductName.Length > 0 ? channel.ProductName : "Compx mouse";
        }

        /// Opens the first Compx configuration interface. Returns null and a reason if none is available.
        public static CompxDevice Open(out string error)
        {
            var ch = HidChannels.OpenFirst(VendorIds, PacketLen, out error);
            return ch == null ? null : new CompxDevice(ch);
        }

        public void Dispose()
        {
            try { channel.Dispose(); } catch { }
        }

        // ---------- Frames ----------
        public static byte[] Build(byte cmd, int address, int length, byte[] data)
        {
            var p = new byte[PacketLen];
            p[0] = ReportId; p[1] = cmd; p[2] = 0;
            p[3] = (byte)((address >> 8) & 0xFF); p[4] = (byte)(address & 0xFF);
            p[5] = (byte)length;
            if (data != null) for (int i = 0; i < Math.Min(data.Length, 10); i++) p[6 + i] = data[i];
            p[PacketLen - 1] = Checksum(p);
            return p;
        }

        public static byte[] BuildSimple(byte cmd) { return Build(cmd, 0, 0, null); }

        static byte Checksum(byte[] p)
        {
            int sum = 0;
            for (int i = 0; i < PacketLen - 1; i++) sum += p[i];
            return (byte)((0x55 - sum) & 0xFF);
        }

        public static bool ChecksumOk(byte[] b)
        {
            if (b == null || b.Length < PacketLen) return false;
            int sum = 0;
            for (int i = 0; i < PacketLen; i++) sum += b[i];
            return (sum & 0xFF) == 0x55;
        }

        public static string Hex(byte[] b)
        {
            if (b == null) return "(null)";
            var sb = new StringBuilder();
            for (int i = 0; i < b.Length; i++) sb.Append(b[i].ToString("X2")).Append(' ');
            return sb.ToString().TrimEnd();
        }

        // ---------- Transport ----------
        bool Send(byte[] packet)
        {
            Log.Add("-> " + Hex(packet));
            string error;
            if (channel.Write(packet, out error)) return true;
            Log.Add("   send failed, " + error);
            return false;
        }

        /// Sends a frame and returns the first valid reply accepted by match, or null on timeout.
        public byte[] Exchange(byte[] packet, Func<byte[], bool> match, int timeoutMs)
        {
            if (!Send(packet)) return null;
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (true)
            {
                int left = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (left < 1) break;
                var r = channel.Read(left);
                if (r == null) break;
                if (r.Length < PacketLen) continue;
                Log.Add("<- " + Hex(r));
                if (r[0] == ReportId && ChecksumOk(r) && match(r)) return r;
            }
            Log.Add("   timeout");
            return null;
        }

        public byte[] Command(byte cmd, int timeoutMs)
        {
            return Exchange(BuildSimple(cmd), r => r[1] == cmd, timeoutMs);
        }

        // ---------- EEPROM ----------
        /// Reads 1-10 bytes. Returns null if the mouse does not answer (asleep, off or out of range).
        public byte[] ReadEeprom(int address, int len)
        {
            byte hi = (byte)((address >> 8) & 0xFF), lo = (byte)(address & 0xFF);
            var packet = Build(CmdReadEeprom, address, len, null);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                // Some firmware answers a read with the write command id.
                var r = Exchange(packet, x => (x[1] == CmdReadEeprom || x[1] == CmdWriteEeprom) && x[2] == 0
                                             && x[3] == hi && x[4] == lo && x[5] >= len, 600);
                if (r != null)
                {
                    var data = new byte[len];
                    Array.Copy(r, 6, data, 0, len);
                    return data;
                }
            }
            return null;
        }

        /// Writes 1-10 bytes. Returns true if the receiver acknowledged. Callers must verify by reading back.
        public bool WriteEeprom(int address, byte[] data)
        {
            if (data == null || data.Length < 1 || data.Length > 10) throw new ArgumentException("1-10 bytes");
            byte hi = (byte)((address >> 8) & 0xFF), lo = (byte)(address & 0xFF);
            var r = Exchange(Build(CmdWriteEeprom, address, data.Length, data), x => x[1] == CmdWriteEeprom && x[3] == hi && x[4] == lo, 800);
            Thread.Sleep(30);
            return r != null && r[2] == 0;
        }

        // ---------- Battery ----------
        public MouseStatus ReadBattery(int timeoutMs)
        {
            var st = new MouseStatus();
            st.ReceiverFound = true;
            st.DeviceName = ProductName;
            var r = Command(CmdBattery, timeoutMs);
            if (r == null) { st.Message = "The mouse is not answering (asleep?)"; return st; }
            st.StatusByte = r[2];
            st.Percent = r[6];
            st.Charging = r[7] != 0;
            st.MilliVolts = (r[8] << 8) | r[9];
            st.Time = DateTime.Now;
            if (st.StatusByte != 0) { st.Message = "The mouse is asleep or switched off"; return st; }
            if (st.Percent > 100) { st.Message = "Invalid battery value " + st.Percent; return st; }
            st.Ok = true;
            st.Message = "OK";
            return st;
        }

        /// Mouse firmware as "major.minor", or null.
        public string ReadFirmware()
        {
            var v = Command(CmdMouseVersion, 500);
            return v == null ? null : v[6] + "." + v[7].ToString("00");
        }
    }

    /// Serialises all device access within the process.
    public static class CompxHid
    {
        static readonly object gate = new object();

        public static MouseStatus QueryBattery(int timeoutMs)
        {
            lock (gate)
            {
                string err;
                using (var d = CompxDevice.Open(out err))
                {
                    if (d == null)
                    {
                        var none = new MouseStatus();
                        none.Message = err ?? "No Compx receiver found.";
                        return none;
                    }
                    return d.ReadBattery(timeoutMs);
                }
            }
        }

        public static T WithDevice<T>(Func<CompxDevice, T> action, out string error)
        {
            lock (gate)
            {
                using (var d = CompxDevice.Open(out error))
                {
                    if (d == null) return default(T);
                    return action(d);
                }
            }
        }
    }
}
