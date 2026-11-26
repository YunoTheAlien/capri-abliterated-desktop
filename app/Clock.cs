using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace CapriDesktop
{
    // Her one clock: every timed thing is a named entry on one due list, and one thread sleeps to the soonest, a second
    // at most. Window work is posted, which a busy window can't starve; off-thread work goes to the pool.
    internal static class Clock
    {
        private sealed class Job
        {
            internal string Name;
            internal long Due;
            internal long Every;
            internal Action Run;
            internal SynchronizationContext Thread;
            internal bool Window, Frame, Queued;

            // Window work runs on the window thread that asked for it, as a window timer does, else on hers.
            internal SynchronizationContext To { get { return Thread ?? ui; } }
        }

        private sealed class Cost
        {
            internal long Runs;
            internal double Ms;
        }

        private static readonly object gate = new object();
        private static readonly Dictionary<string, Job> jobs = new Dictionary<string, Job>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Cost> costs = new Dictionary<string, Cost>(StringComparer.Ordinal);
        private static SynchronizationContext ui;
        private static int uiThread;
        private static System.Threading.Thread counter;
        private static long ticks, frames;
        private static double frameMs;
        private static int numbered;

        private static long Now { get { return Stopwatch.GetTimestamp(); } }

        private static long Span(double ms) { return (long)(Math.Max(0, ms) * Stopwatch.Frequency / 1000); }

        // Her window's thread, set once as she is made; window work added earlier waits for it.
        internal static void Start()
        {
            lock (gate)
            {
                if (ui != null) return;
                uiThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
                ui = SynchronizationContext.Current;
                Monitor.PulseAll(gate);
            }
        }

        internal static bool OnUi { get { return System.Threading.Thread.CurrentThread.ManagedThreadId == uiThread; } }

        // A name of its own for each copy, so two of the same window keep two entries; the readout counts them by the name.
        internal static string Named(string name) { return name + "#" + Interlocked.Increment(ref numbered); }

        // Window work every so often, the first run one interval from now (or at once with soon). Same name replaces it.
        internal static void Every(string name, TimeSpan every, Action run, bool soon = false)
        {
            long span = Math.Max(Span(1), Span(every.TotalMilliseconds));
            Put(name, soon ? Now : Now + span, span, run, true, false);
        }

        // Window work once after a wait; the same name again moves it (a debounce is just this, called on each change).
        internal static void After(string name, TimeSpan wait, Action run) { Put(name, Now + Span(wait.TotalMilliseconds), 0, run, true, false); }

        // Moves a waiting entry to a new due time from now, keeping its run and repeat.
        internal static bool Move(string name, TimeSpan wait)
        {
            lock (gate)
            {
                Job job;
                if (!jobs.TryGetValue(name, out job)) return false;
                job.Due = Now + Span(wait.TotalMilliseconds);
                Monitor.PulseAll(gate);
            }
            return true;
        }

        // A repeating entry's new interval, its next run one interval from now.
        internal static bool Retime(string name, TimeSpan every)
        {
            lock (gate)
            {
                Job job;
                if (!jobs.TryGetValue(name, out job)) return false;
                job.Every = Math.Max(Span(1), Span(every.TotalMilliseconds));
                job.Due = Now + job.Every;
                Monitor.PulseAll(gate);
            }
            return true;
        }

        internal static void Stop(string name) { lock (gate) jobs.Remove(name); }

        internal static bool Has(string name) { lock (gate) return jobs.ContainsKey(name); }

        private static void Put(string name, long due, long every, Action run, bool window, bool frame)
        {
            lock (gate)
            {
                Job job;
                if (!jobs.TryGetValue(name, out job) || job.Queued)
                {
                    job = new Job { Name = name };
                    jobs[name] = job;
                }
                job.Due = due;
                job.Every = every;
                job.Run = run;
                job.Window = window;
                job.Frame = frame;
                job.Thread = window ? SynchronizationContext.Current as System.Windows.Forms.WindowsFormsSynchronizationContext : null;
                if (counter == null)
                {
                    counter = new System.Threading.Thread(Count) { IsBackground = true, Name = "Capri clock", Priority = ThreadPriority.AboveNormal };
                    counter.Start();
                }
                Monitor.PulseAll(gate);
            }
        }

        // The clock's one thread: sleep to the soonest entry, a second at most, then send out whatever is due. A repeat
        // out or still running isn't sent again, so a long tick has the next one after it, behind what it posted.
        private static void Count()
        {
            List<Job> due = new List<Job>();
            while (true)
            {
                lock (gate)
                {
                    long now = Now, next = now + Span(1000);
                    foreach (Job job in jobs.Values)
                        if (job.Due < next && !job.Queued && (!job.Window || job.To != null)) next = job.Due;
                    if (next > now) Monitor.Wait(gate, (int)Math.Max(1, Math.Min(1000, (next - now) * 1000 / Stopwatch.Frequency)));
                    now = Now;
                    ticks++;
                    foreach (Job job in jobs.Values)
                        if (job.Due <= now && !job.Queued && (!job.Window || job.To != null)) due.Add(job);
                    foreach (Job job in due)
                    {
                        job.Queued = true;
                        if (job.Every > 0) job.Due = now + job.Every;
                        else jobs.Remove(job.Name);
                    }
                }
                foreach (Job job in due)
                {
                    try
                    {
                        if (job.Window) job.To.Post(RunOne, job);
                        else ThreadPool.QueueUserWorkItem(RunOne, job);
                    }
                    catch (Exception) { lock (gate) job.Queued = false; }
                }
                due.Clear();
            }
        }

        private static void RunOne(object what)
        {
            Job job = (Job)what;
            Stopwatch took = Stopwatch.StartNew();
            try { job.Run(); }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            // Run: the clock thread, which skipped it while it was out, is told to count it back in.
            lock (gate)
            {
                job.Queued = false;
                if (job.Every > 0) Monitor.PulseAll(gate);
            }
            double ms = took.Elapsed.TotalMilliseconds;
            int mark = job.Name.IndexOf('#');
            string plain = mark > 0 ? job.Name.Substring(0, mark) : job.Name;
            lock (costs)
            {
                Cost cost;
                if (!costs.TryGetValue(plain, out cost)) { cost = new Cost(); costs[plain] = cost; }
                cost.Runs++;
                cost.Ms += ms;
                if (job.Frame) { frames++; frameMs += ms; }
            }
        }

        // A window timer's shape on the due list, so a timer moves over by its type alone: Tick runs every Interval ms
        // while it is enabled, on her thread, and a new Interval restarts the wait. A tick sent before a stop is dropped.
        internal class Ticker : IDisposable
        {
            private readonly string name;
            private readonly bool frame;
            private int interval = 100, generation;
            private bool enabled;

            internal event EventHandler Tick;

            internal object Tag;

            internal Ticker(string name) : this(name, false) { }

            protected Ticker(string name, bool frame)
            {
                this.name = Named(name);
                this.frame = frame;
            }

            internal int Interval
            {
                get { return interval; }
                set
                {
                    if (value < 1) throw new ArgumentOutOfRangeException("value");
                    if (interval == value) return;
                    interval = value;
                    if (enabled) Arm();
                }
            }

            internal bool Enabled
            {
                get { return enabled; }
                set { if (value) Start(); else Stop(); }
            }

            internal void Start()
            {
                if (enabled) return;
                enabled = true;
                Arm();
            }

            internal void Stop()
            {
                if (!enabled) return;
                enabled = false;
                generation++;
                Clock.Stop(name);
            }

            public void Dispose() { Stop(); Tick = null; }

            private void Arm()
            {
                int mine = ++generation;
                long span = Span(interval);
                Put(name, Now + span, span, delegate { Fire(mine); }, true, frame);
            }

            private void Fire(int mine)
            {
                EventHandler run = Tick;
                if (enabled && mine == generation && run != null) run(this, EventArgs.Empty);
            }
        }

        // Something that moves: a Ticker counted as drawing, so the readout shows frames apart from jobs.
        internal sealed class Frames : Ticker
        {
            internal Frames(string name) : base(name, true) { Interval = 33; }
        }

        // A thread timer's shape on the due list: its callback runs on the pool, so a held window can't hold it back.
        internal sealed class OffTimer : IDisposable
        {
            private readonly string name;
            private readonly TimerCallback callback;
            private readonly object state;
            private int generation;
            private bool disposed;

            internal OffTimer(string name, TimerCallback callback, object state = null, int due = Timeout.Infinite, int period = Timeout.Infinite)
            {
                this.name = Named(name);
                this.callback = callback;
                this.state = state;
                Change(due, period);
            }

            internal OffTimer(string name, TimerCallback callback, object state, TimeSpan due, TimeSpan period) : this(name, callback, state, Ms(due), Ms(period)) { }

            private static int Ms(TimeSpan span) { return span == Timeout.InfiniteTimeSpan ? Timeout.Infinite : (int)Math.Min(int.MaxValue, span.TotalMilliseconds); }

            // As Timer.Change: Timeout.Infinite for due stops it, for period runs it once.
            internal bool Change(int due, int period)
            {
                int mine;
                lock (gate)
                {
                    if (disposed) return false;
                    mine = ++generation;
                }
                if (due == Timeout.Infinite) { Clock.Stop(name); return true; }
                long every = period == Timeout.Infinite || period <= 0 ? 0 : Span(period);
                Put(name, Now + Span(due), every, delegate { Fire(mine); }, false, false);
                return true;
            }

            internal bool Change(TimeSpan due, TimeSpan period) { return Change(Ms(due), Ms(period)); }

            private void Fire(int mine)
            {
                lock (gate) if (disposed || mine != generation) return;
                callback(state);
            }

            public void Dispose()
            {
                lock (gate) { disposed = true; generation++; }
                Clock.Stop(name);
            }
        }

        // Her own CPU as a share of one core over the last ten seconds, -1 until first measured; read on the clock's pool
        // lane, so measuring never touches her window.
        internal static double CpuNow = -1;
        private static TimeSpan cpuWas;
        private static long cpuAt, framesWas, loggedAt;
        private static double frameMsWas;
        internal static double FrameMsNow, WakesNow;
        // The entries that cost most over the last ten seconds, dearest first, as "name ms".
        internal static string[] DearestNow = new string[0];
        private static long wakesWas;
        private static readonly Dictionary<string, double> msWas = new Dictionary<string, double>(StringComparer.Ordinal);
        private static OffTimer measuring;

        // Starts the measuring once: every ten seconds her CPU and frame time, and every five minutes a line in her log.
        internal static void Measure()
        {
            if (measuring != null) return;
            measuring = new OffTimer("measure", delegate { Sample(); }, null, 10000, 10000);
        }

        private static void Sample()
        {
            TimeSpan used;
            using (Process self = Process.GetCurrentProcess()) used = self.TotalProcessorTime;
            long now = Now;
            double seconds = cpuAt == 0 ? 0 : (now - cpuAt) * 1.0 / Stopwatch.Frequency;
            if (seconds > 0) CpuNow = (used - cpuWas).TotalMilliseconds / (seconds * 1000) * 100;
            cpuWas = used;
            cpuAt = now;
            lock (costs)
            {
                long drawn = frames - framesWas;
                FrameMsNow = drawn > 0 ? (frameMs - frameMsWas) / drawn : 0;
                framesWas = frames;
                frameMsWas = frameMs;
                if (seconds > 0) WakesNow = (ticks - wakesWas) / seconds;
                wakesWas = ticks;
                List<KeyValuePair<string, double>> spent = new List<KeyValuePair<string, double>>();
                foreach (KeyValuePair<string, Cost> one in costs)
                {
                    double was;
                    msWas.TryGetValue(one.Key, out was);
                    if (one.Value.Ms - was > 0.05) spent.Add(new KeyValuePair<string, double>(one.Key, one.Value.Ms - was));
                    msWas[one.Key] = one.Value.Ms;
                }
                spent.Sort(delegate(KeyValuePair<string, double> a, KeyValuePair<string, double> b) { return b.Value.CompareTo(a.Value); });
                List<string> dearest = new List<string>();
                for (int i = 0; i < Math.Min(3, spent.Count); i++) dearest.Add(spent[i].Key + " " + Math.Round(spent[i].Value) + " ms");
                DearestNow = dearest.ToArray();
            }
            if (loggedAt == 0) loggedAt = now;
            if ((now - loggedAt) / Stopwatch.Frequency < 300) return;
            loggedAt = now;
            CapriDesktop.Brain.Log("clock", -1, Summary(5));
        }

        // One line: CPU, frame time, wakes and the dearest entries.
        internal static string Summary(int dearest)
        {
            List<KeyValuePair<string, Cost>> all;
            lock (costs) all = new List<KeyValuePair<string, Cost>>(costs);
            all.Sort(delegate(KeyValuePair<string, Cost> a, KeyValuePair<string, Cost> b) { return b.Value.Ms.CompareTo(a.Value.Ms); });
            StringBuilder text = new StringBuilder();
            text.Append("cpu ").Append(CpuNow < 0 ? "?" : Math.Round(CpuNow, 1) + "%").Append(", frame ").Append(Math.Round(FrameMsNow, 2)).Append(" ms, wakes ").Append(ticks);
            for (int i = 0; i < Math.Min(dearest, all.Count); i++)
                text.Append(i == 0 ? "; " : ", ").Append(all[i].Key).Append(" ").Append(Math.Round(all[i].Value.Ms)).Append(" ms in ").Append(all[i].Value.Runs);
            return text.ToString();
        }

        // Wakes and frames so far, and each entry by its cost, dearest first: the Settings readout and her log.
        internal static string Readout()
        {
            List<KeyValuePair<string, Cost>> all;
            lock (costs) all = new List<KeyValuePair<string, Cost>>(costs);
            all.Sort(delegate(KeyValuePair<string, Cost> a, KeyValuePair<string, Cost> b) { return b.Value.Ms.CompareTo(a.Value.Ms); });
            int waiting;
            lock (gate) waiting = jobs.Count;
            StringBuilder text = new StringBuilder();
            text.Append("cpu ").Append(CpuNow < 0 ? "?" : Math.Round(CpuNow, 1) + "%").Append(", frame ").Append(Math.Round(FrameMsNow, 2)).Append(" ms; wakes ").Append(ticks).Append(", ").Append(waiting).Append(" waiting; frames ").Append(frames).Append(", ").Append(Math.Round(frameMs)).Append(" ms");
            foreach (KeyValuePair<string, Cost> one in all)
                text.Append("\n").Append(one.Key).Append(": ").Append(one.Value.Runs).Append(" runs, ").Append(Math.Round(one.Value.Ms, 1)).Append(" ms");
            return text.ToString();
        }
    }
}
