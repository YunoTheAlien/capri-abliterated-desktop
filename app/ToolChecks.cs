using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;

#if CHECKS
namespace CapriDesktop
{
    internal partial class Companion
    {
        // A pretend window on her pretend desk: where it is, its normal place for when it's restored, and what it won't do.
        private sealed class PretendWindow
        {
            internal Rectangle Edges, Normal;
            internal bool Minimized, Maximized, Admin, Pinned;
            internal Size Least = new Size(120, 80);
        }

        // Her window tool on three monitors, one portrait and one at 150%, with pretend windows that behave like real
        // ones (a minimum width, admin, not responding). She reports where each window really ended up (Lachy).
        private static void SmokeWindowTool(List<string> checks)
        {
            Func<List<Arrange.Display>> displaysWas = Arrange.Displays;
            Func<IntPtr, Arrange.Spot> readWas = Arrange.Read;
            Func<IntPtr, string, Rectangle, bool> shapeWas = Arrange.Shape;
            Func<IntPtr, bool> raisedWas = Arrange.Raised, hungWas = Arrange.Hung;
            Func<Point> mouseWas = Arrange.Mouse;
            Func<IntPtr, bool> pinnedWas = Arrange.Pinned;
            Func<IntPtr, bool, bool> pinWas = Arrange.Pin;
            int patienceWas = Arrange.Patience;
            Func<List<OpenWindow>> windowsWas = Hands.Windows;
            Func<IntPtr> frontWas = Hands.Foreground;
            Func<string> unlistedWas = Hands.Unlisted;
            Func<OpenWindow, bool> minimizedWas = Hands.Minimized, gameyWas = Hands.Gamey;
            Func<OpenWindow, string> screenWas = Hands.ScreenOf;
            Func<OpenWindow, string, bool> steerWas = Hands.Steer;
            Func<string, string, bool> runningWas = Hands.Running;
            Func<string, string> lookWas = Hands.Look;
            Func<string, string, string> lookOnWas = Hands.LookOn;
            bool windowsOn = Security.On(Security.Windows);
            string openAiWas = Brain.OpenAi, notesWas = Brain.MemoryPath, memoriesWas = Brain.MemoriesPath, transcriptsWas = Brain.TranscriptPath, logWas = Brain.LogPath;
            bool keepingWas = Brain.Keeping;
            TcpListener fake = null;
            try
            {
                List<Arrange.Display> desk = new List<Arrange.Display>
                {
                    new Arrange.Display { Bounds = new Rectangle(0, 0, 1920, 1080), Work = new Rectangle(0, 0, 1920, 1032), Primary = true, Number = 1, Dpi = 96 },
                    new Arrange.Display { Bounds = new Rectangle(1920, -420, 1080, 1920), Work = new Rectangle(1920, -420, 1080, 1872), Number = 2, Dpi = 96 },
                    new Arrange.Display { Bounds = new Rectangle(3000, 0, 2560, 1440), Work = new Rectangle(3000, 0, 2560, 1392), Number = 3, Dpi = 144 }
                };
                Arrange.Named(desk);
                // Nothing on this desk runs without a window.
                Hands.Running = delegate { return false; };
                Dictionary<IntPtr, PretendWindow> pretend = new Dictionary<IntPtr, PretendWindow>();
                pretend[new IntPtr(31)] = new PretendWindow { Edges = new Rectangle(1990, -220, 940, 800), Least = new Size(940, 500) };
                pretend[new IntPtr(32)] = new PretendWindow { Edges = new Rectangle(0, 0, 1920, 1032), Normal = new Rectangle(320, 180, 1280, 720), Maximized = true };
                pretend[new IntPtr(33)] = new PretendWindow { Edges = new Rectangle(-32000, -32000, 160, 28), Normal = new Rectangle(100, 100, 800, 600), Minimized = true };
                pretend[new IntPtr(34)] = new PretendWindow { Edges = new Rectangle(400, 300, 900, 700), Admin = true };
                pretend[new IntPtr(35)] = new PretendWindow { Edges = new Rectangle(200, 200, 700, 500) };
                pretend[new IntPtr(36)] = new PretendWindow { Edges = new Rectangle(3200, 100, 1200, 800) };
                List<OpenWindow> open = new List<OpenWindow>
                {
                    new OpenWindow(new IntPtr(31), "#general | Capri Fans - Discord", "Discord"), new OpenWindow(new IntPtr(32), "YouTube - Google Chrome", "chrome"),
                    new OpenWindow(new IntPtr(33), "notes.txt - Notepad", "notepad"), new OpenWindow(new IntPtr(34), "Task Manager", "Taskmgr"),
                    new OpenWindow(new IntPtr(35), "Frozen App", "frozen"), new OpenWindow(new IntPtr(36), "Spotify Premium", "Spotify")
                };
                Arrange.Displays = delegate { return desk; };
                Arrange.Read = delegate(IntPtr handle)
                {
                    PretendWindow window;
                    if (!pretend.TryGetValue(handle, out window)) return new Arrange.Spot { Gone = true };
                    return new Arrange.Spot { Edges = window.Edges, Minimized = window.Minimized, Maximized = window.Maximized };
                };
                Arrange.Shape = delegate(IntPtr handle, string how, Rectangle wanted)
                {
                    PretendWindow window = pretend[handle];
                    if (window.Admin) return false;
                    if (how == "maximize")
                    {
                        if (!window.Maximized && !window.Minimized) window.Normal = window.Edges;
                        window.Maximized = true;
                        window.Minimized = false;
                        window.Edges = desk[Arrange.Under(window.Normal, desk)].Work;
                    }
                    else if (how == "restore")
                    {
                        if (window.Minimized) { window.Minimized = false; window.Edges = window.Maximized ? desk[Arrange.Under(window.Normal, desk)].Work : window.Normal; }
                        else if (window.Maximized) { window.Maximized = false; window.Edges = window.Normal; }
                    }
                    else
                    {
                        window.Edges = new Rectangle(wanted.Left, wanted.Top, Math.Max(wanted.Width, window.Least.Width), Math.Max(wanted.Height, window.Least.Height));
                        window.Normal = window.Edges;
                    }
                    return true;
                };
                Arrange.Raised = delegate(IntPtr handle) { return pretend[handle].Admin; };
                Arrange.Hung = delegate(IntPtr handle) { return handle == new IntPtr(35); };
                Arrange.Mouse = delegate { return new Point(100, 100); };
                Arrange.Patience = 100;
                Hands.Windows = delegate { return open; };
                Hands.Foreground = delegate { return new IntPtr(31); };
                Hands.Unlisted = delegate { return null; };
                Hands.Minimized = delegate(OpenWindow window) { return pretend[window.Handle].Minimized; };
                Hands.Gamey = delegate { return false; };
                Hands.ScreenOf = delegate(OpenWindow window) { PretendWindow seen = pretend[window.Handle]; return seen.Minimized ? null : desk[Arrange.Under(seen.Edges, desk)].Name; };
                Func<string, string, ToolArguments> asks = delegate(string name, string action) { return new ToolArguments { Name = name, Action = action }; };
                string[] said =
                {
                    Hands.Do("app_window", asks("chrome", "snap_left")),
                    Hands.Do("app_window", asks("discord", "snap_right")),
                    Hands.Do("app_window", asks("discord", "snap_top")),
                    Hands.Do("app_window", new ToolArguments { Name = "chrome", Action = "move_to_screen", Screen = "3" }),
                    Hands.Do("app_window", new ToolArguments { Name = "notepad", Action = "maximize", Screen = "3" }),
                    Hands.Do("app_window", asks("notepad", "restore")),
                    Hands.Do("app_window", asks("notepad", "restore")),
                    Hands.Do("app_window", asks("task manager", "maximize")),
                    Hands.Do("app_window", asks("frozen", "snap_left")),
                    Hands.Do("app_window", new ToolArguments { Name = "spotify", Action = "move", Position = "top_right" }),
                    Hands.Do("app_window", new ToolArguments { Name = "spotify", Action = "move", Left = "-200", Top = "10%" }),
                    Hands.Do("app_window", new ToolArguments { Name = "spotify", Action = "resize", Width = "50%", Height = "+10%" }),
                    Hands.Do("app_window", new ToolArguments { Name = "spotify", Action = "move_to_screen", Screen = "screen 7" }),
                    Hands.Do("app_window", new ToolArguments { Name = "discord", Action = "move_to_screen", Screen = "portrait" }),
                    Hands.Do("app_window", asks("spotify", "move_to_screen")),
                    Hands.Do("app_window", asks("minecraft", "snap_left")),
                    Hands.Do("app_window", new ToolArguments { Name = "spotify", Action = "resize", Width = "huge" }),
                    Hands.Do("app_window", asks("chrome", "maximize")),
                    Hands.Do("app_window", asks("chrome", "maximize"))
                };
                string[] expected =
                {
                    "Snapped chrome to the left half of the main screen.",
                    "Snapped discord to the right half of screen 2, but it stayed 940 x 1872: that's as small as it goes.",
                    "Snapped discord to the top half of screen 2.",
                    "Moved chrome to screen 3.",
                    "Maximized notepad on screen 3.",
                    "Restored notepad from maximized to its normal size.",
                    "notepad isn't minimized or maximized, so it's already at its normal size.",
                    Arrange.Admin("maximize", "task manager"),
                    "Couldn't snap frozen: it isn't responding right now.",
                    "Moved spotify to the top right corner of screen 3.",
                    "Moved spotify to 1160, 139 on screen 3.",
                    "Resized spotify to 1280 x 880.",
                    "There's no screen 7. Your monitors are the main screen, screen 2 and screen 3.",
                    "discord is already on screen 2.",
                    "Moved spotify to the main screen.",
                    "minecraft isn't open.",
                    "Couldn't resize spotify: huge isn't a size. Use pixels like 1280, a percent like 50%, or +20% to grow it.",
                    "Maximized chrome on screen 3.",
                    "chrome is already maximized on screen 3."
                };
                for (int i = 0; i < expected.Length; i++) if (said[i] != expected[i]) throw new Exception("Her window tool said \"" + said[i] + "\" instead of \"" + expected[i] + "\".");
                Rectangle discordNow = pretend[new IntPtr(31)].Edges, chromeNormal = pretend[new IntPtr(32)].Normal, spotifyNow = pretend[new IntPtr(36)].Edges;
                if (discordNow != new Rectangle(1920, -420, 1080, 936) || chromeNormal != new Rectangle(3000, 0, 1280, 1392) || !pretend[new IntPtr(32)].Maximized || spotifyNow != new Rectangle(934, 86, 853, 587) || pretend[new IntPtr(34)].Maximized)
                    throw new Exception("Her window tool's windows didn't end up where she said: discord " + discordNow + ", chrome " + chromeNormal + ", spotify " + spotifyNow);
                if (!Hands.Failed(said[7]) || !Hands.Failed(said[8]) || !Hands.Failed(said[12]) || !Hands.Failed(said[15]) || Hands.Failed(said[1]) || Hands.Failed(said[0]))
                    throw new Exception("Her window tool's failures didn't read as failures to her brain, or a move that worked did.");
                // A monitor that isn't there, or a size that isn't one, is turned down before a minimized or maximized window comes
                // up or down.
                pretend[new IntPtr(33)].Edges = new Rectangle(-32000, -32000, 160, 28);
                pretend[new IntPtr(33)].Minimized = true;
                string nowhere = Hands.Do("app_window", new ToolArguments { Name = "notepad", Action = "move_to_screen", Screen = "screen 7" });
                string unsized = Hands.Do("app_window", new ToolArguments { Name = "chrome", Action = "resize", Width = "huge" });
                if (nowhere != "There's no screen 7. Your monitors are the main screen, screen 2 and screen 3." || !pretend[new IntPtr(33)].Minimized
                    || !unsized.StartsWith("Couldn't resize chrome: huge isn't a size.", StringComparison.Ordinal) || !pretend[new IntPtr(32)].Maximized)
                    throw new Exception("A move her window tool turned down still brought a window up or down: " + nowhere + " / " + unsized);
                // A snapped half stays a half on the new monitor; anything else keeps its size, scaled for the monitor's scale.
                Rectangle carriedHalf = Arrange.Carried(new Rectangle(1920, -420, 540, 1872), desk[1], desk[2]), carriedBox = Arrange.Carried(new Rectangle(100, 100, 800, 600), desk[0], desk[1]);
                if (carriedHalf != new Rectangle(3000, 0, 1280, 1392) || carriedBox.Width != 800 || carriedBox.Height != 600 || !desk[1].Work.Contains(carriedBox)
                    || Arrange.Measure("1280px", 900, 1920, false) != 1280 || Arrange.Measure("-10%", 1000, 1920, false) != 900 || Arrange.Measure("25%", 40, 1920, true) != 480 || Arrange.Measure("+10%", 40, 1920, true) != 232 || Arrange.Measure("wide", 1, 2, false) != null || Arrange.Measure("", 7, 10, true) != 7
                    || Arrange.Pick("portrait", 0, desk) != 1 || Arrange.Pick("right", 0, desk) != 1 || Arrange.Pick("left", 2, desk) != 1 || Arrange.Pick("main", 2, desk) != 0 || Arrange.Pick("here", 2, desk) != 0 || Arrange.Pick("9", 0, desk) != -1)
                    throw new Exception("Her window tool's monitors or sizes were worked out wrong: " + carriedHalf + " / " + carriedBox);
                // What's open says where their monitors are, and where one app's window is, so a brain can move it by pixels.
                string listed = Hands.ListWindows(null), discordListed = Hands.ListWindows("discord");
                const string monitors = "\nMonitors, left to right: main screen 1920 x 1080; screen 2 1080 x 1920, portrait; screen 3 2560 x 1440.";
                if (!listed.EndsWith(monitors, StringComparison.Ordinal) || discordListed != "Windows of discord, nearest the front first: \"#general | Capri Fans - Discord\" (Discord), on screen 2, 1080 x 936 at 0, 0, in front." + monitors)
                    throw new Exception("list_windows didn't say where their monitors and windows are: " + listed + " / " + discordListed);
                // Her set phrases take the everyday asks straight away, for every brain, only for a window that's open now.
                string[,] heard =
                {
                    { "maximize chrome", "maximize chrome  " }, { "snap discord to the left", "snap_left discord  " }, { "put chrome on the right half of the screen", "snap_right chrome  " },
                    { "put spotify on the bottom half", "snap_bottom spotify  " }, { "send spotify to my other monitor", "move_to_screen spotify next " }, { "move discord to screen 2", "move_to_screen discord 2 " },
                    { "move my notepad window to the portrait monitor", "move_to_screen notepad portrait " }, { "put discord in the top right corner", "move discord  top_right" }, { "center notepad", "move notepad  center" },
                    { "restore discord", "restore discord  " }, { "unminimize notepad", "restore notepad  " }, { "put chrome on the left screen", "move_to_screen chrome left " }, { "snap chrome right", "snap_right chrome  " }
                };
                for (int i = 0; i < heard.GetLength(0); i++)
                {
                    string tool;
                    ToolArguments args;
                    bool direct = Hands.Direct(heard[i, 0], out tool, out args);
                    string got = direct && tool == "app_window" ? args.Action + " " + args.Name + " " + args.Screen + " " + args.Position : (direct ? tool : "nothing");
                    if (got != heard[i, 1]) throw new Exception("\"" + heard[i, 0] + "\" became " + got + " instead of " + heard[i, 1]);
                }
                foreach (string other in new[] { "maximize my potential", "move discord to the top", "maximize it", "restore my files" })
                {
                    string tool;
                    ToolArguments args;
                    if (Hands.Direct(other, out tool, out args) && tool == "app_window") throw new Exception("\"" + other + "\" moved a window: " + args.Action + " " + args.Name);
                }
                string ownTool;
                ToolArguments ownArgs;
                if (!Hands.Direct("move to my other monitor", out ownTool, out ownArgs) || ownTool != "move_yourself" || ownArgs.Action != "other_screen") throw new Exception("\"Move to my other monitor\" stopped being her own move.");
                // Her window tool asks first when Close and switch apps and tabs is off in SECURITY, and her calls are checked.
                Security.Set(Security.Windows, false);
                string held = Security.Gate("app_window", new ToolArguments { Name = "chrome", Action = "maximize" }), heldScreen = Security.Gate("app_window", new ToolArguments { Name = "discord", Action = "move_to_screen", Screen = "2" });
                Security.Drop();
                Security.Set(Security.Windows, true);
                if (held == null || held.IndexOf("\"Maximize chrome?", StringComparison.Ordinal) < 0 || heldScreen == null || heldScreen.IndexOf("Move discord to screen 2?", StringComparison.Ordinal) < 0
                    || Security.Asking("app_window", new ToolArguments { Name = "Discord", Action = "snap_left" }) != "snap Discord to the left half of its screen" || Security.Asking("app_window", new ToolArguments { Name = "Discord", Action = "move_to_screen", Screen = "next" }) != "move Discord to your other screen"
                    || Security.Gate("app_window", new ToolArguments { Name = "chrome", Action = "maximize" }) != null)
                    throw new Exception("Her window tool didn't ask first with Close and switch apps and tabs off: " + held + " / " + heldScreen);
                if (!Hands.Valid("app_window", new ToolArguments { Name = "chrome", Action = "snap_bottom" }) || !Hands.Valid("app_window", new ToolArguments { Name = "chrome", Action = "move", Position = "top right" })
                    || Hands.Valid("app_window", new ToolArguments { Name = "chrome", Action = "move" }) || Hands.Valid("app_window", new ToolArguments { Name = "chrome", Action = "resize" }) || Hands.Valid("app_window", new ToolArguments { Name = "chrome", Action = "move", Position = "upside" })
                    || Hands.Valid("app_window", new ToolArguments { Name = "chrome", Action = "fling" }) || !Hands.Valid("app_window", new ToolArguments { Name = "chrome", Action = "resize", Width = "800" }))
                    throw new Exception("Her window tool's calls weren't checked.");
                // A Mac or Linux kit leaves arranging out of her tools and prompts, and says it's Windows only.
                List<string[]> macGaps = new List<string[]>();
                foreach (string arrange in Arrange.Actions) macGaps.Add(new[] { "app_window:" + arrange, "only on Windows for now" });
                Hands.Kit mac = Hands.KitWithout(macGaps), kit = Hands.KitFor();
                if (!mac.Tools.Contains("\"enum\":[\"switch_to\",\"minimize\",\"close\"]") || mac.Tools.Contains("\"maximize\"") || mac.Tools.Contains("\"position\"") || mac.Tools.Contains("Or arrange its window")
                    || mac.Unable("app_window", new ToolArguments { Name = "chrome", Action = "snap_left" }) != Hands.ArrangeLine || mac.Unable("app_window", new ToolArguments { Name = "chrome", Action = "close" }) != null
                    || !mac.Brief.Contains("\n- app_window: switch to, minimize or close their windows") || !Hands.NoteFor(mac).Contains("switch to, minimize or close apps that are open, play songs") || !Hands.PromptFor(mac).Contains("switch to, minimize or close an app, or look")
                    || !mac.NotHere.Contains("maximizing, snapping, moving and resizing their windows") || !mac.Abilities.Contains("switch to, minimize or close your apps and browser tabs")
                    || !kit.Tools.Contains("\"move_to_screen\"") || !kit.Brief.Contains("\n- app_window: switch to, minimize, maximize, snap, move, resize or close their windows") || !kit.WorkTools.Contains("their monitors named left to right") || !kit.WorkTools.Contains("\"position\"") || kit.Unable("app_window", new ToolArguments { Name = "chrome", Action = "snap_left" }) != null)
                    throw new Exception("Arranging windows reached a Mac or Linux kit, or left the Windows one: " + mac.NotHere);
                // Her voice chat gets the same moves with every parameter described, and an agent tuned before them is tuned again.
                string voiced = null;
                for (int i = 0; i < Hands.Count; i++) if (Hands.Name(i) == "app_window") voiced = Hands.ElevenLabsTool(i);
                if (voiced == null || !voiced.Contains("\"move_to_screen\"") || !voiced.Contains("\"position\"") || !voiced.Contains("Or arrange its window")
                    || System.Text.RegularExpressions.Regex.IsMatch(voiced, "\"\\w+\":\\{\"type\":\"(?:string|number|integer|boolean)\"(?![^{}]*\"description\":\"[^\"])") || !Agent.Stale(32, Brain.PersonalityStamp))
                    throw new Exception("Her voice chat's window tool is missing its moves, or her agent wouldn't be tuned again for them: " + voiced);
                // She looks at the monitor they name, from the one they're on, and says which one she saw.
                string[,] looks = { { "2", "screen 2" }, { "portrait", "screen 2" }, { "right", "screen 2" }, { "main", "the main screen" }, { "third", "screen 3" }, { "next", "screen 2" } };
                for (int i = 0; i < looks.GetLength(0); i++)
                {
                    Rectangle area;
                    string named, missing = Arrange.LookArea(looks[i, 0], IntPtr.Zero, out area, out named);
                    if (missing != null || named != looks[i, 1] || area != desk.Find(delegate(Arrange.Display one) { return one.Name == looks[i, 1].Replace("the ", ""); }).Bounds)
                        throw new Exception("Looking at the " + looks[i, 0] + " monitor found " + named + " " + area + " / " + missing);
                }
                Rectangle none;
                string noName, absentScreen = Arrange.LookArea("5", IntPtr.Zero, out none, out noName);
                if (absentScreen != "There's no screen 5. Your monitors are the main screen, screen 2 and screen 3." || noName != null || !none.IsEmpty)
                    throw new Exception("A monitor that isn't there wasn't said: " + absentScreen);
                string[,] lookAsks = { { "what's on my second monitor", "second" }, { "look at my left screen", "left" }, { "check the portrait one", "portrait" }, { "can you look at my other monitor", "next" }, { "look at my screen", "" } };
                for (int i = 0; i < lookAsks.GetLength(0); i++)
                {
                    string tool;
                    ToolArguments args;
                    if (!Hands.Heard(lookAsks[i, 0], out tool, out args) || tool != "look_at_screen" || (args.Screen ?? "") != lookAsks[i, 1])
                        throw new Exception("\"" + lookAsks[i, 0] + "\" didn't look at the monitor they named: " + tool + " " + (args == null ? "" : args.Screen));
                }
                List<string> looked = new List<string>();
                Hands.Look = delegate(string focus) { looked.Add("here"); return "You looked at their screen."; };
                Hands.LookOn = delegate(string focus, string which) { looked.Add(which); return "You looked at screen 2."; };
                Hands.Do("look_at_screen", new ToolArguments { Screen = "left" });
                Hands.Do("look_at_screen", new ToolArguments { Focus = "the error" });
                if (string.Join(",", looked.ToArray()) != "left,here" || !kit.WorkTools.Contains("\"screen\":{\"type\":\"string\",\"description\":\"Another monitor to look at") || !kit.WorkTools.Contains("It looks at the monitor they're using, or the one they name."))
                    throw new Exception("Her look didn't go to the monitor they named: " + string.Join(",", looked.ToArray()));
                // She keeps a window on top of the others and lets it go, read back from the window, and an admin one says why not.
                Arrange.Pinned = delegate(IntPtr handle) { return pretend[handle].Pinned; };
                Arrange.Pin = delegate(IntPtr handle, bool pin) { if (pretend[handle].Admin) return false; pretend[handle].Pinned = pin; return true; };
                string pinned = Hands.Do("app_window", new ToolArguments { Name = "discord", Action = "pin_on_top" }), pinnedAgain = Hands.Do("app_window", new ToolArguments { Name = "discord", Action = "pin_on_top" });
                bool kept = pretend[new IntPtr(31)].Pinned;
                string unpinned = Hands.Do("app_window", new ToolArguments { Name = "discord", Action = "unpin" }), unpinnedAgain = Hands.Do("app_window", new ToolArguments { Name = "discord", Action = "unpin" });
                string adminPin = Hands.Do("app_window", new ToolArguments { Name = "task manager", Action = "pin_on_top" }), frozenPin = Hands.Do("app_window", new ToolArguments { Name = "frozen", Action = "pin_on_top" });
                if (pinned != "Kept discord on top of your other windows. Say unpin discord to let it go." || !kept || pinnedAgain != "discord is already kept on top of your other windows."
                    || unpinned != "discord isn't kept on top any more." || pretend[new IntPtr(31)].Pinned || unpinnedAgain != "discord isn't kept on top, so it's already in among your other windows."
                    || adminPin != Arrange.Admin("pin", "task manager") || frozenPin != "Couldn't pin frozen: it isn't responding right now."
                    || !Hands.Failed(adminPin) || Hands.Failed(pinned) || Security.Asking("app_window", new ToolArguments { Name = "Discord", Action = "pin_on_top" }) != "keep Discord on top of your other windows")
                    throw new Exception("Her window tool didn't keep a window on top or let it go: " + pinned + " / " + pinnedAgain + " / " + unpinned + " / " + unpinnedAgain + " / " + adminPin + " / " + frozenPin);
                string[,] pinAsks = { { "keep discord on top", "pin_on_top" }, { "pin chrome", "pin_on_top" }, { "keep spotify on top of everything", "pin_on_top" }, { "unpin discord", "unpin" }, { "stop keeping discord on top", "unpin" } };
                for (int i = 0; i < pinAsks.GetLength(0); i++)
                {
                    string tool;
                    ToolArguments args;
                    if (!Hands.Heard(pinAsks[i, 0], out tool, out args) || tool != "app_window" || args.Action != pinAsks[i, 1])
                        throw new Exception("\"" + pinAsks[i, 0] + "\" didn't " + pinAsks[i, 1] + ": " + tool + " " + (args == null ? "" : args.Action));
                }
                string unknownPin;
                ToolArguments unknownArgs;
                if (Hands.Heard("keep yourself on top", out unknownPin, out unknownArgs) && unknownPin == "app_window") throw new Exception("\"Keep yourself on top\" went to someone else's window.");
                // A brain that works jobs out checks where the window is, moves it, and hears where it really ended up.
                List<string> asked = new List<string>();
                Func<string, string> says = delegate(string text) { return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":" + Voice.Quote(text) + "}}]}"; };
                Func<string, string, string> calls = delegate(string name, string json) { return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"" + name + "\",\"arguments\":" + Voice.Quote(json) + "}}]}}]}"; };
                pretend[new IntPtr(31)].Edges = new Rectangle(1990, -220, 940, 800);
                Brain.OpenAi = FakeServer(new[] { calls("list_windows", "{\"name\":\"discord\"}"), calls("app_window", "{\"name\":\"discord\",\"action\":\"snap_left\",\"screen\":\"portrait\"}"), says("Discord's on the left of your portrait screen, as narrow as it goes.") }, asked, out fake);
                Brain.MemoryPath = Brain.MemoriesPath = Brain.TranscriptPath = Brain.LogPath = null;
                Brain.Keeping = false;
                List<string> did = new List<string>();
                Brain planner = new Brain { Model = "gpt-5-mini", Remote = new Cloud("sk-proj-test", "gpt-5-mini"), OwnSteps = true };
                planner.Act = delegate(string name, ToolArguments args) { did.Add(name + ":" + args.Name + ":" + args.Action); return Hands.Do(name, args); };
                string reply = planner.Ask("put discord on the left half of my vertical monitor");
                string[] sent;
                lock (asked) sent = asked.ToArray();
                if (reply != "Discord's on the left of your portrait screen, as narrow as it goes." || sent.Length != 3 || string.Join(",", did.ToArray()) != "list_windows:discord:,app_window:discord:snap_left"
                    || !sent[0].Contains("\"move_to_screen\"") || !sent[0].Contains("To arrange a window, use app_window") || !sent[1].Contains("Monitors, left to right: main screen 1920 x 1080; screen 2 1080 x 1920, portrait")
                    || !sent[1].Contains("940 x 800 at 70, 200") || !sent[2].Contains("Snapped discord to the left half of screen 2, but it stayed 940 x 1872: that's as small as it goes."))
                    throw new Exception("Her planner didn't check where the window was, or didn't hear where it really went: " + reply + " / " + string.Join(",", did.ToArray()) + " / " + sent.Length + " calls");
                checks.Add("PASS her window tool on three monitors, one portrait and one at 150%: snaps left, right, top and bottom, maximizes on the monitor they name, restores from maximized or minimized, sends a window to the next, numbered or portrait monitor keeping a snapped half a half and scaling the rest, moves to a corner, to pixels or by a nudge, and resizes by pixels or percent, all read back afterwards; it says when an app won't go smaller and slides it back on screen, when a window runs as admin, isn't responding, isn't open, or is already there, and names the monitors when one doesn't exist; list_windows names their monitors and where one app's window is; everyday asks like \"snap discord to the left\" or \"send spotify to my other monitor\" work for every brain; it asks first with Close and switch apps and tabs off; Mac and Linux kits leave it out and say it's Windows only; her voice chat gets the same moves, every parameter described, and her agent is tuned again for them; she keeps a window on top of the others and lets it go, like \"keep discord on top\"; she looks at the monitor they name, like \"what's on my second monitor\" or \"check the portrait one\", and says when there's no such monitor; a planner checks where the window is before moving it and hears where it ended up");
            }
            finally
            {
                Arrange.Displays = displaysWas;
                Arrange.Read = readWas;
                Arrange.Shape = shapeWas;
                Arrange.Raised = raisedWas;
                Arrange.Hung = hungWas;
                Arrange.Mouse = mouseWas;
                Arrange.Pinned = pinnedWas;
                Arrange.Pin = pinWas;
                Arrange.Patience = patienceWas;
                Hands.Windows = windowsWas;
                Hands.Foreground = frontWas;
                Hands.Unlisted = unlistedWas;
                Hands.Minimized = minimizedWas;
                Hands.Gamey = gameyWas;
                Hands.ScreenOf = screenWas;
                Hands.Look = lookWas;
                Hands.LookOn = lookOnWas;
                Hands.Steer = steerWas;
                Hands.Running = runningWas;
                Security.Drop();
                Security.Set(Security.Windows, windowsOn);
                Brain.OpenAi = openAiWas;
                Brain.MemoryPath = notesWas;
                Brain.MemoriesPath = memoriesWas;
                Brain.TranscriptPath = transcriptsWas;
                Brain.LogPath = logWas;
                Brain.Keeping = keepingWas;
                if (fake != null) fake.Stop();
            }
        }

