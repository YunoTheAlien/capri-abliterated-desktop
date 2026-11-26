#if CHECKS
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace CapriDesktop
{
    // The gallery: every window and page of hers as it stands, at 100% and 150%, named by page, so a later build can be
    // laid beside it and a look that went backwards shows (ci/gallery.ps1 keeps them in _Check/old and _Check/new).
    internal sealed partial class Companion
    {
        private void Gallery(List<string> checks, string output)
        {
            string to = Path.Combine(output, "gallery");
            Directory.CreateDirectory(to);
            List<string> said = new List<string>();
            int sizeWas = preferences.MainframeSize;
            string sectionWas = preferences.ToolSection;
            bool shownWas = mainframe.Visible;
            FormWindowState stateWas = mainframe.WindowState;
            try
            {
                mainframe.WindowState = FormWindowState.Normal;
                mainframe.Show();
                foreach (int percent in new[] { 100, 150 })
                {
                    string at = "-" + percent;
                    mainframe.LayoutScale(96 * percent / 100, 100);
                    mainframe.SizeMainframe(100);
                    mainframe.GalleryPages(to, at, said);
                    Pictured(said, "menu" + at, delegate { MenuShot(Path.Combine(to, "menu" + at + ".png"), null); });
                    foreach (string named in new[] { "Play", "Tamagotchi", "Feed her", "Music" })
                    {
                        ToolStripMenuItem item = MenuItemNamed(menu.Items, named);
                        if (item == null && named == "Tamagotchi")
                        {
                            // The pet game runs only for this picture, as on the GitHub page: Tamagotchi with Give gift open.
                            Pictured(said, "menu-tamagotchi" + at, delegate { if (!PetShot(Path.Combine(to, "menu-tamagotchi" + at + ".png"))) said.Add("menu-tamagotchi" + at + ": the pet game isn't beside this build"); });
                            continue;
                        }
                        if (item == null) { said.Add("menu-" + Slug(named) + at + ": not on her menu here"); continue; }
                        ToolStripMenuItem held = item;
                        Pictured(said, "menu-" + Slug(named) + at, delegate { MenuOpenShot(Path.Combine(to, "menu-" + Slug(named) + at + ".png"), held); });
                    }
                    Pictured(said, "bubble" + at, delegate
                    {
                        Answer(++asking, "Hi! This is a line in my bubble, long enough to wrap onto a second row.", TalkState.Talking, true);
                        for (int i = 0; i < 160; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(25); }
                        Snap(box, Path.Combine(to, "bubble" + at + ".png"));
                        CloseTalk();
                    });
                    Pictured(said, "log" + at, delegate { ShowLog(); Snap(mainframe.LogPartShown, Path.Combine(to, "log" + at + ".png")); mainframe.CloseLog(); said.Add("log" + at + ": saved"); });
                }
            }
            finally
            {
                mainframe.LayoutScale(0, 0);
                mainframe.SizeMainframe(sizeWas);
                preferences.ToolSection = sectionWas;
                mainframe.ShowMods(false);
                mainframe.ShowSetupPage(false);
                mainframe.ShowSettings(false);
                mainframe.WindowState = stateWas;
                if (!shownWas) mainframe.Hide();
            }
            File.WriteAllLines(Path.Combine(to, "shots.txt"), said.ToArray());
            int pictures = Directory.GetFiles(to, "*.png").Length;
            // Every window cut to its clipped corners: nothing of its own drawing outside its outline, so a corner of its
            // picture is clear. Menus and her bubble are drawn their own way and are left out.
            List<string> spilled = new List<string>();
            foreach (string file in Directory.GetFiles(to, "*.png"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (name.StartsWith("menu", StringComparison.Ordinal) || name.StartsWith("bubble", StringComparison.Ordinal)) continue;
                using (Bitmap shot = new Bitmap(file))
                    if (shot.GetPixel(0, 0).A != 0 || shot.GetPixel(shot.Width - 1, shot.Height - 1).A != 0) spilled.Add(name);
            }
            if (spilled.Count > 0) throw new Exception("These windows show past their clipped corners: " + string.Join(", ", spilled.ToArray()));
            checks.Add("PASS the gallery: " + pictures + " pictures of her pages and windows at 100% and 150% in gallery\\, each page's fit in shots.txt");
        }

        // One picture, its line in shots.txt, and one that fails says why without stopping the rest.
        internal static void Pictured(List<string> said, string name, Action take)
        {
            try { take(); }
            catch (Exception error) { said.Add(name + ": not taken, " + error.Message); }
        }

        internal static void Snap(Control window, string path)
        {
            Application.DoEvents();
            // A see-through window (her bubble) draws onto the screen itself: its own picture is the one shown.
            Overlay layered = window as Overlay;
            if (layered != null)
            {
                if (layered.Image == null) throw new Exception("nothing shown in it");
                using (Bitmap copy = new Bitmap(layered.Image)) copy.Save(path, ImageFormat.Png);
                return;
            }
            // A window as Windows shows it: drawn by itself and cut to its outline, so a clipped corner shows only what is
            // inside the cut. A picture of the whole rectangle showed its ground past the corners.
            if (window is Form && window.Visible)
            {
                using (Bitmap cut = WindowPicture.Cut(window)) cut.Save(path, ImageFormat.Png);
                return;
            }
            using (Bitmap picture = new Bitmap(Math.Max(1, window.Width), Math.Max(1, window.Height)))
            {
                window.DrawToBitmap(picture, new Rectangle(Point.Empty, picture.Size));
                picture.Save(path, ImageFormat.Png);
            }
        }

        private static string Slug(string text) { return text.ToLowerInvariant().Replace(" / ", "-").Replace(' ', '-').Replace(",", "").Replace(":", ""); }

        private static ToolStripMenuItem MenuItemNamed(ToolStripItemCollection items, string named)
        {
            foreach (ToolStripItem one in items)
            {
                ToolStripMenuItem item = one as ToolStripMenuItem;
                if (item == null) continue;
                if (item.Text == named || item.Text.StartsWith(named + " (", StringComparison.Ordinal)) return item;
                ToolStripMenuItem inner = MenuItemNamed(item.DropDownItems, named);
                if (inner != null) return inner;
            }
            return null;
        }

        // Her menu with the lists down to this item opened, and the item's own list when it has one.
        private void MenuOpenShot(string path, ToolStripMenuItem item)
        {
            List<ToolStripMenuItem> chain = new List<ToolStripMenuItem>();
            for (ToolStripMenuItem up = item; up != null; up = up.OwnerItem as ToolStripMenuItem) chain.Insert(0, up);
            menu.Show(new Point(80, 80));
            List<ToolStripDropDown> shown = new List<ToolStripDropDown> { menu };
            foreach (ToolStripMenuItem step in chain)
            {
                if (!step.HasDropDownItems) { step.Select(); continue; }
                step.Select();
                step.ShowDropDown();
                Application.DoEvents();
                shown.Add(step.DropDown);
            }
            Application.DoEvents();
            Rectangle area = menu.Bounds;
            foreach (ToolStripDropDown drop in shown) area = Rectangle.Union(area, drop.Bounds);
            using (Bitmap sheet = new Bitmap(area.Width + 16, area.Height + 16, PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(sheet))
                {
                    graphics.Clear(Color.Transparent);
                    foreach (ToolStripDropDown drop in shown) Snapped(graphics, drop, drop.Left - area.Left + 8, drop.Top - area.Top + 8);
                }
                sheet.Save(path, ImageFormat.Png);
            }
            menu.Close();
        }
    }

    internal sealed partial class Mainframe
    {
        // Her Mainframe's pages and the windows opened from it, each with its fit or overlap line.
        internal void GalleryPages(string to, string at, List<string> said)
        {
            Action<string> page = delegate(string name)
            {
                // Settings scrolled shows rows passing under her title band on purpose: only the page as it opens is measured.
                bool scrolled = name == "settings-middle" || name == "settings-bottom";
                Companion.Pictured(said, name + at, delegate
                {
                    Settle();
                    Dictionary<Control, string> live = Steady(this);
                    Update();
                    using (Bitmap picture = new Bitmap(Math.Max(1, Width), Math.Max(1, Height)))
                    {
                        DrawToBitmap(picture, new Rectangle(Point.Empty, picture.Size));
                        DrawRims(picture);
                        using (Bitmap cut = Outlined(picture, this)) cut.Save(Path.Combine(to, name + at + ".png"), ImageFormat.Png);
                    }
                    foreach (KeyValuePair<Control, string> entry in live) entry.Key.Text = entry.Value;
                    said.Add(name + at + ": " + (scrolled ? "scrolled, not measured" : (paged ? TextFits() ?? Overlap() ?? Outside() : Overlap()) ?? "fits, nothing overlaps"));
                });
            };
            ShowMods(false);
            ShowSetupPage(false);
            ShowSettings(false);
            foreach (KeyValuePair<string, string> section in capri.Sections)
            {
                capri.ToolSection = section.Key;
                toolFirst = 0;
                FillTools();
                page("front-" + section.Key);
            }
            capri.ToolSection = capri.Sections[0].Key;
            FillTools();
            UpdateNote("Restart to update to Capri 9.9.999. Use her tray menu.", false);
            page("front-update");
            UpdateNote("", false);
            ShowPetMode(true);
            page("pet-mode");
            ShowPetMode(false);
            ShowSettings(true);
            int most = Math.Max(0, PageNow - pageView);
            ScrollPage(0);
            page("settings-top");
            ScrollPage(most / 2);
            page("settings-middle");
            ScrollPage(most);
            page("settings-bottom");
            ScrollPage(0);
            ShowSetupPage(true);
            page("setup");
            ShowMods(true);
            PickPlugins(false);
            page("mods");
            PickPlugins(true);
            page("plug-ins");
            PickPlugins(false);
            ShowMods(false);
            // Toggles: General open as it opens, then each fold open alone, and Better conversations' side.
            // The Sec: folds are made as Toggles opens; made first here, so the first scale lists them too.
            capri.SectionsChanged();
            List<string> folds = new List<string>();
            foreach (ToolStripItem one in capri.TogglesMenu.DropDownItems)
            {
                ToolStripMenuItem fold = one as ToolStripMenuItem;
                if (fold != null && fold.HasDropDownItems && !Equals(fold.Tag, GeneralTag)) folds.Add(fold.Text);
            }
            Companion.Pictured(said, "toggles" + at, delegate { CloseToggles(); OpenToggles(); Companion.Snap(toggleSheet, Path.Combine(to, "toggles" + at + ".png")); said.Add("toggles" + at + ": saved"); });
            foreach (string fold in folds)
            {
                string named = fold;
                Companion.Pictured(said, "toggles-" + Slug(named) + at, delegate
                {
                    CloseToggles();
                    OpenToggles();
                    UnfoldToggles(named);
                    Companion.Snap(toggleSheet, Path.Combine(to, "toggles-" + Slug(named) + at + ".png"));
                    said.Add("toggles-" + Slug(named) + at + ": saved");
                });
            }
            Companion.Pictured(said, "toggles-better-conversations" + at, delegate
            {
                CloseToggles();
                OpenToggles();
                PickToggleRow("Better conversations");
                Companion.Snap(toggleSheet, Path.Combine(to, "toggles-better-conversations" + at + ".png"));
                said.Add("toggles-better-conversations" + at + ": saved");
            });
            CloseToggles();
            Companion.Pictured(said, "personality" + at, delegate { PicturePersonalitySettings("Sassy", Path.Combine(to, "personality" + at + ".png")); said.Add("personality" + at + ": saved"); });
            ClosePersonalitySettings();
            Companion.Pictured(said, "memory" + at, delegate { OpenMemory(); Companion.Snap(memorySheet, Path.Combine(to, "memory" + at + ".png")); memorySheet.Close(); said.Add("memory" + at + ": saved"); });
            Companion.Pictured(said, "gifs" + at, delegate { OpenGifs(); Companion.Snap(gifSheet, Path.Combine(to, "gifs" + at + ".png")); CloseGifs(); said.Add("gifs" + at + ": saved"); });
            Companion.Pictured(said, "security" + at, delegate
            {
                using (SecuritySheet sheet = new SecuritySheet(this, SheetZoom(SecuritySheet.Wide, SecuritySheet.Tall)))
                {
                    sheet.StartPosition = FormStartPosition.Manual;
                    sheet.Location = Location;
                    sheet.Show(this);
                    Companion.Snap(sheet, Path.Combine(to, "security" + at + ".png"));
                    sheet.Close();
                }
                said.Add("security" + at + ": saved");
            });
            Companion.Pictured(said, "notice" + at, delegate
            {
                using (Notice told = new Notice("Capri has been removed.\n\nYour position and size settings were kept.", "Capri", false))
                {
                    told.Show();
                    Companion.Snap(told, Path.Combine(to, "notice" + at + ".png"));
                    told.Close();
                }
                said.Add("notice" + at + ": saved");
            });
            Companion.Pictured(said, "clear-memory" + at, delegate
            {
                using (Question asked = new Question("Forget this conversation, her transcripts and her memories of you. me.md stays.", "Clear memory", 0, "Clear memory", "Cancel"))
                {
                    asked.Show();
                    Companion.Snap(asked, Path.Combine(to, "clear-memory" + at + ".png"));
                    asked.Close();
                }
                said.Add("clear-memory" + at + ": saved");
            });
            Companion.Pictured(said, "streaming" + at, delegate
            {
                using (StreamingSheet sheet = new StreamingSheet(this, SheetZoom(StreamingSheet.Wide, StreamingSheet.Tall), capri.StreamMenu, null))
                {
                    sheet.StartPosition = FormStartPosition.Manual;
                    sheet.Location = Location;
                    sheet.Show(this);
                    Companion.Snap(sheet, Path.Combine(to, "streaming" + at + ".png"));
                    sheet.Close();
                }
                said.Add("streaming" + at + ": saved");
            });
            Companion.Pictured(said, "conflict" + at, delegate
            {
                using (ConflictSheet sheet = new ConflictSheet(this, SheetZoom(ConflictSheet.Wide, ConflictSheet.Tall), "One mod", "Another mod", new List<FileSlots.Contest>()))
                {
                    sheet.StartPosition = FormStartPosition.Manual;
                    sheet.Location = Location;
                    sheet.Show(this);
                    Companion.Snap(sheet, Path.Combine(to, "conflict" + at + ".png"));
                    sheet.Close();
                }
                said.Add("conflict" + at + ": saved");
            });
            Companion.Pictured(said, "planner" + at, delegate { said.Add("planner" + at + ": " + GalleryPlanner(Path.Combine(to, "planner" + at + ".png"))); });
        }

        private static string Slug(string text) { return text.ToLowerInvariant().Replace(" / ", "-").Replace(' ', '-').Replace(",", "").Replace(":", ""); }

        // Figures that change from one run to the next (CPU and RAM in use, a clock, her build number) put as dashes for
        // the picture, so old and new differ only where the look did; what was there is handed back.
        private static readonly System.Text.RegularExpressions.Regex Live = new System.Text.RegularExpressions.Regex(
            @"\bCPU (?:\d+%|-)|\bRAM (?:[\d.]+/[\d.]+ ?[KMGT]B|-)|\b\d{1,2}:\d{2}(?::\d{2})?(?: ?[ap]m)?\b|^v\d+\.\d+\.\d+\S*$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        private static Dictionary<Control, string> Steady(Control under)
        {
            Dictionary<Control, string> was = new Dictionary<Control, string>();
            Stack<Control> left = new Stack<Control>();
            left.Push(under);
            while (left.Count > 0)
            {
                Control one = left.Pop();
                foreach (Control inner in one.Controls) left.Push(inner);
                string text = one.Text ?? "";
                if (one == under || !Live.IsMatch(text)) continue;
                was[one] = text;
                one.Text = Live.Replace(text, delegate(System.Text.RegularExpressions.Match found) { return found.Value.StartsWith("CPU", StringComparison.OrdinalIgnoreCase) ? "CPU --" : found.Value.StartsWith("RAM", StringComparison.OrdinalIgnoreCase) ? "RAM --" : found.Value.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? "v-.-.-" : "--:--"; });
            }
            return was;
        }
    }
}
#endif
