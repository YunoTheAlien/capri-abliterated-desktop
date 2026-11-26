using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace CapriDesktop
{
    // The Planner: StoryBoard's editor, served by her own PlannerHost on this PC, shown in a window of its own through
    // WebView2. Never glued to the Mainframe; in the taskbar like any app; Open in browser shows the same editor there.
    internal sealed partial class Companion
    {
        private PlannerHost planner;
        private string plannerProblem;

        internal string PlannerFolder { get { return Path.Combine(Logs.DataOf(Brain.LogPath), PlannerHost.FolderName); } }

        // The editor's files laid from this build (again when its copy changes), the starter project only where there is
        // none, and the server started once.
        internal PlannerHost PlannerServer(out string problem)
        {
            problem = null;
            if (planner != null) return planner;
            try
            {
                string app = Path.Combine(PlannerFolder, "app");
                if (!LayPlanner(PlannerFolder)) { problem = plannerProblem = "Planner not in this build"; return null; }
                planner = new PlannerHost(app, PlannerFolder);
                planner.Start();
                return planner;
            }
            catch (Exception error) { planner = null; problem = plannerProblem = "The planner couldn't start: " + error.Message; Brain.Caught(error); return null; }
        }

        private static bool LayPlanner(string folder)
        {
            using (Stream packed = typeof(Companion).Assembly.GetManifestResourceStream("Capri.Planner.zip"))
            {
                string app = Path.Combine(folder, "app"), stamp = Path.Combine(app, ".capri-pack");
                if (packed == null) return File.Exists(Path.Combine(app, "index.html"));
                string mark = packed.Length + " " + typeof(Companion).Assembly.GetName().Version;
                bool fresh = !File.Exists(stamp) || File.ReadAllText(stamp) != mark;
                bool starter = !File.Exists(Path.Combine(folder, "project", "project.json"));
                if (!fresh && !starter) return true;
                using (ZipArchive zip = new ZipArchive(packed, ZipArchiveMode.Read))
                    foreach (ZipArchiveEntry entry in zip.Entries)
                    {
                        if (entry.Name.Length == 0) continue;
                        bool isApp = entry.FullName.StartsWith("app/", StringComparison.Ordinal);
                        if (isApp ? !fresh : !starter) continue;
                        string to = Path.GetFullPath(Path.Combine(folder, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                        if (!to.StartsWith(Path.GetFullPath(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(to));
                        entry.ExtractToFile(to, true);
                    }
                Directory.CreateDirectory(app);
                File.WriteAllText(stamp, mark);
                return File.Exists(Path.Combine(app, "index.html"));
            }
        }

        // The project (its project.json and assets, not its backups or bin) as one zip.
        internal static void ExportPlanner(string folder, string to)
        {
            string project = Path.Combine(folder, "project");
            if (File.Exists(to)) File.Delete(to);
            using (ZipArchive zip = ZipFile.Open(to, ZipArchiveMode.Create))
                foreach (string file in Directory.GetFiles(project, "*", SearchOption.AllDirectories))
                {
                    string inside = file.Substring(project.Length + 1).Replace(Path.DirectorySeparatorChar, '/');
                    if (inside.StartsWith("backups/", StringComparison.Ordinal) || inside.StartsWith("assets/_bin/", StringComparison.Ordinal)) continue;
                    zip.CreateEntryFromFile(file, inside, CompressionLevel.Optimal);
                }
        }

        internal string PlannerPlace { get { return preferences.PlannerBounds; } set { preferences.PlannerBounds = value; SavePreferences(); } }
    }

    internal sealed partial class Mainframe
    {
        private PlannerSheet plannerSheet;

        internal void OpenPlanner()
        {
            if (plannerSheet != null && !plannerSheet.IsDisposed)
            {
                if (plannerSheet.WindowState == FormWindowState.Minimized) plannerSheet.WindowState = FormWindowState.Normal;
                plannerSheet.Show();
                plannerSheet.Activate();
                return;
            }
            string problem;
            PlannerHost host = capri.PlannerServer(out problem);
            plannerSheet = new PlannerSheet(this, SheetZoom(PlannerSheet.Wide, PlannerSheet.Tall), host, problem);
            plannerSheet.Show();
            plannerSheet.Activate();
        }

        internal void ClosePlanner() { if (plannerSheet != null && !plannerSheet.IsDisposed) plannerSheet.Close(); }

        private sealed class PlannerSheet : ModSheet
        {
            internal const int Wide = 1280, Tall = 780, Bar = 50;
            private readonly PlannerHost host;
            private readonly Key export, boards, browser, tuck, grow, shut;
            private readonly Label line;
            private readonly System.Windows.Forms.Panel well;
            internal Control View;
            internal volatile bool Shown;
            // For the checks: as if this PC had no WebView2 runtime.
            internal static bool NoRuntime;

            internal PlannerSheet(Mainframe owner, float zoom, PlannerHost served, string problem) : base(owner, zoom, "Game / project planner", "GAME / PROJECT PLANNER", Wide, Tall)
            {
                host = served;
                ShowInTaskbar = true;
                MinimumSize = new Size(Z(720), Z(420));
                export = Make("Export", 380, 18, 70, 24, delegate { Export(); });
                export.AccessibleName = "Export: the project as one zip";
                boards = Make("Boards folder", 458, 18, 104, 24, delegate { Frame.capri.OpenPlannerFolder(); });
                boards.AccessibleName = "Boards folder: her Planner folder in Explorer";
                browser = Make("Open in browser", 570, 18, 118, 24, delegate { if (host != null) Process.Start(new ProcessStartInfo { FileName = host.Address, UseShellExecute = true }); });
                browser.AccessibleName = "Open in browser";
                browser.AccessibleDescription = "Opens the planner in your browser";
                browser.Enabled = host != null;
                foreach (Control one in Controls) { Key key = one as Key; if (key != null && key.Glyph == 2) shut = key; }
                tuck = new Key { Glyph = 1, AccessibleName = "Minimize" };
                tuck.Click += delegate { WindowState = FormWindowState.Minimized; };
                grow = new Key { Glyph = 9, AccessibleName = "Maximize" };
                grow.Click += delegate { WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; };
                Controls.Add(tuck);
                Controls.Add(grow);
                well = new System.Windows.Forms.Panel { BackColor = Ground };
                Controls.Add(well);
                line = new Note { Font = Font, ForeColor = Rose, BackColor = Ground, AutoSize = false, UseMnemonic = false, TextAlign = ContentAlignment.MiddleCenter, Visible = false };
                well.Controls.Add(line);
                if (host == null) Say(problem);
                else if (NoRuntime || !PlannerWeb.Ready()) Say("WebView2 not found");
                else
                {
                    View = PlannerWeb.Show(well, Frame.capri.PlannerFolder, host.Address, delegate { Shown = true; line.Visible = false; }, Unstarted);
                    // A runtime that is there but never starts leaves the window blank otherwise.
                    starting = new Clock.Ticker("planner start") { Interval = StartMost };
                    starting.Tick += delegate { starting.Stop(); if (!Shown) Unstarted("nothing shown in " + StartMost / 1000 + " s, at " + PlannerWeb.Stage); };
                    starting.Start();
                }
                Place();
                ResumeLayout(false);
            }

            internal string LineShown { get { return line.Visible ? line.Text : null; } }

            internal const int StartMost = 30000;
            private Clock.Ticker starting;

            private void Unstarted(string why)
            {
                if (Shown || IsDisposed) return;
                Brain.Log("planner", -1, "WebView2 didn't show the editor: " + why + ".");
                Say("WebView2 not found");
            }

            internal string KeysShown { get { return export.Text + " | " + boards.Text + " | " + browser.Text; } }

            internal bool BrowserOn { get { return browser.Enabled; } }

            private void Say(string words)
            {
                line.Text = words;
                line.Visible = true;
            }

            private void Export()
            {
                using (SaveFileDialog where = new SaveFileDialog { Filter = "Zip (*.zip)|*.zip", FileName = "planner-project-" + DateTime.Now.ToString("yyyy-MM-dd") + ".zip" })
                {
                    if (where.ShowDialog(this) != DialogResult.OK) return;
                    try { Companion.ExportPlanner(Frame.capri.PlannerFolder, where.FileName); }
                    catch (Exception error) { Notice.Tell(this, "The project couldn't be saved: " + error.Message, "Planner", false); }
                }
            }

            // The keys keep the bar's right end as it grows; the editor fills the rest, inside her frame.
            private void Place()
            {
                if (shut == null || well == null) return;
                int right = ClientSize.Width;
                shut.Left = right - Z(54);
                grow.Bounds = new Rectangle(right - Z(84), shut.Top, shut.Width, shut.Height);
                tuck.Bounds = new Rectangle(right - Z(114), shut.Top, shut.Width, shut.Height);
                well.Bounds = new Rectangle(Z(3), Z(Bar), ClientSize.Width - Z(6), ClientSize.Height - Z(Bar) - Z(3));
                line.Bounds = new Rectangle(0, 0, well.Width, Z(60));
            }

            protected override void OnLoad(EventArgs e)
            {
                base.OnLoad(e);
                string kept = Frame.capri.PlannerPlace;
                string[] parts = (kept ?? "").Split(',');
                int x, y, w, h;
                if (parts.Length >= 4 && int.TryParse(parts[0], out x) && int.TryParse(parts[1], out y) && int.TryParse(parts[2], out w) && int.TryParse(parts[3], out h))
                {
                    Rectangle wanted = new Rectangle(x, y, Math.Max(MinimumSize.Width, w), Math.Max(MinimumSize.Height, h));
                    Rectangle area = Fits.Area(this);
                    if (area.IntersectsWith(wanted)) Bounds = wanted;
                    if (parts.Length >= 5 && parts[4] == "max") WindowState = FormWindowState.Maximized;
                }
                else
                {
                    Rectangle area = Fits.Area(Frame);
                    Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
                    Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
                }
            }

            protected override void OnSizeChanged(EventArgs e)
            {
                base.OnSizeChanged(e);
                Place();
            }

            protected override void OnFormClosing(FormClosingEventArgs e)
            {
                if (starting != null) starting.Dispose();
                Rectangle kept = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                if (Frame.plannerSheet == this) Frame.capri.PlannerPlace =kept.X + "," + kept.Y + "," + kept.Width + "," + kept.Height + (WindowState == FormWindowState.Maximized ? ",max" : "");
                base.OnFormClosing(e);
            }

            // Its edges size it; the bar moves it; the editor takes its own clicks.
            protected override void WndProc(ref Message message)
            {
                base.WndProc(ref message);
                if (message.Msg != 0x0084 || WindowState == FormWindowState.Maximized) return;
                Point at = PointToClient(new Point((short)((long)message.LParam & 0xFFFF), (short)(((long)message.LParam >> 16) & 0xFFFF)));
                int edge = Z(6);
                bool left = at.X < edge, right = at.X >= ClientSize.Width - edge, top = at.Y < edge, bottom = at.Y >= ClientSize.Height - edge;
                if (top && left) message.Result = new IntPtr(13);
                else if (top && right) message.Result = new IntPtr(14);
                else if (bottom && left) message.Result = new IntPtr(16);
                else if (bottom && right) message.Result = new IntPtr(17);
                else if (left) message.Result = new IntPtr(10);
                else if (right) message.Result = new IntPtr(11);
                else if (top) message.Result = new IntPtr(12);
                else if (bottom) message.Result = new IntPtr(15);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                // Esc belongs to the editor here, not to closing the window.
                if (e.KeyCode == Keys.Escape) return;
                base.OnKeyDown(e);
            }
        }
    }

    // WebView2, carried inside her and laid in the Planner folder the first time; its two managed parts loaded from
    // there, its loader's folder given to it. Kept in its own class so nothing touches WebView2 before they are laid.
    internal static class PlannerWeb
    {
        private static string sdk;

        // How far WebView2 got with the editor, for her log and the checks: starting, loading the page, shown, or what stopped it.
        internal static volatile string Stage = "not started";

        private static string Sdk
        {
            get
            {
                if (sdk != null) return sdk;
                string folder = Path.Combine(Logs.DataOf(Brain.LogPath), PlannerHost.FolderName, "webview2-sdk");
                Directory.CreateDirectory(folder);
                Lay("Capri.WebView2.Core.dll", Path.Combine(folder, "Microsoft.Web.WebView2.Core.dll"));
                Lay("Capri.WebView2.WinForms.dll", Path.Combine(folder, "Microsoft.Web.WebView2.WinForms.dll"));
                Lay(Environment.Is64BitProcess ? "Capri.WebView2.Loader.x64.dll" : "Capri.WebView2.Loader.x86.dll", Path.Combine(folder, "WebView2Loader.dll"));
                AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs asked)
                {
                    string name = new AssemblyName(asked.Name).Name;
                    string path = Path.Combine(folder, name + ".dll");
                    return name.StartsWith("Microsoft.Web.WebView2", StringComparison.Ordinal) && File.Exists(path) ? Assembly.LoadFrom(path) : null;
                };
                return sdk = folder;
            }
        }

        private static bool Lay(string resource, string to)
        {
            using (Stream carried = typeof(PlannerWeb).Assembly.GetManifestResourceStream(resource))
            {
                if (carried == null) return false;
                if (File.Exists(to) && new FileInfo(to).Length == carried.Length) return true;
                using (FileStream file = File.Create(to)) carried.CopyTo(file);
                return true;
            }
        }

        // Whether this build carries WebView2 and this PC has its runtime.
        internal static bool Ready()
        {
#if WEBVIEW2
            try { return Sdk != null && File.Exists(Path.Combine(Sdk, "Microsoft.Web.WebView2.Core.dll")) && Loader() && RuntimeVersion() != null; }
            catch (Exception caughtOne) { Brain.Caught(caughtOne); return false; }
#else
            return false;
#endif
        }

#if WEBVIEW2
        private static bool loaderSet;

        // The loader's folder, given once, before anything else of WebView2's is asked.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool Loader()
        {
            if (!loaderSet) { Microsoft.Web.WebView2.Core.CoreWebView2Environment.SetLoaderDllFolderPath(Sdk); loaderSet = true; }
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string RuntimeVersion()
        {
            try { return Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString(); }
            catch (Microsoft.Web.WebView2.Core.WebView2RuntimeNotFoundException) { return null; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static Control Show(Control into, string folder, string address, Action shown, Action<string> failed)
        {
            Stage = "starting WebView2";
            Loader();
            Microsoft.Web.WebView2.WinForms.WebView2 view = new Microsoft.Web.WebView2.WinForms.WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Neon.Ground };
            into.Controls.Add(view);
            view.NavigationCompleted += delegate(object sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs done)
            {
                if (done.IsSuccess) { Stage = "shown"; if (shown != null) shown(); return; }
                Stage = "the page didn't load (" + done.WebErrorStatus + ")";
                if (failed != null) failed(Stage);
            };
            Start(view, Path.Combine(folder, "webview2"), address, failed);
            return view;
        }

        private static async void Start(Microsoft.Web.WebView2.WinForms.WebView2 view, string data, string address, Action<string> failed)
        {
            try
            {
                Microsoft.Web.WebView2.Core.CoreWebView2Environment environment = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, data, null);
                Stage = "starting its browser";
                await view.EnsureCoreWebView2Async(environment);
                Stage = "loading the page";
                view.Source = new Uri(address);
            }
            catch (Exception caughtOne)
            {
                Brain.Caught(caughtOne);
                Stage = "WebView2 couldn't start: " + caughtOne.Message;
                if (failed != null) failed(Stage);
            }
        }

        // The editor's own picture, as WebView2 draws it, for the checks' pictures.
        internal static System.Threading.Tasks.Task Preview(Control view, Stream into)
        {
            Microsoft.Web.WebView2.WinForms.WebView2 web = view as Microsoft.Web.WebView2.WinForms.WebView2;
            if (web == null || web.CoreWebView2 == null) return null;
            return web.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, into);
        }

        // For the speed measure: the page's frame time over a stretch, by requestAnimationFrame, in ms.
        internal static async System.Threading.Tasks.Task<string> FrameTime(Control view, int ms)
        {
            Microsoft.Web.WebView2.WinForms.WebView2 web = view as Microsoft.Web.WebView2.WinForms.WebView2;
            if (web == null || web.CoreWebView2 == null) return null;
            await web.ExecuteScriptAsync("window.__capriFrames=[];(function t(n){window.__capriFrames.push(n);if(window.__capriFrames.length<600)requestAnimationFrame(t);})(performance.now());");
            await System.Threading.Tasks.Task.Delay(ms);
            // The page gives its numbers bare; they are worded here.
            string said = (await web.ExecuteScriptAsync("(function(f){if(f.length<2)return [];var d=[];for(var i=1;i<f.length;i++)d.push(f[i]-f[i-1]);d.sort(function(a,b){return a-b});return [d.reduce(function(a,b){return a+b})/d.length,d[Math.floor(d.length*0.95)],d.length];})(window.__capriFrames)")).Trim('[', ']');
            string[] parts = said.Split(',');
            double mean, worst;
            if (parts.Length != 3 || !double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out mean) || !double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out worst)) return null;
            return mean.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " ms mean, " + worst.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " ms 95th over " + parts[2] + " frames";
        }
#else
        internal static Control Show(Control into, string folder, string address, Action shown, Action<string> failed) { return null; }
#endif
    }
}
