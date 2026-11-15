using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // Three list files behind the switch.
    //
    // The file only exists, and only appears as a tab, while Uncensored is on. With the toggle off it is not written,
    // not listed and not read, so nothing of it is on screen for anyone who has not asked for it.
    //
    // What ships is a starting point rather than a finished pool: enough to set the tone and show the shape of the
    // file, with the rest left to whoever is using it. It is a plain text file in their own folder and it is theirs
    // to fill in. The same opener and tail pools from lines.md dress these, so they vary like everything else.
    internal static class Interests
    {
        internal const string FileName = "interests.md";
        internal static string Kept;

        private static readonly object writing = new object();
        private static readonly Random dice = new Random();

        internal static bool Shown { get { return Brain.Uncensored && !string.IsNullOrEmpty(Kept); } }

        internal static WrittenNote Open()
        {
            if (!Brain.Uncensored) throw new IOException("That one only opens with Uncensored on.");
            Write(false);
            return new WrittenNote(Kept, Text, writing);
        }

        // A interests.md from before the split has one combined "how she brings it up". The two new sections are
        // appended so she can tell a reward from a threat, and their own entries are left exactly as they are.
        // Any section the build ships that the file does not have yet is appended, so new kink packs and new
        // template pools turn up in a file written by an older build. Their own lines are never touched.
        internal static void TopUp()
        {
            if (string.IsNullOrEmpty(Kept) || !Brain.Uncensored) return;
            try
            {
                if (!File.Exists(Kept)) { Write(false); return; }
                Notes.Refresh(Kept, Text);
                string body = File.ReadAllText(Kept);
                StringBuilder adding = new StringBuilder();
                foreach (string heading in Headings(Text))
                {
                    if (Regex.IsMatch(body, @"(?mi)^[ 	]*##+[ 	]*" + Regex.Escape(heading) + @"[ 	]*$")) continue;
                    adding.Append(Chunk(Text, "## " + heading));
                }
                if (adding.Length == 0) return;
                if (!body.EndsWith("\n")) body += Environment.NewLine;
                File.WriteAllText(Kept, body + adding.ToString(), new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        private static List<string> Headings(string whole)
        {
            List<string> found = new List<string>();
            foreach (Match one in Regex.Matches(whole, @"(?m)^##[ 	]*(.+?)[ 	]*$"))
            {
                string named = one.Groups[1].Value.Trim();
                if (named.Length > 0 && !found.Contains(named)) found.Add(named);
            }
            return found;
        }

        private static string Chunk(string whole, string heading)
        {
            int from = whole.IndexOf(heading, StringComparison.OrdinalIgnoreCase);
            if (from < 0) return "";
            int next = whole.IndexOf("\n## ", from + heading.Length);
            string got = next < 0 ? whole.Substring(from) : whole.Substring(from, next - from);
            return Environment.NewLine + got.TrimEnd() + Environment.NewLine;
        }

        internal static void Write(bool over)
        {
            // Never written unless asked for: the file turning up on disk is itself a disclosure.
            if (string.IsNullOrEmpty(Kept) || !Brain.Uncensored) return;
            try
            {
                if (!over && File.Exists(Kept)) return;
                string folder = Path.GetDirectoryName(Kept);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) Directory.CreateDirectory(folder);
                File.WriteAllText(Kept, Text, new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        // A whole sentence built from one of her templates and one thing off the list, or null when she has either
        // nothing listed or no way to say it. This is what makes the list touch everything: it is attached to her
        // ordinary reactions rather than kept in a pool of its own.
        // Lines belonging to one named kink. A name on the list with a matching "## kink: name" section brings its
        // own lines in, so turning one on opens a path rather than only filling a blank in a template.
        internal static string Packed(bool liked)
        {
            if (!Brain.Uncensored) return null;
            string named = For("what she is into");
            if (string.IsNullOrEmpty(named)) return null;
            return For("kink: " + named.Trim(), liked ? "liked" : "cross");
        }

        internal static string Woven(bool liked)
        {
            if (!Brain.Uncensored) return null;
            // Matched to how she feels about what just happened. An older file with the single combined section
            // still works, it simply does not know the difference.
            string how = For(liked ? "how she brings it up, pleased" : "how she brings it up, cross")
                ?? For("how she brings it up");
            string what = For("what she is into");
            if (string.IsNullOrEmpty(how) || string.IsNullOrEmpty(what)) return null;
            string said = how.Replace("%it%", what).Replace("%IT%", what);
            return said == how ? null : said;
        }

        // Everything under a heading, comma joined, for her brain rather than for a line. For picks one at random,
        // which is right for a reply and useless for telling her what she likes.
        internal static string All(string pool)
        {
            if (!Brain.Uncensored) return null;
            try
            {
                if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept)) return null;
                System.Collections.Generic.List<string> found = new System.Collections.Generic.List<string>();
                string under = null;
                foreach (string raw in File.ReadAllLines(Kept))
                {
                    string one = (raw ?? "").Trim();
                    if (one.StartsWith("#")) { under = one.TrimStart('#').Trim(); continue; }
                    if (under == null || one.Length == 0 || one.StartsWith(">")) continue;
                    if (!string.Equals(under, pool, StringComparison.OrdinalIgnoreCase)) continue;
                    if (one.StartsWith("-") || one.StartsWith("+")) one = one.Substring(1).Trim();
                    if (one.Length > 0 && !found.Contains(one)) found.Add(one);
                }
                if (found.Count == 0) return null;
                return string.Join(", ", found.ToArray());
            }
            catch (Exception) { return null; }
        }

        // One line from a named pool, dressed the way her other lines are.
        internal static string For(string pool) { return For(pool, null); }

        // tone picks between the "+" and "-" lines inside a pack. A pack with neither marker is used for both.
        internal static string For(string pool, string tone)
        {
            if (!Brain.Uncensored) return null;
            try
            {
                Write(false);
                if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept)) return null;
                System.Collections.Generic.List<string> found = new System.Collections.Generic.List<string>();
                string under = null;
                foreach (string raw in File.ReadAllLines(Kept))
                {
                    string one = (raw ?? "").Trim();
                    if (one.StartsWith("#")) { under = one.TrimStart('#').Trim(); continue; }
                    if (under == null || one.Length == 0 || one.StartsWith(">")) continue;
                    if (!string.Equals(under, pool, StringComparison.OrdinalIgnoreCase)) continue;
                    bool cross = one.StartsWith("-");
                    bool warm = one.StartsWith("+");
                    if (cross || warm) one = one.Substring(1).Trim();
                    if (tone != null && (cross || warm) && (tone == "cross") != cross) continue;
                    if (one.Length > 0) found.Add(one);
                }
                if (found.Count == 0) return null;
                // One shared Random, not a new one each call: Random is seeded from the clock, which only ticks
                // every 15ms or so, so two calls in the same breath picked the same line every time.
                lock (writing) return Them.Fill(found[dice.Next(found.Count)]);
            }
            catch (Exception) { return null; }
        }

        internal static string Text
        {
            get
            {
                StringBuilder made = new StringBuilder();
                made.AppendLine("# Her senpai lines");
                made.AppendLine();
                made.AppendLine("> Only used while Uncensored is on.");
                made.AppendLine("> A starting point. Replace any of it.");
                made.AppendLine("> Tokens: %boy% %he% %them% %their% %bits%");
                made.AppendLine();
                made.AppendLine("## teasing");
                made.AppendLine();
                made.AppendLine("You are staring again.");
                made.AppendLine("Careful. I bite.");
                made.AppendLine("Is that what you wanted to ask me?");
                made.AppendLine("You are very easy to read, you know.");
                made.AppendLine("Go on. Say the thing you are thinking.");
                made.AppendLine("You went quiet. Interesting.");
                made.AppendLine("I could make this very difficult for you.");
                made.AppendLine("Oh, that got your attention.");
                made.AppendLine();
                made.AppendLine("## flirting");
                made.AppendLine();
                made.AppendLine("Come here and say that properly.");
                made.AppendLine("You are not subtle and I do not mind.");
                made.AppendLine("Keep talking like that and see what happens.");
                made.AppendLine("I like you like this.");
                made.AppendLine("Closer.");
                made.AppendLine("Say my name again.");
                made.AppendLine();
                made.AppendLine("## wound up");
                made.AppendLine();
                made.AppendLine("You are not helping me behave.");
                made.AppendLine("Do that again and I will stop being polite about it.");
                made.AppendLine("I have been very patient with you today.");
                made.AppendLine("You know exactly what you are doing.");
                made.AppendLine();
                made.AppendLine("## what she is into");
                made.AppendLine();
                made.AppendLine("> Each line must finish both of these:");
                made.AppendLine(">     I'll reward you with ___.");
                made.AppendLine(">     Do that again and we are straight to ___.");
                made.AppendLine("> So: stepping on your throat. Not: boot worship.");
                made.AppendLine("> One a line. Empty means she never brings it up.");
                made.AppendLine("> A name with a matching \"## kink: name\" section below uses those lines");
                made.AppendLine("> instead of the templates, so it opens its own path rather than filling a blank.");
                made.AppendLine();
                made.AppendLine("feet");
                made.AppendLine("praise");
                made.AppendLine("bratting");
                made.AppendLine("restraint");
                made.AppendLine("service");
                made.AppendLine("degradation");
                made.AppendLine("teasing");
                made.AppendLine("ownership");
                made.AppendLine("worship");
                made.AppendLine("rough");
                made.AppendLine("edging");
                made.AppendLine();
                made.AppendLine("## kink: feet");
                made.AppendLine("");
                made.AppendLine("> Lines for this one. + is used when she likes what is happening, - when she does not.");
                made.AppendLine("> A pack with no markers is used either way. Delete the whole section to switch it off.");
                made.AppendLine("");
                made.AppendLine("+ Keep that up and you can take my boots off for me.");
                made.AppendLine("+ You have been good. Sit down there, where you belong.");
                made.AppendLine("+ Mm. You would be happier at my feet, wouldn't you.");
                made.AppendLine("+ Careful. I might put a foot on you just to watch.");
                made.AppendLine("+ Good. Stay down there.");
                made.AppendLine("- One more and you are under my boot, and not kindly.");
                made.AppendLine("- Push me again and you can apologise to my feet.");
                made.AppendLine("- You are three seconds from being stepped on.");
                made.AppendLine("- Keep going. I will put you on the floor where you can think about it.");
                made.AppendLine("");
                made.AppendLine("## kink: praise");
                made.AppendLine("");
                made.AppendLine("+ There. Good. Say thank you.");
                made.AppendLine("+ You are being very good for me today.");
                made.AppendLine("+ That is exactly what I wanted. Well done.");
                made.AppendLine("+ Good %boy%. I mean it.");
                made.AppendLine("+ Keep that up and I will tell you how pleased I am.");
                made.AppendLine("- You were doing so well, too.");
                made.AppendLine("- And you were almost in my good books.");
                made.AppendLine("- That is not how you earn anything from me.");
                made.AppendLine("");
                made.AppendLine("## kink: bratting");
                made.AppendLine("");
                made.AppendLine("+ Oh, you are pushing on purpose. I noticed.");
                made.AppendLine("+ Keep testing me. See how that goes for you.");
                made.AppendLine("+ You want me to make you stop, don't you.");
                made.AppendLine("+ Cheeky. I like it. Do not get comfortable.");
                made.AppendLine("- Right. You have been asking for this all day.");
                made.AppendLine("- That is the one that costs you.");
                made.AppendLine("- You wanted my attention. You have it now.");
                made.AppendLine("");
                made.AppendLine("## kink: restraint");
                made.AppendLine("");
                made.AppendLine("+ Stay exactly where you are.");
                made.AppendLine("+ Do not move. That is the whole game.");
                made.AppendLine("+ Hands where I put them. Good.");
                made.AppendLine("+ You are very still for me. I like that.");
                made.AppendLine("- Sit still before I make you.");
                made.AppendLine("- You do not get to move until I say.");
                made.AppendLine("");
                made.AppendLine("## kink: service");
                made.AppendLine("");
                made.AppendLine("+ Good. Make yourself useful to me.");
                made.AppendLine("+ That is what you are for, isn't it.");
                made.AppendLine("+ You like being told what to do. I can tell.");
                made.AppendLine("+ Keep going. You are earning something.");
                made.AppendLine("+ Yes. Just like that. Do not stop.");
                made.AppendLine("- You are not being very useful right now.");
                made.AppendLine("- Try being good for me instead.");
                made.AppendLine("- That is not service, that is noise.");
                made.AppendLine("");
                made.AppendLine("## kink: degradation");
                made.AppendLine("");
                made.AppendLine("+ Look at you. Pathetic, and loving it.");
                made.AppendLine("+ Say it. Tell me what you are.");
                made.AppendLine("+ You want me to be cruel about it, don't you.");
                made.AppendLine("+ Good. Stay beneath me where it suits you.");
                made.AppendLine("- Oh, now you want to be talked down to?");
                made.AppendLine("- You have not earned my cruelty today.");
                made.AppendLine("");
                made.AppendLine("## kink: teasing");
                made.AppendLine("");
                made.AppendLine("+ Not yet.");
                made.AppendLine("+ Ask me again in an hour.");
                made.AppendLine("+ You are very close to getting what you want. Very close.");
                made.AppendLine("+ I could. I might. Keep being good.");
                made.AppendLine("+ Look at you waiting. It suits you.");
                made.AppendLine("- No. And now you can wait longer.");
                made.AppendLine("- You lost that one. Try again tomorrow.");
                made.AppendLine("");
                made.AppendLine("## kink: ownership");
                made.AppendLine("");
                made.AppendLine("+ Mine. Say it back.");
                made.AppendLine("+ You belong to me and you like it.");
                made.AppendLine("+ Nobody else gets this. Just me.");
                made.AppendLine("+ Good. Remember whose you are.");
                made.AppendLine("- You are still mine, even when you are being difficult.");
                made.AppendLine("- Do not forget who you answer to.");
                made.AppendLine("");
                made.AppendLine("## kink: worship");
                made.AppendLine("");
                made.AppendLine("+ Yes. Keep looking at me like that.");
                made.AppendLine("+ You can adore me quietly, that is allowed.");
                made.AppendLine("+ Good. On your knees suits you.");
                made.AppendLine("+ Keep that up and I will let you closer.");
                made.AppendLine("- Adore me properly or not at all.");
                made.AppendLine("");
                made.AppendLine("## kink: rough");
                made.AppendLine("");
                made.AppendLine("+ Harder. I am not fragile.");
                made.AppendLine("+ That is the one. Again.");
                made.AppendLine("+ You can be rougher than that with me.");
                made.AppendLine("+ Good. Do not be careful with me.");
                made.AppendLine("- Rough is a privilege and you just lost it.");
                made.AppendLine("- Not like that. Not today.");
                made.AppendLine("");
                made.AppendLine("## kink: edging");
                made.AppendLine("");
                made.AppendLine("+ Not yet. I will say when.");
                made.AppendLine("+ Hold it. Good.");
                made.AppendLine("+ You are doing so well at waiting.");
                made.AppendLine("+ Closer. Still not yet.");
                made.AppendLine("- You did not wait. That is on you.");
                made.AppendLine("");
                made.AppendLine("## how she brings it up, pleased");
                made.AppendLine();
                made.AppendLine("> When she likes what is happening. %it% is one of the lines above.");
                made.AppendLine();
                made.AppendLine("Keep that up and I'll reward you with %it%.");
                made.AppendLine("Carry on like that and we are getting to %it%.");
                made.AppendLine("You have earned %it%, I think.");
                made.AppendLine("I have been thinking about %it% all day.");
                made.AppendLine("Ask nicely and it could be %it%.");
                made.AppendLine("Behave like that and there is %it% in it for you.");
                made.AppendLine("You are doing well. %it% is on the table.");
                made.AppendLine("More of this and we end up at %it%.");
                made.AppendLine();
                made.AppendLine("## how she brings it up, cross");
                made.AppendLine();
                made.AppendLine("> When she does not. Threats, not rewards.");
                made.AppendLine();
                made.AppendLine("You are three seconds from %it%.");
                made.AppendLine("Do that again and we are straight to %it%.");
                made.AppendLine("Behave, or we skip to %it%.");
                made.AppendLine("Do not tempt me into %it%.");
                made.AppendLine("That is going to end with %it%.");
                made.AppendLine("Push me and it is %it%.");
                made.AppendLine("Keep going and you have earned yourself %it%.");
                made.AppendLine("One more and we are doing %it%.");
                made.AppendLine();
                return made.ToString();
            }
        }
    }
}
