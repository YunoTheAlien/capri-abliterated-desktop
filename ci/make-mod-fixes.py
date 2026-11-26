# Makes assets/mod-fixes.json: our fix for each of upstream's store mods by its file's SHA-256, from the adaption layer's
# verdicts. None of their text goes in beyond a step's anchors. MOD_UPSTREAM: their mods; MOD_ADAPTED: the adapted folder.
import hashlib, json, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
UP = os.environ.get("MOD_UPSTREAM") or os.path.join(ROOT, "agents", "mods-survey", "lachydotmcg-capri-mods-0ba7a7d", "mods")
AD = os.environ.get("MOD_ADAPTED") or os.path.join(ROOT, "Inbox", "Saph_Mods_main", "adapted")
OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "assets", "mod-fixes.json")
KINDS = (".capri-code", ".capri-mod", ".capri-skin", ".capri-skill")
ALLOW = {"capriplus-lyrics": ["lrclib.net"], "music-deck": ["lrclib.net"]}

# The hand patches as steps, in the order the adaption layer ran them.
PATCHES = {
    "russian-ui": [
        {"op": "base64-const", "name": "Dict"},
        {"op": "replace", "find": "Encoding.UTF8.GetString(Convert.FromBase64String(Dict))", "with": "Dict", "count": 1},
        {"op": "drop-his-lines", "name": "Dict"},
    ],
    "voice-trivia": [
        {"op": "cut", "from": "    private static string Download(string url)", "to": "    // With encode=url3986", "with": "    private static string Download(string url)\n    {\n        return null;\n    }\n\n"},
        {"op": "regex", "find": "        string url = \"https://opentdb\\.com/api\\.php[^\\n]*\\n", "with": "        string url = null;\n"},
        {"op": "replace", "find": "                    say.Add(\"Can't reach the trivia server, so you get my own questions.\");\n", "with": "", "count": 1},
        {"op": "replace", "find": "if (problem != null) capri.Log(\"Open Trivia DB: \" + problem);", "with": "if (problem != null) capri.Log(\"Trivia: \" + problem);", "count": -1},
        {"op": "regex", "find": "// Questions come from opentdb\\.com[^\\n]*\\n// how many\\. If it can't be reached, she asks her own questions from the list at the bottom\\.\\n", "with": "// Questions come from the list at the bottom.\n"},
    ],
    "music-deck": [
        {"op": "cut", "from": "    private void Search(Played played)", "to": "    // ---- Its menu ----", "with": "    private void Search(Played played)\n    {\n    }\n\n"},
        {"op": "replace", "find": "    // A song from the history: Spotify opens its search, anything else searches YouTube Music.\n", "with": "", "count": -1},
        {"op": "replace", "find": "private const string Agent = \"Capri Music Deck mod 1.1.0 (https://github.com/lachydotmcg/capri-desktop)\";", "with": "private const string Agent = \"Capri Music Deck mod 1.1.0\";", "count": 1},
    ],
    "capriplus-lyrics": [
        {"op": "replace", "find": "internal const string Agent = \"CapriPlus-Lyrics/1.0.0 (unofficial Capri community mod; https://github.com/lachydotmcg/capri-mods)\";", "with": "internal const string Agent = \"CapriPlus-Lyrics/1.0.0\";", "count": 1},
    ],
}


