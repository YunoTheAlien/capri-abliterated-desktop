using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Text;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Mainframe
    {
        private const int MostModRows = 12, ModRowTop = 276, ModRowHeight = 62, ModsBelow = 130, PagingTall = 42, ColoursGap = 10;
        private readonly List<KeyValuePair<Control, int>> modLeft = new List<KeyValuePair<Control, int>>();
        private readonly Label[] modNames = new Label[MostModRows], modAbouts = new Label[MostModRows];
        private readonly Key[] modSwitches = new Key[MostModRows], modCogs = new Key[MostModRows], modRemoves = new Key[MostModRows], modPublishes = new Key[MostModRows], modOptions = new Key[MostModRows];
        // What sits under the rows (paging, COLORS and its keys), by how far below a list of four rows it was built.
        private readonly List<KeyValuePair<Control, int>> modBelow = new List<KeyValuePair<Control, int>>(), modPaging = new List<KeyValuePair<Control, int>>();
        private Key modColourReset;

        // As many rows as the page's height holds under INSTALLED, with paging and COLORS straight under the last one, so
        // the extra space is used; never fewer than four.
        private int ModRows { get { return Math.Max(4, Math.Min(MostModRows, (MainTall(expanded) - ModsBelow - ModRowTop) / ModRowHeight)); } }

        // Paging under the rows, and COLORS under that clear of its keys, at the scale she is drawn. With fewer mods than a
        // page holds, COLORS comes up under the last one, with no empty rows between.
        private void PlaceModRows()
        {
            int total = modShown == null ? 0 : modShown.Count;
            bool pages = total > ModRows;
            int under = ModRowTop + ModRowHeight * (pages ? ModRows : Math.Max(1, total));
            foreach (KeyValuePair<Control, int> entry in modPaging) entry.Key.Top = Scaled(under + entry.Value);
            foreach (KeyValuePair<Control, int> entry in modBelow) entry.Key.Top = Scaled(under + (pages ? PagingTall : ColoursGap) + entry.Value);
        }
        private Key modsKey, modBack, modNext, modBrowse, modMake, modImport, modFolder;
        private Head modListHead, modColoursHead;
        private Toggle untestedSwitch;

        // Her own OFF/ON switch and key for a mod's panel (Ui.Switch, Ui.Button), in her colours and font, the column's width.
        // A change or click that throws goes to the mod's failures, never to her.
        internal Control ModSwitch(string text, bool on, Action<bool> changed, Action<Exception> failed)
        {
            Toggle toggle = new Toggle { Text = text, Font = Font, Checked = on, Size = new Size(328, 22) };
            toggle.AccessibleName = text;
            toggle.CheckedChanged += delegate
            {
                if (changed == null) return;
                try { changed(toggle.Checked); }
                catch (Exception error) { failed(error); }
            };
            return toggle;
        }

        internal Control ModButton(string text, Action clicked, Action<Exception> failed)
        {
            Key key = new Key { Text = text, Font = strong, Size = new Size(328, 22) };
            key.AccessibleName = text;
            key.Click += delegate
            {
                if (clicked == null) return;
                try { clicked(); }
                catch (Exception error) { failed(error); }
            };
            return key;
        }
        private Label modStatus, modEmpty, modNoMatch, modPage;
        private readonly Timer modStatusTimer = new Timer();
        private Entry modSearch;
        private List<Mod> modShown = new List<Mod>();
        private bool modded;
        private int modFirst;

        private void BuildMods()
        {
            ModAsk = AskToRun;
            PluginAsk = AskToTakePlugin;
            DataAsk = AskDropData;
            Security.Moved = delegate { try { if (!IsDisposed) BeginInvoke((MethodInvoker)CogTurned); } catch (InvalidOperationException) { } };
            int first = Controls.Count;
            Section("MODS >", 0, 58);
            Key import = Make("Import mod", 28, 80, 100, 28, delegate { ImportMod(); });
            import.Accent = Cyan;
            import.Loud = true;
            modBrowse = Make("Browse mods", 134, 80, 100, 28, delegate { BrowseMods(); });
            Key folder = Make("Mods folder", 240, 80, 116, 28, delegate { OpenModsFolder(); });
            Explain(import, "Import mod", "MainframeMods.cs");
            Explain(modBrowse, "Browse mods", "MainframeMods.cs");
            Explain(folder, "Open mods folder", "MainframeMods.cs");
            modImport = import;
            modFolder = folder;
            modMake = Make("Make a mod", 28, 114, 328, 28, delegate { MakeMod(); });
            modMake.AccessibleName = "Make a mod: open the mod helper";
            Explain(modMake, "Make a mod", "MainframeMods.cs");
            untestedSwitch = Switch("Load untested mods", 0, 145, delegate { if (!syncing) UntestedChanged(); });
            Explain(untestedSwitch, "Load untested mods", "MainframeMods.cs");
            modStatus = ModLabel("", 28, 176, 328, 36, Font, Faint);
            modStatusTimer.Tick += delegate { modStatusTimer.Stop(); modStatus.Text = ""; };
            FormClosed += delegate { modStatusTimer.Stop(); modStatusTimer.Dispose(); };
            // A long import error, like a compiler's, is cut to two lines here, and hovering it shows the whole thing.
            ((Note)modStatus).Hovers = true;
            // Mod names can hold an &, like Dice & Decider, so it isn't read as a shortcut key.
            modStatus.UseMnemonic = false;
            modListHead = Section("INSTALLED >", 0, 218);
            modSearch = Input(28, 242, 222, 26);
            modSearch.Cue = "Search by name or author";
            modSearch.Box.AccessibleName = "Search your installed mods";
            Explain(modSearch, "Search installed mods", "MainframeMods.cs", null, "Search installed mods");
            modSearch.Box.TextChanged += delegate { modFirst = 0; FillMods(); };
            modUpdate = Make("Update mods", 256, 242, 100, 26, delegate { UpdateMods(); });
            modUpdate.AccessibleName = "Update your installed mods from the mods directory";
            modUpdateUsual = modUpdate.Accent;
            Explain(modUpdate, "Update mods", "MainframeMods.cs", null, "Update mods");
            modEmpty = ModLabel("No mods yet. Import a " + Mods.Extension + " file.", 28, ModRowTop, 328, 20, Font, Faint);
            modNoMatch = ModLabel("No mods match.", 28, ModRowTop, 328, 20, Font, Faint);
            for (int i = 0; i < MostModRows; i++)
            {
                int row = i, top = ModRowTop + ModRowHeight * i;
                modNames[i] = ModLabel("", 28, top + 3, 168, 20, Type(10f, true), White);
                // Every row's cog in the same place, left of On, drawn like the Mainframe's lock: what that mod, skin or plug-in
                // may do at each SECURITY level.
                modCogs[i] = Make("", 200, top, 26, 22, delegate { OpenCog(row); });
                modSwitches[i] = Make("On", 232, top, 52, 22, delegate { SwitchMod(row); });
                modCogs[i].Glyph = 5;
                Explain(modCogs[i], "Mod settings", "MainframePlugins.cs", null, "Security overrides");
                modRemoves[i] = Make("Remove", 290, top, 66, 22, delegate { RemoveMod(row); });
                modAbouts[i] = ModLabel("", 28, top + 26, 256, 30, Look.Mono(8.5f, FontStyle.Regular, GraphicsUnit.Point), Faint);
                Explain(modSwitches[i], "Mod switch", "MainframeMods.cs", null, "On / Off");
                Explain(modRemoves[i], "Remove", "MainframeMods.cs");
                modPublishes[i] = Make("Publish", 290, top + 28, 66, 22, delegate { PublishMod(row); });
                Explain(modPublishes[i], "Publish mod", "MainframeMods.cs");
                modOptions[i] = Make("Options", 214, top + 28, 70, 22, delegate { OpenModOptions(row); });
                Explain(modOptions[i], "Mod options", "MainframeMods.cs");
            }
            int pagingFrom = Controls.Count, rowsEnd = ModRowTop + ModRowHeight * 4;
            modBack = Make("Back", 28, rowsEnd, 60, 22, delegate { modFirst -= ModRows; FillMods(); });
            modPage = ModLabel("", 96, rowsEnd + 3, 192, 18, Font, Faint);
            modPage.TextAlign = ContentAlignment.MiddleCenter;
            Explain(modBack, "Mod pages", "MainframeMods.cs");
            modNext = Make("Next", 296, rowsEnd, 60, 22, delegate { modFirst += ModRows; FillMods(); });
            Explain(modNext, "Mod pages", "MainframeMods.cs");
            for (int i = pagingFrom; i < Controls.Count; i++) modPaging.Add(new KeyValuePair<Control, int>(Controls[i], Controls[i].Top - rowsEnd));
            // Her art's colours are the CAPRI fold in Custom colours, so one key here opens it.
            int belowFrom = Controls.Count, coloursTop = rowsEnd + PagingTall;
            modColoursHead = Section("COLORS >", 0, coloursTop);
            int colourRow = coloursTop + 24;
            Key colours = Make("Custom colours", 28, colourRow, 328, 26, delegate { OpenColours(); });
            colours.AccessibleName = "Open Custom colours, her own colours at the top";
            Explain(colours, "Her colors", "MainframeMods.cs", null, "Her colors");
            Key colourReset = Make("Reset colors", 28, colourRow + 32, 160, 24, delegate { ResetColours(); });
            Explain(colourReset, "Reset colors", "MainframeMods.cs");
            modColourReset = colourReset;
            for (int i = belowFrom; i < Controls.Count; i++) modBelow.Add(new KeyValuePair<Control, int>(Controls[i], Controls[i].Top - coloursTop));
            for (int i = first; i < Controls.Count; i++)
            {
                Control control = Controls[i];
                modLeft.Add(new KeyValuePair<Control, int>(control, control.Left));
                control.Left += Far;
                Reachable(control, false);
            }
        }

        private Label ModLabel(string text, int x, int y, int width, int height, Font font, Color colour)
        {
            Label label = Caption(text, x, y, font, colour);
            label.AutoSize = false;
            label.AutoEllipsis = true;
            label.Size = new Size(width, height);
            return label;
        }

        internal bool ModsShown { get { return modded; } }

        // Where a skin that's on sits in her stack, said plainly.
        private string Stacked(Mod mod)
        {
            List<Mod> stack = capri.ModList.SkinStack;
            int at = stack.IndexOf(mod);
            if (at < 0) return mod.File.Name + " is off.";
            if (stack.Count == 1) return mod.File.Name + " is on.";
            if (at == 0) return mod.File.Name + " is on, at the bottom, under " + stack[1].File.Name + (stack.Count > 2 ? " and " + (stack.Count - 2) + " more." : ".");
            return mod.File.Name + " is on, on top of " + stack[at - 1].File.Name + (at > 1 ? " and " + (at - 1) + " more." : ".");
        }

        private void CheckModsKey()
        {
            if (more.Left != Scaled(146) || more.Text != "Settings" || modsKey.Top != more.Top || modsKey.Left < more.Right || modsKey.Right > minimize.Left) throw new Exception("Settings or Mods is in the wrong place.");
            try
            {
                foreach (bool fallback in new[] { false, true })
                {
                    LayoutScale(96, 100, fallback);
                    foreach (bool page in new[] { false, true })
                    {
                        ShowMods(page);
                        string crowded = TextFits() ?? Overlap();
                        if (crowded != null) throw new Exception("The Mods page or key overlaps" + (fallback ? " on the fallback font: " : ": ") + crowded);
                    }
                    ShowSettings(false);
                    if (more.Text != "Back") throw new Exception("Settings lost Back while Mods was open.");
                    more.PerformClick();
                    if (modded || paged || more.Text != "Settings") throw new Exception("Back did not leave Mods.");
                }
            }
            finally { LayoutScale(0, 0); ShowMods(false); }
        }

        internal void ShowMods(bool on)
        {
            DismissHint();
            if (on && paged) ShowSettings(false);
            modded = on;
            more.Text = on ? "Back" : "Settings";
            more.Invalidate();
            Place();
            foreach (KeyValuePair<Control, int> entry in mainLeft) Reachable(entry.Key, !on);
            foreach (KeyValuePair<Control, int> entry in modLeft) Reachable(entry.Key, on);
            Reachable(modsKey, !on);
            if (Hidden(ActiveControl) || (on && ActiveControl == modsKey)) more.Focus();
            if (on) FillMods();
            Arrange(expanded);
        }

        // Load untested mods, as her settings have it.
        internal bool UntestedOn { get { return untestedSwitch != null && untestedSwitch.Checked; } }

        // Kept in her settings. Off switches off the running code mods off the checked list there and then.
        private void UntestedChanged()
        {
            capri.SetUntested(untestedSwitch.Checked);
            List<string> off = untestedSwitch.Checked ? new List<string>() : capri.ModList.UncheckedOffNow();
            FillMods();
            if (untestedSwitch.Checked) ModSays("Load untested mods is on. A mod off Capri's checked list stays off until you turn it on in its row, and it asks first.", false);
            else if (off.Count > 0) ModSays("Switched off, not on Capri's checked list: " + string.Join(", ", off.ToArray()) + ".", false);
        }

        // For checks: the switch flipped as a click would.
        internal void TurnUntested(bool on) { untestedSwitch.Checked = on; }

        // For checks: the MODS section with the switch, at 100, 150 and 200%.
        internal List<string> PictureUntested(string folder)
        {
            List<string> shots = new List<string>();
            FormWindowState stateWas = WindowState;
            try
            {
                if (!Visible) Show();
                if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
                foreach (int percent in new[] { 100, 150, 200 })
                {
                    LayoutScale(96, percent, false);
                    ShowMods(true);
                    FillMods();
                    Application.DoEvents();
                    string crowded = TextFits() ?? Overlap();
                    if (crowded != null) throw new Exception("Mods > MODS is crowded at " + percent + "%: " + crowded);
                    string placed = ModsPlaced(percent);
                    if (placed != null) throw new Exception(placed);
                    string file = Path.Combine(folder, "untested-" + percent + ".png");
                    using (Bitmap picture = new Bitmap(Math.Max(1, Width), Math.Max(1, Height)))
                    {
                        DrawToBitmap(picture, new Rectangle(Point.Empty, picture.Size));
                        picture.Save(file, ImageFormat.Png);
                    }
                    shots.Add(Path.GetFileName(file));
                }
            }
            finally
            {
                LayoutScale(0, 0);
                ShowMods(false);
                if (WindowState != stateWas) WindowState = stateWas;
            }
            return shots;
        }

        // For checks: Make a mod spans the three keys above it, COLORS sits under the last row shown with the paging keys
        // clear of its line, and Reset colors fits; with no mods shown too. Null when it's right, else what isn't.
        private string ModsPlaced(int percent)
        {
            string searchWas = modSearch.Box.Text;
            try
            {
                foreach (bool none in new[] { false, true })
                {
                    if (none) { modSearch.Box.Text = "no mod is called this " + Guid.NewGuid().ToString("N"); FillMods(); Application.DoEvents(); }
                    string at = percent + "%" + (none ? " with no mods shown" : "") + ": ";
                    if (modMake.Left != modImport.Left || modMake.Right != modFolder.Right || modMake.Top < modImport.Bottom || untestedSwitch.Top < modMake.Bottom)
                        return "Make a mod doesn't span Import mod to Mods folder between them and the switch at " + at + modMake.Bounds;
                    int lastRow = modEmpty.Visible ? modEmpty.Bottom : modNoMatch.Visible ? modNoMatch.Bottom : modListHead.Bottom;
                    for (int i = 0; i < MostModRows; i++) if (modAbouts[i].Visible) lastRow = Math.Max(lastRow, Math.Max(modAbouts[i].Bottom, modPublishes[i].Bottom));
                    if (modBack.Visible)
                    {
                        if (modBack.Top < lastRow || modColoursHead.Top - modBack.Bottom < Scaled(16)) return "The paging keys touch the rows or COLORS at " + at + "back " + modBack.Bounds + ", COLORS " + modColoursHead.Top;
                        int left = ClientSize.Height - modColourReset.Bottom;
                        if (left > Scaled(ModRowHeight + 24) && ModRows < MostModRows) return "Mods pages without using its height at " + at + left + " px left under Reset colors";
                    }
                    else if (modColoursHead.Top < lastRow || modColoursHead.Top - lastRow > Scaled(ModRowHeight + ColoursGap))
                        return "COLORS isn't straight under the last row shown at " + at + "last row ends " + lastRow + ", COLORS " + modColoursHead.Top;
                    if (modColourReset.Bottom > ClientSize.Height) return "Reset colors is cut off at " + at + modColourReset.Bottom + " in " + ClientSize.Height;
                }
                return null;
            }
            finally { modSearch.Box.Text = searchWas; FillMods(); }
        }

        // What a mod's row on the Mods page says about it, for the checks; null when it isn't shown.
        internal string ModRowAbout(string id)
        {
            int row = ModRowOf(id);
            return row >= 0 && modAbouts[row].Visible ? modAbouts[row].Text : null;
        }

        // A mod's row brought onto the page shown, or -1 when it isn't installed.
        private int ModRowOf(string id)
        {
            int at = Installed().FindIndex(mod => mod.File.Id == id);
            if (at < 0) return -1;
            modSearch.Box.Text = "";
            modFirst = at / ModRows * ModRows;
            FillMods();
            int row = modShown.FindIndex(mod => mod.File.Id == id) - modFirst;
            return row >= 0 && row < ModRows ? row : -1;
        }

        // Its switch pressed, for the checks.
        internal bool SwitchModRow(string id) { int row = ModRowOf(id); return row >= 0 && SwitchMod(row); }

        internal void FillMods()
        {
            if (untestedSwitch != null && untestedSwitch.Checked != capri.UntestedMods)
            {
                bool was = syncing;
                syncing = true;
                untestedSwitch.Checked = capri.UntestedMods;
                syncing = was;
            }
            List<Mod> installed = Installed();
            List<Mod> mods = ShownMods(installed, modSearch.Box.Text);
            modShown = mods;
            int total = mods.Count, pageSize = ModRows;
            modFirst = Math.Max(0, Math.Min(modFirst, (Math.Max(0, total - 1) / pageSize) * pageSize));
            PlaceModRows();
            modEmpty.Visible = installed.Count == 0;
            modNoMatch.Visible = total == 0 && !modEmpty.Visible;
            // Why a mod was switched off for breaking her, said on its own row until it is turned back on.
            Dictionary<string, string> switchedOff = ModGuard.Reasons();
            for (int i = 0; i < MostModRows; i++)
            {
                Mod mod = i < pageSize && modFirst + i < mods.Count ? mods[modFirst + i] : null;
                modNames[i].Visible = modAbouts[i].Visible = modSwitches[i].Visible = modCogs[i].Visible = modRemoves[i].Visible = modPublishes[i].Visible = mod != null;
                bool options = mod != null && mod.Code && mod.On && capri.HasModOptions(mod.File.Id);
                modOptions[i].Visible = options;
                modAbouts[i].Width = Scaled(options ? 180 : 256);
                if (mod == null) continue;
                modCogs[i].AccessibleName = "Open " + (mod.File.Name ?? mod.File.Id) + "'s security overrides";
                if (mod.Hers)
                {
                    // Hers: its name, what it is and its switch; nothing to remove, publish or set overrides for.
                    modCogs[i].Visible = modRemoves[i].Visible = modPublishes[i].Visible = modOptions[i].Visible = false;
                    modSwitches[i].Text = mod.On ? "On" : "Off";
                    modSwitches[i].Accent = mod.On ? Cyan : Rose;
                    modSwitches[i].AccessibleName = (mod.On ? "Turn off " : "Turn on ") + mod.File.Name;
                    modSwitches[i].Invalidate();
                    modNames[i].Text = mod.File.Name;
                    modNames[i].ForeColor = mod.On ? White : Faint;
                    modAbouts[i].Text = "HER OWN. Its parts are in Toggles, its colour in Custom colours.";
                    modAbouts[i].ForeColor = Faint;
                    continue;
                }
                if (mod.Plugin != null)
                {
                    // A plug-in isn't published from here: its name, what it is, its switch, its cog and Remove.
                    modPublishes[i].Visible = false;
                    modSwitches[i].Text = mod.On ? "On" : "Off";
                    modSwitches[i].Accent = mod.On ? Cyan : Rose;
                    modSwitches[i].AccessibleName = (mod.On ? "Turn off " : "Turn on ") + mod.File.Name;
                    modSwitches[i].Invalidate();
                    modNames[i].Text = mod.File.Name + (string.IsNullOrEmpty(mod.File.Version) ? "" : " v" + mod.File.Version);
                    modNames[i].ForeColor = mod.Changed ? Rose : mod.On ? White : Faint;
                    modAbouts[i].Text = PluginAbout(mod.Plugin, Plugins.ToolsOf(mod.Plugin.Id).Count, Plugins.Trouble(mod.Plugin.Id));
                    modAbouts[i].ForeColor = mod.Changed ? Rose : Faint;
                    modRemoves[i].AccessibleName = "Remove " + mod.File.Name;
                    continue;
                }
                modOptions[i].AccessibleName = "Open " + mod.File.Name + "'s options";
                modNames[i].Text = mod.File.Name + (string.IsNullOrEmpty(mod.File.Version) ? "" : " v" + mod.File.Version);
                modNames[i].ForeColor = mod.On ? White : Faint;
                modAbouts[i].Text = ((mod.Changed ? "CHANGED, approve it again. " : mod.Code ? (mod.File.Checked ? "CHECKED BY CAPRI'S MAKERS, NOT A PROMISE IT'S SAFE. " : "") + "RUNS CODE" + (mod.File.Reaches.Length == 0 ? ". " : ": " + string.Join(", ", mod.File.Reaches) + ". ") : mod.Skin ? (mod.On && capri.ModList.SkinStack.Count > 1 ? "SKIN, LAYER " + (capri.ModList.SkinStack.IndexOf(mod) + 1) + " OF " + capri.ModList.SkinStack.Count + ". " : "SKIN. ") : mod.Skill ? "SKILL. " : "") + (string.IsNullOrEmpty(mod.File.Author) ? "" : "by " + mod.File.Author + ". ") + (mod.File.Description ?? "") + (mod.Code || mod.File.WakeNames.Length == 0 ? "" : " Names: " + string.Join(", ", mod.File.WakeNames)) + (mod.Skill && mod.File.Games.Length > 0 ? " For " + mod.File.Games[0] + "." : "") + (mod.File.Colours ? " Has custom colours." : "") + PackHolds(mod)).Trim();
                modAbouts[i].ForeColor = mod.Changed ? Rose : Faint;
                if (mod.Code && mod.On && Mods.StoppedHere(mod.File.Id))
                {
                    modAbouts[i].Text = "STOPPED AT THIS SECURITY LEVEL BY ITS COG. " + modAbouts[i].Text;
                    modAbouts[i].ForeColor = Rose;
                }
                string switchedWhy;
                if (!mod.On && switchedOff.TryGetValue(mod.File.Id, out switchedWhy))
                {
                    modAbouts[i].Text = "SWITCHED OFF: " + switchedWhy + ". " + modAbouts[i].Text;
                    modAbouts[i].ForeColor = Rose;
                }
                modSwitches[i].Text = mod.On ? "On" : "Off";
                modSwitches[i].Accent = mod.On ? Cyan : Rose;
                modSwitches[i].AccessibleName = (mod.On ? "Turn off " : "Turn on ") + mod.File.Name;
                modRemoves[i].AccessibleName = "Remove " + mod.File.Name;
                modPublishes[i].AccessibleName = "Publish " + mod.File.Name + " to the mods directory";
                modSwitches[i].Invalidate();
            }
            bool pages = total > pageSize;
            modBack.Visible = modNext.Visible = modPage.Visible = pages;
            modBack.Enabled = modFirst > 0;
            modNext.Enabled = modFirst + pageSize < total;
            modPage.Text = "Page " + (modFirst / pageSize + 1) + " of " + ((total - 1) / pageSize + 1);
            if (capri.ModList.Problems.Count > 0) ModSays("Skipped " + capri.ModList.Problems[0], true);
        }

        private static List<Mod> ShownMods(List<Mod> mods, string search)
        {
            string[] words = (search ?? "").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return mods.FindAll(delegate(Mod mod)
            {
                string named = (mod.File.Name ?? "") + " " + (mod.File.Author ?? "");
                foreach (string word in words) if (named.IndexOf(word, StringComparison.OrdinalIgnoreCase) < 0) return false;
                return true;
            });
        }

        private void ModSays(string text, bool problem)
        {
            modStatusTimer.Stop();
            modStatus.Text = text;
            modStatus.ForeColor = problem ? Rose : Cyan;
            if (!string.IsNullOrEmpty(text))
            {
                modStatusTimer.Interval = problem ? 10000 : 5000;
                modStatusTimer.Start();
            }
        }

        private void ImportMod()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Import a Capri mod";
                dialog.Filter = "Capri mods (*" + Mods.Extension + ";*" + Mods.CodeExtension + ";*" + Mods.SkinExtension + ";*" + Mods.SkillExtension + ";*" + Mods.PluginExtension + ")|*" + Mods.Extension + ";*" + Mods.CodeExtension + ";*" + Mods.SkinExtension + ";*" + Mods.SkillExtension + ";*" + Mods.PluginExtension;
                if (dialog.ShowDialog(this) == DialogResult.OK) ImportMod(dialog.FileName);
            }
        }

        // A plug-in's switch in its row. It goes in off; on starts its servers and skills, off stops them.
        private bool SwitchPlugin(Mod row)
        {
            try
            {
                bool done = capri.ModList.SetPlugin(row.Plugin.Id, !row.On, PluginAsk);
                FillMods();
                // Her Email on with no account yet: its setup opens, and the row says so.
                if (done && !row.On && Array.Exists(row.Plugin.Servers, one => one.BuiltIn == "mail") && !MailTools.Ready)
                {
                    OpenMailSetup();
                    ModSays(row.File.Name + " is on. Set it up in the window that opened.", false);
                    return true;
                }
                ModSays(!done ? row.File.Name + " stays off. Nothing ran." : row.On ? row.File.Name + " is off. Its servers stopped." : row.Plugin.Servers.Length > 0 ? row.File.Name + " is on. Its tools are starting." : row.File.Name + " is on.", false);
                return done;
            }
            catch (IOException error) { ModSays("Couldn't change that plug-in: " + error.Message, true); }
            catch (UnauthorizedAccessException error) { ModSays("Couldn't change that plug-in: " + error.Message, true); }
            return false;
        }

        // What her box says before a plug-in first runs, when it is switched on: what it starts, and its fingerprint.
        internal static string PluginConsent(PluginInfo plugin)
        {
            StringBuilder starts = new StringBuilder();
            foreach (PluginServer server in plugin.Servers)
                starts.Append("\n- ").Append(server.Name).Append(server.BuiltIn == "mail" ? ": Capri's own mail tools, with the account you set up. She asks you before every email she sends." : server.BuiltIn != null ? ": Capri's own " + server.BuiltIn + " tools" : server.Online ? ": online, at " + server.Url : ": a program on this PC, " + server.Command);
            return (plugin.Name ?? plugin.Id) + (string.IsNullOrEmpty(plugin.Version) ? "" : " v" + plugin.Version) + (string.IsNullOrEmpty(plugin.Author) ? "" : ", which says it's by " + plugin.Author) + ", is a plug-in.\n\n"
                + (plugin.Servers.Length == 0 ? "It starts nothing." : "It starts:" + starts) + (plugin.Skills.Length == 0 ? "" : "\n\nSkills: " + plugin.Skills.Length) + "\n\n"
                + (plugin.Changed ? "It changed since you approved it.\n\n" : "")
                + "A plug-in's programs run on your PC as you, and its online servers get what she sends them.\n\n"
                + "File fingerprint (SHA-256):\n" + plugin.Hash + "\n\nTurn it on?";
        }

        private bool AskToTakePlugin(PluginInfo plugin) { return Question.Ask(this, PluginConsent(plugin), "Turn on this plug-in?"); }

        // The question before a plug-in first runs; the check hands in its own.
        internal Func<PluginInfo, bool> PluginAsk;

        private bool ImportPluginFile(string file)
        {
            try
            {
                PluginInfo plugin = capri.ModList.TakePlugin(file);
                if (plugin == null)
                {
                    ModSays("Not imported. Nothing ran.", false);
                    return false;
                }
                FillMods();
                ModSays((plugin.Name ?? plugin.Id) + " is in, and off. Turning it on in its row asks first.", false);
                return true;
            }
            catch (ModException error)
            {
                Brain.Log("plug-ins", -1, Path.GetFileName(file) + ": " + error.Message);
                FillMods();
                ModSays(error.Message, true);
                return false;
            }
        }

        internal bool ImportMod(string file)
        {
            if (Mods.IsPluginFile(file)) return ImportPluginFile(file);
            try
            {
                Mod mod = capri.ModList.Import(file, ModAsk);
                if (mod == null)
                {
                    ModSays("Not imported. Nothing ran.", false);
                    return false;
                }
                FillMods();
                // A pack that takes over slots another pack holds asks which keeps each.
                AskConflicts();
                ModSays(mod.Skin ? Stacked(mod) : mod.Code ? mod.File.Name + " is on and running." : mod.Skill ? mod.File.Name + " is on." + (mod.File.Games.Length == 0 ? " She can read it when it fits." : " She uses it when she plays " + mod.File.Games[0] + ".") : mod.File.WakeNames.Length == 0 ? mod.File.Name + " is on." : mod.File.Name + " is on. She now answers to " + Mods.Listed(mod.File.WakeNames) + " too.", false);
                return true;
            }
            catch (ModException error)
            {
                // Its whole reason goes to errors.txt too, and the list shows a code mod that saved but didn't start as off.
                Brain.Log("mods", -1, Path.GetFileName(file) + ": " + error.Message);
                FillMods();
                ModSays(error.Message, true);
                return false;
            }
        }

        // An update from a directory card: like an import, but every mod stays on or off as it was.
        internal bool UpdateMod(string file, string id)
        {
            try
            {
                Mod mod = capri.ModList.Update(file, ModAsk);
                if (mod == null)
                {
                    ModSays("Not updated. The old one is still in.", false);
                    return false;
                }
                UpdateDone(id, file);
                FillMods();
                ModSays(mod.File.Name + " is updated" + (mod.File.Version != null ? " to " + mod.File.Version : "") + (mod.On ? "." : ". It's still off."), false);
                return true;
            }
            catch (ModException error)
            {
                Brain.Log("mods", -1, Path.GetFileName(file) + ": " + error.Message);
                FillMods();
                ModSays(error.Message, true);
                return false;
            }
        }

        internal bool ModStatusHovers { get { Note note = modStatus as Note; return note != null && note.Hovers; } }

        private static string DirectoryProblem(Exception error, string lead)
        {
            return error is ModException ? error.Message : lead + error.Message;
        }

        // Make a mod: the mod helper, which has the modding guide a key away.
        private void MakeMod() { OpenModHelper(); }

        private void ResetColours()
        {
            try
            {
                capri.ModList.ResetColours();
                FillMods();
                ModSays("Her own colors are back.", false);
            }
            catch (IOException error) { ModSays("Couldn't reset her colors: " + error.Message, true); }
            catch (UnauthorizedAccessException error) { ModSays("Couldn't reset her colors: " + error.Message, true); }
        }

        private Mod ShownMod(int row)
        {
            int at = modFirst + row;
            if (at < modShown.Count)
            {
                string path = modShown[at].Path;
                string id = modShown[at].File.Id;
                bool plugin = modShown[at].Plugin != null;
                Mod current = Installed().Find(delegate(Mod mod) { return (mod.Plugin != null) == plugin && string.Equals(mod.Path, path, StringComparison.OrdinalIgnoreCase) && string.Equals(mod.File.Id, id, StringComparison.OrdinalIgnoreCase); });
                if (current != null) return current;
            }
            FillMods();
            ModSays("That mod is no longer installed. The list is updated.", true);
            return null;
        }

        private bool SwitchMod(int row)
        {
            Mod mod = ShownMod(row);
            if (mod == null) return false;
            if (mod.Hers)
            {
                capri.SetTamagotchi(!mod.On);
                FillMods();
                ModSays(mod.File.Name + (capri.TamagotchiOn ? " is on." : " is off."), false);
                return true;
            }
            if (mod.Plugin != null) return SwitchPlugin(mod);
            try
            {
                bool done = capri.ModList.Set(mod, !mod.On, ModAsk);
                FillMods();
                if (done && mod.On) AskConflicts();
                ModSays(!done ? mod.File.Name + " stays off. Nothing ran." : mod.Skin ? Stacked(mod) : mod.File.Name + (mod.On ? " is on." : " is off."), false);
                return done;
            }
            catch (ModException error) { ModSays(error.Message, true); }
            catch (IOException error) { ModSays("Couldn't change that mod: " + error.Message, true); }
            catch (UnauthorizedAccessException error) { ModSays("Couldn't change that mod: " + error.Message, true); }
            return false;
        }

        internal bool SwitchModForTest(int row) { return SwitchMod(row); }

        // SECURITY moved or a code mod's cog changed: its Runs row may stop or start it here, and its row says so.
        internal void CogTurned()
        {
            capri.ModsResecured();
            FillMods();
        }

        // The row switch of the mod with this id, and a drop of this file into her mods folder, for checks.
        internal bool SwitchModForTest(string id)
        {
            FillMods();
            int at = modShown.FindIndex(mod => mod.File.Id == id);
            return at >= 0 && SwitchMod(at - modFirst);
        }

        internal void DropForTest(string file)
        {
            dropsKnown.Remove(file);
            TakeDrop(file);
        }

        // A code mod started, stopped or set its options, or one changed her skins: the list shows it, and a stopped mod's options close.
        internal void ModsChanged()
        {
            RefreshKept();
            if (optionsSheet != null && !optionsSheet.IsDisposed && !capri.HasModOptions(optionsSheet.ModId)) optionsSheet.Close();
            if (pageSheet != null && !pageSheet.IsDisposed && !capri.ModRunningNow(pageSheet.ModId)) pageSheet.Close();
            if (modded) FillMods();
            capri.KeepWorkMods();
            RefreshWorkApps();
        }

        private ModOptionsSheet optionsSheet;

        private void OpenModOptions(int row)
        {
            Mod mod = ShownMod(row);
            if (mod == null) return;
            Control made;
            string title;
            string problem = capri.MakeModOptions(mod.File.Id, out made, out title);
            if (problem != null) { ModSays(problem, true); return; }
            if (optionsSheet != null && !optionsSheet.IsDisposed) optionsSheet.Close();
            Size wanted = made.Width > 0 && made.Height > 0 ? made.Size : made.PreferredSize;
            int wide = Math.Max(360, Math.Min(1000, wanted.Width + 48)), tall = Math.Max(160, Math.Min(760, wanted.Height + 84));
            optionsSheet = new ModOptionsSheet(this, SheetZoom(wide, tall), mod.File.Id, title, made, wide, tall);
            optionsSheet.Show(this);
            optionsSheet.Activate();
        }

        internal string ModOptionsTitle { get { return optionsSheet == null || optionsSheet.IsDisposed ? null : optionsSheet.Text; } }

        // A code mod's own page, opened from the mod: one at a time, in a sheet like its options, bigger if it wants.
        private ModOptionsSheet pageSheet;

        internal void OpenModPage(string id, string title, Control made)
        {
            if (pageSheet != null && !pageSheet.IsDisposed) pageSheet.Close();
            Size wanted = made.Width > 0 && made.Height > 0 ? made.Size : made.PreferredSize;
            int wide = Math.Max(360, Math.Min(1200, wanted.Width + 48)), tall = Math.Max(160, Math.Min(900, wanted.Height + 84));
            pageSheet = new ModOptionsSheet(this, SheetZoom(wide, tall), id, title, made, wide, tall);
            pageSheet.Show(this);
            pageSheet.Activate();
        }

        internal void CloseModPage(string id)
        {
            if (pageSheet != null && !pageSheet.IsDisposed && pageSheet.ModId == id) pageSheet.Close();
        }

        internal string ModPageTitle { get { return pageSheet == null || pageSheet.IsDisposed ? null : pageSheet.Text; } }

        internal string OpenModOptionsForTest(int row)
        {
            modFirst = 0;
            OpenModOptions(row);
            return optionsSheet == null || optionsSheet.IsDisposed ? modStatus.Text : optionsSheet.Crowded();
        }

        // A code mod's own options, made by the mod, in a sheet like her other mod windows.
        private sealed class ModOptionsSheet : ModSheet
        {
            internal readonly string ModId;

            internal ModOptionsSheet(Mainframe owner, float zoom, string id, string title, Control made, int wide, int tall)
                : base(owner, zoom, title, title.ToUpperInvariant() + " >", wide, tall)
            {
                ModId = id;
                made.Bounds = Box(24, 60, wide - 48, tall - 84);
                made.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
                Controls.Add(made);
                Frame.Fit(this);
            }
        }
        internal string ModStatusText { get { return modStatus.Text; } }

        internal Func<ModFile, string, bool> ModAsk;

        // ---------- Dropped in ----------
        // A mod put into her mods folder while she runs is taken in like an import: a code mod gets her read and their
        // yes first. Changed or removed files follow; her own writes are known by hash, and nothing runs before a yes.
        private System.IO.FileSystemWatcher modWatch;
        private readonly Dictionary<string, DateTime> dropsDue = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> dropsKnown = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> dropsGone = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Timer dropTick;

        internal void WatchMods()
        {
            if (modWatch != null || capri.Testing) return;
            try
            {
                string folder = capri.ModList.Folder;
                Directory.CreateDirectory(folder);
                foreach (string file in Directory.GetFiles(folder)) if (IsModFile(file)) dropsKnown[file] = DropHash(file);
                modWatch = new System.IO.FileSystemWatcher(folder);
                modWatch.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size;
                System.IO.FileSystemEventHandler seen = delegate(object sender, System.IO.FileSystemEventArgs e) { Dropped(e.FullPath, false); };
                modWatch.Created += seen;
                modWatch.Changed += seen;
                modWatch.Renamed += delegate(object sender, System.IO.RenamedEventArgs e) { Dropped(e.OldFullPath, true); Dropped(e.FullPath, false); };
                modWatch.Deleted += delegate(object sender, System.IO.FileSystemEventArgs e) { Dropped(e.FullPath, true); };
                modWatch.EnableRaisingEvents = true;
                dropTick = new Timer { Interval = 500 };
                dropTick.Tick += delegate { TakeDrops(); };
                dropTick.Start();
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        private static bool IsModFile(string file)
        {
            string kind = Path.GetExtension(file ?? "");
            return string.Equals(kind, Mods.Extension, StringComparison.OrdinalIgnoreCase) || string.Equals(kind, Mods.CodeExtension, StringComparison.OrdinalIgnoreCase)
                || string.Equals(kind, Mods.SkinExtension, StringComparison.OrdinalIgnoreCase) || string.Equals(kind, Mods.SkillExtension, StringComparison.OrdinalIgnoreCase);
        }

        private static string DropHash(string file)
        {
            try
            {
                using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
                    return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(file)));
            }
            catch (Exception) { return null; }
        }

        // Off her window's thread, from the watcher: noted, and taken two seconds after the last change, so a file
        // still being copied in is not read half written.
        private void Dropped(string file, bool gone)
        {
            if (!IsModFile(file)) return;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (gone) dropsGone.Add(file); else dropsGone.Remove(file);
                    dropsDue[file] = DateTime.UtcNow.AddSeconds(2);
                });
            }
            catch (InvalidOperationException) { }
        }

        private void TakeDrops()
        {
            if (dropsDue.Count == 0 || Quitting || IsDisposed) return;
            List<string> ready = new List<string>();
            foreach (KeyValuePair<string, DateTime> one in dropsDue) if (DateTime.UtcNow >= one.Value) ready.Add(one.Key);
            foreach (string file in ready)
            {
                dropsDue.Remove(file);
                try { TakeDrop(file); }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
        }

        private void TakeDrop(string file)
        {
            Mod had = capri.ModList.All.Find(delegate(Mod one) { return string.Equals(one.Path, file, StringComparison.OrdinalIgnoreCase); });
            if (dropsGone.Contains(file) || !File.Exists(file))
            {
                dropsGone.Remove(file);
                dropsKnown.Remove(file);
                if (had == null || File.Exists(file)) return;
                capri.ModList.Remove(had);
                FillMods();
                ModSays(had.File.Name + " was taken out of the mods folder, so it's off and gone.", false);
                Brain.Log("mods", -1, had.File.Name + ": taken out of the mods folder, so taken out of her.");
                ModsChanged();
                return;
            }
            string hash = DropHash(file), known;
            if (hash == null) { dropsDue[file] = DateTime.UtcNow.AddSeconds(2); return; }
            if (dropsKnown.TryGetValue(file, out known) && known == hash) return;
            if (had != null && had.Hash != null && had.Hash == DropHashOfCode(file) && !had.Changed) { dropsKnown[file] = hash; return; }
            dropsKnown[file] = hash;
            Brain.Log("mods", -1, Path.GetFileName(file) + ": dropped into the mods folder, so taken in the way Import takes one.");
            // A code mod's question needs them to see it, so her Mainframe comes up for it.
            if (string.Equals(Path.GetExtension(file), Mods.CodeExtension, StringComparison.OrdinalIgnoreCase) && !Visible) { Show(); Activate(); }
            ImportMod(file);
            string after = DropHash(file);
            if (after != null) dropsKnown[file] = after;
            ModsChanged();
        }

        // A code mod's hash as her mod list keeps it, which is over the text as read, not the raw bytes.
        private string DropHashOfCode(string file)
        {
            if (!string.Equals(Path.GetExtension(file), Mods.CodeExtension, StringComparison.OrdinalIgnoreCase)) return null;
            try { return Mods.HashOf(File.ReadAllBytes(file)); }
            catch (Exception) { return null; }
        }

        // A mod dropped straight into her folder is taken in, the same file again is left alone, and one taken out
        // goes. The watcher is off in a test run, so this drives what it would hand over.
        internal void CheckDrops(List<string> checks)
        {
            Mods mods = capri.ModList;
            string file = Path.Combine(mods.Folder, "drop-test" + Mods.Extension);
            try
            {
                Directory.CreateDirectory(mods.Folder);
                File.WriteAllText(file, "{\"format\":1,\"id\":\"drop-test\",\"name\":\"Drop Test\",\"version\":\"1.0.0\",\"author\":\"Avery\",\"description\":\"Answers to one more name.\",\"wake_names\":[\"Zelda\"]}");
                dropsKnown.Remove(file);
                TakeDrop(file);
                Mod taken = mods.All.Find(delegate(Mod one) { return one.File.Id == "drop-test"; });
                bool inAndOn = taken != null && taken.On && Hands.HeardName("Zelda, what time is it?");
                int before = mods.All.Count;
                TakeDrop(file);
                bool leftAlone = mods.All.Count == before;
                File.Delete(file);
                dropsGone.Add(file);
                TakeDrop(file);
                bool gone = !mods.All.Exists(delegate(Mod one) { return one.File.Id == "drop-test"; }) && !Hands.HeardName("Zelda, what time is it?");
                if (!inAndOn || !leftAlone || !gone)
                    throw new Exception("A mod dropped into her folder went wrong: taken in and on " + inAndOn + ", the same file left alone " + leftAlone + ", gone when taken out " + gone);
                checks.Add("PASS a mod dropped into her mods folder is taken in the way Import takes one, the same file again is left alone, and one taken out of the folder comes out of her");
            }
            finally
            {
                Mod left = mods.All.Find(delegate(Mod one) { return one.File.Id == "drop-test"; });
                if (left != null) try { mods.Remove(left); } catch (Exception) { }
                if (File.Exists(file)) File.Delete(file);
            }
        }

        // Her questions about a code mod, answered by a check when it sets this.
        private Func<string, string, bool> modBoxForCheck;

        private bool AskModBox(string text, string caption)
        {
            Func<string, string, bool> stand = modBoxForCheck;
            return stand != null ? stand(text, caption) : Question.Ask(this, text, caption);
        }

        private bool AskToRun(ModFile mod, string hash)
        {
            // Before the approve box, her brain can lay out code that can't be read, once they know where it goes.
            string explained = null;
            string where = ModReadout.Where == null ? null : ModReadout.Where();
            if (where != null && mod.Source != null && mod.Unreadable != null && AskModBox(ModReadout.Offer(mod, where), "Have her read it first?"))
                explained = ModReadout.Run(mod, where);
            if (!AskModBox(Mods.ReadingNote(explained) + Mods.Consent(mod, hash), "Run this code mod?")) return false;
            // Code nobody can read is asked about again, on its own, after the first yes.
            if (!mod.AskTwice) return true;
            return AskModBox(Mods.Unclear(mod, explained), "Run code you can't read?");
        }

        // The offer comes before the first question for any code that can't be read, and never for code that can.
        internal void CheckModOffer(List<string> checks, string output)
        {
            Func<string> whereWas = ModReadout.Where;
            Func<string, string, string> askWas = ModReadout.Ask;
            Action<string> openWas = ModReadout.Open;
            string folderWas = ModReadout.CopyFolder;
            const string offer = "Have her read it first?", first = "Run this code mod?", second = "Run code you can't read?";
            string head = "// capri-mod: 1\n// id: offer\n// name: test\n// version: 1\n// author: Smoke\n// description: A test.\nusing System;\n";
            byte[] art = new byte[900];
            art[0] = 0x89; art[1] = 0x50; art[2] = 0x4E; art[3] = 0x47;
            string packed = Convert.ToBase64String(art);
            ModFile picture = Mods.ReadCode(head + "class M { string art = \"" + packed + "\"; }");
            ModFile runner = Mods.ReadCode(head + "class M { string art = \"" + packed + "\"; void Go() { System.Diagnostics.Process.Start(\"x\"); } }");
            ModFile plain = Mods.ReadCode(head + "class M { void Go() { } }");
            List<string> boxes = new List<string>();
            string firstBox = null;
            bool readIt = true, approve = true;
            Func<ModFile, string> order = delegate(ModFile mod)
            {
                boxes.Clear();
                firstBox = null;
                AskToRun(mod, "abc");
                return string.Join(" | ", boxes.ToArray());
            };
            try
            {
                ModReadout.CopyFolder = Path.Combine(output, "mod-offer");
                Directory.CreateDirectory(ModReadout.CopyFolder);
                ModReadout.Open = null;
                ModReadout.Where = delegate { return "a stand-in"; };
                ModReadout.Ask = delegate(string rules, string code) { return "// laid out\n" + ModReadout.Body(code); };
                modBoxForCheck = delegate(string text, string caption)
                {
                    boxes.Add(caption);
                    if (caption == first) { firstBox = text; return approve; }
                    return caption == offer ? readIt : true;
                };
                string pictureOrder = order(picture);
                bool pictureNamed = firstBox != null && firstBox.StartsWith("Her brain's reading of it is saved here:", StringComparison.Ordinal);
                string runnerOrder = order(runner), plainOrder = order(plain);
                readIt = false;
                string declined = order(picture);
                bool declinedPlain = firstBox != null && firstBox.IndexOf("Her brain's reading", StringComparison.Ordinal) < 0;
                readIt = true;
                approve = false;
                string refused = order(runner);
                if (pictureOrder != offer + " | " + first || !pictureNamed || runnerOrder != offer + " | " + first + " | " + second || plainOrder != first
                    || declined != offer + " | " + first || !declinedPlain || refused != offer + " | " + first)
                    throw new Exception("Her offer to read a mod wasn't before the approve box: packed art " + pictureOrder + " (copy named " + pictureNamed + "), a runner " + runnerOrder + ", readable " + plainOrder + ", declined " + declined + ", refused " + refused);
                checks.Add("PASS her offer to read code that can't be read comes before the approve box, also for packed data with no second question; the first box names her copy; readable code gets no offer; No to the offer still asks, and No to the box stops there");
            }
            finally
            {
                modBoxForCheck = null;
                ModReadout.Where = whereWas;
                ModReadout.Ask = askWas;
                ModReadout.Open = openWas;
                ModReadout.CopyFolder = folderWas;
            }
        }

        // Whether to delete a removed mod's own saved data too. Only asked when it has some.
        internal Func<Mod, bool> DataAsk;

        private bool AskDropData(Mod mod)
        {
            return Question.Ask(this, mod.File.Name + " kept data of its own while it ran, like its settings. Delete that too? No keeps it, so adding the mod again finds it as it was.", "Delete its data too?");
        }

        private void RemoveMod(int row)
        {
            Mod mod = ShownMod(row);
            if (mod == null) return;
            if (mod.Plugin != null) RemovePlugin(mod.Plugin);
            else RemoveWithData(mod);
        }

        internal void RemoveWithData(Mod mod)
        {
            try
            {
                bool had = capri.ModList.HasData(mod);
                bool drop = had && DataAsk != null && DataAsk(mod);
                capri.ModList.Remove(mod);
                if (drop) capri.ModList.DropData(mod);
                FillMods();
                ModSays(mod.File.Name + " is removed" + (!had ? "." : drop ? ", with its data." : ". Its data is kept."), false);
            }
            catch (IOException error) { ModSays("Couldn't remove that mod: " + error.Message, true); }
            catch (UnauthorizedAccessException error) { ModSays("Couldn't remove that mod: " + error.Message, true); }
        }

        private void OpenModsFolder()
        {
            try
            {
                Directory.CreateDirectory(capri.ModList.Folder);
                using (Process.Start("explorer.exe", "\"" + capri.ModList.Folder + "\"")) { }
            }
            catch (Exception error)
            {
                if (!(error is IOException || error is UnauthorizedAccessException || error is System.ComponentModel.Win32Exception)) throw;
                ModSays("Couldn't open the mods folder: " + error.Message, true);
            }
        }

        internal void CheckModData(List<string> checks, string output)
        {
            Mods mods = capri.ModList;
            Func<Mod, bool> askWas = DataAsk;
            int asked = 0;
            bool answer = false;
            DataAsk = delegate { asked++; return answer; };
            string file = Path.Combine(output, "data-check" + Mods.Extension);
            string body = "{\"format\":" + Mods.Format + ",\"id\":\"data-check\",\"name\":\"Data Check\",\"version\":\"1.0.0\",\"author\":\"Checks\",\"wake_names\":[\"Zedra\"]}";
            Mod mod = null;
            try
            {
                Func<Mod> add = delegate
                {
                    File.WriteAllText(file, body);
                    Mod added = mods.Import(file, ModAsk);
                    if (added == null) throw new Exception("The data check's mod was refused.");
                    return added;
                };
                // No data: removed without a question.
                mod = add();
                RemoveWithData(mod);
                bool quiet = asked == 0;
                // Data, and No: the mod goes, its data stays for when it comes back.
                mod = add();
                Directory.CreateDirectory(mods.DataOf(mod));
                File.WriteAllText(Path.Combine(mods.DataOf(mod), "store.json"), "{}");
                answer = false;
                RemoveWithData(mod);
                bool kept = asked == 1 && Directory.Exists(mods.DataOf(mod)) && !File.Exists(mod.Path);
                // Turning it off and on never asks.
                mod = add();
                mods.Set(mod, false, ModAsk);
                mods.Set(mod, true, ModAsk);
                bool offQuiet = asked == 1 && Directory.Exists(mods.DataOf(mod));
                // Data, and Yes: both go.
                answer = true;
                RemoveWithData(mod);
                bool dropped = asked == 2 && !Directory.Exists(mods.DataOf(mod)) && !File.Exists(mod.Path);
                mod = null;
                if (!quiet || !kept || !offQuiet || !dropped)
                    throw new Exception("Removing a mod's data went wrong: no data asked nothing " + quiet + ", No kept it " + kept + ", off never asked " + offQuiet + ", Yes deleted it " + dropped);
                checks.Add("PASS removing a mod asks whether to delete its own data only when it has some: Yes deletes the folder, No keeps it for when the mod comes back, and turning a mod off or on never asks");
            }
            finally
            {
                DataAsk = askWas;
                Mod left = mods.All.Find(one => one.File.Id == "data-check");
                if (left != null) { try { mods.DropData(left); mods.Remove(left); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); } }
                if (File.Exists(file)) File.Delete(file);
            }
        }

        // The Mods page as it is now, saved as a picture, and the words on a mod's row; for checks.
        internal string PictureMods(string file, string id)
        {
            FormWindowState wasState = WindowState;
            Restore();
            ShowMods(true);
            FillMods();
            Application.DoEvents();
            using (Bitmap page = new Bitmap(Width, Height))
            {
                DrawToBitmap(page, new Rectangle(Point.Empty, Size));
                page.Save(file, ImageFormat.Png);
            }
            string row = null;
            for (int i = 0; i < ModRows && modShown != null; i++)
                if (modFirst + i < modShown.Count && modShown[modFirst + i].File.Id == id) row = modAbouts[i].Text;
            WindowState = wasState;
            return row;
        }

        internal void CheckMods(List<string> checks, string output)
        {
            FormWindowState wasState = WindowState;
            Restore();
            Mods mods = capri.ModList;
            Func<string, string, string> sample = delegate(string name, string body)
            {
                string file = Path.Combine(output, name);
                File.WriteAllText(file, body);
                return file;
            };
            string good = sample("custom-names" + Mods.Extension, "{\"format\":1,\"id\":\"custom-names\",\"name\":\"Custom Names\",\"version\":\"1.0.0\",\"author\":\"Avery\",\"description\":\"Answers to more names.\",\"wake_names\":[\"Nova\",\"Big Z\",\"Capricorn\"]}");
            foreach (Mod old in mods.All.ToArray()) mods.Remove(old);
            if (Hands.HeardName("Nova, what time is it?") || !Hands.HeardName("Capri, what time is it?")) throw new Exception("Her names were wrong before any mod.");
            ShowMods(true);
            if (!modded || more.Text != "Back" || modStatus.Left >= Far || modsKey.Left < Far) throw new Exception("The Mods page did not open, or it kept a Mods key to itself.");
            if (!ImportMod(good)) throw new Exception("A good mod was refused: " + modStatus.Text);
            if (mods.All.Count != 1 || !File.Exists(Path.Combine(mods.Folder, "custom-names" + Mods.Extension))) throw new Exception("An imported mod was not saved.");
            if (!Hands.HeardName("hey nova, what time is it") || !Hands.HeardName("BIG Z open notepad") || !Hands.HeardName("big   z") || !Hands.HeardName("ask capricorn") || !Hands.HeardName("Capri, hello"))
                throw new Exception("An imported name was not heard.");
            if (Hands.HeardName("the supernova was bright") || Hands.HeardName("novas are bright") || Hands.HeardName("big zebra"))
                throw new Exception("Part of a word counted as her name.");
            if (Hands.NoteFor(Hands.KitFor()).IndexOf("They can also call you Big Z, Capricorn or Nova", StringComparison.Ordinal) < 0 && Hands.NoteFor(Hands.KitFor()).IndexOf("They can also call you Nova, Big Z or Capricorn", StringComparison.Ordinal) < 0)
                throw new Exception("Her brain was not told her nicknames.");
            checks.Add("PASS mods: an imported mod adds whole-word names and tells her brain");
            using (Bitmap page = new Bitmap(Width, Height))
            {
                DrawToBitmap(page, new Rectangle(Point.Empty, Size));
                page.Save(Path.Combine(output, "mods-page.png"), ImageFormat.Png);
            }
            SwitchMod(0);
            if (Hands.HeardName("hey nova") || !Hands.HeardName("hey capri") || Hands.NoteFor(Hands.KitFor()).Contains("They can also call you")) throw new Exception("Turning a mod off left its names on.");
            mods.Load();
            if (mods.All[0].On) throw new Exception("A mod turned off came back on after loading.");
            SwitchMod(0);
            if (!Hands.HeardName("hey nova")) throw new Exception("Turning a mod back on did not bring its names back.");
            checks.Add("PASS mods: on and off apply straight away and off survives a reload");
            string[,] bad =
            {
                { "bad-json", "{nope" },
                { "bad-format", "{\"format\":99,\"id\":\"a\",\"name\":\"A\",\"wake_names\":[\"Nova\"]}" },
                { "bad-path", "{\"format\":1,\"id\":\"../../evil\",\"name\":\"A\",\"wake_names\":[\"Nova\"]}" },
                { "bad-name", "{\"format\":1,\"id\":\"a\",\"name\":\"A\",\"wake_names\":[\".*\"]}" },
                { "bad-short", "{\"format\":1,\"id\":\"a\",\"name\":\"A\",\"wake_names\":[\"x\"]}" },
                { "bad-empty", "{\"format\":1,\"id\":\"a\",\"name\":\"A\",\"wake_names\":[]}" }
            };
            for (int i = 0; i < bad.GetLength(0); i++)
                if (ImportMod(sample(bad[i, 0] + Mods.Extension, bad[i, 1])) || mods.All.Count != 1) throw new Exception("A bad mod was let in: " + bad[i, 0]);
            if (ImportMod(sample("wrong.txt", "{}"))) throw new Exception("A file that isn't a mod was let in.");
            if (Directory.GetFiles(mods.Folder, "*" + Mods.Extension).Length != 1) throw new Exception("A refused mod was left in her folder.");
            checks.Add("PASS mods: broken files, bad ids and bad names are refused");
            RemoveMod(0);
            if (mods.All.Count != 0 || Hands.HeardName("hey nova")) throw new Exception("Removing a mod left it behind.");
            ShowSettings(true);
            if (modded || !paged || modStatus.Left < Far) throw new Exception("Settings did not take over from Mods.");
            ShowMods(true);
            if (paged || !modded || modStatus.Left >= Far) throw new Exception("Mods did not take over from Settings.");
            ShowMods(false);
            if (modded || modStatus.Left < Far) throw new Exception("Back did not leave Mods.");
            CheckModsKey();
            checks.Add("PASS mods key: Mods sits in the CAPRI band, Settings stays where it was and turns into Back on the Mods page, and nothing overlaps with her own font or the fallback");
            using (Bitmap page = new Bitmap(Width, Height))
            {
                DrawToBitmap(page, new Rectangle(Point.Empty, Size));
                page.Save(Path.Combine(output, "main-page.png"), ImageFormat.Png);
            }
            WindowState = wasState;
            checks.Add("PASS mods: removing works and Mods and Settings swap cleanly");
            string ears = sample("ears" + Mods.Extension, "{\"format\":1,\"id\":\"ears\",\"name\":\"Ears\",\"sounds_like\":true,\"heard_as\":[\"Coppering\"],\"wake_names\":[\"Choom\"]}");
            string[] missedBefore = { "Copri, what time is it", "hey coppery", "Yokapri what's up", "CAH Bree are you there", "Shum, what time is it", "hey coppering" };
            string[] stayQuiet = { "copy that file", "the copper ore", "a cobra", "show me the map", "that's a shame", "in the room", "keeper of the keys", "hum a song" };
            foreach (string line in missedBefore) if (Hands.HeardName(line)) throw new Exception("Heard a sound-alike with no mod on: " + line);
            if (!ImportMod(ears)) throw new Exception("A sounds_like mod was refused: " + modStatus.Text);
            foreach (string line in missedBefore) if (!Hands.HeardName(line)) throw new Exception("Sounds like missed: " + line);
            foreach (string line in stayQuiet) if (Hands.HeardName(line)) throw new Exception("Sounds like fired on: " + line);
            if (!Hands.NoteFor(Hands.KitFor()).Contains("They can also call you Choom.")) throw new Exception("Her brain wasn't told only her real nickname.");
            SwitchMod(0);
            if (Hands.HeardName("hey coppery") || !Hands.HeardName("hey capri")) throw new Exception("Turning sounds like off left it on.");
            RemoveMod(0);
            checks.Add("PASS mods: sounds like catches her ears' misspellings, skips everyday words and switches off cleanly");
            string log = sample("log" + Mods.Extension, "{\"format\":1,\"id\":\"log\",\"name\":\"Log\",\"hearing_log\":true}");
            mods.LogHearing("nothing on yet", false);
            if (File.Exists(mods.LogPath)) throw new Exception("She kept a hearing log with no mod asking.");
            if (!ImportMod(log)) throw new Exception("A hearing log mod was refused: " + modStatus.Text);
            mods.LogHearing("hello there", false);
            if (!File.ReadAllText(mods.LogPath).Contains("ignored | hello there")) throw new Exception("The hearing log didn't record a line.");
            RemoveMod(0);
            if (File.Exists(mods.LogPath)) throw new Exception("Removing the hearing log mod left its list behind.");
            checks.Add("PASS mods: the hearing log only runs while its mod is on, and goes with it");
            CheckModCogs(checks, output);
            CheckDirectory(checks, output);
        }
    }
}
