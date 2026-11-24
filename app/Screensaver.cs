using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Her screensaver's looks by name, and what sys.md says about how it shows, read once when it opens so a half-edited
    // file never lands on screen mid-change. The screensaver itself is a mod of its own, capri-screensaver.
    internal static class SaverLook
    {
        internal const string Terminals = "terminals", Code = "code", Rain = "rain", Hexes = "hexes";
        internal static readonly string[] Styles = { Terminals, Hexes, Code, Rain };

        // Her ask for one showing: the look asked for, or the one set in Toggles, with sys.md's size, speed, see through,
        // screens and flashing, and the sound only when it was asked for.
        internal static CapriSaverAsk Ask(string asked, bool sound)
        {
            CapriSaverAsk ask = new CapriSaverAsk();
            string style = (asked ?? SysLines.SaverStyleSaved() ?? "").Trim();
            ask.Look = Terminals;
            foreach (string one in Styles) if (style.Equals(one, StringComparison.OrdinalIgnoreCase)) ask.Look = one;
            ask.Size = Step(SysLines.Of(SysLines.SaverSizeKey), "small", 0.72f, "large", 1.5f, 0.4f, 3f);
            ask.Speed = Step(SysLines.Of(SysLines.SaverSpeedKey), "slow", 0.45f, "fast", 2.4f, 0.2f, 6f);
            ask.Solid = Clear(SysLines.Of(SysLines.SaverThroughKey));
            ask.EveryScreen = !string.Equals((SysLines.Of(SysLines.SaverScreensKey) ?? "").Trim(), "main", StringComparison.OrdinalIgnoreCase);
            string flash = (SysLines.Of(SysLines.SaverFlashKey) ?? "").Trim();
            ask.Flash = flash.Equals("off", StringComparison.OrdinalIgnoreCase) || flash.Equals("0", StringComparison.Ordinal) ? 0f
                : flash.Equals("gentle", StringComparison.OrdinalIgnoreCase) || flash.Equals("slow", StringComparison.OrdinalIgnoreCase) ? 0.3f : 1f;
            // Its own share of her sound volume, since a loop running the whole time it is up wants far less than a chime.
            ask.Sound = sound ? SysLines.SaverSound : null;
            ask.Volume = sound && Chimes.On ? Chimes.Volume * SysLines.SaverVolume / 100 : 0;
            return ask;
        }

        // A word they know, or a plain number for anyone who wants to be exact. Anything else is the middle.
        private static float Step(string said, string low, float lowAt, string high, float highAt, float least, float most)
        {
            string one = (said ?? "").Trim();
            if (one.Length == 0) return 1f;
            if (one.Equals(low, StringComparison.OrdinalIgnoreCase)) return lowAt;
            if (one.Equals(high, StringComparison.OrdinalIgnoreCase)) return highAt;
            if (one.Equals("normal", StringComparison.OrdinalIgnoreCase) || one.Equals("medium", StringComparison.OrdinalIgnoreCase)) return 1f;
            float made;
            if (!float.TryParse(one, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out made)) return 1f;
            return Math.Max(least, Math.Min(most, made));
        }

        // How much of the desktop shows through, 0 to 100, as the solidity the layered window wants.
        private static byte Clear(string said)
        {
            int through;
            if (!int.TryParse((said ?? "").Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out through)) return 255;
            through = Math.Max(0, Math.Min(100, through));
            return (byte)Math.Max(30, 255 - through * 225 / 100);
        }
    }

    internal sealed partial class Companion
    {
        private double saverCheckedAt;

        // The screensaver mod running now, or null when it is off or missing.
        private ICapriScreensaver SaverMod { get { return ModList == null ? null : ModList.Screensaver; } }

        private bool SaverShowing { get { ICapriScreensaver mod = SaverMod; return mod != null && mod.Showing; } }

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
            Brain.Log("screensaver", -1, "Asked for from her menu (" + (style ?? "as set in Toggles") + "), with the switch " + (ScreensaverOn ? "on" : "off") + ", the mod " + (SaverMod == null ? "off" : "on") + ".");
            if (SaverMod == null) { Remark("", "My screensaver is a mod now, and it's off. Turn it on in Mods.", true); return; }
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

        // red_alert: her hexagon alert with the alarm, in hexes whatever style was chosen, for six seconds, ending on
        // any key and undoing itself. It is drawn by the screensaver mod; what happens when that is off is decided here.
        private const double AlertSeconds = 6;
        private bool bullySaver;

        private string AlertTool(string action)
        {
            if (InvokeRequired) return (string)Invoke(new Func<string, string>(AlertTool), action);
            if (action == "stop") { SaverOff(); return "Stopped my red alert."; }
            if (SaverMod == null) return "My red alert needs the Screensaver mod on, under Mods.";
            if (SaverShowing) return "My red alert is already up.";
            if (quitting || SaverHeld()) return "I can't show that during full screen, a stream or a mouse job.";
            return ShowSaver(SaverLook.Hexes, 1.4, true, true, AlertSeconds) ? "Red alert." : "Couldn't show my red alert.";
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
            ICapriScreensaver mod = SaverMod;
            if (mod == null) return;
            try { mod.Hide(); }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // The screensaver mod's fade, read from the mod itself: up over five seconds rather than cut to black, and at
        // once with reduced motion.
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
    }
}
