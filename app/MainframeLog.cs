using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Her memory, Her GIFs and Her log are one window of three parts: a key per part in each one's title bar, the part
    // showing lit, another key opening that part in the same place, and Clear memory a key of its own after them.
    internal sealed partial class Mainframe
    {
        internal static readonly string[] MemoryParts = { "Her memory", "Her GIFs", "Her log" };
        // What her key says in Mainframe's BRAIN band, kept here so the log names its own way in.
        internal const string LogKeyText = "LOG";
        private LogPart logPart;
        // The file her log showed last, so it opens on that one again.
        private int logShowing;

        internal void ShowMemoryPart(int part, Form from)
        {
            Point at = from != null && !from.IsDisposed && from.Visible ? from.Location : Point.Empty;
            if (part == 0) OpenMemory(); else if (part == 1) OpenGifs(); else OpenLog();
            Form shown = part == 0 ? (Form)memorySheet : part == 1 ? (Form)gifSheet : logPart;
            if (shown != null && !shown.IsDisposed && at != Point.Empty) shown.Location = at;
            if (from != null && !from.IsDisposed && from != shown) from.Hide();
        }

        // Her log: the two files she is already writing, the lines she builds and what went wrong.
        internal void OpenLog()
        {
            if (logPart != null && !logPart.IsDisposed && logPart.Zoom != zoom) logPart.Close();
            if (logPart == null || logPart.IsDisposed) logPart = new LogPart(this, SheetZoom(LogPart.Wide, LogPart.Tall), LogFiles());
            if (!logPart.Visible) logPart.Show(this);
            logPart.Activate();
        }

        internal void CloseLog() { if (logPart != null && !logPart.IsDisposed) logPart.Close(); }

        private static string[] LogFiles() { return new[] { Path.Combine(Logs.DataOf(Brain.LogPath), "lines-log.txt"), Brain.LogPath }; }

        // What part of the Memory window shows, for the checks.
        internal string MemoryPartShown
        {
            get
            {
                Form[] parts = { memorySheet, gifSheet, logPart };
                for (int i = 0; i < parts.Length; i++) if (parts[i] != null && !parts[i].IsDisposed && parts[i].Visible) return MemoryParts[i];
                return null;
            }
        }

#if CHECKS
        internal Form LogPartForCheck() { return new LogPart(this, 1f, new string[0]); }

        internal Form LogPartShown { get { return logPart != null && !logPart.IsDisposed && logPart.Visible ? logPart : null; } }

        // The joined windows as used: Memory's parts one size in one place, one key row, no Close on the log; Personality
        // settings and Her topics switching in place. A picture of each part. Null when right.
        internal string CheckJoined(string output)
        {
            List<string> wrong = new List<string>();
            Func<Form, float> z0 = delegate(Form part) { return part.ClientSize.Width / (float)LogPart.Wide; };
            Func<Form, string> keys = delegate(Form part)
            {
                List<string> said = new List<string>();
                foreach (Control one in part.Controls) if (one is Key && one.Top < (int)Math.Round(50 * z0(part)) && one.Text.Length > 0) said.Add(one.Text + "@" + one.Left);
                said.Sort(StringComparer.Ordinal);
                return string.Join(",", said.ToArray());
            };
            try
            {
                OpenMemory();
                Application.DoEvents();
                Point at = memorySheet.Location;
                Size size = memorySheet.ClientSize;
                string row = keys(memorySheet);
                float z = memorySheet.Zoom;
                foreach (string want in new[] { "Her memory@" + (int)Math.Round(200 * z), "Her GIFs@" + (int)Math.Round(316 * z), "Her log@" + (int)Math.Round(432 * z), "Clear memory@" + (int)Math.Round(556 * z) })
                    if (row.IndexOf(want, StringComparison.Ordinal) < 0) wrong.Add("Her memory's key row lacks " + want + ": " + row);
                Companion.Snap(memorySheet, Path.Combine(output, "joined-memory.png"));
                for (int i = 1; i < MemoryParts.Length; i++)
                {
                    PressMemoryPart(MemoryParts[i]);
                    Application.DoEvents();
                    Form part = i == 1 ? (Form)gifSheet : logPart;
                    if (MemoryPartShown != MemoryParts[i]) wrong.Add(MemoryParts[i] + " didn't show from its key, " + MemoryPartShown + " did");
                    else
                    {
                        if (part.Location != at || part.ClientSize != size) wrong.Add(MemoryParts[i] + " isn't where Her memory was or its size: " + part.Location + " " + part.ClientSize);
                        if (keys(part) != row) wrong.Add(MemoryParts[i] + "'s key row differs: " + keys(part));
                        foreach (Control one in part.Controls) if (one is Key && one.Text == "Close") wrong.Add(MemoryParts[i] + " still has a Close key");
                        Companion.Snap(part, Path.Combine(output, "joined-" + (i == 1 ? "gifs" : "log") + ".png"));
                    }
                }
                PressMemoryPart(MemoryParts[0]);
                Application.DoEvents();
                if (MemoryPartShown != MemoryParts[0]) wrong.Add("Her memory didn't come back from its key");
                OpenPersonalitySettings();
                Application.DoEvents();
                personalitySheet.Pick("Sassy");
                personalitySheet.ShowPart(true);
                Application.DoEvents();
                if (!personalitySheet.TopicsShown || personalitySheet.Rows.Length == 0) wrong.Add("Topics showed nothing for Sassy: " + personalitySheet.Rows);
                if (personalitySheet.TopicFolds != "HER TOPICS + add | YOUR TOPICS + add") wrong.Add("the Topics page's folds: " + personalitySheet.TopicFolds);
                foreach (Control one in personalitySheet.Controls) if (one is Key && (one.Text == "Hear her" || one.Text == "Defaults" || one.Text == "Default all") && one.Visible) wrong.Add("the settings page's " + one.Text + " shows on Her topics");
                Companion.Snap(personalitySheet, Path.Combine(output, "joined-topics.png"));
                // Her baseline picked, as the window opens: the personality she is in now shows its topics and their Add.
                personalitySheet.Pick("");
                Application.DoEvents();
                if (personalitySheet.Rows.Length == 0 || personalitySheet.TopicFolds != "HER TOPICS + add | YOUR TOPICS + add") wrong.Add("Topics on her baseline showed nothing, or no Add: " + personalitySheet.TopicFolds);
                personalitySheet.ShowPart(false);
                if (personalitySheet.TopicsShown || personalitySheet.Rows.IndexOf("Flirt", StringComparison.OrdinalIgnoreCase) < 0) wrong.Add("Personality settings didn't come back: " + personalitySheet.Rows);
            }
            finally
            {
                if (memorySheet != null && !memorySheet.IsDisposed) memorySheet.Close();
                CloseGifs();
                CloseLog();
                ClosePersonalitySettings();
            }
            return wrong.Count == 0 ? null : string.Join("; ", wrong.ToArray());
        }

        // A part's key in the Memory window's title bar pressed, as they'd press it.
        internal void PressMemoryPart(string part)
        {
            Form[] parts = { memorySheet, gifSheet, logPart };
            foreach (Form from in parts)
            {
                if (from == null || from.IsDisposed || !from.Visible) continue;
                foreach (Control one in from.Controls)
                    if (one is Key && one.Text == part && one.Top < from.ClientSize.Height / 8) { ((Key)one).PerformClick(); return; }
            }
        }
#endif

        // Output only: what she is writing down as she writes it, with who she was beside each line. Nothing typed here
        // reaches her.
        private sealed class LogPart : ModSheet, IMessageFilter
        {
            internal const int Wide = 760, Tall = 700;
            // The wheel goes to whatever has focus, so a pointer resting on the log without a click scrolled nothing. A turn
            // over the log or its bar scrolls the log, the way a web page scrolls under the pointer.
            private const int WheelTurned = 0x020A, LineScroll = 0x00B6;

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wide, IntPtr tall);

            private readonly TextBox box = new TextBox();
            private readonly SlimBar bar = new SlimBar();
            private readonly Clock.Ticker beat = new Clock.Ticker("log beat");
            private readonly Key[] picks = new Key[2];
            private readonly Key follow;
            private readonly Label note;
            private readonly string[] watching;
            private int which;
            private long read;
            private bool following = true;
            private readonly StringBuilder pending = new StringBuilder();
            private int waiting;

            internal LogPart(Mainframe owner, float zoom, string[] files) : base(owner, zoom, "Her log", "HER LOG", Wide, Tall)
            {
                watching = files;
                which = Math.Max(0, Math.Min(files.Length - 1, owner.logShowing));
                Parts(MemoryParts, 2, delegate(int part) { Frame.ShowMemoryPart(part, this); }, "Clear memory", delegate { Frame.capri.ClearMemoryAsked(); });
                // Its tabs are keys on the content's left edge, as Her memory's row is.
                picks[0] = Make("What she said", 28, 78, 124, 26, delegate { Open(0); });
                picks[1] = Make("What went wrong", 158, 78, 124, 26, delegate { Open(1); });
                follow = Make("Follow", 288, 78, 124, 26, delegate { if (following) { following = false; Marked(); } else Resume(); });
                box.Multiline = true;
                box.ReadOnly = true;
                // Wrapped, because there is no sideways scrolling and her log lines run to 300 characters.
                box.WordWrap = true;
                box.ScrollBars = ScrollBars.None;
                box.BorderStyle = BorderStyle.None;
                box.BackColor = Field;
                box.ForeColor = White;
                box.Font = Look.Mono(9f * zoom, FontStyle.Regular, GraphicsUnit.Point);
                box.MaxLength = 0;
                box.AccessibleName = "Her log, output only";
                box.Bounds = Box(32, 120, 686, 504);
                Controls.Add(box);
                bar.Bounds = new Rectangle(Z(722), Z(120), Z(SlimBar.Thin), Z(504));
                bar.BackColor = Field;
                bar.Watch(box);
                // Moving it by hand stops the follow, the way every log window works.
                bar.Taken = Moved;
                box.KeyUp += delegate { Moved(); };
                Controls.Add(bar);
                note = Words("", 28, 641, 704, 18, Small(), Faint);
                beat.Interval = 700;
                beat.Tick += delegate { Fresh(); };
                VisibleChanged += delegate
                {
                    if (Visible) { Open(which); beat.Start(); Application.RemoveMessageFilter(this); Application.AddMessageFilter(this); }
                    else { beat.Stop(); Application.RemoveMessageFilter(this); }
                };
                FormClosed += delegate { beat.Stop(); Application.RemoveMessageFilter(this); };
                Disposed += delegate { beat.Dispose(); };
                ResumeLayout(false);
            }

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

            // Her text box in the window's own frame, the colour of her other boxes.
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Rectangle well = Box(28, 116, 704, 512);
                using (Brush fill = new SolidBrush(Field)) e.Graphics.FillRectangle(fill, well);
                using (Pen edge = new Pen(Edge)) e.Graphics.DrawRectangle(edge, well);
            }

            // A file switched to is read from its end: the last screenful is what anybody wants.
            private void Open(int source)
            {
                which = source < 0 || watching == null || source >= watching.Length ? 0 : source;
                Frame.logShowing = which;
                read = 0;
                box.Text = "";
                following = true;
                pending.Length = 0;
                waiting = 0;
                Marked();
                Fresh();
            }

            private void Marked()
            {
                for (int one = 0; one < picks.Length; one++)
                    if (picks[one] != null) { picks[one].Accent = one == which ? Cyan : Rose; picks[one].Status = one == which ? "Shown" : null; picks[one].Invalidate(); }
                follow.Accent = following ? Cyan : Rose;
                follow.Invalidate();
                string named = watching == null || which >= watching.Length ? "" : Path.GetFileName(watching[which]);
                string held = waiting == 0 ? "   ·   held" : "   ·   held, " + waiting + (waiting == 1 ? " new line" : " new lines") + " below";
                note.Text = named + (following ? "   ·   following" : held) + "   ·   output only, nothing typed here reaches her";
            }

            private void Moved()
            {
                if (IsDisposed) return;
                if (bar.AtEnd) { if (!following) Resume(); return; }
                if (following) { following = false; Marked(); }
            }

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

            // What was added since the last look. A file that got shorter was rewritten, so it is read again from its end.
            private void Fresh()
            {
                if (watching == null || which >= watching.Length) { if (box.TextLength == 0) box.Text = "Nothing written yet."; return; }
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
                        if (said.Length > 0) Add(said);
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            private void Add(string said)
            {
                // Held, new lines wait: putting them in would throw the view back to the top on every line.
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
                Bottom();
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
        }
    }
}
