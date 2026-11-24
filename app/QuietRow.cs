using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Her quiet, two ways: Mute her lasts until turned off; the Quiet timer, her Quiet key and an ask last a random time
    // between the timer's min and max hours, unless they name a time. Each switch shows only its own.
    internal sealed partial class Mainframe
    {
        private Toggle quietSwitch, quietCalls, quietVideos, focusSwitch, focusBox, quietBox;
        private Slider quietMin, quietMax, whileWorking;
        private Readout quietMinName, quietMaxName, whileWorkingName;
        private Entry callApps;
        private Key focusKey;

        // Min is 1 to 8 hours; max is 2 to 9 hours, then until she is told otherwise, and never under min + 1.
        internal static readonly string[] QuietMinNames = Hours(1, Companion.QuietMostHours - 1, null);
        internal static readonly string[] QuietMaxNames = Hours(2, Companion.QuietMostHours, QuietForever);
        internal const string QuietForever = "Until I turn it off";
        internal const string QuietTimer = "Quiet timer";
        // While working's steps, x2 to x10, then the infinity sign: her timers stop whatever is in front.
        internal static readonly string[] WhileWorkingNames = Times(Working.Least, Working.Most);

        private static string[] Times(int from, int to)
        {
            List<string> names = new List<string>();
            for (int times = from; times <= to; times++) names.Add("x" + times);
            names.Add("\u221E");
            return names.ToArray();
        }

        private static string[] Hours(int from, int to, string last)
        {
            List<string> names = new List<string>();
            for (int hours = from; hours <= to; hours++) names.Add(hours + " h");
            if (last != null) names.Add(last);
            return names.ToArray();
        }

        private static int MaxHoursAt(int at) { return at >= QuietMaxNames.Length - 1 ? 0 : at + 2; }

        // Under Hologram, built at CAPRI's own top before the column moves down: the Quiet timer, Focus mode, then the key to
        // their settings window. Mute her joins over the Quiet timer from GAMES when the page is regrouped.
        private void BuildQuiet(int c, int top)
        {
            quietSwitch = Switch("Quiet timer", c, top, delegate { if (!syncing) capri.SetQuiet(quietSwitch.Checked); });
            quietSwitch.AccessibleName = "Quiet timer. She stops talking on her own for a while and only answers when you talk to her, the same as her Quiet key";
            focusSwitch = Switch("Focus mode", c, top + Row, delegate { if (!syncing) capri.SetFocus(focusSwitch.Checked); });
            focusSwitch.AccessibleName = "Focus mode. While an app you added is in front, her timers and chatter slow by While working";
            focusKey = Make(FocusLabel, 28 + c, top + Row * 2 + 3, 328, 22, delegate { OpenWorkApps(); });
            focusKey.AccessibleName = FocusLabel + ": focus and quiet, each with its own switch and settings, and which apps count as work";
        }

        private void UseCallApps()
        {
            capri.SetCallApps(callApps.Box.Text);
            callApps.Box.Text = capri.CallAppsSaid;
        }

        // Focus's controls live in the Focus / Quiet settings window; one is made, unshown, when they're asked for before it opens.
        private bool FocusBuilt { get { return whileWorking != null && !whileWorking.IsDisposed; } }

        private void FocusMade() { if (!FocusBuilt) WorkSheetMade(); }

        internal void TypeCallApps(string typed) { FocusMade(); callApps.Box.Text = typed; UseCallApps(); }

        // Her quiet as it is now: each switch its own, when the timer ends, its hours, the two of her own and While working.
        private void SyncQuiet()
        {
            if (quietSwitch == null) return;
            QuietShow();
            if (!FocusBuilt) return;
            bool was = syncing;
            syncing = true;
            try { SyncFocus(); } finally { syncing = was; }
        }

        private void SyncFocus()
        {
            whileWorking.Value = capri.WhileWorking - Working.Least;
            whileWorkingName.Text = WhileWorkingNames[whileWorking.Value];
            quietMin.Value = capri.QuietMinHours - 1;
            quietMinName.Text = QuietMinNames[quietMin.Value];
            int max = capri.QuietMaxHours;
            quietMax.Value = max <= 0 ? QuietMaxNames.Length - 1 : max - 2;
            quietMaxName.Text = QuietMaxNames[quietMax.Value];
            quietCalls.Checked = capri.QuietOnCalls;
            quietVideos.Checked = capri.QuietOnVideos;
            if (!callApps.Box.Focused) callApps.Box.Text = capri.CallAppsSaid;
        }

        private int quietShownAt;
        // The switch's words as she wrote them, before her language's table, so a translated one isn't rewritten each second.
        private string quietSaid = QuietTimer;

        // Both switches follow her quiet however it changed, and the time the timer ends is kept on its switch.
        internal void QuietTick()
        {
            if (quietSwitch == null) return;
            bool timed = capri.QuietTimed, muted = capri.MuteOn;
            if (quietSwitch.Checked == timed && gameMute.Checked == muted && focusSwitch.Checked == capri.FocusOn && (quietSaid != QuietTimer) == timed && unchecked(Environment.TickCount - quietShownAt) < 1000) return;
            QuietShow();
        }

        private void QuietShow()
        {
            quietShownAt = Environment.TickCount;
            bool was = syncing;
            syncing = true;
            try
            {
                quietSwitch.Checked = capri.QuietTimed;
                gameMute.Checked = capri.MuteOn;
                focusSwitch.Checked = capri.FocusOn;
                if (FocusBuilt) { quietBox.Checked = capri.QuietTimed; focusBox.Checked = capri.FocusOn; }
            }
            finally { syncing = was; }
            string said = capri.QuietLeft;
            if (quietSaid != said) quietSwitch.Text = quietSaid = said;
        }

        internal bool QuietShown { get { return quietSwitch != null && quietSwitch.Checked; } }
        internal bool MuteShown { get { return gameMute != null && gameMute.Checked; } }
        internal string QuietSaid { get { return quietSaid; } }
        internal int QuietMaxShown { get { FocusMade(); return MaxHoursAt(quietMax.Value); } }
        internal int WhileWorkingShown { get { FocusMade(); return whileWorking.Value + Working.Least; } }

        // Their arrow keys on a slider, then let go, for her checks.
        internal void SlideQuiet(bool max, int steps)
        {
            FocusMade();
            Press(max ? quietMax : quietMin, steps);
            capri.SetQuietHours(quietMin.Value + 1, MaxHoursAt(quietMax.Value));
        }
    }

    internal sealed partial class Companion
    {
        // When her quiet ends, or -1 while it lasts until she is told otherwise. Only while shushed.
        private double quietUntil = -1;
        // The quiet is Mute her's: until turned off, never timed. Only while shushed.
        private bool muted;

        internal bool QuietOn { get { return shushed || Focused; } }
        internal bool MuteOn { get { return shushed && muted; } }
        // The Quiet timer's own: her key, an ask or the switch, not Mute her.
        internal bool QuietTimed { get { return QuietOn && !MuteOn; } }

        internal const int QuietMostHours = 9;

        // Her Quiet timer's hours and quiet on calls and in videos, from her settings; max 0 is until turned off.
        internal int QuietMinHours { get { int min = preferences.QuietMin; return min <= 0 ? 1 : Math.Max(1, Math.Min(QuietMostHours - 1, min)); } }

        internal int QuietMaxHours
        {
            get
            {
                if (preferences.QuietForever) return 0;
                int max = preferences.QuietMax <= 0 ? 3 : preferences.QuietMax;
                return Math.Max(QuietMinHours + 1, Math.Min(QuietMostHours, max));
            }
        }

        internal bool QuietOnCalls { get { return !preferences.NoQuietCalls; } }
        internal bool QuietOnVideos { get { return !preferences.NoQuietVideos; } }

        // The call windows as saved, or the stock list; and as titles to look for.
        internal string CallAppsSaid { get { return string.IsNullOrEmpty(preferences.CallApps) ? SysLines.CallAppsStock : preferences.CallApps; } }

        internal string[] CallWindows { get { return SysLines.Titles(CallAppsSaid); } }

        // Blank or the stock list keeps the stock list, so a later build's additions still reach it.
        internal void SetCallApps(string said)
        {
            string trimmed = string.Join(", ", SysLines.Titles(said));
            preferences.CallApps = trimmed.Length == 0 || trimmed == string.Join(", ", SysLines.Titles(SysLines.CallAppsStock)) ? null : trimmed;
            SavePreferences();
        }

        // While working, x2 to x10, or Working.Never.
        internal int WhileWorking { get { int times = preferences.WhileWorking; return times <= 0 ? Working.Stock : Math.Max(Working.Least, Math.Min(Working.Never, times)); } }

        internal void SetWhileWorking(int times)
        {
            preferences.WhileWorking = Math.Max(Working.Least, Math.Min(Working.Never, times));
            Working.Times = WhileWorking;
            SavePreferences();
        }

        // While working's times, work apps and holds from her settings.
        internal void ApplyWorking()
        {
            // Her own work apps were once added for them; once, those go, and only what they added stays.
            if (!preferences.WorkAppsTheirs)
            {
                if (preferences.WorkApps != null)
                {
                    List<string> theirs = new List<string>(preferences.WorkApps.Split('\n'));
                    theirs.RemoveAll(one => Array.Exists(Working.StockNames, stock => string.Equals(stock, Working.Plain(one), StringComparison.OrdinalIgnoreCase)));
                    preferences.WorkApps = theirs.Exists(one => one.Trim().Length > 0) ? string.Join("\n", theirs.ToArray()) : null;
                }
                preferences.WorkAppsTheirs = true;
                SavePreferences();
            }
            Working.On = !preferences.NoFocus;
            Working.Times = WhileWorking;
            Working.Apps = preferences.WorkApps == null ? null : preferences.WorkApps.Split('\n');
            Working.HoldsSaid = preferences.WorkHolds;
        }

        // The work apps as they are now, none kept as none.
        internal void SetWorkApps(string[] apps)
        {
            Working.Apps = apps;
            preferences.WorkApps = Working.Apps.Length == 0 ? null : string.Join("\n", Working.Apps);
            SavePreferences();
        }

        // Focus on or off, from its switch in CAPRI or its box in the window.
        internal bool FocusOn { get { return !preferences.NoFocus; } }

        internal void SetFocus(bool on)
        {
            if (preferences.NoFocus == on) Brain.Log("modes", -1, "Focus mode " + (on ? "on" : "off") + ", from its switch.");
            preferences.NoFocus = !on;
            Working.On = on;
            SavePreferences();
            if (mainframe != null) mainframe.QuietTick();
        }

        internal void SetWorkHold(string row, Working.Hold hold)
        {
            Working.SetHold(row, hold);
            SaveWorkHolds();
        }

        internal void SaveWorkHolds()
        {
            preferences.WorkHolds = Working.HoldsSaid;
            SavePreferences();
        }

        // A mod's row goes with the mod.
        internal void KeepWorkMods()
        {
            if (ModList == null) return;
            if (Working.KeepMods(id => ModList.All.Exists(mod => mod.File.Id == id))) SaveWorkHolds();
        }

        // "Quiet until 15:45", "Quiet until turned off", or "Quiet timer" while it isn't on.
        internal string QuietLeft
        {
            get
            {
                if (!QuietTimed) return Mainframe.QuietTimer;
                double ends = Focused && !shushed ? focusUntil : quietUntil;
                if (ends < 0 || ends > clock.Elapsed.TotalSeconds + 86400 * 30) return "Quiet until turned off";
                return "Quiet until " + DateTime.Now.AddSeconds(ends - clock.Elapsed.TotalSeconds).ToString("HH:mm");
            }
        }

        // How long a quiet lasts in seconds: the time they named, or a random time between the min and max hours; -1 is
        // until they turn it off.
        private double QuietSeconds(string heard)
        {
            double named = QuietNamed(heard);
            if (named > 0) return named;
            int min = QuietMinHours, max = QuietMaxHours;
            if (max <= 0) return -1;
            return (min + random.NextDouble() * (max - min)) * 3600;
        }

        private static readonly string[] QuietCounts = { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve" };

        // A time named in the ask: "for 30 minutes", "an hour", "two hours", "half an hour", "an hour and a half". 0 when none.
        internal static double QuietNamed(string heard)
        {
            string text = (heard ?? "").ToLowerInvariant();
            if (Regex.IsMatch(text, @"\bhalf an? hour\b|\bhalf hour\b") && !Regex.IsMatch(text, @"\band a half\b")) return 1800;
            Match said = Regex.Match(text, @"\b(\d+(?:[.,]\d+)?|an?|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|fifteen|twenty|thirty|forty|forty[- ]five|fifty|ninety)\s*(?:more\s+)?(hours?|hrs?|minutes?|mins?)\b(\s+and\s+a\s+half)?");
            if (!said.Success) return 0;
            string count = said.Groups[1].Value.Replace(',', '.');
            double many;
            int word = Array.IndexOf(QuietCounts, count);
            if (count == "a" || count == "an") many = 1;
            else if (word >= 0) many = word;
            else if (count == "fifteen") many = 15;
            else if (count == "twenty") many = 20;
            else if (count == "thirty") many = 30;
            else if (count == "forty") many = 40;
            else if (count.StartsWith("forty", StringComparison.Ordinal)) many = 45;
            else if (count == "fifty") many = 50;
            else if (count == "ninety") many = 90;
            else if (!double.TryParse(count, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out many)) return 0;
            if (said.Groups[3].Success) many += 0.5;
            double seconds = many * (said.Groups[2].Value.StartsWith("h", StringComparison.Ordinal) ? 3600 : 60);
            return Math.Max(60, Math.Min(86400, seconds));
        }

        // A quiet's length in words, for her brain: "An hour", "Two hours and 15 minutes", "30 minutes".
        private static string QuietSpan(double seconds)
        {
            if (seconds < 3600) return Math.Max(1, (int)Math.Round(seconds / 60)) + " minutes";
            int quarters = (int)Math.Round(seconds / 900), hours = quarters / 4, minutes = quarters % 4 * 15;
            string[] said = { "An hour", "Two hours", "Three hours", "Four hours", "Five hours", "Six hours", "Seven hours", "Eight hours", "Nine hours" };
            string whole = hours <= said.Length ? said[hours - 1] : hours + " hours";
            return minutes == 0 ? whole : whole + " and " + minutes + " minutes";
        }

        // The Quiet timer on or off from the Mainframe, the same as her Quiet key, without her saying so. On over Mute her
        // starts the timer from now.
        internal void SetQuiet(bool on)
        {
            if (on == QuietTimed) { if (mainframe != null) mainframe.QuietTick(); return; }
            if (on && MuteOn)
            {
                muted = false;
                double lasts = QuietSeconds(null);
                quietUntil = lasts > 0 ? clock.Elapsed.TotalSeconds + lasts : -1;
            }
            else if (on) ToggleShush(false);
            else
            {
                if (Focused) EndFocus(false);
                if (shushed) ToggleShush(false);
            }
            if (mainframe != null) mainframe.QuietTick();
        }

        // Mute her: quiet until turned off, with no timer. On over the Quiet timer takes its place.
        internal void SetMute(bool on)
        {
            if (on == MuteOn) { if (mainframe != null) mainframe.QuietTick(); return; }
            if (on)
            {
                if (Focused) EndFocus(false);
                if (!shushed) ToggleShush(false, true);
                else { muted = true; quietUntil = -1; }
                preferences.MuteKept = true;
                SavePreferences();
            }
            else if (shushed) ToggleShush(false);
            if (mainframe != null) mainframe.QuietTick();
        }

        internal void SetQuietHours(int min, int max)
        {
            min = Math.Max(1, Math.Min(QuietMostHours - 1, min));
            if (max > 0) max = Math.Max(min + 1, Math.Min(QuietMostHours, max));
            preferences.QuietMin = min;
            preferences.QuietForever = max <= 0;
            if (max > 0) preferences.QuietMax = max;
            SavePreferences();
        }

        internal void SetQuietCalls(bool on) { preferences.NoQuietCalls = !on; SavePreferences(); }

        internal void SetQuietVideos(bool on) { preferences.NoQuietVideos = !on; SavePreferences(); }

        // Her quiet's time is up: she is back, said only in her bubble.
        private void CheckQuiet(double now)
        {
            if (!shushed || quietUntil < 0 || now < quietUntil) return;
            ToggleShush(false);
            Remark("", "Quiet's over. I'm back.", true);
        }

        private void SmokeQuietRow(List<string> checks)
        {
            bool shushWas = shushed, mutedWas = muted;
            double untilWas = quietUntil;
            int minWas = preferences.QuietMin, maxWas = preferences.QuietMax, workingWas = preferences.WhileWorking;
            bool foreverWas = preferences.QuietForever, callsWas = preferences.NoQuietCalls, videosWas = preferences.NoQuietVideos;
            string appsWas = preferences.CallApps;
            long fedWas = preferences.FedAt;
            Func<Sight> aheadWas = Working.Ahead;
            string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "capri-quiet-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                if (shushed) ToggleShush(false);
                if (Focused) EndFocus(false);
                // The hours: max never under min + 1, and the slider's top is until turned off.
                SetQuietHours(2, 4);
                mainframe.Sync();
                mainframe.SlideQuiet(false, 3);
                bool pushed = QuietMinHours == 5 && QuietMaxHours == 6 && mainframe.QuietMaxShown == 6;
                mainframe.SlideQuiet(true, -3);
                bool floor = QuietMaxHours == 6;
                mainframe.SlideQuiet(true, 20);
                bool forever = QuietMaxHours == 0;
                if (!pushed || !floor || !forever)
                    throw new Exception("Quiet's sliders went wrong: min pushes max " + pushed + ", max stops at min + 1 " + floor + ", the top is until turned off " + forever + " (" + QuietMinHours + " to " + QuietMaxHours + ")");

                // The Quiet timer is timed and shows only on its own switch; the key and an ask are the same timer.
                SetQuietHours(1, 3);
                SetQuiet(true);
                double length = quietUntil - clock.Elapsed.TotalSeconds;
                bool timed = QuietTimed && !MuteOn && length >= 3600 - 5 && length <= 3 * 3600 + 5 && mainframe.QuietShown && !mainframe.MuteShown
                    && mainframe.QuietSaid.StartsWith("Quiet until ", StringComparison.Ordinal) && mainframe.QuietSaid != "Quiet until turned off";
                ToggleShush(false);
                bool keyOff = !QuietOn && !mainframe.QuietShown && mainframe.QuietSaid == Mainframe.QuietTimer;
                SetQuietHours(1, 0);
                ToggleShush(false);
                bool untilOff = shushed && quietUntil < 0 && mainframe.QuietShown && !mainframe.MuteShown && mainframe.QuietSaid == "Quiet until turned off";
                SetQuiet(false);
                lock (shortcutGate) heardLast = "I need some quiet";
                SetSetting("quiet", true);
                bool asked = Focused && mainframe.QuietShown && !mainframe.MuteShown && mainframe.QuietSaid == "Quiet until turned off";
                SetQuiet(false);
                SetQuietHours(1, 3);
                ToggleShush(false);
                quietUntil = clock.Elapsed.TotalSeconds - 1;
                CheckQuiet(clock.Elapsed.TotalSeconds);
                bool ended = !shushed && !QuietOn && !mainframe.QuietShown;
                if (!timed || !keyOff || !untilOff || !asked || !ended)
                    throw new Exception("The Quiet timer went wrong: timed " + timed + ", the key turning it off " + keyOff + ", until turned off " + untilOff + ", an ask showing on the switch " + asked + ", ended on time " + ended + " (" + mainframe.QuietSaid + ")");

                // Mute her is untimed and shows only on its own switch; the timer over it starts from now, and the key ends it.
                SetMute(true);
                CheckQuiet(clock.Elapsed.TotalSeconds + 86400 * 7);
                bool muteHolds = MuteOn && QuietOn && quietUntil < 0 && mainframe.MuteShown && !mainframe.QuietShown && mainframe.QuietSaid == Mainframe.QuietTimer;
                SetQuiet(true);
                double timerLength = quietUntil - clock.Elapsed.TotalSeconds;
                bool timerOver = QuietTimed && !MuteOn && timerLength >= 3600 - 5 && mainframe.QuietShown && !mainframe.MuteShown;
                SetMute(true);
                bool muteOver = MuteOn && quietUntil < 0 && mainframe.MuteShown && !mainframe.QuietShown;
                ToggleShush(false);
                bool keyEnds = !QuietOn && !mainframe.MuteShown && !mainframe.QuietShown;
                SetMute(true);
                SetMute(false);
                bool muteOff = !QuietOn && !mainframe.MuteShown;
                if (!muteHolds || !timerOver || !muteOver || !keyEnds || !muteOff)
                    throw new Exception("Mute her went wrong: holds with no timer " + muteHolds + ", the timer over it " + timerOver + ", Mute her over the timer " + muteOver + ", the key ends it " + keyEnds + ", off " + muteOff);

                // A time they name wins over the hours.
                string[,] named = { { "be quiet for 30 minutes", "1800" }, { "I need an hour of quiet", "3600" }, { "give me two hours", "7200" }, { "quiet for half an hour", "1800" }, { "an hour and a half of peace", "5400" }, { "I need some quiet", "0" } };
                for (int i = 0; i < named.GetLength(0); i++)
                    if (Math.Abs(QuietNamed(named[i, 0]) - double.Parse(named[i, 1], System.Globalization.CultureInfo.InvariantCulture)) > 1)
                        throw new Exception("\"" + named[i, 0] + "\" read as " + QuietNamed(named[i, 0]) + " seconds of quiet.");
                lock (shortcutGate) heardLast = "be quiet for 30 minutes";
                string said = SetSetting("quiet", true);
                bool thirty = Focused && Math.Abs(focusUntil - clock.Elapsed.TotalSeconds - 1800) < 5 && said != null && said.StartsWith("30 minutes of quiet", StringComparison.Ordinal);
                SetQuiet(false);
                if (!thirty) throw new Exception("\"be quiet for 30 minutes\" didn't last 30 minutes: " + said);
                SetQuietCalls(false);
                bool callsOff = !QuietOnCalls && LoadPreferences().NoQuietCalls;
                SetQuietCalls(true);
                if (!callsOff || !QuietOnCalls) throw new Exception("Quiet on calls didn't save to her settings.");
                mainframe.TypeCallApps("Zoom Meeting,  huddle , x");
                bool typed = CallAppsSaid == "zoom meeting, huddle" && CallWindows.Length == 2 && LoadPreferences().CallApps == "zoom meeting, huddle";
                mainframe.TypeCallApps("");
                if (!typed || CallAppsSaid != SysLines.CallAppsStock || preferences.CallApps != null)
                    throw new Exception("The call windows didn't save, or blank didn't put the stock list back: " + CallAppsSaid);
                checks.Add("PASS Mute her and the Quiet timer: Mute her lasts until turned off and shows only on its switch; the Quiet timer, her Quiet key and asks for quiet are one timer that shows only on its own switch with the time it ends, between min and max hours (max never under min + 1, until turned off at the top), a named time wins, and either one turned on over the other takes its place; quiet on calls and the call windows save to her settings, blank putting the stock windows back");

                // While working: a work app in front stretches being ignored, hunger and each chatter wait; nothing else does.
                Sight work = new Sight { Program = "code", Title = "notes.txt - Visual Studio Code" };
                Sight play = new Sight { Program = "notepad", Title = "notes.txt - Notepad" };
                // Only an app they added counts.
                Working.Apps = new[] { "code" };
                Working.Ahead = delegate { return work; };
                Working.Forget();
                SetWhileWorking(6);
                mainframe.Sync();
                double pop = PopWait(), wait = 300000 * PaceNow;
                bool stretched = Working.Now(SimTime.UtcNow) && Math.Abs(ChatterStretch - 6) < 1e-9 && Math.Abs(Probes.Stretch() - 6) < 1e-9 && mainframe.WhileWorkingShown == 6
                    && pop >= PopMin * 60 * 6 - 1 && pop <= PopMax * 60 * 6 + 1 && !IgnoredNow(clock.Elapsed.TotalSeconds, (long)(wait * 5), true) && IgnoredNow(clock.Elapsed.TotalSeconds, (long)(wait * 7), true);
                preferences.FedAt = SimTime.UtcNow.AddHours(-7).Ticks;
                int hunger;
                bool fedLater = Hunger("snack", out hunger) == null;
                Working.Ahead = delegate { return play; };
                Working.Forget();
                bool plain = !Working.Now(SimTime.UtcNow) && ChatterStretch == 1 && Probes.Stretch() == 1 && Hunger("snack", out hunger) == "hungry" && IgnoredNow(clock.Elapsed.TotalSeconds, (long)(wait * 2), false);
                SetWhileWorking(Working.Never);
                mainframe.Sync();
                Working.Forget();
                bool never = Working.Stopped && Working.Now(SimTime.UtcNow) && double.IsInfinity(ChatterStretch) && double.IsInfinity(PopWait()) && Hunger("snack", out hunger) == null
                    && !IgnoredNow(clock.Elapsed.TotalSeconds, long.MaxValue / 2, true) && mainframe.WhileWorkingShown == Working.Never;
                bool kept = LoadPreferences().WhileWorking == Working.Never;
                if (!stretched || !fedLater || !plain || !never || !kept)
                    throw new Exception("While working went wrong: x6 with a work app in front " + stretched + ", hunger later " + fedLater + ", nothing stretched with other apps " + plain + ", its last step stops her timers whatever is in front " + never + ", kept in her settings " + kept);
                checks.Add("PASS While working: with a work app in front her feeling ignored, hunger, pop-ins, glances, jokes and questions run its many times slower (x6 here), with anything else in front as usual, and its last step stops them whatever is in front; check-ins and reminders aren't read from it; kept in her settings");

                // A sys.md from before: its quiet lines and their notes carried into her settings once, then gone; call apps stays.
                System.IO.Directory.CreateDirectory(folder);
                string old = System.IO.Path.Combine(folder, SysLines.FileName);
                System.IO.File.WriteAllText(old, "# Her system lines\r\n\r\n" + SysLines.QuietHeading + "\r\n\r\n>! quiet min hours: the shortest her quiet lasts, 1 to 8.\r\n>! quiet on calls:  1 is quiet during a call in an app below.\r\n>! call apps:       window titles.\r\n\r\nquiet min hours:      5\r\nquiet max hours:      0\r\nquiet on calls:       0\r\nquiet on videos:      1\r\ncall apps:            zoom meeting, huddle\r\n");
                SetQuietHours(1, 3);
                SetQuietCalls(true);
                Dictionary<string, string> taken = SysLines.TakeQuiet(old);
                if (taken != null) PutQuiet(taken);
                string after = System.IO.File.ReadAllText(old);
                bool carried = QuietMinHours == 5 && QuietMaxHours == 0 && !QuietOnCalls && QuietOnVideos && CallAppsSaid == "zoom meeting, huddle" && LoadPreferences().QuietMin == 5;
                bool cleaned = after.IndexOf("quiet", StringComparison.OrdinalIgnoreCase) < 0 && after.IndexOf("call apps", StringComparison.OrdinalIgnoreCase) < 0 && after.Contains("# Her system lines");
                bool once = SysLines.TakeQuiet(old) == null;
                PutQuiet(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { QuietTimeKey, "300" } });
                bool oldTime = QuietMinHours == 5 && QuietMaxHours == 6;
                if (!carried || !cleaned || !once || !oldTime)
                    throw new Exception("An older sys.md's quiet lines didn't carry over once: carried " + carried + ", lines, notes and heading gone " + cleaned + ", once " + once + ", an old quiet time on min and max " + oldTime + ": " + after);
                checks.Add("PASS an older sys.md's quiet min and max hours, quiet on calls, quiet on videos and call apps carry into her settings once, and their lines, notes and heading leave; an old quiet time lands on min and max");
            }
            finally
            {
                if (Focused) EndFocus(false);
                if (shushed != shushWas) ToggleShush(false);
                muted = mutedWas;
                quietUntil = untilWas;
                preferences.QuietMin = minWas;
                preferences.QuietMax = maxWas;
                preferences.QuietForever = foreverWas;
                preferences.NoQuietCalls = callsWas;
                preferences.NoQuietVideos = videosWas;
                preferences.CallApps = appsWas;
                preferences.WhileWorking = workingWas;
                preferences.FedAt = fedWas;
                Working.Times = WhileWorking;
                Working.Ahead = aheadWas;
                Working.Apps = preferences.WorkApps == null ? null : preferences.WorkApps.Split('\n');
                Working.Forget();
                SavePreferences();
                try { if (System.IO.Directory.Exists(folder)) System.IO.Directory.Delete(folder, true); } catch (System.IO.IOException) { }
                mainframe.Sync();
            }
        }
    }
}
