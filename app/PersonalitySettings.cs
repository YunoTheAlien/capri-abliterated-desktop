using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CapriDesktop
{
    // Her personalities' and moods' numbers and switches live in stock data per side, not in their .md files: a file keeps
    // only her lines and words, and the side's stock settings are laid under it as it is read, before mods, then their own.
    internal static class PersonalitySettings
    {
        internal static readonly string[] Headings =
        {
            Personalities.LikesHeading, Personalities.WordsHeading, Personalities.DriftHeading, Personalities.ReactsHeading,
            Personalities.TopicsHeading, Personalities.SwitchHeading, Personalities.NeedsHeading
        };

        internal static bool IsSettings(string heading)
        {
            foreach (string one in Headings) if (string.Equals(one, (heading ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // A file's text cut in two: its words (everything but settings), and each settings heading with its rows. Notes under a
        // settings heading go with it.
        internal static string Split(string text, List<KeyValuePair<string, List<string>>> blocks, bool all = false)
        {
            StringBuilder words = new StringBuilder();
            List<string> rows = null;
            foreach (string raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("#", StringComparison.Ordinal))
                {
                    rows = null;
                    if (line.StartsWith("## ", StringComparison.Ordinal) && (all || IsSettings(line.Substring(3))))
                    {
                        rows = new List<string>();
                        if (blocks != null) blocks.Add(new KeyValuePair<string, List<string>>(line.Substring(3).Trim(), rows));
                        continue;
                    }
                }
                if (rows != null) { rows.Add(raw); continue; }
                words.Append(raw).Append('\n');
            }
            string made = words.ToString();
            while (made.Contains("\n\n\n")) made = made.Replace("\n\n\n", "\n\n");
            return made.TrimEnd('\n') + "\n";
        }

        internal static string WordsOf(string text) { return Split(text, null); }

        // A heading's settings, key to value, its notes left out.
        internal static Dictionary<string, string> Values(List<string> rows)
        {
            Dictionary<string, string> found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in rows)
            {
                string line = raw.Trim();
                if (line.StartsWith("- ", StringComparison.Ordinal)) line = line.Substring(2).Trim();
                if (line.Length == 0 || line.StartsWith(">", StringComparison.Ordinal)) continue;
                int mark = line.LastIndexOf(':');
                if (mark <= 0) continue;
                found[line.Substring(0, mark).Trim()] = line.Substring(mark + 1).Trim();
            }
            return found;
        }

        // A file that is settings throughout (dynamic.md): every heading in it is a settings heading.
        internal static Func<string, bool> AllSettings = delegate { return false; };

        // The stock text a file of hers is cut from, on its own side: a personality's pack or a mood's, by the folder it is in.
        internal static Func<string, string> StockOf = delegate(string path) { return null; };
        // The same with what is only counted when carrying: her needs as she laid them before they moved.
        internal static Func<string, string> CarryStockOf = delegate(string path) { return null; };

        // Settings blocks from the side's stock and from mods, laid under a file as it is read; a file that has its own settings
        // (from before they moved) keeps them, since the carry takes them out at her next start.
        internal static Func<string, List<KeyValuePair<string, List<string>>>> ModBlocks = delegate { return null; };

        internal static string With(string path, string text)
        {
            string stock = null;
            try { stock = StockOf(path); } catch (Exception caughtOne) { Brain.Caught(caughtOne); }
            List<KeyValuePair<string, List<string>>> mods = null;
            try { mods = ModBlocks(path); } catch (Exception caughtOne) { Brain.Caught(caughtOne); }
            if (stock == null && (mods == null || mods.Count == 0)) return text;
            bool all = AllSettings(path);
            List<KeyValuePair<string, List<string>>> had = new List<KeyValuePair<string, List<string>>>();
            Split(text, had, all);
            StringBuilder made = new StringBuilder((text ?? "").TrimEnd('\n', '\r')).Append('\n');
            if (stock != null)
            {
                List<KeyValuePair<string, List<string>>> stockBlocks = new List<KeyValuePair<string, List<string>>>();
                Split(stock, stockBlocks, all);
                // In dynamic.md a key can sit under any heading, so the file's own keys are left to it, wherever they are.
                HashSet<string> own = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (all) foreach (string key in Values(new List<string>((text ?? "").Replace("\r\n", "\n").Split('\n'))).Keys) own.Add(key);
                foreach (KeyValuePair<string, List<string>> block in stockBlocks)
                {
                    if (had.Exists(one => string.Equals(one.Key, block.Key, StringComparison.OrdinalIgnoreCase))) continue;
                    List<string> rows = all ? block.Value.FindAll(row => { Dictionary<string, string> one = Values(new List<string> { row }); foreach (string key in one.Keys) if (own.Contains(key)) return false; return true; }) : block.Value;
                    made.Append("\n## ").Append(block.Key).Append('\n');
                    foreach (string row in rows) made.Append(row).Append('\n');
                }
            }
            // A mod's numbers come last, so they are read over the stock ones.
            if (mods != null)
                foreach (KeyValuePair<string, List<string>> block in mods)
                {
                    made.Append("\n## ").Append(block.Key).Append('\n');
                    foreach (string row in block.Value) made.Append(row).Append('\n');
                }
            return made.ToString();
        }

        // Once each start: a file still holding settings has each value that differs from its stock kept as their own (for who,
        // under its heading), its settings taken out, and the file as it was kept once as <name>.md.bak. Returns how many
        // values were carried.
        internal static int Carry(string path, string who, Func<string, string, string> ownValue, Action<string, string, string> keep)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return 0;
            string text = Notes.ReadAll(path);
            bool all = AllSettings(path);
            List<KeyValuePair<string, List<string>>> blocks = new List<KeyValuePair<string, List<string>>>();
            string words = Split(text, blocks, all);
            if (blocks.Count == 0) return 0;
            List<KeyValuePair<string, List<string>>> stockBlocks = new List<KeyValuePair<string, List<string>>>();
            string stock = null;
            try { stock = CarryStockOf(path); } catch (Exception caughtOne) { Brain.Caught(caughtOne); }
            if (stock != null) Split(stock, stockBlocks, all);
            int carried = 0;
            foreach (KeyValuePair<string, List<string>> block in blocks)
            {
                Dictionary<string, string> stockValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, List<string>> theirs in stockBlocks)
                    if (string.Equals(theirs.Key, block.Key, StringComparison.OrdinalIgnoreCase)) foreach (KeyValuePair<string, string> value in Values(theirs.Value)) stockValues[value.Key] = value.Value;
                foreach (KeyValuePair<string, string> one in Values(block.Value))
                {
                    string was;
                    if (stockValues.TryGetValue(one.Key, out was) && string.Equals(was, one.Value, StringComparison.OrdinalIgnoreCase)) continue;
                    if (ownValue(block.Key, one.Key) != null) continue;
                    keep(block.Key, one.Key, one.Value);
                    carried++;
                }
            }
            string bak = path + ".bak";
            if (!File.Exists(bak)) File.Copy(path, bak);
            File.WriteAllText(path, words, new UTF8Encoding(false));
            Brain.Log("personality", -1, who + " (" + Path.GetFileName(path) + "): its settings moved out" + (carried > 0 ? ", " + carried + " of their own kept" : "") + ".");
            return carried;
        }
    }

    // A personality setting a mod brings: for whom (a personality or mood by name), under which heading, the key and its value.
    [System.Runtime.Serialization.DataContract]
    internal sealed class ModSetting
    {
        [System.Runtime.Serialization.DataMember(Name = "who")] public string Who;
        [System.Runtime.Serialization.DataMember(Name = "section")] public string Section;
        [System.Runtime.Serialization.DataMember(Name = "key")] public string Key;
        [System.Runtime.Serialization.DataMember(Name = "value")] public string Value;
    }

    internal sealed partial class Companion
    {
        // Laid under her files as they are read, before anything reads one: the side's stock settings and the mods' layer.
        private static void HookPersonalitySettings()
        {
            PersonalitySettings.StockOf = delegate(string path) { return Personalities.SettingsStock(path, false); };
            PersonalitySettings.CarryStockOf = delegate(string path) { return Personalities.SettingsStock(path, true); };
            PersonalitySettings.AllSettings = delegate(string path)
            {
                try { return Dynamics.Kept != null && string.Equals(Path.GetFullPath(path), Path.GetFullPath(Dynamics.Kept), StringComparison.OrdinalIgnoreCase); }
                catch (Exception) { return false; }
            };
            FileSlots.Stocked = PersonalitySettings.With;
        }

        // The mods' layer for one of her files: her needs while a pet game is on, and every setting an on mod brings for that
        // personality or mood, grouped by heading.
        private List<KeyValuePair<string, List<string>>> ModSettingsFor(string path) { return ModSettingsFor(path, true); }

        private List<KeyValuePair<string, List<string>>> ModSettingsFor(string path, bool withNeeds)
        {
            List<KeyValuePair<string, List<string>>> blocks = new List<KeyValuePair<string, List<string>>>();
            Mods mods = ModList;
            if (mods == null || string.IsNullOrEmpty(path)) return blocks;
            string who = Path.GetFileNameWithoutExtension(path);
            Func<bool> running = PetStats.Running;
            bool petGame = false;
            try { petGame = running != null && running(); } catch (Exception caughtOne) { Brain.Caught(caughtOne); }
            if (withNeeds && petGame)
            {
                List<string> needs = Personalities.NeedRows(path);
                if (needs.Count > 0) blocks.Add(new KeyValuePair<string, List<string>>(Personalities.NeedsHeading, needs));
            }
            foreach (Mod mod in mods.All)
            {
                if (!mod.On || mod.File.PersonalitySettings == null) continue;
                foreach (ModSetting one in mod.File.PersonalitySettings)
                {
                    if (one == null || !string.Equals(one.Who, who, StringComparison.OrdinalIgnoreCase)) continue;
                    string section = string.IsNullOrWhiteSpace(one.Section) ? Personalities.ReactsHeading : one.Section.Trim();
                    KeyValuePair<string, List<string>> block = blocks.Find(each => string.Equals(each.Key, section, StringComparison.OrdinalIgnoreCase));
                    if (block.Key == null) { block = new KeyValuePair<string, List<string>>(section, new List<string>()); blocks.Add(block); }
                    block.Value.Add(one.Key.Trim() + ": " + (one.Value ?? "").Trim());
                }
            }
            return blocks;
        }

        // The keys a mod sets for a personality or mood under a heading, so their own value for those gives way to the mod's.
        internal HashSet<string> ModKeys(string path, string section)
        {
            HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, List<string>> block in ModSettingsFor(path, false))
                if (string.Equals(block.Key, section, StringComparison.OrdinalIgnoreCase)) foreach (string key in PersonalitySettings.Values(block.Value).Keys) keys.Add(key);
            if (!string.IsNullOrEmpty(path))
            {
                string held = FileSlots.HeldText(FileSlots.FileOf(path) + " > " + section);
                if (held != null) foreach (string key in PersonalitySettings.Values(new List<string>(held.Split('\n'))).Keys) keys.Add(key);
            }
            return keys;
        }

        // Every personality, mood and dynamic.md file of both sides that still holds settings: moved out once, their own values
        // kept in Your Own.
        private void CarryPersonalitySettings()
        {
            PersonalitySettings.ModBlocks = ModSettingsFor;
            Personalities.ModKeys = ModKeys;
            List<KeyValuePair<string, string>> files = new List<KeyValuePair<string, string>>();
            foreach (string folder in new[] { Personalities.Plain, Personalities.SenpaisRead, Dynamics.Plain, Dynamics.SenpaisRead })
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                    foreach (string file in Directory.GetFiles(folder, "*.md")) files.Add(new KeyValuePair<string, string>(file, Path.GetFileNameWithoutExtension(file)));
            if (!string.IsNullOrEmpty(Dynamics.Kept) && File.Exists(Dynamics.Kept)) files.Add(new KeyValuePair<string, string>(Dynamics.Kept, Personalities.Dynamic));
            int carried = 0;
            foreach (KeyValuePair<string, string> one in files)
            {
                string who = one.Value;
                try { carried += PersonalitySettings.Carry(one.Key, who, delegate(string section, string key) { return Personalities.OwnValue(who, section, key); }, delegate(string section, string key, string value) { SetYourOwn(who, section, key, value); }); }
                catch (IOException caughtOne) { Brain.Caught(caughtOne); }
                catch (UnauthorizedAccessException caughtOne) { Brain.Caught(caughtOne); }
            }
            if (carried > 0) Brain.Log("personality", -1, carried + " of their own settings moved from her .md files into Your Own.");
            DropStockPaces();
            DropOldEnergyRests();
        }

        // Once: a Your Own energy falls, needs gain or energy rests still at its pack's old stock was never changed by them, so
        // it goes and the new stock is read; one they changed stays.
        private void DropOldEnergyRests()
        {
            if (preferences.PetFillCarried) return;
            foreach (string row in YourOwnRows)
            {
                string[] parts = row.Split('\t');
                if (parts.Length < 3 || !string.Equals(parts[1], Personalities.NeedsHeading, StringComparison.OrdinalIgnoreCase)) continue;
                int mark = parts[2].IndexOf('=');
                double value;
                if (mark <= 0) continue;
                string key = parts[2].Substring(0, mark).Trim().ToLowerInvariant();
                if (key != "energy rests" && key != "energy falls" && key != "needs gain") continue;
                if (!double.TryParse(parts[2].Substring(mark + 1).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value)) continue;
                string pack = string.IsNullOrEmpty(parts[0]) ? Personalities.Stock : parts[0];
                if (PetStats.WasStock(pack, key, value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)))
                    SetYourOwn(parts[0], parts[1], parts[2].Substring(0, mark), null);
            }
            preferences.PetFillCarried = true;
            SavePreferences();
        }

        // Paces that were carried into Your Own at their stock number, when dynamic.md was given every pace line before each
        // carry: stock reads the same, so those rows go and the window shows them as stock again.
        private void DropStockPaces()
        {
            string section = Dynamics.PaceHeading.Substring(3).Trim();
            foreach (string row in YourOwnRows)
            {
                string[] parts = row.Split('\t');
                if (parts.Length < 3 || !string.Equals(parts[0], Personalities.Dynamic, StringComparison.OrdinalIgnoreCase) || !string.Equals(parts[1], section, StringComparison.OrdinalIgnoreCase)) continue;
                int mark = parts[2].IndexOf('=');
                string key = mark > 0 ? parts[2].Substring(0, mark).Trim() : "";
                if (!key.EndsWith(" pace", StringComparison.OrdinalIgnoreCase)) continue;
                string named = key.Substring(0, key.Length - 5).Trim();
                double value;
                if (!Dynamics.StockPaced(named) && !string.Equals(named, Personalities.Stock, StringComparison.OrdinalIgnoreCase)) continue;
                if (!double.TryParse(parts[2].Substring(mark + 1).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value)) continue;
                if (value == Dynamics.StockPaceOf(named)) SetYourOwn(parts[0], parts[1], parts[2].Substring(0, mark), null);
            }
        }
    }

    internal sealed partial class Companion
    {
        // Settings out of her .md files: read from stock, carried once into Your Own, a dropped mod's number shown and used.
#if CHECKS
        private void SmokePersonalityFile(List<string> checks, string output)
        {
            const string who = "Sassy", key = "flirt";
            string path = Personalities.PlainPathOf(who);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) { checks.Add("LIMIT her personality settings: no " + who + ".md here"); return; }
            List<string> wrong = new List<string>();
            string fileWas = File.ReadAllText(path), bak = path + ".bak";
            bool bakHad = File.Exists(bak);
            string bakWas = bakHad ? File.ReadAllText(bak) : null;
            List<string> ownWas = preferences.YourOwn == null ? null : new List<string>(preferences.YourOwn);
            string chosenWas = Personalities.Chosen, attitudeWas = Personalities.Attitude;
            Mods mods = ModList;
            string dropped = Path.Combine(mods.Folder, "number-test" + Mods.Extension);
            Func<ModFile, string, bool> askWas = mainframe.ModAsk;
            mainframe.ModAsk = delegate { return true; };
            try
            {
                List<KeyValuePair<string, List<string>>> blocks = new List<KeyValuePair<string, List<string>>>();
                PersonalitySettings.Split(File.ReadAllText(path), blocks);
                if (blocks.Count != 0) wrong.Add(who + ".md still holds settings after her start: " + blocks[0].Key);
                string read = FileSlots.ReadText(path);
                if (read.IndexOf("## " + Personalities.ReactsHeading, StringComparison.Ordinal) < 0 || read.IndexOf("## " + Personalities.LikesHeading, StringComparison.Ordinal) < 0)
                    wrong.Add("as read, " + who + " lacks her stock settings");
                List<KeyValuePair<string, List<string>>> stockBlocks = new List<KeyValuePair<string, List<string>>>();
                PersonalitySettings.Split(Personalities.SettingsStock(path, true), stockBlocks);
                Dictionary<string, string> stockReacts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, List<string>> block in stockBlocks)
                    if (block.Key == Personalities.ReactsHeading) foreach (KeyValuePair<string, string> one in PersonalitySettings.Values(block.Value)) stockReacts[one.Key] = one.Value;
                string stockFlirt;
                int flirt;
                if (!stockReacts.TryGetValue(key, out stockFlirt) || !int.TryParse(stockFlirt, out flirt)) { wrong.Add(who + "'s stock has no " + key); flirt = 0; }
                string changed = (flirt == 9 ? 8 : 9).ToString();
                string other = null;
                foreach (string one in stockReacts.Keys) if (one != key) { other = one; break; }

                // The carry: their changed value into Your Own, a stock one not, the file kept as .bak and written without them.
                if (preferences.YourOwn != null) preferences.YourOwn.RemoveAll(row => row.StartsWith(who + "\t", StringComparison.OrdinalIgnoreCase));
                Personalities.OwnLayerVersion++;
                if (File.Exists(bak)) File.Delete(bak);
                File.AppendAllText(path, "\n## " + Personalities.ReactsHeading + "\n" + key + ": " + changed + "\n" + (other == null ? "" : other + ": " + stockReacts[other] + "\n"));
                CarryPersonalitySettings();
                blocks.Clear();
                PersonalitySettings.Split(File.ReadAllText(path), blocks);
                if (Personalities.OwnValue(who, Personalities.ReactsHeading, key) != changed || (other != null && Personalities.OwnValue(who, Personalities.ReactsHeading, other) != null) || blocks.Count != 0 || !File.Exists(bak))
                    wrong.Add("the carry: " + key + " " + Personalities.OwnValue(who, Personalities.ReactsHeading, key) + " (wanted " + changed + "), stock " + other + " kept as theirs " + (other != null && Personalities.OwnValue(who, Personalities.ReactsHeading, other) != null) + ", settings left " + blocks.Count + ", .bak " + File.Exists(bak));
                int rows = (preferences.YourOwn ?? new List<string>()).Count;
                CarryPersonalitySettings();
                if ((preferences.YourOwn ?? new List<string>()).Count != rows) wrong.Add("a second start carried again");
                Personalities.Chosen = who;
                Personalities.Attitude = "";
                if (Personalities.Reacts(key, -99).ToString() != changed) wrong.Add("their own " + key + " isn't what she reads: " + Personalities.Reacts(key, -99));

                // A pack dropped into her mods folder with a number for Sassy: in the window and read over their own; taken out, gone.
                File.WriteAllText(dropped, "{\"format\":2,\"id\":\"number-test\",\"name\":\"Number Test\",\"personality_settings\":[{\"who\":\"" + who + "\",\"key\":\"" + key + "\",\"value\":\"2\"}]}");
                mainframe.DropForCheck(dropped, false);
                mainframe.PicturePersonalitySettings(who, Path.Combine(output, "personality-file-mod.png"));
                string shown = mainframe.PersonalitySettingsValues;
                if (Personalities.Reacts(key, -99) != 2 || shown.IndexOf(key + "=2", StringComparison.OrdinalIgnoreCase) < 0)
                    wrong.Add("a dropped mod's number: read " + Personalities.Reacts(key, -99) + ", shown " + (shown.Split('\n').Length > 0 ? Array.Find(shown.Split('\n'), line => line.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase)) : "none"));
                File.Delete(dropped);
                mainframe.DropForCheck(dropped, true);
                if (Personalities.Reacts(key, -99).ToString() != changed || mods.All.Exists(mod => mod.File.Id == "number-test")) wrong.Add("taking the mod out didn't give their own back: " + Personalities.Reacts(key, -99));
            }
            finally
            {
                mainframe.ModAsk = askWas;
                Mod left = mods.All.Find(mod => mod.File.Id == "number-test");
                if (left != null) mods.Remove(left);
                if (File.Exists(dropped)) File.Delete(dropped);
                File.WriteAllText(path, fileWas);
                if (bakHad) File.WriteAllText(bak, bakWas); else if (File.Exists(bak)) File.Delete(bak);
                preferences.YourOwn = ownWas;
                Personalities.OwnLayerVersion++;
                Personalities.Chosen = chosenWas;
                Personalities.Attitude = attitudeWas;
                SavePreferences();
                mods.Reapply();
                mainframe.ClosePersonalitySettings();
            }
            if (wrong.Count > 0) throw new Exception("Her personality settings went wrong: " + string.Join("; ", wrong.ToArray()));
            checks.Add("PASS her personality settings live in stock data, not her .md files: a file holds none after her start and reads with the side's stock under it; a value someone changed is carried into Your Own once, a stock one not, the file kept as .bak; a pack dropped into her mods folder with a number for a personality shows in the window and is read over their own, and taken out gives theirs back (personality-file-mod.png)");
        }
#endif
    }
}
