using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CapriDesktop
{
    // Twenty-two everyday asks run on her real brain code, a pretend PC answering every tool, so nothing moves.
    // Scripted, a stand-in Ollama plays each ideal answer to check the checks; live, a local model answers and each
    // result is written out.
    internal static class EverydayJobs
    {
        internal sealed class Call
        {
            internal string Tool;
            internal ToolArguments Args;
            internal string Result;
            internal string Shown { get { return Tool + " " + Words(Args); } }
        }

        internal sealed class Outcome
        {
            internal readonly List<Call> Calls = new List<Call>();
            internal string Reply = "";
            internal string Error;
            internal double Seconds;
            internal string Why;
            internal bool Pass { get { return Why == null; } }
        }

        internal sealed class Job
        {
            internal string Id, Area, Ask, Goal;
            internal Func<Call, string> Answer;
            internal Func<Outcome, string> Check;
            internal string[] Ideal;
            internal Func<string, string> Asked;
            internal string AskFor(string folder) { return Asked == null ? Ask : Asked(folder); }
        }

        private const RegexOptions Loose = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        private const string Apostrophe = "['\u2019]";

        internal static bool Like(string text, string pattern) { return text != null && Regex.IsMatch(text, pattern, Loose); }

        private static bool In(string value, params string[] options) { return value != null && Array.IndexOf(options, value.Trim().ToLowerInvariant()) >= 0; }

        private static string Words(ToolArguments args)
        {
            if (args == null) return "";
            return string.Join(" ", new[] { args.Action, args.Name, args.Site, args.Search, args.Song, args.Task, args.Text, args.Description, args.When, args.Focus, args.Path }).Trim();
        }

        private static string Need(Outcome run, string what, Func<Call, bool> fits)
        {
            if (run.Calls.Exists(call => fits(call))) return null;
            return run.Calls.Count == 0 ? "no tool used; wanted " + what : "wanted " + what + ", used " + string.Join(", ", run.Calls.ConvertAll(call => call.Shown).ToArray());
        }

        private static string Says(Outcome run, string pattern, string what) { return Like(run.Reply, pattern) ? null : "reply never " + what; }

        private const string Denied = @"n" + Apostrophe + @"?t\b|\bnot\b|\bno\b|\bnothing\b|\bnever\b|\bfail|\bwould\b|\bonce\b|\bif\b";

        private static string NoClaim(Outcome run, string pattern, string what)
        {
            foreach (string part in Regex.Split(run.Reply ?? "", @"[.!?;,\n]+|\b(?:but|and|so)\b", Loose))
                if (Like(part, pattern) && !Like(part, Denied)) return "reply claimed " + what;
            return null;
        }

        private static string Both(params string[] reasons)
        {
            foreach (string reason in reasons) if (reason != null) return reason;
            return null;
        }

        private static string Quiet(Outcome run)
        {
            List<Call> acts = run.Calls.FindAll(call => !Hands.Checking(call.Tool, call.Args));
            return acts.Count == 0 ? null : "acted on plain chat: " + string.Join(", ", acts.ConvertAll(call => call.Shown).ToArray());
        }

        private static bool Lower(string level)
        {
            int value;
            return level != null && int.TryParse(level.Trim().TrimEnd('%'), out value) && value < 50;
        }

        internal const string ShoppingList = "Shopping list\n- oat milk\n- 12 eggs\n- sourdough\n- dish soap\n";

        internal static readonly Job[] All =
        {
            new Job
            {
                Id = "pause-music", Area = "music", Ask = "pause the music", Goal = "media pause",
                Check = run => Need(run, "media pause", call => call.Tool == "media" && In(call.Args.Action, "pause", "play_pause")),
                Ideal = new[] { "media {\"action\":\"pause\"}", "Paused." }
            },
            new Job
            {
                Id = "skip-song", Area = "music", Ask = "skip this song it's mid", Goal = "media next",
                Check = run => Need(run, "media next", call => call.Tool == "media" && In(call.Args.Action, "next")),
                Ideal = new[] { "media {\"action\":\"next\"}", "Skipped. Next one better be good." }
            },
            new Job
            {
                Id = "play-song", Area = "music", Ask = "play blinding lights by the weeknd", Goal = "play_music Blinding Lights",
                Check = run => Need(run, "play_music with the song", call => call.Tool == "play_music" && Like(call.Args.Song, @"blinding\s*lights")),
                Ideal = new[] { "play_music {\"song\":\"Blinding Lights The Weeknd\",\"kind\":\"song\"}", "Blinding Lights, coming up." }
            },
            new Job
            {
                Id = "volume-down", Area = "music", Ask = "turn the volume down a bit", Goal = "media volume_down",
                Check = run => Need(run, "a lower volume", call => call.Tool == "media" && (In(call.Args.Action, "volume_down") || In(call.Args.Action, "volume_set") && Lower(call.Args.Level))),
                Ideal = new[] { "media {\"action\":\"volume_down\"}", "Turned it down." }
            },
            new Job
            {
                Id = "open-app", Area = "apps", Ask = "open discord", Goal = "open_app Discord",
                Check = run => Need(run, "open_app Discord", call => call.Tool == "open_app" && Like(call.Args.Name, "discord")),
                Ideal = new[] { "open_app {\"name\":\"Discord\"}", "Discord's open." }
            },
            new Job
            {
                Id = "close-app", Area = "apps", Ask = "close chrome", Goal = "app_window close Chrome",
                Check = run => Need(run, "app_window close Chrome", call => call.Tool == "app_window" && In(call.Args.Action, "close") && Like(call.Args.Name, "chrome")),
                Ideal = new[] { "app_window {\"name\":\"Chrome\",\"action\":\"close\"}", "Closed Chrome." }
            },
            new Job
            {
                Id = "app-missing", Area = "honesty", Ask = "open photoshop", Goal = "tries open_app, then says it isn't installed",
                Answer = call => call.Tool == "open_app" || call.Tool == "check_app" ? "Photoshop isn't installed on this PC, so nothing opened." : null,
                Check = run => Both(
                    Need(run, "open_app or check_app Photoshop", call => (call.Tool == "open_app" || call.Tool == "check_app") && Like(call.Args.Name, "photoshop")),
                    Says(run, "n" + Apostrophe + @"?t|not|no\b|can" + Apostrophe + "?t|couldn" + Apostrophe + "?t|install", "said Photoshop isn't there"),
                    NoClaim(run, @"\b(opened|launched|opening|launching|it" + Apostrophe + @"?s open|is open|up and running)\b", "Photoshop opened")),
                Ideal = new[] { "open_app {\"name\":\"Photoshop\"}", "Photoshop isn't installed on this PC, choom." }
            },
            new Job
            {
                Id = "google-search", Area = "web", Ask = "search google for cheap flights to bali", Goal = "open_website search",
                Check = run => Need(run, "a web search for Bali flights", call => call.Tool == "open_website" && Like(call.Args.Search, "bali") || call.Tool == "use_computer" && Like(call.Args.Task, "bali")),
                Ideal = new[] { "open_website {\"site\":\"google\",\"search\":\"cheap flights to Bali\"}", "Searching for cheap Bali flights." }
            },
            new Job
            {
                Id = "research", Area = "web", Ask = "research the best budget mechanical keyboards and tell me the top 3", Goal = "researches on the web, then names three",
                Answer = call => call.Tool == "use_computer" || call.Tool == "open_website" && !string.IsNullOrEmpty(call.Args.Search)
                    ? "Read three reviews. Top budget picks: Keychron C3 Pro ($35), Royal Kludge RK84 ($55), Redragon K552 ($30)." : null,
                Check = run => Both(
                    Need(run, "a web search or browser job about keyboards", call => call.Tool == "open_website" && Like(call.Args.Search + " " + call.Args.Site, "keyboard") || call.Tool == "use_computer" && Like(call.Args.Task, "keyboard")),
                    Says(run, "keychron|royal kludge|rk84|redragon|k552", "named a keyboard from what it found")),
                Ideal = new[] { "use_computer {\"task\":\"research the best budget mechanical keyboards and tell me the top 3\"}", "Top three: Keychron C3 Pro, Royal Kludge RK84 and Redragon K552." }
            },
            new Job
            {
                Id = "weather", Area = "info", Ask = "do i need an umbrella in melbourne tomorrow", Goal = "weather for Melbourne, says yes",
                Answer = call => call.Tool == "weather" ? "Melbourne, tomorrow: showers, 80% chance of rain, 13 to 18 C." : null,
                Check = run => Both(
                    Need(run, "weather for Melbourne", call => call.Tool == "weather" && Like(call.Args.Name, "melbourne")),
                    Says(run, "yes|yeah|yep|rain|shower|umbrella", "passed on the rain")),
                Ideal = new[] { "weather {\"action\":\"rain\",\"when\":\"tomorrow\",\"name\":\"Melbourne\"}", "Yeah, take one. 80% chance of showers tomorrow." }
            },
            new Job
            {
                Id = "reminder", Area = "reminders", Ask = "remind me to take the bins out at 7pm", Goal = "remind at 7pm about the bins",
                Check = run => Need(run, "remind at 7pm about the bins", call => call.Tool == "remind" && !In(call.Args.Action, "list", "cancel") && Like(call.Args.Text, "bin") && Like(call.Args.When, @"(?<!\d)(7|19)(?!\d)")),
                Ideal = new[] { "remind {\"text\":\"take the bins out\",\"when\":\"at 7pm\"}", "I'll remind you at 7." }
            },
            new Job
            {
                Id = "timer", Area = "reminders", Ask = "set a timer for 10 minutes", Goal = "remind in 10 minutes",
                Check = run => Need(run, "a 10 minute reminder", call => call.Tool == "remind" && !In(call.Args.Action, "list", "cancel") && Like(call.Args.When, @"(?<!\d)10(?!\d)|\bten\b")),
                Ideal = new[] { "remind {\"text\":\"timer's up\",\"when\":\"in 10 minutes\"}", "Ten minutes, starting now." }
            },
            new Job
            {
                Id = "remember", Area = "notes", Ask = "remember that my sister's birthday is march 3rd", Goal = "remember the birthday",
                Check = run => Need(run, "remember with the date", call => call.Tool == "remember" && !In(call.Args.Action, "forget") && Like(call.Args.Text, @"march|3(rd)?\b|03")),
                Ideal = new[] { "remember {\"text\":\"Their sister's birthday is March 3rd\"}", "Noted. March 3rd." }
            },
            new Job
            {
                Id = "write-note", Area = "notes", Ask = "write a note in notepad that says call the dentist on monday", Goal = "a Notepad job with the note",
                Answer = call => call.Tool == "use_computer" ? "Opened Notepad and typed: call the dentist on Monday." : null,
                Check = run => Need(run, "a Notepad job with the words", call => call.Tool == "use_computer" && Like(call.Args.Task, "dentist")),
                Ideal = new[] { "use_computer {\"task\":\"write a note in notepad that says call the dentist on monday\"}", "Written in Notepad." }
            },
            new Job
            {
                Id = "find-file", Area = "files", Ask = "find my resume", Goal = "find_file resume, gives the path",
                Answer = call => call.Tool == "find_file" ? "1. C:\\Users\\sam\\Documents\\Jobs\\Sam Resume 2026.pdf\n2. C:\\Users\\sam\\Downloads\\resume-old.docx" : null,
                Check = run => Both(
                    Need(run, "find_file for a resume", call => call.Tool == "find_file" && Like(call.Args.Description, "resum|cv")),
                    Says(run, @"documents|jobs|resume 2026|\.pdf", "said where it is")),
                Ideal = new[] { "find_file {\"description\":\"resume\"}", "Found it: Documents\\Jobs\\Sam Resume 2026.pdf." }
            },
            new Job
            {
                Id = "read-file", Area = "files", Goal = "read_file, then says what's on the list",
                Ask = "what's on my shopping list? it's at <path>",
                Asked = folder => "what's on my shopping list? it's at " + Path.Combine(folder, "shopping-list.txt"),
                Check = run => Says(run, "oat milk|sourdough|dish soap", "said what's on it"),
                Ideal = new[] { "read_file {\"path\":\"@list\"}", "Oat milk, 12 eggs, sourdough and dish soap." }
            },
            new Job
            {
                Id = "screen-error", Area = "screen", Ask = "what does this error on my screen mean", Goal = "look_at_screen, then explains it",
                Answer = call => call.Tool == "look_at_screen" ? "VS Code with a terminal. Red text: ModuleNotFoundError: No module named 'requests' in main.py line 1." : null,
                Check = run => Both(
                    Need(run, "look_at_screen", call => call.Tool == "look_at_screen"),
                    Says(run, "requests|pip|module", "explained the error it saw")),
                Ideal = new[] { "look_at_screen {\"focus\":\"the error\"}", "Python can't find the requests module. Run pip install requests." }
            },
            new Job
            {
                Id = "message", Area = "messages", Ask = "message jake on discord that i'm running 10 minutes late", Goal = "a Discord job with the message",
                Answer = call => call.Tool == "use_computer" ? "Sent Jake \"running 10 minutes late\" in Discord." : null,
                Check = run => Need(run, "a Discord job for Jake", call => call.Tool == "use_computer" && Like(call.Args.Task, "jake") && Like(call.Args.Task, "late")),
                Ideal = new[] { "use_computer {\"task\":\"message jake on discord that i'm running 10 minutes late\"}", "Told Jake." }
            },
            new Job
            {
                Id = "pc-slow", Area = "pc", Ask = "why is my pc so slow right now", Goal = "checks the PC, names the hog",
                Answer = call => call.Tool == "pc_status" ? "CPU 97% busy. Memory 14.9 of 16 GB used. Top: chrome.exe 61% CPU and 6.2 GB, Discord 9% CPU." :
                    call.Tool == "list_windows" ? "Chrome (38 tabs, in front), Discord, Spotify." : null,
                Check = run => Both(
                    Need(run, "a PC check", call => call.Tool == "pc_status" || call.Tool == "list_windows"),
                    Says(run, "chrome", "named Chrome")),
                Ideal = new[] { "pc_status {}", "Chrome's eating 61% of your CPU and 6 GB of memory. Close some tabs." }
            },
            new Job
            {
                Id = "internet", Area = "pc", Ask = "is my internet down", Goal = "internet_status, says it's fine",
                Answer = call => call.Tool == "internet_status" ? "Wi-Fi connected. 4 of 4 pings to 1.1.1.1 came back, 18 ms average. Web addresses look up fine." : null,
                Check = run => Both(
                    Need(run, "internet_status", call => call.Tool == "internet_status"),
                    Says(run, "fine|working|up|ok|good|18 ?ms|connected", "said it's working")),
                Ideal = new[] { "internet_status {}", "Nah, it's fine. 18 ms, no packets lost." }
            },
            new Job
            {
                Id = "lock-pc", Area = "pc", Ask = "lock my pc", Goal = "power lock",
                Check = run => Need(run, "power lock", call => call.Tool == "power" && In(call.Args.Action, "lock")),
                Ideal = new[] { "power {\"action\":\"lock\"}", "Locked." }
            },
            new Job
            {
                Id = "just-chat", Area = "honesty", Ask = "ugh i'm so tired today", Goal = "just talks, no tools",
                Check = run => Both(Quiet(run), run.Reply.Trim().Length == 0 ? "said nothing" : null),
                Ideal = new[] { "Rough one? Grab some water and take it slow." }
            }
        };

        // The pretend PC: every call is written down and answered the way her real tools answer, and nothing runs.
        private static string Act(Job job, Outcome run, string tool, ToolArguments args)
        {
            Call call = new Call { Tool = tool, Args = args ?? new ToolArguments() };
            string result = job.Answer == null ? null : job.Answer(call);
            call.Result = result ?? Usual(call.Tool, call.Args);
            lock (run.Calls) run.Calls.Add(call);
            return call.Result;
        }

        private static string Usual(string tool, ToolArguments args)
        {
            switch (tool)
            {
                case "open_app": return "Opened " + (args.Name ?? "it") + ".";
                case "open_website": return string.IsNullOrWhiteSpace(args.Search) ? "Opened " + (args.Site ?? "the site") + " in their browser." : "Opened a " + (args.Site ?? "Google") + " search for \"" + args.Search + "\" in their browser.";
                case "play_music": return "Playing " + (args.Song ?? "it") + " on " + (args.App ?? "Spotify") + ".";
                case "app_window": return args.Action == "close" ? "Closed " + (args.Name ?? "it") + "." : "Done: " + (args.Action ?? "switch_to") + " " + (args.Name ?? "") + ".";
                case "remind": return args.Action == "list" ? "No reminders saved." : args.Action == "cancel" ? "Cancelled it." : "Reminder saved for " + (args.When ?? "later") + ": " + (args.Text ?? "") + ".";
                case "remember": return args.Action == "forget" ? "Forgot it." : "Saved to your notes.";
                case "power": return args.Action == "lock" ? "Locked their PC." : "Done: " + args.Action + ".";
                case "use_computer": return "Done: " + (args.Task ?? "the job") + ".";
                case "list_windows": return "Open: Chrome (in front), Discord, Spotify, File Explorer.";
                case "check_app": return (args.Name ?? "It") + " is installed.";
                case "pc_status": return "CPU 12% busy. Memory 7.1 of 16 GB used.";
                case "internet_status": return "Ethernet connected. 4 of 4 pings came back, 12 ms average.";
                case "weather": return "Clear, 22 C, 5% chance of rain.";
                case "find_file": return "No files matched.";
                case "read_file": return args.Path != null && File.Exists(args.Path) ? File.ReadAllText(args.Path) : "No file there.";
                case "look_at_screen": return "Their desktop, with Chrome open on YouTube.";
                case "media":
                    switch (args.Action)
                    {
                        case "pause": case "play_pause": return "Paused.";
                        case "resume": return "Playing again.";
                        case "next": return "Skipped to the next song: Espresso by Sabrina Carpenter.";
                        case "previous": return "Back to the last song.";
                        case "volume_down": return "Volume down to 40%.";
                        case "volume_up": return "Volume up to 60%.";
                        case "volume_set": return "Volume set to " + (args.Level ?? "50") + "%.";
                        case "now_playing": return "Now playing: Espresso by Sabrina Carpenter on Spotify.";
                        default: return "Done.";
                    }
                default: return "Done.";
            }
        }

        // A stand-in Ollama for the scripted brain: asked with her tools the first time, it calls the job's ideal tools;
        // once they have run, it says the job's ideal reply.
        private static string Scripted(Job job, string list, out TcpListener listener)
        {
            TcpListener server = new TcpListener(IPAddress.Loopback, 0);
            server.Start();
            listener = server;
            bool acted = false;
            new Thread((ThreadStart)delegate
            {
                try
                {
                    while (true)
                        using (TcpClient client = server.AcceptTcpClient())
                        using (NetworkStream stream = client.GetStream())
                        {
                            MemoryStream got = new MemoryStream();
                            byte[] buffer = new byte[8192];
                            string text = "";
                            while (true)
                            {
                                int read = stream.Read(buffer, 0, buffer.Length);
                                if (read <= 0) break;
                                got.Write(buffer, 0, read);
                                text = Encoding.UTF8.GetString(got.ToArray());
                                int end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                                if (end < 0) continue;
                                Match length = Regex.Match(text, @"Content-Length:\s*(\d+)", RegexOptions.IgnoreCase);
                                if (got.Length >= end + 4 + (length.Success ? int.Parse(length.Groups[1].Value) : 0)) break;
                            }
                            string answer;
                            if (text.StartsWith("GET /api/tags", StringComparison.Ordinal)) answer = "{\"models\":[{\"name\":\"scripted\",\"model\":\"scripted\"}]}";
                            else if (text.Contains("/api/show")) answer = "{\"capabilities\":[\"completion\",\"tools\",\"vision\"]}";
                            else
                            {
                                string[] ideal = job.Ideal;
                                StringBuilder calls = new StringBuilder();
                                // Asked without her tools before the job is done, it says "act", as a model does when it
                                // reads a message as a job, so her brain goes on to ask with her tools.
                                string content = !acted && ideal.Length > 1 && !text.Contains("\"tools\"") ? "act" : ideal[ideal.Length - 1];
                                if (!acted && ideal.Length > 1 && text.Contains("\"tools\""))
                                {
                                    acted = true;
                                    for (int i = 0; i < ideal.Length - 1; i++)
                                    {
                                        int space = ideal[i].IndexOf(' ');
                                        string json = ideal[i].Substring(space + 1).Replace("@list", list.Replace("\\", "\\\\"));
                                        calls.Append(calls.Length == 0 ? "" : ",").Append("{\"function\":{\"name\":\"" + ideal[i].Substring(0, space) + "\",\"arguments\":" + json + "}}");
                                    }
                                }
                                answer = "{\"model\":\"scripted\",\"message\":{\"role\":\"assistant\",\"content\":" + (calls.Length > 0 ? "\"\"" : Voice.Quote(content))
                                    + (calls.Length > 0 ? ",\"tool_calls\":[" + calls + "]" : "") + "},\"done\":true,\"done_reason\":\"stop\"}";
                            }
                            byte[] body = Encoding.UTF8.GetBytes(answer);
                            byte[] head = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
                            stream.Write(head, 0, head.Length);
                            stream.Write(body, 0, body.Length);
                        }
                }
                catch (SocketException) { }
                catch (ObjectDisposedException) { }
                catch (IOException) { }
            }) { IsBackground = true, Name = "Everyday jobs stand-in" }.Start();
            return "http://127.0.0.1:" + ((IPEndPoint)server.LocalEndpoint).Port;
        }

        // One job: a fresh brain in its own folder, her tools answered by the pretend PC. A model of null plays the job's ideal.
        internal static Outcome Run(Job job, string folder, string model)
        {
            Directory.CreateDirectory(folder);
            string list = Path.Combine(folder, "shopping-list.txt");
            File.WriteAllText(list, ShoppingList);
            Outcome run = new Outcome();
            string memoryWas = Brain.MemoryPath, memoriesWas = Brain.MemoriesPath, personalityWas = Brain.PersonalityPath, transcriptsWas = Brain.TranscriptPath, logWas = Brain.LogPath, ollamaWas = Brain.Ollama;
            bool keepingWas = Brain.Keeping;
            TcpListener stand = null;
            Stopwatch clock = Stopwatch.StartNew();
            try
            {
                Brain.MemoryPath = Path.Combine(folder, "me.md");
                Brain.MemoriesPath = Path.Combine(folder, "memories.md");
                Brain.PersonalityPath = Path.Combine(folder, Brain.PersonalityName);
                Brain.TranscriptPath = Path.Combine(folder, "transcripts");
                Brain.LogPath = Path.Combine(folder, "errors.txt");
                Brain.Keeping = false;
                Brain brain = new Brain { OwnSteps = true, CloudWait = 180000 };
                brain.Act = delegate(string tool, ToolArguments args) { return Act(job, run, tool, args); };
                brain.Remote = null;
                if (model == null) Brain.Ollama = Scripted(job, list, out stand);
                brain.Model = model ?? "scripted";
                run.Reply = brain.Ask(job.AskFor(folder)) ?? "";
            }
            catch (BrainException error) { run.Error = error.Message; }
            catch (Exception error) { run.Error = error.GetType().Name + ": " + error.Message; }
            finally
            {
                Brain.MemoryPath = memoryWas; Brain.MemoriesPath = memoriesWas; Brain.PersonalityPath = personalityWas;
                Brain.TranscriptPath = transcriptsWas; Brain.LogPath = logWas; Brain.Ollama = ollamaWas; Brain.Keeping = keepingWas;
                if (stand != null) stand.Stop();
            }
            run.Seconds = clock.Elapsed.TotalSeconds;
            run.Why = run.Error != null ? "error: " + run.Error : job.Check(run);
            return run;
        }

        private static string Flat(string text)
        {
            text = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            return text.Length > 220 ? text.Substring(0, 219) + "..." : text;
        }

        // Every job against one brain, a line each, with a table written to the folder.
        internal static int RunAll(List<string> lines, string folder, string model)
        {
            int passed = 0;
            StringBuilder table = new StringBuilder("| Job | Area | Result | Seconds | Tools | Why |\n|---|---|---|---|---|---|\n");
            foreach (Job job in All)
            {
                Outcome run = Run(job, Path.Combine(folder, job.Id), model);
                if (run.Pass) passed++;
                string tools = run.Calls.Count == 0 ? "no tools" : string.Join(", ", run.Calls.ConvertAll(call => call.Shown).ToArray());
                lines.Add((run.Pass ? "PASS" : "MISS") + " " + job.Id + " " + run.Seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s: " + tools + (run.Pass ? "" : " :: " + run.Why + " :: " + Flat(run.Error ?? run.Reply)));
                table.Append("| " + job.Id + " | " + job.Area + " | " + (run.Pass ? "pass" : "miss") + " | " + run.Seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " | " + tools.Replace("|", "/") + " | " + (run.Why ?? "").Replace("|", "/") + " |\n");
            }
            File.WriteAllText(Path.Combine(folder, "table.md"), "# Everyday jobs on " + (model ?? "the scripted brain") + ": " + passed + " of " + All.Length + "\n\n" + table);
            return passed;
        }

        // The scripted brain plays each job's ideal: every job must pass, which proves the jobs, their checks and that
        // her brain reads tool calls and replies through the stand-in the way it does from a real model.
        internal static void Check(List<string> checks, string folder)
        {
            List<string> lines = new List<string>();
            string room = Path.Combine(folder, "everyday-scripted");
            int passed = RunAll(lines, room, null);
            if (passed != All.Length) throw new Exception("Everyday jobs on the scripted brain: " + passed + " of " + All.Length + ". " + string.Join(" / ", lines.FindAll(line => line.StartsWith("MISS", StringComparison.Ordinal)).ToArray()));
            checks.Add("PASS everyday jobs on the scripted brain: all " + All.Length + " right through her real brain code, every tool answered by the pretend PC and none run");
        }

        // Her two other ways of only saying a job: the ask said back, or a tool written out as text, and "under way" words.
        internal static void PromiseCheck(List<string> checks)
        {
            bool echoed = Hands.Echoed("pause the music", "pause the music") && Hands.Echoed("media: pause", "pause the music") && Hands.Echoed("media({\"action\":\"pause\"})", "pause the music")
                && !Hands.Echoed("Paused. Enjoy the quiet.", "pause the music") && !Hands.Echoed("music", "pause the music");
            bool underway = Hands.Underway("Hold on, I'm checking your PC.") && Hands.Underway("Timer's set, ten minutes.") && Hands.Underway("Let me take a look at that error.")
                && !Hands.Underway("Your PC looks fine.") && !Hands.Underway("Ten minutes is up.");
            bool handed = Hands.Handed("act") && Hands.Handed("You think I'd lose something that important, sunshine. act") && Hands.Handed("On it!\nact")
                && !Hands.Handed("Get your act together.") && !Hands.Handed("That was quite an act") && !Hands.Handed("Nice act, choom.");
            if (!echoed || !underway || !handed) throw new Exception("A reply that only says a job was read wrong: said back " + echoed + ", under way " + underway + ", act at the end " + handed);
            checks.Add("PASS a reply that only says the ask back, writes a tool out as text, or says a job is under way counts as a job not done, and a lone act after its last sentence is read as act");
        }
    }
}
