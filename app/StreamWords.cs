using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CapriDesktop
{
    // Her reply's words shown as they arrive, a finished sentence at a time without tags or cues, and its first piece voiced
    // ahead so her voice plays as the reply lands. Not in another language, on a call, or for a meme.
    internal sealed partial class Companion
    {
        private int streamTicket = -1;
        private string streamText;
        // The first piece voiced ahead: its words, its sound and any problem, and when it is done.
        private volatile string aheadWords;
        private volatile byte[] aheadWav;
        private volatile string aheadProblem;
        private volatile ManualResetEvent aheadDone;

        private static readonly Regex SentenceEnd = new Regex(@"(?<=[.!?\u2026][""'\u201D\u2019)\]]*)\s+");

        // Up to the end of its last finished sentence: one followed by more words or a space.
        internal static string Finished(string text)
        {
            Match last = null;
            foreach (Match one in SentenceEnd.Matches(text ?? "")) last = one;
            return last == null ? "" : text.Substring(0, last.Index).TrimEnd();
        }

        // What her brain is told to call as the words come, or null when they wait for the whole reply.
        private Action<string> StreamTo(int ticket, bool quiet)
        {
            aheadDone = null;
            if (Brain.Language != null || CallModeOn) return null;
            string shown = "";
            bool ahead = false;
            return delegate(string soFar)
            {
                string done = Finished(Brain.Visible(Computer.Mask(soFar)));
                if (done.Length == 0 || done == shown || Sayings.IsMeme(done)) return;
                shown = done;
                Post(delegate { if (ticket == asking && talkState == TalkState.Thinking) { streamTicket = ticket; streamText = done; } });
                if (ahead) return;
                List<string> pieces = RemoteCall.Pieces(Voice.Spoken(done.Replace(Computer.Dots, "the password"), false));
                if (pieces.Count < 2) return;
                ahead = true;
                string first = pieces[0];
                Post(delegate { VoiceAhead(ticket, quiet, first); });
            };
        }

        // The first piece made in her free voice while the rest arrives. A paid voice isn't spent on words the whole reply
        // may still change.
        private void VoiceAhead(int ticket, bool quiet, string words)
        {
            if (ticket != asking || preferences.Silent) return;
            Func<string, byte[]> free;
            Func<string, byte[]> speak = PickVoice(quiet, out free);
            if (speak == null || speak != free || CuedVoice(speak, free)) return;
            ManualResetEvent done = new ManualResetEvent(false);
            aheadWords = words;
            aheadWav = null;
            aheadProblem = null;
            aheadDone = done;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string problem;
                    byte[] wav = VoicedNow(speak, free, words, out problem, true);
                    aheadWav = wav;
                    aheadProblem = problem;
                }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                finally { done.Set(); }
            });
        }

        // The piece voiced ahead, when these are its words in her free voice; null otherwise.
        private byte[] VoicedAhead(Func<string, byte[]> speak, Func<string, byte[]> free, string words, out string problem)
        {
            problem = null;
            ManualResetEvent ahead = aheadDone;
            if (ahead == null || speak == null || speak != free || words != aheadWords) return null;
            aheadDone = null;
            if (!ahead.WaitOne(30000) || aheadWav == null) return null;
            problem = aheadProblem;
            TurnTimes.Mark("voice made ahead");
            return aheadWav;
        }

        // Words shown as they arrived stay shown: the rest types on from them.
        private void StreamShown(int ticket, string said)
        {
            if (ticket == streamTicket && streamText != null && said != null && said.StartsWith(streamText, StringComparison.Ordinal))
                replyStart = clock.Elapsed.TotalSeconds - streamText.Length / Math.Max(1.0, pace);
            streamTicket = -1;
            streamText = null;
        }

