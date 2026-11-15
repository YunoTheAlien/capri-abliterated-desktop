using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text;

namespace CapriDesktop
{
    // Stand-in desktop icons and windows, for when she is on camera.
    //
    // Her rampage picks up the real desktop: real icon pictures and, worse, the real names under them. On a stream that
    // is a folder of someone's full name, a client's name, a save file that says where they work. Same for the fake
    // windows she throws about, which would otherwise carry real titles.
    //
    // So with stream-safe on, nothing from the desktop is drawn. Every icon becomes a generic one of ours, with a
    // generic label, and the order is fixed so the desktop's real arrangement is not readable from the stand-ins either.
    //
    // The pictures are drawn here rather than shipped or downloaded: no licensing to pass on, nothing fetched, and they
    // come out identical on any machine. They are written once into her own assets folder as PNGs so they can be seen,
    // swapped or replaced - drop your own PNG over one and she uses yours.
    internal static class StreamSafe
    {
        internal static volatile bool On;

        // Deliberately dull and generic. Nothing here is a real person, place, company or file.
        private static readonly string[] Names =
        {
            "Documents", "Downloads", "Pictures", "Music", "Videos", "Notes",
            "Readme", "Screenshot", "Game", "Setup", "Archive", "Project",
            "Folder", "Shortcut", "Wallpaper", "Saves"
        };

        private enum Kind { Folder, Document, App, Image, Tune, Zip }

        private static readonly Kind[] Kinds =
        {
            Kind.Folder, Kind.Folder, Kind.Image, Kind.Tune, Kind.Image, Kind.Document,
            Kind.Document, Kind.Image, Kind.App, Kind.App, Kind.Zip, Kind.Folder,
            Kind.Folder, Kind.App, Kind.Image, Kind.Folder
        };

        private static readonly object gate = new object();
        private static readonly Dictionary<string, Bitmap> made = new Dictionary<string, Bitmap>();