        // Her window tool on a real window of her own, on whatever desk the smoke test runs on: snapped, maximized, restored, centred,
        // resized and sent to another monitor through Windows itself, and read back.
        private static void SmokeWindowReal(List<string> checks)
        {
            Form pane = null;
            ManualResetEvent shown = new ManualResetEvent(false);
            Rectangle work = Displays.Home.WorkingArea;
            Thread owner = new Thread(delegate()
            {
                pane = new Form { Text = "Capri window check", StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(work.Left + 60, work.Top + 60, 640, 480), ShowInTaskbar = false };
                pane.Shown += delegate { shown.Set(); };
                Application.Run(pane);
            });
            owner.SetApartmentState(ApartmentState.STA);
            owner.IsBackground = true;
            owner.Start();
            try
            {
                if (!shown.WaitOne(10000)) throw new Exception("Her window check's own window never showed.");
                IntPtr handle = (IntPtr)pane.Invoke(new Func<IntPtr>(delegate { return pane.Handle; }));
                OpenWindow window = new OpenWindow(handle, "Capri window check", "Capri");
                List<Arrange.Display> screens = Arrange.Displays();
                // The screen her checks run on (Displays.Home), which is where the check window opens.
                Rectangle home = Displays.Home.Bounds;
                int main = screens.FindIndex(screen => screen.Bounds == home);
                if (main < 0) main = screens.FindIndex(screen => screen.Primary);
                Func<string, ToolArguments> asks = delegate(string action) { return new ToolArguments { Name = "the check window", Action = action }; };
                string snapped = Arrange.Do(window, "the check window", asks("snap_left"));
                Rectangle left = Arrange.Read(handle).Edges, half = Arrange.Half(screens[main].Work, "left");
                string maximized = Arrange.Do(window, "the check window", asks("maximize"));
                bool zoomed = (bool)pane.Invoke(new Func<bool>(delegate { return pane.WindowState == FormWindowState.Maximized; }));
                string restored = Arrange.Do(window, "the check window", asks("restore"));
                bool normal = (bool)pane.Invoke(new Func<bool>(delegate { return pane.WindowState == FormWindowState.Normal; }));
                string centred = Arrange.Do(window, "the check window", new ToolArguments { Name = "the check window", Action = "move", Position = "center" });
                string resized = Arrange.Do(window, "the check window", new ToolArguments { Name = "the check window", Action = "resize", Width = "500", Height = "400" });
                Rectangle sized = Arrange.Read(handle).Edges;
                if (!snapped.StartsWith("Snapped the check window to the left half of ", StringComparison.Ordinal) || Math.Abs(left.Left - half.Left) > 8 || Math.Abs(left.Width - half.Width) > 16 || Math.Abs(left.Height - half.Height) > 16
                    || maximized != "Maximized the check window" + (screens.Count > 1 ? " on " + (screens[main].Primary ? "the main screen" : screens[main].Name) + "." : ".") || !zoomed || restored != "Restored the check window from maximized to its normal size." || !normal
                    || !centred.StartsWith("Moved the check window to the middle of ", StringComparison.Ordinal) || resized != "Resized the check window to 500 x 400." || Math.Abs(sized.Width - 500) > 4 || Math.Abs(sized.Height - 400) > 4)
                    throw new Exception("Her window tool didn't move a real window: " + snapped + " " + left + " / " + maximized + " " + zoomed + " / " + restored + " / " + centred + " / " + resized + " " + sized);
                string sent = screens.Count > 1 ? Arrange.Do(window, "the check window", asks("move_to_screen")) : null;
                if (sent != null && (!sent.StartsWith("Moved the check window to ", StringComparison.Ordinal) || Arrange.Under(Arrange.Read(handle).Edges, screens) == main))
                    throw new Exception("Her window tool didn't send a real window to another monitor: " + sent);
                checks.Add("PASS her window tool moves a real window through Windows: snapped to the left half, maximized, restored, centred, resized to 500 x 400" + (sent != null ? " and sent to another monitor" : "") + ", each read back from the window itself");
                checks.Add("LIMIT her window tool on their own desk: snap, maximize and send Discord, Chrome and a game between three monitors, one portrait and one at another scale, and ask her to move Task Manager to hear that it runs as admin");
            }
            finally
            {
                if (pane != null && pane.IsHandleCreated) pane.BeginInvoke(new MethodInvoker(delegate { pane.Close(); }));
                owner.Join(5000);
            }
        }

        // Everyday asks said truthfully on a pretend player and desk: pause never starts music and play never stops it,
        // a skip names its song, any player is covered, a tray app is open, a timer is a reminder on every brain
        // (Lachy).
        private void SmokeEverydayAsks(List<string> checks)
        {
            Func<List<Tune>> sessionsWas = Hands.Sessions;
            Func<Tune, string, bool> sendWas = Hands.SessionPress;
            Func<List<OpenWindow>> windowsWas = Hands.Windows;
            Func<string> unlistedWas = Hands.Unlisted, nowPlayingWas = Hands.NowPlaying;
            Func<string, string, bool> runningWas = Hands.Running;
            Action<byte> keysWas = Hands.Press;
            string openAiWas = Brain.OpenAi, notesWas = Brain.MemoryPath, memoriesWas = Brain.MemoriesPath, transcriptsWas = Brain.TranscriptPath, logWas = Brain.LogPath;
            bool keepingWas = Brain.Keeping;
            TcpListener fake = null;
            try
            {
                string app = "Spotify.exe", title = "Consume", artist = "Chase Atlantic";
                bool playing = true, stuck = false, present = true;
                List<string> sent = new List<string>();
                List<byte> keys = new List<byte>();
                // Each read is a fresh copy, the way Windows' sessions are read again after a press.
                Hands.Sessions = delegate
                {
                    List<Tune> tunes = new List<Tune>();
                    if (present) tunes.Add(new Tune { App = app, Title = title, Artist = artist, Playing = playing });
                    return tunes;
                };
                Hands.SessionPress = delegate(Tune tune, string what)
                {
                    sent.Add(what);
                    if (stuck) return true;
                    if (what == "pause" || what == "play") playing = what == "play";
                    else title = what == "next" ? "Swim" : "Consume";
                    return true;
                };
                Hands.Press = delegate(byte key) { keys.Add(key); };
                Func<string, string> media = delegate(string action) { return Hands.Do("media", new ToolArguments { Action = action }); };
                string[,] asks =
                {
                    { "now_playing", "Spotify is playing Consume by Chase Atlantic." }, { "pause", "Paused Spotify." },
                    { "pause", "Nothing's playing: Spotify is already paused." }, { "now_playing", "Spotify is paused on Consume by Chase Atlantic." },
                    { "resume", "Playing now." }, { "resume", "Spotify is already playing." },
                    { "play_pause", "Paused Spotify." }, { "play_pause", "Playing now." },
                    { "next", "Skipped to the next track." }, { "previous", "Went back a track." }
                };
                List<string> wrong = new List<string>();
                for (int i = 0; i < asks.GetLength(0); i++)
                {
                    string got = media(asks[i, 0]);
                    if (got != asks[i, 1]) wrong.Add(asks[i, 0] + " said \"" + got + "\"");
                }
                if (wrong.Count > 0 || string.Join(",", sent.ToArray()) != "pause,play,pause,play,next,previous" || keys.Count != 0)
                    throw new Exception("Her media tool didn't go by what's playing: " + string.Join(" / ", wrong.ToArray()) + " / pressed " + string.Join(",", sent.ToArray()));
                // A player that ignores the press is said plainly, a browser tab is named by its browser, and with no player to go by
                // she presses the key the way she always has.
                stuck = true;
                string ignored = media("pause");
                stuck = false;
                app = "Chrome";
                title = "lofi hip hop radio";
                artist = "Lofi Girl";
                string tab = media("now_playing");
                present = false;
                Hands.NowPlaying = delegate { return ""; };
                string blind = media("pause");
                if (ignored != "Couldn't pause Spotify: it still says it's playing." || !Hands.Failed(ignored) || tab != "Chrome is playing lofi hip hop radio by Lofi Girl." || blind != "Pressed pause." || keys.Count != 1 || keys[0] != 0xB3)
                    throw new Exception("Her media tool didn't say what the player did: " + ignored + " / " + tab + " / " + blind);
                // A brain's play_pause for "pause it" only pauses, and for "unpause it" only plays. An ask that goes both ways keeps
                // each call as the brain wrote it.
                string[,] aims = { { "pause the music", "pause" }, { "can you unpause it", "resume" }, { "un-pause it", "resume" }, { "resume my song", "resume" }, { "stop the music", "pause" }, { "play it again", "resume" }, { "play/pause", "play_pause" }, { "hit play pause", "play_pause" }, { "pause the music for ten seconds and then resume it", "play_pause" } };
                for (int i = 0; i < aims.GetLength(0); i++)
                {
                    ToolArguments aimed = new ToolArguments { Action = "play_pause" };
                    Hands.Aim(aimed, aims[i, 0]);
                    if (aimed.Action != aims[i, 1]) throw new Exception("\"" + aims[i, 0] + "\" aimed play_pause at " + aimed.Action);
                }
                // An app running in the tray with no window isn't "not open".
                Hands.Windows = delegate { return new List<OpenWindow> { new OpenWindow(new IntPtr(41), "notes.txt - Notepad", "notepad") }; };
                Hands.Unlisted = delegate { return null; };
                Hands.Running = delegate(string name, string id) { return name == "discord"; };
                string closed = Hands.Do("app_window", new ToolArguments { Name = "Discord", Action = "close" }), front = Hands.Do("app_window", new ToolArguments { Name = "discord", Action = "switch_to" });
                string snapped = Hands.Do("app_window", new ToolArguments { Name = "discord", Action = "snap_left" }), absent = Hands.Do("app_window", new ToolArguments { Name = "minecraft", Action = "close" });
                if (closed != "Couldn't close Discord: it has no window open, but it's running in the background, maybe in the tray. Quit it from its tray icon."
                    || front != "Couldn't switch to discord: it has no window open, but it's running in the background, maybe in the tray. Opening it should bring its window back."
                    || snapped != "Couldn't snap discord: it has no window open, but it's running in the background, maybe in the tray. Opening it should bring its window back."
                    || absent != "minecraft isn't open." || !Hands.Failed(closed) || !Hands.Failed(front) || !Hands.Failed(absent))
                    throw new Exception("An app in the tray was called not open: " + closed + " / " + front + " / " + snapped + " / " + absent);
                // Minimizing an app that's only in the tray is already done, not a failure.
                string tucked = Hands.Do("app_window", new ToolArguments { Name = "discord", Action = "minimize" });
                if (tucked != "discord has no window up: it's running in the background, maybe in the tray." || Hands.Failed(tucked))
                    throw new Exception("Minimizing an app in the tray failed: " + tucked);
                // A timer is a reminder that says it's done, set straight away on every brain, seconds too.
                string[,] timers =
                {
                    { "set a timer for 10 minutes", "your 10 minute timer is done", "in 10 minutes" }, { "timer 30 seconds", "your 30 second timer is done", "in 30 seconds" },
                    { "start a 5 minute timer", "your 5 minute timer is done", "in 5 minute" }, { "set me a timer for an hour", "your 1 hour timer is done", "in an hour" }
                };
                for (int i = 0; i < timers.GetLength(0); i++)
                {
                    string tool;
                    ToolArguments args;
                    if (!Hands.Heard(timers[i, 0], out tool, out args) || tool != "remind" || args.Text != timers[i, 1] || args.When != timers[i, 2])
                        throw new Exception("\"" + timers[i, 0] + "\" didn't set a timer: " + tool + " " + (args == null ? "" : args.Text + " / " + args.When));
                }
                foreach (string loose in new[] { "set a timer", "set a timer for my pizza", "what's a timer" })
                {
                    string tool;
                    ToolArguments args;
                    if (Hands.Heard(loose, out tool, out args) && tool == "remind") throw new Exception("\"" + loose + "\" set a timer.");
                }
                // Cancelling one goes by the words its reminder says, "10 mins" or "10 minutes" alike.
                string[,] stops = { { "cancel my timer", "timer" }, { "stop the 10 minute timer", "10 minute timer" }, { "cancel the 10 min timer", "10 minute timer" }, { "stop the timer", "timer" } };
                for (int i = 0; i < stops.GetLength(0); i++)
                {
                    string tool;
                    ToolArguments args;
                    if (!Hands.Heard(stops[i, 0], out tool, out args) || tool != "remind" || args.Action != "cancel" || args.Text != stops[i, 1])
                        throw new Exception("\"" + stops[i, 0] + "\" didn't cancel a timer: " + tool + " " + (args == null ? "" : args.Action + " / " + args.Text));
                }
                string minced;
                ToolArguments mincedArgs;
                if (!Hands.Heard("set a timer for 10 mins", out minced, out mincedArgs) || mincedArgs.Text != "your 10 minute timer is done")
                    throw new Exception("A 10 min timer isn't called a 10 minute one: " + (mincedArgs == null ? "" : mincedArgs.Text));
                DateTime clock = new DateTime(2026, 11, 3, 1, 0, 0), due;
                if (!Reminders.Parse("in 30 seconds", clock, out due) || (due - clock).TotalSeconds != 30 || !Reminders.Parse("in 2 hours", clock, out due) || (due - clock).TotalHours != 2)
                    throw new Exception("A timer in seconds wasn't understood.");
                // A brain that works jobs out pauses only when something plays, and hears what the player said. The song is
                // named only when they ask what is playing (8aeebdc), so the paused line leaves it out.
                List<string> asked = new List<string>();
                Func<string, string> says = delegate(string text) { return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":" + Voice.Quote(text) + "}}]}"; };
                Func<string, string, string> calls = delegate(string name, string json) { return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"" + name + "\",\"arguments\":" + Voice.Quote(json) + "}}]}}]}"; };
                app = "Spotify.exe";
                title = "Consume";
                artist = "Chase Atlantic";
                playing = false;
                present = true;
                sent.Clear();
                Brain.OpenAi = FakeServer(new[] { calls("media", "{\"action\":\"play_pause\"}"), says("It's already paused, choom.") }, asked, out fake);
                Brain.MemoryPath = Brain.MemoriesPath = Brain.TranscriptPath = Brain.LogPath = null;
                Brain.Keeping = false;
                List<string> did = new List<string>();
                Brain planner = new Brain { Model = "gpt-5-mini", Remote = new Cloud("sk-proj-test", "gpt-5-mini"), OwnSteps = true };
                planner.Act = delegate(string name, ToolArguments args) { did.Add(name + ":" + args.Action); return Hands.Do(name, args); };
                string reply = planner.Ask("ugh pause whatever's playing, I'm about to jump on a call");
                string[] heard;
                lock (asked) heard = asked.ToArray();
                if (reply != "It's already paused, choom." || string.Join(",", did.ToArray()) != "media:pause" || sent.Count != 0 || heard.Length != 2
                    || !heard[0].Contains("A timer is a reminder") || !heard[0].Contains("pausing never starts the music") || !heard[1].Contains("Nothing's playing: Spotify is already paused.") || heard[1].Contains("Consume"))
                    throw new Exception("Her planner's pause flipped the music or didn't hear the player: " + reply + " / " + string.Join(",", did.ToArray()) + " / " + heard.Length + " calls");
                checks.Add("PASS everyday asks go by what's there: pause only pauses and play only plays by what Windows says is playing, a player that ignores the press is said plainly, next and previous name the song they landed on, what's playing covers Spotify, a browser tab or anything else, a brain's play_pause for pause or unpause can't flip the music the wrong way, with no player to read the keys work as before; an app in the tray says so instead of not open, and minimizing one is already done; set a timer for 10 minutes, 30 seconds or a 5 minute timer is a reminder on every brain, and cancel my timer or stop the 10 min timer cancels it");
                checks.Add("LIMIT pause, play and skip on their own players: Spotify, a YouTube tab and a game's music each report through Windows' media session in their own way");
            }
            finally
            {
                Hands.Sessions = sessionsWas;
                Hands.SessionPress = sendWas;
                Hands.Windows = windowsWas;
                Hands.Unlisted = unlistedWas;
                Hands.NowPlaying = nowPlayingWas;
                Hands.Running = runningWas;
                Hands.Press = keysWas;
                Brain.OpenAi = openAiWas;
                Brain.MemoryPath = notesWas;
                Brain.MemoriesPath = memoriesWas;
                Brain.TranscriptPath = transcriptsWas;
                Brain.LogPath = logWas;
                Brain.Keeping = keepingWas;
                if (fake != null) fake.Stop();
            }
        }

