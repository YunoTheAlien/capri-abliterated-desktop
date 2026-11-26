using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CapriDesktop
{
    // The Planner: StoryBoard's editor served by her on 127.0.0.1 only, a token in its address, and the solo part of
    // StoryBoard's own server: the project and its revision, backups, uploads, the bin, docs and the log.
    internal sealed class PlannerHost : IDisposable
    {
        internal const string FolderName = "Planner";
        internal const int UploadLimit = 300 * 1024 * 1024;
        internal const int BackupEvery = 300, BackupKeep = 40;
        private const string Cookie = "capri_planner";
        private const string DocMarker = "<!-- storyboard board=";
        private static readonly string[] Images = { ".png", ".jpg", ".jpeg", ".webp", ".gif" }, Audio = { ".mp3", ".ogg", ".wav", ".m4a", ".flac", ".opus" }, Fonts = { ".woff2", ".woff", ".ttf", ".otf" };
        private static readonly Dictionary<string, string> Types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ".html", "text/html; charset=utf-8" }, { ".js", "text/javascript; charset=utf-8" }, { ".css", "text/css; charset=utf-8" },
            { ".json", "application/json; charset=utf-8" }, { ".svg", "image/svg+xml" }, { ".png", "image/png" }, { ".jpg", "image/jpeg" },
            { ".jpeg", "image/jpeg" }, { ".webp", "image/webp" }, { ".gif", "image/gif" }, { ".mp3", "audio/mpeg" }, { ".ogg", "audio/ogg" },
            { ".opus", "audio/ogg" }, { ".wav", "audio/wav" }, { ".m4a", "audio/mp4" }, { ".flac", "audio/flac" }, { ".woff2", "font/woff2" },
            { ".woff", "font/woff" }, { ".ttf", "font/ttf" }, { ".otf", "font/otf" }
        };

        private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        private readonly object gate = new object(), saving = new object();
        private readonly string app, root, data, assets, bin, backups, project;
        private readonly string session = Guid.NewGuid().ToString("N").Substring(0, 8);
        private int revision;
        private DateTime lastBackup = DateTime.MinValue;
        private volatile bool stopped;
        internal string Token { get; private set; }
        internal int Port { get; private set; }
        // Her log, for the editor's own lines; checks swap it.
        internal Action<string> Logged = delegate(string line) { Brain.Log("planner", -1, line); };
        internal Func<DateTime> Clock = delegate { return DateTime.UtcNow; };
        // Each request as it comes ("GET /api/project"), for the speed measure; null otherwise.
        internal Action<string> Asked;

        // app: StoryBoard's app folder. folder: her Planner folder, the project under it in project\.
        internal PlannerHost(string app, string folder)
        {
            this.app = Path.GetFullPath(app);
            root = Path.GetFullPath(folder);
            data = Path.Combine(root, "project");
            assets = Path.Combine(data, "assets");
            bin = Path.Combine(assets, "_bin");
            backups = Path.Combine(data, "backups");
            project = Path.Combine(data, "project.json");
            byte[] secret = new byte[16];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create()) random.GetBytes(secret);
            Token = BitConverter.ToString(secret).Replace("-", "").ToLowerInvariant();
        }

        // The address the editor opens on: the token in it once, kept as a cookie for the rest.
        internal string Address { get { return "http://127.0.0.1:" + Port + "/?token=" + Token; } }

        internal void Start()
        {
            foreach (string folder in new[] { data, assets, bin, backups }) Directory.CreateDirectory(folder);
            listener.Start(16);
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            new Thread(Accept) { IsBackground = true, Name = "Capri planner" }.Start();
        }

        public void Dispose()
        {
            stopped = true;
            try { listener.Stop(); } catch (SocketException) { }
        }

        private void Accept()
        {
            while (!stopped)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); }
                catch (Exception) { return; }
                ThreadPool.QueueUserWorkItem(delegate { Serve(client); });
            }
        }

        private sealed class Ask
        {
            internal string Method, Path, Query, Cookie, Range, Host;
            internal byte[] Body = new byte[0];
            internal Dictionary<string, string> Args = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        private void Serve(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 30000;
                    NetworkStream stream = client.GetStream();
                    Ask ask = Read(stream);
                    if (ask == null) return;
                    Action<string> asked = Asked;
                    if (asked != null) asked(ask.Method + " " + ask.Path);
                    Answer(stream, ask);
                }
                catch (IOException) { }
                catch (SocketException) { }
                catch (ObjectDisposedException) { }
                catch (Exception caughtOne) { Brain.Caught(caughtOne); }
            }
        }

        // One request: its line, its headers and a body up to the upload limit.
        private static Ask Read(Stream stream)
        {
            MemoryStream head = new MemoryStream();
            int last = 0, matched = 0;
            while (matched < 4)
            {
                last = stream.ReadByte();
                if (last < 0) return null;
                head.WriteByte((byte)last);
                matched = (matched % 2 == 0 && last == '\r') || (matched % 2 == 1 && last == '\n') ? matched + 1 : last == '\r' ? 1 : 0;
                if (head.Length > 65536) return null;
            }
            string[] lines = Encoding.ASCII.GetString(head.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] first = lines[0].Split(' ');
            if (first.Length < 2) return null;
            Ask ask = new Ask { Method = first[0].ToUpperInvariant() };
            string target = first[1];
            int mark = target.IndexOf('?');
            ask.Path = Uri.UnescapeDataString(mark < 0 ? target : target.Substring(0, mark));
            ask.Query = mark < 0 ? "" : target.Substring(mark + 1);
            foreach (string pair in ask.Query.Split('&'))
            {
                int equals = pair.IndexOf('=');
                if (equals > 0) ask.Args[Uri.UnescapeDataString(pair.Substring(0, equals).Replace('+', ' '))] = Uri.UnescapeDataString(pair.Substring(equals + 1).Replace('+', ' '));
            }
            long length = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon <= 0) continue;
                string name = lines[i].Substring(0, colon).Trim().ToLowerInvariant(), value = lines[i].Substring(colon + 1).Trim();
                if (name == "content-length") long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out length);
                else if (name == "cookie") ask.Cookie = value;
                else if (name == "range") ask.Range = value;
                else if (name == "host") ask.Host = value;
            }
            if (length > UploadLimit) throw new IOException("upload too large");
            ask.Body = new byte[length];
            for (int at = 0; at < length; )
            {
                int got = stream.Read(ask.Body, at, (int)Math.Min(65536, length - at));
                if (got <= 0) return null;
                at += got;
            }
            return ask;
        }

        // Only her token opens it: in the address once, then as its cookie. A page another site points here has neither, and a
        // host name other than her own address is turned away, so a name made to point at 127.0.0.1 gets nothing.
        private bool Allowed(Ask ask)
        {
            if (ask.Host != null && ask.Host != "127.0.0.1:" + Port) return false;
            string given;
            if (ask.Args.TryGetValue("token", out given) && Same(given, Token)) return true;
            foreach (string part in (ask.Cookie ?? "").Split(';'))
            {
                string one = part.Trim();
                if (one.StartsWith(Cookie + "=", StringComparison.Ordinal) && Same(one.Substring(Cookie.Length + 1), Token)) return true;
            }
            return false;
        }

        private static bool Same(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private void Answer(Stream stream, Ask ask)
        {
            if (!Allowed(ask)) { Json(stream, 403, Map("error", "not hers")); return; }
            string extra = ask.Args.ContainsKey("token") ? "Set-Cookie: " + Cookie + "=" + Token + "; Path=/; HttpOnly; SameSite=Strict\r\n" : "";
            try
            {
                if (ask.Method == "GET") Get(stream, ask, extra);
                else if (ask.Method == "PUT") Put(stream, ask);
                else if (ask.Method == "POST") Post(stream, ask);
                else Json(stream, 405, Map("error", "not here"));
            }
            catch (FormatException error) { Json(stream, 400, Map("error", error.Message)); }
            catch (UnauthorizedAccessException error) { Json(stream, 400, Map("error", error.Message)); }
            catch (IOException error) { Json(stream, 400, Map("error", error.Message)); }
        }

        private void Get(Stream stream, Ask ask, string extra)
        {
            string path = ask.Path;
            if (path == "/api/project")
            {
                int now;
                lock (gate) now = revision;
                byte[] body = File.Exists(project) ? File.ReadAllBytes(project) : Encoding.ASCII.GetBytes("null");
                Send(stream, 200, "application/json; charset=utf-8", body, "Cache-Control: no-store\r\nX-Rev: " + now + "\r\nX-Session: " + session + "\r\n");
                return;
            }
            if (path == "/api/browse") { Browse(stream, ask.Args.ContainsKey("dir") ? ask.Args["dir"] : ""); return; }
            if (path == "/api/assets/bin")
            {
                List<object> files = new List<object>();
                foreach (string file in Sorted(Directory.GetFiles(bin))) files.Add(Map("name", Path.GetFileName(file), "size", new FileInfo(file).Length));
                Json(stream, 200, Map("files", files));
                return;
            }
            if (path == "/api/docs") { Json(stream, 200, Docs()); return; }
            if (path == "/api/doc")
            {
                string doc = DocPath(ask.Args.ContainsKey("path") ? ask.Args["path"] : "");
                if (doc == null || !File.Exists(doc)) { Json(stream, 404, Map("error", "no such markdown file")); return; }
                Json(stream, 200, Map("path", Relative(root, doc), "text", File.ReadAllText(doc, Encoding.UTF8)));
                return;
            }
            if (path.StartsWith("/assets/", StringComparison.Ordinal))
            {
                string file = Under(assets, path.Substring("/assets/".Length));
                if (file == null) { Json(stream, 403, Map("error", "outside assets")); return; }
                SendFile(stream, ask, file, true, true);
                return;
            }
            if (path == "/src")
            {
                string file = Under(root, ask.Args.ContainsKey("path") ? ask.Args["path"] : "");
                if (file == null || Kind(file) == null || Inside(data, file)) { Json(stream, 403, Map("error", "not a media file in her Planner folder")); return; }
                SendFile(stream, ask, file, true, true);
                return;
            }
            if (path.StartsWith("/api/", StringComparison.Ordinal)) { Json(stream, 404, Map("error", "not here")); return; }
            string page = Under(app, path.TrimStart('/').Length == 0 ? "index.html" : path.TrimStart('/'));
            if (page == null) { Json(stream, 403, Map("error", "outside the app")); return; }
            SendFile(stream, ask, page, false, false, extra);
        }

        private void Put(Stream stream, Ask ask)
        {
            if (ask.Path == "/api/doc")
            {
                string doc = DocPath(ask.Args.ContainsKey("path") ? ask.Args["path"] : "");
                if (doc == null) { Json(stream, 400, Map("error", "exports are .md files in her Planner folder, outside its project folder")); return; }
                string text = Encoding.UTF8.GetString(ask.Body);
                if (File.Exists(doc))
                {
                    string old = File.ReadAllText(doc, Encoding.UTF8);
                    if (old.IndexOf(DocMarker, StringComparison.Ordinal) < 0 && (!ask.Args.ContainsKey("force") || ask.Args["force"] != "1"))
                    {
                        Json(stream, 409, Map("error", Path.GetFileName(doc) + " was not written by the Planner, so it isn't overwritten"));
                        return;
                    }
                    string kept = Path.Combine(backups, "docs");
                    Directory.CreateDirectory(kept);
                    File.Copy(doc, Path.Combine(kept, Path.GetFileNameWithoutExtension(doc) + "_" + Clock().ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".md"), true);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(doc));
                File.WriteAllText(doc, text, new UTF8Encoding(false));
                Json(stream, 200, Map("ok", true, "path", Relative(root, doc)));
                return;
            }
            if (ask.Path != "/api/project") { Json(stream, 404, Map("error", "not found")); return; }
            string rev = ask.Args.ContainsKey("rev") ? ask.Args["rev"] : "";
            int based;
            bool numbered = int.TryParse(rev, NumberStyles.None, CultureInfo.InvariantCulture, out based);
            if (!numbered && File.Exists(project)) { Json(stream, 400, Map("error", "a save needs the revision it was built on, rev=<number>")); return; }
            HuskJson.Read(Encoding.UTF8.GetString(ask.Body));
            int saved;
            lock (saving)
            {
                int now;
                lock (gate) now = revision;
                if (numbered && based != now && File.Exists(project))
                {
                    Send(stream, 409, "application/json; charset=utf-8", File.ReadAllBytes(project), "Cache-Control: no-store\r\nX-Rev: " + now + "\r\nX-Session: " + session + "\r\n");
                    return;
                }
                DateTime at = Clock();
                if (File.Exists(project) && (at - lastBackup).TotalSeconds > BackupEvery)
                {
                    File.Copy(project, Path.Combine(backups, "project_" + at.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".json"), true);
                    lastBackup = at;
                    List<string> old = Sorted(Directory.GetFiles(backups, "project_*.json"));
                    for (int i = 0; i < old.Count - BackupKeep; i++) File.Delete(old[i]);
                }
                string part = project + ".tmp";
                File.WriteAllBytes(part, ask.Body);
                if (File.Exists(project)) File.Replace(part, project, null);
                else File.Move(part, project);
                lock (gate) saved = ++revision;
            }
            Json(stream, 200, Map("ok", true, "rev", saved, "saved", Clock().ToLocalTime().ToString("s", CultureInfo.InvariantCulture)));
        }

        private void Post(Stream stream, Ask ask)
        {
            if (ask.Path == "/api/upload")
            {
                string name = ask.Args.ContainsKey("name") ? ask.Args["name"] : "file";
                if (Kind(name) == null) { Json(stream, 400, Map("error", "only images, audio and fonts can be uploaded")); return; }
                string stored = Store(ask.Body, name);
                Json(stream, 200, Map("file", stored, "name", Path.GetFileNameWithoutExtension(name), "kind", Kind(name)));
                return;
            }
            Dictionary<string, object> asked = ask.Body.Length == 0 ? new Dictionary<string, object>() : HuskJson.Read(Encoding.UTF8.GetString(ask.Body)) as Dictionary<string, object> ?? new Dictionary<string, object>();
            if (ask.Path == "/api/import")
            {
                List<object> found = new List<object>();
                foreach (object one in HuskJson.List(HuskJson.Get(asked, "paths")) ?? new List<object>())
                {
                    string file = Under(root, one as string ?? "");
                    if (file == null || !File.Exists(file) || Kind(file) == null || Inside(data, file)) continue;
                    found.Add(Map("file", Store(File.ReadAllBytes(file), Path.GetFileName(file)), "name", Path.GetFileNameWithoutExtension(file), "kind", Kind(file), "from", one));
                }
                Json(stream, 200, found);
                return;
            }
            if (ask.Path == "/api/assets/clean")
            {
                HashSet<string> keep = new HashSet<string>(StringComparer.Ordinal);
                foreach (object one in HuskJson.List(HuskJson.Get(asked, "keep")) ?? new List<object>()) if (one is string) keep.Add((string)one);
                bool dry = ask.Args.ContainsKey("dry") && ask.Args["dry"] == "1";
                List<object> moved = new List<object>();
                foreach (string file in Sorted(Directory.GetFiles(assets)))
                {
                    string name = Path.GetFileName(file);
                    if (keep.Contains(name) || name.EndsWith(".part", StringComparison.Ordinal)) continue;
                    moved.Add(Map("name", name, "size", new FileInfo(file).Length));
                    if (dry) continue;
                    string target = Path.Combine(bin, name);
                    if (File.Exists(target)) File.Delete(target);
                    File.Move(file, target);
                }
                Json(stream, 200, Map("moved", moved, "dry", dry, "kept", keep.Count));
                return;
            }
            if (ask.Path == "/api/assets/restore")
            {
                List<object> back = new List<object>();
                foreach (object one in HuskJson.List(HuskJson.Get(asked, "files")) ?? new List<object>())
                {
                    string name = Path.GetFileName(one as string ?? "");
                    string from = Path.Combine(bin, name), to = Path.Combine(assets, name);
                    if (name.Length == 0 || !File.Exists(from) || File.Exists(to)) continue;
                    File.Move(from, to);
                    back.Add(name);
                }
                Json(stream, 200, Map("restored", back));
                return;
            }
            if (ask.Path == "/api/log")
            {
                Logged("[page:" + HuskJson.Text(asked, "level") + "] " + HuskJson.Text(asked, "msg"));
                Json(stream, 200, Map("ok", true));
                return;
            }
            Json(stream, 404, Map("error", "not found"));
        }

        // Saved under its name and a hash of its bytes, so the same file brought in twice is kept once.
        private string Store(byte[] bytes, string name)
        {
            string stem = Regex.Replace(Path.GetFileNameWithoutExtension(name), "[^A-Za-z0-9_-]+", "_").Trim('_');
            if (stem.Length > 60) stem = stem.Substring(0, 60);
            if (stem.Length == 0) stem = "file";
            string hash;
            using (SHA1 sha = SHA1.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant().Substring(0, 8);
            string stored = stem + "_" + hash + Path.GetExtension(name).ToLowerInvariant();
            string target = Path.Combine(assets, stored);
            if (!File.Exists(target))
            {
                File.WriteAllBytes(target + ".part", bytes);
                File.Move(target + ".part", target);
            }
            return stored;
        }

        private void Browse(Stream stream, string relative)
        {
            string folder = Under(root, relative ?? "");
            if (folder == null || !Directory.Exists(folder)) { Json(stream, 404, Map("error", "no such folder")); return; }
            List<object> dirs = new List<object>(), files = new List<object>();
            foreach (string one in Sorted(Directory.GetDirectories(folder)))
                if (!Path.GetFileName(one).StartsWith(".", StringComparison.Ordinal) && !string.Equals(Path.GetFullPath(one), data, StringComparison.OrdinalIgnoreCase)) dirs.Add(Map("name", Path.GetFileName(one), "path", Relative(root, one)));
            foreach (string one in Sorted(Directory.GetFiles(folder)))
                if (Kind(one) != null) files.Add(Map("name", Path.GetFileName(one), "path", Relative(root, one), "kind", Kind(one), "size", new FileInfo(one).Length));
            string here = Relative(root, folder);
            object parent = here.Length == 0 ? null : here.IndexOf('/') < 0 ? "" : here.Substring(0, here.LastIndexOf('/'));
            Json(stream, 200, Map("dir", here, "parent", parent, "root", FolderName, "dirs", dirs, "files", files));
        }

        // Her Planner folder's .md files, outside its project folder; the board each was exported from, if it was.
        private List<object> Docs()
        {
            List<object> found = new List<object>();
            foreach (string file in Sorted(Directory.GetFiles(root, "*.md", SearchOption.AllDirectories)))
            {
                if (Inside(data, file) || Regex.IsMatch(Relative(root, file), @"(^|/)\.")) continue;
                string text = File.ReadAllText(file, Encoding.UTF8);
                string head = text.Length > 4000 ? text.Substring(0, 4000) : text;
                Match board = Regex.Match(head, @"<!-- storyboard board=(\S+)");
                Match title = Regex.Match(head, @"^#{1,2}\s+(.+)$", RegexOptions.Multiline);
                FileInfo info = new FileInfo(file);
                found.Add(Map("path", Relative(root, file), "name", info.Name, "size", info.Length, "mtime", (long)(info.LastWriteTimeUtc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds,
                    "board", board.Success ? board.Groups[1].Value : null, "title", title.Success ? title.Groups[1].Value.Trim() : Path.GetFileNameWithoutExtension(file)));
            }
            return found;
        }

        private string DocPath(string relative)
        {
            if (string.IsNullOrEmpty(relative) || !relative.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) return null;
            string file = Under(root, relative);
            return file == null || Inside(data, file) ? null : file;
        }

        internal static string Kind(string name)
        {
            string extension = Path.GetExtension(name ?? "").ToLowerInvariant();
            if (Array.IndexOf(Images, extension) >= 0) return "image";
            if (Array.IndexOf(Audio, extension) >= 0) return "audio";
            if (Array.IndexOf(Fonts, extension) >= 0) return "font";
            return null;
        }

        // A path under a folder, or null for one that climbs out of it.
        private static string Under(string folder, string relative)
        {
            if (relative == null || relative.IndexOf('\0') >= 0) return null;
            string full;
            try { full = Path.GetFullPath(Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar))); }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
            catch (PathTooLongException) { return null; }
            return Inside(folder, full) ? full : null;
        }

        private static bool Inside(string folder, string path)
        {
            string within = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(path);
            return full.StartsWith(within, StringComparison.OrdinalIgnoreCase) || string.Equals(full.TrimEnd(Path.DirectorySeparatorChar), within.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        }

        private static string Relative(string folder, string path)
        {
            string full = Path.GetFullPath(path), within = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
            return full.Length <= within.Length ? "" : full.Substring(within.Length + 1).Replace(Path.DirectorySeparatorChar, '/');
        }

        private static List<string> Sorted(string[] paths)
        {
            List<string> list = new List<string>(paths);
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        private static Dictionary<string, object> Map(params object[] pairs) { return HuskJson.Map(pairs); }

        private static void Json(Stream stream, int status, object value)
        {
            Send(stream, status, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(HuskJson.Write(value)), "Cache-Control: no-store\r\n");
        }

        // A file, a range of it for audio, kept a day when it's art; what they brought in never runs as a page of its own.
        private static void SendFile(Stream stream, Ask ask, string file, bool cache, bool brought, string extra = "")
        {
            if (!File.Exists(file)) { Json(stream, 404, Map("error", "not found")); return; }
            string type;
            if (!Types.TryGetValue(Path.GetExtension(file), out type)) type = "application/octet-stream";
            byte[] bytes = File.ReadAllBytes(file);
            long start = 0, end = bytes.Length - 1;
            bool partial = false;
            Match range = Regex.Match(ask.Range ?? "", @"^bytes=(\d*)-(\d*)$");
            if (range.Success && bytes.Length > 0)
            {
                long a, b;
                bool hasA = long.TryParse(range.Groups[1].Value, out a), hasB = long.TryParse(range.Groups[2].Value, out b);
                if (hasA) { start = a; if (hasB) end = Math.Min(b, end); }
                else if (hasB) start = Math.Max(0, bytes.Length - b);
                partial = start <= end && (hasA || hasB);
            }
            string headers = extra + "Accept-Ranges: bytes\r\nCache-Control: " + (cache ? "max-age=86400" : "no-store") + "\r\n"
                + (brought ? "Content-Security-Policy: sandbox\r\nX-Content-Type-Options: nosniff\r\n" : "")
                + (partial ? "Content-Range: bytes " + start + "-" + end + "/" + bytes.Length + "\r\n" : "");
            byte[] body = bytes;
            if (partial)
            {
                body = new byte[end - start + 1];
                Buffer.BlockCopy(bytes, (int)start, body, 0, body.Length);
            }
            Send(stream, partial ? 206 : 200, type, body, headers);
        }

        private static void Send(Stream stream, int status, string type, byte[] body, string headers)
        {
            string reason = status == 200 ? "OK" : status == 206 ? "Partial Content" : status == 403 ? "Forbidden" : status == 404 ? "Not Found" : status == 409 ? "Conflict" : status == 400 ? "Bad Request" : "Error";
            byte[] head = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + " " + reason + "\r\nContent-Type: " + type + "\r\nContent-Length: " + body.Length + "\r\n" + headers + "Connection: close\r\n\r\n");
            stream.Write(head, 0, head.Length);
            stream.Write(body, 0, body.Length);
            stream.Flush();
        }

#if CHECKS
        // With a stand-in app, on this PC only: the token and her own address, the project's revisions and backups,
        // uploads kept once, the bin, docs, browse and import kept to her Planner folder, the page's log, and solo.
        internal static void Check(List<string> checks, string folder)
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            string app = Path.Combine(folder, "app"), planner = Path.Combine(folder, FolderName);
            Directory.CreateDirectory(Path.Combine(app, "js"));
            File.WriteAllText(Path.Combine(app, "index.html"), "<!doctype html><script type=\"module\" src=\"js/main.js\"></script>");
            File.WriteAllText(Path.Combine(app, "js", "main.js"), "export const planner = 1;");
            List<string> logged = new List<string>();
            DateTime now = new DateTime(2026, 11, 25, 9, 0, 0, DateTimeKind.Utc);
            using (PlannerHost host = new PlannerHost(app, planner))
            {
                host.Logged = delegate(string line) { lock (logged) logged.Add(line); };
                host.Clock = delegate { return now; };
                host.Start();
                string address = "http://127.0.0.1:" + host.Port;
                string cookie = null;
                Func<string, string, byte[], string, KeyValuePair<int, string>> call = delegate(string method, string path, byte[] body, string hostName)
                {
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(address + path);
                    request.Method = method;
                    request.Proxy = null;
                    request.Timeout = 10000;
                    request.AllowAutoRedirect = false;
                    if (hostName != null) request.Host = hostName;
                    if (cookie != null) request.Headers[HttpRequestHeader.Cookie] = cookie;
                    if (body != null) { request.ContentLength = body.Length; using (Stream up = request.GetRequestStream()) up.Write(body, 0, body.Length); }
                    HttpWebResponse response;
                    try { response = (HttpWebResponse)request.GetResponse(); }
                    catch (WebException failed) { response = failed.Response as HttpWebResponse; if (response == null) throw; }
                    using (response)
                    using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                    {
                        string set = response.Headers["Set-Cookie"];
                        if (set != null) cookie = set.Split(';')[0];
                        string extra = (response.Headers["X-Rev"] ?? "") + "|" + (response.Headers["Content-Security-Policy"] ?? "") + "|";
                        return new KeyValuePair<int, string>((int)response.StatusCode, extra + reader.ReadToEnd());
                    }
                };
                Func<string, byte[]> bytes = delegate(string text) { return Encoding.UTF8.GetBytes(text); };
                List<string> wrong = new List<string>();
                bool loopback = IPAddress.IsLoopback(((IPEndPoint)host.listener.LocalEndpoint).Address);
                int bare = call("GET", "/", null, null).Key, other = call("GET", "/?token=" + host.Token, null, "localhost.example:" + host.Port).Key;
                KeyValuePair<int, string> opened = call("GET", "/?token=" + host.Token, null, null);
                KeyValuePair<int, string> script = call("GET", "/js/main.js", null, null);
                if (!loopback || bare != 403 || other != 403 || opened.Key != 200 || cookie == null || script.Key != 200 || !script.Value.EndsWith("export const planner = 1;", StringComparison.Ordinal) || call("GET", "/../../x", null, null).Key == 200)
                    wrong.Add("the gate: loopback " + loopback + ", bare " + bare + ", another host " + other + ", opened " + opened.Key + ", cookie " + (cookie != null) + ", script " + script.Key);
                KeyValuePair<int, string> empty = call("GET", "/api/project", null, null);
                KeyValuePair<int, string> first = call("PUT", "/api/project?rev=0", bytes("{\"boards\":[1]}"), null);
                KeyValuePair<int, string> stale = call("PUT", "/api/project?rev=0", bytes("{\"boards\":[2]}"), null);
                int unnumbered = call("PUT", "/api/project", bytes("{}"), null).Key, broken = call("PUT", "/api/project?rev=1", bytes("{not json"), null).Key;
                now = now.AddSeconds(BackupEvery + 1);
                call("PUT", "/api/project?rev=1", bytes("{\"boards\":[3]}"), null);
                string[] kept = Directory.GetFiles(Path.Combine(planner, "project", "backups"), "project_*.json");
                if (empty.Value != "0||null" || first.Key != 200 || !first.Value.Contains("\"rev\":1") || stale.Key != 409 || !stale.Value.StartsWith("1||{\"boards\":[1]}", StringComparison.Ordinal) || unnumbered != 400 || broken != 400 || kept.Length != 1 || File.ReadAllText(kept[0]) != "{\"boards\":[1]}")
                    wrong.Add("the project: empty " + empty.Value + ", first " + first.Value + ", stale " + stale.Key + " " + stale.Value + ", no rev " + unnumbered + ", bad JSON " + broken + ", backups " + kept.Length);
                KeyValuePair<int, string> art = call("POST", "/api/upload?name=" + Uri.EscapeDataString("my art.png"), new byte[] { 1, 2, 3 }, null);
                KeyValuePair<int, string> again = call("POST", "/api/upload?name=" + Uri.EscapeDataString("my art.png"), new byte[] { 1, 2, 3 }, null);
                int page = call("POST", "/api/upload?name=x.html", bytes("<script>"), null).Key;
                string stored = Regex.Match(art.Value, "\"file\":\"([^\"]+)\"").Groups[1].Value;
                KeyValuePair<int, string> served = call("GET", "/assets/" + stored, null, null);
                int climbed = call("GET", "/assets/..%2Fproject.json", null, null).Key;
                if (art.Key != 200 || !Regex.IsMatch(stored, @"^my_art_[0-9a-f]{8}\.png$") || art.Value != again.Value || page != 400 || served.Key != 200 || !served.Value.Contains("sandbox") || climbed == 200)
                    wrong.Add("uploads: " + art.Value + " / " + again.Value + ", a page " + page + ", served " + served.Key + " " + served.Value.Split('|')[1] + ", climbed " + climbed);
                KeyValuePair<int, string> dry = call("POST", "/api/assets/clean?dry=1", bytes("{\"keep\":[]}"), null);
                call("POST", "/api/assets/clean", bytes("{\"keep\":[]}"), null);
                KeyValuePair<int, string> binned = call("GET", "/api/assets/bin", null, null);
                call("POST", "/api/assets/restore", bytes("{\"files\":[\"" + stored + "\"]}"), null);
                if (!dry.Value.Contains(stored) || !dry.Value.Contains("\"dry\":true") || !binned.Value.Contains(stored) || !File.Exists(Path.Combine(planner, "project", "assets", stored)))
                    wrong.Add("the bin: dry " + dry.Value + ", bin " + binned.Value);
                string board = "<!-- storyboard board=W1 -->\n# Start here\nA card.";
                int written = call("PUT", "/api/doc?path=notes/start.md", bytes(board), null).Key;
                File.WriteAllText(Path.Combine(planner, "theirs.md"), "# Theirs");
                int theirs = call("PUT", "/api/doc?path=theirs.md", bytes(board), null).Key, intoProject = call("PUT", "/api/doc?path=project/x.md", bytes(board), null).Key, outside = call("PUT", "/api/doc?path=../x.md", bytes(board), null).Key;
                KeyValuePair<int, string> docs = call("GET", "/api/docs", null, null);
                if (written != 200 || theirs != 409 || intoProject != 400 || outside != 400 || !docs.Value.Contains("\"path\":\"notes/start.md\"") || !docs.Value.Contains("\"board\":\"W1\"") || !docs.Value.Contains("\"title\":\"Start here\""))
                    wrong.Add("docs: written " + written + ", theirs " + theirs + ", into project " + intoProject + ", outside " + outside + ", " + docs.Value);
                Directory.CreateDirectory(Path.Combine(planner, "art"));
                File.WriteAllBytes(Path.Combine(planner, "art", "sky.png"), new byte[] { 9, 9 });
                File.WriteAllBytes(Path.Combine(folder, "outside.png"), new byte[] { 7 });
                KeyValuePair<int, string> top = call("GET", "/api/browse?dir=", null, null);
                int climbBrowse = call("GET", "/api/browse?dir=..", null, null).Key;
                KeyValuePair<int, string> imported = call("POST", "/api/import", bytes("{\"paths\":[\"art/sky.png\",\"../outside.png\"]}"), null);
                if (!top.Value.Contains("\"name\":\"art\"") || top.Value.Contains("\"name\":\"project\"") || climbBrowse != 404 || !imported.Value.Contains("sky_") || imported.Value.Contains("outside"))
                    wrong.Add("browse and import: " + top.Value + ", climbed " + climbBrowse + ", imported " + imported.Value);
                call("POST", "/api/log", bytes("{\"level\":\"warn\",\"msg\":\"a page line\"}"), null);
                int solo = call("GET", "/api/whoami", null, null).Key;
                lock (logged) if (logged.Count != 1 || logged[0] != "[page:warn] a page line") wrong.Add("the log: " + string.Join(" / ", logged.ToArray()));
                if (solo != 404) wrong.Add("whoami answered " + solo + ", so the editor wouldn't run solo");
                if (wrong.Count > 0) throw new Exception("The Planner's server went wrong: " + string.Join("; ", wrong.ToArray()));
            }
            checks.Add("PASS the Planner's server: 127.0.0.1 only, her token in the address then a cookie, another host name turned away; the project's revision refuses a stale save and hands back the current one, saves are whole JSON with a backup each " + BackupEvery + " s; uploads kept once by hash and served sandboxed, the bin and its restore, docs kept to .md in her Planner folder and never over a file it didn't write, browse and import inside it only, the page's log in hers, and no sessions so the editor runs solo");
            try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
#endif
    }
}
