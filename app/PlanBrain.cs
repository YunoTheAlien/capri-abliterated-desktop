using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // Their own Claude or ChatGPT plan as her brain, through Claude Code or Codex on their PC (Lachy), signed in by the
    // app itself; she never reads its login. Each call runs it with no tools, MCP or saving; her tools go as text.
    internal static class PlanBrain
    {
        internal const int Wait = 120000;
        private const int Most = 4 * 1024 * 1024;
        // For her smoke test: the app to run in place of the one on this PC, and where its runs go.
        internal static string ClaudeOverride, CodexOverride, FolderOverride;
        // Claude Code without their CLAUDE.md files, hooks, plugins, skills or slash commands, so it's only her brain. Husk mode's
        // hooks don't fire for her own replies either.
        private static readonly string[] Newer = { "--safe-mode", "--disable-slash-commands" };
        private static readonly HashSet<string> dropped = new HashSet<string>(StringComparer.Ordinal);

        // For her smoke test: back to trying every newer flag.
        internal static void Retry() { lock (dropped) dropped.Clear(); }

        // Claude Code or Codex where their installers put it: its own folder, on PATH, or npm's folder.
        internal static string Find(string kind)
        {
            string chosen = kind == "codex" ? CodexOverride : ClaudeOverride;
            if (!string.IsNullOrEmpty(chosen)) return File.Exists(chosen) ? chosen : null;
            string name = kind == "codex" ? "codex" : "claude";
            List<string> spots = new List<string>();
            if (kind != "codex") spots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".local\bin\claude.exe"));
            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                string folder = dir.Trim().Trim('"');
                if (folder.Length == 0 || folder.IndexOfAny(Path.GetInvalidPathChars()) >= 0) continue;
                spots.Add(Path.Combine(folder, name + ".exe"));
                spots.Add(Path.Combine(folder, name + ".cmd"));
            }
            spots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", name + ".cmd"));
            foreach (string spot in spots) if (File.Exists(spot)) return spot;
            return null;
        }

        // One run of the app: her system prompt and their chat in, its reply out. A reply they talk over ends the run at once.
        internal static string Think(string kind, string model, List<ChatMessage> messages, Func<bool> cancelled)
        {
            string app = Find(kind);
            if (app == null) throw new BrainException(Plans.Missing(kind), true);
            // An empty folder of her own, so the app reads none of their projects or settings.
            string home = FolderOverride ?? Path.Combine(HuskHook.Data, "plan"), run = Path.Combine(home, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(run);
            try
            {
                string system;
                List<string> pictures;
                string prompt = Prompt(messages, kind == "codex", out system, out pictures);
                ChatMessage imageMessage = messages.Find(delegate(ChatMessage message) { return message.ImageMime != null; });
                string mime = imageMessage == null ? "image/jpeg" : imageMessage.ImageMime;
                List<string> args = new List<string>();
                if (kind == "codex")
                {
                    string answer = Path.Combine(run, "reply.txt");
                    args.AddRange(new[] { "exec", "--skip-git-repo-check", "--ephemeral", "--sandbox", "read-only", "--color", "never", "-C", run, "-o", answer });
                    // None of Codex's own shell, browser, apps or plugins: it only writes her reply. A Codex without one of these ignores it.
                    foreach (string feature in new[] { "shell_tool", "browser_use", "in_app_browser", "apps", "plugins" }) { args.Add("-c"); args.Add("features." + feature + "=false"); }
                    if (!string.IsNullOrEmpty(model) && model != "default") { args.Add("-m"); args.Add(model); }
                    for (int i = 0; i < pictures.Count; i++)
                    {
                        string picture = Path.Combine(run, "screen" + i + ".jpg");
                        File.WriteAllBytes(picture, Convert.FromBase64String(pictures[i]));
                        args.Add("-i");
                        args.Add(picture);
                    }
                    args.Add("-");
                    Ran codex = Start(kind, app, args, prompt, run, cancelled);
                    string reply = File.Exists(answer) ? File.ReadAllText(answer, Encoding.UTF8).Trim() : "";
                    if (codex.Code != 0 || reply.Length == 0) throw Problem(kind, codex.Errors + "\n" + codex.Output);
                    return reply;
                }
                string rules = Path.Combine(run, "system.txt");
                File.WriteAllText(rules, system, new UTF8Encoding(false));
                args.AddRange(new[] { "-p", "--verbose", "--input-format", "stream-json", "--output-format", "stream-json", "--tools", "", "--strict-mcp-config",
                    "--no-session-persistence", "--setting-sources", "project", "--system-prompt", "You are Capri.", "--append-system-prompt-file", rules });
                if (!string.IsNullOrEmpty(model)) { args.Add("--model"); args.Add(model); }
                Ran claude;
                while (true)
                {
                    List<string> asked = new List<string>(args);
                    lock (dropped) foreach (string flag in Newer) if (!dropped.Contains(flag)) asked.Add(flag);
                    claude = Start(kind, app, asked, Streamed(prompt, pictures, mime), run, cancelled);
                    // An older Claude Code that doesn't know one of the newer flags runs again without it, and keeps running without it.
                    Match unknown = Regex.Match(claude.Errors + claude.Output, @"unknown option '(--[a-z-]+)'");
                    if (!unknown.Success || Array.IndexOf(Newer, unknown.Groups[1].Value) < 0) break;
                    lock (dropped) if (!dropped.Add(unknown.Groups[1].Value)) break;
                }
                string said = null;
                bool failed = false;
                foreach (string line in claude.Output.Split('\n'))
                {
                    Dictionary<string, object> row;
                    try { row = HuskJson.Read(line.Trim()) as Dictionary<string, object>; }
                    catch (FormatException) { continue; }
                    if (HuskJson.Text(row, "type") != "result") continue;
                    said = HuskJson.Get(row, "result") as string ?? "";
                    failed = HuskJson.Get(row, "is_error") is bool && (bool)HuskJson.Get(row, "is_error");
                }
                if (said == null || failed || said.Trim().Length == 0) throw Problem(kind, Brain.RedactedImages((said ?? "") + "\n" + claude.Errors, string.Join(" ", pictures.ToArray())));
                return said.Trim();
            }
            finally
            {
                try { Directory.Delete(run, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        // A tiny run when they pick their plan, so a missing sign-in shows in BRAIN straight away instead of on her first reply.
        internal static void Check(string kind)
        {
            Think(kind, kind == "codex" ? null : "haiku", new List<ChatMessage> { new ChatMessage("system", "Reply with OK and nothing else."), new ChatMessage("user", "OK?") }, null);
        }

        // Her chat as one prompt: a single ask goes as it is, and a longer chat as a transcript, oldest first. Codex has no
        // system prompt of its own to set, so hers goes at the top, and it's told not to use its own tools.
        internal static string Prompt(List<ChatMessage> messages, bool codex, out string system, out List<string> pictures)
        {
            StringBuilder rules = new StringBuilder();
            pictures = new List<string>();
            List<ChatMessage> chat = new List<ChatMessage>();
            foreach (ChatMessage message in messages)
            {
                if (message == null) continue;
                if (message.Images != null) pictures.AddRange(message.Images);
                if (message.Role == "system") { if (rules.Length > 0) rules.Append("\n\n"); rules.Append(message.Content); }
                else chat.Add(message);
            }
            system = rules.ToString();
            string asked;
            // A message that starts with / would be one of the app's own commands.
            if (chat.Count == 1 && chat[0].Role == "user") asked = (chat[0].Content ?? "").TrimStart().StartsWith("/", StringComparison.Ordinal) ? "Them: " + chat[0].Content : chat[0].Content ?? "";
            else
            {
                StringBuilder transcript = new StringBuilder("The chat so far, oldest first. Write only Capri's next message.\n");
                foreach (ChatMessage message in chat) transcript.Append('\n').Append(message.Role == "assistant" ? "Capri: " : "Them: ").Append(message.Content ?? "");
                asked = transcript.ToString();
            }
            if (!codex) return asked;
            return "<instructions>\n" + system + "\nAnswer with text only, as Capri. Don't run commands, read files or change anything: her own tools are listed above when she has any.\n</instructions>\n\n" + asked;
        }

        // Claude Code's stream-json input: one message from them, with any screenshots as pictures.
        internal static string Streamed(string prompt, List<string> pictures, string mime = "image/jpeg")
        {
            List<object> blocks = new List<object>();
            foreach (string picture in pictures) blocks.Add(HuskJson.Map("type", "image", "source", HuskJson.Map("type", "base64", "media_type", mime, "data", picture)));
            blocks.Add(HuskJson.Map("type", "text", "text", prompt));
            return HuskJson.Write(HuskJson.Map("type", "user", "message", HuskJson.Map("role", "user", "content", blocks))) + "\n";
        }

        internal enum Failure { Other, SignIn, Limit }

        internal static Failure ProblemKind(string output)
        {
            string text = Regex.Replace(output ?? "", @"\s+", " ").Trim();
            if (Regex.IsMatch(text, @"not (?:logged|signed) in|log ?in|sign ?in|authenticat|oauth|unauthori[sz]ed|invalid api key|\b401\b|credential", RegexOptions.IgnoreCase))
                return Failure.SignIn;
            if (Regex.IsMatch(text, @"usage limit|limit reached|hit your limit|rate.?limit|quota|too many requests|\b429\b|limit will reset|resets? at|\breset(?:s|ting)?\s+\d", RegexOptions.IgnoreCase))
                return Failure.Limit;
            return Failure.Other;
        }

        // What went wrong, in her words: signed out, out of plan for now, or the app's own reason.
        internal static BrainException Problem(string kind, string output)
        {
            string text = Regex.Replace(Brain.Redacted(Brain.Scrubbed(output)), @"\s+", " ").Trim();
            string app = Plans.App(kind);
            Failure failure = ProblemKind(text);
            if (failure == Failure.SignIn)
                return new BrainException(kind == "codex" ? "Codex isn't signed in. Run codex login first." : "Claude Code isn't signed in. Run claude and sign in.", true);
            if (failure == Failure.Limit)
                return new BrainException("Your " + Plans.Plan(kind) + " is out of messages for now.", true) { Detail = app + " says: " + Short(text) };
            return new BrainException(app + " couldn't answer" + (text.Length == 0 ? "." : ": " + Short(text)), true);
        }

        private static string Short(string text) { return text.Length > 160 ? text.Substring(0, 157) + "..." : text; }

        private sealed class Ran
        {
            internal int Code;
            internal string Output = "", Errors = "";
        }

        // Adds the models their Codex or Claude Code really offers to the ones she names. Codex keeps its list in a cache file;
        // Claude Code is asked once, with no tools, no MCP servers and nothing saved, and given six seconds to answer.
        internal static void ListModels(string kind, string app, List<string> models)
        {
            if (Brain.Off) return;
            try
            {
                List<string> listed = kind == "codex"
                    ? BetterModels.Codex(BetterModels.CodexCache(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)))
                    : ClaudeModels(app);
                if (Brain.Off) return;
                foreach (string model in listed) BetterModels.Add(models, model);
                BetterModels.Remember(kind, listed);
            }
            catch (BrainException) { }
            catch (WebException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (FormatException) { }
            catch (ArgumentException) { }
        }

        private static List<string> ClaudeModels(string app)
        {
            string run = Path.Combine(FolderOverride ?? Path.Combine(HuskHook.Data, "plan"), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(run);
            try
            {
                Stopwatch waited = Stopwatch.StartNew();
                Ran found = Start("claudecode", app, BetterModels.ClaudeArgs(), BetterModels.ClaudeQuery, run,
                    delegate { return Brain.Off || waited.ElapsedMilliseconds >= 6000; });
                return BetterModels.Claude(found.Output);
            }
            finally
            {
                try { Directory.Delete(run, false); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        // Runs the app hidden in her folder with their input on stdin. A .cmd from npm runs through cmd.exe, and none of the
        // arguments are their words, so nothing they say reaches a command line.
        private static Ran Start(string kind, string app, List<string> args, string input, string folder, Func<bool> cancelled)
        {
            if (Brain.Off) { Brain.TurnAway(Plans.App(kind)); throw new BrainException(Brain.OffLine, true); }
            ProcessStartInfo start = new ProcessStartInfo();
            string line = Joined(args);
            if (app.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
            {
                start.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
                start.Arguments = "/d /s /c \"" + Quoted(app) + " " + line + "\"";
            }
            else
            {
                start.FileName = app;
                start.Arguments = line;
            }
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.WorkingDirectory = folder;
            start.RedirectStandardInput = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            start.StandardOutputEncoding = Encoding.UTF8;
            start.StandardErrorEncoding = Encoding.UTF8;
            // Their plan, so an API key in their environment doesn't get billed instead, and none of Claude Code's extra traffic.
            start.EnvironmentVariables.Remove(kind == "codex" ? "OPENAI_API_KEY" : "ANTHROPIC_API_KEY");
            start.EnvironmentVariables.Remove("CODEX_API_KEY");
            start.EnvironmentVariables["CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC"] = "1";
            start.EnvironmentVariables["NO_COLOR"] = "1";
            StringBuilder output = new StringBuilder(), errors = new StringBuilder();
            using (Process process = new Process { StartInfo = start })
            {
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) lock (output) if (output.Length < Most) output.Append(e.Data).Append('\n'); };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) lock (errors) if (errors.Length < Most) errors.Append(e.Data).Append('\n'); };
                try { process.Start(); }
                catch (System.ComponentModel.Win32Exception error) { throw new BrainException(Plans.App(kind) + " wouldn't start: " + error.Message, true); }
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                try
                {
                    using (StreamWriter writer = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false))) writer.Write(input);
                }
                catch (IOException) { }
                Stopwatch waited = Stopwatch.StartNew();
                while (!process.WaitForExit(100))
                {
                    bool stop = cancelled != null && cancelled();
                    if (!stop && waited.ElapsedMilliseconds < Wait) continue;
                    End(process);
                    if (stop) throw new WebException("Reply cancelled.", WebExceptionStatus.RequestCanceled);
                    throw new BrainException(Plans.App(kind) + " took more than " + Wait / 1000 + " seconds to answer. Try again, or pick a faster model under BRAIN.", true);
                }
                process.WaitForExit();
                Ran ran = new Ran { Code = process.ExitCode };
                lock (output) ran.Output = output.ToString();
                lock (errors) ran.Errors = errors.ToString();
                return ran;
            }
        }

        // cmd.exe and npm's node both go, not only the first.
        private static void End(Process process)
        {
            try
            {
                using (Process kill = Process.Start(new ProcessStartInfo("taskkill", "/T /F /PID " + process.Id) { UseShellExecute = false, CreateNoWindow = true })) kill.WaitForExit(5000);
            }
            catch (System.ComponentModel.Win32Exception) { }
            catch (InvalidOperationException) { }
            try { if (!process.HasExited) process.Kill(); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }

        internal static string Joined(List<string> args)
        {
            List<string> quoted = new List<string>();
            foreach (string arg in args) quoted.Add(Quoted(arg));
            return string.Join(" ", quoted.ToArray());
        }

        // Windows' own rules for one argument: quotes around it, a quote inside escaped, and backslashes before a quote doubled.
        internal static string Quoted(string arg)
        {
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return arg;
            StringBuilder text = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in arg)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { text.Append('\\', slashes * 2 + 1).Append('"'); slashes = 0; continue; }
                text.Append('\\', slashes).Append(c);
                slashes = 0;
            }
            return text.Append('\\', slashes * 2).Append('"').ToString();
        }
    }

    // xAI keys and catalog, and the model lists Codex and Claude Code keep for their own plans.
    internal static class BetterModels
    {
        internal const int MaxCatalog = 4 * 1024 * 1024;
        internal const string ClaudeQuery = "{\"type\":\"control_request\",\"request_id\":\"capri-models\",\"request\":{\"subtype\":\"initialize\",\"hooks\":{},\"sdkMcpServers\":[]}}\n";
        internal const string CatalogError = "xAI sent an unreadable language model catalog. Try Refresh.";
        internal const string XaiUrl = "https://api.x.ai/v1";

        internal static bool XaiKey(string key)
        {
            return key != null && Regex.IsMatch(key, @"\Axai-[A-Za-z0-9_-]+\z");
        }

        // xAI lists its chat models under language-models; everything else keeps its own path.
        internal static string XaiAddress(string path) { return XaiUrl + (path == "/models" ? "/language-models" : path); }

        internal static string RedactXai(string text)
        {
            return text == null ? null : Regex.Replace(text, @"\bxai-[A-Za-z0-9_-]{20,}", "[redacted]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        internal static bool PlanId(string id)
        {
            return id != null && Regex.IsMatch(id, @"\A[A-Za-z0-9][A-Za-z0-9._\[\]-]{0,127}\z");
        }

        internal static void Add(List<string> models, string id)
        {
            if (PlanId(id) && !models.Contains(id)) models.Add(id);
        }

        // The models a plan's app listed this run, so one of them can be picked though she doesn't name it.
        private static readonly Dictionary<string, HashSet<string>> offered = new Dictionary<string, HashSet<string>>();

        internal static void Remember(string kind, List<string> listed)
        {
            lock (offered)
            {
                HashSet<string> kept = new HashSet<string>(StringComparer.Ordinal);
                foreach (string id in listed) if (PlanId(id)) kept.Add(id);
                offered[kind ?? ""] = kept;
            }
        }

        internal static bool Offered(string kind, string id)
        {
            HashSet<string> kept;
            lock (offered) return id != null && offered.TryGetValue(kind ?? "", out kept) && kept.Contains(id);
        }

        // xAI's catalog in the shape her model list reads: only grok models that take and give text, with what they see.
        internal static string XaiCatalog(string json)
        {
            XaiPage page = Read<XaiPage>(json);
            if (page == null || page.Models == null) throw new FormatException(CatalogError);
            List<XaiModel> found = new List<XaiModel>();
            foreach (XaiModel model in page.Models)
            {
                if (model == null || !PlanId(model.Id) || !model.Id.StartsWith("grok-", StringComparison.Ordinal) ||
                    model.Input == null || Array.IndexOf(model.Input, "text") < 0 ||
                    model.Output == null || Array.IndexOf(model.Output, "text") < 0) continue;
                model.Architecture = new Modalities { Input = model.Input, Output = model.Output };
                found.Add(model);
            }
            using (MemoryStream stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(CloudPage)).WriteObject(stream, new CloudPage { Models = found.ToArray() });
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        internal static List<string> Codex(string path)
        {
            using (FileStream file = File.OpenRead(path))
            {
                if (file.Length > MaxCatalog) return new List<string>();
                using (StreamReader reader = new StreamReader(file, Encoding.UTF8)) return CodexJson(ReadBounded(reader));
            }
        }

        private static string ReadBounded(TextReader reader)
        {
            StringBuilder text = new StringBuilder();
            char[] buffer = new char[8192];
            int count;
            while ((count = reader.Read(buffer, 0, buffer.Length)) != 0)
            {
                if (text.Length + count > MaxCatalog) throw new FormatException("Model catalog is too large.");
                text.Append(buffer, 0, count);
            }
            return text.ToString();
        }

        internal static List<string> CodexJson(string json)
        {
            List<string> found = new List<string>();
            CodexPage page = Read<CodexPage>(json);
            if (page != null && page.Models != null)
                foreach (CodexModel model in page.Models)
                    if (model != null && model.Visibility == "list") Add(found, model.Id);
            return found;
        }

        internal static List<string> Claude(string jsonLines)
        {
            List<string> found = new List<string>();
            if (jsonLines == null || jsonLines.Length > MaxCatalog) return found;
            using (StringReader lines = new StringReader(jsonLines))
            {
                string line;
                while ((line = lines.ReadLine()) != null)
                {
                    if (line.IndexOf("capri-models", StringComparison.Ordinal) < 0) continue;
                    ClaudeFrame frame;
                    try { frame = Read<ClaudeFrame>(line); }
                    catch (FormatException) { continue; }
                    if (frame == null || frame.Type != "control_response" || frame.Reply == null || frame.Reply.Id != "capri-models") continue;
                    if (frame.Reply.Type == "success" && frame.Reply.Payload != null && frame.Reply.Payload.Models != null)
                        foreach (ClaudeModel model in frame.Reply.Payload.Models)
                            if (model != null) Add(found, model.Id);
                    break;
                }
            }
            return found;
        }

        internal static string CodexCache(string profile)
        {
            string home = Environment.GetEnvironmentVariable("CODEX_HOME");
            return Path.Combine(string.IsNullOrWhiteSpace(home) ? Path.Combine(profile, ".codex") : home, "models_cache.json");
        }

        internal static List<string> ClaudeArgs()
        {
            return new List<string> { "-p", "--verbose", "--input-format", "stream-json", "--output-format", "stream-json",
                "--safe-mode", "--tools", "", "--strict-mcp-config", "--mcp-config", "{\"mcpServers\":{}}",
                "--no-session-persistence", "--setting-sources", "project", "--disable-slash-commands" };
        }

        private static T Read<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > MaxCatalog) throw new FormatException("Invalid model catalog.");
            try
            {
                using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
            }
            catch (SerializationException error) { throw new FormatException("Invalid model catalog.", error); }
        }

        [DataContract] private sealed class CloudPage { [DataMember(Name = "data")] public XaiModel[] Models { get; set; } }
        [DataContract] private sealed class XaiPage { [DataMember(Name = "models")] public XaiModel[] Models { get; set; } }
        [DataContract] private sealed class XaiModel
        {
            [DataMember(Name = "id")] public string Id { get; set; }
            [DataMember(Name = "created")] public long Created { get; set; }
            [DataMember(Name = "input_modalities")] public string[] Input { get; set; }
            [DataMember(Name = "output_modalities")] public string[] Output { get; set; }
            [DataMember(Name = "architecture")] public Modalities Architecture { get; set; }
        }
        [DataContract] private sealed class Modalities
        {
            [DataMember(Name = "input_modalities")] public string[] Input;
            [DataMember(Name = "output_modalities")] public string[] Output;
        }
        [DataContract] private sealed class CodexPage { [DataMember(Name = "models")] public CodexModel[] Models { get; set; } }
        [DataContract] private sealed class CodexModel
        {
            [DataMember(Name = "slug")] public string Id { get; set; }
            [DataMember(Name = "visibility")] public string Visibility { get; set; }
        }
        [DataContract] private sealed class ClaudeFrame
        {
            [DataMember(Name = "type")] public string Type { get; set; }
            [DataMember(Name = "response")] public ClaudeReply Reply { get; set; }
        }
        [DataContract] private sealed class ClaudeReply
        {
            [DataMember(Name = "subtype")] public string Type { get; set; }
            [DataMember(Name = "request_id")] public string Id { get; set; }
            [DataMember(Name = "response")] public ClaudePage Payload { get; set; }
        }
        [DataContract] private sealed class ClaudePage { [DataMember(Name = "models")] public ClaudeModel[] Models { get; set; } }
        [DataContract] private sealed class ClaudeModel { [DataMember(Name = "value")] public string Id { get; set; } }
    }
}
