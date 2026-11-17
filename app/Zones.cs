using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CapriDesktop
{
    // Named places in what she says that a mod can take over, without a compiler and without touching her
    // source.
    //
    // The alternative was splitting her files into small ones so a mod could replace a file, which trades one
    // large thing nobody can safely edit for hundreds of small ones nobody can find. This is the other way
    // round: her files stay as they are and the places worth replacing are named, the way a numbered slot on
    // her Mainframe is named.
    //
    // A zone is one pool of lines, named for the file it lives in and the heading above it, so every pool she
    // already has is claimable and a pool added later is claimable with nothing added here. Small on purpose:
    // a zone that was all of her would be the problem rather than the fix.
    //
    // Every zone keeps its default. A mod turned off, removed, refused or broken leaves her exactly as she
    // shipped, because the default is never replaced, only looked at second.
    //
    // This is what she says. What she does is not claimable, and that is deliberate: a mod inside her
    // decisions is a different thing with its own approval and permissions, and it is not this.
    internal static class Zones
    {
        internal const string ListFile = "mod-zones.txt";

        private static readonly object gate = new object();
        // Zone name to the lines claiming it, and which mod is holding it, for the log and for the list.
        private static readonly Dictionary<string, string[]> taken = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> holder = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Every zone she has read for, so the list can be written even for pools nothing has claimed.
        private static readonly Dictionary<string, string> offered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        internal static string Listed;

        internal static string Name(string file, string section)
        {
            if (string.IsNullOrEmpty(file) || string.IsNullOrEmpty(section)) return null;
            return Path.GetFileNameWithoutExtension(file) + ": " + section.Trim();
        }

        // Noted as claimable the first time she reads it, so the list is what she actually has rather than a
        // list somebody has to remember to add to.
        private static bool more;

        internal static void Offers(string zone)
        {
            if (string.IsNullOrEmpty(zone)) return;
            lock (gate) if (!offered.ContainsKey(zone)) { offered[zone] = ""; more = true; }
        }

        // The list, but only when there is something new in it. She finds her pools as she reads them, so
        // the list fills in over the first few minutes rather than all at once, and rewriting it on every
        // read would be a file write per line she says.
        internal static void Fresh()
        {
            // The switch decides which headings a file even has, so when it moves the files are asked again.
            bool senpai = Brain.Adult;
            bool again;
            lock (gate) { again = !everAsked || senpai != wasSenpai; wasSenpai = senpai; everAsked = true; }
            if (again) Ask();
            lock (gate) { if (!more) return; more = false; }
            Write();
        }

        // Every heading in a file, offered at once. Her pools are found as she reads them otherwise, which
        // means a modder opening the list on a fresh install sees almost nothing in it.
        internal static void Offer(string file, string[] sections)
        {
            if (string.IsNullOrEmpty(file) || sections == null) return;
            foreach (string one in sections) Offers(Name(file, one));
        }

        // A file to ask again later. Which headings a file has depends on the adult switch, and that is read
        // out of sys.md after this is first asked, so the answer at startup is the censored one even on a
        // machine where the switch is on.
        private static readonly List<KeyValuePair<string, Func<string[]>>> pools = new List<KeyValuePair<string, Func<string[]>>>();
        private static bool wasSenpai;
        private static bool everAsked;

        internal static void Offer(string file, Func<string[]> sections)
        {
            if (string.IsNullOrEmpty(file) || sections == null) return;
            lock (gate) pools.Add(new KeyValuePair<string, Func<string[]>>(file, sections));
            Ask();
        }

        private static void Ask()
        {
            KeyValuePair<string, Func<string[]>>[] asking;
            // Emptied first. The list only ever grew, so a zone offered while the switch was on stayed in
            // it after the switch went off and the file went on naming a section she no longer has. A zone
            // a mod is holding keeps its claim: this is the offer list, not the claims.
            lock (gate)
            {
                asking = pools.ToArray();
                List<string> was = new List<string>(offered.Keys);
                offered.Clear();
                foreach (string one in was) if (taken.ContainsKey(one)) offered[one] = "";
                more = true;
            }
            foreach (KeyValuePair<string, Func<string[]>> one in asking)
                try { Offer(one.Key, one.Value()); } catch (Exception) { }
        }

        // The lines a mod has put in this zone, or null for her own.
        internal static string[] Claimed(string zone)
        {
            if (string.IsNullOrEmpty(zone)) return null;
            lock (gate)
            {
                string[] found;
                return taken.TryGetValue(zone, out found) && found != null && found.Length > 0 ? found : null;
            }
        }

        // A mod taking zones as it is turned on. A zone two mods want goes to the one turned on last, logged.
        // Turning one off gives her lines back through Mods.Apply rebuilding the table, not through the Drop
        // below: that is here so claiming replaces what this mod held even from a caller that does not.
        internal static void Claim(string mod, Dictionary<string, string[]> zones)
        {
            if (string.IsNullOrEmpty(mod)) return;
            Drop(mod);
            if (zones == null || zones.Count == 0) return;
            List<string> fought = new List<string>();
            lock (gate)
                foreach (KeyValuePair<string, string[]> one in zones)
                {
                    if (string.IsNullOrEmpty(one.Key) || one.Value == null || one.Value.Length == 0) continue;
                    // A senpai pool is not reachable while the switch is off, so a mod claiming one is not
                    // registered at all rather than held and ignored: nothing of it is in memory, and
                    // nothing of it reaches the list she writes.
                    if (!Brain.Uncensored && one.Key.IndexOf(LineFile.SenpaiMark, StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    string had;
                    if (holder.TryGetValue(one.Key, out had) && had != mod) fought.Add(one.Key + ", also wanted by " + had);
                    taken[one.Key] = one.Value;
                    holder[one.Key] = mod;
                }
            foreach (string one in fought)
                try { Brain.Log("mods", -1, mod + " took the zone " + one + "."); }
                catch (Exception) { }
        }

        internal static void Drop(string mod)
        {
            if (string.IsNullOrEmpty(mod)) return;
            lock (gate)
            {
                List<string> going = new List<string>();
                foreach (KeyValuePair<string, string> one in holder) if (one.Value == mod) going.Add(one.Key);
                foreach (string one in going) { taken.Remove(one); holder.Remove(one); }
            }
        }

        internal static void DropAll()
        {
            lock (gate) { taken.Clear(); holder.Clear(); }
        }

        // What a modder can claim, written beside her files so nobody has to read her source to find out.
        // Rewritten whenever a mod changes, since a zone she has not read for yet is not in it.
        internal static void Write()
        {
            if (string.IsNullOrEmpty(Listed)) return;
            try
            {
                List<string> names = new List<string>();
                Dictionary<string, string> held = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                lock (gate)
                {
                    foreach (KeyValuePair<string, string> one in offered) names.Add(one.Key);
                    foreach (KeyValuePair<string, string> one in holder) { held[one.Key] = one.Value; if (!names.Contains(one.Key)) names.Add(one.Key); }
                }
                names.Sort(StringComparer.OrdinalIgnoreCase);
                StringBuilder made = new StringBuilder();
                made.AppendLine("# What a mod can take over");
                made.AppendLine();
                made.AppendLine("> Every pool of lines she has, by file and heading. A mod claims one by name in its own");
                made.AppendLine("> .capri-mod file and she says its lines there instead of her own. No code, no compiler.");
                made.AppendLine(">");
                made.AppendLine("> \"zones\": { \"moments: When she pops in\": [\"a line\", \"another line\"] }");
                made.AppendLine(">");
                made.AppendLine("> Her own lines are never replaced, only looked at second, so a mod turned off, removed or");
                made.AppendLine("> refused leaves her exactly as she shipped. This is what she says; what she does is not");
                made.AppendLine("> claimable. Hers to read, not to edit: she writes this file.");
                made.AppendLine();
                if (names.Count == 0) made.AppendLine("Nothing read yet. Open her for a while and this fills in.");
                foreach (string one in names)
                {
                    string by;
                    made.AppendLine(held.TryGetValue(one, out by) ? one + "   <- taken by " + by : one);
                }
                string folder = Path.GetDirectoryName(Listed);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) Directory.CreateDirectory(folder);
                File.WriteAllText(Listed, made.ToString(), new UTF8Encoding(false));
            }
            catch (Exception) { }
        }
    }

#if !CROSS
    internal sealed partial class Companion
    {
        // The zone system had no check of its own: a settings mod claiming a pool, her own lines coming back
        // when it lets go, a zone two mods want, and the list she writes for modders.
        internal void CheckModZones(List<string> checks, string output)
        {
            Mods mods = ModList;
            Func<ModFile, string, bool> askWas = mainframe.ModAsk;
            mainframe.ModAsk = delegate { return true; };
            string zone = Zones.Name(Moments.Kept, Moments.PopIn);
            string senpai = Zones.Name(Touch.Kept, LineFile.Senpai(Touch.Chest));
            string[] first = { "Only the first mod says this.", "And this one as well." };
            string[] second = { "Only the second mod says this." };
            Func<string, string[], string, string> write = delegate(string id, string[] lines, string named)
            {
                StringBuilder made = new StringBuilder();
                made.Append("{\"format\":1,\"id\":\"").Append(id).Append("\",\"name\":\"").Append(id);
                made.Append("\",\"version\":\"1\",\"author\":\"Smoke\",\"description\":\"A test.\",\"zones\":{\"");
                made.Append(named).Append("\":[");
                for (int at = 0; at < lines.Length; at++) made.Append(at > 0 ? "," : "").Append(Voice.Quote(lines[at]));
                made.Append("]}}");
                string file = Path.Combine(output, id + Mods.Extension);
                File.WriteAllText(file, made.ToString());
                return file;
            };
            Func<string[], bool> says = delegate(string[] pool)
            {
                for (int i = 0; i < 60; i++) if (Array.IndexOf(pool, Moments.Pick(Moments.PopIn, null)) >= 0) return true;
                return false;
            };
            try
            {
                foreach (Mod old in mods.All.ToArray()) mods.Remove(old);
                if (string.IsNullOrEmpty(zone)) throw new Exception("Her moments pool has no zone name, so nothing could claim it.");
                if (Zones.Claimed(zone) != null) throw new Exception("A zone was taken before any mod claimed it.");
                string[] hers = Moments.Lines(Moments.PopIn);
                if (hers == null || hers.Length == 0) throw new Exception("Her own pool was empty before the test began.");

                if (!mainframe.ImportMod(write("zoneone", first, zone))) throw new Exception("A zone mod was refused: " + mainframe.ModStatusText);
                string[] took = Zones.Claimed(zone);
                if (took == null || took.Length != first.Length) throw new Exception("A zone mod did not take the zone it named.");
                if (!says(first)) throw new Exception("She kept saying her own lines while a mod held the pool.");
                if (Moments.Lines(Moments.PopIn).Length != first.Length) throw new Exception("A claimed pool did not read as the mod's.");
                checks.Add("PASS mod zones: a settings mod takes a pool of her lines by name and she says its lines there instead of her own");

                if (!mainframe.SwitchModForTest(0) || mods.All[0].On) throw new Exception("A zone mod would not turn off.");
                if (Zones.Claimed(zone) != null || !says(hers)) throw new Exception("Turning a zone mod off did not give her own lines back.");
                if (!mainframe.SwitchModForTest(0) || !mods.All[0].On || Zones.Claimed(zone) == null) throw new Exception("Turning it on again did not take the zone back.");
                mods.Remove(mods.All[0]);
                if (Zones.Claimed(zone) != null || !says(hers)) throw new Exception("Removing a zone mod left its lines behind.");
                checks.Add("PASS mod zones: off, on again and removed each leave her own lines exactly as they shipped, since the default is only ever looked at second");

                if (!mainframe.ImportMod(write("zoneone", first, zone))) throw new Exception("A zone mod was refused the second time.");
                string other = Zones.Name(Moments.Kept, Moments.Attention);
                if (string.IsNullOrEmpty(other)) throw new Exception("A second pool of hers has no zone name.");
                if (!mainframe.ImportMod(write("zoneone", first, other))) throw new Exception("A zone mod that moved to another zone was refused: " + mainframe.ModStatusText);
                if (Zones.Claimed(other) == null) throw new Exception("A zone mod did not take the zone it moved to.");
                if (Zones.Claimed(zone) != null || !says(hers)) throw new Exception("A zone mod that stopped claiming a zone kept holding it.");
                foreach (Mod left in mods.All.ToArray()) mods.Remove(left);
                checks.Add("PASS mod zones: a mod that changes which zone it claims lets the old one go, so a zone cannot be left held by a mod no longer asking for it");

                if (!mainframe.ImportMod(write("zoneone", first, zone))) throw new Exception("The first zone mod was refused the third time.");
                if (!mainframe.ImportMod(write("zonetwo", second, zone))) throw new Exception("A second mod wanting the same zone was refused: " + mainframe.ModStatusText);
                string[] held = Zones.Claimed(zone);
                if (held == null || held.Length != second.Length || !says(second)) throw new Exception("A zone two mods wanted did not go to the one turned on last.");
                if (Brain.LogPath != null && File.Exists(Brain.LogPath) && File.ReadAllText(Brain.LogPath).IndexOf("also wanted by", StringComparison.Ordinal) < 0)
                    throw new Exception("A zone taken from another mod left no line in her log.");
                checks.Add("PASS mod zones: a zone two mods want goes to the one turned on last and the handover is logged");

                // Claim on its own, without Apply's DropAll in front of it. Every path in the app goes through
                // Apply, which rebuilds the whole table, so the Drop inside Claim never finds anything and the
                // Sapphire box was right to call its body unreachable. It is kept because it is what makes
                // Claim correct read on its own, and a thing kept for that reason has to be held by a check or
                // it is only an opinion in a comment. This is the caller Apply is not: one mod re-claiming.
                Zones.DropAll();
                Dictionary<string, string[]> firstZones = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                firstZones[zone] = first;
                Zones.Claim("rezone", firstZones);
                if (Zones.Claimed(zone) == null) throw new Exception("A direct claim did not take the zone.");
                Dictionary<string, string[]> noZones = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                Zones.Claim("rezone", noZones);
                if (Zones.Claimed(zone) != null) throw new Exception("A mod re-claiming nothing kept the zone it used to hold, so Claim depends on its caller to be right.");
                Zones.DropAll();
                checks.Add("PASS mod zones: a mod claiming again releases what it held last time without anything dropping it first, so Claim is correct on its own rather than only inside Apply");

                if (!Brain.Uncensored)
                {
                    if (!string.IsNullOrEmpty(senpai) && mainframe.ImportMod(write("zonesenpai", first, senpai)) && Zones.Claimed(senpai) != null)
                        throw new Exception("A mod claimed a senpai pool on a censored install.");
                    checks.Add("PASS mod zones: a senpai pool is not claimable while the switch is off, so nothing of it is held in memory or written to the list");
                }

                Zones.Write();
                if (!string.IsNullOrEmpty(Zones.Listed) && File.Exists(Zones.Listed))
                {
                    string listed = File.ReadAllText(Zones.Listed);
                    if (listed.IndexOf(zone, StringComparison.OrdinalIgnoreCase) < 0) throw new Exception("The zone list left out a pool she had read.");
                    if (listed.IndexOf("taken by", StringComparison.OrdinalIgnoreCase) < 0) throw new Exception("The zone list did not say who held a taken zone.");
                    if (!Brain.Uncensored && listed.IndexOf(LineFile.SenpaiMark, StringComparison.OrdinalIgnoreCase) >= 0) throw new Exception("The zone list named a senpai pool on a censored install.");
                    checks.Add("PASS mod zones: " + Zones.ListFile + " lists every pool she has read, names the mod holding one, and on a censored install has no senpai pool in it");
                }
                else checks.Add("LIMIT mod zones: " + Zones.ListFile + " was not written in this run, so its contents weren't read");
            }
            finally
            {
                mainframe.ModAsk = askWas;
                foreach (Mod left in mods.All.ToArray()) mods.Remove(left);
                Zones.DropAll();
            }
        }

        // A settings line she can say out loud. Her files keep settings in the preamble as "name: value" and
        // her lines under "## heading", and nothing checked that a reader honours the difference: Capri said
        // "reset to stock: 0 is on the table" because one reader treats "#" and "##" alike.
        //
        // Every pool of every line file is read back through her own reader, so this holds for a pool added
        // later and for a file somebody has edited, rather than for the two headings that were reported.
        internal void CheckPoolLines(List<string> checks, string output)
        {
            Func<string, bool> setting = delegate(string line)
            {
                string one = (line ?? "").Trim();
                if (one.StartsWith(CapriDesktop.Notes.ResetKey, StringComparison.OrdinalIgnoreCase)) return true;
                // "name: value" with a short lower-case name, which is how every settings line of hers reads.
                return System.Text.RegularExpressions.Regex.IsMatch(one, @"^[a-z][a-z0-9 '/-]{0,40}:[ \t]*\S");
            };
            string[,] files =
            {
                { Moments.FileName, Moments.Kept }, { Angles.FileName, Angles.Kept },
                { Touch.FileName, Touch.Kept }, { ChessTalk.FileName, ChessTalk.Kept }
            };
            int read = 0, pooled = 0;
            List<string> wrong = new List<string>();
            for (int at = 0; at < files.GetLength(0); at++)
            {
                string named = files[at, 0], path = files[at, 1];
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                read++;
                foreach (KeyValuePair<string, string[]> pool in LineFile.PoolsForTest(File.ReadAllText(path)))
                {
                    if (pool.Value == null) continue;
                    pooled++;
                    foreach (string line in pool.Value)
                        if (setting(line)) wrong.Add(named + " > " + pool.Key + ": " + line);
                }
            }
            if (read == 0) throw new Exception("No line file was on disk, so nothing was checked.");
            if (wrong.Count > 0) throw new Exception("A settings line is in a pool she speaks from: " + string.Join(" | ", wrong.ToArray()));
            checks.Add("PASS pool lines: every pool of her " + read + " line files on disk was read back through her own reader, and none of the "
                + pooled + " holds a settings line, so nothing she says can be a \"" + CapriDesktop.Notes.ResetKey + "\" flag or any other name and value");

            // The rule the readers rely on: settings sit above the first "## heading" and a reader opens a
            // section only on "##". A reader that takes a bare "#" as a heading puts the preamble in its scope,
            // and since headings match ignoring case, a title repeating a section name makes them readable.
            string made = "# What you are into\n\n> A note.\n\n" + CapriDesktop.Notes.ResetKey + ": 0\n\n## what you are into\n\nfeet\n";
            Dictionary<string, string[]> got = LineFile.PoolsForTest(made);
            string[] right;
            if (!got.TryGetValue("what you are into", out right) || right == null || right.Length != 1 || right[0] != "feet")
                throw new Exception("Her reader did not keep the preamble out of a section whose name repeats the title: "
                    + (right == null ? "nothing" : string.Join(" | ", right)));
            foreach (KeyValuePair<string, string[]> pool in got)
                foreach (string line in pool.Value ?? new string[0])
                    if (setting(line)) throw new Exception("A settings line in the preamble was read into the pool " + pool.Key + ": " + line);
            checks.Add("PASS pool lines: a settings line between a title and a section of the same words stays out of every pool, which is the case that made her say one");
        }

        // The spares an update leaves beside the new programs. Nothing exercised them, and they grew without
        // limit: four programs at about 30 MB each, kept for every update ever applied.
        internal void CheckSpares(List<string> checks, string output)
        {
            string folder = Path.Combine(output, "spares");
            Directory.CreateDirectory(folder);
            string program = Path.Combine(folder, "Capri.exe");
            File.WriteAllText(program, "build 1");
            if (CapriSetup.Program.SpareForTest(Path.Combine(folder, "NotHere.exe")) != null) throw new Exception("A program that is not there was spared.");

            List<string> made = new List<string>();
            for (int update = 1; update <= 6; update++)
            {
                string spare = CapriSetup.Program.SpareForTest(program);
                if (spare == null) throw new Exception("Update " + update + " kept no spare of the old program.");
                if (!File.Exists(spare) || File.ReadAllText(spare) != "build " + update) throw new Exception("A spare did not hold the build it replaced.");
                made.Add(Path.GetFileName(spare));
                string[] left = CapriSetup.Program.SparesForTest(program);
                if (left.Length > CapriSetup.Program.MostSpares) throw new Exception("After " + update + " updates there were " + left.Length + " spares, more than the " + CapriSetup.Program.MostSpares + " kept.");
                File.WriteAllText(program, "build " + (update + 1));
                System.Threading.Thread.Sleep(30);
            }
            string[] kept = CapriSetup.Program.SparesForTest(program);
            if (kept.Length != CapriSetup.Program.MostSpares) throw new Exception("Six updates left " + kept.Length + " spares instead of " + CapriSetup.Program.MostSpares + ".");

            List<string> names = new List<string>();
            foreach (string one in kept) names.Add(Path.GetFileName(one));
            for (int back = 0; back < CapriSetup.Program.MostSpares; back++)
                if (!names.Contains(made[made.Count - 1 - back])) throw new Exception("The spares kept are not the newest: " + string.Join(", ", names.ToArray()));
            foreach (string one in kept)
            {
                string said = File.ReadAllText(one);
                if (said == "build 1" || said == "build 2" || said == "build 3") throw new Exception("An old build was kept instead of a recent one: " + said);
            }
            if (File.ReadAllText(program) != "build 7") throw new Exception("Sparing changed the program itself.");
            checks.Add("PASS update spares: every update keeps the build it replaced as a .bak, the newest " + CapriSetup.Program.MostSpares
                + " are kept and older ones go on their own, so six updates leave three recoverable builds rather than six");

            // A spare they put back by hand is theirs: it must not be taken for the oldest and removed.
            string theirs = program + ".bak99";
            File.WriteAllText(theirs, "theirs");
            File.SetLastWriteTimeUtc(theirs, DateTime.UtcNow.AddDays(1));
            File.WriteAllText(program, "build 8");
            CapriSetup.Program.SpareForTest(program);
            if (!File.Exists(theirs) || File.ReadAllText(theirs) != "theirs") throw new Exception("A spare they had put back themselves was removed as the oldest.");
            checks.Add("PASS update spares: which ones go is decided by when each was written, so one put back or copied in by hand outlives the ones she made");
        }
    }
#endif
}
