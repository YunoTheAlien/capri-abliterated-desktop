using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;

namespace CapriDesktop
{
    // The XDG Screenshot portal (org.freedesktop.portal.Screenshot), the standard screenshot on Wayland that GNOME, KDE
    // and wlroots desktops share. Stock Ubuntu has no screenshot tool for Wayland, so without it she can't see the screen
    // at all; the owner's order (12:10, 19 Nov) is the portal first, then the tool list. It runs on the D-Bus library
    // Avalonia already brings, so nothing new is installed.
    //
    // The first time, the desktop asks them whether Capri may take screenshots; the answer is kept by the desktop. While
    // that question is open the portal answers nothing else, so she asks one at a time, closes her own question when it
    // runs out of time, and Watch stops asking after a no or no answer until Watch my screen is turned on again.
    internal static class ScreenshotPortal
    {
        private const string Service = "org.freedesktop.portal.Desktop";
        private const string DesktopPath = "/org/freedesktop/portal/desktop";
        private const string RequestInterface = "org.freedesktop.portal.Request";
        private static readonly object gate = new object();
        private static Connection connection;
        private static bool busy;

        // Set after a no, a failure or a question left unanswered; glances skip the portal until it's cleared.
        internal static volatile bool Refused;
        // How long the desktop's question may stay open before she gives up on this look.
        internal static int AskSeconds = 60;
        // The smoke test's stand-in for the desktop: given the token, it answers (code, file uri) or null for no answer.
        internal static Func<string, (uint Code, string Uri)?> Answer;
        internal static string Problem { get; private set; }

        // A screenshot through the portal as PNG bytes, or null so the tool list gets its turn.
        internal static byte[] Shot(bool quiet)
        {
            if (quiet && Refused) return null;
            lock (gate) { if (busy) return null; busy = true; }
            try
            {
                (uint Code, string Uri)? answer = Answer != null ? Answer("capri-test") : Ask();
                if (answer == null) { Refused = true; return null; }
                if (answer.Value.Code != 0) { Refused = true; Problem = answer.Value.Code == 1 ? "The desktop's screenshot question was answered no." : "The desktop couldn't take the screenshot."; return null; }
                byte[] shot = Collect(answer.Value.Uri);
                if (shot == null) { Problem = "The screenshot portal's file couldn't be read."; return null; }
                Refused = false; Problem = null;
                return shot;
            }
            finally { lock (gate) busy = false; }
        }

        // The portal saves each screenshot as a file (GNOME puts it in Pictures/Screenshots). She reads it and deletes it,
        // so watching doesn't fill their Pictures, but only a fresh file the portal has just named for her.
        internal static byte[] Collect(string uri)
        {
            if (string.IsNullOrEmpty(uri)) return null;
            try
            {
                Uri where = new Uri(uri);
                if (!where.IsFile) return null;
                FileInfo file = new FileInfo(where.LocalPath);
                if (!file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0) return null;
                if (DateTime.UtcNow - file.LastWriteTimeUtc > TimeSpan.FromMinutes(2)) return null;
                byte[] shot = File.ReadAllBytes(file.FullName);
                try { file.Delete(); }
                catch (Exception) { }
                return shot.Length > 0 ? shot : null;
            }
            catch (Exception) { return null; }
        }

