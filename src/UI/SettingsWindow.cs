// SettingsWindow.cs - the main window: performance, DPI, power and app settings.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using CompxTray.Device;

namespace CompxTray.UI
{
    public class SettingsWindow : Form
    {
        const int W = 900, H = 700, HeaderH = 112, FooterH = 64;

        readonly Panel root = new Panel();
        readonly Panel[] pages = new Panel[4];
        readonly TabStrip tabs = new TabStrip();

        // header
        readonly Label lblName = new Label(), lblSub = new Label(), lblBatteryHead = new Label();
        readonly BatteryRing ringHead = new BatteryRing();

        // performance
        readonly Segmented segPolling = new Segmented(), segLod = new Segmented();
        readonly Label lblMeasured = new Label();
        FlatButton btnMeasure;
        readonly Toggle tglMotion = new Toggle();
        readonly FlatSlider sldDebounce = new FlatSlider();
        readonly Label lblDebounce = new Label();

        // dpi
        readonly Stepper stpStages = new Stepper();
        readonly FlatSlider[] sldDpi = new FlatSlider[ConfigMap.MaxStages];
        readonly NumberBox[] numDpi = new NumberBox[ConfigMap.MaxStages];
        readonly ColorDot[] dots = new ColorDot[ConfigMap.MaxStages];
        readonly Label[] lblStage = new Label[ConfigMap.MaxStages];
        readonly Label[] lblActive = new Label[ConfigMap.MaxStages];

        // power
        readonly BatteryRing ringBig = new BatteryRing();
        readonly Label lblVolt = new Label(), lblBatTime = new Label(), lblSleep = new Label();
        readonly FlatSlider sldSleep = new FlatSlider();
        readonly Toggle tglWarn = new Toggle();
        readonly Segmented segInterval = new Segmented();

        // app
        readonly Toggle tglAutostart = new Toggle();
        readonly Label lblDeviceInfo = new Label();
        FlatButton btnRestore;

        // footer
        readonly Label lblStatus = new Label();
        FlatButton btnApply, btnDiscard, btnRefresh;

        readonly List<Control> mouseEditors = new List<Control>();

        MouseConfig current, edited;
        MouseModel model;
        string firmware = "?";
        bool busy, loading, demo;

        public event EventHandler Loaded;
        readonly System.Windows.Forms.Timer stageTimer = new System.Windows.Forms.Timer();
        bool stagePolling;

        public SettingsWindow()
        {
            Text = "CompxTray";
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Theme.S(W), Theme.S(H));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            root.Dock = DockStyle.Fill;
            root.BackColor = Theme.Bg;
            Controls.Add(root);

            BuildHeader();
            BuildFooter();
            BuildPerformance(pages[0] = NewPage());
            BuildDpi(pages[1] = NewPage());
            BuildPower(pages[2] = NewPage());
            BuildApp(pages[3] = NewPage());

            tabs.SetTabs(new[] { "Performance", "DPI", "Power", "App" });
            tabs.Location = new Point(0, Theme.S(HeaderH));
            tabs.Width = Theme.S(W);
            tabs.SelectedChanged += (s, e) => ShowTab(tabs.SelectedIndex);
            root.Controls.Add(tabs);
            ShowTab(0);

            SetMouseEditorsEnabled(false);
            stageTimer.Interval = 1500;
            stageTimer.Tick += (s, e) => PollActiveStage();
            stageTimer.Start();
            AppSettings.Changed += SyncAppSettings;
            SyncAppSettings();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitleBar(Handle);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (demo) FireLoaded(); else ReadFromMouse();
        }

        /// Demo mode: sample data for screenshots and trying the UI without a mouse. Nothing is written.
        public void LoadDemo()
        {
            demo = true;
            model = MouseModel.Find(0x373B, 0x11B3, "Flow 7");
            firmware = "4.04";
            current = MouseConfig.Demo();
            edited = current.Clone();
            lblName.Text = model.Name.ToUpperInvariant();
            lblSub.Text = model.Brand + "  ·  2.4 GHz receiver  ·  Firmware " + firmware;
            lblDeviceInfo.Text = "Device: demo data, no mouse connected";
            var st = new MouseStatus { Ok = true, Percent = 87, MilliVolts = 4050, DeviceName = model.Name, Time = DateTime.Now };
            ShowBattery(st, st);
            Populate();
            SetMouseEditorsEnabled(true);
            SetStatus("Demo mode: sample data. Nothing is written to a mouse.", Theme.Accent);
            UpdateFooter();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            stageTimer.Stop();
            AppSettings.Changed -= SyncAppSettings;
            base.OnFormClosed(e);
        }

        // ================= Layout helpers =================
        Panel NewPage()
        {
            var p = new Panel();
            p.BackColor = Theme.Bg;
            p.Location = new Point(0, Theme.S(HeaderH + 46));
            p.Size = new Size(Theme.S(W), Theme.S(H - HeaderH - 46 - FooterH));
            root.Controls.Add(p);
            return p;
        }

