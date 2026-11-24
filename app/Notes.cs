using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // Keeps the "> " notes at the top of her files matching the build, without touching a line the person wrote, so a
    // file from an older build never keeps stale instructions.
    internal static class Notes
    {
        // A migration that fails silently leaves them on the old file for ever, so every one reports in her log what
        // threw.
        internal static void Failed(string what, Exception bad)
        {
            try { Brain.Log("files", -1, what + " could not finish: " + bad.GetType().Name + ": " + bad.Message); }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
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
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // What was last written into one of her files, by hash, kept beside them so an untouched file can be
        // told from an edited one without a history lookup. An untouched file needs no spare at all, which
        // is what stops her folder filling with copies of text nobody changed.
        internal const string ShippedName = ".shipped";

        // Whether a file in a pack folder is one of her packs: not a backup, a .txt note or her own bookkeeping like
        // .shipped. A dotted name has nothing left once its type is off, so it isn't listed as a blank personality.
        internal static bool IsPack(string file)
        {
            string name = Path.GetFileName(file ?? "");
            return name.Length > 0 && name[0] != '.' && !name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
        }

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
                Overwrite(list, string.Join(Environment.NewLine, rows.ToArray()) + Environment.NewLine);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        // Whether .shipped holds this text for the name, for a record kept beside a file rather than of it.
        internal static bool Stamped(string path, string text)
        {
            try
            {
                string was;
                return !string.IsNullOrEmpty(path) && ShippedList(path).TryGetValue(Path.GetFileName(path), out was) && string.Equals(was, Hash(text), StringComparison.OrdinalIgnoreCase);
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        // Whether there is a record at all of what was last written into the file.
        internal static bool Recorded(string path)
        {
            try { return !string.IsNullOrEmpty(path) && ShippedList(path).ContainsKey(Path.GetFileName(path)); }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
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
                return string.Equals(was, Hash(ReadAll(path)), StringComparison.OrdinalIgnoreCase);
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
                string spare = SpareFiles.Name(path);
                File.Copy(path, spare, true);
                SpareFiles.Prune(path, MostSpares);
                return spare;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        // Every spare of one file, timed or from before spares had a time.
        private static List<string> Spares(string path) { return SpareFiles.Of(path); }

        internal static string[] SparesForTest(string path) { return Spares(path).ToArray(); }

        // A way back to the shipped file: every file has a reset line near the top; set it to 1 and she writes the file
        // fresh on her next read, with the line back at 0. Done here so every file behaves alike.
        internal static bool Stock(string path, string fresh)
        {
            return Stock(path, delegate { return fresh; });
        }

        // The stock text is built only once the file is known to have changed, since building it means a whole
        // personality pack.
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
                string had = ReadAll(path);
                // The carriage return: her files are CRLF and an end-of-line anchor won't pass it, so the pattern
                // allows it, or reset to stock would never match in a real file.
                Match line = Regex.Match(had, @"(?mi)^[ \t]*" + Regex.Escape(ResetKey) + @"[ \t]*:[ \t]*(\S+)[ \t\r]*$");
                if (line.Success)
                {
                    string said = line.Groups[1].Value.Trim();
                    if (said != "1" && !said.Equals("yes", StringComparison.OrdinalIgnoreCase) && !said.Equals("true", StringComparison.OrdinalIgnoreCase)) { Seen(path); return false; }
                    // Theirs kept before the stock one lands on top of it. This is the only place in her files
                    // where work is thrown away rather than added to, so it is the one that needed a way back.
                    Spare(path);
                    string put = Marked(stock);
                    Overwrite(path, put);
                    // What she was just given, so the next check knows this file is untouched until they
                    // touch it.
                    Shipped(path, put);
                    Seen(path);
                    return true;
                }
                // No line yet, so one is added. Their file is untouched otherwise, and one still as she wrote it stays so.
                bool was = Untouched(path);
                string marked = Marked(had);
                Overwrite(path, marked);
                if (was) Shipped(path, marked);
                Seen(path);
                return false;
            }
            catch (Exception) { return false; }
        }

        // The line goes under the first heading, where it is visible without being in the way of anything parsed.
        internal static string Marked(string text)
        {
            if (Regex.IsMatch(text, @"(?mi)^[ \t]*" + Regex.Escape(ResetKey) + @"[ \t]*:")) return text;
            string note = ResetKey + ": 0" + Environment.NewLine
                + ">! Set that to 1 and this file goes back to the one she ships with, next time she reads it." + Environment.NewLine;
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

        // Writes one of her files only when its text differs, so passes with nothing to do leave it and its time alone.
        // Hidden or visible as it was, since Windows won't replace a hidden file outright.
        internal static void Overwrite(string path, string text)
        {
            FileAttributes was = File.Exists(path) ? File.GetAttributes(path) : FileAttributes.Normal;
            bool hidden = (was & FileAttributes.Hidden) != 0;
            if (hidden) File.SetAttributes(path, was & ~FileAttributes.Hidden);
            try { WriteAll(path, text); }
            finally { if (hidden) File.SetAttributes(path, was); }
        }

        // Read whole without standing in a writer's way, and waiting out a moment when a write has it. Her folder
        // watcher and her startup check read her files on their own threads while her packs are being written, and
        // a plain read both refused those writes and failed on them.
        internal static string ReadAll(string path)
        {
            for (int tries = 0; ; tries++)
            {
                try
                {
                    using (FileStream open = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (StreamReader reader = new StreamReader(open, Encoding.UTF8, true))
                        return reader.ReadToEnd();
                }
                catch (FileNotFoundException) { throw; }
                catch (DirectoryNotFoundException) { throw; }
                catch (IOException)
                {
                    if (tries >= 100) throw;
                    System.Threading.Thread.Sleep(50);
                }
            }
        }

        internal static string[] ReadLines(string path)
        {
            string text = ReadAll(path);
            if (text.EndsWith("\r\n", StringComparison.Ordinal)) text = text.Substring(0, text.Length - 2);
            else if (text.EndsWith("\n", StringComparison.Ordinal)) text = text.Substring(0, text.Length - 1);
            return text.Length == 0 ? new string[0] : text.Replace("\r\n", "\n").Split('\n');
        }

        // Written whole, waiting a few seconds while another program (a virus scan, an editor) holds the file just
        // after she wrote it.
        internal static void WriteAll(string path, string text)
        {
            for (int tries = 0; ; tries++)
            {
                try { File.WriteAllText(path, text, new UTF8Encoding(false)); return; }
                catch (IOException)
                {
                    if (tries >= 100 || !File.Exists(path)) throw;
                    System.Threading.Thread.Sleep(50);
                }
            }
        }

        internal static void Put(string path, string text)
        {
            try
            {
                if (File.Exists(path) && ReadAll(path) == text) return;
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            Overwrite(path, text);
        }

        internal static void Refresh(string path, string fresh)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(fresh)) return;
            try
            {
                if (!File.Exists(path)) return;
                string[] had = File.ReadAllLines(path);
                // The reset line and its note go in after the fresh text is built, so a refresh keeps the note that
                // says what the setting does. Only for a file that has the line.
                string authority = fresh;
                foreach (string raw in had)
                    if (Regex.IsMatch((raw ?? "").Trim(), @"^" + Regex.Escape(ResetKey) + @"[ \t]*:", RegexOptions.IgnoreCase))
                    { authority = Marked(fresh); break; }
                // A file with copies keeps its notes in English, and its copy has them in their language (LangCopies); any
                // other file has them in their language in place.
                string[] shipped = authority.Replace("\r\n", "\n").Split('\n');
                Dictionary<string, List<string>> mine = Noted(LangCopies.Knows(path) ? shipped : InTheirLanguage(shipped));
                HashSet<string> moved = MarkedSections(had);

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
                    if (one.Length > 0 && !one.StartsWith(">")) settings = true;
                    if (IsNote(one, under, settings, moved))
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
                string done = made.ToString();
                if (changed && done != string.Join(Environment.NewLine, had) + Environment.NewLine) Overwrite(path, done);
                // Its copy in their language follows.
                string code = LangCopies.Knows(path) ? LangCopies.Code() : null;
                if (code != null) LangCopies.Lay(path, code);
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // Sections where a ">" line is data rather than a note and survives a refresh. Check-ins are here because a ">"
        // bullet is a check-in switched off.
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

        // A note of hers starts ">!"; a plain ">" line is switched off and never a note. A section with no ">!" yet is
        // read the old way once, and its notes come back as ">!".
        internal const string NoteMark = ">!";

        // Her old ">" notes marked ">!" in files no refresh reaches: twitch.md, me.md, memories.md, the unused side,
        // her packs. In an unmarked section every ">" line is a note, but in data sections only the block above the
        // first real line.
        internal static bool Remark(string path) { return Remark(path, null); }

        // offered: a "> " line it names is an option switched off, not a note, and so is every ">" line after it in its
        // section, since a list switched off is written as one run under its notes.
        internal static bool Remark(string path, Func<string, bool> offered)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
                string text = ReadAll(path);
                string newline = text.Contains("\r\n") ? "\r\n" : "\n";
                string[] rows = text.Replace("\r\n", "\n").Split('\n');
                HashSet<string> marked = MarkedSections(rows);
                string under = "";
                bool settings = false, changed = false, options = false;
                for (int at = 0; at < rows.Length; at++)
                {
                    string one = (rows[at] ?? "").Trim();
                    if (one.StartsWith("#", StringComparison.Ordinal)) { under = one.TrimStart('#').Trim(); settings = false; options = false; continue; }
                    if (offered != null && one.Length > 1 && one[0] == '>' && !one.StartsWith(NoteMark, StringComparison.Ordinal) && offered(one.Substring(1).Trim())) options = true;
                    if (options && one.StartsWith(">", StringComparison.Ordinal) && one != ">") continue;
                    if (one.Length > 0 && !one.StartsWith(">", StringComparison.Ordinal)) settings = true;
                    if (!one.StartsWith(">", StringComparison.Ordinal) || one.StartsWith(NoteMark, StringComparison.Ordinal)) continue;
                    // A bare ">" is a spacer in a block of notes wherever it is: nobody switches off an empty line. Left
                    // as ">" in a marked section, a refresh read it as theirs and left it under the notes it was spacing.
                    bool spacer = one == ">";
                    if (!spacer && (marked.Contains(under) || settings && Keeps(under))) continue;
                    if (Regex.IsMatch(one, @"^>\s*(?:you|sys)\s*:", RegexOptions.IgnoreCase)) continue;
                    int mark = rows[at].IndexOf('>');
                    rows[at] = rows[at].Substring(0, mark) + NoteMark + rows[at].Substring(mark + 1);
                    changed = true;
                }
                if (!changed) return false;
                Put(path, string.Join(newline, rows));
                return true;
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); return false; }
        }

        // The old marks moved over in every kind of file of hers: shared ones, both sides, packs; switched-off options, a
        // section already marked and "> you:" left as they are; line ends kept; a second run writes nothing.
        internal static void RemarkCheck(System.Collections.Generic.List<string> checks, string folder)
        {
            string root = Path.Combine(folder, "remark-" + Guid.NewGuid().ToString("N").Substring(0, 6), "personalSettings");
            string[,] files =
            {
                { "twitch.md", "# Twitch\r\n\r\n> Read fresh every message, so edit it while she runs.\r\n>\r\n>   turn off twitch live notifications\r\n\r\n## Channel\r\n\r\n> Your channel name.\r\nchannel: \r\n",
                  "# Twitch\r\n\r\n>! Read fresh every message, so edit it while she runs.\r\n>!\r\n>!   turn off twitch live notifications\r\n\r\n## Channel\r\n\r\n>! Your channel name.\r\nchannel: \r\n" },
                { "censored\\sys.md", "# Her system lines\n\n## Her start-up intro\n\n> How she turns up. Take the \">\" off one to use it.\n\nintros: door\n> shatter\n> hijack\n\n## Lines\n\n> Change the text on the right.\nhello: hi\n> you: stays as it is\n",
                  "# Her system lines\n\n## Her start-up intro\n\n>! How she turns up. Take the \">\" off one to use it.\n\nintros: door\n> shatter\n> hijack\n\n## Lines\n\n>! Change the text on the right.\nhello: hi\n> you: stays as it is\n" },
                { "interests\\me.md", "# Me\n> She reads this every time you chat.\n\n## About you\n\n>! Already the new way.\n>\n>! Spaced by a bare one.\n> a line they switched off\nname: Sam\n",
                  "# Me\n>! She reads this every time you chat.\n\n## About you\n\n>! Already the new way.\n>!\n>! Spaced by a bare one.\n> a line they switched off\nname: Sam\n" },
                { "personalities\\censored\\Bratty.md", "# Bratty\n> Contrary on purpose.\n\n## Pet names\n\n> What she calls them.\nbrat\n", "# Bratty\n>! Contrary on purpose.\n\n## Pet names\n\n>! What she calls them.\nbrat\n" },
                { "check-ins.md", "# Check-ins\n\n## Water\n\n> Her water nudges.\n- Drink some water.\n> - A nudge they switched off.\n", "# Check-ins\n\n## Water\n\n>! Her water nudges.\n- Drink some water.\n> - A nudge they switched off.\n" },
                { "attitudes\\interests\\pack3", "# Pack\r\n> A note.\r\n\r\n## first\r\n\r\n> Its note.\r\none\r\n> A later note.\r\n", "# Pack\r\n>! A note.\r\n\r\n## first\r\n\r\n>! Its note.\r\none\r\n>! A later note.\r\n" },
                { "interests\\" + Interests.HersName, "# List\n\n## list\n\n> Its note.\n>\n> Another note.\n> Zz offered one\n> their own switched off\n> Zz offered two\nlive one\n\n## after\n\n> A note again.\nx\n", "# List\n\n## list\n\n>! Its note.\n>!\n>! Another note.\n> Zz offered one\n> their own switched off\n> Zz offered two\nlive one\n\n## after\n\n>! A note again.\nx\n" },
            };
            string[] moreWas = Interests.SenpaiMore;
            Interests.SenpaiMore = new[] { "Zz offered one", "Zz offered two" };
            for (int i = 0; i < files.GetLength(0); i++)
            {
                string path = Path.Combine(root, files[i, 0]);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, files[i, 1], new UTF8Encoding(false));
            }
            int first, second;
            try { first = RemarkAll(root); second = RemarkAll(root); }
            finally { Interests.SenpaiMore = moreWas; }
            for (int i = 0; i < files.GetLength(0); i++)
            {
                string got = File.ReadAllText(Path.Combine(root, files[i, 0]));
                if (got != files[i, 2]) throw new Exception(files[i, 0] + " wasn't marked the new way as it should be:\n" + got);
            }
            if (first != 7 || second != 0) throw new Exception("The marking pass wrote " + first + " files, then " + second + " on a second run.");
            checks.Add("PASS her old \">\" notes become \">!\" in every file of hers, shared ones, both sides and packs: options switched off, a section already marked and \"> you:\" stay, a bare \">\" spacer becomes \">!\" even in a marked section, line ends are kept, and a second run writes nothing; her packs and interests files with no extension too, where her own list switched off under its notes stays \">\"");
            TranslateCheck(checks, folder);
        }

        // A file in another language: only ">!" note words change. Headings, settings, lists and indented examples stay byte
        // for byte, and a menu path in a note is her menu as it shows, not the translator's wording.
        internal static void TranslateCheck(System.Collections.Generic.List<string> checks, string folder)
        {
            Func<string, string> translateWas = Translate, menuWas = MenuName;
            string path = Path.Combine(folder, "translated-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".md");
            string stock = string.Join("\r\n", new[]
            {
                "# Twitch", "", "## What you can say to her", "", ">! Spoken or typed, read without her brain.", ">!", ">!   turn off twitch live notifications", ">!   stop telling me who's live", "",
                "## Reading chat", "", ">! Chat reading is in Twitch > Read chat. Settings are here.", "", "channel: ", "emotes: Kappa, PogChamp", "",
                "## Lines", "", ">! Tokens: %petname% %name%", "",
                "## Banned words", "", "badword", "",
            });
            try
            {
                File.WriteAllText(path, stock, new UTF8Encoding(false));
                List<string> asked = new List<string>();
                Translate = delegate(string line) { lock (asked) asked.Add(line); return "[pl] " + line; };
                MenuName = delegate(string name) { return "<" + name + ">"; };
                Refresh(path, stock);
                string[] made = File.ReadAllText(path).Split(new[] { "\r\n" }, StringSplitOptions.None);
                string[] was = stock.Split(new[] { "\r\n" }, StringSplitOptions.None);
                if (made.Length != was.Length) throw new Exception("A translated file gained or lost lines: " + made.Length + " for " + was.Length);
                for (int at = 0; at < was.Length; at++)
                {
                    bool note = was[at].StartsWith(NoteMark + " ", StringComparison.Ordinal) && !Untranslated(was[at].Substring(NoteMark.Length));
                    if (!note && made[at] != was[at]) throw new Exception("A translation changed a line that isn't note words: \"" + was[at] + "\" became \"" + made[at] + "\"");
                }
                if (made[4] != ">! [pl] Spoken or typed, read without her brain." || made[11] != ">! [pl] Chat reading is in <Twitch> > <Read chat>. Settings are here.")
                    throw new Exception("Note words or a menu path came out wrong: " + made[4] + " / " + made[11]);
                if (made[18] != ">! [pl] Tokens: %petname% %name%" || !asked.Contains("Tokens") || asked.Exists(delegate(string one) { return one.Contains("%petname%"); }))
                    throw new Exception("A label over tokens was not put in their language on its own: " + made[18]);
                checks.Add("PASS a label over tokens (\"Tokens: %petname% %name%\") goes to her brain alone and comes back in their language, the tokens as written");
                checks.Add("PASS in another language only \">!\" note words change: headings, settings, lists, the \">!\" spacer and indented command examples stay byte for byte, and a menu path is her menu as it shows");
            }
            finally
            {
                Translate = translateWas;
                MenuName = menuWas;
                try { File.Delete(path); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
        }

        // Every .md and .txt of hers in her settings folder, plus the extensionless interest files, marked the new way
        // at each start; the interests list is marked once known. How many were written.
        internal static int RemarkAll(string folder)
        {
            int written = 0;
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return 0;
                foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                {
                    string name = Path.GetFileName(file).ToLowerInvariant();
                    bool bare = name.IndexOf('.') < 0 && string.Equals(Path.GetFileName(Path.GetDirectoryName(file)), "interests", StringComparison.OrdinalIgnoreCase);
                    if (!bare && !(name.EndsWith(".md", StringComparison.Ordinal) || name.EndsWith(".txt", StringComparison.Ordinal)) || name.Contains(".bak")) continue;
                    if (bare && name == Interests.HersName)
                    {
                        string[] list = Interests.SenpaiMore;
                        if (list == null || list.Length == 0) continue;
                        HashSet<string> off = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
                        if (Remark(file, delegate(string line) { return off.Contains(line); })) written++;
                        continue;
                    }
                    if (Remark(file)) written++;
                }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            return written;
        }

        // ---------- In their language ----------
        // With a language picked her notes are written in it by her brain (Said), English until a translation is in. A
        // note starting with a setting's name keeps the name in English, since that is what she reads.
        internal static Func<string, string> Translate;

        // A note line kept word for word in every language: an example of what to say, indented two or more spaces
        // under its note. Her instant path reads English only, so a translated example would never work. noteText is
        // the line after ">!".
        internal static bool Untranslated(string noteText)
        {
            return noteText != null && Regex.IsMatch(noteText, @"^ {2,}\S");
        }

        // Her menu as it shows in their language, a name at a time, from her GUI tables; set by her window. Null keeps the
        // names as written.
        internal static Func<string, string> MenuName;

        // A menu path in a note, like "Twitch > Read chat" or "Mainframe > Settings > DOOMSCROLL".
        private static readonly Regex MenuPath = new Regex(@"\b[A-Z][\w'.]*(?:\s[a-z][\w'.]*)*(?:\s>\s[A-Z][\w'.]*(?:\s[a-z][\w'.]*)*)+");

        private static string Menu(string path)
        {
            Func<string, string> name = MenuName;
            string[] parts = Regex.Split(path, @"\s>\s");
            for (int at = 0; at < parts.Length; at++)
            {
                string part = parts[at], end = "";
                while (part.EndsWith(".", StringComparison.Ordinal) && !Regex.IsMatch(part, @"\.\w+\.$")) { end = "." + end; part = part.Substring(0, part.Length - 1); }
                string shown = null;
                if (name != null) try { shown = name(part); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                parts[at] = (string.IsNullOrEmpty(shown) ? part : shown) + end;
            }
            return string.Join(" > ", parts);
        }

        // One note's words in their language: menu paths kept out of the translation and put back as her menus show them,
        // so a note names the menu they will see. English, or no translation in yet, comes back as it is.
        internal static string InLanguage(string text)
        {
            Func<string, string> translate = Translate;
            if (translate == null || string.IsNullOrWhiteSpace(text)) return text;
            List<string> menus = new List<string>();
            string masked = MenuPath.Replace(text, delegate(Match found) { menus.Add(found.Value); return "{" + (menus.Count - 1) + "}"; });
            string said = null;
            try { said = translate(masked); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            if (string.IsNullOrEmpty(said)) return text;
            for (int at = 0; at < menus.Count; at++)
            {
                string slot = "{" + at + "}";
                if (said.IndexOf(slot, StringComparison.Ordinal) < 0) return said == masked ? MenuPath.Replace(text, delegate(Match found) { return Menu(found.Value); }) : text;
                said = said.Replace(slot, Menu(menus[at]));
            }
            return said;
        }

        // ---------- Whole files in their language ----------
        // A file with this line is written whole in their language while it is still hers. Headings, settings, ">!="
        // lines, switched-off lines and indented examples stay word for word; the rest goes a sentence at a time.
        internal const string WholeMark = ">!~";

        internal static bool WholeFile(string text)
        {
            foreach (string row in (text ?? "").Replace("\r\n", "\n").Split('\n'))
                if (row.TrimStart().StartsWith(WholeMark, StringComparison.Ordinal)) return true;
            return false;
        }

        internal static string WholeInTheirLanguage(string text)
        {
            if (Translate == null || string.IsNullOrEmpty(text)) return text;
            string newline = text.Contains("\r\n") ? "\r\n" : "\n";
            string[] rows = text.Replace("\r\n", "\n").Split('\n');
            for (int at = 0; at < rows.Length; at++)
            {
                string row = rows[at], one = row.Trim();
                if (one.Length == 0 || one.StartsWith("#", StringComparison.Ordinal) || one.StartsWith(SettingMark, StringComparison.Ordinal) || one.StartsWith(WholeMark, StringComparison.Ordinal)) continue;
                // A line with no letters, like a comment's <!-- and -->, has nothing to put in their language.
                if (!Regex.IsMatch(one, @"\p{L}")) continue;
                if (one.StartsWith(NoteMark, StringComparison.Ordinal))
                {
                    string note = one.Substring(NoteMark.Length);
                    if (note.Length > 1 && note[0] == ' ' && !Untranslated(note)) rows[at] = NoteMark + " " + InLanguage(note.Substring(1));
                    continue;
                }
                if (one.StartsWith(">", StringComparison.Ordinal) || Regex.IsMatch(one, @"^[a-z][A-Za-z ]{0,30}[:=]")) continue;
                StringBuilder made = new StringBuilder();
                foreach (string piece in Sentences(one, 380))
                {
                    if (made.Length > 0) made.Append(' ');
                    made.Append(InLanguage(piece));
                }
                rows[at] = row.Substring(0, row.Length - row.TrimStart().Length) + made.ToString();
            }
            return string.Join(newline, rows);
        }

        // A line cut at sentence ends into pieces no longer than most, so a paragraph goes in a few sentences at a time.
        private static List<string> Sentences(string line, int most)
        {
            List<string> pieces = new List<string>();
            StringBuilder piece = new StringBuilder();
            foreach (string sentence in Regex.Split(line, @"(?<=[.!?])\s+"))
            {
                if (piece.Length > 0 && piece.Length + 1 + sentence.Length > most) { pieces.Add(piece.ToString()); piece.Length = 0; }
                if (piece.Length > 0) piece.Append(' ');
                piece.Append(sentence);
            }
            if (piece.Length > 0) pieces.Add(piece.ToString());
            return pieces;
        }

        // A WholeMark file written again in their language, or in English with none picked, while it is still what she
        // last wrote. Once they change it it is theirs and never rewritten. True when written.
        internal static bool RewriteWhole(string path, string english)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path) || !WholeFile(english)) return false;
                string now = ReadAll(path);
                bool hers = Untouched(path) || Plain(now) == Plain(english);
                if (!hers) return false;
                string wanted = WholeInTheirLanguage(english);
                if (Plain(now) == Plain(wanted)) { Shipped(path, now); return false; }
                Overwrite(path, wanted);
                Shipped(path, wanted);
                return true;
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); return false; }
        }

        internal static string Plain(string text) { return (text ?? "").TrimStart((char)0xFEFF).Replace("\r\n", "\n").TrimEnd(); }

        // personality.md, or any flagged file: a file still as shipped is written whole in their language with every heading,
        // setting and flag kept, goes back to English with no language, and is never touched once they change it.
        internal static void WholeCheck(System.Collections.Generic.List<string> checks, string folder)
        {
            string path = Path.Combine(folder, "whole.md");
            Func<string, string> was = Translate;
            try
            {
                string english = "<!--\n" + WholeMark + " shown in your language until you change it.\nNotes between these marks never reach her brain.\n-->\n\n## Her character\n\nYou are Capri. You are kind. " + new string('x', 390) + ". You like rain.\n\nstyle: hexes\n> a line switched off\n";
                File.WriteAllText(path, english.Replace("\n", Environment.NewLine));
                Shipped(path, english.Replace("\n", Environment.NewLine));
                Translate = delegate(string line) { return "[pl] " + line; };
                bool wrote = RewriteWhole(path, english.Replace("\n", Environment.NewLine));
                string[] rows = File.ReadAllLines(path);
                string body = Array.Find(rows, row => row.StartsWith("[pl] You are Capri", StringComparison.Ordinal));
                if (!wrote || body == null || body.Split(new[] { "[pl] " }, StringSplitOptions.None).Length != 4 || !body.Contains("[pl] You like rain.")
                    || Array.IndexOf(rows, "## Her character") < 0 || Array.IndexOf(rows, "style: hexes") < 0 || Array.IndexOf(rows, "> a line switched off") < 0
                    || Array.IndexOf(rows, WholeMark + " shown in your language until you change it.") < 0 || Array.IndexOf(rows, "[pl] Notes between these marks never reach her brain.") < 0 || !Untouched(path))
                    throw new Exception("A flagged file was not written whole in their language with its headings, settings and flag kept: " + string.Join(" | ", rows));
                Translate = null;
                if (!RewriteWhole(path, english.Replace("\n", Environment.NewLine)) || Plain(ReadAll(path)) != Plain(english.Replace("\n", Environment.NewLine)))
                    throw new Exception("A translated flagged file did not go back to English.");
                File.AppendAllText(path, "Theirs now." + Environment.NewLine);
                string theirs = ReadAll(path);
                Translate = delegate(string line) { return "[pl] " + line; };
                if (RewriteWhole(path, english.Replace("\n", Environment.NewLine)) || ReadAll(path) != theirs) throw new Exception("A flagged file they changed was rewritten.");
                if (RewriteWhole(path, "no flag here" + Environment.NewLine)) throw new Exception("A file without the flag was rewritten whole.");
                checks.Add("PASS a file flagged " + WholeMark + " is written whole in their language while it is as shipped, long lines a few sentences at a time, with headings, settings, switched-off lines and the flag kept; back to English with no language; and never once they change it");
            }
            finally
            {
                Translate = was;
                try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { }
            }
        }

        internal static string[] InTheirLanguage(string[] lines)
        {
            Func<string, string> translate = Translate;
            if (translate == null) return lines;
            string[] made = new string[lines.Length];
            for (int at = 0; at < lines.Length; at++)
            {
                string line = lines[at];
                made[at] = line;
                if (line == null || !line.StartsWith(NoteMark + " ", StringComparison.Ordinal)) continue;
                string text = line.Substring(NoteMark.Length);
                if (Untranslated(text)) continue;
                text = text.Substring(1);
                Match named = Regex.Match(text, @"^(\s*[a-z][a-z ]{0,30}:\s+)(.+)$");
                string lead = named.Success ? named.Groups[1].Value : "", rest = named.Success ? named.Groups[2].Value : text;
                if (rest.Trim().Length == 0) continue;
                // A label over a list of tokens ("Tokens: %petname% %name%") reads to her brain as a list to keep as it is, so
                // only the label goes, and the tokens stay as written.
                Match listed = Regex.Match(rest.Trim(), @"^([^%:]{1,40}):\s+((?:%[A-Za-z]+%[\s,]*)+)$");
                if (listed.Success)
                {
                    string label = listed.Groups[1].Value.Trim(), named2 = InLanguage(label);
                    if (!string.IsNullOrEmpty(named2) && named2 != label) made[at] = NoteMark + " " + lead + named2.Trim() + ": " + listed.Groups[2].Value.Trim();
                    continue;
                }
                string said = InLanguage(rest.Trim());
                if (!string.IsNullOrEmpty(said) && said != rest.Trim()) made[at] = NoteMark + " " + lead + said;
            }
            return made;
        }

        // Above each setting, with a language picked: the word for settings, the line and what its values mean, in
        // their language. Keys and values stay English, since she reads them. Marked ">!=" so a refresh keeps it and
        // English removes it.
        internal const string SettingMark = ">!=";

        // Takes the lines out, for English or before they are made again in another language.
        internal static bool Unexplained(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            string[] had = File.ReadAllLines(path);
            List<string> kept = new List<string>();
            foreach (string row in had) if (!(row ?? "").TrimStart().StartsWith(SettingMark, StringComparison.Ordinal)) kept.Add(row);
            if (kept.Count == had.Length) return false;
            Put(path, string.Join(Environment.NewLine, kept.ToArray()) + Environment.NewLine);
            return true;
        }

        // The settings in a file with no line above them yet, or one made for another value; sys.md's terms are left
        // alone. In a line file only lines above the first section and under "## settings" are settings.
        internal static List<string> Unexplaining(string path, bool lineFile = false)
        {
            List<string> wanted = new List<string>();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return wanted;
            string[] rows = File.ReadAllLines(path);
            foreach (int at in Settings(rows, lineFile))
            {
                string one = rows[at].Trim();
                int above = Above(rows, at);
                if (above >= 0 && rows[above].TrimStart().StartsWith(SettingMark, StringComparison.Ordinal) && Fits(rows[above], one)) continue;
                if (!wanted.Contains(one)) wanted.Add(one);
            }
            return wanted;
        }

        // The rows that are settings, for her settings.json.
        internal static List<int> SettingRows(string[] rows, bool lineFile) { return Settings(rows, lineFile); }

        private static List<int> Settings(string[] rows, bool lineFile)
        {
            List<int> found = new List<int>();
            bool terms = false, open = true;
            for (int at = 0; at < rows.Length; at++)
            {
                string one = (rows[at] ?? "").Trim();
                if (one.StartsWith("#", StringComparison.Ordinal))
                {
                    terms = Terms(one);
                    if (lineFile && one.StartsWith("##", StringComparison.Ordinal))
                        open = string.Equals(one.TrimStart('#').Trim(), "settings", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!terms && open && Setting(one)) found.Add(at);
            }
            return found;
        }

        // The nearest line above that is not blank, or -1.
        internal static int Above(IList<string> rows, int at)
        {
            int above = at - 1;
            while (above >= 0 && (rows[above] ?? "").Trim().Length == 0) above--;
            return above;
        }

        // A line fits its setting while it still holds the setting's name and value as they read now.
        internal static bool Fits(string line, string setting)
        {
            return Squashed(line).IndexOf(Squashed(setting), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string Squashed(string text) { return Regex.Replace((text ?? "").Trim(), @"\s+", " "); }

        private static bool Terms(string heading) { return Regex.IsMatch(heading, @"licen[cs]e|terms|agreement", RegexOptions.IgnoreCase); }

        // A setting that holds a sign-in is never one, so its value is never sent to her brain.
        private static bool Setting(string one)
        {
            return one.Length > 2 && !one.StartsWith(">", StringComparison.Ordinal) && !one.StartsWith("-", StringComparison.Ordinal)
                && Regex.IsMatch(one, @"^[A-Za-z][A-Za-z0-9 _'-]{0,40}:\s*\S")
                && !Regex.IsMatch(one.Substring(0, one.IndexOf(':')), @"token|password|secret|login|oauth|api ?key", RegexOptions.IgnoreCase);
        }

        // Puts each line above its setting, from what her brain made of it, and puts a new one in place of a line made for
        // another value. A line that lost the setting's name or value is not written, so it is asked for again.
        internal static int Explained(string path, Dictionary<string, string> lines, bool lineFile = false)
        {
            if (lines == null || lines.Count == 0 || !File.Exists(path)) return 0;
            string[] rows = File.ReadAllLines(path);
            HashSet<int> settings = new HashSet<int>(Settings(rows, lineFile));
            List<string> made = new List<string>();
            int written = 0;
            for (int at = 0; at < rows.Length; at++)
            {
                string one = (rows[at] ?? "").Trim();
                string said;
                if (settings.Contains(at) && lines.TryGetValue(one, out said) && !string.IsNullOrWhiteSpace(said) && Fits(said, one))
                {
                    string line = SettingMark + " " + said.Trim();
                    int above = Above(made, made.Count);
                    if (above < 0 || !made[above].TrimStart().StartsWith(SettingMark, StringComparison.Ordinal)) { made.Add(line); written++; }
                    else if (!Fits(made[above], one)) { made[above] = line; written++; }
                }
                made.Add(rows[at]);
            }
            if (written > 0) Put(path, string.Join(Environment.NewLine, made.ToArray()) + Environment.NewLine);
            return written;
        }

        private static bool IsNote(string one, string under, bool settings, HashSet<string> moved)
        {
            // Her line in their language above a setting is kept through a refresh, and goes only when the language does.
            if (one.StartsWith(SettingMark, StringComparison.Ordinal)) return false;
            if (one.StartsWith(NoteMark, StringComparison.Ordinal)) return true;
            if (!one.StartsWith(">", StringComparison.Ordinal) || moved.Contains(under)) return false;
            return !(settings && Keeps(under));
        }

        // The sections that already have a ">!" line in them.
        private static HashSet<string> MarkedSections(string[] lines)
        {
            HashSet<string> found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string under = "";
            foreach (string raw in lines)
            {
                string one = (raw ?? "").Trim();
                if (one.StartsWith("#")) { under = one.TrimStart('#').Trim(); continue; }
                if (one.StartsWith(NoteMark, StringComparison.Ordinal)) found.Add(under);
            }
            return found;
        }

        private static Dictionary<string, List<string>> Noted(string[] lines)
        {
            Dictionary<string, List<string>> found = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> moved = MarkedSections(lines);
            string under = "";
            bool settings = false;
            foreach (string raw in lines)
            {
                string one = (raw ?? "").Trim();
                if (one.StartsWith("#")) { under = one.TrimStart('#').Trim(); settings = false; continue; }
                // Same rule the writer uses, so her own switched-off lines are never read as notes and written back
                // a second time.
                if (one.Length > 0 && !one.StartsWith(">")) settings = true;
                if (!IsNote(one, under, settings, moved)) continue;
                List<string> had;
                if (!found.TryGetValue(under, out had)) { had = new List<string>(); found[under] = had; }
                had.Add(raw.TrimEnd());
            }
            return found;
        }
    }
}
