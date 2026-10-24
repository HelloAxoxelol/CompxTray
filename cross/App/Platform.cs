// Platform.cs - data folder, log, start at login and desktop notifications on macOS and Linux.
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CompxTray.App
{
    static class Platform
    {
        public const string AppName = "CompxTray";
        public static readonly string Version = (typeof(Platform).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?").Split('+')[0];
        public const string RepoUrl = "https://github.com/HelloAxoxelol/CompxTray";
        const string LaunchAgentId = "io.github.helloaxoxelol.compxtray";

        public static bool IsMac { get { return RuntimeInformation.IsOSPlatform(OSPlatform.OSX); } }
        public static bool IsLinux { get { return RuntimeInformation.IsOSPlatform(OSPlatform.Linux); } }

        /// ~/Library/Application Support/CompxTray on macOS, ~/.config/CompxTray on Linux.
        /// On Windows, where this build only runs for testing, a subfolder keeps it apart from the Windows app's log.
        public static string DataDir
        {
            get
            {
                var root = Environment.GetFolderPath(IsMac ? Environment.SpecialFolder.LocalApplicationData : Environment.SpecialFolder.ApplicationData);
                string d = Path.Combine(root, AppName);
                if (!IsMac && !IsLinux) d = Path.Combine(d, "cross");
                try { Directory.CreateDirectory(d); } catch { }
                return d;
            }
        }

        public static void Log(string msg)
        {
            try
            {
                string path = Path.Combine(DataDir, "log.txt");
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > 256 * 1024) fi.Delete();
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine);
            }
            catch { }
        }

        // ---------- Start at login ----------
        static string AutostartFile
        {
            get
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (IsMac) return Path.Combine(home, "Library", "LaunchAgents", LaunchAgentId + ".plist");
                string config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                if (string.IsNullOrEmpty(config)) config = Path.Combine(home, ".config");
                return Path.Combine(config, "autostart", "compxtray.desktop");
            }
        }

        public static bool StartAtLogin
        {
            get { return File.Exists(AutostartFile); }
            set
            {
                try
                {
                    if (!value) { if (File.Exists(AutostartFile)) File.Delete(AutostartFile); return; }
                    Directory.CreateDirectory(Path.GetDirectoryName(AutostartFile));
                    string exe = Environment.ProcessPath;
                    File.WriteAllText(AutostartFile, IsMac ? LaunchAgent(exe) : DesktopEntry(exe));
                }
                catch (Exception ex) { Log("Start at login: " + ex.Message); }
            }
        }

        static string LaunchAgent(string exe)
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
                + "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n"
                + "<plist version=\"1.0\"><dict>\n"
                + "  <key>Label</key><string>" + LaunchAgentId + "</string>\n"
                + "  <key>ProgramArguments</key><array><string>" + Xml(exe) + "</string></array>\n"
                + "  <key>RunAtLoad</key><true/>\n"
                + "</dict></plist>\n";
        }

        static string DesktopEntry(string exe)
        {
            return "[Desktop Entry]\nType=Application\nName=CompxTray\nComment=Mouse battery level in the tray\n"
                + "Exec=\"" + exe.Replace("\"", "\\\"") + "\"\nTerminal=false\nX-GNOME-Autostart-enabled=true\n";
        }

        static string Xml(string s) { return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;"); }

        // ---------- Notifications ----------
        public static void Notify(string title, string text)
        {
            try
            {
                var psi = new ProcessStartInfo { UseShellExecute = false, CreateNoWindow = true };
                if (IsMac)
                {
                    psi.FileName = "osascript";
                    psi.ArgumentList.Add("-e");
                    psi.ArgumentList.Add("display notification " + AppleString(text) + " with title " + AppleString(title));
                }
                else if (IsLinux)
                {
                    psi.FileName = "notify-send";
                    psi.ArgumentList.Add("--app-name=CompxTray");
                    psi.ArgumentList.Add(title);
                    psi.ArgumentList.Add(text);
                }
                else return;
                using (Process.Start(psi)) { }
            }
            catch (Exception ex) { Log("Notification failed: " + ex.Message); }
        }

        static string AppleString(string s) { return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""; }

        public static void OpenUrl(string url)
        {
            try
            {
                var psi = new ProcessStartInfo { UseShellExecute = false, CreateNoWindow = true, FileName = IsMac ? "open" : IsLinux ? "xdg-open" : url };
                if (IsMac || IsLinux) psi.ArgumentList.Add(url); else psi.UseShellExecute = true;
                using (Process.Start(psi)) { }
            }
            catch (Exception ex) { Log("Could not open " + url + ": " + ex.Message); }
        }
    }
}
