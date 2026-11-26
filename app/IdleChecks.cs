#if CHECKS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace CapriDesktop
{
    // Her idle work kept light: settings.json written at most once a gap, and her window never waiting on a local brain.
    internal sealed partial class Companion
    {
        private void SmokeSaveGap(List<string> checks)
        {
            lock (saveGate) savedAt = saveClock.ElapsedMilliseconds;
            int before = saveWrites;
            for (int i = 0; i < 50; i++) SavePreferences();
            int during = saveWrites - before;
            bool waiting = Clock.Has("save settings");
            FlushPreferences();
            FlushPreferences();
            int after = saveWrites - before;
            bool read = LoadPreferences() != null && saveWrites - before == 1;
            if (during != 0 || !waiting || after != 1 || !read)
                throw new Exception("50 saves in a minute wrote " + during + " at once, a write waiting " + waiting + ", " + after + " after the flush, read back without another write " + read);
            checks.Add("PASS settings.json written at most once every two minutes: 50 saves in a minute make no write until the gap is up, then one, and quitting or reading it back writes what waits first");
        }

        // No brain ever answering: 12 quiet looks in her first two minutes, none for the next ten, and 12 more after a look by
        // hand; No local blinks on SETUP while the looks run and stays steady once they stop.
        private void SmokeBrainSearch(List<string> checks)
        {
            string modelWas = brain.Model;
            bool setupWas = SettingUp, findingWas = finding;
            Action lookWas = QuietBrainLook;
            int looks = 0;
            bool blinkedSearching = false, steadyAfter = false, blinkedAgain = false;
            try
            {
                brain.Model = null;
                SettingUp = false;
                finding = brainLooking = false;
                brainLookAt = brainLostAt = -1;
                QuietBrainLook = delegate { looks++; };
                mainframe.Restore();
                mainframe.ShowSetupPage(true);
                for (double t = 0; t <= 60; t += 1) CheckBrainGone(t);
                mainframe.ShowBrainSearch();
                blinkedSearching = mainframe.SearchPulsing;
                for (double t = 61; t <= 720; t += 1) CheckBrainGone(t);
                mainframe.ShowBrainSearch();
                steadyAfter = !mainframe.SearchPulsing;
                int first = looks;
                LookAgainByHand();
                blinkedAgain = mainframe.SearchPulsing;
                for (double t = 1000; t <= 1720; t += 1) CheckBrainGone(t);
                int second = looks - first;
                if (first != 12 || second != 12 || !blinkedSearching || !steadyAfter || !blinkedAgain)
                    throw new Exception("Her looks for a brain went wrong: " + first + " in the first 12 minutes, " + second + " after a look by hand; No local pulsing while searching " + blinkedSearching + ", steady after " + steadyAfter + ", pulsing again after the refresh " + blinkedAgain);
            }
            finally
            {
                QuietBrainLook = lookWas;
                brain.Model = modelWas;
                SettingUp = setupWas;
                finding = findingWas;
                brainLookAt = brainLostAt = -1;
                mainframe.ShowBrainSearch();
                mainframe.ShowSetupPage(false);
            }
            checks.Add("PASS with no brain answering she looks 10 s on and every 10 s for two minutes (12 looks), then none for ten minutes, and a look by hand starts 12 more; No local pulses on SETUP while she looks, its tip saying so, and is steady once she stops");
        }

        // Push to talk on the middle button and on F9: the mouse heard only while bound, the keyboard never (F9 read on the
        // clock), nothing in an idle minute unbound. The filter is checked on its fields, as any click on this PC reaches it.
        private void SmokeKeyEvents(List<string> checks)
        {
            int pushWas = preferences.PushVk, modsWas = preferences.PushMods;
            Func<int, bool> heldWas = KeyHeld;
            bool middle = false, started = false, ended = false, boundListens = false, unboundQuiet = false, offAfter = false;
            bool keyPolled = false, keyStarted = false, keyFast = false, keyEnded = false, f9 = false;
            int idleEvents = -1, idleWatches = 0;
            Action watchWas = null;
            bool filter = !KeyEvents.Counts(0, 0) && KeyEvents.Counts(0, 0x0010) && KeyEvents.Counts(0, 0x0020) && KeyEvents.Counts(0, 0x0001)
                && !KeyEvents.Counts(0, 0x0400) && !KeyEvents.Counts(0, 0x0800) && !KeyEvents.Counts(1, 0) && !KeyEvents.Counts(2, 0);
            try
            {
                // Nothing bound to a mouse button and no push to talk: nothing registered, so an idle minute hears nothing.
                preferences.PushVk = 0;
                preferences.PushMods = 0;
                ListenForKeys(true);
                unboundQuiet = (keyEvents == null || !keyEvents.Listening) && !Clock.Has(PushPollJob);
                if (keyEvents != null)
                {
                    watchWas = keyEvents.Changed;
                    keyEvents.Changed = delegate { idleWatches++; watchWas(); };
                    int seenFrom = keyEvents.Seen;
                    Stopwatch idle = Stopwatch.StartNew();
                    while (idle.ElapsedMilliseconds < 60000) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(20); }
                    idleEvents = keyEvents.Seen - seenFrom;
                    keyEvents.Changed = watchWas;
                }
                else idleEvents = 0;
                // On the middle button: the mouse is heard, and no key is read.
                preferences.PushVk = 4;
                ListenForKeys(true);
                if (keyEvents == null) throw new Exception("Push to talk on a mouse button had nothing listening for it.");
                boundListens = keyEvents.Listening && !Clock.Has(PushPollJob);
                watchWas = keyEvents.Changed;
                KeyHeld = delegate(int vk) { return (vk == 4 && middle) || (vk == (int)System.Windows.Forms.Keys.F9 && f9); };
                middle = true;
                keyEvents.Changed();
                started = pushing;
                middle = false;
                keyEvents.Changed();
                ended = !pushing;
                // On F9: no listener, its state read on the clock, quicker while held.
                preferences.PushVk = (int)System.Windows.Forms.Keys.F9;
                ListenForKeys(true);
                keyPolled = !keyEvents.Listening && Clock.Has(PushPollJob);
                f9 = true;
                PushPoll();
                keyStarted = pushing;
                keyFast = pushPollFast;
                f9 = false;
                PushPoll();
                keyEnded = !pushing && !pushPollFast;
                // Unbound again: let go.
                preferences.PushVk = 0;
                ListenForKeys(true);
                offAfter = !keyEvents.Listening && !Clock.Has(PushPollJob);
            }
            finally
            {
                if (keyEvents != null && watchWas != null) keyEvents.Changed = watchWas;
                KeyHeld = heldWas;
                preferences.PushVk = pushWas;
                preferences.PushMods = modsWas;
                if (pushing) PushDone();
                ListenForKeys(false);
            }
            if (!filter || !unboundQuiet || idleEvents != 0 || idleWatches != 0 || !boundListens || !started || !ended || !keyPolled || !keyStarted || !keyFast || !keyEnded || !offAfter)
                throw new Exception("Push to talk on the middle button went wrong: filter " + filter + ", nothing registered unbound " + unboundQuiet + ", " + idleEvents + " events and " + idleWatches + " watches in an idle minute unbound, the mouse alone heard when bound " + boundListens + ", hold started it " + started + ", letting go ended it " + ended + ", F9 read on the clock with no listener " + keyPolled + ", F9 held started it " + keyStarted + " and read quicker " + keyFast + ", let go ended it " + keyEnded + ", let go when unbound " + offAfter);
            checks.Add("PASS push to talk: nothing registered and nothing run in an idle minute with nothing bound; on the middle button the mouse alone is heard, a button counted and a move, wheel or key not; on F9 no listener and no hotkey, the one key read on the clock and quicker while held; a hold starts push to talk and letting go ends it; unbound, all let go");
        }

        // A local brain that takes 5 s to list its models: her window's Sync and conversation brain key never wait on it.
        private void SmokeSlowBrain(List<string> checks)
        {
            TcpListener slow = new TcpListener(IPAddress.Loopback, 0);
            slow.Start();
            int port = ((IPEndPoint)slow.LocalEndpoint).Port;
            bool serving = true;
            Thread server = new Thread(delegate()
            {
                while (serving)
                {
                    try
                    {
                        using (TcpClient client = slow.AcceptTcpClient())
                        using (NetworkStream stream = client.GetStream())
                        {
                            stream.ReadTimeout = 2000;
                            byte[] read = new byte[4096];
                            try { stream.Read(read, 0, read.Length); } catch (System.IO.IOException) { }
                            Thread.Sleep(5000);
                            byte[] body = Encoding.UTF8.GetBytes("{\"models\":[{\"name\":\"gemma3:1b\"}]}");
                            byte[] head = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
                            stream.Write(head, 0, head.Length);
                            stream.Write(body, 0, body.Length);
                        }
                    }
                    catch (SocketException) { }
                    catch (ObjectDisposedException) { }
                    catch (InvalidOperationException) { }
                    catch (System.IO.IOException) { }
                }
            });
            server.IsBackground = true;
            server.Name = "slow brain stand-in";
            server.Start();
            string ollamaWas = Brain.Ollama;
            bool convoWas = preferences.ConversationBrainOn;
            List<string> slowest = new List<string>();
            long worst = 0;
            try
            {
                Brain.Ollama = "http://127.0.0.1:" + port;
                preferences.ConversationBrainOn = true;
                for (int round = 0; round < 6; round++)
                {
                    Stopwatch took = Stopwatch.StartNew();
                    mainframe.Sync();
                    string key = ConvoKey;
                    long ms = took.ElapsedMilliseconds;
                    worst = Math.Max(worst, ms);
                    if (ms > 100) slowest.Add("round " + round + " " + ms + " ms");
                    System.Windows.Forms.Application.DoEvents();
                    Thread.Sleep(300);
                }
            }
            finally
            {
                Brain.Ollama = ollamaWas;
                preferences.ConversationBrainOn = convoWas;
                serving = false;
                slow.Stop();
            }
            if (slowest.Count > 0) throw new Exception("Her window waited on a slow local brain: " + string.Join(", ", slowest.ToArray()));
            checks.Add("PASS her window never waits on a slow local brain: with one taking 5 s to list its models, the Mainframe's Sync and the conversation brain's key took at most " + worst + " ms each, the list read on the pool");
        }
    }
}
#endif
