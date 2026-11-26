using System;
using System.Collections.Generic;

namespace CapriDesktop
{
    // Remember viewers: what a Twitch viewer likes to be called and whether she has seen them on another stream, from the
    // list in Viewers.cs. Off until turned on; off, nothing is read or written and she thanks as before.
    internal sealed partial class Companion
    {
        internal bool RememberViewersOn { get { return preferences.RememberViewers; } }

        internal void SetRememberViewers(bool on)
        {
            preferences.RememberViewers = on;
            if (on && string.IsNullOrEmpty(preferences.ViewerSalt)) preferences.ViewerSalt = Guid.NewGuid().ToString("N");
            SavePreferences();
        }

        // Forget viewers: a Yes in her bubble, then the list is emptied.
        internal void AskForgetViewers()
        {
            Remark("", "Forget every viewer I remember?", true);
            box.Choose(delegate(bool yes)
            {
                if (!yes) return;
                Viewers.Forget();
                Remark("", "Forgotten.", true);
            });
        }

        // Who was last seen on another stream, until her next reply to them says so.
        private readonly HashSet<string> seenElsewhere = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private string ViewerWhere(TwitchRules rules)
        {
            TwitchChat reading = twitchChat;
            return Viewers.Hash(reading != null ? reading.Channel : rules.Channel, preferences.ViewerSalt);
        }

        // A viewer's line in Read chat: their answer to her ask is kept and taken; otherwise they are marked as seen here.
        // True when the line was their answer.
        private bool ViewerLine(TwitchRules rules, TwitchLine line)
        {
            if (!preferences.RememberViewers || line == null || line.Notice != null || rules.IsBot(line.Login) || rules.Banned(line.Login)) return false;
            string who = (line.Login ?? "").Trim().TrimStart('@');
            if (who.Length == 0) return false;
            if (Viewers.Waiting(who))
            {
                string term = Viewers.Answer(who, line.Text, rules.Terms);
                if (term != null)
                {
                    string noted = term == Viewers.Neither ? "Noted." : "Noted. " + char.ToUpperInvariant(term[0]) + term.Substring(1) + ".";
                    Brain.Log("twitch", -1, who + " picked what to be called.");
                    Post(delegate { if (twitchChat != null) Remark("twitch " + who, "[" + who + "] " + noted); });
                    return true;
                }
            }
            if (Viewers.Seen(who, ViewerWhere(rules))) lock (seenElsewhere) seenElsewhere.Add(who);
            return false;
        }

        // Her brief on a donor: their own term when known; on a first donation a plain thanks and the ask to end it with;
        // after that one of the pet names, or no good boy or girl when there are none.
        private string ViewerCall(string login, TwitchRules rules, out string ask)
        {
            ask = null;
            if (!preferences.RememberViewers) return "";
            Viewer one = Viewers.Get(login);
            string call = one == null ? null : one.Call;
            if (call != null && call != Viewers.Neither) return "\nCall them a " + call + ".";
            if ((one == null || !one.Asked) && call == null && rules.Terms.Count > 0)
            {
                ask = Viewers.AskLine(rules.Terms);
                return "\nDon't call them a good boy, a good girl or any pet name; thank them plainly.";
            }
            if (rules.PetNames.Count > 0) { string pet; lock (random) pet = rules.PetNames[random.Next(rules.PetNames.Count)]; return "\nCall them a " + pet + "."; }
            return "\nDon't call them a good boy or a good girl.";
        }

