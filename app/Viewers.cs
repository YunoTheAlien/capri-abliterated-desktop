using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // One Twitch viewer she remembers: what they said to be called, when first and last seen, on how many days, and where
    // last seen as a salted hash of the channel, so she can tell another stream from this one and never knows its name.
    [DataContract]
    internal sealed class Viewer
    {
        [DataMember] public string Login;
        [DataMember] public string Call;
        [DataMember] public bool Asked;
        [DataMember] public DateTime First, Last;
        [DataMember] public int Days;
        [DataMember] public string Where;
    }

    // Twitch viewers she remembers, on this PC only, while Remember viewers is on. Kept until Forget viewers.
    internal static class Viewers
    {
        internal const int Most = 5000;
        internal const double AnswerSeconds = 120;
        internal const string Neither = "neither";
        // Her file, set at startup; checks swap it.
        internal static string Kept;
        internal static Func<DateTime> Now = delegate { return DateTime.UtcNow; };
        private static readonly object gate = new object();
        private static Dictionary<string, Viewer> known;
        private static string loadedFrom;
        // Who was asked and when, waiting on an answer; and who has heard "another stream" this run. In memory only.
        private static readonly Dictionary<string, DateTime> waiting = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> greeted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static string Key(string login) { return (login ?? "").Trim().TrimStart('@').ToLowerInvariant(); }

        // The channel as a hash with her own salt: the same channel always gives the same hash, and the hash gives no name.
        internal static string Hash(string channel, string salt)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] made = sha.ComputeHash(Encoding.UTF8.GetBytes((salt ?? "") + "|" + (channel ?? "").Trim().TrimStart('#').ToLowerInvariant()));
                StringBuilder hex = new StringBuilder();
                for (int at = 0; at < 16; at++) hex.Append(made[at].ToString("x2"));
                return hex.ToString();
            }
        }

        private static Dictionary<string, Viewer> All()
        {
            if (known != null && loadedFrom == Kept) return known;
            known = new Dictionary<string, Viewer>(StringComparer.OrdinalIgnoreCase);
            loadedFrom = Kept;
            try
            {
                if (Kept != null && File.Exists(Kept))
                    using (FileStream read = File.OpenRead(Kept))
                    {
                        Viewer[] list = (Viewer[])new DataContractJsonSerializer(typeof(Viewer[])).ReadObject(read);
                        if (list != null) foreach (Viewer one in list) if (one != null && !string.IsNullOrEmpty(one.Login)) known[Key(one.Login)] = one;
                    }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            return known;
        }

        // A busy chat would write the file on every line, so a plain sighting waits up to half a minute.
        private static DateTime savedAt;
        private static bool unsaved;

        private static void Save(bool soon = true)
        {
            if (Kept == null) return;
            if (!soon && (Now() - savedAt).TotalSeconds < 30) { unsaved = true; return; }
            savedAt = Now();
            unsaved = false;
            try
            {
                List<Viewer> list = new List<Viewer>(known.Values);
                // The longest unseen go first once the list is full.
                if (list.Count > Most)
                {
                    list.Sort((a, b) => b.Last.CompareTo(a.Last));
                    list.RemoveRange(Most, list.Count - Most);
                    known.Clear();
                    foreach (Viewer one in list) known[Key(one.Login)] = one;
                }
                string folder = Path.GetDirectoryName(Kept);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                string temp = Kept + ".tmp";
                using (FileStream write = File.Create(temp)) new DataContractJsonSerializer(typeof(Viewer[])).WriteObject(write, list.ToArray());
                if (File.Exists(Kept)) File.Replace(temp, Kept, null); else File.Move(temp, Kept);
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        internal static int Count { get { lock (gate) return All().Count; } }

        internal static Viewer Get(string login)
        {
            lock (gate) { Viewer one; return All().TryGetValue(Key(login), out one) ? one : null; }
        }

        // A line from them on the channel hashed as where. True once a run when they were last seen on another stream.
        internal static bool Seen(string login, string where)
        {
            string key = Key(login);
            if (key.Length == 0) return false;
            lock (gate)
            {
                Dictionary<string, Viewer> all = All();
                DateTime now = Now();
                Viewer one;
                if (!all.TryGetValue(key, out one)) { one = new Viewer { Login = key, First = now, Last = now, Days = 1, Where = where }; all[key] = one; Save(false); return false; }
                bool elsewhere = !string.IsNullOrEmpty(one.Where) && one.Where != where && !greeted.Contains(key);
                if (elsewhere) greeted.Add(key);
                if (one.Last.ToLocalTime().Date != now.ToLocalTime().Date) one.Days++;
                one.Last = now;
                one.Where = where;
                Save(false);
                return elsewhere;
            }
        }

        // Asked which term fits, once: waiting on their answer for two minutes.
        internal static void Asking(string login)
        {
            string key = Key(login);
            lock (gate)
            {
                Dictionary<string, Viewer> all = All();
                Viewer one;
                if (!all.TryGetValue(key, out one)) { DateTime now = Now(); one = new Viewer { Login = key, First = now, Last = now, Days = 1 }; all[key] = one; }
                one.Asked = true;
                waiting[key] = Now();
                Save();
            }
        }

        internal static bool Waiting(string login)
        {
            lock (gate) { DateTime at; return waiting.TryGetValue(Key(login), out at) && (Now() - at).TotalSeconds <= AnswerSeconds; }
        }

        private static readonly Regex NoneOfThem = new Regex(@"^(?:neither|none|nope|no|neutral|non[- ]?binary|enby|they|them|they/them|ni|aucun|keins?|nessuno|nenhum|ninguno|żaden)\b", RegexOptions.IgnoreCase);

        // Their next line after the ask: the term they picked, Neither, or null when it isn't an answer. Either way they are
        // no longer waited on.
        internal static string Answer(string login, string said, List<KeyValuePair<string, string>> terms)
        {
            string key = Key(login);
            lock (gate)
            {
                DateTime at;
                if (!waiting.TryGetValue(key, out at)) return null;
                waiting.Remove(key);
                if ((Now() - at).TotalSeconds > AnswerSeconds) return null;
                string plain = Regex.Replace((said ?? "").Trim().ToLowerInvariant(), @"[^\p{L}\p{N}/ -]+", " ").Trim();
                string found = null;
                foreach (KeyValuePair<string, string> term in terms)
                {
                    string whole = term.Key.ToLowerInvariant(), last = whole.Substring(whole.LastIndexOf(' ') + 1);
                    bool says = Regex.IsMatch(plain, @"(?<![\p{L}])" + Regex.Escape(whole) + @"(?![\p{L}])") || Regex.IsMatch(plain, @"(?<![\p{L}])" + Regex.Escape(last) + @"(?![\p{L}])");
                    if (says && (found == null || whole.Length > found.Length)) found = term.Key;
                }
                if (found == null && NoneOfThem.IsMatch(plain)) found = Neither;
                if (found == null) return null;
                Viewer one;
                if (All().TryGetValue(key, out one)) { one.Call = found; Save(); }
                return found;
            }
        }

        internal static void Forget()
        {
            lock (gate)
            {
                All().Clear();
                waiting.Clear();
                greeted.Clear();
                try { if (Kept != null && File.Exists(Kept)) File.Delete(Kept); }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
        }

        // A new run of reading chat: "another stream" may be said once more to each.
        internal static void NewStream() { lock (gate) greeted.Clear(); }

        // What waited to be written, written now: when chat stops and when she closes.
        internal static void Flush() { lock (gate) if (unsaved && known != null) Save(); }

        // The ask on a first donation, from their terms: "Good boy, good girl or good gremlin?"
        internal static string AskLine(List<KeyValuePair<string, string>> terms)
        {
            List<string> said = new List<string>();
            foreach (KeyValuePair<string, string> term in terms) if (!said.Contains(term.Key)) said.Add(term.Key);
            if (said.Count == 0) return null;
            string joined = said.Count == 1 ? said[0] : string.Join(", ", said.GetRange(0, said.Count - 1).ToArray()) + " or " + said[said.Count - 1];
            return char.ToUpperInvariant(joined[0]) + joined.Substring(1) + "?";
        }
    }
}
