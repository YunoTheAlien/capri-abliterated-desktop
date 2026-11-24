using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Her settings out to a file and back: Export and Import under SETTINGS, and once after an update they are put
    // back from the settings.json she kept.
    internal sealed partial class Companion
    {
        // Her Mainframe settings as JSON, every key and anything sealed left out.
        private string MainframeSettings()
        {
            DataContractJsonSerializer json = new DataContractJsonSerializer(typeof(Preferences));
            Preferences copy;
            using (MemoryStream made = new MemoryStream())
            {
                json.WriteObject(made, preferences);
                made.Position = 0;
                copy = (Preferences)json.ReadObject(made);
            }
            foreach (FieldInfo field in Carried())
                if (!Kept(field)) field.SetValue(copy, field.FieldType.IsValueType ? Activator.CreateInstance(field.FieldType) : null);
            using (MemoryStream made = new MemoryStream())
            {
                json.WriteObject(made, copy);
                return Encoding.UTF8.GetString(made.ToArray());
            }
        }

        private static IEnumerable<FieldInfo> Carried()
        {
            foreach (FieldInfo field in typeof(Preferences).GetFields(BindingFlags.Instance | BindingFlags.Public))
                if (field.IsDefined(typeof(DataMemberAttribute), false)) yield return field;
        }

        // Never in the file and never put back: a key, a token, anything sealed, her remote pairing, and what she keeps for
        // herself about this install.
        private static bool Kept(FieldInfo field)
        {
            if (field.FieldType == typeof(RemoteSettings)) return false;
            if (field.FieldType == typeof(string) && SettingsFile.IsSecret(field.Name)) return false;
            foreach (string own in new[] { "LastVersion", "SenpaiOn", "CareWritten" }) if (field.Name == own) return false;
            return true;
        }

        // Her Mainframe settings back from the file: each one that is there, and different, set. How many were set.
        private int PutMainframeSettings(string json)
        {
            Preferences from;
            try
            {
                using (MemoryStream read = new MemoryStream(Encoding.UTF8.GetBytes(json ?? "")))
                    from = (Preferences)new DataContractJsonSerializer(typeof(Preferences)).ReadObject(read);
            }
            catch (SerializationException) { return 0; }
            if (from == null) return 0;
            int set = 0;
            foreach (FieldInfo field in Carried())
            {
                if (!Kept(field)) continue;
                object now = field.GetValue(preferences), then = field.GetValue(from);
                if (Equals(now, then) || (then == null && field.FieldType != typeof(string))) continue;
                field.SetValue(preferences, then);
                set++;
            }
            if (set > 0) { SavePreferences(); if (mainframe != null) mainframe.Sync(); }
            return set;
        }

        // Load untested mods left sys.md for the switch in Mods. At her start each sys.md still holding it, the one in use first,
        // hands its answer over once and loses the line; a settings.json from before routes it here too, from this side's file.
        private void MoveUntested() { MoveUntested(SysLines.Copies()); }

        private void MoveUntested(params string[] paths)
        {
            SettingsFile.Moves = delegate(string name)
            {
                return string.Equals(name, SysLines.UntestedKey, StringComparison.OrdinalIgnoreCase) || string.Equals(name, SysLines.SaverKey, StringComparison.OrdinalIgnoreCase) || string.Equals(name, SysLines.SaverStyleKey, StringComparison.OrdinalIgnoreCase) || string.Equals(name, SysLines.SaverAfterKey, StringComparison.OrdinalIgnoreCase) || Array.IndexOf(SysLines.QuietKeys, (name ?? "").ToLowerInvariant()) >= 0
                    || string.Equals(name, QuietTimeKey, StringComparison.OrdinalIgnoreCase) || SaverColour(name);
            };
            SettingsFile.Move = delegate(string file, string name, string value)
            {
                string side = (Brain.SenpaiOn ? Personalities.SenpaiName : Personalities.PlainName) + "/" + SysLines.FileName;
                if (!string.Equals(file, side, StringComparison.OrdinalIgnoreCase)) return;
                if (SaverColour(name)) { PutSaverColour(name, value); return; }
                string said = (value ?? "").Trim();
                if (string.Equals(name, SysLines.SaverKey, StringComparison.OrdinalIgnoreCase)) SetScreensaver(SysLines.On(said, false));
                else if (string.Equals(name, SysLines.SaverStyleKey, StringComparison.OrdinalIgnoreCase)) SetSaverStyle(said);
                else if (string.Equals(name, SysLines.SaverAfterKey, StringComparison.OrdinalIgnoreCase)) SetSaverAfter(Minutes(said));
                else if (string.Equals(name, SysLines.UntestedKey, StringComparison.OrdinalIgnoreCase))
                    SetUntested(said.Equals("yes", StringComparison.OrdinalIgnoreCase) || said == "1" || said.Equals("true", StringComparison.OrdinalIgnoreCase));
                else PutQuiet(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { name, said } });
            };
            foreach (string path in paths)
            {
                bool? one = SysLines.TakeUntested(path);
                if (untestedTaken == null) untestedTaken = one;
                bool? saver = SysLines.TakeSaver(path);
                if (saverTaken == null) saverTaken = saver;
                string[] intros = SysLines.TakeIntros(path);
                if (introsTaken == null) introsTaken = intros;
                Dictionary<string, string> picks = SysLines.TakeSaverPicks(path);
                if (saverPicksTaken == null) saverPicksTaken = picks;
                Dictionary<string, string> quiet = SysLines.TakeQuiet(path);
                if (quietTaken == null) quietTaken = quiet;
            }
            if (preferences != null && quietTaken != null) { PutQuiet(quietTaken); quietTaken = null; }
            // Her sys.md files are tidied before her settings are read at the start; the answer waits for them.
            if (preferences == null) return;
            if (untestedTaken != null && untestedTaken.Value != preferences.LoadUntested) { preferences.LoadUntested = untestedTaken.Value; SavePreferences(); }
            untestedTaken = null;
            if (saverTaken != null && saverTaken.Value != preferences.ScreensaverOn) { preferences.ScreensaverOn = saverTaken.Value; SavePreferences(); }
            saverTaken = null;
            if (introsTaken != null) { preferences.Intros = string.Join(",", introsTaken); SavePreferences(); }
            introsTaken = null;
            if (saverPicksTaken != null)
            {
                string said;
                if (saverPicksTaken.TryGetValue(SysLines.SaverStyleKey, out said) && said.Length > 0) preferences.SaverStyle = said;
                if (saverPicksTaken.TryGetValue(SysLines.SaverAfterKey, out said) && Minutes(said) > 0) preferences.SaverAfter = Minutes(said);
                SavePreferences();
            }
            saverPicksTaken = null;
            Mods.Untested = preferences.LoadUntested;
            if (Mods.Untested) ModGuard.ForgetUnchecked();
        }

        private bool? untestedTaken, saverTaken;
        private string[] introsTaken;

        // One intro on or off in Toggles > Intros; with more than one on she picks one at random.
        internal bool IntroOn(string name) { return Array.IndexOf(SysLines.Intros(), name) >= 0; }

        internal void SetIntro(string name, bool on)
        {
            List<string> picked = new List<string>(SysLines.Intros());
            picked.Remove(name);
            if (on) picked.Add(name);
            preferences.Intros = string.Join(",", picked.ToArray());
            SavePreferences();
            if (mainframe != null) mainframe.Sync();
        }
        private Dictionary<string, string> saverPicksTaken;

        private static double Minutes(string said)
        {
            double minutes;
            return double.TryParse((said ?? "").Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out minutes) && minutes > 0 ? minutes : 0;
        }

        // The Screensaver group's style and wait, kept in her settings.
        internal string SaverStyle { get { return string.IsNullOrEmpty(preferences.SaverStyle) ? SaverLook.Terminals : preferences.SaverStyle; } }

        internal double SaverAfterMinutes { get { return SysLines.SaverAfter; } }

        internal void SetSaverStyle(string style)
        {
            foreach (string one in SaverLook.Styles) if (string.Equals(one, (style ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) { preferences.SaverStyle = one; SavePreferences(); return; }
        }

        internal void SetSaverAfter(double minutes)
        {
            if (minutes <= 0) return;
            preferences.SaverAfter = minutes;
            SavePreferences();
        }

        // The Screensaver switch in her Toggles: kept in her settings, and what SysLines.Saver reads.
        internal bool ScreensaverOn { get { return preferences.ScreensaverOn; } }

        internal void SetScreensaver(bool on)
        {
            if (preferences.ScreensaverOn == on) return;
            preferences.ScreensaverOn = on;
            SavePreferences();
            Brain.Log("screensaver", -1, "Switched " + (on ? "on" : "off") + ", saved.");
            if (!on && SaverShowing) SaverOff();
            if (mainframe != null) mainframe.Sync();
        }

        // The screensaver's two colours left sys.md for its fold under ADD-ONS. At her start each sys.md still holding them,
        // the one in use first, hands them over once; a settings.json from before routes them here too.
        private void MoveSaverColours()
        {
            HashSet<string> put = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in SysLines.Copies())
                foreach (KeyValuePair<string, string> one in SysLines.TakeSaverColours(path))
                    if (put.Add(one.Key)) PutSaverColour(one.Key, one.Value);
        }

        private static bool SaverColour(string name)
        {
            return string.Equals(name, SysLines.SaverColourKey, StringComparison.OrdinalIgnoreCase) || string.Equals(name, SysLines.SaverGroundKey, StringComparison.OrdinalIgnoreCase);
        }

        // Its one colour was used by every style, so it goes to the hexagons too.
        private static void PutSaverColour(string name, string value)
        {
            System.Drawing.Color? colour = SysLines.Mixed(value);
            if (!colour.HasValue) return;
            Dictionary<string, System.Drawing.Color?> set = new Dictionary<string, System.Drawing.Color?>();
            if (string.Equals(name, SysLines.SaverGroundKey, StringComparison.OrdinalIgnoreCase)) set["Background"] = colour;
            else { set["Colour"] = colour; set["Hexagons"] = colour; }
            AddonColours.Set(AddonColours.Screensaver, set);
        }

        // Her quiet's lines from a sys.md from before, waiting for her settings to be read; and the name quiet time had
        // before the quiet min and max hours, in minutes.
        private Dictionary<string, string> quietTaken;
        internal const string QuietTimeKey = "quiet time";

        // Quiet lines from an older sys.md, by name, into her settings. An old quiet time in minutes becomes min with max an
        // hour over it; the stock two hours leaves the stock 1 to 3. Max 0 is until turned off.
        internal void PutQuiet(Dictionary<string, string> lines)
        {
            string said;
            int min = QuietMinHours, max = QuietMaxHours;
            if (lines.TryGetValue(QuietTimeKey, out said))
            {
                double minutes;
                if (double.TryParse(said, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out minutes) && minutes > 0 && Math.Abs(minutes - 120) >= 0.5)
                {
                    min = Math.Max(1, Math.Min(QuietMostHours - 1, (int)Math.Round(minutes / 60)));
                    max = min + 1;
                }
            }
            if (lines.TryGetValue(SysLines.QuietMinKey, out said)) min = SysLines.WholeHours(said, min);
            if (lines.TryGetValue(SysLines.QuietMaxKey, out said)) max = SysLines.WholeHours(said, max);
            SetQuietHours(min, max);
            if (lines.TryGetValue(SysLines.QuietCallsKey, out said)) preferences.NoQuietCalls = !SysLines.On(said, true);
            if (lines.TryGetValue(SysLines.QuietVideoKey, out said)) preferences.NoQuietVideos = !SysLines.On(said, true);
            if (lines.TryGetValue(SysLines.CallAppsKey, out said)) SetCallApps(said);
            SavePreferences();
            if (mainframe != null) mainframe.Sync();
        }

        // The switch in Mods > MODS.
        internal bool UntestedMods { get { return preferences.LoadUntested; } }

        // Her mods in settings.json: switches and this side's overrides go out and back, a never-approved mod or
        // starter stays off, nothing sealed goes in, and an update takes the block only when signed here; their own
        // Import takes it from anywhere.
        internal static void CheckModsKept(List<string> checks, string output)
        {
            string data = Path.Combine(output, "mods-kept-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            string startersWas = Mods.StartersFolder, keptWas = ModDirectory.KeptFolder, settingsWas = PluginSettings.Settings;
            Func<SettingsFile.ModsKept> nowWas = SettingsFile.ModsNow;
            Func<SettingsFile.ModsKept, List<string>, int> putWas = SettingsFile.PutMods;
            try
            {
                Directory.CreateDirectory(data);
                Mods.StartersFolder = Path.Combine(data, "keys");
                PluginSettings.Settings = data;
                PluginSettings.Reseal();
                Mods list = new Mods(data);
                list.AddStarters();
                if (!list.SetPlugin("notion", true, delegate { return true; })) throw new Exception("A starter couldn't be turned on for the check.");
                PluginSettings.Set("notion", PluginSettings.Tool, "search", Security.Guarded, PluginSettings.Allowed);
                SettingsFile.ModsKept kept = ModsKeptOf(list, null);
                kept.Signed = PluginSettings.Mark(SettingsFile.Canonical(kept));
                string json = SettingsFile.Json(new SettingsFile.Dump { Files = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>(), Mods = kept });
                string on;
                bool written = kept.On.TryGetValue("plugin:notion", out on) && on.EndsWith("|on", StringComparison.Ordinal) && kept.On["plugin:email"].EndsWith("|off", StringComparison.Ordinal)
                    && kept.Overrides[SideNow].Contains("search") && kept.Signed != null;
                bool sealedOut = json.IndexOf("signin", StringComparison.OrdinalIgnoreCase) < 0 && json.IndexOf("password", StringComparison.OrdinalIgnoreCase) < 0 && json.IndexOf("account", StringComparison.OrdinalIgnoreCase) < 0;

                // Changed here since: put back as it was.
                list.SetPlugin("notion", false, null);
                PluginSettings.Set("notion", PluginSettings.Tool, "search", Security.Guarded, PluginSettings.Follows);
                List<string> missing = new List<string>();
                int put = PutModsBack(list, kept, missing);
                bool back = put >= 2 && !list.InstalledPlugins().Find(one => one.Id == "notion").Off
                    && PluginSettings.Of("notion", PluginSettings.Tool, "search", Security.Guarded) == PluginSettings.Allowed;

                // A file saying a starter never turned on here is on: it stays off, and nothing is approved for it.
                SettingsFile.ModsKept forged = new SettingsFile.ModsKept { On = new Dictionary<string, string>(kept.On), Overrides = kept.Overrides, Signed = kept.Signed };
                forged.On["plugin:email"] = forged.On["plugin:email"].Replace("|off", "|on");
                PutModsBack(list, forged, missing);
                PluginInfo email = list.InstalledPlugins().Find(one => one.Id == Mods.MailStarter);
                bool stayedOff = email.Off && email.Unapproved;

                // The signature: an update takes only the block she signed here; their Import takes either.
                int called = 0;
                SettingsFile.PutMods = delegate { called++; return 0; };
                int set;
                SettingsFile.Dump signedDump = new SettingsFile.Dump { Files = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>(), Mods = kept };
                SettingsFile.Dump forgedDump = new SettingsFile.Dump { Files = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>(), Mods = forged };
                SettingsFile.Import(signedDump, data, out set, false);
                bool updateTook = called == 1;
                List<string> refused = SettingsFile.Import(forgedDump, data, out set, false);
                bool updateRefused = called == 1 && refused.Exists(one => one.Contains("changed outside Capri"));
                SettingsFile.Import(forgedDump, data, out set, true);
                bool importTook = called == 2;
                if (!written || !sealedOut || !back || !stayedOff || !updateTook || !updateRefused || !importTook)
                    throw new Exception("Her mods in settings.json went wrong: written " + written + ", nothing sealed " + sealedOut + ", put back " + back + " (" + put + "), a starter never turned on here stayed off " + stayedOff
                        + ", an update took her signed block " + updateTook + ", refused a changed one " + updateRefused + ", their Import took it " + importTook);
                // Each switch, approval and cell marks her settings.json changed, so what she writes on close (as an update
                // closes her) holds it straight away.
                string folderWas = SettingsFile.Folder;
                bool held = false, approvedMarks = false, cellMarks = false;
                try
                {
                    SettingsFile.Folder = data;
                    SettingsFile.ModsNow = delegate { return ModsKeptOf(list, null); };
                    SettingsFile.Stop();
                    SettingsFile.Save();
                    list.SetPlugin("notion", false, null);
                    SettingsFile.Stop();
                    bool wrote = SettingsFile.Save();
                    SettingsFile.Dump after = SettingsFile.Parse(File.ReadAllText(Path.Combine(data, SettingsFile.FileName)));
                    held = wrote && after != null && after.Mods != null && after.Mods.On["plugin:notion"].EndsWith("|off", StringComparison.Ordinal);
                    list.SetPlugin("linear", true, delegate { return true; });
                    approvedMarks = SettingsFile.Save();
                    PluginSettings.Set("linear", PluginSettings.Tool, "search", Security.Trusted, PluginSettings.Denied);
                    cellMarks = SettingsFile.Save();
                }
                finally { SettingsFile.Stop(); SettingsFile.Folder = folderWas; }
                if (!held || !approvedMarks || !cellMarks) throw new Exception("A mod change didn't reach settings.json straight away: a switch " + held + ", an approval " + approvedMarks + ", a cell " + cellMarks);
                checks.Add("PASS a mod or plug-in switched, an approval given or a security override changed marks her settings.json, so what she writes as she closes for an update holds the new state");
                checks.Add("PASS her mods in settings.json: each mod's and plug-in's switch and this side's security overrides go in and come back, a mod or starter never turned on here stays off whatever the file says and nothing is approved by it, nothing sealed is in it, and an update takes the block only when it is signed here as she wrote it while their own Import takes it from anywhere");
            }
            finally
            {
                SettingsFile.ModsNow = nowWas;
                SettingsFile.PutMods = putWas;
                Mods.StartersFolder = startersWas;
                ModDirectory.KeptFolder = keptWas;
                PluginSettings.Settings = settingsWas;
                PluginSettings.Reseal();
                try { Directory.Delete(data, true); } catch (Exception) { }
            }
        }

        private static string KindOf(Mod mod) { return mod.Code ? "code" : mod.Skin ? "skin" : mod.Skill ? "skill" : "mod"; }

        private static string SideNow { get { return PluginSettings.SecondSide() ? Personalities.SenpaiName : Personalities.PlainName; } }

        // Each mod's and plug-in's switch with the file it was, and this side's overrides; the other side's as the last
        // settings.json had them, since only that side reads its own.
        internal static SettingsFile.ModsKept ModsKeptOf(Mods list, string lastFile)
        {
            if (list == null) return null;
            SettingsFile.ModsKept kept = new SettingsFile.ModsKept { On = new Dictionary<string, string>(StringComparer.Ordinal), Overrides = new Dictionary<string, string>(StringComparer.Ordinal) };
            foreach (Mod mod in list.All)
                if (mod.Plugin == null && mod.Owner == null && !mod.Own) kept.On[KindOf(mod) + ":" + mod.File.Id] = (mod.Hash ?? "") + "|" + (mod.On ? "on" : "off");
            foreach (PluginInfo plugin in list.InstalledPlugins()) kept.On["plugin:" + plugin.Id] = (plugin.Hash ?? "") + "|" + (plugin.Off ? "off" : "on");
            string side = SideNow;
            kept.Overrides[side] = PluginSettings.Snapshot();
            try
            {
                SettingsFile.Dump last = lastFile != null && File.Exists(lastFile) ? SettingsFile.Parse(File.ReadAllText(lastFile)) : null;
                if (last != null && last.Mods != null && last.Mods.Overrides != null)
                    foreach (KeyValuePair<string, string> other in last.Mods.Overrides) if (other.Key != side) kept.Overrides[other.Key] = other.Value;
            }
            catch (IOException caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            return kept;
        }

        // The block put back: a mod switched as it was, only when it is the same file and approved here as it is now. Nothing
        // is approved by an import, so a mod or starter never said yes to on this PC stays off. This side's overrides only.
        internal static int PutModsBack(Mods list, SettingsFile.ModsKept kept, List<string> missing)
        {
            if (list == null || kept == null) return 0;
            int put = 0;
            if (kept.On != null)
                foreach (KeyValuePair<string, string> pair in kept.On)
                {
                    int colon = pair.Key.IndexOf(':'), bar = (pair.Value ?? "").LastIndexOf('|');
                    if (colon <= 0 || bar < 0) continue;
                    string kind = pair.Key.Substring(0, colon), id = pair.Key.Substring(colon + 1), hash = pair.Value.Substring(0, bar);
                    bool wanted = pair.Value.Substring(bar + 1) == "on";
                    try
                    {
                        if (kind == "plugin")
                        {
                            PluginInfo plugin = list.InstalledPlugins().Find(one => one.Id == id);
                            if (plugin == null) { missing.Add("the plug-in " + id); continue; }
                            if (wanted == !plugin.Off || (wanted && ((plugin.Hash ?? "") != hash || plugin.Unapproved || plugin.Changed))) continue;
                            if (list.SetPlugin(id, wanted, null)) put++;
                            continue;
                        }
                        Mod mod = list.All.Find(one => one.Plugin == null && one.Owner == null && !one.Own && one.File.Id == id && KindOf(one) == kind);
                        if (mod == null) { missing.Add("the mod " + id); continue; }
                        if (mod.On == wanted || (wanted && (mod.Changed || (mod.Hash ?? "") != hash))) continue;
                        if (list.Set(mod, wanted, delegate { return false; }) && mod.On == wanted) put++;
                    }
                    catch (ModException caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                    catch (IOException caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                    catch (UnauthorizedAccessException caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                }
            string text;
            if (kept.Overrides != null && kept.Overrides.TryGetValue(SideNow, out text) && text != null && text != PluginSettings.Snapshot()) { PluginSettings.Restore(text); put++; }
            return put;
        }

        internal void SetUntested(bool on)
        {
            Mods.Untested = on;
            // On, by any route: the mods switched off for it lose that reason, and stay off until turned on.
            if (on) ModGuard.ForgetUnchecked();
            if (preferences.LoadUntested == on) return;
            preferences.LoadUntested = on;
            SavePreferences();
        }

        private void StartSettingsFile(string data)
        {
            SettingsFile.Folder = Path.Combine(data, Personalities.SettingsName);
            SettingsFile.Mainframe = MainframeSettings;
            SettingsFile.ModsNow = delegate { return ModsKeptOf(ModList, Path.Combine(SettingsFile.Folder, SettingsFile.FileName)); };
            SettingsFile.PutMods = delegate(SettingsFile.ModsKept kept, List<string> missing)
            {
                int put = 0;
                if (InvokeRequired) Invoke((Action)delegate { put = PutModsBack(ModList, kept, missing); });
                else put = PutModsBack(ModList, kept, missing);
                return put;
            };
            SettingsFile.PutMainframe = delegate(string json)
            {
                int set = 0;
                if (InvokeRequired) Invoke((Action)delegate { set = PutMainframeSettings(json); });
                else set = PutMainframeSettings(json);
                return set;
            };
            // Written once at the start when there is none yet, two minutes on like any change.
            if (!File.Exists(Path.Combine(SettingsFile.Folder, SettingsFile.FileName))) SettingsFile.Changed();
        }

        // Set at the start of an update from a version that had no settings.json: what was gathered from her files then.
        private string gatheredSettings;

        // On a version change, before anything is written new: from a version with no settings.json, every setting gathered
        // from the files on disk.
        private void GatherSettings(string data)
        {
            if (Renew.Changing) gatheredSettings = SettingsFile.Gather(Path.Combine(data, Personalities.SettingsName), Renew.Before ?? "before " + Built);
        }

        // An update wrote her files new: the settings.json from before it, or the one gathered from her files, is kept as
        // settings.before-update.json and put back, without asking. Whatever would not go back is shown in a window and
        // logged as a PROBLEM.
        private void OfferSettingsBack(int replaced)
        {
            try
            {
                if (replaced == 0 || string.IsNullOrEmpty(SettingsFile.Folder)) return;
                string kept = Path.Combine(SettingsFile.Folder, SettingsFile.FileName), before = Path.Combine(SettingsFile.Folder, SettingsFile.BeforeUpdate);
                if (gatheredSettings == null)
                {
                    if (!File.Exists(kept)) return;
                    File.Copy(kept, before, true);
                }
                else if (!File.Exists(before)) return;
                AfterIntro(delegate { ImportSettings(before, true); });
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        internal string ImportSettings(string path) { return ImportSettings(path, false); }

        // From a settings.json: each value back into her files and her Mainframe, and one line in her bubble saying so. After
        // an update, anything that would not go back is also listed in a window and logged.
        private string ImportSettings(string path, bool updating)
        {
            string said;
            List<string> missing = new List<string>();
            string failed = null;
            try
            {
                SettingsFile.Dump dump = SettingsFile.Parse(File.ReadAllText(path));
                if (dump == null) { said = "That isn't a settings file of mine."; failed = said; }
                else
                {
                    int set;
                    missing = SettingsFile.Import(dump, SettingsFile.Folder, out set, !updating);
                    said = updating
                        ? (set == 0 ? "I'm on a new version, and your settings are all still here." : "I'm on a new version, and I've put your settings back: " + set + (set == 1 ? " setting." : " settings.") + " Some take effect when I next start.")
                        : set == 0 ? "Nothing to change: those are already my settings." : "Done, " + set + (set == 1 ? " setting is" : " settings are") + " back. Some take effect when I next start.";
                    if (missing.Count > 0)
                        said += " Not in this version: " + string.Join(", ", missing.Count <= 4 ? missing.ToArray() : missing.GetRange(0, 4).ToArray()) + (missing.Count > 4 ? " and " + (missing.Count - 4) + " more." : ".");
                }
            }
            catch (Exception caught) { said = "I couldn't read that settings file: " + caught.Message; failed = caught.Message; }
            // After an update, what didn't come back is listed in its window, so her bubble doesn't say it again.
            if (updating && (failed != null || missing.Count > 0)) SettingsProblem(path, failed, missing);
            else Remark("", said, true);
            return said;
        }

        // What did not come back after an update, in her log and in a window that lists it all.
        private void SettingsProblem(string path, string failed, List<string> missing)
        {
            try
            {
                string what = failed != null
                    ? "Your settings from before the update could not be read: " + failed
                    : "These settings from before the update have no place in this version, so they were not put back:";
                Brain.Log("PROBLEM", -1, "settings after an update: " + (failed ?? missing.Count + " not put back: " + string.Join(", ", missing.ToArray())));
                StringBuilder told = new StringBuilder(what);
                if (failed == null) foreach (string one in missing) told.Append("\r\n  " + one);
                told.Append("\r\n\r\nEverything from before is kept in " + Path.GetFileName(path) + " in her settings folder.");
                // A test run has no one to press OK, so it only logs.
                if (!SmokeRun.Worker) Notice.Tell(this, told.ToString(), "Settings after the update", true);
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        internal string ExportSettings(string path)
        {
            try
            {
                File.WriteAllText(path, SettingsFile.Json(SettingsFile.Collect(SettingsFile.Folder, Built)), new UTF8Encoding(false));
                return "Saved to " + Path.GetFileName(path) + ".";
            }
            catch (Exception caught) { return "Couldn't save it: " + caught.Message; }
        }

        internal string SettingsFolder { get { return SettingsFile.Folder; } }
    }

    internal sealed partial class Mainframe
    {
        private Key exportSettings, importSettings;

        // Under the SETTINGS title: her settings out to a file, and back from one.
        private void BuildTransfer(int c, int y)
        {
            exportSettings = Make("Export settings", 28 + c, y + 3, 160, 22, delegate { ExportSettings(); });
            exportSettings.AccessibleName = "Export settings: save every setting of hers to a settings.json you choose";
            importSettings = Make("Import settings", 28 + c + 168, y + 3, 160, 22, delegate { ImportSettings(); });
            importSettings.AccessibleName = "Import settings: put every setting back from a settings.json";
        }

        private void ExportSettings()
        {
            using (SaveFileDialog pick = new SaveFileDialog())
            {
                pick.Title = "Export settings";
                pick.Filter = "Settings (*.json)|*.json";
                pick.FileName = SettingsFile.FileName;
                if (Directory.Exists(capri.SettingsFolder ?? "")) pick.InitialDirectory = capri.SettingsFolder;
                if (pick.ShowDialog(this) != DialogResult.OK) return;
                string said = capri.ExportSettings(pick.FileName);
                Notice.Tell(this, said, "Export settings", said.StartsWith("Couldn", StringComparison.Ordinal));
            }
        }

        private void ImportSettings()
        {
            using (OpenFileDialog pick = new OpenFileDialog())
            {
                pick.Title = "Import settings";
                pick.Filter = "Settings (*.json)|*.json";
                if (Directory.Exists(capri.SettingsFolder ?? "")) pick.InitialDirectory = capri.SettingsFolder;
                if (pick.ShowDialog(this) != DialogResult.OK) return;
                capri.ImportSettings(pick.FileName);
                Sync();
            }
        }
    }
}
