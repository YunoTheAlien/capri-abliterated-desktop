using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace CapriDesktop
{
    // How far she or they are into a topic or love language: a % to 100, and this week's count that grows it. One row a
    // line in fields.txt: kind, whose (a personality or Yours), name, level, days, out.
    internal static class Fields
    {
        internal const string FileName = "fields.txt", Topic = "topic", Love = "love", Word = "word", Yours = "~you";
        internal const int Week = 7;
        internal const double Most = 100, Shows = 15;
        internal static string Kept;
        // For the checks: the day it is.
        internal static Func<DateTime> Today = delegate { return SimTime.Now.Date; };
        private static readonly object gate = new object();
        private static Dictionary<string, Row> rows;
        private static string readFrom;

        private sealed class Row
        {
            internal string Kind, Whose, Name;
            internal double Level;
            internal bool Out;
            internal readonly SortedDictionary<int, int> Days = new SortedDictionary<int, int>();
            // The day it was last done for; 0 is never, and one never done for doesn't fall.
            internal int Last;
            // Put there by hand, not from her file.
            internal bool Hand;
        }

        private static string Key(string kind, string whose, string name) { return kind + "|" + whose + "|" + (name ?? "").ToLowerInvariant(); }

        private static int DayOf(DateTime day) { return (int)(day.Date.Ticks / TimeSpan.TicksPerDay); }

        private static void Load()
        {
            if (rows != null && readFrom == Kept) return;
            rows = new Dictionary<string, Row>();
            readFrom = Kept;
            try
            {
                if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept)) return;
                foreach (string line in File.ReadAllLines(Kept))
                {
                    string[] p = line.Split('|');
                    double level;
                    if (p.Length < 4 || !double.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out level)) continue;
                    Row row = new Row { Kind = p[0], Whose = p[1], Name = p[2], Level = Math.Max(0, Math.Min(Most, level)), Out = p.Length > 5 && p[5] == "out" };
                    if (p.Length > 6) int.TryParse(p[6], out row.Last);
                    row.Hand = p.Length > 7 && p[7] == "hand";
                    if (p.Length > 4)
                        foreach (string day in p[4].Split(','))
                        {
                            string[] d = day.Split(':');
                            int at, many;
                            if (d.Length == 2 && int.TryParse(d[0], out at) && int.TryParse(d[1], out many)) row.Days[at] = many;
                        }
                    rows[Key(row.Kind, row.Whose, row.Name)] = row;
                }
            }
            catch (Exception caughtOne) { Brain.Caught(caughtOne); }
        }

        private static void Save()
        {
            if (string.IsNullOrEmpty(Kept)) return;
            List<string> lines = new List<string>();
            foreach (Row one in rows.Values)
            {
                List<string> days = new List<string>();
                foreach (KeyValuePair<int, int> day in one.Days) days.Add(day.Key + ":" + day.Value);
                lines.Add(one.Kind + "|" + one.Whose + "|" + one.Name + "|" + one.Level.ToString("0.##", CultureInfo.InvariantCulture) + "|" + string.Join(",", days.ToArray()) + "|" + (one.Out ? "out" : "in") + "|" + one.Last + (one.Hand ? "|hand" : ""));
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Kept));
                File.WriteAllText(Kept, string.Join("\n", lines.ToArray()) + "\n");
            }
            catch (Exception caughtOne) { Brain.Caught(caughtOne); }
        }

        private static Row Find(string kind, string whose, string name, double start, bool make)
        {
            Row row;
            if (rows.TryGetValue(Key(kind, whose, name), out row) || !make) return row;
            row = new Row { Kind = kind, Whose = whose, Name = name, Level = Math.Max(0, Math.Min(Most, start)) };
            rows[Key(kind, whose, name)] = row;
            return row;
        }

        // Its %: as kept, or the start given when there is none yet (kept from then on).
        internal static double Level(string kind, string whose, string name, double start)
        {
            if (string.IsNullOrEmpty(whose) || string.IsNullOrEmpty(name)) return 0;
            lock (gate)
            {
                Load();
                Row row = Find(kind, whose, name, start, false);
                if (row != null) return row.Level;
                Find(kind, whose, name, start, true);
                Save();
                return Math.Max(0, Math.Min(Most, start));
            }
        }

        internal static int Interactions(string kind, string whose, string name)
        {
            if (string.IsNullOrEmpty(whose) || string.IsNullOrEmpty(name)) return 0;
            lock (gate)
            {
                Load();
                Row row = Find(kind, whose, name, 0, false);
                if (row == null) return 0;
                int today = DayOf(Today()), many = 0;
                foreach (KeyValuePair<int, int> one in row.Days) if (one.Key > today - Week) many += one.Value;
                return many;
            }
        }

        // Something done for it today: counted, the days past the week let go, and it grows; one taken out stays as it is.
        internal static void Did(string kind, string whose, string name, double grow)
        {
            if (string.IsNullOrEmpty(whose) || string.IsNullOrEmpty(name)) return;
            lock (gate)
            {
                Load();
                Row row = Find(kind, whose, name, 0, true);
                if (row.Out) return;
                int today = DayOf(Today()), had;
                row.Days.TryGetValue(today, out had);
                row.Days[today] = had + 1;
                row.Last = today;
                List<int> old = new List<int>();
                foreach (int day in row.Days.Keys) if (day <= today - Week) old.Add(day);
                foreach (int day in old) row.Days.Remove(day);
                row.Level = Math.Min(Most, row.Level + grow);
                Save();
            }
        }

        // Once a day she runs: one not done for in a week falls a 21st of the way, never under Floor, so it fades over 3
        // weeks of her running until it's used again.
        internal const double Floor = 10;
        private static int fellOn;

        internal static void Fall()
        {
            lock (gate)
            {
                Load();
                int today = DayOf(Today());
                string mark = string.IsNullOrEmpty(Kept) ? null : Kept + ".fell";
                int fell;
                if (fellOn == 0 && mark != null && File.Exists(mark) && int.TryParse(File.ReadAllText(mark).Trim(), out fell)) fellOn = fell;
                if (fellOn == today) return;
                fellOn = today;
                bool moved = false;
                foreach (Row one in rows.Values)
                    if (!one.Out && one.Last > 0 && today - one.Last >= Week && one.Level > Floor) { one.Level = Math.Max(Floor, one.Level - Most / 21); moved = true; }
                try { if (mark != null) File.WriteAllText(mark, today.ToString(CultureInfo.InvariantCulture)); }
                catch (Exception caughtOne) { Brain.Caught(caughtOne); }
                if (moved) Save();
            }
        }

        // Added by hand: in, at least the start given. Taken out: kept out, so it doesn't come back by itself.
        internal static void In(string kind, string whose, string name, double start)
        {
            lock (gate)
            {
                Load();
                Row row = Find(kind, whose, name, start, true);
                row.Out = false;
                row.Hand = true;
                row.Level = Math.Max(row.Level, Math.Min(Most, start));
                Save();
            }
        }

        // Set by hand to a %: in, at exactly that.
        internal static void Set(string kind, string whose, string name, double level)
        {
            if (string.IsNullOrEmpty(whose) || string.IsNullOrEmpty(name)) return;
            lock (gate)
            {
                Load();
                Row row = Find(kind, whose, name, level, true);
                row.Out = false;
                row.Level = Math.Max(0, Math.Min(Most, level));
                Save();
            }
        }

        internal static void Out(string kind, string whose, string name)
        {
            lock (gate)
            {
                Load();
                Find(kind, whose, name, 0, true).Out = true;
                Save();
            }
        }

        internal static bool IsOut(string kind, string whose, string name)
        {
            if (string.IsNullOrEmpty(whose) || string.IsNullOrEmpty(name)) return false;
            lock (gate)
            {
                Load();
                Row row = Find(kind, whose, name, 0, false);
                return row != null && row.Out;
            }
        }

        // Ones of a kind put there by hand and not taken out since.
        internal static string[] Added(string kind, string whose)
        {
            List<string> found = new List<string>();
            if (string.IsNullOrEmpty(whose)) return found.ToArray();
            lock (gate)
            {
                Load();
                foreach (Row one in rows.Values) if (one.Kind == kind && one.Whose == whose && one.Hand && !one.Out) found.Add(one.Name);
            }
            return found.ToArray();
        }

        // Theirs of one kind that show: past the mark and not taken out, strongest first.
        internal static List<string> Shown(string kind, string whose)
        {
            List<Row> found = new List<Row>();
            lock (gate)
            {
                Load();
                foreach (Row one in rows.Values) if (one.Kind == kind && one.Whose == whose && !one.Out && one.Level >= Shows) found.Add(one);
            }
            found.Sort(delegate(Row a, Row b) { return b.Level.CompareTo(a.Level); });
            return found.ConvertAll(one => one.Name);
        }

        // A liked act, to the love language it speaks: hers when it is one of hers, and theirs, since they gave it.
        internal static string LanguageOf(string act, string actKind)
        {
            switch (act ?? "")
            {
                case "praised": return "Words of affirmation";
                case "petted": case "uppies": case "tickled": return "Physical touch";
                case "wash": return "Acts of service";
            }
            switch (actKind ?? "")
            {
                case "gift": return "Receiving gifts";
                case "touch": return "Physical touch";
                case "food": case "fed": case "treat": case "nuggies": return "Acts of service";
            }
            return null;
        }

        // A chat line of theirs: every tenth one in a day is time together.
        private static int lines;
        private static int linesDay;

        internal static bool TimeTogether()
        {
            lock (gate)
            {
                int today = DayOf(Today());
                if (today != linesDay) { linesDay = today; lines = 0; }
                return ++lines % 10 == 0;
            }
        }

