using System;
using System.Collections.Generic;
using System.IO;

namespace CapriDesktop
{
    // Her senpai files, named pack1, pack2, pack3 on disk.
    //
    // The folder they sit in is plain and hidden already. The names inside it were not: a listing of
    // their names says everything the folder's name does not. Numbered files say
    // nothing at all.
    //
    // Which pack a file is comes from the file, not from a list kept somewhere else. Every one of them opens
    // with its own title line, so pack3 says which one it is on the first line of itself. That matters more
    // than it sounds: a mapping file can be lost, reset or missed by a reinstall, and then nothing on disk
    // knows what any of these are. This way an update, a new install over the top, or a settings file thrown
    // away leaves every pack exactly where it was and still readable, with nothing to set up again.
    internal static class Slots
    {
        internal const string Stem = "pack";

        private static readonly object gate = new object();
        private static readonly Dictionary<string, Dictionary<string, string>> known = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, long> stamps = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        // The title a file calls itself by: its first heading. Null when it has none, which is a damaged file
        // rather than a pack, and is left for the code that repairs those.
        internal static string Titled(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                foreach (string raw in File.ReadLines(path))
                {
                    string one = (raw ?? "").Trim();
                    if (one.Length == 0) continue;
                    if (!one.StartsWith("#", StringComparison.Ordinal)) return null;
                    string title = one.TrimStart('#').Trim();
                    return title.Length == 0 ? null : title;
                }
            }
            catch (Exception) { }
            return null;
        }

        // Where a pack lives in a folder of numbered files: the one that says it is that pack, or the next
        // free number when it is not there yet.
        internal static string Path(string folder, string named)
        {
            if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(named)) return null;
            Dictionary<string, string> found = Read(folder);
            lock (gate)
            {
                string path;
                if (found.TryGetValue(named.Trim(), out path)) return path;
            }
            return Free(folder);
        }

        private static string Free(string folder)
        {
            try
            {
                for (int one = 1; one < 500; one++)
                {
                    string path = System.IO.Path.Combine(folder, Stem + one.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    if (!File.Exists(path)) return path;
                }
            }
            catch (Exception) { }
            return System.IO.Path.Combine(folder, Stem + Guid.NewGuid().ToString("N").Substring(0, 6));
        }

        // Title to file, for one folder, read again when the folder changes. A dozen small files, so this is
        // cheap, and it is the only thing that knows which is which.
        private static Dictionary<string, string> Read(string folder)
        {
            Dictionary<string, string> found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!Directory.Exists(folder)) return found;
                long stamp = 0;
                string[] files = Directory.GetFiles(folder);
                foreach (string file in files)
                {
                    try { FileInfo about = new FileInfo(file); stamp += about.LastWriteTimeUtc.Ticks + about.Length; }
                    catch (Exception) { }
                }
                lock (gate)
                {
                    long had;
                    Dictionary<string, string> kept;
                    if (stamps.TryGetValue(folder, out had) && had == stamp && known.TryGetValue(folder, out kept)) return kept;
                }
                foreach (string file in files)
                {
                    string title = Titled(file);
                    if (string.IsNullOrEmpty(title)) continue;
                    if (!found.ContainsKey(title)) found[title] = file;
                }
                lock (gate)
                {
                    known[folder] = found;
                    stamps[folder] = stamp;
                }
            }
            catch (Exception) { }
            return found;
        }

        // Files written under their own names become numbered ones, keeping every word and their own title
        // line, which is what says which pack each was. One way and once: a file already numbered is left be.
        internal static void Renamed(string folder)
        {
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
                foreach (string file in Directory.GetFiles(folder, "*.md"))
                {
                    string title = Titled(file);
                    // A file with no title of its own cannot be found again once it is numbered, so it keeps
                    // the only name it has. Her repair pass puts titles back, and it will be numbered then.
                    if (string.IsNullOrEmpty(title)) continue;
                    string path = Free(folder);
                    Tucked.Free(file);
                    File.Move(file, path);
                    string spare = file + ".bak";
                    if (File.Exists(spare)) { Tucked.Free(spare); try { File.Move(spare, path + ".bak"); } catch (IOException) { } }
                }
                lock (gate) { known.Remove(folder); stamps.Remove(folder); }
            }
            catch (Exception) { }
        }

        // For her editor and anything else that shows one to a person: what the file calls itself, or its
        // name when it has nothing to say.
        internal static string Shown(string path)
        {
            string title = Titled(path);
            return string.IsNullOrEmpty(title) ? System.IO.Path.GetFileName(path) : title;
        }
    }
}
