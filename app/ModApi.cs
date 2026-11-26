using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;
#if !CROSS
using System.Windows.Forms;
#endif

namespace CapriDesktop
{
    // The code mod API, the same on Windows, Mac and Linux, on ICapriHost so first-API mods still compile. Hooks run on
    // her window's thread; a throwing mod is only logged. Windows-only parts are refused at import elsewhere, and a
    // missing feature answers as on a PC without it.

    public interface ICapriHost
    {
        string Folder { get; }
        void Say(string line);
        void Log(string line);
#if !CROSS
        Bitmap Art(string name);
#endif
        void PlayIntro();
        void Sound(string name);
        int Api { get; }
        string CapriVersion { get; }
        bool Has(string permission);
        ICapriChat Chat { get; }
        ICapriMemory Memory { get; }
        ICapriUi Ui { get; }
        ICapriMainframe Mainframe { get; }
        ICapriScreen Screen { get; }
        ICapriVoice Voice { get; }
        ICapriTimers Timers { get; }
        ICapriPersonality Personality { get; }
        ICapriSettings Settings { get; }
        ICapriSkins Skins { get; }
        ICapriBody Her { get; }
        ICapriBrain Brain { get; }
        ICapriStore Store { get; }
        ICapriSystem System { get; }
        ICapriMods Mods { get; }
        ICapriKeys Keys { get; }
        ICapriSounds Sounds { get; }
        ICapriMusic Music { get; }
        ICapriActs Acts { get; }
        ICapriMood Mood { get; }
        ICapriSlots Slots { get; }
        ICapriToggles Toggles { get; }
#if !CROSS
        ICapriPetHost Pet { get; }
#endif
    }

    // Her acts: a new one the mod defines, one done, and each as it lands with what it was worth. API 6, "mood".
    public interface ICapriActs
    {
        void Define(string name, string kind, int like);
        void Did(string name);
        event Action<string, int> Happened;
        string[] List { get; }
        // What an act is worth to her now, -5 to 5, and her line for it, liked or not; a line from one of her pools by its
        // heading, like "gift remembered". API 9.
        int Worth(string name);
        string Line(string name, bool liked);
        string Pool(string heading);
    }

    // Her act maths: a term added to an act's worth (-3 to 3) or a multiplier on it (0.25 to 2), each named, gone with
    // the mod; and how she feels now. API 6, "mood".
    public interface ICapriMood
    {
        void Add(string term, string act, int value);
        void Multiply(string term, string act, double value);
        // The same, counted only while she is one of the personalities or moods named ("Emo", "mood sleepy"). API 11.
        void Add(string term, string act, int value, string[] who);
        void Multiply(string term, string act, double value, string[] who);
        void Remove(string term);
        int Warmth { get; }
        int Upset { get; }
        int Willing { get; }
        string Today { get; }
    }

    // Her slots, as slots.md names them: take one over, let it go, and who holds it. A pack's conflict box asks the
    // same way. API 6, "slots".
    public interface ICapriSlots
    {
        bool Override(string slot, string text);
        void Release(string slot);
        string Holder(string slot);
    }

    // Toggles: a setting for the whole group (no item) sits at the top of the mod's group; one for an item lives only in
    // that item's side area. Her settings keep each and hand it back. Switches API 6, the rest 9.
    public interface ICapriToggles
    {
        void Add(string name, bool on, Action<bool> changed);
        void AddSlider(string name, string[] steps, int at, Action<int> changed);
        void Add(string item, string name, bool on, Action<bool> changed);
        void AddSlider(string item, string name, string[] steps, int at, Action<int> changed);
        // Called before the first switch: they go in her group of that title (Intros, Screensaver, Pranks) instead of one
        // of their own, or, with a section's name, the mod sits in that section. API 10.
        void Join(string title);
    }

#if !CROSS
    // A screensaver mod: its looks by name, one shown when she asks with what her settings say, and taken down when she
    // says. She keeps the rules: the idle wait, never over full screen, a stream or a job of hers. API 8.
    public interface ICapriScreensaver
    {
        string[] Looks { get; }
        bool Show(CapriSaverAsk ask);
        void Hide();
        bool Showing { get; }
    }

    // What she asks a screensaver for: the look, and her settings for it as sys.md and her sound switch have them.
    public sealed class CapriSaverAsk
    {
        public string Look;
        // Seconds before a key may end it, so the click that asked for it doesn't; nought to stay until sent away.
        public double Settles = 0.6, Ends;
        // Any key ends it (her prank), not only Space or Escape.
        public bool AnyKey;
        // Her reduced motion setting: off, it is up at once rather than fading in.
        public bool Motion = true;
        public float Size = 1f, Speed = 1f, Flash = 1f;
        // How solid it draws, 30 to 255.
        public byte Solid = 255;
        // Every screen, or only the one she is on.
        public bool EveryScreen = true;
        public Rectangle Hers;
        // A .wav of theirs to loop while it is up, and her sound volume 0 to 100 with her sound switch already in it; a
        // mod's own volume slider scales it.
        public string Sound;
        public int Volume;
    }

