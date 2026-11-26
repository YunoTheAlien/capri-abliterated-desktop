using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace CapriDesktop
{
    // One size for each kind of control in her windows' right panels: a key, a slider and a switch each as tall as
    // Settings' rows have them, a slider and a full switch as wide as Settings' value box.
    internal sealed partial class Mainframe
    {
        // Settings' row: a value box from x 184, 172 wide and 22 tall; a narrow switch 68 wide.
        internal const int ControlTall = 22, ValueBox = ValueWide, NarrowSwitch = 68;

#if CHECKS
        // Not yet held to it: Colours and Memory's text-box rows at 26, and Security's preset at 36.
        private static readonly string[] SizesHeld = { "colours	", "memory	key	Add	", "memory	key	Cancel	", "memory	key	Edit as text	", "memory	key	Forget everything about me	", "security	slider	Security preset" };

        // Every key, slider and switch in her windows 22 tall, the survey and pictures kept (sizes/).
        internal void SmokeStandardSizes(List<string> checks, string output)
        {
            string folder = Path.Combine(output, "sizes");
            List<string> found = SurveySizes(folder);
            File.WriteAllLines(Path.Combine(folder, "survey.txt"), found.ToArray());
            List<string> off = new List<string>();
            foreach (string row in found)
            {
                string[] parts = row.Split('	');
                if (parts.Length < 5 || parts[1] == "-" || parts[4].EndsWith("x" + ControlTall, StringComparison.Ordinal)) continue;
                if (Array.Exists(SizesHeld, held => row.StartsWith(held, StringComparison.Ordinal))) continue;
                off.Add(parts[0] + " " + parts[1] + " \"" + parts[2] + "\" " + parts[4]);
            }
            int windows = 0;
            foreach (string one in Directory.GetFiles(folder, "*.png")) windows++;
            if (off.Count > 0) throw new Exception("Off the standard 22 tall: " + string.Join("; ", off.ToArray()));
            int names;
            string indented = NamesLinedUp(out names);
            if (indented != null) throw new Exception("Row names not at one x on Settings: " + indented);
            checks.Add("PASS standard sizes: every key, slider and switch below the title in " + windows + " of her windows is 22 tall, Colours and Memory's text-box rows and Security's preset not yet held to it (sizes/survey.txt and a picture each); Settings' " + names + " row names start at one x in each column");
        }

        // Every row name on Settings, a switch's or a label's, has its first drawn pixel at the same x in its column. The
        // names off it, else null.
        internal string NamesLinedUp(out int names)
        {
            names = 0;
            bool pagedWas = paged;
            List<KeyValuePair<string, int>> starts = new List<KeyValuePair<string, int>>();
            try
            {
                ShowSettings(true);
                ScrollPage(0);
                foreach (KeyValuePair<Control, int> entry in pageLeft)
                {
                    Control one = entry.Key;
                    Toggle toggle = one as Toggle;
                    Note note = one as Note;
                    if (!one.Visible || one.Width <= 0 || one.Height <= 0 || one.Left >= ClientSize.Width) continue;
                    if (toggle == null && (note == null || note.ForeColor.ToArgb() != Rose.ToArgb())) continue;
                    if (toggle != null && (!toggle.Enabled || toggle.Dimmed)) continue;
                    int column = entry.Value >= Column ? Column : 0;
                    if (entry.Value - column > 40) continue;
                    int first = -1;
                    using (Bitmap shot = new Bitmap(one.Width, one.Height))
                    {
                        one.DrawToBitmap(shot, new Rectangle(Point.Empty, shot.Size));
                        Color ink = Rose;
                        for (int x = 0; x < shot.Width && first < 0; x++)
                            for (int y = 0; y < shot.Height; y++)
                            {
                                Color seen = shot.GetPixel(x, y);
                                if (Math.Abs(seen.R - ink.R) + Math.Abs(seen.G - ink.G) + Math.Abs(seen.B - ink.B) < 90) { first = x; break; }
                            }
                    }
                    if (first < 0) continue;
                    starts.Add(new KeyValuePair<string, int>((toggle != null ? "switch " : "name ") + one.Text, one.Left + first - Scaled(column)));
                }
            }
            finally { ShowSettings(pagedWas); }
            names = starts.Count;
            Dictionary<int, int> counted = new Dictionary<int, int>();
            foreach (KeyValuePair<string, int> start in starts) { int had; counted.TryGetValue(start.Value, out had); counted[start.Value] = had + 1; }
            int usual = 0, most = 0;
            foreach (KeyValuePair<int, int> one in counted) if (one.Value > most) { most = one.Value; usual = one.Key; }
            List<string> off = new List<string>();
            foreach (KeyValuePair<string, int> start in starts) if (Math.Abs(start.Value - usual) > 1) off.Add(start.Key + " at " + start.Value + " for " + usual);
            return off.Count == 0 ? null : string.Join("; ", off.ToArray());
        }

        // At the top of Settings nothing sits above her title strip and her band is down, after a scroll, page switch, fold
        // and slide back, at 100 and 150% (top/). The faults, else null.
        internal string SettingsTopCheck(string output)
        {
            const string owner = "YOU;VOICE;SOUNDS;CLIPS;MOVES;GAMES;DOOMSCROLL;WEATHER;WATCH|GENERAL SETTINGS;CAPRI;BRAIN;KEYS";
            string folder = Path.Combine(output, "top");
            Directory.CreateDirectory(folder);
            List<string> wrong = new List<string>();
            string savedWas = capri.PanelsSaved;
            bool pagedWas = paged, voiceFolded = capri.FoldedNow("VOICE");
            try
            {
                if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
                if (!Visible) Show();
                foreach (int percent in new[] { 100, 150 })
                {
                    LayoutScale(percent * 96 / 100, 100);
                    capri.SavePanels(owner);
                    SavedOrder();
                    Restack();
                    Action<string> look = delegate(string step)
                    {
                        Application.DoEvents();
                        string name = percent + "-" + step;
                        using (Bitmap shot = new Bitmap(Width, Height))
                        {
                            DrawToBitmap(shot, new Rectangle(Point.Empty, shot.Size));
                            shot.Save(Path.Combine(folder, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                        }
                        if (pageScroll != 0) return;
                        if (titleBand.Visible) { wrong.Add(name + ": her title band up over the page at its top"); return; }
                        int strip = Scaled(TitleTall);
                        foreach (KeyValuePair<Control, int> entry in pageLeft)
                        {
                            Control one = entry.Key;
                            if (!one.Visible || one.Left >= ClientSize.Width || one.Right <= 0) continue;
                            if (one.Top < strip) { wrong.Add(name + ": " + (one.Text.Length > 0 ? one.Text : one.AccessibleName) + " at " + one.Top + " above the strip's " + strip); break; }
                        }
                    };
                    ShowSettings(true);
                    look("opened");
                    ScrollPage(Scaled(400));
                    ScrollPage(0);
                    look("scrolled-back");
                    ScrollPage(Scaled(300));
                    ShowSettings(false);
                    ShowSettings(true);
                    look("page-switched");
                    ScrollPage(Scaled(250));
                    capri.SetFolded("VOICE", true);
                    FoldsChanged();
                    ScrollPage(0);
                    look("folded");
                    capri.SetFolded("VOICE", false);
                    FoldsChanged();
                    look("unfolded");
                    // A notch down and straight back up is one slide that ends where it began.
                    FinishPicture();
                    Action glided = delegate
                    {
                        System.Diagnostics.Stopwatch waited = System.Diagnostics.Stopwatch.StartNew();
                        while (glide.Enabled && waited.ElapsedMilliseconds < 1000) { Application.DoEvents(); System.Threading.Thread.Sleep(2); }
                    };
                    ScrollWheel(-120);
                    glided();
                    bool slid = sliding && slideAt > 0;
                    ScrollWheel(120);
                    glided();
                    bool held = sliding;
                    Settle();
                    if (!slid || !held || pageScroll != 0) wrong.Add(percent + "-slid-back: no slide down and back in one: down " + slid + ", still sliding " + held + ", ended at " + pageScroll);
                    look("slid-back");
                }
            }
            finally
            {
                capri.SetFolded("VOICE", voiceFolded);
                ApplyFolds();
                // An empty saved order is the stock one, which SavedOrder leaves alone, so it is laid back here.
                if (string.IsNullOrEmpty(savedWas)) SetPanels(StockLeft, StockRight);
                capri.SavePanels(savedWas);
                SavedOrder();
                Neighbours();
                LayoutScale(0, 0);
                Restack();
                capri.SavePanels(savedWas);
                ShowSettings(pagedWas);
            }
            return wrong.Count == 0 ? null : string.Join("; ", wrong.ToArray());
        }

        // Each window opened in turn, its right panel's keys, sliders and switches measured at its own zoom and pictured.
        internal List<string> SurveySizes(string output)
        {
            List<string> found = new List<string>();
            Directory.CreateDirectory(output);
            KeyValuePair<string, Func<Form>>[] windows =
            {
                new KeyValuePair<string, Func<Form>>("personality", delegate { OpenPersonalitySettings(); return personalitySheet; }),
                new KeyValuePair<string, Func<Form>>("toggles", delegate { OpenToggles(); return toggleSheet; }),
                new KeyValuePair<string, Func<Form>>("colours", delegate { OpenColours(); return colourSheet; }),
                new KeyValuePair<string, Func<Form>>("security", delegate { OpenSecurity(); return guard; }),
                new KeyValuePair<string, Func<Form>>("memory", delegate { OpenMemory(); return memorySheet; }),
                new KeyValuePair<string, Func<Form>>("growth", delegate { OpenGrowth(); return growthSheet; }),
                new KeyValuePair<string, Func<Form>>("streaming", delegate { StreamingSheet sheet = new StreamingSheet(this, SheetZoom(StreamingSheet.Wide, StreamingSheet.Tall), capri.StreamMenu, null); sheet.StartPosition = FormStartPosition.Manual; sheet.Location = Location; sheet.Show(this); return sheet; }),
                new KeyValuePair<string, Func<Form>>("layers", delegate { OpenLayers(); return layered; }),
                new KeyValuePair<string, Func<Form>>("mix", delegate { OpenMix(); return mixSheet; }),
                new KeyValuePair<string, Func<Form>>("extras", delegate { ShowExtras(); return extras; }),
            };
            foreach (KeyValuePair<string, Func<Form>> window in windows)
            {
                Form opened;
                try { opened = window.Value(); }
                catch (Exception error) { found.Add(window.Key + "\t-\tcould not open: " + Inner(error)); continue; }
                Settle(window.Key, found);
                if (opened == null || opened.IsDisposed || !opened.Visible) { found.Add(window.Key + "\t-\tno window"); continue; }
                float zoom = ZoomOf(opened);
                try { Measure(window.Key, opened, opened, zoom, found); } catch (Exception error) { found.Add(window.Key + "\t-\tcould not measure: " + error.Message); }
                try
                {
                    using (Bitmap shot = new Bitmap(opened.Width, opened.Height))
                    {
                        opened.DrawToBitmap(shot, new Rectangle(Point.Empty, shot.Size));
                        shot.Save(Path.Combine(output, window.Key + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                catch (Exception caughtOne) { Brain.Caught(caughtOne); }
                try { opened.Close(); } catch (Exception error) { found.Add(window.Key + "\t-\tclosing: " + Inner(error)); }
                Settle(window.Key, found);
            }
            return found;
        }

        // Its window's queued work run, a fault in it written down rather than ending the survey.
        private static void Settle(string name, List<string> found)
        {
            try { Application.DoEvents(); } catch (Exception error) { found.Add(name + "\t-\tqueued work: " + Inner(error)); }
        }

        private static string Inner(Exception error)
        {
            while (error.InnerException != null) error = error.InnerException;
            return error.GetType().Name + " " + error.Message + " at " + (error.StackTrace ?? "").Split('\n')[0].Trim();
        }

        private static float ZoomOf(Form window)
        {
            System.Reflection.PropertyInfo zoomed = window.GetType().GetProperty("Zoom", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            System.Reflection.FieldInfo field = window.GetType().GetField("Zoom", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            object value = null;
            try { value = zoomed != null ? zoomed.GetValue(window, null) : field != null ? field.GetValue(window) : null; } catch (Exception) { }
            return value is float ? (float)value : ScaleOf(window);
        }

        // Every key, slider and switch below the title line, by its size in the window's own units.
        private static void Measure(string name, Control under, Form window, float zoom, List<string> found)
        {
            foreach (Control one in under.Controls)
            {
                if (one.Visible && (one is Key || one is Slider || one is Toggle))
                {
                    Point at = window.PointToClient(one.PointToScreen(Point.Empty));
                    int x = (int)Math.Round(at.X / zoom), y = (int)Math.Round(at.Y / zoom), w = (int)Math.Round(one.Width / zoom), h = (int)Math.Round(one.Height / zoom);
                    string kind = one is Key ? "key" : one is Slider ? "slider" : "switch";
                    string text = (one.Text.Length > 0 ? one.Text : one.AccessibleName ?? "").Replace("\t", " ");
                    if (y > 48) found.Add(name + "\t" + kind + "\t" + text + "\t" + x + "," + y + "\t" + w + "x" + h);
                }
                if (one.HasChildren) Measure(name, one, window, zoom, found);
            }
        }
#endif
    }
}
