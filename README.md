<div align="center">

<img src="assets/capri.png" alt="Capri" width="240">

# Capri

**An overhaul of Lachy's Capri Desktop: bring Capri (back lol) to life as a desktop companion!**<br>
She sits on your windows, talks back, joins your calls, plays your games and gets into everything.

<a href="https://github.com/YunoTheAlien/capri-abliterated-desktop/releases/latest"><img src="https://img.shields.io/badge/Latest%20release%20for%20Windows-05d9e8?style=for-the-badge" alt="Download the latest release of this overhaul for Windows"></a>
<a href="https://github.com/lachydotmcg/capri-desktop"><img src="https://img.shields.io/badge/The%20original%20by%20Lachy-ff2a6d?style=for-the-badge" alt="Lachy's original Capri Desktop"></a>

<a href="https://github.com/YunoTheAlien/capri-abliterated-desktop/releases/latest"><img src="https://img.shields.io/github/v/release/YunoTheAlien/capri-abliterated-desktop?label=latest&color=05d9e8&style=flat-square" alt="Latest release"></a>
<a href="https://github.com/YunoTheAlien/capri-abliterated-desktop/releases"><img src="https://img.shields.io/github/downloads/YunoTheAlien/capri-abliterated-desktop/total?color=05d9e8&style=flat-square" alt="Downloads"></a>

<sub>This is an overhaul built on <a href="https://github.com/lachydotmcg/capri-desktop">Lachy's original Capri Desktop</a>, with huge credit to him and everyone who built it. His original and its <a href="https://discord.gg/capridesktop">Discord</a> are the place for his own builds. See <a href="#credits">Credits</a>. Zero telemetry.</sub>

</div>

<table align="center">
  <tr>
    <td align="center"><img src="assets/readme/idle.webp" alt="Capri idling and blinking" width="140"><br><sub>Hanging out</sub></td>
    <td align="center"><img src="assets/readme/talk.webp" alt="Capri talking" width="140"><br><sub>Talking</sub></td>
    <td align="center"><img src="assets/readme/tossed.webp" alt="Capri flailing while dragged" width="140"><br><sub>Getting tossed</sub></td>
    <td align="center"><img src="assets/readme/glitch.webp" alt="Capri glitching" width="140"><br><sub>Glitching</sub></td>
    <td align="center"><img src="assets/readme/sleep.webp" alt="Capri asleep, then booting back up" width="140"><br><sub>Napping</sub></td>
  </tr>
</table>

<table align="center">
  <tr>
    <td align="center"><img src="assets/readme/perch.webp" alt="Capri sitting on a window with her legs over the edge, blinking" width="110"><br><sub>Perching on your window</sub></td>
    <td align="center"><img src="assets/readme/perch-music.webp" alt="Capri swaying to music while she sits on a window" width="110"><br><sub>Swaying to your music</sub></td>
  </tr>
</table>

> [!NOTE]
> Capri is an unofficial fan project and is not approved or endorsed by CD PROJEKT RED, the anime's rights holders or its voice actors.

<p align="center"><sub>Press a title or picture to open that section.</sub></p>

<details>
<summary><h2><a name="mods"></a>Mods</h2><br><a name="mods-1"><img src="assets/readme/mods.png" alt="Preview cards for mods in the directory: the Bubblegum, Cyber Cyan, Royal and Toxic colour sets, Custom Names, Sharper Ears, Hearing Log, Hello Code and Startup Intros" width="800"></a></summary>

