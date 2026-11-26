using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CapriDesktop
{
    // Tree mods: a .capri-tree file, a panel and its rows as plain lines (PanelTree.cs), no code. Its header is comment
    // lines like a code mod's: "// capri-tree: 1", then id, name, author, version and description.
    internal sealed partial class Mods
    {
        internal const string TreeExtension = ".capri-tree";

        private Mod LoadTree(string file, HashSet<string> off)
        {
            ModFile read = ReadTree(File.ReadAllText(file));
            if (!string.Equals(read.Id, Path.GetFileNameWithoutExtension(file), StringComparison.OrdinalIgnoreCase)) throw new ModException("its file name doesn't match its id, " + read.Id + ".");
            return new Mod { File = read, Path = file, Tree = true, On = !off.Contains(read.Id) };
        }

        // The header of a tree file, as a mod's file; ModException when it isn't one.
        internal static ModFile ReadTree(string text)
        {
            Dictionary<string, string> head = TreeHead(text);
            string version;
            if (!head.TryGetValue("capri-tree", out version) || version != "1") throw new ModException("A tree mod starts with // capri-tree: 1.");
            ModFile read = new ModFile { WakeNames = new string[0], HeardAs = new string[0], Games = new string[0] };
            string value;
            read.Id = head.TryGetValue("id", out value) ? value : "";
            if (read.Id.Length == 0 || read.Id.Length > 64 || read.Id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new ModException("A tree mod needs an id: letters, numbers and dashes.");
            read.Name = head.TryGetValue("name", out value) ? value : read.Id;
            read.Author = head.TryGetValue("author", out value) ? value : "";
            read.Version = head.TryGetValue("version", out value) ? value : "";
            read.Description = head.TryGetValue("description", out value) ? value : "";
            return read;
        }

        // Its "// key: value" lines, the first of each key kept.
        internal static Dictionary<string, string> TreeHead(string text)
        {
            Dictionary<string, string> head = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in (text ?? "").TrimStart('﻿').Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                if (!line.StartsWith("//", StringComparison.Ordinal)) continue;
                int mark = line.IndexOf(':');
                if (mark <= 2) continue;
                string key = line.Substring(2, mark - 2).Trim();
                if (!head.ContainsKey(key)) head[key] = line.Substring(mark + 1).Trim();
            }
            return head;
        }

        // Its tree: every line but the header's.
        internal static string TreeBody(string text)
        {
            List<string> body = new List<string>();
            foreach (string raw in (text ?? "").TrimStart('﻿').Replace("\r\n", "\n").Split('\n'))
                if (!raw.Trim().StartsWith("//", StringComparison.Ordinal)) body.Add(raw);
            return string.Join("\n", body.ToArray());
        }

        private Mod ImportTree(string file)
        {
            string text;
            try
            {
                if (new FileInfo(file).Length > Largest) throw new ModException("That tree is too big. The limit is " + Largest / 1024 + " KB.");
                text = File.ReadAllText(file);
            }
            catch (IOException error) { throw new ModException("Couldn't read that file: " + error.Message); }
            catch (UnauthorizedAccessException error) { throw new ModException("Couldn't read that file: " + error.Message); }
            ModFile read = ReadTree(text);
            string home = Path.Combine(PlaceFor(read.Id, file, text), read.Id + TreeExtension);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(home));
                string writing = home + ".new";
                File.WriteAllText(writing, text, new UTF8Encoding(false));
                if (File.Exists(home)) File.Delete(home);
                File.Move(writing, home); LeftLoose(file, home);
                HashSet<string> off = Off();
                if (off.Remove(read.Id)) WriteOff(off);
            }
            catch (IOException error) { throw new ModException("Couldn't save that tree: " + error.Message); }
            catch (UnauthorizedAccessException error) { throw new ModException("Couldn't save that tree: " + error.Message); }
            Load();
            return All.Find(mod => mod.File.Id == read.Id);
        }
    }

    internal sealed partial class Companion
    {
        // The tree mods laid now, so a reload lays only what changed and takes away what went.
        private readonly Dictionary<string, string> treesLaid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // After her mods load: each tree mod that is on is laid once (again only when its file changed); one turned off
        // or taken out gives its panels back.
        internal void LayTrees()
        {
            if (ModList == null || mainframe == null) return;
            Dictionary<string, string> now = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Mod mod in ModList.All)
            {
                if (!mod.On) continue;
                // A pack's "tree" lines are laid as a tree file's body.
                if (!mod.Tree) { if (mod.File != null && mod.File.TreeLines != null && mod.File.TreeLines.Length > 0) now[mod.File.Id] = string.Join("\n", mod.File.TreeLines); continue; }
                string text;
                try { text = File.ReadAllText(mod.Path); }
                catch (IOException caughtOne) { Brain.Caught(caughtOne); continue; }
                catch (UnauthorizedAccessException caughtOne) { Brain.Caught(caughtOne); continue; }
                now[mod.File.Id] = text;
            }
            foreach (string id in new List<string>(treesLaid.Keys))
            {
                string was = treesLaid[id], is_;
                if (now.TryGetValue(id, out is_) && is_ == was) continue;
                string gone = id;
                OnWindow(delegate { mainframe.RemoveModPanels(gone); });
                DropLayout(gone);
                treesLaid.Remove(id);
            }
            foreach (KeyValuePair<string, string> one in now)
            {
                if (treesLaid.ContainsKey(one.Key)) continue;
                string why = LayTreeText(one.Key, Mods.TreeBody(one.Value));
                if (why != null) Brain.Log("mods", -1, one.Key + " (tree): " + why);
                treesLaid[one.Key] = one.Value;
            }
        }

#if CHECKS
        // A pack's "tree" field: read from its JSON and laid as a tree file's body, its panel on Settings. Null when right.
        internal string TreePackCheck()
        {
            const string id = "pack-tree-check";
            string json = "{\"id\":\"" + id + "\",\"name\":\"Pack tree\",\"tree\":[\"settings\",\"-PACK TREE\",\"--Glow [switch] (setting: glow)\"]}";
            ModFile read;
            using (MemoryStream body = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                read = (ModFile)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(ModFile)).ReadObject(body);
            if (read.TreeLines == null || read.TreeLines.Length != 3) return "the tree field read " + (read.TreeLines == null ? "nothing" : read.TreeLines.Length + " lines");
            string why = LayTreeText(id, string.Join("\n", read.TreeLines));
            for (int i = 0; i < 20 && mainframe.TreePanelShown(id) == null; i++) { System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(20); }
            string shown = mainframe.TreePanelShown(id);
            mainframe.RemoveModPanels(id);
            if (why != null || shown == null || shown.IndexOf("Glow", StringComparison.Ordinal) < 0) return "laid " + (why ?? "fine") + ", shown " + (shown ?? "nothing");
            return null;
        }
#endif
    }
}
