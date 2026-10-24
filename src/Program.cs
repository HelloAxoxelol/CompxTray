// Program.cs - entry point.
//   CompxTray.exe                     run in the tray (single instance)
//   CompxTray.exe --open              run in the tray and open the window
//   CompxTray.exe --demo              open the window with sample data (nothing is written)
//   CompxTray.exe --screenshot <dir>  open the window, read the mouse, save one PNG per tab and exit
//                                     (add --demo to use sample data)
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using CompxTray.UI;

namespace CompxTray
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch { }
            Theme.InitScale();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            AppSettings.ImportBackupsNextToExe();

            bool demo = Array.IndexOf(args, "--demo") >= 0;
            if (args.Length >= 2 && args[0] == "--screenshot")
            {
                Screenshots(args[1], demo);
                return;
            }
            if (demo)
            {
                var w = new SettingsWindow();
                w.LoadDemo();
                Application.Run(w);
                return;
            }

            bool created;
            using (var mutex = new Mutex(true, "CompxTray_SingleInstance", out created))
            {
                if (!created)
                {
                    // Already running: ask the running instance to open its window.
                    try { EventWaitHandle.OpenExisting(TrayApp.OpenEventName).Set(); } catch { }
                    return;
                }
                var app = new TrayApp();
                if (Array.IndexOf(args, "--open") >= 0) app.OpenWindow();
                Application.Run(app);
            }
        }

        static void Screenshots(string dir, bool demo)
        {
            Directory.CreateDirectory(dir);
            var w = new SettingsWindow();
            if (demo) w.LoadDemo();
            string[] names = { "performance", "dpi", "power", "app" };
            w.Loaded += (s, e) =>
            {
                var t = new System.Windows.Forms.Timer { Interval = 400 };
                t.Tick += (s2, e2) =>
                {
                    t.Stop();
                    for (int i = 0; i < names.Length; i++)
                    {
                        w.ShowTab(i);
                        Application.DoEvents();
                        w.SaveSnapshot(Path.Combine(dir, names[i] + ".png"));
                    }
                    w.Close();
                };
                t.Start();
            };
            Application.Run(w);
        }
    }
}
