using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // The little grey labels she shows after "> sys: ": "pets you", "settings", "brain online" and the rest. They were
    // twelve hardcoded strings, so the one part of her voice nobody could change was the part that narrates her.
    //
    // Every label already passes through one place on its way to the bubble, so this is a lookup in front of that with
    // the author's text as the fallback. A file that is missing, empty, mistyped or half-deleted degrades to stock
    // rather than to blank, which matters because a blank label would read as a bug in her rather than in the file.
    //
    // Same shape as twitch.md: read when it changes, UTF-8 with no byte order mark, and written once with every line
    // already in it so there is nothing to guess at.
    internal static class SysLines
    {
        internal const string FileName = "sys.md";
        // The adult switch. Kept here, at the bottom of a file about her wording, rather than on a menu
        // anyone can hit by accident. 1 is on, anything else is off.
        internal const string SenpaiKey = "confirm";
        // What the key used to be called. Still read, so a file written before this keeps its answer rather
        // than being treated as unanswered and asking again.
        internal const string WasSenpaiKey = "uncensored";

        // Programs she will not look at with Watch my screen on. Comma separated, matched against the program's
        // own name and its window title, so "brave" covers brave.exe and "qview" covers the image viewer whatever
        // it calls its window. Her screen watching is the one feature that reads whatever happens to be in front,
        // so the list of what it must not read belongs with her other settings and not buried in a menu.
        internal const string ExcludedKey = "never watch";

        // Her calendar, by opening the one they use. Nothing signs in anywhere: she opens the app and reads what
        // is on screen, the same as anything else she looks at. Blank means she works out which one from what
        // they keep in front; a name here settles it.
        internal const string CalendarKey = "my calendar";

        // Whether her hello and goodbye come from whoever she is, or from the lines below. 1 is her personality.
        // Her error line is never part of this: that one has to read the same whoever she is, or a report of it
        // means nothing.
        internal const string OwnLinesKey = "personality lines";
        internal static string Kept;

        private static readonly object writing = new object();
        private static readonly Dictionary<string, string> theirs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static DateTime read = DateTime.MinValue;
        private static long wasLong = -1;

        // The author's labels, in the order Said.Labels registers them. The left side is the name used in code and must
        // not change; the right side is only what is shown.
        // Labels that only mean anything with the switch on. They are not written into a file on the other
        // side of it, and one already there is taken out, the same as the moods.
        internal static bool SenpaiLabel(string named) { return named == "slaps you"; }

        internal static readonly string[,] Stock =
        {
            { "startup", "startup" },
            { "pets you", "pets you" },
            { "slaps you", "slaps you" },
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

        // Lines she actually says, as opposed to the labels above. Several can be offered at once, split on "|", and she
        // takes one at random. Her hellos were picked by the hour out of three fixed strings, so the first thing she ever
        // said was the one thing nobody could rewrite.
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

        // Whether adult mode is on, straight out of the file. Null when the file does not mention it yet, which is
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
                    if (!theirs.TryGetValue(SenpaiKey, out said) && !theirs.TryGetValue(WasSenpaiKey, out said)) return null;
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
                string had = File.ReadAllText(Kept);
                // Yes unlocks it. It ships No, so a fresh install has answered nothing.
                string want = SenpaiKey + ": " + (on ? "Yes" : "No");
                Match found = Regex.Match(had, @"(?mi)^[ \t]*" + Regex.Escape(SenpaiKey) + @"[ \t]*:[ \t]*.*$");
                if (found.Success) had = had.Substring(0, found.Index) + want + had.Substring(found.Index + found.Length);
                else
                {
                    if (had.Length > 0 && !had.EndsWith("\n")) had += Environment.NewLine;
                    // The terms, then the question. Nothing here says what answering it does.
                    had += Environment.NewLine + SenpaiHeading + Environment.NewLine + Environment.NewLine
                        + string.Join(Environment.NewLine, DealLines(), 1, DealLines().Length - 1) + Environment.NewLine + Environment.NewLine + want + Environment.NewLine;
                }
                Notes.Put(Kept, had);
                lock (writing) { read = DateTime.MinValue; wasLong = -1; }
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

        // The author's own text for a name, so a caller can fall back without holding its own copy.
        internal static string Stocked(string name)
        {
            for (int line = 0; line < Spoken.GetLength(0); line++)
                if (string.Equals(Spoken[line, 0], name, StringComparison.OrdinalIgnoreCase)) return Spoken[line, 1];
            for (int line = 0; line < Stock.GetLength(0); line++)
                if (string.Equals(Stock[line, 0], name, StringComparison.OrdinalIgnoreCase)) return Stock[line, 1];
            return null;
        }

        // The four colours of her bubble, theirs to set. Named here rather than in the window that paints them,
        // because the names are what people type into sys.md and a setting's name is not a painting detail.
        internal const string InkKey = "bubble text";
        internal const string TitleKey = "bubble title";
        internal const string EdgeKey = "bubble outline";
        internal const string BackKey = "bubble background";

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

        // The .wav they pointed at, found whether they wrote a whole path or just a name beside her files.
        // Null when there is none, which is how it ships: nothing like this comes with her.
        // A screensaver sound that sys.md points at outside her folder, copied into sounds and the line rewritten to the copy,
        // so the file it names is one a backup of her folder carries.
        internal static void KeepSaverSound()
        {
            try
            {
                string said = (Of(SaverSoundKey) ?? "").Trim().Trim('"');
                if (!Library.Loose(said)) return;
                string kept = Library.Keep(said, Library.Sounds);
                if (string.Equals(kept, said, StringComparison.OrdinalIgnoreCase)) return;
                string relative = Library.Sounds + "/" + Path.GetFileName(kept);
                string text = File.ReadAllText(Kept);
                string made = Regex.Replace(text, @"(?mi)^([ \t]*" + Regex.Escape(SaverSoundKey) + @"[ \t]*:[ \t]*)[^\r\n]*", "${1}" + relative.Replace("$", "$$"));
                if (made != text) Notes.Put(Kept, made);
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
            { SaverKey, "0" }, { SaverStyleKey, "terminals" }, { SaverColourKey, "" }, { SaverGroundKey, "" },
            { SaverSizeKey, "normal" }, { SaverSpeedKey, "normal" }, { SaverThroughKey, "0" },
            { SaverScreensKey, "all" }, { SaverFlashKey, "on" }, { SaverSoundKey, "" },
            { SaverVolumeKey, "20" }, { SaverAfterKey, "10" }
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
                "> What fills the screen while the PC is left alone. 1 is on, 0 is off. Spacebar ends it, or",
                "> Escape, and it never covers a game, a full screen app or a stream. Her right-click menu shows",
                "> any of them straight away without changing this.",
                ">",
                "> style:       terminals, little windows writing their own output. hexes, flashing hexagons.",
                ">              code, lines written down columns. rain, characters falling.",
                "> colour:      a name like blue or a hex code, the same as her bubble's. Blank is hers, and red",
                ">              for hexes. background: what is behind it. Blank is near black.",
                "> size:        small, normal, large, or a number where 1 is normal and 2 is twice.",
                "> speed:       slow, normal, fast, or a number the same way.",
                "> see through: 0 to 100, how much of the desktop shows through it. 0 is solid.",
                "> screens:     all, or main for only the screen she is on.",
                "> flashing:    on, gentle or off. Gentle slows it right down and off holds everything steady.",
                "> sound:       a .wav to loop while it is shown from her menu or sounded as her red alert. Blank",
                ">              is silent.",
                ">              It never plays when the screensaver comes up by itself, since that is an alarm at an",
">              empty chair. Nothing of the sort ships with her: this is for a file of your own.",
                "> volume:      0 to 100, how loud that is against her sound volume. A looping alarm wants far",
                ">              less than her chimes do, so it starts at 20.",
                "> after:       minutes, and it takes a fraction, so 0.2 is twelve seconds.",
                ""
            };
        }

        // Whether they turned it on. Off unless the line says one of the words for yes, since it covers the screen.
        internal static bool Saver
        {
            get
            {
                string said = (Of(SaverKey) ?? "").Trim();
                return said == "1" || said.Equals("yes", StringComparison.OrdinalIgnoreCase)
                    || said.Equals("true", StringComparison.OrdinalIgnoreCase) || said.Equals("on", StringComparison.OrdinalIgnoreCase);
            }
        }

        // How long the PC has to be left alone first. Held between six seconds and a day: a typo of 0 would put it
        // over the screen the moment they stopped typing.
        internal static double SaverAfter
        {
            get
            {
                double minutes;
                if (!double.TryParse((Of(SaverAfterKey) ?? "").Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out minutes) || minutes <= 0) minutes = 10;
                return Math.Max(0.1, Math.Min(1440, minutes));
            }
        }

        // The section as it is written into the file, used both by the file she ships and by the top up that puts
        // it into a sys.md written before it existed. One copy, because two drift and the quiet one is the one
        // nobody reads.
        // Her bubble's colours lived here and in layout.md, and the pair here won, so the same four colours
        // sat in two files with one set doing nothing. They are layout.md's now. Whatever is set here is
        // carried across first, so nobody's bubble changes colour under them, and then the section goes.
        internal const string ColourHeading = "## Her bubble's colours";

        private static readonly string[,] WentToLayout =
        {
            { InkKey, "bubble text" },
            { TitleKey, "bubble title" },
            { EdgeKey, "bubble outline" },
            { BackKey, "bubble background" },
        };

        private static void Painted()
        {
            try
            {
                if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept)) return;
                string had = File.ReadAllText(Kept);
                if (had.IndexOf(ColourHeading, StringComparison.OrdinalIgnoreCase) < 0) return;
                for (int one = 0; one < WentToLayout.GetLength(0); one++)
                {
                    string said = Of(WentToLayout[one, 0]);
                    if (!string.IsNullOrEmpty(said)) Layout.Carry(WentToLayout[one, 1], said);
                }
                Unheaded(ColourHeading);
            }
            catch (Exception bad) { Notes.Failed("carrying her bubble colours into layout.md", bad); }
        }

        // A colour they wrote, or hers when the line is blank or not a colour at all. Names like "blue" and hex
        // like #1affd5 both work, with or without the hash, and three digits as well as six. Eight digits set
        // how solid it is, since her bubble is drawn over whatever is behind her.
        //
        // A wrong one is ignored rather than refused: a typo in a colour must not take her bubble away.
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

        // Their text for a label, or null when they have not set one. Null rather than the stock string, so the caller
        // can still send an unchanged label through her translator the way it always did.
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
                // Unchanged from stock is not an override: it should still be translated like the author's own line.
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
                string[] lines = File.ReadAllLines(Kept);
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

        // Adult mode is the last thing in the file and has to stay there. It is the one switch in here with a
        // consequence, so it wants reading deliberately at the bottom rather than coming upon it halfway down
        // between two label rows.
        //
        // It did not stay there on its own: every name added by a later build was appended to the end of the file,
        // which put it after this block and left the switch stranded in the middle of the file it was meant to end.
        internal const string SenpaiHeading = "## End User Licence Agreement";

        // The terms, last but one. Nobody reads the top of a settings file for these, and putting them beside her
        // calendar only buries the calendar; down here they sit where the file has already ended for most people,
        // with the senpai switch under them. Plain words, and short enough to be read if anyone does.
        internal const string DealHeading = "## End User Licence Agreement";

        internal static string[] DealLines()
        {
            return new[]
            {
                DealHeading,
                "",
                "> ACCEPTANCE. By installing or using this software you accept these terms. If you do not accept",
                "> them, do not use it.",
                "> LICENCE. Use of the software is governed by LICENSE.md, PolyForm Noncommercial 1.0.0, and by",
                "> the notices in THIRD-PARTY-NOTICES.md. Noncommercial use only.",
                "> NO WARRANTY. The software is provided \"as is\", without warranty of any kind, express or",
                "> implied, including but not limited to the implied warranties of merchantability, fitness for a",
                "> particular purpose and non-infringement.",
                "> LIMITATION OF LIABILITY. In no event shall the authors or copyright holders be liable for any",
                "> claim, damages or other liability, whether in an action of contract, tort or otherwise, arising",
                "> from, out of or in connection with the software or its use.",
                "> NO PROFESSIONAL ADVICE. Output is generated text and is not medical, legal, financial or safety",
                "> advice. Verify anything that matters.",
                "> USER RESPONSIBILITY. You are responsible for what you instruct the software to do on your",
                "> computer and for the content you supply to it.",
                "> THIRD-PARTY SERVICES. If you configure a cloud brain or voice service, your input is sent to",
                "> that provider and is governed by that provider's own terms and privacy policy.",
                "> AGE RESTRICTION. This software is for persons aged 18 or over, or the age of majority where",
                "> you live, whichever is greater.",
                "> If you've read this far, accepting this isn't needed and might make your girlfriend jealous."
            };
        }

        // The warning that goes with it, so a block that has to be rebuilt still carries one.
        // Nothing here says what the answer does. A file anybody may open states the terms and asks whether
        // they are accepted, and that is all it says.
        private static readonly string[] SenpaiNote = { };

        // Every line of the file written out with CRLF stripped, which is the whole reason this is done by line
        // rather than by one regex over the text: an earlier attempt anchored its heading match with "$", which
        // cannot match the carriage return the file actually has, so the heading was never found and only the key
        // moved, leaving the warning stranded halfway up the file away from the switch it warns about.
        private static List<string> Rows(string had)
        {
            return new List<string>((had ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'));
        }

        private static bool IsSenpaiHead(string row) { return Regex.IsMatch(row ?? "", @"^[ \t]*##[ \t]*(Adult mode|End User Licence Agreement)[ \t]*$", RegexOptions.IgnoreCase); }
        private static bool IsSenpaiKey(string row) { return Regex.IsMatch(row ?? "", @"^[ \t]*(" + Regex.Escape(SenpaiKey) + "|" + Regex.Escape(WasSenpaiKey) + @")[ \t]*:", RegexOptions.IgnoreCase); }

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

        // Adult mode is the last thing in the file and stays there. It is the one switch in here with a consequence,
        // so it wants reading deliberately at the end rather than being come upon halfway down between two label
        // rows. It did not stay there on its own: every name a later build added was appended to the end of the
        // file, which pushed it up into the middle.
        //
        // Rebuilt rather than nudged, so a file where the heading and the key have already drifted apart is put
        // back together instead of being left as it is.
        // Nothing may be in this file twice.
        //
        // Keys are read into a dictionary as the file is walked, so the last one wins, and a top up appends at
        // the end. An edited line near the top was therefore quietly overridden by the stock copy added below
        // it, and their edit looked like it had done nothing. The edit is the one above, so the first of each
        // key is kept and every later copy goes, with any heading left empty going too.
        private static void Tidy()
        {
            try
            {
                if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept)) return;
                string[] rows = File.ReadAllText(Kept).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                Dictionary<string, bool> had = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                List<string> kept = new List<string>();
                bool dropped = false;
                foreach (string raw in rows)
                {
                    string line = raw.Trim();
                    int mark = line.IndexOf(':');
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith(">", StringComparison.Ordinal) || mark <= 0)
                    { kept.Add(raw); continue; }
                    string name = line.Substring(0, mark).Trim();
                    if (name.Length == 0) { kept.Add(raw); continue; }
                    if (had.ContainsKey(name)) { dropped = true; continue; }
                    had[name] = true;
                    kept.Add(raw);
                }
                if (!dropped) return;
                List<string> made = new List<string>();
                for (int at = 0; at < kept.Count; at++)
                {
                    if (!kept[at].TrimStart().StartsWith("##", StringComparison.Ordinal)) { made.Add(kept[at]); continue; }
                    bool anything = false;
                    for (int look = at + 1; look < kept.Count; look++)
                    {
                        string next = kept[look].Trim();
                        if (next.StartsWith("##", StringComparison.Ordinal)) break;
                        if (next.Length > 0 && !next.StartsWith(">", StringComparison.Ordinal)) { anything = true; break; }
                    }
                    if (anything) made.Add(kept[at]);
                    else while (at + 1 < kept.Count && !kept[at + 1].TrimStart().StartsWith("##", StringComparison.Ordinal)) at++;
                }
                while (made.Count > 0 && made[made.Count - 1].Trim().Length == 0) made.RemoveAt(made.Count - 1);
                Notes.Put(Kept, string.Join(Environment.NewLine, made.ToArray()) + Environment.NewLine);
                lock (writing) { read = DateTime.MinValue; wasLong = -1; }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // The two headings a name belongs under, with the note that goes with each. A file written before a
        // heading existed has the names and not the heading, which is the whole fault below.
        private const string LabelHeading = "The labels after it", SpeechHeading = "What she says herself";
        private static readonly string[] LabelNote = { "> Taglines, not speech. They name what just happened." };
        private static readonly string[] SpeechNote = { "> Real speech, not labels. Offer several with \"|\" between them and she picks one." };

        // Every name filed under its own heading. A top up appends a missing name as a bare line and Fitted
        // files it under whatever heading is above the append point, so nine of them sat under the wrong one
        // and "The labels after it" was in no file at all. Moved exactly as written, so an edited value stays
        // theirs, and a name already under its own heading is left alone.
        // A file written before the rename still names what it unlocks, in its heading and in its key. Both
        // are swapped in place, keeping their answer, so an existing install stops saying it too.
        // The group banners, for a file written before they existed. Inserted above the section each group
        // starts with, so an existing file reads the way a fresh one does without its sections being moved.
        private static readonly string[,] Banners =
        {
            { "############### app settings", IntroHeading },
            { "############### convo settings", "What goes in front of a line" },
            { "############### colour settings", "Her screensaver" },
            { "### EULA: default blurb ###", "End User Licence Agreement" },
        };

        // Which sections sit under each banner, in the order a fresh file writes them.
        private static string[] Grouping(int banner)
        {
            if (banner == 0) return new[] { IntroHeading, "Never watch these", "Her calendar" };
            if (banner == 1) return new[] { "What goes in front of a line", "What she says herself", "How she comes and goes", "The labels after it" };
            if (banner == 2) return new[] { "Her screensaver", "Her bubble's colours" };
            return new[] { SenpaiHeading.TrimStart('#').Trim() };
        }

        // A block with its trailing blank lines off, so a second pass cannot pile more on.
        private static List<string> Tight(List<string> block)
        {
            List<string> made = new List<string>(block);
            while (made.Count > 0 && made[made.Count - 1].Trim().Length == 0) made.RemoveAt(made.Count - 1);
            return made;
        }

        private static void Grouped()
        {
            try
            {
                if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept)) return;
                string was = File.ReadAllText(Kept);
                List<string> rows = Rows(was);
                for (int at = 0; at < Banners.GetLength(0); at++)
                {
                    string banner = Banners[at, 0], under = Banners[at, 1];
                    if (rows.Exists(delegate(string row) { return row.Trim() == banner; })) continue;
                    int where = rows.FindIndex(delegate(string row)
                    {
                        string one = row.Trim();
                        return one.StartsWith("##", StringComparison.Ordinal) && !one.StartsWith("###", StringComparison.Ordinal)
                            && string.Equals(one.TrimStart('#').Trim(), under, StringComparison.OrdinalIgnoreCase);
                    });
                    if (where < 0) continue;
                    rows.InsertRange(where, new[] { banner, "" });
                }
                // And each section inside the banner it belongs to. Inserting banners into a file whose
                // sections are in another order leaves the calendar inside convo settings, which is worse than
                // no banner at all. Sections keep their own lines; only their place changes.
                List<string> made = new List<string>();
                List<string> loose = new List<string>();
                Dictionary<string, List<string>> held = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                List<string> order = new List<string>();
                string inside = null;
                foreach (string row in rows)
                {
                    string one = row.Trim();
                    if (one.StartsWith("##", StringComparison.Ordinal) && !one.StartsWith("###", StringComparison.Ordinal))
                    {
                        inside = one.TrimStart('#').Trim();
                        if (!held.ContainsKey(inside)) { held[inside] = new List<string>(); order.Add(inside); }
                        held[inside].Add(row);
                        continue;
                    }
                    if (one.StartsWith("###", StringComparison.Ordinal)) { inside = null; continue; }
                    if (inside == null) loose.Add(row); else held[inside].Add(row);
                }
                made.AddRange(Tight(loose));
                for (int at = 0; at < Banners.GetLength(0); at++)
                {
                    made.Add("");
                    made.Add(Banners[at, 0]);
                    foreach (string name in Grouping(at))
                    {
                        if (!held.ContainsKey(name)) continue;
                        made.Add("");
                        made.AddRange(Tight(held[name]));
                        held.Remove(name);
                    }
                }
                // Anything this build does not know about keeps its place at the end rather than being lost.
                foreach (string name in order)
                    if (held.ContainsKey(name)) { made.Add(""); made.AddRange(Tight(held[name])); }
                rows = made;

                // The pass puts the blank line in front of a section itself, so a file it has already been
                // over comes out the same. Writing only on a change keeps it off a file that is already right.
                string want = string.Join(Environment.NewLine, rows.ToArray()).TrimEnd() + Environment.NewLine;
                if (want == was) return;
                Notes.Put(Kept, want);
                lock (writing) { read = DateTime.MinValue; wasLong = -1; }
            }
            catch (Exception bad) { Notes.Failed("the grouping pass in SysLines.cs", bad); }
        }

        private static void Plainer()
        {
            try
            {
                if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept)) return;
                string had = File.ReadAllText(Kept), made = had;
                made = Regex.Replace(made, @"(?mi)^[ \t]*##[ \t]*Adult mode[ \t\r]*$", SenpaiHeading);
                // A label that only means something with the switch on, taken out of a file on the other side.
                // The switch alone: this deletes from disk, and going off air would not put them back.
                if (!Brain.Uncensored)
                    for (int line = 0; line < Stock.GetLength(0); line++)
                        if (SenpaiLabel(Stock[line, 0]))
                            made = Regex.Replace(made, @"(?mi)^[ \t]*" + Regex.Escape(Stock[line, 0]) + @"[ \t]*:[^\r\n]*\r?\n?", "");
                made = Regex.Replace(made, @"(?mi)^([ \t]*)" + Regex.Escape(WasSenpaiKey) + @"([ \t]*:)", "$1" + SenpaiKey + "$2");
                if (made == had) return;
                Notes.Put(Kept, made);
                lock (writing) { read = DateTime.MinValue; wasLong = -1; }
            }
            catch (Exception bad) { Notes.Failed("the plainer pass in SysLines.cs", bad); }
        }

        private static void Filed()
        {
            try
            {
                if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept)) return;
                Dictionary<string, string> belongs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int line = 0; line < Stock.GetLength(0); line++) belongs[Stock[line, 0]] = LabelHeading;
                for (int line = 0; line < Spoken.GetLength(0); line++) belongs[Spoken[line, 0]] = SpeechHeading;

                string had = File.ReadAllText(Kept);
                List<string> rows = Rows(had), kept = new List<string>();
                Dictionary<string, List<string>> moved = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                moved[LabelHeading] = new List<string>();
                moved[SpeechHeading] = new List<string>();
                string under = "";
                foreach (string row in rows)
                {
                    string one = (row ?? "").Trim();
                    if (one.StartsWith("#", StringComparison.Ordinal)) { under = one.TrimStart('#').Trim(); kept.Add(row); continue; }
                    string name = Named(one), wants;
                    if (name == null || !belongs.TryGetValue(name, out wants) || string.Equals(under, wants, StringComparison.OrdinalIgnoreCase))
                    {
                        kept.Add(row);
                        continue;
                    }
                    moved[wants].Add(row);
                }
                if (moved[LabelHeading].Count == 0 && moved[SpeechHeading].Count == 0) return;

                // Each one back under its heading, which is created where it is missing. In the shipped order:
                // what she says herself, then the labels.
                Refile(kept, SpeechHeading, SpeechNote, moved[SpeechHeading]);
                Refile(kept, LabelHeading, LabelNote, moved[LabelHeading]);
                while (kept.Count > 0 && kept[kept.Count - 1].Trim().Length == 0) kept.RemoveAt(kept.Count - 1);
                string made = string.Join(Environment.NewLine, kept.ToArray()) + Environment.NewLine;
                if (made == had) return;
                Notes.Put(Kept, made);
                lock (writing) { read = DateTime.MinValue; wasLong = -1; }
                Notes.Refresh(Kept, Text);
            }
            catch (Exception bad) { Notes.Failed("the filing pass in SysLines.cs", bad); }
        }

        // The name in front of the colon on a setting line, or null when the line is not one. A note line and a
        // blank line are not, and neither is a value holding a colon of its own, since only the first counts.
        private static string Named(string one)
        {
            if (one.Length == 0 || one.StartsWith(">", StringComparison.Ordinal) || one.StartsWith("#", StringComparison.Ordinal)) return null;
            int colon = one.IndexOf(':');
            if (colon <= 0) return null;
            return one.Substring(0, colon).Trim();
        }

        // Lines back under their heading, after its note and whatever is already there, or as a new section at
        // the end when the file has no such heading. SenpaiLast and DealLast run after this and hold the two
        // sections that have to stay at the bottom, so the end is a safe place to add one.
        private static void Refile(List<string> kept, string heading, string[] note, List<string> lines)
        {
            if (lines.Count == 0) return;
            int at = -1, after = -1;
            for (int i = 0; i < kept.Count; i++)
            {
                string one = (kept[i] ?? "").Trim();
                if (!one.StartsWith("#", StringComparison.Ordinal)) continue;
                if (at < 0 && string.Equals(one.TrimStart('#').Trim(), heading, StringComparison.OrdinalIgnoreCase)) { at = i; continue; }
                if (at >= 0) { after = i; break; }
            }
            if (at < 0)
            {
                kept.Add("");
                kept.Add("## " + heading);
                kept.Add("");
                kept.AddRange(note);
                kept.Add("");
                kept.AddRange(lines);
                return;
            }
            // The last filled line of that section, so they go under what is already there rather than above it.
            int end = after < 0 ? kept.Count : after;
            while (end > at + 1 && kept[end - 1].Trim().Length == 0) end--;
            kept.InsertRange(end, lines);
        }

        // That every name ends up under its own heading, against a file shaped like the one this was found on:
        // the hellos and five taglines stranded under her personality-lines switch, and one tagline sitting
        // with her real speech. Their edited values have to come across untouched.
        internal static void FileCheck(System.Collections.Generic.List<string> checks, string folder)
        {
            string was = Kept;
            try
            {
                Kept = Path.Combine(folder, "sys-filed.md");
                string[] stranded =
                {
                    "# Her settings", "",
                    "## How she comes and goes", "",
                    "> 1: her hello and goodbye come from her personality file. 0: from the lines above.", "",
                    "personality lines:    1", "",
                    "hello morning:        Good morning, mate.",
                    "goodbye:              Later, mate.", "",
                    "high five:            high five!!!",
                    "clang:                clang", "",
                    "## What she says herself", "",
                    "> Real speech, not labels. Offer several with \"|\" between them and she picks one.", "",
                    "broken:               There's something seriously broken with my code.",
                    "error:                error", "",
                    SenpaiHeading, "",
                    "confirm:              No",
                };
                Notes.Put(Kept, string.Join(Environment.NewLine, stranded) + Environment.NewLine);
                Filed();
                List<string> rows = Rows(File.ReadAllText(Kept));

                string under = "";
                Dictionary<string, string> sits = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                Dictionary<string, string> value = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string row in rows)
                {
                    string one = (row ?? "").Trim();
                    if (one.StartsWith("#", StringComparison.Ordinal)) { under = one.TrimStart('#').Trim(); continue; }
                    string name = Named(one);
                    if (name == null) continue;
                    sits[name] = under;
                    value[name] = one.Substring(one.IndexOf(':') + 1).Trim();
                }
                string[,] wanted =
                {
                    { "personality lines", "How she comes and goes" },
                    { "hello morning", SpeechHeading },
                    { "goodbye", SpeechHeading },
                    { "broken", SpeechHeading },
                    { "high five", LabelHeading },
                    { "clang", LabelHeading },
                    { "error", LabelHeading },
                    { SenpaiKey, SenpaiHeading.TrimStart('#').Trim() },
                };
                List<string> wrong = new List<string>();
                for (int at = 0; at < wanted.GetLength(0); at++)
                {
                    string name = wanted[at, 0], want = wanted[at, 1], got;
                    if (!sits.TryGetValue(name, out got)) { wrong.Add(name + " is gone from the file"); continue; }
                    if (!string.Equals(got, want, StringComparison.OrdinalIgnoreCase)) wrong.Add(name + " sits under \"" + got + "\" and belongs under \"" + want + "\"");
                }
                // Their words, not the stock ones.
                if (value["hello morning"] != "Good morning, mate.") wrong.Add("their hello was rewritten: " + value["hello morning"]);
                if (value["high five"] != "high five!!!") wrong.Add("their tagline was rewritten: " + value["high five"]);
                string text = File.ReadAllText(Kept);
                if (text.IndexOf("## " + LabelHeading, StringComparison.Ordinal) < 0) wrong.Add("the labels heading was never written");
                if (text.IndexOf(LabelNote[0], StringComparison.Ordinal) < 0) wrong.Add("the labels heading went in without its note");
                if (wrong.Count > 0) throw new Exception("The filing pass left names in the wrong place: " + string.Join(" | ", wrong.ToArray()));

                // Run twice: a file already filed is left exactly as it is.
                string once = File.ReadAllText(Kept);
                Filed();
                if (File.ReadAllText(Kept) != once) throw new Exception("The filing pass moved something on a file it had already filed.");
                // The grouping pass over a file it has already grouped. It once added a blank line in front of
                // every section each time, so the file grew a little on every start.
                Kept = Path.Combine(folder, "sys-grouped.md");
                string[] jumbled =
                {
                    "# Her settings", "",
                    "## Her calendar", "",
                    "calendar:             1", "",
                    "## What goes in front of a line", "",
                    "prompt:               say something", "",
                    "## Her start-up intro", "",
                    "intros:", "> wake", "",
                    "## Her bubble's colours", "",
                    "bubble:               black", "",
                    SenpaiHeading, "",
                    SenpaiKey + ":              No",
                };
                Notes.Put(Kept, string.Join(Environment.NewLine, jumbled) + Environment.NewLine);
                Grouped();
                string grouped = File.ReadAllText(Kept);
                if (grouped.IndexOf(Banners[0, 0], StringComparison.Ordinal) < 0) throw new Exception("The grouping pass wrote no banner.");
                if (grouped.IndexOf("## Her calendar", StringComparison.Ordinal) < grouped.IndexOf("## " + IntroHeading, StringComparison.Ordinal))
                    throw new Exception("The grouping pass left the calendar above her intro.");
                Grouped();
                if (File.ReadAllText(Kept) != grouped) throw new Exception("The grouping pass changed a file it had already grouped, so the file grows on every start.");
                Grouped();
                if (File.ReadAllText(Kept) != grouped) throw new Exception("The grouping pass changed a file it had already grouped twice over.");
                checks.Add("PASS the grouping pass puts a jumbled sys.md in its groups and leaves one it has already been over exactly as it is");
                Kept = Path.Combine(folder, "sys-filed.md");

                // Nothing in a file anybody may open says what the answer unlocks.
                string fresh2 = Text;
                foreach (string word in new[] { "Adult mode", "uncensored", "sexual", "grown-up", "fetish", "adults only" })
                    if (fresh2.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                        throw new Exception("A fresh sys.md says " + Voice.Quote(word) + ", which it must never say.");
                if (fresh2.IndexOf(SenpaiKey + ":", StringComparison.Ordinal) < 0) throw new Exception("A fresh sys.md has no " + SenpaiKey + " line.");
                if (fresh2.IndexOf(SenpaiKey + ":", StringComparison.Ordinal) < fresh2.IndexOf(SenpaiHeading, StringComparison.Ordinal))
                    throw new Exception("The question is above the terms it belongs to.");
                if (!Regex.IsMatch(fresh2, "(?mi)^[ \t]*" + Regex.Escape(SenpaiKey) + "[ \t]*:[ \t]*No[ \t\r]*$"))
                    throw new Exception("A fresh sys.md does not ship the answer as No.");
                checks.Add("PASS a fresh sys.md names nothing of what the answer unlocks, carries the question under the terms, and ships it as No");

                checks.Add("PASS every name in sys.md is filed under its own heading, with \"" + LabelHeading + "\" and its note written into a file that never had them, their edited values carried across word for word, and a file already filed left alone");
            }
            finally { Kept = was; }
        }

        // Her intro is the first section in the file. A sys.md that gained it later has it further down, so it
        // is moved up once, the same way Adult mode is held at the bottom.
        private static void IntroFirst()
        {
            if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept)) return;
            try
            {
                string[] rows = File.ReadAllText(Kept).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                int from = -1, to = rows.Length, first = -1;
                for (int row = 0; row < rows.Length; row++)
                {
                    string one = rows[row].Trim();
                    if (!one.StartsWith("##", StringComparison.Ordinal)) continue;
                    // A ############### group banner is not a section. Counting it as the first one put her
                    // intro above the banner instead of inside the group it belongs to.
                    if (one.StartsWith("###", StringComparison.Ordinal)) continue;
                    if (first < 0) first = row;
                    if (string.Equals(one.TrimStart('#').Trim(), IntroHeading, StringComparison.OrdinalIgnoreCase)) { from = row; continue; }
                    if (from >= 0) { to = row; break; }
                }
                if (from < 0 || first < 0) return;
                // A refresh once wrote her switch names into the note block, so any that sit above the key
                // line go: the ones under it are the real setting.
                int key = -1;
                for (int row = from; row < to; row++)
                    if (rows[row].Trim().StartsWith("intros:", StringComparison.OrdinalIgnoreCase)) { key = row; break; }
                bool trimmed = false;
                if (key > 0)
                    for (int row = from; row < key; row++)
                    {
                        string one = rows[row].Trim();
                        if (!one.StartsWith(">", StringComparison.Ordinal)) continue;
                        if (Known(one.TrimStart('>').Trim()) == null) continue;
                        rows[row] = null;
                        trimmed = true;
                    }
                if (from == first && !trimmed) return;
                List<string> block = new List<string>();
                for (int row = from; row < to; row++) if (rows[row] != null) block.Add(rows[row]);
                while (block.Count > 0 && block[block.Count - 1].Trim().Length == 0) block.RemoveAt(block.Count - 1);
                List<string> made = new List<string>();
                for (int row = 0; row < rows.Length; row++)
                {
                    if (row == first) { made.AddRange(block); made.Add(""); }
                    if (row >= from && row < to) continue;
                    if (rows[row] != null) made.Add(rows[row]);
                }
                Notes.Put(Kept, string.Join(Environment.NewLine, made.ToArray()));
                read = DateTime.MinValue;
                wasLong = -1;
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        private static void SenpaiLast()
        {
            try
            {
                if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept)) return;
                string had = File.ReadAllText(Kept);
                List<string> block, rows = Rows(had);
                string key;
                List<string> rest = WithoutSenpai(rows, out block, out key);
                if (key == null) return;
                if (block.Count == 0) { block.Add(SenpaiHeading); block.AddRange(SenpaiNote); }
                // A blank line under the heading, like every other section in the file has.
                if (block.Count > 1 && block[1].Length > 0) block.Insert(1, "");
                string made = string.Join(Environment.NewLine, rest.ToArray()).TrimEnd()
                            + Environment.NewLine + Environment.NewLine
                            + string.Join(Environment.NewLine, block.ToArray())
                            + Environment.NewLine + Environment.NewLine + key + Environment.NewLine;
                if (made == had) return;
                Notes.Put(Kept, made);
                lock (writing) { read = DateTime.MinValue; wasLong = -1; }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        private static bool IsDealHead(string row)
        {
            return Regex.IsMatch(row ?? "", @"^[ \t]*" + Regex.Escape(DealHeading) + @"[ \t]*$", RegexOptions.IgnoreCase);
        }

        // The terms go directly above Adult mode, which SenpaiLast then puts under them. Their last line says the
        // section below unlocks adult content, and on a file from an older build it did not: a top up appends at
        // the end, so the colours and the screensaver went in under them and the sentence pointed at the wrong one.
        //
        // Moved, not rewritten. Every line taken out goes back in, so a reworded copy of theirs is kept as theirs.
        private static void DealLast()
        {
            try
            {
                if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept)) return;
                string had = File.ReadAllText(Kept);
                List<string> block, rest = WithoutDeal(Rows(had), out block);
                if (block.Count == 0) return;
                if (block.Count > 1 && block[1].Length > 0) block.Insert(1, "");
                string made = string.Join(Environment.NewLine, rest.ToArray()).TrimEnd()
                            + Environment.NewLine + Environment.NewLine
                            + string.Join(Environment.NewLine, block.ToArray()) + Environment.NewLine;
                if (made == had) return;
                Notes.Put(Kept, made);
                lock (writing) { read = DateTime.MinValue; wasLong = -1; }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // The terms block pulled out of the file: its heading and the note under it. What comes back is the rest,
        // and the block through "block". Two copies of the heading come out as one block rather than being thinned,
        // since nothing here decides what to drop.
        private static List<string> WithoutDeal(List<string> rows, out List<string> block)
        {
            block = new List<string>();
            List<string> rest = new List<string>();
            for (int at = 0; at < rows.Count; at++)
            {
                string row = rows[at];
                if (!IsDealHead(row)) { rest.Add(row); continue; }
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

        // A file written by an older build has none of the names added since, and "write only if missing" means it never
        // gets them: the setting is in the code, documented, and absent from the one file that is meant to expose it. So
        // every name the build knows is appended to a file that lacks it, with their own edits left exactly as they are.
        internal static void TopUp()
        {
            if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept)) return;
            IntroTopUp();
            try
            {
                Fresh();
                List<string> adding = new List<string>();
                lock (writing)
                {
                    if (!theirs.ContainsKey("sys prompt") || !theirs.ContainsKey("you prompt"))
                    {
                        adding.Add("");
                        adding.Add("## What goes in front of a line");
                        adding.Add("");
                        adding.Add("> Change these to anything. A trailing space is added for you.");
                        adding.Add("");
                        if (!theirs.ContainsKey("sys prompt")) adding.Add(Line("sys prompt", SysMark));
                        if (!theirs.ContainsKey("you prompt")) adding.Add(Line("you prompt", YouMark));
                    }
                    List<string> words = new List<string>();
                    for (int line = 0; line < Spoken.GetLength(0); line++)
                        if (!theirs.ContainsKey(Spoken[line, 0])) words.Add(Line(Spoken[line, 0], Spoken[line, 1]));
                    if (words.Count > 0)
                    {
                        adding.Add("");
                        adding.Add("## What she says herself");
                        adding.Add("");
                        adding.Add("> Real speech, not labels. Offer several with \"|\" between them and she picks one.");
                        adding.Add("");
                        adding.AddRange(words);
                    }
                    if (!theirs.ContainsKey(ExcludedKey))
                    {
                        adding.Add("");
                        adding.Add("## Never watch these");
                        adding.Add("");
                        adding.Add("> Programs Watch my screen skips. Comma separated, and a partial name is enough:");
                        adding.Add("> \"brave\" covers brave.exe. Matched on the program and on its window title.");
                        adding.Add("> She says nothing at all while one of these is in front.");
                        adding.Add("");
                        adding.Add(Line(ExcludedKey, ""));
                    }
                    if (!theirs.ContainsKey(CalendarKey))
                    {
                        adding.Add("");
                        adding.Add("## Her calendar");
                        adding.Add("");
                        adding.Add("> The app she opens to read your calendar. Blank and she uses the one you open most.");
                        adding.Add("> She opens it and reads the screen. Nothing signs in and nothing is sent anywhere.");
                        adding.Add("");
                        adding.Add(Line(CalendarKey, ""));
                    }
                    // A file from the two builds where that section was headed "The deal" loses it, so the one
                    // below replaces it rather than sitting beside it. It was notes only and never held a
                    // setting, so nothing of theirs can be inside it.
                    Unheaded("## The deal");
                    // The terms, for a sys.md written before they existed. Added above Adult mode, which SenpaiLast
                    // puts back at the bottom whatever else goes in, so they end up last but one.
                    if (!Regex.IsMatch(File.ReadAllText(Kept), @"(?mi)^[ \t]*" + Regex.Escape(DealHeading) + @"[ \t\r]*$"))
                    {
                        adding.Add("");
                        foreach (string row in DealLines()) adding.Add(row);
                    }
                    // The intro list, for a sys.md written before it existed. Added above Adult mode, which
                    // SenpaiLast puts back at the bottom whatever else goes in.
                    if (!Regex.IsMatch(File.ReadAllText(Kept), @"(?mi)^[ \t]*##[ \t]*" + Regex.Escape(IntroHeading) + @"[ \t\r]*$"))
                    {
                        adding.Add("");
                        foreach (string row in IntroBlock().Replace("\r\n", "\n").TrimEnd('\n').Split('\n')) adding.Add(row);
                    }
                    // Her screensaver, for a sys.md written before it existed. A file that has the switch but
                    // none of the rest is from the build where it had three lines: that whole section goes and
                    // the full one replaces it, so their switch is the only thing read back out of it.
                    bool saverShort = false;
                    for (int one = 0; one < SaverStock.GetLength(0); one++)
                        if (!theirs.ContainsKey(SaverStock[one, 0])) saverShort = true;
                    // The note itself, not only the settings under it. A file with every line in it is never
                    // rebuilt, so a note reworded later reached new installs and nobody else: one written before
                    // the spacebar went on it kept telling them to move the mouse for the rest of its life. Any
                    // note line this build ships that the file does not have is the test, so this holds for the
                    // next rewording as well. Their values are read out and put back either way.
                    bool saverNoteOld = false;
                    if (!saverShort)
                    {
                        string whole = File.ReadAllText(Kept);
                        if (Regex.IsMatch(whole, @"(?mi)^[ \t]*" + Regex.Escape(SaverHeading) + @"[ \t\r]*$"))
                            foreach (string row in SaverNotes())
                                if (row.StartsWith(">", StringComparison.Ordinal) && row.Trim().Length > 2 && !whole.Contains(row))
                                { saverNoteOld = true; break; }
                    }
                    if (saverShort || saverNoteOld)
                    {
                        // Read before the old section is taken out, since taking it out is what loses it.
                        Dictionary<string, string> kept = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        bool any = false;
                        for (int one = 0; one < SaverStock.GetLength(0); one++)
                        {
                            string said;
                            if (!theirs.TryGetValue(SaverStock[one, 0], out said)) continue;
                            kept[SaverStock[one, 0]] = said;
                            any = true;
                        }
                        if (any) Unheaded(SaverHeading);
                        adding.Add("");
                        adding.AddRange(SaverLines(kept));
                    }
                    if (!theirs.ContainsKey(OwnLinesKey))
                    {
                        adding.Add("");
                        adding.Add("## How she comes and goes");
                        adding.Add("");
                        adding.Add("> 1: her hello and goodbye come from her personality file. 0: from the lines above.");
                        adding.Add("> Her error line is never affected either way.");
                        adding.Add("");
                        adding.Add(Line(OwnLinesKey, "1"));
                    }
                    // With their heading, as the speech block above does. Bare lines were the fault Filed cleans
                    // up: Fitted puts a line with no heading after the last filled line of whatever section it
                    // lands in, so a tagline added by a later build was filed under her hellos.
                    List<string> tags = new List<string>();
                    for (int line = 0; line < Stock.GetLength(0); line++)
                        // The switch alone, like the pass that takes them out again.
                        if (!theirs.ContainsKey(Stock[line, 0]) && (Brain.Uncensored || !SenpaiLabel(Stock[line, 0])))
                            tags.Add(Line(Stock[line, 0], Stock[line, 1]));
                    if (tags.Count > 0)
                    {
                        adding.Add("");
                        adding.Add("## " + LabelHeading);
                        adding.Add("");
                        adding.AddRange(LabelNote);
                        adding.Add("");
                        adding.AddRange(tags);
                    }
                }
                if (adding.Count == 0) { Painted(); Plainer(); Filed(); Grouped(); Empties(); Tidy(); IntroFirst(); DealLast(); SenpaiLast(); return; }
                Notes.Put(Kept, Fitted(File.ReadAllText(Kept), adding));
                read = DateTime.MinValue;
                wasLong = -1;
                Notes.Refresh(Kept, Text);
                Painted();
                Plainer();
                Filed();
                Grouped();
                Empties();
                Tidy();
                IntroFirst();
                DealLast();
                SenpaiLast();
            }
            catch (Exception bad) { Notes.Failed("a top up in SysLines.cs", bad); }
        }

        // A name added by a later build goes under the heading it belongs to, when the file already has that
        // heading. Appending a second copy reads the same to her, but it leaves sys.md with two sections of the
        // same name and the settings for one thing in two places, which is exactly what makes a file unreadable.
        // Takes one whole section out of her file, heading and all, up to the next heading. For a section that
        // was renamed: nothing here is theirs to keep, or it would not be safe to cut.
        private static void Unheaded(string heading)
        {
            try
            {
                if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept)) return;
                string had = File.ReadAllText(Kept);
                if (had.IndexOf(heading, StringComparison.OrdinalIgnoreCase) < 0) return;
                List<string> made = new List<string>();
                bool cutting = false;
                foreach (string raw in had.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                {
                    string one = (raw ?? "").Trim();
                    if (one.StartsWith("#", StringComparison.Ordinal))
                        cutting = string.Equals(one, heading, StringComparison.OrdinalIgnoreCase);
                    if (!cutting) made.Add(raw);
                }
                while (made.Count > 0 && made[made.Count - 1].Trim().Length == 0) made.RemoveAt(made.Count - 1);
                Notes.Put(Kept, string.Join(Environment.NewLine, made.ToArray()) + Environment.NewLine);
                read = DateTime.MinValue;
                wasLong = -1;
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        private static string Fitted(string body, List<string> adding)
        {
            List<string> heads = new List<string>();
            Dictionary<string, List<string>> want = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<string>> whole = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            string under = "";
            heads.Add(under);
            want[under] = new List<string>();
            whole[under] = new List<string>();
            foreach (string row in adding)
            {
                string one = (row ?? "").Trim();
                if (one.StartsWith("##", StringComparison.Ordinal))
                {
                    under = one.TrimStart('#').Trim();
                    if (!want.ContainsKey(under)) { heads.Add(under); want[under] = new List<string>(); whole[under] = new List<string>(); }
                }
                whole[under].Add(row);
                if (one.Length > 0 && !one.StartsWith("#", StringComparison.Ordinal) && !one.StartsWith(">", StringComparison.Ordinal))
                    want[under].Add(row);
            }

            // Walk their file, and each heading we have something for takes its keys straight after its last filled line.
            string[] rows = body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            List<string> made = new List<string>();
            List<string> loose = want[""];
            string at = null;
            int after = 0;
            foreach (string row in rows)
            {
                string one = (row ?? "").Trim();
                if (one.StartsWith("#", StringComparison.Ordinal))
                {
                    after = Put(made, at, after, want, heads);
                    at = one.TrimStart('#').Trim();
                    made.Add(row);
                    after = made.Count;
                    continue;
                }
                made.Add(row);
                if (one.Length > 0) after = made.Count;
            }
            after = Put(made, at, after, want, heads);
            while (made.Count > 0 && made[made.Count - 1].Trim().Length == 0) made.RemoveAt(made.Count - 1);

            // Anything left has no heading in their file yet, so it keeps its own, notes and all. Asked by
            // whether the heading is there rather than by whether the section has keys: a section that is all
            // notes has none to place, and was dropped for having none. The terms are exactly that section.
            foreach (string name in heads)
            {
                if (name.Length == 0) continue;
                bool there = false;
                foreach (string row in made)
                {
                    string one = (row ?? "").Trim();
                    if (!one.StartsWith("#", StringComparison.Ordinal)) continue;
                    if (string.Equals(one.TrimStart('#').Trim(), name, StringComparison.OrdinalIgnoreCase)) { there = true; break; }
                }
                if (there) continue;
                made.Add("");
                made.AddRange(whole[name]);
            }
            if (loose.Count > 0 && want.ContainsKey("")) { made.Add(""); made.AddRange(loose); }
            return string.Join(Environment.NewLine, made.ToArray()) + Environment.NewLine;
        }

        private static int Put(List<string> made, string at, int after, Dictionary<string, List<string>> want, List<string> heads)
        {
            List<string> rows;
            if (at == null || !want.TryGetValue(at, out rows) || rows.Count == 0) return after;
            want[at] = new List<string>();
            made.InsertRange(after, rows);
            return after + rows.Count;
        }

        // One heading per name. Earlier builds appended a second copy whenever a name was added, so a file that has
        // been through a few of those carries empty duplicates. Only a heading with nothing under it goes.
        private static void Empties()
        {
            if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept)) return;
            try
            {
                string[] rows = File.ReadAllText(Kept).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

                // Which block each line belongs to, how many keys each block holds, and how many each name holds
                // across the whole file. A block with no keys whose name has keys somewhere else is the spare one.
                int[] block = new int[rows.Length];
                List<string> named = new List<string>();
                List<int> held = new List<int>();
                int now = -1;
                for (int at = 0; at < rows.Length; at++)
                {
                    string one = (rows[at] ?? "").Trim();
                    if (one.StartsWith("##", StringComparison.Ordinal))
                    {
                        named.Add(one.TrimStart('#').Trim());
                        held.Add(0);
                        now = named.Count - 1;
                    }
                    block[at] = now;
                    if (now < 0 || one.Length == 0) continue;
                    if (one.StartsWith("#", StringComparison.Ordinal) || one.StartsWith(">", StringComparison.Ordinal)) continue;
                    held[now] = held[now] + 1;
                }
                Dictionary<string, int> all = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int one = 0; one < named.Count; one++)
                {
                    int sum;
                    all[named[one]] = all.TryGetValue(named[one], out sum) ? sum + held[one] : held[one];
                }
                bool[] goes = new bool[named.Count];
                bool cut = false;
                for (int one = 0; one < named.Count; one++)
                    if (held[one] == 0 && all[named[one]] > 0) { goes[one] = true; cut = true; }
                if (!cut) return;
                List<string> made = new List<string>();
                for (int at = 0; at < rows.Length; at++)
                    if (block[at] < 0 || !goes[block[at]]) made.Add(rows[at]);
                Notes.Put(Kept, string.Join(Environment.NewLine, made.ToArray()));
                read = DateTime.MinValue;
                wasLong = -1;
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
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

        // Her start-up intro. The names sit under the key with a ">" in front, the way a interest does: take the
        // ">" off one to switch it on, and with more than one on she picks at random. Adult mode still goes
        // after this, because Adult mode is always the last thing in the file.
        internal const string IntroHeading = "Her start-up intro";
        internal static readonly string[] IntroNames = { "shatter", "hijack", "wake", "door" };

        internal static string IntroBlock()
        {
            StringBuilder made = new StringBuilder();
            made.AppendLine("## " + IntroHeading);
            made.AppendLine();
            made.AppendLine("> How she turns up. Take the \">\" off one to use it, more than one and she picks.");
            made.AppendLine("> Needs the Startup Intros mod in mods, approved once under Mainframe > Mods.");
            made.AppendLine();
            made.AppendLine("intros:");
            foreach (string one in IntroNames) made.AppendLine("> " + one);
            made.AppendLine();
            return made.ToString();
        }

        // The intros switched on, in the order they are written. Empty means she starts the way she always has.
        internal static string[] Intros()
        {
            List<string> on = new List<string>();
            try
            {
                if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept)) return on.ToArray();
                bool inside = false;
                foreach (string raw in File.ReadAllLines(Kept))
                {
                    string one = (raw ?? "").Trim();
                    if (one.StartsWith("#", StringComparison.Ordinal))
                    {
                        inside = string.Equals(one.TrimStart('#').Trim(), IntroHeading, StringComparison.OrdinalIgnoreCase);
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
            return on.ToArray();
        }

        // A name added to the intro list later, for a sys.md that already has the section: put in under the last
        // one, switched off, so it is there to take the ">" off.
        private static void IntroTopUp()
        {
            try
            {
                string[] rows = File.ReadAllText(Kept).Replace("\r\n", "\n").Split('\n');
                int start = -1, last = -1;
                List<string> seen = new List<string>();
                for (int at = 0; at < rows.Length; at++)
                {
                    string one = rows[at].Trim();
                    if (one.StartsWith("#", StringComparison.Ordinal))
                    {
                        if (start >= 0) break;
                        if (string.Equals(one.TrimStart('#').Trim(), IntroHeading, StringComparison.OrdinalIgnoreCase)) start = at;
                        continue;
                    }
                    if (start < 0) continue;
                    string bare = one.TrimStart('>').Trim();
                    if (bare.StartsWith("intros:", StringComparison.OrdinalIgnoreCase))
                    {
                        last = at;
                        foreach (string part in bare.Substring(7).Split(',')) if (Known(part.Trim()) != null) seen.Add(Known(part.Trim()));
                        continue;
                    }
                    if (Known(bare) != null) { seen.Add(Known(bare)); last = at; }
                }
                if (start < 0 || last < 0) return;
                List<string> missing = new List<string>();
                foreach (string one in IntroNames) if (!seen.Contains(one)) missing.Add("> " + one);
                if (missing.Count == 0) return;
                List<string> made = new List<string>(rows);
                made.InsertRange(last + 1, missing);
                Notes.Put(Kept, string.Join(Environment.NewLine, made.ToArray()));
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
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
                made.AppendLine("> The small grey labels she shows after \"> sys: \".");
                made.AppendLine("> Change the text on the right. Leave one blank to use hers.");
                made.AppendLine("> Names on the left are hers, so keep them as they are.");
                made.AppendLine("> Delete this file to put everything back.");
                made.AppendLine();
                made.AppendLine();
                made.AppendLine("############### app settings");
                made.Append(IntroBlock());
                made.AppendLine("## Never watch these");
                made.AppendLine();
                made.AppendLine("> Programs Watch my screen skips. Comma separated, and a partial name is enough:");
                made.AppendLine("> \"brave\" covers brave.exe. Matched on the program and on its window title.");
                made.AppendLine("> She says nothing at all while one of these is in front.");
                made.AppendLine();
                made.AppendLine(Line(ExcludedKey, ""));
                made.AppendLine();
                made.AppendLine("## Her calendar");
                made.AppendLine();
                made.AppendLine("> The app she opens to read your calendar. Blank and she uses the one you open most.");
                made.AppendLine("> She opens it and reads the screen. Nothing signs in and nothing is sent anywhere.");
                made.AppendLine();
                made.AppendLine(Line(CalendarKey, ""));
                made.AppendLine();
                made.AppendLine();
                made.AppendLine("############### convo settings");
                made.AppendLine("## What goes in front of a line");
                made.AppendLine();
                made.AppendLine("> Change these to anything. A trailing space is added for you.");
                made.AppendLine();
                made.AppendLine(Line("sys prompt", SysMark));
                made.AppendLine(Line("you prompt", YouMark));
                made.AppendLine();
                made.AppendLine("## What she says herself");
                made.AppendLine();
                made.AppendLine("> Real speech, not labels. Offer several with \"|\" between them and she picks one.");
                made.AppendLine();
                for (int line = 0; line < Spoken.GetLength(0); line++)
                    made.AppendLine(Line(Spoken[line, 0], Spoken[line, 1]));
                made.AppendLine();
                made.AppendLine("## How she comes and goes");
                made.AppendLine();
                made.AppendLine("> 1: her hello and goodbye come from her personality file. 0: from the lines above.");
                made.AppendLine("> Her error line is never affected either way.");
                made.AppendLine();
                made.AppendLine(Line(OwnLinesKey, "1"));
                made.AppendLine();
                made.AppendLine("## The labels after it");
                made.AppendLine();
                made.AppendLine("> Taglines, not speech. They name what just happened.");
                made.AppendLine();
                for (int line = 0; line < Stock.GetLength(0); line++)
                {
                    if (!Brain.Uncensored && SenpaiLabel(Stock[line, 0])) continue;
                    made.AppendLine(Line(Stock[line, 0], Stock[line, 1]));
                }
                made.AppendLine();
                made.AppendLine("> What she says when handled lives in the personalities folder.");
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
