using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // Every part of her files a pack can take over: a file and one of its headings, or a whole file. Read from what she
    // ships, so the list is always what she has; slots.md in the mods folder shows modders.
    internal static class FileSlots
    {
        // Named once her files and mods folder are known: each file's stock text, the list's place and who holds what.
        internal static void Setup(Mods mods)
        {
            Ships(Brain.PersonalityName, delegate { return Brain.DefaultPersonality; });
            Ships(Sayings.FileName, delegate { return Sayings.Text; });
            Ships(Moments.FileName, delegate { return Moments.Text; });
            Ships(Touch.FileName, delegate { return Touch.Text; });
            Ships(Angles.FileName, delegate { return Angles.Text; });
            Ships(CareLines.FileName, delegate { return CareLines.Defaults; });
            Ships(SysLines.FileName, delegate { return SysLines.Text; });
            Ships(Dynamics.FileName, delegate { return Dynamics.Settings; });
            Ships(Lore.FileName, delegate { return Lore.Text; });
            Ships(ChessTalk.FileName, delegate { return ChessTalk.Text; });
            foreach (KeyValuePair<string, string> pack in Personalities.PlainPacks())
            {
                string text = pack.Value;
                Ships(Personalities.FolderName + "/" + pack.Key + ".md", delegate { return text; });
            }
            foreach (string mood in Dynamics.ShippedNames())
            {
                string named = mood;
                Ships(Dynamics.FolderName + "/" + named + ".md", delegate { return Dynamics.MoodText(named); });
            }
            Listed = mods == null ? null : Path.Combine(mods.Folder, FileName);
            ModGuide.Kept = mods == null ? null : Path.Combine(mods.Folder, ModGuide.FileName);
            HolderOf = delegate(Slot slot) { return Holder(slot.Name) ?? (slot.Heading == null ? null : Zones.Holder(Zones.Name(slot.File, slot.Heading))); };
        }

        internal const string FileName = "slots.md";
        internal const string ActsFile = "acts";
        internal static string Listed;

        internal sealed class Slot
        {
            internal string File, Heading, Kind;
            internal int Rows;
            internal string Name { get { return FileSlots.Name(File, Heading); } }
        }

        private static readonly object gate = new object();
        private static readonly List<KeyValuePair<string, Func<string>>> files = new List<KeyValuePair<string, Func<string>>>();

        // A file she ships and her stock text for it; a second call for the same file replaces the first.
        internal static void Ships(string file, Func<string> stock)
        {
            if (string.IsNullOrEmpty(file) || stock == null) return;
            lock (gate)
            {
                files.RemoveAll(one => string.Equals(one.Key, file, StringComparison.OrdinalIgnoreCase));
                files.Add(new KeyValuePair<string, Func<string>>(file, stock));
            }
        }

        internal static string Name(string file, string heading) { return string.IsNullOrEmpty(heading) ? file : file + " > " + heading; }

        // Whether she ships a file by that name, for checking a pack's slots.
        internal static bool Knows(string file)
        {
            lock (gate) return files.Exists(one => string.Equals(one.Key, (file ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
        }

        // Who holds a slot, or null while it is hers: a pack's override, else a mod's claim on a line pool.
        internal static Func<Slot, string> HolderOf = delegate { return null; };

        // What packs hold: a slot's name to the pack, its text and whether it waits for the switch. Rebuilt whole from the
        // packs that are on; a slot two packs want goes to the later one, logged, until the conflict box asks.
        private sealed class Held { internal string Pack, Text; internal bool Senpai; }
        private static readonly Dictionary<string, Held> held = new Dictionary<string, Held>(StringComparer.OrdinalIgnoreCase);
        private static volatile int generation;

        // Moves whenever what packs hold changes, so a reader that keeps a file in memory reads it again.
        internal static int Generation { get { return generation; } }

        // A slot two packs want: the one listed first and the one after, and which keeps it. Picked once they've chosen;
        // until then the later one keeps it.
        internal sealed class Contest
        {
            internal string Slot, First, Second, FirstId, SecondId, Key;
            internal string Keeper;
            internal bool Picked;
        }

        private static readonly List<Contest> contests = new List<Contest>();

        // Their choices, kept in her settings: a pair of packs as they are now and a slot, to the id of the one kept. A
        // changed pack makes a new pair, so they are asked again.
        internal static Func<string, string> PickOf = delegate { return null; };
        internal static Action<string, string> Keep = delegate { };

        internal static string PairOf(Mod one, Mod two)
        {
            string a = one.File.Id + "@" + Sign(one), b = two.File.Id + "@" + Sign(two);
            return string.CompareOrdinal(a, b) < 0 ? a + "+" + b : b + "+" + a;
        }

        // What a pack takes over, as a short stamp: a changed override makes a new pair, asked about again.
        private static string Sign(Mod pack)
        {
            StringBuilder all = new StringBuilder();
            foreach (ModOverride one in pack.File.Overrides ?? new ModOverride[0]) all.Append(one.Slot).Append(" > ").Append(one.Text).Append(" | ");
            using (System.Security.Cryptography.SHA1 sha = System.Security.Cryptography.SHA1.Create())
            {
                byte[] made = sha.ComputeHash(Encoding.UTF8.GetBytes(all.ToString()));
                return BitConverter.ToString(made, 0, 6).Replace("-", "").ToLowerInvariant();
            }
        }

        internal static void Hold(List<Mod> packs)
        {
            List<string> fought = new List<string>();
            lock (gate)
            {
                held.Clear();
                contests.Clear();
                Dictionary<string, Mod> by = new Dictionary<string, Mod>(StringComparer.OrdinalIgnoreCase);
                foreach (Mod pack in packs)
                {
                    string name = pack.File.Name ?? pack.File.Id;
                    foreach (ModOverride one in pack.File.Overrides ?? new ModOverride[0])
                    {
                        // A gated part isn't held at all while the switch is off, so nothing of it is in her memory.
                        if (!Brain.SenpaiOn && (one.Senpai || one.Slot.IndexOf(LineFile.SenpaiMark, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                        Held theirs = new Held { Pack = name, Text = (one.Text ?? "").Replace("\r\n", "\n"), Senpai = one.Senpai };
                        Held had;
                        Mod first;
                        if (held.TryGetValue(one.Slot, out had) && had.Pack != name && by.TryGetValue(one.Slot, out first))
                        {
                            string key = PairOf(first, pack) + "|" + one.Slot, pick = null;
                            try { pick = PickOf(key); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                            bool keepFirst = pick == first.File.Id;
                            contests.Add(new Contest { Slot = one.Slot, First = had.Pack, Second = name, FirstId = first.File.Id, SecondId = pack.File.Id, Key = key, Keeper = keepFirst ? had.Pack : name, Picked = pick != null });
                            if (pick == null) fought.Add(one.Slot + " from " + had.Pack + " to " + name);
                            if (keepFirst) continue;
                        }
                        held[one.Slot] = theirs;
                        by[one.Slot] = pack;
                    }
                }
                generation++;
            }
            foreach (string one in fought)
                try { Brain.Log("mods", -1, "Two packs take over " + one + "."); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // Every slot two packs want, chosen or not.
        internal static List<Contest> Contests() { lock (gate) return new List<Contest>(contests); }

        // The slots a pack holds now, and those it gave up with who kept each, for its row in Mods.
        internal static int HeldBy(string pack)
        {
            int count = 0;
            lock (gate) foreach (Held one in held.Values) if (one.Pack == pack) count++;
            return count;
        }

        internal static List<Contest> GaveUp(string pack) { lock (gate) return contests.FindAll(one => (one.First == pack || one.Second == pack) && one.Keeper != pack); }

        // The pack holding a slot by its name, or null. A senpai part counts only with the switch on.
        internal static string Holder(string name)
        {
            lock (gate)
            {
                Held had;
                return held.TryGetValue(name ?? "", out had) && Live(had, name) ? had.Pack : null;
            }
        }

        private static bool Live(Held had, string name) { return Brain.SenpaiOn || !had.Senpai && name.IndexOf(LineFile.SenpaiMark, StringComparison.OrdinalIgnoreCase) < 0; }

        // The file a path is, as slots name it: "lines.md", or "personalities/Sassy.md" for a pack in its side folder.
        internal static string FileOf(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string name = Path.GetFileName(path), folder = Path.GetDirectoryName(path);
            for (int up = 0; up < 2 && !string.IsNullOrEmpty(folder); up++, folder = Path.GetDirectoryName(folder))
            {
                string here = Path.GetFileName(folder);
                if (string.Equals(here, Personalities.FolderName, StringComparison.OrdinalIgnoreCase) || string.Equals(here, Dynamics.FolderName, StringComparison.OrdinalIgnoreCase))
                    return here.ToLowerInvariant() + "/" + name;
            }
            return name;
        }

        // A file's text with what packs hold laid over it: the whole file, or each heading's lines swapped for the pack's,
        // a heading the file lacks added at its end. Hers on disk is never touched.
        internal static string Over(string file, string text)
        {
            if (string.IsNullOrEmpty(file)) return text;
            List<KeyValuePair<string, string>> headings = new List<KeyValuePair<string, string>>();
            lock (gate)
            {
                if (held.Count == 0) return text;
                Held whole;
                if (held.TryGetValue(file, out whole) && Live(whole, file)) return whole.Text;
                string prefix = file + " > ";
                foreach (KeyValuePair<string, Held> one in held)
                    if (one.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && Live(one.Value, one.Key)) headings.Add(new KeyValuePair<string, string>(one.Key.Substring(prefix.Length).Trim(), one.Value.Text));
            }
            if (headings.Count == 0) return text;
            List<string> rows = new List<string>((text ?? "").Replace("\r\n", "\n").Split('\n'));
            foreach (KeyValuePair<string, string> one in headings)
            {
                int at = rows.FindIndex(row => row.Trim().StartsWith("## ", StringComparison.Ordinal) && string.Equals(row.Trim().Substring(3).Trim(), one.Key, StringComparison.OrdinalIgnoreCase));
                string[] theirs = ("\n" + one.Value.Trim() + "\n").Split('\n');
                if (at < 0) { rows.Add(""); rows.Add("## " + one.Key); rows.AddRange(theirs); continue; }
                int end = at + 1;
                while (end < rows.Count && !rows[end].TrimStart().StartsWith("#", StringComparison.Ordinal)) end++;
                rows.RemoveRange(at + 1, end - at - 1);
                rows.InsertRange(at + 1, theirs);
            }
            return string.Join("\n", rows.ToArray());
        }

        // Her file as she uses it, with what packs hold laid over it.
        internal static string ReadText(string path) { return Over(FileOf(path), Notes.ReadAll(path)); }

        internal static string[] ReadLines(string path)
        {
            string text = ReadText(path) ?? "";
            if (text.EndsWith("\n", StringComparison.Ordinal)) text = text.Substring(0, text.Length - 1);
            return text.Length == 0 ? new string[0] : text.Replace("\r\n", "\n").Split('\n');
        }

        private static readonly Regex Number = new Regex(@"^[a-z][a-z0-9 '/-]{0,40}:\s*[-+]?\d", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // A file's slots: the whole file first, then each "## heading" with what it holds. Notes, the title, a reset line
        // and comments are not part of any slot.
        internal static List<Slot> Of(string file, string text)
        {
            List<Slot> found = new List<Slot>();
            Slot whole = new Slot { File = file, Kind = "whole file" };
            found.Add(whole);
            Slot under = null;
            List<string> rows = new List<string>();
            bool comment = false;
            Action close = delegate
            {
                if (under == null) return;
                under.Rows = rows.Count;
                int numbers = rows.FindAll(row => Number.IsMatch(row)).Count;
                under.Kind = rows.Count == 0 ? "empty" : numbers * 2 > rows.Count ? "numbers" : rows.Count == 1 ? "text" : "lines";
                // A settings heading is hers to keep: no pack carries a setting.
                if (!string.Equals(under.Heading, "settings", StringComparison.OrdinalIgnoreCase)) found.Add(under);
            };
            foreach (string raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string one = raw.Trim();
                if (comment) { if (one.Contains("-->")) comment = false; continue; }
                if (one.StartsWith("<!--", StringComparison.Ordinal)) { comment = !one.Contains("-->"); continue; }
                if (one.StartsWith("## ", StringComparison.Ordinal)) { close(); under = new Slot { File = file, Heading = one.Substring(3).Trim() }; rows.Clear(); continue; }
                if (one.Length == 0 || one.StartsWith("#", StringComparison.Ordinal) || one.StartsWith(">", StringComparison.Ordinal)) continue;
                if (one.StartsWith(Notes.ResetKey, StringComparison.OrdinalIgnoreCase)) continue;
                whole.Rows++;
                if (under != null) rows.Add(one);
            }
            close();
            return found;
        }

        // Every slot she has now, file by file in the order they were named, then her acts.
        internal static List<Slot> All()
        {
            KeyValuePair<string, Func<string>>[] named;
            lock (gate) named = files.ToArray();
            List<Slot> all = new List<Slot>();
            foreach (KeyValuePair<string, Func<string>> one in named)
            {
                string text = null;
                try { text = one.Value(); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                if (text != null) all.AddRange(Of(one.Key, text));
            }
            all.Add(new Slot { File = ActsFile, Kind = "whole list", Rows = CapriDesktop.Acts.Stock.Length });
            foreach (CapriDesktop.Acts.Act act in CapriDesktop.Acts.Stock) all.Add(new Slot { File = ActsFile, Heading = act.Name, Kind = act.Kind + " act", Rows = 1 });
            return all;
        }

        // The files she ships, as slots.md names them, without her acts.
        internal static List<string> Files() { lock (gate) return files.ConvertAll(one => one.Key); }

        // A slot's stock text: the whole file, or the rows under its heading without its notes. Null for an act or a
        // slot she doesn't have.
        internal static string StockOf(string name)
        {
            Slot slot = Find(name);
            if (slot == null || slot.File == ActsFile) return null;
            Func<string> stock;
            lock (gate)
            {
                int at = files.FindIndex(one => string.Equals(one.Key, slot.File, StringComparison.OrdinalIgnoreCase));
                if (at < 0) return null;
                stock = files[at].Value;
            }
            string text = null;
            try { text = stock(); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            if (text == null) return null;
            text = text.Replace("\r\n", "\n");
            if (slot.Heading == null) return text;
            List<string> rows = new List<string>();
            bool under = false;
            foreach (string raw in text.Split('\n'))
            {
                string one = raw.Trim();
                if (one.StartsWith("## ", StringComparison.Ordinal)) { if (under) break; under = string.Equals(one.Substring(3).Trim(), slot.Heading, StringComparison.OrdinalIgnoreCase); continue; }
                if (under && !one.StartsWith(Notes.NoteMark, StringComparison.Ordinal)) rows.Add(raw);
            }
            return string.Join("\n", rows.ToArray()).Trim('\n');
        }

        internal static Slot Find(string name)
        {
            foreach (Slot one in All()) if (string.Equals(one.Name, (name ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) return one;
            return null;
        }

        // The list for modders, written at her start and whenever her mods change. Hers to read, not to edit.
        internal static void Write()
        {
            if (string.IsNullOrEmpty(Listed)) return;
            try
            {
                StringBuilder made = new StringBuilder();
                made.AppendLine("# What a pack can take over");
                made.AppendLine();
                made.AppendLine(">! Every slot she has: a file and one of its headings, or the whole file. A pack takes one over");
                made.AppendLine(">! by its name, like \"lines.md > openers warm\", and hers comes back when the pack is off.");
                made.AppendLine(">! Each heading says what it holds: lines she says, numbers she reads, or text. Her safety");
                made.AppendLine(">! rules and SECURITY are not slots. She writes this file; edits to it are lost.");
                string file = null;
                foreach (Slot one in All())
                {
                    if (one.File != file)
                    {
                        file = one.File;
                        made.AppendLine();
                        made.AppendLine("## " + file);
                        made.AppendLine();
                    }
                    string by = null;
                    try { by = HolderOf(one); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                    string what = one.Heading == null ? one.Kind : one.Heading + " (" + (one.Kind.EndsWith(" act", StringComparison.Ordinal) ? one.Kind
                        : one.Kind + ", " + one.Rows + (one.Rows == 1 ? " row" : " rows")) + ")";
                    made.AppendLine("- " + what + (by == null ? "" : ", held by " + by));
                }
                string folder = Path.GetDirectoryName(Listed);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) Directory.CreateDirectory(folder);
                File.WriteAllText(Listed, made.ToString(), new UTF8Encoding(false));
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }
    }

#if !CROSS
    internal sealed partial class Companion
    {
        // A format 2 pack: overrides laid over headings and personality.md whole with her files untouched, an act and a
        // mood term added, wake names joined, all gone when off; gated parts wait; wrong slots refused.
        private void SmokePacks(List<string> checks, string output)
        {
            Mods mods = ModList;
            Func<ModFile, string, bool> askWas = mainframe.ModAsk;
            mainframe.ModAsk = delegate { return true; };
            bool switchWas = Brain.SenpaiOn;
            Func<string, string, string> write = delegate(string id, string body)
            {
                string file = System.IO.Path.Combine(output, id + Mods.Extension);
                System.IO.File.WriteAllText(file, "{\"format\":2,\"id\":\"" + id + "\",\"name\":\"" + id + "\",\"version\":\"1\",\"author\":\"Smoke\",\"description\":\"A test.\"," + body + "}");
                return file;
            };
            string sassy = Personalities.PlainPathOf("Sassy");
            try
            {
                Brain.SenpaiOn = false;
                foreach (Mod old in mods.All.ToArray()) mods.Remove(old);
                string[] openersHers = Sayings.Pool("openers warm");
                string waterHers = careLines.Pick("Water"), personaHers = Brain.Personality;
                string sassyDisk = sassy != null && System.IO.File.Exists(sassy) ? System.IO.File.ReadAllText(sassy) : null;
                int pettedHers = Personalities.Likes("petted");
                string body = "\"overrides\":["
                    + "{\"slot\":\"lines.md > openers warm\",\"text\":\"Packed opener.\"},"
                    + "{\"slot\":\"check-ins.md > Water\",\"text\":\"- Pack water line.\"},"
                    + "{\"slot\":\"personalities/Sassy.md > Her character\",\"text\":\"A pack's Sassy.\"},"
                    + "{\"slot\":\"personality.md\",\"whole\":true,\"text\":\"You are a pack's Capri.\"},"
                    + "{\"slot\":\"lines.md > tails praise\",\"senpai\":true,\"text\":\"a gated tail\"}],"
                    + "\"adds\":{\"acts\":[{\"name\":\"coffee\",\"kind\":\"food\",\"like\":2}],\"mood\":[{\"act\":\"petted\",\"add\":1}]},"
                    + "\"names\":{\"name\":\"Owl\",\"wake\":[\"hey owl\"]}";
                if (!mainframe.ImportMod(write("night-owl", body))) throw new Exception("A format 2 pack was refused: " + mainframe.ModStatusText);
                string[] openers = Sayings.Pool("openers warm");
                string water = careLines.Pick("Water"), persona = Brain.Personality;
                string sassyRead = sassy != null && System.IO.File.Exists(sassy) ? FileSlots.ReadText(sassy) : null;
                bool laid = openers != null && openers.Length == 1 && openers[0] == "Packed opener." && water == "Pack water line." && persona == "You are a pack's Capri."
                    && (sassyRead == null || sassyRead.IndexOf("A pack's Sassy.", StringComparison.Ordinal) >= 0)
                    && (sassyDisk == null || System.IO.File.ReadAllText(sassy) == sassyDisk);
                Acts.Act coffee = Acts.Find("coffee");
                bool added = coffee != null && coffee.Like == 2 && coffee.Kind == "food" && Personalities.Likes("petted") == Math.Min(5, pettedHers + 1);
                bool named = Array.IndexOf(mods.WakeNames, "hey owl") >= 0;
                bool gated = FileSlots.Holder("lines.md > tails praise") == null;
                FileSlots.Slot held = FileSlots.Find("lines.md > openers warm");
                bool listed = held != null && FileSlots.HolderOf(held) == "night-owl";
                if (!laid || !added || !named || !gated || !listed)
                    throw new Exception("A pack's parts didn't take hold: laid " + laid + ", added " + added + ", wake names " + named + ", gated part held " + !gated + ", listed " + listed);
                checks.Add("PASS a format 2 pack lays its overrides over lines.md's openers warm, check-ins.md's Water, Sassy's character and personality.md whole, with her files on disk untouched; adds an act and a mood term; joins its wake names; slots.md names it; a gated part isn't held with the switch off");

                mods.Remove(mods.All.Find(mod => mod.File.Id == "night-owl"));
                string[] back = Sayings.Pool("openers warm");
                bool hers = back != null && openersHers != null && back.Length == openersHers.Length && careLines.Pick("Water") != "Pack water line." && Brain.Personality == personaHers
                    && Acts.Find("coffee") == null && Personalities.Likes("petted") == pettedHers && Array.IndexOf(mods.WakeNames, "hey owl") < 0;
                if (!hers) throw new Exception("Removing the pack didn't give her own back.");
                checks.Add("PASS removing the pack gives back her own lines, check-ins, personality, acts and likes");

                string[,] wrong =
                {
                    { "{\"slot\":\"lines.md > openers warm\",\"whole\":true,\"text\":\"x\"}", "whole on a heading" },
                    { "{\"slot\":\"lines.md\",\"text\":\"x\"}", "a file without whole" },
                    { "{\"slot\":\"nothere.md > x\",\"text\":\"x\"}", "a file she doesn't have" },
                    { "{\"slot\":\"lines.md > funny\",\"text\":\"x\"},{\"slot\":\"lines.md > funny\",\"text\":\"y\"}", "a slot twice" }
                };
                for (int i = 0; i < wrong.GetLength(0); i++)
                    if (mainframe.ImportMod(write("wrong-pack", "\"overrides\":[" + wrong[i, 0] + "]"))) throw new Exception("A pack with " + wrong[i, 1] + " was let in.");
                if (mainframe.ImportMod(write("wrong-pack", "\"adds\":{\"acts\":[{\"name\":\"petted\",\"like\":1}]}"))) throw new Exception("A pack redefining one of her acts was let in.");
                if (mainframe.ImportMod(write("wrong-pack", "\"adds\":{\"mood\":[{\"act\":\"petted\",\"add\":4}]}"))) throw new Exception("A mood term past 3 was let in.");
                checks.Add("PASS a pack is refused for whole on a heading, a file without whole, a file she doesn't have, a slot named twice, redefining one of her acts, or a mood term past 3");
            }
            finally
            {
                foreach (Mod left in mods.All.ToArray()) mods.Remove(left);
                Brain.SenpaiOn = switchWas;
                mainframe.ModAsk = askWas;
            }
        }

        // Her slots: every file she ships by heading, the whole file and her acts, numbers and lines told apart, nothing of
        // the other side with the switch off, and slots.md naming the mod that holds one.
        private void SmokeSlots(List<string> checks, string output)
        {
            List<FileSlots.Slot> all = FileSlots.All();
            Func<string, FileSlots.Slot> find = delegate(string name) { return all.Find(one => string.Equals(one.Name, name, StringComparison.OrdinalIgnoreCase)); };
            FileSlots.Slot persona = find(Brain.PersonalityName), openers = find(Sayings.FileName + " > openers warm"), character = find(Personalities.FolderName + "/Sassy.md > Her character"),
                likes = find(Personalities.FolderName + "/Sassy.md > What she likes"), act = find(FileSlots.ActsFile + " > petted"), care = find(CareLines.FileName + " > Water");
            if (persona == null || persona.Kind != "whole file" || openers == null || openers.Kind != "lines" || character == null || likes == null || likes.Kind != "numbers" || act == null || care == null)
                throw new Exception("A slot she has is missing or misread: " + (persona == null) + " " + (openers == null ? "no openers" : openers.Kind) + " " + (character == null) + " " + (likes == null ? "no likes" : likes.Kind) + " " + (act == null) + " " + (care == null));
            int linesHeadings = all.FindAll(one => one.File == Sayings.FileName && one.Heading != null).Count, sections = Regex.Matches(Sayings.Text, @"(?m)^## (?!settings\s*$)").Count;
            if (linesHeadings != sections) throw new Exception("lines.md has " + sections + " headings but " + linesHeadings + " slots.");
            if (all.Exists(one => string.Equals(one.Heading, "settings", StringComparison.OrdinalIgnoreCase))) throw new Exception("A settings heading is listed as a slot.");
            if (!Brain.SenpaiOn && all.Exists(one => one.Name.IndexOf(LineFile.SenpaiMark, StringComparison.OrdinalIgnoreCase) >= 0)) throw new Exception("A slot of the other side is listed with the switch off.");
            string listedWas = FileSlots.Listed;
            try
            {
                FileSlots.Listed = System.IO.Path.Combine(output, FileSlots.FileName);
                Dictionary<string, string[]> held = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                held[Zones.Name(Sayings.FileName, "openers warm")] = new[] { "A test opener." };
                Zones.Claim("Slot test", held);
                FileSlots.Write();
                string text = System.IO.File.ReadAllText(FileSlots.Listed);
                if (text.IndexOf("## " + Sayings.FileName, StringComparison.Ordinal) < 0 || text.IndexOf("- openers warm (lines, ", StringComparison.Ordinal) < 0
                    || text.IndexOf("held by Slot test", StringComparison.Ordinal) < 0 || text.IndexOf("## " + Brain.PersonalityName, StringComparison.Ordinal) < 0 || text.IndexOf("- petted (touch act)", StringComparison.Ordinal) < 0)
                    throw new Exception("slots.md didn't list her slots by file with what each holds and who holds one.");
            }
            finally { Zones.Drop("Slot test"); FileSlots.Listed = listedWas; }
            checks.Add("PASS her slots: " + all.Count + " from every file she ships, each heading and the whole file (personality.md whole, lines.md's " + sections + " pools, each personality's character, likes as numbers, check-ins), and her acts; no settings heading; nothing of the other side with the switch off; slots.md lists them by file with what each holds and the mod holding one");
        }
    }
#endif
}
