using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace CapriDesktop
{
    // TOOLS, the Mainframe's front page: one section at a time from its dropdown, its tools as square tiles three to a
    // row like a phone's home screen, in the one order a tile dragged onto another sets; CAPRI under them.
    internal sealed partial class Mainframe
    {
        // Her own tools, each a fold in Toggles: its id and full name. The pet game is a mod, switched on Mods.
        internal static readonly string[,] OwnTools =
        {
            { "planner", "Game / project planner" }, { "circuit", "Circuit designer" }, { "coding", "Coding teacher" }, { "music", "Music" },
            { "chess", "Chess" }, { "connect_four", "Connect four" }, { "tic_tac_toe", "Tic-tac-toe" }, { "rock_paper_scissors", "Rock, paper, scissors" },
            { "toggles", "Toggles" }, { "colours", "Custom colours" }, { "personality", "Personality settings" }, { "security", "Security" }, { "md_editor", "MD editor" },
            { "memory", "Her memory" }, { "growth", "Growth" }, { "modding", "Modding" }, { "streaming", "Streaming" }, { "workspace", "Workspace" }
        };
        // Her windows as tiles, from OwnTools' "toggles" on: the tile's name, its section and its icon.
        private const int FirstWindow = 8;
        private static readonly string[,] WindowTiles =
        {
            { "Toggles", "capri", "toggles" }, { "Colours", "capri", "colours" }, { "Personality", "capri", "brain" }, { "Security", "capri", "shield" },
            { "MD editor", "capri", "md" }, { "Memory", "capri", "memory" }, { "Growth", "capri", "radar" }, { "Modding", "utility", "modding" }, { "Streaming", "utility", "streaming" },
            { "Workspace", "productivity", "workspace" }
        };
        private const int ToolsTop = 58, TileTop = ToolsTop + 72, Tile = 98, TileGap = 17, TileColumns = 3, MostTileRows = 6, PagerRoom = 40;
        private readonly List<KeyValuePair<Control, int>> toolLeft = new List<KeyValuePair<Control, int>>();
        private readonly Dictionary<Control, int> toolTop = new Dictionary<Control, int>();
        private Key toolSectionKey, toolBack, toolNext;
        private ContextMenuStrip toolSectionMenu;
        private Label toolPage;
        private readonly ToolTile[] toolTiles = new ToolTile[TileColumns * MostTileRows];
        private int toolFirst;

        // A tool on TOOLS: one of hers or a plug-in. Soon ones show greyed and open nothing yet.
        internal sealed class Tool
        {
            internal string Id, Short, Full, Section, Icon;
            internal bool Soon;
            internal Mod Plugin;
            internal Func<bool> On;
            internal Action Open;
        }

        // Every tool in the one order dragging sets: the order saved, then any new one last.
        internal List<Tool> Tools()
        {
            List<Tool> all = new List<Tool>();
            all.Add(new Tool { Id = "planner", Short = "Planner", Full = OwnTools[0, 1], Section = "productivity", Icon = "planner", On = delegate { return capri.ToolOn("planner"); }, Open = delegate { OpenPlanner(); } });
            all.Add(new Tool { Id = "circuit", Short = "Circuit designer", Full = OwnTools[1, 1], Section = "learning", Icon = "chip", Soon = true, On = delegate { return capri.ToolOn("circuit"); } });
            all.Add(new Tool { Id = "coding", Short = "Coding teacher", Full = OwnTools[2, 1], Section = "learning", Icon = "code", Soon = true, On = delegate { return capri.ToolOn("coding"); } });
            // Fun: the pet game's window while its mod runs, then what her Play menu opens.
            all.Add(new Tool
            {
                Id = "pet", Short = "Tamagotchi", Full = "Tamagotchi", Section = "fun", Icon = "pet",
                On = delegate { return capri.ModList != null && capri.ModList.PetId != null; }, Open = delegate { if (capri.ModList.PetId != null) ShowPetMode(true); }
            });
            all.Add(new Tool { Id = "music", Short = "Music", Full = OwnTools[3, 1], Section = "fun", Icon = "music", On = delegate { return capri.ToolOn("music"); }, Open = delegate { capri.PlayMyMusic(); } });
            string[] boards = { "chess", "connect_four", "tic_tac_toe", "rock_paper_scissors" };
            for (int i = 0; i < boards.Length; i++)
            {
                string board = boards[i];
                all.Add(new Tool { Id = board, Short = OwnTools[4 + i, 1], Full = OwnTools[4 + i, 1], Section = "fun", Icon = board, On = delegate { return capri.ToolOn(board); }, Open = delegate { capri.PlayMini(board); } });
            }
            // Her windows: Memory opens on Her memory and Modding on Make a mod, the first part of each.
            Action[] opens =
            {
                delegate { OpenToggles(); }, delegate { OpenColours(); }, delegate { OpenPersonalitySettings(); }, delegate { OpenSecurity(); },
                delegate { EditNotes(false); }, delegate { OpenMemory(); }, delegate { OpenGrowth(); }, delegate { MakeMod(); }, delegate { capri.OpenStreamingWindow(); },
                delegate { capri.OpenWorkspace(); }
            };
            for (int i = 0; i < opens.Length; i++)
            {
                string id = OwnTools[FirstWindow + i, 0];
                all.Add(new Tool { Id = id, Short = WindowTiles[i, 0], Full = OwnTools[FirstWindow + i, 1], Section = WindowTiles[i, 1], Icon = WindowTiles[i, 2], On = delegate { return capri.ToolOn(id); }, Open = opens[i] });
            }
            // Better conversations' plug-in is a setting of hers, switched in Toggles, not a tool; a plug-in naming no
            // section of hers shows in none, Email in Productivity.
            foreach (Mod mod in capri.ModList.All)
            {
                if (mod.Plugin == null || mod.Plugin.Id == Keyless.PluginId) continue;
                Mod each = mod;
                bool mail = Array.Exists(mod.Plugin.Servers, server => server.BuiltIn == "mail");
                all.Add(new Tool
                {
                    Id = "plugin:" + mod.Plugin.Id, Short = mod.Plugin.Name, Full = mod.Plugin.Name, Section = string.IsNullOrWhiteSpace(mod.Plugin.Section) && mail ? "productivity" : mod.Plugin.Section, Plugin = mod,
                    Icon = mail ? "mail" : "plugin",
                    On = delegate { return each.On; }, Open = delegate { OpenCogFor(each); }
                });
            }
            // A code mod of kind app: its tile opens its options, else its cog. The pet game is the Tamagotchi's tile above.
            foreach (Mod mod in capri.ModList.All)
            {
                if (!mod.Code || !mod.File.App || mod.Plugin != null || Array.IndexOf(mod.File.Permissions, "pet") >= 0) continue;
                Mod each = mod;
                string id = mod.File.Id;
                all.Add(new Tool
                {
                    Id = "mod:" + id, Short = mod.File.Name ?? id, Full = mod.File.Name ?? id, Section = mod.File.Section, Icon = "plugin",
                    On = delegate { return each.On; }, Open = delegate { if (capri.HasModOptions(id)) OpenModOptions(id); else OpenCogFor(each); }
                });
            }
#if CHECKS
            all.AddRange(ExtraTools);
#endif
            List<string> order = capri.ToolOrder;
            all.Sort(delegate(Tool x, Tool y)
            {
                int a = order.IndexOf(x.Id), b = order.IndexOf(y.Id);
                if (a < 0) a = int.MaxValue;
                if (b < 0) b = int.MaxValue;
                return a != b ? a.CompareTo(b) : all.IndexOf(x).CompareTo(all.IndexOf(y));
            });
            return all;
        }

        // The section showing, by id: the one picked last, else their first.
        private string Showing()
        {
            string id = capri.SectionId(capri.ToolSection);
            if (id != null && (id != Companion.Uncategorised || ToolsAt(Tools(), id).Count > 0)) return id;
            List<KeyValuePair<string, string>> sections = capri.Sections;
            return sections.Count > 0 ? sections[0].Key : Companion.Uncategorised;
        }

        // Every tool that sits in a section, on or off.
        private List<Tool> ToolsAt(List<Tool> all, string section) { return all.FindAll(tool => capri.PlaceOf(tool.Id, tool.Section) == section); }

        // The section's tools that are on: one turned off in Toggles, or a plug-in off on Mods, is hidden while installed.
        private List<Tool> ToolsShowing() { return ToolsIn(Tools()); }

        private List<Tool> ToolsIn(List<Tool> all)
        {
            return ToolsAt(all, Showing()).FindAll(tool => tool.On == null || tool.On());
        }

        // CAPRI keeps the bottom of today's front page; the tiles fill what's above it, three to a row.
        private int CapriTop { get { return MainTall(false) - CapriBlock; } }

        private int CapriBlock
        {
            get
            {
                bool ownBuilds = false;
                try { ownBuilds = Brain.SenpaiOn; } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                return Tall - 184 - VoiceHeight - (ownBuilds ? 0 : AbliteratedRow);
            }
        }

        private int TilesShown { get { return TileColumns * Math.Max(1, Math.Min(MostTileRows, (ToolsBottom - PagerRoom - TileTop + TileGap) / (Tile + TileGap))); } }

        private void BuildTools()
        {
            int from = Controls.Count;
            Section("TOOLS", 0, ToolsTop);
            toolSectionMenu = Dropdown();
            toolSectionMenu.AccessibleName = "Tool sections";
            toolSectionKey = Make("", 28, ToolsTop + 30, 328, 26, delegate { toolSectionMenu.Show(toolSectionKey, new Point(0, toolSectionKey.Height)); });
            toolSectionKey.Chevron = true;
            toolSectionKey.AccessibleName = "Tool section";
            for (int i = 0; i < toolTiles.Length; i++)
            {
                int at = i;
                toolTiles[i] = new ToolTile { Bounds = new Rectangle(28 + i % TileColumns * (Tile + TileGap), TileTop + i / TileColumns * (Tile + TileGap), Tile, Tile), Font = Font };
                toolTiles[i].Opened += delegate { OpenTool(toolTiles[at].Shown); };
                toolTiles[i].Dropped += delegate(Point screen) { DropTool(toolTiles[at].Shown, screen); };
                toolTiles[i].Moved += delegate(int by) { if (toolTiles[at].Shown != null) MoveTool(toolTiles[at].Shown.Id, PositionOf(toolTiles[at].Shown.Id) + by); };
                Controls.Add(toolTiles[i]);
            }
            toolBack = Make("Back", 28, TileTop, 60, 22, delegate { toolFirst = Math.Max(0, toolFirst - TilesShown); FillTools(); });
            toolPage = Caption("", 96, TileTop + 3, Font, Faint);
            toolPage.AutoSize = false;
            toolPage.Size = new Size(192, 18);
            toolPage.TextAlign = ContentAlignment.MiddleCenter;
            toolNext = Make("Next", 296, TileTop, 60, 22, delegate { toolFirst += TilesShown; FillTools(); });
            for (int i = from; i < Controls.Count; i++)
            {
                Control control = Controls[i];
                toolLeft.Add(new KeyValuePair<Control, int>(control, control.Left));
                toolTop[control] = control.Top;
                control.Left += Far;
            }
            BuildRingStrip();
        }

        // TOOLS' tips, with her others.
        private void ExplainTools()
        {
            foreach (Control hinted in new Control[] { toolSectionKey, toolBack, toolNext })
                Explain(hinted, "Tools", "Mainframe.cs", null, hinted.Text);
            foreach (ToolTile tile in toolTiles) Explain(tile, "Tools", "Mainframe.cs", null, "Tool");
            Explain(petBack, "Tamagotchi mode", "Mainframe.cs", null, "Back");
            foreach (ToolTile tile in petTiles) Explain(tile, "Tamagotchi mode", "Mainframe.cs", null, tile.Text);
            ExplainRings();
        }

        private void OpenTool(Tool tool)
        {
            if (tool != null && !tool.Soon && tool.Open != null) tool.Open();
        }

        // A tile let go over another takes that one's place.
        private void DropTool(Tool tool, Point screen)
        {
            if (tool == null) return;
            foreach (ToolTile tile in toolTiles)
                if (tile.Visible && tile.Shown != null && tile.Shown != tool && tile.RectangleToScreen(tile.ClientRectangle).Contains(screen)) { MoveTool(tool.Id, PositionOf(tile.Shown.Id)); return; }
        }

        // A tool's place among those showing in its section, from 1.
        internal int PositionOf(string id)
        {
            return ToolsShowing().FindIndex(tool => tool.Id == id) + 1;
        }

        // Moved within its section: the one order keeps hidden tools and the other sections' where they were.
        internal void MoveTool(string id, int position)
        {
            List<Tool> all = Tools();
            List<Tool> section = ToolsIn(all);
            Tool moving = section.Find(tool => tool.Id == id);
            if (moving == null) return;
            position = Math.Max(1, Math.Min(section.Count, position));
            List<int> slots = new List<int>();
            foreach (Tool tool in section) slots.Add(all.IndexOf(tool));
            section.Remove(moving);
            section.Insert(position - 1, moving);
            for (int i = 0; i < slots.Count; i++) all[slots[i]] = section[i];
            // The one order holds mods' places in Toggles too: theirs stay where they were, after the tools.
            List<string> order = all.ConvertAll(tool => tool.Id);
            foreach (string kept in capri.ToolOrder) if (!order.Contains(kept)) order.Add(kept);
            capri.ToolOrder = order;
            toolFirst = (position - 1) / TilesShown * TilesShown;
            FillTools();
            foreach (ToolTile tile in toolTiles) if (tile.Visible && tile.Shown != null && tile.Shown.Id == id && ContainsFocus) tile.Focus();
        }

        // The tiles of the section showing, a page at a time; Back | Page N of M | Next only when they don't all fit.
        internal void FillTools()
        {
            if (toolSectionKey == null) return;
            List<Tool> all = Tools(), showing = ToolsIn(all);
            string section = Showing();
            int room = TilesShown;
            toolSectionKey.Text = capri.SectionName(section);
            // Their sections in their order, and Uncategorised while a tool sits there.
            toolSectionMenu.Items.Clear();
            List<KeyValuePair<string, string>> sections = capri.Sections;
            if (ToolsAt(all, Companion.Uncategorised).Count > 0) sections.Add(new KeyValuePair<string, string>(Companion.Uncategorised, Companion.UncategorisedName));
            foreach (KeyValuePair<string, string> one in sections)
            {
                string id = one.Key;
                toolSectionMenu.Items.Add(Choice(one.Value, one.Key == section, delegate { capri.ToolSection = id; toolFirst = 0; FillTools(); }));
            }
            if (toolFirst >= showing.Count) toolFirst = Math.Max(0, (showing.Count - 1) / room * room);
            for (int i = 0; i < toolTiles.Length; i++)
            {
                Tool tool = i < room && toolFirst + i < showing.Count ? showing[toolFirst + i] : null;
                toolTiles[i].Shown = tool;
                toolTiles[i].Visible = tool != null;
                if (tool == null) continue;
                toolTiles[i].Text = tool.Short;
                toolTiles[i].AccessibleName = tool.Full + (tool.Soon ? ": coming soon, it opens nothing yet" : "");
                toolTiles[i].Invalidate();
            }
            bool paging = showing.Count > room;
            toolBack.Visible = toolNext.Visible = toolPage.Visible = paging;
            int pagerAt = ToolsBottom - PagerRoom;
            toolTop[toolBack] = toolTop[toolNext] = pagerAt;
            toolTop[toolPage] = pagerAt + 3;
            foreach (Control pager in new Control[] { toolBack, toolNext, toolPage }) pager.Top = Scaled(toolTop[pager]);
            FillRingStrip();
            toolBack.Dimmed = toolFirst == 0;
            toolNext.Dimmed = toolFirst + room >= showing.Count;
            toolPage.Text = PageText(toolFirst, showing.Count, room);
            Place();
            ReachMain();
            Invalidate();
        }

        // A tool's tile: clipped corners with her hot strokes, its icon large over its name; greyed with SOON when not out
        // yet. Enter opens it; dragged onto another tile, or Ctrl with an arrow, moves it.
        internal sealed class ToolTile : Control
        {
            internal Tool Shown;
            internal event Action Opened;
            internal event Action<Point> Dropped;
            internal event Action<int> Moved;
            private Point pressedAt;
            private bool pressed, dragging, hovered;

            internal ToolTile()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true);
                TabStop = true;
                Cursor = Cursors.Hand;
                AccessibleRole = AccessibleRole.PushButton;
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left) return;
                pressed = true;
                dragging = false;
                pressedAt = e.Location;
                Focus();
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (pressed && !dragging && (Math.Abs(e.X - pressedAt.X) > SystemInformation.DragSize.Width || Math.Abs(e.Y - pressedAt.Y) > SystemInformation.DragSize.Height))
                {
                    dragging = true;
                    Cursor = Cursors.SizeAll;
                }
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (!pressed) return;
                pressed = false;
                Cursor = Cursors.Hand;
                if (dragging) { dragging = false; if (Dropped != null) Dropped(PointToScreen(e.Location)); }
                else if (ClientRectangle.Contains(e.Location) && Opened != null) Opened();
            }

            protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }

            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }

            protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }

            protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

            protected override bool IsInputKey(Keys keyData)
            {
                return (keyData & Keys.Control) == Keys.Control && ((keyData & Keys.KeyCode) == Keys.Left || (keyData & Keys.KeyCode) == Keys.Right) || base.IsInputKey(keyData);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { e.Handled = e.SuppressKeyPress = true; if (Opened != null) Opened(); }
                else if (e.Control && (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)) { e.Handled = true; if (Moved != null) Moved(e.KeyCode == Keys.Left ? -1 : 1); }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Neon.Ground);
                if (Shown == null) return;
                float s = Width / (float)Tile, line = Math.Max(1f, s), cut = 10 * s;
                bool soon = Shown.Soon, lit = !soon && (hovered || Focused);
                RectangleF box = new RectangleF(line / 2, line / 2, Width - line, Height - line);
                using (GraphicsPath edge = Neon.Cut(box, cut, 0, cut, 0))
                using (Brush fill = new SolidBrush(lit ? Neon.Hover : Neon.Field))
                using (Pen rim = new Pen(lit ? Neon.Cyan : Neon.Edge, line))
                {
                    g.FillPath(fill, edge);
                    g.DrawPath(rim, edge);
                }
                using (Pen hot = new Pen(soon ? Neon.Edge : Neon.Hot, 2 * line))
                {
                    g.DrawLine(hot, box.Left, box.Top + cut, box.Left + cut, box.Top);
                    g.DrawLine(hot, box.Right - cut, box.Bottom, box.Right, box.Bottom - cut);
                }
                float icon = 44 * s;
                GraphicsState state = g.Save();
                g.TranslateTransform((Width - icon) / 2, 14 * s);
                ToolIcon.Draw(g, Shown.Icon, icon, soon, lit ? Neon.Hover : Neon.Field);
                g.Restore(state);
                Rectangle words = new Rectangle((int)(6 * s), (int)(60 * s), Width - (int)(12 * s), Height - (int)(64 * s));
                TextRenderer.DrawText(g, Text, Font, words, soon ? Neon.Faint : Neon.Rose, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                if (soon)
                    using (Font small = Type(Font.SizeInPoints * 0.75f, true))
                        TextRenderer.DrawText(g, "SOON", small, new Rectangle(0, (int)(3 * s), Width - (int)(6 * s), (int)(14 * s)), Neon.Faint, TextFormatFlags.Right | TextFormatFlags.NoPrefix);
            }
        }

