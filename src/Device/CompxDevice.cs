// CompxDevice.cs - low-level access to Compx-based wireless mice through the receiver's vendor HID interface.
//
// Frame (17 bytes incl. report ID), sent as output report 0x08, answered on input report 0x08:
//   [0]=0x08 [1]=command [2]=status [3..4]=address (big-endian) [5]=length [6..15]=data [16]=checksum
//   checksum: the sum of all 17 bytes is 0x55 (mod 256)
// Commands: 0x03 online, 0x04 battery, 0x07 write EEPROM, 0x08 read EEPROM, 0x10 CID/MID,
//           0x12 mouse firmware, 0x1D receiver firmware.
//
// Written for C# 5 so it compiles with the csc.exe that ships with Windows (.NET Framework 4.x).
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

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
        static readonly ushort[] VendorIds = { 0x373B, 0x3554 };

        public string ProductName { get; private set; }
        public ushort Vid { get; private set; }
        public ushort Pid { get; private set; }
        public string Name { get { return string.Format("{0} ({1:X4}:{2:X4})", ProductName, Vid, Pid); } }

        /// Raw traffic, useful for diagnostics.
        public readonly List<string> Log = new List<string>();

        SafeFileHandle readHandle, writeHandle;
        FileStream readStream;
        int inLen;
        IAsyncResult pending;
        byte[] pendingBuf;

        // ---------- Win32 ----------
        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVICE_INTERFACE_DATA { public uint cbSize; public Guid InterfaceClassGuid; public uint Flags; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)]
        struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }
        [StructLayout(LayoutKind.Sequential)]
        struct HIDP_CAPS
        {
            public ushort Usage; public ushort UsagePage;
            public ushort InputReportByteLength; public ushort OutputReportByteLength; public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps; public ushort NumberInputValueCaps; public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps; public ushort NumberOutputValueCaps; public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps; public ushort NumberFeatureValueCaps; public ushort NumberFeatureDataIndices;
        }
        [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
        [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
        [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
        [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr p);
        [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr p, ref HIDP_CAPS c);
        [DllImport("hid.dll", CharSet = CharSet.Unicode)] static extern bool HidD_GetProductString(SafeFileHandle h, StringBuilder b, int len);
        [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_SetOutputReport(SafeFileHandle h, byte[] buf, int len);
        [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_FlushQueue(SafeFileHandle h);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr enumerator, IntPtr hwnd, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid g, uint idx, ref SP_DEVICE_INTERFACE_DATA d);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA d, IntPtr detail, uint size, out uint req, IntPtr devInfo);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteFile(SafeFileHandle h, byte[] buf, int len, out int written, IntPtr overlapped);

        const uint GENERIC_RW = 0xC0000000;
        const uint SHARE_RW = 3;
        const uint OPEN_EXISTING = 3;
        const uint FLAG_OVERLAPPED = 0x40000000;

        class Candidate { public string Path; public string Product; public ushort Vid; public ushort Pid; public int InLen; }

        static List<string> ListHidPaths()
        {
            var list = new List<string>();
            Guid g; HidD_GetHidGuid(out g);
            IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12); // PRESENT | DEVICEINTERFACE
            if (set == IntPtr.Zero || set == new IntPtr(-1)) return list;
            var d = new SP_DEVICE_INTERFACE_DATA();
            d.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
            for (uint i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d); i++)
            {
                uint req;
                SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out req, IntPtr.Zero);
                if (req == 0) continue;
                IntPtr buf = Marshal.AllocHGlobal((int)req);
                try
                {
                    Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetail(set, ref d, buf, req, out req, IntPtr.Zero))
                        list.Add(Marshal.PtrToStringUni(new IntPtr(buf.ToInt64() + 4)));
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            SetupDiDestroyDeviceInfoList(set);
            return list;
        }

        static List<Candidate> FindCandidates()
        {
            var result = new List<Candidate>();
            foreach (var path in ListHidPaths())
            {
                string lower = path.ToLowerInvariant();
                bool vidMatch = false;
                foreach (var v in VendorIds) if (lower.Contains("vid_" + v.ToString("x4"))) vidMatch = true;
                if (!vidMatch) continue;
                using (var h = CreateFile(path, 0, SHARE_RW, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero))
                {
                    if (h.IsInvalid) continue;
                    var a = new HIDD_ATTRIBUTES(); a.Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES));
                    if (!HidD_GetAttributes(h, ref a)) continue;
                    IntPtr pp;
                    if (!HidD_GetPreparsedData(h, out pp)) continue;
                    var c = new HIDP_CAPS();
                    try { HidP_GetCaps(pp, ref c); } finally { HidD_FreePreparsedData(pp); }
                    // The configuration channel: vendor usage page with 17-byte reports.
                    if (c.UsagePage < 0xFF00 || c.OutputReportByteLength != PacketLen || c.InputReportByteLength < PacketLen) continue;
                    var prod = new StringBuilder(256); HidD_GetProductString(h, prod, 512);
                    var cand = new Candidate();
                    cand.Path = path; cand.InLen = c.InputReportByteLength;
                    cand.Product = prod.ToString(); cand.Vid = a.VendorID; cand.Pid = a.ProductID;
                    result.Add(cand);
                }
            }
            return result;
        }

        /// Opens the first Compx configuration interface. Returns null and a reason if none is available.
        public static CompxDevice Open(out string error)
        {
            error = null;
            var cands = FindCandidates();
            if (cands.Count == 0) { error = "No Compx receiver found. Is the dongle plugged in?"; return null; }
            foreach (var c in cands)
            {
                var rh = CreateFile(c.Path, GENERIC_RW, SHARE_RW, IntPtr.Zero, OPEN_EXISTING, FLAG_OVERLAPPED, IntPtr.Zero);
                if (rh.IsInvalid) { error = "Could not open the receiver (error " + Marshal.GetLastWin32Error() + ")."; continue; }
                var wh = CreateFile(c.Path, GENERIC_RW, SHARE_RW, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                var d = new CompxDevice();
                d.readHandle = rh;
                d.writeHandle = wh.IsInvalid ? null : wh;
                d.inLen = c.InLen;
                d.readStream = new FileStream(rh, FileAccess.Read, c.InLen, true);
                d.ProductName = c.Product.Length > 0 ? c.Product : "Compx mouse";
                d.Vid = c.Vid; d.Pid = c.Pid;
                HidD_FlushQueue(rh);
                error = null;
                return d;
            }
            return null;
        }

        public void Dispose()
        {
            try { if (readStream != null) readStream.Dispose(); } catch { }
            try { if (writeHandle != null) writeHandle.Dispose(); } catch { }
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
            int written;
            if (writeHandle != null && WriteFile(writeHandle, packet, packet.Length, out written, IntPtr.Zero) && written == packet.Length) return true;
            if (writeHandle != null && HidD_SetOutputReport(writeHandle, packet, packet.Length)) return true;
            return HidD_SetOutputReport(readHandle, packet, packet.Length);
        }

        byte[] ReadReport(int timeoutMs)
        {
            if (pending == null)
            {
                pendingBuf = new byte[inLen];
                pending = readStream.BeginRead(pendingBuf, 0, inLen, null, null);
            }
            if (timeoutMs < 1 || !pending.AsyncWaitHandle.WaitOne(timeoutMs)) return null;
            int n = readStream.EndRead(pending);
            var b = pendingBuf;
            pending = null; pendingBuf = null;
            if (n < PacketLen) return new byte[0];
            Log.Add("<- " + Hex(b));
            return b;
        }

        /// Sends a frame and returns the first valid reply accepted by match, or null on timeout.
        public byte[] Exchange(byte[] packet, Func<byte[], bool> match, int timeoutMs)
        {
            if (!Send(packet)) { Log.Add("   send failed, error " + Marshal.GetLastWin32Error()); return null; }
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (true)
            {
                int left = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (left < 1) break;
                var r = ReadReport(left);
                if (r == null) break;
                if (r.Length >= PacketLen && r[0] == ReportId && ChecksumOk(r) && match(r)) return r;
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
