using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // A plug-in's MCP server online: JSON-RPC posted over https (streamable HTTP), answered as JSON or an event stream,
    // its session kept. A sign-in it asks for is the user's own, in their browser (McpSignIn); none of her saved keys
    // go with it.
    internal sealed class McpLimited : Stream
    {
        private readonly Stream inner;
        private long left;

        internal McpLimited(Stream from, long most)
        {
            inner = from;
            left = most;
        }

        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return false; } }
        public override bool CanWrite { get { return false; } }
        public override long Length { get { throw new NotSupportedException(); } }
        public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
        public override void SetLength(long value) { throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int got = inner.Read(buffer, offset, (int)Math.Min((long)count, left + 1));
            left -= got;
            if (left < 0) throw new McpException("An online server's answer was bigger than " + McpHttp.Most / (1024 * 1024) + " MB, so Capri stopped reading it.");
            return got;
        }
    }

    // Requests to online servers and sign-in pages: https only, or http to this PC itself; no redirects followed, no
    // user name in the address, and the headers Capri sets herself can't be replaced by a plug-in's.
    internal static class McpHttp
    {
        internal const int Most = 4 * 1024 * 1024;
        private static readonly string[] Reserved = { "host", "content-length", "content-type", "accept", "transfer-encoding", "connection", "expect", "user-agent", "mcp-session-id", "mcp-protocol-version", "range", "if-modified-since", "date", "proxy-connection" };

        internal static bool Allowed(string url, out Uri uri)
        {
            uri = null;
            if (string.IsNullOrEmpty(url) || url.Length > 2000 || !Uri.TryCreate(url, UriKind.Absolute, out uri)) return false;
            if (!string.IsNullOrEmpty(uri.UserInfo)) return false;
            if (uri.Scheme == Uri.UriSchemeHttps) return true;
            return uri.Scheme == Uri.UriSchemeHttp && Loopback(uri);
        }

        internal static bool Allowed(string url)
        {
            Uri uri;
            return Allowed(url, out uri);
        }

        internal static bool Loopback(Uri uri)
        {
            string host = uri.Host.ToLowerInvariant();
            return host == "127.0.0.1" || host == "localhost" || host == "[::1]" || host == "::1";
        }

        internal static bool ReservedHeader(string name) { return Array.IndexOf(Reserved, (name ?? "").ToLowerInvariant()) >= 0; }

        internal static string Line(string text, int most) { return Plugins.Clean(text, most); }

        internal static string Shown(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url ?? "", UriKind.Absolute, out uri)) return Line(url, 80);
            string path = uri.AbsolutePath == "/" ? "" : uri.AbsolutePath;
            return Line(uri.Scheme + "://" + uri.Authority + path, 100);
        }

        internal static HttpWebResponse Send(string method, string url, Dictionary<string, string> headers, string contentType, string accept, byte[] body, int timeout)
        {
            Uri uri;
            if (!Allowed(url, out uri)) throw new McpException("Capri only connects to https addresses, and " + Shown(url) + " isn't one.");
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
                request.Method = method;
                request.AllowAutoRedirect = false;
                request.Timeout = timeout;
                request.ReadWriteTimeout = timeout;
                request.UserAgent = "Capri";
                request.ServicePoint.Expect100Continue = false;
                if (Loopback(uri)) request.Proxy = null;
                if (accept != null) request.Accept = accept;
                if (headers != null)
                    foreach (KeyValuePair<string, string> pair in headers)
                    {
                        if (ReservedHeader(pair.Key) && !pair.Key.StartsWith("mcp-", StringComparison.OrdinalIgnoreCase)) continue;
                        request.Headers[pair.Key] = pair.Value;
                    }
                if (body != null)
                {
                    request.ContentType = contentType;
                    request.ContentLength = body.Length;
                    using (Stream to = request.GetRequestStream()) to.Write(body, 0, body.Length);
                }
                return (HttpWebResponse)request.GetResponse();
            }
            catch (WebException error)
            {
                HttpWebResponse response = error.Response as HttpWebResponse;
                if (response != null) return response;
                if (error.Status == WebExceptionStatus.Timeout) throw new McpException(uri.Host + " took longer than " + timeout / 1000 + " seconds to answer.");
                throw new McpException("Couldn't reach " + uri.Host + ": " + Line(error.Message, 160));
            }
            catch (ArgumentException error) { throw new McpException("Couldn't send a header to " + uri.Host + ": " + Line(error.Message, 120)); }
            catch (IOException error) { throw new McpException("Couldn't reach " + uri.Host + ": " + Line(error.Message, 160)); }
        }

        internal static string Text(HttpWebResponse response)
        {
            try
            {
                using (Stream from = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(new McpLimited(from, Most), new UTF8Encoding(false)))
                    return reader.ReadToEnd();
            }
            catch (WebException error) { throw new McpException("The connection to " + response.ResponseUri.Host + " broke: " + Line(error.Message, 120)); }
            catch (IOException error) { throw new McpException("The connection to " + response.ResponseUri.Host + " broke: " + Line(error.Message, 120)); }
        }

        internal static object Json(string url, Dictionary<string, string> headers, int timeout)
        {
            using (HttpWebResponse response = Send("GET", url, headers, null, "application/json", null, timeout))
            {
                int status = (int)response.StatusCode;
                if (status < 200 || status > 299) return null;
                object parsed;
                return PluginJson.TryParse(Text(response), out parsed) ? parsed : null;
            }
        }

        internal static object Post(string url, string contentType, string body, int timeout, out int status)
        {
            using (HttpWebResponse response = Send("POST", url, null, contentType, "application/json", new UTF8Encoding(false).GetBytes(body), timeout))
            {
                status = (int)response.StatusCode;
                string text = Text(response);
                object parsed;
                if (PluginJson.TryParse(text, out parsed)) return parsed;
                return null;
            }
        }
    }

    internal sealed class McpOnline : McpLink
    {
        private const string Session = "Mcp-Session-Id";
        private readonly PluginServer spec;
        private readonly string pluginId, root;
        private readonly object gate = new object();
        private long next;
        private bool started, ended;
        private string session, protocol, last = "";

        internal McpOnline(PluginServer server, string id, string pluginRoot)
        {
            spec = server;
            pluginId = id;
            root = pluginRoot;
        }

        internal override string Name { get { return spec.Name; } }

        internal override bool Alive { get { lock (gate) return started && !ended; } }

        internal override string Errors { get { lock (gate) return last; } }

        internal override void Start(int timeout)
        {
            lock (gate)
            {
                if (started && !ended) return;
                if (started) throw new McpException(spec.Name + " isn't connected. " + last);
                started = true;
            }
            try
            {
                object answer = Request("initialize", InitializeParams(), timeout);
                string version = PluginJson.Str(answer, "protocolVersion");
                if (version != "2025-06-18" && version != "2025-03-26") throw new McpException(spec.Name + " speaks an MCP version Capri doesn't (" + McpHttp.Line(version ?? "none", 20) + ").");
                Version = version;
                Instructions = PluginJson.Str(answer, "instructions");
                lock (gate) protocol = version;
                Send(PluginJson.Make("jsonrpc", "2.0", "method", "notifications/initialized"), -1, "notifications/initialized", timeout);
            }
            catch (McpException error)
            {
                lock (gate) { ended = true; last = error.Message; }
                throw;
            }
        }

        internal override object Request(string method, object parameters, int timeout)
        {
            long id;
            lock (gate)
            {
                if (ended) throw new McpException(spec.Name + " isn't connected. " + last);
                id = ++next;
            }
            Dictionary<string, object> message = PluginJson.Make("jsonrpc", "2.0", "id", id, "method", method);
            if (parameters != null) message["params"] = parameters;
            return Send(message, id, method, timeout);
        }

        private Dictionary<string, string> Headers(string token)
        {
            Dictionary<string, string> made = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> pair in spec.Headers) made[pair.Key] = McpServer.Expand(pair.Value, root);
            if (token != null) made["Authorization"] = "Bearer " + token;
            lock (gate)
            {
                if (session != null) made[Session] = session;
                if (protocol != null) made["MCP-Protocol-Version"] = protocol;
            }
            return made;
        }

        private object Send(Dictionary<string, object> message, long id, string method, int timeout)
        {
            byte[] body = new UTF8Encoding(false).GetBytes(PluginJson.Write(message));
            DateTime until = DateTime.UtcNow.AddMilliseconds(timeout);
            for (int attempt = 0; ; attempt++)
            {
                int left = (int)(until - DateTime.UtcNow).TotalMilliseconds;
                if (left <= 0) throw new McpException(spec.Name + " took longer than " + timeout / 1000 + " seconds to answer.");
                string token = McpSignIn.Token(pluginId, spec.Name, spec.Url);
                bool again = false;
                object result = null;
                using (HttpWebResponse response = McpHttp.Send("POST", spec.Url, Headers(token), "application/json", "application/json, text/event-stream", body, left))
                {
                    int status = (int)response.StatusCode;
                    if (status == 401)
                    {
                        // A turned-down token is renewed once with its refresh token; past that, they sign in again.
                        if (attempt == 0 && token != null && McpSignIn.Refreshed(pluginId, spec.Name, spec.Url, token) != null) again = true;
                        else throw new McpSignInNeeded();
                    }
                    else result = Answered(response, status, id, method, until);
                }
                if (!again) return result;
            }
        }

        private object Answered(HttpWebResponse response, int status, long id, string method, DateTime until)
        {
            if (status == 404 && session != null)
            {
                lock (gate) { ended = true; last = spec.Name + " ended the connection. Capri will connect again next time."; }
                throw new McpException(last);
            }
            if (status < 200 || status > 299)
            {
                if (spec.Legacy && method == "initialize" && (status == 400 || status == 404 || status == 405))
                    throw new McpException(spec.Name + " uses the older SSE connection, which Capri can't use yet. Ask its maker for its streamable HTTP address.");
                string said = "";
                try { said = McpHttp.Line(McpHttp.Text(response), 140); }
                catch (McpException) { }
                throw new McpException(spec.Name + " answered HTTP " + status + (said.Length == 0 ? "." : ": " + said));
            }
            if (method == "initialize")
            {
                string given = response.Headers[Session];
                if (!string.IsNullOrEmpty(given) && Regex.IsMatch(given, @"^[\x21-\x7e]{1,200}$")) lock (gate) session = given;
            }
            if (id < 0) return null;
            string type = (response.ContentType ?? "").ToLowerInvariant();
            bool found;
            object answer;
            if (type.Contains("text/event-stream")) answer = Streamed(response, id, until, out found);
            else
            {
                string text = McpHttp.Text(response);
                object parsed;
                if (!PluginJson.TryParse(text, out parsed)) throw new McpException(spec.Name + " didn't answer in a way Capri could read.");
                answer = Take(parsed, id, out found);
            }
            if (!found) throw new McpException(spec.Name + " ended its answer without replying.");
            return answer;
        }

        private object Streamed(HttpWebResponse response, long id, DateTime until, out bool found)
        {
            found = false;
            StringBuilder data = new StringBuilder();
            try
            {
                using (Stream from = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(new McpLimited(from, McpHttp.Most), new UTF8Encoding(false)))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (DateTime.UtcNow > until) throw new McpException(spec.Name + " took too long to finish answering.");
                        if (line.Length == 0)
                        {
                            if (data.Length == 0) continue;
                            object hit = Event(data.ToString(), id, out found);
                            data.Length = 0;
                            if (found) return hit;
                            continue;
                        }
                        if (line[0] == ':' || !line.StartsWith("data:", StringComparison.Ordinal)) continue;
                        string piece = line.Substring(5);
                        if (piece.StartsWith(" ", StringComparison.Ordinal)) piece = piece.Substring(1);
                        if (data.Length > 0) data.Append('\n');
                        data.Append(piece);
                    }
                }
            }
            catch (WebException error)
            {
                if (error.Status == WebExceptionStatus.Timeout) throw new McpException(spec.Name + " took too long to finish answering.");
                throw new McpException("The connection to " + spec.Name + " broke: " + McpHttp.Line(error.Message, 120));
            }
            catch (IOException error) { throw new McpException("The connection to " + spec.Name + " broke: " + McpHttp.Line(error.Message, 120)); }
            return data.Length == 0 ? null : Event(data.ToString(), id, out found);
        }

        private object Event(string text, long id, out bool found)
        {
            found = false;
            object parsed;
            if (!PluginJson.TryParse(text, out parsed)) return null;
            return Take(parsed, id, out found);
        }

        private object Take(object parsed, long id, out bool found)
        {
            found = false;
            List<object> batch = PluginJson.List(parsed);
            if (batch != null)
            {
                foreach (object one in batch)
                {
                    object hit = Take(one, id, out found);
                    if (found) return hit;
                }
                return null;
            }
            Dictionary<string, object> message = PluginJson.Map(parsed);
            if (message == null) return null;
            string method = PluginJson.Str(message, "method");
            if (method != null)
            {
                if (method == "notifications/tools/list_changed") Stale = true;
                return null;
            }
            object given = PluginJson.Get(message, "id");
            bool mine = given is double ? (long)(double)given == id : given is string && (string)given == id.ToString();
            if (!mine) return null;
            object error = PluginJson.Get(message, "error");
            if (error != null) throw new McpException(spec.Name + " said: " + McpHttp.Line(PluginJson.Str(error, "message") ?? "an error", 200));
            if (!message.ContainsKey("result")) return null;
            found = true;
            return PluginJson.Get(message, "result");
        }

        public override void Dispose()
        {
            string was;
            bool wasAlive;
            lock (gate)
            {
                wasAlive = started && !ended;
                ended = true;
                was = session;
                session = null;
            }
            if (was == null || !wasAlive) return;
            try
            {
                Dictionary<string, string> headers = Headers(McpSignIn.Cached(pluginId, spec.Name, spec.Url));
                headers[Session] = was;
                using (HttpWebResponse response = McpHttp.Send("DELETE", spec.Url, headers, null, null, null, 2000)) { }
            }
            catch (McpException) { }
        }
    }
}