    // A pet game mod: what her food costs and paying for it, its line at the top of her Tamagotchi menu, and whether
    // feeding is on. She keeps feeding and hunger; it keeps the game. API 9.
    public interface ICapriPet
    {
        // Shown first in her Tamagotchi menu, like "Points: 412", in its colour; empty for none.
        string Heading { get; }
        Color HeadingColour { get; }
        // A food's price by its label in Feed her, 0 for free; paid just before she is fed, false when it can't be.
        int Cost(string food);
        bool Pay(string food);
        // Whether Pay would go through now; a food it can't pay for is greyed in her menu.
        bool CanPay(string food);
        // Its Feeding part: off, Feed her is gone and she never says she's hungry.
        bool Feeding { get; }
    }

    // Her side of a pet game: how fed she is, each feed as it lands, and her Tamagotchi menu. API 9, "pet".
    public interface ICapriPetHost
    {
        // Hours since she was fed, time at a desk counting for less, or -1 when she never has been; and "fed", "peckish"
        // or "hungry".
        double SinceFed { get; }
        string Hunger { get; }
        // The food's act name, as she is fed.
        event Action<string> Fed;
        // Her Tamagotchi menu: what the mod puts in goes when it stops.
        ICapriMenu Menu { get; }
        // One of her six needs, 0 to 100: energy, fullness, stimulation, social, comfort, cleanliness; and told as they move.
        // API 10.
        int Stat(string need);
        event Action StatsChanged;
    }

    public interface ICapriIntro
    {
        double Seconds { get; }
        void Begin(Bitmap desktop, Rectangle screen, Rectangle her);
        void Draw(Graphics graphics, Rectangle screen, Rectangle her, double at);
    }

    // An intro mod with more than one, named, so her test menu lists what the mod has rather than what the build
    // knew of. Optional: an intro mod without it is listed by her own names.
    public interface ICapriIntroNames
    {
        string[] Intros { get; }
    }

    // An intro mod that can stop early, when they press Esc: it closes its own windows and timers. Optional; an intro
    // without it still ends, with her sound for it cut.
    public interface ICapriIntroStops
    {
        void StopIntro();
    }
#endif

    public interface ICapriMod
    {
        void Start(ICapriHost capri);
        bool Heard(string words);
        void Stop();
    }

    public sealed class CapriPermissionException : InvalidOperationException
    {
        public CapriPermissionException(string message) : base(message) { }
    }

    public sealed class CapriMessage
    {
        // Sending, Replying and Speaking can change Text. Cancel on Sending means she ignores it; on Speaking she only shows it.
        public string Text;
        public bool Cancel;
        public string Original { get; internal set; }
        // "you" for what they typed or said, "mod" for a mod's Chat.Send, "reply" for her answers, "remark" for her own lines.
        public string Source { get; internal set; }
        public DateTime At { get; internal set; }
    }

#if !CROSS
    public sealed class CapriFrame
    {
        public Graphics Graphics { get; internal set; }
        // Her whole window and her body in it, in the window's own pixels.
        public Rectangle Window { get; internal set; }
        public Rectangle Body { get; internal set; }
        public Point Head { get; internal set; }
        public bool FacingLeft { get; internal set; }
        // The strip being drawn, like "idle" or "perch", and which frame of it.
        public string Animation { get; internal set; }
        public int Frame { get; internal set; }
        public int Size { get; internal set; }
    }
#endif

    public interface ICapriTimer
    {
        bool Running { get; }
        void Cancel();
    }

    public sealed class CapriToolCall
    {
        // The tool she's about to use, like "open_app", and what it does in plain words, like "open Spotify".
        public string Tool { get; internal set; }
        public string Doing { get; internal set; }
        public string Name { get; internal set; }
        public string Action { get; internal set; }
        public string Text { get; internal set; }
        // Cancel stops it before it runs, and Reason is what her brain is told instead.
        public bool Cancel;
        public string Reason;
    }

    public interface ICapriChat
    {
        event Action<CapriMessage> Sending;
        event Action<CapriMessage> Replying;
        event Action<CapriMessage> Replied;
        event Action<string, string> ToolUsed;
        event Action<CapriToolCall> ToolUsing;
        void AddContext(Func<string> context);
        void ClearContext();
        string LastHeard { get; }
        string LastReply { get; }
        bool Busy { get; }
        void Send(string text);
        bool Start(string about);
        string[] Days();
        string Transcript(DateTime day);
    }

    public interface ICapriMemory
    {
        string[] Notes { get; }
        string Remember(string fact);
        string Forget(string about);
        bool Update(string oldText, string newText);
        void Set(string label, string value);
        string Memories { get; }
        bool WriteMemories(string text);
    }

#if !CROSS
    // A mod's own menu: up to 30 items, each up to 60 characters. API 4.
    public interface ICapriMenu
    {
        ICapriMenuItem Add(string text, Action clicked);
        // An item with a tick, ticked or not to start with; changed gets the new state on each click.
        ICapriMenuItem AddCheck(string text, bool on, Action<bool> changed);
        // A list inside it, dressed as her own the moment it is made. API 9.
        ICapriMenu AddMenu(string text);
        void AddLine();
        void Clear();
        void Remove();
    }

