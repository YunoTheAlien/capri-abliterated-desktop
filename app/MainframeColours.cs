using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Custom colours' zones, presets and the last colours picked. Presets and last used are kept as text in her preferences
    // so settings.json carries them; the colours in use stay in layout.md, and a preset is a copy of them to switch back to.
    internal static class ColourPresets
    {
        internal const int Recent = 12;
        internal static readonly string[] Stock = { "Preset 1", "Preset 2", "Preset 3" };

        // One thing on screen and the colour of layout.md it is drawn in.
        internal sealed class Part
        {
            internal readonly string Shown, Colour;
            internal Part(string shown, string colour) { Shown = shown; Colour = colour; }
        }

        // The parts of her they colour, each named for what it is on screen; Tamagotchi's points have their own fold. A
        // colour drawn in two zones is in both, and each says what else it colours.
        internal static readonly string[] Zones = { "Speech bubble", "Mainframe", "Player", "Tamagotchi" };
        internal static readonly Part[][] Parts =
        {
            new[]
            {
                new Part("Bubble outline", "bubble outline"), new Part("Bubble title", "bubble title"), new Part("Bubble text", "bubble text"),
                new Part("Bubble highlights", "bubble accent"), new Part("Bubble background", "bubble background")
            },
            new[]
            {
                new Part("Panel background", "ground"), new Part("Glow at the top", "glow"), new Part("Section band", "band"),
                new Part("Section line", "rule"), new Part("Titles and setting names", "rose"), new Part("Text", "text"),
                new Part("Faint text", "faint"), new Part("Values and highlights", "cyan"), new Part("Button", "field"),
                new Part("Button under the mouse", "hover"), new Part("Button pressed", "pressed"), new Part("Button edge", "edge"),
                new Part("Button when lit, and corner strokes", "hot"), new Part("Main button", "deep"), new Part("Main button edge", "deep edge"),
                new Part("Text on a lit button", "on ink"), new Part("Slider track", "dash"), new Part("Slider when moved", "lit"),
                new Part("Window outline", "brim"), new Part("Text box and Type box background", "well"), new Part("Text box edge", "well edge"),
                new Part("Warnings", "amber"), new Part("Menu background", "menu"), new Part("Notes background", "sheet"),
                new Part("Notes text", "sheet text"), new Part("Notes faint text", "sheet faint")
            },
            new[]
            {
                new Part("Player background", "player ground"), new Part("Edge, right end, and artist", "player edge"),
                new Part("Edge, left end, and buttons turned on", "player accent"), new Part("Close button", "player title"),
                new Part("Progress track", "player rail"), new Part("Song title", "sheet text"), new Part("Time and buttons", "sheet faint")
            },
            new[] { new Part("Points and costs", "points") }
        };

        // What else a part's colour is drawn in, in the other zones: "Also: Song title (Player)".
        internal static string Also(int zone, string colour)
        {
            List<string> others = new List<string>();
            for (int z = 0; z < Parts.Length; z++)
                if (z != zone)
                    foreach (Part part in Parts[z])
                        if (part.Colour == colour) others.Add(part.Shown + " (" + Zones[z] + ")");
            return others.Count == 0 ? null : "Also: " + string.Join(", ", others.ToArray());
        }

        // A part's own name, for the colour picked: the first part drawn in it, or an add-on colour's own name.
        internal static string NameOf(string colour)
        {
            string id, name;
            if (Addon(colour, out id, out name)) return name;
            foreach (Part[] zone in Parts) foreach (Part part in zone) if (part.Colour == colour) return part.Shown;
            return colour;
        }

        // An add-on's colour, kept apart from hers as "mod:<id>:<name>" in presets and in the window.
        internal const string AddonMark = "mod:";

        internal static string AddonKey(string id, string name) { return AddonMark + id + ":" + name; }

        internal static bool Addon(string key, out string id, out string name)
        {
            id = name = null;
            if (key == null || !key.StartsWith(AddonMark, StringComparison.Ordinal)) return false;
            int mark = key.IndexOf(':', AddonMark.Length);
            if (mark <= AddonMark.Length || mark >= key.Length - 1) return false;
            id = key.Substring(AddonMark.Length, mark - AddonMark.Length);
            name = key.Substring(mark + 1);
            return true;
        }

        // One of hers, or one an add-on can name.
        internal static bool Valid(string key)
        {
            string id, name;
            return Addon(key, out id, out name) ? AddonColours.Clean(name) == name : !Neon.Stock(key).IsEmpty;
        }

        internal static Color ShownOf(string key)
        {
            string id, name;
            return Addon(key, out id, out name) ? AddonColours.Shown(id, name) : Neon.Shown(key);
        }

        internal static Color? TheirsOf(string key)
        {
            string id, name;
            return Addon(key, out id, out name) ? AddonColours.TheirOwn(id, name) : Neon.TheirOwn(key);
        }

        // Set or clear one: hers in layout.md, an add-on's in its own colours.md.
        internal static bool SetOne(string key, Color? colour)
        {
            string id, name;
            if (!Addon(key, out id, out name)) return CapriDesktop.Layout.Set(key, colour);
            Dictionary<string, Color?> one = new Dictionary<string, Color?>();
            one[name] = colour;
            return AddonColours.Set(id, one);
        }

        internal static string Hex(Color colour) { return "#" + colour.R.ToString("x2") + colour.G.ToString("x2") + colour.B.ToString("x2"); }

        internal static Color? Parsed(string hex)
        {
            string bare = (hex ?? "").Trim().TrimStart('#');
            int value;
            if (bare.Length != 6 || !int.TryParse(bare, System.Globalization.NumberStyles.HexNumber, null, out value)) return null;
            return Color.FromArgb(255, (value >> 16) & 255, (value >> 8) & 255, value & 255);
        }

        // The colours they have set now, hers by name and each installed add-on's by its id; one left as hers or the mod's
        // own isn't in it.
        internal static Dictionary<string, Color> Current()
        {
            Dictionary<string, Color> now = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in Neon.Names) { Color? theirs = Neon.TheirOwn(name); if (theirs.HasValue) now[name] = theirs.Value; }
            foreach (KeyValuePair<string, string> mod in AddonColours.Trees())
                foreach (string name in AddonColours.Names(mod.Key)) { Color? theirs = AddonColours.TheirOwn(mod.Key, name); if (theirs.HasValue) now[AddonKey(mod.Key, name)] = theirs.Value; }
            return now;
        }

        // What is set now, keeping what a preset held for an add-on that isn't installed, for when it comes back.
        internal static Dictionary<string, Color> Kept(Dictionary<string, Color> before)
        {
            Dictionary<string, Color> now = Current();
            HashSet<string> installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (AddonColours.Installed != null) foreach (KeyValuePair<string, string> mod in AddonColours.Installed()) installed.Add(mod.Key);
            if (before != null)
                foreach (KeyValuePair<string, Color> entry in before)
                {
                    string id, name;
                    if (Addon(entry.Key, out id, out name) && !installed.Contains(id) && !now.ContainsKey(entry.Key)) now[entry.Key] = entry.Value;
                }
            return now;
        }

        // "Preset 1=hot:#12c85a,cyan:#ffffff|Preset 2=", in order. A name that isn't one of hers, or a colour that isn't
        // one, is left out rather than spoiling the rest.
        internal static List<KeyValuePair<string, Dictionary<string, Color>>> Read(string saved)
        {
            List<KeyValuePair<string, Dictionary<string, Color>>> all = new List<KeyValuePair<string, Dictionary<string, Color>>>();
            foreach (string one in (saved ?? "").Split('|'))
            {
                int mark = one.IndexOf('=');
                string name = (mark < 0 ? one : one.Substring(0, mark)).Trim();
                if (name.Length == 0 || Find(all, name) >= 0) continue;
                Dictionary<string, Color> colours = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
                if (mark >= 0)
                    foreach (string entry in one.Substring(mark + 1).Split(','))
                    {
                        int colon = entry.LastIndexOf(':');
                        if (colon <= 0) continue;
                        string colour = entry.Substring(0, colon).Trim();
                        Color? value = Parsed(entry.Substring(colon + 1));
                        if (value.HasValue && Valid(colour)) colours[colour] = value.Value;
                    }
                all.Add(new KeyValuePair<string, Dictionary<string, Color>>(name, colours));
            }
            return all;
        }

        internal static string Write(List<KeyValuePair<string, Dictionary<string, Color>>> all)
        {
            StringBuilder made = new StringBuilder();
            foreach (KeyValuePair<string, Dictionary<string, Color>> preset in all)
            {
                if (made.Length > 0) made.Append('|');
                made.Append(preset.Key).Append('=');
                bool first = true;
                List<string> order = new List<string>(Neon.Names);
                foreach (string key in preset.Value.Keys) if (!order.Contains(key)) order.Add(key);
                foreach (string name in order)
                {
                    Color colour;
                    if (!preset.Value.TryGetValue(name, out colour)) continue;
                    if (!first) made.Append(',');
                    made.Append(name).Append(':').Append(Hex(colour));
                    first = false;
                }
            }
            return made.ToString();
        }

        internal static int Find(List<KeyValuePair<string, Dictionary<string, Color>>> all, string name)
        {
            for (int i = 0; i < all.Count; i++) if (string.Equals(all[i].Key, name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        // Preset 4, 5 and on: the first number not taken.
        internal static string NewName(List<KeyValuePair<string, Dictionary<string, Color>>> all)
        {
            for (int number = all.Count + 1; ; number++)
                if (Find(all, "Preset " + number) < 0) return "Preset " + number;
        }

        internal static List<Color> ReadRecent(string saved)
        {
            List<Color> recent = new List<Color>();
            foreach (string one in (saved ?? "").Split(','))
            {
                Color? colour = Parsed(one);
                if (colour.HasValue && recent.Count < Recent) recent.Add(colour.Value);
            }
            return recent;
        }

        internal static string WriteRecent(List<Color> recent) { return string.Join(",", recent.ConvertAll(Hex).ToArray()); }

        // A colour picked goes to the front, once, and the oldest drops off past twelve.
        internal static void Push(List<Color> recent, Color colour)
        {
            recent.RemoveAll(delegate(Color one) { return one.ToArgb() == Color.FromArgb(255, colour).ToArgb(); });
            recent.Insert(0, Color.FromArgb(255, colour));
            if (recent.Count > Recent) recent.RemoveRange(Recent, recent.Count - Recent);
        }

        // Hue 0 to 360, saturation and value 0 to 1.
        internal static Color FromHsv(float hue, float saturation, float value)
        {
            float h = ((hue % 360) + 360) % 360 / 60f, c = value * saturation, x = c * (1 - Math.Abs(h % 2 - 1)), m = value - c;
            float r, g, b;
            if (h < 1) { r = c; g = x; b = 0; }
            else if (h < 2) { r = x; g = c; b = 0; }
            else if (h < 3) { r = 0; g = c; b = x; }
            else if (h < 4) { r = 0; g = x; b = c; }
            else if (h < 5) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            return Color.FromArgb(255, Byte(r + m), Byte(g + m), Byte(b + m));
        }

        private static int Byte(float part) { return Math.Max(0, Math.Min(255, (int)Math.Round(part * 255))); }

        internal static void ToHsv(Color colour, out float hue, out float saturation, out float value)
        {
            float r = colour.R / 255f, g = colour.G / 255f, b = colour.B / 255f;
            float most = Math.Max(r, Math.Max(g, b)), least = Math.Min(r, Math.Min(g, b)), span = most - least;
            value = most;
            saturation = most <= 0 ? 0 : span / most;
            if (span <= 0) hue = 0;
            else if (most == r) hue = 60 * (((g - b) / span) % 6);
            else if (most == g) hue = 60 * ((b - r) / span + 2);
            else hue = 60 * ((r - g) / span + 4);
            if (hue < 0) hue += 360;
        }
    }

    internal sealed partial class Companion
    {
        internal string ColourPresetsSaved { get { return preferences.ColourPresets; } }
        internal string ColourPresetPicked { get { return preferences.ColourPreset; } }
        internal string ColourRecentSaved { get { return preferences.ColourRecent; } }

        internal void SaveColours(string presets, string picked, string recent)
        {
            if (preferences.ColourPresets == presets && preferences.ColourPreset == picked && preferences.ColourRecent == recent) return;
            preferences.ColourPresets = presets;
            preferences.ColourPreset = picked;
            preferences.ColourRecent = recent;
            SavePreferences();
        }
    }

    // Custom colours, from Settings > GENERAL SETTINGS: her parts in fold-downs on the left, a picker with the last
    // twelve colours on the right, presets over both. Drawn from the global Mainframe colours; a pick repaints every
    // window of hers.
    internal sealed partial class Mainframe
    {
        private ColourSheet colourSheet;

        // Made fresh when the Mainframe's size changed, like her GIFs window.
        internal void OpenColours()
        {
            if (colourSheet != null && !colourSheet.IsDisposed && colourSheet.Zoom != zoom) colourSheet.Close();
            if (colourSheet == null || colourSheet.IsDisposed) colourSheet = new ColourSheet(this, zoom);
            if (!colourSheet.Visible) colourSheet.Show(this);
            colourSheet.Activate();
        }

        internal void CloseColours() { if (colourSheet != null && !colourSheet.IsDisposed) colourSheet.Close(); }

        internal string ColourSheetShown { get { return colourSheet == null || colourSheet.IsDisposed || !colourSheet.Visible ? null : colourSheet.Snapshot; } }

        internal Button ColoursKey { get { return customColours; } }

        // Whether the colour window's real-mouse check may run: only on a test box, where the run sets the variable.
        internal static bool RealInputOn { get { return ColourSheet.RealInputOn; } }
        internal const string RealInputVariable = ColourSheet.RealInputVariable;

        // The window driven as they'd use it at one size, on a copy of layout.md and with their presets put back after. Null
        // when it all went right, else what didn't, with the size.
        internal string ColoursAt(float zoomAt, string picture)
        {
            string kept = CapriDesktop.Layout.Kept;
            string copy = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "capri-layout-" + Guid.NewGuid().ToString("N") + ".md");
            string presetsWas = capri.ColourPresetsSaved, pickedWas = capri.ColourPresetPicked, recentWas = capri.ColourRecentSaved;
            Func<string, string> folderWas = AddonColours.FolderOf;
            Func<List<KeyValuePair<string, string>>> installedWas = AddonColours.Installed;
            string addons = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "capri-addons-" + Guid.NewGuid().ToString("N"));
            try
            {
                // One made-up add-on with a colour of its own, in a folder of the check's.
                AddonColours.FolderOf = delegate(string id) { return System.IO.Path.Combine(addons, id + "-data"); };
                AddonColours.Installed = delegate { return new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("tint-test", "Tint Test") }; };
                AddonColours.Forget();
                AddonColours.Declare("tint-test", "Glow", Color.FromArgb(200, 30, 60));
                CapriDesktop.Layout.Kept = copy;
                CapriDesktop.Layout.Forget();
                CapriDesktop.Layout.Write(true);
                CapriDesktop.Layout.Fresh();
                capri.RepaintColours();
                capri.SaveColours(null, null, null);
                Color amber = Color.FromArgb(200, 140, 20);
                if (!CapriDesktop.Layout.Set("amber", amber)) return "Setting a custom colour failed.";
                capri.RepaintColours();
                using (ColourSheet drawn = new ColourSheet(this, zoomAt))
                {
                    drawn.Show(this);
                    try
                    {
                        string problem = drawn.Drive(picture, amber);
                        return problem == null ? null : problem + " (" + drawn.ClientSize.Width + " x " + drawn.ClientSize.Height + " at " + zoomAt + ")";
                    }
                    finally { drawn.Close(); }
                }
            }
            finally
            {
                CapriDesktop.Layout.Kept = kept;
                CapriDesktop.Layout.Forget();
                CapriDesktop.Layout.Fresh();
                capri.RepaintColours();
                capri.SaveColours(presetsWas, pickedWas, recentWas);
                AddonColours.FolderOf = folderWas;
                AddonColours.Installed = installedWas;
                AddonColours.Forget();
                try { System.IO.File.Delete(copy); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                try { if (System.IO.Directory.Exists(addons)) System.IO.Directory.Delete(addons, true); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
        }

        // The picker: her square and hue bar, the colour picked, and the twelve last used. Placed by the window that holds
        // it, at design px.
        private sealed class Picker
        {
            internal readonly Shade Square = new Shade(false), HueBar = new Shade(true);
            internal readonly Swatch Preview = new Swatch();
            internal readonly Swatch[] Slots = new Swatch[ColourPresets.Recent];
            internal float Hue, Saturation, Value;
            // The colour as it was given until the picker moves, so a colour shown and set again isn't rounded through HSV.
            internal Color? Exact;
            internal Action<Color, bool> Picked;
            internal List<Color> Recent = new List<Color>();

            internal Picker()
            {
                Square.AccessibleName = "Colour: saturation across, brightness down";
                HueBar.AccessibleName = "Hue";
                Square.Moved += delegate { Saturation = Square.Across; Value = 1 - Square.Down; Moved(); };
                HueBar.Moved += delegate { Hue = HueBar.Down * 360f; Moved(); };
                Square.Done += delegate { Give(Colour, true); };
                HueBar.Done += delegate { Give(Colour, true); };
                Preview.AccessibleName = "The colour picked";
                for (int i = 0; i < Slots.Length; i++)
                {
                    int slot = i;
                    Swatch one = new Swatch { Cursor = Cursors.Hand, TabStop = true };
                    one.Click += delegate { UseRecent(slot); };
                    one.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { e.SuppressKeyPress = true; UseRecent(slot); } };
                    Slots[i] = one;
                }
            }

            internal Color Colour { get { return Exact ?? ColourPresets.FromHsv(Hue, Saturation, Value); } }

            private void Give(Color colour, bool toRecent) { if (Picked != null) Picked(colour, toRecent); }

            private void Moved()
            {
                Exact = null;
                Square.Hue = Hue;
                Square.Invalidate();
                Preview.Colour = Colour;
            }

            internal void Show(Color colour)
            {
                Exact = Color.FromArgb(255, colour);
                ColourPresets.ToHsv(colour, out Hue, out Saturation, out Value);
                Square.Hue = HueBar.Hue = Hue;
                Square.Across = Saturation;
                Square.Down = 1 - Value;
                HueBar.Down = Hue / 360f;
                Square.Invalidate();
                HueBar.Invalidate();
                Preview.Colour = Exact.Value;
            }

            internal void UseRecent(int slot)
            {
                if (slot >= Recent.Count) return;
                Show(Recent[slot]);
                Give(Recent[slot], false);
            }

            internal void ShowRecent()
            {
                for (int i = 0; i < Slots.Length; i++)
                {
                    bool has = i < Recent.Count;
                    Slots[i].Colour = has ? Recent[i] : Color.Empty;
                    Slots[i].Enabled = has;
                    Slots[i].AccessibleName = has ? "Last used " + (i + 1) + ", " + ColourPresets.Hex(Recent[i]) : "Last used " + (i + 1) + ", empty";
                }
            }
        }

        private sealed class ColourSheet : ModSheet
        {
            private const int Wide = 800, Tall = 580, ListWide = 372, ListTall = 408, Right = 424;
            private readonly Key presetKey, plusKey, tuckKey, setHex, hersKey;
            private readonly Entry hexBox = new Entry();
            private readonly Label pickedName, alsoNote, notice;
            private readonly ScrollPanel zones;
            private readonly SlimBar zonesBar = new SlimBar();
            private readonly Picker picker = new Picker();
            private readonly ContextMenuStrip presetMenu;
            private readonly DropperKey dropper = new DropperKey();
            private readonly System.Windows.Forms.Timer noticeTimer = new System.Windows.Forms.Timer();
            private readonly HashSet<string> open = new HashSet<string>(StringComparer.Ordinal);
            private readonly Action repainted;
            private List<KeyValuePair<string, Dictionary<string, Color>>> presets;
            private string picked, selected;

            internal ColourSheet(Mainframe owner, float zoom) : base(owner, zoom, "Custom colours", "CUSTOM COLOURS >", Wide, Tall)
            {
                // Tucked away until Custom colours is pressed again, as it was; a pick in hand ends first.
                tuckKey = Tucks(DropperCancel);
Words("Each part of her, in the colour you pick. One left as hers follows Mainframe colours.", 28, 52, 744, 18, Small(), Faint);
                presetKey = Make("Preset 1", 28, 78, 220, 26, delegate { ShowPresets(); });
                presetKey.Chevron = true;
                presetKey.AccessibleName = "Preset: pick a saved set of colours";
                // Wide enough for its "+" at any text size Windows is set to.
                plusKey = Make("+", 254, 78, 36, 26, delegate { NewPreset(); });
                plusKey.AccessibleName = "New preset from the colours you have now";
                Font small = plusKey.Font;
                plusKey.Font = Type(13f * Zoom, true);
                small.Dispose();
                presetMenu = owner.Dropdown();
                presetMenu.AccessibleName = "Custom colours presets";
                // Her own slim bar beside the list, not Windows' grey one.
                zones = new ScrollPanel { Bounds = Box(28, 116, ListWide, ListTall), BackColor = Ground };
                zones.Scrolled += delegate { zonesBar.Invalidate(); };
                Controls.Add(zones);
                zonesBar.Bounds = new Rectangle(Z(28 + ListWide + 4), Z(116), Z(SlimBar.Thin), Z(ListTall));
                zonesBar.BackColor = Ground;
                zonesBar.Watch(zones);
                Controls.Add(zonesBar);
                pickedName = Words("", Right, 114, 260, 20, Type(10f * Zoom, true), Rose);
                alsoNote = Words("", Right, 136, 252, 18, Small(), Faint);
                picker.Square.Bounds = Box(Right, 160, 220, 220);
                picker.HueBar.Bounds = Box(Right + 230, 160, 22, 220);
                Controls.Add(picker.Square);
                Controls.Add(picker.HueBar);
                Words("Last used", Right + 268, 136, 84, 18, Small(), Faint);
                for (int i = 0; i < picker.Slots.Length; i++)
                {
                    picker.Slots[i].Bounds = Box(Right + 268 + i % 2 * 40, 160 + i / 2 * 38, 30, 30);
                    Controls.Add(picker.Slots[i]);
                }
                picker.Preview.Bounds = Box(Right, 392, 214, 26);
                // The dropper: any colour on the screen, hers and this window's included.
                dropper.Bounds = Box(Right + 222, 392, 30, 26);
                dropper.AccessibleName = "Dropper: pick a colour from anywhere on the screen. Esc cancels";
                dropper.Click += delegate { if (!dropper.Picking) DropperStart(); };
                Controls.Add(dropper);
                Controls.Add(picker.Preview);
                // A colour typed as #rrggbb, set on the part; Hers puts the part back to her own colour.
                hexBox.Bounds = Box(Right, 428, 120, 26);
                hexBox.Box.Font = Type(9.5f * Zoom);
                hexBox.Cue = "#rrggbb";
                hexBox.Box.AccessibleName = "Colour as hex, like #ff2e88";
                hexBox.Box.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.Handled = e.SuppressKeyPress = true; SetHex(); } };
                Controls.Add(hexBox);
                setHex = Make("Set", Right + 126, 428, 60, 26, delegate { SetHex(); });
                setHex.AccessibleName = "Set the part to the colour typed";
                hersKey = Make("Hers", Right + 192, 428, 60, 26, delegate { BackToHers(); });
                hersKey.AccessibleName = "Put the part back to her own colour";
                notice = Words("", Right, 464, 348, 40, Small(), Cyan);
                Words("Click a part on the left, then pick its colour. A last used colour goes on it and stays in its place.", 28, 546, 744, 18, Small(), Faint);
                picker.Picked = Commit;
                noticeTimer.Interval = 8000;
                noticeTimer.Tick += delegate { Says("", false); };
                // A new look, or a colour changed anywhere, shows here at once too.
                repainted = delegate { if (!IsDisposed && selected != null) Choose(selected); };
                Neon.Repainted += repainted;
                FormClosed += delegate { noticeTimer.Stop(); };
                Disposed += delegate { Neon.Repainted -= repainted; noticeTimer.Dispose(); presetMenu.Dispose(); };
                LoadSaved();
                open.Add(ColourPresets.Zones[0]);
                FillZones();
                Choose(ColourPresets.Parts[0][0].Colour);
                ResumeLayout(false);
            }

            // Their presets and last used, or Preset 1 holding the colours they have now with 2 and 3 left as hers.
            private void LoadSaved()
            {
                presets = ColourPresets.Read(Frame.capri.ColourPresetsSaved);
                picker.Recent = ColourPresets.ReadRecent(Frame.capri.ColourRecentSaved);
                if (presets.Count == 0)
                {
                    presets.Add(new KeyValuePair<string, Dictionary<string, Color>>(ColourPresets.Stock[0], ColourPresets.Current()));
                    for (int i = 1; i < ColourPresets.Stock.Length; i++) presets.Add(new KeyValuePair<string, Dictionary<string, Color>>(ColourPresets.Stock[i], new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)));
                }
                picked = Frame.capri.ColourPresetPicked;
                if (picked == null || ColourPresets.Find(presets, picked) < 0) picked = presets[0].Key;
                presetKey.Text = picked;
                Save();
                picker.ShowRecent();
            }

            private void Save() { Frame.capri.SaveColours(ColourPresets.Write(presets), picked, ColourPresets.WriteRecent(picker.Recent)); }

            private void Says(string text, bool problem)
            {
                noticeTimer.Stop();
                notice.Text = text ?? "";
                notice.ForeColor = problem ? Rose : Cyan;
                if (!string.IsNullOrEmpty(text)) noticeTimer.Start();
            }

            // The one heading every add-on's fold-down sits under.
            private const string AddonsZone = "addons";

            private void FillZones()
            {
                zones.SuspendLayout();
                int scrolled = -zones.AutoScrollPosition.Y;
                foreach (Control old in new List<Control>(ControlsOf(zones)))
                {
                    Font own = old.Font;
                    zones.Controls.Remove(old);
                    old.Dispose();
                    if (!ReferenceEquals(own, zones.Font)) own.Dispose();
                }
                // Laid out from the top of what scrolls: a scrolled panel places a row relative to what shows, which
                // would leave a gap above the first fold-down. Scrolled back after.
                zones.AutoScrollPosition = Point.Empty;
                int y = 0;
                for (int z = 0; z < ColourPresets.Zones.Length; z++)
                {
                    string zone = ColourPresets.Zones[z];
                    bool shown = open.Contains(zone);
                    Fold head = new Fold { Text = zone.ToUpperInvariant(), Font = Type(10f * Zoom, true), Bounds = new Rectangle(0, Z(y), Z(ListWide), Z(27)), Open = shown };
                    head.AccessibleName = zone + (shown ? ", open" : ", folded");
                    head.Click += delegate { if (!open.Remove(zone)) open.Add(zone); FillZones(); };
                    zones.Controls.Add(head);
                    y += 31;
                    if (!shown) continue;
                    foreach (ColourPresets.Part part in ColourPresets.Parts[z])
                    {
                        string colour = part.Colour, also = ColourPresets.Also(z, colour);
                        int tall = also == null ? 26 : 40;
                        Row row = new Row(part.Shown, colour, also) { Font = Type(9.5f * Zoom), Bounds = new Rectangle(Z(6), Z(y), Z(ListWide - 6), Z(tall)), Chosen = colour == selected };
                        row.Click += delegate { Choose(colour); };
                        row.SwatchClicked += delegate { SwatchTaken(colour); };
                        zones.Controls.Add(row);
                        y += tall + 2;
                    }
                    y += 6;
                }
                // The add-ons that named colours, under one ADD-ONS heading below hers, each in a fold-down of its own a step in,
                // so hers and theirs never look alike or mix.
                List<KeyValuePair<string, string>> addons = AddonColours.Trees();
                if (addons.Count > 0)
                {
                    bool all = open.Contains(AddonsZone);
                    Fold heading = new Fold { Text = "ADD-ONS", Font = Type(10f * Zoom, true), Bounds = new Rectangle(0, Z(y), Z(ListWide), Z(27)), Open = all };
                    heading.AccessibleName = "Add-ons, " + (all ? "open" : "folded");
                    heading.Click += delegate { if (!open.Remove(AddonsZone)) open.Add(AddonsZone); FillZones(); };
                    zones.Controls.Add(heading);
                    y += 31;
                    if (all)
                        foreach (KeyValuePair<string, string> mod in addons)
                        {
                            string zone = "addon:" + mod.Key, id = mod.Key;
                            bool shown = open.Contains(zone);
                            Fold head = new Fold { Text = mod.Value.ToUpperInvariant(), Font = Type(9.5f * Zoom, true), Bounds = new Rectangle(Z(12), Z(y), Z(ListWide - 12), Z(25)), Open = shown };
                            head.AccessibleName = mod.Value + ", add-on colours, " + (shown ? "open" : "folded");
                            head.Click += delegate { if (!open.Remove(zone)) open.Add(zone); FillZones(); };
                            zones.Controls.Add(head);
                            y += 29;
                            if (!shown) continue;
                            foreach (string name in AddonColours.Names(id))
                            {
                                string colour = ColourPresets.AddonKey(id, name);
                                Row row = new Row(name, colour, null) { Font = Type(9.5f * Zoom), Bounds = new Rectangle(Z(18), Z(y), Z(ListWide - 18), Z(26)), Chosen = colour == selected };
                                row.Click += delegate { Choose(colour); };
                                zones.Controls.Add(row);
                                y += 28;
                            }
                            y += 6;
                        }
                }
                zones.ResumeLayout();
                zones.AutoScrollPosition = new Point(0, scrolled);
                // Told once the rows are laid out: while they were added the list didn't know how tall it was yet.
                zonesBar.Sync();
            }

            private static List<Control> ControlsOf(Control parent)
            {
                List<Control> all = new List<Control>();
                foreach (Control one in parent.Controls) all.Add(one);
                return all;
            }

            private void Repaint()
            {
                foreach (Control one in zones.Controls) { Row row = one as Row; if (row != null) row.Chosen = row.Colour == selected; one.Invalidate(); }
                picker.ShowRecent();
            }

            // A part clicked on the left: the picker and the box show its colour as it is now.
            // Null is no part at all: nothing named above the picker. She always opens on a part, so only a check gets here.
            private void Choose(string colour)
            {
                if (colour == null) { selected = null; pickedName.Text = ""; alsoNote.Text = ""; Says("", false); return; }
                // A note belongs to the part it was about: picking another clears it.
                if (colour != selected) Says("", false);
                selected = colour;
                string id, name;
                bool addon = ColourPresets.Addon(colour, out id, out name);
                pickedName.Text = ColourPresets.NameOf(colour) + (ColourPresets.TheirsOf(colour).HasValue ? "" : addon ? " (the mod's own)" : " (hers)");
                int zone = 0;
                for (int z = 0; z < ColourPresets.Parts.Length; z++) if (Array.Exists(ColourPresets.Parts[z], part => part.Colour == colour)) { zone = z; break; }
                alsoNote.Text = addon ? "" : ColourPresets.Also(zone, colour) ?? "";
                picker.Show(ColourPresets.ShownOf(colour));
                hexBox.Box.Text = ColourPresets.Hex(ColourPresets.ShownOf(colour));
                Repaint();
            }

            // The colour typed, on the part picked, as a pick would put it; one that isn't #rrggbb changes nothing.
            private void SetHex()
            {
                Color? typed = ColourPresets.Parsed(hexBox.Box.Text);
                if (selected == null) { Says("Click a part on the left first.", true); return; }
                if (!typed.HasValue) { Says("That isn't a colour. Type it as #rrggbb, like #ff2e88.", true); return; }
                picker.Show(typed.Value);
                Commit(typed.Value, true);
            }

            // The part picked back to her own colour, or the mod's own.
            private void BackToHers()
            {
                if (selected == null) { Says("Click a part on the left first.", true); return; }
                if (!ColourPresets.SetOne(selected, null)) { Says("Couldn't put that back.", true); return; }
                Frame.capri.RepaintColours();
                Keep();
                Says(ColourPresets.NameOf(selected) + " is hers again.", false);
                Choose(selected);
            }

            // Set on the part selected and kept in the preset picked, and every window of hers repainted from it. Through the
            // picker or the box it goes to the front of the last used; from the last used it stays where it is.
            private void Commit(Color colour, bool toRecent)
            {
                if (selected == null) return;
                if (!ColourPresets.SetOne(selected, colour)) { Says("Couldn't save that colour.", true); return; }
                Frame.capri.RepaintColours();
                if (toRecent) ColourPresets.Push(picker.Recent, colour);
                Keep();
                Says(ColourPresets.NameOf(selected) + " is " + ColourPresets.Hex(colour) + ".", false);
                Choose(selected);
            }

            // The preset picked follows what is set now.
            private void Keep()
            {
                int at = ColourPresets.Find(presets, picked);
                if (at >= 0) presets[at] = new KeyValuePair<string, Dictionary<string, Color>>(picked, ColourPresets.Kept(presets[at].Value));
                Save();
            }

            private void ShowPresets()
            {
                foreach (ToolStripItem old in new List<ToolStripItem>(ItemsOf(presetMenu))) { presetMenu.Items.Remove(old); old.Dispose(); }
                foreach (KeyValuePair<string, Dictionary<string, Color>> preset in presets)
                {
                    string name = preset.Key;
                    presetMenu.Items.Add(Choice(name, name == picked, delegate { PickPreset(name); }));
                }
                presetMenu.Show(presetKey, new Point(0, presetKey.Height));
            }

            private static List<ToolStripItem> ItemsOf(ToolStrip strip)
            {
                List<ToolStripItem> all = new List<ToolStripItem>();
                foreach (ToolStripItem one in strip.Items) all.Add(one);
                return all;
            }

            // Every colour of layout.md set to the preset's in one write, any it doesn't name put back to hers; each
            // installed add-on the same in its own file. A missing add-on is passed over, its preset colours kept for
            // when it returns.
            private void PickPreset(string name)
            {
                int at = ColourPresets.Find(presets, name);
                if (at < 0) return;
                Dictionary<string, Color> held = presets[at].Value;
                Dictionary<string, Color?> all = new Dictionary<string, Color?>(StringComparer.OrdinalIgnoreCase);
                foreach (string one in Neon.Names) { Color colour; all[one] = held.TryGetValue(one, out colour) ? colour : (Color?)null; }
                if (!CapriDesktop.Layout.Set(all)) { Says("Couldn't save to layout.md.", true); return; }
                foreach (KeyValuePair<string, string> mod in AddonColours.Trees())
                {
                    Dictionary<string, Color?> theirs = new Dictionary<string, Color?>(StringComparer.Ordinal);
                    foreach (string one in AddonColours.Names(mod.Key)) theirs[one] = null;
                    foreach (KeyValuePair<string, Color> entry in held)
                    {
                        string id, colourName;
                        if (ColourPresets.Addon(entry.Key, out id, out colourName) && string.Equals(id, mod.Key, StringComparison.OrdinalIgnoreCase)) theirs[colourName] = entry.Value;
                    }
                    if (theirs.Count > 0) AddonColours.Set(mod.Key, theirs);
                }
                Frame.capri.RepaintColours();
                picked = presets[at].Key;
                presetKey.Text = picked;
                Save();
                Says(picked + " is on.", false);
                Choose(selected);
            }

            private void NewPreset()
            {
                string name = ColourPresets.NewName(presets);
                presets.Add(new KeyValuePair<string, Dictionary<string, Color>>(name, ColourPresets.Current()));
                picked = name;
                presetKey.Text = name;
                Save();
                Says(name + " made from the colours you have now.", false);
            }

            // A part's swatch clicked: that part chosen, and its colour put first in the last used, as a pick would.
            private void SwatchTaken(string colour)
            {
                Choose(colour);
                ColourPresets.Push(picker.Recent, Neon.Shown(colour));
                picker.ShowRecent();
                Save();
                Says(ColourPresets.NameOf(colour) + "'s colour is in last used.", false);
            }

            // The screen's colour at a point, swapped by the checks so no real screen is read there.
            internal static Func<Point, Color> ReadPixel = ScreenPixel;

            private static Color ScreenPixel(Point at)
            {
                using (Bitmap one = new Bitmap(1, 1, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (Graphics grab = Graphics.FromImage(one)) grab.CopyFromScreen(at, Point.Empty, new Size(1, 1));
                    return Color.FromArgb(255, one.GetPixel(0, 0));
                }
            }

            private Color dropperWas;
            private DropperVeil veil;
            // What ended the last pick, for the checks.
            private string endedBy;

            // Where the dropper's veil lies: every screen. Checks keep it to the screen they run on.
            internal static Func<Rectangle> VeilArea = delegate { return SystemInformation.VirtualScreen; };

            // The dropper: a veil over the screens takes the mouse until a click takes a colour; right click, Esc or
            // another window in front cancels. Not mouse capture, which Windows drops over other programs' windows.
            internal void DropperStart()
            {
                if (dropper.Picking) return;
                if (selected == null) { Says("Click a part on the left first, then the dropper.", true); return; }
                dropperWas = picker.Preview.Colour;
                dropper.Picking = true;
                Says("Click anywhere on the screen to take its colour. Esc cancels.", false);
                veil = new DropperVeil(VeilArea());
                veil.MouseMove += delegate { DropperAt(Cursor.Position); };
                veil.MouseDown += delegate(object sender, MouseEventArgs e)
                {
                    if (!dropper.Picking || !dropper.Armed) return;
                    if (e.Button == MouseButtons.Left) DropperTake(Cursor.Position); else { endedBy = "right click"; DropperCancel(); }
                };
                veil.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { e.Handled = true; endedBy = "Esc"; DropperCancel(); } };
                // Another program coming to the front cancels it. One of hers rising above it, such as her own window, does not:
                // the veil takes the front back.
                veil.Deactivate += delegate
                {
                    if (!dropper.Picking) return;
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (!dropper.Picking || veil == null || veil.IsDisposed) return;
                        if (OursInFront()) { veil.Activate(); return; }
                        endedBy = "another program came to the front";
                        DropperCancel();
                    });
                };
                endedBy = null;
                veil.Show(this);
                veil.Activate();
                DropperAt(Cursor.Position);
                dropper.Invalidate();
            }

            private static bool OursInFront()
            {
                IntPtr front = Native.GetForegroundWindow();
                if (front == IntPtr.Zero) return true;
                int id;
                Native.GetWindowThreadProcessId(front, out id);
                return id == System.Diagnostics.Process.GetCurrentProcess().Id;
            }

            internal bool DropperPicking { get { return dropper.Picking && veil != null && veil.Visible; } }

            // How fast it follows the mouse, for the checks: the last timing, and the most a move may take on average.
            internal static string DropperTiming;
            internal const double DropperMost = 30;

            internal void DropperAt(Point screen)
            {
                if (!dropper.Picking) return;
                try { picker.Preview.Colour = ReadPixel(screen); }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }

            internal void DropperTake(Point screen)
            {
                if (!dropper.Picking) return;
                Color colour;
                // The veil is nearly clear, so it is put away first and the pixel read without it. Picking is off while it
                // goes, so its going is not taken for a cancel.
                dropper.Picking = false;
                if (veil != null) { veil.Hide(); Update(); System.Threading.Thread.Sleep(60); }
                try { colour = Color.FromArgb(255, ReadPixel(screen)); }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); dropper.Picking = true; DropperCancel(); return; }
                DropperEnd();
                picker.Show(colour);
                Commit(colour, true);
            }

            internal void DropperCancel()
            {
                if (!dropper.Picking) return;
                DropperEnd();
                picker.Preview.Colour = dropperWas;
                Says("", false);
            }

            private void DropperEnd()
            {
                dropper.Picking = false;
                dropper.Invalidate();
                DropperVeil was = veil;
                veil = null;
                if (was != null) { was.Close(); was.Dispose(); }
            }

            protected override void OnFormClosed(FormClosedEventArgs e) { DropperCancel(); base.OnFormClosed(e); }

            // Over the screens while the dropper is on: one step off clear, so everything shows through and is read as it
            // is, while a click anywhere, another program's window included, lands here and not on what is under it.
            private sealed class DropperVeil : Form
            {
                internal DropperVeil(Rectangle area)
                {
                    FormBorderStyle = FormBorderStyle.None;
                    ShowInTaskbar = false;
                    StartPosition = FormStartPosition.Manual;
                    TopMost = true;
                    KeyPreview = true;
                    BackColor = Color.Black;
                    Opacity = 1 / 255.0;
                    Cursor = Cursors.Cross;
                    Bounds = area;
                }
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape && dropper.Picking) { DropperCancel(); e.Handled = true; e.SuppressKeyPress = true; return; }
                if (e.KeyCode == Keys.Escape && presetMenu.Visible) { presetMenu.Close(); e.Handled = true; return; }
                base.OnKeyDown(e);
            }

            // What it shows: the preset, each zone and its parts, then the last used.
            internal string Snapshot
            {
                get
                {
                    StringBuilder all = new StringBuilder(presetKey.Text).Append(" | ");
                    foreach (Control control in zones.Controls)
                    {
                        Row row = control as Row;
                        if (row != null) all.Append(row.Text).Append(" ").Append(ColourPresets.Hex(ColourPresets.ShownOf(row.Colour))).Append(row.Chosen ? " (selected)" : "").Append(" | ");
                        else all.Append(control.AccessibleName).Append(" | ");
                    }
                    return all.Append("last used: ").Append(ColourPresets.WriteRecent(picker.Recent)).ToString();
                }
            }

            private string ListCrowded()
            {
                List<Control> shown = new List<Control>();
                foreach (Control control in zones.Controls) if (control.Visible) shown.Add(control);
                int inside = zones.ClientSize.Width;
                for (int i = 0; i < shown.Count; i++)
                {
                    Control a = shown[i];
                    if (a.Right > inside) return "pokes out: " + a.Text + " " + a.Bounds + " in " + inside;
                    Row row = a as Row;
                    if (row != null && row.Crowded() != null) return row.Crowded();
                    for (int j = i + 1; j < shown.Count; j++)
                        if (a.Bounds.IntersectsWith(shown[j].Bounds)) return "overlap: " + a.Text + " / " + shown[j].Text;
                }
                return null;
            }

            // Null when the list's first row sits at the top of what scrolls, and it isn't scrolled past its end.
            private string ZonesGap(string where)
            {
                int first = int.MaxValue, last = 0;
                foreach (Control one in zones.Controls) { first = Math.Min(first, one.Top - zones.AutoScrollPosition.Y); last = Math.Max(last, one.Bottom - zones.AutoScrollPosition.Y); }
                int scrolled = -zones.AutoScrollPosition.Y, most = Math.Max(0, last - zones.ClientSize.Height);
                if (first != 0 || scrolled > most) return where + ": the first fold-down is " + first + " px down, scrolled " + scrolled + " of at most " + most;
                return null;
            }

            private string Fits(string where)
            {
                string wrong = Crowded() ?? ListCrowded();
                return wrong == null ? null : where + ": " + wrong;
            }

            private static string Off(string name, Color? wanted)
            {
                Color? theirs = Neon.TheirOwn(name);
                if (theirs.HasValue != wanted.HasValue || (wanted.HasValue && theirs.Value.ToArgb() != wanted.Value.ToArgb()))
                    return name + " is " + (theirs.HasValue ? ColourPresets.Hex(theirs.Value) : "hers") + ", not " + (wanted.HasValue ? ColourPresets.Hex(wanted.Value) : "hers");
                return null;
            }

            [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern void mouse_event(int flags, int dx, int dy, int data, IntPtr extra);

            [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
            private struct WindowMessage { public IntPtr Window; public int Message; public IntPtr W; public IntPtr L; public int Time; public int X, Y; }

            [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool PeekMessage(out WindowMessage message, IntPtr window, uint first, uint last, uint remove);
            [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool TranslateMessage(ref WindowMessage message);
            [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref WindowMessage message);

            // A smoke run filters every mouse message out of its own pump, so input from whoever is at the PC never reaches a
            // check. The real-mouse check pumps for itself, only while its own clicks land.
            private static void PumpOwn(int rounds)
            {
                for (int i = 0; i < rounds; i++)
                {
                    WindowMessage message;
                    while (PeekMessage(out message, IntPtr.Zero, 0, 0, 1)) { TranslateMessage(ref message); DispatchMessage(ref message); }
                    System.Threading.Thread.Sleep(15);
                }
            }

            // The dropper with the real mouse: the key starts it once a part is chosen, a click on the screen takes
            // that colour, a right click cancels. It moves the real cursor, so it runs only with CAPRI_REAL_INPUT=1.
            internal const string RealInputVariable = "CAPRI_REAL_INPUT";
            internal static bool RealInputOn { get { return Environment.GetEnvironmentVariable(RealInputVariable) == "1"; } }
            // However it goes, the cursor is let go within this.
            private const int RealInputMost = 6000;

            private string RealDropper(string picture)
            {
                if (!RealInputOn) return null;
                System.Diagnostics.Stopwatch held = System.Diagnostics.Stopwatch.StartNew();
                Point cursorWas = Cursor.Position;
                string chosenWas = selected;
                // One press: down and up close together, so a hand on the mouse at the same moment has little room to move it
                // between them. Sent only where the target is the window under the cursor.
                Func<Point, Control, int, bool> press = delegate(Point at, Control target, int downFlag)
                {
                    if (target == null || target.IsDisposed || held.ElapsedMilliseconds > RealInputMost) return false;
                    Cursor.Position = at;
                    PumpOwn(4);
                    if (target.IsDisposed || Native.WindowFromPoint(new Native.Point(at.X, at.Y)) != target.Handle) return false;
                    mouse_event(downFlag, 0, 0, 0, IntPtr.Zero);
                    PumpOwn(1);
                    mouse_event(downFlag << 1, 0, 0, 0, IntPtr.Zero);
                    PumpOwn(8);
                    return true;
                };
                // Up to three tries until what the press should do has happened; someone else's mouse can spoil one.
                Func<Point, Control, int, Func<bool>, bool> pressUntil = delegate(Point at, Control target, int downFlag, Func<bool> done)
                {
                    for (int attempt = 0; attempt < 3 && held.ElapsedMilliseconds <= RealInputMost; attempt++)
                    {
                        if (press(at, target, downFlag) && done()) return true;
                        if (done()) return true;
                        System.Threading.Thread.Sleep(250);
                    }
                    return false;
                };
                // The veil and the clicks stay on the test screen, never the main one: the window goes there first and
                // back after.
                Point placedWas = Location;
                Rectangle test = Displays.Home.WorkingArea;
                Location = new Point(test.Left + Math.Max(0, (test.Width - Width) / 2), test.Top + Math.Max(0, (test.Height - Height) / 2));
                if (Screen.FromControl(this).Primary && Screen.AllScreens.Length > 1) { Location = placedWas; return "The colour window was on the main screen, so the real mouse wasn't used."; }
                try
                {
                    Activate();
                    PumpOwn(10);
                    Point key = dropper.PointToScreen(new Point(dropper.Width / 2, dropper.Height / 2));
                    // A third of the way down the hue bar: a colour of its own that nothing in the check changes.
                    Point spot = picker.HueBar.PointToScreen(new Point(picker.HueBar.Width / 2, picker.HueBar.Height / 3));
                    const string ChooseFirst = "Click a part on the left first, then the dropper.";

                    Choose(null);
                    if (!pressUntil(key, dropper, 0x0002, delegate { return notice.Text == ChooseFirst; }) || dropper.Picking)
                        return "A real click on the dropper with no part chosen didn't say to choose one: " + notice.Text;
                    if (picture != null) Picture(picture.Replace(".png", "-dropper-no-part.png"));

                    Choose("hot");
                    if (!pressUntil(key, dropper, 0x0002, delegate { return DropperPicking; })) return "A real click on the dropper key didn't start it: " + (endedBy ?? "never started");
                    Cursor.Position = spot;
                    PumpOwn(10);
                    if (picture != null) Picture(picture.Replace(".png", "-dropper-picking.png"));
                    if (!DropperPicking || picker.Preview.Colour.ToArgb() == dropperWas.ToArgb())
                        return "The dropper didn't show the colour under the cursor while picking: " + (endedBy ?? ColourPresets.Hex(picker.Preview.Colour));
                    System.Threading.Thread.Sleep(200);
                    // Read now, with the part chosen and the veil up: one step off clear, so within a step of what is under it. The
                    // screen is shared, so something else drawn over the spot for a moment spoils a try: up to three.
                    Color under = Color.Empty;
                    Color? taken = null;
                    string missed = null;
                    for (int attempt = 0; attempt < 3 && held.ElapsedMilliseconds <= RealInputMost; attempt++)
                    {
                        if (attempt > 0 && !pressUntil(key, dropper, 0x0002, delegate { return DropperPicking; })) return "A real click on the dropper key didn't start it again: " + (endedBy ?? "never started");
                        if (attempt > 0) System.Threading.Thread.Sleep(200);
                        under = ScreenPixel(spot);
                        if (!pressUntil(spot, veil, 0x0002, delegate { return !DropperPicking; })) return "A real click with the dropper on didn't take a colour: " + (endedBy ?? "still picking");
                        taken = Neon.TheirOwn("hot");
                        missed = !taken.HasValue || Math.Abs(taken.Value.R - under.R) > 2 || Math.Abs(taken.Value.G - under.G) > 2 || Math.Abs(taken.Value.B - under.B) > 2
                            ? (taken.HasValue ? ColourPresets.Hex(taken.Value) : "none") + ", not " + ColourPresets.Hex(under) : null;
                        if (missed == null) break;
                    }
                    if (missed != null) return "A real click with the dropper on didn't take the colour under it" + (held.ElapsedMilliseconds > RealInputMost ? ", and gave up after " + RealInputMost / 1000 + " s" : ", three times") + ": " + missed;

                    if (!pressUntil(key, dropper, 0x0002, delegate { return DropperPicking; })) return "A second real click on the dropper key didn't start it: " + (endedBy ?? "never started");
                    System.Threading.Thread.Sleep(200);
                    if (!pressUntil(spot, veil, 0x0008, delegate { return !DropperPicking; }) || Neon.TheirOwn("hot") != taken)
                        return "A real right click didn't cancel the dropper with nothing changed: " + (endedBy ?? "still picking");
                    return null;
                }
                finally
                {
                    DropperCancel();
                    Choose(chosenWas);
                    Cursor.Position = cursorWas;
                    Location = placedWas;
                }
            }

            // A colour set as the picker sets it: shown, then let go.
            private void Typed(string hex)
            {
                picker.Show(ColorTranslator.FromHtml(hex));
                Commit(picker.Colour, true);
            }

            // A part put back to her colour, for the checks.
            private void PutBack()
            {
                CapriDesktop.Layout.Set(selected, null);
                Frame.capri.RepaintColours();
                Keep();
                Choose(selected);
            }

            private static Control Coloured(Control within, Color colour)
            {
                foreach (Control child in within.Controls)
                {
                    if (child.ForeColor.ToArgb() == colour.ToArgb()) return child;
                    Control deeper = Coloured(child, colour);
                    if (deeper != null) return deeper;
                }
                return null;
            }

            // Opened with amber set and nothing saved, Preset 1 holds amber; every layout.md colour is under a part;
            // picker, box, last used and presets each set colours and repaint. A fit problem is noted and the rest
            // still driven.
            private readonly List<string> unfit = new List<string>();

            private void Fitting(string where) { string problem = Fits(where); if (problem != null) unfit.Add(problem); }

            internal string Drive(string picture, Color amber)
            {
                unfit.Clear();
                string wrong = null;
                Fitting("first");
                if (picture != null) Picture(picture);
                if (presets.Count != 3 || presets[0].Key != "Preset 1" || !presets[0].Value.ContainsKey("amber") || presets[0].Value["amber"].ToArgb() != amber.ToArgb() || presets[1].Value.Count != 0 || presets[2].Value.Count != 0 || picked != "Preset 1")
                    return "Preset 1 didn't take the colours set, or Presets 2 and 3 weren't left as hers: " + ColourPresets.Write(presets);
                if (selected != "bubble outline" || Snapshot.IndexOf("Bubble outline", StringComparison.Ordinal) < 0 || Snapshot.IndexOf("Panel background", StringComparison.Ordinal) >= 0)
                    return "It didn't open on Speech bubble with its first part selected: " + Snapshot;
                foreach (string zone in ColourPresets.Zones) open.Add(zone);
                FillZones();
                List<string> listed = new List<string>();
                foreach (Control control in zones.Controls) { Row row = control as Row; if (row != null) listed.Add(row.Colour); }
                foreach (string name in Neon.Names) if (!listed.Contains(name)) return name + " isn't under any part.";
                if (Snapshot.IndexOf("Song title", StringComparison.Ordinal) < 0) return "Player's parts aren't listed: " + Snapshot;
                foreach (Control control in zones.Controls)
                {
                    Row row = control as Row;
                    if (row != null && row.Colour == "sheet text" && (row.Also == null || row.Also.IndexOf("Song title (Player)", StringComparison.Ordinal) < 0 && row.Also.IndexOf("Notes text (Mainframe)", StringComparison.Ordinal) < 0))
                        return "A shared colour doesn't say what else it colours: " + row.Text + " / " + row.Also;
                }
                Fitting("all open");
                // Scrolled down and folded up again, in a few orders: the first fold-down stays at the top with no gap above it,
                // and nothing is left scrolled past the end of what's there.
                foreach (string[] closing in new[] { new[] { "Mainframe" }, new[] { "Speech bubble", "Player" }, ColourPresets.Zones })
                {
                    zones.AutoScrollPosition = new Point(0, 100000);
                    foreach (string zone in closing) open.Remove(zone);
                    FillZones();
                    wrong = ZonesGap("scrolled, then " + string.Join(" and ", closing) + " folded");
                    if (wrong != null) break;
                    foreach (string zone in closing) open.Add(zone);
                    FillZones();
                    wrong = ZonesGap("opened again");
                    if (wrong != null) break;
                }
                if (picture != null) Picture(picture.Replace(".png", "-folds.png"));
                if (wrong != null) return wrong;

                // A part's swatch clicked: the part chosen, its colour first in last used, and once only when clicked again.
                foreach (string zone in ColourPresets.Zones) open.Add(zone);
                FillZones();
                Row titleRow = null;
                foreach (Control control in zones.Controls) { Row row = control as Row; if (row != null && row.Colour == "bubble title") titleRow = row; }
                if (titleRow == null) return "No row for Bubble title.";
                picker.Recent.Clear();
                // A colour typed in the box is set on the part, one that isn't a colour changes nothing, and Hers puts it back.
                Choose("bubble title");
                hexBox.Box.Text = "#123456";
                SetHex();
                Color typedColour = Color.FromArgb(255, 0x12, 0x34, 0x56);
                string typedWrong = Off("bubble title", typedColour);
                hexBox.Box.Text = "#zz";
                SetHex();
                string badWrong = Off("bubble title", typedColour);
                BackToHers();
                string hersWrong = Off("bubble title", null);
                if (typedWrong != null || badWrong != null || hersWrong != null) return "The hex box or Hers went wrong: " + (typedWrong ?? badWrong ?? hersWrong);
                picker.Recent.Clear();
                // Its minimise key puts it away as it is, and showing it again brings back the same window.
                tuckKey.PerformClick();
                bool tucked = !Visible && !IsDisposed;
                Show(Frame);
                if (!tucked || !Visible) return "Its minimise key didn't put it away to come back as it was: hidden " + tucked + ", back " + Visible;
                titleRow.TakeSwatch();
                titleRow.TakeSwatch();
                Color titleShown = Neon.Shown("bubble title");
                if (selected != "bubble title" || picker.Recent.Count != 1 || picker.Recent[0].ToArgb() != Color.FromArgb(255, titleShown).ToArgb())
                    return "A swatch click didn't choose the part and put its colour in last used once: " + selected + ", " + ColourPresets.WriteRecent(picker.Recent);
                // The dropper: the pixel under the cursor shows as it moves, a click puts it on the part and first in last used,
                // and Esc cancels with nothing changed.
                Func<Point, Color> readWas = ReadPixel;
                Func<Rectangle> veilWas = VeilArea;
                Color sky = Color.FromArgb(255, 70, 130, 180), mud = Color.FromArgb(255, 90, 70, 50);
                try
                {
                    VeilArea = delegate { return Screen.FromControl(this).Bounds; };
                    ReadPixel = delegate(Point at) { return at.X < 100 ? sky : mud; };
                    Choose("hot");
                    DropperStart();
                    DropperAt(new Point(50, 50));
                    bool previewed = picker.Preview.Colour.ToArgb() == sky.ToArgb();
                    DropperTake(new Point(40, 40));
                    wrong = Off("hot", sky);
                    if (wrong != null || !previewed || dropper.Picking || picker.Recent[0].ToArgb() != sky.ToArgb()) return "The dropper didn't put the colour under the cursor on the part and in last used: " + (wrong ?? ColourPresets.WriteRecent(picker.Recent) + ", previewed " + previewed);
                    if (picture != null) { DropperStart(); DropperAt(new Point(500, 50)); Picture(picture.Replace(".png", "-dropper.png")); DropperCancel(); }
                    DropperStart();
                    DropperAt(new Point(500, 50));
                    OnKeyDown(new KeyEventArgs(Keys.Escape));
                    if (dropper.Picking || Off("hot", sky) != null || picker.Preview.Colour.ToArgb() != sky.ToArgb()) return "Esc didn't cancel the dropper with nothing changed.";
                    ReadPixel = readWas;
                    // Following the mouse: 50 moves over this screen, each
                    // read from the real screen with its preview painted, timed, and the read alone. The moves are driven here.
                    Rectangle on = Screen.FromControl(this).Bounds;
                    double total = 0, worst = 0, reading = 0;
                    DropperStart();
                    for (int move = 0; move < 50; move++)
                    {
                        Point at = new Point(on.Left + 20 + move * 37 % Math.Max(1, on.Width - 40), on.Top + 20 + move * 23 % Math.Max(1, on.Height - 40));
                        System.Diagnostics.Stopwatch each = System.Diagnostics.Stopwatch.StartNew();
                        DropperAt(at);
                        picker.Preview.Update();
                        double took = each.Elapsed.TotalMilliseconds;
                        total += took;
                        worst = Math.Max(worst, took);
                        each.Restart();
                        ReadPixel(at);
                        reading += each.Elapsed.TotalMilliseconds;
                    }
                    DropperCancel();
                    DropperTiming = "50 moves, " + (total / 50).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " ms each on average, " + worst.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                        + " ms at most, the pixel read alone " + (reading / 50).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " ms";
                    Brain.Log("colours", -1, "The dropper following the mouse: " + DropperTiming + ".");
                    if (total / 50 > DropperMost) return "The dropper is slow to follow the mouse: " + DropperTiming + ", over " + DropperMost + " ms.";
                    wrong = RealDropper(picture);
                    if (wrong != null) return wrong;
                }
                finally { ReadPixel = readWas; VeilArea = veilWas; DropperCancel(); }
                PutBack();
                picker.Recent.Clear();
                if (wrong != null) return wrong;

                // The global hook: a colour picked reaches the Mainframe's own controls, and so does a second change to it.
                Control rosy = Coloured(Frame, Neon.Shown("rose"));
                if (rosy == null) return "Nothing on the Mainframe is drawn in Titles and setting names' colour.";
                Color mint = Color.FromArgb(255, 0, 255, 128), violet = Color.FromArgb(255, 128, 0, 255);
                Choose("rose");
                Typed(ColourPresets.Hex(mint));
                if (rosy.ForeColor.ToArgb() != mint.ToArgb()) return "Picking Titles and setting names didn't repaint " + rosy.Text + " on the Mainframe: " + rosy.ForeColor;
                Typed(ColourPresets.Hex(violet));
                if (rosy.ForeColor.ToArgb() != violet.ToArgb()) return "A second colour for Titles and setting names didn't repaint " + rosy.Text + ": " + rosy.ForeColor;
                PutBack();
                if (rosy.ForeColor.ToArgb() != Neon.Shown("rose").ToArgb()) return "Putting it back didn't put " + rosy.Text + " back to her colour: " + rosy.ForeColor;
                if (picker.Recent.Count != 2) return "The two colours picked aren't in last used: " + ColourPresets.WriteRecent(picker.Recent);
                picker.Recent.Clear();

                // A new look shows at once, here too, with a colour they set kept over it.
                Color slate = Color.FromArgb(255, 16, 32, 48);
                Choose("ground");
                Typed(ColourPresets.Hex(slate));
                if (BackColor.ToArgb() != slate.ToArgb()) return "Panel background didn't repaint this window: " + BackColor;
                int lookWas = Frame.frameColours.Value, other = lookWas == 1 ? 2 : 1;
                try
                {
                    Frame.capri.SetMainframeColours(other);
                    int band = Array.IndexOf(Neon.Names, "band");
                    if (Neon.Shown("ground").ToArgb() != slate.ToArgb() || BackColor.ToArgb() != slate.ToArgb()) return "A new look didn't keep Panel background as they set it: " + BackColor;
                    if (Neon.Shown("band").ToArgb() != Neon.LookColours(other)[band].ToArgb()) return "A new look didn't show under the colours they left as hers.";
                    if (pickedName.Text != "Panel background" || picker.Colour.ToArgb() != slate.ToArgb()) return "The picker didn't follow the new look: " + pickedName.Text;
                }
                finally { Frame.capri.SetMainframeColours(lookWas); }
                PutBack();
                picker.Recent.Clear();

                Color green = Color.FromArgb(255, 18, 200, 90), red = Color.FromArgb(255, 255, 32, 64), blue = Color.FromArgb(255, 0, 0, 255);
                Choose("hot");
                picker.Show(green);
                Commit(picker.Colour, true);
                wrong = Off("hot", green);
                if (wrong != null || picker.Recent.Count != 1 || picker.Recent[0].ToArgb() != green.ToArgb()) return "The picker didn't set hot and put it in last used: " + (wrong ?? ColourPresets.WriteRecent(picker.Recent));
                if (!System.Text.RegularExpressions.Regex.IsMatch(System.IO.File.ReadAllText(CapriDesktop.Layout.Kept), @"(?m)^hot:\s+#12c85a\s+# hers: ")) return "hot didn't reach layout.md.";
                Choose("cyan");
                Typed("#ff2040");
                wrong = Off("cyan", red);
                if (wrong != null || ColourPresets.WriteRecent(picker.Recent) != "#ff2040,#12c85a") return "The box didn't set cyan and put it first: " + (wrong ?? ColourPresets.WriteRecent(picker.Recent));
                picker.UseRecent(1);
                wrong = Off("cyan", green);
                if (wrong != null || ColourPresets.WriteRecent(picker.Recent) != "#ff2040,#12c85a") return "A last used colour didn't go on cyan, or it moved: " + (wrong ?? ColourPresets.WriteRecent(picker.Recent));
                Choose("amber");
                for (int i = 0; i < 12; i++) Typed(ColourPresets.Hex(Color.FromArgb(i * 20, 100, 50)));
                if (picker.Recent.Count != ColourPresets.Recent || picker.Recent[0].ToArgb() != Color.FromArgb(255, 220, 100, 50).ToArgb() || picker.Recent.Exists(one => one.ToArgb() == green.ToArgb()))
                    return "Last used didn't keep the newest twelve: " + ColourPresets.WriteRecent(picker.Recent);
                Choose("cyan");
                PutBack();
                wrong = Off("cyan", null);
                if (wrong != null || !System.Text.RegularExpressions.Regex.IsMatch(System.IO.File.ReadAllText(CapriDesktop.Layout.Kept), @"(?m)^cyan:\s+# hers: ")) return "Putting it back didn't put cyan back: " + wrong;

                // An add-on's colours: under a fold-down of its own below her zones, set in its own file, not in layout.md.
                string glow = ColourPresets.AddonKey("tint-test", "Glow");
                open.Add(AddonsZone);
                open.Add("addon:tint-test");
                FillZones();
                string shown = Snapshot;
                int playerAt = shown.IndexOf("Player, open", StringComparison.Ordinal), tintAt = shown.IndexOf("Tint Test, add-on colours, open", StringComparison.Ordinal);
                int addonsAt = shown.IndexOf("Add-ons, open", StringComparison.Ordinal);
                if (playerAt < 0 || addonsAt < playerAt || tintAt < addonsAt || shown.IndexOf("Glow #", tintAt, StringComparison.Ordinal) < 0) return "The add-on's colours aren't under ADD-ONS in a fold-down of its own below hers: " + shown;
                open.Remove(AddonsZone);
                FillZones();
                if (Snapshot.IndexOf("Add-ons, folded", StringComparison.Ordinal) < 0 || Snapshot.IndexOf("Tint Test", StringComparison.Ordinal) >= 0) return "Folding ADD-ONS didn't fold the add-ons under it: " + Snapshot;
                open.Add(AddonsZone);
                FillZones();
                Fitting("add-on open");
                Says("Something about another part.", false);
                Choose(glow);
                if (notice.Text.Length > 0) return "A note about another part stayed up when an add-on colour was picked: " + notice.Text;
                if (picture != null)
                {
                    // Scrolled to the add-on's fold-down, with its colour chosen.
                    Control last = null;
                    foreach (Control row in zones.Controls) if (last == null || row.Bottom > last.Bottom) last = row;
                    if (last != null) zones.ScrollControlIntoView(last);
                    zonesBar.Invalidate();
                    Application.DoEvents();
                    Picture(picture.Replace(".png", "-addon.png"));
                }
                if (pickedName.Text != "Glow (the mod's own)" || picker.Colour.ToArgb() != Neon.Tone(200, 30, 60).ToArgb()) return "An add-on colour didn't show as the mod's own: " + pickedName.Text;
                Typed("#123456");
                string layoutText = System.IO.File.ReadAllText(CapriDesktop.Layout.Kept), addonText = System.IO.File.ReadAllText(System.IO.Path.Combine(AddonColours.FolderOf("tint-test"), AddonColours.FileName));
                Color? glowSet = AddonColours.TheirOwn("tint-test", "Glow");
                if (!glowSet.HasValue || glowSet.Value.ToArgb() != Color.FromArgb(0x12, 0x34, 0x56).ToArgb() || layoutText.IndexOf("#123456", StringComparison.Ordinal) >= 0 || !System.Text.RegularExpressions.Regex.IsMatch(addonText, @"(?m)^Glow:\s+#123456\s+# usual: #c81e3c"))
                    return "An add-on colour wasn't kept in its own colours.md apart from layout.md: " + addonText;
                if (!presets[ColourPresets.Find(presets, picked)].Value.ContainsKey(glow)) return "The preset picked didn't keep the add-on's colour.";

                NewPreset();
                if (picked != "Preset 4" || presets.Count != 4) return "+ didn't make Preset 4: " + ColourPresets.Write(presets);
                Choose("hot");
                Typed("#0000ff");
                PickPreset("Preset 1");
                wrong = Off("hot", green) ?? Off("amber", Color.FromArgb(220, 100, 50));
                if (wrong != null || presetKey.Text != "Preset 1") return "Picking Preset 1 didn't bring its colours back: " + wrong;
                PickPreset("Preset 2");
                if (ColourPresets.Current().Count != 0) return "Picking Preset 2 didn't leave every colour as hers: " + ColourPresets.Current().Count + " set.";
                PickPreset("Preset 4");
                wrong = Off("hot", blue);
                if (wrong != null) return "Picking Preset 4 didn't bring its colours back: " + wrong;
                glowSet = AddonColours.TheirOwn("tint-test", "Glow");
                if (!glowSet.HasValue || glowSet.Value.ToArgb() != Color.FromArgb(0x12, 0x34, 0x56).ToArgb()) return "Picking Preset 4 didn't bring the add-on's colour back.";

                // The add-on gone: its fold-down goes, a preset passes over it without a word, and keeps its colour for later.
                Func<List<KeyValuePair<string, string>>> installed = AddonColours.Installed;
                AddonColours.Installed = delegate { return new List<KeyValuePair<string, string>>(); };
                try
                {
                    FillZones();
                    if (Snapshot.IndexOf("Tint Test", StringComparison.Ordinal) >= 0) return "A removed add-on's fold-down stayed.";
                    PickPreset("Preset 1");
                    if (notice.ForeColor == Rose) return "Picking a preset with a removed add-on said something went wrong: " + notice.Text;
                    Choose("hot");
                    Typed("#0000ff");
                    if (!presets[ColourPresets.Find(presets, "Preset 1")].Value.ContainsKey(glow)) return "A preset lost a removed add-on's colour.";
                }
                finally { AddonColours.Installed = installed; }
                PickPreset("Preset 4");
                string saved = Frame.capri.ColourPresetsSaved ?? "";
                if (Frame.capri.ColourPresetPicked != "Preset 4" || saved.IndexOf("Preset 4=", StringComparison.Ordinal) < 0 || saved.IndexOf("hot:#0000ff", StringComparison.Ordinal) < 0
                    || ColourPresets.ReadRecent(Frame.capri.ColourRecentSaved).Count != ColourPresets.Recent)
                    return "The presets and last used weren't saved in her preferences: " + saved + " / " + Frame.capri.ColourRecentSaved;
                ShowPresets();
                int items = presetMenu.Items.Count;
                presetMenu.Close();
                if (items != 4) return "The preset dropdown lists " + items + ", not 4.";
                Fitting("end");
                return unfit.Count == 0 ? null : string.Join(" / ", unfit.ToArray());
            }

            // A zone's fold-down: her section band and title, with her dropdown mark filled while it is open.
            private sealed class Fold : Control
            {
                internal bool Open;

                internal Fold()
                {
                    SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                    BackColor = Ground;
                    ForeColor = White;
                    Cursor = Cursors.Hand;
                    TabStop = true;
                    AccessibleRole = AccessibleRole.PushButton;
                }

                protected override void OnKeyDown(KeyEventArgs e)
                {
                    if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { e.SuppressKeyPress = true; OnClick(EventArgs.Empty); }
                    base.OnKeyDown(e);
                }

                protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }

                protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

                protected override void OnPaint(PaintEventArgs e)
                {
                    float s = ScaleOf(this);
                    Graphics graphics = e.Graphics;
                    using (Brush back = new SolidBrush(Ground)) graphics.FillRectangle(back, ClientRectangle);
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    DrawBand(graphics, new RectangleF(0, 0, Width, Height), s);
                    TextRenderer.DrawText(graphics, Text, Font, new Rectangle((int)(14 * s), 0, Width - (int)(44 * s), Height), White, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                    Drop(graphics, Width - 24 * s, Height / 2f, s, Cyan, Open);
                    if (Focused && ShowFocusCues) FocusMark(graphics, new RectangleF(0, 0, Width, Height), s, 0, 0, 0);
                }
            }

            // One part of her on the left: its swatch, its name in setting-name red, its hex in cyan once set, and what
            // else a shared one colours. The dropper key: one of her keys with an eyedropper, lit while picking.
            private sealed class DropperKey : Control
            {
                internal bool Picking;
                private bool hot;
                private long armedAt;

                // A click that started picking isn't also the click that takes a colour.
                internal bool Armed { get { return Environment.TickCount - armedAt > 150; } }

                internal DropperKey()
                {
                    SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                    // Two quick clicks are two clicks, not a double click that starts nothing.
                    SetStyle(ControlStyles.StandardDoubleClick, false);
                    BackColor = Ground;
                    Cursor = Cursors.Hand;
                    TabStop = true;
                    AccessibleRole = AccessibleRole.PushButton;
                }

                protected override void OnClick(EventArgs e) { armedAt = Environment.TickCount; base.OnClick(e); }

                protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }

                protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }

                protected override void OnKeyDown(KeyEventArgs e)
                {
                    if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { e.SuppressKeyPress = true; OnClick(EventArgs.Empty); }
                    base.OnKeyDown(e);
                }

                protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }

                protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

                protected override void OnPaint(PaintEventArgs e)
                {
                    float s = ScaleOf(this);
                    Graphics graphics = e.Graphics;
                    Backdrop(graphics, this, s);
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    float line = Math.Max(1f, s);
                    using (GraphicsPath plate = Cut(new RectangleF(line / 2, line / 2, Width - line, Height - line), 0, 0, 5 * s, 0))
                    {
                        using (Brush face = new SolidBrush(Picking ? Deep : hot ? Hover : Field)) graphics.FillPath(face, plate);
                        using (Pen rim = new Pen(Picking ? Cyan : hot || (Focused && ShowFocusCues) ? Hot : Edge, line)) graphics.DrawPath(rim, plate);
                    }
                    // An eyedropper on a slant: the bulb up and right, the glass down to a tip at the lower left.
                    float x = Width / 2f, y = Height / 2f;
                    using (Pen ink = new Pen(Picking ? Cyan : hot ? White : Cyan, 1.6f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    {
                        graphics.DrawLine(ink, x - 6 * s, y + 6 * s, x + 2 * s, y - 2 * s);
                        graphics.DrawLine(ink, x - 1 * s, y - 5 * s, x + 5 * s, y + 1 * s);
                    }
                    using (Brush bulb = new SolidBrush(Picking ? Cyan : hot ? White : Cyan)) graphics.FillEllipse(bulb, x + 1 * s, y - 7 * s, 6 * s, 6 * s);
                }
            }

            private sealed class Row : Control
            {
                internal readonly string Colour, Also;
                private bool chosen;

                internal Row(string shown, string colour, string also)
                {
                    SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                    Colour = colour;
                    Also = also;
                    Text = shown;
                    BackColor = Ground;
                    Cursor = Cursors.Hand;
                    TabStop = true;
                    AccessibleRole = AccessibleRole.ListItem;
                }

                internal bool Chosen { get { return chosen; } set { if (chosen != value) { chosen = value; Invalidate(); } } }

                // The colour box at the left of the row clicked, as against the rest of it.
                internal event EventHandler SwatchClicked;

                internal Rectangle SwatchBox
                {
                    get
                    {
                        float s = ScaleOf(this);
                        int box = (int)Math.Round(16 * s);
                        return Rectangle.Inflate(new Rectangle((int)Math.Round(10 * s), (NameTall - box) / 2, box, box), (int)Math.Round(3 * s), (int)Math.Round(3 * s));
                    }
                }

                internal void TakeSwatch() { if (SwatchClicked != null) SwatchClicked(this, EventArgs.Empty); }

                protected override void OnMouseUp(MouseEventArgs e)
                {
                    base.OnMouseUp(e);
                    if (e.Button == MouseButtons.Left && SwatchBox.Contains(e.Location)) TakeSwatch();
                }

                protected override void OnKeyDown(KeyEventArgs e)
                {
                    if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { e.SuppressKeyPress = true; OnClick(EventArgs.Empty); }
                    base.OnKeyDown(e);
                }

                protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }

                protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

                private int NameTall { get { return (int)Math.Round(26 * ScaleOf(this)); } }

                private int HexWide { get { return (int)Math.Round(64 * ScaleOf(this)); } }

                private Rectangle NameBox
                {
                    get
                    {
                        float s = ScaleOf(this);
                        int left = (int)Math.Round(34 * s);
                        return new Rectangle(left, 0, Width - left - HexWide - (int)Math.Round(12 * s), NameTall);
                    }
                }

                // Null when the name and its note fit their row.
                internal string Crowded()
                {
                    using (Font small = Look.Mono(8f * ScaleOf(this), FontStyle.Regular, GraphicsUnit.Point))
                    {
                        if (TextRenderer.MeasureText(Text, Font).Width > NameBox.Width) return "doesn't fit: " + Text;
                        if (Also != null && TextRenderer.MeasureText(Also, small).Width > Width - NameBox.Left - (int)Math.Round(6 * ScaleOf(this))) return "note doesn't fit: " + Also;
                    }
                    return null;
                }

                protected override void OnPaint(PaintEventArgs e)
                {
                    float s = ScaleOf(this);
                    Graphics graphics = e.Graphics;
                    Color shown = ColourPresets.ShownOf(Colour);
                    bool theirs = ColourPresets.TheirsOf(Colour).HasValue;
                    AccessibleName = Text + ", " + ColourPresets.Hex(shown) + (theirs ? "" : ", not set") + (Also != null ? ". " + Also : "");
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    float line = Math.Max(1f, s);
                    RectangleF plate = new RectangleF(line / 2, line / 2, Width - line, Height - line);
                    using (GraphicsPath path = Cut(plate, 0, 0, 5 * s, 0))
                    {
                        using (Brush back = new SolidBrush(chosen ? Hover : Ground)) graphics.FillPath(back, path);
                        if (chosen || (Focused && ShowFocusCues)) using (Pen rim = new Pen(chosen ? Hot : Edge, line)) graphics.DrawPath(rim, path);
                    }
                    int box = (int)Math.Round(16 * s);
                    RectangleF swatch = new RectangleF(10 * s, (NameTall - box) / 2f, box, box);
                    using (GraphicsPath chip = Cut(swatch, 0, 0, 4 * s, 0))
                    {
                        using (Brush fill = new SolidBrush(Color.FromArgb(255, shown))) graphics.FillPath(fill, chip);
                        using (Pen rim = new Pen(Edge, line)) graphics.DrawPath(rim, chip);
                    }
                    TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
                    TextRenderer.DrawText(graphics, Text, Font, NameBox, Rose, flags);
                    TextRenderer.DrawText(graphics, ColourPresets.Hex(shown), Font, new Rectangle(Width - HexWide - (int)Math.Round(8 * s), 0, HexWide, NameTall), theirs ? Cyan : Faint, flags | TextFormatFlags.Right);
                    if (Also != null)
                        using (Font small = Look.Mono(8f * s, FontStyle.Regular, GraphicsUnit.Point))
                            TextRenderer.DrawText(graphics, Also, small, new Rectangle(NameBox.Left, NameTall - (int)Math.Round(4 * s), Width - NameBox.Left - (int)Math.Round(6 * s), Height - NameTall + (int)Math.Round(4 * s)), Faint, flags | TextFormatFlags.Top);
                }
            }
        }

        // A colour shown in a box with her clipped corner: the one picked, or a last used slot, empty until one is picked.
        private sealed class Swatch : Control
        {
            private Color colour = Color.Empty;

            internal Swatch()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                BackColor = Ground;
                AccessibleRole = AccessibleRole.PushButton;
            }

            internal Color Colour { get { return colour; } set { colour = value; Invalidate(); } }

            protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }

            protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                float s = ScaleOf(this);
                Graphics graphics = e.Graphics;
                Backdrop(graphics, this, s);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float line = Math.Max(1f, s);
                using (GraphicsPath plate = Cut(new RectangleF(line / 2, line / 2, Width - line, Height - line), 0, 0, 5 * s, 0))
                {
                    using (Brush fill = new SolidBrush(colour.IsEmpty ? Well : colour)) graphics.FillPath(fill, plate);
                    using (Pen rim = new Pen(Focused && ShowFocusCues ? Cyan : colour.IsEmpty ? WellEdge : Edge, line))
                    {
                        if (colour.IsEmpty) rim.DashStyle = DashStyle.Dot;
                        graphics.DrawPath(rim, plate);
                    }
                }
            }
        }

        // The picker's square (saturation across, brightness down, in the hue picked) or its hue bar, dragged with the mouse
        // or moved with the arrow keys; Done when the mouse lets go or Enter is pressed. The spectrum is the colours
        // themselves; its marks and rim are her palette's.
        private sealed class Shade : Control
        {
            private readonly bool bar;
            internal float Hue, Across, Down;
            internal event Action Moved, Done;
            private bool grabbed;

            internal Shade(bool hueBar)
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                bar = hueBar;
                BackColor = Ground;
                Cursor = Cursors.Cross;
                TabStop = true;
                AccessibleRole = AccessibleRole.Slider;
            }

            private void At(Point point)
            {
                float x = Math.Max(0, Math.Min(1, point.X / (float)Math.Max(1, Width - 1))), y = Math.Max(0, Math.Min(1, point.Y / (float)Math.Max(1, Height - 1)));
                if (!bar) Across = x;
                Down = y;
                Invalidate();
                if (Moved != null) Moved();
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) { grabbed = true; Capture = true; Focus(); At(e.Location); }
                base.OnMouseDown(e);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                if (grabbed) At(e.Location);
                base.OnMouseMove(e);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                if (grabbed)
                {
                    At(e.Location);
                    grabbed = false;
                    Capture = false;
                    if (Done != null) Done();
                }
                base.OnMouseUp(e);
            }

            protected override bool IsInputKey(Keys keyData)
            {
                Keys key = keyData & Keys.KeyCode;
                return key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down || base.IsInputKey(keyData);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                float step = 0.02f;
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; if (Done != null) Done(); return; }
                if (e.KeyCode == Keys.Up) Down = Math.Max(0, Down - step);
                else if (e.KeyCode == Keys.Down) Down = Math.Min(1, Down + step);
                else if (!bar && e.KeyCode == Keys.Left) Across = Math.Max(0, Across - step);
                else if (!bar && e.KeyCode == Keys.Right) Across = Math.Min(1, Across + step);
                else { base.OnKeyDown(e); return; }
                e.Handled = true;
                Invalidate();
                if (Moved != null) Moved();
            }

            protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }

            protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                float s = ScaleOf(this);
                Graphics graphics = e.Graphics;
                Rectangle all = ClientRectangle;
                if (all.Width < 2 || all.Height < 2) return;
                if (bar)
                {
                    using (LinearGradientBrush hues = new LinearGradientBrush(all, Color.Red, Color.Red, LinearGradientMode.Vertical))
                    {
                        ColorBlend blend = new ColorBlend(7);
                        for (int i = 0; i < 7; i++) { blend.Colors[i] = ColourPresets.FromHsv(i * 60, 1, 1); blend.Positions[i] = i / 6f; }
                        hues.InterpolationColors = blend;
                        graphics.FillRectangle(hues, all);
                    }
                    float y = Down * (Height - 1);
                    using (Pen mark = new Pen(White, 2 * s)) graphics.DrawRectangle(mark, 1 * s, y - 3 * s, Width - 2 * s, 6 * s);
                    using (Pen inner = new Pen(Ground, Math.Max(1f, s * 0.8f))) graphics.DrawRectangle(inner, 2.5f * s, y - 1.5f * s, Width - 5 * s, 3 * s);
                }
                else
                {
                    using (Brush pure = new SolidBrush(ColourPresets.FromHsv(Hue, 1, 1))) graphics.FillRectangle(pure, all);
                    using (LinearGradientBrush light = new LinearGradientBrush(all, Color.FromArgb(255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Horizontal)) graphics.FillRectangle(light, all);
                    using (LinearGradientBrush dark = new LinearGradientBrush(all, Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0, 0, 0), LinearGradientMode.Vertical)) graphics.FillRectangle(dark, all);
                    float x = Across * (Width - 1), y = Down * (Height - 1), r = 6 * s;
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    using (Pen outer = new Pen(Ground, 2.5f * s)) graphics.DrawEllipse(outer, x - r, y - r, r * 2, r * 2);
                    using (Pen ring = new Pen(White, 1.5f * s)) graphics.DrawEllipse(ring, x - r, y - r, r * 2, r * 2);
                }
                using (Pen rim = new Pen(Focused && ShowFocusCues ? Cyan : WellEdge, Math.Max(1f, s))) graphics.DrawRectangle(rim, 0, 0, Width - 1, Height - 1);
            }
        }
    }
}
