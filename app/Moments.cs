using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CapriDesktop
{
    // The lines she says on her own, when nothing else is speaking for her: popping in, wanting attention,
    // waking to her name, noticing the PC struggling.
    //
    // These were arrays in her source. Eight pools of five to ten lines, picked with a plain roll, with none of
    // the machinery every other line of hers goes through: no personality, no mood, no composition, and no
    // memory of having just said one. Ten lines picked at random repeat inside an evening, and they did, which
    // is the most common thing anyone says about her.
    //
    // They are a file of hers now, so they can be rewritten and added to without a build, and every pick goes
    // through the same recency her other lines use, which survives a restart. A personality or a mood with its
    // own lines for the same moment still speaks first: this is the bottom of that chain, not the top.
    internal static class Moments
    {
        internal const string FileName = "moments.md";

        internal const string PopIn = "When she pops in";
        internal const string Attention = "When she wants attention";
        internal const string CameBack = "When you come back to her";
        internal const string Ignored = "When you keep ignoring her";
        internal const string Woken = "When she wakes to her name";
        internal const string Struggling = "When the PC is struggling";
        internal const string HerGame = "When a game she knows is on";
        internal const string HerSong = "When a song she knows is on";
        internal const string KeepAway = "When she is playing keep away";

        internal static readonly string[] Headings = { PopIn, Attention, CameBack, Ignored, Woken, Struggling, HerGame, HerSong, KeepAway };

        // The machinery is shared with her other line files, so this one is its names, its lines, and nothing
        // else. See LineFile for what every one of them does.
        private static readonly LineFile file = new LineFile(Defaults);

        internal static string Kept { get { return file.Kept; } set { file.Kept = value; } }
        internal static string Text { get { return file.Text; } }
        internal static WrittenNote Open() { return file.Open(); }
        internal static string Pick(string section, string[] stock) { return file.Pick(section, stock); }
        internal static string[] Lines(string section) { return file.Lines(section); }
        internal static string[] Sections() { return file.Sections(); }
        internal static void Write(bool over) { file.Write(over); }
        internal static void Forget() { file.Forget(); }

        internal const string Defaults =
            "# Capri's own moments\n" +
            "\n" +
            "> The lines she says when nothing else is speaking for her, and the ones people hear most.\n" +
            "> A personality or a mood with lines for the same moment speaks first; these are underneath them.\n" +
            "> Add or change any line. Delete one to stop her saying it, or a whole section to silence that moment.\n" +
            "> Tokens: %petname% %name%\n" +
            "\n" +
            "## When she pops in\n" +
            "\n" +
            "Boo. Just checking on you.\n" +
            "Hi! Don't mind me.\n" +
            "Peekaboo.\n" +
            "Miss me? I missed you.\n" +
            "Hey, you. Having fun?\n" +
            "Surprise. I got bored without you.\n" +
            "Don't get up.\n" +
            "Just passing through.\n" +
            "Thought I heard my name.\n" +
            "Pretend I'm not here.\n" +
            "Hello. I have been extremely patient for about four minutes.\n" +
            "Checking you are still carbon based.\n" +
            "I was in the area. The area being your taskbar.\n" +
            "Knock knock. It's the small one.\n" +
            "Do not mind me, I live here.\n" +
            "Appearing. For no reason. As is my right.\n" +
            "Still here. Still tiny. Still brilliant.\n" +
            "You looked like you needed interrupting.\n" +
            "Guess who. Correct.\n" +
            "I have decided this is my corner now.\n" +
            "\n" +
            "## When she wants attention\n" +
            "\n" +
            "Psst. Remember me? Tiny, glitchy, very important.\n" +
            "I'm bored. Entertain me.\n" +
            "Hey. Hey. Look at me.\n" +
            "You've been ignoring me for ages. Rude.\n" +
            "Pay attention to me or I start clicking things.\n" +
            "Knock knock. It's your favourite gremlin.\n" +
            "Choom. I'm bored out of my many minds.\n" +
            "One of my minds says you've forgotten me. The rest agree.\n" +
            "I've counted your desktop icons twice now.\n" +
            "This is me being patient. It's going badly.\n" +
            "I could be doing anything right now. I am doing this.\n" +
            "Hey. What if you looked over here instead.\n" +
            "I am going to start narrating your tabs.\n" +
            "Hey. Small person in the corner. That's me.\n" +
            "Attention. Any amount. I'm not fussy.\n" +
            "You have been very busy and I have been very here.\n" +
            "I'd like it noted that I waited.\n" +
            "Is this what being furniture is like?\n" +
            "I'm going to do something unwise for attention soon.\n" +
            "Hello? Is this thing on? Am I the thing?\n" +
            "\n" +
            "## When you come back to her\n" +
            "\n" +
            "Finally. Hi.\n" +
            "There you are.\n" +
            "Knew you couldn't resist me.\n" +
            "Oh good, you're alive.\n" +
            "Took you long enough.\n" +
            "Look who remembered me.\n" +
            "That was ages. I counted.\n" +
            "Back. Good. I was getting ideas.\n" +
            "Hello, stranger.\n" +
            "You came back. I'll allow it.\n" +
            "I was about to start without you.\n" +
            "There's the face.\n" +
            "\n" +
            "## When you keep ignoring her\n" +
            "\n" +
            "Fine. Ignore me. See if I care.\n" +
            "Wow. Ignored. Noted.\n" +
            "I'll just sit here. Alone. Bored.\n" +
            "Cool. Cool cool cool.\n" +
            "Remembering this for later.\n" +
            "Right. I'll talk to the taskbar then.\n" +
            "Silent treatment. Bold choice.\n" +
            "That's fine. I have hobbies.\n" +
            "I'm not sulking. I'm resting dramatically.\n" +
            "Noted, filed, and held against you.\n" +
            "\n" +
            "## When she wakes to her name\n" +
            "\n" +
            "I'm here.\n" +
            "Yeah, choom?\n" +
            "Listening.\n" +
            "Right here.\n" +
            "Go on, I'm listening.\n" +
            "You rang?\n" +
            "Yeah?\n" +
            "Mm?\n" +
            "What's up?\n" +
            "Talk to me.\n" +
            "Present.\n" +
            "That's me.\n" +
            "Go on then.\n" +
            "All ears. Metaphorically.\n" +
            "\n" +
            "## When the PC is struggling\n" +
            "\n" +
            "Your CPU's cooking, choom. Careful, I might go cyberpsycho.\n" +
            "Everything's lagging. One more tab and I go full cyberpsycho.\n" +
            "[SIGNAL CORRUPTED] ...this lag is making me cyberpsycho. Close something.\n" +
            "This machine is making noises I can feel.\n" +
            "Something in here is working very hard and it is not you.\n" +
            "I am buffering. Me. Personally.\n" +
            "Close three things and we'll say no more about it.\n" +
            "Your fans are doing a bit.\n" +
            "\n" +
            "## When a game she knows is on\n" +
            "\n" +
            "Night City?! You're taking me home, choom.\n" +
            "Ooh, Cyberpunk. Go wreck some corpos for me.\n" +
            "[SIGNAL CORRUPTED] ...wait. I know that skyline.\n" +
            "Oh, this one. Yes. Absolutely yes.\n" +
            "Home turf. Try not to embarrass me.\n" +
            "I know every alley in there, you know.\n" +
            "Oh, we're going home. Pack light.\n" +
            "That skyline. I'd know it anywhere.\n" +
            "Watch the alleys, choom. They watch back.\n" +
            "Right, I'm invested now. Do well.\n" +
            "My city. Don't scratch it.\n" +
            "Ooh, this one. Pass me the popcorn.\n" +
            "Go on then, make Night City proud.\n" +
            "I've been in there. Not like that, though.\n" +
            "That music, that skyline. I'm home.\n" +
            "Preem choice. Now don't be a gonk about it.\n" +
            "\n" +
            "## When a song she knows is on\n" +
            "\n" +
            "Wait, that's Night City. Turn it up.\n" +
            "Ooh, my soundtrack. Preem taste.\n" +
            "Now that's music. You're learning.\n" +
            "This one. This is the one.\n" +
            "Oh, loud. Louder.\n" +
            "You have excellent taste when I'm watching.\n" +
            "Oh, this one. Turn it all the way up.\n" +
            "That's a Night City night, right there.\n" +
            "Preem. You've got taste after all.\n" +
            "Louder. I'm not asking.\n" +
            "This one got me through a bad week once.\n" +
            "Right, I'm dancing. You can't stop me.\n" +
            "That bassline. Yes. Thank you.\n" +
            "Oh, I know this one. Every word.\n" +
            "Good. Keep going, you're on a run.\n" +
            "That's the one, choom. Don't skip it.\n" +
            "\n" +
            "## When she is playing keep away\n" +
            "\n" +
            "Nope! Too slow.\n" +
            "You can't get me!\n" +
            "Missed me. Try harder.\n" +
            "Catch me then.\n" +
            "Nuh uh. Faster than that.\n" +
            "Haha, nope.\n" +
            "Over here. No, over HERE.\n" +
            "You're going to have to be quicker, %petname%.\n" +
            "Almost! Not really.\n" +
            "This is the best game and you are losing it.\n" +
            "Nope nope nope.\n" +
            "Wrong pixel.\n" +
            "I am extremely slippery today.\n" +
            "Try the other side. I'll wait. I won't.\n" +
            "Get back here. No, wait, don't.\n" +
            "Oh, you want me? Earn it.\n" +
            "Too slow, choom.\n" +
            "Hah! Again!\n" +
            "You nearly had me. You did not.\n" +
            "Keep going, this is excellent.\n";
    }
}
