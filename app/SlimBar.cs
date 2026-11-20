using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CapriDesktop
{
    // A scrollbar of hers, for anywhere of hers that scrolls.
    //
    // Windows' own bar is seventeen pixels of grey chrome with arrow buttons, and it looked exactly like what
    // it is in the middle of her windows: the one part of her that did not follow the Mainframe colours
    // setting, because it is drawn by Windows and not by her.
    //
    // This is six pixels of her palette with no buttons and nothing to click but the bar itself, and it takes
    // its colours from Neon, so it changes with every other window when the look is changed.
    //
    // It drives an ordinary AutoScroll panel rather than replacing how scrolling works: the wheel, the
    // keyboard and anything that moves the panel still do what they did, and this follows them.
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
            }
            watching = panel;
            if (watching == null) return;
            watching.Scroll += Moved;
            watching.ClientSizeChanged += Changed;
            watching.ControlAdded += Added;
            watching.ControlRemoved += Added;
            watching.MouseWheel += Wheeled;
            Sync();
        }

        // A text box scrolls itself, in lines. Its own bar is turned off by the caller, so there is nothing
        // to hide and nothing to fight: this asks how many lines there are, which one is at the top, and
        // tells it to move by a number of lines. Typing, clicking, the wheel and the arrow keys all still
        // scroll it, and the bar follows them.
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
            ScrollTo(e.Y < top ? top - tall : top + tall);
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

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (Scrolled != null) Scrolled();
        }
    }
}
