using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Drawing;
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
            if (toggleSheet == null || toggleSheet.IsDisposed) toggleSheet = new ToggleSheet(this, zoom, capri.TogglesMenu);
            if (!toggleSheet.Visible) toggleSheet.Show(this);
            toggleSheet.Activate();
        }

        internal void CloseToggles() { if (toggleSheet != null && !toggleSheet.IsDisposed) toggleSheet.Close(); }

        // For the checks: what the open window shows, a row pressed, a fold opened.
        internal string TogglesText { get { return toggleSheet != null && !toggleSheet.IsDisposed && toggleSheet.Visible ? toggleSheet.Shown : null; } }

        internal void PressToggle(string text) { if (toggleSheet != null && !toggleSheet.IsDisposed) toggleSheet.Press(text); }

        internal void UnfoldToggles(string group) { if (toggleSheet != null && !toggleSheet.IsDisposed) toggleSheet.Unfold(group); }

        private sealed class ToggleSheet : ModSheet
        {
            private const int Wide = 420, Tall = 560, ListTop = 64;
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
                // Her own slim bar beside the list, not Windows' one.
                list = new ScrollPanel { Bounds = Box(16, ListTop, Wide - 42, Tall - ListTop - 16), BackColor = Ground };
                list.Scrolled += delegate { bar.Invalidate(); };
                Controls.Add(list);
                bar.Bounds = new Rectangle(Z(Wide - 22), Z(ListTop), Z(SlimBar.Thin), Z(Tall - ListTop - 16));
                bar.BackColor = Ground;
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
                    if (!item.HasDropDownItems) { plain.Add(item); continue; }
                    Tick(item);
                    List<ToolStripMenuItem> inside = new List<ToolStripMenuItem>();
                    foreach (ToolStripItem sub in item.DropDownItems) { ToolStripMenuItem each = sub as ToolStripMenuItem; if (each != null && !each.HasDropDownItems) inside.Add(each); }
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
                list.AutoScrollPosition = Point.Empty;
                int y = 0, wide = Wide - 32 - 22;
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
                    foreach (ToolStripMenuItem item in group.Value)
                    {
                        ToolStripMenuItem held = item;
                        if (IsWait(item))
                        {
                            if (waits == null) y = WaitsRow(group.Value, y, wide);
                            continue;
                        }
                        Toggle row = new Toggle { Text = item.Text, Font = Font, Bounds = Box(6, y, wide - 6, 22), Checked = item.Checked };
                        row.AccessibleName = item.Text + (string.IsNullOrEmpty(item.ToolTipText) ? "" : ". " + item.ToolTipText);
                        row.CheckedChanged += delegate { if (syncing) return; held.PerformClick(); Sync(); };
                        list.Controls.Add(row);
                        rows.Add(new KeyValuePair<Toggle, ToolStripMenuItem>(row, item));
                        y += 28;
                    }
                    y += 6;
                }
                list.ResumeLayout();
                list.AutoScrollPosition = new Point(0, scrolled);
                list.Moved();
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
                }
                finally { syncing = false; }
            }

            private static bool IsWait(ToolStripMenuItem item) { string tag = item.Tag as string; return tag != null && tag.StartsWith(WaitTag, StringComparison.Ordinal); }

            private static double Minutes(ToolStripMenuItem item) { return double.Parse(((string)item.Tag).Substring(WaitTag.Length), System.Globalization.CultureInfo.InvariantCulture); }

            // One row: its name, then a slider over every wait her menu has, each step named as there ("5 min").
            private int WaitsRow(List<ToolStripMenuItem> items, int y, int wide)
            {
                foreach (ToolStripMenuItem item in items) if (IsWait(item)) waitItems.Add(item);
                List<string> steps = new List<string>();
                foreach (ToolStripMenuItem item in waitItems) steps.Add(item.Text.StartsWith(WaitName + " ", StringComparison.Ordinal) ? item.Text.Substring(WaitName.Length + 1) : item.Text);
                Label name = new Note { Text = WaitName, Font = Font, ForeColor = White, BackColor = Ground, AutoSize = false, Bounds = Box(6, y + 2, 66, 20) };
                list.Controls.Add(name);
                waits = new Slider(steps.ToArray()) { Font = Font, Bounds = Box(80, y, wide - 80, 22) };
                waits.AccessibleName = "Screensaver: after how long alone";
                waits.Value = WaitNow();
                waits.Changed += delegate
                {
                    if (syncing || waits.Value < 0 || waits.Value >= waitItems.Count) return;
                    waitItems[waits.Value].PerformClick();
                    Sync();
                };
                list.Controls.Add(waits);
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
                    }
                    return string.Join(" | ", said.ToArray());
                }
            }

            internal void Press(string text)
            {
                foreach (KeyValuePair<Toggle, ToolStripMenuItem> row in rows) if (row.Key.Text == text) { row.Key.Checked = !row.Key.Checked; return; }
                // "After 5 min" moves the slider to that step.
                for (int i = 0; i < waitItems.Count; i++) if (waitItems[i].Text == text && waits != null) { waits.Slide(i); return; }
            }

            internal void Unfold(string group) { open.Add(group); Fill(); }

            // For the checks: every fold open, so the list is longer than its room.
            internal void UnfoldAll() { foreach (KeyValuePair<string, List<ToolStripMenuItem>> group in Groups()) open.Add(group.Key); Fill(); }

            internal bool Overflows { get { return list.DisplayRectangle.Height > list.ClientSize.Height; } }

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
    }
}
