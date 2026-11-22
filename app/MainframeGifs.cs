using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Her GIFs window, from Settings > GENERAL SETTINGS: the GIFs they saved for her, each named after its file, to add, rename or
    // remove. Remove takes a second click. It lists only what is in her gifs folder; nothing is fetched.
    internal sealed partial class Mainframe
    {
        private GifSheet gifSheet;
        private Key gifsKey;
        internal Func<IWin32Window, string[]> PickGifs = PickGifFiles;

        private static string[] PickGifFiles(IWin32Window owner)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Add GIFs for Capri";
                dialog.Filter = "GIFs (*.gif)|*.gif";
                dialog.Multiselect = true;
                dialog.CheckFileExists = true;
                dialog.RestoreDirectory = true;
                return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileNames : new string[0];
            }
        }

        // Made fresh when the Mainframe's size changed, like her memory window.
        internal void OpenGifs()
        {
            if (gifSheet != null && !gifSheet.IsDisposed && gifSheet.Zoom != zoom) gifSheet.Close();
            if (gifSheet == null || gifSheet.IsDisposed) gifSheet = new GifSheet(this, zoom);
            if (!gifSheet.Visible) gifSheet.Show(this);
            gifSheet.Activate();
        }

        internal void CloseGifs() { if (gifSheet != null && !gifSheet.IsDisposed) gifSheet.Close(); }

        internal string GifsShown { get { return gifSheet == null || gifSheet.IsDisposed || !gifSheet.Visible ? null : gifSheet.Snapshot; } }

        internal Button GifsKey { get { return gifsKey; } }

        // The window driven as they'd use it at one size, for the checks: list, add, rename and remove. Null when it all
        // went right, else what didn't, with the size.
        internal string GifsAt(float zoomAt, string picture, string[] adding)
        {
            using (GifSheet drawn = new GifSheet(this, zoomAt))
            {
                drawn.Inline = true;
                drawn.Show(this);
                Func<IWin32Window, string[]> pickWas = PickGifs;
                try
                {
                    string problem = drawn.Drive(picture, adding);
                    return problem == null ? null : problem + " (" + drawn.ClientSize.Width + " x " + drawn.ClientSize.Height + " at " + zoomAt + ")";
                }
                finally
                {
                    PickGifs = pickWas;
                    drawn.Close();
                }
            }
        }

        private sealed class GifSheet : ModSheet
        {
            private const int Wide = 560, Tall = 520, ListTop = 148, ListTall = 296;
            private readonly Key addKey, saveKey, cancelKey;
            private readonly Entry naming;
            private readonly Label count, notice;
            private readonly System.Windows.Forms.Panel list;
            private readonly System.Windows.Forms.Timer noticeTimer = new System.Windows.Forms.Timer(), armTimer = new System.Windows.Forms.Timer();
            private List<string> names = new List<string>();
            private string editing, armed;
            private int ticket;

            internal GifSheet(Mainframe owner, float zoom) : base(owner, zoom, "Her GIFs", "HER GIFS >", Wide, Tall)
            {
                Words("GIFs you save for her. She sends one when you ask, or now and then.", 28, 52, 504, 18, Small(), Faint);
                addKey = Make("Add GIFs...", 28, 78, 124, 26, delegate { Adding(); });
                addKey.AccessibleName = "Add GIFs: pick .gif files for her to send";
                count = Words("", 164, 82, 368, 18, Small(), Faint);
                naming = new Entry { Bounds = Box(28, 112, 300, 26) };
                naming.Box.MaxLength = GifShelf.Longest;
                naming.Box.KeyDown += delegate(object sender, KeyEventArgs e)
                {
                    if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Save(); }
                    else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; StopEditing(); }
                };
                Controls.Add(naming);
                saveKey = Make("Save", 336, 112, 84, 26, delegate { Save(); });
                saveKey.AccessibleName = "Save the new name";
                cancelKey = Make("Cancel", 428, 112, 104, 26, delegate { StopEditing(); });
                cancelKey.AccessibleName = "Cancel renaming";
                cancelKey.Accent = Rose;
                naming.Visible = saveKey.Visible = cancelKey.Visible = false;
                list = new System.Windows.Forms.Panel { Bounds = Box(28, ListTop, 504, ListTall), AutoScroll = true, BackColor = Ground };
                Controls.Add(list);
                notice = Words("", 28, 452, 504, 22, Small(), Cyan);
                Words("Up to " + GifShelf.Most + " GIFs, each under 8 MB and " + GifShelf.MostFrames + " frames.", 28, 482, 504, 18, Small(), Faint);
                noticeTimer.Interval = 8000;
                noticeTimer.Tick += delegate { Says("", false); };
                armTimer.Interval = 6000;
                armTimer.Tick += delegate { Disarm(); Fill(); };
                FormClosed += delegate { noticeTimer.Stop(); armTimer.Stop(); };
                Disposed += delegate { noticeTimer.Dispose(); armTimer.Dispose(); };
                ResumeLayout(false);
            }

            protected override void OnLoad(EventArgs e)
            {
                base.OnLoad(e);
                Reload();
            }

            internal void Reload()
            {
                int mine = ++ticket;
                Run(delegate
                {
                    List<string> got = GifShelf.Names();
                    Back(delegate
                    {
                        if (mine != ticket) return;
                        names = got;
                        if (editing != null && !names.Contains(editing)) StopEditing();
                        if (armed != null && !names.Contains(armed)) Disarm();
                        count.Text = names.Count + " of " + GifShelf.Most + " GIFs";
                        Fill();
                    });
                });
            }

            private void Says(string text, bool problem)
            {
                noticeTimer.Stop();
                notice.Text = text ?? "";
                notice.ForeColor = problem ? Rose : Cyan;
                if (!string.IsNullOrEmpty(text)) noticeTimer.Start();
            }

            private void Disarm()
            {
                armTimer.Stop();
                armed = null;
            }

            private void Adding()
            {
                string[] files = Frame.PickGifs(this) ?? new string[0];
                if (files.Length == 0) return;
                Disarm();
                Run(delegate
                {
                    int added = 0;
                    string last = null;
                    List<string> problems = new List<string>();
                    foreach (string file in files)
                    {
                        string kept;
                        string problem = GifShelf.Add(file, null, out kept);
                        if (problem == null) { added++; last = kept; }
                        else problems.Add(problem);
                    }
                    Back(delegate
                    {
                        string said = added == 0 ? "" : added == 1 ? "Added \"" + last + "\"." : "Added " + added + " GIFs.";
                        if (problems.Count > 0) said = (said + " " + problems[0] + (problems.Count > 1 ? " " + (problems.Count - 1) + " more couldn't be added." : "")).Trim();
                        Says(said, problems.Count > 0);
                        Reload();
                    });
                });
            }

            // Rename puts its name in the box above the list, and Save or Enter keeps the new one.
            private void Begin(string name)
            {
                editing = name;
                Disarm();
                naming.Box.Text = name;
                naming.Box.AccessibleName = "New name for " + name;
                naming.Visible = saveKey.Visible = cancelKey.Visible = true;
                naming.Box.Select();
                naming.Box.SelectAll();
                Fill();
            }

            private void StopEditing()
            {
                editing = null;
                naming.Box.Text = "";
                naming.Visible = saveKey.Visible = cancelKey.Visible = false;
                Fill();
            }

            private void Save()
            {
                if (editing == null) return;
                string name = editing, to = naming.Box.Text;
                Run(delegate
                {
                    string kept;
                    string problem = GifShelf.Rename(name, to, out kept);
                    Back(delegate
                    {
                        if (problem == null) { StopEditing(); Says(kept == name ? "Kept \"" + kept + "\"." : "Renamed to \"" + kept + "\".", false); }
                        else Says(problem, true);
                        Reload();
                    });
                });
            }

            private void Removing(string name)
            {
                if (armed != name) { armed = name; armTimer.Stop(); armTimer.Start(); Fill(); return; }
                Disarm();
                Run(delegate
                {
                    string problem = GifShelf.Remove(name);
                    Back(delegate
                    {
                        Says(problem ?? "Removed \"" + name + "\".", problem != null);
                        Reload();
                    });
                });
            }

            private void Fill()
            {
                list.SuspendLayout();
                foreach (Control old in new List<Control>(ControlsOf(list))) { list.Controls.Remove(old); old.Dispose(); }
                int y = 0;
                if (names.Count == 0)
                {
                    Label none = new Note { Text = "No GIFs yet. Add some and she'll send one now and then.", Font = Small(), ForeColor = Faint, BackColor = Ground, AutoSize = false };
                    none.Bounds = new Rectangle(Z(8), Z(y), Z(470), Z(20));
                    list.Controls.Add(none);
                }
                foreach (string name in names)
                {
                    string mine = name;
                    Label words = new Note { Text = name, Font = Type(9.5f * Zoom), ForeColor = name == editing ? Rose : White, BackColor = Ground, AutoSize = false, AutoEllipsis = true, UseMnemonic = false };
                    words.Bounds = new Rectangle(Z(8), Z(y + 2), Z(300), Z(20));
                    list.Controls.Add(words);
                    Key rename = Tool("Rename", 316, y, 84, delegate { Begin(mine); });
                    rename.AccessibleName = "Rename " + name;
                    Key remove = Tool(armed == name ? "Click again" : "Remove", 406, y, 76, delegate { Removing(mine); });
                    remove.AccessibleName = armed == name ? "Click again to remove " + name : "Remove " + name + ". Press twice to confirm";
                    remove.Accent = Hot;
                    y += 28;
                }
                list.ResumeLayout();
            }

            private static List<Control> ControlsOf(Control parent)
            {
                List<Control> all = new List<Control>();
                foreach (Control one in parent.Controls) all.Add(one);
                return all;
            }

            private Key Tool(string text, int x, int y, int width, EventHandler click)
            {
                Key key = new Key { Text = text, Font = Type(8.5f * Zoom, true), Bounds = new Rectangle(Z(x), Z(y), Z(width), Z(22)) };
                key.Click += click;
                list.Controls.Add(key);
                return key;
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape && (editing != null || armed != null))
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    Disarm();
                    StopEditing();
                    return;
                }
                base.OnKeyDown(e);
            }

            // What it shows: the count, then each name and its keys.
            internal string Snapshot
            {
                get
                {
                    StringBuilder all = new StringBuilder(count.Text).Append(" | ");
                    foreach (Control control in list.Controls)
                    {
                        Key key = control as Key;
                        if (key != null) all.Append("[").Append(key.Text).Append("] ");
                        else if (control.Text.Length > 0) all.Append(control.Text).Append(" | ");
                    }
                    return all.ToString();
                }
            }

            // The list's own rows: inside it, with nothing overlapping and every key's words fitting.
            private string ListCrowded()
            {
                List<Control> shown = new List<Control>();
                foreach (Control control in list.Controls) if (control.Visible) shown.Add(control);
                int inside = list.ClientSize.Width;
                for (int i = 0; i < shown.Count; i++)
                {
                    Control a = shown[i];
                    string name = a.Text.Length > 0 ? a.Text : a.AccessibleName;
                    if (a.Right > inside) return "pokes out: " + name + " " + a.Bounds;
                    Key key = a as Key;
                    if (key != null && TextRenderer.MeasureText(key.Text, key.Font).Width + Z(12) > key.Width) return "doesn't fit: " + key.Text;
                    for (int j = i + 1; j < shown.Count; j++)
                        if (a.Bounds.IntersectsWith(shown[j].Bounds)) return "overlap: " + name + " / " + (shown[j].Text.Length > 0 ? shown[j].Text : shown[j].AccessibleName);
                }
                return null;
            }

            private string Fits(string where)
            {
                string wrong = Crowded() ?? ListCrowded();
                return wrong == null ? null : where + ": " + wrong;
            }

            private string Showing(string wanted)
            {
                string all = Snapshot;
                return all.IndexOf(wanted, StringComparison.Ordinal) >= 0 ? null : "\"" + wanted + "\" isn't shown in " + all;
            }

            private static bool Kept(string name) { return GifShelf.PathOf(name) != null; }

            private void Escaping() { OnKeyDown(new KeyEventArgs(Keys.Escape)); }

            // With party and wave saved: they list in order, Add takes a GIF and refuses a file that isn't one, Rename opens
            // the box and Esc closes it, Save renames, the first Remove only asks again and Esc drops that, the second removes.
            internal string Drive(string picture, string[] adding)
            {
                Reload();
                string wrong = Fits("list");
                if (wrong != null) return wrong;
                foreach (string wanted in new[] { "2 of " + GifShelf.Most + " GIFs", "party", "wave", "[Rename]", "[Remove]" })
                {
                    wrong = Showing(wanted);
                    if (wrong != null) return wrong;
                }
                if (Snapshot.IndexOf("party", StringComparison.Ordinal) > Snapshot.IndexOf("wave", StringComparison.Ordinal)) return "Her GIFs aren't in order: " + Snapshot;
                if (picture != null) Picture(picture);

                Frame.PickGifs = delegate { return adding; };
                Adding();
                if (!Kept("cat dance") || notice.Text.IndexOf("Added", StringComparison.Ordinal) < 0 || notice.Text.IndexOf("isn't a GIF", StringComparison.Ordinal) < 0 || notice.ForeColor != Rose)
                    return "Add GIFs didn't add the GIF and refuse the rest: " + notice.Text;
                wrong = Showing("cat dance") ?? Showing("3 of " + GifShelf.Most + " GIFs") ?? Fits("added");
                if (wrong != null) return wrong;

                Begin("cat dance");
                if (!naming.Visible || naming.Box.Text != "cat dance") return "Rename didn't open the box with its name.";
                wrong = Fits("rename");
                if (wrong != null) return wrong;
                Escaping();
                if (naming.Visible || editing != null) return "Esc didn't cancel renaming.";
                Begin("cat dance");
                naming.Box.Text = "Cat Dance!!";
                Save();
                if (!Kept("Cat Dance") || editing != null || GifShelf.Names().IndexOf("Cat Dance") < 0) return "Saving a new name didn't rename the GIF: " + notice.Text + " / " + string.Join(", ", GifShelf.Names().ToArray());
                if (notice.Text != "Renamed to \"Cat Dance\".") return "Renaming didn't say its new name: " + notice.Text;
                wrong = Showing("Cat Dance") ?? Fits("renamed");
                if (wrong != null) return wrong;

                Removing("party");
                if (!Kept("party") || armed != "party") return "The first click on Remove didn't only ask for another.";
                wrong = Showing("[Click again]") ?? Fits("remove confirm");
                if (wrong != null) return wrong;
                Escaping();
                if (armed != null || !Kept("party")) return "Esc didn't drop the remove confirm.";
                Removing("party");
                Removing("party");
                if (Kept("party") || Snapshot.IndexOf("party", StringComparison.Ordinal) >= 0 || notice.Text != "Removed \"party\".") return "The second click didn't remove the GIF: " + notice.Text + " / " + Snapshot;
                return Fits("end");
            }
        }
    }
}
