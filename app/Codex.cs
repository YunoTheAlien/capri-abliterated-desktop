using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
#if CROSS
using Companion = CapriDesktop.Pet;
#endif

namespace CapriDesktop
{
    // Her codex: a file of named text sections, one for each hook below, fetched from the release and read in
    // place. Deflated and sealed with the remote link's envelope. Written by a build that has the sections
    // attached, read by any build.
    internal static class Codex
    {
        // Named for the release that carries it, so a file says which version it goes with: capri-codex-0.9.433.pack,
        // capri-codex-0.9.432test.pack. That release is Tag, or this build's own version while Tag is empty.
        internal static string Name { get { return NameFor(string.IsNullOrEmpty(CodexSum.Tag) ? Companion.Built : CodexSum.Tag); } }
        internal static string NameFor(string version) { return "capri-codex-" + version + ".pack"; }
        // Every codex file, this build's and older ones (capri-codex-1.pack was the name before versions).
        internal const string Every = "capri-codex-*.pack";
        // In the release for this version, beside the programs, so a build only ever fetches the one its hash
        // names.
        internal static string Url { get { return ReleaseFile.Address(Name, CodexSum.Tag); } }
        private const string Word = "senpai";
        private const string Label = "capri-codex/1";
        private const byte Kind = 0x43, Way = 0x01;
        private const int SaltBytes = 32;

        // Where it is kept, set when her data folder is known.
        internal static string Folder;
        internal static bool Attached;

        internal static string PathIn(string data) { return string.IsNullOrEmpty(data) ? null : System.IO.Path.Combine(data, Name); }

        // ---------- Writing, on a build that has the sections attached ----------

        // The sealed file. Zero when nothing is attached. The salt and IV come from the content, so the same
        // sections always make the same file and its hash only moves when they do.
        internal static int Write(string path)
        {
            Dictionary<string, string> s = Gather();
            if (s.Count == 0) return 0;
            File.WriteAllBytes(path, Sealed(s));
            return s.Count;
        }

        // The named texts sealed, so nothing in the file reads as text to anybody opening it.
        internal static byte[] Sealed(Dictionary<string, string> s)
        {
            byte[] plain;
            using (MemoryStream m = new MemoryStream())
            {
                using (BinaryWriter w = new BinaryWriter(m, new UTF8Encoding(false), true))
                {
                    List<string> keys = new List<string>(s.Keys);
                    keys.Sort(StringComparer.Ordinal);
                    w.Write(1);
                    w.Write(keys.Count);
                    foreach (string k in keys) { w.Write(k); w.Write(s[k] ?? ""); }
                }
                plain = Deflate(m.ToArray());
            }
            byte[] salt = Cut(RemoteCrypto.Sha256(RemoteCrypto.Join(plain, RemoteCrypto.Ascii("salt"))), SaltBytes);
            byte[] iv = Cut(RemoteCrypto.Sha256(RemoteCrypto.Join(plain, RemoteCrypto.Ascii("iv"))), RemoteCrypto.IvBytes);
            return RemoteCrypto.Join(salt, RemoteCrypto.Seal(Keys(salt), Kind, Way, 0, plain, iv));
        }

