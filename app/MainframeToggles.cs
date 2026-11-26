using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Forms;

namespace CapriDesktop
{
    // The Toggles window, under Hologram in CAPRI and on her right-click menu: every switch of hers and of mods in one place,
    // built like Custom colours with a fold per group. Each row runs its switch's own handler, so nothing is doubled up.
    internal sealed partial class Mainframe
    {
        private ToggleSheet toggleSheet;

        internal void OpenToggles()
        {
            if (toggleSheet != null && !toggleSheet.IsDisposed && toggleSheet.Zoom != zoom) toggleSheet.Close();
            capri.SectionsChanged();
            if (toggleSheet == null || toggleSheet.IsDisposed) toggleSheet = new ToggleSheet(this, SheetZoom(ToggleSheet.Wide, ToggleSheet.Tall), capri.TogglesMenu);
            if (!toggleSheet.Visible) toggleSheet.Show(this);
            toggleSheet.Activate();
        }

        internal void CloseToggles() { if (toggleSheet != null && !toggleSheet.IsDisposed) toggleSheet.Close(); }

        // For the checks: what the open window shows, a row pressed, a fold opened.
        internal string TogglesText { get { return toggleSheet != null && !toggleSheet.IsDisposed && toggleSheet.Visible ? toggleSheet.Shown : null; } }

        internal void PressToggle(string text) { if (toggleSheet != null && !toggleSheet.IsDisposed) toggleSheet.Press(text); }

        internal void UnfoldToggles(string group) { if (toggleSheet != null && !toggleSheet.IsDisposed) toggleSheet.Unfold(group); }

        internal bool OpenToggleSettings(string owner) { return toggleSheet != null && !toggleSheet.IsDisposed && toggleSheet.OpenSettings(owner); }

        // For the checks: the Toggles window with every fold open, as a picture.
        internal Bitmap PictureToggles()
        {
            OpenToggles();
            toggleSheet.UnfoldAll();
            Application.DoEvents();
            Bitmap shot = new Bitmap(toggleSheet.Width, toggleSheet.Height);
            toggleSheet.DrawToBitmap(shot, new Rectangle(Point.Empty, shot.Size));
            return shot;
        }

        internal void PickToggleRow(string text) { if (toggleSheet != null && !toggleSheet.IsDisposed) toggleSheet.PickRow(text); }

#if CHECKS
        internal List<string> PressEveryToggle(Func<string> watch, Func<bool> tidy) { OpenToggles(); return toggleSheet.PressAll(watch, tidy); }

        internal string TogglesPetRow() { OpenToggles(); toggleSheet.UnfoldAll(); return toggleSheet.PetRow(); }
#endif

        // For the checks: a click on a row's name; true when it left the row's switch as it was.
        internal bool ClickToggleName(string text) { return toggleSheet != null && !toggleSheet.IsDisposed && toggleSheet.ClickName(text); }

        internal string ToggleFoldRows(string fold) { OpenToggles(); toggleSheet.UnfoldAll(); return toggleSheet.FoldRows(fold); }

        internal bool OpenToggleRowSettings(string words) { OpenToggles(); toggleSheet.UnfoldAll(); return toggleSheet.OpenRowSettings(words); }

        // A slider's steps as items in a fold: "step:<slider name>:<index>", drawn as one slider row.
        internal const string StepTag = "step:";
        // An item of hers with settings that keeps its row in General rather than a fold of its own; and, among an item's
        // settings, a line of words, and the status box.
        internal const string GeneralTag = "toggles:general", TextTag = "toggles:text", StatusTag = "toggles:status";
        // A setting that is its name and a lit key: "toggles:pick:" then the key's words; its tip is what it holds.
        internal const string PickTag = "toggles:pick:";
        // A row of only a settings key (Sections, or an app switched on Mods); a mod's switches under its name in a Sec:
        // fold; a Sec: fold, by its section's id.
        internal const string SectionsTag = "toggles:sections", OpensTag = "toggles:opens", GroupTag = "toggles:group", SecTag = "toggles:sec:";
        // A key row whose key shows its settings on the right instead of opening anything.
        internal const string SideRow = "toggles:side";

        // The Toggles window made again after sections or a mod's switches changed, when it is open.
        internal void TogglesChanged() { if (toggleSheet != null && !toggleSheet.IsDisposed) toggleSheet.Refill(); }

        // The status box: its words, [STATUS:OK] at rest, centred in a lit frame like a dashboard light, in her palette so it
        // follows Mainframe colours. One look wherever a status shows (Toggles, the Mods page).
        internal static Label StatusBox(string text, Font font, Rectangle bounds)
        {
            Label box = new Note { Text = text, Font = font, ForeColor = DeepEdge, BackColor = Ground, AutoSize = false, UseMnemonic = false, TextAlign = ContentAlignment.MiddleCenter, Cuts = true, Bounds = bounds };
            // Her keys' clipped corner, bottom right, so it sits with everything else on the page.
            box.Paint += delegate(object sender, PaintEventArgs e)
            {
                float s = ScaleOf(box), line = Math.Max(1f, s);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath edge = Cut(new RectangleF(line / 2, line / 2, box.Width - line, box.Height - line), 0, 0, 6 * s, 0))
                using (Pen pen = new Pen(box.ForeColor, line))
                    e.Graphics.DrawPath(pen, edge);
            };
            return box;
        }

        private sealed partial class ToggleSheet : ModSheet
        {
            // The list on the left, in the one left column her windows share (Column); on the right, the picked item's own
            // settings, behind its ">".
            internal const int Wide = 720, Tall = 560, ListTop = 64, SideLeft = LeftColumn.Side;
            internal const string General = "General";
            private readonly ToolStripMenuItem source;
            private readonly ScrollPanel list;
            private readonly SlimBar bar = new SlimBar();
            private readonly HashSet<string> open = new HashSet<string>(StringComparer.Ordinal) { General };
            private readonly List<KeyValuePair<Toggle, ToolStripMenuItem>> rows = new List<KeyValuePair<Toggle, ToolStripMenuItem>>();
            private bool syncing;
            // The screensaver's waits, "After 5 min" and on in her menu, as one slider here rather than a switch each.
            private const string WaitTag = "after:", WaitName = "After";
            private Slider waits;
            private readonly List<ToolStripMenuItem> waitItems = new List<ToolStripMenuItem>();

