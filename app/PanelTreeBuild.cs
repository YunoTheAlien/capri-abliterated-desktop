using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace CapriDesktop
{
    // What a tree's reaches lead to, resolved once when it is read: her switches and sliders through her settings tool,
    // a mod's own values in its Toggles store, and her commands from one named table.
    internal sealed class TreeWiring
    {
        internal Func<string, bool?> SwitchOf;
        internal Func<string, bool, string> SetSwitch;
        internal Func<string, string[]> StepsOf;
        internal Func<string, int> LevelOf;
        internal Func<string, int, string> SetLevel;
        internal Func<string, string> OwnOf;
        internal Action<string, string> SetOwn;
        internal Func<string, Action> Command;
        // A mod's own colour by name, as Custom colours keeps it.
        internal Func<string, Color> OwnColour;
        internal Action<string> Log;
    }

    // How a tree panel's rows are laid: stacked down a mod's panel, or on her own Settings grid exactly as she lays hers.
    internal enum TreeLayout { Stacked, HerGrid }

    internal sealed partial class Mainframe
    {
        // A tree panel's rows, stacked down the column's width, each one of her own pieces; built when the panel is made.
        private sealed class TreeBox : System.Windows.Forms.Panel
        {
            internal TreeBox() { SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true); }
            protected override void OnPaintBackground(PaintEventArgs e) { Backdrop(e.Graphics, this, ScaleOf(this)); }
        }

        // Pieces stage 1 builds; any other piece in a tree is logged and its row left out.
        internal static readonly string[] TreeBuilt = { "switch", "buttonSmall", "buttonLarge", "buttonRow", "slider", "sliderRow", "dropdown", "lightPair", "rungs", "fold" };

#if CHECKS
        // A tree mod's panel as built, its rows' names "|" joined in order; null when it has none on Settings.
        internal string TreePanelShown(string mod)
        {
            foreach (List<Panel> list in columns)
                foreach (Panel panel in list)
                {
                    if (panel.Mod != mod) continue;
                    List<string> names = new List<string>();
                    foreach (Control part in panel.Offsets.Keys)
                        if (part is TreeBox) TreeNames(part, names);
                    return string.Join("|", names.ToArray());
                }
            return null;
        }

        private static void TreeNames(Control box, List<string> names)
        {
            foreach (Control part in box.Controls)
            {
                if (part is TreeBox) TreeNames(part, names);
                else if (part is Toggle || part is Key || part is Note) names.Add(part.Text);
            }
        }
#endif

        // Her Refresh providers key's own press, for a tree's command.
        internal void TreeRefresh() { RefreshProviders(); }

        internal Control TreePanel(PanelTree.Row panel, TreeWiring wire, TreeLayout layout = TreeLayout.Stacked, Dictionary<string, Control> built = null, Point at = default(Point))
        {
            if (layout == TreeLayout.HerGrid) return TreeHerGrid(panel, wire, built ?? new Dictionary<string, Control>(StringComparer.OrdinalIgnoreCase), at.X, at.Y);
            TreeBox box = new TreeBox { Size = new Size(328, 22) };
            int y = 0;
            foreach (PanelTree.Row row in panel.Under)
            {
                Control made = TreeRow(row, wire);
                if (made == null) continue;
                TreeInk(made, row);
                made.Location = new Point(made.Left, y);
                box.Controls.Add(made);
                y += made.Height + 6;
            }
            box.Height = Math.Max(22, y - 6);
            return box;
        }

        private Control TreeRow(PanelTree.Row row, TreeWiring wire)
        {
            string kind = row.Kind ?? "switch";
            if (kind == "dropdown") return TreeDropdown(row, wire);
            if (kind == "lightPair") return TreeLightPair(row, wire);
            if (kind == "rungs") return TreeRungs(row, wire);
            if (kind == "fold") return TreeFold(row, wire);
            if (Array.IndexOf(TreeBuilt, kind) < 0) { wire.Log("line " + row.Line + ": [" + kind + "] is not built in stage 1"); return null; }
            string target = row.Target ?? row.Name;
            if (kind == "switch")
            {
                Toggle toggle = new Toggle { Text = row.Name, Font = Font, Size = new Size(328, 22) };
                toggle.AccessibleName = row.Name;
                bool? on = row.Reach == "setting" && wire.SwitchOf(target) != null ? wire.SwitchOf(target) : (bool?)(wire.OwnOf(target) == "on");
                toggle.Checked = on == true;
                toggle.CheckedChanged += delegate
                {
                    if (row.Reach == "setting" && wire.SwitchOf(target) != null) wire.SetSwitch(target, toggle.Checked);
                    else wire.SetOwn(target, toggle.Checked ? "on" : "off");
                };
                return toggle;
            }
            if (kind == "slider" || kind == "sliderRow")
            {
                string[] steps = wire.StepsOf(target);
                if (steps == null) { wire.Log("line " + row.Line + ": " + target + " has no steps"); return null; }
                TreeBox line = new TreeBox { Size = new Size(328, 22) };
                Label name = new Note { Text = row.Name, Font = Font, ForeColor = Rose, AutoSize = false, Bounds = new Rectangle(0, 2, ValueAt - 28 - 6, 20) };
                Slider slider = new Slider(steps) { Font = Type(9.5f), Bounds = new Rectangle(ValueAt - 28, 0, ValueWide, 22) };
                slider.AccessibleName = row.Name;
                slider.Value = Math.Max(0, wire.LevelOf(target));
                slider.Released += delegate { wire.SetLevel(target, slider.Value); };
                line.Controls.Add(name);
                line.Controls.Add(slider);
                return line;
            }
            if (kind == "buttonRow")
            {
                TreeBox line = new TreeBox { Size = new Size(328, 22) };
                int count = Math.Max(1, row.Under.Count), wide = (328 - 8 * (count - 1)) / count;
                for (int i = 0; i < row.Under.Count; i++)
                {
                    Key key = TreeKey(row.Under[i], wire, wide);
                    if (key == null) continue;
                    key.Left = i * (wide + 8);
                    line.Controls.Add(key);
                }
                return line;
            }
            return TreeKey(row, wire, kind == "buttonSmall" ? ValueWide : 328, kind == "buttonSmall" ? ValueAt - 28 : 0);
        }

        private Key TreeKey(PanelTree.Row row, TreeWiring wire, int wide, int left = 0)
        {
            Action run = row.Reach == "command" ? wire.Command(row.Target) : null;
            if (run == null) { wire.Log("line " + row.Line + ": a key with no command of hers"); return null; }
            Key key = new Key { Text = row.Name, Font = strong, Bounds = new Rectangle(left, 0, wide, 22) };
            key.AccessibleName = row.Name;
            key.Click += delegate { run(); };
            return key;
        }
    }

    internal sealed partial class Companion
    {
        // The commands a tree may run, by name, each taking no input. Nothing that deletes or clears, uninstalls or updates,
        // sends anything online or opens a file or folder picker is ever here; her own confirm boxes stay hers.
        private Dictionary<string, Action> TreeCommands()
        {
            Dictionary<string, Action> table = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase);
            table["preview voice"] = delegate { Hands.Do("capri_settings", new ToolArguments { Action = "preview_voice" }); };
            table["refresh providers"] = delegate { if (mainframe != null) mainframe.TreeRefresh(); };
            return table;
        }

        // A tree's wiring for one mod: her settings by her settings tool's names, the rest its own values.
        internal TreeWiring TreeWiringFor(string modId)
        {
            Dictionary<string, Action> commands = TreeCommands();
            List<string> hers = Hands.SettingNames();
            Func<string, string> key = delegate(string name) { return (name ?? "").Trim().ToLowerInvariant().Replace(' ', '_'); };
            return new TreeWiring
            {
                SwitchOf = delegate(string name) { string k = key(name); return hers.Contains(k) && Hands.StepsOf(k) == null ? SettingOn(k) : null; },
                SetSwitch = delegate(string name, bool on) { return SetSetting(key(name), on); },
                StepsOf = delegate(string name) { return Hands.StepsOf(key(name)); },
                LevelOf = delegate(string name) { return LevelOf(key(name)); },
                SetLevel = delegate(string name, int step) { return SetLevel(key(name), step); },
                OwnOf = delegate(string name) { return ModToggle(modId, null, name); },
                OwnColour = delegate(string name) { return AddonColours.Declare(modId, name, Neon.Rose); },
                SetOwn = delegate(string name, string value) { SetModToggle(modId, null, name, value); },
                Command = delegate(string name) { Action run; return name != null && commands.TryGetValue(name.Trim(), out run) ? run : null; },
                Log = delegate(string line) { Brain.Log("mods", -1, modId + " (tree): " + line); }
            };
        }

        // What a tree's reach may name: her commands from TreeCommands, and any setting (hers by name, else the mod's own).
        // Windows, lists and states are later stages.
        internal bool TreeKnows(string reach, string target)
        {
            if (reach == "setting") return true;
            if (reach == "command")
            {
                bool known = TreeCommands().ContainsKey((target ?? "").Trim());
                if (!known) Brain.Log("mods", -1, "tree: (command: " + target + ") is not on TreeCommands");
                return known;
            }
            return false;
        }

