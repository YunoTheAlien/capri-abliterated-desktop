using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Stage 1's other pieces for a mod's panel, each made of her own controls: a dropdown, a light pair, rungs and a fold.
    // A value is her setting when the row reaches one of hers, else the mod's own in its Toggles store.
    internal sealed partial class Mainframe
    {
        // A row's choices: her setting's steps, or the rows under it by name.
        private static string[] TreeChoices(PanelTree.Row row)
        {
            if (row.Steps != null && row.Steps.Length > 0) return row.Steps;
            List<string> names = new List<string>();
            foreach (PanelTree.Row under in row.Under) names.Add(under.Name);
            return names.ToArray();
        }

        private static string TreeOwnKey(PanelTree.Row row) { return row.Target ?? row.Name; }

        private static int TreeChosen(PanelTree.Row row, TreeWiring wire, string[] choices)
        {
            if (row.Level != null && row.Steps != null) return Math.Max(0, Math.Min(choices.Length - 1, row.Level()));
            int at = Array.IndexOf(choices, wire.OwnOf(TreeOwnKey(row)));
            return Math.Max(0, at);
        }

        private static void TreeChoose(PanelTree.Row row, TreeWiring wire, string[] choices, int at)
        {
            if (row.SetLevel != null && row.Steps != null) row.SetLevel(at);
            else wire.SetOwn(TreeOwnKey(row), choices[at]);
        }

        // A name on the left and a key on the right that opens her dropdown of its choices, the chosen one ticked.
        private Control TreeDropdown(PanelTree.Row row, TreeWiring wire)
        {
            string[] choices = TreeChoices(row);
            if (choices.Length == 0) { wire.Log("line " + row.Line + ": a dropdown with nothing to choose"); return null; }
            TreeBox line = new TreeBox { Size = new Size(328, 22) };
            line.Controls.Add(new Note { Text = row.Name, Font = Font, ForeColor = Rose, AutoSize = false, Bounds = new Rectangle(0, 2, ValueAt - 28 - 6, 20) });
            int chosen = TreeChosen(row, wire, choices);
            Key pick = new Key { Text = choices[chosen], Font = strong, Bounds = new Rectangle(ValueAt - 28, 0, ValueWide, 22), Chevron = true };
            pick.AccessibleName = row.Name;
            ContextMenuStrip menu = Dropdown();
            menu.AccessibleName = row.Name + " choices";
            for (int i = 0; i < choices.Length; i++)
            {
                int at = i;
                menu.Items.Add(Choice(choices[i], i == chosen, delegate
                {
                    TreeChoose(row, wire, choices, at);
                    pick.Text = choices[at];
                    for (int j = 0; j < menu.Items.Count; j++) { ToolStripMenuItem item = menu.Items[j] as ToolStripMenuItem; if (item != null) item.Checked = j == at; }
                }));
            }
            pick.Click += delegate { menu.Show(pick, new Point(0, pick.Height)); };
            pick.Tag = menu;
            line.Controls.Add(pick);
            return line;
        }

        // Two halves, "No cloud / Local", the one that is on lit: off is the first half, on the second.
        private Control TreeLightPair(PanelTree.Row row, TreeWiring wire)
        {
            string[] halves = (row.Words ?? "").Split('/');
            if (halves.Length != 2 || halves[0].Trim().Length == 0 || halves[1].Trim().Length == 0) { wire.Log("line " + row.Line + ": a light pair needs two halves, like No cloud / Local"); return null; }
            TreeBox line = new TreeBox { Size = new Size(328, 22) };
            Key[] keys = new Key[2];
            Func<bool> on = delegate { bool? hers = row.Get != null ? row.Get() : null; return hers ?? wire.OwnOf(TreeOwnKey(row)) == "on"; };
            Action light = delegate { bool now = on(); keys[0].Accent = now ? Rose : Cyan; keys[1].Accent = now ? Cyan : Rose; keys[0].Invalidate(); keys[1].Invalidate(); };
            for (int i = 0; i < 2; i++)
            {
                bool half = i == 1;
                keys[i] = new Key { Text = halves[i].Trim(), Font = strong, Bounds = new Rectangle(i * 168, 0, 160, 22) };
                keys[i].AccessibleName = row.Name + ": " + halves[i].Trim();
                keys[i].Click += delegate
                {
                    if (row.Get != null && row.Get() != null) row.Set(half); else wire.SetOwn(TreeOwnKey(row), half ? "on" : "off");
                    light();
                };
                line.Controls.Add(keys[i]);
            }
            light();
            return line;
        }

        // Her slider over the rungs by name; the rows under the chosen rung show beneath it.
        private Control TreeRungs(PanelTree.Row row, TreeWiring wire)
        {
            string[] choices = TreeChoices(row);
            if (choices.Length < 2) { wire.Log("line " + row.Line + ": rungs need two or more"); return null; }
            TreeBox box = new TreeBox { Size = new Size(328, 22) };
            TreeBox top = new TreeBox { Size = new Size(328, 22) };
            top.Controls.Add(new Note { Text = row.Name, Font = Font, ForeColor = Rose, AutoSize = false, Bounds = new Rectangle(0, 2, ValueAt - 28 - 6, 20) });
            Slider slider = new Slider(choices) { Font = Type(9.5f), Bounds = new Rectangle(ValueAt - 28, 0, ValueWide, 22) };
            slider.AccessibleName = row.Name;
            slider.Value = TreeChosen(row, wire, choices);
            top.Controls.Add(slider);
            box.Controls.Add(top);
            List<TreeBox> rungs = new List<TreeBox>();
            for (int i = 0; i < row.Under.Count && i < choices.Length; i++)
            {
                TreeBox rung = new TreeBox { Size = new Size(328, 0) };
                foreach (PanelTree.Row under in row.Under[i].Under) { Control made = TreeRow(under, wire); if (made != null) rung.Controls.Add(made); }
                TreeStack(rung, false);
                rungs.Add(rung);
                box.Controls.Add(rung);
            }
            Action<bool> show = delegate(bool scaled)
            {
                for (int i = 0; i < rungs.Count; i++) rungs[i].Visible = i == slider.Value && rungs[i].Controls.Count > 0;
                TreeStack(box, scaled);
            };
            slider.Changed += delegate { TreeChoose(row, wire, choices, slider.Value); show(true); TreeResized(box); };
            show(false);
            return box;
        }

        // Toggles' fold heading over its rows; open or closed is kept in the mod's own store.
        private Control TreeFold(PanelTree.Row row, TreeWiring wire)
        {
            TreeBox box = new TreeBox { Size = new Size(328, 27) };
            string kept = "fold: " + row.Name;
            FoldHead head = new FoldHead { Text = row.Name.ToUpperInvariant(), Font = Type(10f, true), Bounds = new Rectangle(0, 0, 328, 27), Open = wire.OwnOf(kept) == "open" };
            head.AccessibleName = row.Name;
            box.Controls.Add(head);
            TreeBox inside = new TreeBox { Size = new Size(328, 0) };
            foreach (PanelTree.Row under in row.Under) { Control made = TreeRow(under, wire); if (made != null) inside.Controls.Add(made); }
            TreeStack(inside, false);
            box.Controls.Add(inside);
            Action<bool> show = delegate(bool scaled) { inside.Visible = head.Open && inside.Controls.Count > 0; TreeStack(box, scaled); };
            head.Click += delegate { head.Open = !head.Open; wire.SetOwn(kept, head.Open ? "open" : "closed"); head.Invalidate(); show(true); TreeResized(box); };
            show(false);
            return box;
        }

#if CHECKS
        // The four pieces from one tree: the dropdown's choice, the light pair's half, the rung's own rows and the fold's
        // open, each kept in the mod's store and shown as it should. Null when right.
        internal string TreePiecesCheck()
        {
            Dictionary<string, string> own = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            List<string> logged = new List<string>(), wrong = new List<string>();
            TreeWiring wire = new TreeWiring
            {
                SwitchOf = delegate { return null; }, SetSwitch = delegate { return null; }, StepsOf = delegate { return null; },
                LevelOf = delegate { return 0; }, SetLevel = delegate { return null; },
                OwnOf = delegate(string name) { string value; return own.TryGetValue(name, out value) ? value : null; },
                SetOwn = delegate(string name, string value) { own[name] = value; },
                Command = delegate { return null; }, Log = delegate(string line) { logged.Add(line); }
            };
            PanelTree.Model model = PanelTree.Read(string.Join("\n", new[] { "settings", "-PIECES", "--Mode [dropdown]", "---Calm", "---Wild", "--Cloud [lightPair] No cloud / Local", "--Tier [rungs]", "---Low", "---High", "----Boost [switch]", "--More [fold]", "---Glow [switch]" }), null);
            PanelTree.Compile(model, wire);
            Control panel = TreePanel(model.Panels[0], wire);
            List<Control> all = new List<Control>();
            Action<Control> walk = null;
            walk = delegate(Control at) { foreach (Control one in at.Controls) { all.Add(one); walk(one); } };
            walk(panel);
            Key pick = all.Find(one => one is Key && ((Key)one).Chevron) as Key;
            ContextMenuStrip menu = pick == null ? null : pick.Tag as ContextMenuStrip;
            if (menu == null || menu.Items.Count != 2) wrong.Add("the dropdown has no list of two");
            else { ((ToolStripMenuItem)menu.Items[1]).PerformClick(); if (own["Mode"] != "Wild" || pick.Text != "Wild") wrong.Add("the dropdown's choice: " + pick.Text); }
            Key local = all.Find(one => one is Key && one.AccessibleName == "Cloud: Local") as Key;
            if (local == null) wrong.Add("no light pair half Local");
            else { local.PerformClick(); if (own["Cloud"] != "on" || local.Accent != Cyan) wrong.Add("the light pair didn't light Local"); }
            Slider tier = all.Find(one => one is Slider && one.AccessibleName == "Tier") as Slider;
            Control boost = all.Find(one => one is Toggle && one.Text == "Boost");
            if (tier == null || boost == null) wrong.Add("no rungs or no Boost under High");
            else
            {
                bool lowHides = !Meant(boost.Parent);
                tier.Choose(1);
                if (!lowHides || !Meant(boost.Parent) || own["Tier"] != "High") wrong.Add("High's rows: hidden on Low " + lowHides + ", shown on High " + Meant(boost.Parent));
            }
            FoldHead fold = all.Find(one => one is FoldHead) as FoldHead;
            Control glow = all.Find(one => one is Toggle && one.Text == "Glow");
            if (fold == null || glow == null) wrong.Add("no fold or no Glow in it");
            else
            {
                bool closed = !Meant(glow.Parent);
                int tall = fold.Parent.Height;
                typeof(Control).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(fold, new object[] { EventArgs.Empty });
                if (!closed || !Meant(glow.Parent) || own["fold: More"] != "open" || fold.Parent.Height <= tall) wrong.Add("the fold: closed first " + closed + ", open after " + Meant(glow.Parent) + ", grew " + (fold.Parent.Height > tall));
            }
            if (logged.Count != 0) wrong.Add("logged " + string.Join(" / ", logged.ToArray()));
            panel.Dispose();
            return wrong.Count == 0 ? null : string.Join("; ", wrong.ToArray());
        }
#endif

        // A row's colour tag on what it built: a key's accent, a name's ink; a box's keys and names take it too.
        private static void TreeInk(Control made, PanelTree.Row row)
        {
            if (row.Ink == null) return;
            Color ink = row.Ink();
            Key key = made as Key;
            if (key != null) { key.Accent = ink; key.Invalidate(); return; }
            if (made is Label) { made.ForeColor = ink; return; }
            foreach (Control one in made.Controls) TreeInk(one, row);
        }

        // A box's shown controls stacked down it, 6 apart (scaled once the page is laid), and its height to fit them.
        private void TreeStack(TreeBox box, bool scaled)
        {
            int gap = scaled ? Scaled(6) : 6, y = 0;
            foreach (Control one in box.Controls)
            {
                if (!Meant(one)) continue;
                one.Top = y;
                y += one.Height + gap;
            }
            box.Height = Math.Max(0, y - gap);
        }

        // Whether a control is set to show, whatever its parents are: Visible reads false under a window not shown yet.
        private static readonly System.Reflection.MethodInfo StateOf = typeof(Control).GetMethod("GetState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, new[] { typeof(int) }, null);

        internal static bool Meant(Control one) { try { return StateOf == null ? one.Visible : (bool)StateOf.Invoke(one, new object[] { 2 }); } catch (Exception) { return one.Visible; } }

        // A piece that grew or shrank: the boxes up to its panel stacked again, the panel's height taken from them and the
        // page placed again.
        private void TreeResized(Control piece)
        {
            Control at = piece.Parent;
            while (at is TreeBox)
            {
                TreeStack((TreeBox)at, true);
                Panel panel;
                if (panelOf.TryGetValue(at, out panel))
                {
                    panel.Span = 26 + (int)Math.Round(at.Height / Math.Max(0.1f, Unit())) + panelGap;
                    Restack();
                    return;
                }
                at = at.Parent;
            }
        }
    }
}
