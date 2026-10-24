// TrayApp.cs - tray icon with the battery level, menu and low-battery notifications.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using CompxTray.Device;
using CompxTray.UI;

namespace CompxTray
{
    class TrayApp : ApplicationContext
    {
        readonly NotifyIcon tray = new NotifyIcon();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly Control invoker = new Control();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        ToolStripMenuItem statusItem, timeItem, warnItem, autostartItem;
        ToolStripMenuItem[] intervalItems;
        readonly int[] intervals = { 30, 60, 300 };
        SettingsWindow window;

        MouseStatus lastOk, lastStatus;
        string lastMessage = "";
        bool busy, warned20, warned10, notifiedFull;
        IntPtr currentHicon = IntPtr.Zero;

        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        public const string OpenEventName = "CompxTray_OpenWindow";
        readonly EventWaitHandle openEvent = new EventWaitHandle(false, EventResetMode.AutoReset, OpenEventName);
        volatile bool exiting;

        public TrayApp()
        {
            IntPtr force = invoker.Handle; // create a window handle for BeginInvoke

            BuildMenu();
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) Refresh(); };
            tray.DoubleClick += (s, e) => OpenWindow();
            tray.BalloonTipClicked += (s, e) => OpenWindow();
            SetIcon(MakeIcon("…", Theme.Faint));
            tray.Text = "CompxTray: reading battery…";
            tray.Visible = true;

            timer.Interval = Math.Max(10, AppSettings.IntervalSec) * 1000;
            timer.Tick += (s, e) => Refresh();
            timer.Start();

            AppSettings.Changed += OnSettingsChanged;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            AppSettings.Log("Started " + AppSettings.AppName + " " + AppSettings.Version);
            Refresh();

