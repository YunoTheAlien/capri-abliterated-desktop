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
        // Her flirty lines that make sense with nothing said first, the only ones she opens with.
        internal const string OpenerPool = "flirting opener";
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

        private static bool reworded;

        // Frames grown since her file was first written: its section, the frame and the kinds it reads with. Attached.
        internal static readonly List<string[]> GrownFrames = new List<string[]>();

        // The grown frames reach a file written before them: each missing one at the end of its section, one switched off
        // with ">" counted as had. The count added, or -1 when there is no file to take them yet.
        internal const string GrownMark = ">! grown frames given:";

        internal static int TopUpFrames()
        {
            if (string.IsNullOrEmpty(Kept) || !Brain.SenpaiOn || !File.Exists(Kept)) return -1;
            try
            {
                string had = File.ReadAllText(Kept);
                Match given = Regex.Match(had, "(?m)^" + Regex.Escape(GrownMark) + @"[ \t]*(\d+)");
                int was;
                if (given.Success && int.TryParse(given.Groups[1].Value, out was) && was >= GrownFrames.Count) return 0;
                string newline = had.Contains("\r\n") ? "\r\n" : "\n";
                List<string> rows = new List<string>(had.Replace("\r\n", "\n").Split('\n'));
                int added = 0;
                foreach (string section in new[] { "how she brings it up, pleased", "how she brings it up, cross", OpenerPool })
                {
                    string named = section;
                    List<string> stock = GrownFrames.FindAll(one => one[0] == named).ConvertAll(one => one[1]);
                    int at = rows.FindIndex(row => string.Equals(row.Trim(), "## " + named, StringComparison.OrdinalIgnoreCase));
                    if (stock.Count == 0 || at < 0) continue;
                    int end = at + 1;
                    while (end < rows.Count && !rows[end].TrimStart().StartsWith("#", StringComparison.Ordinal)) end++;
                    HashSet<string> theirs = new HashSet<string>(StringComparer.Ordinal);
                    int last = at;
                    for (int i = at + 1; i < end; i++)
                    {
                        string line = rows[i].Trim().TrimStart('>').Trim();
                        if (line.Length > 0 && !line.StartsWith("!", StringComparison.Ordinal)) { theirs.Add(line); last = i; }
                    }
                    List<string> missing = stock.FindAll(one => !theirs.Contains(one));
                    rows.InsertRange(last + 1, missing);
                    added += missing.Count;
                }
                // How many grown frames the file has been given, so one they delete stays deleted until more are grown.
                rows.RemoveAll(row => row.Trim().StartsWith(GrownMark, StringComparison.Ordinal));
                int head = rows.FindIndex(row => string.Equals(row.Trim(), "## how she brings it up, pleased", StringComparison.OrdinalIgnoreCase));
                if (head >= 0) rows.Insert(head + 1, GrownMark + " " + GrownFrames.Count);
                Tucked.Write(Kept, string.Join(newline, rows.ToArray()), new UTF8Encoding(false));
                return added;
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); return -1; }
        }

        internal static void Write(bool over)
        {
            // Only written with the switch on.
            if (string.IsNullOrEmpty(Kept) || !Brain.SenpaiOn) return;
            try
            {
                // A stock frame reworded since it shipped reaches a file written before, once a run, unless they edited it.
                if (!reworded && File.Exists(Kept)) { reworded = true; Tucked.Free(Kept); Sayings.Reword(Kept); Tucked.Away(Kept); TopUpFrames(); }
                if (!over && File.Exists(Kept)) return;
                string folder = Path.GetDirectoryName(Kept);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) Directory.CreateDirectory(folder);
                Tucked.Write(Kept, Text, new UTF8Encoding(false));
                TopUpFrames();
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

        internal static string Packed(bool liked) { return Packed(liked, Item()); }

        // Where the tail built last came from, and the one said last, for her line log: the pool, the frame and the list.
        [ThreadStatic] private static string madeFrom;
        [ThreadStatic] internal static string TailFrom;

        private static string Packed(bool liked, string named)
        {
            if (!Brain.SenpaiOn || string.IsNullOrEmpty(named)) return null;
            string said = For(Head + " " + named, liked ? "liked" : "cross");
            if (said != null) madeFrom = "interests: " + Head + " " + named;
            return said;
        }

        internal static string Woven(bool liked) { return Woven(liked, Item()); }

        private static string Woven(bool liked, string what)
        {
            if (!Brain.SenpaiOn) return null;
            // Only a frame that reads with what the thing is: "You are doing well. %it% is on the table." takes a thing or
            // an act, never a wish of hers.
            string kind = KindOf(what);
            Func<string, bool> reads = frame => Fits(frame, kind) && Misfit(frame, what, kind) == null;
            // A frame she used lately waits until the others that fit have had their turn, as her other pools do.
            Func<string, bool> fits = frame => reads(frame) && Last(FrameKey(frame)) == DateTime.MinValue;
            // Matched to how she feels about what just happened. An older file with the single combined section
            // still works, it simply does not know the difference.
            string pool = liked ? "how she brings it up, pleased" : "how she brings it up, cross";
            string how = For(pool, null, fits) ?? For(pool, null, reads);
            if (how == null) { pool = "how she brings it up"; how = For(pool, null, fits) ?? For(pool, null, reads); }
            if (string.IsNullOrEmpty(how) || string.IsNullOrEmpty(what)) return null;
            string said = how.Replace("%it%", what).Replace("%IT%", what);
            if (said == how) return null;
            madeFrom = "interests: " + pool + " \"" + how + "\" with " + (itemFrom ?? "an item");
            madeFrame = how;
            return said;
        }

        // A frame used, kept by its own key beside her lines.
        private static string FrameKey(string frame) { return "sys:tail frame " + frame; }

        [ThreadStatic] private static string madeFrame;

        [ThreadStatic] private static string itemFrom;

        private static string Item()
        {
            string hers = Hers();
            itemFrom = hers != null ? Personalities.Chosen + ": into" : "interests: what she is into";
            return hers ?? Named(For("what she is into"));
        }

        // A thing off her list is a "thing" (a noun), an "act" (done to them) or a "wish" (hers). Unnamed: "being ..." or
        // naming her is a wish, else an act on a pack's list and a thing on the shared one, as its header asks.
        internal static Dictionary<string, string> ItemKinds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // The kinds each frame reads with, space separated. One not named takes a thing or an act, which is what the
        // list's header promises every line will finish.
        internal static Dictionary<string, string> FrameKinds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        internal static string KindOf(string item)
        {
            string named = Named(item), kind;
            if (named.Length == 0) return "thing";
            if (ItemKinds.TryGetValue(named, out kind)) return kind;
            if (Regex.IsMatch(named, @"^being\b|\b(?:me|my|myself)\b", RegexOptions.IgnoreCase)) return "wish";
            return InPack(named) ? "act" : "thing";
        }

        internal static bool Fits(string frame, string kind)
        {
            string kinds;
            if (!FrameKinds.TryGetValue((frame ?? "").Trim(), out kinds)) kinds = "thing act";
            return Array.IndexOf(kinds.Split(' '), kind) >= 0;
        }

        // Why a frame and a thing off her list read wrong together, each seen in her line log: a time of day, a noun word
        // before an -ing phrase, her wish handed to them, a doubled word. Null when the pair reads right.
        private static readonly Regex TimeWords = new Regex(@"\b(all (day|night|evening|morning|week)|today|tonight|tomorrow|yesterday|this (morning|evening|afternoon|week)|every (day|night))\b", RegexOptions.IgnoreCase);
        private static readonly Regex NounAhead = new Regex(@"\b(a|an|the|your|my|his|her|some|this|that)\s+%it%", RegexOptions.IgnoreCase);
        private static readonly Regex TakesIng = new Regex(@"(?:^|[.!?]\s+)%it%|\b(about|of|for|from|into|on|at|by|with|without|after|before|like|enjoy|love|miss|imagine|imagining|fancy|mind|risk|keep|consider|stop|start|try|practise|(closer|forward|straight|skip|back|down|comes|come|up|leads|lead|used|right|get|got|go|move|on) to)\s+%it%", RegexOptions.IgnoreCase);
        private static readonly Regex HandedOver = new Regex(@"\b(give|giving|earn|earned|deserve|deserves|reward|rewarded|owe|treat)\s+(you|yourself)\b[^.!?]*%it%|%it%\s+(is|are)\s+yours", RegexOptions.IgnoreCase);

        internal static string Misfit(string frame, string item, string kind)
        {
            frame = frame ?? "";
            if (TimeWords.IsMatch(frame)) return "a time of day in the frame";
            if (NounAhead.IsMatch(frame) && kind != "thing") return "a word before the slot that wants a noun";
            if ((kind == "act" || kind == "wish") && !TakesIng.IsMatch(frame)) return "the slot after a word that takes no -ing phrase";
            if (kind == "wish" && HandedOver.IsMatch(frame)) return "her wish handed to them";
            string said = frame.Replace("%it%", item ?? "");
            if (Regex.IsMatch(said, @"\b(\w+)\s+\1\b", RegexOptions.IgnoreCase)) return "a doubled word";
            if (Regex.IsMatch(said, @"\b(is|are|was|be|been|being)\s+being\b", RegexOptions.IgnoreCase)) return "two passives together";
            return null;
        }

        private static bool InPack(string item)
        {
            if (!Brain.SenpaiLive) return false;
            try
            {
                string[] mine = Personalities.Lines("into", null);
                return mine != null && Array.Exists(mine, one => string.Equals(Named(one), item, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception) { return false; }
        }

        // Her senpai tail on a line: her pack's own, else a frame filled with one thing off her list. Each thing goes round
        // before one comes back, and words she said lately are built again; both kept in her lines' memory across restarts.
        internal static string Tail(bool liked)
        {
            return Tail(delegate
            {
                string item = Item();
                return new KeyValuePair<string, string>(Packed(liked, item) ?? Woven(liked, item), item);
            });
        }

        // The tail and the thing it names, from make: a thing not named lately with words not said lately, else the thing
        // named longest ago, then the words said longest ago.
        internal static string Tail(Func<KeyValuePair<string, string>> make)
        {
            string best = null, bestItem = null, bestFrom = null, bestFrame = null;
            DateTime bestItemAt = DateTime.MaxValue, bestWordsAt = DateTime.MaxValue;
            TailFrom = null;
            // A fresh one ends it at once; only with every one recent does it look long, so the oldest isn't missed.
            for (int tries = 0; tries < 100; tries++)
            {
                madeFrom = null; madeFrame = null;
                KeyValuePair<string, string> built = make();
                if (string.IsNullOrEmpty(built.Key)) break;
                DateTime itemAt = Last(built.Value == null ? null : ItemKey(built.Value)), wordsAt = Last(built.Key);
                if (itemAt == DateTime.MinValue && wordsAt == DateTime.MinValue) { best = built.Key; bestItem = built.Value; bestFrom = madeFrom; bestFrame = madeFrame; break; }
                if (itemAt < bestItemAt || itemAt == bestItemAt && wordsAt < bestWordsAt)
                {
                    best = built.Key; bestItem = built.Value; bestItemAt = itemAt; bestWordsAt = wordsAt; bestFrom = madeFrom; bestFrame = madeFrame;
                }
            }
            if (best == null) return null;
            TailFrom = bestFrom;
            if (bestFrame != null) Sayings.Remember(FrameKey(bestFrame));
            Sayings.Remember(best);
            if (bestItem != null) Sayings.Remember(ItemKey(bestItem));
            return best;
        }

        // The thing a tail names, kept by its own key beside her lines.
        private static string ItemKey(string item) { return "sys:tail names " + item; }

        // When she last said it within her recent window, or MinValue when not lately.
        private static DateTime Last(string said)
        {
            DateTime when;
            return said != null && Sayings.SaidLately(said) && Sayings.LastSaid(said, out when) ? when : DateTime.MinValue;
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
#if CHECKS
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

        // Every shipped frame with every thing it is tagged for, each personality's list included, read by the pairing
        // rules; the rules are tried on the faults from her log first.
        private static void PairCheck(System.Collections.Generic.List<string> checks)
        {
            System.Collections.Generic.List<string> wrong = new System.Collections.Generic.List<string>();
            if (Misfit("I have been thinking about %it% all day.", "staying up late", "wish") == null || Misfit("I want %it%.", "being kept close", "wish") == null
                || Misfit("You deserve %it%.", "being kept close", "wish") == null || Misfit("Carry on and we are closer to %it%.", "being kept close", "wish") != null)
                wrong.Add("the rules read her log's faults wrongly");
            Dictionary<string, string> things = new Dictionary<string, string>(ItemKinds, StringComparer.OrdinalIgnoreCase);
            // Each personality's list as it ships, from the attached half of its pack.
            int lists = 0;
            Func<string, string> senpaiFor = Personalities.SenpaiFor;
            foreach (string named in Personalities.Names)
            {
                string pack = senpaiFor == null ? null : senpaiFor(named);
                if (string.IsNullOrEmpty(pack)) continue;
                bool under = false, any = false;
                foreach (string raw in pack.Replace("\r\n", "\n").Split('\n'))
                {
                    string one = raw.Trim();
                    if (one.StartsWith("#", StringComparison.Ordinal)) { under = one.StartsWith("## What she is into", StringComparison.OrdinalIgnoreCase); continue; }
                    if (!under || one.Length == 0 || one.StartsWith(">", StringComparison.Ordinal)) continue;
                    string item = Named(one);
                    if (item.Length == 0) continue;
                    any = true;
                    // Read as her pack would be, where an unnamed thing on a pack's list is an act unless its words make it a wish.
                    if (!things.ContainsKey(item)) things[item] = ItemKinds.ContainsKey(item) ? ItemKinds[item] : Regex.IsMatch(item, @"^being\b|\b(?:me|my|myself)\b", RegexOptions.IgnoreCase) ? "wish" : "act";
                }
                if (any) lists++;
            }
            int pairs = 0;
            foreach (KeyValuePair<string, string> frame in FrameKinds)
                foreach (KeyValuePair<string, string> thing in things)
                {
                    if (!Fits(frame.Key, thing.Value)) continue;
                    pairs++;
                    string why = Misfit(frame.Key, thing.Key, thing.Value);
                    if (why != null && wrong.Count < 8) wrong.Add(why + ": " + frame.Key.Replace("%it%", thing.Key));
                }
            if (wrong.Count > 0) throw new Exception("A shipped frame reads wrongly with a thing it is tagged for: " + string.Join(" | ", wrong.ToArray()));
            checks.Add("PASS every shipped frame read with every thing it is tagged for, " + pairs + " pairs from " + FrameKinds.Count + " frames and " + things.Count + " things over " + lists + " personalities' lists: no time of day, no noun word before an -ing phrase, no wish handed over, no doubled word");
        }

        // A thing off her list only in a frame of its kind: a wish of hers is never on the table nor a threat, and a thing
        // still takes them all.
        internal static void KindCheck(System.Collections.Generic.List<string> checks)
        {
            string keptWas = Kept, hersWas = HersKept, yoursWas = YoursKept;
            bool senpaiWas = Brain.SenpaiOn;
            string folder = Path.Combine(Path.GetTempPath(), "capri-kinds-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                Directory.CreateDirectory(folder);
                string hers = Path.Combine(folder, "her_interests");
                File.WriteAllText(hers, string.Join(Environment.NewLine, new[]
                {
                    "# What she is into", "",
                    "## how she brings it up, pleased", "", "You are doing well. %it% is on the table.", "I keep thinking about %it%.", "I have been thinking about %it% all day.", "",
                    "## how she brings it up, cross", "", "That is going to end with %it%.", "",
                }), new UTF8Encoding(false));
                Kept = Path.Combine(folder, WasFileName ?? FileName);
                HersKept = hers;
                YoursKept = Path.Combine(folder, "your_interests");
                Brain.SenpaiOn = true;
                System.Collections.Generic.List<string> wrong = new System.Collections.Generic.List<string>();
                if (KindOf("being Zz") != "wish" || KindOf("Zz for me") != "wish" || KindOf("Zz") != "thing") wrong.Add("kinds read by their words: " + KindOf("being Zz") + ", " + KindOf("Zz for me") + ", " + KindOf("Zz"));
                int wished = 0, thingTabled = 0;
                for (int go = 0; go < 200; go++)
                {
                    string pleased = Woven(true, "being Zz"), cross = Woven(false, "being Zz"), thing = Woven(true, "Zz");
                    if (pleased != null && (pleased.IndexOf("on the table", StringComparison.Ordinal) >= 0 || pleased.IndexOf("all day", StringComparison.Ordinal) >= 0)) wrong.Add(pleased);
                    if (pleased != null) wished++;
                    if (cross != null) wrong.Add(cross);
                    if (thing != null && thing.IndexOf("Zz is on the table", StringComparison.Ordinal) >= 0) thingTabled++;
                }
                if (wished == 0) wrong.Add("a wish was never woven into a frame it reads with");
                if (thingTabled == 0) wrong.Add("a thing never took the frame a wish can't");
                if (wrong.Count > 0) throw new Exception("A frame took a thing of a kind it doesn't read with: " + string.Join(" | ", wrong.GetRange(0, Math.Min(5, wrong.Count)).ToArray()));
                checks.Add("PASS a thing off her list goes only into a frame of its kind in 200 weaves: a wish of hers is thought about (" + wished + "), never on the table, never all day and never a threat; a thing still is on the table (" + thingTabled + ")");
                PairCheck(checks);
                // Four frames a thing reads with: four tails use all four before any comes back.
                File.WriteAllText(hers, string.Join(Environment.NewLine, new[]
                {
                    "# What she is into", "",
                    "## how she brings it up, pleased", "", "I keep thinking about %it%.", "We are one step closer to %it%.", "Careful, this ends with %it%.", "Do not talk me into %it%.", "",
                }), new UTF8Encoding(false));
                Action putBack = Sayings.HeldSaid();
                try
                {
                    System.Collections.Generic.HashSet<string> frames = new System.Collections.Generic.HashSet<string>();
                    for (int one = 0; one < 4; one++)
                    {
                        string thing = "Zq" + one;
                        if (Tail(delegate { madeFrame = null; string woven = Woven(true, thing); return new KeyValuePair<string, string>(woven, thing); }) == null || madeFrame == null) break;
                        frames.Add(madeFrame);
                    }
                    if (frames.Count != 4) throw new Exception("A frame came back before the others had their turn: " + frames.Count + " different in four tails");
                }
                finally { putBack(); }
                checks.Add("PASS four tails use four different frames before any comes back");
                // Grown frames reach an older file once; one they delete afterwards stays deleted.
                List<string[]> grownWas = new List<string[]>(GrownFrames);
                try
                {
                    GrownFrames.Clear();
                    GrownFrames.Add(new[] { "how she brings it up, pleased", "Zz frame one %it%.", "thing" });
                    GrownFrames.Add(new[] { "how she brings it up, cross", "Zz frame two %it%.", "thing" });
                    Kept = hers;
                    int first = TopUpFrames(), again = TopUpFrames();
                    Tucked.Write(hers, File.ReadAllText(hers).Replace("Zz frame one %it%.", ""), new UTF8Encoding(false));
                    int afterDelete = TopUpFrames();
                    string now = File.ReadAllText(hers);
                    if (first != 1 || again != 0 || afterDelete != 0 || now.Contains("Zz frame one") || now.IndexOf(GrownMark + " 2", StringComparison.Ordinal) < 0)
                        throw new Exception("Grown frames didn't reach an older file once: " + first + ", " + again + ", " + afterDelete);
                }
                finally { GrownFrames.Clear(); GrownFrames.AddRange(grownWas); }
                checks.Add("PASS grown frames reach an older interests file once, where it has the section, and one deleted afterwards stays deleted");
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
#endif

        internal static string For(string pool) { return For(pool, null); }

        // tone picks between the "+" and "-" lines inside a pack. A pack with neither marker is used for both.
        internal static string For(string pool, string tone) { return For(pool, tone, null); }

        // fits, when given, keeps only the lines it passes, read before her tokens are filled.
        internal static string For(string pool, string tone, Func<string, bool> fits)
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
                // A line naming a part of the day only in that part, as everywhere else she talks.
                found.RemoveAll(one => !Sayings.FitsHour(one));
                if (fits != null) found.RemoveAll(one => !fits(one));
                if (found.Count == 0) return null;
                // A line of hers straight from this file names the pool in her log; inside a built line the core's name stands.
                Sayings.NamedFrom("interests: " + pool);
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

namespace CapriDesktop
{
    internal sealed partial class Companion
    {
        // A senpai tail from four things and three frames, twenty in a row: no words again within the last eight, never
        // the same thing twice running, and the first four name all four across a restart. Stand-in words only.
#if CHECKS
        private void SmokeTailRepeats(List<string> checks)
        {
            string token = Guid.NewGuid().ToString("N").Substring(0, 8);
            string[] frames = { "first frame " + token + " %it%", "second frame " + token + " %it%", "third frame " + token + " %it%" };
            string[] items = { "apple" + token, "brick" + token, "cloud" + token, "drum" + token };
            Random pick = new Random(7);
            List<string> said = new List<string>(), named = new List<string>();
            for (int i = 0; i < 20; i++)
            {
                string tail = Interests.Tail(delegate
                {
                    string item = items[pick.Next(items.Length)];
                    return new KeyValuePair<string, string>(frames[pick.Next(frames.Length)].Replace("%it%", item), item);
                });
                said.Add(tail);
                named.Add(Array.Find(items, one => tail != null && tail.EndsWith(one, StringComparison.Ordinal)));
                // Her memory dates each line, so twenty in one instant would all be as old as each other.
                System.Threading.Thread.Sleep(20);
                // Half way round the four, she is started again: her memory is saved and read back from the file.
                if (i == 1) Sayings.Restarted();
            }
            List<string> wrong = new List<string>();
            for (int i = 0; i < said.Count; i++)
            {
                if (said[i] == null) { wrong.Add("tail " + (i + 1) + " was none"); continue; }
                for (int back = 1; back <= 8 && back <= i; back++) if (said[i - back] == said[i]) wrong.Add("tail " + (i + 1) + " repeats tail " + (i + 1 - back));
                if (i > 0 && named[i] == named[i - 1]) wrong.Add("tail " + (i + 1) + " names the same thing as the one before");
            }
            if (new HashSet<string>(named.GetRange(0, 4)).Count != 4) wrong.Add("the first four named " + string.Join(", ", named.GetRange(0, 4).ToArray()) + ", not all four");
            if (wrong.Count > 0) throw new Exception("Her senpai tails repeated: " + string.Join("; ", wrong.ToArray()));
            checks.Add("PASS her senpai tails: from four things and three frames, the first four name all four across a restart, and twenty in a row never repeat words within the last eight or name the same thing twice running, kept in her lines' own memory");
        }
#endif
    }
}
