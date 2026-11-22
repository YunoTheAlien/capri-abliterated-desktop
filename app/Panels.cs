using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Settings' sections as panels: a heading and its rows are one panel, and each column stacks its panels, so moving
    // one moves those under it. Carve cuts the built page into panels, Graft moves rows between them, SetPanels sets
    // each column.
    internal sealed partial class Mainframe
    {
        private sealed class Panel
        {
            // Its heading's words without " >": "VOICE", "CAPRI".
            internal string Id;
            internal Head Head;
            // The column it was built in and the one it sits in now, 0 left and 1 right; how tall it stands as built,
            // gap below included; the heading it was built above.
            internal int Built, Column, Span;
            internal Panel Before;
            internal readonly Dictionary<Control, int> Offsets = new Dictionary<Control, int>();
            // A mod's panel: the mod's id and the slot it asked for. Null for hers.
            internal string Mod;
            internal int Slot;
            // How the panel is saved in her panel order: its id, or "mod:<id>:<title>" for a mod's.
            internal string Key { get { return Mod == null ? Id : "mod:" + Mod + ":" + Id; } }
            // Stretches Simple Settings hides, as offsets from the heading.
            internal readonly List<int[]> Folds = new List<int[]>();
        }

        // Each column's panels, top to bottom.
        private readonly List<Panel>[] columns = { new List<Panel>(), new List<Panel>() };
        private readonly Dictionary<Control, Panel> panelOf = new Dictionary<Control, Panel>();
        // Where each column's first panel starts, as built.
        private readonly int[] columnTop = new int[2];
        // The panel the row Free voice in games leaves is in, and the rows under it that move up with it.
        private Panel liftPanel;
        private readonly HashSet<Control> lifting = new HashSet<Control>();
        // A row belongs to the panel whose heading is above it, give or take this: a heading's own keys sit 3 px over it.
        private const int HeadSlack = 4;
        // The gap under a panel whose neighbour below is not the one it was built over: the most common gap as built.
        private int panelGap = 20;
        // What a regroup took off the page: a panel left with nothing in it, and its heading.
        private readonly HashSet<Control> dropped = new HashSet<Control>();

        private static string PanelId(Head head) { return head.Text.Replace(">", "").Trim(); }

        private Panel FindPanel(string id) { foreach (List<Panel> list in columns) foreach (Panel panel in list) if (panel.Id == id) return panel; return null; }

        // Cuts the page as built into panels, in the order they were built.
        private void Carve()
        {
            foreach (KeyValuePair<Control, int> moved in underVoice) lifting.Add(moved.Key);
            for (int column = 0; column < 2; column++)
            {
                List<Head> heads = new List<Head>();
                foreach (KeyValuePair<Control, int> entry in pageLeft)
                {
                    Head head = entry.Key as Head;
                    if (head != null && head.Span > 0 && (entry.Value >= Column ? 1 : 0) == column && built.ContainsKey(head)) heads.Add(head);
                }
                heads.Sort(delegate(Head one, Head other) { return built[one].CompareTo(built[other]); });
                Panel above = null;
                for (int i = 0; i < heads.Count; i++)
                {
                    int top = built[heads[i]];
                    Panel panel = new Panel { Id = PanelId(heads[i]), Head = heads[i], Built = column, Column = column, Span = i + 1 < heads.Count ? built[heads[i + 1]] - top : int.MaxValue };
                    if (above != null) above.Before = panel;
                    columns[column].Add(panel);
                    WireDrag(heads[i]);
                    above = panel;
                }
                columnTop[column] = heads.Count > 0 ? built[heads[0]] : 0;
            }
            foreach (KeyValuePair<Control, int> entry in pageLeft)
            {
                int top;
                if (!built.TryGetValue(entry.Key, out top)) continue;
                foreach (Panel panel in columns[entry.Value >= Column ? 1 : 0])
                {
                    int head = built[panel.Head];
                    if (top < head - HeadSlack || (panel.Span != int.MaxValue && top >= head + panel.Span - HeadSlack)) continue;
                    panel.Offsets[entry.Key] = top - head;
                    panelOf[entry.Key] = panel;
                    break;
                }
            }
            List<int> gaps = new List<int>();
            for (int column = 0; column < 2; column++)
                foreach (Panel panel in columns[column])
                {
                    int head = built[panel.Head];
                    // The last panel of a column ends below its lowest row.
                    if (panel.Span == int.MaxValue) panel.Span = Content(panel) + 24;
                    else gaps.Add(panel.Span - Content(panel));
                    foreach (int[] band in column == 1 ? FoldedRight : FoldedLeft)
                    {
                        int from = Math.Max(band[0], head - HeadSlack), to = Math.Min(band[1], head + panel.Span - HeadSlack);
                        if (to > from) panel.Folds.Add(new[] { from - head, to - head });
                    }
                }
            if (gaps.Count > 0)
            {
                gaps.Sort();
                panelGap = gaps[gaps.Count / 2];
            }
            int liftAt = int.MaxValue;
            foreach (Control row in lifting)
            {
                int top;
                Panel panel;
                if (built.TryGetValue(row, out top) && panelOf.TryGetValue(row, out panel) && top < liftAt) { liftAt = top; liftPanel = panel; }
            }
        }

        // Where a panel's rows end, as an offset from its heading.
        private static int Content(Panel panel)
        {
            int end = 0;
            foreach (KeyValuePair<Control, int> row in panel.Offsets) end = Math.Max(end, row.Value + row.Key.Height);
            return end;
        }

        // How much of a panel above this offset Simple hides.
        private static int FoldedAbove(Panel panel, int offset)
        {
            int gone = 0;
            foreach (int[] band in panel.Folds) gone += Math.Max(0, Math.Min(band[1], offset) - band[0]);
            return gone;
        }

        // How tall a panel stands in the view: its span less what Simple folds and the lift take out of it.
        private int PanelSpan(Panel panel, bool simpleView)
        {
            if (simpleView) return panel.Span - FoldedAbove(panel, panel.Span);
            return panel.Span - (panel == liftPanel ? lifted : 0);
        }

        // Where a panel's heading sits in the view: under the panels above it in its column.
        private int PanelTop(Panel panel, bool simpleView)
        {
            int at = columnTop[panel.Column];
            foreach (Panel above in columns[panel.Column])
            {
                if (above == panel) return at;
                at += PanelSpan(above, simpleView);
            }
            return at;
        }

        // Where a row sits in the view, unscaled: its panel's place plus its own place inside it. A row outside every panel
        // (the page's own keys above the first heading) stays where it was built.
        private int StackTop(Control control, bool simpleView)
        {
            Panel panel;
            if (!panelOf.TryGetValue(control, out panel)) return built[control];
            int inside = panel.Offsets[control];
            if (simpleView) inside -= FoldedAbove(panel, inside);
            else if (panel == liftPanel && lifting.Contains(control)) inside -= lifted;
            return PanelTop(panel, simpleView) + inside;
        }

        // Where a row stands in pixels: its panel's top and its own place in the panel scaled apart, so rows built
        // flush stay flush wherever the panel goes.
        private int PlacedTop(Control control, bool simpleView)
        {
            Panel panel;
            if (!panelOf.TryGetValue(control, out panel)) return Scaled(built[control]);
            int inside = panel.Offsets[control];
            if (simpleView) inside -= FoldedAbove(panel, inside);
            else if (panel == liftPanel && lifting.Contains(control)) inside -= lifted;
            return Scaled(PanelTop(panel, simpleView)) + Scaled(inside);
        }

        private bool FoldedInPanel(Panel panel, Control control)
        {
            int offset = panel.Offsets[control];
            foreach (int[] band in panel.Folds) if (offset >= band[0] && offset < band[1]) return true;
            return false;
        }

        // How far the page runs in a view, measured off where everything in it sits.
        private int PageReachOf(bool simpleView)
        {
            int reach = 0;
            foreach (KeyValuePair<Control, int> entry in pageLeft)
            {
                if (!built.ContainsKey(entry.Key) || FoldedIn(entry.Key, simpleView) || dropped.Contains(entry.Key)) continue;
                reach = Math.Max(reach, StackTop(entry.Key, simpleView) + entry.Key.Height + 24);
            }
            return reach;
        }

        // Moves the rows of one panel in [from, to) (offsets from its heading) into another at "at", in the order they sit:
        // rows under them in their own panel close up, and rows from "at" down in the other make room. The heading stays.
        private void Graft(Panel source, int from, int to, Panel target, int at)
        {
            int tall = to - from;
            if (tall <= 0) return;
            List<Control> moving = new List<Control>();
            foreach (KeyValuePair<Control, int> row in source.Offsets) if (row.Key != source.Head && row.Value >= from && row.Value < to) moving.Add(row.Key);
            List<int[]> carried = new List<int[]>(), kept = new List<int[]>();
            foreach (int[] band in source.Folds)
            {
                if (band[0] < from) kept.Add(new[] { band[0], Math.Min(band[1], from) });
                if (band[1] > from && band[0] < to) carried.Add(new[] { Math.Max(band[0], from) - from + at, Math.Min(band[1], to) - from + at });
                if (band[1] > to) kept.Add(new[] { Math.Max(band[0], to) - tall, band[1] - tall });
            }
            source.Folds.Clear();
            source.Folds.AddRange(kept);
            foreach (Control row in new List<Control>(source.Offsets.Keys))
                if (source.Offsets[row] >= to) source.Offsets[row] -= tall;
            foreach (Control row in new List<Control>(target.Offsets.Keys))
                if (target.Offsets[row] >= at && row != target.Head) target.Offsets[row] += tall;
            foreach (int[] band in target.Folds)
                if (band[0] >= at) { band[0] += tall; band[1] += tall; }
            target.Folds.AddRange(carried);
            foreach (Control row in moving)
            {
                int offset = source.Offsets[row] - from + at;
                source.Offsets.Remove(row);
                target.Offsets[row] = offset;
                panelOf[row] = target;
                if (target.Column != source.Column) ShiftColumn(row, target.Column - source.Column);
            }
            source.Span -= tall;
            target.Span += tall;
        }

        // A control's place across the page, as Place reads it, moved over by whole columns.
        private void ShiftColumn(Control control, int by)
        {
            int at = pageLeft.FindIndex(entry => entry.Key == control);
            if (at >= 0) pageLeft[at] = new KeyValuePair<Control, int>(control, pageLeft[at].Value + by * Column);
        }

        // Each column's panels, top to bottom, by their ids; a panel left out of both goes, heading and all. A panel moved
        // to the other column takes its rows with it, and one with a new neighbour below takes the usual gap under it.
        private void SetPanels(string[] left, string[] right)
        {
            List<Panel> all = new List<Panel>();
            foreach (List<Panel> list in columns) all.AddRange(list);
            string[][] wanted = { left, right };
            for (int column = 0; column < 2; column++)
            {
                columns[column].Clear();
                foreach (string id in wanted[column])
                {
                    Panel panel = all.Find(one => one.Id == id);
                    if (panel == null) continue;
                    if (panel.Column != column)
                    {
                        foreach (Control row in panel.Offsets.Keys) ShiftColumn(row, column - panel.Column);
                        panel.Column = column;
                    }
                    columns[column].Add(panel);
                }
            }
            foreach (Panel gone in all)
            {
                if (columns[gone.Column].Contains(gone)) continue;
                // Off the page altogether: nothing places, measures or Tabs to it again.
                gone.Head.Visible = false;
                dropped.Add(gone.Head);
                foreach (Control row in gone.Offsets.Keys) { row.Visible = false; dropped.Add(row); panelOf.Remove(row); }
                pageLeft.RemoveAll(entry => dropped.Contains(entry.Key));
            }
            foreach (List<Panel> list in columns)
                for (int i = 0; i + 1 < list.Count; i++)
                    if (list[i].Before != list[i + 1]) list[i].Span = Content(list[i]) + panelGap;
        }

        // Where a row starts inside its panel: a switch's or a slider's box sits 3 px into its row.
        private int RowAt(Control control) { return panelOf[control].Offsets[control] - 3; }

        // The regroup: SETTINGS is GENERAL SETTINGS and takes HABITS' rows; Mini player over full screen and the two Explore
        // rows go under Quiet in videos in CAPRI, with no gap between the Explore rows; SOUNDS goes under VOICE, KEYS under
        // BRAIN, DOOMSCROLL under CAPRI.
        private void Regroup()
        {
            Panel habits = FindPanel("HABITS"), capri = FindPanel("CAPRI"), general = FindPanel("GENERAL SETTINGS");
            if (habits == null || capri == null || general == null) return;
            int under = RowAt(quietVideos) + Row;
            int away = RowAt(playerFullScreen);
            Graft(habits, away, RowAt(exploreAway) + Row, capri, under);
            Graft(habits, RowAt(exploreAfter), RowAt(exploreAfter) + Row, capri, under + Row * 2);
            // The rest of HABITS, from the keys on its heading's row down, under Custom colours.
            int first = int.MaxValue, last = 0;
            foreach (KeyValuePair<Control, int> row in habits.Offsets)
            {
                if (row.Key == habits.Head) continue;
                first = Math.Min(first, row.Value);
                last = Math.Max(last, row.Value + row.Key.Height);
            }
            if (first < last) Graft(habits, first, last + 6, general, RowAt(customColours) + Row);
            // The three keys that sat on HABITS' heading share their own row now, across the column.
            if (panelOf.ContainsKey(vaultKey) && panelOf.ContainsKey(memoriesKey) && panelOf.ContainsKey(gifsKey))
            {
                vaultKey.Width = 112;
                memoriesKey.Width = 136;
                gifsKey.Width = 72;
                SetLeft(vaultKey, 28 + Column);
                SetLeft(memoriesKey, 144 + Column);
                SetLeft(gifsKey, 284 + Column);
            }
            SetPanels(StockLeft, StockRight);
            foreach (List<Panel> list in columns) foreach (Panel panel in list) panel.Before = null;
            for (int column = 0; column < 2; column++)
                for (int i = 0; i + 1 < columns[column].Count; i++) columns[column][i].Before = columns[column][i + 1];
            SavedOrder();
        }

        private static readonly string[] StockLeft = { "YOU", "VOICE", "SOUNDS", "CLIPS", "MOVES", "GAMES", "WEATHER" };
        private static readonly string[] StockRight = { "GENERAL SETTINGS", "CAPRI", "DOOMSCROLL", "WATCH", "BRAIN", "KEYS" };

        // Her panels in the order saved in her settings (settings.json carries it), mods' panels left for when they come.
        // A panel of hers the saved order doesn't name keeps its stock place, so one a new version adds still shows.
        private void SavedOrder()
        {
            string saved = capri.PanelsSaved;
            if (string.IsNullOrEmpty(saved)) return;
            string[] halves = saved.Split('|');
            if (halves.Length != 2) return;
            List<string>[] wanted = { new List<string>(), new List<string>() };
            for (int column = 0; column < 2; column++)
                foreach (string key in halves[column].Split(';'))
                    if (key.Length > 0 && !key.StartsWith("mod:", StringComparison.Ordinal) && FindPanel(key) != null && !wanted[0].Contains(key) && !wanted[1].Contains(key)) wanted[column].Add(key);
            string[][] stock = { StockLeft, StockRight };
            for (int column = 0; column < 2; column++)
                for (int i = 0; i < stock[column].Length; i++)
                {
                    string key = stock[column][i];
                    if (wanted[0].Contains(key) || wanted[1].Contains(key)) continue;
                    wanted[column].Insert(Math.Min(i, wanted[column].Count), key);
                }
            SetPanels(wanted[0].ToArray(), wanted[1].ToArray());
        }

        // The order as it is now, saved; nothing saved while it is the stock order with no mod in it.
        private void SaveOrder()
        {
            List<string> left = new List<string>(), right = new List<string>();
            foreach (Panel panel in columns[0]) left.Add(panel.Key);
            foreach (Panel panel in columns[1]) right.Add(panel.Key);
            string now = string.Join(";", left.ToArray()) + "|" + string.Join(";", right.ToArray());
            string stock = string.Join(";", StockLeft) + "|" + string.Join(";", StockRight);
            capri.SavePanels(now == stock ? null : now);
        }

        // A code mod's own panel (ICapriMainframe.AddPanel): title and control at the slot it asks for (1 is the top),
        // or after an earlier mod's. A saved order wins, so a reorder survives a restart; Simple Settings shows it only
        // when asked.
        internal void AddModPanel(string mod, string title, int column, int slot, Control made, bool simpleToo)
        {
            if (made == null || string.IsNullOrEmpty(mod)) return;
            column = column == 0 ? 0 : 1;
            string id = (string.IsNullOrWhiteSpace(title) ? mod : title.Trim()).ToUpperInvariant();
            RemoveModPanel(mod, id);
            int x = column * Column;
            int laid = layout.Count;
            Head head = Section(id + " >", x, 0);
            made.Bounds = new System.Drawing.Rectangle(28 + x, 26, 328, Math.Max(22, made.Height));
            Controls.Add(made);
            Panel panel = new Panel { Id = id, Head = head, Built = column, Column = column, Mod = mod, Slot = Math.Max(1, slot) };
            panel.Offsets[head] = 0;
            panel.Offsets[made] = 26;
            panel.Span = 26 + made.Height + panelGap;
            if (!simpleToo) panel.Folds.Add(new[] { 0, panel.Span });
            WireDrag(head);
            foreach (Control control in new Control[] { head, made })
            {
                built[control] = 0;
                pageLeft.Add(new KeyValuePair<Control, int>(control, control.Left));
                panelOf[control] = panel;
                Scrolls(control);
                // Its place at the top of the page, so the scroll can move it before the next Restack places it.
                tops[control] = control.Top;
                RememberLayout(control);
            }
            for (int i = laid; i < layout.Count; i++) layout[i].Apply(unit, layoutFallback);
            List<Panel> list = columns[column];
            int at = -1;
            string saved = capri.PanelsSaved;
            if (!string.IsNullOrEmpty(saved))
            {
                // After the panel the saved order has before it in this column, when that one is here.
                string[] halves = saved.Split('|');
                List<string> keys = halves.Length == 2 ? new List<string>(halves[column].Split(';')) : new List<string>();
                int mine = keys.IndexOf(panel.Key);
                for (int before = mine - 1; mine >= 0 && before >= -1 && at < 0; before--)
                {
                    if (before < 0) { at = 0; break; }
                    int found = list.FindIndex(one => one.Key == keys[before]);
                    if (found >= 0) at = found + 1;
                }
            }
            if (at < 0)
            {
                at = Math.Min(panel.Slot - 1, list.Count);
                while (at < list.Count && list[at].Mod != null && list[at].Slot == panel.Slot) at++;
            }
            list.Insert(at, panel);
            Restack();
        }

        // A mod's panels go when it stops or is taken out, and the slots they held go back to the panels under them.
        internal void RemoveModPanels(string mod) { RemoveModPanel(mod, null); }

        private void RemoveModPanel(string mod, string id)
        {
            bool gone = false;
            foreach (List<Panel> list in columns)
                foreach (Panel panel in list.FindAll(one => one.Mod == mod && (id == null || one.Id == id)))
                {
                    list.Remove(panel);
                    foreach (Control control in new List<Control>(panel.Offsets.Keys))
                    {
                        panelOf.Remove(control);
                        built.Remove(control);
                        pageLeft.RemoveAll(entry => entry.Key == control);
                        scrolled.Remove(control);
                        tops.Remove(control);
                        layout.RemoveAll(item => item.Control == control || control.Contains(item.Control));
                        Controls.Remove(control);
                        control.Dispose();
                    }
                    gone = true;
                }
            if (gone) Restack();
        }

        // ---------- The lock, and dragging a panel by its heading ----------

        private bool panelsUnlocked;
        private Panel dragged;
        private Label dropMark;
        private int dropColumn, dropIndex;

        internal bool PanelsUnlocked { get { return panelsUnlocked; } }

        // The lock beside minimise. Locked, as she starts, nothing moves; open, every panel's heading can be dragged to
        // another place in either column, and where it lands is saved.
        internal void TogglePanelLock()
        {
            panelsUnlocked = !panelsUnlocked;
            lockKey.Glyph = panelsUnlocked ? 4 : 3;
            lockKey.AccessibleName = panelsUnlocked ? "Panels unlocked. Drag a panel by its heading, then close the lock" : "Panels locked. Open the lock to drag Settings' panels to other places";
            lockKey.Invalidate();
            foreach (List<Panel> list in columns)
                foreach (Panel panel in list)
                {
                    panel.Head.Draggable = panelsUnlocked;
                    panel.Head.Cursor = panelsUnlocked ? Cursors.SizeAll : Cursors.Default;
                }
            if (!panelsUnlocked) EndDrag();
        }

        private void WireDrag(Head head)
        {
            head.Draggable = panelsUnlocked;
            head.Cursor = panelsUnlocked ? Cursors.SizeAll : Cursors.Default;
            head.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                Panel panel;
                if (e.Button == MouseButtons.Left && panelOf.TryGetValue(head, out panel)) StartDrag(panel);
            };
            head.MouseMove += delegate(object sender, MouseEventArgs e) { if (dragged != null) DragTo(PointToClient(head.PointToScreen(e.Location))); };
            head.MouseUp += delegate { if (dragged != null) DropPanel(); };
        }

        private void StartDrag(Panel panel)
        {
            if (!panelsUnlocked || !paged || panel == null) return;
            dragged = panel;
            dropColumn = panel.Column;
            dropIndex = columns[panel.Column].IndexOf(panel);
        }

        // Where the panel would land for the pointer here, in her window's own px: the column it is over, and before the
        // first panel whose middle is under it. A thin line in her accent colour shows the place; near the top or bottom
        // the page scrolls.
        private void DragTo(Point client)
        {
            if (dragged == null) return;
            if (client.Y < Scaled(TitleTall + 24)) ScrollPage(Math.Max(0, pageScroll - ScrollRow / 2));
            else if (client.Y > ClientSize.Height - Scaled(24)) ScrollPage(pageScroll + ScrollRow / 2);
            int column = client.X < Scaled(Column) ? 0 : 1;
            double y = client.Y / Unit() + pageScroll;
            List<Panel> list = columns[column];
            int index = list.Count, mark = columnTop[column];
            for (int i = 0; i < list.Count; i++)
            {
                Panel one = list[i];
                int top = PanelTop(one, simple), span = PanelSpan(one, simple);
                if (span <= 0) continue;
                if (y < top + span / 2.0) { index = i; mark = top - 12; break; }
                mark = top + span - 12;
            }
            dropColumn = column;
            dropIndex = index;
            if (dropMark == null)
            {
                dropMark = new Label { AutoSize = false, Visible = false };
                Controls.Add(dropMark);
            }
            dropMark.BackColor = Cyan;
            dropMark.Bounds = new Rectangle(Scaled(28 + column * Column), Scaled(mark - pageScroll), Scaled(328), Math.Max(2, Scaled(2)));
            dropMark.Visible = true;
            dropMark.BringToFront();
        }

        // The panel moves to where the line was; its old neighbours close up and its new ones make room, and the order is
        // saved with her settings.
        private void DropPanel()
        {
            Panel panel = dragged;
            EndDrag();
            if (panel == null) return;
            MovePanel(panel, dropColumn, dropIndex);
        }

        private void EndDrag()
        {
            dragged = null;
            if (dropMark != null) dropMark.Visible = false;
        }

        private void MovePanel(Panel panel, int column, int index)
        {
            List<Panel> from = columns[panel.Column], to = columns[column];
            int was = from.IndexOf(panel);
            if (from == to && (index == was || index == was + 1)) return;
            if (from == to && index > was) index--;
            from.Remove(panel);
            if (panel.Column != column)
            {
                foreach (Control row in panel.Offsets.Keys) ShiftColumn(row, column - panel.Column);
                panel.Column = column;
            }
            to.Insert(Math.Max(0, Math.Min(index, to.Count)), panel);
            Neighbours();
            Restack();
        }

        // A panel with a new neighbour under it takes the usual gap, as a regroup gives it.
        private void Neighbours()
        {
            foreach (List<Panel> list in columns)
                for (int i = 0; i < list.Count; i++)
                {
                    Panel next = i + 1 < list.Count ? list[i + 1] : null;
                    if (list[i].Before == next) continue;
                    list[i].Span = Content(list[i]) + (next == null ? 24 : panelGap);
                    list[i].Before = next;
                }
        }

        internal void CheckPanelDrag(List<string> checks)
        {
            string savedWas = capri.PanelsSaved;
            bool pagedWas = paged;
            try
            {
                ShowSettings(true);
                ScrollPage(0);
                if (panelsUnlocked) TogglePanelLock();
                if (!lockKey.Visible || lockKey.Left < modsKey.Right || lockKey.Right > minimize.Left || lockKey.Top != minimize.Top || lockKey.Glyph != 3)
                    throw new Exception("The lock isn't in its box beside minimise, shut, on the Settings page: " + lockKey.Bounds + ", Mods " + modsKey.Bounds + ", minimise " + minimize.Bounds + ", glyph " + lockKey.Glyph);
                Panel keys = FindPanel("KEYS"), games = FindPanel("GAMES");
                string before = PanelOrder;
                StartDrag(keys);
                if (dragged != null) throw new Exception("A panel could be dragged while the lock was shut");
                TogglePanelLock();
                if (lockKey.Glyph != 4 || !keys.Head.Draggable) throw new Exception("The lock didn't open, or the headings can't be dragged with it open");
                StartDrag(keys);
                ScrollPage(Math.Max(0, PanelTop(games, simple) - 100));
                DragTo(new Point(Scaled(120), Scaled(PanelTop(games, simple) - pageScroll) + 2));
                bool marked = dropMark != null && dropMark.Visible && dropMark.Left < Scaled(Column);
                DropPanel();
                string after = PanelOrder, saved = capri.PanelsSaved ?? "";
                if (!marked || after != "YOU VOICE SOUNDS CLIPS MOVES KEYS GAMES WEATHER | GENERAL SETTINGS CAPRI DOOMSCROLL WATCH BRAIN" || !saved.Contains("MOVES;KEYS;GAMES"))
                    throw new Exception("Dragging KEYS onto GAMES didn't put it there and save it: marked " + marked + ", " + after + ", saved " + saved);
                string sound = PanelsSound();
                if (sound != null) throw new Exception("After the drag: " + sound);
                TogglePanelLock();
                if (keys.Head.Draggable || lockKey.Glyph != 3) throw new Exception("Shutting the lock left the headings draggable");
                checks.Add("PASS the lock sits in a box beside minimise on the Settings page, shut to start; shut, no panel moves; open, KEYS dragged onto GAMES lands above it in the left column with a line marking the place, every row moves with it, and the order is saved");
            }
            finally
            {
                if (panelsUnlocked) TogglePanelLock();
                SetPanels(StockLeft, StockRight);
                Neighbours();
                capri.SavePanels(savedWas);
                Restack();
                capri.SavePanels(savedWas);
                ShowSettings(pagedWas);
            }
        }

        // After a panel came or went: the page's heights, every row's place, the columns' order saved.
        private void Restack()
        {
            // Rows are placed where they stand at the top of the page, so the page goes to its top first and back
            // after; placed while scrolled, the next wheel turn would move them wrongly.
            int back = ShownScroll;
            ScrollPage(0);
            pageTall = PageReachOf(false);
            simpleTall = PageReachOf(true);
            Place();
            CloseGaps();
            foreach (KeyValuePair<Control, int> entry in pageLeft) Reachable(entry.Key, paged && !Folded(entry.Key));
            if (paged) Arrange(expanded);
            // Nothing kept from before the move outlives it: each row's place at the top of the page, which the scroll
            // moves rows from, is taken again from where it stands now, and the page's picture for sliding is thrown
            // away and drawn again. The rows themselves stay made.
            foreach (Control control in scrolled) tops[control] = control.Top;
            DropPicture();
            if (paged && Visible) pictureTimer.Start();
            ScrollPage(back);
            SaveOrder();
        }

        private void SetLeft(Control control, int left)
        {
            int at = pageLeft.FindIndex(entry => entry.Key == control);
            if (at >= 0) pageLeft[at] = new KeyValuePair<Control, int>(control, left);
        }

        internal void CheckModPanels(List<string> checks)
        {
            string savedWas = capri.PanelsSaved;
            int capriAt = PanelTop(FindPanel("CAPRI"), false), capriSimple = PanelTop(FindPanel("CAPRI"), true);
            try
            {
                capri.SavePanels(null);
                AddModPanel("check-a", "Alpha", 1, 2, new Label { Text = "Alpha's row", Height = 60 }, false);
                AddModPanel("check-b", "Beta", 1, 2, new Label { Text = "Beta's row", Height = 40 }, true);
                string order = PanelOrder;
                Panel alpha = FindPanel("ALPHA"), beta = FindPanel("BETA"), capriPanel = FindPanel("CAPRI");
                if (!order.EndsWith("| GENERAL SETTINGS ALPHA BETA CAPRI DOOMSCROLL WATCH BRAIN KEYS", StringComparison.Ordinal))
                    throw new Exception("Two mods asking for slot 2 didn't take it in load order, pushing CAPRI down: " + order);
                if (PanelTop(capriPanel, false) != capriAt + alpha.Span + beta.Span || PanelTop(capriPanel, true) != capriSimple + beta.Span)
                    throw new Exception("CAPRI didn't move down by the mods' panels: " + PanelTop(capriPanel, false) + " for " + capriAt + " + " + alpha.Span + " + " + beta.Span + ", Simple " + PanelTop(capriPanel, true) + " for " + capriSimple + " + " + beta.Span);
                if (!FoldedIn(alpha.Head, true) || FoldedIn(beta.Head, true)) throw new Exception("A mod's panel showed in Simple Settings without asking, or didn't when it asked");
                string saved = capri.PanelsSaved ?? "";
                if (!saved.Contains("GENERAL SETTINGS;mod:check-a:ALPHA;mod:check-b:BETA;CAPRI")) throw new Exception("The panel order wasn't saved with the mods' panels in it: " + saved);
                RemoveModPanels("check-a");
                if (!PanelOrder.EndsWith("| GENERAL SETTINGS BETA CAPRI DOOMSCROLL WATCH BRAIN KEYS", StringComparison.Ordinal) || (capri.PanelsSaved ?? "").Contains("check-a"))
                    throw new Exception("A mod taken out didn't give its slot back: " + PanelOrder + ", saved " + capri.PanelsSaved);
                // A saved place beats the slot asked for: Beta saved under WATCH comes back there.
                RemoveModPanels("check-b");
                capri.SavePanels("YOU;VOICE;SOUNDS;CLIPS;MOVES;GAMES;WEATHER|GENERAL SETTINGS;CAPRI;DOOMSCROLL;WATCH;mod:check-b:BETA;BRAIN;KEYS");
                AddModPanel("check-b", "Beta", 1, 2, new Label { Text = "Beta's row", Height = 40 }, true);
                if (!PanelOrder.EndsWith("| GENERAL SETTINGS CAPRI DOOMSCROLL WATCH BETA BRAIN KEYS", StringComparison.Ordinal))
                    throw new Exception("A mod's panel didn't come back to its saved place: " + PanelOrder);
                RemoveModPanels("check-b");
                if (PanelOrder != PanelsShipped || PanelTop(FindPanel("CAPRI"), false) != capriAt) throw new Exception("The page didn't go back once the mods' panels went: " + PanelOrder);
            }
            finally
            {
                RemoveModPanels("check-a");
                RemoveModPanels("check-b");
                capri.SavePanels(savedWas);
            }
            checks.Add("PASS a mod's panel (AddPanel) takes the slot it asks for and pushes the panels under it down; two asking for one slot take it in load order; it shows in Simple only when asked; the order is saved with it, a saved place beats the slot asked for, and a mod taken out gives its slot back");
        }

        // Her title bar stays at the top while Settings scrolls under it, at every size; a row Tab reaches is shown under it.
        internal void CheckTitleFixed(List<string> checks)
        {
            bool pagedWas = paged;
            int sizeWas = capri.MainframeSize;
            try
            {
                foreach (int size in new[] { 100, 150, 200 })
                {
                    SizeMainframe(size);
                    ShowSettings(true);
                    ScrollPage(0);
                    Dictionary<Control, int> tops = new Dictionary<Control, int>();
                    foreach (Control control in titleRow) tops[control] = control.Top;
                    if (pageView >= PageNow) continue;
                    ScrollPage(PageNow - pageView);
                    foreach (Control control in titleRow)
                        if (control.Top != tops[control]) throw new Exception(Name(control) + " moved with the page at " + size + "%: " + tops[control] + " to " + control.Top);
                    if (!titleBand.Visible || titleBand.Top != 0 || titleBand.Height != Scaled(TitleTall) || titleBand.Width != ClientSize.Width || (pageScroll > 0) != titleBand.Visible)
                        throw new Exception("Her title band isn't across the top at " + size + "%: " + titleBand.Bounds + " in " + ClientSize);
                    foreach (Control control in titleRow)
                        if (control.Visible && (control.Bottom > titleBand.Bottom || Controls.GetChildIndex(control) > Controls.GetChildIndex(titleBand)))
                            throw new Exception(Name(control) + " isn't on her title band at " + size + "%: " + control.Bounds + ", band " + titleBand.Bounds);
                    Control under = null;
                    foreach (KeyValuePair<Control, int> entry in pageLeft)
                        if (scrolled.Contains(entry.Key) && entry.Key.Visible && entry.Key.Top < titleBand.Bottom && entry.Key.Bottom > 0 && !Folded(entry.Key)) { under = entry.Key; break; }
                    if (under != null && Controls.GetChildIndex(under) < Controls.GetChildIndex(titleBand))
                        throw new Exception(Name(under) + " scrolled over her title band at " + size + "%");
                    // Tab back to the first row of YOU shows it under the title bar, not behind it.
                    Panel you = FindPanel("YOU");
                    Control first = null;
                    foreach (Control row in you.Offsets.Keys) if (Steerable(row) && row.Visible && (first == null || row.Top < first.Top)) first = row;
                    if (first != null)
                    {
                        Reveal(first);
                        if (first.Top < titleBand.Bottom) throw new Exception(Name(first) + " was shown under her title bar at " + size + "%: " + first.Top + " above " + titleBand.Bottom);
                    }
                }
                checks.Add("PASS her title bar stays at the top while Settings scrolls under it at 100, 150 and 200%: MAINFRAME, Settings, Advanced Settings, Mods, the search, the lock, minimise and close keep their place on a band of her own over the page, and a row brought into view sits under it");
                CheckBandWhileSliding(checks);
            }
            finally
            {
                SizeMainframe(sizeWas);
                ScrollPage(0);
                ShowSettings(pagedWas);
            }
        }

        internal const string PanelsShipped = "YOU VOICE SOUNDS CLIPS MOVES GAMES WEATHER | GENERAL SETTINGS CAPRI DOOMSCROLL WATCH BRAIN KEYS";

        internal void CheckPanels(List<string> checks)
        {
            if (PanelOrder != PanelsShipped) throw new Exception("Settings' panels aren't in the order it ships: " + PanelOrder);
            string sound = PanelsSound();
            if (sound != null) throw new Exception(sound);
            if (panelOf[playerFullScreen] != panelOf[quietVideos] || StackTop(playerFullScreen, false) != StackTop(quietVideos, false) + Row
                || StackTop(exploreAway, false) != StackTop(playerFullScreen, false) + Row || StackTop(exploreAfter, false) != StackTop(exploreAway, false) + Row)
                throw new Exception("Mini player over full screen and the Explore rows aren't the rows under Quiet in videos: " + StackTop(quietVideos, false) + ", " + StackTop(playerFullScreen, false) + ", " + StackTop(exploreAway, false) + ", " + StackTop(exploreAfter, false));
            foreach (Control row in new Control[] { memoriesKey, startup, discord, cursor, remembering, security, powerSaver, musicApp, perchFps, keepAwake, hideFullScreen })
                if (!panelOf.ContainsKey(row) || panelOf[row].Id != "GENERAL SETTINGS") throw new Exception(Name(row) + " isn't under GENERAL SETTINGS");
            if (FindPanel("HABITS") != null || settingsHead.Text != "GENERAL SETTINGS >") throw new Exception("HABITS is still a panel, or SETTINGS isn't GENERAL SETTINGS: " + settingsHead.Text);
            checks.Add("PASS Settings' panels stack " + PanelsShipped + " in Advanced and Simple, every row inside its own panel and column; Mini player over full screen, Explore while I'm away and Explore after are the rows under Quiet in videos with no gap; HABITS' other rows are under GENERAL SETTINGS");
            CheckPanelOrders(checks);
        }

        // Panels moved with the page scrolled, as a drag is: WATCH under WEATHER from a saved order, WATCH at every
        // left place, the left column rotated and reversed; rows stay in their panels with no overlap.
        internal void CheckPanelOrders(List<string> checks)
        {
            string savedWas = capri.PanelsSaved;
            bool pagedWas = paged;
            List<string[][]> orders = new List<string[][]>();
            List<string> right = new List<string>(StockRight);
            right.Remove("WATCH");
            for (int at = 0; at <= StockLeft.Length; at++)
            {
                List<string> left = new List<string>(StockLeft);
                left.Insert(at, "WATCH");
                orders.Add(new[] { left.ToArray(), right.ToArray() });
            }
            for (int turn = 1; turn < StockLeft.Length; turn++)
            {
                List<string> left = new List<string>();
                for (int i = 0; i < StockLeft.Length; i++) left.Add(StockLeft[(i + turn) % StockLeft.Length]);
                orders.Add(new[] { left.ToArray(), StockRight });
            }
            string[] backwards = (string[])StockLeft.Clone();
            Array.Reverse(backwards);
            orders.Add(new[] { backwards, StockRight });
            try
            {
                ShowSettings(true);
                foreach (bool view in new[] { false, true })
                {
                    simple = view;
                    foreach (string[][] order in orders)
                    {
                        SetPanels(StockLeft, StockRight);
                        Neighbours();
                        Restack();
                        ScrollPage(Math.Max(0, PanelTop(FindPanel("WEATHER"), simple) - 200));
                        int before = pageScroll;
                        if (order[0].Length == StockLeft.Length + 1 && order[0][order[0].Length - 1] == "WATCH")
                            MovePanel(FindPanel("WATCH"), 0, columns[0].Count);
                        else
                        {
                            SetPanels(order[0], order[1]);
                            Neighbours();
                            Restack();
                        }
                        string named = string.Join(" ", order[0]) + (simple ? " in Simple" : "");
                        string problem = PanelsSound() ?? Overlap() ?? RowsUnderScroll();
                        if (problem == null && pageScroll != Math.Min(before, Math.Max(0, PageNow - pageView))) problem = "the page jumped from " + before + " to " + pageScroll;
                        if (problem != null) throw new Exception("Moving panels while scrolled broke the page, " + named + ": " + problem);
                        MovePage(pageScroll + 100);
                        problem = PanelsSound() ?? Overlap() ?? RowsUnderScroll();
                        if (problem != null) throw new Exception("After a turn of the wheel, " + named + ": " + problem);
                    }
                }
            }
            finally
            {
                simple = !capri.AdvancedSettings;
                SetPanels(StockLeft, StockRight);
                Neighbours();
                capri.SavePanels(savedWas);
                Restack();
                capri.SavePanels(savedWas);
                ShowSettings(pagedWas);
            }
            checks.Add("PASS panels moved while Settings is scrolled (WATCH dropped under WEATHER, WATCH at each place in the left column, the left column turned round and backwards, Advanced and Simple) keep the page where it was, every row in its own panel and where its panel puts it, nothing overlapping, before and after a turn of the wheel");
        }

        // ---------- The panel sweep ----------

        // Every panel dropped into every place in both columns at one size, at the page's top, middle, bottom or after
        // a wheel turn: rows stay in their panels, nothing overlaps, the title band stays in front, and at 125% Auto
        // picture and restart match.
        internal void CheckPanelMoves(List<string> checks, string output, int text, int size)
        {
            string folder = System.IO.Path.Combine(output, "panelsweep");
            System.IO.Directory.CreateDirectory(folder);
            string savedWas = capri.PanelsSaved;
            bool pagedWas = paged, topWas = TopMost;
            int sizeWas = capri.MainframeSize;
            FormWindowState stateWas = WindowState;
            List<string> failed = new List<string>();
            List<System.Drawing.Bitmap> sheet = new List<System.Drawing.Bitmap>();
            int moves = 0, compared = 0, restarted = 0;
            bool owners = text == 125;
            string sizeName = "text " + text + "%, size " + (size == 0 ? "Auto" : size + "%");
            try
            {
                if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
                if (!Visible) Show();
                Location = Displays.Home.WorkingArea.Location;
                TopMost = true;
                if (panelsUnlocked) TogglePanelLock();
                {
                    LayoutScale(96, text, false);
                    SizeMainframe(size);
                    ShowSettings(true);
                    string[] ids = new string[StockLeft.Length + StockRight.Length];
                    StockLeft.CopyTo(ids, 0);
                    StockRight.CopyTo(ids, StockLeft.Length);
                    foreach (string id in ids)
                        for (int column = 0; column < 2; column++)
                        {
                            int places = (column == 0 ? StockLeft.Length : StockRight.Length) + 1;
                            for (int at = 0; at < places; at++)
                            {
                                if (failed.Count >= 12) break;
                                SetPanels(StockLeft, StockRight);
                                Neighbours();
                                int state = moves % 4;
                                int bottom = Math.Max(0, PageNow - pageView);
                                ScrollPage(state == 0 ? 0 : state == 2 ? bottom : bottom / 2);
                                if (state == 3) Wheel(-2, false);
                                Panel panel = FindPanel(id);
                                int was = columns[panel.Column].IndexOf(panel);
                                // A drop onto its own place moves nothing, so the page is laid out as anyone would see it.
                                if (panel.Column == column && (at == was || at == was + 1)) Restack();
                                string named = sizeName + ", " + id + " to column " + (column + 1) + " place " + (at + 1) + ", " + (state == 0 ? "top" : state == 1 ? "mid-page" : state == 2 ? "bottom" : "while moving");
                                MovePanel(panel, column, at);
                                Settle();
                                moves++;
                                bool deep = owners || moves % 3 == 0;
                                string problem = PanelsSound() ?? OverlapNamed() ?? RowsUnderScroll() ?? BandProblem();
                                if (problem == null && deep)
                                {
                                    compared++;
                                    problem = PictureProblem();
                                }
                                if (problem == null && deep)
                                {
                                    restarted++;
                                    problem = RestartProblem();
                                }
                                if (problem != null)
                                {
                                    failed.Add(named + ": " + problem.Replace("\r", " ").Replace("\n", " "));
                                    Shot(System.IO.Path.Combine(folder, "fail-" + text + "-" + size + "-" + failed.Count.ToString("00") + ".png"));
                                }
                                else if (moves % 40 == 1 && sheet.Count < 24) sheet.Add(Thumb(named));
                            }
                        }
                }
                // The lock shut: a drag moves nothing.
                if (owners)
                {
                SetPanels(StockLeft, StockRight);
                Neighbours();
                Restack();
                string before = PanelOrder;
                StartDrag(FindPanel("WATCH"));
                if (dragged != null) { DragTo(new System.Drawing.Point(Scaled(120), Scaled(200))); DropPanel(); }
                if (PanelOrder != before) failed.Add("with the lock shut a drag moved WATCH: " + PanelOrder);
                // A mod's panel added and taken out while the page is scrolled.
                foreach (int column in new[] { 0, 1 })
                {
                    ScrollPage(Math.Max(0, PageNow - pageView) / 2);
                    AddModPanel("sweep-check", "Sweep", column, 3, new Label { Text = "Sweep's row", Height = 40 }, true);
                    string added = PanelsSound() ?? Overlap() ?? RowsUnderScroll() ?? BandProblem();
                    RemoveModPanels("sweep-check");
                    string gone = PanelsSound() ?? Overlap() ?? RowsUnderScroll() ?? BandProblem();
                    if (added != null || gone != null) { failed.Add("a mod panel added or taken out in column " + (column + 1) + " while scrolled: " + (added ?? gone)); Shot(System.IO.Path.Combine(folder, "fail-mod-" + column + ".png")); }
                }
                }
            }
            finally
            {
                RemoveModPanels("sweep-check");
                if (panelsUnlocked) TogglePanelLock();
                SetPanels(StockLeft, StockRight);
                Neighbours();
                capri.SavePanels(savedWas);
                Restack();
                capri.SavePanels(savedWas);
                LayoutScale(0, 0);
                SizeMainframe(sizeWas);
                ShowSettings(pagedWas);
                TopMost = topWas;
                if (WindowState != stateWas) WindowState = stateWas;
                Sheet(sheet, System.IO.Path.Combine(folder, "contact-sheet-" + text + "-" + size + ".png"));
            }
            if (failed.Count > 0) throw new Exception(failed.Count + " of " + moves + " panel moves broke the page (pictures in " + folder + "): " + string.Join(" | ", failed.ToArray()));
            checks.Add("PASS the panel sweep at " + sizeName + ": " + moves + " drops of every panel into every place in both columns, made at the top, mid-page, the bottom and while the page moved, each leave every row in its panel and in place, nothing overlapping, the title band in front; " + compared + " compared the page's picture with the page and " + restarted + " came back the same after a restart from the saved order" + (owners ? "; with the lock shut a drag does nothing; a mod's panel comes and goes while scrolled" : ""));
        }

        // Overlap, naming both controls and where they stand.
        private string OverlapNamed()
        {
            if (Overlap() == null) return null;
            for (int i = 0; i < Controls.Count; i++)
                for (int j = i + 1; j < Controls.Count; j++)
                {
                    Control a = Controls[i], b = Controls[j];
                    if (Hidden(a) || Hidden(b) || !a.Visible || !b.Visible || (a is Label && a.Text.Length == 0) || (b is Label && b.Text.Length == 0)) continue;
                    if (titleRow.Contains(a) != titleRow.Contains(b) && (scrolled.Contains(a) || scrolled.Contains(b)) && Math.Min(a.Top, b.Top) < titleBand.Bottom && titleBand.Visible) continue;
                    if (!a.Bounds.IntersectsWith(b.Bounds)) continue;
                    System.Drawing.Rectangle both = System.Drawing.Rectangle.Intersect(a.Bounds, b.Bounds);
                    return "overlap: " + Name(a) + " (" + a.GetType().Name + " " + a.Bounds + ", in " + (panelOf.ContainsKey(a) ? panelOf[a].Id : "no panel") + ") / " + Name(b) + " (" + b.GetType().Name + " " + b.Bounds + ", in " + (panelOf.ContainsKey(b) ? panelOf[b].Id : "no panel") + "), " + both.Width + "x" + both.Height + " px";
                }
            return Overlap();
        }

        // The title band, once the page has scrolled, in front of every row that runs under it.
        private string BandProblem()
        {
            if (!paged || pageScroll == 0) return titleBand.Visible ? "the title band shows with the page at its top" : null;
            if (!titleBand.Visible) return "the page is scrolled and the title band is hidden";
            int band = Controls.GetChildIndex(titleBand);
            foreach (Control row in scrolled)
                if (row.Visible && row.Parent == this && row.Bounds.IntersectsWith(titleBand.Bounds) && Controls.GetChildIndex(row) < band)
                    return Name(row) + " is drawn over the title band";
            return null;
        }

        // The page's picture for sliding against the page as it stands.
        private string PictureProblem()
        {
            FinishPicture();
            if (pagePicture == null) return "no picture was made";
            using (System.Drawing.Bitmap real = RealPage())
            {
                string differs = PictureDiffers(real, Scaled(pageScroll));
                return differs == null ? null : "the page's picture differs from the page: " + differs;
            }
        }

        // The order saved, laid out again from stock as a restart does, comes back the same.
        private string RestartProblem()
        {
            string order = PanelOrder;
            Dictionary<string, int> placed = new Dictionary<string, int>();
            foreach (List<Panel> list in columns) foreach (Panel one in list) placed[one.Id] = PanelTop(one, simple);
            int scroll = pageScroll;
            SetPanels(StockLeft, StockRight);
            Neighbours();
            SavedOrder();
            Neighbours();
            Restack();
            ScrollPage(scroll);
            if (PanelOrder != order) return "after a restart the order was " + PanelOrder + " for " + order;
            foreach (List<Panel> list in columns)
                foreach (Panel one in list)
                    if (placed.ContainsKey(one.Id) && placed[one.Id] != PanelTop(one, simple)) return "after a restart " + one.Id + " stands at " + PanelTop(one, simple) + " for " + placed[one.Id];
            return RowsUnderScroll();
        }

        private void Shot(string path)
        {
            try
            {
                Settle();
                Update();
                System.Drawing.Rectangle at = RectangleToScreen(ClientRectangle);
                using (System.Drawing.Bitmap shot = new System.Drawing.Bitmap(at.Width, at.Height))
                {
                    using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(shot)) g.CopyFromScreen(at.Location, System.Drawing.Point.Empty, at.Size);
                    shot.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        private System.Drawing.Bitmap Thumb(string named)
        {
            Settle();
            Update();
            System.Drawing.Rectangle at = RectangleToScreen(ClientRectangle);
            System.Drawing.Bitmap small = new System.Drawing.Bitmap(Math.Max(1, at.Width / 3), Math.Max(1, at.Height / 3) + 14);
            using (System.Drawing.Bitmap full = new System.Drawing.Bitmap(at.Width, at.Height))
            {
                using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(full)) g.CopyFromScreen(at.Location, System.Drawing.Point.Empty, at.Size);
                using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(small))
                {
                    g.Clear(System.Drawing.Color.Black);
                    g.DrawImage(full, new System.Drawing.Rectangle(0, 14, small.Width, small.Height - 14));
                    using (System.Drawing.Font font = new System.Drawing.Font("Segoe UI", 7f)) g.DrawString(named, font, System.Drawing.Brushes.White, 1, 1);
                }
            }
            return small;
        }

        private static void Sheet(List<System.Drawing.Bitmap> pictures, string path)
        {
            if (pictures.Count == 0) return;
            try
            {
                int across = 6, wide = 0, tall = 0;
                foreach (System.Drawing.Bitmap one in pictures) { wide = Math.Max(wide, one.Width); tall = Math.Max(tall, one.Height); }
                int rows = (pictures.Count + across - 1) / across;
                using (System.Drawing.Bitmap sheet = new System.Drawing.Bitmap(wide * Math.Min(across, pictures.Count), tall * rows))
                {
                    using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(sheet))
                    {
                        g.Clear(System.Drawing.Color.Black);
                        for (int i = 0; i < pictures.Count; i++) g.DrawImage(pictures[i], (i % across) * wide, (i / across) * tall);
                    }
                    sheet.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            finally { foreach (System.Drawing.Bitmap one in pictures) one.Dispose(); }
        }

        // A row standing somewhere else than its panel puts it under the page's scroll, or null.
        private string RowsUnderScroll()
        {
            int shift = Scaled(pageScroll);
            foreach (KeyValuePair<Control, int> entry in pageLeft)
            {
                Control row = entry.Key;
                if (!built.ContainsKey(row) || Folded(row) || dropped.Contains(row) || !panelOf.ContainsKey(row) || !row.Visible) continue;
                int wanted = PlacedTop(row, simple) - shift;
                if (row.Top != wanted) return Name(row) + " in " + panelOf[row].Id + " stands at " + row.Top + " for " + wanted + " (scrolled " + pageScroll + ")";
            }
            return null;
        }

        // The heading of the panel under a row's own panel in its column, or null for the last one.
        private Head HeadBelow(Control row)
        {
            Panel panel;
            if (!panelOf.TryGetValue(row, out panel)) return null;
            List<Panel> list = columns[panel.Column];
            int at = list.IndexOf(panel);
            return at >= 0 && at + 1 < list.Count ? list[at + 1].Head : null;
        }

        // Each panel's rows under its heading and above the next panel's heading, in its own column, in both views; null
        // when they all are.
        private string PanelsSound()
        {
            foreach (bool view in new[] { false, true })
                for (int column = 0; column < 2; column++)
                {
                    List<Panel> list = columns[column];
                    for (int i = 0; i < list.Count; i++)
                    {
                        Panel panel = list[i];
                        int top = PanelTop(panel, view), next = i + 1 < list.Count ? PanelTop(list[i + 1], view) : int.MaxValue;
                        foreach (KeyValuePair<Control, int> row in panel.Offsets)
                        {
                            if (FoldedIn(row.Key, view) || !row.Key.Visible) continue;
                            int at = StackTop(row.Key, view);
                            // Its height as built, so the check holds at every Mainframe size and not only at 100%.
                            if (at < top - HeadSlack || at + (int)Math.Round(row.Key.Height / Unit()) > next) return Name(row.Key) + " in " + panel.Id + " sits at " + at + ", outside its panel (" + top + " to " + next + ")" + (view ? " in Simple" : "");
                            if (pageLeft.Find(entry => entry.Key == row.Key).Value / Column != column) return Name(row.Key) + " isn't in " + panel.Id + "'s column";
                        }
                    }
                }
            return null;
        }

        private static string Name(Control control) { return control.Text.Length > 0 ? control.Text : control.AccessibleName ?? control.GetType().Name; }

        // Each column's panels, top to bottom, by name: "YOU VOICE MOVES ... | GENERAL SETTINGS CAPRI ...".
        internal string PanelOrder
        {
            get
            {
                List<string> left = new List<string>(), right = new List<string>();
                foreach (Panel panel in columns[0]) left.Add(panel.Id);
                foreach (Panel panel in columns[1]) right.Add(panel.Id);
                return string.Join(" ", left.ToArray()) + " | " + string.Join(" ", right.ToArray());
            }
        }
    }
}
