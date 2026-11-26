using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Settings' panels that fold down: switched in Toggles' PAN: MAINFRAME SETTINGS > Folding, per panel or all of them; a
    // foldable panel's heading folds and unfolds it, and each one's open or closed is kept with her settings.
    internal sealed partial class Companion
    {
        internal const string PanPrefix = "Pan: ";

        internal bool FoldOn(string key) { return preferences.FoldAll || (preferences.FoldPanels != null && preferences.FoldPanels.Contains(key)); }

        internal bool FoldedNow(string key) { return FoldOn(key) && preferences.PanelsFolded != null && preferences.PanelsFolded.Contains(key); }

        internal void SetFolded(string key, bool folded)
        {
            List<string> now = new List<string>(preferences.PanelsFolded ?? new List<string>());
            now.Remove(key);
            if (folded) now.Add(key);
            preferences.PanelsFolded = now.Count == 0 ? null : now;
            SavePreferences();
        }

        private void SetFoldOn(string key, bool on)
        {
            List<string> now = new List<string>(preferences.FoldPanels ?? new List<string>());
            now.Remove(key);
            if (on) now.Add(key);
            preferences.FoldPanels = now.Count == 0 ? null : now;
            SavePreferences();
            if (mainframe != null) mainframe.FoldsChanged();
        }

        private void SetFoldAll(bool on)
        {
            preferences.FoldAll = on;
            SavePreferences();
            if (mainframe != null) mainframe.FoldsChanged();
        }

        // Toggles' PAN: MAINFRAME SETTINGS with its Folding row: its key shows Fold all and a switch per Settings panel, as
        // the panels stand when it opens.
        private const string FoldName = "fold:";

        private ToolStripMenuItem PanFold()
        {
            ToolStripMenuItem pan = new ToolStripMenuItem(PanPrefix + "Mainframe settings");
            ToolStripMenuItem panels = new ToolStripMenuItem("Panels") { Tag = Mainframe.OpensTag, Name = Mainframe.PanelsRow };
            panels.ToolTipText = "Move, hide and gather Settings rows";
            pan.DropDownItems.Add(panels);
            ToolStripMenuItem folding = new ToolStripMenuItem("Folding") { Tag = Mainframe.OpensTag, Name = Mainframe.SideRow };
            folding.ToolTipText = "Settings panels that fold down from their heading";
            // Each switch ticked from her settings at once after a click, so Toggles' row shows what it just did rather than
            // being set back from a tick made before.
            Action tick = delegate
            {
                foreach (ToolStripItem item in folding.DropDownItems)
                {
                    ToolStripMenuItem one = item as ToolStripMenuItem;
                    if (one == null) continue;
                    if (!one.Name.StartsWith(FoldName, StringComparison.Ordinal)) { one.Checked = preferences.FoldAll; continue; }
                    string key = one.Name.Substring(FoldName.Length);
                    one.Checked = preferences.FoldAll || (preferences.FoldPanels != null && preferences.FoldPanels.Contains(key));
                    one.Enabled = !preferences.FoldAll;
                }
            };
            Action fill = delegate
            {
                folding.DropDownItems.Clear();
                ToolStripMenuItem all = new ToolStripMenuItem("Fold all", null, delegate { SetFoldAll(!preferences.FoldAll); tick(); });
                all.ToolTipText = "Every Settings panel folds down from its heading";
                folding.DropDownItems.Add(all);
                if (mainframe != null)
                    foreach (KeyValuePair<string, string> panel in mainframe.FoldablePanels())
                    {
                        string key = panel.Key;
                        ToolStripMenuItem one = new ToolStripMenuItem(panel.Value, null, delegate { SetFoldOn(key, !(preferences.FoldPanels != null && preferences.FoldPanels.Contains(key))); tick(); }) { Name = FoldName + key };
                        folding.DropDownItems.Add(one);
                    }
                tick();
            };
            fill();
            folding.DropDownOpening += delegate { fill(); };
            pan.DropDownItems.Add(folding);
            LikeMenu(pan);
            return pan;
        }
    }

    internal sealed partial class Companion
    {
#if CHECKS
        // Folding: PAN: MAINFRAME SETTINGS after the Sec: folds; a panel switched to fold folds from its heading, its rows
        // hidden and the panel under it moved up, kept in her settings, and opens again; Fold all makes every heading fold.
        private void SmokePanelFolds(List<string> checks, string output)
        {
            bool allWas = preferences.FoldAll, shownWas = mainframe.Visible;
            FormWindowState stateWas = mainframe.WindowState;
            List<string> onWas = preferences.FoldPanels == null ? null : new List<string>(preferences.FoldPanels), foldedWas = preferences.PanelsFolded == null ? null : new List<string>(preferences.PanelsFolded);
            List<string> wrong = new List<string>();
            System.Reflection.MethodInfo show = typeof(ToolStripDropDownItem).GetMethod("OnDropDownShow", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            try
            {
                int pan = togglesMenu.DropDownItems.IndexOf(panFoldMenu), intros = togglesMenu.DropDownItems.IndexOf(introsMenu), lastSec = -1;
                for (int i = 0; i < togglesMenu.DropDownItems.Count; i++) if (togglesMenu.DropDownItems[i].Text.StartsWith(SecPrefix, StringComparison.Ordinal)) lastSec = i;
                if (pan < 0 || pan < lastSec || pan > intros) wrong.Add("PAN: MAINFRAME SETTINGS is at " + pan + ", the last Sec: fold at " + lastSec + ", Intros at " + intros);
                ToolStripMenuItem folding = null;
                foreach (ToolStripItem one in panFoldMenu.DropDownItems) if (one.Text == "Folding") folding = one as ToolStripMenuItem;
                if (folding == null || folding.Name != Mainframe.SideRow) { wrong.Add("no Folding row whose key shows its switches"); throw new Exception(string.Join("; ", wrong.ToArray())); }
                show.Invoke(folding, new object[] { EventArgs.Empty });
                ToolStripMenuItem voice = null;
                foreach (ToolStripItem one in folding.DropDownItems) if (one.Text == "VOICE") voice = one as ToolStripMenuItem;
                if (voice == null) { wrong.Add("Folding has no switch for VOICE"); throw new Exception(string.Join("; ", wrong.ToArray())); }
                mainframe.PictureSettings(Path.Combine(output, "fold-before.png"));
                string below = mainframe.PanelBelow("VOICE");
                int belowTop = below == null ? -1 : mainframe.PanelTopOf(below);
                voice.PerformClick();
                // Lit at once: Toggles' row is set from this tick straight after the click.
                if (!voice.Checked) wrong.Add("VOICE's Folding switch stayed off after its click");
                if (!mainframe.PanelFoldable("VOICE") || mainframe.PanelFolded("VOICE")) wrong.Add("VOICE switched to fold: foldable " + mainframe.PanelFoldable("VOICE") + ", folded at once " + mainframe.PanelFolded("VOICE"));
                mainframe.ClickPanelHead("VOICE");
                string hidden = mainframe.PanelRowsShown("VOICE");
                int foldedTop = below == null ? -1 : mainframe.PanelTopOf(below);
                mainframe.PictureSettings(Path.Combine(output, "fold-voice.png"));
                if (!mainframe.PanelFolded("VOICE") || !hidden.StartsWith("0 of", StringComparison.Ordinal) || (below != null && foldedTop >= belowTop) || preferences.PanelsFolded == null || !preferences.PanelsFolded.Contains("VOICE"))
                    wrong.Add("VOICE's heading didn't fold it: folded " + mainframe.PanelFolded("VOICE") + ", rows shown " + hidden + ", " + below + " from " + belowTop + " to " + foldedTop);
                mainframe.ClickPanelHead("VOICE");
                string back = mainframe.PanelRowsShown("VOICE");
                if (mainframe.PanelFolded("VOICE") || back.StartsWith("0 of", StringComparison.Ordinal) || (below != null && mainframe.PanelTopOf(below) != belowTop))
                    wrong.Add("VOICE didn't open again: rows shown " + back + ", " + below + " at " + mainframe.PanelTopOf(below) + " for " + belowTop);
                show.Invoke(folding, new object[] { EventArgs.Empty });
                ToolStripMenuItem foldAll = (ToolStripMenuItem)folding.DropDownItems[0];
                foldAll.PerformClick();
                if (!foldAll.Checked || !voice.Checked) wrong.Add("Fold all's switch, or the panels under it, stayed off after its click");
                if (!mainframe.EveryPanelFoldable()) wrong.Add("Fold all left a heading that doesn't fold");
            }
            finally
            {
                preferences.FoldAll = allWas; preferences.FoldPanels = onWas; preferences.PanelsFolded = foldedWas;
                SavePreferences();
                mainframe.FoldsChanged();
                mainframe.ShowSettings(false);
                if (mainframe.WindowState != stateWas) mainframe.WindowState = stateWas;
                if (!shownWas && mainframe.Visible) mainframe.Hide();
            }
            if (wrong.Count > 0) throw new Exception("Folding went wrong: " + string.Join("; ", wrong.ToArray()));
            checks.Add("PASS Folding: PAN: MAINFRAME SETTINGS sits after the Sec: folds; a Folding switch lights on its click; VOICE switched to fold folds from its heading, its rows hidden and the panel under it moved up, kept in her settings, and opens again in place; Fold all makes every heading fold (fold-before.png, fold-voice.png)");
        }
#endif
    }

    internal sealed partial class Mainframe
    {
        // Rows a fold hid, shown again when it opens; a row hidden for its own reasons is left alone.
        private readonly HashSet<Control> foldHidden = new HashSet<Control>();

        // Every Settings panel by its saved key and its heading's words, column by column.
        internal List<KeyValuePair<string, string>> FoldablePanels()
        {
            List<KeyValuePair<string, string>> found = new List<KeyValuePair<string, string>>();
            foreach (List<Panel> list in columns) foreach (Panel panel in list) found.Add(new KeyValuePair<string, string>(panel.Key, panel.Head.Shown));
            return found;
        }

        internal void FoldsChanged() { if (IsDisposed) return; ApplyFolds(); Restack(); }

        // Each panel's heading foldable or not, and its rows shown or hidden, as her settings say.
        private void ApplyFolds()
        {
            foreach (List<Panel> list in columns)
                foreach (Panel panel in list)
                {
                    bool can = capri.FoldOn(panel.Key), folded = can && capri.FoldedNow(panel.Key);
                    if (panel.Head.Foldable != can || panel.Head.Folded != folded)
                    {
                        panel.Head.Foldable = can;
                        panel.Head.Folded = folded;
                        panel.Head.Cursor = panelsUnlocked ? Cursors.SizeAll : can ? Cursors.Hand : Cursors.Default;
                        panel.Head.AutoSize = false;
                        panel.Head.AutoSize = true;
                        panel.Head.Invalidate();
                    }
                    panel.Folded = folded;
                    foreach (Control row in panel.Offsets.Keys)
                    {
                        // Its own rows above the heading (YOU's keys) stay when it folds.
                        if (row == panel.Head || panel.Offsets[row] < -HeadSlack) continue;
                        // A row shown later by its own check (VB-CABLE's key) folds away too while its panel is folded.
                        if (foldWatched.Add(row)) row.VisibleChanged += FoldedRowShown;
                        if (folded && row.Visible) { row.Visible = false; foldHidden.Add(row); }
                        else if (!folded && foldHidden.Remove(row)) row.Visible = true;
                    }
                }
        }

        private readonly HashSet<Control> foldWatched = new HashSet<Control>();

        private void FoldedRowShown(object sender, EventArgs e)
        {
            Control row = sender as Control;
            Panel panel;
            if (row == null || !row.Visible || !panelOf.TryGetValue(row, out panel) || !panel.Folded) return;
            row.Visible = false;
            foldHidden.Add(row);
        }

        private void ToggleFold(Panel panel)
        {
            capri.SetFolded(panel.Key, !panel.Folded);
            ApplyFolds();
            Restack();
        }

#if CHECKS
        // For the checks: a panel's heading clicked as a click lands on it; whether it is folded; where its next row is.
        internal void ClickPanelHead(string id) { Panel panel = FindPanel(id); if (panel != null) panel.Head.FoldClick(); }

        internal bool PanelFolded(string id) { Panel panel = FindPanel(id); return panel != null && panel.Folded; }

        internal string PanelRowsShown(string id)
        {
            Panel panel = FindPanel(id);
            if (panel == null) return null;
            int shown = 0, all = 0;
            foreach (Control row in panel.Offsets.Keys) { if (row == panel.Head) continue; all++; if (row.Visible) shown++; }
            return shown + " of " + all;
        }

        internal int PanelTopOf(string id) { Panel panel = FindPanel(id); return panel == null ? -1 : PanelTop(panel); }

        internal void PictureSettings(string path)
        {
            if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
            if (!Visible) Show();
            ShowSettings(true);
            Shot(path);
        }

        internal bool PanelFoldable(string id) { Panel panel = FindPanel(id); return panel != null && panel.Head.Foldable; }

        internal bool EveryPanelFoldable() { foreach (List<Panel> list in columns) foreach (Panel panel in list) if (!panel.Head.Foldable) return false; return true; }

        internal string PanelBelow(string id)
        {
            Panel panel = FindPanel(id);
            if (panel == null) return null;
            int at = columns[panel.Column].IndexOf(panel);
            return at + 1 < columns[panel.Column].Count ? columns[panel.Column][at + 1].Id : null;
        }
#endif
    }
}
