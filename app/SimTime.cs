using System;
using System.Diagnostics;
using System.Threading;

namespace CapriDesktop
{
    // A test's clock: real time plus an offset that only a test run moves, so a speed run can let minutes and hours pass
    // between lines without waiting for them. The offset is zero unless a test sets it, so nothing changes otherwise.
    internal static class SimTime
    {
        private static long offsetTicks;

        internal static TimeSpan Offset { get { return TimeSpan.FromTicks(Interlocked.Read(ref offsetTicks)); } }

        internal static void Advance(TimeSpan by) { if (by > TimeSpan.Zero) Interlocked.Add(ref offsetTicks, by.Ticks); }

        internal static void Reset() { Interlocked.Exchange(ref offsetTicks, 0); }

        internal static DateTime Now { get { return DateTime.Now + Offset; } }

        internal static DateTime UtcNow { get { return DateTime.UtcNow + Offset; } }
    }

    // Her own running clock, a Stopwatch that also counts the test's offset.
    // Held as a plain Stopwatch, by her windows' animations or a mod that reads her clock, it is real time.
    internal sealed class SimStopwatch : Stopwatch
    {
        private SimStopwatch() { Start(); }

        internal static new SimStopwatch StartNew() { return new SimStopwatch(); }

        internal new TimeSpan Elapsed { get { return base.Elapsed + SimTime.Offset; } }

        internal new long ElapsedMilliseconds { get { return (long)Elapsed.TotalMilliseconds; } }
    }

    // A conversation run's seal: while it is on, nothing of hers reaches the real desktop. Every tool, hand, key, click,
    // page and screen look is written down here instead of done, and anything that gets past the tool layer counts as a breach.
    internal static class RunSeal
    {
        internal static volatile bool On;
        internal static readonly System.Collections.Generic.List<string> Tried = new System.Collections.Generic.List<string>();
        internal static readonly System.Collections.Generic.List<string> Breaches = new System.Collections.Generic.List<string>();

        internal static string Record(string what)
        {
            lock (Tried) Tried.Add(what);
            return "Nothing ran: this is a test run and her hands are off.";
        }

        internal static void Breach(string what) { lock (Breaches) Breaches.Add(what); }
    }
}
