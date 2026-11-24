# Making Capri mods

Capri reads four kinds of mod. Import any of them from **Mainframe > CAPRI > Mods > Import mod**, or drop the
file into her mods folder (**Mods > Mods folder**) while she runs: she takes it in the same way, a code mod
after her own check and your yes. Take a file out of that folder and its mod comes out of her.

- `.capri-mod`: Settings and drop-in packs: extra names, spellings, colours, the hearing log, and her lines and numbers taken over or added to
- `.capri-skin`: A zip of her sprite strips that changes how she looks. Several **stack**: each one turned on goes on top
- `.capri-skill`: Know-how for a game that she follows when she plays it
- `.capri-code`: C# that runs inside Capri, on Windows, Mac and Linux

Share finished mods in [capri-mods](https://github.com/lachydotmcg/capri-mods).

## Stacking skins

Turn on several skins and they stack, newest on top. The bottom one is drawn whole. Each skin above it only adds what it
changes from her own art, so a skin that's her own look plus cat ears puts just the ears over the outfit below.

An accessory is a `.capri-skin` whose `skin.json` has `"accessory": true`. It's see-through except what it adds, and goes on
top as it is:

```json
{ "format": 1, "id": "cat-ears", "name": "Cat Ears", "author": "You", "description": "Cat ears for any outfit.", "accessory": true }
```

- Each strip is the same size as hers, see-through everywhere except what you add (ears, a hat, glasses). Only include the strips you draw on.
- It's drawn over whatever is under it in the stack, or her own look.
- The Mods list shows each skin's layer. Turn one off and on again to move it to the top.
- Her colours go on after, so anything drawn in her hair colour follows My Colors.

## Personalities and moods

A `.capri-mod` can bring whole personalities and moods. Each one is a name and the text of its `.md` file, written
the same way as hers (open a personality in her editor to see the layout):

```json
{ "format": 1, "id": "night-owl", "name": "Night Owl", "author": "You", "description": "A sleepy night-shift personality.",
  "personalities": [ { "name": "Night Owl", "text": ">! Wide awake at 3am and proud of it.\n\n## Her character\n\nDrowsy, dry and fond of you." } ],
  "moods": [ { "name": "Wired", "text": ">! Too much coffee.\n\n## How it changes her\n\nShe talks faster." } ] }
```

- A line starting `>!` is a note for whoever reads the file; she skips it. A line starting `>` alone is a line
  switched off, the way she reads it in every file of hers.
- Turn the mod on and they appear in her personality and mood lists. Turn it off or remove it and they go again.
- A file you have edited since stays. A personality or mood she already has with that name is never written over.
- `"senpai": true` on one marks it for abliterated mode, the switch at the bottom of her `sys.md`. It is only
  written while that mode is on, and switching back off leaves it where it is. Abliterated mode lowers her
  guardrails, and it stays behind that switch and the terms beside it: the person turning it on accepts those
  terms and takes responsibility for doing so.

## Drop-in packs

A drop-in pack is a `.capri-mod` with `"format": 2`. It changes her without code: it takes over parts of her files,
adds to them, or both, and everything goes back to hers when it is off. The template in `mods/template` has every
kind in one pack (`night-owl.capri-mod`), with a file for each part explaining it, ready to copy.

**Mainframe > Mods > Make a mod** opens the mod helper: pick one of her files and a part of it, change its text or
take it from a file, and add it to your pack. Reload lays the pack on her so you see it at once; change a part and
Reload again. Save pack writes it to `made` in her mods folder, ready to share or drop in.

```json
{ "format": 2, "id": "night-owl-pack", "name": "Night Owl", "version": "1.0.0", "author": "You",
  "description": "A sleepy night-shift Capri.",
  "overrides": [ { "slot": "lines.md > openers warm", "text": "Mm. Hi.\nStill up? Same." },
                 { "slot": "chess.md", "whole": true, "text": "# Chess, after midnight\n\n## When she wins\n\nMate. Now we can both sleep." } ],
  "adds": { "acts": [ { "name": "coffee", "kind": "food", "like": 3 } ],
            "mood": [ { "act": "coffee", "add": 1 } ] },
  "names": { "name": "Owl", "wake": [ "owl", "hey owl" ] } }
```

### Slots and slots.md

A slot is one of her files and a heading in it, like `lines.md > openers warm`, or a whole file. She writes
`slots.md` in her mods folder at every start: every slot she has, what each holds (lines she says, numbers she reads,
or text) and which pack holds one now. Her safety rules and SECURITY are not slots.

### Overrides

1. `"slot": "file > heading"` takes over one heading. The rest of the file stays hers.
2. `"slot": "file", "whole": true` takes over the whole file. Write every heading you want her to use.
3. `text` is what goes there, as it would be in her file, with `\n` between rows. Numbers are written the same way,
   so a pack can change `personalities/Sassy.md > How she reacts` or `What she likes`.
4. Her files on disk are never touched. Up to 60 overrides, each up to 20000 characters.

### Adds

1. `adds.acts`: a new act, with a `name`, a `kind` (like `food`, `touch` or `words`) and a `like` from -5 to 5.
2. `adds.mood`: a term on an act's worth, hers or the pack's, from -3 to 3.
3. `names`: a name offered to Custom Names once, and wake names she answers to while the pack is on.
4. Adds never conflict. Two packs adding to the same act both count.

### Conflicts

When a pack turns on and takes a slot another pack holds, she asks which keeps it, file by file, and a file opens
into its headings so each heading can come from either. The choice is kept and asked again only when either pack
changes. Both packs' rows in Mods say what they hold and what they gave up.

### The act maths

Every act is worth something to her: her personality's like, plus each term from her mood, packs and mods, held to -5
to 5, then each multiplier from a code mod. Her log writes the sum for every act on a `mood` line, each part by
name, like this:

```
petted: Sassy 2 + Night Owl -1 = 1, sleepy pats x0.5 = worth 1
```

### The abliterated mark

`"senpai": true` on an override holds it back until abliterated mode is on, the switch at the bottom of her
`sys.md`, as it does for personalities and moods above. With the switch off that part isn't laid and the slot stays
hers.

### With code

A pack can't run code. A switch in her Toggles window, a multiplier, or an act that happens on its own comes from a
code mod beside it: `capri.Acts`, `capri.Mood`, `capri.Slots` and `capri.Toggles` below (API 6). The template's
`night-owl-switch.capri-code` is one.

## Code mods

A code mod is one C# 5 file (no `$"..."`, `?.` or `=>` members), starting with its details:

```csharp
// capri-mod: 1
// id: my-mod
// name: My Mod
// version: 1.0.0
// author: You
// description: What it does, up to 200 characters.
// permissions: chat, settings
// requires: api 3

using System;
using CapriDesktop;

public class MyMod : ICapriMod
{
    public void Start(ICapriHost capri) { }
    public bool Heard(string words) { return false; }
    public void Stop() { }
}
```

She asks before a code mod runs, and asks again whenever the file changes. The approve box lists the permissions it asks for. A code mod still has full access to the PC, so permissions describe what it does through Capri. They don't sandbox it.

`Start` gets an `ICapriHost`. Hooks run on her window's thread. If your handler throws, the error goes in `errors.txt` and she carries on. When the mod is turned off or removed, its hooks, timers, menu items, notes, options, drawing, brain tools, context, hotkeys and page are removed with it.

### If a mod breaks her

- A code mod that crashes her, freezes her window, or was starting when she stopped is switched off at her next start. She says which one and why.
- A mod whose code throws 5 times inside a minute is switched off while she runs, with a note.
- If she closes unexpectedly twice in a row with mods on and no one mod can be named, every code mod is switched off.
- A switched-off mod's row in Mods says SWITCHED OFF and why. Turning it back on clears that.
- `Capri.exe --no-mods` starts her once with every mod off, without changing what's saved. After an unexpected close she offers the same.

### On Mac and Linux

The same `.capri-code` file runs on Windows, Mac and Linux. Mac and Linux compile it as C# 5 too, so a mod that runs on one runs on the others. A few parts use Windows' own pictures and windows, so they're Windows only:

- `capri.Art`, Startup Intros (`ICapriIntro`, and `ICapriIntroNames` to list more than one in her Test menu), `Ui.Drawing` and `CapriFrame`
- `Ui.SetOptions`, `Ui.AddMenu`, `Ui.MenuOpening`, `Ui.DrawScale`, `Ui.BubbleOpacity`, `Ui.Switch`, `Ui.Button`, `Ui.Window`, `Mainframe.ShowPage`, `Mainframe.AddPanel` and `Mainframe.Window`
- `Screen.Looked`, `Screen.Capture` and `System.GameInFront`
- `System.Windows.Forms`, and `System.Drawing` pictures like `Bitmap` and `Graphics`. `Point`, `Rectangle`, `Size` and `Color` work everywhere.

A mod that uses one of those is refused on a Mac or Linux PC before she asks to run it, and her message names the part. Everything else works the same, and a few things a Mac or Linux PC can't do answer the way Windows does on a PC without them:

- `Keys.Hotkey` returns `false`, as if another app had the shortcut. A shortcut written wrong still throws.
- `System.Locked`, `Unlocked` and `Sleeping` never fire. `Woke` fires after the PC sleeps, and `Battery` is `-1` with no battery.
- `Voice.Heard` never fires, since voice chat isn't on Mac and Linux yet.
- `Her.SitOn` says it's Windows only.

The Hooks Tour sample draws on her and has an options page, so it's Windows only.

`// requires:` is optional. `api 3` stops the mod importing into an older Capri with a clear message instead of a compile error. A mod id, like `focus-timer`, means that mod must be installed and on before this one starts. Separate them with commas.

`// assets: door.png, knock.wav` is optional too. A mod that carries pictures or sounds as packed text in its code names them here, by file name, up to 40. Her question then says it carries those files rather than calling the packed text hidden, and it isn't asked about twice. It never lets through a mod that unpacks text and loads it as code: that is refused whatever this line says.

Her question also says what changed since you last approved a mod: its version then, any new permission, and anything new or gone in what her scan sees.

### Permissions

Using a hook without its permission throws a `CapriPermissionException` that names the missing one.

- `chat`: Read and change what you and Capri say, and talk to her for you
- `transcripts`: Read her saved chat transcripts
- `memory`: Read, add, change and delete her notes about you and her memories
- `screen`: Take screenshots and see what she sees on your screen
- `voice`: Hear what your mic picks up and change what she says out loud
- `personality`: Add to her personality and change what she calls you
- `settings`: Read and change her settings, like her size and volume
- `skins`: Change her outfit, and put accessories on and off
- `brain`: Ask her brain questions, which can use your API credits
- `tools`: Add tools her brain can use by itself, and see or stop the tools she's about to use
- `system`: Know when you're away or back, when your PC locks, sleeps or wakes, and its battery
- `hotkeys`: Add keyboard shortcuts that work in any app
- `music`: See what's playing, control your music apps and their volume, and hear how loud your PC's sound is

### The host

- `Folder`: A data folder of the mod's own
- `Say(line)`: She says it, out loud if her voice is on
- `Log(line)`: A line in `errors.txt`
- `Art(name)`, `Sound(name)`, `PlayIntro()`: Her sprite strip, one of her sounds, a startup intro
- `Api`: `4` here. Settings panels (`Mainframe.AddPanel`), menus of a mod's own (`Ui.AddMenu`, `Ui.MenuOpening`), `Ui.DrawScale`, `Ui.BubbleOpacity`, `Ui.Switch`, `Ui.Button` and `System.GameInFront` came in 4; brain tools, context, the store, system events, hotkeys, sounds, music, pages, moving her and mod messages in 3; the other hooks in 2
- `CapriVersion`, `Has(permission)`: Her version, and whether this mod asked for a permission

### `capri.Chat`

- `Sending` (needs chat): What they typed or said, before her brain sees it. Change `Text`, or set `Cancel` and she ignores it
- `Replying` (needs chat): Her reply before it shows and is spoken. Change `Text`
- `Replied` (needs chat): Her line once it shows. `Source` is `reply` or `remark`
- `ToolUsed(tool, result)` (needs tools): She used one of her tools
- `ToolUsing` (needs tools): She's about to use a tool. `CapriToolCall` has `Tool`, `Doing` (like "open Spotify"), `Name`, `Action` and `Text`. Set `Cancel`, and `Reason` for what her brain is told instead
- `AddContext(func)`, `ClearContext()` (needs chat): `func` returns a fact for her prompt, read fresh for each message, like "Focus timer: 12 minutes left". Up to 5 per mod, 300 characters each. Her brain is told they're facts, not instructions
- `LastHeard`, `LastReply` (needs chat): The last thing she heard and said
- `Busy`: She's thinking, talking or on a job
- `Send(text)` (needs chat): As if they typed it. `Sending` sees `Source == "mod"`. It never answers her asking first: when SECURITY holds a tool, only their own yes runs it
- `Start(about)` (needs chat): She starts a chat herself: her brain writes a line about it. False if she's busy
- `Days()`, `Transcript(day)` (needs transcripts): Days with a transcript, and one day's transcript

### `capri.Memory` (memory)

- `Notes`: Her notes about them, from `me.md`
- `Remember(fact)`, `Forget(about)`: Add or delete notes, like her own tool does. Returns her line about it
- `Update(oldText, newText)`: Rewrite the first note containing `oldText`
- `Set(label, value)`: One `- Label: value` note, replaced in place
- `Memories`, `WriteMemories(text)`: Read or rewrite `memories.md`

### `capri.Ui`

- `Drawing`: Draw over her every frame she's drawn. `CapriFrame` has `Graphics`, `Window`, `Body`, `Head`, `FacingLeft`, `Animation`, `Frame` and `Size`
- `Redraw()`: Draw her again now, for drawing that changes
- `DrawScale`: How much bigger she draws, `1` to `1.15`, about her seat or feet like her own beat pulse and on top of it. Her size setting never changes. Set it as often as each frame, for a pulse of the mod's own; it stays at `1` while Motion is off, and goes back to `1` when the mod stops. Needs API 4
- `Switch(text, on, changed)`, `Button(text, clicked)`: Her own OFF/ON switch and key, in her colours and font and the column's width, to return from `Mainframe.AddPanel`'s `make` so a mod's panel looks like hers and follows Mainframe colours. `changed` gets the new state; a change or click that throws is the mod's failure, not hers. Needs API 4
- `BubbleOpacity`: How solid her speech bubble draws, `0.3` to `1`. With more than one mod asking, the most see-through wins. Back to `1` when the mod stops. Needs API 4
- `AddMenuItem(text, clicked)`: An item under **Mods** in her right-click menu
- `AddMenu(text)`: A menu of the mod's own under **Mods** in her right-click menu, in her menu's look. It returns an `ICapriMenu`: `Add(text, clicked)`, `AddCheck(text, on, changed)` for an item with a tick (`changed` gets the new state), `AddLine()`, `Clear()` and `Remove()`. `Add` and `AddCheck` return an `ICapriMenuItem` with `Text`, `Checked`, `Enabled` and `Remove()`, which can be changed any time. Up to 30 items and lines, 60 characters each. It goes when the mod stops. Needs API 4
- `MenuOpening`: Just before her right-click menu opens, to bring a menu's ticks and words up to date. Needs API 4
- `SetOptions(title, make)`: An **Options** button on the mod's row in Mods. `make` returns the WinForms `Control` to show
- `Bubble(text)`: A line in her bubble, not spoken
- `Window`: Her window
- `Colour(name, usual)`: A colour of the mod's own, by a name that says what it colours, up to 40 characters and 16 a mod. The first ask names it, so ask in `Start`. It comes back as they set it in **Custom colours**, under a fold-down of the mod's own below hers, or as `usual` in her current look, following Mainframe colours like her own. Kept in `colours.md` in the mod's own data folder. Its approval and its Mods row say it has custom colours. Needs API 5
- `ColoursChanged`: One of the mod's colours was set, or her look changed: ask `Colour` again. Needs API 5

### `capri.Mainframe`

`Opened` and `Closed` events, `Open`, `Show()`, `Hide()`, `ShowSettings()`, `ShowMods()` and `Window`, the Mainframe `Form`.

`ShowPage(title, make)` opens a page of the mod's own beside her Mainframe, up to 1200 × 900, for things bigger than options, like a dashboard. `make` returns the `Control`, made fresh each time. One page is open at a time. `ClosePage()` closes it, and it closes by itself when the mod stops.

`AddPanel(title, column, slot, make, simple)` puts a panel of the mod's own on her Settings page, in the same look as hers: `title` is its heading and `make` returns the `Control` under it, the column's width (328 px at 100%) and as tall as it is made. `column` is `"left"` or `"right"`, and `slot` is where in that column, 1 at the top. The panel in that slot and the ones under it move down. When two mods ask for the same slot, the one loaded first keeps it and the next goes straight after it. It shows in Advanced Settings only, unless `simple` is `true`. Where the panels sit is saved with her settings (settings.json), so it is the same after a restart, an update or an import, and when the mod stops or is taken out its panel goes and the panels under it move back up. Approving a mod that calls it shows "adds a Settings panel". Needs API 4: put `// requires: api 4` in its header. `AddPanel(title, column, slot, make, simple, group)` puts it inside a panel every mod naming the same `group` shares, like `"Yuno Mods"`: the group is the panel's heading, and `title` is this mod's sub-heading in it, in big text. The first part placed sets where the panel sits. A mod taken out takes its part, and the panel goes with the last one. Needs API 7.

```csharp
public void Start(ICapriHost capri)
{
    capri.Mainframe.AddPanel("Lights", "right", 3, delegate
    {
        return capri.Ui.Switch("Lights follow her mood", false, delegate(bool on) { capri.Log("lights " + on); });
    });
}
```

### `capri.Screen` (screen)

- `Capture()`, `Capture(area)`: A screenshot of the screen she's on, or an area. Dispose it when done
- `Looked`: She looked or glanced at their screen. The picture is only good during the handler

### `capri.Voice`

- `Speaking` (needs voice): Her words before her voice says them. Change `Text`, or `Cancel` to keep that line in her bubble
- `Started(text)`, `Finished` (needs voice): Her voice started or stopped
- `Heard(text, forHer)` (needs voice): Their mic heard words, and whether they were for her
- `Talking`: She's talking out loud
- `Speak(text)`, `Stop()`: Say a line, or stop her talking

### `capri.Timers`

`After(seconds, run)`, `Every(seconds, run)` and `At(time, run)` each return an `ICapriTimer` with `Cancel()`. They run on her thread.

`After` and `Every` follow While working: a mod's first one lists it under Work apps > What it slows, Extend to start, so its waits stretch, pause or keep their pace while a work app is in front, and stop at the infinity step. `At` is a time on the clock, like a reminder, and is never held.

### `capri.Personality` (personality)

`Add(note)`, `Remove(note)`, `Clear()` and `Notes` change lines in her prompt while the mod runs, up to 10 of 400 characters. They come after her safety rules and character, which they never override. `Nickname` gets or sets what she calls them.

### `capri.Settings` (settings)

These are the same switches and sliders her own settings tool uses. API keys and SECURITY are never here.

- `Switches`, `Sliders`: Their names, like `wander`, `dance_to_music`, `volume`
- `IsOn(name)`, `Turn(name, on)`: Read or flip a switch
- `Level(name)`, `Steps(name)`, `SetLevel(name, step)`: Read or move a slider by step
- `Size`: Her size in pixels, read or set
- `Changed`: Any of these changed, however it was changed

### `capri.Skins` (skins)

`Installed`, `Current`, `Accessories` and `IsAccessory(id)` read her wardrobe. `Use(id)` makes that skin her only outfit, or her own look with `null`. `Equip(id, on)` puts any skin on top of the stack, or takes it off.

### `capri.Her`

- `Bounds`, `Head`, `Size`: Where she is on screen
- `State`, `StateChanged`: `idle`, `thinking`, `talking`, `dancing`, `asleep` or `hidden`
- `Animation`, `Animate(name)`: The strip she's showing, and play one: `dance`, `stop_dancing`, `idle`, `grin`, `glitch` or `blink`
- `Clicked`, `Patted`: They clicked her, or gave her a head pat
- `Dragged`, `Dropped(hip)`, `Dragging`: They picked her up, or let go of her and where
- `Screen`: The working area of the monitor she's on
- `MoveTo(hip)`: She glitches over with her hip on that point, then drops onto whatever's under her
- `Move(action)`: Her own moves: `come_here`, `other_screen`, `sit`, `stand_up`, `lie_down`, `sit_up`, `walk_around`, `wander`, `hang`, `ride_cursor`, `hop_off`, `hide` or `show`. Returns her line about it
- `SitOn(window)`: Sit on top of a window they'd name, like `Discord` or `taskbar`

### `capri.Brain`

- `Ready` (needs brain): She has a brain set up
- `Ask(instructions, question, answered)` (needs brain): Ask her brain outside her chat. `answered` gets the reply on her thread, or `null` if it failed
- `AddTool(name, description, takes, run)` (needs tools): A tool her brain can pick by itself, like her own. `name` is letters, numbers and `_`. `description` tells her brain when to use it, and `takes` what its text is, or `""`. `run` gets the text and returns what her brain hears back. Up to 8 per mod. False if another running mod has that name. SECURITY > HER MODS > Use tools your code mods added decides whether she asks first: off on Lockdown and Guarded, on on Trusted and Blackwall
- `RemoveTool(name)`, `Tools` (needs tools): Take one away, or list this mod's

Her brain sees every mod's tools as one tool, `mod`, with a `name` to pick. Chat brains (Claude, OpenAI-style and local) get them. ElevenLabs voice chat doesn't yet. A friend on a call can't use them.

### `capri.Store`

The mod's own saved values, kept in `store.json` in its folder. `Get(key)`, `Get(key, fallback)`, `Number(key, fallback)`, `Flag(key, fallback)`, `Set(key, value)` for text, numbers and true/false, `Remove(key)` and `Keys`. Up to 64 KB a value and 1 MB in all. It's saved on every change.

### `capri.System` (system)

- `Away`, `Back`, `AwayMinutes`, `IsAway`: No mouse or keyboard for `AwayMinutes` (5 unless set), and when they're back
- `IdleFor`: How long since they last touched the mouse or keyboard
- `Locked`, `Unlocked`, `Sleeping`, `Woke`: The PC locked, unlocked, went to sleep or woke
- `NewDay`: Midnight passed while she was running
- `Battery`, `Charging`, `BatteryChanged(percent, charging)`: The battery percent, or `-1` with no battery
- `GameInFront`: A game's window is the one in front, by her own check, the one that lowers her over games. Read about once a second. Needs API 4

### `capri.Mods`

`Id`, `Running`, `IsRunning(id)`, `Send(topic, data)` to every other running mod, `SendTo(id, topic, data)` to one, and `Received(from, topic, data)`. Mods use these to work together, like a timer telling another mod to keep her quiet.

### `capri.Keys` (hotkeys)

`Hotkey(keys, pressed)` takes a shortcut in every app, like `Ctrl+Alt+F` or `F9`. It needs Ctrl, Alt or Win with a key, or an F key alone, so it can't take a key they type with. It returns false if another app already has it. `Remove(keys)` lets it go.

### `capri.Sounds`

`Play(file)` or `Play(file, volume)` plays a WAV file, from the mod's folder unless the path is a whole one, at her own sound volume times `volume` (0 to 1). 8, 16, 24 or 32 bit or float, up to 30 seconds. It returns false if the file can't be read. `Cue(name)` plays one of her own sounds, like `Sound(name)`.

### `capri.Music` (music)

What's playing, from the same Windows media session her mini player reads, and the presses that go back to the app playing it.

- `Changed(track)`: A new song, or `null` when nothing has a title any more
- `Updated(track)`: Every read, about once a second, and soon after a press
- `Now`: The `CapriTrack` it shows, or `null`
- `Sessions`: Every app with something in the media session, Windows' own pick first
- `Follow(appId)`, `Following`: Sticks to one app's session by its `AppId`; `null` goes back to whatever Windows picks
- `Play()`, `Pause()`, `Toggle()`, `Next()`, `Previous()`, `Seek(seconds)`: Presses, sent off her window's thread. False when there's nothing to press or the app says it can't
- `SetShuffle(on)`, `SetRepeat("off" / "one" / "all")`: For apps that take them
- `Liked`, `Like(on)`: Spotify's own heart for the song, or `null` elsewhere
- `Art`: The cover the app gave Windows, as PNG or JPEG bytes, or `null`
- `Volume`, `Muted`: The app's own volume in Windows' volume mixer, 0 to 1, or -1 before it has made a sound
- `Levels(bands)`, `Loudness`: How loud the PC's sound is in that many bands from deep bass to treble, each 0 to 1, for a visualizer. It listens to the speakers only while a mod keeps asking, and stops a few seconds after the last ask
- `BuiltInPlayer`: Set it to false to keep her own mini player away while the mod runs, for a mod that shows its own player
- `Open()`: Brings the app playing it to the front

A `CapriTrack` has `Title`, `Artist`, `Album`, `App` (a name to show, like `Spotify`), `AppId`, `Playing`, `Music`, `Video`, `Position` and `Length` in seconds at the read, `At()` for where it's got to by now, `CanSeek`, `CanSkip`, `CanBack`, `Shuffle` and `Repeat` (`null` when the app doesn't say), and a `Key` that's the same for the same song in the same app. On Mac and Linux nothing reads yet, so `Now` is `null` and presses return false.

