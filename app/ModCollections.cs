using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    internal sealed partial class Preferences
    {
        // Mod collections, each "name<tab>id;id;..." in load order; the one in use; and each part's pick, "collection<tab>part<tab>pick".
        [DataMember] public List<string> ModCollections;
        [DataMember] public string ModCollectionInUse;
        [DataMember] public List<string> ModPartPicks;
    }

    // Collections: Stock is her core with nothing laid over it, always there and never changed; every other one is her core
    // with its mods laid on top in their order, and a pick for each part they share. One is in use, and its combined file
    // can be saved.
    internal sealed partial class Companion
    {
        internal const string StockCollection = "Stock", FirstCollection = "My mods";
        private const int LongestCollectionName = 40;

        private void StartCollections()
        {
            ModList.Order = delegate { return OrderInUse(); };
            ModList.CoreOnly = delegate { return CollectionInUse == StockCollection; };
            FileSlots.PartPick = delegate(string part) { return PartPickOf(CollectionInUse, part); };
            FileSlots.PartText = delegate(string part)
            {
                string file = PartFile(CollectionInUse, part);
                return File.Exists(file) ? File.ReadAllText(file) : null;
            };
            if (preferences.ModCollections == null)
            {
                // An install from before collections: her mods as they are now, in the order they were installed, as one
                // collection in use, so nothing she lays changes.
                List<string> order = new List<string>();
                foreach (Mod one in ModList.Layered()) order.Add(one.File.Id);
                preferences.ModCollections = new List<string> { FirstCollection + "\t" + string.Join(";", order.ToArray()) };
                preferences.ModCollectionInUse = FirstCollection;
                SavePreferences();
                return;
            }
            ModList.Reapply();
        }

        // The order of the collection in use, with any mod it doesn't list yet (one just installed or turned on) put on top,
        // as the newest layer.
        private List<string> OrderInUse()
        {
            string use = CollectionInUse;
            List<string> order = CollectionOrder(use);
            if (use == StockCollection || RowOf(use) < 0) return order;
            bool grew = false;
            foreach (Mod mod in ModList.All)
                if (mod.On && (!mod.Code || mod.File.Overrides != null && mod.File.Overrides.Length > 0) && !order.Contains(mod.File.Id)) { order.Insert(Placed(order, mod.File), mod.File.Id); grew = true; }
            if (grew)
            {
                preferences.ModCollections[RowOf(use)] = use + "\t" + string.Join(";", order.ToArray());
                SavePreferences();
            }
            return order;
        }

        // Where a mod first goes: after every mod it loads after, else before the first it loads before, else on top. Only
        // when it is added; their drag decides after that.
        private static int Placed(List<string> order, ModFile file)
        {
            int after = -1, before = order.Count;
            foreach (string id in file.LoadAfter ?? new string[0]) after = Math.Max(after, order.IndexOf(id));
            foreach (string id in file.LoadBefore ?? new string[0]) { int at = order.IndexOf(id); if (at >= 0) before = Math.Min(before, at); }
            return after >= 0 ? after + 1 : before;
        }

        internal List<string> CollectionNames
        {
            get
            {
                List<string> names = new List<string> { StockCollection };
                foreach (string row in preferences.ModCollections ?? new List<string>())
                {
                    string name = row.Split('\t')[0];
                    if (name.Length > 0 && !names.Contains(name)) names.Add(name);
                }
                return names;
            }
        }

        internal string CollectionInUse
        {
            get
            {
                string named = preferences.ModCollectionInUse;
                return !string.IsNullOrEmpty(named) && CollectionNames.Contains(named) ? named : StockCollection;
            }
        }

        private int RowOf(string name)
        {
            List<string> rows = preferences.ModCollections ?? new List<string>();
            return rows.FindIndex(row => row.Split('\t')[0] == name);
        }

        // Its mods' ids, first laid first; Stock has none.
        internal List<string> CollectionOrder(string name)
        {
            int at = RowOf(name);
            if (name == StockCollection || at < 0) return new List<string>();
            string[] parts = preferences.ModCollections[at].Split('\t');
            List<string> ids = new List<string>();
            if (parts.Length > 1) foreach (string id in parts[1].Split(';')) if (id.Length > 0 && !ids.Contains(id)) ids.Add(id);
            return ids;
        }

        // The mods a collection lays, by name, in order: those it lists that are installed and on, then any on that it doesn't
        // list yet (a mod turned on later goes on top).
        internal List<KeyValuePair<string, string>> CollectionLayers(string name)
        {
            List<KeyValuePair<string, string>> layers = new List<KeyValuePair<string, string>>();
            if (name == StockCollection) return layers;
            List<string> order = CollectionOrder(name);
            List<Mod> on = ModList.All.FindAll(mod => mod.On && (!mod.Code || mod.File.Overrides != null && mod.File.Overrides.Length > 0));
            foreach (string id in order)
            {
                Mod found = on.Find(mod => mod.File.Id == id);
                if (found != null) layers.Add(new KeyValuePair<string, string>(id, found.File.Name ?? id));
            }
            foreach (Mod mod in on) if (!order.Contains(mod.File.Id)) layers.Add(new KeyValuePair<string, string>(mod.File.Id, mod.File.Name ?? mod.File.Id));
            return layers;
        }

        internal void SetCollectionOrder(string name, List<string> ids)
        {
            int at = RowOf(name);
            if (name == StockCollection || at < 0) return;
            preferences.ModCollections[at] = name + "\t" + string.Join(";", ids.ToArray());
            SavePreferences();
            if (name == CollectionInUse) ModList.Reapply();
        }

        internal void UseCollection(string name)
        {
            if (!CollectionNames.Contains(name) || name == CollectionInUse) return;
            preferences.ModCollectionInUse = name;
            SavePreferences();
            ModList.Reapply();
        }

        // A copy to build on: Stock's is empty, any other keeps its order and its picks.
        internal string CopyCollection(string name)
        {
            string made = FreeName(name == StockCollection ? FirstCollection : name + " copy");
            List<string> rows = preferences.ModCollections ?? (preferences.ModCollections = new List<string>());
            rows.Add(made + "\t" + string.Join(";", CollectionOrder(name).ToArray()));
            List<string> picks = preferences.ModPartPicks ?? (preferences.ModPartPicks = new List<string>());
            foreach (string pick in picks.ToArray())
            {
                string[] parts = pick.Split('\t');
                if (parts.Length == 3 && parts[0] == name) picks.Add(made + "\t" + parts[1] + "\t" + parts[2]);
            }
            SavePreferences();
            return made;
        }

        private string FreeName(string wanted)
        {
            string named = Tidy(wanted);
            if (named.Length == 0) named = FirstCollection;
            List<string> taken = CollectionNames;
            if (!taken.Exists(one => string.Equals(one, named, StringComparison.OrdinalIgnoreCase))) return named;
            for (int n = 2; ; n++)
            {
                string tried = Tidy(named + " " + n);
                if (!taken.Exists(one => string.Equals(one, tried, StringComparison.OrdinalIgnoreCase))) return tried;
            }
        }

        private static string Tidy(string name)
        {
            string tidy = Regex.Replace((name ?? "").Replace('\t', ' ').Replace(';', ' '), @"\s+", " ").Trim();
            return tidy.Length > LongestCollectionName ? tidy.Substring(0, LongestCollectionName).Trim() : tidy;
        }

        // A new name: never Stock's, never one in use by another; its picks and edited parts go with it.
        internal bool RenameCollection(string name, string wanted)
        {
            int at = RowOf(name);
            string named = Tidy(wanted);
            if (name == StockCollection || at < 0 || named.Length == 0 || named == name) return false;
            if (CollectionNames.Exists(one => one != name && string.Equals(one, named, StringComparison.OrdinalIgnoreCase))) return false;
            string[] parts = preferences.ModCollections[at].Split('\t');
            preferences.ModCollections[at] = named + "\t" + (parts.Length > 1 ? parts[1] : "");
            List<string> picks = preferences.ModPartPicks ?? new List<string>();
            for (int i = 0; i < picks.Count; i++)
            {
                string[] pick = picks[i].Split('\t');
                if (pick.Length == 3 && pick[0] == name) picks[i] = named + "\t" + pick[1] + "\t" + pick[2];
            }
            try
            {
                string from = PartFolder(name), to = PartFolder(named);
                if (Directory.Exists(from) && !Directory.Exists(to)) Directory.Move(from, to);
            }
            catch (IOException caughtOne) { Brain.Caught(caughtOne); }
            catch (UnauthorizedAccessException caughtOne) { Brain.Caught(caughtOne); }
            if (preferences.ModCollectionInUse == name) preferences.ModCollectionInUse = named;
            SavePreferences();
            return true;
        }

        // Taken out with its picks; the one in use goes back to Stock. Its edited parts and saved file stay on disk.
        internal void RemoveCollection(string name)
        {
            int at = RowOf(name);
            if (name == StockCollection || at < 0) return;
            bool used = name == CollectionInUse;
            preferences.ModCollections.RemoveAt(at);
            if (preferences.ModPartPicks != null) preferences.ModPartPicks.RemoveAll(pick => pick.StartsWith(name + "\t", StringComparison.Ordinal));
            if (used) preferences.ModCollectionInUse = StockCollection;
            SavePreferences();
            if (used) ModList.Reapply();
        }

        internal string PartPickOf(string name, string part)
        {
            string head = name + "\t" + part + "\t";
            string row = (preferences.ModPartPicks ?? new List<string>()).Find(one => one.StartsWith(head, StringComparison.OrdinalIgnoreCase));
            return row == null ? null : row.Substring(head.Length);
        }

        // A part's pick: a mod's id, Merge or Edit; null puts it back to the load order.
        internal void SetPartPick(string name, string part, string pick)
        {
            if (name == StockCollection) return;
            List<string> picks = preferences.ModPartPicks ?? (preferences.ModPartPicks = new List<string>());
            string head = name + "\t" + part + "\t";
            picks.RemoveAll(one => one.StartsWith(head, StringComparison.OrdinalIgnoreCase));
            if (pick != null) picks.Add(head + pick);
            SavePreferences();
            if (name == CollectionInUse) ModList.Reapply();
        }

        private string PartFolder(string name) { return Path.Combine(ModList.Folder, "collections", Safe(name)); }

        internal string PartFile(string name, string part) { return Path.Combine(PartFolder(name), Safe(part) + ".md"); }

        private static string Safe(string name)
        {
            string safe = Regex.Replace(name ?? "", @"[^\p{L}\p{N} ._-]+", "-").Trim(' ', '.', '-');
            return safe.Length == 0 ? "part" : safe;
        }

        // Edit in MD editor: the part's file, made from the text used now (the winner's, or every side merged) when it
        // has none yet, and picked as the part's text.
        internal string EditPart(string name, string part)
        {
            string file = PartFile(name, part);
            if (!File.Exists(file))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, (FileSlots.HeldText(part) ?? "") + "\n", new UTF8Encoding(false));
            }
            SetPartPick(name, part, FileSlots.EditPick);
            return file;
        }

        // Her editor on that part's file, a new one each time so its tab is there; saving lays the collection again.
        private FileEditor partEditor;

        internal void OpenPart(string file)
        {
            FileEditor.PartPath = file;
            if (partEditor != null && !partEditor.Over && !partEditor.IsDisposed) partEditor.Close();
            partEditor = new FileEditor();
            partEditor.Saved += delegate { ModList.Reapply(); mainframe.ConflictsChanged(); };
            partEditor.Motion = preferences.Motion;
            partEditor.Open(FileEditor.PartName);
        }

        // The collection in use as one combined pack: every part as it is laid now, picks applied, each part once however
        // many mods touch it. Saved beside her mods, not among them, so it is never laid twice.
        internal string SaveCollection()
        {
            string name = CollectionInUse;
            if (name == StockCollection) return null;
            StringBuilder made = new StringBuilder();
            string id = "collection-" + Regex.Replace(Safe(name).ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
            if (id == "collection-") id = "collection-mods";
            made.Append("{\n  \"format\": 2,\n  \"id\": ").Append(Voice.Quote(id.Length > 48 ? id.Substring(0, 48).Trim('-') : id)).Append(",\n  \"name\": ").Append(Voice.Quote(name));
            made.Append(",\n  \"version\": \"1.0.0\",\n  \"author\": \"You\",\n  \"description\": ").Append(Voice.Quote("Her core with " + CollectionLayers(name).Count + " mods laid on it.")).Append(",\n  \"overrides\": [");
            List<KeyValuePair<string, string>> parts = FileSlots.HeldParts();
            for (int i = 0; i < parts.Count; i++)
            {
                bool whole = parts[i].Key.IndexOf(" > ", StringComparison.Ordinal) < 0;
                made.Append(i == 0 ? "\n" : ",\n").Append("    { \"slot\": ").Append(Voice.Quote(parts[i].Key)).Append(whole ? ", \"whole\": true" : "").Append(", \"text\": ").Append(Voice.Quote(parts[i].Value)).Append(" }");
            }
            made.Append("\n  ]\n}\n");
            string file = Path.Combine(ModList.Folder, "collections", Safe(name) + Mods.Extension);
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, made.ToString(), new UTF8Encoding(false));
            return file;
        }
        // Collections and Conflicts: order, picks, Merge, Edit, Copy, Use, Stock and Save, through the window.
