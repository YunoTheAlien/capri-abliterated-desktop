using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CapriDesktop
{
    // personalSettings\settings.json: every setting of hers in one plain file by file and section, plus her Mainframe
    // settings, to export and import; never a line, key or anything sealed. Written off her window's thread two minutes
    // after the last change and on close; a moving slider writes nothing.
    internal static class SettingsFile
    {
        internal const string FileName = "settings.json";
        internal const string BeforeUpdate = "settings.before-update.json";
        internal const long Quiet = 120000;

        // Her settings folder, personalSettings.
        internal static string Folder;
        // Her Mainframe settings as JSON, with every secret left out, and putting them back: how many were set.
        internal static Func<string> Mainframe;
        internal static Func<string, int> PutMainframe;
        // A setting that has left its file for her settings: whether a name is one, and where its value goes. Put after her
        // Mainframe settings, which an older file carries without it.
        internal static Func<string, bool> Moves = delegate { return false; };
        internal static Action<string, string, string> Move;
        // The clock, in ms since 1970; her checks set their own.
        internal static Func<long> Now = delegate { return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds; };

        private static readonly object gate = new object();
        private static long dirtyAt = -1;
        private static Timer timer;
        private static int saving;

        [DataContract]
        internal sealed class Dump
        {
            [DataMember(Name = "capri settings", Order = 0)] public int Format = 1;
            [DataMember(Name = "version", Order = 1)] public string Version;
            [DataMember(Name = "saved", Order = 2)] public string Saved;
            [DataMember(Name = "files", Order = 3)] public Dictionary<string, Dictionary<string, Dictionary<string, string>>> Files;
            [DataMember(Name = "mainframe", Order = 4)] public string Mainframe;
            [DataMember(Name = "mods", Order = 5, EmitDefaultValue = false)] public ModsKept Mods;
        }

        private static DataContractJsonSerializer Serializer()
        {
            return new DataContractJsonSerializer(typeof(Dump), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
        }

        // ---------- Which files, and their settings ----------

        private const string Plain = Personalities.PlainName, Senpai = Personalities.SenpaiName;
        private static readonly string[] Settled = { "sys.md", "dynamic.md" };
        private static readonly string[] Lined = { "lines.md", "moments.md", "touch.md", "watching.md" };

        // Every settings file by its path in her settings folder; packs and moods by folder and title, since senpai
        // numbers can change. Mods' switches and overrides are signed with this PC's key: an update restores them only
        // as she wrote them; Import does from anywhere.
        [DataContract]
        internal sealed class ModsKept
        {
            // "<kind>:<id>" (code, skin, skill, mod or plugin) to "<hash>|on" or "<hash>|off".
            [DataMember(Name = "on", Order = 0)] public Dictionary<string, string> On;
            // Each side's overrides as her cog keeps them, by the side's name; each side's only ever goes back to that side.
            [DataMember(Name = "overrides", Order = 1)] public Dictionary<string, string> Overrides;
            [DataMember(Name = "signed", Order = 2)] public string Signed;
        }

        // Her mods' block now, and putting one back: how many things changed, with what had no place added to the list.
        internal static Func<ModsKept> ModsNow;
        internal static Func<ModsKept, List<string>, int> PutMods;

        // The block as one text in a fixed order, for its signature.
        internal static string Canonical(ModsKept kept)
        {
            StringBuilder text = new StringBuilder("on\n");
            List<string> keys = kept.On == null ? new List<string>() : new List<string>(kept.On.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string key in keys) text.Append(key).Append('=').Append(kept.On[key]).Append('\n');
            text.Append("overrides\n");
            keys = kept.Overrides == null ? new List<string>() : new List<string>(kept.Overrides.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string key in keys) text.Append(key).Append('\n').Append((kept.Overrides[key] ?? "").Replace("\r\n", "\n")).Append("\n.\n");
            return text.ToString();
        }

        internal static Dictionary<string, string> Files(string folder)
        {
            Dictionary<string, string> found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return found;
            Action<string> add = delegate(string relative) { string path = Path.Combine(folder, relative); if (File.Exists(path)) found[relative.Replace('\\', '/')] = path; };
            add("twitch.md");
            add("layout.md");
            add(Path.Combine("games", "chess.md"));
            foreach (string side in new[] { Plain, Senpai })
            {
                foreach (string one in Settled) add(Path.Combine(side, one));
                foreach (string one in Lined) add(Path.Combine(side, one));
                foreach (string kind in new[] { "personalities", "attitudes" })
                {
                    string packs = Path.Combine(folder, kind, side);
                    if (!Directory.Exists(packs)) continue;
                    foreach (string file in Directory.GetFiles(packs))
                    {
                        string name = Path.GetFileName(file);
                        if (name.IndexOf(".bak", StringComparison.OrdinalIgnoreCase) >= 0 || name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || name.StartsWith(".", StringComparison.Ordinal)) continue;
                        string title = Slots.Titled(file) ?? Path.GetFileNameWithoutExtension(file);
                        found[kind + "/" + side + "/" + title] = file;
                    }
                }
            }
            return found;
        }

        private static bool Pack(string key) { return key.StartsWith("personalities/", StringComparison.OrdinalIgnoreCase) || key.StartsWith("attitudes/", StringComparison.OrdinalIgnoreCase); }
        private static bool LineFile(string key) { foreach (string one in Lined) if (key.EndsWith("/" + one, StringComparison.OrdinalIgnoreCase)) return true; return key.EndsWith("chess.md", StringComparison.OrdinalIgnoreCase); }

        // The sections of a pack or mood that hold settings rather than lines, with or without the switch-on mark.
        private static bool PackSettings(string heading)
        {
            string bare = heading.Replace(CapriDesktop.LineFile.SenpaiMark, "").Trim();
            foreach (string one in new[] { Personalities.LikesHeading, Personalities.WordsHeading, Personalities.DriftHeading, Personalities.ReactsHeading, Personalities.SwitchHeading })
                if (string.Equals(bare, one, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // Each setting of one file, as section, name and value, in the order it is written.
        internal static List<string[]> Read(string key, string[] rows)
        {
            List<string[]> found = new List<string[]>();
            HashSet<int> settings = new HashSet<int>(Notes.SettingRows(rows, LineFile(key)));
            string under = "";
            for (int at = 0; at < rows.Length; at++)
            {
                string one = (rows[at] ?? "").Trim();
                if (one.StartsWith("#", StringComparison.Ordinal)) { under = one.TrimStart('#').Trim(); continue; }
                if (!settings.Contains(at) && !Answer(one)) continue;
                if (Pack(key) && !PackSettings(under)) continue;
                int colon = one.IndexOf(':');
                found.Add(new[] { under, one.Substring(0, colon).Trim(), one.Substring(colon + 1).Trim() });
            }
            return found;
        }

        // Everything, from the files on disk and her Mainframe as they are now.
        internal static Dump Collect(string folder, string version)
        {
            Dump dump = new Dump { Version = version, Saved = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Files = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase) };
            foreach (KeyValuePair<string, string> file in Files(folder))
            {
                string[] rows;
                try { rows = Notes.ReadAll(file.Value).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'); }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                Dictionary<string, Dictionary<string, string>> sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                foreach (string[] one in Read(file.Key, rows))
                {
                    Dictionary<string, string> keys;
                    if (!sections.TryGetValue(one[0], out keys)) sections[one[0]] = keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    if (!keys.ContainsKey(one[1])) keys[one[1]] = one[2];
                }
                if (sections.Count > 0) dump.Files[file.Key] = sections;
            }
            try { if (Mainframe != null) dump.Mainframe = Mainframe(); } catch (Exception caughtOne) { Brain.Caught(caughtOne); }
            try
            {
                ModsKept kept = ModsNow == null ? null : ModsNow();
                if (kept != null) { kept.Signed = PluginSettings.Mark(Canonical(kept)); dump.Mods = kept; }
            }
            catch (Exception caughtOne) { Brain.Caught(caughtOne); }
            return dump;
        }

        internal static string Json(Dump dump)
        {
            using (MemoryStream made = new MemoryStream())
            {
                Serializer().WriteObject(made, dump);
                return Encoding.UTF8.GetString(made.ToArray());
            }
        }

        internal static Dump Parse(string json)
        {
            try
            {
                using (MemoryStream read = new MemoryStream(Encoding.UTF8.GetBytes((json ?? "").TrimStart((char)0xFEFF))))
                    return (Dump)Serializer().ReadObject(read);
            }
            catch (SerializationException) { return null; }
        }

        // The answer to the terms, which sits under them where nothing else is read as a setting.
        private static bool Answer(string one) { return Regex.IsMatch(one ?? "", @"^" + Regex.Escape(SysLines.SenpaiKey) + @"[ \t]*:", RegexOptions.IgnoreCase); }

        // A setting's name that holds a sign-in is never one of these, whatever file it is in.
        private static readonly Regex Secret = new Regex(@"key|token|secret|password|login|oauth|auth|seal|code", RegexOptions.IgnoreCase);
        internal static bool IsSecret(string name) { return Secret.IsMatch(name ?? ""); }

        // From a version with no settings.json: its first update gathers every setting from both sides' files before
        // anything is written new, as settings.before-update.json for her offer. The path, or null.
        internal static string Gather(string folder, string version)
        {
            try
            {
                if (string.IsNullOrEmpty(folder) || File.Exists(Path.Combine(folder, FileName))) return null;
                Func<string> mainframeWas = Mainframe;
                Dump dump;
                Mainframe = null;
                try { dump = Collect(folder, version); }
                finally { Mainframe = mainframeWas; }
                if (dump.Files.Count == 0) return null;
                string before = Path.Combine(folder, BeforeUpdate);
                File.WriteAllText(before, Json(dump), new UTF8Encoding(false));
                return before;
            }
            catch (Exception caughtOne) { Brain.Caught(caughtOne); return null; }
        }

        // ---------- Saving by itself ----------

        // Something of hers changed: only the time is noted.
        internal static void Changed()
        {
            lock (gate) dirtyAt = Now();
            if (timer == null && !string.IsNullOrEmpty(Folder)) lock (gate) if (timer == null) timer = new Timer(delegate { Tick(Now()); }, null, 15000, 15000);
        }

        // Written once two minutes have passed since the last change. True when it wrote.
        internal static bool Tick(long now)
        {
            long at;
            lock (gate) at = dirtyAt;
            if (at < 0 || now - at < Quiet) return false;
            return Save();
        }

        // Written now, if anything changed since the last write: on close, and from the tick.
        internal static bool Save()
        {
            if (string.IsNullOrEmpty(Folder) || Interlocked.CompareExchange(ref saving, 1, 0) != 0) return false;
            try
            {
                long at;
                lock (gate) { at = dirtyAt; dirtyAt = -1; }
                if (at < 0) return false;
                string json = Json(Collect(Folder, Companion.Built));
                string path = Path.Combine(Folder, FileName), writing = path + ".new";
                File.WriteAllText(writing, json, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(writing, path, null);
                else File.Move(writing, path);
                return true;
            }
            catch (Exception caughtOne) { Brain.Caught(caughtOne); return false; }
            finally { Interlocked.Exchange(ref saving, 0); }
        }

        internal static void Stop()
        {
            Timer was;
            lock (gate) { was = timer; timer = null; }
            if (was != null) was.Dispose();
        }

        // ---------- Import ----------

        // Each value written back into its file's line under the same section; stock text, notes and lines stay. Values
        // with no place left are returned, not added. chosen: their own Import; an update takes the mods block only
        // when signed here.
        internal static List<string> Import(Dump dump, string folder, out int set) { return Import(dump, folder, out set, true); }

        internal static List<string> Import(Dump dump, string folder, out int set, bool chosen)
        {
            set = 0;
            List<string> missing = new List<string>();
            List<string[]> moved = new List<string[]>();
            if (dump == null || dump.Files == null) return missing;
            Dictionary<string, string> files = Files(folder);
            foreach (KeyValuePair<string, Dictionary<string, Dictionary<string, string>>> file in dump.Files)
            {
                string path;
                if (!files.TryGetValue(file.Key, out path)) { missing.Add(file.Key); continue; }
                string had = Notes.ReadAll(path);
                string newline = had.Contains("\r\n") ? "\r\n" : "\n";
                string[] rows = had.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                List<string[]> theirs = Read(file.Key, rows);
                HashSet<int> settings = new HashSet<int>(Notes.SettingRows(rows, LineFile(file.Key)));
                bool changed = false;
                foreach (KeyValuePair<string, Dictionary<string, string>> section in file.Value)
                    foreach (KeyValuePair<string, string> one in section.Value)
                    {
                        if (IsSecret(one.Key)) continue;
                        int row = Find(rows, settings, section.Key, one.Key);
                        // A section that lost its switch-on mark: an older pack's "What she likes (overdrive)" is the plain
                        // heading in the new one.
                        if (row < 0 && section.Key.EndsWith(CapriDesktop.LineFile.SenpaiMark, StringComparison.OrdinalIgnoreCase))
                            row = Find(rows, settings, section.Key.Substring(0, section.Key.Length - CapriDesktop.LineFile.SenpaiMark.Length).Trim(), one.Key);
                        // A setting whose section was renamed or moved: the one place in the file that has that name.
                        if (row < 0) row = Only(rows, settings, one.Key);
                        if (row < 0 && string.Equals(one.Key, "quiet time", StringComparison.OrdinalIgnoreCase))
                        {
                            set += QuietHours(rows, settings, section.Key, one.Value);
                            changed = true;
                            continue;
                        }
                        if (row < 0 && Moves(one.Key)) { moved.Add(new[] { file.Key, one.Key, one.Value }); continue; }
                        if (row < 0) { missing.Add(file.Key + " > " + (section.Key.Length == 0 ? "" : section.Key + " > ") + one.Key); continue; }
                        int colon = rows[row].IndexOf(':');
                        string lead = rows[row].Substring(0, colon + 1), gap = Regex.Match(rows[row].Substring(colon + 1), @"^[ \t]*").Value;
                        string made = lead + (gap.Length == 0 ? " " : gap) + (one.Value ?? "");
                        if (made.TrimEnd() == rows[row].TrimEnd()) continue;
                        rows[row] = made;
                        changed = true;
                        set++;
                    }
                if (changed) Notes.Put(path, string.Join(newline, rows));
            }
            if (!string.IsNullOrEmpty(dump.Mainframe) && PutMainframe != null)
            {
                try { set += PutMainframe(dump.Mainframe); } catch (Exception caughtOne) { Brain.Caught(caughtOne); }
            }
            foreach (string[] one in moved)
            {
                try { if (Move != null) { Move(one[0], one[1], one[2]); set++; } } catch (Exception caughtOne) { Brain.Caught(caughtOne); }
            }
            if (dump.Mods != null && PutMods != null)
            {
                if (!chosen && !PluginSettings.Marked(Canonical(dump.Mods), dump.Mods.Signed))
                {
                    missing.Add("your mods' switches and security overrides (changed outside Capri, so an update didn't put them back)");
                    Brain.Log("PROBLEM", -1, "settings.json: its mods block isn't signed here as she wrote it, so the update didn't put it back.");
                }
                else
                {
                    try { set += PutMods(dump.Mods, missing); } catch (Exception caughtOne) { Brain.Caught(caughtOne); }
                }
            }
            Changed();
            return missing;
        }

        // An old "quiet time" in minutes onto quiet min and max hours: a changed time becomes min with max an hour over it,
        // and the stock two hours leaves the new stock 1 to 3 as it is. How many lines it set.
        private static int QuietHours(string[] rows, HashSet<int> settings, string section, string minutes)
        {
            double was;
            if (!double.TryParse((minutes ?? "").Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out was) || was <= 0 || Math.Abs(was - 120) < 0.5) return 0;
            int min = Math.Max(1, Math.Min(SysLines.QuietMostHours - 1, (int)Math.Round(was / 60))), set = 0;
            foreach (KeyValuePair<string, int> one in new[] { new KeyValuePair<string, int>(SysLines.QuietMinKey, min), new KeyValuePair<string, int>(SysLines.QuietMaxKey, min + 1) })
            {
                int row = Find(rows, settings, section, one.Key);
                if (row < 0) continue;
                int colon = rows[row].IndexOf(':');
                string gap = Regex.Match(rows[row].Substring(colon + 1), @"^[ \t]*").Value;
                rows[row] = rows[row].Substring(0, colon + 1) + (gap.Length == 0 ? " " : gap) + one.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                set++;
            }
            return set;
        }

        // The row of a setting by name anywhere in the file, when exactly one row has it; -1 otherwise.
        private static int Only(string[] rows, HashSet<int> settings, string name)
        {
            int found = -1;
            for (int at = 0; at < rows.Length; at++)
            {
                string one = (rows[at] ?? "").Trim();
                if (one.StartsWith("#", StringComparison.Ordinal) || (!settings.Contains(at) && !Empty.IsMatch(one))) continue;
                int colon = one.IndexOf(':');
                if (colon <= 0 || !string.Equals(one.Substring(0, colon).Trim(), name, StringComparison.OrdinalIgnoreCase)) continue;
                if (found >= 0) return -1;
                found = at;
            }
            return found;
        }

        private static readonly Regex Empty = new Regex(@"^[A-Za-z][A-Za-z0-9 _'-]{0,40}:[ \t]*$");

        // The row of a setting under its section, by name; -1 when the file has none.
        private static int Find(string[] rows, HashSet<int> settings, string section, string name)
        {
            string under = "";
            for (int at = 0; at < rows.Length; at++)
            {
                string one = (rows[at] ?? "").Trim();
                if (one.StartsWith("#", StringComparison.Ordinal)) { under = one.TrimStart('#').Trim(); continue; }
                if (!string.Equals(under, section, StringComparison.OrdinalIgnoreCase)) continue;
                // A setting the new file ships empty ("channel:") is still that setting: their value goes in after the colon.
                if (!settings.Contains(at) && !Answer(one) && !Empty.IsMatch(one)) continue;
                int colon = one.IndexOf(':');
                if (colon > 0 && string.Equals(one.Substring(0, colon).Trim(), name, StringComparison.OrdinalIgnoreCase)) return at;
            }
            return -1;
        }

        // ---------- Checks ----------

        // Saved by itself only once nothing has changed for two minutes; export, an update and import bring every setting
        // and the answer to the terms back; nothing secret is ever in it.
        internal static void Check(List<string> checks, string folder)
        {
            string root = Path.Combine(folder, "settingsfile-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            string settings = Path.Combine(root, "personalSettings");
            string folderWas = Folder;
            Func<long> nowWas = Now;
            Func<string> mainframeWas = Mainframe;
            Func<string, int> putWas = PutMainframe;
            long clock = 1000000;
            try
            {
                Directory.CreateDirectory(Path.Combine(settings, Plain));
                Directory.CreateDirectory(Path.Combine(settings, "personalities", Plain));
                string sys = Path.Combine(settings, Plain, "sys.md"), pack = Path.Combine(settings, "personalities", Plain, "Caregiver.md"), lines = Path.Combine(settings, Plain, "lines.md");
                string sysStock = "# Her system lines\r\n\r\n## Her quiet\r\n\r\n>! Notes.\r\nquiet min hours:      1\r\nquiet max hours:      3\r\napi key:              hidden\r\n\r\n## End User Licence Agreement\r\n\r\n>! Terms.\r\n\r\nconfirm:              No\r\n";
                string packStock = "# Caregiver\r\n\r\n## Her character\r\n\r\nYou look after them: kindly.\r\n\r\n## How she reacts\r\n\r\nflirt: -1\r\ncringe: 10\r\n";
                string linesStock = "# Her lines\r\n\r\nrecent hours: 24\r\n\r\n## hello\r\n\r\nhey: there\r\n";
                File.WriteAllText(sys, sysStock.Replace("quiet min hours:      1", "quiet min hours:      4").Replace("quiet max hours:      3", "quiet max hours:      0").Replace("confirm:              No", "confirm:              Yes"));
                File.WriteAllText(pack, packStock.Replace("flirt: -1", "flirt: 2"));
                File.WriteAllText(lines, linesStock.Replace("recent hours: 24", "recent hours: 6"));
                Folder = settings;
                Now = delegate { return clock; };
                Mainframe = delegate { return "{\"Size\":300,\"VoiceKey\":null}"; };
                string put = null;
                PutMainframe = delegate(string given) { put = given; return 1; };

                // A slider dragged for five seconds marks a change many times and writes nothing; one write two minutes after.
                string path = Path.Combine(settings, FileName);
                for (int step = 0; step < 100; step++) { clock += 50; Changed(); Tick(clock); }
                bool quietWhileMoving = !File.Exists(path);
                clock += Quiet - 1000;
                bool notYet = !Tick(clock) && !File.Exists(path);
                clock += 1000;
                bool wrote = Tick(clock) && File.Exists(path);
                bool once = !Tick(clock + Quiet);
                string json = File.ReadAllText(path);
                if (!quietWhileMoving || !notYet || !wrote || !once)
                    throw new Exception("settings.json was not saved by itself as it should be: quiet while moving " + quietWhileMoving + ", not before two minutes " + notYet + ", written at two " + wrote + ", once " + once);

                // Nothing secret: a setting named for a key is never read, and the Mainframe's keys come out null.
                if (json.IndexOf("hidden", StringComparison.Ordinal) >= 0 || json.IndexOf("api key", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new Exception("settings.json carried a key.");
                Dump dump = Parse(json);
                string min, flirt, recent, answer;
                bool read;
                try
                {
                    read = dump != null && dump.Files[Plain + "/sys.md"]["Her quiet"].TryGetValue("quiet min hours", out min) && min == "4"
                    && dump.Files["personalities/" + Plain + "/Caregiver"]["How she reacts"].TryGetValue("flirt", out flirt) && flirt == "2"
                    && !dump.Files["personalities/" + Plain + "/Caregiver"].ContainsKey("Her character")
                    && dump.Files[Plain + "/lines.md"]["Her lines"].TryGetValue("recent hours", out recent) && recent == "6" && !dump.Files[Plain + "/lines.md"].ContainsKey("hello")
                    && dump.Files[Plain + "/sys.md"]["End User Licence Agreement"].TryGetValue("confirm", out answer) && answer == "Yes";
                }
                catch (KeyNotFoundException) { read = false; }
                if (!read) throw new Exception("settings.json did not hold the settings, or held lines: " + json);

                // An update writes every file new; importing brings each value and the answer back, and a setting the new
                // file no longer has is listed rather than added.
                File.WriteAllText(sys, sysStock);
                File.WriteAllText(pack, packStock);
                File.WriteAllText(lines, linesStock);
                dump.Files[Plain + "/sys.md"]["Her quiet"]["quiet gone"] = "1";
                int set;
                List<string> missing = Import(dump, settings, out set);
                string sysAfter = File.ReadAllText(sys), packAfter = File.ReadAllText(pack), linesAfter = File.ReadAllText(lines);
                bool back = sysAfter.Contains("quiet min hours:      4") && sysAfter.Contains("quiet max hours:      0") && sysAfter.Contains("confirm:              Yes")
                    && packAfter.Contains("flirt: 2") && linesAfter.Contains("recent hours: 6") && put != null;
                bool kept = sysAfter.Contains(">! Notes.") && sysAfter.Contains("api key:              hidden") && packAfter.Contains("You look after them: kindly.") && linesAfter.Contains("hey: there");
                bool listed = missing.Count == 1 && missing[0].Contains("quiet gone") && !sysAfter.Contains("quiet gone");
                if (!back || !kept || !listed)
                    throw new Exception("Importing settings.json went wrong: values back " + back + ", the rest of each file kept " + kept + ", a missing setting listed and not added " + listed + " (" + string.Join(", ", missing.ToArray()) + ")");
                // A version from before settings.json: nothing saved, so it is gathered from the files before they are written new.
                File.Delete(path);
                File.WriteAllText(sys, sysStock.Replace("quiet min hours:      1", "quiet min hours:      6").Replace("confirm:              No", "confirm:              Yes"));
                File.WriteAllText(pack, packStock.Replace("cringe: 10", "cringe: 70"));
                string gathered = Gather(settings, "0.9.429");
                bool gatheredRight = gathered != null && File.Exists(gathered) && Gather(Path.Combine(settings, "nothing-here"), "0.9.429") == null;
                File.WriteAllText(sys, sysStock);
                File.WriteAllText(pack, packStock);
                int setAgain;
                List<string> missingAgain = Import(Parse(File.ReadAllText(gathered)), settings, out setAgain);
                string sysBack = File.ReadAllText(sys), packBack = File.ReadAllText(pack);
                if (!gatheredRight || !sysBack.Contains("quiet min hours:      6") || !sysBack.Contains("confirm:              Yes") || !packBack.Contains("cringe: 70") || missingAgain.Count != 0)
                    throw new Exception("An update from a version with no settings.json didn't bring its settings back: gathered " + gatheredRight + ", missing " + string.Join(", ", missingAgain.ToArray()));
                // Into stock files that ship a setting empty, and from an old quiet time.
                string twitch = Path.Combine(settings, "twitch.md");
                File.WriteAllText(twitch, "# Capri on Twitch\r\n\r\n## My channel\r\n\r\nchannel:\r\n");
                File.WriteAllText(sys, sysStock);
                Dump old = new Dump { Files = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase) };
                old.Files["twitch.md"] = new Dictionary<string, Dictionary<string, string>> { { "My channel", new Dictionary<string, string> { { "channel", "sam_streams" } } } };
                old.Files[Plain + "/sys.md"] = new Dictionary<string, Dictionary<string, string>> { { "Her quiet", new Dictionary<string, string> { { "quiet time", "300" } } } };
                old.Files["personalities/" + Plain + "/Caregiver"] = new Dictionary<string, Dictionary<string, string>> { { "How she reacts" + CapriDesktop.LineFile.SenpaiMark, new Dictionary<string, string> { { "flirt", "4" } } }, { "Old heading", new Dictionary<string, string> { { "cringe", "33" } } } };
                File.WriteAllText(pack, packStock);
                int setOld;
                List<string> missingOld = Import(old, settings, out setOld);
                string twitchBack = File.ReadAllText(twitch), sysOld = File.ReadAllText(sys), packOld = File.ReadAllText(pack);
                if (!packOld.Contains("flirt: 4") || !packOld.Contains("cringe: 33")) throw new Exception("A setting whose section lost its mark or was renamed didn't come back: " + packOld);
                if (!twitchBack.Contains("channel: sam_streams") || !sysOld.Contains("quiet min hours:      5") || !sysOld.Contains("quiet max hours:      6") || missingOld.Count != 0)
                    throw new Exception("Importing into stock files went wrong: an empty setting filled " + twitchBack.Contains("channel: sam_streams") + ", an old quiet time on min and max " + (sysOld.Contains("quiet min hours:      5") && sysOld.Contains("quiet max hours:      6")) + ", missing " + string.Join(", ", missingOld.ToArray()));
                File.WriteAllText(path, "{}");
                if (Gather(settings, "0.9.429") != null) throw new Exception("Settings were gathered over a settings.json that was already there.");
                checks.Add("PASS settings.json holds every setting of hers and her Mainframe's, never a line she says or a key; it is written by itself two minutes after the last change and never while a slider moves; importing it after an update writes each value and the answer to the terms back into the new files, keeps their text, and lists a setting the new file no longer has instead of adding it; an update from a version that had no settings.json gathers one from the files before writing them new, and Yes brings every value back, into a setting the new file ships empty too, with an old quiet time on quiet min and max hours, and a setting whose section lost its switch-on mark or was renamed found where it is now");
            }
            finally
            {
                Folder = folderWas;
                Now = nowWas;
                Mainframe = mainframeWas;
                PutMainframe = putWas;
                lock (gate) dirtyAt = -1;
                try { Directory.Delete(root, true); } catch (Exception caughtOne) { Brain.Caught(caughtOne); }
            }
            Companion.CheckModsKept(checks, folder);
        }
    }
}