        // A question she asked, answered: when her brain's turn ends without calling remember, the answer is written
        // down from what they said.
        private void SmokeProbeAnswers(List<string> checks)
        {
            Func<string, string> thinkWas = Think;
            Action<long> keepWas = Probes.Keep;
            string notesWas = Brain.MemoryPath, logWas = Brain.LogPath;
            bool silentWas = preferences.Silent;
            string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "capri-probes-" + Guid.NewGuid().ToString("N"));
            try
            {
                System.IO.Directory.CreateDirectory(folder);
                Brain.MemoryPath = System.IO.Path.Combine(folder, "me.md");
                Brain.LogPath = System.IO.Path.Combine(folder, "errors.txt");
                // Her next question's time is not pushed about by a check.
                Probes.Keep = delegate { };
                Probes.Asked = delegate { };
                Feelings.Keep = delegate { };
                preferences.Silent = true;
                CloseTalk();

                // Her brain replies and never calls remember, which is the case that lost the answer.
                Think = delegate { return "Noted, choom."; };
                Probes.Said("Am I about right for you, or do you want me different?", "personalityLikes");
                Hear("be a bit more dominant");
                WaitedForReply();
                string kept = System.IO.File.Exists(Brain.MemoryPath) ? System.IO.File.ReadAllText(Brain.MemoryPath) : "";
                if (kept.IndexOf("personalityLikes=be a bit more dominant", StringComparison.Ordinal) < 0 || Probes.Pending != null || Probes.PendingKey != null)
                    throw new Exception("Their answer to her own question was not written down: notes [" + kept.Replace("\r\n", " ") + "], still waiting on " + (Probes.Pending ?? "nothing"));
                CloseTalk();

                // A reply her brain tidies replaces an hours line rather than joining it: three answers appended read
                // "nothing at the moment, That was working, all over the place".
                distilTest = delegate(string key, string had, string said) { return key == "workHours" ? "rotating shifts" : null; };
                try
                {
                    Brain.NoteKey("workHours", "Uh nothing at the moment", true);
                    Probes.Said("Are your work hours steady, or all over the place?", "workHours");
                    Hear("uh it's like rotating shifts, nights one week then days");
                    WaitedForReply();
                    kept = System.IO.File.ReadAllText(Brain.MemoryPath);
                    if (kept.IndexOf("workHours=rotating shifts", StringComparison.Ordinal) < 0 || kept.IndexOf("nothing at the moment", StringComparison.OrdinalIgnoreCase) >= 0)
                        throw new Exception("A tidied answer did not replace the hours line: notes [" + kept.Replace("\r\n", " ") + "]");
                    if (!Probes.Irregular("rotating shifts") || Probes.Irregular("9 to 5 weekdays")) throw new Exception("A moving schedule was told apart wrongly.");
                }
                finally { distilTest = null; }
                CloseTalk();

                // Small talk: back from a night is "how did you sleep", once a day, and it is never filed.
                Func<DateTime, string> backWas = Probes.Back;
                Func<string, double, bool> freshWas = Probes.Fresh;
                try
                {
                    System.Collections.Generic.HashSet<string> asked = new System.Collections.Generic.HashSet<string>();
                    Probes.Fresh = delegate(string kind, double hours) { return asked.Add(kind); };
                    Probes.Back = delegate { return "slept"; };
                    string chat = Probes.Chat(new DateTime(2026, 11, 20, 9, 0, 0));
                    if (chat == null || chat.IndexOf("sleep", StringComparison.OrdinalIgnoreCase) < 0 && chat.IndexOf("rest", StringComparison.OrdinalIgnoreCase) < 0 && chat.IndexOf("head", StringComparison.OrdinalIgnoreCase) < 0)
                        throw new Exception("Back from a night did not ask how they slept: " + (chat ?? "nothing"));
                    string twice = Probes.Chat(new DateTime(2026, 11, 20, 9, 30, 0));
                    if (twice != null) throw new Exception("Small talk asked twice in a morning: " + twice);
                    Probes.Back = delegate { return null; };
                    string evening = Probes.Chat(new DateTime(2026, 11, 20, 19, 0, 0));
                    if (evening == null) throw new Exception("An evening had no small talk to ask.");
                }
                finally { Probes.Back = backWas; Probes.Fresh = freshWas; }

                // Their mood: a rough stretch makes her gentler and gets one "you doing alright?", and only a number is kept.
                try
                {
                    Feelings.Set(0);
                    Feelings.Heard("ugh, long day, I'm exhausted and stressed");
                    Feelings.Heard("honestly a bad day, I'm so tired");
                    string low = Feelings.Line();
                    if (low == null || low.IndexOf("low", StringComparison.Ordinal) < 0) throw new Exception("A rough stretch didn't reach her brain as one: " + (low ?? "nothing"));
                    Feelings.Set(0);
                    Feelings.Heard("what's the weather");
                    if (Feelings.Line() != null) throw new Exception("An ordinary line moved their mood.");
                }
                finally { Feelings.Set(0); }

                // A brush-off tells her nothing, so nothing is written and the question is over: left pending, whatever
                // they said next would have been filed as the answer to it.
                Probes.Said("What have you been playing lately?", "gamesUserLikes");
                Hear("nah");
                WaitedForReply();
                kept = System.IO.File.ReadAllText(Brain.MemoryPath);
                // Her notes ship with a line for every key she can fill, so the name is always in the file. What
                // says whether anything was kept is the line having something after the equals.
                if (Probes.Filled(kept, "gamesUserLikes") || Probes.Pending != null)
                    throw new Exception("A brush-off was filed as an answer or left her waiting: notes [" + kept.Replace("\r\n", " ") + "]");
                CloseTalk();

                // A brain that does call remember is the only one that writes, and its own words are what is kept.
                Probes.Said("Anything good in your games list right now?", "gamesUserLikes");
                if (Hands.Do("remember", new ToolArguments { Key = "gamesUserLikes", Text = "Hollow Knight" }) != "" || Probes.Pending != null)
                    throw new Exception("Her brain's own remember said something back or left the question pending.");
                Hear("silksong, mostly");
                WaitedForReply();
                kept = System.IO.File.ReadAllText(Brain.MemoryPath);
                if (kept.IndexOf("gamesUserLikes=Hollow Knight", StringComparison.Ordinal) < 0 || kept.IndexOf("silksong", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new Exception("An answer her brain had already saved was written over or added to: notes [" + kept.Replace("\r\n", " ") + "]");
                CloseTalk();

                // A job for her hands is not an answer, so the question stands and nothing is filed.
                Func<string, KeyValuePair<string, ToolArguments>> planWas = Hands.Plan;
                Probes.Said("What's open on your screen most of the day?", "programsUserLikes");
                try
                {
                    Hands.Plan = delegate { return new KeyValuePair<string, ToolArguments>("animate", new ToolArguments { Name = "blink" }); };
                    Hear("blink for me");
                    WaitedForReply();
                }
                finally { Hands.Plan = planWas; }
                kept = System.IO.File.ReadAllText(Brain.MemoryPath);
                if (Probes.Filled(kept, "programsUserLikes") || Probes.Pending == null)
                    throw new Exception("A job for her hands was filed as an answer to her question: notes [" + kept.Replace("\r\n", " ") + "]");
                Probes.Answered("programsUserLikes");
                CloseTalk();

                // How long her bubble waits for the answer, in seconds, while the two settings beside it stay hours.
                string dynamicWas = Dynamics.Kept;
                try
                {
                    string dynamic = System.IO.Path.Combine(folder, "dynamic.md");
                    System.IO.File.WriteAllText(dynamic, "# Dynamic\r\n\r\nbase: Devoted\r\nprobe min: 1h\r\nprobe max: 2h\r\nprobe wait: 45\r\n", new System.Text.UTF8Encoding(false));
                    Dynamics.Kept = dynamic;
                    if (Math.Abs(Dynamics.ProbeWait - 45) > 0.01 || Math.Abs(Dynamics.ProbeLow - 1) > 0.01 || Math.Abs(Dynamics.ProbeHigh - 2) > 0.01)
                        throw new Exception("probe wait was not read as seconds, or it changed the hours beside it: wait " + Dynamics.ProbeWait + ", min " + Dynamics.ProbeLow + ", max " + Dynamics.ProbeHigh);
                    // Out of range either way, or unreadable, leaves something sensible. The two writes differ in
                    // length, since she re-reads on a change of time or size and two same-size writes in one
                    // millisecond look unchanged.
                    System.IO.File.WriteAllText(dynamic, "# Dynamic\r\n\r\nprobe wait: 9000\r\n", new System.Text.UTF8Encoding(false));
                    double capped = Dynamics.ProbeWait;
                    System.IO.File.WriteAllText(dynamic, "# Dynamic\r\n\r\nprobe wait: not a time at all\r\n", new System.Text.UTF8Encoding(false));
                    if (capped != 300 || Math.Abs(Dynamics.ProbeWait - 90) > 0.01)
                        throw new Exception("probe wait out of range or unreadable did not fall back: capped " + capped + ", unreadable " + Dynamics.ProbeWait);
                }
                finally { Dynamics.Kept = dynamicWas; }

                // What they said never leaves their own notes file, and what went in is in errors.txt to be found later.
                string logged = System.IO.File.Exists(Brain.LogPath) ? System.IO.File.ReadAllText(Brain.LogPath) : "";
                if (logged.IndexOf("personalityLikes", StringComparison.Ordinal) < 0 || logged.IndexOf("be a bit more dominant", StringComparison.Ordinal) >= 0)
                    throw new Exception("The line about keeping their answer is missing, or it repeated what they said.");
                checks.Add("PASS the answer to a question she asked is kept: a brain that replies without calling remember still gets their words into me.md under her question's key, a brush-off writes nothing and closes the question, a brain that does call remember keeps its own words with nothing written over them, a job for her hands is not an answer and leaves the question standing, a tidied hours answer replaces the line rather than joining it, small talk fits the morning and evening and is asked once, a rough stretch reaches her brain as one and an ordinary line doesn't, and errors.txt names the key without repeating what they said");
            }
            finally
            {
                Think = thinkWas;
                Probes.Keep = keepWas;
                Probes.Answered(null);
                Brain.MemoryPath = notesWas;
                Brain.LogPath = logWas;
                preferences.Silent = silentWas;
                CloseTalk();
                try { System.IO.Directory.Delete(folder, true); } catch (System.IO.IOException) { }
            }
        }

        // Her plan and her key both kept under MAX INTELLIGENCE. Picking one used to paste it over the other, so
        // going back meant pasting the key in again, and Forget left the one it was not looking at behind.
        private void SmokePlanAndKey(List<string> checks)
        {
            string keyWas = preferences.PaidKey, modelWas = preferences.PaidModel;
            string spareWas = preferences.PaidSpare, spareModelWas = preferences.PaidSpareModel;
            int tierWas = preferences.Tier;
            try
            {
                preferences.Tier = 2;
                preferences.PaidKey = Voice.Protect("sk-ant-spare-test");
                preferences.PaidModel = "claude-sonnet-4";
                preferences.PaidSpare = null;
                preferences.PaidSpareModel = null;

                // Her plan arrives. The key goes to the spare rather than being written over. Only the part that
                // moves the keys about is run here: connecting is her brain's business and wants a network.
                Spared();
                if (Voice.Unprotect(preferences.PaidSpare) != "sk-ant-spare-test" || preferences.PaidSpareModel != "claude-sonnet-4")
                    throw new Exception("Picking her plan did not keep the key that was there: spare " + (Voice.Unprotect(preferences.PaidSpare) ?? "nothing"));
                if (SpareShown != Voice.Tail("sk-ant-spare-test"))
                    throw new Exception("The way back was not offered by name: " + (SpareShown ?? "nothing"));

                // And back again, which swaps the pair, so the way back works in both directions.
                preferences.PaidKey = Voice.Protect(Plans.ClaudeKey);
                preferences.PaidModel = "sonnet";
                string went = Voice.Unprotect(preferences.PaidSpare);
                SwapSpare();
                if (Voice.Unprotect(preferences.PaidKey) != went || preferences.PaidModel != "claude-sonnet-4")
                    throw new Exception("Going back did not restore the key and its model: " + (Voice.Unprotect(preferences.PaidKey) ?? "nothing") + " on " + (preferences.PaidModel ?? "no model"));
                if (Voice.Unprotect(preferences.PaidSpare) != Plans.ClaudeKey || preferences.PaidSpareModel != "sonnet")
                    throw new Exception("Going back did not leave her plan as the way forward: " + (Voice.Unprotect(preferences.PaidSpare) ?? "nothing"));

                // Forget drops both. The spare holds a real key, so leaving it would keep one they asked her to drop.
                ForgetText();
                if (preferences.PaidKey != null || preferences.PaidSpare != null || preferences.PaidSpareModel != null)
                    throw new Exception("Forget left a key behind: key " + (preferences.PaidKey ?? "none") + ", spare " + (preferences.PaidSpare ?? "none"));

                // And it is scrubbed out of errors.txt like every other key she holds, by its fingerprint: the scrubber never
                // opens it, so it is not among the open keys either.
                const string spareKey = "sk-ant-spare-test-0123456789";
                preferences.PaidSpare = Voice.Protect(spareKey);
                string logged = Brain.Scrubbed("refused with " + spareKey + ". then " + Uri.EscapeDataString(spareKey));
                if (logged.Contains(spareKey) || logged.IndexOf("[redacted]", StringComparison.Ordinal) < 0)
                    throw new Exception("The spare key was not kept out of errors.txt: " + logged);
                foreach (string one in SavedKeys()) if (one == spareKey) throw new Exception("The scrubber opened a sealed key to keep it out of errors.txt.");
                checks.Add("PASS her plan and her key are both kept under MAX INTELLIGENCE: picking one moves the other to the spare with its own model, the way back is offered by name and swaps the pair so it works in both directions, Forget drops both, and the spare is scrubbed out of errors.txt like every other key");
            }
            finally
            {
                preferences.PaidKey = keyWas;
                preferences.PaidModel = modelWas;
                preferences.PaidSpare = spareWas;
                preferences.PaidSpareModel = spareModelWas;
                preferences.Tier = tierWas;
            }
        }

        // Her editable files moving into personalSettings, which is the one change in this release that can lose
        // somebody's own writing if it is wrong. Everything here is a file she would actually move.
        private void SmokeFilesMoved(List<string> checks)
        {
            string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "capri-moved-" + Guid.NewGuid().ToString("N"));
            try
            {
                System.IO.Directory.CreateDirectory(root);
                string settings = System.IO.Path.Combine(root, Personalities.SettingsName);
                string[] theirs = { "me.md", "memories.md", "sys.md", "lines.md", "dynamic.md", "check-ins.md", "personality.md", "twitch.md", Interests.FileName };
                foreach (string name in theirs)
                {
                    System.IO.File.WriteAllText(System.IO.Path.Combine(root, name), "written by them in " + name + "\r\n", new System.Text.UTF8Encoding(false));
                    System.IO.File.WriteAllText(System.IO.Path.Combine(root, name + ".bak"), "their spare of " + name + "\r\n", new System.Text.UTF8Encoding(false));
                }
                string moods = System.IO.Path.Combine(root, "attitudes");
                System.IO.Directory.CreateDirectory(moods);
                System.IO.File.WriteAllText(System.IO.Path.Combine(moods, "Sleepy.md"), "# Sleepy\r\n\r\ntheirs\r\n", new System.Text.UTF8Encoding(false));

                foreach (string name in theirs)
                {
                    string went = Mine(root, name);
                    if (went != System.IO.Path.Combine(settings, name)) throw new Exception(name + " was not moved into " + Personalities.SettingsName + ": " + went);
                    if (System.IO.File.ReadAllText(went) != "written by them in " + name + "\r\n") throw new Exception(name + " lost what they had written in it.");
                    if (System.IO.File.Exists(System.IO.Path.Combine(root, name))) throw new Exception(name + " was copied rather than moved, so two of theirs now exist.");
                    string spare = System.IO.Path.Combine(settings, name + ".bak");
                    if (!System.IO.File.Exists(spare) || System.IO.File.ReadAllText(spare) != "their spare of " + name + "\r\n") throw new Exception(name + "'s spare did not come with it.");
                }
                string movedMoods = Mine(root, "attitudes", true);
                if (!System.IO.Directory.Exists(movedMoods) || !System.IO.File.Exists(System.IO.Path.Combine(movedMoods, "Sleepy.md")))
                    throw new Exception("Her moods folder did not come across whole.");
                if (System.IO.Directory.Exists(moods)) throw new Exception("Her moods folder was copied rather than moved.");

                // Her game notes: the old data\games comes into personalSettings\games even when chess.md made that folder first,
                // a note already there is never written over, and the game notes reader finds what moved.
                string oldGames = System.IO.Path.Combine(root, Sides.Games), newGames = System.IO.Path.Combine(settings, Sides.Games);
                System.IO.Directory.CreateDirectory(oldGames);
                System.IO.Directory.CreateDirectory(newGames);
                System.IO.File.WriteAllText(System.IO.Path.Combine(newGames, "chess.md"), "her board\r\n", new System.Text.UTF8Encoding(false));
                System.IO.File.WriteAllText(System.IO.Path.Combine(oldGames, "chess.md"), "an old board\r\n", new System.Text.UTF8Encoding(false));
                System.IO.File.WriteAllText(System.IO.Path.Combine(oldGames, "valorant.md"), "# Valorant\r\n\r\nwritten by them\r\n", new System.Text.UTF8Encoding(false));
                string gamesWent = Merged(root, Sides.Games);
                string gamesFolderWas = GameNotes.Folder;
                string gameRead;
                try { GameNotes.Folder = gamesWent; gameRead = GameNotes.Brief("Valorant"); }
                finally { GameNotes.Folder = gamesFolderWas; }
                if (gamesWent != newGames || !System.IO.File.Exists(System.IO.Path.Combine(newGames, "valorant.md")) || System.IO.File.Exists(System.IO.Path.Combine(oldGames, "valorant.md"))
                    || System.IO.File.ReadAllText(System.IO.Path.Combine(newGames, "chess.md")) != "her board\r\n" || !System.IO.File.Exists(System.IO.Path.Combine(oldGames, "chess.md"))
                    || gameRead.IndexOf("written by them", StringComparison.Ordinal) < 0)
                    throw new Exception("Her game notes did not come into " + Personalities.SettingsName + "\\" + Sides.Games + " whole: " + gamesWent + " / " + gameRead);
                checks.Add("PASS her game notes live in " + Personalities.SettingsName + "\\games beside chess.md: an old data\\games comes in file by file even when that folder already exists, one already there is never written over, and her game notes reader finds them there");

                // personality.md is one file in personalSettings now, read whichever side is on. Kept per side before: the side
                // in use wins, the other side's copy stays beside it as a spare only when its words differ, and no side keeps one.
                string sharedName = Sides.Shared[0];
                // A personalSettings of its own: the moves above leave a personality.md and its spare in theirs.
                string joinedSettings = System.IO.Path.Combine(root, "joined", Personalities.SettingsName);
                string plainCopy = System.IO.Path.Combine(joinedSettings, Personalities.PlainName, sharedName), senpaiCopy = System.IO.Path.Combine(joinedSettings, Personalities.SenpaiName, sharedName);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(plainCopy));
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(senpaiCopy));
                System.IO.File.WriteAllText(plainCopy, "You are Capri, kind to them.\r\n", new System.Text.UTF8Encoding(false));
                System.IO.File.WriteAllText(senpaiCopy, "You are Capri, other words.\r\n", new System.Text.UTF8Encoding(false));
                string sharedAt = Sides.Joined(joinedSettings, sharedName, false);
                System.Collections.Generic.List<string> sharedSpares = SpareFiles.Of(sharedAt);
                if (sharedAt != System.IO.Path.Combine(joinedSettings, sharedName) || System.IO.File.ReadAllText(sharedAt) != "You are Capri, kind to them.\r\n" || System.IO.File.Exists(plainCopy) || System.IO.File.Exists(senpaiCopy)
                    || sharedSpares.Count != 1 || System.IO.File.ReadAllText(sharedSpares[0]) != "You are Capri, other words.\r\n")
                    throw new Exception("Her two personality.md copies did not become one in " + Personalities.SettingsName + " with the other kept as a spare: " + sharedAt + " / " + sharedSpares.Count + " spare(s)");
                foreach (string spare in sharedSpares) System.IO.File.Delete(spare);
                System.IO.File.Delete(sharedAt);
                System.IO.File.WriteAllText(plainCopy, "Same words.\r\n", new System.Text.UTF8Encoding(false));
                System.IO.File.WriteAllText(senpaiCopy, "Same words.\n", new System.Text.UTF8Encoding(false));
                sharedAt = Sides.Joined(joinedSettings, sharedName, true);
                if (System.IO.File.Exists(plainCopy) || System.IO.File.Exists(senpaiCopy) || SpareFiles.Of(sharedAt).Count != 0 || !System.IO.File.ReadAllText(sharedAt).StartsWith("Same words", StringComparison.Ordinal))
                    throw new Exception("Two personality.md copies with the same words left a spare or a side copy behind.");
                if (Sides.Joined(joinedSettings, sharedName, false) != sharedAt || SpareFiles.Of(sharedAt).Count != 0) throw new Exception("Joining personality.md a second time changed it.");
                checks.Add("PASS personality.md is one file in " + Personalities.SettingsName + ": an install with a copy per side keeps the one of the side in use, the other side's copy as a spare only when its words differ, and no copy in either side folder");

                // Asking again changes nothing: a second start must not move anything a second time.
                foreach (string name in theirs)
                    if (Mine(root, name) != System.IO.Path.Combine(settings, name)) throw new Exception("A second start moved " + name + " again.");

