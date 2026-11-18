using System;
using System.IO;
using System.Text;

namespace CapriDesktop
{
    // commands.md: what she understands without her brain being involved at all.
    //
    // Written fresh every time she starts, so it cannot drift from the code the way a hand-kept list would. That is also
    // why it says plainly at the top that editing it changes nothing: it is a read-out, not a setting. Only the commands
    // read by Hands.Direct are listed, because those are the ones that work every time; anything her brain has to choose
    // a tool for is not a command in this sense and is left out on purpose.
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

        private static string Line(string said, string does)
        {
            string left = "  " + (said ?? "");
            if (left.Length >= Column) return left + "  " + does;
            return left + new string(' ', Column - left.Length) + does;
        }

        internal static string Text
        {
            get
            {
                string[] lines =
                {
                    "# Capri commands",
                    "",
                    "> Read straight from your words, no AI. Works every time, brain or not.",
                    "> Rewritten each start. Editing this does nothing.",
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
                    "> App must be installed or open. Misheard names and \"notepad plus plus\" still find it.",
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
                    "> Apps win over sites, unless you name a browser.",
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
                    "> mp3, m4a, wma, wav, flac. skip and previous only take over while it is playing.",
                    "",
                    "## Twitch",
                    "",
                    Line("who's live", "checks your watch list now"),
                    Line("turn off twitch live notifications", "stops the live mentions"),
                    "",
                    "> Chat reading is in her right-click menu. Settings are in twitch.md.",
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
                return string.Join(Environment.NewLine, lines) + Environment.NewLine;
            }
        }
    }
}