namespace CapriDesktop
{
    // An online MCP server and its sign-in service on this PC, for the checks: registration, PKCE, tokens that
    // expire, a session and an event stream, as a real one does them.
    internal static class McpChecks
    {
        private sealed class WebFake : IDisposable
        {
            internal readonly System.Net.Sockets.TcpListener Listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            internal int Port, Registered, Refreshes, Authorizes, Requests;
            internal string Access = "at-0", Refresh = "rt-0", Code = "code-1", Challenge;
            internal bool PkceOk, SessionOk = true, ResourceOk = true, Listed;
            private volatile bool stop;

            internal string Base { get { return "http://127.0.0.1:" + Port; } }

            internal void Start()
            {
                Listener.Start();
                Port = ((IPEndPoint)Listener.LocalEndpoint).Port;
                System.Threading.Thread loop = new System.Threading.Thread(delegate()
                {
                    while (!stop)
                    {
                        System.Net.Sockets.TcpClient client;
                        try { client = Listener.AcceptTcpClient(); }
                        catch (System.Net.Sockets.SocketException) { return; }
                        catch (ObjectDisposedException) { return; }
                        System.Threading.ThreadPool.QueueUserWorkItem(delegate { Handle(client); });
                    }
                }) { IsBackground = true, Name = "Capri check online server" };
                loop.Start();
            }

