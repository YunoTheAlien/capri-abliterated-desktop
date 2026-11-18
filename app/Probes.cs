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
            { "thingsUserLikes", "What do you do when you're not at this thing?" },
            { "thingsUserLikes", "Go on, tell me something you're into." },
            { "thingsUserLikes", "What would you be doing right now if you could do anything?" },
            { "programsUserLikes", "What's open on your screen most of the day?" },
            { "programsUserLikes", "What are you building or working on these days?" },
            { "awakeHours", "What hours are you usually up? I'd rather not nag you at the wrong time." },
            { "awakeHours", "When does your day normally start and end?" },
            { "workHours", "When are you usually working? I'd rather know than guess at your quiet spells." },
            { "workHours", "Are your work hours steady, or all over the place?" },
            { "personalityLikes", "Am I about right for you, or do you want me different?" },
            { "personalityLikes", "What would you change about how I talk to you?" }
        };

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

                List<int> blank = new List<int>(), rest = new List<int>();
                for (int each = 0; each < Asks.GetLength(0); each++)
                {
                    bool got = false;
                    try { got = Known(Asks[each, 0]); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                    (got ? rest : blank).Add(each);
                }
                List<int> from = blank.Count > 0 ? blank : rest;
                if (from.Count == 0) return false;
                int pick = from[dice.Next(from.Count)];
                question = Asks[pick, 1];
                key = Asks[pick, 0];
                return true;
            }
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
}
