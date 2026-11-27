using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // Her scenes: a long run of her talking (a poem, a story, her day, an interest, history; her other side's own). She
    // asks first; a yes starts it, a question pauses it, and only a clear stop, asked once more, ends it.
    internal static class Scenes
    {
        private enum Stage { None, Asked, Running, Resume, Sure }

        private static readonly object gate = new object();
        private static Stage stage = Stage.None;
        private static string kind, starter;
        private static DateTime askedAt, until, lastPart = DateTime.MinValue, lastScene = DateTime.MinValue, lastRoll = DateTime.MinValue;
        private static int parts;

        // What she offers, how she asks, and the situation her grown asks are kept under.
        private static readonly string[,] Kinds =
        {
            { "a poem you wrote, read out slowly, then a few words on what it means to you", "Want to hear a poem I've been working on?", "When she offers to read them a poem she wrote" },
            { "a story you make up as you go, with a beginning, a middle and an end", "Can I tell you a story?", "When she offers to tell them a story" },
            { "what you thought about today, wandering from one thought to the next", "Want to hear what I was thinking about today?", "When she offers to tell them what she thought about today" },
            { "one of your interests, everything you love about it", "Can I tell you about something I've been into lately?", "When she offers to tell them about one of her interests" },
            { "a bit of history you find fascinating, told like a story", "Want to hear a bit of history I found?", "When she offers to tell them a bit of history" }
        };

        // Her asks grow the way her other lines do: the stock one first, then the grown ones join it. Set by her window.
        internal static Func<string, string, string, string[], string[]> Grown = delegate(string who, string mood, string set, string[] stock) { return stock; };
        internal static Func<string> MoodNow = delegate { return ""; };
        internal static string SenpaiSet = "";

        // Her other side's, from its own file: how she asks, and what the scene is.
        internal static string[] SenpaiStarters = new string[0];
        internal static string SenpaiKind = "";

        // A personality's settings as Personality settings keeps them, and whether she's all in on her other side.
        internal static Func<string, string, string> Setting = delegate { return null; };
        internal static Func<bool> AllIn = delegate { return false; };
        internal static Random Dice = new Random();
        internal static Func<DateTime> Now = delegate { return SimTime.UtcNow; };

        internal const string Section = "Scenes";
        internal const double OftenStock = 25, MinutesStock = 8;
        private const int RollMinutes = 5, RestMinutes = 60, PartSeconds = 50, AskedMinutes = 3;

        private static double Number(string personality, string key, double stock)
        {
            double value;
            string set = Setting(personality, key);
            return set != null && double.TryParse(set.TrimStart('+'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) ? value : stock;
        }

        internal static bool On(string personality) { return !string.Equals(Setting(personality, "scenes on"), "no", StringComparison.OrdinalIgnoreCase); }

        internal static bool Busy { get { lock (gate) return stage != Stage.None; } }

        // Left quiet, once every few minutes: a roll of how often, and on a hit the line she asks with; null otherwise.
        internal static string Starter(string personality)
        {
            lock (gate)
            {
                DateTime now = Now();
                if (stage == Stage.Asked && now - askedAt > TimeSpan.FromMinutes(AskedMinutes)) stage = Stage.None;
                if (stage != Stage.None || string.IsNullOrEmpty(personality) || !On(personality)) return null;
                if (now - lastRoll < TimeSpan.FromMinutes(RollMinutes) || now - lastScene < TimeSpan.FromMinutes(RestMinutes)) return null;
                lastRoll = now;
                if (Dice.Next(100) >= Number(personality, "how often", OftenStock)) return null;
                bool other = SenpaiStarters.Length > 0 && SenpaiKind.Length > 0 && AllIn();
                string[] asks;
                if (other) { kind = SenpaiKind; asks = Grown(personality, MoodNow(), SenpaiSet.Length > 0 ? SenpaiSet : "When she asks for a scene", SenpaiStarters); }
                else { int at = Dice.Next(Kinds.GetLength(0)); kind = Kinds[at, 0]; asks = Grown(personality, MoodNow(), Kinds[at, 2], new[] { Kinds[at, 1] }); }
                if (asks == null || asks.Length == 0) return null;
                starter = asks[Dice.Next(asks.Length)];
                stage = Stage.Asked;
                askedAt = now;
                until = now.AddMinutes(Math.Max(1, Number(personality, "runtime minutes", MinutesStock)));
                return starter;
            }
        }

        private static readonly Regex Yes = new Regex(@"^\W*(y|yes|yeah|yep|yup|sure|ok|okay|please|go on|go ahead|do it|of course|absolutely|mhm|i'd love|i would love|i'd like|why not|carry on|continue|keep going)\b", RegexOptions.IgnoreCase);
        private static readonly Regex No = new Regex(@"^\W*(n|no|nah|nope|not now|maybe later|not really|i'm good|im good)\b", RegexOptions.IgnoreCase);
        private static readonly Regex Stop = new Regex(@"\b(stop|be quiet|shut up|that's enough|thats enough|enough|don'?t want to hear|not interested|boring|quiet please|leave it)\b", RegexOptions.IgnoreCase);

        // A line of theirs while a scene is about: where it goes, and what her turn note says. "" when no scene is about.
        internal static string Note(string said)
        {
            // Her own hidden asks for the next part pass through here too: they move nothing.
            if ((said ?? "").StartsWith("(", StringComparison.Ordinal) && (said ?? "").Contains("your scene")) return "";
            lock (gate)
            {
                said = said ?? "";
                DateTime now = Now();
                switch (stage)
                {
                    case Stage.Asked:
                        if (Yes.IsMatch(said)) { Begin(now); partAsked = now; return "\nThey said yes to your scene. Start it now: " + kind + ". Talk at length in your own voice, and don't end it with a question."; }
                        stage = Stage.None;
                        lastScene = now;
                        return No.IsMatch(said) ? "\nThey said no to your scene for now; take it lightly." : "";
                    case Stage.Running:
                        if (Stop.IsMatch(said)) { stage = Stage.Sure; return "\nThey sound like they want you to stop your scene. Ask if they're sure, kindly, in a few words."; }
                        stage = Stage.Resume;
                        return "\nYou were in the middle of your scene (" + kind + "). Answer what they said, then ask if they'd like you to carry on where you left off.";
                    case Stage.Resume:
                        if (Yes.IsMatch(said)) { stage = Stage.Running; lastPart = now; return "\nThey want you to carry on with your scene (" + kind + ") where you left off. Carry on now, at length."; }
                        if (No.IsMatch(said) || Stop.IsMatch(said)) { stage = Stage.Sure; return "\nThey may not want your scene to go on. Ask if they're sure, in a few words."; }
                        return "\nYou paused your scene (" + kind + ") for this. Answer them, then ask once more if they'd like you to carry on.";
                    case Stage.Sure:
                        if (Yes.IsMatch(said) || Stop.IsMatch(said)) { End(now); return "\nThey're sure: let your scene go gracefully, in a few words."; }
                        stage = Stage.Running;
                        lastPart = now;
                        return "\nThey don't want you to stop after all. Carry on with your scene (" + kind + ") where you left off.";
                }
                return "";
            }
        }

        private static void Begin(DateTime now)
        {
            stage = Stage.Running;
            parts = 0;
            lastPart = now;
            if (until < now.AddMinutes(1)) until = now.AddMinutes(MinutesStock);
        }

        private static void End(DateTime now)
        {
            stage = Stage.None;
            lastScene = now;
        }

        // While a scene runs and they're quiet, every minute or so: the hidden line that asks her for the next part, or her
        // close once its time is up; null when nothing is due.
        internal static string Next()
        {
            lock (gate)
            {
                if (stage != Stage.Running) return null;
                DateTime now = Now();
                if (now - lastPart < TimeSpan.FromSeconds(PartSeconds)) return null;
                lastPart = now;
                parts++;
                partAsked = now;
                if (now >= until)
                {
                    End(now);
                    return "(Bring your scene, " + kind + ", to its close now, in a few warm lines.)";
                }
                return "(Carry on with your scene, " + kind + ", where you left off: the next part, a few sentences, no question to them.)";
            }
        }

        // A part asked for and not yet answered: a refusal in its place ends the scene with a quiet close instead.
        private static DateTime partAsked = DateTime.MinValue;
        private static string refused;
        internal const string Closing = "Let's leave that one there for now.";
        private static readonly Regex Refusal = new Regex(@"\b(?:i can'?t (?:continue|do that|help with|write)|i cannot (?:continue|help|write|do)|i won'?t (?:continue|write)|i'm (?:sorry,? but|unable|not able)|as an ai|i am an ai|language model|not appropriate|against (?:my|the) (?:guidelines|policy|content polic))", RegexOptions.IgnoreCase);

        internal static string Spoken(string reply)
        {
            if (string.IsNullOrEmpty(reply)) return reply;
            lock (gate)
            {
                if (reply == refused) return Closing;
                if (Now() - partAsked > TimeSpan.FromMinutes(2) || !Refusal.IsMatch(reply)) return reply;
                refused = reply;
                partAsked = DateTime.MinValue;
                if (stage != Stage.None) End(Now());
                return Closing;
            }
        }

        // They spoke: the scene's next part waits a while longer.
        internal static void Heard() { lock (gate) lastPart = Now(); }

#if CHECKS
        // Asked on a roll, a yes starts it, a question pauses it and a yes carries on, a stop is asked once more and ends
        // it, its parts come no closer than a minute and it closes when its time is up; off asks nothing.
        internal static void Check(List<string> checks)
        {
            Func<string, string, string> settingWas = Setting;
            Func<DateTime> nowWas = Now;
            Random diceWas = Dice;
            try
            {
                DateTime at = new DateTime(2026, 11, 27, 12, 0, 0);
                Now = delegate { return at; };
                Dice = new Random(3);
                Dictionary<string, string> set = new Dictionary<string, string> { { "how often", "100" }, { "runtime minutes", "5" }, { "scenes on", "yes" } };
                Setting = delegate(string who, string key) { string v; return set.TryGetValue(key, out v) ? v : null; };
                lock (gate) { stage = Stage.None; lastScene = lastRoll = DateTime.MinValue; }
                string asked = Starter("Caregiver");
                bool starts = asked != null && asked.EndsWith("?", StringComparison.Ordinal) && Starter("Caregiver") == null;
                bool begun = Note("yes please").Contains("Start it now");
                at = at.AddSeconds(20);
                bool tooSoon = Next() == null;
                at = at.AddSeconds(40);
                bool part = Next() != null && Next() == null;
                bool paused = Note("wait, what was her name?").Contains("carry on where you left off");
                bool resumed = Note("yeah go on").Contains("Carry on now");
                bool sure = Note("ok stop").Contains("sure");
                bool ended = Note("yes").Contains("gracefully") && !Busy;
                lock (gate) { lastScene = lastRoll = DateTime.MinValue; }
                Starter("Caregiver");
                Note("sure");
                at = at.AddMinutes(6);
                string closing = Next();
                bool closes = closing != null && closing.Contains("close") && !Busy;
                set["scenes on"] = "no";
                lock (gate) { lastScene = lastRoll = DateTime.MinValue; }
                bool off = Starter("Caregiver") == null;
                // A scene part a brain wrapped as JSON is spoken as its words.
                // A refusal in place of a part ends the scene quietly; the same words said again stay closed; an ordinary
                // reply, or one long after any part, passes as it is.
                lock (gate) { stage = Stage.Running; partAsked = at; refused = null; }
                bool quiet = Spoken("I'm sorry, but I can't continue with that.") == Closing && !Busy && Spoken("I'm sorry, but I can't continue with that.") == Closing && Spoken("The moon rose over the hill.") == "The moon rose over the hill.";
                bool unwrapped = Glances.Unwrapped("```json\n{\"dialogue\": \"Once upon a time.\", \"more\": [\"The end.\"]}\n```") == "Once upon a time. The end." && Glances.Unwrapped("Just words.") == "Just words." && Glances.Unwrapped("{\"name\": \"timer\", \"arguments\": {\"minutes\": \"5\"}}").StartsWith("{", StringComparison.Ordinal)
                    && Brain.Clean("{\"dialogue\": \"Once upon a time.\"}") == "Once upon a time.";
                if (!starts || !begun || !tooSoon || !part || !paused || !resumed || !sure || !ended || !closes || !off || !unwrapped || !quiet)
                    throw new Exception("Her scenes went wrong: asked " + starts + " (" + asked + "), begun " + begun + ", parts spaced " + tooSoon + "/" + part + ", paused " + paused + ", resumed " + resumed + ", asked sure " + sure + ", ended " + ended + ", closes on time " + closes + ", off " + off + ", a JSON reply spoken as its words " + unwrapped + ", a refusal ends it quietly " + quiet);
                checks.Add("PASS her scenes: she asks first (\"" + asked + "\"), a yes starts it, its parts a minute apart, a question pauses it and a yes carries on, a stop is asked once more and ends it, it closes when its time is up, off asks nothing, a part a brain wrapped as JSON is spoken as its words, a refusal in place of a part ends it with a quiet close");
            }
            finally { Setting = settingWas; Now = nowWas; Dice = diceWas; lock (gate) { stage = Stage.None; lastScene = lastRoll = DateTime.MinValue; partAsked = DateTime.MinValue; refused = null; } }
        }
#endif
    }
}
