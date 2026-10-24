// HidProbe.cs - lists HID interfaces with their report layout (read-only: GetFeature and passive listening only).
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

public static class HidProbe
{
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
    [DllImport("hid.dll")] static extern int HidP_GetValueCaps(int type, [Out] byte[] caps, ref ushort len, IntPtr p);
    [DllImport("hid.dll")] static extern int HidP_GetButtonCaps(int type, [Out] byte[] caps, ref ushort len, IntPtr p);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)] static extern bool HidD_GetProductString(SafeFileHandle h, StringBuilder b, int len);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)] static extern bool HidD_GetManufacturerString(SafeFileHandle h, StringBuilder b, int len);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetFeature(SafeFileHandle h, [In, Out] byte[] buf, int len);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr enumerator, IntPtr hwnd, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid g, uint idx, ref SP_DEVICE_INTERFACE_DATA d);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA d, IntPtr detail, uint size, out uint req, IntPtr devInfo);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);

    const uint GENERIC_RW = 0xC0000000;
    const uint SHARE_RW = 3;
    const uint OPEN_EXISTING = 3;
    const uint FLAG_OVERLAPPED = 0x40000000;

    public static List<string> ListPaths()
    {
        var list = new List<string>();
        Guid g; HidD_GetHidGuid(out g);
        IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12); // PRESENT | DEVICEINTERFACE
        var d = new SP_DEVICE_INTERFACE_DATA();
        d.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
        for (uint i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d); i++)
        {
            uint req;
            SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out req, IntPtr.Zero);
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

    static string Hex(byte[] b, int n)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < n && i < b.Length; i++) sb.Append(b[i].ToString("X2")).Append(' ');
        return sb.ToString().TrimEnd();
    }

    // Unique report IDs per type (0=Input, 1=Output, 2=Feature)
    static SortedSet<byte> ReportIds(IntPtr pp, int type, ushort nButton, ushort nValue, List<string> detail)
    {
        var ids = new SortedSet<byte>();
        if (nValue > 0)
        {
            ushort n = nValue; var caps = new byte[72 * n];
            if (HidP_GetValueCaps(type, caps, ref n, pp) == 0x00110000)
                for (int i = 0; i < n; i++)
                {
                    int o = i * 72; ids.Add(caps[o + 2]);
                    detail.Add(string.Format("      value  id {0:X2} UP {1:X4} usage {2:X4} bits {3} count {4}",
                        caps[o + 2], BitConverter.ToUInt16(caps, o), BitConverter.ToUInt16(caps, o + 56),
                        BitConverter.ToUInt16(caps, o + 18), BitConverter.ToUInt16(caps, o + 20)));
                }
        }
        if (nButton > 0)
        {
            ushort n = nButton; var caps = new byte[72 * n];
            if (HidP_GetButtonCaps(type, caps, ref n, pp) == 0x00110000)
                for (int i = 0; i < n; i++) ids.Add(caps[i * 72 + 2]);
        }
        return ids;
    }

    static string Join(SortedSet<byte> s)
    {
        var parts = new List<string>();
        foreach (var b in s) parts.Add(b.ToString("X2"));
        return parts.Count == 0 ? "-" : string.Join(",", parts.ToArray());
    }

    public static List<string> Run(int listenMs)
    {
        var r = new List<string>();
        foreach (var path in ListPaths())
        {
            try { Describe(path, listenMs, r); }
            catch (Exception ex) { r.Add("ERROR " + ex.Message + " | " + path); }
        }
        return r;
    }

    static void Describe(string path, int listenMs, List<string> r)
    {
        string mode = "RW";
        var h = CreateFile(path, GENERIC_RW, SHARE_RW, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (h.IsInvalid) { mode = "--"; h = CreateFile(path, 0, SHARE_RW, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero); }
        if (h.IsInvalid) { r.Add("CANNOT OPEN (" + Marshal.GetLastWin32Error() + "): " + path); return; }

        using (h)
        {
            var a = new HIDD_ATTRIBUTES(); a.Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES));
            HidD_GetAttributes(h, ref a);
            var prod = new StringBuilder(256); HidD_GetProductString(h, prod, 512);
            var manu = new StringBuilder(256); HidD_GetManufacturerString(h, manu, 512);

            IntPtr pp;
            if (!HidD_GetPreparsedData(h, out pp)) { r.Add("No preparsed data: " + path); return; }
            try
            {
                var c = new HIDP_CAPS(); HidP_GetCaps(pp, ref c);
                r.Add("");
                r.Add(string.Format("VID_{0:X4} PID_{1:X4} rev {2:X4} | UsagePage 0x{3:X4} Usage 0x{4:X4} | In {5} Out {6} Feat {7} | open {8} | \"{9}\" / \"{10}\"",
                    a.VendorID, a.ProductID, a.VersionNumber, c.UsagePage, c.Usage,
                    c.InputReportByteLength, c.OutputReportByteLength, c.FeatureReportByteLength, mode, manu, prod));
                r.Add("   path: " + path);

                var detail = new List<string>();
                var inIds = ReportIds(pp, 0, c.NumberInputButtonCaps, c.NumberInputValueCaps, detail);
                var outIds = ReportIds(pp, 1, c.NumberOutputButtonCaps, c.NumberOutputValueCaps, detail);
                var featIds = ReportIds(pp, 2, c.NumberFeatureButtonCaps, c.NumberFeatureValueCaps, detail);
                r.Add("   report-ID  in: " + Join(inIds) + "  out: " + Join(outIds) + "  feat: " + Join(featIds));

                bool vendor = c.UsagePage >= 0xFF00;
                bool logitech = a.VendorID == 0x046D;
                if (!vendor || logitech) return;

                r.AddRange(detail);

                // Read feature reports (a GET, changes nothing on the device)
                if (c.FeatureReportByteLength > 0)
                    foreach (var id in featIds)
                    {
                        var buf = new byte[c.FeatureReportByteLength]; buf[0] = id;
                        if (HidD_GetFeature(h, buf, buf.Length)) r.Add(string.Format("   GetFeature {0:X2}: {1}", id, Hex(buf, buf.Length)));
                        else r.Add(string.Format("   GetFeature {0:X2}: error {1}", id, Marshal.GetLastWin32Error()));
                    }

                // Listen passively for input reports for a short while
                if (c.InputReportByteLength > 0 && listenMs > 0)
                    Listen(path, c.InputReportByteLength, listenMs, r);
            }
            finally { HidD_FreePreparsedData(pp); }
        }
    }

    static void Listen(string path, int len, int ms, List<string> r)
    {
        var h = CreateFile(path, 0x80000000, SHARE_RW, IntPtr.Zero, OPEN_EXISTING, FLAG_OVERLAPPED, IntPtr.Zero);
        if (h.IsInvalid) { r.Add("   listen: cannot open for reading (" + Marshal.GetLastWin32Error() + ")"); return; }
        int got = 0;
        using (var fs = new FileStream(h, FileAccess.Read, len, true))
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < deadline && got < 20)
            {
                var buf = new byte[len];
                IAsyncResult ar;
                try { ar = fs.BeginRead(buf, 0, len, null, null); }
                catch (Exception ex) { r.Add("   listen: " + ex.Message); break; }
                int left = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (left < 1 || !ar.AsyncWaitHandle.WaitOne(left)) break;
                int n;
                try { n = fs.EndRead(ar); } catch (Exception ex) { r.Add("   listen: " + ex.Message); break; }
                if (n <= 0) break;
                got++;
                r.Add(string.Format("   input {0:HH:mm:ss.fff}: {1}", DateTime.Now, Hex(buf, n)));
            }
        }
        if (got == 0) r.Add("   listen: no input reports in " + ms + " ms");
    }
}
