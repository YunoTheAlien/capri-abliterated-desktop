using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace CapriDesktop
{
    // When they are usually at the PC. She had no idea: every nudge about sleep came off the clock alone, so one at
    // half past midnight read the same to someone who finishes at ten and someone who works nights.
    //
    // The PC being on is the whole measurement. One counter an hour, raised while she is running, so a week of use
    // draws their day without asking them anything. The counters decay, so a changed routine catches up in about a
    // fortnight rather than being outvoted by last month forever.
    //
    // Nothing here is sent anywhere. It is twenty four numbers in a file beside her settings, and what reaches her
    // brain is one sentence of prose with no numbers in it, the way her mood and her forwardness are given to her.
    internal static class Hours
    {
        internal const string FileName = "hours.txt";
        internal static string Kept;

        private static readonly object gate = new object();
        private static readonly double[] counts = new double[24];
        private static bool read;
        private static int markedHour = -1;
        private static DateTime markedDay = DateTime.MinValue;
        private static DateTime saved = DateTime.MinValue;

        // A fortnight of use outweighs what came before it. Applied once a day, not once an hour, or a day at the PC
        // would wash out the days around it.
        private const double Fade = 0.95;
        // Below this an hour has not been seen enough to mean anything, so her day stays unknown rather than guessed.
        private const double Enough = 2.0;

        // What they wrote in me.md, if anything. Their own word beats a week of counting: someone who works nights
        // knows their hours better than a tally of when the PC happened to be on, and being told off at one in the
        // morning for a normal evening is exactly the thing this is meant to stop.
        internal static string Told;

        private static bool Stated(out int from, out int to)
        {
            from = to = -1;
            string said = Told;
            if (string.IsNullOrWhiteSpace(said)) return false;
            System.Text.RegularExpressions.Match range = System.Text.RegularExpressions.Regex.Match(
                said, @"(\d{1,2})\s*(?::\s*(\d{2}))?\s*([ap]\.?m\.?)?\s*(?:-|to|until|till|\u2013)\s*(\d{1,2})\s*(?::\s*(\d{2}))?\s*([ap]\.?m\.?)?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!range.Success) return false;
            from = Hour(range.Groups[1].Value, range.Groups[3].Value);
            to = Hour(range.Groups[4].Value, range.Groups[6].Value);
            if (from < 0 || to < 0 || from == to) { from = to = -1; return false; }
            // Stored as the last hour they are up, the same shape Usual returns.
            to = (to + 23) % 24;
            return true;
        }

        private static int Hour(string number, string half)
        {
            int hour;
            if (!int.TryParse(number, out hour) || hour < 0 || hour > 24) return -1;
            bool pm = half != null && half.TrimEnd('.').ToLowerInvariant().StartsWith("p");
            bool am = half != null && half.TrimEnd('.').ToLowerInvariant().StartsWith("a");
            if (pm && hour < 12) hour += 12;
            if (am && hour == 12) hour = 0;
            return hour % 24;
        }

        internal static void Mark() { Mark(DateTime.Now); }

        // Called on her tick. Only the first call in an hour counts, so this is cheap and a long session does not
        // count that hour more than once.
        internal static void Mark(DateTime now)
        {
            lock (gate)
            {
                Fresh();
                if (now.Hour == markedHour && now.Date == markedDay) return;
                if (markedDay != DateTime.MinValue && now.Date > markedDay)
                    for (int each = 0; each < counts.Length; each++) counts[each] *= Fade;
                markedHour = now.Hour;
                markedDay = now.Date;
                counts[now.Hour] += 1;
                if (saved == DateTime.MinValue || (now - saved).TotalMinutes >= 10) { Write(); saved = now; }
            }
        }

        // The hours they are usually about, as a from and to. Returns false while there is not enough to say.
        internal static bool Usual(out int from, out int to)
        {
            from = to = -1;
            // Theirs first, always.
            if (Stated(out from, out to)) return true;
            lock (gate)
            {
                Fresh();
                double most = 0;
                foreach (double count in counts) if (count > most) most = count;
                if (most < Enough) return false;
                // An hour counts as theirs when it is used a good part of as often as their busiest.
                double bar = Math.Max(Enough, most * 0.35);
                bool[] on = new bool[24];
                int many = 0;
                for (int each = 0; each < 24; each++) { on[each] = counts[each] >= bar; if (on[each]) many++; }
                if (many == 0 || many == 24) return false;
                // Their day wraps past midnight, so the start is the first on hour whose hour before is off.
                for (int each = 0; each < 24; each++)
                {
                    if (!on[each] || on[(each + 23) % 24]) continue;
                    int run = 0;
                    while (run < 24 && on[(each + run) % 24]) run++;
                    if (run > 0 && (from < 0 || run > ((to - from + 24) % 24) + 1))
                    {
                        from = each;
                        to = (each + run - 1) % 24;
                    }
                }
                return from >= 0;
            }
        }

        // True when they are up outside their usual hours and it is the small hours rather than an early start.
        internal static bool UpLate(DateTime now)
        {
            int from, to;
            if (!Usual(out from, out to)) return false;
            if (Within(now.Hour, from, to)) return false;
            return now.Hour >= 0 && now.Hour < 6;
        }

        private static bool Within(int hour, int from, int to)
        {
            return from <= to ? hour >= from && hour <= to : hour >= from || hour <= to;
        }

        private static string Clock(int hour)
        {
            if (hour == 0) return "midnight";
            if (hour == 12) return "midday";
            return (hour % 12 == 0 ? 12 : hour % 12) + (hour < 12 ? "am" : "pm");
        }

        // One sentence for her brain, or null while she does not know yet. No numbers she could read back as a score,
        // and nothing that tells her to bring it up: it is there so a late night means something to her.
        internal static string Line(DateTime now)
        {
            int from, to;
            if (!Usual(out from, out to)) return null;
            string said = "They are usually at this PC between " + Clock(from) + " and " + Clock((to + 1) % 24) + ".";
            if (UpLate(now)) said += " They are up later than they usually are tonight. Only say something about it if it fits, and let it go if they brush it off.";
            return said;
        }

        private static void Fresh()
        {
            if (read || string.IsNullOrEmpty(Kept)) return;
            read = true;
            try
            {
                if (!File.Exists(Kept)) return;
                foreach (string raw in File.ReadAllLines(Kept))
                {
                    string one = (raw ?? "").Trim();
                    if (one.Length == 0 || one.StartsWith("#") || one.StartsWith(">")) continue;
                    int mark = one.IndexOf(':');
                    int hour;
                    double count;
                    if (mark <= 0
                        || !int.TryParse(one.Substring(0, mark).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out hour)
                        || !double.TryParse(one.Substring(mark + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out count)
                        || hour < 0 || hour > 23 || count < 0) continue;
                    counts[hour] = count;
                }
            }
            catch (Exception) { }
        }

        private static void Write()
        {
            if (string.IsNullOrEmpty(Kept)) return;
            try
            {
                StringBuilder made = new StringBuilder();
                made.AppendLine("# When this PC is on");
                made.AppendLine();
                made.AppendLine("> One count an hour, raised while she is running and faded a little each day.");
                made.AppendLine("> She reads her usual hours out of this. Delete the file to start it again.");
                made.AppendLine("> Nothing here leaves this PC.");
                made.AppendLine();
                for (int each = 0; each < counts.Length; each++)
                    made.AppendLine(each.ToString("00", CultureInfo.InvariantCulture) + ": " + counts[each].ToString("0.##", CultureInfo.InvariantCulture));
                string folder = Path.GetDirectoryName(Kept);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) Directory.CreateDirectory(folder);
                File.WriteAllText(Kept, made.ToString(), new UTF8Encoding(false));
            }
            catch (Exception) { }
        }
    }
}