# Settings moved to her Toggles (API 9): a switch added beside where the mod reads its own, and a control of its own
# that did the same job taken out, so there is one place for it.
TOGGLES = {
    "capri-pad": [
        {"op": "replace", "count": 1, "find": "        autoPerch = capri.Store.Flag(\"auto_perch\", true);\n",
         "with": "        autoPerch = capri.Store.Flag(\"auto_perch\", true);\n        capri.Toggles.Add(\"Perch on the pad by herself\", autoPerch, delegate(bool on) { if (on != autoPerch) SetPerchPadActiveState(on); });\n"},
        {"op": "cut", "from": "        ToolStripMenuItem chk = new ToolStripMenuItem(\"Auto Perch Pad\");", "to": "    }\n\n    private void OnHerDragged()", "with": ""},
    ],
    "better-chat-capri": [
        {"op": "replace", "count": 1, "find": "        bubbleHidden = host.Store.Flag(\"hide_bubble\", false);\n",
         "with": "        bubbleHidden = host.Store.Flag(\"hide_bubble\", false);\n        host.Toggles.Add(\"Hide her bubble\", bubbleHidden, delegate(bool on) { if (on != bubbleHidden) ToggleBubble(); });\n"},
    ],
    "outfit-schedule": [
        {"op": "replace", "count": 1, "find": "        paused = capri.Store.Flag(\"paused\", false);\n",
         "with": "        paused = capri.Store.Flag(\"paused\", false);\n        capri.Toggles.Add(\"Paused\", paused, delegate(bool on) { paused = on; capri.Store.Set(\"paused\", on); });\n"},
    ],
    "track-commentator": [
        {"op": "replace", "count": 1, "find": "            every = saved;\n\n",
         "with": "            every = saved;\n"
                 "        capri.Toggles.Add(\"Commentary\", enabled, delegate(bool on) { enabled = on; Save(); });\n"
                 "        string[] everySteps = new string[20];\n"
                 "        for (int i = 0; i < everySteps.Length; i++) everySteps[i] = (i + 1).ToString();\n"
                 "        capri.Toggles.AddSlider(\"Every\", everySteps, every - 1, delegate(int at) { every = at + 1; Save(); });\n"
                 "        capri.Toggles.AddSlider(\"Language\", new[] { \"English\", \"Russian\" }, lang == \"ru\" ? 1 : 0, delegate(int at) { lang = at == 1 ? \"ru\" : \"en\"; Save(); });\n\n"},
    ],
    "battery-drama": [
        {"op": "replace", "count": 1, "find": "        lieDown = capri.Store.Flag(\"lie down\", true);\n",
         "with": "        lieDown = capri.Store.Flag(\"lie down\", true);\n"
                 "        for (int i = 0; i < Steps.Length; i++)\n"
                 "        {\n"
                 "            int index = i;\n"
                 "            capri.Toggles.Add(\"At \" + Steps[i] + \"%\", on[i], delegate(bool now) { on[index] = now; capri.Store.Set(\"step \" + Steps[index], now); });\n"
                 "        }\n"
                 "        capri.Toggles.Add(\"Lie down when low\", lieDown, delegate(bool now) { lieDown = now; capri.Store.Set(\"lie down\", now); });\n"},
        {"op": "replace", "count": 1, "find": "        capri.Ui.SetOptions(\"Low Battery Drama\", MakeOptions);\n", "with": ""},
    ],
    "focus-mode": [
        {"op": "replace", "count": 1, "find": "        rounds = (int)capri.Store.Number(\"rounds\", 4);\n",
         "with": "        rounds = (int)capri.Store.Number(\"rounds\", 4);\n"
                 "        Func<string[], int, int> near = delegate(string[] steps, int value) { int best = 0; for (int i = 0; i < steps.Length; i++) if (Math.Abs(int.Parse(steps[i]) - value) < Math.Abs(int.Parse(steps[best]) - value)) best = i; return best; };\n"
                 "        string[] workSteps = { \"15\", \"20\", \"25\", \"30\", \"45\", \"50\", \"60\", \"90\" }, breakSteps = { \"3\", \"5\", \"10\", \"15\", \"20\" };\n"
                 "        string[] longSteps = { \"10\", \"15\", \"20\", \"30\", \"45\" }, roundSteps = { \"2\", \"3\", \"4\", \"5\", \"6\", \"8\" };\n"
                 "        capri.Toggles.AddSlider(\"Work\", workSteps, near(workSteps, work), delegate(int at) { work = int.Parse(workSteps[at]); capri.Store.Set(\"work\", work); });\n"
                 "        capri.Toggles.AddSlider(\"Break\", breakSteps, near(breakSteps, rest), delegate(int at) { rest = int.Parse(breakSteps[at]); capri.Store.Set(\"break\", rest); });\n"
                 "        capri.Toggles.AddSlider(\"Long break\", longSteps, near(longSteps, longRest), delegate(int at) { longRest = int.Parse(longSteps[at]); capri.Store.Set(\"long break\", longRest); });\n"
                 "        capri.Toggles.AddSlider(\"Rounds\", roundSteps, near(roundSteps, rounds), delegate(int at) { rounds = int.Parse(roundSteps[at]); capri.Store.Set(\"rounds\", rounds); });\n"},
        {"op": "replace", "count": 1,
         "find": "        NumericUpDown w = Number(page, \"Work (minutes)\", work, 1, 240);\n        NumericUpDown b = Number(page, \"Break (minutes)\", rest, 1, 60);\n        NumericUpDown l = Number(page, \"Long break (minutes)\", longRest, 1, 120);\n        NumericUpDown r = Number(page, \"Rounds before a long break\", rounds, 1, 12);\n",
         "with": ""},
        {"op": "replace", "count": 1,
         "find": "            work = (int)w.Value; rest = (int)b.Value; longRest = (int)l.Value; rounds = (int)r.Value;\n            capri.Store.Set(\"work\", work); capri.Store.Set(\"break\", rest); capri.Store.Set(\"long break\", longRest); capri.Store.Set(\"rounds\", rounds);\n",
         "with": ""},
    ],
    "touch-grass": [
        {"op": "replace", "count": 1, "find": "        Load();\n        lastTick = lastSaved = Clock();\n",
         "with": "        Load();\n"
                 "        string[] everySteps = { \"15\", \"30\", \"45\", \"60\", \"90\", \"120\", \"180\", \"240\" };\n"
                 "        int everyAt = 0;\n"
                 "        for (int i = 0; i < everySteps.Length; i++) if (Math.Abs(double.Parse(everySteps[i], CultureInfo.InvariantCulture) - every) < Math.Abs(double.Parse(everySteps[everyAt], CultureInfo.InvariantCulture) - every)) everyAt = i;\n"
                 "        capri.Toggles.AddSlider(\"Every (minutes of gaming)\", everySteps, everyAt, delegate(int at) { every = double.Parse(everySteps[at], CultureInfo.InvariantCulture); SaveSettings(); });\n"
                 "        capri.Toggles.Add(\"Quiet\", quiet, delegate(bool on) { quiet = on; SaveSettings(); });\n"
                 "        lastTick = lastSaved = Clock();\n"},
    ],
    "soundboard": [
        {"op": "replace", "count": 1, "find": "        Bind();\n        capri.Ui.SetOptions(\"Soundboard\", MakeOptions);\n",
         "with": "        Bind();\n"
                 "        capri.Toggles.Add(\"Soundboard\", on, delegate(bool now) { on = now; capri.Store.Set(\"on\", on); Bind(); });\n"
                 "        string[] volumeSteps = { \"0\", \"10\", \"20\", \"30\", \"40\", \"50\", \"60\", \"70\", \"80\", \"90\", \"100\" };\n"
                 "        foreach (Sound s in sounds)\n"
                 "        {\n"
                 "            Sound sound = s;\n"
                 "            capri.Toggles.AddSlider(sound.Name + \" volume\", volumeSteps, Math.Max(0, Math.Min(10, sound.Volume / 10)), delegate(int at) { sound.Volume = at * 10; capri.Store.Set(\"volume \" + sound.File, sound.Volume); });\n"
                 "        }\n"
                 "        capri.Ui.SetOptions(\"Soundboard\", MakeOptions);\n"},
        {"op": "replace", "count": 1, "find": "        page.Controls.Add(onBox);\n", "with": ""},
        {"op": "replace", "count": 1, "find": "TableLayoutPanel grid = new TableLayoutPanel { ColumnCount = 6, AutoSize = true };", "with": "TableLayoutPanel grid = new TableLayoutPanel { ColumnCount = 5, AutoSize = true };"},
        {"op": "replace", "count": 1, "find": "new[] { \"Sound\", \"Shortcut\", \"\", \"Volume\", \"Reaction\", \"\" }", "with": "new[] { \"Sound\", \"Shortcut\", \"\", \"Reaction\", \"\" }"},
        {"op": "replace", "count": 1,
         "find": "            NumericUpDown volume = new NumericUpDown { Minimum = 0, Maximum = 100, Increment = 10, Value = s.Volume, Width = 60 };\n            volume.ValueChanged += delegate { sound.Volume = (int)volume.Value; capri.Store.Set(\"volume \" + sound.File, sound.Volume); };\n            grid.Controls.Add(volume);\n",
         "with": ""},
    ],
    "capriplus-lyrics": [
        {"op": "replace", "count": 1, "find": "            host.Ui.SetOptions(\"Capri+ Lyrics\", MakeOptions);\n",
         "with": "            MakeOptions().Dispose();\n            host.Ui.SetOptions(\"Capri+ Lyrics\", MakeOptions);\n"},
        {"op": "replace", "count": 1, "find": "        private Control MakeOptions()\n",
         "with": "        private bool togglesMade;\n\n        private Control MakeOptions()\n"},
        {"op": "cut", "from": "            Func<string, bool, Action<bool>, CheckBox> check = delegate", "to": "            check(\"Show lyrics\"",
         "with": "            Func<string, bool, Action<bool>, CheckBox> check = delegate(string text, bool on, Action<bool> changed)\n"
                 "            {\n"
                 "                if (!togglesMade) capri.Toggles.Add(text, on, delegate(bool now) { changed(now); Saved(); });\n"
                 "                return null;\n"
                 "            };\n"
                 "            Func<string, int, int, int, int, Func<int, string>, Action<int>, TrackBar> slider = delegate(string text, int min, int max, int step, int value, Func<int, string> show, Action<int> changed)\n"
                 "            {\n"
                 "                if (togglesMade) return null;\n"
                 "                // Her Toggles sliders hold 21 steps at most, so a finer one takes every few.\n"
                 "                int wide = step * Math.Max(1, ((max - min) / step + 21) / 21);\n"
                 "                List<string> steps = new List<string>();\n"
                 "                for (int v = min; v <= max; v += wide) steps.Add(show(v));\n"
                 "                capri.Toggles.AddSlider(text, steps.ToArray(), Math.Min(steps.Count - 1, (Math.Max(min, Math.Min(max, value)) - min + wide / 2) / wide), delegate(int at) { changed(min + at * wide); Saved(); });\n"
                 "                return null;\n"
                 "            };\n"},
        {"op": "replace", "count": 1, "find": "            Button clear = new Button { Text = \"Forget saved lyrics\"",
         "with": "            togglesMade = true;\n            Button clear = new Button { Text = \"Forget saved lyrics\""},
    ],
    "music-deck": [
        {"op": "replace", "count": 1, "find": "        capri.Ui.SetOptions(\"Music Deck\", deck.Options);\n",
         "with": "        deck.Options().Dispose();\n        capri.Ui.SetOptions(\"Music Deck\", deck.Options);\n"},
        {"op": "replace", "count": 1, "find": "    public Control Options()\n    {\n",
         "with": "    private bool togglesMade;\n\n    public Control Options()\n    {\n"},
        {"op": "cut", "from": "        Action<string, bool, Action<bool>> check = delegate", "to": "        check(\"Take colours from the song's cover\"",
         "with": "        Action<string, bool, Action<bool>> check = delegate(string text, bool on, Action<bool> set)\n"
                 "        {\n"
                 "            if (!togglesMade) host.Toggles.Add(text, on, delegate(bool now) { set(now); settings.Save(host.Store); Recolour(); });\n"
                 "        };\n"},
        {"op": "cut", "from": "        Label sizeLabel = new Label", "to": "        Label keys = new Label",
         "with": "        if (!togglesMade)\n"
                 "        {\n"
                 "            string[] sizes = { \"80%\", \"90%\", \"100%\", \"110%\", \"120%\", \"130%\", \"140%\" };\n"
                 "            host.Toggles.AddSlider(\"Size\", sizes, Math.Max(0, Math.Min(6, (int)Math.Round((settings.Size * 100 - 80) / 10))), delegate(int at) { settings.Size = (80 + at * 10) / 100.0; settings.Save(host.Store); layerKey = null; });\n"
                 "            host.Toggles.AddSlider(\"Mode\", new[] { \"Full\", \"Mini\" }, mode == Mini ? 1 : 0, delegate(int at) { SetMode(at == 1 ? Mini : Full); });\n"
                 "        }\n"
                 "        togglesMade = true;\n"},
    ],
    "study-buddy": [
        {"op": "replace", "count": 1, "find": "        catch (Exception) { studyMode = false; }\n        Type hands",
         "with": "        catch (Exception) { studyMode = false; }\n        host.Toggles.Add(\"Study mode\", studyMode, delegate(bool on) { SetStudyMode(on); });\n        Type hands"},
    ],
}


