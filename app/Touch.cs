using System;

namespace CapriDesktop
{
    // Where on her you put your hand, and what she says about it. Some zones are named by what attached and work only
    // with the switch on. Her forwardness decides: five or more and she's pleased, under that she moves your hand; on
    // air it is zero.
    internal static partial class Touch
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

        // The place a zone names, its words after "her": "head", "shoulders". Null for none.
        internal static string Part(string zone)
        {
            if (string.IsNullOrEmpty(zone)) return null;
            int her = zone.LastIndexOf(" her ", StringComparison.Ordinal);
            return (her < 0 ? zone : zone.Substring(her + 5)).Trim().ToLowerInvariant();
        }

        // What a ctrl click lands as with the switch on, by the place it hit: that place's act, now and then its other act,
        // its sound, what her brain is told and where her lines come from. Null off her body or with nothing attached.
        internal sealed class Clicked
        {
            internal string Act, Heard, Section;
            internal Cue Cue = Cue.Snap;
        }

        internal static Clicked Click(string zone, Func<string, int> worth, Random dice)
        {
            string part = Part(zone), first = Extended.Get("click");
            if (part == null || first == null) return null;
            string act = Extended.Get("click.at." + part) ?? first;
            string also = Extended.Get("click.also." + part);
            if (also != null && dice.NextDouble() < NowAndThen(worth(act), worth(also))) act = also;
            Cue cue;
            if (!Enum.TryParse(Extended.Get("click.cue." + act) ?? "", true, out cue) || !Enum.IsDefined(typeof(Cue), cue)) cue = Cue.Snap;
            return new Clicked
            {
                Act = act,
                Heard = Extended.Get("click.heard." + act + "." + part) ?? Extended.Get("click.heard." + act) ?? (act == first ? Extended.Get("click.heard") : null) ?? act,
                Section = Extended.Get("click.touch." + act + "." + part) ?? (act == first && zone == Lower ? ClickZone : null),
                Cue = cue
            };
        }

        // A ctrl click held a second with the switch on, by the place it landed: the act attached for it, and how she takes it
        // as a roll on her personality's stance for that act plus her mood's shift (0 to 2), each score with its own odds.
        internal sealed class Held
        {
            internal string Act, Heard;
            internal int Way;
            internal Cue Cue = Cue.Click;
        }

        internal const int Loves = 0, Takes = 1, Brushes = 2;
        internal static readonly string[] Ways = { "loves", "takes", "brushes" };

        internal static Held Hold(string part, string personality, string mood, Random dice)
        {
            string act = string.IsNullOrEmpty(part) ? null : Extended.Get("hold.at." + part);
            if (act == null) return null;
            int score = Score(act, personality, mood);
            int[] odds = Numbers(Extended.List("hold.odds." + score));
            int roll = dice.Next(Math.Max(1, odds[0] + odds[1] + odds[2])), way = roll < odds[0] ? Loves : roll < odds[0] + odds[1] ? Takes : Brushes;
            Cue cue;
            if (!Enum.TryParse(Extended.Get("hold.cue." + act) ?? "", true, out cue) || !Enum.IsDefined(typeof(Cue), cue)) cue = Cue.Click;
            return new Held { Act = act, Way = way, Heard = Extended.Get("hold.heard." + act) ?? act, Cue = cue };
        }

        // Her personality's stance for this act (its place in hold.order) and her mood's shift, kept 0 to 2. A personality
        // with no stance takes it (1).
        internal static int Score(string act, string personality, string mood)
        {
            string[] order = Extended.List("hold.order"), stance = Extended.List("hold.stance." + (personality ?? ""));
            int at = Array.FindIndex(order, one => string.Equals(one, act, StringComparison.OrdinalIgnoreCase)), own;
            if (at < 0 || at >= stance.Length || !int.TryParse(stance[at], out own)) own = 1;
            return Math.Max(0, Math.Min(2, own + Extended.Int("hold.shift." + (mood ?? ""), 0)));
        }

        private static int[] Numbers(string[] listed)
        {
            int[] made = { 25, 50, 25 };
            for (int i = 0; i < 3 && i < listed.Length; i++) { int one; if (int.TryParse(listed[i], out one) && one >= 0) made[i] = one; }
            return made;
        }

