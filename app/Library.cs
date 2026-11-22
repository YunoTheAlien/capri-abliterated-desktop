using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Security.Cryptography;

namespace CapriDesktop
{
    // Every sound file somebody hands her is copied into her own folder, so a backup of her folder keeps it and moving or
    // deleting the original breaks nothing. Voice clips go in assets\voice, every other sound in sounds.
    internal static class Library
    {
        internal static string Home;
        internal static readonly string Voices = Path.Combine("assets", "voice");
        internal const string Sounds = "sounds";

        // A copy on her shelf, or the path as given when it is already there, missing, or cannot be copied. The same
        // file picked twice is kept once; a different file with the same name gets a number.
        internal static string Keep(string path, string shelf)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(Home)) return path;
            try
            {
                string full = Path.GetFullPath(path);
                string folder = Path.GetFullPath(Path.Combine(Home, shelf));
                if (!File.Exists(full)) return path;
                if (string.Equals(Path.GetDirectoryName(full), folder, StringComparison.OrdinalIgnoreCase)) return full;
                Directory.CreateDirectory(folder);
                string name = Path.GetFileNameWithoutExtension(full), kind = Path.GetExtension(full);
                string target = Path.Combine(folder, name + kind);
                for (int n = 2; File.Exists(target) && !Same(target, full); n++) target = Path.Combine(folder, name + " (" + n + ")" + kind);
                if (!File.Exists(target)) File.Copy(full, target);
                // Where, never which: her log does not name what is on the shelf.
                Brain.Log("files", -1, "A file was kept in " + shelf + ".");
                return target;
            }
            catch (Exception caughtOne) { Brain.Caught(caughtOne); return path; }
        }

        // A sound file outside her folder, which is what a setting from before the shelf points at.
        internal static bool Loose(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path) || !File.Exists(path)) return false;
                string kind = Path.GetExtension(path).ToLowerInvariant();
                return kind == ".wav" || kind == ".mp3" || kind == ".m4a" || kind == ".flac" || kind == ".ogg" || kind == ".wma";
            }
            catch (Exception caughtOne) { Brain.Caught(caughtOne); return false; }
        }

#if !CROSS  // her Windows voice clone (YourVoice), which the cross build does not have
        // The clip her HD voice actually reads, copied out of the model's folder, where a fresh download of the model
        // would take it with it. Refreshed whenever it changes.
        internal static void KeepHdVoice(string modelFolder)
        {
            if (string.IsNullOrEmpty(Home) || string.IsNullOrEmpty(modelFolder)) return;
            try
            {
                string from = Path.Combine(modelFolder, YourVoice.Using);
                if (!File.Exists(from)) return;
                string folder = Path.Combine(Home, Voices);
                Directory.CreateDirectory(folder);
                string to = Path.Combine(folder, "your-hd-voice.wav");
                if (File.Exists(to) && Same(to, from)) return;
                File.Copy(from, to, true);
            }
            catch (Exception caughtOne) { Brain.Caught(caughtOne); }
        }
