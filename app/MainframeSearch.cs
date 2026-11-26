using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CapriDesktop
{
    // The search box beside Mods on Settings: typing lists settings whose name, translated name, panel or tip
    // match; picking one scrolls to it, marks it and flashes a line under it. The list sits inside her window, so
    // typing never loses the box.
    internal sealed partial class Mainframe
    {
        private Entry settingsSearch;
        private System.Windows.Forms.Panel searchList;
        private Font searchFont;
        private readonly List<Control> searchFound = new List<Control>();
        private Label searchMark;
        private Clock.Ticker searchFade;
        private const int SearchMost = 8;

        private void BuildSearch()
        {
            settingsSearch = Input(446, 20, 200, 22);
            settingsSearch.Cue = "Search settings";
            settingsSearch.Box.AccessibleName = "Search settings";
            settingsSearch.Box.AccessibleDescription = "Type part of a setting's name; Enter goes to the first";
            settingsSearch.Visible = false;
            Explain(settingsSearch, "Search settings");
            settingsSearch.Box.TextChanged += delegate { ShowSearch(); };
            settingsSearch.Box.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter && searchFound.Count > 0) { e.SuppressKeyPress = true; GoToSetting(searchFound[0]); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; settingsSearch.Box.Text = ""; HideSearch(); }
            };
        }

        // The box shows on the Settings page, right of Mods.
        private void PlaceSearch()
        {
            if (settingsSearch == null) return;
            bool show = paged;
            settingsSearch.Left = Scaled(446);
            settingsSearch.Visible = show;
            if (!show) HideSearch();
        }

        // The settings the words find, best first: their name as she shows it or in English, then their panel, then their
        // tip. Only switches, sliders, keys and boxes on the page, shown, and in a panel.
        internal List<Control> FindSettings(string words)
        {
            List<Control> found = new List<Control>();
            string want = (words ?? "").Trim();
            if (want.Length < 2) return found;
            List<Control> named = new List<Control>(), paneled = new List<Control>(), tipped = new List<Control>();
            foreach (KeyValuePair<Control, int> entry in pageLeft)
            {
                Control control = entry.Key;
                SettingHint hint;
                Panel panel;
                // Settings someone can use, not the names beside them, which carry the same help.
                if (!Steerable(control) || !panelOf.TryGetValue(control, out panel) || !hints.TryGetValue(control, out hint) || !control.Visible || dropped.Contains(control)) continue;
                string shown = hint.Label != null ? hint.Label.Text : control.Text;
                if (Has(shown, want) || Has(hint.Title, want)) named.Add(control);
                else if (Has(panel.Head.Shown, want) || Has(panel.Id, want)) paneled.Add(control);
                else if (Has(hint.Text, want)) tipped.Add(control);
            }
            foreach (List<Control> group in new[] { named, paneled, tipped })
                foreach (Control control in group)
                {
                    if (found.Count >= SearchMost) return found;
                    found.Add(control);
                }
            return found;
        }

        private static bool Has(string text, string words) { return !string.IsNullOrEmpty(text) && text.IndexOf(words, StringComparison.CurrentCultureIgnoreCase) >= 0; }

        // A setting's name as the list shows it, with its panel: "Perch - GENERAL SETTINGS".
        private string SearchLine(Control control)
        {
            SettingHint hint;
            Panel panel;
            hints.TryGetValue(control, out hint);
            panelOf.TryGetValue(control, out panel);
            string name = hint != null && hint.Label != null && hint.Label.Text.Length > 0 ? hint.Label.Text : control.Text.Length > 0 ? control.Text : hint != null ? hint.Title : control.AccessibleName;
            return (name ?? "").Trim() + (panel != null ? "  -  " + panel.Head.Shown : "");
        }

        private void ShowSearch()
        {
            searchFound.Clear();
            searchFound.AddRange(FindSettings(settingsSearch.Box.Text));
            if (searchFound.Count == 0) { HideSearch(); return; }
            float s = Unit();
            int row = (int)Math.Round(24 * s), wide = (int)Math.Round(328 * s);
            if (searchList == null)
            {
                searchList = new System.Windows.Forms.Panel { Visible = false };
                searchList.Paint += delegate(object sender, PaintEventArgs e)
                {
                    using (Pen rim = new Pen(Cyan, Math.Max(1f, Unit()))) e.Graphics.DrawRectangle(rim, 0, 0, searchList.Width - 1, searchList.Height - 1);
                };
                Controls.Add(searchList);
            }
            searchList.SuspendLayout();
            foreach (Control old in ToList(searchList.Controls)) { searchList.Controls.Remove(old); old.Dispose(); }
            searchList.BackColor = Ground;
            if (searchFont != null) searchFont.Dispose();
            searchFont = Type(9f * s);
            Font font = searchFont;
            for (int i = 0; i < searchFound.Count; i++)
            {
                Control target = searchFound[i];
                Label line = new Label { AutoSize = false, AutoEllipsis = true, Text = SearchLine(target), Font = font, ForeColor = White, BackColor = Ground, Cursor = Cursors.Hand };
                line.Bounds = new Rectangle((int)Math.Round(8 * s), (int)Math.Round(4 * s) + i * row, wide - (int)Math.Round(16 * s), row);
                line.TextAlign = ContentAlignment.MiddleLeft;
                line.MouseEnter += delegate { line.ForeColor = Cyan; };
                line.MouseLeave += delegate { line.ForeColor = White; };
                line.Click += delegate { GoToSetting(target); };
                searchList.Controls.Add(line);
            }
            searchList.Bounds = new Rectangle(settingsSearch.Left, settingsSearch.Bottom + (int)Math.Round(4 * s), wide, searchFound.Count * row + (int)Math.Round(8 * s));
            searchList.ResumeLayout();
            searchList.Visible = true;
            searchList.BringToFront();
        }

        private static List<Control> ToList(Control.ControlCollection controls)
        {
            List<Control> all = new List<Control>();
            foreach (Control one in controls) all.Add(one);
            return all;
        }

        private void HideSearch()
        {
            searchFound.Clear();
            if (searchList != null) searchList.Visible = false;
        }

        // The page scrolled so the setting sits a third of the way down her window, her keyboard mark on it, and a line
        // in her accent colour under its row for a moment.
        internal void GoToSetting(Control control)
        {
            if (control == null || !panelOf.ContainsKey(control)) return;
            HideSearch();
            if (!paged) ShowSettings(true);
            int top = StackTop(control);
            ScrollPage(Math.Max(0, top - pageView / 3));
            if (Steerable(control)) Hold(control);
            if (searchMark == null)
            {
                searchMark = new Label { AutoSize = false, Visible = false };
                Controls.Add(searchMark);
                searchFade = new Clock.Ticker("search fade") { Interval = 1500 };
                searchFade.Tick += delegate { searchFade.Stop(); searchMark.Visible = false; };
            }
            Panel panel = panelOf[control];
            searchMark.BackColor = Cyan;
            searchMark.Bounds = new Rectangle(Scaled(28 + panel.Column * Column), control.Bottom + Scaled(2), Scaled(328), Math.Max(2, Scaled(2)));
            searchMark.Visible = true;
            searchMark.BringToFront();
            searchFade.Stop();
            searchFade.Start();
        }

