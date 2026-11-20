using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace CapriDesktop
{
    // What she is writing down, while she writes it.
    //
    // Her faults go to errors.txt and the lines she builds go to lines-log.txt, and both are read after the
    // fact, by somebody who already noticed something was wrong and went looking. A window that shows them
    // arriving turns that round: the odd line is on screen the moment she says it, with who she was at the
    // time beside it.
    //
    // Output only, deliberately. There is nothing to type into it and nothing it can do to her: it opens the
    // files she is already writing, reads what has been added since it last looked, and shows it.
    internal sealed class LogSheet : Form, LookWindow, IMessageFilter
    {
        // The wheel goes to whatever has focus, so a pointer resting on the log without a click first scrolled
        // nothing. Every wheel turn is looked at before it is delivered, and one over the log or its bar scrolls
        // the log, the way a web page scrolls under the pointer.
        private const int WheelTurned = 0x020A, LineScroll = 0x00B6;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wide, IntPtr tall);

        public bool PreFilterMessage(ref Message message)
        {
            if (message.Msg != WheelTurned || IsDisposed || !Visible || !box.IsHandleCreated) return false;
            Point at = Cursor.Position;
            if (!box.RectangleToScreen(box.ClientRectangle).Contains(at) && !(bar.Visible && bar.RectangleToScreen(bar.ClientRectangle).Contains(at))) return false;
            int turned = (short)((message.WParam.ToInt64() >> 16) & 0xFFFF);
            SendMessage(box.Handle, LineScroll, IntPtr.Zero, new IntPtr(turned > 0 ? -3 : 3));
            bar.Sync();
            Moved();
            return true;
        }

        // What her key says in Mainframe's BRAIN band, kept here so the window names its own way in.
        internal const string KeyText = "LOG";
        internal const int Wide = 760, Tall = 470;
        internal readonly float Zoom;

        private readonly TextBox box = new TextBox();
        private readonly SlimBar bar = new SlimBar();
        private readonly System.Windows.Forms.Timer beat = new System.Windows.Forms.Timer();
        private readonly Button[] picks = new Button[2];
        private readonly Label note = new Label();

        private string[] watching;
        private int which;
        private long read;
        private bool following = true;
        private readonly StringBuilder pending = new StringBuilder();
        private int waiting;

        internal LogSheet(Form owner, float zoom, string[] files)
        {
            SuspendLayout();
            Zoom = zoom;
            watching = files;
            Text = "Her log";
            if (owner != null) Icon = owner.Icon;
            ShowInTaskbar = false;
            TopMost = true;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Neon.Ground;
            ForeColor = Neon.White;
            DoubleBuffered = true;
            KeyPreview = true;
            ClientSize = new Size(At(Wide), At(Tall));

            Label title = new Label
            {
                Text = "HER LOG >",
                Font = Neon.Display(15f * zoom),
                ForeColor = Neon.Rose,
                BackColor = Neon.Ground,
                AutoSize = true,
                Location = new Point(At(24), At(15))
            };
            Controls.Add(title);

            picks[0] = Pick("What she said", 24, 52, 0);
            picks[1] = Pick("What went wrong", 150, 52, 1);
            Button follow = Pick("Follow", 290, 52, -1);
            follow.Click += delegate { if (following) { following = false; Marked(); } else Resume(); };
            Button shut = Pick("Close", Wide - 92, 52, -2);
            shut.Click += delegate { Close(); };

            note.Text = "";
            note.Font = Look.Mono(8.5f * zoom, FontStyle.Regular, GraphicsUnit.Point);
            note.ForeColor = Neon.Faint;
            note.BackColor = Neon.Ground;
            note.AutoSize = false;
            note.Bounds = new Rectangle(At(24), At(Tall - 30), At(Wide - 48), At(18));
            Controls.Add(note);

            box.Multiline = true;
            box.ReadOnly = true;
            // Wrapped, because there is no sideways scrolling here and her log lines run to 300
            // characters. Without it everything past the width of the box could not be read at all.
            box.WordWrap = true;
            box.ScrollBars = ScrollBars.None;
            box.BorderStyle = BorderStyle.None;
            box.BackColor = Neon.Field;
            box.ForeColor = Neon.White;
            box.Font = Look.Mono(9f * zoom, FontStyle.Regular, GraphicsUnit.Point);
            box.MaxLength = 0;
            box.AccessibleName = "Her log, output only";
            box.Bounds = new Rectangle(At(24), At(86), At(Wide - 48) - At(SlimBar.Thin) - At(6), At(Tall - 126));
            Controls.Add(box);

            bar.Bounds = new Rectangle(box.Right + At(6), box.Top, At(SlimBar.Thin), box.Height);
            bar.BackColor = Neon.Ground;
            bar.Watch(box);
            // Moving it by hand stops the follow, the way every log window works. Otherwise the next line
            // she writes pulls the view back to the bottom and it reads as a log that will not scroll.
            bar.Taken = Moved;
            // The wheel is handled in PreFilterMessage above, focused or not. Rolling back up holds the view;
            // rolling to the bottom picks the follow up again, so it behaves like it looks.
            box.KeyUp += delegate { Moved(); };
            Controls.Add(bar);

            KeyDown += delegate(object sender, KeyEventArgs key) { if (key.KeyCode == Keys.Escape) Close(); };
            beat.Interval = 700;
            beat.Tick += delegate { Fresh(); };
            ResumeLayout();
            Marked();
            // Kept between opens: closed by them it hides, stops reading its files while hidden, and opens again on
            // the file it showed, read fresh from its end.
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (e.CloseReason != CloseReason.UserClosing || Kept == false) return;
                e.Cancel = true;
                Hide();
            };
            // Every showing, the first included: what it reads starts here, and stops while it is hidden.
            VisibleChanged += delegate
            {
                if (Visible) { Open(which); beat.Start(); Application.RemoveMessageFilter(this); Application.AddMessageFilter(this); }
                else { beat.Stop(); Application.RemoveMessageFilter(this); }
            };
        }

        // False once she is quitting, so a close then is a real one.
        internal bool Kept = true;

        private int At(int value) { return (int)Math.Round(value * Zoom); }

        private Button Pick(string label, int x, int y, int source)
        {
            Button made = new Button
            {
                Text = label,
                FlatStyle = FlatStyle.Flat,
                BackColor = Neon.Well,
                ForeColor = Neon.Cyan,
                Font = Look.Mono(8.5f * Zoom, FontStyle.Regular, GraphicsUnit.Point),
                Bounds = new Rectangle(At(x), At(y), At(label.Length * 8 + 26), At(22)),
                TabStop = true,
                AccessibleName = label
            };
            made.FlatAppearance.BorderColor = Neon.Edge;
            if (source >= 0) { int one = source; made.Click += delegate { Open(one); }; }
            Controls.Add(made);
            return made;
        }

        // Switch to one of the files and read it from the end: the last screenful is what anybody wants, not
        // the start of a file that may be months old.
        private void Open(int source)
        {
            which = source < 0 || watching == null || source >= watching.Length ? 0 : source;
            read = 0;
            box.Text = "";
            // A file just switched to is read from its end, so it follows; nothing held from the other one carries over.
            following = true;
            pending.Length = 0;
            waiting = 0;
            Marked();
            Fresh();
        }

        private void Marked()
        {
            for (int one = 0; one < picks.Length; one++)
                if (picks[one] != null) picks[one].ForeColor = one == which ? Neon.Rose : Neon.Cyan;
            string named = watching == null || which >= watching.Length ? "" : Path.GetFileName(watching[which]);
            string held = waiting == 0 ? "   ·   held" : "   ·   held, " + waiting + (waiting == 1 ? " new line" : " new lines") + " below";
            note.Text = named + (following ? "   ·   following" : held) + "   ·   output only, nothing typed here reaches her";
        }

        // The view was moved by hand. Anywhere above the bottom holds it; the bottom follows again.
        private void Moved()
        {
            if (IsDisposed) return;
            if (bar.AtEnd) { if (!following) Resume(); return; }
            if (following) { following = false; Marked(); }
        }

        // Back to following: what arrived while the view was held goes in, and the view goes to the end.
        private void Resume()
        {
            following = true;
            string held = pending.ToString();
            pending.Length = 0;
            waiting = 0;
            Marked();
            if (held.Length > 0) Add(held);
            else Bottom();
        }

        // What has been added since the last look. A file that got shorter was rewritten under us, so it is
        // read again from the end rather than from wherever the old position happened to land.
        private void Fresh()
        {
            if (watching == null || which >= watching.Length) return;
            string path = watching[which];
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) { if (box.TextLength == 0) box.Text = "Nothing written yet."; return; }
                using (FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long end = file.Length;
                    if (end < read) { read = 0; box.Text = ""; }
                    if (read == 0 && end > 60000) read = end - 60000;
                    if (end == read) return;
                    file.Seek(read, SeekOrigin.Begin);
                    byte[] bytes = new byte[end - read];
                    int got = file.Read(bytes, 0, bytes.Length);
                    read = end;
                    string said = new UTF8Encoding(false).GetString(bytes, 0, got);
                    if (said.Length == 0) return;
                    Add(said);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private void Add(string said)
        {
            // Held, new lines wait rather than going in. Putting them in means replacing the box's text, and
            // that sends the view back to the top: every line she wrote yanked the view away from where it
            // had been scrolled to, which is what made the log read as one that could not be scrolled.
            if (!following)
            {
                pending.Append(said);
                foreach (char one in said) if (one == '\n') waiting++;
                Marked();
                return;
            }
            string whole = box.Text + said.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
            // Enough to scroll back through and no more: this is a window on what is happening, not the file.
            string[] rows = whole.Split('\n');
            if (rows.Length > 500) whole = string.Join("\n", rows, rows.Length - 500, 500);
            box.Text = whole;
            if (following) Bottom();
            bar.Sync();
        }

        private void Bottom()
        {
            try
            {
                box.SelectionStart = box.TextLength;
                box.ScrollToCaret();
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            bar.Sync();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            beat.Stop();
            Application.RemoveMessageFilter(this);
            base.OnFormClosed(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen edge = new Pen(Neon.Edge, Math.Max(1, Zoom)))
                e.Graphics.DrawRectangle(edge, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        // Dragged by its own background, like her other windows.
        protected override void WndProc(ref Message message)
        {
            const int WmNcHitTest = 0x0084, Caption = 2;
            base.WndProc(ref message);
            if (message.Msg == WmNcHitTest && message.Result.ToInt32() == 1) message.Result = new IntPtr(Caption);
        }
    }
}
