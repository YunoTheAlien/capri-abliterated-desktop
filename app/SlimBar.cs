using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CapriDesktop
{
    // A scrollbar of hers: six pixels of her palette with no buttons, colours from Neon so it follows every look. It
    // drives an ordinary AutoScroll panel, so wheel and keys still work and it follows them.
    internal sealed class SlimBar : Control
    {
        internal const int Thin = 6;
        private const int Least = 24;

        private ScrollableControl watching;
        private TextBoxBase typing;
        private bool dragging;

        // Told when somebody moves it themselves, so a window that follows the end can stop following
        // rather than yanking them back on the next line written.
        internal Action Taken;
        private int grabbed;
        private int grabbedAt;

        internal SlimBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Width = Thin;
            TabStop = false;
        }

        // The panel this bar belongs to. Hooks what moves it, so the bar follows the panel rather than the
        // panel following the bar: everything that scrolled before still scrolls it.
        internal void Watch(ScrollableControl panel)
        {
            if (watching != null)
            {
                watching.Scroll -= Moved;
                watching.ClientSizeChanged -= Changed;
                watching.ControlAdded -= Added;
                watching.ControlRemoved -= Added;
                watching.MouseWheel -= Wheeled;
                ScrollPanel was = watching as ScrollPanel;
                if (was != null) was.Scrolled -= Followed;
            }
            watching = panel;
            if (watching == null) return;
            watching.Scroll += Moved;
            watching.ClientSizeChanged += Changed;
            watching.ControlAdded += Added;
            watching.ControlRemoved += Added;
            watching.MouseWheel += Wheeled;
            // Her own panel glides without Windows' scroll events, so the bar follows it frame by frame.
            ScrollPanel gliding = watching as ScrollPanel;
            if (gliding != null) gliding.Scrolled += Followed;
            Sync();
        }

        // A text box scrolls itself in lines, with its own bar off: this reads the line count and top line and moves it
        // by lines, and follows whatever else scrolls it.
        internal void Watch(TextBoxBase box)
        {
            if (typing != null)
            {
                typing.TextChanged -= Typed;
                typing.KeyUp -= Keyed;
                typing.MouseUp -= Clicked;
                typing.MouseWheel -= Rolled;
                typing.Resize -= Changed;
            }
            typing = box;
            if (typing == null) return;
            typing.TextChanged += Typed;
            typing.KeyUp += Keyed;
            typing.MouseUp += Clicked;
            typing.MouseWheel += Rolled;
            typing.Resize += Changed;
            Sync();
        }

        private void Typed(object sender, EventArgs e) { Sync(); }
        private void Keyed(object sender, KeyEventArgs e) { Invalidate(); }
        private void Clicked(object sender, MouseEventArgs e) { Invalidate(); }
        private void Rolled(object sender, MouseEventArgs e) { Invalidate(); }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int SendMessage(IntPtr window, int message, int wide, int tall);

        private const int LineCount = 0x00BA, FirstLine = 0x00CE, LineScroll = 0x00B6;

        private int Lines
        {
            get { return typing == null || !typing.IsHandleCreated ? 0 : SendMessage(typing.Handle, LineCount, 0, 0); }
        }

        private int Showing
        {
            get
            {
                if (typing == null) return 0;
                int tall = TextRenderer.MeasureText("Ay", typing.Font).Height;
                return tall <= 0 ? 0 : Math.Max(1, typing.ClientSize.Height / tall);
            }
        }

        private int Top
        {
            get { return typing == null || !typing.IsHandleCreated ? 0 : SendMessage(typing.Handle, FirstLine, 0, 0); }
        }

        private void Moved(object sender, ScrollEventArgs e) { Invalidate(); }
        private void Changed(object sender, EventArgs e) { Sync(); }
        private void Added(object sender, ControlEventArgs e) { Sync(); }
        private void Wheeled(object sender, MouseEventArgs e) { Invalidate(); }
        private void Followed() { Invalidate(); }

        // How far it can scroll, how far it has, and whether there is anything to show.
        // In pixels for a panel and in lines for a text box. The bar does not care which: it is a share of
        // the way down either way.
        private int Reach
        {
            get
            {
                if (typing != null) return Math.Max(0, Lines - Showing);
                return watching == null ? 0 : Math.Max(0, watching.DisplayRectangle.Height - watching.ClientSize.Height);
            }
        }

        private int At
        {
            get
            {
                if (typing != null) return Math.Max(0, Top);
                return watching == null ? 0 : Math.Max(0, -watching.AutoScrollPosition.Y);
            }
        }

        // Scrolled all the way down, or nothing to scroll.
        internal bool AtEnd { get { return At >= Reach; } }

        internal void Sync()
        {
            bool wanted = Reach > 0;
            if (Visible != wanted) Visible = wanted;
            Invalidate();
        }

        private int Thumb
        {
            get
            {
                if (typing != null) { int all = Math.Max(1, Lines); return Math.Max(Least, (int)((long)Height * Math.Min(all, Showing) / all)); }
                return watching == null ? Least : Math.Max(Least, (int)((long)Height * watching.ClientSize.Height / Math.Max(1, watching.DisplayRectangle.Height)));
            }
        }

        private int ThumbTop
        {
            get
            {
                int room = Height - Thumb, reach = Reach;
                return reach <= 0 || room <= 0 ? 0 : (int)((long)room * At / reach);
            }
        }

        private void ScrollTo(int top)
        {
            int room = Height - Thumb;
            int want = room <= 0 ? 0 : (int)((long)Math.Max(0, Math.Min(room, top)) * Reach / room);
            if (typing != null)
            {
                if (typing.IsHandleCreated) SendMessage(typing.Handle, LineScroll, 0, want - Top);
                if (Taken != null) { try { Taken(); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); } }
                Invalidate();
                return;
            }
            if (watching == null) return;
            watching.AutoScrollPosition = new Point(0, want);
            Invalidate();
        }

        // A click on the bar a page down or up, as the mouse would, for the checks.
        internal void Page(bool down) { PageBy(down); }

        // A page of a panel is what shows less a little, so a row cut at the edge is shown whole on the next page.
        private void PageBy(bool down)
        {
            if (typing != null || watching == null) { ScrollTo(down ? ThumbTop + Thumb : ThumbTop - Thumb); return; }
            int step = Math.Max(1, watching.ClientSize.Height - Math.Max(8, watching.ClientSize.Height / 8));
            watching.AutoScrollPosition = new Point(0, Math.Max(0, Math.Min(Reach, At + (down ? step : -step))));
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            // A text box bar has no panel. Testing only for the panel meant no text box bar could be clicked or
            // dragged at all, the log's included.
            if (e.Button != MouseButtons.Left || (watching == null && typing == null)) return;
            int top = ThumbTop, tall = Thumb;
            if (e.Y >= top && e.Y < top + tall)
            {
                dragging = true;
                grabbed = e.Y;
                grabbedAt = top;
                Capture = true;
                return;
            }
            // Anywhere else on the bar jumps a page that way, the way her sliders move by a step.
            PageBy(e.Y >= top);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging) ScrollTo(grabbedAt + (e.Y - grabbed));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            dragging = false;
            Capture = false;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (typing != null)
            {
                if (typing.IsHandleCreated) SendMessage(typing.Handle, LineScroll, 0, e.Delta > 0 ? -3 : 3);
                if (Taken != null) { try { Taken(); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); } }
                Invalidate();
                return;
            }
            if (watching == null) return;
            ScrollPanel gliding = watching as ScrollPanel;
            if (gliding != null && SystemInformation.UIEffectsEnabled) { gliding.GlideBy(-e.Delta); return; }
            watching.AutoScrollPosition = new Point(0, Math.Max(0, Math.Min(Reach, At - e.Delta)));
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (watching == null && typing == null) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Brush track = new SolidBrush(Neon.Field)) Rounded(e.Graphics, track, new Rectangle(0, 0, Width, Height));
            if (Reach <= 0) return;
            Rectangle thumb = new Rectangle(0, ThumbTop, Width, Thumb);
            using (Brush bar = new SolidBrush(dragging ? Neon.Rose : Neon.Cyan)) Rounded(e.Graphics, bar, thumb);
        }

        private static void Rounded(Graphics graphics, Brush brush, Rectangle box)
        {
            if (box.Height <= 0 || box.Width <= 0) return;
            int round = Math.Min(box.Width, box.Height);
            if (round < 3) { graphics.FillRectangle(brush, box); return; }
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddArc(box.Left, box.Top, round, round, 180, 180);
                path.AddArc(box.Left, box.Bottom - round, round, round, 0, 180);
                path.CloseFigure();
                graphics.FillPath(brush, path);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { Watch((ScrollableControl)null); Watch((TextBoxBase)null); }
            base.Dispose(disposing);
        }
    }

    // A panel that scrolls without Windows' own bar on it. AutoScroll still does the scrolling, so the wheel
    // and the keyboard are unchanged; the bar itself is told to stay hidden every time Windows works out the
    // panel's frame, which is the one moment it would otherwise put it back.
    internal sealed class ScrollPanel : Panel
    {
        [DllImport("user32.dll")] private static extern bool ShowScrollBar(IntPtr window, int bar, bool show);

        private const int WmNcCalcSize = 0x0083;
        private const int BothBars = 3;

        internal ScrollPanel() { AutoScroll = true; }

        internal event Action Scrolled;

        // Moved by code, like a row scrolled into view: whatever watches it is told.
        internal void Moved() { if (Scrolled != null) Scrolled(); }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmNcCalcSize && IsHandleCreated) ShowScrollBar(Handle, BothBars, false);
            base.WndProc(ref message);
        }

        protected override void OnScroll(ScrollEventArgs e)
        {
            base.OnScroll(e);
            if (Scrolled != null) Scrolled();
        }

        // The wheel glides: each notch adds to where it is heading and it eases there over a few frames, instead of
        // Windows' own jump of a whole notch at once. With Windows' animations off it jumps as before.
        private readonly Clock.Frames glide = new Clock.Frames("slim bar glide") { Interval = 15 };
        private int heading = -1;

        // Whether a glide is still easing to where it was sent.
        internal bool Gliding { get { return glide.Enabled; } }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (!SystemInformation.UIEffectsEnabled) { base.OnMouseWheel(e); if (Scrolled != null) Scrolled(); return; }
            int notch = Math.Max(1, SystemInformation.MouseWheelScrollLines) * Math.Max(16, VerticalScroll.SmallChange);
            GlideBy(-e.Delta * notch / 120);
            HandledMouseEventArgs handled = e as HandledMouseEventArgs;
            if (handled != null) handled.Handled = true;
        }

        // Ease towards a point that many pixels further on; a notch while it moves adds to where it is heading.
        internal void GlideBy(int pixels)
        {
            int reach = Math.Max(0, DisplayRectangle.Height - ClientSize.Height);
            int from = heading >= 0 ? heading : -AutoScrollPosition.Y;
            heading = Math.Max(0, Math.Min(reach, from + pixels));
            if (!glide.Enabled)
            {
                glide.Tick -= Glided;
                glide.Tick += Glided;
                glide.Start();
            }
        }

        private void Glided(object sender, EventArgs e)
        {
            int now = -AutoScrollPosition.Y, gap = heading - now;
            if (heading < 0 || Math.Abs(gap) <= 1 || IsDisposed)
            {
                if (heading >= 0 && !IsDisposed) AutoScrollPosition = new Point(-AutoScrollPosition.X, heading);
                heading = -1;
                glide.Stop();
            }
            else
            {
                // A third of the way each frame: quick at first, settling softly.
                int step = gap / 3;
                if (step == 0) step = Math.Sign(gap);
                AutoScrollPosition = new Point(-AutoScrollPosition.X, now + step);
            }
            if (Scrolled != null) Scrolled();
        }

        // A panel turned by code glides there over several frames and stops exactly where it was sent, at both ends.