            internal ToggleSheet(Mainframe owner, float zoom, ToolStripMenuItem from) : base(owner, zoom, "Toggles", "TOGGLES", Wide, Tall)
            {
                source = from;
                // Her own slim bar beside the list, not Windows' one, inside the column's frame.
                list = new ScrollPanel { BackColor = Ground };
                LeftColumn.Place(list, bar, ListTop, Tall - ListTop - 28, Zoom);
                list.Scrolled += delegate { bar.Invalidate(); };
                Controls.Add(list);
                bar.Watch(list);
                Controls.Add(bar);
                Fill();
                ResumeLayout(false);
            }

            // Her menu's groups: the plain switches under General, and each item that holds its own a fold of its own.
            internal List<KeyValuePair<string, List<ToolStripMenuItem>>> Groups()
            {
                List<KeyValuePair<string, List<ToolStripMenuItem>>> groups = new List<KeyValuePair<string, List<ToolStripMenuItem>>>();
                List<ToolStripMenuItem> plain = new List<ToolStripMenuItem>();
                groups.Add(new KeyValuePair<string, List<ToolStripMenuItem>>(General, plain));
                if (source == null) return groups;
                Tick(source);
                foreach (ToolStripItem one in source.DropDownItems)
                {
                    ToolStripMenuItem item = one as ToolStripMenuItem;
                    if (item == null) continue;
                    if (!item.HasDropDownItems || Equals(item.Tag, GeneralTag)) { plain.Add(item); continue; }
                    Tick(item);
                    List<ToolStripMenuItem> inside = new List<ToolStripMenuItem>();
                    foreach (ToolStripItem sub in item.DropDownItems) { ToolStripMenuItem each = sub as ToolStripMenuItem; if (each != null) inside.Add(each); }
                    groups.Add(new KeyValuePair<string, List<ToolStripMenuItem>>(item.Text, inside));
                }
                return groups;
            }

            // The menu ticks its items as it opens (DropDownOpening, raised by OnDropDownShow); the same is asked of it here
            // without showing it.
            private static readonly MethodInfo Opening = typeof(ToolStripDropDownItem).GetMethod("OnDropDownShow", BindingFlags.Instance | BindingFlags.NonPublic);

