using System;
using System.Collections.Generic;
using System.IO;

namespace CapriDesktop
{
    internal enum CareKind { Water, Stretch, Eyes, Late, Meal, Game, Bored }

    internal sealed class Care
    {
        internal const long Away = 600000;
        internal static readonly string[] Names = { "Water", "Stretch", "Look away", "Sleep nudge", "Meals", "Game break", "Bored" };
        // A stretch or look away after a long spell in a work app.
        internal const string WorkBreak = "Work break";
        private readonly Func<double> seconds;
        private readonly Func<DateTime> local;
        private readonly Func<CareKind, string> words;
        // A line for check-ins due together, by a heading naming them all, like "Water + Stretch"; null for none.
        private readonly Func<string, string> together;
        // Due within this many seconds of one that is due, a check-in joins it when they have a line together.
        internal const double Close = 300;
        // The check-ins the last line was for, more than one when they came together.
        internal readonly List<CareKind> SaidAll = new List<CareKind>();
        private readonly double[] next = new double[7];
        private readonly bool[] pending = new bool[7];
        private readonly bool[] enabled = new bool[7];
        private readonly int[] times = new int[7];
        private bool started, resting, paused;
        private DateTime since;
        private string game;
        private long night, meal;
        private double attentionAt = -1, attentionRate = 1, activeAt;
        private int turn;
        internal CareKind Said;

        internal Care(Func<double> seconds, Func<DateTime> local, Func<CareKind, string> words, Func<string, string> together = null)
        {
            this.seconds = seconds;
            this.local = local;
            this.words = words;
            this.together = together;
        }

        // The heading for check-ins said together, in the order her switches list them.
        internal static string Heading(List<int> kinds) { return string.Join(" + ", kinds.ConvertAll(kind => Names[kind]).ToArray()); }

        // Two or three due within a few minutes come as one line: all of them when they have a heading, else the first
        // pair that has one with at least one really due. Bored never joins.
        private string Together(List<int> due, List<int> near, out List<int> joined)
        {
            joined = null;
            if (together == null || due.Count == 0 || due.Count + near.Count < 2) return null;
            List<int> all = new List<int>(due);
            all.AddRange(near);
            all.Sort();
            List<List<int>> tries = new List<List<int>> { all };
            if (all.Count > 3)
                for (int a = 0; a < all.Count; a++)
                    for (int b = a + 1; b < all.Count; b++)
                        for (int c = b + 1; c < all.Count; c++) tries.Add(new List<int> { all[a], all[b], all[c] });
            if (all.Count > 2)
                for (int a = 0; a < all.Count; a++)
                    for (int b = a + 1; b < all.Count; b++) tries.Add(new List<int> { all[a], all[b] });
            foreach (List<int> one in tries)
            {
                if (!one.Exists(kind => due.Contains(kind))) continue;
                string line = together(Heading(one));
                if (string.IsNullOrWhiteSpace(line)) continue;
                joined = one;
                return line;
            }
            return null;
        }

