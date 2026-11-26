using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // Tree files: a panel and its rows as plain lines, a dash per level, her own pieces in brackets and what each row
    // reaches in round brackets. Read once into a model; nothing reads the text after that. mods/templates shows one.
    internal static class PanelTree
    {
        // Her pieces a row can be, and the marks a piece can carry.
        internal static readonly string[] Kinds = { "buttonSmall", "buttonLarge", "buttonRow", "dropdown", "rungs", "lightPair", "slider", "sliderRow", "switch", "fold", "readout", "caption", "entry" };
        // "head" puts a key on its panel's heading band; "hidden" keeps a row out of sight until her code shows it.
        internal static readonly string[] Marks = { "flashing", "head", "hidden" };
        // What a row can reach: one of her windows, one of her commands, a setting, a list file, a state she keeps.
        internal static readonly string[] Reaches = { "window", "command", "setting", "list", "state" };

        internal sealed class Row
        {
            internal int Line, Depth;
            internal string Name, Kind, Reach, Target;
            internal readonly List<string> Flags = new List<string>();
            // Words after the brackets, such as a light pair's two halves ("No cloud / Local").
            internal string Words;
            // "+=" adds while the mod is on, "-=" takes one of hers away; a plain line adds.
            internal bool Takes;
            internal readonly List<Row> Under = new List<Row>();
            // The mod a laid row came from; null for hers.
            internal string From;
            // What its reach leads to, resolved once by Compile: a switch's get and set, a slider's steps, level and set,
            // a mod's own value, a command.
            internal Func<bool?> Get;
            internal Func<bool, string> Set;
            internal string[] Steps;
            internal Func<int> Level;
            internal Func<int, string> SetLevel;
            internal Func<string> Own;
            internal Action<string> SetOwn;
            internal Action Run;
            // "colour: hot" among its marks: one of her palette's colours, or "own: <name>" for the mod's own in Custom
            // colours; Ink reads it as the palette is now.
            internal string Colour;
            internal Func<System.Drawing.Color> Ink;
        }

        // Her palette's colours by name, the ones Mainframe colours and Custom colours set: Hot, Cyan, Rose, Ground...
        private static string[] palette;

        internal static string[] Palette
        {
            get
            {
                if (palette != null) return palette;
                List<string> names = new List<string>();
                foreach (System.Reflection.PropertyInfo one in typeof(Neon).GetProperties(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                    if (one.PropertyType == typeof(System.Drawing.Color) && one.GetIndexParameters().Length == 0) names.Add(one.Name);
                return palette = names.ToArray();
            }
        }

        internal sealed class Model
        {
            internal string Page;
            internal readonly List<Row> Panels = new List<Row>();
            // Each refused line: its number and why, one log line each.
            internal readonly List<string> Refused = new List<string>();
        }

        private static readonly Regex Shape = new Regex(@"^(?<dashes>-*)(?<name>[^\[\(]*?)\s*(\[(?<kind>[^\]]*)\])?\s*(?<words>[^\(\[]*?)\s*(\((?<reach>[a-z]+)\s*:\s*(?<target>[^\)]*)\))?\s*(?<op>\+=|-=)?\s*$", RegexOptions.CultureInvariant);
        // A name or target is words, numbers and a few marks: anything like code is refused before it is looked up.
        private static readonly Regex Plain = new Regex(@"^[\p{L}\p{N} _.,'&/:+?!-]*$", RegexOptions.CultureInvariant);

        // Reads a tree file into its model. known says whether a reach's target is one of hers (null: every target is).
        internal static Model Read(string text, Func<string, string, bool> known)
        {
            Model model = new Model();
            List<Row> path = new List<Row>();
            string[] lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
            for (int at = 0; at < lines.Length; at++)
            {
                string line = lines[at].Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith("//", StringComparison.Ordinal)) continue;
                Match shape = Shape.Match(line);
                string why = null;
                Row row = null;
                if (!shape.Success) why = "not a tree line";
                else
                {
                    row = new Row { Line = at + 1, Depth = shape.Groups["dashes"].Value.Length, Name = shape.Groups["name"].Value.Trim(), Words = shape.Groups["words"].Value.Trim() };
                    // Words only follow a piece in brackets; with none, they are all the name.
                    if (!shape.Groups["kind"].Success) { row.Name = (row.Name + " " + row.Words).Trim(); row.Words = ""; }
                    row.Takes = shape.Groups["op"].Value == "-=";
                    why = Check(row, shape, known);
                }
                if (why == null && row.Depth == 0)
                {
                    if (model.Page != null) why = "a second page line";
                    else { model.Page = row.Name; path.Clear(); continue; }
                }
                if (why == null && model.Page == null) why = "a row before its page line";
                if (why == null && row.Depth > path.Count + 1) why = "a row deeper than the one above it";
                if (why != null) { model.Refused.Add("line " + (at + 1) + ": " + why); continue; }
                while (path.Count >= row.Depth) path.RemoveAt(path.Count - 1);
                if (row.Depth == 1) model.Panels.Add(row); else path[path.Count - 1].Under.Add(row);
                path.Add(row);
            }
            return model;
        }

        // Every row's reach resolved once (plan 8.1): building and painting never look a name up again. A command none of
        // hers is logged and its row left without one.
        internal static void Compile(Model model, TreeWiring wire)
        {
            if (model == null || wire == null) return;
            foreach (Row panel in model.Panels) CompileRow(panel, wire);
        }

        private static void CompileRow(Row row, TreeWiring wire)
        {
            string target = row.Target;
            if (row.Reach == "setting" && target != null)
            {
                if (wire.SwitchOf != null) { row.Get = delegate { return wire.SwitchOf(target); }; row.Set = delegate(bool on) { return wire.SetSwitch(target, on); }; }
                if (wire.StepsOf != null) { row.Steps = wire.StepsOf(target); row.Level = delegate { return wire.LevelOf(target); }; row.SetLevel = delegate(int at) { return wire.SetLevel(target, at); }; }
                if (wire.OwnOf != null) { row.Own = delegate { return wire.OwnOf(target); }; row.SetOwn = delegate(string value) { wire.SetOwn(target, value); }; }
            }
            if (row.Reach == "command" && target != null && wire.Command != null)
            {
                row.Run = wire.Command(target);
                if (row.Run == null && wire.Log != null) wire.Log("line " + row.Line + ": no command of hers called " + target);
            }
            if (row.Colour != null)
            {
                string tag = row.Colour;
                if (tag.StartsWith("own:", StringComparison.Ordinal)) { string name = tag.Substring(4); if (wire.OwnColour != null) row.Ink = delegate { return wire.OwnColour(name); }; }
                else
                {
                    System.Reflection.PropertyInfo slot = typeof(Neon).GetProperty(tag, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    if (slot != null) row.Ink = delegate { return (System.Drawing.Color)slot.GetValue(null, null); };
                }
            }
            foreach (Row under in row.Under) CompileRow(under, wire);
        }

        // Mods' trees laid over hers in load order: a row joins its panel by name, "-=" takes one away, a new panel is
        // added. Hers stays as it was, so laying again without a mod undoes it.
        internal static Model Lay(Model hers, IEnumerable<KeyValuePair<string, Model>> mods)
        {
            Model laid = new Model { Page = hers.Page };
            foreach (Row panel in hers.Panels) laid.Panels.Add(Copy(panel));
            foreach (KeyValuePair<string, Model> mod in mods)
            {
                if (mod.Value == null || !string.Equals(mod.Value.Page, hers.Page, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (Row panel in mod.Value.Panels)
                {
                    Row into = laid.Panels.Find(one => string.Equals(one.Name, panel.Name, StringComparison.OrdinalIgnoreCase));
                    if (into == null) { Row added = Copy(panel); Mark(added, mod.Key); laid.Panels.Add(added); continue; }
                    foreach (Row row in panel.Under)
                    {
                        if (row.Takes) { into.Under.RemoveAll(one => string.Equals(one.Name, row.Name, StringComparison.OrdinalIgnoreCase)); continue; }
                        Row added = Copy(row);
                        Mark(added, mod.Key);
                        into.Under.Add(added);
                    }
                }
            }
            return laid;
        }

        private static Row Copy(Row row)
        {
            Row made = new Row { Line = row.Line, Depth = row.Depth, Name = row.Name, Kind = row.Kind, Reach = row.Reach, Target = row.Target, Words = row.Words, Takes = row.Takes, From = row.From, Colour = row.Colour };
            made.Flags.AddRange(row.Flags);
            foreach (Row under in row.Under) made.Under.Add(Copy(under));
            return made;
        }

        private static void Mark(Row row, string mod) { row.From = mod; foreach (Row under in row.Under) Mark(under, mod); }

#if CHECKS
        // Compile and Lay: a switch reads and sets through her settings, a command is found once, one none of hers is
        // logged; a mod adds a row to her panel, takes one away and adds a panel, and hers is untouched. Null when right.
        internal static string LayCheck()
        {
            List<string> wrong = new List<string>(), logged = new List<string>();
            Dictionary<string, bool> switches = new Dictionary<string, bool> { { "hologram", false } };
            int looked = 0, ran = 0;
            TreeWiring wire = new TreeWiring
            {
                SwitchOf = delegate(string name) { bool on; return switches.TryGetValue(name, out on) ? (bool?)on : null; },
                SetSwitch = delegate(string name, bool on) { switches[name] = on; return null; },
                Command = delegate(string name) { looked++; return name == "thing" ? (Action)delegate { ran++; } : null; },
                Log = delegate(string line) { logged.Add(line); }
            };
            Model hers = Read("settings\n-capri\n--Hologram [switch] (setting: hologram)\n--Mute her [switch] (setting: mute)\n", null);
            Model mod = Read("settings\n-capri\n--Mute her -=\n--Thing [buttonSmall] (command: thing)\n--Nope [buttonSmall] (command: nope)\n-extras\n--Glow [switch] (setting: glow)\n", null);
            Model laid = Lay(hers, new[] { new KeyValuePair<string, Model>("tester", mod) });
            Compile(laid, wire);
            Row capri = laid.Panels.Count > 0 ? laid.Panels[0] : null;
            string names = capri == null ? "" : string.Join(",", capri.Under.ConvertAll(one => one.Name).ToArray());
            if (names != "Hologram,Thing,Nope" || laid.Panels.Count != 2 || laid.Panels[1].Name != "extras" || laid.Panels[1].From != "tester" || capri.Under[1].From != "tester" || capri.Under[0].From != null)
                wrong.Add("laid " + names + " and " + laid.Panels.Count + " panels");
            if (hers.Panels[0].Under.Count != 2) wrong.Add("hers changed under the lay");
            if (capri != null && capri.Under.Count == 3)
            {
                Row hologram = capri.Under[0];
                if (hologram.Get == null || hologram.Get() != false || hologram.Set(true) != null || switches["hologram"] != true || hologram.Get() != true) wrong.Add("the switch didn't read and set through her settings");
                if (capri.Under[1].Run == null) wrong.Add("the command wasn't found");
                else { capri.Under[1].Run(); capri.Under[1].Run(); }
                if (ran != 2 || looked != 2) wrong.Add("commands looked up " + looked + " times and ran " + ran + ", wanted 2 and 2 (each looked up once)");
                if (capri.Under[2].Run != null || logged.Count != 1) wrong.Add("a command none of hers: " + string.Join(" / ", logged.ToArray()));
            }
            // Colour tags: one of her palette's read as it is now, a mod's own through Custom colours, one none of hers refused.
            TreeWiring inked = new TreeWiring { OwnColour = delegate(string name) { return name == "Beam" ? System.Drawing.Color.Lime : System.Drawing.Color.Empty; } };
            Model tagged = Read(string.Join("\n", new[] { "settings", "-inks", "--Hot one [buttonSmall, colour: hot]", "--Beam one [buttonSmall, colour: own: Beam]", "--Odd one [buttonSmall, colour: purple]" }), null);
            Compile(tagged, inked);
            List<Row> inks = tagged.Panels.Count == 1 ? tagged.Panels[0].Under : new List<Row>();
            if (inks.Count != 2 || inks[0].Ink == null || inks[0].Ink() != Neon.Hot || inks[1].Ink == null || inks[1].Ink() != System.Drawing.Color.Lime || tagged.Refused.Count != 1)
                wrong.Add("colour tags: " + inks.Count + " rows, refused " + tagged.Refused.Count);
            return wrong.Count == 0 ? null : string.Join("; ", wrong.ToArray());
        }

        // The Brain panel as a tree: its rows, pieces, marks, reaches and levels read as written; code, an unknown piece,
        // an unknown reach and a target none of hers are each refused on their own line. Null when right.
        internal static string ReadCheck()
        {
            string tree = "setup\n-brain\n--Provider [buttonSmall] (window: provider)\n--Refresh providers [buttonLarge, flashing] (command: refresh providers)\n"
                + "--tier [rungs] (setting: brain tier)\n---OFF\n---MEDIUM INTELLIGENCE\n----Free key [dropdown] (list: free keys)\n"
                + "-fallback\n--Brains in use [lightPair] No cloud / Local (state: brains in use)\n--snack -=\n"
                + "--Bad [buttonSmall] (command: run(\"x\"))\n--Odd [hologram]\n--Far [switch] (web: x)\n--Gone [switch] (setting: nobody)\n";
            Model model = Read(tree, delegate(string reach, string target) { return target != "nobody"; });
            List<string> wrong = new List<string>();
            if (model.Page != "setup" || model.Panels.Count != 2) wrong.Add("page " + model.Page + ", panels " + model.Panels.Count);
            else
            {
                Row brain = model.Panels[0], fallback = model.Panels[1];
                if (brain.Under.Count != 3 || brain.Under[1].Kind != "buttonLarge" || !brain.Under[1].Flags.Contains("flashing") || brain.Under[1].Reach != "command" || brain.Under[1].Target != "refresh providers")
                    wrong.Add("brain's rows");
                else if (brain.Under[2].Under.Count != 2 || brain.Under[2].Under[1].Under.Count != 1 || brain.Under[2].Under[1].Under[0].Kind != "dropdown")
                    wrong.Add("the rungs and the dropdown under one");
                if (fallback.Under.Count != 2 || fallback.Under[0].Words != "No cloud / Local" || fallback.Under[0].Name != "Brains in use" || !fallback.Under[1].Takes || fallback.Under[1].Name != "snack")
                    wrong.Add("fallback's rows");
            }
            if (model.Refused.Count != 4) wrong.Add("refused " + model.Refused.Count + ": " + string.Join(" / ", model.Refused.ToArray()));
            return wrong.Count == 0 ? null : string.Join("; ", wrong.ToArray());
        }
#endif

        private static string Check(Row row, Match shape, Func<string, string, bool> known)
        {
            if (row.Name.Length == 0) return "no name";
            if (!Plain.IsMatch(row.Name) || !Plain.IsMatch(row.Words)) return "words that read like code";
            if (shape.Groups["kind"].Success)
            {
                string[] parts = shape.Groups["kind"].Value.Split(',');
                for (int i = 0; i < parts.Length; i++)
                {
                    string part = parts[i].Trim();
                    if (i == 0)
                    {
                        string kind = Array.Find(Kinds, one => string.Equals(one, part, StringComparison.OrdinalIgnoreCase));
                        if (kind == null) return "no piece of hers called [" + part + "]";
                        row.Kind = kind;
                    }
                    else if (Array.Exists(Marks, one => string.Equals(one, part, StringComparison.OrdinalIgnoreCase))) row.Flags.Add(part.ToLowerInvariant());
                    else if (part.StartsWith("colour:", StringComparison.OrdinalIgnoreCase) || part.StartsWith("color:", StringComparison.OrdinalIgnoreCase))
                    {
                        string tag = part.Substring(part.IndexOf(':') + 1).Trim();
                        bool own = tag.StartsWith("own:", StringComparison.OrdinalIgnoreCase) && tag.Substring(4).Trim().Length > 0;
                        if (!own && !Array.Exists(Palette, one => string.Equals(one, tag, StringComparison.OrdinalIgnoreCase))) return "no colour of hers called " + tag;
                        row.Colour = own ? "own:" + tag.Substring(4).Trim() : Array.Find(Palette, one => string.Equals(one, tag, StringComparison.OrdinalIgnoreCase));
                    }
                    else return "no mark called " + part;
                }
            }
            if (shape.Groups["reach"].Success)
            {
                string reach = shape.Groups["reach"].Value, target = shape.Groups["target"].Value.Trim();
                if (Array.IndexOf(Reaches, reach) < 0) return "no reach called (" + reach + ":)";
                if (target.Length == 0 || !Plain.IsMatch(target)) return "a target that reads like code";
                if (known != null && !known(reach, target)) return "(" + reach + ": " + target + ") is none of hers";
                row.Reach = reach;
                row.Target = target;
            }
            return null;
        }
    }
}
