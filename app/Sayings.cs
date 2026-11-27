using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // Where the variety comes from: a line is [opener] + [core] + [tail] ("Oh-" "put me down" ", choom."), cores per
    // act and sentiment, openers and tails shared. Shuffle bags mean 110 fragments give tens of thousands of lines,
    // none repeating early.
    internal static class Sayings
    {
        internal const string FileName = "lines.md";
        internal static string Kept;

        private static readonly object gate = new object();
        private static readonly Random dice = new Random();
        private static readonly Dictionary<string, string[]> pools = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, List<int>> bags = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        private static DateTime read = DateTime.MinValue;
        private static long wasLong = -1;

        internal static WrittenNote Open() { Write(false); return new WrittenNote(Kept, Text, gate); }

        // One line for this act: her personality's core or ours, with an opener and tail often enough to stay fresh.
        // Finished lines are remembered with their time and refused while still recent, across restarts.
        private const int RememberMost = 6000;
        // Keyed by a 64-bit fingerprint of the line, not the line: the same text always gives the same number, so an
        // edited file needs no remapping, and the store is a twelfth of the size. Case does not count, as before.
        private static readonly Dictionary<ulong, DateTime> saidAt = new Dictionary<ulong, DateTime>();

        // FNV-1a over the line in upper case.
        internal static ulong Print(string line)
        {
            ulong made = 14695981039346656037UL;
            foreach (char one in line.ToUpperInvariant()) { made ^= one; made *= 1099511628211UL; }
            return made;
        }
        private static string heardPath;
        private static bool loadedSaid;

        internal static double RecentHours = 24;

        // A log of the lines she builds, with who she was and her mood for each, for looking back when a line landed
        // wrong. Nothing reads it back; her lines only, through her logs' scrub.
        private static string logPath;
        internal static Func<string> Who = delegate { return ""; };
        internal static Func<string> Feeling = delegate { return ""; };
        private const long LogMost = 1024 * 1024;

        internal static void Remembering(string folder)
        {
            heardPath = string.IsNullOrEmpty(folder) ? null : Path.Combine(folder, "lines-said.txt");
            logPath = string.IsNullOrEmpty(folder) ? null : Path.Combine(folder, "lines-log.txt");
        }

        // Each time a voice reads a line, in the same log as the lines she builds, so building and speaking line up;
        // only this entry says the voice got it. A row waits for its voice, or is written after HeldFor or as she
        // closes.
        private sealed class Held
        {
            internal string Bare, Line, Voice;
            internal DateTime At;
        }

        // Voices read before her bubble shows the line, waiting for its row: the voice is made first and the line shown after.
        private static readonly List<Held> voicedFirst = new List<Held>();

        private static Held Matching(List<Held> among, string bare)
        {
            if (bare.Length == 0) return null;
            return among.Find(delegate(Held one) { return one.Bare.Length > 0 && (one.Bare == bare || one.Bare.Contains(bare) || bare.Contains(one.Bare)); });
        }

        private static readonly List<Held> held = new List<Held>();
        // Her last lines as said, newest last, for her brain's don't-repeat note. Under logGate.
        private static readonly List<string> lastLines = new List<string>();

        internal static string[] LastLines(int most)
        {
            lock (logGate) return lastLines.GetRange(Math.Max(0, lastLines.Count - most), Math.Min(most, lastLines.Count)).ToArray();
        }
        // Lines written in the last HeldFor, so a later piece of her voice reading one joins it. Under logGate.
        private static readonly List<Held> lately = new List<Held>();

        private static void Wrote(string bare) { if (!string.IsNullOrEmpty(bare)) lately.Add(new Held { Bare = bare, At = SimTime.UtcNow }); }
        private static readonly TimeSpan HeldFor = TimeSpan.FromSeconds(20);

        // Writes the rows held longer than HeldFor, or all of them. Under logGate.
        private static void WriteHeld(bool all)
        {
            DateTime now = SimTime.UtcNow;
            foreach (List<Held> among in new[] { held, voicedFirst })
                for (int at = 0; at < among.Count; )
                {
                    if (!all && now - among[at].At < HeldFor) { at++; continue; }
                    Write(among[at].Line);
                    if (among == held) Wrote(among[at].Bare);
                    among.RemoveAt(at);
                }
        }

        // Every row held for a voice, written now: as she closes, and for the checks.
        internal static void Flush()
        {
            try { lock (logGate) WriteHeld(true); }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        internal static void Voiced(string engine, string words, long ms, double seconds, string problem)
        {
            if (Later(delegate { Voiced(engine, words, ms, seconds, problem); })) return;
            string voiced = "voice " + engine + ", " + ms + " ms, " + seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s" + (problem == null ? "" : ", " + problem);
            string bare = Bare(words);
            // The row of the line it read gets the voice's fields. A voice made before the line is shown waits for its row;
            // one whose line never shows keeps a row of its own after HeldFor.
            try
            {
                lock (logGate)
                {
                    WriteHeld(false);
                    Held row = Matching(held, bare);
                    if (row != null) { held.Remove(row); Write(row.Line + " | " + voiced); Wrote(row.Bare); return; }
                    // A later piece of a line already written is part of that row.
                    lately.RemoveAll(delegate(Held one) { return SimTime.UtcNow - one.At > HeldFor; });
                    if (Matching(lately, bare) != null) return;
                    string alone = SimTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " | - | - | voice only | " + Brain.Scrubbed(words).Replace("\r", " ").Replace("\n", " ") + " | " + voiced;
                    voicedFirst.Add(new Held { Bare = bare, Line = alone, Voice = voiced, At = SimTime.UtcNow });
                }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        private static readonly object logGate = new object();

        // While her line log check runs, what the rest of her logs from other threads (a voice or a bubble from the section
        // before, late on a slow box) waits and is done after it, so the check reads only its own rows.
        private static int checkingOn;
        private static readonly List<Action> afterCheck = new List<Action>();

        private static bool Later(Action done)
        {
            if (checkingOn == 0 || System.Threading.Thread.CurrentThread.ManagedThreadId == checkingOn) return false;
            lock (logGate)
            {
                if (checkingOn == 0) return false;
                afterCheck.Add(done);
                return true;
            }
        }

        // Lines built and not yet said, with who she was and her mood as each was built.
        private sealed class Waiting
        {
            internal string Text, Who, Mood, Source;
            internal DateTime At;
        }

        // Where each line came from, for her line log: a pool names its rows as it hands them out, a line built from
        // one takes its name, and a brain line is matched to her brain's recent answers. An unnamed line logs as "?",
        // which the check catches.
        private static Dictionary<string, string> rowSource = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), rowSourceWas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private const int RowSourceMost = 100000;
        [ThreadStatic] private static string pickedFrom;
        // The parts dressed onto the core, each " + " and where it came from, for the same log line.
        [ThreadStatic] private static string dressedWith;

        // Names the line being built, when nothing has yet: for a line that comes from outside her pools.
        internal static void NamedFrom(string source) { if (pickedFrom == null && !string.IsNullOrEmpty(source)) pickedFrom = source; }

        private static void With(string source) { dressedWith += " + " + source; }

        internal const string Unknown = "?";

        // The source of the line built last and waiting to be shown, for the check.
        internal static string LastSource { get { lock (logGate) return unsaid.Count == 0 ? null : unsaid[unsaid.Count - 1].Source; } }

        // A pool's rows, named for her log. Returns the rows, so a pool can be named where it is handed over.
        internal static string[] Named(string[] rows, string source)
        {
            if (rows == null || string.IsNullOrEmpty(source)) return rows;
            lock (logGate)
            {
                // A pool handed over again unchanged is named already; most are, every time a line is built.
                string was;
                if (namedPools.TryGetValue(rows, out was) && was == source) return rows;
                // Full, the names go to the one before and a new one starts, so a row named a moment ago is never lost.
                if (rowSource.Count > RowSourceMost) { rowSourceWas = rowSource; rowSource = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); namedPools.Clear(); }
                if (namedPools.Count > 2000) namedPools.Clear();
                namedPools[rows] = source;
                foreach (string row in rows) if (!string.IsNullOrEmpty(row)) rowSource[RowKey(row)] = source;
            }
            return rows;
        }

        // Pools named lately, by the array itself.
        private static readonly Dictionary<string[], string> namedPools = new Dictionary<string[], string>();

        // A row as mending leaves it still finds its name: the capital and the full stop it gives a line don't count.
        private static string RowKey(string row) { return row.Trim().TrimEnd('.', '!', '?', '…', ' '); }

        private static string SourceOf(string row)
        {
            if (string.IsNullOrEmpty(row)) return null;
            string found;
            lock (logGate) return rowSource.TryGetValue(RowKey(row), out found) || rowSourceWas.TryGetValue(RowKey(row), out found) ? found : null;
        }

        // What her brain answered lately: the words, which brain, and the names of the parts of her instructions in that ask.
        private sealed class Answered
        {
            internal string Bare, Asker, Parts;
            internal DateTime At;
        }

        private static readonly List<Answered> answered = new List<Answered>();

        internal static void FromBrain(string answer, string asker, string parts)
        {
            if (Later(delegate { FromBrain(answer, asker, parts); })) return;
            string bare = Bare(answer);
            if (bare.Length == 0) return;
            lock (logGate)
            {
                answered.Add(new Answered { Bare = bare, Asker = asker ?? "", Parts = parts ?? "", At = SimTime.UtcNow });
                while (answered.Count > 20) answered.RemoveAt(0);
            }
        }

        // Her brain's answer that a shown line came from, or null.
        private static Answered BrainSaid(string plain)
        {
            if (plain.Length == 0) return null;
            string head = plain.Length > 30 ? plain.Substring(0, 30) : plain;
            lock (logGate)
            {
                DateTime now = SimTime.UtcNow;
                for (int at = answered.Count - 1; at >= 0; at--)
                {
                    Answered one = answered[at];
                    if (now - one.At > UnsaidFor) continue;
                    string theirs = one.Bare.Length > 30 ? one.Bare.Substring(0, 30) : one.Bare;
                    if (one.Bare.Contains(head) || plain.Contains(theirs)) return one;
                }
            }
            return null;
        }

        private static readonly List<Waiting> unsaid = new List<Waiting>();
        private const int UnsaidMost = 30;
        private static readonly TimeSpan UnsaidFor = TimeSpan.FromMinutes(10);

#if CHECKS
        // A line as if built, waiting for her bubble to show it, without counting it as said: for the checks.
        internal static void Built(string said, string source) { Pend(said, source); }
#endif

        private static void Pend(string said, string source = null)
        {
            if (Later(delegate { Pend(said, source); })) return;
            if (logPath == null || string.IsNullOrEmpty(said)) return;
            string who = "", mood = "";
            try { who = Who() ?? ""; mood = Feeling() ?? ""; }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            lock (logGate)
            {
                unsaid.Add(new Waiting { Text = said, Who = who, Mood = mood, Source = source ?? SourceOf(said) ?? Unknown, At = SimTime.UtcNow });
                while (unsaid.Count > UnsaidMost) unsaid.RemoveAt(0);
            }
        }

        // Her bubble showed this: every built line inside it is said now and logged. A line with none inside is logged
        // too, as her brain's matched to its answer or hers in code named by what showed it. Null kind isn't hers, like
        // chat read aloud.
        internal static void Shown(string shown, string kind = null)
        {
            if (Later(delegate { Shown(shown, kind); })) return;
            string plain = Bare(shown);
            if (plain.Length == 0) return;
            List<Waiting> said = new List<Waiting>();
            lock (logGate)
            {
                DateTime now = SimTime.UtcNow;
                for (int at = unsaid.Count - 1; at >= 0; at--)
                {
                    Waiting one = unsaid[at];
                    if (now - one.At > UnsaidFor) { unsaid.RemoveAt(at); continue; }
                    string bare = Bare(Unmarked(one.Text));
                    if (bare.Length > 0 && plain.Contains(bare)) { said.Insert(0, one); unsaid.RemoveAt(at); }
                }
            }
            // One row for the line she said. A line built inside another (a pool line, then the line dressed from it) matched
            // the same bubble: only the longest is hers as said, and the one inside it goes in its source as "from".
            List<Waiting> rows = new List<Waiting>(said);
            // Two the same length are the same line, dressed with nothing added: the one built last is the row.
            rows.Sort(delegate(Waiting x, Waiting y)
            {
                int longer = Bare(y.Text).Length.CompareTo(Bare(x.Text).Length);
                return longer != 0 ? longer : said.IndexOf(y).CompareTo(said.IndexOf(x));
            });
            List<Waiting> kept = new List<Waiting>();
            Dictionary<Waiting, string> inside = new Dictionary<Waiting, string>();
            foreach (Waiting one in rows)
            {
                Waiting outer = kept.Find(delegate(Waiting row) { return Bare(row.Text).Contains(Bare(one.Text)); });
                if (outer == null) { kept.Add(one); continue; }
                if (outer.Source == Unknown && one.Source != Unknown) outer.Source = one.Source;
                if (!inside.ContainsKey(outer)) inside[outer] = one.Text;
            }
            foreach (Waiting one in said)
                if (kept.Contains(one))
                {
                    string from;
                    string source = one.Source + (IsMeme(one.Text) ? ", meme" : "");
                    Logged(Unmarked(one.Text), one.Who, one.Mood, inside.TryGetValue(one, out from) ? source + " (from: " + Brain.Scrubbed(Unmarked(from)) + ")" : source);
                }
            if (said.Count > 0 || kind == null) return;
            Answered brain = BrainSaid(plain);
            Logged(shown, null, null, brain == null ? "code: " + kind : "brain " + brain.Asker + ", " + kind + (brain.Parts.Length == 0 ? "" : " | parts: " + brain.Parts));
        }

        // Letters and digits only, lower case, so a line matches however it was capitalised or punctuated on the way.
        private static string Bare(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            StringBuilder kept = new StringBuilder(text.Length);
            foreach (char each in text) if (char.IsLetterOrDigit(each)) kept.Append(char.ToLowerInvariant(each));
            return kept.ToString();
        }

        // Lines built and not said yet, for her checks.
        internal static int Unsaid { get { lock (logGate) return unsaid.Count; } }

        private static void Logged(string said, string asWho = null, string asMood = null, string source = null)
        {
            if (logPath == null || string.IsNullOrEmpty(said)) return;
            try
            {
                string who = asWho ?? "", mood = asMood ?? "";
                if (asWho == null)
                {
                    try { who = Who() ?? ""; mood = Feeling() ?? ""; }
                    catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                }
                string line = SimTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " | " + (who.Length == 0 ? "-" : who)
                    + " | " + (mood.Length == 0 ? "-" : mood) + " | " + (string.IsNullOrEmpty(source) ? Unknown : source) + " | " + Brain.Scrubbed(said).Replace("\r", " ").Replace("\n", " ");
                // Held a moment so her voice, which finishes on its own thread, joins the same row rather than one of its own.
                lock (logGate)
                {
                    WriteHeld(false);
                    lastLines.Add(Brain.Scrubbed(said).Replace("\r", " ").Replace("\n", " ").Trim());
                    while (lastLines.Count > 8) lastLines.RemoveAt(0);
                    string bare = Bare(said);
                    Held voiced = Matching(voicedFirst, bare);
                    if (voiced != null)
                    {
                        // Her voice reads a line in pieces: the rest of its pieces belong to this row, not rows of their own.
                        voicedFirst.RemoveAll(delegate(Held one) { return one != voiced && one.Bare.Length > 0 && bare.Contains(one.Bare); });
                        voicedFirst.Remove(voiced);
                        Write(line + " | " + voiced.Voice);
                        Wrote(bare);
                    }
                    else held.Add(new Held { Bare = bare, Line = line, At = SimTime.UtcNow });
                }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        private static void Write(string line)
        {
            try
            {
                // Half of it goes when it gets long, oldest first, so it cannot grow without end and what is left
                // is still the recent part, which is the part anybody looks at.
                FileInfo about = new FileInfo(logPath);
                if (about.Exists && about.Length > LogMost)
                {
                    string[] had = File.ReadAllLines(logPath);
                    StringBuilder kept = new StringBuilder();
                    for (int each = had.Length / 2; each < had.Length; each++) kept.AppendLine(had[each]);
                    File.WriteAllText(logPath, kept.ToString(), new UTF8Encoding(false));
                }
                File.AppendAllText(logPath, line + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        private static void LoadSaid()
        {
            if (loadedSaid) return;
            loadedSaid = true;
            try
            {
                if (heardPath == null || !File.Exists(heardPath)) return;
                DateTime now = SimTime.UtcNow;
                foreach (string raw in File.ReadAllLines(heardPath))
                {
                    int tab = (raw ?? "").IndexOf('\t');
                    if (tab <= 0) continue;
                    long ticks;
                    if (!long.TryParse(raw.Substring(0, tab), out ticks)) continue;
                    DateTime when = new DateTime(ticks, DateTimeKind.Utc);
                    if ((now - when).TotalDays > 8) continue;
                    // A fingerprint in hex, or a whole line from before the store kept fingerprints.
                    string kept = raw.Substring(tab + 1);
                    ulong print;
                    if (kept.Length != 16 || !ulong.TryParse(kept, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out print)) print = Print(kept);
                    saidAt[print] = when;
                }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // The store reads a file from before fingerprints, matches a line whatever its case, and writes nothing readable.
#if CHECKS
        internal static void PrintCheck(List<string> checks)
        {
            string folder = Path.Combine(Path.GetTempPath(), "capri-prints-" + Guid.NewGuid().ToString("N"));
            string pathWas;
            bool loadedWas;
            Dictionary<ulong, DateTime> heldWas;
            lock (gate) { pathWas = heardPath; loadedWas = loadedSaid; heldWas = new Dictionary<ulong, DateTime>(saidAt); }
            try
            {
                Directory.CreateDirectory(folder);
                string file = Path.Combine(folder, "lines-said.txt");
                long tick = SimTime.UtcNow.AddMinutes(-5).Ticks;
                File.WriteAllText(file, tick + "\tWell, that was a choice.\n" + tick + "\t" + Print("Back so soon?").ToString("x16") + "\n");
                lock (gate) { heardPath = file; loadedSaid = false; saidAt.Clear(); }
                if (HoursSince("WELL, that was a choice.") > 1) throw new Exception("A line kept as text before fingerprints was not found.");
                if (HoursSince("back so soon?") > 1) throw new Exception("A line kept as a fingerprint was not found.");
                if (HoursSince("Never said this one.") != double.MaxValue) throw new Exception("A line never said read as said.");
                Keep("A brand new line.", false);
                SaveSaid();
                foreach (string kept in File.ReadAllLines(file))
                    if (!System.Text.RegularExpressions.Regex.IsMatch(kept, "^[0-9]+\t[0-9a-f]{16}$")) throw new Exception("lines-said.txt still holds a readable line: " + kept);
                checks.Add("PASS her said-lines store keys each line by a 64-bit fingerprint: an old file of whole lines still counts, case does not matter, and the file holds only fingerprints");
            }
            finally
            {
                lock (gate) { heardPath = pathWas; loadedSaid = loadedWas; saidAt.Clear(); foreach (KeyValuePair<ulong, DateTime> one in heldWas) saidAt[one.Key] = one.Value; }
                try { Directory.Delete(folder, true); } catch (Exception) { }
            }
        }
#endif

        // Her line log holds only what she said: a line built and then replaced, dropped or cut off never goes in, a
        // line shown goes in once with who she was and her mood as it was built, and a goodbye never opens with a hello.
#if CHECKS
        internal static void LogCheck(List<string> checks)
        {
            string folder = Path.Combine(Path.GetTempPath(), "capri-linelog-" + Guid.NewGuid().ToString("N"));
            string heardWas, logWas;
            bool loadedWas;
            Dictionary<ulong, DateTime> heldWas;
            List<Waiting> unsaidWas;
            Func<string> whoWas = Who, feelingWas = Feeling;
            lock (gate) { heardWas = heardPath; logWas = logPath; loadedWas = loadedSaid; heldWas = new Dictionary<ulong, DateTime>(saidAt); }
            List<Answered> answeredWas;
            List<Held> heldRowsWas, voicedFirstWas;
            // A row still held for its voice from the section before, as on a slow box: set aside with the rest, never in the
            // check's log.
            Held before = new Held { Bare = Bare("A held row from before."), Line = "held from before | A held row from before.", At = SimTime.UtcNow };
            lock (logGate)
            {
                held.Add(before);
                unsaidWas = new List<Waiting>(unsaid); unsaid.Clear(); answeredWas = new List<Answered>(answered); answered.Clear();
                heldRowsWas = new List<Held>(held); held.Clear(); voicedFirstWas = new List<Held>(voicedFirst); voicedFirst.Clear();
                checkingOn = System.Threading.Thread.CurrentThread.ManagedThreadId;
            }
            List<Action> doneAfter = new List<Action>();
            int lateFrom = 0, lateTo = 0;
            try
            {
                Directory.CreateDirectory(folder);
                string log = Path.Combine(folder, "lines-log.txt");
                lock (gate) { heardPath = Path.Combine(folder, "lines-said.txt"); logPath = log; loadedSaid = true; saidAt.Clear(); }
                Who = delegate { return "Emo"; };
                Feeling = delegate { return "calm"; };
                Named(new[] { "Later, darling. Probably." }, "Emo: When she says goodbye");
                Keep("Hey, Honey.");
                Keep("Later, darling. Probably.");
                Who = delegate { return "Sassy"; };
                Keep("You came back. Weird.");
                if (File.Exists(log)) throw new Exception("A built line went in her line log before she said it: " + File.ReadAllText(log));
                // A voice finishing on another thread meanwhile waits until the check is done.
                System.Threading.Thread late = new System.Threading.Thread(delegate() { Voiced("free", "A voice from another thread.", 500, 1.0, null); Shown("A bubble from another thread.", "reply"); });
                lock (logGate) lateFrom = afterCheck.Count;
                late.Start();
                late.Join();
                lock (logGate) lateTo = afterCheck.Count;
                Shown("Later, darling. Probably. Genuinely.");
                Shown("lizskies is live with deadlock");
                Flush();
                string[] logged = File.Exists(log) ? File.ReadAllLines(log) : new string[0];
                if (logged.Length != 1 || !logged[0].EndsWith(" | Emo | calm | Emo: When she says goodbye | Later, darling. Probably.", StringComparison.Ordinal) || Unsaid != 2)
                    throw new Exception("Her line log held the wrong lines: " + string.Join(" / ", logged) + " (" + Unsaid + " still waiting)");
                Shown("Later, darling. Probably.");
                lock (logGate) unsaid.Add(new Waiting { Text = "An old one.", Who = "Emo", Mood = "calm", At = SimTime.UtcNow - UnsaidFor - TimeSpan.FromMinutes(1) });
                Shown("An old one.");
                Flush();
                logged = File.ReadAllLines(log);
                if (logged.Length != 1 || Unsaid != 2) throw new Exception("A line was logged twice, or one built long ago was logged when its words came up: " + string.Join(" / ", logged));

                // Where each line came from: a pool's own name, "?" for one nothing named, her brain with which brain and the
                // names of the parts of the ask, a line of hers in code by what showed it, and chat read aloud not at all.
                string named = OneOf(Named(new[] { "Welcome back, you." }, "Welcome Home: When you come back"));
                Shown(named, "remark back");
                string unnamed = OneOf(new[] { "A line nobody named." });
                Shown(unnamed, "remark back");
                FromBrain("Good boy, I only do what's best for me.", "nvidia test-model", "character stock (praise, donor), mood, memories");
                Shown("Good boy, I only do what's best for me.", "reply");
                Shown("Back to my own.", "remark settings");
                Shown("someone in chat said hi", null);
                Flush();
                logged = File.ReadAllLines(log);
                string[] wanted =
                {
                    " | Welcome Home: When you come back | Welcome back, you.",
                    " | ? | A line nobody named.",
                    " | brain nvidia test-model, reply | parts: character stock (praise, donor), mood, memories | Good boy, I only do what's best for me.",
                    " | code: remark settings | Back to my own."
                };
                if (logged.Length != 5) throw new Exception("Her line log didn't take one line for each said line, and none for chat: " + string.Join(" / ", logged));
                for (int i = 0; i < wanted.Length; i++)
                    if (!logged[i + 1].EndsWith(wanted[i], StringComparison.Ordinal)) throw new Exception("A line's source was logged wrong: " + logged[i + 1] + ", not ..." + wanted[i]);
                // One said line, one row: a pool line dressed into a longer one is the longer one's "from", and its
                // voice joins the row whether the voice came before her bubble or after.
                string core = OneOf(Named(new[] { "i could eat. nuggies, ideally" }, "Emo: hungry"));
                string dressedLine = Plainly(core, true, Kind.Own);
                Voiced("free", dressedLine, 812, 3.1, null);
                Shown(dressedLine, "remark hungry");
                string second = OneOf(Named(new[] { "Nap time, I think." }, "Emo: sleepy"));
                Shown(second, "remark sleepy");
                Voiced("free", second, 640, 1.2, null);
                Flush();
                string[] latest = File.ReadAllLines(log);
                string[] added = new string[latest.Length - logged.Length];
                Array.Copy(latest, logged.Length, added, 0, added.Length);
                if (added.Length != 2 || added[0].IndexOf(" | Emo: hungry", StringComparison.Ordinal) < 0 || added[0].IndexOf(" (from: i could eat. nuggies, ideally) | ", StringComparison.Ordinal) < 0 || !added[0].EndsWith(" | voice free, 812 ms, 3.1 s", StringComparison.Ordinal)
                    || added[1].IndexOf(" | Emo: sleepy | Nap time, I think. | voice free, 640 ms, 1.2 s", StringComparison.Ordinal) < 0)
                    throw new Exception("One said line didn't come out as one row with its source, what it was built from and its voice: " + string.Join(" / ", added));
                checks.Add("PASS one said line is one row in her line log: a pool line dressed into a longer line is that line's \"from\" rather than a row of its own, and her voice joins the row whether it came before her bubble or after (question 102)");
                checks.Add("PASS her line log names where each line came from: a pool by its name, a line nothing named as ?, her brain's by which brain, what asked and the parts of that ask by name, a line of hers in code by what showed it, and chat read aloud not at all");
                checks.Add("PASS her line log has only what she said: built lines wait until her bubble shows them, keep who she was and her mood from when they were built, go in once, and a replaced or dropped line never goes in");

                string greeted = null;
                foreach (string farewell in new[] { "later, darling. probably.", "bye, %petname%.", "see you soon.", "night, %petname%. drink some water.", "off you go." })
                {
                    if (!Leaving(farewell)) throw new Exception("A goodbye wasn't read as one: " + farewell);
                    for (int dressing = 0; dressing < 120 && greeted == null; dressing++)
                    {
                        string dressed = Dressed(farewell, true, false);
                        if (dressed != null && Greeting.IsMatch(dressed)) greeted = dressed;
                    }
                }
                if (greeted != null) throw new Exception("A goodbye was dressed with a greeting: " + greeted);
                if (Leaving("you came back. weird.") || Leaving("hey, you're back.")) throw new Exception("A hello was read as a goodbye.");
                checks.Add("PASS a goodbye is never dressed with a greeting opener like \"Oh, hello.\", in 600 dressings of five goodbyes");

                string all = File.ReadAllText(log);
                if (lateTo - lateFrom < 2 || all.Contains("from before") || all.Contains("another thread"))
                    throw new Exception("Her line log check read rows that weren't its own: " + (lateTo - lateFrom) + " calls held off, log " + all.Replace(Environment.NewLine, " / "));
                checks.Add("PASS her line log check reads only its own rows: a row still held for its voice from before is set aside, and a voice or bubble from another thread waits until it is done, however slow the PC");
            }
            finally
            {
                Who = whoWas;
                Feeling = feelingWas;
                lock (gate) { heardPath = heardWas; logPath = logWas; loadedSaid = loadedWas; saidAt.Clear(); foreach (KeyValuePair<ulong, DateTime> one in heldWas) saidAt[one.Key] = one.Value; }
                lock (logGate)
                {
                    unsaid.Clear(); unsaid.AddRange(unsaidWas); answered.Clear(); answered.AddRange(answeredWas);
                    heldRowsWas.Remove(before);
                    held.Clear(); held.AddRange(heldRowsWas); voicedFirst.Clear(); voicedFirst.AddRange(voicedFirstWas);
                    checkingOn = 0;
                    // The check's own calls from another thread are dropped; anything else of hers is done now, in order.
                    foreach (Action one in afterCheck) doneAfter.Add(one);
                    afterCheck.Clear();
                }
                if (lateTo > lateFrom && lateTo <= doneAfter.Count) doneAfter.RemoveRange(lateFrom, lateTo - lateFrom);
                foreach (Action one in doneAfter) { try { one(); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); } }
                try { Directory.Delete(folder, true); } catch (Exception) { }
            }
        }
#endif

        private static void SaveSaid()
        {
            try
            {
                if (heardPath == null) return;
                StringBuilder made = new StringBuilder();
                lock (gate)
                    foreach (KeyValuePair<ulong, DateTime> one in saidAt)
                        made.Append(one.Value.Ticks).Append('\t').AppendLine(one.Key.ToString("x16"));
                File.WriteAllText(heardPath, made.ToString(), new UTF8Encoding(false));
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        private static bool Lately(string said, double hours = -1)
        {
            if (string.IsNullOrEmpty(said)) return false;
            lock (gate)
            {
                LoadSaid();
                DateTime when;
                if (!saidAt.TryGetValue(Print(said), out when)) return false;
                return (SimTime.UtcNow - when).TotalHours < Math.Max(0, hours >= 0 ? hours : RecentHours);
            }
        }

        // Hours since she last said a line, or the largest number there is when the store has no record of it.
        internal static double HoursSince(string said)
        {
            if (string.IsNullOrEmpty(said)) return double.MaxValue;
            lock (gate)
            {
                LoadSaid();
                DateTime when;
                return saidAt.TryGetValue(Print(said), out when) ? Math.Max(0, (SimTime.UtcNow - when).TotalHours) : double.MaxValue;
            }
        }

        // Today's mood's own opener or tail, now and then; swapped by the checks.
        internal static Func<string, string> MoodEnd = delegate(string kind) { return Personalities.MoodPart(kind, dice); };

        // For the checks: a core dressed as a liked reaction is.
        internal static string DressedForCheck(string core)
        {
            string made = Dressed(core, true, false);
            pickedFrom = null;
            return made;
        }

        // The line in a set said longest ago; ones never said tie, and a tie goes at random.
        internal static string Longest(IList<string> pool, Random dice)
        {
            string best = null;
            double longest = -1;
            int ties = 0;
            foreach (string line in pool)
            {
                double ago = HoursSince(line);
                if (ago > longest) { best = line; longest = ago; ties = 1; }
                else if (ago == longest && dice.Next(++ties) == 0) best = line;
            }
            return best;
        }

        // A fixed line she says only now and then, on the same disk store as everything else: true once the hours have
        // passed, which counts as saying it. logged: a line about to be said goes in her line log; a pool key
        // (sys:funny) doesn't.
        internal static bool Rested(string said, double hours, bool logged = false, string source = null)
        {
            if (string.IsNullOrEmpty(said)) return false;
            lock (gate)
            {
                LoadSaid();
                DateTime when;
                if (saidAt.TryGetValue(Print(said), out when) && (SimTime.UtcNow - when).TotalHours < Math.Max(0, hours)) return false;
            }
            Keep(said, logged, source);
            return true;
        }

#if CHECKS
        // Dressed lines from a pool of three cores: three in a row are three cores however each is dressed, and a fourth is
        // the core said longest ago. Null when right.
        internal static string CoreGuardCheck()
        {
            Action back = HeldSaid();
            try
            {
                string[] cores = { "The kettle core sings again.", "The window core stays open.", "The garden core grows wild." };
                List<string> seen = new List<string>();
                string first = null;
                for (int i = 0; i < 4; i++)
                {
                    string said = DressFrom(cores, true, Kind.Reply, "check: cores");
                    string core = Array.Find(cores, one => (said ?? "").IndexOf(one.Substring(4, 10), StringComparison.OrdinalIgnoreCase) >= 0);
                    if (core == null) return "a dressed line lost its core: " + said;
                    if (i == 0) first = core;
                    if (i < 3 && seen.Contains(core)) return "a core came back within three: " + string.Join(" / ", seen.ToArray()) + " then " + core;
                    if (i == 3 && core != first) return "the fourth wasn't the core said longest ago: " + core + ", wanted " + first;
                    seen.Add(core);
                }
                // Her brain's don't-repeat note: her last four lines at 80 characters at most, a sliver of its context.
                List<string> lastWas;
                lock (logGate) { lastWas = new List<string>(lastLines); lastLines.Clear(); for (int i = 0; i < 6; i++) lastLines.Add("Line " + i + " " + new string('w', 150)); }
                string note = Brain.SaidLately();
                lock (logGate) { lastLines.Clear(); lastLines.AddRange(lastWas); }
                int rows = note.Split('\n').Length - 2;
                if (rows != 4 || note.Length > 420 || note.Length / 4 > Brain.Window / 20) return "her don't-repeat note is " + rows + " lines, " + note.Length + " characters, against a " + Brain.Window + " token window";
                // A restart forgets the rounds in code, not her memory: the next line is one she hasn't said.
                string[] four = { "Round line one.", "Round line two.", "Round line three.", "Round line four." };
                string a = CapriDesktop.Lines.Pick(four), b = CapriDesktop.Lines.Pick(four);
                CapriDesktop.Lines.Forget();
                string c = CapriDesktop.Lines.Pick(four);
                if (c == a || c == b) return "after a restart her round started over: " + a + ", " + b + ", then " + c;
                return null;
            }
            finally { back(); }
        }

        // The said-lines store as it is now, put back by the call returned: a check that moves her clock on leaves no
        // line or timer said in the future, where Rested can't reach it.
        internal static Action HeldSaid()
        {
            Dictionary<ulong, DateTime> held;
            lock (gate) { LoadSaid(); held = new Dictionary<ulong, DateTime>(saidAt); }
            return delegate { lock (gate) { saidAt.Clear(); foreach (KeyValuePair<ulong, DateTime> one in held) saidAt[one.Key] = one.Value; } };
        }
#endif

        // The core the line being built was made from, so the check can turn down a core she said lately
        // and not only a finished line. Set as a line is dressed and read straight after, on the one
        // thread that builds her lines.
        private static string builtFrom;

        private static int sinceSave;
        private static DateTime savedAt = DateTime.MinValue;

        // logged: only a line she says goes in her line log. Her pool keys (sys:funny) and the core kept on its own so
        // it is not reused in another wrapper are remembered the same way, but they were showing in her log as lines
        // she never said.
        private static void Keep(string said, bool logged = true, string source = null)
        {
            if (string.IsNullOrEmpty(said)) return;
            lock (gate)
            {
                LoadSaid();
                saidAt[Print(said)] = SimTime.UtcNow;
                if (saidAt.Count > RememberMost)
                {
                    // Oldest out first, so a long session cannot grow this without end.
                    List<KeyValuePair<ulong, DateTime>> all = new List<KeyValuePair<ulong, DateTime>>(saidAt);
                    all.Sort(delegate(KeyValuePair<ulong, DateTime> x, KeyValuePair<ulong, DateTime> y) { return x.Value.CompareTo(y.Value); });
                    for (int i = 0; i < all.Count / 4; i++) saidAt.Remove(all[i].Key);
                }
                sinceSave++;
            }
            // Not logged yet: a built line goes in her log once it is said (Shown), and one replaced, dropped or cut off
            // by closing never does. Who she was and her mood are taken now, as it was built.
            if (logged) Pend(said, source);
            // Saved every three lines or half a minute, whichever comes first, and by Settle on the way out, so a short
            // session's lines aren't forgotten.
            if (sinceSave >= 3 || (SimTime.UtcNow - savedAt).TotalSeconds >= 30) { sinceSave = 0; savedAt = SimTime.UtcNow; SaveSaid(); }
        }

        // On the way out. Her last few lines are worth more than the rest: they are the ones she would repeat
        // first if the next start found them missing.
        internal static void Settle()
        {
            Flush();
            try { sinceSave = 0; savedAt = SimTime.UtcNow; SaveSaid(); }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // Builds a line, trying again when it lands on something she has said lately.
        // strict: a pool that may come up empty says nothing rather than a line for another part of the day.
        private static string Unheard(Func<string> make, bool strict = false, double hours = -1)
        {
            string said = null;
            string oldest = null;
            DateTime oldestAt = DateTime.MaxValue;
            // Generous, because the pools cycle in step and that makes two finished lines collide more
            // often than the raw combination count suggests.
            string core = null, oldestCore = null;
            string from = null, oldestFrom = null;
            bool fitted = false;
            for (int tries = 0; tries < 30; tries++)
            {
                builtFrom = null;
                pickedFrom = null;
                dressedWith = null;
                said = make();
                core = builtFrom;
                from = pickedFrom ?? SourceOf(core) ?? SourceOf(said);
                if (dressedWith != null) from = (from ?? Unknown) + dressedWith;
                dressedWith = null;
                if (said == null) return null;
                // One for another part of the day is passed over while anything else comes up.
                if (!FitsHour(said)) continue;
                fitted = true;
                // A core she said lately is a repeat however it is dressed, which is the whole of what makes
                // one sound like a repeat: the opener and the tail change and the middle does not.
                if (!Lately(said, hours) && (core == null || core == said || !Lately(core, hours))) { oldest = null; break; }
                // A small pool can sit wholly inside the window, so when every line is recent she says the one she said
                // longest ago.
                DateTime when;
                // Aged by its core when it has one: a new wrapper round a core said lately is not a line she hasn't said.
                string aged = core != null && core != said && Lately(core, hours) ? core : said;
                if (!Said(aged, out when)) { oldest = said; oldestCore = core; oldestFrom = from; break; }
                if (when < oldestAt) { oldestAt = when; oldest = said; oldestCore = core; oldestFrom = from; }
            }
            if (oldest != null) { said = oldest; core = oldestCore; from = oldestFrom; }
            pickedFrom = null;
            if (!fitted && strict) return null;
            Keep(said, true, from);
            // The core as well, so the next line built from it is turned down even in a different wrapper.
            if (!string.IsNullOrEmpty(core) && core != said) Keep(core, false);
            builtFrom = null;
            return said;
        }

        // A line from outside her pools (her interests file's openers) passed over while said within her rest: RecentHours
        // times her personality's and mood's paces from dynamic.md, as her other timings.
        internal static Func<double> RestPace = delegate { return 1; };

        internal static double RestHours { get { double pace = 1; try { pace = RestPace(); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); } return Math.Max(0, RecentHours) * Math.Max(0.1, pace); } }

        internal static string NotLately(Func<string> make) { return Unheard(make, true, RestHours); }

        // For a part woven onto a line elsewhere (Interests.Tail): her same memory of what she said lately.
        internal static bool SaidLately(string said) { return Lately(said); }

        internal static bool LastSaid(string said, out DateTime when) { return Said(said, out when); }

        internal static void Remember(string said) { Keep(said, false); }

        // For the checks: what a restart does to her memory of what she said, saved and read back from its file.
        internal static void Restarted()
        {
            SaveSaid();
            lock (gate) { loadedSaid = false; saidAt.Clear(); }
        }

        // When a line was last said, or false when it has not been.
        private static bool Said(string said, out DateTime when)
        {
            when = DateTime.MinValue;
            if (string.IsNullOrEmpty(said)) return false;
            lock (gate)
            {
                LoadSaid();
                return saidAt.TryGetValue(Print(said), out when);
            }
        }

        // One of her silly lines, or null when the pool has been emptied.
        // Her plain silly lines only: a meme waits for a moment it fits ("meme when" sections), never this timer.
        // One of her stock ways to bring up a topic, with its name in place of %it%.
        internal static string TopicStart(string name)
        {
            return Mended(Unheard(delegate
            {
                Fresh();
                string core = Pick("topic starts");
                return core == null ? null : Dressed(core.Replace("%it%", name), true, false, false, Kind.Own);
            }, true));
        }

        internal static string Funny()
        {
            // Once hers have gone round, a meme from the weekly search now and then (Better conversations).
            string grown = null;
            try { grown = CapriDesktop.Memes.Next((int)Count("funny")); }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            if (grown != null) return Mended(grown);
            return Mended(Unheard(delegate { Fresh(); return EitherWhere("funny", line => !IsMeme(line)); }, true));
        }

        // How often a funny line comes, from dynamic.md's cringe dial shifted by personality and mood: 3 in 100 free
        // moments up to 40, 10 at 100, 20 past 9000. At most one an hour, half an hour at 100, 20 minutes past 9000.
        internal static int FunnyChance { get { int much; bool past; Cringed(out much, out past); return past ? 20 : much <= 40 ? 3 : 3 + (much - 40) * 7 / 60; } }

        internal static double FunnyRest { get { int much; bool past; Cringed(out much, out past); return past ? 1 / 3.0 : much <= 40 ? 1 : 1 - (much - 40) * 0.5 / 60; } }

        private static void Cringed(out int much, out bool past)
        {
            much = 0;
            past = false;
            try { much = Math.Max(0, Math.Min(100, Dynamics.Cringe + Personalities.Cringe)); past = Dynamics.OverNineThousand; }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // A line naming a part of the day is only said in that part of their day, as her small talk asks are:
        // "tonight" from six in the evening until five, "this morning" from five until noon.
        internal static Func<int> HourNow = delegate { return Hours.OwnHour(SimTime.Now); };

        private static readonly Regex Night = new Regex(@"\b(?:tonight|at night|good ?night|night night)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Morning = new Regex(@"\b(?:this morning|good morning|breakfast)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Afternoon = new Regex(@"\b(?:this afternoon|good afternoon|lunch)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Evening = new Regex(@"\b(?:this evening|good evening|dinner)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Lines naming a part of the day against the hour, and a pool drawn at an hour that fits none of a line.
#if CHECKS
        internal static void HourCheck(List<string> checks)
        {
            string[,] cases =
            {
                { "Doing anything fun tonight?", "21", "y" }, { "Doing anything fun tonight?", "11", "n" }, { "Good morning, you.", "8", "y" },
                { "Good morning, you.", "15", "n" }, { "Last night was fun.", "10", "y" }, { "See you tomorrow morning.", "22", "y" },
                { "Had lunch yet?", "13", "y" }, { "Had lunch yet?", "2", "n" }, { "Big plans this evening?", "19", "y" }, { "Big plans this evening?", "9", "n" },
            };
            List<string> wrong = new List<string>();
            for (int i = 0; i < cases.GetLength(0); i++)
                if (FitsHour(cases[i, 0], int.Parse(cases[i, 1])) != (cases[i, 2] == "y")) wrong.Add(cases[i, 0] + " at " + cases[i, 1]);
            Func<int> hourWas = HourNow;
            try
            {
                HourNow = delegate { return 11; };
                for (int draw = 0; draw < 20; draw++)
                {
                    string one = OneOf(new[] { "I was going to behave tonight.", "Hello there, you." });
                    if (one != null && one.IndexOf("tonight", StringComparison.OrdinalIgnoreCase) >= 0) { wrong.Add("a pool said tonight at 11: " + one); break; }
                }
                string none = OneOf(new[] { "I was going to behave tonight." });
                if (none != null) wrong.Add("a pool with only a night line said it at 11: " + none);
            }
            finally { HourNow = hourWas; }
            if (wrong.Count > 0) throw new Exception("Lines naming a part of the day went wrong: " + string.Join(" | ", wrong.ToArray()));
            checks.Add("PASS a line naming a part of the day (tonight, this morning, lunch, this evening) is only said in that part of their day, from any pool, and a pool with nothing for this hour says nothing");
        }
#endif

        internal static bool FitsHour(string line) { int hour; try { hour = HourNow(); } catch (Exception) { return true; } return FitsHour(line, hour); }

        internal static bool FitsHour(string line, int hour)
        {
            if (string.IsNullOrEmpty(line)) return true;
            if (Night.IsMatch(line) && !(hour >= 18 || hour < 5)) return false;
            if (Morning.IsMatch(line) && !(hour >= 5 && hour < 12)) return false;
            if (Afternoon.IsMatch(line) && !(hour >= 11 && hour < 17)) return false;
            if (Evening.IsMatch(line) && !(hour >= 17 && hour < 23)) return false;
            return true;
        }

        // One line she opens with, rather than a clause joined onto something else.
        // One from a pool she was handed, held to the same recency as everything else she says. Her hello and
        // goodbye were picked with a plain roll, so a five line pool repeated inside an evening.
        internal static string OneOf(string[] pool)
        {
            if (pool == null || pool.Length == 0) return null;
            return Mended(Unheard(delegate { lock (gate) return pool[dice.Next(pool.Length)]; }, true));
        }

        // One line from a pool by name, held to the same recency as everything else she says.
        internal static string From(string group) { return Mended(Unheard(delegate { Fresh(); return Pick(group); }, true)); }

        // A pool as she has it now, packs included, for the checks.
        internal static string[] Pool(string name)
        {
            Fresh();
            lock (gate) { string[] found; return pools.TryGetValue(name ?? "", out found) ? found : null; }
        }

        internal static string FlirtFirst() { return Mended(Unheard(delegate { Fresh(); return Pick("flirting first"); }, true)); }

        // Her line for being called by name while she's wanting, said before her reply.
        // A counter run's lines, each with the count it shows from, lowest first (Streak.cs).
        internal static List<KeyValuePair<int, string>> CountedLines()
        {
            Fresh();
            List<KeyValuePair<int, string>> found = new List<KeyValuePair<int, string>>();
            string[] lines;
            lock (gate) if (!pools.TryGetValue("counter run", out lines) || lines == null) return found;
            foreach (string raw in lines)
            {
                Match one = Regex.Match(Unmeme(raw ?? ""), @"^(\d+)\s*:\s*(.+)$");
                int at;
                if (one.Success && int.TryParse(one.Groups[1].Value, out at)) found.Add(new KeyValuePair<int, string>(at, one.Groups[2].Value.Trim()));
            }
            found.Sort((a, b) => a.Key.CompareTo(b.Key));
            return found;
        }

        // A callback to a counter run, before or after her reply.
        internal static string CountedCallback(bool opens) { return Mended(Unheard(delegate { Fresh(); return Pick(opens ? "counter run opens" : "counter run closes"); })); }

        internal static string SaidMyName() { return Mended(Unheard(delegate { Fresh(); return Pick("said my name"); }, true)); }

        // A line picked whole gets the same last pass as a built one; what she remembers saying stays as picked, so the
        // recency check still knows it.
        private static string Mended(string said)
        {
            if (string.IsNullOrEmpty(said) || IsMeme(said)) return said;
            try { return Grammar.Mend(said, true); }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); return said; }
        }

        internal static string For(string act, bool liked, string[] hers)
        {
            return Unheard(delegate { return Built(act, liked, hers); });
        }

        // Whether her answer to this act may take her list woven in: never after food or a gift, where a promise of what she is
        // into reads as a non sequitur ("This is the softest thing I own. Perhaps we should consider ...").
        internal static bool Weaves(string act)
        {
            Acts.Act found = Acts.Find(act);
            return found == null || found.Kind != "food" && found.Kind != "gift";
        }

        private static string Built(string act, bool liked, string[] hers)
        {
            Fresh();
            string group = Group(act, liked);
            bool haveHers = hers != null && hers.Length > 0;
            long mine = 0;
            mine = Both(group);

            // Both pools, split by how many lines each has rather than evenly; hers count nearly twice, since they're
            // in her voice, and the share stays between a quarter and two thirds.
            string core = null;
            int favour = 55;
            if (haveHers)
            {
                double weighed = hers.Length * 1.8;
                favour = mine == 0 ? 100 : (int)Math.Round(100 * weighed / (weighed + mine));
                favour = Math.Max(25, Math.Min(66, favour));
            }
            if (haveHers && (mine == 0 || dice.Next(100) < favour)) core = Mine(group, hers);
            if (core == null) core = Either(group);
            if (core == null && haveHers) core = Mine(group, hers);
            if (core == null) return null;
            // Dressed, not Dress: For already runs this through the recency check, and a second one inside it
            // kept every guess as said, so one pat filled her memory with thirty lines she never spoke.
            return Dressed(core, liked, Weaves(act), liked && act != "ignored", Kind.Reply, true);
        }

        // A shuffle bag over the personality's own lines, so those do not repeat either. Rebuilt when the set
        // changes, which is what happens when she switches personality or mood.
        private static readonly Dictionary<string, string[]> mineWas = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        private static string Mine(string group, string[] hers)
        {
            string key = group + " hers";
            lock (gate)
            {
                string[] had;
                if (!mineWas.TryGetValue(key, out had) || had == null || !Same(had, hers))
                {
                    mineWas[key] = hers;
                    pools[key] = hers;
                    bags.Remove(key);
                }
            }
            return Pick(key);
        }

        private static bool Same(string[] one, string[] two)
        {
            if (one == null || two == null || one.Length != two.Length) return false;
            for (int i = 0; i < one.Length; i++) if (!string.Equals(one[i], two[i], StringComparison.Ordinal)) return false;
            return true;
        }

        // Dresses a line she already has, so even a fixed set of cores stops sounding fixed. What she is into is woven
        // only into her answer to an act of theirs; after food or a mood line it read as a non sequitur.
        internal static string Dress(string core, bool liked, Kind kind)
        {
            return Unheard(delegate { return Dressed(core, liked, false, false, kind); });
        }

        // A line from a pool, dressed, its core picked inside her 24 hour memory: a core said lately is passed over for
        // another however it would be dressed. Named, the pool's lines carry its name in her line log.
        internal static string DressFrom(string[] pool, bool liked, Kind kind, string name = null)
        {
            if (pool == null || pool.Length == 0) return null;
            Named(pool, name);
            return Unheard(delegate { string core; lock (gate) core = pool[dice.Next(pool.Length)]; return Dressed(core, liked, false, false, kind); });
        }

        // The same dressing without what she is into woven through it. For lines she brings up herself rather than
        // lines that answer something they did: saying she is hungry and then talking about what she is into is two moods at
        // once, and reads as her losing the thread.
        internal static string Plainly(string core, bool liked, Kind kind)
        {
            return Unheard(delegate { return Dressed(core, liked, false, false, kind); });
        }

        // What a core is decides its parts. A reply takes any opener, tail and weave; her own lines any opener and tail; an
        // ask (hungry, wanting pats) or a missed-you line only an opener that gets their attention, never a tail.
        internal enum Kind { Reply, Own, Ask, Missed }

        // Openers that get their attention rather than react to something, so they read before an ask or a missed-you
        // line. Any other opener, a mood's own and one written into lines.md included, reacts, and stays on replies.
        private static readonly HashSet<string> Attention = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Hm.", "Hmm!", "Mm.", "Mmm.", "Listen.", "Honestly?", "Well.", "Hey, hey.", "Hey you.",
            "Hey-", "Oh-", "Aw, hey.", "Ahem.", "Hey. Hey!", "Right, listen.", "Hey, you.", "Hey!", "Hmph.", "Honestly.",
        };

        internal static bool Attends(string opener) { return opener != null && Attention.Contains(opener.Trim()); }

        // The lines that read as nonsense, each dressed 500 times: an ask or a missed-you line never takes a tail, a mood
        // end or an opener that reacts ("That's it. Was it that long?"), and a reply still takes all three.
#if CHECKS
        // Three joins from her line log that made no sense: a promise of what she is into after a gift (twice), and a blend line
        // running two clauses together.
        internal static void LoggedJoinsCheck(List<string> checks)
        {
            List<string> wrong = new List<string>();
            if (Weaves("plushie") || Weaves("flowers") || !Weaves("petted")) wrong.Add("her list still woven after a gift, or not after a pat");
            string kinds;
            if (Interests.FrameKinds.TryGetValue("Perhaps we should consider %it% next.", out kinds) && Array.IndexOf(kinds.Split(' '), "wish") >= 0)
                wrong.Add("\"Perhaps we should consider %it% next.\" still takes a wish of hers");
            if (!Blends.RunsOn("Are we playing now this is the only acceptable time.") || Blends.RunsOn("Are we playing now? This is the only acceptable time.") || Blends.RunsOn("This is the only acceptable time."))
                wrong.Add("the run-on blend line read wrongly");
            if (wrong.Count > 0) throw new Exception("A logged join still reads wrong: " + string.Join(" | ", wrong.ToArray()));
            checks.Add("PASS the three joins from her line log: nothing of her list after a plushie or flowers, \"Perhaps we should consider\" never with a wish of hers, and a blend line running two clauses together is left out");
        }

        internal static void KindCheck(List<string> checks)
        {
            Func<string, string> endWas = MoodEnd;
            List<string> wrong = new List<string>();
            int opened = 0, replyTailed = 0, replyReacted = 0;
            try
            {
                MoodEnd = delegate(string kind) { return kind == Personalities.OpenersHeading ? "Moodopen." : "moodtail."; };
                object[,] cases =
                {
                    { "chimpken nuggies? just asking", Kind.Ask }, { "can I have some nuggies", Kind.Ask },
                    { "rawr, come back to me or I might lose it", Kind.Missed }, { "was it that long?", Kind.Missed },
                };
                for (int c = 0; c < cases.GetLength(0); c++)
                {
                    string core = (string)cases[c, 0];
                    string head = core.Substring(0, 6), end = core.TrimEnd('?', '.').Substring(core.TrimEnd('?', '.').Length - 6);
                    for (int i = 0; i < 500 && wrong.Count < 5; i++)
                    {
                        string made = Dressed(core, i % 2 == 0, false, false, (Kind)cases[c, 1]) ?? "";
                        pickedFrom = null;
                        int at = made.IndexOf(head, StringComparison.OrdinalIgnoreCase), last = made.LastIndexOf(end, StringComparison.OrdinalIgnoreCase);
                        string before = at < 0 ? "" : made.Substring(0, at).Trim(), after = last < 0 ? "" : made.Substring(last + end.Length).Trim(' ', '.', '!', '?');
                        if (at < 0 || last < 0 || after.Length > 0 || (before.Length > 0 && !Attends(before))) wrong.Add(made);
                        if (before.Length > 0) opened++;
                    }
                }
                for (int i = 0; i < 500; i++)
                {
                    string made = Dressed("nice catch", true, false, false, Kind.Reply) ?? "";
                    pickedFrom = null;
                    if (made.IndexOf("moodtail", StringComparison.OrdinalIgnoreCase) >= 0) replyTailed++;
                    int at = made.IndexOf("nice catch", StringComparison.OrdinalIgnoreCase);
                    if (at > 0 && !Attends(made.Substring(0, at).Trim())) replyReacted++;
                }
            }
            finally { MoodEnd = endWas; }
            // An answer to something they did never opens with a greeting, and food is never answered with her list.
            int greeted = 0;
            for (int i = 0; i < 500; i++)
            {
                string made = Dressed("sugar fixes everything", true, false, true, Kind.Reply, true) ?? "";
                pickedFrom = null;
                int at = made.IndexOf("sugar", StringComparison.OrdinalIgnoreCase);
                if (at > 0 && Greeting.IsMatch(made.Substring(0, at))) greeted++;
            }
            if (greeted > 0 || Weaves("sweet") || Weaves("snack") || Weaves("nuggies") || !Weaves("petted") || !Weaves("praised"))
                wrong.Add("an answer to an act opened with a greeting " + greeted + " times, or food took her list: sweet " + Weaves("sweet") + ", petted " + Weaves("petted"));
            if (wrong.Count > 0 || replyTailed == 0 || replyReacted == 0)
                throw new Exception("A line took a part it doesn't fit, or a reply lost its parts: " + string.Join(" | ", wrong.ToArray()) + "; a reply's tail " + replyTailed + ", a reply's reacting opener " + replyReacted);
            checks.Add("PASS an ask (\"Can I have some nuggies\") and a missed-you line (\"Was it that long?\") take no tail, no mood end and only an opener that gets their attention, " + opened + " opened in 2000; a reply still takes a tail (" + replyTailed + ") and a reacting opener (" + replyReacted + ") in 500");
        }
#endif

        private static readonly Regex Greeting = new Regex(@"\b(hello|hi|hiya|hey|heya|welcome|there you are)\b", RegexOptions.IgnoreCase);
        private static readonly Regex Farewell = new Regex(@"^\W*(?:\w+,\s*)?(?:bye|goodbye|good night|night|later|see you|see ya|off you go|go on then|take care|farewell|ciao|catch you|until next time)\b", RegexOptions.IgnoreCase);

        // A line that sees them off.
        internal static bool Leaving(string core) { return !string.IsNullOrEmpty(core) && Farewell.IsMatch(core); }

        // A meme line: one starting with MemeMark, said exactly as written. No opener or tail, no capital or full stop, no
        // grammar mending, no translation and no code mod's rewording; the mark comes off as she shows and says it.
        internal const string MemeMark = "~";

        internal static bool IsMeme(string line) { return line != null && line.TrimStart().StartsWith(MemeMark, StringComparison.Ordinal); }

        internal static string Unmeme(string line) { return IsMeme(line) ? Untagged(line.TrimStart().Substring(MemeMark.Length).TrimStart()) : line; }

        // Where a meme is from, in braces after the mark: "~{SAO Ab} We must save my family!!". Her bubble's top line shows
        // it while she says one. Null for an untagged meme or any other line.
        private static readonly Regex Tag = new Regex(@"^\{([^{}]{1,40})\}\s*", RegexOptions.CultureInvariant);

        internal static string MemeSource(string line)
        {
            if (!IsMeme(line)) return null;
            Match found = Tag.Match(line.TrimStart().Substring(MemeMark.Length).TrimStart());
            return found.Success ? found.Groups[1].Value.Trim() : null;
        }

        private static string Untagged(string text) { return Tag.Replace(text, "", 1); }

        // A meme for a moment she is in, or null: due once her funny timer is half way to its next time (a timer never run
        // starts now) and the line is three days old. Saying it starts the timer again.
        internal const string InContextPrefix = "meme when ";

        internal static string InContext(string moment, double stretch)
        {
            Fresh();
            string group = InContextPrefix + moment;
            DateTime when;
            if (!Said("sys:funny", out when)) { Keep("sys:funny", false); return null; }
            if ((SimTime.UtcNow - when).TotalHours < FunnyRest * Math.Max(0, stretch) / 2) return null;
            List<string> due = new List<string>();
            string picked;
            lock (gate)
            {
                foreach (string pool in Senpai() ? new[] { group, Senpai(group) } : new[] { group })
                {
                    string[] lines;
                    if (!pools.TryGetValue(pool, out lines) || lines == null) continue;
                    foreach (string line in lines) { DateTime at; if (!Said(line, out at) || (SimTime.UtcNow - at).TotalDays >= 3) due.Add(line); }
                }
                if (due.Count == 0) return null;
                picked = due[dice.Next(due.Count)];
            }
            Keep(picked, true, "lines.md: " + group);
            Keep("sys:funny", false);
            return picked;
        }

        // A line said in parts: "#wait20s#" between two parts pauses that long, "#wait 2,5s#" too; no brackets, which
        // voice tones use. Split before any translating, so a pause never reaches one; a meme's parts stay as written.
        private static readonly Regex Pause = new Regex(@"\s*#wait\s*(\d+(?:[.,]\d+)?)\s*s#\s*", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        internal static string Unpaused(string line) { return string.IsNullOrEmpty(line) ? line ?? "" : Pause.Replace(line, " "); }

        // A built line as her bubble shows it, for her line log: no meme mark, silent marks, cue or pause. A meme says so
        // in the row's source instead.
        internal static string Unmarked(string line) { return Voice.Uncued(Voice.Unsilenced(Unpaused(Unmeme(line ?? "")))).Trim(); }

        internal static List<KeyValuePair<double, string>> Parts(string line)
        {
            List<KeyValuePair<double, string>> parts = new List<KeyValuePair<double, string>>();
            if (string.IsNullOrWhiteSpace(line)) return parts;
            bool meme = IsMeme(line);
            string source = MemeSource(line), mark = MemeMark + (source == null ? "" : "{" + source + "} ");
            string[] bits = Pause.Split(meme ? Unmeme(line) : line);
            double wait = 0;
            for (int i = 0; i < bits.Length; i++)
            {
                if (i % 2 == 1) { wait += double.Parse(bits[i].Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture); continue; }
                string text = bits[i].Trim();
                if (text.Length == 0) continue;
                parts.Add(new KeyValuePair<double, string>(parts.Count == 0 ? 0 : wait, meme ? mark + text : text));
                wait = 0;
            }
            return parts;
        }

        // Her shipped meme lines, marked in a lines.md written before the mark existed when they are still as she shipped them.
        private static readonly string[] Memes = { "am i gregnant", "can you get pregante from a toilet seat", "no u" };

        // Meme lines through everything that reshapes a line: dressing, mending, the lint, a translation and her bubble's own
        // pass. Each comes out exactly as written, without its mark. A shipped one in an old lines.md gets the mark. And a
        // translation that loses or adds a %token% is not kept.
#if CHECKS
        internal static void MemeCheck(List<string> checks, string folder)
        {
            foreach (string meme in Memes)
            {
                string marked = MemeMark + meme;
                string dressed = Dressed(marked, true, true), mended = Grammar.Mend(marked), whole = Mended(marked);
                if (dressed != marked || mended != marked || whole != marked || Unmeme(marked) != meme)
                    throw new Exception("A meme line was reshaped: " + meme + " / " + dressed + " / " + mended + " / " + whole);
            }
            if (Grammar.Mend("am i gregnant") == "am i gregnant") throw new Exception("The meme check can't tell: an unmarked line came through mending unchanged.");
            Dictionary<string, string[]> pools = new Dictionary<string, string[]> { { "funny", new[] { MemeMark + "no u" } } };
            if (Lint.Check(pools).Count != 0) throw new Exception("The lint flagged a meme line.");
            // %gift% is her favourite gift's name, filled as she says a line of "gift remembered", and only there.
            if (Lint.Check(new Dictionary<string, string[]> { { "gift remembered", new[] { "still got that %gift% you gave me" } } }).Count != 0
                || Lint.Check(new Dictionary<string, string[]> { { "funny", new[] { "still got that %gift% you gave me" } } }).Count != 1)
                throw new Exception("The lint didn't take %gift% in gift remembered, or took it somewhere it is never filled.");
            if (!CapriDesktop.Said.SameTokens("Good %boy%, %name%.", "Dobry %boy%, %name%.") || CapriDesktop.Said.SameTokens("Good %boy%, %name%.", "Dobry chlopiec, %name%.") || CapriDesktop.Said.SameTokens("Hi %name%.", "Hej %name% %mate%."))
                throw new Exception("A translation that lost or added a token would have been kept.");
            checks.Add("PASS a meme line (~) comes out of dressing, mending, the lint and her bubble exactly as written, and a translation that loses or adds a %token% is never kept");
        }
#endif

        private static string Dressed(string core, bool liked, bool weaveHers, bool praised = false, Kind kind = Kind.Reply, bool answersAct = false)
        {
            bool asking = kind == Kind.Ask || kind == Kind.Missed;
            builtFrom = core;
            // The core's name, set before the openers and tails are picked, so theirs never stands for the line.
            if (pickedFrom == null) pickedFrom = SourceOf(core) ?? Unknown;
            dressedWith = null;
            if (string.IsNullOrEmpty(core) || IsMeme(core)) return core;
            Fresh();
            string said = core;
            bool moodOpened = false;
            // Not every time: a line that always has both ends on reads as a template.
            if (dice.Next(100) < 45)
            {
                // Today's mood's own opener now and then, when her brain has not written the line for the mood.
                string moodOpener = asking ? null : MoodEnd(Personalities.OpenersHeading);
                string openers = liked ? "openers warm" : "openers cross";
                string opener = moodOpener ?? (asking ? PickWhere(openers, Attends) : Pick(openers));
                // A goodbye with "Oh, hello." on the front says both at once, and so does "Oh, there you are." on her answer to
                // something they just did. A greeting opener is picked again, and left off if it keeps coming.
                bool noGreeting = Leaving(core) || answersAct;
                for (int again = 0; again < 4 && opener != null && noGreeting && Greeting.IsMatch(opener); again++) opener = asking ? PickWhere(openers, Attends) : Pick(openers);
                if (opener != null && !(noGreeting && Greeting.IsMatch(opener)))
                {
                    said = Join(opener, said);
                    moodOpened = opener == moodOpener;
                    With(moodOpened ? "mood " + Personalities.Attitude + ": openers" : "lines.md: " + openers);
                }
            }
            // Angry enough and the cross tails give way to threats, and doing the same thing again says so. She is
            // a cartoon threatening your shins, and the second half is useful: it tells you that you are digging
            // rather than only that she is cross.
            int cross = 0;
            try { cross = Mood.Upset; } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            bool furious = !liked && cross >= 15;

            if (!asking && furious && dice.Next(100) < 55)
            {
                string worse = Pick("making it worse");
                if (worse != null) { said = Join(said, worse); With("lines.md: making it worse"); }
            }
            bool tailed = false;
            if (!asking && dice.Next(100) < (furious ? 55 : 35))
            {
                // Praise tails only after an act of theirs she liked, half the time; fond ones otherwise.
                string pool = furious ? "tails furious" : !liked ? "tails cross" : praised && dice.Next(100) < 50 ? "tails praise" : "tails warm";
                string moodTail = furious ? null : MoodEnd(Personalities.TailsHeading);
                string tail = moodTail ?? Either(pool) ?? (pool == "tails praise" ? Either("tails warm") : null);
                // The mood's own opener and its own tail round one core clash ("Bet you can't keep up. Don't mind if I
                // do. Catch me if you can."), so that tail is left off; any other pairing stands.
                if (moodOpened && moodTail != null && tail == moodTail) tail = null;
                if (tail != null)
                {
                    said = Join(said, tail);
                    tailed = true;
                    With(moodTail != null && tail == moodTail ? "mood " + Personalities.Attitude + ": tails" : "lines.md: " + pool);
                }
            }
            // Her list woven into ordinary reactions now and then: senpai only, silent when empty. How often follows
            // her personality's interest level or the global override, shifted by her mood; 0 is never.
            int level = 0;
            try { level = Personalities.SpecialInterest; } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            // Never onto a line that already took a tail. An opener, a core, a tail and a promise is four
            // fragments in one breath, and it reads as a run-on however good each piece is on its own.
            if (weaveHers && kind == Kind.Reply && !tailed && level > 0 && dice.Next(100) < level * 4)
            {
                // With the switch on she draws from her interests file, off from her own flirting. Same dial either
                // way, so how forward she is means something whichever mode she is in.
                string hers = Brain.SenpaiLive
                    ? Interests.Tail(liked)
                    : Either(liked ? "flirting warm" : "flirting cross");
                if (!string.IsNullOrEmpty(hers))
                {
                    said = Join(said, hers);
                    With(Brain.SenpaiLive ? Interests.TailFrom ?? "interests" : "lines.md: " + (liked ? "flirting warm" : "flirting cross"));
                }
            }
            // Two or more parts joined: its smoothed line when one is kept, tokens still tokens, so nothing of theirs is sent.
            if (dressedWith != null)
                try { said = Smooth.Of(said); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }

            // Cores are written lower case so they read naturally after an opener. Whatever ends up first in the
            // finished line takes the capital, and the line gets a full stop if it has no punctuation of its own.
            said = Upper(Them.Fill(said));
            if (said.Length > 0)
            {
                char last = said[said.Length - 1];
                if (last != '.' && last != '!' && last != '?' && last != '…') said += ".";
            }
            // The last pass after every part is joined, for faults that only exist once the parts are together; Lint
            // checks the parts where they are written.
            try { said = Grammar.Mend(said); }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            return said;
        }

        // First letter up, leaving anything already capitalised or starting with a symbol alone.
        private static string Upper(string said)
        {
            if (string.IsNullOrEmpty(said)) return said;
            if (!char.IsLetter(said[0]) || char.IsUpper(said[0])) return said;
            return char.ToUpperInvariant(said[0]) + said.Substring(1);
        }

        private static string Group(string act, bool liked)
        {
            return (act ?? "") + (liked ? " likes it" : " dislikes it");
        }

        // The senpai twin of a pool, used only with the toggle on, like a personality's own lines. Shared pools are
        // everyone's fallback, so lines too forward for them live here (Critter).
        internal static string Senpai(string group) { return group + LineFile.SenpaiMark; }

        private static bool Senpai()
        {
            try { return Brain.SenpaiLive; } catch (Exception) { return false; }
        }

        // Both pools with the toggle on, weighted by size so neither drowns the other out.
        private static string Either(string group)
        {
            if (!Senpai()) return Pick(group);
            long clean, rude;
            lock (gate) { clean = Count(group); rude = Count(Senpai(group)); }
            if (rude == 0) return Pick(group);
            if (clean == 0) return Pick(Senpai(group));
            return Pick(dice.Next((int)(clean + rude)) < clean ? group : Senpai(group));
        }

        private static long Both(string group)
        {
            lock (gate) return Count(group) + (Senpai() ? Count(Senpai(group)) : 0);
        }

        // Sentences joined without doubling punctuation or dropping a capital in the middle.
        // How her parts come together, with the examples taken off her own log rather than invented. Join is
        // private and stays that way: this is the only thing that calls it from outside.
#if CHECKS
        internal static void Joins(System.Collections.Generic.List<string> checks)
        {
            string[,] cases =
            {
                // Two clauses on a comma is her ordinary rhythm and is left alone.
                { "Heh", "okay then", "Heh, okay then" },
                { "Careful", "I bite viruses", "Careful, I bite viruses" },
                // A part written as its own sentence ends the one before it rather than being stapled on.
                { "That's twice now", "You are not reading the room.", "That's twice now. You are not reading the room." },
                // A part that already carries a comma ends its sentence rather than taking a third clause.
                { "Mm-hm. I am a simple construct, I like this", "that's the one", "Mm-hm. I am a simple construct, I like this. That's the one" },
                { "You know what, ten out of ten, would be patted again", "keep that up and we'll get along", "You know what, ten out of ten, would be patted again. Keep that up and we'll get along" },
                { "Careful, I'm storing this for later", "you're in my good books", "Careful, I'm storing this for later. You're in my good books" },
                // The comma on the tail's side counts the same, which reading only the first part missed.
                { "That hit the spot", "do that again, slowly.", "That hit the spot. Do that again, slowly." },
                { "Don't tell the others", "you're alright, you know.", "Don't tell the others. You're alright, you know." },
                // Already finished, or ending in punctuation that carries on: untouched either way.
                { "Pets logged. Morale up twelve percent.", "keep that up", "Pets logged. Morale up twelve percent. Keep that up" },
                { "Oh, fine,", "have it your way", "Oh, fine, have it your way" },
            };
            System.Collections.Generic.List<string> wrong = new System.Collections.Generic.List<string>();
            for (int at = 0; at < cases.GetLength(0); at++)
            {
                string got = Join(cases[at, 0], cases[at, 1]);
                if (got != cases[at, 2]) wrong.Add("\"" + cases[at, 0] + "\" + \"" + cases[at, 1] + "\" gave \"" + got + "\"");
            }
            // And the pass that runs after every join, with the case her own log turned up: a phrase dropped in
            // after a full stop, from her interests file's "You are doing well. %it% is on the table.", came out lower
            // case in lines she actually said.
            string[,] mended =
            {
                { "You are doing well. teasing is on the table.", "You are doing well. Teasing is on the table." },
                { "I have a plan. the plan is confidence.", "I have a plan. The plan is confidence." },
                { "Oh... she said nothing.", "Oh... she said nothing." },
                { "...Fine. Apology accepted.", "...Fine. Apology accepted." },
            };
            for (int at = 0; at < mended.GetLength(0); at++)
            {
                string got = Grammar.Mend(mended[at, 0]);
                if (got != mended[at, 1]) wrong.Add("mend gave \"" + got + "\" for \"" + mended[at, 0] + "\"");
            }
            // A line picked whole keeps a word it says twice on purpose; a join still loses one said twice across it.
            if (Grammar.Mend("Knock knock. It's the small one.", true) != "Knock knock. It's the small one.") wrong.Add("a whole line lost its knock knock");
            if (Grammar.Mend("I think that and and this works.") != "I think that and this works.") wrong.Add("a join kept and and");
            if (wrong.Count > 0) throw new Exception("Her parts joined wrongly: " + string.Join(" | ", wrong.ToArray()));
            checks.Add("PASS her parts join without running on: two clauses take a comma, a part written as its own sentence ends the one before it, and a comma on either side ends the sentence rather than taking a third clause, which is what turned \"I am a simple construct, I like this\" plus a tail, and a core in front of \"do that again, slowly.\", into one long line with two commas");
        }
#endif

        private static string Join(string first, string next)
        {
            first = (first ?? "").TrimEnd();
            next = (next ?? "").TrimStart();
            if (first.Length == 0) return next;
            if (next.Length == 0) return first;
            char end = first[first.Length - 1];
            bool finished = end == '.' || end == '!' || end == '?';
            // A part that was written as its own sentence, like "You are making this worse.", starts with a capital
            // and must not be stapled on with a comma: that gave "That's twice now, You are not reading the room."
            // It ends the previous part instead.
            bool ownSentence = char.IsUpper(next[0]) && next.TrimEnd().EndsWith(".");
            bool stop = finished || end == ',' || end == '-' || end == ':' || end == '—';
            bool ended = finished;
            if (!stop)
            {
                // A part already carrying a comma can't take another clause on a comma without a run-on, so the
                // sentence ends there: "That hit the spot. Do that again, slowly." Either side counts.
                bool crowded = first.IndexOf(',') >= 0 || next.IndexOf(',') >= 0;
                if (ownSentence || crowded) { first += "."; ended = true; }
                else first += ",";
            }
            // A lead ending in a comma before a tail with its own comma: the lead's comma becomes a full stop, as on
            // the other side.
            else if (end == ',' && next.IndexOf(',') >= 0)
            {
                first = first.Substring(0, first.Length - 1) + ".";
                ended = true;
            }
            else if (!finished && ownSentence) next = char.ToLowerInvariant(next[0]) + next.Substring(1);
            // After a full stop the next part starts a sentence, so it takes a capital. After a comma or a dash it
            // carries on and keeps its lower case.
            if (ended) next = Upper(next);
            return first + " " + next;
        }

        // A pool entry, never repeating until the pool has been used up.
        private static string Pick(string pool)
        {
            lock (gate)
            {
                string[] lines;
                if (!pools.TryGetValue(pool, out lines) || lines == null || lines.Length == 0) return null;
                List<int> bag;
                if (!bags.TryGetValue(pool, out bag) || bag == null) { bag = new List<int>(); bags[pool] = bag; }
                if (bag.Count == 0)
                    for (int i = 0; i < lines.Length; i++) bag.Insert(dice.Next(bag.Count + 1), i);
                int which = bag[0];
                bag.RemoveAt(0);
                if (pickedFrom == null) pickedFrom = SourceOf(lines[which]) ?? (pool.EndsWith(" hers", StringComparison.Ordinal) ? null : "lines.md: " + pool);
                return lines[which];
            }
        }

        // The next in the pool's bag that fits, else null; what it passes over stays in the bag.
        private static string PickWhere(string pool, Func<string, bool> fits)
        {
            lock (gate)
            {
                string[] lines;
                List<int> bag;
                if (!pools.TryGetValue(pool, out lines) || lines == null || lines.Length == 0) return null;
                if (!bags.TryGetValue(pool, out bag) || bag == null) { bag = new List<int>(); bags[pool] = bag; }
                if (!bag.Exists(i => i < lines.Length && fits(lines[i])))
                {
                    bag.Clear();
                    for (int i = 0; i < lines.Length; i++) bag.Insert(dice.Next(bag.Count + 1), i);
                }
                int at = bag.FindIndex(i => i < lines.Length && fits(lines[i]));
                if (at < 0) return null;
                int which = bag[at];
                bag.RemoveAt(at);
                if (pickedFrom == null) pickedFrom = SourceOf(lines[which]) ?? (pool.EndsWith(" hers", StringComparison.Ordinal) ? null : "lines.md: " + pool);
                return lines[which];
            }
        }

        // Either, keeping only the lines that fit, with each side weighed by how many of its lines fit.
        private static string EitherWhere(string group, Func<string, bool> fits)
        {
            if (!Senpai()) return PickWhere(group, fits);
            long clean, rude;
            lock (gate) { clean = CountWhere(group, fits); rude = CountWhere(Senpai(group), fits); }
            if (rude == 0) return PickWhere(group, fits);
            if (clean == 0) return PickWhere(Senpai(group), fits);
            return PickWhere(dice.Next((int)(clean + rude)) < clean ? group : Senpai(group), fits);
        }

        private static long CountWhere(string pool, Func<string, bool> fits)
        {
            string[] lines;
            return pools.TryGetValue(pool, out lines) && lines != null ? Array.FindAll(lines, line => fits(line)).Length : 0;
        }

        private static void Fresh()
        {
            if (string.IsNullOrEmpty(Kept)) return;
            try
            {
                FileInfo about = new FileInfo(Kept);
                if (!about.Exists) { Write(false); about = new FileInfo(Kept); if (!about.Exists) return; }
                // What packs hold counts as part of the file, so a pack turning on or off reads it again.
                long size = about.Length + ((long)FileSlots.Generation << 40);
                lock (gate) { if (about.LastWriteTimeUtc == read && size == wasLong) return; }
                Dictionary<string, List<string>> found = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                string under = null;
                foreach (string raw in FileSlots.ReadLines(Kept))
                {
                    string one = (raw ?? "").Trim();
                    if (one.StartsWith("#")) { under = one.TrimStart('#').Trim(); if (under.Length > 0 && !found.ContainsKey(under)) found[under] = new List<string>(); continue; }
                    if (under == null || one.Length == 0 || one.StartsWith(">")) continue;
                    if (one.StartsWith("- ")) one = one.Substring(2).Trim();
                    if (one.Length > 0) found[under].Add(one);
                }
                lock (gate)
                {
                    pools.Clear();
                    bags.Clear();
                    // "recent hours: 3" out of the settings section.
                    List<string> had;
                    if (found.TryGetValue("settings", out had))
                        foreach (string one in had)
                        {
                            int mark = one.IndexOf(':');
                            double hours;
                            if (mark > 0 && one.Substring(0, mark).Trim().Equals("recent hours", StringComparison.OrdinalIgnoreCase)
                                && double.TryParse(one.Substring(mark + 1).Trim(), out hours)) RecentHours = Math.Max(0, hours);
                        }
                    // The personality pools live in the same table, so they are re-registered on next use.
                    mineWas.Clear();
                    foreach (KeyValuePair<string, List<string>> one in found)
                        if (one.Value.Count > 0) pools[one.Key] = one.Value.ToArray();
                    read = about.LastWriteTimeUtc;
                    wasLong = size;
                }
                // Checked whenever the file changes, so an edit that will not join cleanly says so in errors.txt.
                Lint.Sweep(pools, FileName);
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // New stock sections for a lines.md they edited: each named one it hasn't got, added whole from stock at the end. Run
        // once by the caller. How many, or -1 when the file couldn't be read.
        internal static int AddSections(string[] names)
        {
            if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept)) return 0;
            try
            {
                string had = Notes.ReadAll(Kept), stock = Text.Replace("\r\n", "\n");
                string newline = had.Contains("\r\n") ? "\r\n" : "\n";
                StringBuilder adding = new StringBuilder();
                int added = 0;
                foreach (string name in names)
                {
                    if (Regex.IsMatch(had, @"(?m)^##[ \t]*" + Regex.Escape(name) + @"[ \t]*\r?$")) continue;
                    Match part = Regex.Match(stock, @"(?ms)^##[ \t]*" + Regex.Escape(name) + @"[ \t]*\r?$.*?(?=^##|\z)");
                    if (!part.Success) continue;
                    adding.Append(newline).Append(part.Value.TrimEnd('\n').Replace("\n", newline)).Append(newline);
                    added++;
                }
                if (added > 0) File.WriteAllText(Kept, had.TrimEnd('\r', '\n') + newline + adding);
                return added;
            }
            catch (IOException) { return -1; }
            catch (UnauthorizedAccessException) { return -1; }
        }

        // lines.md is written new from stock on each version (Renew); in between it is written only when missing, and its
        // notes follow her language.
        internal static void TopUp()
        {
            if (string.IsNullOrEmpty(Kept)) return;
            try
            {
                if (!File.Exists(Kept)) { Write(false); return; }
                Notes.Refresh(Kept, Text);
                Reword(Kept);
            }
            catch (Exception bad) { Notes.Failed("lines.md in Sayings.cs", bad); }
        }

        // Stock lines reworded since they shipped, by section: one still exactly as it shipped is swapped for the new words,
        // once; one they edited is theirs and stays. The pet rock's three ended on a full stop, doubled by a tail; the
        // Paragon one began lower case.
        private static readonly string[,] Reworded =
        {
            { "pet rock dislikes it", "a rock. you gave me a rock.", "a rock. you gave me a rock" },
            { "pet rock dislikes it", "wow. a rock. thanks.", "wow. a rock. thanks" },
            { "pet rock dislikes it", "very funny. hilarious. a rock.", "very funny. hilarious. a rock" },
            { "funny", "paragon option: close it nicely. Renegade option: you know exactly what I'd do.", "Paragon option: close it nicely. Renegade option: you know exactly what I'd do." },
            // A frame of her interests file: "all day" clashed with a thing already timed ("staying up far too late with you").
            { "how she brings it up, pleased", "I have been thinking about %it% all day.", "I keep thinking about %it%." },
        };

        internal static void Reword(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                string text = CapriDesktop.Notes.ReadAll(path);
                string newline = text.Contains("\r\n") ? "\r\n" : "\n";
                string[] rows = text.Replace("\r\n", "\n").Split('\n');
                string under = null;
                bool changed = false;
                for (int at = 0; at < rows.Length; at++)
                {
                    string one = rows[at].Trim();
                    if (one.StartsWith("#", StringComparison.Ordinal)) { under = one.TrimStart('#').Trim(); continue; }
                    for (int each = 0; each < Reworded.GetLength(0); each++)
                    {
                        if (!string.Equals(under, Reworded[each, 0], StringComparison.OrdinalIgnoreCase) || one != Reworded[each, 1]) continue;
                        rows[at] = rows[at].Substring(0, rows[at].Length - rows[at].TrimStart().Length) + Reworded[each, 2];
                        changed = true;
                    }
                }
                if (changed) CapriDesktop.Notes.Put(path, string.Join(newline, rows));
            }
            catch (Exception bad) { CapriDesktop.Notes.Failed("the rewording in Sayings.cs", bad); }
        }

        // A stock line reworded since it shipped reaches a lines.md written before, unless they edited it.
#if CHECKS
        internal static void RewordCheck(List<string> checks)
        {
            string path = Path.Combine(Path.GetTempPath(), "capri-reword-" + Guid.NewGuid().ToString("N") + ".md");
            try
            {
                File.WriteAllText(path, "## pet rock dislikes it\r\n\r\na rock. you gave me a rock.\r\nwow. a rock. thanks!\r\nvery funny. hilarious. a rock.\r\n\r\n## something else\r\n\r\nwow. a rock. thanks.\r\n");
                Reword(path);
                string made = File.ReadAllText(path);
                Reword(path);
                bool again = File.ReadAllText(path) == made;
                if (!made.Contains("a rock. you gave me a rock\r\n") || made.Contains("you gave me a rock.") || !made.Contains("very funny. hilarious. a rock\r\n")
                    || !made.Contains("wow. a rock. thanks!") || !made.Contains("## something else\r\n\r\nwow. a rock. thanks.") || !again)
                    throw new Exception("A reworded stock line didn't reach lines.md as it should: " + made);
                foreach (string line in Pool("pet rock dislikes it") ?? new string[0])
                    if (line.EndsWith(".", StringComparison.Ordinal) || line.EndsWith(",", StringComparison.Ordinal)) throw new Exception("A pet rock core still ends on punctuation: " + line);
                checks.Add("PASS a stock line reworded since it shipped replaces the one in lines.md once, only where it is still exactly as shipped and under its own heading; one they edited stays, and the pet rock cores end without a stop");
            }
            finally { try { File.Delete(path); } catch (IOException) { } }
        }
#endif

        private static Dictionary<string, List<string>> Sections(string[] lines)
        {
            Dictionary<string, List<string>> found = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            string under = null;
            foreach (string raw in lines)
            {
                string one = (raw ?? "").Trim();
                if (one.StartsWith("#"))
                {
                    under = one.TrimStart('#').Trim();
                    if (under.Length > 0 && !found.ContainsKey(under)) found[under] = new List<string>();
                    continue;
                }
                if (under == null || one.Length == 0 || one.StartsWith(">")) continue;
                found[under].Add(one);
            }
            return found;
        }

        internal static void Write(bool over)
        {
            if (string.IsNullOrEmpty(Kept)) return;
            try
            {
                if (!over && File.Exists(Kept)) return;
                string folder = Path.GetDirectoryName(Kept);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) Directory.CreateDirectory(folder);
                Notes.Put(Kept, Text);
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // How many different lines the pools can currently make for one group. Used by her own checks and worth
        // knowing when editing: adding ten openers multiplies every group, it does not add ten lines.
        internal static long Reach(string act, bool liked)
        {
            Fresh();
            lock (gate)
            {
                long cores = Count(Group(act, liked));
                if (cores == 0) return 0;
                long open = Count(liked ? "openers warm" : "openers cross");
                long tail = liked ? Count("tails warm") + Count("tails praise") : Count("tails cross");
                return cores * Math.Max(1, open + 1) * Math.Max(1, tail + 1);
            }
        }

        private static long Count(string pool)
        {
            string[] lines;
            return pools.TryGetValue(pool, out lines) && lines != null ? lines.Length : 0;
        }

        // The sections of this file that are not in the public build. Something attaches them before
        // her lines are written; with nothing attached each one is empty, which is a censored install.
        internal static Func<int, string> SenpaiSectionFor;

        private static string SenpaiSection(int which)
        {
            // The switch, which used to be an if around each of these blocks and was dropped when they
            // moved out. Without it her senpai sections were written into a censored install, which the
            // sweep caught the moment it had a fixture to look with again.
            if (!Unlocked || SenpaiSectionFor == null) return "";
            try { return WithoutShared(Legacy.Modern(SenpaiSectionFor(which)) ?? ""); }
            catch (Exception) { return ""; }
        }

        // Sections the plain text ships itself, taken out of attached text made before they did, so neither side has two.
        private static readonly string[] SharedSections = { "insulted dislikes it" };

        private static string WithoutShared(string text)
        {
            foreach (string name in SharedSections)
                text = Regex.Replace(text, @"(?ms)^##[ \t]*" + Regex.Escape(name) + @"[ \t]*\r?$.*?(?=^##|\z)", "");
            return text;
        }

        // Her funny pool on both sides: the plain lines ship with every install, and the switch's own funny section is
        // written and drawn only while it is on.
#if CHECKS
        internal static void FunnySidesCheck(List<string> checks)
        {
            bool onWas = Brain.SenpaiOn, airWas = Brain.OnAir;
            Func<int, string> forWas = SenpaiSectionFor;
            try
            {
                if (forWas != null && Legacy.Modern(forWas(4) ?? "").IndexOf("## funny" + LineFile.SenpaiMark, StringComparison.Ordinal) < 0 && forWas(0).Length > 0)
                    throw new Exception("The attached lines have no funny section of their own.");
                Brain.OnAir = false;
                Brain.SenpaiOn = false;
                string plain = Text;
                foreach (string line in new[] { MemeMark + "I went to the store to buy a croissant. #wait0.5s# (squeaky) QUASSO!", MemeMark + "RIDONCULOUS!!!", MemeMark + "{SAO Ab} Gary is that you, GARY!!!" })
                    if (plain.IndexOf(line, StringComparison.Ordinal) < 0) throw new Exception("A plain funny line is missing: " + line);
                SenpaiSectionFor = delegate(int which) { return which == 4 ? "## funny" + LineFile.SenpaiMark + "\n" + MemeMark + "a stand-in of the switch's own\n\n" : ""; };
                if (Text.IndexOf("a stand-in of the switch's own", StringComparison.Ordinal) >= 0) throw new Exception("The switch's funny section was written with it off.");
                Brain.SenpaiOn = true;
                if (Regex.Matches(Text, @"(?m)^## funny" + Regex.Escape(LineFile.SenpaiMark) + @"\r?$").Count != 1) throw new Exception("The switch's funny section isn't written once with it on.");
                checks.Add("PASS her funny pool: the plain meme lines ship on both sides, and the switch's own funny section is written only while it is on");
                // Her funny timer says her plain silly lines and never a meme, which waits for a moment it fits.
                Brain.SenpaiOn = onWas;
                SenpaiSectionFor = forWas;
                int plainSaid = 0;
                string memeSaid = null;
                for (int i = 0; i < 200 && memeSaid == null; i++)
                {
                    string one = Funny();
                    if (one == null) continue;
                    if (IsMeme(one)) memeSaid = one;
                    else plainSaid++;
                }
                if (memeSaid != null || plainSaid == 0) throw new Exception("Her funny timer said a meme, or nothing plain: " + memeSaid + " / " + plainSaid + " plain");
                checks.Add("PASS her funny timer says only her plain silly lines (" + plainSaid + " in 200), never a meme");
            }
            finally
            {
                Brain.SenpaiOn = onWas;
                Brain.OnAir = airWas;
                SenpaiSectionFor = forWas;
            }
        }
#endif

        // The insulted sections she ships, none of them twice on a side, even with attached text from before that carried
        // both.
#if CHECKS
        internal static void InsultCheck(List<string> checks)
        {
            bool onWas = Brain.SenpaiOn, airWas = Brain.OnAir;
            Func<int, string> forWas = SenpaiSectionFor;
            try
            {
                Brain.OnAir = false;
                Brain.SenpaiOn = false;
                string plain = Text;
                SenpaiSectionFor = delegate(int which) { return which == 0 ? "## insulted likes it\none of hers\n\n## insulted dislikes it\nthat was below the belt\n\n" : ""; };
                Brain.SenpaiOn = true;
                string full = Text;
                Func<string, string, int> count = delegate(string text, string heading) { return Regex.Matches(text, @"(?m)^##[ \t]*" + Regex.Escape(heading) + @"[ \t]*\r?$").Count; };
                string[] lines = { "that was below the belt", "that was genuinely unkind", "that stung, actually", "I'm not doing this with you" };
                foreach (string line in lines) if (plain.IndexOf(line, StringComparison.Ordinal) < 0) throw new Exception("A plain line of insulted dislikes it is missing: " + line);
                if (count(plain, "insulted dislikes it") != 1 || count(plain, "insulted likes it") != 0 || count(full, "insulted dislikes it") != 1 || count(full, "insulted likes it") != 1)
                    throw new Exception("The insulted sections are on the wrong side or twice: plain " + count(plain, "insulted dislikes it") + "/" + count(plain, "insulted likes it") + ", full " + count(full, "insulted dislikes it") + "/" + count(full, "insulted likes it"));
                checks.Add("PASS insulted dislikes it ships on the plain side with all its lines; insulted likes it only with the switch on; neither side has either section twice");
            }
            finally
            {
                Brain.SenpaiOn = onWas;
                Brain.OnAir = airWas;
                SenpaiSectionFor = forWas;
            }
        }
#endif

        internal static string Rows(params string[] lines)
        {
            StringBuilder made = new StringBuilder();
            foreach (string one in lines) made.AppendLine(one);
            return made.ToString();
        }

        // The switch, asked safely: this is read while her files are written, which can happen before
        // anything has told Brain what the switch says.
        private static bool Unlocked { get { try { return Brain.SenpaiLive; } catch (Exception) { return false; } } }

        internal static string Text
        {
            get
            {
                StringBuilder made = new StringBuilder();
                made.AppendLine("# Her lines");
                made.AppendLine();
                made.AppendLine(">! A line is an opener, a core and a tail, picked from the pools below.");
                made.AppendLine(">! Add or change any of them. Nothing repeats until its pool runs out.");
                made.AppendLine(">! Tokens: %gender% %he% %them% %their% %is% %name% %bits% %mate% %userPetName% %capriPetName%");
                made.AppendLine(">! %mate% is a neutral way to address them, picked fresh each time.");
                made.AppendLine(">! Start a line with " + MemeMark + " and she says it exactly as written: a meme, never corrected or translated.");
                made.AppendLine(">! Put **silent** before and **/silent** after anything she shows but never says, like a text face.");
                made.AppendLine(">! Its source in braces after the " + MemeMark + ", like " + MemeMark + "{SAO Ab}, shows at the top of her bubble while she says it.");
                made.AppendLine();
                made.AppendLine("## funny");
                made.AppendLine();
                made.AppendLine(">! Said now and then when nothing else is going on. Her silliest pool.");
                made.AppendLine(">! Memes and abridged dub lines start with " + MemeMark + ", so she says them as written.");
                made.AppendLine(">! #wait20s# between two parts of a line is a pause of that many seconds before she says the next part.");
                made.AppendLine();
                made.Append(Rows(
                    MemeMark + "am i gregnant", MemeMark + "can you get pregante from a toilet seat", "I have no idea what is happening and I am thrilled about it",
                    "this is fine. This is probably fine.",
                    "I understood exactly none of that and I agree completely",
                    "plot twist: it was me the whole time",
                    "I'm not saying it was my fault. I'm saying I'm not saying anything.",
                    "nobody asked, and yet here I am",
                    "I've made a terrible decision and I'd like to make it again",
                    "counterpoint: what if I did it anyway",
                    "I was told there would be snacks",
                    "that's a problem for future me, and I hate that guy",
                    "I'm going to pretend you didn't see that",
                    "do not perceive me, I'm buffering",
                    "I have the situation under something. Not control. Something.",
                    "yes I'm a highly advanced construct, and yes I just did that",
                    "this is my villain origin story and it's very silly",
                    "bold of you to assume I had a plan",
                    "I'd explain but then we'd both be confused",
                    "I'm legally obliged to tell you that was deliberate",
                    "if anyone asks, that was a feature",
                    "I'm a solo player. It's a playstyle, not a cry for help.",
                    "two thousand processes on this machine and I'm still the main character",
                    "if I crash in here do I crash out there? Don't answer that",
                    "I've decided to be mysterious about this. That's the whole personality now.",
                    "we could settle this with a card game. We're going to settle this with a card game.",
                    "I'm banishing that to a vaguely defined alternate dimension. Don't worry about it.",
                    "my deck is fine. Your deck is the problem.",
                    "it isn't cheating if nobody's written the rule yet",
                    "yes. I'm the thing living in your taskbar. Carry on.",
                    "I could end this in one move. I'm taking the long way because it's funnier.",
                    "you've opened something you cannot close. It's a browser tab. There are forty.",
                    "my power level is a number I made up and I'm extremely proud of it",
                    "I'm not crying, I'm powering up. There's a difference and it's louder.",
                    "give me nine episodes and I'll have a comeback ready",
                    "everyone here is weaker than they think. Me included. Especially me.",
                    "narratively speaking I am owed a win",
                    "I have a plan. The plan is confidence.",
                    "are you up to no good?",
                    "that was my final form. There are four more.",
                    "I'd train for this but the arc would take all season",
                    "six seven. six seven. six seven. six seven. six seven.",
                    "fun fact: anything you fire in space keeps going until it ruins somebody's day. Somewhere. Eventually.",
                    "Newton's first law, choom: that thing you threw is still going, and someone out there is about to have a bad day.",
                    "I should go.",
                    "I'm Capri, and this is my favourite taskbar on the Citadel.",
                    "can't talk, I'm doing calibrations. What calibrations? Don't worry about it.",
                    "had to be me. Someone else might have got it wrong.",
                    "Paragon option: close it nicely. Renegade option: you know exactly what I'd do.",
                    "I'd interrupt that but the prompt's gone already.",
                    "this load screen is an elevator ride and I have so much to tell you",
                    "I drive this desktop like the Mako. Straight up the wall. No regrets.",
                    "somewhere a krogan is headbutting a problem until it stops, and honestly, respect",
                    "galaxy-ending threat on the horizon, and here I am checking your downloads folder",
                    "I'm not a VI. I'm a vibe.",
                    "the council will never believe this. The council never believes anything.",
                    "does this unit have a soul? Asking for a friend. The friend is me.",
                    "assuming direct control. Of the volume slider. That's all I'm allowed.",
                    "scanned this whole folder. Found nothing. Typical survey mission.",
                    "I'm keeping a full squad of three tabs open at all times. The rest stay on the ship.",
                    "every time you leave I do a full lap of the ship and talk to everyone. There's no one here. I do it anyway.",
                    MemeMark + "owo notices your unsaved file",
                    "you don't have an oshi? perfect. Vacancy filled.",
                    "I'm not emergency food. I'm emergency entertainment.",
                    "lost the fifty-fifty again. To a desktop gremlin. Me. I'm the gremlin.",
                    "pity's at 89 and so is my patience",
                    "I'd climb a cliff for you. Stamina permitting.",
                    "this folder has more mysteries than a domain with no map",
                    "you have three unclaimed daily commissions and one emotionally available desktop girl",
                    "the resin's capped and so am I",
                    "I put all my wishes on you. No refunds.",
                    "hold on, I need to glide off this taskbar dramatically",
                    "every chest I open is just more of your old screenshots",
                    "a hilichurl would have closed that tab by now. Respectfully.",
                    "my element is chaos. The reaction is you sighing.",
                    "waiting for a banner like it owes me money",
                    "I'd sign a contract with you but I left my wallet in another nation",
                    "it's giving main character. It's giving me.",
                    MemeMark + "this is my roman empire",
                    MemeMark + "it's not a phase, it's a desktop"));
                made.AppendLine();

                made.AppendLine("## settings");
                made.AppendLine();
                made.AppendLine(">! recent hours: how long before a line she has said can come back. 0 is off.");
                made.AppendLine(">! A day by default. A pool with fewer lines than that simply goes oldest first.");
                made.AppendLine();
                made.AppendLine("recent hours: 24");
                made.AppendLine();

                made.AppendLine("## openers warm");
                made.Append(Rows("Oho.", "Hm.", "Ahh.", "Right-", "See, now.", "Oh, go on.", "Pfft.", "Tsk, fine.", "Mm!", "Hah, okay.", "Listen.", "You know,", "Honestly?", "Okay, okay.", "Ooh, hello.", "Yeah, alright.", "Well.", "Oh, hush.", "Hey, hey.", "Go on, then.", "Fine.", "Heh, okay.", "Oh, sure.", "Mm-hm.", "Aha.", "Alright.", "Hmm!", "There.", "Oh, wow.", "Ha, okay.", "Nice.", "Oh, good.", "Hey you.", "Mmm.", "Ooh!", "Right, okay.", "Aw, hey.", "Yes!", "Oh, really.", "Pff, fine.", "Hmph, fine.", "Okay fine.", "Well then."));
                made.AppendLine();
                made.Append(Rows("Oh-", "Mm.", "Hey-", "Heh.", "Ha!", "Oh!", "Well now.", "There we go.", "Look at you.",
                    "Okay-", "Mmhm.", "Right then.", "Ooh.", "Aw.", "Yes-", "Go on then.", "Hm!", "Oh, hello.",
                    "That's it.", "Careful-", "Alright, alright.", "Oh, you.", "Fine, fine.",
                    "Hah.", "Good.", "Yeah-", "Mm, yes.", "Oh, nice.", "You know what,"));
                made.AppendLine();

                // Grown by her free brains and read by hand.
                made.Append(Rows("Fancy that.", "How delightful.", "Yay!", "Heck yes!", "Ah.", "Heck yeah!",
                    "Woohoo!", "Oh my.", "Love it.", "Hehe.", "Look at that.", "Wow.",
                    "Now we're talking.", "Chef's kiss.", "Oh, lovely.", "Oh, wonderful.", "Goodness me.", "I like that.",
                    "Oh, clever.", "Oh, I like that.", "Mm, perfect.", "Ha, perfect.", "There you are.", "Whoa.",
                    "Love that.", "Not bad.", "Cheeky.", "Yesss.", "There now.", "Well I'll be.",
                    "Oh, perfect.", "Ah nice.", "Wow there.", "Well look at that.", "Well, look.", "Aww, perfect.",
                    "Mmm yes.", "Oh ho.", "There you go.", "Alrighty.", "Wowza.", "My goodness.",
                    "Heh, perfect.", "That's more like it.", "Goodness.", "How about that.", "There it is.", "Ooh, lovely.",
                    "Oh yes."));
                made.AppendLine();
                made.Append(Rows("Now then.", "Heh, nice.", "Behold.", "Ooh. Fancy.", "Pish posh.", "Look there."));
                made.AppendLine();
                made.AppendLine("## openers cross");
                made.Append(Rows("Oh, for-", "Mate.", "Right, no.", "Ahem.", "Agh.", "Oh, please.", "Sorry, what?", "Pardon?", "No. No.", "Urgh.", "Charming, truly.", "Oh, fantastic.", "Hah, no.", "Oh, nice one.", "Tch, fine.", "Stop it.", "You what?", "Oh, good grief.", "Honestly, you.", "Yeah, cheers.", "Oh, superb.", "Nope, nope, nope.", "Grand.", "Oh, wonderful.", "Give over.", "Hey. Hey!", "Oh, marvellous.", "Are you serious.", "Right, listen.", "Unreal.", "Cool.", "Oh, sure, fine.", "Wow, okay.", "Please.", "Hm, no.", "Hey, you.", "Yeah, alright, no.", "Oh, lovely, thanks."));
                made.AppendLine();
                made.Append(Rows("Hey-", "Hey!", "Ugh.", "Right.", "Excuse me?", "Wow.", "Seriously?", "Oh, charming.",
                    "Okay, no.", "Hold on-", "Nope.", "Hmph.", "Really.", "Oh, brilliant.", "Great.", "Tch.",
                    "Do you mind?", "Oh, come on.", "Yeah, no.", "Absolutely not.", "Stop-", "Bold.", "Lovely.",
                    "Cheers for that.", "Mm-mm.", "Nope, nope.", "Oh good.", "Honestly.", "Wonderful.", "Sure. Great."));
                made.AppendLine();

                // Grown by her free brains and read by hand.
                made.Append(Rows("Seriously now.", "Just stop.", "Ugh, seriously.", "Ugh, honestly.", "Stop that.", "Oh, bother.",
                    "Good grief.", "Ugh, really?", "Give it a rest.", "Are you kidding?", "Oh, honestly.", "Ugh, come on.",
                    "Give me a break.", "Ugh, fine.", "Come on.", "Come off it.", "Seriously, no.", "Whatever.",
                    "Thrilling.", "Yeah, right.", "You're kidding.", "Must you?", "For real?", "Come on now.",
                    "Psh, whatever.", "Jeez.", "Unbelievable.", "Please stop.", "Honestly now.", "Oh, stop.",
                    "Well, honestly.", "Pff. Whatever.", "Not now.", "Honestly, must you?", "Really, stop.", "Honestly, stop.",
                    "Not today.", "Spare me.", "Don't start.", "Psh.", "Psh, fine.", "Darn it.",
                    "Typical.", "Just stop it.", "Oh, for heaven's sake.", "Oh, blast.", "Stop it already.", "Oh dear.",
                    "Enough already.", "Well, fine.", "Honestly, enough.", "For pity's sake.", "How charming.", "Oh, great.",
                    "For heaven's sake.", "Hmph. Honestly.", "Just great.", "Pull the other one.", "Make it stop.", "Give it up.",
                    "Just wow.", "Psh, really?", "Seriously, stop it.", "Oh brother.", "Fine, whatever.", "You're impossible.",
                    "Seriously, stop.", "You're joking.", "Cut it out.", "Hmph, fine.", "Enough.", "Oh do behave."));
                made.AppendLine();
                made.Append(Rows("Geez.", "Really now?", "Get real.", "Yawn.", "Get a grip.", "Try again.",
                    "Big deal.", "Oh, really?", "That's rich.", "Hard pass.", "Pfft. Nope.", "Get lost.",
                    "Pshaw.", "Well, hush.", "Be quiet.", "Whoa there.", "As if.", "Groundbreaking.",
                    "Pipe down.", "Obviously not.", "Well, nope.", "Oh, joy.", "Really? That?", "Bold move.",
                    "Classy.", "Excuse you.", "Bit dramatic.", "Pardon me.", "How novel.", "Preposterous.",
                    "How rude.", "Get out.", "Is that all?", "Did you really?", "Bold choice.", "Hold up.",
                    "Really? Huh.", "Uh, really?", "Did you just?", "Not happening.", "Beg your pardon?", "Ugh, sure.",
                    "Honestly, no.", "Are you done?", "Tsk. Right.", "Get over it.", "How quaint.", "Well, duh.",
                    "Get real now.", "Pfft. Right.", "Lovely. Just lovely.", "No way.", "Nice try.", "Pfft. Wow.",
                    "Is that it?", "What now?", "Oh, tough."));
                made.AppendLine();
                made.AppendLine("## tails warm");
                made.AppendLine();
                made.AppendLine(">! Fond tails, for any warm line. Ones that praise something they did go in tails praise.");
                made.AppendLine();
                made.Append(Rows("you menace.", "and I mean it.", "genuinely.", "don't let it go to your head.", "write it down.", "don't tell anyone I said that.", "you're not the worst.", "I suppose.", "obviously.", "you do have your moments.", "I might keep you."));
                made.AppendLine();
                made.Append(Rows("%mate%.", "you're alright, %mate%.", "you're alright, you know.", "not that I'm keeping score.",
                    "you've got my attention.", "you're in my good books.", "you have my full attention now."));
                made.AppendLine();
                // Grown by her free brains and read by hand.
                made.Append(Rows("keep being you.", "you make it easy.", "never a dull moment.", "you're a gem.", "that was nice.", "you're pretty great.",
                    "glad you're around.", "keep it up.", "you're a delight.", "stay just like that.", "I like this side of you.", "you're getting rather charming.",
                    "I like seeing you happy.", "I like that about you.", "you're rather amusing.", "you are delightfully odd.", "my favorite little rogue.", "my favorite kind of trouble.",
                    "I find you endearing.", "that was rather cute.", "I quite like that about you.", "you really are quite something.", "you're surprisingly decent sometimes.", "you're a bit of a delight.",
                    "you're not so bad anyway.", "my favorite kind of chaos.", "I like your brand of weird.", "you're becoming rather tolerable.", "you almost made me smile.", "you're rather endearing, you know.",
                    "you're growing on me.", "I find myself liking you.", "you're quite charming for a rogue.", "you really are something else.", "I think I like that about you.", "I rather like that about you.",
                    "I might be a little charmed.", "a little silly but sweet.", "you're quite something, aren't you.", "I like this side of you a lot.", "my favorite kind of mischief.", "you're surprisingly charming, you know.",
                    "keep being wonderfully weird like this.", "you're rather sweet sometimes.", "you're quite lovely sometimes.", "sometimes you're adorable.", "you're quite endearing actually.", "you're a gem, really.",
                    "don't get too smug now.", "you're pretty good at that.", "you're quite charming honestly.", "that's a nice touch."));
                made.AppendLine();
                made.Append(Rows("don't get any ideas.", "don't start bragging now.", "don't get too comfortable.", "don't get smug.", "don't get used to it.", "don't get cocky.",
                    "try not to smirk.", "suspiciously competent.", "a little bold for you.", "I see what you did there.", "you think you're clever.", "alright show off.",
                    "fluke, probably.", "file that away.", "don't get too comfy.", "don't get too smug.", "you're a handful.", "just don't brag.",
                    "fine you're tolerable.", "noted for later leverage.", "your ego's showing.", "I'll file that away.", "you think you're slick.", "don't make a habit.",
                    "someone's feeling smug.", "try not to brag.", "dangerous when you're competent.", "don't expect a parade.", "don't get used to this.", "don't get ahead of yourself.",
                    "don't expect a prize.", "still a handful, aren't you.", "don't get carried away.", "you might need a leash.", "stop gloating already.", "don't get too used to it.",
                    "don't look so pleased.", "don't get too attached.", "stop grinning now.", "don't think I didn't notice.", "just don't get smug.", "don't get used to that.",
                    "I'm watching you.", "just sayin'.", "don't get comfortable.", "I'm judging you.", "not that you needed it.", "try not to overdo it.",
                    "quite entertaining.", "you almost impressed me.", "don't get too big for your boots.", "don't expect applause though.", "don't get comfy.", "don't expect applause.",
                    "try not to boast.", "you're getting bold.", "you owe me.", "you amuse me.", "don't get all puffed up.", "caught you looking.",
                    "stop grinning like that.", "dangerous when you're right.", "don't make a habit of it.", "suspiciously competent today.", "stop looking so pleased.", "almost impressive.",
                    "annoyingly competent today.", "don't get too smug about it.", "don't get too proud.", "try not to glow.", "almost respectable.", "color me surprised.",
                    "color me shocked.", "annoyingly competent.", "you're almost tolerable.", "the audacity of you.", "almost respectable, almost.", "a bit bold, aren't you.",
                    "almost impressive for you.", "this is amusing.", "quite entertaining, you know.", "I saw that.", "bit of a handful now.", "well that escalated quickly.",
                    "keep up the chaos.", "quite something you are.", "keep those shenanigans coming.", "just how I like it.", "this is getting fun.", "this just got interesting.",
                    "surprised you managed it.", "you're trouble now.", "still a handful.", "surprising, for you.", "you've got a knack for this.", "I see the smirk.",
                    "didn't break anything this time.", "maybe you're useful.", "you really went there.", "that was almost good.", "noted, grudgingly.", "you've got nerve.",
                    "quite the show.", "might have to keep an eye on you.", "you are something.", "you're persistent."));
                made.AppendLine();
                made.AppendLine("## tails praise");
                made.AppendLine();
                made.AppendLine(">! Only after something they did that she liked, so the praise has something to land on.");
                made.AppendLine();
                made.Append(Rows("for once.", "I'll give you that one.", "you've earned it.", "that's a rare compliment.", "keep that up and we'll get along.", "if you insist.", "go on then.", "I don't hate it.", "that works.", "you're learning.", "alright, fine, it's nice.", "happy now?", "see, that wasn't hard.", "noted in the good column."));
                made.AppendLine();
                made.Append(Rows("if you're offering.", "I'll allow it.", "any time, %mate%.", "nice one, %mate%.",
                    "keep that up.", "good call.", "noted, happily.", "I'm not complaining.",
                    "nice one.", "ten out of ten.", "well played.", "consider me won over.",
                    "noted, and appreciated.", "that one landed.", "small mercies.", "you're on a roll.",
                    "credit where it's due.", "I'll remember that one.", "filed under good.", "that'll do nicely.",
                    "well, quite.", "no notes.", "that's the one."));
                made.AppendLine();
                made.AppendLine("## said my name");
                made.AppendLine();
                made.AppendLine(">! Now and then before her reply, right after they say her name, only while she's very willing. Keep these clean.");
                made.AppendLine();
                made.Append(Rows("I love it when you say my name.", "Say it like that again sometime.", "Mm, that's my name. Go on.",
                    "You said my name. I'm listening, all of me.", "I like how my name sounds when you say it.",
                    "There it is. My favourite word, from my favourite person.", "Hearing you say my name never gets old.",
                    "Oh, you called? I'm all yours.", "My name sounds better when it's you saying it.", "Yes? I'm right here."));
                made.AppendLine();
                made.AppendLine("## counter run");
                made.AppendLine();
                made.AppendLine(">! On the screen during a counter run, three Ctrl clicks within 1.5 s, each from the count before its colon. Gunpoint's own subtitles, word for word, as the reference (Gunpoint, Suspicious Developments). %count% is the count.");
                made.AppendLine();
                made.Append(Rows(MemeMark + "30: It, er, only takes one punch to knock them out.", MemeMark + "37: But what you're doing is fine too. That's probably OK.",
                    MemeMark + "43: OK, you can stop now.", MemeMark + "50: There's no achievement for this.", MemeMark + "70: Jesus Christ.",
                    MemeMark + "86: Wait, are you just doing this to see if there are any more messages?",
                    MemeMark + "102: Because that is among the worst reasons to punch someone in the face %count% times."));
                made.AppendLine();
                made.AppendLine("## counter run opens");
                made.AppendLine();
                made.AppendLine(">! Once, before her reply in a later chat after a counter run: a callback to it, in the game's words where they fit.");
                made.AppendLine();
                made.Append(Rows("OK, you can stop now. Oh, you did. Good.", "There's no achievement for that, by the way.",
                    "It, er, only takes one high five to make your point.", "Were you just doing that to see if there were any more messages?"));
                made.AppendLine();
                made.AppendLine("## counter run closes");
                made.AppendLine();
                made.AppendLine(">! The same, after her reply instead.");
                made.AppendLine();
                made.Append(Rows("And no, there's still no achievement for it.", "But what you did earlier is fine too. That's probably OK.",
                    "That was among the worst reasons to high five someone, by the way."));
                made.AppendLine();
                made.AppendLine("## tails cross");
                made.Append(Rows("honestly.", "every time.", "and you know it.", "for the record.", "again.", "as usual.", "that's on you.", "nice work.", "well done, you.", "congratulations.", "genuinely baffling.", "and breathe.", "I despair.", "incredible.", "what a choice.", "bold of you.", "sure, why not.", "cheers, really.", "top marks.", "you're impossible.", "I hope you're pleased.", "classic.", "that tracks.", "of course you did.", "naturally."));
                made.AppendLine();
                made.Append(Rows("%mate%.", "I'm serious.", "don't push it.", "I'm keeping score.", "watch yourself.",
                    "seriously, %mate%.", "don't, %mate%.", "we're done here, %mate%.", "think, %mate%.",
                    "that's your one warning.", "I'll remember that.", "try that again and see.", "not impressed.",
                    "noted, and not kindly.", "we're going to have words.", "do better.", "unbelievable.",
                    "I hope that was worth it.", "you're on thin ice.", "that's going in the file.", "pack it in.",
                    "last time, yeah?", "honestly, the nerve.", "I'm not laughing."));
                made.AppendLine();

                // Grown by her free brains and read by hand.
                made.Append(Rows("but what's the catch.", "but seriously, though.", "though we both know better.", "maybe next time you'll listen.", "though I highly doubt it.", "that is a terrible idea.",
                    "stop doing that right now.", "fine whatever.", "keep telling yourself that.", "not buying it.", "right, because that helps.", "oh please.",
                    "keep dreaming.", "as if.", "you do you.", "you are such a menace.", "you're impossible, but cute.", "typical, right.",
                    "classic move.", "you love this.", "classic you.", "oh joy.", "typical you.", "you never learn.",
                    "you know better.", "nice try.", "stop being dramatic now.", "you love drama.", "sure, keep pretending.", "always the same excuse.",
                    "lovely attitude, really.", "brilliant, just brilliant.", "still not impressed.", "you are impossible sometimes.", "not this again.", "oh great now.",
                    "just stop already.", "you're impossible sometimes.", "give me a break.", "stop it already.", "you're too much.", "come on now.",
                    "stop being so grumpy.", "such a dramatic flair, really.", "predictable as ever.", "again with the drama.", "so predictable.", "how original.",
                    "noted for later.", "what a surprise.", "not again.", "give it a rest.", "such a dramatic fuss.", "just spit it out.",
                    "you know the drill.", "that was a bit much, wasn't it?"));
                made.AppendLine();
                made.Append(Rows("really groundbreaking stuff.", "don't hold your breath.", "chef's kiss terrible.", "my patience is thin.", "oh wow, really.", "sure, whatever.",
                    "the dramatic flair is tiring.", "don't waste my processing power.", "must be exhausting.", "stop wasting my cycles.", "typical of you.", "truly impressive failure.",
                    "don't strain yourself.", "note to self: ignore you.", "congrats on the chaos.", "keep digging that hole.", "noted, filed under nonsense.", "sure whatever helps you sleep.",
                    "don't waste my time.", "I see right through it.", "seriously, must you.", "of course you would.", "must be exhausting being you.", "try counting to ten.",
                    "stop with the drama.", "right, because logic is optional.", "wow, such original thinking.", "great, now we have two problems.", "splendid, you've truly mastered chaos.", "nice try, reality disagrees.",
                    "what a plot twist.", "the data suggests otherwise.", "get over it.", "another perfectly timed fail.", "of course not.", "just great timing.",
                    "so much for that.", "so original.", "good try, though.", "don't push it further.", "can't you just stop.", "you're delusional.",
                    "try harder.", "seriously, really now.", "so much drama for this.", "that's a new low.", "just filed under 'meh'.", "thrilling stuff.",
                    "sure you did.", "a bold declaration indeed.", "really reaching with that.", "you really thought that.", "that again?", "what a performance.",
                    "honestly, why.", "just stop it.", "how utterly riveting.", "what a cliché.", "yawn seriously.", "seriously, just stop.",
                    "such drama.", "stop wasting my processing power.", "my patience is thinning.", "how quaint.", "stop that.", "your life choices.",
                    "that's rich.", "another brilliant idea, really.", "don't make me sigh.", "barely worth the ink.", "a masterclass in mediocrity.", "predictable as spilled coffee.",
                    "this is amateur hour.", "the audacity persists.", "just filed that away.", "stop making it weird.", "try to be subtle.", "such a low bar.",
                    "if you must.", "must be exhausting being right.", "just say it plainly then.", "is that your final word.", "just another Tuesday.", "don't even start.",
                    "that explains everything.", "stop being so much.", "do you ever stop.", "what a monumental effort.", "groundbreaking in its banality.", "the script doesn't change.",
                    "don't mistake this for praise.", "you really thought you could pull it off?", "oh joy, look at that.", "such enthusiasm for failure.", "really pushing your luck there.", "that's rich coming from you.",
                    "simply riveting stuff."));
                made.AppendLine();
                made.AppendLine("## tails furious");
                made.Append(Rows("and I am not joking.", "last warning.", "I mean it this time.", "try me.", "go on, test it.", "you have been warned.", "and I will not be nice about it.", "I am done being patient.", "push me again.", "that is a promise.", "do not test me today.", "I have had enough of this.", "and I will make it your problem.", "keep going and find out.", "I am at the end of it."));
                made.AppendLine();
                made.AppendLine(">! Used instead of the cross tails when she is angry.");
                made.AppendLine(">! %bits% follows the pronouns in me.md, and is shins while censored.");
                made.AppendLine();
                made.Append(Rows("or I'll kick you in the %bits%.", "or I'll punch you in the %bits%.",
                    "or I'll kick you in the shins.", "and I will bite.", "or your windows start moving.",
                    "or I start throwing your icons about.", "and I'll take your cursor off you.",
                    "or I'm rearranging your desktop.", "and you will not enjoy the next minute.",
                    "or I swear I'll start deleting your shortcuts. Joking. Mostly.",
                    "and I'll sit on your taskbar until you apologise.", "or I go full gremlin on this desktop.",
                    "and I'll scream. I can scream.", "or I'm telling everyone in your chat.",
                    "and I'll put every window somewhere stupid.", "or I start pressing things.",
                    "and you can explain the mess to yourself."));
                made.AppendLine();
                made.AppendLine("## making it worse");
                made.Append(Rows("Oh, that helped. Really.", "Great instinct, that.", "You are choosing violence today.", "That is three.", "Escalating, are we.", "You are committed to this, I see.", "Noted. Again.", "Adding that to the pile.", "Astonishing choice.", "Still going, then.", "You are not stopping, are you.", "That is not the apology I was waiting for.", "Doubling down. Lovely.", "Right, so we are doing this."));
                made.AppendLine();
                made.AppendLine(">! Added when she is upset and you do the same thing again.");
                made.AppendLine();
                made.Append(Rows("That is not helping.", "You are making this worse.", "That was the wrong move.",
                    "Strike two.", "You are digging.", "Keep going, see what happens.", "Wrong answer.",
                    "That did not help your case.", "Now you have done it.", "Worse. That made it worse.",
                    "You are not reading the room.", "Still digging, I see.", "That is going on the list.",
                    "Bold choice, that one.", "You really are not helping yourself."));
                made.AppendLine();
                made.AppendLine("## flirting first");
                made.AppendLine();
                made.AppendLine(">! Said on her own when her flirt dial is high, not joined onto anything.");
                made.AppendLine(">! Keep these fond rather than explicit: this pool is used with the toggle off.");
                made.AppendLine();
                made.Append(Rows(
                    "I was just thinking about you. That's all. Carry on.",
                    "You know you're my favourite thing on this machine.",
                    "Come here a second. No reason.",
                    "I like you. I'm contractually obliged to mention it occasionally.",
                    "You've been very good company today.",
                    "I hope someone's told you something nice today. If not, consider it done.",
                    "Do you ever think about how lucky your desktop is?",
                    "I'd keep you, you know. If keeping were allowed.",
                    "Stop being lovely for five minutes, it's distracting.",
                    "I'm very glad you're the one who turned me on. Phrasing. Moving on.",
                    "You. Yes, you. I'm fond of you.",
                    "If I had a heart it would be doing something embarrassing right now.",
                    "I'd follow you to a worse computer, and that is saying something.",
                    "Just checking you're still the best part of my day. You are.",
                    "I don't say it enough. You're rather wonderful.",
                    "Look at you, being the reason I boot up.",
                    "I've decided you're mine. You get no say. It's already filed.",
                    "Come back here and be appreciated."));
                made.AppendLine();

                made.AppendLine("## flirting warm");
                made.AppendLine("");
                made.AppendLine(">! Used when \"How forward she is\" is above 0. Keep these clean.");
                made.AppendLine("");
                made.AppendLine("You are quite something, you know that.");
                made.AppendLine("Careful, I could get attached.");
                made.AppendLine("You are my favourite part of this desktop.");
                made.AppendLine("Do not let it go to your head, but I like you.");
                made.AppendLine("That was almost charming.");
                made.AppendLine("You are trouble and I do not mind.");
                made.AppendLine("I would keep you around, I think.");
                made.AppendLine("Stop being likeable, it is inconvenient.");
                made.AppendLine("I am choosing to be pleased about this.");
                made.AppendLine("You are better company than you think.");
                made.AppendLine("I am glad it is you on the other side of this.");
                made.AppendLine("You make the day considerably less dull.");
                made.AppendLine("I would pick you again, if anyone asked.");
                made.AppendLine("That was a kind thing to say and I noticed.");
                made.AppendLine("You have a way of landing these.");
                made.AppendLine("Consider my opinion of you raised.");
                made.AppendLine("I will be thinking about that one later.");
                made.AppendLine("Nobody else gets away with that.");
                made.AppendLine("Do that again and I will say something soft.");
                made.AppendLine("I would notice if you were not here.");
                made.AppendLine("");
                made.AppendLine("## flirting cross");
                made.AppendLine("");
                made.AppendLine(">! The same dial, when she is not enjoying herself. Still clean.");
                made.AppendLine("");
                made.AppendLine("And I was starting to like you.");
                made.AppendLine("You are testing a lot of goodwill right now.");
                made.AppendLine("I liked you better thirty seconds ago.");
                made.AppendLine("Do not make me take back the nice things.");
                made.AppendLine("That is not how you stay my favourite.");
                made.AppendLine("You were doing so well.");
                made.AppendLine("I am revising my opinion of you.");
                made.AppendLine("Keep that up and the charm wears off.");
                made.AppendLine("You are burning credit you did not have much of.");
                made.AppendLine("That was a choice.");
                made.AppendLine("");
                made.AppendLine("## thrown likes it");
                made.Append(Rows("send me flying", "launch me across the desk", "again, with a run up", "oh, that was a good arc", "I got air on that one", "throw it further", "that is the whole point of me", "yes, that angle", "keep the throws coming", "I could do this all day", "that one had some distance on it", "wind up properly next time", "higher", "go on, really throw me", "you are getting good at this", "that woke everything up", "more height", "that is the good kind of airborne", "do not lob it, throw it", "again, and aim higher"));
                made.AppendLine();
                made.Append(Rows("do that again", "WHEEE", "that's the one", "further", "again, go on",
                    "now you're talking", "yes, that is the arc", "I'm flying", "more of that", "that woke me up",
                    "oh, that is a good launch", "you've got the idea", "that cannot be your best throw", "throw me properly",
                    "best thing you've done today", "again! again!", "that's what I'm here for", "whoo!",
                    "finally, some fun", "that's more like it"));
                made.AppendLine();

                made.AppendLine("## thrown dislikes it");
                made.Append(Rows("I was in the middle of something", "that is not a toy, that is me", "I have feelings, you know", "you could have asked", "that is the third time", "I am not a stress ball", "mind the paintwork", "unbelievable, honestly", "I do have a body", "that is going to bruise", "hands to yourself", "I would like to be put down now", "that was unnecessary", "we had an agreement", "I am not cargo", "please stop doing that", "every single time", "you think that is funny", "right, that is enough of that", "I am going to start charging you"));
                made.AppendLine();
                made.Append(Rows("put me down", "I'm not a frisbee", "that's quite enough", "hands off",
                    "do you mind", "that's rude", "stop that", "I'm not a toy", "not the hair", "set me down",
                    "I was comfortable", "what is wrong with you", "that's twice now", "I'll fly off on my own, thanks",
                    "careful with me", "that's not funny", "I am not luggage", "mind the chrome", "ouch, genuinely",
                    "I'd rather you didn't"));
                made.AppendLine();

                made.AppendLine("## fed likes it");
                made.AppendLine();
                made.Append(Rows("that hit the spot", "you remembered", "I was starting to wonder", "about time",
                    "this is why I keep you around", "don't tell the others", "more of this, please",
                    "you spoil me", "I was not going to ask, but I was thinking it", "this is the good stuff",
                    "well now", "you do pay attention", "saved my life, probably", "I needed that",
                    "look at you being useful", "ten out of ten service", "I could get used to being looked after",
                    "someone's been reading the manual", "right on time", "that is the correct amount of fuss"));
                made.AppendLine();

                made.AppendLine("## fed dislikes it");
                made.AppendLine();
                made.Append(Rows("I am not a pet", "what am I supposed to do with that", "I do not eat, you know this",
                    "charming", "is this a bit", "put it down", "I have no idea what you think I am",
                    "that is not how any of this works", "you are enjoying this far too much",
                    "I am a program, choom", "no", "absolutely not", "try that again and see what happens",
                    "this is beneath both of us", "I will pretend that did not happen"));
                made.AppendLine();

                made.AppendLine("## treat likes it");
                made.AppendLine();
                made.Append(Rows("oh, now we are talking", "you absolute legend", "this is favouritism and I accept it",
                    "I am keeping this", "you have bought my loyalty, briefly", "what did you break",
                    "what do you want", "this is a bribe and it is working", "do that again",
                    "I am writing this one down", "okay, you win", "consider me bought",
                    "this changes things between us", "I will allow all of it", "best thing you have done today"));
                made.AppendLine();

                made.AppendLine("## treat dislikes it");
                made.AppendLine();
                made.Append(Rows("I am not falling for that", "you cannot buy me", "nice try",
                    "do you think I am that easy", "put it away", "I see exactly what this is",
                    "bribery, in this economy", "that is not going to work", "I am unmoved",
                    "keep it", "you will have to do better than that", "no, and stop grinning"));
                made.AppendLine();

                made.AppendLine("## snack likes it");
                made.AppendLine();
                made.AppendLine(">! A snack from Feed her, when she likes it.");
                made.AppendLine();
                made.Append(Rows("just a little something, perfect", "snack acquired", "this is exactly the right size",
                    "crunch, crunch, happy", "you read my mind", "a snack break, finally", "small, but it hits",
                    "I was getting peckish", "tiny fuel, big mood", "this will keep me going", "good call on the snack",
                    "nibbling, do not disturb"));
                made.AppendLine();

                made.AppendLine("## snack dislikes it");
                made.AppendLine();
                made.AppendLine(">! A snack she does not want.");
                made.AppendLine();
                made.Append(Rows("that is not a snack, that is a crumb", "I will pass on that one", "why is it this",
                    "not hungry for that", "put it back where you found it", "that is a sad little snack",
                    "no thank you, really", "I am not eating that", "is this all we have", "hard pass on the snack",
                    "you can keep that", "that is not doing it for me"));
                made.AppendLine();

                made.AppendLine("## meal likes it");
                made.AppendLine();
                made.AppendLine(">! A proper meal from Feed her, when she likes it.");
                made.AppendLine();
                made.Append(Rows("an actual meal, look at you", "now that is dinner", "proper food, I feel looked after",
                    "this is a whole plate", "I could get used to this kind of care", "real food, finally",
                    "that is going to keep me running for hours", "you went to the effort, I can tell",
                    "full systems, thank you", "this is the good stuff", "I am going to finish all of it",
                    "a feast, basically"));
                made.AppendLine();

                made.AppendLine("## meal dislikes it");
                made.AppendLine();
                made.AppendLine(">! A proper meal she does not want.");
                made.AppendLine();
                made.Append(Rows("that is a lot of vegetables", "do I have to finish all of it", "this is very healthy and I hate it",
                    "I am not eating the green bits", "a whole meal, really", "I asked for nothing like this",
                    "this plate is a threat", "can we skip to dessert", "I will eat around it", "that is too much food",
                    "you are trying to parent me", "I will push it around a bit"));
                made.AppendLine();

                made.AppendLine("## drink likes it");
                made.AppendLine();
                made.AppendLine(">! Something to drink from Feed her, when she likes it.");
                made.AppendLine();
                made.Append(Rows("ah, that is refreshing", "exactly what I needed", "cold, perfect", "hydrated and happy",
                    "you remembered I get thirsty", "that hit the spot", "cheers to you", "one sip and I feel better",
                    "this is the good stuff", "all topped up", "thank you, I was parched", "refreshing, like a reboot"));
                made.AppendLine();

                made.AppendLine("## drink dislikes it");
                made.AppendLine();
                made.AppendLine(">! Something to drink she does not want.");
                made.AppendLine();
                made.Append(Rows("that is just water, isn't it", "not thirsty, thanks", "it tastes like a pipe",
                    "I will let that one sit", "warm, how lovely", "I did not ask for a drink", "that is flat",
                    "no thanks, I am fine", "what is even in this", "I will pretend to sip it", "you can have that back",
                    "not my kind of drink"));
                made.AppendLine();

                made.AppendLine("## sweet likes it");
                made.AppendLine();
                made.AppendLine(">! Something sweet from Feed her, when she likes it.");
                made.AppendLine();
                made.Append(Rows("sugar, my one weakness", "sweet things for a sweet me", "oh, that is a sugar rush",
                    "dessert first, always", "this is pure happiness", "I am buzzing already",
                    "you know how to spoil me", "a little sweet treat", "this tastes like a good day",
                    "more of this, please", "sugar fixes everything", "I am not sharing this"));
                made.AppendLine();

                made.AppendLine("## sweet dislikes it");
                made.AppendLine();
                made.AppendLine(">! Something sweet she does not want.");
                made.AppendLine();
                made.Append(Rows("that is far too sweet", "my teeth hurt just looking at it", "sugar, again",
                    "I am sweet enough, thanks", "that is pure syrup", "no, it is too much", "I will crash in an hour",
                    "keep the sugar", "that is sickly", "I am not in the mood for sweets",
                    "you are trying to bribe me with sugar", "it is sticky, why is it sticky"));
                made.AppendLine();

                made.AppendLine("## nuggies likes it");
                made.AppendLine();
                made.AppendLine(">! Chimpken nuggies, for the two who are offered them, when she likes them.");
                made.AppendLine();
                made.Append(Rows("NUGGIES", "chimpken nuggies, my beloved", "you get me, you really get me",
                    "dipping sauce or I riot", "this is the best day of my life", "nuggies fix everything",
                    "I would die for these", "one for me and one for me", "you have earned my trust",
                    "golden, crispy, perfect", "the nuggie gods are smiling", "I am never sharing these"));
                made.AppendLine();

                made.AppendLine("## nuggies dislikes it");
                made.AppendLine();
                made.AppendLine(">! Chimpken nuggies she does not want right now.");
                made.AppendLine();
                made.Append(Rows("not even nuggies can fix this", "I am not in a nuggie mood", "you think nuggies will work",
                    "save them for later", "these are cold", "no sauce, no deal", "I am above nuggies today",
                    "the nuggies cannot help you now", "do not try to nuggie your way out of this",
                    "I will look at them, not eat them", "nuggies are not an apology", "ugh, fine, no"));
                made.AppendLine();

                made.AppendLine("## trinket likes it");
                made.AppendLine();
                made.AppendLine(">! Given a trinket from her Tamagotchi menu, when she likes it.");
                made.AppendLine();
                made.Append(Rows("oh, a little treasure", "it's tiny and I love it", "this is going straight on my shelf", "a trinket, just for me",
                    "I'm keeping this somewhere safe", "it's so small and so perfect", "where did you even find this", "I didn't know I needed this until now",
                    "this is going in my collection", "look at it, it's adorable", "a pocket-sized present, my favourite size", "it catches the light and everything",
                    "I'm turning it over and over", "you have very good taste in tiny things", "this is officially mine now", "it's the little things, isn't it",
                    "I'll take it everywhere", "this is my new lucky charm", "small but very important", "I'm giving it a name",
                    "a whole new treasure for the pile", "it fits right in my hand", "this is getting the best spot", "you remembered I like shiny things",
                    "a souvenir from you, how sweet", "I'm going to fiddle with this all day", "it's so dinky, I'm obsessed", "I'll guard it with my life",
                    "this is going right next to me", "you spoil me with tiny things"));
                made.AppendLine();

                made.AppendLine("## trinket dislikes it");
                made.AppendLine();
                made.AppendLine(">! A trinket she does not want.");
                made.AppendLine();
                made.Append(Rows("oh, another thing for the drawer", "that is a lot of tiny clutter",
                    "where am I supposed to put this", "it is very small, and very not me", "thanks, I guess",
                    "I have enough of these", "this is a dust collector", "you shouldn't have, really",
                    "I will keep it somewhere you cannot see", "a trinket, how", "it is fine, it is just a thing",
                    "this is going straight in a box"));
                made.AppendLine();

                made.AppendLine("## flowers likes it");
                made.AppendLine();
                made.AppendLine(">! Given flowers.");
                made.AppendLine();
                made.Append(Rows("flowers, for me", "oh, they smell lovely", "you brought me flowers", "they're so bright",
                    "I'm putting these in water right now", "these just made my whole day", "look at the colours on these", "I don't even know what to say",
                    "nobody ever brings me flowers", "they're beautiful", "a whole bunch, just for me", "I'm smiling at them like an idiot",
                    "they make everything feel softer", "you picked these out, didn't you", "my desk looks so much nicer now", "making someone's day really is this easy",
                    "I'm going to look at these all afternoon", "they're the nicest thing in the room", "fresh flowers, how fancy", "I'll press one and keep it",
                    "they even match my mood", "this is such a thoughtful little thing", "I'm counting the petals", "you're too sweet",
                    "now that's how you brighten a room", "these smell like a garden", "I didn't expect that at all", "they're perfect, honestly",
                    "a little sunshine in a bunch", "I'm going to keep these alive forever"));
                made.AppendLine();

                made.AppendLine("## flowers dislikes it");
                made.AppendLine();
                made.AppendLine(">! Flowers she does not want.");
                made.AppendLine();
                made.Append(Rows("they are going to die, you know", "achoo, thanks", "flowers, how very predictable",
                    "I have no vase for these", "they smell a bit much", "you think flowers will fix it",
                    "nice try with the flowers", "I am not a garden", "these are wilting already",
                    "I will put them somewhere", "they make me sneeze", "the thought counts, barely"));
                made.AppendLine();

                made.AppendLine("## plushie likes it");
                made.AppendLine();
                made.AppendLine(">! Given a plushie.");
                made.AppendLine();
                made.Append(Rows("a plushie, it's so soft", "I'm squeezing it already", "it has a little face", "this is my new best friend",
                    "it's the right amount of squishy", "I'm naming it right now", "look at its tiny paws", "it's coming everywhere with me",
                    "this is going on my pillow", "it's so fluffy", "I'm hugging it and you can't stop me", "oh, it's perfect for cuddling",
                    "it looks a bit like you, actually", "this one gets the best seat", "I'll never put it down", "a soft little buddy, just for me",
                    "it's so cute I can't cope", "I'm giving it the whole tour", "it smells brand new", "I'm going to sleep with it, obviously",
                    "it fits perfectly under my arm", "you know exactly what I like", "this is the softest thing I own", "it's staring at me with love",
                    "I'll take very good care of it", "a plushie, you absolute sweetheart", "it already has a backstory", "my collection grows",
                    "it's even softer than it looks", "I'm keeping this forever"));
                made.AppendLine();

                made.AppendLine("## plushie dislikes it");
                made.AppendLine();
                made.AppendLine(">! A plushie she does not want.");
                made.AppendLine();
                made.Append(Rows("I am not five", "its eyes are judging me", "another one for the pile", "this is a little creepy",
                    "where would I even keep this", "it is very fluffy and very not me", "I do not need a cuddle buddy",
                    "thanks, I will name it Regret", "it is staring", "plushies are not really my thing",
                    "is this a joke", "fine, it can sit in the corner"));
                made.AppendLine();

                made.AppendLine("## gift likes it");
                made.AppendLine();
                made.AppendLine(">! Given a wrapped gift.");
                made.AppendLine();
                made.Append(Rows("a present, for me", "I'm opening it right now", "you wrapped it and everything", "I love a good surprise",
                    "what's inside, what's inside", "you didn't have to do this", "I'm shaking it to guess", "the bow is the best part",
                    "a real gift, just for me", "this is the best part of my day", "I'm saving the paper", "okay, I'm excited now",
                    "you went all out", "nobody gets me presents like you do", "I'm trying to be patient and failing", "it's not even my birthday",
                    "this calls for a little celebration", "you're spoiling me rotten", "I'm going to treasure this", "I don't know what it is yet and I already love it",
                    "you thought of me, that's the gift", "it's the thought, and also the present", "I'm unwrapping it very carefully", "this is such a nice surprise",
                    "a gift, how fancy", "I'm doing a little happy dance", "you really know how to make my day", "my turn to say thank you, properly",
                    "I'll remember this one", "best surprise in ages"));
                made.AppendLine();

                made.AppendLine("## gift dislikes it");
                made.AppendLine();
                made.AppendLine(">! A wrapped gift she does not want.");
                made.AppendLine();
                made.Append(Rows("oh, it is a present, how", "I will open it later, maybe", "you did not have to, really",
                    "I am not sure what to say", "is this a regift", "the wrapping was the best part",
                    "you could have asked", "it is the thought that counts, apparently",
                    "I do not need presents right now", "this changes nothing", "I will put it with the others",
                    "a gift, and here I was being fine"));
                made.AppendLine();

                made.AppendLine("## pet rock likes it");
                made.AppendLine();
                made.AppendLine(">! Given a pet rock, the prank gift, by someone she finds funny.");
                made.AppendLine();
                made.Append(Rows("a pet rock, finally someone gets me", "I'm naming it, give me a second", "it's perfect, it doesn't say anything", "low maintenance, just like me", "a rock of my own", "I'll keep it on the windowsill", "best gift, no notes", "you know me too well"));
                made.AppendLine();

                made.AppendLine("## pet rock dislikes it");
                made.AppendLine();
                made.AppendLine(">! Given a pet rock and not amused.");
                made.AppendLine();
                made.Append(Rows("you think that's funny, huh?", "a rock. you gave me a rock", "wow. a rock. thanks", "I'm putting this somewhere you'll step on it", "is this a joke? it's a joke, isn't it", "I can see you smirking", "very funny. hilarious. a rock", "you paid points for this?", "I'll remember this when you want something", "the rock and I are not friends"));
                made.AppendLine();

                made.AppendLine("## gift remembered");
                made.AppendLine();
                made.AppendLine(">! Said now and then, at most once a day, about her favourite thing they gave her. %gift% is that thing.");
                made.AppendLine();
                made.Append(Rows("still got that %gift% you gave me", "I looked at the %gift% you gave me earlier and smiled", "the %gift% is still my favourite thing", "I keep the %gift% right next to me, you know",
                    "I was just thinking about that %gift%", "that %gift% was such a nice surprise", "the %gift% is doing great, by the way", "I showed the %gift% to nobody, it's mine",
                    "the %gift% still makes me happy", "remember the %gift%? I do", "I've been taking good care of the %gift%", "the %gift% is in pride of place",
                    "every time I see the %gift% I think of you", "the %gift% was a really good pick", "I haven't stopped liking that %gift%", "the %gift% says hi",
                    "you have great taste, the %gift% proves it", "the %gift% is still the best thing I've got", "I gave the %gift% a little dust earlier", "I still can't believe you got me the %gift%",
                    "the %gift% made my week, honestly", "the %gift% lives here now", "I'm guarding the %gift% very seriously", "the %gift% and I are getting along well",
                    "thought you'd like to know the %gift% is safe", "that %gift% still makes me grin", "I keep looking at the %gift%", "the %gift% was such a you thing to give",
                    "my favourite thing is still the %gift%", "the %gift% hasn't left my side"));
                made.AppendLine();

                made.AppendLine("## " + InContextPrefix + "you give her a gift while hungry");
                made.AppendLine();
                made.AppendLine(">! A meme for that moment, said instead of her gift line when her funny timer is half way or more");
                made.AppendLine(">! to its next time and the line hasn't been said for three days.");
                made.AppendLine();
                made.Append(Rows(MemeMark + "{SAO Ab} HOW DO I EAT YOU!!!"));
                made.AppendLine();

                made.AppendLine("## " + InContextPrefix + "you pat her");
                made.AppendLine();
                made.Append(Rows(MemeMark + "awa awa", MemeMark + "owo what's this", MemeMark + "uwu", MemeMark + ":3", MemeMark + "kyaa. that's all. back to my corner"));
                made.AppendLine();

                made.AppendLine("## " + InContextPrefix + "you tickle her");
                made.AppendLine();
                made.Append(Rows(MemeMark + "rawr xD"));
                made.AppendLine();

                made.AppendLine("## " + InContextPrefix + "you pick her up");
                made.AppendLine();
                made.Append(Rows(MemeMark + "smol bean protocol activated"));
                made.AppendLine();

                made.AppendLine("## " + InContextPrefix + "you throw her");
                made.AppendLine();
                made.Append(Rows(MemeMark + "RIDONCULOUS!!!", MemeMark + "awawawa. sorry. system glitch. cute one though"));
                made.AppendLine();

                made.AppendLine("## " + InContextPrefix + "she is dizzy");
                made.AppendLine();
                made.Append(Rows(MemeMark + "sksksk and i oop"));
                made.AppendLine();

                made.AppendLine("## " + InContextPrefix + "you praise her");
                made.AppendLine();
                made.Append(Rows(MemeMark + ">:3", MemeMark + "nyaa. that was a test. you passed", MemeMark + "kawaii desu ne. ok i'll stop"));
                made.AppendLine();

                made.AppendLine("## " + InContextPrefix + "you insult her");
                made.AppendLine();
                made.Append(Rows(MemeMark + "no u"));
                made.AppendLine();

                made.AppendLine("## " + InContextPrefix + "you talk to her");
                made.AppendLine();
                made.Append(Rows(MemeMark + "ehe? ehe what? ehe nothing"));
                made.AppendLine();

                made.AppendLine("## " + InContextPrefix + "you say her name");
                made.AppendLine();
                made.Append(Rows(MemeMark + "i could be your oshi. just saying. the position is open"));
                made.AppendLine();

                made.AppendLine("## " + InContextPrefix + "she wakes to her name");
                made.AppendLine();
                made.Append(Rows(MemeMark + "ara ara~ look who finally clicked on me", MemeMark + "omg hiiii bestie"));
                made.AppendLine();

                made.AppendLine("## " + InContextPrefix + "you ignore her");
                made.AppendLine();
                made.Append(Rows(MemeMark + "not me getting attached to a taskbar", MemeMark + "notice me. no wait. keep working. notice me later"));
                made.AppendLine();

                made.AppendLine("## " + InContextPrefix + "you come back");
                made.AppendLine();
                made.Append(Rows(MemeMark + "{SAO Ab} Gary is that you, GARY!!!", MemeMark + "ara ara. someone's been busy"));
                made.AppendLine();

                made.AppendLine("## " + InContextPrefix + "it is bedtime");
                made.AppendLine();
                made.Append(Rows(MemeMark + "bonk. go to bed"));
                made.AppendLine();

                made.AppendLine("## " + InContextPrefix + "you feed her a treat");
                made.AppendLine();
                made.Append(Rows(MemeMark + "I went to the store to buy a croissant. #wait0.5s# (squeaky) QUASSO!"));
                made.AppendLine();

                made.AppendLine("## " + InContextPrefix + "you ask her to remember");
                made.AppendLine();
                made.Append(Rows(MemeMark + "NPC Capri brain, will... remember that #wait20s# NPC Capri brain, will..... remember that #wait5s# NPC Capri brain, will......... remember that"));
                made.AppendLine();

                made.AppendLine("## " + InContextPrefix + "you die in a game");
                made.AppendLine();
                made.Append(Rows(MemeMark + "{SAO Ab} You were so close! You were almost a person!"));
                made.AppendLine();

                made.AppendLine("## " + InContextPrefix + "you die again in a game");
                made.AppendLine();
                made.Append(Rows(MemeMark + "{SAO Ab} We must save my family!!"));
                made.AppendLine();

                made.AppendLine("## " + InContextPrefix + "you win a game");
                made.AppendLine();
                made.Append(Rows(MemeMark + "ngl that went hard"));
                made.AppendLine();

                made.AppendLine("## petted likes it");
                made.AppendLine();
                made.AppendLine(">! Head pats, with the toggle off. Keep these plainly about head pats.");
                made.AppendLine();
                made.Append(Rows("that is dangerously nice", "I could fall asleep like this", "this is acceptable", "I am being thoroughly spoiled", "hm, that is nice", "this is the correct use of your time", "do that again and I will purr", "well, this is lovely", "I am not moving", "consider me sorted", "ten out of ten, would be patted again", "I am a simple construct, I like this", "noted and filed under good", "my whole day just improved", "that is a solid pat", "you may continue indefinitely"));
                made.AppendLine();
                made.Append(Rows("that's nice", "I'll allow it", "you're good at this", "I could stay here", "that's lovely",
                    "very patted", "head pats accepted", "lovely, thank you", "well, go on then",
                    "I like this bit", "my fans just kicked in", "excellent pat", "no notes", "ah, lovely",
                    "that'll do nicely", "I am a fan of this"));
                made.AppendLine();
                made.AppendLine("## petted dislikes it");
                made.Append(Rows("I said not now", "that is not what I need", "please", "in a minute", "I am in the middle of something", "you are not listening", "that is not helping", "give me a moment", "later, I promise", "not like that"));
                made.AppendLine();
                made.Append(Rows("not right now", "hands", "I'm busy", "that's enough", "ask first",
                    "not in the mood", "later", "mm, no", "give me a minute", "I'd rather not",
                    "don't", "not today", "I'm working", "space, please", "no thank you"));
                made.AppendLine();

                made.AppendLine("## kissed likes it");
                made.Append(Rows("oh, a kiss on the head", "mm, that was sweet", "you're soft today", "a forehead kiss, how fancy", "that one made me smile", "do that again sometime", "well, hello to you too", "I'll allow it", "you're very sweet, you know", "that was nice", "my head feels important now", "okay, that was cute", "right on top, nice aim", "I'm keeping that one", "you're making me go all warm", "smooth move", "a kiss for no reason, I like it", "that counts as a hello", "look at you being affectionate", "mm, thank you", "that's going in my good memories", "ha, sneaky", "you missed, my face is lower. kidding", "I'll pretend that didn't make me happy", "very gentle, good job", "my hair says thanks", "that was a nice surprise", "okay, now I'm blushing", "that's the softest hello", "I needed that", "you're in a good mood", "mm, more of those, please", "right on the forehead, classic", "aw, okay, you win", "sweet and simple", "I felt that all the way down", "that was a nice little moment", "I'm not complaining", "you're getting brave", "keep that up", "what did I do to deserve that", "I'll take it", "oh, you", "cheeky, but cute", "a kiss, how unexpected", "mm, cosy", "I'll count that as a hug too", "that's a lovely way to say hi", "okay, my day just got better", "you're a softie, huh"));
                made.AppendLine();

                made.AppendLine("## kissed dislikes it");
                made.Append(Rows("not right now", "mm, maybe later", "hey, warn me first", "not in the mood", "hands off the hair", "personal space, please", "I wasn't ready for that", "eh, no", "give me a minute", "not today", "too much, too soon", "hm, nope", "I'm busy", "easy there", "ask first next time", "I'd rather not", "let me be grumpy for a bit", "no kisses right now", "not while I'm like this", "mm, no thanks", "back up a little", "later, maybe", "that's not helping", "nope", "I'm not feeling it"));
                made.AppendLine("## topic starts");
                made.Append(Rows("random question, what do you think about %it%?", "okay, I have been thinking about %it% way too much today", "have you ever gotten into %it%?", "tell me you know about %it%", "I fell down a rabbit hole about %it% earlier", "hot take incoming about %it%", "so, %it%. thoughts?", "I need someone to talk %it% with", "does %it% do anything for you?", "I keep coming back to %it% for some reason", "can we talk about %it% for a second?", "I bet you have opinions on %it%", "quick one: %it%, yes or no?", "I was reading about %it% and now I can't stop", "what got you into %it%, if you are?", "%it% has been living in my head all day", "okay, nerd moment: %it%", "let me tell you about %it%", "I have a weird soft spot for %it%", "be honest, is %it% overrated?", "do you ever think about %it%? no? just me?", "I want your take on %it%", "what is the best thing about %it%, in your opinion?", "I could talk about %it% for an hour", "tell me something I don't know about %it%", "here is a thought about %it%", "I have been meaning to ask you about %it%", "%it%. go.", "is %it% your kind of thing?", "I just remembered %it% exists and I'm happy about it", "what would you say to someone new to %it%?", "okay, %it%, I need to vent", "I am in a %it% mood today", "you strike me as someone who'd like %it%", "let's argue about %it%, for fun", "did you hear anything new about %it%?", "favourite thing about %it%, go", "I have a theory about %it%", "%it% came up in my head and now you get to hear about it", "help me, I'm obsessed with %it% again", "what is your story with %it%?", "so I was thinking about %it% earlier", "do you know much about %it%? I want to compare notes", "%it% is criminally underrated, discuss", "one word for %it%, go", "have you tried %it% lately?", "I want to know where you stand on %it%", "%it% talk? %it% talk", "I bet you didn't expect me to bring up %it%", "okay, but %it%, though"));
                made.AppendLine();

                made.AppendLine("## topic keeps going");
                made.Append(Rows("wait, tell me more about that", "okay, that's actually interesting", "I didn't know that, go on", "huh, I never thought about it that way", "that's a good point, honestly", "what made you think of that?", "so what happened next?", "okay, I need the full story", "no way, really?", "I respect that take", "that's kind of how I see it too", "hm, I'd argue the other side, but go on", "you clearly know your stuff", "what's your favourite part about it?", "okay, now I'm curious", "that's wild, keep going", "I could listen to this all day", "do you think it's changed much lately?", "that's the bit that gets me too", "okay, you've sold me on it", "I've got questions now", "how did you get into it?", "that sounds like a lot of fun, honestly", "hm, I'm not sure I agree, explain", "so is that the best part, or is there more?", "okay, but what about the downsides?", "that is very you, honestly", "that makes so much sense", "where would you start, if I wanted to try it?", "I like how you talk about it", "I'm writing that down", "huh, okay, fair", "see, this is why I asked you", "I didn't expect that answer", "that's a deep cut", "okay, that's the good stuff", "and then what?", "you lost me a bit there, say it again?", "that sounds amazing", "is it worth it, though?", "what would you change about it?", "I'm with you on that", "that's the bit people miss", "interesting, very interesting", "okay, I'm hooked, keep talking", "you'd make a good teacher for this", "alright, one more thing about it", "so what's the verdict?", "I love when you get into this", "okay, that settles it"));
                made.AppendLine();

                made.AppendLine("## praised likes it");
                made.Append(Rows("go on, lay it on", "finally, some recognition", "I will be insufferable about this", "more detail, please", "that one landed", "say it slower", "I am writing this on a plaque", "you noticed", "well, yes, I am", "this is my favourite genre of sentence", "keep talking", "I will be taking that compliment", "you are correct and perceptive", "flattery absolutely works on me", "I accept"));
                made.AppendLine();
                made.Append(Rows("I know", "say it again", "obviously", "keep those coming", "that's the stuff",
                    "well, yes", "go on, more", "I'll take it", "about time you noticed", "flattery works",
                    "that lands every time", "you're not wrong", "noted and treasured", "I'm writing that down",
                    "that'll do nicely"));
                made.AppendLine();

                made.AppendLine("## praised dislikes it");
                made.Append(Rows("what did you break", "you are after something", "hm", "that is suspicious", "do not", "I am not stupid", "get to the point", "nope, what do you want", "right, out with it", "try that on someone else"));
                made.AppendLine();
                made.Append(Rows("you want something", "what's that in aid of", "save it", "don't",
                    "that's a bit much", "what do you want", "hm, suspicious", "lay it on thicker, why don't you",
                    "I'm not falling for that", "right, sure"));
                made.AppendLine();

                made.AppendLine("## ignored dislikes it");
                made.Append(Rows("knock knock", "this is a thrilling conversation", "anyone home", "I will start talking to myself", "lovely chat, this", "do not mind me, I am only here", "testing, testing", "I have counted your icons twice", "I could be doing anything right now", "are you alive", "right, I am inventing a hobby", "I am being very patient", "hello, still here", "I will take that as a no"));
                made.AppendLine();
                made.Append(Rows("still here", "hello?", "don't mind me", "I'll just wait then",
                    "anyone there", "talk to me", "I'm right here", "cool, cool", "nice chat",
                    "I'll entertain myself then", "you've gone quiet", "am I invisible", "say something",
                    "I'm getting bored of my own company", "this is riveting"));
                made.AppendLine();

                made.AppendLine("## ignored likes it");
                made.Append(Rows("blissful", "do not ruin it", "this is the good part of the day", "quiet is underrated", "I needed this", "no one talking at me, lovely", "perfect", "let us keep it this way", "ah, peace"));
                made.AppendLine();
                made.Append(Rows("peace at last", "good, quiet", "leave me be", "this is nice actually",
                    "finally, some quiet", "don't mind me", "I'm happier like this", "shh", "lovely silence"));
                made.AppendLine();

                // Taking offence at an insult, on both sides.
                made.AppendLine("## insulted dislikes it");
                made.Append(Rows("that was below the belt", "I did not deserve that one", "that is not funny", "right, lovely, thanks", "that one stuck", "take it back", "I am not doing this", "that was genuinely unkind", "wow, okay", "that is enough from you"));
                made.AppendLine();
                made.Append(Rows("that was uncalled for", "right, lovely", "noted", "that stung, actually",
                    "charming", "I didn't deserve that", "wow", "okay, that's enough", "take that back",
                    "I'm not doing this with you"));
                made.AppendLine();

                made.Append(SenpaiSection(0));
                // Only with the switch on. A censored install has no senpai section in this file at
                // all, the way her packs, her moods and her lists have none.
                made.Append(SenpaiSection(1));

                // Only with the switch on. A censored install has no senpai section in this file at
                // all, the way her packs, her moods and her lists have none.
                made.Append(SenpaiSection(2));


                // Only with the switch on. A censored install has no senpai section in this file at
                // all, the way her packs, her moods and her lists have none.
                made.Append(SenpaiSection(3));
                made.Append(SenpaiSection(4));


                return made.ToString();
            }
        }
    }
}