        // What she remembers of a viewer for a reply in chat, in its own data block.
        private string ViewerKnown(string login)
        {
            if (!preferences.RememberViewers) return "";
            List<string> facts = new List<string>();
            Viewer one = Viewers.Get(login);
            if (one != null && one.Call != null && one.Call != Viewers.Neither) facts.Add("They like to be called a " + one.Call + ".");
            lock (seenElsewhere) if (seenElsewhere.Remove((login ?? "").Trim().TrimStart('@'))) facts.Add("You have seen them on another stream before. You may say so once, and never name a channel.");
            return facts.Count == 0 ? "" : "\nWhat you remember of this viewer:\n" + Brain.DataBlock("viewer_data", "twitch", "remembered", string.Join("\n", facts.ToArray()));
        }

#if CHECKS
        // Remember viewers with stand-in chat, a stand-in reply and a stand-in clock, never the PC's own list.
        private void SmokeTwitchViewers(List<string> checks, string output)
        {
            string pathWas = twitchPath, keptWas = Viewers.Kept, saltWas = preferences.ViewerSalt;
            bool onWas = preferences.RememberViewers, aloudWas = twitchAloud;
            Func<DateTime> nowWas = Viewers.Now;
            string folder = System.IO.Path.Combine(output, "twitch-viewers");
            System.IO.Directory.CreateDirectory(folder);
            List<string> briefs = new List<string>(), wrong = new List<string>();
            DateTime clock0 = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);
            double at = 0;
            bool pickWas = PickAtOnce;
            PickAtOnce = true;
            try
            {
                // twitch.md: an older file gains the heading under My channel once; the stock file has it already.
                string old = System.IO.Path.Combine(folder, "old-twitch.md");
                System.IO.File.WriteAllText(old, "# Capri on Twitch\r\n\r\n## My channel\r\n\r\nchannel: mine\r\n\r\n## Channels to watch\r\n\r\nsomeone\r\n");
                bool added = TwitchRules.AddCalls(old), again = TwitchRules.AddCalls(old);
                string oldText = System.IO.File.ReadAllText(old);
                TwitchRules oldRules = TwitchRules.Read(old);
                if (!added || again || oldText.IndexOf(TwitchRules.CallsHeading, StringComparison.Ordinal) > oldText.IndexOf("## Channels to watch", StringComparison.Ordinal) || oldText.IndexOf("channel: mine", StringComparison.Ordinal) < 0 || oldRules.Terms.Count != 3 || oldRules.PetNames.Count != 3 || oldRules.Watching.Count != 1)
                    wrong.Add("an older twitch.md didn't gain What she calls chatters once, under My channel: " + oldText);
                string stockFile = System.IO.Path.Combine(folder, "stock-twitch.md");
                System.IO.File.WriteAllText(stockFile, TwitchRules.Default);
                if (TwitchRules.AddCalls(stockFile) || TwitchRules.Read(stockFile).Terms.Count != 3 || Viewers.AskLine(TwitchRules.Read(stockFile).Terms) != "Good boy, good girl or good gremlin?")
                    wrong.Add("the stock twitch.md's terms: " + Viewers.AskLine(TwitchRules.Read(stockFile).Terms));

                twitchPath = System.IO.Path.Combine(folder, TwitchRules.FileName);
                System.IO.File.WriteAllText(twitchPath, TwitchRules.Default.Replace("channel:", "channel: sam_streams"));
                twitchReadAt = DateTime.MinValue;
                twitchAloud = false;
                Viewers.Kept = System.IO.Path.Combine(folder, "viewers.json");
                Viewers.Forget();
                Viewers.Now = delegate { return clock0.AddSeconds(at); };
                TwitchAsideFor = delegate(string system, string user) { lock (briefs) briefs.Add(system); return "Thanks for the eddies, choom."; };
                int bits = 100;
                Func<string, string> donate = delegate(string who)
                {
                    int before;
                    lock (briefs) before = briefs.Count;
                    bits += 7;
                    TwitchSaid(TwitchLine.Parse("@badges=;bits=" + bits + ";id=v" + bits + " :" + who + "!" + who + "@" + who + ".tmi.twitch.tv PRIVMSG #sam_streams :Cheer" + bits));
                    System.Diagnostics.Stopwatch waiting = System.Diagnostics.Stopwatch.StartNew();
                    while (waiting.ElapsedMilliseconds < 5000) { System.Windows.Forms.Application.DoEvents(); lock (briefs) if (briefs.Count > before && System.Threading.Volatile.Read(ref twitchBusy) == 0) return briefs[briefs.Count - 1]; System.Threading.Thread.Sleep(20); }
                    return "";
                };
                Action<string, string> say = delegate(string who, string words) { at += 5; TwitchSaid(TwitchLine.Parse(":" + who + "!" + who + "@" + who + ".tmi.twitch.tv PRIVMSG #sam_streams :" + words)); };

                // Off: she thanks as before and nothing is written.
                preferences.RememberViewers = false;
                string offBrief = donate("nova_kat");
                say("nova_kat", "hello");
                if (offBrief.Contains("thank them plainly") || offBrief.Contains("Call them a") || System.IO.File.Exists(Viewers.Kept) || Viewers.Waiting("nova_kat")) wrong.Add("off still remembered or asked");

                SetRememberViewers(true);
                // A first donation: a plain thanks and the ask; "girl" within two minutes is kept; the next donation says it.
                string first = donate("nova_kat");
                bool asked = first.Contains("thank them plainly") && Viewers.Waiting("nova_kat");
                say("nova_kat", "girl");
                Viewer nova = Viewers.Get("nova_kat");
                string later = donate("nova_kat");
                if (!asked || nova == null || nova.Call != "good girl" || !later.Contains("Call them a good girl.") || Viewers.Waiting("nova_kat")) wrong.Add("the ask and answer: asked " + asked + ", kept " + (nova == null ? "nothing" : nova.Call) + ", then " + later);
                // No answer leaves it unknown and isn't asked again: a pet name at random.
                donate("quietone");
                say("quietone", "lol hi there");
                string unknown = donate("quietone");
                Viewer quiet = Viewers.Get("quietone");
                bool pet = unknown.Contains("Call them a good gremlin.") || unknown.Contains("Call them a good kitten.") || unknown.Contains("Call them a good puppy.");
                if (quiet == null || quiet.Call != null || !pet || Viewers.Waiting("quietone")) wrong.Add("no answer: " + unknown);
                // Neither is kept.
                donate("zed");
                say("zed", "neither");
                Viewer zed = Viewers.Get("zed");
                if (zed == null || zed.Call != Viewers.Neither) wrong.Add("neither wasn't kept");
                // Past two minutes an answer isn't one.
                donate("slowpoke");
                at += Viewers.AnswerSeconds + 10;
                say("slowpoke", "boy");
                Viewer slow = Viewers.Get("slowpoke");
                if (slow == null || slow.Call != null) wrong.Add("an answer after two minutes was kept");

                // Last seen on another stream: said once in her brief, never a channel's name.
                Viewers.Seen("nova_kat", Viewers.Hash("other_channel", preferences.ViewerSalt));
                Viewers.NewStream();
                twitchGeneralAt = -1e9;
                int beforeChat;
                lock (briefs) beforeChat = briefs.Count;
                say("nova_kat", "hey capri, how is it going");
                System.Diagnostics.Stopwatch chatWait = System.Diagnostics.Stopwatch.StartNew();
                string chatBrief = "";
                while (chatWait.ElapsedMilliseconds < 5000) { System.Windows.Forms.Application.DoEvents(); lock (briefs) if (briefs.Count > beforeChat && System.Threading.Volatile.Read(ref twitchBusy) == 0) { chatBrief = briefs[briefs.Count - 1]; break; } System.Threading.Thread.Sleep(20); }
                bool once = !Viewers.Seen("nova_kat", ViewerWhere(TwitchNow()));
                if (!chatBrief.Contains("seen them on another stream") || !chatBrief.Contains("good girl") || chatBrief.Contains("other_channel") || !once) wrong.Add("another stream: " + chatBrief);
                Viewers.Flush();
                string stored = System.IO.File.ReadAllText(Viewers.Kept);
                if (stored.Contains("sam_streams") || stored.Contains("other_channel")) wrong.Add("the list holds a channel's name");

                // Off again: a new name writes nothing.
                preferences.RememberViewers = false;
                int countOff = Viewers.Count;
                say("newbie", "hello all");
                if (Viewers.Count != countOff || Viewers.Get("newbie") != null) wrong.Add("off still wrote a name");
                preferences.RememberViewers = true;

                // Forget viewers, after a Yes in her bubble.
                AskForgetViewers();
                box.Pick(true);
                System.Windows.Forms.Application.DoEvents();
                if (Viewers.Count != 0 || System.IO.File.Exists(Viewers.Kept)) wrong.Add("Forget viewers left " + Viewers.Count);

                // At most 5,000: the longest unseen go first.
                for (int one = 0; one <= Viewers.Most; one++) { at += 1; Viewers.Seen("viewer" + one, "here"); }
                at += 60;
                Viewers.Flush();
                if (Viewers.Count != Viewers.Most || Viewers.Get("viewer0") != null || Viewers.Get("viewer" + Viewers.Most) == null) wrong.Add("the cap kept " + Viewers.Count);

                if (wrong.Count > 0) throw new Exception("Remember viewers went wrong: " + string.Join("; ", wrong.ToArray()));
                checks.Add("PASS Remember viewers: off thanks as before and writes nothing; a first donation is thanked plainly and asked \"Good boy, good girl or good gremlin?\", an answer within two minutes is kept and named on the next, no answer or neither gets no second ask, unknown gets a chatPetNames pick; another stream is said once and no channel's name is kept; Forget viewers empties the list after a Yes; at most " + Viewers.Most + " names; an older twitch.md gains What she calls chatters under My channel once");
            }
            finally
            {
                PickAtOnce = pickWas;
                Viewers.Forget();
                Viewers.Kept = keptWas;
                Viewers.Now = nowWas;
                TwitchAsideFor = null;
                twitchPath = pathWas;
                twitchReadAt = DateTime.MinValue;
                twitchAloud = aloudWas;
                preferences.RememberViewers = onWas;
                preferences.ViewerSalt = saltWas;
                SavePreferences();
                if (box.Choosing) box.Choose(null);
            }
        }
#endif
    }
}