#if CHECKS
        // Kept between reads, capped at 100, counted for the week and let go after it, taken out stays out, theirs show past
        // the mark strongest first, acts find their love language.
        internal static void Check(List<string> checks, string folder)
        {
            string keptWas = Kept;
            Func<DateTime> todayWas = Today;
            try
            {
                Kept = Path.Combine(folder, "fields-check", FileName);
                rows = null;
                DateTime day = new DateTime(2026, 11, 27);
                Today = delegate { return day; };
                double first = Level(Topic, "Sassy", "Concerts", 50);
                for (int i = 0; i < 40; i++) Did(Topic, "Sassy", "Concerts", 2);
                rows = null;
                bool kept = Level(Topic, "Sassy", "Concerts", 10) == Most && Interactions(Topic, "Sassy", "Concerts") == 40;
                day = day.AddDays(Week);
                bool aged = Interactions(Topic, "Sassy", "Concerts") == 0;
                fellOn = 0;
                Fall();
                Fall();
                bool fallen = Math.Abs(Level(Topic, "Sassy", "Concerts", 0) - (Most - Most / 21)) < 0.01;
                Out(Love, Yours, "Receiving gifts");
                Did(Love, Yours, "Receiving gifts", 50);
                for (int i = 0; i < 3; i++) Did(Love, Yours, "Quality time", 5);
                for (int i = 0; i < 5; i++) Did(Love, Yours, "Physical touch", 5);
                Did(Love, Yours, "Words of affirmation", 5);
                List<string> shown = Shown(Love, Yours);
                bool yours = shown.Count == 2 && shown[0] == "Physical touch" && shown[1] == "Quality time";
                bool acts = LanguageOf("praised", "words") == "Words of affirmation" && LanguageOf("flowers", "gift") == "Receiving gifts" && LanguageOf("snack", "food") == "Acts of service" && LanguageOf("insulted", "words") == null;
                int together = 0;
                for (int i = 0; i < 25; i++) if (TimeTogether()) together++;
                if (first != 50 || !kept || !aged || !fallen || !yours || !acts || together != 2)
                    throw new Exception("Her fields went wrong: started " + first + ", kept and capped " + kept + ", let go after a week " + aged + ", fell once " + fallen + ", theirs shown " + string.Join(", ", shown.ToArray()) + ", acts " + acts + ", time together " + together);
                checks.Add("PASS her fields: a % kept between reads and stopped at 100, this week's count kept and let go after 7 days, then it falls once a day she runs, one taken out stays out, theirs show past " + Shows + " strongest first, liked acts find their love language, every tenth chat line is time together");
            }
            finally { Kept = keptWas; Today = todayWas; rows = null; fellOn = 0; }
        }
#endif
    }
}
