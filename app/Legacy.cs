using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // Names her files used in earlier versions, so a file or folder written under one is still read and renamed.
    internal static class Legacy
    {
        // The heading tag and the whole-file marker her files carry now.
        internal const string Mark = "overdrive";
        internal const string OnlyKey = "overdrive only";

        private static readonly HashSet<string> Words = new HashSet<string>(StringComparer.Ordinal)
        {
            "0a4ff57f817a36cb24eeeb2b9149d8dc9de8150aafd5f6440453d17d2d61ab47",
            "7fdc3e8038e87ff56a0e429878710670c2b28ceaf3ece041f2e7e7866b002adb",
            "c6603565c5159fbe846a53e991829d452a1546d41150c0d3c73ddbd7f476ee0d",
        };

        private static readonly HashSet<string> OnlyKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "c01ec759f3a360a86ead9369bceb4f14c6db8b4c6c061ffc0c3118477c8364ea",
            "bdadb8c95f26e50e47da5e028a1b7e97c85d9e864987f498500542aa12a1f76f",
            "9a99ac8a1868d9b3a5df17e567bd34cfee45bf34de672b32dff6ab4f6afb3c94",
        };

        // sys.md's old heading over the terms, and its old answer key.
        private const string OldHead = "f8035300c02ffb3d21afc8e79a115c07df988ff027befa21c6c3943c62a0d52e";
        private const string OldKey = "0a4ff57f817a36cb24eeeb2b9149d8dc9de8150aafd5f6440453d17d2d61ab47";

        // Checked against a freshly written sys.md: single words and two-word phrases.
        private static readonly HashSet<string> Unsaid = new HashSet<string>(StringComparer.Ordinal)
        {
            "f8035300c02ffb3d21afc8e79a115c07df988ff027befa21c6c3943c62a0d52e",
            "0a4ff57f817a36cb24eeeb2b9149d8dc9de8150aafd5f6440453d17d2d61ab47",
            "e683679358a5aea345d8a6084369eae87b0a3db298631dd14aed132f84d3b4e8",
            "356e06027fe19376d7091e53b4826c3d9909d3bcd58866ec2168423c81fc5dd8",
            "102626eace2d12939928583069428230227ca3b4ac67e622d0cec1ee9bd6e5b9",
            "f775d3ce7f03130e499a0f70f4e192ec329dd62fdcd6ed64d5d56f285e346f27",
            "7fdc3e8038e87ff56a0e429878710670c2b28ceaf3ece041f2e7e7866b002adb",
            "c6603565c5159fbe846a53e991829d452a1546d41150c0d3c73ddbd7f476ee0d",
        };

        internal static string Hash(string text)
        {
            byte[] sum;
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
                sum = sha.ComputeHash(Encoding.UTF8.GetBytes((text ?? "").Trim().ToLowerInvariant()));
            StringBuilder hex = new StringBuilder(sum.Length * 2);
            foreach (byte b in sum) hex.Append(b.ToString("x2"));
            return hex.ToString();
        }

        // The tag word, now or as an earlier version wrote it.
        internal static bool IsMark(string word)
        {
            if (string.IsNullOrEmpty(word)) return false;
            string own = Extended.Get("tag");
            return word.Trim().Equals(Mark, StringComparison.OrdinalIgnoreCase) || (own != null && word.Trim().Equals(own, StringComparison.OrdinalIgnoreCase)) || Words.Contains(Hash(word));
        }

        // An old name for the senpai folder.
        internal static bool WasFolder(string name) { return !string.IsNullOrEmpty(name) && Words.Contains(Hash(name)); }

        // The whole-file marker's key, now or old.
        internal static bool IsOnlyKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            string own = Extended.Get("only");
            return key.Trim().Equals(OnlyKey, StringComparison.OrdinalIgnoreCase) || (own != null && key.Trim().Equals(own, StringComparison.OrdinalIgnoreCase)) || OnlyKeys.Contains(Hash(key));
        }

        // Names an act and its sections had before, and what they are called now.
        private static readonly Dictionary<string, string> KeyRenames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "3c8cab8b47d6d0ac0e1bb0a3c209b836f5cf1a61e851626dc7a5d7f011a7e5dd", "insulted" },
        };

        private static readonly Dictionary<string, string> HeadRenames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "1aa6060806aa9e828c189363602cc8652a20188463e83d1f9b9836ad8b230aa1", "insulted likes it" },
            { "bb972775c6da14602e8a0d81be7819c80415a98f0f305ab0d867bbb9b3606fb0", "insulted dislikes it" },
        };

        // Whole lines a safe copy carried that named a pack the safe side does not have, and what they read now.
        private static readonly Dictionary<string, string> PlainLines = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "22b79123247bdd9d90d974eef34ed1b29f034e16f86dbcbefc7afcf4de95897b", "ignored over 16: Emo" },
            { "3365bbae85febaf12bba1fd7b1b4899813cd7a8a48ea827e5b5d36fdcd1377d5", "Where Bratty demands and others deny, you simply have a comment. You are the one who says the thing everyone" },
            { "27e38af60515e81000bcec7fc43175c68635be9f9d0930a75ec315dae071035d", ">! %bits% follows the pronouns in me.md, and is shins while censored." },
        };

        // A score line for an act that only exists with the switch on, which has no place in a censored file.
        private const string OldClickAct = "2230cc56834d4d87b011d7073e56a14ec2bee6908dc27c809eb78dd8fc6a3acd";
        private const string OldClickLabel = "54c8cf73f86b8982d905585d7a54a105c257f121b0c2bf2b9998c59e755276c6";

        internal static bool WasLabel(string named) { return !string.IsNullOrEmpty(named) && Hash(named) == OldClickLabel; }

        internal static bool WasSysKey(string key) { return !string.IsNullOrEmpty(key) && Hash(key) == OldKey; }
        internal static bool WasSysHead(string heading) { return !string.IsNullOrEmpty(heading) && Hash(heading) == OldHead; }

        private static readonly Regex Tagged = new Regex(@"^(.*?)[\s,]*\(?\b([A-Za-z]+)\b\)?\s*$");

        // A heading that ends in the tag, with or without brackets: true, and the heading without it. A heading
        // that is only the word is a section named that, not a tagged one.
        internal static bool Heading(string heading, out string bare)
        {
            bare = heading;
            if (string.IsNullOrEmpty(heading)) return false;
            Match m = Tagged.Match(heading.Trim());
            if (!m.Success || m.Groups[1].Value.Trim().Length == 0 || !IsMark(m.Groups[2].Value)) return false;
            bare = m.Groups[1].Value.Trim();
            return true;
        }

        internal static bool Heading(string heading) { string bare; return Heading(heading, out bare); }

        private static readonly Regex Bracketed = new Regex(@"\(([A-Za-z]+)\)");

        // Anywhere in a line, the tag in brackets.
        internal static bool HasTag(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (Match m in Bracketed.Matches(text)) if (IsMark(m.Groups[1].Value)) return true;
            return false;
        }

        private static readonly Regex OnlyLine = new Regex(@"(?mi)^[ \t]*-?[ \t]*([A-Za-z][A-Za-z ]*?)[ \t]*:[ \t]*(yes|true|on|1)[ \t\r]*$");

        // A file that says it is senpai as a whole.
        internal static bool MarkedOnly(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (Match m in OnlyLine.Matches(text)) if (IsOnlyKey(m.Groups[1].Value)) return true;
            return false;
        }

        // A line that starts with the whole-file marker's key.
        internal static bool StartsOnly(string line)
        {
            int colon = (line ?? "").IndexOf(':');
            return colon > 0 && IsOnlyKey(line.Substring(0, colon));
        }

        private static readonly Regex HeadingLine = new Regex(@"^([ \t]*#+[ \t]*)(.*?)([ \t]*)$");
        private static readonly Regex OnlyKeyLine = new Regex(@"^([ \t]*-?[ \t]*)([A-Za-z][A-Za-z ]*?)([ \t]*:.*)$");

        // Text with every old tag and old marker key written the way they are written now. Unchanged text comes
        // back as the same string.
        internal static string Modern(string text) { return Modern(text, false); }

        // The same, and on the censored side a score line for an act that side does not have is taken out.
        internal static string Modern(string text, bool plainSide)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string nl = text.Contains("\r\n") ? "\r\n" : "\n";
            string[] rows = text.Replace("\r\n", "\n").Split('\n');
            bool changed = false;
            for (int i = 0; i < rows.Length; i++)
            {
                string row = rows[i];
                Match h = HeadingLine.Match(row);
                if (h.Success && row.TrimStart().StartsWith("#", StringComparison.Ordinal))
                {
                    string bare;
                    bool tagged = Heading(h.Groups[2].Value, out bare);
                    if (!tagged) bare = h.Groups[2].Value.Trim();
                    string renamed = Extended.Get("alias." + bare.ToLowerInvariant());
                    if (renamed != null || HeadRenames.TryGetValue(Hash(bare), out renamed)) bare = renamed;
                    string now = h.Groups[1].Value + bare + (tagged ? " (" + Mark + ")" : "");
                    if (now != row && (tagged || renamed != null)) { rows[i] = now; changed = true; }
                    continue;
                }
                string plainNow;
                if (plainSide && row.Trim().Length > 0 && PlainLines.TryGetValue(Hash(row), out plainNow))
                {
                    rows[i] = row.Substring(0, row.Length - row.TrimStart().Length) + plainNow;
                    changed = true;
                    continue;
                }
                Match k = OnlyKeyLine.Match(row);
                if (!k.Success) continue;
                string key = k.Groups[2].Value.Trim(), hash = Hash(key), into;
                if (!key.Equals(OnlyKey, StringComparison.OrdinalIgnoreCase) && OnlyKeys.Contains(hash))
                {
                    rows[i] = k.Groups[1].Value + OnlyKey + k.Groups[3].Value;
                    changed = true;
                }
                else if ((into = Extended.Get("alias." + key.ToLowerInvariant())) != null || KeyRenames.TryGetValue(hash, out into))
                {
                    rows[i] = k.Groups[1].Value + into + k.Groups[3].Value;
                    changed = true;
                }
                else if (plainSide && hash == OldClickAct && Regex.IsMatch(k.Groups[3].Value, @"^[ \t]*:[ \t]*[-+]?[0-9]+[ \t\r]*$"))
                {
                    rows[i] = null;
                    changed = true;
                }
            }
            if (!changed) return text;
            List<string> kept = new List<string>();
            foreach (string row in rows) if (row != null) kept.Add(row);
            return string.Join(nl, kept.ToArray());
        }

        // Every file under her settings folder, old folder names renamed and old tags rewritten. Backups are
        // left as they were.
        internal static void Migrate(string settings)
        {
            if (string.IsNullOrEmpty(settings) || !System.IO.Directory.Exists(settings)) return;
            try
            {
                Personalities.RenamedFolder(settings);
                foreach (string dir in System.IO.Directory.GetDirectories(settings)) Personalities.RenamedFolder(dir);
                foreach (string file in System.IO.Directory.GetFiles(settings, "*", System.IO.SearchOption.AllDirectories))
                {
                    if (file.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        System.IO.FileInfo about = new System.IO.FileInfo(file);
                        if (about.Length > 4 * 1024 * 1024) continue;
                        string had = System.IO.File.ReadAllText(file);
                        if (had.IndexOf('\0') >= 0) continue;
                        bool plain = file.IndexOf(System.IO.Path.DirectorySeparatorChar + Personalities.SenpaiName + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) < 0;
                        string made = Modern(had, plain);
                        if (!ReferenceEquals(made, had) && made != had) System.IO.File.WriteAllText(file, made, new UTF8Encoding(false));
                    }
                    catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // Whether text says any of what a fresh sys.md must not, word by word and in pairs.
        internal static bool SaysUnsaid(string text)
        {
            string[] words = Regex.Split((text ?? "").ToLowerInvariant(), @"[^a-z\-]+");
            for (int i = 0; i < words.Length; i++)
            {
                if (words[i].Length == 0) continue;
                if (Unsaid.Contains(Hash(words[i]))) return true;
                if (i + 1 < words.Length && Unsaid.Contains(Hash(words[i] + " " + words[i + 1]))) return true;
            }
            return false;
        }
    }
}
