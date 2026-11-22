using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace CapriDesktop
{
    // Her log: one file a day in personalSettings\Logs. Three days are kept, two past 10 MB in all, and the oldest
    // other one goes if still over after twelve hours; the one in use never. Old logs go to the Recycle Bin, never off
    // the disk.
    internal static class Logs
    {
        internal const string FolderName = "Logs";
        internal const int Keep = 3, KeepWhenBig = 2;
        internal const long TooBig = 10L * 1024 * 1024;
        internal static readonly TimeSpan BigAfter = TimeSpan.FromHours(12);
        internal static string Folder;
        private static Timer hourly;
        private static readonly object gate = new object();

        internal static string FileFor(string folder, DateTime day)
        {
            return Path.Combine(folder, "capri-" + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log");
        }

        // Her data folder from a log's path: two up from personalSettings\Logs, or the folder an older errors.txt sits in.
        internal static string DataOf(string logPath)
        {
            if (string.IsNullOrEmpty(logPath)) return "";
            string folder = Path.GetDirectoryName(logPath) ?? "";
            if (string.Equals(Path.GetFileName(folder), FolderName, StringComparison.OrdinalIgnoreCase))
            {
                string settings = Path.GetDirectoryName(folder) ?? "";
                if (string.Equals(Path.GetFileName(settings), Personalities.SettingsName, StringComparison.OrdinalIgnoreCase)) return Path.GetDirectoryName(settings) ?? "";
            }
            return folder;
        }

        // Whether a path is one of her daily logs, which grow by appending rather than keeping their last 200 lines.
        internal static bool Owns(string path)
        {
            string folder = Folder;
            return !string.IsNullOrEmpty(folder) && !string.IsNullOrEmpty(path) && string.Equals(Path.GetDirectoryName(path), folder, StringComparison.OrdinalIgnoreCase);
        }

        // Her log for today, after moving errors.txt in and rotating. watch keeps checking every hour, for a real run.
        internal static string Start(string data, DateTime now, bool watch)
        {
            string folder = Path.Combine(data, Personalities.SettingsName, FolderName);
            try { Directory.CreateDirectory(folder); }
            catch (IOException) { return Path.Combine(data, "errors.txt"); }
            catch (UnauthorizedAccessException) { return Path.Combine(data, "errors.txt"); }
            Folder = folder;
            MoveIn(Path.Combine(data, "errors.txt"), folder);
            string today = FileFor(folder, now);
            Rotate(folder, today, now);
            if (watch && hourly == null) hourly = new Timer(delegate { Hour(); }, null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
            return today;
        }

        // The hourly check: a new day writes to a new file, then the rules run.
        private static void Hour()
        {
            try
            {
                string folder = Folder;
                if (string.IsNullOrEmpty(folder) || !Owns(Brain.LogPath)) return;
                DateTime now = DateTime.Now;
                string today = FileFor(folder, now);
                Brain.LogPath = today;
                Rotate(folder, today, now);
            }
            catch (Exception caughtOne) { Brain.Caught(caughtOne); }
        }

        // The errors.txt from before goes in as the file for the day it was last written, ahead of anything already there.
        internal static void MoveIn(string old, string folder)
        {
            try
            {
                if (!File.Exists(old)) return;
                string target = FileFor(folder, File.GetLastWriteTime(old));
                lock (gate)
                {
                    if (!File.Exists(target)) { File.Move(old, target); return; }
                    File.WriteAllText(target, File.ReadAllText(old) + File.ReadAllText(target));
                    File.Delete(old);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        // Her daily logs, oldest first.
        internal static List<string> Files(string folder)
        {
            List<string> files = new List<string>();
            try
            {
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                    foreach (string file in Directory.GetFiles(folder, "capri-*.log")) if (Day(file) != null) files.Add(file);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            files.Sort(StringComparer.OrdinalIgnoreCase);
            return files;
        }

        internal static DateTime? Day(string file)
        {
            string name = Path.GetFileNameWithoutExtension(file ?? "");
            DateTime day;
            if (name.Length != 16 || !name.StartsWith("capri-", StringComparison.Ordinal)) return null;
            return DateTime.TryParseExact(name.Substring(6), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day) ? day : (DateTime?)null;
        }

        private static long Size(string file)
        {
            try { return new FileInfo(file).Length; }
            catch (IOException) { return 0; }
            catch (UnauthorizedAccessException) { return 0; }
        }

        // When a log began: when the file was made, or the start of its day when that can't be read.
        internal static Func<string, DateTime> Began = delegate(string file)
        {
            try { if (File.Exists(file)) return File.GetCreationTime(file); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return Day(file) ?? DateTime.Now;
        };

        // The rules, a to d above. Returns the logs it deleted.
        internal static List<string> Rotate(string folder, string inUse, DateTime now)
        {
            List<string> gone = new List<string>();
            lock (gate)
            {
                List<string> files = Files(folder);
                if (files.Count <= 1) return gone;
                long total = 0;
                foreach (string file in files) total += Size(file);
                int keep = total > TooBig ? KeepWhenBig : Keep;
                while (files.Count > keep)
                {
                    string oldest = Oldest(files, inUse);
                    if (oldest == null || !Delete(oldest)) break;
                    total -= Size(oldest);
                    files.Remove(oldest);
                    gone.Add(oldest);
                }
                if (total > TooBig && files.Count > 1 && now - Began(inUse) >= BigAfter)
                {
                    string oldest = Oldest(files, inUse);
                    if (oldest != null && Delete(oldest)) gone.Add(oldest);
                }
            }
            return gone;
        }

        private static string Oldest(List<string> files, string inUse)
        {
            foreach (string file in files) if (!string.Equals(file, inUse, StringComparison.OrdinalIgnoreCase)) return file;
            return null;
        }

        private static bool Delete(string file) { return Binned(file); }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct FileOperation
        {
            internal IntPtr Window;
            internal uint Func;
            internal string From, To;
            internal ushort Flags;
            internal bool Aborted;
            internal IntPtr Names;
            internal string Title;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHFileOperation(ref FileOperation operation);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHQueryRecycleBin(string root, IntPtr info);
        private const uint DeleteFunc = 3;
        private const ushort AllowUndo = 0x40, NoConfirmation = 0x10, Silent = 0x4, NoErrorUi = 0x400;

        // Whether the drive a file is on has a Recycle Bin she can put it in: a fixed drive whose bin answers.
        internal static bool HasBin(string file)
        {
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(file));
                if (string.IsNullOrEmpty(root) || new DriveInfo(root).DriveType != DriveType.Fixed) return false;
                // Its answer: a size, then two 64-bit counts, laid out 8 apart on 64-bit Windows and packed on 32-bit.
                IntPtr info = Marshal.AllocHGlobal(24);
                try
                {
                    for (int at = 0; at < 24; at += 4) Marshal.WriteInt32(info, at, 0);
                    Marshal.WriteInt32(info, IntPtr.Size == 8 ? 24 : 20);
                    return SHQueryRecycleBin(root, info) == 0;
                }
                finally { Marshal.FreeHGlobal(info); }
            }
            catch (ArgumentException) { return false; }
            catch (IOException) { return false; }
        }

        // A file she is done with, put in the Recycle Bin. Where there is no bin it is kept and her log says so. The
        // check puts in its own, so a check never fills their bin.
        internal static Func<string, bool> Binned = delegate(string file)
        {
            try
            {
                if (!File.Exists(file)) return true;
                if (!HasBin(file)) { Brain.Log("files", -1, "No Recycle Bin for " + Path.GetFileName(file) + ", so it was kept."); return false; }
                FileOperation operation = new FileOperation { Func = DeleteFunc, From = Path.GetFullPath(file) + "\0\0", Flags = (ushort)(AllowUndo | NoConfirmation | Silent | NoErrorUi) };
                return SHFileOperation(ref operation) == 0 && !operation.Aborted && !File.Exists(file);
            }
            catch (Exception caughtOne) { Brain.Caught(caughtOne); return false; }
        };

        // Every log she has, newest last, under a line saying which day, with saved keys and their user folder's name taken
        // out as in the log itself: what Help > Save log writes.
        internal static string Joined(string head, string folder, string current)
        {
            List<string> files = Files(folder);
            if (!string.IsNullOrEmpty(current) && File.Exists(current) && !files.Exists(file => string.Equals(file, current, StringComparison.OrdinalIgnoreCase))) files.Add(current);
            StringBuilder all = new StringBuilder(head ?? "").AppendLine();
            if (files.Count == 0) all.AppendLine().AppendLine("No errors logged.");
            foreach (string file in files)
            {
                string[] lines;
                try { lines = File.ReadAllLines(file); }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                all.AppendLine().AppendLine("== " + Path.GetFileName(file));
                foreach (string line in Brain.Cleaned(lines)) all.AppendLine(Brain.Nameless(line));
            }
            return all.ToString();
        }
    }
}
