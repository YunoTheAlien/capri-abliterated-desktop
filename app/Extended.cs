using System;
using System.Collections.Generic;

namespace CapriDesktop
{
    // Named settings for her senpai side, as "key = value" lines, from whatever attached: the codex, or the local
    // side on the build that makes it. Empty until then, so everything that asks gets nothing and does nothing.
    internal static class Extended
    {
        private static readonly object gate = new object();
        private static string text = "";
        private static Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        internal static string Text
        {
            get { lock (gate) return text; }
            set
            {
                Dictionary<string, string> made = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string raw in (value ?? "").Replace("\r\n", "\n").Split('\n'))
                {
                    string row = raw.Trim();
                    int at = row.IndexOf('=');
                    if (row.Length == 0 || row.StartsWith("#", StringComparison.Ordinal) || at <= 0) continue;
                    made[row.Substring(0, at).Trim()] = row.Substring(at + 1).Trim();
                }
                lock (gate) { text = value ?? ""; values = made; }
            }
        }

        internal static string Get(string key)
        {
            string found;
            lock (gate) return key != null && values.TryGetValue(key, out found) && found.Length > 0 ? found : null;
        }

        internal static bool Has(string key) { return Get(key) != null; }

        // Whether a text is one of these settings' values, or one item of a list of them.
        internal static bool Holds(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string want = text.Trim();
            lock (gate)
                foreach (string value in values.Values)
                {
                    if (string.Equals(value, want, StringComparison.OrdinalIgnoreCase)) return true;
                    foreach (string one in value.Split('|')) if (string.Equals(one.Trim(), want, StringComparison.OrdinalIgnoreCase)) return true;
                }
            return false;
        }

        // A list kept as "one | two | three".
        internal static string[] List(string key)
        {
            string found = Get(key);
            if (found == null) return new string[0];
            List<string> made = new List<string>();
            foreach (string one in found.Split('|')) if (one.Trim().Length > 0) made.Add(one.Trim());
            return made.ToArray();
        }

        internal static int Int(string key, int fallback)
        {
            int found;
            return int.TryParse(Get(key), out found) ? found : fallback;
        }

        // An act's score for a pack or a mood, from its row: "score.<name> = act:-1 | act:+2".
        internal static bool Score(string named, string act, out int score)
        {
            score = 0;
            if (string.IsNullOrEmpty(named) || string.IsNullOrEmpty(act)) return false;
            foreach (string pair in List("score." + named))
            {
                int colon = pair.LastIndexOf(':');
                if (colon <= 0 || !string.Equals(pair.Substring(0, colon).Trim(), act, StringComparison.OrdinalIgnoreCase)) continue;
                return int.TryParse(pair.Substring(colon + 1).Trim().TrimStart('+'), out score);
            }
            return false;
        }
    }
}
