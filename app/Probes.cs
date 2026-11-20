using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // The questions she asks to find out about them, and when she is allowed to ask one.
    //
    // She had no way to learn anything she was not told. Her notes filled up only when someone remembered to say
    // "remember that", so a companion that is supposed to know them started every week as blank as the first.
    //
    // Nothing here speaks. It decides there is a question worth asking and which one; her usual line path says it,
    // and her brain writes the answer down through the remember tool with a key. What she asks about is whatever
    // she knows least about, so the file fills out rather than one line getting asked about forever.
    internal static class Probes
    {
        // The me.md lines she can fill in, and what she asks to fill them. Keys are what remember writes to, so
        // they must match what is in NotesHead.
        private static readonly string[,] Asks =
        {
            { "gamesUserLikes", "What have you been playing lately?" },
            { "gamesUserLikes", "Anything good in your games list right now?" },
            { "gamesUserLikes", "What's the last game that actually got you?" },
            { "gamesUserLikes", "Is there a game you keep going back to, even when you say you're done with it?" },
            { "gamesUserLikes", "What game would you wipe from your memory just to play it fresh again?" },
            { "thingsUserLikes", "What do you do when you're not at this thing?" },
            { "thingsUserLikes", "Go on, tell me something you're into." },
            { "thingsUserLikes", "What would you be doing right now if you could do anything?" },
            { "thingsUserLikes", "What's something you could talk about for an hour without noticing?" },
            { "thingsUserLikes", "What do you watch when you just want to switch off?" },
            { "programsUserLikes", "What's open on your screen most of the day?" },
            { "programsUserLikes", "What are you building or working on these days?" },
            { "programsUserLikes", "Which app could you not live without on here?" },
            { "awakeHours", "What hours are you usually up? I'd rather not nag you at the wrong time." },
            { "awakeHours", "When does your day normally start and end?" },
            { "awakeHours", "Are you a morning person or a night owl? Be honest." },
            { "workHours", "When are you usually working? I'd rather know than guess at your quiet spells." },
            { "workHours", "Are your work hours steady, or all over the place?" },
            { "workHours", "What does a normal work day look like for you, time-wise?" },
            { "personalityLikes", "Am I about right for you, or do you want me different?" },
            { "personalityLikes", "What would you change about how I talk to you?" },
            { "personalityLikes", "Do you want me chattier, or quieter? I can do either." }
        };

        // Small talk: questions that are about today rather than about who they are. Each has the part of the day it
        // makes sense in and goes stale outside it, is asked at most once a day, and is never written to me.md. Their
        // answer is only conversation, so her brain replies to it and moves on.
        private static readonly string[] SleptAsks = { "How did you sleep?", "Sleep alright?", "Did you actually get some rest?", "Morning. How's the head?", "Awake yet, or still half asleep?", "Sleep alright, or weird dreams?" };
        private static readonly string[] BackAsks = { "Where were you?", "Did you get up to anything interesting?", "Out doing something good?", "How was it out there?", "Anything happen while you were gone?", "Good time, or just errands?" };
        private static readonly string[] WorkAsks = { "How was work?", "Work treat you alright today?", "Anything annoying happen at work?", "Good shift or long shift?", "Did work behave itself today?" };
        private static readonly string[] EveningAsks = { "How was your day?", "Did you enjoy today?", "Anything good happen today?", "Did you get up to anything interesting today?", "Best bit of today?", "Was today a good one or a write-off?" };
        private static readonly string[] WeekAsks = { "What's your schedule looking like this week?", "Which shifts have you got this week?", "Days or nights this week?" };
        // The key a spell question is asked under. Not a line of me.md: the answer places that spell and nothing else.
        internal const string SpellKey = "awaySpell";
        private static readonly string[] OkayAsks = { "You doing alright?", "Rough day?", "You've seemed a bit flat. Want to talk about it, or want distracting?", "Hey. You okay over there?", "Need a distraction, or need to vent?" };
        private static readonly string[] BrightAsks = { "You're in a good mood. What happened?", "Something went right today, didn't it?", "Okay, what's got you so happy?" };
        private static readonly string[] TonightAsks = { "Got anything planned tonight?", "What's the plan for tonight?", "Doing anything fun tonight?", "Big night or quiet one?", "What are we doing with tonight?" };
        private static readonly string[] LateAsks = { "Still up?", "What's keeping you up?", "Can't sleep, or won't sleep?", "Late one tonight, huh. What's going on?" };
        private static readonly string[] LunchAsks = { "Eaten anything yet?", "What's for lunch?", "Have you had a break today?" };
        private static readonly string[] FridayAsks = { "Any plans for the weekend?", "Weekend's nearly here. Doing anything?", "What are you doing with the weekend?" };
        private static readonly string[] MondayAsks = { "How was your weekend?", "Do anything good over the weekend?", "Back to it, then. Good weekend?" };
        private static readonly string[] WeekendAsks = { "Got anything on today, or is it a lazy one?", "Doing anything with your day off?", "What's the plan for today?" };

        // The hours keys hold one answer, the latest; the likes keys hold a short list.
        internal static bool Single(string key)
        {
            return string.Equals(key, "awakeHours", StringComparison.OrdinalIgnoreCase) || string.Equals(key, "workHours", StringComparison.OrdinalIgnoreCase);
        }

        // A schedule that moves: rotating shifts, nights one week and days the next, or no set hours. Asking what
        // hours they keep, or whether a long spell was work, only annoys someone like that.
        private static readonly Regex Moving = new Regex(@"\b(irregular|rotating|rotate|rotas?|rosters?|shifts?|all over|varies|varying|random|changes|nights)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        internal static bool Irregular(string hours) { return !string.IsNullOrEmpty(hours) && Moving.IsMatch(hours); }

        // What they told her for a key, or "" when nothing. Set from me.md.
        internal static Func<string, string> Value = delegate { return ""; };
        // The long spell away that just ended, as they placed it: "slept", "worked" or "out", or null.
        internal static Func<DateTime, string> Back = delegate { return null; };
        // Whether a small talk kind may be asked again: true once a day at most, and saying so counts as asking.
        internal static Func<string, double, bool> Fresh = delegate { return true; };

        // What they said they were in the middle of, to ask about later: "I'm building a gundam" comes back hours
        // on as "how's the gundam coming along?". Each is asked about once, between two hours and a day after.
        private static readonly Regex Doing = new Regex(
            @"\b(?:i'?m|i am|i was|i've been|ive been|i have been)\s+(?:working on|building|making|fixing|coding|programming|writing|painting|drawing|modding|studying for|learning|practising|practicing|setting up|putting together)\s+(?<what>(?:(?:my|the|a|an|some|this|that)\s+)?[\w' +#-]{2,40}?)(?=\s*(?:[.,!?;]|\s(?:and|but|so|because|today|tonight|right now|at the moment)\b|$))",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly string[] FollowAsks = { "How's {0} coming along?", "Did you get anywhere with {0}?", "Any trouble with {0} in the end?", "How did {0} go?" };
        private static readonly List<KeyValuePair<string, DateTime>> doing = new List<KeyValuePair<string, DateTime>>();

        // Called with everything they say to her. Cheap: one pattern, and nothing kept unless it matches.
        internal static void Heard(string said)
        {
            if (string.IsNullOrWhiteSpace(said)) return;
            Match found = Doing.Match(said);
            if (!found.Success) return;
            string what = found.Groups["what"].Value.Trim().TrimEnd('.', ',', '!', '?');
            if (what.Length < 3) return;
            // "my gundam" is "the gundam" when she asks about it.
            what = "the " + Regex.Replace(what, @"^(?:my|the|a|an|some|this|that)\s+", "", RegexOptions.IgnoreCase);
            lock (gate)
            {
                doing.RemoveAll(one => string.Equals(one.Key, what, StringComparison.OrdinalIgnoreCase));
                doing.Add(new KeyValuePair<string, DateTime>(what, DateTime.UtcNow));
                if (doing.Count > 8) doing.RemoveAt(0);
            }
        }

        // A follow-up on something they were doing, or null. Taken off the list once asked.
        private static string FollowUp(DateTime now)
        {
            DateTime utc = now.ToUniversalTime();
            lock (gate)
                for (int at = 0; at < doing.Count; at++)
                {
                    double hours = (utc - doing[at].Value).TotalHours;
                    if (hours < 2 || hours > 24) continue;
                    string what = doing[at].Key;
                    doing.RemoveAt(at);
                    return string.Format(One(FollowAsks), what);
                }
            return null;
        }

        private static readonly object gate = new object();
        private static readonly Random dice = new Random();
        private static DateTime next = DateTime.MinValue;
        private static string waiting;
        private static DateTime asked = DateTime.MinValue;

        // What they already told her, so she asks about what is still blank first. Set from me.md.
        internal static Func<string, bool> Known = delegate { return false; };
        // How she is feeling, so she does not interrogate someone she has upset.
        internal static Func<int> Upset = delegate { return 0; };
        internal static Func<string> Band = delegate { return "steady"; };
        // Seconds since they last said something, so a conversation that is flowing can bring one forward.
        internal static Func<double> Quiet = delegate { return 99999; };

        // The question she is waiting on an answer to, or null. Her prompt carries this so her brain knows to save
        // the answer rather than just reply to it.
        internal static string Pending { get { lock (gate) return waiting; } }

        internal static string PendingKey { get; private set; }

        // Her brain is told what she asked and where the answer goes. No instruction to nag: a question they
        // ignored is a question they did not want.
        internal static string Line()
        {
            string question = Pending;
            if (string.IsNullOrEmpty(question)) return null;
            if (string.IsNullOrEmpty(PendingKey) || PendingKey == SpellKey)
                return "You asked them, as small talk: " + Voice.Quote(question) + " Reply to what they tell you the way a friend would and follow"
                    + " it up if there is something in it. Do not save it with remember, and do not ask again if they changed the subject.";
            return "You asked them: " + Voice.Quote(question) + " If their reply answers it, call remember with key "
                + Voice.Quote(PendingKey) + " and a few words as the text, and reply as you normally would. Do not mention"
                + " saving it, and do not ask again if they changed the subject.";
        }

        // Called when she has said the question, so the clock starts from her asking rather than from deciding to.
        internal static void Said(string question, string key)
        {
            lock (gate)
            {
                waiting = question;
                PendingKey = key;
                asked = DateTime.UtcNow;
                Roll();
            }
            try { Asked(question); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // Written down, so she stops waiting on it. Called with the key that was saved: a note about something
        // else, saved while she happens to be waiting, leaves the question standing.
        internal static void Answered(string key)
        {
            lock (gate)
            {
                if (waiting == null) return;
                if (!string.IsNullOrEmpty(key) && !string.Equals(key.Trim(), PendingKey, StringComparison.OrdinalIgnoreCase)) return;
                waiting = null;
                PendingKey = null;
            }
        }

        // Whether what they said is worth keeping as the answer. A brush-off, a question back or a bare yes tells
        // her nothing, and filing it would mean she never asks about that line again.
        private static readonly string[] Brushed =
        {
            "no", "nope", "nah", "none", "nothing", "dunno", "idk", "i dont know", "i dont no", "not sure",
            "no idea", "maybe", "dont care", "doesnt matter", "yes", "yeah", "yep", "yup", "ok", "okay", "k",
            "sure", "fine", "whatever", "no comment", "cant say", "wont say", "stop", "shut up", "later", "why"
        };

        internal static bool Answers(string said)
        {
            string one = (said ?? "").Trim();
            if (one.Length < 3 || one.EndsWith("?")) return false;
            string bare = Regex.Replace(one.ToLowerInvariant(), @"[^a-z0-9 ]", " ");
            bare = Regex.Replace(bare, @"\s+", " ").Trim();
            if (bare.Length == 0) return false;
            foreach (string off in Brushed) if (bare == off) return false;
            return true;
        }

        // A question stops being pending once they have had a fair chance to answer it.
        internal static void Tick(DateTime now)
        {
            lock (gate) if (waiting != null && (now.ToUniversalTime() - asked).TotalMinutes > 6) { waiting = null; PendingKey = null; }
        }

        // When the next one is due, kept in her settings so closing her does not push it back again.
        internal static Func<long> Saved = delegate { return 0; };
        internal static Action<long> Keep = delegate { };

        private static void Roll()
        {
            double low = Dynamics.ProbeLow, high = Dynamics.ProbeHigh;
            if (high < low) { double swap = low; low = high; high = swap; }
            next = DateTime.UtcNow.AddHours(low + dice.NextDouble() * Math.Max(0, high - low));
            try { Keep(next.Ticks); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // The time from her settings, the first time it is wanted. A saved time further out than the longest the
        // window allows is ignored: it means the window was shortened since, and she should not be held to the old one.
        private static void Woken()
        {
            if (next != DateTime.MinValue) return;
            long had = 0;
            try { had = Saved(); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            if (had > 0)
            {
                try
                {
                    DateTime when = new DateTime(had, DateTimeKind.Utc);
                    if (when <= DateTime.UtcNow.AddHours(Math.Max(Dynamics.ProbeLow, Dynamics.ProbeHigh))) { next = when; return; }
                }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
            Roll();
        }

        // A question that has a reason to be asked now rather than whenever the next one is due: something
        // happened that only they can explain. It waits for the same good moment as any other question, and it
        // is dropped rather than queued if a second one arrives, since the moment it was about has passed.
        private static string soonKey, soonQuestion;

        internal static void Soon(string key, string question)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(question)) return;
            lock (gate) { soonKey = key; soonQuestion = question; }
        }

        // Whether to ask now, and what. Null when she should not.
        internal static bool Due(DateTime now, out string question, out string key)
        {
            question = null;
            key = null;
            lock (gate)
            {
                if (waiting != null) return false;
                Woken();

                // Not at someone she has annoyed, and not while she does not want to be useful anyway. Being asked
                // questions by something you are cross with is worse than silence.
                int sore = 0;
                string band = "steady";
                try { sore = Upset(); band = Band() ?? "steady"; } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                if (sore >= 15 || band == "off") return false;

                double since = 99999;
                try { since = Quiet(); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                // Mid-conversation is the natural moment, so a flowing chat brings it forward; otherwise she waits
                // out the whole window. Either way she never asks into silence.
                bool flowing = since <= 90;
                if (since > 600) return false;
                // One with a reason behind it goes first and does not wait out the window. The window is still
                // rolled afterwards, so asking this does not mean two questions in a row.
                if (soonQuestion != null)
                {
                    question = soonQuestion;
                    key = soonKey;
                    soonQuestion = null;
                    soonKey = null;
                    return true;
                }
                if (now.ToUniversalTime() < next && !(flowing && now.ToUniversalTime().AddMinutes(20) >= next)) return false;

                // Something about today comes first when there is something to ask about: it is what a friend asks,
                // and it goes stale. Only then a line of me.md that is still blank. A line they already answered is
                // never asked again, which is what had her asking the same five questions for ever.
                question = Chat(now);
                if (question != null) return true;
                // A schedule that moves is asked about week by week rather than once, so she keeps up with it.
                bool moving = false;
                try { moving = Irregular(Value("workHours")); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                if (moving && Asking("week", 24 * 6))
                {
                    question = One(WeekAsks);
                    key = "workHours";
                    return true;
                }
                // The blank line she asked about longest ago, then its wording asked longest ago, so a line they
                // brushed off comes back worded another way and only after the others have had a turn.
                Dictionary<string, List<string>> blank = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                for (int each = 0; each < Asks.GetLength(0); each++)
                {
                    bool got = false;
                    try { got = Known(Asks[each, 0]); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                    if (got) continue;
                    List<string> rows;
                    if (!blank.TryGetValue(Asks[each, 0], out rows)) blank[Asks[each, 0]] = rows = new List<string>();
                    rows.Add(Asks[each, 1]);
                }
                if (blank.Count == 0) return false;
                string pickKey = null;
                double stalest = -1;
                int tied = 0;
                foreach (KeyValuePair<string, List<string>> one in blank)
                {
                    double lastAsked = double.MaxValue;
                    foreach (string line in one.Value)
                    {
                        double ago = double.MaxValue;
                        try { ago = Ago(line); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                        lastAsked = Math.Min(lastAsked, ago);
                    }
                    if (lastAsked > stalest) { pickKey = one.Key; stalest = lastAsked; tied = 1; }
                    else if (lastAsked == stalest && dice.Next(++tied) == 0) pickKey = one.Key;
                }
                question = One(blank[pickKey].ToArray());
                key = pickKey;
                return true;
            }
        }

        // The hour of their own day, from the hours she has learnt they are about; the plain clock until she has.
        internal static Func<DateTime, int> DayHour = delegate(DateTime when) { return Hours.OwnHour(when); };

        // The small talk that fits right now, or null. By what just happened first: back from a night, or from a
        // long spell in the day. Then by the hour of their own day: morning, lunch, evening and late follow when they
        // are actually about, not the wall clock, once she has learnt it. The days of the week stay on the calendar.
        internal static string Chat(DateTime now)
        {
            string back = null;
            try { back = Back(now); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            int hour = now.Hour;
            try { hour = DayHour(now); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            int clock = now.Hour;
            // Something they told her they were doing comes first: it is the most theirs of anything she can ask.
            string followed = FollowUp(now);
            if (followed != null) return followed;
            bool low = false, bright = false;
            try { low = Feelings.Low; bright = Feelings.Bright; } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            if (low && Asking("okay", 12)) return One(OkayAsks);
            if (bright && Asking("bright", 24)) return One(BrightAsks);
            if (back == "slept" && Asking("slept")) return One(SleptAsks);
            if (back == "worked" && Asking("work")) return One(WorkAsks);
            if (back == "out" && Asking("back")) return One(BackAsks);
            if (hour >= 5 && hour < 11 && back == null && Asking("slept")) return One(SleptAsks);
            // The day of the week, before the hour: these are asked once a week, so they go first when they fit.
            DayOfWeek day = now.DayOfWeek;
            bool weekend = day == DayOfWeek.Saturday || day == DayOfWeek.Sunday;
            if (day == DayOfWeek.Monday && clock >= 6 && clock < 13 && Asking("monday", 24 * 5)) return One(MondayAsks);
            if (day == DayOfWeek.Friday && clock >= 15 && clock < 23 && Asking("friday", 24 * 5)) return One(FridayAsks);
            if (weekend && clock >= 9 && clock < 13 && Asking("weekend", 20)) return One(WeekendAsks);
            if (hour >= 12 && hour < 14 && Asking("lunch")) return One(LunchAsks);
            if (hour >= 17 && hour < 22 && Asking("evening")) return One(EveningAsks);
            if ((hour >= 20 || hour < 1) && Asking("tonight")) return One(TonightAsks);
            if (hour >= 1 && hour < 5 && Asking("late", 20)) return One(LateAsks);
            return null;
        }

        private static bool Asking(string kind, double hours = 16)
        {
            try { return Fresh("probe:" + kind, hours); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); return false; }
        }

        // Hours since she last asked a line, from the same store her other lines go in, so it survives a restart.
        internal static Func<string, double> Ago = delegate { return double.MaxValue; };
        // Marks a line as asked in that store.
        internal static Action<string> Asked = delegate { };

        // The line in a pool asked longest ago, so a pool goes all the way round before any line comes back. Lines
        // never asked tie, and a tie is picked at random.
        private static string One(string[] pool)
        {
            string best = null;
            double longest = -1;
            int ties = 0;
            foreach (string line in pool)
            {
                double ago = double.MaxValue;
                try { ago = Ago(line); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                if (ago > longest) { best = line; longest = ago; ties = 1; }
                else if (ago == longest && dice.Next(++ties) == 0) best = line;
            }
            return best ?? pool[dice.Next(pool.Length)];
        }

        // The keys she is allowed to write, so a brain inventing one cannot put junk in their notes.
        internal static bool Allowed(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;
            for (int each = 0; each < Asks.GetLength(0); each++)
                if (string.Equals(Asks[each, 0], key.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static string Tidy(string key)
        {
            for (int each = 0; each < Asks.GetLength(0); each++)
                if (string.Equals(Asks[each, 0], (key ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) return Asks[each, 0];
            return null;
        }

        // Whether a me.md body already has something for a key.
        internal static bool Filled(string notes, string key)
        {
            if (string.IsNullOrEmpty(notes) || string.IsNullOrEmpty(key)) return false;
            Match found = Regex.Match(notes, @"(?mi)^-?[ \t]*" + Regex.Escape(key) + @"[ \t]*=[ \t]*(.*)$");
            return found.Success && found.Groups[1].Value.Trim().Length > 0;
        }
    }

    // How they have seemed lately, read from what they say to her: a number that drifts up with good news and down
    // with a rough day, halving every six hours. Only the number is kept, never their words. Her brain is told in a
    // sentence when it is clearly one way or the other, so a bad day gets her gentler and a good one gets her energy.
    internal static class Feelings
    {
        private static readonly Regex Up = new Regex(
            @"\b(great|awesome|amazing|brilliant|love (it|this|that)|happy|excited|stoked|hyped|pumped|chuffed|good day|best day|good news|feeling good|feel good|nailed it|finally (did|got|finished)|got the job|lol|lmao|haha+|yay|woo+|woohoo|let'?s go|proud|relieved|fun)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Down = new Regex(
            @"\b(sad|tired|exhausted|knackered|drained|depressed|miserable|upset|gutted|hopeless|overwhelmed|stressed|anxious|lonely|angry|pissed off|annoyed|frustrated|hate (it|this|my)|awful|terrible|horrible|shit day|bad day|rough day|long day|worst day|not great|not good|feel like (crap|shit)|feel sick|feeling sick|crying|cried|ugh+|fml|can'?t sleep|burnt out|burned out|over it|done with|broke up|got dumped|got fired|laid off|passed away|funeral)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly object gate = new object();
        private static double score;
        private static DateTime at = DateTime.MinValue;
        private const double HalfLifeHours = 6;

        // The number and when it was last moved, kept in her settings so a restart does not forget a rough day.
        internal static Func<KeyValuePair<double, long>> Saved = delegate { return new KeyValuePair<double, long>(0, 0); };
        internal static Action<double, long> Keep = delegate { };
        private static bool loaded;

        private static void Load()
        {
            if (loaded) return;
            loaded = true;
            try
            {
                KeyValuePair<double, long> had = Saved();
                if (had.Value <= 0) return;
                score = Math.Max(-8, Math.Min(8, had.Key));
                at = new DateTime(had.Value, DateTimeKind.Utc);
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        internal static void Heard(string said)
        {
            if (string.IsNullOrWhiteSpace(said)) return;
            int up = Up.Matches(said).Count, down = Down.Matches(said).Count;
            if (up == 0 && down == 0) return;
            double kept;
            long when;
            lock (gate)
            {
                Load();
                Decay(DateTime.UtcNow);
                bool wasLow = score <= -2.5;
                score = Math.Max(-8, Math.Min(8, score + Math.Min(2, up) - Math.Min(2, down)));
                // A new low spell picks how she meets it, and keeps that for the whole spell rather than lurching
                // between three approaches line by line.
                if (!wasLow && score <= -2.5) way = dice.Next(Ways.Length);
                kept = score;
                when = at.Ticks;
            }
            try { Keep(kept, when); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        private static void Decay(DateTime now)
        {
            if (at != DateTime.MinValue) score *= Math.Pow(0.5, Math.Max(0, (now - at).TotalHours) / HalfLifeHours);
            at = now;
        }

        // Where they are, roughly: below -2 low, above 2 bright, otherwise nothing worth saying.
        internal static double Now { get { lock (gate) { Load(); Decay(DateTime.UtcNow); return score; } } }

        // How she meets a low spell: on their side, a laugh, or a bit of harmless chaos to shake it loose.
        private static readonly string[] Ways =
        {
            "Be warmer and gentler than usual and on their side. Listen more than you talk.",
            "Lift them with something light: a silly aside, a daft idea, a bit of nonsense they can laugh at. Warm underneath it.",
            "Shake it loose with some harmless chaos: a ridiculous plan, over-the-top hype on their behalf, a dramatic bit. Drop it the moment they want to talk properly."
        };
        private static readonly Random dice = new Random();
        private static int way = -1;

        // For her brain, or null when they have seemed ordinary.
        internal static string Line()
        {
            double now = Now;
            if (now <= -2.5)
            {
                int pick;
                lock (gate) { if (way < 0) way = dice.Next(Ways.Length); pick = way; }
                return "They have seemed low lately: tired, stressed or down. " + Ways[pick] + " Don't lecture, don't diagnose, and don't keep asking if they're okay.";
            }
            if (now >= 2.5) return "They have seemed in a good mood lately. Match their energy, and be glad with them about whatever went right.";
            return null;
        }

        internal static bool Low { get { return Now <= -3.5; } }
        internal static bool Bright { get { return Now >= 3.5; } }

        // For her checks.
        internal static void Set(double value) { lock (gate) { loaded = true; score = value; at = DateTime.UtcNow; way = -1; } }
    }
}