                // A file they have open cannot be moved, and must not be treated as moved: she reads theirs where
                // it is until it can be. Written as a fresh empty file instead, this is where somebody's notes go.
                string held = System.IO.Path.Combine(root, "held.md");
                System.IO.File.WriteAllText(held, "the one they had open\r\n", new System.Text.UTF8Encoding(false));
                using (System.IO.FileStream open = new System.IO.FileStream(held, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                {
                    string asked = Mine(root, "held.md");
                    if (asked != held) throw new Exception("A file they had open was treated as moved: " + asked);
                    if (System.IO.File.ReadAllText(asked) != "the one they had open\r\n") throw new Exception("The file they had open lost what was in it.");
                    if (System.IO.File.Exists(System.IO.Path.Combine(settings, "held.md"))) throw new Exception("An empty one was written in the new place while theirs was still open.");
                }
                // Let go of, it moves on the next start like any other.
                if (Mine(root, "held.md") != System.IO.Path.Combine(settings, "held.md")) throw new Exception("A file that was released did not move on the next start.");

                checks.Add("PASS her editable files move into personalSettings once and keep what is in them: every file and its spare moved rather than copied, her moods folder came whole, a second start moves nothing again, and a file they have open is left where it is and read from there until they close it, rather than being written fresh and emptied");
            }
            finally
            {
                try { System.IO.Directory.Delete(root, true); } catch (System.IO.IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        // A build with a word after its number, like 0.9.427pi: still that number to her update check, so the next
        // release replaces it, and its own release files are fetched from the tag with the numbers only.
        private static void SmokeReleaseTags(List<string> checks)
        {
            Version labelled = NumbersOf("0.9.427pi");
            if (labelled == null || labelled != new Version(0, 9, 427) || !IsNewer("v0.9.428", labelled) || IsNewer("v0.9.427", labelled) || NumbersOf("pi") != null)
                throw new Exception("A build named with a word on the end lost its number, so the next release would not replace it.");
            if (!IsNewer("v0.9.427", new Version(0, 9, 426)) || IsNewer("v0.9.427pi", new Version(0, 9, 426)))
                throw new Exception("An older build's view of the release tags changed.");
            if (ReleaseFile.Own("0.9.427pi") != "0.9.427" || ReleaseFile.Own("0.9.426") != "0.9.426" || ReleaseFile.Own("0.9.070") != "0.9.070"
                || !ReleaseFile.Address("capri-codex-1.pack", "0.9.425").EndsWith("/releases/download/v0.9.425/capri-codex-1.pack", StringComparison.Ordinal)
                || !ReleaseFile.Address(Codex.NameFor("0.9.432test"), "0.9.432test").EndsWith("/releases/download/v0.9.432/capri-codex-0.9.432test.pack", StringComparison.Ordinal))
                throw new Exception("A release file was looked for under the wrong tag.");
            if (Codex.Name != Codex.NameFor(string.IsNullOrEmpty(CodexSum.Tag) ? Companion.Built : CodexSum.Tag) || Codex.NameFor("0.9.433") != "capri-codex-0.9.433.pack")
                throw new Exception("Her codex isn't named for the release that carries it: " + Codex.Name);
            checks.Add("PASS release tags: 0.9.427pi is 0.9.427 to her update check, so v0.9.428 replaces it; an older build sees v0.9.427 and not v0.9.427pi; her own files come from the numbers-only tag; her codex is named for its release, capri-codex-0.9.432test.pack from v0.9.432");
        }

        // A site that is also an app: the app first, what they use or say decides it after, and a question when they
        // use both. "My music" the same way between her own playlist and their player.
        private static void SmokeAppOrWeb(List<string> checks)
        {
            Func<List<InstalledApp>> catalogWas = Hands.Catalog;
            Action<string> runWas = Hands.Run;
            Func<string, string, string> playlistWas = Hands.Playlist;
            Func<bool> stockedWas = Hands.PlaylistStocked;
            Func<DateTime> clockWas = Hands.ChooseClock;
            string keptWas = Favourites.Kept;
            string kept = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "capri-apps-used-" + Guid.NewGuid().ToString("N") + ".txt");
            DateTime now = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
            List<string> ran = new List<string>();
            try
            {
                Hands.Catalog = delegate { return new List<InstalledApp> { new InstalledApp("Spotify", "SpotifyAB.SpotifyMusic!Spotify") }; };
                Hands.Forget();
                Hands.Run = delegate(string id) { ran.Add(id); };
                Hands.Playlist = delegate(string action, string what) { return "playlist " + action; };
                Hands.PlaylistStocked = delegate { return false; };
                Hands.ChooseClock = delegate { return now; };
                Favourites.Kept = kept;
                Action fresh = delegate
                {
                    try { System.IO.File.Delete(kept); } catch (System.IO.IOException) { } catch (UnauthorizedAccessException) { }
                    Favourites.Reload();
                };
                fresh();
                string tool;
                ToolArguments args;
                Func<string, string> heard = delegate(string said)
                {
                    if (!Hands.Heard(said, out tool, out args)) return "nothing";
                    return (tool + ":" + (args.Name ?? args.Site ?? args.Action) + (args.Browser != null ? "@" + args.Browser : "") + (args.Choose != null ? "?" + args.Choose : "")).ToLowerInvariant();
                };

                if (heard("open spotify") != "open_app:spotify") throw new Exception("An installed app did not come first: " + heard("open spotify"));
                if (heard("open spotify on the web") != "open_website:spotify" || Favourites.Where("spotify") != "web")
                    throw new Exception("Asking for the web did not open the page and become the one she uses: " + heard("open spotify on the web") + " / " + Favourites.Where("spotify"));
                if (heard("open spotify") != "open_website:spotify") throw new Exception("After the web was asked for, Spotify still opened the app.");
                heard("open the spotify app");
                if (Favourites.Where("spotify") != "app" || heard("open spotify") != "open_app:spotify") throw new Exception("Asking for the app did not make it the one she uses again.");
                heard("on brave open spotify");
                if (Favourites.Where("spotify") != "web") throw new Exception("Naming a browser did not make the page the one she uses.");
                checks.Add("PASS app or web: an installed app comes first, and \"on the web\", a browser named or \"the spotify app\" opens that and becomes the one she uses");

                // Seen in front: the app and the page about as often is a question; mostly one is that one.
                fresh();
                if (Favourites.WebSite("brave", "Spotify - Web Player - Brave") != "spotify" || Favourites.WebSite("Spotify", "Spotify Premium") != null || Favourites.WebSite("brave", "News - Brave") != null)
                    throw new Exception("A site in a browser's title was misread.");
                for (int spell = 0; spell < 4; spell++)
                {
                    now = now.AddMinutes(5); Favourites.Seen("Spotify", "Spotify Premium", now);
                    now = now.AddMinutes(5); Favourites.Seen("brave", "Spotify - Web Player - Brave", now);
                }
                if (Favourites.Where("spotify") != "both") throw new Exception("Using the app and the page alike did not read as both: " + Favourites.Where("spotify"));
                foreach (string one in Favourites.Choices("music")) if (one.StartsWith("web:", StringComparison.OrdinalIgnoreCase)) throw new Exception("A page in a browser was counted as a music program.");
                if (heard("open spotify") != "open_website:spotify?site:spotify") throw new Exception("Using both did not ask which: " + heard("open spotify"));
                Hands.Heard("open spotify", out tool, out args);
                string asked = Hands.Do(tool, args);
                if (asked != "The Spotify app, or Spotify in your browser?" || ran.Count != 0) throw new Exception("Her question of which was wrong, or something opened: " + asked);
                if (heard("the app please") != "open_app:spotify" || Favourites.Where("spotify") != "app") throw new Exception("\"The app\" did not answer her question or stick.");
                Hands.Heard("the app please", out tool, out args);
                Hands.Do(tool, args);
                if (ran.Count != 1 || heard("the app") == "open_app:spotify") throw new Exception("The answer did not open the app once and let the question go.");

                fresh();
                for (int spell = 0; spell < 4; spell++)
                {
                    now = now.AddMinutes(5); Favourites.Seen("Spotify", "Spotify Premium", now);
                    now = now.AddMinutes(5); Favourites.Seen("brave", "Spotify - Web Player - Brave", now);
                }
                Hands.Heard("open spotify", out tool, out args);
                Hands.Do(tool, args);
                if (heard("in brave") != "open_website:spotify@brave" || Favourites.Where("spotify") != "web") throw new Exception("Naming a browser did not answer her question.");
                Hands.Heard("open spotify", out tool, out args);
                if (args.Choose != null) throw new Exception("Her question came back after it was answered.");
                fresh();
                for (int spell = 0; spell < 4; spell++)
                {
                    now = now.AddMinutes(5); Favourites.Seen("Spotify", "Spotify Premium", now);
                    now = now.AddMinutes(5); Favourites.Seen("brave", "Spotify - Web Player - Brave", now);
                }
                Hands.Heard("open spotify", out tool, out args);
                Hands.Do(tool, args);
                if (heard("what time is it") == "open_app:spotify" || heard("the app") == "open_app:spotify") throw new Exception("Something other than an answer kept her question waiting.");
                Hands.Heard("open spotify", out tool, out args);
                Hands.Do(tool, args);
                now = now.AddMinutes(3);
                if (heard("the app") == "open_app:spotify") throw new Exception("An answer three minutes later was taken for her question.");
                checks.Add("PASS app or web: using the app and the page about as much asks which, the answer opens it once and sticks, a browser named answers it, and anything else or a late answer lets the question go");

                // My music: their player first; with her playlist stocked too, a question, and the answer sticks.
                fresh();
                if (heard("open my music") != "open_app:spotify") throw new Exception("\"Open my music\" did not open their player: " + heard("open my music"));
                Hands.PlaylistStocked = delegate { return true; };
                if (heard("play my music") != "open_app:spotify?music:spotify") throw new Exception("A stocked playlist and a player did not ask which: " + heard("play my music"));
                Hands.Heard("play my music", out tool, out args);
                if (Hands.Do(tool, args) != "Your music folder, or Spotify?") throw new Exception("Her music question was wrong.");
                if (heard("the folder") != "playlist:play" || Favourites.Picked("where:music") != "folder") throw new Exception("\"The folder\" did not answer or stick.");
                Hands.Heard("the folder", out tool, out args);
                Hands.Do(tool, args);
                if (heard("play my music") != "playlist:play") throw new Exception("After the folder was picked, my music still went elsewhere.");
                Favourites.Tell("music", "Spotify");
                if (heard("play my music") != "open_app:spotify") throw new Exception("A player picked did not become their music.");
                checks.Add("PASS my music: their player first, a question between her playlist and the player when both have music, and the answer becomes the one she uses");
            }
            finally
            {
                Hands.Catalog = catalogWas;
                Hands.Forget();
                Hands.Run = runWas;
                Hands.Playlist = playlistWas;
                Hands.PlaylistStocked = stockedWas;
                Hands.ChooseClock = clockWas;
                Favourites.Kept = keptWas;
                Favourites.Reload();
                try { System.IO.File.Delete(kept); } catch (System.IO.IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        // Her packs split in two by what is in them, and a folder from before the split moved into the new shape.
        // The promise being checked is that a censored install's files do not contain her senpai lines at all.
        private static void SmokePackFolders(List<string> checks)
        {
            string folderWas = Personalities.Folder;
            bool switchWas = Brain.SenpaiOn;
            string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "capri-packs-" + Guid.NewGuid().ToString("N"));
            try
            {
                string old = System.IO.Path.Combine(root, "personalities");
                System.IO.Directory.CreateDirectory(old);
                Personalities.Folder = System.IO.Path.Combine(root, Personalities.SettingsName, Personalities.FolderName);

                // One pack with both halves in it, and one that is senpai all the way through.
                string both = "# Testy\r\n\r\n>! Notes.\r\n\r\n## Her character\r\n\r\nPlain character.\r\n\r\n## When you pet her\r\n\r\nPlain pet line.\r\n\r\n## When you pet her (overdrive)\r\n\r\nSenpai pet line.\r\n";
                string whole = "# Marky\r\n\r\n## Her character\r\n\r\nSenpai throughout.\r\n\r\n## When you pet her\r\n\r\nSenpai pet line.\r\n\r\noverdrive only: yes\r\n";
                System.IO.File.WriteAllText(System.IO.Path.Combine(old, "Testy.md"), both, new System.Text.UTF8Encoding(false));
                System.IO.File.WriteAllText(System.IO.Path.Combine(old, "Marky.md"), whole, new System.Text.UTF8Encoding(false));

                // Switch off first: the plain half moves, nothing is written to the senpai folder, and the file they
                // had stays whole where it was until the switch goes on.
                Brain.SenpaiOn = false;
                Personalities.Install();
                string plain = System.IO.Path.Combine(Personalities.Plain, "Testy.md");
                if (!System.IO.File.Exists(plain) || System.IO.Directory.Exists(Personalities.Senpais) || System.IO.File.ReadAllText(System.IO.Path.Combine(old, "Testy.md")) != both)
                    throw new Exception("With the switch off, a pack with both halves wrote its senpai half or lost the file it came from.");
                Brain.SenpaiOn = true;
                Personalities.Install();
                // Files in the senpai folder go by slot names, so each is found through its slot.
                string senpai = Personalities.SenpaiPathOf("Testy");

                if (!System.IO.File.Exists(plain) || !System.IO.File.Exists(senpai)) throw new Exception("A pack with both halves did not end up in both folders.");
                string hadPlain = System.IO.File.ReadAllText(plain), hadSenpai = System.IO.File.ReadAllText(senpai);
                if (hadPlain.IndexOf("Senpai pet line", StringComparison.Ordinal) >= 0 || Legacy.HasTag(hadPlain))
                    throw new Exception("The censored half still contains her senpai lines.");
                if (hadPlain.IndexOf("Plain pet line", StringComparison.Ordinal) < 0 || hadPlain.IndexOf("Plain character", StringComparison.Ordinal) < 0)
                    throw new Exception("The censored half lost her plain lines or her character.");
                if (hadSenpai.IndexOf("Senpai pet line", StringComparison.Ordinal) < 0 || hadSenpai.IndexOf("# Testy", StringComparison.Ordinal) < 0)
                    throw new Exception("The senpai half lost its lines or its title.");
                if (hadSenpai.IndexOf("Plain pet line", StringComparison.Ordinal) < 0 || hadSenpai.IndexOf("Plain character", StringComparison.Ordinal) < 0)
                    throw new Exception("The senpai copy is not whole: it is missing her plain lines or her character.");

                // Senpai all the way through: one file, in the senpai folder, and nothing in the censored one.
                if (System.IO.File.Exists(System.IO.Path.Combine(Personalities.Plain, "Marky.md"))
                    || !System.IO.File.Exists(Personalities.SenpaiPathOf("Marky")))
                    throw new Exception("A pack that is senpai throughout was split or left in the censored folder.");

                // Nothing is deleted on the way.
                if (System.IO.File.Exists(System.IO.Path.Combine(old, "Testy.md")) || SpareFiles.Of(System.IO.Path.Combine(old, "Testy.md")).Count != 1)
                    throw new Exception("The file it came from was deleted instead of kept as a .bak.");

                // Her own packs are there too, each with the same split, and every name she ships can be read back.
                string[] shipped = { "Devoted.md", "Caregiver.md" };
                foreach (string name in shipped)
                    if (!System.IO.File.Exists(System.IO.Path.Combine(Personalities.Plain, name))) throw new Exception("She did not write " + name + " into the censored folder.");
                if (!System.IO.File.Exists(Personalities.SenpaiPathOf("Devoted"))) throw new Exception("Devoted's senpai copy is missing.");
                if (Legacy.HasTag(System.IO.File.ReadAllText(System.IO.Path.Combine(Personalities.Plain, "Devoted.md"))))
                    throw new Exception("A shipped pack's censored half still has a senpai section in it.");

                // Running again changes nothing, which is what stops a startup rewriting her files every time.
                Dictionary<string, string> before = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string file in Personalities.Files()) before[file] = System.IO.File.ReadAllText(file);
                Personalities.Install();
                foreach (string file in Personalities.Files())
                {
                    string had;
                    if (!before.TryGetValue(file, out had)) throw new Exception("A second pass added " + System.IO.Path.GetFileName(file) + ".");
                    string now = System.IO.File.ReadAllText(file);
                    if (had != now)
                    {
                        int differs = 0;
                        while (differs < had.Length && differs < now.Length && had[differs] == now[differs]) differs++;
                        // Its shape only, letters masked: the file may hold the switch's side.
                        Func<string, string> shape = delegate(string text)
                        {
                            int from = Math.Max(0, differs - 40);
                            return System.Text.RegularExpressions.Regex.Replace(text.Substring(from, Math.Min(text.Length - from, 120)), "[A-Za-z]", "a").Replace("\r", "\\r").Replace("\n", "\\n");
                        };
                        throw new Exception("A second pass rewrote " + System.IO.Path.GetFileName(file) + ": " + had.Length + " characters to " + now.Length + ", first different at " + differs
                            + ", first pass there: " + shape(had) + " / second: " + shape(now));
                    }
                }

                // A pack she doesn't ship has no stock to go back to, so a reset marker in it resets nothing and the
                // file is left as it was.
                string theirs = System.IO.Path.Combine(Personalities.Plain, "Ourown.md");
                string wrote = "# Ourown\r\nreset to stock: 1\r\n\r\n## Her character\r\n\r\nWritten here, shipped nowhere.\r\n";
                System.IO.File.WriteAllText(theirs, wrote, new System.Text.UTF8Encoding(false));
                if (Personalities.Half(theirs, "Ourown") != null) throw new Exception("A pack she does not ship was handed a stock text to be reset to.");
                CapriDesktop.Notes.Stock(theirs, delegate { return Personalities.Half(theirs, "Ourown"); });
                if (System.IO.File.ReadAllText(theirs) != wrote) throw new Exception("A reset marker in someone's own pack rewrote it: " + System.IO.File.ReadAllText(theirs));

                checks.Add("PASS her packs are split by what is in them: censored\\ holds her plain sections with no senpai line and no senpai heading anywhere in the file, interests\\ holds a whole copy, her plain sections and the senpai ones together, a pack that is senpai throughout is one file in interests\\ only, a folder from before the split is moved into both with the old file kept as a .bak, and a second pass changes nothing, a pack she does not ship has no stock to go back to so a reset marker in it leaves the file exactly as it was");
            }
            finally
            {
                Personalities.Folder = folderWas;
                Brain.SenpaiOn = switchWas;
                try { System.IO.Directory.Delete(root, true); } catch (System.IO.IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        // Her reply, however it came: these checks drive her real path, which answers on her own message loop.
        private void WaitedForReply()
        {
            System.Diagnostics.Stopwatch waited = System.Diagnostics.Stopwatch.StartNew();
            while (reply == null && waited.ElapsedMilliseconds < 3000) { Application.DoEvents(); Thread.Sleep(5); }
        }

        // One app's volume in a pretend mixer: Spotify, two Discord processes, a game, muted Chrome and an app ignoring
        // changes. Each change is read back, and their real apps' sound is never touched (Lachy).
        private static void SmokeAppVolume(List<string> checks)
        {
            Func<List<AppSound>> soundsWas = Hands.Sounds;
            Func<int, float?, bool?, bool> setWas = Hands.SetSound;
            Func<string, string, bool> runningWas = Hands.Running;
            string openAiWas = Brain.OpenAi, notesWas = Brain.MemoryPath, memoriesWas = Brain.MemoriesPath, transcriptsWas = Brain.TranscriptPath, logWas = Brain.LogPath;
            bool keepingWas = Brain.Keeping;
            TcpListener fake = null;
            try
            {
                List<AppSound> mixer = new List<AppSound>
                {
                    new AppSound { App = "Spotify.exe", Id = 10, Level = 0.5f, Playing = true }, new AppSound { App = "Discord.exe", Id = 20, Level = 1f },
                    new AppSound { App = "Discord.exe", Id = 21, Level = 1f, Playing = true }, new AppSound { App = "FortniteClient-Win64-Shipping.exe", Id = 30, Level = 0.8f, Playing = true },
                    new AppSound { App = "chrome.exe", Id = 40, Level = 0.6f, Muted = true }, new AppSound { App = "Stuck.exe", Id = 50, Level = 0.3f }
                };
                List<string> changed = new List<string>();
                bool readable = true;
                // Fresh copies each read, the way each read of the real mixer is.
                Hands.Sounds = delegate
                {
                    if (!readable) return null;
                    return mixer.ConvertAll(delegate(AppSound sound) { return new AppSound { App = sound.App, Id = sound.Id, Level = sound.Level, Muted = sound.Muted, Playing = sound.Playing }; });
                };
                Hands.SetSound = delegate(int id, float? level, bool? muted)
                {
                    changed.Add(id + (level.HasValue ? " " + Math.Round(level.Value * 100) : "") + (muted.HasValue ? (muted.Value ? " muted" : " unmuted") : ""));
                    if (id == 50) return true;
                    foreach (AppSound sound in mixer)
                    {
                        if (sound.Id != id) continue;
                        if (level.HasValue) sound.Level = level.Value;
                        if (muted.HasValue) sound.Muted = muted.Value;
                    }
                    return true;
                };
                Hands.Running = delegate(string name, string id) { return name == "steam"; };
                Func<string, string, string, string> mix = delegate(string action, string name, string level) { return Hands.Do("app_volume", new ToolArguments { Action = action, Name = name, Level = level }); };
                string[,] asks =
                {
                    { "now", null, null, "In the volume mixer: Spotify 50%, playing now; Discord 100%, playing now; FortniteClient-Win64-Shipping 80%, playing now; Chrome 60%, muted; Stuck 30%." },
                    { "now", "spotify", null, "Spotify is at 50% in the volume mixer." }, { "now", "chrome", null, "Chrome is at 60% in the volume mixer, and it's muted." },
                    { "down", "fortnite", null, "Turned FortniteClient-Win64-Shipping down to 60% in the volume mixer." }, { "down", "FortniteClient-Win64-Shipping.exe", "10", "Turned FortniteClient-Win64-Shipping down to 50% in the volume mixer." },
                    { "set", "Discord", "40", "Set Discord to 40% in the volume mixer." }, { "up", "spotify", "30", "Turned Spotify up to 80% in the volume mixer." },
                    { "up", "chrome", null, "Turned Chrome up to 80% in the volume mixer." }, { "mute", "spotify", null, "Muted Spotify in the volume mixer." },
                    { "mute", "Spotify", null, "Spotify is already muted in the volume mixer." }, { "down", "spotify", null, "Turned Spotify down to 60% in the volume mixer, but it's muted." },
                    { "unmute", "spotify", null, "Unmuted Spotify: it's at 60% in the volume mixer." }, { "unmute", "spotify", null, "Spotify isn't muted: it's at 60% in the volume mixer." },
                    { "set", "spotify", "0", "Set Spotify to 0% in the volume mixer." }, { "down", "spotify", null, "Spotify is already at 0% in the volume mixer." },
                    { "set", "discord", "100", "Set Discord to 100% in the volume mixer." }, { "up", "discord", null, "Discord is already at 100% in the volume mixer. Its own volume slider inside the app, or the PC's volume, can still go up." },
                    { "set", "stuck", "80", "Couldn't set Stuck's volume: it's still at 30%." }, { "set", "spotify", "150", "Couldn't set Spotify's volume: it goes from 0 to 100." },
                    { "now", "steam", null, "steam hasn't made any sound since it opened, so it has no volume of its own in the mixer yet." },
                    { "set", "steam", "50", "Couldn't change steam's volume: it hasn't made any sound since it opened, so it has no volume of its own in the mixer yet." },
                    { "now", "minecraft", null, "minecraft isn't open, or it has another name in the mixer: list_windows names its program." }, { "up", null, null, "Which app, choom?" }
                };
                List<string> wrong = new List<string>();
                for (int i = 0; i < asks.GetLength(0); i++)
                {
                    string got = mix(asks[i, 0], asks[i, 1], asks[i, 2]);
                    if (got != asks[i, 3]) wrong.Add(asks[i, 0] + " " + asks[i, 1] + " " + asks[i, 2] + " said \"" + got + "\"");
                }
                if (wrong.Count > 0) throw new Exception("Her app volume didn't say where each app ended up: " + string.Join(" / ", wrong.ToArray()));
                string moves = string.Join(",", changed.ToArray());
                if (!moves.StartsWith("30 60,30 50,20 40,21 40,10 80,40 80 unmuted,10 muted,10 60,10 unmuted,10 0,20 100,21 100,50 80", StringComparison.Ordinal))
                    throw new Exception("Her app volume moved the wrong sounds: " + moves);
                if (!Hands.Failed(mix("set", "stuck", "80")) || !Hands.Failed(mix("set", "steam", "50")) || Hands.Failed(mix("now", "steam", null)) || Hands.Failed(mix("now", "spotify", null)))
                    throw new Exception("A change that didn't happen wasn't read as one.");
                // Discord playing on two outputs with only one of them muted still unmutes.
                foreach (AppSound sound in mixer) if (sound.Id == 20) sound.Muted = true;
                string halfMuted = mix("unmute", "discord", null);
                if (halfMuted != "Unmuted Discord: it's at 100% in the volume mixer." || mixer.Exists(delegate(AppSound sound) { return sound.App == "Discord.exe" && sound.Muted; }))
                    throw new Exception("An app muted on one of its two outputs stayed muted: " + halfMuted);
                readable = false;
                string unread = mix("now", null, null);
                readable = true;
                if (unread != "Couldn't read the volume mixer just now.") throw new Exception("An unreadable mixer wasn't said: " + unread);
                // Reading it never asks first and stays out of her transcript; changing it is Music, media and volume, like the PC's volume.
                ToolArguments quieter = new ToolArguments { Action = "down", Name = "fortnite" };
                if (!Hands.Valid("app_volume", new ToolArguments { Action = "now" }) || !Hands.Valid("app_volume", quieter) || !Hands.Valid("app_volume", new ToolArguments { Action = "up", Name = "spotify", Level = "30" })
                    || Hands.Valid("app_volume", new ToolArguments { Action = "set", Name = "spotify" }) || Hands.Valid("app_volume", new ToolArguments { Action = "set", Name = "spotify", Level = "loud" })
                    || Hands.Valid("app_volume", new ToolArguments { Action = "mute" }) || Hands.Valid("app_volume", new ToolArguments { Action = "blast", Name = "spotify" })
                    || !Hands.Checking("app_volume", new ToolArguments { Action = "now" }) || Hands.Checking("app_volume", quieter)
                    || Security.Needs("app_volume", new ToolArguments { Action = "now" }) != -1 || Security.Needs("app_volume", quieter) != Security.Media
                    || Security.Asking("app_volume", quieter) != "turn fortnite down" || Security.Asking("app_volume", new ToolArguments { Action = "set", Name = "Spotify", Level = "30" }) != "set Spotify to 30%")
                    throw new Exception("Her app volume's calls weren't checked.");
                // Every brain gets it: her one-shot hands and voice chat as well as a brain that works jobs out.
                Hands.Kit kit = Hands.KitFor();
                bool voiced = false;
                for (int i = 0; i < Hands.Count; i++) if (Hands.Name(i) == "app_volume") voiced = true;
                if (!kit.WorkTools.Contains("\"name\":\"app_volume\"") || !kit.Tools.Contains("app_volume") || !voiced || !Hands.WorkingOut.Contains("is app_volume; the whole PC's is media"))
                    throw new Exception("Her app volume missed a brain or voice chat.");
                // A brain that works jobs out reads the mixer, turns the game down, and hears where it ended up.
                List<string> asked = new List<string>();
                Func<string, string> says = delegate(string text) { return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":" + Voice.Quote(text) + "}}]}"; };
                Func<string, string, string> calls = delegate(string name, string json) { return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"" + name + "\",\"arguments\":" + Voice.Quote(json) + "}}]}}]}"; };
                foreach (AppSound sound in mixer) if (sound.Id == 30) sound.Level = 0.8f;
                changed.Clear();
                Brain.OpenAi = FakeServer(new[] { calls("app_volume", "{\"action\":\"now\"}"), calls("app_volume", "{\"action\":\"down\",\"name\":\"fortnite\",\"level\":\"40\"}"), says("Turned Fortnite down to 40%, choom. Discord's at full.") }, asked, out fake);
                Brain.MemoryPath = Brain.MemoriesPath = Brain.TranscriptPath = Brain.LogPath = null;
                Brain.Keeping = false;
                List<string> did = new List<string>();
                Brain planner = new Brain { Model = "gpt-5-mini", Remote = new Cloud("sk-proj-test", "gpt-5-mini"), OwnSteps = true };
                planner.Act = delegate(string name, ToolArguments args) { did.Add(name + ":" + args.Action + ":" + args.Name); return Hands.Do(name, args); };
                string reply = planner.Ask("the game's way too loud, I can't hear my friends on discord");
                string[] heard;
                lock (asked) heard = asked.ToArray();
                if (reply != "Turned Fortnite down to 40%, choom. Discord's at full." || string.Join(",", did.ToArray()) != "app_volume:now:,app_volume:down:fortnite" || heard.Length != 3
                    || !heard[0].Contains("\"name\":\"app_volume\"") || !heard[1].Contains("FortniteClient-Win64-Shipping 80%, playing now") || !heard[2].Contains("Turned FortniteClient-Win64-Shipping down to 40% in the volume mixer.") || string.Join(",", changed.ToArray()) != "30 40")
                    throw new Exception("Her planner didn't read the mixer or hear where the game ended up: " + reply + " / " + string.Join(",", did.ToArray()) + " / " + heard.Length + " calls");
                // Windows' own mixer answers on this PC; only reading it here, so nothing of theirs gets louder or quieter.
                List<AppSound> real = Mixer.Sounds();
                if (real == null) throw new Exception("Windows' volume mixer couldn't be read on this PC.");
                checks.Add("PASS her app volume reads and changes one app's own volume in the mixer apart from the PC's: lists every app with a sound and which are playing, sets, steps up and down, mutes and unmutes Spotify, both Discord processes, a game by its program and a muted Chrome, reads each back, says when one is already as far as it goes, when an app ignores the change, hasn't made a sound yet or isn't open, and when the mixer can't be read; reading never asks first, changing it is Music, media and volume; only a planner gets it, and it turns the game down after reading the mixer; Windows' mixer read here: " + real.Count + " sounds");
                checks.Add("LIMIT app volume on their own apps: a game, Discord and Spotify each show in Windows' mixer once they've made sound; ask \"turn the game down a bit\" with a game and Discord both playing");
            }
            finally
            {
                Hands.Sounds = soundsWas;
                Hands.SetSound = setWas;
                Hands.Running = runningWas;
                Brain.OpenAi = openAiWas;
                Brain.MemoryPath = notesWas;
                Brain.MemoriesPath = memoriesWas;
                Brain.TranscriptPath = transcriptsWas;
                Brain.LogPath = logWas;
                Brain.Keeping = keepingWas;
                if (fake != null) fake.Stop();
            }
        }

        // How their PC is doing on a pretend busy PC: processor, memory, top apps, one app by name, drives and battery,
        // read only; then a real read of this PC that changes nothing (Lachy).
        private static void SmokePcStatus(List<string> checks)
        {
            Func<PcUse> useWas = Hands.Use;
            string openAiWas = Brain.OpenAi, notesWas = Brain.MemoryPath, memoriesWas = Brain.MemoriesPath, transcriptsWas = Brain.TranscriptPath, logWas = Brain.LogPath;
            bool keepingWas = Brain.Keeping;
            TcpListener fake = null;
            try
            {
                const long Gigabyte = 1073741824;
                PcUse busy = new PcUse { Cpu = 87.4, MemoryUsed = (long)(24.6 * Gigabyte), MemoryTotal = (long)(31.9 * Gigabyte) };
                string[] names = { "FortniteClient-Win64-Shipping", "chrome", "Discord", "obs64", "MsMpEng", "Spotify", "Teams", "explorer" };
                double[] cpu = { 41.2, 18.6, 3.2, 9.5, 7.9, 0.4, 0, 1.0 }, memory = { 6.3, 3.1, 0.9, 0.7, 0.3, 0.4, 1.2, 0.2 };
                for (int i = 0; i < names.Length; i++) busy.Apps.Add(new AppUse { App = names[i], Cpu = cpu[i], Memory = (long)(memory[i] * Gigabyte) });
                busy.Drives.Add("C: 45 GB free of 931 GB");
                busy.Drives.Add("D: 1.2 TB free of 1.8 TB");
                bool readable = true;
                Hands.Use = delegate { return readable ? busy : null; };
                const string Totals = "Their PC right now: the processor is 87% busy, and 25 GB of its 32 GB of memory is in use (77%).";
                string all = Hands.Do("pc_status", new ToolArguments()), chrome = Hands.Do("pc_status", new ToolArguments { Name = "chrome" }), game = Hands.Do("pc_status", new ToolArguments { Name = "Fortnite" });
                string absent = Hands.Do("pc_status", new ToolArguments { Name = "valorant" });
                busy.Battery = "54%, on battery";
                string laptop = Hands.Do("pc_status", new ToolArguments());
                readable = false;
                string unread = Hands.Do("pc_status", new ToolArguments());
                readable = true;
                string wanted = Totals + " Using the most: FortniteClient-Win64-Shipping 41% of the processor and 6.3 GB; chrome 19% of the processor and 3.1 GB; obs64 10% of the processor and 0.7 GB; MsMpEng 8% of the processor and 0.3 GB; "
                    + "Discord 3% of the processor and 0.9 GB; Teams 1.2 GB of memory; Spotify 0.4 GB of memory; explorer 0.2 GB of memory. Drives: C: 45 GB free of 931 GB; D: 1.2 TB free of 1.8 TB.";
                if (all != wanted || chrome != "chrome is using 19% of the processor and 3.1 GB of memory. " + Totals || game != "FortniteClient-Win64-Shipping is using 41% of the processor and 6.3 GB of memory. " + Totals
                    || absent != "Nothing called valorant is running. " + Totals || laptop != wanted + " Battery 54%, on battery." || unread != "Couldn't read how their PC is doing just now."
                    || Usage.Size(45 * Gigabyte) != "45 GB" || Usage.Size((long)(1.2 * 1024 * Gigabyte)) != "1.2 TB" || Usage.Size(Gigabyte / 2) != "0.5 GB")
                    throw new Exception("Her PC check didn't say how it's doing: " + all + " / " + chrome + " / " + game + " / " + absent + " / " + laptop + " / " + unread);
                // A check: it never asks first, stays out of her transcript, and every brain gets it, voice chat too.
                Hands.Kit kit = Hands.KitFor();
                bool voiced = false;
                for (int i = 0; i < Hands.Count; i++) if (Hands.Name(i) == "pc_status") voiced = true;
                if (!Hands.Valid("pc_status", new ToolArguments()) || !Hands.Checking("pc_status", new ToolArguments()) || Security.Needs("pc_status", new ToolArguments()) != -1
                    || !kit.WorkTools.Contains("\"name\":\"pc_status\"") || !kit.Tools.Contains("pc_status") || !voiced || !Hands.WorkingOut.Contains("pc_status says how busy it is and what's using it"))
                    throw new Exception("Her PC check asked first, or missed a brain or voice chat.");
                // A brain that works jobs out finds out what's using the PC before it says why a game is stuttering.
                List<string> asked = new List<string>();
                Func<string, string> says = delegate(string text) { return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":" + Voice.Quote(text) + "}}]}"; };
                Func<string, string, string> calls = delegate(string name, string json) { return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"" + name + "\",\"arguments\":" + Voice.Quote(json) + "}}]}}]}"; };
                busy.Battery = null;
                Brain.OpenAi = FakeServer(new[] { calls("pc_status", "{}"), says("Your CPU's at 87%, choom. Chrome and OBS are eating a chunk next to Fortnite.") }, asked, out fake);
                Brain.MemoryPath = Brain.MemoriesPath = Brain.TranscriptPath = Brain.LogPath = null;
                Brain.Keeping = false;
                List<string> did = new List<string>();
                Brain planner = new Brain { Model = "gpt-5-mini", Remote = new Cloud("sk-proj-test", "gpt-5-mini"), OwnSteps = true };
                planner.Act = delegate(string name, ToolArguments args) { did.Add(name); return Hands.Do(name, args); };
                string reply = planner.Ask("my game keeps stuttering, what's going on with my pc");
                string[] heard;
                lock (asked) heard = asked.ToArray();
                if (reply != "Your CPU's at 87%, choom. Chrome and OBS are eating a chunk next to Fortnite." || string.Join(",", did.ToArray()) != "pc_status" || heard.Length != 2
                    || !heard[0].Contains("\"name\":\"pc_status\"") || !heard[1].Contains("FortniteClient-Win64-Shipping 41% of the processor and 6.3 GB"))
                    throw new Exception("Her planner didn't read the PC before answering: " + reply + " / " + string.Join(",", did.ToArray()) + " / " + heard.Length + " calls");
                // This PC, read for real over half a second; nothing about it goes in this line.
                PcUse real = Usage.Read();
                if (real == null || real.Cpu < 0 || real.Cpu > 100 || real.MemoryTotal <= 0 || real.MemoryUsed <= 0 || real.Apps.Count == 0 || real.Drives.Count == 0)
                    throw new Exception("This PC couldn't be read: " + (real == null ? "nothing" : real.Cpu + "% / " + real.MemoryTotal + " / " + real.Apps.Count + " apps / " + real.Drives.Count + " drives"));
                checks.Add("PASS her PC check says how busy the processor and memory are, the five apps using the most processor and the three using the most memory besides, one app by name, each drive's free space and a laptop's battery, and when it can't read them; it never asks first, only a planner gets it, and a planner reads it before saying why a game stutters; this PC read for real: " + real.Apps.Count + " apps, " + real.Drives.Count + " drives");
            }
            finally
            {
                Hands.Use = useWas;
                Brain.OpenAi = openAiWas;
                Brain.MemoryPath = notesWas;
                Brain.MemoriesPath = memoriesWas;
                Brain.TranscriptPath = transcriptsWas;
                Brain.LogPath = logWas;
                Brain.Keeping = keepingWas;
                if (fake != null) fake.Stop();
            }
        }

        // Their internet on pretend connections: working, dropping out, down, a name lookup that fails and no network at all, with
        // nothing pinged from the checks; only this PC's own links are read for real (Lachy).
        private static void SmokeInternetStatus(List<string> checks)
        {
            Func<string, NetUse> netWas = Hands.Net;
            string openAiWas = Brain.OpenAi, notesWas = Brain.MemoryPath, memoriesWas = Brain.MemoriesPath, transcriptsWas = Brain.TranscriptPath, logWas = Brain.LogPath;
            bool keepingWas = Brain.Keeping;
            TcpListener fake = null;
            try
            {
                NetUse now = null;
                List<string> pinged = new List<string>();
                Hands.Net = delegate(string address) { pinged.Add(address ?? ""); return now; };
                Func<NetUse, string, string> check = delegate(NetUse use, string address) { now = use; return Hands.Do("internet_status", new ToolArguments { Address = address }); };
                NetUse working = new NetUse { Pinged = "1.1.1.1", Sent = 4, Lost = 0, Average = 14, Fastest = 12, Names = true };
                working.Links.Add("Ethernet");
                NetUse dropping = new NetUse { Pinged = "1.1.1.1", Sent = 4, Lost = 2, Average = 180, Fastest = 150, Names = true };
                dropping.Links.Add("Wi-Fi");
                NetUse down = new NetUse { Pinged = "8.8.8.8", Sent = 4, Lost = 4, Names = false };
                down.Links.Add("Wi-Fi");
                NetUse unnamed = new NetUse { Pinged = "1.1.1.1", Sent = 4, Lost = 0, Average = 20, Fastest = 18, Names = false };
                unnamed.Links.Add("Ethernet");
                unnamed.Links.Add("Wi-Fi");
                string fine = check(working, null), drops = check(dropping, null), dead = check(down, null), dns = check(unnamed, null), unplugged = check(new NetUse(), null), unread = check(null, null), site = check(working, "google.com");
                if (fine != "Their internet is working, over Ethernet: pings to 1.1.1.1 took 14 ms on average, 12 ms at best, and web addresses look up fine."
                    || drops != "Their internet is dropping out, over Wi-Fi: 2 of 4 pings to 1.1.1.1 were lost, and the rest took 180 ms on average, 150 ms at best, and web addresses look up fine."
                    || dead != "Their PC is connected over Wi-Fi, but nothing answered: all 4 pings to 8.8.8.8 were lost, and web addresses don't look up either, so the internet looks down."
                    || dns != "Their internet is working, over Ethernet and Wi-Fi: pings to 1.1.1.1 took 20 ms on average, 18 ms at best, but web addresses don't look up, so sites won't load: their DNS is the problem."
                    || unplugged != "Their PC isn't connected to any network: no Ethernet or Wi-Fi is up." || unread != "Couldn't check their internet just now."
                    || site != fine || string.Join(",", pinged.ToArray()) != ",,,,,,google.com")
                    throw new Exception("Her internet check didn't say how the connection is: " + fine + " / " + drops + " / " + dead + " / " + dns + " / " + unplugged + " / " + unread + " / " + string.Join(",", pinged.ToArray()));
                // A check: it never asks first, stays out of her transcript, and every brain gets it, voice chat too.
                Hands.Kit kit = Hands.KitFor();
                bool voiced = false;
                for (int i = 0; i < Hands.Count; i++) if (Hands.Name(i) == "internet_status") voiced = true;
                if (!Hands.Valid("internet_status", new ToolArguments()) || !Hands.Checking("internet_status", new ToolArguments()) || Security.Needs("internet_status", new ToolArguments()) != -1
                    || !kit.WorkTools.Contains("\"name\":\"internet_status\"") || !kit.Tools.Contains("internet_status") || !voiced || !Hands.WorkingOut.Contains("internet_status says whether their connection is the problem"))
                    throw new Exception("Her internet check asked first, or missed a brain or voice chat.");
                // A brain that works jobs out checks the connection before it blames the game.
                List<string> asked = new List<string>();
                Func<string, string> says = delegate(string text) { return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":" + Voice.Quote(text) + "}}]}"; };
                Func<string, string, string> calls = delegate(string name, string json) { return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"" + name + "\",\"arguments\":" + Voice.Quote(json) + "}}]}}]}"; };
                now = dropping;
                Brain.OpenAi = FakeServer(new[] { calls("internet_status", "{}"), says("It's your Wi-Fi, choom: half your pings are getting lost.") }, asked, out fake);
                Brain.MemoryPath = Brain.MemoriesPath = Brain.TranscriptPath = Brain.LogPath = null;
                Brain.Keeping = false;
                List<string> did = new List<string>();
                Brain planner = new Brain { Model = "gpt-5-mini", Remote = new Cloud("sk-proj-test", "gpt-5-mini"), OwnSteps = true };
                planner.Act = delegate(string name, ToolArguments args) { did.Add(name); return Hands.Do(name, args); };
                string reply = planner.Ask("is it my internet or is valorant just lagging");
                string[] heard;
                lock (asked) heard = asked.ToArray();
                if (reply != "It's your Wi-Fi, choom: half your pings are getting lost." || string.Join(",", did.ToArray()) != "internet_status" || heard.Length != 2
                    || !heard[0].Contains("\"name\":\"internet_status\"") || !heard[1].Contains("2 of 4 pings to 1.1.1.1 were lost"))
                    throw new Exception("Her planner didn't check the connection before answering: " + reply + " / " + string.Join(",", did.ToArray()) + " / " + heard.Length + " calls");
                // This PC's links, read for real without pinging anything.
                List<string> links = Network.Links();
                if (links == null) throw new Exception("This PC's network links couldn't be read.");
                checks.Add("PASS her internet check says whether their connection works, is dropping out, is down or can't look names up, which link they're on, and pings an address they name; it never asks first, only a planner gets it, and a planner checks the connection before blaming a game; this PC's links read for real: " + links.Count);
                checks.Add("LIMIT her internet check's pings and name lookups on their own connection: the checks never ping out; ask \"is my internet down?\" and \"what's my ping to google?\"");
            }
            finally
            {
                Hands.Net = netWas;
                Brain.OpenAi = openAiWas;
                Brain.MemoryPath = notesWas;
                Brain.MemoriesPath = memoriesWas;
                Brain.TranscriptPath = transcriptsWas;
                Brain.LogPath = logWas;
                Brain.Keeping = keepingWas;
                if (fake != null) fake.Stop();
            }
        }

        // An ask opens each app, site or song once however often her brain repeats the call, whether one-shot hands
        // writing it over and over or a planner calling it twenty times. Media still goes twice, like skipping two
        // songs.
        private static void SmokeOpensOnce(List<string> checks)
        {
            string openAiWas = Brain.OpenAi, notesWas = Brain.MemoryPath, memoriesWas = Brain.MemoriesPath, transcriptsWas = Brain.TranscriptPath, logWas = Brain.LogPath;
            bool keepingWas = Brain.Keeping;
            TcpListener fake = null;
            try
            {
                string looped = "";
                for (int i = 0; i < 20; i++) looped += "open_website{\"site\":\"youtube.com\"}\n";
                List<KeyValuePair<string, ToolArguments>> written = Hands.Written(looped);
                int parsed = written.Count;
                Brain.Once(written);
                List<KeyValuePair<string, ToolArguments>> mixed = Hands.Written("open_app{\"name\":\"Spotify\"}\nmedia{\"action\":\"next\"}\nopen_app{\"name\":\"spotify\"}\nmedia{\"action\":\"next\"}\nplay_music{\"song\":\"lofi beats\"}\nopen_website{\"site\":\"youtube.com\"}\nplay_music{\"song\":\"lofi beats\"}\nopen_app{\"name\":\"Discord\"}");
                Brain.Once(mixed);
                List<string> kept = mixed.ConvertAll(call => call.Key + ":" + (call.Value.Name ?? call.Value.Song ?? call.Value.Site ?? call.Value.Action));
                if (parsed != 20 || written.Count != 1 || string.Join(",", kept.ToArray()) != "open_app:Spotify,media:next,media:next,play_music:lofi beats,open_website:youtube.com,open_app:Discord")
                    throw new Exception("Repeated opens weren't cut to one: " + parsed + " written, " + written.Count + " left / " + string.Join(",", kept.ToArray()));
                // Her one-shot hands calling the same open twenty times at once open it once.
                List<string> asked = new List<string>(), did = new List<string>();
                Func<string, string> says = delegate(string text) { return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":" + Voice.Quote(text) + "}}]}"; };
                Func<string, string, int, string> repeated = delegate(string name, string json, int times)
                {
                    string made = "";
                    for (int i = 0; i < times; i++) made += (i == 0 ? "" : ",") + "{\"id\":\"c" + i + "\",\"type\":\"function\",\"function\":{\"name\":\"" + name + "\",\"arguments\":" + Voice.Quote(json) + "}}";
                    return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[" + made + "]}}]}";
                };
                Func<string, ToolArguments, string> opening = delegate(string name, ToolArguments args) { did.Add(name + ":" + (args.Name ?? args.Site)); return "Opened " + (args.Name ?? args.Site) + "."; };
                Brain.OpenAi = FakeServer(new[] { repeated("open_app", "{\"name\":\"Blorbo Studio\"}", 20), says("Blorbo Studio's up, choom."), says("Blorbo Studio's up, choom.") }, asked, out fake);
                Brain.MemoryPath = Brain.MemoriesPath = Brain.TranscriptPath = Brain.LogPath = null;
                Brain.Keeping = false;
                Brain hands = new Brain { Model = "gpt-5-mini", Remote = new Cloud("sk-proj-test", "gpt-5-mini") };
                hands.Act = opening;
                string handed = hands.Ask("boot up blorbo studio");
                int handAsks;
                lock (asked) handAsks = asked.Count;
                if (string.Join(",", did.ToArray()) != "open_app:Blorbo Studio" || handAsks == 0)
                    throw new Exception("Her hands opened it more than once, or never asked her brain: " + handed + " / " + string.Join(",", did.ToArray()) + " / " + handAsks + " calls");
                fake.Stop();
                fake = null;
                // A planner calling YouTube twenty times at once, then again a round later, opens it once and hears it's done.
                did.Clear();
                lock (asked) asked.Clear();
                Brain.OpenAi = FakeServer(new[] { repeated("open_website", "{\"site\":\"youtube.com\"}", 20), repeated("open_website", "{\"site\":\"youtube.com\"}", 1), says("YouTube's up, choom.") }, asked, out fake);
                Brain planner = new Brain { Model = "gpt-5-mini", Remote = new Cloud("sk-proj-test", "gpt-5-mini"), OwnSteps = true };
                planner.Act = opening;
                string planned = planner.Ask("can you get youtube going for me while i eat");
                string[] heard;
                lock (asked) heard = asked.ToArray();
                if (planned != "YouTube's up, choom." || string.Join(",", did.ToArray()) != "open_website:youtube.com" || heard.Length != 3 || !heard[2].Contains("Already done this turn"))
                    throw new Exception("Her planner opened YouTube more than once: " + planned + " / " + string.Join(",", did.ToArray()) + " / " + heard.Length + " calls");
                checks.Add("PASS an ask opens each app, site or song once: twenty calls at once from her hands open Blorbo Studio once, twenty written ones are cut to one, a planner calling YouTube twenty times at once and again a round later opens it once and hears it's done, and media still skips twice");
            }
            finally
            {
                Brain.OpenAi = openAiWas;
                Brain.MemoryPath = notesWas;
                Brain.MemoriesPath = memoriesWas;
                Brain.TranscriptPath = transcriptsWas;
                Brain.LogPath = logWas;
                Brain.Keeping = keepingWas;
                if (fake != null) fake.Stop();
            }
        }

        // What she knows about herself and her help page cover her window tool, the settings she can change herself, pausing and
        // timers, in the house style: exact menu names, short lines.
        private static void SmokeHelpForTools(List<string> checks)
        {
            string[,] asks =
            {
                { "how do I move a window to my other monitor?", "Arrange windows:" }, { "can you snap my window to the left?", "Arrange windows:" },
                { "what settings can you change yourself?", "Settings she can change:" }, { "can you make yourself bigger?", "Settings she can change:" },
                { "What is the Arrange windows setting in Capri?", "Arrange windows:" }, { "how do I make you bigger?", "Size:" },
                { "can you set a timer?", "Reminders:" }, { "can you turn discord down?", "App volume:" }, { "can you use the volume mixer?", "App volume:" },
                { "can you see my other monitor?", "Look at another monitor:" }, { "why is my pc lagging?", "How your PC is doing:" }, { "is my internet down?", "How your PC is doing:" }
            };
            for (int i = 0; i < asks.GetLength(0); i++)
            {
                string found = SelfKnow.Matching(asks[i, 0]) ?? "";
                if (!found.StartsWith(asks[i, 1], StringComparison.Ordinal)) throw new Exception("Self knowledge missed " + asks[i, 1] + " for: " + asks[i, 0] + " / " + found);
            }
            string doing = SelfKnow.Matching("what can you do?") ?? "", closing = SelfKnow.Matching("Explain the Close and switch apps and tabs setting") ?? "", sizing = SelfKnow.Matching("how do I make you bigger?") ?? "";
            string changing = SelfKnow.Matching("what settings can you change yourself?") ?? "", timing = SelfKnow.Matching("how do timers work?") ?? "";
            if (!doing.Contains("maximize, snap, move, resize") || !doing.Contains("changes her own settings when you ask") || !closing.Contains("maximizing, snapping, moving and resizing windows")
                || !SelfKnow.Matching("how do I move a window to my other monitor?").Contains("keep Discord on top, until unpin Discord") || !sizing.Contains("make yourself bigger or smaller works too") || !timing.Contains("set a timer for 10 minutes, and cancel my timer stops it") || !changing.Contains("Hologram, Watch my screen and game Comments, plus Size") || !changing.Contains("Keys, brains, her voice service, SECURITY, Remember what she sees, Listen for \"Hey Capri\" and Remember our chats stay yours"))
                throw new Exception("Self knowledge didn't say what her tools do now: " + doing + " / " + closing + " / " + changing);
            string root = null;
            foreach (string start in new[] { AppDomain.CurrentDomain.BaseDirectory, Environment.CurrentDirectory })
            {
                for (System.IO.DirectoryInfo folder = new System.IO.DirectoryInfo(start); folder != null && root == null; folder = folder.Parent)
                    if (System.IO.File.Exists(System.IO.Path.Combine(folder.FullName, "site", "help", "knowledge.md"))) root = folder.FullName;
                if (root != null) break;
            }
            if (root == null)
            {
                checks.Add("PASS self knowledge covers her window tool, the settings she can change herself and Close and switch apps and tabs");
                checks.Add("LIMIT her help page's new sections need the source tree; it is not beside this executable or working directory");
                return;
            }
            string help = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "site", "help", "knowledge.md"));
            foreach (string part in new[] { "### Move, snap and resize windows (0.8.0)", "### Her own settings (0.8.0)", "\"Send Discord to my other monitor\"", "Timers are reminders too (0.8.0)", "One app's own volume (0.8.0), with a cloud brain", "With more than one monitor (0.8.0), she looks at the one with the window you're using.", "### How your PC is doing (0.8.0)", "Ask \"is my internet down?\" or \"what's my ping?\" and she checks your connection too", "\"Keep Discord on top\" keeps it above your other windows until you say \"Unpin Discord\".", "\"Cancel my timer\" stops it.", "\"pause\" with nothing playing doesn't start your music", "4. Close and switch apps and tabs. It covers maximizing, snapping, moving and resizing windows too." })
                if (help.IndexOf(part, StringComparison.Ordinal) < 0) throw new Exception("Her help page is missing: " + part);
            int from = help.IndexOf("### Move, snap and resize windows (0.8.0)", StringComparison.Ordinal), to = help.IndexOf("### Discord and Windows shortcuts", from, StringComparison.Ordinal);
            if (to < from || help.Substring(from, to - from).IndexOf('\u2014') >= 0) throw new Exception("Her help page's new sections are out of place or use an em dash.");
            checks.Add("PASS self knowledge and her help page cover her window tool, the settings she can change herself, pausing only what's playing, timers, one app's own volume, looking at the monitor they name, how their PC is doing and Close and switch apps and tabs, with exact menu names");
        }

