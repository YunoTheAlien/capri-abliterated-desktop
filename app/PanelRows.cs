using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Stage 3: Settings rows moved between panels, hidden, and gathered into categories of their own, from Toggles'
    // PAN: MAINFRAME SETTINGS > Panels. Kept with her settings; a layout mod's arrangement lies under theirs.
    internal sealed partial class Companion
    {
        internal const string OwnPrefix = "own:";

        // "PANEL<tab>key|key|..." for each panel whose rows were moved; row keys "PANEL/Row name".
        internal List<string> RowOrders { get { return preferences.RowOrders ?? new List<string>(); } }

        internal List<string> RowsHidden { get { return preferences.RowsHidden ?? new List<string>(); } }

        internal List<string> OwnPanels { get { return preferences.OwnPanels ?? new List<string>(); } }

        // Layout mods laid now, in load order: their orders and hidden rows, under theirs.
        private readonly List<KeyValuePair<string, KeyValuePair<List<string>, List<string>>>> layouts = new List<KeyValuePair<string, KeyValuePair<List<string>, List<string>>>>();

        internal List<string> LayoutOrders { get { List<string> all = new List<string>(); foreach (var one in layouts) all.AddRange(one.Value.Key); return all; } }

        internal List<string> LayoutHidden { get { List<string> all = new List<string>(); foreach (var one in layouts) all.AddRange(one.Value.Value); return all; } }

        internal void DropLayout(string id)
        {
            if (layouts.RemoveAll(one => one.Key == id) > 0 && mainframe != null) OnWindow(delegate { mainframe.ArrangeRows(); });
        }

        internal void SaveArrangement(List<string> orders, List<string> hidden, List<string> own)
        {
            preferences.RowOrders = orders == null || orders.Count == 0 ? null : orders;
            preferences.RowsHidden = hidden == null || hidden.Count == 0 ? null : hidden;
            preferences.OwnPanels = own == null || own.Count == 0 ? null : own;
            SavePreferences();
        }

        // A layout mod: "layout" as its page, each panel by its heading's words, its rows by name in their order; a row
        // marked [hidden] is taken off. It names only her rows, never code.
        internal string LayLayout(string id, PanelTree.Model model)
        {
            if (model == null || !string.Equals(model.Page, "layout", StringComparison.OrdinalIgnoreCase)) return "not a layout";
            List<string> orders = new List<string>(), hidden = new List<string>();
            foreach (PanelTree.Row panel in model.Panels)
            {
                List<string> keys = new List<string>();
                foreach (PanelTree.Row row in panel.Under)
                {
                    string key = "@" + row.Name;
                    if (row.Flags.Contains("hidden")) hidden.Add(key); else keys.Add(key);
                }
                if (keys.Count > 0) orders.Add(panel.Name.ToUpperInvariant() + "\t" + string.Join("|", keys.ToArray()));
            }
            layouts.RemoveAll(one => one.Key == id);
            layouts.Add(new KeyValuePair<string, KeyValuePair<List<string>, List<string>>>(id, new KeyValuePair<List<string>, List<string>>(orders, hidden)));
            if (mainframe != null) OnWindow(delegate { mainframe.ArrangeRows(); });
            return null;
        }

#if CHECKS
        // Stage 3: rows moved, hidden and shown, Default, their own category made, filled, renamed and removed, a layout
        // mod saved, laid and taken off; nothing overlaps after each, and theirs is put back.
        private void SmokePanelRows(List<string> checks, string output)
        {
            List<string> ordersWas = preferences.RowOrders == null ? null : new List<string>(preferences.RowOrders), hiddenWas = preferences.RowsHidden == null ? null : new List<string>(preferences.RowsHidden), ownWas = preferences.OwnPanels == null ? null : new List<string>(preferences.OwnPanels);
            bool shownWas = mainframe.Visible;
            System.Windows.Forms.FormWindowState stateWas = mainframe.WindowState;
            List<string> wrong = new List<string>();
            string written = null;
            try
            {
                SaveArrangement(null, null, null);
                mainframe.ArrangeRows();
                string first = mainframe.RowNamed("VOICE", 0), second = mainframe.RowNamed("VOICE", 1), into = mainframe.RowNamed("SOUNDS", 0);
                if (first == null || second == null || into == null) throw new Exception("no named rows in VOICE and SOUNDS to move: " + first + ", " + second + ", " + into);
                Func<string, string> overlap = delegate(string when) { string why = mainframe.PageOverlap(); if (why != null) wrong.Add(when + ": " + why); return why; };
                int voiceWas = mainframe.PanelSpanOf("VOICE");
                mainframe.PanelsDrop(first, into, false);
                if (mainframe.RowPanel(first) != "SOUNDS" || mainframe.PanelSpanOf("VOICE") >= voiceWas) wrong.Add("moving " + first + " into SOUNDS: it is in " + mainframe.RowPanel(first) + ", VOICE " + voiceWas + " to " + mainframe.PanelSpanOf("VOICE"));
                overlap("a row moved into another panel");
                string side = mainframe.PanelsText ?? "";
                if (side.IndexOf("[SOUNDS] " + first.Substring(first.IndexOf('/') + 1), StringComparison.Ordinal) < 0) wrong.Add("the Panels side doesn't show it first in SOUNDS: " + side);
                mainframe.PanelsPress(second.Substring(second.IndexOf('/') + 1) + ": Hide");
                if (mainframe.RowShows(second) || !RowsHidden.Contains(second)) wrong.Add("Hide left " + second + " showing");
                overlap("a row hidden");
                mainframe.PicturePanels(Path.Combine(output, "stage3-panels.png"));
                mainframe.PanelsPress(second.Substring(second.IndexOf('/') + 1) + ": Show");
                if (!mainframe.RowShows(second)) wrong.Add("Show didn't bring " + second + " back");
                mainframe.PanelsPress("VOICE: Default");
                if (mainframe.RowPanel(first) != "VOICE" || mainframe.PanelSpanOf("VOICE") != voiceWas) wrong.Add("Default didn't send " + first + " home: in " + mainframe.RowPanel(first) + ", VOICE " + mainframe.PanelSpanOf("VOICE") + " for " + voiceWas);
                overlap("Default");
                if (mainframe.AddOwnPanel("Quick ones") != null) wrong.Add("no category of their own made");
                mainframe.PanelsDrop(first, "QUICK ONES", false);
                if (mainframe.RowPanel(first) != OwnPrefix + "Quick ones") wrong.Add("the row didn't go into their category: " + mainframe.RowPanel(first));
                overlap("a category of their own with a row");
                mainframe.PictureSettings(Path.Combine(output, "stage3-own-category.png"));
                mainframe.RenameOwnPanel(OwnPrefix + "Quick ones", "Fast");
                if (mainframe.RowPanel(first) != OwnPrefix + "Fast") wrong.Add("renamed, the row is in " + mainframe.RowPanel(first));
                written = Path.Combine(ModList.Folder, "stage3-check" + Mods.TreeExtension);
                string saved = SaveLayoutMod("Stage 3 check");
                string[] made = System.IO.Directory.GetFiles(ModList.Folder, "my-layout-*" + Mods.TreeExtension);
                if (saved != null || made.Length == 0) wrong.Add("Save as layout mod: " + (saved ?? "no file"));
                else
                {
                    string text = File.ReadAllText(made[made.Length - 1]);
                    if (text.IndexOf("\nlayout", StringComparison.Ordinal) < 0 || text.IndexOf("-FAST", StringComparison.OrdinalIgnoreCase) < 0) wrong.Add("the layout mod written holds " + text.Length + " characters without its page or their category");
                    foreach (string one in made) File.Delete(one);
                }
                mainframe.RemoveOwnPanel(OwnPrefix + "Fast");
                if (mainframe.RowPanel(first) != "VOICE") wrong.Add("their category removed, the row is in " + mainframe.RowPanel(first));
                overlap("their category removed");
                // A layout mod lies under theirs: its order and its hidden row show, and taken off, hers comes back.
                SaveArrangement(null, null, null);
                string row = first.Substring(first.IndexOf('/') + 1), other = second.Substring(second.IndexOf('/') + 1);
                PanelTree.Model model = PanelTree.Read(string.Join("\n", new[] { "layout", "-SOUNDS", "--" + row, "-VOICE", "--" + other + " [switch, hidden]" }), null);
                LayLayout("layout-check", model);
                mainframe.ArrangeRows();
                if (mainframe.RowPanel(first) != "SOUNDS" || mainframe.RowShows(second)) wrong.Add("a layout mod: " + first + " in " + mainframe.RowPanel(first) + ", " + second + " showing " + mainframe.RowShows(second));
                DropLayout("layout-check");
                mainframe.ArrangeRows();
                if (mainframe.RowPanel(first) != "VOICE" || !mainframe.RowShows(second)) wrong.Add("the layout mod off didn't give hers back");
                overlap("a layout mod off");
            }
            finally
            {
                DropLayout("layout-check");
                SaveArrangement(ordersWas, hiddenWas, ownWas);
                mainframe.ArrangeRows();
                mainframe.ShowSettings(false);
                if (mainframe.WindowState != stateWas) mainframe.WindowState = stateWas;
                if (!shownWas && mainframe.Visible) mainframe.Hide();
            }
            if (wrong.Count > 0) throw new Exception("Stage 3 went wrong: " + string.Join("; ", wrong.ToArray()));
            checks.Add("PASS stage 3, Toggles' Panels: a row dragged into another panel and its panel shrinking, Hide and Show, Default sending rows home, a category of their own made, filled, renamed and removed, the arrangement saved as a layout mod, and a layout mod laid under theirs and taken off; nothing overlaps on the page after each (stage3-panels.png, stage3-own-category.png)");
        }
#endif

        // Their arrangement as a layout mod in her mods folder, so it can be shared. Null when written; else why not.
        internal string SaveLayoutMod(string name)
        {
            if (ModList == null || mainframe == null) return "no mods folder";
            string id = "my-layout-" + DateTime.Now.ToString("yyyyMMdd-HHmm", System.Globalization.CultureInfo.InvariantCulture);
            StringBuilder text = new StringBuilder();
            text.AppendLine("// capri-tree: 1");
            text.AppendLine("// id: " + id);
            text.AppendLine("// name: " + (string.IsNullOrWhiteSpace(name) ? "My Settings layout" : name.Trim()));
            text.AppendLine("// author: " + (Brain.Nickname ?? "me"));
            text.AppendLine("// version: 1");
            text.AppendLine("layout");
            foreach (Mainframe.PanelRowsInfo panel in mainframe.PanelRowsNow())
            {
                text.AppendLine("-" + panel.Name);
                foreach (Mainframe.RowInfo row in panel.Rows)
                    if (row.Movable) text.AppendLine("--" + row.Name + (row.Hidden ? " [switch, hidden]" : ""));
            }
            string path = Path.Combine(ModList.Folder, id + Mods.TreeExtension);
            try { CapriDesktop.Notes.WriteAll(path, text.ToString()); }
            catch (IOException error) { return error.Message; }
            catch (UnauthorizedAccessException error) { return error.Message; }
            return null;
        }
    }

    internal sealed partial class Mainframe
    {
        // Lines that take no room while empty; whatever fills one lays the rows again.
        private readonly HashSet<Control> folding = new HashSet<Control>();

        // The empty lines in a row, each with the room it leaves: down to the next part under it, or the row's foot.
        private List<KeyValuePair<int, int>> Folded(PageRow row)
        {
            List<KeyValuePair<int, int>> shut = new List<KeyValuePair<int, int>>();
            foreach (KeyValuePair<Control, int> line in row.Parts)
            {
                if (!folding.Contains(line.Key) || (line.Key.Text ?? "").Trim().Length > 0) continue;
                int next = row.Band;
                foreach (KeyValuePair<Control, int> other in row.Parts) if (other.Value > line.Value && other.Value < next) next = other.Value;
                shut.Add(new KeyValuePair<int, int>(line.Value, next - line.Value));
            }
            return shut;
        }

        private static int Lifted(List<KeyValuePair<int, int>> shut, int offset)
        {
            int lift = 0;
            foreach (KeyValuePair<int, int> one in shut) if (offset > one.Key) lift += one.Value;
            return lift;
        }

        // A row of a Settings panel as built: the controls on one line, where it sat, how tall a band it takes, and the
        // name it is moved and hidden by (its first words; none for a row only of keys or lines).
        private sealed class PageRow
        {
            internal string Name, Key;
            internal Panel Home;
            internal int Top, Band;
            internal readonly Dictionary<Control, int> Parts = new Dictionary<Control, int>();
        }

        internal sealed class RowInfo { internal string Key, Name; internal bool Hidden, Movable; }

        internal sealed class PanelRowsInfo { internal string Id, Name; internal bool Own; internal readonly List<RowInfo> Rows = new List<RowInfo>(); }

        private readonly List<PageRow> pageRows = new List<PageRow>();
        private readonly HashSet<Control> rowHidden = new HashSet<Control>();
        private readonly Dictionary<Panel, int> rowsStart = new Dictionary<Panel, int>(), tailGap = new Dictionary<Panel, int>();

        private static string RowName(IEnumerable<Control> parts)
        {
            Control first = null;
            foreach (Control one in parts)
            {
                if (one is Key || one is Slider || string.IsNullOrWhiteSpace(one.Text)) continue;
                if (first == null || one.Left < first.Left) first = one;
            }
            return first == null ? null : first.Text.Replace(" >", "").Trim();
        }

        private static bool OnlyWords(IEnumerable<Control> parts)
        {
            foreach (Control one in parts) if (!(one is Label)) return false;
            return true;
        }

        // Her panels' rows as built, grouped by the line they sit on, before any arrangement is laid.
        private void CaptureRows()
        {
            pageRows.Clear();
            foreach (List<Panel> list in columns)
                foreach (Panel panel in list)
                {
                    if (panel.Mod != null) continue;
                    List<KeyValuePair<Control, int>> parts = new List<KeyValuePair<Control, int>>();
                    foreach (KeyValuePair<Control, int> one in panel.Offsets) if (one.Key != panel.Head && one.Value >= 20) parts.Add(one);
                    parts.Sort(delegate(KeyValuePair<Control, int> a, KeyValuePair<Control, int> b) { return a.Value.CompareTo(b.Value); });
                    List<PageRow> rows = new List<PageRow>();
                    foreach (KeyValuePair<Control, int> one in parts)
                    {
                        PageRow last = rows.Count == 0 ? null : rows[rows.Count - 1];
                        if (last == null || one.Value > last.Top + 8) { last = new PageRow { Home = panel, Top = one.Value }; rows.Add(last); }
                        last.Parts[one.Key] = one.Value - last.Top;
                    }
                    // A line with no words of its own (a note's second line, a row of keys under a switch) goes with the row above.
                    for (int i = rows.Count - 1; i > 0; i--)
                    {
                        if (RowName(rows[i].Parts.Keys) != null) continue;
                        foreach (KeyValuePair<Control, int> part in rows[i].Parts) rows[i - 1].Parts[part.Key] = rows[i].Top + part.Value - rows[i - 1].Top;
                        rows.RemoveAt(i);
                    }
                    // A note or caption (only words, nothing to press or set) isn't a row to move: it goes with the row
                    // above, or the one below when it leads the panel.
                    for (int i = rows.Count - 1; i >= 0 && rows.Count > 1; i--)
                    {
                        if (!OnlyWords(rows[i].Parts.Keys)) continue;
                        int into = i > 0 ? i - 1 : 1;
                        int top = Math.Min(rows[into].Top, rows[i].Top);
                        Dictionary<Control, int> joined = new Dictionary<Control, int>();
                        foreach (KeyValuePair<Control, int> part in rows[into].Parts) joined[part.Key] = rows[into].Top + part.Value - top;
                        foreach (KeyValuePair<Control, int> part in rows[i].Parts) joined[part.Key] = rows[i].Top + part.Value - top;
                        rows[into].Parts.Clear();
                        foreach (KeyValuePair<Control, int> part in joined) rows[into].Parts[part.Key] = part.Value;
                        rows[into].Top = top;
                        rows.RemoveAt(i);
                    }
                    int end = Content(panel);
                    HashSet<string> named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < rows.Count; i++)
                    {
                        rows[i].Band = (i + 1 < rows.Count ? rows[i + 1].Top : end + 6) - rows[i].Top;
                        string name = RowName(rows[i].Parts.Keys);
                        if (name != null) { string unique = name; for (int n = 2; !named.Add(unique); n++) unique = name + " (" + n + ")"; rows[i].Name = unique; rows[i].Key = panel.Id + "/" + unique; }
                    }
                    rowsStart[panel] = rows.Count > 0 ? rows[0].Top : 30;
                    tailGap[panel] = panel.Span - end - 6;
                    pageRows.AddRange(rows);
                }
        }

        // Their categories as panels of their own at the foot of the right column, each an empty heading until rows go in.
        private void MakeOwnPanels()
        {
            foreach (string name in capri.OwnPanels)
            {
                string id = Companion.OwnPrefix + name;
                if (FindPanel(id) != null) continue;
                int laid = layout.Count;
                Head head = Section(name.ToUpperInvariant() + " >", Column, 0);
                Panel panel = new Panel { Id = id, Head = head, Built = 1, Column = 1 };
                panel.Offsets[head] = 0;
                panel.Span = 30 + panelGap;
                WireDrag(head);
                built[head] = 0;
                pageLeft.Add(new KeyValuePair<Control, int>(head, head.Left));
                panelOf[head] = panel;
                Scrolls(head);
                tops[head] = head.Top;
                RememberLayout(head);
                for (int i = laid; i < layout.Count; i++) layout[i].Apply(unit, layoutFallback);
                rowsStart[panel] = 30;
                tailGap[panel] = panelGap;
                columns[1].Add(panel);
            }
            // A category gone from their settings goes from the page; its rows go home as the arrangement is laid.
            foreach (List<Panel> list in columns)
                foreach (Panel panel in list.FindAll(one => one.Id.StartsWith(Companion.OwnPrefix, StringComparison.Ordinal) && !capri.OwnPanels.Contains(one.Id.Substring(Companion.OwnPrefix.Length))))
                {
                    list.Remove(panel);
                    panelOf.Remove(panel.Head);
                    built.Remove(panel.Head);
                    pageLeft.RemoveAll(entry => entry.Key == panel.Head);
                    scrolled.Remove(panel.Head);
                    tops.Remove(panel.Head);
                    layout.RemoveAll(item => item.Control == panel.Head);
                    Controls.Remove(panel.Head);
                    panel.Head.Dispose();
                }
        }

        // Each panel's rows as their settings and any layout mod say, then the page placed again.
        internal void ArrangeRows() { ArrangeRows(true); }

        private void ArrangeRows(bool restack)
        {
            if (IsDisposed || pageRows.Count == 0) return;
            MakeOwnPanels();
            Dictionary<string, PageRow> byKey = new Dictionary<string, PageRow>(StringComparer.OrdinalIgnoreCase);
            foreach (PageRow row in pageRows) if (row.Key != null) { byKey[row.Key] = row; if (!byKey.ContainsKey("@" + row.Name)) byKey["@" + row.Name] = row; }
            // Orders: a layout mod's first, theirs over it; a row named by a later panel's order goes there.
            Dictionary<string, List<string>> orders = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (List<string> source in new[] { capri.LayoutOrders, capri.RowOrders })
                foreach (string line in source)
                {
                    int tab = line.IndexOf('\t');
                    if (tab <= 0) continue;
                    orders[line.Substring(0, tab)] = new List<string>(line.Substring(tab + 1).Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries));
                }
            Dictionary<PageRow, Panel> placed = new Dictionary<PageRow, Panel>();
            List<Panel> panels = new List<Panel>();
            foreach (List<Panel> list in columns) foreach (Panel panel in list) if (panel.Mod == null) panels.Add(panel);
            foreach (Panel panel in panels)
            {
                List<string> keys;
                if (!orders.TryGetValue(panel.Id, out keys)) continue;
                foreach (string key in keys) { PageRow row; if (byKey.TryGetValue(key, out row) && !placed.ContainsKey(row)) placed[row] = panel; }
            }
            HashSet<string> hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in capri.LayoutHidden) { PageRow named; hidden.Add(byKey.TryGetValue(key, out named) ? named.Key : key); }
            foreach (string key in capri.RowsHidden) hidden.Add(key);
            // Rows shown again first, so a row no longer hidden comes back.
            foreach (Control one in rowHidden) one.Visible = true;
            rowHidden.Clear();
            foreach (Panel panel in panels)
            {
                List<PageRow> rows = new List<PageRow>();
                List<string> keys;
                if (orders.TryGetValue(panel.Id, out keys))
                    foreach (string key in keys) { PageRow row; if (byKey.TryGetValue(key, out row) && placed[row] == panel) rows.Add(row); }
                // Her rows not sent anywhere stay home at their built place: unnamed ones, ones sent back, and ones a newer
                // version added.
                foreach (PageRow row in pageRows)
                {
                    if (row.Home != panel || (placed.ContainsKey(row) && placed[row] != panel) || rows.Contains(row)) continue;
                    int built = pageRows.IndexOf(row), at = rows.FindIndex(one => one.Home == panel && pageRows.IndexOf(one) > built);
                    rows.Insert(at < 0 ? rows.Count : at, row);
                }
                int y = rowsStart.ContainsKey(panel) ? rowsStart[panel] : 30;
                foreach (PageRow row in rows)
                {
                    bool off = row.Key != null && hidden.Contains(row.Key);
                    List<KeyValuePair<int, int>> shut = Folded(row);
                    foreach (KeyValuePair<Control, int> part in row.Parts)
                    {
                        Control control = part.Key;
                        Panel from;
                        if (panelOf.TryGetValue(control, out from) && from != panel) { from.Offsets.Remove(control); if (from.Column != panel.Column) ShiftColumn(control, panel.Column - from.Column); }
                        panel.Offsets[control] = y + part.Value - Lifted(shut, part.Value);
                        panelOf[control] = panel;
                        if (off && control.Visible) { control.Visible = false; rowHidden.Add(control); }
                    }
                    if (!off) y += row.Band - Lifted(shut, int.MaxValue);
                }
                panel.Span = y + (tailGap.ContainsKey(panel) ? tailGap[panel] : panelGap);
            }
            if (restack) Restack();
        }

        // As the page is built: her rows taken as built, their categories made, the saved order laid again with them in it,
        // and the arrangement laid.
        private void StageThree()
        {
            CaptureRows();
            MakeOwnPanels();
            SavedOrder();
            ArrangeRows(false);
        }

        // What the Panels side lists: each panel of hers and theirs, column by column, with its rows as they stand.
        internal List<PanelRowsInfo> PanelRowsNow()
        {
            List<PanelRowsInfo> found = new List<PanelRowsInfo>();
            HashSet<string> hidden = new HashSet<string>(capri.RowsHidden, StringComparer.OrdinalIgnoreCase);
            foreach (Control part in rowHidden) foreach (PageRow row in pageRows) if (row.Key != null && row.Parts.ContainsKey(part)) hidden.Add(row.Key);
            foreach (List<Panel> list in columns)
                foreach (Panel panel in list)
                {
                    if (panel.Mod != null) continue;
                    PanelRowsInfo info = new PanelRowsInfo { Id = panel.Id, Name = panel.Head.Shown, Own = panel.Id.StartsWith(Companion.OwnPrefix, StringComparison.Ordinal) };
                    List<PageRow> rows = pageRows.FindAll(one => { Control first = null; foreach (Control c in one.Parts.Keys) { first = c; break; } Panel at; return first != null && panelOf.TryGetValue(first, out at) && at == panel; });
                    rows.Sort(delegate(PageRow a, PageRow b) { int x = 0, z = 0; foreach (KeyValuePair<Control, int> c in a.Parts) { x = panel.Offsets[c.Key] - c.Value; break; } foreach (KeyValuePair<Control, int> c in b.Parts) { z = panel.Offsets[c.Key] - c.Value; break; } return x.CompareTo(z); });
                    foreach (PageRow row in rows) info.Rows.Add(new RowInfo { Key = row.Key, Name = row.Name ?? "", Movable = row.Key != null, Hidden = row.Key != null && hidden.Contains(row.Key) });
                    found.Add(info);
                }
            return found;
        }

        private string RowPanelNow(string key)
        {
            PageRow row = pageRows.Find(one => one.Key == key);
            if (row == null) return null;
            foreach (Control part in row.Parts.Keys) { Panel at; return panelOf.TryGetValue(part, out at) ? at.Id : null; }
            return null;
        }

        // A row moved into a panel before another (null: last), as a drag on the Panels side does.
        internal void MoveRow(string key, string panelId, string before)
        {
            List<PanelRowsInfo> now = PanelRowsNow();
            string from = RowPanelNow(key);
            List<string> orders = capri.RowOrders.FindAll(line => !line.StartsWith(panelId + "\t", StringComparison.OrdinalIgnoreCase) && (from == null || !line.StartsWith(from + "\t", StringComparison.OrdinalIgnoreCase)));
            foreach (PanelRowsInfo panel in now)
            {
                if (panel.Id != panelId && panel.Id != from) continue;
                List<string> keys = new List<string>();
                foreach (RowInfo row in panel.Rows) if (row.Movable && row.Key != key) keys.Add(row.Key);
                if (panel.Id == panelId)
                {
                    int at = before == null ? keys.Count : keys.IndexOf(before);
                    keys.Insert(at < 0 ? keys.Count : at, key);
                }
                orders.Add(panel.Id + "\t" + string.Join("|", keys.ToArray()));
            }
            capri.SaveArrangement(orders, capri.RowsHidden, capri.OwnPanels);
            ArrangeRows();
        }

        internal void HideRow(string key, bool hide)
        {
            List<string> hidden = capri.RowsHidden;
            hidden.RemoveAll(one => string.Equals(one, key, StringComparison.OrdinalIgnoreCase));
            if (hide) hidden.Add(key);
            capri.SaveArrangement(capri.RowOrders, hidden, capri.OwnPanels);
            ArrangeRows();
        }

        // Default: the panel's own rows home in her order and shown, and rows from elsewhere sent home.
        internal void DefaultPanel(string panelId)
        {
            List<string> orders = capri.RowOrders.FindAll(line => !line.StartsWith(panelId + "\t", StringComparison.OrdinalIgnoreCase));
            HashSet<string> mine = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PageRow row in pageRows) if (row.Home.Id == panelId && row.Key != null) mine.Add(row.Key);
            for (int i = 0; i < orders.Count; i++)
            {
                int tab = orders[i].IndexOf('\t');
                List<string> keys = new List<string>(orders[i].Substring(tab + 1).Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries));
                keys.RemoveAll(one => mine.Contains(one));
                orders[i] = orders[i].Substring(0, tab) + "\t" + string.Join("|", keys.ToArray());
            }
            List<string> hidden = capri.RowsHidden.FindAll(one => !mine.Contains(one));
            capri.SaveArrangement(orders, hidden, capri.OwnPanels);
            ArrangeRows();
        }

        internal string AddOwnPanel(string name)
        {
            string clean = (name ?? "").Replace("|", " ").Replace(";", " ").Replace("\t", " ").Trim();
            if (clean.Length == 0) return "no name";
            if (FindPanel(clean.ToUpperInvariant()) != null || capri.OwnPanels.Exists(one => string.Equals(one, clean, StringComparison.OrdinalIgnoreCase))) return "a panel has that name";
            List<string> own = capri.OwnPanels;
            own.Add(clean);
            capri.SaveArrangement(capri.RowOrders, capri.RowsHidden, own);
            ArrangeRows();
            return null;
        }

        internal void RenameOwnPanel(string id, string name)
        {
            string clean = (name ?? "").Replace("|", " ").Replace(";", " ").Replace("\t", " ").Trim();
            if (clean.Length == 0 || !id.StartsWith(Companion.OwnPrefix, StringComparison.Ordinal)) return;
            string was = id.Substring(Companion.OwnPrefix.Length), now = Companion.OwnPrefix + clean;
            List<string> own = capri.OwnPanels;
            int at = own.FindIndex(one => one == was);
            if (at < 0) return;
            own[at] = clean;
            List<string> orders = capri.RowOrders.ConvertAll(line => line.StartsWith(id + "\t", StringComparison.Ordinal) ? now + line.Substring(id.Length) : line);
            Panel panel = FindPanel(id);
            if (panel != null) { panel.Id = now; panel.Head.Text = clean.ToUpperInvariant() + " >"; }
            capri.SaveArrangement(orders, capri.RowsHidden, own);
            ArrangeRows();
        }