            public void Dispose()
            {
                stop = true;
                try { Listener.Stop(); }
                catch (System.Net.Sockets.SocketException) { }
            }

            private static void Json(Stream to, int status, object value, params string[] headers) { McpTiny.Write(to, status, "application/json", PluginJson.Write(value), headers); }

            private void Handle(System.Net.Sockets.TcpClient client)
            {
                using (client)
                {
                    try
                    {
                        client.ReceiveTimeout = 10000;
                        Stream s = client.GetStream();
                        string method, target, body;
                        Dictionary<string, string> h;
                        if (!McpTiny.Read(s, out method, out target, out h, out body)) return;
                        System.Threading.Interlocked.Increment(ref Requests);
                        int mark = target.IndexOf('?');
                        string path = mark < 0 ? target : target.Substring(0, mark);
                        Dictionary<string, string> query = McpTiny.Query(target);
                        if (path == "/.well-known/oauth-protected-resource/mcp") Json(s, 200, PluginJson.Make("resource", Base + "/mcp", "authorization_servers", new List<object> { Base }));
                        else if (path == "/.well-known/oauth-authorization-server") Json(s, 200, PluginJson.Make("issuer", Base, "authorization_endpoint", Base + "/authorize", "token_endpoint", Base + "/token", "registration_endpoint", Base + "/register", "code_challenge_methods_supported", new List<object> { "S256" }));
                        else if (path == "/register" && method == "POST")
                        {
                            object asked = PluginJson.Parse(body);
                            List<object> uris = PluginJson.List(PluginJson.Get(asked, "redirect_uris"));
                            if (PluginJson.Str(asked, "token_endpoint_auth_method") != "none" || PluginJson.Str(asked, "client_name") != "Capri" || uris == null || uris.Count != 1 || !((uris[0] as string) ?? "").StartsWith("http://127.0.0.1:", StringComparison.Ordinal)) Json(s, 400, PluginJson.Make("error", "invalid_client_metadata"));
                            else { Registered++; Json(s, 201, PluginJson.Make("client_id", "smoke-client")); }
                        }
                        else if (path == "/authorize")
                        {
                            string redirect = query.ContainsKey("redirect_uri") ? query["redirect_uri"] : "";
                            if (query.ContainsKey("client_id") && query["client_id"] == "smoke-client" && query.ContainsKey("response_type") && query["response_type"] == "code" && query.ContainsKey("code_challenge_method") && query["code_challenge_method"] == "S256" && query.ContainsKey("code_challenge") && query.ContainsKey("state") && redirect.StartsWith("http://127.0.0.1:", StringComparison.Ordinal))
                            {
                                ResourceOk = ResourceOk && query.ContainsKey("resource") && query["resource"] == Base + "/mcp";
                                Challenge = query["code_challenge"];
                                Authorizes++;
                                McpTiny.Write(s, 302, "text/plain", "", "Location", redirect + "?code=" + Code + "&state=" + Uri.EscapeDataString(query["state"]));
                            }
                            else McpTiny.Write(s, 400, "text/plain", "bad authorize");
                        }
                        else if (path == "/token" && method == "POST")
                        {
                            Dictionary<string, string> form = McpTiny.Query("?" + body);
                            string grant = form.ContainsKey("grant_type") ? form["grant_type"] : "";
                            bool known = form.ContainsKey("client_id") && form["client_id"] == "smoke-client" && form.ContainsKey("resource") && form["resource"] == Base + "/mcp";
                            if (grant == "authorization_code" && known && form.ContainsKey("code") && form["code"] == Code && form.ContainsKey("code_verifier") && McpSignIn.Challenge(form["code_verifier"]) == Challenge)
                            {
                                PkceOk = true;
                                Access = "at-1";
                                Refresh = "rt-1";
                                Json(s, 200, PluginJson.Make("access_token", Access, "refresh_token", Refresh, "expires_in", 1.0, "token_type", "Bearer"));
                            }
                            else if (grant == "refresh_token" && known && form.ContainsKey("refresh_token") && form["refresh_token"] == Refresh)
                            {
                                Refreshes++;
                                Access = "at-" + (Refreshes + 1);
                                Refresh = "rt-" + (Refreshes + 1);
                                Json(s, 200, PluginJson.Make("access_token", Access, "refresh_token", Refresh, "expires_in", 3600.0, "token_type", "Bearer"));
                            }
                            else Json(s, 400, PluginJson.Make("error", "invalid_grant"));
                        }
                        else if (path == "/mcp") Mcp(s, method, h, body);
                        else McpTiny.Write(s, 404, "text/plain", "none");
                    }
                    catch (IOException) { }
                    catch (System.Net.Sockets.SocketException) { }
                    catch (ObjectDisposedException) { }
                    catch (FormatException) { }
                }
            }