#if CHECKS
        // Stand-in tools, for the checks' paging.
        internal readonly List<Tool> ExtraTools = new List<Tool>();

        // TOOLS on the front page: today's width, its tiles, SOON greyed, dragging to reorder, paging; Toggles' folds
        // hiding a tool; pictures at 100 and 150%.
        internal void CheckTools(List<string> checks, string output)
        {
            string sectionWas = capri.ToolSection;
            List<string> orderWas = capri.ToolOrder;
            bool[] onWas = new bool[OwnTools.GetLength(0)];
            for (int i = 0; i < onWas.Length; i++) onWas[i] = capri.ToolOn(OwnTools[i, 0]);
            FormWindowState stateWas = WindowState;
            bool shownWas = Visible;
            List<string> wrong = new List<string>();
            try
            {
                WindowState = FormWindowState.Normal;
                Show();
                ShowSetupPage(false);
                ShowSettings(false);
                capri.ToolSection = "Productivity";
                capri.ToolOrder = new List<string>();
                for (int i = 0; i < onWas.Length; i++) capri.SetToolOn(OwnTools[i, 0], true);
                toolFirst = 0;
                FillTools();
                if (ClientSize.Width != Scaled(Wide)) wrong.Add("the front page is " + ClientSize.Width + " px wide, not today's " + Scaled(Wide));
                if (!toolTiles[0].Visible || toolTiles[0].Shown.Id != "planner" || toolTiles[0].Text != "Planner" || toolTiles[0].Shown.Soon) wrong.Add("Productivity doesn't start with the Planner");
                PickSection("learning");
                bool soon = toolTiles[0].Visible && toolTiles[0].Shown.Id == "circuit" && toolTiles[0].Shown.Soon && toolTiles[1].Visible && toolTiles[1].Shown.Id == "coding" && toolTiles[1].Shown.Soon && !toolTiles[2].Visible;
                // Within a pixel: at 125% and the like an edge and a width round apart, so a 98 tile can come out 123 against 122.
                bool row = toolTiles[1].Top == toolTiles[0].Top && Math.Abs(toolTiles[1].Left - toolTiles[0].Right - Scaled(TileGap)) <= 1
                    && Math.Abs(toolTiles[0].Width - Scaled(Tile)) <= 1 && Math.Abs(toolTiles[0].Height - Scaled(Tile)) <= 1;
                if (!soon || !row) wrong.Add("Learning: the two SOON tiles " + soon + ", in a row " + row + " (" + toolTiles[0].Bounds + ", " + toolTiles[1].Bounds + ", tile " + Scaled(Tile) + ", gap " + Scaled(TileGap) + ")");
                if (toolTiles[TilesShown - 1].Bottom > Scaled(CapriTop)) wrong.Add("the tiles reach CAPRI");
                toolTiles[0].Focus();
                MoveTool("circuit", 2);
                if (PositionOf("circuit") != 2 || toolTiles[1].Shown.Id != "circuit" || capri.ToolOrder.IndexOf("circuit") < capri.ToolOrder.IndexOf("coding")) wrong.Add("moved: circuit at " + PositionOf("circuit"));
                DropTool(toolTiles[1].Shown, toolTiles[0].PointToScreen(new Point(toolTiles[0].Width / 2, toolTiles[0].Height / 2)));
                if (PositionOf("circuit") != 1) wrong.Add("a tile dropped onto the first didn't take its place: circuit at " + PositionOf("circuit"));
                for (int i = 0; i < 30; i++) ExtraTools.Add(new Tool { Id = "stand-in-" + i, Short = "Stand-in " + i, Full = "Stand-in " + i, Section = "Learning", Icon = "plugin" });
                FillTools();
                int count = ToolsShowing().Count, room = TilesShown;
                bool paged = toolNext.Visible && toolPage.Text == PageText(0, count, room) && toolBack.Dimmed && !toolNext.Dimmed && toolBack.Top > toolTiles[room - 1].Bottom && toolBack.Bottom < Scaled(CapriTop);
                toolNext.PerformClick();
                bool second = toolFirst == room && toolPage.Text.StartsWith("Page 2 of", StringComparison.Ordinal) && toolTiles[0].Visible;
                MoveTool(ToolsShowing()[count - 1].Id, 1);
                bool toFront = toolFirst == 0 && toolTiles[0].Shown != null && toolTiles[0].Shown.Id.StartsWith("stand-in-", StringComparison.Ordinal);
                if (!paged || !second || !toFront) wrong.Add("paging: " + paged + ", page 2 " + second + ", moved to page 1 " + toFront + " (" + toolPage.Text + ")");
                // Many tiles, with the Tamagotchi strip on and off, at 100 and 150%: the tiles and the pager stop at the strip's
                // divider while it shows, the rings sit between it and CAPRI, and every page is reached; off, they reach CAPRI.
                Func<bool> stripWas = RingStripFor;
                try
                {
                    foreach (int percent in new[] { 100, 150 })
                        foreach (bool strip in new[] { false, true })
                        {
                            RingStripFor = delegate { return strip; };
                            LayoutScale((int)Math.Round(96 * percent / 100f), 100);
                            toolFirst = 0;
                            FillTools();
                            Place();
                            Application.DoEvents();
                            int shownCount = ToolsShowing().Count, perPage = TilesShown, pages = (shownCount + perPage - 1) / perPage;
                            string at = (strip ? "strip on" : "strip off") + " at " + percent + "%";
                            int lastShown = -1;
                            for (int i = 0; i < toolTiles.Length; i++) if (toolTiles[i].Visible) lastShown = i;
                            if (lastShown < 0 || toolTiles[lastShown].Bottom > Scaled(ToolsBottom) || toolBack.Bottom > Scaled(ToolsBottom)) wrong.Add(at + ": the tiles or the pager run past " + (strip ? "the strip" : "CAPRI"));
                            string rings = RingStripText;
                            if (strip != (rings != null)) wrong.Add(at + ": the strip " + (strip ? "isn't" : "is") + " shown");
                            if (strip && (stripRings[0].Top < Scaled(StripTop) || stripRings[0].Bottom > Scaled(CapriTop) || stripRings[stripRings.Count - 1].Right > Scaled(Wide))) wrong.Add(at + ": the rings sit outside the strip (" + stripRings[0].Bounds + ")");
                            int reached = 1;
                            while (!toolNext.Dimmed && reached < pages + 2) { toolNext.PerformClick(); reached++; }
                            if (reached != pages || !toolPage.Text.StartsWith("Page " + pages + " of " + pages, StringComparison.Ordinal)) wrong.Add(at + ": " + reached + " of " + pages + " pages reached (" + toolPage.Text + ")");
                            using (Bitmap picture = new Bitmap(Width, Height))
                            {
                                DrawToBitmap(picture, new Rectangle(Point.Empty, picture.Size));
                                picture.Save(System.IO.Path.Combine(output, "tools-paging-" + (strip ? "strip" : "plain") + "-" + percent + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                            }
                        }
                }
                finally
                {
                    RingStripFor = stripWas;
                    LayoutScale(0, 0);
                    toolFirst = 0;
                    FillTools();
                    Place();
                }
                ExtraTools.Clear();
                capri.ToolOrder = new List<string>();
                PickSection("homelab");
                if (capri.ToolSection != "homelab" || toolSectionKey.Text != "Homelab" || toolTiles[0].Visible) wrong.Add("sections: " + capri.ToolSection);
                // Fun: the pet game while its mod runs, then Music and her four boards, as her Play menu has them.
                PickSection("fun");
                List<string> fun = new List<string>();
                foreach (ToolTile tile in toolTiles) if (tile.Visible) fun.Add(tile.Shown.Id);
                string funWanted = (capri.ModList.PetId != null ? "pet " : "") + "music chess connect_four tic_tac_toe rock_paper_scissors";
                if (string.Join(" ", fun.ToArray()) != funWanted) wrong.Add("Fun: " + string.Join(" ", fun.ToArray()));
                PickSection("productivity");
                // Each of her tools a fold in Toggles whose switch hides its tile.
                string folds = ToolFolds();
                if (folds != null) wrong.Add(folds);
                ToolStripMenuItem plannerRow = RowOf("planner");
                if (plannerRow != null) plannerRow.PerformClick();
                if (capri.ToolOn("planner") || PositionOf("planner") != 0 || toolTiles[0].Visible && toolTiles[0].Shown.Id == "planner") wrong.Add("the planner turned off in Toggles still shows");
                capri.SetToolOn("planner", true);
                if (PositionOf("planner") != 1) wrong.Add("the planner turned back on isn't first");
                // Her windows' tiles: CAPRI's six, Utility's two, and Workspace after the Planner.
                foreach (string[] wanted in new[] { new[] { "capri", "toggles colours personality security md_editor memory growth" }, new[] { "utility", "modding streaming" }, new[] { "productivity", "planner workspace" } })
                {
                    PickSection(wanted[0]);
                    List<string> shown = new List<string>();
                    foreach (ToolTile tile in toolTiles) if (tile.Visible) shown.Add(tile.Shown.Id);
                    if (string.Join(" ", shown.ToArray()) != wanted[1]) wrong.Add(wanted[0] + ": " + string.Join(" ", shown.ToArray()));
                }
                Preferences older = new Preferences { ToolSectionList = new[] { "fun|Games", "productivity|Productivity" } }, gone = new Preferences { ToolSectionList = new[] { "fun|Fun" }, WindowSectionsAdded = true };
                Companion.AddWindowSections(older);
                Companion.AddWindowSections(gone);
                if (string.Join(" ", older.ToolSectionList) != "capri|CAPRI fun|Games productivity|Productivity utility|Utility" || !older.WindowSectionsAdded || gone.ToolSectionList.Length != 1)
                    wrong.Add("their own sections didn't get CAPRI and Utility once: " + string.Join(" ", older.ToolSectionList) + " / " + string.Join(" ", gone.ToolSectionList));
                foreach (string section in new[] { "capri", "learning", "productivity", "fun", "utility" })
                {
                    PickSection(section);
                    foreach (int percent in new[] { 100, 150 })
                    {
                        LayoutScale(96, percent, false);
                        FillTools();
                        Application.DoEvents();
                        string crowded = Overlap();
                        if (crowded != null) wrong.Add(section + " at " + percent + "%: " + crowded);
                        using (Bitmap picture = new Bitmap(Width, Height))
                        {
                            DrawToBitmap(picture, new Rectangle(Point.Empty, picture.Size));
                            picture.Save(Path.Combine(output, "tools-" + section + "-" + percent + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
                }
            }
            finally
            {
                ExtraTools.Clear();
                for (int i = 0; i < onWas.Length; i++) capri.SetToolOn(OwnTools[i, 0], onWas[i]);
                capri.ToolSection = sectionWas;
                capri.ToolOrder = orderWas;
                LayoutScale(0, 0);
                FillTools();
                WindowState = stateWas;
                if (!shownWas) Hide();
            }
            if (wrong.Count > 0) throw new Exception("TOOLS went wrong: " + string.Join("; ", wrong.ToArray()));
            checks.Add("PASS Each of her tools has a switch row in its section's Sec: fold in Toggles that hides its tile while installed (the circuit designer's with its package line)");
            checks.Add("PASS TOOLS on the front page at today's width: tiles three to a row, Productivity with the Planner, Learning with Circuit designer and Coding teacher greyed with SOON, Fun with Music and her four boards (the pet game while it runs), CAPRI with Toggles, Colours, Personality, Security, MD editor, Memory and Growth, Utility with Modding and Streaming, Workspace after the Planner, CAPRI and Utility added once to sections they set before, a tile dragged onto another or moved with Ctrl and an arrow taking its place and kept, paged Back | Page N of M | Next above CAPRI only when they don't fit, a new section starting at page 1; no overlaps at 100 or 150% (tools-<section>-*.png)");
        }

        // A section picked from TOOLS' dropdown, by its id.
        private void PickSection(string id)
        {
            string named = capri.SectionName(id);
            foreach (ToolStripItem item in toolSectionMenu.Items) if (item.Text == named) { item.PerformClick(); return; }
        }

        // Her tool's row in its Sec: fold in her Toggles menu.
        private ToolStripMenuItem RowOf(string id)
        {
            capri.SectionsChanged();
            foreach (ToolStripItem one in capri.TogglesMenu.DropDownItems)
            {
                ToolStripMenuItem fold = one as ToolStripMenuItem;
                if (fold == null || !(fold.Tag is string) || !((string)fold.Tag).StartsWith(SecTag, StringComparison.Ordinal)) continue;
                foreach (ToolStripItem row in fold.DropDownItems) if (Equals(row.Tag, "tool:" + id)) return row as ToolStripMenuItem;
            }
            return null;
        }

        // Each of her tools a switch row in its section's Sec: fold, ticked as it is; the circuit designer's with its package line.
        private string ToolFolds()
        {
            List<string> wrong = new List<string>();
            for (int i = 0; i < OwnTools.GetLength(0); i++)
            {
                ToolStripMenuItem row = RowOf(OwnTools[i, 0]);
                if (row == null) { wrong.Add("no row for " + OwnTools[i, 1]); continue; }
                typeof(ToolStripDropDownItem).GetMethod("OnDropDownShow", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(row.OwnerItem, new object[] { EventArgs.Empty });
                if (row.Checked != capri.ToolOn(OwnTools[i, 0])) wrong.Add(OwnTools[i, 1] + "'s switch isn't ticked as it is");
            }
            ToolStripMenuItem circuit = RowOf("circuit");
            if (circuit == null || circuit.DropDownItems.Count < 1 || !Equals(circuit.DropDownItems[0].Tag, TextTag)) wrong.Add("the circuit designer's row has no package line");
            return wrong.Count == 0 ? null : string.Join("; ", wrong.ToArray());
        }
#endif

        // "Page N of M", one wording for TOOLS and the Mods list.
        internal static string PageText(int first, int total, int size)
        {
            return "Page " + (first / Math.Max(1, size) + 1) + " of " + Math.Max(1, (total - 1) / Math.Max(1, size) + 1);
        }

        // A tool's icon in her palette, from the planner's notes with a joystick to a plug; her windows' too: switches, a
        // palette, a brain, a shield, an .md page, a memory stick, a cog, a screen going live and a terminal.
        internal static class ToolIcon
        {
            // Drawn s across at the origin, greyed for a tool not out yet; under is what it sits on.
            internal static void Draw(Graphics g, string Kind, float s, bool greyed, Color under)
            {
                float w = Math.Max(1.2f, s / 22f);
                using (Pen cyan = new Pen(greyed ? Neon.Faint : Neon.Cyan, w))
                using (Pen red = new Pen(greyed ? Neon.Edge : Neon.Hot, w))
                using (Brush ground = new SolidBrush(under))
                using (Brush dot = new SolidBrush(greyed ? Neon.Edge : Neon.Hot))
                {
                    if (Kind == "planner")
                    {
                        float x0 = s * 0.16f, y0 = s * 0.05f, x1 = s * 0.90f, y1 = s * 0.95f, fold = s * 0.16f;
                        g.DrawPolygon(cyan, new[] { new PointF(x0, y0), new PointF(x1 - fold, y0), new PointF(x1, y0 + fold), new PointF(x1, y1), new PointF(x0, y1) });
                        g.DrawLines(cyan, new[] { new PointF(x1 - fold, y0), new PointF(x1 - fold, y0 + fold), new PointF(x1, y0 + fold) });
                        for (int i = 0; i < 4; i++) g.DrawLine(cyan, x0 + s * 0.10f, y0 + s * (0.24f + i * 0.14f), i == 3 ? x0 + s * 0.42f : x1 - s * 0.10f, y0 + s * (0.24f + i * 0.14f));
                        RectangleF pad = new RectangleF(s * 0.04f, s * 0.70f, s * 0.46f, s * 0.25f);
                        PointF stem = new PointF(pad.X + pad.Width / 2, pad.Top), top = new PointF(stem.X - s * 0.03f, s * 0.54f);
                        g.FillRectangle(ground, pad);
                        g.DrawLine(red, stem, top);
                        g.FillEllipse(ground, top.X - s * 0.08f, top.Y - s * 0.08f, s * 0.16f, s * 0.16f);
                        g.DrawEllipse(red, top.X - s * 0.08f, top.Y - s * 0.08f, s * 0.16f, s * 0.16f);
                        g.DrawRectangle(red, pad.X, pad.Y, pad.Width, pad.Height);
                    }
                    else if (Kind == "chip")
                    {
                        g.DrawRectangle(cyan, s * 0.25f, s * 0.25f, s * 0.5f, s * 0.5f);
                        for (int i = 0; i < 3; i++)
                        {
                            float at = s * (0.33f + i * 0.17f);
                            g.DrawLine(cyan, at, s * 0.05f, at, s * 0.25f);
                            g.DrawLine(cyan, at, s * 0.75f, at, s * 0.95f);
                            g.DrawLine(cyan, s * 0.05f, at, s * 0.25f, at);
                            g.DrawLine(cyan, s * 0.75f, at, s * 0.95f, at);
                        }
                        g.DrawLines(red, new[] { new PointF(s * 0.38f, s * 0.42f), new PointF(s * 0.55f, s * 0.42f), new PointF(s * 0.55f, s * 0.58f) });
                    }
                    else if (Kind == "mail")
                    {
                        g.DrawRectangle(cyan, s * 0.08f, s * 0.24f, s * 0.84f, s * 0.56f);
                        g.DrawLines(red, new[] { new PointF(s * 0.08f, s * 0.24f), new PointF(s * 0.5f, s * 0.58f), new PointF(s * 0.92f, s * 0.24f) });
                    }
                    else if (Kind == "code")
                    {
                        g.DrawRectangle(cyan, s * 0.06f, s * 0.14f, s * 0.88f, s * 0.72f);
                        g.DrawLine(cyan, s * 0.06f, s * 0.28f, s * 0.94f, s * 0.28f);
                        g.DrawLines(red, new[] { new PointF(s * 0.36f, s * 0.42f), new PointF(s * 0.24f, s * 0.56f), new PointF(s * 0.36f, s * 0.70f) });
                        g.DrawLines(red, new[] { new PointF(s * 0.64f, s * 0.42f), new PointF(s * 0.76f, s * 0.56f), new PointF(s * 0.64f, s * 0.70f) });
                        g.DrawLine(cyan, s * 0.55f, s * 0.40f, s * 0.45f, s * 0.72f);
                    }
                    else if (Kind == "pet")
                    {
                        g.DrawEllipse(cyan, s * 0.22f, s * 0.04f, s * 0.56f, s * 0.90f);
                        g.DrawRectangle(cyan, s * 0.34f, s * 0.24f, s * 0.32f, s * 0.26f);
                        g.FillRectangle(dot, s * 0.41f, s * 0.32f, s * 0.05f, s * 0.07f);
                        g.FillRectangle(dot, s * 0.54f, s * 0.32f, s * 0.05f, s * 0.07f);
                        foreach (float x in new[] { 0.36f, 0.50f, 0.64f }) g.DrawEllipse(red, s * (x - 0.04f), s * 0.64f, s * 0.08f, s * 0.08f);
                    }
                    else if (Kind == "feed" || Kind == "fullness")
                    {
                        g.DrawArc(cyan, s * 0.10f, s * 0.14f, s * 0.80f, s * 0.70f, 0, 180);
                        g.DrawLine(cyan, s * 0.10f, s * 0.49f, s * 0.90f, s * 0.49f);
                        g.DrawArc(red, s * 0.30f, s * 0.30f, s * 0.40f, s * 0.24f, 180, 180);
                    }
                    else if (Kind == "gift")
                    {
                        g.DrawRectangle(cyan, s * 0.16f, s * 0.40f, s * 0.68f, s * 0.48f);
                        g.DrawRectangle(cyan, s * 0.10f, s * 0.28f, s * 0.80f, s * 0.12f);
                        g.DrawLine(red, s * 0.50f, s * 0.28f, s * 0.50f, s * 0.88f);
                        g.DrawEllipse(red, s * 0.30f, s * 0.10f, s * 0.20f, s * 0.18f);
                        g.DrawEllipse(red, s * 0.50f, s * 0.10f, s * 0.20f, s * 0.18f);
                    }
                    else if (Kind == "pat")
                    {
                        g.DrawRectangle(cyan, s * 0.22f, s * 0.42f, s * 0.56f, s * 0.46f);
                        for (int i = 0; i < 4; i++) g.DrawRectangle(cyan, s * (0.24f + i * 0.14f), s * (0.12f + (i % 3) * 0.04f), s * 0.10f, s * 0.30f);
                        g.DrawLine(red, s * 0.40f, s * 0.64f, s * 0.60f, s * 0.64f);
                    }
                    else if (Kind == "play")
                    {
                        g.DrawEllipse(cyan, s * 0.14f, s * 0.14f, s * 0.72f, s * 0.72f);
                        g.DrawArc(red, s * 0.02f, s * 0.30f, s * 0.58f, s * 0.70f, 300, 90);
                        g.DrawArc(red, s * 0.40f, s * 0.00f, s * 0.58f, s * 0.70f, 120, 90);
                    }
                    else if (Kind == "energy")
                        g.DrawPolygon(cyan, new[] { new PointF(s * 0.58f, s * 0.06f), new PointF(s * 0.24f, s * 0.56f), new PointF(s * 0.48f, s * 0.56f), new PointF(s * 0.40f, s * 0.94f), new PointF(s * 0.76f, s * 0.40f), new PointF(s * 0.52f, s * 0.40f) });
                    else if (Kind == "stimulation")
                    {
                        PointF[] star = new PointF[10];
                        for (int i = 0; i < 10; i++)
                        {
                            double turn = -Math.PI / 2 + i * Math.PI / 5, far = i % 2 == 0 ? 0.44 : 0.18;
                            star[i] = new PointF(s * (0.5f + (float)(far * Math.Cos(turn))), s * (0.52f + (float)(far * Math.Sin(turn))));
                        }
                        g.DrawPolygon(cyan, star);
                    }
                    else if (Kind == "social")
                    {
                        g.DrawEllipse(cyan, s * 0.12f, s * 0.16f, s * 0.28f, s * 0.28f);
                        g.DrawEllipse(cyan, s * 0.60f, s * 0.16f, s * 0.28f, s * 0.28f);
                        g.DrawArc(cyan, s * 0.02f, s * 0.50f, s * 0.48f, s * 0.54f, 180, 180);
                        g.DrawArc(cyan, s * 0.50f, s * 0.50f, s * 0.48f, s * 0.54f, 180, 180);
                    }
                    else if (Kind == "comfort")
                    {
                        g.DrawArc(red, s * 0.12f, s * 0.18f, s * 0.40f, s * 0.40f, 135, 225);
                        g.DrawArc(red, s * 0.48f, s * 0.18f, s * 0.40f, s * 0.40f, 180, 225);
                        g.DrawLines(red, new[] { new PointF(s * 0.15f, s * 0.48f), new PointF(s * 0.50f, s * 0.88f), new PointF(s * 0.85f, s * 0.48f) });
                    }
                    else if (Kind == "cleanliness")
                    {
                        foreach (float[] spark in new[] { new[] { 0.40f, 0.56f, 0.30f }, new[] { 0.74f, 0.26f, 0.16f } })
                        {
                            float cx = s * spark[0], cy = s * spark[1], r = s * spark[2];
                            g.DrawPolygon(cyan, new[] { new PointF(cx, cy - r), new PointF(cx + r * 0.3f, cy - r * 0.3f), new PointF(cx + r, cy), new PointF(cx + r * 0.3f, cy + r * 0.3f), new PointF(cx, cy + r), new PointF(cx - r * 0.3f, cy + r * 0.3f), new PointF(cx - r, cy), new PointF(cx - r * 0.3f, cy - r * 0.3f) });
                        }
                    }
                    else if (Kind == "music")
                    {
                        g.DrawLine(cyan, s * 0.40f, s * 0.24f, s * 0.40f, s * 0.74f);
                        g.DrawLine(cyan, s * 0.82f, s * 0.12f, s * 0.82f, s * 0.62f);
                        g.DrawLine(cyan, s * 0.40f, s * 0.24f, s * 0.82f, s * 0.12f);
                        g.DrawLine(cyan, s * 0.40f, s * 0.34f, s * 0.82f, s * 0.22f);
                        g.FillEllipse(dot, s * 0.18f, s * 0.66f, s * 0.24f, s * 0.17f);
                        g.FillEllipse(dot, s * 0.60f, s * 0.54f, s * 0.24f, s * 0.17f);
                    }
                    else if (Kind == "chess")
                    {
                        g.DrawEllipse(cyan, s * 0.38f, s * 0.06f, s * 0.24f, s * 0.24f);
                        g.DrawPolygon(cyan, new[] { new PointF(s * 0.42f, s * 0.32f), new PointF(s * 0.58f, s * 0.32f), new PointF(s * 0.66f, s * 0.74f), new PointF(s * 0.34f, s * 0.74f) });
                        g.DrawRectangle(red, s * 0.24f, s * 0.78f, s * 0.52f, s * 0.14f);
                    }
                    else if (Kind == "connect_four")
                    {
                        g.DrawRectangle(cyan, s * 0.06f, s * 0.18f, s * 0.88f, s * 0.64f);
                        for (int row = 0; row < 3; row++)
                            for (int col = 0; col < 4; col++)
                            {
                                RectangleF hole = new RectangleF(s * (0.15f + col * 0.2f), s * (0.26f + row * 0.18f), s * 0.11f, s * 0.11f);
                                if ((row + col) % 3 == 0) g.FillEllipse(dot, hole); else g.DrawEllipse(cyan, hole);
                            }
                    }
                    else if (Kind == "tic_tac_toe")
                    {
                        foreach (float at in new[] { 0.38f, 0.62f })
                        {
                            g.DrawLine(cyan, s * at, s * 0.08f, s * at, s * 0.92f);
                            g.DrawLine(cyan, s * 0.08f, s * at, s * 0.92f, s * at);
                        }
                        foreach (float at in new[] { 0.14f, 0.70f })
                        {
                            g.DrawLine(red, s * at, s * at, s * (at + 0.16f), s * (at + 0.16f));
                            g.DrawLine(red, s * (at + 0.16f), s * at, s * at, s * (at + 0.16f));
                        }
                        g.DrawEllipse(red, s * 0.43f, s * 0.43f, s * 0.14f, s * 0.14f);
                    }
                    else if (Kind == "rock_paper_scissors")
                    {
                        g.DrawRectangle(cyan, s * 0.10f, s * 0.08f, s * 0.30f, s * 0.30f);
                        g.DrawEllipse(cyan, s * 0.12f, s * 0.52f, s * 0.26f, s * 0.26f);
                        g.DrawLine(red, s * 0.52f, s * 0.86f, s * 0.92f, s * 0.12f);
                        g.DrawLine(red, s * 0.52f, s * 0.12f, s * 0.92f, s * 0.86f);
                        g.DrawEllipse(red, s * 0.44f, s * 0.74f, s * 0.14f, s * 0.14f);
                        g.DrawEllipse(red, s * 0.86f, s * 0.74f, s * 0.14f, s * 0.14f);
                    }
                    else if (Kind == "toggles")
                    {
                        g.DrawArc(cyan, s * 0.14f, s * 0.14f, s * 0.26f, s * 0.26f, 90, 180);
                        g.DrawArc(cyan, s * 0.60f, s * 0.14f, s * 0.26f, s * 0.26f, 270, 180);
                        g.DrawLine(cyan, s * 0.27f, s * 0.14f, s * 0.73f, s * 0.14f);
                        g.DrawLine(cyan, s * 0.27f, s * 0.40f, s * 0.73f, s * 0.40f);
                        g.FillEllipse(dot, s * 0.62f, s * 0.17f, s * 0.20f, s * 0.20f);
                        g.DrawArc(cyan, s * 0.14f, s * 0.56f, s * 0.26f, s * 0.26f, 90, 180);
                        g.DrawArc(cyan, s * 0.60f, s * 0.56f, s * 0.26f, s * 0.26f, 270, 180);
                        g.DrawLine(cyan, s * 0.27f, s * 0.56f, s * 0.73f, s * 0.56f);
                        g.DrawLine(cyan, s * 0.27f, s * 0.82f, s * 0.73f, s * 0.82f);
                        g.DrawEllipse(cyan, s * 0.18f, s * 0.59f, s * 0.20f, s * 0.20f);
                    }
                    else if (Kind == "colours")
                    {
                        g.DrawEllipse(cyan, s * 0.08f, s * 0.12f, s * 0.84f, s * 0.76f);
                        g.FillEllipse(dot, s * 0.24f, s * 0.30f, s * 0.14f, s * 0.14f);
                        g.DrawEllipse(cyan, s * 0.44f, s * 0.22f, s * 0.14f, s * 0.14f);
                        g.FillEllipse(dot, s * 0.64f, s * 0.36f, s * 0.14f, s * 0.14f);
                        g.FillEllipse(ground, s * 0.38f, s * 0.56f, s * 0.20f, s * 0.20f);
                        g.DrawEllipse(red, s * 0.38f, s * 0.56f, s * 0.20f, s * 0.20f);
                    }
                    else if (Kind == "brain")
                    {
                        g.DrawArc(cyan, s * 0.08f, s * 0.12f, s * 0.46f, s * 0.42f, 90, 180);
                        g.DrawArc(cyan, s * 0.08f, s * 0.44f, s * 0.46f, s * 0.42f, 90, 180);
                        g.DrawArc(cyan, s * 0.46f, s * 0.12f, s * 0.46f, s * 0.42f, 270, 180);
                        g.DrawArc(cyan, s * 0.46f, s * 0.44f, s * 0.46f, s * 0.42f, 270, 180);
                        g.DrawLine(cyan, s * 0.31f, s * 0.12f, s * 0.69f, s * 0.12f);
                        g.DrawLine(cyan, s * 0.31f, s * 0.86f, s * 0.69f, s * 0.86f);
                        g.DrawLine(red, s * 0.50f, s * 0.12f, s * 0.50f, s * 0.86f);
                        g.DrawArc(red, s * 0.20f, s * 0.30f, s * 0.24f, s * 0.20f, 200, 160);
                        g.DrawArc(red, s * 0.56f, s * 0.50f, s * 0.24f, s * 0.20f, 20, 160);
                    }
                    else if (Kind == "radar")
                    {
                        PointF[] hex = new PointF[6], shape = new PointF[6];
                        float[] reach = { 0.9f, 0.55f, 0.75f, 0.35f, 0.6f, 0.45f };
                        for (int i = 0; i < 6; i++)
                        {
                            double angle = -Math.PI / 2 + Math.PI * i / 3;
                            hex[i] = new PointF(s * (0.5f + 0.44f * (float)Math.Cos(angle)), s * (0.5f + 0.44f * (float)Math.Sin(angle)));
                            shape[i] = new PointF(s * (0.5f + 0.44f * reach[i] * (float)Math.Cos(angle)), s * (0.5f + 0.44f * reach[i] * (float)Math.Sin(angle)));
                            g.DrawLine(cyan, s * 0.5f, s * 0.5f, hex[i].X, hex[i].Y);
                        }
                        g.DrawPolygon(cyan, hex);
                        g.DrawPolygon(red, shape);
                    }
                    else if (Kind == "shield")
                    {
                        g.DrawPolygon(cyan, new[] { new PointF(s * 0.50f, s * 0.06f), new PointF(s * 0.86f, s * 0.20f), new PointF(s * 0.80f, s * 0.62f), new PointF(s * 0.50f, s * 0.94f), new PointF(s * 0.20f, s * 0.62f), new PointF(s * 0.14f, s * 0.20f) });
                        g.DrawLines(red, new[] { new PointF(s * 0.34f, s * 0.48f), new PointF(s * 0.46f, s * 0.60f), new PointF(s * 0.68f, s * 0.36f) });
                    }
                    else if (Kind == "md")
                    {
                        float x0 = s * 0.16f, y0 = s * 0.05f, x1 = s * 0.84f, y1 = s * 0.95f, fold = s * 0.18f;
                        g.DrawPolygon(cyan, new[] { new PointF(x0, y0), new PointF(x1 - fold, y0), new PointF(x1, y0 + fold), new PointF(x1, y1), new PointF(x0, y1) });
                        g.DrawLines(red, new[] { new PointF(s * 0.28f, s * 0.66f), new PointF(s * 0.28f, s * 0.40f), new PointF(s * 0.38f, s * 0.54f), new PointF(s * 0.48f, s * 0.40f), new PointF(s * 0.48f, s * 0.66f) });
                        g.DrawLine(red, s * 0.64f, s * 0.38f, s * 0.64f, s * 0.66f);
                        g.DrawLines(red, new[] { new PointF(s * 0.56f, s * 0.56f), new PointF(s * 0.64f, s * 0.66f), new PointF(s * 0.72f, s * 0.56f) });
                    }
                    else if (Kind == "memory")
                    {
                        g.DrawRectangle(cyan, s * 0.06f, s * 0.30f, s * 0.88f, s * 0.34f);
                        for (int i = 0; i < 3; i++) g.FillRectangle(dot, s * (0.14f + i * 0.26f), s * 0.38f, s * 0.18f, s * 0.18f);
                        for (int i = 0; i < 8; i++) g.DrawLine(cyan, s * (0.12f + i * 0.108f), s * 0.64f, s * (0.12f + i * 0.108f), s * 0.76f);
                    }
                    else if (Kind == "modding")
                    {
                        for (int i = 0; i < 8; i++)
                        {
                            double turn = i * Math.PI / 4;
                            g.DrawLine(cyan, s * (0.42f + (float)(0.26 * Math.Cos(turn))), s * (0.42f + (float)(0.26 * Math.Sin(turn))), s * (0.42f + (float)(0.36 * Math.Cos(turn))), s * (0.42f + (float)(0.36 * Math.Sin(turn))));
                        }
                        g.DrawEllipse(cyan, s * 0.16f, s * 0.16f, s * 0.52f, s * 0.52f);
                        g.DrawEllipse(red, s * 0.32f, s * 0.32f, s * 0.20f, s * 0.20f);
                        g.DrawLine(red, s * 0.70f, s * 0.70f, s * 0.92f, s * 0.92f);
                    }
                    else if (Kind == "streaming")
                    {
                        g.DrawRectangle(cyan, s * 0.18f, s * 0.18f, s * 0.64f, s * 0.46f);
                        g.DrawLine(cyan, s * 0.50f, s * 0.64f, s * 0.50f, s * 0.78f);
                        g.DrawLine(cyan, s * 0.34f, s * 0.80f, s * 0.66f, s * 0.80f);
                        g.FillEllipse(dot, s * 0.40f, s * 0.31f, s * 0.20f, s * 0.20f);
                        g.DrawArc(red, s * 0.00f, s * 0.14f, s * 0.14f, s * 0.54f, 120, 120);
                        g.DrawArc(red, s * 0.86f, s * 0.14f, s * 0.14f, s * 0.54f, 300, 120);
                    }
                    else if (Kind == "workspace")
                    {
                        g.DrawRectangle(cyan, s * 0.06f, s * 0.14f, s * 0.88f, s * 0.72f);
                        g.DrawLine(cyan, s * 0.06f, s * 0.28f, s * 0.94f, s * 0.28f);
                        g.DrawLines(red, new[] { new PointF(s * 0.20f, s * 0.42f), new PointF(s * 0.34f, s * 0.54f), new PointF(s * 0.20f, s * 0.66f) });
                        g.DrawLine(red, s * 0.42f, s * 0.68f, s * 0.66f, s * 0.68f);
                    }
                    else if (Kind == "plugin")
                    {
                        g.DrawRectangle(cyan, s * 0.22f, s * 0.30f, s * 0.56f, s * 0.36f);
                        g.DrawLine(cyan, s * 0.38f, s * 0.08f, s * 0.38f, s * 0.30f);
                        g.DrawLine(cyan, s * 0.62f, s * 0.08f, s * 0.62f, s * 0.30f);
                        g.DrawLine(red, s * 0.5f, s * 0.66f, s * 0.5f, s * 0.95f);
                        g.FillEllipse(dot, s * 0.44f, s * 0.42f, s * 0.12f, s * 0.12f);
                    }
                }
            }
        }
    }
}
