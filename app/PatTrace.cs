using System;
using System.Drawing;
using System.Globalization;

namespace CapriDesktop
{
    // One log line per try at a pat, from the cursor coming onto her until it leaves or a pat lands, so a missed pat
    // says why in numbers: where, how long, how far, how many turns, what the level wanted, her head band.
    internal sealed class PatTrace
    {
        // Shorter than this, in px of cursor path, is the cursor passing by rather than a try, and leaves no line.
        internal const int Least = 20;
        private long since = -1, lastAt;
        private Point last;
        private double path;
        private int left, right, top, bottom, widest, turnsX, turnsY, resets, legX, legY, wayX, wayY, drawn;
        private string zone, resetWhy, blocked;

        internal bool Active { get { return since >= 0; } }

        internal void Move(Point at, long now, Func<string> zoneNow)
        {
            if (since < 0)
            {
                since = lastAt = now;
                last = at;
                path = 0;
                left = right = at.X;
                top = bottom = at.Y;
                widest = turnsX = turnsY = resets = wayX = wayY = drawn = 0;
                legX = at.X;
                legY = at.Y;
                resetWhy = blocked = null;
                string named = zoneNow();
                zone = named == Touch.Head ? "head" : named == Touch.Shoulders ? "shoulders" : "other";
                return;
            }
            double dx = at.X - last.X, dy = at.Y - last.Y;
            path += Math.Sqrt(dx * dx + dy * dy);
            // Each way: how far it went before it turned back, on either axis.
            Leg(Math.Sign(at.X - last.X), last.X, ref wayX, ref legX);
            Leg(Math.Sign(at.Y - last.Y), last.Y, ref wayY, ref legY);
            widest = Math.Max(widest, Math.Max(Math.Abs(at.X - legX), Math.Abs(at.Y - legY)));
            last = at;
            lastAt = now;
            left = Math.Min(left, at.X);
            right = Math.Max(right, at.X);
            top = Math.Min(top, at.Y);
            bottom = Math.Max(bottom, at.Y);
        }

        private static void Leg(int heading, int from, ref int way, ref int leg)
        {
            if (heading == 0) return;
            if (way != 0 && heading != way) leg = from;
            way = heading;
        }

        // Back on her after a moment off: the path carries on from here, nothing added for the way it went.
        internal void Rejoin(Point at) { if (since >= 0) last = at; }

        // The longest leg drawn so far, as the pat counts it: all the movement along an axis between two turns.
        internal void Drawn(int leg) { drawn = Math.Max(drawn, leg); }

        // The turns it has on each axis now.
        internal void Turns(int x, int y) { turnsX = Math.Max(turnsX, x); turnsY = Math.Max(turnsY, y); }

        // The stroke started over inside the try, and why: paused or too fast.
        internal void Reset(string why) { resets++; resetWhy = why; }

        // It had the turns but something held the pat back: the 20 s since the last one, or under a second.
        internal void Blocked(string why) { blocked = why; }

        // The finished line, or null for a pass too short to be a try; the trace is cleared either way. result is "pat",
        // or a reason from outside (hologram, ride, drag), or null to work the reason out from what it saw.
        internal string End(string result, int level, int reach, int wanted, int within, int size, float scale, string bands, int windowTop)
        {
            if (since < 0) return null;
            bool pat = result == "pat";
            string why = result ?? blocked ?? (Math.Max(turnsX, turnsY) >= wanted ? "off her"
                : Math.Max(drawn, widest) < reach ? "reach short" : resets > 0 ? resetWhy : "too few turns inside " + (within / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + " s");
            string line = pat || path >= Least
                ? string.Format(CultureInfo.InvariantCulture, "{0} | {1}, {2:0.00} s on her, path {3:0} px, extent {4} x {5} px, turns x {6} y {7}, widest each way {8} px, longest leg drawn {19} px | level {9} wants {10} turns of {11} px inside {12:0.#} s{13} | cursor y {16} to {17} in her window, {18} | size {14}, scale {15:0.##}",
                    pat ? "pat" : "no pat: " + why, zone, (lastAt - since) / 1000.0, path, right - left, bottom - top, turnsX, turnsY, widest,
                    level, wanted, reach, within / 1000.0, resets > 0 ? ", started over " + resets + "x (" + resetWhy + ")" : "", size, scale, top - windowTop, bottom - windowTop, bands, drawn)
                : null;
            since = -1;
            return line;
        }
    }
}
