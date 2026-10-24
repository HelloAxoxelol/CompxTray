// Program.cs - CompxTray for macOS and Linux: a menu bar / tray icon with the battery level and a read-only settings window.
using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls;

namespace CompxTray.App
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            using (var single = new Mutex(true, "CompxTray.Cross", out bool first))
            {
                if (!first) { Console.Error.WriteLine("CompxTray is already running."); return 0; }
                return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
            }
        }

        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<TrayApp>()
                .UsePlatformDetect()
                .WithInterFont()
                .With(new MacOSPlatformOptions { ShowInDock = false })
                .LogToTrace();
        }
    }
}
