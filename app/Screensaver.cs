using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Her screensaver's looks by name, and what she asks of a showing. The screensaver itself is a mod of its own,
    // capri-screensaver, with its look in its own sliders in Toggles.
    internal static class SaverLook
    {
        internal const string Terminals = "terminals", Code = "code", Rain = "rain", Hexes = "hexes";
        internal static readonly string[] Styles = { Terminals, Hexes, Code, Rain };

        // Her ask for one showing: the look asked for, or one of those switched on in Toggles. Her screensavers are silent;
        // one that has a sound ships it inside its own mod.
        internal static CapriSaverAsk Ask(string asked, bool sound)
        {
            CapriSaverAsk ask = new CapriSaverAsk();
            string style = (asked ?? SysLines.SaverStyleSaved() ?? "").Trim();
            ask.Look = Terminals;
            foreach (string one in Styles) if (style.Equals(one, StringComparison.OrdinalIgnoreCase)) ask.Look = one;
            return ask;
        }
    }

    internal sealed partial class Companion
    {
        private double saverCheckedAt;

        // The screensaver mod running now, or null when it is off or missing.
        private ICapriScreensaver SaverMod { get { return ModList == null ? null : ModList.Screensaver; } }

        private bool SaverShowing { get { ICapriScreensaver mod = SaverMod; return alertWindows.Count > 0 || (mod != null && mod.Showing); } }

        // The looks of the screensaver mod running now, for Test > Screensavers; none when it is off.
        internal string[] SaverLooks
        {
            get
            {
                ICapriScreensaver mod = SaverMod;
                try { return mod == null ? new string[0] : mod.Looks ?? new string[0]; }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); return new string[0]; }
            }
        }

        // Shown from her menu: the pause stays, since the spacebar that picks the menu item is also the key that ends
        // it. Only the idle rule is left off.
        internal void ShowSaverNow() { ShowSaverNow(null); }

        internal void ShowSaverNow(string style)
        {
            if (InvokeRequired) { Invoke(new Action<string>(ShowSaverNow), style); return; }
            Brain.Log("screensaver", -1, "Asked for from her menu (" + (style ?? "as set in Toggles") + "), with " + (ScreensaverOn ? string.Join(", ", SaverLooksOn) : "no look") + " on, the mod " + (SaverMod == null ? "off" : "on") + ".");
            if (SaverMod == null) { Remark("", "My screensaver is a mod now, and it's off. Turn it on in Mods.", true); return; }
            if (style == null && !ScreensaverOn && !SaverShowing) { Remark("", "None of my screensavers is switched on. Switch one on in Toggles.", true); return; }
            if (SaverShowing) { SaverOff(); return; }
            if (SaverHeld()) { Brain.Log("screensaver", -1, "Refused: " + HeldWhy() + "."); Remark("", "Not while something is full screen or on camera.", true); return; }
            // Longer than it takes to let go of a menu, since the click that asked for it is an input too.
            ShowSaver(style, 1.4, true, false, 0);
        }

        // Twice a second is plenty. Idle time only starts it; it ends itself on its key, and is taken down here when
        // something holds it.
        private void SaverTick(double now)
        {
            if (now - saverCheckedAt < 0.5) return;
            saverCheckedAt = now;
            if (!SaverShowing)
            {
                bullySaver = false;
                bool rested = Rested();
                if (SaverMod != null && !SaverHeld() && SysLines.Saver && rested) ShowSaver(null, 0.6, false, false, 0);
                else if (rested && !saverDueLogged) { saverDueLogged = true; Brain.Log("screensaver", -1, "Idle long enough, but " + (!SysLines.Saver ? "the switch is off" : SaverMod == null ? "the mod is off" : "held: " + HeldWhy()) + "."); }
                if (!rested) saverDueLogged = false;
                return;
            }
            if (SaverHeld()) { Brain.Log("screensaver", -1, "Taken down: held, " + HeldWhy() + "."); SaverOff(); }
        }

        private bool saverDueLogged;

        // Whether the PC has been left alone for as long as they asked for.
        private bool Rested()
        {
            try { return Native.IdleMilliseconds() >= SysLines.SaverAfter * 60000; }
            catch (Exception) { return false; }
        }

        // Which of SaverHeld's reasons holds it now, for her log.
        private string HeldWhy()
        {
            if (quitting) return "she is closing";
            if (Brain.OnAir) return "on air";
            if (working != null || MidJob(job)) return "mid-job";
            if (lentOut) return "her mouse is lent out";
            if (dragging || pressed) return "she is being dragged or pressed";
            if (bullySaver) return null;
            if (FullScreen()) return "something is full screen or on camera";
            if (Computer.Streaming) return "streaming";
            return null;
        }

        // What it never covers: a game or anything else filling the screen, a stream, a job of hers, her being
        // held or dragged, and her shutting down. The same list her fake crash respects, for the same reason.
        private bool SaverHeld()
        {
            bool busy = quitting || Brain.OnAir || working != null || MidJob(job) || lentOut || dragging || pressed;
            // A prank chat set off is meant to land on the stream, so full screen and streaming are lifted while one
            // runs; mouse, mid-job, dragging and shutting down still hold.
            return bullySaver ? busy : busy || FullScreen() || Computer.Streaming;
        }

        // Left alone, it plays no sound: an alarm at an empty chair isn't wanted. It still plays when asked for, so
        // the setting can be heard.
        private bool ShowSaver(string style, double settles, bool asked, bool anyKey, double ends)
        {
            ICapriScreensaver mod = SaverMod;
            if (mod == null) return false;
            CapriSaverAsk ask = SaverLook.Ask(style, asked);
            ask.Settles = settles;
            ask.AnyKey = anyKey;
            ask.Ends = ends;
            ask.Motion = preferences.Motion;
            ask.Hers = Screen.FromPoint(new Point(anchor.X + Width / 2, anchor.Y + Height / 2)).Bounds;
            try
            {
                bool shown = mod.Show(ask);
                Brain.Log("screensaver", -1, (shown ? "Shown: " : "The mod didn't show it: ") + ask.Look + (asked ? ", asked for" : ", idle") + (ask.Sound != null ? ", with sound at " + ask.Volume : "") + ".");
                return shown;
            }
            catch (Exception error) { Brain.Log("screensaver", -1, "The screensaver mod couldn't show it: " + error.GetType().Name + ": " + error.Message); return false; }
        }

        // red_alert: her hexagon alert with its alarm, for six seconds, ending on any key and undoing itself. A prank, drawn by
        // her own copy of the hexes (AlertScreen.cs), so it works with the Screensaver mod off, with its own sound.
        private const double AlertSeconds = 6;
        private bool bullySaver;
        private readonly List<AlertWindow> alertWindows = new List<AlertWindow>();

        internal const string AlarmFile = "alarm-loop.wav";
        private string alarmAsked;

        private string AlertTool(string action)
        {
            if (InvokeRequired) return (string)Invoke(new Func<string, string>(AlertTool), action);
            if (action == "stop") { SaverOff(); return "Stopped my red alert."; }
            if (SaverShowing) return "My red alert is already up.";
            if (quitting || SaverHeld()) return "I can't show that during full screen, a stream or a mouse job.";
            return ShowAlert(AlertSeconds) ? "Red alert." : "Couldn't show my red alert.";
        }

        // One window per screen, in the screensaver's Hexagons and Background colours from Custom colours.
        private bool ShowAlert(double ends)
        {
            AlertLook look = new AlertLook();
            look.Style = AlertLook.Hexes;
            look.Ink = AddonColours.Shown(AddonColours.Screensaver, "Hexagons");
            look.Ground = AddonColours.Shown(AddonColours.Screensaver, "Background");
            int seed = Environment.TickCount;
            foreach (Screen screen in Screen.AllScreens)
            {
                AlertWindow one = new AlertWindow(screen.Bounds, look, unchecked(seed += 7919));
                one.Settles = 1.4;
                one.AnyKey = true;
                one.Ends = ends;
                one.Motion = preferences.Motion;
                one.Ended = delegate(string line) { Brain.Log("prank", -1, "Red alert " + line); };
                try { if (!one.Open()) { one.Dispose(); continue; } }
                catch (Exception error) { one.Dispose(); Brain.Log("prank", -1, "Red alert couldn't open on " + screen.DeviceName + ": " + error.Message); continue; }
                alertWindows.Add(one);
                AlertWindow held = one;
                held.FormClosed += delegate
                {
                    alertWindows.Remove(held);
                    if (alertWindows.Count == 0) AlertSound.Off();
                    foreach (AlertWindow other in alertWindows.ToArray()) { try { other.Close(); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); } }
                };
            }
            // At her prank volume, as her other prank sounds play; a check records it rather than sounding it.
            alarmAsked = alertWindows.Count > 0 && prankSounds != null ? Path.Combine(prankSounds, AlarmFile) : null;
            if (alarmAsked != null && !testing) AlertSound.On(alarmAsked, (int)Math.Round(Sfx.SafeVolume(Math.Max(0, Math.Min(100, Sfx.Level)) / 100.0) * 100));
            return alertWindows.Count > 0;
        }

        // The alert chat set off, which is meant to land on the stream, so full screen and streaming are lifted
        // for as long as it runs and nothing else is. The same shape her fake crash uses for the same reason.
        internal string BullyAlert()
        {
            if (InvokeRequired) return (string)Invoke(new Func<string>(BullyAlert));
            bullySaver = true;
            string said = AlertTool("show");
            if (!SaverShowing) bullySaver = false;
            return said;
        }

        private void SaverOff()
        {
            AlertSound.Off();
            foreach (AlertWindow one in alertWindows.ToArray()) { try { one.Close(); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); } }
            ICapriScreensaver mod = SaverMod;
            if (mod == null) return;
            try { mod.Hide(); }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // The screensaver mod put up as she does it: every window open, layered and drawn. One that isn't layered has every
        // frame refused by Windows, which the fade check, reading only the ramp, never saw.
