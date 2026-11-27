using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // The other side's own set on her Topics page, names only in the side's data. A few to start by her lean; each grows
    // as she talks, a % to 100 with this week's count, and pulls its neighbours in; never all of them.
    internal static class OwnSet
    {
        // The side's data, tab-separated rows: title, note, item, near, lean, turns (an act moved at 100), opposite, switch,
        // yours, theirs (their words that say one) and does (their act that says one).
        internal static string SenpaiSet = "";
        internal const int Start = 3;
        internal const double Joins = 8, OwnGrowth = 0.25, NamedGrowth = 1, StartLevel = 4;
        private static readonly object gate = new object();
        private static string parsedFrom;
        private static string title, note, yours, switcher, begs;
        // From the side's data: how she is with one past each mark, and the moods she can be all in.
        private static readonly SortedDictionary<double, string> tiers = new SortedDictionary<double, string>();
        private static readonly List<string> fits = new List<string>();
        private static readonly List<string> items = new List<string>();
        private static readonly Dictionary<string, Dictionary<string, double>> near = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Dictionary<string, double>> leans = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Dictionary<string, double>> turns = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Dictionary<string, double>> opposite = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<KeyValuePair<string, Regex>> theirs = new List<KeyValuePair<string, Regex>>();
        private static readonly Dictionary<string, List<string>> does = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        // Theirs, kept beside hers under this name, whoever she is.
        internal const string Yours = "~you";
        internal const int Week = 7;
        // For the checks: the day it is, and how willing she is.
        internal static Func<DateTime> Today = delegate { return SimTime.Now.Date; };
        internal static Func<int> Willing = delegate { return Mood.Willing; };
        // For the checks: a pick instead of a die.
        internal static Random Dice = new Random();

        private static void Parse()
        {
            string text = SenpaiSet ?? "";
            if (text == parsedFrom) return;
            parsedFrom = text;
            title = note = yours = switcher = begs = null;
            tiers.Clear();
            fits.Clear();
            items.Clear();
            near.Clear();
            leans.Clear();
            turns.Clear();
            opposite.Clear();
            theirs.Clear();
            does.Clear();
            foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                string[] p = raw.Split('\t');
                double w;
                if (p.Length == 2 && p[0] == "title") title = p[1].Trim();
                else if (p.Length == 2 && p[0] == "note") note = p[1].Trim();
                else if (p.Length == 2 && p[0] == "item" && p[1].Trim().Length > 0) items.Add(p[1].Trim());
                else if (p.Length == 4 && p[0] == "near" && double.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out w)) { Link(near, p[1], p[2], w); Link(near, p[2], p[1], w); }
                else if (p.Length == 4 && p[0] == "lean" && double.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out w)) Link(leans, p[1], p[2], w);
                else if (p.Length == 4 && p[0] == "turns" && double.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out w)) Link(turns, p[2], p[1], w);
                else if (p.Length == 3 && p[0] == "opposite") { Link(opposite, p[1], p[2], 1); Link(opposite, p[2], p[1], 1); }
                else if (p.Length == 2 && p[0] == "switch") switcher = p[1].Trim();
                else if (p.Length == 3 && p[0] == "tier" && double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out w)) tiers[w] = p[2].Trim();
                else if (p.Length == 2 && p[0] == "begs") begs = p[1].Trim();
                else if (p.Length == 2 && p[0] == "fits") fits.Add(p[1].Trim());
                else if (p.Length == 2 && p[0] == "yours") yours = p[1].Trim();
                else if (p.Length == 3 && p[0] == "theirs")
                {
                    try { theirs.Add(new KeyValuePair<string, Regex>(p[1].Trim(), new Regex(@"(?<![\p{L}\p{N}])(?:" + p[2].Trim() + @")(?![\p{L}\p{N}])", RegexOptions.IgnoreCase))); }
                    catch (ArgumentException) { }
                }
                else if (p.Length == 3 && p[0] == "does")
                {
                    List<string> into;
                    if (!does.TryGetValue(p[1].Trim(), out into)) does[p[1].Trim()] = into = new List<string>();
                    into.Add(p[2].Trim());
                }
            }
        }

        private static void Link(Dictionary<string, Dictionary<string, double>> table, string a, string b, double w)
        {
            Dictionary<string, double> row;
            if (!table.TryGetValue(a.Trim(), out row)) table[a.Trim()] = row = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            row[b.Trim()] = w;
        }

        // Only on her other side, and only with its data there.
        internal static bool Shown { get { lock (gate) { Parse(); return Brain.SenpaiLive && title != null && items.Count > 0; } } }
        internal static string Title { get { lock (gate) { Parse(); return title ?? ""; } } }
        internal static string YoursTitle { get { lock (gate) { Parse(); return yours ?? ""; } } }
        internal static string[] Items { get { lock (gate) { Parse(); return items.ToArray(); } } }

        // ---------- What she keeps: "personality|name|level|in" a row, in the side's own folder ----------

        private sealed class Row
        {
            internal string Name;
            internal double Level;
            internal bool In, Out, Hand;
            // Day number to how many; only the last week is kept.
            internal readonly SortedDictionary<int, int> Days = new SortedDictionary<int, int>();
            // The day it was last done for; 0 is never, and one never done for doesn't fall.
            internal int Last;
        }

        private static int DayOf(DateTime day) { return (int)(day.Date.Ticks / TimeSpan.TicksPerDay); }

        private static int Recent(Row row)
        {
            int today = DayOf(Today()), many = 0;
            foreach (KeyValuePair<int, int> one in row.Days) if (one.Key > today - Week) many += one.Value;
            return many;
        }

        // Something was done for it today: counted, and the days past the week let go.
        private static void Count(Row row)
        {
            int today = DayOf(Today()), had;
            row.Days.TryGetValue(today, out had);
            row.Days[today] = had + 1;
            row.Last = today;
            List<int> old = new List<int>();
            foreach (int day in row.Days.Keys) if (day <= today - Week) old.Add(day);
            foreach (int day in old) row.Days.Remove(day);
        }

        private static void Grow(Row row, double by) { row.Level = Math.Min(Most, row.Level + by); }

        // An opposite of one already hers stays out of a pull, unless she has the one that can be both.
        private static bool Blocked(Dictionary<string, Row> rows, string name)
        {
            Dictionary<string, double> other;
            if (!opposite.TryGetValue(name, out other)) return false;
            Row both;
            if (switcher != null && rows.TryGetValue(switcher, out both) && both.In) return false;
            foreach (string one in other.Keys) { Row row; if (rows.TryGetValue(one, out row) && row.In) return true; }
            return false;
        }

        internal static Func<string> Folder = delegate { Func<string> f = Expansion.Folder; return f == null ? null : f(); };

        private static string Kept { get { string root = Folder(); return string.IsNullOrEmpty(root) ? null : Path.Combine(root, "own set.txt"); } }

        private static Dictionary<string, Row> Read(string personality)
        {
            Parse();
            Dictionary<string, Row> rows = new Dictionary<string, Row>(StringComparer.OrdinalIgnoreCase);
            string path = Kept;
            if (path == null || !File.Exists(path)) return rows;
            foreach (string line in Notes.ReadLines(path))
            {
                string[] p = line.Split('|');
                double level;
                if (p.Length < 4 || p.Length > 6 || !string.Equals(p[0], personality, StringComparison.OrdinalIgnoreCase) || !double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out level)) continue;
                // One typed as "Name 100%" before a % could be set is that one at that %.
                Match typed = Regex.Match(p[1], @"^(.+?)\s+(\d{1,3})\s*%$");
                if (typed.Success && items.Exists(one => string.Equals(one, typed.Groups[1].Value.Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    p[1] = ItemNamed(typed.Groups[1].Value.Trim());
                    level = Math.Min(Most, double.Parse(typed.Groups[2].Value, CultureInfo.InvariantCulture));
                }
                Row row = rows[p[1]] = new Row { Name = p[1], Level = Math.Min(Most, level), In = p[3] == "in" || p[3] == "hand", Out = p[3] == "out", Hand = p[3] == "hand" };
                if (p.Length == 6) int.TryParse(p[5], out row.Last);
                if (p.Length >= 5)
                    foreach (string day in p[4].Split(','))
                    {
                        string[] d = day.Split(':');
                        int at, many;
                        if (d.Length == 2 && int.TryParse(d[0], out at) && int.TryParse(d[1], out many)) row.Days[at] = many;
                    }
            }
            return rows;
        }

        private static void Write(string personality, Dictionary<string, Row> rows)
        {
            string path = Kept;
            if (path == null) return;
            List<string> lines = new List<string>();
            if (File.Exists(path)) foreach (string line in Notes.ReadLines(path)) if (!line.StartsWith(personality + "|", StringComparison.OrdinalIgnoreCase)) lines.Add(line);
            foreach (Row one in rows.Values)
            {
                List<string> days = new List<string>();
                foreach (KeyValuePair<int, int> day in one.Days) days.Add(day.Key + ":" + day.Value);
                lines.Add(personality + "|" + one.Name + "|" + one.Level.ToString("0.##", CultureInfo.InvariantCulture) + "|" + (one.In && one.Hand ? "hand" : one.In ? "in" : one.Out ? "out" : "-") + (days.Count > 0 || one.Last > 0 ? "|" + string.Join(",", days.ToArray()) : "") + (one.Last > 0 ? "|" + one.Last : ""));
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            Notes.WriteAll(path, string.Join("\n", lines.ToArray()) + "\n");
        }

        // Hers for a personality, strongest first, rolled the first time: a few, weighted by the personality's leaning.
        internal static List<KeyValuePair<string, double>> Hers(string personality)
        {
            List<KeyValuePair<string, double>> hers = new List<KeyValuePair<string, double>>();
            if (string.IsNullOrEmpty(personality)) return hers;
            lock (gate)
            {
                Parse();
                if (items.Count == 0) return hers;
                Dictionary<string, Row> rows = Read(personality);
                if (rows.Count == 0 && personality != Yours)
                {
                    foreach (string one in Roll(personality, null, Start, null)) rows[one] = new Row { Name = one, Level = StartLevel, In = true };
                    Write(personality, rows);
                }
                foreach (Row one in rows.Values) if (one.In) hers.Add(new KeyValuePair<string, double>(one.Name, one.Level));
            }
            hers.Sort(delegate(KeyValuePair<string, double> a, KeyValuePair<string, double> b) { return b.Value.CompareTo(a.Value); });
            return hers;
        }

        // Once a day she runs: one not done for in a week falls a 21st of the way, never under where she starts, so it
        // fades over 3 weeks of her running until it's used again. Days she is closed don't count.
        internal static void Fall()
        {
            lock (gate)
            {
                string path = Kept;
                if (path == null || !File.Exists(path)) return;
                int today = DayOf(Today()), fell;
                string mark = Path.Combine(Path.GetDirectoryName(path), "own set fell.txt");
                if (File.Exists(mark) && int.TryParse(File.ReadAllText(mark).Trim(), out fell) && fell == today) return;
                File.WriteAllText(mark, today.ToString(CultureInfo.InvariantCulture));
                List<string> lines = new List<string>();
                foreach (string line in Notes.ReadLines(path))
                {
                    string[] p = line.Split('|');
                    double level;
                    int last;
                    if (p.Length == 6 && int.TryParse(p[5], out last) && last > 0 && today - last >= Week && double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out level) && level > StartLevel)
                    {
                        p[2] = Math.Max(StartLevel, level - Most / 21).ToString("0.##", CultureInfo.InvariantCulture);
                        lines.Add(string.Join("|", p));
                    }
                    else lines.Add(line);
                }
                Notes.WriteAll(path, string.Join("\n", lines.ToArray()) + "\n");
            }
        }

        // What was done for one in the last week, for the fold beside its %.
        internal static int Interactions(string personality, string name)
        {
            if (string.IsNullOrEmpty(personality)) return 0;
            lock (gate)
            {
                Row row;
                return Read(personality).TryGetValue(name ?? "", out row) ? Recent(row) : 0;
            }
        }

        // A few, none twice: a name the personality leans to weighs more, the rest one each, so a lean is never a lock.
        private static List<string> Roll(string personality, string mood, int many, ICollection<string> skip)
        {
            Dictionary<string, double> lean = Leaning(personality, mood);
            List<string> left = items.FindAll(one => skip == null || !skip.Contains(one)), picked = new List<string>();
            while (picked.Count < Math.Min(many, items.Count) && left.Count > 0)
            {
                double total = 0;
                foreach (string one in left) total += Weight(lean, one);
                double at = Dice.NextDouble() * total;
                foreach (string one in left)
                {
                    at -= Weight(lean, one);
                    if (at > 0) continue;
                    picked.Add(one);
                    left.Remove(one);
                    break;
                }
            }
            return picked;
        }

        // A personality's leaning with its mood's added on top, when the side names one for that mood.
        private static Dictionary<string, double> Leaning(string personality, string mood)
        {
            Dictionary<string, double> made = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase), part;
            foreach (string named in new[] { personality, mood })
                if (!string.IsNullOrEmpty(named) && leans.TryGetValue(named, out part))
                    foreach (KeyValuePair<string, double> one in part) { double had; made.TryGetValue(one.Key, out had); made[one.Key] = had + one.Value; }
            return made;
        }

        // How many she starts with: more where the personality and mood lean hard, fewer where they barely lean.
        internal static int StartFor(string personality, string mood)
        {
            int strong = 0;
            foreach (double weight in Leaning(personality, mood).Values) if (weight >= 2) strong++;
            int many = strong >= 3 ? Start + 2 : strong >= 1 ? Start : Start - 1;
            return Math.Max(1, Math.Min(many, Math.Max(1, items.Count)));
        }

        // Picked again by hand: the ones added by hand stay, the ones taken out stay out, the rest are rolled afresh, a
        // few more or not, each at any level: mostly low, now and then all the way, like someone met for the first time.
        internal static void Reroll(string personality, string mood)
        {
            if (!Shown || string.IsNullOrEmpty(personality) || personality == Yours) return;
            lock (gate)
            {
                Parse();
                if (items.Count == 0) return;
                Dictionary<string, Row> rows = Read(personality), kept = new Dictionary<string, Row>(StringComparer.OrdinalIgnoreCase);
                foreach (Row one in rows.Values) if ((one.In && one.Hand) || one.Out) kept[one.Name] = one;
                foreach (string one in Roll(personality, mood, StartFor(personality, mood) + Dice.Next(3), kept.Keys)) kept[one] = new Row { Name = one, Level = OpenLevel(), In = true };
                Write(personality, kept);
            }
        }

        internal const double Most = 100;

        private static double OpenLevel() { double u = Dice.NextDouble(); return Math.Round(StartLevel + (Most - StartLevel) * u * u * u, 1); }

        private static double Weight(Dictionary<string, double> lean, string one) { double w; return lean != null && lean.TryGetValue(one, out w) ? Math.Max(0, w) : 1; }

        // A line of theirs as she replies: hers grow a little as she does, one named grows more, and each pulls its
        // neighbours by nearness; a neighbour pulled past the mark joins hers, while some always stay out.
        internal static void Heard(string said, string personality)
        {
            if (!Shown || string.IsNullOrEmpty(personality)) return;
            lock (gate)
            {
                Dictionary<string, Row> rows = Read(personality);
                if (rows.Count == 0) return;
                Dictionary<string, double> gains = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                // What was done for one this week grows it faster: up to three times at 20.
                foreach (Row one in rows.Values) if (one.In) Gain(gains, one.Name, OwnGrowth * (1 + Math.Min(20, Recent(one)) / 10.0));
                foreach (string one in items)
                    if (Names(said, one))
                    {
                        Gain(gains, one, NamedGrowth);
                        Row row;
                        if (!rows.TryGetValue(one, out row)) rows[one] = row = new Row { Name = one };
                        Count(row);
                    }
                Dictionary<string, double> pulled = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, double> grown in gains)
                {
                    Gain(pulled, grown.Key, grown.Value);
                    Dictionary<string, double> by;
                    if (near.TryGetValue(grown.Key, out by)) foreach (KeyValuePair<string, double> other in by) Gain(pulled, other.Key, grown.Value * other.Value);
                }
                int members = 0;
                foreach (Row one in rows.Values) if (one.In) members++;
                int most = Math.Max(Start, items.Count - Math.Max(1, items.Count / 4));
                foreach (KeyValuePair<string, double> one in pulled)
                {
                    if (!items.Exists(i => string.Equals(i, one.Key, StringComparison.OrdinalIgnoreCase)) && !rows.ContainsKey(one.Key)) continue;
                    Row row;
                    if (!rows.TryGetValue(one.Key, out row)) rows[one.Key] = row = new Row { Name = one.Key };
                    Grow(row, one.Value);
                    if (!row.In && !row.Out && row.Level >= Joins && members < most && !Blocked(rows, one.Key)) { row.In = true; members++; }
                }
                Write(personality, rows);
                Theirs(said);
            }
        }

        // Their words that say what they're into: each counts for theirs and grows it; the third time it shows.
        private static void Theirs(string said)
        {
            if (string.IsNullOrEmpty(said) || theirs.Count == 0) return;
            Dictionary<string, Row> rows = null;
            foreach (KeyValuePair<string, Regex> one in theirs)
            {
                if (!one.Value.IsMatch(said)) continue;
                if (rows == null) rows = Read(Yours);
                Mark(rows, one.Key);
            }
            if (rows != null) Write(Yours, rows);
        }

        private static void Mark(Dictionary<string, Row> rows, string name)
        {
            Row row;
            if (!rows.TryGetValue(name, out row)) rows[name] = row = new Row { Name = name };
            Count(row);
            Grow(row, NamedGrowth * 3);
            if (!row.In && !row.Out && row.Level >= Joins) row.In = true;
        }

        // Something they did to her that says what they're into.
        internal static void TheyDid(string act)
        {
            if (!Shown || string.IsNullOrEmpty(act)) return;
            lock (gate)
            {
                List<string> into;
                if (!does.TryGetValue(act, out into)) return;
                Dictionary<string, Row> rows = Read(Yours);
                foreach (string one in into) Mark(rows, one);
                Write(Yours, rows);
            }
        }

        // An act turned by the ones of hers that cover it, by % and how willing she is, so in a bad way it still hurts.
        // Only an act that landed counts and grows them; a look ahead just reads.
        internal static int Turned(string act, int worth, string personality, bool landed, out string why)
        {
            why = null;
            if (!Shown || string.IsNullOrEmpty(personality) || string.IsNullOrEmpty(act)) return worth;
            lock (gate)
            {
                Parse();
                Dictionary<string, double> by;
                if (!turns.TryGetValue(act, out by)) return worth;
                double willing = Math.Max(0, Math.Min(1, (Willing() - 2) / 6.0));
                if (willing <= 0) return worth;
                Dictionary<string, Row> rows = Read(personality);
                double shift = 0;
                List<string> named = new List<string>();
                foreach (KeyValuePair<string, double> one in by)
                {
                    Row row;
                    if (!rows.TryGetValue(one.Key, out row) || !row.In) continue;
                    shift += one.Value * row.Level / Most * willing;
                    named.Add(one.Key + " " + Math.Round(row.Level) + "%");
                    if (landed) { Count(row); Grow(row, NamedGrowth); }
                }
                if (named.Count == 0) return worth;
                if (landed) Write(personality, rows);
                why = string.Join(", ", named.ToArray());
                return Math.Max(-5, Math.Min(5, (int)Math.Round(worth + shift)));
            }
        }

        private static void Gain(Dictionary<string, double> into, string name, double by) { double had; into.TryGetValue(name, out had); into[name] = had + by; }

        private static bool Names(string said, string one) { return !string.IsNullOrEmpty(said) && Regex.IsMatch(said, @"(?<![\p{L}\p{N}])" + Regex.Escape(one) + @"(?![\p{L}\p{N}])", RegexOptions.IgnoreCase); }

        // Set by hand to a %: hers at once, at exactly that.
        internal static void Set(string personality, string name, double level)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrEmpty(personality)) return;
            Add(personality, name);
            lock (gate)
            {
                Dictionary<string, Row> rows = Read(personality);
                Row row;
                if (!rows.TryGetValue(ItemNamed(name.Trim()), out row)) return;
                row.Level = Math.Max(0, Math.Min(Most, level));
                Write(personality, rows);
            }
        }

        // A typed name as the side's data spells it, when it is one of its items.
        private static string ItemNamed(string typed)
        {
            Parse();
            string found = items.Find(one => string.Equals(one, typed, StringComparison.OrdinalIgnoreCase));
            return found ?? typed;
        }

        // Added by hand: hers at once, at the joining mark. Taken out: never joins again by a pull, only by hand.
        internal static void Add(string personality, string name)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrEmpty(personality)) return;
            lock (gate)
            {
                name = ItemNamed(name.Trim());
                Dictionary<string, Row> rows = Read(personality);
                Row row;
                if (!rows.TryGetValue(name.Trim(), out row)) rows[name.Trim()] = row = new Row { Name = name.Trim() };
                row.In = true;
                row.Out = false;
                row.Hand = true;
                row.Level = Math.Min(Most, Math.Max(row.Level, Joins));
                Write(personality, rows);
            }
        }

        internal static void Remove(string personality, string name)
        {
            lock (gate)
            {
                Dictionary<string, Row> rows = Read(personality);
                Row row;
                if (!rows.TryGetValue(name ?? "", out row)) return;
                row.In = false;
                row.Out = true;
                Write(personality, rows);
            }
        }

        // For her turn note on the side: hers, strongest first.
        internal static string Note(string personality) { return Note(personality, Personalities.Attitude); }

        // Hers strongest first, then each past a mark with how she is with it; at the top one, willing 8 or more and in a
        // mood that fits, she asks for it outright.
        internal static string Note(string personality, string mood)
        {
            if (!Shown) return "";
            List<KeyValuePair<string, double>> hers = Hers(personality);
            if (hers.Count == 0) return "";
            List<string> names = hers.ConvertAll(one => one.Key);
            StringBuilder said = new StringBuilder("\n" + (note ?? "") + " " + string.Join(", ", names.ToArray()) + ".");
            double top = -1;
            lock (gate)
            {
                foreach (KeyValuePair<double, string> tier in tiers)
                {
                    List<string> at = new List<string>();
                    foreach (KeyValuePair<string, double> one in hers) if (one.Value >= tier.Key && TierOf(one.Value) == tier.Key) at.Add(one.Key);
                    if (at.Count == 0) continue;
                    said.Append(" ").Append(string.Join(", ", at.ToArray())).Append(": ").Append(tier.Value).Append(".");
                    top = tier.Key;
                }
                bool fitting = mood != null && fits.Exists(f => string.Equals(f, mood, StringComparison.OrdinalIgnoreCase));
                if (begs != null && tiers.Count > 0 && top == LastTier() && Willing() >= 8 && fitting) said.Append(" ").Append(begs);
            }
            return said.ToString();
        }

        // All in on one of hers right now: at the top mark, willing 9 or more and in a mood that fits.
        internal static bool AllIn(string personality, string mood)
        {
            if (!Shown || string.IsNullOrEmpty(personality) || Willing() < 9) return false;
            List<KeyValuePair<string, double>> hers = Hers(personality);
            lock (gate)
            {
                if (tiers.Count == 0 || mood == null || !fits.Exists(f => string.Equals(f, mood, StringComparison.OrdinalIgnoreCase))) return false;
                double top = LastTier();
                return hers.Exists(one => one.Value >= top);
            }
        }

        private static double TierOf(double level)
        {
            double at = -1;
            foreach (double mark in tiers.Keys) if (level >= mark) at = mark;
            return at;
        }

        private static double LastTier() { double last = -1; foreach (double mark in tiers.Keys) last = mark; return last; }

