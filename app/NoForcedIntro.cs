#if CHECKS
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace CapriDesktop
{
    // No forced door intros until the new intro is done: after a fresh start, an update or the next start no door is on
    // unless picked, and an intro mod on plays only when its Toggles > Intros switch is.
    internal sealed partial class Companion
    {
        private sealed class NamedIntro : ICapriMod, ICapriIntroNames
        {
            public void Start(ICapriHost capri) { }
            public bool Heard(string words) { return false; }
            public void Stop() { }
            public string[] Intros { get { return new[] { "Door2" }; } }
        }

        internal void SmokeNoForcedIntro(List<string> checks, string output)
        {
            List<string> wrong = new List<string>();
            string keptWas = ModDirectory.KeptFolder;
            Func<string, bool> heardWas = Hands.AlsoHeard;
            Func<string> calledWas = Hands.AlsoCalled;
            string introsWas = preferences.Intros;
            bool allWas = preferences.IntrosAllOff;
            string root = Path.Combine(output, "no-forced-intro");
            Func<string, string, string> doors = delegate(string data, string intros)
            {
                Mods.Stock(data);
                Mods.DoorsOffOnce(data, intros);
                Mods list = new Mods(data);
                List<string> on = new List<string>();
                foreach (string id in new[] { Mods.DoorId, Mods.FrontId })
                {
                    Mod door = list.All.Find(one => one.File.Id == id);
                    if (door == null) return id + " was not laid";
                    if (door.On) on.Add(id);
                }
                return string.Join(",", on.ToArray());
            };
            try
            {
                // A fresh start: neither door on, and again on the next start.
                string fresh = Path.Combine(root, "fresh");
                Directory.CreateDirectory(fresh);
                string first = doors(fresh, null), next = doors(fresh, null);
                if (first != "" || next != "") wrong.Add("fresh start: doors on " + first + ", next start " + next);
                // An update over a build that laid both doors in on: both go off once, and stay as they are after.
                string update = Path.Combine(root, "update");
                Directory.CreateDirectory(update);
                Mods.Stock(update);
                string offFile = Path.Combine(update, "mods", "off.txt");
                File.WriteAllLines(offFile, new List<string>(File.ReadAllLines(offFile)).FindAll(id => id != Mods.DoorId && id != Mods.FrontId).ToArray());
                string updated = doors(update, null);
                if (updated != "") wrong.Add("update: doors left on " + updated);
                File.WriteAllLines(offFile, new List<string>(File.ReadAllLines(offFile)).FindAll(id => id != Mods.FrontId).ToArray());
                string theirs = doors(update, null);
                if (theirs != Mods.FrontId) wrong.Add("after the update, Door 2 switched on by hand didn't stay on: " + theirs);
                // An update where Door 2's intro was picked: Door 2 stays on, door 1 goes off.
                string picked = Path.Combine(root, "picked");
                Directory.CreateDirectory(picked);
                Mods.Stock(picked);
                string pickedOff = Path.Combine(picked, "mods", "off.txt");
                File.WriteAllLines(pickedOff, new List<string>(File.ReadAllLines(pickedOff)).FindAll(id => id != Mods.DoorId && id != Mods.FrontId).ToArray());
                string kept = doors(picked, "shatter,door2");
                if (kept != Mods.FrontId) wrong.Add("update with Door 2 picked: doors on " + kept + ", not door2-intro alone");
                // An intro mod on is only a choice for a start: none of its intros picked, it has no share.
                NamedIntro named = new NamedIntro();
                preferences.IntrosAllOff = false;
                preferences.Intros = "";
                int none = Mods.IntroShares(named);
                preferences.Intros = "door2";
                int one = Mods.IntroShares(named);
                if (none != 0 || one != 1) wrong.Add("an intro mod's share: " + none + " with nothing picked, " + one + " with Door 2 picked");
                if (ModList.StartIntro != ModList.Intro) wrong.Add("a start's intro isn't the Toggles pick");
            }
            finally
            {
                preferences.Intros = introsWas;
                preferences.IntrosAllOff = allWas;
                ModDirectory.KeptFolder = keptWas;
                Hands.AlsoHeard = heardWas;
                Hands.AlsoCalled = calledWas;
            }
            if (wrong.Count > 0) throw new Exception("No forced door intros until the new intro is done: " + string.Join("; ", wrong.ToArray()));
            checks.Add("PASS no forced door intros until the new intro is done: neither door is on after a fresh start or the next, an update over doors laid on turns both off once and leaves a door switched on after that alone, a picked Door 2 stays on, and an intro mod with none of its intros picked has no share of a start");
        }
    }
}
#endif
