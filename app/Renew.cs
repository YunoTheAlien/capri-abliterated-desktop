using System;
using System.Collections.Generic;
using System.IO;

namespace CapriDesktop
{
    // Her files on an update: each shipped file written new from stock, kept first as a .bak when they edited it or it
    // has no record; her own (me.md, settings.json) never. .built's three version records let an unattached side catch
    // up later.
    internal static class Renew
    {
        internal const string StampName = ".built";
        private const string SharedKey = "shared", CensoredKey = "censored", SenpaiKey = "senpai";

        private static readonly object gate = new object();
        private static string stamp, version;
        private static bool sharedDue, censoredDue, senpaiDue;
        private static readonly List<string> spared = new List<string>();
        // What this start wrote: files replaced, and files she made this start, which are hers and never spared. Each is
        // recorded as it stands once the start is done, after anything else of hers has written into it.
        private static readonly List<string> written = new List<string>();
        private static DateTime startedAt;

        // How many files this start wrote new over theirs, for her offer to put their settings back.
        internal static int Replaced { get { lock (gate) return replaced; } }
        private static int replaced;

        // Whether the senpai side can be written this start: the switch on and its half attached.
        internal static Func<bool> SenpaiReady = delegate { return false; };

        // Whether the censored side's own files are the ones in use this start.
        internal static Func<bool> CensoredReady = delegate { return false; };

        // Read once at the start, before anything of hers is written.
        internal static void Begin(string settings, string built)
        {
            lock (gate)
            {
                stamp = string.IsNullOrEmpty(settings) ? null : Path.Combine(settings, StampName);
                version = built ?? "";
                Dictionary<string, string> had = Read();
                string was;
                sharedDue = !had.TryGetValue(SharedKey, out was) || was != version;
                before = was;
                censoredDue = !had.TryGetValue(CensoredKey, out was) || was != version;
                senpaiDue = !had.TryGetValue(SenpaiKey, out was) || was != version;
                spared.Clear();
                written.Clear();
                replaced = 0;
                startedAt = DateTime.UtcNow;
            }
        }

