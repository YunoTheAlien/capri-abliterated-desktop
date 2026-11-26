using System;
using System.Collections.Generic;
using System.Globalization;

namespace CapriDesktop
{
    // Growth by feedback: talking to her or a kind act gives the personality and mood she is in a
    // point, drift-joined ones half, one set on purpose two. It never fades, and only sets how far their lines grow.
    internal static class Growth
    {
        internal const double Joined = 0.5, OnPurpose = 2;
        // Her settings' rows, "side\tp:<name>\tpoints" or "side\tm:<name>\tpoints", and how they are saved.
        internal static Func<List<string>> Rows;
        internal static Action Save;
        private static readonly object gate = new object();
        private static DateTime savedAt = DateTime.MinValue;

        private static string Side { get { return Brain.SenpaiLive ? "senpai" : "plain"; } }

        // One interaction, for whoever she is right now.
        internal static void Interacted()
        {
            try
            {
                string main = Personalities.Main, chosen = Personalities.Chosen, under = Personalities.Under, mood = Personalities.Attitude ?? "";
                Dictionary<string, double> points = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                // The personality: set on purpose when it is the one they picked and she hasn't drifted from it.
                bool picked = main != Personalities.Dynamic && string.Equals(chosen, main, StringComparison.OrdinalIgnoreCase);
                Add(points, "p:" + under, picked ? OnPurpose : 1);
                foreach (string joined in Personalities.DriftsInto(main)) Add(points, "p:" + joined, Joined);
                if (!string.Equals(chosen, main, StringComparison.OrdinalIgnoreCase) && main != Personalities.Dynamic) Add(points, "p:" + main, Joined);
                if (mood.Length > 0)
                {
                    Add(points, "m:" + mood, Dynamics.Held ? OnPurpose : 1);
                    foreach (string joined in Personalities.SoursInto()) Add(points, "m:" + joined, Joined);
                }
                Count(points);
            }
            catch (Exception caughtOne) { Brain.Caught(caughtOne); }
        }

        // The highest weight wins where a name is reached twice: her own personality is never also counted as joined.
        private static void Add(Dictionary<string, double> points, string key, double weight)
        {
            if (key.EndsWith(":", StringComparison.Ordinal)) return;
            double had;
            if (!points.TryGetValue(key, out had) || weight > had) points[key] = weight;
        }

        private static void Count(Dictionary<string, double> points)
        {
            Func<List<string>> rows = Rows;
            if (rows == null || points.Count == 0) return;
            bool due;
            lock (gate)
            {
                List<string> all = rows();
                string side = Side;
                foreach (KeyValuePair<string, double> one in points)
                {
                    int at = all.FindIndex(each => each.StartsWith(side + "\t" + one.Key + "\t", StringComparison.OrdinalIgnoreCase));
                    double had = at < 0 ? 0 : Points(all[at]);
                    string row = side + "\t" + one.Key + "\t" + (had + one.Value).ToString("0.##", CultureInfo.InvariantCulture);
                    if (at < 0) all.Add(row); else all[at] = row;
                }
                due = SimTime.UtcNow - savedAt >= TimeSpan.FromMinutes(1);
                if (due) savedAt = SimTime.UtcNow;
            }
            Action save = Save;
            if (due && save != null) save();
        }

        private static double Points(string row)
        {
            string[] parts = row.Split('\t');
            double points;
            return parts.Length == 3 && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out points) && points > 0 ? points : 0;
        }

        // A name's points on this side, by its row key ("p:<name>" or "m:<name>").
        internal static double PointsOf(string key) { double points, most; Of(key, out points, out most); return points; }

        // A name's points on this side, and the most any of its kind has.
        private static void Of(string key, out double points, out double most)
        {
            points = most = 0;
            Func<List<string>> rows = Rows;
            if (rows == null) return;
            string side = Side, kind = key.Substring(0, 2);
            lock (gate)
                foreach (string row in rows())
                {
                    string[] parts = row.Split('\t');
                    if (parts.Length != 3 || parts[0] != side || !parts[1].StartsWith(kind, StringComparison.Ordinal)) continue;
                    double one = Points(row);
                    most = Math.Max(most, one);
                    if (string.Equals(parts[1], key, StringComparison.OrdinalIgnoreCase)) points = one;
                }
        }