#if CHECKS
        internal static void CheckGlide(List<string> checks)
        {
            if (!SystemInformation.UIEffectsEnabled) { checks.Add("LIMIT the wheel's glide: Windows' animations are off on this PC, so her lists jump as Windows' do"); return; }
            using (Form holder = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-6000, -6000), Size = new Size(300, 200), ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None })
            {
                ScrollPanel list = new ScrollPanel { Dock = DockStyle.Fill };
                list.Controls.Add(new Panel { Bounds = new Rectangle(0, 0, 200, 3000) });
                holder.Controls.Add(list);
                holder.Show();
                List<int> seen = new List<int>();
                Func<int, int, List<int>> go = delegate(int pixels, int want)
                {
                    List<int> stops = new List<int>();
                    list.GlideBy(pixels);
                    DateTime until = DateTime.UtcNow.AddSeconds(2);
                    while (DateTime.UtcNow < until && (list.glide.Enabled || -list.AutoScrollPosition.Y != want))
                    {
                        Application.DoEvents();
                        int at = -list.AutoScrollPosition.Y;
                        if (stops.Count == 0 || stops[stops.Count - 1] != at) stops.Add(at);
                        System.Threading.Thread.Sleep(4);
                    }
                    return stops;
                };
                List<int> down = go(360, 360);
                if (-list.AutoScrollPosition.Y != 360) throw new Exception("A glide of 360 ended at " + (-list.AutoScrollPosition.Y) + ".");
                if (down.Count < 4 || down[0] >= 360) throw new Exception("The wheel jumped rather than glided: " + string.Join(", ", down.ConvertAll(x => x.ToString()).ToArray()));
                int reach = list.DisplayRectangle.Height - list.ClientSize.Height;
                go(100000, reach);
                if (-list.AutoScrollPosition.Y != reach) throw new Exception("A glide past the end stopped at " + (-list.AutoScrollPosition.Y) + ", not the end " + reach + ".");
                go(-100000, 0);
                if (-list.AutoScrollPosition.Y != 0) throw new Exception("A glide past the top stopped at " + (-list.AutoScrollPosition.Y) + ".");
                checks.Add("PASS her lists glide on the wheel: " + down.Count + " frames to 360 px, and they stop exactly at the end and the top");
            }
        }
#endif

        protected override void Dispose(bool disposing)
        {
            if (disposing) glide.Dispose();
            base.Dispose(disposing);
        }
    }
}