    public interface ICapriMenuItem
    {
        string Text { get; set; }
        bool Checked { get; set; }
        bool Enabled { get; set; }
        // Words in the right-hand column, like a price, and the colour it draws in. API 9.
        string Note { get; set; }
        Color Colour { get; set; }
        void Remove();
    }
#endif

    public interface ICapriUi
    {
#if !CROSS
        event Action<CapriFrame> Drawing;
#endif
        void Redraw();
        void AddMenuItem(string text, Action clicked);
#if !CROSS
        void SetOptions(string title, Func<Control> make);
        // Opens the mod's own options window, as its Options key on the Mods page does. API 9.
        void ShowOptions();
        // A menu of the mod's own under Mods in her right-click menu, with items, ticks and lines it can change later.
        // It goes when the mod stops. API 4.
        ICapriMenu AddMenu(string text);
        // Just before her right-click menu opens, so a menu's ticks and words can be brought up to date. API 4.
        event Action MenuOpening;
        // How much bigger she draws, 1 to 1.15, about her seat or feet like her own beat pulse and on top of it, never her
        // size setting. Held at 1 while Motion is off, and back to 1 when the mod stops. API 4.
        float DrawScale { get; set; }
        // How solid her speech bubble draws, 0.3 to 1. With more than one mod asking, the most see-through wins; back to 1 when
        // the mod stops. API 4.
        float BubbleOpacity { get; set; }
        // Her own OFF/ON switch and key, in her colours and font, for a panel of the mod's own (Mainframe.AddPanel). changed
        // gets the new state on each flip. Text is up to 60 characters. API 4.
        Control Switch(string text, bool on, Action<bool> changed);
        Control Button(string text, Action clicked);
        // A colour of the mod's own, named for what it colours (40 characters, 16 a mod): the one set in Custom colours
        // under the mod's fold-down, or usual, following Mainframe colours. Ask first in Start; ColoursChanged says
        // when to ask again. API 5.
        Color Colour(string name, Color usual);
        event Action ColoursChanged;
#endif
        void Bubble(string text);
#if !CROSS
        Form Window { get; }
#endif
    }

    public interface ICapriMainframe
    {
        event Action Opened;
        event Action Closed;
        bool Open { get; }
        void Show();
        void Hide();
        void ShowSettings();
        void ShowMods();
#if !CROSS
        void ShowPage(string title, Func<Control> make);
        // A panel of the mod's own on her Settings page: its title and control, at slot (1 is the top) in the "left" or
        // "right" column; the first loaded keeps a shared slot. simple is taken and ignored: Settings has one view. API 4.
        void AddPanel(string title, string column, int slot, Func<Control> make, bool simple = false);
        // The same, inside a panel shared by every mod naming this group ("Yuno Mods"): title is its sub-heading there.
        // The first part placed sets the group's column and slot. API 7.
        void AddPanel(string title, string column, int slot, Func<Control> make, bool simple, string group);
#endif
        void ClosePage();
#if !CROSS
        Form Window { get; }
#endif
    }

    // Empty on Mac and Linux, where screenshots would be Bitmaps, but still there, so a mod reaching for it is told which part.
    public interface ICapriScreen
    {
#if !CROSS
        event Action<Bitmap> Looked;
        Bitmap Capture();
        Bitmap Capture(Rectangle area);
#endif
    }

    public interface ICapriVoice
    {
        event Action<CapriMessage> Speaking;
        event Action<string> Started;
        event Action Finished;
        event Action<string, bool> Heard;
        bool Talking { get; }
        void Speak(string text);
        void Stop();
    }

    public interface ICapriTimers
    {
        ICapriTimer After(double seconds, Action run);
        ICapriTimer Every(double seconds, Action run);
        ICapriTimer At(DateTime when, Action run);
    }

    public interface ICapriPersonality
    {
        string[] Notes { get; }
        void Add(string note);
        void Remove(string note);
        void Clear();
        string Nickname { get; set; }
    }

    public interface ICapriSettings
    {
        event Action Changed;
        string[] Switches { get; }
        string[] Sliders { get; }
        bool? IsOn(string name);
        bool Turn(string name, bool on);
        int Level(string name);
        string[] Steps(string name);
        bool SetLevel(string name, int step);
        int Size { get; set; }
    }

    public interface ICapriSkins
    {
        string[] Installed { get; }
        string Current { get; }
        string[] Accessories { get; }
        bool IsAccessory(string id);
        bool Use(string id);
        bool Equip(string id, bool on);
    }

