using System;
using System.Collections.Generic;
using System.Globalization;

namespace CapriDesktop
{
    // Her six needs while a pet game runs: 0 to 100, raised through her act maths, run down hourly at her personality's
    // and mood's rates, each changing what she does when low. With no pet game, none of it is kept or felt.
    internal static class PetStats
    {
        internal static readonly string[] Names = { "energy", "fullness", "stimulation", "social", "comfort", "cleanliness" };

        // Where the levels are kept ("energy=70", and "at=" the last tick): her settings, set by her start-up.
        private static List<string> memory = new List<string>();
        internal static Func<List<string>> Load = delegate { return memory; };
        internal static Action<List<string>> Store = delegate(List<string> rows) { memory = rows; };

        // Whether a pet game is running, whether she is resting now, and an act's worth to her now, set by her start-up.
        internal static Func<bool> Running = delegate { return false; };
        internal static Func<bool> Resting = delegate { return false; };
        // How many times as fast a need fills, from its Toggles slider (1 to 10); her start-up sets it.
        internal static Func<string, double> Times = delegate { return 1; };

        // A pack's stock numbers before a change to them, by pack and key: a Your Own row at one is theirs untouched.
        private static readonly Dictionary<string, HashSet<string>> was = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        internal static void Was(string pack, string key, string value)
        {
            lock (was)
            {
                HashSet<string> values;
                if (!was.TryGetValue(pack + "|" + key, out values)) was[pack + "|" + key] = values = new HashSet<string>();
                values.Add(value);
            }
        }

        internal static bool WasStock(string pack, string key, string value)
        {
            Seed();
            lock (was) { HashSet<string> values; return was.TryGetValue(pack + "|" + key, out values) && values.Contains(value); }
        }
        // Whether nobody has been about for a while, and when this run of hers began: time before it was time she was closed.
        internal static Func<bool> Idle = delegate { return false; };
        internal static DateTime StartedAt = DateTime.UtcNow;
        internal static Func<string, int> Worth = delegate { return 0; };

        // Told after any level moves, for a pet game's window and points.
        internal static event Action Changed;

        private static readonly object gate = new object();

