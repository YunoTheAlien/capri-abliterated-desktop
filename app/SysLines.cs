using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // The little grey labels she shows after "> sys: ": "pets you", "settings", "brain online" and the rest. They were
    // twelve hardcoded strings, so the one part of her voice nobody could change was the part that narrates her.
    //
    // Every label already passes through one place on its way to the bubble, so this is a lookup in front of that with
    // the author's text as the fallback. A file that is missing, empty, mistyped or half-deleted degrades to stock
    // rather than to blank, which matters because a blank label would read as a bug in her rather than in the file.
    //
    // Same shape as twitch.md: read when it changes, UTF-8 with no byte order mark, and written once with every line
    // already in it so there is nothing to guess at.
    internal static class SysLines
    {
        internal const string FileName = "sys.md";
        // The adult switch. Kept here, at the bottom of a file about her wording, rather than on a menu
        // anyone can hit by accident. 1 is on, anything else is off.
        internal const string SenpaiKey = "uncensored";

        // Programs she will not look at with Watch my screen on. Comma separated, matched against the program's
        // own name and its window title, so "brave" covers brave.exe and "qview" covers the image viewer whatever
        // it calls its window. Her screen watching is the one feature that reads whatever happens to be in front,
        // so the list of what it must not read belongs with her other settings and not buried in a menu.
        internal const string ExcludedKey = "never watch";

        // Her calendar, by opening the one they use. Nothing signs in anywhere: she opens the app and reads what
        // is on screen, the same as anything else she looks at. Blank means she works out which one from what
        // they keep in front; a name here settles it.
        internal const string CalendarKey = "my calendar";

        // Whether her hello and goodbye come from whoever she is, or from the lines below. 1 is her personality.
        // Her error line is never part of this: that one has to read the same whoever she is, or a report of it
        // means nothing.
        internal const string OwnLinesKey = "personality lines";
        internal static string Kept;

        private static readonly object writing = new object();
        private static readonly Dictionary<string, string> theirs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static DateTime read = DateTime.MinValue;
        private static long wasLong = -1;

        // The author's labels, in the order Said.Labels registers them. The left side is the name used in code and must
        // not change; the right side is only what is shown.
        internal static readonly string[,] Stock =
        {
            { "startup", "startup" },
            { "pets you", "pets you" },
            { "slaps you", "slaps you" },
            { "picks you up", "picks you up" },
            { "clang", "clang" },
            { "throws you", "throws you" },
            { "reaches for you", "reaches for you" },
            { "settings", "settings" },
            { "voice check", "voice check" },
            { "get to know me", "get to know me" },
            { "updated", "updated" },
            { "brain online", "brain online" },
            { "her clones", "her clones" },
            { "holds Esc", "holds Esc" },
            { "holiday", "holiday" },
            { "distracted", "distracted" },
            { "error", "error" }
        };

        // Lines she actually says, as opposed to the labels above. Several can be offered at once, split on "|", and she
        // takes one at random. Her hellos were picked by the hour out of three fixed strings, so the first thing she ever
        // said was the one thing nobody could rewrite.
        internal static readonly string[,] Spoken =
        {
            { "hello morning", "Good morning, choom." },
            { "hello late", "Isn't it late, choom?" },
            { "hello day", "Miss me, choom?" },
            { "goodbye", "Later, choom. | Don't be long. | Going dark. | See you soon." },
            { "broken", "There's something seriously broken with my code." }
        };

        // The two prefixes her bubble puts in front of a line. Kept as their own keys so "> sys: " can become "Capri: "
        // or anything else, without touching the marker the code uses to know a line is a system one.
        internal const string SysMark = "> sys: ";
        internal const string YouMark = "> you: ";

        internal static WrittenNote Open() { Write(false); return new WrittenNote(Kept, Text, writing); }

        // Whether adult mode is on, straight out of the file. Null when the file does not mention it yet, which is
        // how an older sys.md is told apart from one deliberately set to off.
        internal static bool? Senpai
        {
            get
            {
                Fresh();
                lock (writing)
                {
                    string said;
                    if (!theirs.TryGetValue(SenpaiKey, out said)) return null;
                    said = (said ?? "").Trim();
                    if (said.Length == 0) return null;
                    return said == "1" || said.Equals("yes", StringComparison.OrdinalIgnoreCase) || said.Equals("on", StringComparison.OrdinalIgnoreCase) || said.Equals("true", StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        // Puts the line in a file that has not got one, carrying over whatever they were already running so nobody
        // loses the mode they were using by us moving the switch.
        internal static void SetSenpai(bool on)
        {
            if (string.IsNullOrEmpty(Kept)) return;
            try
            {
                if (!File.Exists(Kept)) { Write(false); }
                string had = File.ReadAllText(Kept);
                string want = SenpaiKey + ": " + (on ? "1" : "0");
                Match found = Regex.Match(had, @"(?mi)^[ \t]*" + Regex.Escape(SenpaiKey) + @"[ \t]*:[ \t]*.*$");
                if (found.Success) had = had.Substring(0, found.Index) + want + had.Substring(found.Index + found.Length);
                else
                {
                    if (had.Length > 0 && !had.EndsWith("\n")) had += Environment.NewLine;
                    had += Environment.NewLine + "## Adult mode" + Environment.NewLine + Environment.NewLine
                        + "> WARNING. Setting this to 1 turns on sexual content, intended for adults only." + Environment.NewLine
                        + "> It unlocks her senpai lines, interests.md, and the personalities and moods marked" + Environment.NewLine
                        + "> adult. Leave it at 0 if anyone else uses this PC or you do not want any of that." + Environment.NewLine
                        + "> 1 is on. Anything else is off." + Environment.NewLine + Environment.NewLine + want + Environment.NewLine;
                }
                File.WriteAllText(Kept, had, new UTF8Encoding(false));
                lock (writing) { read = DateTime.MinValue; wasLong = -1; }
            }
            catch (Exception) { }
        }

        // Their list, split and tidied. Empty when they have not set any, which is the default.
        internal static string[] NeverWatch()
        {
            string said = Of(ExcludedKey);
            if (string.IsNullOrWhiteSpace(said)) return new string[0];
            List<string> found = new List<string>();
            foreach (string one in said.Split(',', ';'))
            {
                string name = (one ?? "").Trim();
                // A bare ".exe" or a one letter entry would match most of the desktop, so they are ignored.
                if (name.Length >= 2) found.Add(name);
            }
            return found.ToArray();
        }

        // Whether her own hello and goodbye win over the ones in this file. On unless they say otherwise.
        internal static bool OwnComings
        {
            get
            {
                string said = (Of(OwnLinesKey) ?? "").Trim();
                return said.Length == 0 || said == "1" || said.Equals("yes", StringComparison.OrdinalIgnoreCase)
                    || said.Equals("true", StringComparison.OrdinalIgnoreCase) || said.Equals("on", StringComparison.OrdinalIgnoreCase);
            }
        }

        // Which app to open for their calendar: what they named, else the one they keep in front.
        internal static string Calendar()
        {
            string said = Of(CalendarKey);
            if (!string.IsNullOrWhiteSpace(said)) return said.Trim();
            try { return Favourites.Best("calendar"); } catch (Exception) { return null; }
        }

        // Whether this is one of them. Program and title both, because some of these are known by one and not the
        // other: a browser is brave.exe, an image viewer is whatever file it happens to have open.
        internal static bool Skipped(string program, string title)
        {
            foreach (string one in NeverWatch())
            {
                if (!string.IsNullOrEmpty(program) && program.IndexOf(one, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (!string.IsNullOrEmpty(title) && title.IndexOf(one, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        // What to actually draw in front of a line. The stock prefix goes in, theirs comes out.
        internal static string Prefix(string mark)
        {
            string key = mark == SysMark ? "sys prompt" : mark == YouMark ? "you prompt" : null;
            if (key == null) return mark;
            string said = Of(key);
            if (said == null) return mark;
            // A prefix that runs straight into the words is a typo every time, so one space is kept for them.
            return said.EndsWith(" ") ? said : said + " ";
        }

        // One of the alternatives under a name, or null when they have not set any. Blank pieces are dropped so a stray
        // bar in the middle is not a silent line.
        internal static string Any(string name, Random luck)
        {
            string said = Of(name) ?? Stocked(name);
            if (said == null) return null;
            if (said.IndexOf('|') < 0) return said;
            List<string> parts = new List<string>();
            foreach (string piece in said.Split('|'))
            {
                string one = piece.Trim();
                if (one.Length > 0) parts.Add(one);
            }
            if (parts.Count == 0) return null;
            return parts[(luck ?? new Random()).Next(parts.Count)];
        }

        // The author's own text for a name, so a caller can fall back without holding its own copy.
        internal static string Stocked(string name)
        {
            for (int line = 0; line < Spoken.GetLength(0); line++)
                if (string.Equals(Spoken[line, 0], name, StringComparison.OrdinalIgnoreCase)) return Spoken[line, 1];
            for (int line = 0; line < Stock.GetLength(0); line++)
                if (string.Equals(Stock[line, 0], name, StringComparison.OrdinalIgnoreCase)) return Stock[line, 1];
            return null;
        }

        // Their text for a label, or null when they have not set one. Null rather than the stock string, so the caller
        // can still send an unchanged label through her translator the way it always did.
        internal static string Of(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Fresh();
            lock (writing)
            {
                string said;
                if (!theirs.TryGetValue(name, out said)) return null;
                said = (said ?? "").Trim();
                if (said.Length == 0) return null;
                // Unchanged from stock is not an override: it should still be translated like the author's own line.
                return string.Equals(said, name, StringComparison.Ordinal) ? null : said;
            }
        }

        // Re-read only when the file actually changed, since this is called for every bubble.
        private static void Fresh()
        {
            if (string.IsNullOrEmpty(Kept)) return;
            try
            {
                FileInfo about = new FileInfo(Kept);
                if (!about.Exists)
                {
                    lock (writing) { if (theirs.Count > 0) theirs.Clear(); read = DateTime.MinValue; wasLong = -1; }
                    return;
                }
                if (about.LastWriteTimeUtc == read && about.Length == wasLong) return;
                string[] lines = File.ReadAllLines(Kept);
                lock (writing)
                {
                    theirs.Clear();
                    foreach (string line in lines)
                    {
                        string one = (line ?? "").Trim();
                        if (one.Length == 0 || one.StartsWith("#") || one.StartsWith(">")) continue;
                        int colon = one.IndexOf(':');
                        if (colon <= 0) continue;
                        string name = one.Substring(0, colon).Trim(), said = one.Substring(colon + 1).Trim();
                        if (name.Length > 0) theirs[name] = said;
                    }
                    read = about.LastWriteTimeUtc;
                    wasLong = about.Length;
                }
            }
            catch (Exception) { }
        }

        // Adult mode is the last thing in the file and has to stay there. It is the one switch in here with a
        // consequence, so it wants reading deliberately at the bottom rather than coming upon it halfway down
        // between two label rows.
        //
        // It did not stay there on its own: every name added by a later build was appended to the end of the file,
        // which put it after this block and left the switch stranded in the middle of the file it was meant to end.
        internal const string SenpaiHeading = "## Adult mode";

        // The warning that goes with it, so a block that has to be rebuilt still carries one.
        private static readonly string[] SenpaiNote =
        {
            "> WARNING. Setting this to 1 turns on sexual content, intended for adults only.",
            "> It unlocks her senpai lines, interests.md, and the personalities and moods marked",
            "> adult. Leave it at 0 if anyone else uses this PC or you do not want any of that.",
            "> 1 is on. Anything else is off."
        };

        // Every line of the file written out with CRLF stripped, which is the whole reason this is done by line
        // rather than by one regex over the text: an earlier attempt anchored its heading match with "$", which
        // cannot match the carriage return the file actually has, so the heading was never found and only the key
        // moved, leaving the warning stranded halfway up the file away from the switch it warns about.
        private static List<string> Rows(string had)
        {
            return new List<string>((had ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'));
        }

        private static bool IsSenpaiHead(string row) { return Regex.IsMatch(row ?? "", @"^[ \t]*##[ \t]*Adult mode[ \t]*$", RegexOptions.IgnoreCase); }
        private static bool IsSenpaiKey(string row) { return Regex.IsMatch(row ?? "", @"^[ \t]*" + Regex.Escape(SenpaiKey) + @"[ \t]*:", RegexOptions.IgnoreCase); }

        // The block pulled out of the file: its heading, whatever note follows it, and the key wherever that ended
        // up. What comes back is the rest of the file, and the block itself through "block".
        private static List<string> WithoutSenpai(List<string> rows, out List<string> block, out string key)
        {
            block = new List<string>();
            key = null;
            List<string> rest = new List<string>();
            for (int at = 0; at < rows.Count; at++)
            {
                string row = rows[at];
                if (IsSenpaiKey(row)) { key = row.Trim(); continue; }
                if (!IsSenpaiHead(row)) { rest.Add(row); continue; }
                // Its heading, and the note under it: blank lines and quoted lines, up to anything else.
                block.Add(row.Trim());
                int look = at + 1;
                while (look < rows.Count)
                {
                    string next = rows[look];
                    if (next.Trim().Length == 0) { look++; continue; }
                    if (!next.TrimStart().StartsWith(">", StringComparison.Ordinal)) break;
                    block.Add(next.Trim());
                    look++;
                }
                at = look - 1;
            }
            return rest;
        }

        // Adult mode is the last thing in the file and stays there. It is the one switch in here with a consequence,
        // so it wants reading deliberately at the end rather than being come upon halfway down between two label
        // rows. It did not stay there on its own: every name a later build added was appended to the end of the
        // file, which pushed it up into the middle.
        //
        // Rebuilt rather than nudged, so a file where the heading and the key have already drifted apart is put
        // back together instead of being left as it is.
        // Nothing may be in this file twice.
        //
        // Keys are read into a dictionary as the file is walked, so the last one wins, and a top up appends at
        // the end. An edited line near the top was therefore quietly overridden by the stock copy added below
        // it, and their edit looked like it had done nothing. The edit is the one above, so the first of each
        // key is kept and every later copy goes, with any heading left empty going too.
        private static void Tidy()
        {
            try
            {
                if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept)) return;
                string[] rows = File.ReadAllText(Kept).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                Dictionary<string, bool> had = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                List<string> kept = new List<string>();
                bool dropped = false;
                foreach (string raw in rows)
                {
                    string line = raw.Trim();
                    int mark = line.IndexOf(':');
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith(">", StringComparison.Ordinal) || mark <= 0)
                    { kept.Add(raw); continue; }
                    string name = line.Substring(0, mark).Trim();
                    if (name.Length == 0) { kept.Add(raw); continue; }
                    if (had.ContainsKey(name)) { dropped = true; continue; }
                    had[name] = true;
                    kept.Add(raw);
                }
                if (!dropped) return;
                List<string> made = new List<string>();
                for (int at = 0; at < kept.Count; at++)
                {
                    if (!kept[at].TrimStart().StartsWith("##", StringComparison.Ordinal)) { made.Add(kept[at]); continue; }
                    bool anything = false;
                    for (int look = at + 1; look < kept.Count; look++)
                    {
                        string next = kept[look].Trim();
                        if (next.StartsWith("##", StringComparison.Ordinal)) break;
                        if (next.Length > 0 && !next.StartsWith(">", StringComparison.Ordinal)) { anything = true; break; }
                    }
                    if (anything) made.Add(kept[at]);
                    else while (at + 1 < kept.Count && !kept[at + 1].TrimStart().StartsWith("##", StringComparison.Ordinal)) at++;
                }
                while (made.Count > 0 && made[made.Count - 1].Trim().Length == 0) made.RemoveAt(made.Count - 1);
                File.WriteAllText(Kept, string.Join(Environment.NewLine, made.ToArray()) + Environment.NewLine, new UTF8Encoding(false));
                lock (writing) { read = DateTime.MinValue; wasLong = -1; }
            }
            catch (Exception) { }
        }

        private static void SenpaiLast()
        {
            try
            {
                if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept)) return;
                string had = File.ReadAllText(Kept);
                List<string> block, rows = Rows(had);
                string key;
                List<string> rest = WithoutSenpai(rows, out block, out key);
                if (key == null) return;
                if (block.Count == 0) { block.Add("## Adult mode"); block.AddRange(SenpaiNote); }
                // A blank line under the heading, like every other section in the file has.
                if (block.Count > 1 && block[1].Length > 0) block.Insert(1, "");
                string made = string.Join(Environment.NewLine, rest.ToArray()).TrimEnd()
                            + Environment.NewLine + Environment.NewLine
                            + string.Join(Environment.NewLine, block.ToArray())
                            + Environment.NewLine + Environment.NewLine + key + Environment.NewLine;
                if (made == had) return;
                File.WriteAllText(Kept, made, new UTF8Encoding(false));
                lock (writing) { read = DateTime.MinValue; wasLong = -1; }
            }
            catch (Exception) { }
        }

        // A file written by an older build has none of the names added since, and "write only if missing" means it never
        // gets them: the setting is in the code, documented, and absent from the one file that is meant to expose it. So
        // every name the build knows is appended to a file that lacks it, with their own edits left exactly as they are.
        internal static void TopUp()
        {
            if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept)) return;
            try
            {
                Fresh();
                List<string> adding = new List<string>();
                lock (writing)
                {
                    if (!theirs.ContainsKey("sys prompt") || !theirs.ContainsKey("you prompt"))
                    {
                        adding.Add("");
                        adding.Add("## What goes in front of a line");
                        adding.Add("");
                        adding.Add("> Change these to anything. A trailing space is added for you.");
                        adding.Add("");
                        if (!theirs.ContainsKey("sys prompt")) adding.Add(Line("sys prompt", SysMark));
                        if (!theirs.ContainsKey("you prompt")) adding.Add(Line("you prompt", YouMark));
                    }
                    List<string> words = new List<string>();
                    for (int line = 0; line < Spoken.GetLength(0); line++)
                        if (!theirs.ContainsKey(Spoken[line, 0])) words.Add(Line(Spoken[line, 0], Spoken[line, 1]));
                    if (words.Count > 0)
                    {
                        adding.Add("");
                        adding.Add("## What she says herself");
                        adding.Add("");
                        adding.Add("> Real speech, not labels. Offer several with \"|\" between them and she picks one.");
                        adding.Add("");
                        adding.AddRange(words);
                    }
                    if (!theirs.ContainsKey(ExcludedKey))
                    {
                        adding.Add("");
                        adding.Add("## Never watch these");
                        adding.Add("");
                        adding.Add("> Programs Watch my screen skips. Comma separated, and a partial name is enough:");
                        adding.Add("> \"brave\" covers brave.exe. Matched on the program and on its window title.");
                        adding.Add("> She says nothing at all while one of these is in front.");
                        adding.Add("");
                        adding.Add(Line(ExcludedKey, ""));
                    }
                    if (!theirs.ContainsKey(CalendarKey))
                    {
                        adding.Add("");
                        adding.Add("## Her calendar");
                        adding.Add("");
                        adding.Add("> The app she opens to read your calendar. Blank and she uses the one you open most.");
                        adding.Add("> She opens it and reads the screen. Nothing signs in and nothing is sent anywhere.");
                        adding.Add("");
                        adding.Add(Line(CalendarKey, ""));
                    }
                    if (!theirs.ContainsKey(OwnLinesKey))
                    {
                        adding.Add("");
                        adding.Add("## How she comes and goes");
                        adding.Add("");
                        adding.Add("> 1: her hello and goodbye come from her personality file. 0: from the lines above.");
                        adding.Add("> Her error line is never affected either way.");
                        adding.Add("");
                        adding.Add(Line(OwnLinesKey, "1"));
                    }
                    List<string> tags = new List<string>();
                    for (int line = 0; line < Stock.GetLength(0); line++)
                        if (!theirs.ContainsKey(Stock[line, 0])) tags.Add(Line(Stock[line, 0], Stock[line, 1]));
                    if (tags.Count > 0) { adding.Add(""); adding.AddRange(tags); }
                }
                if (adding.Count == 0) { Empties(); Tidy(); SenpaiLast(); return; }
                File.WriteAllText(Kept, Fitted(File.ReadAllText(Kept), adding), new UTF8Encoding(false));
                read = DateTime.MinValue;
                wasLong = -1;
                Notes.Refresh(Kept, Text);
                Empties();
                Tidy();
                SenpaiLast();
            }
            catch (Exception) { }
        }

        // A name added by a later build goes under the heading it belongs to, when the file already has that
        // heading. Appending a second copy reads the same to her, but it leaves sys.md with two sections of the
        // same name and the settings for one thing in two places, which is exactly what makes a file unreadable.
        private static string Fitted(string body, List<string> adding)
        {
            List<string> heads = new List<string>();
            Dictionary<string, List<string>> want = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<string>> whole = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            string under = "";
            heads.Add(under);
            want[under] = new List<string>();
            whole[under] = new List<string>();
            foreach (string row in adding)
            {
                string one = (row ?? "").Trim();
                if (one.StartsWith("##", StringComparison.Ordinal))
                {
                    under = one.TrimStart('#').Trim();
                    if (!want.ContainsKey(under)) { heads.Add(under); want[under] = new List<string>(); whole[under] = new List<string>(); }
                }
                whole[under].Add(row);
                if (one.Length > 0 && !one.StartsWith("#", StringComparison.Ordinal) && !one.StartsWith(">", StringComparison.Ordinal))
                    want[under].Add(row);
            }

            // Walk their file, and each heading we have something for takes its keys straight after its last filled line.
            string[] rows = body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            List<string> made = new List<string>();
            List<string> loose = want[""];
            string at = null;
            int after = 0;
            foreach (string row in rows)
            {
                string one = (row ?? "").Trim();
                if (one.StartsWith("#", StringComparison.Ordinal))
                {
                    after = Put(made, at, after, want, heads);
                    at = one.TrimStart('#').Trim();
                    made.Add(row);
                    after = made.Count;
                    continue;
                }
                made.Add(row);
                if (one.Length > 0) after = made.Count;
            }
            after = Put(made, at, after, want, heads);
            while (made.Count > 0 && made[made.Count - 1].Trim().Length == 0) made.RemoveAt(made.Count - 1);

            // Anything left has no heading in their file yet, so it keeps its own, notes and all.
            foreach (string name in heads)
            {
                if (name.Length == 0) continue;
                if (!want.ContainsKey(name) || want[name].Count == 0) continue;
                made.Add("");
                made.AddRange(whole[name]);
            }
            if (loose.Count > 0 && want.ContainsKey("")) { made.Add(""); made.AddRange(loose); }
            return string.Join(Environment.NewLine, made.ToArray()) + Environment.NewLine;
        }

        private static int Put(List<string> made, string at, int after, Dictionary<string, List<string>> want, List<string> heads)
        {
            List<string> rows;
            if (at == null || !want.TryGetValue(at, out rows) || rows.Count == 0) return after;
            want[at] = new List<string>();
            made.InsertRange(after, rows);
            return after + rows.Count;
        }

        // One heading per name. Earlier builds appended a second copy whenever a name was added, so a file that has
        // been through a few of those carries empty duplicates. Only a heading with nothing under it goes.
        private static void Empties()
        {
            if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept)) return;
            try
            {
                string[] rows = File.ReadAllText(Kept).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

                // Which block each line belongs to, how many keys each block holds, and how many each name holds
                // across the whole file. A block with no keys whose name has keys somewhere else is the spare one.
                int[] block = new int[rows.Length];
                List<string> named = new List<string>();
                List<int> held = new List<int>();
                int now = -1;
                for (int at = 0; at < rows.Length; at++)
                {
                    string one = (rows[at] ?? "").Trim();
                    if (one.StartsWith("##", StringComparison.Ordinal))
                    {
                        named.Add(one.TrimStart('#').Trim());
                        held.Add(0);
                        now = named.Count - 1;
                    }
                    block[at] = now;
                    if (now < 0 || one.Length == 0) continue;
                    if (one.StartsWith("#", StringComparison.Ordinal) || one.StartsWith(">", StringComparison.Ordinal)) continue;
                    held[now] = held[now] + 1;
                }
                Dictionary<string, int> all = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int one = 0; one < named.Count; one++)
                {
                    int sum;
                    all[named[one]] = all.TryGetValue(named[one], out sum) ? sum + held[one] : held[one];
                }
                bool[] goes = new bool[named.Count];
                bool cut = false;
                for (int one = 0; one < named.Count; one++)
                    if (held[one] == 0 && all[named[one]] > 0) { goes[one] = true; cut = true; }
                if (!cut) return;
                List<string> made = new List<string>();
                for (int at = 0; at < rows.Length; at++)
                    if (block[at] < 0 || !goes[block[at]]) made.Add(rows[at]);
                File.WriteAllText(Kept, string.Join(Environment.NewLine, made.ToArray()), new UTF8Encoding(false));
                read = DateTime.MinValue;
                wasLong = -1;
            }
            catch (Exception) { }
        }

        internal static void Write(bool over)
        {
            if (string.IsNullOrEmpty(Kept)) return;
            try
            {
                if (!over && File.Exists(Kept)) { Notes.Refresh(Kept, Text); return; }
                string folder = Path.GetDirectoryName(Kept);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) Directory.CreateDirectory(folder);
                File.WriteAllText(Kept, Text, new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        private const int Column = 22;

        private static string Line(string name, string said)
        {
            string left = name + ":";
            if (left.Length >= Column) return left + " " + said;
            return left + new string(' ', Column - left.Length) + said;
        }

        internal static string Text
        {
            get
            {
                StringBuilder made = new StringBuilder();
                made.AppendLine("# Her system lines");
                made.AppendLine();
                made.AppendLine("> The small grey labels she shows after \"> sys: \".");
                made.AppendLine("> Change the text on the right. Leave one blank to use hers.");
                made.AppendLine("> Names on the left are hers, so keep them as they are.");
                made.AppendLine("> Delete this file to put everything back.");
                made.AppendLine();
                made.AppendLine("## What goes in front of a line");
                made.AppendLine();
                made.AppendLine("> Change these to anything. A trailing space is added for you.");
                made.AppendLine();
                made.AppendLine(Line("sys prompt", SysMark));
                made.AppendLine(Line("you prompt", YouMark));
                made.AppendLine();
                made.AppendLine("## What she says herself");
                made.AppendLine();
                made.AppendLine("> Real speech, not labels. Offer several with \"|\" between them and she picks one.");
                made.AppendLine();
                for (int line = 0; line < Spoken.GetLength(0); line++)
                    made.AppendLine(Line(Spoken[line, 0], Spoken[line, 1]));
                made.AppendLine();
                made.AppendLine("## Never watch these");
                made.AppendLine();
                made.AppendLine("> Programs Watch my screen skips. Comma separated, and a partial name is enough:");
                made.AppendLine("> \"brave\" covers brave.exe. Matched on the program and on its window title.");
                made.AppendLine("> She says nothing at all while one of these is in front.");
                made.AppendLine();
                made.AppendLine(Line(ExcludedKey, ""));
                made.AppendLine();
                made.AppendLine("## Her calendar");
                made.AppendLine();
                made.AppendLine("> The app she opens to read your calendar. Blank and she uses the one you open most.");
                made.AppendLine("> She opens it and reads the screen. Nothing signs in and nothing is sent anywhere.");
                made.AppendLine();
                made.AppendLine(Line(CalendarKey, ""));
                made.AppendLine();
                made.AppendLine("## How she comes and goes");
                made.AppendLine();
                made.AppendLine("> 1: her hello and goodbye come from her personality file. 0: from the lines above.");
                made.AppendLine("> Her error line is never affected either way.");
                made.AppendLine();
                made.AppendLine(Line(OwnLinesKey, "1"));
                made.AppendLine();
                made.AppendLine("## The labels after it");
                made.AppendLine();
                made.AppendLine("> Taglines, not speech. They name what just happened.");
                made.AppendLine();
                for (int line = 0; line < Stock.GetLength(0); line++)
                    made.AppendLine(Line(Stock[line, 0], Stock[line, 1]));
                made.AppendLine();
                made.AppendLine("> What she says when handled lives in the personalities folder.");
                made.AppendLine();
                made.AppendLine("## Adult mode");
                made.AppendLine();
                made.AppendLine("> WARNING. Setting this to 1 turns on sexual content, intended for adults only.");
                made.AppendLine("> It unlocks her senpai lines, interests.md, and the personalities and moods marked");
                made.AppendLine("> adult. Leave it at 0 if anyone else uses this PC or you do not want any of that.");
                made.AppendLine("> 1 is on. Anything else is off.");
                made.AppendLine();
                made.AppendLine(Line(SenpaiKey, "0"));
                return made.ToString();
            }
        }
    }
}
