using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Three Ctrl clicks within 1.5 s start a counter run, a reference to Gunpoint: she stays put and takes it, nothing
    // moves her mood, and the count climbs large on the screen with its lines under it, never in her bubble.
    internal sealed partial class Companion
    {
        internal const double StreakStarts = 1.5, StreakEnds = 2;
        private readonly List<double> streakClicks = new List<double>();
        private Mood.Kept streakMood;
        private Personalities.Kept streakSelf;
        private StreakKept streakHers;
        private StreakScreen streakScreen;
        private int streakCount;
        private double streakLast = -1;

        internal bool StreakOn { get { return streakCount > 0; } }
        internal int StreakCount { get { return streakCount; } }
        internal int StreakBest { get { return preferences.StreakBest; } }

        // What a run's first acts moved on her side: the sulk count, the drift timers and the acts' own waits.
        private sealed class StreakKept
        {
            internal double Upset;
            internal int StayedOut;
            internal DateTime DriftEnds;
            internal string DriftFlagged;
            internal double HighFived, Clicked;
            internal bool Sulking;
        }

        // A Ctrl click, or any click during a run. True when the run took it, so no act happens and she isn't picked up.
        private bool StreakClick(double now)
        {
            if (StreakOn)
            {
                streakCount++;
                streakLast = now;
                ShowStreak();
                return true;
            }
            streakClicks.RemoveAll(at => now - at > StreakStarts);
            // Her mood and timers as they were before the first click that could start a run.
            if (streakClicks.Count == 0) KeepStreak();
            streakClicks.Add(now);
            if (streakClicks.Count < 3) return false;
            streakClicks.Clear();
            PutStreakBack();
            streakCount = 3;
            streakLast = now;
            CloseTalk();
            Brain.Log("mood", -1, "A counter run started: the two acts before it are undone and her mood is held as it was.");
            ShowStreak();
            return true;
        }

        // Two seconds without a click ends it as if none of it happened, and a new best is kept.
        private void CheckStreak(double now)
        {
            if (!StreakOn || now - streakLast < StreakEnds) return;
            int count = streakCount;
            streakCount = 0;
            PutStreakBack();
            if (count > preferences.StreakBest) { preferences.StreakBest = count; SavePreferences(); }
            streakEndedAt = now;
            Brain.Log("mood", -1, "A counter run ended at " + count + " (best " + preferences.StreakBest + "); her mood and timers are as they were before it.");
            if (streakScreen != null) streakScreen.Hide();
        }

        private void KeepStreak()
        {
            streakMood = Mood.Keep();
            streakSelf = Personalities.Keep();
            streakHers = new StreakKept { Upset = upset, StayedOut = stayedOut, DriftEnds = driftWindowEnds, DriftFlagged = driftFlagged, HighFived = highFivedAt, Clicked = clickedAt, Sulking = Sulking };
        }

        private void PutStreakBack()
        {
            Mood.PutBack(streakMood);
            if (Personalities.PutBack(streakSelf)) ApplySwitches();
            StreakKept kept = streakHers;
            if (kept == null) return;
            upset = kept.Upset;
            stayedOut = kept.StayedOut;
            driftWindowEnds = kept.DriftEnds;
            driftFlagged = kept.DriftFlagged;
            highFivedAt = kept.HighFived;
            clickedAt = kept.Clicked;
            if (!kept.Sulking && Sulking) Forgive("");
        }

        // The count and its line on the screen she's on, with the best so far once there is one.
        private void ShowStreak()
        {
            if (streakScreen == null || streakScreen.IsDisposed) streakScreen = new StreakScreen();
            Rectangle area = Screen.FromControl(this).Bounds;
            int best = preferences.StreakBest;
            streakScreen.Show(area, streakCount, StreakLine(streakCount), best > 0 ? "Best: " + Math.Max(best, streakCount) : null);
        }

        // The line under the count: the last of lines.md's counter run whose count has come, with %count% filled in.
        internal static string StreakLine(int count)
        {
            string line = null;
            foreach (KeyValuePair<int, string> one in Sayings.CountedLines()) if (one.Key <= count) line = one.Value;
            return line == null ? null : line.Replace("%count%", count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        // After a run, her next reply in a later chat gets one callback to it, before or after (lines.md).
        internal const double CallbackAfter = 120;
        private double streakEndedAt = double.NaN;

        private string StreakCallback(int ticket, string said)
        {
            if (ticket != brainTicket || double.IsNaN(streakEndedAt) || string.IsNullOrWhiteSpace(said) || clock.Elapsed.TotalSeconds - streakEndedAt < CallbackAfter) return said;
            streakEndedAt = double.NaN;
            bool opens = random.Next(2) == 0;
            string line = Sayings.CountedCallback(opens);
            if (string.IsNullOrEmpty(line)) return said;
            return opens ? Own(line) + " " + said : said + " " + Own(line);
        }

        // Its lines at their counts; slow clicks are ordinary acts; three runs in a row, in other moods, each put her mood,
        // tallies and timers back, keep her where she is, show only on the screen, keep the best and leave one callback.
#if CHECKS
        private void SmokeStreak(List<string> checks)
        {
            List<KeyValuePair<int, string>> lines = Sayings.CountedLines();
            int[] at = { 30, 37, 43, 50, 70, 86, 102 };
            bool counted = lines.Count == at.Length && StreakLine(29) == null && StreakLine(30) == "It, er, only takes one punch to knock them out."
                && StreakLine(120) == "Because that is among the worst reasons to punch someone in the face 120 times.";
            for (int i = 0; counted && i < at.Length; i++) counted = lines[i].Key == at[i];
            if (!counted) throw new Exception("The counter run's lines didn't read at their counts: " + lines.Count + ", " + StreakLine(30));
            int bestWas = preferences.StreakBest;
            string attitudeWas = Personalities.Attitude;
            Mood.Kept moodWas = Mood.Keep();
            bool silentWas = preferences.Silent;
            Point placeWas = Location;
            TimeSpan offsetWas = SimTime.Offset;
            try
            {
                preferences.Silent = true;
                preferences.StreakBest = 0;
                double t = clock.Elapsed.TotalSeconds + 100;
                // Past the hour, so praise from checks before this one has left her act limits (an hourly cap on praise)
                // and the slow clicks count as the acts they are.
                SimTime.Advance(TimeSpan.FromHours(1.1));
                Mood.Feel(10, 0);
                Mood.Kept slowFrom = Mood.Keep();
                foreach (double gap in new[] { 0.0, 0.8, 1.6 }) if (!StreakClick(t + gap)) HighFive(t + gap);
                bool slow = !StreakOn && Mood.Keep().Warmth != slowFrom.Warmth;
                t += 10;
                string[] moods = { "", "Moody", "Playful" };
                int[] reach = { 10, 25, 40 };
                string seen = "";
                for (int run = 0; run < moods.Length; run++)
                {
                    Personalities.Attitude = moods[run];
                    Mood.Feel(5 + run * 10, run * 4);
                    Mood.Kept from = Mood.Keep();
                    Personalities.Kept self = Personalities.Keep();
                    Point place = Location;
                    CloseTalk();
                    for (int click = 0; click < 3; click++) if (!StreakClick(t + click * 0.65)) HighFive(t + click * 0.65);
                    bool started = StreakOn && StreakCount == 3;
                    for (int more = 3; more < reach[run]; more++) StreakClick(t + 1.3 + more * 0.2);
                    double last = t + 1.3 + (reach[run] - 1) * 0.2;
                    string shown = streakScreen == null ? "" : streakScreen.Shown;
                    bool bubble = speech == null && !box.Visible;
                    CheckStreak(last + 1.5);
                    bool held = StreakOn;
                    CheckStreak(last + StreakEnds + 0.1);
                    Mood.Kept after = Mood.Keep();
                    bool back = !StreakOn && Math.Abs(after.Warmth - from.Warmth) < 0.01 && Math.Abs(after.Sore - from.Sore) < 0.01
                        && Personalities.Keep().Tally.Count == self.Tally.Count && Personalities.Attitude == moods[run] && Location == place;
                    bool best = preferences.StreakBest == reach[run] && (streakScreen == null || !streakScreen.Visible);
                    bool scored = run == 0 ? shown.IndexOf("Best", StringComparison.Ordinal) < 0 : shown.IndexOf("Best: " + reach[run - 1], StringComparison.Ordinal) >= 0 || shown.IndexOf("Best: " + reach[run], StringComparison.Ordinal) >= 0;
                    if (!started || !held || !back || !best || !bubble || !scored)
                        throw new Exception("Counter run " + (run + 1) + " in " + (moods[run].Length == 0 ? "her own mood" : moods[run]) + " went wrong: started " + started + ", held " + held + ", put back " + back + ", best " + best + " (" + preferences.StreakBest + "), bubble clear " + bubble + ", shown " + shown);
                    seen += (run == 0 ? "" : " / ") + shown;
                    t += 20;
                }
                streakEndedAt = clock.Elapsed.TotalSeconds - CallbackAfter - 1;
                string called = StreakCallback(brainTicket, "Anyway, hi."), again = StreakCallback(brainTicket, "Anyway, hi.");
                if (!slow || called == "Anyway, hi." || again != "Anyway, hi.")
                    throw new Exception("Slow clicks weren't ordinary acts, or the callback didn't come once: slow " + slow + ", " + called + " / " + again);
                checks.Add("PASS counter run: three Ctrl clicks within 1.5 s start it and slower ones stay ordinary acts; three runs in a row, in her own mood, Moody and Playful, each held her mood, tallies and place, showed only on the screen (" + seen + "), ended 2 s after the last click as if none of it happened, kept the best as a high score, and left one callback for a later reply");
            }
            finally
            {
                if (streakCount > 0) { streakCount = 0; PutStreakBack(); }
                if (streakScreen != null) streakScreen.Hide();
                streakEndedAt = double.NaN;
                streakClicks.Clear();
                preferences.StreakBest = bestWas;
                preferences.Silent = silentWas;
                Personalities.Attitude = attitudeWas;
                Mood.PutBack(moodWas);
                SimTime.Reset();
                SimTime.Advance(offsetWas);
                SavePreferences();
                CloseTalk();
            }
        }
#endif
    }

    // Large, in the middle of the screen, in the Mainframe's colours. Clicks go straight through it, and it never takes focus.
    internal sealed class StreakScreen : Form
    {
        private int count;
        private string line, best;

        internal StreakScreen()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(255, 1, 1, 1);
            TransparencyKey = BackColor;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams value = base.CreateParams;
                value.ExStyle |= 0x00080000 | 0x00000020 | 0x00000080 | 0x08000000 | 0x00000008;
                return value;
            }
        }

        internal string Shown { get { return Visible ? count + (line == null ? "" : " | " + line) + (best == null ? "" : " | " + best) : ""; } }

        internal void Show(Rectangle area, int now, string said, string top)
        {
            count = now;
            line = said;
            best = top;
            if (Bounds != area) Bounds = area;
            if (!Visible) Show();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            int tall = ClientSize.Height;
            using (Font big = Look.Mono(tall / 6f, FontStyle.Bold))
            using (Font small = Look.Mono(tall / 32f, FontStyle.Regular))
            using (StringFormat middle = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                Rectangle number = new Rectangle(0, tall / 2 - tall / 6, ClientSize.Width, tall / 3);
                Outlined(g, count.ToString(System.Globalization.CultureInfo.InvariantCulture), big, number, middle, Neon.Hot, Neon.Glow);
                if (!string.IsNullOrEmpty(line)) Outlined(g, line, small, new Rectangle(tall / 10, number.Bottom, ClientSize.Width - tall / 5, tall / 8), middle, Neon.White, Neon.Glow);
                if (!string.IsNullOrEmpty(best)) Outlined(g, best, small, new Rectangle(0, number.Top - tall / 10, ClientSize.Width, tall / 12), middle, Neon.Rose, Neon.Glow);
            }
        }

        // Dark-edged so it reads over any picture, drawn in the palette so it follows the Mainframe's colours.
        private static void Outlined(Graphics g, string text, Font font, Rectangle box, StringFormat format, Color fill, Color edge)
        {
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddString(text, font.FontFamily, (int)font.Style, font.Size, box, format);
                using (Pen pen = new Pen(Color.FromArgb(255, Color.Black), Math.Max(4f, font.Size / 12f)) { LineJoin = LineJoin.Round })
                    g.DrawPath(pen, path);
                using (Pen glow = new Pen(Color.FromArgb(255, edge), Math.Max(2f, font.Size / 30f)) { LineJoin = LineJoin.Round })
                    g.DrawPath(glow, path);
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(255, fill))) g.FillPath(brush, path);
            }
        }
    }
}