#if CHECKS
        // Words as they arrive: Ollama's streamed lines and an OpenAI-style stream told piece by piece and joined whole, the
        // ask marked to stream; only finished sentences shown; the first piece voiced ahead once and used by her voice.
        private void SmokeStreamWords(List<string> checks)
        {
            List<string> wrong = new List<string>();
            string ollamaWas = Brain.Ollama;
            List<string> asked = new List<string>(), told = new List<string>();
            System.Net.Sockets.TcpListener fake;
            string lines = "{\"message\":{\"role\":\"assistant\",\"content\":\"Hi there. \"},\"done\":false}\n"
                + "{\"message\":{\"role\":\"assistant\",\"content\":\"How are you?\"},\"done\":false}\n"
                + "{\"message\":{\"role\":\"assistant\",\"content\":\"\"},\"done\":true,\"done_reason\":\"stop\",\"eval_count\":7}\n";
            Brain.Ollama = FakeServer(new[] { lines }, asked, out fake, 200, 0, false, 0, "application/x-ndjson");
            Func<string, byte[]> freeWas = FreeSpeak, speakWas = Speak;
            bool silentWas = preferences.Silent, quietWas = preferences.Quiet;
            Func<Func<bool>, bool> until = delegate(Func<bool> done)
            {
                System.Diagnostics.Stopwatch waited = System.Diagnostics.Stopwatch.StartNew();
                while (!done() && waited.ElapsedMilliseconds < 10000) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(20); }
                return done();
            };
            int asking0 = asking;
            TalkState stateWas = talkState;
            try
            {
                Brain.Pieces = delegate(string soFar) { lock (told) told.Add(soFar); };
                string said;
                try { said = new Brain { Model = "stand-in" }.Aside("Talk.", "hi", 60); }
                finally { Brain.Pieces = null; }
                string sent = asked.Count > 0 ? asked[0] : "";
                if (said != "Hi there. How are you?" || sent.IndexOf("\"stream\":true", StringComparison.Ordinal) < 0 || told.Count != 2 || told[0] != "Hi there. " || told[1] != "Hi there. How are you?")
                    wrong.Add("Ollama's stream: " + said + " | asked to stream " + (sent.IndexOf("\"stream\":true", StringComparison.Ordinal) >= 0) + " | told " + string.Join(" / ", told.ToArray()));
                told.Clear();
                string events = "data: {\"choices\":[{\"delta\":{\"content\":\"One. \"}}]}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"Two.\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n";
                Brain.Pieces = delegate(string soFar) { lock (told) told.Add(soFar); };
                CompletionReply streamed;
                try { using (MemoryStream incoming = new MemoryStream(Encoding.UTF8.GetBytes(events))) streamed = Brain.ReadCompletion(incoming); }
                finally { Brain.Pieces = null; }
                if (streamed.Choices[0].Message.Content != "One. Two." || told.Count != 2 || told[0] != "One. ") wrong.Add("an OpenAI-style stream: " + streamed.Choices[0].Message.Content + " | told " + string.Join(" / ", told.ToArray()));
                if (Finished("Hi there. How ar") != "Hi there." || Finished("Hm, well") != "" || Finished("Done! And (more.) Then") != "Done! And (more.)") wrong.Add("finished sentences: " + Finished("Hi there. How ar") + " | " + Finished("Done! And (more.) Then"));
                // Her bubble and her voice ahead, with a stand-in free voice.
                int made = 0;
                byte[] sound = Voice.Wave(new short[8000], 16000);
                preferences.Silent = false;
                preferences.Quiet = false;
                Speak = FreeSpeak = delegate(string words) { Interlocked.Increment(ref made); return sound; };
                asking = asking0 + 1000;
                talkState = TalkState.Thinking;
                Action<string> stream = StreamTo(asking, false);
                if (stream == null) wrong.Add("no stream for an ordinary reply");
                else
                {
                    stream("It is a lovely rainy evening out here");
                    stream("It is a lovely rainy evening out here. How has your day");
                    stream("It is a lovely rainy evening out here. How has your day been going so far? I hope");
                    until(() => streamText == "It is a lovely rainy evening out here. How has your day been going so far?" && aheadDone != null && aheadDone.WaitOne(0));
                    if (streamTicket != asking || streamText != "It is a lovely rainy evening out here. How has your day been going so far?") wrong.Add("her bubble wasn't given the finished sentences: " + streamText);
                    Func<string, byte[]> free;
                    Func<string, byte[]> speak = PickVoice(false, out free);
                    string problem;
                    byte[] wav = Voiced(speak, free, "It is a lovely rainy evening out here.", out problem);
                    if (made != 1 || wav == null) wrong.Add("the first piece wasn't voiced ahead once and used: made " + made + ", sound " + (wav != null));
                }
            }
            finally
            {
                Brain.Pieces = null;
                Brain.Ollama = ollamaWas;
                FreeSpeak = freeWas;
                Speak = speakWas;
                preferences.Silent = silentWas;
                preferences.Quiet = quietWas;
                asking = asking0;
                talkState = stateWas;
                streamTicket = -1;
                streamText = null;
                aheadDone = null;
                fake.Stop();
            }
            if (wrong.Count > 0) throw new Exception("Words as they arrive went wrong: " + string.Join("; ", wrong.ToArray()));
            checks.Add("PASS words as they arrive: Ollama is asked to stream and its lines are told piece by piece and joined whole, as an OpenAI-style stream is; only finished sentences show in her bubble while she thinks; the first piece is voiced ahead in her free voice once and her voice uses it when the reply lands");
        }
#endif
    }
}
