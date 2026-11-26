using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Make a mod: the mod helper. Her slots by file, a slot's text to change or take from a file, a pack built from them
    // and laid on her with Reload to see it live, and saved ready to drop in.
    internal sealed partial class Mainframe
    {
        private ModHelperSheet modHelper;

        internal void OpenModHelper()
        {
            if (modHelper != null && !modHelper.IsDisposed && modHelper.Zoom != zoom) modHelper.Close();
            if (modHelper == null || modHelper.IsDisposed) modHelper = new ModHelperSheet(this, SheetZoom(ModHelperSheet.Wide, ModHelperSheet.Tall));
            if (!modHelper.Visible) modHelper.Show(this);
            modHelper.Activate();
        }

        internal void CloseModHelper() { if (modHelper != null && !modHelper.IsDisposed) modHelper.Close(); }

        // Make a mod's helper at 100, 150 and 200, driven, with a picture of each.
#if CHECKS
        internal void CheckModHelper(List<string> checks, string output)
        {
            float machine = Px(1000) / 1000f;
            List<string> wrongs = new List<string>();
            foreach (float at in new[] { 1f, 1.5f, 2f })
            {
                string wrong = ModHelperAt(at / machine, Path.Combine(output, "mod-helper-" + (int)(at * 100) + ".png"));
                if (wrong != null) wrongs.Add((int)(at * 100) + "% " + wrong);
            }
            if (wrongs.Count > 0) throw new Exception("Make a mod's helper went wrong: " + string.Join(" / ", wrongs.ToArray()));
            checks.Add("PASS Make a mod opens the mod helper, fitting at 100%, 150% and 200%: a file's parts listed, a part's own text in the box, changed and added to a pack, Reload lays it on her and her lines show it, taking it off gives hers back, Save pack writes a pack that imports, and Take out empties it (pictures mod-helper-*.png)");
        }
#endif

#if CHECKS
        // For checks: the pack Make a mod writes as an app plugging into the Planner, picked from its own list.
        internal string ModHelperKinds()
        {
            using (ModHelperSheet sheet = new ModHelperSheet(this, 1f))
            {
                sheet.KindOf("app");
                KeyValuePair<string, string> planner = sheet.Apps().Find(one => one.Key == "planner");
                if (planner.Key == null) return "no Planner in Plugs into";
                sheet.PlugInto(planner.Key, planner.Value);
                return sheet.Json();
            }
        }
#endif

        // For checks: the helper driven at one size; null when it all went right.
        internal string ModHelperAt(float zoomAt, string picture)
        {
            CloseModHelper();
            using (ModHelperSheet drawn = new ModHelperSheet(this, zoomAt))
            {
                drawn.Show(this);
                try
                {
                    string problem = drawn.Drive(picture);
                    return problem == null ? null : problem + " (" + drawn.ClientSize.Width + " x " + drawn.ClientSize.Height + " at " + zoomAt + ")";
                }
                finally { drawn.Close(); }
            }
        }

        private sealed class ModHelperSheet : ModSheet
        {
            internal const int Wide = 760, Tall = 626, ListWide = 266, ListTall = 330, Right = 330;
            private readonly Entry packName = new Entry(), text = new Entry();
            private readonly Key fileKey, addKey, outKey, kindKey, plugKey;
            private readonly ContextMenuStrip fileMenu, kindMenu, plugMenu;
            // Its kind (mod or app) and the app it plugs into, by id, written into the pack.
            private string kind = "mod", plugsInto;
            private readonly ScrollPanel headings;
            private readonly SlimBar headingsBar = new SlimBar(), textBar = new SlimBar();
            private readonly Label slotName, slotKind, packed, notice;
            private readonly Clock.Ticker noticeTimer = new Clock.Ticker("mod helper notice");
            // The pack as it is being made: each slot's text, in the order they went in.
            private readonly List<KeyValuePair<string, string>> pack = new List<KeyValuePair<string, string>>();
            private string file, slot;

            internal ModHelperSheet(Mainframe owner, float zoom) : base(owner, zoom, "Mod maker", "MOD MAKER >", Wide, Tall)
            {
                Pair(false);
                Tucks(null);
                Words("", 28, 52, 600, 18, Small(), Faint);
                Words("Pack name", 28, 82, 90, 26, Font, Rose);
                packName.Bounds = Box(120, 80, 260, 26);
                packName.Box.Text = "My pack";
                packName.Box.AccessibleName = packName.AccessibleName = "Pack name";
                Controls.Add(packName);
                Key guide = Make("Guide", 612, 80, 120, 26, delegate { OpenGuide(); });
                guide.AccessibleName = "Open the modding guide, MODDING.md";
                Words("Kind", 28, 118, 90, 26, Font, Rose);
                kindKey = Make("Mod", 120, 118, 150, 26, delegate { ShowKinds(); });
                kindKey.Chevron = true;
                kindKey.AccessibleName = "Kind: mod or app";
                kindMenu = owner.Dropdown();
                kindMenu.AccessibleName = "Kinds";
                Words("Plugs into", 300, 118, 80, 26, Font, Rose);
                plugKey = Make("None", 388, 118, 192, 26, delegate { ShowApps(); });
                plugKey.Chevron = true;
                plugKey.AccessibleName = "The app it plugs into";
                plugMenu = owner.Dropdown();
                plugMenu.AccessibleName = "Apps";

                fileKey = Make("Pick a file", 28, 156, ListWide, 26, delegate { ShowFiles(); });
                fileKey.Chevron = true;
                fileKey.AccessibleName = "Her file to pick a part of";
                fileMenu = owner.Dropdown();
                fileMenu.AccessibleName = "Her files";
                headings = new ScrollPanel { Bounds = Box(28, 190, ListWide, ListTall), BackColor = Ground };
                headings.Scrolled += delegate { headingsBar.Invalidate(); };
                Controls.Add(headings);
                headingsBar.Bounds = new Rectangle(Z(28 + ListWide + 4), Z(190), Z(SlimBar.Thin), Z(ListTall));
                headingsBar.BackColor = Ground;
                headingsBar.Watch(headings);
                Controls.Add(headingsBar);

                slotName = Words("", Right, 156, 402, 22, Type(10f * Zoom, true), Rose);
                slotKind = Words("", Right, 180, 402, 18, Small(), Faint);
                text.Bounds = Box(Right, 204, 392, 256);
                text.Box.Multiline = true;
                text.Box.AcceptsReturn = true;
                text.Box.ScrollBars = ScrollBars.None;
                text.Box.AccessibleName = text.AccessibleName = "The part's text";
                Controls.Add(text);
                textBar.Bounds = new Rectangle(Z(Right + 396), Z(204), Z(SlimBar.Thin), Z(256));
                textBar.BackColor = Ground;
                textBar.Watch(text.Box);
                Controls.Add(textBar);
                Key hers = Make("Her text", Right, 468, 96, 26, delegate { HerText(); });
                hers.AccessibleName = "Put her own text for this part in the box";
                Key fromFile = Make("From a file", Right + 104, 468, 110, 26, delegate { FromFile(); });
                fromFile.AccessibleName = "Put a file's text in the box";
                addKey = Make("Add to pack", Right + 222, 468, 104, 26, delegate { AddToPack(); });
                addKey.Accent = Cyan;
                outKey = Make("Take out", Right + 334, 468, 68, 26, delegate { TakeOut(); });
                outKey.AccessibleName = "Take this part out of the pack";
                packed = Words("", Right, 502, 402, 40, Small(), Faint);

                notice = Words("", 28, 528, 290, 40, Small(), Cyan);
                Key reload = Make("Reload", 28, 576, 160, 28, delegate { Reload(); });
                reload.Accent = Cyan;
                reload.Loud = true;
                reload.AccessibleName = "Reload: lay the pack on her now";
                Key off = Make("Take it off her", 196, 576, 140, 28, delegate { TakeOff(); });
                off.AccessibleName = "Take the pack off her";
                Key save = Make("Save pack", 572, 576, 160, 28, delegate { SavePack(); });
                save.AccessibleName = "Save the pack ready to drop in";

                noticeTimer.Interval = 8000;
                noticeTimer.Tick += delegate { Says("", false); };
                Disposed += delegate { noticeTimer.Dispose(); fileMenu.Dispose(); kindMenu.Dispose(); plugMenu.Dispose(); };
                ShowPack();
                Pick(FileSlots.Files().Count > 0 ? FileSlots.Files()[0] : null);
                ResumeLayout(false);
            }

            private void Says(string said, bool problem)
            {
                noticeTimer.Stop();
                notice.Text = said ?? "";
                notice.ForeColor = problem ? Rose : Cyan;
                if (!string.IsNullOrEmpty(said)) noticeTimer.Start();
            }

            private void ShowFiles()
            {
                foreach (ToolStripItem old in new List<ToolStripItem>(Items(fileMenu))) { fileMenu.Items.Remove(old); old.Dispose(); }
                foreach (string one in FileSlots.Files())
                {
                    string name = one;
                    fileMenu.Items.Add(Choice(name, name == file, delegate { Pick(name); }));
                }
                fileMenu.Show(fileKey, new Point(0, fileKey.Height));
            }

            private void ShowKinds()
            {
                foreach (ToolStripItem old in new List<ToolStripItem>(Items(kindMenu))) { kindMenu.Items.Remove(old); old.Dispose(); }
                kindMenu.Items.Add(Choice("Mod", kind == "mod", delegate { KindOf("mod"); }));
                kindMenu.Items.Add(Choice("App", kind == "app", delegate { KindOf("app"); }));
                kindMenu.Show(kindKey, new Point(0, kindKey.Height));
            }

            // None, or each app on her: her tools and the plug-ins and app mods installed, by the id a pack names.
            private void ShowApps()
            {
                foreach (ToolStripItem old in new List<ToolStripItem>(Items(plugMenu))) { plugMenu.Items.Remove(old); old.Dispose(); }
                plugMenu.Items.Add(Choice("None", plugsInto == null, delegate { PlugInto(null, "None"); }));
                foreach (KeyValuePair<string, string> app in Apps())
                {
                    KeyValuePair<string, string> one = app;
                    plugMenu.Items.Add(Choice(one.Value, plugsInto == one.Key, delegate { PlugInto(one.Key, one.Value); }));
                }
                plugMenu.Show(plugKey, new Point(0, plugKey.Height));
            }

            internal List<KeyValuePair<string, string>> Apps()
            {
                List<KeyValuePair<string, string>> all = new List<KeyValuePair<string, string>>();
                foreach (Tool tool in Frame.Tools())
                {
                    if (tool.Soon) continue;
                    string id = tool.Id.StartsWith("plugin:", StringComparison.Ordinal) ? tool.Id.Substring(7) : tool.Id.StartsWith("mod:", StringComparison.Ordinal) ? tool.Id.Substring(4) : tool.Id;
                    all.Add(new KeyValuePair<string, string>(tool.Icon == "mail" ? "email" : id, tool.Short));
                }
                return all;
            }

            internal void PlugInto(string id, string named) { plugsInto = id; plugKey.Text = named; }

            internal void KindOf(string to) { kind = to == "app" ? "app" : "mod"; kindKey.Text = kind == "app" ? "App" : "Mod"; }

            private static List<ToolStripItem> Items(ToolStrip strip)
            {
                List<ToolStripItem> all = new List<ToolStripItem>();
                foreach (ToolStripItem one in strip.Items) all.Add(one);
                return all;
            }

            // A file picked: the whole file first, then each heading, as slots.md lists them.
            private void Pick(string name)
            {
                file = name;
                fileKey.Text = name ?? "Pick a file";
                headings.SuspendLayout();
                foreach (Control old in Children(headings)) { headings.Controls.Remove(old); old.Dispose(); }
                headings.AutoScrollPosition = Point.Empty;
                int y = 0;
                foreach (FileSlots.Slot one in FileSlots.All())
                {
                    if (!string.Equals(one.File, name, StringComparison.OrdinalIgnoreCase)) continue;
                    string slotOf = one.Name;
                    Key row = new Key { Text = (one.Heading ?? "Whole file") + (Has(slotOf) ? "  *" : ""), Font = Type(9f * Zoom), Bounds = new Rectangle(0, Z(y), Z(ListWide), Z(26)) };
                    row.AccessibleName = slotOf + ", " + one.Kind + (Has(slotOf) ? ", in your pack" : "");
                    if (Has(slotOf)) row.Accent = Cyan;
                    row.Click += delegate { Choose(slotOf); };
                    headings.Controls.Add(row);
                    y += 30;
                }
                headings.ResumeLayout();
                headingsBar.Invalidate();
                Choose(null);
            }

            private static List<Control> Children(Control within)
            {
                List<Control> all = new List<Control>();
                foreach (Control one in within.Controls) all.Add(one);
                return all;
            }

            private bool Has(string name) { return pack.Exists(one => string.Equals(one.Key, name, StringComparison.OrdinalIgnoreCase)); }

            private void Choose(string name)
            {
                slot = name;
                FileSlots.Slot found = name == null ? null : FileSlots.Find(name);
                slotName.Text = name ?? "Pick a part on the left";
                slotKind.Text = found == null ? "" : found.Heading == null ? "The whole file: your text stands in for all of it." : "Holds " + found.Kind + (found.Rows > 0 ? ", " + found.Rows + (found.Rows == 1 ? " row" : " rows") : "") + ".";
                int at = pack.FindIndex(one => string.Equals(one.Key, name, StringComparison.OrdinalIgnoreCase));
                text.Box.Text = at >= 0 ? pack[at].Value.Replace("\n", Environment.NewLine) : name == null ? "" : (FileSlots.StockOf(name) ?? "").Replace("\n", Environment.NewLine);
                addKey.Text = at >= 0 ? "Update" : "Add to pack";
                outKey.Enabled = at >= 0;
                textBar.Invalidate();
            }

            private void HerText()
            {
                if (slot == null) { Says("Pick a part on the left first.", true); return; }
                text.Box.Text = (FileSlots.StockOf(slot) ?? "").Replace("\n", Environment.NewLine);
                Says("Her own text for " + slot + " is in the box.", false);
            }

            private void FromFile()
            {
                if (slot == null) { Says("Pick a part on the left first.", true); return; }
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Title = "A file's text for " + slot;
                    dialog.Filter = "Text and md files (*.md;*.txt)|*.md;*.txt";
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    try { text.Box.Text = File.ReadAllText(dialog.FileName).Replace("\r\n", "\n").Replace("\n", Environment.NewLine); Says(Path.GetFileName(dialog.FileName) + " is in the box. Add it to the pack to keep it.", false); }
                    catch (IOException error) { Says("Couldn't read that file: " + error.Message, true); }
                    catch (UnauthorizedAccessException error) { Says("Couldn't read that file: " + error.Message, true); }
                }
            }

            private void AddToPack()
            {
                if (slot == null) { Says("Pick a part on the left first.", true); return; }
                string said = text.Box.Text.Replace("\r\n", "\n").Trim('\n');
                if (said.Trim().Length == 0) { Says("The box is empty. Write the text for " + slot + " first.", true); return; }
                int at = pack.FindIndex(one => string.Equals(one.Key, slot, StringComparison.OrdinalIgnoreCase));
                if (at >= 0) pack[at] = new KeyValuePair<string, string>(slot, said);
                else pack.Add(new KeyValuePair<string, string>(slot, said));
                Says(slot + (at >= 0 ? " is updated in the pack." : " is in the pack.") + " Reload to see it on her.", false);
                Refill();
            }

            private void TakeOut()
            {
                if (slot == null || !Has(slot)) return;
                pack.RemoveAll(one => string.Equals(one.Key, slot, StringComparison.OrdinalIgnoreCase));
                Says(slot + " is out of the pack.", false);
                Refill();
            }

            private void Refill()
            {
                string keep = slot;
                Point scrolled = headings.AutoScrollPosition;
                Pick(file);
                headings.AutoScrollPosition = new Point(-scrolled.X, -scrolled.Y);
                Choose(keep);
                ShowPack();
            }

            private void ShowPack()
            {
                List<string> names = pack.ConvertAll(one => one.Key);
                packed.Text = names.Count == 0 ? "Your pack is empty." : "In your pack (" + names.Count + "): " + string.Join(", ", names.ToArray());
            }

            internal string PackId { get { return IdOf(packName.Box.Text); } }

            // An id from the name: lowercase letters, numbers and dashes.
            internal static string IdOf(string name)
            {
                StringBuilder made = new StringBuilder();
                foreach (char one in (name ?? "").Trim().ToLowerInvariant())
                {
                    if (one >= 'a' && one <= 'z' || one >= '0' && one <= '9') made.Append(one);
                    else if (made.Length > 0 && made[made.Length - 1] != '-') made.Append('-');
                }
                string id = made.ToString().Trim('-');
                if (id.Length > 48) id = id.Substring(0, 48).Trim('-');
                return id.Length == 0 ? "my-pack" : id;
            }

            // The pack as a .capri-mod, format 2.
            internal string Json()
            {
                string name = packName.Box.Text.Trim().Length == 0 ? "My pack" : packName.Box.Text.Trim();
                StringBuilder made = new StringBuilder();
                made.Append("{\n  \"format\": 2,\n  \"id\": ").Append(Quote(PackId)).Append(",\n  \"name\": ").Append(Quote(name));
                made.Append(",\n  \"version\": \"1.0.0\",\n  \"author\": \"You\",\n  \"description\": ").Append(Quote("Made with Capri's mod helper."));
                if (kind == "app") made.Append(",\n  \"kind\": \"app\"");
                if (plugsInto != null) made.Append(",\n  \"plugs_into\": ").Append(Quote(plugsInto));
                made.Append(",\n  \"overrides\": [");
                for (int i = 0; i < pack.Count; i++)
                {
                    bool whole = pack[i].Key.IndexOf(" > ", StringComparison.Ordinal) < 0;
                    made.Append(i == 0 ? "\n" : ",\n").Append("    { \"slot\": ").Append(Quote(pack[i].Key)).Append(whole ? ", \"whole\": true" : "").Append(", \"text\": ").Append(Quote(pack[i].Value)).Append(" }");
                }
                made.Append("\n  ]\n}\n");
                return made.ToString();
            }

            private static string Quote(string value)
            {
                StringBuilder made = new StringBuilder("\"");
                foreach (char one in value ?? "")
                {
                    if (one == '"') made.Append("\\\"");
                    else if (one == '\\') made.Append("\\\\");
                    else if (one == '\n') made.Append("\\n");
                    else if (one == '\t') made.Append("\\t");
                    else if (one < ' ') made.Append("\\u").Append(((int)one).ToString("x4"));
                    else made.Append(one);
                }
                return made.Append('"').ToString();
            }

            // Checked as an import would, before it goes anywhere. Null when it reads, else why not.
            private string Checked()
            {
                if (pack.Count == 0) return "Your pack is empty. Add a part to it first.";
                try { Mods.Read(Json()); return null; }
                catch (ModException error) { return error.Message; }
            }

            // Reload: the pack laid on her now, through an import, so she uses it straight away.
            internal void Reload()
            {
                string problem = Checked();
                if (problem != null) { Says(problem, true); return; }
                string temp = Path.Combine(Path.GetTempPath(), PackId + Mods.Extension);
                try
                {
                    File.WriteAllText(temp, Json(), new UTF8Encoding(false));
                    if (Frame.ImportMod(temp)) Says("Your pack is on her.", false);
                    else Says(Frame.ModStatusText, true);
                }
                catch (IOException error) { Says("Couldn't lay the pack: " + error.Message, true); }
                finally { try { File.Delete(temp); } catch (Exception caughtOne) { Brain.Caught(caughtOne); } }
            }

            internal void TakeOff()
            {
                Mod on = Frame.capri.ModList.All.Find(one => one.File.Id == PackId);
                if (on == null) { Says("Your pack isn't on her.", false); return; }
                Frame.capri.ModList.Remove(on);
                Frame.FillMods();
                Says("Your pack is off her. Her own parts are back.", false);
            }

            // Saved into the mods folder's "made" folder, shown in Explorer, ready to share or drop in.
            internal string SavePack(bool show = true)
            {
                string problem = Checked();
                if (problem != null) { Says(problem, true); return null; }
                try
                {
                    string folder = Path.Combine(Frame.capri.ModList.Folder, "made");
                    Directory.CreateDirectory(folder);
                    string path = Path.Combine(folder, PackId + Mods.Extension);
                    File.WriteAllText(path, Json(), new UTF8Encoding(false));
                    Says("Saved as made\\" + Path.GetFileName(path) + " in her mods folder.", false);
                    if (show) using (Process.Start("explorer.exe", "/select,\"" + path + "\"")) { }
                    return path;
                }
                catch (Exception error)
                {
                    if (!(error is IOException || error is UnauthorizedAccessException || error is System.ComponentModel.Win32Exception)) throw;
                    Says("Couldn't save the pack: " + error.Message, true);
                    return null;
                }
            }

            private void OpenGuide()
            {
                string problem = ModGuide.Lay();
                if (problem != null) { Says(problem, true); return; }
                Frame.capri.OpenFiles(ModGuide.FileName);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape && fileMenu.Visible) { fileMenu.Close(); e.Handled = true; return; }
                base.OnKeyDown(e);
            }

            // The helper as they'd use it: a file and a heading picked, text put in and added, laid on her with Reload and
            // seen in her lines, taken off again, and saved as a pack that imports; fitting at this size.
            internal string Drive(string picture)
            {
                Application.DoEvents();
                string crowded = Crowded();
                if (crowded != null) return "Make a mod is crowded: " + crowded;
                Pick(Sayings.FileName);
                if (Children(headings).Count < 2) return "Picking lines.md listed no headings.";
                Choose(FileSlots.Name(Sayings.FileName, "openers warm"));
                if (text.Box.Text.Length == 0) return "Picking openers warm didn't put her text in the box.";
                text.Box.Text = "Helper opener.";
                AddToPack();
                if (!Has("lines.md > openers warm") || addKey.Text != "Update") return "Add to pack didn't put openers warm in the pack.";
                packName.Box.Text = "Helper Check";
                string[] hers = Sayings.Pool("openers warm");
                Reload();
                string[] laid = Sayings.Pool("openers warm");
                if (laid == null || laid.Length != 1 || laid[0] != "Helper opener.") return "Reload didn't lay the pack on her: " + notice.Text;
                if (picture != null) Picture(picture);
                TakeOff();
                string[] back = Sayings.Pool("openers warm");
                if (back == null || hers == null || back.Length != hers.Length) return "Taking the pack off didn't give her lines back.";
                string saved = SavePack(false);
                if (saved == null || !File.Exists(saved)) return "Save pack didn't write the pack: " + notice.Text;
                try { Mods.Read(File.ReadAllText(saved)); }
                catch (ModException error) { return "The saved pack doesn't import: " + error.Message; }
                finally { try { File.Delete(saved); } catch (Exception caughtOne) { Brain.Caught(caughtOne); } }
                TakeOut();
                if (Has("lines.md > openers warm")) return "Take out left the part in the pack.";
                return null;
            }
        }
    }
}