        static Card NewCard(Panel page, string title, int x, int y, int w, int h)
        {
            var c = new Card();
            c.Title = title;
            c.Location = new Point(Theme.S(x), Theme.S(y));
            c.Size = new Size(Theme.S(w), Theme.S(h));
            page.Controls.Add(c);
            return c;
        }

        static Label NewLabel(Control parent, string text, Font font, Color color, Color back, int x, int y, int w, int h, ContentAlignment align)
        {
            var l = new Label();
            l.Text = text; l.Font = font; l.ForeColor = color; l.BackColor = back;
            l.Location = new Point(Theme.S(x), Theme.S(y));
            l.Size = new Size(Theme.S(w), Theme.S(h));
            l.TextAlign = align;
            l.UseMnemonic = false;
            parent.Controls.Add(l);
            return l;
        }

        static void Place(Control parent, Control c, int x, int y, int w, int h)
        {
            c.Location = new Point(Theme.S(x), Theme.S(y));
            if (w > 0) c.Width = Theme.S(w);
            if (h > 0) c.Height = Theme.S(h);
            parent.Controls.Add(c);
        }

        /// A settings row: title + description on the left, control on the right.
        static void Row(Card card, int y, string title, string desc, int textWidth)
        {
            NewLabel(card, title, Theme.Strong, Theme.Text, Theme.Surface, 20, y, textWidth, 22, ContentAlignment.MiddleLeft);
            NewLabel(card, desc, Theme.Small, Theme.Dim, Theme.Surface, 20, y + 24, textWidth, 34, ContentAlignment.TopLeft);
        }

        public void ShowTab(int i)
        {
            for (int p = 0; p < pages.Length; p++) pages[p].Visible = p == i;
            if (tabs.SelectedIndex != i) tabs.SelectedIndex = i;
        }

        // ================= Header =================
        void BuildHeader()
        {
            var glyph = new MouseGlyph();
            Place(root, glyph, 30, 20, 48, 72);

            lblName.Text = "Connecting…";
            lblName.Font = Theme.Title; lblName.ForeColor = Theme.Text; lblName.BackColor = Theme.Bg;
            lblName.AutoSize = true; lblName.Location = new Point(Theme.S(90), Theme.S(24));
            root.Controls.Add(lblName);

            lblSub.Text = "Looking for a Compx receiver";
            lblSub.Font = Theme.Small; lblSub.ForeColor = Theme.Dim; lblSub.BackColor = Theme.Bg;
            lblSub.AutoSize = true; lblSub.Location = new Point(Theme.S(93), Theme.S(66));
            root.Controls.Add(lblSub);

            ringHead.BackColor = Theme.Bg; ringHead.Thickness = 5; ringHead.ShowCaption = false;
            Place(root, ringHead, W - 32 - 72, 20, 72, 72);

            lblBatteryHead.Font = Theme.Small; lblBatteryHead.ForeColor = Theme.Dim; lblBatteryHead.BackColor = Theme.Bg;
            lblBatteryHead.TextAlign = ContentAlignment.MiddleRight;
            Place(root, lblBatteryHead, W - 32 - 72 - 16 - 220, 30, 220, 52);
            lblBatteryHead.Text = "BATTERY";
        }

        // ================= Footer =================
        void BuildFooter()
        {
            var footer = new Panel();
            footer.BackColor = Theme.Surface;
            footer.Location = new Point(0, Theme.S(H - FooterH));
            footer.Size = new Size(Theme.S(W), Theme.S(FooterH));
            root.Controls.Add(footer);
            var line = new Panel { BackColor = Theme.Border, Location = new Point(0, 0), Size = new Size(Theme.S(W), 1) };
            footer.Controls.Add(line);

            lblStatus.Font = Theme.Small; lblStatus.ForeColor = Theme.Dim; lblStatus.BackColor = Theme.Surface;
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            lblStatus.Location = new Point(Theme.S(32), Theme.S(8));
            lblStatus.Size = new Size(Theme.S(500), Theme.S(FooterH - 16));
            footer.Controls.Add(lblStatus);

            btnApply = new FlatButton("Apply", ButtonKind.Primary);
            btnDiscard = new FlatButton("Discard", ButtonKind.Secondary);
            btnRefresh = new FlatButton("Read from mouse", ButtonKind.Ghost);
            foreach (var b in new[] { btnApply, btnDiscard, btnRefresh })
            {
                b.BackColor = Theme.Surface;
                footer.Controls.Add(b);
            }
            btnApply.Click += (s, e) => ApplyToMouse();
            btnDiscard.Click += (s, e) => { if (current != null) { edited = current.Clone(); Populate(); } };
            btnRefresh.Click += (s, e) => ReadFromMouse();
            UpdateFooter();
        }

