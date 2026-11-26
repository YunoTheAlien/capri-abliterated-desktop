using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Where the Mainframe comes up when it is opened from her right-click menu: beside her on her own screen, or where they
    // pinned it with Save Mainframe position.
    internal sealed partial class Companion
    {
        internal bool RememberPanel { get { return preferences.RememberPanel; } }

        // Settings' panel order as saved, or null for the stock one.
        internal string PanelsSaved { get { return preferences.Panels; } }

        internal void SavePanels(string order)
        {
            if (preferences.Panels == order) return;
            preferences.Panels = order;
            SavePreferences();
        }

        // Turned on, the spot the Mainframe is at now is where it opens from then on; turned off, it opens beside her again.
        internal void ToggleRememberPanel()
        {
            preferences.RememberPanel = !preferences.RememberPanel;
            if (preferences.RememberPanel)
            {
                Rectangle at = mainframe.WindowState == FormWindowState.Normal ? mainframe.Bounds : mainframe.RestoreBounds;
                preferences.PanelX = at.X;
                preferences.PanelY = at.Y;
            }
            SavePreferences();
            mainframe.Sync();
        }

        // From her right-click menu: put where MainframeSpot says, then brought up.
        internal void OpenMainframeByHer()
        {
            Rectangle spot = MainframeSpot();
            if (mainframe.WindowState != FormWindowState.Normal) mainframe.WindowState = FormWindowState.Normal;
            mainframe.Location = spot.Location;
            mainframe.Restore();
        }

        // Its pinned spot while Save Mainframe position is on and that spot is still on a screen, kept wholly on it. Otherwise
        // beside her, on the screen she is on.
        internal Rectangle MainframeSpot()
        {
            Rectangle frame = mainframe.WindowState == FormWindowState.Normal ? mainframe.Bounds : mainframe.RestoreBounds;
            if (preferences.RememberPanel)
            {
                Rectangle kept = new Rectangle(preferences.PanelX, preferences.PanelY, frame.Width, frame.Height);
                foreach (Screen screen in Screen.AllScreens)
                    if (screen.WorkingArea.IntersectsWith(kept)) return Placed(kept, Screen.FromRectangle(kept).WorkingArea);
            }
            return BesideHer(HerForPanel(), frame.Size, Screen.FromRectangle(HerForPanel()).WorkingArea, (int)Math.Round(16 * DeviceDpi / 96f));
        }

        // Her body as the Mainframe is placed beside it: the perched one while she sits on a window.
        private Rectangle HerForPanel() { return BodyOnScreen(anchor, seat != null || riding || onPanel ? PerchedBody : Body); }

        // On whichever side of her has more room, level with her middle, and moved inside the screen's working area.
        internal static Rectangle BesideHer(Rectangle her, Size size, Rectangle area, int gap)
        {
            bool right = area.Right - her.Right >= her.Left - area.Left;
            int x = right ? her.Right + gap : her.Left - gap - size.Width;
            int y = her.Top + (her.Height - size.Height) / 2;
            return Placed(new Rectangle(x, y, size.Width, size.Height), area);
        }

#if CHECKS
        private void SmokePanelSpot(List<string> checks)
        {
            // Beside her: the roomier side, level with her, wholly on the screen, and on her where there is no room either side.
            Rectangle area = new Rectangle(0, 0, 1920, 1040);
            Size panel = new Size(380, 820);
            Rectangle nearLeft = BesideHer(new Rectangle(100, 500, 200, 300), panel, area, 16);
            Rectangle nearRight = BesideHer(new Rectangle(1600, 100, 200, 300), panel, area, 16);
            Rectangle second = BesideHer(new Rectangle(2200, 300, 200, 300), panel, new Rectangle(1920, 0, 1280, 984), 16);
            Rectangle crowded = BesideHer(new Rectangle(500, 100, 300, 600), new Size(700, 600), new Rectangle(0, 0, 1366, 728), 16);
            if (nearLeft != new Rectangle(316, 220, 380, 820) || nearRight != new Rectangle(1204, 0, 380, 820) || second != new Rectangle(2416, 40, 380, 820)
                || !new Rectangle(0, 0, 1366, 728).Contains(crowded))
                throw new Exception("The Mainframe beside her went to the wrong side or off her screen: " + nearLeft + " " + nearRight + " " + second + " " + crowded);
            checks.Add("PASS beside her, the Mainframe takes the side with more room, level with her and wholly on her screen, the second screen too");

            bool rememberWas = preferences.RememberPanel;
            int xWas = preferences.PanelX, yWas = preferences.PanelY;
            Point anchorWas = anchor;
            bool visibleWas = mainframe.Visible;
            FormWindowState stateWas = mainframe.WindowState;
            Rectangle boundsWas = stateWas == FormWindowState.Normal ? mainframe.Bounds : mainframe.RestoreBounds;
            try
            {
                Rectangle work = Displays.Home.WorkingArea;
                if (preferences.RememberPanel) ToggleRememberPanel();
                // Opened from her menu with her at the left of the screen, then at the right.
                anchor = new Point(work.Left + 40 - PaddingSize, work.Top + work.Height / 3);
                OpenMainframeByHer();
                Rectangle her = HerForPanel();
                Rectangle opened = mainframe.Bounds;
                bool besideLeft = Screen.FromRectangle(opened).DeviceName == Screen.FromRectangle(her).DeviceName && work.Contains(opened) && opened.Left >= her.Right;
                anchor = new Point(work.Right - 40 - PaddingSize - (int)Math.Ceiling(preferences.Size * Body.Right), work.Top + work.Height / 3);
                OpenMainframeByHer();
                her = HerForPanel();
                Rectangle openedRight = mainframe.Bounds;
                bool besideRight = work.Contains(openedRight) && openedRight.Right <= her.Left;
                if (!besideLeft || !besideRight) throw new Exception("Opened from her menu, the Mainframe wasn't beside her on her screen: " + opened + " " + openedRight + " for her at " + her);
                // Save Mainframe position: on keeps the spot it is at, and it opens there wherever she is.
                // A spot it fits at whole: a screen barely taller than the Mainframe pulled a 20 px offset back to the top.
                Point pinned = new Point(work.Left + Math.Min(30, Math.Max(0, work.Width - mainframe.Width)), work.Top + Math.Min(20, Math.Max(0, work.Height - mainframe.Height)));
                mainframe.Location = pinned;
                ToggleRememberPanel();
                if (!preferences.RememberPanel || preferences.PanelX != pinned.X || preferences.PanelY != pinned.Y) throw new Exception("Save Mainframe position didn't keep the spot the Mainframe was at.");
                mainframe.Location = new Point(work.Left + 300, work.Top + 200);
                OpenMainframeByHer();
                if (mainframe.Location != pinned) throw new Exception("With Save Mainframe position on, the Mainframe didn't open at its spot: " + mainframe.Location);
                // A pinned spot no screen shows any more opens beside her instead.
                preferences.PanelX = -60000;
                OpenMainframeByHer();
                bool rescued = work.Contains(mainframe.Bounds) && mainframe.Bounds.Right <= her.Left;
                preferences.PanelX = pinned.X;
                ToggleRememberPanel();
                OpenMainframeByHer();
                if (!rescued || preferences.RememberPanel || mainframe.Location == pinned) throw new Exception("A pinned spot on no screen, or Save Mainframe position off, didn't open the Mainframe beside her.");
                checks.Add("PASS opened from her menu the Mainframe comes up beside her on her screen; Save Mainframe position on keeps the spot it was at and opens it there, a spot on no screen falls back to beside her, and off opens beside her again");
            }
            finally
            {
                preferences.RememberPanel = rememberWas;
                preferences.PanelX = xWas;
                preferences.PanelY = yWas;
                anchor = anchorWas;
                mainframe.WindowState = FormWindowState.Normal;
                mainframe.Bounds = boundsWas;
                if (stateWas != FormWindowState.Normal) mainframe.WindowState = stateWas;
                if (!visibleWas) mainframe.Hide();
                mainframe.Sync();
            }
            // The same setting among her switches in the Toggles window, ticked as it is, and the Mainframe's switch following it.
            ToolStripMenuItem panelItem = null;
            foreach (ToolStripItem one in togglesMenu.DropDownItems) if (one.Text == "Save Mainframe position") panelItem = one as ToolStripMenuItem;
            if (panelItem == null || panelItem.ToolTipText != Mainframe.PanelTip) throw new Exception("Her Toggles window has no Save Mainframe position with the owner's tip.");
            bool panelWas = preferences.RememberPanel;
            int panelX = preferences.PanelX, panelY = preferences.PanelY;
            try
            {
                panelItem.PerformClick();
                bool flipped = preferences.RememberPanel != panelWas && mainframe.RememberPanelShown == preferences.RememberPanel;
                panelItem.PerformClick();
                if (!flipped || preferences.RememberPanel != panelWas || mainframe.RememberPanelShown != panelWas) throw new Exception("Save Mainframe position in her Toggles didn't switch the Mainframe's setting.");
            }
            finally { preferences.RememberPanel = panelWas; preferences.PanelX = panelX; preferences.PanelY = panelY; mainframe.Sync(); }
            checks.Add("PASS Save Mainframe position is in her Toggles window with the owner's tip, and switches the same setting as the Mainframe's row");
            string column = mainframe.SettingsColumnWrong();
            if (column != null) throw new Exception(column);
            checks.Add("PASS Settings opens with SETTINGS at the top right (Save Mainframe position, Always on top, Mainframe size, Mainframe colours, Custom colours), then CAPRI with Hologram first, every row of the column under them, and Save Mainframe position's tip in the owner's words");
            string colours = mainframe.CustomColoursWrong();
            if (colours != null) throw new Exception(colours);
            checks.Add("PASS Custom colours opens its window on her CAPRI fold with Hair selected");
            string missing = null;
            string languageWas = Brain.Language;
            try
            {
                for (int i = 0; i < Brain.Languages.GetLength(0) && missing == null; i++)
                {
                    if (Brain.Languages[i, 2] == "en") continue;
                    Brain.Language = Brain.Languages[i, 1];
                    Gui.Forget();
                    // A language with no table of her menus at all is left to her brain, as every label is.
                    if (Gui.T("Settings") == "Settings") continue;
                    foreach (string label in new[] { "Save Mainframe position", "Custom colours", "Custom colours...", "Reset selected colour", "Make a mod" })
                        if (Gui.T(label) == label) { missing = Brain.Languages[i, 1] + " has no " + label; break; }
                }
            }
            finally { Brain.Language = languageWas; Gui.Forget(); }
            if (missing != null) throw new Exception("A new SETTINGS label isn't in every language her menus have: " + missing);
            checks.Add("PASS Save Mainframe position, Custom colours, Custom colours..., Reset selected colour and Make a mod read in every language her menus have");
        }
#endif
    }

    // SETTINGS at the top right of the Settings page, and Custom colours opening its window from it.
    internal sealed partial class Mainframe
    {
        // How far CAPRI and everything under it in the right column sit below where they were built: SETTINGS' heading and
        // five rows, and the gap above the next heading.
        private const int SettingsDrop = 178 + 28;
        internal const string PanelTip = "Remember where it loaded each time until you toggle it off, move the panel and toggle it back on.";
        private Head settingsHead, capriHead;
        private Toggle rememberPanel;
        internal bool RememberPanelShown { get { return rememberPanel.Checked; } }
        private Key customColours;

        // Null when the right column starts with SETTINGS and its rows, then CAPRI with Hologram first and the rest under it,
        // and Save Mainframe position's tip is its one sentence.
        internal string SettingsColumnWrong()
        {
            Control[] order = { settingsHead, rememberPanel, onTop, frameSize, frameColours, customColours, capriHead, hologram, sizeSlider };
            int top;
            for (int i = 0; i < order.Length; i++)
            {
                if (!built.TryGetValue(order[i], out top)) return "A SETTINGS or CAPRI row isn't on the Settings page: " + order[i].Text;
                if (!pageLeft.Exists(entry => entry.Key == order[i] && entry.Value >= Column)) return order[i].Text + " isn't in the right column.";
                if (i > 0 && top <= built[order[i - 1]]) return order[i].Text + " isn't under " + order[i - 1].Text + ".";
            }
            int capriTop = built[capriHead], settingsEnd = built[customColours] + customColours.Height;
            foreach (KeyValuePair<Control, int> entry in pageLeft)
            {
                if (entry.Value < Column || Array.IndexOf(order, entry.Key) >= 0 || !built.TryGetValue(entry.Key, out top)) continue;
                // Only SETTINGS' own rows, their names and readouts, sit above CAPRI.
                if (top >= settingsEnd && top < capriTop) return entry.Key.Text + " (" + entry.Key.GetType().Name + " at " + top + ") sits between SETTINGS and CAPRI.";
            }
            SettingHint tip;
            if (!hints.TryGetValue(rememberPanel, out tip) || tip.Exact != PanelTip) return "Save Mainframe position's tip isn't the owner's words.";
            return null;
        }

        // Null when Custom colours opens its window, on her CAPRI fold with Hair selected.
        internal string CustomColoursWrong()
        {
            try
            {
                customColours.PerformClick();
                string shown = ColourSheetShown;
                if (shown == null) return "Custom colours didn't open its window.";
                if (shown.IndexOf("Hair #", StringComparison.Ordinal) < 0 || shown.IndexOf("(selected)", StringComparison.Ordinal) < 0) return "Custom colours opened without her CAPRI colours: " + shown;
                return null;
            }
            finally { CloseColours(); }
        }
    }
}
