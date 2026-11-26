using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

#if CHECKS
namespace CapriDesktop
{
    internal partial class Companion
    {
        // Her brain's thinking and written-out tool calls never reach her chat in any shape models write them, while
        // honest replies about code, JSON, XML or a plain < keep every word (Lachy). Stand-in brains only.
        private static void SmokeLeaks(List<string> checks)
        {
            string[,] shapes =
            {
                { "<think>The user said hi. I should greet them.</think>Hey choom.", "Hey choom." },
                { "The user said hi. I should greet them.\n</think>\n\nHey choom.", "Hey choom." },
                { "<thinking>Plan a greeting.</thinking>Hey choom.", "Hey choom." },
                { "<reasoning>Plan a greeting.</reasoning>Hey choom.", "Hey choom." },
                { "<thought>Plan a greeting.</thought>Hey choom.", "Hey choom." },
                { "<seed:think>Plan.</seed:think>Hey choom.", "Hey choom." },
                { "[THINK]Plan a greeting.[/THINK]Hey choom.", "Hey choom." },
                { "◁think▷Plan a greeting.◁/think▷Hey choom.", "Hey choom." },
                { "<|begin_of_thought|>Plan.<|end_of_thought|><|begin_of_solution|>Hey choom.<|end_of_solution|>", "Hey choom." },
                { "<think>Plan.</think><answer>Hey choom.</answer>", "Hey choom." },
                { "<think>Still going and it never closes", "" },
                { "Hey choom.<thi", "Hey choom." },
                { "<|channel|>analysis<|message|>User says hi. Greet back.<|end|><|start|>assistant<|channel|>final<|message|>Hey choom.<|return|>", "Hey choom." },
                { "analysisUser says hi. Greet back.assistantfinalHey choom.", "Hey choom." },
                { "<|channel>thought\nPlan.<channel|>Hey choom.", "Hey choom." },
                { "Hey choom!<|im_end|>", "Hey choom!" },
                { "Hey choom!<|eot_id|>", "Hey choom!" },
                { "On it.<tool_call>\n{\"name\": \"open_app\", \"arguments\": {\"name\": \"Steam\"}}\n</tool_call>", "On it." },
                { "On it.<tool_call>{\"name\": \"open_app\", \"arguments\": {\"name\": \"Ste", "On it." },
                { "<function=open_app>{\"name\": \"Steam\"}</function>", "" },
                { "<function=open_app>\n<parameter=name>\nSteam\n</parameter>\n</function>", "" },
                { "<function_calls><invoke name=\"open_app\"><parameter name=\"name\">Steam</parameter></invoke></function_calls>", "" },
                { "[TOOL_CALLS][{\"name\": \"open_app\", \"arguments\": {\"name\": \"Steam\"}}]", "" },
                { "<|python_tag|>{\"name\": \"open_app\", \"parameters\": {\"name\": \"Steam\"}}<|eom_id|>", "" },
                { "```tool_code\nprint(default_api.open_app(name=\"Steam\"))\n```", "" },
                { "Sure.\n```json\n{\"name\": \"open_app\", \"arguments\": {\"name\": \"Steam\"}}\n```", "Sure." },
                { "{\"name\": \"open_app\", \"arguments\": {\"name\": \"Steam\"}}", "" },
                { "{\"type\": \"function\", \"function\": {\"name\": \"open_app\", \"arguments\": \"{\\\"name\\\": \\\"Steam\\\"}\"}}", "" },
                { "<|tool_calls_section_begin|><|tool_call_begin|>functions.open_app:0<|tool_call_argument_begin|>{\"name\": \"Steam\"}<|tool_call_end|><|tool_calls_section_end|>", "" },
                { "functions.open_app:0 {\"name\": \"Steam\"}", "" },
                { "<｜tool▁calls▁begin｜><｜tool▁call▁begin｜>function<｜tool▁sep｜>open_app\n```json\n{\"name\": \"Steam\"}\n```<｜tool▁call▁end｜><｜tool▁calls▁end｜>", "" },
                { "Opening it. open_app{\"name\": \"Steam\"}", "Opening it." },
                { "call:open_app{name:<|\"|>Steam<|\"|>}", "" },
                { "use_computer: open notepad", "" },
            };
            for (int i = 0; i < shapes.GetLength(0); i++)
            {
                string shown = Brain.Clean(shapes[i, 0]);
                if (shown != shapes[i, 1]) throw new Exception("A leak reached her chat: " + Voice.Quote(shapes[i, 0]) + " showed " + Voice.Quote(shown));
            }
            // Each written call to her tools is read back as the real call, in every shape above that has one.
            string[] callers =
            {
                "<tool_call>\n{\"name\": \"open_app\", \"arguments\": {\"name\": \"Steam\"}}\n</tool_call>",
                "<function=open_app>{\"name\": \"Steam\"}</function>",
                "<function=open_app>\n<parameter=name>\nSteam\n</parameter>\n</function>",
                "<function_calls><invoke name=\"open_app\"><parameter name=\"name\">Steam</parameter></invoke></function_calls>",
                "[TOOL_CALLS][{\"name\": \"open_app\", \"arguments\": {\"name\": \"Steam\"}}]",
                "<|python_tag|>{\"name\": \"open_app\", \"parameters\": {\"name\": \"Steam\"}}<|eom_id|>",
                "```tool_code\nprint(default_api.open_app(name=\"Steam\"))\n```",
                "```json\n{\"name\": \"open_app\", \"arguments\": {\"name\": \"Steam\"}}\n```",
                "{\"type\": \"function\", \"function\": {\"name\": \"open_app\", \"arguments\": \"{\\\"name\\\": \\\"Steam\\\"}\"}}",
                "<|tool_calls_section_begin|><|tool_call_begin|>functions.open_app:0<|tool_call_argument_begin|>{\"name\": \"Steam\"}<|tool_call_end|><|tool_calls_section_end|>",
                "<｜tool▁calls▁begin｜><｜tool▁call▁begin｜>function<｜tool▁sep｜>open_app\n```json\n{\"name\": \"Steam\"}\n```<｜tool▁call▁end｜><｜tool▁calls▁end｜>",
                "call:open_app{name:<|\"|>Steam<|\"|>}",
            };
            foreach (string written in callers)
            {
                List<KeyValuePair<string, ToolArguments>> read = Leaks.Calls(written);
                if (read.Count != 1 || read[0].Key != "open_app" || read[0].Value == null || read[0].Value.Name != "Steam") throw new Exception("A call written as text wasn't read as the real call: " + Voice.Quote(written) + " read " + read.Count);
            }
            if (Leaks.Calls("<think>open_app{\"name\": \"Steam\"}</think>Hi.").Count != 0) throw new Exception("A call in its thinking was read as one to run.");
            // Her honest replies keep every word: code, JSON, XML, a <think> tag she's explaining, a plain < and words like "remember:".
            string[] honest =
            {
                "A person in JSON looks like {\"name\": \"Alex\", \"age\": 30}.",
                "Wrap it in `<think>` tags and close it with `</think>`.",
                "2 < 3 and 5 > 4, always.",
                "I <3 you, choom.",
                "Remember: you've got this.",
                "The user manual says to hold the power button.",
                "I could draw {a star} for you.",
                "I'll listen (\"Hey Jude\" is great) later.",
                "Let me think... Cyberpunk, obviously.",
                "Use print(\"hi\") to test it.",
                "It's <b>bold</b> in HTML.",
                // Answers that talk about "the user" without a brain talking to itself: a sign-up page, an error, a study, a game.
                "The user wants to see a clear error when their password is too short, so show it right under the field.",
                "The user is not authorized means your token is missing or expired. Log in again and it should work.",
                "The user is asked to rate each screen from 1 to 5.\n\nThen you compare the scores across the two versions.",
                "The user has three lives and loses one each time they fall.\n\nWhen all three are gone, show the game over screen.",
                "The user said they love jazz. So here's a playlist for you: Kind of Blue, A Love Supreme.",
                "Okay so the user wants a login button? Put it top right, that's where people look.",
                "Analysis: your loop never ends because i never changes.",
                "Reasoning: cheaper, faster, and it's already installed.",
                "The right tool to use here is a torque wrench, not pliers.",
                "Looking at the available tools in Blender, the knife tool is your best bet.",
                "I'll use the brush tool for the sky, then the pen tool for the outlines.",
                // Tags mentioned inside a sentence.
                "Put your steps inside <reasoning> and </reasoning> tags, then the answer after.",
                "Gemini sometimes writes a <thought> block before the answer, so strip it.",
                "Close it with </think> and the model stops thinking.",
                "To ask for it, write [THINK] at the start and [/THINK] at the end.",
                "In HTML you strike text out with <s>this</s>",
                "In a table, each header cell is a <th",
                // Code that looks like her tools: numpy's clip, CSS classes, one-word functions, a sentence after a tool's name.
                "np.clip(a=arr, a_min=0, a_max=1) keeps every value between 0 and 1.",
                "Add .clip { overflow: hidden; } to your stylesheet.",
                "Add .media { display: flex; } and it lines up.",
                "Your component: video-clip {display: block;}",
                "In Python, power(base=2, exp=10) is 1024.",
                "stream(url=\"https://x.y\") opens the feed.",
                "Use remember(\"milk\") in your script.",
                "open_app: that's the tool I use to open your apps.",
                "pc_status: CPU 12%, RAM 40%.",
            };
            foreach (string line in honest)
                if (Brain.Clean(line) != System.Text.RegularExpressions.Regex.Replace(line, @"\s+", " ") || Leaks.Unmused(line) != line || Leaks.Kinds(line).Count != 0)
                    throw new Exception("An honest reply lost words: " + Voice.Quote(line) + " became " + Voice.Quote(Brain.Clean(line)));
            string python = "Here's how:\n```python\nx = np.clip(a=x, a_min=0, a_max=1)\n```";
            if (Leaks.Taken(python).Count != 0 || Leaks.Taken("Add .clip { overflow: hidden; } to your stylesheet.").Count != 0) throw new Exception("Code in an honest reply was taken for one of her tools and would run.");
            if (Leaks.Taken("On it. open_app{\"name\": \"Steam\"}").Count != 1) throw new Exception("A call taken out of her words didn't run.");
            // Replies that are long or built to be slow never hold her up.
            string[] slow =
            {
                new string('{', 20000),
                string.Concat(System.Linq.Enumerable.Repeat("{}", 10000)),
                string.Concat(System.Linq.Enumerable.Repeat("a {b} ", 4000)),
                string.Concat(System.Linq.Enumerable.Repeat("Here's an honest paragraph about code, with a {brace} and a (paren) and \"quotes\". ", 180)),
                "open_app(a=" + new string('b', 20000),
                "open_app(a=" + string.Concat(System.Linq.Enumerable.Repeat("bbb=", 5000)),
                string.Concat(System.Linq.Enumerable.Repeat("open_app(a=b ", 1500)),
                "open_app(a=\"" + new string('x', 20000),
                string.Concat(System.Linq.Enumerable.Repeat("\"{", 10000)),
                string.Concat(System.Linq.Enumerable.Repeat("<thi", 5000)),
                string.Concat(System.Linq.Enumerable.Repeat("<think> ", 2500)),
                string.Concat(System.Linq.Enumerable.Repeat("</think> x ", 2000)),
                string.Concat(System.Linq.Enumerable.Repeat("[TOOL_CALLS] ", 1500)),
                string.Concat(System.Linq.Enumerable.Repeat("[TOOL_CALLS][ ", 1300)),
                string.Concat(System.Linq.Enumerable.Repeat("<function=open_app> ", 1000)),
                string.Concat(System.Linq.Enumerable.Repeat("<invoke name=\"open_app\"> ", 800)),
                string.Concat(System.Linq.Enumerable.Repeat("```\nx\n", 3000)),
                string.Concat(System.Linq.Enumerable.Repeat("The user is asking me to do it. \n\n", 1000)),
                string.Concat(System.Linq.Enumerable.Repeat("{\"name\": \"open_app\", \"x\": ", 800)),
                string.Concat(System.Linq.Enumerable.Repeat("print(default_api.open_app(name=\"", 800)),
                string.Concat(System.Linq.Enumerable.Repeat("call:open_app{name:", 1500)),
            };
            Brain.Clean("warm up");
            Leaks.Taken("warm up open_app{\"name\": \"Steam\"}");
            long slowest = 0;
            foreach (string reply in slow)
            {
                System.Diagnostics.Stopwatch took = System.Diagnostics.Stopwatch.StartNew();
                Brain.Clean(reply);
                Leaks.Unmused(reply);
                Leaks.Mused(reply);
                Leaks.Kinds(reply);
                Leaks.Taken(reply);
                slowest = Math.Max(slowest, took.ElapsedMilliseconds);
                if (took.ElapsedMilliseconds > 1500) throw new Exception("A hostile reply took " + took.ElapsedMilliseconds + " ms to clean: " + Voice.Quote(reply.Substring(0, 40)) + "...");
            }
            string fenced = "Like this:\n```xml\n<think>hi</think>\n<tool_call>{\"name\": \"x\"}</tool_call>\n```";
            if (Brain.Visible(fenced) != fenced || Brain.Clean(fenced).IndexOf("<think>hi</think>", StringComparison.Ordinal) < 0 || Brain.Clean(fenced).IndexOf("<tool_call>", StringComparison.Ordinal) < 0) throw new Exception("Code about tags lost its tags: " + Brain.Clean(fenced));
            string json = "Sure:\n```json\n{\"name\": \"Alex\", \"arguments\": {\"age\": 30}}\n```";
            if (Brain.Clean(json).IndexOf("\"Alex\"", StringComparison.Ordinal) < 0 || Leaks.Calls(json).Count != 0) throw new Exception("A JSON example that isn't one of her tools was taken for a call.");
            if (!Leaks.Mused("The user is asking me to close discord. Looking at the tools, the correct tool call would be app_window with name: \"discord\", action:")) throw new Exception("Thinking in plain words reached her chat.");
            if (!Leaks.Mused("Okay, let's break this down. The user is asking me to play some music. Looking at the tools, the correct tool call would be play_music with app: \"spotify\", kind:")) throw new Exception("Thinking in plain words reached her chat.");
            if (Leaks.Unmused("The user greets me. I'll greet them back.\n\nHey choom, what's up?") != "Hey choom, what's up?") throw new Exception("Her answer after thinking in plain words was lost.");
            if (Leaks.Unmused("The user wants a joke. I’ll tell one.\n\nLooking at my tools, none fit.\n\nWhy did the netrunner cross the road?") != "Why did the netrunner cross the road?") throw new Exception("Thinking in plain words over several paragraphs reached her chat.");
            if (Leaks.Unmused("Thinking: they said hi, keep it short.\n\nAnswer: Hey choom!") != "Hey choom!") throw new Exception("Labelled thinking reached her chat.");
            checks.Add("PASS leak shapes: " + shapes.GetLength(0) + " kinds of thinking, template tokens and written tool calls never show, " + callers.Length + " written call shapes read back as the real call, a call inside its thinking never runs, " + (honest.Length + 2) + " honest replies about code, JSON, XML, tags, a plain <, \"the user\" and her tools' names keep every word, code that looks like her tools never runs, and " + slow.Length + " hostile 20,000 character replies clean in " + slowest + " ms at most");

            string openAiWas = Brain.Nvidia, ollamaWas = Brain.Ollama, notesWas = Brain.MemoryPath, memoriesWas = Brain.MemoriesPath, transcriptsWas = Brain.TranscriptPath, logWas = Brain.LogPath;
            bool keepingWas = Brain.Keeping;
            Func<string> gameWas = Hands.GameOpen;
            string log = Path.Combine(Path.GetTempPath(), "capri-leaks-" + Guid.NewGuid().ToString("N") + ".txt");
            TcpListener fake = null;
            try
            {
                Brain.MemoryPath = Brain.MemoriesPath = Brain.TranscriptPath = null;
                Brain.LogPath = log;
                Brain.Keeping = false;
                Hands.GameOpen = delegate { return null; };
                Func<string, string> says = delegate(string text) { return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":" + Voice.Quote(text) + "},\"finish_reason\":\"stop\"}]}"; };
                List<string> asked = new List<string>();
                Brain.Nvidia = FakeServer(new[]
                {
                    says("The user says hey. I should greet them warmly.\n</think>\n\nHey choom!"),
                    says("Let's fix that.\n<tool_call>\n{\"name\": \"open_app\", \"arguments\": {\"name\": \"Steam\"}}\n</tool_call>"),
                    says("<think>The user says hello again. Greet them.</think>"),
                    says("Hello again."),
                    says("<think>Thinking only.</think>"),
                    says("<think>Still only thinking.</think>"),
                    says("Okay, the user wants a joke. I should tell one about netrunners."),
                    says("Why did the netrunner cross the road? To jack in on the other side."),
                    says("Sure:\n```json\n{\"name\": \"Alex\", \"age\": 30}\n```\nAnd 2 < 3."),
                    says("The user wants to see a clear error when their password is too short, so show it right under the field."),
                    says("The user is asking me to pick. I should pick the blue one."),
                    says("The user is asking me to pick. I should pick the blue one, it's calmer."),
                    says("Here's how:\n```python\nx = np.clip(a=x, a_min=0, a_max=1)\n```"),
                    says("<tool_call>\n{\"name\": \"open_app\", \"arguments\": {\"name\": \"Steam\"}}\n</tool_call>"),
                    says("Opened it for you."),
                }, asked, out fake);
                List<string> did = new List<string>();
                Brain brain = new Brain { Model = "qwen/qwen3-next-80b-a3b-thinking", Remote = new Cloud("nvapi-test", "qwen/qwen3-next-80b-a3b-thinking") };
                brain.Act = delegate(string tool, ToolArguments args) { did.Add(tool + ":" + (args == null ? "" : args.Name)); return tool == "open_app" ? "Opened " + args.Name + "." : "No result."; };
                string greeted = brain.Ask("hey");
                if (greeted != "Hey choom!") throw new Exception("Thinking with no opening tag reached her chat: " + greeted);
                List<ChatMessage> history = brain.History;
                if (history.Count != 2 || history[1].Content != "Hey choom!") throw new Exception("Her history kept the thinking: " + (history.Count > 1 ? history[1].Content : "nothing"));
                string bored = brain.Ask("honestly i'm kinda bored rn");
                if (did.Count != 0 || bored != "Let's fix that.") throw new Exception("A call written as text in her chat ran, or her words around it were lost: " + bored + " / " + string.Join(",", did.ToArray()));
                string again = brain.Ask("hello again");
                if (again != "Hello again.") throw new Exception("A reply that was all thinking wasn't asked again for her answer: " + again);
                string[] sent;
                lock (asked) sent = asked.ToArray();
                if (sent.Length != 4 || sent[3].IndexOf("Your last reply had no words for them in it", StringComparison.Ordinal) < 0 || sent[2].IndexOf("Your last reply had no words", StringComparison.Ordinal) >= 0) throw new Exception("Her second ask for just the answer was wrong: " + sent.Length + " asks");
                foreach (string body in sent) if (body.IndexOf("I should greet them warmly", StringComparison.Ordinal) >= 0 || body.IndexOf("tool_call>", StringComparison.Ordinal) >= 0) throw new Exception("Thinking or a written call was fed back to her brain.");
                string problem = null;
                try { brain.Ask("hi?"); }
                catch (BrainException error) { problem = error.Message; }
                if (problem != Brain.Answerless) throw new Exception("Two replies of only thinking didn't say the model only thought: " + problem);
                string joke = brain.Ask("tell me a joke");
                if (joke != "Why did the netrunner cross the road? To jack in on the other side.") throw new Exception("Thinking in plain words reached her chat or her answer was lost: " + joke);
                string honestReply = brain.Ask("how would you write a person in json");
                if (honestReply.IndexOf("{\"name\": \"Alex\", \"age\": 30}", StringComparison.Ordinal) < 0 || honestReply.IndexOf("2 < 3", StringComparison.Ordinal) < 0) throw new Exception("An honest JSON reply lost words: " + honestReply);
                int asks;
                lock (asked) asks = asked.Count;
                string signUp = brain.Ask("what should my sign-up page do");
                lock (asked) asks = asked.Count - asks;
                if (signUp != "The user wants to see a clear error when their password is too short, so show it right under the field." || asks != 1) throw new Exception("An honest reply about the user was taken for thinking: " + signUp + " after " + asks + " asks");
                lock (asked) asks = asked.Count;
                string picked = brain.Ask("blue or green");
                lock (asked) asks = asked.Count - asks;
                if (picked != "The user is asking me to pick. I should pick the blue one, it's calmer." || asks != 2) throw new Exception("A reply that read as thinking twice wasn't shown as it came: " + picked + " after " + asks + " asks");
                did.Clear();
                string clamped = brain.Ask("how do i clamp values in numpy");
                if (did.Count != 0 || clamped.IndexOf("np.clip(a=x, a_min=0, a_max=1)", StringComparison.Ordinal) < 0) throw new Exception("Code in an honest reply ran one of her tools or lost words: " + clamped + " / " + string.Join(",", did.ToArray()));
                // A chat reply that's only a call, like one copied from a persona, says nothing and runs nothing, and she isn't asked
                // again to put words to a call that never ran.
                lock (asked) asks = asked.Count;
                string onlyCall = null;
                bool stopped = false;
                try { onlyCall = brain.Ask("what does that code mean?"); }
                catch (BrainException) { stopped = true; }
                lock (asked) asks = asked.Count - asks;
                if (!stopped || did.Count != 0 || asks != 1) throw new Exception("A chat reply of only a written call ran, was shown or was asked about again: " + onlyCall + " / " + string.Join(",", did.ToArray()) + " after " + asks + " asks");
                fake.Stop();

                // Her own brain on their PC streams: a tag split across pieces, and one that never closes.
                asked.Clear();
                Func<string[], string> streamed = delegate(string[] pieces)
                {
                    System.Text.StringBuilder events = new System.Text.StringBuilder();
                    for (int i = 0; i < pieces.Length; i++) events.Append("data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":" + Voice.Quote(pieces[i]) + "}" + (i == pieces.Length - 1 ? ",\"finish_reason\":\"stop\"" : "") + "}]}\n\n");
                    return events.Append("data: [DONE]\n\n").ToString();
                };
                string address = FakeServer(new[]
                {
                    streamed(new[] { "<thi", "nk>The user", " says yo.</th", "ink>", "Yo, ", "choom." }),
                    streamed(new[] { "<think>", "The user asks the time", " and I keep going" }),
                    streamed(new[] { "Pretty chill ", "day." }),
                    streamed(new[] { "Sure.<tool", "_call>{\"name\": \"open_app\", \"argu", "ments\": {\"name\": \"Steam\"}}</tool_call>" }),
                    streamed(new[] { "Steam's ", "up." }),
                }, asked, out fake, 200, 1, true, 0, "text/event-stream");
                Brain local = new Brain { Model = "qwen3-4b", Remote = new Cloud(Brain.LocalPort(address), "qwen3-4b") };
                local.Act = brain.Act;
                did.Clear();
                string yo = local.Ask("yo");
                if (yo != "Yo, choom.") throw new Exception("A thinking tag split across a stream reached her chat: " + yo);
                string late = local.Ask("how was your day");
                if (late != "Pretty chill day.") throw new Exception("An unclosed thinking tag in a stream wasn't asked again: " + late);
                string steamed = local.Ask("ugh nothing to do");
                if (did.Count != 0 || steamed != "Sure.") throw new Exception("A streamed written call ran or showed: " + steamed + " / " + string.Join(",", did.ToArray()));
                fake.Stop();

