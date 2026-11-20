using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using CapriDesktop;

// Her GNOME window helper. "offline" checks the pieces with nothing real touched; run.sh adds "install" and "live",
// which run against a headless GNOME Shell on a private session bus with its own settings and extensions folders.
internal static class Program
{
    private static int passed;

    private static void Check(bool ok, string what)
    {
        if (!ok) throw new Exception("FAIL " + what);
        passed++;
        Console.WriteLine("PASS " + what);
    }

    private static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "offline";
        try
        {
            if (mode == "offline") Offline();
            else if (mode == "install") Install();
            else if (mode == "live") Live(args.Length > 1 ? args[1] : "calculator");
            // The helper as she'd write it for a given GNOME, for checking it on another GNOME (run-legacy.sh).
            else if (mode == "files")
            {
                int shell = int.Parse(args[2]);
                Directory.CreateDirectory(args[1]);
                File.WriteAllText(Path.Combine(args[1], "extension.js"), GnomeHelper.Script(shell));
                File.WriteAllText(Path.Combine(args[1], "metadata.json"), GnomeHelper.Metadata(shell));
                Check(true, "wrote the GNOME " + shell + " helper to " + args[1]);
            }
            else throw new Exception("Unknown mode " + mode);
        }
        catch (Exception error) { Console.WriteLine(error.Message); return 1; }
        Console.WriteLine("PASS gnome " + mode + " checks=" + passed);
        return 0;
    }

    private static void Offline()
    {
        List<string> none = GnomeHelper.Names("@as []");
        List<string> two = GnomeHelper.Names("['ding@rastersoft.com', 'ubuntu-dock@ubuntu.com']");
        Check(none != null && none.Count == 0, "an empty GSettings list reads as empty");
        Check(two != null && two.Count == 2 && two[1] == "ubuntu-dock@ubuntu.com", "a GSettings list reads every name");
        Check(GnomeHelper.Names("['it\\'s@odd']") == null && GnomeHelper.Names("nonsense") == null && GnomeHelper.Names(null) == null, "a list she can't read safely is left alone");
        Check(GnomeHelper.Listed(two) == "['ding@rastersoft.com', 'ubuntu-dock@ubuntu.com']", "a list is written back in GSettings' own form");

        string modern = GnomeHelper.Script(50), old = GnomeHelper.Script(42);
        Check(modern.StartsWith("import Gio from 'gi://Gio';", StringComparison.Ordinal) && modern.Contains("export default class CapriWindowsExtension extends Extension") && !modern.Contains("imports.gi"), "GNOME 45 and later get a module");
        Check(old.StartsWith("const {Gio, GLib, Meta} = imports.gi;", StringComparison.Ordinal) && old.Contains("function init()") && !old.Contains("import ") && !old.Contains("export "), "GNOME 40 to 44 get the old style");
        Check(!modern.Contains("@HELPER@") && modern.Contains("helper: '" + GnomeHelper.Version + "'"), "the helper says which version it is");
        Check(GnomeHelper.Metadata(50).Contains("\"50\"") && GnomeHelper.Metadata(50).Contains("\"52\"") && !GnomeHelper.Metadata(50).Contains("\"53\""), "a module helper also lists the next two GNOMEs");
        Check(GnomeHelper.Metadata(42).Contains("\"44\"") && !GnomeHelper.Metadata(42).Contains("\"45\""), "an old-style helper never claims GNOME 45");

        string home = Path.Combine(Path.GetTempPath(), "capri-gnome-" + Guid.NewGuid().ToString("N"));
        GnomeHelper.DataHome = () => home;
        try
        {
            var settings = new Dictionary<string, string> { ["disable-user-extensions"] = "false", ["disabled-extensions"] = "@as []", ["enabled-extensions"] = "['ubuntu-dock@ubuntu.com']" };
            string shell = "GNOME Shell 50.1\n";
            List<string> ran = new List<string>();
            GnomeHelper.Run = delegate(string file, string[] a)
            {
                ran.Add(file + " " + string.Join(" ", a));
                if (file == "gnome-shell") return shell;
                if (file == "gsettings" && a[0] == "get") return settings.TryGetValue(a[2], out string v) ? v + "\n" : null;
                if (file == "gsettings" && a[0] == "set") { settings[a[2]] = a[3]; return ""; }
                return null;
            };
            Check(GnomeHelper.Install() == null && File.Exists(Path.Combine(GnomeHelper.Folder, "extension.js")) && File.ReadAllText(Path.Combine(GnomeHelper.Folder, "metadata.json")).Contains(GnomeHelper.Uuid), "install writes the helper into the user's extensions folder");
            Check(settings["enabled-extensions"] == "['ubuntu-dock@ubuntu.com', '" + GnomeHelper.Uuid + "']", "install turns it on and keeps their other extensions");
            DateTime written = File.GetLastWriteTimeUtc(Path.Combine(GnomeHelper.Folder, "extension.js"));
            int sets = ran.FindAll(r => r.StartsWith("gsettings set", StringComparison.Ordinal)).Count;
            Thread.Sleep(20);
            Check(GnomeHelper.Install() == null && File.GetLastWriteTimeUtc(Path.Combine(GnomeHelper.Folder, "extension.js")) == written && ran.FindAll(r => r.StartsWith("gsettings set", StringComparison.Ordinal)).Count == sets, "a second start rewrites and resets nothing");
            shell = "GNOME Shell 42.9\n";
            Check(GnomeHelper.Install() == null && File.ReadAllText(Path.Combine(GnomeHelper.Folder, "extension.js")).Contains("function init()"), "a different GNOME gets its own style rewritten");
            settings["disabled-extensions"] = "['" + GnomeHelper.Uuid + "']";
            settings["enabled-extensions"] = "@as []";
            string turnedOff = GnomeHelper.Install();
            Check(turnedOff != null && turnedOff.Contains("Extensions app") && settings["enabled-extensions"] == "@as []", "a helper they turned off stays off, and she says where to turn it on");
            settings["disabled-extensions"] = "@as []";
            settings["disable-user-extensions"] = "true";
            Check((GnomeHelper.Install() ?? "").Contains("extensions switched off") && settings["enabled-extensions"] == "@as []", "with GNOME's extensions off she says so and changes nothing");
            settings["disable-user-extensions"] = "false";
            shell = "GNOME Shell 3.38\n";
            Check((GnomeHelper.Install() ?? "").Contains("GNOME 40 or newer"), "GNOME older than 40 is told plainly");
            shell = null;
            Check((GnomeHelper.Install() ?? "").Contains("couldn't tell which GNOME"), "no gnome-shell is told plainly");
        }
        finally
        {
            try { Directory.Delete(home, true); } catch (Exception) { }
        }

        GnomeBridge.Hear("Windows", "[1920,[[\"7\"," + Environment.ProcessId + ",0,0,100,100,28,\"Capri\",\"Capri\"],[\"9\",4242,10,20,800,600,28,\"Untitled - Text Editor\",\"org.gnome.TextEditor\"],[\"11\",4243,0,0,300,200,4,\"Save?\",\"org.gnome.TextEditor\"]]]");
        Limbs.ProcessName = id => id == 4242 ? "gnome-text-editor" : "";
        List<OpenWindow> listed = GnomeBridge.Windows();
        Check(listed.Count == 1 && listed[0].Handle.ToInt64() == 9 && listed[0].Program == "gnome-text-editor" && listed[0].Title == "Untitled - Text Editor", "her window tool lists app windows, not hers or dialogs, named by process");
        Check(GnomeBridge.Front().ToInt64() == 7, "the focused window is the front one");
        GnomeBridge.Hear("Moved", "[\"9\",4242,300,40,800,600,28,\"Untitled - Text Editor\",\"org.gnome.TextEditor\"]");
        Check(GnomeBridge.Get(9).Bounds.X == 300 && GnomeBridge.Stack().Count == 3, "a window being dragged moves in place");
        GnomeDesk desk = new GnomeDesk();
        desk.Screens.Add(new Avalonia.PixelRect(0, 0, 3840, 2160));
        List<Spot> spots = desk.Windows(new HashSet<long>());
        Check(spots.Count == 2 && spots[0].Window == 9 && spots[0].Bounds.X == 600 && spots[0].Bounds.Width == 1600, "her desk skips her own window and scales GNOME's logical pixels to hers");
    }

    private static void Install()
    {
        string why = GnomeHelper.Install();
        Check(why == null, "the real install has nothing to report: " + why);
        Check(File.Exists(Path.Combine(GnomeHelper.Folder, "extension.js")), "the helper is in " + GnomeHelper.Folder);
    }

    private static List<KWinWindow> Wait(Func<List<KWinWindow>, bool> ok, string what)
    {
        for (int i = 0; i < 60; i++)
        {
            List<KWinWindow> now = GnomeBridge.Stack();
            if (ok(now)) return now;
            Thread.Sleep(100);
        }
        throw new Exception("FAIL " + what + ": " + string.Join(" | ", GnomeBridge.Stack().ConvertAll(w => w.Id + " " + w.Class + " '" + w.Title + "' min " + w.Minimized + " active " + w.Active)));
    }

    private static void Live(string word)
    {
        Check(GnomeBridge.Wanted, "GNOME Wayland is recognised");
        Brain.LogPath = Path.Combine(Path.GetTempPath(), "capri-gnome-errors-" + Environment.ProcessId + ".txt");
        // At log-in GNOME can still be settling its extensions, so like Capri this keeps asking for a while.
        bool started = GnomeBridge.Start();
        for (int i = 0; i < 40 && !GnomeBridge.Live; i++) { Thread.Sleep(250); started = GnomeBridge.Beat(); }
        if (!started && File.Exists(Brain.LogPath)) Console.WriteLine(File.ReadAllText(Brain.LogPath));
        Check(started && GnomeBridge.Live, "she subscribes to the helper GNOME loaded: " + GnomeBridge.Why);
        Check(GnomeBridge.Why == null, "the loaded helper is the current version");
        Func<KWinWindow, bool> target = w => (w.Class + " " + w.Title).IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0;
        KWinWindow app = Wait(s => s.Exists(w => target(w) && w.Bounds.Width > 0 && w.Shown), "the test app's window is listed once it has a size").Find(w => target(w) && w.Bounds.Width > 0);
        Check(app.Bounds.Width > 0 && app.Bounds.Height > 0 && app.Pid > 0 && GnomeBridge.Width > 0, "its window has a size, a pid and a desktop to sit on: " + app.Bounds + " pid " + app.Pid);
        OpenWindow listed = GnomeBridge.Windows().Find(w => w.Handle.ToInt64() == app.Id);
        Check(listed != null, "her window tool lists it");
        Check(GnomeBridge.Steer(listed, "minimize"), "minimize is accepted");
        Wait(s => s.Exists(w => w.Id == app.Id && w.Minimized), "it minimizes, and she hears about it");
        Check(GnomeBridge.Steer(listed, "switch_to"), "switch to is accepted");
        Wait(s => s.Exists(w => w.Id == app.Id && !w.Minimized && w.Active), "switching to it restores and focuses it");
        Check(!GnomeBridge.Act(999999999, "close"), "a window id that isn't there does nothing");
        Check(GnomeBridge.Steer(listed, "close"), "close is accepted and it's gone");
        Wait(s => !s.Exists(w => w.Id == app.Id), "it stays gone from her list");
    }
}