        // ================= Performance =================
        void BuildPerformance(Panel page)
        {
            const int cw = W - 64;
            var poll = NewCard(page, "Polling rate", 32, 24, cw, 156);
            NewLabel(poll, "How often the mouse reports its position to the PC. Higher feels smoother, lower saves battery.",
                Theme.Small, Theme.Dim, Theme.Surface, 20, 44, 460, 36, ContentAlignment.TopLeft);
            Place(poll, segPolling, 20, 94, 460, 40);
            segPolling.SelectionChanged += (s, e) => { if (!loading && edited != null) { edited.PollingHz = segPolling.SelectedValue; UpdateFooter(); } };
            mouseEditors.Add(segPolling);

            NewLabel(poll, "MEASURED", Theme.Caption, Theme.Dim, Theme.Surface, 560, 44, 120, 18, ContentAlignment.MiddleLeft);
            lblMeasured.Text = "– Hz"; lblMeasured.Font = Theme.Medium; lblMeasured.ForeColor = Theme.Text; lblMeasured.BackColor = Theme.Surface;
            lblMeasured.TextAlign = ContentAlignment.MiddleLeft;
            Place(poll, lblMeasured, 556, 66, 140, 40);
            btnMeasure = new FlatButton("Measure", ButtonKind.Secondary);
            btnMeasure.Location = new Point(Theme.S(cw - 20) - btnMeasure.Width, Theme.S(97));
            poll.Controls.Add(btnMeasure);
            btnMeasure.Click += (s, e) => StartMeasure();

            var sensor = NewCard(page, "Sensor", 32, 196, cw, 270);

            Row(sensor, 48, "Lift-off distance", "Height above the surface where the sensor stops tracking.", 460);
            segLod.SetItems(new[] { "Low", "High" }, new[] { 1, 2 });
            Place(sensor, segLod, cw - 20 - 220, 52, 220, 36);
            segLod.SelectionChanged += (s, e) => { if (!loading && edited != null) { edited.LiftOffCode = segLod.SelectedValue; UpdateFooter(); } };
            mouseEditors.Add(segLod);
            sensor.AddDivider(Theme.S(116));

            Row(sensor, 130, "Motion sync", "Aligns sensor frames with USB polling. A little smoother, about 1 ms more latency.", 460);
            Place(sensor, tglMotion, cw - 20 - 46, 138, 0, 0);
            tglMotion.CheckedChanged += (s, e) => { if (!loading && edited != null) { edited.MotionSync = tglMotion.Checked; UpdateFooter(); } };
            mouseEditors.Add(tglMotion);
            sensor.AddDivider(Theme.S(194));

            Row(sensor, 208, "Button debounce", "Filters accidental double clicks from worn switches. Lower is faster.", 420);
            sldDebounce.Stops = ConfigMap.DebounceOptions;
            Place(sensor, sldDebounce, cw - 20 - 70 - 250, 214, 250, 28);
            NewLabelInto(sensor, lblDebounce, "", Theme.Strong, Theme.Text, cw - 20 - 64, 214, 64, 28, ContentAlignment.MiddleRight);
            sldDebounce.ValueChanged += (s, e) =>
            {
                lblDebounce.Text = sldDebounce.Value + " ms";
                if (!loading && edited != null) { edited.DebounceMs = sldDebounce.Value; UpdateFooter(); }
            };
            mouseEditors.Add(sldDebounce);
        }

        static void NewLabelInto(Control parent, Label l, string text, Font font, Color color, int x, int y, int w, int h, ContentAlignment align)
        {
            l.Text = text; l.Font = font; l.ForeColor = color; l.BackColor = Theme.Surface; l.TextAlign = align;
            l.Location = new Point(Theme.S(x), Theme.S(y)); l.Size = new Size(Theme.S(w), Theme.S(h));
            parent.Controls.Add(l);
        }

