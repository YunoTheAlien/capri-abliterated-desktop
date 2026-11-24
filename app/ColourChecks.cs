using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace CapriDesktop
{
    internal sealed partial class Companion
    {
        // Custom colours' window: the player's own colours, layout.md as an update writes it naming every colour, presets
        // and last used read back as written, and the window driven at 100, 150 and 200%.
        private void SmokeColourWindow(List<string> checks, string output)
        {
            // The player has colours of its own, starting a shade off the bubble's and the two it draws fixed.
            string text = CapriDesktop.Layout.Text;
            foreach (string name in Neon.Names)
                if (!System.Text.RegularExpressions.Regex.IsMatch(text, @"(?m)^" + System.Text.RegularExpressions.Regex.Escape(name) + @":\s+# hers: #")) throw new Exception("layout.md as an update writes it has no line for " + name + ".");
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
            checks.Add("PASS her player has colours of its own in layout.md (player edge, accent, title, rail, ground), stocked to exactly how it looked in every look, each look still has no two colours a control keeps the same, and setting one leaves her bubble as it was");

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
            checks.Add("PASS Custom colours opens CUSTOM COLOURS in her Mainframe's sub-window look: Preset 1 takes the colours set and 2 and 3 start as hers, the parts of her (Speech bubble, Mainframe, Player) fold down with every colour of layout.md under a part named for what it is and a shared one saying what else it colours, a colour picked repaints her Mainframe at once and again when changed a second time, a new Mainframe colours look shows under the colours they set, the picker puts a colour first of twelve last used, a last used colour goes on the selected part without moving, an add-on's colour goes into last used from the hex box, the dropper and its swatch as hers does, + makes Preset 4 and picking a preset sets every colour, all saved in her preferences and fitting at 100%, 150% and 200% (pictures colours-window-*.png)");
        }
    }
}
