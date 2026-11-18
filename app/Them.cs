using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // Who she is talking to, for the lines she says as written rather than through her brain.
    //
    // Her personality files are fixed text, so "good girl" is wrong for half the people who install her and writing
    // "good boy. Or girl." is worse than either. The lines carry tokens instead and this fills them in
    // from the "- Pronouns:" line already in me.md.
    //
    //   %boy%    boy / girl / one          %them%   him / her / them
    //   %he%     he / she / they           %their%  his / her / their
    //   %is%     is / is / are             %name%   what she calls them
    //   %bits%   balls / boob / crotch, or shins while censored
    //   %mate%   a neutral way to address them, from a shared pool
    //
    // %bits% exists because she is allowed to threaten to kick you somewhere and not everyone has the same
    // somewhere. It follows the pronouns in me.md, falls back to the general "crotch" when they have not said, and
    // stays on "shins" entirely while censored, so the same written line is fine in both modes.
    //
    // %is% exists because "they" takes a plural verb: "%He% %is% late" has to come out as "They are late", not
    // "They is late".
    //
    // Anything it cannot work out goes neutral, which reads properly for everyone rather than guessing wrong.
    internal static class Them
    {
        private static readonly object gate = new object();
        private static string path;
        private static DateTime read = DateTime.MinValue;
        private static long wasLong = -1;
        private static string he = "they", them = "them", their = "their", boy = "one", isAre = "are", bits = "crotch", name = "", awake = "", pets = "";

        // What they wrote for awakeHours in me.md, or empty. Hours prefers this over what it counted.
        internal static string Awake { get { Fresh(); lock (gate) return awake; } }

        // What they are called, from me.md. Used to take their name back out of anything written to a file.
        internal static string Called { get { Fresh(); lock (gate) return name; } }

        internal static void Watch(string mePath) { lock (gate) { path = mePath; read = DateTime.MinValue; wasLong = -1; } }

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
                if (put.Length > 0 && at + 1 < line.Length && char.IsUpper(line[at + 1]))
                    put = char.ToUpperInvariant(put[0]) + put.Substring(1);
                line = line.Substring(0, at) + put + line.Substring(at + token.Length);
                at = line.IndexOf(token, at + put.Length, StringComparison.OrdinalIgnoreCase);
            }
            return line;
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
        //
        // "You look busy, darling" is not said the way "You look busy" is. The fond part lands warmer and
        // slower, dragged out, and praise does the opposite with its second word: good BOY, not good boy.
        // Flat delivery over the whole line is what made her pet names read as a tic.
        //
        // Two things happen with this, because her voices are not alike:
        //
        //   a voice that performs cues gets "(tender)" put in front of the fond part, mid-line, and acts it
        //   every other voice has the line's own pitch and pace, so the whole line is warmed by half a cue
        //
        // The second is weaker on purpose: nothing but a call voices one answer in pieces, so a tail cannot
        // have a voice of its own without splitting every line she says. Half a cue over the line is honest
        // and audible; a tail of its own would need her piece voicing generalised first.
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

        // The same thing for a voice that performs cues: the cue goes in front of the fond part, where it can
        // act on it, rather than in front of the line. A line that is nothing but the fond name gets it at the
        // front, which is the same place and still right. Left alone when a cue is already there.
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
                try { senpai = Brain.Uncensored; } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                if (!senpai) return "shins";
                return bits;
            }
        }

        // One keyed line out of me.md, written however they happened to write it.
        //
        // This file is edited by hand, so it comes back in every shape: with the bullet or without it, a dash or a
        // star, spaces in front, a colon or an equals, "pet names" or "petNames". The readers each wanted one exact
        // shape and quietly found nothing otherwise, which reads as her ignoring what they wrote.
        //
        // A line starting with ">" is a note to them, not data, so it never matches.
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
                FileInfo about = new FileInfo(where);
                if (!about.Exists) return;
                lock (gate) { if (about.LastWriteTimeUtc == read && about.Length == wasLong) return; }
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
                        { he = "he"; them = "him"; their = "his"; boy = "boy"; isAre = "is"; bits = "balls"; }
                    else if (Regex.IsMatch(said, @"\bshe\b|\bher\b|\bhers\b"))
                        { he = "she"; them = "her"; their = "her"; boy = "girl"; isAre = "is"; bits = "boob"; }
                    // "any pronouns", anything unreadable, and nothing at all all land here on purpose.
                    else { he = "they"; them = "them"; their = "their"; boy = "one"; isAre = "are"; bits = "crotch"; }
                    read = about.LastWriteTimeUtc;
                    wasLong = about.Length;
                }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }
    }
}
