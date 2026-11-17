using System;

namespace CapriDesktop
{
    // What she says over a chess board, in a file of hers rather than nine arrays in the source.
    //
    // She had four or five lines a moment, picked with a plain roll, so anybody who plays her more than
    // twice hears the same taunt on the same move. Her other pools stopped being arrays for exactly this
    // reason; this is the last of them.
    //
    // Nine moments, read through the same recency as everything else she says, so a line does not come back
    // until its pool has been round. The lines the build ships are still in the source as the fallback, so
    // an emptied section leaves her saying the shipped one rather than nothing at all.
    internal static class ChessTalk
    {
        internal const string FileName = "chess.md";

        internal const string YourQueen = "When she takes your queen";
        internal const string HerQueen = "When you take her queen";
        internal const string YourCheck = "When she puts you in check";
        internal const string HerCheck = "When you put her in check";
        internal const string Won = "When she wins";
        internal const string Lost = "When she loses";
        internal const string Blunder = "When you blunder";
        internal const string Draw = "When it is a draw";
        internal const string Resign = "When you resign";

        // In the order ChessLines holds them, so a moment maps to a heading by position.
        internal static readonly string[] Headings = { YourQueen, HerQueen, YourCheck, HerCheck, Won, Lost, Blunder, Draw, Resign };

        private static readonly LineFile file = new LineFile(Defaults);

        internal static string Kept { get { return file.Kept; } set { file.Kept = value; } }
        internal static string Text { get { return file.Text; } }
        internal static WrittenNote Open() { return file.Open(); }
        internal static string[] Lines(string section) { return file.Lines(section); }
        internal static string[] Sections() { return file.Sections(); }
        internal static void Write(bool over) { file.Write(over); }
        internal static void Forget() { file.Forget(); }

        // Her line for a moment, or null when there is no such moment. Through her recency, with the build's
        // own lines underneath.
        internal static string Pick(int at, string[] shipped)
        {
            if (at < 0 || at >= Headings.Length) return null;
            return file.Pick(Headings[at], shipped);
        }

