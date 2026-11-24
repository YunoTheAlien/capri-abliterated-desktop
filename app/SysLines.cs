using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // The little grey labels after "> sys: ", from a file with her text as the fallback, so a missing or broken file
    // falls back to stock, never blank. Same shape as twitch.md: re-read on change, UTF-8 with no BOM.
    internal static class SysLines
    {
        internal const string FileName = "sys.md";
        // The switch's key, the last thing in the file. 1 is on, anything else is off.
        internal const string SenpaiKey = "confirm";
        // What the key used to be called. Still read, so a file written before this keeps its answer rather
        // than being treated as unanswered and asking again.

        // Programs she won't look at with Watch my screen on, comma separated, matched against the program's name and
        // window title.
        internal const string ExcludedKey = "never watch";

        // Her calendar, by opening the one they use. Nothing signs in anywhere: she opens the app and reads what
        // is on screen, the same as anything else she looks at. Blank means she works out which one from what
        // they keep in front; a name here settles it.
        internal const string CalendarKey = "my calendar";

        // Her quiet left sys.md for the Focus / Quiet settings window and her settings; the names stay so a sys.md from before
        // is read once and tidied.
        internal const string QuietHeading = "## Her quiet";
        internal const string QuietCallsKey = "quiet on calls";
        internal const string QuietVideoKey = "quiet on videos";
        internal const string QuietMinKey = "quiet min hours";
        internal const string QuietMaxKey = "quiet max hours";
        internal const string CallAppsKey = "call apps";
        internal static readonly string[] QuietKeys = { QuietMinKey, QuietMaxKey, QuietCallsKey, QuietVideoKey, CallAppsKey };
        // Titles a window carries only while a call is on, not while the app merely sits open: Zoom's meeting
        // window, a Google Meet tab, a Slack huddle, a Webex meeting, Teams' meeting and call windows.
        internal const string CallAppsStock = "zoom meeting, zoom webinar, meet -, huddle, webex meeting, meeting in, call with, meeting compact view";

        // A whole number of hours as written, or stock when it isn't one.
        internal static int WholeHours(string said, int stock)
        {
            double hours;
            if (!double.TryParse((said ?? "").Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out hours)) return stock;
            return (int)Math.Round(hours);
        }

        // A sys.md from before her quiet left it: each quiet line's value by name, the lines and their notes taken out,
        // and the heading with its notes when nothing of theirs is left under it. Null when it had none.
        internal static Dictionary<string, string> TakeQuiet(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                string text = CapriDesktop.Notes.ReadAll(path);
                string newline = text.Contains("\r\n") ? "\r\n" : "\n";
                List<string> rows = new List<string>(text.Replace("\r\n", "\n").Split('\n'));
                Dictionary<string, string> said = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                bool changed = false;
                for (int at = 0; at < rows.Count; at++)
                {
                    string one = rows[at].Trim();
                    bool note = one.StartsWith(">", StringComparison.Ordinal);
                    string plain = note ? one.TrimStart('>', '!').Trim() : one;
                    foreach (string key in QuietKeys)
                    {
                        Match line = Regex.Match(plain, @"^" + Regex.Escape(key) + @"[ \t]*:[ \t]*(.*)$", RegexOptions.IgnoreCase);
                        if (!line.Success) continue;
                        if (!note && !said.ContainsKey(key)) said[key] = line.Groups[1].Value.Trim();
                        rows.RemoveAt(at);
                        at--;
                        changed = true;
                        break;
                    }
                }
                if (!changed) return null;
                int head = rows.FindIndex(row => row.Trim() == QuietHeading);
                if (head >= 0)
                {
                    int end = head + 1;
                    bool theirs = false;
                    for (; end < rows.Count && !rows[end].TrimStart().StartsWith("#", StringComparison.Ordinal); end++)
                        if (rows[end].Trim().Length > 0 && !rows[end].TrimStart().StartsWith(">", StringComparison.Ordinal)) theirs = true;
                    if (!theirs) rows.RemoveRange(head, end - head);
                }
                Notes.Put(path, string.Join(newline, rows.ToArray()));
                lock (writing) { read = DateTime.MinValue; wasLong = -1; }
                return said.Count == 0 ? null : said;
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); return null; }
        }

        internal static bool On(string said, bool blank) { return Yes(said, blank); }

        // Load untested mods was a line here before it was the switch in Mainframe > Mods, which keeps it in her settings. The
        // names stay so a sys.md from before can be read once and tidied.
        internal const string UntestedKey = "load untested mods";
        internal const string UntestedHeading = "## Untested mods";

        // A sys.md from before the switch: its answer handed back and the line taken out, with the section's heading and notes
        // when nothing of theirs is under it. Every other line stays. Null when it had none.
        internal static bool? TakeUntested(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                string text = CapriDesktop.Notes.ReadAll(path);
                string newline = text.Contains("\r\n") ? "\r\n" : "\n";
                List<string> rows = new List<string>(text.Replace("\r\n", "\n").Split('\n'));
                bool? said = null;
                for (int at = 0; at < rows.Count; at++)
                {
                    Match key = Regex.Match(rows[at].Trim(), @"^" + Regex.Escape(UntestedKey) + @"[ \t]*:[ \t]*(.*)$", RegexOptions.IgnoreCase);
                    if (!key.Success) continue;
                    if (said == null) said = Yes(key.Groups[1].Value.Trim(), false);
                    rows.RemoveAt(at);
                    at--;
                }
                if (said == null) return null;
                int head = rows.FindIndex(row => row.Trim() == UntestedHeading);
                if (head >= 0)
                {
                    int end = head + 1;
                    bool theirs = false;
                    for (; end < rows.Count && !rows[end].TrimStart().StartsWith("#", StringComparison.Ordinal); end++)
                        if (rows[end].Trim().Length > 0 && !rows[end].TrimStart().StartsWith(">", StringComparison.Ordinal)) theirs = true;
                    if (!theirs) rows.RemoveRange(head, end - head);
                }
                Notes.Put(path, string.Join(newline, rows.ToArray()));
                lock (writing) { read = DateTime.MinValue; wasLong = -1; }
                return said;
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); return null; }
        }

        // The screensaver's two colours left sys.md for its fold in Custom colours. Each sys.md still holding them hands what
        // they say over once and loses the lines and their notes; a blank one hands nothing.
        internal static Dictionary<string, string> TakeSaverColours(string path)
        {
            Dictionary<string, string> said = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return said;
                string text = CapriDesktop.Notes.ReadAll(path);
                string newline = text.Contains("\r\n") ? "\r\n" : "\n";
                List<string> rows = new List<string>(text.Replace("\r\n", "\n").Split('\n'));
                bool took = false;
                for (int at = 0; at < rows.Count; at++)
                {
                    string row = rows[at].Trim();
                    bool note = row.StartsWith(">! colour:", StringComparison.Ordinal) || row.StartsWith(">! background:", StringComparison.Ordinal);
                    Match key = Regex.Match(row, @"^(" + Regex.Escape(SaverColourKey) + "|" + Regex.Escape(SaverGroundKey) + @")[ \t]*:[ \t]*(.*)$", RegexOptions.IgnoreCase);
                    if (!note && !key.Success) continue;
                    if (key.Success && key.Groups[2].Value.Trim().Length > 0 && !said.ContainsKey(key.Groups[1].Value)) said[key.Groups[1].Value] = key.Groups[2].Value.Trim();
                    rows.RemoveAt(at);
                    at--;
                    took = true;
                }
                if (!took) return said;
                Notes.Put(path, string.Join(newline, rows.ToArray()));
                lock (writing) { read = DateTime.MinValue; wasLong = -1; }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            return said;
        }

        // Every sys.md in her settings folder, the one in use first: the other side's, and a loose one from before the split.
        internal static string[] Copies()
        {
            List<string> found = new List<string>();
            try
            {
                if (string.IsNullOrEmpty(Kept)) return found.ToArray();
                string here = Path.GetFullPath(Kept);
                found.Add(here);
                string side = Path.GetDirectoryName(here), settings = side == null ? null : Path.GetDirectoryName(side);
                if (settings == null || !string.Equals(Path.GetFileName(settings), Personalities.SettingsName, StringComparison.OrdinalIgnoreCase)) return found.ToArray();
                foreach (string other in new[] { Path.Combine(settings, Personalities.PlainName, FileName), Path.Combine(settings, Personalities.SenpaiName, FileName), Path.Combine(settings, FileName) })
                    if (!string.Equals(Path.GetFullPath(other), here, StringComparison.OrdinalIgnoreCase)) found.Add(other);
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            return found.ToArray();
        }

        // Comma separated window titles, each trimmed and in lower case; one letter alone isn't one.
        internal static string[] Titles(string said)
        {
            List<string> apps = new List<string>();
            foreach (string one in (said ?? "").Split(',')) if (one.Trim().Length > 1) apps.Add(one.Trim().ToLowerInvariant());
            return apps.ToArray();
        }

        private static bool Yes(string said, bool blank)
        {
            string plain = (said ?? "").Trim();
            if (plain.Length == 0) return blank;
            return plain == "1" || plain.Equals("yes", StringComparison.OrdinalIgnoreCase) || plain.Equals("on", StringComparison.OrdinalIgnoreCase) || plain.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        // Whether her hello and goodbye come from whoever she is, or from the lines below. 1 is her personality.
        // Her error line is never part of this: that one has to read the same whoever she is, or a report of it
        // means nothing.
        internal const string OwnLinesKey = "personality lines";
        internal static string Kept;

        private static readonly object writing = new object();
        private static readonly Dictionary<string, string> theirs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static DateTime read = DateTime.MinValue;
        private static long wasLong = -1;

        // Her labels in Said.Labels order: the left name is code's and never changes, the right is shown. Labels that
        // only mean anything with the switch on aren't written on the other side, and are taken out if there.
        internal static bool SenpaiLabel(string named)
        {
            return !string.IsNullOrEmpty(named) && (named == Extended.Get("click.heard") || Legacy.WasLabel(named));
        }

        internal static readonly string[,] Stock =
        {
            { "startup", "startup" },
            { "pets you", "pets you" },
            { "high five", "high five" },
            { "picks you up", "picks you up" },
            { "clang", "clang" },
            { "throws you", "throws you" },
            { "reaches for you", "reaches for you" },
            { "settings", "settings" },
            { "voice check", "voice check" },
            { "get to know me", "get to know me" },
            { "updated", "updated" },
            { "brain online", "brain online" },
            { "her clones", "her clones" },
            { "holds Esc", "holds Esc" },
            { "holiday", "holiday" },
            { "distracted", "distracted" },
            { "error", "error" }
        };

        // Lines she actually says, as opposed to the labels above; several can be offered split on "|", and she takes
        // one at random, so even her hellos can be rewritten.
        internal static readonly string[,] Spoken =
        {
            { "hello morning", "Good morning, choom." },
            { "hello late", "Isn't it late, choom?" },
            { "hello day", "Miss me, choom?" },
            { "goodbye", "Later, choom. | Don't be long. | Going dark. | See you soon." },
            { "broken", "There's something seriously broken with my code." }
        };

        // The two prefixes her bubble puts in front of a line. Kept as their own keys so "> sys: " can become "Capri: "
        // or anything else, without touching the marker the code uses to know a line is a system one.
        internal const string SysMark = "> sys: ";
        internal const string YouMark = "> you: ";

        internal static WrittenNote Open() { Write(false); return new WrittenNote(Kept, Text, writing); }

        // Whether the switch is on, straight out of the file. Null when the file does not mention it yet, which is
        // how an older sys.md is told apart from one deliberately set to off.
        internal static bool? Senpai
        {
            get
            {
                Fresh();
                lock (writing)
                {
                    string said;
                    // The old name too, so a file written before the rename keeps the answer it has
                    // rather than reading as unanswered and asking again.
                    if (!theirs.TryGetValue(SenpaiKey, out said))
                    {
                        said = null;
                        foreach (KeyValuePair<string, string> one in theirs) if (Legacy.WasSysKey(one.Key)) { said = one.Value; break; }
                        if (said == null) return null;
                    }
                    said = (said ?? "").Trim();
                    if (said.Length == 0) return null;
                    return said == "1" || said.Equals("yes", StringComparison.OrdinalIgnoreCase) || said.Equals("on", StringComparison.OrdinalIgnoreCase) || said.Equals("true", StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        // Puts the line in a file that has not got one, carrying over whatever they were already running so nobody
        // loses the mode they were using by us moving the switch.
        internal static void SetSenpai(bool on)
        {
            if (string.IsNullOrEmpty(Kept)) return;
            try
            {
                if (!File.Exists(Kept)) { Write(false); }
                string had = CapriDesktop.Notes.ReadAll(Kept);
                // Yes unlocks it. It ships No, so a fresh install has answered nothing.
                string want = Line(SenpaiKey, on ? "Yes" : "No");
                // To the end of the line and no further: ".*" took the carriage return with it.
                Match found = Regex.Match(had, @"(?mi)^[ \t]*" + Regex.Escape(SenpaiKey) + @"[ \t]*:[^\r\n]*");
                if (found.Success) had = had.Substring(0, found.Index) + want + had.Substring(found.Index + found.Length);
                else
                {
                    if (had.Length > 0 && !had.EndsWith("\n")) had += Environment.NewLine;
                    // The terms, unless the file has them already (a second copy was added under the first), then the
                    // question.
                    bool terms = Regex.IsMatch(had, @"(?mi)^[ \t]*" + Regex.Escape(SenpaiHeading) + @"[ \t]*\r?$");
                    had += Environment.NewLine + (terms ? "" : string.Join(Environment.NewLine, DealLines()) + Environment.NewLine + Environment.NewLine) + want + Environment.NewLine;
                }
                Notes.Put(Kept, had);
                lock (writing) { read = DateTime.MinValue; wasLong = -1; }
                SenpaiLast();
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // Their list, split and tidied. Empty when they have not set any, which is the default.
        internal static string[] NeverWatch()
        {
            string said = Of(ExcludedKey);
            if (string.IsNullOrWhiteSpace(said)) return new string[0];
            List<string> found = new List<string>();
            foreach (string one in said.Split(',', ';'))
            {
                string name = (one ?? "").Trim();
                // A bare ".exe" or a one letter entry would match most of the desktop, so they are ignored.
                if (name.Length >= 2) found.Add(name);
            }
            return found.ToArray();
        }

        // Whether her own hello and goodbye win over the ones in this file. On unless they say otherwise.
        internal static bool OwnComings
        {
            get
            {
                string said = (Of(OwnLinesKey) ?? "").Trim();
                return said.Length == 0 || said == "1" || said.Equals("yes", StringComparison.OrdinalIgnoreCase)
                    || said.Equals("true", StringComparison.OrdinalIgnoreCase) || said.Equals("on", StringComparison.OrdinalIgnoreCase);
            }
        }

        // Only what they wrote, with no falling back. Favourites asks this before it counts anything, and
        // Calendar below falls back to Favourites, so the two would call each other round for ever.
        internal static string CalendarNamed()
        {
            string said = Of(CalendarKey);
            return string.IsNullOrWhiteSpace(said) ? null : said.Trim();
        }

        // Whether this is one of them. Program and title both, because some of these are known by one and not the
        // other: a browser is brave.exe, an image viewer is whatever file it happens to have open.
        internal static bool Skipped(string program, string title)
        {
            foreach (string one in NeverWatch())
            {
                if (!string.IsNullOrEmpty(program) && program.IndexOf(one, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (!string.IsNullOrEmpty(title) && title.IndexOf(one, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        // What to actually draw in front of a line. The stock prefix goes in, theirs comes out.
        internal static string Prefix(string mark)
        {
            string key = mark == SysMark ? "sys prompt" : mark == YouMark ? "you prompt" : null;
            if (key == null) return mark;
            string said = Of(key);
            if (said == null) return mark;
            // A prefix that runs straight into the words is a typo every time, so one space is kept for them.
            return said.EndsWith(" ") ? said : said + " ";
        }

        // One of the alternatives under a name, or null when they have not set any. Blank pieces are dropped so a stray
        // bar in the middle is not a silent line.
        internal static string Any(string name, Random luck)
        {
            string said = Of(name) ?? Stocked(name);
            if (said == null) return null;
            if (said.IndexOf('|') < 0) return said;
            List<string> parts = new List<string>();
            foreach (string piece in said.Split('|'))
            {
                string one = piece.Trim();
                if (one.Length > 0) parts.Add(one);
            }
            if (parts.Count == 0) return null;
            return parts[(luck ?? new Random()).Next(parts.Count)];
        }

        // The stock text for a name (Lachy), so a caller can fall back without holding its own copy.
        internal static string Stocked(string name)
        {
            for (int line = 0; line < Spoken.GetLength(0); line++)
                if (string.Equals(Spoken[line, 0], name, StringComparison.OrdinalIgnoreCase)) return Spoken[line, 1];
            for (int line = 0; line < Stock.GetLength(0); line++)
                if (string.Equals(Stock[line, 0], name, StringComparison.OrdinalIgnoreCase)) return Stock[line, 1];
            return null;
        }

        // Her screensaver, named here with the rest of what people type into this file.
        internal const string SaverKey = "screensaver";
        internal const string SaverStyleKey = "screensaver style";
        internal const string SaverColourKey = "screensaver colour";
        internal const string SaverGroundKey = "screensaver background";
        internal const string SaverSizeKey = "screensaver size";
        internal const string SaverSpeedKey = "screensaver speed";
        internal const string SaverThroughKey = "screensaver see through";
        internal const string SaverScreensKey = "screensaver screens";
        internal const string SaverFlashKey = "screensaver flashing";
        internal const string SaverSoundKey = "screensaver sound";
        internal const string SaverVolumeKey = "screensaver volume";

        // How loud the screensaver's own sound is, as a share of her sound volume. Low by default: an alarm
        // looping at the volume her chimes are set to is far louder than anybody wants from an idle screen.
        internal static int SaverVolume
        {
            get
            {
                int much;
                if (!int.TryParse((Of(SaverVolumeKey) ?? "").Trim(), System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out much)) return 20;
                return Math.Max(0, Math.Min(100, much));
            }
        }

        // The .wav they named, found by whole path or name beside her files; null when none. A sound sys.md names
        // outside her folder is copied into sounds and the line pointed at the copy, so backups carry it.
        internal static void KeepSaverSound()
        {
            try
            {
                string said = (Of(SaverSoundKey) ?? "").Trim().Trim('"');
                if (!Library.Loose(said)) return;
                string kept = Library.Keep(said, Library.Sounds);
                if (string.Equals(kept, said, StringComparison.OrdinalIgnoreCase)) return;
                string relative = Library.Sounds + "/" + Path.GetFileName(kept);
                string text = CapriDesktop.Notes.ReadAll(Kept);
                string made = Regex.Replace(text, @"(?mi)^([ \t]*" + Regex.Escape(SaverSoundKey) + @"[ \t]*:[ \t]*)[^\r\n]*", "${1}" + relative.Replace("$", "$$"));
                if (made != text) Notes.Put(Kept, made);
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // One of her settings rewritten in place, as the Mainframe sets it. A line missing from an older file is left alone.
        internal static void SetValue(string name, string value)
        {
            try
            {
                if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept)) return;
                string text = CapriDesktop.Notes.ReadAll(Kept);
                string made = Regex.Replace(text, @"(?mi)^([ \t]*" + Regex.Escape(name) + @"[ \t]*:[ \t]*)[^\r\n]*", "${1}" + (value ?? "").Replace("$", "$$"));
                if (made == text) return;
                Notes.Put(Kept, made);
                lock (writing) { read = DateTime.MinValue; wasLong = -1; }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        internal static string SaverSound
        {
            get
            {
                string said = (Of(SaverSoundKey) ?? "").Trim().Trim('"');
                if (said.Length == 0 || string.IsNullOrEmpty(Kept)) return null;
                try
                {
                    if (Path.IsPathRooted(said)) return File.Exists(said) ? said : null;
                    string beside = Path.GetDirectoryName(Kept);
                    foreach (string folder in new[] { beside, Path.GetDirectoryName(beside) })
                    {
                        if (string.IsNullOrEmpty(folder)) continue;
                        string full = Path.GetFullPath(Path.Combine(folder, said));
                        if (File.Exists(full)) return full;
                    }
                }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                return null;
            }
        }
        internal const string SaverAfterKey = "screensaver after";

        private static readonly Dictionary<string, Color> mixed = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);

        // Every line in the section and what it says when nobody has touched it. One list, so the file she
        // writes, the top up that repairs an older file, and the check for a line that is missing all read
        // the same thing and cannot drift apart.
        internal static readonly string[,] SaverStock =
        {
            { SaverSizeKey, "normal" }, { SaverSpeedKey, "normal" }, { SaverThroughKey, "0" },
            { SaverScreensKey, "all" }, { SaverFlashKey, "on" }, { SaverSoundKey, "" },
            { SaverVolumeKey, "20" }
        };

        internal const string SaverHeading = "## Her screensaver";

        internal static string[] SaverLines() { return SaverLines(null); }

        // The section as it goes into the file. Anything they had already set is kept: a line added later
        // must never cost somebody the settings they wrote.
        internal static string[] SaverLines(Dictionary<string, string> had)
        {
            List<string> made = new List<string>(SaverNotes());
            for (int one = 0; one < SaverStock.GetLength(0); one++)
            {
                string said;
                if (had == null || !had.TryGetValue(SaverStock[one, 0], out said) || said == null) said = SaverStock[one, 1];
                made.Add(Line(SaverStock[one, 0], said.Trim()));
            }
            return made.ToArray();
        }

        private static string[] SaverNotes()
        {
            return new[]
            {
                SaverHeading,
                "",
                ">! Shows while the PC is idle, when Toggles > Screensaver is on. Space or Esc ends it. Never over games or full screen.",
                ">! Its colours are in Mainframe > Settings > GENERAL SETTINGS > Custom colours, under ADD-ONS.",
                ">! size:        small, normal, large, or a number. 1 is normal.",
                ">! speed:       slow, normal, fast, or a number. 1 is normal.",
                ">! see through: 0 to 100. 0 is solid.",
                ">! screens:     all, or main.",
                ">! flashing:    on, gentle or off.",
                ">! sound:       your own .wav, for her menu and red alert only. Blank is silent.",
                ">! volume:      0 to 100.",
                ""
            };
        }

        // Whether they turned it on: the Screensaver switch in her Toggles, kept in her settings. Off out of the box,
        // since it covers the screen.
        internal static Func<bool> SaverOn = delegate { return false; };

        // Its style and how long the PC waits first, from the Screensaver group in her Toggles; sys.md held them before.
        internal static Func<string> SaverStyleSaved = delegate { return null; };
        internal static Func<double> SaverAfterSaved = delegate { return 0; };

        // A sys.md from before hands its style and wait over once, by key, and loses those lines and their notes.
        internal static Dictionary<string, string> TakeSaverPicks(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                string text = CapriDesktop.Notes.ReadAll(path);
                string newline = text.Contains("\r\n") ? "\r\n" : "\n";
                List<string> rows = new List<string>(text.Replace("\r\n", "\n").Split('\n'));
                Dictionary<string, string> said = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                bool changed = false;
                for (int at = 0; at < rows.Count; at++)
                {
                    string one = rows[at].Trim();
                    bool note = one.StartsWith(">", StringComparison.Ordinal);
                    string plain = note ? one.TrimStart('>', '!').Trim() : one;
                    foreach (string key in new[] { SaverStyleKey, SaverAfterKey, "style", "after" })
                    {
                        Match line = Regex.Match(plain, @"^" + Regex.Escape(key) + @"[ \t]*:[ \t]*(.*)$", RegexOptions.IgnoreCase);
                        if (!line.Success || (!note && key.Length < 7)) continue;
                        if (!note && !said.ContainsKey(key)) said[key] = line.Groups[1].Value.Trim();
                        rows.RemoveAt(at);
                        at--;
                        changed = true;
                        break;
                    }
                }
                if (!changed) return null;
                Notes.Put(path, string.Join(newline, rows.ToArray()));
                lock (writing) { read = DateTime.MinValue; wasLong = -1; }
                return said;
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); return null; }
        }
        internal static bool Saver { get { return SaverOn(); } }

        // The on and off line left sys.md for the switch in Toggles. A sys.md from before hands its answer over once and loses
        // the line; the rest of the section stays. Null when it had none.
        internal static bool? TakeSaver(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                string text = CapriDesktop.Notes.ReadAll(path);
                string newline = text.Contains("\r\n") ? "\r\n" : "\n";
                List<string> rows = new List<string>(text.Replace("\r\n", "\n").Split('\n'));
                bool? said = null;
                for (int at = 0; at < rows.Count; at++)
                {
                    Match key = Regex.Match(rows[at].Trim(), @"^" + Regex.Escape(SaverKey) + @"[ \t]*:[ \t]*(.*)$", RegexOptions.IgnoreCase);
                    if (!key.Success) continue;
                    if (said == null) said = Yes(key.Groups[1].Value.Trim(), false);
                    rows.RemoveAt(at);
                    at--;
                }
                if (said == null) return null;
                for (int at = 0; at < rows.Count; at++)
                    if (rows[at].Trim().StartsWith(">! Shows while the PC is idle. 1 is on, 0 is off.", StringComparison.Ordinal))
                        rows[at] = ">! Shows while the PC is idle, when Toggles > Screensaver is on. Space or Esc ends it. Never over games or full screen.";
                Notes.Put(path, string.Join(newline, rows.ToArray()));
                lock (writing) { read = DateTime.MinValue; wasLong = -1; }
                return said;
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); return null; }
        }

        // How long the PC has to be left alone first. Held between six seconds and a day: a typo of 0 would put it
        // over the screen the moment they stopped typing.
        internal static double SaverAfter
        {
            get
            {
                double minutes = SaverAfterSaved();
                if (minutes <= 0) minutes = 10;
                return Math.Max(0.1, Math.Min(1440, minutes));
            }
        }

        // A colour they wrote, or hers: a name or hex with or without #, three, six or eight digits (eight sets alpha).
        // A wrong one is ignored, so a typo never takes her bubble away.
        internal static Color Colour(string name, Color hers)
        {
            string said = Of(name);
            if (string.IsNullOrEmpty(said)) return hers;
            lock (writing)
            {
                Color found;
                if (mixed.TryGetValue(said, out found)) return found;
            }
            Color made = Mixed(said) ?? hers;
            lock (writing) mixed[said] = made;
            return made;
        }

        internal static Color? Mixed(string said)
        {
            string one = (said ?? "").Trim().TrimEnd(';');
            if (one.Length == 0) return null;
            string digits = one.StartsWith("#", StringComparison.Ordinal) ? one.Substring(1) : one;
            if (Regex.IsMatch(digits, "^[0-9a-fA-F]+$") && (digits.Length == 3 || digits.Length == 6 || digits.Length == 8))
            {
                if (digits.Length == 3) digits = "" + digits[0] + digits[0] + digits[1] + digits[1] + digits[2] + digits[2];
                try
                {
                    long value = Convert.ToInt64(digits, 16);
                    return digits.Length == 8
                        ? Color.FromArgb((int)((value >> 24) & 255), (int)((value >> 16) & 255), (int)((value >> 8) & 255), (int)(value & 255))
                        : Color.FromArgb((int)((value >> 16) & 255), (int)((value >> 8) & 255), (int)(value & 255));
                }
                catch (Exception) { return null; }
            }
            try
            {
                Color named = System.Drawing.ColorTranslator.FromHtml(one);
                return named.A == 0 ? (Color?)null : named;
            }
            catch (Exception) { return null; }
        }

        // Their text for a label, or null so an unchanged label still goes through her translator. Every "|"
        // alternative under "What she says herself", so her translator knows them ahead; their wording is never
        // rewritten.
        internal static List<string> SpokenLines()
        {
            List<string> found = new List<string>();
            for (int line = 0; line < Spoken.GetLength(0); line++)
                foreach (string said in new[] { Of(Spoken[line, 0]), Spoken[line, 1] })
                    if (!string.IsNullOrEmpty(said))
                        foreach (string piece in said.Split('|'))
                            if (piece.Trim().Length > 0 && !found.Contains(piece.Trim())) found.Add(piece.Trim());
            return found;
        }

        // Her prompt lines in another language: the ">!=" line above "sys prompt" and "you prompt" holds "> sys:" or
        // "> you:" in its words, and must never be read as a setting, a switched-off line or a note, while the setting under
        // it keeps its value with the trailing space.
        internal static void PromptCheck(List<string> checks, string folder)
        {
            string was = Kept;
            string path = Path.Combine(folder, "sys-prompts-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".md");
            try
            {
                Kept = path;
                Write(true);
                Dictionary<string, string> lines = new Dictionary<string, string>
                {
                    { Line("sys prompt", SysMark).Trim(), "Ustawienia: sys prompt: > sys: (przed jej wierszami)" },
                    { Line("you prompt", YouMark).Trim(), "Ustawienia: you prompt: > you: (przed twoimi)" },
                };
                CapriDesktop.Notes.Explained(path, lines);
                lock (writing) { read = DateTime.MinValue; wasLong = -1; }
                CapriDesktop.Notes.Remark(path);
                CapriDesktop.Notes.Refresh(path, Text);
                TopUp();
                string after = CapriDesktop.Notes.ReadAll(path);
                lock (writing) { read = DateTime.MinValue; wasLong = -1; }
                bool explained = after.Contains(CapriDesktop.Notes.SettingMark + " Ustawienia: sys prompt: > sys:") && after.Contains(CapriDesktop.Notes.SettingMark + " Ustawienia: you prompt: > you:");
                bool values = Of("sys prompt") == SysMark.Trim() || Regex.IsMatch(after, @"(?m)^sys prompt:[ \t]+> sys: ?\r?$");
                bool kept = Regex.IsMatch(after, @"(?m)^sys prompt:[ \t]+> sys: \r?$") && Regex.IsMatch(after, @"(?m)^you prompt:[ \t]+> you: \r?$");
                bool unread = !theirs.ContainsKey("Ustawienia") && !Regex.IsMatch(after, @"(?m)^>![ ]Ustawienia");
                if (!explained || !values || !kept || !unread) throw new Exception("Her prompt lines in another language went wrong: explained " + explained + ", read " + values + ", trailing space kept " + kept + ", the line above read as nothing else " + unread);
                checks.Add("PASS in another language the line above \"sys prompt\" and \"you prompt\" holds \"> sys:\" and \"> you:\" and is never read as a setting, note or switched-off line, and both settings keep their value with its trailing space");
            }
            finally
            {
                Kept = was;
                lock (writing) { read = DateTime.MinValue; wasLong = -1; }
                try { File.Delete(path); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
        }

        internal static string Of(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Fresh();
            lock (writing)
            {
                string said;
                if (!theirs.TryGetValue(name, out said)) return null;
                said = (said ?? "").Trim();
                if (said.Length == 0) return null;
                // Unchanged from stock is not an override: it should still be translated like a stock line.
                return string.Equals(said, name, StringComparison.Ordinal) ? null : said;
            }
        }

        // Re-read only when the file actually changed, since this is called for every bubble.
        private static void Fresh()
        {
            if (string.IsNullOrEmpty(Kept)) return;
            try
            {
                FileInfo about = new FileInfo(Kept);
                if (!about.Exists)
                {
                    lock (writing) { if (theirs.Count > 0) theirs.Clear(); read = DateTime.MinValue; wasLong = -1; }
                    return;
                }
                if (about.LastWriteTimeUtc == read && about.Length == wasLong) return;
                string[] lines = CapriDesktop.Notes.ReadLines(Kept);
                lock (writing)
                {
                    theirs.Clear();
                    foreach (string line in lines)
                    {
                        string one = (line ?? "").Trim();
                        if (one.Length == 0 || one.StartsWith("#") || one.StartsWith(">")) continue;
                        int colon = one.IndexOf(':');
                        if (colon <= 0) continue;
                        string name = one.Substring(0, colon).Trim(), said = one.Substring(colon + 1).Trim();
                        if (name.Length > 0) theirs[name] = said;
                    }
                    read = about.LastWriteTimeUtc;
                    wasLong = about.Length;
                }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // The switch is the last thing in the file and kept there, since it is the one setting with a consequence;
        // later names are added above it.
        internal const string SenpaiHeading = "## End User Licence Agreement";

        // The terms, last but one: down here, out of the calendar's way, where the file has ended for most people, with
        // the senpai switch under them. Plain words, short enough to read.
        internal const string DealHeading = "## End User Licence Agreement";

        internal static string[] DealLines()
        {
            return new[]
            {
                DealHeading,
                "",
                ">! ACCEPTANCE. By installing or using this software you accept these terms. If you do not accept",
                ">! them, do not use it.",
                ">! LICENCE. Use of the software is governed by LICENSE.md, PolyForm Noncommercial 1.0.0, and by",
                ">! the notices in THIRD-PARTY-NOTICES.md. Noncommercial use only.",
                ">! NO WARRANTY. The software is provided \"as is\", without warranty of any kind, express or",
                ">! implied, including but not limited to the implied warranties of merchantability, fitness for a",
                ">! particular purpose and non-infringement.",
                ">! LIMITATION OF LIABILITY. In no event shall the authors or copyright holders be liable for any",
                ">! claim, damages or other liability, whether in an action of contract, tort or otherwise, arising",
                ">! from, out of or in connection with the software or its use.",
                ">! NO PROFESSIONAL ADVICE. Output is generated text and is not medical, legal, financial or safety",
                ">! advice. Verify anything that matters.",
                ">! USER RESPONSIBILITY. You are responsible for what you instruct the software to do on your",
                ">! computer and for the content you supply to it.",
                ">! THIRD-PARTY SERVICES. If you configure a cloud brain or voice service, your input is sent to",
                ">! that provider and is governed by that provider's own terms and privacy policy.",
                ">! AGE RESTRICTION. This software is for persons aged 18 or over, or the age of majority where",
                ">! you live, whichever is greater.",
                ">! If you've read this far, accepting this isn't needed and might make your girlfriend jealous."
            };
        }

        // Each line written with CRLF stripped, so a "$" anchor can match the heading, and the warning stays beside its
        // switch.
        private static List<string> Rows(string had)
        {
            return new List<string>((had ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'));
        }

        private static bool IsSenpaiHead(string row)
        {
            Match m = Regex.Match(row ?? "", @"^[ \t]*##[ \t]*(.+?)[ \t]*$");
            return m.Success && (m.Groups[1].Value.Equals("End User Licence Agreement", StringComparison.OrdinalIgnoreCase) || Legacy.WasSysHead(m.Groups[1].Value));
        }
        private static bool IsSenpaiKey(string row)
        {
            Match m = Regex.Match(row ?? "", @"^[ \t]*([^:\r\n]+?)[ \t]*:");
            return m.Success && (m.Groups[1].Value.Equals(SenpaiKey, StringComparison.OrdinalIgnoreCase) || Legacy.WasSysKey(m.Groups[1].Value));
        }

        // The block pulled out of the file: its heading, whatever note follows it, and the key wherever that ended
        // up. What comes back is the rest of the file, and the block itself through "block".
        private static List<string> WithoutSenpai(List<string> rows, out List<string> block, out string key)
        {
            block = new List<string>();
            key = null;
            List<string> rest = new List<string>();
            for (int at = 0; at < rows.Count; at++)
            {
                string row = rows[at];
                if (IsSenpaiKey(row)) { key = row.Trim(); continue; }
                if (!IsSenpaiHead(row)) { rest.Add(row); continue; }
                // Its heading, and the note under it: blank lines and quoted lines, up to anything else.
                block.Add(row.Trim());
                int look = at + 1;
                while (look < rows.Count)
                {
                    string next = rows[look];
                    if (next.Trim().Length == 0) { look++; continue; }
                    if (!next.TrimStart().StartsWith(">", StringComparison.Ordinal)) break;
                    block.Add(next.Trim());
                    look++;
                }
                at = look - 1;
            }
            return rest;
        }

        // Every name filed under its own heading, moved exactly as written; one already in place stays. Renamed
        // headings and keys are swapped in place, keeping their answer, and group banners go above their first section.
        private static readonly string[,] Banners =
        {
            { "############### app settings", "Never watch these" },
            { "############### convo settings", "What goes in front of a line" },
            { "############### colour settings", "Her screensaver" },
            { "### EULA: default blurb ###", "End User Licence Agreement" },
        };

        // The name in front of the colon on a setting line, or null when the line is not one. A note line and a
        // blank line are not, and neither is a value holding a colon of its own, since only the first counts.
        private static string Named(string one)
        {
            if (one.Length == 0 || one.StartsWith(">", StringComparison.Ordinal) || one.StartsWith("#", StringComparison.Ordinal)) return null;
            int colon = one.IndexOf(':');
            if (colon <= 0) return null;
            return one.Substring(0, colon).Trim();
        }

        // A fresh sys.md, and the end every sys.md keeps: the terms, then the switch.
        internal static void FileCheck(System.Collections.Generic.List<string> checks, string folder)
        {
            string was = Kept;
            try
            {
                Kept = Path.Combine(folder, "sys-filed.md");
                // Nothing in a file anybody may open says what the answer unlocks.
                string fresh2 = Text;
                if (Legacy.SaysUnsaid(fresh2)) throw new Exception("A fresh sys.md says a word it must never say.");
                if (fresh2.IndexOf(SenpaiKey + ":", StringComparison.Ordinal) < 0) throw new Exception("A fresh sys.md has no " + SenpaiKey + " line.");
                if (fresh2.IndexOf(SenpaiKey + ":", StringComparison.Ordinal) < fresh2.IndexOf(SenpaiHeading, StringComparison.Ordinal))
                    throw new Exception("The question is above the terms it belongs to.");
                if (!Regex.IsMatch(fresh2, "(?mi)^[ \t]*" + Regex.Escape(SenpaiKey) + "[ \t]*:[ \t]*No[ \t\r]*$"))
                    throw new Exception("A fresh sys.md does not ship the answer as No.");
                checks.Add("PASS a fresh sys.md names nothing of what the answer unlocks, carries the question under the terms, and ships it as No");
                EndCheck(checks, folder);
            }
            finally { Kept = was; }
        }

        // Null when the file ends with the banner (if it has one) right above the terms, the terms once, and the switch once
        // as the last line, answered "want".
        private static string EndsRight(string text, string want)
        {
            List<string> rows = Rows(text);
            while (rows.Count > 0 && rows[rows.Count - 1].Trim().Length == 0) rows.RemoveAt(rows.Count - 1);
            int heads = rows.FindAll(IsSenpaiHead).Count, keys = rows.FindAll(IsSenpaiKey).Count, banners = rows.FindAll(row => row.Trim() == EulaBanner).Count;
            if (heads != 1 || keys != 1 || banners > 1) return heads + " headings, " + keys + " switches, " + banners + " banners";
            string last = rows.Count == 0 ? "" : rows[rows.Count - 1].Trim();
            if (!IsSenpaiKey(last) || !last.EndsWith(want, StringComparison.OrdinalIgnoreCase)) return "it ends with \"" + last + "\", not the switch answered " + want;
            int head = rows.FindIndex(IsSenpaiHead);
            for (int at = head + 1; at < rows.Count - 1; at++)
            {
                string one = rows[at].Trim();
                if (one.Length > 0 && !one.StartsWith(">", StringComparison.Ordinal) && !one.StartsWith(CleanupKey, StringComparison.OrdinalIgnoreCase)) return "\"" + one + "\" sits between the terms and the switch";
            }
            if (banners == 1)
            {
                int banner = rows.FindIndex(row => row.Trim() == EulaBanner);
                for (int at = banner + 1; at < head; at++) if (rows[at].Trim().Length > 0) return "the banner is not right above the terms";
            }
            return null;
        }

        // Every shape a sys.md has been found in, or could be: each ends with the terms and the switch once, their answer
        // kept, and stays that way on the next start.
        private static void EndCheck(System.Collections.Generic.List<string> checks, string folder)
        {
            string newline = Environment.NewLine, fresh = Text;
            string body = fresh.Substring(0, fresh.IndexOf(EulaBanner, StringComparison.Ordinal)).TrimEnd() + newline;
            string terms = string.Join(newline, DealLines()) + newline;
            int firstHeading = body.IndexOf(newline + "## ", StringComparison.Ordinal);
            int afterFirst = body.IndexOf(newline + newline, firstHeading + 4, StringComparison.Ordinal);
            // Name, file, the answer they gave SetSenpai when the file had none, the answer it must end with.
            string[,] shapes =
            {
                { "a fresh file", fresh, "", "No" },
                { "an old \"The deal\" section with the switch inside it", body + newline + "## The deal" + newline + newline + ">! Old terms." + newline + newline + SenpaiKey + ": Yes" + newline, "", "Yes" },
                { "no terms and no switch", body, "Yes", "Yes" },
                { "the terms with no switch", body + newline + terms, "No", "No" },
                { "the switch with no terms", body + newline + SenpaiKey + ": Yes" + newline, "", "Yes" },
                { "the switch halfway up", body.Substring(0, afterFirst) + newline + newline + SenpaiKey + ": Yes" + body.Substring(afterFirst) + newline + terms, "", "Yes" },
                { "a section added under the switch", body + newline + terms + newline + SenpaiKey + ": Yes" + newline + newline + "## Something new" + newline + newline + "new thing: 1" + newline, "", "Yes" },
                { "Linux line ends", (body + newline + terms + newline + SenpaiKey + ": Yes" + newline).Replace("\r\n", "\n"), "", "Yes" },
                { "two copies of the terms", body + newline + terms + newline + SenpaiKey + ": Yes" + newline + newline + terms, "", "Yes" },
                { "terms quoted the old way", body + newline + terms.Replace(">!", ">") + newline + SenpaiKey + ": Yes" + newline, "", "Yes" },
                { "a blank answer", body + newline + terms + newline + SenpaiKey + ":" + newline, "No", "No" },
            };
            for (int i = 0; i < shapes.GetLength(0); i++)
            {
                Kept = Path.Combine(folder, "sys-end-" + i + ".md");
                File.WriteAllText(Kept, shapes[i, 1], new System.Text.UTF8Encoding(false));
                lock (writing) { read = DateTime.MinValue; wasLong = -1; }
                Write(false);
                TopUp();
                if (Senpai == null) SetSenpai(shapes[i, 2] == "Yes");
                string once = CapriDesktop.Notes.ReadAll(Kept);
                string wrong = EndsRight(once, shapes[i, 3]);
                if (wrong != null) throw new Exception("A sys.md with " + shapes[i, 0] + " didn't end with the terms and the switch once, answer kept: " + wrong);
                // The next starts keep it so. A file a top up added sections to can take one more start to settle its blank
                // lines (a grouping pass that predates this), so it is the second and third that must match.
                TopUp();
                string twice = CapriDesktop.Notes.ReadAll(Kept);
                TopUp();
                wrong = EndsRight(twice, shapes[i, 3]);
                if (wrong != null || CapriDesktop.Notes.ReadAll(Kept) != twice) throw new Exception("A sys.md with " + shapes[i, 0] + " didn't stay put on the next starts: " + (wrong ?? "it changed again"));
            }
            checks.Add("PASS every sys.md ends with the terms and the switch, once each and their answer kept, from a fresh file, an old \"The deal\" section holding the switch, no terms or no switch, the switch halfway up, a section added under it, Linux line ends, two copies of the terms, old quoting and a blank answer, and stays so on the next start");

            // The other copies in her folder: the other side's, with no switch, takes this side's answer; a loose one from
            // before the split keeps its own.
            string settings = Path.Combine(folder, "end-" + Guid.NewGuid().ToString("N").Substring(0, 6), Personalities.SettingsName);
            Directory.CreateDirectory(Path.Combine(settings, Personalities.PlainName));
            Directory.CreateDirectory(Path.Combine(settings, Personalities.SenpaiName));
            string other = Path.Combine(settings, Personalities.SenpaiName, FileName), loose = Path.Combine(settings, FileName);
            File.WriteAllText(other, body + newline + "## Something new" + newline + newline + "new thing: 1" + newline);
            File.WriteAllText(loose, body + newline + terms + newline + SenpaiKey + ": No" + newline + newline + "## Old leftovers" + newline + newline + "old thing: 2" + newline);
            Kept = Path.Combine(settings, Personalities.PlainName, FileName);
            File.WriteAllText(Kept, body + newline + terms + newline + SenpaiKey + ": Yes" + newline);
            lock (writing) { read = DateTime.MinValue; wasLong = -1; }
            TopUp();
            string otherWrong = EndsRight(CapriDesktop.Notes.ReadAll(other), "Yes"), looseWrong = EndsRight(CapriDesktop.Notes.ReadAll(loose), "No");
            if (otherWrong != null || looseWrong != null) throw new Exception("The other copies of sys.md didn't end right: the other side's " + otherWrong + ", the loose one " + looseWrong);
            checks.Add("PASS the other side's sys.md and a loose one from before the split end with the terms and the switch too: one with no switch takes this side's answer, one with a switch keeps its own");

            // Reset to stock keeps their answer.
            Kept = Path.Combine(folder, "sys-end-stock.md");
            File.WriteAllText(Kept, body + newline + terms + newline + SenpaiKey + ": Yes" + newline);
            string stocked = StockKeeping(Kept);
            if (EndsRight(stocked, "Yes") != null) throw new Exception("Reset to stock wrote over their answer: " + EndsRight(stocked, "Yes"));
            checks.Add("PASS reset to stock writes her stock sys.md with their answer kept at the bottom");
        }

        // Yes deletes the senpai side's own files when the switch goes off.
        internal const string CleanupKey = "cleanup files";

        private static bool OnFullSide
        {
            get
            {
                try { return !string.IsNullOrEmpty(Kept) && string.Equals(Path.GetFileName(Path.GetDirectoryName(Kept)), Personalities.SenpaiName, StringComparison.OrdinalIgnoreCase); }
                catch (Exception) { return false; }
            }
        }

        internal static bool Cleanup
        {
            get
            {
                try
                {
                    if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept)) return false;
                    Match said = Regex.Match(CapriDesktop.Notes.ReadAll(Kept), @"(?mi)^[ \t]*" + Regex.Escape(CleanupKey) + @"[ \t]*:[ \t]*(\S+)");
                    string value = said.Success ? said.Groups[1].Value : "";
                    return value.Equals("yes", StringComparison.OrdinalIgnoreCase) || value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); return false; }
            }
        }

        // Hard rule: the terms and the switch under them end every sys.md, once each. Run after every pass that writes
        // the file, and on a file whose switch line is missing too, so nothing appended meanwhile sits under the terms.
        private static void SenpaiLast()
        {
            try
            {
                if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept)) return;
                string had = CapriDesktop.Notes.ReadAll(Kept);
                string made = Ended(had, null, OnFullSide);
                if (made == had) return;
                Notes.Put(Kept, made);
                lock (writing) { read = DateTime.MinValue; wasLong = -1; }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // Any other sys.md (the other side's copy, or a loose one from before the split) ended the same way. Its switch is
        // kept as it is; one with no switch line gets keyIfMissing, the one the side in use has. True when it was written.
        internal static bool EnsureEnd(string path, string keyIfMissing)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
                string had = CapriDesktop.Notes.ReadAll(path);
                string made = Ended(had, keyIfMissing, null);
                if (made == had) return false;
                Notes.Put(path, made);
                if (string.Equals(Path.GetFullPath(path), string.IsNullOrEmpty(Kept) ? "" : Path.GetFullPath(Kept), StringComparison.OrdinalIgnoreCase))
                    lock (writing) { read = DateTime.MinValue; wasLong = -1; }
                return true;
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); return false; }
        }

        private static string EulaBanner { get { return Banners[Banners.GetLength(0) - 1, 0]; } }

        // Banner, terms, cleanup choice and switch taken out and put back as its end, in that order, one copy of the
        // terms. fullSide true adds the cleanup choice, false drops it, null leaves it; with no switch and no
        // keyIfMissing the terms end it.
        private static string Ended(string had, string keyIfMissing, bool? fullSide)
        {
            List<string> block, rows = Rows(had);
            string key;
            List<string> rest = WithoutSenpai(rows, out block, out key);
            if (key == null) key = keyIfMissing;
            if (key != null) key = Answered(key);
            int second = block.Count > 1 ? block.FindIndex(1, IsSenpaiHead) : -1;
            if (second > 0) block.RemoveRange(second, block.Count - second);
            if (block.Count <= 1) block = new List<string>(DealLines());
            // A blank line under the heading, like every other section in the file has.
            if (block.Count > 1 && block[1].Length > 0) block.Insert(1, "");
            string banner = EulaBanner;
            bool bannered = rest.RemoveAll(row => row.Trim() == banner) > 0;
            // The cleanup choice sits under the terms and above the answer, on the senpai side only.
            string cleanup = null;
            for (int at = rest.Count - 1; at >= 0; at--)
                if (Regex.IsMatch(rest[at], @"^[ \t]*" + Regex.Escape(CleanupKey) + @"[ \t]*:", RegexOptions.IgnoreCase))
                {
                    if (cleanup == null) cleanup = rest[at].Trim();
                    rest.RemoveAt(at);
                }
            // What the block left behind where it was taken from is at most the two blank lines a group ends with, so moving it
            // again on the next start finds nothing to change.
            for (int at = rest.Count - 1; at >= 2; at--)
                if (rest[at].Trim().Length == 0 && rest[at - 1].Trim().Length == 0 && rest[at - 2].Trim().Length == 0) rest.RemoveAt(at);
            if (fullSide == true) { if (cleanup == null) cleanup = CleanupKey + ": No"; }
            else if (fullSide == false) cleanup = null;
            string tail = (cleanup == null ? "" : cleanup + Environment.NewLine) + (key == null ? "" : key + Environment.NewLine);
            return string.Join(Environment.NewLine, rest.ToArray()).TrimEnd()
                 + Environment.NewLine + Environment.NewLine
                 + (bannered ? banner + Environment.NewLine + Environment.NewLine : "")
                 + string.Join(Environment.NewLine, block.ToArray()).TrimEnd() + Environment.NewLine
                 + (tail.Length > 0 ? Environment.NewLine + tail : "");
        }

        // Reset to stock, keeping their answer: the stock file ships "No", and writing that over their "Yes" turned the
        // switch off under them. The cleanup choice they made is kept the same way.
        internal static string StockKeeping(string path)
        {
            string text = Text;
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return text;
                string key = null, cleanup = null;
                foreach (string row in Rows(CapriDesktop.Notes.ReadAll(path)))
                {
                    if (IsSenpaiKey(row) && key == null) key = Answered(row);
                    else if (cleanup == null && Regex.IsMatch(row, @"^[ \t]*" + Regex.Escape(CleanupKey) + @"[ \t]*:", RegexOptions.IgnoreCase)) cleanup = row.Trim();
                }
                if (key != null && key.Length > SenpaiKey.Length + 2) text = Regex.Replace(text, @"(?mi)^[ \t]*" + Regex.Escape(SenpaiKey) + @"[ \t]*:[^\r\n]*", key.Replace("$", "$$"));
                if (cleanup != null) text = Regex.Replace(text, @"(?mi)^[ \t]*" + Regex.Escape(CleanupKey) + @"[ \t]*:[^\r\n]*", cleanup.Replace("$", "$$"));
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            return text;
        }

        // A sys.md written new on an update keeps the answer in force, on either side: an update never asks again, and the
        // switch stays as it was. A new install's is written as stock, which says No.
        internal static string OnUpdate(bool on) { return WithAnswer(Text, on); }

        // Read the file again on the next look, for a file swapped under her.
        internal static void ForgetRead() { lock (writing) { read = DateTime.MinValue; wasLong = -1; } }

        // An update over a sys.md answered Yes, and edited, writes it new still answered Yes and reads as on; one answered No
        // stays No; a new install's says No.
        internal static void CheckOnUpdate(List<string> checks, string output)
        {
            string keptWas = Kept;
            string file = System.IO.Path.Combine(output, "sys-update-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".md");
            try
            {
                Kept = file;
                Func<string, bool?> read = delegate(string text) { File.WriteAllText(file, text); ForgetRead(); return Senpai; };
                bool? before = read(WithAnswer(Text, true) + "their own line" + Environment.NewLine);
                bool? after = read(Notes.Marked(OnUpdate(before == true)));
                bool? stayedNo = read(Notes.Marked(OnUpdate(false)));
                bool? fresh = read(Text);
                if (before != true || after != true || stayedNo != false || fresh != false)
                    throw new Exception("The terms answer didn't carry over an update: Yes before " + before + ", after the update " + after + ", No kept " + stayedNo + ", a new install " + fresh);
                checks.Add("PASS an update writes sys.md new keeping the terms answer in force: answered Yes it stays Yes and the switch stays on, No stays No, and a new install still says No");
            }
            finally
            {
                Kept = keptWas;
                ForgetRead();
                try { File.Delete(file); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
        }

        // Her stock sys.md with the switch answered: a side written new while the switch it carries is on keeps the answer that
        // turned it on, or she would read No from it and switch straight back off.
        internal static string WithAnswer(string text, bool on)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return Regex.Replace(text, @"(?mi)^[ \t]*" + Regex.Escape(SenpaiKey) + @"[ \t]*:[^\r\n]*", Line(SenpaiKey, on ? "Yes" : "No").Replace("$", "$$"));
        }

        // The switch line with its answer at the column the other settings use, whatever spacing it was written with.
        private static string Answered(string row)
        {
            int colon = (row ?? "").IndexOf(':');
            string said = colon < 0 ? "" : row.Substring(colon + 1).Trim();
            return said.Length == 0 ? SenpaiKey + ":" : Line(SenpaiKey, said);
        }

        // A sys.md is written new from stock on each version (Renew), so between versions nothing is added to it. The terms
        // and the switch under them stay the end of every sys.md, on both sides.
        internal static void TopUp()
        {
            if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept)) return;
            try
            {
                SenpaiLast();
                EndOthers();
            }
            catch (Exception bad) { Notes.Failed("keeping the end of sys.md in SysLines.cs", bad); }
        }

        // The other sys.md files end the same way: the other side's copy, topped up only while that side runs, and a
        // loose one from before the split. Their own switch stays; one with none takes this side's answer.
        private static void EndOthers()
        {
            try
            {
                if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept)) return;
                string here = Path.GetFullPath(Kept);
                string side = Path.GetDirectoryName(here), settings = side == null ? null : Path.GetDirectoryName(side);
                if (settings == null || !string.Equals(Path.GetFileName(settings), Personalities.SettingsName, StringComparison.OrdinalIgnoreCase)) return;
                string key = null;
                foreach (string row in Rows(CapriDesktop.Notes.ReadAll(Kept)))
                    if (IsSenpaiKey(row)) key = Answered(row);
                if (key != null && key.Length <= SenpaiKey.Length + 2) key = null;
                foreach (string other in new[] { Path.Combine(settings, FileName), Path.Combine(settings, Personalities.PlainName, FileName), Path.Combine(settings, Personalities.SenpaiName, FileName) })
                    if (!string.Equals(Path.GetFullPath(other), here, StringComparison.OrdinalIgnoreCase)) EnsureEnd(other, key);
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        private static int Put(List<string> made, string at, int after, Dictionary<string, List<string>> want, List<string> heads)
        {
            List<string> rows;
            if (at == null || !want.TryGetValue(at, out rows) || rows.Count == 0) return after;
            want[at] = new List<string>();
            made.InsertRange(after, rows);
            return after + rows.Count;
        }

        internal static void Write(bool over)
        {
            if (string.IsNullOrEmpty(Kept)) return;
            try
            {
                if (!over && File.Exists(Kept)) { Notes.Refresh(Kept, Text); return; }
                string folder = Path.GetDirectoryName(Kept);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) Directory.CreateDirectory(folder);
                Notes.Put(Kept, Text);
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // Her start-up intro, sys.md's section before Toggles > Intros. The intro mods ask for each as "intro_<name>".
        internal const string IntroHeading = "Her start-up intro";
        internal static readonly string[] IntroNames = { "shatter", "hijack", "wake", "door", "door2" };
        internal const string IntroSetting = "intro_";

        // The intros switched on: Toggles > Intros, kept in her settings as their names. Empty means she starts the way she
        // always has.
        internal static Func<string> IntrosSaved = delegate { return null; };

        internal static string[] Intros()
        {
            List<string> on = new List<string>();
            foreach (string named in (IntrosSaved() ?? "").Split(','))
            {
                string found = Known(named.Trim());
                if (found != null && !on.Contains(found)) on.Add(found);
            }
            return on.ToArray();
        }

        // The intro picks left sys.md for Toggles > Intros. A sys.md from before hands its picks over once, in the order
        // written, and loses the section. Null when it had none.
        internal static string[] TakeIntros(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                string[] picked = IntrosIn(path);
                if (picked == null) return null;
                string text = CapriDesktop.Notes.ReadAll(path);
                string newline = text.Contains("\r\n") ? "\r\n" : "\n";
                List<string> rows = new List<string>(text.Replace("\r\n", "\n").Split('\n'));
                int head = rows.FindIndex(row => row.Trim().StartsWith("#", StringComparison.Ordinal) && string.Equals(row.Trim().TrimStart('#').Trim(), IntroHeading, StringComparison.OrdinalIgnoreCase));
                if (head >= 0)
                {
                    int end = head + 1;
                    while (end < rows.Count && !rows[end].TrimStart().StartsWith("#", StringComparison.Ordinal)) end++;
                    rows.RemoveRange(head, end - head);
                }
                Notes.Put(path, string.Join(newline, rows.ToArray()));
                lock (writing) { read = DateTime.MinValue; wasLong = -1; }
                return picked;
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); return null; }
        }

        // The intros a sys.md from before switched on, in the order written; null when it has no intro section.
        private static string[] IntrosIn(string path)
        {
            List<string> on = new List<string>();
            bool had = false;
            try
            {
                bool inside = false;
                foreach (string raw in CapriDesktop.Notes.ReadLines(path))
                {
                    string one = (raw ?? "").Trim();
                    if (one.StartsWith("#", StringComparison.Ordinal))
                    {
                        inside = string.Equals(one.TrimStart('#').Trim(), IntroHeading, StringComparison.OrdinalIgnoreCase);
                        had |= inside;
                        continue;
                    }
                    if (!inside || one.Length == 0 || one.StartsWith(">", StringComparison.Ordinal)) continue;
                    if (one.StartsWith("intros:", StringComparison.OrdinalIgnoreCase))
                    {
                        // "intros: shatter, wake" works as well as the lines under it.
                        foreach (string named in one.Substring(7).Split(','))
                            if (Known(named.Trim()) != null) on.Add(Known(named.Trim()));
                        continue;
                    }
                    string found = Known(one);
                    if (found != null && !on.Contains(found)) on.Add(found);
                }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            return had ? on.ToArray() : null;
        }

        private static string Known(string said)
        {
            foreach (string one in IntroNames)
                if (string.Equals(one, said, StringComparison.OrdinalIgnoreCase)) return one;
            return null;
        }

        private const int Column = 22;

        private static string Line(string name, string said)
        {
            string left = name + ":";
            if (left.Length >= Column) return left + " " + said;
            return left + new string(' ', Column - left.Length) + said;
        }

        internal static string Text
        {
            get
            {
                StringBuilder made = new StringBuilder();
                made.AppendLine("# Her system lines");
                made.AppendLine();
                made.AppendLine(">! The small grey labels she shows after \"> sys: \".");
                made.AppendLine(">! Change the text on the right. Leave one blank to use hers.");
                made.AppendLine(">! Names on the left are hers, so keep them as they are.");
                made.AppendLine(">! Delete this file to put everything back.");
                made.AppendLine();
                made.AppendLine();
                made.AppendLine("############### app settings");
                made.AppendLine("## Never watch these");
                made.AppendLine();
                made.AppendLine(">! Programs Watch my screen skips. Comma separated, and a partial name is enough:");
                made.AppendLine(">! \"brave\" covers brave.exe. Matched on the program and on its window title.");
                made.AppendLine(">! She says nothing at all while one of these is in front.");
                made.AppendLine();
                made.AppendLine(Line(ExcludedKey, ""));
                made.AppendLine();
                made.AppendLine("## Her calendar");
                made.AppendLine();
                made.AppendLine(">! The app she opens to read your calendar. Blank and she uses the one you open most.");
                made.AppendLine(">! She opens it and reads the screen. Nothing signs in and nothing is sent anywhere.");
                made.AppendLine();
                made.AppendLine(Line(CalendarKey, ""));
                made.AppendLine();
                made.AppendLine("## Doomscroll guard");
                made.AppendLine();
                made.AppendLine(">! Sites and apps Doomscroll guard keeps time on, once it's on in Mainframe > Settings > DOOMSCROLL.");
                made.AppendLine(">! Comma separated. Any word counts when it is in a window's title or the program's name.");
                made.AppendLine();
                made.AppendLine(Line(Doomscroll.ListKey, Doomscroll.Stock));
                made.AppendLine();
                made.AppendLine();
                made.AppendLine("############### convo settings");
                made.AppendLine("## What goes in front of a line");
                made.AppendLine();
                made.AppendLine(">! Change these to anything. A trailing space is added for you.");
                made.AppendLine();
                made.AppendLine(Line("sys prompt", SysMark));
                made.AppendLine(Line("you prompt", YouMark));
                made.AppendLine();
                made.AppendLine("## What she says herself");
                made.AppendLine();
                made.AppendLine(">! Real speech, not labels. Offer several with \"|\" between them and she picks one.");
                made.AppendLine();
                for (int line = 0; line < Spoken.GetLength(0); line++)
                    made.AppendLine(Line(Spoken[line, 0], Spoken[line, 1]));
                made.AppendLine();
                made.AppendLine("## How she comes and goes");
                made.AppendLine();
                made.AppendLine(">! 1: her hello and goodbye come from her personality file. 0: from the lines above.");
                made.AppendLine(">! Her error line is never affected either way.");
                made.AppendLine();
                made.AppendLine(Line(OwnLinesKey, "1"));
                made.AppendLine();
                made.AppendLine("## The labels after it");
                made.AppendLine();
                made.AppendLine(">! Taglines, not speech. They name what just happened.");
                made.AppendLine();
                for (int line = 0; line < Stock.GetLength(0); line++)
                {
                    if (!Brain.SenpaiOn && SenpaiLabel(Stock[line, 0])) continue;
                    made.AppendLine(Line(Stock[line, 0], Stock[line, 1]));
                }
                if (Brain.SenpaiOn && Extended.Has("click.heard")) made.AppendLine(Line(Extended.Get("click.heard"), Extended.Get("click.heard")));
                made.AppendLine();
                made.AppendLine(">! What she says when handled lives in the personalities folder.");
                made.AppendLine();
                made.AppendLine();
                made.AppendLine("############### colour settings");
                foreach (string row in SaverLines()) made.AppendLine(row);
                made.AppendLine();
                made.AppendLine();
                made.AppendLine("### EULA: default blurb ###");
                foreach (string row in DealLines()) made.AppendLine(row);
                made.AppendLine();
                made.AppendLine(Line(SenpaiKey, "No"));
                return made.ToString();
            }
        }
    }
}
