using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Her chattiness with Twitch chat: every 20 s, or her pick of 10 s to a minute, she takes one line that came in, one
    // calling her first, to answer or read; the rest pass. Read all chat aloud reads every message in order.
    internal sealed partial class Companion
    {
        internal static readonly int[] TwitchPaces = { 10, 15, 20, 30, 45, 60 };
        internal const int TwitchPaceDefault = 20;
        private const string TwitchRead = "twitch read";
        private const string TwitchPaceSlider = "Read every";

        // A chat line waiting for her next pick: one to answer (Read chat) or words to read (Read chat aloud).
        private sealed class PacedLine
        {
            internal TwitchLine Line;
            internal string Aloud;
            internal bool Calls;
        }

        private readonly List<PacedLine> paced = new List<PacedLine>();
        // Lines picked to read aloud, and every line under Read all chat aloud, oldest first.
        private readonly Queue<string> aloudReady = new Queue<string>();
        internal int TwitchPulls;
        // For the checks: a pick's wait in milliseconds per second of pace, so a minute's span plays in a moment; and each
        // line picked as it comes, for checks of what a reply does rather than when.
        internal static double TwitchPaceMs = 1000;
        internal static bool PickAtOnce;

        internal int TwitchPace
        {
            get
            {
                int picked = preferences == null ? 0 : preferences.TwitchReadSeconds;
                return Array.IndexOf(TwitchPaces, picked) >= 0 ? picked : TwitchPaceDefault;
            }
        }

        internal void SetTwitchPace(int seconds)
        {
            if (Array.IndexOf(TwitchPaces, seconds) < 0) return;
            preferences.TwitchReadSeconds = seconds;
            SavePreferences();
            Clock.Retime(TwitchRead, TimeSpan.FromMilliseconds(seconds * TwitchPaceMs));
        }

        // Its row under Twitch: a slider of paces, in Toggles' Twitch fold behind the row's >.
        private ToolStripMenuItem TwitchPaceRow()
        {
            ToolStripMenuItem row = new ToolStripMenuItem("Chat pace") { ToolTipText = "How often she picks one chat line to answer or read; Read all chat aloud reads every one" };
            for (int i = 0; i < TwitchPaces.Length; i++)
            {
                int seconds = TwitchPaces[i];
                ToolStripMenuItem step = new ToolStripMenuItem(seconds < 60 ? seconds + " s" : "1 min") { Tag = Mainframe.StepTag + TwitchPaceSlider + ":" + i };
                step.Click += delegate { SetTwitchPace(seconds); TickTwitchPace(row); };
                row.DropDownItems.Add(step);
            }
            row.DropDownOpening += delegate { TickTwitchPace(row); };
            TickTwitchPace(row);
            return row;
        }

        private void TickTwitchPace(ToolStripMenuItem row)
        {
            int at = Array.IndexOf(TwitchPaces, TwitchPace);
            for (int i = 0; i < row.DropDownItems.Count; i++)
            {
                ToolStripMenuItem step = row.DropDownItems[i] as ToolStripMenuItem;
                if (step != null) step.Checked = i == at;
            }
        }

        // Chat open: the pick runs at its pace. Closed: it stops, and nothing waits.
        private void TwitchGathering(bool on)
        {
            if (on) Clock.Every(TwitchRead, TimeSpan.FromMilliseconds(TwitchPace * TwitchPaceMs), TwitchGather);
            else Clock.Stop(TwitchRead);
            lock (aloudGate) paced.Clear();
        }

        // A line for her next pick. Under Read all chat aloud it skips the pick and waits its turn to be read.
        private void Paced(TwitchLine line, string aloud)
        {
            if (aloud != null && twitchAloudAll) { lock (aloudGate) aloudReady.Enqueue(aloud); return; }
            string plain = (line == null ? "" : line.Text ?? "").Trim().ToLowerInvariant();
            bool calls = plain.StartsWith(TwitchNow().Callout, StringComparison.Ordinal);
            lock (aloudGate) paced.Add(new PacedLine { Line = line, Aloud = aloud, Calls = calls });
            if (PickAtOnce) TwitchGather();
        }

        // Her pick: the newest line calling her, else the newest line; the others since the last pick pass.
        private void TwitchGather()
        {
            TwitchPulls++;
            PacedLine pick = null;
            int passed;
            lock (aloudGate)
            {
                for (int i = paced.Count - 1; i >= 0 && pick == null; i--) if (paced[i].Calls) pick = paced[i];
                if (pick == null && paced.Count > 0) pick = paced[paced.Count - 1];
                passed = Math.Max(0, paced.Count - 1);
                paced.Clear();
                if (pick != null && pick.Aloud != null) aloudReady.Enqueue(pick.Aloud);
            }
            if (passed > 0) Brain.Log("twitch", -1, "Picked one chat line of " + (passed + 1) + "; the rest passed.");
            if (pick != null && pick.Aloud == null && pick.Line != null) TwitchHeard(pick.Line);
        }

        // For the checks: what waits to be read aloud, picked or not, oldest first.
        internal string[] AloudWaiting()
        {
            List<string> all = new List<string>();
            lock (aloudGate)
            {
                all.AddRange(aloudReady);
                foreach (PacedLine one in paced) if (one.Aloud != null) all.Add(one.Aloud);
            }
            return all.ToArray();
        }

#if CHECKS
        // Picks counted over a played 3 minutes at 20 s and at 1 min; one line picked of many, a line calling her first;
        // Read all chat aloud keeps every line in order.
        internal void SmokeTwitchPace(List<string> checks)
        {
            double msWas = TwitchPaceMs;
            int paceWas = preferences.TwitchReadSeconds;
            bool aloudWas = twitchAloud, allWas = twitchAloudAll;
            List<string> wrong = new List<string>();
            try
            {
                TwitchPaceMs = 20;
                twitchAloud = true;
                twitchAloudAll = false;
                foreach (int pace in new[] { 20, 60 })
                {
                    preferences.TwitchReadSeconds = pace;
                    lock (aloudGate) aloudReady.Clear();
                    TwitchPulls = 0;
                    TwitchGathering(true);
                    System.Diagnostics.Stopwatch played = System.Diagnostics.Stopwatch.StartNew();
                    while (played.Elapsed.TotalMilliseconds < 180 * TwitchPaceMs) { Application.DoEvents(); System.Threading.Thread.Sleep(5); }
                    TwitchGathering(false);
                    int wanted = 180 / pace;
                    if (Math.Abs(TwitchPulls - wanted) > 1) wrong.Add(pace + " s pace picked " + TwitchPulls + " times over 3 minutes, not " + wanted);
                }
                lock (aloudGate) aloudReady.Clear();
                string callout = TwitchNow().Callout;
                Paced(new TwitchLine { Login = "a", Text = "first" }, "a: first");
                Paced(new TwitchLine { Login = "b", Text = callout + " hi" }, "b: " + callout + " hi");
                Paced(new TwitchLine { Login = "c", Text = "last" }, "c: last");
                TwitchGather();
                string picked = string.Join(",", AloudWaiting());
                if (picked != "b: " + callout + " hi") wrong.Add("of three lines, one calling her, the pick was: " + picked);
                lock (aloudGate) aloudReady.Clear();
                Paced(new TwitchLine { Login = "a", Text = "first" }, "a: first");
                Paced(new TwitchLine { Login = "c", Text = "last" }, "c: last");
                TwitchGather();
                picked = string.Join(",", AloudWaiting());
                if (picked != "c: last") wrong.Add("of two lines, the pick wasn't the newest: " + picked);
                lock (aloudGate) aloudReady.Clear();
                twitchAloudAll = true;
                for (int i = 1; i <= 8; i++) Paced(new TwitchLine { Login = "v", Text = "line " + i }, "line " + i);
                string all = string.Join(",", AloudWaiting());
                if (all != "line 1,line 2,line 3,line 4,line 5,line 6,line 7,line 8") wrong.Add("Read all chat aloud didn't keep every line in order: " + all);
            }
            finally
            {
                TwitchGathering(false);
                TwitchPaceMs = msWas;
                preferences.TwitchReadSeconds = paceWas;
                twitchAloud = aloudWas;
                twitchAloudAll = allWas;
                lock (aloudGate) aloudReady.Clear();
            }
            if (wrong.Count > 0) throw new Exception("Twitch pace: " + string.Join("; ", wrong.ToArray()));
            checks.Add("PASS her chat pace picks one line 9 times over 3 minutes at 20 s and 3 at 1 min, a line calling her first, else the newest; Read all chat aloud keeps every line in order");
        }
#endif
    }
}