        // Which act fills which need, before its kind is looked at. A mod's act names its own with Map.
        private static readonly Dictionary<string, string> named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "petted", "social" }, { "praised", "social" }, { "called on", "social" }, { "uppies", "social" },
            { "insulted", "comfort" }, { "thrown", "comfort" }, { "ignored", "social" }, { "wash", "cleanliness" }
        };

        // And by kind: anything she eats (kind food, in groups fed, treat and nuggies) fills her, gifts and touch are comfort.
        private static readonly Dictionary<string, string> kinds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "food", "fullness" }, { "fed", "fullness" }, { "treat", "fullness" }, { "nuggies", "fullness" }, { "gift", "comfort" }, { "touch", "comfort" }
        };

        // What raises a need without being an act of hers, shown in the list of what helps.
        private static readonly string[,] Others = { { "stimulation", "A game" }, { "stimulation", "A story" }, { "stimulation", "Music" }, { "energy", "Rest" } };

        // Each plain pack's and mood's stock needs, in Personalities.NeedKeys' order: energy, fullness, stimulation, social,
        // comfort and cleanliness falls an hour, then low, high and gain. A mood's are added to its pack's, so it names only
        // what it changes. The senpai side registers its own when it attaches.
        private static bool seeded;

        internal static void Seed()
        {
            lock (gate) { if (seeded) return; seeded = true; }
            string[,] packs =
            {
                { Personalities.Stock, "2", "12", "8", "6", "3", "1.5", "30", "80", "20", "25", "0.25" },
                { "Caregiver", "2", "12", "6", "5", "2", "1.5", "30", "80", "20", "25", "0.25" },
                { "Welcome Home", "2", "12", "6", "6", "2", "1.5", "30", "80", "20", "25", "0.25" },
                { "Devoted", "2", "12", "7", "9", "3", "1.5", "30", "80", "24", "25", "0.25" },
                { "Emo", "2", "10", "7", "7", "5", "1.5", "35", "85", "17", "25", "0.25" },
                { "Bratty", "2", "12", "11", "7", "4", "2", "30", "80", "20", "25", "0.25" },
                { "Sassy", "2", "12", "9", "5", "3", "1.5", "30", "80", "20", "25", "0.25" },
                { "Feral", "2", "14", "10", "4", "3", "2.5", "25", "75", "20", "25", "0.25" },
            };
            // Each pack's energy falls, needs gain and energy rests as they stood before: a Your Own row still at one goes once.
            string[,] was =
            {
                { Personalities.Stock, "4", "6", "8" },
                { "Caregiver", "3", "6", "8" },
                { "Welcome Home", "3", "6", "8" },
                { "Devoted", "4", "7", "8" },
                { "Emo", "5", "5", "8" },
                { "Bratty", "5", "6", "8" },
                { "Sassy", "4", "6", "8" },
                { "Feral", "6", "6", "8" }
            };
            for (int at = 0; at < was.GetLength(0); at++) { Was(was[at, 0], "energy falls", was[at, 1]); Was(was[at, 0], "needs gain", was[at, 2]); Was(was[at, 0], "energy rests", was[at, 3]); Was(was[at, 0], "energy rests", "2"); }
            for (int at = 0; at < packs.GetLength(0); at++)
                Personalities.StockNeeds(packs[at, 0], false, false, packs[at, 1], packs[at, 2], packs[at, 3], packs[at, 4], packs[at, 5], packs[at, 6], packs[at, 7], packs[at, 8], packs[at, 9], packs[at, 10], packs[at, 11]);
            string[,] moods =
            {
                { "Sleepy", "3", "", "", "", "", "" }, { "Hyper", "2", "", "4", "", "", "" }, { "Moody", "", "", "", "", "2", "" },
                { "Clingy", "", "", "", "4", "", "" }, { "Needy", "", "", "", "3", "2", "" }, { "Playful", "", "", "3", "", "", "" },
                { "Touchy", "", "", "", "", "2", "" }, { "Affectionate", "", "", "", "2", "", "" }, { "Mischievous", "", "", "3", "", "", "" },
                { "Focused", "", "", "-3", "-2", "", "" }, { "Melancholy", "1", "", "", "", "2", "" }, { "Furious", "", "", "", "", "3", "" },
                { "Jealous", "", "", "", "3", "", "" }, { "Smug", "", "", "", "-1", "", "" },
            };
            for (int at = 0; at < moods.GetLength(0); at++)
                Personalities.StockNeeds(moods[at, 0], true, false, moods[at, 1], moods[at, 2], moods[at, 3], moods[at, 4], moods[at, 5], moods[at, 6]);
        }

        internal static void Map(string act, string need) { if (Array.IndexOf(Names, need) >= 0) lock (gate) named[act] = need; }

        internal static string NeedOf(string act)
        {
            string need;
            lock (gate) if (named.TryGetValue(act ?? "", out need)) return need;
            Acts.Act found = Acts.Find(act);
            return found != null && kinds.TryGetValue(found.Kind ?? "", out need) ? need : null;
        }

        // A number from Her needs: her personality's with her mood's added, her stock pack's when her file says nothing.
        internal static double Rate(string key) { Seed(); return Personalities.Scaled(key, Personalities.StockNeed(Personalities.Stock, key, 0)); }

        internal static int Level(string need)
        {
            string row = Row(need);
            double level;
            return row != null && double.TryParse(row, NumberStyles.Float, CultureInfo.InvariantCulture, out level) ? (int)Math.Round(level) : 50;
        }

        internal static bool Low(string need) { return Running() && Level(need) < Rate("needs low"); }

        internal static bool High(string need) { return Running() && Level(need) > Rate("needs high"); }

        // An act as it lands, worth already through her maths: the need it fills moves by its worth times her gain. One she
        // disliked takes comfort and social down whatever it is, and harder than a liked one lifts: bad weighs more than
        // good (Baumeister and others, 2001).
        internal const double BadWeighs = 1.5;

        internal static void Took(string act, int worth)
        {
            if (!Running()) return;
            string need = NeedOf(act);
            if (worth < 0)
            {
                double hit = -worth * Rate("needs gain") * BadWeighs;
                bool social = need == "social";
                Move(social ? "social" : "comfort", -hit);
                Move(social ? "comfort" : "social", -hit / 2);
            }
            if (need == null) return;
            // Eating fills her and a wash cleans her whatever she thinks of it; liking it fills more.
            if (need == "fullness" || need == "cleanliness") { Move(need, Math.Max(1, worth) * Rate("needs gain") * Times(need)); return; }
            if (worth > 0) Move(need, worth * Rate("needs gain") * Times(need));
        }

        // Something that isn't an act raised a need: a game, a story, music.
        internal static void Raise(string need, int worth)
        {
            if (!Running() || Array.IndexOf(Names, need) < 0) return;
            Move(need, worth * Rate("needs gain") * Times(need));
        }

        // The hour's run-down since the last look; resting, idle or closed, energy comes back at its rest rate. Time she was
        // closed takes the others down only a little. Cleanliness low takes comfort down at its own rate too.
        internal static void Tick(DateTime now)
        {
            if (!Running()) return;
            string at = Row("at");
            long was;
            bool known = at != null && long.TryParse(at, out was) && was > 0 && was <= now.Ticks;
            if (!known) { Set("at", now.Ticks.ToString(CultureInfo.InvariantCulture)); return; }
            long.TryParse(at, out was);
            // A minute at least between looks, so her settings aren't written every frame.
            if (now.Ticks - was < TimeSpan.TicksPerMinute) return;
            Set("at", now.Ticks.ToString(CultureInfo.InvariantCulture));
            double hours = Math.Min(48, (now.Ticks - was) / (double)TimeSpan.TicksPerHour);
            bool closed = was < StartedAt.Ticks, resting = !closed && Resting(), idle = !closed && Idle();
            double closedShare = closed ? Math.Max(0, Math.Min(1, Rate("closed falls"))) : 1;
            foreach (string need in Names)
            {
                double fall = Rate(need + " falls") * hours * closedShare;
                // Resting, idle or closed, her energy comes back at its rest rate, as fast again as its Toggles slider says.
                if (need == "energy" && (resting || closed || idle)) fall = -Rate("energy rests") * hours * Times(need);
                if (need == "comfort" && Low("cleanliness")) fall += Rate("cleanliness falls") * hours * closedShare;
                if (fall != 0) Move(need, -fall, false);
            }
            Tell();
            Ask();
        }

        // How far under low a need is, as a part of low: 1 when it isn't low, less the lower it goes, never under the share
        // her high mark is of her low one, so a need at 0 is a few times sooner, not endlessly.
        internal static double Under(string need)
        {
            if (!Low(need)) return 1;
            double low = Math.Max(1, Rate("needs low")), most = Math.Max(1, Rate("needs high") / low);
            return Math.Max(1 / most, Math.Max(1, Level(need)) / low);
        }

        // How much more often a mood comes up in Dynamic's turn while a need is low: Sleepy for little energy, Clingy and
        // Needy for little company, Playful and Mischievous for little to do.
        internal static double MoodWeight(string mood)
        {
            if (!Running()) return 1;
            string need = string.Equals(mood, "Sleepy", StringComparison.OrdinalIgnoreCase) ? "energy"
                : string.Equals(mood, "Clingy", StringComparison.OrdinalIgnoreCase) || string.Equals(mood, "Needy", StringComparison.OrdinalIgnoreCase) ? "social"
                : string.Equals(mood, "Playful", StringComparison.OrdinalIgnoreCase) || string.Equals(mood, "Mischievous", StringComparison.OrdinalIgnoreCase) ? "stimulation" : null;
            return need == null ? 1 : 1 / Under(need);
        }

        // Her waits for attention and for a prank, shortened while she is short of company or of something to do.
        internal static double Sooner() { return Math.Min(Under("social"), Under("stimulation")); }

        // Her sulk mark, lowered while she is short of comfort.
        internal static int Sulkier(int mark) { return mark <= 0 ? mark : Math.Max(1, (int)Math.Round(mark * Under("comfort"))); }

        // Said once each time cleanliness goes low, through her start-up's voice.
        internal static Action<string> Asks;
        private static readonly string[] WashLines = { "I feel a bit grubby. A wash would be nice.", "Could I get a wash? I'm all dusty.", "Bath time? I'm not at my shiniest." };
        private static bool washAsked;
        private static readonly Random dice = new Random();

        private static void Ask()
        {
            bool low = Low("cleanliness");
            if (!low) { washAsked = false; return; }
            if (washAsked) return;
            washAsked = true;
            Action<string> say = Asks;
            if (say == null) return;
            try { say(WashLines[dice.Next(WashLines.Length)]); }
            catch (Exception caughtOne) { Brain.Caught(caughtOne); }
        }

        // What raises a need, the one worth most to her now first: her acts that fill it, then the rest.
        internal static List<string> Helps(string need)
        {
            List<KeyValuePair<string, int>> acts = new List<KeyValuePair<string, int>>();
            foreach (string act in Acts.Names())
            {
                if (NeedOf(act) != need) continue;
                int worth = 0;
                try { worth = Worth(act); }
                catch (Exception caughtOne) { Brain.Caught(caughtOne); }
                if (worth > 0) acts.Add(new KeyValuePair<string, int>(act, worth));
            }
            acts.Sort(delegate(KeyValuePair<string, int> a, KeyValuePair<string, int> b) { return b.Value.CompareTo(a.Value); });
            List<string> found = new List<string>();
            foreach (KeyValuePair<string, int> one in acts) found.Add(one.Key);
            for (int at = 0; at < Others.GetLength(0); at++) if (Others[at, 0] == need) found.Add(Others[at, 1]);
            return found;
        }

        // Forgotten, as when the pet game is taken away.
        internal static void Clear() { lock (gate) Store(new List<string>()); Tell(); }

        private static void Move(string need, double by, bool tell = true)
        {
            double level = Math.Max(0, Math.Min(100, Level(need) + by));
            Set(need, level.ToString("0.##", CultureInfo.InvariantCulture));
            if (tell) Tell();
        }

        private static string Row(string key)
        {
            lock (gate)
                foreach (string row in Load() ?? new List<string>())
                    if (row.StartsWith(key + "=", StringComparison.Ordinal)) return row.Substring(key.Length + 1);
            return null;
        }

        private static void Set(string key, string value)
        {
            lock (gate)
            {
                List<string> rows = new List<string>(Load() ?? new List<string>());
                rows.RemoveAll(row => row.StartsWith(key + "=", StringComparison.Ordinal));
                rows.Add(key + "=" + value);
                Store(rows);
            }
        }

        private static void Tell()
        {
            Action told = Changed;
            if (told == null) return;
            try { told(); }
            catch (Exception caughtOne) { Brain.Caught(caughtOne); }
        }
    }
}