#if CHECKS
        internal void CheckSettingsSearch(List<string> checks)
        {
            bool pagedWas = paged;
            string perchWas = null;
            Control perchLabel = null;
            try
            {
                ShowSettings(true);
                if (!settingsSearch.Visible || settingsSearch.Left < modsKey.Right || settingsSearch.Right > lockKey.Left)
                    throw new Exception("The settings search isn't between Mods and the lock on Settings: " + settingsSearch.Bounds + ", Mods " + modsKey.Bounds + ", lock " + lockKey.Bounds);
                settingsSearch.Box.Text = "perch";
                if (searchFound.Count == 0 || searchFound[0] != perchFps || searchList == null || !searchList.Visible || !SearchLine(perchFps).EndsWith("GENERAL SETTINGS", StringComparison.Ordinal))
                    throw new Exception("\"perch\" didn't list Perch under GENERAL SETTINGS first: " + (searchFound.Count > 0 ? SearchLine(searchFound[0]) : "nothing"));
                ScrollPage(0);
                settingsSearch.Box.Focus();
                GoToSetting(searchFound[0]);
                Application.DoEvents();
                bool inView = perchFps.Top >= 0 && perchFps.Bottom <= ClientSize.Height, marked = searchMark.Visible && ActiveControl == perchFps;
                if (!inView || !marked || searchList.Visible)
                    throw new Exception("Picking Perch didn't take the page to it with its mark: in view " + inView + " (" + perchFps.Bounds + " in " + ClientSize + "), marked " + marked + ", list still up " + searchList.Visible);
                // Her own language: the name as she shows it is searched too.
                SettingHint hint;
                if (hints.TryGetValue(perchFps, out hint) && hint.Label != null)
                {
                    perchLabel = hint.Label;
                    perchWas = perchLabel.Text;
                    perchLabel.Text = "Perchoir";
                    settingsSearch.Box.Text = "perchoir";
                    if (searchFound.Count == 0 || searchFound[0] != perchFps) throw new Exception("A setting's name in her language wasn't searched");
                }
                settingsSearch.Box.Text = "zzqx";
                if (searchList.Visible) throw new Exception("A search that finds nothing left its list up");
                checks.Add("PASS the settings search sits between Mods and the lock on Settings only; \"perch\" lists Perch under GENERAL SETTINGS first, picking it scrolls the page to it with her mark and a line under it, a name in her language is found, and nothing found shows no list");
            }
            finally
            {
                if (perchLabel != null) perchLabel.Text = perchWas;
                settingsSearch.Box.Text = "";
                HideSearch();
                ShowSettings(pagedWas);
            }
        }
#endif
    }
}
