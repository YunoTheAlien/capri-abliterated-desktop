using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Her own Settings panels as tree files, built through the same Section, Make, Switch and Setting as before, so each
    // looks as it did. Each built piece is kept by its name for her fields to take.
    internal sealed partial class Mainframe
    {
        // The faint small type her captions use, given by the page that builds them.
        private Font captionFont;

        private Control TreeHerGrid(PanelTree.Row panel, TreeWiring wire, Dictionary<string, Control> built, int x, int top)
        {
            // A panel built in parts, her own code between them: the parts after the first carry no heading of their own.
            Head head = herHeadless ? null : Section(panel.Name + " >", x, top);
            if (head != null) built[panel.Name] = head;
            int y = top + 24, band = 28 + x + 328;
            foreach (PanelTree.Row row in panel.Under)
            {
                string kind = row.Kind ?? "switch";
                Action run = row.Run;
                // Where a row sits when her grid's next line isn't it: x from the column, the row's own y on the page, a width
                // and a height; any other words are its text (a dropdown's pick). A plain number is a small key's width.
                int px, py, pw, ph;
                string words = Placed(row.Words, out px, out py, out pw, out ph);
                if (py >= 0) y = py;
                if (kind == "buttonSmall")
                {
                    int wide = pw > 0 ? pw : 100;
                    int left = px >= 0 ? px + x : band - wide;
                    if (px < 0) band = left;
                    Key small = Make(row.Name, left, py >= 0 ? py : top - 3, wide, ph > 0 ? ph : 20, delegate { if (run != null) run(); });
                    // A hidden one shows on the band only for a moment (KEYS' Confirm while a key is picked), so it stays there.
                    if (row.Flags.Contains("hidden")) { small.Visible = false; onBand.Add(small); }
                    built[row.Name] = small;
                    if (px < 0) band -= 6;
                    continue;
                }
                if (kind == "switch")
                {
                    Toggle toggle = Switch(row.Name, x, y, delegate { if (!syncing && run != null) run(); });
                    built[row.Name] = toggle;
                    // A key under a switch: the switch narrows and the key sits at the row's end, dropping its own choices.
                    foreach (PanelTree.Row pick in row.Under)
                    {
                        Action choose = pick.Run;
                        toggle.Width = 224;
                        toggle.Switch = Toggle.Narrow;
                        Key key = Make(pick.Name, 260 + x, y + 3, 96, 22, delegate { if (choose != null) choose(); });
                        key.Chevron = true;
                        built[pick.Name] = key;
                    }
                }
                else if (kind == "sliderRow")
                {
                    string[] steps = row.Steps ?? new string[] { "" };
                    Slider slider = Setting(row.Name, steps, x, y, row.Name);
                    slider.Changed += delegate { if (!syncing && row.SetLevel != null) row.SetLevel(slider.Value); };
                    built[row.Name] = slider;
                }
                else if (kind == "buttonLarge") built[row.Name] = Make(row.Name, 28 + x, y + 3, 328, ph > 0 ? ph : 22, delegate { if (run != null) run(); });
                else if (kind == "caption") built[row.Name] = Caption(row.Name, 28 + x, y + 4, captionFont, Faint);
                // A dropdown: its name on the left as a setting's is, and a key in the value box saying what is picked.
                else if (kind == "dropdown")
                {
                    Named(row.Name, x, y);
                    Key picks = Make(words, ValueAt + x, y + 3, ValueWide, 22, delegate { if (run != null) run(); }, row.Name);
                    picks.Chevron = true;
                    built[row.Name] = picks;
                }
                // Two keys side by side in the value box under the row's name: a light pair's halves from its words ("No cloud /
                // No local"), lit only by her, or a key row's keys, each pressing its own.
                else if (kind == "lightPair" || kind == "buttonRow")
                {
                    Named(row.Name, x, y);
                    List<string> names = new List<string>(), placed = new List<string>();
                    List<Action> presses = new List<Action>();
                    if (kind == "lightPair") foreach (string part in words.Split('/')) { names.Add(part.Trim()); presses.Add(null); placed.Add(""); }
                    else foreach (PanelTree.Row one in row.Under) { names.Add(one.Name); presses.Add(one.Run); placed.Add(one.Words); }
                    int half = (ValueWide - 6) / 2;
                    for (int i = 0; i < names.Count && i < 2; i++)
                    {
                        Action press = presses[i];
                        // A key with its own place sits there and has its own tip; the others halve the value box.
                        int kx, ky, kw, kh;
                        Placed(placed[i], out kx, out ky, out kw, out kh);
                        Key one = kx >= 0
                            ? Make(names[i], kx + x, ky >= 0 ? ky : y + 3, kw > 0 ? kw : half, kh > 0 ? kh : 22, delegate { if (press != null) press(); })
                            : Make(names[i], ValueAt + x + i * ((ValueWide + 6) / 2), y + 3, half, 22, delegate { if (press != null) press(); }, row.Name);
                        built[kind == "lightPair" ? row.Name + " " + (i + 1) : names[i]] = one;
                    }
                }
                // A text box: the row's words are its cue; a key beside it is a row of its own with place words. One set in the
                // value box has its row's name on the left, as a setting's.
                else if (kind == "entry")
                {
                    if (px > 0) Named(row.Name, x, y);
                    built[row.Name] = TreeEntry(row, wire, new Rectangle(28 + x + Math.Max(0, px), y + 3, pw > 0 ? pw : 244, ph > 0 ? ph : 22));
                }
                else { wire.Log("line " + row.Line + ": [" + kind + "] is not on her grid yet"); continue; }
                y += Row;
            }
            return head;
        }

        // A row's place words taken out of its words: "x104 y653 w112 h20", each -1 when not given; a lone number is a width.
        private static string Placed(string words, out int x, out int y, out int w, out int h)
        {
            x = y = w = h = -1;
            List<string> rest = new List<string>();
            foreach (string part in (words ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int n;
                if (part.Length > 1 && "xywh".IndexOf(part[0]) >= 0 && int.TryParse(part.Substring(1), out n))
                {
                    if (part[0] == 'x') x = n; else if (part[0] == 'y') y = n; else if (part[0] == 'w') w = n; else h = n;
                }
                else if (int.TryParse(part, out n) && w < 0 && rest.Count == 0) w = n;
                else rest.Add(part);
            }
            return string.Join(" ", rest.ToArray());
        }

        // Her own panels' trees, carried in her: a panel a tree, her pieces in brackets, what each reaches in round ones.
        internal const string MovesTree = "settings\n-MOVES\n--Pop-in times [buttonSmall] x28 w328 h22 (command: pop times)\n--Mini player [switch] (command: mini player)\n"
            + "--Wander [sliderRow] (setting: wander)\n--Sleep [sliderRow] (setting: sleep)\n--Pop-ins [sliderRow] (setting: pop-ins)\n"
            + "--Dance to music [switch] (command: dance)\n--Glitch in at startup [switch] (command: intro)\n";

        // KEYS: Confirm, Cancel and Off on its band only while a key is being picked, right to left as listed.
        internal const string KeysTree = "settings\n-KEYS\n--Talk: Ctrl + Alt + V [buttonLarge] (command: talk key)\n--Mute mic: Ctrl + Alt + N [buttonLarge] (command: mute key)\n"
            + "--Type: Ctrl + Alt + T [buttonLarge] (command: type key)\n--Hide/show: Ctrl + Alt + H [buttonLarge] (command: hide key)\n"
            + "--Quiet: Ctrl + Alt + P [buttonLarge] (command: quiet key)\n--Push to talk: not set [buttonLarge] (command: push key)\n"
            + "--Gaming Mode: Ctrl + Alt + G [buttonLarge] (command: gaming key)\n--Mainframe: Ctrl + Alt + M [buttonLarge] (command: mainframe key)\n"
            + "--Keys note [caption]\n--Cancel [buttonSmall, hidden] 60 (command: cancel keys)\n--Confirm [buttonSmall, hidden] 78 (command: confirm keys)\n"
            + "--Off [buttonSmall, hidden] 46 (command: push off)\n";

        internal const string GeneralTree = "settings\n-GENERAL SETTINGS\n--Save Mainframe position [switch] (command: remember panel)\n"
            + "--Always on top [switch] (command: on top)\n--Mainframe size [sliderRow] (setting: mainframe size)\n"
            + "--Mainframe colours [sliderRow] (setting: mainframe colours)\n--Custom colours... [buttonLarge] (command: custom colours)\n";

        internal const string WatchTree = "settings\n-WATCH\n--Watch my screen [switch] (command: watch)\n--Screens [dropdown] All screens (command: screens)\n"
            + "--Remember what she sees [switch] (command: caught up)\n--Every [sliderRow] (setting: every)\n--Watch-along memory [sliderRow] (setting: watch memory)\n";

        // HABITS: its three keys on the heading's band, Power saver and Perch on one line (her page shows one of them), the
        // rows after Perch at their own places.
        internal const string HabitsTree = "settings\n-HABITS\n--Obsidian vault [buttonSmall] x104 w112 (command: vault)\n"
            + "--Open her memories [buttonSmall] x220 w136 (command: memories)\n--Her GIFs [buttonSmall] x298 w58 (command: gifs)\n"
            + "--Start with Windows [switch] (command: startup)\n--Show on Discord [switch] (command: discord)\n--Use her cursor [switch] (command: cursor)\n"
            + "--Remember our chats [switch] (command: remembering)\n--Security [buttonLarge] (command: security)\n--Security note [caption] y792\n"
            + "--Power saver [sliderRow] y820 (setting: power saver)\n--Music app [sliderRow] (setting: music app)\n"
            + "--Perch [sliderRow] y820 (setting: perch)\n--Keep PC awake for jobs [switch] y848 (command: keep awake)\n"
            + "--Hide in full screen [switch] (command: hide full screen)\n--Mini player over full screen [switch] (command: player full screen)\n"
            + "--Explore while I'm away [switch] (command: explore away)\n--Explore after [sliderRow] y994 (setting: explore after)\n";

        // CAPRI in three parts, her Quiet rows' code between the first two; each part's rows are moved below as before.
        internal const string CapriTree = "settings\n-CAPRI\n--Hologram [switch] (command: hologram)\n--Better conversations [switch] (command: better conversations)\n--Toggles... [buttonLarge] (command: toggles)\n";
        internal const string CapriCareTree = "settings\n-CAPRI\n--Size [sliderRow] y82 (setting: size)\n--Check in on me [switch] (command: check ins)\n"
            + "--Water [switch] (command: care water)\n---Water time [buttonSmall] (command: care time water)\n"
            + "--Stretch [switch] (command: care stretch)\n---Stretch time [buttonSmall] (command: care time stretch)\n"
            + "--Look away [switch] (command: care look away)\n---Look away time [buttonSmall] (command: care time look away)\n"
            + "--Sleep nudge [switch] (command: care sleep nudge)\n---Sleep nudge time [buttonSmall] (command: care time sleep nudge)\n"
            + "--Meals [switch] (command: care meals)\n---Meals time [buttonSmall] (command: care time meals)\n"
            + "--Game break [switch] (command: care game break)\n---Game break time [buttonSmall] (command: care time game break)\n"
            + "--Bored [switch] (command: care bored)\n---Bored time [buttonSmall] (command: care time bored)\n"
            + "--Edit check-in lines [buttonLarge] y335 h20 (command: edit check-ins)\n";
        internal const string CapriPatsTree = "settings\n-CAPRI\n--Head pats [switch] y138 (command: head pats)\n--Pat stroke [sliderRow] (setting: pat stroke)\n"
            + "--Chaos when upset [switch] (command: chaos)\n--Legacy stand [switch] (command: legacy stand)\n--Jealous of other AIs [switch] (command: jealous)\n"
            + "--Close other AIs for me [switch] (command: close ais)\n--Apps she may close [dropdown] Choose apps (command: choose apps)\n";

        // BRAIN on the right in parts, her code between them where its rows are moved or are not yet tree pieces.
        internal const string BrainHuskTree = "settings\n-BRAIN\n--Code/Husk Mode [dropdown] y354 Off (command: husk)\n--Thinking [dropdown] (command: thinking)\n"
            + "--Extra request JSON [dropdown] Add (command: extra json)\n";
        internal const string BrainVerbosityTree = "settings\n-BRAIN\n--Verbosity [switch] y354 (command: verbosity)\n";
        internal const string BrainRepliesTree = "settings\n-BRAIN\n--Cloud model lock [switch] y354 (command: model lock)\n--Replies [sliderRow] (setting: replies)\n"
            + "--Memory [sliderRow] (setting: memory)\n--Memory size [sliderRow] (setting: memory size)\n--Max reply [sliderRow] (setting: max reply)\n"
            + "--Bubble [sliderRow] (setting: bubble)\n--Follow-up messages [switch] (command: follow-ups)\n";
        // The custom model goes to SETUP with FALLBACK's rows, between BRAIN and FALLBACK; Local servers is SETUP's Provider key.
        internal const string BrainModelTree = "settings\n-BRAIN\n--Custom model name [entry] y522\n--Use [buttonSmall] x280 y525 w76 h22 (command: use custom)\n"
            + "--Custom model note [caption] y550\n";
        internal const string BrainFallbackTree ="settings\n-BRAIN\n--Local fallback [switch] y646 (command: local fallback)\n"
            + "--Fallback list [dropdown] Refresh (command: refresh fallback)\n--Fallback brain [dropdown] None (command: fallback brain)\n"
            + "--Brains in use [lightPair] No cloud / No local\n--Local priority [buttonRow]\n---Secondary [buttonSmall] (command: cloud first)\n"
            + "---Primary [buttonSmall] (command: local first)\n";

        // YOU: its keys in a row over its heading, then its rows, which are built a row lower and moved up with VOICE's once Hearing is built.
        internal const string YouTree = "settings\n-YOU\n--MD editor [buttonSmall] x139 y53 w106 h22 (command: md editor)\n"
            + "--Her folder [buttonSmall] x251 y53 w105 h22 (command: her folder)\n--Her personality [buttonSmall] x28 y53 w105 h22 (command: personality menu)\n";
        internal const string YouRowsTree = "settings\n-YOU\n--What should she call you? [buttonRow] y110\n"
            + "---Get to know me [buttonSmall] x236 w120 (command: get to know)\n--Your name or a nickname [entry]\n"
            + "--Save [buttonSmall] x280 y141 w76 h22 (command: save nickname)\n";

        internal const string SoundsTree = "settings\n-SOUNDS\n--Chimes [switch] (command: chimes)\n--Sound set [sliderRow] (setting: sound set)\n"
            + "--Chime volume [sliderRow] (setting: chime volume)\n--Message sent [switch] (command: chime sent)\n--Heard you [switch] (command: chime heard)\n"
            + "--Listening [switch] (command: chime listening)\n";
        internal const string WeatherTree = "settings\n-WEATHER\n--Weather [switch] (command: weather)\n--City [entry] x156 w172 Your city\n";
        internal const string DoomscrollTree = "settings\n-DOOMSCROLL\n--Scroll limit [sliderRow] (setting: scroll limit)\n";

        internal const string GamesTree = "settings\n-GAMES\n--Play... [buttonSmall] x28 w161 h22 (command: mini games)\n--Controls [buttonSmall] x195 w161 h22 (command: game notes)\n"
            + "--Comments [sliderRow] (setting: comments)\n--Live reactions [switch] (command: reactions)\n--Game sounds [switch] (command: game sounds)\n"
            + "--Gaming Mode [switch] (command: gaming)\n--Fast hands in games [switch] (command: fast hands)\n--Mute her [switch] (command: mute)\n";
        // CLIPS: its Save folder row stays her code between the two parts.
        internal const string ClipsTree = "settings\n-CLIPS\n--Clip my games [switch] (command: clips)\n--Save big moments [switch] (command: clip moments)\n";
        internal const string ClipsRecordTree = "settings\n-CLIPS\n--Open clips folder [buttonLarge] y1319 (command: open clips)\n"
            + "--Default length [sliderRow] y1350 (setting: clip length)\n--Frame rate [sliderRow] (setting: clip rate)\n"
            + "--Resolution [sliderRow] (setting: clip size)\n--Video bitrate [sliderRow] (setting: clip bits)\n";

        // What her own trees reach: her commands and her sliders' steps and setters, the same calls her rows made before.
        private TreeWiring HerWiring()
        {
            Dictionary<string, Action> commands = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase)
            {
                { "pop times", delegate { using (PopWindow dialog = new PopWindow(capri)) dialog.ShowDialog(this); } },
                { "mini player", delegate { capri.TogglePlayer(); } },
                { "dance", delegate { capri.ToggleDance(); } },
                { "intro", delegate { capri.ToggleIntro(); } },
                { "remember panel", delegate { capri.ToggleRememberPanel(); } }, { "on top", delegate { capri.ToggleOnTop(); } },
                { "custom colours", delegate { OpenColours(); } },
                { "mini games", delegate { ShowMiniGames(); } }, { "game notes", delegate { ShowGameNotes(); } }, { "reactions", delegate { capri.ToggleReactions(); } },
                { "game sounds", delegate { capri.ToggleGameSounds(); } }, { "gaming", delegate { capri.ToggleGaming(); } }, { "fast hands", delegate { capri.ToggleFastHands(); } },
                { "mute", delegate { capri.SetMute(gameMute.Checked); } }, { "clips", delegate { capri.ToggleClips(); } },
                { "clip moments", delegate { capri.ToggleClipMoments(); } }, { "open clips", delegate { capri.OpenClips(); } },
                { "husk", delegate { huskMenu.Show(huskMode, new Point(0, huskMode.Height)); } },
                { "thinking", delegate { thinkingMenu.Show(thinkingLevel, new Point(0, thinkingLevel.Height)); } },
                { "extra json", delegate { OpenThinkingExtra(); } }, { "verbosity", delegate { capri.ToggleHuskVerbosity(); } },
                { "model lock", delegate { capri.ToggleStayOnModel(); } }, { "follow-ups", delegate { capri.ToggleFollowUps(); } },
                { "local fallback", delegate { capri.ToggleLocalFallback(); } }, { "refresh fallback", delegate { RefreshFallback(); } },
                { "fallback brain", delegate { ShowTimed(fallbackMenu, fallbackBrain, "Fallback brain"); } },
                { "cloud first", delegate { if (!cloudPriority.Dimmed) capri.SetLocalFirst(false); } },
                { "local first", delegate { if (!localPriority.Dimmed) capri.SetLocalFirst(true); } },
                { "better conversations", delegate { capri.SetBetterConversations(betterTalk.Checked); Sync(); } },
                { "hologram", delegate { capri.ToggleHologram(); } }, { "toggles", delegate { OpenToggles(); } }, { "check ins", delegate { capri.ToggleCheckIns(); } },
                { "edit check-ins", delegate { capri.OpenCare(); } }, { "head pats", delegate { capri.ToggleHeadPats(); } }, { "chaos", delegate { capri.ToggleUpsetChaos(); } },
                { "legacy stand", delegate { capri.ToggleLegacyStand(); } }, { "jealous", delegate { capri.ToggleJealousAIs(); } }, { "close ais", delegate { capri.ToggleCloseAIs(); } },
                { "choose apps", delegate { jealousMenu.Show(jealousApps, new Point(0, jealousApps.Height)); } },
                { "vault", delegate { ShowMemoryVault(); } }, { "memories", delegate { OpenMemory(); } }, { "gifs", delegate { OpenGifs(); } },
                { "startup", delegate { capri.ToggleStartup(); } }, { "discord", delegate { capri.ToggleDiscord(); } }, { "cursor", delegate { capri.ToggleCursor(); } },
                { "remembering", delegate { capri.ToggleRemembering(); } }, { "security", delegate { OpenSecurity(); } },
                { "keep awake", delegate { capri.ToggleKeepAwake(); } }, { "hide full screen", delegate { capri.ToggleHideFullScreen(); } },
                { "player full screen", delegate { capri.TogglePlayerFullScreen(); } }, { "explore away", delegate { capri.ToggleExploreAway(); } },
                { "watch", delegate { capri.ToggleWatch(); } }, { "screens", delegate { ShowScreens(); } }, { "caught up", delegate { capri.ToggleCaughtUp(); } },
                { "talk key", delegate { PickKeys(0); } }, { "mute key", delegate { PickKeys(1); } }, { "type key", delegate { PickKeys(2); } },
                { "hide key", delegate { PickKeys(3); } }, { "quiet key", delegate { PickKeys(4); } }, { "push key", delegate { PickKeys(Companion.PushSlot); } },
                { "gaming key", delegate { PickKeys(5); } }, { "mainframe key", delegate { PickKeys(Companion.FrameSlot); } },
                { "confirm keys", delegate { ConfirmKeys(); } }, { "cancel keys", delegate { CancelKeys(); } }, { "push off", delegate { ClearPush(); } },
                { "md editor", delegate { EditNotes(false); } }, { "her folder", delegate { capri.OpenHerFolder(); } },
                { "personality menu", delegate { ShowPersonalityMenu(); } }, { "get to know", delegate { capri.GetToKnow(); } },
                { "save nickname", delegate { UseNickname(); } }, { "weather", delegate { capri.ToggleWeather(); } },
                { "chimes", delegate { capri.ToggleChimes(); } }, { "chime sent", delegate { capri.ToggleChime(Chime.Sent); } },
                { "chime heard", delegate { capri.ToggleChime(Chime.Heard); } }, { "chime listening", delegate { capri.ToggleChime(Chime.Listening); } }, { "use custom", delegate { UseCustom(); } },
            };
            for (int at = 0; at < Care.Names.Length; at++)
            {
                CareKind kind = (CareKind)at;
                commands["care " + Care.Names[at]] = delegate { capri.ToggleCare(kind); };
                commands["care time " + Care.Names[at]] = delegate { ChooseCareTime(kind); };
            }
            Dictionary<string, string[]> steps = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "wander", WanderNames }, { "sleep", SleepNames }, { "pop-ins", PopNames },
                { "mainframe size", FrameSizeNames }, { "mainframe colours", Neon.LookNames },
                { "every", GapNames }, { "watch memory", WatchLineNames },
                { "size", SizeNames }, { "pat stroke", Lengths }, { "comments", Commentary.PaceNames },
                { "sound set", SetNames() }, { "chime volume", ChimeVolumeNames }, { "scroll limit", ScrollNames },
                { "clip length", new[] { "15 s", "30 s", "45 s", "1 min", "1.5 min", "2 min" } },
                { "clip rate", new[] { "Up to 15 fps", "Up to 24 fps", "Up to 30 fps", "Up to 60 fps" } },
                { "clip size", new[] { "Auto", "Up to 480p", "Up to 720p", "Up to 1080p" } },
                { "clip bits", new[] { "Auto", "2 Mbps", "4 Mbps", "8 Mbps", "12 Mbps", "20 Mbps" } },
                { "replies", Lengths }, { "memory", Lengths }, { "memory size", MemoryNames }, { "max reply", ReplyNames }, { "bubble", BubbleNames },
                { "power saver", SaverNames }, { "music app", Hands.MusicSteps() }, { "perch", PerchNames }, { "explore after", AwayNames }
            };
            Dictionary<string, Action<int>> levels = new Dictionary<string, Action<int>>(StringComparer.OrdinalIgnoreCase)
            {
                { "wander", delegate(int at) { wanderName.Text = WanderNames[at]; capri.SetWanderLevel(at); } },
                { "sleep", delegate(int at) { sleepName.Text = SleepNames[at]; capri.SetSleepAfter(Sleeps[at]); } },
                { "pop-ins", delegate(int at) { popsName.Text = PopNames[at]; capri.SetPopPace(at); } },
                // The size is set when the slider is let go (its Released, on her field); moving it only shows the size.
                { "mainframe size", delegate(int at) { ShowFrameSize(); } },
                { "mainframe colours", delegate(int at) { capri.SetMainframeColours(at); } },
                { "every", delegate(int at) { gapName.Text = GapNames[at]; capri.SetGlanceGap(Gaps[at]); } },
                { "watch memory", delegate(int at) { capri.SetWatchMemory(WatchLines[at]); } },
                { "size", delegate(int at)
                    {
                        // Guarded: a mismatch between the two lists used to leave the size unapplied and the slider looking stuck.
                        int step = Math.Max(0, Math.Min(Math.Min(SizeNames.Length, SizeSteps.Length) - 1, at));
                        sizeName.Text = SizeNames[step];
                        capri.SetCharacterSize(SizeSteps[step]);
                    } },
                { "pat stroke", delegate(int at) { capri.SetPatStroke(at); } },
                { "sound set", delegate(int at) { chimeSetName.Text = SetNames()[at]; capri.SetChimeSet(Chimes.Picked[at]); } },
                { "chime volume", delegate(int at) { chimeVolumeName.Text = ChimeVolumeNames[at]; capri.SetChimeVolume((at + 1) * 10); } },
                { "scroll limit", delegate(int at) { capri.SetDoomscrollStep(at == 0 ? 0 : Doomscroll.Limits[at - 1]); } },
                { "comments", delegate(int at) { commentsName.Text = Commentary.PaceNames[at]; capri.SetCommentPace(at); } },
                { "clip length", delegate(int at) { SaveClipOptions(); } }, { "clip rate", delegate(int at) { SaveClipOptions(); } },
                { "clip size", delegate(int at) { SaveClipOptions(); } }, { "clip bits", delegate(int at) { SaveClipOptions(); } },
                { "replies", delegate(int at) { repliesName.Text = Lengths[at]; capri.SetWordiness(at); } },
                { "memory", delegate(int at) { recallName.Text = Lengths[at]; capri.SetRecall(Recalls[at]); } },
                { "memory size", delegate(int at) { memorySizeName.Text = MemoryNames[at]; capri.SetMemorySize(Brain.MemorySizes[at]); } },
                { "max reply", delegate(int at) { replyLengthName.Text = ReplyNames[at]; capri.SetReplyLength(at); } },
                { "bubble", delegate(int at) { bubbleName.Text = BubbleNames[at]; capri.SetBubbleTime(at); } },
                { "power saver", delegate(int at) { powerSaverName.Text = SaverNames[at]; capri.SetPowerSaver(at); } },
                { "music app", delegate(int at) { musicAppName.Text = Hands.MusicSteps()[at]; capri.SetMusicApp(at); } },
                { "perch", delegate(int at) { capri.SetPerchFps(Companion.PerchRates[at]); ShowPerch(); } },
                { "explore after", delegate(int at) { capri.SetExploreAfter(AwayTimes[at]); } },
            };
            VoiceWiring(commands, steps, levels);
            return new TreeWiring
            {
                Command = delegate(string name) { Action run; return commands.TryGetValue(name ?? "", out run) ? run : null; },
                StepsOf = delegate(string name) { string[] found; return steps.TryGetValue(name ?? "", out found) ? found : null; },
                SetLevel = delegate(string name, int at) { Action<int> set; if (levels.TryGetValue(name ?? "", out set)) set(at); return null; },
                Log = delegate(string line) { Brain.Log("mainframe", -1, "her tree: " + line); }
            };
        }

        // Heading keys: YOU's three sit in a row of their own just above YOU's heading, moving with YOU wherever it goes and
        // staying when it folds; every other heading's keys become the first row inside their panel, so they fold away with it.
        private readonly HashSet<Control> onBand = new HashSet<Control>();

        private const int YouKeysGap = 10;

        private void KeysInPanels()
        {
            foreach (List<Panel> list in columns)
                foreach (Panel panel in list)
                {
                    List<Control> keys = new List<Control>();
                    foreach (KeyValuePair<Control, int> row in panel.Offsets) if (row.Key != panel.Head && row.Value < 0 && !onBand.Contains(row.Key)) keys.Add(row.Key);
                    if (keys.Count == 0) continue;
                    foreach (Control row in new List<Control>(panel.Offsets.Keys)) if (row != panel.Head && !keys.Contains(row)) panel.Offsets[row] += Row;
                    foreach (Control key in keys) panel.Offsets[key] = 27;
                    panel.Span += Row;
                }
            Panel you = FindPanel("YOU");
            if (you == null) return;
            // The keys end the same 10 px over YOU's rule as any panel's last row does over the next one's.
            you.Lead = Row + YouKeysGap;
            foreach (Control key in new Control[] { notesKey, filesKey, personalityKey })
            {
                you.Offsets[key] = -Row - YouKeysGap;
                panelOf[key] = you;
            }
        }

        // One of her panels built from its tree on her grid; its pieces by name.
        private bool herHeadless;

        private Dictionary<string, Control> HerPanel(string tree, int x, int top, Font captions, bool heading = true)
        {
            herHeadless = !heading;
            captionFont = captions;
            Dictionary<string, Control> built = new Dictionary<string, Control>(StringComparer.OrdinalIgnoreCase);
            PanelTree.Model model = PanelTree.Read(tree, null);
            foreach (string refused in model.Refused) Brain.Log("mainframe", -1, "her tree: " + refused);
            TreeWiring wire = herWiring ?? (herWiring = HerWiring());
            PanelTree.Compile(model, wire);
            foreach (PanelTree.Row panel in model.Panels) TreePanel(panel, wire, TreeLayout.HerGrid, built, new Point(x, top));
            herHeadless = false;
            return built;
        }

