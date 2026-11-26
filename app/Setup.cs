using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;
using CapriDesktop;
using Microsoft.Win32;

namespace CapriSetup
{
    internal static class Program
    {
        internal static string InstallPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Capri"); } }
        internal const string RegistryPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Capri.Desktop";
        internal static readonly string[] Packs = { "brain", "voice", "ears", "wake" };

        internal static bool Wanted(string file, string[] args)
        {
            if (Array.IndexOf(args, "--silent") >= 0 || Array.IndexOf(args, "--package") >= 0) return true;
            if (args.Length > 0 && Array.IndexOf(new[] { "--close", "--update", "--verify", "--verify-voice", "--remove-files", "--uninstall", "--apply-update" }, args[0]) >= 0) return true;
            string name = Path.GetFileNameWithoutExtension(file);
            return name.IndexOf("setup", StringComparison.OrdinalIgnoreCase) >= 0 || string.Equals(name, "Uninstall", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Updater", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "Update", StringComparison.OrdinalIgnoreCase);
        }

        // Updater.exe is this same executable renamed, carrying the new build, so nothing is downloaded. It writes the
        // three programs and nothing else, unlike unzipping a release, which overwrites keys and notes.
        internal static bool Updating(string file, string[] args)
        {
            if (args.Length > 0 && args[0] == "--apply-update") return true;
            // No flag turns an updater into an uninstaller. RemoveFiles deletes her brain, voice, ears and wake folders,
            // which are gigabytes and are not downloaded again, so "Updater.exe --silent --uninstall" must not reach it.
            // An exe under this name only ever updates; package managers uninstall through Capri-Setup.exe as before.
            string name = Path.GetFileNameWithoutExtension(file);
            return string.Equals(name, "Updater", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "Update", StringComparison.OrdinalIgnoreCase);
        }

        // How many spares of one program are kept. Four programs at about 30 MB each grew by 120 MB an update and
        // nothing ever took one away, so a PC updated often carried gigabytes of builds nobody asked to keep.
        internal const int MostSpares = 3;

        // The programs an update replaces are kept beside the new ones, so a bad build can be put back by hand. Spares
        // are numbered upward, the newest highest, and the oldest go past MostSpares.
        private static string Spare(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string spare = CapriDesktop.SpareFiles.Name(path);
                File.Copy(path, spare);
                CapriDesktop.SpareFiles.Prune(path, MostSpares);
                return spare;
            }
            // A spare that cannot be written must not stop the update: they came here to get the new build.
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        internal static string SpareForTest(string path) { return Spare(path); }

        internal static string[] SparesForTest(string path) { return Spares(path).ToArray(); }

        // Every spare of one program, timed or from before spares had a time.
        private static List<string> Spares(string path) { return CapriDesktop.SpareFiles.Of(path); }

        // The oldest spares removed until MostSpares are left. Ordered by when each was written rather than by the
        // number in its name, so one put back or copied in by hand is not taken for the oldest.

        private static readonly string[] Programs = { "Capri.exe", "Uninstall.exe", "Capri-Setup.exe", "Updater.exe" };

        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow found, IntPtr nothing);
        [DllImport("user32.dll")] private static extern int GetWindowThreadProcessId(IntPtr window, out int process);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wide, IntPtr low);
        private delegate bool EnumWindow(IntPtr window, IntPtr nothing);

        private const uint Close = 0x0010;

        // WM_CLOSE to every top level window the process owns, as her tray Quit does: her layered tool window has no
        // main window, so Process.CloseMainWindow finds nothing.
        private static void CloseWindows(int id)
        {
            EnumWindows(delegate(IntPtr window, IntPtr nothing)
            {
                int owner;
                GetWindowThreadProcessId(window, out owner);
                if (owner == id) PostMessage(window, Close, IntPtr.Zero, IntPtr.Zero);
                return true;
            }, IntPtr.Zero);
        }

