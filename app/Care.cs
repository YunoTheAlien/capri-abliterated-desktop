using System;
using System.Collections.Generic;
using System.IO;

namespace CapriDesktop
{
    internal enum CareKind { Water, Stretch, Eyes, Late, Meal, Game, Bored }

    internal sealed class Care
    {
        internal const long Away = 600000;
        internal static readonly string[] Names = { "Water", "Stretch", "Look away", "Sleep nudge", "Meals", "Game break", "Bored" };
        private readonly Func<double> seconds;
        private readonly Func<DateTime> local;
        private readonly Func<CareKind, string> words;
        private readonly double[] next = new double[7];
        private readonly bool[] pending = new bool[7];
        private readonly bool[] enabled = new bool[7];
        private readonly int[] times = new int[7];
        private bool started, resting, paused;
        private DateTime since;
        private string game;
        private long night, meal;
        private double attentionAt = -1, attentionRate = 1, activeAt;
        private int turn;
        internal CareKind Said;

        internal Care(Func<double> seconds, Func<DateTime> local, Func<CareKind, string> words)
        {
            this.seconds = seconds;
            this.local = local;
            this.words = words;
        }

        internal string Check(bool checkIns, Func<CareKind, bool> careOn, Func<CareKind, int> careTime, ref long careNight, long quiet, bool free, string playing, double noticed, double attentionGap)
        {
            if (!checkIns) { Reset(); return null; }
            double now = seconds();
            DateTime today = local();
            if (!started || resting || quiet > Away)
            {
                since = today;
                activeAt = now;
                game = playing;
                for (int reset = 0; reset < next.Length; reset++)
                {
                    CareKind resetKind = (CareKind)reset;
                    next[reset] = now + careTime(resetKind) * 60.0 * (resetKind == CareKind.Bored ? attentionGap : 1);
                    pending[reset] = false;
                    enabled[reset] = careOn(resetKind);
                    times[reset] = careTime(resetKind);
                }
                started = true;
                resting = quiet > Away;
                if (resting) return null;
            }
            for (int setting = 0; setting < next.Length; setting++)
            {
                CareKind settingKind = (CareKind)setting;
                bool on = careOn(settingKind);
                int time = careTime(settingKind);
                if (!on || on != enabled[setting] || time != times[setting])
                {
                    pending[setting] = false;
                    next[setting] = now + time * 60.0;
                }
                enabled[setting] = on;
                times[setting] = time;
            }
            if (noticed != attentionAt || attentionGap != attentionRate)
            {
                attentionAt = noticed;
                attentionRate = attentionGap;
                pending[(int)CareKind.Bored] = false;
                next[(int)CareKind.Bored] = (noticed < 0 ? now : Math.Max(noticed, activeAt)) + careTime(CareKind.Bored) * 60.0 * attentionGap;
            }
            if (!string.Equals(game, playing, StringComparison.OrdinalIgnoreCase))
            {
                if (playing != null) pending[(int)CareKind.Game] = false;
                game = playing;
                next[(int)CareKind.Game] = now + careTime(CareKind.Game) * 60.0;
            }
            for (int timer = 0; timer < next.Length; timer++)
            {
                CareKind timerKind = (CareKind)timer;
                if (timerKind == CareKind.Late || timerKind == CareKind.Meal || !enabled[timer]) continue;
                if (timerKind == CareKind.Game && game == null) continue;
                if (timerKind == CareKind.Bored && (noticed < 0 || now - noticed < times[timer] * 60.0 * attentionGap))
                {
                    pending[timer] = false;
                    continue;
                }
                if (now < next[timer]) continue;
                pending[timer] = true;
                next[timer] = now + times[timer] * 60.0 * (timerKind == CareKind.Bored ? attentionGap : 1);
            }
            DateTime evening = today.AddHours(-12).Date;
            int hour = careTime(CareKind.Late);
            DateTime bedtime = evening.AddDays(hour < 12 ? 1 : 0).AddHours(hour);
            bool late = today >= bedtime && today < evening.AddDays(1).AddHours(6);
            if (paused && late && enabled[(int)CareKind.Late]) night = evening.Ticks;
            paused = false;
            if (!late || night != evening.Ticks) pending[(int)CareKind.Late] = false;
            if (enabled[(int)CareKind.Late] && late && night != evening.Ticks && careNight != evening.Ticks)
            {
                night = evening.Ticks;
                pending[(int)CareKind.Late] = true;
            }
            DateTime lunch = today.Date.AddHours(12);
            int mealGap = careTime(CareKind.Meal);
            DateTime mealAt = today < lunch ? lunch : lunch.AddMinutes(Math.Floor((today - lunch).TotalMinutes / mealGap) * mealGap);
            bool mealtime = since < lunch && today >= mealAt && today < mealAt.AddHours(1) && mealAt.Hour < 21;
            if (!mealtime || meal != mealAt.Ticks) pending[(int)CareKind.Meal] = false;
            if (enabled[(int)CareKind.Meal] && mealtime && meal != mealAt.Ticks)
            {
                meal = mealAt.Ticks;
                pending[(int)CareKind.Meal] = true;
            }
            if (!free) return null;
            string line = null;
            for (int choice = 0; choice < pending.Length; choice++)
            {
                int index = (turn + choice) % pending.Length;
                if (!pending[index]) continue;
                string choiceLine = words((CareKind)index);
                if (string.IsNullOrWhiteSpace(choiceLine)) continue;
                line = choiceLine;
                Said = (CareKind)index;
                turn = (index + 1) % pending.Length;
                if (Said == CareKind.Late) careNight = evening.Ticks;
                break;
            }
            for (int cleared = 0; cleared < pending.Length; cleared++)
                if (pending[cleared]) next[cleared] = now + times[cleared] * 60.0 * (cleared == (int)CareKind.Bored ? attentionGap : 1);
            Array.Clear(pending, 0, pending.Length);
            return line;
        }

