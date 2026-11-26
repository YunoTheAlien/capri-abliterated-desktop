using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

#if CHECKS
namespace CapriDesktop
{
    internal sealed partial class Companion
    {
        // A light Mainframe colours look reaches her newer windows: Toggles with every fold open, the Streaming window's
        // side and the Clear memory question. Any of the stock red look's own colours filling an area fails it.
        private void SmokeColoursFollow(List<string> checks, string output)
        {
            int lookWas = MainframeColours;
            int light = Array.IndexOf(Neon.LookNames, "White");
            if (light < 0) throw new Exception("There is no White look to try.");
            Directory.CreateDirectory(output);
            // The red look's colours that the White look doesn't share; finding one in a window means it kept the old.
            Color[] red = Neon.LookColours(0), white = Neon.LookColours(light);
            HashSet<int> stale = new HashSet<int>();
            foreach (Color one in red) stale.Add(one.ToArgb() | unchecked((int)0xFF000000));
            foreach (Color one in white) stale.Remove(one.ToArgb() | unchecked((int)0xFF000000));
            List<string> wrong = new List<string>();
            Action<Bitmap, string> look = delegate(Bitmap shot, string name)
            {
                shot.Save(Path.Combine(output, "white-" + name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                Dictionary<int, int> found = new Dictionary<int, int>();
                for (int y = 0; y < shot.Height; y++)
                    for (int x = 0; x < shot.Width; x++)
                    {
                        int argb = shot.GetPixel(x, y).ToArgb() | unchecked((int)0xFF000000);
                        if (stale.Contains(argb)) { int had; found.TryGetValue(argb, out had); found[argb] = had + 1; }
                    }
                foreach (KeyValuePair<int, int> one in found)
                    if (one.Value > 20) wrong.Add(name + " has " + one.Value + " pixels of the red look's " + Color.FromArgb(one.Key).Name);
            };
            try
            {
                // Her screensaver mod's own sliders (See through and the rest) come in as it starts; the picture waits for them
                // a few seconds, so it shows every setting the window has rather than whichever arrived first.
                for (int waited = 0; waited < 8000 && saverMenu != null; waited += 100)
                {
                    bool through = false;
                    foreach (ToolStripItem one in saverMenu.DropDownItems) { string tag = one.Tag as string; if (tag != null && tag.StartsWith(Mainframe.StepTag + "See through", StringComparison.Ordinal)) through = true; }
                    if (through) break;
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(100);
                }
                // Her own look first, as a picture to compare with the White one.
                using (Bitmap own = mainframe.PictureToggles()) own.Save(Path.Combine(output, "own-toggles.png"), System.Drawing.Imaging.ImageFormat.Png);
                // A General row with settings open on the right: its words and its status box.
                mainframe.PickToggleRow("Better conversations");
                using (Bitmap talk = mainframe.PictureToggles()) talk.Save(Path.Combine(output, "own-toggles-conversations.png"), System.Drawing.Imaging.ImageFormat.Png);
                mainframe.CloseToggles();
                SetMainframeColours(light);
                using (Bitmap toggles = mainframe.PictureToggles()) look(toggles, "toggles");
                mainframe.CloseToggles();
                streamOpenItem.PerformClick();
                Form side = mainframe.StreamingSide;
                if (side == null) throw new Exception("The Streaming window didn't open.");
                Application.DoEvents();
                using (Bitmap shot = new Bitmap(side.Width, side.Height))
                {
                    side.DrawToBitmap(shot, new Rectangle(Point.Empty, shot.Size));
                    look(shot, "streaming");
                }
                if (streamWindow != null && !streamWindow.IsDisposed) SetStreamWindow();
                using (Question question = new Question("Forget this conversation, her transcripts and her memories of you. me.md stays.", "Clear memory", 0, "Clear memory", "Cancel"))
                {
                    question.Show();
                    Application.DoEvents();
                    using (Bitmap shot = new Bitmap(question.Width, question.Height))
                    {
                        question.DrawToBitmap(shot, new Rectangle(Point.Empty, shot.Size));
                        look(shot, "clear-memory");
                    }
                    question.Close();
                }
            }
            finally
            {
                mainframe.CloseToggles();
                if (streamWindow != null && !streamWindow.IsDisposed) SetStreamWindow();
                SetMainframeColours(lookWas);
            }
            if (wrong.Count > 0) throw new Exception("A window kept the red look under White: " + string.Join("; ", wrong.ToArray()));
            checks.Add("PASS under the White Mainframe colours, Toggles with every fold open, the Streaming window's side and the Clear memory question show none of the red look's own colours (pictures white-*.png)");
        }

        // Custom colours' window: every colour of hers settable by name, presets and last used read back, at 100 to 200%.
        // Old layout.md and colours.md are carried into her settings once and kept as .bak; nothing writes an .md after.
        private void SmokeColoursOut(List<string> checks, string output)
        {
            string folder = Path.Combine(output, "colours-out-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            string keptWas = CapriDesktop.Layout.Kept;
            Func<List<string>> loadWas = CapriDesktop.Layout.Load, addonLoadWas = AddonColours.Load;
            Action<List<string>> storeWas = CapriDesktop.Layout.Store, addonStoreWas = AddonColours.Store;
            Func<string, string> folderWas = AddonColours.FolderOf;
            List<string> rows = new List<string>(), addonRows = new List<string>();
            try
            {
                Directory.CreateDirectory(Path.Combine(folder, "tint-test-data"));
                CapriDesktop.Layout.Load = delegate { return rows; };
                CapriDesktop.Layout.Store = delegate(List<string> made) { rows = made; };
                AddonColours.Load = delegate { return addonRows; };
                AddonColours.Store = delegate(List<string> made) { addonRows = made; };
                AddonColours.FolderOf = delegate(string id) { return Path.Combine(folder, id + "-data"); };
                AddonColours.Forget();
                string layout = Path.Combine(folder, CapriDesktop.Layout.FileName);
                File.WriteAllText(layout, "# Her colours\n\n## What stands out\n\nhot:               #12c85a    # hers: #e84246\ncyan:                  # hers: #5ef0f6\n");
                CapriDesktop.Layout.Kept = layout;
                int carried = CapriDesktop.Layout.CarryOld();
                if (carried != 1 || CapriDesktop.Layout.Row("hot") != "#12c85a" || CapriDesktop.Layout.Row("cyan") != null || File.Exists(layout) || !File.Exists(layout + ".bak"))
                    throw new Exception("layout.md wasn't carried in once and kept as .bak: " + carried + ", " + string.Join(", ", rows.ToArray()));
                if (CapriDesktop.Layout.CarryOld() != 0) throw new Exception("layout.md was carried in twice.");
                // A layout.md.bak already there is theirs and stays: the next one is kept as a timed spare beside it.
                File.WriteAllText(layout, "hot:               #000001\n");
                CapriDesktop.Layout.CarryOld();
                if (File.Exists(layout) || File.ReadAllText(layout + ".bak").IndexOf("#12c85a", StringComparison.Ordinal) < 0 || SpareFiles.Of(layout).Count != 2)
                    throw new Exception("A layout.md.bak already there wasn't kept: " + string.Join(", ", SpareFiles.Of(layout).ToArray()));
                string mine = Path.Combine(folder, "tint-test-data", AddonColours.FileName);
                File.WriteAllText(mine, "# Tint Test's colours\n\nGlow:              #123456    # usual: #c81e3c\n");
                Color? glow = AddonColours.TheirOwn("tint-test", "Glow");
                if (!glow.HasValue || glow.Value.ToArgb() != Color.FromArgb(0x12, 0x34, 0x56).ToArgb() || File.Exists(mine) || !File.Exists(mine + ".bak") || addonRows.Count != 1)
                    throw new Exception("A mod's colours.md wasn't carried in once and kept as .bak: " + string.Join(" | ", addonRows.ToArray()));
                CapriDesktop.Layout.Set("amber", Color.FromArgb(200, 140, 20));
                AddonColours.Set("tint-test", new Dictionary<string, Color?> { { "Glow", Color.FromArgb(10, 20, 30) } });
                if (Directory.GetFiles(folder, "*.md", SearchOption.AllDirectories).Length != 0) throw new Exception("Setting a colour wrote an .md file.");
                checks.Add("PASS her colours out of the .md files: layout.md and a mod's colours.md carried into her settings once, each kept as .bak and not read again, and a colour set after writes no .md");
            }
            finally
            {
                CapriDesktop.Layout.Kept = keptWas;
                CapriDesktop.Layout.Load = loadWas;
                CapriDesktop.Layout.Store = storeWas;
                AddonColours.Load = addonLoadWas;
                AddonColours.Store = addonStoreWas;
                AddonColours.FolderOf = folderWas;
                AddonColours.Forget();
                CapriDesktop.Layout.Forget();
                CapriDesktop.Layout.Fresh();
            }
        }

        private void SmokeColourWindow(List<string> checks, string output)
        {
            // The player has colours of its own, starting a shade off the bubble's and the two it draws fixed.
            foreach (string name in Neon.Names)
                if (Neon.Stock(name).IsEmpty) throw new Exception("Her colour " + name + " has no stock colour to set it against.");
            for (int look = 0; look < Neon.LookNames.Length; look++)
            {
                Color[] colours = Neon.LookColours(look);
                HashSet<int> slots = new HashSet<int>();
                int kept = 0;
                for (int slot = 0; slot < colours.Length; slot++) if (!Neon.PaintedOnly(slot)) { slots.Add(colours[slot].ToArgb()); kept++; }
                int edge = Array.IndexOf(Neon.Names, "player edge");
                if (slots.Count != kept || colours.Length != Neon.Names.Length || !Neon.PaintedOnly(edge) || Neon.PaintedOnly(edge - 1) || !Neon.PaintedOnly(edge + 4) || Neon.PaintedOnly(edge + 5)) throw new Exception(Neon.LookNames[look] + " has two colours a control keeps the same, or not one for each name.");
                if (colours[edge].ToArgb() != colours[Array.IndexOf(Neon.Names, "bubble outline")].ToArgb() || colours[edge + 1].ToArgb() != colours[Array.IndexOf(Neon.Names, "bubble accent")].ToArgb()
                    || colours[edge + 2].ToArgb() != colours[Array.IndexOf(Neon.Names, "bubble title")].ToArgb() || colours[edge + 3].ToArgb() != Color.FromArgb(58, 63, 74).ToArgb() || colours[edge + 4].ToArgb() != Color.FromArgb(5, 6, 9).ToArgb())
                    throw new Exception(Neon.LookNames[look] + "'s player colours aren't exactly how the player looked before.");
            }
            bool changed = Neon.Theirs("player edge", Color.FromArgb(1, 2, 3));
            try
            {
                if (!changed || Look.PlayerEdge.ToArgb() != Color.FromArgb(1, 2, 3).ToArgb() || Look.Cyan.ToArgb() == Color.FromArgb(1, 2, 3).ToArgb()) throw new Exception("Setting player edge didn't change the player alone.");
            }
            finally { Neon.Theirs("player edge", null); }
            checks.Add("PASS her player has colours of its own (player edge, accent, title, rail, ground), stocked to exactly how it looked in every look, each look still has no two colours a control keeps the same, and setting one leaves her bubble as it was");

            // Her zones are the six named for them and hold only her own colours; an add-on's never mix into them.
            List<string> hers = new List<string>(Neon.Names);
            if (ColourPresets.Zones.Length != 6 || ColourPresets.Zones[0] != "Speech bubble" || ColourPresets.Zones[1] != "Mainframe" || ColourPresets.Zones[2] != "Menus and notes" || ColourPresets.Zones[3] != "Player" || ColourPresets.Zones[4] != "Tamagotchi" || ColourPresets.Zones[5] != "File editor")
                throw new Exception("Custom colours' zones aren't Speech bubble, Mainframe, Menus and notes, Player, Tamagotchi and File editor: " + string.Join(", ", ColourPresets.Zones));
            // Menus and notes holds her menus, notes and the Type box, and the Mainframe's list no longer does.
            List<string> menusAndNotes = new List<string>(Array.ConvertAll(ColourPresets.Parts[2], part => part.Colour));
            if (!menusAndNotes.Contains("menu") || !menusAndNotes.Contains("sheet") || !menusAndNotes.Contains("well") || Array.Exists(ColourPresets.Parts[1], part => part.Colour == "menu" || part.Colour == "sheet"))
                throw new Exception("Menus and notes doesn't hold her menu, notes and Type box colours, or the Mainframe still lists them: " + string.Join(", ", menusAndNotes.ToArray()));
            // A colour saved for one of them while it sat under Mainframe is kept by its name, so it carries to the new zone.
            List<KeyValuePair<string, Dictionary<string, Color>>> saved = ColourPresets.Read("Before=menu:#123456,sheet text:#abcdef,sheet faint:#010203,well:#0a0b0c");
            Dictionary<string, Color> carried = saved.Count == 1 ? saved[0].Value : new Dictionary<string, Color>();
            string[] carriedKeys = { "menu", "sheet text", "sheet faint", "well" };
            string[] carriedHex = { "#123456", "#abcdef", "#010203", "#0a0b0c" };
            for (int i = 0; i < carriedKeys.Length; i++)
                if (!carried.ContainsKey(carriedKeys[i]) || ColourPresets.Hex(carried[carriedKeys[i]]) != carriedHex[i] || !menusAndNotes.Contains(carriedKeys[i]))
                    throw new Exception("A colour saved for " + carriedKeys[i] + " before the Menus and notes zone didn't carry to it: " + (carried.ContainsKey(carriedKeys[i]) ? ColourPresets.Hex(carried[carriedKeys[i]]) : "missing"));
            foreach (ColourPresets.Part[] zone in ColourPresets.Parts)
                foreach (ColourPresets.Part part in zone)
                    if (!hers.Contains(part.Colour)) throw new Exception(part.Shown + " in Custom colours isn't one of her own colours.");
            checks.Add("PASS Custom colours' zones are Speech bubble, Mainframe, Menus and notes (her menus, notes and the Type box, a colour saved for one before carried by its name), Player and Tamagotchi's gold points, and list only her own colours, none of an add-on's");

            List<KeyValuePair<string, Dictionary<string, Color>>> presets = ColourPresets.Read("Preset 1=hot:#12c85a,nothing:#ffffff,cyan:#zz|Preset 2=|Preset 1=rose:#000000|Mine=player rail:#010203");
            if (presets.Count != 3 || presets[0].Value.Count != 1 || presets[2].Key != "Mine" || ColourPresets.Write(presets) != "Preset 1=hot:#12c85a|Preset 2=|Mine=player rail:#010203" || ColourPresets.NewName(presets) != "Preset 4")
                throw new Exception("Presets didn't read back as written: " + ColourPresets.Write(presets));
            List<Color> recent = new List<Color>();
            for (int i = 0; i < 14; i++) ColourPresets.Push(recent, Color.FromArgb(i, i, i));
            ColourPresets.Push(recent, Color.FromArgb(5, 5, 5));
            if (recent.Count != ColourPresets.Recent || recent[0].ToArgb() != Color.FromArgb(5, 5, 5).ToArgb() || recent[1].ToArgb() != Color.FromArgb(13, 13, 13).ToArgb() || ColourPresets.ReadRecent(ColourPresets.WriteRecent(recent)).Count != ColourPresets.Recent)
                throw new Exception("Last used didn't keep twelve, newest first, each once: " + ColourPresets.WriteRecent(recent));
            Func<Color, Color, bool> near = delegate(Color a, Color b) { return Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) <= 1; };
            for (int hue = 0; hue < 360; hue += 15)
            {
                Color made = ColourPresets.FromHsv(hue, 0.7f, 0.8f);
                float h, s, v;
                ColourPresets.ToHsv(made, out h, out s, out v);
                if (!near(ColourPresets.FromHsv(h, s, v), made)) throw new Exception("The picker's colour didn't survive a turn through hue: " + made);
            }
            checks.Add("PASS Custom colours' presets and last used read back as written, a colour that isn't one of hers is dropped, last used keeps twelve newest first with each colour once, and the picker's hue turns a colour back into itself");

            if (mainframe == null) return;
            mainframe.CloseColours();
            float machine = mainframe.Px(1000) / 1000f;
            List<string> wrongs = new List<string>();
            // The window belongs to her Mainframe, so it is up while the window is driven, as it is when they use it: a
            // minimised one left by an earlier check took the window with it the moment it was brought to the front.
            System.Windows.Forms.FormWindowState stateWas = mainframe.WindowState;
            bool shownWas = mainframe.Visible;
            try
            {
                mainframe.Restore();
                foreach (float zoom in new[] { 1f, 1.5f, 2f })
                {
                    string wrong = mainframe.ColoursAt(zoom / machine, Path.Combine(output, "colours-window-" + (int)(zoom * 100) + ".png"));
                    if (wrong != null) wrongs.Add((int)(zoom * 100) + "% " + wrong);
                }
            }
            finally
            {
                mainframe.WindowState = stateWas;
                if (!shownWas) mainframe.Hide();
            }
            if (wrongs.Count > 0) throw new Exception("The CUSTOM COLOURS window went wrong: " + string.Join(" / ", wrongs.ToArray()));
            checks.Add("PASS Custom colours opens on its CAPRI fold at the top, Hair selected: a colour typed on her Hair, Accents, Eyes, Edges and Body goes on her art, and Reset selected colour puts each back to the shown skin's stock, the last one ending My Colors; on an add-on's colour it goes back to the add-on's own and on one of hers to its default over Mainframe colours, at 100%, 150% and 200% (pictures colours-window-*-capri.png)");
            if (Mainframe.RealInputOn)
                checks.Add("PASS the dropper with the real mouse at 100, 150 and 200: with no part chosen its key says to choose one; with a part, a click on the key starts it, a click on the screen takes the colour there onto the part, and a right click cancels with nothing changed");
            else checks.Add("LIMIT the dropper with the real mouse: it moves the real cursor, so it runs only on Sapphire (" + Mainframe.RealInputVariable + "=1); its stubbed parts ran here");
            checks.Add("PASS Custom colours opens CUSTOM COLOURS in her Mainframe's sub-window look: Preset 1 takes the colours set and 2 and 3 start as hers, the parts of her (Speech bubble, Mainframe, Player) fold down with every colour of hers under a part named for what it is and a shared one saying what else it colours, a colour picked repaints her Mainframe at once and again when changed a second time, a new Mainframe colours look shows under the colours they set, the picker puts a colour first of twelve last used, a last used colour goes on the selected part without moving, an add-on's colour goes into last used from the hex box, the dropper and its swatch as hers does, + makes Preset 4 and picking a preset sets every colour, all saved in her preferences and fitting at 100%, 150% and 200% (pictures colours-window-*.png)");
        }
    }
}
#endif
