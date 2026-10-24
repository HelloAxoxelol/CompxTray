// Controls.cs - owner-drawn controls for the dark UI: cards, buttons, segmented selectors,
// toggles, sliders, a battery ring, tabs, a number box, a stepper and a mouse glyph.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CompxTray.UI
{
    public class UiControl : Control
    {
        protected bool Hover, Pressed;

        public UiControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                   | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Theme.Surface;
            ForeColor = Theme.Text;
            Font = Theme.Body;
        }

        protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Hover = false; Pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Pressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { Pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected Graphics Begin(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            Theme.Prepare(g);
            return g;
        }
    }

    // ---------- Card ----------
    public class Card : Panel
    {
        public string Title = "";

        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            Theme.Prepare(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.FillRound(g, Theme.Surface, r, Theme.Sf(10));
            Theme.StrokeRound(g, Theme.Border, r, Theme.Sf(10), 1f);
            if (Title.Length > 0)
                Theme.DrawText(g, Title.ToUpperInvariant(), Theme.Caption, Theme.Dim,
                    new Rectangle(Theme.S(20), Theme.S(16), Width - Theme.S(40), Theme.S(20)), TextFormatFlags.Left);
        }

        /// Adds a plain text label on the card.
        public Label AddText(string text, Font font, Color color, int x, int y, int width)
        {
            var l = new Label();
            l.Text = text; l.Font = font; l.ForeColor = color; l.BackColor = Theme.Surface;
            l.Location = new Point(x, y);
            l.MaximumSize = new Size(width, 0);
            l.AutoSize = true;
            Controls.Add(l);
            return l;
        }

        /// Thin divider line inside the card.
        public void AddDivider(int y)
        {
            var d = new Panel();
            d.BackColor = Theme.Border;
            d.Location = new Point(Theme.S(20), y);
            d.Size = new Size(Width - Theme.S(40), 1);
            Controls.Add(d);
        }
    }

    // ---------- Button ----------
    public enum ButtonKind { Primary, Secondary, Ghost }

    public class FlatButton : UiControl
    {
        public ButtonKind Kind = ButtonKind.Secondary;

        public FlatButton(string text, ButtonKind kind)
        {
            Text = text; Kind = kind; Font = Theme.Button;
            Cursor = Cursors.Hand;
            Size = new Size(Math.Max(Theme.S(96), Theme.Measure(text.ToUpperInvariant(), Theme.Button).Width + Theme.S(36)), Theme.S(34));
        }

        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            Color fill, text;
            if (Kind == ButtonKind.Primary)
            {
                fill = !Enabled ? Theme.Surface3 : Pressed ? Theme.Accent : Hover ? Theme.AccentHover : Theme.Accent;
                text = Enabled ? Theme.OnAccent : Theme.Faint;
            }
            else if (Kind == ButtonKind.Secondary)
            {
                fill = Pressed ? Theme.Surface2 : Hover && Enabled ? Theme.Surface3 : Theme.Surface2;
                text = Enabled ? Theme.Text : Theme.Faint;
            }
            else
            {
                fill = Hover && Enabled ? Theme.Surface2 : BackColor;
                text = Enabled ? Theme.Accent : Theme.Faint;
            }
            Theme.FillRound(g, fill, new RectangleF(0, 0, Width - 1, Height - 1), Theme.Sf(6));
            Theme.DrawText(g, Text.ToUpperInvariant(), Font, text, ClientRectangle,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    // ---------- Segmented selector ----------
    public class Segmented : UiControl
    {
        string[] items = new string[0];
        public int[] Values = new int[0];
        int selected = -1, hoverIndex = -1;
        public event EventHandler SelectionChanged;

        public Segmented() { Cursor = Cursors.Hand; Height = Theme.S(36); Font = Theme.Strong; }

        public void SetItems(string[] texts, int[] values) { items = texts; Values = values; Invalidate(); }

        public int SelectedIndex { get { return selected; } set { selected = value; Invalidate(); } }

        public int SelectedValue
        {
            get { return selected >= 0 && selected < Values.Length ? Values[selected] : -1; }
            set { selected = Array.IndexOf(Values, value); Invalidate(); }
        }

        int IndexAt(int x) { return items.Length == 0 ? -1 : Math.Min(items.Length - 1, Math.Max(0, x * items.Length / Math.Max(1, Width))); }

        protected override void OnMouseMove(MouseEventArgs e) { int i = IndexAt(e.X); if (i != hoverIndex) { hoverIndex = i; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hoverIndex = -1; base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (!Enabled) return;
            int i = IndexAt(e.X);
            if (i >= 0 && i != selected)
            {
                selected = i; Invalidate();
                if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
            }
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            Theme.FillRound(g, Theme.Surface2, new RectangleF(0, 0, Width - 1, Height - 1), Theme.Sf(8));
            if (items.Length == 0) return;
            float w = (Width - 1f) / items.Length;
            float pad = Theme.Sf(3);
            for (int i = 0; i < items.Length; i++)
            {
                var r = new RectangleF(i * w + pad, pad, w - pad * 2, Height - 1 - pad * 2);
                Color text = Enabled ? Theme.Dim : Theme.Faint;
                if (i == selected)
                {
                    Theme.FillRound(g, Enabled ? Theme.Accent : Theme.Surface3, r, Theme.Sf(6));
                    text = Enabled ? Theme.OnAccent : Theme.Dim;
                }
                else if (i == hoverIndex && Enabled)
                {
                    Theme.FillRound(g, Theme.Surface3, r, Theme.Sf(6));
                    text = Theme.Text;
                }
                Theme.DrawText(g, items[i], Font, text, Rectangle.Round(r),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
        }
    }

    // ---------- Toggle switch ----------
    public class Toggle : UiControl
    {
        bool isOn;
        float pos;
        readonly Timer anim = new Timer();
        public event EventHandler CheckedChanged;

        public Toggle()
        {
            Cursor = Cursors.Hand;
            Size = new Size(Theme.S(46), Theme.S(26));
            anim.Interval = 15;
            anim.Tick += (s, e) =>
            {
                float target = isOn ? 1f : 0f;
                pos += (target - pos) * 0.35f;
                if (Math.Abs(target - pos) < 0.02f) { pos = target; anim.Stop(); }
                Invalidate();
            };
        }

        public bool Checked
        {
            get { return isOn; }
            set { isOn = value; pos = value ? 1f : 0f; Invalidate(); }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (!Enabled) return;
            isOn = !isOn;
            anim.Start();
            if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            Color track = Theme.Mix(Theme.Surface3, Enabled ? Theme.Accent : Theme.Faint, pos);
            Theme.FillRound(g, track, r, r.Height / 2);
            float d = r.Height - Theme.Sf(6);
            float x = Theme.Sf(3) + (r.Width - d - Theme.Sf(6)) * pos;
            using (var b = new SolidBrush(Enabled ? (pos > 0.5f ? Theme.OnAccent : Theme.Text) : Theme.Dim))
                g.FillEllipse(b, x, Theme.Sf(3), d, d);
        }
    }

    // ---------- Slider ----------
    public class FlatSlider : UiControl
    {
        public int Minimum = 0, Maximum = 100, Step = 1;
        /// Values above 1 give the low end of the range more room (useful for DPI).
        public float Exponent = 1f;
        int[] stops;
        int value;
        bool dragging;
        public event EventHandler ValueChanged, ValueCommitted;

        public FlatSlider() { Cursor = Cursors.Hand; Height = Theme.S(28); TabStop = true; }

        /// Discrete mode: the slider snaps to these values.
        public int[] Stops { get { return stops; } set { stops = value; Invalidate(); } }

        public int Value
        {
            get { return value; }
            set { this.value = Clamp(value); Invalidate(); }
        }

        int Clamp(int v)
        {
            if (stops != null && stops.Length > 0)
            {
                int best = stops[0];
                foreach (var s in stops) if (Math.Abs(s - v) < Math.Abs(best - v)) best = s;
                return best;
            }
            v = Math.Max(Minimum, Math.Min(Maximum, v));
            return Minimum + (int)Math.Round((v - Minimum) / (double)Step) * Step;
        }

        float Radius { get { return Theme.Sf(8); } }
        float TrackLeft { get { return Radius + 1; } }
        float TrackRight { get { return Width - Radius - 2; } }

        float Fraction()
        {
            if (stops != null && stops.Length > 1) return Array.IndexOf(stops, value) / (float)(stops.Length - 1);
            if (Maximum == Minimum) return 0;
            float lin = (value - Minimum) / (float)(Maximum - Minimum);
            return Exponent == 1f ? lin : (float)Math.Pow(Math.Max(0, lin), 1.0 / Exponent);
        }

        int ValueAt(int x)
        {
            float t = Math.Max(0, Math.Min(1, (x - TrackLeft) / (TrackRight - TrackLeft)));
            if (stops != null && stops.Length > 1) return stops[(int)Math.Round(t * (stops.Length - 1))];
            double lin = Exponent == 1f ? t : Math.Pow(t, Exponent);
            return Clamp(Minimum + (int)Math.Round(lin * (Maximum - Minimum)));
        }

        void SetFromUser(int v, bool commit)
        {
            v = Clamp(v);
            if (v != value) { value = v; Invalidate(); if (ValueChanged != null) ValueChanged(this, EventArgs.Empty); }
            if (commit && ValueCommitted != null) ValueCommitted(this, EventArgs.Empty);
        }

        void Nudge(int dir)
        {
            if (stops != null && stops.Length > 0)
            {
                int i = Math.Max(0, Math.Min(stops.Length - 1, Array.IndexOf(stops, value) + dir));
                SetFromUser(stops[i], true);
            }
            else SetFromUser(value + dir * Step, true);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (Enabled && e.Button == MouseButtons.Left) { dragging = true; Focus(); SetFromUser(ValueAt(e.X), false); }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging) SetFromUser(ValueAt(e.X), false);
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (dragging) { dragging = false; SetFromUser(ValueAt(e.X), true); }
            base.OnMouseUp(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (Enabled && Focused) Nudge(e.Delta > 0 ? 1 : -1);
            base.OnMouseWheel(e);
        }

        protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || base.IsInputKey(k); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (Enabled && e.KeyCode == Keys.Left) Nudge(-1);
            if (Enabled && e.KeyCode == Keys.Right) Nudge(1);
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            float cy = Height / 2f, th = Theme.Sf(4);
            float x = TrackLeft + (TrackRight - TrackLeft) * Fraction();
            Theme.FillRound(g, Theme.Surface3, new RectangleF(TrackLeft, cy - th / 2, TrackRight - TrackLeft, th), th / 2);
            Color accent = Enabled ? Theme.Accent : Theme.Faint;
            Theme.FillRound(g, accent, new RectangleF(TrackLeft, cy - th / 2, Math.Max(th, x - TrackLeft), th), th / 2);

            if (stops != null && stops.Length > 1)
            {
                for (int i = 0; i < stops.Length; i++)
                {
                    float sx = TrackLeft + (TrackRight - TrackLeft) * i / (stops.Length - 1);
                    using (var b = new SolidBrush(sx <= x ? Theme.OnAccent : Theme.Faint))
                        g.FillEllipse(b, sx - Theme.Sf(1.5f), cy - Theme.Sf(1.5f), Theme.Sf(3), Theme.Sf(3));
                }
            }

            float r = (Hover || dragging) && Enabled ? Radius : Radius - Theme.Sf(1);
            if (Focused && Enabled)
                using (var halo = new SolidBrush(Color.FromArgb(50, Theme.Accent)))
                    g.FillEllipse(halo, x - r - Theme.Sf(4), cy - r - Theme.Sf(4), (r + Theme.Sf(4)) * 2, (r + Theme.Sf(4)) * 2);
            using (var b = new SolidBrush(Enabled ? Theme.Text : Theme.Dim)) g.FillEllipse(b, x - r, cy - r, r * 2, r * 2);
            using (var p = new Pen(accent, Theme.Sf(2.5f))) g.DrawEllipse(p, x - r, cy - r, r * 2, r * 2);
        }
    }

    // ---------- Battery ring ----------
    public class BatteryRing : UiControl
    {
        public int Percent = -1;
        public bool Charging, Stale;
        public float Thickness = 6;
        public bool ShowCaption = true;

        public void SetState(int percent, bool charging, bool stale)
        {
            Percent = percent; Charging = charging; Stale = stale; Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            float t = Theme.Sf(Thickness);
            float size = Math.Min(Width, Height) - t - 2;
            var r = new RectangleF((Width - size) / 2f, (Height - size) / 2f, size, size);
            using (var bg = new Pen(Theme.Surface3, t)) g.DrawEllipse(bg, r);
            Color c = Percent < 0 || Stale ? Theme.Dim : Theme.LevelColor(Percent, Charging);
            if (Percent > 0)
                using (var p = new Pen(c, t) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(p, r, -90, 360f * Percent / 100f);

            string num = Percent < 0 ? "–" : Percent.ToString();
            float fontPx = size * (Percent >= 100 ? 0.30f : 0.36f);
            using (var f = new Font("Segoe UI Semibold", fontPx, GraphicsUnit.Pixel))
            using (var small = new Font("Segoe UI", fontPx * 0.45f, GraphicsUnit.Pixel))
            {
                var numSize = TextRenderer.MeasureText(g, num, f, Size.Empty, TextFormatFlags.NoPadding);
                var pctSize = TextRenderer.MeasureText(g, "%", small, Size.Empty, TextFormatFlags.NoPadding);
                int total = numSize.Width + (Percent >= 0 ? pctSize.Width : 0);
                int x = (Width - total) / 2;
                int y = (Height - numSize.Height) / 2 - (ShowCaption ? (int)(size * 0.06f) : 0);
                TextRenderer.DrawText(g, num, f, new Point(x, y), Stale ? Theme.Dim : Theme.Text, TextFormatFlags.NoPadding);
                if (Percent >= 0)
                    TextRenderer.DrawText(g, "%", small, new Point(x + numSize.Width, y + numSize.Height - pctSize.Height - (int)(fontPx * 0.12f)), Theme.Dim, TextFormatFlags.NoPadding);
                if (ShowCaption)
                {
                    string cap = Percent < 0 ? "UNKNOWN" : Stale ? "ASLEEP" : Charging ? "CHARGING" : "BATTERY";
                    using (var cf = new Font("Segoe UI Semibold", Math.Max(7f, fontPx * 0.24f), GraphicsUnit.Pixel))
                    {
                        var cs = TextRenderer.MeasureText(g, cap, cf, Size.Empty, TextFormatFlags.NoPadding);
                        TextRenderer.DrawText(g, cap, cf, new Point((Width - cs.Width) / 2, y + numSize.Height + (int)(size * 0.02f)),
                            Charging && !Stale ? Theme.Accent : Theme.Dim, TextFormatFlags.NoPadding);
                    }
                }
            }
        }
    }

    // ---------- Tabs ----------
    public class TabStrip : UiControl
    {
        string[] tabs = new string[0];
        int selected, hoverIndex = -1;
        Rectangle[] hit = new Rectangle[0];
        public event EventHandler SelectedChanged;

        public TabStrip() { BackColor = Theme.Bg; Font = Theme.Tab; Cursor = Cursors.Hand; Height = Theme.S(46); }

        public void SetTabs(string[] t) { tabs = t; hit = new Rectangle[t.Length]; Invalidate(); }

        public int SelectedIndex
        {
            get { return selected; }
            set { selected = value; Invalidate(); if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty); }
        }

        int IndexAt(Point p) { for (int i = 0; i < hit.Length; i++) if (hit[i].Contains(p)) return i; return -1; }

        protected override void OnMouseMove(MouseEventArgs e) { int i = IndexAt(e.Location); if (i != hoverIndex) { hoverIndex = i; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hoverIndex = -1; base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e) { int i = IndexAt(e.Location); if (i >= 0 && i != selected) SelectedIndex = i; base.OnMouseClick(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            int x = Theme.S(32);
            for (int i = 0; i < tabs.Length; i++)
            {
                string t = tabs[i].ToUpperInvariant();
                var sz = Theme.Measure(t, Font);
                var r = new Rectangle(x, 0, sz.Width, Height - Theme.S(3));
                hit[i] = new Rectangle(x - Theme.S(10), 0, sz.Width + Theme.S(20), Height);
                Color c = i == selected ? Theme.Text : i == hoverIndex ? Theme.Text : Theme.Dim;
                Theme.DrawText(g, t, Font, c, r, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                if (i == selected)
                    Theme.FillRound(g, Theme.Accent, new RectangleF(x, Height - Theme.Sf(3), sz.Width, Theme.Sf(3)), Theme.Sf(1.5f));
                x += sz.Width + Theme.S(36);
            }
            using (var p = new Pen(Theme.Border)) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
        }
    }

    // ---------- Number box ----------
    public class NumberBox : UiControl
    {
        readonly TextBox box = new TextBox();
        int value;
        public int Minimum = 0, Maximum = 100, Step = 1;
        public event EventHandler ValueCommitted;

        public NumberBox()
        {
            BackColor = Theme.Surface;
            Size = new Size(Theme.S(84), Theme.S(32));
            box.BorderStyle = BorderStyle.None;
            box.BackColor = Theme.Surface2;
            box.ForeColor = Theme.Text;
            box.Font = Theme.Strong;
            box.TextAlign = HorizontalAlignment.Center;
            box.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { Commit(); e.SuppressKeyPress = true; }
                if (e.KeyCode == Keys.Up) { SetAndCommit(value + Step); e.SuppressKeyPress = true; }
                if (e.KeyCode == Keys.Down) { SetAndCommit(value - Step); e.SuppressKeyPress = true; }
            };
            box.Leave += (s, e) => Commit();
            box.GotFocus += (s, e) => Invalidate();
            box.LostFocus += (s, e) => Invalidate();
            Controls.Add(box);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            int h = box.PreferredHeight;
            box.SetBounds(Theme.S(6), (Height - h) / 2, Width - Theme.S(12), h);
            base.OnLayout(e);
        }

        protected override void OnEnabledChanged(EventArgs e) { box.Enabled = Enabled; box.ForeColor = Enabled ? Theme.Text : Theme.Dim; base.OnEnabledChanged(e); }

        public int Value
        {
            get { return value; }
            set { this.value = Clamp(value); box.Text = this.value.ToString(); }
        }

        int Clamp(int v)
        {
            v = Math.Max(Minimum, Math.Min(Maximum, v));
            return Minimum + (int)Math.Round((v - Minimum) / (double)Step) * Step;
        }

        void SetAndCommit(int v) { Value = v; if (ValueCommitted != null) ValueCommitted(this, EventArgs.Empty); }

        void Commit()
        {
            int v;
            if (!int.TryParse(box.Text.Replace(" ", "").Replace(" ", ""), out v)) v = value;
            SetAndCommit(v);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            Theme.FillRound(g, Theme.Surface2, r, Theme.Sf(6));
            if (box.Focused) Theme.StrokeRound(g, Theme.Accent, r, Theme.Sf(6), 1.2f);
        }
    }

    // ---------- Stepper  [-] n [+] ----------
    public class Stepper : UiControl
    {
        int value;
        public int Minimum = 1, Maximum = 6;
        public event EventHandler ValueChanged;
        int hoverSide;

        public Stepper() { Size = new Size(Theme.S(120), Theme.S(34)); Font = Theme.Strong; Cursor = Cursors.Hand; }

        public int Value { get { return value; } set { this.value = Math.Max(Minimum, Math.Min(Maximum, value)); Invalidate(); } }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int side = e.X < Height ? -1 : e.X > Width - Height ? 1 : 0;
            if (side != hoverSide) { hoverSide = side; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hoverSide = 0; base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (!Enabled) return;
            int side = e.X < Height ? -1 : e.X > Width - Height ? 1 : 0;
            int v = Math.Max(Minimum, Math.Min(Maximum, value + side));
            if (side != 0 && v != value) { value = v; Invalidate(); if (ValueChanged != null) ValueChanged(this, EventArgs.Empty); }
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            float h = Height - 1;
            Theme.FillRound(g, Theme.Surface2, new RectangleF(0, 0, Width - 1, h), Theme.Sf(8));
            for (int s = -1; s <= 1; s += 2)
            {
                var r = new RectangleF(s < 0 ? 0 : Width - 1 - h, 0, h, h);
                bool can = Enabled && (s < 0 ? value > Minimum : value < Maximum);
                if (hoverSide == s && can) Theme.FillRound(g, Theme.Surface3, r, Theme.Sf(8));
                using (var p = new Pen(can ? Theme.Text : Theme.Faint, Theme.Sf(2)))
                {
                    float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, a = Theme.Sf(5);
                    g.DrawLine(p, cx - a, cy, cx + a, cy);
                    if (s > 0) g.DrawLine(p, cx, cy - a, cx, cy + a);
                }
            }
            Theme.DrawText(g, value.ToString(), Font, Enabled ? Theme.Text : Theme.Dim, ClientRectangle,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    // ---------- Generic mouse glyph ----------
    public class MouseGlyph : UiControl
    {
        public MouseGlyph() { BackColor = Theme.Bg; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            float w = Width * 0.62f, h = Height * 0.9f;
            var body = new RectangleF((Width - w) / 2, (Height - h) / 2, w, h);
            using (var path = Theme.Round(body, w * 0.48f))
            {
                for (int i = 3; i >= 1; i--)
                    using (var glow = new Pen(Color.FromArgb(18, Theme.Accent), Theme.Sf(2 + i * 3))) g.DrawPath(glow, path);
                using (var fill = new LinearGradientBrush(body, Theme.Surface2, Theme.Bg, 90f)) g.FillPath(fill, path);
                using (var pen = new Pen(Theme.Accent, Theme.Sf(1.6f))) g.DrawPath(pen, path);
            }
            using (var pen = new Pen(Color.FromArgb(140, Theme.Accent), Theme.Sf(1.2f)))
            {
                float midX = body.X + body.Width / 2, split = body.Y + body.Height * 0.36f;
                g.DrawLine(pen, midX, body.Y + Theme.Sf(2), midX, split);
                g.DrawLine(pen, body.X + Theme.Sf(3), split, body.Right - Theme.Sf(3), split);
            }
            var wheel = new RectangleF(body.X + body.Width / 2 - Theme.Sf(2.5f), body.Y + body.Height * 0.12f, Theme.Sf(5), body.Height * 0.14f);
            Theme.FillRound(g, Theme.Accent, wheel, Theme.Sf(2.5f));
        }
    }
}