        internal const string Defaults =
            "# What she says over a chess board\n" +
            "\n" +
            "> One line each time something happens on the board. Add or change any of them.\n" +
            "> Delete a line to stop her saying it, or a whole section to fall back to the ones she ships.\n" +
            "> Nothing repeats until its section has been round.\n" +
            "> Tokens: %petname% %name%\n" +
            "\n" +
            "## When she takes your queen\n" +
            "\n" +
            "Your queen just joined my payroll.\n" +
            "Royalty has terrible survival instincts.\n" +
            "That's one expensive parking spot.\n" +
            "Queen collected. No receipt.\n" +
            "Night City takes its cut. So do I.\n" +
            "She was the only one of you with a plan.\n" +
            "Straight to the chrome shop with her.\n" +
            "I'll look after her. Define look after.\n" +
            "That is going to leave a hole in the budget.\n" +
            "Nine squares of regret, all at once.\n" +
            "You can have her back for a rook and an apology.\n" +
            "The crown changes hands quietly round here.\n" +
            "\n" +
            "## When you take her queen\n" +
            "\n" +
            "That was my favourite problem piece.\n" +
            "My queen has left the group chat.\n" +
            "You just made this personal. Mildly.\n" +
            "Fine. I'll delegate.\n" +
            "A hostile takeover. Cute.\n" +
            "Noted. Filed. Held against you.\n" +
            "She died doing what she loved: being overextended.\n" +
            "I had three plans. You took two of them.\n" +
            "Enjoy it. I have a long memory and a short temper.\n" +
            "Expensive, that. For both of us.\n" +
            "Right. The gloves were already off, but still.\n" +
            "You get one smug moment. That was it.\n" +
            "\n" +
            "## When she puts you in check\n" +
            "\n" +
            "Check. Your king has a visitor.\n" +
            "Your king's address just leaked.\n" +
            "Tiny crown. Large problem.\n" +
            "Check. Relocation might be wise.\n" +
            "Your security detail missed something.\n" +
            "Check. Take your time, I am comfortable.\n" +
            "Someone is knocking and it is not room service.\n" +
            "That square stopped being safe two moves ago.\n" +
            "Check. No rush. Well, some rush.\n" +
            "Your king looks tired. Of me, probably.\n" +
            "Check, %petname%. Sorry about the furniture.\n" +
            "I have been looking forward to this diagonal.\n" +
            "\n" +
            "## When you put her in check\n" +
            "\n" +
            "Check. Yes, I noticed.\n" +
            "My king enjoys unnecessary cardio.\n" +
            "That's inconveniently competent.\n" +
            "You found the executive suite.\n" +
            "Fine. Moving management.\n" +
            "Rude. Effective, but rude.\n" +
            "I saw that. I saw it late, but I saw it.\n" +
            "One moment, relocating the important one.\n" +
            "Who taught you that. It was not me.\n" +
            "I am going to pretend I allowed that.\n" +
            "Well spotted. Do not get used to it.\n" +
            "Noted, and I am already annoyed about the next bit.\n" +
            "\n" +
            "## When she wins\n" +
            "\n" +
            "Mate. I'll try to be unbearable quietly.\n" +
            "Your king's out of exits.\n" +
            "Board's mine. Ego also expanding.\n" +
            "Mate. No chrome required.\n" +
            "Good game, choom. Same time, same ego?\n" +
            "Mate. You made me work for about four moves of that.\n" +
            "That is the board, and I am being gracious about it.\n" +
            "Checkmate. I will stop talking about it eventually.\n" +
            "Done. Rematch whenever you have recovered.\n" +
            "Mate, %petname%. You played better than the result says.\n" +
            "The king is out of road. Good game.\n" +
            "I enjoyed that. You may not have.\n" +
            "\n" +
            "## When she loses\n" +
            "\n" +
            "Mate. You earned that smug face.\n" +
            "You got me. I'm keeping the board warm.\n" +
            "My other minds would like a recount.\n" +
            "That was clean. Annoyingly clean.\n" +
            "King down. Dignity buffering.\n" +
            "Well played. I mean it, which is worse.\n" +
            "You beat me properly. No excuses on offer.\n" +
            "Right. I am going to think about that one.\n" +
            "Good game. I want another immediately.\n" +
            "That was yours from about eight moves back.\n" +
            "Beaten by %petname%. I will survive it.\n" +
            "Take the win. I am taking notes.\n" +
            "\n" +
            "## When you blunder\n" +
            "\n" +
            "That move came with an invoice.\n" +
            "You left something expensive unattended.\n" +
            "I think your plan has a loose wire.\n" +
            "That looked better before the consequences.\n" +
            "I found a hole in your security.\n" +
            "Are you sure. You can be sure, I just wanted to check.\n" +
            "Interesting. Wrong, but interesting.\n" +
            "That piece was doing a job. It is not now.\n" +
            "I will wait while you look at that again.\n" +
            "Bold. We will see what it was.\n" +
            "Something just went unguarded and we both know it.\n" +
            "No notes. Well, one note.\n" +
            "\n" +
            "## When it is a draw\n" +
            "\n" +
            "Draw. Two egos, no receipt.\n" +
            "Nobody gets the crown today.\n" +
            "We're calling that mutual inconvenience.\n" +
            "All that plotting. Shared custody of nothing.\n" +
            "A draw. Deeply unsatisfying for both of us.\n" +
            "Neither of us blinked, so nobody won.\n" +
            "Honours even, which I resent slightly.\n" +
            "That is a draw. Let us never speak of it.\n" +
            "Equal. I would like it on record that I was close.\n" +
            "Nothing to show for it but the moves.\n" +
            "\n" +
            "## When you resign\n" +
            "\n" +
            "Resignation accepted. Board's still warm.\n" +
            "Fair. We can rerack whenever.\n" +
            "I'll take the point without a speech.\n" +
            "King clocks off. Understandable.\n" +
            "Taken. That was getting uncomfortable.\n" +
            "Sensible. I was enjoying it, mind.\n" +
            "Accepted, %petname%. Again when you like.\n" +
            "Fine by me. Good instinct, that.\n" +
            "The honourable exit. Respect.\n" +
            "Noted. Same again, and I will go easier. Probably not.\n";
    }
}
