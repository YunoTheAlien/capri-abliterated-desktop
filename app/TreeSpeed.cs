using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Companion
    {
#if CHECKS
        // How long her Settings page, Toggles and her menu take to open, at 100 and 150%, the median of five each, written to
        // tree-speed.txt: the numbers one build is held against another's by (a stage of trees lands within 5%).
        private void SmokeTreeSpeed(List<string> checks, string output)
        {
            List<string> lines = new List<string>();
            int sizeWas = LevelOf("mainframe_size");
            Func<Action, Action, double> median = delegate(Action open, Action close)
            {
                List<double> took = new List<double>();
                for (int run = 0; run < 6; run++)
                {
                    close();
                    Application.DoEvents();
                    Stopwatch watch = Stopwatch.StartNew();
                    open();
                    Application.DoEvents();
                    watch.Stop();
                    if (run > 0) took.Add(watch.Elapsed.TotalMilliseconds);
                }
                took.Sort();
                return took[took.Count / 2];
            };
            try
            {
                foreach (int step in new[] { 1, 3 })
                {
                    SetLevel("mainframe_size", step);
                    string zoom = step == 1 ? "100%" : "150%";
                    mainframe.Show();
                    double settings = median(delegate { mainframe.ShowSettings(true); mainframe.Refresh(); }, delegate { mainframe.ShowSettings(false); mainframe.Refresh(); });
                    double toggles = median(delegate { mainframe.OpenToggles(); }, delegate { mainframe.CloseToggles(); });
                    mainframe.CloseToggles();
                    double menuOpen = median(delegate { menu.Show(this, new Point(Width / 2, Height / 2)); menu.Refresh(); }, delegate { menu.Close(); });
                    menu.Close();
                    lines.Add(zoom + "\tsettings " + settings.ToString("0.0") + " ms\ttoggles " + toggles.ToString("0.0") + " ms\tmenu " + menuOpen.ToString("0.0") + " ms");
                }
            }
            finally
            {
                SetLevel("mainframe_size", sizeWas);
                mainframe.ShowSettings(false);
            }
            try { File.WriteAllLines(Path.Combine(output, "tree-speed.txt"), lines.ToArray()); } catch (IOException caughtOne) { Brain.Caught(caughtOne); }
            checks.Add("PASS speed for trees' gate (tree-speed.txt): " + string.Join(" | ", lines.ToArray()).Replace("\t", " "));
        }
#endif
    }
}