        // Her line for a hold, by how she took it: the act's lines for that way, dressed as her answers are.
        internal static string HeldSaid(Held held)
        {
            if (held == null) return null;
            string[] lines = Extended.List("hold.lines." + held.Act + "." + Ways[held.Way]);
            if (lines.Length == 0) return null;
            return Sayings.DressFrom(lines, held.Way != Brushes, Sayings.Kind.Reply, "extended: hold " + held.Act);
        }

        // A ctrl click's act welcome by her wanting or mood: "welcome.<act> = <wanting 0-10> | <mood> | ...".
        internal static bool Welcome(string act, int wanting, string mood)
        {
            string[] rule = Extended.List("welcome." + (act ?? ""));
            if (rule.Length == 0) return false;
            int least;
            if (int.TryParse(rule[0], out least) && wanting >= least) return true;
            for (int i = 1; i < rule.Length; i++) if (string.Equals(rule[i], mood, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // How often the other act comes instead: half her odds of leaning to it over the first, from what each is worth
        // to her now, so a personality and mood that like it more get it more, and it never outnumbers the first.
        internal static double NowAndThen(int first, int other) { return 0.5 / (1 + Math.Exp(first - other)); }

        // Her line for a place only what attached names: her lines for it when she wants it, her not-now lines when she
        // doesn't, built with the act's own pools the way her other reactions are. Null off the switch or with none.
        internal static string Said(string section, string act, bool wanted)
        {
            if (string.IsNullOrEmpty(section) || !Brain.SenpaiLive) return null;
            string[] hers = file.Lines(LineFile.Senpai(wanted ? section : NotNow));
            if (hers.Length == 0) return null;
            return Sayings.For(act, wanted, hers) ?? Sayings.DressFrom(hers, wanted, Sayings.Kind.Reply, "touch.md: " + (wanted ? section : NotNow));
        }

#if CHECKS
        // With stand-in keys, so the public check names nothing of the switch's own: a place with its own act gets it, the
        // rest the one act, off her body nothing; the other act comes now and then, more as she likes it more; each says
        // its own words and sound.
        internal static void Check(System.Collections.Generic.List<string> checks)
        {
            string was = Extended.Text;
            try
            {
                Extended.Text = "click = poked\nclick.heard = pokes you\nclick.at.feet = nudged\nclick.also.feet = poked\nclick.cue.nudged = Click\n"
                    + "click.heard.nudged.feet = nudges your feet\nclick.touch.nudged.feet = When you nudge her feet\n";
                Func<string, int> even = delegate { return 0; };
                Clicked head = Click(Head, even, new Random(1)), feet = Click(Feet, delegate(string act) { return act == "nudged" ? 5 : -5; }, new Random(1));
                if (head == null || head.Act != "poked" || head.Heard != "pokes you" || head.Cue != Cue.Snap || head.Section != null)
                    throw new Exception("A click on her head wasn't the one act with its words and snap.");
                if (feet == null || feet.Act != "nudged" || feet.Heard != "nudges your feet" || feet.Cue != Cue.Click || feet.Section != "When you nudge her feet")
                    throw new Exception("A click on her feet wasn't the act named for her feet, with its words, sound and lines.");
                if (Click(null, even, new Random(1)) != null) throw new Exception("A click off her body landed as something.");
                if (Part(Shoulders) != "shoulders" || Part(Head) != "head") throw new Exception("A zone's place is read wrong: " + Part(Shoulders));
                if (!(NowAndThen(0, 0) < 0.5 && NowAndThen(-3, 3) > NowAndThen(3, -3) && NowAndThen(-5, 5) <= 0.5))
                    throw new Exception("The other act's odds don't follow what she likes, or can outnumber the first.");
                int others = 0;
                Random dice = new Random(7);
                for (int i = 0; i < 400; i++) if (Click(Feet, even, dice).Act == "poked") others++;
                if (others < 60 || others > 140) throw new Exception("At even worth the other act came " + others + " in 400, not about a quarter.");
                checks.Add("PASS the switch-on ctrl click by place: a place's own act, words, sound and lines, the one act elsewhere, nothing off her body, and the place's other act now and then, more often the more she likes it and never past the first");
            }
            finally { Extended.Text = was; }
            LocalClicks(checks);
        }

        static partial void LocalClicks(System.Collections.Generic.List<string> checks);
#endif

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
