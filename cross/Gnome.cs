using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Tmds.DBus.Protocol;

namespace CapriDesktop
{
    // GNOME on Wayland gives apps no way to see or steer other apps' windows: no wmctrl, no scripting like KWin's. The
    // one way in that needs nothing changed in GNOME is an extension, so Capri carries a small one of her own
    // (cross/gnome/capri-windows.js) and puts it in the user's own extensions folder. GNOME loads it at the next log-in;
    // from then on it tells her where the windows are and switches, minimizes and closes them when she's asked.
    //
    // Only the user's own files and their own GNOME setting that lists turned-on extensions are touched. A helper they
    // turned off in GNOME's Extensions app stays off: she says how to turn it back on instead.
    internal static class GnomeHelper
    {
        internal const string Uuid = "capri-windows@capriai.app";
        // Bump when capri-windows.js changes, so an older copy is rewritten.
        internal const string Version = "1";

        // Swapped in checks so nothing on the real desktop is read or written.
        internal static Func<string, string[], string> Run = delegate(string file, string[] args) { return Music.Run(file, args); };
        internal static Func<string> DataHome = delegate
        {
            string data = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            return !string.IsNullOrEmpty(data) ? data : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        };

        internal static string Folder { get { return Path.Combine(DataHome(), "gnome-shell", "extensions", Uuid); } }

        // GNOME Shell's major version, like 50, or 0 when it can't be read.
        internal static int Shell()
        {
            Match found = Regex.Match(Run("gnome-shell", new[] { "--version" }) ?? "", @"GNOME Shell (\d+)");
            int major;
            return found.Success && int.TryParse(found.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out major) ? major : 0;
        }

        // GNOME 45 changed how extensions are written (modules instead of imports), so the same body gets the right ends.
        internal static string Script(int shell)
        {
            string body;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Capri.Gnome.capri-windows.js"))
            using (StreamReader reader = new StreamReader(stream))
                body = reader.ReadToEnd().Replace("@HELPER@", Version);
            if (shell >= 45)
                return "import Gio from 'gi://Gio';\nimport GLib from 'gi://GLib';\nimport Meta from 'gi://Meta';\n"
                    + "import * as Main from 'resource:///org/gnome/shell/ui/main.js';\n"
                    + "import {Extension} from 'resource:///org/gnome/shell/extensions/extension.js';\n\n" + body
                    + "\nexport default class CapriWindowsExtension extends Extension {\n"
                    + "    enable() { this._capri = new CapriWindows(); this._capri.enable(); }\n"
                    + "    disable() { if (this._capri) this._capri.disable(); this._capri = null; }\n}\n";
            return "const {Gio, GLib, Meta} = imports.gi;\nconst Main = imports.ui.main;\n\n" + body
                + "\nclass CapriWindowsExtension {\n"
                + "    constructor() { this._capri = null; }\n"
                + "    enable() { this._capri = new CapriWindows(); this._capri.enable(); }\n"
                + "    disable() { if (this._capri) this._capri.disable(); this._capri = null; }\n}\n\n"
                + "function init() { return new CapriWindowsExtension(); }\n";
        }

