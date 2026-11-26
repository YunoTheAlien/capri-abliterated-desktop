using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

#if CHECKS
namespace CapriDesktop
{
    // Each of her windows on stand-in screens (1280 x 720 to 1600 x 900, 100 to 150%): opened at the zoom it would get
    // there and measured, wholly inside the area, every control inside its window.
    internal sealed partial class Mainframe
    {
        // Her title bar on every Mainframe page (front, Tamagotchi mode, SETUP, Settings, Mods) at the zoom each stand-in screen gives
        // it: minimise and close always there, wholly inside the window and under no other key; the page keys before them.
        internal void CheckTitleKeys(List<string> checks, string output)
        {
            int[][] screens = { new[] { 1280, 720 }, new[] { 1366, 768 }, new[] { 1600, 900 } };
            int[] percents = { 100, 125, 150 };
            Rectangle home = Screen.FromControl(this).WorkingArea;
            Rectangle standWas = Fits.StandIn;
            float scaleWas = Fits.StandInScale;
            int sizeWas = capri.MainframeSize;
            FormWindowState stateWas = WindowState;
            bool shownWas = Visible;
            List<string> wrong = new List<string>();
            int pages = 0;
            try
            {
                WindowState = FormWindowState.Normal;
                Show();
                foreach (int[] screen in screens)
                    foreach (int percent in percents)
                    {
                        float scale = percent / 100f;
                        Fits.StandIn = new Rectangle(home.Left, home.Top, screen[0], screen[1] - (int)Math.Round(40 * scale));
                        Fits.StandInScale = scale;
                        LayoutScale((int)Math.Round(96 * scale), 100);
                        SizeMainframe(0);
                        foreach (string page in new[] { "front", "Tamagotchi mode", "SETUP", "Settings", "Mods" })
                        {
                            ShowSettings(page == "Settings");
                            ShowSetupPage(page == "SETUP");
                            ShowMods(page == "Mods");
                            if (page == "Tamagotchi mode") ShowPetMode(true);
                            Application.DoEvents();
                            string problem = TitleProblem();
                            if (problem != null) wrong.Add(page + " on " + screen[0] + "x" + screen[1] + " at " + percent + "%: " + problem);
                            if (page == "Tamagotchi mode") ShowPetMode(false);
                            pages++;
                            if (screen[0] == 1600 && percent == 100 && page == "front")
                                using (Bitmap picture = new Bitmap(Width, Height))
                                {
                                    DrawToBitmap(picture, new Rectangle(Point.Empty, picture.Size));
                                    picture.Save(System.IO.Path.Combine(output, "mainframe-title-fixed.png"), System.Drawing.Imaging.ImageFormat.Png);
                                }
                        }
                    }
            }
            finally
            {
                Fits.StandIn = standWas;
                Fits.StandInScale = scaleWas;
                ShowMods(false);
                ShowSetupPage(false);
                ShowSettings(false);
                LayoutScale(0, 0);
                SizeMainframe(sizeWas);
                WindowState = stateWas;
                if (!shownWas) Hide();
            }
            if (wrong.Count > 0) throw new Exception("Her title bar went wrong: " + string.Join("; ", wrong.ToArray()));
            checks.Add("PASS her title bar on " + pages + " page views (front, Tamagotchi mode, SETUP, Settings, Mods on nine stand-in screens): minimise and close always there, inside the window, under no other key, the page keys before them (mainframe-title-fixed.png)");
        }

