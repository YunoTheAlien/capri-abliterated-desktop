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
                string had = ReadAll(path);
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
                    Overwrite(path, put);
                    // What she was just given, so the next check knows this file is untouched until they
                    // touch it.
                    Shipped(path, put);
                    Seen(path);
                    return true;
                }
                // No line yet, so one is added. Their file is untouched otherwise.
                Overwrite(path, Marked(had));
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

        // Writing one of her files, skipped when the file already says exactly that. Her passes run on every
        // start and most of them have nothing to do on most of them, so without this they rewrite the same
        // bytes over and over: her watcher sees a change, rereads, and the file's time says it was edited
        // when nobody touched it. Comparing the whole text is the only honest test, since a pass that only
        // normalises line ends is a real change and has to go through.
        // Written whatever the file's attribute, and left as hidden or as visible as it was. Windows refuses to replace a
        // hidden file outright, so a tucked-away file took no refresh at all, and said nothing until its catch did.
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

        // Written whole, waiting out a few seconds when the file is busy. A file she has just written is opened by
        // other programs straight after (a virus scan, an editor watching the folder), so a second write at startup
        // could fail; on a fresh install that left a pack without a line it should have been given. Measured: a
        // second was not always enough.
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
                // The reset line and its note go in after the fresh text is built, so the fresh text does not
                // carry that note and a refresh dropped it. The setting was then left in the file with nothing
                // saying what it does. Only for a file that has the line: the rest never had one.
                string authority = fresh;
                foreach (string raw in had)
                    if (Regex.IsMatch((raw ?? "").Trim(), @"^" + Regex.Escape(ResetKey) + @"[ \t]*:", RegexOptions.IgnoreCase))
                    { authority = Marked(fresh); break; }
                Dictionary<string, List<string>> mine = Noted(InTheirLanguage(authority.Replace("\r\n", "\n").Split('\n')));
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
                if (!changed) return;
                string done = made.ToString();
                if (done == string.Join(Environment.NewLine, had) + Environment.NewLine) return;
                Overwrite(path, done);
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
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

        // A note of hers starts ">!". A line with a plain ">" in front is switched off, theirs or hers, and is never
        // a note, so a refresh can no longer take away a line somebody switched off.
        //
        // A section still written the old way, with no ">!" in it yet, is read by the old rule once: every ">" is a
        // note, except in the sections listed above, where only the block above the first real line is. Its notes
        // come back as ">!" and from then on it is read the new way.
        internal const string NoteMark = ">!";

        // ---------- In their language ----------
        // Her notes are hers, so with a language picked they are written in it, from the notes she ships, by her
        // brain (Said), the same way her lines are; English until a line's translation is in. A note that starts
        // with a setting's name keeps the name in English, since the name is what she reads.
        internal static Func<string, string> Translate;

        private static string[] InTheirLanguage(string[] lines)
        {
            Func<string, string> translate = Translate;
            if (translate == null) return lines;
            string[] made = new string[lines.Length];
            for (int at = 0; at < lines.Length; at++)
            {
                string line = lines[at];
                made[at] = line;
                if (line == null || !line.StartsWith(NoteMark + " ", StringComparison.Ordinal)) continue;
                string text = line.Substring(NoteMark.Length + 1);
                Match named = Regex.Match(text, @"^(\s*[a-z][a-z ]{0,30}:\s+)(.+)$");
                string lead = named.Success ? named.Groups[1].Value : "", rest = named.Success ? named.Groups[2].Value : text;
                if (rest.Trim().Length == 0) continue;
                string said = null;
                try { said = translate(rest.Trim()); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                if (!string.IsNullOrEmpty(said)) made[at] = NoteMark + " " + lead + said;
            }
            return made;
        }

        // Above each setting, with a language picked: the word for settings in their language, the line in it, and
        // what its values mean, so a file read in Polish says what "yes" does. Keys and values stay in English,
        // since she reads them. Marked ">!=" so it is kept through a refresh and taken out again for English.
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

        // The settings in a file that have no line above them yet. The terms at the bottom of sys.md are left alone.
        internal static List<string> Unexplaining(string path)
        {
            List<string> wanted = new List<string>();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return wanted;
            string[] rows = File.ReadAllLines(path);
            bool terms = false;
            for (int at = 0; at < rows.Length; at++)
            {
                string one = (rows[at] ?? "").Trim();
                if (one.StartsWith("#", StringComparison.Ordinal)) { terms = Terms(one); continue; }
                if (terms || !Setting(one)) continue;
                int above = at - 1;
                while (above >= 0 && (rows[above] ?? "").Trim().Length == 0) above--;
                if (above >= 0 && (rows[above] ?? "").TrimStart().StartsWith(SettingMark, StringComparison.Ordinal)) continue;
                if (!wanted.Contains(one)) wanted.Add(one);
            }
            return wanted;
        }

        private static bool Terms(string heading) { return Regex.IsMatch(heading, @"licen[cs]e|terms|agreement", RegexOptions.IgnoreCase); }

        private static bool Setting(string one)
        {
            return one.Length > 2 && !one.StartsWith(">", StringComparison.Ordinal) && !one.StartsWith("-", StringComparison.Ordinal)
                && Regex.IsMatch(one, @"^[A-Za-z][A-Za-z0-9 _'-]{0,40}:\s*\S");
        }

        // Puts each line above its setting, from what her brain made of it.
        internal static void Explained(string path, Dictionary<string, string> lines)
        {
            if (lines == null || lines.Count == 0 || !File.Exists(path)) return;
            string[] rows = File.ReadAllLines(path);
            List<string> made = new List<string>();
            bool terms = false;
            foreach (string row in rows)
            {
                string one = (row ?? "").Trim();
                if (one.StartsWith("#", StringComparison.Ordinal)) terms = Terms(one);
                string said;
                if (!terms && Setting(one) && lines.TryGetValue(one, out said) && !string.IsNullOrWhiteSpace(said)
                    && (made.Count == 0 || !made[made.Count - 1].TrimStart().StartsWith(SettingMark, StringComparison.Ordinal)))
                    made.Add(SettingMark + " " + said.Trim());
                made.Add(row);
            }
            Put(path, string.Join(Environment.NewLine, made.ToArray()) + Environment.NewLine);
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
