using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Her quiet is one state however it is turned on, the switch, key, "Mute her" or an ask, and every one shows it. It
    // lasts a random time between sys.md's min and max hours, unless they name a time.
    internal sealed partial class Mainframe
    {
        private Toggle quietSwitch, quietCalls, quietVideos;
        private Slider quietMin, quietMax;
        private Readout quietMinName, quietMaxName;

        // Min is 1 to 8 hours; max is 2 to 9 hours, then until she is told otherwise, and never under min + 1.
        internal static readonly string[] QuietMinNames = Hours(1, SysLines.QuietMostHours - 1, null);
        internal static readonly string[] QuietMaxNames = Hours(2, SysLines.QuietMostHours, QuietForever);
        internal const string QuietForever = "Until I turn it off";

        private static string[] Hours(int from, int to, string last)
        {
            List<string> names = new List<string>();
            for (int hours = from; hours <= to; hours++) names.Add(hours + " h");
            if (last != null) names.Add(last);
            return names.ToArray();
        }

        private static int MaxHoursAt(int at) { return at >= QuietMaxNames.Length - 1 ? 0 : at + 2; }

        // The rows under Hologram, built at CAPRI's own tops before the column moves down.
        private void BuildQuiet(int c, int top)
        {
            quietSwitch = Switch("Quiet", c, top, delegate { if (!syncing) capri.SetQuiet(quietSwitch.Checked); });
            quietSwitch.Switch = 68;
            quietSwitch.AccessibleName = "Quiet. She stops talking on her own and only answers when you talk to her, the same as her Quiet key and Mute her";
            quietMin = Setting("Quiet min", QuietMinNames, c, top + Row, "Quiet min: the shortest her quiet lasts, from 1 to 8 hours");
            quietMinName = new Readout(quietMin);
            quietMax = Setting("Quiet max", QuietMaxNames, c, top + Row * 2, "Quiet max: the longest her quiet lasts, at least an hour over Quiet min, or until you turn it off");
            quietMaxName = new Readout(quietMax);
            // Max never sits under min + 1: raising min pushes max up, and max stops at min + 1 on the way down.
            quietMin.Changed += delegate
            {
                if (syncing) return;
                quietMinName.Text = QuietMinNames[quietMin.Value];
                if (quietMax.Value < quietMin.Value) { quietMax.Value = quietMin.Value; quietMaxName.Text = QuietMaxNames[quietMax.Value]; }
            };
            quietMax.Changed += delegate
            {
                if (syncing) return;
                if (quietMax.Value < quietMin.Value) quietMax.Value = quietMin.Value;
                quietMaxName.Text = QuietMaxNames[quietMax.Value];
            };
            foreach (Slider hours in new[] { quietMin, quietMax })
                hours.Released += delegate { if (!syncing) capri.SetQuietHours(quietMin.Value + 1, MaxHoursAt(quietMax.Value)); };
            quietCalls = Switch("Quiet on calls", c, top + Row * 3, delegate { if (!syncing) capri.SetQuietOn(SysLines.QuietCallsKey, quietCalls.Checked); });
            quietCalls.AccessibleName = "Quiet on calls. She goes quiet on her own during a call in Zoom, Meet, Slack, Webex or Teams";
            quietVideos = Switch("Quiet in videos", c, top + Row * 4, delegate { if (!syncing) capri.SetQuietOn(SysLines.QuietVideoKey, quietVideos.Checked); });
            quietVideos.AccessibleName = "Quiet in videos. She goes quiet on her own while a video fills the screen";
        }

        // Her quiet as it is now: on or off however it was turned on, when it ends, its hours and the two of her own.
        private void SyncQuiet()
        {
            if (quietSwitch == null) return;
            QuietShow();
            quietMin.Value = SysLines.QuietMinHours - 1;
            quietMinName.Text = QuietMinNames[quietMin.Value];
            int max = SysLines.QuietMaxHours;
            quietMax.Value = max <= 0 ? QuietMaxNames.Length - 1 : max - 2;
            quietMaxName.Text = QuietMaxNames[quietMax.Value];
            quietCalls.Checked = SysLines.QuietOnCalls;
            quietVideos.Checked = SysLines.QuietOnVideos;
        }

        private int quietShownAt;
        // The switch's words as she wrote them, before her language's table, so a translated one isn't rewritten each second.
        private string quietSaid = "Quiet";

        // Both switches follow her quiet however it changed, and the time it ends is kept on the switch.
        internal void QuietTick()
        {
            if (quietSwitch == null) return;
            bool on = capri.QuietOn;
            if (quietSwitch.Checked == on && gameMute.Checked == on && (quietSaid != "Quiet") == on && unchecked(Environment.TickCount - quietShownAt) < 1000) return;
            QuietShow();
        }

        private void QuietShow()
        {
            quietShownAt = Environment.TickCount;
            bool on = capri.QuietOn, was = syncing;
            syncing = true;
            try { quietSwitch.Checked = on; gameMute.Checked = on; } finally { syncing = was; }
            string said = capri.QuietLeft;
            if (quietSaid != said) quietSwitch.Text = quietSaid = said;
        }

        internal bool QuietShown { get { return quietSwitch != null && quietSwitch.Checked && gameMute.Checked; } }
        internal string QuietSaid { get { return quietSaid; } }
        internal int QuietMaxShown { get { return MaxHoursAt(quietMax.Value); } }

        // Their arrow keys on a slider, then let go, for her checks.
        internal void SlideQuiet(bool max, int steps)
        {
            Press(max ? quietMax : quietMin, steps);
            capri.SetQuietHours(quietMin.Value + 1, MaxHoursAt(quietMax.Value));
        }
    }

    internal sealed partial class Companion
    {
        // When her quiet ends, or -1 while it lasts until she is told otherwise. Only while shushed.
        private double quietUntil = -1;

        internal bool QuietOn { get { return shushed || Focused; } }

        // "Quiet until 15:45", "Quiet until turned off", or just "Quiet" while she isn't.
        internal string QuietLeft
        {
            get
            {
                if (!QuietOn) return "Quiet";
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
            int min = SysLines.QuietMinHours, max = SysLines.QuietMaxHours;
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

        // On or off from the Mainframe, the same as her Quiet key, without her saying so.
        internal void SetQuiet(bool on)
        {
            if (on == QuietOn) { if (mainframe != null) mainframe.QuietTick(); return; }
            if (on) ToggleShush(false);
            else
            {
                if (Focused) EndFocus(false);
                if (shushed) ToggleShush(false);
            }
            if (mainframe != null) mainframe.QuietTick();
        }

        internal void SetQuietHours(int min, int max)
        {
            min = Math.Max(1, Math.Min(SysLines.QuietMostHours - 1, min));
            if (max > 0) max = Math.Max(min + 1, Math.Min(SysLines.QuietMostHours, max));
            SysLines.SetValue(SysLines.QuietMinKey, min.ToString(System.Globalization.CultureInfo.InvariantCulture));
            SysLines.SetValue(SysLines.QuietMaxKey, Math.Max(0, max).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        internal void SetQuietOn(string key, bool on)
        {
            SysLines.SetValue(key, on ? "1" : "0");
        }

        // Her quiet's time is up: she is back, said only in her bubble.
        private void CheckQuiet(double now)
        {
            if (!shushed || quietUntil < 0 || now < quietUntil) return;
            ToggleShush(false);
            Remark("", "Quiet's over. I'm back.", true);
        }

        private void SmokeQuietRow(List<string> checks)
        {
            bool shushWas = shushed;
            double untilWas = quietUntil;
            string minWas = SysLines.Of(SysLines.QuietMinKey), maxWas = SysLines.Of(SysLines.QuietMaxKey);
            try
            {
                if (shushed) ToggleShush(false);
                if (Focused) EndFocus(false);
                // The hours: max never under min + 1, and the slider's top is until turned off.
                SetQuietHours(2, 4);
                mainframe.Sync();
                mainframe.SlideQuiet(false, 3);
                bool pushed = SysLines.QuietMinHours == 5 && SysLines.QuietMaxHours == 6 && mainframe.QuietMaxShown == 6;
                mainframe.SlideQuiet(true, -3);
                bool floor = SysLines.QuietMaxHours == 6;
                mainframe.SlideQuiet(true, 20);
                bool forever = SysLines.QuietMaxHours == 0;
                if (!pushed || !floor || !forever)
                    throw new Exception("Quiet's sliders went wrong: min pushes max " + pushed + ", max stops at min + 1 " + floor + ", the top is until turned off " + forever + " (" + SysLines.QuietMinHours + " to " + SysLines.QuietMaxHours + ")");
                // One state: the switch, the key and Mute her each turn it on and off and every one shows it.
                SetQuietHours(1, 3);
                SetQuiet(true);
                double length = quietUntil - clock.Elapsed.TotalSeconds;
                bool timed = shushed && QuietOn && length >= 3600 - 5 && length <= 3 * 3600 + 5 && mainframe.QuietShown && mainframe.QuietSaid.StartsWith("Quiet until ", StringComparison.Ordinal) && mainframe.QuietSaid != "Quiet until turned off";
                ToggleShush(false);
                bool keyOff = !QuietOn && !mainframe.QuietShown && mainframe.QuietSaid == "Quiet";
                SetQuietHours(1, 0);
                ToggleShush(false);
                bool untilOff = shushed && quietUntil < 0 && mainframe.QuietShown && mainframe.QuietSaid == "Quiet until turned off";
                SetQuiet(false);
                lock (shortcutGate) heardLast = "I need some quiet";
                SetSetting("quiet", true);
                bool asked = Focused && mainframe.QuietShown && mainframe.QuietSaid == "Quiet until turned off";
                SetQuiet(false);
                SetQuietHours(1, 3);
                ToggleShush(false);
                quietUntil = clock.Elapsed.TotalSeconds - 1;
                CheckQuiet(clock.Elapsed.TotalSeconds);
                bool ended = !shushed && !QuietOn && !mainframe.QuietShown;
                if (!timed || !keyOff || !untilOff || !asked || !ended)
                    throw new Exception("Her quiet went wrong: timed " + timed + ", the key turning it off " + keyOff + ", until turned off " + untilOff + ", an ask showing on the switch " + asked + ", ended on time " + ended + " (" + mainframe.QuietSaid + ")");
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
                SetQuietOn(SysLines.QuietCallsKey, false);
                bool callsOff = !SysLines.QuietOnCalls;
                SetQuietOn(SysLines.QuietCallsKey, true);
                if (!callsOff || !SysLines.QuietOnCalls) throw new Exception("Quiet on calls didn't save to sys.md.");
                checks.Add("PASS Quiet is one state: the switch under Hologram, her Quiet key, Mute her and the asks for quiet turn it on and off and all show it with the time it ends; it lasts a random time between sys.md's min and max hours, max never under min + 1 with until turned off at the top, a time they name wins, the end is said in her bubble only, and quiet on calls saves to sys.md");
            }
            finally
            {
                if (Focused) EndFocus(false);
                if (shushed != shushWas) ToggleShush(false);
                quietUntil = untilWas;
                SysLines.SetValue(SysLines.QuietMinKey, minWas ?? "1");
                SysLines.SetValue(SysLines.QuietMaxKey, maxWas ?? "3");
                mainframe.Sync();
            }
        }
    }
}