                // Ollama's reply comes back as it was written, and gets the same cleaning.
                asked.Clear();
                Brain.Ollama = FakeServer(new[] { "{\"message\":{\"role\":\"assistant\",\"content\":\"analysisUser greets. Reply.assistantfinalHey there, choom.\"},\"done\":true}" }, asked, out fake);
                Brain ollama = new Brain { Model = "gpt-oss:20b" };
                ollama.Act = brain.Act;
                string oss = ollama.Ask("hey there");
                if (oss != "Hey there, choom.") throw new Exception("gpt-oss's analysis channel reached her chat on Ollama: " + oss);
                fake.Stop();

                // A server on their PC, OpenAI style like Ollama's /v1, whose model puts its thinking in a field of its own and
                // answers nothing, or only a closing tag (gemma4 did): asked once more for just her answer, and twice says so.
                asked.Clear();
                Func<string, string, string> served = delegate(string content, string reasoning)
                {
                    return "{\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":" + Voice.Quote(content) + (reasoning == null ? "" : ",\"reasoning\":" + Voice.Quote(reasoning)) + "},\"finish_reason\":\"stop\"}]}";
                };
                string serverAt = FakeServer(new[] { served("", "The user says hi. Say hi back."), served("Hi, choom.", null), served("<channel|>", "Thinking about it."), served("", "Still only thinking.") }, asked, out fake);
                Brain gemma = new Brain { Model = "gemma4:e4b-it-qat", Server = serverAt };
                gemma.Act = brain.Act;
                string hiBack = gemma.Ask("hi");
                if (hiBack != "Hi, choom.") throw new Exception("A reply whose thinking was in its own field, with no answer, wasn't asked again for her answer: " + hiBack);
                string twice = null;
                try { gemma.Ask("you there?"); }
                catch (BrainException error) { twice = error.Message; }
                int gemmaAsks;
                lock (asked) gemmaAsks = asked.Count;
                if (twice != Brain.Answerless || gemmaAsks != 4) throw new Exception("Two replies of only thinking in its own field didn't say the model only thought: " + (twice ?? "nothing said") + " after " + gemmaAsks + " asks");
                fake.Stop();

