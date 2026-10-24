// TrayApp.cs - menu bar / tray icon with the battery level, menu and low-battery notifications.
using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using CompxTray.Device;

namespace CompxTray.App
{
    public class TrayApp : Application
    {
        TrayIcon tray;
        NativeMenuItem statusItem, timeItem, loginItem;
        readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        StatusWindow window;

        MouseStatus lastOk, lastLogged;
        string lastMessage = "";
        // Voltage readings jitter by a few mV, so smaller changes are not logged.
        const int LogMilliVoltStep = 10;
        bool busy, warned20, warned10, notifiedFull;

        public override void Initialize()
        {
            Name = Platform.AppName;
            Styles.Add(new FluentTheme());
            RequestedThemeVariant = ThemeVariant.Dark;
        }

        public override void OnFrameworkInitializationCompleted()
        {
            BuildTray();
            timer.Tick += (s, e) => Refresh();
            timer.Start();
            Platform.Log("Started " + Platform.AppName + " " + Platform.Version + " (" + System.Runtime.InteropServices.RuntimeInformation.OSDescription + ")");
            Refresh();
            var desktop = ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            if (desktop != null && desktop.Args != null && Array.IndexOf(desktop.Args, "--open") >= 0) OpenWindow();
            base.OnFrameworkInitializationCompleted();
        }

        void BuildTray()
        {
            var menu = new NativeMenu();
            statusItem = new NativeMenuItem("Reading battery…") { IsEnabled = false };
            timeItem = new NativeMenuItem("") { IsEnabled = false };
            var open = new NativeMenuItem("Open CompxTray…");
            open.Click += (s, e) => OpenWindow();
            var now = new NativeMenuItem("Check now");
            now.Click += (s, e) => Refresh();
            loginItem = new NativeMenuItem("Start at login") { ToggleType = MenuItemToggleType.CheckBox, IsChecked = Platform.StartAtLogin };
            loginItem.Click += (s, e) => { Platform.StartAtLogin = !Platform.StartAtLogin; loginItem.IsChecked = Platform.StartAtLogin; };
            var quit = new NativeMenuItem("Quit");
            quit.Click += (s, e) => { tray.IsVisible = false; ((Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)ApplicationLifetime).Shutdown(); };
            menu.Add(statusItem);
            menu.Add(timeItem);
            menu.Add(new NativeMenuItemSeparator());
            menu.Add(open);
            menu.Add(now);
            menu.Add(loginItem);
            menu.Add(new NativeMenuItemSeparator());
            menu.Add(quit);

            tray = new TrayIcon { Icon = MakeIcon("…", Palette.Faint), ToolTipText = "CompxTray: reading battery…", Menu = menu, IsVisible = true };
            tray.Clicked += (s, e) => OpenWindow();
            TrayIcon.SetIcons(this, new TrayIcons { tray });
        }

        void OpenWindow()
        {
            if (window == null)
            {
                window = new StatusWindow();
                window.Closed += (s, e) => window = null;
                window.Show();
                if (lastOk != null) window.ShowBattery(lastOk, lastOk);
            }
            else
            {
                if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                window.Activate();
            }
        }

        // ---------- Battery ----------
        public void Refresh()
        {
            if (busy) return;
            busy = true;
            Task.Run(() =>
            {
                MouseStatus st;
                try { st = CompxHid.QueryBattery(1500); }
                catch (Exception ex) { st = new MouseStatus(); st.Message = "Error: " + ex.Message; }
                Dispatcher.UIThread.Post(() => { busy = false; Apply(st); });
            });
        }

        void Apply(MouseStatus st)
        {
            bool batteryChanged = st.Ok && (lastLogged == null || st.Percent != lastLogged.Percent || st.Charging != lastLogged.Charging
                                            || Math.Abs(st.MilliVolts - lastLogged.MilliVolts) >= LogMilliVoltStep);
            if (st.Message != lastMessage || batteryChanged)
            {
                Platform.Log(st.Ok ? string.Format("Battery {0} % charging={1} {2} mV", st.Percent, st.Charging, st.MilliVolts) : st.Message);
                lastMessage = st.Message;
                if (st.Ok) lastLogged = st;
            }
            string name = st.DeviceName.Length > 0 ? st.DeviceName : lastOk != null ? lastOk.DeviceName : "Mouse";

            if (st.Ok)
            {
                lastOk = st;
                tray.Icon = MakeIcon(st.Percent.ToString(), Palette.LevelColor(st.Percent, st.Charging));
                string charge = st.Charging ? " (charging)" : "";
                tray.ToolTipText = string.Format("{0}: {1} %{2}\n{3:0.00} V · {4:HH:mm}", name, st.Percent, charge, st.MilliVolts / 1000.0, st.Time);
                statusItem.Header = string.Format("{0}: {1} %{2}", name, st.Percent, charge);
                timeItem.Header = "Updated " + st.Time.ToString("HH:mm:ss");
                CheckWarnings(st, name);
            }
            else if (lastOk != null && st.ReceiverFound)
            {
                tray.Icon = MakeIcon(lastOk.Percent.ToString(), Palette.Faint);
                tray.ToolTipText = string.Format("{0}: {1} % (at {2:HH:mm})\nMouse is asleep", name, lastOk.Percent, lastOk.Time);
                statusItem.Header = string.Format("{0}: {1} % (at {2:HH:mm})", name, lastOk.Percent, lastOk.Time);
                timeItem.Header = "Mouse is asleep";
            }
            else
            {
                tray.Icon = MakeIcon("?", Palette.Faint);
                tray.ToolTipText = "CompxTray\n" + st.Message;
                statusItem.Header = "Battery unknown";
                timeItem.Header = st.Message;
            }
            if (window != null) window.ShowBattery(st, lastOk);
        }

        void CheckWarnings(MouseStatus st, string name)
        {
            if (st.Charging || st.Percent > 25) { warned20 = false; warned10 = false; }
            if (!st.Charging) notifiedFull = false;
            if (!st.Charging)
            {
                if (st.Percent <= 10 && !warned10)
                {
                    warned10 = true; warned20 = true;
                    Platform.Notify(name + ": " + st.Percent + " %", "The battery is almost empty. Time to charge.");
                }
                else if (st.Percent <= 20 && !warned20)
                {
                    warned20 = true;
                    Platform.Notify(name + ": " + st.Percent + " %", "The battery is running low.");
                }
            }
            if (st.Charging && st.Percent >= 100 && !notifiedFull)
            {
                notifiedFull = true;
                Platform.Notify(name, "Fully charged.");
            }
        }

        // ---------- Tray icon ----------
        static WindowIcon MakeIcon(string text, Color bg)
        {
            const int size = 64;
            var bmp = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
            using (var ctx = bmp.CreateDrawingContext())
            {
                ctx.DrawRectangle(new SolidColorBrush(bg), null, new RoundedRect(new Rect(0, 0, size, size), 13));
                var fg = Palette.Brightness(bg) > 0.55 ? Palette.OnAccent : Colors.White;
                var typeface = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);
                double fontSize = size * 0.78;
                FormattedText ft;
                while (true)
                {
                    ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, fontSize, new SolidColorBrush(fg));
                    if ((ft.Width <= size - 4 && ft.Height <= size + 6) || fontSize <= 12) break;
                    fontSize -= 2;
                }
                ctx.DrawText(ft, new Point((size - ft.Width) / 2, (size - ft.Height) / 2));
            }
            var ms = new MemoryStream();
            bmp.Save(ms, PngBitmapEncoderOptions.Default);
            ms.Position = 0;
            return new WindowIcon(ms);
        }
    }
}
