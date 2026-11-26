using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // Topics and hobbies she can pick up on (topics.md, about 1,500). Each personality picks a few of its own, leaning as
    // its file says; a mood adds two for the day. Picked on this PC, nothing asked online.
    internal static class Topics
    {
        internal const string FileName = "topics.md";

        private static readonly LineFile file = new LineFile(delegate { return Stock; });

        internal static string Kept { get { return file.Kept; } set { file.Kept = value; } }
        internal static string Text { get { return file.Text; } }
        internal static WrittenNote Open() { return file.Open(); }
        internal static void Write(bool over) { file.Write(over); }
        internal static void Forget() { file.Forget(); }

        internal sealed class Topic
        {
            internal string Heading, Name, Blurb;
        }

        // The list she ships, carried in her: the build puts topics.md in as a resource.
        private static string stock;
        internal static string Stock
        {
            get
            {
                if (stock != null) return stock;
                try
                {
                    using (Stream carried = Assembly.GetExecutingAssembly().GetManifestResourceStream("Capri.Topics.md"))
                        if (carried != null) using (StreamReader reader = new StreamReader(carried, Encoding.UTF8)) stock = reader.ReadToEnd().Replace("\r\n", "\n");
                }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                return stock ?? (stock = "# Topics and hobbies\n");
            }
        }

        // Every topic in her file, read at the last ": " so a name may hold one ("Mad Max: Fury Road").
        internal static List<Topic> All()
        {
            List<Topic> all = new List<Topic>();
            string[] headings = file.Sections();
            if (headings == null) return all;
            foreach (string heading in headings)
            {
                string[] rows = file.Lines(heading);
                if (rows == null) continue;
                foreach (string row in rows)
                {
                    Topic one = Read(heading, row);
                    if (one != null) all.Add(one);
                }
            }
            all.AddRange(new List<Topic>(GrownTopics(all)));
            return all;
        }

        internal static Topic Read(string heading, string row)
        {
            string line = (row ?? "").Trim();
            if (line.StartsWith("- ", StringComparison.Ordinal)) line = line.Substring(2).Trim();
            int at = line.LastIndexOf(": ", StringComparison.Ordinal);
            string name = at > 0 ? line.Substring(0, at).Trim() : line;
            if (name.Length == 0) return null;
            return new Topic { Heading = heading, Name = name, Blurb = at > 0 ? line.Substring(at + 2).Trim() : "" };
        }

        // The stock leanings, for a personality or mood whose file has no "Topics she leans to" section (her own has no
        // file); a heading not named weighs 1. Each pack and mood carries its own in its file.
        internal static readonly Dictionary<string, Dictionary<string, int>> Leans = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase)
        {
            { "Her own", Weights("computers 3, games 3, anime 2, science 2, space 2, internet and culture 2") },
            { "Caregiver", Weights("food 3, home and living 3, health and fitness 2, crafts 2, animals 2, outdoors 2") },
            { "Welcome Home", Weights("home and living 3, food 3, everyday life 2, books 2, series 2, drinks 2") },
            { "Devoted", Weights("everyday life 2, music 2, films 2, animals 2, travel and places 2, art 2") },
            { "Emo", Weights("music 3, mystery and the strange 3, art 2, books 2, anime 2, philosophy and big questions 2") },
            { "Bratty", Weights("games 3, internet and culture 3, fashion 2, food 2, anime 2, tabletop and games at the table 2") },
            { "Sassy", Weights("fashion 3, series 3, films 2, food 2, travel and places 2, internet and culture 2") },
            { "Feral", Weights("animals 3, outdoors 3, sport 2, food 2, games 2, more hobbies 2") },
        };

        // A mood's two for the day lean the same way.
        internal static readonly Dictionary<string, Dictionary<string, int>> MoodLeans = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase)
        {
            { "Sleepy", Weights("everyday life 3, home and living 3") }, { "Hyper", Weights("sport 3, games 3, music 2") },
            { "Moody", Weights("music 3, philosophy and big questions 2") }, { "Clingy", Weights("everyday life 3") },
            { "Needy", Weights("everyday life 3") }, { "Playful", Weights("games 3, tabletop and games at the table 3, more hobbies 2") },
            { "Touchy", Weights("art 3") }, { "Affectionate", Weights("animals 3, food 2") },
            { "Mischievous", Weights("mystery and the strange 3, internet and culture 2") }, { "Focused", Weights("computers 3, science 3, work and careers 2") },
            { "Melancholy", Weights("books 3, music 3, philosophy and big questions 2") }, { "Furious", Weights("sport 3") },
            { "Jealous", Weights("internet and culture 3") }, { "Smug", Weights("fashion 3, money and the world 2") },
        };

        internal static Dictionary<string, int> Weights(string said)
        {
            Dictionary<string, int> made = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string part in said.Split(','))
            {
                string one = part.Trim();
                int space = one.LastIndexOf(' ');
                int weight;
                if (space > 0 && int.TryParse(one.Substring(space + 1), out weight)) made[one.Substring(0, space).Trim()] = weight;
            }
            return made;
        }

        // Her picks as her settings keep them, a personality to the names it picked; set by her window.
        internal static Func<string, string[]> Saved = delegate { return null; };
        internal static Action<string, string[]> Save = delegate { };

        // What a line of theirs names from her list: a topic's whole name, or its name without a closing kind word ("Studio
        // Ghibli films" heard as "Studio Ghibli"), as a whole word or words. The names, never their words.
        private static readonly string[] Kinds = { "anime", "films", "film", "series", "games", "game", "books", "book", "shows", "show", "music" };

        internal static List<Topic> Named(string said, List<Topic> all)
        {
            List<Topic> found = new List<Topic>();
            if (string.IsNullOrEmpty(said) || all == null) return found;
            string text = " " + Regex.Replace(said.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ") + " ";
            foreach (Topic one in all)
            {
                string name = Regex.Replace(one.Name.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();
                string shorter = name;
                foreach (string kind in Kinds) if (name.EndsWith(" " + kind, StringComparison.Ordinal)) { shorter = name.Substring(0, name.Length - kind.Length - 1).Trim(); break; }
                if (name.Length >= 4 && text.Contains(" " + name + " ") || shorter.Length >= 5 && shorter != name && text.Contains(" " + shorter + " ")) found.Add(one);
            }
            return found;
        }

        // Their topics: each name heard, on how many days, flagged once it has come up on three. Kept as
        // "name|days|last day|flagged" rows with her settings.
        internal static Func<List<string>> Theirs = delegate { return null; };
        internal static Action SaveTheirs = delegate { };
        internal const int FlagDays = 3;

        internal static void Heard(string said, DateTime today)
        {
            List<string> rows = Theirs();
            if (rows == null) return;
            List<Topic> named = Named(said, All());
            if (named.Count == 0) return;
            string day = today.ToString("yyyy-MM-dd");
            bool changed = false;
            lock (rows)
                foreach (Topic one in named)
                {
                    int at = rows.FindIndex(row => row.StartsWith(one.Name + "|", StringComparison.OrdinalIgnoreCase));
                    string[] parts = at < 0 ? new[] { one.Name, "0", "", "no" } : rows[at].Split('|');
                    if (parts.Length < 4 || parts[2] == day) continue;
                    int days;
                    int.TryParse(parts[1], out days);
                    days++;
                    string made = one.Name + "|" + days + "|" + day + "|" + (days >= FlagDays ? "yes" : "no");
                    if (at < 0) rows.Add(made); else rows[at] = made;
                    changed = true;
                }
            if (changed) SaveTheirs();
        }

        // Their flagged topics, by name.
        internal static string[] TheirFlagged()
        {
            List<string> rows = Theirs(), flagged = new List<string>();
            if (rows == null) return flagged.ToArray();
            lock (rows) foreach (string row in rows) { string[] parts = row.Split('|'); if (parts.Length >= 4 && parts[3] == "yes") flagged.Add(parts[0]); }
            return flagged.ToArray();
        }

        // One of theirs added by hand on the Topics page: flagged at once.
        internal static void AddTheirs(string name)
        {
            List<string> rows = Theirs();
            if (rows == null || string.IsNullOrWhiteSpace(name)) return;
            lock (rows)
            {
                rows.RemoveAll(row => row.StartsWith(name + "|", StringComparison.OrdinalIgnoreCase));
                rows.Add(name.Trim() + "|" + FlagDays + "||yes");
            }
            SaveTheirs();
        }

        internal static void Unflag(string name)
        {
            List<string> rows = Theirs();
            if (rows == null) return;
            lock (rows) rows.RemoveAll(row => row.StartsWith(name + "|", StringComparison.OrdinalIgnoreCase));
            SaveTheirs();
        }

        // What she knows about a topic, kept in expansionLines' topics.txt: "<name> facts", "<name> starts" and "<name> keeps
        // going". Stored first; with Better conversations on, a thin one is asked for once a session, one ask a minute.
        internal const string Known = "topics";
        private static readonly HashSet<string> learnt = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object learning = new object();
        private static DateTime lastLearn = DateTime.MinValue;
        private static bool asking;
        internal static Func<DateTime> Now = delegate { return DateTime.UtcNow; };

        internal static string[] Facts(string name) { return Expansion.LinesNamed(Known, name + " facts"); }

        internal static string Words(string name, string blurb)
        {
            return "The topic: " + name + (string.IsNullOrEmpty(blurb) ? "" : " (" + blurb + ")") + ".\n" +
                "First say whether it is well known enough to talk about: it has a wiki page or many reviews. Then give plain facts and " +
                "talking points someone keen on it would know, and short lines a cheeky friend might say to start a chat about it or to " +
                "keep one going. One sentence each, no links, no lists inside a line, nothing rude.\n\n" +
                "Reply with only a JSON object and nothing else: \"well known\" with [\"yes\"] or [\"no\"], \"facts\" with five facts, " +
                "\"starts\" with five lines, \"keeps going\" with five lines.\n";
        }

        internal static void LearnWhenThin(string name)
        {
            if (string.IsNullOrEmpty(name) || !Keyless.On() || Facts(name).Length >= 3) return;
            lock (learning)
            {
                if (asking || learnt.Contains(name) || Now() - lastLearn < TimeSpan.FromMinutes(1)) return;
                learnt.Add(name);
                asking = true;
                lastLearn = Now();
            }
            Topic found = All().Find(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            string blurb = found == null ? null : found.Blurb;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try { Learn(name, blurb); }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                finally { lock (learning) asking = false; }
            });
        }

        // One ask and what it kept; refused, and nothing kept, when the reply says the topic isn't well known. How many lines.
        internal static int Learn(string name, string blurb) { bool refused; return Learn(name, blurb, out refused); }

        internal static int Learn(string name, string blurb, out bool refused)
        {
            refused = false;
            string who;
            string reply = AskFree(Words(name, blurb), out who);
            if (reply == null) return 0;
            Dictionary<string, string[]> kept = Blends.Kept(reply, new List<string> { "well known", "facts", "starts", "keeps going" });
            string[] known;
            if (!kept.TryGetValue("well known", out known) || known.Length == 0 || !known[0].StartsWith("yes", StringComparison.OrdinalIgnoreCase))
            {
                CapriDesktop.Brain.Log("PROBLEM", -1, "Topics: " + who + " said " + name + " isn't well known enough to talk about, or didn't say; nothing kept.");
                refused = true;
                return 0;
            }
            int added = 0;
            foreach (string part in new[] { "facts", "starts", "keeps going" })
            {
                string[] lines;
                if (kept.TryGetValue(part, out lines)) added += Expansion.AddNamed(Known, name + " " + part, lines);
            }
            CapriDesktop.Brain.Log("conversations", -1, "Topics: " + who + " gave " + added + " new line(s) about " + name);
            return added;
        }

        // The free services on either side: her own brain knows little of the other side's topics, so it is asked there too,
        // and her own brain only when none of them answers.
        private static string AskFree(string words, out string who)
        {
            if (!Brain.SenpaiLive) return Keyless.Ask(words, out who);
            who = "the free services";
            string reply = Keyless.AskAny(Keyless.Instructions, words);
            if (reply != null) return reply;
            who = "her own brain";
            Func<string, string, string> own = Keyless.OwnBrain;
            try { reply = own == null ? null : own(Keyless.Instructions, words); }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); reply = null; }
            return string.IsNullOrWhiteSpace(reply) || reply.IndexOf('{') < 0 ? null : reply;
        }

        // For the checks: a new session's asks.
        internal static void ForgetAsks() { lock (learning) { learnt.Clear(); asking = false; lastLearn = DateTime.MinValue; lastGrow = DateTime.MinValue; } }

        // Names grown under their topics, then hers: recent, upcoming and top rated asked of the free services as she idles,
        // each kept once its knowledge ask says well known (expansionLines' "topics grown" files, a "parent|name" row each).
        internal const string GrownFile = "topics grown";
        internal static readonly string[] ListKinds = { "recent", "upcoming", "top rated" };
        private static DateTime lastGrow = DateTime.MinValue;

        internal static string ListWords(string topic, string kind)
        {
            string when = kind == "recent" ? "from the last ten years" : kind == "upcoming" ? "announced and still to come out" : "rated the best of all time, a top 20";
            return "The topic: " + topic + ".\n" +
                "Name up to " + (kind == "top rated" ? 20 : 10) + " titles or names under it " + when + ". Real ones only, each with a wiki page " +
                "or many reviews" + (Brain.SenpaiLive ? "" : ", keep it clean") + ". Names only, no links.\n\n" +
                "Reply with only a JSON object and nothing else: \"names\" with the names.\n";
        }

        private static List<string> GrownRows(string part) { return new List<string>(Expansion.LinesNamed(GrownFile + " " + part, "rows")); }

        // A file each, since a row is written once per file whatever its section.
        private static void AddGrown(string part, IEnumerable<string> rows) { Expansion.AddNamed(GrownFile + " " + part, "rows", rows); }

        private static bool HasRow(List<string> rows, string row) { return rows.Exists(one => string.Equals(one, row, StringComparison.OrdinalIgnoreCase)); }

        // The kept names as topics, under their parent's heading.
        private static IEnumerable<Topic> GrownTopics(List<Topic> stock)
        {
            Dictionary<string, string> headings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Topic one in stock) if (!headings.ContainsKey(one.Name)) headings[one.Name] = one.Heading;
            foreach (string row in GrownRows("kept"))
            {
                int bar = row.IndexOf('|');
                if (bar <= 0 || bar == row.Length - 1) continue;
                string parent = row.Substring(0, bar), name = row.Substring(bar + 1), heading;
                if (headings.ContainsKey(name)) continue;
                headings.TryGetValue(parent, out heading);
                yield return new Topic { Heading = heading ?? "Your topics", Name = name, Blurb = parent };
            }
        }

        // A name with a word from her gate's list never goes on the SFW side's list.
        private static bool Gated(string name)
        {
            if (Brain.SenpaiLive) return false;
            foreach (string word in System.Text.RegularExpressions.Regex.Split(name ?? "", @"[^\p{L}\p{N}']+"))
                if (word.Length > 0 && TwitchAloud.IsGated(word)) return true;
            return false;
        }

        // As she idles, at the idle top-up's pace: one ask, or nothing when it isn't time.
        internal static void GrowWhenIdle(string personality, string mood)
        {
            if (!Keyless.On() || !Keyless.Free()) return;
            lock (learning)
            {
                if (asking || Now() - lastGrow < Keyless.IdleEvery) return;
                asking = true;
                lastGrow = Now();
            }
            List<string> order = new List<string>(TheirFlagged());
            foreach (string one in Hers(personality, mood)) if (!order.Exists(o => string.Equals(o, one, StringComparison.OrdinalIgnoreCase))) order.Add(one);
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try { Grow(order); }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                finally { lock (learning) asking = false; }
            });
        }

        // One ask: a name waiting on its well known check first, else the next list for the first topic still missing one.
        // What it did, or null when there was nothing to ask.
        internal static string Grow(List<string> order)
        {
            List<string> pending = GrownRows("pending"), kept = GrownRows("kept"), dropped = GrownRows("dropped"), asked = GrownRows("asked");
            foreach (string row in pending)
            {
                if (HasRow(kept, row) || HasRow(dropped, row)) continue;
                int bar = row.IndexOf('|');
                string parent = row.Substring(0, bar), name = row.Substring(bar + 1);
                bool refused;
                Learn(name, parent, out refused);
                if (refused) { AddGrown("dropped", new[] { row }); return "dropped " + name; }
                if (Facts(name).Length > 0) { AddGrown("kept", new[] { row }); return "kept " + name; }
                return null;
            }
            HashSet<string> known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Topic one in All()) known.Add(one.Name);
            foreach (string topic in order)
                foreach (string kind in ListKinds)
                {
                    if (HasRow(asked, topic + "|" + kind)) continue;
                    string who;
                    string reply = AskFree(ListWords(topic, kind), out who);
                    if (reply == null) return null;
                    AddGrown("asked", new[] { topic + "|" + kind });
                    string[] names;
                    Blends.Kept(reply, new List<string> { "names" }).TryGetValue("names", out names);
                    List<string> fresh = new List<string>();
                    foreach (string raw in names ?? new string[0])
                    {
                        string name = (raw ?? "").Trim().TrimEnd('.');
                        if (name.Length == 0 || name.Length > 80 || name.Contains("|") || name.Contains("://") || known.Contains(name) || Gated(name)) continue;
                        string row = topic + "|" + name;
                        if (HasRow(dropped, row) || HasRow(kept, row)) continue;
                        known.Add(name);
                        fresh.Add(row);
                    }
                    int added = fresh.Count;
                    AddGrown("pending", fresh);
                    CapriDesktop.Brain.Log("conversations", -1, "Topics: " + who + " named " + added + " new " + kind + " name(s) under " + topic + ", each to be checked.");
                    return "asked " + kind + " under " + topic;
                }
            return null;
        }

        // Her bringing up a topic of hers: when bored, when chatty, in a chat or with a pop-in, never closer together than the
        // topic gap in dynamic.md. Her line: a starter grown for that topic, or one of her stock ones with its name.
        internal static Func<TimeSpan> Gap = delegate { return TimeSpan.FromHours(Dynamics.TopicGap); };
        // Her wanting (0 to 100) and how much shorter a high one makes the gap, in percent; never under 15 minutes.
        internal static Func<int> Wanting = delegate { return Blends.Percent(); };
        internal static Func<int> WantingCut = delegate { return Dynamics.TopicGapWanting; };
        internal const int WantingHigh = 70;

        internal static TimeSpan GapNow()
        {
            TimeSpan gap = Gap();
            if (Wanting() >= WantingHigh) gap = TimeSpan.FromTicks((long)(gap.Ticks * (1 - Math.Max(0, Math.Min(50, WantingCut())) / 100.0)));
            return gap < TimeSpan.FromMinutes(15) ? TimeSpan.FromMinutes(15) : gap;
        }
        internal static Func<DateTime> Clock = delegate { return SimTime.UtcNow; };
        private static DateTime raisedAt = DateTime.MinValue;
        private static readonly object raising = new object();
        internal static readonly string[] Chatty = { "Hyper", "Playful", "Clingy", "Needy" };

        internal static bool Due() { TimeSpan gap = GapNow(); lock (raising) return Clock() - raisedAt >= gap; }

        private static string Take(string personality, string mood, Random dice)
        {
            string[] hers = Hers(personality, mood);
            if (hers.Length == 0) return null;
            TimeSpan gap = GapNow();
            lock (raising)
            {
                if (Clock() - raisedAt < gap) return null;
                raisedAt = Clock();
            }
            return hers[dice.Next(hers.Length)];
        }

        // A line of hers bringing one up, or null when it isn't time.
        internal static string Raise(string personality, string mood, Random dice)
        {
            string name = Take(personality, mood, dice);
            if (name == null) return null;
            string[] grown = Expansion.LinesNamed(Known, name + " starts");
            if (grown.Length > 0 && dice.Next(2) == 0) return grown[dice.Next(grown.Length)];
            return Sayings.TopicStart(name);
        }

        // In a chat, at a natural turn: the line for her turn note, or "" when it isn't time (one turn in four once due).
        internal static string RaiseInTalk(string personality, string mood, Random dice)
        {
            if (!Due() || dice.Next(4) != 0) return "";
            string name = Take(personality, mood, dice);
            return name == null ? "" : "\nIf it fits where the talk is going, bring up " + name + ", one of your interests, in your own words.";
        }

        // For the checks: the gap starts over.
        internal static void ForgetRaised() { lock (raising) raisedAt = DateTime.MinValue; }

        // The line on her turn note: her topics today, and how a topic named in their line sits with her (hers, keen; theirs
        // or any other, polite interest). Empty when there is nothing to say.
        internal static string Note(string said, string personality, string mood)
        {
            string[] hers = Hers(personality, mood);
            StringBuilder note = new StringBuilder();
            if (hers.Length > 0) note.Append("\nYour own interests today: ").Append(string.Join(", ", hers)).Append(".");
            foreach (string mine in hers) LearnWhenThin(mine);
            note.Append(RaiseInTalk(personality, mood, new Random()));
            List<Topic> named = Named(said, All());
            foreach (Topic one in named)
            {
                bool own = Array.Exists(hers, h => string.Equals(h, one.Name, StringComparison.OrdinalIgnoreCase));
                note.Append("\nThey mentioned ").Append(one.Name).Append(own ? ", one of your interests: be keen about it." : ": not your thing, but show polite interest.");
                string[] facts = Facts(one.Name);
                if (facts.Length > 0) note.Append(" What you know about it: ").Append(string.Join(" ", facts, 0, Math.Min(3, facts.Length)));
                LearnWhenThin(one.Name);
                if (note.Length > 600) break;
            }
            return note.ToString();
        }

        // Her own topics as this personality: kept once picked, so they stay hers; a mood adds two for today.
        internal static string[] Hers(string personality, string mood)
        {
            List<string> hers = new List<string>();
            if (string.IsNullOrEmpty(personality)) return hers.ToArray();
            List<Topic> all = All();
            string[] kept = Saved(personality);
            if (kept == null || kept.Length == 0)
            {
                Random dice = new Random();
                kept = Pick(all, Personalities.TopicLeans(personality) ?? Lean(Leans, personality), 6 + dice.Next(5), dice);
                if (kept.Length > 0) Save(personality, kept);
            }
            hers.AddRange(kept);
            Dictionary<string, int> moodLean = string.IsNullOrEmpty(mood) ? null : Dynamics.TopicLeans(mood) ?? (MoodLeans.ContainsKey(mood) ? MoodLeans[mood] : null);
            if (moodLean != null)
            {
                // The same two all day: seeded by the date and the mood.
                Random today = new Random((DateTime.Today.ToString("yyyyMMdd") + mood).GetHashCode());
                foreach (string one in Pick(all.FindAll(t => !hers.Contains(t.Name)), moodLean, 2, today)) hers.Add(one);
            }
            return hers.ToArray();
        }

        private static Dictionary<string, int> Lean(Dictionary<string, Dictionary<string, int>> table, string name)
        {
            Dictionary<string, int> lean;
            return table.TryGetValue(name ?? "", out lean) ? lean : new Dictionary<string, int>();
        }

        // How many, weighted by heading, none twice.
        internal static string[] Pick(List<Topic> from, Dictionary<string, int> lean, int many, Random dice)
        {
            List<Topic> left = new List<Topic>(from);
            List<string> picked = new List<string>();
            while (picked.Count < many && left.Count > 0)
            {
                long total = 0;
                foreach (Topic one in left) total += Weight(lean, one.Heading);
                long roll = (long)(dice.NextDouble() * total);
                int at = 0;
                for (; at < left.Count - 1; at++)
                {
                    roll -= Weight(lean, left[at].Heading);
                    if (roll < 0) break;
                }
                picked.Add(left[at].Name);
                left.RemoveAt(at);
            }
            return picked.ToArray();
        }

        // Her list as shipped: about 1,500 under some 30 headings, no name twice, no link; written to her folder and read
        // back whole. Her picks: 6 to 10, kept and the same next time, mostly from the headings she leans to, and a mood's
        // two the same all day.
