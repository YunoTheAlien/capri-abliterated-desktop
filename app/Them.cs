using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // Fills the tokens in her written lines from me.md's "- Pronouns:" line: %boy% %them% %he% %their% %is% (they are)
    // %name% %mate%, and %bits%, a zone by pronoun or "shins" while censored. Anything unclear goes neutral.
    internal static class Them
    {
        private static readonly object gate = new object();
        private static string path;
        private static long wasStamp = -1;
        private static string he = "they", them = "them", their = "their", boy = "one", isAre = "are", bits = "lower mid b", name = "", awake = "", pets = "";

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
                said = Swap(said, "%boy%", boy);
                said = Swap(said, "%he%", he);
                said = Swap(said, "%them%", them);
                said = Swap(said, "%their%", their);
                said = Swap(said, "%is%", isAre);
                said = Swap(said, "%bits%", Bits);
                said = Swap(said, "%mate%", Mate);
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
                    else { he = "they"; them = "them"; their = "their"; boy = "one"; isAre = "are"; bits = "lower mid b"; }
                    wasStamp = stamp;
                }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }
    }
}