        // Her settings tool's sliders and switches on pretend settings: a step by name, number or one up or down, read
        // back after, ends and stuck sliders said plainly, and nothing off-limits within reach (Lachy).
        private static void SmokeSettingsTool(List<string> checks)
        {
            Func<string, bool?> settingWas = Hands.SettingOn;
            Func<string, bool, string> setWas = Hands.SetSetting;
            Func<string, int> levelWas = Hands.LevelOf;
            Func<string, int, string> moveWas = Hands.SetLevel;
            string openAiWas = Brain.OpenAi, notesWas = Brain.MemoryPath, memoriesWas = Brain.MemoriesPath, transcriptsWas = Brain.TranscriptPath, logWas = Brain.LogPath;
            bool keepingWas = Brain.Keeping;
            TcpListener fake = null;
            try
            {
                Dictionary<string, bool?> switches = new Dictionary<string, bool?> { { "wander", true }, { "head_pats", true }, { "sit_on_windows", true }, { "typing_sound", false } };
                Dictionary<string, int> levels = new Dictionary<string, int> { { "size", 3 }, { "wander", 1 }, { "pop_ins", 1 }, { "sleep", 3 }, { "volume", 8 }, { "voice_speed", 5 }, { "replies", 1 }, { "bubble", 1 } };
                List<string> moved = new List<string>();
                Hands.SettingOn = delegate(string name) { return switches.ContainsKey(name) ? switches[name] : null; };
                Hands.SetSetting = delegate(string name, bool on) { switches[name] = on; return null; };
                Hands.LevelOf = delegate(string name) { return levels.ContainsKey(name) ? levels[name] : -1; };
                // Her bubble's slider is stuck, the way a setting can be when something else holds it.
                Hands.SetLevel = delegate(string name, int step) { moved.Add(name + " " + step); if (name != "bubble") levels[name] = step; return null; };
                Func<string, string, string> set = delegate(string name, string level) { return Hands.Do("capri_settings", new ToolArguments { Action = "set", Name = name, Level = level }); };
                string[,] asks =
                {
                    { "size", "bigger", "Set my size to Medium+." }, { "size", "Giant", "Set my size to Giant." }, { "size", "giant", "My size is already Giant." },
                    // Giant stopped being the top at 0.9.319 and this read as though it still was, which is how
                    // her two biggest sizes went unreachable: "biggest" landed on Giant and left two above it.
                    { "size", "a bit bigger", "Set my size to Double." }, { "size", "biggest", "Set my size to Triple." },
                    { "size", "a bit bigger", "My size is already Triple, as far as it goes." },
                    { "size", "smallest", "Set my size to Tiny." }, { "size", "huge+", "Set my size to Huge+." },
                    { "wander", "off", "Set Wander to Off." }, { "wander", "more", "Set Wander to Rarely." }, { "wander", "Restless", "Set Wander to Restless." },
                    { "volume", "50", "Set my voice's volume to 50%." }, { "volume", "louder", "Set my voice's volume to 60%." }, { "volume", "max", "Set my voice's volume to 150%." }, { "volume", "mute", "Set my voice's volume to Mute." },
                    { "voice_speed", "1.3", "Set my voice's speed to 1.3x." }, { "voice_speed", "slower", "Set my voice's speed to 1.2x." }, { "voice_speed", "1x", "Set my voice's speed to Normal." }, { "voice_speed", "normal", "My voice's speed is already Normal." },
                    { "sleep", "10 minutes", "Set Sleep to 10 min." }, { "sleep", "never", "Set Sleep to Never." }, { "sleep", "to 30 min", "Set Sleep to 30 min." },
                    { "pop_ins", "often", "Set Pop-ins to Often." }, { "replies", "shorter", "Set Replies to Short." }, { "replies", "shorter", "Replies is already Short, as far as it goes." },
                    { "bubble", "long", "Couldn't set Bubble to Long: it's still Normal." }, { "size", "humongous", "There's no humongous for my size. It goes Tiny, Small, Small+, Medium, Medium+, Large, Large+, Huge, Huge+, Giant, Double, Triple." },
                    { "brain", "bigger", "There's no slider of mine called brain." }
                };
                List<string> wrong = new List<string>();
                for (int i = 0; i < asks.GetLength(0); i++)
                {
                    string got = set(asks[i, 0], asks[i, 1]);
                    if (got != asks[i, 2]) wrong.Add(asks[i, 0] + " " + asks[i, 1] + " said \"" + got + "\"");
                }
                if (wrong.Count > 0) throw new Exception("Her sliders didn't say where they ended up: " + string.Join(" / ", wrong.ToArray()));
                if (!Hands.Failed(set("bubble", "quick")) || !Hands.Failed(set("size", "humongous")) || Hands.Failed(set("size", "medium")) || string.Join(",", moved.ToArray()).IndexOf("bubble 2", StringComparison.Ordinal) < 0)
                    throw new Exception("A slider that didn't move wasn't read as a failure: " + string.Join(",", moved.ToArray()));
                levels["size"] = -1;
                string unread = set("size", "bigger");
                levels["size"] = 3;
                switches["head_pats"] = true;
                string pats = Hands.Do("capri_settings", new ToolArguments { Action = "off", Name = "head_pats" }), typing = Hands.Do("capri_settings", new ToolArguments { Action = "on", Name = "typing_sound" });
                if (unread != "Couldn't read my size just now." || pats != "Turned Head pats off." || typing != "Turned Typing sound on.")
                    throw new Exception("Her settings tool didn't read her new switches back: " + unread + " / " + pats + " / " + typing);
                // Only a slider takes set, only a switch takes on or off, and set needs a level; none of it asks first or counts as a check.
                ToolArguments bigger = new ToolArguments { Action = "set", Name = "size", Level = "bigger" };
                if (!Hands.Valid("capri_settings", bigger) || Hands.Valid("capri_settings", new ToolArguments { Action = "set", Name = "size" }) || Hands.Valid("capri_settings", new ToolArguments { Action = "set", Name = "head_pats", Level = "up" })
                    || Hands.Valid("capri_settings", new ToolArguments { Action = "on", Name = "size" }) || !Hands.Valid("capri_settings", new ToolArguments { Action = "off", Name = "sit_on_windows" }) || Hands.Valid("capri_settings", new ToolArguments { Action = "set", Name = "watch_my_screen", Level = "on" })
                    || Hands.Checking("capri_settings", bigger) || Security.Needs("capri_settings", bigger) != -1)
                    throw new Exception("Her settings tool's calls weren't checked.");
                // Nothing she mustn't touch is in reach: keys, brains, her voice service, SECURITY, memories or "Hey Capri". Every one of
                // her own settings is (Lachy).
                string tools = Hands.KitFor().WorkTools;
                int at = tools.IndexOf("\"name\":\"capri_settings\"", StringComparison.Ordinal);
                string spec = at < 0 ? "" : tools.Substring(at, tools.IndexOf("}}", tools.IndexOf("\"level\"", at), StringComparison.Ordinal) - at);
                string names = spec.Length == 0 ? "" : spec.Substring(spec.IndexOf("\"enum\":[\"voice\"", StringComparison.Ordinal));
                names = names.Substring(0, names.IndexOf(']') + 1);
                foreach (string never in new[] { "api_key", "brain", "model", "voice_service", "security", "blackwall", "permissions", "remember", "memories", "hey_capri", "password", "bypass" })
                    if (names.Contains("\"" + never + "\"")) throw new Exception("Her settings tool reached " + never + ".");
                foreach (string name in Hands.SettingNames())
                    if (Array.IndexOf(Hands.SettingActions, name) < 0 && !names.Contains("\"" + name + "\"")) throw new Exception("Her settings tool omitted " + name + ".");
                // OpenAI refuses every tool when one's description runs past 1024 characters, so none of hers may.
                for (int i = 0; i < Hands.Count; i++)
                    if (Hands.Description(i).Length > 1024) throw new Exception("OpenAI would refuse " + Hands.Name(i) + "'s description: " + Hands.Description(i).Length + " characters.");
                foreach (string name in new[] { "voice", "quiet", "mic", "check_ins", "wander", "dance_to_music", "head_pats", "chaos_when_upset", "legacy_stand", "sit_on_windows", "always_on_top", "mini_player", "typing_sound", "follow_ups", "hide_in_full_screen", "glitch_in", "clip_my_games", "save_big_moments", "live_reactions", "gaming_mode", "hologram", "watch_my_screen", "game_comments" })
                    if (!Hands.Valid("capri_settings", new ToolArguments { Action = "on", Name = name })) throw new Exception("Her settings tool's switch " + name + " wasn't taken.");
                foreach (string name in new[] { "size", "wander", "pop_ins", "sleep", "volume", "voice_speed", "replies", "bubble" })
                    if (!Hands.Valid("capri_settings", new ToolArguments { Action = "set", Name = name, Level = "up" })) throw new Exception("Her settings tool's slider " + name + " wasn't taken.");
                // Her sliders' steps are the ones her Mainframe's sliders stop on, by name and by value.
                string[] mainframeNames = { "SizeNames", "WanderNames", "PopNames", "SleepNames", "VolumeNames", "PaceNames", "Lengths", "BubbleNames" };
                for (int i = 0; i < mainframeNames.Length; i++)
                {
                    string[] shown = (string[])typeof(Mainframe).GetField(mainframeNames[i], System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
                    if (string.Join(",", shown) != string.Join(",", Hands.OwnSteps[i])) throw new Exception("Her settings tool's " + mainframeNames[i] + " aren't her Mainframe's: " + string.Join(",", Hands.OwnSteps[i]));
                }
                string[,] values = { { "SizeSteps", "OwnSizes" }, { "Sleeps", "OwnSleeps" }, { "Volumes", "OwnVolumes" }, { "Paces", "OwnPaces" } };
                for (int i = 0; i < values.GetLength(0); i++)
                {
                    int[] shown = (int[])typeof(Mainframe).GetField(values[i, 0], System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
                    int[] own = (int[])typeof(Companion).GetField(values[i, 1], System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
                    if (string.Join(",", Array.ConvertAll(shown, step => step.ToString())) != string.Join(",", Array.ConvertAll(own, step => step.ToString()))) throw new Exception("Her settings tool's " + values[i, 1] + " aren't her Mainframe's " + values[i, 0] + ".");
                }
                // Saying she changed one with no tool behind it is a claim; questions and where a setting lives aren't.
                List<KeyValuePair<string, ToolArguments>> none = new List<KeyValuePair<string, ToolArguments>>();
                string[,] claims =
                {
                    { "There, I made myself bigger!", "capri_settings" }, { "Okay, I stopped wandering.", "capri_settings" }, { "Turned off head pats, choom.", "capri_settings" }, { "Set my volume to 50%.", "capri_settings" },
                    { "I switched Always on top off.", "capri_settings" }, { "Turned on the legacy stand, choom.", "capri_settings" }, { "I switched legacy stand off.", "capri_settings" },
                    { "Want me to make myself bigger?", "" }, { "You can make me bigger in Settings.", "" }, { "Head pats are on by default.", "" }, { "Legacy stand is off by default.", "" }, { "Should I stop wandering?", "" }
                };
                for (int i = 0; i < claims.GetLength(0); i++)
                    if ((Hands.Claimed(claims[i, 0], none) ?? "") != claims[i, 1]) throw new Exception("\"" + claims[i, 0] + "\" read as a claim for " + (Hands.Claimed(claims[i, 0], none) ?? "nothing") + " instead of " + (claims[i, 1].Length == 0 ? "nothing" : claims[i, 1]));
                // A brain that works jobs out makes itself bigger and stops wandering with the tool, and can't just say it did.
                List<string> asked = new List<string>();
                Func<string, string> says = delegate(string text) { return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":" + Voice.Quote(text) + "}}]}"; };
                Func<string, string, string> calls = delegate(string name, string json) { return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"" + name + "\",\"arguments\":" + Voice.Quote(json) + "}}]}}]}"; };
                levels["size"] = 3;
                switches["wander"] = true;
                Brain.OpenAi = FakeServer(new[]
                {
                    calls("capri_settings", "{\"action\":\"set\",\"name\":\"size\",\"level\":\"bigger\"}"), says("There, a bit bigger, choom."),
                    calls("capri_settings", "{\"action\":\"off\",\"name\":\"wander\"}"), says("Okay, I'll stay put."),
                    says("Done, I made myself bigger."), says("Done, I made myself bigger."),
                    calls("capri_settings", "{\"action\":\"on\",\"name\":\"legacy_stand\"}"), says("Back on my old stand.")
                }, asked, out fake);
                Brain.MemoryPath = Brain.MemoriesPath = Brain.TranscriptPath = Brain.LogPath = null;
                Brain.Keeping = false;
                List<string> did = new List<string>();
                Brain planner = new Brain { Model = "gpt-5-mini", Remote = new Cloud("sk-proj-test", "gpt-5-mini"), OwnSteps = true };
                planner.Act = delegate(string name, ToolArguments args) { did.Add(name + ":" + args.Name + ":" + args.Action + (args.Level == null ? "" : ":" + args.Level)); return Hands.Do(name, args); };
                string senpai = planner.Ask("make yourself bigger");
                string still = planner.Ask("stop wandering");
                string faked = planner.Ask("make yourself even bigger");
                string[] sent;
                lock (asked) sent = asked.ToArray();
                if (senpai != "There, a bit bigger, choom." || still != "Okay, I'll stay put." || faked != "That setting hasn't changed yet. It's in Capri Mainframe > Settings." || sent.Length != 6
                    || string.Join(",", did.ToArray()) != "capri_settings:size:set:bigger,capri_settings:wander:off" || levels["size"] != 4 || switches["wander"] != false
                    || !sent[0].Contains("like set size bigger for make yourself bigger") || !sent[0].Contains("\"voice_speed\"") || !sent[1].Contains("Set my size to Medium+.") || !sent[3].Contains("Turned Wander off.")
                    || !sent[5].Contains("You said you did something that needs capri_settings"))
                    throw new Exception("Her planner didn't change her settings with the tool: " + senpai + " / " + still + " / " + faked + " / " + string.Join(",", did.ToArray()) + " / " + sent.Length + " calls");
                // In plain words, the way Chaos when upset gets there: nothing routes it by its wording, so her brain has to see the
                // switch and what it's for in the tool she's handed, and turning it on goes through the tool like the rest.
                switches["legacy_stand"] = false;
                did.Clear();
                string older = planner.Ask("can you go back to your old stand?");
                lock (asked) sent = asked.ToArray();
                if (older != "Back on my old stand." || string.Join(",", did.ToArray()) != "capri_settings:legacy_stand:on" || switches["legacy_stand"] != true || !sent[6].Contains("\"legacy_stand\"") || !sent[6].Contains("your old stand, for mods your new one broke") || !sent[7].Contains("Turned Legacy stand on."))
                    throw new Exception("A plain request didn't reach Legacy stand as a tool: " + older + " / " + string.Join(",", did.ToArray()));
                checks.Add("PASS her settings tool moves her sliders: size, wander, pop-ins, sleep, her voice's volume and speed, replies and bubble, by a step's name, a number, or a step up or down, says when one is already as far as it goes, when a step doesn't exist and when a slider didn't move, and flips her everyday switches like head pats, dancing, sitting on windows and always on top; only sliders take set and nothing she mustn't touch is in reach; her steps match her Mainframe's; saying she changed one with no tool is a claim; a planner makes herself bigger and stops wandering with the tool");
            }
            finally
            {
                Hands.SettingOn = settingWas;
                Hands.SetSetting = setWas;
                Hands.LevelOf = levelWas;
                Hands.SetLevel = moveWas;
                Brain.OpenAi = openAiWas;
                Brain.MemoryPath = notesWas;
                Brain.MemoriesPath = memoriesWas;
                Brain.TranscriptPath = transcriptsWas;
                Brain.LogPath = logWas;
                Brain.Keeping = keepingWas;
                if (fake != null) fake.Stop();
            }
        }

        // Her settings tool on her real switches and sliders: each new switch flipped and flipped back the way its own switch does,
        // and each slider moved and read back from the setting itself, with nothing of her own said over her brain's reply.
        private void SmokeOwnSliders(List<string> checks)
        {
            bool danceWas = preferences.NoDance, patsWas = preferences.NoHeadPats, chaosWas = preferences.UpsetChaos, legacyWas = preferences.LegacyStand, sitWas = preferences.NoSitting, topWas = preferences.OnTop, playerWas = preferences.NoPlayer;
            bool followWas = preferences.FollowUps, fullWas = preferences.HideFullScreen, introWas = preferences.NoIntro;
            int typingWas = preferences.Typing, sizeWas = preferences.Size, wanderWas = WanderLevel, popWas = preferences.PopPace, sleepWas = preferences.SleepAfter, volumeWas = Voice.Loudness, speedWas = VoiceFx.Speed, wordsWas = Brain.Wordiness, bubbleWas = preferences.BubbleTime;
            Action<short[]> typeOutWas = Sfx.TypeOut;
            int askingWas = asking;
            try
            {
                // Her typing sound plays a sample when it's turned on; the check keeps it off their speakers.
                Sfx.TypeOut = null;
                List<string> wrong = new List<string>();
                string[,] switches =
                {
                    { "dance_to_music", "Dance to music" }, { "head_pats", "Head pats" }, { "chaos_when_upset", "Chaos when upset" }, { "legacy_stand", "Legacy stand" }, { "sit_on_windows", "Sit on windows" }, { "always_on_top", "Always on top" },
                    { "mini_player", "Mini player" }, { "typing_sound", "Typing sound" }, { "follow_ups", "Follow-up messages" }, { "hide_in_full_screen", "Hide in full screen" }, { "glitch_in", "Glitch in at startup" }
                };
                Func<string, bool> real = delegate(string name)
                {
                    switch (name)
                    {
                        case "dance_to_music": return !preferences.NoDance;
                        case "head_pats": return !preferences.NoHeadPats;
                        case "chaos_when_upset": return preferences.UpsetChaos;
                        case "legacy_stand": return preferences.LegacyStand;
                        case "sit_on_windows": return !preferences.NoSitting;
                        case "always_on_top": return preferences.OnTop;
                        case "mini_player": return !preferences.NoPlayer;
                        case "typing_sound": return TypingSound;
                        case "follow_ups": return preferences.FollowUps;
                        case "hide_in_full_screen": return preferences.HideFullScreen;
                        default: return !preferences.NoIntro;
                    }
                };
                for (int i = 0; i < switches.GetLength(0); i++)
                {
                    string name = switches[i, 0];
                    bool was = real(name);
                    string flipped = Hands.Do("capri_settings", new ToolArguments { Action = was ? "off" : "on", Name = name });
                    bool now = real(name), lifted = TopMost == preferences.OnTop;
                    string back = Hands.Do("capri_settings", new ToolArguments { Action = was ? "on" : "off", Name = name });
                    if (flipped != "Turned " + switches[i, 1] + (was ? " off." : " on.") || now == was || back != "Turned " + switches[i, 1] + (was ? " on." : " off.") || real(name) != was || name == "always_on_top" && !lifted)
                        wrong.Add(name + ": " + flipped + " / " + back);
                }
                if (wrong.Count > 0) throw new Exception("Her settings tool didn't flip her real switches: " + string.Join(" | ", wrong.ToArray()));
                SetCharacterSize(240);
                SetWanderLevel(1);
                SetPopPace(1);
                SetSleepAfter(300);
                SetVolume(80);
                SetVoiceSpeed(120);
                SetWordiness(1);
                SetBubbleTime(1);
                Func<string, string, string> set = delegate(string name, string level) { return Hands.Do("capri_settings", new ToolArguments { Action = "set", Name = name, Level = level }); };
                string listed = Hands.Do("capri_settings", new ToolArguments { Action = "list" });
                string bigger = set("size", "bigger"), giant = set("size", "giant"), wander = set("wander", "often"), still = set("wander", "off"), pops = set("pop_ins", "rarely"), sleep = set("sleep", "10 min");
                string volume = set("volume", "50%"), speed = set("voice_speed", "faster"), replies = set("replies", "long"), bubble = set("bubble", "quick");
                if (!listed.Contains("my size: Medium; Wander: Rarely; Pop-ins: Sometimes; Sleep: 5 min; my voice's volume: 80%; my voice's speed: 1.2x; Replies: Medium; Bubble: Normal;")
                    // Giant is 560; the steps past it (Huge+ up to Triple) came later, so Giant is no longer her largest.
                    || bigger != "Set my size to Medium+." || giant != "Set my size to Giant." || preferences.Size != 560 || Size != CanvasSize(560)
                    || wander != "Set Wander to Often." || still != "Set Wander to Off." || preferences.Wander || WanderLevel != 0 || preferences.WanderPace != 2
                    || pops != "Set Pop-ins to Rarely." || preferences.PopPace != 0 || sleep != "Set Sleep to 10 min." || preferences.SleepAfter != 600
                    || volume != "Set my voice's volume to 50%." || Voice.Loudness != 50 || preferences.Volume != 50 || speed != "Set my voice's speed to 1.3x." || VoiceFx.Speed != 130
                    || replies != "Set Replies to Long." || Brain.Wordiness != 2 || bubble != "Set Bubble to Quick." || preferences.BubbleTime != 0 || asking != askingWas)
                    throw new Exception("Her settings tool didn't move her real sliders: " + listed + " / " + bigger + " / " + giant + " / " + wander + " / " + still + " / " + pops + " / " + sleep + " / " + volume + " / " + speed + " / " + replies + " / " + bubble + " / asking moved " + (asking - askingWas)
                        + " / size " + preferences.Size + " canvas " + Size + ", wander " + preferences.Wander + " " + WanderLevel + " " + preferences.WanderPace
                        + ", pops " + preferences.PopPace + ", sleep " + preferences.SleepAfter + ", loud " + Voice.Loudness + " " + preferences.Volume + ", speed " + VoiceFx.Speed
                        + ", words " + Brain.Wordiness + ", bubble " + preferences.BubbleTime);
                checks.Add("PASS her settings tool flips Dance to music, Head pats, Chaos when upset, Legacy stand, Sit on windows, Always on top, Mini player, Typing sound, Follow-up messages, Hide in full screen and Glitch in at startup the way their own switches do, moves her size, wander, pop-ins, sleep, voice volume and speed, replies and bubble the way her Mainframe's sliders do, reads each one back from the setting itself, and never talks over her brain's reply");
            }
            finally
            {
                Sfx.TypeOut = typeOutWas;
                if (preferences.NoDance != danceWas) ToggleDance();
                if (preferences.NoHeadPats != patsWas) ToggleHeadPats();
                if (preferences.UpsetChaos != chaosWas) ToggleUpsetChaos();
                if (preferences.LegacyStand != legacyWas) ToggleLegacyStand();
                if (preferences.NoSitting != sitWas) ToggleSitting();
                if (preferences.OnTop != topWas) ToggleOnTop();
                if (preferences.NoPlayer != playerWas) TogglePlayer();
                if (preferences.FollowUps != followWas) ToggleFollowUps();
                if (preferences.HideFullScreen != fullWas) ToggleHideFullScreen();
                if (preferences.NoIntro != introWas) ToggleIntro();
                preferences.Typing = typingWas;
                if (preferences.Size != sizeWas) SetCharacterSize(sizeWas);
                SetWanderLevel(wanderWas);
                SetPopPace(popWas);
                SetSleepAfter(sleepWas);
                SetVolume(volumeWas);
                SetVoiceSpeed(speedWas);
                SetWordiness(wordsWas);
                SetBubbleTime(bubbleWas);
                SavePreferences();
                mainframe.Sync();
            }
        }

        // "Play Bloons TD 6" is one job that opens the game and keeps playing, by voice or text with any brain, and a
        // song is still a song (Lachy). Every voice reply with no tool behind it gets her brain's second look; a
        // brain's pick is never swapped.
        private void SmokePlayJobs(List<string> checks)
        {
            Func<List<InstalledApp>> catalogWas = Hands.Catalog;
            Func<string, string, string> workWas = Hands.Work;
            Action<string> runWas = Hands.Run;
            Func<string, string, List<KeyValuePair<string, ToolArguments>>> keepWas = keepTest;
            int keepWaitWas = KeepWait;
            string modelWas = brain.Model;
            bool chattingWas = chatting, localWas = local;
            Agent agentWas = agent, pretend = null;
            List<string> jobs = new List<string>(), problems = new List<string>(), launches = new List<string>();
            int looks = 0;
            Func<List<KeyValuePair<string, ToolArguments>>, string> picked = delegate(List<KeyValuePair<string, ToolArguments>> list)
            {
                List<string> names = new List<string>();
                foreach (KeyValuePair<string, ToolArguments> call in list) names.Add(call.Key + (call.Value != null && call.Value.Task != null ? " " + call.Value.Task : ""));
                return "[" + string.Join(", ", names.ToArray()) + "]";
            };
            Func<string, ToolArguments, KeyValuePair<string, ToolArguments>> pick = delegate(string tool, ToolArguments args) { return new KeyValuePair<string, ToolArguments>(tool, args); };
            try
            {
                Hands.Catalog = delegate { return new List<InstalledApp> { new InstalledApp("Bloons TD 6", "btd"), new InstalledApp("Spotify", "spotify"), new InstalledApp("Discord", "discord") }; };
                Hands.Forget();
                Hands.Work = delegate(string job, string asked) { lock (jobs) jobs.Add(job); return "Did it."; };
                Hands.Run = delegate(string id) { lock (launches) launches.Add(id); };
                Security.Pick(Security.Trusted);
                brain.Model = "smoke-test-model";
                string tool;
                ToolArguments args;

                // Told to play, in the ways people say it, is the job. Wanting to play, or opening, isn't, and apps for listening only open.
                if (!Hands.Heard("Hey Capri, play Bloons TD 6", out tool, out args, true) || tool != "use_computer") problems.Add("\"Hey Capri, play Bloons TD 6\" went to " + tool);
                foreach (string told in new[] { "play bloons td 6", "Three, play \"Balloons Tower Defense 6.\"", "go play bloons", "can you play bloons for me", "you should play bloons", "i want you to play bloons", "let's play bloons", "open discord and play bloons" })
                    if (!Hands.ToldToPlay(told, "Bloons TD 6")) problems.Add("not told to play: " + told);
                foreach (string notTold in new[] { "open bloons td 6", "i want to play bloons", "i wanna play bloons", "so i can play bloons", "i'll play bloons later" })
                    if (Hands.ToldToPlay(notTold, "Bloons TD 6")) problems.Add("told to play: " + notTold);
                if (Hands.ToldToPlay("play spotify", "Spotify")) problems.Add("Spotify counted as a game to play");
                if (!Hands.Direct("play spotify", out tool, out args) || tool != "open_app") problems.Add("\"play spotify\" went to " + tool);
                if (!Hands.Direct("open bloons td 6", out tool, out args) || tool != "open_app") problems.Add("\"open bloons td 6\" went to " + tool);

                // Songs: a bare title is left to a brain when there is one, a brain's song stands, and with no brain a title is music.
                Hands.Kit kit = Hands.KitFor(false);
                if (Hands.Heard("play blinding lights", out tool, out args, true) && tool == "play_music") problems.Add("a bare title skipped the brain");
                List<KeyValuePair<string, ToolArguments>> calls = new List<KeyValuePair<string, ToolArguments>> { pick("play_music", new ToolArguments { Song = "blinding lights" }) };
                Hands.Rescue("play blinding lights", calls, kit);
                if (calls.Count != 1 || calls[0].Key != "play_music") problems.Add("a brain's song was overruled: " + picked(calls));
                calls = new List<KeyValuePair<string, ToolArguments>>();
                Hands.Rescue("play stay with me", calls, kit);
                if (calls.Count != 1 || calls[0].Key != "play_music") problems.Add("with no pick, a title stopped being music: " + picked(calls));
                if (!Hands.Wants("play blinding lights")) problems.Add("\"play blinding lights\" isn't asking for anything");
                ToolArguments music;
                if (Hands.MusicJob("play Balloons Tower Defense 6", out music)) problems.Add("a game job became a song");
                if (!Hands.MusicJob("play some music", out music)) problems.Add("a music job stopped being a song");

                // A brain's pick is never swapped: an open for a game they said to play stays its open, an open stays an open, and a
                // play job stays a play job.
                calls = new List<KeyValuePair<string, ToolArguments>> { pick("open_app", new ToolArguments { Name = "Bloons TD 6" }) };
                Hands.Rescue("Capri, play Bloons TD 6.", calls, kit);
                if (calls.Count != 1 || calls[0].Key != "open_app") problems.Add("a brain's open for a game was swapped: " + picked(calls));
                calls = new List<KeyValuePair<string, ToolArguments>> { pick("open_app", new ToolArguments { Name = "Bloons TD 6" }) };
                Hands.Rescue("open bloons td 6", calls, kit);
                if (calls.Count != 1 || calls[0].Key != "open_app") problems.Add("an open became " + picked(calls));
                calls = new List<KeyValuePair<string, ToolArguments>> { pick("use_computer", new ToolArguments { Task = "play bloons td 6" }) };
                Hands.Rescue("play bloons td 6", calls, kit);
                if (calls.Count != 1 || calls[0].Key != "use_computer") problems.Add("a brain's play job became " + picked(calls));

                // What every brain reads.
                string openText = null;
                for (int i = 0; i < Hands.Count; i++) if (Hands.Name(i) == "open_app") openText = Hands.Description(i);
                if (openText == null || openText.IndexOf("or play", StringComparison.OrdinalIgnoreCase) >= 0 || openText.IndexOf("call use_computer once instead", StringComparison.Ordinal) < 0) problems.Add("open_app still offers to play a game");
                if (Agent.Prompt.IndexOf("only a tool can", StringComparison.Ordinal) < 0) problems.Add("voice chat's prompt lost its only-a-tool line");
                List<string> unfound = Hands.Unfound();
                if (unfound.Count > 0) problems.Add("lines a weaker brain drops weren't found: " + string.Join(" | ", unfound.ToArray()));
                if (Computer.Playing.IndexOf("you keep playing until they stop you", StringComparison.Ordinal) < 0 || Computer.Playing.IndexOf("turns out to be a song or a video", StringComparison.Ordinal) < 0) problems.Add("game jobs lost their keep-playing rule");
                if (Computer.Finishing.IndexOf("they play until they're told to stop", StringComparison.Ordinal) < 0 || Computer.Finishing.IndexOf("a song or video and it's playing", StringComparison.Ordinal) < 0) problems.Add("the done check would end a plain play job");

                // Voice chat: every reply with no tool behind it gets her brain's second look after a short wait, and its pick runs as it is.
                chatting = true;
                local = false;
                pretend = new Agent();
                agent = pretend;
                KeepWait = 150;
                List<KeyValuePair<string, ToolArguments>> keepPick = new List<KeyValuePair<string, ToolArguments>>();
                bool keepThrows = false;
                keepTest = delegate(string words, string said)
                {
                    Interlocked.Increment(ref looks);
                    if (keepThrows) throw new InvalidOperationException("The smoke brain is down.");
                    return new List<KeyValuePair<string, ToolArguments>>(keepPick);
                };
                Func<string, string, Action, string> voice = delegate(string heardLine, string saidLine, Action between)
                {
                    lock (jobs) jobs.Clear();
                    Interlocked.Exchange(ref looks, 0);
                    ChatHeard(heardLine);
                    ChatReplied(saidLine);
                    if (between != null) between();
                    System.Diagnostics.Stopwatch wait = System.Diagnostics.Stopwatch.StartNew();
                    while (wait.ElapsedMilliseconds < 1500) { lock (jobs) if (jobs.Count > 0) break; Thread.Sleep(20); }
                    Thread.Sleep(250);
                    lock (jobs) return Thread.VolatileRead(ref looks) + " looks, jobs [" + string.Join(" | ", jobs.ToArray()) + "]";
                };
                keepPick.Add(pick("use_computer", new ToolArguments { Task = "play Bloons TD 6 and pick a map" }));
                string picks = voice("Hey Capri, play Bloons TD 6 and pick a map.", "On it, opening Bloons TD 6 now.", null);
                if (picks != "1 looks, jobs [play Bloons TD 6 and pick a map]") problems.Add("her brain's play job didn't run as it picked it: " + picks);
                keepPick.Clear();
                keepPick.Add(pick("open_app", new ToolArguments { Name = "Bloons TD 6" }));
                lock (launches) launches.Clear();
                string opens = voice("Hey Capri, play Bloons TD 6 and pick a map for me.", "Opening Bloons TD 6.", null);
                string opened;
                lock (launches) opened = string.Join(" | ", launches.ToArray());
                if (opens != "1 looks, jobs []" || opened != "btd") problems.Add("her brain's open_app pick was swapped: " + opens + ", opened [" + opened + "]");
                keepPick.Clear();
                keepPick.Add(pick("use_computer", new ToolArguments { Task = "play Bloons TD 6 and pick a map you like" }));
                // A plain "play" and the game starts the job as she hears it, before any reply.
                string heardJob = voice("Capri, play Bloons TD 6.", "Starting it up.", null);
                if (heardJob != "0 looks, jobs [play Bloons TD 6.]") problems.Add("\"Capri, play Bloons TD 6.\" didn't start the job as she heard it: " + heardJob);
                keepThrows = true;
                string forced = voice("Capri, play Bloons TD 6 and pick a hard map.", "I'll open Bloons TD 6 and play it.", null);
                if (!forced.StartsWith("1 looks, jobs [", StringComparison.Ordinal) || forced.IndexOf("play Bloons TD 6 and pick a hard map", StringComparison.Ordinal) < 0 || forced.IndexOf(" | ", StringComparison.Ordinal) >= 0) problems.Add("a promise with her brain down didn't run the one job: " + forced);
                keepThrows = false;
                string agentCalled = voice("Capri, play Bloons TD 6 and pick an easy map.", "Sure, starting Bloons TD 6.", delegate { lock (shortcutGate) toolAsked = true; });
                if (agentCalled != "0 looks, jobs []") problems.Add("the agent's own call still got a second look: " + agentCalled);
                string talkedOver = voice("Capri, play Bloons TD 6 and pick any map.", "Yeah, let me get Bloons TD 6 going.", delegate { ChatHeard("hmm hold on a sec"); });
                if (talkedOver != "0 looks, jobs []") problems.Add("a reply they talked over still got a second look: " + talkedOver);
                string askedBack = voice("Capri, play Bloons TD 6 and pick a map you like.", "Want me to play Bloons TD 6?", null);
                if (askedBack != "1 looks, jobs [play Bloons TD 6 and pick a map you like]") problems.Add("her asking back didn't get a second look that runs its pick: " + askedBack);
                keepPick.Clear();
                string chat = voice("how was your day, Capri?", "Pretty chill, just vibing on your desktop.", null);
                if (chat != "1 looks, jobs []") problems.Add("plain chat didn't get a second look, or its look ran something: " + chat);
                string nothing = voice("can you play bloons td 6, Capri?", "Bloons is such a vibe.", null);
                if (nothing != "1 looks, jobs []") problems.Add("a look that picked nothing still ran something, or never looked: " + nothing);

                // The agent's own open_app runs as it picked it, even for a game they said to play.
                lock (jobs) jobs.Clear();
                lock (launches) launches.Clear();
                string acted = AgentAct(null, "open_app", new ToolArguments { Name = "Bloons TD 6", Asked = "Capri, play Bloons TD 6." });
                string actedJobs, actedOpens;
                lock (jobs) actedJobs = string.Join(" | ", jobs.ToArray());
                lock (launches) actedOpens = string.Join(" | ", launches.ToArray());
                if (acted == null || !acted.StartsWith("Opened Bloons TD 6", StringComparison.Ordinal) || actedJobs.Length > 0 || actedOpens != "btd") problems.Add("the agent's open_app was swapped: " + acted + " [" + actedJobs + "] opened [" + actedOpens + "]");
            }
            finally
            {
                keepTest = keepWas;
                KeepWait = keepWaitWas;
                agent = agentWas;
                chatting = chattingWas;
                local = localWas;
                lock (shortcutGate) { toolAsked = false; heardLast = null; }
                if (pretend != null) pretend.Dispose();
                Hands.Catalog = catalogWas;
                Hands.Forget();
                Hands.Work = workWas;
                Hands.Run = runWas;
                brain.Model = modelWas;
                Security.Drop();
            }
            if (problems.Count > 0) throw new Exception("Play jobs went wrong: " + string.Join("; ", problems.ToArray()));
            checks.Add("PASS play jobs: \"play Bloons TD 6\" is one job that opens the game and keeps playing, by voice or text, and it starts as she hears it in voice chat; open, wanting to play and apps for listening still only open; a bare title is left to a brain, and a brain's or the agent's pick is never swapped; every voice reply with no tool behind it gets her brain's second look, plain chat and a question back too, except a reply they talked over and the agent's own call, and a promise still runs when her brain can't look");
        }

        // Her own brain holds every tool once it says a message is a job, and answers chat without them (Lachy). A
        // plain ask goes straight to its tools, set phrases don't run first, and a model that refuses tools falls back
        // to her hands' shortcuts.
        private static void SmokeLocalWorkingOut(List<string> checks)
        {
            string ollamaWas = Brain.Ollama, notesWas = Brain.MemoryPath, memoriesWas = Brain.MemoriesPath, transcriptsWas = Brain.TranscriptPath, logWas = Brain.LogPath;
            bool keepingWas = Brain.Keeping;
            Func<string> openWas = Computer.GameOpen;
            Func<List<InstalledApp>> catalogWas = Hands.Catalog;
            Func<string, string> says = delegate(string text) { return "{\"message\":{\"role\":\"assistant\",\"content\":" + Voice.Quote(text) + "},\"done\":true}"; };
            Func<string, string, string> calls = delegate(string name, string json) { return "{\"message\":{\"role\":\"assistant\",\"content\":\"\",\"tool_calls\":[{\"function\":{\"name\":\"" + name + "\",\"arguments\":" + json + "}}]},\"done\":true}"; };
            List<string> asked = new List<string>(), did = new List<string>(), problems = new List<string>();
            TcpListener fake = null;
            Func<int, bool> tooled = delegate(int index) { lock (asked) return index < asked.Count && asked[index].IndexOf("\"tools\":", StringComparison.Ordinal) >= 0; };
            try
            {
                try
                {
                    Brain.Ollama = FakeServer(new[]
                    {
                        says("Pretty chill, just vibing on your desktop."),
                        says("act"), calls("play_music", "{\"song\":\"lofi beats\"}"), says("Put some lofi on, choom."),
                        calls("open_app", "{\"name\":\"Discord\"}"), says("Discord's up."),
                        "!400 {\"error\":\"registry.ollama.ai/library/old:latest does not support tools\"}", says("act"), says("open_app{\"name\":\"Discord\"}"), says("Discord's open, choom.")
                    }, asked, out fake);
                }
                catch (SocketException) { checks.Add("LIMIT her local brain's tools: the test server couldn't open"); return; }
                Brain.MemoryPath = Brain.MemoriesPath = Brain.TranscriptPath = Brain.LogPath = null;
                Brain.Keeping = false;
                Computer.GameOpen = delegate { return null; };
                Hands.Catalog = delegate { return new List<InstalledApp> { new InstalledApp("Discord", "discord") }; };
                Hands.Forget();
                Func<string, ToolArguments, string> act = delegate(string name, ToolArguments args) { lock (did) did.Add(name + ":" + (args.Song ?? args.Name)); return name == "play_music" ? "Playing lofi beats." : "Opened " + args.Name + "."; };
                Brain local = new Brain { Model = "local-test", OwnSteps = true, Act = act };
                if (!local.WorksOut() || !local.OnThisPc || !local.HoldsTools()) problems.Add("her brain on their PC didn't work jobs out itself");

                // Chat: one call, without the tool list.
                local.Ask("how's your day going?");
                int chatCalls;
                lock (asked) chatCalls = asked.Count;
                if (chatCalls != 1 || tooled(0) || did.Count > 0) problems.Add("chat took " + chatCalls + " calls, carried the tools or ran one");

                // Her brain says it's a job: it gets every tool and picks, and its own line is her reply.
                const string bored = "my vibe is so off today";
                if (Hands.Wants(bored)) problems.Add("the test's hint counted as a plain ask");
                string played = new Brain { Model = "local-test", OwnSteps = true, Act = act }.Ask(bored);
                lock (asked) if (asked.Count != 4 || tooled(1) || !tooled(2) || !tooled(3)) problems.Add("a job her brain named didn't go to its tools: " + asked.Count + " calls");
                if (did.Count != 1 || did[0] != "play_music:lofi beats" || played != "Put some lofi on, choom.") problems.Add("her brain's own pick didn't run as it picked it: [" + string.Join(", ", did.ToArray()) + "] " + played);

                // A plain ask goes straight to her tools, and the open shortcut waits for her brain.
                string opened = new Brain { Model = "local-test", OwnSteps = true, Act = act }.Ask("open discord");
                lock (asked) if (asked.Count != 6 || !tooled(4) || !tooled(5)) problems.Add("a plain ask didn't go straight to her tools: " + asked.Count + " calls");
                if (did.Count != 2 || did[1] != "open_app:Discord" || opened != "Discord's up.") problems.Add("a plain ask ran wrong: [" + string.Join(", ", did.ToArray()) + "] " + opened);

                // A model that turns tools down goes back to her hands' shortcuts, from that message on.
                Brain old = new Brain { Model = "old", OwnSteps = true, Act = act };
                string oldOpened = old.Ask("open discord");
                lock (asked) if (asked.Count != 10 || !tooled(6) || tooled(7) || tooled(8) || tooled(9)) problems.Add("a model without tools didn't fall back to written calls: " + asked.Count + " calls");
                if (did.Count != 3 || did[2] != "open_app:Discord" || old.WorksOut() || old.HoldsTools() || !local.WorksOut()) problems.Add("the fallback didn't run or wasn't remembered for that model alone: [" + string.Join(", ", did.ToArray()) + "] " + oldOpened);
            }
            finally
            {
                if (fake != null) fake.Stop();
                Brain.Ollama = ollamaWas;
                Brain.MemoryPath = notesWas; Brain.MemoriesPath = memoriesWas; Brain.TranscriptPath = transcriptsWas; Brain.LogPath = logWas;
                Brain.Keeping = keepingWas;
                Computer.GameOpen = openWas;
                Hands.Catalog = catalogWas;
                Hands.Forget();
            }
            if (problems.Count > 0) throw new Exception("Her local brain's tools went wrong: " + string.Join("; ", problems.ToArray()));
            checks.Add("PASS her brain on their PC holds her tools (Lachy, 8 Nov): chat is one call without the tool list, a job it names or a plain ask goes to every tool and its own pick runs, the open shortcut waits for it, and a model that turns tools down falls back to written calls for that model alone");
        }

        // A personality whose file is gone. Her lists are built from the folder so the menus and the editor
        // lose it on their own; what is checked here is the saved choice, which used to go on naming it.
        private void SmokeGonePack(List<string> checks)
        {
            List<string> problems = new List<string>();
            if (!Gone("A pack that is not there " + Guid.NewGuid().ToString("N").Substring(0, 6)))
                problems.Add("a name with no file was not seen as gone");
            if (Gone(Personalities.Stock)) problems.Add("her own lines were treated as a missing pack");
            if (Gone(Personalities.Dynamic)) problems.Add("Dynamic was treated as a missing pack");
            if (Gone(null) || Gone("")) problems.Add("no choice at all was treated as a missing pack");
            foreach (string one in Personalities.Names)
                if (Gone(one)) { problems.Add("a pack she lists was seen as gone: " + one); break; }
            // A drift into a pack with no file is skipped rather than followed, which is the other half of it.
            if (problems.Count > 0) throw new Exception("A deleted personality went wrong: " + string.Join("; ", problems.ToArray()));
            checks.Add("PASS a personality whose file is gone: the saved choice falls back to her own lines and is written back, her own lines and Dynamic are modes rather than files and are never treated as missing, and every pack she lists is seen as present");
        }

        // A sweep of everything she has written, for the senpai side in a censored install: not a test of one
        // mechanism, but every file read for what only exists with the switch on.
        private void SmokeCensoredSweep(List<string> checks)
        {
            List<string> problems = new List<string>();
            if (Brain.SenpaiOn)
            {
                checks.Add("SKIP the censored sweep: the switch is on here, and the sweep is about what a censored install has");
                return;
            }
            try
            {
                string folder = Personalities.SettingsFolder;
                if (string.IsNullOrEmpty(folder) || !System.IO.Directory.Exists(folder))
                {
                    checks.Add("SKIP the censored sweep: she has written no files to read");
                    return;
                }

                // The folders and files that only exist with the switch on.
                foreach (string found in System.IO.Directory.GetDirectories(folder, "*", System.IO.SearchOption.AllDirectories))
                {
                    string leaf = System.IO.Path.GetFileName(found);
                    if (string.Equals(leaf, Personalities.SenpaiName, StringComparison.OrdinalIgnoreCase) || Legacy.WasFolder(leaf))
                        problems.Add("a senpai folder exists: " + found.Substring(folder.Length).TrimStart('\\'));
                }
                // The old names are null in a build without the senpai files, which is the build this check is
                // for, so a null one is skipped rather than handed to Path.Combine.
                foreach (string named in new[] { Interests.FileName, Interests.HersName, Interests.YoursName, Interests.WasFileName, Interests.WasHersName, Interests.WasYoursName })
                    if (!string.IsNullOrEmpty(named) && System.IO.File.Exists(System.IO.Path.Combine(folder, named))) problems.Add("a senpai file exists: " + named);

                // Every line of the senpai halves she ships that the censored halves don't have, read from the build so
                // a later pack is swept too. Lines shipped to both halves are taken out first, so a shared greeting is
                // never called a leak.
                HashSet<string> plainLines = new HashSet<string>(StringComparer.Ordinal);
                List<string> senpaiLines = new List<string>();
                foreach (string pack in Personalities.Shipped())
                {
                    string plain, senpai;
                    Personalities.Split(pack, out plain, out senpai);
                    if (string.IsNullOrEmpty(plain)) continue;
                    foreach (string raw in plain.Replace("\r\n", "\n").Split('\n')) plainLines.Add(raw.Trim());
                }
                foreach (string pack in Personalities.Shipped())
                {
                    string plain, senpai;
                    Personalities.Split(pack, out plain, out senpai);
                    if (string.IsNullOrEmpty(senpai)) continue;
                    foreach (string raw in senpai.Replace("\r\n", "\n").Split('\n'))
                    {
                        string one = raw.Trim();
                        // Long enough to be hers rather than a word two files share, and not something she
                        // says on a censored install anyway.
                        if (one.Length >= 24 && !one.StartsWith("#", StringComparison.Ordinal) && !one.StartsWith(">", StringComparison.Ordinal)
                            && !plainLines.Contains(one)) senpaiLines.Add(one);
                    }
                }

                // The sweep needs its list: an empty one finds nothing and passes, so a floor well under today's count
                // catches nothing-at-all. With nothing attached there is nothing to look for, which is the build that
                // ships; said either way.
                bool attached = Personalities.SenpaiFor != null;
                if (attached && senpaiLines.Count < 100)
                    throw new Exception("The censored sweep had almost nothing to look for: " + senpaiLines.Count
                        + " senpai lines out of " + Personalities.Shipped().Count + " shipped packs, with a senpai half attached. A sweep with an empty fixture passes without checking anything, so it fails instead.");
                if (!attached && senpaiLines.Count > 0)
                    throw new Exception("Nothing attached a senpai half and the sweep still found " + senpaiLines.Count + " senpai lines, so the build is carrying some of it.");

                int read = 0;
                List<string> everything = new List<string>(System.IO.Directory.GetFiles(folder, "*.md", System.IO.SearchOption.AllDirectories));
                // And what the split left beside them: a backup of a file written before the split holds both
                // halves of it.
                everything.AddRange(System.IO.Directory.GetFiles(folder, "*.bak", System.IO.SearchOption.AllDirectories));
                foreach (string file in everything)
                {
                    string body;
                    try { body = System.IO.File.ReadAllText(file); }
                    catch (System.IO.IOException) { continue; }
                    read++;
                    string named = file.Substring(folder.Length).TrimStart('\\');
                    if (Legacy.HasTag(body)) problems.Add("a senpai heading is in " + named);
                    // Whole lines, not substrings: a senpai line that opens a longer censored one isn't a leak.
                    HashSet<string> inFile = new HashSet<string>(StringComparer.Ordinal);
                    foreach (string raw in body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')) inFile.Add(raw.Trim());
                    foreach (string line in senpaiLines)
                        if (inFile.Contains(line)) { problems.Add("a senpai line is in " + named + ": " + line); break; }
                }
                if (read == 0) problems.Add("the sweep read no files at all, so it proves nothing");
                if (problems.Count == 0) checks.Add("PASS the censored sweep: " + read + " of her files read, " + senpaiLines.Count + " senpai lines looked for" + (attached ? "" : " (nothing attached one, so there was none to look for)") + ", no senpai folder, file, heading or line anywhere in a censored install");
            }
            catch (Exception bad) { problems.Add("the sweep could not finish: " + bad.Message); }
            if (problems.Count > 0) throw new Exception("A censored install has the senpai side in it: " + string.Join("; ", problems.ToArray()));
        }

        // Where a hand lands, and the wall around the senpai half of it: that a censored install is written none
        // of it and can reach none of it, that the switch alone is not enough, and that her forwardness decides.
        private void SmokeTouchZones(List<string> checks)
        {
            // The senpai zones are named by what attached; a build with nothing attached has none to write.
            if (CapriDesktop.Touch.Upper == null)
            {
                checks.Add("LIMIT touch zones: nothing attached here names the senpai zones, so only her ordinary ones were there to check");
                return;
            }
            List<string> problems = new List<string>();
            string keptWas = CapriDesktop.Touch.Kept;
            bool senpaiWas = Brain.SenpaiOn, airWas = Brain.OnAir;
            int specialInterestWas = Personalities.SpecialInterestOverride;
            string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "capri-touch-" + Guid.NewGuid().ToString("N"));
            try
            {
                System.IO.Directory.CreateDirectory(folder);
                CapriDesktop.Touch.Kept = System.IO.Path.Combine(folder, CapriDesktop.Touch.FileName);

                // Censored: the senpai sections are not written at all, and asking for one gets nothing whatever
                // her forwardness says. A file that merely hid them would still be a file naming them.
                Brain.OnAir = false;
                Brain.SenpaiOn = false;
                CapriDesktop.Touch.Forget();
                CapriDesktop.Touch.Write(false);
                string had = System.IO.File.ReadAllText(CapriDesktop.Touch.Kept);
                if (Legacy.HasTag(had) || (CapriDesktop.Touch.Lower != null && had.IndexOf(CapriDesktop.Touch.Lower, StringComparison.OrdinalIgnoreCase) >= 0) || (CapriDesktop.Touch.Upper != null && had.IndexOf(CapriDesktop.Touch.Upper, StringComparison.OrdinalIgnoreCase) >= 0))
                    problems.Add("a censored install was written the senpai zones");
                if (CapriDesktop.Touch.Line(CapriDesktop.Touch.Lower, 10) != null || CapriDesktop.Touch.Line(CapriDesktop.Touch.Upper, 10) != null || CapriDesktop.Touch.Line(CapriDesktop.Touch.ClickZone, 10) != null)
                    problems.Add("a censored install answered for a senpai zone");
                if (CapriDesktop.Touch.Line(CapriDesktop.Touch.Head, 0) == null) problems.Add("her head said nothing on a censored install");

                // The switch on: the sections are written, and her forwardness decides which answer comes back.
                Brain.SenpaiOn = true;
                CapriDesktop.Touch.Forget();
                CapriDesktop.Touch.Write(false);
                had = System.IO.File.ReadAllText(CapriDesktop.Touch.Kept);
                if (!Legacy.HasTag(had)) problems.Add("the switch did not write the senpai zones");
                string willing = CapriDesktop.Touch.Line(CapriDesktop.Touch.Lower, CapriDesktop.Touch.Willing);
                string not = CapriDesktop.Touch.Line(CapriDesktop.Touch.Lower, CapriDesktop.Touch.Willing - 1);
                if (string.IsNullOrEmpty(willing) || string.IsNullOrEmpty(not)) problems.Add("a senpai zone said nothing with the switch on");
                else if (willing == not) problems.Add("her forwardness made no difference to a senpai zone");
                string refused = CapriDesktop.Touch.Line(CapriDesktop.Touch.Upper, 0);
                if (string.IsNullOrEmpty(refused)) problems.Add("an unwelcome touch said nothing at all");

                // On air her forwardness is zero by its own rule, so nothing senpai can be reached while she is
                // in front of an audience, switch or no switch.
                Brain.OnAir = true;
                Personalities.SpecialInterestOverride = 10;
                if (Personalities.SpecialInterest != 0) problems.Add("her forwardness was not zero on air: " + Personalities.SpecialInterest);
            }
            finally
            {
                CapriDesktop.Touch.Kept = keptWas;
                CapriDesktop.Touch.Forget();
                Brain.SenpaiOn = senpaiWas;
                Brain.OnAir = airWas;
                Personalities.SpecialInterestOverride = specialInterestWas;
                try { System.IO.Directory.Delete(folder, true); } catch (System.IO.IOException) { }
            }
            if (problems.Count > 0) throw new Exception("Where a hand lands went wrong: " + string.Join("; ", problems.ToArray()));
            checks.Add("PASS where a hand lands: her ordinary zones answer at any time, a censored install is written none of the senpai ones and answers for none of them, the switch writes them, her forwardness decides whether she welcomes one or moves your hand, and on air her forwardness is zero so none of it is reachable in front of an audience");
        }

        // Her stories: that a piece is told in order and finished before the next one starts, that a finished
        // one is left alone until she has been through them all, and that closing her mid-story keeps her place.
        private void SmokeLoreTelling(List<string> checks)
        {
            List<string> problems = new List<string>();
            string keptWas = Lore.Kept, markWas = Lore.Marked;
            string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "capri-lore-" + Guid.NewGuid().ToString("N"));
            try
            {
                System.IO.Directory.CreateDirectory(folder);
                Lore.Kept = System.IO.Path.Combine(folder, Lore.FileName);
                Lore.Marked = System.IO.Path.Combine(folder, Lore.MarkName);
                Lore.Forget();
                System.IO.File.WriteAllText(Lore.Kept,
                    "# What she tells you about herself\r\n\r\n## One\r\n\r\nfirst of one\r\nsecond of one\r\n\r\n## Two\r\n\r\nfirst of two\r\nsecond of two\r\n");

                // In order, and the piece is finished before the other one begins.
                string[] want = { "first of one", "second of one", "first of two", "second of two" };
                for (int each = 0; each < want.Length; each++)
                {
                    string told = Lore.Next();
                    if (told != want[each]) problems.Add("story line " + (each + 1) + " was " + (told ?? "nothing") + ", wanted " + want[each]);
                }

                // Been through them all, so they come round again rather than her running out for good.
                string again = Lore.Next();
                if (again != "first of one") problems.Add("after both stories she did not start again: " + (again ?? "nothing"));

                // Her place survives being closed mid-story: the bookmark is a file, not something she holds.
                Lore.Settle();
                Lore.Forget();
                Lore.Kept = System.IO.Path.Combine(folder, Lore.FileName);
                Lore.Marked = System.IO.Path.Combine(folder, Lore.MarkName);
                // Forget leaves nothing read, so the mark is read back on the next telling.
                typeof(Lore).GetField("read", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).SetValue(null, false);
                string resumed = Lore.Next();
                if (resumed != "second of one") problems.Add("she did not pick her story up where she left it: " + (resumed ?? "nothing"));

                // A file with nothing in it says nothing, rather than throwing or saying an empty line.
                System.IO.File.WriteAllText(Lore.Kept, "# What she tells you about herself\r\n");
                Lore.Forget();
                if (Lore.Next() != null) problems.Add("an empty lore file still told a story");

                // A story whose mood matches hers comes first; with none matching, the next untold one does.
                Func<string> feelingWas = Lore.Feeling;
                string packWas = Lore.PackPath;
                try
                {
                    Lore.PackPath = null;
                    System.IO.File.WriteAllText(Lore.Kept,
                        "# What she tells you about herself\r\n\r\n## One\r\n> mood: funny\r\nfirst of one\r\n\r\n## Two\r\n> mood: sad\r\nfirst of two\r\n");
                    Lore.Feeling = delegate { return "Melancholy"; };
                    Lore.Forget();
                    string matched = Lore.Next();
                    if (matched != "first of two") problems.Add("a sad story did not come first in a sad mood: " + (matched ?? "nothing"));
                    Lore.Feeling = delegate { return "Smug"; };
                    Lore.Forget();
                    string fallback = Lore.Next();
                    if (fallback != "first of one") problems.Add("with no proud story she did not fall back to the next one: " + (fallback ?? "nothing"));
                    if (Lore.Moods().Count != 2) problems.Add("the mood tags read as " + Lore.Moods().Count + " rather than 2");
                }
                finally { Lore.Feeling = feelingWas; Lore.PackPath = packWas; }

                // A piece the pack renamed takes the new heading rather than arriving as a second copy, and a copy
                // already appended that way is taken out. A piece whose lines differ is left alone.
                bool changed;
                string mine = "## The old name\r\n\r\nsame line one\r\nsame line two\r\n\r\n## Rewritten by them\r\n\r\ntheir own line\r\n";
                string renamedPack = "## The new name\r\n\r\nsame line one\r\nsame line two\r\n\r\n## Rewritten by them\r\n\r\nthe shipped line\r\n";
                string followed = Lore.Renamed(mine, renamedPack, out changed);
                if (!changed || followed.IndexOf("## The new name", StringComparison.Ordinal) < 0 || followed.IndexOf("The old name", StringComparison.Ordinal) >= 0 || followed.IndexOf("their own line", StringComparison.Ordinal) < 0)
                    problems.Add("a renamed piece was not followed: " + followed.Replace("\r\n", " | "));
                string doubled = "## The old name\r\n\r\nsame line one\r\nsame line two\r\n\r\n## The new name\r\n\r\nsame line one\r\nsame line two\r\n";
                string tidied = Lore.Renamed(doubled, renamedPack, out changed);
                if (!changed || tidied.IndexOf("The old name", StringComparison.Ordinal) >= 0 || tidied.IndexOf("## The new name", StringComparison.Ordinal) < 0)
                    problems.Add("the second copy of a renamed piece stayed: " + tidied.Replace("\r\n", " | "));

                // lore1.md beside lore.md is more of her stories; a file named any other way is not.
                Lore.Forget();
                System.IO.File.WriteAllText(Lore.Kept, "## One\r\n\r\nfirst of one\r\n");
                System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "lore1.md"), "## Extra\r\n\r\nfirst of extra\r\n");
                System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "lore copy.md"), "## Spare\r\n\r\nfirst of spare\r\n");
                System.IO.File.WriteAllText(Lore.Marked, "");
                string heard = (Lore.Next() ?? "") + " / " + (Lore.Next() ?? "") + " / " + (Lore.Next() ?? "");
                if (heard != "first of one / first of extra / first of one") problems.Add("the numbered lore file was read wrong: " + heard);
            }
            finally
            {
                Lore.Kept = keptWas;
                Lore.Marked = markWas;
                Lore.Forget();
                try { System.IO.Directory.Delete(folder, true); } catch (System.IO.IOException) { }
            }
            if (problems.Count > 0) throw new Exception("Her stories went wrong: " + string.Join("; ", problems.ToArray()));
            checks.Add("PASS her stories are told a piece at a time: the lines of one come in the order they are written, the piece is finished before another starts, the set comes round again once she has been through it, her place survives being closed mid-story, an empty file says nothing, and a story matching her mood comes first with the next one as the fallback; a piece the pack renamed is followed rather than doubled, and lore1.md is read as more of her stories while other names are not");
        }

        // The lines she says on her own: that they come out of her file, that her file can be edited and emptied
        // without leaving her mute, and that topping it up never touches a line of theirs.
        private void SmokeOwnMoments(List<string> checks)
        {
            List<string> problems = new List<string>();
            string keptWas = Moments.Kept;
            string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "capri-moments-" + Guid.NewGuid().ToString("N"));
            string[] stock = { "stock one", "stock two" };
            try
            {
                System.IO.Directory.CreateDirectory(folder);
                Moments.Kept = System.IO.Path.Combine(folder, Moments.FileName);
                Moments.Write(false);
                if (!System.IO.File.Exists(Moments.Kept)) problems.Add("her moments file was not written");

                // Every heading the code asks for is a heading the file ships, or that pool silently falls back
                // to the source for ever and the file looks like it does nothing.
                string had = System.IO.File.ReadAllText(Moments.Kept);
                foreach (string heading in Moments.Headings)
                    if (had.IndexOf("## " + heading, StringComparison.Ordinal) < 0) problems.Add("her moments file has no section for " + heading);

                // A line out of the file, not the source.
                string said = Moments.Pick(Moments.PopIn, stock);
                if (said == null || Array.IndexOf(stock, said) >= 0) problems.Add("popping in did not come from her file: " + (said ?? "nothing"));

                // Their own line is the one she says when it is the only one there, and between versions the file is left as
                // they wrote it: a section they took out stays out until a new version writes the file new.
                System.IO.File.WriteAllText(Moments.Kept, "# Capri's own moments\r\n\r\n## " + Moments.Woken + "\r\n\r\nOi.\r\n");
                said = Moments.Pick(Moments.Woken, stock);
                if (said != "Oi.") problems.Add("a file with one line of theirs said something else: " + (said ?? "nothing"));
                Moments.Write(false);
                had = System.IO.File.ReadAllText(Moments.Kept);
                if (had.IndexOf("Oi.", StringComparison.Ordinal) < 0) problems.Add("writing her moments file lost their line");
                if (had.IndexOf("## " + Moments.PopIn, StringComparison.Ordinal) >= 0) problems.Add("a section they took out was put back between versions");

                // A section emptied on purpose falls back to the lines in the build rather than leaving her mute.
                System.IO.File.WriteAllText(Moments.Kept, "# Capri's own moments\r\n\r\n## " + Moments.PopIn + "\r\n\r\n");
                said = Moments.Pick(Moments.PopIn, stock);
                if (said == null || Array.IndexOf(stock, said) < 0) problems.Add("an emptied section did not fall back to the lines in the build: " + (said ?? "nothing"));

                // And with no file at all she still speaks.
                System.IO.File.Delete(Moments.Kept);
                said = Moments.Pick(Moments.PopIn, stock);
                if (said == null || Array.IndexOf(stock, said) < 0) problems.Add("with no file she had nothing to say: " + (said ?? "nothing"));
            }
            finally
            {
                Moments.Kept = keptWas;
                try { System.IO.Directory.Delete(folder, true); } catch (System.IO.IOException) { }
            }
            if (problems.Count > 0) throw new Exception("Her own moments went wrong: " + string.Join("; ", problems.ToArray()));
            checks.Add("PASS the lines she says on her own come out of moments.md: every pool the code asks for is a section in it, a line of theirs is what she says, their file is left as they wrote it between versions, and an emptied section or a deleted file falls back to the lines in the build rather than leaving her mute");
        }

        // Her spells away from the PC: short ones aren't sleep, long ones get a question, their answer decides, and the
        // bedtime she expects follows the nights she has seen. Driven with made-up spells, since the real thing takes a
        // week.
        private void SmokeAwaySpells(List<string> checks)
        {
            List<string> problems = new List<string>();
            string keptWas = Away.Kept, notesWas = Brain.MemoryPath;
            Func<long> idleWas = Away.Idle;
            string toldWas = Hours.Told;
            bool setWas = preferences.CareLateSet;
            int hourWas = preferences.CareLateHour;
            string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "capri-away-" + Guid.NewGuid().ToString("N"));
            try
            {
                System.IO.Directory.CreateDirectory(folder);
                Away.Kept = System.IO.Path.Combine(folder, Away.FileName);
                Away.Forget();
                // A known set of hours for the run, so what counts as a night is the same on every machine. Left to
                // what she has counted, the box this runs on would decide the answers.
                Hours.Told = "9am-11pm";

                // A short spell is a meal or a film, not a night, so there is nothing to ask about.
                DateTime night = new DateTime(2026, 11, 10, 23, 30, 0);
                Away.Add(night, night.AddHours(2), "");
                string question;
                if (Away.Ask(night.AddHours(2), out question)) problems.Add("a two hour gap was asked about: " + question);

                // Eight hours overnight is a night or a shift, and only they can say which. Asked once.
                Away.Forget();
                Away.Add(night, night.AddHours(8), "");
                if (!Away.Ask(night.AddHours(8).AddMinutes(10), out question) || question == null || question.IndexOf("working", StringComparison.OrdinalIgnoreCase) < 0)
                    problems.Add("a long overnight gap did not ask whether they were working: " + (question ?? "nothing"));
                string again;
                if (Away.Ask(night.AddHours(8).AddMinutes(20), out again)) problems.Add("the same gap was asked about twice: " + again);

                // Their answer settles it, and her line to her brain says work rather than sleep.
                Away.Called("worked");
                string line = Away.Line(night.AddHours(8).AddMinutes(30));
                if (line == null || line.IndexOf("working", StringComparison.OrdinalIgnoreCase) < 0) problems.Add("after they said work, her line did not say so: " + (line ?? "nothing"));

                // Twice the same shift placed as work, and the third is placed by her routine without a question.
                Away.Forget();
                Away.Add(night.AddDays(-2), night.AddDays(-2).AddHours(8), "worked");
                Away.Add(night.AddDays(-1), night.AddDays(-1).AddHours(8), "worked");
                Away.Add(night, night.AddHours(8), "");
                string routine;
                if (Away.Ask(night.AddHours(8).AddMinutes(10), out routine)) problems.Add("a shift she had seen twice was still asked about: " + routine);
                if (Away.Back(night.AddHours(8).AddMinutes(15)) != "worked") problems.Add("a shift she had seen twice was not placed as work");

                // A night they have not called anything reads as sleep, and a short one says so.
                Away.Forget();
                DateTime early = new DateTime(2026, 11, 11, 1, 0, 0);
                Away.Add(early, early.AddHours(4), "");
                line = Away.Line(early.AddHours(4).AddMinutes(5));
                if (line == null || line.IndexOf("slept", StringComparison.OrdinalIgnoreCase) < 0 || line.IndexOf("short", StringComparison.OrdinalIgnoreCase) < 0)
                    problems.Add("four hours overnight did not read as a short sleep: " + (line ?? "nothing"));
                // And a spell from days ago is not news.
                if (Away.Line(early.AddDays(2)) != null) problems.Add("a gap from days ago was still being reported to her brain");

                // Three nights beginning at one in the morning make that her idea of their bedtime, and it moves
                // her sleep nudge off the hour in her settings.
                Away.Forget();
                for (int each = 0; each < 3; each++)
                {
                    DateTime from = new DateTime(2026, 11, 10 + each, 1, 0, 0);
                    Away.Add(from, from.AddHours(7), "");
                }
                if (Away.SleepsAt() != 1) problems.Add("three nights starting at 1am gave a bedtime of " + Away.SleepsAt());
                preferences.CareLateSet = false;
                preferences.CareLateHour = 23;
                if (CareTimeNow(CareKind.Late) != 1) problems.Add("her sleep nudge ignored the nights she has seen: " + CareTimeNow(CareKind.Late));
                // Once it has been set by hand it stays where it was put.
                preferences.CareLateSet = true;
                if (CareTimeNow(CareKind.Late) != 23) problems.Add("a bedtime set by hand was overruled: " + CareTimeNow(CareKind.Late));

                // Two nights is not enough to call anything usual.
                Away.Forget();
                DateTime one = new DateTime(2026, 11, 12, 2, 0, 0);
                Away.Add(one, one.AddHours(7), "");
                Away.Add(one.AddDays(1), one.AddDays(1).AddHours(7), "");
                if (Away.SleepsAt() != -1) problems.Add("two nights were treated as a routine: " + Away.SleepsAt());

                // Their own answer as people really write it: "about midday, and then it ends at probably like 3 to 5
                // a.m" means a day from midday to the small hours, not the one-hour range inside it.
                int said, until;
                Hours.Told = "Uh, probably about midday, and then it ends at probably like 3 to 5 a.m";
                if (!Hours.Usual(out said, out until) || said != 12 || until != 4) problems.Add("a day written as midday until 5am read as " + said + " to " + until);
                Hours.Told = "9am-1am";
                if (!Hours.Usual(out said, out until) || said != 9 || until != 0) problems.Add("9am-1am read as " + said + " to " + until);
                Hours.Told = "midnight to midday";
                if (!Hours.Usual(out said, out until) || said != 0 || until != 11) problems.Add("midnight until midday read as " + said + " to " + until);
                // Always on is no answer, so her counting takes over. What that says depends on the machine; what
                // matters is that the words were not read as a range of their own.
                Hours.Told = "I'm on it 24/7";
                if (Hours.Usual(out said, out until) && said == 0 && until == 6) problems.Add("always-on was read as midnight to 7am");

                // A me.md from an older build gains the keys she ships now, and nothing of theirs moves.
                Brain.MemoryPath = System.IO.Path.Combine(folder, "me.md");
                System.IO.File.WriteAllText(Brain.MemoryPath,
                    "# What Capri remembers about you\r\n>! She reads this every time you chat.\r\n\r\n- thingsUserLikes=\r\n- awakeHours=9am-1am\r\n- They have a cat called Biscuit\r\n");
                Brain.TopUpNotes();
                string notes = System.IO.File.ReadAllText(Brain.MemoryPath);
                if (!Probes.Filled(notes, "awakeHours") || notes.IndexOf("Biscuit", StringComparison.Ordinal) < 0)
                    problems.Add("topping up me.md lost something of theirs: [" + notes.Replace("\r\n", " ") + "]");
                if (notes.IndexOf("- workHours=", StringComparison.Ordinal) < 0 || notes.IndexOf("workHours is when you are working", StringComparison.Ordinal) < 0)
                    problems.Add("me.md did not gain workHours and its note: [" + notes.Replace("\r\n", " ") + "]");
                if (notes.IndexOf("- petNames=", StringComparison.Ordinal) < 0)
                    problems.Add("me.md did not gain petNames, which has been shipped since 0.9.0: [" + notes.Replace("\r\n", " ") + "]");
                // Run twice, nothing doubles.
                Brain.TopUpNotes();
                string twice = System.IO.File.ReadAllText(Brain.MemoryPath);
                int keys = 0, at = 0;
                while ((at = twice.IndexOf("- workHours=", at, StringComparison.Ordinal)) >= 0) { keys++; at++; }
                if (keys != 1) problems.Add("topping up me.md twice wrote workHours " + keys + " times");

                // The angles her glance steers by are a file of hers too, and the same machinery carries it.
                string anglesWas = Angles.Kept;
                try
                {
                    Angles.Kept = System.IO.Path.Combine(folder, Angles.FileName);
                    Angles.Forget();
                    Angles.Write(false);
                    string angle = Angles.Angle();
                    if (string.IsNullOrEmpty(angle)) problems.Add("her watching file gave no angle to take");
                    string anglesHad = System.IO.File.ReadAllText(Angles.Kept);
                    int many = 0;
                    foreach (string row in anglesHad.Replace("\r\n", "\n").Split('\n'))
                        if (row.Trim().Length > 0 && !row.TrimStart().StartsWith("#", StringComparison.Ordinal) && !row.TrimStart().StartsWith(">", StringComparison.Ordinal)) many++;
                    if (many < 40) problems.Add("her watching file shipped only " + many + " angles");
                    // Nothing gated reaches a censored install, here as anywhere else.
                    if (!Brain.SenpaiLive && Legacy.HasTag(anglesHad))
                        problems.Add("a censored install was written a senpai section of her watching file");
                }
                finally
                {
                    Angles.Kept = anglesWas;
                    Angles.Forget();
                }

                // Only keys she asks about may be written, so a brain inventing one cannot put junk in their notes.
                if (!Probes.Allowed("workHours") || Probes.Allowed("sleepHours")) problems.Add("the keys her brain may write are wrong");
            }
            finally
            {
                Away.Kept = keptWas;
                Away.Idle = idleWas;
                Away.Forget();
                Brain.MemoryPath = notesWas;
                Hours.Told = toldWas;
                preferences.CareLateSet = setWas;
                preferences.CareLateHour = hourWas;
                try { System.IO.Directory.Delete(folder, true); } catch (System.IO.IOException) { }
            }
            if (problems.Count > 0) throw new Exception("Her idea of sleep and work went wrong: " + string.Join("; ", problems.ToArray()));
            checks.Add("PASS her spells away from the PC: a short one is not sleep, a long one is asked about once and their answer decides what it was, a short night reads as a short night, a gap from days ago is not news, three nights set the hour she expects them in bed and a bedtime set by hand is not overruled, hours they wrote in their own words are read from the first time to the last rather than the first range in the line, and an older me.md gains the keys she ships without losing a line of theirs or doubling on a second run");
        }
    }
}

