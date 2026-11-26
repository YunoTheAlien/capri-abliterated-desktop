using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Whether a file she re-reads has changed, from memory: the first ask looks once, a watcher hears each save, and a
    // background pass every few seconds catches what it missed, so a slow disk never freezes her.
    internal static class Stamps
    {
        private const int PassMs = 3000, SettleMs = 60;

        // What was found: for a file its write time and length as one number (0 when it isn't there); for a folder its
        // files and a number that changes when any of them does.
        private sealed class Seen
        {
            internal long Stamp;
            internal string[] Files = new string[0];
        }

        private static readonly object gate = new object();
        private static readonly Dictionary<string, Seen> seen = new Dictionary<string, Seen>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, FileSystemWatcher> watchers = new Dictionary<string, FileSystemWatcher>(StringComparer.OrdinalIgnoreCase);
        private static readonly AutoResetEvent poke = new AutoResetEvent(false);
        private static Thread worker;
        private static int uiCalls, passes;

        // Disk calls made on her window's thread: one for each file or folder the first time it is asked about.
        internal static int UiCalls { get { return Interlocked.CompareExchange(ref uiCalls, 0, 0); } }

        // Passes the background thread has finished, so a check can wait for the next one.
        internal static int Passes { get { return Interlocked.CompareExchange(ref passes, 0, 0); } }

        internal static long Of(string path) { return string.IsNullOrEmpty(path) ? 0 : Get(path, false).Stamp; }

        internal static bool Exists(string path) { return Of(path) != 0; }

        // A folder's files, full paths in the order Windows lists them; empty when it isn't there.
        internal static string[] Files(string folder) { return string.IsNullOrEmpty(folder) ? new string[0] : Get(folder, true).Files; }

        internal static long FolderStamp(string folder) { return string.IsNullOrEmpty(folder) ? 0 : Get(folder, true).Stamp; }

        private static string Key(string path, bool folder) { return (folder ? "folder|" : "file|") + path; }

        private static Seen Get(string path, bool folder)
        {
            string key = Key(path, folder);
            Seen one;
            lock (gate) if (seen.TryGetValue(key, out one)) return one;
            // The first question about it looks now, on whichever thread asked.
            one = Look(path, folder) ?? new Seen();
            if (Application.MessageLoop) Interlocked.Increment(ref uiCalls);
            lock (gate)
            {
                Seen had;
                if (seen.TryGetValue(key, out had)) return had;
                seen[key] = one;
                string place = folder ? path : Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(place)) folders.Add(place);
                if (worker == null)
                {
                    worker = new Thread(Work) { IsBackground = true, Name = "Capri file stamps", Priority = ThreadPriority.BelowNormal };
                    worker.Start();
                }
            }
            poke.Set();
            return one;
        }

        // Null when the disk wouldn't answer, so a pass keeps what it had rather than calling a file gone.
        private static Seen Look(string path, bool folder)
        {
            try
            {
                if (!folder)
                {
                    FileInfo about = new FileInfo(path);
                    return new Seen { Stamp = about.Exists ? Mix(about.LastWriteTimeUtc.Ticks, about.Length) : 0 };
                }
                if (!Directory.Exists(path)) return new Seen();
                string[] files = Directory.GetFiles(path);
                long stamp = files.Length + 1;
                foreach (string file in files)
                {
                    FileInfo about = new FileInfo(file);
                    stamp = stamp * 31 + Mix(about.LastWriteTimeUtc.Ticks, about.Length) + file.Length;
                }
                return new Seen { Stamp = stamp == 0 ? 1 : stamp, Files = files };
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
        }

        private static long Mix(long ticks, long length)
        {
            long mixed = unchecked(ticks * 397 ^ length);
            return mixed == 0 ? 1 : mixed;
        }

        private static void Work()
        {
            while (true)
            {
                bool heard = poke.WaitOne(PassMs);
                // A save comes as several events; let them land and be read as one.
                if (heard) Thread.Sleep(SettleMs);
                try { Pass(); }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                Interlocked.Increment(ref passes);
            }
        }

        // Every file and folder asked about, looked at again; a folder not watched yet gets its watcher.
        private static void Pass()
        {
            List<string> keys, places;
            lock (gate)
            {
                keys = new List<string>(seen.Keys);
                places = new List<string>(folders);
            }
            foreach (string place in places)
            {
                bool watched;
                lock (gate) watched = watchers.ContainsKey(place);
                if (!watched) Watch(place);
            }
            foreach (string key in keys)
            {
                bool folder = key.StartsWith("folder|", StringComparison.Ordinal);
                Seen now = Look(key.Substring(key.IndexOf('|') + 1), folder);
                if (now == null) continue;
                lock (gate) seen[key] = now;
            }
        }

        private static void Watch(string place)
        {
            try
            {
                if (!Directory.Exists(place)) return;
                FileSystemWatcher watcher = new FileSystemWatcher(place) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime };
                FileSystemEventHandler heard = delegate { poke.Set(); };
                watcher.Changed += heard;
                watcher.Created += heard;
                watcher.Deleted += heard;
                watcher.Renamed += delegate { poke.Set(); };
                watcher.Error += delegate { poke.Set(); };
                watcher.EnableRaisingEvents = true;
                lock (gate) watchers[place] = watcher;
            }
            catch (IOException) { }
            catch (ArgumentException) { }
            catch (UnauthorizedAccessException) { }
        }

        // ---------- Check ----------

        // Her tick and her reactions ask from memory: after the first look, three hundred rounds of asking whether me.md,
        // a pack and a folder changed make no disk call on her window's thread, and a save, a new pack and a deleted file
        // are still seen within a few seconds.
