using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Windows.Forms;

#if CHECKS
namespace CapriDesktop
{
    internal sealed partial class Companion
    {
        // From the line review: her own lines a minute apart, an opener or a tail round a core and never both, a line
        // file's lines named in her log, and a check-in set gone round before one repeats.
        private void SmokeLineReview(List<string> checks)
        {
            double shownWas = ownShownAt;
            try
            {
                ownShownAt = 100;
                bool paced = !Paced(100 + OwnGap - 1) && Paced(100 + OwnGap);
                Remark("check-in", "Pace check.", true);
                double stamped = ownShownAt;
                bool stamps = Math.Abs(stamped - clock.Elapsed.TotalSeconds) < 5 && !Paced(clock.Elapsed.TotalSeconds);
                if (!paced || !stamps) throw new Exception("Her own lines aren't held a minute apart: the gap " + paced + ", a shown line counts " + stamps);
                checks.Add("PASS a line of her own timers waits a minute after the last line she showed on her own or in reaction, so check-ins, hunger, questions and flirts can't come five in a minute");
            }
            finally { ownShownAt = shownWas; CloseTalk(); }

            // Every mood end stood in for, so each dressing that takes an end takes the mood's: both mood ends never meet,
            // while her own openers and tails still go round a core with one of them.
            Func<string, string> endWas = Sayings.MoodEnd;
            int both = 0, opened = 0, tailed = 0, mixed = 0;
            try
            {
                Sayings.MoodEnd = delegate(string kind) { return kind == Personalities.OpenersHeading ? "Moodopen." : "moodtail."; };
                for (int i = 0; i < 400; i++)
                {
                    string made = Sayings.DressedForCheck("Don't mind if I do.");
                    bool front = made.IndexOf("Moodopen", StringComparison.OrdinalIgnoreCase) >= 0, back = made.IndexOf("moodtail", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (front && back) both++;
                    if (front) opened++;
                    if (back) tailed++;
                }
                Sayings.MoodEnd = delegate(string kind) { return kind == Personalities.OpenersHeading ? null : "moodtail."; };
                for (int i = 0; i < 400; i++)
                {
                    string made = Sayings.DressedForCheck("Don't mind if I do.");
                    int at = made.IndexOf("Don't mind", StringComparison.OrdinalIgnoreCase);
                    if (at > 0 && made.IndexOf("moodtail", StringComparison.OrdinalIgnoreCase) > at) mixed++;
                }
            }
            finally { Sayings.MoodEnd = endWas; }
            if (both > 0 || opened == 0 || tailed == 0 || mixed == 0)
                throw new Exception("The mood's own ends went wrong in 400 dressings: both " + both + ", a mood opener " + opened + ", a mood tail " + tailed + ", her opener with the mood's tail " + mixed);
            checks.Add("PASS the mood's own opener and its own tail never go round one core (400 tries: " + opened + " with its opener, " + tailed + " with its tail, none with both), while her opener with the mood's tail still does (" + mixed + " in 400)");

            CapriDesktop.Touch.Line(CapriDesktop.Touch.Head, 0);
            string source = Sayings.LastSource;
            if (source == null || source == Sayings.Unknown || source.IndexOf("When you touch her head", StringComparison.Ordinal) < 0)
                throw new Exception("A touch line was logged without its source: " + source);
            checks.Add("PASS a line from one of her line files (a touch line) is named in her log by its file and heading, not \"?\": " + source);

            List<string> seen = new List<string>();
            string text = File.Exists(careLines.Kept) ? File.ReadAllText(careLines.Kept) : "";
            int water = 0;
            bool under = false;
            foreach (string row in text.Replace("\r\n", "\n").Split('\n'))
            {
                string one = row.Trim();
                if (one.StartsWith("#", StringComparison.Ordinal)) under = one == "## Water";
                else if (under && one.StartsWith("- ", StringComparison.Ordinal)) water++;
            }
            for (int i = 0; i < water; i++) { string picked = careLines.Pick("Water"); if (picked != null && !seen.Contains(picked)) seen.Add(picked); }
            if (water < 2 || seen.Count != water) throw new Exception("Water check-ins repeated before the set went round: " + seen.Count + " different in " + water + " picks");
            checks.Add("PASS check-ins go round their set before one repeats: " + water + " Water picks, all different, from the store that survives a restart");
        }

        // A start whose first check is refused (the network down, a provider not answering) keeps the cloud key and the
        // provider they saved, in memory and in settings.json. Lachy's 0.9.7 lost both on such a start.
        private void SmokeKeysSurvive(List<string> checks)
        {
            Preferences wasPreferences = preferences;
            Cloud wasRemote = brain.Remote;
            Engine wasLocal = brain.Local;
            string wasModel = brain.Model, wasGoogle = Brain.Google, wasAddress, wasKey;
            brain.GetServer(out wasAddress, out wasKey);
            bool wasFinding = finding;
            const string Refused = "http://127.0.0.1:9";
            Func<Preferences> onDisk = delegate
            {
                using (MemoryStream read = new MemoryStream(Encoding.UTF8.GetBytes(File.ReadAllText(settingsPath))))
                    return (Preferences)new DataContractJsonSerializer(typeof(Preferences)).ReadObject(read);
            };
            Action<int> wait = delegate(int ms)
            {
                Stopwatch held = Stopwatch.StartNew();
                while (held.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(10); }
            };
            try
            {
                finding = false;
                // A Gemini key, its check refused.
                Brain.Google = Refused + "/";
                string sealedKey = Voice.Protect("AIza-keep-test");
                preferences = new Preferences { Tiered = true, Silent = true, Tier = 2, PaidKey = sealedKey, PaidModel = "gemini-90-flash" };
                brain.Local = null;
                brain.Remote = new Cloud("AIza-keep-test", "gemini-90-flash");
                brain.Model = brain.Remote.Model;
                SavePreferences();
                ++brainLook;
                FindBrain(false);
                wait(3000);
                SavePreferences();
                FlushPreferences();
                Preferences cloudDisk = onDisk();
                bool cloudKept = preferences.PaidKey == sealedKey && cloudDisk.PaidKey == sealedKey && preferences.Tier == 2 && cloudDisk.Tier == 2 && brain.Remote != null && brain.Remote.Key == "AIza-keep-test";

                // A provider of their own, its address refused.
                string sealedProvider = Voice.Protect("sk-keep-test");
                string list = Refused + "\tMine\tkeep-model\t" + sealedProvider;
                preferences = new Preferences { Tiered = true, Silent = true, Tier = 0, ProviderOn = true, ProviderList = list, ProviderName = "Mine", ProviderModel = "keep-model" };
                brain.Remote = null;
                brain.SetServer(Refused, "sk-keep-test");
                SavePreferences();
                ++brainLook;
                FindBrain(false);
                wait(3000);
                SavePreferences();
                FlushPreferences();
                Preferences providerDisk = onDisk();
                string address, key;
                brain.GetServer(out address, out key);
                bool providerKept = preferences.ProviderOn && providerDisk.ProviderOn && preferences.ProviderList == list && providerDisk.ProviderList == list
                    && providerDisk.ProviderName == "Mine" && address == Refused && key == "sk-keep-test";
                if (!cloudKept || !providerKept)
                    throw new Exception("A start whose check was refused lost what they saved: the cloud key kept " + cloudKept + ", the provider kept " + providerKept);
                checks.Add("PASS a start whose first check is refused keeps a sealed Gemini key and a provider of their own (address, name, model and its sealed key) in memory and in settings.json");
            }
            finally
            {
                Brain.Google = wasGoogle;
                preferences = wasPreferences;
                brain.Remote = wasRemote;
                brain.Local = wasLocal;
                brain.Model = wasModel;
                brain.SetServer(wasAddress, wasKey);
                finding = wasFinding;
                ++brainLook;
                SavePreferences();
            }
        }
    }
}
#endif