def run(steps, text):
    for step in steps:
        if step["op"] == "replace":
            assert text.count(step["find"]) == step["count"], step["find"][:60]
            text = text.replace(step["find"], step["with"])
        elif step["op"] == "cut":
            a = text.index(step["from"]); b = text.index(step["to"], a)
            text = text[:a] + step["with"] + text[b:]
        else:
            raise ValueError(step["op"])
    return text


def mod_file(folder):
    if not os.path.isdir(folder): return None
    for f in sorted(os.listdir(folder)):
        if f.endswith(KINDS): return os.path.join(folder, f)
    return None


def sha(path):
    return hashlib.sha256(open(path, "rb").read()).hexdigest()


def version(path):
    t = open(path, encoding="utf-8", errors="replace").read(6000)
    v = re.search(r'(?m)^(?://\s*)?version:\s*(\S+)', t) or re.search(r'"version"\s*:\s*"([^"]+)"', t)
    return v.group(1) if v else None


def permissions(path):
    for line in open(path, encoding="utf-8").read().replace("\r\n", "\n").split("\n")[:60]:
        m = re.match(r"^\s*//\s*permissions\s*:\s*(.*?)\s*$", line)
        if m: return m.group(1)
    return None


# His addresses as ci/audit-servers.py lists them, so the importer refuses what that audit keeps out of her.
def his_addresses():
    text = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "audit-servers.py"), encoding="utf-8").read()
    block = text[text.index("his = ["):text.index("]\n", text.index("his = ["))]
    return re.findall(r're\.compile\(r"([^"]+)"', block)


