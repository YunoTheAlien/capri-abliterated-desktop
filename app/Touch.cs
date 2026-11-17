using System;

namespace CapriDesktop
{
    // Where on her you put your hand, and what she says about it.
    //
    // A stroke anywhere on her read as the same head pat, and a slap landed the same wherever it hit, so her
    // head and her hip were the same place as far as she was concerned.
    //
    // The ordinary zones are hers at any time. The senpai ones exist only with the adult switch on: the sections
    // are not written into a censored install, not read there, and the bands they cover simply read as nothing,
    // so touching her there is the plain pat it always was and nothing anywhere says there is more to find.
    //
    // Whether she wants it is her forwardness, the same number her brain is told about: five or more and she is
    // pleased, under that and she moves your hand, whatever the toggle says. On air it is zero by that number's
    // own rule, so none of this reaches a stream.
    internal static class Touch
    {
        internal const string FileName = "touch.md";

        // The ordinary zones, which exist whatever the switch says.
        internal const string Head = "When you touch her head";
        internal const string Shoulders = "When you touch her shoulders";
        internal const string Middle = "When you touch her middle";
        internal const string Legs = "When you touch her legs";
        internal const string Feet = "When you touch her feet";
        // What the two bands over her chest and her hips answer with when the switch is off. Without it a
        // hand there got nothing back at all, which reads as her being broken rather than as a boundary. The
        // high five is a ctrl click rather than a zone, and has its own lines.
        internal const string Tickled = "When you tickle her";
        // The senpai ones, and what she says when she is not in the mood for them.
        internal const string Chest = "When you touch her chest";
        internal const string Hips = "When you touch her hips";
        internal const string Slapped = "When you slap her hips";
        internal const string NotNow = "When she is not in the mood";

        // The forwardness at which she welcomes a senpai zone rather than moving your hand.
        internal const int Willing = 5;

        // This file was written for the senpai side first, so her censored lines carried that register even
        // where the zone was an ordinary one: a shoulder rub that reads as something else in a bubble with
        // nothing around it. Swapped in files already written as well as in the one she ships.
        private static readonly string[,] Reworded =
        {
            { "Right there, yeah.", "Right behind the ear, yeah." },
            { "Oh, that's the one. Do that again.", "Oh, that's the spot. Mind the antenna." },
            { "That's cheating and I'm not stopping you.", "That's cheating. My hair's a lost cause now." },
            { "You know exactly what you're doing.", "You've done this before, haven't you." },
            { "Press harder. I can take it.", "Lean on it. There's a knot under there." },
            { "Harder. I'm not made of glass. Mostly.", "Dig in. I'm not made of glass. Mostly." },
            { "Oh, that's criminal. Don't stop.", "Oh, that's the knot. Keep at it." },
            { "Both hands, choom. Commit.", "Both hands, choom. It's a big knot." },
            { "Go on, dig in. I've earned it.", "Go on, knuckle it out. I've earned it." },
            { "Mm. Keep that up and I'll owe you.", "Mm. Keep that up and I owe you a coffee." },
            { "That's a bold place to put a hand.", "That's a lot of hand for a ribcage." },
            { "That is not a handle.", "My ribs are not a handle." },
            { "That's a bold hand placement, choom.", "That's a lot of hand for a kneecap, choom." },
            { "If you're checking they're real, they are.", "If you're checking the chrome's real, it is." },
            { "Harder. I've been sat here all day.", "Right into that knot, go on. I've been sat here all day." },
            { "Mm. Don't stop on my account.", "That's the one. Mind the shoulder blade." },
            { "Rude. Do it again.", "Rude. I'm getting you back for that." }
        };

        private static readonly LineFile file = new LineFile(delegate { return Defaults; }, Reworded);

        internal static string Kept { get { return file.Kept; } set { file.Kept = value; } }
        internal static string Text { get { return file.Text; } }
        internal static string[] Sections() { return file.Sections(); }
        internal static WrittenNote Open() { return file.Open(); }
        internal static void Write(bool over) { file.Write(over); }
        internal static void Forget() { file.Forget(); }

        internal static bool Senpai(string zone)
        {
            return zone == Chest || zone == Hips || zone == Slapped;
        }

        // Her line for a zone, or null to leave her saying what she always said. A senpai zone with the switch
        // off never gets here: the band reads as nothing, so the caller never asks about it.
        internal static string Line(string zone, int forward)
        {
            if (string.IsNullOrEmpty(zone)) return null;
            if (!Senpai(zone)) return file.Pick(zone, null);
            if (!Brain.Adult) return null;
            if (forward < Willing) return file.Pick(LineFile.Senpai(NotNow), null);
            return file.Pick(LineFile.Senpai(zone), null);
        }

        // The zones that are not in the public build. Something attaches them before her file is
        // written; with nothing attached the file simply ends after her ordinary ones.
        internal static string SenpaiTouch = "";

