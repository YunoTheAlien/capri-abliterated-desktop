using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Her memory window: what she keeps about them, line by line, to edit, forget, add or undo, and "Forget everything
    // about me". It opens only me.md, memories.md and her game notes; undo lasts while she runs.
    internal sealed partial class Mainframe
    {
        private MemorySheet memorySheet;

        // Open her memories: the window, made fresh when the Mainframe's size changed.
        internal void OpenMemory()
        {
            if (memorySheet != null && !memorySheet.IsDisposed && memorySheet.Zoom != zoom) memorySheet.Close();
            if (memorySheet == null || memorySheet.IsDisposed) memorySheet = new MemorySheet(this, zoom);
            if (!memorySheet.Visible) memorySheet.Show(this);
            memorySheet.Activate();
        }

        internal static class MemoryLines
        {
            internal const string About = "about", Chats = "chats", Games = "games";
            private static readonly object gameGate = new object();

            internal sealed class Line
            {
                internal string Kind, Group, Text, Path;
                internal int At;
            }

            internal sealed class Change
            {
                internal string Path, Before, After, Kind;
            }

            // Her lines worth showing: in me.md every line that isn't a heading or a note, in memories.md and game notes
            // every bullet, each under the heading it sits under.
            internal static List<Line> Read(string kind, string path)
            {
                List<Line> lines = new List<Line>();
                string text = Text(path);
                if (text == null) return lines;
                string[] rows = text.Replace("\r\n", "\n").Split('\n');
                string group = kind == Games ? Path.GetFileNameWithoutExtension(path) : "";
                for (int i = 0; i < rows.Length; i++)
                {
                    string row = rows[i].Trim();
                    if (row.Length == 0 || row.StartsWith(">", StringComparison.Ordinal)) continue;
                    if (row.StartsWith("#", StringComparison.Ordinal)) { if (kind != About) group = row.TrimStart('#').Trim(); continue; }
                    bool bullet = row.StartsWith("- ", StringComparison.Ordinal) || row.StartsWith("* ", StringComparison.Ordinal);
                    if (kind != About && !bullet) continue;
                    string words = bullet ? row.Substring(2).Trim() : row;
                    // An empty slot, "Name:" or "petNames=", is not a memory yet.
                    if (words.Length == 0 || Regex.IsMatch(words, @"^[\w ]{1,30}[:=]\s*$")) continue;
                    lines.Add(new Line { Kind = kind, Group = group, Text = words, Path = path, At = i });
                }
                return lines;
            }

            internal static List<string> GameFiles()
            {
                List<string> found = new List<string>();
                string folder = GameNotes.Folder;
                try
                {
                    if (folder == null || !Directory.Exists(folder)) return found;
                    foreach (string file in Directory.GetFiles(folder, "*.md"))
                        if (!file.EndsWith(".controls.md", StringComparison.OrdinalIgnoreCase)) found.Add(file);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                found.Sort(StringComparer.OrdinalIgnoreCase);
                return found;
            }

            internal static string Text(string path)
            {
                try { return path != null && File.Exists(path) ? File.ReadAllText(path) : null; }
                catch (IOException) { return null; }
                catch (UnauthorizedAccessException) { return null; }
            }

            // The file written whole: her notes and memories through her own writer and lock, a game note as it is.
            internal static string Write(string kind, string path, string text)
            {
                if (kind == Games)
                {
                    lock (gameGate)
                    {
                        try { File.WriteAllText(path, text); return null; }
                        catch (IOException) { return "Couldn't change that on your PC."; }
                        catch (UnauthorizedAccessException) { return "Couldn't change that on your PC."; }
                    }
                }
                try
                {
                    WrittenNote note = Brain.OpenNote(kind == Chats);
                    if (!string.Equals(note.Path, path, StringComparison.OrdinalIgnoreCase)) return "That file isn't hers to change now.";
                    return note.Save(text);
                }
                catch (IOException) { return "Couldn't change that on your PC."; }
            }

            // A line her code writes into: "- key= value" or "- key: value". Its key part, up to and including the = or :,
            // and anything before it, is never forgotten or edited away, so she always has the slot to write into again.
            private static readonly Regex Keyed = new Regex(@"^(\s*[-*]\s+[\w ]{1,30}?\s*[=:])\s*(.*)$");

            internal static string KeyOf(string row)
            {
                Match keyed = Keyed.Match(row ?? "");
                return keyed.Success ? keyed.Groups[1].Value : null;
            }

            // One line changed (now) or forgotten (null), checked against the file as it is first. A keyed line keeps its key
            // either way: forgetting clears only its value. Any other line is taken out whole. The change, or null with why not.
            internal static Change Set(Line line, string now, out string problem)
            {
                problem = null;
                string text = Text(line.Path);
                if (text == null) { problem = "That memory changed on your PC."; return null; }
                string newline = text.Contains("\r\n") ? "\r\n" : "\n";
                List<string> rows = new List<string>(text.Replace("\r\n", "\n").Split('\n'));
                if (line.At >= rows.Count || rows[line.At].IndexOf(line.Text, StringComparison.Ordinal) < 0) { problem = "That memory changed on your PC."; return null; }
                string key = line.Kind == About ? KeyOf(rows[line.At]) : null;
                if (key != null)
                {
                    string bare = Regex.Replace(key, @"^\s*[-*]\s+", "").TrimEnd();
                    string value = now == null ? "" : now.StartsWith(bare, StringComparison.OrdinalIgnoreCase) ? now.Substring(bare.Length).Trim() : now.Trim();
                    rows[line.At] = key + (value.Length == 0 ? "" : " " + value);
                }
                else if (now == null) rows.RemoveAt(line.At);
                else rows[line.At] = rows[line.At].Replace(line.Text, now);
                string after = string.Join(newline, rows.ToArray());
                problem = Write(line.Kind, line.Path, after);
                return problem == null ? new Change { Path = line.Path, Before = text, After = after, Kind = line.Kind } : null;
            }

            // me.md with every value cleared: headings, notes, blank lines and keys stay, free lines go.
            internal static string Cleared(string text)
            {
                string newline = text.Contains("\r\n") ? "\r\n" : "\n";
                List<string> kept = new List<string>();
                foreach (string row in text.Replace("\r\n", "\n").Split('\n'))
                {
                    string trimmed = row.Trim();
                    bool bullet = trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal);
                    if (!bullet && trimmed.Length > 0 && !trimmed.StartsWith("#", StringComparison.Ordinal) && !trimmed.StartsWith(">", StringComparison.Ordinal)) continue;
                    if (!bullet) { kept.Add(row); continue; }
                    string key = KeyOf(row);
                    if (key != null) kept.Add(key);
                }
                return string.Join(newline, kept.ToArray());
            }

            // Back to how it was, only while the file is still as the change left it.
            internal static string Undo(List<Change> changes)
            {
                foreach (Change change in changes)
                    if (!string.Equals(Text(change.Path) ?? "", change.After ?? "", StringComparison.Ordinal)) return "Her notes changed since, so she can't undo that.";
                foreach (Change change in changes)
                {
                    string problem = Write(change.Kind, change.Path, change.Before);
                    if (problem != null) return problem;
                }
                return null;
            }
        }

        private sealed class MemorySheet : ModSheet
        {
            private const int Wide = 760, Tall = 700, ListTop = 120, ListTall = 470;
            private readonly Entry adding;
            private readonly Key addKey, cancelKey, undoKey, wipeKey;
            private readonly Label notice;
            private readonly System.Windows.Forms.Panel list;
            private readonly List<List<MemoryLines.Change>> undone = new List<List<MemoryLines.Change>>();
            private MemoryLines.Line editing;
            private bool wipeArmed;

            internal MemorySheet(Mainframe owner, float zoom) : base(owner, zoom, "Her memory", "HER MEMORY >", Wide, Tall)
            {
                Words("What she keeps about you on this PC. Edit or forget any of it.", 28, 52, 704, 18, Small(), Faint);
                adding = new Entry { Bounds = Box(28, 78, 500, 26), Cue = "Something she should remember" };
                adding.Box.AccessibleName = "Something she should remember about you";
                adding.Box.KeyDown += delegate(object sender, KeyEventArgs e)
                {
                    if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Adding(); }
                    else if (e.KeyCode == Keys.Escape && editing != null) { e.SuppressKeyPress = true; StopEditing(); }
                };
                Controls.Add(adding);
                addKey = Make("Add", 536, 78, 64, 26, delegate { Adding(); });
                addKey.AccessibleName = "Add what you typed to her memory";
                cancelKey = Make("Cancel", 608, 78, 124, 26, delegate { StopEditing(); });
                cancelKey.Visible = false;
                Key text = Make("Edit as text", 608, 78, 124, 26, delegate { Frame.EditNotes(true); });
                text.AccessibleName = "Edit her memories as text";
                list = new System.Windows.Forms.Panel { Bounds = Box(28, ListTop, 704, ListTall), AutoScroll = true, BackColor = Ground };
                Controls.Add(list);
                notice = Words("", 28, 606, 580, 22, Small(), Faint);
                undoKey = Make("Undo", 626, 606, 106, 22, delegate { UndoLast(); });
                undoKey.AccessibleName = "Undo the last change to her memory";
                undoKey.Visible = false;
                wipeKey = Make("Forget everything about me", 28, 640, 260, 24, delegate { ForgetAll(); });
                wipeKey.AccessibleName = "Forget everything she remembers about you. Press twice to confirm";
                Fill();
                ResumeLayout(false);
            }

            internal bool Editing { get { return editing != null; } }

            // Every line, under her notes about them, what she remembers from their chats, and each game's notes.
            internal void Fill()
            {
                list.SuspendLayout();
                foreach (Control old in new List<Control>(ControlsOf(list))) { list.Controls.Remove(old); old.Dispose(); }
                int y = 0;
                y = Group("ABOUT YOU", MemoryLines.Read(MemoryLines.About, Brain.MemoryPath), y);
                y = Group("WHAT SHE REMEMBERS FROM YOUR CHATS", MemoryLines.Read(MemoryLines.Chats, Brain.MemoriesPath), y);
                List<MemoryLines.Line> games = new List<MemoryLines.Line>();
                foreach (string file in MemoryLines.GameFiles()) games.AddRange(MemoryLines.Read(MemoryLines.Games, file));
                Group("GAME NOTES", games, y);
                list.ResumeLayout();
            }

            private static List<Control> ControlsOf(Control parent)
            {
                List<Control> all = new List<Control>();
                foreach (Control one in parent.Controls) all.Add(one);
                return all;
            }

            private int Group(string title, List<MemoryLines.Line> lines, int y)
            {
                Label head = new Note { Text = title, Font = Type(9.5f * Zoom, true), ForeColor = Rose, BackColor = Ground, AutoSize = false };
                head.Bounds = new Rectangle(0, Z(y), Z(680), Z(22));
                list.Controls.Add(head);
                y += 26;
                if (lines.Count == 0)
                {
                    Label none = new Note { Text = "Nothing yet.", Font = Small(), ForeColor = Faint, BackColor = Ground, AutoSize = false };
                    none.Bounds = new Rectangle(Z(8), Z(y), Z(600), Z(20));
                    list.Controls.Add(none);
                    return y + 30;
                }
                string under = null;
                foreach (MemoryLines.Line line in lines)
                {
                    if (line.Group.Length > 0 && line.Group != under)
                    {
                        under = line.Group;
                        Label group = new Note { Text = line.Group, Font = Small(), ForeColor = Faint, BackColor = Ground, AutoSize = false, AutoEllipsis = true };
                        group.Bounds = new Rectangle(Z(8), Z(y), Z(660), Z(18));
                        list.Controls.Add(group);
                        y += 20;
                    }
                    MemoryLines.Line one = line;
                    Label words = new Note { Text = line.Text, Font = Small(), ForeColor = White, BackColor = Ground, AutoSize = false, AutoEllipsis = true, UseMnemonic = false };
                    words.Bounds = new Rectangle(Z(8), Z(y + 2), Z(500), Z(20));
                    list.Controls.Add(words);
                    Key edit = Tool("Edit", 520, y, 70, delegate { Begin(one); });
                    edit.AccessibleName = "Edit: " + Clip(line.Text);
                    Key forget = Tool("Forget", 596, y, 80, delegate { Forget(one); });
                    forget.AccessibleName = "Forget: " + Clip(line.Text);
                    forget.Accent = Hot;
                    y += 26;
                }
                return y + 10;
            }

            private Key Tool(string text, int x, int y, int width, EventHandler click)
            {
                Key key = new Key { Text = text, Font = Type(8.5f * Zoom, true), Bounds = new Rectangle(Z(x), Z(y), Z(width), Z(22)) };
                key.Click += click;
                list.Controls.Add(key);
                return key;
            }

            private static string Clip(string text) { return text.Length > 60 ? text.Substring(0, 57) + "..." : text; }

            private void Say(string text) { notice.Text = text; }

            private void Kept(List<MemoryLines.Change> changes)
            {
                undone.Add(changes);
                if (undone.Count > 12) undone.RemoveAt(0);
                undoKey.Visible = true;
            }

            // Edit puts the line in the box above, the box saves it.
            private void Begin(MemoryLines.Line line)
            {
                editing = line;
                adding.Box.Text = line.Text;
                adding.Box.Select();
                adding.Box.SelectAll();
                addKey.Text = "Save";
                cancelKey.Visible = true;
                cancelKey.BringToFront();
                Say("Change it above, then Save.");
            }

            private void StopEditing()
            {
                editing = null;
                adding.Box.Text = "";
                addKey.Text = "Add";
                cancelKey.Visible = false;
                Say("");
            }

            private void Adding()
            {
                string words = Regex.Replace(adding.Box.Text ?? "", @"\s+", " ").Trim(' ', '-', '"');
                if (words.Length == 0) { Say(editing != null ? "Write something, or forget it instead." : "What should she remember?"); return; }
                if (Brain.Refused(words)) { Say(Brain.Private); return; }
                if (editing != null)
                {
                    words = char.ToUpperInvariant(words[0]) + words.Substring(1);
                    if (words == editing.Text) { StopEditing(); Say("Nothing changed."); return; }
                    string problem;
                    MemoryLines.Change change = MemoryLines.Set(editing, words, out problem);
                    StopEditing();
                    if (change == null) { Say(problem); Fill(); return; }
                    Kept(new List<MemoryLines.Change> { change });
                    Say("Saved.");
                    Fill();
                    return;
                }
                string before = MemoryLines.Text(Brain.MemoryPath);
                string said = Brain.Note(words);
                string after = MemoryLines.Text(Brain.MemoryPath);
                if (!string.Equals(before, after, StringComparison.Ordinal))
                {
                    adding.Box.Text = "";
                    Kept(new List<MemoryLines.Change> { new MemoryLines.Change { Path = Brain.MemoryPath, Before = before ?? Brain.NotesHead, After = after, Kind = MemoryLines.About } });
                }
                Say(said);
                Fill();
            }

            private void Forget(MemoryLines.Line line)
            {
                string problem;
                MemoryLines.Change change = MemoryLines.Set(line, null, out problem);
                if (change == null) { Say(problem); Fill(); return; }
                Kept(new List<MemoryLines.Change> { change });
                Say("Forgot it.");
                Fill();
            }

            // Pressed twice: her notes about them and what she remembers from their chats go back to their headings.
            private void ForgetAll()
            {
                if (!wipeArmed)
                {
                    wipeArmed = true;
                    wipeKey.Text = "Click again to forget everything";
                    return;
                }
                wipeArmed = false;
                wipeKey.Text = "Forget everything about me";
                List<MemoryLines.Change> changes = new List<MemoryLines.Change>();
                foreach (KeyValuePair<string, string> one in new[] { new KeyValuePair<string, string>(MemoryLines.About, Brain.NotesHead), new KeyValuePair<string, string>(MemoryLines.Chats, Brain.MemoriesHead) })
                {
                    string path = one.Key == MemoryLines.About ? Brain.MemoryPath : Brain.MemoriesPath, before = MemoryLines.Text(path);
                    if (before == null || MemoryLines.Read(one.Key, path).Count == 0) continue;
                    // Her notes keep their headings, notes and every key, each with its value cleared; only free lines go.
                    string after = one.Key == MemoryLines.About ? MemoryLines.Cleared(before) : one.Value;
                    string problem = MemoryLines.Write(one.Key, path, after);
                    if (problem != null) { Say(problem); Fill(); return; }
                    changes.Add(new MemoryLines.Change { Path = path, Before = before, After = after, Kind = one.Key });
                }
                if (changes.Count == 0) { Say("There was nothing to forget."); return; }
                Kept(changes);
                Say("She forgot everything about you.");
                Fill();
            }

            private void UndoLast()
            {
                if (undone.Count == 0) return;
                List<MemoryLines.Change> last = undone[undone.Count - 1];
                string problem = MemoryLines.Undo(last);
                undone.RemoveAt(undone.Count - 1);
                undoKey.Visible = undone.Count > 0;
                Say(problem ?? "Put it back.");
                Fill();
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { if (editing != null) StopEditing(); else Close(); e.Handled = true; return; }
                base.OnKeyDown(e);
            }

            // For her checks: a line's Edit, Forget, the box and its keys, the way they'd use them.
            internal string Drive(string pathAbout, string pathChats)
            {
                Func<string, Label> showing = delegate(string text)
                {
                    foreach (Control one in list.Controls) { Label label = one as Label; if (label != null && label.Text == text) return label; }
                    return null;
                };
                Func<string, string, Key> keyFor = delegate(string verb, string text)
                {
                    foreach (Control one in list.Controls) { Key key = one as Key; if (key != null && key.AccessibleName == verb + ": " + Clip(text)) return key; }
                    return null;
                };
                string notesBefore = File.ReadAllText(pathAbout), memoriesBefore = File.ReadAllText(pathChats);
                if (showing("They like cats") == null || showing("I helped them fix their mic.") == null || showing("Secret from the other folder") != null) return "Her lines weren't shown, or a file she doesn't use now was read.";
                keyFor("Edit", "They like cats").PerformClick();
                adding.Box.Text = "They like dogs";
                addKey.PerformClick();
                if (File.ReadAllText(pathAbout).IndexOf("They like dogs", StringComparison.Ordinal) < 0 || showing("They like dogs") == null) return "Edit didn't save: " + notice.Text;
                undoKey.PerformClick();
                if (File.ReadAllText(pathAbout) != notesBefore) return "Undo didn't put the edited note back: " + notice.Text;
                keyFor("Forget", "I helped them fix their mic.").PerformClick();
                if (File.ReadAllText(pathChats).IndexOf("fix their mic", StringComparison.Ordinal) >= 0) return "Forget didn't take the memory out: " + notice.Text;
                undoKey.PerformClick();
                if (File.ReadAllText(pathChats) != memoriesBefore) return "Undo didn't bring a forgotten memory back: " + notice.Text;
                adding.Box.Text = "They main Sukuna";
                addKey.PerformClick();
                if (File.ReadAllText(pathAbout).IndexOf("They main Sukuna", StringComparison.Ordinal) < 0) return "Adding didn't keep the note: " + notice.Text;
                undoKey.PerformClick();
                if (File.ReadAllText(pathAbout) != notesBefore) return "Undo didn't take an added note back: " + notice.Text;
                adding.Box.Text = "my password is hunter2";
                addKey.PerformClick();
                if (File.ReadAllText(pathAbout) != notesBefore || notice.Text != Brain.Private) return "A password was kept: " + notice.Text;
                adding.Box.Text = "";
                // A keyed line forgotten keeps its key, empty; a free one goes whole; undo brings the value back.
                Func<string, bool> keys = delegate(string text)
                {
                    foreach (string key in Brain.TemplateKeys) if (!System.Text.RegularExpressions.Regex.IsMatch(text, @"^- " + key + "=", System.Text.RegularExpressions.RegexOptions.Multiline)) return false;
                    return System.Text.RegularExpressions.Regex.IsMatch(text, @"^- Name:", System.Text.RegularExpressions.RegexOptions.Multiline);
                };
                keyFor("Forget", "workHours= 9 to 5").PerformClick();
                string afterKeyed = File.ReadAllText(pathAbout);
                if (!keys(afterKeyed) || afterKeyed.IndexOf("9 to 5", StringComparison.Ordinal) >= 0 || !System.Text.RegularExpressions.Regex.IsMatch(afterKeyed, @"^- workHours=\r?$", System.Text.RegularExpressions.RegexOptions.Multiline)) return "Forget on a keyed line took its key or kept its value: " + afterKeyed;
                undoKey.PerformClick();
                if (File.ReadAllText(pathAbout) != notesBefore) return "Undo didn't bring a keyed value back: " + notice.Text;
                keyFor("Forget", "They like cats").PerformClick();
                string afterFree = File.ReadAllText(pathAbout);
                if (!keys(afterFree) || afterFree.IndexOf("They like cats", StringComparison.Ordinal) >= 0) return "Forget on a free line went wrong: " + afterFree;
                undoKey.PerformClick();
                wipeKey.PerformClick();
                if (File.ReadAllText(pathAbout) != notesBefore) return "One press of Forget everything forgot already";
                wipeKey.PerformClick();
                string wiped = File.ReadAllText(pathAbout);
                if (MemoryLines.Read(MemoryLines.About, pathAbout).Count != 0 || MemoryLines.Read(MemoryLines.Chats, pathChats).Count != 0 || !keys(wiped) || wiped.IndexOf(">! workHours is when", StringComparison.Ordinal) < 0) return "Forget everything left something, or took a key or a note: " + notice.Text + " / " + wiped;
                undoKey.PerformClick();
                if (File.ReadAllText(pathAbout) != notesBefore || File.ReadAllText(pathChats) != memoriesBefore) return "Undo didn't bring everything back: " + notice.Text;
                return Crowded();
            }
        }

        internal void CheckMemoryWindow(List<string> checks, string output)
        {
            string aboutWas = Brain.MemoryPath, chatsWas = Brain.MemoriesPath, gamesWas = GameNotes.Folder;
            string folder = Path.Combine(output, "memory-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            string mine = Path.Combine(folder, "mine"), other = Path.Combine(folder, "other");
            try
            {
                Directory.CreateDirectory(mine);
                Directory.CreateDirectory(other);
                Directory.CreateDirectory(Path.Combine(mine, "games"));
                File.WriteAllText(Path.Combine(mine, "me.md"), Brain.NotesHead.Replace("- workHours=\n", "- workHours= 9 to 5\n") + "- Name: Sam\n- They like cats\n");
                File.WriteAllText(Path.Combine(mine, "memories.md"), Brain.MemoriesHead + "## 2026-11-20\n- I helped them fix their mic.\n");
                File.WriteAllText(Path.Combine(other, "me.md"), Brain.NotesHead + "- Secret from the other folder\n");
                File.WriteAllText(Path.Combine(mine, "games", "chess.md"), "# Chess\n- They open with e4\n");
                Brain.MemoryPath = Path.Combine(mine, "me.md");
                Brain.MemoriesPath = Path.Combine(mine, "memories.md");
                GameNotes.Folder = Path.Combine(mine, "games");
                foreach (float size in new[] { 1f, 2f })
                    using (MemorySheet sheet = new MemorySheet(this, size))
                    {
                        sheet.Inline = true;
                        sheet.Show(this);
                        try
                        {
                            string problem = sheet.Drive(Brain.MemoryPath, Brain.MemoriesPath);
                            if (problem != null) throw new Exception("Her memory window at " + size + "x: " + problem);
                        }
                        finally { sheet.Close(); }
                    }
                checks.Add("PASS her memory window shows her notes, what she remembers and her game notes from the files she uses now and nothing else; Edit, Forget, Add, a refused password, Forget everything (pressed twice) and Undo each change the files as they should, at 1x and 2x, nothing crowded; Forget on a keyed line and Forget everything clear values and keep every key and note, and a free line goes whole");
                // A me.md an older Forget took a key out of: the key goes back, empty, among her keys, its note not doubled.
                string hurt = Path.Combine(mine, "me-hurt.md");
                File.WriteAllText(hurt, Brain.NotesHead.Replace("- workHours=\n", "").Replace("- petNames=\n", "- petNames= boss\n") + "- They like cats\n");
                Brain.MemoryPath = hurt;
                Brain.TopUpNotes();
                string mended = File.ReadAllText(hurt);
                bool slotted = mended.IndexOf("- awakeHours=\n- workHours=\n- petNames= boss\n", StringComparison.Ordinal) >= 0;
                bool single = mended.IndexOf(">! workHours is when", StringComparison.Ordinal) == mended.LastIndexOf(">! workHours is when", StringComparison.Ordinal);
                string again = mended;
                Brain.TopUpNotes();
                if (!slotted || !single || File.ReadAllText(hurt) != again || !mended.Contains("- They like cats")) throw new Exception("A me.md missing a key wasn't mended right: back in its place " + slotted + ", its note once " + single + " / " + mended);
                checks.Add("PASS a me.md missing one of her keys gets it back, empty, in its place among her keys, its note not doubled, nothing of theirs touched, and once only");
            }
            finally
            {
                Brain.MemoryPath = aboutWas;
                Brain.MemoriesPath = chatsWas;
                GameNotes.Folder = gamesWas;
                try { Directory.Delete(folder, true); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
        }
    }
}
