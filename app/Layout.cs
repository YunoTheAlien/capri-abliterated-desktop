using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // Her interface, in colours of their choosing.
    //
    // Mainframe colours picks one of the looks she ships, and that swaps every window at once. This is the
    // same palette, one entry at a time: a colour named here is used wherever that entry is drawn, and a
    // colour left blank follows whichever look is picked. So somebody can take Night City and make one thing
    // green without rebuilding a look, and the look they are on still works underneath.
    //
    // Read when she starts and whenever the file is saved, with her windows repainted on the spot. The same
    // colour writing her sys.md lines take: a name like blue, or a hex code with or without the hash, three
    // digits or six, and eight to say how solid it is.
    internal static class Layout
    {
        internal const string FileName = "layout.md";
        internal static string Kept;

        private static readonly object gate = new object();
        private static long wasLong = -1;
        private static DateTime read = DateTime.MinValue;

        // Her windows, so a colour saved into the file shows without a restart.
        internal static Action Changed;

        internal static WrittenNote Open() { Write(false); return new WrittenNote(Kept, Text, gate); }

        internal static void Write(bool over)
        {
            if (string.IsNullOrEmpty(Kept)) return;
            try
            {
                string folder = Path.GetDirectoryName(Kept);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) Directory.CreateDirectory(folder);
                if (over || !File.Exists(Kept))
                {
                    File.WriteAllText(Kept, Text, new UTF8Encoding(false));
                    lock (gate) { wasLong = -1; read = DateTime.MinValue; }
                    return;
                }
                // Every entry she ships that the file has not got, added with nothing set, so a palette entry
                // added by a later build turns up in a file written before it existed.
                string had = File.ReadAllText(Kept);
                StringBuilder adding = new StringBuilder();
                foreach (string name in Neon.Names)
                    if (!System.Text.RegularExpressions.Regex.IsMatch(had, @"(?mi)^[ \t]*" + System.Text.RegularExpressions.Regex.Escape(name) + @"[ \t]*:"))
                        adding.AppendLine(Line(name));
                if (adding.Length > 0)
                {
                    if (!had.EndsWith("\n", StringComparison.Ordinal)) had += Environment.NewLine;
                    File.WriteAllText(Kept, had + adding, new UTF8Encoding(false));
                    lock (gate) { wasLong = -1; read = DateTime.MinValue; }
                }
                Notes.Refresh(Kept, Text);
            }
            catch (Exception) { }
        }

        // Called on her tick, like her other files: it only touches the disk when the file has changed.
        internal static void Fresh()
        {
            if (string.IsNullOrEmpty(Kept)) return;
            try
            {
                FileInfo about = new FileInfo(Kept);
                if (!about.Exists)
                {
                    bool had;
                    lock (gate) { had = Neon.NoneOfTheirs(); wasLong = -1; read = DateTime.MinValue; }
                    if (had) Tell();
                    return;
                }
                long stamp = about.LastWriteTimeUtc.Ticks + about.Length;
                lock (gate) if (stamp == wasLong && read != DateTime.MinValue) return;
                bool moved = false;
                lock (gate)
                {
                    moved = Neon.NoneOfTheirs();
                    foreach (string raw in File.ReadAllLines(Kept))
                    {
                        string one = (raw ?? "").Trim();
                        if (one.Length == 0 || one.StartsWith("#", StringComparison.Ordinal) || one.StartsWith(">", StringComparison.Ordinal)) continue;
                        int mark = one.IndexOf(':');
                        if (mark <= 0) continue;
                        string name = one.Substring(0, mark).Trim(), said = one.Substring(mark + 1).Trim();
                        // Each line carries hers after a "#", for reference. A hex colour starts with one too,
                        // so the note is the "#" with a space in front of it and anything after a colour is not
                        // part of it. Without this, setting a colour and leaving the note read as neither.
                        int note = said.IndexOf(" #", StringComparison.Ordinal);
                        if (note > 0) said = said.Substring(0, note).Trim();
                        if (said.Length == 0) continue;
                        Color? colour = SysLines.Mixed(said);
                        if (colour.HasValue && Neon.Theirs(name, colour)) moved = true;
                    }
                    wasLong = stamp;
                    read = DateTime.Now;
                }
                if (moved) Tell();
            }
            catch (Exception) { }
        }

        // A colour set somewhere else, written in here. Her bubble's four were in sys.md as well and that
        // pair won, so moving them had to bring whatever was set across or somebody's bubble would change
        // colour on the build that tidied it. Only into a line nobody has filled in: theirs stays theirs.
        internal static void Carry(string name, string said)
        {
            try
            {
                if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept) || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(said)) return;
                string had = File.ReadAllText(Kept);
                Match line = Regex.Match(had, @"(?mi)^[ \t]*" + Regex.Escape(name) + @"[ \t]*:[ \t]*([^\r\n]*)$");
                if (!line.Success) return;
                // A line they have already filled in is theirs and is left alone.
                string inside = line.Groups[1].Value;
                if (inside.Trim().Length > 0 && !inside.TrimStart().StartsWith("#")) return;
                string keeping = inside.TrimStart().StartsWith("#") ? "    " + inside.TrimStart() : "";
                string pad = name.Length >= 18 ? " " : new string(' ', 18 - name.Length);
                string made = had.Substring(0, line.Index) + name + ":" + pad + said + keeping + had.Substring(line.Index + line.Length);
                Notes.Put(Kept, made);
                lock (gate) { wasLong = -1; read = DateTime.MinValue; }
            }
            catch (Exception) { }
        }

        private static void Tell()
        {
            try { if (Changed != null) Changed(); }
            catch (Exception) { }
        }

        private static string Line(string name)
        {
            string pad = name.Length >= 18 ? " " : new string(' ', 18 - name.Length);
            Color stock = Neon.Stock(name);
            return name + ":" + pad + "    # hers: " + Hex(stock);
        }

        private static string Hex(Color colour)
        {
            return colour.IsEmpty ? "" : "#" + colour.R.ToString("x2") + colour.G.ToString("x2") + colour.B.ToString("x2");
        }

        internal static string Text
        {
            get
            {
                StringBuilder made = new StringBuilder();
                made.AppendLine("# Her colours");
                made.AppendLine();
                made.AppendLine("> Every colour of her interface. Set one and it is used wherever that part of her is drawn;");
                made.AppendLine("> leave it blank and it follows Mainframe colours, which swaps the whole set at once.");
                made.AppendLine("> A name like blue, or a hex code like #1affd5, with or without the hash. Three digits work");
                made.AppendLine("> too, and eight set how solid it is. The value after each one is hers, for reference.");
                made.AppendLine("> Saved and she repaints: no restart.");
                made.AppendLine();
                made.AppendLine("## Her windows");
                made.AppendLine();
                foreach (string name in new[] { "ground", "glow", "band", "field", "well", "well edge", "menu", "sheet" })
                    made.AppendLine(Line(name));
                made.AppendLine();
                made.AppendLine("## Lines and edges");
                made.AppendLine();
                foreach (string name in new[] { "rule", "edge", "dash", "brim", "lit", "deep", "deep edge" })
                    made.AppendLine(Line(name));
                made.AppendLine();
                made.AppendLine("## Words");
                made.AppendLine();
                foreach (string name in new[] { "text", "faint", "rose", "on ink", "sheet text", "sheet faint" })
                    made.AppendLine(Line(name));
                made.AppendLine();
                made.AppendLine("## What stands out");
                made.AppendLine();
                foreach (string name in new[] { "cyan", "hot", "amber", "hover", "pressed" })
                    made.AppendLine(Line(name));
                made.AppendLine();
                made.AppendLine("## Her speech bubble");
                made.AppendLine();
                made.AppendLine("> Her speech bubble, set here and nowhere else.");
                made.AppendLine();
                foreach (string name in new[] { "bubble outline", "bubble title", "bubble text", "bubble accent", "bubble background" })
                    made.AppendLine(Line(name));
                return made.ToString();
            }
        }
    }
}