#if CHECKS
        private void SmokeSaverDraws(List<string> checks)
        {
            ICapriScreensaver mod = SaverMod;
            if (mod == null) { checks.Add("LIMIT the screensaver mod isn't running here, so its windows weren't put up"); return; }
            if (SaverShowing) SaverOff();
            List<string> wrong = new List<string>();
            int opened = 0;
            bool shown = false;
            try
            {
                shown = ShowSaver(SaverLook.Hexes, 0.6, false, false, 3);
                for (int wait = 0; wait < 10; wait++) { Application.DoEvents(); System.Threading.Thread.Sleep(30); }
                System.Reflection.FieldInfo field = mod.GetType().GetField("windows", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                System.Collections.IList windows = field == null ? null : field.GetValue(mod) as System.Collections.IList;
                if (windows != null)
                    foreach (object one in windows)
                    {
                        Form window = one as Form;
                        if (window == null) continue;
                        opened++;
                        bool layered = window.IsHandleCreated && (Native.GetWindowLong(window.Handle, -20) & 0x00080000) != 0;
                        System.Reflection.PropertyInfo drawn = one.GetType().GetProperty("Drawn", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                        bool painted = drawn != null && (bool)drawn.GetValue(one, null);
                        if (!layered || !painted || !window.Visible || window.FormBorderStyle != FormBorderStyle.None || window.ShowInTaskbar)
                            wrong.Add(window.Bounds + ": layered " + layered + ", drawn " + painted + ", shown " + window.Visible + ", border " + window.FormBorderStyle + ", taskbar " + window.ShowInTaskbar);
                    }
            }
            finally { SaverOff(); }
            if (!shown || opened != Screen.AllScreens.Length || wrong.Count > 0)
                throw new Exception("The screensaver mod didn't put up a drawn, layered window per screen: shown " + shown + ", " + opened + " of " + Screen.AllScreens.Length + (wrong.Count > 0 ? "; " + string.Join(" | ", wrong.ToArray()) : ""));
            checks.Add("PASS the screensaver mod puts up a window per screen, borderless, off the taskbar and layered, and each frame of it is drawn");
        }
#endif

        // The screensaver mod's fade, read from the mod itself: up over five seconds rather than cut to black, and at
        // once with reduced motion.
#if CHECKS
        private void SmokeSaverFade(List<string> checks)
        {
            ICapriScreensaver mod = SaverMod;
            if (mod == null) { checks.Add("LIMIT the screensaver mod isn't running here, so its fade wasn't checked"); return; }
            Type window = mod.GetType().Assembly.GetType("SaverWindow");
            System.Reflection.MethodInfo check = window == null ? null : window.GetMethod("FadeCheck", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (check == null) throw new Exception("The screensaver mod has no fade to check.");
            try { check.Invoke(null, new object[] { checks }); }
            catch (System.Reflection.TargetInvocationException error) { throw error.InnerException ?? error; }
        }
#endif
    
#if CHECKS
        // The screensavers' own switch: off with every look on, none can run and each look keeps its switch; on again, the
        // looks are exactly as they were; her settings tool's off is the same switch.
        private void SmokeSaverMaster(System.Collections.Generic.List<string> checks)
        {
            string looksWas = preferences.SaverLooks;
            bool allWas = preferences.SaverAllOff;
            System.Collections.Generic.List<string> wrong = new System.Collections.Generic.List<string>();
            try
            {
                preferences.SaverAllOff = false;
                foreach (string look in SaverLook.Styles) SetSaverLook(look, true);
                string allOn = preferences.SaverLooks;
                SetSaversAllowed(false);
                if (SysLines.Saver || ScreensaverOn || preferences.SaverLooks != allOn) wrong.Add("off: can run " + SysLines.Saver + ", looks " + preferences.SaverLooks + " for " + allOn);
                SetSaversAllowed(true);
                if (!SysLines.Saver || preferences.SaverLooks != allOn) wrong.Add("on again: can run " + SysLines.Saver + ", looks " + preferences.SaverLooks + " for " + allOn);
                SetScreensaver(false);
                if (SysLines.Saver || preferences.SaverLooks != allOn || SaversAllowed) wrong.Add("her settings tool's off changed the looks: " + preferences.SaverLooks);
                SetScreensaver(true);
                if (!SysLines.Saver || preferences.SaverLooks != allOn) wrong.Add("her settings tool's on: " + preferences.SaverLooks);
                System.Windows.Forms.ToolStripMenuItem first = saverMenu.DropDownItems.Count > 0 ? saverMenu.DropDownItems[0] as System.Windows.Forms.ToolStripMenuItem : null;
                if (first == null || (first.Tag as string) != "savers:all") wrong.Add("Screensavers isn't the first switch in Toggles' Screensaver");
                // One row for the group: Screensavers' own > opens its settings, and no other row in the fold has a lit key.
                string rows = mainframe.ToggleFoldRows("Screensaver");
                bool second = Array.Exists(rows.Split('|'), one => one.Trim().TrimEnd('>') == "Screensaver");
                if (!rows.StartsWith("Screensavers>", StringComparison.Ordinal) || second || !mainframe.OpenToggleRowSettings("Screensavers")) wrong.Add("Toggles' Screensaver isn't one settings row: " + rows);
            }
            finally { preferences.SaverLooks = looksWas; preferences.SaverAllOff = allWas; SavePreferences(); }
            if (wrong.Count > 0) throw new Exception("The screensavers' own switch went wrong: " + string.Join("; ", wrong.ToArray()));
            checks.Add("PASS Screensavers, first in Toggles' Screensaver: off with every look on, none can run and each look keeps its switch; on again, the looks are exactly as they were; her settings tool's off and on are the same switch");
        }

        // The intros' own switch, as the screensavers': off with every intro on, none plays and each keeps its switch; on
        // again, they are exactly as they were.
        private void SmokeIntroMaster(System.Collections.Generic.List<string> checks)
        {
            string introsWas = preferences.Intros;
            bool allWas = preferences.IntrosAllOff;
            System.Collections.Generic.List<string> wrong = new System.Collections.Generic.List<string>();
            try
            {
                preferences.IntrosAllOff = false;
                foreach (string intro in SysLines.IntroNames) SetIntro(intro, true);
                string allOn = preferences.Intros;
                SetIntrosAllowed(false);
                if (SysLines.IntrosAllowed() || (ModList != null && ModList.Intro != null) || preferences.Intros != allOn) wrong.Add("off: allowed " + SysLines.IntrosAllowed() + ", an intro picked " + (ModList != null && ModList.Intro != null) + ", intros " + preferences.Intros + " for " + allOn);
                SetIntrosAllowed(true);
                if (!SysLines.IntrosAllowed() || preferences.Intros != allOn) wrong.Add("on again: intros " + preferences.Intros + " for " + allOn);
                System.Windows.Forms.ToolStripMenuItem first = introsMenu.DropDownItems.Count > 0 ? introsMenu.DropDownItems[0] as System.Windows.Forms.ToolStripMenuItem : null;
                if (first == null || (first.Tag as string) != "intros:all") wrong.Add("Intros isn't the first switch in Toggles' Intros");
                // No second settings row beside Intros: any intro sliders sit behind its own >.
                string rows = mainframe.ToggleFoldRows("Intros");
                if (!rows.StartsWith("Intros", StringComparison.Ordinal) || rows.Split('>').Length - 1 > 1) wrong.Add("Toggles' Intros has more than one settings row: " + rows);
            }
            finally { preferences.Intros = introsWas; preferences.IntrosAllOff = allWas; SavePreferences(); }
            if (wrong.Count > 0) throw new Exception("The intros' own switch went wrong: " + string.Join("; ", wrong.ToArray()));
            checks.Add("PASS Intros, first in Toggles' Intros: off with every intro on, none plays and each keeps its switch; on again, they are exactly as they were");
        }
#endif
}
}