        // GNOME refuses an extension that doesn't list its version. Before 45 only the old style runs, so it lists up to
        // 44; from 45 it also lists the next two, so a GNOME update doesn't cost another log-in before she rewrites it.
        internal static string Metadata(int shell)
        {
            List<string> versions = new List<string>();
            int last = shell >= 45 ? shell + 2 : 44;
            for (int v = shell; v <= last; v++) versions.Add(v.ToString(CultureInfo.InvariantCulture));
            var meta = new SortedDictionary<string, object>
            {
                ["uuid"] = Uuid,
                ["name"] = "Capri window helper",
                ["description"] = "Lets Capri, your desktop companion, see where your windows are and switch to, minimize or close one when you ask her. Installed by Capri; it does nothing while Capri isn't running.",
                ["shell-version"] = versions,
                ["url"] = "https://capriai.app",
                ["version-name"] = Version,
            };
            return JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }) + "\n";
        }

        // The quoted names in a GSettings string list, like ['a@b', 'c@d'] or @as []. Null when it can't be read safely.
        internal static List<string> Names(string listed)
        {
            if (listed == null) return null;
            string text = listed.Trim();
            if (text.StartsWith("@as", StringComparison.Ordinal)) text = text.Substring(3).Trim();
            if (!text.StartsWith("[", StringComparison.Ordinal) || !text.EndsWith("]", StringComparison.Ordinal)) return null;
            List<string> names = new List<string>();
            string inside = text.Substring(1, text.Length - 2).Trim();
            if (inside.Length == 0) return names;
            foreach (Match one in Regex.Matches(inside, @"\G\s*'([^'\\]*)'\s*(?:,|$)")) names.Add(one.Groups[1].Value);
            // Anything she couldn't account for (an escaped quote, say) means she leaves their list alone.
            return Regex.Matches(inside, "'").Count == names.Count * 2 ? names : null;
        }

        internal static string Listed(List<string> names)
        {
            List<string> quoted = new List<string>();
            foreach (string name in names) quoted.Add("'" + name + "'");
            return "[" + string.Join(", ", quoted) + "]";
        }

        // Puts the helper in place and turns it on for the next log-in. Returns why it can't, or null.
        internal static string Install()
        {
            int shell = Shell();
            if (shell == 0) return "I couldn't tell which GNOME this is, so I couldn't set up my window helper.";
            if (shell < 40) return "Seeing and switching windows on GNOME needs GNOME 40 or newer; this is GNOME " + shell + ".";
            try
            {
                string folder = Folder;
                Directory.CreateDirectory(folder);
                Write(Path.Combine(folder, "extension.js"), Script(shell));
                Write(Path.Combine(folder, "metadata.json"), Metadata(shell));
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                return "I couldn't write my window helper into your GNOME extensions folder: " + error.Message;
            }
            if ((Run("gsettings", new[] { "get", "org.gnome.shell", "disable-user-extensions" }) ?? "").Trim() == "true")
                return "GNOME has extensions switched off. Turn them on in the Extensions app, log out and back in, and ask again.";
            List<string> off = Names(Run("gsettings", new[] { "get", "org.gnome.shell", "disabled-extensions" }));
            if (off != null && off.Contains(Uuid))
                return "My window helper is turned off in GNOME's Extensions app. Turn on \"Capri window helper\" there, then ask again.";
            List<string> on = Names(Run("gsettings", new[] { "get", "org.gnome.shell", "enabled-extensions" }));
            if (on == null) return "I couldn't read which GNOME extensions are on, so I left my window helper off. Turn on \"Capri window helper\" in the Extensions app.";
            if (!on.Contains(Uuid))
            {
                on.Add(Uuid);
                if (Run("gsettings", new[] { "set", "org.gnome.shell", "enabled-extensions", Listed(on) }) == null)
                    return "I couldn't turn my window helper on. Turn on \"Capri window helper\" in GNOME's Extensions app.";
            }
            return null;
        }

        // Only rewritten when it changed, so GNOME isn't told about a new file every time she starts.
        private static void Write(string path, string text)
        {
            if (File.Exists(path) && File.ReadAllText(path) == text) return;
            string temporary = path + ".new";
            File.WriteAllText(temporary, text);
            File.Move(temporary, path, true);
        }
    }

    // Her end of the helper: she subscribes once, and the helper calls her back with the windows whenever they change,
    // the way her KDE bridge does.
    internal static partial class GnomeBridge
    {
        private const string ObjectPath = "/app/capri/Gnome";
        private const string Interface = "app.capri.Gnome";
        private static readonly object gate = new object();
        private static readonly object data = new object();
        private static List<KWinWindow> stack = new List<KWinWindow>();
        private static Connection connection;
        internal static double Width;
        internal static volatile bool Heard;
        // Why she can't see windows yet, for her window tool to say.
        internal static volatile string Why;

        internal static bool Wanted
        {
            get
            {
                return OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") != null
                    && (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "").IndexOf("GNOME", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        internal static bool Live { get { return connection != null && Heard; } }

        private static System.Threading.Timer heartbeat;
        private static bool installed;
        private static string installTrouble;
        private static bool complained;

        // Installs the helper once (if it's missing or old), connects to the session bus, and from then on keeps asking the
        // helper for the windows every 10 seconds. GNOME turns extensions off while the screen is locked and can take a
        // moment to load them at log-in, so a missed answer just means trying again, never giving up.
        internal static bool Start()
        {
            lock (gate)
            {
                if (!Wanted) return false;
                if (!installed)
                {
                    installTrouble = GnomeHelper.Install();
                    installed = true;
                }
                if (connection == null)
                {
                    Connection made = null;
                    try
                    {
                        string address = Address.Session;
                        if (string.IsNullOrEmpty(address)) { Why = installTrouble ?? "I couldn't reach your desktop's session bus."; return false; }
                        made = new Connection(address);
                        Connection connecting = made;
                        Task.Run(() => connecting.ConnectAsync().AsTask()).WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                        made.AddMethodHandler(new Handler());
                        connection = made;
                        made = null;
                        AppDomain.CurrentDomain.ProcessExit += delegate { Stop(); };
                    }
                    catch (Exception error)
                    {
                        Why = installTrouble ?? "I couldn't reach your desktop's session bus.";
                        Brain.Log("gnome", -1, error.GetType().Name + ": " + error.Message);
                        return false;
                    }
                    finally { made?.Dispose(); }
                }
                if (heartbeat == null) heartbeat = new System.Threading.Timer(delegate { Beat(); }, null, 10000, 10000);
            }
            return Beat();
        }

        // One ask: who GNOME Shell is on the bus (her callbacks are only taken from it), then Subscribe, which is safe to
        // repeat and answers with the whole list.
        internal static bool Beat()
        {
            Connection talk = connection;
            if (talk == null) return false;
            try
            {
                shellName = Owner(talk);
                Hear("Windows", Ask(talk, "Subscribe"));
                string hello = null;
                try { hello = Ask(talk, "Hello"); } catch (Exception) { }
                Why = hello != null && hello.IndexOf("\"helper\":\"" + GnomeHelper.Version + "\"", StringComparison.Ordinal) < 0
                    ? "My window helper was updated; log out and back in when it suits you so GNOME loads the new one." : null;
                complained = false;
                return true;
            }
            catch (Exception error)
            {
                Heard = false;
                // Not answering means GNOME hasn't loaded it (it picks up a new extension at log-in) or has it off while locked.
                Why = installTrouble ?? "I've set up my window helper for GNOME. If you haven't since, log out and back in once so GNOME loads it, then ask again.";
                if (!complained) Brain.Log("gnome", -1, error.GetType().Name + ": " + error.Message);
                complained = true;
                return false;
            }
        }

        internal static void Stop()
        {
            lock (gate)
            {
                heartbeat?.Dispose();
                heartbeat = null;
                if (connection == null) return;
                connection.Dispose();
                connection = null;
                Heard = false;
            }
        }

        internal static List<KWinWindow> Stack()
        {
            lock (data) return new List<KWinWindow>(stack);
        }

        internal static KWinWindow Get(long id)
        {
            lock (data) foreach (KWinWindow window in stack) if (window.Id == id) return window;
            return null;
        }

        // switch_to, minimize or close, done by the helper inside GNOME. False when it's gone or the helper isn't there.
        internal static bool Act(long id, string action)
        {
            KWinWindow window = Get(id);
            Connection talk = connection;
            if (window == null || talk == null) return false;
            try
            {
                using MessageWriter writer = talk.GetMessageWriter();
                writer.WriteMethodCallHeader(destination: "org.gnome.Shell", path: ObjectPath, @interface: Interface, member: "Act", signature: "ss");
                writer.WriteString(window.Key);
                writer.WriteString(action);
                MessageBuffer message = writer.CreateMessage();
                return Task.Run(() => talk.CallMethodAsync(message, (reply, _) => reply.GetBodyReader().ReadBool())).WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            }
            catch (Exception error) { Brain.Log("gnome", -1, error.Message); return false; }
        }

        private static string Ask(Connection talk, string member)
        {
            using MessageWriter writer = talk.GetMessageWriter();
            writer.WriteMethodCallHeader(destination: "org.gnome.Shell", path: ObjectPath, @interface: Interface, member: member);
            MessageBuffer message = writer.CreateMessage();
            return Task.Run(() => talk.CallMethodAsync(message, (reply, _) => reply.GetBodyReader().ReadString())).WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
        }

        // A row is [id, pid, x, y, w, h, flags, title, class]. GNOME's window ids are never reused, so hers are the same.
        private static KWinWindow Read(JsonElement row)
        {
            int flags = row[6].GetInt32();
            string key = row[0].GetString() ?? "";
            long id;
            if (!long.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out id)) id = 0;
            return new KWinWindow
            {
                Id = id,
                Key = key,
                Pid = row[1].GetInt32(),
                Bounds = new PixelRect(row[2].GetInt32(), row[3].GetInt32(), Math.Max(0, row[4].GetInt32()), Math.Max(0, row[5].GetInt32())),
                Minimized = (flags & 1) != 0,
                FullScreen = (flags & 2) != 0,
                Shown = (flags & 4) != 0,
                Active = (flags & 8) != 0,
                Normal = (flags & 16) != 0,
                Title = row[7].GetString() ?? "",
                Class = row[8].GetString() ?? "",
            };
        }

        // "Windows" carries [desktop width, [rows...]], "Moved" one row while it's being dragged.
        internal static void Hear(string member, string json)
        {
            using JsonDocument parsed = JsonDocument.Parse(json);
            JsonElement root = parsed.RootElement;
            lock (data)
            {
                if (member == "Windows")
                {
                    Width = root[0].GetDouble();
                    List<KWinWindow> fresh = new List<KWinWindow>();
                    foreach (JsonElement row in root[1].EnumerateArray())
                    {
                        KWinWindow window = Read(row);
                        if (window.Id != 0) fresh.Add(window);
                    }
                    stack = fresh;
                    Heard = true;
                }
                else if (member == "Moved")
                {
                    KWinWindow moved = Read(root);
                    List<KWinWindow> fresh = new List<KWinWindow>(stack);
                    for (int i = 0; i < fresh.Count; i++) if (fresh[i].Key == moved.Key) fresh[i] = moved;
                    stack = fresh;
                }
            }
        }

        private sealed class Handler : IMethodHandler
        {
            public string Path { get { return ObjectPath; } }
            public bool RunMethodHandlerSynchronously(Message message) { return true; }

            public ValueTask HandleMethodAsync(MethodContext context)
            {
                Message request = context.Request;
                string member = request.MemberAsString;
                // Only GNOME Shell's own connection may tell her what's on screen.
                string owner = shellName;
                bool shell = owner != null && request.SenderAsString == owner;
                if (shell && request.InterfaceAsString == Interface && (member == "Windows" || member == "Moved") && request.SignatureAsString == "s")
                {
                    try { Hear(member, request.GetBodyReader().ReadString()); }
                    catch (Exception error) { Brain.Log("gnome", -1, error.Message); }
                    if (!context.NoReplyExpected)
                    {
                        using MessageWriter writer = context.CreateReplyWriter(null);
                        context.Reply(writer.CreateMessage());
                    }
                }
                else if (!context.NoReplyExpected) context.ReplyError("org.freedesktop.DBus.Error.AccessDenied", "Not GNOME Shell's window helper.");
                return ValueTask.CompletedTask;
            }
        }

        // The unique name that owns org.gnome.Shell. Her callbacks must come from it.
        private static volatile string shellName;

        private static string Owner(Connection talk)
        {
            using MessageWriter writer = talk.GetMessageWriter();
            writer.WriteMethodCallHeader(destination: "org.freedesktop.DBus", path: "/org/freedesktop/DBus", @interface: "org.freedesktop.DBus", member: "GetNameOwner", signature: "s");
            writer.WriteString("org.gnome.Shell");
            MessageBuffer message = writer.CreateMessage();
            return Task.Run(() => talk.CallMethodAsync(message, (reply, _) => reply.GetBodyReader().ReadString())).WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
        }
    }

    // GNOME on Wayland: windows come from her helper. Like KDE's, the mouse is only known while she's held.
    internal sealed class GnomeDesk : Desk
    {
        internal Func<PixelPoint?> HeldAt;
        private readonly int own = Environment.ProcessId;

        internal override string Name { get { return "GNOME Wayland"; } }
        internal override string Debug() { return "scale " + Rescale().ToString(CultureInfo.InvariantCulture) + (GnomeBridge.Live ? "" : ", helper quiet"); }
        internal override PixelPoint? Grabbed() { return HeldAt == null ? null : HeldAt(); }

        // GNOME speaks in logical pixels; XWayland apps that scale themselves see physical ones.
        private double Rescale()
        {
            int right = 0;
            foreach (PixelRect screen in Screens) right = Math.Max(right, screen.Right);
            double width = GnomeBridge.Width;
            return right > 0 && width > 0 ? right / width : 1;
        }

        private Spot Look(KWinWindow window, double scale)
        {
            if (window.Pid == own || window.Minimized || !window.Shown || window.Bounds.Width <= 0 || window.Bounds.Height <= 0) return null;
            PixelRect b = window.Bounds;
            PixelRect bounds = scale == 1 ? b : new PixelRect((int)Math.Round(b.X * scale), (int)Math.Round(b.Y * scale), (int)Math.Round(b.Width * scale), (int)Math.Round(b.Height * scale));
            Spot spot = Make(window.Id, bounds, !window.FullScreen);
            spot.Name = window.Class + "\n" + window.Title;
            return spot;
        }

        internal override List<Spot> Windows(ICollection<long> skip)
        {
            double scale = Rescale();
            List<Spot> found = new List<Spot>();
            foreach (KWinWindow window in GnomeBridge.Stack())
            {
                Spot spot = Look(window, scale);
                if (spot != null && !skip.Contains(spot.Window)) found.Add(spot);
            }
            return found;
        }

        internal override Spot Find(long window)
        {
            KWinWindow found = GnomeBridge.Get(window);
            return found == null ? null : Look(found, Rescale());
        }

        internal override bool Alive(long window) { return GnomeBridge.Get(window) != null; }
    }
}
