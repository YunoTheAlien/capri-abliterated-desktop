using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace CapriDesktop
{
    // A new user's first start, again and again: this build on an empty folder, as togithub-bench starts it, has to reach
    // her first frame every time, and two at once on their own folders both do.
    internal sealed partial class Companion
    {
#if CHECKS
        internal const int FreshStartTimes = 10;

        private void SmokeFreshStarts(List<string> checks, string output)
        {
            List<string> wrong = new List<string>();
            string exe = System.Windows.Forms.Application.ExecutablePath;
            for (int one = 1; one <= FreshStartTimes; one++)
            {
                string why = FreshStart(exe, Path.Combine(output, "fresh-starts", "start-" + one));
                if (why != null) wrong.Add("start " + one + ": " + why);
            }
            // Two at once, each on its own folder: neither sends the other away.
            Process[] both = new Process[2];
            string[] folders = { Path.Combine(output, "fresh-starts", "pair-1"), Path.Combine(output, "fresh-starts", "pair-2") };
            try
            {
                for (int one = 0; one < 2; one++) both[one] = Launch(exe, folders[one]);
                for (int one = 0; one < 2; one++)
                {
                    string why = FirstFrame(both[one], folders[one]);
                    if (why != null) wrong.Add("pair " + (one + 1) + ": " + why);
                }
            }
            finally { foreach (Process run in both) Close(run); }
            if (wrong.Count > 0) throw new Exception("A fresh start didn't reach her first frame: " + string.Join("; ", wrong.ToArray()));
            checks.Add("PASS a fresh start on an empty folder reaches her first frame " + FreshStartTimes + " times in " + FreshStartTimes + ", and two at once on their own folders both do");
        }

        private static string FreshStart(string exe, string folder)
        {
            Process run = Launch(exe, folder);
            try { return FirstFrame(run, folder); }
            finally { Close(run); }
        }

        // Started the way togithub-bench starts her: an empty folder with the doors marked seen, so nothing takes the screen.
        private static Process Launch(string exe, string folder)
        {
            Directory.CreateDirectory(Path.Combine(folder, "mods", "door-intro-data"));
            File.WriteAllText(Path.Combine(folder, "mods", "door-intro-data", "shown"), "bench");
            Directory.CreateDirectory(Path.Combine(folder, "mods", "door2-intro-data"));
            File.WriteAllText(Path.Combine(folder, "mods", "door2-intro-data", "shown"), "bench");
            return Process.Start(new ProcessStartInfo(exe, "--bench \"" + folder + "\"") { UseShellExecute = false });
        }

        // Null once "First frame on screen" is in her log; else how she ended, or that she never got there.
        private static string FirstFrame(Process run, string folder)
        {
            string log = Path.Combine(folder, "errors.txt");
            Stopwatch waiting = Stopwatch.StartNew();
            while (waiting.Elapsed.TotalSeconds < 60)
            {
                if (File.Exists(log))
                {
                    string text;
                    try { text = File.ReadAllText(log); }
                    catch (IOException) { text = ""; }
                    if (text.IndexOf("First frame on screen", StringComparison.Ordinal) >= 0) return null;
                }
                if (run.HasExited) return "she exited with " + run.ExitCode + " before her first frame" + LastLines(log);
                Thread.Sleep(200);
            }
            return "no first frame in 60 s" + LastLines(log);
        }

        private static string LastLines(string log)
        {
            try
            {
                if (!File.Exists(log)) return ", no log";
                string[] rows = File.ReadAllLines(log);
                return ", her log ends: " + string.Join(" / ", rows, Math.Max(0, rows.Length - 3), Math.Min(3, rows.Length));
            }
            catch (IOException) { return ""; }
        }

        // Ended by its own id, never by name: the PC's own Capri may be running beside it. Only her first frame is wanted, so a
        // bench folder needs nothing saved and she is ended at once, keeping ten starts inside the section's time.
        private static void Close(Process run)
        {
            if (run == null) return;
            try
            {
                if (!run.HasExited) { run.Kill(); run.WaitForExit(5000); }
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            run.Dispose();
        }
#endif
    }
}
