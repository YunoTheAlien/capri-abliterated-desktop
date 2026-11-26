using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CapriDesktop
{
    // The Room camera: the Room page in its own window, its frames going to Capri Camera, one feed at a time with Capri
    // only. Nothing leaves this PC: RoomHost serves the page from 127.0.0.1, so it runs with no relay, counting or free
    // brain.
    internal sealed partial class Companion
    {
        private RoomHost roomHost;
        private CapriCamera roomCamera;
        private readonly object roomGate = new object();
        // True while the Room feed is the one on Capri Camera.
        private volatile bool roomFeed;

        internal static string RemotePage
        {
            get
            {
                string page = Environment.GetEnvironmentVariable("CAPRI_REMOTE_PAGE");
                if (page != null && (page.StartsWith("http://127.0.0.1:", StringComparison.Ordinal) || page.StartsWith("http://localhost:", StringComparison.Ordinal) || page.StartsWith("https://", StringComparison.Ordinal)) && page.EndsWith("/", StringComparison.Ordinal) && page.IndexOfAny(new[] { '"', ' ', '#', '?' }) < 0) return page;
                // A page of ours, none yet; with none, Room says it isn't available.
                return "";
            }
        }

        internal const string NoRoom = "Room isn't available until we have our own server.";

        // Room on Capri Camera. Her own picture stops feeding it first, so the two never fight over the device.
        internal void OpenRoomWindow()
        {
            if (string.IsNullOrEmpty(RemotePage)) { Remark("", NoRoom, true); return; }
            if (VirtualCamera.On)
            {
                VirtualCamera.Stop();
                ReadOnAir();
            }
            roomFeed = true;
            if (roomHost == null)
            {
                try
                {
                    roomHost = new RoomHost(RemotePage);
                    roomHost.CameraState = delegate { return CapriCamera.State; };
                    roomHost.CameraInstall = CapriCamera.AskInstall;
                    roomHost.Frame = RoomFrame;
                }
                catch (ArgumentException) { }
                catch (SocketException) { }
            }
            RoomWindow(roomHost != null ? roomHost.Url : RemotePage + "?room=pc");
            StreamLabel();
        }

        // A frame from the Room page, onto Capri Camera while Room is the feed; dropped otherwise.
        private void RoomFrame(byte[] jpeg)
        {
            if (!roomFeed || jpeg == null || jpeg.Length == 0) return;
            CapriCamera camera;
            lock (roomGate)
            {
                if (!roomFeed) return;
                if (roomCamera == null) roomCamera = new CapriCamera();
                camera = roomCamera;
            }
            camera.Send(jpeg);
        }

        // Room off Capri Camera, so Capri only can have it.
        private void RoomOff()
        {
            CapriCamera camera;
            lock (roomGate)
            {
                roomFeed = false;
                camera = roomCamera;
                roomCamera = null;
            }
            try { if (camera != null) camera.Dispose(); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // Ticked in her menu while Room is the feed and its window is open.
        private bool RoomShowing { get { RoomHost host = roomHost; return roomFeed && host != null && host.Watching > 0; } }

        private void RoomTick()
        {
            RoomHost host = roomHost;
            if (host != null) host.Talk(TalkingNow);
        }

        internal static Action<string> RoomWindow = delegate(string url)
        {
            string[] places = { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) };
            foreach (string place in places)
            {
                if (string.IsNullOrEmpty(place)) continue;
                string edge = Path.Combine(place, @"Microsoft\Edge\Application\msedge.exe");
                if (!File.Exists(edge)) continue;
                try
                {
                    string own = url.StartsWith("http://127.0.0.1:", StringComparison.Ordinal) ? " --user-data-dir=\"" + Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Capri Room") + "\" --disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows --no-first-run" : "";
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(edge, "--app=\"" + url + "\" --window-size=1180,880" + own) { UseShellExecute = false });
                    return;
                }
                catch (System.ComponentModel.Win32Exception) { }
            }
            Hands.Run(url);
        };

        // RoomHost on its own: the page from the site through 127.0.0.1 with only GETs of its files, a wrong token, host or
        // path refused, the camera's state and install answered, frames handed on, and the feeds taking turns.
#if CHECKS
        private void SmokeRoomHost(List<string> checks)
        {
            try { new RoomHost("https://room.example/elsewhere/").Dispose(); throw new Exception("RoomHost took a page that isn't a Room page."); }
            catch (ArgumentException) { }
            if (RemotePage != "" && Environment.GetEnvironmentVariable("CAPRI_REMOTE_PAGE") == null) throw new Exception("The Room page still has somewhere to go with no server of ours: " + RemotePage);
            // As shipped there's no Room page of ours: Room says so, and opens and serves nothing.
            if (RemotePage == "")
            {
                Action<string> windowWas = RoomWindow;
                string opened = null;
                bool hostless = roomHost == null;
                RoomWindow = delegate(string url) { opened = url; };
                Func<bool> told = delegate
                {
                    List<ChatMessage> said = brain.History;
                    return said.Count > 0 && said[said.Count - 1].Role == "assistant" && said[said.Count - 1].Content == NoRoom;
                };
                try
                {
                    OpenRoomWindow();
                    for (int i = 0; i < 250 && !told(); i++) { System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(20); }
                    if (!told() || opened != null || (hostless && roomHost != null) || roomFeed)
                        throw new Exception("Room had somewhere to go with no page of ours: said it isn't available " + told() + ", opened " + (opened ?? "nothing") + ", a host made " + (hostless && roomHost != null) + ", the feed taken " + roomFeed);
                }
                finally { RoomWindow = windowWas; }
                checks.Add("PASS with no Room page of ours, Room says it isn't available and opens, serves and feeds nothing");
            }
            List<string> fetched = new List<string>();
            List<int> frames = new List<int>();
            bool feedWas = roomFeed;
            using (RoomHost host = new RoomHost("https://room.example/remote/"))
            {
                host.Fetch = delegate(string url) { lock (fetched) fetched.Add(url); return url.EndsWith("remote/index.html", StringComparison.Ordinal) ? Encoding.UTF8.GetBytes("<html>room</html>") : null; };
                host.CameraState = delegate { return "needs-install"; };
                host.CameraInstall = delegate { return "ready"; };
                host.Frame = delegate(byte[] jpeg) { lock (frames) frames.Add(jpeg.Length); };
                string root = "http://127.0.0.1:" + host.ListeningPort + "/";
                string token = host.Url.Substring(root.Length).Split('/')[0];
                Func<string, string, byte[], KeyValuePair<int, string>> ask = delegate(string method, string path, byte[] body)
                {
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(root + path);
                    request.Method = method;
                    request.Proxy = null;
                    request.AllowAutoRedirect = false;
                    request.Timeout = 5000;
                    if (body != null) { request.ContentLength = body.Length; using (Stream up = request.GetRequestStream()) up.Write(body, 0, body.Length); }
                    try
                    {
                        using (HttpWebResponse answer = (HttpWebResponse)request.GetResponse())
                        using (StreamReader read = new StreamReader(answer.GetResponseStream())) return new KeyValuePair<int, string>((int)answer.StatusCode, read.ReadToEnd());
                    }
                    catch (WebException error)
                    {
                        HttpWebResponse answer = error.Response as HttpWebResponse;
                        return new KeyValuePair<int, string>(answer == null ? -1 : (int)answer.StatusCode, "");
                    }
                };
                KeyValuePair<int, string> page = ask("GET", token + "/remote/", null);
                KeyValuePair<int, string> again = ask("GET", token + "/remote/index.html", null);
                KeyValuePair<int, string> wrongToken = ask("GET", "0000/remote/", null);
                KeyValuePair<int, string> climbing = ask("GET", token + "/remote/..%2F..%2Fsecret", null);
                KeyValuePair<int, string> camera = ask("GET", token + "/camera", null);
                KeyValuePair<int, string> install = ask("POST", token + "/camera/install", new byte[0]);
                KeyValuePair<int, string> frame = ask("POST", token + "/frame", new byte[] { 0xFF, 0xD8, 1, 2, 3 });
                if (page.Key != 200 || page.Value != "<html>room</html>" || again.Key != 200 || wrongToken.Key != 404 || (climbing.Key != 404 && climbing.Key != -1))
                    throw new Exception("RoomHost served the wrong things: page " + page.Key + ", again " + again.Key + ", wrong token " + wrongToken.Key + ", a climbing path " + climbing.Key);
                if (camera.Value != "{\"state\":\"needs-install\"}" || install.Value != "{\"state\":\"ready\"}" || frame.Key != 200 || frames.Count != 1 || frames[0] != 5)
                    throw new Exception("RoomHost's camera calls went wrong: " + camera.Value + " / " + install.Value + " / frame " + frame.Key + ", " + frames.Count + " handed on");
                lock (fetched)
                    if (fetched.Count != 1 || fetched[0] != "https://room.example/remote/index.html")
                        throw new Exception("RoomHost fetched more than the page's own file, or fetched it twice: " + string.Join(" ", fetched.ToArray()));
            }
            checks.Add("PASS RoomHost serves the Room page from 127.0.0.1 behind its token, fetching only the page's own files once each with a GET, refuses a wrong token or a climbing path, answers Capri Camera's state and install, and hands the frames on");

            // The two feeds take turns: a Room frame is dropped once Capri only has the camera.
            try
            {
                roomFeed = false;
                RoomFrame(new byte[] { 1, 2, 3 });
                bool none;
                lock (roomGate) none = roomCamera == null;
                if (!none) throw new Exception("A Room frame reached Capri Camera while Room wasn't the feed.");
            }
            finally { roomFeed = feedWas; }
            checks.Add("PASS Room and Capri only take turns on Capri Camera: a Room frame is dropped while Room isn't the feed");
        }
#endif
    }
}
