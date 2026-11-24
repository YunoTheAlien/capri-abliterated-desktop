using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CapriDesktop
{
    // What her ears hear from the mic, for the meter: the level now, the room's level, the level of their last line and the
    // line where talk starts. Levels are 0 to 1, as her ears measure them; a level never measured is negative.
    internal struct MicReading
    {
        internal double Now, Room, Voice, Gate;
        internal bool Auto, Live;
    }

    // Mic sensitivity like a call app's: a live bar of what her mic hears, a line to drag for where talk starts, a mountain
    // at the room's level and a talking head at their voice's, so the gap between them shows. Double-click for Auto.
    internal sealed class MicMeter : Control
    {
        // The bar runs from -60 dB, under any room, to 0 dB, the loudest a mic gives.
        internal const double Floor = -60;
        internal static double Decibels(double level) { return level <= 0 ? Floor : Math.Max(Floor, Math.Min(0, 20 * Math.Log10(level))); }
        internal static double Level(double decibels) { return Math.Pow(10, decibels / 20); }
        // A spot on the bar, 1 to 100, and back; 0 is Auto.
        internal static int Spot(double level) { return Math.Max(1, Math.Min(100, (int)Math.Round((Decibels(level) - Floor) / -Floor * 100))); }
        internal static double SpotLevel(int spot) { return Level(Floor + Math.Max(1, Math.Min(100, spot)) * -Floor / 100); }

        internal Func<MicReading> Read;
        // A spot dragged to, or 0 for Auto on a double-click.
        internal event Action<int> Picked;

        private readonly Timer ticks = new Timer { Interval = 66 };
        private MicReading shown;
        private bool dragging;
        private double peak;

        internal MicMeter()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Cursor = Cursors.SizeWE;
            TabStop = true;
            AccessibleRole = AccessibleRole.Slider;
            ticks.Tick += delegate { Refresh(false); };
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) { Refresh(true); ticks.Start(); } else ticks.Stop();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) ticks.Dispose();
            base.Dispose(disposing);
        }

        // Reads her levels now, as its timer does while it shows.
        internal void Take() { Refresh(true); }

        private void Refresh(bool always)
        {
            if (Read == null || IsDisposed) return;
            MicReading next;
            try { next = Read(); }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); return; }
            // A peak that falls back slowly, so a word shows long enough to read.
            peak = next.Live ? Math.Max(next.Now, peak * 0.86) : 0;
            if (!always && next.Now == shown.Now && next.Room == shown.Room && next.Voice == shown.Voice && next.Gate == shown.Gate && next.Auto == shown.Auto && next.Live == shown.Live && peak < 0.0001) return;
            shown = next;
            AccessibleDescription = Said;
            Invalidate();
        }

        // What it shows, for a screen reader and the checks.
        internal string Said
        {
            get
            {
                string gate = (shown.Auto ? "Auto, " : "") + "talk starts at " + (int)Math.Round(Decibels(shown.Gate)) + " dB";
                string room = shown.Room < 0 ? "background not heard yet" : "background noise " + (int)Math.Round(Decibels(shown.Room)) + " dB";
                string voice = shown.Voice <= 0 ? "no line heard yet" : "your voice " + (int)Math.Round(Decibels(shown.Voice)) + " dB";
                return gate + ", " + room + ", " + voice + (shown.Live ? "" : ", last heard");
            }
        }

        private float X(double level, RectangleF bar) { return bar.Left + (float)((Decibels(level) - Floor) / -Floor) * bar.Width; }

        private RectangleF Bar
        {
            get
            {
                float s = Height / 26f;
                return new RectangleF(2 * s, 4 * s, Width - 4 * s, 9 * s);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics pen = e.Graphics;
            pen.Clear(Neon.Ground);
            pen.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Height / 26f;
            RectangleF bar = Bar;
            using (Brush track = new SolidBrush(Neon.Dash)) pen.FillRectangle(track, bar);
            if (shown.Live && peak > 0)
            {
                float lit = X(peak, bar) - bar.Left;
                bool talk = peak >= shown.Gate;
                using (Brush fill = new SolidBrush(talk ? Neon.Lit : Neon.Faint)) pen.FillRectangle(fill, bar.Left, bar.Top, lit, bar.Height);
            }
            using (Pen edge = new Pen(Focused ? Neon.Hot : Neon.Edge, Math.Max(1f, s))) pen.DrawRectangle(edge, bar.Left, bar.Top, bar.Width, bar.Height);
            // Where talk starts: a bar across, solid when they set it and dashed while Auto follows the room.
            float gate = X(shown.Gate, bar);
            using (Pen line = new Pen(Neon.Hot, 2 * s) { DashStyle = shown.Auto ? DashStyle.Dash : DashStyle.Solid }) pen.DrawLine(line, gate, bar.Top - 3 * s, gate, bar.Bottom + 3 * s);
            float under = bar.Bottom + 4 * s, mark = 8 * s;
            if (shown.Room >= 0) Mountain(pen, X(shown.Room, bar), under, mark);
            if (shown.Voice > 0) Head(pen, X(shown.Voice, bar), under, mark);
        }

        // The room's level: two peaks.
        private static void Mountain(Graphics pen, float x, float top, float size)
        {
            PointF[] peaks = { new PointF(x - size * 0.75f, top + size), new PointF(x - size * 0.2f, top + size * 0.15f), new PointF(x + size * 0.1f, top + size * 0.55f), new PointF(x + size * 0.35f, top + size * 0.3f), new PointF(x + size * 0.75f, top + size) };
            using (Brush fill = new SolidBrush(Neon.Faint)) pen.FillPolygon(fill, peaks);
        }

        // Their voice's level: a head with a sound line in front of its mouth.
        private static void Head(Graphics pen, float x, float top, float size)
        {
            using (Brush fill = new SolidBrush(Neon.Cyan))
            using (Pen sound = new Pen(Neon.Cyan, Math.Max(1f, size / 8)))
            {
                pen.FillEllipse(fill, x - size * 0.45f, top, size * 0.6f, size * 0.6f);
                pen.FillPie(fill, x - size * 0.65f, top + size * 0.55f, size, size * 0.9f, 180, 180);
                pen.DrawArc(sound, x + size * 0.1f, top + size * 0.1f, size * 0.45f, size * 0.45f, -50, 100);
            }
        }

        private int SpotAt(int x)
        {
            RectangleF bar = Bar;
            double part = Math.Max(0, Math.Min(1, (x - bar.Left) / bar.Width));
            return Math.Max(1, Math.Min(100, (int)Math.Round(part * 100)));
        }

        private void Pick(int spot)
        {
            if (Picked != null) Picked(spot);
            Refresh(true);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Focus();
            if (e.Clicks >= 2) { dragging = false; Pick(0); return; }
            dragging = true;
            Pick(SpotAt(e.X));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging) Pick(SpotAt(e.X));
        }

        protected override void OnMouseUp(MouseEventArgs e) { dragging = false; base.OnMouseUp(e); }

        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int spot = Spot(shown.Gate);
            if (e.KeyCode == Keys.Left) Pick(spot - 2);
            else if (e.KeyCode == Keys.Right) Pick(spot + 2);
            else if (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete) Pick(0);
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    }
}
