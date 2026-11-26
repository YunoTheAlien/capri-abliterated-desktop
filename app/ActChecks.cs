using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

#if CHECKS
namespace CapriDesktop
{
    internal sealed partial class Companion
    {
        // The stock likes of the acts the checks use, for Her own and any file that doesn't name them.
        private static readonly string[] ActsChecked = { "petted", "touched shoulders", "trinket", "insulted" };
        private static readonly int[] ActsDefault = { 2, 1, 2, -2 };

        // The act maths: the act list, the willing factor against the worked rows, the repeat window under upset and
        // warmth, every stock personality in two moods and at willing 1, 5 and 9, a disliked gift and food, and hunger.
        private void SmokeActMaths(List<string> checks)
        {
            string chosenWas = Personalities.Chosen, moodWas = Personalities.Attitude;
            bool plainWas = Personalities.HeldPlain;
            long fedWas = preferences.FedAt;
            ICapriPet petWas = petStandIn;
            double upsetWas = upset;
            string trial = Dynamics.PlainPathOf(ActTrial);
            List<string> packs = new List<string>(Personalities.Softest);
            packs.Insert(0, Personalities.Stock);
            try
            {
                string listed = ActList();
                if (listed != null) throw new Exception(listed);
                checks.Add("PASS the act list: its limits and likes, each food its own act with its cost, the gifts, and touch zones scoring as before; no senpai act ships in it");

                string rows = WorkedRows();
                if (rows != null) throw new Exception(rows);
                string window = WindowRanges();
                if (window != null) throw new Exception(window);
                checks.Add("PASS the willing factor gives the worked rows (a +3 at kind returns 1.12 and a -2 at mean scale 0.94, repeated, at willing 1, 5 and 9), and the repeat window runs 20 to 60 minutes, upset raising its shortest and warmth lowering its longest");

                Dynamics.Install();
                Personalities.HeldPlain = false;
                int lines = 0;
                foreach (string pack in packs)
                    foreach (string mood in new[] { "Sleepy", "Playful" })
                    {
                        ActRun run = new ActRun(this, pack, mood, 0, 0);
                        foreach (string act in new[] { "petted", "touched shoulders", "trinket", "trinket", "praised", "insulted", "insulted" }) { run.Next(act); lines++; }
                        run.Ended();
                    }
                foreach (string pack in packs)
                    foreach (int willing in new[] { 1, 5, 9 })
                    {
                        ActRun run = new ActRun(this, pack, "", willing == 9 ? 16 : 0, willing == 1 ? 18 : 0);
                        foreach (string act in new[] { "trinket", "trinket", "trinket", "insulted" }) { run.Next(act); lines++; }
                        run.Ended();
                    }
                checks.Add("PASS the act log: " + packs.Count + " personalities in Sleepy and Playful, and at willing 1, 5 and 9, give the worth, warmth and upset the settled sums do, and all " + lines + " lines hold the sum term by term with the window, willing, gain and fade");

                File.WriteAllText(trial, "# " + ActTrial + "\n\n## " + Personalities.LikesHeading + "\n\ntrinket: -4\nfed: -2\n");
                Personalities.Chosen = Personalities.Stock;
                Personalities.Attitude = ActTrial;
                Mood.Forget();
                preferences.FedAt = SimTime.UtcNow.Ticks;
                Acts.Sum disliked = Scored("trinket", int.MinValue, int.MinValue);
                bool lower = Mood.Warmth <= 0 && disliked.Line.Contains("worth -2;") && disliked.Line.Contains("-> upset +");
                upset = 0;
                Did("trinket");
                bool sulks = upset == 2;
                bool dislikeLine = GiftLine("trinket", false) != null;
                // Did can ease her back to the personality they picked, so the snack is scored as her own again.
                Personalities.Chosen = Personalities.Stock;
                Acts.Sum snack = Scored("snack", int.MinValue, int.MinValue);
                bool fedShift = snack.Line.Contains("Her own 2 + " + ActTrial + " -2 = worth 0;");
                if (!lower || !sulks || !dislikeLine || !fedShift)
                    throw new Exception("A disliked gift went wrong: lowers warmth " + lower + " (" + disliked.Line + "), counts to a sulk " + sulks + ", her dislike line " + dislikeLine + ", a mood's fed on a snack " + fedShift + " (" + snack.Line + ")");

                Personalities.Chosen = "Bratty";
                Personalities.Attitude = "";
                Mood.Forget();
                preferences.FedAt = SimTime.UtcNow.AddHours(-30).Ticks;
                Acts.Sum plain = Scored("meal", int.MinValue, int.MinValue);
                petStandIn = new StandInPet();
                Acts.Sum hungry = Scored("meal", int.MinValue, int.MinValue);
                preferences.FedAt = SimTime.UtcNow.Ticks;
                Acts.Sum full = Scored("drink", int.MinValue, int.MinValue);
                petStandIn = petWas;
                if (!hungry.Line.Contains(": Bratty -1 + hungry +2 = worth 1;") || !full.Line.Contains(": Bratty 1 = worth 1;") || !plain.Line.Contains(": Bratty -1 = worth -1;"))
                    throw new Exception("Hunger didn't add to a food with a pet game, or added without one: " + hungry.Line + " / " + full.Line + " / " + plain.Line);
                checks.Add("PASS a disliked gift counts against her: no floor of 1, warmth goes down and upset up, it counts toward a sulk, and she says a dislike line; a mood's fed reaches each food; hunger adds to a food only with a pet game (Bratty's meal, -1, lands as +1 when she is hungry, and as -1 without one)");

                // Limited out over and over, as being ignored is each tick: one line in her log a window.
                Acts.Sum.ForgetLimited();
                string mark = "act log mark " + Guid.NewGuid().ToString("N");
                Brain.Log("mood", -1, mark);
                for (int again = 0; again < 5; again++) { Acts.Sum held = Scored("ignored", int.MinValue, int.MinValue); held.Limited(240, 8); held.Log(); }
                string[] written = Brain.LogPath != null && File.Exists(Brain.LogPath) ? File.ReadAllLines(Brain.LogPath) : new string[0];
                int markAt = Array.FindLastIndex(written, delegate(string one) { return one.Contains(mark); }), limitedLines = 0;
                for (int i = markAt + 1; markAt >= 0 && i < written.Length; i++) if (written[i].Contains("ignored, limited out")) limitedLines++;
                if (markAt < 0 || limitedLines != 1) throw new Exception("An act limited out five times wrote " + limitedLines + " lines, not one" + (markAt < 0 ? " (her log wasn't found)" : "") + ".");
                checks.Add("PASS an act limited out again and again writes one line in her log a window, not one each time");
            }
            finally
            {
                Personalities.Chosen = chosenWas;
                Personalities.Attitude = moodWas;
                Personalities.HeldPlain = plainWas;
                preferences.FedAt = fedWas;
                petStandIn = petWas;
                upset = upsetWas;
                try { if (trial != null) File.Delete(trial); } catch (IOException) { }
                Mood.Forget();
            }
        }

