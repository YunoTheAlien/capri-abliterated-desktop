#if CHECKS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Companion
    {
        // The Planner's window: free-standing (no owner, in the taskbar), its keys, the editor served through it, Export's
        // zip, the line and Open in browser where WebView2 is missing; a picture of it.
        private void SmokePlannerWindow(List<string> checks, string output)
        {
            string problem;
            PlannerHost host = PlannerServer(out problem);
            if (host == null) { checks.Add("LIMIT planner window: " + problem + " A build with the owner's StoryBoard beside it checks the rest."); return; }
            List<string> wrong = new List<string>();
            try
            {
                HttpWebRequest asked = (HttpWebRequest)WebRequest.Create(host.Address);
                asked.Timeout = 5000;
                using (HttpWebResponse answer = (HttpWebResponse)asked.GetResponse())
                using (StreamReader body = new StreamReader(answer.GetResponseStream()))
                    if (answer.StatusCode != HttpStatusCode.OK || body.ReadToEnd().IndexOf("<html", StringComparison.OrdinalIgnoreCase) < 0) wrong.Add("the editor wasn't served at its address");
                mainframe.OpenPlanner();
                Application.DoEvents();
                string state = mainframe.PlannerState;
                if (!state.StartsWith("free taskbar | Export | Boards folder | Open in browser", StringComparison.Ordinal)) wrong.Add("the window: " + state);
                bool web = PlannerWeb.Ready();
                string stuck = null;
                if (web)
                {
                    Stopwatch waited = Stopwatch.StartNew();
                    while (!mainframe.PlannerLoaded && waited.ElapsedMilliseconds < 20000) { Application.DoEvents(); Thread.Sleep(25); }
                    // WebView2 there but not starting on this PC (a guest without its GPU, a blocked browser process) is a
                    // limit of the PC: the window says so, and the stage it stopped at is named.
                    if (!mainframe.PlannerLoaded) stuck = PlannerWeb.Stage;
                    else
                    {
                        Thread.Sleep(1500);
                        Application.DoEvents();
                        mainframe.PicturePlanner(Path.Combine(output, "planner-window.png"));
                    }
                }
                string zipped = Path.Combine(output, "planner-export.zip");
                ExportPlanner(PlannerFolder, zipped);
                using (ZipArchive zip = ZipFile.OpenRead(zipped))
                    if (zip.GetEntry("project.json") == null) wrong.Add("Export's zip has no project.json");
                mainframe.ClosePlanner();
                Mainframe.PlannerNoRuntime(true);
                try
                {
                    mainframe.OpenPlanner();
                    Application.DoEvents();
                    if (mainframe.PlannerLine == null || !mainframe.PlannerState.Contains("Open in browser on")) wrong.Add("with no WebView2: " + mainframe.PlannerState + " / " + mainframe.PlannerLine);
                    mainframe.PicturePlanner(Path.Combine(output, "planner-no-runtime.png"));
                    mainframe.ClosePlanner();
                }
                finally { Mainframe.PlannerNoRuntime(false); }
                if (wrong.Count > 0) throw new Exception("The Planner's window went wrong: " + string.Join("; ", wrong.ToArray()));
                checks.Add("PASS the Planner's window: free-standing in the taskbar, Export, Boards folder and Open in browser in its bar, the editor served at its address" + (web && stuck == null ? " and shown through WebView2" : "") + ", Export zips the project, and with no WebView2 its line and Open in browser (planner-window.png, planner-no-runtime.png)");
                if (!web) checks.Add("LIMIT the Planner through WebView2: this build or PC has no WebView2, so only the browser way was checked");
                if (stuck != null) checks.Add("LIMIT the Planner through WebView2: the runtime is here but in 20 s it got only to " + stuck + "; after " + Mainframe.PlannerStartMost / 1000 + " s the window says WebView2 not found, and the browser way was checked");
            }
            finally { mainframe.ClosePlanner(); }
        }

        // How fast the editor opens in her window and in an Edge app window, three runs each, and her window's frame time;
        // written to planner-speed.txt for the report, nothing shipped.
        private void SmokePlannerSpeed(List<string> checks, string output)
        {
            string problem;
            PlannerHost host = PlannerServer(out problem);
            if (host == null || !PlannerWeb.Ready()) { checks.Add("LIMIT planner speed: " + (problem ?? "no WebView2 here") + "."); return; }
            List<string> lines = new List<string>();
            long loaded = -1;
            Stopwatch clock = new Stopwatch();
            host.Asked = delegate(string what) { if (what.StartsWith("GET /api/project", StringComparison.Ordinal) && Interlocked.Read(ref loaded) < 0) Interlocked.Exchange(ref loaded, clock.ElapsedMilliseconds); };
            try
            {
                for (int run = 1; run <= 3; run++)
                {
                    mainframe.ClosePlanner();
                    Interlocked.Exchange(ref loaded, -1);
                    clock.Restart();
                    mainframe.OpenPlanner();
                    while (!mainframe.PlannerLoaded && clock.ElapsedMilliseconds < 30000) { Application.DoEvents(); Thread.Sleep(5); }
                    long shown = clock.ElapsedMilliseconds;
                    if (!mainframe.PlannerLoaded) { checks.Add("LIMIT planner speed: WebView2 got only to " + PlannerWeb.Stage + " in 30 s, so nothing was measured."); return; }
                    while (Interlocked.Read(ref loaded) < 0 && clock.ElapsedMilliseconds < 30000) { Application.DoEvents(); Thread.Sleep(5); }
                    string frames = mainframe.PlannerFrameTime(2000);
                    lines.Add("WebView2 run " + run + ": page shown " + shown + " ms, project asked " + Interlocked.Read(ref loaded) + " ms, frames " + (frames ?? "not read"));
                }
                mainframe.ClosePlanner();
                string edge = null;
                foreach (string at in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) })
                {
                    string exe = Path.Combine(at, @"Microsoft\Edge\Application\msedge.exe");
                    if (File.Exists(exe)) edge = exe;
                }
                if (edge == null) lines.Add("Edge: not on this PC");
                else
                    for (int run = 1; run <= 3; run++)
                    {
                        string profile = Path.Combine(output, "edge-profile-" + run);
                        Interlocked.Exchange(ref loaded, -1);
                        clock.Restart();
                        Process started = Process.Start(new ProcessStartInfo { FileName = edge, Arguments = "--app=\"" + host.Address + "\" --user-data-dir=\"" + profile + "\" --no-first-run --no-default-browser-check", UseShellExecute = false });
                        while (Interlocked.Read(ref loaded) < 0 && clock.ElapsedMilliseconds < 30000) { Application.DoEvents(); Thread.Sleep(5); }
                        lines.Add("Edge app window run " + run + ": project asked " + Interlocked.Read(ref loaded) + " ms (its frame time can't be read from outside it)");
                        Thread.Sleep(1000);
                        try { Process.Start(new ProcessStartInfo { FileName = "taskkill", Arguments = "/T /F /PID " + started.Id, UseShellExecute = false, CreateNoWindow = true }).WaitForExit(10000); } catch (Exception caughtOne) { Brain.Caught(caughtOne); }
                    }
            }
            finally { host.Asked = null; mainframe.ClosePlanner(); }
            File.WriteAllLines(Path.Combine(output, "planner-speed.txt"), lines.ToArray());
            checks.Add("PASS the Planner's speed, measured (planner-speed.txt): " + string.Join("; ", lines.ToArray()));
        }
    }

    internal sealed partial class Mainframe
    {
        internal string PlannerState
        {
            get
            {
                if (plannerSheet == null || plannerSheet.IsDisposed || !plannerSheet.Visible) return "closed";
                return (plannerSheet.Owner == null ? "free" : "owned") + " " + (plannerSheet.ShowInTaskbar ? "taskbar" : "hidden") + " | " + plannerSheet.KeysShown + " " + (plannerSheet.BrowserOn ? "on" : "off");
            }
        }

        internal string PlannerLine { get { return plannerSheet == null || plannerSheet.IsDisposed ? null : plannerSheet.LineShown; } }

        internal bool PlannerLoaded { get { return plannerSheet != null && !plannerSheet.IsDisposed && plannerSheet.Shown; } }

        internal static void PlannerNoRuntime(bool on) { PlannerSheet.NoRuntime = on; }

        internal static int PlannerStartMost { get { return PlannerSheet.StartMost; } }

        // Her window drawn by itself, and the editor's own picture from WebView2 laid in its place: never the screen, so
        // nothing else on it can end up in the picture.
        internal void PicturePlanner(string path)
        {
            if (plannerSheet == null || plannerSheet.IsDisposed) return;
            using (Bitmap shot = new Bitmap(plannerSheet.Width, plannerSheet.Height, PixelFormat.Format24bppRgb))
            {
                plannerSheet.DrawToBitmap(shot, new Rectangle(Point.Empty, shot.Size));
#if WEBVIEW2
                if (plannerSheet.View != null)
                    using (MemoryStream page = new MemoryStream())
                    {
                        System.Threading.Tasks.Task taken = PlannerWeb.Preview(plannerSheet.View, page);
                        Stopwatch waited = Stopwatch.StartNew();
                        while (taken != null && !taken.IsCompleted && waited.ElapsedMilliseconds < 5000) { Application.DoEvents(); Thread.Sleep(10); }
                        if (taken != null && taken.IsCompleted && taken.Exception == null && page.Length > 0)
                        {
                            page.Position = 0;
                            using (Image editor = Image.FromStream(page))
                            using (Graphics graphics = Graphics.FromImage(shot))
                            {
                                Point at = plannerSheet.PointToClient(plannerSheet.View.PointToScreen(Point.Empty));
                                graphics.DrawImage(editor, new Rectangle(at, plannerSheet.View.Size));
                            }
                        }
                    }
#endif
                using (Bitmap cut = Outlined(shot, plannerSheet)) cut.Save(path, ImageFormat.Png);
            }
        }

        // For the gallery: opened fresh at this scale, pictured from itself, closed; the place it had is put back.
        internal string GalleryPlanner(string path)
        {
            string kept = capri.PlannerPlace;
            try
            {
                ClosePlanner();
                capri.PlannerPlace = null;
                OpenPlanner();
                bool web = plannerSheet.View != null;
                Stopwatch waited = Stopwatch.StartNew();
                while (web && !PlannerLoaded && waited.ElapsedMilliseconds < 20000) { Application.DoEvents(); Thread.Sleep(25); }
                if (web && !PlannerLoaded) return "not taken, the editor didn't load in 20 s";
                for (int i = 0; i < 60; i++) { Application.DoEvents(); Thread.Sleep(25); }
                PicturePlanner(path);
                return web ? "saved" : "saved, " + (PlannerLine ?? "no editor");
            }
            finally
            {
                ClosePlanner();
                capri.PlannerPlace = kept;
            }
        }

        internal string PlannerFrameTime(int ms)
        {
#if WEBVIEW2
            if (plannerSheet == null || plannerSheet.View == null) return null;
            System.Threading.Tasks.Task<string> measured = PlannerWeb.FrameTime(plannerSheet.View, ms);
            Stopwatch waited = Stopwatch.StartNew();
            while (!measured.IsCompleted && waited.ElapsedMilliseconds < ms + 5000) { Application.DoEvents(); Thread.Sleep(10); }
            return measured.IsCompleted && measured.Exception == null ? (measured.Result ?? "").Trim('"') : null;
#else
            return null;
#endif
        }
    }
}
#endif
