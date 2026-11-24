using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;

namespace CapriDesktop
{
    // Colours a mod names for itself through Ui.Colour: one tree per mod in its own colours.md, kept apart from her
    // palette. One left as the mod's own follows Mainframe colours; Custom colours lists each under the mod's fold-down,
    // after her own items that keep colours the same way.
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
                    if (theirs.HasValue) tree.Theirs[name] = theirs.Value;
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
            foreach (Item item in Items) if (string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)) return item.Name;
            if (Installed != null) foreach (KeyValuePair<string, string> mod in Installed()) if (string.Equals(mod.Key, id, StringComparison.OrdinalIgnoreCase)) return mod.Value;
            return id;
        }

        // A mod's colour by its name: theirs, or the mod's own in the look picked. The first ask for a name declares it, up
        // to sixteen a mod; past that, or with no name, the mod's own colour comes back and nothing is kept.
        internal static Color Declare(string id, string name, Color usual)
        {
            string key = Clean(name);
            usual = Color.FromArgb(255, usual);
            if (key.Length == 0 || string.IsNullOrEmpty(id)) return Toned(id, usual);
            lock (gate)
            {
                Tree tree = Of(id);
                if (!tree.Order.Contains(key))
                {
                    if (tree.Order.Count >= Most) return Toned(id, usual);
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
                return tree.Theirs.TryGetValue(key, out theirs) ? theirs : Toned(id, usual);
            }
        }

        // A mod's own colour follows Mainframe colours; one of her own items keeps its own.
        private static Color Toned(string id, Color usual) { return IsHers(id) ? usual : Neon.Tone(usual.R, usual.G, usual.B); }

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
                return tree.Usual.TryGetValue(key, out colour) ? Toned(id, colour) : Color.Empty;
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
                    if (entry.Value.HasValue && (!had || was.ToArgb() != entry.Value.Value.ToArgb())) { tree.Theirs[key] = entry.Value.Value; moved = true; }
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

        // Her own items with colours, then the mods installed now that have named colours, as id and name, in their order.
        internal static List<KeyValuePair<string, string>> Trees()
        {
            List<KeyValuePair<string, string>> found = new List<KeyValuePair<string, string>>();
            if (FolderOf != null) foreach (Item item in Items) found.Add(new KeyValuePair<string, string>(item.Id, item.Name));
            if (Installed == null) return found;
            // A mod with one of her own items' ids (her screensaver) is that item's fold, listed once.
            foreach (KeyValuePair<string, string> mod in Installed())
                lock (gate) if (!found.Exists(one => one.Key == mod.Key) && Of(mod.Key, true).Order.Count > 0) found.Add(mod);
            return found;
        }

        // Forgets what was read, for the checks.
        internal static void Forget() { lock (gate) trees.Clear(); }

        // Her own items that keep colours like a mod's, each a fold under ADD-ONS that doesn't follow the Mainframe's shift.
        // Their colours start as Night City drew them.
        internal const string Screensaver = "capri-screensaver", Tear = "capri-tear", Shatter = "capri-shatter", Takeover = "capri-takeover", Bolts = "capri-bolts";

        private sealed class Item
        {
            internal readonly string Id, Name;
            internal readonly string[] Names;
            internal readonly Color[] Usual;
            internal Item(string id, string name, params object[] colours)
            {
                Id = id;
                Name = name;
                Names = new string[colours.Length / 2];
                Usual = new Color[colours.Length / 2];
                for (int i = 0; i < Names.Length; i++) { Names[i] = (string)colours[i * 2]; Usual[i] = Color.FromArgb(255, Color.FromArgb((int)colours[i * 2 + 1])); }
            }
        }

        private static readonly Item[] Items =
        {
            new Item(Screensaver, "Screensaver", "Colour", 0x5EF0F6, "Hexagons", 0xFF1E26, "Background", 0x04060A),
            new Item(Tear, "Tear prank", "Red", 0xE82440, "Crimson", 0xB00620, "Ink", 0x08060A, "Violet", 0x6E1EAA, "Hot pink", 0xFF788C,
                "Haze", 0xD20C60, "Halo", 0xE60A50, "Bright violet", 0x7A3CFF, "Mint", 0x1EF08A, "Rim red", 0xEC0644, "Specks", 0xFFDEE8, "White", 0xFFFFFF),
            new Item(Shatter, "Shatter prank", "Violet", 0x7A3CFF, "Mint", 0x1EF08A, "Red", 0xE82440, "Pink", 0xFF2E88, "White", 0xFFFFFF),
            new Item(Takeover, "Takeover prank", "Red", 0xE82440, "Pink", 0xFF2E88, "Violet", 0x7A3CFF, "Mint", 0x1EF08A, "Cyan", 0x28E6FF,
                "Rim red", 0xEC0644, "Dark", 0x0A080E, "Panel top", 0x22162C, "Panel foot", 0x0C0812, "White", 0xFFFFFF),
            new Item(Bolts, "Cursor bolts", "Red", 0xFF0046, "Black", 0x08060C)
        };

        internal static bool IsHers(string id)
        {
            foreach (Item item in Items) if (string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // One of her items' colours by name: theirs, or its own.
        internal static Color Hers(string id, string name)
        {
            foreach (Item item in Items)
                if (string.Equals(item.Id, id, StringComparison.Ordinal))
                    for (int i = 0; i < item.Names.Length; i++)
                        if (item.Names[i] == name) return Declare(id, name, item.Usual[i]);
            return Color.Gray;
        }

        // Her own items' folds: listed first under ADD-ONS before any is drawn, in Night City's colours whatever the look while
        // a mod's own follows it, alpha kept, and the screensaver's two sys.md colours taken out once with their notes.
        internal static void Check(List<string> checks, string output)
        {
            string folder = Path.Combine(output, "addon-colours-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Func<string, string> folderWas = FolderOf;
            Func<List<KeyValuePair<string, string>>> installedWas = Installed;
            try
            {
                Directory.CreateDirectory(folder);
                FolderOf = delegate(string id) { return Path.Combine(folder, id + "-data"); };
                Installed = delegate { return new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("tint-test", "Tint Test") }; };
                Forget();
                Lay();
                List<KeyValuePair<string, string>> listed = Trees();
                string[] wanted = { "Screensaver", "Tear prank", "Shatter prank", "Takeover prank", "Cursor bolts" };
                if (listed.Count < wanted.Length) throw new Exception("Her own items aren't all under ADD-ONS: " + listed.Count);
                for (int i = 0; i < wanted.Length; i++) if (listed[i].Value != wanted[i]) throw new Exception("Her own items aren't first under ADD-ONS, in order: " + listed[i].Value);
                if (string.Join(",", Names(Screensaver).ToArray()) != "Colour,Hexagons,Background") throw new Exception("The screensaver's fold doesn't list Colour, Hexagons and Background before it is drawn.");
                Color saver = Hers(Screensaver, "Colour"), bolt = Hers(Bolts, "Red");
                Color mod = Declare("tint-test", "Tint", Color.FromArgb(94, 240, 246));
                Neon.UseLook(1);
                bool kept = Hers(Screensaver, "Colour").ToArgb() == saver.ToArgb() && Hers(Bolts, "Red").ToArgb() == bolt.ToArgb();
                bool modFollows = Declare("tint-test", "Tint", Color.FromArgb(94, 240, 246)).ToArgb() != mod.ToArgb();
                Neon.UseLook(0);
                if (saver.ToArgb() != Color.FromArgb(94, 240, 246).ToArgb() || bolt.ToArgb() != Color.FromArgb(255, 0, 70).ToArgb()) throw new Exception("Her own items don't start in Night City's colours.");
                if (!kept) throw new Exception("Her own items followed the Mainframe's look.");
                if (!modFollows) throw new Exception("A mod's own colour stopped following the Mainframe's look.");
                Set(Screensaver, new Dictionary<string, Color?> { { "Background", Color.FromArgb(128, 16, 32, 48) } });
                Forget();
                if (Hers(Screensaver, "Background").ToArgb() != Color.FromArgb(128, 16, 32, 48).ToArgb()) throw new Exception("A see-through colour lost its alpha in its file.");
                string sys = Path.Combine(folder, "sys.md");
                File.WriteAllText(sys, SysLines.SaverHeading + "\n\n>! colour:      a name or hex code. Blank is hers.\n>! background:  blank is near black.\nscreensaver: 1\n"
                    + SysLines.SaverColourKey + ": #ff0000\n" + SysLines.SaverGroundKey + ": blue\n");
                Dictionary<string, string> taken = SysLines.TakeSaverColours(sys);
                string left = File.ReadAllText(sys);
                if (taken.Count != 2 || taken[SysLines.SaverColourKey] != "#ff0000" || taken[SysLines.SaverGroundKey] != "blue") throw new Exception("The screensaver's colours weren't taken out of sys.md as written.");
                if (left.Contains(SysLines.SaverColourKey) || left.Contains(">! colour:") || left.Contains(">! background:") || !left.Contains("screensaver: 1")) throw new Exception("sys.md kept the screensaver's colour lines, or lost another: " + left);
                if (SysLines.TakeSaverColours(sys).Count != 0) throw new Exception("The screensaver's colours were taken out of sys.md twice.");
                checks.Add("PASS her own items keep colours like a mod's: the screensaver, three pranks and her cursor bolts are first under ADD-ONS before any is drawn, stay in their own colours in any look while a mod's follows, keep alpha, and the screensaver's sys.md colours are taken out once");
            }
            finally
            {
                FolderOf = folderWas;
                Installed = installedWas;
                Forget();
                Neon.UseLook(0);
            }
        }

        // Her items' colours written to their files in order once her folders are known, so Custom colours lists each one
        // before it is first drawn.
        internal static void Lay()
        {
            if (FolderOf == null) return;
            foreach (Item item in Items) for (int i = 0; i < item.Names.Length; i++) Declare(item.Id, item.Names[i], item.Usual[i]);
        }
    }
}
