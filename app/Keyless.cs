using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CapriDesktop
{
    // Better conversations: her lines grown by role-play style asks to free keyless services in turn, then her own brain.
    // Off until turned on. Only her personality, mood, wanting and the moment are sent; on the switch's side only her own
    // brain is asked.
    internal static class Keyless
    {
        // Its plug-in on the Mods page, laid in off with her other starters: the same setting as its Toggles row.
        internal const string PluginId = "conversations";

        internal sealed class Engine
        {
            internal string Name, Short, Address, Prefer, Key;
            internal bool Agent;
            internal string Model;
            internal int Fails;
            internal DateTime WaitUntil;
            // Its budget, a little under what its provider publishes (0 is no limit), and the asks sent inside the last day.
            internal int PerMinute, PerHour, PerDay;
            internal readonly List<DateTime> Sent = new List<DateTime>();
            internal bool LimitSaid;
        }

        // A provider's own "too many": the time it says to wait, or the length of the budget it ran past.
        internal sealed class Limited : Exception
        {
            internal readonly TimeSpan Reset;
            internal Limited(TimeSpan reset) : base("hit its rate limit") { Reset = reset; }
        }

        // Asked in this order. Each lists its models at /v1/models; the one used is picked by
        // Prefer from that list, so a catalogue that changes never breaks it.
        internal static readonly Engine[] Engines =
        {
            // Published: 2 a minute for each address and model without a key (OVHcloud's AI Endpoints capabilities page).
            new Engine { Name = "OVHcloud AI Endpoints", Short = "OVH", Address = "https://oai.endpoints.kepler.ai.cloud.ovh.net", Prefer = @"llama[-_.]?3[-_.]3.*70b", PerMinute = 1 },
            // Published: 200 an hour for each address on free models (Kilo's Models & Providers page); 200 a day is listed elsewhere.
            new Engine { Name = "Kilo Gateway", Short = "KILO", Address = "https://ai-gateway.kilo.ai/api", Prefer = @"^kilo-auto/free$|:free$", PerMinute = 15, PerHour = 180, PerDay = 180 },
            // Published: 2 a second and 90 a minute for each address on its submit routes (AI Horde's service limits).
            new Engine { Name = "AI Horde", Short = "HORDE", Address = "https://oai.aihorde.net", Prefer = @".", Key = "0000000000", Agent = true, PerMinute = 60 }
        };

        // The switch, her own brain as the last to ask, and how they are reached. Set by her window; checks swap them.
        internal static Func<bool> On = delegate { return false; };
        internal static Func<string, string, string> OwnBrain;
        internal static Func<Engine, string, string, string> Send = Post;
        internal static Func<DateTime> Now = delegate { return DateTime.UtcNow; };
        // What the Toggles side shows: OK, or the first one resting after a failure, by a short name so it fits its box at any
        // size; the whole of it is in the box's tip.
        internal static string Status
        {
            get
            {
                foreach (Engine one in Engines) if (Now() < one.WaitUntil) return "[" + one.Short + " RESTING]";
                return "[STATUS:OK]";
            }
        }

#if CHECKS
        // Every engine's resting status and the all-clear fit the Toggles status box (170 wide, minus its frame) at 100 and
        // 150%, in the box's own font; the whole sentence is its tip.
        internal static void StatusCheck(List<string> checks)
        {
            Func<DateTime> nowWas = Now;
            DateTime[] waitWas = Array.ConvertAll(Engines, one => one.WaitUntil);
            List<string> wrong = new List<string>();
            try
            {
                Now = delegate { return new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc); };
                List<string> shown = new List<string> { Status };
                for (int i = Engines.Length - 1; i >= 0; i--) { Engines[i].WaitUntil = Now().AddMinutes(5); shown.Add(Status + "|" + StatusTip); }
                foreach (float zoom in new[] { 1f, 1.5f })
                    using (System.Drawing.Font font = Neon.Type(9.5f * zoom))
                        foreach (string one in shown)
                        {
                            string text = one.Split('|')[0];
                            int wide = System.Windows.Forms.TextRenderer.MeasureText(text, font).Width;
                            if (wide > (int)(162 * zoom)) wrong.Add(text + " is " + wide + " px at " + zoom * 100 + "%");
                            if (one.IndexOf('|') > 0 && one.IndexOf(" min", StringComparison.Ordinal) < 0) wrong.Add("no tip for " + text);
                        }
            }
            finally { Now = nowWas; for (int i = 0; i < Engines.Length; i++) Engines[i].WaitUntil = waitWas[i]; }
            if (wrong.Count > 0) throw new Exception("A Better conversations status does not fit its box: " + string.Join(" | ", wrong.ToArray()));
            checks.Add("PASS every Better conversations status fits its box at 100 and 150%, a resting service by its short name, the whole sentence in the box's tip");
        }
#endif

#if CHECKS
        // A burst of asks over three simulated hours never sends past any engine's budget in any minute, hour or day; a "too
        // many" that still comes back rests that engine until its reset, said once, and it is not asked before then.
        internal static void BudgetCheck(List<string> checks)
        {
            Func<Engine, string, string, string> sendWas = Send;
            Func<DateTime> nowWas = Now;
            Func<string, string, string> ownWas = OwnBrain;
            DateTime at = new DateTime(2026, 11, 27, 0, 0, 0, DateTimeKind.Utc);
            Dictionary<Engine, List<DateTime>> sent = new Dictionary<Engine, List<DateTime>>();
            List<string> wrong = new List<string>();
            try
            {
                foreach (Engine one in Engines) { one.Sent.Clear(); one.Fails = 0; one.WaitUntil = DateTime.MinValue; one.LimitSaid = false; sent[one] = new List<DateTime>(); }
                Now = delegate { return at; };
                OwnBrain = null;
                Engine refusing = null;
                DateTime refusedAt = DateTime.MinValue;
                Send = delegate(Engine one, string system, string words)
                {
                    sent[one].Add(at);
                    if (one == refusing && refusedAt == DateTime.MinValue) { refusedAt = at; throw new Limited(TimeSpan.FromMinutes(30)); }
                    return "{\"ok\": [\"fine\"]}";
                };
                // A burst: an ask every half second for three hours, the first engine with room taking each.
                for (int i = 0; i < 21600; i++)
                {
                    at = at.AddMilliseconds(500);
                    if (i == 2000) refusing = Engines[2];
                    string who;
                    Ask("words", out who);
                }
                foreach (KeyValuePair<Engine, List<DateTime>> one in sent)
                {
                    List<DateTime> times = one.Value;
                    for (int i = 0; i < times.Count; i++)
                    {
                        Func<TimeSpan, int> back = span => times.FindAll(t => t <= times[i] && times[i] - t < span).Count;
                        if (one.Key.PerMinute > 0 && back(TimeSpan.FromMinutes(1)) > one.Key.PerMinute) { wrong.Add(one.Key.Short + " past its minute budget"); break; }
                        if (one.Key.PerHour > 0 && back(TimeSpan.FromHours(1)) > one.Key.PerHour) { wrong.Add(one.Key.Short + " past its hour budget"); break; }
                        if (one.Key.PerDay > 0 && back(TimeSpan.FromDays(1)) > one.Key.PerDay) { wrong.Add(one.Key.Short + " past its day budget"); break; }
                    }
                }
                List<DateTime> horde = sent[Engines[2]];
                int during = horde.FindAll(t => t > refusedAt && t < refusedAt.AddMinutes(30)).Count;
                if (refusedAt == DateTime.MinValue || during != 0) wrong.Add("the refused engine was asked " + during + " times before its reset");
                if (wrong.Count > 0) throw new Exception("The free engines' budgets went wrong: " + string.Join(" | ", wrong.ToArray()));
                checks.Add("PASS a burst of asks over three hours stays inside every free engine's budget (OVH " + sent[Engines[0]].Count + ", Kilo " + sent[Engines[1]].Count + ", Horde " + horde.Count + " sent), and a \"too many\" rests that engine until its reset, not asked before");
            }
            finally
            {
                Send = sendWas; Now = nowWas; OwnBrain = ownWas;
                foreach (Engine one in Engines) { one.Sent.Clear(); one.Fails = 0; one.WaitUntil = DateTime.MinValue; one.LimitSaid = false; }
            }
        }
#endif

        internal static string StatusTip
        {
            get
            {
                foreach (Engine one in Engines)
                    if (Now() < one.WaitUntil) return one.Name + " is resting after a failure, back in " + Math.Max(1, (int)Math.Ceiling((one.WaitUntil - Now()).TotalMinutes)) + " min; the next one is asked meanwhile.";
                return "Every free service is answering.";
            }
        }

        internal const string Instructions = "You write short dialogue lines for a companion character in a cozy desktop role-playing game. Reply with only the JSON asked for.";
        // Her other side's own role-play instruction, from its file: the free ones grow that side's lines too.
        internal static string SenpaiInstructions = "";
        private static string InstructionsNow { get { return Brain.SenpaiLive && SenpaiInstructions.Length > 0 ? SenpaiInstructions : Instructions; } }

        // Each in turn while it isn't resting; on a drop, a refusal or a limit, the next. One that fails rests 1 minute, then
        // 2, 4 and on to an hour, and says so in her log as a PROBLEM line. Null when nobody gave a usable reply.
        internal static string Ask(string words, out string who)
        {
            who = null;
            // Both sides go to the free ones; her other side only with its own instruction.
            if (!Brain.SenpaiLive || SenpaiInstructions.Length > 0)
                foreach (Engine one in Engines)
                {
                    if (Now() < one.WaitUntil || !Slot(one)) continue;
                    string reply = null, why = null;
                    try { reply = Send(one, InstructionsNow, words); }
                    catch (Limited limit) { Rested(one, limit); continue; }
                    catch (Exception bad) { why = bad.Message; }
                    if (Usable(reply)) { one.Fails = 0; one.LimitSaid = false; who = one.Name; return reply; }
                    one.Fails++;
                    one.WaitUntil = Now().AddMinutes(Math.Min(60, Math.Pow(2, Math.Min(6, one.Fails - 1))));
                    Brain.Log("PROBLEM", -1, "Better conversations: " + one.Name + " " + (why ?? (reply == null ? "gave nothing back" : "sent nothing usable")) + "; asking the next.");
                }
            Func<string, string, string> own = OwnBrain;
            if (own == null) return null;
            try
            {
                string reply = own(InstructionsNow, words);
                if (Usable(reply)) { who = "her own brain"; return reply; }
            }
            catch (Exception caughtOne) { Brain.Caught(caughtOne); }
            return null;
        }

        // The free ones in turn on either side, for a line of hers only (smoothing): a role-play ask carries the line and
        // nothing of theirs. Null when none gave a usable reply; a failure rests as above.
        internal static string AskAny(string system, string words)
        {
            foreach (Engine one in Engines)
            {
                if (Now() < one.WaitUntil || !Slot(one)) continue;
                string reply = null;
                try { reply = Send(one, system, words); }
                catch (Limited limit) { Rested(one, limit); continue; }
                catch (Exception caughtOne) { Brain.Caught(caughtOne); }
                if (Usable(reply)) { one.Fails = 0; one.LimitSaid = false; return reply; }
                one.Fails++;
                one.WaitUntil = Now().AddMinutes(Math.Min(60, Math.Pow(2, Math.Min(6, one.Fails - 1))));
            }
            return null;
        }

        // A slot in the engine's budget, taken when one is free: then the ask may go. With none free it waits its turn and the
        // next engine is asked, so an ask is never sent only to come back "too many".
        internal static bool Slot(Engine one)
        {
            DateTime now = Now();
            lock (one.Sent)
            {
                one.Sent.RemoveAll(at => now - at >= TimeSpan.FromDays(1));
                Func<TimeSpan, int> within = span => one.Sent.FindAll(at => now - at < span).Count;
                if (one.PerMinute > 0 && within(TimeSpan.FromMinutes(1)) >= one.PerMinute) return false;
                if (one.PerHour > 0 && within(TimeSpan.FromHours(1)) >= one.PerHour) return false;
                if (one.PerDay > 0 && one.Sent.Count >= one.PerDay) return false;
                one.Sent.Add(now);
                return true;
            }
        }

        // A "too many" that still came back: the engine rests until its reset, said once in her log, not asked every tick.
        private static void Rested(Engine one, Limited limit)
        {
            one.WaitUntil = Now() + limit.Reset;
            if (one.LimitSaid) return;
            one.LimitSaid = true;
            Brain.Log("PROBLEM", -1, "Better conversations: " + one.Name + " hit its rate limit; resting until " + one.WaitUntil.ToLocalTime().ToString("HH:mm") + ".");
        }

        // The reset a provider's 429 names, or the length of the budget it ran past.
        private static TimeSpan ResetOf(Engine one, WebResponse response)
        {
            int seconds;
            string after = response == null ? null : response.Headers["Retry-After"];
            if (after != null && int.TryParse(after.Trim(), out seconds) && seconds > 0) return TimeSpan.FromSeconds(Math.Min(seconds, 86400));
            return one.PerDay > 0 && one.Sent.Count >= one.PerDay ? TimeSpan.FromHours(24) : one.PerHour > 0 ? TimeSpan.FromHours(1) : TimeSpan.FromMinutes(1);
        }

        private static bool Usable(string reply) { return !string.IsNullOrWhiteSpace(reply) && reply.IndexOf('{') >= 0; }

        // The model it lists that matches what is wanted from it, or its first.
        internal static string Pick(string[] models, string prefer)
        {
            if (models == null || models.Length == 0) return null;
            foreach (string one in models) if (Regex.IsMatch(one ?? "", prefer, RegexOptions.IgnoreCase)) return one;
            return models[0];
        }

        private static string Post(Engine one, string system, string words)
        {
            if (one.Model == null) one.Model = Pick(Brain.ServerModels(one.Address, one.Key), one.Prefer);
            if (one.Model == null) throw new Exception("lists no model to use");
            CompletionRequest body = new CompletionRequest
            {
                Model = one.Model,
                Messages = new[] { new ChatMessage("system", system), new ChatMessage("user", words) },
                Limit = 900,
                Temperature = 0.9
            };
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(one.Address + "/v1/chat/completions");
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Timeout = request.ReadWriteTimeout = 60000;
            request.AllowAutoRedirect = false;
            request.UserAgent = "Capri";
            if (one.Key != null) request.Headers["Authorization"] = "Bearer " + one.Key;
            // AI Horde asks each app to name itself; the name and where it comes from, nothing of theirs.
            if (one.Agent) request.Headers["Client-Agent"] = "Capri:1:github.com/YunoTheAlien";
            using (Stream into = request.GetRequestStream()) new DataContractJsonSerializer(typeof(CompletionRequest)).WriteObject(into, body);
            WebResponse answered;
            try { answered = request.GetResponse(); }
            catch (WebException failed)
            {
                HttpWebResponse said = failed.Response as HttpWebResponse;
                if (said != null && (int)said.StatusCode == 429) throw new Limited(ResetOf(one, said));
                throw;
            }
            using (WebResponse response = answered)
            using (Stream back = response.GetResponseStream())
            {
                CompletionReply reply = (CompletionReply)new DataContractJsonSerializer(typeof(CompletionReply)).ReadObject(back);
                return reply != null && reply.Choices != null && reply.Choices.Length > 0 && reply.Choices[0].Message != null ? reply.Choices[0].Message.Content : null;
            }
        }

        // What is asked: the reaction's name, her personality's name, her mood, how much she wants them, and a few of the
        // reaction's own lines with tokens as tokens. Nothing else of hers or theirs goes in.
        internal static string Words(string personality, string mood, int band, string set, string[] lines, int many = 8)
        {
            StringBuilder words = new StringBuilder();
            words.Append("The character: ").Append(personality).Append(".\n");
            words.Append("Their mood: ").Append(string.IsNullOrEmpty(mood) ? "calm" : mood).Append(".\n");
            words.Append("How much they want the player's attention: ").Append(band * 2).Append(" out of 10.\n\n");
            words.Append("For the situation below, write ").Append(many).Append(" new short lines this character would say, in their own voice coloured by this mood. ");
            words.Append("Keep any word in percent signs, like %petname%, exactly as it is, and use only the ones shown. One or two sentences each, no quotes, no emoji, no stage directions").Append(Brain.SenpaiLive ? ".\n" : ", all of it wholesome.\n");
            words.Append("\nSituation: ").Append(set).Append('\n');
            List<string> shown = Blends.Sample(lines);
            if (shown.Count > 0) words.Append("Usually they say: ").Append(string.Join(" | ", shown.ToArray())).Append('\n');
            words.Append("\nReply with only a JSON object and nothing else: the situation's name as the key, and an array of its lines as the value.\n");
            return words.ToString();
        }

        // Her lines for a reaction: hers, then once hers have gone round, the grown ones too. Thin ones ask for more, one ask a
        // minute, the most used pair first; how thin, how many and how often follow Growth.cs.
        private static readonly object gate = new object();
        private static readonly Dictionary<string, int> used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> asked = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Action> waiting = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, double> waitingShare = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private static DateTime lastAsk = DateTime.MinValue;
        private static bool asking;

        internal static string[] Grown(string personality, string mood, string set, string[] hers)
        {
            if (hers == null || hers.Length == 0 || string.IsNullOrEmpty(set) || !On()) return hers;
            int band = Expansion.Band(Blends.Percent());
            string key = personality + "|" + mood + "|" + band + "|" + set;
            int times;
            lock (gate) { used.TryGetValue(key, out times); used[key] = ++times; stockOf[set] = hers; lastUse = Now(); }
            if (times <= hers.Length) return hers;
            string[] grown = Expansion.Lines(personality, mood, band, set);
            if (grown.Length < Growth.Thin(personality, mood)) Start(personality, mood, band, set, hers);
            Expansion.TidyWhenDue();
            if (grown.Length == 0) return hers;
            List<string> both = new List<string>(hers);
            foreach (string one in grown) if (!both.Contains(one)) both.Add(one);
            return both.ToArray();
        }

        // Idle top-ups: while she is idle, one ask at most every 10 minutes, mostly for the pairs and moments in use and,
        // slower, for those near them: her drift-joined personalities, the moods hers sours into, the bands either side.
        private static readonly Dictionary<string, string[]> stockOf = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        private static DateTime lastUse = DateTime.MinValue, lastIdle = DateTime.MinValue;
        internal static readonly TimeSpan IdleAfter = TimeSpan.FromMinutes(10), IdleEvery = TimeSpan.FromMinutes(10);
        // Set by her window: no game, call or job going. Nothing said for 10 minutes is counted here.
        internal static Func<bool> Free = delegate { return false; };
        internal static Func<string, List<string>> Near = Personalities.DriftsInto;
        internal static Func<List<string>> NearMoods = Personalities.SoursInto;

        internal static string IdleTopUp(string personality, string mood)
        {
            if (!On() || !Free() || string.IsNullOrEmpty(personality)) return null;
            lock (gate) if (Now() - lastUse < IdleAfter || Now() - lastIdle < IdleEvery || asking) return null;
            string key = IdlePick(personality, mood, Expansion.Band(Blends.Percent()));
            if (key == null) return null;
            string[] parts = key.Split('|');
            string[] hers;
            lock (gate) { lastIdle = Now(); stockOf.TryGetValue(parts[3], out hers); }
            Start(parts[0], parts[1], int.Parse(parts[2]), parts[3], hers ?? new string[0]);
            return key;
        }

        // A weighted pick among the thin ones still under their asks: weight is how much it was used, plus one, over the
        // square of one plus how far it is (another personality or mood one each, a band one each step).
        internal static string IdlePick(string personality, string mood, int band)
        {
            List<KeyValuePair<string, int>> people = new List<KeyValuePair<string, int>> { new KeyValuePair<string, int>(personality, 0) };
            foreach (string one in Near(personality) ?? new List<string>()) if (!people.Exists(p => string.Equals(p.Key, one, StringComparison.OrdinalIgnoreCase))) people.Add(new KeyValuePair<string, int>(one, 1));
            List<KeyValuePair<string, int>> moods = new List<KeyValuePair<string, int>> { new KeyValuePair<string, int>(mood ?? "", 0) };
            foreach (string one in NearMoods() ?? new List<string>()) if (!moods.Exists(m => string.Equals(m.Key, one, StringComparison.OrdinalIgnoreCase))) moods.Add(new KeyValuePair<string, int>(one, 1));
            List<string> sets;
            lock (gate) sets = new List<string>(stockOf.Keys);
            List<KeyValuePair<string, double>> weighed = new List<KeyValuePair<string, double>>();
            double total = 0;
            foreach (KeyValuePair<string, int> who in people)
                foreach (KeyValuePair<string, int> feel in moods)
                    for (int b = 1; b <= 5; b++)
                        foreach (string set in sets)
                        {
                            string key = who.Key + "|" + feel.Key + "|" + b + "|" + set;
                            int uses, times;
                            lock (gate) { used.TryGetValue(key, out uses); asked.TryGetValue(key, out times); }
                            if (times >= Growth.Asks(who.Key, feel.Key) || Expansion.Lines(who.Key, feel.Key, b, set).Length >= Growth.Thin(who.Key, feel.Key)) continue;
                            int far = who.Value + feel.Value + Math.Abs(b - band);
                            double weight = (1 + uses) / Math.Pow(1 + far, 2);
                            weighed.Add(new KeyValuePair<string, double>(key, weight));
                            total += weight;
                        }
            if (weighed.Count == 0) return null;
            double roll;
            lock (gate) roll = idleDice.NextDouble() * total;
            foreach (KeyValuePair<string, double> one in weighed) { roll -= one.Value; if (roll <= 0) return one.Key; }
            return weighed[weighed.Count - 1].Key;
        }

        internal static Random idleDice = new Random();

        private static void Start(string personality, string mood, int band, string set, string[] hers)
        {
            string key = personality + "|" + mood + "|" + band + "|" + set;
            double share = Growth.Share(personality, mood);
            int times, most = Growth.Asks(personality, mood), many = Growth.Lines(personality, mood);
            Action ask = delegate
            {
                string words = Words(personality, mood, band, set, hers, many);
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try { Grow(personality, mood, band, set, words); }
                    catch (Exception caughtOne) { Brain.Caught(caughtOne); }
                    finally { lock (gate) asking = false; }
                });
            };
            Action next = null;
            lock (gate)
            {
                asked.TryGetValue(key, out times);
                if (times < most) { waiting[key] = ask; waitingShare[key] = share; }
                if (asking || waiting.Count == 0 || Now() - lastAsk < TimeSpan.FromMinutes(1)) return;
                // The most used pair among those due goes first.
                string first = null;
                foreach (KeyValuePair<string, double> one in waitingShare) if (first == null || one.Value > waitingShare[first]) first = one.Key;
                next = waiting[first];
                waiting.Remove(first);
                waitingShare.Remove(first);
                asked.TryGetValue(first, out times);
                asked[first] = times + 1;
                asking = true;
                lastAsk = Now();
            }
            next();
        }

        // One ask and what it kept, written to her expansionLines. How many lines were new.
        internal static int Grow(string personality, string mood, int band, string set, string words)
        {
            string who;
            string reply = Ask(words, out who);
            if (reply == null) return 0;
            string[] lines;
            if (!Blends.Kept(reply, new List<string> { set }).TryGetValue(set, out lines) || lines.Length == 0)
            {
                Brain.Log("PROBLEM", -1, "Better conversations: " + who + " sent no lines that could be kept for " + set + ".");
                return 0;
            }
            return Expansion.Add(personality, mood, band, set, lines);
        }

