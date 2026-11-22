using System;

namespace CapriDesktop
{
    // Where on her you put your hand, and what she says about it. Some zones are named by what attached and work only
    // with the switch on. Her forwardness decides: five or more and she's pleased, under that she moves your hand; on
    // air it is zero.
    internal static class Touch
    {
        internal const string FileName = "touch.md";

        // The ordinary zones, which exist whatever the switch says.
        internal const string Head = "When you touch her head";
        internal const string Shoulders = "When you touch her shoulders";
        internal const string Middle = "When you touch her middle";
        internal const string Legs = "When you touch her legs";
        internal const string Feet = "When you touch her feet";
        // What the bands over her chest and hips answer with while the switch is off, so a hand there meets a boundary,
        // not silence. The high five is a ctrl click with its own lines.
        internal const string Tickled = "When you tickle her";
        // The senpai ones, and what she says when she is not in the mood for them.
        internal static string Upper { get { return Extended.Get("upper"); } }
        internal static string Lower { get { return Extended.Get("lower"); } }
        internal static string ClickZone { get { return Extended.Get("click.zone"); } }
        internal const string NotNow = "When she is not in the mood";

        // The forwardness at which she welcomes a senpai zone rather than moving your hand.
        internal const int Willing = 5;

        private static readonly LineFile file = new LineFile(delegate { return Defaults; });

        internal static string Kept { get { return file.Kept; } set { file.Kept = value; } }
        internal static string Text { get { return file.Text; } }
        internal static string[] Sections() { return file.Sections(); }
        internal static WrittenNote Open() { return file.Open(); }
        internal static void Write(bool over) { file.Write(over); }
        internal static void Forget() { file.Forget(); }

        internal static bool Senpai(string zone)
        {
            return !string.IsNullOrEmpty(zone) && (zone == Upper || zone == Lower || zone == ClickZone);
        }

        // Her line for a zone, or null to leave her saying what she always said. A senpai zone with the switch
        // off never gets here: the band reads as nothing, so the caller never asks about it.
        internal static string Line(string zone, int forward)
        {
            if (string.IsNullOrEmpty(zone)) return null;
            if (!Senpai(zone)) return file.Pick(zone, null);
            if (!Brain.SenpaiLive) return null;
            if (forward < Willing) return file.Pick(LineFile.Senpai(NotNow), null);
            return file.Pick(LineFile.Senpai(zone), null);
        }

        // The lines she can say for a public zone, as her file holds them.
        internal static string[] Lines(string zone)
        {
            return string.IsNullOrEmpty(zone) || Senpai(zone) ? new string[0] : file.Lines(zone);
        }

        // The zones that are not in the public build. Something attaches them before her file is
        // written; with nothing attached the file simply ends after her ordinary ones.
        internal static string SenpaiTouch = "";

        internal static string Defaults { get { return
            "# Where you touch her\n" +
            "\n" +
            ">! What she says for a hand in each place.\n" +
            ">! An empty zone uses her ordinary petted lines, so empty one to switch it off.\n" +
            ">! Tokens: %petname% %name%\n" +
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
            Legacy.Modern(SenpaiTouch); } }
    }
}