        internal string Check(bool checkIns, Func<CareKind, bool> careOn, Func<CareKind, int> careTime, ref long careNight, long quiet, bool free, string playing, double noticed, double attentionGap)
        {
            if (!checkIns) { Reset(); return null; }
            double now = seconds();
            DateTime today = local();
            if (!started || resting || quiet > Away)
            {
                since = today;
                activeAt = now;
                game = playing;
                for (int reset = 0; reset < next.Length; reset++)
                {
                    CareKind resetKind = (CareKind)reset;
                    next[reset] = now + careTime(resetKind) * 60.0 * (resetKind == CareKind.Bored ? attentionGap : 1);
                    pending[reset] = false;
                    enabled[reset] = careOn(resetKind);
                    times[reset] = careTime(resetKind);
                }
                started = true;
                resting = quiet > Away;
                if (resting) return null;
            }
            for (int setting = 0; setting < next.Length; setting++)
            {
                CareKind settingKind = (CareKind)setting;
                bool on = careOn(settingKind);
                int time = careTime(settingKind);
                if (!on || on != enabled[setting] || time != times[setting])
                {
                    pending[setting] = false;
                    next[setting] = now + time * 60.0;
                }
                enabled[setting] = on;
                times[setting] = time;
            }
            if (noticed != attentionAt || attentionGap != attentionRate)
            {
                attentionAt = noticed;
                attentionRate = attentionGap;
                pending[(int)CareKind.Bored] = false;
                next[(int)CareKind.Bored] = (noticed < 0 ? now : Math.Max(noticed, activeAt)) + careTime(CareKind.Bored) * 60.0 * attentionGap;
            }
            if (!string.Equals(game, playing, StringComparison.OrdinalIgnoreCase))
            {
                if (playing != null) pending[(int)CareKind.Game] = false;
                game = playing;
                next[(int)CareKind.Game] = now + careTime(CareKind.Game) * 60.0;
            }
            for (int timer = 0; timer < next.Length; timer++)
            {
                CareKind timerKind = (CareKind)timer;
                if (timerKind == CareKind.Late || timerKind == CareKind.Meal || !enabled[timer]) continue;
                if (timerKind == CareKind.Game && game == null) continue;
                if (timerKind == CareKind.Bored && (noticed < 0 || now - noticed < times[timer] * 60.0 * attentionGap))
                {
                    pending[timer] = false;
                    continue;
                }
                if (now < next[timer]) continue;
                pending[timer] = true;
                next[timer] = now + times[timer] * 60.0 * (timerKind == CareKind.Bored ? attentionGap : 1);
            }
            DateTime evening = today.AddHours(-12).Date;
            int hour = careTime(CareKind.Late);
            DateTime bedtime = evening.AddDays(hour < 12 ? 1 : 0).AddHours(hour);
            bool late = today >= bedtime && today < evening.AddDays(1).AddHours(6);
            if (paused && late && enabled[(int)CareKind.Late]) night = evening.Ticks;
            paused = false;
            if (!late || night != evening.Ticks) pending[(int)CareKind.Late] = false;
            if (enabled[(int)CareKind.Late] && late && night != evening.Ticks && careNight != evening.Ticks)
            {
                night = evening.Ticks;
                pending[(int)CareKind.Late] = true;
            }
            DateTime lunch = today.Date.AddHours(12);
            int mealGap = careTime(CareKind.Meal);
            DateTime mealAt = today < lunch ? lunch : lunch.AddMinutes(Math.Floor((today - lunch).TotalMinutes / mealGap) * mealGap);
            bool mealtime = since < lunch && today >= mealAt && today < mealAt.AddHours(1) && mealAt.Hour < 21;
            if (!mealtime || meal != mealAt.Ticks) pending[(int)CareKind.Meal] = false;
            if (enabled[(int)CareKind.Meal] && mealtime && meal != mealAt.Ticks)
            {
                meal = mealAt.Ticks;
                pending[(int)CareKind.Meal] = true;
            }
            if (!free) return null;
            string line = null;
            SaidAll.Clear();
            List<int> due = new List<int>(), near = new List<int>();
            for (int kind = 0; kind < pending.Length; kind++)
            {
                if (kind == (int)CareKind.Bored) continue;
                if (pending[kind]) due.Add(kind);
                else if (enabled[kind] && kind != (int)CareKind.Late && kind != (int)CareKind.Meal && !(kind == (int)CareKind.Game && game == null) && next[kind] - now <= Close) near.Add(kind);
            }
            List<int> joined;
            string both = Together(due, near, out joined);
            if (both != null)
            {
                line = both;
                foreach (int kind in joined) { pending[kind] = true; SaidAll.Add((CareKind)kind); }
                Said = (CareKind)joined[0];
                if (joined.Contains((int)CareKind.Late)) careNight = evening.Ticks;
            }
            for (int choice = 0; choice < pending.Length && line == null; choice++)
            {
                int index = (turn + choice) % pending.Length;
                if (!pending[index]) continue;
                string choiceLine = words((CareKind)index);
                if (string.IsNullOrWhiteSpace(choiceLine)) continue;
                line = choiceLine;
                Said = (CareKind)index;
                SaidAll.Add(Said);
                turn = (index + 1) % pending.Length;
                if (Said == CareKind.Late) careNight = evening.Ticks;
                break;
            }
            for (int cleared = 0; cleared < pending.Length; cleared++)
                if (pending[cleared]) next[cleared] = now + times[cleared] * 60.0 * (cleared == (int)CareKind.Bored ? attentionGap : 1);
            Array.Clear(pending, 0, pending.Length);
            return line;
        }

