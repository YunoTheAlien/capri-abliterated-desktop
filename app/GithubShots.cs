#if CHECKS
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Companion
    {
        // Pictures for the GitHub page: each window of the SFW side as it shows at 100%, in a clean test folder, saved
        // into <output>\github with a see-through ground where nothing is drawn. Nothing of anyone's own goes in them.
        private void ShotsGithub(List<string> checks, string output)
        {
            if (Brain.SenpaiOn) { checks.Add("LIMIT the GitHub pictures are of the SFW side only, and the switch is on here"); return; }
            string to = Path.Combine(output, "github");
            Directory.CreateDirectory(to);
            int wasSize = preferences.MainframeSize;
            // A check build's menu title carries its test mark; the page shows her name only.
            ToolStripItem title = menu.Items.Count > 0 ? menu.Items[0] : null;
            string titleWas = title != null ? title.Text : null;
            bool wasPage = mainframe.SettingsShown, wasShown = mainframe.Visible;
            FormWindowState wasState = mainframe.WindowState;
            List<string> made = new List<string>();
            Action<string> took = delegate(string name) { if (File.Exists(Path.Combine(to, name))) made.Add(name); };
            try
            {
                // A Mainframe left minimised draws as a bare title bar.
                mainframe.WindowState = FormWindowState.Normal;
                mainframe.Show();
                mainframe.LayoutScale(96, 100);
                mainframe.SizeMainframe(100);
                foreach (bool settings in new[] { false, true })
                {
                    mainframe.ShowSettings(settings);
                    mainframe.Settle();
                    mainframe.Update();
                    if (mainframe.ClientSize.Width < 300 || mainframe.ClientSize.Height < 300)
                        throw new Exception("The Mainframe drew at " + mainframe.ClientSize + ", too small for a picture.");
                    string name = settings ? "mainframe-settings.png" : "mainframe-front.png";
                    Dictionary<Control, string> plain = PlainLabels(mainframe);
                    try
                    {
                        using (Bitmap picture = new Bitmap(mainframe.ClientSize.Width, mainframe.ClientSize.Height))
                        {
                            mainframe.DrawToBitmap(picture, new Rectangle(Point.Empty, picture.Size));
                            picture.Save(Path.Combine(to, name), ImageFormat.Png);
                        }
                    }
                    finally { foreach (KeyValuePair<Control, string> entry in plain) entry.Key.Text = entry.Value; }
                    took(name);
                }
                mainframe.PictureMods(Path.Combine(to, "mods-page.png"), "");
                took("mods-page.png");
                using (Bitmap toggles = mainframe.PictureToggles()) toggles.Save(Path.Combine(to, "toggles.png"), ImageFormat.Png);
                took("toggles.png");
                mainframe.PickToggleRow("Better conversations");
                using (Bitmap toggles = mainframe.PictureToggles()) toggles.Save(Path.Combine(to, "toggles-settings.png"), ImageFormat.Png);
                took("toggles-settings.png");
                mainframe.CloseToggles();
                mainframe.PicturePersonalitySettings("Sassy", Path.Combine(to, "personality-settings.png"));
                took("personality-settings.png");
                mainframe.PicturePersonalitySettings(Personalities.Dynamic, Path.Combine(to, "personality-settings-dynamic.png"));
                took("personality-settings-dynamic.png");
                mainframe.ClosePersonalitySettings();
                mainframe.LayersAt(1f, Path.Combine(to, "voice-settings.png"));
                took("voice-settings.png");
                mainframe.ColoursAt(1f, Path.Combine(to, "custom-colours.png"));
                took("custom-colours.png");
                if (title != null) title.Text = "CAPRI";
                MenuShot(Path.Combine(to, "her-menu.png"), null);
                took("her-menu.png");
                if (PetShot(Path.Combine(to, "tamagotchi.png"))) took("tamagotchi.png");
                checks.Add("PASS the GitHub pictures, SFW side, at 100%: " + string.Join(", ", made.ToArray()));
            }
            finally
            {
                mainframe.LayoutScale(0, 0);
                mainframe.SizeMainframe(wasSize);
                mainframe.ShowSettings(wasPage);
                if (title != null) title.Text = titleWas;
                mainframe.WindowState = wasState;
                if (!wasShown) mainframe.Hide();
            }
        }

        // Words from the PC the pictures are taken on, put as a clean install shows them: no device names, her keys free
        // (another Capri on this PC holds them), no check build's version. What was there before is given back.
        private Dictionary<Control, string> PlainLabels(Control under)
        {
            Dictionary<Control, string> was = new Dictionary<Control, string>();
            string[] keys = mainframe.KeyStatus.Split(new[] { " | " }, StringSplitOptions.None);
            string version = ShownVersion(Packaged.Is, false);
            Stack<Control> left = new Stack<Control>();
            left.Push(under);
            while (left.Count > 0)
            {
                Control one = left.Pop();
                foreach (Control inner in one.Controls) left.Push(inner);
                string text = one.Text ?? "", plain = text;
                if (text.StartsWith("Hearing: ", StringComparison.Ordinal))
                    plain = System.Text.RegularExpressions.Regex.Replace(text, @"default mic \([^)]*\)", "default mic");
                else if (keys.Length == 2 && text == keys[0] && text.EndsWith(" is taken", StringComparison.Ordinal))
                    plain = text.Substring(0, text.Length - 9) + " to talk";
                else if (keys.Length == 2 && text == keys[1] && text.EndsWith(" is taken", StringComparison.Ordinal))
                    plain = text.Substring(0, text.Length - 9) + " mutes mic";
                else if (text == version && TestNumbered(Built))
                    plain = "";
                if (plain == text) continue;
                was[one] = text;
                one.Text = plain;
            }
            return was;
        }

        // Her right-click menu, with one list of it dropped open, cut to its own shape on a see-through sheet.
        private void MenuShot(string path, ToolStripMenuItem open)
        {
            menu.Show(new Point(80, 80));
            List<ToolStripDropDown> shown = new List<ToolStripDropDown> { menu };
            if (open != null)
            {
                open.Select();
                open.ShowDropDown();
                shown.Add(open.DropDown);
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

        // Her Tamagotchi menu with the pet game running, from the mod she carries, approved for the picture only.
        private bool PetShot(string path)
        {
            // The copy this build carries, laid beside the pictures; one found up the folders only when it has none.
            string laid = Path.Combine(Path.GetDirectoryName(path) ?? "", "laid");
            Mods.Stock(laid);
            string carried = Path.Combine(laid, "mods", TamagotchiMod + Mods.CodeExtension);
            string file = File.Exists(carried) ? carried : null;
            for (DirectoryInfo up = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); up != null && file == null; up = up.Parent)
                foreach (string here in new[] { Path.Combine(up.FullName, "mods", TamagotchiMod + Mods.CodeExtension), Path.Combine(up.FullName, "CapriSourceFiles", "mods", TamagotchiMod + Mods.CodeExtension) })
                    if (file == null && File.Exists(here)) file = here;
            if (file == null || tamagotchiItem == null) return false;
            Func<ModFile, string, bool> askWas = mainframe.ModAsk;
            // Put back as it was after the picture (every install has it laid in off), so the checks after this start as they would.
            Mod before = ModList.All.Find(mod => mod.File.Id == TamagotchiMod);
            bool had = before != null, wasOn = had && before.On;
            try
            {
                mainframe.ModAsk = delegate { return true; };
                if (!mainframe.ImportMod(file)) return false;
                for (int i = 0; i < 120 && ModList.Pet == null; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(25); }
                menu.Show(new Point(80, 80));
                tamagotchiItem.Select();
                tamagotchiItem.ShowDropDown();
                Application.DoEvents();
                ToolStripMenuItem gifts = null;
                foreach (ToolStripItem one in tamagotchiItem.DropDownItems) if (one.Text == "Give gift") gifts = one as ToolStripMenuItem;
                List<ToolStripDropDown> shown = new List<ToolStripDropDown> { menu, tamagotchiItem.DropDown };
                if (gifts != null) { gifts.Select(); gifts.ShowDropDown(); Application.DoEvents(); shown.Add(gifts.DropDown); }
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
                return true;
            }
            finally
            {
                mainframe.ModAsk = askWas;
                Mod ours = ModList.All.Find(mod => mod.File.Id == TamagotchiMod);
                if (ours != null && !had) ModList.Remove(ours);
                else if (ours != null && !wasOn) ModList.Set(ours, false, askWas);
                for (int i = 0; i < 120 && !wasOn && ModList.Pet != null; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(25); }
            }
        }
    }
}
#endif
