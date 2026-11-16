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
                try { fond = Theirs() ?? Personalities.PetNames(); } catch (Exception) { }
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
                try { fond = Theirs() ?? Personalities.PetNames(); } catch (Exception) { }
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
                try { senpai = Brain.Uncensored; } catch (Exception) { }
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
            catch (Exception) { }
        }
    }
}
