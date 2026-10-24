// Program.cs - compxtray-cli, a read-only command line tool for Compx mice on macOS, Linux and Windows.
using System;
using System.Diagnostics;
using System.Threading;
using CompxTray.Device;

namespace CompxTray.Cli
{
    static class Program
    {
        const string Usage =
@"compxtray-cli - battery and settings of Compx-based wireless mice (Svive Flow 7 and others). Read-only.

Usage: compxtray-cli <command> [--wait <seconds>] [--trace]

  battery     battery level, charging state and voltage
  settings    DPI stages, polling rate, lift-off, debounce, motion sync and sleep
  diagnose    raw replies and the settings memory, for bug reports
  devices     the receiver's HID interfaces, for bug reports

  --wait <s>  keep trying for up to s seconds while the mouse is asleep (move it to wake it)
  --trace     print the raw HID traffic";

        static int Main(string[] args)
        {
            if (args.Length == 0 || args[0] == "-h" || args[0] == "--help" || args[0] == "help") { Console.WriteLine(Usage); return args.Length == 0 ? 1 : 0; }
            int wait = 0;
            bool trace = false;
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--wait" && i + 1 < args.Length && int.TryParse(args[i + 1], out wait)) i++;
                else if (args[i] == "--trace") trace = true;
                else { Console.Error.WriteLine("Unknown option " + args[i] + "\n\n" + Usage); return 1; }
            }

            switch (args[0])
            {
                case "battery": return Retry(wait, trace, Battery);
                case "settings": return Retry(wait, trace, Settings);
                case "diagnose": return Retry(0, trace, Diagnose);
                case "devices":
                    foreach (var line in HidChannels.Describe(CompxDevice.VendorIds, CompxDevice.PacketLen)) Console.WriteLine(line);
                    return 0;
                default:
                    Console.Error.WriteLine("Unknown command " + args[0] + "\n\n" + Usage);
                    return 1;
            }
        }

        /// Runs action until it succeeds (returns 0) or the wait time is up. Exit code 2 means the mouse did not answer.
        static int Retry(int waitSeconds, bool trace, Func<CompxDevice, int> action)
        {
            var sw = Stopwatch.StartNew();
            bool told = false;
            while (true)
            {
                CompxDevice d = CompxDevice.Open(out string err);
                if (d == null) { Console.Error.WriteLine(err); return 3; }
                int rc;
                using (d)
                {
                    rc = action(d);
                    if (trace) foreach (var line in d.Log) Console.Error.WriteLine(line);
                }
                if (rc != 2 || sw.Elapsed.TotalSeconds >= waitSeconds) return rc;
                if (!told) { Console.Error.WriteLine("Waiting for the mouse. Move it to wake it up..."); told = true; }
                Thread.Sleep(1000);
            }
        }

        static int Battery(CompxDevice d)
        {
            var st = d.ReadBattery(1500);
            if (!st.Ok) { Console.Error.WriteLine(st.Message); return 2; }
            Console.WriteLine("{0}: {1} %{2}, {3:0.000} V", d.Name, st.Percent, st.Charging ? " (charging)" : "", st.MilliVolts / 1000.0);
            return 0;
        }

        static int Settings(CompxDevice d)
        {
            var st = d.ReadBattery(1500);
            if (!st.Ok) { Console.Error.WriteLine(st.Message); return 2; }
            MouseConfig c;
            try { c = MouseConfig.Read(d); }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 2; }
            var model = MouseModel.Find(d.Vid, d.Pid, d.ProductName);
            Console.WriteLine("{0}{1}", d.Name, model.SettingsVerified ? "" : "  (model not verified, values may be wrong)");
            Console.WriteLine("  firmware      {0}", d.ReadFirmware() ?? "?");
            Console.WriteLine("  battery       {0} %{1}, {2:0.000} V", st.Percent, st.Charging ? " (charging)" : "", st.MilliVolts / 1000.0);
            Console.WriteLine("  polling rate  {0} Hz", c.PollingHz);
            Console.WriteLine("  DPI stages    {0}, active stage {1}", c.StageCount, c.ActiveStage + 1);
            for (int i = 0; i < c.StageCount; i++)
                Console.WriteLine("    stage {0}     {1} DPI{2}", i + 1, c.Dpi[i], i == c.ActiveStage ? "  <" : "");
            Console.WriteLine("  lift-off      code {0}", c.LiftOffCode);
            Console.WriteLine("  debounce      {0} ms", c.DebounceMs);
            Console.WriteLine("  motion sync   {0}", c.MotionSync ? "on" : "off");
            Console.WriteLine("  sleep after   {0}", MouseConfig.FormatSleep(c.SleepSeconds));
            return 0;
        }

        static int Diagnose(CompxDevice d)
        {
            foreach (var line in ConfigMap.Diagnose(d)) Console.WriteLine(line);
            return 0;
        }
    }
}
