using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // Keeps the "> " notes at the top of her editable files matching the build, without touching a single line the
    // person has written.
    //
    // Her files are written once and then left alone, so a file made by an older build keeps that build's notes for
    // ever. Those notes are instructions, so stale ones describe settings that have moved or no longer exist.
    internal static class Notes
    {
        // Replaces every "> " note in the file with the ones the build ships, section by section. Anything that is
        // not a note is left exactly as it was, in its original order.

        internal const string ResetKey = "reset to stock";

        private static readonly Dictionary<string, string> stocked = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static void Seen(string path)
        {
            try
            {
                FileInfo about = new FileInfo(path);
                if (!about.Exists) return;
                lock (stocked) stocked[path] = about.LastWriteTimeUtc.Ticks + ":" + about.Length;
            }
            catch (Exception) { }
        }

        // A way back to the file we ship, without anyone copying anything. Every one of her files carries a line
        // near the top; set it to 1 and the file is written fresh next time she reads it, with the line back at 0.
        // Done here rather than in each file's own code so they all behave the same way and none gets forgotten.
        internal static bool Stock(string path, string fresh)
        {
            return Stock(path, delegate { return fresh; });
        }

        // The stock text is asked for only once the file is known to have changed. Building it is not free:
        // it is a whole personality pack out of the table, and handing it in as a string meant building one
        // for every file on every pass and throwing nearly all of them away.
        internal static bool Stock(string path, Func<string> fresh)
        {
            if (string.IsNullOrEmpty(path) || fresh == null) return false;
            try
            {
                FileInfo about = new FileInfo(path);
                if (!about.Exists) return false;
                // Only a file that has actually changed is opened. This is checked for every personality and every
                // mood on a timer, and reading all of them each time is work for nothing: the flag can only have
                // moved if the file was written.
                string when = about.LastWriteTimeUtc.Ticks + ":" + about.Length;
                lock (stocked)
                {
                    string before;
                    if (stocked.TryGetValue(path, out before) && before == when) return false;
                }
                string stock = fresh();
                if (string.IsNullOrEmpty(stock)) return false;
                string had = File.ReadAllText(path);
                Match line = Regex.Match(had, @"(?mi)^[ \t]*" + Regex.Escape(ResetKey) + @"[ \t]*:[ \t]*(\S+)[ \t]*$");
                if (line.Success)
                {
                    string said = line.Groups[1].Value.Trim();
                    if (said != "1" && !said.Equals("yes", StringComparison.OrdinalIgnoreCase) && !said.Equals("true", StringComparison.OrdinalIgnoreCase)) { Seen(path); return false; }
                    File.WriteAllText(path, Marked(stock), new UTF8Encoding(false));
                    Seen(path);
                    return true;
                }
                // No line yet, so one is added. Their file is untouched otherwise.
                File.WriteAllText(path, Marked(had), new UTF8Encoding(false));
                Seen(path);
                return false;
            }
            catch (Exception) { return false; }
        }

        // The line goes under the first heading, where it is visible without being in the way of anything parsed.
        private static string Marked(string text)
        {
            if (Regex.IsMatch(text, @"(?mi)^[ \t]*" + Regex.Escape(ResetKey) + @"[ \t]*:")) return text;
            string note = ResetKey + ": 0" + Environment.NewLine
                + "> Set that to 1 and this file goes back to the one she ships with, next time she reads it." + Environment.NewLine;
            Match head = Regex.Match(text, @"(?m)^#[^\r\n]*\r?\n");
            if (!head.Success) return note + Environment.NewLine + text;
            int at = head.Index + head.Length;
            return text.Substring(0, at) + note + text.Substring(at);
        }

        internal static void Refresh(string path, string fresh)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(fresh)) return;
            try
            {
                if (!File.Exists(path)) return;
                string[] had = File.ReadAllLines(path);
                Dictionary<string, List<string>> mine = Noted(fresh.Replace("\r\n", "\n").Split('\n'));

                StringBuilder made = new StringBuilder();
                string under = "";
                bool wrote = false, changed = false;
                foreach (string raw in had)
                {
                    string one = (raw ?? "").Trim();
                    if (one.StartsWith("#"))
                    {
                        under = one.TrimStart('#').Trim();
                        wrote = false;
                        made.AppendLine(raw);
                        continue;
                    }
                    if (one.StartsWith(">"))
                    {
                        // Their notes go; ours take their place once, where theirs began.
                        changed = true;
                        if (wrote) continue;
                        wrote = true;
                        List<string> ours;
                        if (mine.TryGetValue(under, out ours)) foreach (string line in ours) made.AppendLine(line);
                        continue;
                    }
                    made.AppendLine(raw);
                }
                if (!changed) return;
                string done = made.ToString();
                if (done == string.Join(Environment.NewLine, had) + Environment.NewLine) return;
                File.WriteAllText(path, done, new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        private static Dictionary<string, List<string>> Noted(string[] lines)
        {
            Dictionary<string, List<string>> found = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            string under = "";
            foreach (string raw in lines)
            {
                string one = (raw ?? "").Trim();
                if (one.StartsWith("#")) { under = one.TrimStart('#').Trim(); continue; }
                if (!one.StartsWith(">")) continue;
                List<string> had;
                if (!found.TryGetValue(under, out had)) { had = new List<string>(); found[under] = had; }
                had.Add(raw.TrimEnd());
            }
            return found;
        }
    }
}