#if CHECKS
        internal static void Check(List<string> checks, string output, Action round)
        {
            string place = Path.Combine(output, "stamps-" + Guid.NewGuid().ToString("N").Substring(0, 6)), packs = Path.Combine(place, "packs"), me = Path.Combine(place, "me.md");
            string watchedWas = Them.Watched;
            try
            {
                Directory.CreateDirectory(packs);
                File.WriteAllText(me, "- Name: Ada\n- awakeHours: 9-17\n", new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(packs, "pack1"), "# Bright\nHello.\n", new UTF8Encoding(false));
                Them.Watch(me);
                bool onUi = Application.MessageLoop;
                // The first look at each, then the rounds.
                string called = Them.Called, awake = Them.Awake, bright = Slots.Find(packs, "Bright");
                if (round != null) round();
                int before = UiCalls;
                for (int i = 0; i < 300; i++)
                {
                    called = Them.Called;
                    awake = Them.Awake;
                    bright = Slots.Find(packs, "Bright");
                    if (Slots.Find(packs, "Calm") != null || !Exists(me)) throw new Exception("A file was seen that isn't there, or one that is wasn't.");
                    if (round != null) round();
                }
                int made = UiCalls - before;
                if (!onUi || made != 0 || called != "Ada" || awake != "9-17" || bright == null || !bright.EndsWith("pack1", StringComparison.Ordinal))
                    throw new Exception("Asking whether her files changed went to the disk on her window's thread: " + made + " calls in 300 rounds (on her thread " + onUi + "), or read wrong: " + called + ", " + awake + ", " + bright);

                // Changes are still seen: a save to me.md, a new pack, me.md gone.
                File.WriteAllText(me, "- Name: Bea\n- awakeHours: 10-18\n", new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(packs, "pack2"), "# Calm\nHi.\n", new UTF8Encoding(false));
                string seenName = Wait(delegate { return Them.Called == "Bea" && Slots.Find(packs, "Calm") != null; }) ? "" : Them.Called + " / " + Slots.Find(packs, "Calm");
                if (seenName.Length > 0) throw new Exception("A save to me.md or a new pack wasn't seen within a few seconds: " + seenName);
                File.Delete(me);
                if (!Wait(delegate { return !Exists(me); })) throw new Exception("me.md taken away was still seen as there.");
                if (UiCalls - before != 0) throw new Exception("Seeing a change went to the disk on her window's thread: " + (UiCalls - before) + " calls.");
                checks.Add("PASS her tick and reactions ask from memory whether me.md, her pack and its folder changed: 300 rounds made no disk call to find out on her window's thread after the first look, and a save, a new pack and a deleted file were still seen within a few seconds");
            }
            finally
            {
                Them.Watch(watchedWas);
                try { if (Directory.Exists(place)) Directory.Delete(place, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
#endif

        // Up to ten seconds for a background pass to see it, pumping her window meanwhile.
        private static bool Wait(Func<bool> done)
        {
            DateTime until = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < until)
            {
                if (done()) return true;
                Application.DoEvents();
                Thread.Sleep(50);
            }
            return done();
        }
    }
}