        // Minimise or close missing, cut off or under another key; a page key past minimise; or null.
        private string TitleProblem()
        {
            foreach (Key key in new[] { minimize, close })
            {
                string name = key == close ? "close" : "minimise";
                if (!key.Visible || key.Left >= Far) return name + " isn't shown";
                if (key.Left < 0 || key.Right > ClientSize.Width || key.Bottom > ClientSize.Height) return name + " is cut off at " + key.Bounds + " in " + ClientSize;
                foreach (Control other in new Control[] { advancedKey, settingsPageKey, more, modsKey, lockKey })
                    if (other.Visible && other.Left < Far && other.Bounds.IntersectsWith(key.Bounds)) return name + " is under " + (other.Text.Length > 0 ? other.Text : other.AccessibleName) + " (" + other.Bounds + " over " + key.Bounds + ")";
            }
            if (minimize.Right > close.Left) return "minimise runs into close";
            // The front page's bar is MAINFRAME, Settings, Mods, minimise, close, and nothing else.
            if (!paged && !modded && !setuped && (advancedKey.Left < Far || more.Text != "Settings" || !modsKey.Visible || modsKey.Left >= Far)) return "the front page's bar isn't Settings and Mods alone";
            foreach (Control page in new Control[] { advancedKey, settingsPageKey, more, modsKey })
                if (page.Visible && page.Left < Far && page.Right > minimize.Left) return page.Text + " runs past minimise (" + page.Bounds + ")";
            // Settings and SETUP: Back, then Settings, Brain and Mods together in that order, none over another, the page shown lit.
            if (paged || setuped)
            {
                Control[] order = { more, settingsPageKey, advancedKey, modsKey };
                for (int at = 0; at < order.Length; at++)
                {
                    if (!order[at].Visible || order[at].Left >= Far) return order[at].Text + " isn't on the bar";
                    if (at > 0 && (order[at].Left < order[at - 1].Right || order[at].Left - order[at - 1].Right > Scaled(8))) return order[at].Text + " isn't beside " + order[at - 1].Text + " (" + order[at - 1].Bounds + ", " + order[at].Bounds + ")";
                }
                if (more.Text != "Back" || settingsPageKey.Picked != paged || advancedKey.Picked != setuped || modsKey.Picked) return "the page shown isn't the one lit";
                if (settingsSearch != null && settingsSearch.Visible && modsKey.Right > settingsSearch.Left) return "Mods runs into the search";
            }
            return null;
        }