            var listener = new Thread(() =>
            {
                while (openEvent.WaitOne() && !exiting)
                    try { invoker.BeginInvoke(new Action(OpenWindow)); } catch { }
            });
            listener.IsBackground = true;
            listener.Start();
        }

        // ---------- Menu ----------
        void BuildMenu()
        {
            menu.Renderer = new DarkMenuRenderer();
            menu.ShowImageMargin = true;
            menu.Font = Theme.Body;

            statusItem = new ToolStripMenuItem("Mouse") { Enabled = false };
            timeItem = new ToolStripMenuItem("Not updated yet") { Enabled = false };
            var openItem = new ToolStripMenuItem("Open CompxTray", null, (s, e) => OpenWindow());
            openItem.Font = new Font(Theme.Body, FontStyle.Bold);
            var refreshItem = new ToolStripMenuItem("Refresh now", null, (s, e) => Refresh());

            var intervalMenu = new ToolStripMenuItem("Check battery every");
            intervalMenu.DropDown.BackColor = Theme.Surface;
            ((ToolStripDropDownMenu)intervalMenu.DropDown).Renderer = new DarkMenuRenderer();
            intervalItems = new ToolStripMenuItem[intervals.Length];
            for (int i = 0; i < intervals.Length; i++)
            {
                int sec = intervals[i];
                var item = new ToolStripMenuItem(sec < 60 ? sec + " seconds" : (sec / 60) + (sec == 60 ? " minute" : " minutes"));
                item.Click += (s, e) => AppSettings.IntervalSec = sec;
                intervalItems[i] = item;
                intervalMenu.DropDownItems.Add(item);
            }

            warnItem = new ToolStripMenuItem("Low battery warnings");
            warnItem.Click += (s, e) => AppSettings.LowBatteryWarnings = !AppSettings.LowBatteryWarnings;
            autostartItem = new ToolStripMenuItem("Start with Windows");
            autostartItem.Click += (s, e) => AppSettings.StartWithWindows = !AppSettings.StartWithWindows;
            var exitItem = new ToolStripMenuItem("Exit", null, (s, e) => ExitThread());

            menu.Items.AddRange(new ToolStripItem[] {
                statusItem, timeItem, new ToolStripSeparator(),
                openItem, refreshItem, intervalMenu, warnItem, autostartItem, new ToolStripSeparator(),
                exitItem });
            menu.Opening += (s, e) => SyncMenu();
            SyncMenu();
        }

        void SyncMenu()
        {
            int current = AppSettings.IntervalSec;
            for (int i = 0; i < intervals.Length; i++) intervalItems[i].Checked = intervals[i] == current;
            warnItem.Checked = AppSettings.LowBatteryWarnings;
            autostartItem.Checked = AppSettings.StartWithWindows;
        }

        void OnSettingsChanged()
        {
            timer.Interval = Math.Max(10, AppSettings.IntervalSec) * 1000;
            SyncMenu();
        }

        public void OpenWindow()
        {
            if (window == null || window.IsDisposed)
            {
                window = new SettingsWindow();
                window.FormClosed += (s, e) => { window = null; Refresh(); };
                if (lastStatus != null) window.ShowBattery(lastStatus, lastOk);
                window.Show();
            }
            else
            {
                if (window.WindowState == FormWindowState.Minimized) window.WindowState = FormWindowState.Normal;
                window.Activate();
            }
        }

        // ---------- Battery ----------
        void Refresh()
        {
            if (busy) return;
            busy = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                MouseStatus st;
                try { st = CompxHid.QueryBattery(1500); }
                catch (Exception ex) { st = new MouseStatus(); st.Message = "Error: " + ex.Message; }
                try { invoker.BeginInvoke(new Action(() => { busy = false; Apply(st); })); }
                catch { busy = false; }
            });
        }

        void Apply(MouseStatus st)
        {
            lastStatus = st;
            if (st.Message != lastMessage)
            {
                AppSettings.Log(st.Ok ? string.Format("Battery {0} % charging={1} {2} mV", st.Percent, st.Charging, st.MilliVolts) : st.Message);
                lastMessage = st.Message;
            }
            string name = st.DeviceName.Length > 0 ? st.DeviceName : lastOk != null ? lastOk.DeviceName : "Mouse";

            if (st.Ok)
            {
                lastOk = st;
                SetIcon(MakeIcon(st.Percent.ToString(), Theme.LevelColor(st.Percent, st.Charging)));
                string charge = st.Charging ? " (charging)" : "";
                SetTooltip(string.Format("{0}: {1} %{2}\n{3:0.00} V · {4:HH:mm}", name, st.Percent, charge, st.MilliVolts / 1000.0, st.Time));
                statusItem.Text = string.Format("{0}: {1} %{2}", name, st.Percent, charge);
                timeItem.Text = "Updated " + st.Time.ToString("HH:mm:ss");
                CheckWarnings(st, name);
            }
            else if (lastOk != null && st.ReceiverFound)
            {
                SetIcon(MakeIcon(lastOk.Percent.ToString(), Theme.Faint));
                SetTooltip(string.Format("{0}: {1} % (at {2:HH:mm})\nMouse is asleep", name, lastOk.Percent, lastOk.Time));
                statusItem.Text = string.Format("{0}: {1} % (at {2:HH:mm})", name, lastOk.Percent, lastOk.Time);
                timeItem.Text = "Mouse is asleep";
            }
            else
            {
                SetIcon(MakeIcon("?", Theme.Faint));
                SetTooltip("CompxTray\n" + st.Message);
                statusItem.Text = "Battery unknown";
                timeItem.Text = st.Message;
            }
            if (window != null && !window.IsDisposed) window.ShowBattery(st, lastOk);
        }

        void CheckWarnings(MouseStatus st, string name)
        {
            if (st.Charging || st.Percent > 25) { warned20 = false; warned10 = false; }
            if (!st.Charging) notifiedFull = false;
            if (AppSettings.LowBatteryWarnings && !st.Charging)
            {
                if (st.Percent <= 10 && !warned10)
                {
                    warned10 = true; warned20 = true;
                    tray.ShowBalloonTip(8000, name + ": " + st.Percent + " %", "The battery is almost empty. Time to charge.", ToolTipIcon.Warning);
                }
                else if (st.Percent <= 20 && !warned20)
                {
                    warned20 = true;
                    tray.ShowBalloonTip(8000, name + ": " + st.Percent + " %", "The battery is running low.", ToolTipIcon.Info);
                }
            }
            if (st.Charging && st.Percent >= 100 && !notifiedFull)
            {
                notifiedFull = true;
                tray.ShowBalloonTip(5000, name, "Fully charged.", ToolTipIcon.Info);
            }
        }

        void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.Resume) return;
            try
            {
                invoker.BeginInvoke(new Action(() =>
                {
                    var once = new System.Windows.Forms.Timer { Interval = 5000 };
                    once.Tick += (s, a) => { once.Stop(); once.Dispose(); Refresh(); };
                    once.Start();
                }));
            }
            catch { }
        }

        // ---------- Tray icon ----------
        static Icon MakeIcon(string text, Color bg)
        {
            Size sz = SystemInformation.SmallIconSize;
            int w = Math.Max(16, sz.Width), h = Math.Max(16, sz.Height);
            using (var bmp = new Bitmap(w, h))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);
                Theme.FillRound(g, bg, new RectangleF(0, 0, w - 1, h - 1), Math.Max(3, w / 5));

                Color fg = bg.GetBrightness() > 0.55f ? Theme.OnAccent : Color.White;
                float size = h * 0.80f;
                Font font = null;
                SizeF measured = SizeF.Empty;
                using (var fmt = StringFormat.GenericTypographic)
                {
                    while (size > 5)
                    {
                        if (font != null) font.Dispose();
                        font = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel);
                        measured = g.MeasureString(text, font, 1000, fmt);
                        if (measured.Width <= w - 1 && measured.Height <= h + 2) break;
                        size -= 0.5f;
                    }
                    using (var brush = new SolidBrush(fg))
                        g.DrawString(text, font, brush, (w - measured.Width) / 2f, (h - measured.Height) / 2f, fmt);
                    font.Dispose();
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        void SetIcon(Icon icon)
        {
            IntPtr old = currentHicon;
            tray.Icon = icon;
            currentHicon = icon.Handle;
            if (old != IntPtr.Zero) DestroyIcon(old);
        }

        void SetTooltip(string text) { tray.Text = text.Length > 63 ? text.Substring(0, 63) : text; }

        protected override void ExitThreadCore()
        {
            exiting = true;
            try { openEvent.Set(); } catch { }
            AppSettings.Changed -= OnSettingsChanged;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            timer.Stop();
            if (window != null && !window.IsDisposed) window.Close();
            tray.Visible = false;
            tray.Dispose();
            if (currentHicon != IntPtr.Zero) DestroyIcon(currentHicon);
            AppSettings.Log("Exited");
            base.ExitThreadCore();
        }
    }
}
