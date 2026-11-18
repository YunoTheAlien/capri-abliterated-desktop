using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CapriDesktop
{
    // When they were away from the PC, and whether that was sleep or work.
    //
    // Hours counts which hours of the day they are about. That draws their routine but says nothing about one
    // night: a machine left on all night counts those hours the same as a night at the desk, and nothing
    // anywhere measured a gap. So she could not tell a full night's sleep from a nap, could not say whether
    // they had had enough, and had no reason to ask about the one thing a gap cannot tell her apart.
    //
    // Keyboard and mouse decide it. A spell with no input at all is a spell they were not here. Short ones are
    // ignored: a film, a meal and a long read are all away from the keyboard and none of them is sleep.
    //
    // A long spell is the ambiguous one. Seven hours and more is a night's sleep or a working day, and the two
    // mean opposite things, so one of those gets a question rather than a guess.
    //
    // Nothing here is sent anywhere. It is a handful of lines in a file beside her settings, and what reaches
    // her brain is prose with no numbers to read back.
    internal static class Away
    {
        internal const string FileName = "away.txt";
        internal static string Kept;

        // No input for this long and they are not at the PC. Ten minutes is what her check-ins already call away.
        private const double AwayMinutes = 10;
        // Shorter than this is not worth a thought: lunch, a film, the shops.
        private const double ShortestHours = 2.5;
        // A night's sleep and a working day both live above this, which is why it is the line where she asks.
        private const double UnsureHours = 7;
        // How many spells are kept. Enough to see a pattern, few enough that the file stays readable.
        private const int Keeps = 12;

        internal sealed class Spell
        {
            internal DateTime From;
            internal DateTime To;
            // What it was: "", "slept", "worked" or "out". Set from their own answer, never from a guess.
            internal string Called = "";
            internal bool Asked;
            internal double Length { get { return (To - From).TotalHours; } }
        }

        private static readonly object gate = new object();
        private static readonly List<Spell> spells = new List<Spell>();
        private static bool read;
        private static DateTime seen = DateTime.MinValue;
        private static DateTime wrote = DateTime.MinValue;

        // Milliseconds since the last key or mouse. Set by her, so this file needs nothing from Windows.
        internal static Func<long> Idle = delegate { return 0; };

        // The last spell she knows about, or null.
        internal static Spell Last
        {
            get { lock (gate) { Fresh(); return spells.Count == 0 ? null : spells[spells.Count - 1]; } }
        }

        // True while they are at the PC now.
        internal static bool Here
        {
            get { long idle = 0; try { idle = Idle(); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); } return idle < AwayMinutes * 60000; }
        }

        // Called on her tick. Cheap: it looks at one number, and only writes a file when something changed.
        internal static void Watch(DateTime now)
        {
            long idle;
            try { idle = Idle(); }
            catch (Exception) { return; }
            lock (gate)
            {
                Fresh();
                if (idle >= AwayMinutes * 60000) return;
                // They are here. Their last moment of being here was either a tick ago, which is nothing, or long
                // enough ago to be a spell away. Her being closed for that time reads the same way, which is
                // right: the file is the record, not her being open.
                DateTime was = seen;
                seen = now;
                if (was == DateTime.MinValue) { Write(now); return; }
                double gap = (now - was).TotalHours;
                if (gap < ShortestHours) { if ((now - wrote).TotalMinutes >= 10) Write(now); return; }
                spells.Add(new Spell { From = was, To = now });
                while (spells.Count > Keeps) spells.RemoveAt(0);
                Write(now);
            }
        }

        // Their own word about a spell, from the question she asked. Anything but working is left as sleep only
        // when the spell looks like a night; a daytime answer of "no" says they were out, not that they slept.
        internal static void Told(string said)
        {
            if (string.IsNullOrEmpty(said)) return;
            string plain = said.ToLowerInvariant();
            bool worked = plain.IndexOf("work", StringComparison.Ordinal) >= 0 || plain.IndexOf("shift", StringComparison.Ordinal) >= 0
                || plain.IndexOf("job", StringComparison.Ordinal) >= 0 || plain.IndexOf("office", StringComparison.Ordinal) >= 0
                || plain.IndexOf("uni", StringComparison.Ordinal) >= 0 || plain.IndexOf("school", StringComparison.Ordinal) >= 0
                || plain.IndexOf("college", StringComparison.Ordinal) >= 0;
            bool slept = plain.IndexOf("sleep", StringComparison.Ordinal) >= 0 || plain.IndexOf("slept", StringComparison.Ordinal) >= 0
                || plain.IndexOf("bed", StringComparison.Ordinal) >= 0 || plain.IndexOf("nap", StringComparison.Ordinal) >= 0;
            lock (gate)
            {
                Fresh();
                for (int each = spells.Count - 1; each >= 0; each--)
                {
                    if (!spells[each].Asked || spells[each].Called.Length > 0) continue;
                    spells[each].Called = worked && !slept ? "worked" : slept && !worked ? "slept" : Nightly(spells[each]) ? "slept" : "out";
                    Write(DateTime.Now);
                    return;
                }
            }
        }

        // A spell she has not been able to place, that is worth asking about: long enough to be either a night or
        // a working day, recent enough that asking is not odd, and asked about only once.
        internal static bool Ask(DateTime now, out string question)
        {
            question = null;
            lock (gate)
            {
                Fresh();
                if (spells.Count == 0) return false;
                Spell last = spells[spells.Count - 1];
                if (last.Asked || last.Called.Length > 0) return false;
                if (last.Length < UnsureHours) return false;
                if ((now - last.To).TotalHours > 3) return false;
                last.Asked = true;
                Write(now);
                question = Nightly(last)
                    ? "You were gone a good while there. Were you asleep, or working?"
                    : "You were off for most of that. Work, or were you out?";
                return true;
            }
        }

        // Whether a spell sits in the part of the day they are not usually about, which is the shape of a night
        // whatever hours they keep. Falls back to the small hours while she does not know their routine yet.
        private static bool Nightly(Spell spell)
        {
            int from, to;
            if (Hours.Usual(out from, out to))
            {
                int covered = 0, counted = 0;
                for (double step = 0; step < spell.Length; step += 1)
                {
                    int hour = spell.From.AddHours(step).Hour;
                    counted++;
                    if (!Within(hour, from, to)) covered++;
                }
                if (counted > 0) return covered * 2 >= counted;
            }
            int began = spell.From.Hour;
            return began >= 21 || began <= 4;
        }

        private static bool Within(int hour, int from, int to)
        {
            return from <= to ? hour >= from && hour <= to : hour >= from || hour <= to;
        }

        // How long they are usually away in one stretch, from the spells she has that look like nights. Null until
        // there are a few, so one bad night is not called usual.
        private static double Typical()
        {
            List<double> nights = new List<double>();
            foreach (Spell spell in spells)
                if (spell.Called != "worked" && spell.Called != "out" && spell.Length >= ShortestHours && Nightly(spell)) nights.Add(spell.Length);
            if (nights.Count < 3) return -1;
            nights.Sort();
            return nights[nights.Count / 2];
        }

        private static string Roughly(double hours)
        {
            if (hours < 1.5) return "about an hour";
            if (hours < 2.5) return "a couple of hours";
            int whole = (int)Math.Round(hours);
            return "about " + whole + " hours";
        }

        // One or two sentences for her brain, or null while there is nothing to say. No numbers she can read back
        // as a score beyond a rough count of hours, and nothing telling her to bring it up.
        internal static string Line(DateTime now)
        {
            lock (gate)
            {
                Fresh();
                if (spells.Count == 0) return null;
                Spell last = spells[spells.Count - 1];
                // Only while it is the thing that just happened. A gap from last week is not news.
                if ((now - last.To).TotalHours > 4) return null;
                if (last.Called == "worked") return "They were working for " + Roughly(last.Length) + " before this, and have just got back to the PC.";
                if (last.Called == "out") return "They were out for " + Roughly(last.Length) + " and have just got back.";
                bool night = Nightly(last);
                string said = night
                    ? "They were away from the PC for " + Roughly(last.Length) + " overnight and have just come back, so they have probably just slept."
                    : "They were away from the PC for " + Roughly(last.Length) + " and have just come back.";
                if (night)
                {
                    double usual = Typical();
                    if (last.Length < 6 && (usual < 0 || last.Length < usual - 1)) said += " That is on the short side for them, so they may be tired. Say something kind about it only if it fits, and drop it if they brush it off.";
                    else if (usual > 0 && last.Length > usual + 1.5) said += " That is a longer sleep than they usually get.";
                }
                return said;
            }
        }

        // The hour their day usually ends, worked out from the nights she has seen rather than from the clock:
        // the hour the longest night of the last dozen began. Minus one when she has nothing, so her caller keeps
        // whatever it was going to use.
        internal static int SleepsAt()
        {
            lock (gate)
            {
                Fresh();
                List<int> starts = new List<int>();
                foreach (Spell spell in spells)
                    if (spell.Called != "worked" && spell.Called != "out" && spell.Length >= ShortestHours && Nightly(spell)) starts.Add(spell.From.Hour);
                if (starts.Count < 3) return -1;
                starts.Sort();
                return starts[starts.Count / 2];
            }
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
                    string[] bits = one.Split('|');
                    for (int each = 0; each < bits.Length; each++) bits[each] = bits[each].Trim();
                    if (bits.Length == 2 && bits[0] == "here")
                    {
                        DateTime when;
                        if (DateTime.TryParse(bits[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out when)) seen = when;
                        continue;
                    }
                    if (bits.Length < 3 || bits[0] != "away") continue;
                    DateTime from, to;
                    if (!DateTime.TryParse(bits[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out from)) continue;
                    if (!DateTime.TryParse(bits[2], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out to)) continue;
                    if (to <= from) continue;
                    Spell spell = new Spell { From = from, To = to };
                    if (bits.Length > 3) spell.Called = bits[3] == "slept" || bits[3] == "worked" || bits[3] == "out" ? bits[3] : "";
                    if (bits.Length > 4) spell.Asked = bits[4] == "asked";
                    spells.Add(spell);
                }
                while (spells.Count > Keeps) spells.RemoveAt(0);
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        private static void Write(DateTime now)
        {
            wrote = now;
            if (string.IsNullOrEmpty(Kept)) return;
            try
            {
                StringBuilder made = new StringBuilder();
                made.AppendLine("# When this PC was left alone");
                made.AppendLine();
                made.AppendLine("> One line per spell with no keyboard or mouse at all, longest kept last, and the last");
                made.AppendLine("> moment there was any. She reads sleep and work out of these. Delete the file to");
                made.AppendLine("> start it again. Nothing here leaves this PC.");
                made.AppendLine();
                made.AppendLine("here | " + seen.ToString("o", CultureInfo.InvariantCulture));
                foreach (Spell spell in spells)
                    made.AppendLine("away | " + spell.From.ToString("o", CultureInfo.InvariantCulture) + " | " + spell.To.ToString("o", CultureInfo.InvariantCulture)
                        + " | " + (spell.Called.Length == 0 ? "-" : spell.Called) + " | " + (spell.Asked ? "asked" : "-"));
                string folder = Path.GetDirectoryName(Kept);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) Directory.CreateDirectory(folder);
                File.WriteAllText(Kept, made.ToString(), new UTF8Encoding(false));
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // For her checks: everything back to how it starts, with no file behind it.
        internal static void Forget()
        {
            lock (gate)
            {
                spells.Clear();
                seen = DateTime.MinValue;
                wrote = DateTime.MinValue;
                read = true;
            }
        }

        // For her checks: a spell put in by hand, so the lines and the question can be driven without waiting a night.
        internal static void Add(DateTime from, DateTime to, string called)
        {
            lock (gate)
            {
                Fresh();
                spells.Add(new Spell { From = from, To = to, Called = called ?? "" });
                while (spells.Count > Keeps) spells.RemoveAt(0);
            }
        }
    }
}
