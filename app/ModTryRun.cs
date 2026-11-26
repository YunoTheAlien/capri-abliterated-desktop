#if CHECKS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CapriDesktop
{
    // Test build only: one imported mod on and every other off, her bench start for some seconds, then one report line:
    // whether it started and is still running, what it threw and what her guard wrote for it. For a test PC's loop.
    internal static class ModTryRun
    {
        internal static string Id;
        private static string report, data;
        private static int seconds;
        private static DateTime logFrom;
        private static string toggled = "";

        // --try-mod <data folder> <mod id> <seconds> [report file]: the folder is set up, and the start goes on as --bench.
        internal static string[] PrepareModTry(string[] args)
        {
            int wait;
            if (args.Length < 4 || args.Length > 5 || !int.TryParse(args[3], out wait) || wait < 5 || wait > 3600)
            {
                Console.Error.WriteLine("Use: Capri.exe --try-mod <data folder> <mod id> <seconds, 5 to 3600> [report file]");
                return null;
            }
            data = Path.GetFullPath(args[1]);
            Id = args[2];
            seconds = wait;
            report = args.Length == 5 ? Path.GetFullPath(args[4]) : Path.Combine(data, "try-report.txt");
            Directory.CreateDirectory(data);
            // As their "load untested mods" switch would, for this run only: store mods are not on her checked list.
            Mods.UntestedForChecks = true;
            Mods.Stock(data);
            Mods list = new Mods(data);
            if (!list.All.Exists(mod => mod.File.Id == Id))
            {
                Console.Error.WriteLine(Id + " isn't imported in " + data + ". Run --import-mod first.");
                return null;
            }
            string folder = list.Folder;
            List<string> off = new List<string>();
            foreach (Mod mod in list.All) if (mod.File.Id != Id) off.Add(mod.File.Id);
            File.WriteAllLines(Path.Combine(folder, "off.txt"), off.ToArray());
            // Door 2 counts as shown, so a fresh folder never plays it over the screen.
            string front = Path.Combine(Mods.HomeIn(folder, Mods.FrontId), Mods.FrontId + "-data");
            Directory.CreateDirectory(front);
            File.WriteAllText(Path.Combine(front, "shown"), "");
            string log = Path.Combine(data, "errors.txt");
            // By time, not by length: her start trims the log to its last runs, so a length taken now can point past
            // her own "Started" line.
            logFrom = DateTime.Now.AddSeconds(-1);
            return new[] { "--bench", data };
        }

        internal static void Watch(Companion app)
        {
            Clock.Ticker done = new Clock.Ticker("ModTryRun done") { Interval = seconds * 1000 };
            done.Tick += delegate
            {
                done.Stop();
                bool running = app.ModRunningNow(Id);
                try { toggled = app.TryToggles(Id); } catch (Exception broke) { toggled = "Toggles rows threw: " + broke.GetType().Name + ": " + broke.Message; }
                // The rows' changes get a moment to show up in her log before it is read.
                for (int i = 0; i < 20; i++) { System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(50); }
                try { Write(running); } catch (Exception broke) { Console.Error.WriteLine("The try's report failed: " + broke.Message); }
                app.Quit();
            };
            done.Start();
        }

        private static void Write(bool running)
        {
            string mark = "mod " + Id;
            List<string> thrown = new List<string>();
            string log = Path.Combine(data, "errors.txt");
            if (File.Exists(log))
                using (FileStream file = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    foreach (string line in new StreamReader(file, Encoding.UTF8).ReadToEnd().Split('\n'))
                    {
                        DateTime when;
                        if (line.Length < 19 || !DateTime.TryParseExact(line.Substring(0, 19), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out when) || when < logFrom) continue;
                        if (line.IndexOf(mark, StringComparison.Ordinal) >= 0 || line.IndexOf(Id, StringComparison.OrdinalIgnoreCase) >= 0) thrown.Add(line.Trim());
                    }
                }
            List<string> guard = new List<string>();
            foreach (string name in new[] { ModGuard.GuardFile, ModGuard.WhyFile })
            {
                string path = Path.Combine(data, "mods", name);
                if (File.Exists(path)) foreach (string line in File.ReadAllLines(path)) if (line.IndexOf(Id, StringComparison.Ordinal) >= 0) guard.Add(name + ": " + line.Replace('\t', ' '));
            }
            string first = thrown.Count == 0 ? "" : thrown[0];
            if (first.Length > 300) first = first.Substring(0, 300);
            string said = Id + "\t" + (running ? "running" : "not running") + " after " + seconds + " s\t" + thrown.Count + " log line(s)\t"
                + first.Replace('\t', ' ') + "\t" + string.Join(" | ", guard.ToArray()) + "\t" + toggled;
            Directory.CreateDirectory(Path.GetDirectoryName(report));
            File.AppendAllText(report, said + Environment.NewLine, new UTF8Encoding(false));
            Console.WriteLine(said);
        }
    }
}
#endif
