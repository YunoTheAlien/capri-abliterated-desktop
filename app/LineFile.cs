using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CapriDesktop
{
    // One of her plain line files: a markdown file of "## section" headings with a line under each, read back as
    // pools she picks from. moments.md was the first and watching.md the second, and a third copy of the same
    // hundred lines of loading, topping up and re-reading is how two of them end up behaving differently.
    //
    // What every one of them does: ships with real content, is re-read when it changes without a restart, gains
    // sections and lines a later build adds without touching a word of theirs, and falls back to the lines in
    // the build when a section is emptied, so nothing anyone deletes can leave her with nothing to say.
    //
    // A section whose heading ends in "(uncensored)" is read only with the adult switch on, the same rule her
    // packs and lines.md follow, and it is written only then as well, so a censored install has no trace of it.
    internal sealed class LineFile
    {
        internal const string SenpaiMark = " (uncensored)";

        private readonly string shipped;
        // Lines she shipped that were wrong, and what they say now: an exact match of what was written, so a
        // line somebody reworded themselves does not match and is left alone.
        private readonly string[,] reworded;
        private readonly object gate = new object();
        private readonly Dictionary<string, string[]> loaded = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        private DateTime read = DateTime.MinValue;
        private long wasLong = -1;

        internal string Kept;

        // Whether a mod can stand in for this file's pools. Her stories say no: thousands of pieces would
        // bury the list, and a piece of history is content rather than a place worth replacing.
        internal bool Zoned = true;

        internal LineFile(string shipped) : this(shipped, null) { }

        internal LineFile(string shipped, string[,] reworded)
        {
            this.shipped = shipped;
            this.reworded = reworded;
        }

        // The same, but read when it is wanted rather than when the class loads. A file whose text gains a
        // half from somewhere else cannot be captured in a static field initialiser: that runs long before
        // anything attaches, so the half was always missing and the file was written without it.
        internal LineFile(Func<string> shipping, string[,] reworded)
        {
            this.shipping = shipping;
            this.reworded = reworded;
        }

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
                foreach (string raw in File.ReadAllText(path).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
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
            // A mod may have taken this pool. Her own lines are still here and still read when it lets go,
            // so nothing of hers is lost by a mod claiming it. See Zones.
            // A mod may have taken this pool. Her own lines are still here and still read when it lets go,
            // so nothing of hers is lost by a mod claiming it. The senpai half of a pool is a zone of its own
            // and wins the same way her own senpai lines do, which also means a mod cannot reach it at all
            // while the switch is off. See Zones.
            if (Zoned && !string.IsNullOrEmpty(Kept))
            {
                if (Brain.Adult)
                {
                    string deep = Zones.Name(Kept, Senpai(section));
                    Zones.Offers(deep);
                    string[] senpai = Zones.Claimed(deep);
                    if (senpai != null) return senpai;
                }
                string zone = Zones.Name(Kept, section);
                Zones.Offers(zone);
                string[] theirs = Zones.Claimed(zone);
                if (theirs != null) return theirs;
            }
            Fresh();
            lock (gate)
            {
                string[] found;
                if (Brain.Adult && loaded.TryGetValue(Senpai(section), out found) && found != null && found.Length > 0) return found;
                return loaded.TryGetValue(section, out found) ? found : null;
            }
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
                foreach (string raw in File.ReadAllText(Kept).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                {
                    string one = raw.Trim();
                    if (!one.StartsWith("##", StringComparison.Ordinal)) continue;
                    string named = one.TrimStart('#').Trim();
                    if (named.Length == 0) continue;
                    if (!Brain.Adult && named.IndexOf(SenpaiMark, StringComparison.OrdinalIgnoreCase) >= 0) continue;
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
                long stamp = about.LastWriteTimeUtc.Ticks + about.Length;
                lock (gate) if (stamp == wasLong && read != DateTime.MinValue) return;
                Dictionary<string, string[]> found = Read(File.ReadAllText(Kept));
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

        // The senpai sections, out of the text, while the switch is off. Absent rather than empty: a censored
        // install should show no sign that there is anything to unlock.
        private static string Fit(string body)
        {
            if (Brain.Adult) return Ended(body);
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

        // Written once, then topped up: a section the build ships that the file has not got goes on the end, and
        // a line she ships that is missing from a section it does have is added to it, matched exactly. A line
        // they reworded stays reworded, and one they deleted comes back only if the whole section went with it.
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
                string had = File.ReadAllText(Kept);
                string made = Reword(ToppedUp(had));
                if (made != had)
                {
                    Notes.Put(Kept, made);
                    lock (gate) { wasLong = -1; read = DateTime.MinValue; }
                }
                Notes.Refresh(Kept, Text);
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        private string ToppedUp(string had)
        {
            List<string> rows = new List<string>(had.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'));
            foreach (KeyValuePair<string, List<string>> section in Shipped())
            {
                // A senpai section is not put into a censored install at all, and not taken out of one that has it:
                // turning the switch off leaves what is already written where it is, as everywhere else.
                if (!Brain.Adult && section.Key.IndexOf(SenpaiMark, StringComparison.OrdinalIgnoreCase) >= 0) continue;
                int from = -1, to = rows.Count;
                for (int row = 0; row < rows.Count; row++)
                {
                    string one = rows[row].Trim();
                    if (!one.StartsWith("##", StringComparison.Ordinal)) continue;
                    if (from < 0)
                    {
                        if (string.Equals(one.TrimStart('#').Trim(), section.Key, StringComparison.OrdinalIgnoreCase)) from = row + 1;
                        continue;
                    }
                    to = row;
                    break;
                }
                if (from < 0)
                {
                    while (rows.Count > 0 && rows[rows.Count - 1].Trim().Length == 0) rows.RemoveAt(rows.Count - 1);
                    rows.Add("");
                    rows.Add("## " + section.Key);
                    rows.Add("");
                    foreach (string line in section.Value) rows.Add(line);
                    continue;
                }
                List<string> has = new List<string>();
                for (int row = from; row < to; row++) if (rows[row].Trim().Length > 0) has.Add(rows[row].Trim());
                int at = to;
                while (at > from && rows[at - 1].Trim().Length == 0) at--;
                foreach (string line in section.Value)
                {
                    // A line with ">" in front of it is that line switched off, which is how every file of
                    // hers comments something out. It counts as present, or the top up would put the live
                    // one back beside it on her next start and nothing could ever be turned off by hand.
                    if (has.Contains(line) || has.Contains("> " + line) || has.Contains(">" + line)) continue;
                    rows.Insert(at, line);
                    at++;
                }
            }
            string made = string.Join(Environment.NewLine, rows.ToArray());
            if (!made.EndsWith(Environment.NewLine, StringComparison.Ordinal)) made += Environment.NewLine;
            return made;
        }

        // The swap, after the top up: top up will have put the new line in already, so the old one is dropped
        // rather than rewritten when its replacement is there, or the file ends up with both.
        private string Reword(string body)
        {
            if (reworded == null) return body;
            string[] rows = body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int at = 0; at < reworded.GetLength(0); at++)
            {
                bool has = false;
                foreach (string raw in rows) if (string.Equals(raw.Trim(), reworded[at, 1], StringComparison.Ordinal)) has = true;
                List<string> kept = new List<string>();
                foreach (string raw in rows)
                {
                    if (!string.Equals(raw.Trim(), reworded[at, 0], StringComparison.Ordinal)) { kept.Add(raw); continue; }
                    if (has) continue;
                    kept.Add(reworded[at, 1]);
                    has = true;
                }
                rows = kept.ToArray();
            }
            string made = string.Join(Environment.NewLine, rows);
            if (!made.EndsWith(Environment.NewLine, StringComparison.Ordinal)) made += Environment.NewLine;
            return made;
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