        private static (uint Code, string Uri)? Ask()
        {
            Connection talk = Connect();
            if (talk == null) return null;
            string token = "capri" + Guid.NewGuid().ToString("N").Substring(0, 16);
            string request = DesktopPath + "/request/" + talk.UniqueName.TrimStart(':').Replace('.', '_') + "/" + token;
            var answer = new TaskCompletionSource<(uint Code, string Uri)>(TaskCreationOptions.RunContinuationsAsynchronously);
            List<IDisposable> matches = new List<IDisposable>();
            try
            {
                // Listening starts before asking, so a desktop that answers at once isn't missed.
                matches.Add(Listen(talk, request, answer));
                string handle;
                using (MessageWriter writer = talk.GetMessageWriter())
                {
                    writer.WriteMethodCallHeader(destination: Service, path: DesktopPath, @interface: "org.freedesktop.portal.Screenshot", member: "Screenshot", signature: "sa{sv}");
                    writer.WriteString("");
                    ArrayStart options = writer.WriteDictionaryStart();
                    writer.WriteDictionaryEntryStart(); writer.WriteString("handle_token"); writer.WriteVariantString(token);
                    writer.WriteDictionaryEntryStart(); writer.WriteString("interactive"); writer.WriteVariantBool(false);
                    writer.WriteDictionaryEnd(options);
                    MessageBuffer message = writer.CreateMessage();
                    handle = Task.Run(() => talk.CallMethodAsync(message, (reply, _) => reply.GetBodyReader().ReadObjectPathAsString(), null)).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                }
                // An older portal names its own request; listen there too.
                if (!string.IsNullOrEmpty(handle) && handle != request) { matches.Add(Listen(talk, handle, answer)); request = handle; }
                if (answer.Task.Wait(TimeSpan.FromSeconds(AskSeconds))) return answer.Task.Result;
                Problem = "The desktop's screenshot question wasn't answered.";
                Close(talk, request);
                return null;
            }
            catch (Exception error)
            {
                // No portal here (a plain X server, a bus without xdg-desktop-portal) or it failed: the tool list goes next.
                Problem = error.GetType().Name + ": " + error.Message;
                return null;
            }
            finally { foreach (IDisposable match in matches) match.Dispose(); }
        }

        private static IDisposable Listen(Connection talk, string path, TaskCompletionSource<(uint Code, string Uri)> answer)
        {
            MatchRule rule = new MatchRule { Type = MessageType.Signal, Sender = Service, Interface = RequestInterface, Member = "Response", Path = path };
            return Task.Run(() => talk.AddMatchAsync(rule, (Message message, object _) =>
            {
                Reader reader = message.GetBodyReader();
                uint code = reader.ReadUInt32();
                Dictionary<string, VariantValue> results = reader.ReadDictionaryOfStringToVariantValue();
                string uri = results.TryGetValue("uri", out VariantValue value) && value.Type == VariantValueType.String ? value.GetString() : null;
                return (code, uri);
            }, (Exception error, (uint Code, string Uri) response, object _, object __) =>
            {
                if (error == null) answer.TrySetResult(response);
            }, ObserverFlags.None, null, null, false).AsTask()).WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
        }

        // Only the connection that asked may close the question, so this goes on hers.
        private static void Close(Connection talk, string request)
        {
            try
            {
                using (MessageWriter writer = talk.GetMessageWriter())
                {
                    writer.WriteMethodCallHeader(destination: Service, path: request, @interface: RequestInterface, member: "Close");
                    MessageBuffer message = writer.CreateMessage();
                    Task.Run(() => talk.CallMethodAsync(message)).WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                }
            }
            catch (Exception error) { Brain.Log("portal", -1, "Closing the screenshot question: " + error.Message); }
        }

        private static Connection Connect()
        {
            Connection talk = connection;
            if (talk != null) return talk;
            string address = Address.Session;
            if (string.IsNullOrEmpty(address)) return null;
            Connection made = new Connection(address);
            try
            {
                Task.Run(() => made.ConnectAsync().AsTask()).WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                Connection kept = made;
                connection = kept;
                made = null;
                // A dropped bus (a new login session) gets a new connection on the next look.
                _ = kept.DisconnectedAsync().ContinueWith(_ => { if (connection == kept) connection = null; kept.Dispose(); }, TaskScheduler.Default);
                return kept;
            }
            catch (Exception error) { Problem = error.GetType().Name + ": " + error.Message; return null; }
            finally { made?.Dispose(); }
        }
    }
}
