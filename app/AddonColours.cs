using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;

namespace CapriDesktop
{
    // Colours a mod names for itself through Ui.Colour: one tree per mod in its own colours.md, kept apart from her
    // palette. One left as the mod's own follows Mainframe colours; Custom colours lists each under the mod's
    // fold-down.
    internal static class AddonColours
    {
        internal const string FileName = "colours.md";
        internal const int Most = 16, Longest = 40;

        // Where a mod's data folder is, by its id, and the mods installed now as id and name; set by her once her mods are
        // loaded, swapped by the checks.
        internal static Func<string, string> FolderOf;
        internal static Func<List<KeyValuePair<string, string>>> Installed;
        // A tree changed: the mod's id.
        internal static event Action<string> Changed;

        private sealed class Tree
        {
            internal readonly List<string> Order = new List<string>();
            internal readonly Dictionary<string, Color> Usual = new Dictionary<string, Color>(StringComparer.Ordinal);
            internal readonly Dictionary<string, Color> Theirs = new Dictionary<string, Color>(StringComparer.Ordinal);
            internal long Stamp = -1;
        }

        private static readonly object gate = new object();
        private static readonly Dictionary<string, Tree> trees = new Dictionary<string, Tree>(StringComparer.OrdinalIgnoreCase);

        // A name as a colour's key: no line breaks, none of the marks presets and the file use, and kept short.
        internal static string Clean(string name)
        {
            StringBuilder kept = new StringBuilder();
            foreach (char one in (name ?? "").Trim())
                if (!char.IsControl(one) && one != ':' && one != ',' && one != '|' && one != '=' && one != '#') kept.Append(one);
            string made = kept.ToString().Trim();
            return made.Length > Longest ? made.Substring(0, Longest).Trim() : made;
        }

        private static string PathOf(string id)
        {
            string folder = FolderOf == null || string.IsNullOrEmpty(id) ? null : FolderOf(id);
            return folder == null ? null : Path.Combine(folder, FileName);
        }

        private static long StampOf(string path)
        {
            try
            {
                FileInfo about = new FileInfo(path);
                return about.Exists ? about.LastWriteTimeUtc.Ticks + about.Length : 0;
            }
            catch (IOException) { return 0; }
            catch (UnauthorizedAccessException) { return 0; }
        }