        internal void CheckFitScreens(List<string> checks)
        {
            List<KeyValuePair<string, Func<Form>>> windows = new List<KeyValuePair<string, Func<Form>>>
            {
                Sized("Provider", Wide, 484, delegate(float z) { return new ProviderSheet(this, z); }),
                Sized("Thinking", Wide, 356, delegate(float z) { return new ThinkingSheet(this, z); }),
                Sized("Server", Wide, 420, delegate(float z) { return new ServerSheet(this, z); }),
                Sized("SECURITY", SecuritySheet.Wide, SecuritySheet.Tall, delegate(float z) { return new SecuritySheet(this, z); }),
                Sized("Extras", ExtrasSheet.Wide, ExtrasSheet.Tall, delegate(float z) { return new ExtrasSheet(this, z); }),
                Sized("Voice settings", LayersSheet.Wide, LayersSheet.Tall, delegate(float z) { return new LayersSheet(this, z); }),
                Sized("Mix", MixSheet.Wide, MixSheet.Tall, delegate(float z) { return new MixSheet(this, z); }),
                Sized("Personality settings", PersonalitySheet.Wide, PersonalitySheet.Tall, delegate(float z) { return new PersonalitySheet(this, z); }),
                Sized("Custom colours", ColourSheet.Wide, ColourSheet.Tall, delegate(float z) { return new ColourSheet(this, z); }),
                Sized("Toggles", ToggleSheet.Wide, ToggleSheet.Tall, delegate(float z) { return new ToggleSheet(this, z, capri.TogglesMenu); }),
                Sized("Make a mod", ModHelperSheet.Wide, ModHelperSheet.Tall, delegate(float z) { return new ModHelperSheet(this, z); }),
                new KeyValuePair<string, Func<Form>>("Streaming", delegate { return MakeStreaming(null); }),
                Sized("Her GIFs", GifSheet.Wide, GifSheet.Tall, delegate(float z) { return new GifSheet(this, z); }),
                Sized("Her memory", MemorySheet.Wide, MemorySheet.Tall, delegate(float z) { return new MemorySheet(this, z); }),
                Sized("Mods directory", ModsSheet.Wide, ModsSheet.Tall, delegate(float z) { return new ModsSheet(this, z); }),
                Sized("Set up Email", MailSheet.Wide, MailSheet.Tall, delegate(float z) { return new MailSheet(this, z); }),
                Sized("Mods conflict", ConflictSheet.Wide, ConflictSheet.Tall, delegate(float z) { return new ConflictSheet(this, z, "First", "Second", new List<FileSlots.Contest>()); }),
                Sized("Game / project planner", PlannerSheet.Wide, PlannerSheet.Tall, delegate(float z) { return new PlannerSheet(this, z, null, "Planner not in this build"); }),
                // A notice as long as the update's list of settings with no place: it scrolls inside the screen, OK showing.
                new KeyValuePair<string, Func<Form>>("Long notice", delegate { return new Notice("These settings have no place:" + string.Concat(System.Linq.Enumerable.Repeat("\r\n  personalities/censored/Bratty > How she reacts > flirt", 90)), "Settings after the update", true); }),
            };
            int[][] screens = { new[] { 1280, 720 }, new[] { 1366, 768 }, new[] { 1600, 900 } };
            int[] percents = { 100, 125, 150 };
            Rectangle home = Screen.FromControl(this).WorkingArea;
            float real = DeviceScale();
            List<string> over = new List<string>();
            int measured = 0;
            Rectangle standWas = Fits.StandIn;
            float scaleWas = Fits.StandInScale;
            try
            {
                foreach (int[] screen in screens)
                    foreach (int percent in percents)
                    {
                        float scale = percent / 100f;
                        Fits.StandIn = new Rectangle(home.Left, home.Top, screen[0], screen[1] - (int)Math.Round(40 * scale));
                        Fits.StandInScale = scale;
                        string at = screen[0] + "x" + screen[1] + " at " + percent + "%";
                        // Her Mainframe's main page at the zoom it would take there.
                        float frame = Fitted(1f) * scale * TextScale();
                        if (Wide * frame > Fits.StandIn.Width || NeedsTall * frame > Fits.StandIn.Height)
                            over.Add("Mainframe on " + at + ": " + (int)(Wide * frame) + " x " + (int)(NeedsTall * frame));
                        // The first-run card's tallest screen.
                        float card = FirstRunCard.FitScale(scale * TextScale(), Fits.StandIn);
                        if (FirstRunCard.Wide * card > Fits.StandIn.Width || FirstRunCard.Tallest * card > Fits.StandIn.Height)
                            over.Add("first-run card on " + at + ": " + (int)(FirstRunCard.Wide * card) + " x " + (int)(FirstRunCard.Tallest * card));
                        foreach (KeyValuePair<string, Func<Form>> one in windows)
                        {
                            Form made = null;
                            try
                            {
                                made = one.Value();
                                made.StartPosition = FormStartPosition.Manual;
                                made.Location = home.Location;
                                made.Show(this);
                                Application.DoEvents();
                                measured++;
                                // Drawn on this screen at its own scale; as it would be on the stand-in.
                                Size there = new Size((int)Math.Ceiling(made.Width * scale / real), (int)Math.Ceiling(made.Height * scale / real));
                                if (there.Width > Fits.StandIn.Width || there.Height > Fits.StandIn.Height)
                                    over.Add(one.Key + " on " + at + ": " + there.Width + " x " + there.Height + " in " + Fits.StandIn.Width + " x " + Fits.StandIn.Height);
                                foreach (Control inner in made.Controls)
                                    if (inner.Visible && (inner.Right > made.ClientSize.Width + 1 || inner.Bottom > made.ClientSize.Height + 1))
                                    {
                                        over.Add(one.Key + " on " + at + ": " + (inner.AccessibleName ?? inner.Text ?? inner.GetType().Name) + " past its edge");
                                        break;
                                    }
                            }
                            catch (Exception error) { over.Add(one.Key + " on " + at + " couldn't be opened: " + error.Message); }
                            finally { if (made != null && !made.IsDisposed) { made.Close(); made.Dispose(); } }
                        }
                    }
            }
            finally { Fits.StandIn = standWas; Fits.StandInScale = scaleWas; }
            if (over.Count > 0) throw new Exception("A window doesn't fit a small screen: " + string.Join("; ", over.ToArray()));
            checks.Add("PASS each window fits 1280 x 720, 1366 x 768 and 1600 x 900 at 100%, 125% and 150%: the Mainframe, the first-run card and " + windows.Count + " windows, " + measured + " opened and measured, every control inside its window");
        }

        // A window by name, opened at the zoom its design size gets on the stand-in screen.
        private KeyValuePair<string, Func<Form>> Sized(string name, int wide, int tall, Func<float, Form> make)
        {
            return new KeyValuePair<string, Func<Form>>(name, delegate { return make(SheetZoom(wide, tall)); });
        }
    }
}
#endif
