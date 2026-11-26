using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

#if CHECKS
namespace CapriDesktop
{
    internal sealed partial class Companion
    {
        // Her log a day a file: each rotation rule with made-up days and sizes, errors.txt moving in, a daily log keeping every
        // line, and Save log writing them all, newest last, with keys and their user folder's name taken out.
        private void SmokeLogs(List<string> checks, string output)
        {
            string root = Path.Combine(output, "logs-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string folderWas = Logs.Folder, logWas = Brain.LogPath;
            Func<string, DateTime> beganWas = Logs.Began;
            Func<string> pickWas = PickLogFile;
            Func<string, bool> binWas = Logs.Binned;
            // A stand-in bin, so a check never fills theirs: what goes is moved into a bin folder of its own.
            string bin = Path.Combine(root, "bin");
            Logs.Binned = delegate(string file)
            {
                Directory.CreateDirectory(bin);
                File.Move(file, Path.Combine(bin, Path.GetFileName(file) + "." + Guid.NewGuid().ToString("N").Substring(0, 6)));
                return true;
            };
            DateTime now = new DateTime(2026, 11, 21, 15, 0, 0);
            try
            {
                Func<string, string> fresh = delegate(string name)
                {
                    string folder = Path.Combine(root, name);
                    Directory.CreateDirectory(folder);
                    return folder;
                };
                Action<string, DateTime, long> made = delegate(string folder, DateTime day, long size)
                {
                    using (FileStream file = new FileStream(Logs.FileFor(folder, day), FileMode.Create, FileAccess.Write))
                    {
                        byte[] line = Encoding.ASCII.GetBytes(day.ToString("yyyy-MM-dd") + " 10:00:00 | test | - | a line\r\n");
                        file.Write(line, 0, line.Length);
                        if (size > line.Length) file.SetLength(size);
                    }
                };
                Func<string, string> left = delegate(string folder)
                {
                    List<string> names = new List<string>();
                    foreach (string file in Logs.Files(folder)) names.Add(Path.GetFileNameWithoutExtension(file).Substring(6));
                    return string.Join(",", names.ToArray());
                };
                Logs.Began = delegate { return now.AddHours(-1); };

                // a. Three days kept, the oldest goes.
                string three = fresh("three");
                for (int day = 18; day <= 21; day++) made(three, new DateTime(2026, 11, day), 100);
                Logs.Rotate(three, Logs.FileFor(three, now), now);
                if (left(three) != "2026-11-19,2026-11-20,2026-11-21") throw new Exception("Her logs didn't keep three days: " + left(three));
                // b. Past 10 MB, two.
                string big = fresh("big");
                made(big, new DateTime(2026, 11, 19), 6L * 1024 * 1024);
                made(big, new DateTime(2026, 11, 20), 5L * 1024 * 1024);
                made(big, new DateTime(2026, 11, 21), 100);
                Logs.Rotate(big, Logs.FileFor(big, now), now);
                if (left(big) != "2026-11-20,2026-11-21") throw new Exception("Her logs past 10 MB didn't keep two: " + left(big));
                // c. Twelve hours into today's log and still past 10 MB, the older one goes; an hour in, it stays.
                string late = fresh("late");
                made(late, new DateTime(2026, 11, 20), 11L * 1024 * 1024);
                made(late, new DateTime(2026, 11, 21), 100);
                Logs.Rotate(late, Logs.FileFor(late, now), now);
                string anHourIn = left(late);
                Logs.Began = delegate { return now.AddHours(-13); };
                Logs.Rotate(late, Logs.FileFor(late, now), now);
                if (anHourIn != "2026-11-20,2026-11-21" || left(late) != "2026-11-21") throw new Exception("The twelve hour rule went wrong: " + anHourIn + " then " + left(late));
                // d. The log in use is never deleted, and nothing goes while there's only one.
                string alone = fresh("alone");
                made(alone, new DateTime(2026, 11, 21), 12L * 1024 * 1024);
                Logs.Rotate(alone, Logs.FileFor(alone, now), now);
                string inUse = fresh("inuse");
                for (int day = 18; day <= 21; day++) made(inUse, new DateTime(2026, 11, day), 100);
                Logs.Rotate(inUse, Logs.FileFor(inUse, new DateTime(2026, 11, 18)), now);
                if (left(alone) != "2026-11-21" || left(inUse) != "2026-11-18,2026-11-20,2026-11-21") throw new Exception("A log in use or the only one was deleted: " + left(alone) + " / " + left(inUse));
                checks.Add("PASS her log rotates: three days kept, two past 10 MB, the older one gone twelve hours into today's log while still past 10 MB, never the log in use and nothing while there's only one");

                // Her chat transcripts: a size limit, never an age one; here 1000 bytes stands in for 10 MB.
                Action<string, string, int> chatFile = delegate(string folder, string name, int size) { File.WriteAllText(Path.Combine(folder, name), new string('a', size - 1) + "\n"); };
                Func<string, string> kept = delegate(string folder)
                {
                    List<string> names = new List<string>();
                    foreach (string file in Directory.GetFiles(folder)) names.Add(Path.GetFileName(file));
                    names.Sort(StringComparer.OrdinalIgnoreCase);
                    return string.Join(",", names.ToArray());
                };
                string under = fresh("chats-under");
                for (int day = 1; day <= 30; day++) chatFile(under, "2026-10-" + day.ToString("00") + ".md", 20);
                chatFile(under, "unfolded.txt", 300);
                Brain.CapTranscripts(under, now, 1000);
                bool nothingGone = Directory.GetFiles(under).Length == 31;
                string over = fresh("chats-over");
                foreach (string day in new[] { "2026-11-18", "2026-11-19", "2026-11-20", "2026-11-21" }) chatFile(over, day + ".md", 400);
                File.WriteAllText(Path.Combine(over, "unfolded.txt"), "2026-11-18 10:00 You: from the first day\n2026-11-21 14:00 You: today\n");
                Brain.CapTranscripts(over, now, 1000);
                string unfolded = File.ReadAllText(Path.Combine(over, "unfolded.txt"));
                string lone = fresh("chats-lone");
                chatFile(lone, "2026-11-21.md", 900);
                File.WriteAllText(Path.Combine(lone, "unfolded.txt"), new string('b', 99) + "\n" + new string('c', 99) + "\n" + "2026-11-21 14:00 You: newest\n");
                Brain.CapTranscripts(lone, now, 1000);
                string loneQueue = File.ReadAllText(Path.Combine(lone, "unfolded.txt"));
                string notToday = fresh("chats-old-today");
                foreach (string day in new[] { "2026-11-19", "2026-11-20" }) chatFile(notToday, day + ".md", 800);
                Brain.CapTranscripts(notToday, new DateTime(2026, 11, 19, 15, 0, 0), 1000);
                if (!nothingGone || kept(over) != "2026-11-20.md,2026-11-21.md,unfolded.txt" || unfolded.IndexOf("from the first day", StringComparison.Ordinal) < 0
                    || kept(lone) != "2026-11-21.md,unfolded.txt" || loneQueue != "2026-11-21 14:00 You: newest\n" || kept(notToday) != "2026-11-19.md")
                    throw new Exception("Her chat transcripts weren't held to their size: under the limit kept all " + nothingGone + ", over " + kept(over) + " (unfolded \"" + unfolded.Trim() + "\"), alone " + kept(lone) + " (\"" + loneQueue.Trim() + "\"), today's kept " + kept(notToday));
                checks.Add("PASS her chat transcripts are held to 10 MB with unfolded.txt counted, never by age: thirty small days under it all stay; past it the oldest day files go first until under, their unfolded lines kept for memories.md; today's file never goes, and with only it left the oldest unfolded lines go");

                // What goes goes to the Recycle Bin, never off the disk; with no bin, everything stays.
                int binned = Directory.Exists(bin) ? Directory.GetFiles(bin).Length : 0;
                string noBin = fresh("chats-no-bin");
                foreach (string day in new[] { "2026-11-18", "2026-11-19", "2026-11-20", "2026-11-21" }) chatFile(noBin, day + ".md", 400);
                File.WriteAllText(Path.Combine(noBin, "unfolded.txt"), "2026-11-18 10:00 You: kept\n");
                Func<string, bool> fakeBin = Logs.Binned;
                Logs.Binned = delegate { return false; };
                Brain.CapTranscripts(noBin, now, 1000);
                string logsNoBin = fresh("logs-no-bin");
                for (int day = 15; day <= 21; day++) made(logsNoBin, new DateTime(2026, 11, day), 100);
                Logs.Rotate(logsNoBin, Logs.FileFor(logsNoBin, now), now);
                Logs.Binned = fakeBin;
                bool fixedDrive = Logs.HasBin(Path.Combine(root, "x.txt")) || new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))).DriveType != DriveType.Fixed;
                if (binned < 2 || Directory.GetFiles(noBin).Length != 5 || Logs.Files(logsNoBin).Count != 7 || File.ReadAllText(Path.Combine(noBin, "unfolded.txt")).IndexOf("kept", StringComparison.Ordinal) < 0 || !fixedDrive)
                    throw new Exception("What goes didn't go to the Recycle Bin, or went anyway with no bin: binned " + binned + ", chats kept " + Directory.GetFiles(noBin).Length + " of 5, logs kept " + Logs.Files(logsNoBin).Count + " of 7, bin found on a fixed drive " + fixedDrive);
                checks.Add("PASS old chat days and old logs go to the Recycle Bin, not off the disk; on a drive with no bin they all stay, unfolded lines too, and her log says why (" + binned + " files binned in these checks, to a stand-in bin)");

