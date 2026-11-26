using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace CapriDesktop
{
    // The conversation brain, Gemma 3 1B on this PC: beside an online main brain it judges what her rules miss and answers
    // chat; with a local one the two swap as she idles (372). One local model is loaded at a time.
    internal sealed partial class Companion
    {
        internal const string ConvoName = "Gemma 3 1B", ConvoOllama = "gemma3:1b";

        private static string Clipped(string text, int most) { text = text ?? ""; return text.Length <= most ? text : text.Substring(0, most - 3) + "..."; }

        // Gemma 4 E4B as the local fallback when none was picked: her engine's, else Ollama's, else none. Swapped by the checks.
        internal Func<string> FallbackDefault;

        private string E4bHere()
        {
            if (engine.Has(Engine.E4B)) return Brain.BuiltIn;
            try { if (Array.IndexOf(Brain.InstalledModels(), Engine.E4B.Ollama) >= 0) return Engine.E4B.Ollama; } catch (Exception) { }
            return null;
        }

        // Ready to answer: switched on, this PC carries it, and it's in Ollama or her own engine has it.
        private bool ConversationReady(out string to, out Pick pick)
        {
            to = null;
            pick = null;
            if (!preferences.ConversationBrainOn || ConversationBrain != ConvoName || !Engine.Carries(FallbackMachine(), Engine.Gemma3.Size)) return false;
            if (ConvoInOllama()) { to = ConvoOllama; return true; }
            if (engine.Has(Engine.Gemma3)) { to = Brain.BuiltIn; pick = Engine.Gemma3; return true; }
            return false;
        }

        internal static Func<bool> ConvoInOllama = delegate
        {
            // The known list: this is asked from her window's thread (Mainframe.Sync), which must never wait on Ollama.
            try { return Array.Exists(Brain.InstalledModelsKnown(), one => one == ConvoOllama || one == ConvoOllama + ":latest"); }
            catch (Exception) { return false; }
        };

        // The local model loaded now, by where it runs: "ollama:<model>" or "engine:<pick>". Taking another lets this one
        // go first: Ollama's with keep_alive 0, waited for; her engine's by loading the new pick in its place.
        private string localHeld;
        private readonly object localGate = new object();

        private void HoldLocal(string to, Pick pick)
        {
            string key = to == Brain.BuiltIn ? "engine:" + (pick == null ? "" : pick.Name) : "ollama:" + to;
            string going;
            lock (localGate)
            {
                if (localHeld == key) return;
                going = localHeld;
                localHeld = key;
            }
            if (going != null && going.StartsWith("ollama:", StringComparison.Ordinal))
            {
                brain.Unload(going.Substring(7));
                Brain.Log("brain", -1, going.Substring(7) + " let go in Ollama, so only one local brain is loaded.");
            }
        }

        // The swap: idle this long, it stands in; this many of its replies in a row, or a line this long, and the main
        // brain takes over again.
        internal const double SwapIdle = 600;
        internal const int DragTurns = 6, HeavyChars = 300;
        private volatile bool toothpickIn;
        private int toothpickTurns;
        private double lastTalkAt;

        internal bool ToothpickIn { get { return toothpickIn; } }

        // Her main brain in Ollama or her own engine, not the toothpick itself.
        private bool MainLocal { get { return brain.Remote == null && string.IsNullOrEmpty(brain.Server) && brain.Model != null && brain.Model != ConvoOllama; } }

        private void HookSwap()
        {
            // Any ask of her main brain, by any path, is her using it: it is never let go while in use.
            Brain.MainAsked = delegate { lastTalkAt = clock.Elapsed.TotalSeconds; if (toothpickIn) SwapBack("her main brain was asked"); };
            Brain.IdleAside = delegate(Func<string> ask)
            {
                string to;
                Pick pick;
                if (!toothpickIn || !ConversationReady(out to, out pick)) return ask();
                return Brain.AskFallback(to, engine, pick, ask, "1h");
            };
        }

        // Nobody has talked to her or asked her main brain anything for the idle time.
        private bool Idle { get { return clock.Elapsed.TotalSeconds - lastTalkAt >= SwapIdle; } }

        // Looked at as she idles: long enough without talk, the main brain lets go and the toothpick loads.
        private void SwapWhenIdle()
        {
            if (toothpickIn || !MainLocal || !Idle) return;
            ThreadPool.QueueUserWorkItem(delegate { try { SwapIn(); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); } });
        }

        private void SwapIn()
        {
            string to;
            Pick pick;
            if (toothpickIn || !MainLocal || !Idle || !ConversationReady(out to, out pick)) return;
            toothpickIn = true;
            toothpickTurns = 0;
            string main = brain.Model;
            lock (localGate) localHeld = null;
            if (main == Brain.BuiltIn) { if (to != Brain.BuiltIn && engine.Running) engine.Stop(); }
            else brain.Unload(main);
            HoldLocal(to, pick);
            if (to != Brain.BuiltIn) brain.Preload(to, "1h");
            Brain.Log("brain", -1, "Idle: her main brain let go and " + ConvoName + " loaded for idle work and light chat.");
        }

        // The main brain takes over again: the toothpick lets go, and the main brain loads on its next ask.
        private void SwapBack(string why)
        {
            if (!toothpickIn) return;
            toothpickIn = false;
            toothpickTurns = 0;
            string going;
            lock (localGate) { going = localHeld; localHeld = null; }
            if (going != null && going.StartsWith("ollama:", StringComparison.Ordinal)) brain.Unload(going.Substring(7));
            else if (going != null && brain.Model != Brain.BuiltIn && engine.Running) engine.Stop();
            Brain.Log("brain", -1, ConvoName + " handed over to her main brain (" + why + "), loading it again.");
        }

        // A line heard while the toothpick stands in: light chat it answers; a tool ask, its "tool", a long line or a
        // dragging conversation goes back to the main brain (null).
        internal string ToothpickAnswer(string text, bool toolAsk, Func<string> answer)
        {
            lastTalkAt = clock.Elapsed.TotalSeconds;
            if (!toothpickIn) return null;
            string why = toolAsk ? "a tool ask" : (text ?? "").Length > HeavyChars ? "a long line" : toothpickTurns >= DragTurns ? "a long conversation" : null;
            string said = why == null ? ConversationAnswer(text, false, answer) : null;
            if (said != null) { toothpickTurns++; return said; }
            SwapBack(why ?? "more than light chat");
            return null;
        }

        // Chat answered by the conversation brain, or null when her main brain should take it: a tool ask by her rules or
        // by its judgement, an error, or no answer.
        internal string ConversationAnswer(string text, bool toolAsk, Func<string> answer)
        {
            string to;
            Pick pick;
            if (toolAsk || !ConversationReady(out to, out pick)) return null;
            try
            {
                HoldLocal(to, pick);
                string route = Brain.AskFallback(to, engine, pick, delegate { return brain.Route(text); }, "1h");
                if (route != "chat") { Brain.Log("brain", -1, "The conversation brain sent \"" + Clipped(text, 60) + "\" to her main brain (" + (route ?? "no answer") + ")."); return null; }
                string said = Brain.AskFallback(to, engine, pick, answer, "1h");
                if (string.IsNullOrWhiteSpace(said)) return null;
                // Her note's time read back is no answer: her main brain takes it.
                if (Brain.ReadsTime(said) && !Brain.AsksTime(text)) { brain.TakeBack(said); Brain.Log("brain", -1, "The conversation brain read the time out instead of answering, so her main brain did."); return null; }
                string shown = Brain.Within(said, 25);
                brain.Shortened(said, shown);
                return shown;
            }
            catch (Exception error)
            {
                Brain.Log("brain", -1, "The conversation brain couldn't answer, so her main brain did: " + error.Message);
                return null;
            }
        }

        // Get it: Ollama's own library when Ollama is on this PC, else her engine's pinned file, its size and hash checked.
        private volatile int convoGetting = -1;
        private string convoProblem;

        internal string ConvoKey
        {
            get
            {
                if (!Engine.Carries(FallbackMachine(), Engine.Gemma3.Size)) return null;
                if (convoGetting >= 0) return convoGetting + "%";
                if (ConvoInOllama() || engine.Has(Engine.Gemma3)) return "Ready";
                return "Get it, " + (Engine.Gemma3.Size / 1e9).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " GB";
            }
        }

        internal string ConvoProblem { get { return convoProblem; } }

        internal void GetConversationBrain()
        {
            if (convoGetting >= 0 || ConvoKey == null || ConvoKey == "Ready") return;
            convoGetting = 0;
            convoProblem = null;
            Action<string, long, long> progress = delegate(string what, long done, long total)
            {
                if (total > 0) convoGetting = (int)Math.Min(99, done * 100 / total);
                OnWindow(delegate { if (mainframe != null) mainframe.Sync(); });
            };
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    if (Brain.OllamaExe() != null) { Brain.WakeOllama(); Brain.Pull(ConvoOllama, progress, delegate { return false; }); }
                    else engine.Install(Engine.Gemma3, progress);
                    Brain.Log("brain", -1, ConvoName + " is ready as her conversation brain.");
                }
                catch (Exception error) { convoProblem = error.Message; Brain.Log("brain", -1, "Getting " + ConvoName + " stopped: " + error.Message); }
                finally { convoGetting = -1; OnWindow(delegate { if (mainframe != null) mainframe.Sync(); }); }
            });
        }

