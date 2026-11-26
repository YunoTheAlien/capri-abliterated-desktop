#if CHECKS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Companion
    {
        // Her idle CPU, a share of one core, with the Mainframe closed and then open on Settings: each under the budget.
        internal const double IdleBudget = 5;

        internal void SmokeIdleCpu(List<string> checks)
        {
            bool shownWas = mainframe.Visible;
            FormWindowState stateWas = mainframe.WindowState;
            Func<double> idle = delegate
            {
                // Settled first, then measured over eight seconds while her window thread pumps as it would idle.
                Stopwatch settle = Stopwatch.StartNew();
                while (settle.ElapsedMilliseconds < 3000) { Application.DoEvents(); System.Threading.Thread.Sleep(15); }
                TimeSpan from;
                using (Process self = Process.GetCurrentProcess()) from = self.TotalProcessorTime;
                Stopwatch span = Stopwatch.StartNew();
                while (span.ElapsedMilliseconds < 8000) { Application.DoEvents(); System.Threading.Thread.Sleep(15); }
                TimeSpan to;
                using (Process self = Process.GetCurrentProcess()) to = self.TotalProcessorTime;
                return (to - from).TotalMilliseconds / span.Elapsed.TotalMilliseconds * 100;
            };
            double closed, open;
            try
            {
                mainframe.Hide();
                closed = idle();
                mainframe.Restore();
                mainframe.ShowSettings(true);
                open = idle();
            }
            finally
            {
                if (shownWas) { if (!mainframe.Visible) mainframe.Show(); mainframe.WindowState = stateWas; }
                else mainframe.Hide();
            }
            string said = "closed " + closed.ToString("0.0") + "%, open on Settings " + open.ToString("0.0") + "% of a core; " + Clock.Summary(5);
            Brain.Log("clock", -1, "idle check: " + said);
            if (closed > IdleBudget || open > IdleBudget) throw new Exception("Her idle CPU is over " + IdleBudget + "% of a core: " + said);
            checks.Add("PASS her idle CPU stays under " + IdleBudget + "% of a core: " + said);
        }
    }
}
#endif