        private static Dictionary<string, string> Gather()
        {
            Dictionary<string, string> s = new Dictionary<string, string>(StringComparer.Ordinal);
            if (Personalities.SenpaiFor == null) return s;
            string[] order = Call(Personalities.OrderFor);
            string[] moods = Call(Dynamics.SenpaiNamesFor);
            string[] shipped = Dynamics.ShippedNames();
            s["order"] = string.Join("\n", order);
            s["moods"] = string.Join("\n", moods);
            foreach (string one in order) Put(s, "pack/" + one, Ask(Personalities.SenpaiFor, one));
            foreach (string one in moods) Put(s, "mood/" + one, Ask(Dynamics.SenpaiMoodFor, one));
            foreach (string one in shipped) Put(s, "part/" + one, Ask(Dynamics.SenpaiPartFor, one));
            List<string> off = new List<string>();
            if (Dynamics.OffByDefaultFor != null)
                foreach (string one in Both(shipped, moods))
                {
                    try { if (Dynamics.OffByDefaultFor(one)) off.Add(one); }
                    catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                }
            s["off"] = string.Join("\n", off.ToArray());
            if (Dynamics.SenpaiRetunedFor != null)
            {
                string[,] rows = null;
                try { rows = Dynamics.SenpaiRetunedFor(); }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                if (rows != null) s["retune"] = Grid(rows);
            }
            Put(s, "body", Ask(Interests.BodyFor));
            Put(s, "head", Ask(Interests.HeadFor));
            Put(s, "was", Ask(Interests.WasNameFor));
            // The paces, the old names of her two interests files and a story mood for each of the moods above, which
            // the side attaches beside the rest and which a build with only this pack had none of.
            s["paces"] = Grid(Dynamics.StockPaces());
            Put(s, "washers", Ask(Interests.WasHersFor));
            Put(s, "wasyours", Ask(Interests.WasYoursFor));
            if (Lore.StoryMoodFor != null)
            {
                List<string> stories = new List<string>();
                foreach (string one in moods)
                {
                    string story = null;
                    try { story = Lore.StoryMoodFor(one); }
                    catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                    if (!string.IsNullOrEmpty(story)) stories.Add(one + "\t" + story);
                }
                s["storymood"] = string.Join("\n", stories.ToArray());
            }
            s["more"] = string.Join("\n", Interests.SenpaiMore ?? new string[0]);
            if (Sayings.SenpaiSectionFor != null)
                for (int i = 0; i < 4; i++)
                {
                    try { Put(s, "lines/" + i, Sayings.SenpaiSectionFor(i)); }
                    catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                }
            Put(s, "touch", Touch.SenpaiTouch);
            Put(s, "extended", Extended.Text);
            if (Personalities.SenpaiAdded != null) s["added"] = Grid(Personalities.SenpaiAdded);
            Put(s, "angles", Angles.SenpaiAngles);
            if (VoiceProfiles.SenpaiVoice != null)
                foreach (string one in Both(order, moods))
                {
                    VoiceProfile v = null;
                    try { v = VoiceProfiles.SenpaiVoice(one); }
                    catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                    if (v != null) s["voice/" + one] = v.Pitch + "," + v.Speed + "," + v.Volume + "," + v.Robot + "," + v.Glitch;
                }
            if (Personalities.RepairData != null && Personalities.RepairData.Length == 3) s["repair"] = string.Join("\n", Personalities.RepairData);
            // Her gated labels in each language, a table each.
            Dictionary<string, string> gui = Gui.CodexTables;
            if (gui != null) foreach (KeyValuePair<string, string> one in gui) Put(s, "gui/" + one.Key, one.Value);
            return s;
        }

        // ---------- Reading, on any build ----------

        // Reads the codex beside her and sets every hook from it. False when it is missing, does not match this
        // build, or cannot be read.
        internal static bool Load(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            if (!string.IsNullOrEmpty(CodexSum.Sha) && !Fine(path))
            {
                Brain.Log("files", -1, "Her codex does not match this build, so it is fetched again.");
                return false;
            }
            Dictionary<string, string> s = null;
            try { s = Sections(Opened(File.ReadAllBytes(path))); }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            if (s == null)
            {
                Brain.Log("files", -1, "Her codex could not be read, so it was not used.");
                return false;
            }
            Install(s);
            Brain.Log("files", -1, "Her codex: " + s.Count + " sections read.");
            return true;
        }

