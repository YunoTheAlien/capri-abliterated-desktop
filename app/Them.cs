using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // Fills her lines' tokens from me.md: %gender% (boy or girl, else a pet name of theirs), %userPetName% (what she calls
    // them), %capriPetName% (what they call her), %he% %them% %their% %is% %name% %mate% %bits%; %boy%, %petname% old names.
    internal static class Them
    {
        private static readonly object gate = new object();
        private static string path;
        private static long wasStamp = -1;
        private static string he = "they", them = "them", their = "their", boy = "", isAre = "are", bits = "lower mid b", name = "", awake = "", pets = "";

        // What they wrote for awakeHours in me.md, or empty. Hours prefers this over what it counted.
        internal static string Awake { get { Fresh(); lock (gate) return awake; } }

        // What they are called, from me.md. Used to take their name back out of anything written to a file.
        internal static string Called { get { Fresh(); lock (gate) return name; } }

        internal static void Watch(string mePath) { lock (gate) { path = mePath; wasStamp = -1; } }

        internal static string Watched { get { lock (gate) return path; } }

        // Replaces the tokens in a line she is about to say.
        internal static string Fill(string line)
        {
            if (string.IsNullOrEmpty(line) || line.IndexOf('%') < 0) return line;
            Fresh();
            lock (gate)
            {
                string said = line;
                // Boy or girl only; anyone else is called one of their pet names, never "good they".
                string gender = boy.Length > 0 ? boy : Pet;
                said = Swap(said, "%gender%", gender);
                said = Swap(said, "%boy%", gender);
                said = Swap(said, "%capriPetName%", CapriPet);
                said = Swap(said, "%he%", he);
                said = Swap(said, "%them%", them);
                said = Swap(said, "%their%", their);
                said = Swap(said, "%is%", isAre);
                said = Swap(said, "%bits%", Bits);
                said = Swap(said, "%mate%", Mate);
                said = Swap(said, "%userPetName%", Pet);
                said = Swap(said, "%petname%", Pet);
                said = Swap(said, "%name%", name);
                return said;
            }
        }

        // Keeps the capital when the token started a sentence, so "%He% is" reads as "He is".
        private static string Swap(string line, string token, string with)
        {
            int at = line.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            while (at >= 0)
            {
                string put = with;
                if (put.Length > 0 && ((at + 1 < line.Length && char.IsUpper(line[at + 1])) || Opens(line, at)))
                    put = char.ToUpperInvariant(put[0]) + put.Substring(1);
                line = line.Substring(0, at) + put + line.Substring(at + token.Length);
                at = line.IndexOf(token, at + put.Length, StringComparison.OrdinalIgnoreCase);
            }
            return line;
        }

        // The token begins the line or a sentence: only spaces back to the start or to a full stop, question or exclamation mark.
        private static bool Opens(string line, int at)
        {
            int back = at - 1;
            while (back >= 0 && line[back] == ' ') back--;
            return back < 0 || ((line[back] == '.' || line[back] == '!' || line[back] == '?') && back < at - 1);
        }

        // Ways to address them that work for anyone. Her own "choom" leads, the rest keep it varied without
        // guessing at anyone. Nothing gendered goes in here.
        private static readonly string[] Mates =
        {
            "choom", "mate", "you", "trouble", "pal", "chief", "boss", "gremlin", "menace",
            "nova", "legend", "sunshine", "nerd", "disaster", "champ", "friend", "chum", "scout",
            "choomba", "samurai", "merc", "edgerunner", "netrunner", "solo", "preem one", "wildcard",
            "hotshot", "ace", "captain", "rockstar", "troublemaker", "goblin", "creature", "comrade",
            "partner", "buddy", "tiger", "genius", "weirdo", "menace to society", "chaos engine",
            "problem", "handful", "danger", "trooper", "operator", "ringleader", "firecracker"
        };

        private static readonly Random dice = new Random();

        // What they asked to be called in me.md, which beats everything: it is the one list they wrote themselves.
        private static string[] Theirs()
        {
            Fresh();
            string said;
            lock (gate) said = pets;
            if (string.IsNullOrWhiteSpace(said)) return null;
            List<string> found = new List<string>();
            foreach (string one in said.Split(',', ';', '/'))
            {
                string name = (one ?? "").Trim();
                if (name.Length > 0 && name.Length < 40) found.Add(name);
            }
            return found.Count == 0 ? null : found.ToArray();
        }

        // ---------- A fond name on the end, and how it is said ----------
        // A fond tail lands warmer and slower. A voice that acts cues gets "(tender)" before it mid-line; every other
        // voice warms the whole line by half a cue, since a tail can't be voiced apart.
        private static readonly string[] Common =
        {
            "darling", "darlin", "love", "lover", "babe", "baby", "honey", "hun", "sweetheart", "sweetie",
            "sweets", "dear", "cutie", "princess", "prince", "angel", "kitten", "puppy", "pup", "boss",
            "chief", "captain", "choom", "mate", "doll", "treasure", "pumpkin", "bub", "bubba", "handsome",
            "gorgeous", "beautiful", "trouble", "stranger", "champ", "tiger", "sunshine"
        };

        // Praise, which is the other shape of it: the stress belongs on the second word.
        private static readonly Regex Praised = new Regex(
            @"(?:\bsuch\s+a\s+|\bwhat\s+a\s+|\bthere'?s\s+a\s+|\bthat'?s\s+(?:my\s+)?)?\bgood\s+(?:boy|girl|lad|lass|pup|puppy|kitten|one|human|choom|boi|job|work)\b[\s\p{P}]*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Where a fond ending starts in the text, or -1. "my darling" and "you darling" go in whole, since the
        // word in front of it is part of how it is said.
        private static int FondAt(string text)
        {
            string said = text ?? "";
            if (said.Trim().Length == 0) return -1;
            Match praise = Praised.Match(said);
            if (praise.Success) return praise.Index;
            // The last word, with its punctuation and any emote markers off it.
            Match last = Regex.Match(said, @"([\p{L}\p{Nd}'-]+)[\s\p{P}*]*$");
            if (!last.Success) return -1;
            string word = last.Groups[1].Value.Trim(new[] { '\'', '-' }).ToLowerInvariant();
            if (word.Length == 0) return -1;
            bool fond = false;
            foreach (string one in Common) if (word == one) { fond = true; break; }
            if (!fond)
            {
                string[] theirs = Fond;
                if (theirs != null)
                    foreach (string one in theirs)
                        if (word == (one ?? "").Trim().ToLowerInvariant()) { fond = true; break; }
            }
            if (!fond)
                foreach (string one in Mates) if (word == (one ?? "").Trim().ToLowerInvariant()) { fond = true; break; }
            if (!fond) return -1;
            int at = last.Groups[1].Index;
            // The small word in front belongs with it.
            Match lead = Regex.Match(said.Substring(0, at), @"\b(my|you|oh|hey|ah)\s+$", RegexOptions.IgnoreCase);
            return lead.Success ? lead.Index : at;
        }

        // The cue a fond ending wants, or null when the line does not end in one. Praise is proud, which is
        // louder and a touch slower; a pet name is tender, which is softer, slower and a shade higher.
        internal static string TailCue(string said)
        {
            try
            {
                string text = said ?? "";
                if (FondAt(text) < 0) return null;
                return Praised.IsMatch(text) ? "proud" : "tender";
            }
            catch (Exception) { return null; }
        }

        // The same for a voice that acts cues: the cue goes before the fond part, where it can act on it, or at the
        // front of a line that is only the fond name. Left alone when a cue is already there.
        internal static string Shaded(string text, string cue)
        {
            try
            {
                if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(cue)) return text;
                int at = FondAt(text);
                if (at < 0) return text;
                string before = text.Substring(0, at);
                // Already shaded, or her mood or her own cue is sitting there: those win, since one is about
                // this line and this is only about how it ends.
                if (Regex.IsMatch(before, @"[\(\[][^\)\]]*[\)\]][ 	]*$")) return text;
                if (at == 0 && Regex.IsMatch(text, @"^[ 	]*[\(\[]")) return text;
                return before + "(" + cue + ") " + text.Substring(at);
            }
            catch (Exception) { return text; }
        }

        // The fond names in play: theirs from me.md, else her personality's. Her brain needs these as a sentence,
        // because the raw "- petNames=" line in her notes told it nothing and her notes say not to recite them.
        internal static string[] Fond
        {
            get
            {
                try { return Theirs() ?? Personalities.PetNames(); }
                catch (Exception) { return null; }
            }
        }

        // Theirs if they set any, else her personality's own, else nothing. Her neutral pool still gets a turn either
        // way: a fond name every single time stops reading as fond and starts reading as a tic.
        private static string Mate
        {
            get
            {
                string[] fond = null;
                try { fond = Theirs() ?? Personalities.PetNames(); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                lock (gate)
                {
                    if (fond != null && fond.Length > 0 && dice.Next(3) > 0) return fond[dice.Next(fond.Length)];
                    return Mates[dice.Next(Mates.Length)];
                }
            }
        }

        // Always a fond one: theirs from me.md, else her personality's, and only her neutral pool when neither
        // has any, because a line built around a pet name still has to say something.
        private static string Pet
        {
            get
            {
                string[] fond = null;
                try { fond = Theirs() ?? Personalities.PetNames(); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                lock (gate)
                {
                    if (fond != null && fond.Length > 0) return fond[dice.Next(fond.Length)];
                    return Mates[dice.Next(Mates.Length)];
                }
            }
        }

        // What they call her, for lines where she repeats it: one of hers, her other side's added when it is on.
        internal static string[] CapriPets = { "girl", "kitten", "sweetheart", "princess" };
        internal static string[] SenpaiCapriPets = new string[0];

        internal static string[] CapriPetsNow
        {
            get
            {
                bool senpai = false;
                try { senpai = Brain.SenpaiLive; } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                if (!senpai || SenpaiCapriPets.Length == 0) return CapriPets;
                List<string> all = new List<string>(CapriPets);
                all.AddRange(SenpaiCapriPets);
                return all.ToArray();
            }
        }

        private static string CapriPet { get { string[] all = CapriPetsNow; lock (gate) return all[dice.Next(all.Length)]; } }

        // The words for a token in a phrase they might say to her, for matching what they said: her pet names for
        // %capriPetName%, boy and girl for %gender%. Null when the phrase holds neither.
        internal static string[] Heard(string phrase)
        {
            if (string.IsNullOrEmpty(phrase) || phrase.IndexOf('%') < 0) return null;
            List<string> made = new List<string> { phrase.ToLowerInvariant() };
            List<string> next = new List<string>();
            foreach (KeyValuePair<string, string[]> token in new[] {
                new KeyValuePair<string, string[]>("%capripetname%", CapriPetsNow), new KeyValuePair<string, string[]>("%gender%", new[] { "boy", "girl" }),
                new KeyValuePair<string, string[]>("%boy%", new[] { "boy", "girl" }) })
            {
                next.Clear();
                foreach (string one in made)
                    if (one.IndexOf(token.Key, StringComparison.Ordinal) < 0) next.Add(one);
                    else foreach (string word in token.Value) next.Add(one.Replace(token.Key, word.ToLowerInvariant()));
                made = new List<string>(next);
            }
            return made.Count == 1 && made[0] == phrase.ToLowerInvariant() ? null : made.ToArray();
        }

#if CHECKS
        // %gender% is boy or girl by their pronouns and one of their pet names otherwise, never "one" or "they"; %boy% the
        // same; %userPetName% and %capriPetName% filled; a phrase with %capriPetName% matches "good girl".
        internal static void TokensCheck(List<string> checks, string folder)
        {
            string was = Watched;
            string me = Path.Combine(folder, "tokens-me-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".md");
            try
            {
                File.WriteAllText(me, "# Me\r\n\r\n## About you\r\n\r\n- Name: Sam\r\n- Pronouns: she/her\r\n");
                Watch(me);
                string she = Fill("Good %gender%."), old = Fill("Good %boy%.");
                File.WriteAllText(me, "# Me\r\n\r\n## About you\r\n\r\n- Name: Sam\r\n- Pronouns: they/them\r\n");
                File.SetLastWriteTimeUtc(me, DateTime.UtcNow.AddMinutes(1));
                Watch(me);
                string they = Fill("Good %gender%.");
                string hers = Fill("Who's my good %capriPetName%?"), theirs = Fill("Hi, %userPetName%.");
                string[] heard = Heard("good %capriPetName%");
                bool right = she == "Good girl." && old == "Good girl." && they.IndexOf('%') < 0 && they != "Good one." && they != "Good they." && they.Length > 6
                    && Array.Exists(CapriPets, n => hers == "Who's my good " + n + "?") && theirs.IndexOf('%') < 0 && theirs.Length > 5
                    && heard != null && Array.IndexOf(heard, "good girl") >= 0 && Heard("thank you") == null;
                if (!right) throw new Exception("Her tokens went wrong: she/her " + she + " and " + old + ", they/them " + they + ", her pet name " + hers + ", theirs " + theirs + ", heard " + (heard == null ? "none" : string.Join("|", heard)));
                checks.Add("PASS %gender% is girl for she/her (%boy% the same) and one of their pet names for they/them (\"" + they + "\"), never \"one\" or \"they\"; %userPetName% and %capriPetName% filled; \"good %capriPetName%\" matches good girl");
            }
            finally
            {
                Watch(was);
                try { File.Delete(me); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
        }
#endif

        //   %petname%  always one of their pet names, for lines written around one
        // Censored keeps it to shins whatever the pronouns say.
        private static string Bits
        {
            get
            {
                bool senpai = false;
                try { senpai = Brain.SenpaiOn; } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                if (!senpai) return "shins";
                return Extended.Get("bits." + bits) ?? "shins";
            }
        }

        // One keyed line from me.md, however they wrote it: with or without a bullet, dash or star, colon or equals,
        // spaced or camelCased. A ">" line is a note and never matches.
        internal static string Keyed(string notes, string key)
        {
            if (string.IsNullOrEmpty(notes)) return "";
            try
            {
                Match found = Regex.Match(notes, @"^[ \t]*(?:[-*•][ \t]*)?(?:" + key + @")[ \t]*[=:][ \t]*(.+)$",
                    RegexOptions.Multiline | RegexOptions.IgnoreCase);
                return found.Success ? found.Groups[1].Value.Trim() : "";
            }
            catch (Exception) { return ""; }
        }

        private static void Fresh()
        {
            string where;
            lock (gate) where = path;
            if (string.IsNullOrEmpty(where)) return;
            try
            {
                // Asked on every tick, so whether it changed comes from memory; it is read again only when it did.
                long stamp = Stamps.Of(where);
                if (stamp == 0) return;
                lock (gate) { if (stamp == wasStamp) return; }
                string notes = File.ReadAllText(where);
                string said = Keyed(notes, @"pronouns?").ToLowerInvariant();
                string called = Keyed(notes, @"name|call\s+(?:them|me)|they\s+go\s+by|go\s+by");
                // Their own hours, written as awakeHours in me.md. Read here because this already watches that file
                // and re-reads it only when it changes.
                string up = Keyed(notes, @"awake\s*_?-?\s*hours?");
                // What they would like to be called, same file, same read.
                string fond = Keyed(notes, @"pet\s*_?-?\s*names?");

                lock (gate)
                {
                    name = called;
                    awake = up;
                    pets = fond;
                    // "any pronouns" and anything unreadable both land on neutral on purpose.
                    if (Regex.IsMatch(said, @"\bhe\b|\bhim\b|\bhis\b"))
                        { he = "he"; them = "him"; their = "his"; boy = "boy"; isAre = "is"; bits = "lower mid a"; }
                    else if (Regex.IsMatch(said, @"\bshe\b|\bher\b|\bhers\b"))
                        { he = "she"; them = "her"; their = "her"; boy = "girl"; isAre = "is"; bits = "top mid"; }
                    // "any pronouns", anything unreadable, and nothing at all all land here on purpose.
                    else { he = "they"; them = "them"; their = "their"; boy = ""; isAre = "are"; bits = "lower mid b"; }
                    wasStamp = stamp;
                }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }
    }
}
