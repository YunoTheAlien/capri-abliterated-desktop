using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Everything sys.md lets them set about her screensaver, read once when it opens so a half-edited file
    // never lands on screen mid-change.
    internal sealed class SaverLook
    {
        internal const string Terminals = "terminals", Code = "code", Rain = "rain", Hexes = "hexes";
        internal static readonly string[] Styles = { Terminals, Hexes, Code, Rain };

        internal string Style = Terminals;
        internal Color Ink = Neon.Cyan;
        internal Color Ground = Color.FromArgb(4, 6, 10);
        internal float Size = 1f, Speed = 1f;
        internal byte Solid = 255;
        internal bool EveryScreen = true;
        // How hard it may flash: 0 holds steady (what reduced motion gets), 1 is fastest, and no hexagon ever passes
        // three flashes a second.
        internal float Flash = 1f;
        internal string Sound;
        internal int Volume = 20;

        internal static SaverLook Read() { return Read(null); }

        // One of hers, or the one asked for by name when it is being shown on purpose.
        internal static SaverLook Read(string asked)
        {
            SaverLook look = new SaverLook();
            string style = (asked ?? SysLines.Of(SysLines.SaverStyleKey) ?? "").Trim();
            foreach (string one in Styles) if (style.Equals(one, StringComparison.OrdinalIgnoreCase)) look.Style = one;
            // Hexagons are a red alert panel, so red is what they are unless a colour is written down.
            look.Ink = SysLines.Colour(SysLines.SaverColourKey, look.Style == Hexes ? Color.FromArgb(255, 30, 38) : Neon.Cyan);
            look.Ground = SysLines.Colour(SysLines.SaverGroundKey, Color.FromArgb(4, 6, 10));
            look.Size = Step(SysLines.Of(SysLines.SaverSizeKey), "small", 0.72f, "large", 1.5f, 0.4f, 3f);
            look.Speed = Step(SysLines.Of(SysLines.SaverSpeedKey), "slow", 0.45f, "fast", 2.4f, 0.2f, 6f);
            look.Solid = Clear(SysLines.Of(SysLines.SaverThroughKey));
            look.EveryScreen = !string.Equals((SysLines.Of(SysLines.SaverScreensKey) ?? "").Trim(), "main", StringComparison.OrdinalIgnoreCase);
            string flash = (SysLines.Of(SysLines.SaverFlashKey) ?? "").Trim();
            look.Flash = flash.Equals("off", StringComparison.OrdinalIgnoreCase) || flash.Equals("0", StringComparison.Ordinal) ? 0f
                : flash.Equals("gentle", StringComparison.OrdinalIgnoreCase) || flash.Equals("slow", StringComparison.OrdinalIgnoreCase) ? 0.3f : 1f;
            look.Sound = SysLines.SaverSound;
            look.Volume = SysLines.SaverVolume;
            return look;
        }

        // A word they know, or a plain number for anyone who wants to be exact. Anything else is the middle.
        private static float Step(string said, string low, float lowAt, string high, float highAt, float least, float most)
        {
            string one = (said ?? "").Trim();
            if (one.Length == 0) return 1f;
            if (one.Equals(low, StringComparison.OrdinalIgnoreCase)) return lowAt;
            if (one.Equals(high, StringComparison.OrdinalIgnoreCase)) return highAt;
            if (one.Equals("normal", StringComparison.OrdinalIgnoreCase) || one.Equals("medium", StringComparison.OrdinalIgnoreCase)) return 1f;
            float made;
            if (!float.TryParse(one, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out made)) return 1f;
            return Math.Max(least, Math.Min(most, made));
        }

        // How much of the desktop shows through, 0 to 100, as the solidity the layered window wants.
        private static byte Clear(string said)
        {
            int through;
            if (!int.TryParse((said ?? "").Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out through)) return 255;
            through = Math.Max(0, Math.Min(100, through));
            return (byte)Math.Max(30, 255 - through * 225 / 100);
        }
    }

    // Her screensaver while the PC is left alone: Terminals (made-up output, nothing of theirs), Hexes, Code or Rain.
    // Off unless sys.md turns it on, styled from that file. Space or Escape ends it, not the mouse; see Woken.
    internal sealed class Screensaver : Overlay
    {
        private const double TypedPerSecond = 46, HoldFullSeconds = 5;
        // A window opens this far apart at the start and this far apart once it is going.
        private const double FirstGap = 1.1, LastGap = 0.34, RampSeconds = 14;
        // Columns of code, for the plain style.
        private const double SlowStart = 0.75, FastEnd = 0.05, CodeRamp = 90;
        private const string RainFace = "01</>{}[]()=+-*&^%$#@!?;:~ABCDEFGHJKLMNPQRSTUVWXYZ";

        private readonly Rectangle room;
        private readonly SaverLook look;
        private readonly Random luck;
        private readonly System.Windows.Forms.Timer ticks = new System.Windows.Forms.Timer();
        private readonly List<Box> boxes = new List<Box>();
        private readonly List<Row> rows = new List<Row>();
        private readonly Drop[] drops;
        private readonly Cell[] cells;
        private readonly List<int> edge = new List<int>();
        private readonly int across, down;
        private readonly float reach, stepX, stepY;
        private readonly Font face, label, tag, way;
        private readonly int lineHeight, columnWidth, lines, columns;
        private readonly int[] filled;
        private readonly bool[,] covered = new bool[48, 27];
        private readonly float letter;

        // Made once and kept. StringFormat.GenericTypographic is a property that builds a new one on every
        // read, so a local taken once a frame was a drawing object leaked twenty times a second for as
        // long as she was up, and the middle-aligned one was built outright.
        private readonly StringFormat exact = new StringFormat(Look.Exact);
        private readonly StringFormat middle = new StringFormat(Look.Exact);

        private Native.Surface surface;
        private double age, nextThing, fullAt = -1;
        // Wall time since it opened, which is not age: age is scaled by their speed setting, so a fade built
        // on it would take two and a half seconds at double speed and ten at half.
        private double shown;
        private int alight;
        private bool drawn;
        private Point woke;

        // What holds it back and takes it down, never the idle clock: time left alone puts it up and only a deliberate
        // key takes it away.
        internal Func<bool> Over = delegate { return false; };

        // Nothing dismisses it for this long after it opens. The click that asks for it from her menu is an
        // input like any other, and without this it would close on the very click that asked.
        internal double Settles = 0.6;

        // Their reduced motion setting. Off, the screen is dark on the first frame rather than fading.
        internal bool Motion = true;

        internal const string Way = "[ Spacebar to exit ]";
        internal const string AnyWay = "[ Any key to exit ]";

        // A prank ends on any key, since nobody asked for it, but still never on the mouse, so a nudged desk doesn't
        // end it.
        internal bool AnyKey;

        // Seconds it gives itself, or nought to stay until it is sent away. Her alert is a few seconds of
        // noise, not a thing somebody has to go and turn off.
        internal double Ends;

        // One key only: space, or Escape; movement doesn't end it, since a leant-on desk would. The way out is on
        // screen.
        private bool Woken()
        {
            if (age < Settles) return false;
            try
            {
                if (!AnyKey) return (Native.GetAsyncKeyState(0x20) & 0x8000) != 0 || (Native.GetAsyncKeyState(0x1B) & 0x8000) != 0;
                // Every key, from Backspace up, which leaves the mouse buttons below it alone: they are 1, 2,
                // 4, 5 and 6, and a click is not a key.
                for (int key = 0x08; key <= 0xFE; key++) if ((Native.GetAsyncKeyState(key) & 0x8000) != 0) return true;
                return false;
            }
            catch (Exception) { return false; }
        }

        // The way out, on a plate at the bottom of every screen it covers.
        private void Exit(Graphics graphics)
        {
            // Never disposed: it comes from the cache and is shared with everything else drawing at that size.
            Font font = way;
            if (font == null) return;
            string Way = AnyKey ? AnyWay : Screensaver.Way;
            SizeF said = graphics.MeasureString(Way, font);
            float pad = said.Height * 0.7f;
            RectangleF plate = new RectangleF(
                (room.Width - said.Width - pad * 2) / 2f,
                room.Height - said.Height - pad * 3,
                said.Width + pad * 2,
                said.Height + pad);
            using (SolidBrush back = new SolidBrush(Color.FromArgb(255, 0, 0, 0)))
            using (SolidBrush ink = new SolidBrush(look.Ink))
            using (Pen edge = new Pen(look.Ink, Math.Max(1f, said.Height / 14f)))
            {
                graphics.FillRectangle(back, plate);
                graphics.DrawRectangle(edge, plate.X, plate.Y, plate.Width, plate.Height);
                graphics.DrawString(Way, font, ink, plate.X + pad, plate.Y + pad / 2);
            }
        }

        // One of the little windows: where it is, what it calls itself, what it has written and what it is
        // writing now.
        private sealed class Box
        {
            internal Rectangle At;
            internal string Title;
            internal readonly List<string> Said = new List<string>();
            internal string Typing = "";
            internal int Shown, Rows;
            internal double Next;
            internal bool Bar;
            internal double Done;
        }

        private sealed class Row
        {
            internal string Text;
            internal int X, Y, Shown;
            internal double Born;
        }

        // One falling column: where its head is, how fast it goes, how long its tail is and the characters in it.
        private sealed class Drop
        {
            internal double Head, Rate, Wakes;
            internal int Tail;
            internal char[] Face;
        }

        // One hexagon on the grid: whether it has lit, when, whether it is a solid one or an outline, and the
        // word in it if it has one.
        private sealed class Cell
        {
            internal bool Lit, Solid;
            internal double Born;
            internal string Said;
            internal bool Arrow, Hard;
            // Its own flashing: how fast and where in the beat it started, so no two are in step. Nought is a
            // cell that does not flash at all, which the flashing ones need to be read against.
            internal double Rate, Phase;
        }

        internal Screensaver(Rectangle area, SaverLook how, int seed)
            : base("Capri screensaver")
        {
            room = area;
            look = how ?? new SaverLook();
            luck = new Random(seed);
            // Big enough to read from across a room, small enough that a wide screen holds plenty of it.
            int size = (int)Math.Round(Math.Max(13, Math.Min(22, room.Height / 70)) * look.Size);
            size = Math.Max(8, Math.Min(90, size));
            face = Fonts.Mono(size, FontStyle.Regular, GraphicsUnit.Pixel);
            label = Fonts.Mono(Math.Max(7, size - 2), FontStyle.Regular, GraphicsUnit.Pixel);
            letter = size * 0.6f;
            // Hexagons are drawn big, the way an alert panel has them, so a screen holds a few hundred rather
            // than a few thousand and every one of them can be filled and lettered every frame.
            reach = Math.Max(22f, room.Height / 26f) * look.Size;
            stepX = reach * 1.5f;
            stepY = (float)(Math.Sqrt(3) * reach);
            tag = Fonts.Mono(Math.Max(7, (int)Math.Round(reach * 0.19f)), FontStyle.Bold, GraphicsUnit.Pixel);
            way = Fonts.Mono(Math.Max(11, (int)Math.Round(room.Height / 62f)), FontStyle.Bold, GraphicsUnit.Pixel);
            across = Math.Max(1, (int)(room.Width / stepX) + 2);
            down = Math.Max(1, (int)(room.Height / stepY) + 2);
            if (look.Style == SaverLook.Hexes)
            {
                cells = new Cell[across * down];
                for (int one = 0; one < cells.Length; one++) cells[one] = new Cell();
                Seed(5);
            }
            lineHeight = (int)Math.Ceiling(size * 1.35);
            columnWidth = (int)Math.Ceiling(letter * 54);
            lines = Math.Max(1, (room.Height - lineHeight) / lineHeight);
            columns = Math.Max(1, room.Width / columnWidth);
            filled = new int[columns];
            if (look.Style == SaverLook.Rain)
            {
                int wide = Math.Max(1, (int)Math.Ceiling(letter));
                drops = new Drop[Math.Max(1, room.Width / wide)];
                for (int one = 0; one < drops.Length; one++) drops[one] = Woken(true);
            }
            // The centred one, set here since a field cannot. Used for the word inside a hexagon.
            middle.Alignment = StringAlignment.Center;
            middle.LineAlignment = StringAlignment.Center;
            ticks.Interval = 50;
            ticks.Tick += delegate
            {
                try { Advance(ticks.Interval / 1000.0); }
                catch (Exception) { Close(); }
            };
        }

        internal bool Running { get { return ticks.Enabled; } }
        // How much it has put up, for anything that wants to look without a screen.
        internal int Written { get { return look.Style == SaverLook.Rain ? drops.Length : look.Style == SaverLook.Code ? rows.Count : boxes.Count; } }
        internal bool Drawn { get { return drawn; } }

        internal bool Open()
        {
            if (Over()) return false;
            Bounds = room;
            try { woke = Cursor.Position; } catch (Exception) { woke = Point.Empty; }
            surface = new Native.Surface(room.Width, room.Height);
            Render();
            if (Over()) return false;
            Show();
            Native.SetWindowPos(Handle, new IntPtr(-1), room.X, room.Y, room.Width, room.Height, 0x0010);
            ticks.Start();
            return true;
        }

        internal void Advance(double step)
        {
            if (IsDisposed || surface == null) return;
            shown += step;
            age += step * look.Speed;
            if (Over() || Woken() || (Ends > 0 && age >= Ends)) { Close(); return; }
            if (look.Style == SaverLook.Rain) { Falling(step * look.Speed); Render(); return; }
            if (look.Style == SaverLook.Code) { Coding(); return; }
            if (look.Style == SaverLook.Hexes) { Spreading(); return; }
            Terminals();
        }

        // The screen fills with windows, holds while it is full so it can be seen, then clears and goes again.
        private void Terminals()
        {
            if (fullAt >= 0)
            {
                if (age - fullAt < HoldFullSeconds) { Render(); return; }
                boxes.Clear();
                Array.Clear(covered, 0, covered.Length);
                fullAt = -1;
                nextThing = age;
            }
            if (age >= nextThing && fullAt < 0)
            {
                if (Room()) { boxes.Add(Opened()); nextThing = age + Gap(FirstGap, LastGap, RampSeconds); }
                else fullAt = age;
            }
            foreach (Box box in boxes) Typing(box);
            Render();
        }

        // Still somewhere worth putting a window: a tenth of the screen has nothing on it yet.
        private bool Room()
        {
            if (boxes.Count >= 40) return false;
            int free = 0;
            for (int x = 0; x < covered.GetLength(0); x++)
                for (int y = 0; y < covered.GetLength(1); y++)
                    if (!covered[x, y]) free++;
            return free * 10 > covered.Length;
        }

        private Box Opened()
        {
            int wide = (int)(letter * luck.Next(30, 66)) + 16;
            int rows = luck.Next(5, 15);
            int tall = rows * lineHeight + lineHeight * 2;
            wide = Math.Min(wide, Math.Max(120, room.Width - 20));
            tall = Math.Min(tall, Math.Max(90, room.Height - 20));
            Box box = new Box
            {
                At = new Rectangle(luck.Next(8, Math.Max(9, room.Width - wide - 8)), luck.Next(8, Math.Max(9, room.Height - tall - 8)), wide, tall),
                Title = Fake.Title(luck),
                Rows = rows,
                Bar = luck.Next(3) == 0,
                Typing = Fake.Line(luck).TrimStart()
            };
            Mark(box.At);
            return box;
        }

        private void Mark(Rectangle at)
        {
            int across = covered.GetLength(0), down = covered.GetLength(1);
            int left = Math.Max(0, at.Left * across / Math.Max(1, room.Width));
            int right = Math.Min(across - 1, at.Right * across / Math.Max(1, room.Width));
            int top = Math.Max(0, at.Top * down / Math.Max(1, room.Height));
            int bottom = Math.Min(down - 1, at.Bottom * down / Math.Max(1, room.Height));
            for (int x = left; x <= right; x++)
                for (int y = top; y <= bottom; y++) covered[x, y] = true;
        }

        // A window writes a line out, keeps it, and starts the next one after a breath.
        private void Typing(Box box)
        {
            if (box.Bar && box.Done < 1) box.Done = Math.Min(1, box.Done + 0.004 * look.Speed);
            if (box.Typing == null)
            {
                if (age < box.Next) return;
                box.Typing = Fake.Line(luck).TrimStart();
                box.Shown = 0;
                return;
            }
            box.Shown += Math.Max(1, (int)Math.Round(TypedPerSecond * look.Speed * ticks.Interval / 1000.0));
            if (box.Shown < box.Typing.Length) return;
            box.Said.Add(box.Typing);
            while (box.Said.Count > box.Rows) box.Said.RemoveAt(0);
            box.Typing = null;
            box.Next = age + 0.05 + luck.NextDouble() * 0.5;
        }

        private double Gap(double first, double last, double over)
        {
            double into = Math.Min(1, age / over);
            return (first + (last - first) * into) / Math.Max(0.1, look.Speed);
        }

        // Hexagons spread from a handful of starting points, cell by cell, until the screen is a wall of them,
        // then it holds and starts again somewhere else. Growing outwards is what makes it read as something
        // taking hold rather than a pattern being drawn.
        private void Seed(int many)
        {
            for (int one = 0; one < many; one++)
            {
                int at = luck.Next(cells.Length);
                if (cells[at].Lit) continue;
                Light(at);
            }
        }

        private void Light(int at)
        {
            Cell cell = cells[at];
            if (cell.Lit) return;
            cell.Lit = true;
            cell.Born = age;
            alight++;
            // Most are solid, the way a filled alert panel looks, with outlines through it for depth.
            cell.Solid = luck.Next(10) < 6;
            cell.Arrow = luck.Next(7) == 0;
            cell.Said = cell.Arrow ? null : luck.Next(100) < 42 ? Fake.Alert(luck) : null;
            // Three in four flash, each at its own rate and out of step with its neighbours. The hard ones snap
            // on and off; the rest swell. The steady quarter is what the flashing reads against.
            bool flashes = look.Flash > 0 && luck.Next(4) != 0;
            // Held under two and a half flashes a second even at its fastest, and each one is a hexagon rather
            // than the whole screen. Nobody should have to find out the hard way what their screensaver does.
            cell.Rate = flashes ? Math.Min(9.4, (0.7 + luck.NextDouble() * 3.4) * look.Flash) : 0;
            cell.Phase = luck.NextDouble() * Math.PI * 2;
            cell.Hard = flashes && look.Flash >= 1f && luck.Next(2) == 0;
            // Only some neighbours are taken up, so it grows ragged and leaves the holes this look needs.
            foreach (int near in Around(at))
                if (!cells[near].Lit && luck.Next(100) < 68 && !edge.Contains(near)) edge.Add(near);
        }

        private IEnumerable<int> Around(int at)
        {
            int col = at % across, row = at / across;
            int up = (col & 1) == 0 ? -1 : 0, low = (col & 1) == 0 ? 0 : 1;
            int[,] steps = { { 0, -1 }, { 0, 1 }, { -1, up }, { -1, low }, { 1, up }, { 1, low } };
            for (int one = 0; one < steps.GetLength(0); one++)
            {
                int x = col + steps[one, 0], y = row + steps[one, 1];
                if (x >= 0 && x < across && y >= 0 && y < down) yield return y * across + x;
            }
        }

        private void Spreading()
        {
            if (fullAt >= 0)
            {
                // It keeps flashing while it holds: a still screen would look like it had stopped working.
                if (age - fullAt < HoldFullSeconds * 2) { Render(); return; }
                foreach (Cell cell in cells) { cell.Lit = false; cell.Said = null; }
                edge.Clear();
                alight = 0;
                fullAt = -1;
                nextThing = age;
                Seed(5);
            }
            // Faster the longer it runs, so it starts as a few cells and ends as a sweep.
            int many = Math.Max(1, (int)(1 + 5 * Math.Min(1, age / 18) * look.Speed));
            for (int one = 0; one < many && edge.Count > 0; one++)
            {
                int pick = luck.Next(edge.Count);
                int at = edge[pick];
                edge.RemoveAt(pick);
                Light(at);
            }
            // A new cluster now and then, so it does not grow as one blob.
            if (luck.Next(60) == 0) Seed(1);
            // Two thirds of the grid is as full as this gets. Past that the gaps close up and it stops being
            // hexagons on black and becomes one red sheet, which is not the look.
            if ((edge.Count == 0 || alight > cells.Length * 2 / 3) && fullAt < 0) fullAt = age;
            Render();
        }

        // The plain style: lines of code written down columns until the screen is full.
        private void Coding()
        {
            bool changed = false;
            if (fullAt >= 0)
            {
                if (age - fullAt < HoldFullSeconds) return;
                rows.Clear();
                Array.Clear(filled, 0, filled.Length);
                fullAt = -1;
                nextThing = age;
                changed = true;
            }
            while (age >= nextThing && fullAt < 0)
            {
                if (!Start()) { fullAt = age; break; }
                nextThing += SlowStart + (FastEnd - SlowStart) * Math.Min(1, age / CodeRamp);
                changed = true;
            }
            foreach (Row row in rows)
            {
                int want = Math.Min(row.Text.Length, (int)((age - row.Born) * TypedPerSecond));
                if (want != row.Shown) { row.Shown = want; changed = true; }
            }
            if (changed) Render();
        }

        // A new line in the shortest column, so it fills evenly rather than piling into one corner. False once
        // every column is full.
        private bool Start()
        {
            int pick = -1;
            for (int tries = 0; tries < 6; tries++)
            {
                int one = luck.Next(columns);
                if (filled[one] >= lines) continue;
                if (pick < 0 || filled[one] < filled[pick]) pick = one;
            }
            if (pick < 0)
            {
                for (int one = 0; one < columns; one++) if (filled[one] < lines && (pick < 0 || filled[one] < filled[pick])) pick = one;
                if (pick < 0) return false;
            }
            Row row = new Row
            {
                Text = Fake.Line(luck),
                X = pick * columnWidth + columnWidth / 20,
                Y = filled[pick] * lineHeight + lineHeight / 2,
                Born = age
            };
            filled[pick]++;
            rows.Add(row);
            return true;
        }

        // A column that has not started yet waits a while first, so they do not all fall in step.
        private Drop Woken(bool first)
        {
            Drop drop = new Drop();
            drop.Rate = (60 + luck.Next(140)) * look.Size;
            drop.Tail = 6 + luck.Next(22);
            drop.Head = -luck.Next(10, 60);
            drop.Wakes = first ? luck.NextDouble() * 50 : age + luck.NextDouble() * 2;
            drop.Face = new char[drop.Tail + 1];
            for (int one = 0; one < drop.Face.Length; one++) drop.Face[one] = RainFace[luck.Next(RainFace.Length)];
            return drop;
        }

        private void Falling(double step)
        {
            double tall = room.Height + 40;
            for (int one = 0; one < drops.Length; one++)
            {
                Drop drop = drops[one];
                if (age < drop.Wakes) continue;
                drop.Head += drop.Rate * step;
                // A character changing now and then is what stops it reading as one string sliding down.
                if (luck.Next(14) == 0) drop.Face[luck.Next(drop.Face.Length)] = RainFace[luck.Next(RainFace.Length)];
                if (drop.Head - drop.Tail * lineHeight > tall) drops[one] = Woken(false);
            }
        }

        // How long the screen takes to go dark. It went black on the frame it opened, which is a hard cut
        // from whatever they were looking at to nothing.
        internal const double FadeIn = 5;

        // The whole layered window's alpha, ramped over five seconds, so the black and its drawing come up together.
        // Reduced motion gets it at once.
        private byte Fading() { return FadeTo(look.Solid, shown, Motion); }

        // The ramp on its own, so it can be checked without a screen to put a window on.
        internal static byte FadeTo(byte solid, double since, bool motion)
        {
            if (!motion || since >= FadeIn) return solid;
            double part = since <= 0 ? 0 : since / FadeIn;
            return (byte)Math.Round(solid * part);
        }

        // That the screen arrives over five seconds rather than on the frame it opens, and that it arrives at
        // whatever they set rather than at full.
        internal static void FadeCheck(System.Collections.Generic.List<string> checks)
        {
            const byte Theirs = 200;
            if (FadeTo(Theirs, 0, true) != 0) throw new Exception("The screensaver was already visible on its first frame.");
            if (FadeTo(Theirs, FadeIn / 2, true) != 100) throw new Exception("Halfway through the fade it was not half there: " + FadeTo(Theirs, FadeIn / 2, true));
            if (FadeTo(Theirs, FadeIn, true) != Theirs || FadeTo(Theirs, 60, true) != Theirs) throw new Exception("The fade did not finish on what they set.");
            // Their own transparency is the ceiling, so a fade never shows more than they asked for.
            for (double at = 0; at <= FadeIn; at += 0.25) if (FadeTo(Theirs, at, true) > Theirs) throw new Exception("The fade went past what they set at " + at + " seconds.");
            if (FadeTo(Theirs, 0, false) != Theirs) throw new Exception("Reduced motion still got a five second fade.");
            checks.Add("PASS her screensaver fades the screen in over " + FadeIn + " seconds instead of cutting to black on the frame it opens, ending on the transparency they set and never going past it, and lands at once with reduced motion on");
        }

        private void Render()
        {
            using (Graphics graphics = Graphics.FromImage(surface.Image))
            {
                graphics.Clear(Color.FromArgb(255, look.Ground));
                graphics.TextRenderingHint = TextRenderingHint.AntiAlias;
                if (look.Style == SaverLook.Rain) PaintRain(graphics);
                else if (look.Style == SaverLook.Code) PaintCode(graphics);
                else if (look.Style == SaverLook.Hexes) PaintHexes(graphics);
                else PaintTerminals(graphics);
                Exit(graphics);
            }
            surface.Show(Handle, room.Location, Fading());
            drawn = true;
        }

        // Each window in the order it opened, so a new one lands on top of the ones before it.
        private void PaintTerminals(Graphics graphics)
        {
            Color ink = look.Ink;
            using (SolidBrush ground = new SolidBrush(Color.FromArgb(252, Blend(look.Ground, ink, 0.05f))))
            using (SolidBrush bar = new SolidBrush(Blend(look.Ground, ink, 0.22f)))
            using (SolidBrush named = new SolidBrush(Blend(ink, Color.White, 0.45f)))
            using (SolidBrush said = new SolidBrush(Color.FromArgb(Math.Max(90, ink.A * 15 / 20), ink)))
            using (SolidBrush fresh = new SolidBrush(ink))
            using (SolidBrush done = new SolidBrush(Color.FromArgb(Math.Max(70, ink.A * 2 / 5), ink)))
            using (Pen edge = new Pen(Color.FromArgb(Math.Max(120, ink.A * 4 / 5), ink)))
                foreach (Box box in boxes)
                {
                    Rectangle at = box.At;
                    graphics.FillRectangle(ground, at);
                    graphics.FillRectangle(bar, at.X, at.Y, at.Width, lineHeight);
                    graphics.DrawRectangle(edge, at.X, at.Y, at.Width - 1, at.Height - 1);
                    graphics.DrawLine(edge, at.X, at.Y + lineHeight, at.Right - 1, at.Y + lineHeight);
                    // The three dots a terminal window has in its corner, drawn rather than named.
                    for (int dot = 0; dot < 3; dot++)
                        graphics.FillEllipse(done, at.Right - 10 - dot * 9, at.Y + lineHeight / 2 - 3, 6, 6);
                    Region was = graphics.Clip;   // a new Region on every read, so it is freed below
                    graphics.SetClip(new Rectangle(at.X + 1, at.Y + 1, Math.Max(1, at.Width - 2), Math.Max(1, at.Height - 2)));
                    graphics.DrawString(box.Title, label, named, at.X + 7, at.Y + 2, exact);
                    int line = 0;
                    foreach (string row in box.Said)
                        graphics.DrawString(row, face, said, at.X + 7, at.Y + lineHeight + 3 + line++ * lineHeight, exact);
                    if (box.Typing != null)
                    {
                        string typed = box.Typing.Substring(0, Math.Min(box.Typing.Length, Math.Max(0, box.Shown)));
                        float y = at.Y + lineHeight + 3 + line * lineHeight;
                        graphics.DrawString(typed, face, fresh, at.X + 7, y, exact);
                        graphics.FillRectangle(fresh, at.X + 7 + typed.Length * letter, y + 2, Math.Max(2f, letter * 0.85f), face.Size);
                    }
                    if (box.Bar)
                    {
                        int width = at.Width - 16, y = at.Bottom - lineHeight + 2;
                        graphics.DrawRectangle(edge, at.X + 8, y, width, lineHeight - 8);
                        graphics.FillRectangle(fresh, at.X + 9, y + 1, Math.Max(0, (int)((width - 1) * box.Done)), lineHeight - 9);
                    }
                    graphics.Clip = was;
                    was.Dispose();
                }
        }

        // Where one hexagon is in its own flash, nought to one. A hard one snaps between on and nearly off; a
        // soft one swells; a steady one sits at full.
        private double Beat(Cell cell)
        {
            if (cell.Rate <= 0) return 1;
            double turn = Math.Sin(age * cell.Rate + cell.Phase);
            return cell.Hard ? (turn > 0 ? 1 : 0.12) : 0.3 + 0.7 * (0.5 + 0.5 * turn);
        }

        // Solid hexagons with the word knocked out of them, outlined ones with the word in the colour, and a
        // small triangle on some instead of a word. Each one fades up as it lights, then flashes on its own.
        private void PaintHexes(Graphics graphics)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            PointF[] shape = new PointF[6];
            using (SolidBrush fill = new SolidBrush(look.Ink))
            using (SolidBrush knocked = new SolidBrush(Color.FromArgb(255, look.Ground)))
            using (Pen line = new Pen(look.Ink, Math.Max(1.5f, reach * 0.055f)))
                for (int at = 0; at < cells.Length; at++)
                {
                    Cell cell = cells[at];
                    if (!cell.Lit) continue;
                    int col = at % across, row = at / across;
                    float cx = reach + col * stepX;
                    float cy = (row + ((col & 1) == 1 ? 0.5f : 0f)) * stepY + stepY / 2;
                    if (cx + reach < 0 || cx - reach > room.Width || cy + reach < 0 || cy - reach > room.Height) continue;
                    // A hexagon's six corners, flat top and bottom, points left and right.
                    for (int corner = 0; corner < 6; corner++)
                    {
                        double turn = Math.PI / 3 * corner;
                        shape[corner] = new PointF(cx + (float)(reach * 0.94 * Math.Cos(turn)), cy + (float)(reach * 0.94 * Math.Sin(turn)));
                    }
                    int alpha = (int)(Math.Min(1, Math.Max(0.08, (age - cell.Born) / 0.3)) * Beat(cell) * look.Ink.A);
                    if (alpha < 6) continue;
                    if (cell.Solid)
                    {
                        fill.Color = Color.FromArgb(alpha, look.Ink);
                        graphics.FillPolygon(fill, shape);
                    }
                    else
                    {
                        line.Color = Color.FromArgb(alpha, look.Ink);
                        graphics.DrawPolygon(line, shape);
                    }
                    Brush over = cell.Solid ? (Brush)knocked : fill;
                    if (!cell.Solid) fill.Color = Color.FromArgb(alpha, look.Ink);
                    if (cell.Arrow)
                    {
                        float arm = reach * 0.26f;
                        graphics.FillPolygon(over, new[]
                        {
                            new PointF(cx, cy + arm * 0.7f),
                            new PointF(cx - arm, cy - arm * 0.6f),
                            new PointF(cx + arm, cy - arm * 0.6f)
                        });
                    }
                    else if (cell.Said != null)
                        graphics.DrawString(cell.Said, tag, over, new RectangleF(cx - reach * 0.8f, cy - reach * 0.3f, reach * 1.6f, reach * 0.6f), middle);
                }
            graphics.SmoothingMode = SmoothingMode.Default;
        }

        private void PaintCode(Graphics graphics)
        {
            // The line being written is bright and the rest settle back, so the eye follows what is being
            // written instead of reading a wall of the same colour.
            using (SolidBrush fresh = new SolidBrush(look.Ink))
            using (SolidBrush settled = new SolidBrush(Color.FromArgb(Math.Max(60, look.Ink.A * 11 / 20), look.Ink)))
            using (SolidBrush cursor = new SolidBrush(Color.FromArgb(Math.Max(120, look.Ink.A * 17 / 20), look.Ink)))
                foreach (Row row in rows)
                {
                    if (row.Shown <= 0) continue;
                    bool writing = row.Shown < row.Text.Length;
                    string said = writing ? row.Text.Substring(0, row.Shown) : row.Text;
                    graphics.DrawString(said, face, writing ? fresh : settled, row.X, row.Y, exact);
                    if (!writing) continue;
                    graphics.FillRectangle(cursor, row.X + said.Length * letter + 1, row.Y + 2, Math.Max(2f, letter * 0.85f), face.Size);
                }
        }

        // The head of each column is near white and the tail fades out behind it.
        private void PaintRain(Graphics graphics)
        {
            float wide = Math.Max(1f, letter);
            using (SolidBrush brush = new SolidBrush(look.Ink))
                for (int one = 0; one < drops.Length; one++)
                {
                    Drop drop = drops[one];
                    if (age < drop.Wakes) continue;
                    float x = one * wide;
                    for (int back = 0; back <= drop.Tail; back++)
                    {
                        float y = (float)drop.Head - back * lineHeight;
                        if (y < -lineHeight || y > room.Height) continue;
                        int alpha = back == 0 ? 255 : Math.Max(0, 210 - back * 210 / Math.Max(1, drop.Tail));
                        if (alpha <= 4) continue;
                        brush.Color = back == 0 ? Blend(look.Ink, Color.White, 0.62f) : Color.FromArgb(alpha * look.Ink.A / 255, look.Ink);
                        graphics.DrawString(drop.Face[back].ToString(), face, brush, x, y, exact);
                    }
                }
        }

        private static Color Blend(Color from, Color to, float how)
        {
            return Color.FromArgb(255,
                (int)(from.R + (to.R - from.R) * how),
                (int)(from.G + (to.G - from.G) * how),
                (int)(from.B + (to.B - from.B) * how));
        }

        // It never takes a click or a key: whatever is under it keeps them, and the watch on idle time is what
        // closes this. Hit testing is refused so it cannot even be the window under the pointer.
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0084) { message.Result = new IntPtr(-1); return; }
            if (message.Msg == 0x0021) { message.Result = new IntPtr(3); return; }
            base.WndProc(ref message);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ticks.Dispose();
                exact.Dispose();
                middle.Dispose();
                if (surface != null) { surface.Dispose(); surface = null; }
            }
            base.Dispose(disposing);
        }
    }

    // Lines that look like work and are not. Built from shapes and made-up names, so no two runs read the same
    // and nothing in it came off anybody's disk.
    internal static class Fake
    {
        private static readonly string[] Shapes =
        {
            "if ({a}.{m} == null) return {b};",
            "for (int {i} = 0; {i} < {a}.Count; {i}++)",
            "{a} = await {b}.{m}({c});",
            "while (!{a}.Ready) Wait({n});",
            "{a}.{m}({b}, {n});",
            "return {a} == null ? {b} : {a}.{m}();",
            "var {a} = new {T}({n});",
            "try { {a}.{m}(); } catch { }",
            "{a}[{i}] = {b}[{i}] ^ {n};",
            "public static bool {m}({T} {a})",
            "def {m}({a}): return {a} * {n}",
            "let {a} = {b}.filter(x => x.{m});",
            "{a} := make([]byte, {n})",
            "#define {U} {n}",
            "// {note}",
            "}"
        };

        // What a terminal prints while something is happening, which is most of what fills these windows.
        private static readonly string[] Output =
        {
            "> {m} {a} ... ok",
            "> linking {a}.{m} [{n} refs]",
            "{n} packets through {a}, 0 dropped",
            "[ ok ] {a} came up in {n}ms",
            "[warn] {a} retried {i} times",
            "reading {a} ... {n} of {n} blocks",
            "{U} = {n}",
            "$ build {a} --{m}",
            "$ watch {a}",
            "sha {h}  {a}",
            "{a}: {n} rows, {n} written",
            "waiting on {a}.{m}",
            "-- {note} --",
            "{n}%  {a} {m}",
            "+ {a}.{m}",
            "- {b}.{m}"
        };

        private static readonly string[] Titles =
        {
            "~/{a}", "{a} -- build", "{a}.log", "trace {h}", "{a}@{b}", "watch {a}", "{a}/{b}",
            "{U}", "tail -f {a}.log", "{a} -- {m}"
        };

        private static readonly string[] Names =
        {
            "buffer", "frame", "surface", "packet", "cursor", "lattice", "daemon", "shard", "kernel", "socket",
            "vector", "anchor", "token", "glyph", "spindle", "relay", "beacon", "cipher", "ledger", "pivot",
            "tether", "lumen", "drift", "signal", "mantle", "quill", "ember", "strand", "harbour", "cradle"
        };

        private static readonly string[] Members =
        {
            "count", "ready", "open", "flush", "next", "write", "seek", "bind", "wake", "drain",
            "depth", "head", "tail", "scale", "reset", "settle", "mark", "pull", "hold", "clear"
        };

        private static readonly string[] Types =
        {
            "Buffer", "Surface", "Packet", "Lattice", "Relay", "Cipher", "Ledger", "Spindle", "Beacon", "Strand"
        };

        private static readonly string[] Notes =
        {
            "this should never happen", "left in on purpose", "faster than it looks", "off by one, fixed",
            "ask the compiler", "do not touch", "holds while the lock is up", "counted from the other end",
            "the slow path, and it has to be", "rounded on the way in", "nothing below here runs twice",
            "it was a one line fix", "keep this above the flush", "cheaper than checking"
        };

        // Short enough to sit inside a hexagon and mean nothing in particular.
        private static readonly string[] Alerts =
        {
            "EMERGENCY", "WARNING", "ALERT", "LEVEL 1", "LEVEL 2", "LEVEL 3", "CODE RED", "LOCKED",
            "SEALED", "STANDBY", "SIGNAL", "BREACH", "KEYCODE", "REFUSED", "NO ENTRY", "HOLD",
            "SECURE", "OVERRIDE", "PRIORITY", "CONTAIN", "SECTOR A", "SECTOR B", "SECTOR C", "GATE 4"
        };

        internal static string Alert(Random luck) { return Alerts[luck.Next(Alerts.Length)]; }

        internal static string Title(Random luck) { return Filled(Titles[luck.Next(Titles.Length)], luck); }

        // Two lines in three are output, since a terminal spends most of its life printing rather than being
        // written in.
        internal static string Line(Random luck)
        {
            string[] from = luck.Next(3) == 0 ? Shapes : Output;
            string shape = from[luck.Next(from.Length)];
            string made = Filled(shape, luck);
            if (from != Shapes) return made;
            int deep = luck.Next(4);
            return deep == 0 ? made : new string(' ', deep * 2) + made;
        }

        private static string Filled(string shape, Random luck)
        {
            return shape
                .Replace("{a}", Names[luck.Next(Names.Length)])
                .Replace("{b}", Names[luck.Next(Names.Length)])
                .Replace("{c}", Names[luck.Next(Names.Length)])
                .Replace("{m}", Members[luck.Next(Members.Length)])
                .Replace("{T}", Types[luck.Next(Types.Length)])
                .Replace("{U}", Names[luck.Next(Names.Length)].ToUpperInvariant())
                .Replace("{i}", "ijkn".Substring(luck.Next(4), 1))
                .Replace("{h}", luck.Next(0x100000, 0xFFFFFF).ToString("x"))
                .Replace("{n}", luck.Next(2, 4096).ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Replace("{note}", Notes[luck.Next(Notes.Length)]);
        }
    }

    // A .wav of theirs, looped while the screensaver is up: only the file sys.md names, none ships. Her sound switch
    // mutes it, and her volume is applied to the samples, since the player has no volume of its own.
    internal static class SaverSound
    {
        private static System.Media.SoundPlayer playing;
        private static string loaded;
        private static int loadedAt;

        internal static bool Playing { get { return playing != null; } }

        internal static void On(string path, int volume)
        {
            if (string.IsNullOrEmpty(path) || !Chimes.On) { Off(); return; }
            volume = Math.Max(0, Math.Min(100, volume));
            if (volume == 0) { Off(); return; }
            if (playing != null && loaded == path && loadedAt == volume) return;
            Off();
            try
            {
                byte[] wav = Quieter(File.ReadAllBytes(path), volume);
                System.Media.SoundPlayer made = new System.Media.SoundPlayer(new MemoryStream(wav));
                made.PlayLooping();
                playing = made;
                loaded = path;
                loadedAt = volume;
            }
            catch (Exception error)
            {
                playing = null;
                try { Brain.Log("screensaver", -1, "Her screensaver could not play " + Path.GetFileName(path) + ": " + error.GetType().Name + ": " + error.Message); }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
        }

        internal static void Off()
        {
            System.Media.SoundPlayer stopping = playing;
            playing = null;
            loaded = null;
            if (stopping == null) return;
            try { stopping.Stop(); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            try { stopping.Dispose(); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // Her volume, applied to the samples themselves. A file that is not plain 16 bit PCM is left exactly
        // as it is and plays at whatever Windows gives it, rather than being mangled by a guess.
        private static byte[] Quieter(byte[] wav, int volume)
        {
            if (volume >= 100 || wav.Length < 44) return wav;
            int at = 12;
            int bits = 0, kind = 0;
            while (at + 8 <= wav.Length)
            {
                string tag = "" + (char)wav[at] + (char)wav[at + 1] + (char)wav[at + 2] + (char)wav[at + 3];
                int size = BitConverter.ToInt32(wav, at + 4);
                if (size < 0 || at + 8 + size > wav.Length) break;
                if (tag == "fmt " && size >= 16)
                {
                    kind = BitConverter.ToInt16(wav, at + 8);
                    bits = BitConverter.ToInt16(wav, at + 22);
                }
                else if (tag == "data")
                {
                    if (kind != 1 || bits != 16) return wav;
                    byte[] made = (byte[])wav.Clone();
                    for (int one = at + 8; one + 1 < at + 8 + size; one += 2)
                    {
                        short sample = BitConverter.ToInt16(made, one);
                        byte[] quieter = BitConverter.GetBytes((short)(sample * volume / 100));
                        made[one] = quieter[0];
                        made[one + 1] = quieter[1];
                    }
                    return made;
                }
                at += 8 + size + (size & 1);
            }
            return wav;
        }
    }

    internal sealed partial class Companion
    {
        private readonly List<Screensaver> savers = new List<Screensaver>();
        private double saverCheckedAt;
        private bool saverHeldLogged;

        // Shown from her menu: the pause stays, since the spacebar that picks the menu item is also the key that ends
        // it. Only the idle rule is left off.
        internal void ShowSaverNow() { ShowSaverNow(null); }

        internal void ShowSaverNow(string style)
        {
            if (InvokeRequired) { Invoke(new Action<string>(ShowSaverNow), style); return; }
            if (savers.Count > 0) { SaverOff(); return; }
            if (SaverHeld()) { Remark("", "Not while something is full screen or on camera.", true); return; }
            // Longer than it takes to let go of a menu, since the click that asked for it is an input too.
            ShowSaver(style, 1.4);
        }

        // Twice a second is plenty. Idle time only starts it; the windows end themselves, so this also stops a sound
        // still looping after they have all gone.
        private void SaverTick(double now)
        {
            if (now - saverCheckedAt < 0.5) return;
            saverCheckedAt = now;
            if (savers.Count == 0)
            {
                if (SaverSound.Playing) SaverSound.Off();
                if (!SaverHeld() && SysLines.Saver && Rested()) ShowSaver();
                return;
            }
            if (SaverHeld()) SaverOff();
        }

        // Whether the PC has been left alone for as long as they asked for.
        private bool Rested()
        {
            try { return Native.IdleMilliseconds() >= SysLines.SaverAfter * 60000; }
            catch (Exception) { return false; }
        }

        // What it never covers: a game or anything else filling the screen, a stream, a job of hers, her being
        // held or dragged, and her shutting down. The same list her fake crash respects, for the same reason.
        private bool SaverHeld()
        {
            bool busy = quitting || Brain.OnAir || working != null || MidJob(job) || lentOut || dragging || pressed;
            // A prank chat set off is meant to land on the stream, so full screen and streaming are lifted while one
            // runs; mouse, mid-job, dragging and shutting down still hold.
            return bullySaver ? busy : busy || FullScreen() || Computer.Streaming;
        }

        // Left alone, it plays no sound: an alarm at an empty chair isn't wanted. It still plays when asked for from
        // her menu, so the setting can be heard.
        private void ShowSaver() { ShowSaver(null, 0.6, false); }

        private void ShowSaver(string style, double settles) { ShowSaver(style, settles, true); }

        private void ShowSaver(string style, double settles, bool asked) { ShowSaver(style, settles, asked, false, 0); }

        private void ShowSaver(string style, double settles, bool asked, bool anyKey, double ends)
        {
            SaverLook look = SaverLook.Read(style);
            int seed = Environment.TickCount;
            Point middle = new Point(anchor.X + Width / 2, anchor.Y + Height / 2);
            Screen hers = Screen.FromPoint(middle);
            foreach (Screen screen in Screen.AllScreens)
            {
                if (!look.EveryScreen && !screen.Bounds.Equals(hers.Bounds)) continue;
                Screensaver one = new Screensaver(screen.Bounds, look, unchecked(seed += 7919));
                one.Settles = settles;
                one.AnyKey = anyKey;
                one.Ends = ends;
                one.Motion = preferences.Motion;
                one.Over = SaverHeld;
                try
                {
                    if (!one.Open()) { one.Dispose(); continue; }
                    savers.Add(one);
                    Screensaver held = one;
                    // The windows take themselves down, so the last one out stops the sound. Waiting for the
                    // next tick to notice left it looping over a screen that had already gone.
                    held.FormClosed += delegate
                    {
                        savers.Remove(held);
                        if (savers.Count == 0) SaverSound.Off();
                        else foreach (Screensaver other in savers.ToArray()) { try { other.Close(); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); } }
                    };
                }
                catch (Exception error)
                {
                    one.Dispose();
                    if (!saverHeldLogged)
                    {
                        saverHeldLogged = true;
                        try { Brain.Log("screensaver", -1, "Her screensaver could not open: " + error.GetType().Name + ": " + error.Message); }
                        catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                    }
                    return;
                }
            }
            // Only once there is something on screen, so a sound never plays over nothing, and only when it was
            // asked for. Its own share of her sound volume, because a loop running the whole time it is up wants
            // far less than a chime does.
            if (asked && savers.Count > 0) SaverSound.On(look.Sound, Chimes.Volume * look.Volume / 100);
            else SaverSound.Off();
        }

        // red_alert: her hexagon alert with the alarm, in hexes whatever style was chosen, for six seconds, ending on
        // any key and undoing itself.
        private const double AlertSeconds = 6;
        private bool bullySaver;

        private string AlertTool(string action)
        {
            if (InvokeRequired) return (string)Invoke(new Func<string, string>(AlertTool), action);
            if (action == "stop") { SaverOff(); return "Stopped my red alert."; }
            if (savers.Count > 0) return "My red alert is already up.";
            if (quitting || SaverHeld()) return "I can't show that during full screen, a stream or a mouse job.";
            ShowSaver(SaverLook.Hexes, 1.4, true, true, AlertSeconds);
            return savers.Count > 0 ? "Red alert." : "Couldn't show my red alert.";
        }

        // The alert chat set off, which is meant to land on the stream, so full screen and streaming are lifted
        // for as long as it runs and nothing else is. The same shape her fake crash uses for the same reason.
        internal string BullyAlert()
        {
            if (InvokeRequired) return (string)Invoke(new Func<string>(BullyAlert));
            bullySaver = true;
            string said = AlertTool("show");
            if (savers.Count == 0) bullySaver = false;
            else foreach (Screensaver one in savers.ToArray()) one.FormClosed += delegate { bullySaver = false; };
            return said;
        }

        private void SaverOff()
        {
            SaverSound.Off();
            if (savers.Count == 0) return;
            Screensaver[] closing = savers.ToArray();
            savers.Clear();
            foreach (Screensaver one in closing) { try { one.Close(); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); } }
        }
    }
}