        private static void Install(Dictionary<string, string> s)
        {
            string[] order = Lines(Get(s, "order"));
            string[] moods = Lines(Get(s, "moods"));
            HashSet<string> off = new HashSet<string>(Lines(Get(s, "off")), StringComparer.OrdinalIgnoreCase);
            Personalities.OrderFor = delegate { return order; };
            Personalities.SenpaiFor = delegate(string named) { return Get(s, "pack/" + named); };
            Dynamics.SenpaiNamesFor = delegate { return moods; };
            Dynamics.SenpaiMoodFor = delegate(string named) { return Get(s, "mood/" + named); };
            Dynamics.SenpaiPartFor = delegate(string named) { return Get(s, "part/" + named); };
            Dynamics.OffByDefaultFor = delegate(string named) { return off.Contains(named ?? ""); };
            string retune = Get(s, "retune");
            if (retune != null)
            {
                string[,] rows = Grid(retune);
                Dynamics.SenpaiRetunedFor = delegate { return rows; };
            }
            string body = Get(s, "body"), head = Get(s, "head"), was = Get(s, "was");
            if (body != null) Interests.BodyFor = delegate { return body; };
            if (head != null) Interests.HeadFor = delegate { return head; };
            if (was != null) Interests.WasNameFor = delegate { return was; };
            string[,] paces = Grid(Get(s, "paces"));
            for (int row = 0; row < paces.GetLength(0); row++)
            {
                double pace;
                if (double.TryParse(paces[row, 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out pace))
                    Dynamics.StockPace(paces[row, 0], pace);
            }
            string wasHers = Get(s, "washers"), wasYours = Get(s, "wasyours");
            if (wasHers != null) Interests.WasHersFor = delegate { return wasHers; };
            if (wasYours != null) Interests.WasYoursFor = delegate { return wasYours; };
            Dictionary<string, string> stories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string one in Lines(Get(s, "storymood")))
            {
                int tab = one.IndexOf('\t');
                if (tab > 0) stories[one.Substring(0, tab)] = one.Substring(tab + 1);
            }
            if (stories.Count > 0) Lore.StoryMoodFor = delegate(string named) { string story; return stories.TryGetValue(named ?? "", out story) ? story : null; };
            Interests.SenpaiMore = Lines(Get(s, "more"));
            Sayings.SenpaiSectionFor = delegate(int which) { return Get(s, "lines/" + which) ?? ""; };
            Touch.SenpaiTouch = Get(s, "touch") ?? "";
            Extended.Text = Get(s, "extended") ?? "";
            string added = Get(s, "added");
            if (added != null) Personalities.SenpaiAdded = Grid(added);
            Angles.SenpaiAngles = Get(s, "angles") ?? "";
            VoiceProfiles.SenpaiVoice = delegate(string named) { return Voice(Get(s, "voice/" + named)); };
            string[] repair = Lines(Get(s, "repair"));
            if (repair.Length == 3)
            {
                Personalities.RepairData = repair;
                Personalities.RenamedFor = delegate { Mend(repair); };
                Personalities.RenamedToFor = delegate(string chosen) { return MendedName(repair, chosen); };
            }
            Dictionary<string, string> gui = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> one in s) if (one.Key.StartsWith("gui/", StringComparison.Ordinal)) gui[one.Key.Substring(4)] = one.Value;
            Gui.CodexTables = gui.Count == 0 ? null : gui;
            Gui.Forget();
            Attached = true;
        }

        // For her checks: a sealed file opened back into its sections, without installing anything.
        internal static Dictionary<string, string> Unsealed(byte[] file) { return Sections(Opened(file)); }

        private static Dictionary<string, string> Sections(byte[] raw)
        {
            if (raw == null) return null;
            Dictionary<string, string> s = new Dictionary<string, string>(StringComparer.Ordinal);
            using (BinaryReader r = new BinaryReader(new MemoryStream(raw), new UTF8Encoding(false)))
            {
                if (r.ReadInt32() != 1) return null;
                int count = r.ReadInt32();
                if (count < 0 || count > 100000) return null;
                for (int i = 0; i < count; i++)
                {
                    string key = r.ReadString();
                    s[key] = r.ReadString();
                }
            }
            return s;
        }

        // ---------- The seal ----------

        private static RemoteKey Keys(byte[] salt)
        {
            byte[] okm = RemoteCrypto.Hkdf(RemoteCrypto.Ascii(Word), salt, RemoteCrypto.Ascii(Label), 64);
            return new RemoteKey(Cut(okm, 32), Tail(okm, 32));
        }

        // Salt, then the envelope. Null when the tag does not check or anything after it fails.
        private static byte[] Opened(byte[] file)
        {
            if (file == null || file.Length <= SaltBytes) return null;
            byte[] salt = Cut(file, SaltBytes);
            string reason;
            byte[] plain = RemoteCrypto.Open(Keys(salt), Kind, Way, 0, file, SaltBytes, file.Length - SaltBytes, out reason);
            return plain == null ? null : Inflate(plain);
        }

