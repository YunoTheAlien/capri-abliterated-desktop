# Security and privacy

## Zero telemetry

The app has no analytics, no crash reports and no usage pings. We never receive your chat logs, transcriptions, screenshots, files, keys or notes.

Your settings, chats, transcripts, notes and error logs are stored on your PC, in `%LOCALAPPDATA%\Capri` on Windows. On Windows, keys you save are encrypted with a key worked out from this PC's hardware, so they survive a Windows reinstall but can't be read on another PC. On Mac and Linux they sit unencrypted in local files that only your account can read. If you pick an Obsidian vault for her notes, they live in that vault instead, and go wherever your vault syncs.

The [code is public](https://github.com/YunoTheAlien/capri-abliterated-desktop), so you can check all of this yourself.

## What the app connects to

| What | Where it goes | When |
| --- | --- | --- |
| Update checks | `api.github.com` | After she starts, then every three hours on Windows and every six on Mac and Linux, or when you check |
| Updates | `github.com` | On Windows a new release downloads by itself. On Mac and Linux you click through to it |
| Free voice and ears packs | GitHub releases | The installer gets the free voice. The app gets any missing free voice or ears pack when she starts |
| Brain, vision, HD voice and wake word packs | GitHub releases and `huggingface.co` | When you set one up or turn it on. If one goes missing while it's still on, she gets it again. Ollama downloads its own models |
| Local brains and HD voice | Addresses on your own PC | When she finds, loads and uses them |
| Cloud brains and voices | Your provider, or the server address you entered | After you connect one: model lists, tests, chats, speech and jobs. Greetings, screen watching and memory summaries can send requests by themselves, depending on your settings |
| Windows online speech | Microsoft, through Windows | During voice chat, when her own ears aren't ready or don't cover your language, if online speech is on in Windows |
| Searches, music and web links | DuckDuckGo, YouTube and the sites you ask for | When you ask for a search, a song or a site, or press a link. Your search words go to that site |

Anything online sees ordinary connection details, such as your IP address and the address asked for. GitHub hosts the downloads and keeps its own logs under its own privacy policy.

## Capri Remote and Room

This build has no relay of its own yet, so Capri Remote and Room can't be turned on and nothing goes to anyone else's.

## Your brain and voice providers

What you send to OpenAI, Claude, DeepSeek, NVIDIA, Google AI (Gemini), OpenRouter, Fish Audio or ElevenLabs goes straight from your PC to them with your own key, including NVIDIA's free key. A custom OpenAI-compatible provider or server gets what you send it at the address you entered. Those provider requests do not pass through anything of ours.

Google AI requests go to `generativelanguage.googleapis.com/v1beta/openai/`; OpenRouter requests go to `openrouter.ai/api/v1`. A brain key goes only to its provider, and authenticated brain requests do not follow redirects. A custom provider key goes only to the address you entered, and authenticated requests there do not follow redirects. OpenRouter gets `X-Title: Capri`, with no `HTTP-Referer` or user identifier added by Capri. OpenRouter routes the request to the provider serving the selected model, whose privacy policy also applies.

What a provider logs or keeps is up to them, under their own privacy policy.

- **A brain** can receive your chat history, transcriptions, notes, memories, screenshots, files and tool results, whatever the job needs.
- **A voice service** receives the words to speak and your voice settings.
- **ElevenLabs voice chat** also receives your mic audio, remembered context and tool results, and its hosted agent runs on Gemini.
- **Fish Audio's voice creation** uploads the clip you pick.

A local brain, her own ears and her local voices stay on your PC. A custom brain or voice server gets what you send it, wherever you pointed it.

## Windows or your antivirus warned me

This overhaul's builds on GitHub aren't code signed, and each new release starts with no reputation, so you may see **Windows protected your PC**. Press **More info**, then **Run anyway**.

Antivirus apps can flag her too. Taking screenshots, moving the mouse, pressing keys, listening to the mic and starting apps are her job, and they look a lot like what malware does. That usually means a false alarm, but an alert alone can't tell you a file is safe, so check it:

1. Only download this overhaul from [its release page](https://github.com/YunoTheAlien/capri-abliterated-desktop/releases/latest).
2. In PowerShell, from your download folder, run:

   ```powershell
   Get-FileHash -LiteralPath .\Capri-Setup-<version>.exe -Algorithm SHA256
   ```

3. Compare the `Hash` with the `Capri-Setup-<version>.exe` line in `SHA256SUMS.txt` on the same release. A match means you have the file we published.

Keep your antivirus on. If it quarantined her and the hash matches, restore that one file.

You can also build her yourself from the public code. [CONTRIBUTING.md](CONTRIBUTING.md) has what you need.

## Reporting a problem

Open an [issue](https://github.com/YunoTheAlien/capri-abliterated-desktop/issues). If it's a security hole, say only that you have one and leave the details out, so they stay private until it's fixed.
