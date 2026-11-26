using System;
using System.Collections.Generic;
using System.Globalization;

namespace CapriDesktop
{
    // What she was done to, and the maths of how it moved her.
    internal static class Acts
    {
        // One thing done to her: its kind, touch zone, limits (seconds between and times an hour, 0 for none), her own
        // like, and what Tamagotchi offers it as. A food's group gives its lines and an older file's like.
        internal sealed class Act
        {
            internal readonly string Name, Kind, Zone, Group;
            internal readonly int Seconds, PerHour, Like;
            internal readonly Offer[] Offers;

            internal Act(string name, string kind, string zone, int seconds, int perHour, int like, params Offer[] offers)
            {
                Name = name; Kind = kind; Zone = zone; Seconds = seconds; PerHour = perHour; Like = like; Offers = offers; Group = name;
            }

            internal Act(string name, string group, int like, Offer offer) : this(name, "food", null, 0, 0, like, offer) { Group = group; }

            internal bool Limited { get { return Seconds > 0 || PerHour > 0; } }

            // The heading her lines for it sit under: its zone's in touch.md, else its set's in her personality.
            internal string Heading { get { return Zone ?? Personalities.SetHeading(Group); } }
        }

        // An act on the Tamagotchi menu: its label, its cost in points, and what she calls it.
        internal sealed class Offer
        {
            internal readonly string Label, Said;
            internal readonly int Cost;

            internal Offer(string label, int cost) : this(label, cost, label) { }
            internal Offer(string label, int cost, string said) { Label = label; Cost = cost; Said = said; }
        }

        // The stock acts. Senpai acts aren't here: they come with what attached, limits and likes included.
        internal static readonly Act[] Stock =
        {
            // A pat is on her head and scored by Petted, not by its zone.
            new Act("petted", "touch", null, 20, 40, 2),
            new Act("touched shoulders", "touch", Touch.Shoulders, 20, 40, 1),
            new Act("touched middle", "touch", Touch.Middle, 20, 40, 0),
            new Act("touched legs", "touch", Touch.Legs, 20, 40, -1),
            new Act("touched feet", "touch", Touch.Feet, 20, 40, -1),
            new Act("tickled", "touch", Touch.Tickled, 20, 40, 1),
            // A ctrl click held on her head, on the plain side.
            new Act("kissed", "touch", null, 20, 40, 2),
            new Act("uppies", "grab", null, 0, 0, 2),
            new Act("thrown", "grab", null, 12, 30, -2),
            new Act("praised", "words", null, 45, 12, 2),
            new Act("insulted", "words", null, 20, 20, -2),
            new Act("called on", "words", null, 30, 30, 0),
            new Act("ignored", "other", null, 240, 8, -1),
            // Feed her, cheapest first once sorted, each food its own act. A file that names only "fed" or "treat"
            // gives that to each food in it.
            new Act("snack", "fed", 2, new Offer("A snack", 10)),
            new Act("meal", "fed", 2, new Offer("A proper meal", 25)),
            new Act("drink", "fed", 2, new Offer("Something to drink", 10)),
            new Act("treat", "treat", 3, new Offer("A treat", 20)),
            new Act("sweet", "treat", 3, new Offer("Something sweet", 20)),
            // Offered only while she is Emo or Bratty, on its own item.
            new Act("nuggies", "nuggies", 2, new Offer(Companion.Nuggies, 15)),
            // Gifts, top to bottom, a few a day on a working day's points. A personality or mood can dislike one.
            new Act("trinket", "gift", null, 0, 0, 2, new Offer("Trinket", 60, "trinket")),
            new Act("flowers", "gift", null, 0, 0, 3, new Offer("Flowers", 100, "bunch of flowers")),
            new Act("plushie", "gift", null, 0, 0, 2, new Offer("Plushie", 140, "plushie")),
            new Act("gift", "gift", null, 0, 0, 3, new Offer("Gift", 180, "present")),
            // A prank gift: the ones with a sense of humour like it, the rest see straight through it.
            new Act("pet rock", "gift", null, 0, 0, -1, new Offer("Pet rock", 30, "pet rock")),
            // A wash from the pet game, for her cleanliness.
            new Act("wash", "care", null, 0, 0, 1)
        };