#endif

        private static bool Same(string one, string two)
        {
            if (new FileInfo(one).Length != new FileInfo(two).Length) return false;
            using (SHA256 hash = SHA256.Create())
            {
                byte[] a, b;
                using (FileStream stream = File.OpenRead(one)) a = hash.ComputeHash(stream);
                using (FileStream stream = File.OpenRead(two)) b = hash.ComputeHash(stream);
                return Convert.ToBase64String(a) == Convert.ToBase64String(b);
            }
        }
    }

    // A spare of one of her files named with the moment it was made, seconds since 1970 in UTC: sys.md.1763472612.bak.
    // The name is the record, since copies and saves change file times.
    internal static class SpareFiles
    {
        private static readonly DateTime Zero = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        internal static long Epoch(DateTime utc) { return (long)(utc - Zero).TotalSeconds; }

        // The next spare's name, a second past any already there, so two made in one second still sort.
        internal static string Name(string path)
        {
            long at = Epoch(DateTime.UtcNow);
            foreach (string one in Of(path)) at = Math.Max(at, When(one) + 1);
            return path + "." + at.ToString(CultureInfo.InvariantCulture) + ".bak";
        }

        // Every spare of one file: the timed ones, and name.bak, name.bak2 from before they had a time.
        internal static List<string> Of(string path)
        {
            List<string> found = new List<string>();
            try
            {
                string folder = Path.GetDirectoryName(path), name = Path.GetFileName(path);
                if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(name) || !Directory.Exists(folder)) return found;
                Regex mine = new Regex("^" + Regex.Escape(name) + @"(\.[0-9]{9,}\.bak|\.bak[0-9]*)$", RegexOptions.IgnoreCase);
                foreach (string one in Directory.GetFiles(folder, name + ".*")) if (mine.IsMatch(Path.GetFileName(one))) found.Add(one);
            }
            catch (Exception caughtOne) { Brain.Caught(caughtOne); }
            return found;
        }

        // When a spare was made: from its name, or for one from before that or put back by hand, the later of when it
        // was created and when it was written, so one they put back is not taken for the oldest.
        internal static long When(string spare)
        {
            Match timed = Regex.Match(Path.GetFileName(spare) ?? "", @"\.([0-9]{9,})\.bak$", RegexOptions.IgnoreCase);
            long at;
            if (timed.Success && long.TryParse(timed.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out at)) return at;
            try
            {
                DateTime made = File.GetCreationTimeUtc(spare), written = File.GetLastWriteTimeUtc(spare);
                return Epoch(written > made ? written : made);
            }
            catch (Exception caughtOne) { Brain.Caught(caughtOne); return 0; }
        }

        // The newest kept, by when each was made, and the rest gone.
        internal static void Prune(string path, int most)
        {
            List<string> all = Of(path);
            if (all.Count <= most) return;
            all.Sort(delegate(string a, string b) { return When(b).CompareTo(When(a)); });
            for (int at = most; at < all.Count; at++)
            {
                try { File.Delete(all[at]); }
                catch (Exception caughtOne) { Brain.Caught(caughtOne); }
            }
        }

        // A file renamed or moved takes its spares with it, each keeping its time.
        internal static void Carry(string from, string to)
        {
            foreach (string one in Of(from))
            {
                try
                {
                    string target = to + "." + When(one).ToString(CultureInfo.InvariantCulture) + ".bak";
                    if (!File.Exists(target)) File.Move(one, target);
                }
                catch (Exception caughtOne) { Brain.Caught(caughtOne); }
            }
        }

        // Spares from before they had a time, given one from their creation time. A timed one is left alone, so after
        // the first start this finds nothing to do.
        internal static void Tidy(string folder, bool deep)
        {
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
                Regex old = new Regex(@"^(.+)\.bak[0-9]*$", RegexOptions.IgnoreCase);
                Regex timed = new Regex(@"\.[0-9]{9,}\.bak$", RegexOptions.IgnoreCase);
                foreach (string one in Directory.GetFiles(folder, "*.bak*", deep ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
                {
                    string name = Path.GetFileName(one);
                    if (timed.IsMatch(name)) continue;
                    Match legacy = old.Match(name);
                    if (!legacy.Success) continue;
                    string live = Path.Combine(Path.GetDirectoryName(one), legacy.Groups[1].Value);
                    long at = When(one);
                    string target = live + "." + at.ToString(CultureInfo.InvariantCulture) + ".bak";
                    while (File.Exists(target)) target = live + "." + (++at).ToString(CultureInfo.InvariantCulture) + ".bak";
                    try { File.Move(one, target); }
                    catch (Exception caughtOne) { Brain.Caught(caughtOne); }
                }
            }
            catch (Exception caughtOne) { Brain.Caught(caughtOne); }
        }
    }
    // Each mode keeps its own copy of every file that holds, or could hold, anything from the other side, in
    // a plain folder and a senpai folder in personalSettings, and neither reads or writes the other's. Only the
    // switch crosses. Games have a folder of their own.
    internal static class Sides
    {
        internal const string Games = "games";
        internal static readonly string[] Both =
        {
            "lines.md", "dynamic.md", "sys.md", "touch.md", "watching.md", "moments.md", "mod-zones.txt",
            "me.md", "memories.md", "mood.txt", "mood.when"
        };
        // Once kept per side, now one file in personalSettings itself, read whichever side is on: their personality has
        // nothing of either side in it and does not change between them (Joined brings an existing install together).
        internal static readonly string[] Shared = { "personality.md", "personality.backup.md" };
        internal static readonly string[] OneSide = { "her_interests", "random_interests", "your_interests" };
        internal static readonly string[] Played = { "chess.md" };
        // A censored copy was made from a file that had both halves, and the rest of it still wants scrubbing.
        internal static bool MadePlain;
        // Line files whose marked sections a censored copy drops when it is made.
        private static readonly string[] Marked = { "lines.md", "touch.md", "watching.md", "moments.md" };

        // The mode she last ran in, read before anything else loads.
        internal static bool LastRan(string data)
        {
            try
            {
                string file = Path.Combine(data, "settings.json");
                if (!File.Exists(file)) return false;
                foreach (Match m in Regex.Matches(File.ReadAllText(file), "\"(\\w+)\"\\s*:\\s*true"))
                    if (m.Groups[1].Value == "Interests" || Legacy.WasFolder(m.Groups[1].Value)) return true;
                return false;
            }
            catch (Exception caughtOne) { Brain.Caught(caughtOne); return false; }
        }

        private static bool Has(string[] list, string name)
        {
            foreach (string one in list) if (string.Equals(one, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // Where a file lives for this side: a loose one moves in on first start, and a new side starts from the
        // censored copy, never the other way. Made one file again: the side in use wins, and a differing copy is kept
        // as a spare.
        internal static string Joined(string settings, string name, bool full)
        {
            if (string.IsNullOrEmpty(settings) || string.IsNullOrEmpty(name)) return null;
            string shared = Path.Combine(settings, name);
            string mine = Path.Combine(settings, full ? Personalities.SenpaiName : Personalities.PlainName, name);
            string other = Path.Combine(settings, full ? Personalities.PlainName : Personalities.SenpaiName, name);
            try
            {
                if (File.Exists(mine))
                {
                    if (File.Exists(shared))
                    {
                        if (SameWords(shared, mine)) File.Delete(shared);
                        else File.Move(shared, SpareFiles.Name(shared));
                    }
                    File.Move(mine, shared);
                }
                if (File.Exists(other))
                {
                    if (!File.Exists(shared)) File.Move(other, shared);
                    else if (SameWords(shared, other)) File.Delete(other);
                    else File.Move(other, SpareFiles.Name(shared));
                }
            }
            catch (IOException caughtOne) { Brain.Caught(caughtOne); }
            catch (UnauthorizedAccessException caughtOne) { Brain.Caught(caughtOne); }
            return shared;
        }

        // The same words, whatever line endings or trailing space each was saved with.
        private static bool SameWords(string one, string two)
        {
            Func<string, string> plain = delegate(string path) { return Regex.Replace(File.ReadAllText(path).Replace("\r\n", "\n"), @"[ \t]+\n", "\n").Trim(); };
            return plain(one) == plain(two);
        }

        internal static string Place(string loose, bool full)
        {
            if (string.IsNullOrEmpty(loose)) return loose;
            string name = Path.GetFileName(loose), settings = Path.GetDirectoryName(loose) ?? "";
            string into = Has(Played, name) ? Games
                : Has(Both, name) ? (full ? Personalities.SenpaiName : Personalities.PlainName)
                : Has(OneSide, name) && full ? Personalities.SenpaiName : null;
            if (into == null) return loose;
            string placed = Path.Combine(settings, into, name), plain = Path.Combine(settings, Personalities.PlainName, name);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(placed));
                if (File.Exists(loose) && !File.Exists(placed))
                {
                    if (full && Has(Both, name) && !File.Exists(plain))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(plain));
                        File.Copy(loose, plain);
                        if (Has(Marked, name)) LineFile.Sided(plain);
                        if (name.Equals("mod-zones.txt", StringComparison.OrdinalIgnoreCase))
                        {
                            List<string> zones = new List<string>();
                            foreach (string line in File.ReadAllLines(plain))
                                if (line.IndexOf(LineFile.SenpaiMark, StringComparison.OrdinalIgnoreCase) < 0) zones.Add(line);
                            File.WriteAllLines(plain, zones.ToArray());
                        }
                        MadePlain = true;
                    }
                    File.Move(loose, placed);
                    SpareFiles.Carry(loose, placed);
                }
                else if (full && Has(Both, name) && !File.Exists(placed) && File.Exists(plain)) File.Copy(plain, placed);
            }
            catch (Exception caughtOne) { Brain.Caught(caughtOne); return File.Exists(placed) || !File.Exists(loose) ? placed : loose; }
            return placed;
        }
    }
}