        internal static string Store
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Capri", "assets", "stream");
            }
        }

        internal static string Label(int which)
        {
            return Names[((which % Names.Length) + Names.Length) % Names.Length];
        }

        // A stand-in icon at the size the desktop is using. Cached, so a rampage of forty icons draws six pictures.
        internal static Bitmap Picture(int which, int size)
        {
            Kind kind = Kinds[((which % Kinds.Length) + Kinds.Length) % Kinds.Length];
            size = Math.Max(16, Math.Min(256, size));
            string key = kind + "-" + size;
            lock (gate)
            {
                Bitmap had;
                if (made.TryGetValue(key, out had) && had != null) return had;

                Bitmap drawn = null;
                string file = Path.Combine(Store, kind.ToString().ToLowerInvariant() + ".png");
                // Theirs wins, if they have put one there.
                try
                {
                    if (File.Exists(file))
                        using (Bitmap read = new Bitmap(file))
                            drawn = Fit(read, size);
                }
                catch (Exception) { drawn = null; }

                if (drawn == null)
                {
                    drawn = Draw(kind, size);
                    try
                    {
                        Directory.CreateDirectory(Store);
                        if (!File.Exists(file))
                            using (Bitmap big = Draw(kind, 256))
                                big.Save(file, ImageFormat.Png);
                    }
                    catch (Exception) { }
                }

                made[key] = drawn;
                return drawn;
            }
        }

        private static Bitmap Fit(Bitmap source, int size)
        {
            Bitmap into = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            using (Graphics pen = Graphics.FromImage(into))
            {
                pen.InterpolationMode = InterpolationMode.HighQualityBicubic;
                pen.PixelOffsetMode = PixelOffsetMode.HighQuality;
                pen.DrawImage(source, new Rectangle(0, 0, size, size));
            }
            return into;
        }

        private static Bitmap Draw(Kind kind, int size)
        {
            Bitmap into = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            using (Graphics pen = Graphics.FromImage(into))
            {
                pen.SmoothingMode = SmoothingMode.AntiAlias;
                pen.InterpolationMode = InterpolationMode.HighQualityBicubic;
                float u = size / 32f;   // everything below is in 32-unit space

                switch (kind)
                {
                    case Kind.Folder:
                        Shape(pen, u, 2f, 9f, 28f, 19f, Color.FromArgb(255, 222, 148, 44), Color.FromArgb(255, 196, 120, 26), 2.5f);
                        Shape(pen, u, 2f, 6f, 12f, 5f, Color.FromArgb(255, 236, 170, 70), Color.FromArgb(255, 196, 120, 26), 1.5f);
                        break;

                    case Kind.Document:
                        Shape(pen, u, 6f, 3f, 20f, 26f, Color.White, Color.FromArgb(255, 168, 172, 178), 1.5f);
                        using (Brush ink = new SolidBrush(Color.FromArgb(255, 150, 156, 164)))
                            for (int line = 0; line < 5; line++)
                                pen.FillRectangle(ink, 9.5f * u, (8f + line * 3.6f) * u, (line == 4 ? 8f : 13f) * u, 1.3f * u);
                        break;

                    case Kind.App:
                        Shape(pen, u, 4f, 4f, 24f, 24f, Color.FromArgb(255, 80, 150, 226), Color.FromArgb(255, 48, 108, 176), 3f);
                        using (Brush glass = new SolidBrush(Color.FromArgb(70, Color.White)))
                            pen.FillRectangle(glass, 4f * u, 4f * u, 24f * u, 10f * u);
                        using (Brush dot = new SolidBrush(Color.White))
                            pen.FillEllipse(dot, 13f * u, 13f * u, 6f * u, 6f * u);
                        break;

                    case Kind.Image:
                        Shape(pen, u, 3f, 6f, 26f, 20f, Color.FromArgb(255, 238, 240, 243), Color.FromArgb(255, 168, 172, 178), 2f);
                        using (Brush sky = new SolidBrush(Color.FromArgb(255, 126, 194, 232)))
                            pen.FillRectangle(sky, 4.2f * u, 7.2f * u, 23.6f * u, 17.6f * u);
                        using (Brush sun = new SolidBrush(Color.FromArgb(255, 252, 214, 92)))
                            pen.FillEllipse(sun, 20f * u, 9.5f * u, 5f * u, 5f * u);
                        using (Brush hill = new SolidBrush(Color.FromArgb(255, 96, 170, 108)))
                            pen.FillPolygon(hill, new[]
                            {
                                new PointF(4.2f * u, 24.8f * u), new PointF(12f * u, 14f * u),
                                new PointF(18f * u, 24.8f * u)
                            });
                        break;

                    case Kind.Tune:
                        using (Brush disc = new SolidBrush(Color.FromArgb(255, 92, 98, 110)))
                            pen.FillEllipse(disc, 3f * u, 3f * u, 26f * u, 26f * u);
                        using (Brush mid = new SolidBrush(Color.FromArgb(255, 226, 230, 236)))
                            pen.FillEllipse(mid, 13f * u, 13f * u, 6f * u, 6f * u);
                        using (Pen ring = new Pen(Color.FromArgb(120, Color.White), 1f * u))
                            pen.DrawEllipse(ring, 7f * u, 7f * u, 18f * u, 18f * u);
                        break;

                    case Kind.Zip:
                        Shape(pen, u, 6f, 3f, 20f, 26f, Color.FromArgb(255, 246, 222, 140), Color.FromArgb(255, 198, 164, 70), 2f);
                        using (Brush teeth = new SolidBrush(Color.FromArgb(255, 150, 120, 40)))
                            for (int tooth = 0; tooth < 7; tooth++)
                                pen.FillRectangle(teeth, 14.6f * u, (5f + tooth * 3.2f) * u, 2.8f * u, 2f * u);
                        break;
                }
            }
            return into;
        }

        private static void Shape(Graphics pen, float u, float x, float y, float w, float h, Color fill, Color edge, float round)
        {
            RectangleF box = new RectangleF(x * u, y * u, w * u, h * u);
            float r = Math.Max(0.5f, round * u);
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddArc(box.Left, box.Top, r * 2, r * 2, 180, 90);
                path.AddArc(box.Right - r * 2, box.Top, r * 2, r * 2, 270, 90);
                path.AddArc(box.Right - r * 2, box.Bottom - r * 2, r * 2, r * 2, 0, 90);
                path.AddArc(box.Left, box.Bottom - r * 2, r * 2, r * 2, 90, 90);
                path.CloseFigure();
                using (Brush paint = new SolidBrush(fill)) pen.FillPath(paint, path);
                using (Pen line = new Pen(edge, Math.Max(1f, 0.8f * u))) pen.DrawPath(line, path);
            }
        }

        // ---- stand-in windows, for her to shove around on camera ----

        private static readonly string[,] Windows =
        {
            { "Untitled - Notepad", "text" },
            { "Photos", "picture" },
            { "Music Player", "player" },
            { "File Explorer", "files" },
            { "Calculator", "keys" },
            { "Settings", "list" }
        };

        internal static int WindowCount { get { return Windows.GetLength(0); } }

        internal static string WindowTitle(int which)
        {
            return Windows[((which % WindowCount) + WindowCount) % WindowCount, 0];
        }

        // A believable but entirely invented window. Written out as a PNG too, so they are swappable like the icons.
        internal static Bitmap Window(int which, int wide, int tall)
        {
            which = ((which % WindowCount) + WindowCount) % WindowCount;
            wide = Math.Max(160, Math.Min(1600, wide));
            tall = Math.Max(120, Math.Min(1200, tall));
            string key = "win-" + which + "-" + wide + "x" + tall;
            lock (gate)
            {
                Bitmap had;
                if (made.TryGetValue(key, out had) && had != null) return had;

                string file = Path.Combine(Store, "window-" + Windows[which, 1] + ".png");
                Bitmap drawn = null;
                try
                {
                    if (File.Exists(file))
                        using (Bitmap read = new Bitmap(file))
                        {
                            drawn = new Bitmap(wide, tall, PixelFormat.Format32bppPArgb);
                            using (Graphics pen = Graphics.FromImage(drawn))
                            {
                                pen.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                pen.DrawImage(read, new Rectangle(0, 0, wide, tall));
                            }
                        }
                }
                catch (Exception) { drawn = null; }

                if (drawn == null)
                {
                    drawn = DrawWindow(which, wide, tall);
                    try
                    {
                        Directory.CreateDirectory(Store);
                        if (!File.Exists(file))
                            using (Bitmap big = DrawWindow(which, 640, 440))
                                big.Save(file, ImageFormat.Png);
                    }
                    catch (Exception) { }
                }

                made[key] = drawn;
                return drawn;
            }
        }

        private static Bitmap DrawWindow(int which, int wide, int tall)
        {
            string title = Windows[which, 0], body = Windows[which, 1];
            Bitmap into = new Bitmap(wide, tall, PixelFormat.Format32bppPArgb);
            using (Graphics pen = Graphics.FromImage(into))
            {
                pen.SmoothingMode = SmoothingMode.AntiAlias;
                pen.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                int bar = Math.Max(22, tall / 14);
                using (Brush page = new SolidBrush(Color.FromArgb(255, 252, 252, 253)))
                    pen.FillRectangle(page, 0, 0, wide, tall);
                using (Brush chrome = new SolidBrush(Color.FromArgb(255, 238, 240, 243)))
                    pen.FillRectangle(chrome, 0, 0, wide, bar);
                using (Pen edge = new Pen(Color.FromArgb(255, 198, 202, 208)))
                    pen.DrawRectangle(edge, 0, 0, wide - 1, tall - 1);
                using (Pen under = new Pen(Color.FromArgb(255, 214, 218, 224)))
                    pen.DrawLine(under, 0, bar, wide, bar);

                using (Font font = new Font("Segoe UI", bar * 0.46f, FontStyle.Regular, GraphicsUnit.Pixel))
                using (Brush ink = new SolidBrush(Color.FromArgb(255, 60, 64, 70)))
                    pen.DrawString(title, font, ink, 8, (bar - font.GetHeight(pen)) / 2f);

                // The three buttons, close in red like everyone expects.
                int dot = Math.Max(8, bar / 2), top = (bar - dot) / 2;
                Color[] lights = { Color.FromArgb(255, 170, 176, 184), Color.FromArgb(255, 170, 176, 184), Color.FromArgb(255, 226, 86, 86) };
                for (int light = 0; light < 3; light++)
                    using (Brush paint = new SolidBrush(lights[light]))
                        pen.FillEllipse(paint, wide - (3 - light) * (dot + 8), top, dot, dot);

                Rectangle inner = new Rectangle(10, bar + 10, wide - 20, tall - bar - 20);
                if (inner.Width < 10 || inner.Height < 10) return into;

                switch (body)
                {
                    case "text":
                        using (Brush ink = new SolidBrush(Color.FromArgb(255, 168, 174, 182)))
                            for (int line = 0; line * 16 + 8 < inner.Height; line++)
                                pen.FillRectangle(ink, inner.Left, inner.Top + line * 16,
                                    inner.Width * (line % 4 == 3 ? 0.35f : line % 3 == 0 ? 0.92f : 0.7f), 5);
                        break;

                    case "picture":
                        using (Brush sky = new SolidBrush(Color.FromArgb(255, 126, 194, 232)))
                            pen.FillRectangle(sky, inner);
                        using (Brush sun = new SolidBrush(Color.FromArgb(255, 252, 214, 92)))
                            pen.FillEllipse(sun, inner.Right - inner.Width * 0.26f, inner.Top + inner.Height * 0.12f,
                                inner.Width * 0.14f, inner.Width * 0.14f);
                        using (Brush hill = new SolidBrush(Color.FromArgb(255, 96, 170, 108)))
                            pen.FillPolygon(hill, new[]
                            {
                                new PointF(inner.Left, inner.Bottom),
                                new PointF(inner.Left + inner.Width * 0.42f, inner.Top + inner.Height * 0.3f),
                                new PointF(inner.Left + inner.Width * 0.84f, inner.Bottom)
                            });
                        break;

                    case "player":
                        float span = inner.Height * 0.6f, discX = inner.Left + inner.Width / 2f - span / 2f, discY = inner.Top + inner.Height * 0.08f;
                        using (Brush disc = new SolidBrush(Color.FromArgb(255, 92, 98, 110)))
                            pen.FillEllipse(disc, discX, discY, span, span);
                        using (Pen groove = new Pen(Color.FromArgb(90, Color.White), Math.Max(1f, span / 40f)))
                            pen.DrawEllipse(groove, discX + span * 0.17f, discY + span * 0.17f, span * 0.66f, span * 0.66f);
                        using (Brush hole = new SolidBrush(Color.FromArgb(255, 226, 230, 236)))
                            pen.FillEllipse(hole, discX + span * 0.4f, discY + span * 0.4f, span * 0.2f, span * 0.2f);
                        using (Brush track = new SolidBrush(Color.FromArgb(255, 214, 218, 224)))
                            pen.FillRectangle(track, inner.Left, inner.Bottom - 18, inner.Width, 6);
                        using (Brush got = new SolidBrush(Color.FromArgb(255, 120, 180, 230)))
                            pen.FillRectangle(got, inner.Left, inner.Bottom - 18, inner.Width * 0.38f, 6);
                        break;

                    case "files":
                        for (int row = 0; row * 34 + 30 < inner.Height; row++)
                        {
                            // Not disposed: Picture hands back the cached copy, it is not ours to free.
                            pen.DrawImage(Picture(row, 24), inner.Left + 4, inner.Top + row * 34, 24, 24);
                            using (Brush ink = new SolidBrush(Color.FromArgb(255, 168, 174, 182)))
                                pen.FillRectangle(ink, inner.Left + 36, inner.Top + row * 34 + 9, inner.Width * 0.45f, 6);
                        }
                        break;

                    case "keys":
                        using (Brush screen = new SolidBrush(Color.FromArgb(255, 238, 240, 243)))
                            pen.FillRectangle(screen, inner.Left, inner.Top, inner.Width, inner.Height * 0.22f);
                        for (int row = 0; row < 4; row++)
                            for (int col = 0; col < 4; col++)
                                using (Brush key = new SolidBrush(Color.FromArgb(255, 228, 232, 238)))
                                    pen.FillRectangle(key,
                                        inner.Left + col * (inner.Width / 4f) + 3,
                                        inner.Top + inner.Height * 0.28f + row * (inner.Height * 0.72f / 4f) + 3,
                                        inner.Width / 4f - 6, inner.Height * 0.72f / 4f - 6);
                        break;

                    default:
                        for (int row = 0; row * 30 + 26 < inner.Height; row++)
                        {
                            using (Brush ink = new SolidBrush(Color.FromArgb(255, 176, 182, 190)))
                                pen.FillRectangle(ink, inner.Left, inner.Top + row * 30 + 8, inner.Width * 0.4f, 6);
                            using (Brush knob = new SolidBrush(Color.FromArgb(255, 120, 180, 230)))
                                pen.FillRectangle(knob, inner.Right - 44, inner.Top + row * 30 + 4, 36, 14);
                        }
                        break;
                }
            }
            return into;
        }

        // Writes every stand-in out at full size, so they can be looked at and swapped.
        internal static string Export()
        {
            StringBuilder said = new StringBuilder();
            try
            {
                Directory.CreateDirectory(Store);
                foreach (Kind kind in Enum.GetValues(typeof(Kind)))
                    using (Bitmap one = Draw(kind, 256))
                        one.Save(Path.Combine(Store, kind.ToString().ToLowerInvariant() + ".png"), ImageFormat.Png);
                for (int which = 0; which < WindowCount; which++)
                    using (Bitmap one = DrawWindow(which, 640, 440))
                        one.Save(Path.Combine(Store, "window-" + Windows[which, 1] + ".png"), ImageFormat.Png);
                said.Append(Store);
            }
            catch (Exception bad) { said.Append("could not write them: " + bad.Message); }
            return said.ToString();
        }

        internal static void Forget()
        {
            lock (gate)
            {
                foreach (KeyValuePair<string, Bitmap> each in made)
                    try { if (each.Value != null) each.Value.Dispose(); } catch (Exception) { }
                made.Clear();
            }
        }
    }
}
