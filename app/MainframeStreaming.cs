using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Capri Stream: one frame round her capture window (the box) and every option, flat. The frame is cut open where the
    // box sits, and only the box is captured, so Spout, Direct3D and a Window Capture take her alone.
    internal sealed partial class Mainframe
    {
        private StreamingSheet streamingSheet;
        // The zoom its window was fitted from, so an open with nothing changed keeps the window it has.
        private float streamingFitted;

        // The side made to fit: its zoom from its design, then smaller while its rows, as the fonts on this PC measure them,
        // are taller than the screen's room. Measured as it is on the screen it opens on, whose scale can differ from here.
        private StreamingSheet MakeStreaming(Form box)
        {
            float fitted = SheetZoom(StreamingSheet.Wide, StreamingSheet.Tall), use = fitted;
            StreamingSheet made = new StreamingSheet(this, use, capri.StreamMenu, box);
            Rectangle area = Fits.Area(this);
            float there = Fits.Device(this) / Math.Max(0.5f, DeviceScale());
            int room = area.Height - Fits.Margin;
            for (int tries = 0; tries < 4 && made.Height * there > room && room > 0; tries++)
            {
                use = Math.Max(SheetFloor, use * room / (made.Height * there) * 0.98f);
                made.LetGo();
                made.Dispose();
                made = new StreamingSheet(this, use, capri.StreamMenu, box);
            }
            streamingFitted = fitted;
            return made;
        }

        // The side for her capture window: opened beside it, or brought back if it is there already.
        internal void OpenStreaming(Form box)
        {
            if (box == null || box.IsDisposed) return;
            // A side made for another box or zoom goes, and lets go of its box first so the box stays open.
            if (streamingSheet != null && !streamingSheet.IsDisposed && (streamingFitted != SheetZoom(StreamingSheet.Wide, StreamingSheet.Tall) || streamingSheet.CaptureBox != box)) { streamingSheet.LetGo(); streamingSheet.Close(); }
            if (streamingSheet == null || streamingSheet.IsDisposed) streamingSheet = MakeStreaming(box);
            if (!streamingSheet.Visible) streamingSheet.Show(box);
            streamingSheet.Glue();
            streamingSheet.Activate();
        }

        internal void CloseStreaming() { if (streamingSheet != null && !streamingSheet.IsDisposed) streamingSheet.Close(); }

        // For the checks: what the open window shows, the side itself, and a row pressed.
        internal string StreamingText { get { return StreamingOpen ? streamingSheet.Shown : null; } }

        internal bool StreamingOpen { get { return streamingSheet != null && !streamingSheet.IsDisposed && streamingSheet.Visible; } }

        internal Form StreamingSide { get { return streamingSheet != null && !streamingSheet.IsDisposed ? streamingSheet : null; } }

        internal int StreamingDeepest { get { return streamingSheet == null ? 0 : streamingSheet.Deepest; } }

        internal int StreamingBoxMoves { get { return streamingSheet == null ? 0 : streamingSheet.BoxMoves; } }

        internal bool StreamingOnLeft { get { return streamingSheet != null && streamingSheet.OnLeft; } }

        internal void PressStreaming(string text) { if (streamingSheet != null && !streamingSheet.IsDisposed) streamingSheet.Press(text); }

        internal string StreamingCrowded { get { return streamingSheet == null || streamingSheet.IsDisposed ? "not open" : streamingSheet.Crowded(); } }

        internal void PictureStreaming(string path) { if (streamingSheet != null && !streamingSheet.IsDisposed) streamingSheet.Picture(path); }

        // A window's edges as seen, without the invisible borders Windows pads it with.
        internal static Rectangle SeenEdges(Form window)
        {
            SeenBox seen;
            if (window.IsHandleCreated && DwmGetWindowAttribute(window.Handle, 9, out seen, Marshal.SizeOf(typeof(SeenBox))) == 0 && seen.Right > seen.Left)
                return Rectangle.FromLTRB(seen.Left, seen.Top, seen.Right, seen.Bottom);
            return window.Bounds;
        }

        [StructLayout(LayoutKind.Sequential)] private struct SeenBox { internal int Left, Top, Right, Bottom; }

        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out SeenBox value, int size);

        private sealed class StreamingSheet : ModSheet
        {
            internal const int Wide = 480, Tall = 370, Column = 200, FirstRow = 64;
            internal const string Capture = "Capture", Live = "Going live", Twitch = "Twitch";
            // Rows that open something rather than switch it.
            private static readonly string[] Actions = { "Room", "Who's live now", "Edit twitch.md", "Forget viewers" };
            // Her capture window; null only for a check that looks at the side alone.
            internal readonly Form CaptureBox;
            private readonly ToolStripMenuItem source;
            private readonly List<Head> heads = new List<Head>();
            private readonly List<KeyValuePair<Toggle, ToolStripMenuItem>> rows = new List<KeyValuePair<Toggle, ToolStripMenuItem>>();
            private readonly List<KeyValuePair<Key, ToolStripMenuItem>> keys = new List<KeyValuePair<Key, ToolStripMenuItem>>();
            private readonly Clock.Ticker follow = new Clock.Ticker("streaming follow") { Interval = 700 };
            private bool syncing, placing, boxGone, closed;
            private int natural, depth;
            // Where the box sits in the frame, and each option's place before the box pushed the options right of it.
            private Rectangle hole;
            private readonly Dictionary<Control, int> lefts = new Dictionary<Control, int>();
            private readonly Control shut;
            private readonly Key tuck;
            // For the checks: the deepest the glue ever nested, and how often the side moved the box.
            internal int Deepest, BoxMoves;

            internal StreamingSheet(Mainframe owner, float zoom, ToolStripMenuItem from, Form box) : base(owner, zoom, "Streaming", "CAPRI STREAM", Wide, Tall)
            {
                source = from;
                CaptureBox = box;
                Control title = Controls[0];
                shut = Controls[1];
                // Minimised, the box goes to the taskbar and Windows hides the frame with it.
                tuck = Make("", Wide - 84, 20, 26, 22, delegate { if (CaptureBox != null && !CaptureBox.IsDisposed) CaptureBox.WindowState = FormWindowState.Minimized; });
                tuck.Glyph = 1;
                tuck.AccessibleName = "Minimize";
                Lay();
                Frame.Fit(this);
                foreach (Control part in Controls) if (part != title && part != shut && part != tuck) lefts[part] = part.Left;
                // Tall enough for every row as the fonts on this PC lay them out, so nothing is cut off whatever its text size.
                int deepest = 0;
                foreach (Control row in Controls) deepest = Math.Max(deepest, row.Bottom);
                if (deepest + Z(16) > ClientSize.Height) ClientSize = new Size(ClientSize.Width, deepest + Z(16));
                natural = ClientSize.Height;
                // Her state moves from elsewhere too (a voice command, the box closed), so the rows follow it.
                // These run inside Windows' own calls (a move, a timer, a close): a throw there ends her, so each one is caught
                // and written down with where it was.
                follow.Tick += delegate { try { if (Visible) Sync(); } catch (Exception caughtOne) { Brain.Caught(caughtOne); } };
                follow.Start();
                Disposed += delegate { follow.Dispose(); };
                if (box == null) return;
                // Glued: the box moving or resizing takes the side with it, and the side dragged takes the box. Minimised,
                // Windows hides the side with its owner.
                box.LocationChanged += BoxMoved;
                box.SizeChanged += BoxMoved;
                box.FormClosing += BoxClosing;
                box.FormClosed += BoxClosed;
                // Once only: closing the box raises this side's closing first, and closing the box again from here looped.
                FormClosed += delegate
                {
                    if (closed) return;
                    closed = true;
                    Unhook();
                    try { if (!boxGone) Frame.capri.CloseCapture(); } catch (Exception caughtOne) { Brain.Caught(caughtOne); }
                };
            }

            // A side made and let go before it was shown (a refit) leaves the box alone too.
            private void Unhook()
            {
                if (CaptureBox == null) return;
                CaptureBox.FormClosing -= BoxClosing;
                CaptureBox.LocationChanged -= BoxMoved;
                CaptureBox.SizeChanged -= BoxMoved;
                CaptureBox.FormClosed -= BoxClosed;
            }

            private void BoxMoved(object sender, EventArgs e) { try { Glue(); } catch (Exception caughtOne) { Brain.Caught(caughtOne); } }

            private void BoxClosed(object sender, FormClosedEventArgs e)
            {
                boxGone = true;
                try { if (!IsDisposed) Close(); } catch (Exception caughtOne) { Brain.Caught(caughtOne); }
            }

            private void BoxClosing(object sender, FormClosingEventArgs e) { boxGone = true; }

            internal void LetGo() { boxGone = true; Unhook(); }

            protected override void OnFormClosing(FormClosingEventArgs e)
            {
                if (e.CloseReason == CloseReason.FormOwnerClosing) boxGone = true;
                base.OnFormClosing(e);
            }

            protected override void OnLocationChanged(EventArgs e)
            {
                base.OnLocationChanged(e);
                if (placing || !Visible || CaptureBox == null || CaptureBox.IsDisposed || CaptureBox.WindowState == FormWindowState.Minimized) return;
                int dx = Left + HoleLeft - CaptureBox.Left, dy = Top + HoleTop - CaptureBox.Top;
                if (dx == 0 && dy == 0) return;
                placing = true;
                Deepest = Math.Max(Deepest, ++depth);
                try { BoxMoves++; CaptureBox.Location = new Point(CaptureBox.Left + dx, CaptureBox.Top + dy); }
                catch (Exception caughtOne) { Brain.Caught(caughtOne); }
                finally { placing = false; depth--; }
                // Dragged by the side, the pair is kept on its screen too.
                try { Glue(); } catch (Exception caughtOne) { Brain.Caught(caughtOne); }
            }

            // Kept for the checks: the frame is one window now, so the options are never on the box's left.
            internal bool OnLeft { get { return false; } }

            private int HoleLeft { get { return Z(12); } }

            private int HoleTop { get { return Z(52); } }

            // The frame round the box: the box in its opening, the options right of it. The box is at least as tall as the
            // options, so no band is left empty, and the pair is kept whole on its screen.
            internal void Glue()
            {
                if (placing || IsDisposed || !Visible || CaptureBox == null || CaptureBox.IsDisposed || CaptureBox.WindowState == FormWindowState.Minimized) return;
                placing = true;
                Deepest = Math.Max(Deepest, ++depth);
                try
                {
                    Size least = new Size(Z(160), Math.Max(Z(160), natural - HoleTop - Z(12)));
                    if (CaptureBox.MinimumSize != least) CaptureBox.MinimumSize = least;
                    Rectangle boxAt = CaptureBox.Bounds;
                    Rectangle area = Screen.FromRectangle(boxAt).WorkingArea;
                    int offset = HoleLeft + boxAt.Width - Z(14);
                    Rectangle want = new Rectangle(boxAt.Left - HoleLeft, boxAt.Top - HoleTop, offset + Z(Wide), Math.Max(HoleTop + boxAt.Height + Z(12), natural));
                    int dx = want.Right > area.Right ? area.Right - want.Right : 0, dy = want.Bottom > area.Bottom ? area.Bottom - want.Bottom : 0;
                    if (want.Left + dx < area.Left) dx = area.Left - want.Left;
                    if (want.Top + dy < area.Top) dy = area.Top - want.Top;
                    if (dx != 0 || dy != 0) { CaptureBox.Location = new Point(boxAt.Left + dx, boxAt.Top + dy); want.Offset(dx, dy); }
                    hole = new Rectangle(HoleLeft, HoleTop, boxAt.Width, boxAt.Height);
                    foreach (KeyValuePair<Control, int> part in lefts) if (part.Key.Left != part.Value + offset) part.Key.Left = part.Value + offset;
                    shut.Left = want.Width - Z(54);
                    tuck.Left = want.Width - Z(84);
                    if (Bounds != want) Bounds = want;
                    Shape();
                }
                finally { placing = false; depth--; }
            }

            // Cut as the Mainframe is, and open where the box sits so the box shows through.
            private void Shape()
            {
                Clip(this, ScaleOf(this));
                if (hole.IsEmpty || Region == null) return;
                Region open = Region.Clone();
                open.Exclude(hole);
                Region old = Region;
                Region = open;
                old.Dispose();
                Invalidate();
            }

            protected override void OnSizeChanged(EventArgs e)
            {
                base.OnSizeChanged(e);
                if (!hole.IsEmpty) Shape();
            }

            // Her Streaming menu in three groups: Window's items as Capture, Going live with Lock her in place, and Twitch.
            private List<KeyValuePair<string, List<ToolStripMenuItem>>> Groups()
            {
                List<ToolStripMenuItem> capture = new List<ToolStripMenuItem>(), live = new List<ToolStripMenuItem>(), twitch = new List<ToolStripMenuItem>();
                if (source != null)
                {
                    Frame.capri.StreamTicks();
                    foreach (ToolStripItem one in source.DropDownItems)
                    {
                        ToolStripMenuItem item = one as ToolStripMenuItem;
                        if (item == null) continue;
                        List<ToolStripMenuItem> into = item.Text == "Window" ? capture : item.Text == "Going live" ? live : item.Text == "Twitch" ? twitch : null;
                        if (into == null) { if (!item.HasDropDownItems) live.Add(item); continue; }
                        foreach (ToolStripItem sub in item.DropDownItems) { ToolStripMenuItem each = sub as ToolStripMenuItem; if (each != null && !each.HasDropDownItems) into.Add(each); }
                    }
                }
                return new List<KeyValuePair<string, List<ToolStripMenuItem>>>
                {
                    new KeyValuePair<string, List<ToolStripMenuItem>>(Capture, capture),
                    new KeyValuePair<string, List<ToolStripMenuItem>>(Live, live),
                    new KeyValuePair<string, List<ToolStripMenuItem>>(Twitch, twitch),
                };
            }

            // Every option laid out, no folds: Capture down the left, Going live then Twitch down the right.
            private void Lay()
            {
                Font head = Type(10f * Zoom, true);
                int right = FirstRow;
                List<KeyValuePair<string, List<ToolStripMenuItem>>> groups = Groups();
                for (int g = 0; g < groups.Count; g++)
                {
                    if (groups[g].Value.Count == 0) continue;
                    int x = g == 0 ? 28 : 52 + Column, y = g == 0 || right == FirstRow ? FirstRow : right + 12;
                    heads.Add(Heading(this, groups[g].Key.ToUpperInvariant() + " >", new Point(Z(x), Z(y)), head, White, Column + 14));
                    y += 24;
                    foreach (ToolStripMenuItem item in groups[g].Value)
                    {
                        ToolStripMenuItem held = item;
                        string tip = item.Text + (string.IsNullOrEmpty(item.ToolTipText) ? "" : ". " + item.ToolTipText);
                        if (Array.IndexOf(Actions, item.Text) >= 0)
                        {
                            Key press = Make(item.Text, x, y + 1, 160, 22, delegate { held.PerformClick(); if (!IsDisposed) Sync(); });
                            press.AccessibleName = tip;
                            keys.Add(new KeyValuePair<Key, ToolStripMenuItem>(press, item));
                            y += 30;
                            continue;
                        }
                        Toggle row = new Toggle { Text = item.Text, Font = Font, Bounds = Box(x, y, Column, 22), Checked = item.Checked };
                        row.AccessibleName = tip;
                        row.CheckedChanged += delegate { if (syncing) return; held.PerformClick(); if (!IsDisposed) Sync(); };
                        Controls.Add(row);
                        rows.Add(new KeyValuePair<Toggle, ToolStripMenuItem>(row, item));
                        y += 28;
                    }
                    if (g > 0) right = y;
                }
            }

            // Every row from its item, after her menu's own ticks have run.
            internal void Sync()
            {
                if (IsDisposed) return;
                syncing = true;
                try
                {
                    Groups();
                    foreach (KeyValuePair<Toggle, ToolStripMenuItem> row in rows)
                    {
                        if (row.Key.Checked != row.Value.Checked) row.Key.Checked = row.Value.Checked;
                        if (row.Key.Text != row.Value.Text) row.Key.Text = row.Value.Text;
                    }
                }
                finally { syncing = false; }
            }

            // What it shows, for the checks: each group's heading, its rows with their state, and its keys.
            internal string Shown
            {
                get
                {
                    List<string> said = new List<string>();
                    foreach (Control one in Controls)
                    {
                        Head title = one as Head;
                        Toggle row = one as Toggle;
                        Key press = one as Key;
                        if (title != null && heads.Contains(title)) said.Add("[" + title.Shown + "]");
                        else if (row != null) said.Add(row.Text + (row.Checked ? " on" : " off"));
                        else if (press != null && press.Text.Length > 0) said.Add(press.Text + " key");
                    }
                    return string.Join(" | ", said.ToArray());
                }
            }

            internal void Press(string text)
            {
                foreach (KeyValuePair<Toggle, ToolStripMenuItem> row in rows) if (row.Key.Text == text) { row.Key.Checked = !row.Key.Checked; return; }
                foreach (KeyValuePair<Key, ToolStripMenuItem> press in keys) if (press.Key.Text == text) { press.Key.PerformClick(); return; }
            }
        }
    }
}

