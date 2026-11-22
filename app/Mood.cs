using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // How willing she is and how fast feelings move (adapted from the Named Emotions mod): a cooldown and hourly
    // ceiling per act, apologies that name what they did as real repair, and willingness told to her brain in words.
    // Weights stay in her personality and mood.
    internal static class Mood
    {
        private static readonly object gate = new object();

        // Warmth is the good side of the ledger and upset the bad, both on our -60..60 tally scale with the same
        // decay as everything else, so nothing here invents a second kind of arithmetic.
        private static double warmth;
        private static double sore;
        private static DateTime cooled = DateTime.UtcNow;
        private const double HalfLifeMinutes = 45;

        // How kindness and meanness land for her now: her personality's numbers with her mood's added, never under these
        // floors, so a kindness always counts for something and she never lets mistreatment go entirely.
        internal const double KindFloor = 0.20, MeanFloor = 0.15;
        internal static double Kind { get { return Math.Max(KindFloor, Personalities.Scaled("kind returns", 1)); } }
        internal static double Mean { get { return Math.Max(MeanFloor, Personalities.Scaled("mean scale", 1)); } }

        // Per act: how long before it can count again, and how many times an hour it may count at all. An act with no
        // entry is unlimited, which keeps rare one-offs simple.
        private static readonly string[,] Limits =
        {
            //  act            seconds   per hour
            { "petted",          "20",     "40" },
            { "thrown",          "12",     "30" },
            { "insulted",        "20",     "20" },
            { "praised",         "45",     "12" },
            { "called on",       "30",     "30" },
            { "ignored",        "240",      "8" },
            // A touch on a place other than her head, each its own, as often as a pat.
            { "touched shoulders", "20",   "40" },
            { "touched middle",  "20",     "40" },
            { "touched legs",    "20",     "40" },
            { "touched feet",    "20",     "40" },
            { "tickled",         "20",     "40" }
        };

        private static readonly Dictionary<string, DateTime> lastAt = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, List<DateTime>> hourly = new Dictionary<string, List<DateTime>>(StringComparer.OrdinalIgnoreCase);

        // True when this act may count now. The caller still shows her reaction either way: being rate limited means
        // her mood does not move, not that she ignores them.
        internal static bool Counts(string act)
        {
            if (string.IsNullOrEmpty(act)) return false;
            int seconds = Limit(act, 1), perHour = Limit(act, 2);
            DateTime now = DateTime.UtcNow;
            lock (gate)
            {
                DateTime had;
                if (seconds > 0 && lastAt.TryGetValue(act, out had) && (now - had).TotalSeconds < seconds) return false;

                if (perHour > 0)
                {
                    List<DateTime> when;
                    if (!hourly.TryGetValue(act, out when)) { when = new List<DateTime>(); hourly[act] = when; }
                    when.RemoveAll(delegate(DateTime at) { return (now - at).TotalHours >= 1; });
                    if (when.Count >= perHour) return false;
                    when.Add(now);
                }
                lastAt[act] = now;
            }
            return true;
        }

        // An act's limits, for working out how fast it can land: seconds between counts and counts an hour, 0 for none.
        internal static void LimitOf(string act, out int seconds, out int perHour)
        {
            seconds = Limit(act, 1);
            perHour = Limit(act, 2);
        }

        private static int Limit(string act, int column)
        {
            for (int row = 0; row < Limits.GetLength(0); row++)
                if (string.Equals(Limits[row, 0], act, StringComparison.OrdinalIgnoreCase))
                    return int.Parse(Limits[row, column]);
            // A senpai act's limits come with it, as "seconds, per hour".
            string[] two = (Extended.Get("limit." + (act ?? "").ToLowerInvariant()) ?? "").Split(',');
            int given;
            return two.Length == 2 && int.TryParse(two[column - 1].Trim(), out given) ? given : 0;
        }

        internal static void Cool()
        {
            lock (gate)
            {
                DateTime now = DateTime.UtcNow;
                double mins = (now - cooled).TotalMinutes;
                if (mins < 1) return;
                cooled = now;
                double keep = Math.Pow(0.5, mins / HalfLifeMinutes);
                warmth *= keep;
                sore *= keep;
                if (Math.Abs(warmth) < 0.5) warmth = 0;
                if (Math.Abs(sore) < 0.5) sore = 0;
            }
        }

        // One act, already weighted by her personality and mood: positive feeds warmth, negative upset. Repetition is
        // uneven: flattery wears thin and goading builds up.
        internal static void Took(string act, int worth)
        {
            if (worth == 0) return;
            Cool();
            int again = Lately(act);
            double kind = Kind, mean = Mean;
            lock (gate)
            {
                if (worth > 0)
                {
                    // Diminishing: full the first time, then less each time, never quite nothing. Her personality's "kind
                    // returns", with her mood's added, sets how fast: under 1 a needy or clingy one keeps more of each.
                    double much = worth / (1.0 + again * 0.6 * kind);
                    if (much < 0.4) much = 0.4;
                    warmth = Math.Min(60, warmth + much);
                }
                else
                {
                    // Escalating, capped at two and a half times so a bad run cannot run away entirely. Her personality's "mean
                    // scale", with her mood's added, sets how much it hurts: a devoted one lets most of it go.
                    double much = -worth * mean * Math.Min(2.5, 1.0 + again * 0.35);
                    sore = Math.Min(60, sore + much);
                    // A bad moment takes some of the goodwill with it, but only a little: counting it twice, once in
                    // upset and again as lost warmth, was what made three slips enough to stop her helping.
                    warmth = Math.Max(-60, warmth - much * 0.2);
                }
            }
        }

        // How many times this act has already landed in the last half hour.
        private static readonly Dictionary<string, List<DateTime>> recent = new Dictionary<string, List<DateTime>>(StringComparer.OrdinalIgnoreCase);

        private static int Lately(string act)
        {
            if (string.IsNullOrEmpty(act)) return 0;
            DateTime now = DateTime.UtcNow;
            lock (gate)
            {
                List<DateTime> when;
                if (!recent.TryGetValue(act, out when)) { when = new List<DateTime>(); recent[act] = when; }
                when.RemoveAll(delegate(DateTime at) { return (now - at).TotalMinutes >= 30; });
                int had = when.Count;
                when.Add(now);
                return had;
            }
        }

        internal static int Upset { get { Cool(); lock (gate) return (int)Math.Round(sore); } }
        internal static int Warmth { get { Cool(); lock (gate) return (int)Math.Round(warmth); } }

        // 0 to 10, our scale rather than the mod's 0 to 100. 5 is where she starts.
        internal static int Willing
        {
            get
            {
                Cool();
                lock (gate)
                {
                    // Upset outweighs warmth, but not so hard that three bad moments put her at a flat no. Measured:
                    // this takes about four or five before she stops being useful, which is a row rather than a slip.
                    double w = 5 + warmth / 4.0 - sore / 4.5;
                    return (int)Math.Round(Math.Max(0, Math.Min(10, w)));
                }
            }
        }

        internal static string Band
        {
            get
            {
                int w = Willing;
                if (w >= 9) return "devoted";
                if (w >= 7) return "warm";
                if (w >= 4) return "steady";
                if (w >= 2) return "hesitant";
                if (w >= 1) return "reluctant";
                return "refusing";
            }
        }

        // What her brain is told. Prose only: she is never given a number and so never quotes one.
        internal static string Line
        {
            get
            {
                string said;
                switch (Band)
                {
                    case "devoted": said = "You want to do things for them right now. You say yes readily and look for ways to help."; break;
                    case "warm": said = "You are glad to help right now. Normal favours are easy, though you might tease while you do them."; break;
                    case "steady": said = "You will help with normal things. A big or annoying ask gets a sigh and a condition, not a flat no."; break;
                    case "hesitant": said = "You are not in the mood to be useful. Small things, maybe. Anything that takes real effort gets a \"do I have to?\" first."; break;
                    case "reluctant": said = "You do not want to be helpful right now. You might do one small thing if they ask nicely, and you are not hiding that you would rather not."; break;
                    default: said = "You are not in the mood to do things for them. You will still talk, and you say no plainly rather than sulking in silence."; break;
                }
                if (Upset >= 20) said += " You are genuinely upset with them. A real apology, one that names what they did, would change that. A bare \"sorry\" would not.";
                // Labelled as willingness, which is what every band above says; they're rules about helping, not
                // feelings.
                return "How willing you are to do things for them right now, which you never mention as a number or a score: " + said;
            }
        }

        // An apology that names what it is for. The mod's rule, kept: a bare "sorry" barely counts, naming the thing
        // counts properly, and saying it over and over does not stack.
        private static readonly Regex Named = new Regex(
            @"\b(for|about)\b|\bthrow|\bthrew|\bignor|\bleft you|\bshout|\byell|\bmean to you|\bsaid that|\bdid that|\bwas rude|\brude to you",
            RegexOptions.IgnoreCase);

        internal static int Sorry(string said)
        {
            if (string.IsNullOrEmpty(said)) return 0;
            if (!Counts("apology")) return 0;
            bool real = Named.IsMatch(said) || said.Trim().Length > 28;
            Cool();
            lock (gate)
            {
                double eased = real ? Math.Min(sore, 14) : Math.Min(sore, 4);
                sore -= eased;
                if (real) warmth = Math.Min(60, warmth + 2);
                return (int)Math.Round(eased);
            }
        }

        // Forgiven, or simply put down for long enough.
        internal static void Settle()
        {
            lock (gate) { sore = 0; }
        }

        internal static void Forget()
        {
            lock (gate)
            {
                warmth = 0;
                sore = 0;
                cooled = DateTime.UtcNow;
                lastAt.Clear();
                hourly.Clear();
                recent.Clear();
            }
        }

        // The things she does whatever mood she is in. A mood must never be able to trap them in their own app.
        private static readonly string[] Always =
        {
            "quit", "new_chat", "capri_settings", "mainframe", "speech", "listen", "voice_chat",
            "mini_player", "fake_crash", "clone", "desktop_icons", "playlist", "nap"
        };

        internal static bool MustObey(string tool)
        {
            if (string.IsNullOrEmpty(tool)) return true;
            foreach (string one in Always) if (string.Equals(one, tool, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // The same list, said to her brain, and only while she is in a mood to refuse things. The list existed and
        // nothing asked it, so the one thing it was written to prevent, a sulk that will not open her settings or
        // close her, was never actually prevented.
        internal static string Obeys
        {
            get
            {
                if (Willing >= 4) return "";
                // To her, as the rest of her prompt is. This was written about her in the third person and
                // appended straight onto a line addressing her as you, so her prompt changed person halfway
                // through a paragraph: "...not a flat no. However she feels, she always does these..."
                return " However you feel, you always do these without argument: closing you, starting a new chat,"
                    + " opening your settings or your Mainframe, your voice and listening, your mini player, your"
                    + " playlist and your nap. Being in a mood never means they cannot get at your controls.";
            }
        }
    }
}
