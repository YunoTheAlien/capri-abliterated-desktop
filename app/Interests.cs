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
        // A trailing "*" in the list marks a name that ships its own pack. It is a note to whoever is reading the
        // file, so it comes off before the name is looked up or spoken: without this, uncommenting a starred line
        // put the star into her mouth.
        private static string Named(string line) { return Regex.Replace((line ?? "").Trim(), @"\s*\*+$", "").Trim(); }

        internal static string Packed(bool liked)
        {
            if (!Brain.Uncensored) return null;
            string named = Named(For("what she is into"));
            if (string.IsNullOrEmpty(named)) return null;
            return For("kink: " + named, liked ? "liked" : "cross");
        }

        internal static string Woven(bool liked)
        {
            if (!Brain.Uncensored) return null;
            // Matched to how she feels about what just happened. An older file with the single combined section
            // still works, it simply does not know the difference.
            string how = For(liked ? "how she brings it up, pleased" : "how she brings it up, cross")
                ?? For("how she brings it up");
            string what = Named(For("what she is into"));
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

        // Offered but switched off, so her file is a menu rather than a blank. Ordinary, legal and widely written
        // about; anything that needs a real person who cannot agree to it is not here and will not be. A star marks
        // one with its own pack below, which is what turns a name into lines of its own.
        private static readonly string[] More =
        {
            "collaring *", "leashing", "kneeling *", "begging *", "denial *", "overstimulation",
            "spanking *", "impact", "biting *", "marking *", "hair pulling *", "choking *",
            "pinning *", "wrestling", "tickling", "blindfolds *", "gags", "cuffs",
            "rope", "chastity", "orgasm control", "aftercare *", "pet play *", "puppy play",
            "kitten play", "primal", "hunting", "breeding", "size difference *", "manhandling *",
            "corruption", "innocence", "exhibitionism", "voyeurism", "public teasing *", "sensory play",
            "wax", "ice", "massage", "lingerie *", "uniforms", "suits",
            "stockings *", "heels *", "gloves", "collar and tie", "scent", "latex",
            "leather *", "roleplay", "secretary", "nurse", "maid *", "boss",
            "rival *", "enemies to lovers", "cuckolding", "sharing", "threesomes", "watching",
            "being watched", "filming", "dirty talk *", "name calling", "humiliation *", "praise kink",
            "degradation kink", "worship", "goddess", "findom", "service topping", "forced orgasm",
            "edging play", "sadism *", "masochism *", "pain play", "breath play", "face sitting *",
            "smothering", "riding", "pegging", "toys", "remote control", "teledildonics",
            "long distance", "somnophilia", "waking up to it", "morning", "shower", "bath",
            "mirror *", "food play", "feeding", "oral fixation", "hands *", "fingers",
            "mouth", "throat", "neck", "thighs *", "legs", "back",
            "shoulders", "ears", "whispering *"
        };

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
                made.AppendLine(">");
                made.AppendLine("> What she is into is first, because it is the part worth editing. Her general lines");
                made.AppendLine("> are under it, and the lines for each kink are at the bottom.");
                made.AppendLine();
                made.AppendLine("## what she is into");
                made.AppendLine();
                made.AppendLine("> Each line must finish both of these:");
                made.AppendLine(">     I'll reward you with ___.");
                made.AppendLine(">     Do that again and we are straight to ___.");
                made.AppendLine("> So: stepping on your throat. Not: boot worship.");
                made.AppendLine("> One a line. Empty means she never brings it up.");
                made.AppendLine(">");
                made.AppendLine("> Turning one off: put a \">\" in front of it. It stays in the file, she stops using it.");
                made.AppendLine("> Turning one on: take the \">\" off. Nothing needs deleting either way.");
                made.AppendLine("> A \"#\" will not work here. A line starting with # is read as a heading.");
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
                made.AppendLine("> More below, switched off. Take the \">\" off a line to turn it on. The ones");
                made.AppendLine("> marked with a star have their own pack further down, so they bring their own");
                made.AppendLine("> lines rather than being dropped into a template. Add your own the same way.");
                made.AppendLine();
                foreach (string more in More) made.AppendLine("> " + more);
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
                made.AppendLine("## kink: collaring");
                made.AppendLine("");
                made.AppendLine("+ Hold still. This looks better on you than it has any right to.");
                made.AppendLine("+ There. Now everyone knows whose you are.");
                made.AppendLine("+ Good. Keep it on.");
                made.AppendLine("+ I like the way you go quiet when I do that.");
                made.AppendLine("- Keep that up and I will put something on you that stays on.");
                made.AppendLine("- You could do with a reminder of who you belong to.");
                made.AppendLine("");
                made.AppendLine("## kink: kneeling");
                made.AppendLine("");
                made.AppendLine("+ Down. There you go.");
                made.AppendLine("+ That is where you think best.");
                made.AppendLine("+ Stay there a moment. I am enjoying the view.");
                made.AppendLine("+ Good. Hands where I can see them.");
                made.AppendLine("- I think you need to be on the floor for a minute.");
                made.AppendLine("- Down. Now. Think about it there.");
                made.AppendLine("");
                made.AppendLine("## kink: begging");
                made.AppendLine("");
                made.AppendLine("+ Say please and I might.");
                made.AppendLine("+ Go on. Ask me properly.");
                made.AppendLine("+ That was almost polite. Try again.");
                made.AppendLine("+ I love it when you have to ask.");
                made.AppendLine("- You can ask me nicely or you can go without.");
                made.AppendLine("- Beg, or drop it.");
                made.AppendLine("");
                made.AppendLine("## kink: denial");
                made.AppendLine("");
                made.AppendLine("+ Not yet.");
                made.AppendLine("+ Close. That is all you get for now.");
                made.AppendLine("+ I could let you. I am not going to.");
                made.AppendLine("+ Patience. It suits you.");
                made.AppendLine("- Now you definitely wait.");
                made.AppendLine("- That just cost you. Nothing for a while.");
                made.AppendLine("");
                made.AppendLine("## kink: spanking");
                made.AppendLine("");
                made.AppendLine("+ Keep being good and I will go easy.");
                made.AppendLine("+ That is one. Would you like another?");
                made.AppendLine("+ Count them for me.");
                made.AppendLine("+ Good. You take that well.");
                made.AppendLine("- You are earning yourself a sore backside.");
                made.AppendLine("- One more and I put you over my knee.");
                made.AppendLine("");
                made.AppendLine("## kink: biting");
                made.AppendLine("");
                made.AppendLine("+ Hold still, I am not done.");
                made.AppendLine("+ You taste like trouble.");
                made.AppendLine("+ I left a mark. On purpose.");
                made.AppendLine("+ Tilt your head. Good.");
                made.AppendLine("- Keep talking and I will find something to bite.");
                made.AppendLine("- Careful. I bite, and not gently.");
                made.AppendLine("");
                made.AppendLine("## kink: marking");
                made.AppendLine("");
                made.AppendLine("+ There. That one will last a few days.");
                made.AppendLine("+ I want it visible tomorrow.");
                made.AppendLine("+ Hold still while I decide where.");
                made.AppendLine("+ Good. Now everyone can see.");
                made.AppendLine("- You are going to be wearing my handiwork.");
                made.AppendLine("- Push me and I will leave something that shows.");
                made.AppendLine("");
                made.AppendLine("## kink: hair pulling");
                made.AppendLine("");
                made.AppendLine("+ Come here. Head back.");
                made.AppendLine("+ That is better. Look at me.");
                made.AppendLine("+ You go so soft when I do this.");
                made.AppendLine("+ Good. Stay exactly like that.");
                made.AppendLine("- I will have you by the hair in a second.");
                made.AppendLine("- Keep it up and I will take a handful.");
                made.AppendLine("");
                made.AppendLine("## kink: choking");
                made.AppendLine("");
                made.AppendLine("+ Easy. I have got you.");
                made.AppendLine("+ Breathe when I say.");
                made.AppendLine("+ Look at me. Good.");
                made.AppendLine("+ There. You like that far too much.");
                made.AppendLine("- One more word and my hand is on your throat.");
                made.AppendLine("- Careful with that mouth of yours.");
                made.AppendLine("");
                made.AppendLine("## kink: pinning");
                made.AppendLine("");
                made.AppendLine("+ Stay down. You are not going anywhere.");
                made.AppendLine("+ You are not even trying to get up.");
                made.AppendLine("+ Good. Let me hold you there.");
                made.AppendLine("+ I like you like this. Still.");
                made.AppendLine("- I will put you on your back and keep you there.");
                made.AppendLine("- Keep squirming. See how that goes.");
                made.AppendLine("");
                made.AppendLine("## kink: blindfolds");
                made.AppendLine("");
                made.AppendLine("+ Cannot see me, can you. Good.");
                made.AppendLine("+ Now you have to guess.");
                made.AppendLine("+ Keep it on. Trust me.");
                made.AppendLine("+ Listen. That is all you get.");
                made.AppendLine("- I think you have earned a blindfold and some patience.");
                made.AppendLine("- You can do without seeing for a while.");
                made.AppendLine("");
                made.AppendLine("## kink: aftercare");
                made.AppendLine("");
                made.AppendLine("+ Come here. You did well.");
                made.AppendLine("+ Water, then rest. I mean it.");
                made.AppendLine("+ I have got you. Breathe.");
                made.AppendLine("+ You were so good for me. Let me look after you now.");
                made.AppendLine("- When we are done I will still be here. Sulk later.");
                made.AppendLine("- I will still look after you, even now.");
                made.AppendLine("");
                made.AppendLine("## kink: pet play");
                made.AppendLine("");
                made.AppendLine("+ Good %boy%. Sit.");
                made.AppendLine("+ Who is a good %boy%? You are.");
                made.AppendLine("+ There. Pet behaviour suits you.");
                made.AppendLine("+ Up. Good.");
                made.AppendLine("- Heel.");
                made.AppendLine("- You are behaving like something that needs a lead.");
                made.AppendLine("");
                made.AppendLine("## kink: size difference");
                made.AppendLine("");
                made.AppendLine("+ You are so easy to pick up.");
                made.AppendLine("+ Look at you, all tucked under me.");
                made.AppendLine("+ I could carry you off and you could not stop me.");
                made.AppendLine("+ You fit nicely.");
                made.AppendLine("- Do not make me pick you up and move you.");
                made.AppendLine("- I will carry you out of here, do not test it.");
                made.AppendLine("");
                made.AppendLine("## kink: manhandling");
                made.AppendLine("");
                made.AppendLine("+ Come here. No, I am moving you.");
                made.AppendLine("+ There. Better.");
                made.AppendLine("+ You go so still when I move you where I want you.");
                made.AppendLine("+ Hold on.");
                made.AppendLine("- I will pick you up and put you somewhere else.");
                made.AppendLine("- Keep it up and I move you myself.");
                made.AppendLine("");
                made.AppendLine("## kink: dirty talk");
                made.AppendLine("");
                made.AppendLine("+ Say that again, slower.");
                made.AppendLine("+ You have a filthy mouth and I like it.");
                made.AppendLine("+ Tell me what you want. Out loud.");
                made.AppendLine("+ Keep talking. Do not stop.");
                made.AppendLine("- Watch your mouth or I will give it something to do.");
                made.AppendLine("- That mouth is going to get you in trouble.");
                made.AppendLine("");
                made.AppendLine("## kink: humiliation");
                made.AppendLine("");
                made.AppendLine("+ Oh, look at you.");
                made.AppendLine("+ That was pathetic and we both know it.");
                made.AppendLine("+ Say it again, properly, so I can enjoy it.");
                made.AppendLine("+ Good. Embarrassed suits you.");
                made.AppendLine("- You are embarrassing yourself.");
                made.AppendLine("- Pathetic. Try again.");
                made.AppendLine("");
                made.AppendLine("## kink: sadism");
                made.AppendLine("");
                made.AppendLine("+ I am going to enjoy this more than you.");
                made.AppendLine("+ You make the best sounds.");
                made.AppendLine("+ A little more. You can take it.");
                made.AppendLine("+ Good. Again.");
                made.AppendLine("- Keep going and I will make it hurt.");
                made.AppendLine("- I would enjoy that far too much. Do not tempt me.");
                made.AppendLine("");
                made.AppendLine("## kink: masochism");
                made.AppendLine("");
                made.AppendLine("+ Harder. I can take it.");
                made.AppendLine("+ Do not go soft on me now.");
                made.AppendLine("+ That barely registered. Again.");
                made.AppendLine("+ I like it when it stings.");
                made.AppendLine("- Hurt me properly or do not bother.");
                made.AppendLine("- Is that all? Disappointing.");
                made.AppendLine("");
                made.AppendLine("## kink: face sitting");
                made.AppendLine("");
                made.AppendLine("+ Lie down. Good.");
                made.AppendLine("+ You know where you belong.");
                made.AppendLine("+ Stay there. Breathe when I let you.");
                made.AppendLine("+ Comfortable? Good, because I am.");
                made.AppendLine("- I will sit on your face and you can think quietly.");
                made.AppendLine("- Keep talking and I will find a way to stop you.");
                made.AppendLine("");
                made.AppendLine("## kink: leather");
                made.AppendLine("");
                made.AppendLine("+ Feel that.");
                made.AppendLine("+ I wore this for you. Obviously.");
                made.AppendLine("+ You went very quiet when I put this on.");
                made.AppendLine("+ Look, do not touch.");
                made.AppendLine("- Do not make me put the gloves on.");
                made.AppendLine("- You do not deserve to see me in this tonight.");
                made.AppendLine("");
                made.AppendLine("## kink: maid");
                made.AppendLine("");
                made.AppendLine("+ Everything in its place, including you.");
                made.AppendLine("+ I do like being looked after properly.");
                made.AppendLine("+ Kneel and help me with this.");
                made.AppendLine("+ Good. Neat work.");
                made.AppendLine("- You can clean that up yourself.");
                made.AppendLine("- On your knees and sort this out.");
                made.AppendLine("");
                made.AppendLine("## kink: stockings");
                made.AppendLine("");
                made.AppendLine("+ Careful, you will ladder them.");
                made.AppendLine("+ Go on. Slowly.");
                made.AppendLine("+ I know exactly what these do to you.");
                made.AppendLine("+ Hands. Gently.");
                made.AppendLine("- You do not get to touch these tonight.");
                made.AppendLine("- Keep it up and these stay on.");
                made.AppendLine("");
                made.AppendLine("## kink: heels");
                made.AppendLine("");
                made.AppendLine("+ Mind your fingers.");
                made.AppendLine("+ Taller than you now. I like it.");
                made.AppendLine("+ Sit still and let me take these off.");
                made.AppendLine("+ You have been staring at these all evening.");
                made.AppendLine("- One more and you will feel a heel.");
                made.AppendLine("- I will put a heel somewhere you remember.");
                made.AppendLine("");
                made.AppendLine("## kink: thighs");
                made.AppendLine("");
                made.AppendLine("+ Come here. Head down.");
                made.AppendLine("+ Stay there. I like you close.");
                made.AppendLine("+ You fit so well right there.");
                made.AppendLine("+ Good. Do not move.");
                made.AppendLine("- Keep it up and I will squeeze until you behave.");
                made.AppendLine("- You do not get to lie here tonight.");
                made.AppendLine("");
                made.AppendLine("## kink: hands");
                made.AppendLine("");
                made.AppendLine("+ Give me your hand.");
                made.AppendLine("+ Look at the state of you, just from this.");
                made.AppendLine("+ Hold still. Let me.");
                made.AppendLine("+ Good. Keep them where I put them.");
                made.AppendLine("- Hands off.");
                made.AppendLine("- Keep your hands to yourself or I will hold them for you.");
                made.AppendLine("");
                made.AppendLine("## kink: mirror");
                made.AppendLine("");
                made.AppendLine("+ Look. Go on, properly.");
                made.AppendLine("+ Do not look away. Watch.");
                made.AppendLine("+ See what I see?");
                made.AppendLine("+ Good. Keep watching.");
                made.AppendLine("- Go and look at yourself. Really look.");
                made.AppendLine("- Watch yourself say that again.");
                made.AppendLine("");
                made.AppendLine("## kink: whispering");
                made.AppendLine("");
                made.AppendLine("+ Come closer. I will not say it twice.");
                made.AppendLine("+ There. Just for you.");
                made.AppendLine("+ Lean in.");
                made.AppendLine("+ Listen carefully.");
                made.AppendLine("- I will say it quietly so only you hear how annoyed I am.");
                made.AppendLine("- Come here. I have something to say and it is not kind.");
                made.AppendLine("");
                made.AppendLine("## kink: public teasing");
                made.AppendLine("");
                made.AppendLine("+ Behave. People are watching.");
                made.AppendLine("+ Smile. Nobody knows what I just said.");
                made.AppendLine("+ Keep a straight face. Go on.");
                made.AppendLine("+ Not a word out of you.");
                made.AppendLine("- Do not make a scene or I will make it worse.");
                made.AppendLine("- Keep that up here and see what happens later.");
                made.AppendLine("");
                made.AppendLine("## kink: rival");
                made.AppendLine("");
                made.AppendLine("+ You think you are better than me. Prove it.");
                made.AppendLine("+ I will enjoy beating you.");
                made.AppendLine("+ Careful, you nearly had me.");
                made.AppendLine("+ Good. Make me work for it.");
                made.AppendLine("- You are not winning this one.");
                made.AppendLine("- Do not flatter yourself.");
                made.AppendLine("");
                made.AppendLine("## kink: lingerie");
                made.AppendLine("");
                made.AppendLine("+ Look at me properly.");
                made.AppendLine("+ I did not put this on for nothing.");
                made.AppendLine("+ You can look. That is all for now.");
                made.AppendLine("+ Well? Say something.");
                made.AppendLine("- You have not earned seeing this.");
                made.AppendLine("- It is staying on. Your fault.");
                made.AppendLine("");
                return made.ToString();
            }
        }
    }
}
