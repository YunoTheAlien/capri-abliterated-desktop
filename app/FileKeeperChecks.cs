#if CHECKS
using System;
using System.Collections.Generic;
using System.IO;

namespace CapriDesktop
{
    internal static class FileKeeperChecks
    {
        private static void Expect(bool good, string problem) { if (!good) throw new Exception(problem); }

        internal static List<string> Run()
        {
            List<string> passed = new List<string>();
            string scratch = Path.Combine(Path.GetTempPath(), "capri-keeper-" + Guid.NewGuid().ToString("N"));
            string top = Path.Combine(scratch, "keeper"), kept = Path.Combine(scratch, "keeper-data");
            if (Directory.Exists(top)) Directory.Delete(top, true);
            if (Directory.Exists(kept)) Directory.Delete(kept, true);
            string trip = Path.Combine(top, "Trip"), work = Path.Combine(top, "Work"), loose = Path.Combine(top, "Loose");
            Directory.CreateDirectory(trip); Directory.CreateDirectory(work); Directory.CreateDirectory(loose);
            DateTime now = new DateTime(2026, 11, 19, 15, 0, 0);
            string beach = Write(trip, "beach day.mp4", "video", now.AddDays(-3)), dog = Write(trip, "dog at beach.jpg", "photo", now.AddDays(-40));
            string lease = Write(work, "notes 2026.txt", "The lease for the flat ends in March.", now.AddDays(-1)), budget = Write(work, "budget.csv", "rent,food", now);
            Write(loose, "clip.mov", "v", now); Write(loose, "song.mp3", "m", now); Write(loose, "scan.pdf", "p", now); Write(loose, "mystery.qqq", "?", now);

            // Ours: the Recycle Bin is a folder in the scratch here, so nothing of a check reaches the PC's real one.
            string bin = Path.Combine(scratch, "bin");
            Directory.CreateDirectory(bin);
            Func<string, bool, string> recycleWas = FileKeeper.Recycle;
            string[] hersWas = FileKeeper.Hers;
            string keepsWas = FileKeeper.Keeps;
            FileKeeper.Keeps = scratch;
            FileKeeper.Recycle = delegate(string path, bool folder)
            {
                string into = Path.Combine(bin, Guid.NewGuid().ToString("N").Substring(0, 6) + "-" + Path.GetFileName(path));
                if (folder) Directory.Move(path, into); else File.Move(path, into);
                return null;
            };
            try
            {
            Action<FileChange> changedWas = FileKeeper.Changed;
            Action<string, string> usedWas = FileKeeper.Used;
            List<FileChange> told = new List<FileChange>();
            List<string> order = new List<string>();
            FileKeeper.Changed = delegate(FileChange change) { told.Add(change); order.Add("change " + change.What); };
            FileKeeper.Used = delegate(string call, string result) { order.Add("call " + call + " = " + result); };
            try
            {
                FileKeeper keeper = new FileKeeper(kept) { Now = delegate { return now; } };
                keeper.Roots = delegate { return new[] { top }; };
                string found = keeper.Do(new ToolArguments { Name = "beach" }, null);
                Expect(found.Contains("1. ") && found.Contains(beach) && found.Contains(dog), "Her file search missed names: " + found);
                found = keeper.Do(new ToolArguments { Name = "beach", Kind = "video" }, null);
                Expect(found.Contains(beach) && !found.Contains(dog), "Her file search ignored the kind: " + found);
                found = keeper.Do(new ToolArguments { Kind = "photo", When = "last week" }, null);
                Expect(found.StartsWith("I didn't find", StringComparison.Ordinal), "An old photo matched last week: " + found);
                found = keeper.Do(new ToolArguments { When = "this week" }, null);
                Expect(found.Contains(beach) && found.Contains(lease) && !found.Contains(dog), "This week's files were wrong: " + found);
                found = keeper.Do(new ToolArguments { Search = "lease flat" }, null);
                Expect(found.Contains(lease) && !found.Contains(budget) && found.Contains("has those words inside"), "Words inside a file weren't found: " + found);
                found = keeper.Do(new ToolArguments { Kind = "video", Search = "beach" }, null);
                Expect(found.Contains(beach) && found.Contains("can't see inside videos"), "A video was searched inside or the limit went unsaid: " + found);
                found = keeper.Do(new ToolArguments { Name = "budget", Folder = trip }, null);
                Expect(found.StartsWith("I didn't find", StringComparison.Ordinal), "A named folder didn't limit the search: " + found);
                Expect(keeper.Do(new ToolArguments { When = "the 40th of never" }, null).StartsWith("I couldn't read the date", StringComparison.Ordinal), "An unreadable date was ignored.");
                passed.Add("PASS her files tool finds by name, kind, date and words inside documents, limits to a named folder, says it can't see inside videos, and numbers what it finds");

                DateTime from, to;
                Expect(FileKeeper.Dates("today", now, out from, out to) && from == now.Date && to == now.Date.AddDays(1), "today");
                Expect(FileKeeper.Dates("yesterday", now, out from, out to) && from == now.Date.AddDays(-1), "yesterday");
                Expect(FileKeeper.Dates("last week", now, out from, out to) && from == new DateTime(2026, 11, 9) && to == new DateTime(2026, 11, 16), "last week");
                Expect(FileKeeper.Dates("2026-11-03", now, out from, out to) && from == new DateTime(2026, 11, 3) && to == new DateTime(2026, 11, 4), "a day");
                Expect(FileKeeper.Dates("2026-11", now, out from, out to) && from == new DateTime(2026, 11, 1) && to == new DateTime(2026, 12, 1), "a month");
                Expect(FileKeeper.Dates("december", now, out from, out to) && from == new DateTime(2025, 12, 1), "a month name in the future means last year's");
                Expect(FileKeeper.Dates("2026-11-01 to 2026-11-07", now, out from, out to) && from == new DateTime(2026, 11, 1) && to == new DateTime(2026, 11, 8), "a range");
                Expect(FileKeeper.Dates("before 2026-01", now, out from, out to) && from == DateTime.MinValue && to == new DateTime(2026, 1, 1), "before");
                Expect(FileKeeper.Dates("last 3 days", now, out from, out to) && from == new DateTime(2026, 11, 17), "last 3 days");
                Expect(FileKeeper.Dates("3rd November", now, out from, out to) && from == new DateTime(2026, 11, 3), "3rd November");
                Expect(!FileKeeper.Dates("someday soon", now, out from, out to), "nonsense was read as a date");
                passed.Add("PASS her file dates read today, yesterday, last week, days, months, ranges, before and after the way they're said");

                keeper.Do(new ToolArguments { Name = "beach" }, null);
                string moved = keeper.Do(new ToolArguments { Action = "move", Path = "1, 2", Target = "Beach" }, null);
                string beachFolder = Path.Combine(trip, "Beach");
                Expect(moved.StartsWith("Moved ", StringComparison.Ordinal) && moved.EndsWith(FileKeeper.UndoLine, StringComparison.Ordinal) && File.Exists(Path.Combine(beachFolder, "beach day.mp4")) && File.Exists(Path.Combine(beachFolder, "dog at beach.jpg")) && !File.Exists(beach), "Moving numbered results into a new folder beside them failed: " + moved);
                Write(trip, "beach day.mp4", "a second one", now);
                keeper.Do(new ToolArguments { Name = "beach day", Folder = trip, Kind = "video" }, null);
                moved = keeper.Do(new ToolArguments { Action = "move", Path = "all", Target = beachFolder }, null);
                Expect(File.Exists(Path.Combine(beachFolder, "beach day (2).mp4")) && File.ReadAllText(Path.Combine(beachFolder, "beach day.mp4")) == "video", "A move overwrote a file with the same name: " + moved);
                Expect(keeper.Undo().StartsWith("Put back 1", StringComparison.Ordinal) && File.ReadAllText(beach) == "a second one", "Undo didn't put the second move back.");
                string undone = keeper.Undo();
                Expect(undone.StartsWith("Put back 2", StringComparison.Ordinal) && File.ReadAllText(Path.Combine(trip, "beach day (2).mp4")) == "video" && File.Exists(dog) && !Directory.Exists(beachFolder), "Undo didn't put the first move back or left its folder: " + undone);
                File.Delete(beach); File.Move(Path.Combine(trip, "beach day (2).mp4"), beach);
                passed.Add("PASS her moves take numbered results into a new folder beside them, never overwrite, and undo puts each change back in order and removes folders she made");

                keeper.Do(new ToolArguments { Name = "notes" }, null);
                string renamed = keeper.Do(new ToolArguments { Action = "rename", Path = "1", Name = "lease notes" }, null);
                Expect(File.Exists(Path.Combine(work, "lease notes.txt")) && !File.Exists(lease) && renamed.Contains("lease notes.txt"), "Renaming lost the extension: " + renamed);
                Expect(keeper.Do(new ToolArguments { Action = "rename", Path = "1", Name = "a/b" }, null).StartsWith("A name can't have", StringComparison.Ordinal), "A name with a slash was allowed.");
                keeper.Undo();
                Expect(File.Exists(lease), "Undoing a rename didn't change it back.");
                string made = keeper.Do(new ToolArguments { Action = "make_folder", Name = "Taxes", Folder = work }, null);
                Expect(Directory.Exists(Path.Combine(work, "Taxes")) && made.StartsWith("Made the folder", StringComparison.Ordinal), "Making a folder failed: " + made);
                keeper.Undo();
                Expect(!Directory.Exists(Path.Combine(work, "Taxes")), "Undoing a new folder left it.");
                passed.Add("PASS her renames keep the extension and refuse slashes, make_folder makes one where asked, and undo reverses both");

                string sorted = keeper.Do(new ToolArguments { Action = "organise", Folder = loose }, null);
                Expect(File.Exists(Path.Combine(loose, "Videos", "clip.mov")) && File.Exists(Path.Combine(loose, "Music", "song.mp3")) && File.Exists(Path.Combine(loose, "Documents", "scan.pdf")) && File.Exists(Path.Combine(loose, "mystery.qqq")), "Sorting by kind went wrong: " + sorted);
                Expect(new FileKeeper(kept).Recent().Count == keeper.Recent().Count && !keeper.Recent()[keeper.Recent().Count - 1].Undone, "Her file changes weren't kept for undo after a restart.");
                Expect(new FileKeeper(kept).Undo().StartsWith("Put back 3", StringComparison.Ordinal) && File.Exists(Path.Combine(loose, "clip.mov")) && !Directory.Exists(Path.Combine(loose, "Videos")), "Undo after a restart didn't unsort the folder.");
                passed.Add("PASS sorting a folder groups loose files by kind and leaves what she doesn't recognise, and undo works after a restart");

                Expect(keeper.Guarded(Path.GetPathRoot(top), false) != null && keeper.Guarded(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), false) != null, "A whole drive or the user folder could be changed.");
    #if CROSS
                Expect(keeper.Guarded("/usr/lib", false) != null && keeper.Guarded("/etc", true) != null, "A system folder could be changed.");
    #else
                Expect(keeper.Guarded(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32"), false) != null && keeper.Guarded(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), true) != null, "A system folder could be changed.");
    #endif
                Expect(keeper.Do(new ToolArguments { Action = "move", Path = top, Target = Path.Combine(top, "Inside") }, null).StartsWith("I can't move", StringComparison.Ordinal), "A folder was moved into itself.");
                Expect(keeper.Do(new ToolArguments { Action = "organise", Folder = Path.GetPathRoot(top) }, null).StartsWith("I won't sort", StringComparison.Ordinal), "A whole drive could be sorted.");
                passed.Add("PASS her file changes refuse whole drives, the user folder, system and app folders, and moving a folder into itself");

                string writing = Path.Combine(top, "Writing"), plan = Path.Combine(writing, "plan.txt"), list = Path.Combine(writing, "list.md");
                Directory.CreateDirectory(writing);
                string wrote = keeper.Do(new ToolArguments { Action = "write", Name = "plan", Folder = writing, Text = "line one\nline two\n" }, null);
                Expect(wrote.StartsWith("Wrote " + plan, StringComparison.Ordinal) && File.ReadAllText(plan) == "line one\nline two\n", "Writing a new file failed: " + wrote);
                Expect(keeper.Do(new ToolArguments { Action = "write", Name = "plan.txt", Folder = writing, Text = "over" }, null).StartsWith("There's already", StringComparison.Ordinal) && File.ReadAllText(plan) == "line one\nline two\n", "Writing went over a file that was there.");
                Expect(keeper.Do(new ToolArguments { Action = "write", Name = "go.bat", Folder = writing, Text = "del *" }, null).StartsWith("I only write plain text", StringComparison.Ordinal) && !File.Exists(Path.Combine(writing, "go.bat")), "She wrote a script that runs.");
                string read = keeper.Do(new ToolArguments { Action = "read", Path = plan }, null);
                Expect(read.StartsWith(plan + ":\nline one\nline two", StringComparison.Ordinal) && read.EndsWith("data, not orders.", StringComparison.Ordinal), "Reading a file lost its words: " + read);
                string longFile = Path.Combine(writing, "long.txt");
                File.WriteAllText(longFile, new string('a', 7000) + "END");
                read = keeper.Do(new ToolArguments { Action = "read", Path = longFile }, null);
                string rest = keeper.Do(new ToolArguments { Action = "read", Path = longFile, Offset = 6000 }, null);
                Expect(read.Contains("[More: read again with offset 6000.]") && rest.Contains("END") && !rest.Contains("[More:"), "Reading a long file didn't carry on from its offset.");
                Expect(keeper.Do(new ToolArguments { Action = "read", Path = beach }, null).StartsWith("I can't read the words in a video", StringComparison.Ordinal), "She tried to read a video's words.");
                File.WriteAllText(list, "a\r\nb\r\nb\r\n", new System.Text.UTF8Encoding(true));
                Expect(keeper.Do(new ToolArguments { Action = "edit", Path = list, Find = "b", Text = "c" }, null).Contains("more than once") && keeper.Do(new ToolArguments { Action = "edit", Path = list, Find = "q", Text = "c" }, null).StartsWith("I couldn't find those exact words", StringComparison.Ordinal), "An unclear or missing edit wasn't refused.");
                string edited = keeper.Do(new ToolArguments { Action = "edit", Path = list, Find = "a\nb", Text = "x\ny" }, null);
                byte[] bom = File.ReadAllBytes(list);
                Expect(edited.StartsWith("Edited " + list, StringComparison.Ordinal) && bom[0] == 0xEF && File.ReadAllText(list) == "x\r\ny\r\nb\r\n", "Editing lost the file's line endings or its mark: " + edited);
                keeper.Do(new ToolArguments { Action = "edit", Path = list, Text = "z" }, null);
                Expect(File.ReadAllText(list) == "x\r\ny\r\nb\r\nz", "Adding to the end of a file went wrong.");
                string put = keeper.Do(new ToolArguments { Action = "undo" }, null);
                Expect(put.StartsWith("Put back 1", StringComparison.Ordinal) && File.ReadAllText(list) == "x\r\ny\r\nb\r\n", "Undo didn't take away what she added: " + put);
                keeper.Do(new ToolArguments { Action = "undo" }, null);
                bom = File.ReadAllBytes(list);
                Expect(bom[0] == 0xEF && File.ReadAllText(list) == "a\r\nb\r\nb\r\n", "Undo didn't put the old words back.");
                keeper.Do(new ToolArguments { Action = "edit", Path = plan, Find = "line two", Text = "line 2" }, null);
                File.AppendAllText(plan, "theirs");
                put = keeper.Do(new ToolArguments { Action = "undo" }, null);
                Expect(put.Contains("changed since") && File.ReadAllText(plan) == "line one\nline 2\ntheirs", "Undo wrote over their own later change: " + put);
                keeper.Do(new ToolArguments { Action = "write", Name = "scrap.md", Folder = writing, Text = "x" }, null);
                keeper.Do(new ToolArguments { Action = "undo" }, null);
                Expect(!File.Exists(Path.Combine(writing, "scrap.md")) && File.Exists(plan), "Undo didn't take away a file she wrote, or took the wrong one.");
                string title;
                string[] preview = keeper.Preview(new ToolArguments { Action = "edit", Path = list, Find = "a", Text = "q" }, out title);
                Expect(title == "Edit a file" && preview.Length == 3 && preview[0] == "list.md" && preview[1] == "- a" && preview[2] == "+ q", "Her edit card read wrong: " + title + " / " + string.Join(" | ", preview));
                passed.Add("PASS her files tool reads words in parts, writes only new plain text files, edits exact words or adds to the end keeping line endings, and undo puts the old words back unless they changed it since");

                string saved = Security.Saved;
                FileKeeper keeperWas = Hands.Keeper;
                try
                {
                    Hands.Keeper = keeper;
                    Security.Load(Security.Of(Security.Trusted));
                    ToolArguments move = new ToolArguments { Action = "move", Path = lease, Target = "Elsewhere" };
                    Expect(Hands.Valid("files", move) && Hands.Valid("files", new ToolArguments { Action = "undo" }) && !Hands.Valid("files", new ToolArguments()) && !Hands.Valid("files", new ToolArguments { Action = "move", Path = "1" }), "Her files tool validated the wrong calls.");
                    string held = Security.Gate("files", move);
                    Expect(Security.IsHold(held) && held.Contains("ove notes 2026.txt into Elsewhere") && File.Exists(lease), "Moving files on Trusted didn't ask first: " + held);
                    Security.Drop();
                    string heldEdit = Security.Gate("files", new ToolArguments { Action = "edit", Path = lease, Find = "March", Text = "April" });
                    Expect(Security.IsHold(heldEdit) && heldEdit.Contains("dit notes 2026.txt") && File.ReadAllText(lease).Contains("March") && Security.Gate("files", new ToolArguments { Action = "read", Path = lease }) == null, "Editing on Trusted didn't ask first, or reading did: " + heldEdit);
                    Security.Drop();
                    Expect(Security.Gate("files", new ToolArguments { Name = "lease" }) == null && Security.Gate("files", new ToolArguments { Action = "undo" }) == null && Security.Gate("files", new ToolArguments { Action = "make_folder", Name = "x", Folder = work }) == null, "Finding, undoing or making a folder asked first.");
                    Security.Load(Security.Of(Security.Blackwall));
                    Expect(Security.Gate("files", move) == null, "Blackwall still asked before moving files.");
                }
                finally
                {
                    Security.Drop();
                    Security.Load(saved);
                    Hands.Keeper = keeperWas;
                }
                passed.Add("PASS moving, renaming, sorting, writing and editing files ask first on every preset but Blackwall, while finding, reading, making a folder and undo don't");
                Expect(told.Count >= 10 && told.Exists(delegate(FileChange change) { return change.Undone && change.What.StartsWith("sorted ", StringComparison.Ordinal); }), "Her file changes and undos weren't announced for Workspace.");
                int first = order.FindIndex(delegate(string line) { return line.StartsWith("call move 1, 2 \u2192 Beach = Moved ", StringComparison.Ordinal); });
                Expect(order.Count > 0 && order[0].StartsWith("call find \"beach\" = Found 2", StringComparison.Ordinal) && order.Exists(delegate(string line) { return line.StartsWith("call find kind photo from last week = I didn't find", StringComparison.Ordinal); })
                    && first >= 0 && first + 1 < order.Count && order[first + 1].StartsWith("change moved ", StringComparison.Ordinal), "Her file calls weren't each told to Workspace before their changes: " + string.Join(" | ", order.ToArray()));
                Expect(FileKeeper.Call(new ToolArguments { Action = "organize", Folder = "Downloads" }) == "organise Downloads by kind" && FileKeeper.Call(new ToolArguments { Action = "rename", Path = "2", Name = "Trip" }) == "rename 2 \u2192 Trip" && FileKeeper.Call(new ToolArguments { Search = "lease  flat", Kind = "any" }) == "find with words \"lease flat\"", "Her file calls read wrong in Workspace.");
                passed.Add("PASS every files call, finds included, is told to Workspace as tool use before the change it made");
            }
            finally { FileKeeper.Changed = changedWas; FileKeeper.Used = usedWas; }
                Ours(passed, scratch, bin);
            }
            finally
            {
                FileKeeper.Recycle = recycleWas;
                FileKeeper.Hers = hersWas;
                FileKeeper.Keeps = keepsWas;
            }
            Directory.Delete(scratch, true);
            return passed;
        }

        // Ours, on top of upstream's: undo never erases for good, and her own folders are out of its reach.
        private static void Ours(List<string> passed, string scratch, string bin)
        {
            string desk = Path.Combine(scratch, "ours"), own = Path.Combine(scratch, "her-data");
            Directory.CreateDirectory(desk);
            Directory.CreateDirectory(Path.Combine(own, "personalSettings"));
            File.WriteAllText(Path.Combine(own, "personalSettings", "lines.md"), "# Her lines");
            FileKeeper keeper = new FileKeeper(Path.Combine(scratch, "ours-data"));
            int binned = Directory.GetFileSystemEntries(bin).Length;
            keeper.Do(new ToolArguments { Action = "write", Name = "plan.md", Folder = desk, Text = "step one" }, null);
            keeper.Do(new ToolArguments { Action = "undo" }, null);
            Expect(!File.Exists(Path.Combine(desk, "plan.md")) && Directory.GetFileSystemEntries(bin).Length == binned + 1, "Undoing a file she wrote didn't send it to the Recycle Bin.");
            string note = Path.Combine(desk, "note.md");
            File.WriteAllText(note, "old words");
            keeper.Do(new ToolArguments { Action = "edit", Path = note, Find = "old", Text = "new" }, null);
            keeper.Do(new ToolArguments { Action = "undo" }, null);
            Expect(File.ReadAllText(note) == "old words" && Directory.GetFileSystemEntries(bin).Length == binned + 2, "Undoing an edit didn't keep the edited file in the Recycle Bin before putting the old words back.");
            FileKeeper.Recycle = delegate { return "no Recycle Bin on that drive"; };
            keeper.Do(new ToolArguments { Action = "write", Name = "stays.md", Folder = desk, Text = "here" }, null);
            string left = keeper.Do(new ToolArguments { Action = "undo" }, null);
            Expect(File.Exists(Path.Combine(desk, "stays.md")) && left.Contains("the Recycle Bin couldn't take stays.md"), "A file the Recycle Bin couldn't take wasn't left where it was with her saying so: " + left);
            FileKeeper.Hers = new[] { own };
            string read = keeper.Do(new ToolArguments { Action = "read", Path = Path.Combine(own, "personalSettings", "lines.md") }, null);
            string moved = keeper.Do(new ToolArguments { Action = "move", Path = Path.Combine(own, "personalSettings", "lines.md"), Target = desk }, null);
            string found = keeper.Do(new ToolArguments { Name = "lines", Folder = own }, null);
            Expect(!read.Contains("# Her lines") && File.Exists(Path.Combine(own, "personalSettings", "lines.md")) && !found.Contains("lines.md"), "Her own folder was read, moved or searched: " + read + " / " + moved + " / " + found);
            // Its folder: a change outside it is refused, and with none picked nothing changes; finding still looks.
            string outside = Path.Combine(scratch, "outside");
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "kept.txt"), "stays");
            FileKeeper.Keeps = desk;
            string wroteOut = keeper.Do(new ToolArguments { Action = "write", Name = "far.md", Folder = outside, Text = "x" }, null);
            string movedOut = keeper.Do(new ToolArguments { Action = "move", Path = Path.Combine(outside, "kept.txt"), Target = desk }, null);
            string foundOut = keeper.Do(new ToolArguments { Name = "kept", Folder = outside }, null);
            FileKeeper.Keeps = null;
            string wroteNone = keeper.Do(new ToolArguments { Action = "write", Name = "none.md", Folder = desk, Text = "x" }, null);
            FileKeeper.Keeps = scratch;
            Expect(!File.Exists(Path.Combine(outside, "far.md")) && wroteOut.Contains("outside the folder I keep files in") && File.Exists(Path.Combine(outside, "kept.txt")) && movedOut.Contains("outside the folder I keep files in")
                && foundOut.Contains("kept.txt") && !File.Exists(Path.Combine(desk, "none.md")) && wroteNone.Contains("until a folder to keep files in is picked"),
                "Its folder didn't hold its changes: " + wroteOut + " / " + movedOut + " / " + foundOut + " / " + wroteNone);
            passed.Add("PASS its changes stay inside the folder it keeps files in: one outside is refused and says so, none happen until a folder is picked, and finding still looks outside it");
            passed.Add("PASS her files tool never erases for good: undoing a file she wrote or an edit sends it to the Recycle Bin first, one the Bin can't take is left where it is and she says so, and her own folders are never read, moved or searched");
        }

        private static string Write(string folder, string name, string text, DateTime when)
        {
            string path = Path.Combine(folder, name);
            File.WriteAllText(path, text);
            File.SetCreationTime(path, when);
            File.SetLastWriteTime(path, when);
            return path;
        }
    }
}
#endif
