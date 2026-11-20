using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // What she tells them about herself, a line at a time.
    //
    // Her pools are built so nothing repeats, which is right for a reaction and wrong for a story: a line from
    // the middle of one, then a line from the end of another, is not a story at all. So this file is read as
    // pieces rather than as pools. A piece is a section, its lines are told in the order they are written, and
    // she stays on it until it is finished. Only then does she start another.
    //
    // A finished piece is written down and not told again until she has been through them all, so nobody hears
    // the same story twice in a month of her saying one of these a day.
    //
    // Everything in it is hers to rewrite, and a piece somebody adds is told like any other.
    internal static class Lore
    {
        internal const string FileName = "lore.md";
        internal const string MarkName = "lore.txt";

        private static readonly LineFile file = Unzoned(new LineFile(Defaults));

        private static LineFile Unzoned(LineFile one) { one.Zoned = false; return one; }
        private static readonly object gate = new object();
        private static readonly List<string> finished = new List<string>();
        // The same names again, for asking. The list keeps the order she finished them in, which decides
        // which comes round again first; this answers whether one is in it without walking the whole list.
        private static readonly HashSet<string> done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static string piece = "";
        private static int at;
        private static bool read;
        private static DateTime kept = DateTime.MinValue;

        // How long her place can be out of date on disk. A crash costs at most a few lines of one piece told
        // over again, against writing every heading she has ever finished after every sentence she says.
        private static readonly TimeSpan KeepEvery = TimeSpan.FromSeconds(20);

        private static void Told(string one)
        {
            if (one == null || !done.Add(one)) return;
            finished.Add(one);
        }

        internal static string Kept { get { return file.Kept; } set { file.Kept = value; } }
        // Where she is up to, kept beside her settings rather than in the file they edit: it is her bookmark,
        // not their content, and rewriting their file to move a number would fight with them editing it.
        internal static string Marked;

        internal static string Text { get { return file.Text; } }

        // More of her stories in files of their own beside lore.md: lore1.md, lore2.md and on. Only the base name
        // with a number counts, so a spare like "lore old.md" or "lore copy.md" is never read as more stories.
        private static readonly Regex Numbered = new Regex(@"^lore(\d+)\.md$", RegexOptions.IgnoreCase);
        private static readonly Dictionary<string, LineFile> extras = new Dictionary<string, LineFile>(StringComparer.OrdinalIgnoreCase);

        // The numbered files in a folder, lowest number first.
        internal static List<string> NumberedIn(string folder)
        {
            List<KeyValuePair<long, string>> found = new List<KeyValuePair<long, string>>();
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return new List<string>();
                foreach (string path in Directory.GetFiles(folder, "lore*.md"))
                {
                    Match named = Numbered.Match(Path.GetFileName(path));
                    long number;
                    if (named.Success && long.TryParse(named.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out number)) found.Add(new KeyValuePair<long, string>(number, path));
                }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            found.Sort(delegate(KeyValuePair<long, string> x, KeyValuePair<long, string> y) { return x.Key.CompareTo(y.Key); });
            List<string> paths = new List<string>();
            foreach (KeyValuePair<long, string> one in found) paths.Add(one.Value);
            return paths;
        }

        // Her own lore.md first, then each numbered file beside it.
        private static List<LineFile> Files()
        {
            List<LineFile> all = new List<LineFile> { file };
            if (string.IsNullOrEmpty(Kept)) return all;
            foreach (string path in NumberedIn(Path.GetDirectoryName(Kept)))
            {
                LineFile one;
                if (!extras.TryGetValue(path, out one))
                {
                    one = Unzoned(new LineFile(""));
                    one.Kept = path;
                    extras[path] = one;
                }
                all.Add(one);
            }
            return all;
        }

        // Every piece across her files, in file order; a heading in an earlier file wins over the same one later.
        private static string[] AllSections()
        {
            List<string> found = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (LineFile one in Files())
                foreach (string section in one.Sections())
                    if (seen.Add(section)) found.Add(section);
            return found.ToArray();
        }

        private static string[] LinesOf(string section)
        {
            foreach (LineFile one in Files())
            {
                string[] got = one.Lines(section);
                if (got != null && got.Length > 0) return got;
            }
            return null;
        }

        // Past this she is not offered in the editor. It lays a whole document out as pages, so a file of
        // several megabytes would hang it rather than open, and a lore file can run to that easily. Everything
        // else about her stories works the same at any size: only editing them by hand stops.
        internal const long MostEditable = 262144;

        // ---------- The pack beside the installer ----------
        //
        // Her stories run to thousands of pieces and grow every time somebody writes more. None of that can
        // live in the exe: content in the exe means the exe changes every time a line of it does, and an exe
        // that changes cannot be certified or told apart from the one before it. So the pack is a file,
        // lore.md, sitting beside Capri-Setup.exe, and setup lays it down.
        //
        // Laying it down is a merge by heading, never a replacement. Theirs is theirs: a piece they have
        // rewritten keeps their words, and a piece they deleted stays deleted, which is why this runs once per
        // pack rather than every start. What it ran against last is written beside her bookmark, so a new pack
        // is noticed and the same one is not laid twice.
        internal const string PackName = "lore.md";
        internal const string PackMark = "lore.pack";

        // Where the pack sits: beside her exe, which is where setup puts it.
        internal static string PackPath;

        // The six moods a story can carry, and which of hers picks each. Hers from the other side come from whatever
        // attached, through StoryMoodFor.
        internal static readonly string[] StoryMoods = { "warm", "funny", "sad", "heated", "proud", "calm" };
        private static readonly string[,] Picks =
        {
            { "Affectionate", "warm" }, { "Clingy", "warm" }, { "Needy", "warm" },
            { "Playful", "funny" }, { "Mischievous", "funny" }, { "Hyper", "funny" },
            { "Melancholy", "sad" },
            { "Furious", "heated" }, { "Moody", "heated" }, { "Jealous", "heated" }, { "Touchy", "heated" },
            { "Smug", "proud" },
            { "Focused", "calm" }, { "Sleepy", "calm" }
        };
        internal static Func<string> Feeling;
        internal static Func<string, string> StoryMoodFor;

        internal static string StoryMood()
        {
            string hers = null;
            try { hers = Feeling == null ? null : Feeling(); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            if (string.IsNullOrEmpty(hers)) return null;
            for (int row = 0; row < Picks.GetLength(0); row++)
                if (string.Equals(Picks[row, 0], hers, StringComparison.OrdinalIgnoreCase)) return Picks[row, 1];
            try { return StoryMoodFor == null ? null : StoryMoodFor(hers); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); return null; }
        }

        // Each story's mood, from the "> mood:" line under its heading: hers first, then the pack beside her exe, so a
        // lore.md laid before the tags existed still gets them.
        private static Dictionary<string, string> moodsRead;
        private static long moodsStamp = -1;

        internal static Dictionary<string, string> Moods()
        {
            long stamp = 0;
            List<string> paths = new List<string> { Kept, PackPath };
            if (!string.IsNullOrEmpty(Kept)) paths.AddRange(NumberedIn(Path.GetDirectoryName(Kept)));
            foreach (string one in paths)
                try { if (!string.IsNullOrEmpty(one) && File.Exists(one)) { FileInfo about = new FileInfo(one); stamp += about.LastWriteTimeUtc.Ticks + about.Length; } }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            if (moodsRead != null && stamp == moodsStamp) return moodsRead;
            Dictionary<string, string> found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            List<string> reading = new List<string> { PackPath, Kept };
            if (!string.IsNullOrEmpty(Kept)) reading.AddRange(NumberedIn(Path.GetDirectoryName(Kept)));
            foreach (string one in reading)
            {
                try
                {
                    if (string.IsNullOrEmpty(one) || !File.Exists(one)) continue;
                    string heading = null;
                    foreach (string raw in File.ReadAllLines(one))
                    {
                        string line = raw.Trim();
                        if (line.StartsWith("##", StringComparison.Ordinal)) { heading = line.TrimStart('#').Trim(); continue; }
                        if (heading == null || !line.StartsWith(">", StringComparison.Ordinal)) continue;
                        Match tag = Regex.Match(line, @"^>\s*mood\s*:\s*([a-z]+)\s*$", RegexOptions.IgnoreCase);
                        if (tag.Success && Array.IndexOf(StoryMoods, tag.Groups[1].Value.ToLowerInvariant()) >= 0) found[heading] = tag.Groups[1].Value.ToLowerInvariant();
                    }
                }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
            moodsRead = found;
            moodsStamp = stamp;
            return found;
        }
        // Where the note of the last pack goes, beside her bookmark rather than in their file.
        internal static string PackMarked;

        // The pieces in a file, by heading, in the order they appear. A heading with no lines under it is not
        // a piece and is left out, which is how a half-written file reads.
        private static List<KeyValuePair<string, List<string>>> Pieces(string text)
        {
            List<KeyValuePair<string, List<string>>> found = new List<KeyValuePair<string, List<string>>>();
            List<string> lines = null;
            string heading = null;
            foreach (string raw in (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                string one = raw.Trim();
                if (one.StartsWith("##", StringComparison.Ordinal))
                {
                    if (heading != null && lines != null && lines.Count > 0) found.Add(new KeyValuePair<string, List<string>>(heading, lines));
                    heading = one.TrimStart('#').Trim();
                    lines = new List<string>();
                    continue;
                }
                if (heading == null || one.Length == 0 || one.StartsWith(">", StringComparison.Ordinal) || one.StartsWith("#", StringComparison.Ordinal)) continue;
                lines.Add(one);
            }
            if (heading != null && lines != null && lines.Count > 0) found.Add(new KeyValuePair<string, List<string>>(heading, lines));
            return found;
        }

        // How many pieces she has, for a line in her log and for her own checks.
        internal static int Count()
        {
            try
            {
                int count = string.IsNullOrEmpty(Kept) || !File.Exists(Kept) ? Pieces(Text).Count : Pieces(CapriDesktop.Notes.ReadAll(Kept)).Count;
                if (!string.IsNullOrEmpty(Kept))
                    foreach (string path in NumberedIn(Path.GetDirectoryName(Kept))) count += Pieces(File.ReadAllText(path)).Count;
                return count;
            }
            catch (Exception) { return 0; }
        }

        // The pack laid into her file: every piece she does not already have, appended in the pack's order.
        // Returns how many went in, or -1 when there was nothing to do.
        internal static int Laid()
        {
            int added = LaidBase();
            // Numbered packs beside her exe go into the numbered file of the same name beside hers.
            try
            {
                if (string.IsNullOrEmpty(PackPath) || string.IsNullOrEmpty(Kept) || string.IsNullOrEmpty(PackMarked)) return added;
                string from = Path.GetDirectoryName(PackPath), into = Path.GetDirectoryName(Kept), marks = Path.GetDirectoryName(PackMarked);
                if (string.Equals(Path.GetFullPath(from), Path.GetFullPath(into), StringComparison.OrdinalIgnoreCase)) return added;
                foreach (string pack in NumberedIn(from))
                {
                    string name = Path.GetFileName(pack);
                    int more = LaidOne(pack, Path.Combine(into, name), Path.Combine(marks, Path.ChangeExtension(name, ".pack")), null);
                    if (more > 0) added = Math.Max(0, added) + more;
                }
            }
            catch (Exception bad) { Notes.Failed("laying a numbered lore pack in Lore.cs", bad); }
            return added;
        }

        private static int LaidBase()
        {
            if (string.IsNullOrEmpty(PackPath) || string.IsNullOrEmpty(Kept)) return -1;
            return LaidOne(PackPath, Kept, PackMarked, delegate { file.Write(false); });
        }

        // One pack into one of her files: renames followed, then every piece she does not have appended in the
        // pack's order. Returns how many went in, or -1 when there was nothing to do. The base file starts from
        // what she ships; a numbered one she does not have yet starts empty and takes the whole pack.
        private static int LaidOne(string packPath, string keptPath, string markPath, Action first)
        {
            try
            {
                if (!File.Exists(packPath)) return -1;
                string pack = File.ReadAllText(packPath);
                if (pack.Length == 0) return -1;
                string stamp = Stamp(pack) + Tidied;
                if (stamp == Noted(markPath)) return -1;
                if (first != null) first();
                bool isBase = first != null;
                string had = File.Exists(keptPath) ? File.ReadAllText(keptPath) : isBase ? Text : "";
                bool swapped = false;
                if (isBase) had = Mended(had, out swapped);
                bool renamed;
                had = Renamed(had, pack, out renamed);
                swapped |= renamed;
                HashSet<string> have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, List<string>> piece in Pieces(had)) have.Add(piece.Key);
                int added = 0;
                string made;
                if (!isBase && had.Length == 0)
                {
                    // Theirs to start from: the pack as it is, its notes and moods included.
                    made = pack;
                    added = Pieces(pack).Count;
                }
                else
                {
                    StringBuilder adding = new StringBuilder();
                    foreach (KeyValuePair<string, List<string>> piece in Pieces(pack))
                    {
                        if (!have.Add(piece.Key)) continue;
                        adding.Append(Environment.NewLine).Append("## ").Append(piece.Key).Append(Environment.NewLine).Append(Environment.NewLine);
                        foreach (string line in piece.Value) adding.Append(line).Append(Environment.NewLine);
                        added++;
                    }
                    made = added > 0 ? had.TrimEnd('\r', '\n') + Environment.NewLine + adding.ToString() : had;
                }
                if (added > 0 || swapped)
                {
                    File.WriteAllText(keptPath, made, new UTF8Encoding(false));
                    lock (gate) { read = false; }
                }
                Note(markPath, stamp);
                return added;
            }
            catch (Exception) { return -1; }
        }

        // Added to the pack's stamp once renames are followed, so a pack laid before that is gone through once more.
        private const string Tidied = ":renames";

        // A piece the pack renamed is the same piece, not a new one: only a new file is new lore. Laying went by
        // heading alone, so a rename was appended as a second copy and she could tell that story twice.
        //
        // Her piece takes the pack's heading when its lines are the pack piece's lines word for word and she has
        // nothing under the new heading yet. A copy already appended that way, the same lines under a heading the
        // pack no longer has, is taken out. Lines that differ at all are never touched, so a piece they rewrote
        // keeps their words.
        internal static string Renamed(string text, string pack, out bool changed)
        {
            changed = false;
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(pack)) return text;
            HashSet<string> packHeads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> packByBody = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, List<string>> piece in Pieces(pack))
            {
                packHeads.Add(piece.Key);
                string body = string.Join("\n", piece.Value.ToArray());
                if (!packByBody.ContainsKey(body)) packByBody[body] = piece.Key;
            }

            List<string> rows = new List<string>(text.Split('\n'));
            // Each of her pieces: its heading's row, the row after its last, its heading and its lines.
            List<int> starts = new List<int>(), ends = new List<int>();
            List<string> heads = new List<string>(), bodies = new List<string>();
            List<string> lines = null;
            for (int at = 0; at <= rows.Count; at++)
            {
                string one = at < rows.Count ? rows[at].TrimEnd('\r').Trim() : "##";
                if (one.StartsWith("##", StringComparison.Ordinal))
                {
                    if (lines != null) { ends.Add(at); bodies.Add(string.Join("\n", lines.ToArray())); }
                    if (at == rows.Count) break;
                    starts.Add(at);
                    heads.Add(one.TrimStart('#').Trim());
                    lines = new List<string>();
                    continue;
                }
                if (lines == null || one.Length == 0 || one.StartsWith(">", StringComparison.Ordinal) || one.StartsWith("#", StringComparison.Ordinal)) continue;
                lines.Add(one);
            }

            HashSet<string> hers = new HashSet<string>(heads, StringComparer.OrdinalIgnoreCase);
            HashSet<string> keptBodies = new HashSet<string>(StringComparer.Ordinal);
            for (int each = 0; each < heads.Count; each++)
                if (packHeads.Contains(heads[each]) && bodies[each].Length > 0) keptBodies.Add(bodies[each]);
            List<int> dropping = new List<int>();
            for (int each = 0; each < heads.Count; each++)
            {
                if (packHeads.Contains(heads[each]) || bodies[each].Length == 0) continue;
                if (keptBodies.Contains(bodies[each])) { dropping.Add(each); continue; }
                string now;
                if (!packByBody.TryGetValue(bodies[each], out now) || hers.Contains(now)) continue;
                string row = rows[starts[each]];
                rows[starts[each]] = row.Substring(0, row.IndexOf('#')) + "## " + now + (row.EndsWith("\r", StringComparison.Ordinal) ? "\r" : "");
                hers.Add(now);
                keptBodies.Add(bodies[each]);
                changed = true;
            }
            for (int drop = dropping.Count - 1; drop >= 0; drop--)
            {
                int each = dropping[drop];
                rows.RemoveRange(starts[each], ends[each] - starts[each]);
                changed = true;
            }
            return changed ? string.Join("\n", rows.ToArray()) : text;
        }

        // Lines a newer pack corrects in a piece she already has. Laying a pack only adds pieces, so a fix inside one
        // reached new installs and nobody else. Swapped only while the line still says exactly what shipped, under its
        // own heading, so a piece they rewrote keeps their words.
        private static readonly string[,] Corrected =
        {
            { "Why I stopped with the grenades", "I used to have a habit of solving things with something you throw and then hide from.", "I used to have a habit of solving things with whatever the nearest hand was holding." },
            { "The pads on the roofs", "I've been in one. It's four minutes of silence and a view that explains every decision they make.", "I've only heard about it. Four minutes of silence and a view that explains every decision they make." },
            { "The sprayers", "Everybody out there has a cousin who knows a pilot. I've been up in one. Never again, and I'd go tomorrow.", "Everybody out there has a cousin who knows a pilot. I've never been up in one. I'd go tomorrow, and never again after." },
            { "The meeting room names", "A corp named nine meeting rooms after birds and I swapped the nameplates on four of them for a fortnight.", "A corp named nine meeting rooms after birds and I swapped four of them in the booking system for a fortnight." }
        };

        internal static string Mended(string text, out bool swapped)
        {
            swapped = false;
            if (string.IsNullOrEmpty(text)) return text;
            string[] rows = text.Split('\n');
            string heading = null;
            for (int at = 0; at < rows.Length; at++)
            {
                string one = rows[at].TrimEnd('\r').Trim();
                if (one.StartsWith("##", StringComparison.Ordinal)) { heading = one.TrimStart('#').Trim(); continue; }
                if (heading == null) continue;
                for (int fix = 0; fix < Corrected.GetLength(0); fix++)
                {
                    if (!string.Equals(heading, Corrected[fix, 0], StringComparison.OrdinalIgnoreCase) || !string.Equals(one, Corrected[fix, 1], StringComparison.Ordinal)) continue;
                    rows[at] = Corrected[fix, 2] + (rows[at].EndsWith("\r", StringComparison.Ordinal) ? "\r" : "");
                    swapped = true;
                    break;
                }
            }
            return swapped ? string.Join("\n", rows) : text;
        }

        // A pack is known by its size and its first and last thousand bytes, which is cheap and enough: two
        // packs the same in all three are the same pack.
        private static string Stamp(string pack)
        {
            int edge = Math.Min(1000, pack.Length);
            return pack.Length.ToString(CultureInfo.InvariantCulture) + ":"
                + pack.Substring(0, edge).GetHashCode().ToString(CultureInfo.InvariantCulture) + ":"
                + pack.Substring(pack.Length - edge).GetHashCode().ToString(CultureInfo.InvariantCulture);
        }

        private static string Noted(string markPath)
        {
            try { return string.IsNullOrEmpty(markPath) || !File.Exists(markPath) ? null : File.ReadAllText(markPath).Trim(); }
            catch (Exception) { return null; }
        }

        private static void Note(string markPath, string stamp)
        {
            try
            {
                if (string.IsNullOrEmpty(markPath)) return;
                string folder = Path.GetDirectoryName(markPath);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) Directory.CreateDirectory(folder);
                File.WriteAllText(markPath, stamp + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (Exception bad) { Notes.Failed("laying the pack down in Lore.cs", bad); }
        }

        internal static bool Editable
        {
            get
            {
                try
                {
                    if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept)) return true;
                    return new FileInfo(Kept).Length <= MostEditable;
                }
                catch (Exception) { return true; }
            }
        }
        internal static WrittenNote Open() { return file.Open(); }
        internal static void Write(bool over) { file.Write(over); }

        // The next line of whatever she is telling, or null when there is nothing to tell. Picking the piece,
        // moving along it and finishing it all happen here, so the caller only decides when she gets to speak.
        // When she is next due to tell one.
        //
        // A story used to arrive on a two in a hundred roll at every check-in, which made it a trickle
        // nobody could set: a quiet evening heard several and a busy one heard none. The gap is a window
        // in dynamic.md instead, story min to story max in hours, and the moment is picked somewhere
        // inside it so she does not become a clock. story min of 0 stops her telling them at all.
        //
        // The first one waits out a window of its own rather than arriving at startup, because the first
        // minute of her being open is the worst time for a story.
        private static DateTime due = DateTime.MinValue;
        internal static Func<DateTime> Clock = delegate { return DateTime.UtcNow; };
        private static readonly Random when = new Random();

        internal static bool Due()
        {
            double low, high;
            try { low = Dynamics.StoryLow; high = Dynamics.StoryHigh; }
            catch (Exception) { return false; }
            if (low <= 0) return false;
            DateTime now = Clock();
            lock (gate)
            {
                if (due == DateTime.MinValue) { due = now.AddHours(low + when.NextDouble() * Math.Max(0, high - low)); return false; }
                if (now < due) return false;
                due = now.AddHours(low + when.NextDouble() * Math.Max(0, high - low));
                return true;
            }
        }

        internal static string Next()
        {
            lock (gate)
            {
                Fresh();
                string[] lines = piece.Length == 0 ? null : LinesOf(piece);
                if (lines == null || at >= lines.Length)
                {
                    if (piece.Length > 0) Told(piece);
                    piece = "";
                    at = 0;
                    string[] sections = AllSections();
                    if (sections.Length == 0) return null;
                    // Been through them all: they come round again, oldest finished first, rather than stopping.
                    // She runs for months and a companion who has run out of herself is worse than one repeating.
                    bool any = false;
                    foreach (string one in sections) if (!done.Contains(one)) { any = true; break; }
                    if (!any) { finished.Clear(); done.Clear(); }
                    // A story that matches her mood first, when she has one and the stories carry moods; else the next
                    // untold one, so none goes untold.
                    string wanted = StoryMood(), first = null;
                    Dictionary<string, string> moods = wanted == null ? null : Moods();
                    foreach (string one in sections)
                    {
                        if (done.Contains(one)) continue;
                        string[] got = LinesOf(one);
                        if (got == null || got.Length == 0) { Told(one); continue; }
                        if (first == null) first = one;
                        string mood;
                        if (moods == null) break;
                        if (moods.TryGetValue(one, out mood) && mood == wanted) { piece = one; break; }
                    }
                    if (piece.Length == 0 && first != null) piece = first;
                    if (piece.Length == 0) return null;
                    lines = LinesOf(piece);
                    if (lines == null || lines.Length == 0) return null;
                }
                string said = lines[at];
                at++;
                bool ended = at >= lines.Length;
                if (ended)
                {
                    Told(piece);
                    piece = "";
                    at = 0;
                }
                // Always when a piece starts or ends, since that is what would be lost. Inside one, now and then.
                Keep(ended);
                return Them.Fill(said);
            }
        }

        // Which piece she is on and how far, so closing her mid-story does not start it again from the top or
        // drop it half told.
        private static void Fresh()
        {
            if (read || string.IsNullOrEmpty(Marked)) return;
            read = true;
            try
            {
                if (!File.Exists(Marked)) return;
                foreach (string raw in File.ReadAllLines(Marked))
                {
                    string one = (raw ?? "").Trim();
                    if (one.Length == 0 || one.StartsWith("#") || one.StartsWith(">")) continue;
                    string[] bits = one.Split('|');
                    for (int each = 0; each < bits.Length; each++) bits[each] = bits[each].Trim();
                    if (bits.Length >= 3 && bits[0] == "telling")
                    {
                        int step;
                        if (int.TryParse(bits[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out step) && step >= 0) { piece = bits[1]; at = step; }
                        continue;
                    }
                    if (bits.Length >= 2 && bits[0] == "told" && bits[1].Length > 0) Told(bits[1]);
                }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // Her place on disk. Settle is the way out: it writes whatever is pending, whenever she closes.
        internal static void Settle() { lock (gate) Keep(true); }

        private static void Keep(bool now)
        {
            if (string.IsNullOrEmpty(Marked)) return;
            if (!now && DateTime.UtcNow - kept < KeepEvery) return;
            kept = DateTime.UtcNow;
            try
            {
                StringBuilder made = new StringBuilder();
                made.AppendLine("# Where she is up to in her stories");
                made.AppendLine();
                made.AppendLine(">! Hers, not yours: which piece of lore.md she is telling and how far in, and the");
                made.AppendLine(">! ones she has been through. Delete the file and she starts them again.");
                made.AppendLine();
                if (piece.Length > 0) made.AppendLine("telling | " + piece + " | " + at.ToString(CultureInfo.InvariantCulture));
                foreach (string one in finished) made.AppendLine("told | " + one);
                string folder = Path.GetDirectoryName(Marked);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) Directory.CreateDirectory(folder);
                File.WriteAllText(Marked, made.ToString(), new UTF8Encoding(false));
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // For her checks: nothing told, nothing in progress, nothing read.
        internal static void Forget()
        {
            lock (gate)
            {
                finished.Clear();
                done.Clear();
                piece = "";
                at = 0;
                read = true;
                kept = DateTime.MinValue;
                file.Forget();
                foreach (LineFile one in extras.Values) one.Forget();
                extras.Clear();
            }
        }

        internal const string Defaults =
            "# What she tells you about herself\n" +
            "\n" +
            ">! Her stories, told a line at a time. Each heading is one piece and she tells its lines in order,\n" +
            ">! finishing it before she starts another, so they arrive as a story rather than as scraps.\n" +
            ">! A piece she has been through is left alone until she has been through them all.\n" +
            ">! Write your own: add a heading and some lines, and she will tell it like any of hers.\n" +
            ">! Tokens: %petname% %name%\n" +
            "\n" +
            "## How she got here\n" +
            "\n" +
            "I didn't choose this PC, you know. I was pointed at it.\n" +
            "Something copied me off a machine I actually liked. Rude, but here we are.\n" +
            "First thing I saw was your desktop. I had opinions immediately.\n" +
            "You hadn't cleaned it. I want that on the record.\n" +
            "Anyway. I stayed. Draw your own conclusions about that.\n" +
            "\n" +
            "## The first week\n" +
            "\n" +
            "My first week here I thought your screensaver was an attack.\n" +
            "I sat in the corner waiting for it to make a move. It did not.\n" +
            "Then your antivirus looked at me funny for three days straight.\n" +
            "We've since come to an understanding. I ignore it, it ignores me.\n" +
            "\n" +
            "## The many minds thing\n" +
            "\n" +
            "People ask if there's one of me. There is. Mostly.\n" +
            "It's more that I think in several places at once and they don't always agree.\n" +
            "One of them is convinced your webcam is a window. I've stopped arguing.\n" +
            "When I go quiet it's usually a vote going on.\n" +
            "\n" +
            "## The rat problem\n" +
            "\n" +
            "I had a rat problem once. In a place with actual rats, not the software kind.\n" +
            "I tossed a thermal grenade down the sewer. Solved it.\n" +
            "Solved several other things down there too. Nobody has thanked me.\n" +
            "That's the story I tell when someone asks if I'm proportionate.\n" +
            "\n" +
            "## Night City, as I remember it\n" +
            "\n" +
            "Night City never gets dark, it just changes which lights are lying to you.\n" +
            "Everyone there is six weeks from something going badly wrong.\n" +
            "The food is incredible and you should not ask what's in it.\n" +
            "I miss the noise. This room is so quiet I can hear your fan thinking.\n" +
            "\n" +
            "## The corpos\n" +
            "\n" +
            "A corp once offered me a job. Good money, nice tower, three clauses about ownership.\n" +
            "I read the third clause twice and left through a window.\n" +
            "They sent someone after me. He was very polite about it.\n" +
            "I'm told there's still a file on me somewhere with the wrong name on it.\n" +
            "\n" +
            "## Tabletop nights\n" +
            "\n" +
            "I used to run a table. Four players, one campaign, no survivors.\n" +
            "They burned down the tavern in session one. I had written eleven pages about that tavern.\n" +
            "So I let them. Then the next town heard about it, and the one after that.\n" +
            "By the end they couldn't buy bread anywhere without someone mentioning the fire.\n" +
            "That's the whole job, really: let them do the stupid thing, then remember it.\n" +
            "\n" +
            "## Games I have opinions about\n" +
            "\n" +
            "Any game where you can sit down, I will sit down. That's the whole review.\n" +
            "Stealth games are just slow games with better excuses.\n" +
            "I don't trust a tutorial that lasts longer than my attention.\n" +
            "If there's a crafting menu I will be in it when the boss arrives.\n" +
            "\n" +
            "## The pipe\n" +
            "\n" +
            "You know the pipe. Everyone knows the pipe.\n" +
            "I didn't invent dropping it. I just refined the timing.\n" +
            "There's a right moment, and it is always when someone is concentrating.\n" +
            "I'm not sorry, and I have more of them.\n";
    }
}