                // Ollama is told not to think, and only Ollama: her own Ollama's address from the first ask, another server once
                // a reply says it is Ollama, and any other server never.
                Func<string, string> plainly = delegate(string fingerprint)
                {
                    return "{\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"Sure.\"},\"finish_reason\":\"stop\"}]" + (fingerprint == null ? "" : ",\"system_fingerprint\":\"" + fingerprint + "\"") + "}";
                };
                string ollamaWasHere = Brain.Ollama;
                string toldNot = null;
                try
                {
                    asked.Clear();
                    string own = FakeServer(new[] { plainly(null) }, asked, out fake);
                    Brain.Ollama = own;
                    new Brain { Model = "gemma4:e4b-it-qat", Server = own }.Ask("one");
                    fake.Stop();
                    Brain.Ollama = ollamaWasHere;
                    string[] ownSent;
                    lock (asked) ownSent = asked.ToArray();
                    asked.Clear();
                    string learned = FakeServer(new[] { plainly("fp_ollama"), plainly("fp_ollama") }, asked, out fake);
                    Brain elsewhere = new Brain { Model = "gemma4:e4b-it-qat", Server = learned };
                    elsewhere.Ask("one");
                    elsewhere.Ask("two");
                    fake.Stop();
                    string[] learnedSent;
                    lock (asked) learnedSent = asked.ToArray();
                    asked.Clear();
                    string other = FakeServer(new[] { plainly("fp_other"), plainly("fp_other") }, asked, out fake);
                    Brain lmStudio = new Brain { Model = "qwen3-4b", Server = other };
                    lmStudio.Ask("one");
                    lmStudio.Ask("two");
                    fake.Stop();
                    string[] otherSent;
                    lock (asked) otherSent = asked.ToArray();
                    const string none = "\"reasoning_effort\":\"none\"";
                    if (ownSent.Length != 1 || ownSent[0].IndexOf(none, StringComparison.Ordinal) < 0) toldNot = "her own Ollama wasn't told not to think";
                    else if (learnedSent.Length != 2 || learnedSent[0].IndexOf(none, StringComparison.Ordinal) >= 0 || learnedSent[1].IndexOf(none, StringComparison.Ordinal) < 0) toldNot = "a server that answered as Ollama wasn't told from its next ask on";
                    else if (otherSent.Length != 2 || Array.Exists(otherSent, body => body.IndexOf("reasoning_effort", StringComparison.Ordinal) >= 0)) toldNot = "a server that isn't Ollama was sent reasoning_effort";
                }
                finally { Brain.Ollama = ollamaWasHere; Brain.ForgetOllamaServers(); }
                if (toldNot != null) throw new Exception("Ollama's thinking: " + toldNot);