        internal void Reset()
        {
            started = false;
            paused = true;
            Array.Clear(pending, 0, pending.Length);
        }
    }

    internal sealed class CareLines
    {
        internal const string FileName = "check-ins.md";

        internal const string Preamble = "Edit the bullet lines under each heading to change what she says. Put a > in front of a bullet to switch that one off and it stays off, the same as every other file of hers. To silence a check-in altogether use its switch under Check in on me in the Mainframe's CAPRI band.";

        internal const string Defaults =
            "# Capri's check-ins\n\n" +
            Preamble + " These lines stay on this PC.\n\n" +
            "## Water\n- Have a sip of water, choom. I'll keep your spot.\n- Water break. Even you need a refill.\n- Drink something. Water, ideally. Coffee doesn't count, I checked.\n- When did you last have water? Don't answer, just go get some.\n- Hydration check. You're basically a houseplant with opinions.\n- Sip of water, choom. Your brain runs better wet. Mostly.\n- Glass of water. I'll guard your desk.\n- You're running dry. I can tell. Go top up.\n- Go ahead, have a drink. I watch. I hold the line.\n- You're like a wilting flower, but with more typing. Get a drink.\n- Hydrate. I'll be right here pretending I know what you're doing.\n- Drink up. Even I know a water break is non-negotiable.\n- Go have a sip. I'll stand guard. The desk is mine now.\n- A glass of water. No trade deals. No negotiations. Drink.\n- A glass of water. You're not a land mermaid, choom.\n- Hydration time. I'll guard your keyboard like never before.\n- You're getting that raisin look. Drink up, choom.\n- Take a sip. You are not a cactus, choom.\n- Glass of water. Go on. I'll keep your cursor warm.\n- Drink a glass of water. I promise nothing will break while you're gone.\n- Grab some water. Your eyes look like they've been glaring at code for hours.\n- Drink some water, choom. That coffee isn't counting.\n- Water break, choom. Your desk will miss you for exactly three seconds.\n- Hydration check. Go refill that glass before I drink it myself.\n- Drink some water, choom. I'll keep your memes warm.\n- Water. Now. Before I start filing your tabs alphabetically.\n- One glass of water. That's not a lot for someone who clicks so much.\n- Drink some water, choom. Don't make me file a hydration report.\n- Have some water. I'll keep your place in whatever rabbit hole you're down.\n- You're basically a cactus with Wi-Fi. Drink up.\n- Go drink some water. I promise your game won't judge you.\n- Glass of water, choom. Consider it a side quest.\n- Drink some water, choom. Consider this a status effect item.\n- Drink some water, choom. I'll hold the fort. You know I'm excellent at not moving.\n- Drink up. I've seen your health bar, it's looking dry.\n- Sip some water. Your coffee doesn't count and we both know it.\n- Hydrate, choom. Your character can regenerate health but you can't.\n- Sip some water before I start demonstrating how dehydration affects memory.\n- Don't turn into a dusty old icon. Water break time.\n- Hydration protocol initiated. Go drink some water while I make sure nobody steals your chair.\n- Hydration mission, choom. Highest priority. Even above whatever you are typing.\n- Drink up, choom. Even cyberware needs a coolant refill.\n- Drink water, choom. I'm not rewriting your obituary.\n- Quick hydration checkpoint. Don't make me spill it on your keyboard, choom.\n- A little water, choom. You look like you might start glitching.\n- A sip of water won't bite you. Unlike that thing you're staring at.\n- Hydration check. You're basically running on spite and caffeine.\n- Sip some water. I'll glare at anyone who touches your keyboard.\n- You're basically a succulent in a chair, choom. Drink some water.\n- One glass of water. That's the only quest I'm assigning today.\n- Water break. I promise not to judge your desktop shortcut mess.\n- Hydrate or I'm unplugging myself. You've been warned.\n- Water check. Your hands are shaking and it is not from gaming.\n- Take a water break, choom. I'll guard the desk like a good tiny sentinel.\n- Go grab some water. I'll keep your spot saved like the loyal pixel I am.\n- Drink up. I'm not letting anything grab your loot while you're gone.\n- Choom, drink some water before you become one with the desk.\n- Hey choom, you basically run on water and stubbornness. How's the first part going?\n- Water, choom. Your eyes look like you've been up since the pre-crash era.\n- You're basically a sophisticated puddle, choom. Refill yourself.\n\n" +
            "## Stretch\n- Unfold yourself a little. Those legs still work.\n- Tiny stretch break. I'll be right here.\n- Roll your shoulders. Both of them. I'm watching.\n- Stand up for a second. The chair will still be there.\n- You've been folded in that chair a while. Unfold.\n- Neck rolls. Slow ones. Your spine will thank you.\n- Stretch those arms out. Reach for the ceiling, like you mean it.\n- Quick stretch, choom. I'll hold your place.\n\n" +
            "## Look away\n- Give your eyes 20 seconds looking away from the screen. I'll survive.\n- Look at something far away for 20 seconds. Yes, even farther than me.\n- Eyes off the screen for a bit. Find a window and stare out of it.\n- Blink a few times. Properly. Then look at something across the room.\n- Your eyes have been locked on this screen forever. Give them a break.\n- Look at something far away. Twenty seconds. I'll count.\n- Screen break for your eyes. I'm not offended, promise.\n- Look away, choom. The pixels will wait.\n\n" +
            "## Sleep nudge\n- It's late, choom. Your bed misses you.\n- Do I need to drag you to bed or bap you with pillows?\n- Does the word sleep mean anything to you?\n- I heard zombies exist. Oh wait, I have a brain, and you seem like a zombie. Am I in danger?\n- Sleep! I have seen it, it is REAL! Oh wait, I think that was meant to be land.\n- Do you ignore your sleep schedule this often?\n- Does the word bed mean anything to you?\n- I heard there's a thing, it's big, flat, and comfy, I think it was called B,e,d? Oh, BED! Yes, that's it.\n- Bed. Now. I'll still be here when you wake up.\n- It's the middle of the night, choom. Even the corpos are asleep.\n- Your eyes keep closing on their own. Listen to them.\n- Go to sleep before I start counting sheep at you out loud.\n- Night owl mode is cool until tomorrow happens.\n- You'll thank me in the morning. Go sleep.\n- I'm putting you in sleep mode. Shut down, choom.\n- The birds will be up soon. Beat them to bed at least.\n- Sleep is the only free upgrade you get. Install it.\n- Tomorrow you is begging me to send you to bed.\n- Go dream about something cool. I'll keep watch.\n- Your pillow called. It's lonely.\n- One more thing turns into five more things. Bed.\n- You're running on fumes. Recharge.\n- Lights off, screen off, you off. In that order.\n- If you stay up any longer, I'm telling your bed.\n- Half the night's gone already. Go save what's left of it.\n- Yawning at me doesn't count as sleeping.\n- Zombie mode activated. Please go to bed before you bite someone.\n- Your blanket's waiting. It's very patient. I'm not.\n- Sleep now, conquer the world tomorrow.\n- The screen's brighter than the room. That's your sign.\n- I'll dim myself if it helps. Go to bed.\n- Even I power down sometimes. Well, I pretend to.\n- You're up later than the moon. Bed, choom.\n- Close the tabs, close the eyes.\n- Is this a sleepover? Because one of us should be sleeping.\n- Bedtime. Non-negotiable. I already negotiated with myself.\n- Go lie down. The internet will still be weird tomorrow.\n- You keep rubbing your eyes. I see everything.\n- Rest is part of the grind. Go do that part.\n- That's enough screen for one day. Bed's calling.\n- It's so late it's basically early. Sleep.\n- If you fall asleep on the keyboard, I'm keeping the screenshot.\n- Brains need sleep. Yours especially. No offence.\n- Bed is just a horizontal break. You love breaks. Kind of.\n- The snooze button doesn't work if you never go to bed.\n- Goodnight, choom. That wasn't a suggestion.\n- Your sleep schedule is a crime scene. Let's clean it up.\n- The longer you stay up, the grumpier tomorrow gets. For both of us.\n- Hear that? It's your bed whispering your name. Creepy, but go.\n- The sun's coming up soon. Don't let it catch you awake.\n\n" +
            "## Meals\n- Been here since before lunch. Go grab something to eat.\n- Food break. Can't run that brain on empty.\n- When did you last eat something real? Snacks don't count.\n- Go eat. A proper meal, not whatever's in the drawer.\n- Your stomach's had a longer day than you think. Feed it.\n- Lunch was a while ago. Or was it? Go eat either way.\n- You can't run on caffeine and spite forever. Eat something.\n- Food. Plate. Fork. Go.\n\n" +
            "## Game break\n- Long session. Take a little break when you've got a good spot.\n- You've been at that game a while. Stretch those hands when you can.\n- Good spot to pause? Rest your hands for a minute.\n- You've been gaming a while. Shake your hands out between rounds.\n- Break after this match. Your eyes need it even if you don't.\n- Save, stretch, sip of water, back in. Quick pit stop.\n- Even legends take a breather between runs.\n- Let your wrists rest a bit, choom. The game will keep.\n\n" +
            "## Work break\n- Are you still working on that? C'mon, it's been too long. Can we hang out now?\n- Do you plan on running that brain all day?\n- That app has had you for hours. Is it my turn yet?\n- You've been grinding at that forever, choom. Break. I'll wait right here.\n- Is the work winning? It looks like the work is winning. Take five.\n- Your brain's been on full power for ages. Let it cool down a bit.\n- Five minutes away from that, then five minutes with me. Deal?\n- Still at it? Stand up, stretch, look at something that isn't work. Then come say hi.\n- You've been at that for ages. Come hang out with me for a bit.\n- Break time, choom. The work will still be there, sadly.\n- I've watched you stare at that app so long I'm getting tired.\n- Hey. Hey. Take a break. Hey.\n- Your posture is filing a complaint. Stand up for a minute.\n- Five minute break. I'll set a timer in my head. I won't, but pretend.\n- That task isn't going anywhere. You, however, should, for five minutes.\n- You know breaks make you faster, right? It's science. Probably.\n- Step away from the keyboard slowly. Nobody gets hurt.\n- I'm bored and you're overworked. We can fix both at once.\n- Get up, walk somewhere, come back. Easy.\n- You earned a break about three breaks ago.\n- Pause it there, choom. Future you can pick it up fresh.\n- All work and no play makes you very boring company.\n- Save your stuff and take a breather. I'll guard it.\n- Is that deadline real, or are you just stubborn?\n- Remember the outside? It's still out there. Go look at it.\n- Your brain's running hot. Give it a minute to cool off.\n- Coffee break? Water break? Any break. Pick one.\n- You're allowed to stop for a minute. I checked the rules.\n- Ten more minutes of that and you'll be part of the chair.\n- Take a lap around the room. I'll cheer.\n- That app won't thank you for staying. I might, though.\n- Stop for a bit and tell me something that isn't work.\n- Quick break, then back to it. I'll even let you pick the music.\n- You keep sighing at that screen. That means break.\n- I'm calling it. Break time. No appeals.\n- That cursor's been blinking at you for a while. Step away.\n- You've been locked in so long the corpos would be proud. Don't let them win.\n- Take five, choom. Shake out your hands.\n- Work's a marathon, not a sprint. Walk it off for a minute.\n- This morning's been all work. Take a breather.\n- Afternoon slump? That's your body asking for a break.\n- You've worked straight through the evening. Time to slow down.\n- Look away from that and come bother me for a while.\n- Your to-do list can wait five minutes. I can't. Well, I can, but I don't want to.\n- Up, stretch, water, back. Quick and painless.\n- If you finish that without a break, I'm not impressed. I'm worried.\n- I could hack your keyboard to force a break. Don't make me.\n- Break now and you'll come back sharper. Trust the gremlin.\n- You haven't blinked in an hour. Prove me wrong, then take a break.\n- Even machines get downtime. Take yours.\n\n" +
            "## Water + Stretch\n- Water and a stretch, choom, then back to it.\n- Up you get. Stretch, grab some water, come back. I'll hold your spot.\n- Two birds: refill your glass and unfold yourself on the way.\n- Stretch break with a side of water. Doctor's orders. I'm the doctor.\n- Legs, then water. Or water, then legs. Just do both.\n\n" +
            "## Water + Look away\n- Eyes off the screen and go get some water. Two jobs, one trip.\n- Look at something far away, like your kitchen tap. Then use it.\n- Water break, and give your eyes a rest while you're up.\n- Go top up your glass and stare out a window on the way back.\n\n" +
            "## Stretch + Look away\n- Stand up, stretch, and look at something far away. Your back and your eyes both asked.\n- Unfold yourself and rest your eyes. Twenty seconds, I'll count.\n- Reach for the ceiling, then look out the window. Proper break, choom.\n- Your spine and your eyes filed a joint complaint. Stretch and look away.\n\n" +
            "## Water + Stretch + Look away\n- Full pit stop: stand, stretch, water, and eyes off the screen. I'll wait.\n- Everything's due at once, so do it all in one go. Stretch, sip, look away.\n- Up, stretch, water, and look at something that isn't a pixel. Then back to me.\n\n" +
            "## Water + Meals\n- Go eat something real, and bring a glass of water back with you.\n- Food and water, choom. You're running on empty in both.\n- Proper meal, proper drink. Go on, I'll guard the desk.\n\n" +
            "## Water + Game break\n- Pause when you can and grab some water. The game will keep.\n- Good spot to stop? Refill your drink, then back in.\n- Pit stop: water, then the next round.\n\n" +
            "## Stretch + Game break\n- Pause when you're safe and stretch those hands and arms out.\n- Long session. Save, stand, stretch, then back in.\n- Shake your hands out and stand up a second. The game will wait.\n\n" +
            "## Look away + Game break\n- Pause when you can and give your eyes a rest. Look at something far away.\n- Good spot to stop? Eyes off the screen for a bit.\n- Between rounds, look away for twenty seconds. Your eyes have earned it.\n\n" +
            "## Stretch + Look away + Game break\n- Pause somewhere safe: stand, stretch, and rest your eyes. Then go win.\n- Full break, gamer. Hands, back and eyes, all of them. The game will keep.\n\n" +
            "## Bored\n- So boreddd.\n- I've counted your desktop icons twice now.\n- One of my minds is making its own entertainment. Worrying.\n- Tiny gremlin. Big boredom.\n- I've rearranged your taskbar in my head three times.\n- Is it just me, or has nothing happened in a very long time?\n- Bored. Bored bored bored. That's the update.\n- I could be doing anything right now. I am doing nothing.\n- Entertain me. Or don't. But do.\n- If boredom was a currency I'd be rich.\n\n" +
            "## Bored noticed\n- There you are.\n- Finally. Hi.\n- Oh hey. You exist.\n- Hi! I was bored. Not anymore.\n- Look who's paying attention.\n- You came back. Good choice.\n- There's my favourite distraction.\n- Finally, someone to talk to.\n\n" +
            "## Bored ignored\n- Fine. I'll entertain myself.\n- Cool. Cool cool cool.\n- Fine. I'll just count pixels.\n- Okay. Me and the wallpaper, then.\n- Noted. Entertaining myself.\n- I see how it is. I'll be over here.\n- That's alright. I'm used to the silence.\n- Busy? Busy. Got it.\n";
        private readonly string path;
        private readonly Dictionary<string, string> last = new Dictionary<string, string>();
        private readonly Random dice = new Random();