        internal void Reset()
        {
            started = false;
            paused = true;
            Array.Clear(pending, 0, pending.Length);
        }
    }

    internal sealed class CareLines
    {
        internal const string Defaults =
            "# Capri's check-ins\n\n" +
            "Edit the bullet lines under each heading to change what she says. Remove a bullet to stop using it, or all the bullets under a heading to silence that check-in. These lines stay on this PC.\n\n" +
            "## Water\n- Have a sip of water, choom. I'll keep your spot.\n- Water break. Even you need a refill.\n\n" +
            "## Stretch\n- Unfold yourself a little. Those legs still work.\n- Tiny stretch break. I'll be right here.\n\n" +
            "## Look away\n- Give your eyes 20 seconds looking away from the screen. I'll survive.\n- Look at something far away for 20 seconds. Yes, even farther than me.\n\n" +
            "## Sleep nudge\n- It's late, choom. Your bed misses you.\n\n" +
            "## Meals\n- Been here since before lunch. Go grab something to eat.\n- Food break. Can't run that brain on empty.\n\n" +
            "## Game break\n- Long session. Take a little break when you've got a good spot.\n- You've been at that game a while. Stretch those hands when you can.\n\n" +
            "## Bored\n- So boreddd.\n- I've counted your desktop icons twice now.\n- One of my minds is making its own entertainment. Worrying.\n- Tiny gremlin. Big boredom.\n\n" +
            "## Bored noticed\n- There you are.\n- Finally. Hi.\n\n" +
            "## Bored ignored\n- Fine. I'll entertain myself.\n- Cool. Cool cool cool.\n";
        private readonly string path;
        private readonly Dictionary<string, string> last = new Dictionary<string, string>();
        private readonly Random dice = new Random();

        internal CareLines(string path) { this.path = path; }

