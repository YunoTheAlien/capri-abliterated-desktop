using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // The other side's own set on her Topics page, its names only in the side's data (SenpaiSet). A few to start by her
    // personality's lean; each grows as she talks and pulls its neighbours, which join past a mark; never all of them.
    internal static class OwnSet
    {
        // The side's data, a tab-separated row each: "title", "note", "item", "near a b weight", "lean personality item weight".
        internal static string SenpaiSet = "";
        internal const int Start = 3;
        internal const double Joins = 8, OwnGrowth = 0.25, NamedGrowth = 1, StartLevel = 4;
        private static readonly object gate = new object();
        private static string parsedFrom;
        private static string title, note;
        private static readonly List<string> items = new List<string>();
        private static readonly Dictionary<string, Dictionary<string, double>> near = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Dictionary<string, double>> leans = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
        // For the checks: a pick instead of a die.
        internal static Random Dice = new Random();

        private static void Parse()
        {
            string text = SenpaiSet ?? "";
            if (text == parsedFrom) return;
            parsedFrom = text;
            title = note = null;
            items.Clear();
            near.Clear();
            leans.Clear();
            foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                string[] p = raw.Split('\t');
                double w;
                if (p.Length == 2 && p[0] == "title") title = p[1].Trim();
                else if (p.Length == 2 && p[0] == "note") note = p[1].Trim();
                else if (p.Length == 2 && p[0] == "item" && p[1].Trim().Length > 0) items.Add(p[1].Trim());
                else if (p.Length == 4 && p[0] == "near" && double.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out w)) { Link(near, p[1], p[2], w); Link(near, p[2], p[1], w); }
                else if (p.Length == 4 && p[0] == "lean" && double.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out w)) Link(leans, p[1], p[2], w);
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

        // ---------- What she keeps: "personality|name|level|in" a row, in the side's own folder ----------

        private sealed class Row { internal string Name; internal double Level; internal bool In, Out; }

        internal static Func<string> Folder = delegate { Func<string> f = Expansion.Folder; return f == null ? null : f(); };

        private static string Kept { get { string root = Folder(); return string.IsNullOrEmpty(root) ? null : Path.Combine(root, "own set.txt"); } }

        private static Dictionary<string, Row> Read(string personality)
        {
            Dictionary<string, Row> rows = new Dictionary<string, Row>(StringComparer.OrdinalIgnoreCase);
            string path = Kept;
            if (path == null || !File.Exists(path)) return rows;
            foreach (string line in Notes.ReadLines(path))
            {
                string[] p = line.Split('|');
                double level;
                if (p.Length != 4 || !string.Equals(p[0], personality, StringComparison.OrdinalIgnoreCase) || !double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out level)) continue;
                rows[p[1]] = new Row { Name = p[1], Level = level, In = p[3] == "in", Out = p[3] == "out" };
            }
            return rows;
        }

        private static void Write(string personality, Dictionary<string, Row> rows)
        {
            string path = Kept;
            if (path == null) return;
            List<string> lines = new List<string>();
            if (File.Exists(path)) foreach (string line in Notes.ReadLines(path)) if (!line.StartsWith(personality + "|", StringComparison.OrdinalIgnoreCase)) lines.Add(line);
            foreach (Row one in rows.Values) lines.Add(personality + "|" + one.Name + "|" + one.Level.ToString("0.##", CultureInfo.InvariantCulture) + "|" + (one.In ? "in" : one.Out ? "out" : "-"));
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
                if (rows.Count == 0)
                {
                    foreach (string one in Roll(personality)) rows[one] = new Row { Name = one, Level = StartLevel, In = true };
                    Write(personality, rows);
                }
                foreach (Row one in rows.Values) if (one.In) hers.Add(new KeyValuePair<string, double>(one.Name, one.Level));
            }
            hers.Sort(delegate(KeyValuePair<string, double> a, KeyValuePair<string, double> b) { return b.Value.CompareTo(a.Value); });
            return hers;
        }

        // A few, none twice: a name the personality leans to weighs more, the rest one each, so a lean is never a lock.
        private static List<string> Roll(string personality)
        {
            Dictionary<string, double> lean;
            leans.TryGetValue(personality, out lean);
            List<string> left = new List<string>(items), picked = new List<string>();
            while (picked.Count < Math.Min(Start, items.Count) && left.Count > 0)
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
                foreach (Row one in rows.Values) if (one.In) Gain(gains, one.Name, OwnGrowth);
                foreach (string one in items) if (Names(said, one)) Gain(gains, one, NamedGrowth);
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
                    row.Level += one.Value;
                    if (!row.In && !row.Out && row.Level >= Joins && members < most) { row.In = true; members++; }
                }
                Write(personality, rows);
            }
        }

        private static void Gain(Dictionary<string, double> into, string name, double by) { double had; into.TryGetValue(name, out had); into[name] = had + by; }

        private static bool Names(string said, string one) { return !string.IsNullOrEmpty(said) && Regex.IsMatch(said, @"(?<![\p{L}\p{N}])" + Regex.Escape(one) + @"(?![\p{L}\p{N}])", RegexOptions.IgnoreCase); }

        // Added by hand: hers at once, at the joining mark. Taken out: never joins again by a pull, only by hand.
        internal static void Add(string personality, string name)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrEmpty(personality)) return;
            lock (gate)
            {
                Dictionary<string, Row> rows = Read(personality);
                Row row;
                if (!rows.TryGetValue(name.Trim(), out row)) rows[name.Trim()] = row = new Row { Name = name.Trim() };
                row.In = true;
                row.Out = false;
                row.Level = Math.Max(row.Level, Joins);
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
        internal static string Note(string personality)
        {
            if (!Shown) return "";
            List<KeyValuePair<string, double>> hers = Hers(personality);
            if (hers.Count == 0) return "";
            List<string> names = hers.ConvertAll(one => one.Key);
            return "\n" + (note ?? "") + " " + string.Join(", ", names.ToArray()) + ".";
        }

#if CHECKS
        internal static void Check(List<string> checks, string folder)
        {
            string setWas = SenpaiSet;
            bool sideWas = Brain.SenpaiOn, airWas = Brain.OnAir;
            Func<string> folderWas = Folder;
            Random diceWas = Dice;
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
                Add("Leaner", "Something of theirs");
                bool added = Hers("Leaner").Exists(one => one.Key == "Something of theirs");
                string said = Note("Leaner");
                List<KeyValuePair<string, double>> order = Hers("Leaner");
                bool strongestFirst = said.StartsWith("\nYours, strongest first: " + order[0].Key, StringComparison.Ordinal);
                if (!leaned || !kept || !joined || !neverAll || !stayedOut || !added || !strongestFirst)
                    wrong.Add("leaned " + leaned + ", kept " + kept + ", a neighbour joined " + joined + ", never all " + neverAll + ", taken out stays out " + stayedOut + ", added " + added + ", note strongest first " + strongestFirst + " (" + said.Trim() + ")");
            }
            finally { SenpaiSet = setWas; Brain.SenpaiOn = sideWas; Brain.OnAir = airWas; Folder = folderWas; Dice = diceWas; }
            if (wrong.Count > 0) throw new Exception("Her own set went wrong: " + string.Join("; ", wrong.ToArray()));
            checks.Add("PASS her other side's own set with stand-in data: nothing of it on the plain side; three to start, leaning by personality and kept; a named one grows and pulls its neighbour in past the mark; never all of them; one taken out stays out; one added is hers; her note lists hers strongest first");
        }
#endif
    }
}
