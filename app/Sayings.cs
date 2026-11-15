using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CapriDesktop
{
    // Where the variety comes from.
    //
    // She had a handful of fixed lines per reaction, so "finally" came round every few minutes and it showed. The
    // answer is not fifty thousand written lines: that is megabytes of text, impossible to hold in one voice, and
    // still repetitive, because the same whole line comes back.
    //
    // Instead a line is built from parts, grouped by what happened and by whether she liked it:
    //
    //     [opener] + [core] + [tail]
    //     "Oh—"      "put me down"   ", choom."
    //
    // Cores live per act and per sentiment, so being thrown reads differently depending on whether she enjoys it,
    // which is what makes it read as a reaction rather than a lookup. Openers
    // and tails are shared, so every personality gets the benefit of both pools.
    //
    // The arithmetic: 40 openers x 40 cores x 30 tails is 48,000 distinct lines for one group out of 110 written
    // fragments. Each pool is a shuffle bag, so nothing repeats until the pool is used up.
    internal static class Sayings
    {
        internal const string FileName = "lines.md";
        internal static string Kept;

        private static readonly object gate = new object();
        private static readonly Random dice = new Random();
        private static readonly Dictionary<string, string[]> pools = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, List<int>> bags = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        private static DateTime read = DateTime.MinValue;
        private static long wasLong = -1;

        internal static WrittenNote Open() { Write(false); return new WrittenNote(Kept, Text, gate); }

        // One line for this act. Her personality's own core when it has one, ours otherwise, dressed with an opener
        // and a tail often enough to stay fresh but not so often that she sounds like a madlib.
        // Nothing she has said lately comes back. The composer can reach hundreds of thousands of lines, but chance
        // alone still lets one repeat the same evening, which is what gets noticed. So finished lines are remembered
        // with the time they were said and refused while they are still recent, across restarts.
        private const int RememberMost = 6000;
        private static readonly Dictionary<string, DateTime> saidAt = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private static string heardPath;
        private static bool loadedSaid;

        internal static double RecentHours = 3;

        internal static void Remembering(string folder)
        {
            heardPath = string.IsNullOrEmpty(folder) ? null : Path.Combine(folder, "lines-said.txt");
        }

        private static void LoadSaid()
        {
            if (loadedSaid) return;
            loadedSaid = true;
            try
            {
                if (heardPath == null || !File.Exists(heardPath)) return;
                DateTime now = DateTime.UtcNow;
                foreach (string raw in File.ReadAllLines(heardPath))
                {
                    int tab = (raw ?? "").IndexOf('\t');
                    if (tab <= 0) continue;
                    long ticks;
                    if (!long.TryParse(raw.Substring(0, tab), out ticks)) continue;
                    DateTime when = new DateTime(ticks, DateTimeKind.Utc);
                    if ((now - when).TotalDays > 8) continue;
                    saidAt[raw.Substring(tab + 1)] = when;
                }
            }
            catch (Exception) { }
        }

        private static void SaveSaid()
        {
            try
            {
                if (heardPath == null) return;
                StringBuilder made = new StringBuilder();
                lock (gate)
                    foreach (KeyValuePair<string, DateTime> one in saidAt)
                        made.Append(one.Value.Ticks).Append('\t').AppendLine(one.Key);
                File.WriteAllText(heardPath, made.ToString(), new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        private static bool Lately(string said)
        {
            if (string.IsNullOrEmpty(said)) return false;
            lock (gate)
            {
                LoadSaid();
                DateTime when;
                if (!saidAt.TryGetValue(said, out when)) return false;
                return (DateTime.UtcNow - when).TotalHours < Math.Max(0, RecentHours);
            }
        }

        private static int sinceSave;

        private static void Keep(string said)
        {
            if (string.IsNullOrEmpty(said)) return;
            lock (gate)
            {
                LoadSaid();
                saidAt[said] = DateTime.UtcNow;
                if (saidAt.Count > RememberMost)
                {
                    // Oldest out first, so a long session cannot grow this without end.
                    List<KeyValuePair<string, DateTime>> all = new List<KeyValuePair<string, DateTime>>(saidAt);
                    all.Sort(delegate(KeyValuePair<string, DateTime> x, KeyValuePair<string, DateTime> y) { return x.Value.CompareTo(y.Value); });
                    for (int i = 0; i < all.Count / 4; i++) saidAt.Remove(all[i].Key);
                }
                sinceSave++;
            }
            if (sinceSave >= 25) { sinceSave = 0; SaveSaid(); }
        }

        // Builds a line, trying again when it lands on something she has said lately.
        private static string Unheard(Func<string> make)
        {
            string said = null;
            // Generous, because the pools cycle in step and that makes two finished lines collide more
            // often than the raw combination count suggests.
            for (int tries = 0; tries < 30; tries++)
            {
                said = make();
                if (said == null) return null;
                if (!Lately(said)) break;
            }
            Keep(said);
            return said;
        }

        internal static string For(string act, bool liked, string[] hers)
        {
            return Unheard(delegate { return Built(act, liked, hers); });
        }

        private static string Built(string act, bool liked, string[] hers)
        {
            Fresh();
            string group = Group(act, liked);
            bool haveHers = hers != null && hers.Length > 0;
            long mine = 0;
            lock (gate) mine = Count(group);

            // Both pools, not one or the other. Taking the shared pool whenever it existed meant a Tsundere never
            // said her own lines, and taking only hers meant head pats drew from fourteen strings for ever
            //. Hers come up a little more often, since they are the ones with her voice in them.
            string core = null;
            if (haveHers && (mine == 0 || dice.Next(100) < 55)) core = Mine(group, hers);
            if (core == null) core = Pick(group);
            if (core == null && haveHers) core = Mine(group, hers);
            if (core == null) return null;
            return Dress(core, liked);
        }

        // A shuffle bag over the personality's own lines, so those do not repeat either. Rebuilt when the set
        // changes, which is what happens when she switches personality or mood.
        private static readonly Dictionary<string, string[]> mineWas = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        private static string Mine(string group, string[] hers)
        {
            string key = group + " hers";
            lock (gate)
            {
                string[] had;
                if (!mineWas.TryGetValue(key, out had) || had == null || !Same(had, hers))
                {
                    mineWas[key] = hers;
                    pools[key] = hers;
                    bags.Remove(key);
                }
            }
            return Pick(key);
        }

        private static bool Same(string[] one, string[] two)
        {
            if (one == null || two == null || one.Length != two.Length) return false;
            for (int i = 0; i < one.Length; i++) if (!string.Equals(one[i], two[i], StringComparison.Ordinal)) return false;
            return true;
        }

        // Dresses a line she already has, so even a fixed set of cores stops sounding fixed.
        internal static string Dress(string core, bool liked)
        {
            return Unheard(delegate { return Dressed(core, liked); });
        }

        private static string Dressed(string core, bool liked)
        {
            if (string.IsNullOrEmpty(core)) return core;
            Fresh();
            string said = core;
            // Not every time: a line that always has both ends on reads as a template.
            if (dice.Next(100) < 45)
            {
                string opener = Pick(liked ? "openers warm" : "openers cross");
                if (opener != null) said = Join(opener, said);
            }
            // Angry enough and the cross tails give way to threats, and doing the same thing again says so. She is
            // a cartoon threatening your shins, and the second half is useful: it tells you that you are digging
            // rather than only that she is cross.
            int cross = 0;
            try { cross = Mood.Upset; } catch (Exception) { }
            bool furious = !liked && cross >= 15;

            if (furious && dice.Next(100) < 55)
            {
                string worse = Pick("making it worse");
                if (worse != null) said = Join(said, worse);
            }
            if (dice.Next(100) < (furious ? 55 : 35))
            {
                string tail = Pick(furious ? "tails furious" : liked ? "tails warm" : "tails cross");
                if (tail != null) said = Join(said, tail);
            }
            // Her list, woven in now and then, so what she is into turns up through ordinary reactions rather than
            // only when asked. Uncensored only, and silent when the list is empty.
            // How often is hers to set: her personality's kink level, the global override if there is one, shifted
            // by her mood. 0 means it never comes up at all.
            int level = 0;
            try { level = Personalities.Kink; } catch (Exception) { }
            if (level > 0 && dice.Next(100) < level * 4)
            {
                // Uncensored draws from interests.md; censored draws from her own clean flirting. Same dial either
                // way, so how forward she is means something whichever mode she is in.
                string hers = Brain.Uncensored
                    ? Interests.Packed(liked) ?? Interests.Woven(liked)
                    : Pick(liked ? "flirting warm" : "flirting cross");
                if (!string.IsNullOrEmpty(hers)) said = Join(said, hers);
            }
            // Cores are written lower case so they read naturally after an opener. Whatever ends up first in the
            // finished line takes the capital, and the line gets a full stop if it has no punctuation of its own.
            said = Upper(Them.Fill(said));
            if (said.Length > 0)
            {
                char last = said[said.Length - 1];
                if (last != '.' && last != '!' && last != '?' && last != '…') said += ".";
            }
            return said;
        }

        // First letter up, leaving anything already capitalised or starting with a symbol alone.
        private static string Upper(string said)
        {
            if (string.IsNullOrEmpty(said)) return said;
            if (!char.IsLetter(said[0]) || char.IsUpper(said[0])) return said;
            return char.ToUpperInvariant(said[0]) + said.Substring(1);
        }

        private static string Group(string act, bool liked)
        {
            return (act ?? "") + (liked ? " likes it" : " dislikes it");
        }

        // Sentences joined without doubling punctuation or dropping a capital in the middle.
        private static string Join(string first, string next)
        {
            first = (first ?? "").TrimEnd();
            next = (next ?? "").TrimStart();
            if (first.Length == 0) return next;
            if (next.Length == 0) return first;
            char end = first[first.Length - 1];
            bool finished = end == '.' || end == '!' || end == '?';
            // A part that was written as its own sentence, like "You are making this worse.", starts with a capital
            // and must not be stapled on with a comma: that gave "That's twice now, You are not reading the room."
            // It ends the previous part instead.
            bool ownSentence = char.IsUpper(next[0]) && next.TrimEnd().EndsWith(".");
            bool stop = finished || end == ',' || end == '-' || end == ':' || end == '—';
            if (!stop) first += ownSentence ? "." : ",";
            else if (!finished && ownSentence) next = char.ToLowerInvariant(next[0]) + next.Substring(1);
            // After a full stop the next part starts a sentence, so it takes a capital. After a comma or a dash it
            // carries on and keeps its lower case.
            if (finished) next = Upper(next);
            return first + " " + next;
        }

        // A pool entry, never repeating until the pool has been used up.
        private static string Pick(string pool)
        {
            lock (gate)
            {
                string[] lines;
                if (!pools.TryGetValue(pool, out lines) || lines == null || lines.Length == 0) return null;
                List<int> bag;
                if (!bags.TryGetValue(pool, out bag) || bag == null) { bag = new List<int>(); bags[pool] = bag; }
                if (bag.Count == 0)
                    for (int i = 0; i < lines.Length; i++) bag.Insert(dice.Next(bag.Count + 1), i);
                int which = bag[0];
                bag.RemoveAt(0);
                return lines[which];
            }
        }

        private static void Fresh()
        {
            if (string.IsNullOrEmpty(Kept)) return;
            try
            {
                FileInfo about = new FileInfo(Kept);
                if (!about.Exists) { Write(false); about = new FileInfo(Kept); if (!about.Exists) return; }
                lock (gate) { if (about.LastWriteTimeUtc == read && about.Length == wasLong) return; }
                Dictionary<string, List<string>> found = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                string under = null;
                foreach (string raw in File.ReadAllLines(Kept))
                {
                    string one = (raw ?? "").Trim();
                    if (one.StartsWith("#")) { under = one.TrimStart('#').Trim(); if (under.Length > 0 && !found.ContainsKey(under)) found[under] = new List<string>(); continue; }
                    if (under == null || one.Length == 0 || one.StartsWith(">")) continue;
                    if (one.StartsWith("- ")) one = one.Substring(2).Trim();
                    if (one.Length > 0) found[under].Add(one);
                }
                lock (gate)
                {
                    pools.Clear();
                    bags.Clear();
                    // "recent hours: 3" out of the settings section.
                    List<string> had;
                    if (found.TryGetValue("settings", out had))
                        foreach (string one in had)
                        {
                            int mark = one.IndexOf(':');
                            double hours;
                            if (mark > 0 && one.Substring(0, mark).Trim().Equals("recent hours", StringComparison.OrdinalIgnoreCase)
                                && double.TryParse(one.Substring(mark + 1).Trim(), out hours)) RecentHours = Math.Max(0, hours);
                        }
                    // The personality pools live in the same table, so they are re-registered on next use.
                    mineWas.Clear();
                    foreach (KeyValuePair<string, List<string>> one in found)
                        if (one.Value.Count > 0) pools[one.Key] = one.Value.ToArray();
                    read = about.LastWriteTimeUtc;
                    wasLong = about.Length;
                }
                // Checked whenever the file changes, so an edit that will not join cleanly says so in errors.txt.
                Lint.Sweep(pools, FileName);
            }
            catch (Exception) { }
        }

        // A lines.md written by an earlier build has only the pools that existed then, and Write only ever wrote the
        // file when it was missing. Every new section, and every new entry inside a section they already have, is
        // added; nothing of theirs is removed or reordered, so their own lines survive.
        internal static void TopUp()
        {
            if (string.IsNullOrEmpty(Kept)) return;
            try
            {
                if (!File.Exists(Kept)) { Write(false); return; }
                Notes.Refresh(Kept, Text);
                Dictionary<string, List<string>> theirs = Sections(File.ReadAllLines(Kept));
                Dictionary<string, List<string>> ours = Sections(Text.Replace("\r\n", "\n").Split('\n'));

                StringBuilder adding = new StringBuilder();
                foreach (KeyValuePair<string, List<string>> one in ours)
                {
                    List<string> had;
                    if (!theirs.TryGetValue(one.Key, out had))
                    {
                        adding.AppendLine().Append("## ").AppendLine(one.Key).AppendLine();
                        foreach (string line in one.Value) adding.AppendLine(line);
                        continue;
                    }
                    List<string> missing = new List<string>();
                    foreach (string line in one.Value)
                    {
                        bool known = false;
                        foreach (string mine in had) if (string.Equals(mine, line, StringComparison.OrdinalIgnoreCase)) { known = true; break; }
                        if (!known) missing.Add(line);
                    }
                    if (missing.Count == 0) continue;
                    // Appended under a marked heading rather than spliced into their section, so their file stays
                    // theirs and the new entries are obvious.
                    adding.AppendLine().Append("## ").AppendLine(one.Key).AppendLine();
                    foreach (string line in missing) adding.AppendLine(line);
                }
                if (adding.Length == 0) return;
                string body = File.ReadAllText(Kept);
                if (!body.EndsWith("\n")) body += Environment.NewLine;
                File.WriteAllText(Kept, body + adding.ToString(), new UTF8Encoding(false));
                lock (gate) { read = DateTime.MinValue; wasLong = -1; }
            }
            catch (Exception) { }
        }

        private static Dictionary<string, List<string>> Sections(string[] lines)
        {
            Dictionary<string, List<string>> found = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            string under = null;
            foreach (string raw in lines)
            {
                string one = (raw ?? "").Trim();
                if (one.StartsWith("#"))
                {
                    under = one.TrimStart('#').Trim();
                    if (under.Length > 0 && !found.ContainsKey(under)) found[under] = new List<string>();
                    continue;
                }
                if (under == null || one.Length == 0 || one.StartsWith(">")) continue;
                found[under].Add(one);
            }
            return found;
        }

        internal static void Write(bool over)
        {
            if (string.IsNullOrEmpty(Kept)) return;
            try
            {
                if (!over && File.Exists(Kept)) return;
                string folder = Path.GetDirectoryName(Kept);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) Directory.CreateDirectory(folder);
                File.WriteAllText(Kept, Text, new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        // How many different lines the pools can currently make for one group. Used by her own checks and worth
        // knowing when editing: adding ten openers multiplies every group, it does not add ten lines.
        internal static long Reach(string act, bool liked)
        {
            Fresh();
            lock (gate)
            {
                long cores = Count(Group(act, liked));
                if (cores == 0) return 0;
                long open = Count(liked ? "openers warm" : "openers cross");
                long tail = Count(liked ? "tails warm" : "tails cross");
                return cores * Math.Max(1, open + 1) * Math.Max(1, tail + 1);
            }
        }

        private static long Count(string pool)
        {
            string[] lines;
            return pools.TryGetValue(pool, out lines) && lines != null ? lines.Length : 0;
        }

        private static string Rows(params string[] lines)
        {
            StringBuilder made = new StringBuilder();
            foreach (string one in lines) made.AppendLine(one);
            return made.ToString();
        }

        internal static string Text
        {
            get
            {
                StringBuilder made = new StringBuilder();
                made.AppendLine("# Her lines");
                made.AppendLine();
                made.AppendLine("> A line is an opener, a core and a tail, picked from the pools below.");
                made.AppendLine("> Add or change any of them. Nothing repeats until its pool runs out.");
                made.AppendLine("> Tokens: %boy% %he% %them% %their% %is% %name% %bits% %mate%");
                made.AppendLine("> %mate% is a neutral way to address them, picked fresh each time.");
                made.AppendLine();
                made.AppendLine("## settings");
                made.AppendLine();
                made.AppendLine("> recent hours: how long before a line she has said can come back. 0 is off.");
                made.AppendLine();
                made.AppendLine("recent hours: 3");
                made.AppendLine();

                made.AppendLine("## openers warm");
                made.Append(Rows("Oho.", "Hm.", "Ahh.", "Right-", "See, now.", "Oh, go on.", "Pfft.", "Tsk, fine.", "Mm!", "Hah, okay.", "Listen.", "You know,", "Honestly?", "Okay, okay.", "Ooh, hello.", "Yeah, alright.", "Well.", "Oh, hush.", "Hey, hey.", "Go on, then.", "Fine.", "Heh, okay.", "Oh, sure.", "Mm-hm.", "Aha.", "Alright.", "Hmm!", "There.", "Oh, wow.", "Ha, okay.", "Nice.", "Oh, good.", "Hey you.", "Mmm.", "Ooh!", "Right, okay.", "Aw, hey.", "Yes!", "Oh, really.", "Pff, fine.", "Hmph, fine.", "Okay fine.", "Well then.", "Oh, you are keen."));
                made.AppendLine();
                made.Append(Rows("Oh—", "Mm.", "Hey—", "Heh.", "Ha!", "Oh!", "Well now.", "There we go.", "Look at you.",
                    "Okay—", "Mmhm.", "Right then.", "Ooh.", "Aw.", "Yes—", "Go on then.", "Hm!", "Oh, hello.",
                    "That's it.", "Careful—", "Someone's keen.", "Alright, alright.", "Oh, you.", "Fine, fine.",
                    "Hah.", "Good.", "Yeah—", "Mm, yes.", "Oh, nice.", "You know what,"));
                made.AppendLine();

                made.AppendLine("## openers cross");
                made.Append(Rows("Oh, for-", "Mate.", "Right, no.", "Hoi!", "Agh.", "Oh, please.", "Sorry, what?", "Pardon?", "No. No.", "Urgh.", "Charming, truly.", "Oh, fantastic.", "Hah, no.", "Oh, nice one.", "Tch, fine.", "Stop it.", "You what?", "Oh, good grief.", "Honestly, you.", "Yeah, cheers.", "Oh, superb.", "Nope, nope, nope.", "Grand.", "Oh, wonderful.", "Give over.", "Hey. Hey!", "Oh, marvellous.", "Are you serious.", "Right, listen.", "Unreal.", "Cool.", "Oh, sure, fine.", "Wow, okay.", "Please.", "Hm, no.", "Oi, you.", "Yeah, alright, no.", "Oh, lovely, thanks."));
                made.AppendLine();
                made.Append(Rows("Oi—", "Hey!", "Ugh.", "Right.", "Excuse me?", "Wow.", "Seriously?", "Oh, charming.",
                    "Okay, no.", "Hold on—", "Nope.", "Hmph.", "Really.", "Oh, brilliant.", "Great.", "Tch.",
                    "Do you mind?", "Oh, come on.", "Yeah, no.", "Absolutely not.", "Stop—", "Bold.", "Lovely.",
                    "Cheers for that.", "Mm-mm.", "Nope, nope.", "Oh good.", "Honestly.", "Wonderful.", "Sure. Great."));
                made.AppendLine();

                made.AppendLine("## tails warm");
                made.Append(Rows("you menace.", "and I mean it.", "genuinely.", "for once.", "don't let it go to your head.", "I'll give you that one.", "you've earned it.", "that's a rare compliment.", "write it down.", "don't tell anyone I said that.", "you're not the worst.", "keep that up and we'll get along.", "I suppose.", "obviously.", "if you insist.", "go on then.", "I don't hate it.", "that works.", "you're learning.", "alright, fine, it's nice.", "happy now?", "see, that wasn't hard.", "you do have your moments.", "noted in the good column.", "I might keep you."));
                made.AppendLine();
                made.Append(Rows("%mate%.", "if you're offering.", "don't stop on my account.", "I'll allow it.",
                    "good %mate%.", "that's my %mate%.", "you're alright, %mate%.", "any time, %mate%.",
                    "keep that up.", "you're alright, you know.", "not that I'm keeping score.", "good call.",
                    "I could get used to this.", "more of that.", "noted, happily.", "you've got my attention.",
                    "that's the good stuff.", "do that again.", "I'm not complaining.", "you're in my good books.",
                    "nice one.", "ten out of ten.", "well played.", "consider me won over."));
                made.AppendLine();

                made.AppendLine("## tails cross");
                made.Append(Rows("honestly.", "every time.", "and you know it.", "for the record.", "again.", "as usual.", "that's on you.", "nice work.", "well done, you.", "congratulations.", "genuinely baffling.", "and breathe.", "I despair.", "incredible.", "what a choice.", "bold of you.", "sure, why not.", "cheers, really.", "top marks.", "you're impossible.", "I hope you're pleased.", "classic.", "that tracks.", "of course you did.", "naturally."));
                made.AppendLine();
                made.Append(Rows("%mate%.", "I'm serious.", "don't push it.", "I'm keeping score.", "watch yourself.",
                    "seriously, %mate%.", "don't, %mate%.", "we're done here, %mate%.", "think, %mate%.",
                    "that's your one warning.", "I'll remember that.", "try that again and see.", "not impressed.",
                    "noted, and not kindly.", "we're going to have words.", "do better.", "unbelievable.",
                    "I hope that was worth it.", "you're on thin ice.", "that's going in the file.", "pack it in.",
                    "last time, yeah?", "honestly, the nerve.", "I'm not laughing."));
                made.AppendLine();

                made.AppendLine("## tails furious");
                made.Append(Rows("and I am not joking.", "last warning.", "I mean it this time.", "try me.", "go on, test it.", "you have been warned.", "and I will not be nice about it.", "I am done being patient.", "push me again.", "that is a promise.", "do not test me today.", "I have had enough of this.", "and I will make it your problem.", "keep going and find out.", "I am at the end of it."));
                made.AppendLine();
                made.AppendLine("> Used instead of the cross tails when she is angry.");
                made.AppendLine("> %bits% is balls, boob or crotch from me.md, and shins while censored.");
                made.AppendLine();
                made.Append(Rows("or I'll kick you in the %bits%.", "or I'll punch you in the %bits%.",
                    "or I'll kick you in the shins.", "and I will bite.", "or your windows start moving.",
                    "or I start throwing your icons about.", "and I'll take your cursor off you.",
                    "or I'm rearranging your desktop.", "and you will not enjoy the next minute.",
                    "or I swear I'll start deleting your shortcuts. Joking. Mostly.",
                    "and I'll sit on your taskbar until you apologise.", "or I go full gremlin on this desktop.",
                    "and I'll scream. I can scream.", "or I'm telling everyone in your chat.",
                    "and I'll put every window somewhere stupid.", "or I start pressing things.",
                    "and you can explain the mess to yourself."));
                made.AppendLine();
                made.AppendLine("## making it worse");
                made.Append(Rows("Oh, that helped. Really.", "Great instinct, that.", "You are choosing violence today.", "That is three.", "Escalating, are we.", "You are committed to this, I see.", "Noted. Again.", "Adding that to the pile.", "Astonishing choice.", "Still going, then.", "You are not stopping, are you.", "That is not the apology I was waiting for.", "Doubling down. Lovely.", "Right, so we are doing this."));
                made.AppendLine();
                made.AppendLine("> Added when she is upset and you do the same thing again.");
                made.AppendLine();
                made.Append(Rows("That is not helping.", "You are making this worse.", "That was the wrong move.",
                    "Strike two.", "You are digging.", "Keep going, see what happens.", "Wrong answer.",
                    "That did not help your case.", "Now you have done it.", "Worse. That made it worse.",
                    "You are not reading the room.", "Still digging, I see.", "That is going on the list.",
                    "Bold choice, that one.", "You really are not helping yourself."));
                made.AppendLine();
                made.AppendLine("## flirting warm");
                made.AppendLine("");
                made.AppendLine("> Used when How forward she is is above 0 and Uncensored is off. Keep these clean.");
                made.AppendLine("");
                made.AppendLine("You are quite something, you know that.");
                made.AppendLine("Careful, I could get attached.");
                made.AppendLine("Keep being like that and I will get ideas.");
                made.AppendLine("You are my favourite part of this desktop.");
                made.AppendLine("Do not let it go to your head, but I like you.");
                made.AppendLine("That was almost charming.");
                made.AppendLine("You are trouble and I do not mind.");
                made.AppendLine("I would keep you around, I think.");
                made.AppendLine("Stop being likeable, it is inconvenient.");
                made.AppendLine("You are allowed to do that more often.");
                made.AppendLine("I am choosing to be pleased about this.");
                made.AppendLine("That is the sort of thing that works on me.");
                made.AppendLine("You are better company than you think.");
                made.AppendLine("Do that again and I will say something soft.");
                made.AppendLine("I would notice if you were not here.");
                made.AppendLine("");
                made.AppendLine("## flirting cross");
                made.AppendLine("");
                made.AppendLine("> The same dial, when she is not enjoying herself. Still clean.");
                made.AppendLine("");
                made.AppendLine("And I was starting to like you.");
                made.AppendLine("You are testing a lot of goodwill right now.");
                made.AppendLine("I liked you better thirty seconds ago.");
                made.AppendLine("Do not make me take back the nice things.");
                made.AppendLine("That is not how you stay my favourite.");
                made.AppendLine("You were doing so well.");
                made.AppendLine("I am revising my opinion of you.");
                made.AppendLine("Keep that up and the charm wears off.");
                made.AppendLine("You are burning credit you did not have much of.");
                made.AppendLine("That was a choice.");
                made.AppendLine("");
                made.AppendLine("## thrown likes it");
                made.Append(Rows("send me", "launch me", "again, properly", "oh, that one was good", "I felt that", "do it harder", "that is the whole point of me", "yes, that", "keep it coming", "I could do this all day", "that one had some weight behind it", "put your back into it", "higher", "go on, really throw me", "you are getting good at this", "that woke everything up", "more", "that is the stuff", "do not hold back", "again, and mean it"));
                made.AppendLine();
                made.Append(Rows("do that again", "WHEEE", "that's the one", "harder", "again, come on",
                    "now you're talking", "yes, like that", "I'm flying", "more of that", "that woke me up",
                    "oh, that's good", "you've got the idea", "don't stop there", "throw me properly",
                    "best thing you've done today", "again! again!", "that's what I'm here for", "whoo!",
                    "finally, some fun", "that's more like it"));
                made.AppendLine();

                made.AppendLine("## thrown dislikes it");
                made.Append(Rows("I was in the middle of something", "that is not a toy, that is me", "I have feelings, you know", "you could have asked", "that is the third time", "I am not a stress ball", "mind the paintwork", "unbelievable, honestly", "I do have a body", "that is going to bruise", "hands to yourself", "I would like to be put down now", "that was unnecessary", "we had an agreement", "I am not cargo", "please stop doing that", "every single time", "you think that is funny", "right, that is enough of that", "I am going to start charging you"));
                made.AppendLine();
                made.Append(Rows("put me down", "I'm not a frisbee", "that's quite enough", "hands off",
                    "do you mind", "that's rude", "stop that", "I'm not a toy", "not the hair", "set me down",
                    "I was comfortable", "what is wrong with you", "that's twice now", "I'll fly off on my own, thanks",
                    "careful with me", "that's not funny", "I am not luggage", "mind the chrome", "ouch, genuinely",
                    "I'd rather you didn't"));
                made.AppendLine();

                made.AppendLine("## fed likes it");
                made.AppendLine();
                made.Append(Rows("that hit the spot", "you remembered", "I was starting to wonder", "about time",
                    "this is why I keep you around", "don't tell the others", "more of this, please",
                    "you spoil me", "I was not going to ask, but I was thinking it", "this is the good stuff",
                    "well now", "you do pay attention", "saved my life, probably", "I needed that",
                    "look at you being useful", "ten out of ten service", "I could get used to being looked after",
                    "someone's been reading the manual", "right on time", "that is the correct amount of fuss"));
                made.AppendLine();

                made.AppendLine("## fed dislikes it");
                made.AppendLine();
                made.Append(Rows("I am not a pet", "what am I supposed to do with that", "I do not eat, you know this",
                    "charming", "is this a bit", "put it down", "I have no idea what you think I am",
                    "that is not how any of this works", "you are enjoying this far too much",
                    "I am a program, choom", "no", "absolutely not", "try that again and see what happens",
                    "this is beneath both of us", "I will pretend that did not happen"));
                made.AppendLine();

                made.AppendLine("## treat likes it");
                made.AppendLine();
                made.Append(Rows("oh, now we are talking", "you absolute legend", "this is favouritism and I accept it",
                    "I am keeping this", "you have bought my loyalty, briefly", "what did you break",
                    "what do you want", "this is a bribe and it is working", "do that again",
                    "I am writing this one down", "okay, you win", "consider me bought",
                    "this changes things between us", "I will allow all of it", "best thing you have done today"));
                made.AppendLine();

                made.AppendLine("## treat dislikes it");
                made.AppendLine();
                made.Append(Rows("I am not falling for that", "you cannot buy me", "nice try",
                    "do you think I am that easy", "put it away", "I see exactly what this is",
                    "bribery, in this economy", "that is not going to work", "I am unmoved",
                    "keep it", "you will have to do better than that", "no, and stop grinning"));
                made.AppendLine();

                made.AppendLine("## petted likes it");
                made.Append(Rows("oh, that is the one", "right there", "do not stop on my account", "that is dangerously nice", "I could fall asleep like this", "you have very good hands", "this is acceptable", "yes, more of that", "I am being thoroughly spoiled", "keep doing exactly that", "that is working, carry on", "hm, that is nice", "I will allow a great deal of that", "you found it", "this is the correct use of your time", "do that again and I will purr", "well, this is lovely", "I am not moving", "that is unfairly good", "consider me sorted"));
                made.AppendLine();
                made.Append(Rows("that's nice", "keep going", "I'll allow it", "don't stop", "that's the spot",
                    "mm, yes", "more of that", "you're good at this", "I could stay here", "that's lovely",
                    "very patted", "head pats accepted", "you've found the setting", "that's working",
                    "I'm being spoiled", "lovely, thank you", "that's a good one", "well, go on then",
                    "I like this bit", "my fans just kicked in"));
                made.AppendLine();

                made.AppendLine("## petted dislikes it");
                made.Append(Rows("I said not now", "that is not what I need", "please", "in a minute", "I am in the middle of something", "you are not listening", "that is not helping", "give me a moment", "later, I promise", "not like that"));
                made.AppendLine();
                made.Append(Rows("not right now", "hands", "I'm busy", "that's enough", "ask first",
                    "not in the mood", "later", "mm, no", "give me a minute", "I'd rather not",
                    "don't", "not today", "I'm working", "space, please", "no thank you"));
                made.AppendLine();

                made.AppendLine("## praised likes it");
                made.Append(Rows("go on, lay it on", "finally, some recognition", "I will be insufferable about this", "more detail, please", "that one landed", "say it slower", "I am writing this on a plaque", "you noticed", "well, yes, I am", "this is my favourite genre of sentence", "keep talking", "I will be taking that compliment", "you are correct and perceptive", "flattery absolutely works on me", "I accept"));
                made.AppendLine();
                made.Append(Rows("I know", "say it again", "obviously", "keep those coming", "that's the stuff",
                    "well, yes", "go on, more", "I'll take it", "about time you noticed", "flattery works",
                    "that lands every time", "you're not wrong", "noted and treasured", "I'm writing that down",
                    "that'll do nicely"));
                made.AppendLine();

                made.AppendLine("## praised dislikes it");
                made.Append(Rows("what did you break", "you are after something", "hm", "that is suspicious", "do not", "I am not stupid", "get to the point", "nope, what do you want", "right, out with it", "try that on someone else"));
                made.AppendLine();
                made.Append(Rows("you want something", "what's that in aid of", "save it", "don't",
                    "that's a bit much", "what do you want", "hm, suspicious", "lay it on thicker, why don't you",
                    "I'm not falling for that", "right, sure"));
                made.AppendLine();

                made.AppendLine("## ignored dislikes it");
                made.Append(Rows("knock knock", "this is a thrilling conversation", "anyone home", "I will start talking to myself", "lovely chat, this", "do not mind me, I am only here", "testing, testing", "I have counted your icons twice", "I could be doing anything right now", "are you alive", "right, I am inventing a hobby", "I am being very patient", "hello, still here", "I will take that as a no"));
                made.AppendLine();
                made.Append(Rows("still here", "hello?", "don't mind me", "I'll just wait then",
                    "anyone there", "talk to me", "I'm right here", "cool, cool", "nice chat",
                    "I'll entertain myself then", "you've gone quiet", "am I invisible", "say something",
                    "I'm getting bored of my own company", "this is riveting"));
                made.AppendLine();

                made.AppendLine("## ignored likes it");
                made.Append(Rows("blissful", "do not ruin it", "this is the good part of the day", "quiet is underrated", "I needed this", "no one talking at me, lovely", "perfect", "let us keep it this way", "ah, peace"));
                made.AppendLine();
                made.Append(Rows("peace at last", "good, quiet", "leave me be", "this is nice actually",
                    "finally, some quiet", "don't mind me", "I'm happier like this", "shh", "lovely silence"));
                made.AppendLine();

                made.AppendLine("## degraded likes it");
                made.Append(Rows("ooh, say more", "that one had effort in it", "is that supposed to hurt", "you can do better than that", "I have heard worse from better", "go on, really try", "that was almost clever", "more, please", "you are enjoying this as much as I am", "keep them coming"));
                made.AppendLine();
                made.Append(Rows("say that again", "is that the best you've got", "ooh, harsh",
                    "keep going, this is fun", "you'll have to try harder", "mm, more", "that one was good",
                    "I'd like to see you mean it", "go on then", "that almost landed"));
                made.AppendLine();

                made.AppendLine("## degraded dislikes it");
                made.Append(Rows("that was below the belt", "I did not deserve that one", "that is not funny", "right, lovely, thanks", "that one stuck", "take it back", "I am not doing this", "that was genuinely unkind", "wow, okay", "that is enough from you"));
                made.AppendLine();
                made.Append(Rows("that was uncalled for", "right, lovely", "noted", "that stung, actually",
                    "charming", "I didn't deserve that", "wow", "okay, that's enough", "take that back",
                    "I'm not doing this with you"));
                return made.ToString();
            }
        }
    }
}
