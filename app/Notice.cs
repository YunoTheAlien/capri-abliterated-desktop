using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Media;
using System.Reflection;
using System.Windows.Forms;

namespace CapriDesktop
{
    // A note from Capri or her installer in her look, instead of Windows' grey box: its title in rose, or hot red when
    // something went wrong, the message under it and OK. Enter or Esc closes it, and Ctrl + C copies it like Windows' own.
    internal sealed class Notice : Form, LookWindow
    {
        private const int Wide = 440;
        private readonly string caption;
        private readonly string message;
        private readonly Label title;
        private readonly Label body;
        private readonly Plate ok;

        // Shows a notice and waits for OK. If her own window can't show, Windows' box says it instead.
        internal static void Tell(IWin32Window owner, string message, string caption, bool error)
        {
            try
            {
                using (Notice notice = new Notice(message, caption, error))
                {
                    if (owner == null) { notice.ShowInTaskbar = true; notice.StartPosition = FormStartPosition.CenterScreen; }
                    if (error) SystemSounds.Hand.Play(); else SystemSounds.Asterisk.Play();
                    notice.ShowDialog(owner);
                }
            }
            catch (Exception)
            {
                MessageBoxIcon icon = error ? MessageBoxIcon.Error : MessageBoxIcon.Information;
                if (owner == null) MessageBox.Show(message, caption, MessageBoxButtons.OK, icon);
                else MessageBox.Show(owner, message, caption, MessageBoxButtons.OK, icon);
            }
        }

        internal Notice(string message, string caption, bool error)
        {
            this.message = message;
            this.caption = caption;
            Text = caption;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Neon.Ground;
            ForeColor = Neon.White;
            DoubleBuffered = true;
            float s = DeviceDpi / 96f;
            Font = Neon.Type(10.5f);
            using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("Capri.Chip.ico")) if (source != null) Icon = new Icon(source);
            int wide = Px(Wide, s);
            int inset = Px(26, s);
            title = new Neon.Caption();
            title.Text = caption.ToUpperInvariant();
            title.Font = Neon.Display(15f);
            title.ForeColor = error ? Neon.Hot : Neon.Rose;
            title.AutoSize = true;
            title.Location = new Point(inset, Px(18, s));
            Controls.Add(title);
            body = new Neon.Caption();
            body.Text = message;
            body.Font = Font;
            body.ForeColor = Neon.White;
            body.MaximumSize = new Size(wide - 2 * inset, 0);
            body.AutoSize = true;
            body.Location = new Point(inset, title.Bottom + Px(12, s));
            Controls.Add(body);
            ok = new Plate();
            ok.Text = "OK";
            ok.Accent = Neon.Cyan;
            ok.Font = Neon.Type(10f, true);
            ok.Size = new Size(Px(96, s), Px(30, s));
            ok.Location = new Point(wide - inset - ok.Width, body.Bottom + Px(22, s));
            ok.DialogResult = DialogResult.OK;
            Controls.Add(ok);
            AcceptButton = ok;
            CancelButton = ok;
            ClientSize = new Size(wide, ok.Bottom + Px(22, s));
            Neon.Clip(this, 16 * s, 6 * s);
        }

        private static int Px(float value, float s) { return (int)Math.Round(value * s); }

        internal string Said { get { return title.Text + "\n" + body.Text; } }

