using System;
using System.Linq;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Companion
    {
        // The Tamagotchi's row in Toggles: a slider per need, 1x to 10x, then its pet game's own switches. Its key shows
        // them; nothing here opens Tamagotchi mode or switches the mod.
        internal static class PetRates
        {
            internal static readonly string[] Needs = { "Energy", "Fullness", "Stimulation", "Social", "Comfort", "Cleanliness" };
            internal const int Most = 10;

            internal static ToolStripMenuItem Row(Companion capri, string title)
            {
                ToolStripMenuItem row = new ToolStripMenuItem(title) { Tag = Mainframe.OpensTag, Name = Mainframe.SideRow };
                for (int need = 0; need < Needs.Length; need++)
                    for (int rate = 1; rate <= Most; rate++)
                    {
                        int which = need, times = rate;
                        ToolStripMenuItem step = new ToolStripMenuItem(rate + "x") { Tag = Mainframe.StepTag + Needs[need] + ":" + (rate - 1), Checked = capri.PetRate(need) == rate, ToolTipText = "Increases gain rates by multiplier" };
                        step.Click += delegate { capri.SetPetRate(which, times); Tick(row, capri); };
                        row.DropDownItems.Add(step);
                    }
                row.DropDownOpening += delegate { Tick(row, capri); };
                return row;
            }

            // Each step ticked where her settings have it.
            private static void Tick(ToolStripMenuItem row, Companion capri)
            {
                foreach (ToolStripItem one in row.DropDownItems)
                {
                    ToolStripMenuItem step = one as ToolStripMenuItem;
                    string tag = step == null ? null : step.Tag as string;
                    if (tag == null || !tag.StartsWith(Mainframe.StepTag, StringComparison.Ordinal)) continue;
                    string[] parts = tag.Substring(Mainframe.StepTag.Length).Split(':');
                    int need = Array.IndexOf(Needs, parts[0]), at;
                    if (need >= 0 && parts.Length == 2 && int.TryParse(parts[1], out at)) step.Checked = capri.PetRate(need) == at + 1;
                }
            }
        }

        // A need's rate, 1 to 10; 1 when unset.
        internal int PetRate(int need)
        {
            int[] rates = preferences == null ? null : preferences.PetRates;
            if (rates == null || need < 0 || need >= rates.Length || rates[need] < 1) return 1;
            return Math.Min(PetRates.Most, rates[need]);
        }

#if CHECKS
        // Every switch, key and slider in Toggles pressed and put back: none opens a window (a plug-in's options aside),
        // enters a mode or starts anything. The Tamagotchi's key shows a slider per need.
        private void SmokeTogglesInert(System.Collections.Generic.List<string> checks)
        {
            System.Collections.Generic.HashSet<Form> before = new System.Collections.Generic.HashSet<Form>();
            mainframe.OpenToggles();
            foreach (Form one in Application.OpenForms) before.Add(one);
            Func<string> watch = delegate
            {
                System.Collections.Generic.List<string> windows = new System.Collections.Generic.List<string>();
                foreach (Form one in Application.OpenForms)
                    // A plug-in's options (its cog's window) are what its key is for.
                    if (one.Visible && !before.Contains(one) && !(one is Overlay) && !(one is ToolStripDropDown) && one.GetType().Name != "OverridesSheet") windows.Add(one.GetType().Name + " " + one.Text);
                windows.Sort();
                return "Tamagotchi mode " + mainframe.PetShown + ", docked " + petDocked + ", capture window " + (streamWindow != null && !streamWindow.IsDisposed) + ", new windows [" + string.Join(", ", windows.ToArray()) + "]";
            };
            Func<bool> tidy = delegate
            {
                if (mainframe.PetShown) mainframe.ShowPetMode(false);
                if (streamWindow != null && !streamWindow.IsDisposed) SetStreamWindow();
                foreach (Form one in new System.Collections.Generic.List<Form>(Application.OpenForms.Cast<Form>()))
                    if (!before.Contains(one) && !(one is Overlay) && !(one is ToolStripDropDown)) { try { one.Close(); } catch (Exception caughtOne) { Brain.Caught(caughtOne); } }
                return true;
            };
            System.Collections.Generic.List<string> wrong = mainframe.PressEveryToggle(watch, tidy);
            string pet = null;
            bool running = ModList != null && ModList.PetId != null;
            if (running)
            {
                pet = mainframe.TogglesPetRow() ?? "";
                foreach (string need in PetRates.Needs) if (pet.IndexOf("slider " + need + " ", StringComparison.Ordinal) < 0) wrong.Add("the Tamagotchi's row has no " + need + " slider: " + pet);
                if (mainframe.PetShown) wrong.Add("the Tamagotchi's key opened Tamagotchi mode");
            }
            tidy();
            if (wrong.Count > 0) throw new Exception("Toggles did more than set: " + string.Join("; ", wrong.ToArray()));
            checks.Add("PASS every switch, key and slider in Toggles, pressed and put back, opens no window, enters no mode and starts nothing; only a plug-in's key opens its options");
            checks.Add(running ? "PASS the Tamagotchi's row: its key shows " + pet + ", and never Tamagotchi mode" : "LIMIT the Tamagotchi's row: no pet game on in this folder, so no row to press");
        }
#endif

        private void SetPetRate(int need, int rate)
        {
            int[] rates = new int[PetRates.Needs.Length];
            for (int i = 0; i < rates.Length; i++) rates[i] = PetRate(i);
            rates[need] = Math.Max(1, Math.Min(PetRates.Most, rate));
            preferences.PetRates = rates;
            SavePreferences();
        }
    }
}