#if CHECKS
        // A tree mod's panel built from her pieces; unknown pieces, commands and code left out with a log line; the mod
        // out gives its slot back. Null when right.
        internal string TreePanelCheck()
        {
            const string id = "tree-check";
            string tree = string.Join("\n", new[]
            {
                "settings", "-TREE CHECK", "--Wander [switch] (setting: wander)", "--Glow [switch] (setting: glow)", "--Size [slider] (setting: size)",
                "--Hear it [buttonLarge] (command: preview voice)", "--Keys [buttonRow]", "---One [buttonSmall] (command: preview voice)",
                "---Two [buttonSmall] (command: refresh providers)", "--Pick [dropdown] (list: things)", "--Wipe [buttonLarge] (command: clear memory)",
                "--Bad [switch] (setting: run(1))"
            });
            List<string> wrong = new List<string>();
            string laid = LayTreeText(id, tree);
            if (laid != null) return laid;
            Application.DoEvents();
            string built = mainframe.TreePanelShown(id);
            if (built == null) wrong.Add("no panel on Settings");
            else if (built != "Wander|Glow|Size|Hear it|One|Two") wrong.Add("rows " + built);
            mainframe.RemoveModPanels(id);
            Application.DoEvents();
            if (mainframe.TreePanelShown(id) != null) wrong.Add("the panel stayed after the mod went");
            return wrong.Count == 0 ? null : string.Join("; ", wrong.ToArray());
        }