#if CHECKS
        internal static void Check(List<string> checks)
        {
            string keptWas = Kept;
            Func<string, string[]> savedWas = Saved;
            Action<string, string[]> saveWas = Save;
            string folder = Path.Combine(Path.GetTempPath(), "capri-topics-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                Directory.CreateDirectory(folder);
                Kept = Path.Combine(folder, FileName);
                Forget();
                Write(false);
                List<Topic> all = All();
                HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                HashSet<string> headings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string twice = null, linked = null;
                foreach (Topic one in all)
                {
                    if (!names.Add(one.Name)) twice = one.Name;
                    headings.Add(one.Heading);
                    if ((one.Name + one.Blurb).IndexOf("http", StringComparison.OrdinalIgnoreCase) >= 0 || (one.Name + one.Blurb).IndexOf("www.", StringComparison.OrdinalIgnoreCase) >= 0) linked = one.Name;
                }
                if (!File.Exists(Kept) || all.Count < 1500 || headings.Count < 30 || twice != null || linked != null || Read("films", "Mad Max: Fury Road: a war rig").Name != "Mad Max: Fury Road")
                    throw new Exception("Her topics list read wrong: " + all.Count + " topics, " + headings.Count + " headings, twice " + twice + ", a link in " + linked);
                Dictionary<string, string[]> kept = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                Saved = delegate(string who) { string[] had; return kept.TryGetValue(who, out had) ? had : null; };
                Save = delegate(string who, string[] picked) { kept[who] = picked; };
                string[] first = Hers("Her own", null), again = Hers("Her own", null);
                bool stays = first.Length >= 6 && first.Length <= 10 && string.Join("|", first) == string.Join("|", again);
                string[] moody = Hers("Her own", "Hyper"), moodyAgain = Hers("Her own", "Hyper");
                bool mood = moody.Length == first.Length + 2 && string.Join("|", moody) == string.Join("|", moodyAgain);
                Dictionary<string, int> lean = Leans["Her own"];
                int leaning = 0, picks = 0;
                Random dice = new Random(7);
                for (int i = 0; i < 40; i++)
                    foreach (string name in Pick(all, lean, 8, dice))
                    {
                        picks++;
                        Topic one = all.Find(t => t.Name == name);
                        if (one != null && lean.ContainsKey(one.Heading)) leaning++;
                    }
                // The leanings live in each pack's and mood's own file; the table here is only the fallback.
                Dictionary<string, int> emo = Personalities.TopicLeans("Emo"), hyper = Dynamics.TopicLeans("Hyper");
                bool filed = emo != null && emo.ContainsKey("music") && emo["music"] == 3 && hyper != null && hyper.ContainsKey("sport") && Personalities.LeansIn("# X\n## Words she likes\nhi: 1\n") == null;
                if (!filed) throw new Exception("A pack's or mood's topics it leans to weren't read from its file: Emo " + (emo == null ? "none" : string.Join(",", new List<string>(emo.Keys).ToArray())) + ", Hyper " + (hyper == null ? "none" : "some"));
                if (!stays || !mood || leaning * 2 < picks)
                    throw new Exception("Her own topics went wrong: " + first.Length + " picked, kept " + stays + ", a mood's two for the day " + mood + ", leaning " + leaning + " of " + picks);
                checks.Add("PASS her topics: " + all.Count + " under " + headings.Count + " headings, none twice, no link, read at the last colon; her own 6 to 10 kept as picked (" + string.Join(", ", first) + "), a mood's two the same all day, " + leaning + " of " + picks + " picks from the headings she leans to");
                // Theirs: a name heard on three days is flagged, once a day counts, only the name is kept; her turn note.
                List<string> rows = new List<string>();
                Func<List<string>> theirsWas = Theirs;
                Action saveTheirsWas = SaveTheirs;
                try
                {
                    Theirs = delegate { return rows; };
                    SaveTheirs = delegate { };
                    Topic ghibli = all.Find(t => t.Name.EndsWith(" films", StringComparison.Ordinal) && t.Name.Length > 10 && Named(t.Name.Substring(0, t.Name.Length - 6), all).Count == 1) ?? all[0];
                    string shortName = ghibli.Name.Substring(0, ghibli.Name.Length - " films".Length);
                    string line = "watched some " + shortName.ToLowerInvariant() + " stuff with my sister last night";
                    DateTime day = new DateTime(2026, 11, 26);
                    Heard(line, day);
                    Heard(line, day);
                    Heard(line, day.AddDays(1));
                    bool notYet = TheirFlagged().Length == 0;
                    Heard(line, day.AddDays(3));
                    string[] flagged = TheirFlagged();
                    bool namesOnly = rows.TrueForAll(row => !row.Contains("sister"));
                    string note = Note(line, "Emo", null);
                    if (Named(line, all).Count == 0 || !notYet || flagged.Length != 1 || flagged[0] != ghibli.Name || !namesOnly || note.IndexOf("They mentioned " + ghibli.Name, StringComparison.Ordinal) < 0 || note.IndexOf("Your own interests today", StringComparison.Ordinal) < 0)
                        throw new Exception("Their topics or her note went wrong: named " + Named(line, all).Count + ", flagged early " + !notYet + ", flagged " + string.Join(",", flagged) + ", names only " + namesOnly + ", note: " + note);
                    Unflag(ghibli.Name);
                    if (TheirFlagged().Length != 0) throw new Exception("A topic of theirs taken off stayed.");
                    checks.Add("PASS their topics: a name from her list heard on a third day is flagged (" + ghibli.Name + ", heard as \"" + shortName + "\"), once a day counts, only the name is kept and Remove takes it off; her turn note carries her interests today and how a named topic sits with her");
                }
                finally { Theirs = theirsWas; SaveTheirs = saveTheirsWas; }
                // Knowledge, with stand-in services (nothing sent): a well known topic's facts, starts and keep-going lines kept
                // and on her note; a not well known one refused with nothing kept; one ask a session.
                Func<Keyless.Engine, string, string, string> sendWas = Keyless.Send;
                Func<bool> onWas = Keyless.On;
                Func<string> expansionWas = Expansion.Folder;
                string grown = Path.Combine(folder, "expansion");
                int sent = 0;
                try
                {
                    Expansion.Folder = delegate { return grown; };
                    Keyless.On = delegate { return true; };
                    foreach (Keyless.Engine one in Keyless.Engines) { one.Fails = 0; one.WaitUntil = DateTime.MinValue; one.Sent.Clear(); }
                    Keyless.Send = delegate(Keyless.Engine one, string system, string asked)
                    {
                        sent++;
                        return asked.Contains("Mystery fruit")
                            ? "{\"well known\": [\"no\"], \"facts\": [\"Made up.\"]}"
                            : "{\"well known\": [\"yes\"], \"facts\": [\"It started in 1999.\", \"It has a huge fan base.\", \"There is a sequel.\"], \"starts\": [\"Have you seen the sequel?\"], \"keeps going\": [\"Which part did you like most?\"]}";
                    };
                    Topic known = all[0];
                    int learnt = Learn(known.Name, known.Blurb), refused = Learn("Mystery fruit", null);
                    Keyless.On = delegate { return false; };
                    string note = Note("I love " + known.Name.ToLowerInvariant() + " so much", "Emo", null);
                    Keyless.On = delegate { return true; };
                    ForgetAsks();
                    sent = 0;
                    LearnWhenThin("Another topic");
                    for (int i = 0; i < 40 && sent == 0; i++) System.Threading.Thread.Sleep(50);
                    LearnWhenThin("Another topic");
                    System.Threading.Thread.Sleep(300);
                    if (learnt != 5 || refused != 0 || Facts("Mystery fruit").Length != 0 || note.IndexOf("It started in 1999.", StringComparison.Ordinal) < 0 || sent != 1)
                        throw new Exception("Topic knowledge went wrong: kept " + learnt + ", not well known kept " + refused + ", facts on her note " + (note.IndexOf("It started in 1999.", StringComparison.Ordinal) >= 0) + ", asks in a session " + sent);
                    checks.Add("PASS topic knowledge with stand-in services: a well known topic's facts, starts and keep-going lines kept in expansionLines' topics.txt and its facts on her note when they name it; a not well known one refused with nothing kept; one ask a session");
                }
                finally { Keyless.Send = sendWas; Keyless.On = onWas; Expansion.Folder = expansionWas; ForgetAsks(); }
                // Names grown with stand-in services, nothing sent: one ask at a time, gated or listed names left out, a refused
                // one dropped.
                Func<Keyless.Engine, string, string, string> growSendWas = Keyless.Send;
                Func<bool> growOnWas = Keyless.On;
                Func<string> growFolderWas = Expansion.Folder;
                string growRoot = Path.Combine(folder, "grown");
                List<string> asks = new List<string>();
                const string standIn = "zzstandinword";
                TwitchAloud.StandIn(standIn, true);
                try
                {
                    Expansion.Folder = delegate { return growRoot; };
                    Keyless.On = delegate { return true; };
                    foreach (Keyless.Engine one in Keyless.Engines) { one.Fails = 0; one.WaitUntil = DateTime.MinValue; one.Sent.Clear(); }
                    Topic parent = all[0];
                    string listed = all[1].Name;
                    Keyless.Send = delegate(Keyless.Engine one, string system, string asked)
                    {
                        lock (asks) asks.Add(asked);
                        if (asked.Contains("Name up to") && asked.Contains("last ten years")) return "{\"names\": [\"Bright Comet\", \"Made Up Thing\", \"" + standIn + " Show\", \"" + listed + "\"]}";
                        if (asked.Contains("Name up to") && asked.Contains("still to come out")) return "{\"names\": [\"Bright Comet\"]}";
                        if (asked.Contains("Name up to")) return "{\"names\": [\"Old Classic\"]}";
                        if (asked.Contains("Made Up Thing")) return "{\"well known\": [\"no\"]}";
                        string about = asked.Contains("Old Classic") ? "the classic" : "the comet one";
                        return "{\"well known\": [\"yes\"], \"facts\": [\"People love " + about + ".\", \"" + about + " won awards.\", \"" + about + " has a sequel.\"], \"starts\": [\"Seen " + about + "?\"], \"keeps going\": [\"What did you like about " + about + "?\"]}";
                    };
                    List<string> order = new List<string> { parent.Name };
                    List<string> steps = new List<string>();
                    for (int i = 0; i < 8; i++) { string did = Grow(order); if (did == null) break; steps.Add(did); }
                    string said = string.Join(" / ", steps.ToArray());
                    List<Topic> grownNames = All().FindAll(t => t.Blurb == parent.Name && (t.Name == "Bright Comet" || t.Name == "Old Classic"));
                    bool shaped = grownNames.Count == 2 && grownNames.TrueForAll(t => t.Heading == parent.Heading);
                    bool gatedOut = !asks.Exists(a => a.Contains(standIn + " Show")) && !All().Exists(t => t.Name.Contains(standIn));
                    bool dropped = !All().Exists(t => t.Name == "Made Up Thing") && GrownRows("dropped").Count == 1;
                    if (said != "asked recent under " + parent.Name + " / kept Bright Comet / dropped Made Up Thing / asked upcoming under " + parent.Name + " / asked top rated under " + parent.Name + " / kept Old Classic"
                        || !shaped || !gatedOut || !dropped || asks.Count != 6 || Facts("Bright Comet").Length < 3)
                        throw new Exception("Grown names went wrong: " + said + " | under its parent " + shaped + ", gated left out " + gatedOut + ", refused dropped " + dropped + ", asks " + asks.Count + ": " + string.Join(" || ", asks.ConvertAll(x => x.Length > 70 ? x.Substring(0, 70) : x).ToArray()) + " | pending " + string.Join(", ", GrownRows("pending").ToArray()) + " | dropped " + string.Join(", ", GrownRows("dropped").ToArray()));
                    // The other side asks the free services too, her own brain only when none of them answers.
                    bool senpaiWas = Brain.SenpaiOn, airWas = Brain.OnAir;
                    Func<string, string, string> ownWas = Keyless.OwnBrain;
                    int own = 0, freeBefore = asks.Count;
                    try
                    {
                        Brain.SenpaiOn = true;
                        Brain.OnAir = false;
                        Keyless.OwnBrain = delegate { own++; return "{\"well known\": [\"yes\"], \"facts\": [\"Her own words on it.\"]}"; };
                        bool refusedThere;
                        Learn("Side topic", null, out refusedThere);
                        bool online = asks.Count == freeBefore + 1 && own == 0;
                        Keyless.Send = delegate { return null; };
                        foreach (Keyless.Engine one in Keyless.Engines) { one.Fails = 0; one.WaitUntil = DateTime.MinValue; one.Sent.Clear(); }
                        Learn("Other side topic", null, out refusedThere);
                        if (!online || own != 1 || Facts("Other side topic").Length != 1)
                            throw new Exception("The other side's asks didn't go to the free services first: free first " + online + ", her own brain " + own);
                    }
                    finally { Brain.SenpaiOn = senpaiWas; Brain.OnAir = airWas; Keyless.OwnBrain = ownWas; }
                    checks.Add("PASS grown names with stand-in services: recent, upcoming and top rated names asked under a topic one ask at a time, theirs first; a gated or already listed name never kept; each kept only once its knowledge ask says well known, then a topic under its parent's heading with its facts; a refused one dropped with a PROBLEM line; on the other side the free services are asked too, her own brain only when none answers");
                }
                finally { Keyless.Send = growSendWas; Keyless.On = growOnWas; Expansion.Folder = growFolderWas; TwitchAloud.StandIn(standIn, false); ForgetAsks(); }
                // Bringing one up: a long chat and an hour of pop-ins bring up at most one, under the gap; past it, one more.
                Func<DateTime> clockWas = Clock;
                Func<TimeSpan> gapWas = Gap;
                Func<int> wantingWas = Wanting, cutWas = WantingCut;
                DateTime at = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);
                try
                {
                    Clock = delegate { return at; };
                    Gap = delegate { return TimeSpan.FromHours(2); };
                    int wanting = 40;
                    Wanting = delegate { return wanting; };
                    WantingCut = delegate { return 15; };
                    ForgetRaised();
                    Random raiseDice = new Random(5);
                    int raised = 0;
                    string firstRaised = null;
                    for (int minute = 0; minute < 60; minute++)
                    {
                        at = at.AddMinutes(1);
                        for (int turn = 0; turn < 5; turn++) if (RaiseInTalk("Emo", null, raiseDice).Length > 0) raised++;
                        string line = Raise("Emo", null, raiseDice);
                        if (line != null) { raised++; firstRaised = firstRaised ?? line; }
                    }
                    int inHour = raised;
                    at = at.AddHours(2);
                    string later = Raise("Emo", null, raiseDice);
                    if (inHour != 1 || later == null)
                        throw new Exception("Bringing up her topics went wrong: " + inHour + " in a long chat and an hour of pop-ins, none past the gap " + (later == null));
                    // High wanting, any mood: 15% off two hours is 1h42m, so 1h45m on is due for her and not for a calm one.
                    at = at.AddMinutes(105);
                    bool calmDue = Due();
                    wanting = 85;
                    bool wantingDue = Due();
                    WantingCut = delegate { return 99; };
                    Gap = delegate { return TimeSpan.FromMinutes(20); };
                    TimeSpan floor = GapNow();
                    if (calmDue || !wantingDue || floor != TimeSpan.FromMinutes(15))
                        throw new Exception("High wanting didn't shorten the topic gap a little: calm due at 1h45m " + calmDue + ", wanting due " + wantingDue + ", the floor " + floor);
                    checks.Add("PASS her bringing up a topic: 300 chat turns and 60 pop-ins in an hour bring up one, never closer than the topic gap; past it she brings up another; high wanting in any mood takes topic gap wanting off it, never under 15 minutes");
                }
                finally { Clock = clockWas; Gap = gapWas; Wanting = wantingWas; WantingCut = cutWas; ForgetRaised(); }
            }
            finally
            {
                Kept = keptWas;
                Saved = savedWas;
                Save = saveWas;
                Forget();
                try { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
        }
#endif

        private static int Weight(Dictionary<string, int> lean, string heading)
        {
            int weight;
            // A heading she leans to counts four times its weight, so most of her picks come from them.
            return lean != null && lean.TryGetValue(heading ?? "", out weight) ? Math.Max(0, weight) * 4 : 1;
        }
    }
}
