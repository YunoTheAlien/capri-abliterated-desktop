using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

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
