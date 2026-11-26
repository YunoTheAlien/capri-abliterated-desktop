using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Her scan as she lands: a dim cyan ring out across every screen, a grid fading behind it over half a second. One
    // click-through window a screen, each keeping one picture and drawing it again in place.
    internal sealed class ScanWave
    {
        // The ring reaches the farthest corner of every screen in this long, and the grid lasts this long behind it.
        internal const double Across = 1.2, Trail = 0.5;
        internal const int Cell = 32, Bands = 6, RingMost = 90, GridMost = 70, Brightest = 180;
        private readonly List<Overlay> windows = new List<Overlay>();
        private readonly List<Bitmap> pictures = new List<Bitmap>();
        private readonly List<Rectangle> screens = new List<Rectangle>();
        private readonly Clock.Frames ticks = new Clock.Frames("scan wave") { Interval = 33 };
        private readonly Stopwatch watch = new Stopwatch();
        private Point origin;
        private double speed, length;
        // For the checks: the time it has run, in place of its own watch.
        internal Func<double> Clock;

        internal ScanWave() { ticks.Tick += delegate { Step(); }; }

        internal bool Running { get { return ticks.Enabled; } }

        internal int Shown { get { int up = 0; foreach (Overlay window in windows) if (window.Visible) up++; return up; } }

        internal double Length { get { return length; } }

        internal void Start(Point from, Rectangle[] on)
        {
            Stop();
            origin = from;
            screens.Clear();
            screens.AddRange(on);
            double far = 1;
            foreach (Rectangle screen in on) far = Math.Max(far, Farthest(screen, from));
            speed = far / Across;
            length = Across + Trail;
            while (windows.Count < screens.Count) windows.Add(new Overlay("Capri scan") { ClickThrough = true });
            watch.Reset();
            watch.Start();
            Step();
            ticks.Start();
        }

        internal void Stop()
        {
            ticks.Stop();
            foreach (Overlay window in windows) if (window.Visible) window.Hide();
            foreach (Bitmap picture in pictures) if (picture != null) picture.Dispose();
            pictures.Clear();
            foreach (Overlay window in windows) window.Forget();
        }

        private void Step()
        {
            double t = Clock != null ? Clock() : watch.Elapsed.TotalSeconds;
            if (t >= length) { Stop(); return; }
            while (pictures.Count < screens.Count) pictures.Add(null);
            for (int i = 0; i < screens.Count; i++)
            {
                Rectangle screen = screens[i];
                if (!Meets(screen, origin, speed * t, speed * Trail)) { if (windows[i].Visible) windows[i].Hide(); continue; }
                if (pictures[i] == null) pictures[i] = new Bitmap(screen.Width, screen.Height, PixelFormat.Format32bppPArgb);
                Draw(pictures[i], screen, origin, speed * t, speed * Trail, Neon.Cyan);
                try { windows[i].Present(pictures[i], screen.Location); }
                catch (Exception caughtOne) { Brain.Caught(caughtOne); }
            }
        }

        private static double Farthest(Rectangle screen, Point from)
        {
            double most = 0;
            foreach (Point corner in new[] { screen.Location, new Point(screen.Right, screen.Top), new Point(screen.Left, screen.Bottom), new Point(screen.Right, screen.Bottom) })
                most = Math.Max(most, Math.Sqrt(Math.Pow(corner.X - from.X, 2) + Math.Pow(corner.Y - from.Y, 2)));
            return most;
        }

        private static double Nearest(Rectangle screen, Point from)
        {
            double dx = Math.Max(0, Math.Max(screen.Left - from.X, from.X - screen.Right)), dy = Math.Max(0, Math.Max(screen.Top - from.Y, from.Y - screen.Bottom));
            return Math.Sqrt(dx * dx + dy * dy);
        }

        // Whether the ring or the grid behind it is on this screen now.
        internal static bool Meets(Rectangle screen, Point from, double radius, double trail)
        {
            return radius + RingWidth >= Nearest(screen, from) && radius - trail <= Farthest(screen, from);
        }

        private const float RingWidth = 16f;

        // One screen's picture: the ring at its radius and the grid in bands behind it, each band fainter than the one before.
        internal static void Draw(Bitmap picture, Rectangle screen, Point from, double radius, double trail, Color ink)
        {
            using (Graphics graphics = Graphics.FromImage(picture))
            {
                graphics.Clear(Color.Transparent);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float x = from.X - screen.X, y = from.Y - screen.Y;
                for (int band = 0; band < Bands; band++)
                {
                    float outer = (float)(radius - trail * band / Bands), inner = (float)(radius - trail * (band + 1) / Bands);
                    if (outer <= 0) break;
                    int alpha = (int)(GridMost * Math.Pow(1 - (band + 0.5) / Bands, 1.4));
                    using (GraphicsPath ring = new GraphicsPath(FillMode.Alternate))
                    using (Pen line = new Pen(Color.FromArgb(alpha, ink), 1f))
                    {
                        ring.AddEllipse(x - outer, y - outer, outer * 2, outer * 2);
                        if (inner > 0) ring.AddEllipse(x - inner, y - inner, inner * 2, inner * 2);
                        graphics.SetClip(ring);
                        RectangleF box = ring.GetBounds();
                        int left = (int)Math.Max(0, box.Left), right = (int)Math.Min(screen.Width, box.Right), top = (int)Math.Max(0, box.Top), bottom = (int)Math.Min(screen.Height, box.Bottom);
                        for (int across = left - left % Cell; across <= right; across += Cell) graphics.DrawLine(line, across, top, across, bottom);
                        for (int down = top - top % Cell; down <= bottom; down += Cell) graphics.DrawLine(line, left, down, right, down);
                        graphics.ResetClip();
                    }
                }
                if (radius <= 0) return;
                float r = (float)radius;
                using (Pen soft = new Pen(Color.FromArgb(RingMost / 3, ink), RingWidth))
                using (Pen ring = new Pen(Color.FromArgb(RingMost, ink), RingWidth / 3))
                {
                    graphics.DrawEllipse(soft, x - r, y - r, r * 2, r * 2);
                    graphics.DrawEllipse(ring, x - r, y - r, r * 2, r * 2);
                }
            }
        }
    }

    internal sealed partial class Companion
    {
        // Where her scan plays: as she starts, on Show Capri and on Bring here.
        internal static readonly string[] ScanLandings = { "start", "show", "bring" };
        private ScanWave scanWave;
        // For the checks: lets her scan run in a test, where nothing else of hers starts one.
        private bool scanChecking;

        // Her scan from where she landed: never with Motion off, hidden, over a full-screen game or Gaming Mode, or while an
        // intro shows or is held for, so after an intro it plays as she lands.
        private bool ScanLanded(string landing)
        {
            if (Array.IndexOf(ScanLandings, landing) < 0 || preferences.NoScanWave || !preferences.Motion || hidden || docked) return false;
            if (testing && !scanChecking) return false;
            if (IntroShowing(clock.Elapsed.TotalSeconds) || FullScreen()) return false;
            Rectangle body = BodyOnScreen(anchor, seat != null || riding || onPanel ? PerchedBody : Body);
            List<Rectangle> on = new List<Rectangle>();
            foreach (Screen screen in Screen.AllScreens) on.Add(screen.Bounds);
            if (scanWave == null) scanWave = new ScanWave();
            scanWave.Start(new Point(body.Left + body.Width / 2, body.Top + body.Height / 2), on.ToArray());
            return true;
        }

        internal bool ScanWaveOn { get { return !preferences.NoScanWave; } }

        internal void ToggleScanWave()
        {
            preferences.NoScanWave = !preferences.NoScanWave;
            if (preferences.NoScanWave && scanWave != null) scanWave.Stop();
            SavePreferences();
        }

#if CHECKS
        // The ring reaches each screen and the grid fades behind it; it plays at its landings only, and not with it off, Motion
        // off or a full-screen game in front.
        private void SmokeScanWave(List<string> checks)
        {
            List<string> wrong = new List<string>();
            Rectangle screen = new Rectangle(0, 0, 640, 360);
            Point from = new Point(320, 180);
            using (Bitmap picture = new Bitmap(640, 360, PixelFormat.Format32bppPArgb))
            {
                ScanWave.Draw(picture, screen, from, 150, 100, Color.FromArgb(94, 240, 246));
                Color onRing = picture.GetPixel(470, 180), behind = picture.GetPixel(320 + 140, 192), farBehind = picture.GetPixel(320 + 20, 180), ahead = picture.GetPixel(320 + 200, 180);
                int lit = 0, most = 0;
                for (int px = 320 + 50; px < 320 + 150; px++) for (int py = 0; py < 360; py += 2) { int a = picture.GetPixel(px, py).A; if (a > 0) lit++; most = Math.Max(most, a); }
                if (onRing.A == 0 || ahead.A != 0 || farBehind.A != 0) wrong.Add("the ring or the grid's edge isn't where it should be: ring " + onRing.A + ", ahead " + ahead.A + ", far behind " + farBehind.A);
                if (lit == 0) wrong.Add("no grid behind the ring");
                if (most > ScanWave.Brightest) wrong.Add("the ring is brighter than it should be: " + most + " of 255");
                if (onRing.G < onRing.R) wrong.Add("the ring isn't her cyan: " + onRing);
            }
            if (!ScanWave.Meets(new Rectangle(2000, 0, 640, 360), from, 1700, 500) || ScanWave.Meets(new Rectangle(2000, 0, 640, 360), from, 600, 500))
                wrong.Add("a far screen was reached at the wrong time");
            bool noWas = preferences.NoScanWave, motionWas = preferences.Motion, hiddenWas = hidden;
            Func<bool> fullWas = FullScreen;
            try
            {
                scanChecking = true;
                preferences.NoScanWave = false;
                preferences.Motion = true;
                hidden = false;
                FullScreen = delegate { return false; };
                bool hop = ScanLanded("hop");
                bool started = ScanLanded("start");
                bool running = scanWave != null && scanWave.Running;
                if (scanWave != null) scanWave.Stop();
                FullScreen = delegate { return true; };
                bool overGame = ScanLanded("show");
                FullScreen = delegate { return false; };
                preferences.NoScanWave = true;
                bool switchedOff = ScanLanded("bring");
                preferences.NoScanWave = false;
                preferences.Motion = false;
                bool still = ScanLanded("bring");
                preferences.Motion = true;
                if (hop || !started || !running || overGame || switchedOff || still)
                    wrong.Add("it played where it shouldn't, or not where it should: a hop " + hop + ", start " + started + " (running " + running + "), over a game " + overGame + ", switched off " + switchedOff + ", Motion off " + still);
                // Its whole run on a stand-in clock: it ends and puts every window away.
                double at = 0;
                scanWave.Clock = delegate { return at; };
                ScanLanded("start");
                at = scanWave.Length + 0.1;
                for (int i = 0; i < 3 && scanWave.Running; i++) { System.Threading.Thread.Sleep(50); Application.DoEvents(); }
                if (scanWave.Running || scanWave.Shown != 0) wrong.Add("it didn't end and put its windows away");
                scanWave.Clock = null;
            }
            finally
            {
                scanChecking = false;
                if (scanWave != null) scanWave.Stop();
                preferences.NoScanWave = noWas;
                preferences.Motion = motionWas;
                hidden = hiddenWas;
                FullScreen = fullWas;
            }
            if (wrong.Count > 0) throw new Exception("Her scan wave went wrong: " + string.Join("; ", wrong.ToArray()));
            checks.Add("PASS her scan wave: a dim cyan ring from where she lands with a grid fading behind it, each screen reached as the ring gets there; it plays as she starts, on Show Capri and on Bring here, never on a hop, over a full-screen game, switched off or with Motion off, and ends with its windows put away");
        }
#endif
    }
}