        internal static string Defaults { get { return
            "# Where you touch her\n" +
            "\n" +
            "> What she says for a hand in one place rather than another. A zone with nothing under it falls\n" +
            "> back to her ordinary lines for being petted, so emptying a section is how you switch one off.\n" +
            "> Her personality and her mood still colour everything else she says; these are the words themselves.\n" +
            "> Tokens: %petname% %name%\n" +
            "\n" +
            "## When you touch her head\n" +
            "\n" +
            "Mm. That's the good spot.\n" +
            "Careful, I'll fall asleep standing up.\n" +
            "You've done this before, haven't you.\n" +
            "Hair. Ruined. Worth it.\n" +
            "Again. That was a test and you passed.\n" +
            "That is unfairly effective.\n" +
            "Keep going and I'll start purring, and neither of us wants that.\n" +
            "Right behind the ear, yeah.\n" +
            "One more and I'm legally yours.\n" +
            "Oh, that's the spot. Mind the antenna.\n" +
            "Scratch behind the ear and I'll confess things.\n" +
            "I had plans today. They're cancelled.\n" +
            "My many minds all agree on this.\n" +
            "Careful. I'm storing this for later.\n" +
            "That's cheating. My hair's a lost cause now.\n" +
            "You've unlocked something. I don't know what.\n" +
            "\n" +
            "## When you touch her shoulders\n" +
            "\n" +
            "Oh, that's where I keep all the tension.\n" +
            "Right into that knot, go on. I've been sat here all day.\n" +
            "That's the one. Mind the shoulder blade.\n" +
            "You've found the knot. Congratulations, it's yours now.\n" +
            "That's nice. I'll allow it.\n" +
            "Careful, I'm mostly held together by that bit.\n" +
            "Lean on it. There's a knot under there.\n" +
            "There's a corpo's worth of stress in that one.\n" +
            "Go on, knuckle it out. I've earned it.\n" +
            "That's twelve hours of standing still, right there.\n" +
            "Mm. Keep that up and I owe you a coffee.\n" +
            "Dig in. I'm not made of glass. Mostly.\n" +
            "Oh, that's the knot. Keep at it.\n" +
            "Both hands, choom. It's a big knot.\n" +
            "That knot's been there since Tuesday.\n" +
            "You're better at this than you should be.\n" +
            "\n" +
            "## When you touch her middle\n" +
            "\n" +
            "Hey. Ticklish.\n" +
            "That's a lot of hand for a ribcage.\n" +
            "Hey! Warn me first.\n" +
            "I felt that in my whole chassis.\n" +
            "Rude. I'm getting you back for that.\n" +
            "Poke me once more and see what happens.\n" +
            "Hands. Where I can see them.\n" +
            "That's a lot of confidence for a Tuesday.\n" +
            "Careful, there's wiring under there.\n" +
            "Hey! I am fragile. Allegedly.\n" +
            "You're brave. Stupid, but brave.\n" +
            "One more and I'm filing a complaint.\n" +
            "My ribs are not a handle.\n" +
            "Hey. That's load bearing too.\n" +
            "I squeaked. We're not discussing it.\n" +
            "You get exactly one of those.\n" +
            "\n" +
            "## When you touch her legs\n" +
            "\n" +
            "Those are load bearing, you know.\n" +
            "Mm. I've been standing all day on those.\n" +
            "Careful. I bruise theatrically.\n" +
            "That tickles more than you'd think.\n" +
            "If you're checking the chrome's real, it is.\n" +
            "Twelve hours on these. Be kind.\n" +
            "Mind the knee. It clicks.\n" +
            "They're chrome under there, you know.\n" +
            "Careful, I kick on reflex.\n" +
            "Mm. That's the ache, right there.\n" +
            "All that standing about has to go somewhere.\n" +
            "Keep that up and I'll sit down properly.\n" +
            "These have walked Night City. Show some respect.\n" +
            "Hey, warn a girl.\n" +
            "That's a lot of hand for a kneecap, choom.\n" +
            "I'll allow it. Barely.\n" +
            "\n" +
            "## When you touch her feet\n" +
            "\n" +
            "Absolutely not.\n" +
            "Feet? Really? That's where we are?\n" +
            "You get one.\n" +
            "I'm going to pretend that didn't happen.\n" +
            "Bold. Weird. Noted.\n" +
            "Hey. I walk on those.\n" +
            "No. Next question.\n" +
            "That's a whole decision you've made.\n" +
            "That was your one.\n" +
            "I'm choosing to forget this.\n" +
            "Out of everything, you picked those.\n" +
            "Hey! Those do the walking.\n" +
            "We're going to pretend you didn't.\n" +
            "That's between you and whatever's left of your judgement.\n" +
            "Weird. Committed. Noted.\n" +
            "Absolutely not, gonk.\n" +
            "\n" +
            "## When you tickle her\n" +
            "\n" +
            "Hey! No. Absolutely not.\n" +
            "That's cheating and you know it.\n" +
            "I will remember this.\n" +
            "Stop, I'm made of circuits, this shouldn't work.\n" +
            "Betrayal. From you of all people.\n" +
            "No no no, not there.\n" +
            "I'm going to get you back for that.\n" +
            "That's not fair and I'm telling someone.\n" +
            "Right, that's it. War.\n" +
            "You've found a weakness. Forget it immediately.\n" +
            "Hands off, gonk.\n" +
            "I don't even know why that works.\n" +
            "\n" +
            SenpaiTouch; } }
    }
}