        private const string ActTrial = "Act Check";

        // One run of acts in one personality and mood from a set warmth and upset, each checked against the sums worked
        // here and its log line, the window's minutes read from the line and held to the range.
        private sealed class ActRun
        {
            private readonly Companion her;
            private readonly string pack, mood;
            private double warm, upset;
            private readonly Dictionary<string, int> repeats = new Dictionary<string, int>(), windows = new Dictionary<string, int>();
            private readonly List<string> done = new List<string>();

            internal ActRun(Companion her, string pack, string mood, double warm, double upset)
            {
                this.her = her; this.pack = pack; this.mood = mood; this.warm = warm; this.upset = upset;
                Personalities.Chosen = pack;
                Personalities.Attitude = mood;
                Mood.Forget();
                Mood.Feel(warm, upset);
            }

            internal void Next(string act)
            {
                bool words = act == "praised", limited = act == "insulted" && done.Contains(act);
                done.Add(act);
                int willing = ActWilling(warm, upset);
                int shortest, longest;
                ActWindow(warm, upset, out shortest, out longest);
                Acts.Sum sum = her.Scored(act, words ? 3 : int.MinValue, int.MinValue);
                int total = 3;
                string terms = words ? "words 3" : ExpectedTerms(pack, mood, act, out total), head = act, moved;
                int worth = Math.Max(-5, Math.Min(5, total));
                List<string> steps = new List<string>();
                if (worth != total) steps.Add("held to " + worth);
                if (act == "insulted" && her.FirstRunHandling == "Rough" && worth > 0) { worth++; steps.Add("Rough +1"); }
                if (act == "insulted" && her.FirstRunHandling == "Gentle" && worth > 0) { worth--; steps.Add("Gentle -1"); }
                string line = sum.Line;
                if (limited) { head += ", limited out"; moved = "didn't count, limited to once in 20 s and 20 an hour"; }
                else if (worth == 0) moved = "moves nothing, willing " + willing;
                else
                {
                    int again;
                    repeats.TryGetValue(act, out again);
                    repeats[act] = again + 1;
                    Match picked = Regex.Match(line, @", [0-9]+(st|nd|rd|th) in ([0-9]+) min \(window ([0-9]+)-([0-9]+)\):");
                    int minutes = picked.Success ? int.Parse(picked.Groups[2].Value) : -1;
                    if (again == 0 && (minutes < shortest || minutes > longest) || again > 0 && minutes != windows[act])
                        throw new Exception(pack + ", " + act + ": its window of " + minutes + " minutes isn't in " + shortest + "-" + longest + " or its run's: " + line);
                    if (again == 0) windows[act] = minutes;
                    head += ", " + Acts.Ordinal(again + 1) + " in " + minutes + " min (window " + shortest + "-" + longest + ")";
                    if (worth > 0)
                    {
                        double pull = Mood.Numbers.Now().Pull, slows = Mood.Numbers.Now().Slows;
                        double gain = 1 + pull * (willing - 5), slow = 1 - slows * (willing - 5), kind = Mood.Kind;
                        double much = Math.Max(0.4, worth * gain / (1.0 + again * 0.6 * kind * slow)), was = warm;
                        warm = Math.Min(60, warm + much);
                        moved = "willing " + willing + ", gain " + Acts.N(gain) + ", fade " + Acts.N(slow) + ", kind returns " + Acts.N(kind) + " -> warmth " + Acts.Signed(much) + " (" + Acts.F(was) + " to " + Acts.F(warm) + "), upset " + Acts.F(upset);
                    }
                    else
                    {
                        double pull = Mood.Numbers.Now().Pull, slows = Mood.Numbers.Now().Slows;
                        double hurt = 1 - pull * (willing - 5), build = 1 - slows * (willing - 5), mean = Mood.Mean;
                        double grows = Math.Min(2.5, 1.0 + again * 0.35 * build), much = -worth * mean * hurt * grows, wasUpset = upset, was = warm;
                        upset = Math.Min(60, upset + much);
                        warm = Math.Max(-60, warm - much * 0.2);
                        moved = "willing " + willing + ", hurt " + Acts.N(hurt) + ", build " + Acts.N(build) + ", mean scale " + Acts.N(mean) + ", grows x" + Acts.N(grows) + " -> upset " + Acts.Signed(much) + " (" + Acts.F(wasUpset) + " to " + Acts.F(upset) + "), warmth " + Acts.Signed(-much * 0.2) + " (" + Acts.F(was) + " to " + Acts.F(warm) + ")";
                    }
                    moved += ", willing " + ActWilling(warm, upset);
                }
                string expected = head + ": " + terms + " = " + (steps.Count > 0 ? total + ", " + string.Join(", ", steps.ToArray()) + " = " : "") + "worth " + worth + "; " + moved;
                if (sum.Worth != worth || sum.Counted == limited || line != expected || Acts.Last != line)
                    throw new Exception(pack + " in " + (mood.Length == 0 ? "no mood" : mood) + ", " + act + ": worth " + sum.Worth + " against " + worth + ", logged \"" + line + "\" against \"" + expected + "\"");
            }

