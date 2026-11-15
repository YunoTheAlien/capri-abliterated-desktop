using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

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
        internal static string Kept;

        private static readonly object writing = new object();
        private static readonly Dictionary<string, string> theirs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static DateTime read = DateTime.MinValue;
        private static long wasLong = -1;

        // The author's labels, in the order Said.Labels registers them. The left side is the name used in code and must
        // not change; the right side is only what is shown.
        internal static readonly string[,] Stock =
        {
            { "startup", "startup" },
            { "pets you", "pets you" },
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
            { "distracted", "distracted" }
        };

        // Lines she actually says, as opposed to the labels above. Several can be offered at once, split on "|", and she
        // takes one at random. Her hellos were picked by the hour out of three fixed strings, so the first thing she ever
        // said was the one thing nobody could rewrite.
        internal static readonly string[,] Spoken =
        {
            { "hello morning", "Good morning, choom." },
            { "hello late", "Isn't it late, choom?" },
            { "hello day", "Miss me, choom?" }
        };

        // The two prefixes her bubble puts in front of a line. Kept as their own keys so "> sys: " can become "Capri: "
        // or anything else, without touching the marker the code uses to know a line is a system one.
        internal const string SysMark = "> sys: ";
        internal const string YouMark = "> you: ";

        internal static WrittenNote Open() { Write(false); return new WrittenNote(Kept, Text, writing); }

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
            catch (Exception) { }
        }

        // A file written by an older build has none of the names added since, and "write only if missing" means it never
        // gets them: the setting is in the code, documented, and absent from the one file that is meant to expose it. So
        // every name the build knows is appended to a file that lacks it, with their own edits left exactly as they are.
        internal static void TopUp()
        {
            if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept)) return;
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
                    List<string> tags = new List<string>();
                    for (int line = 0; line < Stock.GetLength(0); line++)
                        if (!theirs.ContainsKey(Stock[line, 0])) tags.Add(Line(Stock[line, 0], Stock[line, 1]));
                    if (tags.Count > 0) { adding.Add(""); adding.AddRange(tags); }
                }
                if (adding.Count == 0) return;
                string had = File.ReadAllText(Kept);
                if (had.Length > 0 && !had.EndsWith("\n")) had += Environment.NewLine;
                File.WriteAllText(Kept, had + string.Join(Environment.NewLine, adding.ToArray()) + Environment.NewLine, new UTF8Encoding(false));
                read = DateTime.MinValue;
                wasLong = -1;
                Notes.Refresh(Kept, Text);
            }
            catch (Exception) { }
        }

        internal static void Write(bool over)
        {
            if (string.IsNullOrEmpty(Kept)) return;
            try
            {
                if (!over && File.Exists(Kept)) { Notes.Refresh(Kept, Text); return; }
                string folder = Path.GetDirectoryName(Kept);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) Directory.CreateDirectory(folder);
                File.WriteAllText(Kept, Text, new UTF8Encoding(false));
            }
            catch (Exception) { }
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
                made.AppendLine("## The labels after it");
                made.AppendLine();
                made.AppendLine("> Taglines, not speech. They name what just happened.");
                made.AppendLine();
                for (int line = 0; line < Stock.GetLength(0); line++)
                    made.AppendLine(Line(Stock[line, 0], Stock[line, 1]));
                made.AppendLine();
                made.AppendLine("> What she says when handled lives in the personalities folder.");
                return made.ToString();
            }
        }
    }
}
