// HidSharpChannel.cs - the receiver's configuration interface on macOS and Linux, through HidSharp.
// Also works on Windows, which is how it is tested against a real mouse.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using HidSharp;

namespace CompxTray.Device
{
    public static class HidChannels
    {
        /// Opens the first vendor interface with packetLen-byte reports on one of the vendor IDs. Returns null and a reason if none is available.
        public static IHidChannel OpenFirst(ushort[] vendorIds, int packetLen, out string error)
        {
            error = null;
            var cands = FindCandidates(vendorIds, packetLen);
            if (cands.Count == 0) { error = "No Compx receiver found. Is the dongle plugged in?"; return null; }
            foreach (var dev in cands)
            {
                try
                {
                    if (dev.TryOpen(out HidStream stream)) return new HidSharpChannel(dev, stream);
                    error = "Could not open the receiver." + PermissionHint();
                }
                catch (Exception ex) { error = "Could not open the receiver: " + ex.Message + PermissionHint(); }
            }
            return null;
        }

        static List<HidDevice> FindCandidates(ushort[] vendorIds, int packetLen)
        {
            var list = new List<HidDevice>();
            foreach (var dev in DeviceList.Local.GetHidDevices())
            {
                if (Array.IndexOf(vendorIds, (ushort)dev.VendorID) < 0) continue;
                try { if (IsConfigInterface(dev, packetLen)) list.Add(dev); } catch { }
            }
            return list;
        }

        // The configuration channel: a vendor usage page with report 0x08 of packetLen bytes in both directions.
        static bool IsConfigInterface(HidDevice dev, int packetLen)
        {
            var desc = dev.GetReportDescriptor();
            bool vendor = desc.DeviceItems.Any(item => item.Usages.GetAllValues().Any(u => (u >> 16) >= 0xFF00));
            bool output = desc.OutputReports.Any(r => r.ReportID == CompxDevice.ReportId && r.Length == packetLen);
            bool input = desc.InputReports.Any(r => r.ReportID == CompxDevice.ReportId && r.Length >= packetLen);
            return vendor && output && input;
        }

        static string PermissionHint()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return " On Linux, install the udev rule that comes with CompxTray, then unplug and replug the receiver.";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return " On macOS, allow CompxTray under System Settings > Privacy & Security > Input Monitoring.";
            return "";
        }

        /// One line per HID interface on a Compx vendor ID, for bug reports.
        public static List<string> Describe(ushort[] vendorIds, int packetLen)
        {
            var o = new List<string>();
            foreach (var dev in DeviceList.Local.GetHidDevices())
            {
                if (Array.IndexOf(vendorIds, (ushort)dev.VendorID) < 0) continue;
                string product = "", usages = "?", config = "?";
                try { product = dev.GetProductName(); } catch { }
                try
                {
                    var desc = dev.GetReportDescriptor();
                    usages = string.Join(" ", desc.DeviceItems.SelectMany(i => i.Usages.GetAllValues()).Select(u => u.ToString("X8")));
                    config = IsConfigInterface(dev, packetLen) ? "yes" : "no";
                }
                catch (Exception ex) { usages = "descriptor unreadable: " + ex.Message; }
                o.Add(string.Format("{0:X4}:{1:X4}  {2}  in {3} / out {4} bytes  usages {5}  config interface: {6}\n    {7}",
                    dev.VendorID, dev.ProductID, product, SafeLen(dev.GetMaxInputReportLength), SafeLen(dev.GetMaxOutputReportLength),
                    usages, config, dev.DevicePath));
            }
            if (o.Count == 0) o.Add("No HID interfaces with a Compx vendor ID (" + string.Join(", ", vendorIds.Select(v => v.ToString("X4"))) + ").");
            return o;
        }

        static string SafeLen(Func<int> f)
        {
            try { return f().ToString(); } catch { return "?"; }
        }
    }

    sealed class HidSharpChannel : IHidChannel
    {
        readonly HidStream stream;
        readonly int inLen;

        public string ProductName { get; }
        public ushort Vid { get; }
        public ushort Pid { get; }

        public HidSharpChannel(HidDevice dev, HidStream stream)
        {
            this.stream = stream;
            inLen = dev.GetMaxInputReportLength();
            Vid = (ushort)dev.VendorID;
            Pid = (ushort)dev.ProductID;
            string name = "";
            try { name = dev.GetProductName() ?? ""; } catch { }
            ProductName = name;
        }

        public bool Write(byte[] packet, out string error)
        {
            error = null;
            try { stream.Write(packet); return true; }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        public byte[] Read(int timeoutMs)
        {
            if (timeoutMs < 1) return null;
            var buf = new byte[inLen];
            try
            {
                stream.ReadTimeout = timeoutMs;
                int n = stream.Read(buf, 0, buf.Length);
                if (n < buf.Length) Array.Resize(ref buf, n);
                return buf;
            }
            catch (TimeoutException) { return null; }
            catch (IOException) { return null; }
        }

        public void Dispose()
        {
            try { stream.Dispose(); } catch { }
        }
    }
}