            internal void Ended()
            {
                if (Mood.Warmth != (int)Math.Round(warm) || Mood.Upset != (int)Math.Round(upset))
                    throw new Exception(pack + " in " + mood + " ended on warmth " + Mood.Warmth + " and upset " + Mood.Upset + ", not " + Acts.F(warm) + " and " + Acts.F(upset));
            }
        }

        private static int ActWilling(double warm, double upset)
        {
            return (int)Math.Round(Math.Max(0, Math.Min(10, 5 + warm / 4.0 - upset / 4.5)));
        }

        // The window's range as 146 settled it, for her stock 20 to 60.
        private static void ActWindow(double warm, double upset, out int shortest, out int longest)
        {
            double tilt = Math.Max(-1, Math.Min(1, (upset - warm) / 60.0));
            shortest = (int)Math.Round(tilt > 0 ? 20 + 40 * tilt : 20);
            longest = (int)Math.Round(tilt < 0 ? 60 + 40 * tilt : 60);
        }

        // 146's worked rows, each to two places (one there is 2.00 where the sum is 1.993). Null when they match.
        private static string WorkedRows()
        {
            Mood.Numbers stock = new Mood.Numbers();
            double[][] kind = { new[] { 2.4, 1.24, 0.83, 0.63 }, new[] { 3, 1.79, 1.28, 0.99 }, new[] { 3.6, 2.57, 2.00, 1.63 } };
            double[][] mean = { new[] { 2.26, 3.36, 4.47, 5.57 }, new[] { 1.88, 2.54, 3.20, 3.85 }, new[] { 1.50, 1.82, 2.14, 2.45 } };
            int[] willing = { 1, 5, 9 };
            for (int at = 0; at < willing.Length; at++)
                for (int again = 0; again < 4; again++)
                {
                    double gain, slow, hurt, build, grows;
                    double up = Mood.Kindly(3, again, 1.12, willing[at], stock, out gain, out slow);
                    double down = Mood.Meanly(-2, again, 0.94, willing[at], stock, out hurt, out build, out grows);
                    if (Math.Abs(up - kind[at][again]) > 0.0101 || Math.Abs(down - mean[at][again]) > 0.0101)
                        return "At willing " + willing[at] + ", repeat " + again + ": kind " + Acts.F(up) + " against " + kind[at][again] + ", mean " + Acts.F(down) + " against " + mean[at][again];
                }
            return null;
        }

