using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CapriDesktop
{
    // Mood blends: a personality's voice coloured by today's mood, written once by her brain per pair into
    // "<personality> when <mood>.md". Tokens stay tokens and no gated word is sent; a refusal or a wrong reply is
    // dropped once and today's lines are used.
    internal static class Blends
    {
        internal const string FolderName = "blends";
        // Where the blends of the side in use are kept. Set by her window.
        internal static Func<string> Folder;
        // Her brain, asked (system, words) for the lines. Null or Ready false: no blend is asked for.
        internal static Func<string, string, string> Ask;
        internal static Func<bool> Ready;
        // A word that must never be sent or kept on the SFW side. Tests swap it.
        internal static Func<string, bool> Gated = Legacy.SaysUnsaid;
        // How forward she is (0 to 10) and how warm she is with them now (-60 to 60). Set by her window; tests swap it.
        internal static Func<KeyValuePair<int, int>> Feeling = delegate { return new KeyValuePair<int, int>(2, 0); };

        // How much she loves and wants them, in four steps: half how forward, half how warm she is. Part of a blend's
        // name, so a pair is written again as she grows fonder, and told to the brain so a sad mood never hides it.
        internal static int Percent()
        {
            KeyValuePair<int, int> feeling = Feeling();
            double forward = Math.Max(0, Math.Min(10, feeling.Key)) / 10.0, warm = (Math.Max(-60, Math.Min(60, feeling.Value)) + 60) / 120.0;
            return (int)Math.Round((forward + warm) / 2 * 100);
        }

        internal static int Step()
        {
            KeyValuePair<int, int> feeling = Feeling();
            double forward = Math.Max(0, Math.Min(10, feeling.Key)) / 10.0, warm = (Math.Max(-60, Math.Min(60, feeling.Value)) + 60) / 120.0;
            return (int)Math.Round((forward + warm) / 2 * 3);
        }

        private static readonly string[] Fond =
        {
            "They are fond of the player and look out for them, warmly.",
            "They care about the player a lot and are happiest with the player around.",
            "They adore the player and openly want the player's attention and company.",
            "They are devoted to the player, want the player close and are not shy about it."
        };

        private const int Each = 6, MostSets = 12, Longest = 160, Fewest = 3;
        private static readonly object gate = new object();
        private static readonly HashSet<string> tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, KeyValuePair<DateTime, Dictionary<string, string[]>>> read = new Dictionary<string, KeyValuePair<DateTime, Dictionary<string, string[]>>>(StringComparer.OrdinalIgnoreCase);
        private static readonly string[] Tokens = { "%boy%", "%he%", "%them%", "%their%", "%is%", "%name%", "%bits%", "%mate%", "%petname%" };
        private static readonly Regex Refusal = new Regex(@"\b(?:i can'?t|i cannot|i won'?t|i'm (?:sorry|unable|not able)|as an ai|i am an ai|language model|not appropriate|inappropriate|against (?:my|the) (?:guidelines|policy)|i'd rather not)\b", RegexOptions.IgnoreCase);

        internal static string PathFor(string personality, string mood)
        {
            Func<string> folder = Folder;
            string root = folder == null ? null : folder();
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(personality) || string.IsNullOrEmpty(mood)) return null;
            string name = Regex.Replace(personality + " when " + mood + " fond " + Step(), @"[^\w\- ]", "");
            return Path.Combine(root, name + ".md");
        }

        // Her lines for this reaction in this pair, or null when there is no blend for it yet.
        internal static string[] Lines(string personality, string mood, string set)
        {
            string path = PathFor(personality, mood);
            if (path == null) return null;
            // None yet for how fond she is now: asked for, once a session, and today's lines meanwhile.
            if (!File.Exists(path)) { Start(personality, mood); return null; }
            try
            {
                DateTime stamp = File.GetLastWriteTimeUtc(path);
                Dictionary<string, string[]> sets;
                lock (gate)
                {
                    KeyValuePair<DateTime, Dictionary<string, string[]>> had;
                    if (read.TryGetValue(path, out had) && had.Key == stamp) sets = had.Value;
                    else { sets = Parse(Notes.ReadAll(path)); read[path] = new KeyValuePair<DateTime, Dictionary<string, string[]>>(stamp, sets); }
                }
                string[] lines;
                if (!sets.TryGetValue(set, out lines)) return null;
                lines = Array.FindAll(lines, line => !RunsOn(line));
                return lines.Length > 0 ? lines : null;
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); return null; }
        }

        // A mood rolled on a personality: its blend is asked for in the background, once a session, when there is none.
        internal static void Start(string personality, string mood)
        {
            string path = PathFor(personality, mood);
            if (path == null || File.Exists(path)) return;
            lock (gate) if (!tried.Add(path)) return;
            Func<bool> ready = Ready;
            if (Ask == null || ready == null || !ready()) { lock (gate) tried.Remove(path); return; }
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string character;
                    Dictionary<string, KeyValuePair<string[], string[]>> sources = Personalities.BlendSources(out character);
                    Make(path, personality, mood, character, sources);
                }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            });
        }

        // Asks for the pair's lines and keeps the ones that pass. How many reactions were kept; 0 writes nothing.
        internal static int Make(string path, string personality, string mood, string character, Dictionary<string, KeyValuePair<string[], string[]>> sources)
        {
            Func<string, string, string> ask = Ask;
            if (ask == null || sources == null || sources.Count == 0) return 0;
            List<string> sets = new List<string>();
            StringBuilder words = new StringBuilder();
            words.Append("The character: ").Append(Clean(character) ?? personality).Append('\n');
            words.Append("Their mood today: ").Append(mood).Append(".\n");
            words.Append("How they feel about the player: ").Append(Fond[Math.Max(0, Math.Min(Fond.Length - 1, Step()))]).Append(" About ").Append(Percent()).Append(" out of 100.");
            words.Append(" Whatever their mood, it shows that they care.").Append(Brain.SenpaiLive ? "" : " Keep it warm and wholesome.").Append("\n\n");
            words.Append("For each situation below, write ").Append(Each).Append(" new short lines this character would say as is, in their own voice coloured by this mood. ");
            words.Append("Keep any word in percent signs, like %petname%, exactly as it is, and use only the ones shown. One or two sentences each, no quotes, no emoji, no stage directions, all of it wholesome.\n");
            foreach (KeyValuePair<string, KeyValuePair<string[], string[]>> source in sources)
            {
                if (sets.Count >= MostSets) break;
                List<string> own = Sample(source.Value.Key), moody = Sample(source.Value.Value);
                if (own.Count == 0 && moody.Count == 0) continue;
                sets.Add(source.Key);
                words.Append("\nSituation: ").Append(source.Key).Append('\n');
                if (own.Count > 0) words.Append("Usually they say: ").Append(string.Join(" | ", own.ToArray())).Append('\n');
                if (moody.Count > 0) words.Append("In this mood they say: ").Append(string.Join(" | ", moody.ToArray())).Append('\n');
            }
            if (sets.Count == 0) return 0;
            words.Append("\nReply with only a JSON object and nothing else: each situation's name as a key, and an array of its lines as the value.\n");
            string reply = null;
            try { reply = ask("You write short dialogue lines for a companion character in a cozy desktop role-playing game. Reply with only the JSON asked for.", words.ToString()); }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            Dictionary<string, string[]> made = Kept(reply, sets);
            // A line the brain copied from the examples it was shown is already hers, so it is not a blend.
            foreach (string set in new List<string>(made.Keys))
            {
                List<string> fresh = new List<string>();
                foreach (string line in made[set])
                    if (!Shown(line, sources[set].Key) && !Shown(line, sources[set].Value)) fresh.Add(line);
                if (fresh.Count == 0) made.Remove(set); else made[set] = fresh.ToArray();
            }
            int lines = 0;
            foreach (string[] set in made.Values) lines += set.Length;
            if (made.Count == 0 || lines < Fewest) return 0;
            // Her meme lines go into the pool as they are.
            foreach (string set in new List<string>(made.Keys))
            {
                List<string> pool = new List<string>(made[set]);
                foreach (string own in sources[set].Key) if (Sayings.IsMeme(own) && !pool.Contains(own)) pool.Add(own);
                made[set] = pool.ToArray();
            }
            StringBuilder file = new StringBuilder();
            file.Append("# ").Append(personality).Append(" when ").Append(mood).Append('\n').Append('\n');
            file.Append(Notes.NoteMark).Append(" Lines her brain wrote for this pair. Edit or delete any; delete the file to have them written again.\n");
            foreach (KeyValuePair<string, string[]> set in made)
            {
                file.Append('\n').Append("## ").Append(set.Key).Append('\n').Append('\n');
                foreach (string line in set.Value) file.Append(line).Append('\n');
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            Notes.Put(path, file.ToString().Replace("\n", Environment.NewLine));
            return made.Count;
        }

        private static bool Shown(string line, string[] lines)
        {
            if (lines == null) return false;
            foreach (string one in lines)
                if (string.Equals(Keyboard(Clean(one) ?? ""), line, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // A few of their lines to show the brain: none gated, none a meme, tokens as tokens.
        internal static List<string> Sample(string[] lines)
        {
            List<string> picked = new List<string>();
            if (lines == null) return picked;
            foreach (string line in lines)
            {
                string one = Clean(line);
                if (one == null || Sayings.IsMeme(one) || Bad(one)) continue;
                picked.Add(one);
                if (picked.Count >= 4) break;
            }
            return picked;
        }

        private static string Clean(string text)
        {
            string one = Regex.Replace(text ?? "", @"\s+", " ").Trim();
            return one.Length == 0 ? null : one;
        }

        // Gated, or with a percent sign that is not one of her tokens: never sent, never kept, on either side. The
        // brain only writes the line around her tokens; their words are filled in here as she says it.
        private static bool Bad(string line)
        {
            Func<string, bool> gated = Gated;
            if (gated != null && gated(line)) return true;
            string rest = line;
            foreach (string token in Tokens) rest = rest.Replace(token, "");
            return rest.IndexOf('%') >= 0;
        }

        // Two sentences run together with nothing between them: an order ("sit down", "go on then") running straight on into
        // a new one ("i've been busy", "show me"). Her brain writes these; they read as one garbled line.
        private static readonly Regex RunOn = new Regex(@"^\W*(?:sit down|come here|come on|go on|hold on|calm down|look|listen|wait|stop|relax|chill|hush|shush)(?:\s+then)?\s+(?:i|i'm|i've|i'd|i'll|you're|you've|we|we're|it's|that's|they|show|tell|give|let|do)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // A question or a clause running on into the next with no stop: "Are we playing now this is the only acceptable time."
        private static readonly Regex RunOnClause = new Regex(@"\b(?:now|then|today|again|here)\s+(?:this|that|it|there)\s+(?:is|was|'s)\b", RegexOptions.IgnoreCase);

        internal static bool RunsOn(string line) { return line != null && (RunOn.IsMatch(line) || RunOnClause.IsMatch(line)); }

        // The lines of a reply that were asked for, read from its ```json block (or the bare object), with labels,
        // quotes and lead-ins taken off. A line too long, refused, gated or with a token lost or made up is left out.
        internal static Dictionary<string, string[]> Kept(string reply, List<string> asked)
        {
            Dictionary<string, string[]> kept = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(reply)) return kept;
            Match fenced = Regex.Match(reply, @"```(?:json)?\s*(\{[\s\S]*?\})\s*```", RegexOptions.IgnoreCase);
            string whole = reply.Trim(), body = fenced.Success ? fenced.Groups[1].Value : whole.StartsWith("{", StringComparison.Ordinal) && whole.EndsWith("}", StringComparison.Ordinal) ? whole : null;
            if (body == null) return kept;
            foreach (Match situation in Regex.Matches(body, @"""((?:[^""\\]|\\.)*)""\s*:\s*\[([\s\S]*?)\]"))
            {
                string named = asked.Find(one => string.Equals(one, Unescape(situation.Groups[1].Value), StringComparison.OrdinalIgnoreCase));
                if (named == null) continue;
                List<string> good = new List<string>();
                foreach (Match item in Regex.Matches(situation.Groups[2].Value, @"""((?:[^""\\]|\\.)*)"""))
                {
                    string line = Tidy(Unescape(item.Groups[1].Value));
                    if (line == null || line.Length > Longest || Refusal.IsMatch(line) || Bad(line) || RunsOn(line) || good.Contains(line)) continue;
                    good.Add(line);
                }
                if (good.Count > 0) kept[named] = good.ToArray();
            }
            return kept;
        }

        // A line as she would say it: no "Capri:" or "Dominant (sad):" label, no "She could say:" lead-in, no quote marks
        // round it. Null when nothing is left.
        private static string Tidy(string line)
        {
            string one = Clean(line);
            if (one == null) return null;
            one = Regex.Replace(one, @"^(?:she|they|he|the character|capri|[A-Z][\w ]{0,20})\s*(?:\([^)]*\))?\s*(?:could|might|would|may)?\s*(?:say|says)?\s*:\s*", "", RegexOptions.IgnoreCase);
            one = one.Trim().Trim('"', '\u201c', '\u201d', '\'', '\u2018', '\u2019').Trim();
            one = Keyboard(one);
            return one.Length == 0 ? null : one;
        }

        // Plain keyboard text, as she says it: curly quotes straight, an ellipsis character three dots, a dash a comma (or a
        // full stop at the end), a semicolon a full stop with a capital after it.
        internal static string Keyboard(string line)
        {
            string one = (line ?? "").Replace('\u2018', '\'').Replace('\u2019', '\'').Replace('\u201c', '"').Replace('\u201d', '"').Replace("\u2026", "...");
            one = Regex.Replace(one, @"\s*[\u2014\u2013]\s*$", ".");
            one = Regex.Replace(one, @"\s*[\u2014\u2013]\s*", ", ");
            one = Regex.Replace(one, @"\s+--\s+", ", ");
            one = Regex.Replace(one, @"\s*;\s*(\S)", delegate(Match next) { return ". " + char.ToUpperInvariant(next.Groups[1].Value[0]); });
            one = Regex.Replace(one, @"\s*;\s*$", ".");
            one = Regex.Replace(one, @",\s*([.,!?])", "$1");
            return Regex.Replace(one, @"\s{2,}", " ").Trim();
        }

        private static string Unescape(string json)
        {
            return Regex.Replace(json, @"\\(u[0-9a-fA-F]{4}|.)", delegate(Match escape)
            {
                string one = escape.Groups[1].Value;
                if (one.Length == 5) return ((char)Convert.ToInt32(one.Substring(1), 16)).ToString();
                return one == "n" ? " " : one == "t" ? " " : one;
            });
        }

        // "## situation" headings and the "- line" (or plain) lines under them.
        private static Dictionary<string, string[]> Parse(string text)
        {
            Dictionary<string, List<string>> found = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            string under = null;
            foreach (string raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string one = raw.Trim();
                if (one.StartsWith("#", StringComparison.Ordinal))
                {
                    under = one.StartsWith("##", StringComparison.Ordinal) ? one.TrimStart('#').Trim() : null;
                    if (under != null && !found.ContainsKey(under)) found[under] = new List<string>();
                    continue;
                }
                if (under == null || one.Length == 0 || one.StartsWith(">", StringComparison.Ordinal)) continue;
                if (one.StartsWith("- ", StringComparison.Ordinal)) one = one.Substring(2).Trim();
                if (one.Length > 0) found[under].Add(one);
            }
            Dictionary<string, string[]> done = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, List<string>> set in found) if (set.Value.Count > 0) done[set.Key] = set.Value.ToArray();
            return done;
        }

        // A stand-in brain: a blend made, kept and used, asked once; one with a lost token, a gated word or a refusal not
        // kept; a hand-written pairing in the mood file beating the blend.
#if CHECKS
        internal static void Check(List<string> checks, string folder)
        {
            Func<string> folderWas = Folder;
            Func<string, string, string> askWas = Ask;
            Func<bool> readyWas = Ready;
            Func<string, bool> gatedWas = Gated;
            Func<KeyValuePair<int, int>> feelingWas = Feeling;
            string root = Path.Combine(folder, "blends-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            int asks = 0;
            string sent = null;
            try
            {
                Folder = delegate { return root; };
                Feeling = delegate { return new KeyValuePair<int, int>(10, 60); };
                Ready = delegate { return true; };
                Gated = delegate(string text) { return text.IndexOf("zorbqword", StringComparison.OrdinalIgnoreCase) >= 0; };
                Ask = delegate(string system, string words)
                {
                    asks++;
                    sent = system + "\n" + words;
                    return "Sure! Here are the lines.\n```json\n{ \"petted\": [\"Dominant (sad): I'm meant to be the one looking after you, %petname%.\", \"Fine. Pet me. %petnam% will cope.\", \"She could say: \\\"Don't stop. Not yet.\\\"\", \"this has zorbqword in it\", \"Just this once.\", \"Sigh. Fine.\", \"Don\u2019t stop yet; I\u2019m asking \u2014 and that\u2019s an order\u2026\"],\n  \"thrown\": [\"I'm sorry, but I can't write that.\"] }\n```\nHope that helps!";
                };
                Dictionary<string, KeyValuePair<string[], string[]>> sources = new Dictionary<string, KeyValuePair<string[], string[]>>(StringComparer.OrdinalIgnoreCase)
                {
                    { "petted", new KeyValuePair<string[], string[]>(new[] { "Good %boy%. Now me.", "~no u", "zorbqword line" }, new[] { "Sigh. Fine." }) },
                    { "thrown", new KeyValuePair<string[], string[]>(new[] { "Put me down!" }, new[] { "Whatever." }) },
                };
                string path = PathFor("Dominant", "Melancholy");
                int made = Make(path, "Dominant", "Melancholy", "Bossy and protective.", sources);
                string[] petted = Lines("Dominant", "Melancholy", "petted");
                if (made != 1 || petted == null || petted.Length != 5 || Array.IndexOf(petted, "Don't stop yet. I'm asking, and that's an order...") < 0 || petted[0] != "I'm meant to be the one looking after you, %petname%." || Array.IndexOf(petted, "Don't stop. Not yet.") < 0 || Array.IndexOf(petted, "Just this once.") < 0 || Array.IndexOf(petted, "~no u") < 0)
                    throw new Exception("A mood blend wasn't made and kept as it should be: " + made + " / " + (petted == null ? "none" : string.Join(" | ", petted)));
                if (Lines("Dominant", "Melancholy", "thrown") != null) throw new Exception("A refused reaction was kept.");
                if (sent.Contains("zorbqword") || sent.Contains("Honey") || !sent.Contains("%boy%") || !sent.Contains("role-playing game") || !sent.Contains("out of 100")) throw new Exception("The brain was sent a gated word or a pet name, or not the role-play framing: " + sent);
                if (Step() != 3 || !path.EndsWith("fond 3.md", StringComparison.Ordinal) || !sent.Contains("devoted to the player") || !sent.Contains("it shows that they care"))
                    throw new Exception("How fond she is wasn't told to the brain or kept in the blend's name: " + path);
                Ready = delegate { return false; };
                Feeling = delegate { return new KeyValuePair<int, int>(0, -60); };
                if (Step() != 0 || Lines("Dominant", "Melancholy", "petted") != null) throw new Exception("A blend made at one fondness was used at another.");
                Feeling = delegate { return new KeyValuePair<int, int>(10, 60); };
                // Asked once: a pair with its file is never asked again, and a pair tried this session is not retried.
                Start("Dominant", "Melancholy");
                Ask = delegate { asks++; return "As an AI language model I won't."; };
                if (Make(PathFor("Bratty", "Sleepy"), "Bratty", "Sleepy", null, sources) != 0 || File.Exists(PathFor("Bratty", "Sleepy"))) throw new Exception("A refusing reply was kept.");
                Ask = delegate { asks++; return "```json\n{\"petted\": [\"One good line.\", \"Another good one.\"]}\n```"; };
                if (Make(PathFor("Bratty", "Hyper"), "Bratty", "Hyper", null, sources) != 0) throw new Exception("A reply with fewer than three good lines was kept.");
                Ask = delegate { asks++; return "{\"petted\": [\"Plain object one.\", \"Plain object two.\", \"Plain object three.\"]} trailing words"; };
                if (Make(PathFor("Bratty", "Moody"), "Bratty", "Moody", null, sources) != 0) throw new Exception("A reply with no fenced block and words outside its object was read.");
                if (asks != 4) throw new Exception("The brain was asked " + asks + " times, not once a pair.");
                // Two sentences run together aren't kept, and one already in a blend file isn't said.
                if (!RunsOn("Sit down i've been busy being feral") || !RunsOn("Go on then show me what chu got") || RunsOn("Sit down, I've been busy.") || RunsOn("Look at you go"))
                    throw new Exception("Two sentences run together weren't told from one line.");
                Ask = delegate { asks++; return "```json\n{\"missed you\": [\"Sit down i've been busy being feral\", \"Good, I was getting ideas.\", \"Where were you, my time was insane.\", \"I missed the chaos.\"]}\n```"; };
                Dictionary<string, KeyValuePair<string[], string[]>> missed = new Dictionary<string, KeyValuePair<string[], string[]>>(StringComparer.OrdinalIgnoreCase) { { "missed you", new KeyValuePair<string[], string[]>(new[] { "Sit down, I've been busy." }, new[] { "Back already?" }) } };
                int keptMissed = Make(PathFor("Emo", "Mischievous"), "Emo", "Mischievous", null, missed);
                string[] missedLines = Lines("Emo", "Mischievous", "missed you");
                File.AppendAllText(PathFor("Emo", "Mischievous"), "Come here i want to see you\n");
                lock (gate) read.Clear();
                string[] missedAgain = Lines("Emo", "Mischievous", "missed you");
                if (keptMissed != 1 || missedLines == null || missedLines.Length != 3 || Array.Exists(missedLines, RunsOn) || missedAgain == null || Array.Exists(missedAgain, RunsOn))
                    throw new Exception("A run-on blend line was kept or said: " + (missedLines == null ? "none" : string.Join(" | ", missedLines)) + " / " + (missedAgain == null ? "none" : string.Join(" | ", missedAgain)));
                checks.Add("PASS a mood blend is asked for as role-play lines in a JSON block with tokens as tokens and nothing gated or named, read only inside the block with labels, quotes and lead-ins taken off, kept per personality and mood with her memes and never asked twice; a lost token, a gated word, a refusal, chatter outside the block or fewer than three good lines is not kept, and two sentences run together (\"Sit down i've been busy being feral\") are neither kept nor said from a blend file");
            }
            finally
            {
                Folder = folderWas; Ask = askWas; Ready = readyWas; Gated = gatedWas; Feeling = feelingWas;
                lock (gate) { read.Clear(); tried.Clear(); }
                try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
            NoBrainCheck(checks);
        }
#endif

        // With no blend: the mood's lines and their personality's in one pool at mood share, the same pool each time, and a
        // mood's openers and tails never taken for a reaction to blend.
#if CHECKS
        private static void NoBrainCheck(List<string> checks)
        {
            string[] mood = { "m1", "m2" }, own = { "o1", "o2", "o3", "o4", "o5", "o6" };
            foreach (int share in new[] { 25, 50, 75 })
            {
                string[] pool = Personalities.Mixed(mood, own, share);
                int moods = 0;
                foreach (string one in pool) if (one.StartsWith("m", StringComparison.Ordinal)) moods++;
                double got = (double)moods / pool.Length * 100;
                if (Math.Abs(got - share) > 5 || string.Join("|", pool) != string.Join("|", Personalities.Mixed(mood, own, share)))
                    throw new Exception("A mixed pool at share " + share + " came out " + got.ToString("0") + " in a hundred mood lines, or changed between two calls.");
            }
            if (Personalities.Mixed(mood, own, 0) != own || Personalities.Mixed(mood, own, 100) != mood || Personalities.Mixed(mood, null, 10) != mood)
                throw new Exception("Mood share 0 or 100, or a personality with no lines, did not give one side's lines alone.");
            if (!Personalities.IsPart("openers") || !Personalities.IsPart("Tails") || Personalities.IsPart("When you pet her"))
                throw new Exception("A mood's openers and tails are not told apart from its reactions.");
            if (Personalities.MoodPart(Personalities.OpenersHeading, null) != null)
                throw new Exception("A mood opener came with nothing to pick it.");
            checks.Add("PASS with no blend, a mood's lines and their personality's share one pool at mood share (25, 50 and 75 in a hundred), the same pool each call; share 0 or 100 gives one side alone; a mood's openers and tails are pieces, not reactions");
        }
#endif
    }
}
