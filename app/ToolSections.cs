using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Sections are theirs: added, renamed, ordered and removed in Toggles' Sections side, kept by id so a rename never
    // loses an app. TOOLS' dropdown and Toggles' Sec: folds read the same list; every app sits in one, else Uncategorised.
    internal sealed partial class Companion
    {
        internal static readonly string[,] StockSections = { { "capri", "CAPRI" }, { "fun", "Fun" }, { "homelab", "Homelab" }, { "learning", "Learning" }, { "productivity", "Productivity" }, { "utility", "Utility" } };
        internal const string Uncategorised = "uncategorised", UncategorisedName = "Uncategorised";
        // Other words a mod or plug-in may name a stock section by.
        private static readonly string[,] SectionAliases = { { "games", "fun" }, { "play", "fun" }, { "work", "productivity" }, { "office", "productivity" } };

        // Each section as id and name, in their order.
        internal List<KeyValuePair<string, string>> Sections
        {
            get
            {
                List<KeyValuePair<string, string>> all = new List<KeyValuePair<string, string>>();
                if (preferences.ToolSectionList != null)
                    foreach (string one in preferences.ToolSectionList)
                    {
                        int cut = one == null ? -1 : one.IndexOf('|');
                        if (cut > 0 && cut < one.Length - 1 && one.Substring(0, cut) != Uncategorised) all.Add(new KeyValuePair<string, string>(one.Substring(0, cut), one.Substring(cut + 1)));
                    }
                if (preferences.ToolSectionList == null)
                    for (int i = 0; i < StockSections.GetLength(0); i++) all.Add(new KeyValuePair<string, string>(StockSections[i, 0], StockSections[i, 1]));
                return all;
            }
        }

        // Sections they set before her windows had tiles get CAPRI first and Utility last, once; one removed after stays removed.
        internal static void AddWindowSections(Preferences saved)
        {
            if (saved.WindowSectionsAdded) return;
            saved.WindowSectionsAdded = true;
            if (saved.ToolSectionList == null) return;
            List<string> all = new List<string>(saved.ToolSectionList);
            if (!all.Exists(one => one != null && one.StartsWith("capri|", StringComparison.Ordinal))) all.Insert(0, "capri|CAPRI");
            if (!all.Exists(one => one != null && one.StartsWith("utility|", StringComparison.Ordinal))) all.Add("utility|Utility");
            saved.ToolSectionList = all.ToArray();
        }

        internal void SaveSections(List<KeyValuePair<string, string>> all)
        {
            preferences.ToolSectionList = all.ConvertAll(one => one.Key + "|" + one.Value.Replace("|", " ")).ToArray();
            SavePreferences();
            SectionsChanged();
        }

        // A section named by its id or its name, either case; null for none of theirs.
        internal string SectionId(string named)
        {
            string wanted = (named ?? "").Trim();
            if (wanted.Length == 0) return null;
            if (string.Equals(wanted, Uncategorised, StringComparison.OrdinalIgnoreCase) || string.Equals(wanted, UncategorisedName, StringComparison.OrdinalIgnoreCase)) return Uncategorised;
            foreach (KeyValuePair<string, string> one in Sections)
                if (string.Equals(one.Key, wanted, StringComparison.OrdinalIgnoreCase) || string.Equals(one.Value, wanted, StringComparison.OrdinalIgnoreCase)) return one.Key;
            for (int i = 0; i < SectionAliases.GetLength(0); i++)
                if (string.Equals(SectionAliases[i, 0], wanted, StringComparison.OrdinalIgnoreCase) && Sections.Exists(one => one.Key == SectionAliases[i, 1])) return SectionAliases[i, 1];
            return null;
        }

        internal string SectionName(string id)
        {
            if (id == Uncategorised) return UncategorisedName;
            foreach (KeyValuePair<string, string> one in Sections) if (one.Key == id) return one.Value;
            return UncategorisedName;
        }

        // Where an app sits: where they put it, else where it asks to be, else Uncategorised.
        internal string PlaceOf(string app, string asks)
        {
            if (preferences.ToolPlaces != null)
                foreach (string one in preferences.ToolPlaces)
                    if (one != null && one.StartsWith(app + "|", StringComparison.Ordinal))
                    {
                        string id = one.Substring(app.Length + 1);
                        if (id == Uncategorised || Sections.Exists(section => section.Key == id)) return id;
                    }
            return SectionId(asks) ?? Uncategorised;
        }

        internal void PlaceApp(string app, string section)
        {
            List<string> places = new List<string>(preferences.ToolPlaces ?? new string[0]);
            places.RemoveAll(one => one != null && one.StartsWith(app + "|", StringComparison.Ordinal));
            places.Add(app + "|" + section);
            preferences.ToolPlaces = places.ToArray();
            SavePreferences();
            SectionsChanged();
        }

        // A section taken out: its apps go to Uncategorised.
        internal void RemoveSection(string id)
        {
            List<KeyValuePair<string, string>> all = Sections;
            List<string> moved = new List<string>();
            foreach (SectionApp app in SectionApps()) if (app.Section == id) moved.Add(app.Id);
            all.RemoveAll(one => one.Key == id);
            preferences.ToolSectionList = all.ConvertAll(one => one.Key + "|" + one.Value).ToArray();
            List<string> places = new List<string>(preferences.ToolPlaces ?? new string[0]);
            places.RemoveAll(one => moved.Exists(app => one != null && one.StartsWith(app + "|", StringComparison.Ordinal)));
            foreach (string app in moved) places.Add(app + "|" + Uncategorised);
            preferences.ToolPlaces = places.ToArray();
            SavePreferences();
            SectionsChanged();
        }

        // A new section's id: never one in use, so a section added after one was removed doesn't take its apps.
        internal string NewSectionId()
        {
            for (int n = 1; ; n++)
            {
                string id = "section-" + n;
                if (!Sections.Exists(one => one.Key == id) && (preferences.ToolPlaces == null || !Array.Exists(preferences.ToolPlaces, one => one != null && one.EndsWith("|" + id, StringComparison.Ordinal)))) return id;
            }
        }

        // An app in a section: one of her tools, a plug-in or the pet game (opening its options), or a mod with
        // switches of its own.
        internal sealed class SectionApp
        {
            internal string Id, Name, Section;
            internal Mainframe.Tool Tool;
            internal ToolStripMenuItem Fold;
            // The switches of mods that plug into this app (and an app mod's own), inside its row under their own names.
            internal readonly List<ToolStripMenuItem> Guests = new List<ToolStripMenuItem>();
        }

        // An app by its id or its name, either case: one of her tools, a plug-in or a mod of kind app; "email" or "mail" is the
        // mail plug-in, and the pet game's mod id is the Tamagotchi. Null for none.
        internal Mainframe.Tool AppNamed(string named)
        {
            string wanted = (named ?? "").Trim().ToLowerInvariant();
            if (wanted.Length == 0 || mainframe == null) return null;
            Mod pet = PetMod();
            if (pet != null && wanted == pet.File.Id) wanted = "pet";
            foreach (Mainframe.Tool tool in mainframe.Tools())
            {
                if (tool.Id == wanted || tool.Id == "plugin:" + wanted || tool.Id == "mod:" + wanted) return tool;
                if (string.Equals(tool.Short, wanted, StringComparison.OrdinalIgnoreCase) || string.Equals(tool.Full, wanted, StringComparison.OrdinalIgnoreCase)) return tool;
                if ((wanted == "email" || wanted == "mail") && tool.Icon == "mail") return tool;
            }
            return null;
        }

        // The pet game's mod: one asking for the pet permission, the running one first.
        private Mod PetMod()
        {
            if (ModList == null) return null;
            string running = ModList.PetId;
            return ModList.All.Find(one => one.File.Id == running) ?? ModList.All.Find(one => one.Code && Array.IndexOf(one.File.Permissions, "pet") >= 0);
        }

        // An app's name while it is missing or off, for a mod that plugs into it; null while it is there. Before the
        // Mainframe is made nothing is held back.
        internal string AppAbsent(string named)
        {
            if (mainframe == null) return null;
            Mainframe.Tool app = AppNamed(named);
            if (app == null) return named;
            Mod mod = app.Plugin ?? (app.Id == "pet" ? PetMod() : app.Id.StartsWith("mod:", StringComparison.Ordinal) ? ModList.All.Find(one => "mod:" + one.File.Id == app.Id) : null);
            bool on = mod != null ? mod.On : app.On == null || app.On();
            return on ? null : app.Short;
        }

        // The app a mod's switches sit in: the one it plugs into, else the app its section names when no section is
        // called that, else its own row as a kind app; null when it sits in a section by itself.
        private string HostOf(string id, string asks)
        {
            Mod made = ModList == null ? null : ModList.All.Find(one => one.File.Id == id);
            Mainframe.Tool app = null;
            if (made != null && made.File.App) app = AppNamed(id);
            if (app == null && made != null && made.File.PlugsInto != null) app = AppNamed(made.File.PlugsInto);
            if (app == null && asks != null && SectionId(asks) == null) app = AppNamed(asks);
            return app == null ? null : app.Id;
        }

        // A mod's own switches, by mod id: they sit in a Sec: fold under the mod's name, not in a fold of their own.
        private readonly Dictionary<string, ToolStripMenuItem> modFolds = new Dictionary<string, ToolStripMenuItem>();

        // The section a mod asked to sit in (ICapriToggles.Join with a section's name or id).
        private readonly Dictionary<string, string> modSections = new Dictionary<string, string>();

        internal void ModFold(string mod, ToolStripMenuItem fold, string asks = null)
        {
            if (fold == null) { modFolds.Remove(mod); modSections.Remove(mod); }
            else { modFolds[mod] = fold; if (asks != null) modSections[mod] = asks; }
            SectionsChanged();
        }

        // One of her own folds by its title, for a mod joining it: Intros, Screensaver, Pranks.
        internal ToolStripMenuItem FoldTitled(string title)
        {
            if (togglesMenu == null || string.IsNullOrWhiteSpace(title)) return null;
            foreach (ToolStripItem one in togglesMenu.DropDownItems)
            {
                ToolStripMenuItem fold = one as ToolStripMenuItem;
                if (fold != null && fold.HasDropDownItems && !secFolds.Contains(fold) && string.Equals(fold.Text, title.Trim(), StringComparison.OrdinalIgnoreCase)) return fold;
            }
            return null;
        }

        // Every app, in the one order TOOLS' Position and the Sections side keep.
        internal List<SectionApp> SectionApps()
        {
            List<SectionApp> apps = new List<SectionApp>();
            if (mainframe != null)
                foreach (Mainframe.Tool tool in mainframe.Tools())
                    apps.Add(new SectionApp { Id = tool.Id, Name = tool.Short, Tool = tool, Section = PlaceOf(tool.Id, tool.Section) });
            foreach (KeyValuePair<string, ToolStripMenuItem> mod in modFolds)
            {
                string id = "mod:" + mod.Key;
                Mod made = ModList == null ? null : ModList.All.Find(one => one.File.Id == mod.Key);
                string asks;
                if (!modSections.TryGetValue(mod.Key, out asks)) asks = made != null ? made.File.Section : null;
                // One that plugs into an app sits inside that app's row; a section matching no section or app, Uncategorised.
                string host = HostOf(mod.Key, asks);
                SectionApp into = host == null ? null : apps.Find(app => app.Id == host);
                if (into != null) { into.Guests.Add(mod.Value); continue; }
                apps.Add(new SectionApp { Id = id, Name = mod.Value.Text, Fold = mod.Value, Section = PlaceOf(id, asks) });
            }
            List<string> order = ToolOrder;
            List<SectionApp> made2 = new List<SectionApp>(apps);
            apps.Sort(delegate(SectionApp x, SectionApp y)
            {
                int a = order.IndexOf(x.Id), b = order.IndexOf(y.Id);
                if (a < 0) a = int.MaxValue;
                if (b < 0) b = int.MaxValue;
                return a != b ? a.CompareTo(b) : made2.IndexOf(x).CompareTo(made2.IndexOf(y));
            });
            return apps;
        }

        // An app moved into a section, before the app at "before" in it (null: last).
        internal void MoveApp(string app, string section, string before)
        {
            List<SectionApp> all = SectionApps();
            List<string> order = all.ConvertAll(one => one.Id);
            order.Remove(app);
            int at = before == null ? -1 : order.IndexOf(before);
            if (at < 0)
            {
                int last = -1;
                for (int i = 0; i < all.Count; i++) if (all[i].Section == section && all[i].Id != app) last = order.IndexOf(all[i].Id);
                at = last + 1;
                if (last < 0) at = order.Count;
            }
            order.Insert(at, app);
            preferences.ToolOrder = order.ToArray();
            PlaceApp(app, section);
        }

        // The Sec: folds in her Toggles menu, made again from the sections and their apps: right after General's rows,
        // before Intros.
        private ToolStripMenuItem introsMenu, sectionsRow, panFoldMenu;
        private readonly List<ToolStripMenuItem> secFolds = new List<ToolStripMenuItem>();
        internal const string SecPrefix = "Sec: ";

        internal void SectionsChanged()
        {
            if (togglesMenu == null || introsMenu == null) return;
            foreach (ToolStripMenuItem old in secFolds)
            {
                // A mod's own fold is kept: only taken off the Sec: fold, or the app's row, it was in.
                foreach (ToolStripItem inner in new List<ToolStripItem>(EachItem(old.DropDownItems)))
                {
                    if (modFolds.ContainsValue(inner as ToolStripMenuItem)) { old.DropDownItems.Remove(inner); continue; }
                    ToolStripMenuItem row = inner as ToolStripMenuItem;
                    if (row != null) foreach (ToolStripItem guest in new List<ToolStripItem>(EachItem(row.DropDownItems))) if (modFolds.ContainsValue(guest as ToolStripMenuItem)) row.DropDownItems.Remove(guest);
                }
                togglesMenu.DropDownItems.Remove(old);
                old.Dispose();
            }
            secFolds.Clear();
            List<SectionApp> apps = SectionApps();
            List<KeyValuePair<string, string>> sections = Sections;
            if (apps.Exists(app => app.Section == Uncategorised)) sections.Add(new KeyValuePair<string, string>(Uncategorised, UncategorisedName));
            // The Sec: folds go before PAN: MAINFRAME SETTINGS, which comes after them.
            int at = togglesMenu.DropDownItems.IndexOf(panFoldMenu != null ? panFoldMenu : introsMenu);
            foreach (KeyValuePair<string, string> section in sections)
            {
                ToolStripMenuItem fold = new ToolStripMenuItem(SecPrefix + section.Value) { Tag = Mainframe.SecTag + section.Key };
                // Her tools' switches ticked as the fold opens, as her other groups are.
                fold.DropDownOpening += delegate
                {
                    foreach (ToolStripItem inner in fold.DropDownItems)
                    {
                        string tag = inner.Tag as string;
                        ToolStripMenuItem row = inner as ToolStripMenuItem;
                        if (row != null && tag != null && tag.StartsWith("tool:", StringComparison.Ordinal)) row.Checked = ToolOn(tag.Substring(5));
                    }
                };
                foreach (SectionApp app in apps)
                {
                    if (app.Section != section.Key) continue;
                    if (app.Fold != null) { fold.DropDownItems.Add(app.Fold); continue; }
                    // The Tamagotchi's row only while its pet game runs: a row with nothing behind it is never shown.
                    if (app.Tool.Id == "pet" && (ModList == null || ModList.PetId == null)) continue;
                    ToolStripMenuItem row = AppRow(app.Tool);
                    foreach (ToolStripMenuItem guest in app.Guests) row.DropDownItems.Add(guest);
                    fold.DropDownItems.Add(row);
                }
                // A section with nothing in it has no fold; it shows on the Sections side.
                if (fold.DropDownItems.Count == 0) { fold.Dispose(); continue; }
                togglesMenu.DropDownItems.Insert(at++, fold);
                secFolds.Add(fold);
            }
            if (mainframe != null) { mainframe.FillTools(); mainframe.TogglesChanged(); }
        }

        private static IEnumerable<ToolStripItem> EachItem(ToolStripItemCollection items) { foreach (ToolStripItem one in items) yield return one; }

        // One of her tools: its switch, its parts behind its key. A plug-in: its key opens its options. The Tamagotchi: its key
        // shows its rates and its pet game's switches; it never opens Tamagotchi mode, which is TOOLS' tile and Mods' switch.
        private ToolStripMenuItem AppRow(Mainframe.Tool tool)
        {
            string id = tool.Id;
            if (tool.Id == "pet") return PetRates.Row(this, tool.Full);
            if (tool.Plugin != null || tool.Id.StartsWith("mod:", StringComparison.Ordinal))
                return new ToolStripMenuItem(tool.Full, null, delegate { if (tool.Open != null) tool.Open(); }) { Tag = Mainframe.OpensTag };
            ToolStripMenuItem row = new ToolStripMenuItem(tool.Full, null, delegate { SetToolOn(id, !ToolOn(id)); }) { Tag = "tool:" + id };
            row.ToolTipText = "Off hides it from TOOLS; nothing it holds is removed";
            row.Checked = ToolOn(id);
            if (id == "music")
            {
                playerKeepsItem = new ToolStripMenuItem("Keep playing when closed", null, delegate { TogglePlayerKeeps(); }) { Checked = preferences.PlayerCloseKeepsMusic };
                playerKeepsItem.ToolTipText = "On, closing the music player leaves the music playing; off, closing it stops the music";
                row.DropDownItems.Add(playerKeepsItem);
                row.DropDownOpening += delegate { playerKeepsItem.Checked = preferences.PlayerCloseKeepsMusic; };
            }
            if (id == "circuit")
                row.DropDownItems.Add(new ToolStripMenuItem("Advanced circuits package: a download, off until turned on, its size shown first. It comes with the designer.") { Tag = Mainframe.TextTag });
            return row;
        }

#if CHECKS
        // Sections: the left side's order, the Sections side, a drag within, across and onto an empty section, a section
        // moved, renamed (its id kept), removed (its apps to Uncategorised) and added; TOOLS' dropdown follows.
        private void SmokeToggleSections(List<string> checks, string output)
        {
            string[] listWas = preferences.ToolSectionList, placesWas = preferences.ToolPlaces, orderWas = preferences.ToolOrder;
            string sectionWas = preferences.ToolSection;
            List<string> wrong = new List<string>();
            try
            {
                preferences.ToolSectionList = null;
                preferences.ToolPlaces = null;
                preferences.ToolOrder = null;
                SectionsChanged();
                List<string> folds = new List<string>();
                foreach (ToolStripItem one in togglesMenu.DropDownItems) { ToolStripMenuItem fold = one as ToolStripMenuItem; if (fold != null && fold.HasDropDownItems && !Equals(fold.Tag, Mainframe.GeneralTag)) folds.Add(fold.Text); }
                string order = string.Join(" | ", folds.ToArray());
                int intros = folds.IndexOf("Intros");
                // The Sec: folds, then PAN: MAINFRAME SETTINGS, then her own groups.
                int panAt = folds.IndexOf(PanPrefix + "Mainframe settings");
                bool secFirst = panAt > 0 && panAt == intros - 1 && folds.GetRange(0, panAt).TrueForAll(one => one.StartsWith(SecPrefix, StringComparison.Ordinal));
                if (!order.StartsWith("Sec: CAPRI | Sec: Fun | Sec: Learning | Sec: Productivity | Sec: Utility", StringComparison.Ordinal) || !secFirst || folds.IndexOf("Screensaver") != intros + 1 || folds.IndexOf("Pranks") != intros + 2)
                    wrong.Add("the left side's order: " + order);
                if (sectionsRow == null || togglesMenu.DropDownItems.IndexOf(sectionsRow) < 0 || sectionsRow.HasDropDownItems) wrong.Add("no Sections row in General");
                // A mod names its section by id; other words by the alias table; an unknown one is Uncategorised.
                if (SectionId("fun") != "fun" || SectionId("games") != "fun" || SectionId("play") != "fun" || SectionId("work") != "productivity" || SectionId("office") != "productivity" || PlaceOf("mod:stand-in", "nowhere") != Uncategorised)
                    wrong.Add("section ids and aliases: games " + SectionId("games") + ", office " + SectionId("office") + ", unknown " + PlaceOf("mod:stand-in", "nowhere"));
                mainframe.OpenSections();
                Application.DoEvents();
                string side = mainframe.SectionsText ?? "";
                if (!side.StartsWith("[CAPRI] Toggles Colours Personality Security MD editor Memory Growth [Fun] ", StringComparison.Ordinal) || side.IndexOf("Music Chess Connect four Tic-tac-toe Rock, paper, scissors [Homelab] [Learning] Circuit designer Coding teacher [Productivity] Planner Workspace [Utility] Modding Streaming", StringComparison.Ordinal) < 0 || side.IndexOf("[Uncategorised]", StringComparison.Ordinal) < 0)
                    wrong.Add("the Sections side: " + side);
                using (System.Drawing.Bitmap shot = mainframe.PictureToggles()) shot.Save(System.IO.Path.Combine(output, "toggles-sections.png"), System.Drawing.Imaging.ImageFormat.Png);
                mainframe.OpenSections();
                mainframe.DragSection("Music", "Planner", false);
                List<SectionApp> apps = SectionApps();
                int music = apps.FindIndex(app => app.Id == "music"), planner = apps.FindIndex(app => app.Id == "planner");
                if (PlaceOf("music", "fun") != "productivity" || music < 0 || music > planner) wrong.Add("Music dragged above the Planner: " + PlaceOf("music", "fun") + ", " + music + " / " + planner);
                mainframe.DragSection("Chess", "Homelab", false);
                ToolSection = "homelab";
                mainframe.FillTools();
                if (PlaceOf("chess", "fun") != "homelab" || mainframe.PositionOf("chess") != 1) wrong.Add("Chess dropped on Homelab's empty row: " + PlaceOf("chess", "fun"));
                mainframe.DragSection("Productivity", "Fun", false);
                Func<string, int> at = delegate(string id) { return Sections.FindIndex(one => one.Key == id); };
                if (at("productivity") != at("fun") - 1) wrong.Add("Productivity dragged above Fun: " + at("productivity") + " / " + at("fun"));
                // Ctrl and an arrow, as on TOOLS: a section down one; an app up one in its section; the first app of a
                // section up into the one before.
                mainframe.OpenSections();
                mainframe.ShiftSection("s:productivity", 1);
                if (at("productivity") != at("fun") + 1) wrong.Add("Ctrl+Down on Productivity: " + at("productivity") + " / " + at("fun"));
                mainframe.OpenSections();
                mainframe.ShiftSection("tic_tac_toe", -1);
                List<SectionApp> funNow = SectionApps().FindAll(app => app.Section == "fun");
                if (funNow.FindIndex(app => app.Id == "tic_tac_toe") > funNow.FindIndex(app => app.Id == "connect_four")) wrong.Add("Ctrl+Up on Tic-tac-toe didn't pass Connect four");
                mainframe.OpenSections();
                mainframe.ShiftSection("circuit", -1);
                if (PlaceOf("circuit", "learning") != "homelab") wrong.Add("Ctrl+Up on Learning's first app went to " + PlaceOf("circuit", "learning"));
                List<KeyValuePair<string, string>> renamed = Sections;
                renamed[renamed.FindIndex(one => one.Key == "fun")] = new KeyValuePair<string, string>("fun", "Games");
                SaveSections(renamed);
                if (SectionId("Games") != "fun" || SectionName("fun") != "Games" || PlaceOf("connect_four", "fun") != "fun" || FoldTitled("Sec: Games") != null || !togglesMenu.DropDownItems.Cast<ToolStripItem>().Any(one => one.Text == "Sec: Games"))
                    wrong.Add("a rename lost its id or its fold");
                mainframe.PressSection("homelab", true);
                SectionsChanged();
                if (Sections.Exists(one => one.Key == "homelab") || PlaceOf("chess", "fun") != Uncategorised || !togglesMenu.DropDownItems.Cast<ToolStripItem>().Any(one => one.Text == SecPrefix + UncategorisedName))
                    wrong.Add("Homelab removed: its Chess went to " + PlaceOf("chess", "fun"));
                string added = NewSectionId();
                List<KeyValuePair<string, string>> more = Sections;
                more.Add(new KeyValuePair<string, string>(added, "Media"));
                SaveSections(more);
                mainframe.OpenSections();
                if ((mainframe.SectionsText ?? "").IndexOf("[Media]", StringComparison.Ordinal) < 0 || NewSectionId() == added) wrong.Add("Add: " + mainframe.SectionsText);
            }
            finally
            {
                preferences.ToolSectionList = listWas;
                preferences.ToolPlaces = placesWas;
                preferences.ToolOrder = orderWas;
                preferences.ToolSection = sectionWas;
                SavePreferences();
                SectionsChanged();
                mainframe.CloseToggles();
            }
            if (wrong.Count > 0) throw new Exception("Sections went wrong: " + string.Join("; ", wrong.ToArray()));
            checks.Add("PASS Sections: a mod's section by id or alias (games, play, work, office), an unknown one Uncategorised; Toggles reads General with its Sections row, then a Sec: fold per section (an empty one has none), then Intros, Screensaver and Pranks; the Sections side lists each section and its apps with Uncategorised last; a drag, or Ctrl and an arrow, moves an app within or across sections and onto an empty one, and moves a section; a rename keeps its id; a removed section's apps go to Uncategorised; Add makes a new one; TOOLS follows (toggles-sections.png)");
        }

        // Kinds: an app mod gets a tile and a row; one plugging into an app sits in that app's row and waits while it is off;
        // a section naming an app puts a mod there, an unknown one in Uncategorised.
        private void SmokeModKinds(List<string> checks)
        {
            List<string> wrong = new List<string>();
            ModFile code = Mods.ReadCode("// capri-mod: 1\n// id: kind-check\n// name: Kind check\n// kind: App\n// plugs into: Email\n// section: fun\n\nusing System;\n");
            if (!code.App || code.PlugsInto != "email" || code.Section != "fun") wrong.Add("the header read kind " + code.Kind + ", plugs into " + code.PlugsInto + ", section " + code.Section);
            try { Mods.ReadCode("// capri-mod: 1\n// id: kind-bad\n// name: Kind bad\n// kind: widget\n"); wrong.Add("a kind other than app or mod was read"); }
            catch (ModException) { }
            ModFile pack = Mods.Read("{\"format\": 2, \"id\": \"kind-pack\", \"name\": \"Kind pack\", \"kind\": \"mod\", \"plugs_into\": \"Pet\", \"wake_names\": [\"kindcheck\"]}");
            if (pack.App || pack.PlugsInto != "pet") wrong.Add("a pack read kind " + pack.Kind + ", plugs into " + pack.PlugsInto);
            bool plannerWas = ToolOn("planner");
            string[] placesWas = preferences.ToolPlaces;
            Func<string, string, string, string, Mod> stand = delegate(string id, string kind, string plugs, string section)
            {
                return new Mod { File = new ModFile { Id = id, Name = id, Kind = kind, PlugsInto = plugs, Section = section, WakeNames = new string[0] }, Code = true, On = true };
            };
            Mod app = stand("kind-app", "app", null, "fun"), guest = stand("kind-guest", null, "kind-app", null), named = stand("kind-named", null, null, "kind-app");
            Mod lost = stand("kind-lost", null, null, "nowhere"), planned = stand("kind-planned", null, "planner", null);
            Mod[] all = { app, guest, named, lost, planned };
            Dictionary<string, ToolStripMenuItem> folds = new Dictionary<string, ToolStripMenuItem>();
            try
            {
                preferences.ToolPlaces = null;
                ModList.All.AddRange(all);
                foreach (Mod one in all) { folds[one.File.Id] = new ToolStripMenuItem(one.File.Id); ModFold(one.File.Id, folds[one.File.Id]); }
                Mainframe.Tool tile = mainframe.Tools().Find(tool => tool.Id == "mod:kind-app");
                if (tile == null || PlaceOf(tile.Id, tile.Section) != "fun") wrong.Add("the app mod has no tile in Fun: " + (tile == null ? "none" : PlaceOf(tile.Id, tile.Section)));
                List<SectionApp> apps = SectionApps();
                SectionApp row = apps.Find(one => one.Id == "mod:kind-app");
                if (row == null || !row.Guests.Contains(folds["kind-app"]) || !row.Guests.Contains(folds["kind-guest"]) || !row.Guests.Contains(folds["kind-named"]))
                    wrong.Add("the app's row doesn't hold its own, its plug-in's and the named mod's switches: " + (row == null ? "no row" : row.Guests.Count + " inside"));
                if (apps.Exists(one => one.Id == "mod:kind-guest" || one.Id == "mod:kind-named")) wrong.Add("a mod plugging into an app also sits as an app of its own");
                SectionApp lostRow = apps.Find(one => one.Id == "mod:kind-lost");
                if (lostRow == null || lostRow.Section != Uncategorised) wrong.Add("a section naming nothing isn't Uncategorised: " + (lostRow == null ? "no row" : lostRow.Section));
                SectionApp plannerRow = apps.Find(one => one.Id == "planner");
                if (plannerRow == null || !plannerRow.Guests.Contains(folds["kind-planned"])) wrong.Add("a mod plugging into the Planner isn't in its row");
                ToolStripMenuItem fun = secFolds.Find(one => Equals(one.Tag, Mainframe.SecTag + "fun"));
                ToolStripMenuItem shown = fun == null ? null : fun.DropDownItems.OfType<ToolStripMenuItem>().FirstOrDefault(one => one.Text == "kind-app");
                if (shown == null || !shown.DropDownItems.Contains(folds["kind-guest"])) wrong.Add("Toggles' Sec: Fun has no kind-app row with its plug-in inside");
                if (ModList.AppAbsent == null || AppAbsent("kind-app") != null || AppAbsent("no-such-app") != "no-such-app") wrong.Add("an app there was missing, or one not there wasn't");
                app.On = false;
                if (AppAbsent("kind-app") != "kind-app") wrong.Add("an app switched off didn't hold its plug-in back");
                app.On = true;
                SetToolOn("planner", false);
                if (AppAbsent("planner") != "Planner") wrong.Add("the Planner off didn't hold its plug-in back");
                SetToolOn("planner", true);
                if (AppAbsent("email") != null && AppNamed("email") != null) wrong.Add("Email is installed but read as missing");
                string made = mainframe.ModHelperKinds();
                if (made.IndexOf("\"kind\": \"app\"", StringComparison.Ordinal) < 0 || made.IndexOf("\"plugs_into\": \"planner\"", StringComparison.Ordinal) < 0) wrong.Add("Make a mod didn't write kind and plugs into: " + made);
                else
                {
                    ModFile helped = Mods.Read(made.Replace("\"overrides\": [", "\"wake_names\": [\"kindcheck\"], \"overrides\": ["));
                    if (!helped.App || helped.PlugsInto != "planner") wrong.Add("Make a mod's pack read back kind " + helped.Kind + ", plugs into " + helped.PlugsInto);
                }
            }
            finally
            {
                foreach (KeyValuePair<string, ToolStripMenuItem> one in folds) ModFold(one.Key, null);
                foreach (ToolStripMenuItem one in folds.Values) one.Dispose();
                ModList.All.RemoveAll(one => Array.IndexOf(all, one) >= 0);
                SetToolOn("planner", plannerWas);
                preferences.ToolPlaces = placesWas;
                SectionsChanged();
            }
            if (wrong.Count > 0) throw new Exception("Mod kinds went wrong: " + string.Join("; ", wrong.ToArray()));
            checks.Add("PASS Mod kinds: \"// kind: app\" and \"// plugs into:\" read from a code mod's header and a pack, a kind other than app or mod refused; an app mod has a tile in its section and a row in Toggles holding its own switches; a mod plugging into an app, or whose section names an app, sits inside that app's row (the Planner's too), never as an app of its own; a section naming nothing known is Uncategorised; a plug-in waits while its app is missing or off; Make a mod writes kind and plugs into and the pack reads back");
        }
#endif
    }
}
