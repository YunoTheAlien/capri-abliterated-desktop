using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Her right-click menu near a screen's edge stays on the screen, its submenus opening left when there isn't room
    // right. Her window comes forward first so a click elsewhere closes it; her Yes and No asks give up after ten
    // seconds.
    internal sealed partial class Companion
    {
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, int message, IntPtr wide, IntPtr data);
        private const int NullMessage = 0x0000;

        // How long any Yes and No waits in her bubble, the jealous offer included, at most 10 s.
        internal const double ChoiceMost = 10;

        // Her menu at a point: inside the screen it is on, with room to its right for the deepest run of submenus.
        private void ShowMenu(ContextMenuStrip strip, Point at)
        {
            Point placed = MenuSpot(strip, at, Screen.FromPoint(at).WorkingArea);
            // In front first, then the menu, then a message so Windows lets it close on a click anywhere else.
            if (IsHandleCreated) SetForegroundWindow(Handle);
            strip.Show(placed);
            if (IsHandleCreated) PostMessage(Handle, NullMessage, IntPtr.Zero, IntPtr.Zero);
        }

        // Where a menu goes: at the point, kept inside the area, raised so it fits above the bottom. Its submenus find their
        // own side as they open (Sided).
        internal static Point MenuSpot(ToolStripDropDown strip, Point at, Rectangle area)
        {
            Size size = strip.GetPreferredSize(Size.Empty);
            int x = at.X, y = at.Y;
            if (x + size.Width > area.Right) x = area.Right - size.Width;
            if (y + size.Height > area.Bottom) y = area.Bottom - size.Height;
            return new Point(Math.Max(area.Left, x), Math.Max(area.Top, y));
        }

        private readonly HashSet<ToolStripMenuItem> sided = new HashSet<ToolStripMenuItem>();

        // A submenu's side, decided as it opens, and kept on its screen once open and whenever it moves or grows.
        private void Sided(ToolStripMenuItem parent)
        {
            if (parent == null || !sided.Add(parent)) return;
            parent.DropDownOpening += delegate { parent.DropDownDirection = SideFits(parent) ? ToolStripDropDownDirection.Left : ToolStripDropDownDirection.Right; };
            parent.DropDown.Opened += delegate { KeptOnScreen(parent); };
            parent.DropDown.SizeChanged += delegate { KeptOnScreen(parent); };
            parent.DropDown.LocationChanged += delegate { KeptOnScreen(parent); };
            parent.DropDown.ItemAdded += delegate(object sender, ToolStripItemEventArgs added) { DressAdded(added.Item); };
            Traced(parent);
        }

        // While her menu is up, each window that comes to the front goes in her log, named, and whether it is one of hers, so a
        // list closed by a focus change says what took it. Unhooked as the menu closes.
        private delegate void WinEventProc(IntPtr hook, uint happened, IntPtr window, int item, int child, uint thread, uint time);
        [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module, WinEventProc proc, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, System.Text.StringBuilder text, int most);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, System.Text.StringBuilder text, int most);
        private const uint ForegroundChanged = 3;
        private static WinEventProc menuFrontProc;
        private static IntPtr menuFrontHook;

        internal void TraceFront(ToolStripDropDown strip)
        {
            strip.Opened += delegate
            {
                if (menuFrontHook != IntPtr.Zero) return;
                menuFrontProc = delegate(IntPtr hook, uint happened, IntPtr window, int item, int child, uint thread, uint time) { Brain.Log("menu trace", -1, "in front now: " + WindowNamed(window)); };
                menuFrontHook = SetWinEventHook(ForegroundChanged, ForegroundChanged, IntPtr.Zero, menuFrontProc, 0, 0, 0);
            };
            strip.Closing += delegate(object sender, ToolStripDropDownClosingEventArgs e) { Brain.Log("menu trace", -1, "her menu closing because " + e.CloseReason + (e.CloseReason == ToolStripDropDownCloseReason.AppFocusChange ? "; in front: " + WindowNamed(GetForegroundWindow()) : "")); };
            strip.Closed += delegate { if (menuFrontHook != IntPtr.Zero) UnhookWinEvent(menuFrontHook); menuFrontHook = IntPtr.Zero; };
        }

        // A window by its title, class and program, and which of her own windows it is when it's hers.
        internal static string WindowNamed(IntPtr window)
        {
            if (window == IntPtr.Zero) return "none";
            System.Text.StringBuilder title = new System.Text.StringBuilder(256), kind = new System.Text.StringBuilder(256);
            GetWindowText(window, title, title.Capacity);
            GetClassName(window, kind, kind.Capacity);
            int process;
            GetWindowThreadProcessId(window, out process);
            string program = "?";
            try { using (System.Diagnostics.Process owner = System.Diagnostics.Process.GetProcessById(process)) program = owner.ProcessName; }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            Control hers = Control.FromHandle(window);
            return "\"" + title + "\" [" + kind + "] " + program + (hers != null ? " hers: " + hers.GetType().Name + (hers.Name.Length > 0 ? " " + hers.Name : "") : "");
        }

        // Each submenu's opening and closing, in her log with the reason Windows gives for a close, so one that opens and
        // shuts at once on a real hover says why. For finding the submenus that vanish by hand.
        private static void Traced(ToolStripMenuItem parent)
        {
            ToolStripDropDown drop = parent.DropDown;
            Func<string> where = delegate { return parent.Text + " " + drop.Bounds + (drop.Visible ? " shown" : " hidden") + " " + parent.DropDownDirection; };
            parent.DropDownOpening += delegate { Brain.Log("menu trace", -1, "opening " + where()); };
            drop.Opening += delegate(object sender, System.ComponentModel.CancelEventArgs e) { Brain.Log("menu trace", -1, "drop opening " + where() + (e.Cancel ? " cancelled" : "")); };
            drop.Opened += delegate
            {
                ToolStripDropDown under = parent.Owner as ToolStripDropDown;
                bool behind = under != null && drop.IsHandleCreated && under.IsHandleCreated && Behind(drop.Handle, under.Handle);
                string off = RealPlace(parent);
                Brain.Log("menu trace", -1, "opened " + where() + (behind ? " behind its parent" : "") + (off != null ? "; " + off : ""));
            };
            drop.Closing += delegate(object sender, ToolStripDropDownClosingEventArgs e) { Brain.Log("menu trace", -1, "closing " + where() + " because " + e.CloseReason + (e.Cancel ? " cancelled" : "") + (e.CloseReason == ToolStripDropDownCloseReason.AppFocusChange ? "; in front: " + WindowNamed(GetForegroundWindow()) : "")); };
            drop.Closed += delegate(object sender, ToolStripDropDownClosedEventArgs e) { Brain.Log("menu trace", -1, "closed " + parent.Text + " because " + e.CloseReason); };
        }

        // Where Windows really has a submenu, only when that isn't where WinForms thinks or it sits inside another window,
        // as a list dressed late did: open in the right place yet never drawn, or at about twice its spot.
        private static string RealPlace(ToolStripMenuItem parent)
        {
            try
            {
                ToolStripDropDown drop = parent.DropDown, under = parent.Owner as ToolStripDropDown;
                if (!drop.IsHandleCreated) return "no window yet";
                MenuEdges edges;
                MenuWindowRect(drop.Handle, out edges);
                int style = MenuWindowLong(drop.Handle, -16);
                IntPtr above = MenuAncestor(drop.Handle, 1), owner = GetWindow(drop.Handle, 4);
                IntPtr desktop = MenuDesktop();
                if ((style & 0x40000000) == 0 && above == desktop && edges.Left == drop.Left && edges.Top == drop.Top) return null;
                ToolStrip on = parent.GetCurrentParent();
                Rectangle row = under == null ? Rectangle.Empty : new Rectangle(under.PointToScreen(parent.Bounds.Location), parent.Bounds.Size);
                System.Reflection.PropertyInfo due = typeof(ToolStripDropDownItem).GetProperty("DropDownLocation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                return "real {" + edges.Left + "," + edges.Top + "," + (edges.Right - edges.Left) + "," + (edges.Bottom - edges.Top) + "}"
                    + ((style & 0x40000000) != 0 ? " CHILD" : "") + ((style & unchecked((int)0x80000000)) != 0 ? " popup" : "")
                    + ", parent " + (above == desktop ? "desktop" : WindowNamed(above)) + ", owner " + WindowNamed(owner)
                    + ", row " + row + " on " + (on == null ? "nothing" : on == under ? "its menu" : on.GetType().Name + " " + on.Bounds)
                    + ", due at " + (due == null ? "?" : due.GetValue(parent, null));
            }
            catch (Exception error) { return "real place unread: " + error.GetType().Name; }
        }

        private struct MenuEdges { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll", EntryPoint = "GetWindowRect")] private static extern bool MenuWindowRect(IntPtr window, out MenuEdges edges);
        [DllImport("user32.dll", EntryPoint = "GetWindowLong")] private static extern int MenuWindowLong(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "GetAncestor")] private static extern IntPtr MenuAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll", EntryPoint = "GetDesktopWindow")] private static extern IntPtr MenuDesktop();

        // The top menu's side, unless this submenu as wide as it is now would leave the screen and fits the other side.
        internal static bool SideFits(ToolStripMenuItem parent)
        {
            bool left = OpensLeft(parent);
            ToolStripDropDown under = parent.Owner as ToolStripDropDown;
            if (under == null) return left;
            Rectangle area = Screen.FromRectangle(under.Bounds).WorkingArea;
            int wide = parent.DropDown.GetPreferredSize(Size.Empty).Width;
            bool rightFits = under.Right + wide <= area.Right, leftFits = under.Left - wide >= area.Left;
            if (!left && !rightFits && leftFits) return true;
            if (left && !leftFits && rightFits) return false;
            return left;
        }

        // A submenu away from its menu's row or past its screen goes back beside it, inside the screen; one was seen with its
        // parent's corner counted twice. Then it goes above its parent in Windows' order, so a flip never hides it.
        internal static void KeptOnScreen(ToolStripMenuItem parent)
        {
            ToolStripDropDown drop = parent.DropDown, under = parent.Owner as ToolStripDropDown;
            if (drop == null || under == null || !drop.Visible || puttingBack) return;
            Rectangle shown = drop.Bounds, area = Screen.FromRectangle(under.Bounds).WorkingArea;
            int row = under.Top + parent.Bounds.Top;
            string away = !area.Contains(shown) ? "past " + area : !Beside(shown, under.Bounds) ? "not beside" : shown.Top > row + Slack || shown.Bottom < row ? "off its row " + row : null;
            if (away != null)
            {
                ToolStripDropDownDirection way = parent.DropDownDirection;
                bool left = way == ToolStripDropDownDirection.Left || way == ToolStripDropDownDirection.AboveLeft || way == ToolStripDropDownDirection.BelowLeft;
                int x = left ? under.Left - shown.Width : under.Right;
                if (x + shown.Width > area.Right) x = under.Left - shown.Width;
                else if (x < area.Left) x = under.Right;
                x = Math.Max(area.Left, Math.Min(area.Right - shown.Width, x));
                int y = Math.Max(area.Top, Math.Min(area.Bottom - shown.Height, row));
                // Moved as a window, since a Location set here drifted on each reopen; then redrawn, since one moved was
                // seen open but never painted. One already in place is left alone.
                if (drop.IsHandleCreated && (x != shown.X || y != shown.Y))
                {
                    puttingBack = true;
                    try
                    {
                        SetWindowPos(drop.Handle, OnTopOfAll, x, y, 0, 0, KeepSize);
                        RedrawWindow(drop.Handle, IntPtr.Zero, IntPtr.Zero, RedrawAll);
                    }
                    finally { puttingBack = false; }
                    Brain.Log("menu trace", -1, "put back " + parent.Text + " (" + away + ") from " + shown + " to " + drop.Bounds + " beside " + under.Bounds);
                    return;
                }
            }
            if (drop.IsHandleCreated) SetWindowPos(drop.Handle, OnTopOfAll, 0, 0, 0, 0, KeepPlace);
        }

        private static bool puttingBack;

        [DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr window, IntPtr area, IntPtr region, uint flags);
        // Invalidate, erase, frame, all children, now.
        private const uint RedrawAll = 0x0001 | 0x0004 | 0x0400 | 0x0080 | 0x0100;

        // How far a submenu may sit from its menu's edge, or below its row, and still count as beside it at that row.
        private const int Slack = 12;

        internal static bool Beside(Rectangle shown, Rectangle under) { return Math.Abs(shown.Left - under.Right) <= Slack || Math.Abs(shown.Right - under.Left) <= Slack; }

        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int wide, int high, uint flags);
        private static readonly IntPtr OnTopOfAll = new IntPtr(-1);
        private const uint KeepPlace = 0x0001 | 0x0002 | 0x0010, KeepSize = 0x0001 | 0x0010;

        // Every submenu's side is its top menu's: left when the room right of that menu, on its screen, is less than its
        // widest run of submenus.
        internal static bool OpensLeft(ToolStripMenuItem parent)
        {
            ToolStripDropDown top = TopOf(parent);
            return top != null && TopOpensLeft(top);
        }

        internal static bool TopOpensLeft(ToolStripDropDown top)
        {
            Rectangle at = top.Bounds;
            Rectangle area = Screen.FromRectangle(at).WorkingArea;
            return area.Right - at.Right < Beside(top.Items, 0);
        }

        // The menu at the top of a submenu's chain.
        private static ToolStripDropDown TopOf(ToolStripMenuItem parent)
        {
            ToolStripDropDown strip = parent.Owner as ToolStripDropDown;
            for (int depth = 0; strip != null && depth < 8; depth++)
            {
                ToolStripMenuItem above = strip.OwnerItem as ToolStripMenuItem;
                ToolStripDropDown next = above == null ? null : above.Owner as ToolStripDropDown;
                if (next == null) return strip;
                strip = next;
            }
            return strip;
        }

        // The widest run of submenus under these items, each as wide as its own items need.
        private static int Beside(ToolStripItemCollection items, int depth)
        {
            int widest = 0;
            if (depth > 4) return 0;
            foreach (ToolStripItem item in items)
            {
                ToolStripMenuItem holder = item as ToolStripMenuItem;
                if (holder == null || !holder.Available || !holder.HasDropDownItems) continue;
                ToolStripDropDown drop = holder.DropDown;
                int width = drop.GetPreferredSize(Size.Empty).Width + Beside(drop.Items, depth + 1);
                if (width > widest) widest = width;
            }
            return widest;
        }

        // A Yes and No left unanswered: the buttons go and the bubble can close, nothing done. Every ask goes through
        // Choose, so one timer covers all; the site-or-app ask drops its guess, and the jealous offer ends as on No.
        private void ChoiceLapsed()
        {
            if (!box.Choosing || (DateTime.UtcNow - box.ChoiceSince).TotalSeconds < ChoiceMost) return;
            bool site = siteChoice, jealousAsk = jealousOffer != null;
            string asked = box.Reply ?? "";
            box.Choose(null);
            proofAfter = null;
            if (site)
            {
                siteChoice = false;
                Security.Drop();
            }
            if (jealousAsk) EndJealous();
            Brain.Log("ask", -1, "Nothing picked in " + ChoiceMost + " s, so nothing was done: \"" + (asked.Length > 80 ? asked.Substring(0, 80) + "..." : asked) + "\"");
        }

        // ---------- Checks ----------

        // Her menu at the test screen's right and bottom edges at three sizes: kept on the screen, submenus opening
        // left at the right edge, never over their parent. With another window in front hers comes forward; a Yes and
        // No gives up after ten seconds.
        private void SmokeMenuEdges(List<string> checks)
        {
            Rectangle area = Displays.Home.WorkingArea;
            Font fontWas = menu.Font;
            try
            {
                foreach (float size in new[] { 10f, 15f, 20f })
                {
                    MenuFont(Neon.Type(size));
                    foreach (Point corner in new[] { new Point(area.Right - 2, area.Top + 200), new Point(area.Left + 200, area.Bottom - 2), new Point(area.Right - 2, area.Bottom - 2) })
                    {
                        Point spot = MenuSpot(menu, corner, area);
                        Size need = menu.GetPreferredSize(Size.Empty);
                        Rectangle whole = new Rectangle(spot, need);
                        if (!area.Contains(whole) || (corner.X > area.Right - 10 && whole.Right < area.Right - 2))
                            throw new Exception("Her menu at " + corner + " (" + size + " pt) didn't open where it was asked, inside the screen: " + whole + " in " + area);
                    }
                }
                string sides = SubmenusLeft(area);
                if (sides != null) throw new Exception(sides);
                string grown = GrownSubmenu(area);
                if (grown != null) throw new Exception(grown);
                checks.Add("PASS a submenu whose list fills wider after its side was decided, or that lands past the screen's edge as one was seen to, opened where the top menu says right, goes to the left of its menu inside the screen");
                checks.Add("PASS her right-click menu near the right edge stays where it opened, and its submenus, nested ones too, all open to the one side decided from the menu itself: left when the room beside it is less than its widest run of submenus, right otherwise, at three places by the edge");
                checks.Add("PASS her right-click menu opened by the right or bottom edge of a screen, at three sizes, opens inside it with room to its right for every run of submenus, so none opens over it");

                MenuCloses(checks);
                ChoiceTimes(checks);
            }
            finally { MenuFont(fontWas); }
        }

        // Her menu and every list dressed in it at one size, as a change of the screen's scale gives them; changing the
        // menu's alone left its lists at the old size, and a list refilled as it opened grew past the side chosen.
        private void MenuFont(Font font)
        {
            menu.Font = font;
            foreach (ToolStripMenuItem parent in sided) if (parent.DropDown != null) parent.DropDown.Font = font;
        }

        // Her right-click menu near the right edge: just short of the room its submenus need, just past it and at the edge.
        // Each time every submenu, nested ones too, opens to the side decided from the menu itself, inside the screen. Null
        // when they all do.
        private string SubmenusLeft(Rectangle area)
        {
            int wide = menu.GetPreferredSize(Size.Empty).Width, need = Beside(menu.Items, 0);
            foreach (int x in new[] { area.Right - wide - need - 12, area.Right - wide - need + 12, area.Right - 2 })
            {
                string wrong = null;
                bool left = false;
                // Her menu closes when another program comes to the front, as any Windows menu does: closed partway, the walk
                // is done again, twice at most.
                for (int walk = 0; walk < 3; walk++)
                {
                    menu.Show(MenuSpot(menu, new Point(x, area.Top + 40), area));
                    try
                    {
                        for (int wait = 0; wait < 20 && !menu.Visible; wait++) Pump(15);
                        Pump(80);
                        left = TopOpensLeft(menu);
                        int opened = 0;
                        wrong = OpenEach(menu, area, 0, left, ref opened);
                        if (wrong == null && opened == 0) wrong = "no submenu could be opened";
                        if (wrong == null || menu.Visible) break;
                    }
                    finally { menu.Close(); Pump(50); }
                }
                if (wrong != null) return "Her menu at " + x + " (" + (left ? "left" : "right") + " decided from the top) had a submenu go wrong: " + wrong;
            }
            return null;
        }

        // Her menu opening right, with a submenu that fills wider once open and is then pushed past the screen's edge.
        // Null when it ends inside the screen, beside its menu and not over it.
        private string GrownSubmenu(Rectangle area)
        {
            ToolStripMenuItem grows = new ToolStripMenuItem("Grows");
            grows.DropDownItems.Add(new ToolStripMenuItem("Short"));
            menu.Items.Add(grows);
            LikeMenu(grows);
            bool filled = false;
            grows.DropDown.Opened += delegate
            {
                if (filled) return;
                filled = true;
                // As wide as fits left of her menu, up to 60 letters, so there is a side for it to go back to.
                string wider = "W";
                while (wider.Length < 60 && TextRenderer.MeasureText(wider + "W", grows.DropDown.Font).Width + 80 < menu.Left - area.Left) wider += "W";
                grows.DropDownItems.Add(new ToolStripMenuItem(wider));
            };
            try
            {
                int wide = menu.GetPreferredSize(Size.Empty).Width, need = Beside(menu.Items, 0);
                string wrong = null;
                for (int walk = 0; walk < 3; walk++)
                {
                    menu.Show(MenuSpot(menu, new Point(area.Right - wide - need - 12, area.Top + 40), area));
                    try
                    {
                        for (int wait = 0; wait < 20 && !menu.Visible; wait++) Pump(15);
                        Pump(80);
                        if (TopOpensLeft(menu)) { wrong = "the top decided left, so this didn't test a right side"; break; }
                        ToolStripDropDown drop = grows.DropDown;
                        for (int tries = 0; tries < 3 && !drop.Visible && menu.Visible; tries++)
                        {
                            grows.ShowDropDown();
                            for (int wait = 0; wait < 20 && !drop.Visible; wait++) Pump(15);
                        }
                        Pump(50);
                        if (drop.Visible) { drop.Location = new Point(area.Right + 40, drop.Top); Settled(drop); }
                        Rectangle under = menu.Bounds, shown = drop.Bounds;
                        wrong = !drop.Visible ? "it didn't open" : !filled ? "its list didn't fill as it opened"
                            : !area.Contains(shown) ? "it opened past the screen at " + shown + " in " + area
                            : shown.Right > under.Left + 4 && shown.Left < under.Right - 4 ? "it opened over its menu at " + shown + " beside " + under : null;
                        grows.HideDropDown();
                        if (wrong == null || menu.Visible) break;
                    }
                    finally { menu.Close(); Pump(50); }
                }
                return wrong == null ? null : "A submenu that filled wider as it opened went wrong: " + wrong;
            }
            finally
            {
                menu.Items.Remove(grows);
                sided.Remove(grows);
                grows.Dispose();
            }
        }

        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        private const uint WindowAbove = 3;

        // Whether a window sits under another in Windows' order: walking up from it reaches the other.
        internal static bool Behind(IntPtr window, IntPtr other)
        {
            IntPtr at = window;
            for (int steps = 0; steps < 4096 && at != IntPtr.Zero; steps++)
            {
                at = GetWindow(at, WindowAbove);
                if (at == other) return true;
            }
            return false;
        }

        // Her real menu at every screen's left and right edge, Capri options > Test's Intros, Screensavers and Pranks opened
        // five times each: where each lands, beside Test's list and on a screen.
        private void SmokeMenuSeams(List<string> checks)
        {
            ToolStripMenuItem test = null;
            foreach (ToolStripItem item in optionsItem.DropDownItems) if (item.Text == "Test") test = item as ToolStripMenuItem;
            if (test == null) throw new Exception("No Test item under Capri options.");
            List<string> wrong = new List<string>();
            int opened = 0;
            foreach (Screen screen in Screen.AllScreens)
            {
                Rectangle area = screen.WorkingArea;
                foreach (int x in new[] { area.Left + area.Width / 2, area.Right - 2, area.Left + area.Width / 2, area.Left + 40, area.Left + area.Width / 2 })
                    for (int round = 0; round < 3; round++)
                    {
                        menu.Show(MenuSpot(menu, new Point(x, area.Top + 40), area));
                        try
                        {
                            for (int wait = 0; wait < 20 && !menu.Visible; wait++) Pump(15);
                            Pump(60);
                            for (int tries = 0; tries < 3 && !optionsItem.DropDown.Visible && menu.Visible; tries++) { optionsItem.ShowDropDown(); for (int wait = 0; wait < 20 && !optionsItem.DropDown.Visible; wait++) Pump(15); }
                            Settled(optionsItem.DropDown);
                            for (int tries = 0; tries < 3 && !test.DropDown.Visible && optionsItem.DropDown.Visible; tries++) { test.ShowDropDown(); for (int wait = 0; wait < 20 && !test.DropDown.Visible; wait++) Pump(15); }
                            Settled(test.DropDown);
                            foreach (ToolStripItem sub in test.DropDownItems)
                            {
                                ToolStripMenuItem holder = sub as ToolStripMenuItem;
                                if (holder == null || !holder.HasDropDownItems || !holder.Available) continue;
                                ToolStripDropDown drop = holder.DropDown;
                                for (int tries = 0; tries < 3 && !drop.Visible && test.DropDown.Visible; tries++) { holder.ShowDropDown(); for (int wait = 0; wait < 20 && !drop.Visible; wait++) Pump(15); }
                                Settled(drop);
                                opened++;
                                Rectangle under = RealBounds(test.DropDown), shown = RealBounds(drop);
                                bool onScreen = false;
                                foreach (Screen any in Screen.AllScreens) onScreen |= any.WorkingArea.Contains(shown);
                                bool beside = Beside(shown, under) && shown.Top <= under.Top + holder.Bounds.Top + Slack && shown.Bottom >= under.Top + holder.Bounds.Top;
                                bool behind = drop.Visible && Behind(drop.Handle, test.DropDown.Handle), inside = drop.Visible && InsideAnother(drop);
                                if (!drop.Visible || !onScreen || !beside || behind || inside) wrong.Add(holder.Text + " from x " + x + " round " + round + ": " + (drop.Visible ? "" : "not open ") + (behind ? "behind Test's list " : "") + (inside ? "inside another window " : "") + shown + " beside " + under);
                                holder.HideDropDown();
                            }
                            test.HideDropDown();
                            optionsItem.HideDropDown();
                        }
                        finally { menu.Close(); Pump(40); }
                    }
            }
            if (wrong.Count > 0) throw new Exception(wrong.Count + " of " + opened + " submenus off screen or over Test: " + string.Join(" | ", wrong.GetRange(0, Math.Min(8, wrong.Count)).ToArray()));
            if (dressedLate != null) throw new Exception(DressedLateSays());
            checks.Add("PASS Test's Intros, Screensavers and Pranks open beside Test's list and on a screen, where Windows itself has them and not inside another window, five times each from every screen's left and right edge (" + opened + " opened), and no list was dressed only as her menu opened");
        }

        // A chain of her menu's items opened in turn by code, from the middle of the main screen: null when the last
        // one's list lands beside its parent's list, on a screen, where Windows has it and not inside another window.
        internal string OpensBeside(params ToolStripMenuItem[] chain)
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            menu.Show(MenuSpot(menu, new Point(area.Left + area.Width / 2, area.Top + 40), area));
            try
            {
                for (int wait = 0; wait < 20 && !menu.Visible; wait++) Pump(15);
                Pump(60);
                foreach (ToolStripMenuItem holder in chain)
                {
                    for (int tries = 0; tries < 3 && !holder.DropDown.Visible && menu.Visible; tries++) { holder.ShowDropDown(); for (int wait = 0; wait < 20 && !holder.DropDown.Visible; wait++) Pump(15); }
                    Settled(holder.DropDown);
                    if (!holder.DropDown.Visible) return holder.Text + " didn't open";
                }
                ToolStripMenuItem last = chain[chain.Length - 1];
                ToolStripDropDown under = last.Owner as ToolStripDropDown;
                if (under == null) return last.Text + " isn't in a list";
                Rectangle shown = RealBounds(last.DropDown), beside = RealBounds(under);
                bool onScreen = false, inside = InsideAnother(last.DropDown);
                foreach (Screen any in Screen.AllScreens) onScreen |= any.WorkingArea.Contains(shown);
                int row = beside.Top + last.Bounds.Top;
                if (inside || !onScreen || !Beside(shown, beside) || shown.Top > row + Slack || shown.Bottom < row)
                    return last.Text + " landed at " + shown + " beside " + beside + (inside ? ", inside another window" : "") + "; " + RealPlace(last);
                return null;
            }
            finally { menu.Close(); Pump(40); }
        }

        // Windows' own rectangle for a list, not WinForms' remembered one.
        private static Rectangle RealBounds(ToolStripDropDown drop)
        {
            if (!drop.IsHandleCreated) return drop.Bounds;
            MenuEdges edges;
            MenuWindowRect(drop.Handle, out edges);
            return Rectangle.FromLTRB(edges.Left, edges.Top, edges.Right, edges.Bottom);
        }

        // A list made a child of another window, or parented to one: what put Screensavers off its spot, or undrawn.
        private static bool InsideAnother(ToolStripDropDown drop)
        {
            return drop.IsHandleCreated && ((MenuWindowLong(drop.Handle, -16) & 0x40000000) != 0 || MenuAncestor(drop.Handle, 1) != MenuDesktop());
        }

        // --bench's "!menu walk x y": her real menu opened at that point and every submenu opened in turn, as the checks do,
        // each written to bench.txt with where it landed, whether it shows, its side and whether it is behind its parent.
        internal void BenchMenuWalk(Point at, Action<string> write)
        {
            Rectangle area = Screen.FromPoint(at).WorkingArea;
            // As a right-click shows it: her window in front first, or Windows won't let its submenus open.
            ShowMenu(menu, at);
            try
            {
                for (int wait = 0; wait < 20 && !menu.Visible; wait++) Pump(15);
                Pump(80);
                write("menu walk at " + at + ": top " + menu.Bounds + (TopOpensLeft(menu) ? " opens left" : " opens right") + " on " + area);
                int opened = 0, wrong = 0;
                Walk(menu, "", 0, write, ref opened, ref wrong);
                write("menu walk done: " + opened + " submenus, " + wrong + " wrong");
            }
            finally { menu.Close(); Pump(50); }
        }

        private void Walk(ToolStripDropDown strip, string path, int depth, Action<string> write, ref int opened, ref int wrong)
        {
            if (depth > 3) return;
            foreach (ToolStripItem item in strip.Items)
            {
                ToolStripMenuItem holder = item as ToolStripMenuItem;
                if (holder == null || !holder.Available || !holder.HasDropDownItems) continue;
                ToolStripDropDown drop = holder.DropDown;
                for (int tries = 0; tries < 3 && !drop.Visible && strip.Visible; tries++) { holder.ShowDropDown(); for (int wait = 0; wait < 20 && !drop.Visible; wait++) Pump(15); }
                Settled(drop);
                opened++;
                Rectangle under = strip.Bounds, shown = drop.Bounds;
                bool onScreen = false;
                foreach (Screen any in Screen.AllScreens) onScreen |= any.WorkingArea.Contains(shown);
                bool beside = shown.Right <= under.Left + 4 || shown.Left >= under.Right - 4, behind = drop.Visible && Behind(drop.Handle, strip.Handle);
                bool bad = !drop.Visible || !onScreen || !beside || behind;
                if (bad) wrong++;
                write((bad ? "WRONG " : "ok    ") + path + holder.Text + ": " + (drop.Visible ? "shown" : "not shown") + " " + shown + (shown.Left >= under.Right - 4 ? " right" : shown.Right <= under.Left + 4 ? " left" : " over") + " of " + under + (onScreen ? "" : " off every screen") + (behind ? " behind its parent" : "") + ", direction " + holder.DropDownDirection);
                Walk(drop, path + holder.Text + " > ", depth + 1, write, ref opened, ref wrong);
                holder.HideDropDown();
            }
        }

        private string OpenEach(ToolStripDropDown strip, Rectangle area, int depth, bool left, ref int opened)
        {
            if (depth > 3) return null;
            foreach (ToolStripItem item in strip.Items)
            {
                ToolStripMenuItem holder = item as ToolStripMenuItem;
                if (holder == null || !holder.Available || !holder.HasDropDownItems) continue;
                // Read once it is open: a drop can take a moment to show, and one that didn't is opened again, twice at most.
                ToolStripDropDown drop = holder.DropDown;
                for (int tries = 0; tries < 3 && !drop.Visible && strip.Visible; tries++)
                {
                    holder.ShowDropDown();
                    for (int wait = 0; wait < 20 && !drop.Visible; wait++) Pump(15);
                }
                Settled(drop);
                opened++;
                Rectangle under = strip.Bounds, shown = drop.Bounds;
                string wrong = null;
                if (!drop.Visible) wrong = holder.Text + " didn't open";
                else if ((left ? shown.Right > under.Left + 4 : shown.Left < under.Right - 4) || !area.Contains(shown))
                    wrong = holder.Text + " opened " + (left ? "not left" : "not right") + " at " + shown + " beside " + under + " in " + area;
                else if (!Beside(shown, under) || shown.Top > under.Top + holder.Bounds.Top + Slack || shown.Bottom < under.Top + holder.Bounds.Top)
                    wrong = holder.Text + " opened away from its row at " + shown + " beside " + under;
                else if (Behind(drop.Handle, strip.Handle)) wrong = holder.Text + " opened behind its menu";
                if (wrong == null) wrong = OpenEach(drop, area, depth + 1, left, ref opened);
                holder.HideDropDown();
                if (wrong != null) return wrong;
            }
            return null;
        }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern int GetWindowThreadProcessId(IntPtr window, out int process);
        [DllImport("user32.dll")] private static extern bool MoveWindow(IntPtr window, int x, int y, int width, int height, bool paint);

        private static bool Ours(IntPtr window)
        {
            int process;
            GetWindowThreadProcessId(window, out process);
            return process == System.Diagnostics.Process.GetCurrentProcess().Id;
        }

        // Waits until a drop has stopped moving, a second at most, since Windows places a dropdown and then fits it.
        private static void Settled(ToolStripDropDown drop)
        {
            Rectangle was = drop.Bounds;
            for (int wait = 0; wait < 30; wait++)
            {
                Pump(30);
                Rectangle now = drop.Bounds;
                if (now == was && wait > 0) return;
                was = now;
            }
        }

        private static void Pump(int ms)
        {
            System.Diagnostics.Stopwatch wait = System.Diagnostics.Stopwatch.StartNew();
            while (wait.ElapsedMilliseconds < ms) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
        }

        // Another program's window in front (a console on the test screen): her menu shown from there brings her window to
        // the front, and the other window coming back to the front closes the menu, as a click on it would.
        private void MenuCloses(List<string> checks)
        {
            Rectangle area = Displays.Home.WorkingArea;
            System.Diagnostics.Process other = null;
            try
            {
                other = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c timeout /t 30 /nobreak") { UseShellExecute = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal });
                IntPtr window = IntPtr.Zero;
                for (int tries = 0; tries < 60 && window == IntPtr.Zero; tries++) { Pump(100); other.Refresh(); window = other.MainWindowHandle; }
                if (window == IntPtr.Zero) { checks.Add("LIMIT her menu closing on a click elsewhere: no other window could be opened to stand in front"); return; }
                MoveWindow(window, area.Left + 40, area.Top + 40, 500, 300, true);
                SetForegroundWindow(window);
                Pump(400);
                bool otherInFront = GetForegroundWindow() == window;
                // Windows hands the front only to a program that just had a click or a key from them. Her right-click is
                // that click; a check has none, and pressing keys for one would take over their PC, so it stops here.
                if (!otherInFront)
                {
                    checks.Add("LIMIT her menu closing on a click elsewhere needs a live try: right-click her while another program is in front, then click that program; Windows gives the front only after a real click, which a check can't make without taking over the PC");
                    return;
                }
                ShowMenu(menu, new Point(area.Left + 700, area.Top + 300));
                Pump(400);
                bool herInFront = Ours(GetForegroundWindow()) && menu.Visible;
                SetForegroundWindow(window);
                Pump(600);
                bool closed = !menu.Visible;
                if (menu.Visible) menu.Close();
                if (!otherInFront || !herInFront || !closed)
                    throw new Exception("Her menu didn't take the front and close when another window came forward: other in front first " + otherInFront + ", hers in front with the menu " + herInFront + ", menu closed after " + closed);
                checks.Add("PASS her menu shown while another program's window is in front brings her window forward, and closes when that window comes back to the front, as a click anywhere else does");
            }
            finally
            {
                if (menu.Visible) menu.Close();
                try { if (other != null && !other.HasExited) other.Kill(); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
        }

        // Every kind of ask she has: a plain Yes and No (her mods after a crash, "is this your editor?"), the proofread fix,
        // the site-or-app choice and the jealous offer. Each stays up to ten seconds, then goes with nothing done.
        private void ChoiceTimes(List<string> checks)
        {
            List<string> wrong = new List<string>();
            foreach (string kind in new[] { "plain", "proofread", "site", "jealous" })
            {
                bool fired = false;
                if (kind == "proofread") proofAfter = "kept";
                if (kind == "site") siteChoice = true;
                if (kind == "jealous") jealousOffer = new JealousWindow();
                box.Choose(delegate(bool yes) { fired = true; });
                box.ChoiceSince = DateTime.UtcNow.AddSeconds(-(ChoiceMost - 1));
                ChoiceLapsed();
                bool stillUp = box.Choosing;
                box.ChoiceSince = DateTime.UtcNow.AddSeconds(-(ChoiceMost + 0.5));
                ChoiceLapsed();
                bool gone = !box.Choosing && !fired;
                bool undone = kind == "proofread" ? proofAfter == null : kind == "site" ? !siteChoice : kind == "jealous" ? jealousOffer == null : true;
                if (!stillUp || !gone || !undone) wrong.Add(kind + ": up at 9 s " + stillUp + ", gone at 10.5 s with nothing picked " + gone + ", let go " + undone);
                if (box.Choosing) box.Choose(null);
                jealousOffer = null;
                siteChoice = false;
            }
            if (wrong.Count > 0) throw new Exception("A Yes and No didn't give up after " + ChoiceMost + " s with nothing done: " + string.Join("; ", wrong.ToArray()));
            checks.Add("PASS every Yes and No in her bubble (a plain ask such as \"is this your editor?\", the proofread fix, the site-or-app choice and the jealous offer) stays up to " + ChoiceMost + " s, then goes with nothing done");
        }
    }
}