#if CHECKS
        // With stand-in services, nothing sent: the order, the next on a failure, resting longer each time, her own brain
        // last; what the ask holds; lines kept once, rolled over, read back, and joining hers only when on and gone round.
        internal static void Check(List<string> checks, string folder)
        {
            Func<Engine, string, string, string> sendWas = Send;
            Func<DateTime> nowWas = Now;
            Func<bool> onWas = On;
            Func<string, string, string> ownWas = OwnBrain;
            Func<string> folderWas = Expansion.Folder;
            DateTime clock = new DateTime(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);
            List<string> calls = new List<string>();
            string good = "{\"petted\": [\"Fine, that was nice.\", \"Again, if you must.\", \"Mm. Acceptable work.\"]}";
            try
            {
                Now = delegate { return clock; };
                foreach (Engine one in Engines) { one.Fails = 0; one.WaitUntil = DateTime.MinValue; one.Sent.Clear(); }
                Send = delegate(Engine one, string system, string asked)
                {
                    calls.Add(one.Name);
                    if (one == Engines[0]) throw new WebException("The remote server returned an error: (429) Too Many Requests.");
                    if (one == Engines[1]) return null;
                    return good;
                };
                OwnBrain = delegate { calls.Add("own"); return good; };
                string who;
                if (Ask("x", out who) != good || who != Engines[2].Name || string.Join(",", calls.ToArray()) != Engines[0].Name + "," + Engines[1].Name + "," + Engines[2].Name)
                    throw new Exception("The services weren't asked in turn, the next on a failure: " + string.Join(",", calls.ToArray()));
                if (Engines[0].WaitUntil != clock.AddMinutes(1) || Engines[1].WaitUntil != clock.AddMinutes(1) || !Status.Contains("RESTING"))
                    throw new Exception("A failed service didn't rest a minute, or the status didn't say so.");
                calls.Clear();
                clock = clock.AddSeconds(30);
                Ask("x", out who);
                if (calls.Count != 1 || calls[0] != Engines[2].Name) throw new Exception("A resting service was asked: " + string.Join(",", calls.ToArray()));
                clock = clock.AddMinutes(1);
                calls.Clear();
                Ask("x", out who);
                if (Engines[0].WaitUntil != clock.AddMinutes(2)) throw new Exception("A service failing again didn't rest longer.");
                Send = delegate(Engine one, string system, string asked) { calls.Add(one.Name); return "I can't help with that."; };
                calls.Clear();
                foreach (Engine one in Engines) { one.WaitUntil = DateTime.MinValue; one.Sent.Clear(); }
                if (Ask("x", out who) != good || who != "her own brain" || calls.Count != 4) throw new Exception("Her own brain wasn't asked last when every service failed.");
                foreach (Engine one in Engines) { one.Fails = 0; one.WaitUntil = DateTime.MinValue; one.Sent.Clear(); }
                if (Status != "[STATUS:OK]") throw new Exception("The status didn't go back to OK once nothing was resting.");

                // The ask: the names, the band and tokens as tokens; a gated or odd line never goes.
                string words = Words("Sassy", "Playful", 3, "petted", new[] { "Hi, %petname%.", "Thanks, %name%.", "Nice one." });
                if (!words.Contains("Sassy") || !words.Contains("Playful") || !words.Contains("6 out of 10") || !words.Contains("%petname%") || !words.Contains("Situation: petted"))
                    throw new Exception("The ask is missing what it should hold.");
                if (Pick(new[] { "mistral-7b", "Meta-Llama-3_3-70B-Instruct" }, Engines[0].Prefer) != "Meta-Llama-3_3-70B-Instruct" || Pick(new[] { "x/y", "z:free" }, Engines[1].Prefer) != "z:free" || Pick(new string[0], ".") != null)
                    throw new Exception("A service's model wasn't picked from its list as wanted.");

                // The files: grown, kept once, read back, rolled over.
                string root = Path.Combine(folder, "expansion-check");
                if (Directory.Exists(root)) Directory.Delete(root, true);
                Expansion.Folder = delegate { return root; };
                if (Expansion.Band(0) != 1 || Expansion.Band(20) != 1 || Expansion.Band(21) != 2 || Expansion.Band(100) != 5) throw new Exception("Wanting isn't in five bands.");
                if (Expansion.Add("Sassy", "Playful", 3, "petted", new[] { "One.", "Two.", "two.", "Three." }) != 3 || Expansion.Add("Sassy", "Playful", 3, "petted", new[] { "One.", "Four." }) != 1)
                    throw new Exception("Grown lines weren't kept once each.");
                string[] back = Expansion.Lines("Sassy", "Playful", 3, "petted");
                if (back.Length != 4 || back[3] != "Four.") throw new Exception("Grown lines didn't read back in their section: " + string.Join("|", back));
                string first = Path.Combine(root, "Sassy Playful wanting 3.txt");
                File.AppendAllText(first, new string('x', Expansion.Roll));
                Expansion.Add("Sassy", "Playful", 3, "petted", new[] { "Five." });
                if (!File.Exists(Path.Combine(root, "Sassy Playful wanting 3_1.txt"))) throw new Exception("A full file didn't roll over into _1.");
                // The weekly cleanup: a line both files have stays in the older one only.
                File.AppendAllText(Path.Combine(root, "Sassy Playful wanting 3_1.txt"), "Two." + Environment.NewLine + "Six." + Environment.NewLine);
                if (Expansion.Tidy(root) != 1 || File.ReadAllText(Path.Combine(root, "Sassy Playful wanting 3_1.txt")).Contains("Two.") || !File.ReadAllText(first).Contains("Two.") || !File.Exists(Path.Combine(root, Expansion.StampFile)))
                    throw new Exception("The cleanup didn't keep a repeated line in the oldest file only, or didn't stamp its time.");

                // Hers first; with the switch off nothing joins.
                On = delegate { return false; };
                string[] hers = { "Mine one.", "Mine two." };
                for (int i = 0; i < 4; i++) if (Grown("Sassy", "Playful", "petted", hers).Length != 2) throw new Exception("Grown lines joined with the switch off.");
                On = delegate { return true; };
                lock (gate) { used.Clear(); asked.Clear(); waiting.Clear(); waitingShare.Clear(); }
                Func<KeyValuePair<int, int>> feelingWas = Blends.Feeling;
                try
                {
                    Blends.Feeling = delegate { return new KeyValuePair<int, int>(5, 0); };
                    int band = Expansion.Band(Blends.Percent());
                    Expansion.Add("Sassy", "Focused", band, "petted", new[] { "Grown one.", "Grown two.", "Grown three." });
                    if (Grown("Sassy", "Focused", "petted", hers).Length != 2 || Grown("Sassy", "Focused", "petted", hers).Length != 2) throw new Exception("Grown lines came before hers had gone round.");
                    if (Grown("Sassy", "Focused", "petted", hers).Length != 5) throw new Exception("Grown lines didn't join hers once hers had gone round.");
                }
                finally { Blends.Feeling = feelingWas; }
                checks.Add("PASS Better conversations with stand-in services: asked in turn, the next on a refusal, a drop or a limit, a failed one resting 1 then 2 minutes and the status saying so, her own brain last; the ask holds only the names, the band and her tokens; grown lines kept once, read back, rolled into _1 at the size, repeats cleaned out of the newer file, and joining hers only with the switch on and once hers have gone round");
                // The weekly meme search: one ask a week, its lines filtered, joining her funny lines after hers have gone round.
                Send = delegate(Engine one, string system, string asked) { return "{\"funny\": [\"Touch grass, choom.\", \"see https://x.com/meme\", \"ok\", \"Skill issue, honestly.\"]}"; };
                foreach (Engine one in Engines) { one.Fails = 0; one.WaitUntil = DateTime.MinValue; one.Sent.Clear(); }
                On = delegate { return true; };
                Func<DateTime> memeClockWas = Expansion.Clock;
                try
                {
                    Expansion.Clock = delegate { return clock; };
                    bool dueFirst = Memes.Due();
                    int memesKept = Memes.Ask();
                    bool dueAfter = Memes.Due();
                    string[] memes = Expansion.LinesNamed(Memes.Name, Memes.Section);
                    Memes.Forget();
                    int early = 0, later = 0;
                    for (int i = 0; i < 3; i++) if (Memes.Next(3) != null) early++;
                    for (int i = 0; i < 60; i++) if (Memes.Next(3) != null) later++;
                    clock = clock.AddDays(8);
                    bool dueWeek = Memes.Due();
                    if (!dueFirst || memesKept != 2 || dueAfter || !dueWeek || memes.Length != 2 || Array.Exists(memes, one => one.Contains("http") || one == "ok") || early != 0 || later == 0)
                        throw new Exception("The weekly meme search went wrong: due " + dueFirst + "/" + dueAfter + "/" + dueWeek + ", kept " + memesKept + " (" + string.Join(" | ", memes) + "), said before hers went round " + early + ", after " + later);
                }
                finally { Expansion.Clock = memeClockWas; Memes.Forget(); }
                // Idle top-ups: mostly what is in use, slower near it, far ones still now and then.
                Func<string, List<string>> nearWas = Near;
                Func<List<string>> nearMoodsWas = NearMoods;
                Random idleDiceWas = idleDice;
                try
                {
                    Near = delegate { return new List<string> { "Bratty" }; };
                    NearMoods = delegate { return new List<string> { "Grumpy" }; };
                    idleDice = new Random(7);
                    lock (gate) { used.Clear(); asked.Clear(); stockOf.Clear(); stockOf["idle one"] = hers; stockOf["idle two"] = hers; used["Sassy|Playful|3|idle one"] = 20; }
                    Dictionary<string, int> picks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < 4000; i++)
                    {
                        string key = IdlePick("Sassy", "Playful", 3);
                        int had;
                        picks.TryGetValue(key ?? "", out had);
                        picks[key ?? ""] = had + 1;
                    }
                    Func<string, int> got = delegate(string key) { int n; return picks.TryGetValue(key, out n) ? n : 0; };
                    int inUse = got("Sassy|Playful|3|idle one"), unused = got("Sassy|Playful|3|idle two"), band = got("Sassy|Playful|4|idle one"), nearOne = got("Bratty|Playful|3|idle one"), far = got("Bratty|Grumpy|5|idle two");
                    if (!(inUse > band && band > far && inUse > nearOne && nearOne > far && inUse > unused && far > 0))
                        throw new Exception("Idle top-ups didn't follow use, then nearness: in use " + inUse + ", unused moment " + unused + ", next band " + band + ", near personality " + nearOne + ", far " + far);
                    checks.Add("PASS idle top-ups pick mostly what is in use (" + inUse + " of 4000), less for the next band (" + band + ") and a near personality (" + nearOne + "), and a far one still now and then (" + far + ")");
                }
                finally { Near = nearWas; NearMoods = nearMoodsWas; idleDice = idleDiceWas; lock (gate) stockOf.Clear(); }
                checks.Add("PASS the weekly meme search: asked once a week through the same services, a link or a too-short line left out, kept in expansionLines' memes.txt, and said among her funny lines only once hers have gone round");
            }
            finally
            {
                Send = sendWas; Now = nowWas; On = onWas; OwnBrain = ownWas; Expansion.Folder = folderWas;
                foreach (Engine one in Engines) { one.Fails = 0; one.WaitUntil = DateTime.MinValue; one.Sent.Clear(); }
                lock (gate) { used.Clear(); asked.Clear(); waiting.Clear(); waitingShare.Clear(); }
            }
        }