def main():
    report = json.load(open(os.path.join(AD, "_translate.json"), encoding="utf-8"))
    mods = {}
    missing = []
    for verdict in ("adapted", "held", "rejected", "superseded", "shipped"):
        for folder, about in sorted(report.get(verdict, {}).items()):
            src = mod_file(os.path.join(UP, folder))
            if src is None: missing.append(folder); continue
            name = os.path.basename(src)
            entry = {"file": name, "sha256": sha(src), "version": version(src), "verdict": verdict}
            if verdict != "adapted":
                entry["why"] = about if isinstance(about, str) else about.get("why")
            else:
                done = os.path.join(AD, folder, name)
                if not os.path.exists(done): missing.append(folder + " (adapted file)"); continue
                entry["result"] = sha(done)
                steps = []
                if open(done, "rb").read() != open(src, "rb").read():
                    steps += PATCHES.get(folder, [])
                    steps.append({"op": "comment-links", "allow": ALLOW.get(folder, [])})
                    p = permissions(done)
                    if p is not None and name.endswith(".capri-code"): steps.append({"op": "permissions", "set": p})
                if folder in TOGGLES:
                    # Run on the adaption layer's result, so the checked hash is ours from here on.
                    raw = open(done, "rb").read().decode("utf-8")
                    nl = "\r\n" if "\r\n" in raw else "\n"
                    made = run(TOGGLES[folder], raw.replace("\r\n", "\n").replace("\r", "\n")).replace("\n", nl).encode("utf-8")
                    entry["result"] = hashlib.sha256(made).hexdigest()
                    entry["rewritten"] = True
                    steps += TOGGLES[folder]
                entry["steps"] = steps
                if about.get("toggles"): entry["toggles"] = about["toggles"]
            mods[folder] = entry
    json.dump({"format": 1, "mods": mods, "his": his_addresses()}, open(OUT, "w", encoding="utf-8", newline="\n"), indent=1, sort_keys=True)
    print("mods", len(mods), "missing", missing)


main()