#if CHECKS
        // The conversation brain on a stand-in Ollama: chat answered with no tools, tool asks and errors passed on, the other
        // local brain let go first, a PC that can't carry it skipped, Get it showing its size.
        private void SmokeConversationBrain(List<string> checks)
        {
            bool onWas = preferences.ConversationBrainOn;
            string pickWas = preferences.ConversationBrain, ollamaWas = Brain.Ollama, heldWas = localHeld;
            Func<bool> inOllamaWas = ConvoInOllama;
            Func<Machine> machineWas = FallbackMachine;
            List<string> asked = new List<string>();
            System.Net.Sockets.TcpListener fake;
            Brain.Ollama = FakeServer(new string[]
            {
                "{\"message\":{\"role\":\"assistant\",\"content\":\"\"},\"done_reason\":\"unload\"}",
                "{\"message\":{\"role\":\"assistant\",\"content\":\"chat\"},\"done_reason\":\"stop\"}",
                "{\"message\":{\"role\":\"assistant\",\"content\":\"Mm, rainy days are for blankets.\"},\"done_reason\":\"stop\"}",
                "{\"message\":{\"role\":\"assistant\",\"content\":\"tool\"},\"done_reason\":\"stop\"}",
                "{\"error\":\"model not found\"}",
                "{\"message\":{\"role\":\"assistant\",\"content\":\"\"},\"done_reason\":\"stop\"}"
            }, asked, out fake);
            List<string> wrong = new List<string>();
            try
            {
                preferences.ConversationBrainOn = true;
                preferences.ConversationBrain = ConvoName;
                ConvoInOllama = delegate { return true; };
                FallbackMachine = delegate { return new Machine { Vram = 8L << 30, Ram = 16L << 30 }; };
                localHeld = "ollama:gemma4:e4b-it-qat";
                Func<string> answer = delegate { return new Brain { Model = "stand-in" }.Aside("Talk.", "what a rainy day", 40); };
                string chat = ConversationAnswer("what a rainy day", false, answer);
                if (chat != "Mm, rainy days are for blankets.") wrong.Add("chat wasn't answered by it: " + chat);
                string[] sent;
                lock (asked) sent = asked.ToArray();
                if (sent.Length < 3 || sent[0].IndexOf("\"keep_alive\":\"0s\"", StringComparison.Ordinal) < 0 || sent[0].IndexOf("gemma4", StringComparison.Ordinal) < 0)
                    wrong.Add("E4B wasn't let go before the 1B loaded: " + (sent.Length > 0 ? Clipped(sent[0], 120) : "nothing sent"));
                if (sent.Length >= 2 && (sent[1].IndexOf("Answer tool or chat.", StringComparison.Ordinal) < 0 || sent[1].IndexOf("\"num_predict\":4", StringComparison.Ordinal) < 0 || sent[1].IndexOf("\"tools\"", StringComparison.Ordinal) >= 0))
                    wrong.Add("the router's ask wasn't the framed one: " + Clipped(sent[1], 200));
                if (sent.Length >= 3 && sent[2].IndexOf("\"tools\"", StringComparison.Ordinal) >= 0) wrong.Add("the conversation brain was given tools");
                if (ConversationAnswer("open discord", true, answer) != null || asked.Count != sent.Length) wrong.Add("a tool ask by her rules reached it");
                if (ConversationAnswer("could you sort that out", false, answer) != null) wrong.Add("its \"tool\" didn't go to her main brain");
                if (ConversationAnswer("hmm", false, answer) != null) wrong.Add("its error didn't go to her main brain");
                FallbackMachine = delegate { return new Machine { Vram = 0, Ram = 1L << 30 }; };
                int before = asked.Count;
                if (ConversationAnswer("hello", false, answer) != null || asked.Count != before || ConvoKey != null) wrong.Add("a PC that can't carry it loaded it");
                FallbackMachine = delegate { return new Machine { Vram = 8L << 30, Ram = 16L << 30 }; };
                ConvoInOllama = delegate { return false; };
                if (!engine.Has(Engine.Gemma3) && ConvoKey != "Get it, 0.8 GB") wrong.Add("Get it reads " + ConvoKey);
                preferences.ConversationBrainOn = false;
                if (ConversationAnswer("hello", false, answer) != null) wrong.Add("switched off, it still answered");
                // Its three faults from the 10-conversation run: the note's time read out, replies past 25 words, chat taken for asks.
                if (!Brain.ReadsTime("Five-thirty-five. It smells faintly of burnt toast.") || !Brain.ReadsTime("Wednesday, November 25th, and you're home.") || !Brain.ReadsTime("It's 4:17, chill.")
                    || Brain.ReadsTime("Pizza with extra pepperoni. Don't overthink it.") || Brain.ReadsTime("Still alive. Just another Wednesday in the sprawl and I love it here."))
                    wrong.Add("a reply reading out the time wasn't told from one that answers");
                if (!Brain.AsksTime("what time is it") || !Brain.AsksTime("what's the date") || Brain.AsksTime("can I ask you something"))
                    wrong.Add("an ask for the time wasn't told from other lines");
                string kept25 = Brain.Within("Seriously? It's fascinating. A tiny little loop of data never settling down, like watching dust motes dance in sunlight, pointless but oddly compelling to watch for hours.", 25);
                if (kept25 != "Seriously? It's fascinating." || Brain.Within("Short and done.", 25) != "Short and done.")
                    wrong.Add("a reply wasn't kept to whole sentences within 25 words: " + kept25);
                if (!Brain.SmallBrain("gemma3:1b") || Brain.SmallBrain("gemma4:e4b-it-qat")) wrong.Add("the 1B wasn't read as a small brain, or E4B was");
            }
            finally
            {
                preferences.ConversationBrainOn = onWas;
                preferences.ConversationBrain = pickWas;
                ConvoInOllama = inOllamaWas;
                FallbackMachine = machineWas;
                localHeld = heldWas;
                Brain.Ollama = ollamaWas;
                fake.Stop();
            }
            if (wrong.Count > 0) throw new Exception("The conversation brain went wrong: " + string.Join("; ", wrong.ToArray()));
            checks.Add("PASS conversation brain: Gemma 3 1B judges what her rules miss with the framed eight-example ask and answers chat with no tools; a tool ask by her rules never reaches it, its \"tool\" or an error goes to her main brain; the other local brain is let go first (keep_alive 0); a PC that can't carry it never loads it; switched off it never answers; Get it shows 0.8 GB; a reply reading out her note's time goes to her main brain, the note's time reaches a small brain only when they ask it, and its replies keep to whole sentences within 25 words");
        }

        // The swap with a local main brain on a stand-in Ollama: idle, the main brain let go and the toothpick loaded; light
        // chat it answers, an aside goes to it; a tool ask, a long conversation or the main brain asked hands back, the
        // toothpick let go first.
        private void SmokeToothpickSwap(List<string> checks)
        {
            bool onWas = preferences.ConversationBrainOn;
            string pickWas = preferences.ConversationBrain, ollamaWas = Brain.Ollama, heldWas = localHeld, modelWas = brain.Model, serverWas = brain.Server;
            Cloud remoteWas = brain.Remote;
            Engine localWas = brain.Local;
            Func<bool> inOllamaWas = ConvoInOllama;
            Func<Machine> machineWas = FallbackMachine;
            List<string> asked = new List<string>();
            System.Net.Sockets.TcpListener fake;
            string none = "{\"message\":{\"role\":\"assistant\",\"content\":\"\"},\"done_reason\":\"stop\"}";
            Func<string, string> says = delegate(string words) { return "{\"message\":{\"role\":\"assistant\",\"content\":\"" + words + "\"},\"done_reason\":\"stop\"}"; };
            Brain.Ollama = FakeServer(new string[] { none, none, says("chat"), says("Mm, cosy."), none, none, none, none, none, none, says("An idle line."), none, says("Main line."), none, none }, asked, out fake);
            List<string> wrong = new List<string>();
            const string main = "gemma4:e4b-it-qat";
            Func<int, string> sent = delegate(int at) { lock (asked) return at < asked.Count ? asked[at] : ""; };
            Func<string, string, bool> letGo = delegate(string one, string model) { return one.IndexOf("\"keep_alive\":\"0s\"", StringComparison.Ordinal) >= 0 && one.IndexOf("\"" + model + "\"", StringComparison.Ordinal) >= 0; };
            Func<string> answer = delegate { return new Brain { Model = "stand-in" }.Aside("Talk.", "nice evening", 40); };
            Action idle = delegate { lastTalkAt = clock.Elapsed.TotalSeconds - SwapIdle - 1; };
            try
            {
                preferences.ConversationBrainOn = true;
                preferences.ConversationBrain = ConvoName;
                ConvoInOllama = delegate { return true; };
                FallbackMachine = delegate { return new Machine { Vram = 8L << 30, Ram = 16L << 30 }; };
                brain.Model = main;
                brain.Remote = null;
                brain.Server = null;
                brain.Local = null;
                localHeld = null;
                idle();
                SwapIn();
                if (!toothpickIn || !letGo(sent(0), main) || sent(1).IndexOf("\"keep_alive\":\"1h\"", StringComparison.Ordinal) < 0 || sent(1).IndexOf(ConvoOllama, StringComparison.Ordinal) < 0)
                    wrong.Add("idle didn't let the main brain go and load the toothpick: " + Clipped(sent(0), 100) + " / " + Clipped(sent(1), 100));
                string chat = ToothpickAnswer("nice evening", false, answer);
                if (chat != "Mm, cosy." || !toothpickIn) wrong.Add("light chat wasn't the toothpick's: " + chat);
                if (ToothpickAnswer("open discord", true, answer) != null || toothpickIn || !letGo(sent(4), ConvoOllama)) wrong.Add("a tool ask didn't hand back with the toothpick let go: " + Clipped(sent(4), 100));
                idle();
                SwapIn();
                toothpickTurns = DragTurns;
                if (ToothpickAnswer("and another thing", false, answer) != null || toothpickIn || !letGo(sent(7), ConvoOllama)) wrong.Add("a long conversation didn't hand back: " + Clipped(sent(7), 100));
                idle();
                SwapIn();
                string aside = brain.Aside("Write one idle line.", "go", 60);
                if (aside != "An idle line." || sent(10).IndexOf(ConvoOllama, StringComparison.Ordinal) < 0) wrong.Add("an idle aside didn't go to the toothpick: " + aside + " / " + Clipped(sent(10), 100));
                Action mainAsked = Brain.MainAsked;
                if (mainAsked != null) mainAsked();
                if (toothpickIn || !letGo(sent(11), ConvoOllama)) wrong.Add("her main brain asked didn't let the toothpick go first");
                int before = asked.Count;
                FallbackMachine = delegate { return new Machine { Vram = 0, Ram = 1L << 30 }; };
                idle();
                SwapIn();
                if (toothpickIn || asked.Count != before) wrong.Add("a PC that can't carry the toothpick swapped to it");
                // A main-brain ask by any path, here an aside, is her using it: no swap; a real idle after it still swaps.
                FallbackMachine = delegate { return new Machine { Vram = 8L << 30, Ram = 16L << 30 }; };
                idle();
                string busy = brain.Aside("Write one line.", "go", 60);
                int afterAsk = asked.Count;
                SwapIn();
                bool kept = !toothpickIn && asked.Count == afterAsk && busy == "Main line.";
                idle();
                SwapIn();
                if (!kept || !toothpickIn || !letGo(sent(afterAsk), main)) wrong.Add("a main-brain ask didn't count as her using it, or a real idle after it didn't swap: kept " + kept + " (" + busy + "), swapped " + toothpickIn);
            }
            finally
            {
                toothpickIn = false;
                toothpickTurns = 0;
                preferences.ConversationBrainOn = onWas;
                preferences.ConversationBrain = pickWas;
                ConvoInOllama = inOllamaWas;
                FallbackMachine = machineWas;
                localHeld = heldWas;
                brain.Model = modelWas;
                brain.Remote = remoteWas;
                brain.Server = serverWas;
                brain.Local = localWas;
                Brain.Ollama = ollamaWas;
                fake.Stop();
            }
            if (wrong.Count > 0) throw new Exception("The toothpick swap went wrong: " + string.Join("; ", wrong.ToArray()));
            checks.Add("PASS toothpick swap (372): with a local main brain, idle lets the main brain go (keep_alive 0) and loads the toothpick; light chat is the toothpick's and an aside goes to it; a tool ask, a long conversation or the main brain asked hands back with the toothpick let go first; any main-brain ask counts as her using it, so it never swaps then, and a real idle after still does; a PC that can't carry it never swaps");
        }
#endif
    }
}
