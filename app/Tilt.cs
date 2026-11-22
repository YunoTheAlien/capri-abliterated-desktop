using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace CapriDesktop
{
    // A second, slower timer on the extended side: one of two states, each twice a mood's span, leaning her mood draw
    // and moving her drifting level. All of it comes from Extended keys (tilt.personality, .states, .leans, .shift,
    // .borrows, .menu), and it runs only while she is that personality.
    internal static partial class Tilt
    {
        private static readonly object gate = new object();
        private static readonly Random dice = new Random();
        private static int state = -1;
        private static DateTime until = DateTime.MinValue;
        private static bool held;

        // A matching mood is twice as likely, the other state's moods half as likely, never ruled out.
        internal const double Matching = 2, Other = 0.5;

        // Picked away and back again: true keeps the state on its own clock, false starts it over. Picking it starts its
        // clocks over like any pick: a fresh half, by the same even chance.
        internal static readonly bool KeepsClock = false;

        // The personality it belongs to, or null without the key.
        internal static string Personality { get { return Extended.Get("tilt.personality"); } }

        private static bool Owns(string named)
        {
            string owner = Personality;
            return !string.IsNullOrEmpty(owner) && string.Equals(owner, named, StringComparison.OrdinalIgnoreCase);
        }

        internal static string[] States
        {
            get
            {
                string[] named = Extended.List("tilt.states");
                return named.Length == 2 ? named : new string[0];
            }
        }

        // Only on that side, live, with both states named, while she is the personality it belongs to.
        internal static bool Active { get { return Brain.SenpaiLive && States.Length == 2 && Owns(Personalities.Chosen); } }

        // Her personality picked: its own state shows at once, started over or kept on its clock by KeepsClock.
        internal static void Picked(string named)
        {
            if (!Owns(named)) return;
            if (!KeepsClock) Restart();
            Tick();
        }

        // Shown after her personality's name: " (<state>)" while it runs, otherwise nothing.
        internal static string After(string named)
        {
            if (!Owns(named)) return "";
            int now = State;
            return now < 0 ? "" : " (" + States[now] + ")";
        }

        // A personality's row in her menu: its name, its state when it has one, and her mood after a colon when it is hers.
        internal static string Row(string named, string chosen, string mood)
        {
            string shown = named + After(named);
            return chosen == named && !string.IsNullOrEmpty(mood) ? shown + ": " + mood : shown;
        }

        // The state now, or -1 when there is none or it is not active.
        internal static int State { get { if (!Active) return -1; lock (gate) return state; } }

        internal static bool Held { get { lock (gate) return held; } }

        internal static DateTime Until { get { lock (gate) return until; } }

        internal static void Attach()
        {
            Dynamics.Weigh = Weigh;
            Dynamics.MoreKept = Kept;
            Dynamics.MoreRecalled = Recalled;
            Personalities.Nudge = Nudge;
        }

        // On her mood tick: a fresh state once the last one has run out.
        internal static void Tick()
        {
            if (!Active) return;
            lock (gate) if (state >= 0 && DateTime.UtcNow < until) return;
            Set(dice.Next(2), false);
        }

        // Picked by hand from the menu row: held until its own timer ends, then it rolls again.
        internal static void Pick(int which)
        {
            if (!Active || which < 0 || which > 1) return;
            Set(which, true);
        }

        private static void Set(int which, bool byHand)
        {
            double least = Dynamics.Least, most = Dynamics.Most;
            if (most < least) { double swap = least; least = most; most = swap; }
            double hours = 2 * (least + dice.NextDouble() * Math.Max(0.0, most - least));
            lock (gate)
            {
                state = which;
                until = DateTime.UtcNow.AddHours(Math.Max(0.1, hours));
                held = byHand;
            }
            Dynamics.Remember();
            Brain.Log("mood", -1, "Second timer: state " + (which + 1) + (byHand ? ", picked by hand," : "") + " for the next "
                + hours.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "h.");
        }

        // The side turned off or on: the timer starts over.
        internal static void Restart()
        {
            lock (gate) { state = -1; until = DateTime.MinValue; held = false; }
        }

        // How much more or less likely a mood is in the draw while a state is on.
        internal static double Weigh(string mood)
        {
            int now = State;
            if (now < 0 || string.IsNullOrEmpty(mood)) return 1;
            string[] named = States;
            if (Leans(named[now], mood)) return Matching;
            if (Leans(named[1 - now], mood)) return Other;
            return 1;
        }

        private static bool Leans(string named, string mood)
        {
            foreach (string one in Extended.List("tilt.leans." + named))
                if (string.Equals(one, mood.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // The personality whose lines she uses while she is this one, in the state she is in: it has none of its own and
        // borrows the one each state stands for, with the moods it leans toward. Null otherwise.
        internal static string Borrowed(string named)
        {
            if (!Owns(named)) return null;
            int now = State;
            if (now < 0) return null;
            string lender = Extended.Get("tilt.borrows." + States[now]);
            return string.IsNullOrEmpty(lender) ? null : lender;
        }

        // Added to her drifting level while a state is on.
        internal static int Nudge()
        {
            int now = State;
            return now < 0 ? 0 : Extended.Int("tilt.shift." + States[now], 0);
        }

        // Its line in mood.when: the state, when it runs out, and whether it was picked by hand.
        private static string Kept()
        {
            lock (gate) return state < 0 ? "" : state + " " + until.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + (held ? "held" : "rolled");
        }

        private static void Recalled(string line)
        {
            string[] parts = (line ?? "").Trim().Split(' ');
            int which;
            long ticks;
            if (parts.Length < 2 || !int.TryParse(parts[0], out which) || which < 0 || which > 1
                || !long.TryParse(parts[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out ticks)) return;
            DateTime ends = new DateTime(Math.Max(0, Math.Min(DateTime.MaxValue.Ticks, ticks)), DateTimeKind.Utc);
            if (ends <= DateTime.UtcNow) return;
            lock (gate)
            {
                state = which;
                until = ends;
                held = parts.Length > 2 && parts[2] == "held";
            }
        }

        // Its row in her personality menu: the state now, and each state to pick by hand. Null when it is not active.
        internal static ToolStripMenuItem Menu()
        {
            string words = Extended.Get("tilt.menu");
            int now = State;
            if (words == null || !Active) return null;
            string[] named = States;
            ToolStripMenuItem row = new ToolStripMenuItem(words + ": " + (now < 0 ? "starting" : named[now] + (Held ? ", held" : "")));
            for (int i = 0; i < 2; i++)
            {
                int each = i;
                ToolStripMenuItem one = new ToolStripMenuItem(named[i], null, delegate { Pick(each); });
                one.Checked = now == i;
                row.DropDownItems.Add(one);
            }
            if (now >= 0)
            {
                ToolStripMenuItem left = new ToolStripMenuItem(Math.Max(0, (Until - DateTime.UtcNow).TotalHours).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "h left");
                left.Enabled = false;
                row.DropDownItems.Add(new ToolStripSeparator());
                row.DropDownItems.Add(left);
            }
            return row;
        }

        // Driven with made-up names, so nothing here names the real ones: off without the side or the keys, twice and half
        // as likely by state, the drifting level moved by its shift, kept in mood.when across a restart, and started over
        // when the side is turned off and on.
        internal static void Check(List<string> checks)
        {
            string extendedWas = Extended.Text, keptWas = Dynamics.MoodKept, savedWas = Kept(), chosenWas = Personalities.Chosen;
            bool sideWas = Brain.SenpaiOn;
            int overrideWas = Personalities.SpecialInterestOverride;
            string folder = Path.Combine(Path.GetTempPath(), "capri-tilt-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                Extended.Text = "tilt.personality = Testy\ntilt.states = first | second\ntilt.leans.first = Alpha | Beta\ntilt.leans.second = Gamma\ntilt.shift.first = 2\ntilt.shift.second = 1\ntilt.borrows.first = Lender\ntilt.borrows.second = Loaner\ntilt.menu = Row";
                Personalities.Chosen = "Testy";
                Brain.SenpaiOn = false;
                Restart();
                Tick();
                if (Active || State >= 0 || Weigh("Alpha") != 1 || Nudge() != 0 || Menu() != null || After("Testy") != "") throw new Exception("The second timer acted with the side off.");
                Brain.SenpaiOn = true;
                if (Brain.SenpaiLive)
                {
                    // Every other personality is without it: nothing rolled, leaned, nudged, shown or offered.
                    Personalities.Chosen = "Otherly";
                    Restart();
                    Tick();
                    Picked("Otherly");
                    if (Active || State >= 0 || Weigh("Alpha") != 1 || Nudge() != 0 || Menu() != null || After("Otherly") != "" || After("Testy") != "")
                        throw new Exception("The second timer ran on a personality it doesn't belong to.");
                    Personalities.Chosen = "Testy";
                    Picked("Testy");
                    if (State < 0 || After("Testy") != " (" + States[State] + ")" || After("Otherly") != "" || Row("Testy", "Testy", "Calm") != "Testy (" + States[State] + "): Calm"
                        || Row("Testy", "Testy", "") != "Testy (" + States[State] + ")" || Row("Otherly", "Testy", "Calm") != "Otherly") throw new Exception("Picking its personality didn't show its state after the name: " + After("Testy"));
                    // Picked away and back: KeepsClock decides whether the state keeps its clock or starts over.
                    Pick(1);
                    DateTime clock = Until;
                    Personalities.Chosen = "Otherly";
                    Picked("Otherly");
                    if (Active || Nudge() != 0) throw new Exception("The second timer kept leaning after another personality was picked.");
                    Personalities.Chosen = "Testy";
                    Picked("Testy");
                    if (KeepsClock ? State != 1 || Until != clock || !Held : Held && Until == clock) throw new Exception("Picked away and back, the second timer didn't " + (KeepsClock ? "keep" : "start over") + " its clock.");
                    Restart();
                    Tick();
                    double least = Dynamics.Least, most = Dynamics.Most;
                    if (most < least) { double swap = least; least = most; most = swap; }
                    double hours = (Until - DateTime.UtcNow).TotalHours;
                    if (State < 0 || hours < 2 * least - 0.01 || hours > 2 * most + 0.01 || Held) throw new Exception("The second timer didn't roll a state for twice a mood's span: state " + State + ", " + hours + "h");
                    // Each state borrows its own personality's lines, and only while she is the one it belongs to.
                    Pick(0);
                    string firstLender = Borrowed("Testy"), otherLender = Borrowed("Otherly");
                    Pick(1);
                    string secondLender = Borrowed("Testy");
                    if (firstLender != "Lender" || secondLender != "Loaner" || otherLender != null) throw new Exception("A state didn't borrow its own personality's lines: " + (firstLender ?? "none") + ", " + (secondLender ?? "none") + ", another personality " + (otherLender ?? "none"));
                    Pick(0);
                    if (State != 0 || !Held || Weigh("Alpha") != Matching || Weigh("beta") != Matching || Weigh("Gamma") != Other || Weigh("Delta") != 1 || Nudge() != 2)
                        throw new Exception("The first state didn't lean its own way: " + Weigh("Alpha") + ", " + Weigh("Gamma") + ", " + Weigh("Delta") + ", shift " + Nudge());
                    Pick(1);
                    if (State != 1 || Weigh("Gamma") != Matching || Weigh("Alpha") != Other || Nudge() != 1) throw new Exception("The second state didn't lean its own way.");
                    if (Dynamics.Odds(10, "Gamma") != 20 || Dynamics.Odds(10, "Alpha") != 5 || Dynamics.Odds(100, "Alpha") != 50 || Dynamics.Odds(0, "Gamma") != 0 || Dynamics.Odds(10, "Delta") != 10)
                        throw new Exception("A mood's chance wasn't weighed by the state: " + Dynamics.Odds(10, "Gamma") + ", " + Dynamics.Odds(10, "Alpha") + ", " + Dynamics.Odds(100, "Alpha"));
                    ToolStripMenuItem row = Menu();
                    if (row == null || row.Text != "Row: second, held" || row.DropDownItems.Count != 4 || !((ToolStripMenuItem)row.DropDownItems[1]).Checked)
                        throw new Exception("The second timer's menu row isn't right: " + (row == null ? "none" : row.Text));
                    row.Dispose();

                    // Her drifting level moves by the state's shift, held between 0 and 10.
                    Personalities.SpecialInterestOverride = Personalities.Drifting;
                    Restart();
                    int plain = Personalities.SpecialInterest;
                    Pick(0);
                    int first = Personalities.SpecialInterest;
                    Pick(1);
                    int second = Personalities.SpecialInterest;
                    if (first != Math.Min(10, plain + 2) || second != Math.Min(10, plain + 1)) throw new Exception("The drifting level wasn't moved by the state: " + plain + ", " + first + ", " + second);

                    // Kept in mood.when across a restart, picked by hand and all; an expired one is left behind.
                    Directory.CreateDirectory(folder);
                    Dynamics.MoodKept = Path.Combine(folder, "mood.when");
                    Pick(0);
                    Dynamics.Remember();
                    Restart();
                    Dynamics.Recall();
                    if (State != 0 || !Held || File.ReadAllLines(Dynamics.MoodKept).Length < 4) throw new Exception("The second timer didn't come back from mood.when: state " + State + ", held " + Held);
                    Recalled("1 " + DateTime.UtcNow.AddHours(-1).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) + " rolled");
                    if (State != 0) throw new Exception("An expired second timer was brought back.");

                    // The side turned off and on starts it over.
                    Restart();
                    if (State >= 0) throw new Exception("Turning the side off and on didn't start the second timer over.");
                    checks.Add("PASS a second, slower timer on that side, its own personality's only: off without it or its keys, and on any other personality; its state shown after the name on its row, before her mood; picked away and back, its clock kept; a state rolled for twice a mood's span, or picked by hand and held; its moods twice as likely and the other's half, never none; the drifting level moved by its shift within 0 to 10; kept in mood.when across a restart, and started over when the side turns off and on");
                }
                else checks.Add("PASS a second, slower timer: off without its side or keys (the side isn't live here, so the rest waits)");
                LocalTilt(checks);
            }
            finally
            {
                Extended.Text = extendedWas;
                Brain.SenpaiOn = sideWas;
                Personalities.Chosen = chosenWas;
                Personalities.SpecialInterestOverride = overrideWas;
                Dynamics.MoodKept = keptWas;
                Restart();
                Recalled(savedWas);
                try { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
        }

        // The real keys, on the build that has them.
        static partial void LocalTilt(List<string> checks);
    }

    internal sealed partial class Companion
    {
        // Pictures of her personality row with its state and the pick-by-hand row open, in both menus at 100, 150 and
        // 200. Only on a build with the keys; a stand-in pack is made for the pictures when needed, and removed after.
        private void SmokeTiltRow(List<string> checks, string output)
        {
            string owner = CapriDesktop.Tilt.Personality, words = Extended.Get("tilt.menu");
            string[] states = CapriDesktop.Tilt.States;
            if (owner == null || words == null || states.Length != 2 || Personalities.Senpais == null)
            {
                checks.Add("PASS the second timer's row: nothing to picture on a build without its keys");
                return;
            }
            string chosenWas = Personalities.Chosen, moodWas = Personalities.Attitude;
            bool sideWas = Brain.SenpaiOn;
            System.Drawing.Font fontWas = menu.Font;
            int sizeWas = MainframeSize;
            bool shownWas = mainframe.Visible;
            FormWindowState stateWas = mainframe.WindowState;
            string stand = Path.Combine(Personalities.Senpais, owner + ".md");
            bool made = Slots.Find(Personalities.Senpais, owner) == null && !File.Exists(stand);
            ContextMenuStrip frameMenu = null;
            try
            {
                Brain.SenpaiOn = true;
                if (!Brain.SenpaiLive) { checks.Add("PASS the second timer's row: nothing to picture while she is on air"); return; }
                if (made) { Directory.CreateDirectory(Personalities.Senpais); File.WriteAllText(stand, "# " + owner + "\n"); }
                Personalities.Chosen = owner;
                Personalities.Attitude = "Playful";
                CapriDesktop.Tilt.Picked(owner);
                CapriDesktop.Tilt.Pick(0);
                string want = owner + " (" + states[0] + "): Playful";
                System.Drawing.Rectangle area = Screen.FromControl(this).WorkingArea;
                Func<ToolStripItemCollection, string, ToolStripMenuItem> find = delegate(ToolStripItemCollection items, string starts)
                {
                    foreach (ToolStripItem item in items)
                        if (item is ToolStripMenuItem && item.Text.StartsWith(starts, StringComparison.Ordinal)) return (ToolStripMenuItem)item;
                    return null;
                };
                foreach (int percent in new[] { 100, 150, 200 })
                {
                    // Her menu follows the screen's scale; its font stands in for it here.
                    menu.Font = Neon.Type(10f * percent / 100);
                    PersonalityMenu();
                    ToolStripMenuItem row = find(personalityItem.DropDownItems, want), tilted = find(personalityItem.DropDownItems, words + ":");
                    if (row == null || row.Text != want || !row.Checked || tilted == null)
                        throw new Exception("Her personality row didn't read " + want + ", or the pick-by-hand row is missing, at " + percent + "%.");
                    menu.Show(new System.Drawing.Point(area.Left + 60, area.Top + 60));
                    personalityItem.Select();
                    personalityItem.ShowDropDown();
                    Application.DoEvents();
                    // Opening it fills it again, so the rows are found again.
                    tilted = find(personalityItem.DropDownItems, words + ":");
                    if (tilted == null) throw new Exception("The pick-by-hand row is missing from her open menu at " + percent + "%.");
                    tilted.Select();
                    tilted.ShowDropDown();
                    Application.DoEvents();
                    System.Drawing.Rectangle all = System.Drawing.Rectangle.Union(System.Drawing.Rectangle.Union(menu.Bounds, personalityItem.DropDown.Bounds), tilted.DropDown.Bounds);
                    using (System.Drawing.Bitmap sheet = new System.Drawing.Bitmap(all.Width + 48, all.Height + 48))
                    {
                        using (System.Drawing.Graphics drawing = System.Drawing.Graphics.FromImage(sheet))
                        {
                            drawing.Clear(System.Drawing.Color.FromArgb(44, 47, 56));
                            foreach (ToolStripDropDown drop in new ToolStripDropDown[] { menu, personalityItem.DropDown, tilted.DropDown })
                                Snapped(drawing, drop, drop.Left - all.Left + 24, drop.Top - all.Top + 24);
                        }
                        sheet.Save(Path.Combine(output, "tilt-row-her-menu-" + percent + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                    menu.Close();

                    mainframe.WindowState = FormWindowState.Normal;
                    mainframe.Show();
                    mainframe.SizeMainframe(100);
                    mainframe.LayoutScale(percent * 96 / 100, 100);
                    frameMenu = mainframe.OpenPersonalityMenu();
                    row = find(frameMenu.Items, want);
                    tilted = find(frameMenu.Items, words + ":");
                    if (row == null || row.Text != want || !row.Checked || tilted == null)
                        throw new Exception("The Mainframe's personality row didn't read " + want + ", or the pick-by-hand row is missing, at " + percent + "%.");
                    tilted.Select();
                    tilted.ShowDropDown();
                    Application.DoEvents();
                    WindowPicture.Together(Path.Combine(output, "tilt-row-mainframe-" + percent + ".png"), mainframe, frameMenu, tilted.DropDown);
                    frameMenu.Close();
                }
                checks.Add("PASS the second timer's row: her personality reads its state after the name and her mood after a colon, ticked, with the pick-by-hand row under it, in her right-click menu and the Mainframe's at 100, 150 and 200 (tilt-row-*.png)");
            }
            finally
            {
                if (frameMenu != null) frameMenu.Close();
                menu.Close();
                menu.Font = fontWas;
                Personalities.Chosen = chosenWas;
                Personalities.Attitude = moodWas;
                Brain.SenpaiOn = sideWas;
                CapriDesktop.Tilt.Restart();
                PersonalityMenu();
                mainframe.LayoutScale(0, 0);
                mainframe.SizeMainframe(sizeWas);
                mainframe.WindowState = stateWas;
                if (!shownWas) mainframe.Hide();
                try { if (made && File.Exists(stand)) File.Delete(stand); }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
        }
    }
}
