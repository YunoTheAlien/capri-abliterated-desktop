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
        // File names. What they were called before is kept beside them, because a file written under the old
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
                try { return Legacy.Modern(HeadFor()) ?? "specialInterest:"; }
                catch (Exception) { return "specialInterest:"; }
            }
        }

        // Three files, so neither list is buried in the settings. All three are hers only with the toggle on: no tab,
        // reading or writing with it off.
        internal const string HersName = "her_interests";
        internal const string YoursName = "your_interests";
        // The two other old names, set by the same pack as the first and null without it.
        internal static Func<string> WasHersFor, WasYoursFor;
        internal static string WasHersName { get { return Named(WasHersFor); } }
        internal static string WasYoursName { get { return Named(WasYoursFor); } }

        private static string Named(Func<string> hook)
        {
            if (hook == null) return null;
            try { return hook(); }
            catch (Exception) { return null; }
        }
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
                SpareFiles.Carry(old, now);
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // How she likes being handled. Was a dropdown on the setup card, which asked before anyone had met her.
        internal const string HandlingKey = "how she likes it";
        internal static readonly string[] Handlings = { "Gentle", "Rough", "Both" };
        internal static string Kept;

        private static readonly object writing = new object();
        private static readonly Random dice = new Random();

        internal static bool Shown { get { return Brain.SenpaiOn && !string.IsNullOrEmpty(Kept); } }

        internal static WrittenNote Open()
        {
            if (!Brain.SenpaiOn) throw new IOException("That one only opens with the switch at the bottom of sys.md on.");
            Write(false);
            return new WrittenNote(Kept, Text, writing);
        }

        internal static WrittenNote OpenHers()
        {
            if (!Brain.SenpaiOn) throw new IOException("That one only opens with the switch at the bottom of sys.md on.");
            Write(false);
            Split();
            return new WrittenNote(HersKept, HersHead.Replace("\n", Environment.NewLine), writing);
        }

        internal static WrittenNote OpenYours()
        {
            if (!Brain.SenpaiOn) throw new IOException("That one only opens with the switch at the bottom of sys.md on.");
            Split();
            return new WrittenNote(YoursKept, YoursHead.Replace("\n", Environment.NewLine), writing);
        }

        // Sections the build ships that the file lacks are appended, including the reward and threat split, so an older
        // file gets new packs and pools; their lines are never touched. The senpai file holds the list and each
        // interest's lines.
        private static bool Hers(string heading)
        {
            string head = (heading ?? "").Trim();
            return head.StartsWith(Head, StringComparison.OrdinalIgnoreCase)
                || head.Equals("what she is into", StringComparison.OrdinalIgnoreCase);
        }

        private const string YoursHead =
            "# What you are into\n"
            + "\n"
            + ">! Yours, not hers. She reads this so she knows what you like without having to be told twice.\n"
            + ">! One a line. Empty is fine and she will not bring any of it up.\n"
            + ">! Only read with the switch at the bottom of sys.md on.\n"
            + "\n"
            + "reset to stock: 0\n"
            + "\n"
            + "## what you are into\n"
            + "\n";

        private const string HersHead =
            "# What she is into\n"
            + "\n"
            + ">! Her list, and the lines that go with each one. Kept apart from her other one so neither gets lost.\n"
            + ">! Only read with the switch at the bottom of sys.md on.\n"
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
                string[] rows = CapriDesktop.Notes.ReadAll(Kept).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
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
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        private static void MakeYours()
        {
            try
            {
                if (string.IsNullOrEmpty(YoursKept) || File.Exists(YoursKept)) return;
                Tucked.Write(YoursKept, YoursHead.Replace("\n", Environment.NewLine), new UTF8Encoding(false));
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // Both only exist with the toggle on, like her interests file itself.
        internal static void Split()
        {
            if (!Brain.SenpaiOn) return;
            Carve();
            MakeYours();
        }

        private static void MakeHers()
        {
            try { Tucked.Write(HersKept, HersHead.Replace("\n", Environment.NewLine), new UTF8Encoding(false)); }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        internal static void TopUp()
        {
            if (!Brain.SenpaiOn) { Gone(); return; }
            if (string.IsNullOrEmpty(Kept)) return;
            Split();
            try
            {
                // Written new from stock on each version (Renew); in between only a missing file is written, and the
                // notes follow her language. With her own file already there, a missing interests file is written
                // without her sections, which live in hers.
                if (!File.Exists(Kept))
                {
                    if (!string.IsNullOrEmpty(HersKept) && File.Exists(HersKept)) Tucked.Write(Kept, RandomText, new UTF8Encoding(false));
                    else { Write(false); Split(); }
                }
                Notes.Refresh(Kept, Text);
                if (!string.IsNullOrEmpty(HersKept) && !File.Exists(HersKept)) MakeHers();
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }


        // Their word from the file, or Gentle. Read fresh so an edit takes without a restart.
        internal static string Handling()
        {
            try
            {
                if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept)) return "Gentle";
                foreach (string row in Chunk(CapriDesktop.Notes.ReadAll(Kept), "## " + HandlingKey).Replace("\r\n", "\n").Split('\n'))
                {
                    string line = row.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith(">", StringComparison.Ordinal)) continue;
                    foreach (string one in Handlings) if (string.Equals(line, one, StringComparison.OrdinalIgnoreCase)) return one;
                }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            return "Gentle";
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
            // Only written with the switch on.
            if (string.IsNullOrEmpty(Kept) || !Brain.SenpaiOn) return;
            try
            {
                if (!over && File.Exists(Kept)) return;
                string folder = Path.GetDirectoryName(Kept);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) Directory.CreateDirectory(folder);
                Tucked.Write(Kept, Text, new UTF8Encoding(false));
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // A sentence from one of her templates and one thing off the list, or null, attached to her ordinary reactions.
        // A "## interest: name" section brings that interest's own lines; a trailing "*" marks a shipped pack and comes
        // off before use.
        private static string Named(string line) { return Regex.Replace((line ?? "").Trim(), @"\s*\*+$", "").Trim(); }

        // What the character she is being is into, when her pack carries its own list; otherwise null and the shared
        // list answers, which would have the gentlest pack promising what the hardest would.
        private static string Hers()
        {
            // Behind the switch twice: her pack's list lives in the senpai half, which a censored install lacks, and
            // isn't asked for there either. A section hand-written into the plain half stays unread.
            if (!Brain.SenpaiLive) return null;
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
            if (!Brain.SenpaiOn) return null;
            string named = Hers() ?? Named(For("what she is into"));
            if (string.IsNullOrEmpty(named)) return null;
            return For(Head + " " + named, liked ? "liked" : "cross");
        }

        internal static string Woven(bool liked)
        {
            if (!Brain.SenpaiOn) return null;
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
            if (!Brain.SenpaiOn) return null;
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
                            // A single # is the file's title, not a section, so the title isn't read into her list.
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
            bool senpaiWas = Brain.SenpaiOn;
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
                    ">! Her list, and the lines that go with each one.",
                    ">! Only read with the switch at the bottom of sys.md on.",
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
                Brain.SenpaiOn = true;
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
                Brain.SenpaiOn = senpaiWas;
                try { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
        }

        internal static string For(string pool) { return For(pool, null); }

        // tone picks between the "+" and "-" lines inside a pack. A pack with neither marker is used for both.
        internal static string For(string pool, string tone)
        {
            if (!Brain.SenpaiOn) return null;
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
                            // A single # is the file's title, not a section, so the senpai interests file's title isn't
                            // read into her list.
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

        // Offered but switched off, so her file is a menu rather than a blank; nothing needing a person who can't agree
        // is here. The rest of her list isn't in this source; with nothing attached the lines are simply absent.
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

        // Her interests file as it is written fresh: the whole text less her own sections, which go to her_interests.
        internal static string RandomText
        {
            get
            {
                string whole = Text;
                if (string.IsNullOrEmpty(whole)) return whole;
                StringBuilder made = new StringBuilder();
                bool moving = false;
                foreach (string raw in whole.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("##", StringComparison.Ordinal)) moving = Hers(line.TrimStart('#'));
                    if (!moving) made.Append(raw).Append(Environment.NewLine);
                }
                return made.ToString().TrimEnd() + Environment.NewLine;
            }
        }

        internal static string Text
        {
            get
            {
                StringBuilder made = new StringBuilder();
                if (BodyFor != null) { try { made.Append(Legacy.Modern(BodyFor())); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); } }
                return made.ToString();
            }
        }
    }
}