        internal bool Fits { get { return body.PreferredSize.Height <= body.Height && body.Right <= ClientSize.Width && ok.Bottom <= ClientSize.Height && ok.Top >= body.Bottom; } }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ok.Focus();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.C)
            {
                try { Clipboard.SetText(caption + Environment.NewLine + Environment.NewLine + message); } catch (System.Runtime.InteropServices.ExternalException) { }
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        // It drags from anywhere that isn't the button.
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == 0x0084 && message.Result == new IntPtr(1)) message.Result = new IntPtr(2);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { Neon.Backdrop(e.Graphics, ClientSize, 210 * DeviceDpi / 96f); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            float s = DeviceDpi / 96f;
            Neon.Frame(e.Graphics, ClientSize, s, 16 * s, 6 * s, 52 * s, 26 * s);
        }
    }

    // Markup a message to her question may carry, at a line's start: [[H]] [[M]] [[L]] a row in that group, [[h]] [[m]]
    // [[l]] its heading, [[c]] the counts line, [[f+]] [[f-]] a fold's handle open or shut, [[e]] the fold's end.
    internal static partial class Marked
    {
        // Each group's colour: her Night City look has its own red, amber and cyan. Black, White and Grey are grey all
        // through, so there severity takes its own red, amber and teal, chosen to read on that look's ground.
        internal static Color Severity(char group)
        {
            Color high = Neon.Hot, medium = Neon.Amber, low = Neon.Cyan;
            if (high.GetSaturation() <= 0.3f || medium.GetSaturation() <= 0.3f || low.GetSaturation() <= 0.3f)
            {
                float light = Neon.Ground.GetBrightness();
                if (light > 0.7f) { high = Color.FromArgb(0xC6, 0x28, 0x28); medium = Color.FromArgb(0xA8, 0x5A, 0x00); low = Color.FromArgb(0x00, 0x7C, 0x89); }
                else if (light > 0.2f) { high = Color.FromArgb(0xFF, 0x9E, 0x9E); medium = Color.FromArgb(0xFF, 0xE0, 0x82); low = Color.FromArgb(0x84, 0xFF, 0xFF); }
                else { high = Color.FromArgb(0xFF, 0x5C, 0x5C); medium = Color.FromArgb(0xFF, 0xD5, 0x4F); low = Color.FromArgb(0x4D, 0xD0, 0xE1); }
            }
            return group == 'H' ? high : group == 'M' ? medium : low;
        }
    }

    // A yes-or-no question in her notice's look: hot red title behind a warning mark, the message, Yes and No. No is
    // picked, so Enter, Esc, close and Alt+F4 answer No, and Y or N work as in Windows' box. A tall message scrolls.
    internal sealed class Question : Form, LookWindow
    {
        private const int Wide = 440;
        private readonly string caption;
        private readonly string message;
        private readonly float s;
        private readonly Font words;
        private readonly Font heading;
        private readonly Font keys;
        private readonly Label title;
        private readonly Lines body;
        private readonly Plate yes;
        private readonly Plate no;
        private readonly Plate close;

        // Asks and waits. Only Yes returns true. If her own window can't show, Windows' box asks instead, with No picked.
        internal static bool Ask(IWin32Window owner, string message, string caption)
        {
            DialogResult answer = DialogResult.None;
            try
            {
                Control near = owner as Control;
                using (Question question = new Question(message, caption, near != null && near.IsHandleCreated ? near.DeviceDpi / 96f : 0))
                {
                    question.Place(owner);
                    SystemSounds.Exclamation.Play();
                    answer = question.ShowDialog(owner);
                }
            }
            catch (Exception)
            {
                if (answer == DialogResult.None)
                {
                    message = Marked.Plain(message);
                    if (owner == null) answer = MessageBox.Show(message, caption, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                    else answer = MessageBox.Show(owner, message, caption, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                }
            }
            return answer == DialogResult.Yes;
        }

        // Scale 0 takes her own screen's; the checks pass 1.25 or 1.5 to see her at other sizes on this one.
        internal Question(string message, string caption, float scale)
        {
            this.message = message;
            this.caption = caption;
            Text = caption;
            AccessibleName = caption;
            AccessibleRole = AccessibleRole.Dialog;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Neon.Ground;
            ForeColor = Neon.White;
            DoubleBuffered = true;
            s = scale > 0 ? scale : DeviceDpi / 96f;
            words = Sized(Neon.Type(10.5f));
            heading = Sized(Neon.Display(15f));
            keys = Sized(Neon.Type(10f, true));
            Font = words;
            using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("Capri.Chip.ico")) if (source != null) Icon = new Icon(source);
            title = new Neon.Caption();
            title.Text = caption.ToUpperInvariant();
            title.AccessibleName = caption;
            title.Font = heading;
            title.ForeColor = Neon.Hot;
            title.AutoSize = true;
            Controls.Add(title);
            body = new Lines(message, words, s);
            Controls.Add(body);
            yes = Key("Yes", Neon.Hot, DialogResult.Yes);
            no = Key("No", Neon.Cyan, DialogResult.No);
            close = new Plate();
            close.Glyph = 2;
            close.AccessibleName = "Close";
            close.TabStop = false;
            close.DialogResult = DialogResult.No;
            Controls.Add(close);
            AcceptButton = no;
            CancelButton = no;
            ActiveControl = no;
            Lay(Displays.Home.WorkingArea, Rectangle.Empty);
        }

        private Plate Key(string name, Color accent, DialogResult result)
        {
            Plate key = new Plate();
            key.Text = name;
            key.AccessibleName = name;
            key.Accent = accent;
            key.Font = keys;
            key.DialogResult = result;
            // Windows can hide focus marks again as the box activates, after its own mouse use, so the keys keep theirs.
            key.Cued = true;
            key.TabIndex = Controls.Count;
            Controls.Add(key);
            return key;
        }

        // Her fonts at their point sizes, held in pixels so the box can be drawn at a scale other than this screen's.
        private Font Sized(Font font)
        {
            using (font) return new Font(font.FontFamily, font.SizeInPoints * 96f / 72f * s, font.Style, GraphicsUnit.Pixel);
        }

        private int Px(float value) { return (int)Math.Round(value * s); }

        // Sizes the box to its words inside `area`, the working area of the screen it shows on, and centres it on `over`
        // (or on the area), never past the area's edges. A message too tall for the area scrolls, and the keys stay below it.
        internal void Lay(Rectangle area, Rectangle over)
        {
            int inset = Px(26), margin = Px(8);
            int wide = Math.Min(Px(Wide), Math.Max(Px(240), area.Width - 2 * margin));
            close.Bounds = new Rectangle(wide - Px(18) - Px(26), Px(16), Px(26), Px(22));
            int mark = Px(24);
            title.MaximumSize = new Size(Math.Max(1, close.Left - Px(10) - inset - mark), 0);
            title.Location = new Point(inset + mark, Px(18));
            int top = Math.Max(title.Bottom, close.Bottom) + Px(12);
            int keyWide = Px(96), keyTall = Px(30);
            int room = Math.Max(words.Height * 3, area.Height - 2 * margin - top - Px(22) - keyTall - Px(22));
            int wrap = wide - 2 * inset;
            int tall = body.Measure(wrap);
            if (tall > room)
            {
                wrap -= Px(14);
                tall = body.Measure(wrap);
            }
            body.Bounds = new Rectangle(inset, top, wide - 2 * inset, Math.Min(tall, room));
            body.Fill(wrap, tall);
            no.Bounds = new Rectangle(wide - inset - keyWide, body.Bottom + Px(22), keyWide, keyTall);
            yes.Bounds = new Rectangle(no.Left - Px(10) - keyWide, no.Top, keyWide, keyTall);
            ClientSize = new Size(wide, no.Bottom + Px(22));
            Rectangle centre = over.IsEmpty ? area : over;
            Location = new Point(Math.Max(area.Left, Math.Min(area.Right - Width, centre.Left + (centre.Width - Width) / 2)), Math.Max(area.Top, Math.Min(area.Bottom - Height, centre.Top + (centre.Height - Height) / 2)));
            Neon.Clip(this, 16 * s, 6 * s);
            Invalidate(true);
        }

        // On the screen her owner is on, centred on it, and top-most when it is. With no owner, on the screen under the mouse.
        internal void Place(IWin32Window owner)
        {
            IntPtr handle = owner == null ? IntPtr.Zero : owner.Handle;
            Rectangle over = Rectangle.Empty;
            Form form = owner as Form;
            if (form != null && form.Visible && form.WindowState != FormWindowState.Minimized) over = form.Bounds;
            Screen screen = handle != IntPtr.Zero ? Screen.FromHandle(handle) : Screen.FromPoint(Cursor.Position);
            if (handle == IntPtr.Zero) ShowInTaskbar = true;
            else TopMost = (Native.GetWindowLong(handle, -20) & 8) != 0;
            Lay(screen.WorkingArea, over);
        }

        internal Button YesKey { get { return yes; } }
        internal Button NoKey { get { return no; } }
        internal Button CloseKey { get { return close; } }
        internal bool Scrolls { get { return body.Content > body.Height; } }
        internal int ScrolledTo { get { return body.Offset; } }
        internal int Unseen { get { return Math.Max(0, body.Content - body.Height); } }
        internal string Said { get { return title.Text + "\n" + body.Text; } }
        internal bool FoldOpen { get { return body.Open; } }
        internal bool Folds { get { return body.Folds; } }

        // The fold opened or shut: laid out again where it stands, never past her working area.
        internal void Refold()
        {
            if (!body.Folds) return;
            body.Open = !body.Open;
            Point at = Location;
            Rectangle area = Screen.FromControl(this).WorkingArea;
            Lay(area, Rectangle.Empty);
            Location = new Point(Math.Max(area.Left, Math.Min(area.Right - Width, at.X)), Math.Max(area.Top, Math.Min(area.Bottom - Height, at.Y)));
        }

        // The handle on screen, for the click and the hand cursor.
        private bool OnHandle(Point client)
        {
            if (!body.Folds || body.Handle.IsEmpty) return false;
            Rectangle handle = new Rectangle(body.Left + body.Handle.Left, body.Top + body.Handle.Top - body.Offset, body.Handle.Width, body.Handle.Height);
            return handle.Contains(client) && body.Bounds.Contains(client);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && OnHandle(e.Location)) Refold();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = OnHandle(e.Location) ? Cursors.Hand : Cursors.Default;
        }

        // Nothing clipped: the title fits beside the close key, every line of the message fits its width and is in the box
        // or reachable by scrolling, and both keys sit whole under it, side by side.
        internal bool Fits
        {
            get
            {
                if (title.PreferredSize.Width > title.Width || title.Height > heading.Height * 3 / 2 || title.Right > close.Left) return false;
                if (body.Measure(body.Wrap) != body.Content || body.Widest() > body.Wrap || body.Wrap > body.Width || body.Top < Math.Max(title.Bottom, close.Bottom)) return false;
                if (body.Content > body.Height && body.Wrap > body.Width - Px(10)) return false;
                if (!ClientRectangle.Contains(body.Bounds) || !ClientRectangle.Contains(close.Bounds)) return false;
                foreach (Plate key in new[] { yes, no })
                {
                    Size ink = TextRenderer.MeasureText(key.Text, key.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                    if (!ClientRectangle.Contains(key.Bounds) || key.Top < body.Bottom || ink.Width > key.Width - Px(12) || ink.Height > key.Height - Px(4)) return false;
                }
                return yes.Right < no.Left && yes.Top == no.Top;
            }
        }

        // Windows hides focus marks on a window opened with the mouse, which hid that No is picked. Her keys light their rim
        // when focused, so the marks are turned on: the default shows from the start and moves with Tab.
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Native.SendMessage(Handle, 0x0127, new IntPtr(0x10002), IntPtr.Zero);
            no.Focus();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult != DialogResult.Yes) DialogResult = DialogResult.No;
            base.OnFormClosing(e);
        }

        // A key held down from before she asked repeats into the box; Enter, Space, Y and N answer only to a fresh press.
        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            if ((message.Msg == 0x0100 || message.Msg == 0x0104) && (message.LParam.ToInt64() & 0x40000000) != 0 && (key == Keys.Enter || key == Keys.Space || key == Keys.Y || key == Keys.N)) return true;
            return base.ProcessCmdKey(ref message, keyData);
        }

        private bool Answer(char key)
        {
            if ((ModifierKeys & Keys.Control) != 0) return false;
            if (key == 'y' || key == 'Y') { yes.PerformClick(); return true; }
            if (key == 'n' || key == 'N') { no.PerformClick(); return true; }
            if ((key == 'd' || key == 'D') && body.Folds) { Refold(); return true; }
            return false;
        }

        protected override bool ProcessDialogChar(char key) { return Answer(key) || base.ProcessDialogChar(key); }

        protected override bool ProcessMnemonic(char key) { return Answer(key) || base.ProcessMnemonic(key); }

        protected override bool ProcessDialogKey(Keys key)
        {
            if (key == Keys.Escape) { no.PerformClick(); return true; }
            if (Scrolls)
            {
                if (key == Keys.PageDown) { body.ScrollBy(body.Height - words.Height); return true; }
                if (key == Keys.PageUp) { body.ScrollBy(words.Height - body.Height); return true; }
                if (key == Keys.End) { body.ScrollBy(body.Content); return true; }
                if (key == Keys.Home) { body.ScrollBy(-body.Content); return true; }
            }
            return base.ProcessDialogKey(key);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (Scrolls)
            {
                int lines = SystemInformation.MouseWheelScrollLines;
                body.ScrollBy(-e.Delta * (lines < 0 ? body.Height : Math.Max(1, lines) * words.Height) / 120);
            }
            base.OnMouseWheel(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.C)
            {
                try { Clipboard.SetText(caption + Environment.NewLine + Environment.NewLine + Marked.Plain(message)); } catch (System.Runtime.InteropServices.ExternalException) { }
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        // It drags from anywhere that isn't a key or the fold's handle.
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == 0x0084 && message.Result == new IntPtr(1))
            {
                Point at = PointToClient(new Point(unchecked((short)(message.LParam.ToInt64() & 0xFFFF)), unchecked((short)((message.LParam.ToInt64() >> 16) & 0xFFFF))));
                if (!OnHandle(at)) message.Result = new IntPtr(2);
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e) { Neon.Backdrop(e.Graphics, ClientSize, 210 * s); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;
            Neon.Frame(graphics, ClientSize, s, 16 * s, 6 * s, 52 * s, 26 * s);
            // The warning mark: a hot red triangle with a stroke and dot in it, level with the title's capitals.
            System.Drawing.Drawing2D.SmoothingMode was = graphics.SmoothingMode;
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float x = Px(26), y = title.Top + title.Height / 2f, half = 8 * s;
            using (Pen pen = new Pen(Neon.Hot, 1.6f * s))
            {
                pen.LineJoin = System.Drawing.Drawing2D.LineJoin.Miter;
                graphics.DrawPolygon(pen, new[] { new PointF(x + half, y - half), new PointF(x + 2 * half, y + 0.8f * half), new PointF(x, y + 0.8f * half) });
                graphics.DrawLine(pen, x + half, y - 0.25f * half, x + half, y + 0.25f * half);
            }
            using (Brush dot = new SolidBrush(Neon.Hot)) graphics.FillEllipse(dot, x + half - 1.1f * s, y + 0.42f * half, 2.2f * s, 2.2f * s);
            graphics.SmoothingMode = was;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing) return;
            words.Dispose();
            heading.Dispose();
            keys.Dispose();
        }

        // The message wrapped to its width and scrolled with the Mainframe's thin thumb when tall; the mouse passes
        // through to the box. A Marked message draws its rows in their group's colour and shape, a counts line and one
        // fold.
        private sealed class Lines : Control
        {
            private const TextFormatFlags Flow = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix;
            private readonly float s;
            private readonly List<Marked.Line> lines;
            internal int Wrap;
            internal int Content;
            internal int Offset;
            internal bool Open;
            // Where the fold's handle sits, in the message's own coordinates (before scrolling); empty when there is none.
            internal Rectangle Handle;

            internal Lines(string message, Font font, float scale)
            {
                s = scale;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                SetStyle(ControlStyles.Selectable, false);
                TabStop = false;
                lines = Marked.Parse(message);
                foreach (Marked.Line line in lines) if (line.Kind == 'f') Open = line.Open;
                Text = Marked.Plain(message);
                Font = font;
                AccessibleName = Text;
                AccessibleRole = AccessibleRole.StaticText;
            }

            internal bool Folds { get { return lines.Exists(line => line.Kind == 'f'); } }

            private int Indent { get { return (int)Math.Round(16 * s); } }

            // The lines that show: everything, less the fold's rows while it is shut.
            private IEnumerable<Marked.Line> Shown()
            {
                bool inside = false;
                foreach (Marked.Line line in lines)
                {
                    if (line.Kind == 'f') { inside = true; yield return line; continue; }
                    if (line.Kind == 'e') { inside = false; continue; }
                    if (inside && !Open) continue;
                    yield return line;
                }
            }

            private int Tall(Marked.Line line, int width)
            {
                string text = line.Text.Length == 0 ? " " : line.Text;
                bool row = char.IsUpper(line.Kind);
                int tall = TextRenderer.MeasureText(text, Font, new Size(Math.Max(1, width - (row || line.Kind == 'f' ? Indent : 0)), int.MaxValue), Flow).Height;
                return line.Kind == 'c' && line.Counts.Count > 0 ? tall + Font.Height : tall;
            }

            internal int Measure(int width)
            {
                int tall = 0;
                foreach (Marked.Line line in Shown()) tall += Tall(line, width);
                return tall;
            }

            internal int Widest()
            {
                int widest = 0;
                foreach (Marked.Line line in Shown())
                {
                    bool row = char.IsUpper(line.Kind) || line.Kind == 'f';
                    int width = TextRenderer.MeasureText(line.Text.Length == 0 ? " " : line.Text, Font, new Size(Math.Max(1, Wrap - (row ? Indent : 0)), int.MaxValue), Flow).Width + (row ? Indent : 0);
                    widest = Math.Max(widest, width);
                }
                return widest;
            }

            internal void Fill(int wrap, int content)
            {
                Wrap = wrap;
                Content = content;
                Offset = 0;
                Invalidate();
            }

            internal void ScrollBy(int rows)
            {
                Offset = Math.Max(0, Math.Min(Content - Height, Offset + rows));
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Neon.Behind(e.Graphics, this, 210 * s);
                Graphics graphics = e.Graphics;
                Color plain = Neon.White;
                int y = -Offset;
                Handle = Rectangle.Empty;
                foreach (Marked.Line line in Shown())
                {
                    int tall = Tall(line, Wrap);
                    char kind = line.Kind;
                    if (char.IsUpper(kind))
                    {
                        Shape(graphics, kind, new Rectangle(0, y, Indent, Font.Height));
                        TextRenderer.DrawText(graphics, line.Text, Font, new Rectangle(Indent, y, Wrap - Indent, tall), plain, Flow);
                    }
                    else if (kind == 'h' || kind == 'm' || kind == 'l')
                        TextRenderer.DrawText(graphics, line.Text, Font, new Rectangle(0, y, Wrap, tall), Marked.Severity(char.ToUpperInvariant(kind)), Flow);
                    else if (kind == 'f')
                    {
                        Handle = new Rectangle(0, y + Offset, Wrap, tall);
                        Fold(graphics, new Rectangle(0, y, Indent, Font.Height), Open);
                        TextRenderer.DrawText(graphics, line.Text, Font, new Rectangle(Indent, y, Wrap - Indent, tall), Neon.Cyan, Flow);
                    }
                    else if (kind == 'c')
                    {
                        int head = tall - (line.Counts.Count > 0 ? Font.Height : 0);
                        TextRenderer.DrawText(graphics, line.Text, Font, new Rectangle(0, y, Wrap, head), plain, Flow);
                        int x = 0;
                        for (int i = 0; i < line.Counts.Count; i++)
                        {
                            KeyValuePair<char, string> count = line.Counts[i];
                            Shape(graphics, count.Key, new Rectangle(x, y + head, Indent, Font.Height));
                            x += Indent;
                            string said = count.Value + (i < line.Counts.Count - 1 ? "," : "");
                            TextRenderer.DrawText(graphics, said, Font, new Point(x, y + head), plain, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                            x += TextRenderer.MeasureText(graphics, said + " ", Font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width;
                        }
                    }
                    else TextRenderer.DrawText(graphics, line.Text, Font, new Rectangle(0, y, Wrap, tall), plain, Flow);
                    y += tall;
                }
                if (Content > Height) WorkPaint.Rail(e.Graphics, new RectangleF(Width - 2 * s, 0, 2 * s, Height), Height, Content, Offset);
            }

            // A group's mark beside its row: a square for high, a diamond for medium, a circle for low, so the groups read
            // apart without colour too.
            private void Shape(Graphics graphics, char group, Rectangle room)
            {
                int dot = Math.Max(5, (int)Math.Round(7 * s));
                int left = room.Left + (int)Math.Round(2 * s), top = room.Top + (room.Height - dot) / 2;
                System.Drawing.Drawing2D.SmoothingMode was = graphics.SmoothingMode;
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (SolidBrush paint = new SolidBrush(Marked.Severity(group)))
                {
                    if (group == 'H') graphics.FillRectangle(paint, left, top, dot, dot);
                    else if (group == 'M') graphics.FillPolygon(paint, new[] { new Point(left + dot / 2, top - 1), new Point(left + dot + 1, top + dot / 2), new Point(left + dot / 2, top + dot + 1), new Point(left - 1, top + dot / 2) });
                    else graphics.FillEllipse(paint, left, top, dot, dot);
                }
                graphics.SmoothingMode = was;
            }

            // The handle's mark: a triangle pointing right while shut, down while open, in cyan.
            private void Fold(Graphics graphics, Rectangle room, bool open)
            {
                float x = room.Left + 3 * s, y = room.Top + room.Height / 2f, half = 4 * s;
                PointF[] mark = open ? new[] { new PointF(x, y - half / 2), new PointF(x + 2 * half, y - half / 2), new PointF(x + half, y + half) }
                    : new[] { new PointF(x + half / 2, y - half), new PointF(x + half / 2, y + half), new PointF(x + 2 * half, y) };
                System.Drawing.Drawing2D.SmoothingMode was = graphics.SmoothingMode;
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (SolidBrush paint = new SolidBrush(Neon.Cyan)) graphics.FillPolygon(paint, mark);
                graphics.SmoothingMode = was;
            }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == 0x0084) { message.Result = new IntPtr(-1); return; }
                base.WndProc(ref message);
            }
        }
    }
}