namespace CapriDesktop
{
    internal sealed partial class Companion
    {
        // A plain look-up: the browser's own search started with the words (Chromium "? words", Firefox -search), else Google;
        // no engine is picked and nothing of the browser's settings is read on the way.
        private void SmokeBrowserSearch(List<string> checks)
        {
            Func<string, string> pathWas = Hands.BrowserPath, openedWas = Hands.Opened;
            Action<string, string> browseWas = Hands.Browse, searchWas = Hands.BrowseSearch;
            Action<string> runWas = Hands.Run;
            List<string> searched = new List<string>(), browsed = new List<string>(), ran = new List<string>();
            try
            {
                Hands.BrowserPath = delegate(string named) { return named == "Firefox" ? @"C:\fx\firefox.exe" : @"C:\b\brave.exe"; };
                Hands.BrowseSearch = delegate(string path, string words) { searched.Add(path + "|" + words); };
                Hands.Browse = delegate(string path, string address) { browsed.Add(path + " " + address); };
                Hands.Run = delegate(string address) { ran.Add(address); };
                Hands.Opened = delegate { return ""; };
                string said = Hands.Do("open_website", new ToolArguments { Site = "google", Search = "hollow knight", Browser = "brave", AnyEngine = true });
                bool own = searched.Count == 1 && searched[0] == @"C:\b\brave.exe|hollow knight" && said == "Searched for hollow knight with Brave's own search." && browsed.Count == 0;
                string chromium = Hands.SearchLaunch(@"C:\b\brave.exe", " cat \"videos\" ").Arguments, fox = Hands.SearchLaunch(@"C:\fx\firefox.exe", "cats").Arguments;
                bool shaped = chromium == "\"? cat videos\"" && fox == "-search \"cats\"";
                Hands.BrowserPath = delegate { return null; };
                Hands.Do("open_website", new ToolArguments { Site = "google", Search = "hollow knight", Browser = "brave", AnyEngine = true });
                bool google = ran.Count == 1 && ran[0] == "https://www.google.com/search?q=hollow%20knight";
                if (!own || !shaped || !google)
                    throw new Exception("A plain look-up went wrong: the browser's own search " + own + " (" + said + "), its arguments " + shaped + " (" + chromium + " / " + fox + "), Google with no browser " + google);
                checks.Add("PASS a plain look-up goes to the browser's own search started with the words (Chromium \"? words\", Firefox -search), else Google, picking no engine and reading none of the browser's files");
            }
            finally
            {
                Hands.BrowserPath = pathWas; Hands.Opened = openedWas;
                Hands.Browse = browseWas; Hands.BrowseSearch = searchWas; Hands.Run = runWas;
            }
        }
    }
}
#endif
