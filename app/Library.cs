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

    // A spare of one of her files with the moment it was made in its name, as seconds since 1970 in UTC:
    // sys.md.1763472612.bak. The name is the record. A folder copied to a backup drive gets a new creation time on
    // every file, and a spare opened and saved gets a new write time, and neither can reorder these.
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

        // When a spare was made: from its name, or for one from before that, from its creation time.
        internal static long When(string spare)
        {
            Match timed = Regex.Match(Path.GetFileName(spare) ?? "", @"\.([0-9]{9,})\.bak$", RegexOptions.IgnoreCase);
            long at;
            if (timed.Success && long.TryParse(timed.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out at)) return at;
            try { return Epoch(File.GetCreationTimeUtc(spare)); }
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
}