        private static byte[] Deflate(byte[] data)
        {
            using (MemoryStream m = new MemoryStream())
            {
                using (DeflateStream d = new DeflateStream(m, CompressionLevel.Optimal, true)) d.Write(data, 0, data.Length);
                return m.ToArray();
            }
        }

        private static byte[] Inflate(byte[] data)
        {
            using (DeflateStream d = new DeflateStream(new MemoryStream(data), CompressionMode.Decompress))
            using (MemoryStream plain = new MemoryStream())
            {
                d.CopyTo(plain);
                return plain.ToArray();
            }
        }

        private static byte[] Cut(byte[] from, int count)
        {
            byte[] made = new byte[count];
            Buffer.BlockCopy(from, 0, made, 0, count);
            return made;
        }

        private static byte[] Tail(byte[] from, int at)
        {
            byte[] made = new byte[from.Length - at];
            Buffer.BlockCopy(from, at, made, 0, made.Length);
            return made;
        }

        // ---------- Fetching ----------

        internal static string Fetch(string data)
        {
            Remove(data, Name);
            return ReleaseFile.Fetch(Name, CodexSum.Sha, CodexSum.Size, PathIn(data), CodexSum.Tag);
        }

        // Every codex file in her folder but the one named keep (null for all): an older version's is never read
        // again, and switched off with cleanup, none stays on disk.
        internal static void Remove(string data, string keep)
        {
            if (string.IsNullOrEmpty(data) || !Directory.Exists(data)) return;
            foreach (string one in Directory.GetFiles(data, Every))
                if (keep == null || !string.Equals(System.IO.Path.GetFileName(one), keep, StringComparison.OrdinalIgnoreCase))
                    try { File.Delete(one); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        internal static bool Fine(string path) { return ReleaseFile.Matches(path, CodexSum.Sha, CodexSum.Size); }

        // ---------- A pack that shipped under the wrong name ----------

        // The plain file is moved to the name that matches what it says, and the second-folder file whose title
        // is the wrong one gets the right one. Only a file carrying the marker line is touched.
        internal static void Mend(string[] data)
        {
            if (data == null || data.Length != 3) return;
            string from = data[0], to = data[1], mark = data[2];
            try
            {
                string was = Personalities.PlainPathOf(from), now = Personalities.PlainPathOf(to);
                if (was != null && now != null && File.Exists(was) && !File.Exists(now)
                    && File.ReadAllText(was).IndexOf(mark, StringComparison.Ordinal) >= 0)
                {
                    File.Move(was, now);
                    Personalities.PackRenamed = true;
                }
                string folder = Personalities.Senpais;
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
                foreach (string file in Directory.GetFiles(folder))
                {
                    if (!string.Equals(Slots.Titled(file), from, StringComparison.OrdinalIgnoreCase)) continue;
                    string text = File.ReadAllText(file);
                    if (text.IndexOf(mark, StringComparison.Ordinal) < 0) continue;
                    File.WriteAllText(file, Regex.Replace(text, @"^[ \t]*#[ \t]*" + Regex.Escape(from) + @"[ \t\r]*$", "# " + to, RegexOptions.Multiline), new UTF8Encoding(false));
                    Personalities.PackRenamed = true;
                    return;
                }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        internal static string MendedName(string[] data, string chosen)
        {
            if (data == null || data.Length != 3) return null;
            return string.Equals(chosen, data[0], StringComparison.OrdinalIgnoreCase) ? data[1] : null;
        }

        // ---------- Small parts ----------

        private static string Get(Dictionary<string, string> s, string key)
        {
            string found;
            return s.TryGetValue(key, out found) ? found : null;
        }

        private static void Put(Dictionary<string, string> s, string key, string value)
        {
            if (!string.IsNullOrEmpty(value)) s[key] = value;
        }

        private static string Ask(Func<string, string> hook, string named)
        {
            if (hook == null) return null;
            try { return hook(named); }
            catch (Exception) { return null; }
        }

        private static string Ask(Func<string> hook)
        {
            if (hook == null) return null;
            try { return hook(); }
            catch (Exception) { return null; }
        }

        private static string[] Call(Func<string[]> hook)
        {
            if (hook == null) return new string[0];
            try { return hook() ?? new string[0]; }
            catch (Exception) { return new string[0]; }
        }

        private static IEnumerable<string> Both(string[] one, string[] two)
        {
            foreach (string a in one) yield return a;
            foreach (string b in two) yield return b;
        }

        private static string[] Lines(string text)
        {
            if (string.IsNullOrEmpty(text)) return new string[0];
            List<string> kept = new List<string>();
            foreach (string one in text.Split('\n')) if (one.Length > 0) kept.Add(one);
            return kept.ToArray();
        }

        private static string Grid(string[,] rows)
        {
            StringBuilder made = new StringBuilder();
            int count = rows.GetLength(0), width = rows.GetLength(1);
            made.Append(width);
            for (int i = 0; i < count; i++)
            {
                made.Append('\n');
                for (int j = 0; j < width; j++)
                {
                    if (j > 0) made.Append('\t');
                    made.Append(rows[i, j] ?? "");
                }
            }
            return made.ToString();
        }

        private static string[,] Grid(string text)
        {
            string[] lines = Lines(text);
            int width;
            if (lines.Length == 0 || !int.TryParse(lines[0], out width) || width <= 0) return new string[0, 0];
            string[,] rows = new string[lines.Length - 1, width];
            for (int i = 1; i < lines.Length; i++)
            {
                string[] cells = lines[i].Split('\t');
                for (int j = 0; j < width; j++) rows[i - 1, j] = j < cells.Length ? cells[j] : "";
            }
            return rows;
        }

        private static VoiceProfile Voice(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string[] parts = text.Split(',');
            int[] n = new int[5];
            if (parts.Length != 5) return null;
            for (int i = 0; i < 5; i++) if (!int.TryParse(parts[i], out n[i])) return null;
            return new VoiceProfile(n[0], n[1], n[2], n[3], n[4]);
        }
    }

    // One file from the release that carries it, kept only when size and hash match this build's, for her codex and
    // stories. The tag is the release it first shipped on, so an unchanged file uploads once; empty is this build's.
    internal static class ReleaseFile
    {
        internal static string Address(string name, string tag)
        {
            return "https://github.com/YunoTheAlien/capri-abliterated-desktop/releases/download/v" + Own(string.IsNullOrEmpty(tag) ? Companion.Built : tag) + "/" + name;
        }

        // Her own release is tagged with the numbers of her version only, so 0.9.427pi fetches from v0.9.427: a word in
        // the tag would hide the release from older builds' update check.
        internal static string Own(string built)
        {
            string numbers = System.Text.RegularExpressions.Regex.Match(built ?? "", @"^\d+(?:\.\d+){1,3}").Value;
            return numbers.Length > 0 ? numbers : built;
        }

        // Null when it is in place, otherwise why not.
        internal static string Fetch(string name, string sha, long size, string path, string tag)
        {
            if (string.IsNullOrEmpty(path)) return "no folder to put it in";
            if (Matches(path, sha, size)) return null;
            if (string.IsNullOrEmpty(sha) || size <= 0) return "this build was made without one";
            string part = path + ".part";
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Address(name, tag));
                request.UserAgent = "Capri";
                request.Timeout = 30000;
                request.ReadWriteTimeout = 60000;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream body = response.GetResponseStream())
                using (FileStream file = new FileStream(part, FileMode.Create, FileAccess.Write))
                {
                    byte[] buffer = new byte[1 << 16];
                    long have = 0;
                    int read;
                    while ((read = body.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        have += read;
                        if (have > size) return "it was larger than it should be";
                        file.Write(buffer, 0, read);
                    }
                }
                if (!Matches(part, sha, size)) return "it did not match";
                if (File.Exists(path)) File.Delete(path);
                File.Move(part, path);
                return null;
            }
            catch (WebException bad) { return bad.Message; }
            catch (IOException bad) { return bad.Message; }
            catch (UnauthorizedAccessException bad) { return bad.Message; }
            finally
            {
                try { if (File.Exists(part)) File.Delete(part); }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
        }

        internal static bool Matches(string path, string sha, long size)
        {
            try
            {
                if (string.IsNullOrEmpty(sha) || !File.Exists(path) || new FileInfo(path).Length != size) return false;
                using (FileStream stream = File.OpenRead(path))
                using (SHA256 hash = SHA256.Create())
                    return string.Equals(BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", ""), sha, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }
    }
}
