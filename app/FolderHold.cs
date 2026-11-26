using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace CapriDesktop
{
    // Where her data folder really is, noted at start. A folder reached through a link can be pointed somewhere else
    // while she runs; her settings are then not written there, so another folder's never take the place of hers.
    internal static class FolderHold
    {
        private static string held, heldAs;

        internal static void Note(string folder)
        {
            heldAs = Full(folder);
            held = Real(folder);
        }

        // True when the folder still leads where it did at start, or when where it leads can't be read.
        internal static bool Same(string folder)
        {
            if (held == null || !string.Equals(Full(folder), heldAs, StringComparison.OrdinalIgnoreCase)) return true;
            string now = Real(folder);
            return now == null || string.Equals(now, held, StringComparison.OrdinalIgnoreCase);
        }

        private static string Full(string folder)
        {
            try { return Path.GetFullPath(folder ?? "").TrimEnd('\\'); } catch (Exception) { return folder ?? ""; }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint size, uint flags);

        // The folder's final path with every link followed, or null when it can't be opened.
        internal static string Real(string folder)
        {
            try
            {
                using (SafeFileHandle handle = CreateFile(folder, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero))
                {
                    if (handle.IsInvalid) return null;
                    StringBuilder path = new StringBuilder(1024);
                    uint got = GetFinalPathNameByHandle(handle, path, (uint)path.Capacity, 0);
                    if (got == 0 || got >= path.Capacity) return null;
                    string found = path.ToString();
                    if (found.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)) found = @"\\" + found.Substring(8);
                    else if (found.StartsWith(@"\\?\", StringComparison.Ordinal)) found = found.Substring(4);
                    return found.TrimEnd('\\');
                }
            }
            catch (Exception) { return null; }
        }

        // A link to one folder, noted, then pointed at another: she sees it moved; the plain folder stays the same.
#if CHECKS
        internal static void Check(List<string> checks, string folder)
        {
            string root = Path.Combine(folder, "hold-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            string first = Path.Combine(root, "first"), second = Path.Combine(root, "second"), link = Path.Combine(root, "link");
            string heldWas = held, heldAsWas = heldAs;
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(second);
            try
            {
                Note(first);
                if (!Same(first)) throw new Exception("A plain folder read as moved.");
                if (!Link(link, first)) { checks.Add("LIMIT her folder held by its real path: a link couldn't be made here"); return; }
                Note(link);
                if (!Same(link)) throw new Exception("Her folder read as moved while its link was unchanged.");
                Directory.Delete(link);
                if (!Link(link, second)) throw new Exception("The link couldn't be pointed at the second folder.");
                if (Same(link)) throw new Exception("Her folder's link pointed elsewhere and she didn't see it.");
                checks.Add("PASS her folder held by its real path: a link to it pointed somewhere else while she ran is seen, so her settings aren't written there");
            }
            finally
            {
                held = heldWas;
                heldAs = heldAsWas;
                try { if (Directory.Exists(link)) Directory.Delete(link); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                try { Directory.Delete(root, true); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
        }
#endif

        private static bool Link(string link, string target)
        {
            ProcessStartInfo start = new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + target + "\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            using (Process made = Process.Start(start))
            {
                made.StandardOutput.ReadToEnd();
                made.WaitForExit(10000);
                return made.ExitCode == 0 && Directory.Exists(link);
            }
        }
    }
}