            private static void Tick(ToolStripMenuItem item)
            {
                try { if (Opening != null) Opening.Invoke(item, new object[] { EventArgs.Empty }); }
                catch (TargetInvocationException caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }

            private void Fill()
            {
                list.SuspendLayout();
                int scrolled = -list.AutoScrollPosition.Y;
                foreach (Control old in new List<Control>(Each(list.Controls))) { list.Controls.Remove(old); old.Dispose(); }
                rows.Clear();
                waits = null;
                waitItems.Clear();
                steppers.Clear();
                list.AutoScrollPosition = Point.Empty;
                settingsKeys.Clear();
                int y = 0, wide = LeftColumn.Wide - 4;
                foreach (KeyValuePair<string, List<ToolStripMenuItem>> group in Groups())
                {
                    if (group.Value.Count == 0) continue;
                    string name = group.Key;
                    bool shown = open.Contains(name);
                    FoldHead head = new FoldHead { Text = name.ToUpperInvariant(), Font = Type(10f * Zoom, true), Bounds = Box(0, y, wide, 27), Open = shown };
                    head.AccessibleName = name + (shown ? ", open" : ", folded");
                    head.Click += delegate { if (!open.Remove(name)) open.Add(name); Fill(); };
                    list.Controls.Add(head);
                    y += 31;
                    if (!shown) continue;
                    // A mod's own switches in a Sec: fold come after its name, as a part does in a shared Settings panel.
                    List<ToolStripMenuItem> inside = new List<ToolStripMenuItem>();
                    foreach (ToolStripMenuItem item in group.Value)
                    {
                        inside.Add(item);
                        if (Equals(item.Tag, GroupTag)) { Tick(item); foreach (ToolStripItem sub in item.DropDownItems) { ToolStripMenuItem each = sub as ToolStripMenuItem; if (each != null) inside.Add(each); } }
                    }
                    string owner = name;
                    Dictionary<string, List<ToolStripMenuItem>> sliders = new Dictionary<string, List<ToolStripMenuItem>>(StringComparer.Ordinal);
                    foreach (ToolStripMenuItem item in inside)
                    {
                        if (Equals(item.Tag, GroupTag)) { owner = item.Text; continue; }
                        if (!IsWait(item) && SliderOf(item) == null) continue;
                        if (!sliders.ContainsKey(owner)) sliders[owner] = new List<ToolStripMenuItem>();
                        sliders[owner].Add(item);
                    }
                    owner = name;
                    foreach (ToolStripMenuItem item in inside)
                    {
                        ToolStripMenuItem held = item;
                        if (Equals(item.Tag, GroupTag)) owner = item.Text;
                        if (Equals(item.Tag, GroupTag))
                        {
                            Label part = new Note { Text = item.Text, Font = Type(9.5f * Zoom, true), ForeColor = White, BackColor = Ground, AutoSize = false, UseMnemonic = false, Bounds = Box(6, y + 2, wide - 6, 20), Cuts = true };
                            part.AccessibleName = item.Text + ", a mod's switches";
                            list.Controls.Add(part);
                            y += 26;
                            continue;
                        }
                        if (Equals(item.Tag, SectionsTag) || Equals(item.Tag, OpensTag)) { y = KeyRow(item, y, wide); continue; }
                        if (IsWait(item) || SliderOf(item) != null)
                        {
                            if (!settingsKeys.ContainsKey(owner)) y = SettingsRow(owner, sliders[owner], y, wide);
                            continue;
                        }
                        bool more = item.HasDropDownItems;
                        // A group's own switch (Screensavers, Intros) is the one row for the group: its > opens the group's
                        // sliders, so they get no row of their own.
                        string tag = item.Tag as string;
                        bool master = tag != null && tag.EndsWith(":all", StringComparison.Ordinal) && sliders.ContainsKey(owner) && !settingsKeys.ContainsKey(owner);
                        string ownerHeld = owner;
                        if (master) more = true;
                        // The narrow pair, as on the Mainframe's settings, so the column stays narrow; a long name is cut, its tip whole.
                        Toggle row = new Toggle { Text = item.Text, Font = Font, Bounds = Box(6, y, wide - 36, 22), Checked = item.Checked, Switch = Toggle.Narrow };
                        row.AccessibleName = item.Text + (string.IsNullOrEmpty(item.ToolTipText) ? "" : ". " + item.ToolTipText);
                        row.CheckedChanged += delegate { if (syncing) return; held.PerformClick(); Sync(); };
                        list.Controls.Add(row);
                        // Every row has its settings key in the same column, so the switches line up: lit and pressable where
                        // the row has settings of its own, which open on the right, dimmed like an OFF switch where it has none.
                        OptionsKey opens = new OptionsKey { Text = ">", Font = Font, Bounds = Box(wide - 28, y, 28, 22), Has = more, Item = held, Picked = held == picked, Enabled = more };
                        opens.AccessibleName = item.Text + (more ? " settings" : " has no settings");
                        if (!more) opens.Cursor = Cursors.Default;
                        if (master)
                        {
                            List<ToolStripMenuItem> groupSliders = sliders[ownerHeld];
                            opens.Item = null;
                            opens.Picked = ownerHeld == pickedSettings;
                            opens.Click += delegate { PickSettings(ownerHeld, groupSliders); };
                            row.NameOpens = delegate { PickSettings(ownerHeld, groupSliders); };
                            settingsKeys[ownerHeld] = opens;
                            if (ownerHeld == pickedSettings) pickedSliders = groupSliders;
                        }
                        else
                        {
                            opens.Click += delegate { if (more) Pick(held); };
                            if (more) row.NameOpens = delegate { Pick(held); };
                        }
                        list.Controls.Add(opens);
                        rows.Add(new KeyValuePair<Toggle, ToolStripMenuItem>(row, item));
                        y += 28;
                    }
                    y += 6;
                }
                list.ResumeLayout();
                list.AutoScrollPosition = new Point(0, scrolled);
                list.Moved();
                FillSide();
            }

            // A row of only its name and a lit settings key: Sections opens its side; an app's opens its own options.
            private int KeyRow(ToolStripMenuItem item, int y, int wide)
            {
                ToolStripMenuItem held = item;
                bool sections = Equals(item.Tag, SectionsTag);
                // Flush, so its name starts where a switch row's does.
                Label named = new Note { Text = item.Text, Font = Font, ForeColor = Rose, BackColor = Ground, AutoSize = false, UseMnemonic = false, Bounds = Box(6, y + 3, wide - 44, 18), Cuts = true, Flush = true };
                list.Controls.Add(named);
                OptionsKey opens = new OptionsKey { Text = ">", Font = Font, Bounds = Box(wide - 28, y, 28, 22), Has = true, Item = held, Picked = sections && sectionsShown };
                opens.AccessibleName = item.Text + (sections ? " settings: name, order and remove sections" : ": its options");
                // A row of settings (the Tamagotchi's) shows them on the right; a plug-in's key opens its options.
                opens.Click += delegate { if (sections) PickSections(); else if (held.Name == PanelsRow) PickPanels(); else if (held.Name == SideRow) Pick(held); else held.PerformClick(); };
                named.Cursor = Cursors.Hand;
                named.Click += delegate { opens.PerformClick(); };
                list.Controls.Add(opens);
                return y + 28;
            }

            internal void Refill() { if (!IsDisposed) Fill(); }

            // A group's own sliders (the screensaver's wait, a mod's) sit behind its >: a row of the group's name and a lit
            // settings key; the sliders show on the right when it's picked.
            private readonly Dictionary<string, OptionsKey> settingsKeys = new Dictionary<string, OptionsKey>(StringComparer.Ordinal);
            private string pickedSettings;
            private List<ToolStripMenuItem> pickedSliders;

            private int SettingsRow(string owner, List<ToolStripMenuItem> items, int y, int wide)
            {
                Label named = new Note { Text = owner, Font = Font, ForeColor = Rose, BackColor = Ground, AutoSize = false, UseMnemonic = false, Bounds = Box(6, y + 3, wide - 44, 18), Cuts = true };
                list.Controls.Add(named);
                OptionsKey opens = new OptionsKey { Text = ">", Font = Font, Bounds = Box(wide - 28, y, 28, 22), Has = true, Picked = owner == pickedSettings };
                opens.AccessibleName = owner + " settings";
                opens.Click += delegate { PickSettings(owner, items); };
                named.Cursor = Cursors.Hand;
                named.Click += delegate { opens.PerformClick(); };
                list.Controls.Add(opens);
                settingsKeys[owner] = opens;
                if (owner == pickedSettings) pickedSliders = items;
                return y + 28;
            }

            private void PickSettings(string owner, List<ToolStripMenuItem> items)
            {
                Pick(null);
                pickedSettings = owner;
                pickedSliders = items;
                foreach (KeyValuePair<string, OptionsKey> one in settingsKeys) { one.Value.Picked = one.Key == owner; one.Value.Invalidate(); }
                FillSide();
            }

            // The item whose settings show on the right, and what is drawn there.
            private ToolStripMenuItem picked;
            private readonly List<Control> side = new List<Control>();

            // Only the right side and the keys' lit one change, with drawing held until both are done, so the list doesn't
            // flash by being built again.
            internal void Pick(ToolStripMenuItem item)
            {
                picked = item != null && item.HasDropDownItems ? item : null;
                sectionsShown = false;
                panelsShown = false;
                pickedSettings = null;
                pickedSliders = null;
                foreach (OptionsKey one in settingsKeys.Values) if (one.Picked) { one.Picked = false; one.Invalidate(); }
                foreach (Control one in list.Controls)
                {
                    OptionsKey key = one as OptionsKey;
                    if (key != null && key.Picked != (key.Item == picked)) { key.Picked = key.Item == picked; key.Invalidate(); }
                }
                if (IsHandleCreated) SendMessage(Handle, 0x000B, IntPtr.Zero, IntPtr.Zero);
                SuspendLayout();
                try { FillSide(); }
                finally
                {
                    ResumeLayout();
                    if (IsHandleCreated) { SendMessage(Handle, 0x000B, new IntPtr(1), IntPtr.Zero); Invalidate(true); }
                }
            }

            private void FillSide()
            {
                rows.RemoveAll(one => side.Contains(one.Key));
                foreach (Control old in side) { Controls.Remove(old); old.Dispose(); }
                side.Clear();
                steppers.RemoveAll(one => one.Value.Key.Parent == null);
                if (sectionsShown) { SectionsSide(); return; }
                if (panelsShown) { PanelsSide(); return; }
                if (pickedSettings != null && pickedSliders != null) { SlidersSide(); return; }
                if (picked == null || !picked.HasDropDownItems) return;
                int y = ListTop, wide = Wide - SideLeft - 16;
                Label heading = new Note { Text = picked.Text.ToUpperInvariant(), Font = Type(10f * Zoom, true), ForeColor = Hot, BackColor = Ground, AutoSize = false, Bounds = Box(SideLeft, y, wide, 22) };
                Controls.Add(heading);
                side.Add(heading);
                y += 32;
                List<ToolStripMenuItem> settings = new List<ToolStripMenuItem>();
                Tick(picked);
                foreach (ToolStripItem one in picked.DropDownItems)
                {
                    ToolStripMenuItem each = one as ToolStripMenuItem;
                    if (each == null) continue;
                    // A mod's switches plugged into this row: its own rows, not a switch of its group's name.
                    if (Equals(each.Tag, GroupTag)) { Tick(each); foreach (ToolStripItem sub in each.DropDownItems) { ToolStripMenuItem inner = sub as ToolStripMenuItem; if (inner != null) settings.Add(inner); } }
                    else settings.Add(each);
                }
                foreach (ToolStripMenuItem item in settings)
                {
                    ToolStripMenuItem held = item;
                    string slider = SliderOf(item);
                    if (slider != null)
                    {
                        if (!steppers.Exists(one => one.Key == slider && one.Value.Key.Parent == this)) y = StepsRow(slider, settings, y, wide, this, SideLeft, settings.Exists(one => SliderOf(one) == null));
                        continue;
                    }
                    if (Equals(item.Tag, TextTag) || Equals(item.Tag, StatusTag)) { y = WordsRow(item, y, wide); continue; }
                    string pick = item.Tag as string;
                    if (pick != null && pick.StartsWith(PickTag, StringComparison.Ordinal)) { y = PickRow(item, pick.Substring(PickTag.Length), y, wide); continue; }
                    Toggle row = new Toggle { Text = item.Text, Font = Font, Bounds = Box(SideLeft, y, wide, 22), Checked = item.Checked };
                    row.AccessibleName = picked.Text + ", " + item.Text;
                    row.CheckedChanged += delegate { if (syncing) return; held.PerformClick(); Sync(); };
                    Controls.Add(row);
                    side.Add(row);
                    rows.Add(new KeyValuePair<Toggle, ToolStripMenuItem>(row, item));
                    y += 28;
                }
            }

            // A name and a lit key in the ON switches' blue; the key's tip is what it holds.
            private ToolTip pickTips;

            private int PickRow(ToolStripMenuItem item, string words, int y, int wide)
            {
                ToolStripMenuItem held = item;
                int keyWide = Math.Min(150, wide / 2);
                Label named = new Note { Text = item.Text, Font = Font, ForeColor = Rose, BackColor = Ground, AutoSize = false, UseMnemonic = false, Bounds = Box(SideLeft, y + 3, wide - keyWide - 8, 18) };
                Controls.Add(named);
                side.Add(named);
                Key key = new Key { Text = words, Font = Font, Picked = true, Bounds = Box(SideLeft + wide - keyWide, y, keyWide, 22) };
                key.AccessibleName = item.Text + ", " + words + (string.IsNullOrEmpty(item.ToolTipText) ? "" : ": " + item.ToolTipText);
                if (!string.IsNullOrEmpty(item.ToolTipText)) { if (pickTips == null) pickTips = WorkPaint.Tips(delegate { return Font; }); pickTips.SetToolTip(key, item.ToolTipText); }
                // Made again after the click, so its tip shows what was picked.
                key.Click += delegate { held.PerformClick(); if (!IsDisposed) BeginInvoke(new Action(Fill)); };
                Controls.Add(key);
                side.Add(key);
                return y + 28;
            }

            // A group's own sliders on the right, under its name: her wait as one slider, each of a mod's as its own.
            private void SlidersSide()
            {
                int y = ListTop, wide = Wide - SideLeft - 16;
                Label heading = new Note { Text = pickedSettings.ToUpperInvariant(), Font = Type(10f * Zoom, true), ForeColor = Hot, BackColor = Ground, AutoSize = false, Bounds = Box(SideLeft, y, wide, 22), Cuts = true };
                Controls.Add(heading);
                side.Add(heading);
                y += 32;
                bool waited = false;
                foreach (ToolStripMenuItem item in pickedSliders)
                {
                    if (IsWait(item)) { if (!waited) { waited = true; y = WaitsRow(pickedSliders, y, wide, this, SideLeft); } continue; }
                    string slider = SliderOf(item);
                    if (slider != null && !steppers.Exists(one => one.Key == slider && one.Value.Key.Parent == this)) y = StepsRow(slider, pickedSliders, y, wide, this, SideLeft);
                }
            }

            // A setting that is only words: wrapped to the side's width, or the status box, its words in a lit frame like a
            // dashboard light. Both in her palette, so they follow Mainframe colours.
            private int WordsRow(ToolStripMenuItem item, int y, int wide)
            {
                bool status = Equals(item.Tag, StatusTag);
                Size need = TextRenderer.MeasureText(item.Text ?? "", Font, new Size(Z(wide), 0), TextFormatFlags.WordBreak);
                int tall = status ? 24 : Math.Max(18, (int)Math.Ceiling(need.Height / Zoom) + 2);
                Label words = status ? StatusBox(item.Text, Font, Box(SideLeft, y, Math.Min(wide, 170), tall))
                    : new Note { Text = item.Text, Font = Font, ForeColor = Faint, BackColor = Ground, AutoSize = false, UseMnemonic = false, Bounds = Box(SideLeft, y, wide, tall) };
                words.AccessibleName = item.Text + (string.IsNullOrEmpty(item.ToolTipText) ? "" : ". " + item.ToolTipText);
                if (status && !string.IsNullOrEmpty(item.ToolTipText)) { if (pickTips == null) pickTips = WorkPaint.Tips(delegate { return Font; }); pickTips.SetToolTip(words, item.ToolTipText); }
                Controls.Add(words);
                side.Add(words);
                return y + tall + (status ? 10 : 6);
            }

            // Every row from its item, after the item's own menu has ticked it.
            internal void Sync()
            {
                syncing = true;
                try
                {
                    Groups();
                    foreach (KeyValuePair<Toggle, ToolStripMenuItem> row in rows) if (row.Key.Checked != row.Value.Checked) row.Key.Checked = row.Value.Checked;
                    if (waits != null && waits.Value != WaitNow()) waits.Value = WaitNow();
                    foreach (KeyValuePair<string, KeyValuePair<Slider, List<ToolStripMenuItem>>> one in steppers)
                    {
                        int at = one.Value.Value.FindIndex(item => item.Checked);
                        if (at >= 0 && one.Value.Key.Value != at) one.Value.Key.Value = at;
                    }
                }
                finally { syncing = false; }
            }

            // A mod's slider rows: its name, its slider and the items behind each step.
            private readonly List<KeyValuePair<string, KeyValuePair<Slider, List<ToolStripMenuItem>>>> steppers = new List<KeyValuePair<string, KeyValuePair<Slider, List<ToolStripMenuItem>>>>();

            private static string SliderOf(ToolStripMenuItem item)
            {
                string tag = item.Tag as string;
                if (tag == null || !tag.StartsWith(StepTag, StringComparison.Ordinal)) return null;
                int cut = tag.LastIndexOf(':');
                return cut > StepTag.Length ? tag.Substring(StepTag.Length, cut - StepTag.Length) : null;
            }

            // Beside switches on a side (the Tamagotchi's), its name starts where theirs do and the slider spans their OFF and ON.
            private int StepsRow(string slider, List<ToolStripMenuItem> items, int y, int wide, Control into, int left, bool besideSwitches = false)
            {
                List<ToolStripMenuItem> mine = items.FindAll(item => SliderOf(item) == slider);
                List<string> steps = new List<string>();
                foreach (ToolStripMenuItem item in mine) steps.Add(item.Text);
                bool lined = besideSwitches && into != list;
                Label named = new Note { Text = slider, Font = Font, ForeColor = White, BackColor = Ground, AutoSize = false, Bounds = lined ? Box(left, y + 2, wide - Toggle.Full - 10, 20) : Box(left + 6, y + 2, 96, 20), Flush = lined };
                into.Controls.Add(named);
                if (into != list) side.Add(named);
                // In the list it starts where the switches' OFF halves do and spans to the settings keys' right edge.
                Slider row = new Slider(steps.ToArray()) { Font = Font, Bounds = into == list ? Box(SwitchAt(wide), y, wide - SwitchAt(wide), 22) : lined ? Box(left + wide - Toggle.Full, y, Toggle.Full, 22) : Box(left + 108, y, wide - 108, 22) };
                string tip = mine.Count > 0 ? mine[0].ToolTipText : null;
                if (!string.IsNullOrEmpty(tip))
                {
                    if (pickTips == null) pickTips = WorkPaint.Tips(delegate { return Font; });
                    pickTips.SetToolTip(row, tip);
                    pickTips.SetToolTip(named, tip);
                }
                row.AccessibleName = slider;
                row.Value = Math.Max(0, mine.FindIndex(item => item.Checked));
                row.Changed += delegate
                {
                    if (syncing || row.Value < 0 || row.Value >= mine.Count) return;
                    mine[row.Value].PerformClick();
                    Sync();
                };
                into.Controls.Add(row);
                if (into != list) side.Add(row);
                steppers.Add(new KeyValuePair<string, KeyValuePair<Slider, List<ToolStripMenuItem>>>(slider, new KeyValuePair<Slider, List<ToolStripMenuItem>>(row, mine)));
                return y + 30;
            }

            private static bool IsWait(ToolStripMenuItem item) { string tag = item.Tag as string; return tag != null && tag.StartsWith(WaitTag, StringComparison.Ordinal); }

            private static double Minutes(ToolStripMenuItem item) { return double.Parse(((string)item.Tag).Substring(WaitTag.Length), System.Globalization.CultureInfo.InvariantCulture); }

            // One row: its name, then a slider over every wait her menu has, each step named as there ("5 min").
            // Where the switches' OFF halves start: a slider starts there and runs to the keys' right edge.
            private static int SwitchAt(int wide) { return wide - 30 - Toggle.Narrow; }

            private int WaitsRow(List<ToolStripMenuItem> items, int y, int wide, Control into, int left)
            {
                waitItems.Clear();
                foreach (ToolStripMenuItem item in items) if (IsWait(item)) waitItems.Add(item);
                List<string> steps = new List<string>();
                foreach (ToolStripMenuItem item in waitItems) steps.Add(item.Text.StartsWith(WaitName + " ", StringComparison.Ordinal) ? item.Text.Substring(WaitName.Length + 1) : item.Text);
                Label name = new Note { Text = WaitName, Font = Font, ForeColor = White, BackColor = Ground, AutoSize = false, Bounds = Box(left + 6, y + 2, 96, 20) };
                into.Controls.Add(name);
                if (into != list) side.Add(name);
                waits = new Slider(steps.ToArray()) { Font = Font, Bounds = Box(left + 108, y, wide - 108, 22) };
                waits.AccessibleName = "Screensaver: after how long alone";
                waits.Value = WaitNow();
                waits.Changed += delegate
                {
                    if (syncing || waits.Value < 0 || waits.Value >= waitItems.Count) return;
                    waitItems[waits.Value].PerformClick();
                    Sync();
                };
                into.Controls.Add(waits);
                if (into != list) side.Add(waits);
                return y + 30;
            }

            // The step her wait is on: the ticked one, or the nearest, so a wait set elsewhere keeps its value until moved.
            private int WaitNow()
            {
                int best = 0;
                double now = Frame.capri.SaverAfterMinutes, gap = double.MaxValue;
                for (int i = 0; i < waitItems.Count; i++)
                {
                    if (waitItems[i].Checked) return i;
                    double off = Math.Abs(Minutes(waitItems[i]) - now);
                    if (off < gap) { gap = off; best = i; }
                }
                return best;
            }

            private static IEnumerable<Control> Each(Control.ControlCollection controls) { foreach (Control one in controls) yield return one; }

            // What it shows, for the checks: each fold and, open, its rows with their state.
            internal string Shown
            {
                get
                {
                    List<string> said = new List<string>();
                    foreach (Control one in Each(list.Controls))
                    {
                        FoldHead head = one as FoldHead;
                        Toggle row = one as Toggle;
                        if (head != null) said.Add("[" + head.Text + "]");
                        else if (row != null) said.Add(row.Text + (row.Checked ? " on" : " off"));
                        else if (one == waits) said.Add(WaitName + ": " + waits.Shown);
                        else foreach (KeyValuePair<string, KeyValuePair<Slider, List<ToolStripMenuItem>>> step in steppers) if (one == step.Value.Key) said.Add(step.Key + ": " + step.Value.Key.Shown);
                    }
                    if (pickedSettings != null)
                    {
                        said.Add("{" + pickedSettings + "}");
                        foreach (Control one in side)
                        {
                            if (one == waits) said.Add(WaitName + ": " + waits.Shown);
                            else foreach (KeyValuePair<string, KeyValuePair<Slider, List<ToolStripMenuItem>>> step in steppers) if (one == step.Value.Key) said.Add(step.Key + ": " + step.Value.Key.Shown);
                        }
                    }
                    if (picked != null)
                    {
                        said.Add("{" + picked.Text + "}");
                        foreach (Control one in side)
                        {
                            Toggle row = one as Toggle;
                            Key key = one as Key;
                            if (row != null) said.Add(row.Text + (row.Checked ? " on" : " off"));
                            else if (key != null) said.Add("[" + key.AccessibleName + "]");
                            else foreach (KeyValuePair<string, KeyValuePair<Slider, List<ToolStripMenuItem>>> step in steppers) if (one == step.Value.Key) said.Add(step.Key + ": " + step.Value.Key.Shown);
                        }
                    }
                    return string.Join(" | ", said.ToArray());
                }
            }

            internal void Press(string text)
            {
                foreach (KeyValuePair<Toggle, ToolStripMenuItem> row in rows) if (row.Key.Text == text) { row.Key.Checked = !row.Key.Checked; return; }
                // A key in the picked row's side, by its words.
                foreach (Control one in side) { Key key = one as Key; if (key != null && key.Text == text) { key.PerformClick(); return; } }
                // "After 5 min" moves the slider to that step.
                for (int i = 0; i < waitItems.Count; i++) if (waitItems[i].Text == text && waits != null) { waits.Slide(i); return; }
                // "<slider>: <step>" moves a mod's slider to that step.
                foreach (KeyValuePair<string, KeyValuePair<Slider, List<ToolStripMenuItem>>> step in steppers)
                    for (int i = 0; i < step.Value.Value.Count; i++)
                        if (text == step.Key + ": " + step.Value.Value[i].Text) { step.Value.Key.Slide(i); return; }
            }

            internal void Unfold(string group) { open.Add(group); Fill(); }

            // For the checks: a group's own sliders opened on the right by its settings key.
            internal bool OpenSettings(string owner)
            {
                OptionsKey key;
                if (!settingsKeys.TryGetValue(owner, out key)) return false;
                key.PerformClick();
                return true;
            }

            // For the checks: the column's place, the switches' pair width, and every name cut short in the list (which rolls).
            internal string ColumnShown
            {
                get
                {
                    List<string> said = new List<string> { "list " + list.Left + "," + list.Width, "bar " + bar.Left };
                    foreach (Control one in list.Controls)
                    {
                        Toggle row = one as Toggle;
                        if (row != null) said.Add("pair " + row.Pair);
                    }
                    return string.Join(" | ", said.ToArray());
                }
            }

            // For the checks: the row named, picked by pressing its own settings key, after asking the window what is under it
            // so a key that lets clicks through fails; and every switch the same width, so a settings key can't push one out.
            internal void PickRow(string text)
            {
                int wide = -1, edge = -1;
                foreach (Control one in list.Controls)
                    if (one is OptionsKey) edge = one.Right;
                foreach (Control one in list.Controls)
                    if (one is Slider && edge > 0 && Math.Abs(one.Right - edge) > 1)
                        throw new Exception("A slider in Toggles runs to " + one.Right + ", not where the rows' > keys stop at " + edge + ".");
                int offAt = -1;
                foreach (Control one in list.Controls)
                    if (one is Toggle) offAt = one.Right - Z(Toggle.Narrow);
                foreach (Control one in list.Controls)
                    if (one is Slider && offAt > 0 && Math.Abs(one.Left - offAt) > 1)
                        throw new Exception("A slider in Toggles starts at " + one.Left + ", not where the switches' OFF halves start at " + offAt + ".");
                foreach (Control one in list.Controls)
                    if (one is Toggle)
                    {
                        if (wide >= 0 && one.Width != wide) throw new Exception("The Toggles switches aren't one width: " + one.Text + " is " + one.Width + ", not " + wide + ".");
                        wide = one.Width;
                    }
                foreach (Control one in list.Controls)
                {
                    OptionsKey opens = one as OptionsKey;
                    if (opens == null || opens.AccessibleName != text + " settings") continue;
                    Point middle = opens.PointToScreen(new Point(opens.Width / 2, opens.Height / 2));
                    IntPtr under = SendMessage(opens.Handle, 0x0084, IntPtr.Zero, (IntPtr)((middle.Y << 16) | (middle.X & 0xFFFF)));
                    if (under == new IntPtr(-1)) throw new Exception(text + "'s settings key lets a click through to what is under it.");
                    if (!opens.Enabled) throw new Exception(text + "'s settings key can't be pressed.");
                    opens.PerformClick();
                    return;
                }
                throw new Exception("No settings key on " + text + "'s row.");
            }

            [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wide, IntPtr high);

            internal bool ClickName(string text)
            {
                foreach (Control one in list.Controls)
                {
                    Toggle row = one as Toggle;
                    if (row == null || row.Text != text) continue;
                    bool was = row.Checked;
                    InvokeOnClick(row, new MouseEventArgs(MouseButtons.Left, 1, 3, row.Height / 2, 0));
                    return row.Checked == was;
                }
                return false;
            }

            // For the checks: every fold open, so the list is longer than its room.
            internal void UnfoldAll() { foreach (KeyValuePair<string, List<ToolStripMenuItem>> group in Groups()) open.Add(group.Key); Fill(); }

            // For the checks: a fold's rows by their words, top to bottom, "name>" where its settings key is lit; and its
            // settings key pressed by the row's words.
            internal string FoldRows(string fold)
            {
                List<Control> parts = new List<Control>(Each(list.Controls));
                parts.Sort(delegate(Control a, Control b) { return a.Top != b.Top ? a.Top.CompareTo(b.Top) : a.Left.CompareTo(b.Left); });
                List<string> rows = new List<string>();
                bool inside = false;
                foreach (Control one in parts)
                {
                    FoldHead head = one as FoldHead;
                    if (head != null) { inside = head.Text == fold.ToUpperInvariant(); continue; }
                    if (!inside) continue;
                    OptionsKey key = one as OptionsKey;
                    if (key != null) { if (key.Enabled && rows.Count > 0) rows[rows.Count - 1] += ">"; continue; }
                    if ((one is Toggle || one is Label) && one.Text.Length > 0) rows.Add(one.Text);
                }
                return string.Join(" | ", rows.ToArray());
            }

            internal bool OpenRowSettings(string words)
            {
                foreach (Control one in Each(list.Controls))
                {
                    OptionsKey key = one as OptionsKey;
                    if (key == null || !key.Enabled || key.AccessibleName != words + " settings") continue;
                    key.PerformClick();
                    return pickedSettings != null;
                }
                return false;
            }

            internal bool Overflows { get { return list.DisplayRectangle.Height > list.ClientSize.Height; } }

#if CHECKS
            // For the checks: every switch, key and slider in the list and behind each key, pressed and put back, with what
            // watch reads before and after each. A press that changes it is named; keys allowed to open a window say so.
            internal List<string> PressAll(Func<string> watch, Func<bool> tidy)
            {
                List<string> wrong = new List<string>();
                UnfoldAll();
                Action<string, Action, Action> press = delegate(string what, Action doIt, Action undo)
                {
                    string before = watch();
                    doIt();
                    string after = watch();
                    if (undo != null) undo();
                    if (after != before) wrong.Add(what + ": " + before + " became " + after);
                    tidy();
                };
                Action sideOnes = delegate
                {
                    foreach (Control one in new List<Control>(side))
                    {
                        Toggle row = one as Toggle;
                        if (row != null && !row.IsDisposed) { Toggle held = row; press("its switch " + held.AccessibleName, delegate { held.Checked = !held.Checked; }, delegate { held.Checked = !held.Checked; }); continue; }
                        Slider slide = one as Slider;
                        if (slide != null && !slide.IsDisposed) { Slider held = slide; int was = held.Value; press("its slider " + held.AccessibleName, delegate { held.Choose(was == 0 ? 1 : 0); }, delegate { held.Choose(was); }); }
                    }
                };
                foreach (Control one in new List<Control>(Each(list.Controls)))
                {
                    if (one.IsDisposed) continue;
                    Toggle row = one as Toggle;
                    if (row != null) { Toggle held = row; press(held.Text, delegate { held.Checked = !held.Checked; }, delegate { held.Checked = !held.Checked; }); continue; }
                    Slider slide = one as Slider;
                    if (slide != null) { Slider held = slide; int was = held.Value; press(held.AccessibleName, delegate { held.Choose(was == 0 ? 1 : 0); }, delegate { held.Choose(was); }); continue; }
                    OptionsKey key = one as OptionsKey;
                    if (key == null || !key.Enabled || !key.Has) continue;
                    OptionsKey pressed = key;
                    press(key.AccessibleName, delegate { pressed.PerformClick(); }, null);
                    sideOnes();
                }
                Pick(null);
                return wrong;
            }

            // For the checks: the Tamagotchi's row and what its key shows: its slider names and its switches.
            internal string PetRow()
            {
                foreach (Control one in Each(list.Controls))
                {
                    OptionsKey key = one as OptionsKey;
                    if (key == null || key.Item == null || key.Item.Name != SideRow) continue;
                    key.PerformClick();
                    List<string> shown = new List<string>();
                    foreach (Control part in side) if (part is Slider || part is Toggle) shown.Add(part is Slider ? "slider " + part.AccessibleName + " " + ((Slider)part).Value : "switch " + part.Text);
                    Pick(null);
                    return string.Join(", ", shown.ToArray());
                }
                return null;
            }
#endif

            internal Control.ControlCollection ListControls { get { return list.Controls; } }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                LeftColumn.Frame(e.Graphics, list, Zoom);
            }