        // ================= DPI =================
        void BuildDpi(Panel page)
        {
            const int cw = W - 64;
            var card = NewCard(page, "DPI stages", 32, 24, cw, 434);
            NewLabel(card, "The DPI button on the mouse cycles through these stages. The marked stage is active right now.",
                Theme.Small, Theme.Dim, Theme.Surface, 20, 44, 520, 36, ContentAlignment.TopLeft);
            NewLabel(card, "STAGES", Theme.Caption, Theme.Dim, Theme.Surface, cw - 20 - 120 - 70, 46, 60, 26, ContentAlignment.MiddleRight);
            stpStages.Minimum = 1; stpStages.Maximum = ConfigMap.MaxStages;
            Place(card, stpStages, cw - 20 - 120, 42, 120, 34);
            stpStages.ValueChanged += (s, e) =>
            {
                if (!loading && edited != null) { edited.StageCount = stpStages.Value; UpdateFooter(); }
                UpdateStageRows();
            };
            mouseEditors.Add(stpStages);

            for (int i = 0; i < ConfigMap.MaxStages; i++)
            {
                int y = 96 + i * 54;
                int idx = i;
                dots[i] = new ColorDot();
                Place(card, dots[i], 20, y + 12, 14, 14);
                lblStage[i] = NewLabel(card, "Stage " + (i + 1), Theme.Strong, Theme.Text, Theme.Surface, 44, y + 6, 80, 26, ContentAlignment.MiddleLeft);
                lblActive[i] = NewLabel(card, "ACTIVE", Theme.Caption, Theme.Accent, Theme.AccentDark, 124, y + 9, 58, 20, ContentAlignment.MiddleCenter);
                lblActive[i].Visible = false;

                var sl = new FlatSlider();
                sl.Minimum = 100; sl.Maximum = 30000; sl.Step = ConfigMap.DpiStep; sl.Exponent = 2.5f;
                Place(card, sl, 200, y + 5, cw - 200 - 20 - 100 - 20, 28);
                sldDpi[i] = sl;
                var nb = new NumberBox();
                nb.Minimum = 100; nb.Maximum = 30000; nb.Step = ConfigMap.DpiStep;
                Place(card, nb, cw - 20 - 100, y + 3, 100, 32);
                numDpi[i] = nb;

                sl.ValueChanged += (s, e) => { numDpi[idx].Value = sldDpi[idx].Value; StageEdited(idx, sldDpi[idx].Value); };
                nb.ValueCommitted += (s, e) => { sldDpi[idx].Value = numDpi[idx].Value; StageEdited(idx, numDpi[idx].Value); };
                mouseEditors.Add(sl); mouseEditors.Add(nb);
            }
        }