#if CHECKS
        private void SmokeConflictsWindow(List<string> checks, string output)
        {
            Mods mods = ModList;
            Func<ModFile, string, bool> askWas = mainframe.ModAsk;
            mainframe.ModAsk = delegate { return true; };
            List<string> collectionsWas = preferences.ModCollections == null ? null : new List<string>(preferences.ModCollections);
            List<string> picksWas = preferences.ModPartPicks == null ? null : new List<string>(preferences.ModPartPicks);
            string useWas = preferences.ModCollectionInUse, pairWas = preferences.PackPicks;
            Func<string, string, string, string> write = delegate(string id, string name, string word)
            {
                string file = Path.Combine(output, id + Mods.Extension);
                File.WriteAllText(file, "{\"format\":2,\"id\":\"" + id + "\",\"name\":\"" + name + "\",\"version\":\"1\",\"author\":\"Smoke\",\"description\":\"A test.\","
                    + "\"overrides\":[{\"slot\":\"lines.md > openers warm\",\"text\":\"" + word + " opener.\\nShared line.\"},{\"slot\":\"chess.md\",\"whole\":true,\"text\":\"# Chess\\n\\n" + word + " chess.\"}]}");
                return file;
            };
            const string lines = "lines.md > openers warm", chess = "chess.md", set = "Smoke set";
            List<string> wrong = new List<string>();
            try
            {
                foreach (Mod old in mods.All.ToArray()) mods.Remove(old);
                preferences.ModCollections = new List<string> { set + "\t" };
                preferences.ModCollectionInUse = set;
                preferences.ModPartPicks = null;
                preferences.PackPicks = null;
                foreach (string[] pack in new[] { new[] { "night-owl", "Night Owl", "Owl" }, new[] { "sleepy-cat", "Sleepy Cat", "Cat" }, new[] { "red-fox", "Red Fox", "Fox" } })
                {
                    if (!mainframe.ImportMod(write(pack[0], pack[1], pack[2]))) throw new Exception(pack[1] + " was refused: " + mainframe.ModStatusText);
                    mainframe.ConflictPutAway();
                }
                FileSlots.Clash onLines = FileSlots.Clashes().Find(one => one.Slot == lines), onChess = FileSlots.Clashes().Find(one => one.Slot == chess);
                if (onLines == null || onLines.Mods.Count != 3 || !onLines.Joins || onChess == null || onChess.Joins || FileSlots.Holder(lines) != "Red Fox")
                    wrong.Add("three packs on one part: " + (onLines == null ? "no clash" : onLines.Mods.Count + " mods, joins " + onLines.Joins) + ", chess joins " + (onChess != null && onChess.Joins) + ", held by " + FileSlots.Holder(lines));
                SetCollectionOrder(set, new List<string> { "red-fox", "sleepy-cat", "night-owl" });
                if (FileSlots.Holder(lines) != "Night Owl") wrong.Add("the order didn't move the winner: " + FileSlots.Holder(lines));
                SetPartPick(set, chess, "sleepy-cat");
                if (FileSlots.HeldText(chess) != "# Chess\n\nCat chess.") wrong.Add("a pick didn't hold against the order: " + FileSlots.HeldText(chess));
                SetPartPick(set, lines, FileSlots.MergePick);
                string merged = FileSlots.HeldText(lines) ?? "";
                if (merged.IndexOf("Owl opener.", StringComparison.Ordinal) < 0 || merged.IndexOf("Cat opener.", StringComparison.Ordinal) < 0 || merged.IndexOf("Fox opener.", StringComparison.Ordinal) < 0
                    || merged.IndexOf("Shared line.", StringComparison.Ordinal) != merged.LastIndexOf("Shared line.", StringComparison.Ordinal)) wrong.Add("Merge didn't join each side's lines once: " + merged.Replace("\n", " / "));
                SetPartPick(set, chess, FileSlots.MergePick);
                if (FileSlots.Holder(chess) != "Night Owl") wrong.Add("Merge on a whole file wasn't left to the order: " + FileSlots.Holder(chess));
                SetPartPick(set, chess, "sleepy-cat");
                string part = EditPart(set, lines);
                if (!File.Exists(part) || File.ReadAllText(part).IndexOf("Cat opener.", StringComparison.Ordinal) < 0) wrong.Add("Edit didn't start from the part as laid");
                File.WriteAllText(part, "Edited opener.");
                mods.Reapply();
                if (FileSlots.HeldText(lines) != "Edited opener.") wrong.Add("Edit didn't use their file: " + FileSlots.HeldText(lines));

                // The window.
                string shownSets = mainframe.ConflictsCollections, shownOrder = mainframe.ConflictsOrder, shownClashes = mainframe.ConflictsClashes("night-owl");
                if (!shownSets.StartsWith("Stock | *" + set + " (Core, Your Own, Red Fox, Sleepy Cat, Night Owl)", StringComparison.Ordinal)) wrong.Add("collections shown: " + shownSets);
                if (shownOrder != "1 Core | 2 Your Own | 3 Red Fox (2) | 4 Sleepy Cat (2) | 5 Night Owl (2)") wrong.Add("order shown: " + shownOrder);
                if (shownClashes != lines + ": Edited | " + chess + ": Sleepy Cat") wrong.Add("clashes shown: " + shownClashes);
                if (mainframe.ConflictsCrowdedNow != null) wrong.Add("the window is crowded: " + mainframe.ConflictsCrowdedNow);
                mainframe.ConflictsPicture(Path.Combine(output, "conflicts-window.png"));
                // One window: the pair in its title bar switches the part, in the same place.
                mainframe.ShowModPart(false, null);
                Point makerAt = mainframe.ModPartAt(false);
                mainframe.PressModPart(true);
                string onConflicts = mainframe.ModPartShown;
                Point clashesAt = mainframe.ModPartAt(true);
                mainframe.PressModPart(false);
                if (onConflicts != "Conflicts" || clashesAt != makerAt || mainframe.ModPartShown != "Mod maker") wrong.Add("the pair didn't switch the part in one place: " + onConflicts + " at " + clashesAt + " from " + makerAt + ", then " + mainframe.ModPartShown);
                mainframe.CloseModHelper();
                SetPartPick(set, lines, null);
                mainframe.ConflictsDrag("Red Fox", "Night Owl", true);
                List<string> afterDrag = CollectionOrder(set);
                if (FileSlots.Holder(lines) != "Red Fox" || afterDrag.Count != 3 || afterDrag[2] != "red-fox") wrong.Add("a drag didn't move the order: " + string.Join(", ", afterDrag.ToArray()) + ", held by " + FileSlots.Holder(lines));

                // Copy, Use, rename, remove, Stock.
                mainframe.ConflictsPickCollection(set);
                mainframe.ConflictsCopy();
                string copy = set + " copy";
                if (!CollectionNames.Contains(copy) || string.Join(";", CollectionOrder(copy).ToArray()) != string.Join(";", CollectionOrder(set).ToArray()) || PartPickOf(copy, chess) != "sleepy-cat")
                    wrong.Add("Copy didn't keep the order and picks");
                mainframe.ConflictsPickCollection(copy);
                mainframe.ConflictsUse();
                if (CollectionInUse != copy) wrong.Add("Use didn't put the copy in use");
                if (!RenameCollection(copy, "Second set") || CollectionInUse != "Second set" || RenameCollection("Second set", set) || RenameCollection(StockCollection, "Mine")) wrong.Add("rename: a free name, a taken one and Stock");
                RemoveCollection("Second set");
                if (CollectionInUse != StockCollection || FileSlots.Holder(lines) != null || FileSlots.Holder(chess) != null) wrong.Add("removing the one in use didn't go back to Stock, her core alone: " + CollectionInUse + ", " + FileSlots.Holder(lines));
                if (!mods.All.TrueForAll(mod => mod.On)) wrong.Add("Stock turned mods off");

                // Save collection.
                UseCollection(set);
                string said = mainframe.ConflictsSave();
                string saved = Path.Combine(mods.Folder, "collections", set + Mods.Extension);
                ModFile read = File.Exists(saved) ? Mods.Read(File.ReadAllText(saved)) : null;
                if (read == null || read.Overrides.Length != FileSlots.HeldParts().Count || said.IndexOf(set, StringComparison.Ordinal) < 0) wrong.Add("Save collection didn't write one pack holding each part once: " + said);
                if (mods.All.Exists(mod => mod.File.Id.StartsWith("collection-", StringComparison.Ordinal))) wrong.Add("the saved collection was laid as a mod");
            }
            finally
            {
                mainframe.CloseConflicts();
                mainframe.ConflictPutAway();
                foreach (Mod left in mods.All.ToArray()) mods.Remove(left);
                try { string folder = Path.Combine(mods.Folder, "collections"); if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch (IOException caughtOne) { Brain.Caught(caughtOne); }
                preferences.ModCollections = collectionsWas;
                preferences.ModPartPicks = picksWas;
                preferences.ModCollectionInUse = useWas;
                preferences.PackPicks = pairWas;
                SavePreferences();
                mods.Reapply();
                mainframe.ModAsk = askWas;
                // Her own code mods it stopped, laid again and running, so no check after it finds them gone.
                ModsBack();
            }
            if (wrong.Count > 0) throw new Exception("Conflicts went wrong: " + string.Join("; ", wrong.ToArray()));
            checks.Add("PASS Conflicts: three packs on a list of lines and a whole file, the later one winning by default; the order and a drag move the winner; a pick holds against the order; Merge joins a list's lines once each and isn't used on a whole file; Edit in MD editor starts from the part as laid and uses their file; Copy keeps order and picks, Use, a rename, Stock fixed; removing the one in use goes to Stock, her core with no pack laid and every mod still on; Save collection writes one pack holding each part once, never laid as a mod; the window lists it all (conflicts-window.png); Mod maker and Conflicts are one window, its title bar's pair switching the part in the same place");
        }
#endif
        // Her mods folder by type: a flat folder sorted once, imports and drops placed, load after placing a new mod.
#if CHECKS
        private void SmokeModFolders(List<string> checks, string output)
        {
            List<string> wrong = new List<string>();
            string mods = Path.Combine(output, "mods-folders", "mods");
            if (Directory.Exists(Path.GetDirectoryName(mods))) Directory.Delete(Path.GetDirectoryName(mods), true);
            Directory.CreateDirectory(mods);
            Action<string, string> put = delegate(string name, string text) { File.WriteAllText(Path.Combine(mods, name), text, new UTF8Encoding(false)); };
            string head = "// capri-mod: 1\n// name: {0}\n// id: {0}\n// version: 1\n// author: Smoke\n// description: A test.\n";
            put("tint.capri-mod", "{\"format\":1,\"id\":\"tint\",\"name\":\"Tint\",\"colors\":{\"hair\":\"#ff0000\"}}");
            put("nick.capri-mod", "{\"format\":2,\"id\":\"nick\",\"name\":\"Nick\",\"names\":{\"name\":\"Nova\",\"wake\":[\"nova\"]}}");
            put("words.capri-mod", "{\"format\":2,\"id\":\"words\",\"name\":\"Words\",\"overrides\":[{\"slot\":\"lines.md > openers warm\",\"text\":\"Hi.\"}]}");
            put("door-test.capri-code", string.Format(head, "door-test") + "using CapriDesktop;\npublic class M : ICapriMod, ICapriIntro { }\n");
            put("game-app.capri-code", string.Format(head, "game-app") + "// kind: app\nusing System;\n");
            put("filters.capri-code", string.Format(head, "filters") + "// plugs into: email\nusing System;\n");
            put("plain.capri-code", string.Format(head, "plain") + "using System;\n");
            put("know.capri-skill", "---\ncapri-skill: 1\nid: know\nname: Know\nversion: 1\nauthor: Smoke\ndescription: A test.\n---\nSome know-how.\n");
            Directory.CreateDirectory(Path.Combine(mods, "door-test-data"));
            File.WriteAllText(Path.Combine(mods, "door-test-data", "shown"), "");
            Directory.CreateDirectory(Path.Combine(mods, "capri-tear-data"));
            Directory.CreateDirectory(Path.Combine(mods, "plugins", "notes"));
            File.WriteAllText(Path.Combine(mods, "plugins", "notes.capri-plugin"), "x");
            File.WriteAllText(Path.Combine(mods, "approved.txt"), "plain=1");
            Directory.CreateDirectory(Path.Combine(mods, Mods.Other));
            File.WriteAllText(Path.Combine(mods, Mods.Other, "plain.capri-code"), "taken");
            List<string> problems = new List<string>();
            List<string> moved = Mods.SettleIn(mods, problems);
            string[,] want =
            {
                { Mods.SkinsCapri, "tint.capri-mod" }, { Mods.Personas, "nick.capri-mod" }, { Mods.Lines, "words.capri-mod" }, { Mods.Intros, "door-test.capri-code" },
                { Mods.Intros, "door-test-data" }, { Mods.PluginsApps + @"\game-app", "game-app.capri-code" }, { Mods.PluginsApps + @"\email", "filters.capri-code" },
                { Mods.Skills, "know.capri-skill" }, { Mods.Pranks, "capri-tear-data" }, { Mods.PluginsApps, "notes.capri-plugin" }, { Mods.PluginsApps, "notes" }
            };
            for (int i = 0; i < want.GetLength(0); i++)
            {
                string at = Path.Combine(mods, want[i, 0], want[i, 1]);
                if (!File.Exists(at) && !Directory.Exists(at)) wrong.Add(want[i, 1] + " isn't in " + want[i, 0]);
            }
            if (!File.Exists(Path.Combine(mods, "plain.capri-code")) || File.ReadAllText(Path.Combine(mods, Mods.Other, "plain.capri-code")) != "taken" || !problems.Exists(one => one.StartsWith("plain.capri-code stays loose", StringComparison.Ordinal)))
                wrong.Add("a name already taken wasn't left loose and said: " + string.Join("; ", problems.ToArray()));
            if (!File.Exists(Path.Combine(mods, "approved.txt")) || Directory.Exists(Path.Combine(mods, "plugins"))) wrong.Add("her record files moved, or plugins stayed");
            string list = File.Exists(Path.Combine(mods, "moved.txt")) ? File.ReadAllText(Path.Combine(mods, "moved.txt")) : "";
            if (list.IndexOf("tint.capri-mod > " + Mods.SkinsCapri, StringComparison.Ordinal) < 0 || list.IndexOf("plugins > " + Mods.PluginsApps, StringComparison.Ordinal) < 0) wrong.Add("moved.txt doesn't list the moves: " + list.Replace(Environment.NewLine, " | "));
            File.Delete(Path.Combine(mods, "plain.capri-code"));
            if (Mods.SettleIn(mods, new List<string>()).Count != 0) wrong.Add("a second start moved something again");
            if (Mods.HomeIn(mods, "door-test") != Path.Combine(mods, Mods.Intros) || Mods.HomeIn(mods, "capri-tamagotchi") != Path.Combine(mods, Mods.PluginsApps, "tamagotchi") || Mods.HomeIn(mods, "nothing-here") != Path.Combine(mods, Mods.Other))
                wrong.Add("a mod's home: " + Mods.HomeIn(mods, "door-test") + ", " + Mods.HomeIn(mods, "capri-tamagotchi"));

            // Live: an import lands in its folder, a dropped copy goes; load after places a new mod; a drag still wins.
            Mods live = ModList;
            Func<ModFile, string, bool> askWas = mainframe.ModAsk;
            mainframe.ModAsk = delegate { return true; };
            List<string> collectionsWas = preferences.ModCollections == null ? null : new List<string>(preferences.ModCollections);
            string useWas = preferences.ModCollectionInUse;
            try
            {
                foreach (Mod old in live.All.ToArray()) live.Remove(old);
                preferences.ModCollections = new List<string> { "Sorted\t" };
                preferences.ModCollectionInUse = "Sorted";
                string dropped = Path.Combine(live.Folder, "first.capri-mod");
                Directory.CreateDirectory(live.Folder);
                File.WriteAllText(dropped, "{\"format\":2,\"id\":\"first\",\"name\":\"First\",\"overrides\":[{\"slot\":\"lines.md > tails warm\",\"text\":\"One.\"}]}");
                if (!mainframe.ImportMod(dropped)) wrong.Add("a dropped pack was refused: " + mainframe.ModStatusText);
                if (!File.Exists(dropped) || File.Exists(Path.Combine(live.Folder, Mods.Lines, "first.capri-mod"))) wrong.Add("a dropped pack didn't stay where it was dropped while she runs");
                string second = Path.Combine(output, "second.capri-mod"), third = Path.Combine(output, "third.capri-mod");
                File.WriteAllText(second, "{\"format\":2,\"id\":\"second\",\"name\":\"Second\",\"overrides\":[{\"slot\":\"lines.md > tails warm\",\"text\":\"Two.\"}]}");
                File.WriteAllText(third, "{\"format\":2,\"id\":\"third\",\"name\":\"Third\",\"load_before\":[\"second\"],\"overrides\":[{\"slot\":\"lines.md > tails warm\",\"text\":\"Three.\"}]}");
                mainframe.ImportMod(second);
                mainframe.ConflictPutAway();
                mainframe.ImportMod(third);
                mainframe.ConflictPutAway();
                string order = string.Join(",", CollectionOrder("Sorted").ToArray());
                if (order != "first,third,second") wrong.Add("load before didn't place the new mod: " + order);
                SetCollectionOrder("Sorted", new List<string> { "third", "first", "second" });
                live.Reapply();
                if (string.Join(",", CollectionOrder("Sorted").ToArray()) != "third,first,second") wrong.Add("the order they set was moved by load before");
                Mod secondMod = live.All.Find(mod => mod.File.Id == "second");
                if (secondMod == null || !File.Exists(Path.Combine(live.Folder, Mods.Lines, "second.capri-mod")) || live.DataOf(secondMod) != Path.Combine(live.Folder, Mods.Lines, "second-data")) wrong.Add("an import didn't go to its folder, or its data isn't beside it");
            }
            finally
            {
                mainframe.ConflictPutAway();
                foreach (Mod left in live.All.ToArray()) live.Remove(left);
                preferences.ModCollections = collectionsWas;
                preferences.ModCollectionInUse = useWas;
                SavePreferences();
                live.Reapply();
                mainframe.ModAsk = askWas;
                // Her own code mods it stopped, laid again and running, so no check after it finds them gone.
                ModsBack();
            }
            if (wrong.Count > 0) throw new Exception("Her mods folder went wrong: " + string.Join("; ", wrong.ToArray()));
            checks.Add("PASS her mods folder by type: a flat folder sorted once (colours and skins to skinsCapri, a name to personas, words to lines, an intro with its data to intros, an app and a plug-in's mod to their pluginsApps folders, a skill to skills, a prank's data to pranks, plugins to pluginsApps), moved.txt lists it, a taken name stays loose and is said, record files stay, nothing moves twice; a pack dropped while she runs stays where it was dropped until her next start, an import goes to its folder with its data beside it; load before places a new mod and their own order then stays");
        }
#endif
            // Only when named: a copy of someone's mods folder (CAPRI_MODMOVE names its data folder) sorted as her start would,
        // with every move, anything left loose, each mod as it loads, and the files and bytes before and after.
#if CHECKS
        private void ReportModsHere(List<string> checks, string output)
        {
            string data = Environment.GetEnvironmentVariable("CAPRI_MODMOVE");
            if (string.IsNullOrEmpty(data) || !Directory.Exists(Path.Combine(data, "mods"))) { checks.Add("SKIP the mods folder report: CAPRI_MODMOVE names no data folder with mods in it"); return; }
            string mods = Path.Combine(data, "mods");
            Func<long[]> count = delegate
            {
                long files = 0, bytes = 0;
                foreach (string file in Directory.GetFiles(mods, "*", SearchOption.AllDirectories)) { files++; bytes += new FileInfo(file).Length; }
                return new[] { files, bytes };
            };
            long[] before = count();
            HashSet<string> offBefore = new HashSet<string>(File.Exists(Path.Combine(mods, "off.txt")) ? File.ReadAllLines(Path.Combine(mods, "off.txt")) : new string[0]);
            string approvedBefore = File.Exists(Path.Combine(mods, "approved.txt")) ? File.ReadAllText(Path.Combine(mods, "approved.txt")) : "";
            Mods sorted = new Mods(data);
            long[] after = count();
            StringBuilder said = new StringBuilder();
            said.AppendLine("Before: " + before[0] + " files, " + before[1] + " bytes. After: " + after[0] + " files (moved.txt added), " + after[1] + " bytes.");
            said.AppendLine();
            said.AppendLine("Moves (moved.txt):");
            string moved = Path.Combine(mods, "moved.txt");
            said.AppendLine(File.Exists(moved) ? File.ReadAllText(moved).TrimEnd() : "none");
            said.AppendLine();
            said.AppendLine("Left loose or a problem: " + (sorted.Problems.Count == 0 ? "none" : string.Join("; ", sorted.Problems.ToArray())));
            said.AppendLine();
            said.AppendLine("Mods as she reads them now (id | where | on | changed since approved):");
            foreach (Mod mod in sorted.All)
                said.AppendLine(mod.File.Id + " | " + (string.IsNullOrEmpty(mod.Path) ? "-" : Path.GetDirectoryName(mod.Path).Substring(mods.Length).TrimStart('\\')) + " | " + (mod.On ? "on" : "off") + " | " + (mod.Changed ? "CHANGED" : "approved as before"));
            List<Mod> plugins = sorted.All.FindAll(mod => mod.Plugin != null);
            said.AppendLine();
            said.AppendLine("On and off, by id, unchanged: " + (offBefore.SetEquals(new HashSet<string>(File.Exists(Path.Combine(mods, "off.txt")) ? File.ReadAllLines(Path.Combine(mods, "off.txt")) : new string[0]))));
            said.AppendLine("approved.txt unchanged: " + (approvedBefore == (File.Exists(Path.Combine(mods, "approved.txt")) ? File.ReadAllText(Path.Combine(mods, "approved.txt")) : "")));
            said.AppendLine("Plug-ins read: " + plugins.Count);
            File.WriteAllText(Path.Combine(output, "modmove-report.txt"), said.ToString());
            checks.Add("PASS the mods folder report written to modmove-report.txt: " + sorted.All.Count + " mods read, " + (File.Exists(moved) ? File.ReadAllLines(moved).Length : 0) + " moves, files " + before[0] + " to " + after[0] + ", bytes " + before[1] + " to " + after[1]);
        }
#endif
    }
}
