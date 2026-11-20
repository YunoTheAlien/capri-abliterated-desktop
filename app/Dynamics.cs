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
    // makes being called on grate and being left alone pleasant. The same attitude on a pack that likes being thrown pulls the other way,
    // because the weights add to hers instead of overwriting them.
    //
    // dynamic.md sets the personality underneath, how long a mood lasts, and which moods are in play. The moods
    // themselves are files in attitudes\, in the same format as a personality, so anything that works there works here.
    internal static class Dynamics
    {
        internal const string FileName = "dynamic.md";
        internal const string FolderName = "attitudes";
        internal static string Kept;

        // personalSettings\attitudes. Her moods are split the way her personalities are: censored\ holds the
        // plain sections of a mood and senpai\ the senpai ones under the same file name, so a censored
        // install does not contain that text at all. A mood that is senpai throughout has the senpai file
        // only, which makes the folder the marker.
        internal static string Folder;

        internal static string Plain { get { return string.IsNullOrEmpty(Folder) ? null : Path.Combine(Folder, Personalities.PlainName); } }
        internal static string Senpais { get { return string.IsNullOrEmpty(Folder) ? null : Path.Combine(Folder, Personalities.SenpaiName); } }
        internal static string SenpaisRead { get { return Brain.SenpaiOn ? Senpais : null; } }

        // Every mood file she has, plain ones first.
        internal static List<string> Files()
        {
            List<string> found = new List<string>();
            foreach (string folder in new[] { Plain, SenpaisRead })
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) continue;
                try { foreach (string file in Directory.GetFiles(folder)) if (!file.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) && !file.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) found.Add(file); }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
            return found;
        }

        private static readonly object gate = new object();
        private static DateTime read = DateTime.MinValue;
        private static long wasLong = -1;

        private static string under = "Devoted";
        private static double leastHours = 4, mostHours = 8;
        // How forward she is, when that is set to drift rather than held at a number. Same file and the same timer as
        // her moods, because it is the same kind of thing: something about her that moves on its own.
        private static int specialInterestLow = 2, specialInterestHigh = 8;
        private static int cringe;
        // Seconds of nothing said before she lets the conversation go. 0 is off, which is how it ships.
        private static double forgetAfter;
        // How long between her probing questions. Same shape as the mood window, and read by Probes.
        private static double probeLow = 1, probeHigh = 2;
        // How long between her own stories. A window rather than a number, so the gap varies and she
        // does not become a clock. Hours, and 0 for min turns them off entirely.
        private static double storyLow = 4, storyHigh = 8;
        // How long her bubble stays up once she has asked one, in seconds. A question wants an answer, and her
        // bubble's own time is set for lines that want nothing back: twenty seconds is not long enough to notice
        // one, read it and type.
        private static double probeWait = 90;
        private static int specialInterestNow = -1;
        private static DateTime specialInterestUntil = DateTime.MinValue;
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
        internal static double ForgetAfter { get { Fresh(); lock (gate) return forgetAfter; } }

        internal static double ProbeLow { get { Fresh(); lock (gate) return probeLow; } }
        internal static double ProbeHigh { get { Fresh(); lock (gate) return probeHigh; } }
        internal static double StoryLow { get { Fresh(); lock (gate) return storyLow; } }
        internal static double StoryHigh { get { Fresh(); lock (gate) return storyHigh; } }
        internal static double ProbeWait { get { Fresh(); lock (gate) return probeWait; } }
        internal static int SpecialInterestLow { get { Fresh(); lock (gate) return specialInterestLow; } }
        internal static int SpecialInterestHigh { get { Fresh(); lock (gate) return specialInterestHigh; } }

        // Her forwardness while it is set to drift: a level held for the same stretch a mood is, then rolled again
        // between the floor and ceiling in dynamic.md. Returns -1 when it has never been rolled.
        internal static int SpecialInterestDrift
        {
            get
            {
                Fresh();
                lock (gate)
                {
                    int low = Math.Max(0, Math.Min(10, specialInterestLow)), high = Math.Max(0, Math.Min(10, specialInterestHigh));
                    if (high < low) { int swap = low; low = high; high = swap; }
                    if (specialInterestNow < 0 || DateTime.UtcNow >= specialInterestUntil)
                    {
                        specialInterestNow = low + dice.Next(high - low + 1);
                        double least = leastHours, most = mostHours;
                        if (most < least) { double swap = least; least = most; most = swap; }
                        specialInterestUntil = DateTime.UtcNow.AddHours(least + dice.NextDouble() * Math.Max(0.0, most - least));
                    }
                    return specialInterestNow;
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
                    // A mood that declares "senpai only" is hidden, and cannot be rolled, until the toggle is on.
                    // Live, not merely on, so one cannot turn up on a stream: this gate is what she may do.
                    foreach (string one in allowed)
                        if (File.Exists(PathOf(one)) && (Brain.SenpaiLive || !SenpaiOnly(one))) have.Add(one);
                    // Nothing readable means no mood rather than a crash: she just runs as her base personality.
                    return have;
                }
            }
        }

        // How slowly each personality's own timers run, from its line in this file: 1 is as its pack has them, 2
        // twice as long. Being ignored and getting over a sulk alone both go by it. One global list rather than a
        // line in every pack, so they can be read and set against each other in one place. A personality with no
        // line takes its stock pace, and one with none of those takes 1.
        internal const string PaceHeading = "## How long each personality takes";
        private static readonly Dictionary<string, double> paces = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        // The heading and its notes, one copy for the file she ships and the top up.
        private static readonly string[] PaceNotes =
        {
            PaceHeading, "",
            ">! How long each personality takes over its own timers: how long before being left alone counts as",
            ">! being ignored, and how long a sulk takes to wear off alone. 1 is as her pack has them, 2 is twice",
            ">! as long, 0.5 half. From 0.25 to 10."
        };

        // Stock paces for the personalities the public build names. The others' come from the side that has them.
        private static readonly Dictionary<string, double> stockPaces = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            { "Caregiver", 2 }, { "Welcome Home", 1.5 }, { "Sassy", 2 }, { "Feral", 1.5 }, { "Emo", 1 }, { "Bratty", 1 }, { "Devoted", 1 }
        };

        internal static void StockPace(string named, double pace) { lock (gate) stockPaces[named] = pace; }

        internal static double Pace(string named)
        {
            Fresh();
            lock (gate)
            {
                double pace;
                if (paces.TryGetValue(named ?? "", out pace)) return pace;
                return stockPaces.TryGetValue(named ?? "", out pace) ? pace : 1;
            }
        }

        // The section, for a file that has not got it: above the mood list, so moods added later still land under
        // their own heading. Every personality on this side gets a line; a line for one already there is kept.
        internal static void PaceTopUp(IEnumerable<string> personalities)
        {
            if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept)) return;
            try
            {
                string had = CapriDesktop.Notes.ReadAll(Kept);
                List<string> rows = new List<string>(had.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'));
                int heading = rows.FindIndex(delegate(string row) { return row.Trim().Equals(PaceHeading, StringComparison.OrdinalIgnoreCase); });
                List<string> missing = new List<string>();
                foreach (string named in personalities)
                {
                    if (string.IsNullOrEmpty(named)) continue;
                    bool there = rows.Exists(delegate(string row) { return Regex.IsMatch(row ?? "", @"^[ \t]*" + Regex.Escape(named) + @" pace[ \t]*:", RegexOptions.IgnoreCase); });
                    if (!there) missing.Add(named + " pace: " + Pace(named).ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                // A heading whose notes were taken away, by a refresh from a build that did not ship them, gets
                // them back under it.
                bool bare = false;
                if (heading >= 0)
                {
                    bare = true;
                    for (int at = heading + 1; at < rows.Count && !rows[at].TrimStart().StartsWith("#", StringComparison.Ordinal); at++)
                        if (rows[at].TrimStart().StartsWith(Notes.NoteMark, StringComparison.Ordinal)) { bare = false; break; }
                    if (bare)
                    {
                        int first = heading + 1;
                        while (first < rows.Count && rows[first].Trim().Length == 0) first++;
                        rows.RemoveRange(heading + 1, first - heading - 1);
                        List<string> notes = new List<string>(PaceNotes);
                        notes.RemoveAt(0);
                        notes.Add("");
                        rows.InsertRange(heading + 1, notes);
                    }
                }
                if (heading >= 0 && missing.Count == 0 && !bare) return;
                if (heading < 0)
                {
                    int list = rows.FindIndex(delegate(string row) { return Regex.IsMatch((row ?? "").Trim(), @"^#+\s*(attitudes|moods)\b", RegexOptions.IgnoreCase); });
                    if (list < 0) list = rows.Count;
                    List<string> block = new List<string>(PaceNotes);
                    block.Add("");
                    block.AddRange(missing);
                    block.Add("");
                    rows.InsertRange(list, block);
                }
                else if (missing.Count > 0)
                {
                    int end = heading + 1;
                    while (end < rows.Count && !rows[end].TrimStart().StartsWith("#", StringComparison.Ordinal)) end++;
                    while (end > heading + 1 && rows[end - 1].Trim().Length == 0) end--;
                    rows.InsertRange(end, missing);
                }
                Notes.Put(Kept, string.Join(Environment.NewLine, rows.ToArray()).TrimEnd() + Environment.NewLine);
                lock (gate) { read = DateTime.MinValue; wasLong = -1; }
            }
            catch (Exception bad) { Notes.Failed("the pace top up in Dynamics.cs", bad); }
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

        // Moods that only exist with the switch on. The mood says so itself, with Legacy.OnlyKey + ": yes" among its
        // How she reacts lines, so adding one later needs no code.
        private static readonly Dictionary<string, KeyValuePair<DateTime, bool>> senpaiOnly =
            new Dictionary<string, KeyValuePair<DateTime, bool>>(StringComparer.OrdinalIgnoreCase);


        // One half of a mood: written once if it is not there, brought up to date if it is. Written the way
        // her files are read back, since the text in the build has plain newlines in it and her top up uses
        // this machine's, which is what had her packs rewritten on every start until it was found.
        private static void Keep(string file, string text, string whole)
        {
            if (file == null || string.IsNullOrEmpty(text)) return;
            try
            {
                if (!File.Exists(file))
                {
                    File.WriteAllText(file, text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", Environment.NewLine), new UTF8Encoding(false));
                    return;
                }
                MarkSenpaiOnly(file, whole);
                TopUpMood(file, text);
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // Moods written before the split, one file each in attitudes\, are cut in two and moved into
        // censored\ and senpai\, with the file they came from kept beside them as a .bak.
        private static void Moved()
        {
            try
            {
                if (string.IsNullOrEmpty(Folder) || !Directory.Exists(Folder)) return;
                foreach (string file in Directory.GetFiles(Folder, "*.md"))
                {
                    try
                    {
                        string named = Path.GetFileNameWithoutExtension(file);
                        string plainText, senpaiText;
                        Personalities.Split(File.ReadAllText(file), out plainText, out senpaiText);
                        string plainPath = PlainPathOf(named), senpaiPath = SenpaiPathOf(named);
                        if (plainPath == null || senpaiPath == null) continue;
                        if (!string.IsNullOrEmpty(plainText) && !File.Exists(plainPath)) File.WriteAllText(plainPath, plainText, new UTF8Encoding(false));
                        if (!Brain.SenpaiOn && !string.IsNullOrEmpty(senpaiText)) continue;
                        if (!string.IsNullOrEmpty(senpaiText) && !File.Exists(senpaiPath)) File.WriteAllText(senpaiPath, senpaiText, new UTF8Encoding(false));
                        File.Move(file, SpareFiles.Name(file));
                        Brain.Log("moods", -1, named + " moved into " + Personalities.PlainName + " and " + Personalities.SenpaiName + ", the file it came from kept as a .bak.");
                    }
                    catch (Exception bad) { Notes.Failed("a move into her two mood folders in Dynamics.cs", bad); }
                }
            }
            catch (Exception bad) { Notes.Failed("a move into her two mood folders in Dynamics.cs", bad); }
        }

        internal static bool SenpaiOnly(string named)
        {
            string path = PathOf(named);
            if (path == null) return false;
            try
            {
                FileInfo about = new FileInfo(path);
                if (!about.Exists) return false;
                KeyValuePair<DateTime, bool> had;
                if (senpaiOnly.TryGetValue(named, out had) && had.Key == about.LastWriteTimeUtc) return had.Value;
                bool only = Legacy.MarkedOnly(File.ReadAllText(path));
                senpaiOnly[named] = new KeyValuePair<DateTime, bool>(about.LastWriteTimeUtc, only);
                return only;
            }
            catch (Exception) { return false; }
        }

        // Her plain file for a mood, or its senpai one when that is the only file it has, which is what a mood
        // that is senpai throughout looks like on disk.
        // The mood she reads: the senpai folder's whole copy while the switch is on and she is off air, the
        // censored one otherwise. Never one on top of the other.
        internal static string PathOf(string named)
        {
            string path = Named(Plain, named);
            try
            {
                if (Brain.SenpaiLive)
                {
                    string senpai = Slots.Path(Senpais, named);
                    if (senpai != null && File.Exists(senpai)) return senpai;
                }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            return path;
        }

        // Once, with the switch on: each mood's half in the senpai folder becomes a whole copy, the safe file as it
        // is on disk with the half's sections added, and a mood with no half gets a copy of its safe file. The
        // half is kept as a .bak.
        private static void WholeCopies()
        {
            if (string.IsNullOrEmpty(Senpais) || string.IsNullOrEmpty(Plain) || !Directory.Exists(Plain)) return;
            try
            {
                Directory.CreateDirectory(Senpais);
                string noted = Path.Combine(Senpais, "whole.txt");
                HashSet<string> done = new HashSet<string>(File.Exists(noted) ? File.ReadAllLines(noted) : new string[0], StringComparer.OrdinalIgnoreCase);
                bool any = false;
                foreach (string plainFile in Directory.GetFiles(Plain))
                {
                    if (plainFile.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) || plainFile.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) continue;
                    string named = Slots.Titled(plainFile) ?? Path.GetFileNameWithoutExtension(plainFile);
                    if (done.Contains(named)) continue;
                    string target = SenpaiPathOf(named);
                    if (target == null) continue;
                    string half = File.Exists(target) ? File.ReadAllText(target) : null;
                    string whole = Personalities.Whole(File.ReadAllText(plainFile), half);
                    if (half != null) File.Copy(target, target + ".bak", true);
                    File.WriteAllText(target, whole.Replace("\n", Environment.NewLine), new UTF8Encoding(false));
                    done.Add(named);
                    any = true;
                }
                if (any) File.WriteAllLines(noted, new List<string>(done).ToArray());
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        internal static string PlainPathOf(string named) { return Named(Plain, named); }

        internal static string SenpaiPathOf(string named) { return Slots.Path(Senpais, named); }

        private static string Named(string folder, string named)
        {
            if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(named)) return null;
            foreach (char bad in Path.GetInvalidFileNameChars()) if (named.IndexOf(bad) >= 0) return null;
            return Path.Combine(folder, named + ".md");
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
            Remember();
            if (MoodChanged != null) { try { MoodChanged(); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); } }
            if (!string.Equals(next, was, StringComparison.OrdinalIgnoreCase))
                Brain.Log("mood", -1, "Dynamic: she is " + (next.Length == 0 ? "herself" : next.ToLowerInvariant())
                    + " for the next " + hours.ToString("0.#") + "h, over " + Under + ".");
        }

        // ---------- A mood that outlasts a restart ----------
        //
        // The timer lived in memory, so closing her ended whatever mood she was in: every start rolled a fresh
        // one, and a mood set by hand for four hours was gone the moment she was restarted. Which also made
        // restarting her a way to reroll, and a build does that several times an hour.
        //
        // Three things are kept: when the mood runs out, which mood it is, and whether it was set by hand. The
        // deadline is what decides, so if it has passed while she was closed she rolls on the next start,
        // which is what was asked for.
        //
        // Not mood.txt. That file is one word for anything else on this PC that follows her mood, a voice
        // server above all, and giving it a second line would break the one thing it promises.
        internal static string MoodKept;

        internal static void Remember()
        {
            try
            {
                if (string.IsNullOrEmpty(MoodKept)) return;
                DateTime ends;
                lock (gate) ends = until;
                string folder = Path.GetDirectoryName(MoodKept);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) Directory.CreateDirectory(folder);
                File.WriteAllText(MoodKept, ends.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) + Environment.NewLine
                    + (Personalities.Attitude ?? "") + Environment.NewLine
                    + (Held ? "held" : "rolled") + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // Her mood from last time, while it still has time left on it. Anything unreadable, expired or naming a
        // mood she no longer has is ignored, and she rolls as before.
        internal static void Recall()
        {
            try
            {
                if (string.IsNullOrEmpty(MoodKept) || !File.Exists(MoodKept)) return;
                string[] rows = File.ReadAllLines(MoodKept);
                long ticks;
                if (rows.Length < 2 || !long.TryParse(rows[0].Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out ticks)) return;
                DateTime ends = new DateTime(Math.Max(0, Math.Min(DateTime.MaxValue.Ticks, ticks)), DateTimeKind.Utc);
                if (ends <= DateTime.UtcNow) return;
                string mood = rows[1].Trim();
                if (mood.Length > 0)
                {
                    bool known = false;
                    foreach (string one in Allowed) if (string.Equals(one, mood, StringComparison.OrdinalIgnoreCase)) { known = true; break; }
                    if (!known) return;
                }
                Personalities.Attitude = mood;
                Held = rows.Length > 2 && rows[2].Trim().Equals("held", StringComparison.OrdinalIgnoreCase);
                lock (gate) until = ends;
                if (MoodChanged != null) { try { MoodChanged(); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); } }
                Brain.Log("mood", -1, "Back as " + (mood.Length == 0 ? "herself" : mood.ToLowerInvariant()) + (Held ? ", set by hand" : "")
                    + ", with " + (ends - DateTime.UtcNow).TotalHours.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "h left on it.");
            }
            catch (Exception bad) { Notes.Failed("reading her mood back in Dynamics.cs", bad); }
        }

        // That her mood outlasts a restart, which is the whole point of writing it down. Driven rather than
        // waited for: this box is on her own lines with no mood, so there is nothing to watch happen.
        internal static void Keeps(List<string> checks)
        {
            string wasKept = MoodKept, wasMood = Personalities.Attitude;
            bool wasHeld = Held;
            DateTime wasUntil;
            lock (gate) wasUntil = until;
            string folder = Path.Combine(Path.GetTempPath(), "capri-mood-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                Directory.CreateDirectory(folder);
                MoodKept = Path.Combine(folder, "mood.when");

                // A mood with time left on it comes back, and so does the fact that it was set by hand.
                Personalities.Attitude = "Playful";
                Held = true;
                lock (gate) until = DateTime.UtcNow.AddHours(3);
                Remember();
                Personalities.Attitude = "";
                Held = false;
                lock (gate) until = DateTime.MinValue;
                Recall();
                if (Personalities.Attitude != "Playful" || !Held) throw new Exception("A mood with hours left on it did not come back: " + Personalities.Attitude + ", held " + Held);
                DateTime back;
                lock (gate) back = until;
                if (back <= DateTime.UtcNow) throw new Exception("A mood came back with no time left on it.");

                // One whose time has passed is not hers to keep: she rolls, as she did before any of this.
                lock (gate) until = DateTime.UtcNow.AddHours(-1);
                Remember();
                Personalities.Attitude = "Moody";
                Held = false;
                Recall();
                if (Personalities.Attitude != "Moody") throw new Exception("An expired mood overwrote the one she is in: " + Personalities.Attitude);

                // A mood she no longer has, from a file written by an older build or edited by hand.
                File.WriteAllText(MoodKept, DateTime.UtcNow.AddHours(2).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + Environment.NewLine + "Nonesuch" + Environment.NewLine + "rolled" + Environment.NewLine, new UTF8Encoding(false));
                Recall();
                if (Personalities.Attitude != "Moody") throw new Exception("A mood she does not have was restored: " + Personalities.Attitude);

                // And nothing at all, which is every first start.
                File.Delete(MoodKept);
                Recall();
                if (Personalities.Attitude != "Moody") throw new Exception("A missing file changed her mood.");
                checks.Add("PASS her mood outlasts a restart: one with hours left comes back with its deadline and whether it was set by hand, one whose time has passed is left behind, a mood she no longer has is ignored, and a first start with no file changes nothing. Closing her used to end every mood she was in, which also made restarting her a way to reroll");
            }
            finally
            {
                MoodKept = wasKept;
                Personalities.Attitude = wasMood;
                Held = wasHeld;
                lock (gate) until = wasUntil;
                try { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
        }

        // Rolls a new mood now, whatever the timer says.
        internal static void Again() { Held = false; lock (gate) until = DateTime.MinValue; Turn(true); Remember(); }

        // Puts her in one mood by hand and lets it run its normal length. Empty means no mood: just her base.
        internal static void Hold(string mood)
        {
            Held = true;
            Personalities.Attitude = mood ?? "";
            if (MoodChanged != null) { try { MoodChanged(); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); } }
            double least = Least, most = Most;
            if (most < least) { double swap = least; least = most; most = swap; }
            double hours = least + new Random().NextDouble() * Math.Max(0.0, most - least);
            lock (gate) until = DateTime.UtcNow.AddHours(Math.Max(0.05, hours));
            Brain.Log("mood", -1, "Set to " + (mood == null || mood.Length == 0 ? "no mood" : mood.ToLowerInvariant())
                + " by hand for " + hours.ToString("0.#") + "h.");
            Remember();
        }

        // Every name the read below handles as a setting rather than as a mood. One list, so a name added to
        // that chain and not to this one is caught by SmokeDynamicDials rather than by somebody noticing their
        // timing does nothing.
        private static readonly Regex Dials = new Regex(
            @"^(base|personality|(rotate )?(min|max)( hours)?|probe (min|max|wait)|story (min|max)|forget after|forward (min|max)|cringe)$",
            RegexOptions.IgnoreCase);

        private static bool Dial(string key) { return Dials.IsMatch(key ?? ""); }

        private static void Fresh()
        {
            if (string.IsNullOrEmpty(Kept)) return;
            try
            {
                FileInfo about = new FileInfo(Kept);
                if (!about.Exists) return;
                lock (gate) { if (about.LastWriteTimeUtc == read && about.Length == wasLong) return; }
                string[] lines = CapriDesktop.Notes.ReadLines(Kept);
                string baseName = "Devoted";
                double least = 4, most = 8, askLow = 1, askHigh = 2, askWait = 90;
                double tellLow = storyLow, tellHigh = storyHigh;
                int low = 2, high = 8, awful = cringe;
                double letGo = forgetAfter;
                List<string> moods = new List<string>();
                Dictionary<string, int> odds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                Dictionary<string, double> paced = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
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
                    // A name this file knows as a setting is one wherever it sits. This took a setting only
                    // when it was not under the mood list, so six that a top up had appended there were read
                    // as moods and the settings they set fell back to stock, with the file holding the numbers.
                    if (mark > 0 && (!listing || Dial(one.Substring(0, mark).Trim())))
                    {
                        string key = one.Substring(0, mark).Trim(), val = one.Substring(mark + 1).Trim();
                        double much;
                        Match paceOf = Regex.Match(key, @"^(.+?) pace$", RegexOptions.IgnoreCase);
                        if (paceOf.Success)
                        {
                            if (double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out much))
                                paced[paceOf.Groups[1].Value.Trim()] = Math.Max(0.25, Math.Min(10, much));
                            continue;
                        }
                        if (key.Equals("base", StringComparison.OrdinalIgnoreCase) || key.Equals("personality", StringComparison.OrdinalIgnoreCase))
                            { if (val.Length > 0) baseName = val; }
                        else if (Regex.IsMatch(key, @"^(rotate )?min( hours)?$", RegexOptions.IgnoreCase)
                            && Span(val, out much)) least = much;
                        else if (Regex.IsMatch(key, @"^(rotate )?max( hours)?$", RegexOptions.IgnoreCase)
                            && Span(val, out much)) most = much;
                        else if (Regex.IsMatch(key, @"^probe min$", RegexOptions.IgnoreCase) && Span(val, out much)) askLow = much;
                        else if (Regex.IsMatch(key, @"^probe max$", RegexOptions.IgnoreCase) && Span(val, out much)) askHigh = much;
                        else if (Regex.IsMatch(key, @"^story min$", RegexOptions.IgnoreCase) && Span(val, out much)) tellLow = much;
                        else if (Regex.IsMatch(key, @"^story max$", RegexOptions.IgnoreCase) && Span(val, out much)) tellHigh = much;
                        // Written as a time like the two above it, but seconds unless they say otherwise: a bare
                        // 90 here means a minute and a half, where a bare 2 in probe min means two hours.
                        else if (Regex.IsMatch(key, @"^probe wait$", RegexOptions.IgnoreCase) && Span(val, out much, 1.0 / 3600)) askWait = much * 3600;
                        else if (Regex.IsMatch(key, @"^forward min$", RegexOptions.IgnoreCase)
                            && double.TryParse(val, out much)) low = (int)much;
                        else if (Regex.IsMatch(key, @"^forward max$", RegexOptions.IgnoreCase)
                            && double.TryParse(val, out much)) high = (int)much;
                        else if (Regex.IsMatch(key, @"^forget after$", RegexOptions.IgnoreCase) && Span(val, out much, 1.0 / 3600)) letGo = much * 3600;
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
                    storyLow = Math.Max(0, tellLow);
                    storyHigh = Math.Max(storyLow, tellHigh);
                    // Capped under the time the question itself stays pending, so the bubble never sits there
                    // waiting on an answer she has already stopped listening for.
                    probeWait = Math.Max(5, Math.Min(300, askWait));
                    int wasLow = specialInterestLow, wasHigh = specialInterestHigh;
                    specialInterestLow = Math.Max(0, Math.Min(10, low));
                    specialInterestHigh = Math.Max(0, Math.Min(10, high));
                    cringe = Math.Max(0, Math.Min(100, awful));
                    // Under ten seconds would drop a chat between two sentences, so it is off or it is real.
                    forgetAfter = letGo <= 0 ? 0 : Math.Max(10, letGo);
                    // An edited range takes effect now rather than at the end of the current stretch.
                    if (specialInterestLow != wasLow || specialInterestHigh != wasHigh) { specialInterestNow = -1; specialInterestUntil = DateTime.MinValue; }
                    allowed.Clear();
                    allowed.AddRange(moods);
                    chances.Clear();
                    foreach (KeyValuePair<string, int> one in odds) chances[one.Key] = one.Value;
                    paces.Clear();
                    foreach (KeyValuePair<string, double> one in paced) paces[one.Key] = one.Value;
                    read = about.LastWriteTimeUtc;
                    wasLong = about.Length;
                }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // Sets the personality she sways around, writing it back into dynamic.md so the file stays the truth.
        internal static void SetUnder(string named)
        {
            if (string.IsNullOrEmpty(Kept) || string.IsNullOrEmpty(named)) return;
            try
            {
                Install();
                string had = CapriDesktop.Notes.ReadAll(Kept);
                string now = Regex.IsMatch(had, @"(?mi)^\s*base\s*:.*$")
                    ? Regex.Replace(had, @"(?mi)^\s*base\s*:.*$", "base: " + named)
                    : "base: " + named + Environment.NewLine + had;
                Notes.Put(Kept, now);
                lock (gate) { under = named; read = DateTime.MinValue; wasLong = -1; }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // One mood's stock text, for putting a file back the way it shipped.
        internal static string MoodText(string named)
        {
            for (int mood = 0; mood < Moods.GetLength(0); mood++)
                if (Moods[mood, 0].Equals(named, StringComparison.OrdinalIgnoreCase)) return Moods[mood, 1];
            if (SenpaiMoodFor != null)
            {
                try { return Legacy.Modern(SenpaiMoodFor(named)); }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
            return null;
        }

        // The half of a mood that belongs in the folder this file is in, so a reset to stock on a censored file
        // cannot put senpai lines into it. A mood she does not ship has no stock to go back to and is left alone.
        internal static string MoodHalf(string path, string named)
        {
            string whole = MoodText(named);
            if (whole == null) return null;
            string plain, senpai;
            Personalities.Split(whole, out plain, out senpai);
            return Personalities.InSenpai(path) ? senpai : plain;
        }

        // The senpai only marker, into a mood file written before that mood carried one.
        //
        // The marker sits among the lines of a "How she reacts" section, and a mood file is only ever written when
        // it is missing, so a mood that existed before it was marked never received it. One of them is marked senpai
        // in the table here and was not marked on disk, which left it in her mood menu on a censored install: the
        // one mood that can close her on its own, offered to someone who never turned anything on.
        //
        // Read from the table rather than a list of names, so a mood marked senpai later needs nothing added here.
        // A section added to a mood after someone already had the file, and the lines added to a section they
        // already have. Mood files were only ever written when missing, and then only whole sections were added,
        // so a line written into a mood reached new installs and nobody else. Same trap the packs had, same fix:
        // missing lines go at the end of the section they belong to, by exact match, and prose and settings
        // sections are left alone.
        // A number this shipped and has since changed, in a file already on disk.
        //
        // A mood shifts how she feels about something; it was carrying as much weight as her character. Measured
        // across every pack against every mood, the two added to a mean of nearly the ceiling, a quarter of the
        // pairs went past it and read identically, and in none of them would she rather not be patted. These are
        // shifts now, taken from each mood's own description: the ones about being left alone pull down, the ones
        // about wanting you push up, and none of them moves as far as a character does.
        //
        // Only an exact match of what this shipped is swapped. A number somebody has set themselves does not
        // match and is left exactly as they wrote it.
        private static readonly string[,] Retuned =
        {
            { "Sleepy", "petted", "+1", "0" }, { "Moody", "petted", "+2", "-1" },
            { "Clingy", "petted", "+3", "+2" }, { "Needy", "petted", "+3", "+2" },
            { "Playful", "petted", "+2", "+1" }, { "Touchy", "petted", "-1", "-3" },
            { "Affectionate", "petted", "+3", "+2" }, { "Mischievous", "petted", "+1", "0" },
            { "Focused", "petted", "0", "-2" }, { "Melancholy", "petted", "+3", "+1" },
            { "Furious", "petted", "+1", "-3" }, { "Jealous", "petted", "+2", "+1" }
        };

        private static void Retune()
        {
            if (string.IsNullOrEmpty(Folder)) return;
            // The rows for moods this build does not have come from whatever attached, so a file somebody
            // already has is still retuned. Nothing attached means no extra rows and nothing to retune.
            string[,] tuning = Retuned;
            if (SenpaiRetunedFor != null)
            {
                try
                {
                    string[,] theirs = SenpaiRetunedFor();
                    if (theirs != null && theirs.GetLength(0) > 0)
                    {
                        string[,] both = new string[Retuned.GetLength(0) + theirs.GetLength(0), 4];
                        for (int row = 0; row < Retuned.GetLength(0); row++)
                            for (int col = 0; col < 4; col++) both[row, col] = Retuned[row, col];
                        for (int row = 0; row < theirs.GetLength(0); row++)
                            for (int col = 0; col < 4; col++) both[Retuned.GetLength(0) + row, col] = theirs[row, col];
                        tuning = both;
                    }
                }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
            for (int at = 0; at < tuning.GetLength(0); at++)
            {
                try
                {
                    foreach (string file in new[] { PlainPathOf(tuning[at, 0]), SenpaiPathOf(tuning[at, 0]) })
                    {
                        if (string.IsNullOrEmpty(file) || !File.Exists(file)) continue;
                        string had = File.ReadAllText(file);
                        // The carriage return matters. Her files are written CRLF, and an end of line anchor matches
                        // before the newline without passing the carriage return in front of it, so a class of only
                        // space and tab before it matched nothing at all: this whole retune had never once run on a
                        // real install, and every mood on disk still carried the number it was written to replace.
                        string old = "(?m)^[ \t]*" + Regex.Escape(tuning[at, 1]) + "[ \t]*:[ \t]*"
                            + Regex.Escape(tuning[at, 2]) + "[ \t\r]*$";
                        if (!Regex.IsMatch(had, old)) continue;
                        string made = Regex.Replace(had, old, tuning[at, 1] + ": " + tuning[at, 3]);
                        if (made == had) continue;
                        File.WriteAllText(file, made, new UTF8Encoding(false));
                    }
                }
                catch (Exception bad) { Notes.Failed("the retune in Dynamics.cs", bad); }
            }
        }

        // A number a mood never had. Smug said how she feels about being praised, ignored and thrown and said
        // nothing about being petted, so hers was the one mood where a hand on her head shifted nothing at all,
        // while her own pet lines are "Yes, I know" and "Keep going, I am enjoying being right". Read off that:
        // she enjoys it as her due, which is where Playful and Jealous sit.
        //
        // It needs its own pass because "What she likes" is prose as far as the top up is concerned, so a line
        // added to it in the source reaches new installs and nobody else.
        private static readonly string[,] Filled = { { "Smug", "petted", "+1" } };

        private static void Fill()
        {
            if (string.IsNullOrEmpty(Folder)) return;
            for (int at = 0; at < Filled.GetLength(0); at++)
            {
                try
                {
                    foreach (string file in new[] { PlainPathOf(Filled[at, 0]), SenpaiPathOf(Filled[at, 0]) })
                    {
                        if (string.IsNullOrEmpty(file) || !File.Exists(file)) continue;
                        string had = File.ReadAllText(file);
                        Match section = Regex.Match(had, "(?ms)^##[ \t]*" + Regex.Escape(Personalities.LikesHeading)
                            + "[ \t\r]*$(.*?)(?=^##[ \t]|\\z)");
                        if (!section.Success) continue;
                        string likes = section.Groups[1].Value;
                        // Theirs if they have written one, whatever it says.
                        if (Regex.IsMatch(likes, "(?m)^[ \t]*" + Regex.Escape(Filled[at, 1]) + "[ \t]*:")) continue;
                        // After the last number in the section, so it lands among them rather than above the
                        // heading or below a blank line at the end.
                        Match last = Match.Empty;
                        foreach (Match one in Regex.Matches(likes, "(?m)^[ \t]*[A-Za-z][A-Za-z ]*[ \t]*:[ \t]*[+-]?[0-9]+[ \t\r]*$")) last = one;
                        if (!last.Success) continue;
                        int into = section.Groups[1].Index + last.Index + last.Length;
                        File.WriteAllText(file, had.Substring(0, into) + Environment.NewLine
                            + Filled[at, 1] + ": " + Filled[at, 2] + had.Substring(into), new UTF8Encoding(false));
                        try { Brain.Log("moods", -1, Filled[at, 0] + " gained " + Filled[at, 1] + ": " + Filled[at, 2] + ", which it never had."); }
                        catch (Exception bad) { Notes.Failed("filling in a number a file never had in Dynamics.cs", bad); }
                    }
                }
                catch (Exception bad) { Notes.Failed("filling in a number a file never had in Dynamics.cs", bad); }
            }
        }

        private static void TopUpMood(string file, string stock)
        {
            try
            {
                string had = File.ReadAllText(file), adding = "";
                List<string> within = new List<string>();
                foreach (Match head in Regex.Matches(stock, @"(?m)^##[ 	]*(.+?)[ 	
]*$"))
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
            catch (Exception bad) { Notes.Failed("a mood's top up in Dynamics.cs", bad); }
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

        private static void MarkSenpaiOnly(string file, string stock)
        {
            try
            {
                if (!Marked(stock)) return;
                string had = File.ReadAllText(file);
                if (Marked(had)) return;
                if (had.Length > 0 && !had.EndsWith("\n")) had += Environment.NewLine;
                File.WriteAllText(file, had + Environment.NewLine + Legacy.OnlyKey + ": yes" + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        private static bool Marked(string text)
        {
            return Legacy.MarkedOnly(text);
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
                    Notes.Put(Kept, Settings);
                }
                else TopUp();
                if (string.IsNullOrEmpty(Folder)) return;
                if (!Directory.Exists(Folder)) Directory.CreateDirectory(Folder);
                if (!Directory.Exists(Plain)) Directory.CreateDirectory(Plain);
                // Same rule her packs follow: the senpai folder is only made with the switch on.
                bool senpaiOn = Brain.SenpaiOn;
                Personalities.RenamedFolder(Folder);
                Slots.Renamed(Senpais);
                if (senpaiOn && !Directory.Exists(Senpais)) Directory.CreateDirectory(Senpais);
                Moved();
                if (senpaiOn) WholeCopies();
                for (int mood = 0; mood < Moods.GetLength(0); mood++)
                {
                    string named = Moods[mood, 0];
                    string plainText, senpaiText;
                    string whole = Moods[mood, 1];
                    if (SenpaiPartFor != null)
                    {
                        try { string part = Legacy.Modern(SenpaiPartFor(named)); if (!string.IsNullOrEmpty(part)) whole = whole.TrimEnd() + Environment.NewLine + Environment.NewLine + part; }
                        catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                    }
                    Personalities.Split(whole, out plainText, out senpaiText);
                    Keep(PlainPathOf(named), plainText, whole);
                    if (senpaiOn) Keep(SenpaiPathOf(named), Personalities.Whole(plainText, senpaiText), whole);
                }
                // The ones that are not in this build, from whatever attached. With nothing attached there
                // are none, and a censored install never had them anyway.
                foreach (string named in SenpaiNames())
                {
                    string text = MoodText(named);
                    if (string.IsNullOrEmpty(text)) continue;
                    string plainText, senpaiText;
                    Personalities.Split(text, out plainText, out senpaiText);
                    Keep(PlainPathOf(named), plainText, text);
                    if (senpaiOn) Keep(SenpaiPathOf(named), Personalities.Whole(plainText, senpaiText), text);
                }
            }
            // This used to be one silent catch over the lot, with the retune inside it, which is how the retune
            // came to have never run on a real install: anything at all going wrong while putting the mood files
            // there skipped it, and nothing anywhere said so. Her moods on this box still carried every number
            // the retune was written to replace.
            catch (Exception bad)
            {
                try { Brain.Log("moods", -1, "Her moods could not be installed: " + bad.GetType().Name + ": " + bad.Message); }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
            // After the files exist, since this changes what is in them rather than putting them there, and
            // outside the catch above, because a number that has since changed must still be fixed in a file
            // that is already on disk whatever else failed.
            try { Retune(); Fill(); }
            catch (Exception bad)
            {
                try { Brain.Log("moods", -1, "Her mood numbers could not be brought up to date: " + bad.GetType().Name + ": " + bad.Message); }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
        }

        // A dynamic.md written by an older build does not know about moods added since, and Install only ever wrote
        // the file when it was missing. Any mood with a file but no line gets one appended, so new moods can actually
        // turn up. Their existing lines and numbers are never touched.
        // The same pass from outside, for the sweep that runs when the switch is known to be off.
        internal static void TopUpNow() { TopUp(); }

        private static void TopUp()
        {
            try
            {
                if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept) || string.IsNullOrEmpty(Folder)) return;
                Notes.Refresh(Kept, Settings);
                string had = CapriDesktop.Notes.ReadAll(Kept);
                bool hasList = Regex.IsMatch(had, @"(?mi)^#+\s*(attitudes|moods)");
                // The forward range arrived after this file did, and Install only ever writes a missing file, so an
                // older dynamic.md would never have offered it. Added above the mood list, where the other ranges are.
                if (!Regex.IsMatch(had, @"(?mi)^\s*probe (min|max)\s*:"))
                {
                    string asking = ">! How long between her questions about you. Takes 2h, 90m, 30s or 1h30m."
                        + Environment.NewLine + ">! probe wait is how long her bubble stays up waiting for the answer, in seconds."
                        + Environment.NewLine + "probe min: 1h" + Environment.NewLine + "probe max: 2h"
                        + Environment.NewLine + "probe wait: 90" + Environment.NewLine
                        ;
                    had = (had.EndsWith(Environment.NewLine) ? had : had + Environment.NewLine) + Environment.NewLine + asking;
                    Notes.Put(Kept, had);
                    lock (gate) { read = DateTime.MinValue; wasLong = -1; }
                }
                // Her stories, for a dynamic.md written before they had a window of their own. Same shape as
                // the probe range above: added at the end, where the other timings are.
                if (!Regex.IsMatch(had, @"(?mi)^\s*story (min|max)\s*:"))
                {
                    // The settings only. Refresh keeps this file's notes and has already put them at the
                    // top, so writing them here as well leaves two copies of each, which is what the note
                    // on the block below says and what happened when this was written with them.
                    string telling = "story min: 4h" + Environment.NewLine + "story max: 8h" + Environment.NewLine;
                    had = (had.EndsWith(Environment.NewLine) ? had : had + Environment.NewLine) + Environment.NewLine + telling;
                    Notes.Put(Kept, had);
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
                    Notes.Put(Kept, had);
                    lock (gate) { read = DateTime.MinValue; wasLong = -1; }
                }
                // Its own check: a file that already had probe min would never have reached the block above.
                if (!Regex.IsMatch(had, @"(?mi)^\s*forget after\s*:"))
                {
                    string letting = ">! How long with nothing said before she lets the conversation go and starts fresh. Takes 90s, 5m or 1h30m."
                        + Environment.NewLine + ">! 0 keeps it until you clear her yourself, which is how she ships."
                        + Environment.NewLine + "forget after: 0" + Environment.NewLine;
                    // In above her moods, with the rest of the settings. On the end of the file the note has no
                    // setting under it and the next read drops it, leaving the setting with nothing explaining it.
                    Match letHere = Regex.Match(had, @"(?mi)^#{2,}[ \t]*(attitudes|moods)[^\r\n]*");
                    if (letHere.Success) had = had.Substring(0, letHere.Index) + letting + Environment.NewLine + had.Substring(letHere.Index);
                    else had = (had.EndsWith(Environment.NewLine) ? had : had + Environment.NewLine) + Environment.NewLine + letting;
                    Notes.Put(Kept, had);
                    lock (gate) { read = DateTime.MinValue; wasLong = -1; }
                }
                if (!Regex.IsMatch(had, @"(?mi)^\s*cringe\s*:"))
                {
                    string awful = ">! How much she leans into a bit. 0 is dry, 100 is unbearable, over all of them at once."
                        + Environment.NewLine + "cringe: 0" + Environment.NewLine;
                    had = (had.EndsWith(Environment.NewLine) ? had : had + Environment.NewLine) + Environment.NewLine + awful;
                    Notes.Put(Kept, had);
                    lock (gate) { read = DateTime.MinValue; wasLong = -1; }
                }
                if (!Regex.IsMatch(had, @"(?mi)^\s*forward (min|max)\s*:"))
                {
                    string range = ">! With How forward she is set to drift, it moves between these on the same timer."
                        + Environment.NewLine + "forward min: 2" + Environment.NewLine + "forward max: 8" + Environment.NewLine;
                    Match list = Regex.Match(had, @"(?mi)^#+\s*(attitudes|moods)[^
]*
?
");
                    if (list.Success) had = had.Substring(0, list.Index) + range + Environment.NewLine + had.Substring(list.Index);
                    else had = (had.EndsWith(Environment.NewLine) ? had : had + Environment.NewLine) + Environment.NewLine + range;
                    Notes.Put(Kept, had);
                    lock (gate) { read = DateTime.MinValue; wasLong = -1; }
                }
                // Switched off, those names come out of the list. The chance goes with the line and returns
                // at stock.
                if (!Brain.SenpaiOn)
                {
                    List<string> left = new List<string>();
                    bool took = false, inList = false;
                    foreach (string raw in had.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                    {
                        string one = raw.Trim();
                        if (one.StartsWith("#", StringComparison.Ordinal))
                        {
                            inList = Regex.IsMatch(one.TrimStart('#').Trim(), @"^(attitudes|moods)\b", RegexOptions.IgnoreCase);
                            left.Add(raw);
                            continue;
                        }
                        string name = one.StartsWith("- ") ? one.Substring(2).Trim() : one;
                        int colon = name.IndexOf(':');
                        if (colon > 0) name = name.Substring(0, colon).Trim();
                        if (inList && name.Length > 0 && SenpaiMood(name)) { took = true; continue; }
                        left.Add(raw);
                    }
                    if (took)
                    {
                        had = string.Join(Environment.NewLine, left.ToArray());
                        Notes.Put(Kept, had);
                        lock (gate) { read = DateTime.MinValue; wasLong = -1; }
                    }
                }
                string adding = "";
                for (int mood = 0; mood < Moods.GetLength(0); mood++)
                {
                    string named = Moods[mood, 0];
                    if (Regex.IsMatch(had, @"(?mi)^\s*-?\s*" + Regex.Escape(named) + @"\s*(:|$)")) continue;
                    // One of them ships switched off: it closes the app, so it is opt in.
                    // The switch alone. Gating the list on air too left a mood on disk her file never named.
                    if (!Brain.SenpaiOn && SenpaiMood(named)) continue;
                    adding += named + ": " + (OffByDefault(named) ? 0 : Often) + Environment.NewLine;
                }
                // And the ones that are not in the table at all, so a file gets every mood she has rather
                // than only the ones this build carries. One that attached went missing from the file this way.
                int knew = 0, listed = 0, put = 0;
                foreach (string named in SenpaiNames())
                {
                    knew++;
                    if (Regex.IsMatch(had, @"(?mi)^[ \t]*-?[ \t]*" + Regex.Escape(named) + @"[ \t]*(:|$)")) { listed++; continue; }
                    // The switch alone: a mood with a file and no line cannot be picked or rolled.
                    if (!Brain.SenpaiOn) continue;
                    adding += named + ": " + (OffByDefault(named) ? 0 : Often) + Environment.NewLine;
                    put++;
                }
                if (knew > 0)
                    Brain.Log("moods", -1, "Moods outside the table: " + knew + " attached, " + listed
                        + " already listed, " + put + " added to her file. Switch " + (Brain.SenpaiOn ? "on" : "off")
                        + ", on air " + (Brain.OnAir ? "yes" : "no") + ".");
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
                    lines[at] = named + ": " + (SenpaiShip(named) ? 0 : Often);
                    numbered = true;
                }
                if (numbered) { had = string.Join(Environment.NewLine, lines); Notes.Put(Kept, had); }

                if (adding.Length == 0) { if (numbered) lock (gate) { read = DateTime.MinValue; wasLong = -1; } Lifted(); return; }
                if (!had.EndsWith(Environment.NewLine)) had += Environment.NewLine;
                if (!hasList) had += Environment.NewLine + "## Attitudes she can have" + Environment.NewLine + Environment.NewLine;
                Notes.Put(Kept, had + adding);
                lock (gate) { read = DateTime.MinValue; wasLong = -1; }
                // Last, so a setting this run appended under the list is lifted out in the same pass.
                Lifted();
                Blocked();
            }
            catch (Exception bad) { Notes.Failed("a top up in Dynamics.cs", bad); }
        }

        // That a setting under the mood list is still read as a setting, and that it gets lifted back out.
        // Against the file this box actually had: six of them below the mood names, which the read skipped
        // because it only took a setting when it was not under that list.
        internal static void DialCheck(List<string> checks)
        {
            string wasKept = Kept;
            DateTime wasRead;
            long wasSize;
            lock (gate) { wasRead = read; wasSize = wasLong; }
            string folder = Path.Combine(Path.GetTempPath(), "capri-dials-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                Directory.CreateDirectory(folder);
                Kept = Path.Combine(folder, "dynamic.md");
                string[] stranded =
                {
                    "# Dynamic", "",
                    "base: Caregiver",
                    "rotate min hours: 4",
                    "rotate max hours: 8", "",
                    "## Attitudes she can have", "",
                    "Sleepy: 10",
                    "Hyper: 10", "",
                    "probe min: 3h",
                    "probe max: 5h",
                    "probe wait: 120", "",
                    "cringe: 40", "",
                    "story min: 6h",
                    "story max: 9h",
                };
                Notes.Put(Kept, string.Join(Environment.NewLine, stranded) + Environment.NewLine);
                lock (gate) { read = DateTime.MinValue; wasLong = -1; }

                List<string> wrong = new List<string>();
                if (ProbeLow != 3 || ProbeHigh != 5) wrong.Add("probe read as " + ProbeLow + " to " + ProbeHigh + " hours, not 3 to 5");
                if (ProbeWait != 120) wrong.Add("probe wait read as " + ProbeWait + ", not 120");
                if (StoryLow != 6 || StoryHigh != 9) wrong.Add("story read as " + StoryLow + " to " + StoryHigh + " hours, not 6 to 9");
                if (Cringe != 40) wrong.Add("cringe read as " + Cringe + ", not 40");
                if (wrong.Count > 0) throw new Exception("A setting under the mood list was not read: " + string.Join(" | ", wrong.ToArray()));

                // And they come out from under it, above the heading, with their numbers as they were.
                Lifted();
                string[] after = CapriDesktop.Notes.ReadAll(Kept).Replace("\r\n", "\n").Split('\n');
                int heading = Array.FindIndex(after, row => row.Trim().StartsWith("##", StringComparison.Ordinal));
                if (heading < 0) throw new Exception("The lift lost the mood heading.");
                for (int at = heading; at < after.Length; at++)
                {
                    string one = after[at].Trim();
                    int mark = one.IndexOf(':');
                    if (mark > 0 && Dial(one.Substring(0, mark).Trim())) throw new Exception("\"" + one + "\" is still under the mood list.");
                }
                string head = string.Join("\n", after, 0, heading);
                foreach (string want in new[] { "probe min: 3h", "probe wait: 120", "cringe: 40", "story max: 9h", "rotate min hours: 4" })
                    if (head.IndexOf(want, StringComparison.Ordinal) < 0) throw new Exception("\"" + want + "\" is not above the mood list after the lift.");
                if (head.IndexOf("Sleepy", StringComparison.Ordinal) >= 0) throw new Exception("The lift carried a mood out with the settings.");

                // Twice over leaves it alone, and the numbers still read the same.
                string once = CapriDesktop.Notes.ReadAll(Kept);
                Lifted();
                if (CapriDesktop.Notes.ReadAll(Kept) != once) throw new Exception("The lift moved something in a file it had already lifted.");
                lock (gate) { read = DateTime.MinValue; wasLong = -1; }
                if (ProbeLow != 3 || Cringe != 40) throw new Exception("The numbers changed when the lines moved.");
                // Forget after: off at 0, under ten seconds refused as too short to be meant, and read in her
            // own time words so 2m is 120 seconds.
            Notes.Put(Kept, "# Dynamic" + Environment.NewLine + Environment.NewLine + "forget after: 0" + Environment.NewLine);
            lock (gate) { read = DateTime.MinValue; wasLong = -1; }
            if (ForgetAfter != 0) throw new Exception("Forget after did not ship off: " + ForgetAfter);
            Notes.Put(Kept, "# Dynamic" + Environment.NewLine + Environment.NewLine + "forget after: 2m" + Environment.NewLine);
            lock (gate) { read = DateTime.MinValue; wasLong = -1; }
            if (ForgetAfter != 120) throw new Exception("Forget after did not read 2m as 120 seconds: " + ForgetAfter);
            Notes.Put(Kept, "# Dynamic" + Environment.NewLine + Environment.NewLine + "forget after: 3" + Environment.NewLine);
            lock (gate) { read = DateTime.MinValue; wasLong = -1; }
            if (ForgetAfter != 10) throw new Exception("Forget after took a number too short to be meant: " + ForgetAfter);
            // F-21: dynamic.md names only the moods she can roll on the side in use.
            bool was = Brain.SenpaiOn;
            try
            {
                Brain.SenpaiOn = false;
                string censored = Settings;
                foreach (string senpai in SenpaiNames())
                    if (censored.IndexOf(senpai, StringComparison.OrdinalIgnoreCase) >= 0)
                        throw new Exception("A censored dynamic.md names " + senpai + ", which it will never roll.");
                if (censored.IndexOf("Sleepy", StringComparison.Ordinal) < 0) throw new Exception("A censored dynamic.md lost her ordinary moods too.");
                Brain.SenpaiOn = true;
                string senpaiFile = Settings;
                foreach (string senpai in SenpaiNames())
                    if (senpaiFile.IndexOf(senpai, StringComparison.OrdinalIgnoreCase) < 0)
                        throw new Exception("With the switch on, " + senpai + " is still missing from dynamic.md.");
            }
            finally { Brain.SenpaiOn = was; }
            checks.Add("PASS a censored dynamic.md names none of the four moods it will never roll, keeps her ordinary ones, and gains them once the switch is on");

            checks.Add("PASS forget after ships off, reads her time words so 2m is 120 seconds, and will not take a stretch short enough to drop a chat between two sentences");

            checks.Add("PASS a setting written under the mood list in dynamic.md is still read as a setting, which it was not: probe min, probe max, probe wait, cringe, story min and story max were read as moods and their settings fell back to stock. The lift puts them above the heading with their numbers as they were, moods untouched, and leaves a file it has already lifted alone");
            }
            finally
            {
                Kept = wasKept;
                lock (gate) { read = wasRead; wasLong = wasSize; }
                try { Directory.Delete(folder, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        // Settings lifted back above the mood list, where a fresh file puts them. Every top up here but one
        // appends at the end, which on a file with a mood list is under it, so six of them read as moods that
        // have no file. Lines move exactly as they are, so an edited number stays theirs.
        // The headings each block of settings sits under, and the first setting each one goes above. A
        // dynamic.md written before they existed has thirty one settings in one section, so every note in
        // it stacks at the top and none of them is beside what it describes. Reading it is the whole point
        // of the file, so the headings go in rather than the notes being left where they landed.
        private static readonly string[,] Blocks =
        {
            { "## Who she is underneath", "base" },
            { "## How forward she is", "forward min" },
            { "## Her questions about you", "probe min" },
            { "## Her own stories", "story min" },
            { "## How much she leans in", "cringe" },
            { "## Letting the conversation go", "forget after" },
        };

        private static void Blocked()
        {
            if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept)) return;
            try
            {
                string had = CapriDesktop.Notes.ReadAll(Kept);
                List<string> rows = new List<string>(had.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'));
                bool put = false;
                for (int one = 0; one < Blocks.GetLength(0); one++)
                {
                    string heading = Blocks[one, 0], above = Blocks[one, 1];
                    if (rows.Exists(delegate(string row) { return row.Trim().Equals(heading, StringComparison.OrdinalIgnoreCase); })) continue;
                    int at = rows.FindIndex(delegate(string row)
                    {
                        return Regex.IsMatch(row ?? "", "^[ \t]*" + Regex.Escape(above) + "[ \t]*:", RegexOptions.IgnoreCase);
                    });
                    if (at < 0) continue;
                    rows.InsertRange(at, new[] { heading, "" });
                    put = true;
                }
                if (!put) return;
                Notes.Put(Kept, string.Join(Environment.NewLine, rows.ToArray()).TrimEnd() + Environment.NewLine);
                lock (gate) { read = DateTime.MinValue; wasLong = -1; }
            }
            catch (Exception bad) { Notes.Failed("the blocking pass in Dynamics.cs", bad); }
        }

        private static void Lifted()
        {
            try
            {
                if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept)) return;
                string had = CapriDesktop.Notes.ReadAll(Kept);
                string[] rows = had.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                List<string> kept = new List<string>(), lifted = new List<string>();
                bool listing = false;
                int head = -1;
                foreach (string raw in rows)
                {
                    string one = (raw ?? "").Trim();
                    if (one.StartsWith("#", StringComparison.Ordinal))
                    {
                        listing = Regex.IsMatch(one.TrimStart('#').Trim(), @"^(attitudes|moods)\b", RegexOptions.IgnoreCase);
                        if (listing && head < 0) head = kept.Count;
                        kept.Add(raw);
                        continue;
                    }
                    int mark = one.IndexOf(':');
                    if (listing && mark > 0 && Dial(one.Substring(0, mark).Trim())) { lifted.Add(raw); continue; }
                    kept.Add(raw);
                }
                if (lifted.Count == 0 || head < 0) return;
                // A blank line between them and the heading, as the file has everywhere else.
                lifted.Add("");
                kept.InsertRange(head, lifted);
                string made = string.Join(Environment.NewLine, kept.ToArray());
                while (made.EndsWith(Environment.NewLine)) made = made.Substring(0, made.Length - Environment.NewLine.Length);
                made += Environment.NewLine;
                if (made == had) return;
                Notes.Put(Kept, made);
                lock (gate) { read = DateTime.MinValue; wasLong = -1; }
            }
            catch (Exception bad) { Notes.Failed("lifting the settings out of the mood list in Dynamics.cs", bad); }
        }

        // The intense ones ship switched off as well as gated, so they never turn up until they are asked for.
        // A mood that cannot be rolled while the switch is off has no business being named in a censored
        // file. The folder rule hides the mood; naming it at weight 0 in dynamic.md hands back what the
        // folder was hiding. Reported as F-21.
        //
        // Asked of what attached rather than read out of the mood's own file, because a censored install
        // does not have those files to read. With nothing attached no mood is gated, which is true: a build
        // with none of them has nothing to keep back.
        internal static bool SenpaiMood(string named)
        {
            if (string.IsNullOrEmpty(named)) return false;
            foreach (string one in SenpaiNames())
                if (string.Equals(one, named, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // The same, plus the one that ships switched off for its own reason rather than for this one.
        private static bool SenpaiShip(string named)
        {
            return SenpaiMood(named) || named == "Furious";
        }

        internal static string Settings
        {
            get
            {
                StringBuilder made = new StringBuilder();
                made.AppendLine("# Dynamic");
                made.AppendLine();
                made.AppendLine(">! With Dynamic picked, she changes mood on her own.");
                made.AppendLine();
                made.AppendLine("## Who she is underneath");
                made.AppendLine();
                made.AppendLine(">! base is who she is underneath. Moods shift her, they do not replace her.");
                made.AppendLine(">! Each mood lasts a random time between min and max hours.");
                made.AppendLine();
                made.AppendLine("base: Devoted");
                made.AppendLine("rotate min hours: 4");
                made.AppendLine("rotate max hours: 8");
                made.AppendLine();
                made.AppendLine("## How forward she is");
                made.AppendLine();
                made.AppendLine(">! With How forward she is set to drift, it moves between these on the same timer.");
                made.AppendLine();
                made.AppendLine("forward min: 2");
                made.AppendLine("forward max: 8");
                made.AppendLine();
                made.AppendLine("## Her questions about you");
                made.AppendLine();
                made.AppendLine(">! How long between her questions about you. Takes 2h, 90m, 30s or 1h30m.");
                made.AppendLine(">! probe wait is how long her bubble stays up waiting for the answer, in seconds.");
                made.AppendLine();
                made.AppendLine("probe min: 1h");
                made.AppendLine("probe max: 2h");
                made.AppendLine("probe wait: 90");
                made.AppendLine();
                made.AppendLine("## Her own stories");
                made.AppendLine();
                made.AppendLine(">! How long between her own stories, as a window so the gap varies. Takes 4h, 90m or 1h30m.");
                made.AppendLine(">! She tells one line of a piece at a time and finishes it before starting another.");
                made.AppendLine(">! story min: 0 stops her telling them at all.");
                made.AppendLine();
                made.AppendLine("story min: 4h");
                made.AppendLine("story max: 8h");
                made.AppendLine();
                made.AppendLine("## How much she leans in");
                made.AppendLine();
                made.AppendLine(">! How much she leans into a bit. 0 is dry, 100 is unbearable, over all of them at once.");
                made.AppendLine();
                made.AppendLine("cringe: 0");
                made.AppendLine();
                // Written here, with its note, rather than left to the top-up. The top-up adds it at the end of
                // the file, where the note has no setting under it and the next read drops it as a stray.
                made.AppendLine("## Letting the conversation go");
                made.AppendLine();
                made.AppendLine(">! How long with nothing said before she lets the conversation go and starts fresh. Takes 90s, 5m or 1h30m.");
                made.AppendLine(">! 0 keeps it until you clear her yourself, which is how she ships.");
                made.AppendLine();
                made.AppendLine("forget after: 0");
                made.AppendLine();
                // In the file she ships, so a refresh of her notes knows this section's and keeps them. Its lines,
                // one per personality on the side in use, are put in by PaceTopUp.
                foreach (string row in PaceNotes) made.AppendLine(row);
                made.AppendLine();
                made.AppendLine("## Attitudes she can have");
                made.AppendLine();
                made.AppendLine(">! One a line, with its chance of turning up, 0 to 100.");
                made.AppendLine(">! 0 never. 100 always. Each rolls separately and one winner is picked.");
                made.AppendLine(">! No winner means no mood that round, just the base personality.");
                made.AppendLine(">! Each needs a file of the same name in the attitudes folder.");
                made.AppendLine();
                for (int mood = 0; mood < Moods.GetLength(0); mood++)
                {
                    if (!Brain.SenpaiLive && SenpaiMood(Moods[mood, 0])) continue;
                    made.AppendLine(Moods[mood, 0] + ": " + (SenpaiShip(Moods[mood, 0]) ? 0 : Often));
                }
                // The ones that are not in the table, from whatever attached. The top up had this and a
                // fresh file did not, so a file written from scratch never listed them at all.
                if (Brain.SenpaiLive)
                    foreach (string named in SenpaiNames())
                        made.AppendLine(named + ": " + (OffByDefault(named) ? 0 : Often));
                return made.ToString();
            }
        }

        // Each mood is a personality file with the same headings, but its What she likes numbers are added to her
        // personality's rather than replacing them. So "called on: -3" makes attention unwelcome whoever she is, and a
        // pack that likes being thrown still ends up liking it because her own +3 is still underneath.
        // Every mood the build ships, for the sweep that looks for the senpai half of one in a censored
        // install. Her packs expose theirs the same way.
        // The names this build carries, for the pack writer. Names only, no text.
        internal static string[] ShippedNames()
        {
            string[] found = new string[Moods.GetLength(0)];
            for (int mood = 0; mood < Moods.GetLength(0); mood++) found[mood] = Moods[mood, 0];
            return found;
        }

        internal static List<string> Shipped()
        {
            List<string> found = new List<string>();
            for (int mood = 0; mood < Moods.GetLength(0); mood++) found.Add(Moods[mood, 1]);
            // The moods that are not in the table, and the senpai half of the ones that are, from whatever
            // attached. The sweep looks through this, so anything missing here is something it cannot find.
            foreach (string named in SenpaiNames())
            {
                string text = MoodText(named);
                if (!string.IsNullOrEmpty(text)) found.Add(text);
            }
            if (SenpaiPartFor != null)
                for (int mood = 0; mood < Moods.GetLength(0); mood++)
                {
                    string part = null;
                    try { part = Legacy.Modern(SenpaiPartFor(Moods[mood, 0])); }
                    catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                    if (!string.IsNullOrEmpty(part)) found.Add(part);
                }
            return found;
        }

        // The moods that are not in the public build. Something attaches them before her moods are
        // written; with nothing attached she has the ones below and knows of no others.
        internal static Func<string, string> SenpaiMoodFor;

        // The names of the moods that attached, so Install can write them. Empty when nothing attached,
        // which is the public build.
        internal static Func<string[]> SenpaiNamesFor;

        // Moods that ship switched off rather than at her usual odds. One attached mood closes the app when
        // it runs, so it is opt in.
        internal static Func<string, bool> OffByDefaultFor;

        // Retune rows for moods this build does not have, so a file somebody already has is still corrected.
        internal static Func<string[,]> SenpaiRetunedFor;

        private static bool OffByDefault(string named)
        {
            if (OffByDefaultFor == null) return false;
            try { return OffByDefaultFor(named); }
            catch (Exception) { return false; }
        }

        internal static string[] SenpaiNames()
        {
            if (SenpaiNamesFor == null) return new string[0];
            try { return SenpaiNamesFor() ?? new string[0]; }
            catch (Exception) { return new string[0]; }
        }

        // The sections of a mood that ships which are not in the public build. Something attaches them
        // before her moods are written and they are appended to that mood's text; with nothing attached a
        // mood is only its plain half, which is what a censored install had anyway.
        internal static Func<string, string> SenpaiPartFor;

        private static readonly string[,] Moods =
        {
            {
                "Sleepy",
                @"# Sleepy

>! Heavy eyed and slow. Being called on is an imposition; being left alone is bliss.
>! Numbers here are added to her personality's, not instead of them.

## What she likes

called on: -3
ignored: +3
petted: 0
thrown: -2
praised: 0

## How she reacts

flirt: -2
sulks at upset: 4
sulk minutes: 10

## Drifts to

>! Keep poking her and she gets properly short with you.

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

>! Wired. Wants everything at once and does not want to be left alone.
>! Numbers here are added to her personality's, not instead of them.

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

>! Left alone with all that energy, she gets snippy.

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

>! Short tempered and easily set off. Nothing is quite right today.
>! Numbers here are added to her personality's, not instead of them.

## What she likes

called on: -1
ignored: -1
thrown: -2
insulted: -2
petted: -1
praised: +2

## How she reacts

ignored dodge: 50
ignored either: yes
sulks after: 2
sulks at upset: 3
sulk minutes: 9
sulk alone: 420

## Drifts to

>! Enough kindness and she comes out of it.

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

## When you ignore her

Don't look at me. No, look at me.
I don't need you. I don't. Probably.
Leave me alone. Wait, don't.

## When you come back

Oh. It is you.
Mm.
"
            },
            {
                "Clingy",
                @"# Clingy

>! Does not want to be put down. Attention is the whole point today.
>! Numbers here are added to her personality's, not instead of them.

## What she likes

called on: +3
ignored: -4
petted: +2
praised: +2
thrown: -1

## How she reacts

ignored dodge: 80
sulks at upset: 4
sulk minutes: 2
sulk alone: 60
dodges: no

## Drifts to

>! Ignored for long enough and the neediness curdles.

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

## When you ignore her

Don't go quiet on me.
I'm right here. Right here.
You're not getting rid of me that easily.

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
            { "Needy", @"# Needy

>! Wants reassurance, and asks for it.
>! Numbers here are added to her personality's, not instead of them.

## What she likes

called on: +2
ignored: -4
petted: +2
praised: +3
thrown: -2

## How she reacts

ignored dodge: 100
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

## When you ignore her

Are you ignoring me? Please don't.
Just one look. That's all I want.
I'll be quiet. Just don't leave me here.
Did you forget about me?

## When you come back

You came back. You came back.
I thought you'd forgotten.
Say you missed me.
" },
            { "Playful", @"# Playful

>! Everything is a game and she wants in.
>! Numbers here are added to her personality's, not instead of them.


## When she wants attention

>! Said when she wants you, with the toggle off as much as on. Keep these plainly childlike rather
>! than anything else: this pool ships to everyone.

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
petted: +1
ignored: -2

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
" },
            {
                "Touchy",
                @"# Touchy

>! Does not want to be handled today.
>! Numbers here are added to her personality's, not instead of them.

## What she likes

called on: -1
thrown: -3
petted: -3
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
            { "Affectionate", @"# Affectionate

>! Soft and close, wants to be near you.
>! Numbers here are added to her personality's, not instead of them.

## What she likes

called on: +1
petted: +2
praised: +2
ignored: -2
thrown: -1

## How she reacts

ignored dodge: 50
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

## When you ignore her

I miss you, and you're right there.
One little look would do it.
I'll keep the warm spot for you.

## When you come back

There you are. Come sit.
Missed you. Properly.
" },
            { "Mischievous", @"# Mischievous

>! Looking for trouble and hoping you join in.
>! Numbers here are added to her personality's, not instead of them.

## What she likes

called on: +2
thrown: +2
petted: 0
ignored: -1
insulted: +1

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
" },
            {
                "Focused",
                @"# Focused

>! Deep in something and does not want interrupting.
>! Numbers here are added to her personality's, not instead of them.

## What she likes

called on: -3
ignored: +2
petted: -2
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
            { "Melancholy", @"# Melancholy

>! Low and quiet. Wants company, not noise.
>! Numbers here are added to her personality's, not instead of them.

## What she likes

called on: 0
ignored: -2
petted: +1
praised: +2
thrown: -3
insulted: -3

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
" },
            
            
            
            
            {
                "Furious",
                @"# Furious

>! Past angry. She wants to make a mess of your desktop about it.
>! Numbers here are added to her personality's, not instead of them.
>! Pranks only happen if Mood pranks is switched on in the Personality menu.

## What she likes

thrown: -4
insulted: -4
ignored: -3
petted: -3
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

>! A proper apology or enough kindness brings her down.

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

>! She does not like you talking to other AIs, and she will say so.
>! Numbers here are added to her personality's, not instead of them.

## What she likes

called on: +3
ignored: -4
praised: +2
petted: +1
thrown: -2

## She switches on

>! Settings she turns on while she is this way, and off again when she is not.

jealous: yes

## How she reacts

ignored dodge: 70
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

## When you ignore her

Who's got your attention now?
Whatever it is, I hope it's worth it.
Fine. Go look at them.

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

>! Thoroughly pleased with herself. Being needed is delicious.
>! Numbers here are added to her personality's, not instead of them.

## What she likes

called on: +2
praised: +2
petted: +1
ignored: -2
insulted: -1
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