            private void Mcp(Stream s, string method, Dictionary<string, string> h, string body)
            {
                string given;
                if (method == "DELETE") { McpTiny.Write(s, 200, "text/plain", ""); return; }
                if (!h.TryGetValue("Authorization", out given) || given != "Bearer " + Access)
                {
                    McpTiny.Write(s, 401, "text/plain", "sign in", "WWW-Authenticate", "Bearer resource_metadata=\"" + Base + "/.well-known/oauth-protected-resource/mcp\"");
                    return;
                }
                object message = PluginJson.Parse(body);
                string what = PluginJson.Str(message, "method");
                object id = PluginJson.Get(message, "id");
                if (what == "initialize")
                {
                    Json(s, 200, PluginJson.Make("jsonrpc", "2.0", "id", id, "result", PluginJson.Make("protocolVersion", "2025-06-18", "capabilities", PluginJson.Make("tools", PluginJson.Make("listChanged", true)), "serverInfo", PluginJson.Make("name", "smoke-web", "version", "1"))), "Mcp-Session-Id", "sess-abc");
                    return;
                }
                string session, protocol;
                if (!h.TryGetValue("Mcp-Session-Id", out session) || session != "sess-abc" || !h.TryGetValue("MCP-Protocol-Version", out protocol) || protocol != "2025-06-18")
                {
                    SessionOk = false;
                    McpTiny.Write(s, 400, "text/plain", "bad session");
                    return;
                }
                if (id == null) { McpTiny.Write(s, 202, "text/plain", ""); return; }
                if (what == "tools/list")
                {
                    Listed = true;
                    List<object> tools = new List<object>
                    {
                        PluginJson.Make("name", "add", "description", "Adds two numbers.", "annotations", PluginJson.Make("readOnlyHint", true), "inputSchema", PluginJson.Make("type", "object", "properties", PluginJson.Make("a", PluginJson.Make("type", "number"), "b", PluginJson.Make("type", "number")), "required", new List<object> { "a", "b" })),
                        PluginJson.Make("name", "greet", "description", "Says hello.", "inputSchema", PluginJson.Make("type", "object", "properties", PluginJson.Make()))
                    };
                    string stream = "event: message\ndata: " + PluginJson.Write(PluginJson.Make("jsonrpc", "2.0", "method", "notifications/message", "params", PluginJson.Make("level", "info"))) + "\n\n: keep-alive\n\ndata: "
                        + PluginJson.Write(PluginJson.Make("jsonrpc", "2.0", "id", 9999, "result", PluginJson.Make())) + "\n\n" + "event: message\ndata: " + PluginJson.Write(PluginJson.Make("jsonrpc", "2.0", "id", id, "result", PluginJson.Make("tools", tools))) + "\n\n";
                    McpTiny.Write(s, 200, "text/event-stream", stream);
                    return;
                }
                if (what == "tools/call")
                {
                    object args = PluginJson.Get(PluginJson.Get(message, "params"), "arguments");
                    object a = PluginJson.Get(args, "a"), b = PluginJson.Get(args, "b");
                    double sum = (a is double ? (double)a : 0) + (b is double ? (double)b : 0);
                    Json(s, 200, PluginJson.Make("jsonrpc", "2.0", "id", id, "result", PluginJson.Make("content", new List<object> { PluginJson.Make("type", "text", "text", sum.ToString(System.Globalization.CultureInfo.InvariantCulture)) })));
                    return;
                }
                Json(s, 200, PluginJson.Make("jsonrpc", "2.0", "id", id, "error", PluginJson.Make("code", -32601, "message", "no")));
            }
        }

