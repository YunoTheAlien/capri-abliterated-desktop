using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // Three list files behind the switch. Written, listed and read only with the toggle on, and dressed by
    // the opener and tail pools from lines.md like everything else.
    internal static class Interests
    {
        // Plain names and no type, so the folder reads as nothing and Windows has nothing to open them
        // with. What they were called before is kept beside them, because a file written under the old
        // name is theirs and is renamed rather than left behind or written over.
        internal const string FileName = "random_interests";

        // Hooks a pack fills in. Nothing attached means the defaults below.
        internal static Func<string> WasNameFor;
        internal static Func<string> HeadFor;

        internal static string WasFileName
        {
            get
            {
                if (WasNameFor == null) return null;
                try { return WasNameFor(); }
                catch (Exception) { return null; }
            }
        }

        internal static string Head
        {
            get
            {
                if (HeadFor == null) return "specialInterest:";
                try { return HeadFor() ?? "specialInterest:"; }
                catch (Exception) { return "specialInterest:"; }
            }
        }

        // Three files, not one. her interests file had her settings, her lines, the list of what she is into and a pack
        // of lines for each of about thirty of them, which ran to six hundred lines before anything went wrong
        // with it. Nobody scrolls that far, so the two lists live on their own and her interests file keeps the settings
        // and her general lines.
        //
        // All three are hers only with the toggle on: no tab, no reading, nothing written, with it off.
        internal const string HersName = "her_interests";
        internal const string YoursName = "your_interests";
        internal const string WasHersName = "Her_Kinks.md";
        internal const string WasYoursName = "Your_Kinks.md";
        internal static string HersKept, YoursKept;

        private static string[] Files { get { return new[] { Kept, HersKept, YoursKept }; } }

        // A file written under its old name becomes the new one, keeping every word of it. Run before
        // anything reads or writes, so nothing is created beside a file that is already theirs.
        internal static void Renamed()
        {
            Renamed(Kept, WasFileName);
            Renamed(HersKept, WasHersName);
            Renamed(YoursKept, WasYoursName);
        }

        private static void Renamed(string now, string was)
        {
            try
            {
                // No old name means nothing attached, so there is no file under it to rename.
                if (string.IsNullOrEmpty(now) || string.IsNullOrEmpty(was)) return;
                string folder = Path.GetDirectoryName(now);
                if (string.IsNullOrEmpty(folder)) return;
                string old = Path.Combine(folder, was);
                if (!File.Exists(old)) return;
                // Theirs either way: with both there the old one is left alone rather than written over.
                if (File.Exists(now)) return;
                Tucked.Free(old);
                File.Move(old, now);
                Tucked.Away(now);
                string spare = old + ".bak";
                if (File.Exists(spare)) { Tucked.Free(spare); try { File.Move(spare, now + ".bak"); } catch (IOException) { } Tucked.Away(now + ".bak"); }
            }
            catch (Exception) { }
        }

        // How she likes being handled. Was a dropdown on the setup card, which asked before anyone had met her.
        internal const string HandlingKey = "how she likes it";
        internal static readonly string[] Handlings = { "Gentle", "Rough", "Both" };
        internal static string Kept;

        private static readonly object writing = new object();
        private static readonly Random dice = new Random();

        internal static bool Shown { get { return Brain.Uncensored && !string.IsNullOrEmpty(Kept); } }

        internal static WrittenNote Open()
        {
            if (!Brain.Uncensored) throw new IOException("That one only opens with Uncensored on.");
            Write(false);
            return new WrittenNote(Kept, Text, writing);
        }

        internal static WrittenNote OpenHers()
        {
            if (!Brain.Uncensored) throw new IOException("That one only opens with Uncensored on.");
            Write(false);
            Split();
            return new WrittenNote(HersKept, HersHead.Replace("\n", Environment.NewLine), writing);
        }

        internal static WrittenNote OpenYours()
        {
            if (!Brain.Uncensored) throw new IOException("That one only opens with Uncensored on.");
            Split();
            return new WrittenNote(YoursKept, YoursHead.Replace("\n", Environment.NewLine), writing);
        }

        // A her interests file from before the split has one combined "how she brings it up". The two new sections are
        // appended so she can tell a reward from a threat, and their own entries are left exactly as they are.
        // Any section the build ships that the file does not have yet is appended, so new interest packs and new
        // template pools turn up in a file written by an older build. Their own lines are never touched.
        // What belongs in Her_Kinks.md: the list of what she is into, and the pack of lines for each one.
        private static bool Hers(string heading)
        {
            string head = (heading ?? "").Trim();
            return head.StartsWith(Head, StringComparison.OrdinalIgnoreCase)
                || head.Equals("what she is into", StringComparison.OrdinalIgnoreCase);
        }

        private const string YoursHead =
            "# What you are into\n"
            + "\n"
            + "> Yours, not hers. She reads this so she knows what you like without having to be told twice.\n"
            + "> One a line. Empty is fine and she will not bring any of it up.\n"
            + "> Only read with Uncensored on.\n"
            + "\n"
            + "reset to stock: 0\n"
            + "\n"
            + "## what you are into\n"
            + "\n";

        private const string HersHead =
            "# What she is into\n"
            + "\n"
            + "> Her list, and the lines that go with each one. Kept apart from her other one so neither gets lost.\n"
            + "> Only read with Uncensored on.\n"
            + "\n"
            + "reset to stock: 0\n"
            + "\n";

        // Moves her list and her interest packs out of her interests file, once. her interests file keeps the settings and her
        // general lines; nothing is rewritten, the sections are carried across as they are, edits and all.
        private static void Carve()
        {
            try
            {
                if (string.IsNullOrEmpty(Kept) || string.IsNullOrEmpty(HersKept) || !File.Exists(Kept)) return;
                if (File.Exists(HersKept)) return;
                string[] rows = File.ReadAllText(Kept).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                List<string> stays = new List<string>(), goes = new List<string>();
                bool moving = false;
                foreach (string raw in rows)
                {
                    string line = raw.Trim();
                    if (line.StartsWith("##", StringComparison.Ordinal)) moving = Hers(line.TrimStart('#'));
                    (moving ? goes : stays).Add(raw);
                }
                if (goes.Count == 0) return;
                while (stays.Count > 0 && stays[stays.Count - 1].Trim().Length == 0) stays.RemoveAt(stays.Count - 1);
                while (goes.Count > 0 && goes[goes.Count - 1].Trim().Length == 0) goes.RemoveAt(goes.Count - 1);
                Tucked.Write(HersKept, HersHead.Replace("\n", Environment.NewLine)
                    + string.Join(Environment.NewLine, goes.ToArray()) + Environment.NewLine, new UTF8Encoding(false));
                Tucked.Write(Kept, string.Join(Environment.NewLine, stays.ToArray()) + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        private static void MakeYours()
        {
            try
            {
                if (string.IsNullOrEmpty(YoursKept) || File.Exists(YoursKept)) return;
                Tucked.Write(YoursKept, YoursHead.Replace("\n", Environment.NewLine), new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        // Both only exist with the toggle on, like her interests file itself.
        internal static void Split()
        {
            if (!Brain.Uncensored) return;
            Carve();
            MakeYours();
        }

        // Adds the sections of one kind that the file does not have yet.
        private static void TopUpInto(string file, bool hers)
        {
            try
            {
                if (string.IsNullOrEmpty(file) || !File.Exists(file)) return;
                string body = File.ReadAllText(file);
                StringBuilder adding = new StringBuilder();
                foreach (string heading in Headings(Text))
                {
                    if (Hers(heading) != hers) continue;
                    // The \r matters: these files are CRLF and "$" cannot match before a carriage return, so
                    // every heading read as missing and the whole file was appended to itself on every start.
                    if (Regex.IsMatch(body, @"(?mi)^[ \t]*##+[ \t]*" + Regex.Escape(heading) + @"[ \t\r]*$")) continue;
                    adding.Append(Chunk(Text, "## " + heading));
                }
                if (adding.Length == 0) return;
                if (!body.EndsWith("\n")) body += Environment.NewLine;
                Tucked.Write(file, body + adding.ToString(), new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        private static void TopUpHers()
        {
            if (string.IsNullOrEmpty(HersKept)) return;
            if (!File.Exists(HersKept)) MakeHers();
            TopUpInto(HersKept, true);
        }

        private static void MakeHers()
        {
            try { Tucked.Write(HersKept, HersHead.Replace("\n", Environment.NewLine), new UTF8Encoding(false)); }
            catch (Exception) { }
        }

        // Anything of hers left in her interests file after the carve, taken out. Nothing is lost: it is already in
        // Her_Kinks.md by the time this runs, and anything only in her interests file is moved rather than dropped.
        private static void NoHersInRandom()
        {
            try
            {
                if (string.IsNullOrEmpty(Kept) || string.IsNullOrEmpty(HersKept) || !File.Exists(Kept) || !File.Exists(HersKept)) return;
                string had = File.ReadAllText(Kept), theirs = File.ReadAllText(HersKept);
                string[] rows = had.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                List<string> stays = new List<string>(), moves = new List<string>();
                bool moving = false, found = false;
                foreach (string raw in rows)
                {
                    string line = raw.Trim();
                    if (line.StartsWith("##", StringComparison.Ordinal))
                    {
                        string head = line.TrimStart('#').Trim();
                        moving = Hers(head);
                        if (moving)
                        {
                            found = true;
                            // Already over there: drop this copy rather than making a second one.
                            moving = !Regex.IsMatch(theirs, @"(?mi)^[ \t]*##+[ \t]*" + Regex.Escape(head) + @"[ \t\r]*$")
                                ? true : false;
                            if (!moving) { moving = true; moves.Add(null); continue; }
                        }
                    }
                    if (moving) moves.Add(raw); else stays.Add(raw);
                }
                if (!found) return;
                List<string> real = new List<string>();
                bool skip = false;
                foreach (string row in moves)
                {
                    if (row == null) { skip = true; continue; }
                    if (row.TrimStart().StartsWith("##", StringComparison.Ordinal)) skip = false;
                    if (!skip) real.Add(row);
                }
                while (stays.Count > 0 && stays[stays.Count - 1].Trim().Length == 0) stays.RemoveAt(stays.Count - 1);
                Tucked.Write(Kept, string.Join(Environment.NewLine, stays.ToArray()) + Environment.NewLine, new UTF8Encoding(false));
                if (real.Count > 0)
                {
                    if (!theirs.EndsWith("\n")) theirs += Environment.NewLine;
                    Tucked.Write(HersKept, theirs + string.Join(Environment.NewLine, real.ToArray()) + Environment.NewLine, new UTF8Encoding(false));
                }
            }
            catch (Exception) { }
        }

        internal static void TopUp()
        {
            if (!Brain.Uncensored) { Gone(); return; }
            if (string.IsNullOrEmpty(Kept)) return;
            Split();
            NoHersInRandom();
            try
            {
                if (!File.Exists(Kept)) { Write(false); Split(); }
                Notes.Refresh(Kept, Text);
                // Each section to the file it belongs in. Without this the carve moved her list and her interest
                // packs out and this put them straight back, so both files had them.
                TopUpInto(Kept, false);
                TopUpHers();
                Reword();
                OnlyOnce();
                Settle();
            }
            catch (Exception) { }
        }


        // Their word from the file, or Gentle. Read fresh so an edit takes without a restart.
        internal static string Handling()
        {
            try
            {
                if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept)) return "Gentle";
                foreach (string row in Chunk(File.ReadAllText(Kept), "## " + HandlingKey).Replace("\r\n", "\n").Split('\n'))
                {
                    string line = row.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith(">", StringComparison.Ordinal)) continue;
                    foreach (string one in Handlings) if (string.Equals(line, one, StringComparison.OrdinalIgnoreCase)) return one;
                }
            }
            catch (Exception) { }
            return "Gentle";
        }

        // Puts "how she likes it" back under "what she is into".
        //
        // TopUp appends a missing section to the end of the file, and this one belongs near the top with the two
        // lists people actually edit. At the bottom of a file this long nobody would ever find it.
        // One of each section, for a file that grew copies of itself.
        //
        // The heading check above anchored with "$", which cannot match before the carriage return a CRLF file
        // puts there, so every heading read as missing and the whole file was appended again on every start.
        // Measured here at 125 copies of each section and 63,000 lines. The check is fixed; this clears what it
        // already wrote.
        //
        // A line she shipped that was wrong, swapped for the one she ships now. Top up only ever adds what is
        // missing, so a line already in their file keeps whatever it said for ever, and a weave template is a
        // sentence she says: a broken one is heard every time it comes round.
        //
        // An exact match of what shipped, in the file it shipped in. Anything reworded by hand does not match
        // and is left alone. The old line is dropped rather than rewritten when the new one is already there,
        // since top up will have put it in.
        private static readonly string[,] Reworded =
        {
            // "we end up at being told exactly what to do": her list is written as gerunds, so this one template
            // read as broken English every time it was picked.
            { "More of this and we end up at %it%.", "More of this and we end up here: %it%." },
            { "Ask nicely and it could be %it%.", "Ask nicely and we could get to %it%." },
            { "Behave like that and there is %it% in it for you.", "Behave like that and I might be talked into %it%." }
        };

        // Whether a line is already in the section that starts at this row, looking no further than its end.
        private static bool Holds(string[] rows, int from, string line)
        {
            for (int at = from; at < rows.Length; at++)
            {
                string one = rows[at].Trim();
                if (at > from && one.StartsWith("#", StringComparison.Ordinal)) return false;
                if (string.Equals(one, line, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static void Reword()
        {
            if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept)) return;
            try
            {
                string had = File.ReadAllText(Kept);
                string[] rows = had.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                List<string> kept = new List<string>();
                bool moved = false;
                for (int at = 0; at < Reworded.GetLength(0); at++)
                {
                    // Per section, not per file. The same template is in several sections on purpose, and a
                    // check across the whole file read the copy in one section as the copy in another, so the
                    // second one was dropped instead of reworded and that pool came out a line short. Nothing
                    // puts it back either: these files top up whole missing sections only, by design.
                    kept.Clear();
                    bool has = Holds(rows, 0, Reworded[at, 1]);
                    foreach (string raw in rows)
                    {
                        string one = raw.Trim();
                        if (one.StartsWith("#", StringComparison.Ordinal))
                        {
                            kept.Add(raw);
                            has = Holds(rows, kept.Count, Reworded[at, 1]);
                            continue;
                        }
                        if (!string.Equals(one, Reworded[at, 0], StringComparison.Ordinal)) { kept.Add(raw); continue; }
                        moved = true;
                        if (has) continue;
                        kept.Add(Reworded[at, 1]);
                        has = true;
                    }
                    rows = kept.ToArray();
                }
                if (moved) Tucked.Write(Kept, string.Join(Environment.NewLine, rows), new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        // The first copy is the one kept, because that is the one anyone has edited. Later copies go whole.
        private static void OnlyOnce()
        {
            try
            {
                if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept)) return;
                string had = File.ReadAllText(Kept);
                string[] rows = had.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                Dictionary<string, bool> seen = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                List<string> kept = new List<string>();
                bool skipping = false, dropped = false;
                foreach (string raw in rows)
                {
                    string line = raw.Trim();
                    if (line.StartsWith("##", StringComparison.Ordinal))
                    {
                        string head = line.TrimStart('#').Trim();
                        if (seen.ContainsKey(head)) { skipping = true; dropped = true; continue; }
                        seen[head] = true;
                        skipping = false;
                    }
                    if (!skipping) kept.Add(raw);
                }
                if (!dropped) return;
                while (kept.Count > 0 && kept[kept.Count - 1].Trim().Length == 0) kept.RemoveAt(kept.Count - 1);
                Tucked.Write(Kept, string.Join(Environment.NewLine, kept.ToArray()) + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        private static void Settle()
        {
            try
            {
                if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept)) return;
                List<string> rows = new List<string>(File.ReadAllText(Kept).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'));
                // Every copy comes out, then one goes back in the right place. Appending and moving on separate
                // runs left two of it, and a settings section that exists twice is worse than one in the wrong spot.
                List<string> block = null;
                for (int from = Heading(rows, "## " + HandlingKey); from >= 0; from = Heading(rows, "## " + HandlingKey))
                {
                    int ends = Next(rows, from);
                    if (block == null) block = rows.GetRange(from, ends - from);
                    rows.RemoveRange(from, ends - from);
                }
                int into = Heading(rows, "## what she is into");
                if (block == null || into < 0) return;
                rows.InsertRange(Next(rows, into), block);
                Tucked.Write(Kept, string.Join(Environment.NewLine, rows.ToArray()).TrimEnd() + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        private static int Heading(List<string> rows, string named)
        {
            for (int at = 0; at < rows.Count; at++)
                if (string.Equals(rows[at].Trim(), named, StringComparison.OrdinalIgnoreCase)) return at;
            return -1;
        }

        // The line the next section starts on, trailing blanks left with the section being moved.
        private static int Next(List<string> rows, int from)
        {
            for (int at = from + 1; at < rows.Count; at++)
                if (rows[at].TrimStart().StartsWith("##", StringComparison.Ordinal)) return at;
            return rows.Count;
        }

        private static List<string> Headings(string whole)
        {
            List<string> found = new List<string>();
            foreach (Match one in Regex.Matches(whole, @"(?m)^##[ 	]*(.+?)[ 	
]*$"))
            {
                string named = one.Groups[1].Value.Trim();
                if (named.Length > 0 && !found.Contains(named)) found.Add(named);
            }
            return found;
        }

        private static string Chunk(string whole, string heading)
        {
            int from = whole.IndexOf(heading, StringComparison.OrdinalIgnoreCase);
            if (from < 0) return "";
            int next = whole.IndexOf("\n## ", from + heading.Length);
            string got = next < 0 ? whole.Substring(from) : whole.Substring(from, next - from);
            return Environment.NewLine + got.TrimEnd() + Environment.NewLine;
        }

        // Switched off, her three lists go. Turning it back on writes them again.
        internal static void Gone()
        {
            foreach (string one in Files)
            {
                if (string.IsNullOrEmpty(one)) continue;
                try
                {
                    if (!File.Exists(one)) continue;
                    File.Delete(one);
                    Brain.Log("files", -1, Path.GetFileName(one) + " removed: the switch is off, so it does not stay on disk.");
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        internal static void Write(bool over)
        {
            // Never written unless asked for: the file turning up on disk is itself a disclosure.
            if (string.IsNullOrEmpty(Kept) || !Brain.Uncensored) return;
            try
            {
                if (!over && File.Exists(Kept)) return;
                string folder = Path.GetDirectoryName(Kept);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) Directory.CreateDirectory(folder);
                Tucked.Write(Kept, Text, new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        // A whole sentence built from one of her templates and one thing off the list, or null when she has either
        // nothing listed or no way to say it. This is what makes the list touch everything: it is attached to her
        // ordinary reactions rather than kept in a pool of its own.
        // Lines belonging to one named interest. A name on the list with a matching "## interest: name" section brings its
        // own lines in, so turning one on opens a path rather than only filling a blank in a template.
        // A trailing "*" in the list marks a name that ships its own pack. It is a note to whoever is reading the
        // file, so it comes off before the name is looked up or spoken: without this, uncommenting a starred line
        // put the star into her mouth.
        private static string Named(string line) { return Regex.Replace((line ?? "").Trim(), @"\s*\*+$", "").Trim(); }

        // What the character she is being is into, when her own pack carries a list of it. The shared list is
        // everyone's, so it has the gentlest pack promising what the hardest would and the other way round. Null
        // when her pack says nothing, and then the shared one answers as it always did.
        private static string Hers()
        {
            // Behind the switch, and said twice: her pack's list of this is written into the senpai half of her file,
            // which a censored install does not have, and this never asks for it there either. A section
            // somebody writes by hand into the plain half stays unread, which is the rule everywhere else.
            if (!Brain.Adult) return null;
            try
            {
                string[] mine = Personalities.Lines("into", null);
                if (mine == null || mine.Length == 0) return null;
                lock (dice) return Named(mine[dice.Next(mine.Length)]);
            }
            catch (Exception) { return null; }
        }

        internal static string Packed(bool liked)
        {
            if (!Brain.Uncensored) return null;
            string named = Hers() ?? Named(For("what she is into"));
            if (string.IsNullOrEmpty(named)) return null;
            return For(Head + " " + named, liked ? "liked" : "cross");
        }

        internal static string Woven(bool liked)
        {
            if (!Brain.Uncensored) return null;
            // Matched to how she feels about what just happened. An older file with the single combined section
            // still works, it simply does not know the difference.
            string how = For(liked ? "how she brings it up, pleased" : "how she brings it up, cross")
                ?? For("how she brings it up");
            string what = Hers() ?? Named(For("what she is into"));
            if (string.IsNullOrEmpty(how) || string.IsNullOrEmpty(what)) return null;
            string said = how.Replace("%it%", what).Replace("%IT%", what);
            return said == how ? null : said;
        }

        // Everything under a heading, comma joined, for her brain rather than for a line. For picks one at random,
        // which is right for a reply and useless for telling her what she likes.
        internal static string All(string pool)
        {
            if (!Brain.Uncensored) return null;
            try
            {
                System.Collections.Generic.List<string> found = new System.Collections.Generic.List<string>();
                foreach (string file in Files)
                {
                    if (string.IsNullOrEmpty(file) || !File.Exists(file)) continue;
                    string under = null;
                    foreach (string raw in File.ReadAllLines(file))
                    {
                        string one = (raw ?? "").Trim();
                        if (one.StartsWith("#"))
                        {
                            // A single # is the file's title, not a section. Her_Kinks.md is titled "What she
                            // is into" and has a "## what she is into" under it, so counting the title as a
                            // section put everything between the two into her list, the reset marker with it.
                            // She offered "Reset to stock: 0 is on the table."
                            under = one.StartsWith("##", StringComparison.Ordinal) ? one.TrimStart('#').Trim() : null;
                            continue;
                        }
                        if (under == null || one.Length == 0 || one.StartsWith(">")) continue;
                        // And the marker itself, wherever it sits. Notes writes it into every file of hers and
                        // it is never a line of content.
                        if (Regex.IsMatch(one, "(?i)^" + Regex.Escape(Notes.ResetKey) + "[ \t]*:")) continue;
                        if (!string.Equals(under, pool, StringComparison.OrdinalIgnoreCase)) continue;
                        if (one.StartsWith("-") || one.StartsWith("+")) one = one.Substring(1).Trim();
                        if (one.Length > 0 && !found.Contains(one)) found.Add(one);
                    }
                }
                if (found.Count == 0) return null;
                return string.Join(", ", found.ToArray());
            }
            catch (Exception) { return null; }
        }

        // One line from a named pool, dressed the way her other lines are.
        // That her list holds what she is into and nothing else. The fault this pins was visible on screen:
        // she said "Oh, we're going somewhere. You are doing well. Reset to stock: 0 is on the table."
        internal static void ListCheck(System.Collections.Generic.List<string> checks)
        {
            string keptWas = Kept, hersWas = HersKept, yoursWas = YoursKept;
            bool senpaiWas = Brain.Uncensored;
            string folder = Path.Combine(Path.GetTempPath(), "capri-interests-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                Directory.CreateDirectory(folder);
                // Her file as it is written: a title that happens to name a section, the note, the marker,
                // then the section itself.
                string hers = Path.Combine(folder, "her_interests");
                File.WriteAllText(hers, string.Join(Environment.NewLine, new[]
                {
                    "# What she is into",
                    "",
                    "> Her list, and the lines that go with each one.",
                    "> Only read with Uncensored on.",
                    "",
                    Notes.ResetKey + ": 0",
                    "",
                    "## what she is into",
                    "",
                    "being told what to do",
                    "being praised",
                    "",
                }), new UTF8Encoding(false));
                // With nothing attached there is no old name, so the check uses the new one.
                Kept = Path.Combine(folder, WasFileName ?? FileName);
                HersKept = hers;
                YoursKept = Path.Combine(folder, "your_interests");
                Brain.Uncensored = true;
                System.Collections.Generic.List<string> wrong = new System.Collections.Generic.List<string>();
                bool sawMine = false;
                // Her own shipped list is topped up into this file, so what comes back is hers plus the two
                // written above. What may never come back is a line that is not a thing at all.
                for (int go = 0; go < 120; go++)
                {
                    string one = For("what she is into");
                    if (one == null) { wrong.Add("her list read as empty"); break; }
                    if (one == "being told what to do" || one == "being praised") sawMine = true;
                    if (Regex.IsMatch(one, "(?i)^" + Regex.Escape(Notes.ResetKey))) wrong.Add("the reset marker: " + one);
                    else if (Regex.IsMatch(one, "^[A-Za-z][A-Za-z ]{2,22}:")) wrong.Add("a setting rather than a thing: " + one);
                    else if (one.StartsWith("#", StringComparison.Ordinal) || one.StartsWith(">", StringComparison.Ordinal)) wrong.Add("a heading or a note: " + one);
                }
                if (!sawMine) wrong.Add("neither line written into the file came back in 120 draws");
                if (wrong.Count > 0) throw new Exception("What she is into read wrongly: " + string.Join(" | ", wrong.ToArray()));
                checks.Add("PASS what she is into holds her list and nothing else: the file's own title is not a section, however it is named, and the reset marker is never a line of content. She was offering \"Reset to stock: 0 is on the table\" because that file is titled with the same words as the section inside it");
            }
            finally
            {
                Kept = keptWas;
                HersKept = hersWas;
                YoursKept = yoursWas;
                Brain.Uncensored = senpaiWas;
                try { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
                catch (Exception) { }
            }
        }

        internal static string For(string pool) { return For(pool, null); }

        // tone picks between the "+" and "-" lines inside a pack. A pack with neither marker is used for both.
        internal static string For(string pool, string tone)
        {
            if (!Brain.Uncensored) return null;
            try
            {
                Write(false);
                System.Collections.Generic.List<string> found = new System.Collections.Generic.List<string>();
                foreach (string file in Files)
                {
                    if (string.IsNullOrEmpty(file) || !File.Exists(file)) continue;
                    string under = null;
                    foreach (string raw in File.ReadAllLines(file))
                    {
                        string one = (raw ?? "").Trim();
                        if (one.StartsWith("#"))
                        {
                            // A single # is the file's title, not a section of it. Her_Kinks.md is titled "What she is
                            // into" and has a "## what she is into" under it, so counting the title as a section put
                            // everything between the two into her list. She offered "Reset to stock: 0 is on the table."
                            under = one.StartsWith("##", StringComparison.Ordinal) ? one.TrimStart('#').Trim() : null;
                            continue;
                        }
                        if (under == null || one.Length == 0 || one.StartsWith(">")) continue;
                        // The marker Notes writes near the top of every file of hers, wherever it sits. It is
                        // not a line of content and she was offering it as one.
                        if (Regex.IsMatch(one, "(?i)^" + Regex.Escape(Notes.ResetKey) + "[ 	]*:")) continue;
                        if (!string.Equals(under, pool, StringComparison.OrdinalIgnoreCase)) continue;
                        bool cross = one.StartsWith("-");
                        bool warm = one.StartsWith("+");
                        if (cross || warm) one = one.Substring(1).Trim();
                        if (tone != null && (cross || warm) && (tone == "cross") != cross) continue;
                        if (one.Length > 0) found.Add(one);
                    }
                }
                if (found.Count == 0) return null;
                // One shared Random, not a new one each call: Random is seeded from the clock, which only ticks
                // every 15ms or so, so two calls in the same breath picked the same line every time.
                lock (writing) return Them.Fill(found[dice.Next(found.Count)]);
            }
            catch (Exception) { return null; }
        }

        // Offered but switched off, so her file is a menu rather than a blank. Ordinary, legal and widely written
        // about; anything that needs a real person who cannot agree to it is not here and will not be. A star marks
        // one with its own pack below, which is what turns a name into lines of its own.
        // The rest of her list, which is not in this source and not in the public build. Something
        // attaches it before her file is written; with nothing attached the extra lines are simply
        // absent, which is what a censored install wants.
        internal static string[] SenpaiMore = new string[0];

        // The body of her file, which is not in this source and not in the public build. Something
        // attaches it; with nothing attached the file is empty, and a censored install never writes it.
        internal static Func<string> BodyFor;

        // What each of the two new files looks like fresh, for reset to stock.
        internal static string HersText
        {
            get
            {
                StringBuilder made = new StringBuilder(HersHead.Replace("\n", Environment.NewLine));
                foreach (string heading in Headings(Text)) if (Hers(heading)) made.Append(Chunk(Text, "## " + heading));
                return made.ToString();
            }
        }

        internal static string YoursText { get { return YoursHead.Replace("\n", Environment.NewLine); } }

        internal static string Text
        {
            get
            {
                StringBuilder made = new StringBuilder();
                if (BodyFor != null) { try { made.Append(BodyFor()); } catch (Exception) { } }
                return made.ToString();
            }
        }
    }
}