        /// The mouse stores the active stage when the DPI button is pressed; follow it while the DPI tab is open.
        void PollActiveStage()
        {
            if (busy || stagePolling || demo || current == null || !pages[1].Visible || !Visible) return;
            stagePolling = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                int stage = -1;
                try
                {
                    string err;
                    stage = CompxHid.WithDevice(d => { var r = d.ReadEeprom(ConfigMap.AddrSystem, 6); return r != null && ConfigMap.PairOk(r, 4) ? r[4] : -1; }, out err);
                }
                catch { }
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        stagePolling = false;
                        if (stage < 0 || current == null || stage == current.ActiveStage) return;
                        current.ActiveStage = stage;
                        if (edited != null) edited.ActiveStage = stage;
                        loading = true;
                        stpStages.Minimum = Math.Min(ConfigMap.MaxStages, stage + 1);
                        loading = false;
                        UpdateStageRows();
                    }));
                }
                catch { stagePolling = false; }
            });
        }

        void StageEdited(int i, int dpi)
        {
            if (loading || edited == null) return;
            edited.Dpi[i] = dpi;
            UpdateFooter();
        }

        void UpdateStageRows()
        {
            int n = stpStages.Value;
            for (int i = 0; i < ConfigMap.MaxStages; i++)
            {
                bool on = i < n;
                dots[i].Visible = on; lblStage[i].Visible = on; sldDpi[i].Visible = on; numDpi[i].Visible = on;
                lblActive[i].Visible = on && current != null && current.ActiveStage == i;
            }
        }

        // ================= Power =================
        void BuildPower(Panel page)
        {
            var bat = NewCard(page, "Battery", 32, 24, 300, 434);
            ringBig.Thickness = 9;
            Place(bat, ringBig, 60, 70, 180, 180);
            NewLabelInto(bat, lblVolt, "– V", Theme.Medium, Theme.Text, 20, 276, 260, 36, ContentAlignment.MiddleCenter);
            NewLabelInto(bat, lblBatTime, "", Theme.Small, Theme.Dim, 20, 314, 260, 40, ContentAlignment.TopCenter);
            NewLabel(bat, "Level and voltage are read directly from the mouse.", Theme.Small, Theme.Faint, Theme.Surface,
                20, 380, 260, 36, ContentAlignment.TopCenter);

            var sleep = NewCard(page, "Sleep", 348, 24, 520, 196);
            NewLabel(sleep, "Time without movement before the mouse goes to sleep. Shorter saves battery, longer avoids wake-up lag.",
                Theme.Small, Theme.Dim, Theme.Surface, 20, 44, 480, 36, ContentAlignment.TopLeft);
            NewLabelInto(sleep, lblSleep, "–", Theme.Medium, Theme.Text, 20, 84, 200, 36, ContentAlignment.MiddleLeft);
            sldSleep.Stops = ConfigMap.SleepOptions;
            Place(sleep, sldSleep, 16, 128, 488, 28);
            NewLabel(sleep, "30 s", Theme.Small, Theme.Faint, Theme.Surface, 20, 160, 80, 18, ContentAlignment.MiddleLeft);
            NewLabel(sleep, "30 min", Theme.Small, Theme.Faint, Theme.Surface, 420, 160, 80, 18, ContentAlignment.MiddleRight);
            sldSleep.ValueChanged += (s, e) =>
            {
                lblSleep.Text = MouseConfig.FormatSleep(sldSleep.Value);
                if (!loading && edited != null) { edited.SleepSeconds = sldSleep.Value; UpdateFooter(); }
            };
            mouseEditors.Add(sldSleep);

            const int nw = 520;
            var notify = NewCard(page, "Tray", 348, 236, nw, 222);
            Row(notify, 48, "Low battery warning", "Show a notification at 20 % and 10 %.", 380);
            Place(notify, tglWarn, nw - 20 - 46, 56, 0, 0);
            tglWarn.CheckedChanged += (s, e) => AppSettings.LowBatteryWarnings = tglWarn.Checked;
            notify.AddDivider(Theme.S(116));
            Row(notify, 130, "Battery check interval", "How often the tray icon asks the mouse.", 250);
            segInterval.SetItems(new[] { "30 s", "1 min", "5 min" }, new[] { 30, 60, 300 });
            Place(notify, segInterval, nw - 20 - 210, 134, 210, 36);
            segInterval.SelectionChanged += (s, e) => AppSettings.IntervalSec = segInterval.SelectedValue;
        }

        // ================= App =================
        void BuildApp(Panel page)
        {
            const int gw = 418;
            var general = NewCard(page, "General", 32, 24, gw, 196);
            Row(general, 48, "Start with Windows", "Run CompxTray in the tray when you sign in.", 300);
            Place(general, tglAutostart, gw - 20 - 46, 56, 0, 0);
            tglAutostart.CheckedChanged += (s, e) => AppSettings.StartWithWindows = tglAutostart.Checked;
            general.AddDivider(Theme.S(116));
            var openData = new FlatButton("Open data folder", ButtonKind.Ghost);
            openData.Location = new Point(Theme.S(14), Theme.S(134));
            general.Controls.Add(openData);
            openData.Click += (s, e) => { try { Process.Start(AppSettings.DataDir); } catch { } };

            var backup = NewCard(page, "Backup", 466, 24, 402, 196);
            NewLabel(backup, "Your original settings were saved the first time CompxTray read this mouse. Restoring writes them back.",
                Theme.Small, Theme.Dim, Theme.Surface, 20, 44, 362, 54, ContentAlignment.TopLeft);
            btnRestore = new FlatButton("Restore original", ButtonKind.Secondary);
            btnRestore.Location = new Point(Theme.S(20), Theme.S(134));
            backup.Controls.Add(btnRestore);
            btnRestore.Click += (s, e) => RestoreOriginal();
            mouseEditors.Add(btnRestore);

            var about = NewCard(page, "About", 32, 236, W - 64, 222);
            NewLabel(about, AppSettings.AppName + " " + AppSettings.Version, Theme.Strong, Theme.Text, Theme.Surface, 20, 46, 400, 24, ContentAlignment.MiddleLeft);
            NewLabel(about, "Battery and settings for Compx-based wireless mice. Open source under the MIT license.",
                Theme.Small, Theme.Dim, Theme.Surface, 20, 72, 760, 20, ContentAlignment.MiddleLeft);
            NewLabelInto(about, lblDeviceInfo, "Device: not connected", Theme.Small, Theme.Text, 20, 104, 760, 20, ContentAlignment.MiddleLeft);
            NewLabel(about, "Not affiliated with Svive, Compx or any mouse maker. Product names are trademarks of their owners. "
                + "Changing settings writes to the mouse's memory. Use at your own risk.",
                Theme.Small, Theme.Faint, Theme.Surface, 20, 136, 760, 40, ContentAlignment.TopLeft);
            if (AppSettings.RepoUrl.Length > 0)
            {
                var link = new LinkLabel();
                link.Text = AppSettings.RepoUrl; link.Font = Theme.Small; link.BackColor = Theme.Surface;
                link.LinkColor = Theme.Accent; link.ActiveLinkColor = Theme.AccentHover; link.LinkBehavior = LinkBehavior.HoverUnderline;
                link.AutoSize = true; link.Location = new Point(Theme.S(20), Theme.S(182));
                link.LinkClicked += (s, e) => { try { Process.Start(AppSettings.RepoUrl); } catch { } };
                about.Controls.Add(link);
            }
        }

        void SyncAppSettings()
        {
            if (IsDisposed) return;
            tglWarn.Checked = AppSettings.LowBatteryWarnings;
            tglAutostart.Checked = AppSettings.StartWithWindows;
            segInterval.SelectedValue = AppSettings.IntervalSec;
        }

        // ================= State =================
        void SetMouseEditorsEnabled(bool on)
        {
            foreach (var c in mouseEditors) c.Enabled = on;
            btnRestore.Enabled = on && model != null && model.SettingsVerified && File.Exists(AppSettings.BackupPath(model.Vid, model.Pid));
        }

        void SetStatus(string text, Color color)
        {
            lblStatus.Text = text;
            lblStatus.ForeColor = color;
        }

        void UpdateFooter()
        {
            bool dirty = current != null && edited != null && !edited.SameSettings(current);
            btnApply.Enabled = dirty && !busy;
            bool[] show = { dirty || busy, dirty, !dirty };
            var buttons = new[] { btnApply, btnDiscard, btnRefresh };
            btnRefresh.Enabled = !busy && !demo;
            int x = Theme.S(W - 32);
            for (int i = 0; i < buttons.Length; i++)
            {
                var b = buttons[i];
                b.Visible = show[i];
                if (!show[i]) continue;
                x -= b.Width;
                b.Location = new Point(x, (Theme.S(FooterH) - b.Height) / 2);
                x -= Theme.S(10);
            }
            lblStatus.Width = Math.Max(Theme.S(200), x - Theme.S(48));
            if (dirty && !busy) SetStatus("You have unsaved changes.", Theme.Warn);
            if (btnRestore != null && demo) btnRestore.Enabled = false;
        }

        void Populate()
        {
            if (edited == null) return;
            loading = true;
            try
            {
                var rates = model.PollingRates;
                var texts = new string[rates.Length];
                for (int i = 0; i < rates.Length; i++) texts[i] = rates[i] + " Hz";
                segPolling.SetItems(texts, rates);
                segPolling.SelectedValue = edited.PollingHz;
                segLod.SelectedValue = edited.LiftOffCode;
                tglMotion.Checked = edited.MotionSync;
                sldDebounce.Value = edited.DebounceMs;
                lblDebounce.Text = edited.DebounceMs + " ms";

                stpStages.Minimum = Math.Min(ConfigMap.MaxStages, Math.Max(1, current.ActiveStage + 1));
                stpStages.Value = edited.StageCount;
                for (int i = 0; i < ConfigMap.MaxStages; i++)
                {
                    int dpi = edited.Dpi[i] > 0 ? edited.Dpi[i] : 800;
                    sldDpi[i].Minimum = model.MinDpi; sldDpi[i].Maximum = model.MaxDpi;
                    numDpi[i].Minimum = model.MinDpi; numDpi[i].Maximum = model.MaxDpi;
                    sldDpi[i].Value = dpi;
                    numDpi[i].Value = dpi;
                    dots[i].Color = edited.Colors[i] >= 0 ? Theme.Hex(edited.Colors[i]) : Theme.Dim;
                }
                UpdateStageRows();

                sldSleep.Value = edited.SleepSeconds;
                lblSleep.Text = MouseConfig.FormatSleep(edited.SleepSeconds);
            }
            finally { loading = false; }
            UpdateFooter();
        }

        public void ShowBattery(MouseStatus st, MouseStatus lastOk)
        {
            if (IsDisposed) return;
            MouseStatus show = st != null && st.Ok ? st : lastOk;
            bool stale = st == null || !st.Ok;
            if (show == null)
            {
                ringHead.SetState(-1, false, true); ringBig.SetState(-1, false, true);
                lblBatteryHead.Text = "BATTERY\n" + (st != null ? st.Message : "unknown");
                lblVolt.Text = "– V";
                lblBatTime.Text = st != null ? st.Message : "";
                return;
            }
            ringHead.SetState(show.Percent, show.Charging, stale);
            ringBig.SetState(show.Percent, show.Charging, stale);
            string state = stale ? "Asleep, last seen " + show.Time.ToString("HH:mm") : show.Charging ? "Charging" : "On battery";
            lblBatteryHead.Text = "BATTERY\n" + state;
            lblVolt.Text = (show.MilliVolts / 1000.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " V";
            lblBatTime.Text = state + "\nUpdated " + show.Time.ToString("HH:mm:ss");
        }

        // ================= Device work =================
        class ReadResult { public MouseConfig Config; public string ConfigError; public MouseStatus Battery; public string Firmware; public MouseModel Model; public string DeviceName; }

        void RunDevice<T>(string status, Func<CompxDevice, T> work, Action<T> done)
        {
            if (busy) return;
            busy = true;
            UseWaitCursor = true;
            SetStatus(status, Theme.Dim);
            UpdateFooter();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                T result = default(T);
                string error = null;
                try
                {
                    string openErr;
                    result = CompxHid.WithDevice(work, out openErr);
                    if (openErr != null) error = openErr;
                }
                catch (Exception ex) { error = ex.Message; }
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        busy = false;
                        UseWaitCursor = false;
                        if (error != null)
                        {
                            SetStatus(error, Theme.Bad);
                            AppSettings.Log("Error: " + error);
                            UpdateFooter();
                            FireLoaded();
                            return;
                        }
                        done(result);
                        UpdateFooter();
                    }));
                }
                catch { }
            });
        }

        void FireLoaded() { var h = Loaded; Loaded = null; if (h != null) h(this, EventArgs.Empty); }

        public void ReadFromMouse()
        {
            RunDevice("Reading settings from the mouse…", d =>
            {
                var r = new ReadResult();
                r.Model = MouseModel.Find(d.Vid, d.Pid, d.ProductName);
                r.DeviceName = d.Name;
                r.Battery = d.ReadBattery(800);
                r.Firmware = d.ReadFirmware() ?? "?";
                try { r.Config = MouseConfig.Read(d); }
                catch (Exception ex) { r.ConfigError = ex.Message; }
                return r;
            }, r =>
            {
                model = r.Model;
                firmware = r.Firmware;
                lblName.Text = model.Name.ToUpperInvariant();
                lblSub.Text = (model.Brand.Length > 0 ? model.Brand + "  ·  " : "") + "2.4 GHz receiver  ·  Firmware " + firmware;
                lblDeviceInfo.Text = string.Format("Device: {0}  ·  {1:X4}:{2:X4}  ·  firmware {3}  ·  {4}",
                    model.DisplayName, model.Vid, model.Pid, firmware, model.SettingsVerified ? "settings verified" : "settings not verified (read-only)");
                if (r.Battery.Ok) ShowBattery(r.Battery, r.Battery);
                if (r.Config == null)
                {
                    SetMouseEditorsEnabled(false);
                    SetStatus(r.ConfigError, Theme.Warn);
                    AppSettings.Log("Read failed: " + r.ConfigError);
                    FireLoaded();
                    return;
                }
                current = r.Config;
                edited = current.Clone();

                string backup = AppSettings.BackupPath(model.Vid, model.Pid);
                if (!File.Exists(backup))
                {
                    try { File.WriteAllText(backup, current.ToBackupText(r.DeviceName)); } catch { }
                }

                Populate();
                SetMouseEditorsEnabled(model.SettingsVerified);
                if (model.SettingsVerified)
                    SetStatus("Settings read from the mouse at " + DateTime.Now.ToString("HH:mm:ss") + ".", Theme.Dim);
                else
                    SetStatus("This mouse model has not been verified yet, so settings are read-only. Battery works.", Theme.Warn);
                AppSettings.Log(string.Format("Read {0}: {1} Hz, {2} stages (active {3}), DPI {4}, LOD {5}, debounce {6} ms, motion sync {7}, sleep {8} s",
                    r.DeviceName, current.PollingHz, current.StageCount, current.ActiveStage,
                    string.Join("/", Array.ConvertAll(current.Dpi, x => x.ToString()), 0, current.StageCount),
                    current.LiftOffCode, current.DebounceMs, current.MotionSync, current.SleepSeconds));
                FireLoaded();
            });
        }

        void ApplyToMouse()
        {
            if (current == null || edited == null || model == null || !model.SettingsVerified) return;
            if (demo) { current = edited.Clone(); Populate(); SetStatus("Demo mode: changes are not written anywhere.", Theme.Accent); return; }
            if (edited.StageCount <= current.ActiveStage)
            {
                SetStatus("Switch to a lower DPI stage on the mouse before removing the active stage.", Theme.Warn);
                return;
            }
            var old = current;
            var neu = edited.Clone();
            RunDevice("Writing to the mouse…", d =>
            {
                var log = MouseConfig.Apply(d, old, neu);
                var fresh = MouseConfig.Read(d);
                return new KeyValuePair<List<string>, MouseConfig>(log, fresh);
            }, r => AfterWrite(r.Key, r.Value));
        }

        void RestoreOriginal()
        {
            if (model == null) return;
            string path = AppSettings.BackupPath(model.Vid, model.Pid);
            if (!File.Exists(path)) return;
            var answer = MessageBox.Show(this, "Write back the settings the mouse had when CompxTray first read it?",
                "Restore original settings", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;
            string text = File.ReadAllText(path);
            RunDevice("Restoring original settings…", d =>
            {
                var log = MouseConfig.Restore(d, text);
                var fresh = MouseConfig.Read(d);
                return new KeyValuePair<List<string>, MouseConfig>(log, fresh);
            }, r => AfterWrite(r.Key, r.Value));
        }

        void AfterWrite(List<string> log, MouseConfig fresh)
        {
            current = fresh;
            edited = current.Clone();
            Populate();
            int failed = 0;
            foreach (var line in log) { AppSettings.Log("Write: " + line); if (line.StartsWith("FAILED")) failed++; }
            if (log.Count == 0) SetStatus("Nothing to change.", Theme.Dim);
            else if (failed == 0) SetStatus("Saved and verified: " + log.Count + (log.Count == 1 ? " change." : " changes."), Theme.Good);
            else SetStatus(failed + " of " + log.Count + " changes failed. " + log.Find(l => l.StartsWith("FAILED")).Substring(8), Theme.Bad);
        }

        // ================= Polling measurement (Raw Input) =================
        [StructLayout(LayoutKind.Sequential)] struct RAWINPUTDEVICE { public ushort UsagePage; public ushort Usage; public uint Flags; public IntPtr Target; }
        [StructLayout(LayoutKind.Sequential)] struct RAWINPUTHEADER { public uint Type; public uint Size; public IntPtr Device; public IntPtr WParam; }
        [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devs, uint num, uint size);
        [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr raw, uint cmd, out RAWINPUTHEADER data, ref uint size, uint headerSize);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint GetRawInputDeviceInfo(IntPtr dev, uint cmd, StringBuilder data, ref uint size);

        const int WM_INPUT = 0x00FF;
        const uint RID_HEADER = 0x10000005, RIDI_DEVICENAME = 0x20000007, RIDEV_INPUTSINK = 0x100, RIDEV_REMOVE = 0x1;
        const int MeasureMs = 3000, BucketMs = 200;

        bool measuring;
        readonly Stopwatch watch = new Stopwatch();
        readonly Dictionary<IntPtr, bool> isCompx = new Dictionary<IntPtr, bool>();
        int[] buckets;
        System.Windows.Forms.Timer measureTimer;

        void StartMeasure()
        {
            if (measuring) return;
            var dev = new RAWINPUTDEVICE[1];
            dev[0].UsagePage = 1; dev[0].Usage = 2; dev[0].Flags = RIDEV_INPUTSINK; dev[0].Target = Handle;
            if (!RegisterRawInputDevices(dev, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)))) { SetStatus("Could not start the measurement.", Theme.Bad); return; }
            buckets = new int[MeasureMs / BucketMs + 1];
            measuring = true;
            btnMeasure.Enabled = false;
            btnMeasure.Text = "Move the mouse";
            lblMeasured.Text = "…";
            SetStatus("Move the mouse quickly in circles for 3 seconds.", Theme.Accent);
            watch.Restart();
            measureTimer = new System.Windows.Forms.Timer { Interval = MeasureMs };
            measureTimer.Tick += (s, e) => StopMeasure();
            measureTimer.Start();
        }

        void StopMeasure()
        {
            measureTimer.Stop(); measureTimer.Dispose();
            measuring = false;
            var dev = new RAWINPUTDEVICE[1];
            dev[0].UsagePage = 1; dev[0].Usage = 2; dev[0].Flags = RIDEV_REMOVE; dev[0].Target = IntPtr.Zero;
            RegisterRawInputDevices(dev, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
            btnMeasure.Enabled = true;
            btnMeasure.Text = "Measure";
            int best = 0;
            foreach (var b in buckets) best = Math.Max(best, b);
            int hz = best * 1000 / BucketMs;
            lblMeasured.Text = hz == 0 ? "– Hz" : "≈ " + hz + " Hz";
            if (hz == 0) SetStatus("No movement from the mouse was picked up. Try again and keep moving.", Theme.Warn);
            else SetStatus("Measured about " + hz + " Hz (peak during the test).", Theme.Dim);
            AppSettings.Log("Measured: about " + hz + " Hz");
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_INPUT && measuring)
            {
                RAWINPUTHEADER h;
                uint size = (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER));
                if (GetRawInputData(m.LParam, RID_HEADER, out h, ref size, size) != uint.MaxValue && IsCompx(h.Device))
                {
                    int idx = (int)(watch.ElapsedMilliseconds / BucketMs);
                    if (idx < buckets.Length) buckets[idx]++;
                }
            }
            base.WndProc(ref m);
        }

        bool IsCompx(IntPtr device)
        {
            bool known;
            if (isCompx.TryGetValue(device, out known)) return known;
            var sb = new StringBuilder(512);
            uint size = 512;
            GetRawInputDeviceInfo(device, RIDI_DEVICENAME, sb, ref size);
            string name = sb.ToString().ToLowerInvariant();
            known = name.Contains("vid_373b") || name.Contains("vid_3554");
            isCompx[device] = known;
            return known;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (measuring) StopMeasure();
            base.OnFormClosing(e);
        }

        // ================= Screenshots (for docs) =================
        public void SaveSnapshot(string path)
        {
            using (var bmp = new Bitmap(root.Width, root.Height))
            {
                root.DrawToBitmap(bmp, new Rectangle(0, 0, root.Width, root.Height));
                bmp.Save(path, ImageFormat.Png);
            }
        }
    }

    /// Small coloured circle used for DPI stage colours.
    public class ColorDot : UiControl
    {
        Color color = Theme.Dim;
        public Color Color { get { return color; } set { color = value; Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            float d = Math.Min(Width, Height) - 2;
            using (var halo = new SolidBrush(Color.FromArgb(60, color))) g.FillEllipse(halo, 0, 0, d + 1, d + 1);
            using (var b = new SolidBrush(color)) g.FillEllipse(b, Theme.Sf(2), Theme.Sf(2), d - Theme.Sf(3), d - Theme.Sf(3));
        }
    }
}
