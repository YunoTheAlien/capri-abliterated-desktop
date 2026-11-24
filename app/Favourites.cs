using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Globalization;
using System.IO;
using System.Text;

namespace CapriDesktop
{
    // Which program they actually use for a thing, learned from what they put in front, never from her own openings, so
    // a wrong guess can't reinforce itself. Counts decay like her hours; nothing here is sent anywhere.
    internal static class Favourites
    {
        internal const string FileName = "apps-used.txt";
        internal static string Kept;

        private static readonly object gate = new object();
        private static readonly Dictionary<string, double> counts = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        // What they have told her outright, and what they have told her it is not.
        private static readonly Dictionary<string, string> picks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> refused = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static bool read;
        private static string marked;
        private static DateTime markedAt = DateTime.MinValue, faded = DateTime.MinValue, saved = DateTime.MinValue;

        private const double Fade = 0.95;
        // Below this it has not been in front enough to call it theirs.
        private const double Enough = 3.0;
        // One count per program per spell in front, so leaving something open all day is one use, not a landslide.
        private static readonly TimeSpan Spell = TimeSpan.FromMinutes(4);

        // The kinds she is ever asked for, and the programs that count as each. Matched on the name containing
        // one of these, so "brave.exe" and "Brave Browser" both count.
        private static readonly Dictionary<string, string[]> Kinds = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "browser", new[] { "brave", "chrome", "firefox", "msedge", "edge", "opera", "vivaldi", "librewolf", "waterfox", "zen" } },
            { "calendar", new[] { "outlook", "calendar", "thunderbird", "em client", "emclient", "mailbird", "morgen", "notion" } },
            { "music", new[] { "spotify", "foobar", "musicbee", "winamp", "aimp", "itunes", "applemusic", "deezer", "tidal", "vlc" } },
            { "images", new[] { "qview", "irfanview", "xnview", "honeyview", "nomacs", "imageglass", "photos", "paint.net", "photoshop" } },
            // Programming and plain text are two kinds, asked about apart.
            { "code editor", new[] { "code", "codium", "sublime", "rider", "devenv", "idea", "pycharm" } },
            { "text editor", new[] { "notepad++", "notepad" } },
            { "chat", new[] { "discord", "slack", "teams", "telegram", "signal", "whatsapp" } }
        };

        // What people call each kind, for asking about it.
        private static readonly Dictionary<string, string> Called = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "browser", "browser" }, { "calendar", "calendar" }, { "music", "music player" },
            { "images", "image viewer" }, { "code editor", "code editor" }, { "text editor", "text editor" }, { "chat", "chat app" }
        };

        // What they might call a kind out loud, for "open my browser". The kind's own name and the friendly one
        // are both in here, with the words people put around them stripped before the lookup.
        private static readonly Dictionary<string, string> Asked = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "browser", "browser" }, { "web browser", "browser" }, { "internet", "browser" },
            { "calendar", "calendar" }, { "diary", "calendar" }, { "schedule", "calendar" },
            { "music", "music" }, { "music player", "music" }, { "music app", "music" }, { "tunes", "music" },
            { "images", "images" }, { "image viewer", "images" }, { "photo viewer", "images" }, { "gallery", "images" },
            { "editor", "editor" }, { "code editor", "code editor" }, { "text editor", "text editor" }, { "ide", "code editor" },
            { "chat", "chat" }, { "chat app", "chat" }, { "messenger", "chat" }
        };

        // The kind someone means, or null when they named an actual app. "my browser", "the browser", "browser
        // please" all land on browser; "brave" does not, because that is a program and they said which one.
        internal static string KindOf(string said)
        {
            if (string.IsNullOrWhiteSpace(said)) return null;
            string want = said.Trim().ToLowerInvariant();
            want = Regex.Replace(want, @"^(?:please\s+|open\s+|launch\s+|start\s+|run\s+)+", "");
            want = Regex.Replace(want, @"^(?:my|our|the|a|some)\s+", "");
            want = Regex.Replace(want, @"\s+(?:please|now|up|for me)$", "");
            want = Regex.Replace(want, @"[^a-z ]", "").Trim();
            string kind;
            return Asked.TryGetValue(want, out kind) ? kind : null;
        }

        internal static string Name(string kind)
        {
            string said;
            return Called.TryGetValue(kind ?? "", out said) ? said : kind;
        }

        // Every one of this kind she has seen them use, most used first.
        internal static List<string> Choices(string kind)
        {
            List<string> found = new List<string>();
            string[] names;
            if (string.IsNullOrEmpty(kind) || !Kinds.TryGetValue(kind, out names)) return found;
            lock (gate)
            {
                Load();
                List<KeyValuePair<string, double>> all = new List<KeyValuePair<string, double>>();
                foreach (KeyValuePair<string, double> one in counts)
                    if (!one.Key.StartsWith(WebMark, StringComparison.OrdinalIgnoreCase))
                        foreach (string name in names)
                            if (one.Key.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) { all.Add(one); break; }
                all.Sort(delegate(KeyValuePair<string, double> x, KeyValuePair<string, double> y) { return y.Value.CompareTo(x.Value); });
                foreach (KeyValuePair<string, double> one in all) found.Add(one.Key);
            }
            return found;
        }

        // Every program she has seen in front, most used first, with how much; sites in a browser aren't programs.
        internal static List<KeyValuePair<string, double>> Programs()
        {
            List<KeyValuePair<string, double>> all = new List<KeyValuePair<string, double>>();
            lock (gate)
            {
                Load();
                foreach (KeyValuePair<string, double> one in counts) if (!one.Key.StartsWith(WebMark, StringComparison.OrdinalIgnoreCase)) all.Add(one);
            }
            all.Sort(delegate(KeyValuePair<string, double> x, KeyValuePair<string, double> y) { return y.Value.CompareTo(x.Value); });
            return all;
        }

        // The kind a program is, by the same names her asks use, or null.
        internal static string KindOfProgram(string program)
        {
            foreach (KeyValuePair<string, string[]> kind in Kinds)
                foreach (string name in kind.Value)
                    if ((program ?? "").IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) return kind.Key;
            return null;
        }

        // A program seen in front as a game, by her own game check, kept beside her other picks.
        private const string GameMark = "game:";

        internal static void Play(string program)
        {
            string plain = Plain(program);
            if (plain.Length == 0) return;
            lock (gate)
            {
                Load();
                if (picks.ContainsKey(GameMark + plain)) return;
                picks[GameMark + plain] = "yes";
                Write();
            }
        }

        internal static bool Played(string program)
        {
            lock (gate) { Load(); return picks.ContainsKey(GameMark + Plain(program)); }
        }

        // Their own word, which beats any amount of counting.
        internal static void Pick(string kind, string program)
        {
            if (string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(program)) return;
            lock (gate) { Load(); picks[kind] = Plain(program); Write(); }
        }

        // "No, not that one", so she does not ask about it again.
        internal static void NotThat(string kind, string program)
        {
            if (string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(program)) return;
            lock (gate)
            {
                Load();
                string had;
                string plain = Plain(program);
                refused[kind] = refused.TryGetValue(kind, out had) && had.Length > 0 ? had + "|" + plain : plain;
                Write();
            }
        }

        private static bool Turned(string kind, string program)
        {
            string had;
            if (!refused.TryGetValue(kind ?? "", out had) || string.IsNullOrEmpty(had)) return false;
            foreach (string one in had.Split('|'))
                if (string.Equals(one, program, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // The one to ask about: seen enough, not already picked, and not one they have ruled out. Null when
        // there is nothing worth asking, which includes there being only one of them.
        internal static string Ask(string kind)
        {
            if (!string.IsNullOrEmpty(Picked(kind))) return null;
            List<string> all = Choices(kind);
            List<string> worth = new List<string>();
            lock (gate)
            {
                foreach (string one in all)
                {
                    double had;
                    if (counts.TryGetValue(one, out had) && had >= 2 && !Turned(kind, one)) worth.Add(one);
                }
            }
            // One of them needs no question, and nothing seen needs no question either.
            return worth.Count >= 2 ? worth[0] : null;
        }

        internal static string Picked(string kind)
        {
            lock (gate)
            {
                Load();
                string had;
                return picks.TryGetValue(kind ?? "", out had) && had.Length > 0 ? had : null;
            }
        }

        // Theirs, for this kind of thing, or null when nothing has been in front enough to say.
        // What they have written in a file of theirs for a kind of app. Set at startup, so nothing in here
        // reads her files or knows which file it is.
        internal static Func<string, string> Written = delegate { return null; };

        internal static string Best(string kind)
        {
            // "My editor" on its own is whichever of their code and text editors they use more.
            if (string.Equals(kind, "editor", StringComparison.OrdinalIgnoreCase)) return EitherEditor();
            // Written down beats counted and beats picked: typing an app's name into a file of hers is the
            // most deliberate thing anybody can do about it, and it was being ignored entirely.
            string wrote = null;
            try { wrote = Written(kind); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            if (!string.IsNullOrEmpty(wrote)) return wrote;
            string told = Picked(kind);
            if (told != null) return told;
            string[] names;
            if (string.IsNullOrEmpty(kind) || !Kinds.TryGetValue(kind, out names)) return null;
            lock (gate)
            {
                Load();
                string best = null;
                double most = Enough;
                foreach (KeyValuePair<string, double> one in counts)
                {
                    if (one.Value < most || one.Key.StartsWith(WebMark, StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (string name in names)
                        if (one.Key.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) { best = one.Key; most = one.Value; break; }
                }
                return best;
            }
        }

        private static string EitherEditor()
        {
            string code = Best("code editor"), text = Best("text editor");
            if (code == null || text == null) return code ?? text;
            lock (gate)
            {
                double codeUsed, textUsed;
                counts.TryGetValue(code, out codeUsed);
                counts.TryGetValue(text, out textUsed);
                return textUsed > codeUsed ? text : code;
            }
        }

        // An answer from before the split, "editor", goes to whichever kind its program is.
        private static void SplitEditor()
        {
            string had;
            if (picks.TryGetValue("editor", out had))
            {
                picks.Remove("editor");
                string kind = KindHolding(had);
                if (kind != null && !picks.ContainsKey(kind)) picks[kind] = had;
            }
            if (refused.TryGetValue("editor", out had))
            {
                refused.Remove("editor");
                foreach (string one in had.Split('|'))
                {
                    string kind = KindHolding(one), kept;
                    if (kind == null || one.Length == 0) continue;
                    refused[kind] = refused.TryGetValue(kind, out kept) && kept.Length > 0 ? kept + "|" + one : one;
                }
            }
        }

        // The editor kind a program belongs to, or null.
        private static string KindHolding(string program)
        {
            foreach (string kind in new[] { "text editor", "code editor" })
                foreach (string name in Kinds[kind])
                    if ((program ?? "").IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) return kind;
            return null;
        }

        // What is in front now. Called on her slow tick, not every frame.
        internal static void Seen(string program, DateTime now)
        {
            string plain = Plain(program);
            if (plain.Length == 0) return;
            lock (gate)
            {
                Load();
                Decay(now);
                // The same thing still in front is the same use of it.
                if (string.Equals(plain, marked, StringComparison.OrdinalIgnoreCase) && now - markedAt < Spell) return;
                marked = plain;
                markedAt = now;
                double had;
                counts[plain] = (counts.TryGetValue(plain, out had) ? had : 0) + 1;
                if (now - saved >= TimeSpan.FromMinutes(5)) { saved = now; Write(); }
            }
        }

        // Sites that are also an app on the PC. With a browser in front, the window title says which one is open, so
        // Spotify in a tab counts for Spotify on the web and Spotify.exe for the app.
        private static readonly string[] Both = { "spotify", "discord", "slack", "teams", "whatsapp", "telegram", "notion", "figma", "outlook", "deezer", "tidal", "netflix", "steam", "twitch" };
        private const string WebMark = "web:";
        private static string webMarked;
        private static DateTime webMarkedAt = DateTime.MinValue;

        internal static void Seen(string program, string title, DateTime now)
        {
            Seen(program, now);
            string site = WebSite(program, title);
            if (site == null) return;
            lock (gate)
            {
                if (string.Equals(site, webMarked, StringComparison.OrdinalIgnoreCase) && now - webMarkedAt < Spell) return;
                webMarked = site;
                webMarkedAt = now;
                double had;
                counts[WebMark + site] = (counts.TryGetValue(WebMark + site, out had) ? had : 0) + 1;
            }
        }

        // The site in a browser's title, or null when a browser is not what is in front.
        internal static string WebSite(string program, string title)
        {
            string plain = Plain(program);
            if (plain.Length == 0 || string.IsNullOrEmpty(title)) return null;
            bool browser = false;
            foreach (string name in Kinds["browser"]) if (plain.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) browser = true;
            if (!browser) return null;
            foreach (string site in Both)
                if (Regex.IsMatch(title, @"\b" + Regex.Escape(site) + @"\b", RegexOptions.IgnoreCase)) return site;
            return null;
        }

        // The app or the page for a site that is both: "app" or "web" when they said so or plainly use one, "both" when
        // they use each about as much, and null when she has not seen enough to say.
        internal static string Where(string site)
        {
            string key = (site ?? "").Trim().ToLowerInvariant();
            if (key.Length == 0) return null;
            string told = Picked("where:" + key);
            if (told != null) return told;
            double app = 0, web = 0;
            lock (gate)
            {
                Load();
                foreach (KeyValuePair<string, double> one in counts)
                {
                    if (one.Key.StartsWith(WebMark, StringComparison.OrdinalIgnoreCase)) { if (string.Equals(one.Key, WebMark + key, StringComparison.OrdinalIgnoreCase)) web = one.Value; }
                    else if (one.Key.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0) app = Math.Max(app, one.Value);
                }
            }
            if (app < Enough && web < Enough) return null;
            if (app >= web * 2) return "app";
            if (web >= app * 2) return "web";
            return "both";
        }

        // Their word on it, which becomes the one she uses until they say otherwise.
        internal static void Tell(string site, string where)
        {
            string key = (site ?? "").Trim().ToLowerInvariant();
            if (key.Length == 0 || string.IsNullOrEmpty(where)) return;
            lock (gate) { Load(); picks["where:" + key] = where; Write(); }
        }

        // Her opening something is one use of it and no more, so her own guess cannot vote for itself.
        internal static void SheOpened(string program)
        {
            string plain = Plain(program);
            if (plain.Length == 0) return;
            lock (gate) { marked = plain; markedAt = DateTime.UtcNow; }
        }

        private static string Plain(string program)
        {
            string name = (program ?? "").Trim();
            if (name.Length == 0) return "";
            try { name = Path.GetFileNameWithoutExtension(name); }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            return (name ?? "").Trim();
        }

        private static void Decay(DateTime now)
        {
            if (faded == DateTime.MinValue) { faded = now.Date; return; }
            int days = (int)(now.Date - faded).TotalDays;
            if (days <= 0) return;
            faded = now.Date;
            List<string> gone = new List<string>();
            List<string> keys = new List<string>(counts.Keys);
            foreach (string key in keys)
            {
                double left = counts[key] * Math.Pow(Fade, Math.Min(days, 90));
                if (left < 0.2) gone.Add(key); else counts[key] = left;
            }
            foreach (string key in gone) counts.Remove(key);
        }

        private static void Load()
        {
            if (read) return;
            read = true;
            try
            {
                if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept)) return;
                foreach (string row in File.ReadAllLines(Kept))
                {
                    int tab = (row ?? "").LastIndexOf('\t');
                    double count;
                    if (row.StartsWith("pick:", StringComparison.OrdinalIgnoreCase) || row.StartsWith("notthat:", StringComparison.OrdinalIgnoreCase)) continue;
                    if (tab <= 0 || !double.TryParse(row.Substring(tab + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out count)) continue;
                    if (row.StartsWith("faded\t", StringComparison.OrdinalIgnoreCase)) continue;
                    counts[row.Substring(0, tab)] = count;
                }
                DateTime when;
                foreach (string row in File.ReadAllLines(Kept))
                {
                    if (row.StartsWith("faded\t", StringComparison.OrdinalIgnoreCase)
                        && DateTime.TryParse(row.Substring(6), CultureInfo.InvariantCulture, DateTimeStyles.None, out when)) faded = when.Date;
                    else if (row.StartsWith("pick:", StringComparison.OrdinalIgnoreCase))
                    {
                        string[] parts = row.Substring(5).Split('\t');
                        if (parts.Length == 2) picks[parts[0]] = parts[1];
                    }
                    else if (row.StartsWith("notthat:", StringComparison.OrdinalIgnoreCase))
                    {
                        string[] parts = row.Substring(8).Split('\t');
                        if (parts.Length == 2) refused[parts[0]] = parts[1];
                    }
                }
                SplitEditor();
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        private static void Write()
        {
            try
            {
                if (string.IsNullOrEmpty(Kept)) return;
                StringBuilder made = new StringBuilder();
                made.Append("faded\t").Append(faded.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(Environment.NewLine);
                foreach (KeyValuePair<string, string> one in picks)
                    made.Append("pick:").Append(one.Key).Append('\t').Append(one.Value).Append(Environment.NewLine);
                foreach (KeyValuePair<string, string> one in refused)
                    made.Append("notthat:").Append(one.Key).Append('\t').Append(one.Value).Append(Environment.NewLine);
                foreach (KeyValuePair<string, double> one in counts)
                    made.Append(one.Key).Append('\t').Append(one.Value.ToString("0.##", CultureInfo.InvariantCulture)).Append(Environment.NewLine);
                File.WriteAllText(Kept, made.ToString(), new UTF8Encoding(false));
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        internal static void Save() { lock (gate) { if (read) Write(); } }

        // Everything let go, so the file at Kept is read afresh, for her checks. The editor split: "my editor", "code
        // editor" and "text editor" each mean their kind; "my editor" opens whichever is used more, and an older answer
        // keeps its program.
        internal static void CheckEditors(List<string> checks, string output)
        {
            string keptWas = Kept;
            string kept = System.IO.Path.Combine(output, "favourites-editors.txt");
            try
            {
                File.WriteAllText(kept, "pick:editor\tcodium\nnotthat:editor\tnotepad\n");
                Kept = kept;
                Reload();
                string fromBefore = Picked("code editor"), ruledOut = null;
                lock (gate) refused.TryGetValue("text editor", out ruledOut);
                bool kinds = KindOf("my editor") == "editor" && KindOf("code editor") == "code editor" && KindOf("the ide") == "code editor" && KindOf("text editor") == "text editor";
                File.WriteAllText(kept, "");
                Reload();
                DateTime at = DateTime.UtcNow;
                for (int i = 0; i < 6; i++) { Seen("Code.exe", at); Seen("explorer.exe", at); at = at.AddMinutes(10); }
                for (int i = 0; i < 4; i++) { Seen("notepad.exe", at); Seen("explorer.exe", at); at = at.AddMinutes(10); }
                string code = Best("code editor"), text = Best("text editor"), either = Best("editor");
                for (int i = 0; i < 6; i++) { Seen("notepad.exe", at); Seen("explorer.exe", at); at = at.AddMinutes(10); }
                string more = Best("editor");
                if (fromBefore != "codium" || ruledOut != "notepad" || !kinds || code == null || code.IndexOf("code", StringComparison.OrdinalIgnoreCase) < 0
                    || text == null || text.IndexOf("notepad", StringComparison.OrdinalIgnoreCase) < 0 || either != code || more != text)
                    throw new Exception("The editor split went wrong: from before " + fromBefore + " / ruled out " + ruledOut + " / kinds " + kinds + " / code " + code + " / text " + text + " / editor " + either + " then " + more);
                checks.Add("PASS code editors and text editors are two kinds: \"my editor\" opens whichever they use more, \"code editor\" and \"the IDE\" mean code, \"text editor\" means text, and an answer from before the split keeps its program under its own kind");
            }
            finally
            {
                Kept = keptWas;
                Reload();
                try { File.Delete(kept); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
        }

        internal static void Reload()
        {
            lock (gate)
            {
                counts.Clear(); picks.Clear(); refused.Clear();
                read = false; marked = webMarked = null;
                markedAt = webMarkedAt = faded = saved = DateTime.MinValue;
            }
        }
    }
}
