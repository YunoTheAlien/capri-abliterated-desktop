using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Tamagotchi mode: the pet game inside her Mainframe, in TOOLS' place. She is drawn live in the well, her needs ring down its
    // side, Feed, Gifts, Wash, Pat and Play sit under her. Back, Esc or leaving the page puts her back on the desktop.
    internal sealed partial class Mainframe
    {
        // Five buttons across the well's and rings' width, as tall as before.
        private const int PetRing = 48, PetTile = 74, PetTileWide = 58, PetTileGap = 9;
        private readonly List<KeyValuePair<Control, int>> petLeft = new List<KeyValuePair<Control, int>>();
        private Key petBack;
        private Label petPoints;
        private PetWell petWell;
        private readonly List<PetRingControl> petRings = new List<PetRingControl>();
        private readonly ToolTile[] petTiles = new ToolTile[5];
        private bool petShown;

        internal bool PetShown { get { return petShown; } }

        private int PetWellTop { get { return ToolsTop + 54; } }
        private int PetTilesTop { get { return CapriTop - 16 - PetTile; } }

        private void BuildPet()
        {
            int from = Controls.Count;
            Section("TAMAGOTCHI MODE", 0, ToolsTop);
            petBack = Make("Back", 290, ToolsTop - 2, 66, 22, delegate { ShowPetMode(false); });
            petBack.AccessibleName = "Back to TOOLS";
            petPoints = Caption("", 28, ToolsTop + 28, Font, Neon.Points);
            int wellBottom = PetTilesTop - 12;
            petWell = new PetWell { Bounds = new Rectangle(28, PetWellTop, 264, wellBottom - PetWellTop), Font = Font };
            petWell.AccessibleName = "Her, in Tamagotchi mode";
            Controls.Add(petWell);
            int span = wellBottom - PetWellTop - PetRing;
            for (int i = 0; i < PetStats.Names.Length; i++)
            {
                string need = PetStats.Names[i];
                PetRingControl ring = new PetRingControl { Need = need, Bounds = new Rectangle(308, PetWellTop + i * span / (PetStats.Names.Length - 1), PetRing, PetRing) };
                ring.AccessibleName = Cap(need);
                petRings.Add(ring);
                Controls.Add(ring);
            }
            string[,] actions = { { "Feed", "feed" }, { "Gifts", "gift" }, { "Wash", "cleanliness" }, { "Pat", "pat" }, { "Play", "play" } };
            for (int i = 0; i < petTiles.Length; i++)
            {
                int at = i;
                petTiles[i] = new ToolTile { Bounds = new Rectangle(28 + i * (PetTileWide + PetTileGap), PetTilesTop, PetTileWide, PetTile), Font = Font, Text = actions[i, 0] };
                petTiles[i].Shown = new Tool { Id = "pet:" + actions[i, 1], Short = actions[i, 0], Full = actions[i, 0], Icon = actions[i, 1] };
                petTiles[i].AccessibleName = actions[i, 0];
                petTiles[i].Opened += delegate { PetAction(at); };
                Controls.Add(petTiles[i]);
            }
            for (int i = from; i < Controls.Count; i++)
            {
                Control control = Controls[i];
                petLeft.Add(new KeyValuePair<Control, int>(control, control.Left));
                control.Left += Far;
            }
            PetStats.Changed += delegate { if (petShown && IsHandleCreated && !IsDisposed) BeginInvoke(new MethodInvoker(FillPet)); };
        }

        private static string Cap(string word) { return string.IsNullOrEmpty(word) ? "" : char.ToUpperInvariant(word[0]) + word.Substring(1); }

        // Tamagotchi mode on or off. Never the pet game's options, which are on its Toggles row; while
        // Gaming Mode has her on a game's display, the tile waits for the game to end.
        internal void ShowPetMode(bool on)
        {
            if (on == petShown) return;
            if (on && capri.GamingDocked) return;
            if (on && (paged || modded || setuped)) return;
            petShown = on;
            DismissHint();
            capri.PetDock(on);
            if (!on) petWell.Take(null);
            Place();
            ReachMain();
            if (on) FillPet();
            else FillTools();
            Invalidate(true);
        }

        // Her points and her six needs as they stand.
        internal void FillPet()
        {
            if (petWell == null) return;
            ICapriPet pet = capri.Pet;
            petPoints.Text = pet == null ? "" : pet.Heading;
            if (pet != null) petPoints.ForeColor = pet.HeadingColour;
            foreach (PetRingControl ring in petRings)
            {
                ring.Level = PetStats.Level(ring.Need);
                ring.Low = PetStats.Low(ring.Need);
                ring.Invalidate();
                SettingHint hint;
                if (hints.TryGetValue(ring, out hint)) hint.Exact = NeedFacts(ring.Need);
            }
        }

        // Her frame, as she draws it each tick while she's in the well; null clears it.
        internal void PetFrame(Bitmap frame, Rectangle body, bool steady = true)
        {
            if (petWell == null || !petShown) return;
            petWell.Take(frame, body, steady);
        }

        // Her line in the well's bubble, typed out at her pace; null when she's done.
        // Her own speech bubble, drawn by its own code, placed in the well over her; null when it closes.
        internal void PetBubble(SpeechBox box, double now) { if (petWell != null && petShown) petWell.Bubble(box, now); }

        // Frames drawn into the well, for the check that nothing draws there once Tamagotchi mode is closed.
        internal int PetFramesTaken { get { return petWell == null ? 0 : petWell.Taken; } }

        // A ring's tip: its level, and what raises it, the one worth most to her now first.
        internal string NeedFacts(string need)
        {
            List<string> helps = PetStats.Helps(need);
            string facts = capri.PetFacts(need);
            return PetStats.Level(need) + " of 100" + (PetStats.Low(need) ? ", low" : "") + (helps.Count == 0 ? "" : ". Raised by: " + string.Join(", ", helps.ToArray())) + (facts.Length == 0 ? "" : ". " + facts);
        }

        // The rings in her tips, as her other controls are: hover or focus shows the card, with each ring's own words.
        private void ExplainRings()
        {
            SelfKnow.Topic topic = SelfKnow.Setting("Mainframe.cs", "Tamagotchi mode");
            if (topic == null) return;
            List<PetRingControl> every = new List<PetRingControl>(petRings);
            every.AddRange(stripRings);
            foreach (PetRingControl ring in every)
            {
                SettingHint hint = new SettingHint { Control = ring, Topic = topic, Title = Cap(ring.Need), Exact = NeedFacts(ring.Need) };
                hintRows.Add(hint);
                WatchHint(ring, hint);
            }
        }

        // Each ring as it shows ("social 10 low"), for the checks.
        internal string PetRingsShown
        {
            get
            {
                List<string> rings = new List<string>();
                foreach (PetRingControl ring in petRings) rings.Add(ring.Need + " " + ring.Level + (ring.Low ? " low" : ""));
                return string.Join(", ", rings.ToArray());
            }
        }

        internal bool PetBubbleShown { get { return petWell != null && petWell.BubbleShown; } }

        // How many rings her tips know, each with its own words, for the checks.
        internal int RingTips
        {
            get
            {
                int known = 0;
                SettingHint hint;
                foreach (PetRingControl ring in petRings) if (hints.TryGetValue(ring, out hint) && !string.IsNullOrEmpty(hint.Exact) && hint.Exact.IndexOf("of 100", StringComparison.Ordinal) >= 0) known++;
                return known;
            }
        }

        // Her drawn bounds against what the well shows of her, for the check that nothing of hers is cut inside the well.
        internal string PetCut(Bitmap frame)
        {
            if (petWell == null || frame == null) return "no frame";
            Rectangle lit = PetWell.Lit(frame), kept = petWell.Showing(frame.Size);
            if (lit.IsEmpty) return "nothing drawn";
            if (!kept.Contains(lit)) return "drawn " + lit + " outside what the well shows " + kept;
            Rectangle placed = petWell.Placed(kept.Size);
            return petWell.ClientRectangle.Contains(placed) ? null : "placed " + placed + " past the well " + petWell.ClientRectangle;
        }

        // What the need's tip lists, for the checks.
        internal string NeedTip(string need) { return string.Join(", ", PetStats.Helps(need).ToArray()); }

        // Feed, Gifts and Play drop their lists from the tile; Wash washes her through the pet game, Pat pats her.
        private void PetAction(int at)
        {
            ToolTile tile = petTiles[at];
            Point under = new Point(0, tile.Height);
            if (at == 0 || at == 1) ListFrom(capri.PetList(at == 0 ? "feed" : "gift"), tile, under);
            else if (at == 2) { foreach (ToolStripItem one in capri.NeedItems("cleanliness")) if (one.Available && one.Enabled) { one.PerformClick(); break; } }
            else if (at == 3) capri.PatFromPet();
            else MiniChoices().Show(tile, under);
        }

        // Her Feed or Give gift list as Play's is: her own dropdown under the tile, each choice pressing hers, so it opens at
        // the tile and never where her body last stood.
        internal ContextMenuStrip PetListShown;

        // A tile pressed, and its bounds on screen, for the checks.
        internal void PetPress(int at) { PetAction(at); }

        internal Rectangle PetTileOnScreen(int at) { return petTiles[at].RectangleToScreen(petTiles[at].ClientRectangle); }

        private void ListFrom(ToolStripMenuItem from, Control tile, Point under)
        {
            if (from != null) ListOf(from.DropDownItems, tile, under);
        }

        private void ListOf(System.Collections.IEnumerable items, Control tile, Point under)
        {
            ContextMenuStrip list = Dropdown();
            foreach (ToolStripItem one in items)
            {
                if (!one.Available) continue;
                if (one is ToolStripSeparator) { list.Items.Add(new ToolStripSeparator()); continue; }
                ToolStripMenuItem hers = one as ToolStripMenuItem;
                if (hers == null) continue;
                ToolStripMenuItem choice = Choice(hers.Text, hers.Checked, delegate { hers.PerformClick(); });
                choice.Enabled = hers.Enabled;
                choice.ShortcutKeyDisplayString = hers.ShortcutKeyDisplayString;
                choice.ShowShortcutKeys = hers.ShowShortcutKeys;
                choice.ToolTipText = hers.ToolTipText;
                list.Items.Add(choice);
            }
            list.Closed += delegate { if (PetListShown == list) PetListShown = null; };
            PetListShown = list;
            list.Show(tile, under);
        }

        // The well: her drawn frame scaled to fit, her line in a bubble at its top.
        internal sealed class PetWell : Control
        {
            private Bitmap frame;
            private Rectangle body;
            private Bitmap bubble;
            private Point bubbleAt;
            internal bool BubbleShown { get { return bubble != null; } }
            internal int Taken;

            internal PetWell()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
                Disposed += delegate { if (bubble != null) bubble.Dispose(); };
            }

            internal void Take(Bitmap drawn, Rectangle drawnBody = default(Rectangle), bool steady = true)
            {
                frame = drawn;
                body = drawnBody;
                passing = Rectangle.Empty;
                if (drawn == null) { seen = Rectangle.Empty; seenOf = Size.Empty; }
                else
                {
                    Taken++;
                    // What she draws in her steady loops, measured now and then and only ever grown, so her hair, her glow and
                    // her music notes all fit and she doesn't shift about as they come and go.
                    if (drawn.Size != seenOf) { seen = Rectangle.Empty; seenOf = drawn.Size; }
                    if (steady && (seen.IsEmpty || Taken % 8 == 0)) Grow(drawn);
                    // A glitch, an arrival or a leave reaches further: each such frame is fitted whole for itself.
                    else if (!steady) passing = Lit(drawn);
                }
                Invalidate();
            }

            // This frame's own reach while she isn't in a steady loop.
            private Rectangle passing;

            // What the well shows of her frame now.
            internal Rectangle Showing(Size of)
            {
                Rectangle all = seen.IsEmpty ? passing : passing.IsEmpty ? seen : Rectangle.Union(seen, passing);
                if (all.IsEmpty) return new Rectangle(Point.Empty, of);
                return Rectangle.Intersect(Rectangle.Inflate(all, 2, 2), new Rectangle(Point.Empty, of));
            }

            // Her drawing with its outer edge faded, so whatever reaches the border of what is shown fades out rather than
            // ending in a straight cut.
            private static Bitmap Feathered(Bitmap source, Rectangle from, Size size)
            {
                Bitmap made = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(made))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(source, new Rectangle(Point.Empty, made.Size), from, GraphicsUnit.Pixel);
                }
                int fade = Math.Max(4, Math.Min(made.Width, made.Height) / 16);
                System.Drawing.Imaging.BitmapData data = made.LockBits(new Rectangle(Point.Empty, made.Size), System.Drawing.Imaging.ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try
                {
                    int[] row = new int[made.Width];
                    for (int y = 0; y < made.Height; y++)
                    {
                        IntPtr at = new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride);
                        System.Runtime.InteropServices.Marshal.Copy(at, row, 0, made.Width);
                        int fromEdgeY = Math.Min(y, made.Height - 1 - y);
                        for (int x = 0; x < made.Width; x++)
                        {
                            int near = Math.Min(fromEdgeY, Math.Min(x, made.Width - 1 - x));
                            if (near >= fade) continue;
                            int alpha = (row[x] >> 24) & 0xFF;
                            alpha = alpha * near / fade;
                            row[x] = (alpha << 24) | (row[x] & 0xFFFFFF);
                        }
                        System.Runtime.InteropServices.Marshal.Copy(row, 0, at, made.Width);
                    }
                }
                finally { made.UnlockBits(data); }
                return made;
            }

            // Everything she has drawn so far in this Tamagotchi mode, in her frame's pixels.
            private Rectangle seen;
            private Size seenOf;
            internal Rectangle Seen { get { return seen; } }

            private void Grow(Bitmap drawn)
            {
                Rectangle lit = Lit(drawn);
                if (lit.IsEmpty) return;
                seen = seen.IsEmpty ? lit : Rectangle.Union(seen, lit);
            }

            // The box round every pixel of hers that shows.
            internal static Rectangle Lit(Bitmap drawn)
            {
                int left = drawn.Width, top = drawn.Height, right = -1, bottom = -1;
                System.Drawing.Imaging.BitmapData data = null;
                try
                {
                    data = drawn.LockBits(new Rectangle(Point.Empty, drawn.Size), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    int[] row = new int[drawn.Width];
                    for (int y = 0; y < drawn.Height; y++)
                    {
                        System.Runtime.InteropServices.Marshal.Copy(new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride), row, 0, drawn.Width);
                        for (int x = 0; x < drawn.Width; x++)
                        {
                            if (((row[x] >> 24) & 0xFF) < 8) continue;
                            if (x < left) left = x;
                            if (x > right) right = x;
                            if (y < top) top = y;
                            bottom = y;
                        }
                    }
                }
                catch (Exception caughtOne) { Brain.Caught(caughtOne); return Rectangle.Empty; }
                finally { if (data != null) drawn.UnlockBits(data); }
                return right < 0 ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
            }

            // Where her drawing lands in the well: all of it, a margin inside the well's border, below her bubble.
            internal Rectangle Placed(Size of)
            {
                float margin = Math.Max(8, Width / 24f), top = Math.Max(margin, Height * 0.18f);
                float room = Math.Min((Width - 2 * margin) / of.Width, (Height - top - margin) / of.Height);
                Size size = new Size(Math.Max(1, (int)(of.Width * room)), Math.Max(1, (int)(of.Height * room)));
                return new Rectangle((Width - size.Width) / 2, (int)(Height - margin) - size.Height, size.Width, size.Height);
            }

            // Her bubble from its own drawing, over her head inside the well, or gone with null.
            internal void Bubble(SpeechBox box, double now)
            {
                Bitmap was = bubble;
                bubble = null;
                if (box != null && frame != null)
                {
                    Rectangle from = seen.IsEmpty ? new Rectangle(Point.Empty, frame.Size) : Rectangle.Inflate(seen, 2, 2);
                    Rectangle her = Placed(from.Size);
                    Rectangle area = Rectangle.Inflate(ClientRectangle, -6, -6);
                    float s = Math.Max(0.6f, Math.Min(1.2f, Width / 280f));
                    try { bubble = box.Picture(now, s, new Point(her.Left + her.Width / 2, her.Top), new Point(her.Left + her.Width / 2, her.Bottom), area, out bubbleAt); }
                    catch (Exception caughtOne) { Brain.Caught(caughtOne); }
                    // The bubble works in screen points; here they are the well's own.
                }
                if (was != null) was.Dispose();
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float s = Math.Max(1f, Height / 400f), cut = 10 * s;
                RectangleF box = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
                using (GraphicsPath edge = Neon.Cut(box, cut, 0, cut, 0))
                using (Brush fill = new SolidBrush(Neon.Well))
                using (Pen rim = new Pen(Neon.Edge, 1))
                {
                    g.FillPath(fill, edge);
                    g.DrawPath(rim, edge);
                }
                using (Pen hot = new Pen(Neon.Hot, 2))
                {
                    g.DrawLine(hot, box.Left, box.Top + cut, box.Left + cut, box.Top);
                    g.DrawLine(hot, box.Right - cut, box.Bottom, box.Right, box.Bottom - cut);
                }
                Bitmap shown = frame;
                if (shown != null)
                {
                    Rectangle from = Showing(shown.Size);
                    if (from.Width > 0 && from.Height > 0)
                    {
                        Rectangle to = Placed(from.Size);
                        try { using (Bitmap soft = Feathered(shown, from, to.Size)) g.DrawImage(soft, to.Location); }
                        catch (Exception caughtOne) { Brain.Caught(caughtOne); }
                    }
                }
                Bitmap said = bubble;
                if (said != null) g.DrawImage(said, bubbleAt.X, bubbleAt.Y, said.Width, said.Height);
            }
        }

        // One need: its picture in a ring filled to its level, red under her low mark; her tip card shows what raises it.
        internal sealed class PetRingControl : Control
        {
            internal string Need;
            internal int Level;
            internal bool Low;

            internal PetRingControl()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true);
                TabStop = true;
                Cursor = Cursors.Hand;
                AccessibleRole = AccessibleRole.ProgressBar;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Neon.Ground);
                float s = Width / (float)PetRing, thick = 4 * s;
                RectangleF round = new RectangleF(thick / 2 + 1, thick / 2 + 1, Width - thick - 2, Height - thick - 2);
                using (Brush fill = new SolidBrush(Neon.Well)) g.FillEllipse(fill, round);
                using (Pen track = new Pen(Neon.Edge, 1)) g.DrawEllipse(track, round);
                using (Pen arc = new Pen(Low ? Neon.Hot : Neon.Cyan, thick))
                    if (Level > 0) g.DrawArc(arc, round, -90, 360f * Math.Min(100, Level) / 100f);
                float icon = 24 * s;
                GraphicsState state = g.Save();
                g.TranslateTransform((Width - icon) / 2, (Height - icon) / 2);
                ToolIcon.Draw(g, Need, icon, false, Neon.Well);
                g.Restore(state);
                AccessibleDescription = Level + " of 100" + (Low ? ", low" : "");
            }
        }
    }
}
