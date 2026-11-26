using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace CapriDesktop
{
    // What she holds while idle, and how fast her most used windows open: the numbers the unloading is measured by.
    internal sealed partial class Mainframe
    {
#if CHECKS
        // WM_TIMER messages counted by the window they reach, while it is on.
        private sealed class TimerCount : IMessageFilter
        {
            internal readonly Dictionary<IntPtr, int> Seen = new Dictionary<IntPtr, int>();

            public bool PreFilterMessage(ref Message message)
            {
                if (message.Msg != 0x0113) return false;
                int had;
                Seen.TryGetValue(message.HWnd, out had);
                Seen[message.HWnd] = had + 1;
                return false;
            }
        }

        // A WM_TIMER's window named by the timer behind it: its interval and the class and method its Tick runs.
        private static string TimerName(IntPtr window)
        {
            try
            {
                const System.Reflection.BindingFlags hidden = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                NativeWindow native = NativeWindow.FromHandle(window);
                if (native == null) return TimerWatch.Who(window);
                System.Reflection.FieldInfo owner = native.GetType().GetField("_owner", hidden) ?? native.GetType().GetField("owner", hidden);
                Timer timer = owner == null ? null : owner.GetValue(native) as Timer;
                if (timer == null) return native.GetType().Name;
                System.Reflection.FieldInfo tick = typeof(Timer).GetField("onTimer", hidden) ?? typeof(Timer).GetField("_onTimer", hidden);
                Delegate runs = tick == null ? null : tick.GetValue(timer) as Delegate;
                List<string> names = new List<string>();
                if (runs != null) foreach (Delegate one in runs.GetInvocationList()) names.Add(one.Method.DeclaringType.FullName.Replace("CapriDesktop.", "") + "." + one.Method.Name);
                return "every " + timer.Interval + " ms: " + string.Join(", ", names.ToArray());
            }
            catch (Exception caughtOne) { return "unnamed (" + caughtOne.GetType().Name + ")"; }
        }

        // Her clock's jobs by how often each ran between two readouts, busiest first, with its frames and moving count.
        private static List<string> ClockRuns(string before, string after)
        {
            Func<string, Dictionary<string, int>> runs = delegate(string readout)
            {
                Dictionary<string, int> found = new Dictionary<string, int>();
                foreach (string line in readout.Split('\n'))
                {
                    int colon = line.LastIndexOf(": ", StringComparison.Ordinal), space = colon < 0 ? -1 : line.IndexOf(" runs", colon, StringComparison.Ordinal);
                    int count;
                    if (space > colon && int.TryParse(line.Substring(colon + 2, space - colon - 2), out count)) found[line.Substring(0, colon)] = count;
                }
                return found;
            };
            Dictionary<string, int> from = runs(before), to = runs(after);
            List<KeyValuePair<string, int>> ran = new List<KeyValuePair<string, int>>();
            foreach (KeyValuePair<string, int> job in to)
            {
                int had;
                from.TryGetValue(job.Key, out had);
                if (job.Value > had) ran.Add(new KeyValuePair<string, int>(job.Key, job.Value - had));
            }
            ran.Sort(delegate(KeyValuePair<string, int> a, KeyValuePair<string, int> b) { return b.Value.CompareTo(a.Value); });
            List<string> lines = new List<string> { "clock: " + after.Split('\n')[0] };
            foreach (KeyValuePair<string, int> job in ran) lines.Add("  " + job.Value + "\t" + job.Key);
            return lines;
        }

        private static void Idle(int ms)
        {
            Stopwatch wait = Stopwatch.StartNew();
            while (wait.ElapsedMilliseconds < ms) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
        }

        // Every window of hers that is alive, shown or hidden.
        private static List<string> LiveForms()
        {
            List<string> live = new List<string>();
            foreach (Form one in Application.OpenForms) live.Add(one.GetType().Name + (one.Visible ? "" : " (hidden)"));
            live.Sort(StringComparer.Ordinal);
            return live;
        }

        // Her windows by name, each with what opens it.
        private KeyValuePair<string, Action>[] Openers(out int windows)
        {
            KeyValuePair<string, Action>[] opens =
            {
                new KeyValuePair<string, Action>("Toggles", OpenToggles), new KeyValuePair<string, Action>("Custom colours", OpenColours),
                new KeyValuePair<string, Action>("Personality", OpenPersonalitySettings), new KeyValuePair<string, Action>("Memory", OpenMemory),
                new KeyValuePair<string, Action>("Growth", OpenGrowth), new KeyValuePair<string, Action>("Security", OpenSecurity),
                new KeyValuePair<string, Action>("Extras", ShowExtras), new KeyValuePair<string, Action>("Voice settings", OpenLayers),
                new KeyValuePair<string, Action>("Mix", OpenMix), new KeyValuePair<string, Action>("Provider", OpenProvider),
                new KeyValuePair<string, Action>("Extra request JSON", OpenThinkingExtra), new KeyValuePair<string, Action>("Remote", OpenRemote),
                new KeyValuePair<string, Action>("Her GIFs", OpenGifs), new KeyValuePair<string, Action>("Log", OpenLog),
                new KeyValuePair<string, Action>("MD editor", delegate { capri.OpenFiles("me.md"); }),
                new KeyValuePair<string, Action>("Workspace", capri.OpenWorkspace),
                new KeyValuePair<string, Action>("Voice server", delegate { OpenServer(false, null); }), new KeyValuePair<string, Action>("Mods", BrowseMods),
            };
            windows = opens.Length;
            return opens;
        }

        // Each of her windows opened, closed and given a short grace: after it, the window is disposed, none of its timers
        // ticks, and nothing holds its controls (a static event still holding one keeps it alive). The faults, else null.
        internal string UnloadCheck(out int windows)
        {
            KeyValuePair<string, Action>[] opens = Openers(out windows);
            TimeSpan graceWas = CloseGrace;
            CloseGrace = TimeSpan.FromMilliseconds(300);
            List<string> wrong = new List<string>();
            List<KeyValuePair<string, WeakReference>> parts = new List<KeyValuePair<string, WeakReference>>();
            HashSet<string> kinds = new HashSet<string>();
            try
            {
                foreach (KeyValuePair<string, Action> open in opens)
                {
                    HashSet<Form> had = new HashSet<Form>();
                    foreach (Form one in Application.OpenForms) if (one.Visible) had.Add(one);
                    open.Value();
                    Idle(300);
                    Form shown = null;
                    foreach (Form one in Application.OpenForms) if (one.Visible && !had.Contains(one)) shown = one;
                    if (shown == null) { wrong.Add(open.Key + " didn't open"); continue; }
                    kinds.Add(shown.GetType().Name);
                    foreach (Control part in shown.Controls) parts.Add(new KeyValuePair<string, WeakReference>(open.Key, new WeakReference(part)));
                    shown.Close();
                    Stopwatch gone = Stopwatch.StartNew();
                    while (!shown.IsDisposed && gone.ElapsedMilliseconds < 5000) Idle(50);
                    if (!shown.IsDisposed) wrong.Add(open.Key + " (" + shown.GetType().Name + ") still made " + gone.ElapsedMilliseconds + " ms after closing");
                }
                TimerCount timers = new TimerCount();
                Application.AddMessageFilter(timers);
                Idle(3000);
                Application.RemoveMessageFilter(timers);
                foreach (KeyValuePair<IntPtr, int> seen in timers.Seen)
                {
                    string name = TimerName(seen.Key);
                    foreach (string kind in kinds) if (name.Contains("+" + kind + ".") || name.Contains("+" + kind + "+")) wrong.Add("a closed " + kind + "'s timer still ticks: " + name);
                }
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                int held = 0;
                Dictionary<string, List<string>> byWindow = new Dictionary<string, List<string>>();
                foreach (KeyValuePair<string, WeakReference> part in parts)
                {
                    Control alive = part.Value.Target as Control;
                    if (alive == null) continue;
                    held++;
                    List<string> seen;
                    if (!byWindow.TryGetValue(part.Key, out seen)) byWindow[part.Key] = seen = new List<string>();
                    if (seen.Count < 4) seen.Add(alive.GetType().Name + " '" + (alive.Text.Length > 0 ? alive.Text : alive.AccessibleName) + "'");
                }
                List<string> where = new List<string>();
                foreach (KeyValuePair<string, List<string>> one in byWindow) where.Add(one.Key + ": " + string.Join(", ", one.Value.ToArray()));
                if (held > 0) wrong.Add(held + " of the closed windows' " + parts.Count + " controls still held (" + string.Join("; ", where.ToArray()) + ")");
            }
            finally { CloseGrace = graceWas; }
            return wrong.Count == 0 ? null : string.Join("; ", wrong.ToArray());
        }

        // Every control of Settings, SETUP, the main page and each of her windows, one line each by area, kind and name, so two
        // builds can be compared and a control gone missing named (perf/controls.txt). The number of controls listed.
        internal int ControlInventory(string output)
        {
            string folder = Path.Combine(output, "perf");
            Directory.CreateDirectory(folder);
            List<string> lines = new List<string>();
            Func<Control, string> named = delegate(Control one)
            {
                string words = one.Text.Length > 0 ? one.Text : one.AccessibleName ?? "";
                return one.GetType().Name + "\t" + words.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
            };
            Action<string, IEnumerable<Control>> list = delegate(string area, IEnumerable<Control> controls)
            {
                List<string> mine = new List<string>();
                foreach (Control one in controls) mine.Add(area + "\t" + named(one));
                mine.Sort(StringComparer.Ordinal);
                lines.AddRange(mine);
            };
            Func<Control, List<Control>> every = null;
            every = delegate(Control within)
            {
                List<Control> found = new List<Control>();
                foreach (Control one in within.Controls) { found.Add(one); found.AddRange(every(one)); }
                return found;
            };
            List<Control> page = new List<Control>(), setup = new List<Control>(), main = new List<Control>();
            foreach (KeyValuePair<Control, int> entry in pageLeft) page.Add(entry.Key);
            foreach (KeyValuePair<Control, int> entry in setupLeft) setup.Add(entry.Key);
            foreach (KeyValuePair<Control, int> entry in mainLeft) main.Add(entry.Key);
            list("Settings", page);
            list("SETUP", setup);
            list("main page", main);
            int unused;
            foreach (KeyValuePair<string, Action> open in Openers(out unused))
            {
                HashSet<Form> had = new HashSet<Form>();
                foreach (Form one in Application.OpenForms) if (one.Visible) had.Add(one);
                open.Value();
                Idle(300);
                Form shown = null;
                foreach (Form one in Application.OpenForms) if (one.Visible && !had.Contains(one)) shown = one;
                if (shown == null) { lines.Add(open.Key + "\t(did not open)"); continue; }
                list(open.Key, every(shown));
                shown.Close();
                Stopwatch gone = Stopwatch.StartNew();
                while (!shown.IsDisposed && shown.Visible && gone.ElapsedMilliseconds < 5000) Idle(50);
            }
            File.WriteAllLines(Path.Combine(folder, "controls.txt"), lines.ToArray());
            return lines.Count;
        }

        // Each Mainframe dropdown and her right-click menu opened cold and again, timed from the call to its first paint.
        // The ones over 100 ms on their first open, named; the times go to perf/dropdown-opens.txt.
        internal List<string> DropdownOpens(string output)
        {
            string folder = Path.Combine(output, "perf");
            Directory.CreateDirectory(folder);
            List<string> lines = new List<string>(), slow = new List<string>();
            bool pagedWas = paged;
            try
            {
                Show();
                ShowSettings(true);
                Idle(300);
                List<KeyValuePair<string, ToolStripDropDown>> menus = new List<KeyValuePair<string, ToolStripDropDown>>();
                foreach (ContextMenuStrip strip in Dropdowns) menus.Add(new KeyValuePair<string, ToolStripDropDown>(strip.AccessibleName ?? "(unnamed)", strip));
                ContextMenuStrip herMenu = typeof(Companion).GetField("menu", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(capri) as ContextMenuStrip;
                if (herMenu != null) menus.Add(new KeyValuePair<string, ToolStripDropDown>("her right-click menu", herMenu));
                foreach (KeyValuePair<string, ToolStripDropDown> one in menus)
                {
                    string times = "";
                    bool cold = !one.Value.IsHandleCreated;
                    for (int round = 0; round < 2; round++)
                    {
                        Stopwatch open = Stopwatch.StartNew();
                        one.Value.Show(this, new Point(Scaled(40), Scaled(80)));
                        one.Value.Update();
                        long ms = open.ElapsedMilliseconds;
                        times += (round == 0 ? "" : " / ") + ms + " ms";
                        one.Value.Close();
                        Idle(100);
                        if (round == 0 && cold && ms > 100) slow.Add(one.Key + " " + ms + " ms");
                    }
                    lines.Add(one.Key + "\t" + times + (cold ? "" : "\t(already made)"));
                }
            }
            finally { ShowSettings(pagedWas); }
            File.WriteAllLines(Path.Combine(folder, "dropdown-opens.txt"), lines.ToArray());
            return slow;
        }

        // Idle for a while with the Mainframe hidden: memory, handles, CPU and the timers that fire, then each of five windows
        // opened twice and closed. The report goes to perf/idle-use.txt; the summary line is returned.
        internal string IdleUse(string output)
        {
            string folder = Path.Combine(output, "perf");
            Directory.CreateDirectory(folder);
            StringBuilder report = new StringBuilder();
            KeyValuePair<string, Action>[] windows =
            {
                new KeyValuePair<string, Action>("Toggles", OpenToggles),
                new KeyValuePair<string, Action>("Custom colours", OpenColours),
                new KeyValuePair<string, Action>("Personality", OpenPersonalitySettings),
                new KeyValuePair<string, Action>("Memory", OpenMemory),
                new KeyValuePair<string, Action>("MD editor", delegate { capri.OpenFiles("me.md"); }),
            };
            Func<string, string> idle = delegate(string when)
            {
                Hide();
                Idle(3000);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                TimerCount timers = new TimerCount();
                Application.AddMessageFilter(timers);
                TimeSpan cpuFrom;
                using (Process own = Process.GetCurrentProcess()) cpuFrom = own.TotalProcessorTime;
                string clockFrom = Clock.Readout();
                Stopwatch took = Stopwatch.StartNew();
                Idle(10000);
                List<string> clockRuns = ClockRuns(clockFrom, Clock.Readout());
                double cpu;
                using (Process own = Process.GetCurrentProcess()) cpu = (own.TotalProcessorTime - cpuFrom).TotalMilliseconds / took.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100;
                Application.RemoveMessageFilter(timers);
                WindowUse use = WindowUse.Read();
                int ticks = 0;
                List<string> byWindow = new List<string>();
                foreach (KeyValuePair<IntPtr, int> seen in timers.Seen)
                {
                    ticks += seen.Value;
                    byWindow.Add(seen.Value + "\t" + TimerName(seen.Key));
                }
                byWindow.Sort(delegate(string a, string b) { return int.Parse(b.Split('\t')[0]).CompareTo(int.Parse(a.Split('\t')[0])); });
                List<string> live = LiveForms();
                report.AppendLine("== " + when);
                report.AppendLine(use.ToString());
                report.AppendLine("idle CPU " + cpu.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "% of the machine over 10 s");
                report.AppendLine("WM_TIMER in 10 s: " + ticks);
                foreach (string line in byWindow) report.AppendLine("  " + line);
                foreach (string line in clockRuns) report.AppendLine(line);
                int strips = 0, made = 0;
                foreach (ContextMenuStrip strip in Dropdowns) { strips++; if (strip.IsHandleCreated) made++; }
                report.AppendLine("Mainframe dropdowns: " + strips + ", " + made + " with their window made");
                // Her right-click menu: its items and how many of its menus have a window made.
                ContextMenuStrip herMenu = typeof(Companion).GetField("menu", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(capri) as ContextMenuStrip;
                int items = 0, menus = 0, menusMade = 0;
                Action<ToolStrip> walk = null;
                walk = delegate(ToolStrip strip)
                {
                    menus++;
                    if (strip.IsHandleCreated) menusMade++;
                    foreach (ToolStripItem item in strip.Items)
                    {
                        items++;
                        ToolStripMenuItem holder = item as ToolStripMenuItem;
                        if (holder != null && holder.HasDropDownItems) walk(holder.DropDown);
                    }
                };
                if (herMenu != null) walk(herMenu);
                report.AppendLine("her right-click menu: " + items + " items in " + menus + " menus, " + menusMade + " with their window made");
                report.AppendLine("windows alive: " + live.Count);
                foreach (string line in live) report.AppendLine("  " + line);
                return "working set " + (use.Working >> 20) + " MB, handles " + use.Handles + ", USER " + use.User + ", GDI " + use.Gdi + ", CPU "
                    + cpu.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "%, " + ticks + " timer ticks in 10 s, " + live.Count + " windows alive";
            };
            FormWindowState stateWas = WindowState;
            bool shownWas = Visible;
            string before = idle("idle at start");
            List<string> opens = new List<string>();
            foreach (KeyValuePair<string, Action> window in windows)
            {
                string times = "";
                for (int round = 0; round < 2; round++)
                {
                    HashSet<Form> had = new HashSet<Form>();
                    foreach (Form one in Application.OpenForms) if (one.Visible) had.Add(one);
                    Stopwatch open = Stopwatch.StartNew();
                    window.Value();
                    Form shown = null;
                    foreach (Form one in Application.OpenForms) if (one.Visible && !had.Contains(one)) shown = one;
                    if (shown != null) shown.Update();
                    long ms = open.ElapsedMilliseconds;
                    times += (round == 0 ? "" : " / ") + ms + " ms";
                    Idle(400);
                    if (shown != null) shown.Close();
                    // The MD editor closes with its own short animation; each one is let finish.
                    Stopwatch gone = Stopwatch.StartNew();
                    while (shown != null && !shown.IsDisposed && shown.Visible && gone.ElapsedMilliseconds < 4000) Idle(50);
                    Idle(400);
                }
                opens.Add(window.Key + " " + times);
            }
            report.AppendLine("== opening, first / again");
            foreach (string line in opens) report.AppendLine(line);
            string after = idle("idle after opening and closing the five");
            File.WriteAllText(Path.Combine(folder, "idle-use.txt"), report.ToString());
            if (shownWas) Show();
            WindowState = stateWas;
            return "at start: " + before + "; open (first / again): " + string.Join(", ", opens.ToArray()) + "; after closing them: " + after;
        }
#endif
    }
}
