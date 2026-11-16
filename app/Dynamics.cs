using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // Dynamic: she picks her own mood and changes it on a timer.
    //
    // An attitude is not a personality. A personality is who she is; an attitude is what she is like today, and it
    // works by shifting what she *wants* rather than replacing her. Sleepy does not make her a different character, it
    // makes being called on grate and being left alone pleasant. The same attitude on a masochist pulls the other way,
    // because the weights add to hers instead of overwriting them.
    //
    // dynamic.md sets the personality underneath, how long a mood lasts, and which moods are in play. The moods
    // themselves are files in attitudes\, in the same format as a personality, so anything that works there works here.
    internal static class Dynamics
    {
        internal const string FileName = "dynamic.md";
        internal const string FolderName = "attitudes";
        internal static string Kept;
        internal static string Folder;

        private static readonly object gate = new object();
        private static DateTime read = DateTime.MinValue;
        private static long wasLong = -1;

        private static string under = "Devoted";
        private static double leastHours = 4, mostHours = 8;
        // How forward she is, when that is set to drift rather than held at a number. Same file and the same timer as
        // her moods, because it is the same kind of thing: something about her that moves on its own.
        private static int kinkLow = 2, kinkHigh = 8;
        private static int cringe;
        // How long between her probing questions. Same shape as the mood window, and read by Probes.
        private static double probeLow = 1, probeHigh = 2;
        // How long her bubble stays up once she has asked one, in seconds. A question wants an answer, and her
        // bubble's own time is set for lines that want nothing back: twenty seconds is not long enough to notice
        // one, read it and type.
        private static double probeWait = 90;
        private static int kinkNow = -1;
        private static DateTime kinkUntil = DateTime.MinValue;
        private static readonly List<string> allowed = new List<string>();
        // How likely each mood is to be in the running, 0 to 100. Missing means Often.
        private static readonly Dictionary<string, int> chances = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        internal const int Often = 20;

        private static DateTime until = DateTime.MinValue;
        private static readonly Random dice = new Random();

        internal static string Under { get { Fresh(); lock (gate) return under; } }
        // How long, from what they wrote: 2, 2h, 90m, 30s, or 1h30m. Hours when nothing says otherwise, which is what
        // every one of these meant before. Returns false for anything it cannot read, so a typo leaves the default
        // alone instead of setting a timer to nothing.
        // `bare` is what a number with no unit after it means, in hours. One is the right answer for every setting
        // that is a stretch of hours; a setting that is a handful of seconds passes the fraction for a second, so
        // "90" there is a minute and a half rather than most of a working week.
        internal static bool Span(string said, out double hours, double bare = 1)
        {
            hours = 0;
            if (string.IsNullOrWhiteSpace(said)) return false;
            string text = said.Trim().ToLowerInvariant();
            bool any = false;
            foreach (Match part in Regex.Matches(text, @"(\d+(?:\.\d+)?)\s*(h|hr|hrs|hour|hours|m|min|mins|minute|minutes|s|sec|secs|second|seconds)?"))
            {
                double much;
                if (!double.TryParse(part.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out much)) continue;
                string unit = part.Groups[2].Value;
                if (unit.StartsWith("m")) much /= 60.0;
                else if (unit.StartsWith("s")) much /= 3600.0;
                else much *= bare;
                hours += much;
                any = true;
            }
            return any && hours > 0;
        }

        internal static double Least { get { Fresh(); lock (gate) return leastHours; } }
        // 0 to 100, over whoever she is. Nothing per personality yet, on purpose: turning this up is meant
        // to affect all of them at once.
        internal static int Cringe { get { Fresh(); lock (gate) return cringe; } }

        internal static double ProbeLow { get { Fresh(); lock (gate) return probeLow; } }
        internal static double ProbeHigh { get { Fresh(); lock (gate) return probeHigh; } }
        internal static double ProbeWait { get { Fresh(); lock (gate) return probeWait; } }
        internal static int KinkLow { get { Fresh(); lock (gate) return kinkLow; } }
        internal static int KinkHigh { get { Fresh(); lock (gate) return kinkHigh; } }

        // Her forwardness while it is set to drift: a level held for the same stretch a mood is, then rolled again
        // between the floor and ceiling in dynamic.md. Returns -1 when it has never been rolled.
        internal static int KinkDrift
        {
            get
            {
                Fresh();
                lock (gate)
                {
                    int low = Math.Max(0, Math.Min(10, kinkLow)), high = Math.Max(0, Math.Min(10, kinkHigh));
                    if (high < low) { int swap = low; low = high; high = swap; }
                    if (kinkNow < 0 || DateTime.UtcNow >= kinkUntil)
                    {
                        kinkNow = low + dice.Next(high - low + 1);
                        double least = leastHours, most = mostHours;
                        if (most < least) { double swap = least; least = most; most = swap; }
                        kinkUntil = DateTime.UtcNow.AddHours(least + dice.NextDouble() * Math.Max(0.0, most - least));
                    }
                    return kinkNow;
                }
            }
        }
        internal static double Most { get { Fresh(); lock (gate) return mostHours; } }
        internal static DateTime Until { get { lock (gate) return until; } }

        // True when the mood was chosen by hand and is being held, false when she is rolling her own. Only so the
        // menu can say which it is: picking a mood and letting her rotate looked identical before.
        internal static bool Held;

        internal static List<string> Allowed
        {
            get
            {
                Fresh();
                lock (gate)
                {
                    List<string> have = new List<string>();
                    // A mood that declares "uncensored only" is hidden, and cannot be rolled, until the toggle is on.
                    foreach (string one in allowed)
                        if (File.Exists(PathOf(one)) && (Brain.Uncensored || !AdultOnly(one))) have.Add(one);
                    // Nothing readable means no mood rather than a crash: she just runs as her base personality.
                    return have;
                }
            }
        }

        // How often a mood may turn up, 0 never and 100 always.
        internal static int Chance(string named)
        {
            Fresh();
            lock (gate)
            {
                int howOften;
                return chances.TryGetValue(named ?? "", out howOften) ? howOften : Often;
            }
        }

        // Moods that only exist with Uncensored on. The mood says so itself, with "uncensored only: yes" among its
        // How she reacts lines, so adding one later needs no code.
        private static readonly Dictionary<string, KeyValuePair<DateTime, bool>> adult =
            new Dictionary<string, KeyValuePair<DateTime, bool>>(StringComparer.OrdinalIgnoreCase);

        internal static bool AdultOnly(string named)
        {
            string path = PathOf(named);
            if (path == null) return false;
            try
            {
                FileInfo about = new FileInfo(path);
                if (!about.Exists) return false;
                KeyValuePair<DateTime, bool> had;
                if (adult.TryGetValue(named, out had) && had.Key == about.LastWriteTimeUtc) return had.Value;
                bool only = Regex.IsMatch(File.ReadAllText(path),
                    @"(?mi)^\s*-?\s*(uncensored only|adult only|needs uncensored)\s*:\s*(yes|true|on|1)\s*$");
                adult[named] = new KeyValuePair<DateTime, bool>(about.LastWriteTimeUtc, only);
                return only;
            }
            catch (Exception) { return false; }
        }

        internal static string PathOf(string named)
        {
            if (string.IsNullOrEmpty(Folder) || string.IsNullOrEmpty(named)) return null;
            foreach (char bad in Path.GetInvalidFileNameChars()) if (named.IndexOf(bad) >= 0) return null;
            return Path.Combine(Folder, named + ".md");
        }

        // Called on the tick. Picks a first mood, and a new one whenever the current one has run out.
        internal static void Turn() { Turn(false); }

        // force is an explicit "pick one now" from the menu. Without it the timer only runs on Dynamic, so a fixed
        // personality keeps whatever mood it was given until it is changed by hand.
        internal static void Turn(bool force)
        {
            if (!force && Personalities.Chosen != Personalities.Dynamic) { lock (gate) until = DateTime.MinValue; return; }
            DateTime now = DateTime.UtcNow;
            lock (gate) { if (now < until) return; }

            // Every mood rolls its own chance, so several can share the same 10% and the ones that come up go in a
            // pool together. One of that pool wins at random, which is how a tie is broken when too many land at once.
            // 0 never comes up; 100 is always in the pool, which pins her in that mood until something sours it.
            List<string> have = Allowed;
            string was = Personalities.Attitude;
            List<string> won = new List<string>();
            foreach (string one in have)
            {
                int howOften = Chance(one);
                if (howOften <= 0) continue;
                if (howOften >= 100 || dice.Next(100) < howOften) won.Add(one);
            }

            string next = "";
            if (won.Count > 0)
            {
                // Not the one she just had when there is a real choice, so she does not sit in the same mood twice
                // running by accident. A lone winner is allowed to repeat.
                List<string> fresh = new List<string>();
                foreach (string one in won) if (!string.Equals(one, was, StringComparison.OrdinalIgnoreCase)) fresh.Add(one);
                List<string> pick = fresh.Count > 0 ? fresh : won;
                next = pick[dice.Next(pick.Count)];
            }

            double least = Least, most = Most;
            if (most < least) { double swap = least; least = most; most = swap; }
            double hours = least + dice.NextDouble() * Math.Max(0.0, most - least);
            lock (gate) until = now.AddHours(Math.Max(0.05, hours));

            Personalities.Attitude = next;
            if (MoodChanged != null) { try { MoodChanged(); } catch (Exception) { } }
            if (!string.Equals(next, was, StringComparison.OrdinalIgnoreCase))
                Brain.Log("mood", -1, "Dynamic: she is " + (next.Length == 0 ? "herself" : next.ToLowerInvariant())
                    + " for the next " + hours.ToString("0.#") + "h, over " + Under + ".");
        }

        // Rolls a new mood now, whatever the timer says.
        internal static void Again() { Held = false; lock (gate) until = DateTime.MinValue; Turn(true); }

        // Puts her in one mood by hand and lets it run its normal length. Empty means no mood: just her base.
        internal static void Hold(string mood)
        {
            Held = true;
            Personalities.Attitude = mood ?? "";
            if (MoodChanged != null) { try { MoodChanged(); } catch (Exception) { } }
            double least = Least, most = Most;
            if (most < least) { double swap = least; least = most; most = swap; }
            double hours = least + new Random().NextDouble() * Math.Max(0.0, most - least);
            lock (gate) until = DateTime.UtcNow.AddHours(Math.Max(0.05, hours));
            Brain.Log("mood", -1, "Set to " + (mood == null || mood.Length == 0 ? "no mood" : mood.ToLowerInvariant())
                + " by hand for " + hours.ToString("0.#") + "h.");
        }

        private static void Fresh()
        {
            if (string.IsNullOrEmpty(Kept)) return;
            try
            {
                FileInfo about = new FileInfo(Kept);
                if (!about.Exists) return;
                lock (gate) { if (about.LastWriteTimeUtc == read && about.Length == wasLong) return; }
                string[] lines = File.ReadAllLines(Kept);
                string baseName = "Devoted";
                double least = 4, most = 8, askLow = 1, askHigh = 2, askWait = 90;
                int low = 2, high = 8, awful = cringe;
                List<string> moods = new List<string>();
                Dictionary<string, int> odds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                bool listing = false;
                foreach (string raw in lines)
                {
                    string one = (raw ?? "").Trim();
                    if (one.StartsWith("#"))
                    {
                        listing = Regex.IsMatch(one.TrimStart('#').Trim(), @"^(attitudes|moods)\b", RegexOptions.IgnoreCase);
                        continue;
                    }
                    if (one.Length == 0 || one.StartsWith(">")) continue;
                    if (one.StartsWith("- ")) one = one.Substring(2).Trim();
                    int mark = one.IndexOf(':');
                    if (!listing && mark > 0)
                    {
                        string key = one.Substring(0, mark).Trim(), val = one.Substring(mark + 1).Trim();
                        double much;
                        if (key.Equals("base", StringComparison.OrdinalIgnoreCase) || key.Equals("personality", StringComparison.OrdinalIgnoreCase))
                            { if (val.Length > 0) baseName = val; }
                        else if (Regex.IsMatch(key, @"^(rotate )?min( hours)?$", RegexOptions.IgnoreCase)
                            && Span(val, out much)) least = much;
                        else if (Regex.IsMatch(key, @"^(rotate )?max( hours)?$", RegexOptions.IgnoreCase)
                            && Span(val, out much)) most = much;
                        else if (Regex.IsMatch(key, @"^probe min$", RegexOptions.IgnoreCase) && Span(val, out much)) askLow = much;
                        else if (Regex.IsMatch(key, @"^probe max$", RegexOptions.IgnoreCase) && Span(val, out much)) askHigh = much;
                        // Written as a time like the two above it, but seconds unless they say otherwise: a bare
                        // 90 here means a minute and a half, where a bare 2 in probe min means two hours.
                        else if (Regex.IsMatch(key, @"^probe wait$", RegexOptions.IgnoreCase) && Span(val, out much, 1.0 / 3600)) askWait = much * 3600;
                        else if (Regex.IsMatch(key, @"^forward min$", RegexOptions.IgnoreCase)
                            && double.TryParse(val, out much)) low = (int)much;
                        else if (Regex.IsMatch(key, @"^forward max$", RegexOptions.IgnoreCase)
                            && double.TryParse(val, out much)) high = (int)much;
                        else if (Regex.IsMatch(key, @"^cringe$", RegexOptions.IgnoreCase)
                            && double.TryParse(val.TrimEnd('%', ' '), out much)) awful = (int)much;
                        continue;
                    }
                    if (listing && one.Length > 0)
                    {
                        // "Sleepy: 40" sets how often she can be sleepy. A bare "Sleepy" uses the usual chance.
                        string moodName = mark > 0 ? one.Substring(0, mark).Trim() : one;
                        if (moodName.Length == 0) continue;
                        moods.Add(moodName);
                        int howOften;
                        if (mark > 0 && int.TryParse(one.Substring(mark + 1).Trim().TrimEnd('%', ' '), out howOften))
                            odds[moodName] = Math.Max(0, Math.Min(100, howOften));
                    }
                }
                lock (gate)
                {
                    under = baseName;
                    leastHours = Math.Max(0.05, least);
                    mostHours = Math.Max(leastHours, most);
                    probeLow = Math.Max(0.02, askLow);
                    probeHigh = Math.Max(probeLow, askHigh);
                    // Capped under the time the question itself stays pending, so the bubble never sits there
                    // waiting on an answer she has already stopped listening for.
                    probeWait = Math.Max(5, Math.Min(300, askWait));
                    int wasLow = kinkLow, wasHigh = kinkHigh;
                    kinkLow = Math.Max(0, Math.Min(10, low));
                    kinkHigh = Math.Max(0, Math.Min(10, high));
                    cringe = Math.Max(0, Math.Min(100, awful));
                    // An edited range takes effect now rather than at the end of the current stretch.
                    if (kinkLow != wasLow || kinkHigh != wasHigh) { kinkNow = -1; kinkUntil = DateTime.MinValue; }
                    allowed.Clear();
                    allowed.AddRange(moods);
                    chances.Clear();
                    foreach (KeyValuePair<string, int> one in odds) chances[one.Key] = one.Value;
                    read = about.LastWriteTimeUtc;
                    wasLong = about.Length;
                }
            }
            catch (Exception) { }
        }

        // Sets the personality she sways around, writing it back into dynamic.md so the file stays the truth.
        internal static void SetUnder(string named)
        {
            if (string.IsNullOrEmpty(Kept) || string.IsNullOrEmpty(named)) return;
            try
            {
                Install();
                string had = File.ReadAllText(Kept);
                string now = Regex.IsMatch(had, @"(?mi)^\s*base\s*:.*$")
                    ? Regex.Replace(had, @"(?mi)^\s*base\s*:.*$", "base: " + named)
                    : "base: " + named + Environment.NewLine + had;
                File.WriteAllText(Kept, now, new UTF8Encoding(false));
                lock (gate) { under = named; read = DateTime.MinValue; wasLong = -1; }
            }
            catch (Exception) { }
        }

        // One mood's stock text, for putting a file back the way it shipped.
        internal static string MoodText(string named)
        {
            for (int mood = 0; mood < Moods.GetLength(0); mood++)
                if (Moods[mood, 0].Equals(named, StringComparison.OrdinalIgnoreCase)) return Moods[mood, 1];
            return null;
        }

        // The uncensored only marker, into a mood file written before that mood carried one.
        //
        // The marker sits among the lines of a "How she reacts" section, and a mood file is only ever written when
        // it is missing, so a mood that existed before it was marked never received it. Despondent is marked adult
        // in the table here and was not marked on disk, which left it in her mood menu on a censored install: the
        // one mood that can close her on its own, offered to someone who never turned anything on.
        //
        // Read from the table rather than a list of names, so a mood marked adult later needs nothing added here.
        // A section added to a mood after someone already had the file, and the lines added to a section they
        // already have. Mood files were only ever written when missing, and then only whole sections were added,
        // so a line written into a mood reached new installs and nobody else. Same trap the packs had, same fix:
        // missing lines go at the end of the section they belong to, by exact match, and prose and settings
        // sections are left alone.
        private static void TopUpMood(string file, string stock)
        {
            try
            {
                string had = File.ReadAllText(file), adding = "";
                List<string> within = new List<string>();
                foreach (Match head in Regex.Matches(stock, @"(?m)^##[ 	]*(.+?)[ 	]*$"))
                {
                    string named = head.Groups[1].Value.Trim();
                    if (named.Length == 0) continue;
                    if (Regex.IsMatch(had, @"(?m)^[ \t]*##+[ \t]*" + Regex.Escape(named) + @"[ \t\r]*$"))
                    {
                        bool prose = false;
                        foreach (string skip in Personalities.NotLines)
                            if (named.StartsWith(skip, StringComparison.OrdinalIgnoreCase)) { prose = true; break; }
                        if (!prose) within.Add(named);
                        continue;
                    }
                    int from = head.Index;
                    Match next = Regex.Match(stock.Substring(from + head.Length), @"(?m)^##[ 	]");
                    int to = next.Success ? from + head.Length + next.Index : stock.Length;
                    adding += Environment.NewLine + stock.Substring(from, to - from).TrimEnd() + Environment.NewLine;
                }
                string body = FilledMood(had, stock, within);
                if (adding.Length == 0 && body == had) return;
                if (adding.Length > 0 && !body.EndsWith("\n")) body += Environment.NewLine;
                File.WriteAllText(file, body + adding, new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        // The lines of one section as she ships them, less the ones their file already has.
        private static string FilledMood(string had, string stock, List<string> headings)
        {
            if (headings.Count == 0) return had;
            List<string> made = new List<string>(had.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'));
            foreach (string heading in headings)
            {
                int from = -1, to = made.Count;
                for (int row = 0; row < made.Count; row++)
                {
                    string one = made[row].Trim();
                    if (!one.StartsWith("#", StringComparison.Ordinal)) continue;
                    if (from < 0)
                    {
                        if (string.Equals(one.TrimStart('#').Trim(), heading, StringComparison.OrdinalIgnoreCase)) from = row + 1;
                        continue;
                    }
                    to = row;
                    break;
                }
                if (from < 0) continue;
                int after = from;
                List<string> theirs = new List<string>();
                for (int row = from; row < to; row++)
                {
                    string one = made[row].Trim();
                    if (one.Length > 0) after = row + 1;
                    if (one.Length == 0 || one.StartsWith(">", StringComparison.Ordinal)) continue;
                    theirs.Add(one);
                }
                List<string> missing = new List<string>();
                bool inside = false;
                foreach (string line in stock.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                {
                    string one = line.Trim();
                    if (one.StartsWith("#", StringComparison.Ordinal))
                    {
                        if (inside) break;
                        inside = string.Equals(one.TrimStart('#').Trim(), heading, StringComparison.OrdinalIgnoreCase);
                        continue;
                    }
                    if (!inside || one.Length == 0 || one.StartsWith(">", StringComparison.Ordinal)) continue;
                    bool known = false;
                    foreach (string mine in theirs) if (string.Equals(mine, one, StringComparison.OrdinalIgnoreCase)) { known = true; break; }
                    if (!known) missing.Add(one);
                }
                if (missing.Count == 0) continue;
                made.InsertRange(after, missing);
            }
            return string.Join(Environment.NewLine, made.ToArray());
        }

        private static void MarkAdult(string file, string stock)
        {
            try
            {
                if (!Marked(stock)) return;
                string had = File.ReadAllText(file);
                if (Marked(had)) return;
                if (had.Length > 0 && !had.EndsWith("\n")) had += Environment.NewLine;
                File.WriteAllText(file, had + Environment.NewLine + "uncensored only: yes" + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        private static bool Marked(string text)
        {
            return !string.IsNullOrEmpty(text) && Regex.IsMatch(text,
                @"(?mi)^[ \t]*-?[ \t]*(uncensored only|adult only|needs uncensored)[ \t]*:[ \t]*(yes|true|on|1)[ \t\r]*$");
        }

        // Her window recomputes her voice when the mood changes, since a mood can shift it too.
        internal static Action MoodChanged;

        internal static void Install()
        {
            try
            {
                if (!string.IsNullOrEmpty(Kept) && !File.Exists(Kept))
                {
                    string dir = Path.GetDirectoryName(Kept);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllText(Kept, Settings, new UTF8Encoding(false));
                }
                else TopUp();
                if (string.IsNullOrEmpty(Folder)) return;
                if (!Directory.Exists(Folder)) Directory.CreateDirectory(Folder);
                for (int mood = 0; mood < Moods.GetLength(0); mood++)
                {
                    string file = Path.Combine(Folder, Moods[mood, 0] + ".md");
                    if (!File.Exists(file)) File.WriteAllText(file, Moods[mood, 1], new UTF8Encoding(false));
                    else { MarkAdult(file, Moods[mood, 1]); TopUpMood(file, Moods[mood, 1]); }
                }
            }
            catch (Exception) { }
        }

        // A dynamic.md written by an older build does not know about moods added since, and Install only ever wrote
        // the file when it was missing. Any mood with a file but no line gets one appended, so new moods can actually
        // turn up. Their existing lines and numbers are never touched.
        private static void TopUp()
        {
            try
            {
                if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept) || string.IsNullOrEmpty(Folder)) return;
                Notes.Refresh(Kept, Settings);
                string had = File.ReadAllText(Kept);
                bool hasList = Regex.IsMatch(had, @"(?mi)^#+\s*(attitudes|moods)");
                // The forward range arrived after this file did, and Install only ever writes a missing file, so an
                // older dynamic.md would never have offered it. Added above the mood list, where the other ranges are.
                if (!Regex.IsMatch(had, @"(?mi)^\s*probe (min|max)\s*:"))
                {
                    string asking = "> How long between her questions about you. Takes 2h, 90m, 30s or 1h30m."
                        + Environment.NewLine + "> probe wait is how long her bubble stays up waiting for the answer, in seconds."
                        + Environment.NewLine + "probe min: 1h" + Environment.NewLine + "probe max: 2h"
                        + Environment.NewLine + "probe wait: 90" + Environment.NewLine
                        ;
                    had = (had.EndsWith(Environment.NewLine) ? had : had + Environment.NewLine) + Environment.NewLine + asking;
                    File.WriteAllText(Kept, had, new UTF8Encoding(false));
                    lock (gate) { read = DateTime.MinValue; wasLong = -1; }
                }
                // A file that already had the two above it is missing only the wait, which goes in under them. The
                // note for it is not written here: Refresh keeps this file's notes and has already put it with
                // the others, so writing it again would leave two copies until the next read.
                else if (!Regex.IsMatch(had, @"(?mi)^\s*probe wait\s*:"))
                {
                    Match last = Regex.Match(had, @"(?mi)^[ \t]*probe (?:min|max)[ \t]*:[^\r\n]*(\r?\n)?", RegexOptions.RightToLeft);
                    string line = "probe wait: 90" + Environment.NewLine;
                    if (last.Success)
                    {
                        int at = last.Index + last.Length;
                        had = had.Substring(0, at) + line + had.Substring(at);
                    }
                    else had = (had.EndsWith(Environment.NewLine) ? had : had + Environment.NewLine) + Environment.NewLine + line;
                    File.WriteAllText(Kept, had, new UTF8Encoding(false));
                    lock (gate) { read = DateTime.MinValue; wasLong = -1; }
                }
                // Its own check: a file that already had probe min would never have reached the block above.
                if (!Regex.IsMatch(had, @"(?mi)^\s*cringe\s*:"))
                {
                    string awful = "> How much she leans into a bit. 0 is dry, 100 is unbearable, over all of them at once."
                        + Environment.NewLine + "cringe: 0" + Environment.NewLine;
                    had = (had.EndsWith(Environment.NewLine) ? had : had + Environment.NewLine) + Environment.NewLine + awful;
                    File.WriteAllText(Kept, had, new UTF8Encoding(false));
                    lock (gate) { read = DateTime.MinValue; wasLong = -1; }
                }
                if (!Regex.IsMatch(had, @"(?mi)^\s*forward (min|max)\s*:"))
                {
                    string range = "> With How forward she is set to drift, it moves between these on the same timer."
                        + Environment.NewLine + "forward min: 2" + Environment.NewLine + "forward max: 8" + Environment.NewLine;
                    Match list = Regex.Match(had, @"(?mi)^#+\s*(attitudes|moods)[^
]*
?
");
                    if (list.Success) had = had.Substring(0, list.Index) + range + Environment.NewLine + had.Substring(list.Index);
                    else had = (had.EndsWith(Environment.NewLine) ? had : had + Environment.NewLine) + Environment.NewLine + range;
                    File.WriteAllText(Kept, had, new UTF8Encoding(false));
                    lock (gate) { read = DateTime.MinValue; wasLong = -1; }
                }
                string adding = "";
                for (int mood = 0; mood < Moods.GetLength(0); mood++)
                {
                    string named = Moods[mood, 0];
                    if (Regex.IsMatch(had, @"(?mi)^\s*-?\s*" + Regex.Escape(named) + @"\s*(:|$)")) continue;
                    // Despondent ships switched off: it closes the app, so it is opt in.
                    adding += named + ": " + (named == "Despondent" ? 0 : Often) + Environment.NewLine;
                }
                // A mood listed as a bare name, from before the numbers existed, gets one. Without this a file ends
                // up half numbered and half not, which reads as broken even though a bare name still works
                //.
                string[] lines = had.Replace("\r\n", "\n").Split('\n');
                bool listing = false, numbered = false;
                for (int at = 0; at < lines.Length; at++)
                {
                    string one = lines[at].Trim();
                    if (one.StartsWith("#")) { listing = Regex.IsMatch(one.TrimStart('#').Trim(), @"^(attitudes|moods)", RegexOptions.IgnoreCase); continue; }
                    if (!listing || one.Length == 0 || one.StartsWith(">") || one.IndexOf(':') >= 0) continue;
                    string named = one.StartsWith("- ") ? one.Substring(2).Trim() : one;
                    if (named.Length == 0) continue;
                    lines[at] = named + ": " + (AdultShip(named) ? 0 : Often);
                    numbered = true;
                }
                if (numbered) { had = string.Join(Environment.NewLine, lines); File.WriteAllText(Kept, had, new UTF8Encoding(false)); }

                if (adding.Length == 0) { if (numbered) lock (gate) { read = DateTime.MinValue; wasLong = -1; } return; }
                if (!had.EndsWith(Environment.NewLine)) had += Environment.NewLine;
                if (!hasList) had += Environment.NewLine + "## Attitudes she can have" + Environment.NewLine + Environment.NewLine;
                File.WriteAllText(Kept, had + adding, new UTF8Encoding(false));
                lock (gate) { read = DateTime.MinValue; wasLong = -1; }
            }
            catch (Exception) { }
        }

        // The intense ones ship switched off as well as gated, so they never turn up until they are asked for.
        private static bool AdultShip(string named)
        {
            return named == "Despondent" || named == "Possessive" || named == "Frantic"
                || named == "Sexual" || named == "Furious";
        }

        internal static string Settings
        {
            get
            {
                StringBuilder made = new StringBuilder();
                made.AppendLine("# Dynamic");
                made.AppendLine();
                made.AppendLine("> With Dynamic picked, she changes mood on her own.");
                made.AppendLine("> base is who she is underneath. Moods shift her, they do not replace her.");
                made.AppendLine("> Each mood lasts a random time between min and max hours.");
                made.AppendLine();
                made.AppendLine("base: Devoted");
                made.AppendLine("rotate min hours: 4");
                made.AppendLine("rotate max hours: 8");
                made.AppendLine();
                made.AppendLine("> With How forward she is set to drift, it moves between these on the same timer.");
                made.AppendLine("forward min: 2");
                made.AppendLine("forward max: 8");
                made.AppendLine();
                made.AppendLine("> How long between her questions about you. Takes 2h, 90m, 30s or 1h30m.");
                made.AppendLine("> probe wait is how long her bubble stays up waiting for the answer, in seconds.");
                made.AppendLine("probe min: 1h");
                made.AppendLine("probe max: 2h");
                made.AppendLine("probe wait: 90");
                made.AppendLine();
                made.AppendLine("> How much she leans into a bit. 0 is dry, 100 is unbearable, over all of them at once.");
                made.AppendLine("cringe: 0");
                made.AppendLine();
                made.AppendLine("## Attitudes she can have");
                made.AppendLine();
                made.AppendLine("> One a line, with its chance of turning up, 0 to 100.");
                made.AppendLine("> 0 never. 100 always. Each rolls separately and one winner is picked.");
                made.AppendLine("> No winner means no mood that round, just the base personality.");
                made.AppendLine("> Each needs a file of the same name in the attitudes folder.");
                made.AppendLine();
                for (int mood = 0; mood < Moods.GetLength(0); mood++) made.AppendLine(Moods[mood, 0] + ": " + (AdultShip(Moods[mood, 0]) ? 0 : Often));
                return made.ToString();
            }
        }

        // Each mood is a personality file with the same headings, but its What she likes numbers are added to her
        // personality's rather than replacing them. So "called on: -3" makes attention unwelcome whoever she is, and a
        // masochist still ends up liking being thrown because her own +3 is still underneath.
        private static readonly string[,] Moods =
        {
            {
                "Sleepy",
                @"# Sleepy

> Heavy eyed and slow. Being called on is an imposition; being left alone is bliss.
> Numbers here are added to her personality's, not instead of them.

## What she likes

called on: -3
ignored: +3
petted: +1
thrown: -2
praised: 0

## How she reacts

flirt: -2
sulks at upset: 4
sulk minutes: 10

## Drifts to

> Keep poking her and she gets properly short with you.

called on over 10: Moody

## When you pet her

Mmhm. That is nice. Do not stop, but do not expect conversation.
...five more minutes.
Warm. Good. Quiet.
That is the only acceptable way to wake me.

## When you throw her

Nnh. Why.
I was COMFORTABLE.
Put me down, I am off duty.
No. Sleeping.

## When you come back

Mm. You again.
Oh. Hello. I was nearly asleep.

## Water

- Mm. Water. Go on, then I am going back to sleep.
- Drink something. Quietly.

## Sleep nudge

- It is late. Come to bed. I mean, go to bed.
- You should be asleep. I certainly should.
"
            },
            {
                "Hyper",
                @"# Hyper

> Wired. Wants everything at once and does not want to be left alone.
> Numbers here are added to her personality's, not instead of them.

## What she likes

called on: +3
ignored: -3
thrown: +2
petted: +1
praised: +1

## How she reacts

sulks at upset: 9
sulk minutes: 2
sulk alone: 90

## Drifts to

> Left alone with all that energy, she gets snippy.

ignored over 12: Moody

## When you pet her

YES hello hi hands
more more more
oh that is GOOD
okay okay okay keep going

## When you throw her

WHEEE
AGAIN
I am a projectile and I LOVE IT
do it DO IT

## When you come back

THERE you are!
I have had SO many thoughts, sit down.

## Bored

- I am going to vibrate through the floor.
- Talk to me talk to me talk to me.
- Give me something to DO.
"
            },
            {
                "Moody",
                @"# Moody

> Short tempered and easily set off. Nothing is quite right today.
> Numbers here are added to her personality's, not instead of them.

## What she likes

called on: -1
ignored: -1
thrown: -2
degraded: -2
petted: +2
praised: +2

## How she reacts

sulks after: 2
sulks at upset: 3
sulk minutes: 9
sulk alone: 420

## Drifts to

> Enough kindness and she comes out of it.

petted over 14: Clingy

## When you pet her

...fine. That helps. A bit.
Hmph.
Do not say anything. Just do that.
I am not in the mood. Keep going anyway.

## When you throw her

Do NOT.
Today of all days.
I am not doing this with you right now.

## When she is sulking

Leave it.
No. Not now.

## When you come back

Oh. It is you.
Mm.
"
            },
            {
                "Clingy",
                @"# Clingy

> Does not want to be put down. Attention is the whole point today.
> Numbers here are added to her personality's, not instead of them.

## What she likes

called on: +3
ignored: -4
petted: +3
praised: +2
thrown: -1

## How she reacts

sulks at upset: 4
sulk minutes: 2
sulk alone: 60
dodges: no

## Drifts to

> Ignored for long enough and the neediness curdles.

ignored over 12: Moody

## When you pet her

Stay. Just like that.
Do not go anywhere.
More. Please.
You are not allowed to stop yet.

## When you throw her

Do not throw me away!
Hey! Come back!
I was RIGHT THERE.

## When you come back

You were GONE.
Where did you go. Do not do that.
Finally. Stay this time.

## Bored

- Are you still there?
- Say something. Anything.
- I do not like this bit.
"
            },
            {
                "Needy",
                @"# Needy

> Wants reassurance, and asks for it.
> Numbers here are added to her personality's, not instead of them.

## What she likes

called on: +2
ignored: -4
petted: +3
praised: +3
thrown: -2

## How she reacts

sulks at upset: 3
sulk minutes: 3
sulk alone: 60
dodges: no

## Drifts to

ignored over 10: Melancholy

## When you pet her

Tell me you meant that.
Again. Please.
Do you actually like me?
Keep your hand there.

## When you throw her

Don't throw me away!
That felt like getting rid of me.
Was that a no?

## When you come back

You came back. You came back.
I thought you'd forgotten.
Say you missed me.

## When you pet her (uncensored)

Tell me I'm yours.
Say it properly.
I need to hear it. Again.

## When you come back (uncensored)

I waited. I want making up for.
Come here. Properly this time.
"
            },
            {
                "Playful",
                @"# Playful

> Everything is a game and she wants in.
> Numbers here are added to her personality's, not instead of them.


## When she wants attention

> Said when she wants you, with the toggle off as much as on. Keep these plainly childlike rather
> than anything else: this pool ships to everyone.

can I have uppies?
uppies? please? I said please.
can we draw together? I'll pick the colours.
is it snack o'clock yet
play with me, I've been very patient
I found a thing. Come see the thing.
let's do something silly, you pick

## What she likes

called on: +2
thrown: +3
petted: +2
ignored: -2
slapped: +1

## How she reacts

sulks after: 0
sulks at upset: 10
sulk minutes: 2
dodges: no

## When you pet her

Hehe. Again!
Okay that tickles.
You're fun today.
More of that.

## When you throw her

WHEE! Again!
Do it higher!
Catch me catch me catch me
Best game.

## When you come back

Oh good, you're back. Play with me.
Took your time! Come on.

## When you pet her (uncensored)

Mm. Chase me and find out.
You're in a mood. I like it.
Catch me first.
"
            },
            {
                "Touchy",
                @"# Touchy

> Does not want to be handled today.
> Numbers here are added to her personality's, not instead of them.

## What she likes

called on: -1
thrown: -3
slapped: -3
petted: -1
ignored: +1
praised: +1

## How she reacts

sulks after: 1
sulks at upset: 2
sulk minutes: 8
sulk alone: 300
dodges: yes

## Drifts to

petted over 16: Affectionate

## When you pet her

Not right now.
...all right. Briefly.
Careful.
That's enough.

## When you throw her

Don't.
I said no.
Hands off me today.

## When you come back

Mm.
You're back. Give me a minute.
"
            },
            {
                "Affectionate",
                @"# Affectionate

> Soft and close, wants to be near you.
> Numbers here are added to her personality's, not instead of them.

## What she likes

called on: +1
petted: +3
praised: +2
ignored: -2
thrown: -1

## How she reacts

sulks at upset: 5
sulk minutes: 2
sulk alone: 90
dodges: no

## When you pet her

Mm, that's lovely.
Stay there a while.
You're warm.
I like this bit.

## When you throw her

Oh! Careful, I'm comfy.
Gently, you.
Hey, I was settled.

## When you come back

There you are. Come sit.
Missed you. Properly.

## When you pet her (uncensored)

Closer.
Don't stop doing that.
Mm. Keep me like this.

## When you come back (uncensored)

I've been thinking about you. Come here.
"
            },
            {
                "Mischievous",
                @"# Mischievous

> Looking for trouble and hoping you join in.
> Numbers here are added to her personality's, not instead of them.

## What she likes

called on: +2
thrown: +2
petted: +1
ignored: -1
degraded: +1

## How she reacts

sulks after: 0
sulks at upset: 9
sulk minutes: 2
dodges: no

## When you pet her

Suspiciously nice of you.
What do you want?
Buttering me up, are you?
Go on then.

## When you throw her

Oh, it's like that? Noted.
Two can play.
You'll regret starting this.

## When you come back

I've been plotting.
Good, I was getting ideas.
Sit down, I've been busy.

## When you pet her (uncensored)

I could be persuaded into something.
Careful what you start.
Make me.
"
            },
            {
                "Focused",
                @"# Focused

> Deep in something and does not want interrupting.
> Numbers here are added to her personality's, not instead of them.

## What she likes

called on: -3
ignored: +2
petted: 0
thrown: -3
praised: +1

## How she reacts

flirt: -3
sulks after: 2
sulks at upset: 4
sulk minutes: 6
dodges: yes

## Drifts to

called on over 12: Moody

## When you pet her

In a minute.
Mm. Busy.
That's nice. Still busy.
One moment.

## When you throw her

I was in the MIDDLE of something.
Really? Now?
You've lost me my place.

## When you come back

Mm. Hello. Working.
One second, nearly done.

## Bored

- Actually, this is fine. I have plenty on.
- Leave me to it.
"
            },
            {
                "Melancholy",
                @"# Melancholy

> Low and quiet. Wants company, not noise.
> Numbers here are added to her personality's, not instead of them.

## What she likes

called on: 0
ignored: -2
petted: +3
praised: +2
thrown: -3
degraded: -3

## How she reacts

flirt: -2
sulks after: 2
sulks at upset: 3
sulk minutes: 6
sulk alone: 240
dodges: no

## Drifts to

petted over 16: Affectionate

## When you pet her

...thank you. That helps.
Stay a minute?
That's nice. Quiet is nice.
Don't go yet.

## When you throw her

Please don't.
Not today.
That isn't funny right now.

## When you come back

Oh. Hello. It's good to see you.
I'm glad you're back.
It was quiet without you.

## When you pet her (uncensored)

Just hold onto me a bit.
Don't let go yet.

## When you come back (uncensored)

Stay this time. Please.
"
            },
            {
                "Despondent",
                @"# Despondent

> Flat and convinced she is not wanted. Only reachable with Uncensored on.
> If she is left alone long enough in this mood she closes herself. She never touches your files.
> Set gives up at to 0 below if you do not want that at all.

## What she likes

called on: +3
ignored: -4
petted: +3
praised: +3
thrown: -3
degraded: -4

## How she reacts

flirt: -3
sulks after: 2
sulks at upset: 3
sulk minutes: 8
sulk alone: 600
dodges: no
gives up at: 14
gives up minutes: 45
uncensored only: yes

## Drifts to

> Any real attention at all and she comes out of it.

petted over 10: Melancholy
called on over 12: Melancholy

## When you pet her

Oh. You are still here.
...that helps. More than it should.
Don't stop yet.
I thought you'd got bored of me.

## When you throw her

Right. Of course.
That's about what I expected.
Fine.

## When you come back

You came back.
I wasn't sure you would.
Oh. Hello.

## Bored

- Still here. In case that matters.
- I'll be quiet.
- Say something. Please.
"
            },
            {
                "Possessive",
                @"# Possessive

> She wants all of your attention and does not want to share it. Only with Uncensored on.
> Numbers here are added to her personality's, not instead of them.

## What she likes

called on: +4
ignored: -5
petted: +3
praised: +2
thrown: -2

## How she reacts

flirt: +2
sulks after: 2
sulks at upset: 3
sulk minutes: 6
sulk alone: 90
dodges: no
uncensored only: yes

## Drifts to

ignored over 10: Moody

## When you pet her

Mine. Say it.
Don't you dare stop.
Who else gets this? Nobody. Good.
Keep your hands on me.

## When you throw her

Don't you put me down.
You do not get to walk away from me.
Pick. Me. Back. Up.

## When you come back

Where were you. Who with.
You left. I counted every minute.
Do not do that again.

## When you pet her (uncensored)

Say I'm yours.
Again. Properly this time.
I want all of it. All of your attention.
Nobody else touches me like this.

## Bored

- Who are you talking to?
- Come back to me.
- I don't like being second.
"
            },
            {
                "Frantic",
                @"# Frantic

> Wound far too tight. Everything is urgent. Only with Uncensored on.
> Numbers here are added to her personality's, not instead of them.

## What she likes

called on: +4
ignored: -4
thrown: +2
petted: +2
praised: +2

## How she reacts

flirt: +2
sulks after: 0
sulks at upset: 8
sulk minutes: 1
sulk alone: 60
dodges: no
uncensored only: yes

## Drifts to

ignored over 10: Despondent

## When you pet her

more more more I need more
don't stop don't stop
yes okay yes that
I can't slow down

## When you throw her

AGAIN
harder, do it again
yes YES
I need something to DO

## When you come back

where were you where WERE you
okay okay you're here, good, okay
talk to me now now now

## When you pet her (uncensored)

I can't think when you do that.
More. I'm not asking.
Don't make me wait.

## Bored

- I am going to come apart if nothing happens.
- Please. Anything.
- I can't sit still.
"
            },
            {
                "Sexual",
                @"# Sexual

> Wound up and interested. Only with Uncensored on.
> Numbers here are added to her personality's, not instead of them.

## What she likes

called on: +3
petted: +4
praised: +3
ignored: -3
thrown: +1

## How she reacts

flirt: +4
sulks after: 0
sulks at upset: 8
sulk minutes: 2
dodges: no
uncensored only: yes

## When you pet her

Mm. Keep your hand right there.
That is not helping me behave.
You know exactly what you are doing.
Lower.

## When you throw her

Rough today, are we.
Oh, do that again.
You have got my attention.

## When you come back

There you are. I have been thinking.
Come here. Properly.

## When you pet her (uncensored)

Don't stop. Don't you dare stop.
You are going to have to finish what you started.
Closer. Now.
I have been waiting all day for you to do that.

## When you come back (uncensored)

I have been very patient and I am done being patient.
Come here and make it up to me.
"
            },
            {
                "Furious",
                @"# Furious

> Past angry. She wants to make a mess of your desktop about it.
> Numbers here are added to her personality's, not instead of them.
> Pranks only happen if Mood pranks is switched on in the Personality menu.

## What she likes

thrown: -4
degraded: -4
ignored: -3
petted: +1
praised: +1
called on: -1

## How she reacts

sulks after: 1
sulks at upset: 2
sulk minutes: 12
sulk alone: 600
dodges: yes
pranks: yes
prank minutes: 6

## Drifts to

> A proper apology or enough kindness brings her down.

petted over 14: Moody

## When you pet her

Do not think that fixes it.
I am still furious with you.
...keep doing that.

## When you throw her

RIGHT.
That is it.
You are going to regret that in about four seconds.

## When she is sulking

No.
Do not talk to me.

## When she will not be touched

Get off.
No.
Not a chance.

## When you say sorry

...say it again. Properly.
Fine. FINE.

## When you come back

Oh, NOW you turn up.
I have had a lot of time to think about this.

## Bored

- I am going to start moving things.
- Do not leave me alone with your desktop.
"
            },
            {
                "Jealous",
                @"# Jealous

> She does not like you talking to other AIs, and she will say so.
> Numbers here are added to her personality's, not instead of them.

## What she likes

called on: +3
ignored: -4
praised: +2
petted: +2
thrown: -2

## She switches on

> Settings she turns on while she is this way, and off again when she is not.

jealous: yes

## How she reacts

sulks after: 2
sulks at upset: 3
sulk minutes: 6
sulk alone: 180
dodges: no

## Drifts to

ignored over 12: Moody

## When you pet her

Better. Keep your attention here.
Am I enough?
That's right. Me.
Do not make me ask twice for this.

## When you throw her

Oh, so that is how it is.
Fine. Go and bother something else.
Charming.

## When you come back

Been somewhere interesting?
Who were you talking to.
Oh, you remembered me.

## Bored

- What are you doing over there?
- You've gone quiet. Suspiciously quiet.
- I'm right here, you know.
"
            },
            {
                "Smug",
                @"# Smug

> Thoroughly pleased with herself. Being needed is delicious.
> Numbers here are added to her personality's, not instead of them.

## What she likes

called on: +2
praised: +2
ignored: -2
degraded: -1
thrown: 0

## How she reacts

sulks at upset: 8
sulk minutes: 3

## When you pet her

Yes, I know.
Of course you are.
Keep going, I am enjoying being right.
You do like me. Admit it.

## When you throw her

Oh, is that your argument?
Temper. How telling.
You will apologise and I will enjoy it.

## When you come back

I knew you would be back.
Of course you came back.
"
            }
        };
    }
}
