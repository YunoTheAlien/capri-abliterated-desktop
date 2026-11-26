using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // commands.md: what she understands without her brain, written fresh at each start from the code, so editing it
    // changes nothing. Only commands Hands.Direct reads are listed.
    internal static class Commands
    {
        internal const string FileName = "commands.md";
        internal static string Kept;
        private static readonly object writing = new object();

        internal static WrittenNote Open() { return new WrittenNote(Kept, Text, writing); }

        internal static void Write()
        {
            if (Kept == null) return;
            try
            {
                string folder = Path.GetDirectoryName(Kept);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) Directory.CreateDirectory(folder);
                File.WriteAllText(Kept, Text, new UTF8Encoding(false));
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // Her editor is a plain text box in a monospace font, so the columns are padded with spaces rather than written
        // as a markdown table: pipes would just show as pipes and the rows would not line up.
        private const int Column = 34;

        // The command stays in English word for word, since her instant path reads English only. In another language it
        // reads like a setting: what it does on a ">!=" line above it, translated, and the command under it.
        private static string Line(string said, string does)
        {
            if (Brain.Language != null) return Notes.SettingMark + " " + Notes.InLanguage(does) + Environment.NewLine + "  " + (said ?? "");
            string left = "  " + (said ?? "");
            if (left.Length >= Column) return left + "  " + does;
            return left + new string(' ', Column - left.Length) + does;
        }

        // A note, in their language when one is picked.
        private static string Note(string words) { return Notes.NoteMark + " " + Notes.InLanguage(words); }

        // commands.md and twitch.md in another language: every command and example stays in English word for word, in its
        // column or with its indent; only what each does and the notes change.
#if CHECKS
        internal static void LanguageCheck(System.Collections.Generic.List<string> checks, string folder)
        {
            Func<string, string> translateWas = Notes.Translate, menuWas = Notes.MenuName;
            string languageWas = Brain.Language;
            try
            {
                string english = Text;
                Brain.Language = "Polish";
                Notes.Translate = delegate(string line) { return "[pl] " + line; };
                Notes.MenuName = delegate(string name) { return "<" + name + ">"; };
                string[] was = english.Replace("\r\n", "\n").Split('\n'), made = Text.Replace("\r\n", "\n").Split('\n');
                System.Collections.Generic.List<string> rows = new System.Collections.Generic.List<string>(made);
                int told = rows.FindIndex(row => row.StartsWith(Notes.NoteMark + " [pl] Say these in English", StringComparison.Ordinal));
                if (told < 0) throw new Exception("commands.md in another language doesn't say its commands are said in English.");
                rows.RemoveAt(told);
                // Each command, as a setting: its ">!=" line in their language right above it, then the command alone.
                int commands = 0, at2 = 0;
                for (int at = 0; at < was.Length; at++)
                {
                    if (at2 >= rows.Count) throw new Exception("commands.md in another language lost lines.");
                    if (was[at].StartsWith("  ", StringComparison.Ordinal))
                    {
                        string command = Regex.Match(was[at], @"^  (.+?)(?:\s{2,}|$)").Groups[1].Value;
                        if (!rows[at2].StartsWith(Notes.SettingMark + " [pl] ", StringComparison.Ordinal) || rows[at2 + 1] != "  " + command)
                            throw new Exception("A command in another language isn't its line in their language over the English command: " + rows[at2] + " / " + rows[at2 + 1]);
                        commands++;
                        at2 += 2;
                        continue;
                    }
                    if (was[at].StartsWith(Notes.NoteMark + " ", StringComparison.Ordinal))
                    {
                        if (!rows[at2].StartsWith(Notes.NoteMark + " [pl] ", StringComparison.Ordinal)) throw new Exception("A commands.md note wasn't in their language: " + rows[at2]);
                    }
                    else if (rows[at2] != was[at]) throw new Exception("A commands.md line changed in another language: " + was[at]);
                    at2++;
                }
                if (at2 != rows.Count || commands < 20) throw new Exception("commands.md in another language gained lines, or has too few commands: " + commands);
                checks.Add("PASS commands.md in another language reads each command like a setting: what it does on a \">!=\" line in their language above it, and the command under it in English word for word; a note says why");

                // twitch.md: a notes refresh in their language.
                string path = System.IO.Path.Combine(folder, "twitch-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".md");
                string stock = TwitchRules.Default;
                System.IO.File.WriteAllText(path, stock, new UTF8Encoding(false));
                TwitchRules.NotesInLanguage(path);
                string[] withLines = System.IO.File.ReadAllText(path).Replace("\r\n", "\n").Split('\n');
                // Each command example has what it does above it, in their language, like a setting.
                int explained = 0;
                for (int at = 1; at < withLines.Length; at++)
                    if (withLines[at].StartsWith(Notes.NoteMark, StringComparison.Ordinal) && Notes.Untranslated(withLines[at].Substring(Notes.NoteMark.Length)))
                    {
                        if (!withLines[at - 1].StartsWith(Notes.SettingMark + " [pl] ", StringComparison.Ordinal)) throw new Exception("A twitch.md example has no line in their language above it: " + withLines[at]);
                        explained++;
                    }
                string[] stockRows = stock.Replace("\r\n", "\n").Split('\n'), done = Array.FindAll(withLines, row => !row.StartsWith(Notes.SettingMark, StringComparison.Ordinal));
                if (explained < 3) throw new Exception("twitch.md's command examples weren't explained in their language: " + explained);
                int examples = 0;
                for (int at = 0; at < Math.Min(stockRows.Length, done.Length); at++)
                {
                    string one = stockRows[at];
                    bool example = one.StartsWith(Notes.NoteMark, StringComparison.Ordinal) && Notes.Untranslated(one.Substring(Notes.NoteMark.Length));
                    bool note = one.StartsWith(Notes.NoteMark + " ", StringComparison.Ordinal) && !example;
                    if (example) examples++;
                    if (!note && done[at] != one) throw new Exception("twitch.md in another language changed a line that isn't note words: \"" + one + "\" became \"" + done[at] + "\"");
                }
                if (stockRows.Length != done.Length || examples < 3 || Array.IndexOf(stockRows, ">") >= 0) throw new Exception("twitch.md in another language lost lines, or its examples or \">!\" spacers are missing.");
                if (!Array.Exists(done, row => row.Contains("<Twitch> > <Read chat>"))) throw new Exception("twitch.md's menu names don't read as her menu shows them.");
                // Back in English the lines above the examples go again.
                Brain.Language = null;
                TwitchRules.NotesInLanguage(path);
                if (Array.Exists(System.IO.File.ReadAllText(path).Replace("\r\n", "\n").Split('\n'), row => row.StartsWith(Notes.SettingMark, StringComparison.Ordinal)))
                    throw new Exception("twitch.md back in English kept its lines in another language above the examples.");
                checks.Add("PASS twitch.md in another language keeps every heading, setting, list, \">!\" spacer and indented command example byte for byte, each example with what it does above it on a \">!=\" line in their language and gone again in English, with its notes in their language and its menu names as her menu shows them");
                try { System.IO.File.Delete(path); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
            finally { Notes.Translate = translateWas; Notes.MenuName = menuWas; Brain.Language = languageWas; }
        }
#endif

        internal static string Text
        {
            get
            {
                string[] lines =
                {
                    "# Capri commands",
                    "",
                    Note("Read straight from your words, no AI. Works every time, brain or not."),
                    Note("Rewritten each start. Editing this does nothing."),
                    // With a language picked, why the commands below are English.
                    Brain.Language == null ? null : Note("Say these in English. In your own language, her brain still understands you."),
                    "",
                    "## Apps",
                    "",
                    Line("open discord", "opens it"),
                    Line("close discord", "closes it. also quit, exit, kill"),
                    Line("switch to brave", "brings it up. also go to, show me"),
                    Line("minimise notepad", "minimises"),
                    Line("snap notepad left", "snap, move, centre, other screen"),
                    "",
                    "## Typing",
                    "",
                    Line("type test in notepad", "types it"),
                    Line("control discord and type hi", "same"),
                    Line("use discord to say hi", "same"),
                    Line("say hi in discord", "same"),
                    Line("notepad type test", "same"),
                    "",
                    Note("App must be installed or open. Misheard names and \"notepad plus plus\" still find it."),
                    "",
                    "## Web",
                    "",
                    Line("open yahoo", "opens the site"),
                    Line("on brave open wikipedia", "that site, that browser"),
                    Line("open wikipedia in brave", "same"),
                    Line("on brave open silksong", "not a site, so searches it"),
                    Line("look up weather tomorrow", "searches, your browser's engine"),
                    Line("google weather tomorrow", "searches Google"),
                    Line("search youtube for lofi", "searches that site"),
                    "",
                    Note("Apps win over sites, unless you name a browser."),
                    "",
                    "## Playlist",
                    "",
                    Line(@"playlist add D:\Music\x", "adds a folder. type it"),
                    Line("play my playlist", "starts"),
                    Line("skip / next song", "next"),
                    Line("previous song", "back one"),
                    Line("shuffle / shuffle off", "shuffle"),
                    Line("repeat one / repeat all / repeat off", "looping"),
                    Line("remove song / delete song", "off the list, not off your drive"),
                    Line("stop the playlist", "stops"),
                    "",
                    Note("mp3, m4a, wma, wav, flac. skip and previous only take over while it is playing."),
                    "",
                    "## Twitch",
                    "",
                    Line("who's live", "checks your watch list now"),
                    Line("turn off twitch live notifications", "stops the live mentions"),
                    "",
                    Note("Chat reading is in her right-click menu. Settings are in twitch.md."),
                    "",
                    "## Other",
                    "",
                    Line("again / do it again", "repeats your last command"),
                    Line("repeat song / repeat playlist", "not the same thing, that is the playlist"),
                    Line("volume up / mute / pause", "media keys"),
                    Line("clear memory", "forgets this chat. asks once first"),
                    Line("show commands", "opens this"),
                    ""
                };
                return string.Join(Environment.NewLine, Array.FindAll(lines, line => line != null)) + Environment.NewLine;
            }
        }
    }
}
