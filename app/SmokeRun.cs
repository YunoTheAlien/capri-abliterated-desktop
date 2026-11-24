using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace CapriDesktop
{
    internal sealed class SmokeOptions
    {
        internal string[] Only = new string[0];
        internal bool Steps, NoSteps;
        internal bool RunSteps { get { return Steps || (!NoSteps && Only.Length == 0); } }

        internal static SmokeOptions Parse(string[] args)
        {
            SmokeOptions options = new SmokeOptions();
            for (int i = 2; i < args.Length; i++)
            {
                if (args[i] == "--steps") options.Steps = true;
                else if (args[i] == "--no-steps") options.NoSteps = true;
                else if (args[i] == "--only" && i + 1 < args.Length)
                {
                    options.Only = args[++i].Split(',');
                    foreach (string name in options.Only)
                        if (name.Trim().Length == 0) throw new ArgumentException("--only needs a comma list of section names.");
                }
                else throw new ArgumentException("Unknown smoke option: " + args[i]);
            }
            if (options.Steps && (options.NoSteps || options.Only.Length != 0))
                throw new ArgumentException("--steps runs the steps alone; use --only or --no-steps for sections.");
            return options;
        }
    }

    internal static class SmokeRun
    {
        private const string WorkerVariable = "CAPRI_SMOKE_WORKER";
        // A folder laid into each worker's fresh data folder before it starts, so a run can find mods, settings or
        // packs while every attempt still starts clean.
        private const string SeedVariable = "CAPRI_SMOKE_SEED";
        // Sections run inside another, so --only can name a child and its parent still runs to reach it.
        private static readonly Dictionary<string, string[]> Children = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "SmokeReactions", new[] { "SmokeReactQuick", "SmokeReactFeed", "SmokeReactFade", "SmokeReactMoods", "SmokeReactWritten", "SmokeReactBrain", "SmokeReactVoice", "SmokeReactMotion", "SmokeReactSwitch", "SmokeReactBanners" } },
            { "SmokeThrows", new[] { "SmokeThrowReads", "SmokeThrowSlow", "SmokeThrowFast", "SmokeThrowUp", "SmokeThrowSlam", "SmokeThrowStanding", "SmokeThrowCatch", "SmokeThrowCalm", "SmokeThrowGrudge", "SmokeThrowDodges", "SmokeThrowSorry", "SmokeThrowPat", "SmokeThrowAlone" } },
            { "SmokeCloudPicker", new[] { "SmokePickerRejectedKeys", "SmokePickerGemini", "SmokePickerClears", "SmokePickerSavedGemini", "SmokePickerQueuedChoices", "SmokePickerLegacyKeys", "SmokePickerPending-new-key", "SmokePickerPending-unrecognised-key", "SmokePickerPending-tier", "SmokePickerPending-forget", "SmokePickerPending-local", "SmokePickerRefresh", "SmokePickerOldRefresh" } }
        };
        private static string folder, kind, current;
        private static DateTime began;
        private static int limit;
        private static readonly object progressLock = new object();
        private static Thread watched, watcher;
        private static SmokeOptions options;
        private static readonly HashSet<string> matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> sectionsDone = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, bool> stepsDone = new Dictionary<int, bool>();
        private static readonly Stack<bool> selected = new Stack<bool>();
        // What each open section interrupted, so leaving a child goes back to its parent's (or step's) deadline.
        private static readonly Stack<string[]> outer = new Stack<string[]>();
        private sealed class SectionTime
        {
            internal long Started, Children, Recovered;
            internal string Name, Parent;
        }
        private static readonly Stack<SectionTime> timings = new Stack<SectionTime>();
        internal static bool Worker { get { return Environment.GetEnvironmentVariable(WorkerVariable) == "1"; } }

        // What the seed folder holds, copied into a worker's folder before it starts. Nothing when the
        // variable is unset, which is every ordinary run.
        private static void Seed(string room)
        {
            try
            {
                string from = Environment.GetEnvironmentVariable(SeedVariable);
                if (string.IsNullOrEmpty(from) || !Directory.Exists(from)) return;
                from = Path.GetFullPath(from);
                if (!Directory.Exists(room)) Directory.CreateDirectory(room);
                foreach (string made in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
                    Directory.CreateDirectory(Path.Combine(room, made.Substring(from.Length).TrimStart(Path.DirectorySeparatorChar)));
                foreach (string one in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
                    File.Copy(one, Path.Combine(room, one.Substring(from.Length).TrimStart(Path.DirectorySeparatorChar)), true);
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }
        private static string ownAsync;
        // The last time she showed or moved one of her own windows in a way Windows carries out later on its thread, and from where.
        internal static string OwnAsync { get { return Volatile.Read(ref ownAsync); } }

        internal static void NoteAsync(IntPtr window, string what)
        {
            if (!Worker) return;
            int process;
            Native.GetWindowThreadProcessId(window, out process);
            if (process != Process.GetCurrentProcess().Id) return;
            List<string> found = new List<string>();
            foreach (string line in Environment.StackTrace.Split('\n'))
            {
                string frame = line.Trim();
                if (frame.StartsWith("at ", StringComparison.Ordinal)) frame = frame.Substring(3);
                int place = frame.IndexOf(" in ", StringComparison.Ordinal);
                if (place > 0) frame = frame.Substring(0, place);
                if (frame.StartsWith("CapriDesktop.", StringComparison.Ordinal) && frame.IndexOf(".NoteAsync", StringComparison.Ordinal) < 0) found.Add(frame);
                if (found.Count == 6) break;
            }
            Volatile.Write(ref ownAsync, what + " on thread " + Thread.CurrentThread.ManagedThreadId + " from " + string.Join(" < ", found.ToArray()));
        }
        internal static int NextStep
        {
            get { int next = 0; while (stepsDone.ContainsKey(next)) next++; return next; }
        }

        internal static void Start(string output, SmokeOptions settings)
        {
            folder = output;
            options = settings;
            matched.Clear(); sectionsDone.Clear(); stepsDone.Clear(); selected.Clear(); outer.Clear(); timings.Clear();
            string path = Path.Combine(folder, "sections-done.txt");
            if (File.Exists(path)) foreach (string name in File.ReadAllLines(path)) sectionsDone.Add(name);
            path = Path.Combine(folder, "steps-done.txt");
            if (File.Exists(path)) foreach (string line in File.ReadAllLines(path))
            {
                string[] pieces = line.Split(' ');
                int step;
                if (pieces.Length == 2 && int.TryParse(pieces[1], out step)) stepsDone[step] = pieces[0] == "PASS";
            }
            Begin("startup", "startup", 60);
            watched = Thread.CurrentThread;
            if (watcher == null)
            {
                watcher = new Thread(Watch);
                watcher.IsBackground = true;
                watcher.Start();
            }
        }

        // A step or section 10 s from running out of time notes where the UI thread is stuck in hangs.txt, so its timeout
        // can say what hung. A step only flushes its checks when it ends, so without this a timeout says nothing more.
        private static void Watch()
        {
            long noted = 0;
            while (true)
            {
                Thread.Sleep(1000);
                string phase, name;
                long start;
                int seconds;
                lock (progressLock) { phase = kind; name = current; start = began.Ticks; seconds = limit; }
                if (phase != "step" && phase != "section" || start == noted || seconds < 30 || DateTime.UtcNow.Ticks - start < TimeSpan.FromSeconds(seconds - 10).Ticks) continue;
                noted = start;
                string where = Heartbeat.Where(watched);
                try { File.AppendAllText(Path.Combine(folder, "hangs.txt"), phase + " " + name + ":" + (where.Length == 0 ? " Stuck at: (couldn't read it)" : where) + Environment.NewLine); }
                catch (IOException) { }
            }
        }

        // What the worker noted in hangs.txt for the step or section that just timed out, if anything.
        private static string Stuck(string[] active)
        {
            try
            {
                string path = Path.Combine(folder, "hangs.txt");
                if (!File.Exists(path)) return "";
                string[] lines = File.ReadAllLines(path);
                string mark = active[0] + " " + active[1] + ":";
                return lines.Length != 0 && lines[lines.Length - 1].StartsWith(mark, StringComparison.Ordinal) ? "." + lines[lines.Length - 1].Substring(mark.Length) : "";
            }
            catch (IOException) { return ""; }
        }

        // Whether -Only names this section itself, for one that otherwise runs inside a step.
        internal static bool Named(string name)
        {
            foreach (string item in options.Only) if (string.Equals(name, item.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static bool Wants(string name)
        {
            // --steps runs the steps alone, with the sections inside them.
            if (options.Steps) return kind == "step";
            bool wanted = options.Only.Length == 0 || (selected.Count != 0 && selected.Peek());
            foreach (string item in options.Only)
                if (string.Equals(name, item.Trim(), StringComparison.OrdinalIgnoreCase)) { matched.Add(item); wanted = true; }
            string[] children;
            if (Children.TryGetValue(name, out children)) foreach (string child in children)
                foreach (string item in options.Only)
                    if (string.Equals(child, item.Trim(), StringComparison.OrdinalIgnoreCase)) { matched.Add(item); wanted = true; }
            return wanted;
        }

        // Sections entered across every attempt of this run, read from the file the workers append to.
        private static int Entered()
        {
            try
            {
                string path = folder == null ? null : Path.Combine(folder, "sections-done.txt");
                if (path == null || !File.Exists(path)) return sectionsDone.Count;
                int count = 0;
                foreach (string line in File.ReadAllLines(path)) if (line.Trim().Length > 0) count++;
                return Math.Max(count, sectionsDone.Count);
            }
            catch (Exception) { return sectionsDone.Count; }
        }

        internal static bool EnterSection(string name)
        {
            if (!Wants(name) || sectionsDone.Contains(name)) return false;
            bool whole = options.Only.Length == 0 || (selected.Count != 0 && selected.Peek());
            foreach (string item in options.Only)
                if (string.Equals(name, item.Trim(), StringComparison.OrdinalIgnoreCase)) whole = true;
            selected.Push(whole);
            outer.Push(new[] { kind, current, limit.ToString(CultureInfo.InvariantCulture) });
            Begin("section", name, name.StartsWith("Probe", StringComparison.Ordinal) ? 14400 : 180);
            timings.Push(new SectionTime { Started = Stopwatch.GetTimestamp(), Name = name, Parent = timings.Count == 0 ? "" : timings.Peek().Name, Recovered = RecoveredChildren(name) });
            return true;
        }

        internal static void LeaveSection(string name, List<string> checks)
        {
            RecordTime(name);
            Flush(checks);
            sectionsDone.Add(name);
            File.AppendAllText(Path.Combine(folder, "sections-done.txt"), name + Environment.NewLine);
            selected.Pop();
            string[] back = outer.Pop();
            if (back[0] == "section" || back[0] == "step") { kind = null; Begin(back[0], back[1], int.Parse(back[2], CultureInfo.InvariantCulture)); }
            else Begin("startup", "between sections", 60);
        }

        private static List<string> SavedTimes()
        {
            string path = Path.Combine(folder, "smoke-timings.txt");
            List<string> lines = new List<string>();
            if (!File.Exists(path)) return lines;
            string[] saved = File.ReadAllText(path).Split('\n');
            for (int i = 1; i < saved.Length - 1; i++) lines.Add(saved[i].TrimEnd('\r'));
            return lines;
        }

        private static long RecoveredChildren(string name)
        {
            List<string> lines;
            try { lines = SavedTimes(); }
            catch (IOException) { return 0; }
            catch (UnauthorizedAccessException) { return 0; }
            string depth = (timings.Count + 1).ToString(CultureInfo.InvariantCulture);
            long recovered = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                string[] parts = lines[i].Split('\t');
                double milliseconds;
                if (parts.Length == 5 && parts[1] == depth && string.Equals(parts[4], name, StringComparison.OrdinalIgnoreCase)
                    && double.TryParse(parts[2], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out milliseconds))
                    recovered += (long)Math.Round(milliseconds * Stopwatch.Frequency / 1000.0);
            }
            return recovered;
        }

        private static void RecordTime(string name)
        {
            SectionTime time = timings.Pop();
            long elapsed = Stopwatch.GetTimestamp() - time.Started;
            long total = elapsed + time.Recovered;
            if (timings.Count != 0)
            {
                timings.Peek().Children += elapsed;
                timings.Peek().Recovered += time.Recovered;
            }
            double milliseconds = 1000.0 / Stopwatch.Frequency;
            string row = name + "\t" + timings.Count.ToString(CultureInfo.InvariantCulture)
                + "\t" + (total * milliseconds).ToString("F3", CultureInfo.InvariantCulture)
                + "\t" + (Math.Max(0L, elapsed - time.Children) * milliseconds).ToString("F3", CultureInfo.InvariantCulture)
                + "\t" + time.Parent;
            try
            {
                string path = Path.Combine(folder, "smoke-timings.txt");
                List<string> lines = new List<string>(new[] { "section\tdepth\tinclusive_ms\town_ms\tparent" });
                lines.AddRange(SavedTimes());
                lines.Add(row);
                AtomicLines(path, lines.ToArray());
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        internal static void ValidateSelection(List<string> checks)
        {
            foreach (string item in options.Only)
                if (!matched.Contains(item)) checks.Add("FAIL --only: no section matches " + item.Trim());
            Flush(checks);
        }

        internal static void Begin(string phase, string name, int seconds)
        {
            if (kind == phase && current == name) return;
            lock (progressLock) { kind = phase; current = name; began = DateTime.UtcNow; limit = seconds; }
            WriteProgress();
        }

        private static void WriteProgress()
        {
            AtomicLines(Path.Combine(folder, "progress.txt"), new[] {
                kind == "step" ? "step " + current : kind + " " + current,
                "started " + began.ToString("O", CultureInfo.InvariantCulture),
                "limit " + limit + " s",
                kind + "\t" + current + "\t" + began.Ticks + "\t" + limit,
                Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture)
            });
        }

        internal static void Flush(List<string> checks)
        {
            AtomicLines(Path.Combine(folder, "smoke-test.txt"), checks.ToArray());
        }

        private static void AtomicLines(string path, string[] lines)
        {
            string temporary = path + ".new";
            File.WriteAllLines(temporary, lines);
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    if (File.Exists(path)) File.Replace(temporary, path, null);
                    else File.Move(temporary, path);
                    return;
                }
                catch (IOException) { if (attempt == 9) throw; Thread.Sleep(10); }
            }
        }

        internal static List<string> Checks()
        {
            string path = Path.Combine(folder, "smoke-test.txt");
            return File.Exists(path) ? new List<string>(File.ReadAllLines(path)) : new List<string>();
        }

        internal static int StepSeconds(int step)
        {
            int testLimit;
            if (Fault("HANG", step) && int.TryParse(Environment.GetEnvironmentVariable("CAPRI_SMOKE_TEST_SECONDS"), out testLimit) && testLimit > 0 && testLimit <= 60) return testLimit;
            // Step 0 covers UI and render paths; step 1 has long transcode and voice-server waits, so it gets 25
            // minutes; step 22 runs talk and voice checks that can each take over a minute. A real hang still fails.
            return step == 0 ? 180 : step == 1 ? 1500 : step == 22 ? 180 : 60;
        }

        // 0-13 exchange asynchronous chat/voice results. Glances (14) and screen/cloud checks (17) have their own fixtures.
        internal static int Needs(int step)
        {
            if (step == 0 || step == 14 || step == 17) return -1;
            bool passed;
            return stepsDone.TryGetValue(step - 1, out passed) && passed ? -1 : step - 1;
        }

        internal static void StepDone(int step, bool passed, List<string> checks)
        {
            Flush(checks);
            stepsDone[step] = passed;
            File.AppendAllText(Path.Combine(folder, "steps-done.txt"), (passed ? "PASS " : "FAIL ") + step + Environment.NewLine);
            Begin("startup", "between steps", 60);
        }

        internal static void StepFailed(int step, Exception error, List<string> checks)
        {
            checks.Add("FAIL step " + step + ": " + error.Message);
            File.AppendAllText(Path.Combine(folder, "failure.txt"), "step " + step + ": " + error + Environment.NewLine);
            StepDone(step, false, checks);
            Begin("recover", step.ToString(CultureInfo.InvariantCulture), 10);
            // Exiting the worker also stops blocked callbacks, audio, menus and every board it opened. The supervisor
            // starts a fresh Companion on the same desktop; no failed step's hooks or pending replies survive.
            Environment.Exit(2);
        }

        internal static void Inject(int step)
        {
            if (Fault("THROW", step)) throw new InvalidOperationException("deliberate smoke test exception");
            if (Fault("HANG", step)) new ManualResetEvent(false).WaitOne();
        }

        private static bool Fault(string name, int step)
        {
            return Environment.GetEnvironmentVariable("CAPRI_SMOKE_TEST_" + name) == step.ToString(CultureInfo.InvariantCulture);
        }

        internal static void Complete(List<string> checks)
        {
            Flush(checks);
            Begin("done", "closing", 10);
        }

        internal static int Child(string[] args)
        {
            try
            {
                if (args.Length == 4 && args[1] == "pipes")
                {
                    using (StreamWriter errors = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)))
                    using (StreamWriter output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)))
                    {
                        errors.Write(new string('e', 131072)); errors.Flush();
                        output.Write(new string('o', 131072)); output.Flush();
                        using (Process tar = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "tar.exe"), "-xf " + Quote(args[2]) + " -C " + Quote(args[3])) { UseShellExecute = false, CreateNoWindow = true }))
                        {
                            tar.WaitForExit();
                            return tar.ExitCode;
                        }
                    }
                }
                // A stand-in for her being closed from outside: a window that, asked to close, deletes the markers as she
                // does on the way out ("closes"), or a stuck one that won't close at all ("stays").
                if (args.Length == 4 && (args[1] == "closes" || args[1] == "stays"))
                {
                    bool stays = args[1] == "stays";
                    string marker = args[2], ready = args[3];
                    // Stuck: no window for a close request to reach, so only a stop ends it.
                    if (stays) { File.WriteAllText(ready, "up"); while (true) Thread.Sleep(1000); }
                    using (System.Windows.Forms.Form stand = new System.Windows.Forms.Form { Text = "Capri stand-in", ShowInTaskbar = false, StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new System.Drawing.Point(-3000, -3000), Size = new System.Drawing.Size(120, 80) })
                    {
                        stand.FormClosing += delegate(object sender, System.Windows.Forms.FormClosingEventArgs closing)
                        {
                            try { File.Delete(marker); File.Delete(Path.Combine(Path.GetDirectoryName(marker), Program.StartMarker)); } catch (IOException) { }
                        };
                        stand.Shown += delegate { File.WriteAllText(ready, "up"); };
                        System.Windows.Forms.Application.Run(stand);
                    }
                    return 0;
                }
                if (args.Length == 3 && args[1] == "hang")
                {
                    using (Process child = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "ping.exe"), "-t 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true }))
                    {
                        File.WriteAllText(args[2] + ".new", child.Id.ToString(CultureInfo.InvariantCulture));
                        File.Move(args[2] + ".new", args[2]);
                        while (true) Thread.Sleep(1000);
                    }
                }
                Console.Error.WriteLine("Unknown smoke child arguments.");
                return 2;
            }
            catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
        }

        internal static int Run(string[] args)
        {
            string output = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(output);
            string identity = WindowsIdentity.GetCurrent().User.Value;
            using (Mutex mutex = new Mutex(false, "Local\\Capri.Desktop." + identity + ".Test"))
            {
                bool held;
                try { held = mutex.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
                if (!held)
                {
                    File.WriteAllText(Path.Combine(output, "failure.txt"), "Another smoke test holds Capri's test lock. This run did not start.");
                    return 1;
                }
                try { return RunLocked(args, output); }
                finally { mutex.ReleaseMutex(); }
            }
        }

        private static int RunLocked(string[] args, string output)
        {
            folder = output;
            foreach (string name in new[] { "smoke-test.txt", "failure.txt", "progress.txt", "sections-done.txt", "steps-done.txt", "hangs.txt" })
                File.Delete(Path.Combine(folder, name));
            try { File.Delete(Path.Combine(folder, "smoke-timings.txt")); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            List<string> checks = new List<string>();
            try
            {
                options = SmokeOptions.Parse(args);
                // Kept short: on GitHub's runner the run folder plus her longest download path (the "Hey Capri" encoder in its
                // staging folder) reached Windows' 260-letter limit with a full GUID here, so the pack read as missing files.
                string runtime = "data-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                for (int attempt = 0; attempt < 100; attempt++)
                {
                    using (SmokeJob job = new SmokeJob())
                    using (Process child = new Process())
                    {
                        ProcessStartInfo start = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName);
                        start.UseShellExecute = false;
                        start.CreateNoWindow = true;
                        start.WindowStyle = ProcessWindowStyle.Hidden;
                        List<string> arguments = new List<string>();
                        foreach (string arg in args) arguments.Add(Quote(arg));
                        start.Arguments = string.Join(" ", arguments.ToArray());
                        start.EnvironmentVariables[WorkerVariable] = "1";
                        string room = Path.Combine(folder, runtime + "-" + attempt);
                        start.EnvironmentVariables["CAPRI_SMOKE_DATA"] = room;
                        Seed(room);
                        child.StartInfo = start;
                        child.Start();
                        job.Assign(child);
                        string[] active = null;
                        string stamp = null;
                        Stopwatch deadline = Stopwatch.StartNew();
                        int seconds = 60;
                        bool timedOut = false, escaped = false;
                        while (!child.WaitForExit(100))
                        {
                            if (EscPressed(child.Id, Process.GetCurrentProcess().Id))
                            {
                                escaped = true;
                                job.Stop();
                                child.WaitForExit(5000);
                                break;
                            }
                            string[] progress = ReadProgress(child.Id);
                            if (progress != null && progress[2] != stamp)
                            {
                                active = progress; stamp = progress[2];
                                seconds = int.Parse(progress[3], CultureInfo.InvariantCulture);
                                deadline.Restart();
                            }
                            if (deadline.Elapsed.TotalSeconds < seconds) continue;
                            timedOut = true;
                            job.Stop();
                            if (!child.WaitForExit(5000)) throw new Exception("The smoke worker did not stop.");
                            break;
                        }
                        string[] last = ReadProgress(child.Id);
                        if (last != null) active = last;
                        checks = Checks();
                        if (escaped)
                        {
                            checks.Add("FAIL smoke stopped with Esc" + (active != null && active.Length > 1 ? " during " + active[0] + " " + active[1] : ""));
                            File.AppendAllText(Path.Combine(folder, "failure.txt"), checks[checks.Count - 1] + Environment.NewLine);
                            break;
                        }
                        if (active != null && active[0] == "done")
                        {
                            if (timedOut) checks.Add("FAIL smoke shutdown: timed out after " + seconds + " s");
                            else if (child.ExitCode != 0 && !checks.Exists(IsFailure)) checks.Add("FAIL smoke worker exited " + child.ExitCode);
                            break;
                        }
                        if (active != null && active[0] == "recover")
                        {
                            if (timedOut)
                            {
                                checks.Add("LIMIT smoke worker needed a forced exit after failed step " + active[1]);
                                Flush(checks);
                            }
                            continue;
                        }
                        string reason = timedOut ? "timed out after " + seconds + " s" : "worker exited " + child.ExitCode;
                        if (active == null || active[0] == "startup") { checks.Add("FAIL smoke startup: " + reason); break; }
                        if (timedOut) reason += Stuck(active);
                        if (active[0] == "section")
                        {
                            checks.Add("FAIL section " + active[1] + ": " + reason);
                            File.AppendAllText(Path.Combine(folder, "sections-done.txt"), active[1] + Environment.NewLine);
                        }
                        else if (active[0] == "step")
                        {
                            checks.Add("FAIL step " + active[1] + ": " + reason);
                            File.AppendAllText(Path.Combine(folder, "steps-done.txt"), "FAIL " + active[1] + Environment.NewLine);
                        }
                        else { checks.Add("FAIL smoke recovery: " + reason); break; }
                        File.AppendAllText(Path.Combine(folder, "failure.txt"), checks[checks.Count - 1] + Environment.NewLine);
                        Flush(checks);
                    }
                    if (attempt == 99) checks.Add("FAIL smoke exceeded its recovery limit");
                }
            }
            catch (Exception error)
            {
                checks = Checks();
                checks.Add("FAIL smoke: " + error.Message);
                File.AppendAllText(Path.Combine(folder, "failure.txt"), error + Environment.NewLine);
            }
            bool failed = checks.Exists(IsFailure);
            checks.Add("TOTAL PASS " + checks.FindAll(delegate(string line) { return line.StartsWith("PASS ", StringComparison.Ordinal); }).Count
                + " / LIMIT " + checks.FindAll(delegate(string line) { return line.StartsWith("LIMIT ", StringComparison.Ordinal); }).Count
                + " / FAIL " + checks.FindAll(IsFailure).Count
                + " / SKIP " + checks.FindAll(delegate(string line) { return line.StartsWith("SKIP ", StringComparison.Ordinal); }).Count
                // How many sections were actually entered: a full run enters a known number, so fewer here is the only
                // sign something ended the run early.
                + " / SECTIONS " + Entered());
            Flush(checks);
            Begin("finished", failed ? "failed" : "passed", 0);
            return failed ? 1 : 0;
        }

        private static bool IsFailure(string line) { return line.StartsWith("FAIL ", StringComparison.Ordinal); }

        // Esc stops a test run only while one of its own windows is in front (an intro, a window under test, or her),
        // never from their apps. Checks send Esc as a window message, never a key; the first read clears an old press.
        internal static Func<bool> EscKey = delegate { return (CapriDesktop.Native.GetAsyncKeyState(0x1B) & 0x8001) != 0; };
        private static bool escCleared;

        // The process whose window is in front, or -1.
        internal static Func<int> InFront = delegate
        {
            try
            {
                IntPtr front = CapriDesktop.Native.GetForegroundWindow();
                int process;
                if (front == IntPtr.Zero || CapriDesktop.Native.GetWindowThreadProcessId(front, out process) == 0) return -1;
                return process;
            }
            catch (Exception) { return -1; }
        };

        // A press read either way, so one in their own app is used up there and never counts later.
        internal static bool EscPressed(params int[] run)
        {
            bool pressed = EscKey();
            if (!escCleared) { escCleared = true; return false; }
            return pressed && Array.IndexOf(run, InFront()) >= 0;
        }

        // Esc stops a run with one of its windows in front and never with their own app in front; a press from before the run
        // never counts.
        internal static void EscCheck(List<string> checks)
        {
            Func<bool> keyWas = EscKey;
            Func<int> frontWas = InFront;
            bool clearedWas = escCleared;
            try
            {
                int run = 4242, theirs = 777;
                EscKey = delegate { return true; };
                escCleared = false;
                InFront = delegate { return run; };
                if (EscPressed(run)) throw new Exception("A press from before the run stopped it.");
                InFront = delegate { return theirs; };
                if (EscPressed(run, run + 1)) throw new Exception("Esc in their own app stopped a run.");
                InFront = delegate { return run + 1; };
                if (!EscPressed(run, run + 1)) throw new Exception("Esc with a run window in front didn't stop it.");
                InFront = delegate { return -1; };
                if (EscPressed(run)) throw new Exception("Esc with no window in front stopped a run.");
                checks.Add("PASS Esc stops a test run only with one of its windows in front: Esc in their own app or with nothing in front leaves it running, and a press from before the run never counts");
            }
            finally { EscKey = keyWas; InFront = frontWas; escCleared = clearedWas; }
        }

        internal static void EscStops(System.Windows.Forms.Form app, Action quit)
        {
            int own = Process.GetCurrentProcess().Id;
            System.Threading.Thread watch = new System.Threading.Thread(delegate()
            {
                EscKey();
                while (true)
                {
                    System.Threading.Thread.Sleep(100);
                    if (!EscKey() || InFront() != own) continue;
                    try { app.BeginInvoke(quit); } catch (Exception) { }
                    System.Threading.Thread.Sleep(2000);
                    Environment.Exit(3);
                }
            });
            watch.IsBackground = true;
            watch.Start();
        }

        private static string[] ReadProgress(int process)
        {
            try
            {
                string[] lines;
                using (FileStream file = new FileStream(Path.Combine(folder, "progress.txt"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader reader = new StreamReader(file)) lines = reader.ReadToEnd().Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                if (lines.Length < 5 || lines[4] != process.ToString(CultureInfo.InvariantCulture)) return null;
                string[] parts = lines[3].Split('\t');
                return parts.Length == 4 ? parts : null;
            }
            catch (IOException) { return null; }
        }

        internal static string Quote(string value)
        {
            StringBuilder quoted = new StringBuilder("\"");
            int slashes = 0;
            foreach (char letter in value)
            {
                if (letter == '\\') { slashes++; continue; }
                quoted.Append('\\', letter == '"' ? slashes * 2 + 1 : slashes);
                quoted.Append(letter);
                slashes = 0;
            }
            quoted.Append('\\', slashes * 2);
            return quoted.Append('"').ToString();
        }
    }

    internal sealed class SmokeJob : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] private struct BasicLimits
        {
            internal long ProcessTime, JobTime;
            internal uint Flags;
            internal UIntPtr Minimum, Maximum;
            internal uint Processes;
            internal UIntPtr Affinity;
            internal uint Priority, Scheduling;
        }
        [StructLayout(LayoutKind.Sequential)] private struct Limits
        {
            internal BasicLimits Basic;
            internal ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes;
            internal UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateJobObject(IntPtr attributes, string name);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(IntPtr job, int info, ref Limits limits, int size);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateJobObject(IntPtr job, uint code);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        private IntPtr handle;
        internal SmokeJob()
        {
            handle = CreateJobObject(IntPtr.Zero, null);
            Limits limits = new Limits(); limits.Basic.Flags = 0x2000;
            if (handle == IntPtr.Zero || !SetInformationJobObject(handle, 9, ref limits, Marshal.SizeOf(typeof(Limits))))
            {
                Dispose();
                throw new System.ComponentModel.Win32Exception();
            }
        }
        internal void Assign(Process process)
        {
            if (!AssignProcessToJobObject(handle, process.Handle))
            {
                int error = Marshal.GetLastWin32Error();
                if (!process.HasExited) process.Kill();
                throw new System.ComponentModel.Win32Exception(error);
            }
        }
        internal void Stop() { if (!TerminateJobObject(handle, 99)) throw new System.ComponentModel.Win32Exception(); }
        public void Dispose() { if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; } }
    }

    // The screen she treats as home: the primary for her own runs. A test run takes CAPRI_TEST_SCREEN (0 is the first
    // Windows lists), else the last screen that isn't the primary, else the primary, so it stays off the one in use.
    internal static class Displays
    {
        internal const string ScreenVariable = "CAPRI_TEST_SCREEN";
        // Set at the start of a bench or shots run; a smoke worker is known by its own variable.
        internal static bool Testing;

        // A bench, shots or smoke worker run.
        internal static bool Test { get { return Testing || SmokeRun.Worker; } }

        internal static System.Windows.Forms.Screen Home
        {
            get
            {
                System.Windows.Forms.Screen primary = System.Windows.Forms.Screen.PrimaryScreen;
                if (!Test) return primary;
                System.Windows.Forms.Screen[] all = System.Windows.Forms.Screen.AllScreens;
                int picked;
                if (int.TryParse(Environment.GetEnvironmentVariable(ScreenVariable) ?? "", NumberStyles.Integer, CultureInfo.InvariantCulture, out picked) && picked >= 0 && picked < all.Length) return all[picked];
                for (int i = all.Length - 1; i >= 0; i--) if (!all[i].Primary) return all[i];
                return primary;
            }
        }
    }
}