        private static Dictionary<string, string> Read()
        {
            Dictionary<string, string> had = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (stamp == null || !File.Exists(stamp)) return had;
                foreach (string row in File.ReadAllLines(stamp))
                {
                    int tab = row.IndexOf('\t');
                    if (tab > 0) had[row.Substring(0, tab).Trim()] = row.Substring(tab + 1).Trim();
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return had;
        }

        // Which record a file is under: anything in a folder named for the senpai side is that side's (her packs, moods and
        // interests files too), the censored side's folder in her settings folder is that side's, and the rest is shared.
        private static string Key(string path)
        {
            string parent = Path.GetDirectoryName(path ?? "") ?? "";
            string folder = Path.GetFileName(parent), above = Path.GetFileName(Path.GetDirectoryName(parent) ?? "");
            if (string.Equals(folder, Personalities.SenpaiName, StringComparison.OrdinalIgnoreCase)) return SenpaiKey;
            if (string.Equals(folder, Personalities.PlainName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(above, Personalities.SettingsName, StringComparison.OrdinalIgnoreCase)) return CensoredKey;
            return SharedKey;
        }

        private static bool Ready(Func<bool> ready)
        {
            try { return ready(); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); return false; }
        }

        // Whether this start is a new version for any of her files: a side due, with files of hers already on disk.
        // The version her files were written for before this start, from her settings folder's record; null on a first start.
        private static string before;
        internal static string Before { get { lock (gate) return before; } }

        internal static bool Changing { get { lock (gate) return stamp != null && (sharedDue || censoredDue || senpaiDue); } }

        internal static bool Due(string path)
        {
            string key = Key(path);
            lock (gate)
            {
                if (stamp == null) return false;
                if (key == SharedKey) return sharedDue;
                if (key == CensoredKey && !censoredDue || key == SenpaiKey && !senpaiDue) return false;
            }
            return Ready(key == SenpaiKey ? SenpaiReady : CensoredReady);
        }

        // The text as compared: line ends evened out, and her reset to stock line and its note left out, since she adds
        // those to every file she reads and the stock she ships does not carry them.
        private static readonly System.Text.RegularExpressions.Regex ResetLines = new System.Text.RegularExpressions.Regex(@"(?mi)^[ \t]*" + System.Text.RegularExpressions.Regex.Escape(Notes.ResetKey) + @"[ \t]*:[^\n]*\n(?:>! Set that to 1 and this file goes back[^\n]*\n)?");

        private static string Plain(string text)
        {
            string even = (text ?? "").TrimStart((char)0xFEFF).Replace("\r\n", "\n").Replace('\r', '\n');
            return ResetLines.Replace(even, "").TrimEnd();
        }

        // For a failing check: each spare of a file, its time, and whether it says what the file says, so a run that fails
        // on one PC shows why.
        private static string SpareSays(string path)
        {
            try
            {
                string now = Plain(Notes.ReadAll(path));
                List<string> said = new List<string>();
                foreach (string one in Notes.SparesForTest(path))
                    said.Add(Path.GetFileName(one) + " at " + SpareFiles.When(one) + (Plain(Notes.ReadAll(one)) == now ? " same" : " differs") + (Notes.Untouched(path) ? "" : ", file not untouched"));
                return said.Count == 0 ? "no spares" : string.Join(", ", said.ToArray());
            }
            catch (Exception caughtOne) { return "spares unreadable: " + caughtOne.Message; }
        }

        // Spares of this file that say what it says now are no way back to anything, so they go, and of spares that say the
        // same as each other only the newest stays.
        private static void Unspare(string path)
        {
            try
            {
                string now = Plain(Notes.ReadAll(path));
                List<string> all = new List<string>(Notes.SparesForTest(path));
                all.Sort(delegate(string a, string b) { return SpareFiles.When(b).CompareTo(SpareFiles.When(a)); });
                HashSet<string> kept = new HashSet<string>();
                foreach (string one in all)
                {
                    string said = Plain(Notes.ReadAll(one));
                    if (said != now && kept.Add(said)) continue;
                    // A scanner or another reader can hold a file for a moment; a few short tries outlast that, and the
                    // last one says why in her log rather than leaving a copy behind in silence.
                    for (int tries = 0; ; tries++)
                    {
                        try { File.Delete(one); break; }
                        catch (IOException) { if (tries >= 4) throw; System.Threading.Thread.Sleep(100); }
                        catch (UnauthorizedAccessException) { if (tries >= 4) throw; System.Threading.Thread.Sleep(100); }
                    }
                    lock (gate) spared.Remove(one);
                }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // One file she ships, on an update: theirs kept as a .bak when it is not what she last wrote, then hers written new
        // and recorded. True when it was written.
        internal static bool Replace(string path, string stock)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(stock) || !File.Exists(path) || !Due(path)) return false;
            try
            {
                // Made by her this start, a new install's files included: nothing of theirs is in it.
                if (File.GetCreationTimeUtc(path) >= startedAt) { lock (gate) if (!written.Contains(path)) written.Add(path); return false; }
                // Already what she ships: recorded as it stands, reset line and all, so it reads as untouched from now on.
                if (Plain(Notes.ReadAll(path)) == Plain(stock)) { Notes.Shipped(path, Notes.ReadAll(path)); Unspare(path); return false; }
                string spare = Notes.Spare(path);
                // Written with the reset line she adds on reading it, so her reading it does not make it theirs.
                string put = Notes.Marked(stock);
                Notes.Overwrite(path, put);
                Notes.Shipped(path, put);
                lock (gate)
                {
                    if (spare != null) spared.Add(spare);
                    if (!written.Contains(path)) written.Add(path);
                    replaced++;
                }
                Unspare(path);
                return true;
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); return false; }
        }

        // A file with no record yet that still says exactly what she ships gets one, so the next update knows nobody has
        // touched it and needs no copy of it.
        internal static void Adopt(string path, string stock)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(stock) || !File.Exists(path) || Notes.Recorded(path)) return;
                if (Plain(Notes.ReadAll(path)) == Plain(stock)) Notes.Shipped(path, Notes.ReadAll(path));
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // Written fresh: what she wrote is recorded at once.
        internal static void Wrote(string path, string text) { Notes.Shipped(path, text); }