        // The window at rest, most upset, warmest and halfway each way. Null when right.
        private static string WindowRanges()
        {
            double[][] felt = { new[] { 0.0, 0 }, new[] { 0.0, 60 }, new[] { 60.0, 0 }, new[] { 0.0, 30 }, new[] { 30.0, 0 } };
            string[] want = { "20-60", "60-60", "20-20", "40-60", "20-40" };
            string chosenWas = Personalities.Chosen;
            try
            {
                Personalities.Chosen = Personalities.Stock;
                for (int at = 0; at < felt.Length; at++)
                {
                    Mood.Feel(felt[at][0], felt[at][1]);
                    int shortest, longest;
                    Mood.WindowNow(out shortest, out longest);
                    if (shortest + "-" + longest != want[at]) return "At warmth " + felt[at][0] + " and upset " + felt[at][1] + " the window was " + shortest + "-" + longest + ", not " + want[at];
                }
                return null;
            }
            finally { Personalities.Chosen = chosenWas; Mood.Forget(); }
        }

        // The act list: "name seconds/per hour like" and each offer's label, cost and name, against what it should hold.
        // Null when it matches.
        private static string ActList()
        {
            string[] want =
            {
                "petted 20/40 2", "touched shoulders 20/40 1", "touched middle 20/40 0", "touched legs 20/40 -1", "touched feet 20/40 -1",
                "tickled 20/40 1", "kissed 20/40 2", "uppies 0/0 2", "thrown 12/30 -2", "praised 45/12 2", "insulted 20/20 -2", "called on 30/30 0",
                "ignored 240/8 -1", "snack 0/0 2 A snack 10", "meal 0/0 2 A proper meal 25", "drink 0/0 2 Something to drink 10",
                "treat 0/0 3 A treat 20", "sweet 0/0 3 Something sweet 20", "nuggies 0/0 2 chimpken nuggies 15",
                "trinket 0/0 2 Trinket 60 trinket", "flowers 0/0 3 Flowers 100 bunch of flowers", "plushie 0/0 2 Plushie 140 plushie",
                "gift 0/0 3 Gift 180 present",
                "pet rock 0/0 -1 Pet rock 30 pet rock",
                "wash 0/0 1"
            };
            List<string> now = new List<string>();
            foreach (Acts.Act one in Acts.Stock)
            {
                List<string> offers = new List<string>();
                foreach (Acts.Offer offer in one.Offers) offers.Add(offer.Label + " " + offer.Cost + (one.Kind == "gift" ? " " + offer.Said : ""));
                now.Add((one.Name + " " + one.Seconds + "/" + one.PerHour + " " + one.Like + " " + string.Join(", ", offers.ToArray())).Trim());
            }
            if (string.Join(" | ", now.ToArray()) != string.Join(" | ", want)) return "The act list isn't as it should be: " + string.Join(" | ", now.ToArray());
            string zones = TouchAct(Touch.Shoulders) + ", " + TouchAct(Touch.Middle) + ", " + TouchAct(Touch.Legs) + ", " + TouchAct(Touch.Feet) + ", " + TouchAct(Touch.Tickled) + ", " + (TouchAct(Touch.Head) ?? "none");
            if (zones != "touched shoulders, touched middle, touched legs, touched feet, tickled, none") return "Touch zones don't score as before: " + zones;
            // Senpai acts come only with what attached, never from the list that ships.
            foreach (Acts.Act one in Acts.Stock) if (Personalities.SenpaiSet(one.Name)) return "The act list ships a senpai act: " + one.Name;
            return null;
        }