            // Where a Windows bar would sit: the list's right edge on the screen.
            internal Rectangle EdgeOnScreen { get { Rectangle at = RectangleToScreen(list.Bounds); return new Rectangle(at.Right - Z(10), at.Top, Z(10), at.Height); } }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BarInfo { internal int Size, Left, Top, Right, Bottom, Line, Thumb, ThumbEnd, Reserved, Bar, Up, Track, Page, Down, Thumb2; }

        [DllImport("user32.dll")] private static extern bool GetScrollBarInfo(IntPtr window, int which, ref BarInfo info);

        // Toggles with every fold open, Her GIFs and Her memory: no control in them has Windows' scrollbar, only her slim
        // bar beside each list. Null when right.
        internal string WindowsBars()
        {
            List<string> found = new List<string>();
            Action<Control, string> look = null;
            look = delegate(Control control, string where)
            {
                if (control.IsHandleCreated && control.Visible && (Native.GetWindowLong(control.Handle, -16) & 0x00300000) != 0)
                {
                    // The style asks for a bar; whether Windows draws one is what its own scroll bar info says.
                    BarInfo info = new BarInfo { Size = Marshal.SizeOf(typeof(BarInfo)) };
                    bool drawn = GetScrollBarInfo(control.Handle, unchecked((int)0xFFFFFFFB), ref info) && (info.Bar & 0x8000) == 0 && info.Right > info.Left;
                    if (drawn) found.Add(where + ": " + control.GetType().Name + " " + control.Bounds + " draws it");
                }
                foreach (Control inside in control.Controls) look(inside, where);
            };
            bool overflowed = false;
            try
            {
                OpenToggles();
                toggleSheet.UnfoldAll();
                Application.DoEvents();
                overflowed = toggleSheet.Overflows;
                look(toggleSheet, "Toggles");
                // And by eye: a Windows bar is light down that edge, her list is dark there.
                Rectangle edge = toggleSheet.EdgeOnScreen;
                using (Bitmap seen = new Bitmap(edge.Width, edge.Height))
                {
                    using (Graphics copy = Graphics.FromImage(seen)) copy.CopyFromScreen(edge.Location, Point.Empty, edge.Size);
                    int light = 0;
                    for (int y = 0; y < seen.Height; y++) for (int x = 0; x < seen.Width; x++) { Color one = seen.GetPixel(x, y); if (one.R + one.G + one.B > 540) light++; }
                    if (light * 3 > seen.Width * seen.Height) found.Add("Toggles: its list's right edge is light, as a Windows bar is (" + light + " of " + seen.Width * seen.Height + " pixels)");
                }
                CloseToggles();
                OpenGifs();
                Application.DoEvents();
                look(gifSheet, "Her GIFs");
                CloseGifs();
                OpenMemory();
                Application.DoEvents();
                look(memorySheet, "Her memory");
                if (memorySheet != null && !memorySheet.IsDisposed) memorySheet.Close();
            }
            finally
            {
                CloseToggles();
                CloseGifs();
                if (memorySheet != null && !memorySheet.IsDisposed) memorySheet.Close();
            }
            if (!overflowed) found.Add("Toggles with every fold open didn't overflow its list, so a scrollbar wasn't tested");
            return found.Count == 0 ? null : string.Join("; ", found.ToArray());
        }

