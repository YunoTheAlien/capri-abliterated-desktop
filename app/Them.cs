using System;
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
        private static string he = "they", them = "them", their = "their", boy = "one", isAre = "are", bits = "crotch", name = "";

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

        private static string Mate { get { lock (gate) return Mates[dice.Next(Mates.Length)]; } }

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
                string said = "";
                Match found = Regex.Match(notes, @"^-\s*Pronouns:\s*(.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                if (found.Success) said = found.Groups[1].Value.Trim().ToLowerInvariant();
                string called = "";
                Match named = Regex.Match(notes, @"^-\s*(?:Name|Call them|They go by):\s*(.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                if (named.Success) called = named.Groups[1].Value.Trim();

                lock (gate)
                {
                    name = called;
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