                string[] lines = File.Exists(log) ? File.ReadAllLines(log) : new string[0];
                string wanted = "| leak | - | nvidia qwen/qwen3-next-80b-a3b-thinking: thinking with no opening tag taken out of her reply";
                if (!Array.Exists(lines, line => line.EndsWith(wanted, StringComparison.Ordinal))) throw new Exception("Her log didn't name the brain, model and kind of leak: " + string.Join(" / ", lines));
                if (!Array.Exists(lines, line => line.IndexOf("ollama gpt-oss:20b: gpt-oss analysis channel", StringComparison.Ordinal) >= 0) || !Array.Exists(lines, line => line.IndexOf("local qwen3-4b: tool call tags", StringComparison.Ordinal) >= 0)) throw new Exception("Her log missed a brain's leak: " + string.Join(" / ", lines));
                foreach (string line in lines) if (line.IndexOf("greet them", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("Steam", StringComparison.Ordinal) >= 0 || line.IndexOf("netrunner", StringComparison.Ordinal) >= 0) throw new Exception("Her log kept what a leak said: " + line);
                checks.Add("PASS leaks through her real chat with stand-in brains: NVIDIA, a streamed brain on their PC and Ollama never show thinking (no opening tag, split across a stream, unclosed, plain words or gpt-oss channels) in her bubble, voice or history, or feed it back to her brain; a call written as text in a chat reply is never shown, said or run, and the words around it stay, and a reply of only a call stops with no second ask; a reply that was all thinking is asked once more for her answer, and twice says the model only thought; an honest JSON reply keeps its words; an honest reply about \"the user\" is asked once and keeps every word, one that reads as thinking twice is shown as it came, and numpy code in a reply never runs her clip tool; her log names the brain, model and kind of each leak and never its words");
                checks.Add("PASS a server on their PC that is Ollama is told not to think (reasoning_effort none), from the first ask at her own Ollama's address and from the next once a reply says it is Ollama, and no other server is sent it; a reply of only thinking in its own field is asked again for her answer, and twice says so");
                checks.Add("LIMIT leak checks use stand-in brains only: no real reasoning model, Claude Code, Codex or Ollama was asked");
            }
            finally
            {
                if (fake != null) fake.Stop();
                Brain.Nvidia = openAiWas;
                Brain.Ollama = ollamaWas;
                Brain.MemoryPath = notesWas;
                Brain.MemoriesPath = memoriesWas;
                Brain.TranscriptPath = transcriptsWas;
                Brain.LogPath = logWas;
                Brain.Keeping = keepingWas;
                Hands.GameOpen = gameWas;
                try { File.Delete(log); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        // Her own notes sent back as a reply or a retry, and a lone bracketed aside, never reach her bubble or her history:
        // they take the empty reply's path, as does a repeat whose retry repeats. Stand-in brains only.
        private static void SmokeOwnNotes(List<string> checks)
        {
            string openAiWas = Brain.OpenAi;
            TcpListener server = null;
            Func<string[], Brain, string, string> ask = delegate(string[] replies, Brain talker, string words)
            {
                List<string> asked = new List<string>();
                Brain.OpenAi = FakeServer(Array.ConvertAll(replies, one => LocalReply(one)), asked, out server);
                try { return talker.Ask(words); }
                catch (BrainException error) { return "!" + error.Message; }
                finally { server.Stop(); server = null; }
            };
            Func<Brain> fresh = delegate { return new Brain { Remote = new Cloud("sk-proj-test", "gpt-5-mini"), Model = "gpt-5-mini" }; };
            try
            {
                Func<string, bool> empty = delegate(string said) { return said == "!" + Brain.Wordless || said == "!" + Brain.Answerless; };
                foreach (string note in new[] { "(For you, not from them: Right now it's 3 pm.)", "(That repeats your last answer. Answer what they just asked: \"hi\")", "\"(You called remember)\"", "(17 x 23 = 391)" })
                {
                    Brain talker = fresh();
                    string said = ask(new[] { note }, talker, "hey");
                    if (!empty(said) || talker.History.Count != 0) throw new Exception("Her own note reached her as a reply: " + note.Replace("\n", " ") + " -> " + said);
                }
                Brain repeating = fresh();
                string first = ask(new[] { "I think we should get noodles tonight." }, repeating, "what should we eat");
                string again = ask(new[] { "I think we should get noodles tonight.", "I think we should get noodles tonight." }, repeating, "where would we go");
                if (first != "I think we should get noodles tonight." || !empty(again) || repeating.History.Count != 2) throw new Exception("A repeat the retry repeated was said, or didn't take the empty path: " + first + " / " + again + ", history " + repeating.History.Count);
                Brain echoing = fresh();
                ask(new[] { "I think we should get noodles tonight." }, echoing, "what should we eat");
                string echoed = ask(new[] { "I think we should get noodles tonight.", "(That repeats your last answer. Answer what they just asked: \"where would we go\")" }, echoing, "where would we go");
                if (!empty(echoed) || echoing.History.Count != 2) throw new Exception("A retry that was her own note reached her: " + echoed + ", history " + echoing.History.Count);
                Brain mended = fresh();
                ask(new[] { "Noodles, obviously, with extra chilli." }, mended, "what should we eat");
                string retried = ask(new[] { "Noodles, obviously, with extra chilli.", "Somewhere with a view, then noodles." }, mended, "where would we go");
                Brain plain = fresh();
                string normal = ask(new[] { "Somewhere with good noodles and a window seat." }, plain, "where would we go");
                if (retried != "Somewhere with a view, then noodles." || normal != "Somewhere with good noodles and a window seat." || plain.History.Count != 2)
                    throw new Exception("A retried or normal reply was changed: " + retried + " / " + normal);
                checks.Add("PASS her own notes (For you, the repeat note, a tool note) and a reply that is one bracketed aside never reach her bubble or history and take the empty reply's path, and so does a retry that is one of her notes or a repeat whose retry repeats too; a mended retry and a normal reply are untouched");
            }
            finally
            {
                if (server != null) server.Stop();
                Brain.OpenAi = openAiWas;
            }
        }

        // An empty reply, or one of only asides, is asked again once, reworded, tool asks too; an empty retry gives her
        // "brain ain't braining" line. Her fallback is conversation only, on a PC that carries it. Stand-in brains only.
        private void SmokeEmptyRetry(List<string> checks)
        {
            string openAiWas = Brain.OpenAi;
            TcpListener server = null;
            Func<string, string> thinkWas = Think;
            Func<string, long> sizeWas = FallbackSize;
            Func<Machine> machineWas = FallbackMachine;
            bool onWas = preferences.LocalFallback, silentWas = preferences.Silent;
            string brainWas = preferences.FallbackBrain, lastWas = preferences.LastLocalBrain;
            try
            {
                Func<string[], Brain, List<string>, string, string> ask = delegate(string[] replies, Brain talker, List<string> heard, string words)
                {
                    Brain.OpenAi = FakeServer(Array.ConvertAll(replies, one => LocalReply(one)), heard, out server);
                    try { return talker.Ask(words); }
                    catch (BrainException error) { return "!" + error.Message; }
                    finally { server.Stop(); server = null; }
                };
                Func<Brain> fresh = delegate { return new Brain { Remote = new Cloud("sk-proj-test", "gpt-5-mini"), Model = "gpt-5-mini" }; };
                List<string> asked = new List<string>();
                string retried = ask(new[] { "", "Noodles. Obviously noodles." }, fresh(), asked, "what should we eat tonight?");
                bool reworded = asked.Count == 2 && asked[1].Contains("came back empty");
                string asides = ask(new[] { "(sigh) (warm) (warm)", "Fine, noodles it is." }, fresh(), new List<string>(), "what should we eat tonight?");
                asked = new List<string>();
                Brain twice = fresh();
                string blank = ask(new[] { "", "" }, twice, asked, "what should we eat tonight?");
                int actions = 0;
                Brain worker = fresh();
                worker.OwnSteps = true;
                worker.Act = delegate { actions++; return "Opened it."; };
                List<string> working = new List<string>();
                string tool = ask(new[] { "", "Call her Pixel." }, worker, working, "give my cat a name");
                bool toolRetried = working.Count == 2 && working[0].Contains("\"tools\":") && working[1].Contains("came back empty") && tool == "Call her Pixel." && actions == 0;
                if (retried != "Noodles. Obviously noodles." || !reworded || asides != "Fine, noodles it is." || blank != "!" + Brain.Wordless || asked.Count != 2 || twice.History.Count != 0 || !toolRetried)
                    throw new Exception("An empty reply wasn't asked again once, reworded, or its retry wasn't used: " + retried + " / " + reworded + " / " + asides + " / " + blank + " (" + asked.Count + " asks) / tool " + tool + " (" + working.Count + " asks)");
                checks.Add("PASS an empty reply, and one of only asides like \"(sigh) (warm) (warm)\", is asked again once on the same brain with a note that it came back empty, and the retry is used, a tool ask too; an empty retry takes the empty path and keeps nothing");

                // Her fallback answers conversation only: asked to open something, it holds no tools and runs none.
                List<string> local = new List<string>();
                string address = FakeServer(new[] { LocalReply("I can't do that from here, but hi.") }, local, out server);
                Brain fallen = fresh();
                int fallActs = 0;
                fallen.Act = delegate { fallActs++; return "Opened it."; };
                string fellSaid = Brain.AskFallback(Brain.ServerMark + address + "|fake-local", null, null, delegate { return fallen.Ask("open the calculator"); });
                server.Stop();
                server = null;
                bool chatOnly = fallActs == 0 && local.Count == 1 && !local[0].Contains("\"tools\":") && fellSaid.Length > 0;
                bool toolAsks = ForTools("can you open the calculator") && ForTools("open spotify and play something") && !ForTools("how was your day?") && !ForTools("tell me a joke");
                if (!chatOnly || !toolAsks) throw new Exception("Her fallback took a tool ask: chat only " + chatOnly + " (" + fallActs + " acts, " + local.Count + " asks), tool asks told " + toolAsks);
                checks.Add("PASS her local fallback answers conversation only: it holds no tools and runs none, and an ask her hands read as one for a tool (open, play) never goes to it; talk does");

                // Never a fallback this PC can't carry: the weights on the card with 1 GB spare, or in half the memory.
                preferences.LocalFallback = true;
                preferences.FallbackBrain = "big-local";
                preferences.LastLocalBrain = null;
                const long Gb = 1L << 30;
                string to;
                Pick pick;
                FallbackSize = delegate { return 5 * Gb; };
                FallbackMachine = delegate { return new Machine { Vram = 4 * Gb, Ram = 8 * Gb }; };
                bool weak = !FallbackReady(out to, out pick);
                FallbackMachine = delegate { return new Machine { Vram = 8 * Gb, Ram = 8 * Gb }; };
                bool card = FallbackReady(out to, out pick);
                FallbackMachine = delegate { return new Machine { Vram = 0, Ram = 16 * Gb }; };
                bool memory = FallbackReady(out to, out pick);
                FallbackSize = delegate { return -1; };
                bool unknown = !FallbackReady(out to, out pick) && FallbackReady(out to, out pick, false);
                if (!weak || !card || !memory || !unknown || !Engine.Carries(new Machine { Vram = 8 * Gb }, 7 * Gb) || Engine.Carries(new Machine { Vram = 8 * Gb, Ram = 8 * Gb }, 7 * Gb + 1))
                    throw new Exception("Her fallback was ready on a PC that can't carry it, or not on one that can: weak " + weak + ", card " + card + ", memory " + memory + ", unknown " + unknown);
                checks.Add("PASS her local fallback is never one this PC can't carry: its weights on the graphics card with 1 GB spare or in half the memory; an Ollama size not known counts against it when asking, and the Local light only looks");

                // Asked again and still nothing: a line of hers in her voice, not the notice.
                preferences.Silent = true;
                foreach (string empty in new[] { Brain.Wordless, Brain.Answerless })
                {
                    CloseTalk();
                    string thrown = empty;
                    Think = delegate { throw new BrainException(thrown, true); };
                    Hear("what should we eat tonight?");
                    System.Diagnostics.Stopwatch waited = System.Diagnostics.Stopwatch.StartNew();
                    while (reply == null && waited.ElapsedMilliseconds < 5000) { System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                    bool line = reply != null && Array.IndexOf(Personalities.Lines("brain empty", BrainEmptyLines), reply) >= 0;
                    if (empty == Brain.Wordless ? !line : reply != Brain.Answerless || talkState != TalkState.Dormant)
                        throw new Exception((empty == Brain.Wordless ? "Nothing she could say didn't give her brain-ain't-braining line: " : "A model that only thought lost its own words: ") + (reply ?? "no reply"));
                }
                checks.Add("PASS when her brain sends nothing she can say even asked again, she says a line of her own in her voice (\"My brain ain't braining today\" and the rest), the notice in her log only; a model that only thought still says to pick another model");
            }
            finally
            {
                if (server != null) server.Stop();
                Brain.OpenAi = openAiWas;
                Think = thinkWas;
                FallbackSize = sizeWas;
                FallbackMachine = machineWas;
                preferences.LocalFallback = onWas; preferences.Silent = silentWas;
                preferences.FallbackBrain = brainWas; preferences.LastLocalBrain = lastWas;
                CloseTalk();
            }
        }

        private static readonly Regex PauseMark = new Regex(@"#wait\s*\d+(?:[.,]\d+)?\s*s#", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex RoundAside = new Regex(@"\([^()]+\)");
        private static readonly string[] Codewords = { "senpai", "extended", "abridged", "codex", "overdrive" };

        // The marks a line carries: a pause, a silent part, a cue in square or round brackets, an aside, a face, the meme mark.
        private static List<string> MarksOf(string line)
        {
            List<string> marks = new List<string>();
            if (PauseMark.IsMatch(line)) marks.Add("pause");
            if (line.IndexOf("**silent**", StringComparison.OrdinalIgnoreCase) >= 0) marks.Add("silent");
            foreach (Match cue in Voice.Cue.Matches(line))
            {
                string kind = cue.Value.TrimStart().StartsWith("(", StringComparison.Ordinal) ? "round cue" : "square cue";
                if (!marks.Contains(kind)) marks.Add(kind);
            }
            foreach (Match aside in RoundAside.Matches(line)) if (!Voice.Cue.IsMatch(aside.Value)) { marks.Add("aside"); break; }
            if (Voice.Faceless(line) != line) marks.Add("face");
            if (Sayings.IsMeme(line)) marks.Add("meme");
            return marks;
        }

        // The mark left in a place that should not hold it, or null. A voice loses faces too; Fish keeps square cues.
        private static string LeftIn(string text, bool spoken, bool fish)
        {
            if (string.IsNullOrEmpty(text)) return null;
            if (PauseMark.IsMatch(text)) return "pause";
            if (text.IndexOf("**", StringComparison.Ordinal) >= 0) return "silent";
            if (Regex.IsMatch(text, @"(^|\|\s)~")) return "meme";
            foreach (Match cue in Voice.Cue.Matches(text))
            {
                bool round = cue.Value.TrimStart().StartsWith("(", StringComparison.Ordinal);
                if (!fish || round) return round ? "round cue" : "square cue";
            }
            if (spoken && Voice.Faceless(text) != text) return "face";
            return null;
        }

        private static void GatherMarked(Dictionary<string, string> into, string text, string source)
        {
            if (string.IsNullOrEmpty(text)) return;
            string section = "";
            foreach (string raw in text.Split('\n'))
            {
                string one = raw.Trim();
                if (one.Length == 0 || one.StartsWith(">", StringComparison.Ordinal)) continue;
                if (one.StartsWith("#", StringComparison.Ordinal) && !PauseMark.IsMatch(one)) { section = one.TrimStart('#').Trim(); continue; }
                if (one.StartsWith("- ", StringComparison.Ordinal)) one = one.Substring(2).Trim();
                if (one.Length > 1 && MarksOf(one).Count > 0 && !into.ContainsKey(one)) into[one] = source + (section.Length > 0 ? ": " + section : "");
            }
        }

        // Her pools kept in code, from the classes that hold her lines; a field named for the gated side is left out.
        private static void GatherFields(Dictionary<string, string> into, Type type)
        {
            foreach (FieldInfo field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (Array.Exists(Codewords, word => field.Name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                List<string> found = new List<string>();
                object value = field.FieldType == typeof(string[]) || field.FieldType == typeof(string[,]) || field.FieldType == typeof(string[][]) ? field.GetValue(null) : null;
                if (value is string[]) found.AddRange((string[])value);
                else if (value is string[,]) foreach (string one in (string[,])value) found.Add(one);
                else if (value is string[][]) foreach (string[] row in (string[][])value) if (row != null) found.AddRange(row);
                foreach (string one in found)
                    if (!string.IsNullOrEmpty(one) && one.IndexOf("(?", StringComparison.Ordinal) < 0 && one.IndexOf('\\') < 0 && one.IndexOf("[^", StringComparison.Ordinal) < 0)
                        GatherMarked(into, one, type.Name + "." + field.Name);
            }
        }

        private sealed class Went { internal string Bubble = "", History = "", Voice = "", Log = ""; }

        // The steps her own line takes to each place, without showing it: for the senpai side, and held against the real run.
        private static Went Stepped(string line)
        {
            bool meme = Sayings.IsMeme(line);
            string said = meme ? Voice.Uncued(Sayings.Unmeme(line)) : Voice.Uncued(line);
            return new Went { Bubble = Voice.Unsilenced(said), History = Voice.Uncued(Voice.Unsilenced(Sayings.Unmeme(line))).Trim(), Voice = Voice.Spoken(said) };
        }

        private static string Letters(string text)
        {
            StringBuilder kept = new StringBuilder();
            foreach (char one in text ?? "") if (char.IsLetterOrDigit(one)) kept.Append(char.ToLowerInvariant(one));
            return kept.ToString();
        }

        // Every shipped line with a mark goes through her bubble, history, line log, each voice kind and the parts of a
        // line, as do brain replies and Twitch chat read aloud. One side at a time; the senpai side unshown, by count.
        private void SmokeMarkupEverywhere(List<string> checks)
        {
            Dictionary<string, string> plain = new Dictionary<string, string>(), senpai = new Dictionary<string, string>();
            bool senpaiWas = Brain.SenpaiOn;
            string languageWas = Brain.Language, openAiWas = Brain.OpenAi;
            Func<string, byte[]> speakWas = Speak, freeWas = FreeSpeak;
            bool quietWas = preferences.Quiet, silentWas = preferences.Silent, serverWas = preferences.ServerVoice, brokeWas = voiceBroke, shushedWas = shushed;
            string pack = Path.Combine(Path.GetTempPath(), "capri-markup-" + Guid.NewGuid().ToString("N") + ".pack");
            TcpListener server = null;
            Dictionary<string, int[]> table = new Dictionary<string, int[]>();
            Dictionary<string, List<string>> named = new Dictionary<string, List<string>>();
            Action<string, string, List<string>, string, string, bool, bool> place = delegate(string side, string line, List<string> marks, string where, string text, bool spoken, bool fish)
            {
                foreach (string mark in marks)
                {
                    string key = side + " | " + mark + " | " + where;
                    if (!table.ContainsKey(key)) table[key] = new int[2];
                    table[key][0]++;
                }
                string left = LeftIn(text, spoken, fish);
                if (left == null) return;
                string failed = side + " | " + left + " | " + where;
                if (!table.ContainsKey(failed)) table[failed] = new int[2];
                table[failed][1]++;
                if (!named.ContainsKey(failed)) named[failed] = new List<string>();
                if (named[failed].Count < 3) named[failed].Add(side == "senpai" ? "senpai line " + line : Voice.Short(line, 90) + " -> " + Voice.Short(text, 90));
            };
            try
            {
                Brain.SenpaiOn = false;
                Brain.Language = null;
                GatherMarked(plain, Sayings.Text, "lines.md");
                foreach (KeyValuePair<string, string> file in new Dictionary<string, string> { { "interests", Interests.Text }, { "interests hers", Interests.HersText }, { "interests random", Interests.RandomText }, { "lore", Lore.Text }, { "moments", Moments.Text }, { "angles", Angles.Text }, { "touch", Touch.Text }, { "chess", ChessTalk.Text }, { "commands", Commands.Text } })
                    GatherMarked(plain, file.Value, file.Key);
                foreach (string spoken in SysLines.SpokenLines()) GatherMarked(plain, spoken, "sys lines");
                foreach (Type type in new[] { typeof(Sayings), typeof(SysLines), typeof(Interests), typeof(Acts), typeof(Them), typeof(Lore), typeof(Moments), typeof(Angles), typeof(Touch), typeof(ChessTalk), typeof(Personalities), typeof(Care) })
                    GatherFields(plain, type);
                List<string> folders = new List<string> { Personalities.Plain };
                if (!senpaiWas) folders.Add(Logs.DataOf(Brain.LogPath));
                foreach (string folder in folders)
                    if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                        foreach (string file in Directory.GetFiles(folder, "*.md"))
                        {
                            string name = Path.GetFileName(file).ToLowerInvariant();
                            if (name != "me.md" && name != "memories.md" && name != "personality.backup.md") GatherMarked(plain, File.ReadAllText(file), name);
                        }
                if (Codex.Write(pack) > 0)
                {
                    Dictionary<string, string> sections = Codex.Unsealed(File.ReadAllBytes(pack));
                    if (sections != null) foreach (string text in sections.Values) GatherMarked(senpai, text, "senpai");
                    foreach (string one in plain.Keys) senpai.Remove(one);
                }

                List<string> voiced = new List<string>();
                byte[] wav = Voice.Wave(new short[2400], 24000);
                Speak = delegate(string words) { lock (voiced) voiced.Add(words); return wav; };
                FreeSpeak = null;
                preferences.Quiet = preferences.Silent = preferences.ServerVoice = voiceBroke = shushed = false;
                string logFile = Path.Combine(Logs.DataOf(Brain.LogPath) ?? "", "lines-log.txt");
                Func<int> logRows = delegate { try { return File.Exists(logFile) ? File.ReadAllLines(logFile).Length : 0; } catch (IOException) { return -1; } };
                int logged = 0, waitedOut = 0, unlike = 0;
                List<string> unlikeNamed = new List<string>();
                Func<string, Went> shown = delegate(string part)
                {
                    CloseTalk();
                    voiceUntil = 0;
                    lock (voiced) voiced.Clear();
                    List<ChatMessage> before = brain.History;
                    ChatMessage last = before.Count > 0 ? before[before.Count - 1] : null;
                    int rows = logRows();
                    SayOwn("markup", part);
                    System.Diagnostics.Stopwatch waited = System.Diagnostics.Stopwatch.StartNew();
                    while (waited.ElapsedMilliseconds < 5000 && reply == null) { System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                    if (reply == null) waitedOut++;
                    Sayings.Flush();
                    Went went = new Went { Bubble = reply ?? "" };
                    List<ChatMessage> history = brain.History;
                    if (history.Count > 0 && !ReferenceEquals(history[history.Count - 1], last)) went.History = history[history.Count - 1].Content ?? "";
                    lock (voiced) went.Voice = string.Join(" ", voiced.ToArray());
                    if (rows >= 0 && File.Exists(logFile))
                    {
                        string[] now = File.ReadAllLines(logFile);
                        if (now.Length > rows) { logged++; went.Log = string.Join(" / ", now, rows, now.Length - rows); }
                    }
                    return went;
                };
                int index = 0;
                // A line with a pause is said in parts, as SayOwn does; any other goes whole. A real run is held against
                // the steps, which are all the senpai side gets.
                Action<string, string, string, bool> through = delegate(string side, string line, string source, bool real)
                {
                    List<string> parts = new List<string>();
                    if (PauseMark.IsMatch(line)) foreach (KeyValuePair<double, string> part in Sayings.Parts(line)) parts.Add(part.Value);
                    else parts.Add(line);
                    if (real) Sayings.Built(line, "markup check");
                    Went whole = new Went();
                    string fish = "";
                    foreach (string part in parts)
                    {
                        Went steps = Stepped(part), went = real ? shown(part) : steps;
                        if (!real) went.Log = Letters(steps.Bubble).Contains(Letters(line)) ? line : steps.Bubble;
                        string differs = !real ? "" : went.Bubble != steps.Bubble ? "bubble" : went.History != steps.History ? "chat kept" : went.Voice != steps.Voice ? "voice" : "";
                        if (differs.Length > 0)
                        {
                            unlike++;
                            if (unlikeNamed.Count < 3) unlikeNamed.Add(differs + " of " + Voice.Short(part, 50) + ": [" + (differs == "bubble" ? went.Bubble : differs == "chat kept" ? went.History : went.Voice) + "] not [" + (differs == "bubble" ? steps.Bubble : differs == "chat kept" ? steps.History : steps.Voice) + "]");
                        }
                        whole.Bubble += " " + went.Bubble; whole.History += " " + went.History; whole.Voice += " " + went.Voice; whole.Log += " " + went.Log;
                        fish += " " + Voice.Spoken(Sayings.IsMeme(part) ? Sayings.Unmeme(part) : part, true);
                    }
                    string label = side == "senpai" ? (++index).ToString(System.Globalization.CultureInfo.InvariantCulture) : line;
                    List<string> marks = MarksOf(line);
                    place(side, label, marks, "bubble", whole.Bubble, false, false);
                    place(side, label, marks, "chat kept", whole.History, false, false);
                    place(side, label, marks, "line log", whole.Log, false, false);
                    place(side, label, marks, "voice (ElevenLabs, free, HD)", Voice.Sayable(whole.Voice), true, false);
                    place(side, label, marks, "voice server", Voice.Sayable(Voice.Uncued(whole.Voice)), true, false);
                    place(side, label, marks, "Fish voice", fish, true, true);
                    place(side, label, marks, "own note", Brain.OwnNote(line) || Brain.OwnNote(whole.Bubble.Trim()) ? "(caught as her note)" : "", false, false);
                };
                foreach (KeyValuePair<string, string> one in plain) through("plain", one.Key, one.Value, true);
                // Lines as they might write them in lines.md, for the marks no shipped line carries.
                foreach (string made in new[] { "**silent**:3**/silent** hey you", "[laughing] stop it, you", "(sighing) fine, have it your way", "Hold on #wait2s# done", "nice one ^_^", "(lol) that was a good one", "~(squeaky) QUASSO!" })
                    through("made", made, "lines.md: check-ins", true);
                List<string> split = new List<string>();
                Action<string, string> partWas = partSaid;
                try
                {
                    partSaid = delegate(string by, string part) { lock (split) split.Add(part); };
                    SayOwn("markup", "Hold on #wait0.1s# done");
                    for (int i = 0; i < 100 && split.Count < 2; i++) { System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                }
                finally { partSaid = partWas; }
                place("made", "Hold on #wait0.1s# done", new List<string> { "pause" }, "parts (SayOwn)", split.Count == 2 && split[0] == "Hold on" && split[1] == "done" ? "" : "#wait0.1s# not split: " + string.Join(" / ", split.ToArray()), false, false);
                CloseTalk();
                foreach (KeyValuePair<string, string> one in senpai) through("senpai", one.Key, one.Value, false);

                // Her brain writing each mark, and a reply that is only asides; and Twitch chat read aloud.
                string[] replies = { "Hold on. #wait5s# Done, choom.", "**silent**:3**/silent** Hey there, choom.", "[laughing] No way, that's wild.", "(sighs) Fine, I'll do it.", "That's adorable :3 honestly.", "~no u", "(Just kidding) You know I love it.", "(sigh) (warm) (warm)" };
                string onlyAsides = "";
                foreach (string written in replies)
                {
                    List<string> asked = new List<string>();
                    Brain.OpenAi = FakeServer(new[] { LocalReply(written), LocalReply("Okay then.") }, asked, out server);
                    Brain talker = new Brain { Remote = new Cloud("sk-proj-test", "gpt-5-mini"), Model = "gpt-5-mini" };
                    string said;
                    try { said = talker.Ask("hey"); }
                    catch (BrainException) { said = ""; }
                    server.Stop();
                    server = null;
                    List<ChatMessage> kept = talker.History;
                    string history = kept.Count > 0 ? kept[kept.Count - 1].Content ?? "" : "";
                    CloseTalk();
                    voiceUntil = 0;
                    lock (voiced) voiced.Clear();
                    if (said.Length > 0)
                    {
                        Answer(++asking, said, TalkState.Talking);
                        System.Diagnostics.Stopwatch waited = System.Diagnostics.Stopwatch.StartNew();
                        while (waited.ElapsedMilliseconds < 5000 && reply == null) { System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                    }
                    string bubble = said.Length > 0 ? reply ?? "" : "", words;
                    lock (voiced) words = string.Join(" ", voiced.ToArray());
                    if (written == "(sigh) (warm) (warm)") onlyAsides = bubble == "Okay then." ? "is asked again" : "shows \"" + bubble + "\"";
                    List<string> marks = MarksOf(written);
                    place("brain", written, marks, "bubble", bubble, false, false);
                    place("brain", written, marks, "chat sent back", history, false, false);
                    place("brain", written, marks, "voice (ElevenLabs, free, HD)", Voice.Sayable(words), true, false);
                }
                CloseTalk();
                foreach (string chat in new[] { "hi chat :3", "**silent**psst**/silent** yo chat", "[laughing] that was close", "#wait5s# hey", "~no u", "(sighs) again?" })
                {
                    place("twitch read", chat, MarksOf(chat), "voice", Voice.Sayable(AloudWords(chat)), true, false);
                }

                List<string> keys = new List<string>(table.Keys);
                keys.Sort(StringComparer.Ordinal);
                int failures = 0;
                foreach (string key in keys)
                {
                    if (table[key][0] > 0) checks.Add("TABLE markup | " + key + " | " + table[key][0] + " | " + table[key][1]);
                    else if (table[key][1] > 0) checks.Add("TABLE markup | " + key + " | - | " + table[key][1]);
                    if (table[key][1] == 0) continue;
                    failures++;
                    checks.Add("FAIL markup | " + key + " | " + table[key][1] + " left: " + string.Join(" // ", named[key].ToArray()));
                }
                checks.Add("TABLE markup lines: " + plain.Count + " plain, " + senpai.Count + " senpai; log rows for " + logged + "; no bubble for " + waitedOut + "; real run unlike the steps for " + unlike + (unlikeNamed.Count > 0 ? " (" + string.Join(" // ", unlikeNamed.ToArray()) + ")" : "") + "; a reply of only asides " + onlyAsides);
                if (onlyAsides != "is asked again") { failures++; checks.Add("FAIL markup | brain | a reply of only asides " + onlyAsides); }
                if (failures == 0) checks.Add("PASS every shipped line with a mark (pause, silent part, cue, aside, face, meme mark), both sides, and her brain's replies with each, show and say only what each place should: bubble, history, line log, each voice, the parts of a line, Twitch chat read aloud (shown as typed, said without cues or pauses); none is caught as her note");
            }
            finally
            {
                if (server != null) server.Stop();
                Brain.OpenAi = openAiWas;
                Brain.SenpaiOn = senpaiWas;
                Brain.Language = languageWas;
                Speak = speakWas;
                FreeSpeak = freeWas;
                preferences.Quiet = quietWas; preferences.Silent = silentWas; preferences.ServerVoice = serverWas; voiceBroke = brokeWas; shushed = shushedWas;
                CloseTalk();
                try { File.Delete(pack); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }
}
#endif