    public interface ICapriBody
    {
        event Action<string> StateChanged;
        event Action Clicked;
        event Action Patted;
        event Action Dragged;
        event Action<Point> Dropped;
        Rectangle Bounds { get; }
        Point Head { get; }
        int Size { get; }
        string State { get; }
        string Animation { get; }
        string Animate(string name);
        bool Dragging { get; }
        Rectangle Screen { get; }
        void MoveTo(Point hip);
        string Move(string action);
        string SitOn(string window);
    }

    public interface ICapriBrain
    {
        bool Ready { get; }
        void Ask(string instructions, string question, Action<string> answered);
        bool AddTool(string name, string description, string takes, Func<string, string> run);
        void RemoveTool(string name);
        string[] Tools { get; }
    }

    public interface ICapriStore
    {
        string[] Keys { get; }
        string Get(string key);
        string Get(string key, string fallback);
        double Number(string key, double fallback);
        bool Flag(string key, bool fallback);
        void Set(string key, string value);
        void Set(string key, double value);
        void Set(string key, bool value);
        bool Remove(string key);
    }

    public interface ICapriSystem
    {
        event Action Away;
        event Action Back;
        event Action Locked;
        event Action Unlocked;
        event Action Sleeping;
        event Action Woke;
        event Action NewDay;
        event Action<int, bool> BatteryChanged;
        TimeSpan IdleFor { get; }
        double AwayMinutes { get; set; }
        bool IsAway { get; }
        int Battery { get; }
        bool Charging { get; }
#if !CROSS
        // A game's window is in front, by her own check, read about once a second. API 4.
        bool GameInFront { get; }
#endif
    }

    public interface ICapriMods
    {
        string Id { get; }
        string[] Running { get; }
        bool IsRunning(string id);
        void Send(string topic, string data);
        void SendTo(string id, string topic, string data);
        event Action<string, string, string> Received;
    }

    public interface ICapriKeys
    {
        bool Hotkey(string keys, Action pressed);
        void Remove(string keys);
    }

    public interface ICapriSounds
    {
        bool Play(string file);
        bool Play(string file, double volume);
        void Cue(string name);
    }

    // A song or video playing in an app, as Windows' media session told it at a read, about once a second.
    public sealed class CapriTrack
    {
        public string Title { get; internal set; }
        public string Artist { get; internal set; }
        public string Album { get; internal set; }
        // The name to show, like "Spotify" or "Chrome", and the app's own id, which Music.Follow takes.
        public string App { get; internal set; }
        public string AppId { get; internal set; }
        public bool Playing { get; internal set; }
        public bool Music { get; internal set; }
        public bool Video { get; internal set; }
        // Seconds into it at the read, and how long it is; Length is 0 when the app doesn't say.
        public double Position { get; internal set; }
        public double Length { get; internal set; }
        public double Rate { get; internal set; }
        public bool CanSeek { get; internal set; }
        public bool CanSkip { get; internal set; }
        public bool CanBack { get; internal set; }
        // Null when the app doesn't say. Repeat is "off", "one" or "all".
        public bool? Shuffle { get; internal set; }
        public string Repeat { get; internal set; }
        // The same song in the same app has the same key.
        public string Key { get; internal set; }
        internal DateTime ReadAt;
        internal object Source;

        // Where it's got to by now: it plays on from where it was read.
        public double At()
        {
            double at = Position + (Playing ? Math.Max(0, (DateTime.UtcNow - ReadAt).TotalSeconds) * (Rate > 0 ? Rate : 1) : 0);
            return Length > 0 ? Math.Min(at, Length) : at;
        }
    }

    // What's playing on the PC and presses back to its app, the session her mini player reads; presses go off her
    // thread and the next read shows them. On Mac and Linux nothing reads yet: Now null, presses false, Levels 0, no
    // events.
    public interface ICapriMusic
    {
        // A new song, or null when nothing has a title any more.
        event Action<CapriTrack> Changed;
        // Every read, about once a second, and soon after a press.
        event Action<CapriTrack> Updated;
        CapriTrack Now { get; }
        // Every app with something in Windows' media session, Windows' own pick first.
        CapriTrack[] Sessions { get; }
        // Follows one app's session by its AppId, or null for whatever Windows picks.
        string Following { get; }
        void Follow(string appId);
        bool Play();
        bool Pause();
        bool Toggle();
        bool Next();
        bool Previous();
        bool Seek(double seconds);
        bool SetShuffle(bool on);
        bool SetRepeat(string mode);
        // Spotify's own heart for the song, or null outside Spotify or when it can't tell.
        bool? Liked { get; }
        bool Like(bool on);
        // The cover the app gave Windows for Now, as PNG or JPEG bytes, or null.
        byte[] Art { get; }
        // The app's own volume in Windows' volume mixer, 0 to 1, or -1 before it has made a sound.
        double Volume { get; set; }
        bool Muted { get; set; }
        // How loud the PC's sound is right now in that many bands from deep bass to treble, each 0 to 1, for a visualizer. It
        // listens only while a mod keeps asking, and stops a few seconds after the last ask.
        float[] Levels(int bands);
        float Loudness { get; }
        // False keeps her own mini player away while this mod runs, for a mod that shows its own.
        bool BuiltInPlayer { get; set; }
        // Brings the app playing it to the front.
        bool Open();
    }