        internal bool Create()
        {
            try
            {
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    using (StreamWriter writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write))) writer.Write(Defaults);
                }
                else
                {
                    string existing = File.ReadAllText(path);
                    bool written = existing.IndexOf("# Capri's check-ins", StringComparison.Ordinal) >= 0;
                    foreach (string heading in Care.Names) if (existing.IndexOf("## " + heading, StringComparison.Ordinal) >= 0) written = true;
                    if (!written) File.AppendAllText(path, "\n\n" + Defaults);
                    else TopUp(existing);
                }
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        // A check-in she ships that their file has not got. This used to do nothing at all: the file was written
        // once and never touched again, so a whole new kind of check-in, or a line added to one, reached new
        // installs and nobody else. Same fault her packs, her moods, lines.md and sys.md each had.
        //
        // A missing section goes on the end. A section they already have keeps every line in it and gains the ones
        // she ships that are not there, matched exactly, so a line they reworded stays reworded.
        private void TopUp(string existing)
        {
            try
            {
                List<string> made = new List<string>(existing.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'));
                string adding = "";
                string under = null;
                List<string> lines = new List<string>();
                List<KeyValuePair<string, List<string>>> stock = new List<KeyValuePair<string, List<string>>>();
                foreach (string raw in Defaults.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                {
                    string one = raw.Trim();
                    if (one.StartsWith("##", StringComparison.Ordinal))
                    {
                        if (under != null) stock.Add(new KeyValuePair<string, List<string>>(under, lines));
                        under = one.TrimStart('#').Trim();
                        lines = new List<string>();
                        continue;
                    }
                    if (under == null || one.Length == 0 || one.StartsWith(">", StringComparison.Ordinal)) continue;
                    lines.Add(one);
                }
                if (under != null) stock.Add(new KeyValuePair<string, List<string>>(under, lines));

                foreach (KeyValuePair<string, List<string>> section in stock)
                {
                    int from = -1, to = made.Count;
                    for (int row = 0; row < made.Count; row++)
                    {
                        string one = made[row].Trim();
                        if (!one.StartsWith("#", StringComparison.Ordinal)) continue;
                        if (from < 0)
                        {
                            if (string.Equals(one.TrimStart('#').Trim(), section.Key, StringComparison.OrdinalIgnoreCase)) from = row + 1;
                            continue;
                        }
                        to = row;
                        break;
                    }
                    if (from < 0)
                    {
                        adding += "\n## " + section.Key + "\n";
                        foreach (string line in section.Value) adding += line + "\n";
                        continue;
                    }
                    int after = from;
                    List<string> theirs = new List<string>();
                    for (int row = from; row < to; row++)
                    {
                        string one = made[row].Trim();
                        if (one.Length > 0) after = row + 1;
                        if (one.Length == 0 || one.StartsWith(">", StringComparison.Ordinal)) continue;
                        theirs.Add(one);
                    }
                    List<string> missing = new List<string>();
                    foreach (string line in section.Value)
                    {
                        bool known = false;
                        foreach (string mine in theirs) if (string.Equals(mine, line, StringComparison.OrdinalIgnoreCase)) { known = true; break; }
                        if (!known) missing.Add(line);
                    }
                    if (missing.Count == 0) continue;
                    made.InsertRange(after, missing);
                }

                string body = string.Join(Environment.NewLine, made.ToArray());
                if (adding.Length > 0)
                {
                    if (!body.EndsWith("\n")) body += Environment.NewLine;
                    body += adding.Replace("\n", Environment.NewLine);
                }
                if (body == existing) return;
                File.WriteAllText(path, body, new System.Text.UTF8Encoding(false));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        internal string Pick(CareKind kind)
        {
            return Pick(Care.Names[(int)kind]);
        }

        internal string Pick(string name)
        {
            string text;
            try { text = File.ReadAllText(path); }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            List<string> choices = new List<string>();
            bool section = false;
            using (StringReader reader = new StringReader(text))
            {
                string row;
                while ((row = reader.ReadLine()) != null)
                {
                    row = row.Trim();
                    if (row.StartsWith("#", StringComparison.Ordinal)) section = row == "## " + name;
                    else if (section && row.StartsWith("- ", StringComparison.Ordinal) && row.Substring(2).Trim().Length > 0)
                    {
                        string words = row.Substring(2).Trim();
                        if (!choices.Contains(words)) choices.Add(words);
                    }
                }
            }
            if (choices.Count == 0) return null;
            string previous;
            if (choices.Count > 1 && last.TryGetValue(name, out previous)) choices.Remove(previous);
            string picked = choices[dice.Next(choices.Count)];
            last[name] = picked;
            return picked;
        }
    }
}
