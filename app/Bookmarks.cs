using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // Their default browser's bookmarks, for a word that is no app: she asks "From your bookmarks?" before the domain
    // lookup. Read on this PC only and never kept; her brain, transcripts and a phone get the question without it.
    internal static class Bookmarks
    {
        internal sealed class Mark
        {
            internal string Title, Address;
        }

        // Tests swap these, so no check reads the bookmarks of the PC it runs on.
        internal static Func<string> Browser = Hands.DefaultBrowser;
        internal static Func<string, List<string>> FilesFor = Files;
        private static readonly Random dice = new Random();

        private static readonly string[] Lines = { "From your bookmarks?", "From your bookmarks, %petname%?" };

        // What Security.Bookmark wants: what to show, the address, and her line. Null when nothing is close.
        internal static string[] Ask(string word)
        {
            Mark found = Find(word);
            if (found == null) return null;
            string host = Host(found.Address);
            string shown = string.IsNullOrWhiteSpace(found.Title) || string.Equals(found.Title.Trim(), host, StringComparison.OrdinalIgnoreCase) ? host : found.Title.Trim() + " (" + host + ")";
            string line;
            lock (dice) line = Lines[dice.Next(Lines.Length)];
            return new[] { shown, found.Address, Them.Fill(line) };
        }

        // The bookmark closest to the word, by its name or its address, or null when none is close enough.
        internal static Mark Find(string word)
        {
            string want = Regex.Replace((word ?? "").ToLowerInvariant(), @"[^\p{L}\p{Nd}]", "");
            if (want.Length < 2) return null;
            string browser = Browser == null ? null : Browser();
            if (browser == null) return null;
            Mark best = null;
            int bestScore = 0;
            foreach (string file in FilesFor(browser))
            {
                List<Mark> marks;
                try { marks = Read(file); }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); continue; }
                foreach (Mark mark in marks)
                {
                    int score = Score(want, mark);
                    if (score > bestScore || score == bestScore && score > 0 && (mark.Title ?? "").Length < (best.Title ?? "").Length) { best = mark; bestScore = score; }
                }
            }
            return bestScore >= 45 ? best : null;
        }

        // How close a bookmark is: its site's own name the same as the word scores highest, then its name, the initials of
        // its name or site, a name or site that starts with the word, one that holds it, and a site one letter off.
        internal static int Score(string want, Mark mark)
        {
            string host = Host(mark.Address);
            if (host.Length == 0) return 0;
            string label = Regex.Replace(host, @"\.[^.]+$", "");
            label = label.Contains(".") ? label.Substring(label.LastIndexOf('.') + 1) : label;
            string squashed = label.Replace("-", "");
            string title = (mark.Title ?? "").ToLowerInvariant();
            string[] words = Regex.Split(title, @"[^\p{L}\p{Nd}]+");
            words = Array.FindAll(words, one => one.Length > 0);
            string initials = string.Concat(Array.ConvertAll(words, one => one.Substring(0, 1)));
            string labelInitials = string.Concat(Array.ConvertAll(label.Split('-'), one => one.Length > 0 ? one.Substring(0, 1) : ""));
            if (squashed == want) return 100;
            if (Regex.Replace(title, @"[^\p{L}\p{Nd}]", "") == want) return 95;
            if (Array.IndexOf(words, want) >= 0) return 90;
            if (initials.Length >= 2 && initials == want) return 80;
            if (labelInitials.Length >= 2 && labelInitials == want) return 75;
            if (want.Length >= 3 && squashed.StartsWith(want, StringComparison.Ordinal)) return 70;
            if (want.Length >= 3 && Array.Exists(words, one => one.StartsWith(want, StringComparison.Ordinal))) return 60;
            if (want.Length >= 3 && squashed.Contains(want)) return 50;
            if (want.Length >= 4 && Distance(squashed, want) <= 1) return 45;
            return 0;
        }

        private static string Host(string address)
        {
            Uri uri;
            if (!Uri.TryCreate(address ?? "", UriKind.Absolute, out uri) || (uri.Scheme != "http" && uri.Scheme != "https")) return "";
            string host = uri.Host.ToLowerInvariant();
            return host.StartsWith("www.", StringComparison.Ordinal) ? host.Substring(4) : host;
        }

        private static int Distance(string a, string b)
        {
            int[] row = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) row[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                int corner = row[0];
                row[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int up = row[j];
                    row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), corner + (a[i - 1] == b[j - 1] ? 0 : 1));
                    corner = up;
                }
            }
            return row[b.Length];
        }

        // Where the browser keeps them: Chromium browsers a Bookmarks file in each profile, Firefox its newest compressed
        // backup in each profile.
        private static List<string> Files(string browser)
        {
            List<string> files = new List<string>();
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string data = browser == "Chrome" ? Path.Combine(local, @"Google\Chrome\User Data")
                : browser == "Edge" ? Path.Combine(local, @"Microsoft\Edge\User Data")
                : browser == "Brave" ? Path.Combine(local, @"BraveSoftware\Brave-Browser\User Data")
                : browser == "Vivaldi" ? Path.Combine(local, @"Vivaldi\User Data")
                : browser == "Opera" ? Path.Combine(roaming, @"Opera Software\Opera Stable")
                : browser == "Opera GX" ? Path.Combine(roaming, @"Opera Software\Opera GX Stable") : null;
            try
            {
                if (data != null && Directory.Exists(data))
                {
                    if (File.Exists(Path.Combine(data, "Bookmarks"))) files.Add(Path.Combine(data, "Bookmarks"));
                    foreach (string profile in Directory.GetDirectories(data))
                    {
                        string name = Path.GetFileName(profile);
                        if ((name == "Default" || name.StartsWith("Profile ", StringComparison.Ordinal)) && File.Exists(Path.Combine(profile, "Bookmarks"))) files.Add(Path.Combine(profile, "Bookmarks"));
                    }
                }
                string fox = Path.Combine(roaming, @"Mozilla\Firefox\Profiles");
                if (browser == "Firefox" && Directory.Exists(fox))
                    foreach (string profile in Directory.GetDirectories(fox))
                    {
                        string backups = Path.Combine(profile, "bookmarkbackups");
                        if (!Directory.Exists(backups)) continue;
                        string newest = null;
                        foreach (string backup in Directory.GetFiles(backups, "*.jsonlz4"))
                            if (newest == null || File.GetLastWriteTimeUtc(backup) > File.GetLastWriteTimeUtc(newest)) newest = backup;
                        if (newest != null) files.Add(newest);
                    }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            return files;
        }

        // Every http and https bookmark in one file. Chromium's JSON has a node's name before its "type": "url" and its
        // address; Firefox's backup has a node's title before its uri. Each address takes the last name before it.
        internal static List<Mark> Read(string file)
        {
            byte[] bytes = File.ReadAllBytes(file);
            string text = bytes.Length > 12 && Encoding.ASCII.GetString(bytes, 0, 8) == "mozLz40\0"
                ? Encoding.UTF8.GetString(Lz4(bytes, 12, BitConverter.ToInt32(bytes, 8)))
                : Encoding.UTF8.GetString(bytes);
            const string quoted = @"""((?:[^""\\]|\\.)*)""";
            List<KeyValuePair<int, string>> names = new List<KeyValuePair<int, string>>();
            foreach (Match name in Regex.Matches(text, @"""(?:name|title)""\s*:\s*" + quoted)) names.Add(new KeyValuePair<int, string>(name.Index, Unescape(name.Groups[1].Value)));
            List<Mark> marks = new List<Mark>();
            int at = 0;
            foreach (Match address in Regex.Matches(text, @"(?:""type""\s*:\s*""url""\s*,\s*""url""|""uri"")\s*:\s*" + quoted))
            {
                string url = Unescape(address.Groups[1].Value);
                if (!Regex.IsMatch(url, "^https?://", RegexOptions.IgnoreCase)) continue;
                while (at + 1 < names.Count && names[at + 1].Key < address.Index) at++;
                string title = names.Count > 0 && names[at].Key < address.Index ? names[at].Value : "";
                marks.Add(new Mark { Title = title, Address = url });
            }
            return marks;
        }

        private static string Unescape(string json)
        {
            return Regex.Replace(json, @"\\(u[0-9a-fA-F]{4}|.)", delegate(Match escape)
            {
                string one = escape.Groups[1].Value;
                if (one.Length == 5) return ((char)Convert.ToInt32(one.Substring(1), 16)).ToString();
                return one == "n" ? "\n" : one == "t" ? "\t" : one == "r" ? "\r" : one == "b" ? "\b" : one == "f" ? "\f" : one;
            });
        }

        // An LZ4 block, as Firefox's mozLz4 files hold after their 8-byte name and 4-byte size.
        internal static byte[] Lz4(byte[] source, int from, int size)
        {
            byte[] made = new byte[size];
            int s = from, d = 0;
            while (s < source.Length && d < size)
            {
                int token = source[s++];
                int literal = token >> 4;
                if (literal == 15) { int more; do { more = source[s++]; literal += more; } while (more == 255); }
                Array.Copy(source, s, made, d, literal);
                s += literal;
                d += literal;
                if (s >= source.Length || d >= size) break;
                int back = source[s] | source[s + 1] << 8;
                s += 2;
                int length = token & 15;
                if (length == 15) { int more; do { more = source[s++]; length += more; } while (more == 255); }
                length += 4;
                for (int i = 0; i < length && d < size; i++, d++) made[d] = made[d - back];
            }
            return made;
        }
    }
}
