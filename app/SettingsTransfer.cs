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
                Dictionary<string, string> looks = SysLines.TakeSaverLooks(path);
                if (saverLooksTaken == null) saverLooksTaken = looks;
                Dictionary<string, string> quiet = SysLines.TakeQuiet(path);
                if (quietTaken == null) quietTaken = quiet;
            }
            if (preferences != null && quietTaken != null) { PutQuiet(quietTaken); quietTaken = null; }
            // Her sys.md files are tidied before her settings are read at the start; the answer waits for them.
            if (preferences == null) return;
            if (untestedTaken != null && untestedTaken.Value != preferences.LoadUntested) { preferences.LoadUntested = untestedTaken.Value; SavePreferences(); }
            untestedTaken = null;
            if (saverTaken != null && saverTaken.Value != ScreensaverOn) SetScreensaver(saverTaken.Value);
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
            if (saverLooksTaken != null) PutSaverLooks(saverLooksTaken);
            saverLooksTaken = null;
            Mods.Untested = preferences.LoadUntested;
            if (Mods.Untested) ModGuard.ForgetUnchecked();
        }

        private bool? untestedTaken, saverTaken;
        private string[] introsTaken;

        // One intro on or off in Toggles > Intros; with more than one on she picks one at random.
        internal bool IntroOn(string name) { return Array.IndexOf(SysLines.Intros(), name) >= 0; }

        internal void SetIntrosAllowed(bool on)
        {
            preferences.IntrosAllOff = !on;
            SavePreferences();
            Brain.Log("intro", -1, "Intros switched " + (on ? "on; each by its own switch." : "off; each keeps its switch."));
            if (mainframe != null) mainframe.Sync();
        }

        internal void SetIntro(string name, bool on)
        {
            List<string> picked = new List<string>(SysLines.Intros());
            picked.Remove(name);
            if (on) picked.Add(name);
            preferences.Intros = string.Join(",", picked.ToArray());
            SavePreferences();
            // A door's intro switched on here turns its mod on, so it joins the list a start picks from; only by their hand.
            if (on && (name == "door" || name == "door2") && ModList != null)
            {
                Mod door = ModList.All.Find(mod => mod.File.Id == (name == "door" ? Mods.DoorId : Mods.FrontId));
                if (door != null && !door.On) ModList.Set(door, true, null);
            }
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

        // Its looks switched on in Toggles, a switch each: what SysLines.Saver reads. A settings file from before had one
        // switch and a style, which become that one look on.
        internal string[] SaverLooksOn
        {
            get
            {
                List<string> on = new List<string>();
                foreach (string one in (preferences.SaverLooks ?? (preferences.ScreensaverOn ? SaverStyle : "")).Split(','))
                    foreach (string style in SaverLook.Styles)
                        if (string.Equals(style, one.Trim(), StringComparison.OrdinalIgnoreCase) && !on.Contains(style)) on.Add(style);
                return on.ToArray();
            }
        }

        internal bool SaverLookOn(string look) { return Array.Exists(SaverLooksOn, one => string.Equals(one, look, StringComparison.OrdinalIgnoreCase)); }

        // Off by the screensavers' own switch, none runs; her looks keep their own switches as they are.
        internal bool ScreensaverOn { get { return !preferences.SaverAllOff && SaverLooksOn.Length > 0; } }

        internal bool SaversAllowed { get { return !preferences.SaverAllOff; } }

        internal void SetSaversAllowed(bool on)
        {
            preferences.SaverAllOff = !on;
            SavePreferences();
            Brain.Log("screensaver", -1, "Screensavers switched " + (on ? "on; each look by its own switch." : "off; the looks keep their switches."));
            if (!on && SaverShowing && !bullySaver) SaverOff();
            if (mainframe != null) mainframe.Sync();
        }

        internal void SetSaverLook(string look, bool on)
        {
            List<string> now = new List<string>(SaverLooksOn);
            now.RemoveAll(one => string.Equals(one, look, StringComparison.OrdinalIgnoreCase));
            if (on && Array.Exists(SaverLook.Styles, one => string.Equals(one, look, StringComparison.OrdinalIgnoreCase))) now.Add(look.Trim().ToLowerInvariant());
            preferences.SaverLooks = string.Join(",", now.ToArray());
            SavePreferences();
            Brain.Log("screensaver", -1, look + " switched " + (on ? "on" : "off") + ", saved; on now: " + (now.Count == 0 ? "none" : string.Join(", ", now.ToArray())) + ".");
            if (now.Count == 0 && SaverShowing && !bullySaver) SaverOff();
            if (mainframe != null) mainframe.Sync();
        }

        // On is the look last picked, off is every look off: for her settings tool and an old sys.md's one switch.
        // On or off as the screensavers' own switch, so her looks' switches stay as they were; on with none on turns the
        // last one picked on.
        internal void SetScreensaver(bool on)
        {
            if (on == ScreensaverOn) return;
            if (on != SaversAllowed) SetSaversAllowed(on);
            if (on && SaverLooksOn.Length == 0) SetSaverLook(SaverStyle, true);
        }

        // A look for this showing: the one on, or a random one of those on; null when none is.
        private readonly Random saverPick = new Random();

        internal string SaverPick()
        {
            string[] on = SaverLooksOn;
            if (on.Length == 0) return null;
            lock (saverPick) return on[saverPick.Next(on.Length)];
        }

        private Dictionary<string, string> saverLooksTaken;

        // The screensaver's look as its own sliders in Toggles name it, step by step, each with the value it stands for; the
        // mod adds the same. A sys.md from before is carried to the nearest step once.
        internal const string SaverModId = "capri-screensaver";
        internal static readonly string[] SaverSizes = { "Small", "Normal", "Large", "2x", "3x" }, SaverSpeeds = { "Slow", "Normal", "Fast" }, SaverFlashes = { "Off", "Gentle", "On" };
        private static readonly double[] SizeValues = { 0.72, 1, 1.5, 2, 3 }, SpeedValues = { 0.45, 1, 2.4 };

        internal static string[] Percents(int most) { List<string> made = new List<string>(); for (int one = 0; one <= most; one += 10) made.Add(one + "%"); return made.ToArray(); }

        // Each look's own row in Toggles > Screensaver, as the mod names them.
        internal static readonly string[] SaverLookRows = { "Terminals", "Hexes", "Code", "Rain" };

        // Size and speed were one for every look, so each look takes them; flashing was only ever the hexes'; see through is
        // the whole group's. Which screens and the volume are gone: it covers every screen and is silent.
        private void PutSaverLooks(Dictionary<string, string> said)
        {
            string one;
            if (said.TryGetValue(SysLines.SaverSizeKey, out one)) foreach (string look in SaverLookRows) SetModToggle(SaverModId, look, "Size", SaverSizes[Nearest(one, SizeValues, "small", "large")]);
            if (said.TryGetValue(SysLines.SaverSpeedKey, out one)) foreach (string look in SaverLookRows) SetModToggle(SaverModId, look, "Speed", SaverSpeeds[Nearest(one, SpeedValues, "slow", "fast")]);
            if (said.TryGetValue(SysLines.SaverThroughKey, out one)) SetModToggle(SaverModId, null, "See through", Percents(90)[Math.Min(9, Tenths(one))]);
            if (said.TryGetValue(SysLines.SaverFlashKey, out one))
            {
                string flash = one.Trim().ToLowerInvariant();
                SetModToggle(SaverModId, "Hexes", "Flashing", flash == "off" || flash == "0" ? "Off" : flash == "gentle" || flash == "slow" ? "Gentle" : "On");
            }
        }

        // A word, or a number to the nearest of the values; anything else is the middle step.
        private static int Nearest(string said, double[] values, string low, string high)
        {
            string one = (said ?? "").Trim();
            if (one.Equals(low, StringComparison.OrdinalIgnoreCase)) return 0;
            if (one.Equals(high, StringComparison.OrdinalIgnoreCase)) return Array.IndexOf(values, low == "small" ? 1.5 : 2.4);
            double made;
            if (!double.TryParse(one, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out made)) return Array.IndexOf(values, 1.0);
            int best = 0;
            for (int at = 1; at < values.Length; at++) if (Math.Abs(values[at] - made) < Math.Abs(values[best] - made)) best = at;
            return best;
        }

        private static int Tenths(string said)
        {
            double made;
            if (!double.TryParse((said ?? "").Trim().TrimEnd('%'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out made)) return 0;
            return (int)Math.Round(Math.Max(0, Math.Min(100, made)) / 10);
        }

        // An old sys.md's screensaver look carried once to the mod's sliders, nothing of it left in the file she writes; the
        // mod's fold in Toggles; each intro switched on counting once in the pick. Her settings are put back after.
#if CHECKS
        private void SmokeModToggles(List<string> checks, string output)
        {
            string sys = Path.Combine(Path.GetTempPath(), "capri-looks-" + Guid.NewGuid().ToString("N") + ".md");
            List<string> togglesWas = preferences.ModToggles == null ? null : new List<string>(preferences.ModToggles);
            string introsWas = preferences.Intros;
            List<string> wrong = new List<string>();
            try
            {
                File.WriteAllText(sys, SysLines.SaverHeading + "\r\n\r\n>! Shows while the PC is idle, when Toggles > Screensaver is on. Space or Esc ends it.\r\n"
                    + ">! size:        small, normal, large, or a number. 1 is normal.\r\n>! volume:      0 to 100.\r\nscreensaver size:    large\r\n"
                    + "screensaver speed:   2\r\nscreensaver see through: 35\r\nscreensaver screens: main\r\nscreensaver flashing: gentle\r\n"
                    + "screensaver sound:   sounds/alarm.wav\r\nscreensaver volume:  55\r\n");
                Dictionary<string, string> taken = SysLines.TakeSaverLooks(sys), again = SysLines.TakeSaverLooks(sys);
                string left = File.ReadAllText(sys);
                if (taken == null || taken.Count != 6 || again != null) wrong.Add("taken " + (taken == null ? "none" : taken.Count.ToString()) + ", again " + (again != null));
                foreach (string gone in new[] { "screensaver size", "speed", "see through", "screens:", "flashing", "volume", ">! size:", "Shows while" })
                    if (left.IndexOf(gone, StringComparison.OrdinalIgnoreCase) >= 0) wrong.Add("left " + gone);
                if (left.Contains("screensaver sound") || left.Contains(SysLines.SaverHeading)) wrong.Add("its sound line or heading stayed");
                preferences.ModToggles = new List<string>();
                if (taken != null) PutSaverLooks(taken);
                // Size and speed to every look, flashing to the hexes, see through to the group; screens and volume nowhere.
                string[,] want = { { "Terminals", "Size", "Large" }, { "Rain", "Size", "Large" }, { "Code", "Speed", "Fast" }, { "", "See through", "40%" }, { "Hexes", "Flashing", "Gentle" }, { "", "Every screen", null }, { "", "Volume", null } };
                for (int i = 0; i < want.GetLength(0); i++)
                {
                    string item = want[i, 0].Length == 0 ? null : want[i, 0];
                    if (ModToggle(SaverModId, item, want[i, 1]) != want[i, 2]) wrong.Add(want[i, 0] + " " + want[i, 1] + " carried as " + ModToggle(SaverModId, item, want[i, 1]));
                }
                List<string> saved = LoadPreferences().ModToggles;
                if (saved == null || !saved.Contains(SaverModId + "\tTerminals\tSize\tLarge")) wrong.Add("not saved in her settings");
                if (SysLines.Text.IndexOf(SysLines.SaverHeading, StringComparison.Ordinal) >= 0) wrong.Add("the sys.md she writes still has a screensaver section");
                // One on is always that one, several on each a share, none picked no share (no forced intros).
                NamedIntros three = new NamedIntros();
                preferences.Intros = "hijack";
                int one = Mods.IntroShares(three);
                preferences.Intros = "hijack,wake,door";
                int two = Mods.IntroShares(three);
                preferences.Intros = "door";
                int none = Mods.IntroShares(three);
                if (one != 1 || two != 2 || none != 0) wrong.Add("intro shares " + one + "/" + two + "/" + none);
                // Its fold: the screensaver mod's look as sliders in Toggles, a step pressed there reaching the mod and her settings.
                preferences.ModToggles = new List<string>();
                ICapriScreensaver saver = ModList == null ? null : ModList.Screensaver;
                if (saver == null) checks.Add("LIMIT the screensaver mod isn't running here, so its sliders in Toggles weren't checked");
                else
                {
                    // The group's own settings first (After, See through), then the looks, each with its own behind its ">".
                    mainframe.OpenToggles();
                    mainframe.UnfoldToggles("Screensaver");
                    string list = mainframe.TogglesText ?? "";
                    // A picture of the fold with the mod's own sliders in it, for an eye on how they line up.
                    using (System.Drawing.Bitmap shot = mainframe.PictureToggles()) shot.Save(Path.Combine(output, "toggles-screensaver.png"), System.Drawing.Imaging.ImageFormat.Png);
                    // The group's own sliders sit behind the screensaver's > (After, then See through), not in the list.
                    mainframe.OpenToggleSettings("Screensaver");
                    string group = mainframe.TogglesText ?? "";
                    int side = group.IndexOf("{Screensaver}", StringComparison.Ordinal);
                    int after = side < 0 ? -1 : group.IndexOf("After:", side, StringComparison.Ordinal), through = side < 0 ? -1 : group.IndexOf("See through:", side, StringComparison.Ordinal);
                    bool ordered = after > side && through > after && list.IndexOf("After:", StringComparison.Ordinal) < 0 && list.Contains("Terminals ") && !list.Contains("Every screen") && !list.Contains("Size:");
                    if (!ordered) list += " / its > side " + group;
                    mainframe.PickToggleRow("Rain");
                    string rain = mainframe.TogglesText ?? "";
                    mainframe.PressToggle("Size: Large");
                    System.Reflection.FieldInfo sizes = saver.GetType().GetField("sizes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    Dictionary<string, float> held = sizes == null ? null : sizes.GetValue(saver) as Dictionary<string, float>;
                    float now = held != null && held.ContainsKey("rain") ? held["rain"] : -1;
                    bool nameKept = mainframe.ClickToggleName("Hexes");
                    string hexes = mainframe.TogglesText ?? "";
                    bool own = rain.Contains("{Rain}") && rain.Contains("Size: Normal") && rain.Contains("Speed: Normal") && rain.Contains("Density: 100%") && !rain.Contains("Flashing")
                        && hexes.Contains("{Hexes}") && hexes.Contains("Flashing: On") && !hexes.Contains("Density");
                    if (!ordered || !own || !nameKept || ModToggle(SaverModId, "Rain", "Size") != "Large" || Math.Abs(now - 1.5f) > 0.01)
                        wrong.Add("its group showed " + list + " / rain " + rain + " / hexes by its name " + hexes + (nameKept ? "" : ", the name click turned Hexes' switch") + ", Rain's size saved " + ModToggle(SaverModId, "Rain", "Size") + ", the mod's " + now);
                    mainframe.PickToggleRow("Rain");
                    mainframe.PressToggle("Size: Normal");
                    mainframe.CloseToggles();
                }
                // Toggles > Pranks: a switch per prank, off to start; twitch.md's old list carried in once and taken out.
                string twitchWas = twitchPath, pranksWas = preferences.PranksOn;
                string twitch = Path.Combine(Path.GetTempPath(), "capri-twitch-" + Guid.NewGuid().ToString("N") + ".md");
                try
                {
                    File.WriteAllText(twitch, "# Capri on Twitch\r\n\r\n## Streamer bully\r\n\r\n>! What chat can set off.\r\n\r\ncrash, alert\r\n\r\n## Connection\r\n\r\nchannel:\r\n");
                    twitchPath = twitch;
                    preferences.PranksOn = null;
                    CarryBully();
                    string kept = File.ReadAllText(twitch);
                    bool carried = PrankOn("crash") && PrankOn("alert") && !PrankOn("dance") && !kept.Contains("Streamer bully") && !kept.Contains("crash, alert") && kept.Contains("## Connection") && TwitchRules.TakeBully(twitch) == null;
                    mainframe.OpenToggles();
                    mainframe.UnfoldToggles("Pranks");
                    string shown = mainframe.TogglesText ?? "";
                    mainframe.PressToggle("Dance");
                    bool pressed = PrankOn("dance") && (mainframe.TogglesText ?? "").Contains("Dance on");
                    mainframe.CloseToggles();
                    preferences.PranksOn = "";
                    bool fresh = !PrankOn("crash") && TwitchRules.Default.IndexOf("Streamer bully", StringComparison.OrdinalIgnoreCase) < 0;
                    if (!carried || !pressed || !fresh || !shown.Contains("[PRANKS]") || !shown.Contains("Crash on") || !shown.Contains("Metal pipe off"))
                        wrong.Add("pranks: carried " + carried + ", pressed " + pressed + ", fresh " + fresh + ", shown " + shown);
                }
                finally
                {
                    twitchPath = twitchWas;
                    twitchReadAt = DateTime.MinValue;
                    preferences.PranksOn = pranksWas;
                    try { File.Delete(twitch); } catch (IOException) { }
                }
                // Her red alert is her own copy of the hexes: it comes up and goes with nothing asked of the screensaver mod.
                if (!SaverShowing)
                {
                    string shown = AlertTool("show");
                    bool up = alertWindows.Count > 0;
                    AlertTool("stop");
                    if (shown != "Red alert." || !up || alertWindows.Count > 0) wrong.Add("her red alert said " + shown + ", up " + up + ", left " + alertWindows.Count);
                    // Its own alarm, laid with her prank sounds, with no sys.md line and whether the screensaver mod runs or not.
                    if (alarmAsked == null || !File.Exists(alarmAsked) || new FileInfo(alarmAsked).Length != 1713308) wrong.Add("her red alert's own alarm wasn't there to play: " + alarmAsked);
                }
                if (wrong.Count > 0) throw new Exception("A mod's switches and sliders went wrong: " + string.Join(" | ", wrong.ToArray()));
                checks.Add("PASS a mod's settings in Toggles, kept in her settings: one for the whole group sits behind the group's own >, one for an item only in that item's side area behind its >; the screensaver's group is After and See through then its looks, Rain with Size, Speed and Density, Hexes with Flashing; an old sys.md's size and speed go to every look, flashing to Hexes, see through to the group, to the nearest step, and the sys.md she writes has none; each intro switched on counts once in the start's pick; her red alert draws its own hexes and plays its own alarm, needing nothing of the screensaver mod or sys.md; Toggles > Pranks has a switch per prank, off to start, deciding what her mood and Twitch chat may set off, with twitch.md's old list carried in once and taken out of the file");
            }
            finally
            {
                preferences.ModToggles = togglesWas;
                preferences.Intros = introsWas;
                SavePreferences();
                try { File.Delete(sys); } catch (IOException) { }
            }
        }
#endif

        private sealed class NamedIntros : ICapriMod, ICapriIntroNames
        {
            public string[] Intros { get { return new[] { "Hijack", "Shatter", "Wake" }; } }
            public void Start(ICapriHost capri) { }
            public bool Heard(string words) { return false; }
            public void Stop() { }
        }

        // A mod's own switch or slider as last picked, or null; set as it changes. item is the row it belongs to, or null for
        // one of the mod's own rows or the whole group.
        internal string ModToggle(string id, string item, string name)
        {
            if (preferences == null || preferences.ModToggles == null) return null;
            string head = id + "\t" + (item ?? "") + "\t" + name + "\t";
            foreach (string one in preferences.ModToggles) if (one != null && one.StartsWith(head, StringComparison.OrdinalIgnoreCase)) return one.Substring(head.Length);
            return null;
        }

        internal void SetModToggle(string id, string item, string name, string value)
        {
            if (preferences == null) return;
            if (preferences.ModToggles == null) preferences.ModToggles = new List<string>();
            string head = id + "\t" + (item ?? "") + "\t" + name + "\t";
            preferences.ModToggles.RemoveAll(one => one == null || one.StartsWith(head, StringComparison.OrdinalIgnoreCase));
            preferences.ModToggles.Add(head + value);
            SavePreferences();
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

#if CHECKS
        // The pick as her saved settings have it, read back from disk.
        internal bool ModsPluginsSaved() { return LoadPreferences().ModsShowPlugins; }
#endif

        // Her own topics per personality, as her settings keep them.
        // Her picks rolled for the personality she is in when it has none yet: an updated install the same as a new one.
        private void RollHerTopics()
        {
            string now = Personalities.Under ?? Personalities.Chosen;
            if (string.IsNullOrEmpty(now) || now == Personalities.Dynamic) return;
            if (OwnSet.Shown) OwnSet.Hers(now);
            if (HerTopicsFor(now) != null) return;
            Topics.Hers(now, null);
        }

        private string[] HerTopicsFor(string personality)
        {
            foreach (string line in (preferences.HerTopics ?? "").Split('\n'))
            {
                int tab = line.IndexOf('\t');
                if (tab > 0 && string.Equals(line.Substring(0, tab), personality, StringComparison.OrdinalIgnoreCase))
                    return line.Substring(tab + 1).Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            }
            return null;
        }

        private void KeepHerTopics(string personality, string[] picked)
        {
            List<string> lines = new List<string>();
            foreach (string line in (preferences.HerTopics ?? "").Split('\n'))
            {
                int tab = line.IndexOf('\t');
                if (tab > 0 && !string.Equals(line.Substring(0, tab), personality, StringComparison.OrdinalIgnoreCase)) lines.Add(line);
            }
            lines.Add(personality + "\t" + string.Join("|", picked));
            preferences.HerTopics = string.Join("\n", lines.ToArray());
            SavePreferences();
        }

        // Mods > INSTALLED's pick: plug-ins, or mods.
        internal bool ModsPlugins
        {
            get { return preferences.ModsShowPlugins; }
            set { if (preferences.ModsShowPlugins == value) return; preferences.ModsShowPlugins = value; SavePreferences(); }
        }

        // Her mods in settings.json: switches and this side's overrides go out and back, a never-approved mod or
        // starter stays off, nothing sealed goes in, and an update takes the block only when signed here; their own
        // Import takes it from anywhere.
#if CHECKS
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
#endif

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
                // A first start has nothing of theirs to put back, whatever it wrote.
                if (replaced == 0 || !Renew.Changing || string.IsNullOrEmpty(SettingsFile.Folder)) return;
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

#if CHECKS
        // A first start puts nothing back and says nothing of an update; an update, with or without a settings.json from
        // before, still gathers what was theirs.
        internal void CheckFreshNoUpdateLine(List<string> checks, string output)
        {
            string root = Path.Combine(output, "fresh-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            string settings = Path.Combine(root, Personalities.SettingsName), side = Path.Combine(settings, Personalities.PlainName);
            string folderWas = SettingsFile.Folder, gatheredWas = gatheredSettings, before = Path.Combine(settings, SettingsFile.BeforeUpdate);
            List<string> wrong = new List<string>();
            try
            {
                // A first start: only her day log there before she writes anything.
                Directory.CreateDirectory(Path.Combine(settings, Logs.FolderName));
                File.WriteAllText(Path.Combine(settings, Logs.FolderName, "today.txt"), "a log line");
                bool held = Renew.Held(settings);
                Renew.Begin(settings, "9.9.1", held);
                if (held || Renew.Changing) wrong.Add("a first start read as an update");
                // Then what a first start writes, her settings.json among it.
                Directory.CreateDirectory(side);
                File.WriteAllText(Path.Combine(side, "lines.md"), "# Her lines\r\n\r\n## settings\r\nrecent hours: 24\r\n");
                File.WriteAllText(Path.Combine(settings, SettingsFile.FileName), "{}");
                SettingsFile.Folder = settings;
                gatheredSettings = null;
                GatherSettings(root);
                OfferSettingsBack(5);
                if (gatheredSettings != null || File.Exists(before)) wrong.Add("a first start kept settings to put back");
                // An update from a version without settings.json: her files were there first, so they are gathered.
                File.Delete(Path.Combine(settings, SettingsFile.FileName));
                Renew.Begin(settings, "9.9.2", Renew.Held(settings));
                GatherSettings(root);
                if (!Renew.Changing || gatheredSettings == null || !File.Exists(before)) wrong.Add("an update from a version without settings.json gathered nothing");
                // An update with a record of an older version.
                File.WriteAllText(Path.Combine(settings, Renew.StampName), "shared\t9.9.1\r\n");
                Renew.Begin(settings, "9.9.2", Renew.Held(settings));
                if (!Renew.Changing) wrong.Add("an update with an older record read as a first start");
                if (wrong.Count > 0) throw new Exception("Settings after a first start or an update went wrong: " + string.Join("; ", wrong.ToArray()));
                checks.Add("PASS a first start (only her day log there) says nothing of an update and keeps no settings to put back; an update from a version without settings.json still gathers theirs, and one with an older record still counts as an update");
            }
            finally
            {
                // As the smoke start left it: no record, nothing due.
                Renew.Begin(null, Built, false);
                SettingsFile.Folder = folderWas;
                gatheredSettings = gatheredWas;
                try { Directory.Delete(root, true); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
        }
#endif

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
                        ? (set == 0 ? "I'm on a new version, and your settings are all still here." : "I'm on a new version, and I've put your settings back: " + set + (set == 1 ? " setting." : " settings."))
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
                // The first twenty, and how many more; every one is in her log.
                if (failed == null) for (int at = 0; at < missing.Count && at < 20; at++) told.Append("\r\n  " + missing[at]);
                if (failed == null && missing.Count > 20) told.Append("\r\n  and " + (missing.Count - 20) + " more, all in her log");
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
