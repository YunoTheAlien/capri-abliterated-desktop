using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CapriDesktop
{
    // A line built from two or more parts made into one natural line by a brain, kept in expansionLines' smoothed.txt and
    // said that way after. Not smoothed yet, it goes out joined and is asked for in the background.
    internal static class Smooth
    {
        internal const string FileName = "smoothed";
        internal const string Instructions = "You write short dialogue lines for a companion character in a role-playing game. Reply with only the JSON asked for.";

        // A brain asked (system, words): her own when it is loaded, else the free ones under Better conversations. Null
        // when none could. Set by her window; checks swap it.
        internal static Func<string, string, string> Ask;
        // Something can be asked now without a model being started. Set by her window.
        internal static Func<bool> Ready = delegate { return false; };

        private static readonly object gate = new object();
        private static readonly Dictionary<string, int> used = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly HashSet<string> asked = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Random dice = new Random();
        private static bool busy;

        // The line to say for one joined from parts: its smoothed one when there is one, else the joined line, with its
        // smoothing asked for in the background.
        internal static string Of(string joined)
        {
            if (string.IsNullOrWhiteSpace(joined)) return joined;
            string[] warm = Expansion.LinesNamed(FileName, Heading(joined));
            if (warm.Length > 0) { lock (gate) return warm[dice.Next(warm.Length)]; }
            lock (gate)
            {
                int times;
                used.TryGetValue(joined, out times);
                used[joined] = times + 1;
                if (used.Count > 400) used.Clear();
            }
            Soon(joined);
            return joined;
        }

        // Idle: the most used joined line not smoothed or asked for yet.
        internal static void Idle()
        {
            string most = null;
            int best = 0;
            lock (gate) foreach (KeyValuePair<string, int> one in used) if (one.Value > best && !asked.Contains(one.Key)) { most = one.Key; best = one.Value; }
            if (most != null) Soon(most);
        }

        private static void Soon(string joined)
        {
            if (!Ready()) return;
            lock (gate)
            {
                if (busy || asked.Contains(joined)) return;
                busy = true;
                asked.Add(joined);
                if (asked.Count > 2000) asked.Clear();
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { Warm(joined); }
                catch (Exception caughtOne) { Brain.Caught(caughtOne); }
                finally { lock (gate) busy = false; }
            });
        }

        // Asks for it and keeps what passes. True when one was kept.
        internal static bool Warm(string joined)
        {
            Func<string, string, string> ask = Ask;
            if (ask == null) return false;
            string made = Read(ask(Instructions, Words(joined)));
            if (made == null || Fault(joined, made) != null) return false;
            return Expansion.AddNamed(FileName, Heading(joined), new[] { made }) > 0;
        }

        // The ask: the line only, with its tokens as tokens, so nothing of theirs goes out.
        internal static string Words(string joined)
        {
            StringBuilder words = new StringBuilder();
            words.Append("{\"character\": \"a playful desktop companion\", \"task\": \"smooth one line of hers\"}\n");
            words.Append("Rewrite the line below as one natural line she would say aloud: the same meaning, the same speaker (I is her, you is the player), ");
            words.Append("every word in percent signs kept exactly as it is, and no longer than it is. It was joined from pieces, so make the joins read naturally.\n");
            words.Append("Line: ").Append(joined).Append('\n');
            words.Append("Reply with only JSON: {\"line\": \"...\"}");
            return words.ToString();
        }

        private static string Read(string reply)
        {
            if (string.IsNullOrEmpty(reply)) return null;
            Match json = Regex.Match(reply, "\"line\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (!json.Success) return null;
            string made = Regex.Unescape(json.Groups[1].Value).Replace('\n', ' ').Trim();
            return made.Length == 0 ? null : made;
        }

        // Why a smoothed line is turned down, or null to keep it: a token lost or added, much longer, the speaker changed,
        // the meaning lost (most of its longer words gone), or a gated word on the plain side.
        internal static string Fault(string joined, string made)
        {
            if (!Said.SameTokens(joined, made)) return "its tokens changed";
            if (made.Length > joined.Length * 1.3 + 8) return "much longer";
            bool herBefore = Speaks(joined, @"\b(I|I'm|I'll|I've|me|my|mine)\b"), herAfter = Speaks(made, @"\b(I|I'm|I'll|I've|me|my|mine)\b");
            bool themBefore = Speaks(joined, @"\b(you|your|you're|yours)\b"), themAfter = Speaks(made, @"\b(you|your|you're|yours)\b");
            if (herBefore && !herAfter || themBefore && !themAfter || !Speaks(joined, @"\bshe\b") && Speaks(made, @"\bshe\b")) return "the speaker changed";
            List<string> longer = new List<string>();
            foreach (Match word in Regex.Matches(joined.ToLowerInvariant(), @"[a-z]{5,}")) if (!longer.Contains(word.Value)) longer.Add(word.Value);
            int kept = 0;
            string lower = made.ToLowerInvariant();
            foreach (string word in longer) if (lower.Contains(word.Substring(0, word.Length - 1))) kept++;
            if (longer.Count > 0 && kept * 10 < longer.Count * 6) return "its meaning was lost";
            if (!Brain.SenpaiLive && Legacy.SaysUnsaid(made)) return "a gated word";
            return null;
        }

        private static bool Speaks(string line, string pattern) { return Regex.IsMatch(line ?? "", pattern, RegexOptions.IgnoreCase); }

        private static string Heading(string joined) { return Regex.Replace(joined.Trim(), @"\s+", " ").TrimStart('#', '>', ' '); }

#if CHECKS
        internal static void Check(List<string> checks)
        {
            Func<string, string, string> askWas = Ask;
            Func<bool> readyWas = Ready;
            Func<string> folderWas = Expansion.Folder;
            string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "capri-smooth-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                Expansion.Folder = delegate { return root; };
                List<string> wrong = new List<string>();
                string joined = "Oh, nice. you're alright, %mate%. Keep that up and we are straight to tea.";
                if (Fault(joined, "Oh, nice, you are alright, %mate%. Keep that up and we go straight to tea.") != null) wrong.Add("a good smoothing turned down: " + Fault(joined, "Oh, nice, you are alright, %mate%. Keep that up and we go straight to tea."));
                if (Fault(joined, "Oh, nice. Keep that up and we are straight to tea.") == null) wrong.Add("a lost token kept");
                if (Fault(joined, "She thinks %mate% is alright and wants tea.") == null) wrong.Add("a changed speaker kept");
                if (Fault(joined, "Lovely weather for ducks today, %mate%, isn't it, I think you know.") == null) wrong.Add("a lost meaning kept");
                if (Fault(joined, joined + " And then a whole lot more words that nobody asked for at all, %mate%.") == null) wrong.Add("a much longer one kept");
                int asks = 0;
                string sent = null;
                Ready = delegate { return true; };
                Ask = delegate(string system, string words) { asks++; sent = words; return "{\"line\": \"Oh, nice, you are alright, %mate%. Keep that up and we go straight to tea.\"}"; };
                string first = Of(joined);
                for (int wait = 0; wait < 100 && Expansion.LinesNamed(FileName, Heading(joined)).Length == 0; wait++) Thread.Sleep(30);
                string second = Of(joined);
                if (first != joined || second != "Oh, nice, you are alright, %mate%. Keep that up and we go straight to tea." || asks != 1 || sent == null || sent.IndexOf("%mate%", StringComparison.Ordinal) < 0)
                    wrong.Add("the joined line first, then its smoothed one, one ask with its tokens as tokens: " + first + " / " + second + " / " + asks);
                Ready = delegate { return false; };
                string other = "Hm. that one landed. Back at it then.";
                if (Of(other) != other || asks != 1) wrong.Add("asked with nothing ready");
                if (wrong.Count > 0) throw new Exception("Smoothing her built lines went wrong: " + string.Join(" | ", wrong.ToArray()));
                checks.Add("PASS a line built of parts goes out joined, is smoothed once in the background and said smoothed after; a lost token, a changed speaker, a lost meaning or a much longer line is turned down; nothing is asked when no brain is ready");
            }
            finally
            {
                Ask = askWas;
                Ready = readyWas;
                Expansion.Folder = folderWas;
                lock (gate) { used.Clear(); asked.Clear(); }
                try { if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true); }
                catch (Exception caughtOne) { Brain.Caught(caughtOne); }
            }
        }
#endif
    }
}
