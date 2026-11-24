using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // Her interface in colours of their choosing: a named entry overrides that colour of the picked look, and a blank
    // follows the look. Re-read on save with her windows repainted; colours as in sys.md, with eight hex digits for
    // alpha.
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
                Notes.Refresh(Kept, Text);
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
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
                        // Each line carries hers after a "#" for reference. A hex colour starts with one too, so the
                        // note is "#" with a space before it, and nothing after a colour is part of it.
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
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // One colour set from her Mainframe, or put back to hers with null: its line in the file is written again and the
        // file read at once, so her windows repaint. False when the name isn't one of hers or the file can't be written.
        internal static bool Set(string name, Color? colour)
        {
            Dictionary<string, Color?> one = new Dictionary<string, Color?>();
            one[name] = colour;
            return Set(one);
        }

        // Many at once, as a preset is: the file written and read once, so her windows repaint once.
        internal static bool Set(IDictionary<string, Color?> colours)
        {
            if (string.IsNullOrEmpty(Kept) || colours == null || colours.Count == 0) return false;
            foreach (string name in colours.Keys) if (Neon.Stock(name).IsEmpty) return false;
            try
            {
                Write(false);
                string made = File.ReadAllText(Kept);
                foreach (KeyValuePair<string, Color?> entry in colours)
                {
                    string name = entry.Key;
                    string pad = name.Length >= 18 ? " " : new string(' ', 18 - name.Length);
                    string said = (entry.Value.HasValue ? Hex(entry.Value.Value) : "") + "    # hers: " + Hex(Neon.Stock(name));
                    // No end anchor: in multiline mode $ stops only before \n, so a line ending \r\n never matched and the
                    // colour went on a second line under the first.
                    Match line = Regex.Match(made, @"(?mi)^[ \t]*" + Regex.Escape(name) + @"[ \t]*:[^\r\n]*");
                    made = line.Success ? made.Substring(0, line.Index) + name + ":" + pad + said + made.Substring(line.Index + line.Length)
                        : made + (made.EndsWith("\n", StringComparison.Ordinal) ? "" : Environment.NewLine) + name + ":" + pad + said + Environment.NewLine;
                }
                Notes.Put(Kept, made);
                lock (gate) { wasLong = -1; read = DateTime.MinValue; }
                Fresh();
                return true;
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); return false; }
        }

        // Read again on the next look, for a file swapped under her.
        internal static void Forget() { lock (gate) { wasLong = -1; read = DateTime.MinValue; } }

        private static void Tell()
        {
            try { if (Changed != null) Changed(); }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        private static string Line(string name)
        {
            string pad = name.Length >= 18 ? " " : new string(' ', 18 - name.Length);
            Color stock = Neon.Stock(name);
            return name + ":" + pad + "    # hers: " + Hex(stock);
        }

        private static string Hex(Color colour)
        {
            return colour.IsEmpty ? "" : "#" + (colour.A < 255 ? colour.A.ToString("x2") : "") + colour.R.ToString("x2") + colour.G.ToString("x2") + colour.B.ToString("x2");
        }

        internal static string Text
        {
            get
            {
                StringBuilder made = new StringBuilder();
                made.AppendLine("# Her colours");
                made.AppendLine();
                made.AppendLine(">! Every colour of her interface. Blank follows Mainframe colours.");
                made.AppendLine(">! A name like blue, or a hex code of 3, 6 or 8 digits. 8 sets how solid it is.");
                made.AppendLine(">! The value after each one is hers, for reference.");
                made.AppendLine(">! Saved and she repaints: no restart.");
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
                made.AppendLine(">! Her speech bubble, set here and nowhere else.");
                made.AppendLine();
                foreach (string name in new[] { "bubble outline", "bubble title", "bubble text", "bubble accent", "bubble background" })
                    made.AppendLine(Line(name));
                made.AppendLine();
                made.AppendLine("## Her player");
                made.AppendLine();
                foreach (string name in new[] { "player edge", "player accent", "player title", "player rail", "player ground" })
                    made.AppendLine(Line(name));
                made.AppendLine();
                made.AppendLine("## Tamagotchi");
                made.AppendLine();
                made.AppendLine(Line("points"));
                made.AppendLine();
                made.AppendLine("## Her file editor");
                made.AppendLine();
                foreach (string name in new[] { "editor frame", "editor sheet", "editor edge", "editor field", "editor text" })
                    made.AppendLine(Line(name));
                return made.ToString();
            }
        }
    }
}