        // Every file this start could write is done: each side that was written gets this version, and the .bak files
        // kept along the way come back for her to tell them about.
        internal static string[] Finish()
        {
            lock (gate)
            {
                if (stamp == null) return new string[0];
                foreach (string path in written)
                {
                    try { if (File.Exists(path)) Notes.Shipped(path, Notes.ReadAll(path)); }
                    catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                }
                written.Clear();
                Dictionary<string, string> had = Read();
                bool censoredDone = censoredDue && Ready(CensoredReady), senpaiDone = senpaiDue && Ready(SenpaiReady);
                if (sharedDue) had[SharedKey] = version;
                if (censoredDone) had[CensoredKey] = version;
                if (senpaiDone) had[SenpaiKey] = version;
                if (sharedDue || censoredDone || senpaiDone)
                {
                    try
                    {
                        List<string> rows = new List<string>();
                        foreach (KeyValuePair<string, string> one in had) rows.Add(one.Key + "\t" + one.Value);
                        rows.Sort(StringComparer.OrdinalIgnoreCase);
                        Directory.CreateDirectory(Path.GetDirectoryName(stamp));
                        File.WriteAllText(stamp, string.Join(Environment.NewLine, rows.ToArray()) + Environment.NewLine);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                sharedDue = false;
                if (censoredDone) censoredDue = false;
                if (senpaiDone) senpaiDue = false;
                string[] kept = spared.ToArray();
                spared.Clear();
                return kept;
            }
        }

        // On a version change: untouched replaced with no copy, edited kept as a .bak then replaced, no record counted as
        // edited, the senpai side only with the switch on, the side's record written once and the next start writing
        // nothing; files she writes for herself are never in it.
        internal static void Check(List<string> checks, string folder)
        {
            string settings = Path.Combine(folder, "renew-" + Guid.NewGuid().ToString("N").Substring(0, 6), "personalSettings");
            string side = Path.Combine(settings, Personalities.PlainName), senpai = Path.Combine(settings, Personalities.SenpaiName);
            Directory.CreateDirectory(side);
            Directory.CreateDirectory(senpai);
            Func<bool> readyWas = SenpaiReady, censoredWas = CensoredReady;
            string stampWas, versionWas;
            bool sharedWas, censoredDueWas, senpaiWas;
            List<string> sparedWas;
            lock (gate) { stampWas = stamp; versionWas = version; sharedWas = sharedDue; censoredDueWas = censoredDue; senpaiWas = senpaiDue; sparedWas = new List<string>(spared); }
            string stock = "# Her lines\r\n\r\n## hello\r\nhey there\r\n", old = "# Her lines\r\n\r\n## hello\r\nhey\r\n";
            string untouched = Path.Combine(side, "lines.md"), edited = Path.Combine(side, "moments.md"), unknown = Path.Combine(side, "touch.md");
            string theirs = Path.Combine(side, "me.md"), hers = Path.Combine(senpai, "lines.md");
            try
            {
                File.WriteAllText(untouched, old);
                Notes.Shipped(untouched, old);
                File.WriteAllText(edited, old + "their line\r\n");
                Notes.Shipped(edited, old);
                File.WriteAllText(unknown, old);
                File.WriteAllText(theirs, "# Me\r\nname: Sam\r\n");
                File.WriteAllText(hers, old);
                Notes.Shipped(hers, old);
                bool ready = false;
                SenpaiReady = delegate { return ready; };
                CensoredReady = delegate { return !ready; };

                Begin(settings, "9.9.1");
                foreach (string one in new[] { untouched, edited, unknown, hers }) Replace(one, stock);
                string[] kept = Finish();
                Func<string, int> spares = delegate(string path) { return Notes.SparesForTest(path).Length; };
                bool replaced = Plain(File.ReadAllText(untouched)) == Plain(stock) && spares(untouched) == 0;
                bool spareEdited = Plain(File.ReadAllText(edited)) == Plain(stock) && spares(edited) == 1 && File.ReadAllText(Notes.SparesForTest(edited)[0]).Contains("their line");
                bool spareUnknown = Plain(File.ReadAllText(unknown)) == Plain(stock) && spares(unknown) == 1;
                bool senpaiWaits = File.ReadAllText(hers) == old;
                bool meKept = File.ReadAllText(theirs) == "# Me\r\nname: Sam\r\n";
                bool told = kept.Length == 2;
                if (!replaced || !spareEdited || !spareUnknown || !senpaiWaits || !meKept || !told)
                    throw new Exception("An update's rewrite went wrong: lines.md " + SpareSays(untouched) + "; moments.md " + SpareSays(edited) + "; touch.md " + SpareSays(unknown) + "; kept " + string.Join(", ", Array.ConvertAll(kept, Path.GetFileName)) + "; untouched replaced with no copy " + replaced + ", edited kept then replaced " + spareEdited + ", no record kept " + spareUnknown + ", senpai side waited for the switch " + senpaiWaits + ", me.md left " + meKept + ", two kept " + told + " (" + kept.Length + " kept)");

                // A new install: a file she makes after the start began is hers, never spared, and recorded as it stands.
                Begin(settings, "9.9.0");
                string made = Path.Combine(side, "watching.md");
                File.WriteAllText(made, old + "added by her this start\r\n");
                Replace(made, stock);
                kept = Finish();
                if (kept.Length != 0 || spares(made) != 0 || !File.ReadAllText(made).Contains("added by her") || !Notes.Untouched(made))
                    throw new Exception("A file she made this start was spared or replaced as if it were theirs.");
                Begin(settings, "9.9.1");
                kept = Finish();

                // The switch on later, same version: only the senpai side is still due, and it goes untouched with no copy.
                ready = true;
                Begin(settings, "9.9.1");
                File.WriteAllText(edited, old + "edited again\r\n");
                foreach (string one in new[] { untouched, edited, hers }) Replace(one, stock);
                kept = Finish();
                bool senpaiNow = Plain(File.ReadAllText(hers)) == Plain(stock) && spares(hers) == 0;
                bool plainLeft = File.ReadAllText(edited).Contains("edited again") && kept.Length == 0;
                // A third start of the same version writes nothing at all.
                Begin(settings, "9.9.1");
                bool quiet = !Due(untouched) && !Due(hers);
                Finish();
                // The next version: what nobody touched since goes with no copy; their edit is kept.
                ready = false;
                Begin(settings, "9.9.2");
                bool next = Due(untouched) && !Due(hers);
                Replace(untouched, stock + "a new line\r\n");
                Replace(edited, stock + "a new line\r\n");
                kept = Finish();
                bool nextRight = spares(untouched) == 0 && kept.Length == 1 && spares(edited) == 2;
                if (!senpaiNow || !plainLeft || !quiet || !next || !nextRight)
                    throw new Exception("An update's rewrite went wrong on later starts: senpai side once the switch was on " + senpaiNow + ", plain side left until the next version " + plainLeft + ", a third start quiet " + quiet + ", the next version due on the side in use only " + next + ", untouched with no copy and edited kept " + nextRight);

                // Her reset line, added as she reads a file, does not make it theirs: a file written new and read stays
                // untouched, two passes in one start keep nothing, and a spare that says what the file says goes.
                string mood = Path.Combine(side, "Calm.md"), moodStock = "# Calm\r\n\r\n## When you pet her\r\nMm.\r\n";
                File.WriteAllText(mood, Notes.Marked(moodStock));
                Notes.Shipped(mood, moodStock);
                File.WriteAllText(mood + ".1700000000.bak", Notes.Marked(moodStock));
                Begin(settings, "9.9.3");
                Replace(mood, moodStock);
                Notes.Stock(mood, moodStock);
                Replace(mood, moodStock);
                kept = Finish();
                bool readStays = spares(mood) == 0 && kept.Length == 0 && Notes.Untouched(mood);
                Begin(settings, "9.9.4");
                Replace(mood, moodStock + "Stay.\r\n");
                Notes.Stock(mood, moodStock + "Stay.\r\n");
                Replace(mood, moodStock + "Stay.\r\n");
                kept = Finish();
                bool newStays = spares(mood) == 0 && kept.Length == 0 && Notes.Untouched(mood) && File.ReadAllText(mood).Contains("Stay.");
                // Spares that say the same as each other: only the newest stays, beside the copy of their edit.
                string twice = Path.Combine(side, "Moody.md");
                File.WriteAllText(twice, "# Moody\r\nmy own edit\r\n");
                File.WriteAllText(twice + ".1700000000.bak", "# Moody\r\nan older edit\r\n");
                File.WriteAllText(twice + ".1700000001.bak", "# Moody\r\nan older edit\r\n");
                Begin(settings, "9.9.5");
                Replace(twice, moodStock);
                Finish();
                bool oneOfEach = spares(twice) == 2;
                if (!readStays || !newStays || !oneOfEach)
                    throw new Exception("Her reset line made a file theirs, or copies piled up: " + SpareSays(mood) + "; same stock with no copy " + readStays + " (" + spares(mood) + " spares), new stock with no copy " + newStays + ", one of each copy " + oneOfEach + " (" + spares(twice) + ")");
                checks.Add("PASS on an update every file she ships is written new: one nobody touched is replaced with no copy, an edited one or one with no record is kept as a .bak first, one she made this start is never spared, the senpai side waits until the switch is on, each side is written once per version, and me.md and the other files she writes are never touched; her reset line added on reading keeps a file untouched, so no copy is made of it");
            }
            finally
            {
                SenpaiReady = readyWas;
                CensoredReady = censoredWas;
                lock (gate) { stamp = stampWas; version = versionWas; sharedDue = sharedWas; censoredDue = censoredDueWas; senpaiDue = senpaiWas; spared.Clear(); spared.AddRange(sparedWas); }
                try { Directory.Delete(Path.GetDirectoryName(settings), true); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
        }
    }
}