#if CHECKS
        internal static void Check(List<string> checks, string folder)
        {
            string setWas = SenpaiSet;
            bool sideWas = Brain.SenpaiOn, airWas = Brain.OnAir;
            Func<string> folderWas = Folder;
            Random diceWas = Dice;
            Func<DateTime> todayWas = Today;
            Func<int> willingWas = Willing;
            string root = Path.Combine(folder, "own-set");
            List<string> wrong = new List<string>();
            try
            {
                Directory.CreateDirectory(root);
                Folder = delegate { return root; };
                SenpaiSet = "title\tSTAND-IN SET\nnote\tYours, strongest first:\nitem\tAlpha\nitem\tBeta\nitem\tGamma\nitem\tDelta\nitem\tEpsilon\nitem\tZeta\n"
                    + "near\tAlpha\tBeta\t0.8\nnear\tBeta\tGamma\t0.5\nnear\tDelta\tEpsilon\t0.6\nlean\tLeaner\tAlpha\t50\nlean\tLeaner\tBeta\t50\nlean\tLeaner\tGamma\t50\n";
                Brain.SenpaiOn = false;
                Brain.OnAir = false;
                if (Shown || Note("Leaner").Length > 0) wrong.Add("it showed on the plain side");
                Brain.SenpaiOn = true;
                Dice = new Random(7);
                List<KeyValuePair<string, double>> first = Hers("Leaner");
                bool leaned = first.Count == Start && first.TrueForAll(one => one.Key == "Alpha" || one.Key == "Beta" || one.Key == "Gamma");
                List<KeyValuePair<string, double>> again = Hers("Leaner");
                bool kept = again.Count == first.Count && again.TrueForAll(one => first.Exists(f => f.Key == one.Key));
                // Talk of Delta pulls Epsilon past the mark; never every one of them joins.
                for (int i = 0; i < 16; i++) Heard("I really like delta, you know", "Leaner");
                List<KeyValuePair<string, double>> grown = Hers("Leaner");
                bool joined = grown.Exists(one => one.Key == "Delta") && grown.Exists(one => one.Key == "Epsilon") && grown.Count < 6;
                for (int i = 0; i < 200; i++) Heard("alpha beta gamma delta epsilon zeta", "Leaner");
                bool neverAll = Hers("Leaner").Count < 6;
                Remove("Leaner", "Epsilon");
                for (int i = 0; i < 20; i++) Heard("epsilon", "Leaner");
                bool stayedOut = !Hers("Leaner").Exists(one => one.Key == "Epsilon");
                Set("Leaner", "gamma", 100);
                bool setRight = Hers("Leaner").Exists(one => one.Key == "Gamma" && one.Value == Most);
                File.AppendAllText(Kept, "Leaner|Zeta 70%|8|hand\n");
                bool repaired = Hers("Leaner").Exists(one => one.Key == "Zeta" && one.Value == 70);
                if (!setRight || !repaired) wrong.Add("set by hand to 100 " + setRight + ", one typed with a % read as that one at that % " + repaired);
                Add("Leaner", "Something of theirs");
                bool added = Hers("Leaner").Exists(one => one.Key == "Something of theirs");
                string said = Note("Leaner");
                List<KeyValuePair<string, double>> order = Hers("Leaner");
                bool strongestFirst = said.StartsWith("\nYours, strongest first: " + order[0].Key, StringComparison.Ordinal);
                // Rolled again by hand: the one added stays, the one taken out stays out, fresh ones land at any level.
                bool rerolled = true, high = false, low = false;
                for (int i = 0; i < 60; i++)
                {
                    Reroll("Leaner", null);
                    List<KeyValuePair<string, double>> now = Hers("Leaner");
                    if (!now.Exists(one => one.Key == "Something of theirs") || now.Exists(one => one.Key == "Epsilon") || now.Exists(one => one.Value < StartLevel || one.Value > Most)) rerolled = false;
                    foreach (KeyValuePair<string, double> one in now) { if (one.Key != "Something of theirs" && one.Value >= 60) high = true; if (one.Value < 20) low = true; }
                }
                // What she's into turns an act around while she's willing; capped at 100; counted for the week; theirs from
                // their words and acts; an opposite of one of hers stays out of a pull.
                DateTime day = new DateTime(2026, 11, 27);
                Today = delegate { return day; };
                SenpaiSet += "tier\t30\tleans\ntier\t80\tlives it\ntier\t100\tall in\nbegs\tASKS\nfits\tClingy\n";
                string tiered = Note("Turner", "Clingy");
                SenpaiSet += "turns\tAlpha\tthrown\t6\nopposite\tAlpha\tZeta\nyours\tYOURS\ntheirs\tBeta\tbeta me|more beta\ndoes\tthrown\tGamma\n";
                Add("Turner", "Alpha");
                for (int i = 0; i < 200; i++) Heard("alpha", "Turner");
                double alpha = Hers("Turner").Find(one => one.Key == "Alpha").Value;
                Willing = delegate { return 10; };
                string why;
                int warm = Turned("thrown", -2, "Turner", true, out why);
                Willing = delegate { return 1; };
                string cold;
                int sore = Turned("thrown", -2, "Turner", true, out cold);
                bool turnedRight = alpha == Most && warm >= 3 && why != null && why.StartsWith("Alpha 100%", StringComparison.Ordinal) && sore == -2 && cold == null;
                int counted = Interactions("Turner", "Alpha");
                day = day.AddDays(Week);
                bool aged = Interactions("Turner", "Alpha") == 0;
                for (int i = 0; i < 300; i++) Heard("zeta zeta", "Turner");
                bool opposed = !Hers("Turner").Exists(one => one.Key == "Zeta");
                Heard("beta me please", "Turner");
                Heard("more beta", "Turner");
                Heard("beta me", "Turner");
                TheyDid("thrown");
                List<KeyValuePair<string, double>> yoursNow = Hers(Yours);
                Willing = delegate { return 9; };
                string allIn = Note("Turner", "Clingy"), calm = Note("Turner", "Sleepy");
                Willing = delegate { return 4; };
                string cool = Note("Turner", "Clingy");
                // Unused for a week, it falls once a day she runs and not twice the same day.
                day = day.AddDays(Week);
                double before = Hers(Yours).Find(one => one.Key == "Beta").Value;
                Fall();
                Fall();
                double after = Hers(Yours).Find(one => one.Key == "Beta").Value;
                bool fell = Math.Abs(before - Math.Max(StartLevel, after + Most / 21)) < 0.02 && after < before;
                day = day.AddDays(-Week);
                int allAt = allIn.IndexOf(": all in.", StringComparison.Ordinal);
                string allGroup = allAt > 0 ? allIn.Substring(0, allAt) : "";
                bool tiersRight = allGroup.Substring(allGroup.LastIndexOf('.') + 1).Contains("Alpha") && allIn.EndsWith(" ASKS", StringComparison.Ordinal) && !calm.Contains("ASKS") && !cool.Contains("ASKS") && tiered != null;
                bool theirsRight = tiersRight && fell && YoursTitle == "YOURS" && yoursNow.Exists(one => one.Key == "Beta") && Interactions(Yours, "Beta") == 3 && Interactions(Yours, "Gamma") == 1;
                if (!turnedRight || counted < 200 || !aged || !opposed || !theirsRight)
                    wrong.Add("capped at " + alpha + ", turned " + warm + " (" + why + ") willing and " + sore + " not, counted " + counted + ", let go after a week " + aged + ", opposite kept out " + opposed + ", tiers, theirs and their fall " + theirsRight + " (" + allIn.Trim() + ")");
                if (!leaned || !kept || !joined || !neverAll || !stayedOut || !added || !strongestFirst || !rerolled || !high || !low)
                    wrong.Add("leaned " + leaned + ", kept " + kept + ", a neighbour joined " + joined + ", never all " + neverAll + ", taken out stays out " + stayedOut + ", added " + added + ", note strongest first " + strongestFirst + " (" + said.Trim() + "), rerolled keeping hand and out " + rerolled + ", landed high " + high + " and low " + low);
            }
            finally { SenpaiSet = setWas; Brain.SenpaiOn = sideWas; Brain.OnAir = airWas; Folder = folderWas; Dice = diceWas; Today = todayWas; Willing = willingWas; }
            if (wrong.Count > 0) throw new Exception("Her own set went wrong: " + string.Join("; ", wrong.ToArray()));
            checks.Add("PASS her other side's own set with stand-in data: nothing of it on the plain side; three to start, leaning by personality and kept; a named one grows and pulls its neighbour in past the mark; never all of them; one taken out stays out; one added is hers; her note lists hers strongest first; a reroll by hand keeps the added and the taken out, and lands fresh ones anywhere from low to high; one at 100 turns a throw to liked while she's willing and not when she isn't, stops at 100, is counted for the week and let go after it; an opposite stays out; theirs come from their words and acts; unused for a week, one falls once a day she runs; her note says how she is with each past its mark, and asks outright only at the top one, willing and in a mood that fits");
        }
#endif
    }
}