        // The mod's tree: read the first time, and again only when asked to look (Custom colours opening, or a check), so a
        // mod asking for its colour as it draws costs no disk.
        private static Tree Of(string id, bool look = false)
        {
            Tree tree;
            bool known = trees.TryGetValue(id, out tree);
            if (!known) trees[id] = tree = new Tree();
            if (known && !look) return tree;
            string path = PathOf(id);
            if (path == null) return tree;
            long stamp = StampOf(path);
            if (stamp == tree.Stamp) return tree;
            tree.Order.Clear();
            tree.Usual.Clear();
            tree.Theirs.Clear();
            tree.Stamp = stamp;
            if (stamp == 0) return tree;
            try
            {
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = (raw ?? "").Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith(">", StringComparison.Ordinal)) continue;
                    int mark = line.IndexOf(':');
                    if (mark <= 0) continue;
                    string name = Clean(line.Substring(0, mark)), said = line.Substring(mark + 1).Trim(), usual = null;
                    int note = said.IndexOf("# usual:", StringComparison.Ordinal);
                    if (note >= 0) { usual = said.Substring(note + 8).Trim(); said = said.Substring(0, note).Trim(); }
                    if (name.Length == 0 || tree.Order.Contains(name) || tree.Order.Count >= Most) continue;
                    tree.Order.Add(name);
                    Color? was = usual == null ? null : SysLines.Mixed(usual);
                    tree.Usual[name] = was.HasValue ? Color.FromArgb(255, was.Value) : Color.Gray;
                    Color? theirs = said.Length == 0 ? null : SysLines.Mixed(said);
                    if (theirs.HasValue) tree.Theirs[name] = Color.FromArgb(255, theirs.Value);
                }
            }
            catch (IOException caughtOne) { Brain.Caught(caughtOne); }
            catch (UnauthorizedAccessException caughtOne) { Brain.Caught(caughtOne); }
            return tree;
        }

        private static void Save(string id, string name, Tree tree)
        {
            string path = PathOf(id);
            if (path == null) return;
            StringBuilder made = new StringBuilder();
            made.AppendLine("# " + (name ?? id) + "'s colours");
            made.AppendLine();
            made.AppendLine(">! Set in Mainframe > Settings > GENERAL SETTINGS > Custom colours, or here. Blank follows the mod's own.");
            made.AppendLine();
            foreach (string one in tree.Order)
            {
                Color theirs;
                string pad = one.Length >= 18 ? " " : new string(' ', 18 - one.Length);
                made.AppendLine(one + ":" + pad + (tree.Theirs.TryGetValue(one, out theirs) ? ColourPresets.Hex(theirs) : "") + "    # usual: " + ColourPresets.Hex(tree.Usual[one]));
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, made.ToString(), new UTF8Encoding(false));
                tree.Stamp = StampOf(path);
            }
            catch (IOException caughtOne) { Brain.Caught(caughtOne); }
            catch (UnauthorizedAccessException caughtOne) { Brain.Caught(caughtOne); }
        }

        private static string NameOf(string id)
        {
            if (Installed != null) foreach (KeyValuePair<string, string> mod in Installed()) if (string.Equals(mod.Key, id, StringComparison.OrdinalIgnoreCase)) return mod.Value;
            return id;
        }

        // A mod's colour by its name: theirs, or the mod's own in the look picked. The first ask for a name declares it, up
        // to sixteen a mod; past that, or with no name, the mod's own colour comes back and nothing is kept.
        internal static Color Declare(string id, string name, Color usual)
        {
            string key = Clean(name);
            usual = Color.FromArgb(255, usual);
            if (key.Length == 0 || string.IsNullOrEmpty(id)) return Toned(usual);
            lock (gate)
            {
                Tree tree = Of(id);
                if (!tree.Order.Contains(key))
                {
                    if (tree.Order.Count >= Most) return Toned(usual);
                    tree.Order.Add(key);
                    tree.Usual[key] = usual;
                    Save(id, NameOf(id), tree);
                }
                else if (tree.Usual[key].ToArgb() != usual.ToArgb())
                {
                    tree.Usual[key] = usual;
                    Save(id, NameOf(id), tree);
                }
                Color theirs;
                return tree.Theirs.TryGetValue(key, out theirs) ? theirs : Toned(usual);
            }
        }

        private static Color Toned(Color usual) { return Neon.Tone(usual.R, usual.G, usual.B); }

        // The colours a mod has named, in the order it named them.
        internal static List<string> Names(string id) { lock (gate) return new List<string>(Of(id).Order); }

        internal static Color? TheirOwn(string id, string name)
        {
            lock (gate) { Color theirs; return Of(id).Theirs.TryGetValue(Clean(name), out theirs) ? theirs : (Color?)null; }
        }

        internal static Color Shown(string id, string name)
        {
            lock (gate)
            {
                Tree tree = Of(id);
                string key = Clean(name);
                Color colour;
                if (tree.Theirs.TryGetValue(key, out colour)) return colour;
                return tree.Usual.TryGetValue(key, out colour) ? Toned(colour) : Color.Empty;
            }
        }

        // Set or clear some of one mod's colours in one write, then tell it. A name it hasn't named yet is kept for when it
        // does, as a preset may carry one.
        internal static bool Set(string id, IDictionary<string, Color?> colours)
        {
            if (string.IsNullOrEmpty(id) || PathOf(id) == null || colours == null || colours.Count == 0) return false;
            bool moved = false;
            lock (gate)
            {
                Tree tree = Of(id);
                foreach (KeyValuePair<string, Color?> entry in colours)
                {
                    string key = Clean(entry.Key);
                    if (key.Length == 0) continue;
                    if (!tree.Order.Contains(key))
                    {
                        if (!entry.Value.HasValue || tree.Order.Count >= Most) continue;
                        tree.Order.Add(key);
                        tree.Usual[key] = entry.Value.Value;
                    }
                    Color was;
                    bool had = tree.Theirs.TryGetValue(key, out was);
                    if (entry.Value.HasValue && (!had || was.ToArgb() != Color.FromArgb(255, entry.Value.Value).ToArgb())) { tree.Theirs[key] = Color.FromArgb(255, entry.Value.Value); moved = true; }
                    else if (!entry.Value.HasValue && had) { tree.Theirs.Remove(key); moved = true; }
                }
                if (moved) Save(id, NameOf(id), tree);
            }
            if (moved) Tell(id);
            return true;
        }

        internal static void Tell(string id)
        {
            try { if (Changed != null) Changed(id); }
            catch (Exception caughtOne) { Brain.Caught(caughtOne); }
        }

        // The mods installed now that have named colours, as id and name, in their order.
        internal static List<KeyValuePair<string, string>> Trees()
        {
            List<KeyValuePair<string, string>> found = new List<KeyValuePair<string, string>>();
            if (Installed == null) return found;
            foreach (KeyValuePair<string, string> mod in Installed())
                lock (gate) if (Of(mod.Key, true).Order.Count > 0) found.Add(mod);
            return found;
        }

        // Forgets what was read, for the checks.
        internal static void Forget() { lock (gate) trees.Clear(); }
    }
}
