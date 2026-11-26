using System;
using System.Collections.Generic;
using System.IO;

namespace CapriDesktop
{
    // Her senpai files, named pack1, pack2 on disk so a listing says nothing. Each file says which pack it is in its
    // own first title line, so no mapping file can be lost and an update or reinstall leaves every pack readable.
    internal static class Slots
    {
        internal const string Stem = "pack";

        private static readonly object gate = new object();
        private static readonly Dictionary<string, Dictionary<string, string>> known = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, long> stamps = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Dictionary<string, string>> seenTitles = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, long> seenStamps = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        // The title a file calls itself by, its first heading; null is a damaged file, left for the repair code.
        // Retried briefly while a writer holds it, since on a first start files are read as they are laid.
        internal static string Titled(string path)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                    using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        string raw;
                        while ((raw = reader.ReadLine()) != null)
                        {
                            string one = raw.Trim();
                            if (one.Length == 0) continue;
                            if (!one.StartsWith("#", StringComparison.Ordinal)) return null;
                            string title = one.TrimStart('#').Trim();
                            return title.Length == 0 ? null : title;
                        }
                    }
                    return null;
                }
                catch (IOException caughtOne)
                {
                    if (attempt < 10 && !(caughtOne is FileNotFoundException) && !(caughtOne is DirectoryNotFoundException)) { System.Threading.Thread.Sleep(50); continue; }
                    CapriDesktop.Brain.Caught(caughtOne);
                    return null;
                }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); return null; }
            }
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

        // Which file a pack is in, for reading her lines: from the folder as Stamps last saw it, so asking makes no disk
        // call unless the folder changed. Null when no file says it is that pack. Path is for writing one, and looks now.
        internal static string Find(string folder, string named)
        {
            if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(named)) return null;
            long stamp = Stamps.FolderStamp(folder);
            Dictionary<string, string> found = null;
            lock (gate)
            {
                long had;
                Dictionary<string, string> kept;
                if (seenStamps.TryGetValue(folder, out had) && had == stamp && seenTitles.TryGetValue(folder, out kept)) found = kept;
            }
            if (found == null)
            {
                found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string file in Stamps.Files(folder))
                {
                    if (!Notes.IsPack(file)) continue;
                    string title = Titled(file);
                    if (!string.IsNullOrEmpty(title) && !found.ContainsKey(title)) found[title] = file;
                }
                lock (gate)
                {
                    seenTitles[folder] = found;
                    seenStamps[folder] = stamp;
                }
            }
            string path;
            return found.TryGetValue(named.Trim(), out path) ? path : null;
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
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
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
                    catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                }
                lock (gate)
                {
                    long had;
                    Dictionary<string, string> kept;
                    if (stamps.TryGetValue(folder, out had) && had == stamp && known.TryGetValue(folder, out kept)) return kept;
                }
                foreach (string file in files)
                {
                    // A spare or a stamp is never a pack: taken for one, the update spared the spare (pack1.1.bak.2.bak).
                    if (!Notes.IsPack(file)) continue;
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
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
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
                    SpareFiles.Carry(file, path);
                }
                lock (gate) { known.Remove(folder); stamps.Remove(folder); }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // For her editor and anything else that shows one to a person: what the file calls itself, or its
        // name when it has nothing to say.
        internal static string Shown(string path)
        {
            string title = Titled(path);
            return string.IsNullOrEmpty(title) ? System.IO.Path.GetFileName(path) : title;
        }

        // A spare beside a pack, with a title the pack no longer has, is never taken for the pack: finding it says none, and
        // writing it goes to a new numbered file, so an update never spares a spare.
#if CHECKS
        internal static void CheckSpares(List<string> checks, string output)
        {
            string folder = System.IO.Path.Combine(output, "slots-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(System.IO.Path.Combine(folder, "pack1"), "# Calm\nHi.\n");
                File.WriteAllText(System.IO.Path.Combine(folder, "pack1.1795210264.bak"), "# Bright\nHello.\n");
                File.WriteAllText(System.IO.Path.Combine(folder, "pack1.txt"), "# Loud\nHey.\n");
                string found = Find(folder, "Bright"), written = Path(folder, "Bright"), calm = Path(folder, "Calm"), loud = Find(folder, "Loud");
                if (found != null || loud != null || written == null || !written.EndsWith("pack2", StringComparison.Ordinal) || calm == null || !calm.EndsWith("pack1", StringComparison.Ordinal))
                    throw new Exception("A spare or a .txt beside a pack was taken for a pack: find " + found + ", " + loud + ", write " + written + ", calm " + calm);
                checks.Add("PASS a spare (pack1.<time>.bak) or a .txt beside her numbered packs is never taken for a pack, so an update never spares a spare");
            }
            finally
            {
                try { Directory.Delete(folder, true); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
        }
#endif
    }
}
