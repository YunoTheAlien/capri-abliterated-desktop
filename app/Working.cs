using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // Whether the thing in front of them is work: work slows her needs, since it is attention, just not on her. It
    // never makes her needier.
    internal static class Working
    {
        // Work apps she knows, by the fold each shows under when she has seen it; none counts as work unless they add it.
        // Each is a program name without its path, with a pattern after a bar when one name stands for several.
        internal const string Editors = "Editors", Games = "Games", Music = "Music", Browsers = "Browsers", Office = "Office", Calls = "Chat and calls", Other = "Other";
        internal static readonly string[] Kinds = { Editors, Games, Music, Browsers, Office, Calls, Other };

        private static readonly string[][] StockApps =
        {
            new[] { Editors, "code", "code-insiders", "codium", "vscodium", "devenv", "rider", @"idea|idea\d*", "pycharm", "webstorm", "clion",
                "goland", "phpstorm", "rubymine", "studio", "sublime_text", @"notepad++|notepad\+\+", "atom", "eclipse", "netbeans", "android studio",
                "xcode", "ssms", "datagrip", "dbeaver", @"pgadmin|pgadmin\d*", "mysqlworkbench", "mongodbcompass", "matlab", "rstudio", "spyder",
                @"jupyter|jupyter\w*", @"anaconda|anaconda\w*", "octave" },
            new[] { Office, "excel", "winword", "powerpnt", "onenote", "outlook", "msaccess", "mspub", "visio", "winproj", "soffice", "libreoffice",
                "scalc", "swriter", "simpress", "obsidian", "notion", "evernote", "todoist", "asana", @"jira|jira\w*", "confluence", "trello",
                "clickup", "linear", "acrord32", "acrobat", "foxitreader", "sumatrapdf", "calibre", "quickbooks", "xero", "sage", "myob", "tally",
                "stata", "spss" },
            new[] { Calls, "teams", "ms-teams", "slack", "zoom", "webex", "gotomeeting", "bluejeans", "ringcentral" },
            new[] { Other, "photoshop", "illustrator", "indesign", "afterfx", "premiere", "audition", "lightroom", "blender", "maya", "3dsmax",
                "houdini", "cinema4d", "zbrush", @"substance|substance\w*", "unity", "unityhub", @"unreal|unreal\w*", @"ueeditor|ue\d*editor", "godot",
                "figma", "xd", "sketch", @"affinity|affinity\w*", "gimp", "inkscape", "krita", @"davinci|davinci\w*", "resolve", "putty", "mobaxterm",
                "winscp", "filezilla", "cmd", "powershell", "pwsh", "windowsterminal", "wt", "conemu", "alacritty", "wezterm", "docker desktop",
                "postman", "insomnia", "fiddler", "wireshark", "autocad", "revit", "solidworks", "fusion360", "inventor", "altium", "kicad", "eagle",
                "labview", "teamviewer", "anydesk", "mstsc", @"vmware|vmware\w*", "virtualbox", "vmconnect" }
        };

        // Each stock name, its pattern and its kind.
        private static readonly Dictionary<string, string> stockPattern = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> stockKind = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        internal static readonly string[] StockNames = Unpack();

        private static string[] Unpack()
        {
            List<string> names = new List<string>();
            foreach (string[] kind in StockApps)
                for (int i = 1; i < kind.Length; i++)
                {
                    string[] parts = kind[i].Split(new[] { '|' }, 2);
                    stockPattern[parts[0]] = parts.Length > 1 ? parts[1] : Regex.Escape(parts[0]);
                    stockKind[parts[0]] = kind[0];
                    names.Add(parts[0]);
                }
            return names.ToArray();
        }

        // The apps that count as work: only the ones they added, kept in her settings. Her own list only sorts the apps
        // she has seen into kinds.
        private static string[] apps = new string[0];
        private static Regex appsMatch;

        internal static string[] Apps
        {
            get { lock (gate) return (string[])apps.Clone(); }
            set
            {
                List<string> kept = new List<string>();
                foreach (string one in value ?? new string[0])
                {
                    string name = Plain(one);
                    if (name.Length > 0 && !kept.Exists(had => string.Equals(had, name, StringComparison.OrdinalIgnoreCase))) kept.Add(name);
                }
                lock (gate) { Settle(Real()); apps = kept.ToArray(); appsMatch = Matcher(apps); checkedAt = DateTime.MinValue; }
            }
        }

        // Focus on or off, from its switch: off, nothing counts as work and the last step stops nothing.
        private static volatile bool on = true;

        internal static bool On
        {
            get { return on; }
            set { lock (gate) { Settle(Real()); on = value; checkedAt = DateTime.MinValue; } }
        }

        private static Regex Matcher(string[] names)
        {
            if (names.Length == 0) return null;
            StringBuilder all = new StringBuilder();
            foreach (string name in names)
            {
                string pattern;
                if (!stockPattern.TryGetValue(name, out pattern)) pattern = Regex.Escape(name);
                all.Append(all.Length == 0 ? "" : "|").Append(pattern);
            }
            return new Regex("^(" + all + ")$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        // A program as it is kept: its name without a folder or ".exe", so every copy of it counts.
        internal static string Plain(string program)
        {
            string name = (program ?? "").Trim();
            int slash = Math.Max(name.LastIndexOf('\\'), name.LastIndexOf('/'));
            if (slash >= 0) name = name.Substring(slash + 1);
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
            return name.Trim();
        }

        // The fold a program shows under: her own list's kind first, then the games, browsers, music players, editors and chat
        // apps she already tells apart; Other when none of them knows it.
        internal static string KindOf(string program)
        {
            string name = Plain(program), kind;
            if (stockKind.TryGetValue(name, out kind)) return kind;
            foreach (KeyValuePair<string, string> one in stockPattern)
                if (one.Value != Regex.Escape(one.Key) && Regex.IsMatch(name, "^(" + one.Value + ")$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return stockKind[one.Key];
            if (Favourites.Played(name) || Computer.GameNamed(name)) return Games;
            switch (Favourites.KindOfProgram(name))
            {
                case "browser": return Browsers;
                case "music": return Music;
                case "code editor": case "text editor": return Editors;
                case "chat": return Calls;
                case "calendar": return Office;
            }
            return Other;
        }

        // While working, from her settings: how many times slower what it stretches runs while they work, so a desk day
        // ends with her wanting something, not sulking by lunchtime. At Never it stops it all, whatever is in front.
        private static volatile int times = Stock;
        internal const int Stock = 4, Least = 2, Most = 10, Never = 11;

        internal static int Times
        {
            get { return times; }
            set { lock (gate) { Settle(Real()); times = value; } }
        }

        internal static bool Stopped { get { return on && times >= Never; } }

        // What While working does to each timer while it applies: stretched by its times, paused, or left at its own
        // pace. Her wandering's Lock is a pause.
        internal enum Hold { Extend, Pause, Leave }

        internal const string Chatter = "chatter", Pet = "tamagotchi", CheckIns = "check-ins", Wandering = "wandering", ModMark = "mod:";
        internal static readonly string[] Hers = { Chatter, Pet, CheckIns, Wandering };

        private static readonly Dictionary<string, Hold> holds = new Dictionary<string, Hold>(StringComparer.OrdinalIgnoreCase);

        internal static Hold Usual(string row) { return row == CheckIns ? Hold.Leave : row == Wandering ? Hold.Pause : Hold.Extend; }

        internal static Hold HoldOf(string row)
        {
            lock (gate)
            {
                Hold had;
                return holds.TryGetValue(row ?? "", out had) ? had : Usual(row);
            }
        }

        internal static void SetHold(string row, Hold hold) { lock (gate) { Settle(Real()); holds[row] = hold; } }

        // Every row as kept in her settings, one "row=Hold" a line; a mod's row is kept at its usual too, so it stays listed.
        internal static string HoldsSaid
        {
            get
            {
                lock (gate)
                {
                    StringBuilder said = new StringBuilder();
                    foreach (KeyValuePair<string, Hold> one in holds)
                        if (one.Key.StartsWith(ModMark, StringComparison.Ordinal) || one.Value != Usual(one.Key)) said.Append(one.Key).Append('=').Append(one.Value).Append('\n');
                    return said.Length == 0 ? null : said.ToString();
                }
            }
            set
            {
                lock (gate)
                {
                    Settle(Real());
                    holds.Clear();
                    foreach (string line in (value ?? "").Split('\n'))
                    {
                        int at = line.LastIndexOf('=');
                        Hold hold;
                        if (at > 0 && Enum.TryParse(line.Substring(at + 1).Trim(), true, out hold)) holds[line.Substring(0, at).Trim()] = hold;
                    }
                }
            }
        }

        // The mods that have made a timer that waits, by id.
        internal static List<string> Mods()
        {
            List<string> ids = new List<string>();
            lock (gate) foreach (string row in holds.Keys) if (row.StartsWith(ModMark, StringComparison.Ordinal)) ids.Add(row.Substring(ModMark.Length));
            return ids;
        }

        // A mod's first timer lists it, at Extend. True when it wasn't listed yet, so its owner saves.
        internal static bool AddMod(string id)
        {
            lock (gate)
            {
                if (string.IsNullOrEmpty(id) || holds.ContainsKey(ModMark + id)) return false;
                Settle(Real());
                holds[ModMark + id] = Hold.Extend;
                return true;
            }
        }

        // The mods no longer installed lose their rows. True when one went.
        internal static bool KeepMods(Predicate<string> installed)
        {
            bool gone = false;
            foreach (string id in Mods())
                if (!installed(id)) lock (gate) { holds.Remove(ModMark + id); gone = true; }
            return gone;
        }

        // How fast a row's time runs now: once while not working, its hold while working, and not at all at Never.
        internal static double Rate(string row, bool working)
        {
            if (Stopped) return 0;
            if (!working) return 1;
            switch (HoldOf(row))
            {
                case Hold.Pause: return 0;
                case Hold.Leave: return 1;
                default: return 1.0 / Math.Max(Least, Math.Min(Most, times));
            }
        }

        // How many times longer a wait of that row is now; endless while it's paused.
        internal static double Stretch(string row, bool working)
        {
            double rate = Rate(row, working);
            return rate <= 0 ? double.PositiveInfinity : 1 / rate;
        }

        // What is in front, supplied so this needs no window handling of its own and the checks can drive it.
        internal static Func<Sight> Ahead = delegate { return null; };

        // The clock each row's time is counted by, swapped by the checks.
        internal static Func<DateTime> Real = delegate { return DateTime.UtcNow; };

        private static readonly object gate = new object();
        private static bool was;
        private static DateTime checkedAt = DateTime.MinValue;

        // Read at most once a second: this is called from her tick and a window look is not free.
        internal static bool Now(DateTime when)
        {
            if (Stopped) return true;
            lock (gate)
            {
                if (checkedAt != DateTime.MinValue && (when - checkedAt).TotalSeconds < 1) return was;
                checkedAt = when;
                Sight front = null;
                try { front = Ahead(); } catch (Exception) { front = null; }
                bool working = on && Is(front);
                if (working != was)
                {
                    Settle(Real());
                    was = working;
                    // Logged each time, so it's plain when focus started or stopped applying by itself.
                    Brain.Log("modes", -1, working ? "Focus applies: " + Plain(front.Program) + " is in front, timers x" + times + "." : "Focus stopped applying.");
                }
                return was;
            }
        }

        // For the checks: the next look reads what is in front again.
        internal static void Forget() { lock (gate) checkedAt = DateTime.MinValue; }

        internal static bool Is(Sight front)
        {
            if (front == null) return false;
            string program = Plain(front.Program);
            Regex match;
            lock (gate) match = appsMatch;
            return program.Length > 0 && match != null && match.IsMatch(program);
        }

        // Seconds each row has run, counted at its rate: a timer that waits on a row is due when this has gone past its mark.
        private static readonly Dictionary<string, double> counted = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private static DateTime countedAt = DateTime.MinValue;

        // Every row's count brought up to now at the rate it ran since the last change.
        private static void Settle(DateTime now)
        {
            if (countedAt != DateTime.MinValue)
            {
                double real = (now - countedAt).TotalSeconds;
                if (real > 0) foreach (string row in new List<string>(counted.Keys)) counted[row] += real * Rate(row, was);
            }
            countedAt = now;
        }

        internal static double Clock(string row)
        {
            lock (gate)
            {
                Settle(Real());
                double had;
                if (!counted.TryGetValue(row, out had)) counted[row] = had = 0;
                return had;
            }
        }

        // A row's rate by what was in front at the last look, for a timer off her tick.
        internal static double RateNow(string row) { lock (gate) return Rate(row, was); }
    }
}
