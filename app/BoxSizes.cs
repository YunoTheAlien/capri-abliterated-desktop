using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Companion
    {
#if CHECKS
        // Each strip at each size: a frame's drawn extent against the area the next frame clears. Anything past it stays in
        // her canvas as slices. The table goes to box-sizes.md; a frame over its area fails.
        private void SmokeBoxSizes(List<string> checks, string output, int fromStep, int toStep, int firstStrip = 0, int lastStrip = 99)
        {
            Clip[] strips = { idle, idleBlink, grin, glitch, dangle, sleep, wake, walk, arrive, leave, perch, perchTalk, perchBlink, perchGlitch, perchDance, perchArrive, perchLeave, laydown, laydownTalk, laydownSleep, laydownGlitch, laydownLeave };
            string table = Path.Combine(output, "box-sizes.md");
            List<string> rows = new List<string>();
            if (!File.Exists(table)) { rows.Add("| Strip | Size | Drawn (x, y, w, h) | Area | Over by |"); rows.Add("|---|---|---|---|---|"); }
            List<string> over = new List<string>();
            int sizeWas = LevelOf("size");
            try
            {
                for (int step = fromStep; step <= Math.Min(toStep, OwnSizes.Length - 1); step++)
                {
                    SetLevel("size", step);
                    for (int which = firstStrip; which <= Math.Min(lastStrip, strips.Length - 1); which++)
                    {
                        Clip strip = strips[which];
                        Play(strip);
                        Rectangle drawnAll = Rectangle.Empty, areaAll = Rectangle.Empty;
                        int worst = 0;
                        for (int look = 0; look < 3; look++)
                        {
                            for (int i = 0; i < 2; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
                            RenderFrame();
                            Bitmap now = frame;
                            if (now == null || lastContent.IsEmpty) continue;
                            Rectangle lit = Mainframe.PetWell.Lit(now), area = Rectangle.Inflate(lastContent, 4, 4);
                            if (lit.IsEmpty) continue;
                            drawnAll = drawnAll.IsEmpty ? lit : Rectangle.Union(drawnAll, lit);
                            areaAll = areaAll.IsEmpty ? area : Rectangle.Union(areaAll, area);
                            int past = Math.Max(Math.Max(area.Left - lit.Left, lit.Right - area.Right), Math.Max(area.Top - lit.Top, lit.Bottom - area.Bottom));
                            worst = Math.Max(worst, past);
                        }
                        if (drawnAll.IsEmpty) continue;
                        rows.Add("| " + strip.Title + " | " + OwnSizes[step] + " | " + drawnAll.X + ", " + drawnAll.Y + ", " + drawnAll.Width + ", " + drawnAll.Height
                            + " | " + areaAll.Width + " x " + areaAll.Height + " | " + (worst > 0 ? worst + " px" : "under") + " |");
                        if (worst > 0) over.Add(strip.Title + " at " + OwnSizes[step] + " by " + worst + " px");
                    }
                }
            }
            finally { SetLevel("size", sizeWas); Play(idle); }
            try { File.AppendAllLines(table, rows.ToArray()); } catch (IOException caughtOne) { Brain.Caught(caughtOne); }
            if (over.Count > 0) throw new Exception("Frames drew past the area they were given (box-sizes.md): " + string.Join("; ", over.ToArray()));
            checks.Add("PASS sizes " + OwnSizes[fromStep] + " to " + OwnSizes[Math.Min(toStep, OwnSizes.Length - 1)] + ": every strip draws inside the area its frame was given, so nothing stays in her canvas to show later (box-sizes.md)");
        }
        // Her close-up at its fullest, at the size she starts from now: her window on the screen, and nothing of her
        // drawing cut at its bottom edge, which is where her feet are. Null when right.
        private string FeetCloseCheck(Func<string> atHold = null)
        {
            closeUp = true; closeZoom = 1f; closeWasSize = preferences.Size; closeWasAt = anchor; closeStep = 0;
            closeAt = clock.Elapsed.TotalSeconds; closeBack = -1;
            string wrong = null;
            try
            {
                System.Diagnostics.Stopwatch waited = System.Diagnostics.Stopwatch.StartNew();
                while (closeBack < 0 && waited.Elapsed.TotalSeconds < 20) { CloseTick(clock.Elapsed.TotalSeconds); Application.DoEvents(); System.Threading.Thread.Sleep(40); }
                RenderFrame();
                Rectangle screen = Screen.FromPoint(new Point(anchor.X + Width / 2, anchor.Y + Height / 2)).WorkingArea;
                Rectangle lit = frame == null ? Rectangle.Empty : Mainframe.PetWell.Lit(frame);
                if (closeBack < 0) wrong = "it never reached its hold";
                else if (lit.IsEmpty) wrong = "nothing drawn at its fullest";
                else if (lit.Bottom >= frame.Height - 1) wrong = "her lowest drawing runs off the window's bottom (" + lit + " in " + frame.Size + ")";
                else if (anchor.Y + lit.Bottom > screen.Bottom) wrong = "her lowest drawing is under the screen's bottom (" + (anchor.Y + lit.Bottom) + " past " + screen.Bottom + ")";
                else if (atHold != null) wrong = atHold();
            }
            finally
            {
                closeBack = clock.Elapsed.TotalSeconds - 1; closeAt = 0;
                System.Diagnostics.Stopwatch back = System.Diagnostics.Stopwatch.StartNew();
                while (closeUp && back.Elapsed.TotalSeconds < 20) { CloseTick(clock.Elapsed.TotalSeconds); Application.DoEvents(); System.Threading.Thread.Sleep(40); }
            }
            return wrong;
        }

        // Capri Stream at each size: every cell of every strip she has, drawn as on the desktop, inside her capture area, and the
        // area no wider or taller than those cells need past its margins.
        private void SmokeStreamFit(List<string> checks, int fromStep, int toStep)
        {
            Clip[] strips = Clip.Every();
            int sizeWas = LevelOf("size");
            List<string> wrong = new List<string>(), sizes = new List<string>();
            try
            {
                for (int step = fromStep; step <= Math.Min(toStep, OwnSizes.Length - 1); step++)
                {
                    SetLevel("size", step);
                    Rectangle area = StreamArea(), drawn = Rectangle.Empty;
                    using (Bitmap canvas = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
                        foreach (Clip strip in strips)
                            foreach (bool left in new[] { false, true })
                                for (int cell = 0; cell < strip.Count; cell++)
                                {
                                    bool facingWas = facingLeft;
                                    facingLeft = left;
                                    try
                                    {
                                        using (Graphics paper = Graphics.FromImage(canvas)) { paper.Clear(Color.Transparent); Draw(paper, strip, cell, -1, null, false); }
                                    }
                                    finally { facingLeft = facingWas; }
                                    Rectangle lit = Mainframe.PetWell.Lit(canvas);
                                    if (lit.IsEmpty) continue;
                                    drawn = drawn.IsEmpty ? lit : Rectangle.Union(drawn, lit);
                                    if (!area.Contains(lit) && wrong.Count < 12) wrong.Add(strip.Title + (left ? " left" : "") + " cell " + cell + " at " + OwnSizes[step] + ": " + lit + " past " + area);
                                }
                    // Her glitch's reach each side, which these cells are drawn without, and a few pixels of her art's smoothing.
                    int spare = 2 * (UnderReach() + 3 + StreamMargin) + 16;
                    if (!drawn.IsEmpty && (area.Width - drawn.Width > spare || area.Bottom - drawn.Bottom > 3 + StreamMargin + 2)) wrong.Add("at " + OwnSizes[step] + " the area " + area + " is bigger than her cells need, " + drawn);
                    sizes.Add(OwnSizes[step] + ": " + area.Width + " x " + area.Height);
                }
            }
            finally { SetLevel("size", sizeWas); Play(idle); }
            if (wrong.Count > 0) throw new Exception("Her capture area doesn't fit her: " + string.Join("; ", wrong.ToArray()));
            checks.Add("PASS Capri Stream holds every cell of every strip facing either way, no bigger than they need (" + string.Join(", ", sizes.ToArray()) + ")");
        }

        // Her bubble with the capture window open, short and long, her at each edge of her screen: wherever her own rule
        // puts it, all of it is in the picture, as she is, and the window grew rather than moved it.
        private void SmokeStreamBubble(List<string> checks)
        {
            Point anchorWas = anchor;
            bool opened = streamWindow == null || streamWindow.IsDisposed;
            if (opened) SetStreamWindow();
            List<string> wrong = new List<string>();
            try
            {
                Rectangle room = Screen.FromPoint(new Point(anchor.X + Width / 2, anchor.Y + Height / 2)).WorkingArea;
                Point[] spots = { new Point(room.Left, room.Top + room.Height / 2 - Height / 2), new Point(room.Right - Width, room.Top + room.Height / 2 - Height / 2), new Point(room.Left + room.Width / 2 - Width / 2, room.Top), new Point(room.Left + room.Width / 2 - Width / 2, room.Bottom - Height) };
                string[] edges = { "left", "right", "top", "bottom" };
                string[] lines = { "Hi chat.", "This one runs long on purpose, so her bubble has to wrap over several lines and grow taller and wider than a short hello ever would." };
                for (int e = 0; e < spots.Length; e++)
                    foreach (string line in lines)
                    {
                        anchor = spots[e];
                        Location = anchor;
                        Remark("", line, true);
                        for (int i = 0; i < 6; i++) { System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(30); }
                        RenderFrame();
                        if (box == null || !box.Visible) { wrong.Add(edges[e] + ": no bubble"); continue; }
                        Rectangle said = new Rectangle(box.Left - anchor.X, box.Top - anchor.Y, box.Width, box.Height);
                        string which = edges[e] + (line.Length > 20 ? " long" : " short");
                        if (!streamWindow.Holds.Contains(said)) wrong.Add(which + ": bubble " + said + " outside " + streamWindow.Holds);
                        if (!streamWindow.OneToOne) wrong.Add(which + ": fitted down");
                    }
                anchor = anchorWas;
                Location = anchor;
                string feet = FeetCloseCheck(delegate
                {
                    Rectangle lit = Mainframe.PetWell.Lit(frame);
                    return streamWindow.Holds.Contains(lit) ? null : "her close-up " + lit + " outside " + streamWindow.Holds;
                });
                if (feet != null) wrong.Add(feet);
            }
            finally
            {
                anchor = anchorWas;
                Location = anchor;
                if (opened && streamWindow != null && !streamWindow.IsDisposed) SetStreamWindow();
            }
            if (wrong.Count > 0) throw new Exception("Her bubble in Capri Stream: " + string.Join("; ", wrong.ToArray()));
            checks.Add("PASS her bubble in Capri Stream, short and long, her at each edge of her screen: all of it in the picture where her own rule put it, drawn as she is; her feet close-up whole in it too");
        }

        // Her glitch's Under at a size, drawn as Draw draws it (her window's canvas, its quality settings, turned and
        // facing left): the middle of nine runs in ms.
        private double UnderSpeed(int size, Clip clip)
        {
            Bitmap strip = Art(clip);
            int width = strip.Width / clip.Count;
            float tall = size * clip.Height, wide = tall * width / strip.Height;
            Size canvas = CanvasSize(size);
            List<double> took = new List<double>();
            using (Bitmap paper = new Bitmap(canvas.Width, canvas.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
                for (int run = 0; run < 9; run++)
                    using (Graphics graphics = Graphics.FromImage(paper))
                    {
                        graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                        graphics.TranslateTransform(canvas.Width / 2f, canvas.Height / 2f);
                        graphics.RotateTransform(1.5f);
                        graphics.ScaleTransform(-1, 1);
                        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                        Shatter.Under(graphics, strip, new Rectangle(0, 0, width, strip.Height), new RectangleF(-wide / 2f, -tall / 2f, wide, tall), 1, run + 1);
                        took.Add(watch.Elapsed.TotalMilliseconds);
                    }
            took.Sort();
            return took[took.Count / 2];
        }

        // A whole frame of a strip at a size, render and show: the middle of five in ms.
        private double FrameSpeed(int step, Clip clip)
        {
            int sizeWas = LevelOf("size");
            List<double> took = new List<double>();
            try
            {
                SetLevel("size", step);
                Play(clip);
                for (int run = 0; run < 5; run++)
                {
                    System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                    RenderFrame();
                    took.Add(watch.Elapsed.TotalMilliseconds);
                }
            }
            finally { SetLevel("size", sizeWas); Play(idle); }
            took.Sort();
            return took[took.Count / 2];
        }
#endif
    }
}
