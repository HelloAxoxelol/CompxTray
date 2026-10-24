// WinHid.cs - the receiver's configuration interface on Windows, through hid.dll and setupapi.dll.
// Written for C# 5 so it compiles with the csc.exe that ships with Windows (.NET Framework 4.x).
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace CompxTray.Device
{
    public static class HidChannels
    {
        /// Opens the first vendor interface with packetLen-byte reports on one of the vendor IDs. Returns null and a reason if none is available.
        public static IHidChannel OpenFirst(ushort[] vendorIds, int packetLen, out string error)
        {
            error = null;
            var cands = WinHidChannel.FindCandidates(vendorIds, packetLen);
            if (cands.Count == 0) { error = "No Compx receiver found. Is the dongle plugged in?"; return null; }
            foreach (var c in cands)
            {
                var ch = WinHidChannel.Open(c, out error);
                if (ch != null) return ch;
            }
            return null;
        }
    }

    sealed class WinHidChannel : IHidChannel
    {
        public string ProductName { get; private set; }
        public ushort Vid { get; private set; }
        public ushort Pid { get; private set; }

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

        public class Candidate { public string Path; public string Product; public ushort Vid; public ushort Pid; public int InLen; }

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

        public static List<Candidate> FindCandidates(ushort[] vendorIds, int packetLen)
        {
            var result = new List<Candidate>();
            foreach (var path in ListHidPaths())
            {
                string lower = path.ToLowerInvariant();
                bool vidMatch = false;
                foreach (var v in vendorIds) if (lower.Contains("vid_" + v.ToString("x4"))) vidMatch = true;
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
                    if (c.UsagePage < 0xFF00 || c.OutputReportByteLength != packetLen || c.InputReportByteLength < packetLen) continue;
                    var prod = new StringBuilder(256); HidD_GetProductString(h, prod, 512);
                    var cand = new Candidate();
                    cand.Path = path; cand.InLen = c.InputReportByteLength;
                    cand.Product = prod.ToString(); cand.Vid = a.VendorID; cand.Pid = a.ProductID;
                    result.Add(cand);
                }
            }
            return result;
        }

        public static WinHidChannel Open(Candidate c, out string error)
        {
            error = null;
            var rh = CreateFile(c.Path, GENERIC_RW, SHARE_RW, IntPtr.Zero, OPEN_EXISTING, FLAG_OVERLAPPED, IntPtr.Zero);
            if (rh.IsInvalid) { error = "Could not open the receiver (error " + Marshal.GetLastWin32Error() + ")."; return null; }
            var wh = CreateFile(c.Path, GENERIC_RW, SHARE_RW, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            var ch = new WinHidChannel();
            ch.readHandle = rh;
            ch.writeHandle = wh.IsInvalid ? null : wh;
            ch.inLen = c.InLen;
            ch.readStream = new FileStream(rh, FileAccess.Read, c.InLen, true);
            ch.ProductName = c.Product;
            ch.Vid = c.Vid; ch.Pid = c.Pid;
            HidD_FlushQueue(rh);
            return ch;
        }

        public void Dispose()
        {
            try { if (readStream != null) readStream.Dispose(); } catch { }
            try { if (writeHandle != null) writeHandle.Dispose(); } catch { }
        }

        public bool Write(byte[] packet, out string error)
        {
            error = null;
            int written;
            if (writeHandle != null && WriteFile(writeHandle, packet, packet.Length, out written, IntPtr.Zero) && written == packet.Length) return true;
            if (writeHandle != null && HidD_SetOutputReport(writeHandle, packet, packet.Length)) return true;
            if (HidD_SetOutputReport(readHandle, packet, packet.Length)) return true;
            error = "error " + Marshal.GetLastWin32Error();
            return false;
        }

        public byte[] Read(int timeoutMs)
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
            if (n < b.Length) Array.Resize(ref b, n);
            return b;
        }
    }
}
