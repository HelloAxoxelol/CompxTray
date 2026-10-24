// StatusWindow.cs - shows the battery and the mouse's settings. Read-only on macOS and Linux for now.
using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using CompxTray.Device;

namespace CompxTray.App
{
    public class StatusWindow : Window
    {
        readonly TextBlock lblName = Text("Looking for the mouse…", 18, Palette.Text, FontWeight.SemiBold);
        readonly TextBlock lblModel = Text("", 12, Palette.Dim);
        readonly TextBlock lblPercent = Text("–", 44, Palette.Text, FontWeight.Bold);
        readonly TextBlock lblBattery = Text("", 12, Palette.Dim);
        readonly ProgressBar bar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 6, MinHeight = 6, Background = Palette.Brush(Palette.Surface2) };
        readonly StackPanel rows = new StackPanel { Spacing = 2 };
        readonly TextBlock lblStatus = Text("", 12, Palette.Dim);
        readonly Button btnRefresh = new Button { Content = "Read again" };
        bool reading;

        class ReadResult { public MouseConfig Config; public string ConfigError; public MouseStatus Battery; public string Firmware; public MouseModel Model; public string DeviceName; }

        public StatusWindow()
        {
            Title = "CompxTray";
            Width = 440; Height = 700; MinWidth = 380; MinHeight = 480;
            Background = Palette.Brush(Palette.Bg);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            try { Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://CompxTray/Assets/icon.png"))); } catch { }

            var header = new StackPanel { Spacing = 2, Children = { lblName, lblModel } };

            var batteryCard = Card(new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    Text("BATTERY", 11, Palette.Dim, FontWeight.SemiBold),
                    lblPercent,
                    bar,
                    lblBattery,
                }
            });

            var settingsCard = Card(new StackPanel
            {
                Spacing = 8,
                Children = { Text("SETTINGS", 11, Palette.Dim, FontWeight.SemiBold), rows }
            });

            var note = Text("Settings are shown read-only on macOS and Linux in this version. Change them with the Windows app for now.", 12, Palette.Faint);
            note.TextWrapping = TextWrapping.Wrap;

            var login = new CheckBox { Content = "Start at login", IsChecked = Platform.StartAtLogin, Foreground = Palette.Brush(Palette.Text) };
            login.IsCheckedChanged += (s, e) => Platform.StartAtLogin = login.IsChecked == true;
            var repo = new Button { Content = "Project page" };
            repo.Click += (s, e) => Platform.OpenUrl(Platform.RepoUrl);
            btnRefresh.Click += (s, e) => ReadAll();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { btnRefresh, repo } };

            var footer = new StackPanel { Spacing = 10, Children = { lblStatus, note, login, buttons, Text("CompxTray " + Platform.Version + " · not affiliated with Svive or Compx", 11, Palette.Faint) } };

            Content = new ScrollViewer
            {
                Content = new StackPanel { Margin = new Thickness(20), Spacing = 14, Children = { header, batteryCard, settingsCard, footer } }
            };
            Opened += (s, e) => ReadAll();
        }

        static TextBlock Text(string text, double size, Color color, FontWeight weight = FontWeight.Normal)
        {
            return new TextBlock { Text = text, FontSize = size, Foreground = Palette.Brush(color), FontWeight = weight };
        }

        static Border Card(Control child)
        {
            return new Border
            {
                Background = Palette.Brush(Palette.Surface), BorderBrush = Palette.Brush(Palette.Border), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10), Padding = new Thickness(16), Child = child
            };
        }

        void Row(string label, string value)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("140,*") };
            var l = Text(label, 13, Palette.Dim);
            var v = Text(value, 13, Palette.Text);
            Grid.SetColumn(v, 1);
            g.Children.Add(l); g.Children.Add(v);
            rows.Children.Add(g);
        }

        // ---------- Battery ----------
        public void ShowBattery(MouseStatus st, MouseStatus lastOk)
        {
            var show = st != null && st.Ok ? st : lastOk;
            if (show == null)
            {
                lblPercent.Text = "–";
                lblBattery.Text = st != null ? st.Message : "";
                bar.Value = 0;
                return;
            }
            bool stale = show != st;
            lblPercent.Text = show.Percent + " %";
            bar.Foreground = Palette.Brush(stale ? Palette.Faint : Palette.LevelColor(show.Percent, show.Charging));
            bar.Value = show.Percent;
            string state = stale ? "Asleep, last seen " + show.Time.ToString("HH:mm") : show.Charging ? "Charging" : "On battery";
            lblBattery.Text = string.Format("{0} · {1:0.00} V · updated {2:HH:mm:ss}", state, show.MilliVolts / 1000.0, show.Time);
        }

        // ---------- Read ----------
        void ReadAll()
        {
            if (reading) return;
            reading = true;
            btnRefresh.IsEnabled = false;
            lblStatus.Text = "Reading the mouse…";
            Task.Run(() =>
            {
                string openErr = null;
                ReadResult r = null;
                try
                {
                    r = CompxHid.WithDevice(d =>
                    {
                        var res = new ReadResult();
                        res.Model = MouseModel.Find(d.Vid, d.Pid, d.ProductName);
                        res.DeviceName = d.Name;
                        res.Battery = d.ReadBattery(800);
                        res.Firmware = d.ReadFirmware() ?? "?";
                        try { res.Config = MouseConfig.Read(d); }
                        catch (Exception ex) { res.ConfigError = ex.Message; }
                        return res;
                    }, out openErr);
                }
                catch (Exception ex) { openErr = ex.Message; }
                Dispatcher.UIThread.Post(() => ShowResult(r, openErr));
            });
        }

        void ShowResult(ReadResult r, string openErr)
        {
            reading = false;
            btnRefresh.IsEnabled = true;
            rows.Children.Clear();
            if (r == null)
            {
                lblName.Text = "No mouse found";
                lblModel.Text = "";
                lblStatus.Text = openErr ?? "No Compx receiver found.";
                lblStatus.Foreground = Palette.Brush(Palette.Warn);
                return;
            }
            lblName.Text = r.Model.DisplayName;
            lblModel.Text = r.DeviceName + " · firmware " + r.Firmware;
            if (r.Battery.Ok) ShowBattery(r.Battery, r.Battery);
            if (r.Config == null)
            {
                lblStatus.Text = r.ConfigError;
                lblStatus.Foreground = Palette.Brush(Palette.Warn);
                return;
            }
            var c = r.Config;
            Row("Polling rate", c.PollingHz + " Hz");
            Row("DPI stages", c.StageCount + ", active stage " + (c.ActiveStage + 1));
            for (int i = 0; i < c.StageCount; i++)
                Row("   Stage " + (i + 1), c.Dpi[i] + " DPI" + (i == c.ActiveStage ? "  ●" : ""));
            Row("Lift-off", c.LiftOffCode == 1 ? "Low (1)" : c.LiftOffCode == 2 ? "High (2)" : "code " + c.LiftOffCode);
            Row("Debounce", c.DebounceMs + " ms");
            Row("Motion sync", c.MotionSync ? "On" : "Off");
            Row("Sleep after", MouseConfig.FormatSleep(c.SleepSeconds));
            lblStatus.Text = r.Model.SettingsVerified ? "Read " + DateTime.Now.ToString("HH:mm:ss") + "."
                : "This mouse model has not been verified yet, so the values may be wrong.";
            lblStatus.Foreground = Palette.Brush(r.Model.SettingsVerified ? Palette.Dim : Palette.Warn);
        }
    }
}
