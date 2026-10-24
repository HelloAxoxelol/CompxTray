// AppSettings.cs - app preferences (registry), data folder and log.
using System;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CompxTray
{
    public static class AppSettings
    {
        public const string AppName = "CompxTray";
        public const string Version = "1.0.0";
        /// Set this to your repository URL before publishing; the About card hides the link while it is empty.
        public const string RepoUrl = "https://github.com/HelloAxoxelol/CompxTray";

        const string SettingsKey = @"Software\CompxTray";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static event Action Changed;

        static void Raise() { var h = Changed; if (h != null) h(); }

        public static int IntervalSec
        {
            get { return ReadInt("IntervalSec", 60); }
            set { WriteInt("IntervalSec", value); Raise(); }
        }

        public static bool LowBatteryWarnings
        {
            get { return ReadInt("LowWarn", 1) == 1; }
            set { WriteInt("LowWarn", value ? 1 : 0); Raise(); }
        }

        public static bool StartWithWindows
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                        return k != null && string.Equals(k.GetValue(AppName) as string, ExeCommand(), StringComparison.OrdinalIgnoreCase);
                }
                catch { return false; }
            }
            set
            {
                try
                {
                    using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                    {
                        if (value) k.SetValue(AppName, ExeCommand());
                        else if (k.GetValue(AppName) != null) k.DeleteValue(AppName);
                    }
                }
                catch { }
                Raise();
            }
        }

        static string ExeCommand() { return "\"" + Application.ExecutablePath + "\""; }

        static int ReadInt(string name, int def)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(SettingsKey))
                    if (k != null) { object v = k.GetValue(name); if (v is int) return (int)v; }
            }
            catch { }
            return def;
        }

        static void WriteInt(string name, int value)
        {
            try { using (var k = Registry.CurrentUser.CreateSubKey(SettingsKey)) k.SetValue(name, value, RegistryValueKind.DWord); }
            catch { }
        }

        // ---------- Data folder ----------
        public static string DataDir
        {
            get
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);
                try { Directory.CreateDirectory(d); } catch { }
                return d;
            }
        }

        public static string BackupPath(ushort vid, ushort pid)
        {
            return Path.Combine(DataDir, string.Format("backup-{0:X4}-{1:X4}.txt", vid, pid));
        }

        /// Imports backup-*.txt files placed next to the exe (e.g. from a portable copy) into the data folder.
        public static void ImportBackupsNextToExe()
        {
            try
            {
                string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
                foreach (var f in Directory.GetFiles(exeDir, "backup-*.txt"))
                {
                    string target = Path.Combine(DataDir, Path.GetFileName(f));
                    if (!File.Exists(target)) File.Copy(f, target);
                }
            }
            catch { }
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
    }
}
