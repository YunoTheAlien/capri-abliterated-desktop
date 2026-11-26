using System;
using System.Drawing;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Her side of Tamagotchi mode: docked into her Mainframe's well the way Gaming Mode docks her on a game's
    // display, except that she keeps drawing, into the well rather than onto the desktop. Gaming Mode wins over it.
    internal sealed partial class Companion
    {
        private bool petDocked;

        internal bool PetDocked { get { return petDocked; } }

        internal bool GamingDocked { get { return docked; } }

        // One of her steady loops, not a glitch, an arrival or a leave: only these set how big she is drawn in the well.
        private bool PetSteady { get { return clip == idle || clip == perch || clip == laydown || clip == laydownSleep || clip == dance || clip == perchDance || clip == grin || clip == perchTalk || clip == laydownTalk; } }

        // The pet game running, for Tamagotchi mode's points.
        internal ICapriPet Pet { get { return RunningPet; } }

        // Into the well, or back out where she was.
        internal void PetDock(bool on)
        {
            if (on == petDocked || quitting || IsDisposed) return;
            if (on)
            {
                if (docked) return;
                StopRide();
                LeavePanel(true);
                EndWalk();
                falling = false;
                dropAfter = false;
                petDocked = true;
                if (box.Visible) box.Hide();
                pill.Hide();
                Hide();
                frames.Start();
                RenderFrame();
                Brain.Log("pet", -1, "Tamagotchi mode: she's in the Mainframe.");
            }
            else
            {
                petDocked = false;
                if (!hidden && !docked)
                {
                    if (seat == null && !hanging) anchor = KeepOnScreen(anchor);
                    Location = anchor;
                    Show();
                    RenderFrame();
                    UpdateAnimation();
                }
                Brain.Log("pet", -1, "Tamagotchi mode: she's back on the desktop.");
            }
            mainframe.Sync();
        }

        // Gaming Mode wins over Tamagotchi mode: the Mainframe can't be seen over the game.
        private void PetGivesWay() { if (petDocked) mainframe.ShowPetMode(false); }

        // A head pat from Tamagotchi mode's Pat, as a pat on her head counts.
        internal void PatFromPet() { Petted(); }

        // A need ring's list on the Mainframe's strip: her own items that fill it, named and priced as her menu has them as
        // it opens. Food, gifts, a pet game's wash, her rest.
        internal System.Collections.Generic.List<ToolStripItem> NeedItems(string need)
        {
            RestLabels();
            ShowFeedMenu();
            ShowTamagotchi();
            ModsMenuOpening();
            System.Collections.Generic.List<ToolStripItem> items = new System.Collections.Generic.List<ToolStripItem>();
            ToolStripMenuItem from = need == "fullness" ? PetList("feed") : need == "comfort" ? PetList("gift") : null;
            if (from != null) foreach (ToolStripItem one in from.DropDownItems) items.Add(one);
            if (need == "cleanliness" && tamagotchiItem != null) foreach (ToolStripItem one in tamagotchiItem.DropDownItems) if (one.Text == "Wash her") items.Add(one);
            if (need == "energy") { items.Add(sleepItem); items.Add(lieItem); }
            return items;
        }

        // Tamagotchi mode's Feed and Gifts: her own lists' items, for the Mainframe to show from the tile.
        internal ToolStripMenuItem PetList(string which)
        {
            ToolStripMenuItem from = which == "feed" ? feedItem : null;
            if (which == "gift" && tamagotchiItem != null)
                foreach (ToolStripItem one in tamagotchiItem.DropDownItems) if (one.Text == "Give gift") from = one as ToolStripMenuItem;
            return from != null && from.HasDropDownItems ? from : null;
        }

#if CHECKS
        // Which of a frame's top, left and right edges have her drawing on them, or null when none do.
        private static string LitEdges(Bitmap drawn)
        {
            Rectangle lit = Mainframe.PetWell.Lit(drawn);
            if (lit.IsEmpty) return null;
            System.Collections.Generic.List<string> touched = new System.Collections.Generic.List<string>();
            if (lit.Top <= 0) touched.Add("top");
            if (lit.Left <= 0) touched.Add("left");
            if (lit.Right >= drawn.Width) touched.Add("right");
            return touched.Count == 0 ? null : string.Join(" and ", touched.ToArray()) + " (" + lit + " in " + drawn.Size + ")";
        }

        // Tamagotchi mode start to end: she draws into the well, her rings at their levels, a tip's list, her line in its bubble,
        // Back undocking her with nothing drawn after, Gaming Mode winning both ways, the tile hidden with no pet game.
        private void SmokePetMode(System.Collections.Generic.List<string> checks, string output)
        {
            ICapriPet petWas = petStandIn;
            Func<System.Collections.Generic.List<string>> loadWas = PetStats.Load;
            Action<System.Collections.Generic.List<string>> storeWas = PetStats.Store;
            Func<bool> runningWas = PetStats.Running;
            System.Collections.Generic.List<string> rows = new System.Collections.Generic.List<string>();
            foreach (string need in PetStats.Names) rows.Add(need + "=" + (need == "social" ? "10" : "70"));
            System.Collections.Generic.List<string> wrong = new System.Collections.Generic.List<string>();
            Action settle = delegate { for (int i = 0; i < 40; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(25); } };
            try
            {
                petStandIn = new StandInPet();
                PetStats.Load = delegate { return rows; };
                PetStats.Store = delegate(System.Collections.Generic.List<string> made) { rows = made; };
                PetStats.Running = delegate { return true; };
                if (hidden && !docked) SetHidden(false);
                // Her glitch as every surface draws it (her window, the well, the stream): at full strength over twelve seeds,
                // drawn on a canvas as wide as her window gives it, nothing reaches the canvas's top or sides; a picture of four.
                Bitmap idleArt = idle.Strip;
                int cellWide = idleArt.Width / idle.Count;
                string underEdges = Shatter.UnderEdges(idleArt, new Rectangle(0, 0, cellWide, idleArt.Height), System.IO.Path.Combine(output, "glitch-desktop.png"));
                if (underEdges != null) wrong.Add("her glitch reaches the edge of where it's drawn: " + underEdges);
                mainframe.WindowState = FormWindowState.Normal;
                mainframe.Show();
                mainframe.ShowSettings(false);
                mainframe.ShowPetMode(true);
                settle();
                if (!petDocked || Visible || !mainframe.PetShown) wrong.Add("Tamagotchi mode didn't take her off the desktop: docked " + petDocked + ", her window shown " + Visible);
                if (mainframe.PetFramesTaken == 0) wrong.Add("nothing was drawn into the well");
                if (WanderHeld(clock.Elapsed.TotalSeconds, 0) != "Tamagotchi mode" && WanderHeld(clock.Elapsed.TotalSeconds, 0) == null) wrong.Add("she could wander off in Tamagotchi mode");
                string rings = mainframe.PetRingsShown;
                if (rings.IndexOf("social 10 low", StringComparison.Ordinal) < 0 || rings.IndexOf("energy 70,", StringComparison.Ordinal) < 0) wrong.Add("rings read " + rings);
                if (mainframe.NeedTip("social").IndexOf("praised", StringComparison.Ordinal) < 0) wrong.Add("social's tip: " + mainframe.NeedTip("social"));
                Remark("", "Hello from the well.", true);
                settle();
                if (box.Visible || !mainframe.PetBubbleShown) wrong.Add("her bubble wasn't drawn in the well: desktop bubble " + box.Visible + ", in the well " + mainframe.PetBubbleShown);
                // All of her drawing, hair, glow and notes, inside what the well shows, and the well's placing inside its border.
                string cut = mainframe.PetCut(frame);
                if (cut != null) wrong.Add("her drawing is cut in the well: " + cut);
                // Feed, Gifts and Play each open their list at their tile.
                for (int tile = 0; tile < 5; tile++)
                {
                    // Wash and Pat have no list, and with the stand-in pet there is no pet game to give gifts.
                    if (tile == 2 || tile == 3 || tile == 1 && PetList("gift") == null) continue;
                    mainframe.PetPress(tile);
                    settle();
                    ToolStripDropDown shown = tile == 4 ? null : mainframe.PetListShown;
                    if (tile != 4)
                    {
                        // Feed's first food, pressed from the tile's list, fills her and moves the ring.
                        if (tile == 0 && shown != null)
                        {
                            int before = PetStats.Level("fullness");
                            ToolStripMenuItem food = null;
                            foreach (ToolStripItem one in shown.Items) { food = one as ToolStripMenuItem; if (food != null && food.Enabled) break; }
                            if (food != null) food.PerformClick();
                            settle();
                            int after = PetStats.Level("fullness");
                            if (after <= before || mainframe.PetRingsShown.IndexOf("fullness " + after, StringComparison.Ordinal) < 0)
                                wrong.Add("feeding from the tile didn't fill her or move the ring: " + before + " to " + after + ", rings " + mainframe.PetRingsShown);
                            mainframe.PetPress(tile);
                            settle();
                            shown = mainframe.PetListShown;
                        }
                        Rectangle near = Rectangle.Inflate(mainframe.PetTileOnScreen(tile), 2, 2);
                        if (shown == null || !shown.Visible || !shown.Bounds.IntersectsWith(near)) wrong.Add("tile " + tile + "'s list opened at " + (shown == null ? "nowhere" : shown.Bounds.ToString()) + ", not at " + near);
                        if (shown != null) shown.Close();
                    }
                }
                try
                {
                    System.IO.Directory.CreateDirectory(output);
                    using (Bitmap picture = new Bitmap(mainframe.ClientSize.Width, mainframe.ClientSize.Height))
                    {
                        mainframe.DrawToBitmap(picture, new Rectangle(Point.Empty, picture.Size));
                        picture.Save(System.IO.Path.Combine(output, "pet-mode.png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                catch (Exception caughtOne) { Brain.Caught(caughtOne); }

                // Each ring has her tip card with its own words.
                if (mainframe.RingTips != PetStats.Names.Length) wrong.Add("rings with her tip card: " + mainframe.RingTips + " of " + PetStats.Names.Length);
                // Her canvas in the well holds only this frame: across a glitch, an idle and a walk, no pixel lies outside what the
                // frame drew, so nothing from an earlier frame smears in the well.
                foreach (Clip moving in new[] { glitch, idle, walk })
                {
                    Play(moving);
                    for (int tick = 0; tick < 12; tick++)
                    {
                        for (int i = 0; i < 3; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(25); }
                        Bitmap now = frame;
                        if (now == null) continue;
                        Rectangle lit = Mainframe.PetWell.Lit(now), drew = Rectangle.Inflate(drawnBounds, 6, 6);
                        if (!lit.IsEmpty && !drew.Contains(lit)) { wrong.Add("the well's canvas kept an earlier frame: lit " + lit + " past what " + moving.Title + " drew, " + drawnBounds); break; }
                    }
                }
                Play(idle);
                settle();
                // Her glitch reaches further than she does: it shows whole in the well too.
                Play(glitch);
                settle();
                string glitchCut = mainframe.PetCut(frame);
                // A picture of the well mid-glitch, drawn from the window, for an eye on where her streaks end.
                try
                {
                    using (Bitmap picture = new Bitmap(mainframe.ClientSize.Width, mainframe.ClientSize.Height))
                    {
                        mainframe.DrawToBitmap(picture, new Rectangle(Point.Empty, picture.Size));
                        picture.Save(System.IO.Path.Combine(output, "pet-mode-glitch.png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                catch (Exception caughtOne) { Brain.Caught(caughtOne); }
                if (glitchCut != null) wrong.Add("her glitch is cut in the well: " + glitchCut);
                Play(idle);
                settle();

                // Back: she's on the desktop again and nothing more is drawn into the well.
                mainframe.ShowPetMode(false);
                int taken = mainframe.PetFramesTaken;
                settle();
                if (petDocked || !Visible || mainframe.PetShown) wrong.Add("Back didn't put her back: docked " + petDocked + ", shown " + Visible);
                if (mainframe.PetFramesTaken != taken) wrong.Add("the well was still drawn into after Back");

                // Gaming Mode wins: docked on a game, the tile doesn't dock her twice; in Tamagotchi mode, Gaming Mode taking her ends it.
                docked = true;
                mainframe.ShowPetMode(true);
                if (petDocked || mainframe.PetShown) wrong.Add("Tamagotchi mode docked her over Gaming Mode");
                docked = false;
                mainframe.ShowPetMode(true);
                PetGivesWay();
                if (petDocked || mainframe.PetShown) wrong.Add("Gaming Mode didn't end Tamagotchi mode");

                // With no pet game the tile isn't shown.
                petStandIn = null;
                Mainframe.Tool pet = mainframe.Tools().Find(one => one.Id == "pet");
                if (pet != null && pet.On != null && pet.On() && (ModList == null || ModList.Pet == null)) wrong.Add("the Tamagotchi tile shows with no pet game");
            }
            finally
            {
                if (mainframe.PetShown) mainframe.ShowPetMode(false);
                docked = false;
                petStandIn = petWas;
                PetStats.Load = loadWas; PetStats.Store = storeWas; PetStats.Running = runningWas;
            }
            if (wrong.Count > 0) throw new Exception("Tamagotchi mode went wrong: " + string.Join("; ", wrong.ToArray()));
            checks.Add("PASS Tamagotchi mode: she leaves the desktop and is drawn into the well, held from wandering; six rings at their levels with a low one red; a need's tip lists what raises it; her line shows in the well, not her bubble; Back puts her back with nothing more drawn into the well; Gaming Mode wins both ways; no tile with no pet game");
        }
#endif
    }
}
