using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // The last pass over a line she has built, mending what joins break: a capital mid-sentence, doubled punctuation, a
    // space before a comma, "a" before a vowel, a word said twice. Only certain faults, so her voice is never changed.
    internal static class Grammar
    {
        internal sealed class Fault
        {
            internal string What;
            internal string Was;
            internal string Now;
        }

        // A mended line. Safe on anything: text with none of these faults comes back unchanged.
        internal static string Mend(string said)
        {
            List<Fault> found;
            return Mend(said, out found);
        }

        // A line picked whole has no seams, so a word said twice in it is meant: "Knock knock", "Nope nope nope".
        internal static string Mend(string said, bool whole)
        {
            List<Fault> found;
            return Mend(said, out found, whole);
        }

        internal static string Mend(string said, out List<Fault> found) { return Mend(said, out found, false); }

        private static string Mend(string said, out List<Fault> found, bool whole)
        {
            found = new List<Fault>();
            // A meme line is said exactly as written (Sayings.MemeMark).
            if (string.IsNullOrEmpty(said) || said.TrimStart().StartsWith("~", StringComparison.Ordinal)) return said;
            string was = said;

            // A space before punctuation that never takes one. An ellipsis opening a phrase keeps the space before it.
            said = Note(found, "a space before punctuation", was, Regex.Replace(said, @"[ \t]+([,;:!?]|\.(?!\.))", "$1"));
            // Two spaces where one belongs. Not at the start of a line: her .md files use indents of their own.
            was = said;
            said = Note(found, "a double space", was, Regex.Replace(said, @"(?<=\S)[ \t]{2,}(?=\S)", " "));
            // Punctuation doubled at a seam. An ellipsis and an interrobang are deliberate and are left alone.
            was = said;
            said = Note(found, "doubled punctuation", was, Doubled(said));
            // A comma or a semicolon with nothing after it, which is how a fragment ends when its tail is gone.
            was = said;
            said = Note(found, "a line ending in a comma", was, Regex.Replace(said, @"[,;]+\s*$", "."));
            // A capital in the middle of a sentence, which is a core or a tail that was written to start one.
            was = said;
            said = Note(found, "a capital mid-sentence", was, Lowered(said));
            // The same word twice across a join: "and and", "you you".
            was = said;
            if (!whole) said = Note(found, "a word said twice", was, Regex.Replace(said, @"(?<![\p{L}'])(\p{L}{1,12})([ \t]+)\1(?![\p{L}'])", "$1", RegexOptions.IgnoreCase));
            // "a" in front of a vowel, and "an" in front of a consonant.
            was = said;
            said = Note(found, "a or an", was, Articles(said));
            // No space after a comma or a full stop. Left alone inside a number, a time, a file name or a
            // web address, where the dot is not punctuation at all.
            was = said;
            said = Note(found, "no space after punctuation", was,
                Regex.Replace(said, @"(?<![\d\p{L}]\.[\p{L}\d]{0,6})([,;])(?=\p{L})", "$1 "));
            was = said;
            // The last dot of an ellipsis is not a full stop: "...Fine." is written that way.
            said = Note(found, "no space after a full stop", was,
                Regex.Replace(said, @"(?<!\p{Lu})(?<!\d)(?<!\.)\.(?=\p{Lu}\p{Ll})", ". "));
            // A lower case letter starting a sentence.
            was = said;
            said = Note(found, "a sentence not starting with a capital", was,
                Regex.Replace(said, @"(?<!\.\.[ \t]+)(?<=[.!?][ \t]+)(\p{Ll})", delegate(Match found2) { return found2.Value.ToUpperInvariant(); }));
            // The word I, on its own, in lower case.
            was = said;
            said = Note(found, "a lower case I", was, Regex.Replace(said, @"(?<![\p{L}'])i(?=[ \t]|'\p{L})", "I"));

            return said;
        }

        // What is wrong with a piece of writing, without changing it. The same rules, reported rather than applied.
        internal static List<Fault> Faults(string said)
        {
            List<Fault> found;
            Mend(said, out found);
            return found;
        }

        // A look over somebody's writing in her voice: what she found and a mended copy, the original left alone. The
        // same rules she holds herself to; no brain and nothing sent.
        internal static string Checked(string text, out string mended)
        {
            mended = text;
            if (string.IsNullOrWhiteSpace(text)) return "Give me something to read first.";
            List<Fault> found;
            mended = Mend(text, out found);
            if (found.Count == 0) return "Had a read. Nothing wrong with it, choom.";
            List<string> named = new List<string>();
            foreach (Fault one in found) if (!named.Contains(one.What)) named.Add(one.What);
            string list = named.Count == 1 ? named[0]
                : string.Join(", ", named.GetRange(0, named.Count - 1).ToArray()) + " and " + named[named.Count - 1];
            return (found.Count == 1 ? "One thing: " : found.Count + " things: ") + list + ". Fixed copy is on your clipboard.";
        }

        private static string Note(List<Fault> found, string what, string was, string now)
        {
            if (!string.Equals(was, now, StringComparison.Ordinal)) found.Add(new Fault { What = what, Was = was, Now = now });
            return now;
        }

        private static string Doubled(string said)
        {
            // Held out of the way, mended, then put back, so "..." and "?!" survive a pass that collapses runs.
            string kept = said.Replace("...", "\u0001").Replace("?!", "\u0002").Replace("!?", "\u0003");
            kept = Regex.Replace(kept, @"([,.;:!?])[ \t]*([,.;:!?])+", "$1");
            return kept.Replace("\u0001", "...").Replace("\u0002", "?!").Replace("\u0003", "!?");
        }

        // A capital after something that is not the end of a sentence. Her own words keep theirs: a name, a
        // Night City word, the word I, and anything written in shouting capitals on purpose.
        private static string Lowered(string said)
        {
            return Regex.Replace(said, @"(?<=[,;:][ \t]+)(\p{Lu}\p{Ll}+)", delegate(Match found)
            {
                string word = found.Groups[1].Value;
                return Proper(word) ? word : char.ToLowerInvariant(word[0]) + word.Substring(1);
            });
        }

        private static bool Proper(string word)
        {
            if (string.IsNullOrEmpty(word)) return true;
            if (word == "I" || word.StartsWith("I'", StringComparison.Ordinal)) return true;
            if (word.Length > 1 && word == word.ToUpperInvariant()) return true;
            string plain = word.ToLowerInvariant();
            foreach (string known in Lint.Known) if (plain == known) return true;
            foreach (string named in Names) if (plain == named) return true;
            return false;
        }

        // Words that keep a capital wherever they land. Her own name is in Lint's list already.
        private static readonly string[] Names =
        {
            "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday",
            "january", "february", "march", "april", "june", "july", "august", "september", "october",
            "november", "december", "windows", "discord", "twitch", "spotify", "steam", "youtube", "chrome",
            "firefox", "google", "ollama", "night"
        };

        // Sounds, not letters: an hour, a user, an MP3. Only the cases that are certain, and anything else is
        // left exactly as it was written.
        private static readonly string[] SoundsVowel = { "hour", "honest", "honour", "honor", "heir" };
        private static readonly string[] SoundsConsonant = { "user", "university", "unicorn", "uniform", "union", "unique", "european", "one", "once", "useful" };

        private static string Articles(string said)
        {
            return Regex.Replace(said, @"(?<![\p{L}'])(a|an|A|An)([ \t]+)(\p{L}[\p{L}'-]*)", delegate(Match found)
            {
                string article = found.Groups[1].Value, gap = found.Groups[2].Value, word = found.Groups[3].Value;
                string plain = word.ToLowerInvariant();
                bool vowel = "aeiou".IndexOf(plain[0]) >= 0;
                foreach (string odd in SoundsVowel) if (plain == odd || plain.StartsWith(odd, StringComparison.Ordinal)) vowel = true;
                foreach (string odd in SoundsConsonant) if (plain == odd || plain.StartsWith(odd, StringComparison.Ordinal)) vowel = false;
                bool upper = char.IsUpper(article[0]);
                string want = vowel ? "an" : "a";
                if (string.Equals(article, want, StringComparison.OrdinalIgnoreCase)) return found.Value;
                return (upper ? char.ToUpperInvariant(want[0]) + want.Substring(1) : want) + gap + word;
            });
        }
    }
}