    internal sealed partial class Mods
    {
        internal const int Api = 11;

        // What each permission lets a code mod do, as her approve box says it.
        internal static readonly string[,] PermissionWords =
        {
            { "chat", "read and change what you and Capri say to each other, and talk to her for you" },
            { "transcripts", "read her saved chat transcripts" },
            { "memory", "read, add, change and delete her notes about you and her memories" },
            { "screen", "take screenshots and see what she sees on your screen" },
            { "voice", "hear what your mic picks up and change what she says out loud" },
            { "personality", "add to her personality and change what she calls you" },
            { "settings", "read and change her settings, like her size and volume" },
            { "skins", "change her outfit and equip or take off her accessories" },
            { "brain", "ask her brain questions, which can use your API credits" },
            { "tools", "add tools her brain can use by itself, and see or stop the tools she's about to use" },
            { "system", "know when you're away or back, when your PC locks, sleeps or wakes, and its battery" },
            { "hotkeys", "add keyboard shortcuts that work in any app" },
            { "music", "see what's playing, control your music apps and their volume, and hear how loud your PC's sound is" },
            { "mood", "add acts and change how much she likes them, and read how she feels" },
            { "slots", "take over parts of her files, the same as a drop-in pack" },
            { "pet", "charge for her food, add to her Tamagotchi menu, and see when she was fed" }
        };

        internal static bool KnownPermission(string name)
        {
            for (int i = 0; i < PermissionWords.GetLength(0); i++) if (PermissionWords[i, 0] == name) return true;
            return false;
        }

        internal static string PermissionMeaning(string name)
        {
            for (int i = 0; i < PermissionWords.GetLength(0); i++) if (PermissionWords[i, 0] == name) return PermissionWords[i, 1];
            return name;
        }

        internal static string[] ReadPermissions(string value)
        {
            List<string> asked = new List<string>();
            foreach (string part in Regex.Split(value ?? "", @"[\s,]+"))
            {
                string name = part.Trim().ToLowerInvariant();
                if (name.Length == 0) continue;
                if (!KnownPermission(name))
                {
                    List<string> known = new List<string>();
                    for (int i = 0; i < PermissionWords.GetLength(0); i++) known.Add(PermissionWords[i, 0]);
                    throw new ModException("\"" + part.Trim() + "\" isn't a permission. A code mod can ask for " + string.Join(", ", known.ToArray()) + ".");
                }
                if (!asked.Contains(name)) asked.Add(name);
            }
            return asked.ToArray();
        }

        // The most whole addresses her question lists under the sites a mod contacts; the rest is a count.
        private const int MostPlaces = 12;

        // Her brain's reading, when they asked for one, named first: it is what they read before answering.
        internal static string ReadingNote(string explained)
        {
            return explained == null ? "" : "Her brain's reading of it is saved here:\n" + explained + "\n\nIt can be wrong; the code that would run is the original.\n\n";
        }

        // The second question, for code that cannot be read. When it is only laid out badly, a readable copy is
        // saved and named so it can be read first. When it carries packed data, nothing can open it, and that
        // is said instead.
        internal static string Unclear(ModFile mod, string explained = null)
        {
            string read = ReadingNote(explained);
            string name = mod.Name ?? mod.Id ?? "This mod";
            string why = mod.Unreadable ?? "it can't be read";
            if (mod.Tidy == null)
                return read + name + "'s code can't be read as it is: " + why + ". It can also start programs, call Windows directly or call code by its name, which is how packed data gets run, so what Capri listed about it may not be what it really does.\n\n"
                    + "Mods sometimes carry a picture or a sound this way, but packed data can hide anything. If you didn't expect it from this mod, don't run it.\n\nRun it anyway?";
            string copy = null;
            try
            {
                copy = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "capri-mod-" + System.Text.RegularExpressions.Regex.Replace(mod.Id ?? "mod", "[^A-Za-z0-9-]", "") + "-readable.cs");
                System.IO.File.WriteAllText(copy, mod.Tidy);
            }
            catch (Exception caughtOne) { Brain.Caught(caughtOne); copy = null; }
            return read + name + "'s code can't be read as it is: " + why + ". What Capri listed about it, and what you'd see opening the file, may not be what it really does.\n\n"
                + (copy == null ? "" : "She laid it back out so it can be read, and saved that copy here:\n" + copy + "\n\nRead that before saying yes. ")
                + "Generated code can look like this too, so it isn't proof of anything. If you didn't expect it from this mod, don't run it.\n\nRun it anyway?";
        }