#endif
    }

    // Lines that grow by themselves (expansionLines): .txt files per personality, mood and wanting band, a "## section" per
    // moment, in the side's own folder. A file rolls into "_1", "_2" at about 100 KB; a read rolls a file and falls back
    // when its section is thin.
    internal static class Expansion
    {
        internal const string FolderName = "expansionLines";
        internal const int Roll = 100 * 1024, Thin = 3;
        // The side's folder for them. Set by her window; checks swap it.
        internal static Func<string> Folder;
        private static readonly Random dice = new Random();
        private static readonly object gate = new object();

        // Wanting in five bands of two points; nought goes with the lowest.
        internal static int Band(int percent) { return Math.Max(1, Math.Min(5, (int)Math.Ceiling(Math.Max(0, Math.Min(100, percent)) / 20.0))); }

        private static string Stem(string personality, string mood, int band)
        {
            Func<string> folder = Folder;
            string root = folder == null ? null : folder();
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(personality)) return null;
            string name = Regex.Replace(personality + " " + (string.IsNullOrEmpty(mood) ? "calm" : mood) + " wanting " + band, @"[^\w\- ]", "");
            return Path.Combine(root, name);
        }

        // The combination's files, oldest first.
        private static List<string> Files(string stem)
        {
            List<string> found = new List<string>();
            if (stem == null) return found;
            if (File.Exists(stem + ".txt")) found.Add(stem + ".txt");
            for (int n = 1; File.Exists(stem + "_" + n + ".txt"); n++) found.Add(stem + "_" + n + ".txt");
            return found;
        }

        private static string[] Section(string text, string section)
        {
            List<string> lines = new List<string>();
            bool under = false;
            foreach (string raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string row = raw.Trim();
                if (row.StartsWith("## ", StringComparison.Ordinal)) { under = string.Equals(row.Substring(3).Trim(), section, StringComparison.OrdinalIgnoreCase); continue; }
                if (under && row.Length > 0 && !row.StartsWith(">", StringComparison.Ordinal) && !row.StartsWith("#", StringComparison.Ordinal)) lines.Add(row);
            }
            return lines.ToArray();
        }

        // A file of its own in the folder, by name: memes.txt and its overflow.
        private static string Named(string name)
        {
            Func<string> folder = Folder;
            string root = folder == null ? null : folder();
            return string.IsNullOrEmpty(root) ? null : Path.Combine(root, name);
        }

        internal static string[] Lines(string personality, string mood, int band, string section) { return LinesAt(Stem(personality, mood, band), section); }

        internal static string[] LinesNamed(string name, string section) { return LinesAt(Named(name), section); }

        private static string[] LinesAt(string stem, string section)
        {
            try
            {
                List<string> files = Files(stem);
                if (files.Count == 0) return new string[0];
                int first;
                lock (gate) first = dice.Next(files.Count);
                string[] best = new string[0];
                for (int i = 0; i < files.Count; i++)
                {
                    string[] lines = Section(File.ReadAllText(files[i == 0 ? first : (i <= first ? i - 1 : i)], Encoding.UTF8), section);
                    if (lines.Length >= Thin) return lines;
                    if (lines.Length > best.Length) best = lines;
                }
                return best;
            }
            catch (Exception caughtOne) { Brain.Caught(caughtOne); return new string[0]; }
        }

        // The weekly duplicate cleanup: a line stays in the oldest of a combination's files and leaves the newer ones, so
        // the one filling up stays small. Runs when grown lines are next used a week after cleaned.txt's time.
        internal const string StampFile = "cleaned.txt";
        internal static Func<DateTime> Clock = delegate { return DateTime.UtcNow; };
        private static bool tidying;

        internal static void TidyWhenDue()
        {
            Func<string> folder = Folder;
            string root = folder == null ? null : folder();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;
            lock (gate) { if (tidying) return; tidying = true; }
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { if (Due(root)) Tidy(root); }
                catch (Exception caughtOne) { Brain.Caught(caughtOne); }
                finally { lock (gate) tidying = false; }
            });
        }

        private static bool Due(string root)
        {
            long last;
            string stamp = Path.Combine(root, StampFile);
            long now = (long)(Clock() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            return !File.Exists(stamp) || !long.TryParse(File.ReadAllText(stamp).Trim(), out last) || now - last >= 7 * 24 * 3600;
        }

        internal static int Tidy(string root)
        {
            int gone = 0;
            lock (gate)
            {
                Dictionary<string, SortedDictionary<int, string>> groups = new Dictionary<string, SortedDictionary<int, string>>(StringComparer.OrdinalIgnoreCase);
                foreach (string file in Directory.GetFiles(root, "*.txt"))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (string.Equals(Path.GetFileName(file), StampFile, StringComparison.OrdinalIgnoreCase)) continue;
                    Match rolled = Regex.Match(name, @"^(.*)_(\d+)$");
                    string stem = rolled.Success ? rolled.Groups[1].Value : name;
                    if (!groups.ContainsKey(stem)) groups[stem] = new SortedDictionary<int, string>();
                    groups[stem][rolled.Success ? int.Parse(rolled.Groups[2].Value) : 0] = file;
                }
                foreach (SortedDictionary<int, string> files in groups.Values)
                {
                    HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string file in files.Values)
                    {
                        List<string> rows = new List<string>(File.ReadAllText(file, Encoding.UTF8).Replace("\r\n", "\n").Split('\n'));
                        int before = rows.Count;
                        rows.RemoveAll(row =>
                        {
                            string one = row.Trim();
                            if (one.Length == 0 || one.StartsWith("#", StringComparison.Ordinal) || one.StartsWith(">", StringComparison.Ordinal)) return false;
                            return !seen.Add(one);
                        });
                        if (rows.Count == before) continue;
                        gone += before - rows.Count;
                        File.WriteAllText(file, string.Join(Environment.NewLine, rows.ToArray()), new UTF8Encoding(false));
                    }
                }
                File.WriteAllText(Path.Combine(root, StampFile), ((long)(Clock() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds).ToString());
            }
            if (gone > 0) Brain.Log("conversations", -1, "expansionLines: the weekly cleanup took out " + gone + " repeated lines");
            return gone;
        }

        // New lines under the section, in the newest file or a new one past the roll size; a line any of its files has is
        // left out. How many went in.
        internal static int Add(string personality, string mood, int band, string section, IEnumerable<string> lines) { return AddAt(Stem(personality, mood, band), section, lines); }

        internal static int AddNamed(string name, string section, IEnumerable<string> lines) { return AddAt(Named(name), section, lines); }

        private static int AddAt(string stem, string section, IEnumerable<string> lines)
        {
            if (stem == null) return 0;
            lock (gate)
            {
                List<string> files = Files(stem);
                HashSet<string> had = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string file in files)
                    foreach (string row in File.ReadAllText(file, Encoding.UTF8).Replace("\r\n", "\n").Split('\n')) if (row.Trim().Length > 0) had.Add(row.Trim());
                List<string> fresh = new List<string>();
                foreach (string line in lines) { string one = (line ?? "").Trim(); if (one.Length > 0 && !one.StartsWith("#", StringComparison.Ordinal) && had.Add(one)) fresh.Add(one); }
                if (fresh.Count == 0) return 0;
                string path = files.Count == 0 ? stem + ".txt" : files[files.Count - 1];
                if (File.Exists(path) && new FileInfo(path).Length >= Roll) path = stem + "_" + files.Count + ".txt";
                string text = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n")
                    : "# " + Path.GetFileName(stem) + "\n\n>! Lines grown by Better conversations. Edit or delete any.\n";
                List<string> rows = new List<string>(text.TrimEnd('\n').Split('\n'));
                int at = rows.FindIndex(row => string.Equals(row.Trim(), "## " + section, StringComparison.OrdinalIgnoreCase));
                if (at < 0) { rows.Add(""); rows.Add("## " + section); rows.Add(""); rows.AddRange(fresh); }
                else
                {
                    int end = at + 1;
                    while (end < rows.Count && !rows[end].TrimStart().StartsWith("## ", StringComparison.Ordinal)) end++;
                    while (end > at + 1 && rows[end - 1].Trim().Length == 0) end--;
                    rows.InsertRange(end, fresh);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, string.Join(Environment.NewLine, rows.ToArray()) + Environment.NewLine, new UTF8Encoding(false));
                return fresh.Count;
            }
        }
    }

    // The weekly meme search, on the same files: once a week, with Better conversations on, the services in turn are asked
    // for new meme lines; what is kept goes in memes.txt under funny, and joins her funny lines once hers have gone round.
    internal static class Memes
    {
        internal const string Name = "memes", Section = "funny", StampFile = "memes-asked.txt";
        private static readonly Regex Link = new Regex(@"https?://|www\.|\.com\b|#\w", RegexOptions.IgnoreCase);
        private static readonly Random dice = new Random();
        private static readonly object gate = new object();
        private static bool asking;
        private static int said;

        // A new session's count, and the checks'.
        internal static void Forget() { lock (gate) said = 0; }

        internal const string Words =
            "Write ten short funny meme lines people are saying online this month, the way a cheeky gamer girl would say them " +
            "out loud. One line each, no links, no hashtags, no emoji, nothing rude, keep it clean, no quotes around them.\n\n" +
            "Reply with only a JSON object and nothing else: \"funny\" as the key, and an array of the lines as the value.\n";

        // One of the grown memes now and then once her own funny lines have gone round, or null: never on air, never off.
        internal static string Next(int hers)
        {
            if (!Keyless.On() || Brain.OnAir) return null;
            AskWhenDue();
            string[] grown = Expansion.LinesNamed(Name, Section);
            lock (gate) said++;
            if (grown.Length == 0 || said <= hers || dice.Next(3) != 0) return null;
            string picked = Sayings.Longest(grown, dice);
            Sayings.Rested(picked, 0, true, Name + ": " + Section);
            return picked;
        }

        internal static bool Due()
        {
            Func<string> folder = Expansion.Folder;
            string root = folder == null ? null : folder();
            if (string.IsNullOrEmpty(root)) return false;
            string stamp = Path.Combine(root, StampFile);
            long last, now = (long)(Expansion.Clock() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            return !File.Exists(stamp) || !long.TryParse(File.ReadAllText(stamp).Trim(), out last) || now - last >= 7 * 24 * 3600;
        }

        internal static void AskWhenDue()
        {
            lock (gate) { if (asking || !Due()) return; asking = true; }
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { Ask(); }
                catch (Exception caughtOne) { Brain.Caught(caughtOne); }
                finally { lock (gate) asking = false; }
            });
        }

        // The week's ask: the time is written first, so a failure waits a week like a success. How many lines were new.
        internal static int Ask()
        {
            Func<string> folder = Expansion.Folder;
            string root = folder == null ? null : folder();
            if (string.IsNullOrEmpty(root)) return 0;
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, StampFile), ((long)(Expansion.Clock() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds).ToString());
            string who;
            string reply = Keyless.Ask(Words, out who);
            if (reply == null) return 0;
            string[] lines;
            if (!Blends.Kept(reply, new List<string> { Section }).TryGetValue(Section, out lines) || lines.Length == 0)
            {
                Brain.Log("PROBLEM", -1, "Better conversations: " + who + " sent no meme lines that could be kept this week.");
                return 0;
            }
            List<string> kept = new List<string>();
            foreach (string one in lines) if (one.Length >= 4 && one.Length <= 120 && !Link.IsMatch(one) && one.IndexOf('\n') < 0) kept.Add(one);
            int added = Expansion.AddNamed(Name, Section, kept);
            Brain.Log("conversations", -1, "expansionLines: the weekly meme search kept " + added + " new line(s) from " + who);
            return added;
        }
    }
}
