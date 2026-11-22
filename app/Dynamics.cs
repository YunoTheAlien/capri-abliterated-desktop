using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // Dynamic: she picks her own mood on a timer. A mood shifts what she wants rather than who she is, its weights
    // added to her personality's. dynamic.md sets the personality underneath, timing and the moods in play.
    internal static class Dynamics
    {
        internal const string FileName = "dynamic.md";
        internal const string FolderName = "attitudes";
        internal static string Kept;

        // personalSettings\attitudes, split like her personalities: censored\ holds a mood's plain sections and senpai\
        // its senpai ones under the same name, so a censored install doesn't contain that text.
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
                try { foreach (string file in Directory.GetFiles(folder)) if (Notes.IsPack(file)) found.Add(file); }
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
        private static bool overNineThousand;
        // How often, out of 100, a line is her mood's own rather than her personality's when her brain has not written the
        // two together; the mood's openers and tails go round her personality's lines as often.
        private static int moodShare = 50;
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
        // A length from what they wrote: 2, 2h, 90m, 30s or 1h30m; false for anything unreadable, so a typo keeps the
        // default. bare is what a plain number means, in hours.
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
        // The cringe dial set past 9000: 100 is the top of the scale, and this is past it.
        internal static bool OverNineThousand { get { Fresh(); lock (gate) return overNineThousand; } }
        internal static int MoodShare { get { Fresh(); lock (gate) return moodShare; } }
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
        // Her drifting forwardness moved up by steps (a gift, a few feeds), never past the ceiling in dynamic.md.
        internal static void NudgeForward(int steps)
        {
            int now = SpecialInterestDrift;
            lock (gate)
            {
                int low = Math.Max(0, Math.Min(10, specialInterestLow)), high = Math.Max(0, Math.Min(10, specialInterestHigh));
                if (high < low) { int swap = low; low = high; high = swap; }
                specialInterestNow = Math.Max(low, Math.Min(high, now + steps));
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

        // How slowly each personality's own timers run, from its line here: 1 as its pack has them, 2 twice as long.
        // One list so they can be set against each other; no line takes its stock pace, or 1.
        internal const string PaceHeading = "## How long each personality takes";
        private static readonly Dictionary<string, double> paces = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        // The heading and its notes, one copy for the file she ships and the top up.
        private static readonly string[] PaceNotes =
        {
            PaceHeading, "",
            ">! How long each personality takes to feel ignored, and to get over a sulk alone.",
            ">! 1 is as her pack has them, 2 twice as long, 0.5 half. From 0.25 to 10."
        };

        // Stock paces for the personalities the public build names. The others' come from the side that has them.
        private static readonly Dictionary<string, double> stockPaces = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            { "Caregiver", 2 }, { "Welcome Home", 1.5 }, { "Sassy", 2 }, { "Feral", 1.5 }, { "Emo", 1 }, { "Bratty", 1 }, { "Devoted", 1 }
        };

        internal static void StockPace(string named, double pace) { lock (gate) stockPaces[named] = pace; }

        // Every stock pace as name and number, for the codex to carry.
        internal static string[,] StockPaces()
        {
            lock (gate)
            {
                string[,] rows = new string[stockPaces.Count, 2];
                int at = 0;
                foreach (KeyValuePair<string, double> one in stockPaces)
                {
                    rows[at, 0] = one.Key;
                    rows[at, 1] = one.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    at++;
                }
                return rows;
            }
        }

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


        // One half of a mood, written once if missing and brought up to date if not, with this machine's line ends, as
        // her files are read back, so a top up never sees it as changed.
        private static void Keep(string file, string text, string whole)
        {
            if (file == null || string.IsNullOrEmpty(text)) return;
            try
            {
                if (!File.Exists(file))
                {
                    string fresh = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", Environment.NewLine);
                    File.WriteAllText(file, fresh, new UTF8Encoding(false));
                    Renew.Wrote(file, fresh);
                    return;
                }
                // On an update the mood is written new, theirs kept as a .bak when they had changed it. In between it is
                // theirs as it is.
                if (Renew.Replace(file, text)) return;
                Renew.Adopt(file, text);
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
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

        // The file she reads for a mood: the senpai folder's whole copy while the switch is on and she is off air, the
        // censored one otherwise, never one on top of the other.
        internal static string PathOf(string named)
        {
            string path = Named(Plain, named);
            try
            {
                if (Brain.SenpaiLive)
                {
                    string senpai = Slots.Find(Senpais, named);
                    if (senpai != null) return senpai;
                }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            return path;
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

        // How much more or less likely a mood is in the draw, from whatever leans it (Tilt.cs); 1 with nothing attached.
        internal static Func<string, double> Weigh;

        // A mood's chance out of 100 after anything leaning the draw: 0 stays never, and 100 or more is always in the pool.
        internal static double Odds(int howOften, string named)
        {
            if (howOften <= 0) return 0;
            double weight = 1;
            if (Weigh != null) { try { weight = Weigh(named); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); } }
            return howOften * weight;
        }

        // force is an explicit "pick one now" from the menu. Without it the timer runs on Dynamic and on any
        // personality whose mood is Dynamic; a mood held by hand, or none on a fixed personality, stays until changed
        // by hand.
        internal static void Turn(bool force)
        {
            Tilt.Tick();
            if (!force && Held && Personalities.Chosen != Personalities.Dynamic) { lock (gate) until = DateTime.MinValue; return; }
            DateTime now = DateTime.UtcNow;
            lock (gate) { if (now < until) return; }

            // Every mood rolls its own chance, and the ones that come up form a pool, from which one wins at random. 0
            // never comes up; 100 is always in the pool, pinning her in that mood until something sours it.
            List<string> have = Allowed;
            string was = Personalities.Attitude;
            // A round with nobody in the draw rolls again: each mood is meant to hold for hours, so a round of no
            // mood at all would leave her moodless that long. Only every chance at 0 means no mood.
            List<string> won = new List<string>();
            for (int round = 0; round < 1000 && won.Count == 0; round++)
            {
                bool any = false;
                foreach (string one in have)
                {
                    double howOften = Odds(Chance(one), one);
                    if (howOften <= 0) continue;
                    any = true;
                    if (howOften >= 100 || dice.NextDouble() * 100 < howOften) won.Add(one);
                }
                if (!any) break;
            }

            // Any winner, the one she is in included: a roll with nothing pushing it is a chance, and landing on the same mood
            // is allowed.
            string next = won.Count > 0 ? won[dice.Next(won.Count)] : "";

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
        // When the mood runs out, which mood it is and whether it was set by hand are kept, so a restart doesn't reroll
        // it. Not in mood.txt, which stays one word for whatever follows her mood.
        internal static string MoodKept;

        // A fourth line kept beside the mood for whatever else runs on her mood's clock (Tilt.cs), written and read back.
        internal static Func<string> MoreKept;
        internal static Action<string> MoreRecalled;

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
                    + (Held ? "held" : "rolled") + Environment.NewLine
                    + (MoreKept == null ? "" : MoreKept() ?? "") + Environment.NewLine, new UTF8Encoding(false));
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
                // Its own deadline, whether or not the mood's has passed.
                if (rows.Length > 3 && MoreRecalled != null) { try { MoreRecalled(rows[3]); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); } }
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

        // Mood: Dynamic rolls on the timer on a fixed personality too, and a mood held by hand there stays put. Driven: the
        // deadline is set and passed by hand rather than waited for.
        internal static void Rolls(List<string> checks)
        {
            string wasKept = MoodKept, wasMood = Personalities.Attitude, wasChosen = Personalities.Chosen;
            bool wasHeld = Held;
            DateTime wasUntil;
            lock (gate) wasUntil = until;
            try
            {
                MoodKept = null;
                List<string> have = Allowed;
                if (have.Count < 2) { checks.Add("PASS a fixed personality's dynamic mood: fewer than two moods here, nothing to roll"); return; }
                Personalities.Chosen = "Fixedly";
                Held = false;
                Personalities.Attitude = "";
                lock (gate) until = DateTime.MinValue;
                Turn();
                string first = Personalities.Attitude;
                DateTime ends;
                lock (gate) ends = until;
                if (first.Length == 0 || ends <= DateTime.UtcNow) throw new Exception("Mood: Dynamic on a fixed personality didn't roll a mood on the timer.");
                Turn();
                if (Personalities.Attitude != first) throw new Exception("A rolled mood changed before its time was up.");
                lock (gate) until = DateTime.UtcNow.AddMinutes(-1);
                Turn();
                lock (gate) ends = until;
                if (Personalities.Attitude.Length == 0 || ends <= DateTime.UtcNow) throw new Exception("A fixed personality's dynamic mood didn't roll again when its time was up.");
                // Every roll lands on a mood while any mood has a chance: a round with no winner rolls again.
                for (int roll = 0; roll < 200; roll++)
                {
                    lock (gate) until = DateTime.MinValue;
                    Turn();
                    if (Personalities.Attitude.Length == 0) throw new Exception("A roll came out with no mood, on roll " + roll + ".");
                }

                // Two moods in every draw: the one she is in comes up again some of the time, and the other some of the time.
                List<string> pair = Allowed.FindAll(one => Chance(one) > 0);
                if (pair.Count >= 2)
                {
                    Func<string, double> weighWas = Weigh;
                    string a = pair[0], b = pair[1];
                    int same = 0;
                    try
                    {
                        Weigh = delegate(string named) { return named == a || named == b ? 1000 : 0; };
                        Personalities.Attitude = a;
                        for (int roll = 0; roll < 200; roll++)
                        {
                            string before = Personalities.Attitude;
                            lock (gate) until = DateTime.MinValue;
                            Turn();
                            if (Personalities.Attitude == before) same++;
                        }
                    }
                    finally { Weigh = weighWas; }
                    if (same == 0 || same == 200) throw new Exception("With two moods in every draw, she landed on the same one " + same + " times in 200: a roll should allow the same mood, not force it.");
                }

                // Held by hand, a mood or no mood, it stays.
                Held = true;
                Personalities.Attitude = "Playful";
                lock (gate) until = DateTime.UtcNow.AddMinutes(-1);
                Turn();
                if (Personalities.Attitude != "Playful") throw new Exception("A held mood on a fixed personality was rolled away: " + Personalities.Attitude);
                Personalities.Attitude = "";
                Turn();
                if (Personalities.Attitude != "") throw new Exception("No mood held on a fixed personality was rolled into " + Personalities.Attitude);
                checks.Add("PASS Mood: Dynamic rolls on the timer on a fixed personality from a fresh start, holds for its span and rolls again when it runs out, and 200 rolls all land on a mood; a mood held by hand, or no mood, stays");
            }
            finally
            {
                MoodKept = wasKept;
                Personalities.Attitude = wasMood;
                Personalities.Chosen = wasChosen;
                Held = wasHeld;
                lock (gate) until = wasUntil;
            }
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

        // Picked by hand: her mood stays as it is and its clock starts again from now, since it was chosen.
        internal static void Restarted()
        {
            double least = Least, most = Most;
            if (most < least) { double swap = least; least = most; most = swap; }
            double hours = least + new Random().NextDouble() * Math.Max(0.0, most - least);
            lock (gate) until = DateTime.UtcNow.AddHours(Math.Max(0.05, hours));
            Brain.Log("mood", -1, "Her mood's clock started again on a pick, for " + hours.ToString("0.#") + "h.");
            Remember();
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
            @"^(base|personality|(rotate )?(min|max)( hours)?|probe (min|max|wait)|story (min|max)|forget after|forward (min|max)|cringe|mood share)$",
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
                int low = 2, high = 8, awful = cringe, share = 50;
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
                    // A name this file knows as a setting is a setting wherever it sits, even below the mood list.
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
                        else if (Regex.IsMatch(key, @"^mood share$", RegexOptions.IgnoreCase)
                            && double.TryParse(val.TrimEnd('%', ' '), out much)) share = (int)much;
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
                    overNineThousand = awful > 9000;
                    moodShare = Math.Max(0, Math.Min(100, share));
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

        // Her window recomputes her voice when the mood changes, since a mood can shift it too.
        internal static Action MoodChanged;

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
                    if (!Notes.IsPack(plainFile)) continue;
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
            // Each step logs its own failure, so the retune can't be skipped in silence by something else going wrong.
            catch (Exception bad)
            {
                try { Brain.Log("moods", -1, "Her moods could not be installed: " + bad.GetType().Name + ": " + bad.Message); }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
            // After the files exist, since this changes what is in them rather than putting them there, and
            // outside the catch above, because a number that has since changed must still be fixed in a file
            // that is already on disk whatever else failed.

        }

        // Her mood list names what is installed: a mood with a file and no line gets one, and a senpai mood leaves it
        // with the switch off. Their lines and numbers are never touched; the settings above are written new on each
        // version.
        internal static void TopUpNow() { TopUp(); }

        private static void TopUp()
        {
            try
            {
                if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept) || string.IsNullOrEmpty(Folder)) return;
                Notes.Refresh(Kept, Settings);
                string had = CapriDesktop.Notes.ReadAll(Kept);
                bool hasList = Regex.IsMatch(had, @"(?mi)^#+\s*(attitudes|moods)");
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
                if (adding.Length == 0) return;
                if (!had.EndsWith(Environment.NewLine)) had += Environment.NewLine;
                if (!hasList) had += Environment.NewLine + "## Attitudes she can have" + Environment.NewLine + Environment.NewLine;
                Notes.Put(Kept, had + adding);
                lock (gate) { read = DateTime.MinValue; wasLong = -1; }
            }
            catch (Exception bad) { Notes.Failed("a top up in Dynamics.cs", bad); }
        }

        // That a setting under the mood list is still read as a setting.
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
            // Cringe at 100 is the top tier; past 9000 is one more.
            Notes.Put(Kept, "# Dynamic" + Environment.NewLine + Environment.NewLine + "cringe: 100" + Environment.NewLine);
            lock (gate) { read = DateTime.MinValue; wasLong = -1; }
            string top = Brain.CringeLine() ?? "";
            if (Cringe != 100 || OverNineThousand || top.IndexOf("nine thousand", StringComparison.OrdinalIgnoreCase) >= 0
                || (Personalities.Cringe >= 0 && !top.StartsWith("You are unbearable on purpose", StringComparison.Ordinal)))
                throw new Exception("Cringe at 100 wasn't the unbearable line: " + top);
            Notes.Put(Kept, "# Dynamic" + Environment.NewLine + Environment.NewLine + "cringe: 9001" + Environment.NewLine);
            lock (gate) { read = DateTime.MinValue; wasLong = -1; }
            string past = Brain.CringeLine() ?? "";
            if (Cringe != 100 || !OverNineThousand || past.IndexOf("nine thousand", StringComparison.OrdinalIgnoreCase) < 0) throw new Exception("Cringe at 9001 wasn't past the top: " + past);
            int chancePast = Sayings.FunnyChance;
            double restPast = Sayings.FunnyRest;
            Notes.Put(Kept, "# Dynamic" + Environment.NewLine + Environment.NewLine + "cringe: 100" + Environment.NewLine);
            lock (gate) { read = DateTime.MinValue; wasLong = -1; }
            int topDial = Math.Max(0, Math.Min(100, 100 + Personalities.Cringe)), topWant = topDial <= 40 ? 3 : 3 + (topDial - 40) * 7 / 60;
            int chanceTop = Sayings.FunnyChance;
            double restTop = Sayings.FunnyRest;
            Notes.Put(Kept, "# Dynamic" + Environment.NewLine + Environment.NewLine + "cringe: 0" + Environment.NewLine);
            lock (gate) { read = DateTime.MinValue; wasLong = -1; }
            int chanceNone = Sayings.FunnyChance;
            if (chancePast != 20 || Math.Abs(restPast - 1 / 3.0) > 0.01 || chanceTop != topWant || topDial == 100 && Math.Abs(restTop - 0.5) > 0.01 || chanceNone < 3)
                throw new Exception("The cringe dial didn't set how often a funny line comes: past 9000 " + chancePast + "% every " + restPast + " h, at 100 " + chanceTop + "% every " + restTop + " h, at 0 " + chanceNone + "%");
            checks.Add("PASS cringe at 100 is the unbearable line and past 9000 one more past it, with the dial still 100 for everything else; a funny line comes 3 in 100 up to 40, 10 at 100 at most every half hour, 20 past 9000 at most every 20 minutes");
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

            checks.Add("PASS a setting written under the mood list in dynamic.md is still read as a setting, which it was not: probe min, probe max, probe wait, cringe, story min and story max were read as moods and their settings fell back to stock.");
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

        // A mood that can't be rolled with the switch off isn't named in a censored file. Asked of what is attached,
        // since a censored install has no file to read; with nothing attached no mood is gated.
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
                made.AppendLine(">! How much she leans into a bit, over all of them at once: 0-100, 0 is dry, but over 9000 will make it unbearable.");
                made.AppendLine();
                made.AppendLine("cringe: 0");
                made.AppendLine();
                made.AppendLine("## Her mood in her lines");
                made.AppendLine();
                made.AppendLine(">! How often, 0 to 100, she says her mood's own line instead of her personality's.");
                made.AppendLine(">! The mood's openers and tails go round her lines as often. Used until her brain blends the two.");
                made.AppendLine();
                made.AppendLine("mood share: 50");
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
                made.AppendLine(">! One mood a line, with its chance from 0 to 100.");
                made.AppendLine(">! Each rolls a d100 and is in the draw on its chance or under. One is drawn from those.");
                made.AppendLine(">! None in the draw rolls again. Only every chance at 0 means no mood.");
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

        // A mood's numbers are added to her personality's rather than replacing them. Every mood the build ships, for
        // the censored-install sweep, and the names it carries, for the pack writer.
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
touched shoulders: +1
touched middle: 0
touched legs: 0
touched feet: -1
tickled: -2

## How she reacts

flirt: -2
sulks at upset: 4
sulk minutes: 10
kind returns: 0.00
mean scale: -0.17
drift push: -0.21

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

## Openers

>! Said before her usual lines while she is this way.

*yawns*
Mmh.
Huh? Oh.
Sorry, drifting.
Mm-hm.
Wha-

## Tails

>! Said after them.

five more minutes.
if I stay awake for it.
or I'll nap on it.
probably.
don't make me get up.
*yawn*.
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
touched shoulders: +1
touched middle: +1
touched legs: 0
touched feet: 0
tickled: +2

## How she reacts

sulks at upset: 9
sulk minutes: 2
sulk alone: 90
kind returns: -0.08
mean scale: -0.21
drift push: +0.13

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

## Openers

>! Said before her usual lines while she is this way.

Ooh!
Okay okay okay.
Wait wait.
Ha!
Yes!
Right, quick!

## Tails

>! Said after them.

and then something else!
let's go again!
faster next time!
I can't sit still.
what's next?
again, again!
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
touched shoulders: -1
touched middle: -2
touched legs: -1
touched feet: -1
tickled: -2

## How she reacts

ignored dodge: 50
ignored either: yes
sulks after: 2
sulks at upset: 3
sulk minutes: 9
sulk alone: 420
kind returns: +0.19
mean scale: +0.28
drift push: +0.18

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

## Openers

>! Said before her usual lines while she is this way.

Ugh.
Fine.
What.
Hmph.
Look.
Great.

## Tails

>! Said after them.

not that it matters today.
whatever.
don't push it.
I'm not in the mood.
today of all days.
just so you know.
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
touched shoulders: +2
touched middle: +1
touched legs: +1
touched feet: 0
tickled: +1

## How she reacts

ignored dodge: 80
sulks at upset: 4
sulk minutes: 2
sulk alone: 60
dodges: no
kind returns: -0.37
mean scale: -0.42
drift push: -0.14

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

## Openers

>! Said before her usual lines while she is this way.

Don't go.
Stay?
Oh, you're here.
Wait, wait.
Hey, hey.
Mm, close.

## Tails

>! Said after them.

don't leave yet.
stay a bit longer.
you're not going anywhere, right?
just a little longer.
promise you'll come back.
I missed you already.
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
touched shoulders: +2
touched middle: +1
touched legs: 0
touched feet: 0
tickled: +1

## How she reacts

ignored dodge: 100
sulks at upset: 3
sulk minutes: 3
sulk alone: 60
dodges: no
kind returns: -0.46
mean scale: -0.33
drift push: -0.12

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

## Openers

>! Said before her usual lines while she is this way.

Hey, um.
Be honest.
Did I do okay?
Tell me.
Oh, good.
Um.

## Tails

>! Said after them.

you'd tell me if I was annoying, right?
is that okay?
you still like me, yeah?
say something nice.
I just need to hear it.
right?
" },
            { "Playful", @"# Playful

>! Everything is a game and she wants in.
>! Numbers here are added to her personality's, not instead of them.


## When she wants attention

>! Said when she wants you. Keep these plainly childlike.

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
touched shoulders: +1
touched middle: 0
touched legs: 0
touched feet: +1
tickled: +2

## How she reacts

sulks after: 0
sulks at upset: 10
sulk minutes: 2
dodges: no
kind returns: -0.07
mean scale: -0.18
drift push: +0.06

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

## Openers

>! Said before her usual lines while she is this way.

Tag!
Hehe.
Ooh, a game.
Okay, my turn.
Ha, gotcha.
Bet you can't keep up.

## Tails

>! Said after them.

your move.
catch me if you can.
best of three?
I win, obviously.
let's play.
no takebacks.
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
touched shoulders: -2
touched middle: -3
touched legs: -2
touched feet: -2
tickled: -3

## How she reacts

sulks after: 1
sulks at upset: 2
sulk minutes: 8
sulk alone: 300
dodges: yes
kind returns: +0.21
mean scale: +0.39
drift push: +0.22

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

## Openers

>! Said before her usual lines while she is this way.

Hey.
Careful.
Hands.
Back up.
Easy.
Not now.

## Tails

>! Said after them.

and don't touch.
no poking today.
give me some room.
personal space, please.
look, don't touch.
I mean it.
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
touched shoulders: +2
touched middle: +1
touched legs: +1
touched feet: 0
tickled: +1

## How she reacts

ignored dodge: 50
sulks at upset: 5
sulk minutes: 2
sulk alone: 90
dodges: no
kind returns: -0.18
mean scale: -0.26
drift push: -0.08

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

## Openers

>! Said before her usual lines while she is this way.

Aw.
Mm.
Hey, you.
Oh, there you are.
Come here.
Hi.

## Tails

>! Said after them.

you're sweet.
I like having you around.
stay close.
that's nice.
I'm glad it's you.
all warm and fuzzy.
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
touched shoulders: 0
touched middle: 0
touched legs: -1
touched feet: +1
tickled: +2

## How she reacts

sulks after: 0
sulks at upset: 9
sulk minutes: 2
dodges: no
kind returns: +0.03
mean scale: -0.09
drift push: +0.08

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

## Openers

>! Said before her usual lines while she is this way.

Heh.
Ooh, idea.
Don't look at me.
Psst.
Nothing to see here.
Oops.

## Tails

>! Said after them.

I may have done a thing.
you didn't see anything.
want to cause some trouble?
it wasn't me.
this'll be fun.
trust me.
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
touched shoulders: 0
touched middle: -1
touched legs: -1
touched feet: -1
tickled: -2

## How she reacts

flirt: -3
sulks after: 2
sulks at upset: 4
sulk minutes: 6
dodges: yes
kind returns: +0.04
mean scale: -0.06
drift push: -0.26

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

## Openers

>! Said before her usual lines while she is this way.

One sec.
Hold on.
Mm, busy.
Right.
Hang on.
In the zone, hang on.

## Tails

>! Said after them.

back to it.
where was I?
let me finish this.
quickly, I'm working.
don't break my focus.
nearly there.
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
touched shoulders: +1
touched middle: 0
touched legs: 0
touched feet: 0
tickled: -1

## How she reacts

flirt: -2
sulks after: 2
sulks at upset: 3
sulk minutes: 6
sulk alone: 240
dodges: no
kind returns: -0.16
mean scale: +0.23
drift push: +0.11

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

## Openers

>! Said before her usual lines while she is this way.

Oh.
Hm.
Yeah.
I guess.
Hey.
Mm, okay.

## Tails

>! Said after them.

it's been one of those days.
stay a while?
quietly, if that's okay.
sorry, I'm a bit low.
thanks for being here.
it'll pass.
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
touched shoulders: -2
touched middle: -3
touched legs: -2
touched feet: -2
tickled: -3

## How she reacts

sulks after: 1
sulks at upset: 2
sulk minutes: 12
sulk alone: 600
dodges: yes
pranks: yes
prank minutes: 6
kind returns: +0.38
mean scale: +0.47
drift push: +0.35

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

## Openers

>! Said before her usual lines while she is this way.

Oh, really?
Unbelievable.
Excuse me?
Seriously.
Right, that's it.
Wow.

## Tails

>! Said after them.

and I'm still mad.
don't test me.
I'm keeping score.
watch your desktop.
you're on thin ice.
not forgiven.
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
touched shoulders: +1
touched middle: +1
touched legs: 0
touched feet: 0
tickled: 0

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
kind returns: -0.27
mean scale: -0.14
drift push: +0.16

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

## Openers

>! Said before her usual lines while she is this way.

Oh, so now I'm interesting?
Hm, finally.
Remember me?
Look who's back.
Hmph.
Oh, it's me now?

## Tails

>! Said after them.

not some other AI.
I'm the one here, you know.
who were you talking to?
don't go wandering off.
I saw that.
just me, okay?
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
touched shoulders: +1
touched middle: -1
touched legs: -1
touched feet: -1
tickled: 0

## How she reacts

sulks at upset: 8
sulk minutes: 3
kind returns: +0.17
mean scale: -0.11
drift push: -0.09

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

## Openers

>! Said before her usual lines while she is this way.

Naturally.
Of course.
As expected.
Heh, see?
Told you.
Obviously.

## Tails

>! Said after them.

you're welcome.
I'm just that good.
admit it, you need me.
try to keep up.
as always.
no applause needed.
"
            }
        };
    }
}
