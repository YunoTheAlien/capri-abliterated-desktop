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
        private static DateTime cooled = SimTime.UtcNow;
        private const double HalfLifeMinutes = 45;

        // How kindness and meanness land for her now: her personality's numbers with her mood's added, never under these
        // floors, so a kindness always counts for something and she never lets mistreatment go entirely.
        internal const double KindFloor = 0.20, MeanFloor = 0.15;
        internal static double Kind { get { return Math.Max(KindFloor, Personalities.Scaled("kind returns", 1)); } }
        internal static double Mean { get { return Math.Max(MeanFloor, Personalities.Scaled("mean scale", 1)); } }

        private static readonly Dictionary<string, DateTime> lastAt = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, List<DateTime>> hourly = new Dictionary<string, List<DateTime>>(StringComparer.OrdinalIgnoreCase);

        // True when this act may count now. The caller still shows her reaction either way: being rate limited means
        // her mood does not move, not that she ignores them.
        internal static bool Counts(string act)
        {
            if (string.IsNullOrEmpty(act)) return false;
            int seconds = Limit(act, 1), perHour = Limit(act, 2);
            DateTime now = SimTime.UtcNow;
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

        // Per act, from the act list: how long before it can count again, and how many times an hour it may count at
        // all. An act with no limits is unlimited, which keeps rare one-offs simple.
        private static int Limit(string act, int column)
        {
            Acts.Act listed = Acts.Find(act);
            if (listed != null && listed.Limited) return column == 1 ? listed.Seconds : listed.PerHour;
            // A senpai act's limits come with it, as "seconds, per hour".
            string[] two = (Extended.Get("limit." + (act ?? "").ToLowerInvariant()) ?? "").Split(',');
            int given;
            return two.Length == 2 && int.TryParse(two[column - 1].Trim(), out given) ? given : 0;
        }

        internal static void Cool()
        {
            lock (gate)
            {
                DateTime now = SimTime.UtcNow;
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
        // uneven: flattery wears thin and goading builds up, both leaning on how willing she is. What it moved goes
        // into sum, for her log.
        internal static void Took(string act, int worth, Acts.Sum sum)
        {
            if (worth == 0) { sum.Moved("moves nothing", Peek()); return; }
            Cool();
            int window, shortest, longest;
            int again = Lately(act, out window, out shortest, out longest);
            double kind = Kind, mean = Mean;
            Numbers numbers = Numbers.Now();
            lock (gate)
            {
                int willing = WillingOf(warmth, sore);
                sum.Repeat(again, window, shortest, longest);
                if (worth > 0)
                {
                    double gain, slow;
                    double much = Kindly(worth, again, kind, willing, numbers, out gain, out slow), was = warmth;
                    warmth = Math.Min(60, warmth + much);
                    sum.Moved("willing " + willing + ", gain " + Acts.N(gain) + ", fade " + Acts.N(slow) + ", kind returns " + Acts.N(kind) + " -> warmth " + Acts.Signed(much) + " (" + Acts.F(was) + " to " + Acts.F(warmth) + "), upset " + Acts.F(sore), WillingOf(warmth, sore));
                }
                else
                {
                    double hurt, build, grows;
                    double much = Meanly(worth, again, mean, willing, numbers, out hurt, out build, out grows), wasUpset = sore, was = warmth;
                    sore = Math.Min(60, sore + much);
                    // A bad moment takes some of the goodwill with it, but only a little: counting it twice, once in
                    // upset and again as lost warmth, was what made three slips enough to stop her helping.
                    warmth = Math.Max(-60, warmth - much * 0.2);
                    sum.Moved("willing " + willing + ", hurt " + Acts.N(hurt) + ", build " + Acts.N(build) + ", mean scale " + Acts.N(mean) + ", grows x" + Acts.N(grows) + " -> upset " + Acts.Signed(much) + " (" + Acts.F(wasUpset) + " to " + Acts.F(sore) + "), warmth " + Acts.Signed(-much * 0.2) + " (" + Acts.F(was) + " to " + Acts.F(warmth) + ")", WillingOf(warmth, sore));
                }
            }
        }

        // The repeat numbers from "How she reacts": her personality's with her mood's added, today's values when
        // unset. Pull and slows stay within 0 to 0.2, so gain and its kin never go below nothing at willing 0 or 10.
        internal sealed class Numbers
        {
            internal double Fades = 0.6, Least = 0.4, Grows = 0.35, Most = 2.5, Pull = 0.05, Slows = 0.1;

            internal static Numbers Now()
            {
                Numbers n = new Numbers();
                n.Fades = Math.Max(0, Personalities.Scaled("like fades", n.Fades));
                n.Least = Math.Max(0, Personalities.Scaled("like least", n.Least));
                n.Grows = Math.Max(0, Personalities.Scaled("dislike grows", n.Grows));
                n.Most = Math.Max(1, Personalities.Scaled("dislike most", n.Most));
                n.Pull = Math.Max(0, Math.Min(0.2, Personalities.Scaled("willing pull", n.Pull)));
                n.Slows = Math.Max(0, Math.Min(0.2, Personalities.Scaled("willing slows", n.Slows)));
                return n;
            }
        }

        // A kind act's warmth: full the first time, less each repeat, never under "like least". The warmer she is,
        // the more it lands (gain) and the slower it fades (slow); at willing 5 both are 1.
        internal static double Kindly(int worth, int again, double kind, int willing, Numbers n, out double gain, out double slow)
        {
            gain = 1 + n.Pull * (willing - 5);
            slow = 1 - n.Slows * (willing - 5);
            return Math.Max(n.Least, worth * gain / (1.0 + again * n.Fades * kind * slow));
        }

        // A mean act's upset, growing each repeat up to "dislike most" times. The warmer she is, the less it hurts and
        // the slower it builds; at willing 5 both are 1.
        internal static double Meanly(int worth, int again, double mean, int willing, Numbers n, out double hurt, out double build, out double grows)
        {
            hurt = 1 - n.Pull * (willing - 5);
            build = 1 - n.Slows * (willing - 5);
            grows = Math.Min(n.Most, 1.0 + again * n.Grows * build);
            return -worth * mean * hurt * grows;
        }

        // The repeat window's range now, in minutes, from her personality's "repeat window": upset raises its
        // shortest and warmth lowers its longest, so a good day forgives fast and a bad one piles up.
        internal static void WindowNow(out int shortest, out int longest)
        {
            int low, high;
            Personalities.Range("repeat window", 20, 60, out low, out high);
            double tilt;
            lock (gate) tilt = Math.Max(-1, Math.Min(1, (sore - warmth) / 60.0));
            shortest = (int)Math.Round(tilt > 0 ? low + (high - low) * tilt : low);
            longest = (int)Math.Round(tilt < 0 ? high + (high - low) * tilt : high);
        }

        // Each act's run: when it landed, and the window picked fresh in the range when the run started.
        private static readonly Dictionary<string, List<DateTime>> recent = new Dictionary<string, List<DateTime>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> windows = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Random dice = new Random();

        // How many times this act has already landed in its run, with the run's window and the range now.
        private static int Lately(string act, out int window, out int shortest, out int longest)
        {
            WindowNow(out shortest, out longest);
            window = 0;
            if (string.IsNullOrEmpty(act)) return 0;
            DateTime now = SimTime.UtcNow;
            lock (gate)
            {
                List<DateTime> when;
                if (!recent.TryGetValue(act, out when)) { when = new List<DateTime>(); recent[act] = when; }
                int run;
                windows.TryGetValue(act, out run);
                when.RemoveAll(delegate(DateTime at) { return (now - at).TotalMinutes >= run; });
                if (when.Count == 0) { run = dice.Next(shortest, longest + 1); windows[act] = run; }
                window = run;
                int had = when.Count;
                when.Add(now);
                return had;
            }
        }

        // Her mood as it stands, kept whole so a counter run can put it back as if none of its acts happened.
        internal sealed class Kept
        {
            internal double Warmth, Sore;
            internal DateTime Cooled;
            internal Dictionary<string, DateTime> LastAt;
            internal Dictionary<string, List<DateTime>> Hourly, Recent;
        }

        internal static Kept Keep()
        {
            lock (gate)
                return new Kept { Warmth = warmth, Sore = sore, Cooled = cooled, LastAt = new Dictionary<string, DateTime>(lastAt, StringComparer.OrdinalIgnoreCase), Hourly = Copied(hourly), Recent = Copied(recent) };
        }

        internal static void PutBack(Kept kept)
        {
            if (kept == null) return;
            lock (gate)
            {
                warmth = kept.Warmth;
                sore = kept.Sore;
                cooled = kept.Cooled;
                lastAt.Clear();
                foreach (KeyValuePair<string, DateTime> one in kept.LastAt) lastAt[one.Key] = one.Value;
                Restore(hourly, kept.Hourly);
                Restore(recent, kept.Recent);
            }
        }

        private static Dictionary<string, List<DateTime>> Copied(Dictionary<string, List<DateTime>> from)
        {
            Dictionary<string, List<DateTime>> copy = new Dictionary<string, List<DateTime>>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, List<DateTime>> one in from) copy[one.Key] = new List<DateTime>(one.Value);
            return copy;
        }

        private static void Restore(Dictionary<string, List<DateTime>> into, Dictionary<string, List<DateTime>> from)
        {
            into.Clear();
            foreach (KeyValuePair<string, List<DateTime>> one in from) into[one.Key] = new List<DateTime>(one.Value);
        }

        // For the checks: her warmth and upset set outright.
        internal static void Feel(double warm, double upset)
        {
            lock (gate)
            {
                warmth = warm;
                sore = upset;
                cooled = SimTime.UtcNow;
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
                lock (gate) return WillingOf(warmth, sore);
            }
        }

        // Upset outweighs warmth, but not so hard that three bad moments put her at a flat no. Measured: this takes
        // about four or five before she stops being useful, which is a row rather than a slip.
        private static int WillingOf(double warm, double upset)
        {
            double w = 5 + warm / 4.0 - upset / 4.5;
            return (int)Math.Round(Math.Max(0, Math.Min(10, w)));
        }

        // Willing as Cool would leave it, without cooling, so reading it for her log moves nothing.
        private static int Peek()
        {
            lock (gate)
            {
                double warm = warmth, upset = sore, mins = (SimTime.UtcNow - cooled).TotalMinutes;
                if (mins >= 1)
                {
                    double keep = Math.Pow(0.5, mins / HalfLifeMinutes);
                    warm *= keep;
                    upset *= keep;
                    if (Math.Abs(warm) < 0.5) warm = 0;
                    if (Math.Abs(upset) < 0.5) upset = 0;
                }
                return WillingOf(warm, upset);
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
                cooled = SimTime.UtcNow;
                lastAt.Clear();
                hourly.Clear();
                recent.Clear();
                windows.Clear();
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