                // errors.txt from before moves in as its day's file, ahead of anything that day already has.
                string data = fresh("data");
                string old = Path.Combine(data, "errors.txt");
                File.WriteAllText(old, "2026-11-15 09:00:00 | old | - | from before\r\n");
                File.SetLastWriteTime(old, new DateTime(2026, 11, 15, 9, 0, 0));
                string folder15 = Path.Combine(data, Personalities.SettingsName, Logs.FolderName);
                Directory.CreateDirectory(folder15);
                File.WriteAllText(Logs.FileFor(folder15, new DateTime(2026, 11, 15)), "2026-11-15 10:00:00 | new | - | already there\r\n");
                string today = Logs.Start(data, now, false);
                string moved = File.ReadAllText(Logs.FileFor(folder15, new DateTime(2026, 11, 15)));
                if (File.Exists(old) || today != Logs.FileFor(folder15, now) || moved.IndexOf("from before", StringComparison.Ordinal) < 0 || moved.IndexOf("from before", StringComparison.Ordinal) > moved.IndexOf("already there", StringComparison.Ordinal)
                    || Logs.DataOf(today) != data || Logs.DataOf(old) != data || !Logs.Owns(today) || Logs.Owns(old))
                    throw new Exception("errors.txt didn't move into personalSettings\\Logs as its day's file: " + moved);
                checks.Add("PASS errors.txt from before moves into personalSettings\\Logs as the file for the day it was last written, ahead of that day's lines, and her data folder is still found from her log's path");

