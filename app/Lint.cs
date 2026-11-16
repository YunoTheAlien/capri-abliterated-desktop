using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // Checks the line pools join cleanly, so a bad fragment is caught where it is written instead of turning up
    // mid-sentence. Runs on start and writes what it finds to errors.txt.
    //
    // Not a grammar checker. It looks for the handful of faults that actually show: a core that cannot follow an
    // opener, punctuation that will double up, a tail that reads as its own sentence, and tokens that will not fill.
    internal static class Lint
    {
        // Night City words, so they are not flagged as typos or sentence starts.
        internal static readonly string[] Known =
        {
            "choom", "chooms", "gonk", "gonks", "gronk", "preem", "nova", "delta", "flatline", "braindance",
            "ripperdoc", "netrunner", "edgerunner", "corpo", "chrome", "chromed", "eddies", "solo", "nomad",
            "arasaka", "militech", "kiroshi", "scav", "scavs", "borg", "cyberpsycho", "input", "merc", "zero",
            "capri", "capricorn", "kyoko"
        };

        private static readonly Regex Token = new Regex(@"%[a-z]+%", RegexOptions.IgnoreCase);

        private static readonly string[] Tokens =
        {
            "%boy%", "%he%", "%them%", "%their%", "%is%", "%name%", "%bits%", "%mate%", "%petname%", "%it%"
        };

        // Fault lines, worst first. Empty when the pools are clean.
        internal static List<string> Check(Dictionary<string, string[]> pools) { return Check(pools, true); }

        // joined: these lines are fragments that get stuck after an opener, so capitals and trailing
        // punctuation matter. A personality pack writes whole sentences instead, which are correct as they
        // are, so it passes false and only the faults that are wrong either way are checked.
        internal static List<string> Check(Dictionary<string, string[]> pools, bool joined)
        {
            List<string> found = new List<string>();
            if (pools == null) return found;

            foreach (KeyValuePair<string, string[]> pool in pools)
            {
                string name = pool.Key;
                if (pool.Value == null) continue;
                bool core = joined && name.IndexOf("likes it", StringComparison.OrdinalIgnoreCase) >= 0;
                bool tail = joined && name.StartsWith("tails", StringComparison.OrdinalIgnoreCase);
                bool opener = joined && name.StartsWith("openers", StringComparison.OrdinalIgnoreCase);

                foreach (string raw in pool.Value)
                {
                    string one = (raw ?? "").Trim();
                    if (one.Length == 0) continue;

                    foreach (Match mark in Token.Matches(one))
                    {
                        bool good = false;
                        foreach (string ok in Tokens) if (string.Equals(ok, mark.Value, StringComparison.OrdinalIgnoreCase)) { good = true; break; }
                        if (!good) found.Add(name + ": unknown token " + mark.Value + " in \"" + one + "\"");
                    }

                    // A core is joined after an opener, so it must not start a sentence of its own or end one.
                    if (core)
                    {
                        if (char.IsUpper(one[0]) && !StartsKnown(one))
                            found.Add(name + ": core starts with a capital, so it reads as a new sentence: \"" + one + "\"");
                        char last = one[one.Length - 1];
                        if (last == '.' || last == ',')
                            found.Add(name + ": core ends with punctuation, which doubles up when a tail follows: \"" + one + "\"");
                    }

                    // A tail is joined after a core, usually with a comma, so a capital start fights it.
                    if (tail && char.IsUpper(one[0]) && !StartsKnown(one) && !one.TrimEnd().EndsWith("."))
                        found.Add(name + ": tail starts with a capital but is not a sentence: \"" + one + "\"");

                    if (opener)
                    {
                        char last = one[one.Length - 1];
                        if (char.IsLetter(last))
                            found.Add(name + ": opener has no punctuation to join on: \"" + one + "\"");
                    }

                    if (one.IndexOf("  ", StringComparison.Ordinal) >= 0)
                        found.Add(name + ": double space in \"" + one + "\"");
                    // An ellipsis and an interrobang are both deliberate, so only real doubling counts.
                    if (Regex.IsMatch(Regex.Replace(one, @"\.\.\.|[?!][?!]", ""), @"[,.!?]{2,}"))
                        found.Add(name + ": doubled punctuation in \"" + one + "\"");
                }
            }
            return found;
        }

        // A line beginning with a proper noun or a Night City word is allowed its capital.
        private static bool StartsKnown(string one)
        {
            Match first = Regex.Match(one, @"^[A-Za-z']+");
            if (!first.Success) return true;
            string word = first.Value;
            // Shouting is deliberate: WHEEE and AGAIN are written that way on purpose.
            if (word.Length > 1 && word == word.ToUpperInvariant()) return true;
            // "I", and every contraction of it, is always capitalised.
            if (word == "I" || word.StartsWith("I'", StringComparison.Ordinal)) return true;
            string plain = word.ToLowerInvariant();
            foreach (string ok in Known) if (plain == ok) return true;
            return plain.Length <= 1;
        }

        // Runs the check and logs a summary. Silent when the pools are clean.
        internal static void Sweep(Dictionary<string, string[]> pools, string what) { Sweep(pools, what, true); }

        internal static void Sweep(Dictionary<string, string[]> pools, string what, bool joined)
        {
            try
            {
                List<string> faults = Check(pools, joined);
                if (faults.Count == 0) return;
                StringBuilder said = new StringBuilder();
                said.Append(faults.Count).Append(" line faults in ").Append(what).Append(':');
                for (int at = 0; at < faults.Count && at < 12; at++) said.Append("\n  ").Append(faults[at]);
                if (faults.Count > 12) said.Append("\n  and ").Append(faults.Count - 12).Append(" more");
                Brain.Log("lines", -1, said.ToString());
            }
            catch (Exception) { }
        }
    }
}