### `capri.Acts` and `capri.Mood` (mood)

Her acts and how much each is worth to her. Needs API 6, so a mod using them says `// requires: api 6`.

- `Acts.Define(name, kind, like)`: A new act while the mod runs: a name of its own (not one of hers), a kind like `food` or `touch`, and how much she likes it, -5 to 5
- `Acts.Did(name)`: The act happened: she reacts and her mood moves, the same as her own
- `Acts.Happened(act, worth)`: Every act as it lands, with what it was worth
- `Acts.List`: Every act she has now: hers, packs' and mods'
- `Mood.Add(term, act, value)`: Adds -3 to 3 to an act's worth, under the term's name in her mood log
- `Mood.Multiply(term, act, value)`: Multiplies an act's worth by 0.25 to 2, after every add
- `Mood.Remove(term)`: Takes the term back
- `Mood.Warmth`, `Mood.Upset`, `Mood.Willing`, `Mood.Today`: How she feels now, and today's mood by name

A value past its limit throws `ArgumentOutOfRangeException`. Everything the mod added goes when it stops.

### `capri.Slots` (slots)

Her files' slots, as `slots.md` in the mods folder names them, like `lines.md > openers warm` or a whole file. Needs API 6.

- `Override(slot, text)`: Takes the slot over with this text, the same as a drop-in pack; false when it isn't a slot. Two holders of one slot get the conflict box
- `Release(slot)`: Gives it back
- `Holder(slot)`: Who holds it, or `null` while it's hers

