using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Opening what they named when it isn't what she first took it for: an app that isn't installed may be a website, and
    // "do it" soon after a failed opening tries the other way.
    internal sealed partial class Companion
    {
        // Whether her last repeatable command failed, so "do it" soon after tries the other way instead of failing again.
        private bool againFailed;
        // True while her bubble's Yes and No keys are hers for a website question.
        private bool siteChoice;

        // An app that isn't installed may be a website: she says no app was found, then asks about the closest browser
        // bookmark, or else the first of .com, .net and .io with a site. Either opens only on their yes.
        private static string AppOrSite(ToolArguments args, string missing)
        {
            ToolArguments site = Hands.AsSite(args == null ? null : args.Name);
            if (site == null) return missing;
            site.Lead = missing.TrimEnd() + " ";
            string asked = Security.Gate("open_website", site);
            if (Security.IsHold(asked)) return asked;
            return Security.IsNoSite(asked) ? Security.Said(asked) : missing;
        }

        // Noted after each tool runs: a failed opening, or a word with no site, is what lets "do it" try the other way.
        private void Tried(ToolArguments args, string result)
        {
            if (args != null && ReferenceEquals(args, againArgs)) againFailed = Hands.Failed(result) || Security.IsNoSite(result);
        }

        // "do it" or "again" within a minute of a failed opening: an app that wasn't found is tried as a website, and a site
        // as an app. Null when there is no other way, so the command runs again as it was.
        private string OtherWayAgain()
        {
            string other;
            ToolArguments otherArgs;
            if (!againFailed || DateTime.UtcNow - Hands.CommandAt > Hands.PushWindow || !Hands.OtherWay(againTool, againArgs, out other, out otherArgs)) return null;
            return Hands.Shown(Security.Said(DoTool(other, otherArgs)));
        }

        // While she asks whether a word is a website, Yes and No in her bubble answer it, as saying or typing yes or no does.
        // Once nothing is waiting, the keys go.
        private void OfferSiteChoice()
        {
            if (Security.Guessing)
            {
                if (box.Choosing) return;
                siteChoice = true;
                box.Choose(delegate(bool yes) { siteChoice = false; SiteChosen(yes); });
            }
            else if (siteChoice)
            {
                siteChoice = false;
                if (box.Choosing) box.Choose(null);
            }
        }

        // A click on Yes or No: her answer comes as if they had typed it.
        private void SiteChosen(bool yes)
        {
            string answer = yes ? "Yes." : "No.";
            Brain.Transcribe("You", answer);
            int ticket = ++asking;
            heard = answer;
            box.Prompt = "> you: ";
            ThreadPool.QueueUserWorkItem(delegate
            {
                string result;
                try { result = SiteAnswer(yes); }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); result = "That didn't work."; }
                Post(delegate { Answer(ticket, result, TalkState.Talking); });
            });
        }

        private string SiteAnswer(bool yes)
        {
            return Hands.Shown(Security.Resume(yes ? "yes" : "no", ResumeTool) ?? Security.Left);
        }