namespace CapriDesktop
{
    internal sealed partial class Companion
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr into, uint flags);

        // Her right-click Streaming opens the box and its side as one window: every option laid out, glued through a move,
        // a resize, a drag and a minimise, the box holding nothing of the side, options doing what the menu does, and
        // closing either closes both.
#if CHECKS
        private void SmokeStreamingWindow(System.Collections.Generic.List<string> checks, string output)
        {
            bool lockedWas = streamLocked, clearWas = streamClear;
            Color colourWas = backdropColour;
            if (streamWindow != null && !streamWindow.IsDisposed) SetStreamWindow();
            try
            {
                streamOpenItem.PerformClick();
                Form box = streamWindow, side = mainframe.StreamingSide;
                if (box == null || box.IsDisposed || side == null || !side.Visible || side.Owner != box)
                    throw new Exception("Her right-click Streaming didn't open the capture window with its side: box " + (box != null && !box.IsDisposed) + ", side " + (side != null && side.Visible) + ", owned by the box " + (side != null && side.Owner == box));
                string shown = mainframe.StreamingText ?? "";
                foreach (string want in new[] { "[CAPTURE]", "[GOING LIVE]", "[TWITCH]", "Capture window on", "Transparent", "Green screen", "Streaming mode", "Capri only", "Room key", "Lock her in place", "Read chat", "Read chat aloud", "Read all chat aloud", "Remember viewers", "Who's live now key", "Edit twitch.md key", "Forget viewers key" })
                    if (shown.IndexOf(want, StringComparison.Ordinal) < 0) throw new Exception("The Streaming window is missing " + want + ": " + shown);
                if (shown.Contains("[") && (shown.Contains(", open") || shown.Contains(", folded"))) throw new Exception("The Streaming window still folds: " + shown);
                foreach (Control part in side.Controls)
                    if (part.Visible && (part.Bottom > side.ClientSize.Height || part.Right > side.ClientSize.Width)) throw new Exception("The Streaming window cuts off " + (part.Text ?? part.GetType().Name) + ": " + part.Bounds + " in " + side.ClientSize);
                using (Bitmap sideShot = new Bitmap(side.Width, side.Height)) { side.DrawToBitmap(sideShot, new Rectangle(Point.Empty, side.Size)); sideShot.Save(System.IO.Path.Combine(output, "streaming-side.png"), System.Drawing.Imaging.ImageFormat.Png); }

                List<string> unglued = new List<string>();
                Size inset = new Size(box.Left - side.Left, box.Top - side.Top);
                Func<string, bool> glued = delegate(string when)
                {
                    Rectangle seen = box.Bounds;
                    bool flush = side.Bounds.Contains(seen) && seen.Left - side.Left == inset.Width && seen.Top - side.Top == inset.Height && side.Bottom - seen.Bottom >= 0;
                    if (!flush) unglued.Add(when + ": box " + seen + ", frame " + side.Bounds);
                    return flush;
                };
                if (inset.Width <= 0 || inset.Height <= 0) unglued.Add("opened: the box isn't inside its frame, " + box.Bounds + " in " + side.Bounds);
                glued("opened");
                box.Location = new Point(box.Left + 37, box.Top + 23);
                glued("the box moved");
                box.ClientSize = new Size(box.ClientSize.Width + 40, box.ClientSize.Height + 30);
                glued("the box resized");
                side.Location = new Point(side.Left - 20, side.Top + 15);
                glued("the side dragged");
                box.WindowState = FormWindowState.Minimized;
                bool hidden = !Native.IsWindowVisible(side.Handle);
                box.WindowState = FormWindowState.Normal;
                bool back = Native.IsWindowVisible(side.Handle);
                glued("restored");
                if (unglued.Count > 0 || !hidden || !back)
                    throw new Exception("The side came unglued from the box: " + string.Join("; ", unglued.ToArray()) + "; hidden with the box minimised " + hidden + ", back with it " + back);

                // A third of the box pushed off its screen's right, then its left, then below, so it stays mostly on this screen:
                // the pair comes back whole, the side on the box's left at the right edge and on its right at the left.
                Rectangle room = Screen.FromRectangle(Mainframe.SeenEdges(box)).WorkingArea;
                Func<bool> whole = delegate { return room.Contains(side.Bounds) && side.Bounds.Contains(box.Bounds); };
                box.Location = new Point(room.Right - box.Width * 2 / 3, box.Top);
                bool rightEdge = whole();
                Rectangle rightAt = Rectangle.Union(Mainframe.SeenEdges(box), side.Bounds);
                box.Location = new Point(room.Left - box.Width / 3, box.Top);
                bool leftEdge = whole();
                Rectangle leftAt = Rectangle.Union(Mainframe.SeenEdges(box), side.Bounds);
                box.Location = new Point(box.Left, room.Bottom - box.Height * 2 / 3);
                bool bottomEdge = whole();
                Rectangle bottomAt = Rectangle.Union(Mainframe.SeenEdges(box), side.Bounds);
                if (!rightEdge || !leftEdge || !bottomEdge)
                    throw new Exception("The pair left its screen (" + room + "): at the right " + rightEdge + " " + rightAt + ", at the left " + leftEdge + " " + leftAt + ", at the bottom " + bottomEdge + " " + bottomAt);
                box.Location = new Point(room.Left + 24, room.Top + 24);
                glued("back in the corner");

                // Many moves and resizes either way: still flush, the glue never nests, and the side moves the box only
                // when it is dragged, once each.
                int movesWas = mainframe.StreamingBoxMoves;
                for (int i = 0; i < 20; i++)
                {
                    box.Location = new Point(box.Left + (i % 2 == 0 ? 11 : -7), box.Top + (i % 3 == 0 ? 5 : -3));
                    box.ClientSize = new Size(box.ClientSize.Width + (i % 2 == 0 ? 6 : -6), box.ClientSize.Height + (i % 2 == 0 ? 4 : -4));
                    side.Location = new Point(side.Left + (i % 2 == 0 ? -9 : 9), side.Top + (i % 2 == 0 ? 4 : -4));
                    side.Location = side.Location;
                    glued("round " + (i + 1));
                }
                int deepest = mainframe.StreamingDeepest, moves = mainframe.StreamingBoxMoves - movesWas;
                if (unglued.Count > 0 || deepest != 1 || moves != 20)
                    throw new Exception("The glue looped or slipped over 20 rounds: nested " + deepest + " deep, the side moved the box " + moves + " times for 20 drags; " + string.Join("; ", unglued.ToArray()));

                // Closed and opened again five times each way, by the toggle and by the box's own close: one box, one side.
                for (int i = 0; i < 5; i++)
                {
                    SetStreamWindow();
                    bool shut = (streamWindow == null || streamWindow.IsDisposed) && !mainframe.StreamingOpen;
                    SetStreamWindow();
                    bool opened = streamWindow != null && !streamWindow.IsDisposed && mainframe.StreamingOpen && mainframe.StreamingSide.Owner == streamWindow;
                    streamWindow.Close();
                    bool boxShut = streamWindow.IsDisposed && !mainframe.StreamingOpen;
                    streamOpenItem.PerformClick();
                    bool back2 = streamWindow != null && !streamWindow.IsDisposed && mainframe.StreamingOpen && mainframe.StreamingSide.Owner == streamWindow;
                    if (!shut || !opened || !boxShut || !back2)
                        throw new Exception("Round " + (i + 1) + " of closing and opening went wrong: shut by the toggle " + shut + ", opened " + opened + ", shut by the box " + boxShut + ", opened again " + back2);
                }
                box = streamWindow;
                side = mainframe.StreamingSide;

                // What a Window Capture takes is the box's own picture: a tall stand-in on green, and the side nowhere in it.
                backdropColour = Color.FromArgb(0, 177, 64);
                box.BackColor = backdropColour;
                Clear(false);
                streamWindow.Forget();
                using (Bitmap stand = new Bitmap(20, 200))
                {
                    using (Graphics paint = Graphics.FromImage(stand)) paint.Clear(Color.FromArgb(255, 0, 255));
                    streamWindow.Took(stand, new Rectangle(0, 0, 20, 200), null);
                }
                box.Refresh();
                int sideless = 0, wrong = 0;
                using (Bitmap took = new Bitmap(box.ClientSize.Width, box.ClientSize.Height))
                {
                    using (Graphics into = Graphics.FromImage(took))
                    {
                        IntPtr dc = into.GetHdc();
                        try { PrintWindow(box.Handle, dc, 1); }
                        finally { into.ReleaseHdc(dc); }
                    }
                    took.Save(System.IO.Path.Combine(output, "streaming-box.png"), System.Drawing.Imaging.ImageFormat.Png);
                    for (int y = 0; y < took.Height; y++)
                        foreach (int x in new[] { 0, 1, 2, took.Width - 3, took.Width - 2, took.Width - 1 })
                        {
                            Color one = took.GetPixel(x, y);
                            if (Math.Abs(one.R - 0) > 8 || Math.Abs(one.G - 177) > 8 || Math.Abs(one.B - 64) > 8) wrong++;
                            else sideless++;
                        }
                }
                if (wrong > 0 || sideless == 0)
                    throw new Exception("The box's own picture holds more than her on the colour at its edges: " + wrong + " other pixels (streaming-box.png)");

                streamLocked = false;
                mainframe.PressStreaming("Lock her in place");
                bool locked = streamLocked;
                mainframe.PressStreaming("Transparent");
                bool clear = streamClear && streamWindow == box && !box.IsDisposed && streamWindow.Clear;
                mainframe.PressStreaming("Green screen");
                bool coloured = !streamClear && backdropColour.ToArgb() == Color.FromArgb(0, 177, 64).ToArgb();
                string after = mainframe.StreamingText ?? "";
                bool inStep = after.Contains("Green screen on") && after.Contains("Transparent off") && after.Contains("Capture window on");
                string crowded = mainframe.StreamingCrowded;
                // The whole window as it shows: the frame's picture with her capture window's own in its opening.
                string windowPicture = System.IO.Path.Combine(output, "streaming-window.png");
                streamWindow.Forget();
                RenderFrame();
                box.Refresh();
                mainframe.PictureStreaming(windowPicture);
                try
                {
                    using (Bitmap framePicture = new Bitmap(windowPicture))
                    using (Bitmap joined = new Bitmap(framePicture))
                    using (Bitmap boxPicture = new Bitmap(box.Width, box.Height))
                    {
                        using (Graphics into = Graphics.FromImage(boxPicture))
                        {
                            IntPtr dc = into.GetHdc();
                            try { PrintWindow(box.Handle, dc, 1); }
                            finally { into.ReleaseHdc(dc); }
                        }
                        using (Graphics into = Graphics.FromImage(joined)) into.DrawImageUnscaled(boxPicture, box.Left - side.Left, box.Top - side.Top);
                        framePicture.Dispose();
                        joined.Save(windowPicture, System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                catch (Exception caughtOne) { Brain.Caught(caughtOne); }
                if (!locked || !clear || !coloured || !inStep || crowded != null)
                    throw new Exception("The Streaming window went wrong: Lock her in place " + locked + ", Transparent " + clear + ", a colour " + coloured + ", rows in step " + inStep + " (" + after + "), crowded " + crowded);

                // As on a PC with no Direct3D and no Spout plugin: Transparent falls back to the colour, and the pair still
                // closes and opens again with it picked.
                Func<bool> pluginWas = SpoutSender.PluginFound;
                ClearFrame.Refused = true;
                SpoutSender.PluginFound = delegate { return false; };
                try
                {
                    Func<bool> fellBack = delegate
                    {
                        using (Bitmap stand = new Bitmap(40, 40)) streamWindow.Took(stand, new Rectangle(0, 0, 40, 40), null);
                        streamWindow.Refresh();
                        return !streamWindow.IsDisposed && !streamWindow.Clear && streamWindow.Spout == null && mainframe.StreamingOpen;
                    };
                    mainframe.PressStreaming("Transparent");
                    bool first = streamClear && fellBack();
                    SetStreamWindow();
                    bool shut = (streamWindow == null || streamWindow.IsDisposed) && !mainframe.StreamingOpen;
                    SetStreamWindow();
                    bool again = streamWindow != null && fellBack();
                    if (!first || !shut || !again)
                        throw new Exception("With no Direct3D and no Spout, Transparent went wrong: on the colour " + first + ", closed " + shut + ", opened again on the colour " + again);
                }
                finally
                {
                    ClearFrame.Refused = false;
                    SpoutSender.PluginFound = pluginWas;
                }
                mainframe.PressStreaming("Green screen");
                box = streamWindow;
                side = mainframe.StreamingSide;

                // Closing the side closes the box, and the Capture window row off closes both.
                side.Close();
                bool bothShut = (streamWindow == null || streamWindow.IsDisposed) && !mainframe.StreamingOpen;
                streamOpenItem.PerformClick();
                mainframe.PressStreaming("Capture window");
                bool rowShut = (streamWindow == null || streamWindow.IsDisposed) && !mainframe.StreamingOpen;
                if (!bothShut || !rowShut)
                    throw new Exception("The box and its side didn't close together: the side closed " + bothShut + ", Capture window off " + rowShut);
                checks.Add("PASS her right-click Streaming opens Capri Stream, one frame round the capture window and its options: every option of her Streaming menu laid out with no folds, the box held in its opening through a move, a resize, a drag of the frame and a minimise, the pair kept whole on its screen at each edge, and through 20 rounds of each with the glue never nesting; five rounds of closing and opening by the toggle and by the box itself; with no Direct3D and no Spout, Transparent on the colour; the box's picture holding only her on the colour, Lock her in place, Transparent and a colour pressed there doing what the menu does, and closing either closing both (pictures streaming-window.png, streaming-box.png)");
            }
            finally
            {
                mainframe.CloseStreaming();
                if (streamWindow != null && !streamWindow.IsDisposed) SetStreamWindow();
                backdropColour = colourWas;
                Clear(clearWas);
                streamLocked = lockedWas;
                StreamLabel();
            }
        }
#endif
    }
}