        private static int ApplyUpdate(bool quiet)
        {
            string destination = InstallPath;
            string app = Path.Combine(destination, "Capri.exe");
            string version = Companion.Built;
            try
            {
                if (!File.Exists(app))
                {
                    string missing = "Capri isn't installed for this Windows user yet, so there's nothing to update. Run Capri-Setup.exe first.";
                    if (quiet) { Console.Error.WriteLine(missing); return 1; }
                    using (Notice tell = new Notice(missing, "Capri", true)) tell.ShowDialog();
                    return 1;
                }
                // Whether she was up decides whether she is started again, so an update from the tray feels like a restart
                // and one run on its own does not open her behind their back.
                bool wasUp = false;
                foreach (Process process in Process.GetProcessesByName("Capri"))
                    using (process)
                    {
                        bool held;
                        if (Same(SetupFiles.FileOf(process, out held), app)) wasUp = true;
                    }
                // Everything of hers is asked to close, as the tray's Quit asks, and ended by its PID after the wait.
                string stuck = null;
                if (CloseEverything(destination)) stuck = AdminCopy + Ended();
                else lock (NotEnded) if (NotEnded.Count > 0) stuck = "Capri wouldn't close, so the update was stopped rather than risk your settings." + Ended();
                if (stuck != null)
                {
                    if (quiet) { Console.Error.WriteLine(stuck); return 1; }
                    using (Notice tell = new Notice(stuck, "Capri", true)) tell.ShowDialog();
                    return 1;
                }
                // A clean install: her folder moved aside whole, the programs laid fresh, and everything of hers moved back.
                // The folder aside is kept until she starts cleanly once.
                List<string> notBack = new List<string>();
                string backup;
                string failed = CleanLay(destination, OldVersion(destination), delegate { Install(false, quiet); }, out backup, notBack);
                if (failed != null) throw new IOException(failed);
                // The other two tools are refreshed too, so the next update is applied by this build's updater, not an old one.
                foreach (string tool in new[] { "Capri-Setup.exe", "Updater.exe" })
                {
                    try { File.Copy(Application.ExecutablePath, Path.Combine(destination, tool), true); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                if (wasUp)
                    using (Process.Start(new ProcessStartInfo(app) { UseShellExecute = true, WorkingDirectory = destination })) { }
                if (quiet) return 0;
                using (Notice done = new Notice("Capri is now " + version + ".\n\nYour settings, keys, notes, personalities and model packs were all carried over." + (notBack.Count > 0 ? "\n\nThese stayed in " + backup + ":\n" + string.Join("\n", notBack.ToArray()) : "")
                    + "\n\nThe version from before is kept beside her folder until she has started cleanly once."
                    + (wasUp ? "\n\nShe's starting again now." : ""), "Capri updated", false)) done.ShowDialog();
                return 0;
            }
            catch (Exception error)
            {
                if (quiet) { Console.Error.WriteLine(error.Message); return 1; }
                using (Notice failed = new Notice("Capri couldn't be updated.\n\n" + error.Message, "Capri", true)) failed.ShowDialog();
                return 1;
            }
        }

        // The folder "--close" closes her in, or null for any other command. With no folder it is her usual install, the
        // one an update writes; checked before the update, so an updater given only "--close" never updates.
        internal static string Closing(string[] args)
        {
            if (args == null || args.Length == 0 || args[0] != "--close") return null;
            return args.Length >= 2 ? args[1] : InstallPath;
        }

        internal static int Run(string[] args)
        {
            string closing = Closing(args);
            if (closing != null) return CloseFolder(closing);
            if (Updating(Application.ExecutablePath, args)) return ApplyUpdate(Array.IndexOf(args, "--silent") >= 0);
            // A silent command she can't read exits 2, and one that fails exits 1, each with the reason on stderr.
            int mode;
            string package;
            try { mode = SetupFiles.Mode(args, out package); }
            catch (ArgumentException error) { Console.Error.WriteLine(error.Message); return 2; }
            try
            {
                if (mode == 2) return RemoveFiles(true, HerFolder());
                if (mode == 1)
                {
                    string unsaved = Install(false, true);
                    // She's installed by now, so the marker says who carries her even when her shortcut couldn't be saved.
                    SetupFiles.Mark(InstallPath, package ?? "manual");
                    if (unsaved != null) throw new IOException("Capri is installed, but her shortcut could not be saved: " + unsaved);
                    return 0;
                }
            }
            catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 0 && args[0] == "--update") return Update(args);
            if (args.Length == 2 && args[0] == "--verify") return Verify(args[1]);
            if (args.Length == 2 && args[0] == "--verify-voice") return VerifyVoice(args[1]);
            if (args.Length == 1 && args[0] == "--remove-files") return RemoveFiles(false, InstallPath);
            if (args.Length >= 2 && args[0] == "--remove-files") return RemoveFiles(false, args[1], args.Length >= 3 ? args[2] : "");
#if CHECKS
            if (args.Length == 3 && args[0] == "--fit-check") return FitCheck(args[1], args[2]);
#endif
            using (SetupWindow window = new SetupWindow(args.Length == 1 && args[0] == "--uninstall")) Application.Run(window);
            return 0;
        }

        [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr context);

#if CHECKS
        // A real start of the install window against a stand-in screen ("1280x720@150"): its work area is that size less a
        // taskbar at that scale, set at the top left of this screen's. The window opens, is checked and pictured, and
        // closes without installing anything. Exit 0 when it fits.
        private static int FitCheck(string screen, string output)
        {
            string[] parts = screen.Split('x', '@');
            int wide, tall, percent;
            if (parts.Length != 3 || !int.TryParse(parts[0], out wide) || !int.TryParse(parts[1], out tall) || !int.TryParse(parts[2], out percent)) return 2;
            float scale = percent / 100f;
            Rectangle real = Screen.PrimaryScreen.WorkingArea;
            Fits.StandIn = new Rectangle(real.Left, real.Top, wide, tall - (int)Math.Round(40 * scale));
            Fits.StandInScale = scale;
            bool fits = false;
            string said = "";
            using (SetupWindow window = new SetupWindow(false))
            {
                window.Shown += delegate
                {
                    Application.DoEvents();
                    fits = Fits.Inside(window.Bounds, Fits.StandIn);
                    said = screen + ": " + (fits ? "fits" : "DOES NOT FIT") + ", window " + window.Bounds + " in " + Fits.StandIn;
                    try
                    {
                        Directory.CreateDirectory(output);
                        using (Bitmap picture = new Bitmap(window.Width, window.Height))
                        {
                            window.DrawToBitmap(picture, new Rectangle(Point.Empty, picture.Size));
                            picture.Save(Path.Combine(output, "setup-" + wide + "x" + tall + "-" + percent + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                        }
                        File.AppendAllText(Path.Combine(output, "fit-check.txt"), said + Environment.NewLine);
                    }
                    catch (Exception caughtOne) { said += " (" + caughtOne.Message + ")"; }
                    window.Close();
                };
                Application.Run(window);
            }
            Console.WriteLine(said);
            return fits ? 0 : 1;
        }
#endif

        private static int Update(string[] args)
        {
            try
            {
                int parent;
                long started;
                if (args.Length != 3 || !int.TryParse(args[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out parent) || parent <= 0 || !long.TryParse(args[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out started) || started <= 0) throw new IOException("The update command is invalid.");
                Process process = null;
                try { process = Process.GetProcessById(parent); } catch (ArgumentException) { }
                if (process != null)
                    using (process)
                    {
                        bool same = false;
                        try { same = process.StartTime.ToUniversalTime().Ticks == started; } catch (InvalidOperationException) { }
                        if (same && !process.WaitForExit(60000)) throw new IOException("Capri is still closing. Try the update again.");
                    }
                Install(false);
                using (Process launched = Process.Start(new ProcessStartInfo(Path.Combine(InstallPath, "Capri.exe")) { UseShellExecute = true, WorkingDirectory = InstallPath }))
                    if (launched == null) throw new IOException("Capri updated but couldn't restart. Open her from the Start menu.");
                return 0;
            }
            catch (Exception error) { Notice.Tell(null, error.Message, "Capri couldn't update", true); return 1; }
        }

        internal static byte[] Payload() { return File.ReadAllBytes(Application.ExecutablePath); }

        private static int Verify(string output)
        {
            try
            {
                output = Path.GetFullPath(output);
                Directory.CreateDirectory(output);
                byte[] bytes = Payload();
                if (bytes.Length < 1024 || bytes[0] != 77 || bytes[1] != 90) throw new InvalidDataException("Application payload is not a Windows executable.");
                File.WriteAllBytes(Path.Combine(output, "Capri.exe"), bytes);
                using (SHA256 sha = SHA256.Create()) File.WriteAllText(Path.Combine(output, "payload.sha256"), BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant());
                if (!Ours(Path.Combine(output, "Capri.exe"))) throw new InvalidDataException("The installer doesn't recognise its own Capri.exe.");
                if (Ours(Path.Combine(Environment.SystemDirectory, "notepad.exe")) || Ours(Path.Combine(output, "missing.exe"))) throw new InvalidDataException("The installer takes other programs for Capri.");
                // Starting fresh, on a folder built for it: her packs must still be there afterwards and every
                // file of theirs must be in the dated folder, because this is the one path that moves their
                // files and the cost of it being wrong is somebody's memories.
                string fresh = Path.Combine(output, "fresh");
                Directory.CreateDirectory(Path.Combine(fresh, "personalSettings"));
                Directory.CreateDirectory(Path.Combine(fresh, "ears"));
                Directory.CreateDirectory(Path.Combine(fresh, "voice"));
                File.WriteAllText(Path.Combine(fresh, "settings.json"), "{}");
                File.WriteAllText(Path.Combine(fresh, "settings.json.bak"), "{}");
                File.WriteAllText(Path.Combine(fresh, "chat.json"), "[]");
                File.WriteAllText(Path.Combine(fresh, "personalSettings", "me.md"), "# me");
                File.WriteAllText(Path.Combine(fresh, "ears", "model.bin"), "pack");
                File.WriteAllText(Path.Combine(fresh, "Capri.package"), "package");
                File.WriteAllText(Path.Combine(fresh, "lore.md"), "# pack");
                if (!Started(fresh)) throw new InvalidDataException("A folder with their files in it did not read as started.");
                int keptBack;
                string put = Aside(fresh, out keptBack);
                if (put == null || keptBack != 0) throw new InvalidDataException("Starting fresh moved nothing, or could not move " + keptBack + " of them.");
                foreach (string gone in new[] { "settings.json", "settings.json.bak", "chat.json", "personalSettings" })
                    if (File.Exists(Path.Combine(fresh, gone)) || Directory.Exists(Path.Combine(fresh, gone)))
                        throw new InvalidDataException("Starting fresh left " + gone + " where it was.");
                foreach (string there in new[] { "settings.json", "settings.json.bak", "chat.json" })
                    if (!File.Exists(Path.Combine(put, there))) throw new InvalidDataException("Starting fresh lost " + there + " instead of keeping a copy.");
                if (!File.Exists(Path.Combine(put, "personalSettings", "me.md"))) throw new InvalidDataException("Starting fresh lost their me.md.");
                foreach (string stays in new[] { "ears", "voice" })
                    if (!Directory.Exists(Path.Combine(fresh, stays))) throw new InvalidDataException("Starting fresh carried off her " + stays + " pack.");
                foreach (string stays in new[] { "Capri.package", "lore.md" })
                    if (!File.Exists(Path.Combine(fresh, stays))) throw new InvalidDataException("Starting fresh carried off " + stays + ".");
                if (Started(fresh)) throw new InvalidDataException("A folder that has just been cleared still reads as started.");
                if (Aside(fresh, out keptBack) != null) throw new InvalidDataException("Starting fresh twice moved something the second time.");
                if (!Started(Path.Combine(output, "nowhere")) == false) throw new InvalidDataException("A folder that is not there read as started.");

                string home = Path.Combine(output, "Capri");
                if (!Inside(Path.Combine(home, "Capri.exe"), home) || !Inside(Path.Combine(home, "brain", "llama-server.exe"), home + "\\") || Inside(Path.Combine(output, "Capri2", "Capri.exe"), home) || Inside(Path.Combine(Environment.SystemDirectory, "notepad.exe"), home) || Inside(home, home)) throw new InvalidDataException("The uninstaller would close the wrong programs.");
                string link = Path.Combine(output, "Capri.lnk");
                File.WriteAllText(link, "an old shortcut");
                File.SetAttributes(link, FileAttributes.ReadOnly);
                string unsaved = Shortcut(link, Path.Combine(output, "Capri.exe"));
                if (unsaved != null || !string.Equals(LinkTarget(link), Path.Combine(output, "Capri.exe"), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The installer can't save her shortcut. " + unsaved);
                File.Delete(link);
                if (Shortcut(Path.Combine(output, "missing", "Capri.lnk"), Path.Combine(output, "Capri.exe")) == null) throw new InvalidDataException("A shortcut Windows can't save counts as saved.");
                using (SetupWindow preview = new SetupWindow(false))
                {
                    preview.Show();
                    Application.DoEvents();
                    Snap(preview, Path.Combine(output, "installer.png"));
                    preview.Speaking(127L << 20, VoicePack.Size, Megabytes(127L << 20) + " of " + Megabytes(VoicePack.Size));
                    Application.DoEvents();
                    if (!preview.Saying("FREE VOICE", "Installing her free voice", "This may take some time. Other voices are under VOICE in Capri Mainframe.")) throw new InvalidDataException("The installer doesn't name her free voice or say where to pick other voices.");
                    Snap(preview, Path.Combine(output, "installer-speech.png"));
                    preview.Close();
                }
                using (SetupWindow farewell = new SetupWindow(true))
                {
                    farewell.Show();
                    Application.DoEvents();
                    Snap(farewell, Path.Combine(output, "installer-remove.png"));
                    farewell.Close();
                }
                // Her notices, which stand in for Windows' grey boxes, fit their message under their title with OK below it.
                using (Notice told = new Notice("Capri has been removed.\n\nYour position and size settings were kept in " + InstallPath + ".", "Capri", false))
                using (Notice failed = new Notice("There's a Capri.exe in " + InstallPath + " that isn't this Capri. Move it out of that folder, then install again.", "Capri couldn't install", true))
                {
                    told.Show();
                    failed.Show();
                    Application.DoEvents();
                    if (!told.Fits || !failed.Fits || !told.Said.StartsWith("CAPRI\nCapri has been removed.", StringComparison.Ordinal) || !failed.Said.StartsWith("CAPRI COULDN'T INSTALL\n", StringComparison.Ordinal)) throw new InvalidDataException("A notice doesn't fit or show its message: " + told.Said + " / " + failed.Said);
                    Snap(told, Path.Combine(output, "notice.png"));
                    Snap(failed, Path.Combine(output, "notice-error.png"));
                    told.Close();
                    failed.Close();
                }
                if (Array.IndexOf(Packs, "brain") < 0 || Array.IndexOf(Packs, "voice") < 0 || Array.IndexOf(Packs, "ears") < 0 || Array.IndexOf(Packs, "wake") < 0) throw new InvalidDataException("The uninstaller would leave one of her packs behind.");
                File.WriteAllText(Path.Combine(output, "packaging-test.txt"), "PASS the installer wrote itself as Capri.exe\nPASS PE signature\nPASS SHA-256 computed\nPASS an unmarked Capri.exe is recognised as Capri, other programs aren't\nPASS installer and uninstaller windows rendered\nPASS her notices show their title, message and OK in her look, sized to fit the message\nPASS the installer says it's installing the local speech model, that it may take some time, and that an API key gives a better, faster voice\nPASS the uninstaller closes Capri and anything running from her folder, like her brain, and nothing else\nPASS her shortcut is saved through Windows' own shell link, even over an old read-only one, and points at Capri.exe\nPASS a shortcut Windows won't save comes back as its reason instead of stopping the install\nPASS the uninstaller removes her brain, voice, ears and wake packs\nLIMIT installation and uninstallation were not performed\n");
                return 0;
            }
            catch (Exception error) { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString()); return 1; }
        }

        // Puts the new file in place of one that may be locked: Windows always allows a rename, so the old one is moved
        // aside and tidied on the next run, where File.Replace would fail under an antivirus.
        private static void Swap(string made, string target)
        {
            try { File.Replace(made, target, null); return; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            string aside = target + ".old";
            for (int next = 2; File.Exists(aside); next++) aside = target + ".old" + next.ToString(System.Globalization.CultureInfo.InvariantCulture);
            File.Move(target, aside);
            File.Move(made, target);
            try { File.Delete(aside); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        // A shortcut Windows won't save doesn't stop the install; its reason comes back, null when all saved.
        // ---------- A clean update ----------
        internal const string UpdateBackup = "Capri.update-";

        // Laid fresh by an update, so never carried back: her programs, their spares, the install marker, a half-written copy.
        internal static bool LaidFresh(string name)
        {
            string lower = name.ToLowerInvariant();
            if (lower == ".capri-install" || lower.EndsWith(".new", StringComparison.Ordinal) || lower.Contains(".exe.old")) return true;
            // Any program file loose in her folder is an old build's; her mods keep theirs in mods\.
            if (lower.EndsWith(".exe", StringComparison.Ordinal) || lower.EndsWith(".dll", StringComparison.Ordinal) || lower.EndsWith(".pdb", StringComparison.Ordinal)) return true;
            foreach (string program in Programs)
            {
                string one = program.ToLowerInvariant();
                if (lower == one || lower.StartsWith(one + ".", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        internal static string OldVersion(string home)
        {
            try
            {
                string mark = File.ReadAllText(Path.Combine(home, ".capri-install")).Trim();
                string version = mark.Substring(mark.LastIndexOf(' ') + 1);
                return System.Text.RegularExpressions.Regex.IsMatch(version, @"^[\w.\-]+$") ? version : "before";
            }
            catch (Exception) { return "before"; }
        }

        // Beside her real folder, on the same drive, so moving into it copies nothing.
        internal static string BackupFor(string home, string version)
        {
            string real = Final(home).TrimEnd('\\');
            string name = UpdateBackup + version, at = Path.Combine(Path.GetDirectoryName(real), name);
            for (int next = 2; Directory.Exists(at) || File.Exists(at); next++) at = Path.Combine(Path.GetDirectoryName(real), name + "-" + next);
            return at;
        }

        // Her folder moved aside, the fresh programs laid by lay, then everything but what lay made moved back. Null when
        // done; else why, with everything put back where it was. What couldn't come back is named in left.
        internal static string CleanLay(string home, string oldVersion, Action lay, out string backup, List<string> left)
        {
            backup = BackupFor(home, oldVersion);
            List<string> moved = new List<string>();
            try
            {
                Directory.CreateDirectory(backup);
                foreach (string entry in Directory.GetFileSystemEntries(home))
                {
                    // The program running this update stays where it is; the fresh one is written over it or beside it.
                    if (Same(entry, Application.ExecutablePath)) continue;
                    string into = Path.Combine(backup, Path.GetFileName(entry));
                    if (Directory.Exists(entry)) Directory.Move(entry, into); else File.Move(entry, into);
                    moved.Add(Path.GetFileName(entry));
                }
            }
            catch (Exception error)
            {
                foreach (string name in moved) MoveBack(Path.Combine(backup, name), Path.Combine(home, name), null);
                try { Directory.Delete(backup); } catch (Exception) { }
                return "Her folder couldn't be moved aside, so nothing was changed: " + error.Message;
            }
            try { lay(); }
            catch (Exception error)
            {
                foreach (string entry in Directory.GetFileSystemEntries(home)) if (!Same(entry, Application.ExecutablePath)) try { if (Directory.Exists(entry)) Directory.Delete(entry, true); else File.Delete(entry); } catch (Exception) { }
                foreach (string name in moved) MoveBack(Path.Combine(backup, name), Path.Combine(home, name), null);
                try { Directory.Delete(backup); } catch (Exception) { }
                return "The new version couldn't be laid, so her folder was put back as it was: " + error.Message;
            }
            foreach (string name in moved)
                if (!LaidFresh(name)) MoveBack(Path.Combine(backup, name), Path.Combine(home, name), left);
            return null;
        }

        private static void MoveBack(string from, string to, List<string> left)
        {
            try
            {
                if (File.Exists(to) || Directory.Exists(to)) { if (left != null) left.Add(Path.GetFileName(from)); return; }
                if (Directory.Exists(from)) Directory.Move(from, to); else File.Move(from, to);
            }
            catch (Exception) { if (left != null) left.Add(Path.GetFileName(from)); }
        }

#if CHECKS
        // A clean update on a stand-in install: laid fresh with hers carried back, old programs and strays left aside,
        // a failed lay undone, put back and a clean start's clearing. Never the PC's own install.
        internal static void CheckCleanUpdate(List<string> checks)
        {
            string scratch = Path.Combine(Path.GetTempPath(), "capri-update-" + Guid.NewGuid().ToString("N").Substring(0, 8)), home = Path.Combine(scratch, "Capri");
            List<string> wrong = new List<string>();
            Action stand = delegate
            {
                if (Directory.Exists(scratch)) Directory.Delete(scratch, true);
                Directory.CreateDirectory(Path.Combine(home, "personalSettings", "censored"));
                Directory.CreateDirectory(Path.Combine(home, "mods"));
                Directory.CreateDirectory(Path.Combine(home, "brain"));
                File.WriteAllText(Path.Combine(home, "Capri.exe"), "old");
                File.WriteAllText(Path.Combine(home, "Capri.exe.1.bak"), "older");
                File.WriteAllText(Path.Combine(home, "Uninstall.exe"), "old");
                File.WriteAllText(Path.Combine(home, "old.dll"), "stray");
                File.WriteAllText(Path.Combine(home, ".capri-install"), "Capri desktop companion 0.9.500");
                File.WriteAllText(Path.Combine(home, "settings.json"), "{\"TermsAnswer\":1}");
                File.WriteAllText(Path.Combine(home, "personalSettings", "censored", "lines.md"), "their edit");
                File.WriteAllText(Path.Combine(home, "mods", "a.capri-mod"), "mod");
                File.WriteAllText(Path.Combine(home, "brain", "model.bin"), "pack");
            };
            Action lay = delegate
            {
                File.WriteAllText(Path.Combine(home, "Capri.exe"), "new");
                File.WriteAllText(Path.Combine(home, "Uninstall.exe"), "new");
                File.WriteAllText(Path.Combine(home, ".capri-install"), "Capri desktop companion 0.9.600");
            };
            try
            {
                stand();
                if (OldVersion(home) != "0.9.500") wrong.Add("the old version read " + OldVersion(home));
                List<string> left = new List<string>();
                string backup;
                string failed = CleanLay(home, OldVersion(home), lay, out backup, left);
                bool fresh = failed == null && left.Count == 0 && File.ReadAllText(Path.Combine(home, "Capri.exe")) == "new" && !File.Exists(Path.Combine(home, "Capri.exe.1.bak")) && !File.Exists(Path.Combine(home, "old.dll"));
                bool carried = File.Exists(Path.Combine(home, "settings.json")) && File.ReadAllText(Path.Combine(home, "personalSettings", "censored", "lines.md")) == "their edit"
                    && File.Exists(Path.Combine(home, "mods", "a.capri-mod")) && File.Exists(Path.Combine(home, "brain", "model.bin"));
                bool aside = Path.GetFileName(backup) == UpdateBackup + "0.9.500" && File.ReadAllText(Path.Combine(backup, "Capri.exe")) == "old" && File.Exists(Path.Combine(backup, "old.dll")) && !File.Exists(Path.Combine(backup, "settings.json"));
                if (!fresh || !carried || !aside) wrong.Add("a clean update: laid fresh " + fresh + ", hers carried back " + carried + ", the old programs aside " + aside + " (" + failed + ")");
                if (PutBack(home) != null || File.ReadAllText(Path.Combine(home, "Capri.exe")) != "old" || !File.Exists(Path.Combine(home, "old.dll")) || UpdateBackups(home).Count != 0 || !File.Exists(Path.Combine(home, "settings.json")))
                    wrong.Add("putting the version from before back didn't");
                failed = CleanLay(home, OldVersion(home), lay, out backup, left);
                ClearUpdateBackups(home);
                if (failed != null || Directory.Exists(backup)) wrong.Add("a clean start didn't clear the folder aside");
                stand();
                failed = CleanLay(home, OldVersion(home), delegate { lay(); throw new IOException("a stand-in failure"); }, out backup, left);
                if (failed == null || File.ReadAllText(Path.Combine(home, "Capri.exe")) != "old" || !File.Exists(Path.Combine(home, "settings.json")) || !File.Exists(Path.Combine(home, "old.dll")) || Directory.Exists(backup))
                    wrong.Add("a lay that failed didn't put her folder back as it was: " + failed);
                if (wrong.Count > 0) throw new Exception("A clean update went wrong: " + string.Join("; ", wrong.ToArray()));
                checks.Add("PASS a clean update: her folder moved aside beside her real one, the programs laid fresh, her settings, edited files, mods and packs carried back, old programs, spares and a stray library left aside; the version from before put back whole; a clean start clears what was aside; a lay that fails puts everything back");
            }
            finally { try { Directory.Delete(scratch, true); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); } }
        }
#endif

        // The folders an update set aside beside her real one.
        internal static List<string> UpdateBackups(string home)
        {
            List<string> found = new List<string>();
            try
            {
                string real = Final(home).TrimEnd('\\'), above = Path.GetDirectoryName(real);
                if (!string.IsNullOrEmpty(above) && Directory.Exists(above)) found.AddRange(Directory.GetDirectories(above, UpdateBackup + "*"));
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            return found;
        }

        // She started cleanly once after an update: what was set aside goes.
        internal static void ClearUpdateBackups(string home)
        {
            foreach (string backup in UpdateBackups(home))
                try { Directory.Delete(backup, true); CapriDesktop.Brain.Log("update", -1, "Removed " + Path.GetFileName(backup) + " after a clean start."); }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // Two starts that didn't finish after an update: the version from before is put back, programs and all; null when
        // done, else why. The newest backup is used.
        internal static string PutBack(string home)
        {
            List<string> backups = UpdateBackups(home);
            if (backups.Count == 0) return "There's no version from before to put back.";
            backups.Sort(delegate(string a, string b) { return Directory.GetCreationTimeUtc(b).CompareTo(Directory.GetCreationTimeUtc(a)); });
            string backup = backups[0];
            try
            {
                foreach (string entry in Directory.GetFileSystemEntries(backup))
                {
                    string name = Path.GetFileName(entry), here = Path.Combine(home, name);
                    if (File.Exists(here)) File.Delete(here);
                    else if (Directory.Exists(here)) Directory.Delete(here, true);
                    if (Directory.Exists(entry)) Directory.Move(entry, here); else File.Move(entry, here);
                }
                Directory.Delete(backup, true);
                return null;
            }
            catch (Exception error) { return "The version from before couldn't be put back: " + error.Message; }
        }

        // ---------- Starting fresh ----------
        // Their files moved by a named allow list into a dated folder before a fresh install; nothing is deleted.
        private static readonly string[] Theirs =
        {
            "settings.json", "settings.crashed.json", "apps.json", "apps-used.txt", "away.txt", "chat.json",
            "errors.txt", "hours.txt", "lines-log.txt", "lines-said.txt", "lore.txt", "lore.pack",
            "playlist.json", "reminders.json", "running.on", "websites.json", "security.json", "routines.json",
            "me.md", "memories.md", "personality.md", "personality.backup.md", "twitch.md", "twitch-seen.txt", "check-ins.md"
        };

        private static readonly string[] TheirFolders = { "personalSettings", "personalities", "attitudes", "games", "transcripts", "workspace", "clips" };

        // Whether there is anything of theirs here at all, which decides whether the choice is offered: on a
        // first install there is nothing to start fresh from and the tick would be noise.
        internal static bool Started() { return Started(InstallPath); }

        internal static bool Started(string home)
        {
            try
            {
                if (string.IsNullOrEmpty(home) || !Directory.Exists(home)) return false;
                foreach (string name in Theirs) if (File.Exists(Path.Combine(home, name))) return true;
                foreach (string name in TheirFolders) if (Directory.Exists(Path.Combine(home, name))) return true;
                return false;
            }
            catch (Exception) { return false; }
        }

        // Moves their files into a dated folder and says what it moved, or null when there was nothing. A name
        // it cannot move is left where it is and counted, since half a clean install is better than a failed
        // one and the window says how many.
        internal static string Aside(out int kept) { return Aside(InstallPath, out kept); }

        internal static string Aside(string home, out int kept)
        {
            kept = 0;
            if (string.IsNullOrEmpty(home) || !Directory.Exists(home)) return null;
            string into = Path.Combine(home, "before-" + DateTime.Now.ToString("yyyy-MM-dd-HHmm", System.Globalization.CultureInfo.InvariantCulture));
            int moved = 0;
            foreach (string name in Theirs)
            {
                string from = Path.Combine(home, name);
                List<string> theirs = CapriDesktop.SpareFiles.Of(from);
                if (File.Exists(from)) theirs.Insert(0, from);
                if (theirs.Count == 0) continue;
                foreach (string one in theirs)
                {
                    string each = Path.GetFileName(one);
                    try
                    {
                        Directory.CreateDirectory(into);
                        File.Move(one, Path.Combine(into, each));
                        moved++;
                    }
                    catch (Exception) { kept++; }
                }
            }
            foreach (string name in TheirFolders)
            {
                string from = Path.Combine(home, name);
                if (!Directory.Exists(from)) continue;
                try
                {
                    Directory.CreateDirectory(into);
                    Directory.Move(from, Path.Combine(into, name));
                    moved++;
                }
                catch (Exception) { kept++; }
            }
            return moved > 0 ? into : null;
        }

        internal static string Install(bool desktop, bool quiet = false)
        {
            string destination = InstallPath;
            string marker = Path.Combine(destination, ".capri-install");
            string existing = Path.Combine(destination, "Capri.exe");
            if (Directory.Exists(destination) && !File.Exists(marker) && File.Exists(existing) && !Ours(existing)) throw new IOException("There's a Capri.exe in " + destination + " that isn't this Capri. Move it out of that folder, then install again.");
            // Her input helper lives a moment after she closes, so a copy on its way out gets a few seconds. A copy started as
            // administrator can't be closed from here, so Windows is asked to close it as administrator (Splinter).
            foreach (Process process in Process.GetProcessesByName("Capri"))
            {
                using (process)
                {
                    bool held;
                    if (!string.Equals(SetupFiles.FileOf(process, out held), existing, StringComparison.OrdinalIgnoreCase)) continue;
                    if (Gone(process, 3000)) continue;
                    if (!held) throw new IOException("Quit Capri from her tray menu, then install again.");
                    if (quiet) throw new IOException(AdminCopy);
                    CloseAsAdmin(destination);
                    if (!Gone(process, 5000)) throw new IOException(AdminCopy);
                }
            }
            Directory.CreateDirectory(destination);
            // Best-effort: an uninstaller still exiting, or antivirus holding its old copy, mustn't fail the install.
            try { SetupFiles.ClearOld(Path.Combine(destination, "Uninstall.exe")); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            string version = Companion.Built;
            File.WriteAllText(marker, "Capri desktop companion " + version);
            string target = Path.Combine(destination, "Capri.exe");
            string temporary = target + ".new";
            try
            {
                File.WriteAllBytes(temporary, Payload());
                if (File.Exists(target)) Swap(temporary, target); else File.Move(temporary, target);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            File.Copy(Application.ExecutablePath, Path.Combine(destination, "Uninstall.exe"), true);
            // What the build leaves beside this file, carried over with her: her stories and her notice. Neither
            // replaces one already there, since that may be theirs.
            string beside = Path.GetDirectoryName(Application.ExecutablePath);
            foreach (string spare in new[] { "lore.md", "NOTICE.txt" })
            {
                try
                {
                    string from = Path.Combine(beside, spare), to = Path.Combine(destination, spare);
                    if (File.Exists(from) && !File.Exists(to)) File.Copy(from, to, false);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            string unsaved = StartMenu(destination);
            string onDesktop = desktop ? Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolderOption.Create), "Capri.lnk"), target) : null;
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
            {
                key.SetValue("DisplayName", "Capri");
                key.SetValue("DisplayVersion", version);
                key.SetValue("Publisher", "Capri fan project");
                key.SetValue("InstallLocation", destination);
                key.SetValue("DisplayIcon", Path.Combine(destination, "Capri.exe"));
                key.SetValue("UninstallString", "\"" + Path.Combine(destination, "Uninstall.exe") + "\" --uninstall");
                // Package managers like winget remove her through this one, with no window.
                key.SetValue("QuietUninstallString", "\"" + Path.Combine(destination, "Uninstall.exe") + "\" --silent --uninstall");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("EstimatedSize", new FileInfo(Application.ExecutablePath).Length * 2 / 1024, RegistryValueKind.DWord);
            }
            return unsaved ?? onDesktop;
        }

        // A window drawn on a plain backdrop and cut to its own shape, the way the screen shows it.
        private static void Snap(Form form, string path)
        {
            using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
            using (Bitmap sheet = new Bitmap(form.Width + 48, form.Height + 48))
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                using (Graphics graphics = Graphics.FromImage(sheet))
                {
                    graphics.Clear(Color.FromArgb(44, 47, 56));
                    if (form.Region != null)
                        using (Region shape = form.Region.Clone())
                        {
                            shape.Translate(24, 24);
                            graphics.SetClip(shape, CombineMode.Replace);
                        }
                    graphics.DrawImageUnscaled(bitmap, 24, 24);
                }
                sheet.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        // Fetches the real voice pack into a test folder, the same way an install does.
        private static int VerifyVoice(string data)
        {
            try
            {
                data = Path.GetFullPath(data);
                Directory.CreateDirectory(data);
                FetchVoice(data, delegate { }, delegate { return false; });
                if (!VoicePack.Complete(VoicePack.Folder(data))) throw new IOException("The local speech model is missing files after installing.");
                File.WriteAllText(Path.Combine(data, "voice-test.txt"), "PASS the local speech model downloaded, matched its SHA-256 and unpacked where Capri looks for it\n");
                return 0;
            }
            catch (Exception error) { Directory.CreateDirectory(data); File.WriteAllText(Path.Combine(data, "failure.txt"), error.ToString()); return 1; }
        }

        internal static string Megabytes(long bytes) { return (bytes >> 20) + " MB"; }

        // Her local speech model goes where Capri's own download puts it, under the same file names, so either can finish
        // a download the other started.
        internal static void FetchVoice(string data, Action<string, long, long> progress, Func<bool> stopped)
        {
            string folder = VoicePack.Folder(data);
            if (VoicePack.Complete(folder)) return;
            string root = Path.GetDirectoryName(folder);
            Directory.CreateDirectory(root);
            long needed = VoicePack.Size * 3 + (64L << 20);
            string drive = Path.GetPathRoot(root);
            if (new DriveInfo(drive).AvailableFreeSpace < needed) throw new IOException("The local speech model needs " + Megabytes(needed) + " free on " + drive.TrimEnd('\\') + ".");
            string zip = folder + ".zip";
            string part = zip + ".part";
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            long have = File.Exists(part) ? new FileInfo(part).Length : 0;
            if (have > VoicePack.Size) { File.Delete(part); have = 0; }
            int stalls = 0;
            while (have < VoicePack.Size)
            {
                long before = have;
                try
                {
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(VoicePack.Url);
                    request.UserAgent = "Capri-Setup";
                    request.Timeout = 30000;
                    request.ReadWriteTimeout = 60000;
                    if (have > 0) request.AddRange(have);
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    {
                        if (have > 0 && response.StatusCode != HttpStatusCode.PartialContent) have = 0;
                        using (Stream body = response.GetResponseStream())
                        using (FileStream file = new FileStream(part, have > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write))
                        {
                            byte[] buffer = new byte[1 << 16];
                            long shown = 0;
                            int read;
                            while ((read = body.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                if (stopped()) throw new OperationCanceledException();
                                file.Write(buffer, 0, read);
                                have += read;
                                if (have - shown >= (1L << 20)) { shown = have; progress("Downloading", have, VoicePack.Size); }
                            }
                        }
                    }
                }
                catch (WebException) { if (stopped()) throw new OperationCanceledException(); }
                catch (IOException) { if (stopped()) throw new OperationCanceledException(); }
                if (have < VoicePack.Size)
                {
                    stalls = have > before ? 0 : stalls + 1;
                    if (stalls >= 4) throw new IOException("The download keeps stopping. Check your connection.");
                    Thread.Sleep(1500 * stalls);
                }
            }
            progress("Checking", VoicePack.Size, VoicePack.Size);
            string sum;
            using (SHA256 sha = SHA256.Create())
            using (FileStream file = File.OpenRead(part)) sum = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
            if (have != VoicePack.Size || sum != VoicePack.Sha) { File.Delete(part); throw new IOException("The download came down damaged. Try again from Capri Mainframe."); }
            if (File.Exists(zip)) File.Delete(zip);
            File.Move(part, zip);
            progress("Unpacking", VoicePack.Size, VoicePack.Size);
            string staging = folder + ".new";
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            ZipFile.ExtractToDirectory(zip, staging);
            if (!VoicePack.Complete(staging)) throw new IOException("The download is missing files.");
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.Move(staging, folder);
            File.Delete(zip);
        }

        internal static bool Ours(string path)
        {
            try
            {
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
                return info.ProductName == "Capri" && string.Equals(info.OriginalFilename, "Capri.exe", StringComparison.OrdinalIgnoreCase);
            }
            catch (FileNotFoundException) { return false; }
        }

        // Her shortcuts go through Windows' own shell link instead of Windows Script Host, and an old read-only one is
        // replaced. When Windows still won't save one, its reason comes back instead of an error.
        private static string Shortcut(string path, string target, string arguments = null)
        {
            try
            {
                if (File.Exists(path)) File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
                IShellLinkW link = (IShellLinkW)new ShellLink();
                try
                {
                    link.SetPath(target);
                    if (!string.IsNullOrEmpty(arguments)) link.SetArguments(arguments);
                    link.SetWorkingDirectory(Path.GetDirectoryName(target));
                    link.SetDescription("Capri desktop companion");
                    link.SetIconLocation(target, 0);
                    ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Save(path, true);
                }
                finally { Marshal.FinalReleaseComObject(link); }
                return null;
            }
            catch (Exception error) { return error.Message; }
        }

        private static string LinkTarget(string path)
        {
            IShellLinkW link = (IShellLinkW)new ShellLink();
            try
            {
                ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Load(path, 0);
                System.Text.StringBuilder target = new System.Text.StringBuilder(1024);
                link.GetPath(target, target.Capacity, IntPtr.Zero, 0);
                return target.ToString();
            }
            finally { Marshal.FinalReleaseComObject(link); }
        }

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder file, int size, IntPtr found, int flags);
            void GetIDList(out IntPtr list);
            void SetIDList(IntPtr list);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder name, int size);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder folder, int size);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string folder);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder arguments, int size);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
            void GetHotkey(out short key);
            void SetHotkey(short key);
            void GetShowCmd(out int show);
            void SetShowCmd(int show);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder icon, int size, out int index);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string icon, int index);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relative, int reserved);
            void Resolve(IntPtr window, int flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
        }

        internal static void BeginUninstall() { BeginUninstall(""); }

        internal static void BeginUninstall(string also)
        {
            string temporary = Path.Combine(Path.GetTempPath(), "Capri-Uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
            File.Copy(Application.ExecutablePath, temporary);
            Process.Start(new ProcessStartInfo(temporary, "--remove-files \"" + HerFolder() + "\"" + (string.IsNullOrEmpty(also) ? "" : " " + also)) { UseShellExecute = true });
        }

#if CHECKS
        // An install reached by two paths: a junction in a temp folder over a stand-in one, a stand-in program started
        // by the real path, shortcuts to either path, and her Start menu folder, all under stand-ins; nothing of the
        // PC's own Start menu, desktop, entry or install is touched.
        internal static void CheckUninstallPaths(List<string> checks)
        {
            string scratch = Path.Combine(Path.GetTempPath(), "capri-paths-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string real = Path.Combine(scratch, "real"), link = Path.Combine(scratch, "link"), start = Path.Combine(scratch, "start"), desk = Path.Combine(scratch, "desk");
            Func<string> startWas = StartFolder, deskWas = Desktop;
            Process waiting = null;
            List<string> wrong = new List<string>();
            try
            {
                foreach (string folder in new[] { real, start, desk }) Directory.CreateDirectory(folder);
                using (Process made = Process.Start(new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + real + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }))
                    made.WaitForExit(10000);
                if (!Directory.Exists(link)) { checks.Add("LIMIT uninstall paths: no junction could be made here, so the two-path cases weren't checked"); return; }
                File.WriteAllText(Path.Combine(real, "Capri.exe"), "");
                File.WriteAllText(Path.Combine(real, "Uninstall.exe"), "");
                if (!Same(link, real) || !Inside(Path.Combine(real, "Capri.exe"), link) || !Inside(Path.Combine(link, "Capri.exe"), real) || Inside(Path.Combine(scratch, "Capri.exe"), link))
                    wrong.Add("a junction and its real folder weren't read as one");
                // A program started by the real path is found, and closed, given the junction.
                string stand = Path.Combine(real, "capri-test-wait.exe");
                File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), stand);
                waiting = Process.Start(new ProcessStartInfo(stand, "-n 60 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
                Thread.Sleep(300);
                CloseEverything(link, 1000);
                if (!waiting.WaitForExit(5000)) wrong.Add("a program started by the real path wasn't closed through the junction");
                lock (NotEnded) if (NotEnded.Count > 0) wrong.Add("it said it couldn't end " + string.Join(", ", NotEnded.ToArray()));
                // Her Start menu folder replaces the loose shortcut, and every shortcut of hers goes by either path.
                StartFolder = delegate { return start; };
                Desktop = delegate { return desk; };
                Shortcut(Path.Combine(start, "Capri.lnk"), Path.Combine(real, "Capri.exe"));
                Shortcut(Path.Combine(desk, "Capri (2).lnk"), Path.Combine(link, "Capri.exe"));
                Shortcut(Path.Combine(desk, "Other.lnk"), Path.Combine(Environment.SystemDirectory, "notepad.exe"));
                string menuMade = StartMenu(link);
                string menu = Path.Combine(start, MenuFolder);
                if (menuMade != null || !File.Exists(Path.Combine(menu, "Capri.lnk")) || !File.Exists(Path.Combine(menu, UninstallLink)) || File.Exists(Path.Combine(start, "Capri.lnk")))
                    wrong.Add("her Start menu folder wasn't Capri and Uninstall Capri with the loose shortcut gone: " + menuMade);
                if (!Inside(LinkTarget(Path.Combine(menu, UninstallLink)), real)) wrong.Add("Uninstall Capri doesn't lead to her Uninstall.exe");
                List<string> left = RemoveShortcuts(real);
                if (left.Count > 0 || Directory.Exists(menu) || File.Exists(Path.Combine(desk, "Capri (2).lnk")) || !File.Exists(Path.Combine(desk, "Other.lnk")))
                    wrong.Add("her shortcuts didn't all go, or another's went: left " + string.Join(", ", left.ToArray()));
                // The uninstaller finds her folder from its own place.
                File.WriteAllText(Path.Combine(real, ".capri-install"), "Capri desktop companion");
                if (!Same(HerFolderOf(link), real) || HerFolderOf(scratch) != InstallPath) wrong.Add("her folder wasn't found from the uninstaller's own place");
                Func<string, bool> askWas = AskAdmin, elevateWas = Elevate;
                List<string> asked = new List<string>(), raised = new List<string>();
                try
                {
                    AskAdmin = delegate(string message) { asked.Add(message); return true; };
                    Elevate = delegate(string folder) { raised.Add(folder); return true; };
                    int code = Finish(real, "These couldn't be removed:\nCapri.exe", true, 1);
                    bool offered = code == 0 && (Elevated() ? asked.Count == 0 : asked.Count == 1 && raised.Count == 1 && Same(raised[0], real) && asked[0].Contains(real));
                    if (!offered) wrong.Add("what was left didn't offer to try again as administrator for the same folder");
                }
                finally { AskAdmin = askWas; Elevate = elevateWas; }
                if (wrong.Count > 0) throw new Exception("Uninstalling went wrong with two paths: " + string.Join("; ", wrong.ToArray()));
                checks.Add("PASS a folder reached by a junction and its real path is one folder: a program started either way is closed, her Start menu folder holds Capri and Uninstall Capri in place of the loose shortcut, every shortcut of hers goes by either path and the folder with them, another's stays, and the uninstaller finds her folder from its own place; anything left offers one key to try again as administrator for that folder only");
            }
            finally
            {
                StartFolder = startWas;
                Desktop = deskWas;
                if (waiting != null) { try { if (!waiting.HasExited) waiting.Kill(); } catch (Exception) { } waiting.Dispose(); }
                // The junction first, on its own, so nothing is followed through it; then the rest.
                try { if (Directory.Exists(link)) Directory.Delete(link); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                try { Directory.Delete(scratch, true); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
        }
#endif

#if CHECKS
        // The uninstall window's picks: its picture, Everything ticking the rest, and each pick taking only its own files
        // from a stand-in folder.
        internal static void CheckUninstallPicks(List<string> checks, string output)
        {
            List<string> wrong = new List<string>();
            using (SetupWindow window = new SetupWindow(true))
            {
                window.StartPosition = FormStartPosition.Manual;
                window.Location = new Point(-30000, -30000);
                window.Show();
                Application.DoEvents();
                using (Bitmap picture = new Bitmap(window.Width, window.Height))
                {
                    window.DrawToBitmap(picture, new Rectangle(Point.Empty, picture.Size));
                    picture.Save(Path.Combine(output, "308-uninstall-2.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                if (window.Also != "") wrong.Add("something was picked to start with: " + window.Also);
                window.PickForCheck("mods");
                window.PickForCheck("md");
                if (window.Also != "mods,md") wrong.Add("two picks read " + window.Also);
                window.PickForCheck("everything");
                if (window.Also != "everything") wrong.Add("Everything read " + window.Also);
                window.Close();
            }
            string scratch = Path.Combine(Path.GetTempPath(), "capri-picks-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                Func<string> lay = delegate
                {
                    if (Directory.Exists(scratch)) Directory.Delete(scratch, true);
                    string own = Path.Combine(scratch, "personalSettings");
                    Directory.CreateDirectory(Path.Combine(own, "censored"));
                    Directory.CreateDirectory(Path.Combine(scratch, "mods", "a-data"));
                    File.WriteAllText(Path.Combine(scratch, "settings.json"), "{}");
                    File.WriteAllText(Path.Combine(scratch, "settings.json.bak"), "{}");
                    File.WriteAllText(Path.Combine(own, "censored", "lines.md"), "# lines");
                    File.WriteAllText(Path.Combine(own, "censored", "lines.md.1700000000.bak"), "# old");
                    File.WriteAllText(Path.Combine(own, "me.md"), "# me");
                    File.WriteAllText(Path.Combine(own, "memories.md"), "# memories");
                    File.WriteAllText(Path.Combine(scratch, "chat.json"), "[]");
                    return own;
                };
                string mine = lay();
                RemoveHers(scratch, "settings");
                if (File.Exists(Path.Combine(scratch, "settings.json")) || File.Exists(Path.Combine(scratch, "settings.json.bak")) || !Directory.Exists(Path.Combine(scratch, "mods"))) wrong.Add("Settings took the wrong files");
                mine = lay();
                RemoveHers(scratch, "mods");
                if (Directory.Exists(Path.Combine(scratch, "mods")) || !File.Exists(Path.Combine(scratch, "settings.json"))) wrong.Add("Mods took the wrong files");
                mine = lay();
                RemoveHers(scratch, "md");
                if (File.Exists(Path.Combine(mine, "censored", "lines.md")) || File.Exists(Path.Combine(mine, "censored", "lines.md.1700000000.bak")) || !File.Exists(Path.Combine(mine, "me.md")) || !File.Exists(Path.Combine(mine, "memories.md"))) wrong.Add(".md files took the wrong files");
                mine = lay();
                File.WriteAllText(Path.Combine(scratch, ".capri-install"), "x");
                RemoveHers(scratch, "everything");
                if (Directory.GetFileSystemEntries(scratch).Length != 1) wrong.Add("Everything left " + Directory.GetFileSystemEntries(scratch).Length + " things");
                mine = lay();
                RemoveHers(scratch, "");
                if (!File.Exists(Path.Combine(scratch, "settings.json")) || !Directory.Exists(Path.Combine(scratch, "mods"))) wrong.Add("no pick took files");
            }
            finally { try { Directory.Delete(scratch, true); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); } }
            if (wrong.Count > 0) throw new Exception("The uninstall's picks went wrong: " + string.Join("; ", wrong.ToArray()));
            checks.Add("PASS the uninstall window's picks: none to start with, Everything ticks the rest, and each takes only its own: Settings settings.json and its copies, Mods her mods folder, .md files her shipped .md files and their .bak copies but never me.md or her memories, Everything all of her folder (308-uninstall-2.png)");
        }
#endif

        // Her folder for an uninstall: the one this program sits in when it holds her install marker (Uninstall.exe in it),
        // else where she installs.
        internal static string HerFolder() { return HerFolderOf(Path.GetDirectoryName(Application.ExecutablePath)); }

        internal static string HerFolderOf(string own)
        {
            return File.Exists(Path.Combine(own, ".capri-install")) && File.Exists(Path.Combine(own, "Capri.exe")) ? own : InstallPath;
        }

        private static int RemoveFiles(bool silent, string folder) { return RemoveFiles(silent, folder, ""); }

        private static int RemoveFiles(bool silent, string folder, string also)
        {
            string destination = folder;
            try
            {
                destination = Path.GetFullPath(folder);
                if (!File.Exists(Path.Combine(destination, ".capri-install"))) throw new IOException("The Capri install marker could not be verified in " + destination + ".");
                if (CloseEverything(destination))
                {
                    if (silent) throw new IOException(AdminCopy);
                    CloseAsAdmin(destination);
                    if (CloseEverything(destination)) throw new IOException(AdminCopy + Ended());
                }
                lock (NotEnded) if (NotEnded.Count > 0) throw new IOException("Capri couldn't be closed, so nothing was removed." + Ended());
                List<string> left = new List<string>();
                string uninstaller = Path.Combine(destination, "Uninstall.exe");
                SetupFiles.Retire(uninstaller, string.Equals(Path.GetFullPath(Application.ExecutablePath), uninstaller, StringComparison.OrdinalIgnoreCase));
                try
                {
                    SetupFiles.RemoveApp(Path.Combine(destination, "Capri.exe"));
                    foreach (string pack in Packs)
                    {
                        string packed = Path.Combine(destination, pack);
                        Patiently(delegate { if (Directory.Exists(packed)) Directory.Delete(packed, true); });
                    }
                    left = RemoveShortcuts(destination);
                    left.AddRange(RemoveHers(destination, also));
                    SetupFiles.Unmark(destination);
                }
                catch
                {
                    // Her uninstall entry still points at the uninstaller, so it goes back for the next try.
                    SetupFiles.Unretire(uninstaller, Application.ExecutablePath);
                    throw;
                }
                // Her Apps & Features entry goes when it is this folder's, whichever path it was written with.
                using (RegistryKey entry = Registry.CurrentUser.OpenSubKey(RegistryPath))
                {
                    string listed = entry == null ? null : entry.GetValue("InstallLocation") as string;
                    if (entry != null && (listed == null || Same(listed, destination))) { entry.Close(); Registry.CurrentUser.DeleteSubKeyTree(RegistryPath, false); }
                }
                File.Delete(Path.Combine(destination, ".capri-install"));
                string notGone = left.Count == 0 ? "" : "\n\nThese couldn't be removed:\n" + string.Join("\n", left.ToArray());
                if (silent && left.Count > 0) Console.Error.WriteLine(notGone.Trim());
                string kept = (also ?? "").Contains("everything") ? "Capri and all her files have been removed." : string.IsNullOrEmpty(also) ? "Capri has been removed.\n\nYour position and size settings were kept in " + destination + "." : "Capri has been removed, with her " + also.Replace("md", ".md files").Replace(",", ", ") + ".";
                if (!silent) return Finish(destination, kept + notGone, left.Count > 0, 0);
                return 0;
            }
            catch (Exception error) { if (silent) { Console.Error.WriteLine(error.Message); return 1; } return Finish(destination, error.Message, true, 1); }
        }

        // Her last word: with anything left and not yet as administrator, one key tries again as administrator, for this
        // same folder only. Checks stand in for both.
        internal static Func<string, bool> AskAdmin = delegate(string message) { return Question.Ask(null, message, "Capri", "Try as administrator", "Close"); };
        internal static Func<string, bool> Elevate = delegate(string folder)
        {
            try { using (Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--remove-files \"" + folder.TrimEnd('\\') + "\"") { UseShellExecute = true, Verb = "runas" })) { } return true; }
            catch (System.ComponentModel.Win32Exception) { return false; }
        };

        internal static bool Elevated()
        {
            using (System.Security.Principal.WindowsIdentity me = System.Security.Principal.WindowsIdentity.GetCurrent())
                return new System.Security.Principal.WindowsPrincipal(me).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }

        // Her own files the uninstall was asked to take too, only inside her folder: settings (settings.json and its
        // copies), mods (her mods folder), .md files (her shipped .md files and their .bak copies, not me.md or memories),
        // or everything left in it. What couldn't go is named.
        internal static List<string> RemoveHers(string folder, string also)
        {
            List<string> left = new List<string>();
            string[] picked = (also ?? "").Split(',');
            bool all = Array.IndexOf(picked, "everything") >= 0;
            if (!all && Array.IndexOf(picked, "settings") < 0 && Array.IndexOf(picked, "mods") < 0 && Array.IndexOf(picked, "md") < 0) return left;
            Action<string> gone = delegate(string path)
            {
                if (!Inside(path, folder)) return;
                try
                {
                    if (Directory.Exists(path)) { if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) Directory.Delete(path); else Directory.Delete(path, true); }
                    else if (File.Exists(path)) { File.SetAttributes(path, FileAttributes.Normal); File.Delete(path); }
                }
                catch (Exception) { left.Add(path); }
            };
            if (all)
            {
                foreach (string one in Directory.GetFileSystemEntries(folder)) if (!string.Equals(Path.GetFileName(one), ".capri-install", StringComparison.OrdinalIgnoreCase)) gone(one);
                return left;
            }
            if (Array.IndexOf(picked, "settings") >= 0)
                foreach (string one in Directory.GetFiles(folder, "settings*.json*")) gone(one);
            if (Array.IndexOf(picked, "mods") >= 0) gone(Path.Combine(folder, "mods"));
            string own = Path.Combine(folder, "personalSettings");
            if (Array.IndexOf(picked, "md") >= 0 && Directory.Exists(own))
                foreach (string one in Directory.GetFiles(own, "*.md*", SearchOption.AllDirectories))
                {
                    string name = Path.GetFileName(one).ToLowerInvariant();
                    if (name.StartsWith("me.md", StringComparison.Ordinal) || name.StartsWith("memories.md", StringComparison.Ordinal)) continue;
                    if (name.EndsWith(".md", StringComparison.Ordinal) || name.Contains(".md.") && name.EndsWith(".bak", StringComparison.Ordinal)) gone(one);
                }
            return left;
        }

        internal static int Finish(string folder, string message, bool leftover, int code)
        {
            if (leftover && !Elevated() && AskAdmin(message + "\n\nAs administrator she can close and remove what was left in " + folder + "."))
                return Elevate(folder) ? 0 : 1;
            Notice.Tell(null, message, leftover ? "Capri could not be removed" : "Capri", leftover);
            return code;
        }

        // Everything running from her folder is closed first: asked as her tray Quit asks, given 10 s to save and go,
        // then stopped, clearing her markers. Nothing outside the folder is touched. True when one ran as administrator.
        internal static bool CloseEverything(string folder) { return CloseEverything(folder, AskWait); }

        internal const int AskWait = 10000;

        // What the last close couldn't end, by name and PID, for the uninstaller to say.
        internal static readonly List<string> NotEnded = new List<string>();

        private static string Ended() { lock (NotEnded) return NotEnded.Count == 0 ? "" : "\n\nStill running: " + string.Join(", ", NotEnded.ToArray()); }

        internal static bool CloseEverything(string folder, int wait)
        {
            int self;
            bool locked = false, stopped = false;
            lock (NotEnded) NotEnded.Clear();
            using (Process me = Process.GetCurrentProcess()) self = me.Id;
            List<Process> inside = new List<Process>();
            foreach (Process process in Process.GetProcesses())
            {
                bool keep = false;
                try
                {
                    bool held;
                    if (process.Id != self && Inside(SetupFiles.FileOf(process, out held), folder)) { if (held) { locked = true; lock (NotEnded) NotEnded.Add(process.ProcessName + ".exe (PID " + process.Id + ", running as administrator)"); } else keep = true; }
                }
                catch (System.ComponentModel.Win32Exception) { }
                catch (InvalidOperationException) { }
                catch (NotSupportedException) { }
                if (keep) inside.Add(process);
                else process.Dispose();
            }
            try
            {
                foreach (Process process in inside)
                    try { if (!process.HasExited) CloseWindows(process.Id); }
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception) { }
                Stopwatch waited = Stopwatch.StartNew();
                foreach (Process process in inside) Gone(process, (int)Math.Max(0, wait - waited.ElapsedMilliseconds));
                foreach (Process process in inside)
                {
                    try
                    {
                        if (process.HasExited) continue;
                        // Ended by its PID once its wait is up.
                        process.Kill();
                        if (process.WaitForExit(5000)) stopped = true;
                        else lock (NotEnded) NotEnded.Add(process.ProcessName + ".exe (PID " + process.Id + ")");
                    }
                    catch (System.ComponentModel.Win32Exception) { lock (NotEnded) NotEnded.Add("PID " + process.Id); }
                    catch (InvalidOperationException) { }
                    catch (NotSupportedException) { lock (NotEnded) NotEnded.Add("PID " + process.Id); }
                }
            }
            finally { foreach (Process process in inside) process.Dispose(); }
            if (stopped) Unmark(folder);
            return locked;
        }

        // --close, the installer and uninstall ask before they stop: a stand-in that closes when asked goes on its own and
        // takes its markers with it; one that won't is stopped and its markers cleared; a marker nobody closed, a real
        // crash, is left for her crash guard.
#if CHECKS
        internal static void CloseCheck(List<string> checks, string folder)
        {
            Directory.CreateDirectory(folder);
            string exe = Path.Combine(folder, "Capri.exe"), marker = Path.Combine(folder, CapriDesktop.RunMark.Marker), start = Path.Combine(folder, CapriDesktop.Program.StartMarker);
            File.Copy(Application.ExecutablePath, exe, true);
            Func<string, Process> stand = delegate(string kind)
            {
                File.WriteAllText(marker, "1");
                File.WriteAllText(start, "1");
                string ready = Path.Combine(folder, kind + ".up");
                if (File.Exists(ready)) File.Delete(ready);
                Process made = Process.Start(new ProcessStartInfo(exe, "--smoke-child " + kind + " \"" + marker + "\" \"" + ready + "\"") { UseShellExecute = false });
                Stopwatch up = Stopwatch.StartNew();
                while (!File.Exists(ready) && up.ElapsedMilliseconds < 20000) Thread.Sleep(50);
                return made;
            };
            Process closes = null, stays = null;
            try
            {
                closes = stand("closes");
                Stopwatch took = Stopwatch.StartNew();
                CloseEverything(folder, AskWait);
                bool asked = closes.HasExited && took.ElapsedMilliseconds < AskWait - 1000 && !File.Exists(marker) && !File.Exists(start);
                stays = stand("stays");
                CloseEverything(folder, 1500);
                bool stopped = stays.HasExited && !File.Exists(marker) && !File.Exists(start);
                string why = "exited " + stays.HasExited + ", marker " + File.Exists(marker) + ", start " + File.Exists(start) + ", up " + File.Exists(Path.Combine(folder, "stays.up"));
                File.WriteAllText(marker, "1");
                CloseEverything(folder, 500);
                bool crash = File.Exists(marker);
                if (!asked || !stopped || !crash)
                    throw new Exception("Closing her from outside went wrong: asked and closed on its own with no marker left " + asked + ", one that wouldn't close stopped with its markers cleared " + stopped + " (" + why + "), a real crash's marker kept " + crash);
                checks.Add("PASS --close, the installer and uninstall ask her to close and wait up to " + AskWait / 1000 + " s: a stand-in that closes when asked goes on its own with no marker left, one that won't is stopped and its running and starting markers cleared, so neither counts as a crash; a marker nobody closed still does");
            }
            finally
            {
                foreach (Process one in new[] { closes, stays })
                    if (one != null) { try { if (!one.HasExited) one.Kill(); } catch (Exception) { } one.Dispose(); }
                try { Directory.Delete(folder, true); } catch (Exception) { }
            }
        }
#endif

        // A stop that was ours, not a crash: her running and starting markers go, so her next start doesn't count it
        // toward switching her mods off or putting her settings aside.
        private static void Unmark(string folder)
        {
            foreach (string name in new[] { CapriDesktop.RunMark.Marker, CapriDesktop.Program.StartMarker })
            {
                try { File.Delete(Path.Combine(folder, name)); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        internal const string AdminCopy = "Capri is running as administrator, so setup can't close her. Quit her from her tray menu, then try again.";

        // Starts this setup again as administrator only to close what runs from her folder, and waits for it. No to Windows'
        // prompt leaves her running, so it says how to quit her instead. The caller checks she's gone.
        private static void CloseAsAdmin(string folder)
        {
            try
            {
                using (Process helper = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--close \"" + folder.TrimEnd('\\') + "\"") { UseShellExecute = true, Verb = "runas" }))
                    if (helper == null || !helper.WaitForExit(30000)) throw new IOException(AdminCopy);
            }
            catch (System.ComponentModel.Win32Exception) { throw new IOException(AdminCopy); }
        }

        private static bool Gone(Process process, int wait)
        {
            try { return process.WaitForExit(wait); }
            catch (System.ComponentModel.Win32Exception) { return false; }
        }

        // The administrator copy CloseAsAdmin starts. It only closes things in a folder named Capri with her install marker.
        internal static int CloseFolder(string folder)
        {
            try
            {
                folder = Path.GetFullPath(folder);
                if (!SetupFiles.Closable(folder)) return 2;
                return CloseEverything(folder) ? 1 : 0;
            }
            catch (Exception) { return 1; }
        }

        // Compared as the files they are: a junction, its real folder, a subst drive and a short name all come to one path.
        internal static bool Inside(string file, string folder)
        {
            if (string.IsNullOrEmpty(file) || string.IsNullOrEmpty(folder)) return false;
            return Final(file).StartsWith(Final(folder).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool Same(string a, string b) { return !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) && string.Equals(Final(a).TrimEnd('\\'), Final(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }

        // The path Windows ends at for a file or folder that is there; for one that isn't, its nearest folder that is,
        // with the rest added back.
        internal static string Final(string path)
        {
            string full;
            try { full = Path.GetFullPath(path); } catch (Exception) { return path ?? ""; }
            string rest = "";
            for (string at = full; !string.IsNullOrEmpty(at); at = Path.GetDirectoryName(at))
            {
                string found = FinalOf(at);
                if (found != null) return rest.Length == 0 ? found : Path.Combine(found, rest);
                rest = rest.Length == 0 ? Path.GetFileName(at) : Path.Combine(Path.GetFileName(at), rest);
            }
            return full;
        }

        private static string FinalOf(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return null;
            using (Microsoft.Win32.SafeHandles.SafeFileHandle handle = CreateFile(path, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero))
            {
                if (handle.IsInvalid) return null;
                System.Text.StringBuilder named = new System.Text.StringBuilder(1024);
                int length = GetFinalPathNameByHandle(handle, named, named.Capacity, 0);
                if (length <= 0 || length >= named.Capacity) return null;
                string said = named.ToString();
                if (said.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)) return @"\\" + said.Substring(8);
                return said.StartsWith(@"\\?\", StringComparison.Ordinal) ? said.Substring(4) : said;
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(string name, int access, int share, IntPtr security, int disposition, int flags, IntPtr template);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern int GetFinalPathNameByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle handle, System.Text.StringBuilder path, int size, int flags);

        // A file can stay locked for a moment after the program holding it closes.
        private static void Patiently(Action remove)
        {
            SetupFiles.Patiently(remove);
        }

        // A shortcut that can't be read or removed stays behind instead of stopping the rest of the uninstall.
        private static void DeleteShortcut(string path, string destination)
        {
            try { if (File.Exists(path) && Inside(LinkTarget(path), destination)) File.Delete(path); }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // Her Start menu folder, and where the loose shortcut of older installs sat. Checks point them elsewhere.
        internal static Func<string> StartFolder = delegate { return Environment.GetFolderPath(Environment.SpecialFolder.Programs, Environment.SpecialFolderOption.Create); };
        internal static Func<string> Desktop = delegate { return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolderOption.Create); };
        internal const string MenuFolder = "Capri", UninstallLink = "Uninstall Capri.lnk";

        // A Start menu folder "Capri" with Capri and Uninstall Capri; a loose Capri.lnk of an older install goes into it.
        internal static string StartMenu(string destination)
        {
            string menu = Path.Combine(StartFolder(), MenuFolder);
            Directory.CreateDirectory(menu);
            string made = Shortcut(Path.Combine(menu, "Capri.lnk"), Path.Combine(destination, "Capri.exe")) ?? Shortcut(Path.Combine(menu, UninstallLink), Path.Combine(destination, "Uninstall.exe"), "--uninstall");
            if (made == null) DeleteShortcut(Path.Combine(StartFolder(), "Capri.lnk"), destination);
            return made;
        }

        // Every shortcut of hers that leads into her folder, by either path, in the Start menu, her folder there and on
        // the desktop, then the folder itself when it is empty. What couldn't go is named.
        internal static List<string> RemoveShortcuts(string destination)
        {
            List<string> left = new List<string>();
            string menu = Path.Combine(StartFolder(), MenuFolder);
            foreach (string folder in new[] { StartFolder(), menu, Desktop() })
            {
                if (!Directory.Exists(folder)) continue;
                foreach (string link in Directory.GetFiles(folder, "*.lnk"))
                {
                    string target;
                    try { target = LinkTarget(link); } catch (Exception) { continue; }
                    if (!Inside(target, destination)) continue;
                    try { File.Delete(link); } catch (Exception) { left.Add(link); }
                }
            }
            try { if (Directory.Exists(menu) && Directory.GetFileSystemEntries(menu).Length == 0) Directory.Delete(menu); }
            catch (Exception) { left.Add(menu); }
            return left;
        }
    }

    // Her installer in the Mainframe's look: borderless, clipped corners, red rim and hot strokes over the wine glow, a
    // rose INSTALL CAPRI title, options on a band, cyan ticks, a cyan Install Capri (red Uninstall). It drags
    // anywhere and scales.
    internal sealed class SetupWindow : Form
    {
        private readonly bool removing;
        internal const int Wide = 680, Tall = 470;
        // The uninstall window is narrow, its picks one to a line.
        internal const int Narrow = 400;
        private readonly int wide;
        private readonly float s, text;
        private readonly PictureBox chip;
        private readonly Plate primary;
        private readonly Tick desktop;
        private readonly Tick launch;
        private readonly Tick fresh;
        private readonly Tick everything, settingsTick, modsTick, mdTick;
        private readonly Label section;
        private readonly Label status;
        private readonly Label stage;
        private readonly Label note;
        private readonly Plate cancel;
        private long got, total;
        private string noShortcut;
        private bool fetching;
        private volatile bool stopping;

        internal SetupWindow(bool uninstall)
        {
            removing = uninstall;
            wide = removing ? Narrow : Wide;
            Text = removing ? "Uninstall Capri" : "Install Capri";
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = true;
            AutoScaleMode = AutoScaleMode.None;
            // Fitted to the work area of the screen it opens on: at 1280 x 720 and 150% the whole window would not fit.
            Rectangle area = Fits.Area(Control.MousePosition);
            s = Fits.Scale(new Size(wide, Tall), Fits.Device(this), area, 0.5f);
            // Her fonts are in points, which Windows already draws at the screen's own scale.
            text = s / (DeviceDpi / 96f);
            ClientSize = new Size(Px(wide), Px(Tall));
            StartPosition = FormStartPosition.Manual;
            Location = Fits.Centred(Size, area);
            BackColor = Neon.Ground;
            ForeColor = Neon.White;
            DoubleBuffered = true;
            Font = Neon.Type(10.5f * text);
            using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("Capri.Chip.ico")) if (source != null) Icon = new Icon(source);
            LabelAt(removing ? "UNINSTALL CAPRI" : "INSTALL CAPRI", 24, 14, wide - 140, 32, Neon.Display(15f * text), Neon.Rose);
            Glyph(1, wide - 86, delegate { WindowState = FormWindowState.Minimized; });
            Glyph(2, wide - 54, delegate { Close(); });
            LabelAt(removing ? "See you around." : "Unofficial\nDesktop Mate!", 32, removing ? 60 : 64, removing ? 250 : 420, removing ? 80 : 124, Neon.Display((removing ? 26f : 30f) * text), Neon.White);
            chip = new PictureBox();
            chip.Bounds = removing ? Box(wide - 126, 62, 96, 64) : Box(462, 74, 180, 112);
            chip.SizeMode = PictureBoxSizeMode.Zoom;
            chip.BackColor = Color.Transparent;
            using (Stream image = Assembly.GetExecutingAssembly().GetManifestResourceStream("Capri.Chip.png")) if (image != null) using (Bitmap loaded = new Bitmap(image)) chip.Image = new Bitmap(loaded);
            Controls.Add(chip);
            LabelAt(removing ? "Remove the app and its shortcuts for this Windows user." : File.Exists(Path.Combine(Program.InstallPath, "settings.json")) ? "" : "She'll show you around when she opens.", 34, removing ? 146 : 198, wide - 68, removing ? 44 : 50, Neon.Type(11f * text), Neon.Faint);
            section = LabelAt(removing ? "ALSO REMOVE" : "OPTIONS", 34, removing ? 200 : 254, 300, 20, Neon.Type(10f * text, true), Neon.White);
            desktop = new Tick();
            desktop.Unit = s;
            desktop.Text = "Add a desktop shortcut";
            desktop.Bounds = Box(34, 284, 340, 26);
            desktop.Visible = !removing;
            launch = new Tick();
            launch.Unit = s;
            launch.Text = "Open Capri when finished";
            launch.Checked = true;
            launch.Bounds = Box(34, 314, 340, 26);
            launch.Visible = !removing;
            // Under Open Capri, always there so people know it exists; off by default, because an install over the top
            // keeping their files is the right thing nearly every time. On a first install there is nothing to start
            // fresh from, so it is shown but can't be ticked.
            fresh = new Tick();
            fresh.Unit = s;
            fresh.Text = "Clean install, keeping a copy of my current files";
            fresh.Bounds = Box(34, 344, 440, 26);
            fresh.Visible = !removing;
            fresh.Enabled = Program.Started();
            Controls.Add(desktop);
            Controls.Add(launch);
            Controls.Add(fresh);
            // What else goes with her, picked here; none ticked removes the app and its shortcuts only.
            everything = Pick("Everything", 34, 228);
            settingsTick = Pick("Settings", 34, 256);
            modsTick = Pick("Mods", 34, 284);
            mdTick = Pick(".md files", 34, 312);
            everything.CheckedChanged += delegate { foreach (Tick one in new[] { settingsTick, modsTick, mdTick }) { one.Enabled = !everything.Checked; if (everything.Checked) one.Checked = true; } Said(); };
            foreach (Tick one in new[] { settingsTick, modsTick, mdTick }) one.CheckedChanged += delegate { Said(); };
            status = LabelAt(removing ? "Your saved position and size will be kept." : "", 34, removing ? 350 : 374, wide - 76, 22, Neon.Type(9.5f * text), Neon.Faint);
            stage = LabelAt("", 34, 282, 604, 28, Neon.Type(13f * text, true), Neon.White);
            stage.Visible = false;
            note = LabelAt("", 34, 314, 604, 22, Neon.Type(10f * text), Neon.Faint);
            note.Visible = false;
            primary = new Plate();
            primary.Unit = s;
            primary.Text = removing ? "Uninstall" : "Install Capri";
            primary.Accent = removing ? Neon.Hot : Neon.Cyan;
            primary.Solid = true;
            primary.Font = Neon.Type(11.5f * text, true);
            primary.Bounds = Box(wide - 230, 400, 194, 42);
            primary.Click += Perform;
            Controls.Add(primary);
            cancel = new Plate();
            cancel.Unit = s;
            cancel.Text = "Cancel";
            cancel.Font = Neon.Type(11f * text, true);
            cancel.Bounds = Box(wide - 362, 400, 116, 42);
            cancel.Click += delegate
            {
                if (!fetching) { Close(); return; }
                // Skipping the speech model still leaves Capri installed; she can fetch it later from her Mainframe.
                stopping = true;
                cancel.Enabled = false;
                status.Text = "Skipping it...";
            };
            Controls.Add(cancel);
            AcceptButton = primary;
            CancelButton = cancel;
            Neon.Clip(this, 16 * s, 6 * s);
        }

        // Without a title bar it keeps a taskbar button that minimizes and restores it, and Alt + Space's menu.
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams value = base.CreateParams;
                value.Style = (value.Style | 0x00020000 | 0x00080000) & ~0x00010000;
                return value;
            }
        }

        private Tick Pick(string words, int x, int y)
        {
            Tick made = new Tick();
            made.Unit = s;
            made.Text = words;
            made.Bounds = Box(x, y, 200, 26);
            made.Visible = removing;
            Controls.Add(made);
            return made;
        }

        // What the picks take, for the uninstall: "everything", or settings, mods and md by name.
        internal string Also
        {
            get
            {
                if (everything == null || !removing) return "";
                if (everything.Checked) return "everything";
                List<string> picked = new List<string>();
                if (settingsTick.Checked) picked.Add("settings");
                if (modsTick.Checked) picked.Add("mods");
                if (mdTick.Checked) picked.Add("md");
                return string.Join(",", picked.ToArray());
            }
        }

        // The line under the picks says what stays.
        private void Said()
        {
            if (!removing || status == null) return;
            status.Text = everything.Checked ? "Nothing of hers will be kept." : Also.Length == 0 ? "Your saved position and size will be kept." : "The rest of her files will be kept.";
        }

#if CHECKS
        internal void PickForCheck(string which) { if (which == "everything") everything.Checked = true; else if (which == "settings") settingsTick.Checked = true; else if (which == "mods") modsTick.Checked = true; else if (which == "md") mdTick.Checked = true; }
#endif

        private int Px(float value) { return (int)Math.Round(value * s); }

        private Rectangle Box(int x, int y, int width, int height) { return new Rectangle(Px(x), Px(y), Px(width), Px(height)); }

        private Label LabelAt(string text, int x, int y, int width, int height, Font font, Color color)
        {
            Label label = new Neon.Caption();
            label.Text = text;
            label.Bounds = Box(x, y, width, height);
            label.Font = font;
            label.ForeColor = color;
            Controls.Add(label);
            return label;
        }

        // Minimize and close, top right like the Mainframe's. They're for the mouse, so Enter still means the main button.
        private void Glyph(int glyph, int x, EventHandler click)
        {
            Plate mark = new Plate();
            mark.Unit = s;
            mark.Glyph = glyph;
            mark.TabStop = false;
            mark.AccessibleName = glyph == 1 ? "Minimize" : "Close";
            mark.Bounds = Box(x, 14, 30, 26);
            mark.Click += click;
            Controls.Add(mark);
        }

        // It drags from anywhere that isn't a control.
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == 0x0084 && message.Result == new IntPtr(1)) message.Result = new IntPtr(2);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { Neon.Backdrop(e.Graphics, ClientSize, 210 * s); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;
            if (section.Visible)
            {
                // Its options sit on a dark band under a teal rule with a red tick, like a Mainframe section.
                RectangleF band = new RectangleF(section.Left - 14 * s, section.Top - 5 * s, ClientSize.Width - section.Left - 6 * s, 24 * s);
                using (Brush fill = new SolidBrush(Neon.Band)) graphics.FillRectangle(fill, band);
                using (Brush rule = new SolidBrush(Neon.Rule)) graphics.FillRectangle(rule, band.Left, band.Top, band.Width, Math.Max(1, (int)s));
                using (Brush tick = new SolidBrush(Neon.Hot)) graphics.FillRectangle(tick, band.Left, band.Top + 7 * s, 2.5f * s, band.Height - 13 * s);
            }
            using (Brush line = new SolidBrush(Neon.Edge)) graphics.FillRectangle(line, Px(34), Px(384), ClientSize.Width - Px(68), Math.Max(1, (int)s));
            Neon.Frame(graphics, ClientSize, s, 16 * s, 6 * s, 64 * s, 34 * s);
            if (!fetching || total <= 0) return;
            // The download's progress: a dark well filling with cyan segments.
            int tall = Px(7);
            Rectangle bar = new Rectangle(status.Left, status.Top - tall - Px(8), status.Width, tall);
            using (Brush track = new SolidBrush(Neon.Well)) graphics.FillRectangle(track, bar);
            int filled = (int)(bar.Width * Math.Min(1.0, (double)got / total));
            using (Brush fill = new SolidBrush(Neon.Cyan)) graphics.FillRectangle(fill, bar.X, bar.Y, filled, bar.Height);
            using (Brush gap = new SolidBrush(Neon.Well))
                for (int x = bar.X + Px(10); x < bar.X + filled; x += Px(10)) graphics.FillRectangle(gap, x, bar.Y, Math.Max(1, Px(2)), bar.Height);
            using (Pen rim = new Pen(Neon.WellEdge, Math.Max(1, (int)s))) graphics.DrawRectangle(rim, bar.X, bar.Y, bar.Width - 1, bar.Height - 1);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            stopping = true;
            base.OnFormClosing(e);
        }

        private void Perform(object sender, EventArgs e)
        {
            primary.Enabled = false;
            try
            {
                if (removing) { Program.BeginUninstall(Also); Close(); return; }
                if (fresh.Checked)
                {
                    status.Text = "Putting your files aside...";
                    int keptBack;
                    string into = Program.Aside(out keptBack);
                    if (into != null) status.Text = "Your files are in " + Path.GetFileName(into) + (keptBack > 0 ? ", and " + keptBack + " could not be moved" : "");
                }
                status.Text = "Making a little room for Capri...";
                noShortcut = Program.Install(desktop.Checked);
                try { SetupFiles.Mark(Program.InstallPath, "manual"); }
                catch (Exception error) { Notice.Tell(this, "Capri is installed, but the package marker couldn't be cleared. Delete " + SetupFiles.Marker + " in " + Program.InstallPath + " to let her update herself. " + error.Message, "Capri", true); }
            }
            catch (Exception error) { status.Text = "Capri could not finish. Your files are still here."; primary.Enabled = true; Notice.Tell(this, error.Message, "Capri couldn't install", true); return; }
            // She opens straight away and fetches her free voice herself while she shows them around, like the Store copy.
            Finish();
        }

        // Without her shortcut she's still installed, so they're told why and shown Capri.exe to pin.
        private void Finish()
        {
            string app = Path.Combine(Program.InstallPath, "Capri.exe");
            if (noShortcut != null)
            {
                Notice.Tell(this, "Capri is installed, but Windows wouldn't let the installer save her shortcut:\n" + noShortcut + "\n\nShe's in " + Program.InstallPath + ". Right-click Capri.exe there and pick Pin to Start.", "Capri", false);
                try { using (Process.Start("explorer.exe", "/select,\"" + app + "\"")) { } }
                catch (System.ComponentModel.Win32Exception) { }
            }
            if (launch.Checked) Process.Start(new ProcessStartInfo(app) { UseShellExecute = true });
            Close();
        }

        // Her local speech model comes with the install, and the window says so plainly while it downloads.
        private void FetchVoice()
        {
            Speaking(0, VoicePack.Size, "Starting the download...");
            Thread worker = new Thread(delegate()
            {
                string failed = null;
                try
                {
                    Program.FetchVoice(Program.InstallPath, delegate(string what, long have, long size)
                    {
                        string detail = what == "Downloading" ? Program.Megabytes(have) + " of " + Program.Megabytes(size) : what == "Checking" ? "Checking the download..." : "Unpacking...";
                        Post(delegate { Speaking(have, size, detail); });
                    }, delegate { return stopping; });
                }
                catch (OperationCanceledException) { failed = ""; }
                catch (Exception error) { failed = error.Message; }
                Post(delegate { Fetched(failed); });
            });
            worker.IsBackground = true;
            worker.Start();
        }

        private void Post(Action action)
        {
            try { if (!IsDisposed) BeginInvoke(action); } catch (InvalidOperationException) { }
        }

        internal void Speaking(long have, long size, string detail)
        {
            fetching = true;
            desktop.Visible = false;
            launch.Visible = false;
            // The Mainframe's own words for it, so people can find it again under VOICE.
            section.Text = "FREE VOICE";
            stage.Text = "Installing her free voice";
            note.Text = "This may take some time. Other voices are under VOICE in Capri Mainframe.";
            stage.Visible = true;
            note.Visible = true;
            if (!stopping) cancel.Text = "Skip";
            primary.Text = "Installing...";
            primary.Enabled = false;
            got = have;
            total = size;
            if (!stopping) status.Text = detail;
            Invalidate();
        }

        internal bool Saying(string title, string heading, string warning) { return stage.Visible && note.Visible && section.Text == title && stage.Text == heading && note.Text == warning; }

        private void Fetched(string failed)
        {
            fetching = false;
            if (string.IsNullOrEmpty(failed)) { Finish(); return; }
            stage.Text = "Couldn't install the local speech model";
            note.Text = "Capri is installed. Get it later in Capri Mainframe, or add an API key.";
            status.Text = failed;
            cancel.Visible = false;
            primary.Text = "Finish";
            primary.Click -= Perform;
            primary.Click += delegate { Finish(); };
            primary.Enabled = true;
            Invalidate();
        }

        protected override void Dispose(bool disposing) { if (disposing && chip.Image != null) chip.Image.Dispose(); base.Dispose(disposing); }

        // A tick box in her look: a small dark box with a red rim that fills cyan with a dark tick when it's on, its words in
        // white beside it. The rim lights up under the mouse or with keyboard focus.
        private sealed class Tick : CheckBox
        {
            private bool hot;
            // Its window's scale, which on a small screen is under the screen's own.
            internal float Unit;

            internal Tick()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
                Cursor = Cursors.Hand;
                UseMnemonic = false;
            }

            protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }

            protected override void OnMouseEnter(EventArgs e) { hot = Enabled; Invalidate(); base.OnMouseEnter(e); }

            protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                float s = Unit > 0 ? Unit : DeviceDpi / 96f;
                Graphics graphics = e.Graphics;
                Neon.Behind(graphics, this, 210 * s);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float side = 16 * s;
                RectangleF box = new RectangleF(1.5f * s, (Height - side) / 2f, side, side);
                bool lit = hot || (Focused && ShowFocusCues);
                using (GraphicsPath plate = Neon.Cut(box, 0, 0, 4 * s, 0))
                {
                    using (Brush fill = new SolidBrush(Checked ? Neon.Cyan : Neon.Well)) graphics.FillPath(fill, plate);
                    using (Pen rim = new Pen(Checked ? (lit ? Color.FromArgb(190, 252, 254) : Neon.Cyan) : lit ? Neon.Hot : Neon.WellEdge, Math.Max(1f, 1.2f * s))) graphics.DrawPath(rim, plate);
                }
                if (Checked)
                    using (Pen tick = new Pen(Neon.OnInk, 1.8f * s))
                    {
                        tick.LineJoin = LineJoin.Miter;
                        graphics.DrawLines(tick, new[] { new PointF(box.Left + side * 0.22f, box.Top + side * 0.52f), new PointF(box.Left + side * 0.42f, box.Top + side * 0.72f), new PointF(box.Left + side * 0.78f, box.Top + side * 0.3f) });
                    }
                int left = (int)(box.Right + 10 * s);
                // A box that can't be ticked yet reads faint, so it shows without inviting a click.
                TextRenderer.DrawText(graphics, Text, Font, new Rectangle(left, 0, Width - left, Height), Enabled ? Neon.White : Neon.Faint, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
        }
    }
}
