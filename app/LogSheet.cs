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
    internal sealed class LogSheet : Form, LookWindow
    {
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
            follow.Click += delegate { following = !following; Marked(); if (following) Bottom(); };
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
            bar.Taken = delegate { if (following) { following = false; Marked(); } };
            // And the wheel, which is what anybody reaches for first. Rolling back up holds the view;
            // rolling to the bottom picks the follow up again, so it behaves like it looks.
            box.MouseWheel += delegate(object who, MouseEventArgs rolled)
            {
                if (rolled.Delta < 0) return;
                if (following) { following = false; Marked(); }
            };
            Controls.Add(bar);

            KeyDown += delegate(object sender, KeyEventArgs key) { if (key.KeyCode == Keys.Escape) Close(); };
            beat.Interval = 700;
            beat.Tick += delegate { Fresh(); };
            ResumeLayout();
            Marked();
            Load += delegate { Open(0); beat.Start(); };
        }

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
            Marked();
            Fresh();
        }

        private void Marked()
        {
            for (int one = 0; one < picks.Length; one++)
                if (picks[one] != null) picks[one].ForeColor = one == which ? Neon.Rose : Neon.Cyan;
            string named = watching == null || which >= watching.Length ? "" : Path.GetFileName(watching[which]);
            note.Text = named + (following ? "   ·   following" : "   ·   held") + "   ·   output only, nothing typed here reaches her";
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
            catch (Exception) { }
            bar.Sync();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            beat.Stop();
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