#if CHECKS
        private void SmokeOpenOtherWay(List<string> checks)
        {
            List<string> launches = new List<string>();
            List<string> looked = new List<string>();
            HashSet<string> sites = new HashSet<string>();
            Action<string> runWas = Hands.Run;
            Action<string, string> browseWas = Hands.Browse;
            Func<string, bool> hasWas = Hands.HasSite;
            Func<string> againWas = Hands.RunAgain;
            DateTime commandWas = Hands.CommandAt;
            bool quickWas = preferences.QuickHands;
            Hands.Run = delegate(string launched) { lock (launches) launches.Add(launched); };
            Hands.Browse = delegate(string path, string address) { lock (launches) launches.Add(address); };
            Hands.HasSite = delegate(string host) { lock (looked) looked.Add(host); lock (sites) return sites.Contains(host); };
            Hands.RunAgain = delegate { return AgainTool(); };
            preferences.QuickHands = true;
            Func<string, string[]> bookmarkWas = Security.Bookmark;
            Func<string> browserWas = Bookmarks.Browser;
            Func<string, List<string>> filesWas = Bookmarks.FilesFor;
            List<string> made = new List<string>();
            try
            {
                if (bookmarkWas == null) throw new Exception("Her bookmarks aren't hooked to her hands.");
                // Their bookmarks here are made up: one Chromium Bookmarks file and one Firefox backup, never this PC's.
                string folder = System.IO.Path.GetTempPath();
                string chromium = System.IO.Path.Combine(folder, "capri-bookmarks-" + Guid.NewGuid().ToString("N"));
                System.IO.File.WriteAllText(chromium, "{\"roots\":{\"bookmark_bar\":{\"children\":[{\"date_added\":\"1\",\"guid\":\"a\",\"id\":\"1\",\"name\":\"PSS Portal\",\"type\":\"url\",\"url\":\"https://pss-portal.net/home\"},{\"date_added\":\"1\",\"guid\":\"b\",\"id\":\"2\",\"name\":\"Weather\",\"type\":\"url\",\"url\":\"https://weather.com/\"}],\"name\":\"Bookmarks bar\",\"type\":\"folder\"}}}");
                made.Add(chromium);
                byte[] fox = System.Text.Encoding.UTF8.GetBytes("{\"title\":\"\",\"children\":[{\"guid\":\"x\",\"title\":\"Zorbq Hub\",\"index\":0,\"type\":\"text/x-moz-place\",\"uri\":\"https://zorbqhub.org/\"}]}");
                List<byte> packed = new List<byte>(System.Text.Encoding.ASCII.GetBytes("mozLz40\0"));
                packed.AddRange(BitConverter.GetBytes(fox.Length));
                packed.Add((byte)(fox.Length >= 15 ? 0xF0 : fox.Length << 4));
                if (fox.Length >= 15) { int rest = fox.Length - 15; while (rest >= 255) { packed.Add(255); rest -= 255; } packed.Add((byte)rest); }
                packed.AddRange(fox);
                string firefox = System.IO.Path.Combine(folder, "capri-bookmarks-" + Guid.NewGuid().ToString("N") + ".jsonlz4");
                System.IO.File.WriteAllBytes(firefox, packed.ToArray());
                made.Add(firefox);
                string browserNow = "Chrome";
                Bookmarks.Browser = delegate { return browserNow; };
                Bookmarks.FilesFor = delegate(string browser) { return new List<string> { browser == "Firefox" ? firefox : chromium }; };
                Security.Bookmark = Bookmarks.Ask;
                Security.Drop();
                // 0. A word close to a bookmark: no app first, then her bookmark question, with no lookup, and her brain's copy
                //    of the result, her transcript line and a phone never carry the bookmark.
                foreach (string asking in new[] { "app", "router" })
                {
                    lock (looked) looked.Clear();
                    string marked;
                    if (asking == "app") marked = DoTool("open_app", new ToolArguments { Name = "pss" });
                    else
                    {
                        string routedTool;
                        ToolArguments routedArgs;
                        if (!Hands.Direct("open pss", out routedTool, out routedArgs) || routedTool != "open_website" || !routedArgs.Guessed) throw new Exception("\"open pss\" didn't reach her guessed-site path.");
                        marked = DoTool(routedTool, routedArgs);
                    }
                    string shown = Security.Said(marked);
                    if (!Security.IsHold(marked) || !shown.StartsWith("No app called pss is installed on this PC. From your bookmarks", StringComparison.Ordinal) || !shown.EndsWith("? PSS Portal (pss-portal.net)", StringComparison.Ordinal) || looked.Count != 0 || !Security.Guessing)
                        throw new Exception("A word close to a bookmark wasn't asked about from their bookmarks (" + asking + "): " + shown + " / " + looked.Count + " looked up");
                    if (marked.Contains("PSS Portal") || marked.Contains("pss-portal") || Security.Unshared(shown).Contains("pss-portal") || Security.Unshared(shown).Contains("PSS Portal"))
                        throw new Exception("Her bookmark got into what leaves her bubble: " + marked);
                    int opensBefore = launches.Count;
                    string openedMark = SiteAnswer(true);
                    if (launches.Count != opensBefore + 1 || launches[launches.Count - 1] != "https://pss-portal.net/home") throw new Exception("Yes to her bookmark question didn't open it: " + openedMark);
                    launches.RemoveAt(launches.Count - 1);
                }
                // Firefox's compressed backup, and a name that starts the word's site.
                browserNow = "Firefox";
                Bookmarks.Mark foxMark = Bookmarks.Find("zorbq");
                browserNow = "Chrome";
                if (foxMark == null || foxMark.Title != "Zorbq Hub" || foxMark.Address != "https://zorbqhub.org/") throw new Exception("Firefox's bookmark backup wasn't read: " + (foxMark == null ? "none" : foxMark.Title));
                if (Bookmarks.Find("zorbqa") != null || Bookmarks.Find("xyz") != null) throw new Exception("A word nothing like a bookmark matched one.");
                checks.Add("PASS a word that is no app says so first, then asks \"From your bookmarks?\" about the closest of the default browser's bookmarks (Chromium's file and Firefox's compressed backup), with no lookup, opening it on a yes; her brain's result, transcripts and phone never carry the bookmark");
                Security.Drop();
                // 1. An app that isn't installed, with a site at .net: she asks about it and opens nothing.
                sites.Add("zorbqa.net");
                string held = DoTool("open_app", new ToolArguments { Name = "zorbqa" });
                if (!Security.IsHold(held) || Security.Said(held) != "No app called zorbqa is installed on this PC. Is it a website? zorbqa.net" || !Security.Guessing || launches.Count != 0)
                    throw new Exception("An app that isn't installed wasn't asked about as a website: " + Security.Said(held));
                // 2. Her bubble shows Yes and No for it, and Yes opens it once and takes the keys away.
                OfferSiteChoice();
                if (!box.Choosing) throw new Exception("Her website question showed no Yes and No keys.");
                string opened = SiteAnswer(true);
                OfferSiteChoice();
                if (launches.Count != 1 || launches[0] != "https://zorbqa.net/" || box.Choosing || Security.Guessing)
                    throw new Exception("Yes to her website question didn't open it once, or the keys stayed: " + opened + " / " + string.Join(" ", launches.ToArray()));
                // 3. No leaves it, and she says so.
                held = DoTool("open_app", new ToolArguments { Name = "zorbqa" });
                OfferSiteChoice();
                string declined = SiteAnswer(false);
                OfferSiteChoice();
                if (!Security.IsHold(held) || declined != Security.Left || launches.Count != 1 || box.Choosing)
                    throw new Exception("No to her website question opened it or said nothing: " + declined);
                // 4. A click on the bubble's key answers too: the yes runs off her window's thread and opens it.
                DoTool("open_app", new ToolArguments { Name = "zorbqa" });
                OfferSiteChoice();
                box.Pick(true);
                DateTime clickedAt = DateTime.UtcNow;
                while (DateTime.UtcNow - clickedAt < TimeSpan.FromSeconds(5)) { lock (launches) if (launches.Count == 2) break; Application.DoEvents(); Thread.Sleep(20); }
                if (launches.Count != 2 || launches[1] != "https://zorbqa.net/" || box.Choosing) throw new Exception("Clicking Yes in her bubble didn't open the site.");
                checks.Add("PASS an app that isn't installed is said first and then asked about as a website (\"No app called zorbqa is installed on this PC. Is it a website? zorbqa.net\"), and Yes in her bubble, a said or typed yes, opens it once; a no leaves it and says so");

                // 5. No app and no site: she says there's neither, after looking up all three.
                lock (looked) looked.Clear();
                string neither = DoTool("open_app", new ToolArguments { Name = "zorbqb" });
                if (neither != "No app called zorbqb is installed on this PC. There's no site at zorbqb.com, zorbqb.net or zorbqb.io." || !Hands.Failed(neither) || Security.Holding != null || looked.Count != 3)
                    throw new Exception("No app and no site wasn't said plainly: " + neither + " / " + looked.Count + " looked up");
                // 6. A word with no site, then "do it": the other way, an app, is tried, and she says neither is there.
                Security.Drop();
                string noSite = DoTool("open_website", new ToolArguments { Site = "zorbqc", Guessed = true });
                string push, pushTool;
                ToolArguments pushArgs;
                if (!Security.IsNoSite(noSite) || !Hands.Direct("do it", out pushTool, out pushArgs) || pushTool != "do_again") throw new Exception("\"do it\" after a word with no site didn't reach her last command: " + noSite);
                push = DoTool(pushTool, pushArgs);
                if (push != "No app called zorbqc is installed on this PC. There's no site at zorbqc.com, zorbqc.net or zorbqc.io." || againTool != "open_app")
                    throw new Exception("\"do it\" after no site didn't try it as an app: " + push);
                // 7. An app not found, then "do it" once the site answers: tried as a website, and she asks about it.
                DoTool("open_app", new ToolArguments { Name = "zorbqe" });
                sites.Add("zorbqe.com");
                if (!Hands.Direct("just do it", out pushTool, out pushArgs) || pushTool != "do_again") throw new Exception("\"just do it\" after a missing app didn't reach her last command.");
                push = DoTool(pushTool, pushArgs);
                if (push != "Is it a website? zorbqe.com" || againTool != "open_website" || !Security.Guessing) throw new Exception("\"just do it\" after a missing app didn't try it as a website: " + push);
                Security.Drop();
                // 8. Past the minute, "again" runs the same command as it was.
                DoTool("open_app", new ToolArguments { Name = "zorbq f" });
                Hands.CommandAt = DateTime.UtcNow.AddSeconds(-90);
                push = AgainTool();
                if (!push.StartsWith("No app called zorbq f", StringComparison.Ordinal) || againTool != "open_app") throw new Exception("\"again\" past the minute didn't repeat the command as it was: " + push);
                if (launches.Count != 2) throw new Exception("Trying the other way opened something without a yes: " + string.Join(" ", launches.ToArray()));
                checks.Add("PASS \"do it\" within a minute of a failed opening tries the other way: a word with no site as an app, a missing app as a website asked about first; past the minute \"again\" repeats it as it was");
            }
            finally
            {
                Security.Drop();
                OfferSiteChoice();
                Security.Bookmark = bookmarkWas;
                Bookmarks.Browser = browserWas;
                Bookmarks.FilesFor = filesWas;
                foreach (string file in made) try { System.IO.File.Delete(file); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                Hands.Run = runWas;
                Hands.Browse = browseWas;
                Hands.HasSite = hasWas;
                Hands.RunAgain = againWas;
                Hands.CommandAt = commandWas;
                preferences.QuickHands = quickWas;
            }
        }
#endif
    }
}
