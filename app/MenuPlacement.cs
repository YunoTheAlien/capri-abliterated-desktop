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

        // A submenu's side, decided each time it opens.
        private void Sided(ToolStripMenuItem parent)
        {
            if (parent == null || !sided.Add(parent)) return;
            parent.DropDownOpening += delegate { parent.DropDownDirection = OpensLeft(parent) ? ToolStripDropDownDirection.Left : ToolStripDropDownDirection.Right; };
        }

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
                    menu.Font = Neon.Type(size);
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
                checks.Add("PASS her right-click menu near the right edge stays where it opened, and its submenus, nested ones too, all open to the one side decided from the menu itself: left when the room beside it is less than its widest run of submenus, right otherwise, at three places by the edge");
                checks.Add("PASS her right-click menu opened by the right or bottom edge of a screen, at three sizes, opens inside it with room to its right for every run of submenus, so none opens over it");

                MenuCloses(checks);
                ChoiceTimes(checks);
            }
            finally { menu.Font = fontWas; }
        }

        // Her right-click menu near the right edge: just short of the room its submenus need, just past it and at the edge.
        // Each time every submenu, nested ones too, opens to the side decided from the menu itself, inside the screen. Null
        // when they all do.
        private string SubmenusLeft(Rectangle area)
        {
            DressDropDowns(menu.Items);
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
                Pump(30);
                opened++;
                Rectangle under = strip.Bounds, shown = drop.Bounds;
                string wrong = null;
                if (!drop.Visible) wrong = holder.Text + " didn't open";
                else if ((left ? shown.Right > under.Left + 4 : shown.Left < under.Right - 4) || !area.Contains(shown))
                    wrong = holder.Text + " opened " + (left ? "not left" : "not right") + " at " + shown + " beside " + under + " in " + area;
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
