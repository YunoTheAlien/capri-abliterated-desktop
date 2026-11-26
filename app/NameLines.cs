using System;
using System.Collections.Generic;

namespace CapriDesktop
{
    // Checks for her lines after her name, praise tails only after an act she liked, work breaks after a long spell in
    // a work app, check-ins due close together said as one, and Mute her kept across a restart.
    internal sealed partial class Companion
    {
        // A meme in parts: split at its pauses, each part a meme, the pauses timed, and the rest dropped once they talk to her.
#if CHECKS
        private void SmokePausedMeme(List<string> checks)
        {
            const string npc = Sayings.MemeMark + "NPC Capri brain, will... remember that #wait20s# NPC Capri brain, will..... remember that #wait5s# NPC Capri brain, will......... remember that";
            if (Sayings.Text.IndexOf(npc, StringComparison.Ordinal) < 0) throw new Exception("The NPC Capri brain meme isn't in her funny pool.");
            List<KeyValuePair<double, string>> parts = Sayings.Parts(npc);
            if (parts.Count != 3 || parts[0].Key != 0 || parts[1].Key != 20 || parts[2].Key != 5 || !parts.TrueForAll(one => Sayings.IsMeme(one.Value))
                || Sayings.Unmeme(parts[2].Value) != "NPC Capri brain, will......... remember that" || Sayings.Parts("one line").Count != 1)
                throw new Exception("A line in parts didn't split at its pauses: " + parts.Count);
            List<KeyValuePair<double, string>> plain = Sayings.Parts("Hold on #wait 2,5s# got it #WAIT1.5s# done");
            if (plain.Count != 3 || plain[1].Key != 2.5 || plain[2].Key != 1.5 || plain.Exists(one => one.Value.IndexOf('#') >= 0 || Sayings.IsMeme(one.Value)))
                throw new Exception("A plain line's pauses weren't taken out of its parts: " + plain.Count);
            // In their language: a meme part goes out as written and is never handed to be translated or kept in her
            // translations file; a plain line's parts are translated one by one, with no pause in them.
            string folderWas = CapriDesktop.Said.Folder, tongueWas = Brain.Language;
            Func<string, string, string> askWas = CapriDesktop.Said.Ask;
            Func<bool> readyWas = CapriDesktop.Said.Ready;
            string saidFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "capri-parts-" + Guid.NewGuid().ToString("N"));
            try
            {
                System.IO.Directory.CreateDirectory(saidFolder);
                CapriDesktop.Said.Folder = saidFolder;
                CapriDesktop.Said.Reload();
                CapriDesktop.Said.Ready = delegate { return false; };
                CapriDesktop.Said.Ask = delegate(string system, string user) { return ""; };
                Brain.Language = "Spanish";
                SayOwn("check-in", parts[0].Value);
                bool untouched = !CapriDesktop.Said.Knows(parts[0].Value) && !CapriDesktop.Said.Knows(Sayings.Unmeme(parts[0].Value));
                if (!untouched) throw new Exception("A meme part was learnt to be translated.");
            }
            finally
            {
                Brain.Language = tongueWas;
                CapriDesktop.Said.Ask = askWas;
                CapriDesktop.Said.Ready = readyWas;
                CapriDesktop.Said.Folder = folderWas;
                CapriDesktop.Said.Reload();
                CloseTalk();
                try { System.IO.Directory.Delete(saidFolder, true); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
            List<string> heard = new List<string>();
            System.Diagnostics.Stopwatch took = System.Diagnostics.Stopwatch.StartNew();
            List<double> when = new List<double>();
            Action<string, string> saidWas = partSaid;
            try
            {
                partSaid = delegate(string shown, string part) { heard.Add(part); when.Add(took.Elapsed.TotalSeconds); };
                SayParts("check-in", Sayings.MemeMark + "one #wait0.4s# two #wait0.3s# three");
                for (int i = 0; i < 100 && heard.Count < 3; i++) { System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(20); }
                bool timed = heard.Count == 3 && Sayings.Unmeme(heard[2]) == "three" && when[1] >= 0.35 && when[2] - when[1] >= 0.25;
                heard.Clear();
                SayParts("check-in", Sayings.MemeMark + "one #wait0.3s# two");
                typer.Visible = true;
                for (int i = 0; i < 40; i++) { System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(20); }
                typer.Visible = false;
                bool cut = heard.Count == 1;
                if (!timed || !cut) throw new Exception("A line in parts wasn't timed, or went on while they were typing to her: timed " + timed + ", cut " + cut);
            }
            finally { partSaid = saidWas; typer.Visible = false; }
            checks.Add("PASS a line in parts: \"NPC Capri brain, will... remember that\" is in her funny pool, said in three parts 20 s and then 5 s apart, each said as written and never translated; a plain line's parts carry no pause into a translation, and #wait 2,5s# reads as 2.5 s; a part waits its pause, and the rest is dropped once they're typing or talking to her");
        }
#endif

        // A meme part with a voice cue: the cue acts in her voice as pitch, pace and loudness, or as Fish Audio's nearest
        // cue, and never shows or is kept; the words stay as written, and each part takes its own cue.
#if CHECKS
        private void SmokeCuedMeme(List<string> checks)
        {
            string line = Sayings.MemeMark + "I went to the store to buy a croissant. #wait0.5s# (squeaky) QUASSO!";
            List<KeyValuePair<double, string>> parts = Sayings.Parts(line);
            string first = parts.Count == 2 ? Sayings.Unmeme(parts[0].Value) : "", second = parts.Count == 2 ? Sayings.Unmeme(parts[1].Value) : "";
            if (parts.Count != 2 || Voice.FeelingOf(first) != null || Voice.FeelingOf(second) != "squeaky" || Voice.Uncued(first) != "I went to the store to buy a croissant." || Voice.Uncued(second) != "QUASSO!")
                throw new Exception("A meme's parts didn't each keep their own cue and their words as written: " + first + " / " + second);
            if (Voice.FeelingOf("(high pitch voice) QUASSO!") != "high pitch voice" || Voice.Uncued("(high pitch voice) QUASSO!") != "QUASSO!")
                throw new Exception("\"(high pitch voice)\" isn't a voice cue.");
            int pitch, pace, loud, highPitch, highPace, highLoud;
            if (!VoiceFeel.Shift("squeaky", out pitch, out pace, out loud) || !VoiceFeel.Shift("high pitch voice", out highPitch, out highPace, out highLoud)
                || pitch != 3 || highPitch != 3 || pace <= 100 || loud <= 100 || highPace <= 100 || highLoud <= 100)
                throw new Exception("A high voice cue isn't three semitones up with pace and loudness a little up: " + pitch + ", " + pace + ", " + loud);
            if (VoiceServer.Rounded("(squeaky) QUASSO!") != "(excited) QUASSO!") throw new Exception("Fish Audio's s1 didn't get the nearest cue: " + VoiceServer.Rounded("(squeaky) QUASSO!"));
            bool silentWas = preferences.Silent;
            try
            {
                preferences.Silent = true;
                Remark("", Sayings.MemeMark + "(squeaky) QUASSO!", true);
                Func<bool> kept = delegate { return brain.History.Count > 0 && Sayings.Unmeme(brain.History[brain.History.Count - 1].Content) == "QUASSO!"; };
                for (int i = 0; i < 250 && !kept(); i++) { System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(20); }
                if (!kept()) throw new Exception("A meme's voice cue showed or was kept: " + (brain.History.Count > 0 ? brain.History[brain.History.Count - 1].Content : "nothing kept"));
            }
            finally { preferences.Silent = silentWas; CloseTalk(); }
            checks.Add("PASS a meme part's voice cue: \"(squeaky)\" and \"(high pitch voice)\" are cues, three semitones up with pace and loudness a little up, Fish Audio's s1 hears its nearest, \"(excited)\"; each part of a line in parts takes its own, the words stay as written, and the cue never shows or is kept");
        }
#endif

        // A question asked again gets "I just told you" at most once in four hours, kept across a restart; a request,
        // undo or redo is never counted.
#if CHECKS
        private void SmokeJustTold(List<string> checks)
        {
            Func<long> savedWas = Brain.ToldSaved;
            Action<long> keepWas = Brain.ToldKeep;
            TimeSpan offsetWas = SimTime.Offset;
            long told = 0;
            try
            {
                Brain.ToldSaved = delegate { return told; };
                Brain.ToldKeep = delegate(long at) { told = at; };
                Brain asker = new Brain();
                asker.AskedAgain("what is your favourite colour?");
                bool first = asker.AskedAgain("what's your favourite colour?").Length > 0 && told > 0;
                long when = told;
                Brain restarted = new Brain();
                restarted.AskedAgain("what was the name of that film?");
                bool held = restarted.AskedAgain("what was the name of that film?").Length == 0 && told == when;
                SimTime.Advance(TimeSpan.FromHours(4) + TimeSpan.FromMinutes(1));
                Brain later = new Brain();
                later.AskedAgain("where did you put the keys?");
                bool again = later.AskedAgain("where did you put the keys?").Length > 0 && told > when;
                told = 0;
                Brain asks = new Brain();
                bool requests = true;
                foreach (string one in new[] { "can you undo that?", "can you undo that?", "could you redo it?", "could you redo it?", "can you open notepad?", "can you open notepad?", "will you please read it?", "will you please read it?" })
                    if (asks.AskedAgain(one).Length > 0) requests = false;
                if (!first || !held || !again || !requests || told != 0)
                    throw new Exception("\"I just told you\" wasn't held to once in four hours, or a request was counted: first " + first + ", held " + held + ", again after " + again + ", requests left out " + requests);
                checks.Add("PASS \"I just told you\": a question asked again gets it once, then not for four hours even after a restart (kept in her settings), and again after; undo, redo and any request are never counted, and nothing on screen counts");
            }
            finally
            {
                Brain.ToldSaved = savedWas;
                Brain.ToldKeep = keepWas;
                SimTime.Reset();
                SimTime.Advance(offsetWas);
            }
        }
#endif

        // She answers in: apart from I speak, saved on its own; Auto follows the language of each message, worked out here;
        // her voice follows the language a reply is in; the settings tool and VOICE's menu set it.
#if CHECKS
        private void SmokeAnswerLanguage(List<string> checks)
        {
            const string french = "Bonjour, comment \u00e7a va aujourd'hui ? Je suis tr\u00e8s contente.", spanish = "Hola, \u00bfc\u00f3mo est\u00e1s hoy? Estoy muy contenta.";
            string answersWas = preferences.AnswerLanguage, languageWas = Brain.Language, matchedWas = Brain.Matched;
            try
            {
                if (LanguagePair.Detect(french, "English") != "French" || LanguagePair.Detect(spanish, "English") != "Spanish" || LanguagePair.Detect("\u3053\u3093\u306b\u3061\u306f\u3001\u5143\u6c17\u3067\u3059\u304b", "English") != "Japanese")
                    throw new Exception("A line's language wasn't worked out: " + LanguagePair.Detect(french, "English") + ", " + LanguagePair.Detect(spanish, "English"));
                Brain.Language = null;
                SetAnswerLanguage(null);
                bool same = Brain.Speech() == "" && Brain.Speaking("Hello there, how are you today?") == null;
                SetAnswerLanguage("Spanish");
                mainframe.Sync();
                string firmSeen = Brain.Speech().Trim() + " / " + LoadPreferences().AnswerLanguage + " / " + Brain.Language + " / " + mainframe.AnswerShown + " / " + Brain.Speaking(spanish);
                bool firm = Brain.Speech().Contains("Always reply in Spanish") && LoadPreferences().AnswerLanguage == "Spanish" && Brain.Language == null && mainframe.AnswerShown == "Spanish" && Brain.Speaking(spanish) == "Spanish";
                SetAnswerLanguage(LanguagePair.Auto);
                mainframe.Sync();
                Brain.MatchLanguage(french);
                string autoSeen = Brain.Speech().Trim() + " / " + Brain.Speaking(french) + " / " + Brain.Speaking("Hello there, how are you doing today?") + " / " + mainframe.AnswerShown;
                bool auto = Brain.Speech().Contains("which is French") && Brain.Speaking(french) == "French" && Brain.Speaking("Hello there, how are you doing today?") == null && mainframe.AnswerShown == LanguagePair.AutoName;
                if (!same || !firm || !auto) throw new Exception("She answers in didn't keep apart from I speak: same " + same + ", Spanish " + firm + " (" + firmSeen + "), Auto " + auto + " (" + autoSeen + ")");
                int picked = Hands.StepFor("reply_language", "Spanish", 0), back = Hands.StepFor("reply_language", "same", 3), matching = Hands.StepFor("reply_language", "auto", 0);
                SetLevel("reply_language", picked);
                bool tool = AnswerLanguage == "Spanish" && LevelOf("reply_language") == picked && back == 0 && matching == 1;
                SetLevel("reply_language", 0);
                if (!tool || AnswerLanguage != null || mainframe.AnswerShown != LanguagePair.SameName) throw new Exception("The settings tool didn't set She answers in: " + picked + ", " + back + ", " + matching + ", " + AnswerLanguage);
                System.Windows.Forms.ContextMenuStrip menu = mainframe.ShowAnswers();
                int leaves = 0;
                foreach (System.Windows.Forms.ToolStripMenuItem item in Mainframe.LanguageItems(menu.Items)) if (!item.HasDropDownItems && item.Tag != null) leaves++;
                bool listed = menu.Items[0].Text == LanguagePair.SameName && menu.Items[1].Text == LanguagePair.AutoName && leaves == Brain.Languages.GetLength(0) + 2;
                menu.Close();
                if (!listed) throw new Exception("She answers in's menu doesn't lead with Same as I speak and Auto and hold every language once: " + leaves);
                checks.Add("PASS She answers in (VOICE): saved apart from I speak, which stays what they speak; Same as I speak follows it, a language holds her replies to it, Auto follows each message's language worked out on this PC; her voice follows the language a reply is in; \"talk to me in Spanish\" through her settings tool and VOICE's menu set it");
            }
            finally
            {
                preferences.AnswerLanguage = answersWas;
                Brain.Answers = LanguagePair.Answers(answersWas);
                Brain.Language = languageWas;
                Brain.Matched = matchedWas;
                SavePreferences();
                mainframe.Sync();
            }
        }
#endif

        // Text faces show in her bubble and no voice reads them; **silent** parts show without their marks and are never
        // said, on every voice path, and a translation can't drop the marks.
#if CHECKS
        private void SmokeTextFaces(List<string> checks)
        {
            string[,] faceless =
            {
                { "hi :<", "hi" }, { ">:3 nice one", "nice one" }, { "lol ^_^ ok T_T", "lol ok" }, { "<3 you", "you" }, { "sad :( but fine :D", "sad but fine" },
                { "at 3:30, ratio 2:1, a:b https://x.com/a:b <30 mins", "at 3:30, ratio 2:1, a:b https://x.com/a:b <30 mins" },
                { "XD that was funny uwu rawr owo sksksk xD", "XD that was funny uwu rawr owo sksksk xD" }, { ":3", "" },
            };
            for (int i = 0; i < faceless.GetLength(0); i++)
                if (Voice.Faceless(faceless[i, 0]) != faceless[i, 1]) throw new Exception("A text face wasn't taken out, or a word or time was: " + faceless[i, 0] + " -> " + Voice.Faceless(faceless[i, 0]));
            string quiet = "**silent**XD**/silent** hi :<";
            bool spoken = Voice.Spoken(quiet) == "hi" && Voice.Spoken(":3") == "" && Voice.Spoken("hi **silent** never said") == "hi" && Voice.Spoken(">:3") == ""
                && Voice.Spoken("[laughing] **silent**:3**/silent** ha", true) == "[laughing] ha" && Voice.Sayable("hi :< **silent**x**/silent**") == "hi"
                && Voice.Unsilenced(quiet) == "XD hi :<" && Voice.Unsilenced("ok **silent**:3**/silent**.") == "ok :3.";
            bool kept = Said.SameTokens("a **silent**x**/silent**", "b **silent**x**/silent**") && !Said.SameTokens("a **silent**x**/silent**", "b x");
            if (!spoken || !kept) throw new Exception("A silent part or a face reached her voice, or its marks reached her bubble: spoken " + spoken + " (" + Voice.Spoken(quiet) + " / " + Voice.Unsilenced(quiet) + "), translation " + kept);
            checks.Add("PASS text faces (:< :3 >:3 ^_^ T_T <3 and the like) stay in her bubble and no voice says them, a time, a ratio, a link and XD, uwu and rawr are left alone; **silent** parts show without their marks and are never said, Fish Audio's cue path too; a translation that drops a mark isn't kept");

            Func<string, byte[]> speakWas = Speak, freeWas = FreeSpeak;
            bool quietWas = preferences.Quiet, silentWas = preferences.Silent, serverWas = preferences.ServerVoice, brokeWas = voiceBroke, shushedWas = shushed;
            List<string> voiced = new List<string>();
            try
            {
                byte[] wav = Voice.Wave(new short[12000], 24000);
                Speak = delegate(string words) { lock (voiced) voiced.Add(words); return wav; };
                FreeSpeak = null;
                preferences.Quiet = preferences.Silent = preferences.ServerVoice = voiceBroke = shushed = false;
                Func<string, string, string> say = delegate(string line, string shown)
                {
                    CloseTalk();
                    voiceUntil = 0;
                    lock (voiced) voiced.Clear();
                    Remark("", line);
                    System.Diagnostics.Stopwatch waited = System.Diagnostics.Stopwatch.StartNew();
                    while (waited.ElapsedMilliseconds < 8000 && reply != shown) { System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(15); }
                    lock (voiced) return reply + " | " + string.Join(",", voiced.ToArray());
                };
                string tagged = say(quiet, "XD hi :<"), face = say(":3", ":3");
                CloseTalk();
                if (tagged != "XD hi :< | hi" || face != ":3 | ") throw new Exception("Her bubble and her voice didn't split a silent part or a face: " + tagged + " / " + face);
                checks.Add("PASS on her voice: a line with a silent part and a face shows them in her bubble and her voice says only the words, and a line that is only a face shows it and says nothing");
            }
            finally
            {
                Speak = speakWas;
                FreeSpeak = freeWas;
                preferences.Quiet = quietWas; preferences.Silent = silentWas; preferences.ServerVoice = serverWas; voiceBroke = brokeWas; shushed = shushedWas;
                CloseTalk();
            }
        }
#endif

        // Her replies in another language: she speaks of herself as a woman where the grammar shows it, and a reply in
        // English or half English while she is held to a language is asked again once, in it.
#if CHECKS
        private void SmokeHeldLanguage(List<string> checks)
        {
            string answersWas = preferences.AnswerLanguage, languageWas = Brain.Language, matchedWas = Brain.Matched, openAiWas = Brain.OpenAi;
            System.Net.Sockets.TcpListener server = null;
            try
            {
                const string french = "Je suis pr\u00eate. On y va tout de suite, tu vas adorer.", english = "I'm ready. Let's go right now, you're going to love it.";
                bool lines = LanguagePair.AsHerself("French").Contains("pr\u00eate") && LanguagePair.AsHerself("Polish").Contains("zm\u0119czona") && LanguagePair.AsHerself("Hindi").Length > 0
                    && LanguagePair.AsHerself("English") == "" && LanguagePair.AsHerself("German") == "";
                const string onlyHer = "That is about you only: speak of them as your notes about them say, and where your notes don't say, in ";
                bool them = LanguagePair.AsHerself("French").Contains(onlyHer + "French's usual forms for someone whose gender you don't know")
                    && LanguagePair.AsHerself("Brazilian Portuguese").Contains(onlyHer + "Brazilian Portuguese's") && !System.Text.RegularExpressions.Regex.IsMatch(LanguagePair.AsHerself("French"), @"\b(tu es|t'es) \w+");
                Brain.Language = "French";
                SetAnswerLanguage(null);
                bool firm = Brain.Speech().Contains("feminine forms") && Brain.Speech().Contains(onlyHer) && Brain.HeldTo() == "French";
                SetAnswerLanguage(LanguagePair.Auto);
                Brain.MatchLanguage("Cze\u015b\u0107, jak si\u0119 dzisiaj masz? Co robisz?");
                bool auto = Brain.Speech().Contains("in Polish, use the feminine forms") && Brain.Speech().Contains(onlyHer + "Polish's") && Brain.HeldTo() == "Polish";
                SetAnswerLanguage(null);
                if (!lines || !them || !firm || !auto) throw new Exception("Her prompt doesn't say she speaks as a woman, of herself only: lines " + lines + ", them " + them + ", French " + firm + ", Auto " + auto + " (" + Brain.Speech().Trim() + ")");
                bool told = !LanguagePair.EnglishIn(french, "French") && LanguagePair.EnglishIn(english, "French") && LanguagePair.EnglishIn(french + " But honestly that is what I wanted to say.", "French")
                    && !LanguagePair.EnglishIn("Salut choom, preem ! On joue ?", "French") && !LanguagePair.EnglishIn(english, "English");
                if (!told) throw new Exception("A reply in English or half English wasn't told from one in French.");
                checks.Add("PASS her prompt tells a language that shows who is speaking that she is a woman, with its own feminine forms (French, Polish, Hindi and the rest), on She answers in and on Auto, and that it is about her only: them as her notes say, else the language's usual forms for someone whose gender isn't known; English and German get no line");
                string[][] scripts = { new[] { english, french }, new[] { english, english }, new[] { french }, new[] { english } };
                string[] languages = { "French", "French", "French", null };
                string[] expected = { french, english, french, english };
                int[] calls = { 2, 2, 1, 1 };
                for (int run = 0; run < scripts.Length; run++)
                {
                    List<string> asked = new List<string>();
                    string[] replies = Array.ConvertAll(scripts[run], one => LocalReply(one));
                    Brain.OpenAi = FakeServer(replies, asked, out server);
                    Brain.Language = languages[run];
                    Brain talker = new Brain { Remote = new Cloud("sk-proj-test", "gpt-5-mini"), Model = "gpt-5-mini" };
                    string said = talker.Ask("on y va ?");
                    int count;
                    lock (asked) count = asked.Count;
                    bool redoAsked = false;
                    lock (asked) if (count > 1) redoAsked = asked[1].Contains("all in French");
                    server.Stop();
                    server = null;
                    if (said != expected[run] || count != calls[run] || (calls[run] > 1 && !redoAsked))
                        throw new Exception("Run " + run + ": a reply held to " + (languages[run] ?? "English") + " wasn't asked again once: " + said + ", " + count + " calls");
                }
                checks.Add("PASS a reply in English while she is held to French is asked again once, all in French: the French retry is kept, an English retry leaves the first reply and asks no third time, a French reply is never asked again, and with English picked nothing is");
            }
            finally
            {
                if (server != null) server.Stop();
                Brain.OpenAi = openAiWas;
                preferences.AnswerLanguage = answersWas;
                Brain.Answers = LanguagePair.Answers(answersWas);
                Brain.Language = languageWas;
                Brain.Matched = matchedWas;
                SavePreferences();
            }
        }
#endif

        // A meme names its source at the top of her bubble while she says it, and one comes in context (a gift while she's
        // hungry) once her funny timer is half way to its next time and that line hasn't been said for three days.
#if CHECKS
        private void SmokeMemeSources(List<string> checks)
        {
            const string tagged = Sayings.MemeMark + "{SAO Ab} We must save my family!!";
            List<KeyValuePair<double, string>> parts = Sayings.Parts(Sayings.MemeMark + "{SAO Ab} one #wait1s# two");
            bool read = Sayings.MemeSource(tagged) == "SAO Ab" && Sayings.Unmeme(tagged) == "We must save my family!!" && Sayings.MemeSource(Sayings.MemeMark + "no u") == null
                && Sayings.MemeSource("{SAO Ab} not a meme") == null && parts.Count == 2 && parts.TrueForAll(one => Sayings.MemeSource(one.Value) == "SAO Ab")
                && Sayings.Unmeme(parts[0].Value) == "one" && Sayings.Unmeme(parts[1].Value) == "two";
            if (!read) throw new Exception("A meme's source wasn't read, taken off or kept on each part.");
            CloseTalk();
            Remark("", tagged, true, null, false, "> sys: ");
            string shown = box.Prompt;
            CloseTalk();
            Remark("", Sayings.MemeMark + "no u", true, null, false, "> sys: ");
            string plain = box.Prompt;
            CloseTalk();
            if (shown != "> SAO Ab: " || plain != "> sys: ") throw new Exception("Her bubble's top line didn't name the meme's source: " + shown + " / " + plain);
            checks.Add("PASS a meme names its source in braces after the mark (~{SAO Ab}); her bubble's top line shows it while she says it, on every part of a line in parts, and an untagged meme keeps the line it had");

            const string moment = "you give her a gift while hungry";
            TimeSpan offsetWas = SimTime.Offset;
            long fedWas = preferences.FedAt;
            Action unsay = Sayings.HeldSaid();
            try
            {
                double half = Sayings.FunnyRest / 2;
                Sayings.Rested("sys:funny", 0);
                string early = Sayings.InContext(moment, 1);
                SimTime.Advance(TimeSpan.FromHours(half + 0.05));
                string due = Sayings.InContext(moment, 1);
                string straightAfter = Sayings.InContext(moment, 1);
                SimTime.Advance(TimeSpan.FromHours(half + 0.05));
                string sameLine = Sayings.InContext(moment, 1);
                SimTime.Advance(TimeSpan.FromDays(3));
                string later = Sayings.InContext(moment, 1);
                if (early != null || due == null || Sayings.MemeSource(due) != "SAO Ab" || Sayings.Unmeme(due) != "HOW DO I EAT YOU!!!" || straightAfter != null || sameLine != null || later != due)
                    throw new Exception("A meme in context came at the wrong time: before half way " + early + ", half way " + due + ", straight after " + straightAfter + ", the same line within three days " + sameLine + ", three days on " + later);
                preferences.FedAt = (SimTime.UtcNow - TimeSpan.FromHours(30)).Ticks;
                bool hungry = HungerWord == "hungry";
                preferences.FedAt = SimTime.UtcNow.Ticks;
                if (!hungry || HungerWord == "hungry") throw new Exception("Hungry wasn't read as her Feed her menu reads it.");
                checks.Add("PASS a meme in context: \"HOW DO I EAT YOU!!!\" (SAO Ab) for a gift while she's hungry comes only once her funny timer is half way to its next time, starts that timer again, and isn't said twice within three days");
            }
            finally
            {
                SimTime.Reset();
                SimTime.Advance(offsetWas);
                preferences.FedAt = fedWas;
                unsay();
                Sayings.Rested("sys:funny", 0);
            }
        }
#endif

#if CHECKS
        // A line from outside her pools, like her interests file's check-in openers, keeps the same said-lately rest.
        private void SmokeNotLately(List<string> checks)
        {
            string[] openers = { "You are very distracting from over here, you know. zq1", "Every time you lean back like that I lose my train of thought. zq2", "Come and keep me company for a bit. zq3" };
            Action unsay = Sayings.HeldSaid();
            Func<double> paceWas = Sayings.RestPace;
            try
            {
                Sayings.Remember(openers[0]);
                Sayings.Remember(openers[1]);
                int at = 0;
                List<string> picked = new List<string>();
                for (int i = 0; i < 6; i++) picked.Add(Sayings.NotLately(delegate { return openers[at++ % openers.Length]; }));
                if (picked[0] != openers[2] || !Sayings.SaidLately(openers[2]))
                    throw new Exception("A check-in opener said lately came up again: " + string.Join(" / ", picked.ToArray()));
                // The rest follows her pace: twice as slow a pace rests a line twice as long, half as slow half as long.
                double stock = Sayings.RestHours;
                Sayings.RestPace = delegate { return 2; };
                double slow = Sayings.RestHours;
                Sayings.RestPace = delegate { return 0.5; };
                double quick = Sayings.RestHours;
                Sayings.RestPace = paceWas;
                if (Math.Abs(slow - Sayings.RecentHours * 2) > 0.01 || Math.Abs(quick - Sayings.RecentHours * 0.5) > 0.01)
                    throw new Exception("The said-lately rest didn't follow her pace: stock " + stock + " h, slow " + slow + " h, quick " + quick + " h");
                checks.Add("PASS her check-in openers keep a said-lately rest: one said within it is passed over while another comes up, the one picked is remembered, and the rest is RecentHours at her personality's and mood's paces (twice as slow a pace, twice as long)");
            }
            finally { Sayings.RestPace = paceWas; unsay(); }
        }
#endif

#if CHECKS
        // Each moment a meme waits for has its section, and each section's memes come only at their moment.
        private void SmokeMemeMoments(List<string> checks)
        {
            string[][] moments =
            {
                new[] { "you pat her", "awa awa" }, new[] { "you tickle her", "rawr xD" }, new[] { "you pick her up", "smol bean protocol activated" },
                new[] { "you throw her", "RIDONCULOUS!!!" }, new[] { "she is dizzy", "sksksk and i oop" }, new[] { "you praise her", ">:3" },
                new[] { "you insult her", "no u" }, new[] { "you talk to her", "ehe? ehe what? ehe nothing" },
                new[] { "you say her name", "i could be your oshi. just saying. the position is open" }, new[] { "she wakes to her name", "omg hiiii bestie" },
                new[] { "you ignore her", "not me getting attached to a taskbar" }, new[] { "you come back", "ara ara. someone's been busy" },
                new[] { "it is bedtime", "bonk. go to bed" }, new[] { "you feed her a treat", "QUASSO!" }, new[] { "you ask her to remember", "NPC Capri brain" },
                new[] { "you die in a game", "You were so close!" }, new[] { "you die again in a game", "We must save my family!!" }, new[] { "you win a game", "ngl that went hard" }
            };
            TimeSpan offsetWas = SimTime.Offset;
            string wordsWas = wordsMoment;
            double wordsAtWas = wordsMomentAt;
            int ticketWas = brainTicket;
            double nameWas = nameCalledAt;
            Action unsay = Sayings.HeldSaid();
            try
            {
                double half = Sayings.FunnyRest / 2;
                string wrong = null;
                // Past anything said earlier in the run, so each meme is due.
                SimTime.Advance(TimeSpan.FromDays(3.1));
                foreach (string[] moment in moments)
                {
                    Sayings.Rested("sys:funny", 0);
                    SimTime.Advance(TimeSpan.FromHours(half + 0.05));
                    string meme = Sayings.InContext(moment[0], 1);
                    string[] section = Sayings.Pool(Sayings.InContextPrefix + moment[0]) ?? new string[0];
                    bool held = Array.Exists(section, line => Sayings.Unmeme(line).IndexOf(moment[1], StringComparison.Ordinal) >= 0);
                    if (meme == null || !Sayings.IsMeme(meme) || Array.IndexOf(section, meme) < 0 || !held) wrong = wrong ?? moment[0] + " gave " + (meme ?? "nothing");
                }
                if (wrong != null) throw new Exception("A moment had no meme of its own: " + wrong);
                string[] funnies = Sayings.Pool("funny") ?? new string[0];
                foreach (string[] moment in moments)
                    foreach (string line in Sayings.Pool(Sayings.InContextPrefix + moment[0]))
                        if (Array.IndexOf(funnies, line) >= 0) throw new Exception("A meme with its moment is still among her funny lines: " + line);
                // An insult while she is furious falls back to an insult's meme when the furious one has none on this side.
                SimTime.Advance(TimeSpan.FromDays(3.1));
                Sayings.Rested("sys:funny", 0);
                SimTime.Advance(TimeSpan.FromHours(Sayings.FunnyRest * Math.Max(1, ChatterStretch)));
                brainTicket = 7;
                nameCalledAt = -1;
                wordsMoment = "you insult her while she is furious";
                wordsMomentAt = clock.Elapsed.TotalSeconds;
                string reply = NameLine(7, "rude.");
                string[] furious = Sayings.Pool(Sayings.InContextPrefix + "you insult her while she is furious" + LineFile.SenpaiMark) ?? new string[0];
                if (reply != "no u rude." && !Array.Exists(furious, line => reply == Sayings.Unmeme(line) + " rude.")) throw new Exception("An insult's meme didn't open her reply: " + reply);
                checks.Add("PASS memes in context: each of the 18 moments a meme waits for has its own section and its meme comes there, none of them is left among her funny lines, and an insult's meme opens her reply to it, a furious insult falling back to it");
            }
            finally
            {
                SimTime.Reset();
                SimTime.Advance(offsetWas);
                wordsMoment = wordsWas;
                wordsMomentAt = wordsAtWas;
                brainTicket = ticketWas;
                nameCalledAt = nameWas;
                unsay();
                Sayings.Rested("sys:funny", 0);
            }
        }
#endif

#if CHECKS
        private void SmokeNameLines(List<string> checks)
        {
            Dictionary<string, List<string>> stock = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            string under = null;
            foreach (string raw in Sayings.Text.Split('\n'))
            {
                string row = raw.Trim();
                if (row.StartsWith("## ", StringComparison.Ordinal)) { under = row.Substring(3); stock[under] = new List<string>(); continue; }
                if (under != null && row.Length > 0 && !row.StartsWith(">", StringComparison.Ordinal)) stock[under].Add(row.StartsWith("- ", StringComparison.Ordinal) ? row.Substring(2) : row);
            }
            List<string> warm, praise, name;
            if (!stock.TryGetValue("tails warm", out warm) || !stock.TryGetValue("tails praise", out praise) || !stock.TryGetValue("said my name", out name)
                || warm.Exists(one => one == "credit where it's due." || one == "nice one." || one == "ten out of ten." || one == "well played." || one == "good call." || one == "you've earned it.")
                || !praise.Contains("credit where it's due.") || !name.Contains("I love it when you say my name."))
                throw new Exception("lines.md's tails warm still holds praise, or tails praise or said my name is missing.");
            string[] praising = { "credit where it's due", "ten out of ten", "well played", "you've earned it", "that'll do nicely", "consider me won over" };
            bool plainPraised = false;
            for (int i = 0; i < 120 && !plainPraised; i++)
            {
                string said = Sayings.Plainly("come back to me", true, Sayings.Kind.Own) ?? "";
                foreach (string one in praising) if (said.IndexOf(one, StringComparison.OrdinalIgnoreCase) >= 0) plainPraised = true;
            }
            if (plainPraised) throw new Exception("A warm line she brought up herself took a praise tail.");
            checks.Add("PASS lines.md: tails warm keeps the fond tails and tails praise holds the ones that praise something they did, used only after an act of theirs she liked; a line she brings up herself never takes one; said my name holds \"I love it when you say my name\"");

            double warmWas = Mood.Warmth, upsetWas = Mood.Upset;
            int ticketWas = brainTicket;
            try
            {
                Mood.Feel(0, 30);
                CalledOn("Capri, are you there?");
                bool cold = nameCalledAt < 0;
                Mood.Feel(30, 0);
                CalledOn("what time is it");
                bool unnamed = nameCalledAt < 0;
                brainTicket = 7;
                string opened = null;
                for (int i = 0; i < 40 && opened == null; i++)
                {
                    CalledOn("Capri, what time is it?");
                    string said = NameLine(7, "It's three.");
                    if (said != "It's three.") opened = said;
                }
                CalledOn("Capri, again?");
                string rested = NameLine(7, "Still three.");
                CalledOn("Capri?");
                string other = NameLine(8, "Not a reply.");
                if (!cold || !unnamed || opened == null || !opened.EndsWith(" It's three.", StringComparison.Ordinal) || !name.Exists(one => opened.StartsWith(one.TrimEnd('.'), StringComparison.Ordinal)) || rested != "Still three." || other != "Not a reply.")
                    throw new Exception("A line from said my name didn't open her reply only right after her name while she's very willing, once in two hours: cold " + cold + ", unnamed " + unnamed + ", opened " + opened + ", rested " + rested + ", other " + other);
                checks.Add("PASS said my name: only after they say her name, only while she's willing " + NameWilling + " or more, and at most once in two hours, a line like \"" + opened.Substring(0, opened.Length - " It's three.".Length) + "\" opens her reply; not when she's cold, not without her name, not on another line");
            }
            finally
            {
                Mood.Feel(warmWas, upsetWas);
                brainTicket = ticketWas;
                nameCalledAt = -1;
            }

            double? fromWas = workFrom, leftWas = workLeftAt;
            try
            {
                double now = clock.Elapsed.TotalSeconds;
                workFrom = workLeftAt = null;
                WorkSpell(now, true);
                bool started = workFrom == now && !WorkedLong;
                workFrom = now - WorkSpellMinutes * 60 - 1;
                bool breaks = false, plain = true;
                for (int i = 0; i < 40; i++)
                {
                    string stretch = CareLine(CareKind.Stretch), eyes = CareLine(CareKind.Eyes), water = CareLine(CareKind.Water);
                    if (careLines.Pick(Care.WorkBreak) == null) break;
                    if (IsBreak(stretch) || IsBreak(eyes)) breaks = true;
                    if (IsBreak(water)) plain = false;
                }
                WorkSpell(now, false);
                WorkSpell(now + 200, true);
                bool kept = WorkedLong;
                WorkSpell(now + 300, false);
                WorkSpell(now + 601, false);
                bool ended = !workFrom.HasValue;
                if (!started || !breaks || !plain || !kept || !ended)
                    throw new Exception("Work breaks didn't follow a long spell in a work app: started " + started + ", stretch or look away " + breaks + ", water plain " + plain + ", a short look away kept it " + kept + ", five minutes away ended it " + ended);
                checks.Add("PASS work breaks: after " + WorkSpellMinutes + " minutes with a work app in front her Stretch and Look away check-ins can be a work break (\"" + Care.WorkBreak + "\" in check-ins.md), others never; a look away under five minutes keeps the spell, five minutes ends it; with check-ins paused or at infinity there are none");
            }
            finally { workFrom = fromWas; workLeftAt = leftWas; }

            double careNow = 0;
            DateTime day = new DateTime(2026, 10, 30, 10, 0, 0);
            Preferences set = new Preferences();
            for (int kind = 0; kind < Care.Names.Length; kind++) set.SetCare((CareKind)kind, false, set.CareTime((CareKind)kind));
            set.SetCare(CareKind.Water, true, 30);
            set.SetCare(CareKind.Stretch, true, 33);
            set.SetCare(CareKind.Eyes, true, 50);
            Dictionary<string, string> joined = new Dictionary<string, string> { { "Water + Stretch", "both" } };
            Care care = new Care(delegate { return careNow; }, delegate { return day.AddSeconds(careNow); }, delegate(CareKind kind) { return kind.ToString(); },
                delegate(string heading) { string line; return joined.TryGetValue(heading, out line) ? line : null; });
            care.Check(true, set.CareOn, set.CareTime, ref set.CareNight, 0, true, null, -1, 1);
            careNow = 30 * 60;
            string first = care.Check(true, set.CareOn, set.CareTime, ref set.CareNight, 0, true, null, -1, 1);
            bool both = first == "both" && care.SaidAll.Count == 2 && care.SaidAll.Contains(CareKind.Stretch);
            careNow = 33 * 60;
            string after = care.Check(true, set.CareOn, set.CareTime, ref set.CareNight, 0, true, null, -1, 1);
            careNow = 50 * 60;
            string alone = care.Check(true, set.CareOn, set.CareTime, ref set.CareNight, 0, true, null, -1, 1);
            if (!both || after != null || alone != "Eyes")
                throw new Exception("Check-ins close together didn't come as one line: " + first + " (" + care.SaidAll.Count + "), then " + after + ", then " + alone);
            string[] heads = { "Water + Stretch", "Water + Look away", "Stretch + Look away", "Water + Stretch + Look away", "Water + Meals" };
            foreach (string head in heads)
                if (StockCare(head).Count < 3) throw new Exception("check-ins.md has no " + head + " lines.");
            checks.Add("PASS check-ins due within " + (int)(Care.Close / 60) + " minutes of each other come as one line from a heading naming them (" + string.Join(", ", heads) + " and the game break ones), the one joined early isn't said again on its own, and one due alone keeps its own line");

            bool keptWas = preferences.MuteKept;
            try
            {
                if (MuteOn) SetMute(false);
                SetMute(true);
                bool saved = LoadPreferences().MuteKept;
                SetMute(false);
                bool cleared = !LoadPreferences().MuteKept;
                SetQuiet(true);
                bool timerNot = !preferences.MuteKept;
                SetQuiet(false);
                if (!saved || !cleared || !timerNot) throw new Exception("Mute her wasn't kept across a restart until turned off: kept " + saved + ", cleared " + cleared + ", the Quiet timer not kept " + timerNot);
                checks.Add("PASS Mute her is kept in her settings while it's on, so it's still on after a restart until turned off; the Quiet timer isn't kept");
            }
            finally
            {
                if (MuteOn) SetMute(false);
                preferences.MuteKept = keptWas;
                SavePreferences();
            }
        }
#endif

        // The lines under one heading of check-ins.md as it ships.
        private static List<string> StockCare(string heading)
        {
            List<string> found = new List<string>();
            bool under = false;
            foreach (string raw in CareLines.Defaults.Split('\n'))
            {
                if (raw.StartsWith("#", StringComparison.Ordinal)) under = raw == "## " + heading;
                else if (under && raw.StartsWith("- ", StringComparison.Ordinal)) found.Add(raw.Substring(2));
            }
            return found;
        }

        private static bool IsBreak(string line) { return line != null && StockCare(Care.WorkBreak).Contains(line); }
    }
}
