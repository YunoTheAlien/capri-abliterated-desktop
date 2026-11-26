using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CapriDesktop
{
    // One of her plain line files: "## section" headings read back as pools, re-read on change, topped up by later
    // builds without touching their words, and falling back to the build's lines when emptied. "(senpai)" sections only
    // with the switch on.
    internal sealed class LineFile
    {
        internal const string SenpaiMark = " (" + Legacy.Mark + ")";

        private readonly string shipped;
        private readonly object gate = new object();
        private readonly Dictionary<string, string[]> loaded = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        private DateTime read = DateTime.MinValue;
        private long wasLong = -1;

        internal string Kept;

        // Whether a mod can stand in for this file's pools. Her stories say no: thousands of pieces would
        // bury the list, and a piece of history is content rather than a place worth replacing.
        internal bool Zoned = true;

        internal LineFile(string shipped) { this.shipped = shipped; }

        // The same, read when wanted rather than when the class loads: a static initialiser runs before anything
        // attaches, so a half added from elsewhere would always be missing.
        internal LineFile(Func<string> shipping) { this.shipping = shipping; }

        private readonly Func<string> shipping;

        // Whichever way it was given, read the same way everywhere.
        private string Given { get { return shipping != null ? shipping() : shipped; } }

        // The file as the build ships it, with the senpai sections left out while the switch is off.
        internal string Text { get { return Fit(Given); } }

        internal WrittenNote Open() { Write(false); return new WrittenNote(Kept, Text, gate); }

        internal static string Senpai(string section) { return section + SenpaiMark; }

        // Marked sections cut out of a file on disk. Fit shapes shipped text and cannot reach one somebody
        // already has. One method for every line file: per file is how three were missed.
        internal static void Sided(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                List<string> kept = new List<string>();
                bool cutting = false, cut = false;
                foreach (string raw in Notes.ReadAll(path).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                {
                    string one = raw.Trim();
                    if (one.StartsWith("##", StringComparison.Ordinal))
                        cutting = one.IndexOf(SenpaiMark, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (cutting) { cut = true; continue; }
                    kept.Add(raw);
                }
                if (!cut) return;
                while (kept.Count > 0 && kept[kept.Count - 1].Trim().Length == 0) kept.RemoveAt(kept.Count - 1);
                Notes.Put(path, string.Join(Environment.NewLine, kept.ToArray()) + Environment.NewLine);
                Brain.Log("files", -1, Path.GetFileName(path) + ": senpai sections removed, because the switch is off.");
            }
            catch (Exception bad) { Notes.Failed("taking the senpai sections out of " + Path.GetFileName(path), bad); }
        }

        // Her lines for a section, or nothing when the file has none. The senpai half of a section wins while the
        // switch is on, the same way her packs read.
        internal string[] Lines(string section)
        {
            if (string.IsNullOrEmpty(section)) return null;
            // A mod may have taken this pool; her lines stay and are read when it lets go. The senpai half is its own
            // zone, out of a mod's reach while the switch is off. See Zones.
            if (Zoned && !string.IsNullOrEmpty(Kept))
            {
                if (Brain.SenpaiLive)
                {
                    string deep = Zones.Name(Kept, Senpai(section));
                    Zones.Offers(deep);
                    string[] senpai = Zones.Claimed(deep);
                    if (senpai != null) return Sayings.Named(senpai, "mod: " + section.Replace(SenpaiMark, ""));
                }
                string zone = Zones.Name(Kept, section);
                Zones.Offers(zone);
                string[] theirs = Zones.Claimed(zone);
                if (theirs != null) return Sayings.Named(theirs, "mod: " + section.Replace(SenpaiMark, ""));
            }
            Fresh();
            string[] lines;
            lock (gate)
            {
                string[] found;
                if (Brain.SenpaiLive && loaded.TryGetValue(Senpai(section), out found) && found != null && found.Length > 0) lines = found;
                else lines = loaded.TryGetValue(section, out found) ? found : null;
            }
            // Named for her log, so a line built from these says where it came from rather than "?".
            return Sayings.Named(lines, (string.IsNullOrEmpty(Kept) ? "code" : Path.GetFileName(Kept)) + ": " + section.Replace(SenpaiMark, ""));
        }

        // Every section in the file, in the order it is written, with the senpai ones left out while the switch
        // is off. For a file read as pieces rather than as pools: the order on the page is the order she goes in.
        internal string[] Sections()
        {
            Fresh();
            List<string> found = new List<string>();
            try
            {
                if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept)) return found.ToArray();
                foreach (string raw in FileSlots.ReadText(Kept).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                {
                    string one = raw.Trim();
                    if (!one.StartsWith("##", StringComparison.Ordinal)) continue;
                    string named = one.TrimStart('#').Trim();
                    if (named.Length == 0) continue;
                    if (!Brain.SenpaiLive && named.IndexOf(SenpaiMark, StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    found.Add(named);
                }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            return found.ToArray();
        }

        // One line from a section, through the recency she keeps for everything else she says, with the tokens
        // filled in. Falls back to the lines handed in, which are the ones in the build.
        internal string Pick(string section, string[] stock)
        {
            string[] mine = Lines(section);
            string[] pool = mine != null && mine.Length > 0 ? mine : stock;
            if (pool == null || pool.Length == 0) return null;
            // Named for her log, so a touch or moment line says where it came from rather than "?".
            string named = string.IsNullOrEmpty(Kept) ? "code" : Path.GetFileName(Kept);
            Sayings.Named(pool, (mine != null && mine.Length > 0 ? named : "code") + ": " + section.Replace(SenpaiMark, ""));
            string said = Sayings.OneOf(pool);
            return said == null ? null : Them.Fill(said);
        }

        private void Fresh()
        {
            if (string.IsNullOrEmpty(Kept)) return;
            try
            {
                FileInfo about = new FileInfo(Kept);
                if (!about.Exists) return;
                long stamp = (about.LastWriteTimeUtc.Ticks + about.Length) ^ ((long)FileSlots.Generation << 56);
                lock (gate) if (stamp == wasLong && read != DateTime.MinValue) return;
                Dictionary<string, string[]> found = Read(FileSlots.ReadText(Kept));
                lock (gate)
                {
                    loaded.Clear();
                    foreach (KeyValuePair<string, string[]> one in found) loaded[one.Key] = one.Value;
                    wasLong = stamp;
                    read = DateTime.Now;
                }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        internal static Dictionary<string, string[]> PoolsForTest(string body) { return Read(body); }

        private static Dictionary<string, string[]> Read(string body)
        {
            Dictionary<string, string[]> found = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            string under = null;
            List<string> lines = new List<string>();
            foreach (string raw in body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                string one = raw.Trim();
                if (one.StartsWith("##", StringComparison.Ordinal))
                {
                    if (under != null) found[under] = lines.ToArray();
                    under = one.TrimStart('#').Trim();
                    lines = new List<string>();
                    continue;
                }
                if (under == null || one.Length == 0 || one.StartsWith(">", StringComparison.Ordinal) || one.StartsWith("#", StringComparison.Ordinal)) continue;
                lines.Add(one);
            }
            if (under != null) found[under] = lines.ToArray();
            return found;
        }

        // The senpai sections, out of the text, while the switch is off: left out rather than empty.
        private static string Fit(string body)
        {
            if (Brain.SenpaiLive) return Ended(body);
            List<string> kept = new List<string>();
            bool skipping = false;
            foreach (string raw in body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                string one = raw.Trim();
                if (one.StartsWith("##", StringComparison.Ordinal))
                    skipping = one.IndexOf(SenpaiMark, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!skipping) kept.Add(raw);
            }
            while (kept.Count > 0 && kept[kept.Count - 1].Trim().Length == 0) kept.RemoveAt(kept.Count - 1);
            return string.Join(Environment.NewLine, kept.ToArray()) + Environment.NewLine;
        }

        private static string Ended(string text)
        {
            return (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", Environment.NewLine);
        }

        // Written once, then topped up: a section the build ships that the file lacks goes on the end, and a missing
        // shipped line, matched exactly, is added to its section. Reworded lines stay; a deleted one returns only with
        // its whole section.
        internal void Write(bool over)
        {
            if (string.IsNullOrEmpty(Kept)) return;
            try
            {
                string folder = Path.GetDirectoryName(Kept);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) Directory.CreateDirectory(folder);
                if (over || !File.Exists(Kept))
                {
                    Notes.Put(Kept, Text);
                    lock (gate) { wasLong = -1; read = DateTime.MinValue; }
                    return;
                }
                // Written new from stock on each version (Renew); in between only its notes follow her language, and the
                // switch on adds its sections to a file written without them.
                string had = Notes.ReadAll(Kept);
                string made = SenpaiAdded(had);
                if (made != had)
                {
                    Notes.Put(Kept, made);
                    lock (gate) { wasLong = -1; read = DateTime.MinValue; }
                }
                Notes.Refresh(Kept, Text);
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // The senpai sections she ships, each added whole to a file that has not got it, while the switch is on and she is
        // off air. Turning the switch off leaves what is already written where it is.
        private string SenpaiAdded(string had)
        {
            if (!Brain.SenpaiLive) return had;
            List<string> rows = new List<string>(had.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'));
            bool added = false;
            foreach (KeyValuePair<string, List<string>> section in Shipped())
            {
                if (section.Key.IndexOf(SenpaiMark, StringComparison.OrdinalIgnoreCase) < 0) continue;
                bool there = false;
                foreach (string row in rows)
                    if (row.Trim().StartsWith("##", StringComparison.Ordinal) && string.Equals(row.Trim().TrimStart('#').Trim(), section.Key, StringComparison.OrdinalIgnoreCase)) { there = true; break; }
                if (there) continue;
                while (rows.Count > 0 && rows[rows.Count - 1].Trim().Length == 0) rows.RemoveAt(rows.Count - 1);
                rows.Add("");
                rows.Add("## " + section.Key);
                rows.Add("");
                rows.AddRange(section.Value);
                added = true;
            }
            if (!added) return had;
            return string.Join(Environment.NewLine, rows.ToArray()) + Environment.NewLine;
        }

        private List<KeyValuePair<string, List<string>>> Shipped()
        {
            List<KeyValuePair<string, List<string>>> found = new List<KeyValuePair<string, List<string>>>();
            string under = null;
            List<string> lines = new List<string>();
            foreach (string raw in Given.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                string one = raw.Trim();
                if (one.StartsWith("##", StringComparison.Ordinal))
                {
                    if (under != null) found.Add(new KeyValuePair<string, List<string>>(under, lines));
                    under = one.TrimStart('#').Trim();
                    lines = new List<string>();
                    continue;
                }
                if (under == null || one.Length == 0 || one.StartsWith(">", StringComparison.Ordinal) || one.StartsWith("#", StringComparison.Ordinal)) continue;
                lines.Add(one);
            }
            if (under != null) found.Add(new KeyValuePair<string, List<string>>(under, lines));
            return found;
        }

        // For her checks: back to how it starts, with nothing read.
        internal void Forget()
        {
            lock (gate) { loaded.Clear(); wasLong = -1; read = DateTime.MinValue; }
        }
    }
}