        // How serious each thing a mod asks for, or her scan sees in it, is: High sees or does private things, Medium changes
        // her or reaches past her, Low is looks and sound. By its permission name or its scan row.
        internal static char GroupOf(string row)
        {
            string one = (row ?? "").Trim();
            foreach (string high in new[] { "chat", "transcripts", "screen", "voice", "tools", "hotkeys", Loads, Packs, "unlocks saved secrets", "reads plug-in sign-ins", ChangesSecurity, "calls Windows directly", "starts programs", "reads the keyboard", "makes itself start with Windows", PrivateCode, ChatHistory, NotesMemories, BrainDirect, OtherMods, FindsAddress, UntrustedSite, SendsPrivate })
                if (one == high) return 'H';
            foreach (string low in new[] { "skins", "music", "pet", "changes what her brain is told", "adds a Settings panel" })
                if (one == low) return 'L';
            return 'M';
        }

        // Stage 1 of her mod question: what it asks for and what her scan saw, counted by group on its face, with every row
        // grouped behind a handle that opens straight away when anything is High (Notice.Marked draws it).
        internal static string Grouped(ModFile mod)
        {
            List<KeyValuePair<char, string>> rows = new List<KeyValuePair<char, string>>();
            foreach (string permission in mod.Permissions ?? new string[0]) rows.Add(new KeyValuePair<char, string>(GroupOf(permission), "asks to " + PermissionMeaning(permission)));
            int asked = rows.Count;
            string[] reaches = mod.Reaches ?? new string[0], places = mod.Places ?? new string[0];
            bool contacts = Array.Exists(reaches, one => IsContacts(one));
            foreach (string one in reaches)
            {
                // Addresses sit under the one network row, never as rows of their own.
                if (one == "uses the network" && contacts) continue;
                if (!IsContacts(one)) { rows.Add(new KeyValuePair<char, string>(GroupOf(one), one)); continue; }
                List<string> sites = new List<string>();
                foreach (string place in places) { string site = Site(place); if (site.Length > 0 && !sites.Contains(site)) sites.Add(site); }
                int shown = 0;
                System.Text.StringBuilder said = new System.Text.StringBuilder("uses the network:");
                foreach (string site in sites)
                {
                    said.Append("\n    contacts ").Append(site);
                    foreach (string place in places)
                    {
                        if (Site(place) != site) continue;
                        if (shown++ < MostPlaces) said.Append("\n        ").Append(place);
                    }
                }
                if (shown > MostPlaces) said.Append("\n        and ").Append(shown - MostPlaces).Append(" more addresses");
                rows.Add(new KeyValuePair<char, string>(GroupOf("uses the network"), said.ToString()));
            }
            // A count of 0 only when her scan saw nothing, worded so it never reads as "needs nothing".
            string none = asked == 0 ? "It asks for none of Capri's permissions.\n\n" : "";
            string zero = rows.Count > asked ? "" : "What her scan could see in " + (mod.Name ?? mod.Id ?? "this mod") + "'s code (0): none of the things on her list. Her scan reads the code's text only, so this isn't proof it's safe.\n\n";
            if (rows.Count == 0) return none + zero;
            string[] names = { "high", "medium", "low" }, heads = { "High: sees or does private things", "Medium: changes her or reaches past her", "Low: looks and sound" };
            char[] groups = { 'H', 'M', 'L' };
            System.Text.StringBuilder made = new System.Text.StringBuilder("[[c]]What it asks for and what her scan saw:");
            for (int g = 0; g < 3; g++)
            {
                int count = rows.FindAll(row => row.Key == groups[g]).Count;
                if (count > 0) made.Append('|').Append(groups[g]).Append('|').Append(count).Append(' ').Append(names[g]);
            }
            bool high = rows.Exists(row => row.Key == 'H');
            made.Append("\n[[f").Append(high ? '+' : '-').Append("]]What we could see (").Append(rows.Count).Append(")");
            for (int g = 0; g < 3; g++)
            {
                if (!rows.Exists(row => row.Key == groups[g])) continue;
                made.Append("\n[[").Append(char.ToLowerInvariant(groups[g])).Append("]]").Append(heads[g]);
                foreach (KeyValuePair<char, string> row in rows)
                    if (row.Key == groups[g])
                    {
                        string[] lines = row.Value.Split('\n');
                        made.Append("\n[[").Append(groups[g]).Append("]]").Append(lines[0]);
                        for (int i = 1; i < lines.Length; i++) made.Append("\n").Append(lines[i]);
                    }
            }
            return none + made.Append("\n[[e]]\n\n").ToString() + zero;
        }