#endif

#if CHECKS
        // The two templates in mods	emplates, found up the folders from this build: the tree's header reads as a mod's,
        // its panel builds with every row, and the code template building the same panel compiles. Null when right;
        // "" when this build has no source folder beside it.
        internal string TreeTemplatesCheck()
        {
            string folder = AppDomain.CurrentDomain.BaseDirectory, templates = null;
            for (int up = 0; up < 6 && !string.IsNullOrEmpty(folder); up++, folder = Path.GetDirectoryName(folder.TrimEnd(Path.DirectorySeparatorChar)))
                if (File.Exists(Path.Combine(folder, "mods", "templates", "settings-panel" + Mods.TreeExtension))) { templates = Path.Combine(folder, "mods", "templates"); break; }
            if (templates == null) return "";
            List<string> wrong = new List<string>();
            string text = File.ReadAllText(Path.Combine(templates, "settings-panel" + Mods.TreeExtension));
            ModFile read = Mods.ReadTree(text);
            if (read.Id != "settings-panel" || read.Name != "Settings panel") wrong.Add("its header read " + read.Id + " / " + read.Name);
            string laid = LayTreeText(read.Id, Mods.TreeBody(text));
            if (laid != null) wrong.Add(laid);
            Application.DoEvents();
            string rows = mainframe.TreePanelShown(read.Id);
            if (rows != "Wander|My own switch|Size|Hear her voice|Hear her|Refresh providers") wrong.Add("its rows " + rows);
            mainframe.RemoveModPanels(read.Id);
            string compiled = Mods.CompilesForChecks(File.ReadAllBytes(Path.Combine(templates, "settings-panel-code" + Mods.CodeExtension)));
            if (compiled != null) wrong.Add("the code template: " + compiled);
            return wrong.Count == 0 ? null : string.Join("; ", wrong.ToArray());
        }
#endif

        // A .capri-tree file: "// capri-tree: 1" and its id, name, author and version, then the tree. Each panel goes on her
        // Settings page through the same path a code mod's panel takes. Null when laid; else why not.
        internal string LayTree(string file)
        {
            string text;
            try { text = File.ReadAllText(file); }
            catch (IOException error) { return error.Message; }
            catch (UnauthorizedAccessException error) { return error.Message; }
            Dictionary<string, string> head = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            List<string> body = new List<string>();
            foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("//", StringComparison.Ordinal))
                {
                    int mark = line.IndexOf(':');
                    if (mark > 2) head[line.Substring(2, mark - 2).Trim()] = line.Substring(mark + 1).Trim();
                    continue;
                }
                body.Add(raw);
            }
            string version, id;
            if (!head.TryGetValue("capri-tree", out version) || version != "1") return "not a capri-tree 1 file";
            if (!head.TryGetValue("id", out id) || id.Length == 0) return "no id";
            return LayTreeText(id, string.Join("\n", body.ToArray()));
        }

        internal string LayTreeText(string id, string tree)
        {
            TreeWiring wire = TreeWiringFor(id);
            PanelTree.Model model = PanelTree.Read(tree, TreeKnows);
            foreach (string refused in model.Refused) wire.Log(refused);
            PanelTree.Compile(model, wire);
            if (string.Equals(model.Page, "layout", StringComparison.OrdinalIgnoreCase)) return LayLayout(id, model);
            if (model.Page != "settings") return "only Settings panels in stage 1, not " + model.Page;
            if (mainframe == null) return "no Mainframe yet";
            int slot = 1;
            foreach (PanelTree.Row panel in model.Panels)
            {
                PanelTree.Row one = panel;
                int at = slot;
                OnWindow(delegate { mainframe.AddModPanel(id, one.Name, 1, at, mainframe.TreePanel(one, wire)); });
                slot++;
            }
            return null;
        }
    }
}
