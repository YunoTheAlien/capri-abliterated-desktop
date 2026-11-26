using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace CapriDesktop
{
    // Her interface in colours of their choosing: a named entry overrides that colour of the picked look, and a blank
    // follows the look. Kept with her settings, set only in Custom colours; layout.md, where they used to be, is carried in
    // once and kept as layout.md.bak.
    internal static class Layout
    {
        internal const string FileName = "layout.md";
        // The old file's place, for the one carry.
        internal static string Kept;

        private static readonly object gate = new object();
        // What was last applied, so a look that finds nothing new costs nothing.
        private static string applied;
        private static List<string> memory = new List<string>();

        // Where the rows ("name=#hex") are kept: her settings, set by her window; a list of its own until then and for the
        // checks.
        internal static Func<List<string>> Load = delegate { return memory; };
        internal static Action<List<string>> Store = delegate(List<string> rows) { memory = rows; };

        // Her windows, so a colour set shows without a restart.
        internal static Action Changed;

        // Called on her tick and after a set: applies the rows when they changed.
        internal static void Fresh()
        {
            try
            {
                List<string> rows = Load() ?? new List<string>();
                string now = string.Join("\n", rows.ToArray());
                bool moved;
                lock (gate)
                {
                    if (now == applied) return;
                    moved = Neon.NoneOfTheirs();
                    foreach (string row in rows)
                    {
                        int mark = row.IndexOf('=');
                        if (mark <= 0) continue;
                        Color? colour = SysLines.Mixed(row.Substring(mark + 1).Trim());
                        if (colour.HasValue && Neon.Theirs(row.Substring(0, mark).Trim(), colour)) moved = true;
                    }
                    applied = now;
                }
                if (moved) Tell();
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // One colour set from her Mainframe, or put back to hers with null. False when the name isn't one of hers.
        internal static bool Set(string name, Color? colour)
        {
            Dictionary<string, Color?> one = new Dictionary<string, Color?>();
            one[name] = colour;
            return Set(one);
        }

        // Many at once, as a preset is: kept and applied once, so her windows repaint once.
        internal static bool Set(IDictionary<string, Color?> colours)
        {
            if (colours == null || colours.Count == 0) return false;
            foreach (string name in colours.Keys) if (Neon.Stock(name).IsEmpty) return false;
            try
            {
                List<string> rows = new List<string>(Load() ?? new List<string>());
                foreach (KeyValuePair<string, Color?> entry in colours)
                {
                    string key = entry.Key;
                    rows.RemoveAll(row => row.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase));
                    if (entry.Value.HasValue) rows.Add(key + "=" + Hex(entry.Value.Value));
                }
                Store(rows);
                Fresh();
                return true;
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); return false; }
        }

        // Her colour by name as she keeps it, or null when it follows the look.
        internal static string Row(string name)
        {
            foreach (string row in Load() ?? new List<string>())
                if (row.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase)) return row.Substring(name.Length + 1);
            return null;
        }

        // The one carry: layout.md's colours into her settings, where none are kept yet, then the file kept as
        // layout.md.bak and never read again. How many came across.
        internal static int CarryOld()
        {
            if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept)) return 0;
            int carried = 0;
            try
            {
                List<string> rows = new List<string>(Load() ?? new List<string>());
                if (rows.Count == 0)
                {
                    foreach (string raw in File.ReadAllLines(Kept))
                    {
                        string one = (raw ?? "").Trim();
                        if (one.Length == 0 || one.StartsWith("#", StringComparison.Ordinal) || one.StartsWith(">", StringComparison.Ordinal)) continue;
                        int mark = one.IndexOf(':');
                        if (mark <= 0) continue;
                        string name = one.Substring(0, mark).Trim(), said = one.Substring(mark + 1).Trim();
                        // Each line carried hers after " #" for reference; a hex colour starts with "#" too.
                        int note = said.IndexOf(" #", StringComparison.Ordinal);
                        if (note > 0) said = said.Substring(0, note).Trim();
                        if (said.Length == 0 || said.StartsWith("#", StringComparison.Ordinal) && said.Contains(" ")) continue;
                        Color? colour = SysLines.Mixed(said);
                        if (!colour.HasValue || Neon.Stock(name).IsEmpty) continue;
                        rows.RemoveAll(row => row.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase));
                        rows.Add(name + "=" + Hex(colour.Value));
                        carried++;
                    }
                    if (carried > 0) Store(rows);
                }
                // A .bak already there is theirs: this one takes a timed spare name, the newest three kept.
                string old = Kept + ".bak";
                File.Move(Kept, File.Exists(old) ? SpareFiles.Name(Kept) : old);
                SpareFiles.Prune(Kept, Notes.MostSpares);
                Fresh();
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            return carried;
        }

        // Read again on the next look, for a store swapped under her.
        internal static void Forget() { lock (gate) applied = null; }

        private static void Tell()
        {
            try { if (Changed != null) Changed(); }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        internal static string Hex(Color colour)
        {
            return colour.IsEmpty ? "" : "#" + (colour.A < 255 ? colour.A.ToString("x2") : "") + colour.R.ToString("x2") + colour.G.ToString("x2") + colour.B.ToString("x2");
        }
    }
}