        // What her box says before a code mod runs, the same on every platform: what it asks for and what her scan saw,
        // what changed since it was approved, that it runs with full access to the PC, and its fingerprint.
        internal static string Consent(ModFile mod, string hash)
        {
            return mod.Name + (string.IsNullOrEmpty(mod.Version) ? "" : " v" + mod.Version) + (string.IsNullOrEmpty(mod.Author) ? "" : ", which says it's by " + mod.Author) + ", is a code mod.\n\n"
                + (mod.Checked ? CheckedNote : UncheckedNote) + "\n\n"
                + Grouped(mod)
                + (mod.Colours ? "Has custom colours: you can change them under GENERAL SETTINGS > Custom colours.\n\n" : "")
                + ChangedSince(mod)
                + "Code mods run as part of Capri with full access to your PC: your files, your accounts and the API keys she has saved. Nothing limits what it can do, and Capri doesn't check whether it's safe.\n\n"
                + "Only run it if you wrote it, or you've read it and trust who did.\n\nFile fingerprint (SHA-256):\n" + hash + "\n\nRun this code?";
        }
    }

    // The text half of the markup her questions read (Notice.cs draws it): kept here so every platform can show a
    // marked message as plain text.
    internal static partial class Marked
    {
        internal sealed class Line
        {
            internal char Kind = ' ';
            internal string Text = "";
            internal bool Open;
            internal readonly List<KeyValuePair<char, string>> Counts = new List<KeyValuePair<char, string>>();
        }

        internal static List<Line> Parse(string message)
        {
            List<Line> lines = new List<Line>();
            foreach (string raw in (message ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                Line line = new Line();
                string text = raw;
                if (text.StartsWith("[[", StringComparison.Ordinal))
                {
                    int close = text.IndexOf("]]", StringComparison.Ordinal);
                    string tag = close > 2 ? text.Substring(2, close - 2) : "";
                    if (tag.Length == 1 && "HMLhmlce".IndexOf(tag[0]) >= 0) { line.Kind = tag[0]; text = text.Substring(close + 2); }
                    else if (tag == "f+" || tag == "f-") { line.Kind = 'f'; line.Open = tag == "f+"; text = text.Substring(close + 2); }
                }
                if (line.Kind == 'c')
                {
                    string[] parts = text.Split('|');
                    text = parts[0];
                    for (int i = 1; i + 1 < parts.Length; i += 2) if (parts[i].Length == 1) line.Counts.Add(new KeyValuePair<char, string>(parts[i][0], parts[i + 1]));
                }
                line.Text = text;
                lines.Add(line);
            }
            return lines;
        }

        // The message as plain text, for copying, a screen reader and Windows' own box: rows as "- text", every row shown.
        internal static string Plain(string message)
        {
            if ((message ?? "").IndexOf("[[", StringComparison.Ordinal) < 0) return message;
            List<string> said = new List<string>();
            foreach (Line line in Parse(message))
            {
                if (line.Kind == 'e') continue;
                if (char.IsUpper(line.Kind)) said.Add("- " + line.Text);
                else if (line.Kind == 'f') said.Add(line.Text + ":");
                else if (line.Kind == 'c')
                {
                    List<string> counts = new List<string>();
                    foreach (KeyValuePair<char, string> count in line.Counts) counts.Add(count.Value);
                    said.Add(line.Text + (counts.Count == 0 ? "" : " " + string.Join(", ", counts.ToArray())));
                }
                else said.Add(line.Text);
            }
            return string.Join("\n", said.ToArray());
        }
    }

    // A code mod's own saved values, kept in store.json in its folder between runs. The same on every platform.
    internal sealed class ModStore : ICapriStore
    {
        private readonly object gate = new object();
        private readonly Func<string> folder;
        private readonly string id;
        private Dictionary<string, string> store;

        internal ModStore(Func<string> modFolder, string modId)
        {
            folder = modFolder;
            id = modId;
        }

        private static System.Runtime.Serialization.Json.DataContractJsonSerializer Serializer()
        {
            return new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Dictionary<string, string>), new System.Runtime.Serialization.Json.DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
        }

        private Dictionary<string, string> Stored()
        {
            if (store != null) return store;
            store = new Dictionary<string, string>();
            string file = System.IO.Path.Combine(folder(), "store.json");
            try
            {
                if (System.IO.File.Exists(file))
                    using (System.IO.FileStream reading = System.IO.File.OpenRead(file))
                    {
                        Dictionary<string, string> read = Serializer().ReadObject(reading) as Dictionary<string, string>;
                        if (read != null) store = read;
                    }
            }
            catch (Exception error)
            {
                if (!(error is System.IO.IOException || error is UnauthorizedAccessException || error is System.Runtime.Serialization.SerializationException || error is System.Xml.XmlException)) throw;
                Brain.Log("mod " + id, -1, "Its store.json couldn't be read, so it starts empty: " + error.Message);
            }
            return store;
        }

        private void Save()
        {
            string file = System.IO.Path.Combine(folder(), "store.json"), writing = file + ".new";
            try
            {
                using (System.IO.FileStream output = System.IO.File.Create(writing)) Serializer().WriteObject(output, store);
                if (System.IO.File.Exists(file)) System.IO.File.Replace(writing, file, null); else System.IO.File.Move(writing, file);
            }
            catch (System.IO.IOException error) { Brain.Log("mod " + id, -1, "Its store.json couldn't be saved: " + error.Message); }
            catch (UnauthorizedAccessException error) { Brain.Log("mod " + id, -1, "Its store.json couldn't be saved: " + error.Message); }
        }

        private static string Key(string key)
        {
            string clean = (key ?? "").Trim();
            if (clean.Length == 0 || clean.Length > 100) throw new ArgumentException("A store key is 1 to 100 characters.");
            return clean;
        }

        public string[] Keys { get { lock (gate) { List<string> keys = new List<string>(Stored().Keys); keys.Sort(StringComparer.Ordinal); return keys.ToArray(); } } }
        public string Get(string key) { return Get(key, null); }

        public string Get(string key, string fallback)
        {
            string found;
            lock (gate) return Stored().TryGetValue(Key(key), out found) ? found : fallback;
        }

        public double Number(string key, double fallback)
        {
            double number;
            string found = Get(key, null);
            return found != null && double.TryParse(found, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number) ? number : fallback;
        }

        public bool Flag(string key, bool fallback)
        {
            string found = Get(key, null);
            return found == "true" ? true : found == "false" ? false : fallback;
        }

        public void Set(string key, string value)
        {
            string named = Key(key);
            if (value == null) { Remove(named); return; }
            if (value.Length > 64 * 1024) throw new ArgumentException("A stored value is at most 64 KB.");
            lock (gate)
            {
                Dictionary<string, string> kept = Stored();
                string was;
                if (kept.TryGetValue(named, out was) && was == value) return;
                long total = value.Length;
                foreach (KeyValuePair<string, string> entry in kept) if (entry.Key != named) total += entry.Key.Length + entry.Value.Length;
                if (total > 1024 * 1024) throw new InvalidOperationException("A mod's store holds at most 1 MB.");
                kept[named] = value;
                Save();
            }
        }

        public void Set(string key, double value) { Set(key, value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)); }
        public void Set(string key, bool value) { Set(key, value ? "true" : "false"); }

        public bool Remove(string key)
        {
            lock (gate)
            {
                if (!Stored().Remove(Key(key))) return false;
                Save();
                return true;
            }
        }
    }

    internal static class ModSound
    {
        // A WAV file in the mod's folder, 8, 16, 24 or 32 bit or float, made into her own 24 kHz sound, at most 30 seconds.
        internal static short[] ReadWave(string file, double volume)
        {
            byte[] bytes;
            try { bytes = System.IO.File.ReadAllBytes(file); }
            catch (System.IO.IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
            if (bytes.Length < 44 || System.Text.Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF" || System.Text.Encoding.ASCII.GetString(bytes, 8, 4) != "WAVE") return null;
            int format = 0, channels = 0, rate = 0, bits = 0, dataAt = -1, dataLength = 0;
            for (int at = 12; at + 8 <= bytes.Length;)
            {
                string chunk = System.Text.Encoding.ASCII.GetString(bytes, at, 4);
                int length = BitConverter.ToInt32(bytes, at + 4);
                if (length < 0) return null;
                if (chunk == "fmt " && at + 24 <= bytes.Length)
                {
                    format = BitConverter.ToUInt16(bytes, at + 8);
                    channels = BitConverter.ToUInt16(bytes, at + 10);
                    rate = BitConverter.ToInt32(bytes, at + 12);
                    bits = BitConverter.ToUInt16(bytes, at + 22);
                    if (format == 0xFFFE && at + 34 <= bytes.Length) format = BitConverter.ToUInt16(bytes, at + 32);
                }
                else if (chunk == "data") { dataAt = at + 8; dataLength = Math.Min(length, bytes.Length - dataAt); break; }
                at += 8 + length + (length & 1);
            }
            bool floats = format == 3 && bits == 32;
            if (dataAt < 0 || channels < 1 || channels > 8 || rate < 4000 || rate > 192000 || !(format == 1 && (bits == 8 || bits == 16 || bits == 24 || bits == 32) || floats)) return null;
            int step = bits / 8 * channels, frames = dataLength / step;
            int outCount = (int)Math.Min((long)frames * Sfx.Rate / rate, Sfx.Rate * 30L);
            if (outCount <= 0) return null;
            double gain = Math.Max(0, Math.Min(1, volume)) * Math.Min(100, Sfx.Level) / 100.0;
            short[] samples = new short[outCount];
            for (int i = 0; i < outCount; i++)
            {
                int frame = (int)Math.Min(frames - 1, (long)i * rate / Sfx.Rate);
                double sum = 0;
                for (int c = 0; c < channels; c++)
                {
                    int at = dataAt + frame * step + c * bits / 8;
                    double sample;
                    if (floats) sample = BitConverter.ToSingle(bytes, at);
                    else if (bits == 8) sample = (bytes[at] - 128) / 128.0;
                    else if (bits == 16) sample = BitConverter.ToInt16(bytes, at) / 32768.0;
                    else if (bits == 24) sample = ((bytes[at] << 8 | bytes[at + 1] << 16 | bytes[at + 2] << 24) >> 8) / 8388608.0;
                    else sample = BitConverter.ToInt32(bytes, at) / 2147483648.0;
                    sum += sample;
                }
                samples[i] = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, sum / channels * gain * 32767));
            }
            return samples;
        }
    }
}