        internal static Act Find(string name)
        {
            foreach (Act one in Stock) if (string.Equals(one.Name, name, StringComparison.OrdinalIgnoreCase)) return one;
            lock (packGate)
            {
                foreach (Act one in packActs) if (string.Equals(one.Name, name, StringComparison.OrdinalIgnoreCase)) return one;
                foreach (KeyValuePair<string, Act> one in modActs) if (string.Equals(one.Value.Name, name, StringComparison.OrdinalIgnoreCase)) return one.Value;
            }
            return null;
        }

        // Acts and terms a code mod adds while it runs, by the mod's id; gone when it stops.
        internal sealed class ModTermOf { internal string Mod, Name, Act; internal int Add; internal double Times = 1; internal string[] Who; }
        private static readonly List<KeyValuePair<string, Act>> modActs = new List<KeyValuePair<string, Act>>();
        private static readonly List<ModTermOf> modTerms = new List<ModTermOf>();
        internal const int MostAdd = 3;
        internal const double LeastTimes = 0.25, MostTimes = 2;

        internal static void Define(string mod, string name, string kind, int like)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("An act needs a name.");
            if (Array.Exists(Stock, one => string.Equals(one.Name, name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException(name + " is one of her own acts.");
            if (like < -5 || like > 5) throw new ArgumentOutOfRangeException("like", "An act's like is from -5 to 5.");
            lock (packGate)
            {
                modActs.RemoveAll(one => string.Equals(one.Value.Name, name, StringComparison.OrdinalIgnoreCase));
                modActs.Add(new KeyValuePair<string, Act>(mod, new Act(name.Trim().ToLowerInvariant(), string.IsNullOrWhiteSpace(kind) ? "other" : kind.Trim().ToLowerInvariant(), null, 0, 0, like)));
            }
        }

        // Who a term counts for, tidied: personalities and moods by name, "mood sleepy" for a mood only; none, everyone.
        internal static string[] WhoOf(string[] named)
        {
            if (named == null) return null;
            List<string> kept = new List<string>();
            foreach (string raw in named)
            {
                string one = System.Text.RegularExpressions.Regex.Replace((raw ?? "").Trim(), @"\s+", " ");
                if (one.Length == 0) continue;
                if (one.Length > 40 || kept.Count >= 24) throw new ArgumentException("A term's who names up to 24 personalities and moods, each in up to 40 characters.");
                if (!kept.Exists(had => string.Equals(had, one, StringComparison.OrdinalIgnoreCase))) kept.Add(one);
            }
            return kept.Count == 0 ? null : kept.ToArray();
        }

        // Whether a term counts now: no who is everyone; else she is one of the personalities named, or in one of the moods
        // (a plain name counts for either, "mood <name>" for a mood only).
        internal static bool For(string[] who, string personality, string mood)
        {
            if (who == null || who.Length == 0) return true;
            foreach (string one in who)
            {
                if (one.StartsWith("mood ", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(mood) && string.Equals(one.Substring(5).Trim(), mood, StringComparison.OrdinalIgnoreCase)) return true;
                    continue;
                }
                if (!string.IsNullOrEmpty(personality) && string.Equals(one, personality, StringComparison.OrdinalIgnoreCase)) return true;
                if (!string.IsNullOrEmpty(mood) && string.Equals(one, mood, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        internal static void Term(string mod, string name, string act, int add, double times, string[] who = null)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(act)) throw new ArgumentException("A term needs a name and an act.");
            if (Math.Abs(add) > MostAdd) throw new ArgumentOutOfRangeException("value", "An add is from -" + MostAdd + " to " + MostAdd + ".");
            if (times < LeastTimes || times > MostTimes) throw new ArgumentOutOfRangeException("value", "A multiplier is from " + LeastTimes + " to " + MostTimes + ".");
            lock (packGate)
            {
                modTerms.RemoveAll(one => one.Mod == mod && one.Name == name);
                modTerms.Add(new ModTermOf { Mod = mod, Name = name, Act = act, Add = add, Times = times, Who = WhoOf(who) });
            }
        }

        internal static void Unterm(string mod, string name) { lock (packGate) modTerms.RemoveAll(one => one.Mod == mod && one.Name == name); }

        internal static void DropMod(string mod)
        {
            lock (packGate)
            {
                modActs.RemoveAll(one => one.Key == mod);
                modTerms.RemoveAll(one => one.Mod == mod);
            }
        }

        // The multipliers on an act, by term name: every one, or with a personality and mood, those that count for her now.
        internal static List<KeyValuePair<string, double>> Multipliers(string act, bool everyone = true, string personality = null, string mood = null)
        {
            List<KeyValuePair<string, double>> found = new List<KeyValuePair<string, double>>();
            lock (packGate)
                foreach (ModTermOf one in modTerms)
                    if (one.Times != 1 && string.Equals(one.Act, act, StringComparison.OrdinalIgnoreCase) && (everyone || For(one.Who, personality, mood))) found.Add(new KeyValuePair<string, double>(one.Name, one.Times));
            return found;
        }

        // Every act she has now: hers, the packs' and the mods'.
        internal static string[] Names()
        {
            List<string> all = new List<string>();
            foreach (Act one in Stock) all.Add(one.Name);
            lock (packGate)
            {
                foreach (Act one in packActs) all.Add(one.Name);
                foreach (KeyValuePair<string, Act> one in modActs) all.Add(one.Value.Name);
            }
            return all.ToArray();
        }

        // Acts and mood terms the packs that are on add, rebuilt whole as packs change. A term is named after its pack.
        private static readonly object packGate = new object();
        private static readonly List<Act> packActs = new List<Act>();
        private static readonly List<KeyValuePair<string, ModTerm>> packTerms = new List<KeyValuePair<string, ModTerm>>();

        internal static void Hold(List<KeyValuePair<string, ModFile>> packs)
        {
            lock (packGate)
            {
                packActs.Clear();
                packTerms.Clear();
                foreach (KeyValuePair<string, ModFile> pack in packs)
                {
                    if (pack.Value.Adds == null) continue;
                    foreach (ModAct one in pack.Value.Adds.Acts ?? new ModAct[0])
                        if (!packActs.Exists(had => string.Equals(had.Name, one.Name, StringComparison.OrdinalIgnoreCase)) && !Array.Exists(Stock, had => string.Equals(had.Name, one.Name, StringComparison.OrdinalIgnoreCase)))
                            packActs.Add(new Act(one.Name, one.Kind, null, 0, 0, one.Like));
                    foreach (ModTerm one in pack.Value.Adds.Mood ?? new ModTerm[0]) packTerms.Add(new KeyValuePair<string, ModTerm>(pack.Key, one));
                }
            }
        }

        internal static bool Added(string name) { lock (packGate) return packActs.Exists(one => string.Equals(one.Name, name, StringComparison.OrdinalIgnoreCase)); }

        // The packs' and code mods' terms for an act, by name: every one, or with a personality and mood, those that count for
        // her now.
        internal static List<KeyValuePair<string, int>> Terms(string act, bool everyone = true, string personality = null, string mood = null)
        {
            List<KeyValuePair<string, int>> found = new List<KeyValuePair<string, int>>();
            lock (packGate)
            {
                foreach (KeyValuePair<string, ModTerm> one in packTerms)
                    if (string.Equals(one.Value.Act, act, StringComparison.OrdinalIgnoreCase) && (everyone || For(one.Value.Who, personality, mood))) found.Add(new KeyValuePair<string, int>(one.Key, one.Value.Add));
                foreach (ModTermOf one in modTerms)
                    if (one.Add != 0 && string.Equals(one.Act, act, StringComparison.OrdinalIgnoreCase) && (everyone || For(one.Who, personality, mood))) found.Add(new KeyValuePair<string, int>(one.Name, one.Add));
            }
            return found;
        }

        internal static Act[] Of(string kind)
        {
            List<Act> found = new List<Act>();
            foreach (Act one in Stock) if (one.Kind == kind) found.Add(one);
            return found.ToArray();
        }

        // Her own like for an act when nothing else names it, 0 for one not on the list.
        internal static int LikeOf(string name)
        {
            Act one = Find(name);
            return one == null ? 0 : one.Like;
        }

        // The act a touch zone scores as, or null for a zone with none.
        internal static string AtZone(string zone)
        {
            if (zone == null) return null;
            foreach (Act one in Stock) if (one.Zone == zone) return one.Name;
            // A place only what attached names has its act there too, while that side is live.
            if (Brain.SenpaiLive)
            {
                string attached = Extended.Get("touch.act." + Touch.Part(zone));
                if (!string.IsNullOrEmpty(attached)) return attached;
            }
            return null;
        }

        // The last act's line, as her log has it.
        internal static volatile string Last;

        // One act's whole sum, each term under its own name, written to her log as one line:
        // "trinket, 2nd in 30 min (window 30): Emo 2 + Sleepy -1 = worth 1; kind returns 1.12 -> warmth +0.81 (4.00
        // to 4.81), upset 0.00, willing 6".
        internal sealed class Sum
        {
            internal readonly string Act;
            private readonly List<string> terms = new List<string>();
            private readonly List<string> steps = new List<string>();
            private int total;
            private string repeat = "", moved;

            internal Sum(string act) { Act = act; }

            internal int Worth { get; private set; }
            internal bool Counted { get; private set; }

            // A part that adds into the worth: her personality's like, the default's, their words.
            internal void Term(string name, int value)
            {
                terms.Add(name + " " + value);
                total += value;
                Worth = total;
            }

            // Her mood's shift, signed, as it adds.
            internal void Shift(string name, int value)
            {
                terms.Add(name + " " + (value >= 0 ? "+" : "") + value);
                total += value;
                Worth = total;
            }

            // The total held to -5..5.
            internal void Held(int held)
            {
                if (held != total) steps.Add("held to " + held);
                Worth = held;
            }

            // A step after the total, like their handling or a gift's least, and the worth it left.
            internal void Then(string what, int worth)
            {
                steps.Add(what);
                Worth = worth;
            }

            internal int Given(int given)
            {
                Term("words", given);
                return given;
            }

            // How many times it has landed in its run, this one included, the run's window and the range it's picked in.
            internal void Repeat(int before, int window, int shortest, int longest)
            {
                repeat = ", " + Ordinal(before + 1) + " in " + window + " min (window " + shortest + "-" + longest + ")";
            }

            internal void Limited(int seconds, int perHour)
            {
                List<string> by = new List<string>();
                if (seconds > 0) by.Add("once in " + seconds + " s");
                if (perHour > 0) by.Add(perHour + " an hour");
                repeat = ", limited out";
                limitedFor = Math.Max(seconds, 60);
                moved = "didn't count" + (by.Count == 0 ? "" : ", limited to " + string.Join(" and ", by.ToArray()));
            }

            // The scaler numbers, what moved from what to what, and willing after.
            internal void Moved(string what, int willing)
            {
                Counted = true;
                moved = what + ", willing " + willing;
            }

            internal string Line
            {
                get
                {
                    string sum = terms.Count == 0 ? "" : string.Join(" + ", terms.ToArray()) + " = ";
                    if (steps.Count > 0) sum += total + ", " + string.Join(", ", steps.ToArray()) + " = ";
                    return Act + repeat + ": " + sum + "worth " + Worth + (moved == null ? "" : "; " + moved);
                }
            }

            private int limitedFor;
            private static readonly Dictionary<string, DateTime> limitedLogged = new Dictionary<string, DateTime>();
            internal static void ForgetLimited() { lock (limitedLogged) limitedLogged.Clear(); }

            // One limited-out line per act per window: being ignored landed again every few hundred ms and filled the log.
            internal void Log()
            {
                string line = Line;
                Last = line;
                if (limitedFor > 0)
                    lock (limitedLogged)
                    {
                        DateTime at, now = SimTime.UtcNow;
                        if (limitedLogged.TryGetValue(Act, out at) && now >= at && now - at < TimeSpan.FromSeconds(limitedFor)) return;
                        limitedLogged[Act] = now;
                    }
                Brain.Log("mood", -1, line);
            }
        }

        internal static string Ordinal(int n)
        {
            int tens = n % 100, ones = n % 10;
            string end = tens >= 11 && tens <= 13 ? "th" : ones == 1 ? "st" : ones == 2 ? "nd" : ones == 3 ? "rd" : "th";
            return n + end;
        }

        // A factor, to two places at most.
        internal static string N(double value)
        {
            return Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);
        }

        // To two places, as her log shows warmth and upset.
        internal static string F(double value)
        {
            return value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        internal static string Signed(double value)
        {
            return (value >= 0 ? "+" : "") + F(value);
        }
    }
}