Press **Mods** at the top of the Capri Mainframe, beside Settings. Browse the [mods directory](https://github.com/lachydotmcg/capri-mods) with previews, downloads and ratings, install one with a click, import your own, and publish yours.

The directory has colour sets (Bubblegum, Cyber Cyan, Royal and Toxic), Custom Names, Sharper Ears, Hearing Log, Hello Code and Startup Intros. Her door intros come with her.

<p align="center">
  <img src="assets/readme/intros.png" alt="The Startup Intros mod's card showing its three intros: Hijack, Shatter and Wake" width="640">
  <br><sub>Startup Intros. Say "intro shatter" to lock one in, or "intro shuffle" to mix them up.</sub>
</p>

- **Settings mods** (`.capri-mod`) only change her settings, and **skins** (`.capri-skin`) change how she looks. Skins **stack**: turn on several and each goes on top, so a cat ears skin goes over any outfit
- **Code mods** (`.capri-code`) run code on your PC, so she asks before one runs. One that has changed since you approved it asks again and shows what changed, and the approve box groups what it can do by risk. They can hook her chat, voice, memory, settings, skins, menu and drawing, give her brain new tools, add hotkeys and react when you're away. See [mods/MODDING.md](mods/MODDING.md)
- A mod that stops her starting is switched off, and `Capri.exe --no-mods` starts her with none
- **The directory is Lachy's.** Other people write and update its mods. We check some and flag the problems we find, and her mod panel shows an issue on the spot when she catches one, but we can't promise every mod is safe or works across versions
- **Recolour her yourself:** press Custom colours on the Mods page and pick a colour for her Hair, Accents, Eyes, Edges or Body in the CAPRI fold. She saves it as a My Colors mod you can share
- **Share yours:** fork [capri-mods](https://github.com/lachydotmcg/capri-mods), add a folder with the mod, a README and a `preview.png`, then open a pull request

</details>

<details>
<summary><h2><a name="how-this-build-differs"></a>How this build differs from Lachy's 0.9.7</h2></summary>

Everything here is in this build and not in his. For his own features, see [his original](https://github.com/lachydotmcg/capri-desktop).

- **Moods and personalities that move.** Her mood and personality drift and ease back to the one you picked, Mood: Dynamic changes her mood every few hours, and each personality takes kindness, teasing and being ignored its own way.
- **She gets to know you.** She notices how you've seemed lately, learns your routine so her morning and evening talk follows your day, and goes through all her questions before one repeats.
- **Conversation comes first.** While you're talking, her jokes and pop-ins wait. A reply that only says your request back gets another go, and "no, I asked you to..." redoes it your way.
- **Quiet and focus that fit your day.** One click for Mute, the Quiet timer or Focus mode; she goes quiet by herself in calls and full-screen video; Focus stretches her timers while the apps you pick are in front.
- **A brain that doesn't drop out.** If your cloud brain fails, a brain on your PC takes over. Saved providers with your own names, local servers found for you, and Ollama started when it's installed.
- **Her whole interface in your language.** Menus, Mainframe and windows in French, Spanish, Italian, Portuguese, Brazilian Portuguese, Hindi and Polish, common labels in 72 more, and her settings notes in your language.
- **Mods you can read before you run them.** Drop a mod into her mods folder and she takes it in. Before it runs she shows what it reaches for and where, what changed since you approved it, and turns packed code back into something readable, without running any of it. Mod packs can change one part of her files without touching hers.
- **Custom colours.** Her speech bubble, Mainframe, player, menus and file editor, with presets, your last colours, a dropper and see-through colours; mods get their own folds.
- **Tamagotchi.** Points build while she runs; feed her and give her gifts she remembers, each personality with its own likes.
- **Touch that means something.** Head pats only on her head, the same at every size. Her top, top middle, bottom middle and bottom each get their own reaction, liked or not by her personality and mood. Pick her up and carry her around, and a click with Ctrl held is a high five.
- **Story time.** Each story carries a mood that follows hers, and her voice goes with it. Add your own.
- **Her voice per personality and mood,** a line's own feeling ("(joyful) You came back.") and her free voice in three sizes.
- **Words.** "spell check this" and "proofread this" with undo, plus "say <words>" and "read this aloud".
- **Streaming.** Streaming mode keeps your file names and window titles off camera, and Twitch chat can be read aloud with banned words bleeped.
- **Door intros.** A door that builds itself on her first start, and intro mods, one picked each start.
- **Meme lines,** said exactly as written.
- **Abliterated mode,** opt-in behind its own terms.

</details>

<details>
<summary><h2><a name="what-she-does"></a>What she does</h2><br><a name="what-she-does-1"><img src="assets/readme/mini-player.png" alt="Capri perched on her cyberpunk mini player while a song plays" width="300"></a> <a name="what-she-does-2"><img src="assets/readme/bubble.png" alt="Capri's speech box with the pencil and wave buttons" width="220"></a> <a name="what-she-does-3"><img src="assets/readme/mainframe.png" alt="The Capri Mainframe with her BRAIN and VOICE rows" width="170"></a></summary>

- **Talks back.** Type to her or voice chat, and turn on "Hey Capri" to call her by name. She answers in her own words, in your language.
- **Capri Remote.** Text her, call her and view your PC's screen from your phone's browser.
- **Talks out loud.** In her own free voice, a voice model of yours, or Fish Audio, ElevenLabs or OpenAI. See [Her voice](#her-voice).
- **Joins your calls.** She covers for you on Discord and other call apps, and launches what your friends pick (beta).
- **Plays your games.** She comments while you play, and she can play too. She's still learning.
- **Does things for you.** She opens apps, plays music, closes tabs and clicks through jobs with her own cursor. Talk to her mid-job, or press Esc to stop her. Say "shut up" and she stops at once.
- **Remembers you.** Your past chats and what she saw on your screen, kept on your PC. Her memories open in their own window to edit, forget or add to.
- **Has moods.** Her mood and personality drift and ease back to the one you picked; Mood: Dynamic changes her mood every few hours.
- **Sits on your stuff.** She perches on your windows and sways to your music, or drops onto an icon or the bottom of your screen.
- **Shows your music.** A little cyberpunk mini player, with her perched on it.

<details>
<summary><b>And a bunch more</b></summary>

- **Reminders:** "remind me on 5 December at 9am to call Mum", or "set a timer for 10 minutes"
- **Mini games:** Chess, Connect four, Tic-tac-toe and Rock, paper, scissors, from the Play menu in GAMES
- **Game controls:** teach her a game's controls and she reads them before her first move
- **Clips:** turn on Clip my games, then say "clip that" and she saves the last bit of your game
- **Live reactions:** short lines pop up over full-screen games when something happens
- **Watch along:** say "watch this with me" and she reacts to a video with you
- **Keeps an eye out:** "tell me when my health is low" or "let me know when the download finishes", with a brain that can see
- **Draws on your screen:** "draw me a cat" or "circle the start button"
- **Reads your files:** drag a file onto her, like a PDF, a Word doc or a picture
- **Explores while you're away:** turn it on and she scrolls the page under your mouse, paused YouTube pages and their comments included
- **Clones and chaos mode:** "make 5 clones", then hold Esc to clear them
- **Hologram:** clicks go straight through her
- **Tamagotchi:** earn points, feed her and give gifts (Toggles > Tamagotchi)
- **Custom colours:** her bubble, Mainframe and Player, presets, recent colours and a dropper
- **Plug-ins:** tools and skills, with Email, Notion and Linear starters, off until you turn them on
- **Streaming:** Twitch chat read aloud, Streaming mode that keeps file names and window titles off camera, and Capri Camera
- **Words:** "spell check this", "proofread this", "say <words>" and "read this aloud"
- **Quiet:** its own switch under CAPRI, for a random time between a min and max you set
- **Settings:** Export and Import keep all her settings in one file

</details>

Beware that Capri may try to take over your human CPU. We take no responsibility for her actions.

</details>

<details>
<summary><a name="codehusk-mode-1"><img src="site/assets/presence/clones.gif" alt="A crowd of Capri clones glitching" width="96" align="left"></a><h2><a name="codehusk-mode"></a>Code/Husk Mode</h2><br>She becomes Claude Code's, Codex's and OpenCode's body while they work<br clear="left"></summary>

She becomes Claude Code's, Codex's and OpenCode's body while they work. Leave them coding and go do something else: she acts it all out on your desktop, says when they need your OK, and tells you what they answered.

**Set it up:** Mainframe > Settings > Advanced Settings > BRAIN > **Code/Husk Mode**, then pick Off, Claude Code, Codex, OpenCode or All. If you picked Both before, it stays Claude Code + Codex until you pick something else. That installs her hooks, with backups, and keeps your other hooks and settings. Restart Claude Code. For Codex, restart it and trust her hooks when it asks. OpenCode has plugins instead of hooks, so she adds her own `capri.js` to its plugins folder (`~/.config/opencode/plugins`, or `OPENCODE_CONFIG_DIR`). Restart OpenCode to load it. It needs OpenCode 2.

<div align="center">

| When Claude Code, Codex or OpenCode... | She... |
| --- | --- |
| gets your prompt | perks up: "Claude's on it." |
| reads, edits, runs or searches | shows a quiet caption, like "Editing Brain.cs" |
| hits a failure | flinches: "Claude hit a snag." |
| needs your OK | tells you, out loud when her voice is on: "Claude needs your OK. Allow npm test?" |
| finishes | does her done reaction and shows a short answer. She reads it out when you're looking elsewhere |
| starts a subagent | sends out a clone of herself |
| compacts its memory | glitches: "There goes that memory." |

</div>

- **Ask her about it:** what Claude, Codex or OpenCode is doing, or today's prompts, finished tasks, edited files, errors and approvals
- **She only watches.** She never approves anything or controls the coding assistant. You answer in the coding app
- **Off** stops her listener and removes only her hooks and her OpenCode plugin
- With a cloud brain, the answers and actions she reads go to that brain to be shortened or talked about, with keys, tokens and passwords taken out first

</details>

<details>
<summary><a name="getting-started-1"><img src="site/assets/presence/plugged.gif" alt="Capri glitching as she powers up" width="96" align="left"></a><h2><a name="getting-started"></a>Getting started</h2><br>Three steps, and what your PC needs<br clear="left"></summary>

1. Download this overhaul's latest release, **[Capri-Setup](https://github.com/YunoTheAlien/capri-abliterated-desktop/releases/latest)**, and run it. Lachy's original is at **[his releases](https://github.com/lachydotmcg/capri-desktop/releases)**
2. In the Capri Mainframe, press **Set up her brain**, or press **Free key** to use a free NVIDIA key
3. Optional: pick another voice under **VOICE** (see [Her voice](#her-voice))

A Linux `.run` is on each release too. It is made from the Windows build by itself and is untested, so expect issues.

**Not signed yet.** This overhaul's Capri-Setup updates herself, but it isn't signed yet, so Windows or your antivirus may warn you as if it were a virus.

Get this overhaul only from [its GitHub releases](https://github.com/YunoTheAlien/capri-abliterated-desktop/releases), and the original only from [Lachy's GitHub](https://github.com/lachydotmcg/capri-desktop/releases).

**Zero telemetry.** No analytics, crash reports or usage pings. [SECURITY.md](SECURITY.md) lists what she connects to, what the website counts, and what to do if Windows or your antivirus warns you.

Stuck? Ask in the [Discord](https://discord.gg/capridesktop). [Windows or your antivirus may warn you](SECURITY.md#windows-or-your-antivirus-warned-me) because she's a new app. All the code is here.

<details>
<summary><b>What your PC needs</b></summary>

- Windows 10 or 11 (64-bit). Nothing else to install.
- A mic, if you want to voice chat.

<div align="center">

| | Brain on your PC | Brain from a key (like the free NVIDIA one) |
| --- | --- | --- |
| Memory | 8 GB, 16 GB is better | 4 GB |
| Graphics card | Optional, but 4 GB or more makes her faster | Not needed |
| Space | 3 to 5 GB | Under 100 MB |
| Internet | Only for the download | Always |

</div>

</details>

</details>

<details>
<summary><a name="how-to-use-her-1"><img src="site/assets/presence/cursor.gif" alt="Her glitching cursor" width="96" align="left"></a><h2><a name="how-to-use-her"></a>How to use her</h2><br>Type, talk, drag her around and ask for things<br clear="left"></summary>

- **Pencil:** type to her
- **Wave:** voice chat
- **Drag her:** sit her on a window
- **Right-click her** or the tray icon: menu and settings

Just ask her for things, like "open Spotify", "play Blinding Lights", "pause", "what's playing?" or "close Discord".

<details>
<summary><b>All controls</b></summary>

<div align="center">

| Control | What it does |
| --- | --- |
| Shift + drop | sit her right where you drop her |
| Ctrl + drop | she hangs on; click her to let go |
| Mic button | mute your mic in voice chat |
| "Hey Capri" | she glitches in and opens voice chat. Turn on Listen for "Hey Capri" under Settings > HABITS > Security (English for now) |
| Ctrl + Alt + V | start or stop voice chat (click its hint in the Mainframe to change it) |
| Ctrl + Alt + N | mute your mic (click its hint in the Mainframe to change it) |
| Ctrl + Alt + T | open her typing box from anywhere |
| Ctrl + Alt + H | hide or show her |
| Ctrl + Alt + P | Quiet: no comments, pop-ins or check-ins until you press it again. She still answers you |
| Call mode (beta) | in her typing box: she hears your call, and you say Capri for your requests |
| Esc | stop a job she's doing |
| Ctrl + Alt + C | get her off your cursor |
| Ctrl + Alt + M | open or close her Mainframe |
| Double-click her | she rides your cursor |
| Right-click, Move | sit here, hang here, lie down, ride the cursor, or move her to another screen |
| Watch my screen | in the Mainframe: she comments as you play |
| Help | in the Mainframe: opens the help page |

</div>

</details>

</details>

<details>
<summary><a name="her-voice-1"><img src="site/assets/presence/voice.gif" alt="Capri lying back on a voice call" width="96" align="left"></a><h2><a name="her-voice"></a>Her voice</h2><br>Free on your PC, a voice model of yours, or a cloud voice<br clear="left"></summary>

<div align="center">

| Under VOICE | What it is |
| --- | --- |
| **PREBUILT LOCAL** | Her own voices, free on your PC, in HQ, Mixed or LQ: the smaller two use less memory while she talks. **HD** copies a clip you pick: 6 GB graphics memory and 16 GB RAM recommended; 4 GB may fit with smaller chunks, or pick CPU for slower HD |
| **CUSTOM LOCAL** | A voice model you run: a Piper, Kokoro, Kitten or Pocket TTS file, or your own Kokoro-FastAPI, Fish Speech or Qwen3-TTS server |
| **CLOUD VOICE** | Fish Audio (free tier), ElevenLabs or OpenAI, with your own key |

</div>


> [!IMPORTANT]
> Our project-provided synthetic voice is in Capri's Mainframe and runs on your PC. You may connect third-party text-to-speech services, but we don't provide guides for voice cloning tools. Our only voice-design help is a text prompt for making a synthetic voice.
>
> Only use voices you have the necessary rights and permissions to use, and follow the provider's terms. We do not endorse unauthorized cloning or impersonation of voice actors or other individuals. **Capri prohibits cloning or impersonating a living person's voice without explicit, informed consent or other valid authorization.**

</details>

<details>
<summary><a name="make-her-yours-1"><img src="site/assets/presence/dancing.gif" alt="Capri dancing" width="96" align="left"></a><h2><a name="make-her-yours"></a>Make her yours</h2><br>What she calls you, her language, her voice and who she is<br clear="left"></summary>

Pick what she calls you, her language, her voice and what she does on your PC from the Capri Mainframe. To change who she is and how she talks, rewrite `personality.md`: Settings > YOU > Her personality > Edit personality.md. It starts as her default, and Reset to her default puts her back. For her colours, skins and new tricks, see [Mods](#mods).

- **Her safety rules are part of who she is.** They're fixed in her code, in `SafetyStock` in [app/Brain.cs](app/Brain.cs), right after her personality. If you build on her, please keep them as they are, out of respect for Capri's character
- Her personality is `DefaultPersonality` in [app/Brain.cs](app/Brain.cs), so a pull request can suggest a change for everyone

</details>

## Credits

This overhaul is built on [Lachy's original Capri Desktop](https://github.com/lachydotmcg/capri-desktop). Huge credit to Lachy and to everyone who built the original. Contributors are credited from the original repository; its full history is on [its contributors page](https://github.com/lachydotmcg/capri-desktop/graphs/contributors).

Inspired by jpartnma_'s TikTok video.

Contributors to the original: [lachydotmcg](https://github.com/lachydotmcg), [danielxxomg](https://github.com/danielxxomg), [nathanZW](https://github.com/nathanZW), [Vocyn](https://github.com/Vocyn), [fadievr](https://github.com/fadievr), [tuno473](https://github.com/tuno473), [coiai](https://github.com/coiai), [Aaqibhafeezkhan](https://github.com/Aaqibhafeezkhan), [Denoax](https://github.com/Denoax), [Denoaxx](https://github.com/Denoaxx), [Zak6571](https://github.com/Zak6571), [nikitchxd](https://github.com/nikitchxd), [painbaba](https://github.com/painbaba), [tob7as](https://github.com/tob7as), [swrd1337](https://github.com/swrd1337), [AlgorithmWiz](https://github.com/AlgorithmWiz), [AsLirith](https://github.com/AsLirith), [Givienne](https://github.com/Givienne), [jay23606](https://github.com/jay23606), [RajareCorp](https://github.com/RajareCorp), [Sharaj00](https://github.com/Sharaj00), [super-cr4SH](https://github.com/super-cr4SH).

Big thanks to **Vocyn** for the mods, **fadievr** for Capri VR, **Denoax** for the Mainframe's look on the original and **danielxxomg** for provider keys and silent installs, and to everyone in the Discord who sends ideas and bug reports.

Every feature this overhaul took or adapted from the original carries its credit in the release notes.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for how to build her, test her and send a pull request. Made a mod? Share it in [capri-mods](https://github.com/lachydotmcg/capri-mods).

## Licence

**Fan work:** Capri is an unofficial fan project and is not approved or endorsed by CD PROJEKT RED, the anime's rights holders or its voice actors.

**Voices:** see [Her voice](#her-voice) for what Capri supplies and what you may use with her.

**What is ours:** the code, none of it proprietary, with parts from other open source projects under their own licences ([THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)). The artwork, stills, sounds and voices are not ours. Strip what you do not own before passing any of it on.

Free for anything non-commercial: use her, share her, change her. No selling her or putting her in paid products. Credit Capri, with a link to this page, when you use any part of her: her code, her art and animations, or anything else. Full terms in [LICENSE.md](LICENSE.md) (PolyForm Noncommercial 1.0.0). The credit is one of its Required Notices, so anyone who passes on any part of her has to pass it on too.

Have fun and more updates to come!

<sub>Window perch and music perch animations: her artist, who prefers to stay private. Click effect: mixmix312352. Transformation animation she forms from when she teleports, and her two secret emotes: Qewiw (mix_qewiw1412_86144). Starter mods: Vocyn. Capri VR (Capri Room): fadievr. Mainframe theme on the original: Denoax. Setup guide based on Dancer's guide in the Discord. Font: Share Tech Mono (OFL). Free voice: Kokoro-82M (Apache-2.0). Her own ears: NVIDIA Parakeet TDT 0.6B v2 for English and Ultra for 25 European languages (weights by Moondream, trained from nvidia/parakeet-tdt-0.6b-v3; both CC-BY-4.0), int8 exports by sherpa-onnx. Both run with sherpa-onnx (Apache-2.0). HD voice: Qwen3-TTS 1.7B (Apache-2.0), run with qwentts.cpp (MIT). Her voice in your mic: VB-CABLE by VB-Audio, donationware, carried unchanged and installed only by its own setup. Links and licence texts: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).</sub>