#if CHECKS
        // For the checks: the panel a row sits in on the page now, whether it shows, and where its first part stands.
        internal string RowPanel(string key)
        {
            PageRow row = pageRows.Find(one => one.Key == key);
            if (row == null) return null;
            foreach (Control part in row.Parts.Keys) { Panel at; return panelOf.TryGetValue(part, out at) ? at.Id : null; }
            return null;
        }

        internal bool RowShows(string key)
        {
            PageRow row = pageRows.Find(one => one.Key == key);
            if (row == null) return false;
            foreach (Control part in row.Parts.Keys) return !rowHidden.Contains(part);
            return false;
        }

        internal string RowNamed(string panel, int at)
        {
            List<PageRow> rows = pageRows.FindAll(one => one.Home.Id == panel && one.Key != null);
            return at < rows.Count ? rows[at].Key : null;
        }

        internal int PanelSpanOf(string id) { Panel panel = FindPanel(id); return panel == null ? -1 : PanelSpan(panel); }

        internal string PageOverlap() { return OverlapNamed(); }

        internal void PicturePanels(string path)
        {
            OpenPanels();
            Application.DoEvents();
            using (Bitmap shot = new Bitmap(toggleSheet.Width, toggleSheet.Height))
            {
                toggleSheet.DrawToBitmap(shot, new Rectangle(Point.Empty, shot.Size));
                shot.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        internal void PanelsDrop(string what, string onto, bool below) { OpenPanels(); toggleSheet.PanelsListNow.DropBy(what, onto, below); }

        internal void PanelsPress(string named) { OpenPanels(); toggleSheet.PanelsListNow.Press(named); }
#endif

        internal void RemoveOwnPanel(string id)
        {
            List<string> own = capri.OwnPanels;
            own.RemoveAll(one => Companion.OwnPrefix + one == id);
            capri.SaveArrangement(capri.RowOrders.FindAll(line => !line.StartsWith(id + "\t", StringComparison.Ordinal)), capri.RowsHidden, own);
            ArrangeRows();
        }
    }
}