#if CHECKS
        // YOU's keys sit just above its heading wherever YOU is: first, and moved under VOICE, they move with it, clear of
        // VOICE, and stay when YOU folds. Pictures in you-keys/. Null when right.
        internal string YouKeysCheck(string output, Action<bool> foldable)
        {
            string folder = System.IO.Path.Combine(output, "you-keys");
            System.IO.Directory.CreateDirectory(folder);
            string savedWas = capri.PanelsSaved;
            bool pagedWas = paged, foldedWas = capri.FoldedNow("YOU"), foldableWas = capri.FoldOn("YOU");
            List<string> wrong = new List<string>();
            Action<string> look = delegate(string step)
            {
                Panel you = FindPanel("YOU");
                int head = you.Head.Top;
                foreach (Control key in new Control[] { notesKey, filesKey, personalityKey })
                    if (!key.Visible || head - key.Bottom < Scaled(YouKeysGap) || head - key.Bottom > Scaled(16 + YouKeysGap)) wrong.Add(step + ": " + key.Text + " at " + key.Bounds + " for YOU's heading at " + head);
                int index = columns[you.Column].IndexOf(you);
                if (index > 0)
                {
                    Panel above = columns[you.Column][index - 1];
                    foreach (KeyValuePair<Control, int> row in above.Offsets)
                        if (row.Key.Visible && row.Key.Bottom > notesKey.Top) wrong.Add(step + ": " + above.Id + "'s " + row.Key.Text + " runs under YOU's keys");
                }
                using (Bitmap shot = new Bitmap(Width, Height))
                {
                    DrawToBitmap(shot, new Rectangle(Point.Empty, new Size(Width, Height)));
                    shot.Save(System.IO.Path.Combine(folder, step + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                }
            };
            FormWindowState stateWas = WindowState;
            try
            {
                if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
                if (!Visible) Show();
                ShowSettings(true);
                ScrollPage(0);
                look("first");
                capri.SavePanels("VOICE;YOU;SOUNDS;CLIPS;MOVES;GAMES;WEATHER|GENERAL SETTINGS;CAPRI;DOOMSCROLL;WATCH;BRAIN;KEYS");
                SavedOrder();
                Restack();
                ScrollPage(PanelTop(FindPanel("YOU")) - 120);
                look("under-voice");
                foldable(true);
                capri.SetFolded("YOU", true);
                FoldsChanged();
                if (!FindPanel("YOU").Folded) wrong.Add("YOU didn't fold");
                look("under-voice-folded");
            }
            finally
            {
                capri.SetFolded("YOU", foldedWas);
                foldable(foldableWas);
                ApplyFolds();
                if (string.IsNullOrEmpty(savedWas)) SetPanels(StockLeft, StockRight);
                capri.SavePanels(savedWas);
                SavedOrder();
                Neighbours();
                Restack();
                capri.SavePanels(savedWas);
                ShowSettings(pagedWas);
                WindowState = stateWas;
            }
            return wrong.Count == 0 ? null : string.Join("; ", wrong.ToArray());
        }
#endif

#if CHECKS
        // Better conversations in Settings' CAPRI: directly under Hologram, with its tip, and in step with its Toggles row both
        // ways (asked twice). Null when right.
        internal string BetterTalkCheck()
        {
            List<string> wrong = new List<string>();
            if (betterTalk == null || !Controls.Contains(betterTalk) || betterTalk.Text != "Better conversations") return "no Better conversations row in Settings' CAPRI";
            if (betterTalk.Left != hologram.Left || betterTalk.Top <= hologram.Top || betterTalk.Top - hologram.Top > hologram.Height + Scaled(10) || betterTalk.Top >= togglesKey.Top)
                wrong.Add("not directly under Hologram: " + hologram.Bounds + " / " + betterTalk.Bounds);
            SettingHint hint;
            if (!hints.TryGetValue(betterTalk, out hint) || hint.Topic == null || hint.Topic.Facts.IndexOf("RPG-style prompts", StringComparison.Ordinal) < 0) wrong.Add("its tip doesn't say what it does");
            ToolStripMenuItem row = null;
            Func<ToolStripItemCollection, ToolStripMenuItem> find = null;
            find = delegate(ToolStripItemCollection items)
            {
                foreach (ToolStripItem one in items)
                {
                    ToolStripMenuItem item = one as ToolStripMenuItem;
                    if (item == null) continue;
                    if (item.Text == "Better conversations") return item;
                    ToolStripMenuItem under = item.HasDropDownItems ? find(item.DropDownItems) : null;
                    if (under != null) return under;
                }
                return null;
            };
            row = capri.TogglesMenu == null ? null : find(capri.TogglesMenu.DropDownItems);
            Func<PluginInfo, bool> askWas = PluginAsk;
            bool was = capri.BetterConversationsOn;
            try
            {
                PluginAsk = delegate { return true; };
                // From Settings: the setting follows, and so does what Toggles shows as it opens.
                betterTalk.Checked = !was;
                bool fromSettings = capri.BetterConversationsOn == !was;
                // From Toggles: Settings follows on its next sync.
                if (row != null) row.PerformClick();
                Sync();
                bool fromToggles = row == null || (capri.BetterConversationsOn == was && betterTalk.Checked == was);
                if (!fromSettings || !fromToggles || row == null) wrong.Add("out of step with Toggles: from Settings " + fromSettings + ", from Toggles " + fromToggles + ", Toggles row " + (row != null));
            }
            finally
            {
                if (capri.BetterConversationsOn != was) capri.SetBetterConversations(was);
                PluginAsk = askWas;
                Sync();
            }
            return wrong.Count == 0 ? null : string.Join("; ", wrong.ToArray());
        }
#endif

        private TreeWiring herWiring;
    }
}