        // 0 to 1: how much of her use this personality and mood have had, against the most used of each.
        internal static double Share(string personality, string mood)
        {
            double p, pMost, m, mMost;
            Of("p:" + (personality ?? ""), out p, out pMost);
            double share = pMost > 0 ? p / pMost : 0;
            if (string.IsNullOrEmpty(mood)) return share;
            Of("m:" + mood, out m, out mMost);
            return share * (mMost > 0 ? m / mMost : 0);
        }

        // How many grown lines a pair keeps before more are asked for: 3 unused, up to 9 for the most used.
        internal static int Thin(string personality, string mood) { return Expansion.Thin + (int)Math.Round(2 * Expansion.Thin * Share(personality, mood)); }

        // How many new lines one ask is for: 8, up to 16.
        internal static int Lines(string personality, string mood) { return 8 + (int)Math.Round(8 * Share(personality, mood)); }

        // How many times a pair may ask in one session: once, up to 3.
        internal static int Asks(string personality, string mood) { return 1 + (int)Math.Round(2 * Share(personality, mood)); }

        // A stand-in tally: Sassy picked on purpose counts two, drift-joined ones half, a held mood two; the most used pair
        // grows most; her numbers and the other side's rows stay as they were.
#if CHECKS
        internal static void Check(List<string> checks)
        {
            Func<List<string>> rowsWas = Rows;
            Action saveWas = Save;
            string mainWas = Personalities.Main, chosenWas = Personalities.Chosen, moodWas = Personalities.Attitude;
            bool heldWas = Dynamics.Held;
            List<string> rows = new List<string> { "senpai	p:Sassy	50" };
            List<string> wrong = new List<string>();
            try
            {
                Rows = delegate { return rows; };
                Save = null;
                Personalities.Main = Personalities.Chosen = "Sassy";
                string under = Personalities.Under;
                List<string> joined = Personalities.DriftsInto("Sassy");
                int reacts = Personalities.Reacts("flirt", -99);
                Personalities.Attitude = "";
                for (int i = 0; i < 4; i++) Interacted();
                double sassy, most;
                Of("p:" + under, out sassy, out most);
                if (sassy != 4 * OnPurpose) wrong.Add("picked on purpose counted " + sassy + ", not " + 4 * OnPurpose);
                if (joined.Count > 0)
                {
                    double half, ignored;
                    Of("p:" + joined[0], out half, out ignored);
                    if (half != 4 * Joined) wrong.Add(joined[0] + ", joined by her drift, counted " + half);
                }
                if (Share(under, null) != 1 || Thin(under, null) != 3 * Expansion.Thin || Lines(under, null) != 16 || Asks(under, null) != 3)
                    wrong.Add("the most used grew " + Thin(under, null) + " kept, " + Lines(under, null) + " asked, " + Asks(under, null) + " times");
                if (Share("Nobody", null) != 0 || Thin("Nobody", null) != Expansion.Thin || Lines("Nobody", null) != 8 || Asks("Nobody", null) != 1)
                    wrong.Add("one never talked to grew past stock");
                Personalities.Attitude = "Clingy";
                Dynamics.Held = true;
                Interacted();
                double clingy, moodMost;
                Of("m:Clingy", out clingy, out moodMost);
                if (clingy != OnPurpose) wrong.Add("a mood held by hand counted " + clingy);
                if (Personalities.Reacts("flirt", -99) != reacts) wrong.Add("her numbers changed");
                if (rows[0] != "senpai	p:Sassy	50") wrong.Add("the other side's rows changed");
                if (wrong.Count > 0) throw new Exception("Growth by feedback went wrong: " + string.Join("; ", wrong.ToArray()));
                checks.Add("PASS growth by feedback: talking to a personality picked on purpose counts " + OnPurpose + " a time, one her drift joins " + Joined + ", a mood held by hand " + OnPurpose + "; the most used pair keeps " + Thin(under, null) + " grown lines before asking, asks for 16 at a time and up to 3 times a session, one never talked to stays at 3, 8 and once; her numbers and the other side's rows are untouched");
            }
            finally
            {
                Rows = rowsWas;
                Save = saveWas;
                Personalities.Main = mainWas;
                Personalities.Chosen = chosenWas;
                Personalities.Attitude = moodWas;
                Dynamics.Held = heldWas;
            }
        }
#endif
    }
}
