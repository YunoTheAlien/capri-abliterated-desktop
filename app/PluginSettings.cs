using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CapriDesktop
{
    // Security overrides from each Mods > INSTALLED cog: per SECURITY level, a plug-in's tools and websites, or a code
    // mod's running and tools, follow (/), allowed or denied. One signed file a side, written only by her cog;
    // tampered, it resets with a warning.
    internal static class PluginSettings
    {
        internal const int Follows = 0, Allowed = 1, Denied = 2;
        internal const string Tool = "tool", Site = "site", Run = "run";
        // The columns, by Security's own numbers: Lockdown, Guarded, Trusted, Blackwall, Custom.
        internal static readonly int[] Levels = { Security.Lockdown, Security.Guarded, Security.Trusted, Security.Blackwall, Security.Custom };
        private static readonly string[] Words = { "follows", "allow", "deny" };
        // Where each side kept them before they were signed: read once, signed into the new place, and removed.
        internal const string FileName = "plugins.txt";
        internal const string FolderName = "overrides", SealName = "seal.txt", Signed = "# signed ";
        // A file changed outside her cog: her warning, in a box over her first window, on its own thread.
        internal static Action<string> Tampered = Warn;

        private static void Warn(string said)
        {
#if !CROSS
            System.Windows.Forms.Form over = null;
            foreach (System.Windows.Forms.Form open in System.Windows.Forms.Application.OpenForms) { over = open; break; }
            if (over == null || over.IsDisposed || Displays.Test || Array.IndexOf(Environment.GetCommandLineArgs(), "--smoke-test") >= 0) return;
            try { over.BeginInvoke((System.Windows.Forms.MethodInvoker)delegate { Notice.Tell(over, said, "Security overrides", true); }); }
            catch (InvalidOperationException) { }
#endif
        }
        private static readonly Dictionary<string, byte[]> seals = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        private static readonly object gate = new object();
        // personalSettings; the check points it at its own folder.
        internal static string Settings;
        // Which side is in use; the check sets it to try both.
        internal static Func<bool> SecondSide = delegate { return Brain.SenpaiOn; };

        // Other text signed with the same sealed key, for her settings.json's mods block: its signature, or null with no key.
        internal static string Mark(string text)
        {
            lock (gate)
            {
                byte[] key = Seal();
                return key == null ? null : Sign(text ?? "", key);
            }
        }

        // Whether text carries this PC's signature. With no key to check it by, nothing counts as signed here.
        internal static bool Marked(string text, string signature)
        {
            if (string.IsNullOrEmpty(signature)) return false;
            lock (gate)
            {
                byte[] key = Seal();
                return key != null && string.Equals(signature, Sign(text ?? "", key), StringComparison.Ordinal);
            }
        }
        private static string readPath;
        private static DateTime readAt;
        private static readonly Dictionary<string, int> cells = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, List<string>> sites = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        private static string Root
        {
            get
            {
                string settings = Settings;
                if (string.IsNullOrEmpty(settings) && !string.IsNullOrEmpty(Personalities.Folder)) settings = System.IO.Path.GetDirectoryName(Personalities.Folder);
                return string.IsNullOrEmpty(settings) ? null : settings;
            }
        }

        private static string Side { get { return SecondSide() ? Personalities.SenpaiName : Personalities.PlainName; } }

        internal static string Folder { get { string root = Root; return root == null ? null : System.IO.Path.Combine(root, "keys", FolderName); } }

        internal static string Path { get { string folder = Folder; return folder == null ? null : System.IO.Path.Combine(folder, Side + ".txt"); } }

        private static string OldPath { get { string root = Root; return root == null ? null : System.IO.Path.Combine(root, Side, FileName); } }

        // The signing key: made once, kept sealed for this Windows user beside the files. One that can't be opened is made
        // again, which leaves every file signed with the old one unused. Null where sealing isn't there: nothing is signed.
        private static byte[] Seal() { return SealAt(Folder); }

        // The folder whose key signs a file in her mods folder (approved.txt, plugins-on.txt): personalSettings\keys\overrides
        // in the same data folder as that mods folder, so the file and its key always go together.
        private static string KeysFor(string path)
        {
            string mods = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
            string data = System.IO.Path.GetDirectoryName(mods);
            return data == null ? null : System.IO.Path.Combine(data, "personalSettings", "keys", FolderName);
        }

        private static byte[] SealAt(string folder)
        {
            if (folder == null) return null;
            byte[] seal;
            if (seals.TryGetValue(folder, out seal)) return seal;
            string kept = System.IO.Path.Combine(folder, SealName);
            try
            {
                string opened = File.Exists(kept) ? Voice.Unprotect(File.ReadAllText(kept).Trim()) : null;
                byte[] key = null;
                if (opened != null) try { key = Convert.FromBase64String(opened); } catch (FormatException) { key = null; }
                if (key == null || key.Length != 32)
                {
                    key = new byte[32];
                    using (RandomNumberGenerator random = RandomNumberGenerator.Create()) random.GetBytes(key);
                    Gate(folder);
                    File.WriteAllText(kept, Voice.Protect(Convert.ToBase64String(key)));
                }
                seals[folder] = key;
                return key;
            }
            catch (PlatformNotSupportedException) { return null; }
            catch (IOException caughtOne) { Brain.Caught(caughtOne); return null; }
            catch (UnauthorizedAccessException caughtOne) { Brain.Caught(caughtOne); return null; }
        }

        private static void Gate(string folder)
        {
            try { McpSignIn.Gate(folder); }
            catch (PlatformNotSupportedException) { Directory.CreateDirectory(folder); }
            catch (UnauthorizedAccessException) { Directory.CreateDirectory(folder); }
            catch (InvalidOperationException) { Directory.CreateDirectory(folder); }
        }

        private static string Sign(string body, byte[] key)
        {
            using (HMACSHA256 mac = new HMACSHA256(key)) return BitConverter.ToString(mac.ComputeHash(Encoding.UTF8.GetBytes(body))).Replace("-", "").ToLowerInvariant();
        }

        // The lines of a file and whether its signature is good. A file with no key to check it by counts as good.
        private static List<string> Read(string path, out bool good) { return Read(path, Seal(), out good); }

        private static List<string> Read(string path, byte[] key, out bool good)
        {
            List<string> lines = new List<string>(File.ReadAllLines(path));
            if (key == null) { good = true; return lines; }
            int last = lines.Count - 1;
            while (last >= 0 && lines[last].Length == 0) last--;
            good = last >= 0 && lines[last].StartsWith(Signed, StringComparison.Ordinal);
            if (!good) return lines;
            string said = lines[last].Substring(Signed.Length).Trim();
            lines.RemoveRange(last, lines.Count - last);
            good = string.Equals(said, Sign(string.Join("\n", lines.ToArray()), key), StringComparison.Ordinal);
            return lines;
        }

        private static void Parse(List<string> lines)
        {
            foreach (string line in lines)
            {
                string[] parts = line.Split('\t');
                int level, how;
                if (parts.Length == 5 && (parts[0] == Tool || parts[0] == Site || parts[0] == Run) && int.TryParse(parts[3], out level) && (how = Array.IndexOf(Words, parts[4])) > Follows)
                    cells[Key(parts[1], parts[0], parts[2], level)] = how;
                else if (parts.Length == 3 && parts[0] == "website") Seen(parts[1], parts[2]);
            }
        }

        private static string Key(string id, string kind, string name, int level) { return id + "\t" + kind + "\t" + name + "\t" + level; }

        // Read again when the side changes or the file was changed on disk.
        private static void Fresh()
        {
            string path = Path;
            Moved(path);
            DateTime at = DateTime.MinValue;
            try { if (path != null && File.Exists(path)) at = File.GetLastWriteTimeUtc(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            if (path == readPath && at == readAt) return;
            readPath = path;
            readAt = at;
            cells.Clear();
            sites.Clear();
            if (at == DateTime.MinValue) return;
            try
            {
                bool good;
                List<string> lines = Read(path, out good);
                if (good) { Parse(lines); return; }
                // Changed outside her cog: none of it is used, and a clean file takes its place.
                string said = "Your mod and plug-in security overrides were changed outside Capri, so she isn't using them: every one is back to /, following SECURITY. Set them again with each row's cog in Mods.";
                Brain.Log("PROBLEM", -1, "security overrides: " + path + " was changed outside Capri, so every override is back to /.");
                Save();
                try { Tampered(said); } catch (Exception caughtOne) { Brain.Caught(caughtOne); }
            }
            catch (IOException caughtOne) { Brain.Caught(caughtOne); }
            catch (UnauthorizedAccessException caughtOne) { Brain.Caught(caughtOne); }
        }

        // A side's file from before signing: read once as it was, signed into the new place, and removed.
        private static void Moved(string path)
        {
            string old = OldPath;
            if (path == null || old == null || File.Exists(path) || !File.Exists(old)) return;
            try
            {
                cells.Clear();
                sites.Clear();
                Parse(new List<string>(File.ReadAllLines(old)));
                readPath = path;
                Save();
                File.Delete(old);
                readPath = null;
            }
            catch (IOException caughtOne) { Brain.Caught(caughtOne); }
            catch (UnauthorizedAccessException caughtOne) { Brain.Caught(caughtOne); }
        }

        private static bool Seen(string id, string host)
        {
            List<string> known;
            if (!sites.TryGetValue(id, out known)) sites[id] = known = new List<string>();
            if (known.Exists(one => string.Equals(one, host, StringComparison.OrdinalIgnoreCase))) return false;
            known.Add(host);
            return true;
        }

        private static void Save()
        {
            string path = Path;
            if (path == null) return;
            List<string> lines = new List<string> { "# Each mod's and plug-in's security overrides, from its cog in Mods > INSTALLED." };
            foreach (KeyValuePair<string, List<string>> pair in sites)
                foreach (string host in pair.Value) lines.Add("website\t" + pair.Key + "\t" + host);
            List<string> keys = new List<string>(cells.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string key in keys)
            {
                string[] parts = key.Split('\t');
                lines.Add(parts[1] + "\t" + parts[0] + "\t" + parts[2] + "\t" + parts[3] + "\t" + Words[cells[key]]);
            }
            try
            {
                Gate(System.IO.Path.GetDirectoryName(path));
                byte[] key = Seal();
                if (key != null) lines.Add(Signed + Sign(string.Join("\n", lines.ToArray()), key));
                File.WriteAllText(path, string.Join("\r\n", lines.ToArray()) + "\r\n", new UTF8Encoding(false));
                readPath = path;
                readAt = File.GetLastWriteTimeUtc(path);
                // In her settings.json's mods block, so it is written with her other settings (on close, before an update).
                SettingsFile.Changed();
            }
            catch (IOException caughtOne) { Brain.Caught(caughtOne); }
            catch (UnauthorizedAccessException caughtOne) { Brain.Caught(caughtOne); }
        }

        // One cell: a tool or website of a plug-in at one SECURITY level.
        internal static int Of(string id, string kind, string name, int level)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name)) return Follows;
            lock (gate)
            {
                Fresh();
                int how;
                return cells.TryGetValue(Key(id, kind, name, level), out how) ? how : Follows;
            }
        }

        // The names of this kind with any cell set for this id: a mod's tools kept while it isn't running to list them.
        internal static List<string> Named(string id, string kind)
        {
            List<string> named = new List<string>();
            if (string.IsNullOrEmpty(id)) return named;
            lock (gate)
            {
                Fresh();
                foreach (string key in cells.Keys)
                {
                    string[] parts = key.Split('\t');
                    if (parts.Length == 4 && parts[0] == id && parts[1] == kind && !named.Contains(parts[2])) named.Add(parts[2]);
                }
            }
            named.Sort(StringComparer.Ordinal);
            return named;
        }

        // The cell for the level SECURITY is at now.
        internal static int Now(string id, string kind, string name) { return Of(id, kind, name, Security.Preset); }

        internal static void Set(string id, string kind, string name, int level, int how)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name) || how < Follows || how > Denied) return;
            lock (gate)
            {
                Fresh();
                if (how == Follows) cells.Remove(Key(id, kind, name, level)); else cells[Key(id, kind, name, level)] = how;
                Save();
            }
        }

        // One click: follows, then allowed, then denied, then back.
        internal static int Cycle(string id, string kind, string name, int level)
        {
            int next = (Of(id, kind, name, level) + 1) % 3;
            Set(id, kind, name, level, next);
            return next;
        }

        // Whether anything of this plug-in is allowed at this level: in Lockdown, what lets it run at all.
        internal static bool AnyAllowed(string id, int level)
        {
            lock (gate)
            {
                Fresh();
                foreach (KeyValuePair<string, int> pair in cells)
                    if (pair.Value == Allowed && pair.Key.StartsWith(id + "\t", StringComparison.Ordinal) && pair.Key.EndsWith("\t" + level, StringComparison.Ordinal)) return true;
                return false;
            }
        }

        // The websites a plug-in is known to call: its online servers' and each one first called since, kept per side.
        internal static List<string> Websites(string id)
        {
            lock (gate)
            {
                Fresh();
                List<string> known;
                return sites.TryGetValue(id ?? "", out known) ? new List<string>(known) : new List<string>();
            }
        }

        // A website this plug-in reached for: listed in its cog from now on.
        internal static void Called(string id, string host)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(host)) return;
            lock (gate)
            {
                Fresh();
                if (Seen(id, host.ToLowerInvariant())) Save();
            }
        }

        // Other files only her windows write (approved.txt, plugins-on.txt), good when the last line signs the rest.
        // One never signed here counts once and is signed then; after that changed or unsigned isn't good. signed.txt
        // lists them.
        internal const string SignedList = "signed.txt";

        internal static List<string> ReadSigned(string path, out bool good) { return ReadSigned(path, out good, true); }

        // takeOnce false: a file never signed here is not taken either, for a file that only ever came from a signing build.
        internal static List<string> ReadSigned(string path, out bool good, bool takeOnce)
        {
            lock (gate)
            {
                good = true;
                if (path == null || !File.Exists(path)) return new List<string>();
                List<string> lines = Read(path, SealAt(KeysFor(path)), out good);
                if (good) return lines;
                if (takeOnce && !SignedBefore(path))
                {
                    good = true;
                    WriteSigned(path, lines);
                }
                return lines;
            }
        }

        internal static void WriteSigned(string path, List<string> lines)
        {
            lock (gate)
            {
                List<string> written = new List<string>(lines);
                written.RemoveAll(line => line.StartsWith(Signed, StringComparison.Ordinal));
                byte[] key = SealAt(KeysFor(path));
                if (key != null) written.Add(Signed + Sign(string.Join("\n", written.ToArray()), key));
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                File.WriteAllText(path, string.Join("\r\n", written.ToArray()) + "\r\n", new UTF8Encoding(false));
                if (key != null) MarkSigned(path);
            }
        }

        private static string SignedListPath(string path) { string folder = KeysFor(path); return folder == null ? null : System.IO.Path.Combine(folder, SignedList); }

        private static bool SignedBefore(string path)
        {
            string list = SignedListPath(path);
            if (list == null || !File.Exists(list)) return false;
            string full = System.IO.Path.GetFullPath(path);
            foreach (string line in File.ReadAllLines(list)) if (string.Equals(line.Trim(), full, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static void MarkSigned(string path)
        {
            string list = SignedListPath(path);
            if (list == null || SignedBefore(path)) return;
            Gate(System.IO.Path.GetDirectoryName(list));
            File.AppendAllText(list, System.IO.Path.GetFullPath(path) + "\r\n");
        }

        // A signed file changed outside her: a PROBLEM line and her warning box, saying what goes back to asking.
        internal static void Refused(string path, string said)
        {
            Brain.Log("PROBLEM", -1, path + " was changed outside Capri, so it isn't used.");
            try { Tampered(said); } catch (Exception caughtOne) { Brain.Caught(caughtOne); }
        }

        // This side's overrides as text, for her settings.json, and that text put back and signed again, for an import they
        // chose. The one way in from outside her cog; the text has no signature of its own.
        internal static string Snapshot()
        {
            lock (gate)
            {
                Fresh();
                string path = Path;
                if (path == null || !File.Exists(path)) return "";
                bool good;
                List<string> lines = Read(path, out good);
                return good ? string.Join("\n", lines.ToArray()) : "";
            }
        }

        internal static void Restore(string text)
        {
            lock (gate)
            {
                Fresh();
                cells.Clear();
                sites.Clear();
                List<string> lines = new List<string>((text ?? "").Replace("\r\n", "\n").Split('\n'));
                lines.RemoveAll(line => line.StartsWith(Signed, StringComparison.Ordinal));
                Parse(lines);
                Save();
            }
        }

        // For the check: the key forgotten, so the next read opens it again from its sealed file.
        internal static void Reseal() { lock (gate) { seals.Clear(); readPath = null; } }

        // A plug-in taken out: its overrides go from this side's file. The other side's are left for that side.
        internal static void Forget(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            lock (gate)
            {
                Fresh();
                bool any = sites.Remove(id);
                foreach (string key in new List<string>(cells.Keys))
                    if (key.StartsWith(id + "\t", StringComparison.Ordinal)) { cells.Remove(key); any = true; }
                if (any) Save();
            }
        }
    }
}