### `capri.Toggles`

`Add(name, on, changed)` puts a switch of the mod's own in her Toggles window (right-click her > Toggles), under her own; `changed` gets the new state. It goes when the mod stops. Needs API 6.

### Screensavers (`ICapriScreensaver`)

A mod that draws a screensaver implements `ICapriScreensaver` beside `ICapriMod`: `Looks` names its looks, `Show(ask)` puts one up and returns whether it did, `Hide()` takes it down, and `Showing` says whether it is up. She decides when: the PC left alone as long as Toggles says, never over full screen, a stream or a job of hers. `ask` carries the look and her settings for it (size, speed, see through, screens, flashing, sound and volume, how long before a key may end it, and whether any key does). Her own is `capri-screensaver`, carried like the doors; its looks are what Test > Screensavers lists. Needs API 8.

## Plug-ins

A plug-in is a `.capri-plugin` zip (or a folder) with a `plugin.json`, an `.mcp.json` listing its MCP servers, and
any `SKILL.md` skills, the way Claude's plug-ins are laid out. Import it like a mod. She asks first, showing what it
starts, the keys it takes and any website it connects to.

1. A server in `.mcp.json` is a program she starts (`command`, `args`, `env`) or an address online (`url`, https).
   A program starts in the plug-in's folder and gets none of her keys, only the ones its `env` names.