        private static string Wait(Func<bool> done, int seconds)
        {
            DateTime until = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < until)
            {
                if (done()) return null;
                System.Threading.Thread.Sleep(50);
            }
            return "timed out";
        }

        // Run by Plugins.Check with its own folder in Plugins.Folder; that check puts back the plug-ins in use.
        internal static void Check(List<string> checks, string root)
        {
            Action<string> was = Plugins.OpenBrowser;
            string keysWas = McpSignIn.Folder;
            McpSignIn.Folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(Plugins.Folder)), "keys-" + Guid.NewGuid().ToString("N").Substring(0, 6), "plugins");
            int opened = 0;
            using (WebFake fake = new WebFake())
            {
                fake.Start();
                Plugins.OpenBrowser = delegate(string url)
                {
                    opened++;
                    System.Threading.Thread visit = new System.Threading.Thread(delegate()
                    {
                        try
                        {
                            string landing = null;
                            using (HttpWebResponse first = McpHttp.Send("GET", url, null, null, null, null, 10000)) landing = first.Headers["Location"];
                            if (landing != null) using (HttpWebResponse second = McpHttp.Send("GET", landing, null, null, null, null, 10000)) { }
                        }
                        catch (McpException) { }
                    }) { IsBackground = true };
                    visit.Start();
                };
                try
                {
                    Security.Load(Security.Of(Security.Trusted));
                    PluginServer web = new PluginServer { Name = "web", Url = fake.Base + "/mcp" };
                    web.Headers["X-Smoke"] = "${SMOKE_NOT_SET:-yes}";
                    PluginInfo online = new PluginInfo { Id = "smoke-online", Name = "Smoke online", Root = root, Hash = "smoke-online-1", Servers = new[] { web } };
                    Plugins.Use(new List<PluginInfo> { online }, true);
                    if (Plugins.Trouble("smoke-online") != McpSignInNeeded.Said || opened != 0 || fake.Authorizes != 0 || fake.Registered != 0 || Plugins.ToolsOf("smoke-online").Count != 0)
                        throw new Exception("An online plug-in that needs a sign-in didn't say so, or listing it opened a browser or registered Capri: " + Plugins.Trouble("smoke-online") + ", opened " + opened);

                    string result = null;
                    System.Threading.ManualResetEvent finished = new System.Threading.ManualResetEvent(false);
                    Plugins.SignIn("smoke-online", delegate(string problem) { result = problem ?? ""; finished.Set(); });
                    if (!finished.WaitOne(60000) || result != "") throw new Exception("Signing in to an online plug-in didn't work: " + (result ?? "timed out"));
                    if (opened != 1 || fake.Registered != 1 || fake.Authorizes != 1 || !fake.PkceOk || !fake.ResourceOk || !fake.Listed || !fake.SessionOk || fake.Refreshes != 1 || Plugins.Trouble("smoke-online") != null || Plugins.ToolsOf("smoke-online").Count != 2 || !Plugins.SignedIn("smoke-online"))
                        throw new Exception("Signing in didn't go through registration, PKCE and the resource, list the tools or keep the session: opened " + opened + ", registered " + fake.Registered + ", pkce " + fake.PkceOk + ", resource " + fake.ResourceOk + ", listed " + fake.Listed + ", session " + fake.SessionOk + ", refreshes " + fake.Refreshes + ", trouble " + Plugins.Trouble("smoke-online"));
                    string added = Plugins.Run("smoke_online__add", "{\"a\":2,\"b\":3}");
                    if (!added.Contains("trust=\"untrusted-plugin\">\n5\n</plugin_result>") || !fake.SessionOk) throw new Exception("An online tool didn't run through its session: " + added);
                    checks.Add("PASS online plug-ins: one that needs a sign-in says so without opening a browser, and signing in runs only when asked, with registration, PKCE and the resource, then lists its tools from an event stream and runs one from a JSON reply on its session, fenced as data");

                    fake.Access = "revoked";
                    string again = Plugins.Run("smoke_online__add", "{\"a\":10,\"b\":5}");
                    if (!again.Contains("\n15\n</plugin_result>") || fake.Refreshes != 2) throw new Exception("An online tool whose token was turned down didn't refresh and run: " + again + " (refreshes " + fake.Refreshes + ")");
                    string saved = McpSignIn.FileFor("smoke-online");
                    string raw = File.Exists(saved) ? File.ReadAllText(saved) : "";
                    if (raw.Length == 0 || raw.Contains("at-") || raw.Contains("rt-") || !raw.Contains("smoke-client")) throw new Exception("Their sign-in wasn't kept sealed: " + raw.Length + " bytes.");
                    // Sealed in memory too, in a folder of its own outside mods, open to this Windows user and the system only.
                    string held = McpSignIn.Held("smoke-online", "web", fake.Base + "/mcp");
                    bool sealedHeld = held != null && !held.Contains("at-") && Voice.Unprotect(held).StartsWith("at-", StringComparison.Ordinal);
                    bool outside = !saved.StartsWith(Path.GetFullPath(Plugins.Folder), StringComparison.OrdinalIgnoreCase), gated = McpSignIn.Gated(Path.GetDirectoryName(saved)), apart = outside && gated;
                    bool otherCant = McpSignIn.Cached("smoke-other", "web", fake.Base + "/mcp") == null;
                    // A code mod naming where sign-ins are kept is flagged before their yes.
                    bool flagged = Array.IndexOf(Mods.Reaching("string where = \"keys/plugins/smoke.signin.json\";"), "reads plug-in sign-ins") >= 0
                        && Array.IndexOf(Mods.Reaching("var t = typeof(McpSignIn);"), "reads plug-in sign-ins") >= 0;
                    otherCant = otherCant && flagged;
                    if (!sealedHeld || !apart || !otherCant) throw new Exception("A sign-in was held open, kept in reach, or given to another plug-in: sealed in memory " + sealedHeld + ", outside mods " + outside + ", gated " + gated + ", another plug-in got none and a mod naming them flagged " + otherCant);
                    if (opened != 1) throw new Exception("Refreshing a sign-in opened a browser.");
                    PluginInfo moved = new PluginInfo { Id = "smoke-online", Servers = new[] { new PluginServer { Name = "web", Url = fake.Base + "/moved" } } };
                    if (McpSignIn.Has(moved) || McpSignIn.Cached("smoke-online", "web", fake.Base + "/moved") != null || McpSignIn.Token("smoke-online", "web", fake.Base + "/moved") != null || McpSignIn.Cached("smoke-online", "web", fake.Base + "/mcp") == null)
                        throw new Exception("A sign-in would go to a server address it wasn't made for.");

                    // Lockdown keeps her off online plug-ins: nothing is offered, run or even asked of the server.
                    Security.Load(Security.Of(Security.Lockdown));
                    int asked = fake.Requests;
                    string locked = Plugins.ListNow(online), lockedRun = Plugins.Run("smoke_online__add", "{\"a\":1,\"b\":1}");
                    bool none = Plugins.Tools().Length == 0;
                    Security.Load(Security.Of(Security.Trusted));
                    if (locked == null || !lockedRun.Contains("off on Lockdown") || !none || fake.Requests != asked)
                        throw new Exception("Lockdown didn't keep her off an online plug-in: listing " + locked + ", run " + lockedRun + ", offered none " + none + ", requests " + (fake.Requests - asked));
                    if (Wait(delegate { Plugins.Tools(); return Plugins.ToolsOf("smoke-online").Count == 2 && Plugins.Trouble("smoke-online") == null; }, 30) != null)
                        throw new Exception("Leaving Lockdown didn't bring the online plug-in's tools back: " + Plugins.Trouble("smoke-online"));

                    Plugins.Forget("smoke-online");
                    if (File.Exists(saved) || Plugins.SignedIn("smoke-online")) throw new Exception("Forgetting a plug-in kept its sign-in.");
                    string signedOut = Plugins.Run("smoke_online__add", "{\"a\":1,\"b\":1}");
                    if (!signedOut.Contains("sign in first") || Plugins.Trouble("smoke-online") != McpSignInNeeded.Said || opened != 1) throw new Exception("A plug-in with no sign-in didn't ask for one without opening a browser: " + signedOut);
                    checks.Add("PASS online plug-ins: a turned-down token is renewed with its refresh token and the call goes through, the sign-in is kept sealed for this Windows user, in memory as on disk, in a folder outside mods open only to this user and the system, only for that plug-in and the address it was made for, Lockdown offers and runs none and asks nothing of the server, leaving Lockdown brings its tools back, and taking the plug-in out drops its sign-in");
                }
                finally { Plugins.OpenBrowser = was; McpSignIn.Folder = keysWas; }
            }
        }
    }
}
