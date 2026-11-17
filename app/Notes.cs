using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // Keeps the "> " notes at the top of her editable files matching the build, without touching a single line the
    // person has written.
    //
    // Her files are written once and then left alone, so a file made by an older build keeps that build's notes for
    // ever. Those notes are instructions, so stale ones describe settings that have moved or no longer exist.
    internal static class Notes
    {
        // A top up, a move or a carried-over number that fails and says nothing leaves them on the old file
        // for ever. That is how the mood retune sat dead for a hundred builds: the one thing that would have
        // said so was a catch with an empty body. Every migration says so now, in her log, with what threw.
        internal static void Failed(string what, Exception bad)
        {
            try { Brain.Log("files", -1, what + " could not finish: " + bad.GetType().Name + ": " + bad.Message); }
            catch (Exception) { }
        }

        // Replaces every "> " note in the file with the ones the build ships, section by section. Anything that is
        // not a note is left exactly as it was, in its original order.

        internal const string ResetKey = "reset to stock";

        private static readonly Dictionary<string, string> stocked = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static void Seen(string path)
        {
            try
            {
                FileInfo about = new FileInfo(path);
                if (!about.Exists) return;
                lock (stocked) stocked[path] = about.LastWriteTimeUtc.Ticks + ":" + about.Length;
            }
            catch (Exception) { }
        }

        // What was last written into one of her files, by hash, kept beside them so an untouched file can be
        // told from an edited one without a history lookup. An untouched file needs no spare at all, which
        // is what stops her folder filling with copies of text nobody changed.
        internal const string ShippedName = ".shipped";

        private static string ShippedPath(string path)
        {
            string folder = Path.GetDirectoryName(path);
            return string.IsNullOrEmpty(folder) ? null : Path.Combine(folder, ShippedName);
        }

        private static string Hash(string text)
        {
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            {
                // The line ends are not the content. A file written LF here and read back CRLF after an
                // editor touched nothing but the endings is still theirs untouched.
                byte[] of = sha.ComputeHash(new UTF8Encoding(false).GetBytes((text ?? "").Replace("\r\n", "\n").Replace('\r', '\n')));
                StringBuilder made = new StringBuilder(of.Length * 2);
                foreach (byte one in of) made.Append(one.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                return made.ToString();
            }
        }

        private static Dictionary<string, string> ShippedList(string path)
        {
            Dictionary<string, string> had = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string list = ShippedPath(path);
                if (list == null || !File.Exists(list)) return had;
                foreach (string row in File.ReadAllLines(list))
                {
                    int tab = row.IndexOf('\t');
                    if (tab > 0) had[row.Substring(0, tab).Trim()] = row.Substring(tab + 1).Trim();
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return had;
        }

        // Called the moment we write stock content into one of her files, so the record is what she was
        // given rather than what she has now.
        internal static void Shipped(string path, string text)
        {
            try
            {
                string list = ShippedPath(path);
                if (list == null) return;
                Dictionary<string, string> had = ShippedList(path);
                had[Path.GetFileName(path)] = Hash(text);
                List<string> rows = new List<string>();
                foreach (KeyValuePair<string, string> one in had) rows.Add(one.Key + "\t" + one.Value);
                rows.Sort(StringComparer.OrdinalIgnoreCase);
                File.WriteAllText(list, string.Join(Environment.NewLine, rows.ToArray()) + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        // Whether the file is still exactly what was written into it. Unknown counts as theirs: a file from
        // a build before this record existed has no entry, and guessing untouched there loses their edits.
        internal static bool Untouched(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
                string was;
                if (!ShippedList(path).TryGetValue(Path.GetFileName(path), out was) || string.IsNullOrEmpty(was)) return false;
                return string.Equals(was, Hash(File.ReadAllText(path)), StringComparison.OrdinalIgnoreCase);
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        // How many spares of one of her files are kept. The same three the updater keeps of her programs, and
        // the same numbering, so the two read the same way to somebody looking in the folder.
        internal const int MostSpares = 3;

        // Their file kept before a reset to stock overwrites it, numbered past the highest there, newest
        // three kept and the oldest dropped by write time. Same rules as Setup.Spare uses for her programs.
        // Best effort: a spare that will not write must not stop the reset they asked for.
        internal static string Spare(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                // Nothing of theirs in it, so nothing to keep. This is what stops her folder filling with
                // copies of text nobody ever changed.
                if (Untouched(path)) return null;
                string spare = path + ".bak";
                if (File.Exists(spare))
                {
                    int highest = 1;
                    foreach (string found in Spares(path))
                    {
                        string tail = Path.GetFileName(found).Substring(Path.GetFileName(path).Length + 4);
                        int number;
                        if (tail.Length == 0) number = 1;
                        else if (!int.TryParse(tail, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out number)) continue;
                        if (number > highest) highest = number;
                    }
                    spare = path + ".bak" + (highest + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                File.Copy(path, spare, true);
                Prune(path);
                return spare;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        // Every spare of one file: name.bak, name.bak2 and so on, and nothing else that happens to start the same.
        private static List<string> Spares(string path)
        {
            List<string> found = new List<string>();
            try
            {
                string folder = Path.GetDirectoryName(path), name = Path.GetFileName(path);
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return found;
                foreach (string one in Directory.GetFiles(folder, name + ".bak*"))
                {
                    string tail = Path.GetFileName(one).Substring(name.Length + 4);
                    int number;
                    if (tail.Length == 0 || int.TryParse(tail, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out number)) found.Add(one);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return found;
        }

        internal static string[] SparesForTest(string path) { return Spares(path).ToArray(); }

        // The oldest go until three are left, by write time.
        private static void Prune(string path)
        {
            try
            {
                List<string> spares = Spares(path);
                if (spares.Count <= MostSpares) return;
                spares.Sort(delegate(string a, string b) { return File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)); });
                for (int at = MostSpares; at < spares.Count; at++)
                {
                    try { File.Delete(spares[at]); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (Exception) { }
        }

        // A way back to the file we ship, without anyone copying anything. Every one of her files carries a line
        // near the top; set it to 1 and the file is written fresh next time she reads it, with the line back at 0.
        // Done here rather than in each file's own code so they all behave the same way and none gets forgotten.
        internal static bool Stock(string path, string fresh)
        {
            return Stock(path, delegate { return fresh; });
        }

        // The stock text is asked for only once the file is known to have changed. Building it is not free:
        // it is a whole personality pack out of the table, and handing it in as a string meant building one
        // for every file on every pass and throwing nearly all of them away.
        internal static bool Stock(string path, Func<string> fresh)
        {
            if (string.IsNullOrEmpty(path) || fresh == null) return false;
            try
            {
                FileInfo about = new FileInfo(path);
                if (!about.Exists) return false;
                // Only a file that has actually changed is opened. This is checked for every personality and every
                // mood on a timer, and reading all of them each time is work for nothing: the flag can only have
                // moved if the file was written.
                string when = about.LastWriteTimeUtc.Ticks + ":" + about.Length;
                lock (stocked)
                {
                    string before;
                    if (stocked.TryGetValue(path, out before) && before == when) return false;
                }
                string stock = fresh();
                if (string.IsNullOrEmpty(stock)) return false;
                string had = File.ReadAllText(path);
                // The carriage return. Her files are CRLF and an end of line anchor will not pass it, so this
                // matched nothing in a real file: somebody wrote 1, she rewrote the file with the same content
                // and the line stayed at 1, and reset to stock had never once worked outside a test.
                Match line = Regex.Match(had, @"(?mi)^[ \t]*" + Regex.Escape(ResetKey) + @"[ \t]*:[ \t]*(\S+)[ \t\r]*$");
                if (line.Success)
                {
                    string said = line.Groups[1].Value.Trim();
                    if (said != "1" && !said.Equals("yes", StringComparison.OrdinalIgnoreCase) && !said.Equals("true", StringComparison.OrdinalIgnoreCase)) { Seen(path); return false; }
                    // Theirs kept before the stock one lands on top of it. This is the only place in her files
                    // where work is thrown away rather than added to, so it is the one that needed a way back.
                    Spare(path);
                    string put = Marked(stock);
                    File.WriteAllText(path, put, new UTF8Encoding(false));
                    // What she was just given, so the next check knows this file is untouched until they
                    // touch it.
                    Shipped(path, put);
                    Seen(path);
                    return true;
                }
                // No line yet, so one is added. Their file is untouched otherwise.
                File.WriteAllText(path, Marked(had), new UTF8Encoding(false));
                Seen(path);
                return false;
            }
            catch (Exception) { return false; }
        }

        // The line goes under the first heading, where it is visible without being in the way of anything parsed.
        private static string Marked(string text)
        {
            if (Regex.IsMatch(text, @"(?mi)^[ \t]*" + Regex.Escape(ResetKey) + @"[ \t]*:")) return text;
            string note = ResetKey + ": 0" + Environment.NewLine
                + "> Set that to 1 and this file goes back to the one she ships with, next time she reads it." + Environment.NewLine;
            Match head = Regex.Match(text, @"(?m)^#[^\r\n]*\r?\n");
            if (!head.Success) return note + Environment.NewLine + text;
            int at = head.Index + head.Length;
            return text.Substring(0, at) + note + text.Substring(at);
        }

        // That a reset to stock leaves them a way back, that only three are kept, and that the oldest goes by
        // when it was written rather than by the number in its name.
        internal static void SpareCheck(System.Collections.Generic.List<string> checks, string folder)
        {
            string path = Path.Combine(folder, "lines.md");
            string stock = "# Her lines" + Environment.NewLine + Environment.NewLine + "## hello" + Environment.NewLine + "hey" + Environment.NewLine;
            Func<string> fresh = delegate { return stock; };

            // Four resets, each on a file holding something of theirs, so four spares are asked for and the
            // first of them has to go. Each new spare is stamped later than the one before it, since four
            // copies made inside the same second cannot be told apart by write time.
            List<string> made = new List<string>();
            for (int round = 1; round <= 4; round++)
            {
                File.WriteAllText(path, "# Her lines" + Environment.NewLine + ResetKey + ": 1" + Environment.NewLine
                    + Environment.NewLine + "## hello" + Environment.NewLine + "theirs " + round + Environment.NewLine, new UTF8Encoding(false));
                Seen(path);
                lock (stocked) stocked.Remove(path);
                List<string> had = new List<string>(Spares(path));
                // Stock does the sparing; this only watches which one it made.
                if (!Stock(path, fresh)) throw new Exception("Reset to stock did not run on round " + round + ".");
                string fresher = null;
                foreach (string one in Spares(path)) if (!had.Contains(one)) fresher = one;
                if (fresher == null) throw new Exception("Round " + round + " of the reset kept no spare.");
                // Backwards from now, so each round's spare is newer than the one before it. Stamping them
                // forwards puts every older spare in the future, and then the one just made reads as the
                // oldest and prunes itself, which is what this check did on its first run.
                File.SetLastWriteTimeUtc(fresher, DateTime.UtcNow.AddMinutes(round - 10));
                made.Add(fresher);
            }
            // The first one made is the one that should have gone. Pruning runs as each spare is made, so by
            // the fourth there are three left and the oldest of the four is not among them.
            if (File.Exists(made[0])) throw new Exception("The oldest spare was kept: " + made[0]);

            List<string> left = Spares(path);
            if (left.Count != MostSpares) throw new Exception("A reset kept " + left.Count + " spares rather than " + MostSpares + ": " + string.Join(", ", left.ToArray()));
            if (File.ReadAllText(path).IndexOf("theirs", StringComparison.Ordinal) >= 0) throw new Exception("Reset to stock left their words in the file.");

            // Their words are in the spares, which is the whole point: a way back.
            bool found = false;
            foreach (string one in left) if (File.ReadAllText(one).IndexOf("theirs", StringComparison.Ordinal) >= 0) found = true;
            if (!found) throw new Exception("Not one spare held what they had written.");

            // A file nothing has overwritten gets no spare, and a missing one is not an error.
            string untouched = Path.Combine(folder, "untouched.md");
            File.WriteAllText(untouched, "# Nothing" + Environment.NewLine, new UTF8Encoding(false));
            if (Spares(untouched).Count != 0) throw new Exception("A file nothing overwrote already had a spare.");
            if (Spare(Path.Combine(folder, "not-here.md")) != null) throw new Exception("Sparing a file that is not there did not come back empty handed.");

            // The hash half: a file nobody has touched gets no spare, and an unknown file counts as theirs.
            string fresh2 = Path.Combine(folder, "fresh.md");
            string given = "# Fresh" + Environment.NewLine + "## hello" + Environment.NewLine + "hey" + Environment.NewLine;
            File.WriteAllText(fresh2, given, new UTF8Encoding(false));
            Shipped(fresh2, given);
            if (!Untouched(fresh2)) throw new Exception("A file still exactly as it was written did not read as untouched.");
            if (Spare(fresh2) != null || Spares(fresh2).Count != 0) throw new Exception("A file with nothing of theirs in it was spared anyway.");

            // Line ends alone are not an edit: an editor that rewrites CRLF has changed nothing of theirs.
            File.WriteAllText(fresh2, given.Replace(Environment.NewLine, "\n"), new UTF8Encoding(false));
            if (!Untouched(fresh2)) throw new Exception("Changing only the line ends read as an edit of theirs.");

            File.WriteAllText(fresh2, given + "their line" + Environment.NewLine, new UTF8Encoding(false));
            if (Untouched(fresh2)) throw new Exception("A file they added a line to still read as untouched.");
            if (Spare(fresh2) == null) throw new Exception("A file they had edited was not kept.");

            // No record at all is the dangerous case: every file written before this existed has none, and
            // guessing untouched there would throw away the edits of everybody already using her.
            string older = Path.Combine(folder, "older.md");
            File.WriteAllText(older, "# From an older build" + Environment.NewLine, new UTF8Encoding(false));
            if (Untouched(older)) throw new Exception("A file with no record read as untouched, which would discard their edits.");
            if (Spare(older) == null) throw new Exception("A file with no record was not kept.");

            checks.Add("PASS a file she was given and nobody touched gets no spare at all, which is what keeps her folder from filling with copies of text nobody changed; line ends alone are not an edit; and a file with no record counts as theirs, so a build from before the record existed cannot lose anyone's work");
            checks.Add("PASS a reset to stock keeps their file beside it first, so an evening's work has a way back: the newest " + MostSpares + " spares are kept, numbered past the highest, the oldest goes by when it was written rather than by the number in its name, and a file nothing overwrites never gets one");
        }

        // Writing one of her files, skipped when the file already says exactly that. Her passes run on every
        // start and most of them have nothing to do on most of them, so without this they rewrite the same
        // bytes over and over: her watcher sees a change, rereads, and the file's time says it was edited
        // when nobody touched it. Comparing the whole text is the only honest test, since a pass that only
        // normalises line ends is a real change and has to go through.
        internal static void Put(string path, string text)
        {
            try
            {
                if (File.Exists(path) && File.ReadAllText(path) == text) return;
            }
            catch (Exception) { }
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        internal static void Refresh(string path, string fresh)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(fresh)) return;
            try
            {
                if (!File.Exists(path)) return;
                string[] had = File.ReadAllLines(path);
                // The reset line and its note go in after the fresh text is built, so the fresh text does not
                // carry that note and a refresh dropped it. The setting was then left in the file with nothing
                // saying what it does. Only for a file that has the line: the rest never had one.
                string authority = fresh;
                foreach (string raw in had)
                    if (Regex.IsMatch((raw ?? "").Trim(), @"^" + Regex.Escape(ResetKey) + @"[ \t]*:", RegexOptions.IgnoreCase))
                    { authority = Marked(fresh); break; }
                Dictionary<string, List<string>> mine = Noted(authority.Replace("\r\n", "\n").Split('\n'));

                StringBuilder made = new StringBuilder();
                string under = "";
                bool wrote = false, changed = false, settings = false;
                foreach (string raw in had)
                {
                    string one = (raw ?? "").Trim();
                    if (one.StartsWith("#"))
                    {
                        under = one.TrimStart('#').Trim();
                        wrote = false;
                        settings = false;
                        made.AppendLine(raw);
                        continue;
                    }
                    // In some sections a ">" line is the setting rather than a note: an intro with ">" in front
                    // of it is switched off. Those sit under the section's own lines, so once a line that is not
                    // a note has gone by, every ">" after it is left exactly as it is. The note block at the top
                    // still gets her current wording.
                    if (one.Length > 0 && !one.StartsWith(">")) settings = true;
                    if (one.StartsWith(">") && !(settings && Keeps(under)))
                    {
                        // Their notes go; ours take their place once, where theirs began.
                        changed = true;
                        if (wrote) continue;
                        wrote = true;
                        List<string> ours;
                        if (mine.TryGetValue(under, out ours)) foreach (string line in ours) made.AppendLine(line);
                        continue;
                    }
                    made.AppendLine(raw);
                }
                if (!changed) return;
                string done = made.ToString();
                if (done == string.Join(Environment.NewLine, had) + Environment.NewLine) return;
                File.WriteAllText(path, done, new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        // Sections where a ">" line is data rather than a note, and has to survive a refresh.
        // Sections where a ">" line is data rather than a note, and has to survive a refresh. Her check-ins
        // are in here because a bullet with ">" in front is that check-in line switched off, the same
        // convention her intros use, and a refresh would otherwise read it as a note and take it away.
        private static readonly string[] Commented =
        {
            "Her start-up intro", "Water", "Stretch", "Look away", "Sleep nudge", "Meals",
            "Game break", "Bored", "Bored noticed", "Bored ignored",
        };

        private static bool Keeps(string under)
        {
            foreach (string one in Commented)
                if (string.Equals(under, one, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static Dictionary<string, List<string>> Noted(string[] lines)
        {
            Dictionary<string, List<string>> found = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            string under = "";
            bool settings = false;
            foreach (string raw in lines)
            {
                string one = (raw ?? "").Trim();
                if (one.StartsWith("#")) { under = one.TrimStart('#').Trim(); settings = false; continue; }
                // Same rule the writer uses: in a section whose ">" lines are settings, only the block above
                // the first real line counts as notes. Without this her own switches would be read as notes
                // and written back a second time.
                if (one.Length > 0 && !one.StartsWith(">")) settings = true;
                if (settings && Keeps(under)) continue;
                if (!one.StartsWith(">")) continue;
                List<string> had;
                if (!found.TryGetValue(under, out had)) { had = new List<string>(); found[under] = had; }
                had.Add(raw.TrimEnd());
            }
            return found;
        }
    }
}