2. Its tools reach her brain as one `plugin` tool, and what they give back reaches her as data from the plug-in,
   never as your words.
3. An online server that needs an account: Sign in, in the plug-in's security overrides, opens your own browser.
   The key it gives back is sealed for your Windows user and kept outside the mods folder, for that plug-in alone.
   A code mod that names where it is kept shows "reads plug-in sign-ins" (High) when you approve it.
4. SECURITY > HER MODS > Use tools your plug-ins added sets what they do by default. Each plug-in has a row in
   Mods > INSTALLED with its own switch: it goes in off and starts nothing until it is turned on. The cog left of
   the switch opens its security overrides, a cell per level for each tool: / follows SECURITY, a tick allows it
   there without asking, a cross denies it there. The websites it reaches are listed under them.
5. Every other row has the same cog. A code mod's has Runs (a cross stops the mod at that level) and a cell for each
   tool it added; the websites its code names are listed.
6. A changed plug-in asks again before any of it runs.

## Examples

- [hello-code.capri-code](hello-code.capri-code) answers "mod check".
- [hooks-tour.capri-code](hooks-tour.capri-code) grows or shrinks her when asked, counts head pats, draws a heart over her head, and adds a menu item and options.
- [startup-intros.capri-code](startup-intros.capri-code) plays a startup intro.
- [template](template/README.md) is a drop-in pack with every kind of part, and a code mod with a switch.