        // A row's settings key in Toggles: the ON switch's resting colours where the row has settings (lit when they are
        // the ones showing), the OFF switch's dark resting red where it has none. Every colour from her palette.
        private sealed class OptionsKey : Button
        {
            internal bool Has, Picked;
            internal ToolStripMenuItem Item;
            private bool over;

            internal OptionsKey()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
                BackColor = Ground;
                TabStop = false;
                Cursor = Cursors.Hand;
            }

            protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; base.OnEnabledChanged(e); }
            protected override void OnMouseEnter(EventArgs e) { over = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { over = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                float s = ScaleOf(this), line = Math.Max(1f, s);
                Graphics graphics = e.Graphics;
                Backdrop(graphics, this, s);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Color fill, rim, ink;
                if (!Has) { fill = Neon.Tone(32, 12, 19); rim = Neon.Tone(50, 18, 28); ink = Neon.Tone(84, 26, 38); }
                else if (Picked) { fill = rim = Cyan; ink = OnInk; }
                else
                {
                    fill = over ? Neon.Tone(12, 46, 50) : Neon.Tone(9, 30, 33);
                    rim = over ? DeepEdge : Neon.Tone(16, 44, 48);
                    ink = over ? Neon.Tone(110, 210, 216) : Neon.Tone(28, 68, 72);
                }
                if (Neon.Mono)
                {
                    // One-colour looks have no red, so no settings is the faintest outline and ink, settings a clearer one.
                    fill = Has && Picked ? Cyan : Field;
                    rim = Has ? (Picked ? Cyan : over ? White : Faint) : Field;
                    ink = Has ? (Picked ? OnInk : over ? White : Faint) : Brim;
                }
                RectangleF plate = new RectangleF(line / 2, line / 2, Width - line, Height - line);
                using (GraphicsPath path = Cut(plate, 0, 0, 5 * s, 0))
                {
                    using (Brush brush = new SolidBrush(fill)) graphics.FillPath(brush, path);
                    using (Pen pen = new Pen(rim, line)) graphics.DrawPath(pen, path);
                }
                TextRenderer.DrawText(graphics, Text, Font, ClientRectangle, ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
        }

    }
}