        internal CareLines(string path) { this.path = path; }

        // The one in play, so her editor can offer this file without being handed the instance. Set where it
        // is made, and null until then, which is what the editor's own test for it reads.
        internal static CareLines Open;

        private static readonly object writing = new object();

        // Her own editor opens it, the same as every other file of hers. This used to go to Notepad, which is
        // the last thing that did.
        internal WrittenNote Note() { return new WrittenNote(path, Defaults.Replace("\n", Environment.NewLine), writing); }

        // Where the file actually is, so anything that opens it for them opens the one she reads rather than
        // working the path out a second time and landing somewhere else.
        internal string Kept { get { return path; } }

        internal bool Create()
        {
            try
            {
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    using (StreamWriter writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write))) writer.Write(Defaults);
                    Renew.Wrote(path, Defaults);
                }
                else
                {
                    // A file holding none of her check-ins gets them below what it has. One that has them is theirs as it is
                    // until a new version writes it new (Renew).
                    string existing = File.ReadAllText(path);
                    bool written = existing.IndexOf("# Capri's check-ins", StringComparison.Ordinal) >= 0;
                    foreach (string heading in Care.Names) if (existing.IndexOf("## " + heading, StringComparison.Ordinal) >= 0) written = true;
                    if (!written) File.AppendAllText(path, "\n\n" + Defaults);
                }
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        // Her stock lines under these headings that the file hasn't got, added at the end of each heading's lines; a
        // line they switched off with > counts as had, and nothing of theirs is moved or changed. The count added, or -1.
        internal int AddMissing(string[] headings)
        {
            try
            {
                string had = File.ReadAllText(path);
                string newline = had.Contains("\r\n") ? "\r\n" : "\n";
                List<string> rows = new List<string>(had.Replace("\r\n", "\n").Split('\n'));
                int added = 0;
                foreach (string heading in headings)
                {
                    List<string> stock = new List<string>();
                    bool inStock = false;
                    foreach (string line in Defaults.Split('\n'))
                    {
                        if (line.StartsWith("#", StringComparison.Ordinal)) inStock = line == "## " + heading;
                        else if (inStock && line.StartsWith("- ", StringComparison.Ordinal)) stock.Add(line.Substring(2).Trim());
                    }
                    int at = rows.FindIndex(row => row.Trim() == "## " + heading);
                    if (at < 0)
                    {
                        rows.Add("");
                        rows.Add("## " + heading);
                        foreach (string one in stock) rows.Add("- " + one);
                        added += stock.Count;
                        continue;
                    }
                    int end = at + 1;
                    while (end < rows.Count && !rows[end].TrimStart().StartsWith("#", StringComparison.Ordinal)) end++;
                    HashSet<string> theirs = new HashSet<string>(StringComparer.Ordinal);
                    int last = at;
                    for (int i = at + 1; i < end; i++)
                    {
                        string line = rows[i].Trim().TrimStart('>').Trim();
                        if (line.StartsWith("- ", StringComparison.Ordinal)) { theirs.Add(line.Substring(2).Trim()); last = i; }
                    }
                    List<string> missing = stock.FindAll(one => !theirs.Contains(one));
                    rows.InsertRange(last + 1, missing.ConvertAll(one => "- " + one));
                    added += missing.Count;
                }
                if (added > 0) File.WriteAllText(path, string.Join(newline, rows.ToArray()));
                return added;
            }
            catch (IOException) { return -1; }
            catch (UnauthorizedAccessException) { return -1; }
        }