        // A number under "What she likes" in a shipped file, or null when it names none.
        private static int? LikeIn(string text, string act)
        {
            Match likes = Regex.Match(text ?? "", "(?ms)^## " + Personalities.LikesHeading + "\\s*$(.*?)(?=^## |\\z)");
            Match one = Regex.Match(likes.Groups[1].Value, "(?m)^" + Regex.Escape(act) + ": ([+-]?[0-9]+)\\s*$");
            return one.Success ? int.Parse(one.Groups[1].Value.TrimStart('+')) : (int?)null;
        }

        // Her personality's like (or the default) and her mood's shift, as the log names them, and their total.
        private static string ExpectedTerms(string pack, string mood, string act, out int total)
        {
            int? own = pack == Personalities.Stock ? null : LikeIn(Personalities.Written(pack), act);
            int? shift = mood.Length == 0 ? null : LikeIn(Dynamics.MoodText(mood), act);
            int mine = own ?? ActsDefault[Array.IndexOf(ActsChecked, act)];
            total = mine + (shift ?? 0);
            string terms = (pack == Personalities.Stock ? pack : own == null ? "default" : pack) + " " + mine;
            return shift == null ? terms : terms + " + " + mood + " " + (shift >= 0 ? "+" : "") + shift;
        }
    }
}
#endif
