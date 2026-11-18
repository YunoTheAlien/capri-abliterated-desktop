using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CapriDesktop
{
    // Capri on Twitch. Everything about it lives in twitch.md beside her settings, so it can be edited while she is running
    // and none of it needs a rebuild.
    //
    // She is a chatbot there and nothing else. Twitch replies go through Brain.Aside, a one-shot call with no tools wired
    // into it at all, so "open Discord", "read your settings" or "what is in twitch.md" from chat cannot reach anything.
    // That is structural rather than a line in a prompt someone could argue her out of, which is the only way to do it when
    // the input comes from strangers. She also never sends anything back to Twitch and never moderates: nothing here can
    // time out, ban or delete, and the connection is read only.
    internal sealed class TwitchRules
    {
        internal string Channel = "";
        internal string Login = "";
        internal string Token = "";
        internal int GeneralSeconds = 90;
        internal int CalloutSeconds = 20;
        internal string Callout = "!capri";
        internal int CheckSeconds = 20 * 60;
        internal int AgainSeconds = 3 * 3600;
        internal int QuietSeconds = 12 * 3600;
        internal int AtMost = 3;
        internal int BullySeconds = 5 * 60;
        internal int PrankSeconds = 30;
        private readonly List<string> words = new List<string>();
        private readonly List<string> watch = new List<string>();
        private readonly HashSet<string> users = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> bully = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // What chat is allowed to set off, and nothing else. An empty list means chat can set off nothing at all.
        internal bool Bully(string word) { return bully.Contains((word ?? "").Trim().TrimStart('!')); }
        internal int BullyCount { get { return bully.Count; } }

        internal List<string> Watching { get { return new List<string>(watch); } }

        internal int WordCount { get { return words.Count; } }
        internal int UserCount { get { return users.Count; } }

        // Curly apostrophes and the rest of Unicode's lookalikes are folded first, so a banned "what's your" still catches
        // "what’s your" typed from a phone.
        private static string Plain(string text)
        {
            string said = (text ?? "").Replace('’', '\'').Replace('‘', '\'').Replace('ʼ', '\'');
            return said.Replace('–', '-').Replace('—', '-');
        }

        // A banned word on its own matches only as a whole word, so "ip" does not fire inside "script" or "chip". Anything
        // with a space in it is a phrase and matches anywhere, which is how "where do you" and "what's your" are caught.
        internal string Hit(string text)
        {
            string said = Plain(text);
            foreach (string word in words)
            {
                if (word.IndexOf(' ') >= 0)
                {
                    if (said.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0) return word;
                }
                else if (Regex.IsMatch(said, @"(?<![\p{L}\p{N}])" + Regex.Escape(word) + @"(?![\p{L}\p{N}])", RegexOptions.IgnoreCase)) return word;
            }
            return null;
        }

        internal bool Banned(string user) { return users.Contains((user ?? "").Trim().TrimStart('@')); }

        // That word taken out of a line, matched the same way Hit finds it, with the spacing tidied after.
        internal static string Without(string text, string word)
        {
            // Folded the same way Hit folds it, or a curly apostrophe would be found and then not removed.
            string said = Plain(text);
            string gone = word.IndexOf(' ') >= 0
                ? Regex.Replace(said, Regex.Escape(word), " ", RegexOptions.IgnoreCase)
                : Regex.Replace(said, @"(?<![\p{L}\p{N}])" + Regex.Escape(word) + @"(?![\p{L}\p{N}])", " ", RegexOptions.IgnoreCase);
            gone = Regex.Replace(gone, @"\s{2,}", " ");
            return Regex.Replace(gone, @"\s+([,.!?;:])", "$1").Trim();
        }

        // Chat that is only an emote is not worth a reply, but the emote is not a banned word either: she should still be
        // able to say KEKW herself. So these are their own list, checked on the way in and never on the way out.
        internal bool Ignored(string text)
        {
            string said = Plain(text).Trim();
            if (said.Length == 0) return true;
            foreach (string word in Regex.Split(said, @"\s+"))
            {
                string bare = word.Trim('.', ',', '!', '?', ')', '(', '"', '\'');
                if (bare.Length == 0) continue;
                if (!ignored.Contains(bare)) return false;
            }
            return true;
        }

        private readonly HashSet<string> ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal int IgnoredCount { get { return ignored.Count; } }

        // A link or a wall of emotes has nothing in it to answer, so neither spends a reply or starts the timer.
        private static readonly Regex Link = new Regex(@"(?:https?://|www\.)|[\p{L}\p{N}-]+\.(?:com|net|org|io|tv|gg|me|ly|xyz|shop|link|info|biz|online|site|store|club|ru|co|uk)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        internal static bool Worth(string text)
        {
            string said = (text ?? "").Trim();
            if (said.Length < 2) return false;
            if (Link.IsMatch(said)) return false;
            foreach (string piece in Regex.Replace(said, @"[^\p{L}]+", " ").Split(' ')) if (piece.Length >= 2) return true;
            return false;
        }

        // twitch.md, by its headings. Unknown headings are ignored rather than refused, so notes can be kept in the file.
        internal static TwitchRules Read(string path)
        {
            TwitchRules rules = new TwitchRules();
            if (path == null || !File.Exists(path)) return rules;
            string zone = "";
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = (raw ?? "").Trim();
                if (line.Length == 0 || line.StartsWith(">", StringComparison.Ordinal) || line.StartsWith("<!--", StringComparison.Ordinal)) continue;
                if (line.StartsWith("#", StringComparison.Ordinal))
                {
                    zone = line.TrimStart('#').Trim().ToLowerInvariant();
                    continue;
                }
                if (line.StartsWith("-", StringComparison.Ordinal) || line.StartsWith("*", StringComparison.Ordinal)) line = line.Substring(1).Trim();
                if (line.Length == 0) continue;
                // Every list takes one per line or several on a line separated by commas, whichever is less tedious to
                // write. A banned phrase holds spaces but never a comma, so splitting on commas cannot break one up.
                if (zone == "streamer bully" || zone == "streamer_bully")
                {
                    foreach (string piece in line.Split(',')) { string act = piece.Trim().TrimStart('!').ToLowerInvariant(); if (act.Length > 0) rules.bully.Add(act); }
                    continue;
                }
                if (zone == "banned words" || zone == "banned users" || zone == "ignored" || zone == "channels to watch" || zone == "users to watch")
                {
                    foreach (string piece in line.Split(','))
                    {
                        string item = piece.Trim();
                        if (item.Length == 0) continue;
                        if (zone == "banned words") { string word = Plain(item).ToLowerInvariant(); if (!rules.words.Contains(word)) rules.words.Add(word); }
                        else if (zone == "banned users") rules.users.Add(item.TrimStart('@'));
                        else if (zone == "ignored") rules.ignored.Add(item);
                        // Order matters here and nowhere else: when more are live than she will mention, the top ones win.
                        else { string who = item.TrimStart('@'); if (!rules.watch.Contains(who)) rules.watch.Add(who); }
                    }
                    continue;
                }
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                string key = line.Substring(0, colon).Trim().ToLowerInvariant();
                string value = line.Substring(colon + 1).Trim();
                if (key == "channel") rules.Channel = value.TrimStart('#').Replace("https://", "").Replace("http://", "").Replace("www.", "").Replace("twitch.tv/", "").Trim('/').Trim();
                else if (key == "login") rules.Login = value;
                else if (key == "token" || key == "api" || key == "oauth") rules.Token = value.Replace("oauth:", "").Trim();
                else if (key == "general" || key == "general timer") rules.GeneralSeconds = Span(value, rules.GeneralSeconds, 1);
                else if (key == "callout" || key == "callout timer") rules.CalloutSeconds = Span(value, rules.CalloutSeconds, 1);
                else if (key == "prefix" || key == "command") rules.Callout = value.Length == 0 ? rules.Callout : value.ToLowerInvariant();
                else if (key == "check every") rules.CheckSeconds = Span(value, rules.CheckSeconds, 60);
                else if (key == "mention again") rules.AgainSeconds = Span(value, rules.AgainSeconds, 3600);
                else if (key == "still live for") rules.QuietSeconds = Span(value, rules.QuietSeconds, 3600);
                else if (key == "at most") rules.AtMost = Math.Max(1, Number(value, rules.AtMost));
                else if (key == "bully") rules.BullySeconds = Span(value, rules.BullySeconds, 1);
                // Never nothing and never shorter than half a minute: a prank with no end, or one gone before it is seen,
                // is not something the file should be able to ask for.
                else if (key == "prank for") rules.PrankSeconds = Math.Max(30, Span(value, rules.PrankSeconds, 1));
            }
            return rules;
        }

        private static int Number(string value, int fallback)
        {
            Match digits = Regex.Match(value ?? "", @"\d+");
            int found;
            return digits.Success && int.TryParse(digits.Value, out found) ? Math.Max(0, found) : fallback;
        }

        // Every timer in the file is written the same way: 30s, 20m, 3h. A bare number keeps working and means whatever
        // that line used to mean, which is what `bare` carries, so a file written before the suffixes still reads the same.
        private static int Span(string value, int fallback, int bare)
        {
            Match timed = Regex.Match(value ?? "", @"(\d+)\s*([smh])?", RegexOptions.IgnoreCase);
            int found;
            if (!timed.Success || !int.TryParse(timed.Groups[1].Value, out found)) return fallback;
            string unit = timed.Groups[2].Success ? timed.Groups[2].Value.ToLowerInvariant() : "";
            int each = unit == "s" ? 1 : unit == "m" ? 60 : unit == "h" ? 3600 : bare;
            return Math.Max(0, found) * each;
        }

        internal const string FileName = "twitch.md";

        // Where the file lives, set once at startup, so her own editor can open it as a third page beside personality.md
        // and me.md rather than handing it to whatever Windows has registered for.md.
        internal static string Kept;
        private static readonly object writing = new object();

        internal static WrittenNote Open()
        {
            return new WrittenNote(Kept, Default, writing);
        }

        // What chat may set off, and what it said before her red alert existed. A list somebody has edited is
        // theirs: deleting a word is the documented way to switch that prank off, so only a list that still
        // reads exactly as the old one shipped is brought up to date.
        internal const string BullyStock = "crash, clones, rampage, dance, mess, alert";
        private const string BullyWas = "crash, clones, rampage, dance, mess";
        // The note under that list, which has to go back with the word: a prank nothing explains is half a
        // prank. Again by exact match, so a note somebody has written over is theirs.
        private const string BullyNoteWas = "> mess: your icons scatter for a bit. None of them touches a file.";
        private static readonly string[] BullyNoteNow =
        {
            "> mess: your icons scatter for a bit. alert: six seconds of flashing red hexagons across your",
            "> screens with her alarm over them, ending on any key. None of them touches a file."
        };

        // The timers she ships and the note explaining them, in one place. The file she writes and the top-up
        // into a file that already exists are both built from this, so the two can never come to say
        // different things, which is how prank for ended up shipped in the code and in no file on disk.
        private static readonly string[,] TimerStock =
        {
            { "general", "90s" }, { "callout", "20s" }, { "check every", "20m" }, { "mention again", "3h" },
            { "still live for", "12h" }, { "at most", "3" }, { "bully", "5m" }, { "prank for", "30s" },
            { "prefix", "!capri" }
        };

        private static readonly string[] TimerNotes =
        {
            "> Every time takes s, m or h: 30s, 20m, 3h.",
            "> general: gap between replies to ordinary chat. callout: gap for !capri. 0 means every message.",
            "> check every: how often she looks for who is live.",
            "> mention again: how long before she brings the same person up twice.",
            "> still live for: how long they count as still live, so a long stream is not announced over and over.",
            "> at most: how many she mentions at once.",
            "> bully: gap between pranks chat sets off.",
            "> prank for: how long a prank lasts before she clears it up. 30s is the shortest she will take."
        };

        private static string TimerBlock
        {
            get
            {
                List<string> made = new List<string>();
                for (int one = 0; one < TimerStock.GetLength(0); one++) made.Add(TimerStock[one, 0] + ": " + TimerStock[one, 1]);
                made.Add("");
                made.AddRange(TimerNotes);
                made.Add("");
                return string.Join(Environment.NewLine, made.ToArray());
            }
        }

        // A twitch.md written before a timer existed never gains it: the file is only written when it is not
        // there at all. So it is read here, and anything missing from the Timers section is put in with the
        // note that explains it, their own values left exactly as they are. Nothing else in the file is
        // touched, and a file with everything in it is not rewritten.
        internal static void Carried(string path)
        {
            try
            {
                if (path == null || !File.Exists(path)) return;
                List<string> lines = new List<string>(File.ReadAllLines(path));
                int heading = -1, ends = lines.Count;
                for (int one = 0; one < lines.Count; one++)
                {
                    string said = lines[one].Trim();
                    if (heading < 0 && said.StartsWith("#", StringComparison.Ordinal) && said.TrimStart('#').Trim().Equals("Timers", StringComparison.OrdinalIgnoreCase)) { heading = one; continue; }
                    if (heading >= 0 && said.StartsWith("#", StringComparison.Ordinal)) { ends = one; break; }
                }
                if (heading < 0) return;
                HashSet<string> had = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int lastSetting = heading;
                for (int one = heading + 1; one < ends; one++)
                {
                    string said = lines[one].Trim();
                    if (said.Length == 0 || said.StartsWith(">", StringComparison.Ordinal)) continue;
                    int colon = said.IndexOf(':');
                    if (colon <= 0) continue;
                    had.Add(said.Substring(0, colon).Trim());
                    lastSetting = one;
                }
                List<string> adding = new List<string>();
                for (int one = 0; one < TimerStock.GetLength(0); one++)
                    if (!had.Contains(TimerStock[one, 0])) adding.Add(TimerStock[one, 0] + ": " + TimerStock[one, 1]);
                // The prank list, while the file is open anyway. Untouched means the old line and nothing else.
                bool prankList = false;
                for (int one = 0; one < lines.Count; one++)
                {
                    if (lines[one].Trim() != BullyWas) continue;
                    lines[one] = lines[one].Replace(BullyWas, BullyStock);
                    prankList = true;
                    break;
                }
                for (int one = 0; one < lines.Count; one++)
                {
                    if (lines[one].Trim() != BullyNoteWas) continue;
                    lines.RemoveAt(one);
                    lines.InsertRange(one, BullyNoteNow);
                    prankList = true;
                    break;
                }
                if (adding.Count == 0)
                {
                    if (prankList) File.WriteAllText(path, string.Join(Environment.NewLine, lines.ToArray()) + Environment.NewLine, new System.Text.UTF8Encoding(false));
                    return;
                }
                lines.InsertRange(lastSetting + 1, adding);
                ends += adding.Count;
                // The note goes back whole, since a line added with nothing explaining it is half a setting.
                int from = -1, to = -1;
                for (int one = lastSetting + 1 + adding.Count; one < ends; one++)
                {
                    if (!lines[one].Trim().StartsWith(">", StringComparison.Ordinal)) continue;
                    if (from < 0) from = one;
                    to = one;
                }
                if (from >= 0) { lines.RemoveRange(from, to - from + 1); lines.InsertRange(from, TimerNotes); }
                File.WriteAllText(path, string.Join(Environment.NewLine, lines.ToArray()) + Environment.NewLine, new System.Text.UTF8Encoding(false));
            }
            catch (Exception bad) { Notes.Failed("carrying a shipped value over in Twitch.cs", bad); }
        }

        // What the file says when she makes it. The doxxing words are the ones asked for; the slur list is left empty on
        // purpose, with a note, so nobody has to read a list of slurs in a source file to know how it works.
        internal static string Default
        {
            get
            {
                return string.Join(Environment.NewLine, new[]
                {
                    "# Capri on Twitch",
                    "",
                    "> Read fresh every message, so edit it while she runs.",
                    "> On Twitch she is a chatbot only: no apps, no keys, no files, no moderating, and she never posts back.",
                    "",
                    "## What you can say to her",
                    "",
                    "> Spoken or typed, read without her brain. These only turn the live mentions on and off.",
                    ">",
                    ">   turn off twitch live notifications",
                    ">   turn on twitch live notifications",
                    ">   stop telling me who's live",
                    ">",
                    "> Right-click her for Twitch > Read chat, Twitch > Edit twitch.md, and Clear memory.",
                    "",
                    "## My channel",
                    "",
                    "channel:",
                    "",
                    "> The chat she reads. Name or link. No account needed, so any live channel works.",
                    "",
                    "## Channels to watch",
                    "",
                    "> She tells you when these go live. One per line or commas, best first.",
                    "",
                    "## Timers",
                    "",
                    TimerBlock,
                    "## Streamer bully",
                    "",
                    "> What chat can set off on your screen with !capri <word>. Delete a word to switch it off, empty the",
                    "> list and chat can do nothing. These ignore full screen and the stream on purpose, but never fire while",
                    "> she has your mouse or is mid-job. They are the only things chat can ever make her do.",
                    "",
                    BullyStock,
                    "",
                    "> crash: her fake blue screen for five seconds. clones: copies of her that wander until cleared.",
                    "> rampage: your desktop icons fly around for ten seconds, then go home. dance: she dances.",
                    "> mess: your icons scatter for a bit. alert: six seconds of flashing red hexagons across your",
                    "> screens with her alarm over them, ending on any key. None of them touches a file.",
                    "",
                    "## Connection",
                    "",
                    "token:",
                    "login:",
                    "",
                    "> Leave blank. Only needed the day you want her posting into chat as an account.",
                    "",
                    "## Banned words",
                    "",
                    "> One per line or commas. She stays silent on these, and will not say them herself while reading chat.",
                    "> One word matches whole, so ip misses script. Anything with a space matches anywhere. Put slurs here.",
                    "",
                    "location, area, address, addy, postcode, zip code, suburb, ip, dox, doxx",
                    "where do you, where are you, what's your, whats your, real name, phone number",
                    "",
                    "## Banned users",
                    "",
                    "> Ignored completely. Bots go here. One per line or commas.",
                    "",
                    "nightbot, streamelements, streamlabs, moobot, fossabot, sery_bot",
                    "",
                    "## Ignored",
                    "",
                    "> Not worth a reply. A message of only these is skipped. She can still say them, so emotes go here.",
                    "> One per line or commas.",
                    "",
                    "LUL, KEKW, OMEGALUL, Pog, PogChamp, POGGERS, Sadge, copium, W, L, f, lol, lmao, gg",
                    ""
                });
            }
        }
    }

    // Who is live, with no account and no key. Twitch renders the channel page in the browser, so the HTML itself says
    // nothing useful, but the tags it leaves for link previews do: a live channel's og:title ends "- Live on Twitch" and its
    // og:description ends "| Streaming <game> for <n> viewers.", while an offline one has the channel's bio and a plain
    // "<name> - Twitch". Measured against live and offline channels on 13 Nov; a channel that does not exist has no
    // og:title at all, which reads as offline.
    internal sealed class TwitchLive
    {
        internal string Name = "";
        internal string Game = "";
        internal string Viewers = "";

        // Twitch does not write its meta tags in a fixed order. On one channel it is property then content, on the next
        // it is content then property, in the same page:
        //     <meta property="og:site_name" content="Twitch"/>
        //     <meta content="https://..." name="twitter:image"/>
        // Matching only the first order quietly missed whole channels, which read as "not live" (deme, 14 Nov). Both
        // orders are tried, and the plain <title> is the fallback, since "<name> - Live on Twitch" is always there.
        private static readonly Regex TitleFirst = new Regex("<meta[^>]*?property=\"og:title\"[^>]*?content=\"([^\"]*)\"", RegexOptions.IgnoreCase);
        private static readonly Regex TitleLast = new Regex("<meta[^>]*?content=\"([^\"]*)\"[^>]*?property=\"og:title\"", RegexOptions.IgnoreCase);
        private static readonly Regex DescribesFirst = new Regex("<meta[^>]*?property=\"og:description\"[^>]*?content=\"([^\"]*)\"", RegexOptions.IgnoreCase);
        private static readonly Regex DescribesLast = new Regex("<meta[^>]*?content=\"([^\"]*)\"[^>]*?property=\"og:description\"", RegexOptions.IgnoreCase);
        private static readonly Regex PageTitle = new Regex("<title[^>]*>([^<]*)</title>", RegexOptions.IgnoreCase);
        // "| Streaming hollow knight: silksong for 68 viewers." and also, on a smaller stream, just
        // "| Streaming subnautica 2." with no count at all, so the viewers half has to be optional (LunariaMay, 13 Nov).
        private static readonly Regex Streaming = new Regex(@"\|\s*Streaming\s+(.+?)(?:\s+for\s+([\d,]+)\s+viewers?)?\.\s*$", RegexOptions.IgnoreCase);

        // Null when they are not live, their page cannot be read, or there is no such channel.
        //
        // A live channel nearly always names what it is playing, so coming back with no game at all is more likely a page
        // that arrived half built than a stream that genuinely has none. One more look settles it. The
        // viewer count is not part of the test: Twitch leaves it out of the tag on smaller streams, so its absence means
        // nothing. If the second look is no better she still reports them live without the detail, because being quiet
        // about a stream that is actually running is the worse mistake.
        internal static TwitchLive Check(string who)
        {
            TwitchLive found = Look(who);
            if (found == null || found.Game.Length > 0) return found;
            Thread.Sleep(1500);
            TwitchLive again = Look(who);
            return again != null && again.Game.Length > 0 ? again : found;
        }

        // The same tag written either way round.
        private static string Either(string page, Regex first, Regex last)
        {
            Match one = first.Match(page);
            if (one.Success) return one.Groups[1].Value;
            Match two = last.Match(page);
            return two.Success ? two.Groups[1].Value : null;
        }

        private static TwitchLive Look(string who)
        {
            string name = (who ?? "").Trim().TrimStart('@');
            if (name.Length == 0) return null;
            string page;
            try { page = Hands.Fetch("https://www.twitch.tv/" + Uri.EscapeDataString(name)); }
            catch (Exception) { return null; }
            if (string.IsNullOrEmpty(page)) return null;
            string titled = Either(page, TitleFirst, TitleLast);
            if (titled == null)
            {
                Match plain = PageTitle.Match(page);
                if (plain.Success) titled = Readable(plain.Groups[1].Value);
            }
            if (string.IsNullOrEmpty(titled)) return null;
            bool live = titled.IndexOf("- Live on Twitch", StringComparison.OrdinalIgnoreCase) >= 0;
            string about = Either(page, DescribesFirst, DescribesLast);
            Match what = about != null ? Streaming.Match(Readable(about)) : Match.Empty;
            if (!live && !what.Success) return null;
            return new TwitchLive
            {
                Name = titled.Replace(" - Live on Twitch", "").Trim(),
                Game = what.Success ? what.Groups[1].Value.Trim() : "",
                Viewers = what.Success ? what.Groups[2].Value : ""
            };
        }

        // The preview tags are HTML, so the few entities Twitch actually puts in a stream title are turned back into text.
        private static string Readable(string text)
        {
            string plain = text ?? "";
            plain = plain.Replace("&#39;", "'").Replace("&quot;", "\"").Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&nbsp;", " ");
            return Regex.Replace(plain, @"&#(\d+);", delegate(Match found)
            {
                int code;
                return int.TryParse(found.Groups[1].Value, out code) && code > 31 && code < 0x10000 ? ((char)code).ToString() : "";
            });
        }

        internal string Line
        {
            get
            {
                // The count is still read, because having one is a sign the page came back whole, but it is not worth
                // saying out loud, so her line is just who and what.
                return Game.Length > 0 ? Name + " is live with " + Game : Name + " is live";
            }
        }
    }

    // An anonymous read-only reader for one channel's chat. Twitch lets any client join without an account as justinfan
    // followed by digits, which is how a channel can be watched with no login and no key at all.
    internal sealed class TwitchChat : IDisposable
    {
        private const string Host = "irc.chat.twitch.tv";
        private const string WebHost = "irc-ws.chat.twitch.tv";
        private const int Port = 6697;
        private readonly string channel;
        private readonly string login;
        private readonly string token;
        private readonly Action<string, string> heard;
        private readonly Action<string> noted;
        private readonly object gate = new object();
        private Thread reader;
        private TcpClient client;
        private SslStream stream;
        private volatile bool stopping;
        private volatile bool joined;

        internal TwitchChat(string channel, string login, string token, Action<string, string> heard, Action<string> noted)
        {
            this.channel = (channel ?? "").Trim().ToLowerInvariant();
            this.login = (login ?? "").Trim().ToLowerInvariant();
            this.token = (token ?? "").Trim();
            this.heard = heard;
            this.noted = noted;
        }

        internal bool Joined { get { return joined; } }
        internal string Channel { get { return channel; } }

        internal void Start()
        {
            if (channel.Length == 0) { Note("No channel in " + TwitchRules.FileName + " yet."); return; }
            lock (gate)
            {
                if (reader != null) return;
                stopping = false;
                reader = new Thread(Run);
                reader.IsBackground = true;
                reader.Name = "Twitch chat";
                reader.Start();
            }
        }

        internal void Stop()
        {
            Thread ending;
            lock (gate)
            {
                stopping = true;
                ending = reader;
                reader = null;
                Close();
            }
            if (ending != null && !ending.Join(2000)) Note("Twitch chat did not close cleanly.");
            joined = false;
        }

        public void Dispose() { Stop(); }

        private void Close()
        {
            try { if (stream != null) stream.Dispose(); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            try { if (client != null) client.Close(); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            stream = null;
            client = null;
        }

        private void Note(string line) { Action<string> told = noted; if (told != null) try { told(line); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); } }

        // Reconnects with a widening wait, because a dropped socket on a long stream is ordinary rather than a failure.
        // Twitch serves the same chat protocol two ways: IRC over TLS on 6697 and a WebSocket on 443. 6697 goes first because
        // it is the one measured working here, joining a real channel anonymously and reading real messages. 443 is only a
        // fallback, for a network that blocks 6697 the way plenty of work and school networks do. Once one of them has
        // joined, the other is not tried again for the rest of the session.
        private void Run()
        {
            int failures = 0;
            bool ircFirst = true;
            while (!stopping)
            {
                try
                {
                    if (ircFirst)
                    {
                        try { ListenIrc(); }
                        catch (Exception error)
                        {
                            if (stopping || joined) throw;
                            ircFirst = false;
                            Note("Twitch chat on 6697 wouldn't open (" + error.GetType().Name + "), trying 443.");
                            ListenWeb();
                        }
                    }
                    else ListenWeb();
                    failures = 0;
                }
                catch (Exception error)
                {
                    if (stopping) break;
                    failures++;
                    Note("Twitch chat dropped (" + error.GetType().Name + "): " + error.Message);
                }
                finally { Close(); joined = false; }
                for (int waited = 0; waited < Math.Min(60, 2 * failures) * 10 && !stopping; waited++) Thread.Sleep(100);
                if (failures == 0 && !stopping) Thread.Sleep(1000);
            }
        }

        private bool Anonymous { get { return token.Length == 0 || login.Length == 0; } }

        // The three lines that join a channel. Anonymous means the nick justinfan followed by digits, which Twitch accepts
        // from anyone with no account and no key, read only.
        private void Hello(Action<string> send)
        {
            send("PASS " + (Anonymous ? "SCHMOOPIIE" : "oauth:" + token));
            send("NICK " + (Anonymous ? "justinfan" + new Random().Next(10000, 99999).ToString(CultureInfo.InvariantCulture) : login));
            send("JOIN #" + channel);
        }

        // One protocol line, whichever way it arrived.
        private void Handle(string line, Action<string> send)
        {
            if (line == null || line.Length == 0) return;
            if (line.StartsWith("PING", StringComparison.Ordinal)) { send("PONG :tmi.twitch.tv"); return; }
            if (line.IndexOf(" 366 ", StringComparison.Ordinal) > 0 || line.IndexOf(" JOIN #", StringComparison.Ordinal) > 0)
            {
                if (!joined) { joined = true; Note("Reading #" + channel + (Anonymous ? " anonymously." : " as " + login + ".")); }
                return;
            }
            if (line.IndexOf(" NOTICE ", StringComparison.Ordinal) > 0 && line.IndexOf("Login authentication failed", StringComparison.Ordinal) > 0) throw new IOException("Twitch refused the login in " + TwitchRules.FileName + ".");
            Said(line);
        }

        private void ListenIrc()
        {
            client = new TcpClient();
            client.Connect(Host, Port);
            client.ReceiveTimeout = 400000;
            stream = new SslStream(client.GetStream(), false);
            stream.AuthenticateAsClient(Host, null, SslProtocols.Tls12, false);
            StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\r\n" };
            StreamReader lines = new StreamReader(stream, new UTF8Encoding(false));
            Action<string> send = delegate(string out_) { writer.WriteLine(out_); };
            Hello(send);
            while (!stopping)
            {
                string line = lines.ReadLine();
                if (line == null) throw new IOException("Twitch closed the connection.");
                Handle(line, send);
            }
        }

        // The same protocol over a WebSocket on 443. Frames can carry several lines or half of one, so they are joined up and
        // split on the newline rather than taken a frame at a time.
        private void ListenWeb()
        {
            using (System.Net.WebSockets.ClientWebSocket socket = new System.Net.WebSockets.ClientWebSocket())
            {
                if (!socket.ConnectAsync(new Uri("wss://" + WebHost + ":443"), System.Threading.CancellationToken.None).Wait(20000)) throw new IOException("Twitch's chat socket timed out.");
                if (socket.State != System.Net.WebSockets.WebSocketState.Open) throw new IOException("Twitch's chat socket did not open.");
                Action<string> send = delegate(string out_)
                {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(out_ + "\r\n");
                    socket.SendAsync(new ArraySegment<byte>(bytes), System.Net.WebSockets.WebSocketMessageType.Text, true, System.Threading.CancellationToken.None).Wait(20000);
                };
                Hello(send);
                byte[] buffer = new byte[16384];
                StringBuilder held = new StringBuilder();
                while (!stopping)
                {
                    System.Net.WebSockets.WebSocketReceiveResult got = socket.ReceiveAsync(new ArraySegment<byte>(buffer), System.Threading.CancellationToken.None).Result;
                    if (got.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) throw new IOException("Twitch closed the chat socket.");
                    held.Append(new UTF8Encoding(false).GetString(buffer, 0, got.Count));
                    string all = held.ToString();
                    int cut = all.LastIndexOf('\n');
                    if (cut < 0) continue;
                    held.Length = 0;
                    held.Append(all.Substring(cut + 1));
                    foreach (string line in all.Substring(0, cut).Split('\n')) Handle(line.TrimEnd('\r'), send);
                }
            }
        }

        // ":name!name@name.tmi.twitch.tv PRIVMSG #channel :what they said"
        private static readonly Regex Message = new Regex(@"^(?:@[^ ]+ )?:([^!]+)![^ ]+ PRIVMSG #[^ ]+ :(.*)$", RegexOptions.Compiled);

        private void Said(string line)
        {
            Match spoke = Message.Match(line ?? "");
            if (!spoke.Success) return;
            Action<string, string> told = heard;
            if (told == null) return;
            try { told(spoke.Groups[1].Value, spoke.Groups[2].Value.TrimEnd('\r', '\n')); }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }
    }
}