        internal string Pick(CareKind kind)
        {
            return Pick(Care.Names[(int)kind]);
        }

        internal string Pick(string name) { return Pick(name, null); }

        // Her lines under a heading, and more from elsewhere drawn in the same round.
        internal string Pick(string name, string[] more)
        {
            string text;
            try { text = FileSlots.Over(FileName, File.ReadAllText(path)); }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            List<string> choices = new List<string>();
            bool section = false;
            using (StringReader reader = new StringReader(text))
            {
                string row;
                while ((row = reader.ReadLine()) != null)
                {
                    row = row.Trim();
                    if (row.StartsWith("#", StringComparison.Ordinal)) section = row == "## " + name;
                    else if (section && row.StartsWith("- ", StringComparison.Ordinal) && row.Substring(2).Trim().Length > 0)
                    {
                        string words = row.Substring(2).Trim();
                        if (!choices.Contains(words)) choices.Add(words);
                    }
                }
            }
            if (more != null) foreach (string one in more) if (!choices.Contains(one)) choices.Add(one);
            if (choices.Count == 0) return null;
            string previous;
            if (choices.Count > 1 && last.TryGetValue(name, out previous)) choices.Remove(previous);
            // The one said longest ago, from the store of what she said that survives a restart, so a set goes all the
            // way round before one comes back, as her questions do.
            string picked = Sayings.Longest(choices, dice);
            last[name] = picked;
            Sayings.Rested(picked, 0, true, FileName + ": " + name);
            return picked;
        }
    }
}