                // A daily log keeps every line; any other log still keeps its last 200.
                Brain.LogPath = today;
                for (int i = 0; i < 250; i++) Brain.Log("check", -1, "line " + i);
                string side = Path.Combine(data, "side-log.txt");
                for (int i = 0; i < 250; i++) Brain.LogAt(side, "check", -1, "line " + i);
                int dayLines = File.ReadAllLines(today).Length, sideLines = File.ReadAllLines(side).Length;
                if (dayLines != 250 || sideLines != 200) throw new Exception("Her daily log didn't keep every line, or another log lost its 200 line limit: " + dayLines + " / " + sideLines);
                checks.Add("PASS her daily log keeps every line of the day, written a line at a time, and her other logs still keep their last 200");

                // Save log: every log, oldest first, her settings' line on top, keys and their user folder's name taken out.
                string secret = "sk-proj-" + new string('Q', 40);
                File.WriteAllText(Logs.FileFor(folder15, new DateTime(2026, 11, 20)), "2026-11-20 08:00:00 | https://api.openai.com/v1/x | - | Bad key " + secret + " in C:\\Users\\" + Environment.UserName + "\\Documents\r\n");
                string saved = Path.Combine(root, "saved-log.txt");
                PickLogFile = delegate { return saved; };
                string wrote = SaveLog();
                string text = wrote == null || !File.Exists(saved) ? "" : File.ReadAllText(saved);
                int first = text.IndexOf("== capri-2026-11-15.log", StringComparison.Ordinal), second = text.IndexOf("== capri-2026-11-20.log", StringComparison.Ordinal), third = text.IndexOf("== capri-2026-11-21.log", StringComparison.Ordinal);
                if (wrote != saved || !text.StartsWith("Capri ", StringComparison.Ordinal) || first < 0 || second < first || third < second || text.IndexOf("line 249", StringComparison.Ordinal) < 0
                    || text.Contains(secret) || (Environment.UserName.Length >= 3 && text.IndexOf("Users\\" + Environment.UserName, StringComparison.OrdinalIgnoreCase) >= 0))
                    throw new Exception("Save log didn't write every log newest last with keys and their folder name taken out: " + (text.Length > 400 ? text.Substring(0, 400) : text));
                PickLogFile = delegate { return null; };
                if (SaveLog() != null) throw new Exception("Save log wrote something after the picker was cancelled.");
                checks.Add("PASS Help > Save log writes every log she has, newest last, under her settings' line, with saved keys and their user folder's name taken out, and nothing when the picker is cancelled");
            }
            finally
            {
                Logs.Folder = folderWas;
                Brain.LogPath = logWas;
                Logs.Began = beganWas;
            Logs.Binned = binWas;
                PickLogFile = pickWas;
                try { Directory.Delete(root, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
#endif
