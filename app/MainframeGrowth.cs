using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Growth: her personalities or her moods on a radar, the side she is on only. The filled shape is her growth points;
    // each spoke's line out from the centre is how much its grown lines weigh, so one whose lines aren't growing shows.
    internal sealed partial class Mainframe
    {
        private GrowthSheet growthSheet;

        internal void OpenGrowth()
        {
            if (growthSheet != null && !growthSheet.IsDisposed && growthSheet.Zoom != zoom) { growthSheet.Dispose(); growthSheet = null; }
            if (growthSheet == null || growthSheet.IsDisposed) growthSheet = Kept(new GrowthSheet(this, SheetZoom(GrowthSheet.Wide, GrowthSheet.Tall)));
            if (!growthSheet.Visible) growthSheet.Show(this);
            growthSheet.Activate();
        }

        // One spoke: its name, her growth points for it and the bytes of its grown lines.
        internal sealed class GrowthSpoke
        {
            internal string Name;
            internal double Points;
            internal long Bytes;
        }

        // The names on the side she is on: her personalities (not Dynamic, a mode) or her moods. Swapped by the checks.
        internal static Func<bool, string[]> GrowthNames = delegate(bool moods)
        {
            if (moods) return Dynamics.Allowed.ToArray();
            List<string> names = new List<string>();
            foreach (string one in Personalities.Names)
            {
                if (one == Personalities.Dynamic) continue;
                // Off the other side, a pack shows only where its plain file is.
                if (!Brain.SenpaiLive && one != Personalities.Stock && !File.Exists(Personalities.PlainPathOf(one) ?? "")) continue;
                names.Add(one);
            }
            return names.ToArray();
        };

        internal static List<GrowthSpoke> GrowthSpokes(bool moods)
        {
            List<GrowthSpoke> spokes = new List<GrowthSpoke>();
            string[] names = GrowthNames(moods) ?? new string[0];
            Dictionary<string, long> bytes = GrownBytes(moods, names);
            foreach (string name in names)
            {
                long weight;
                bytes.TryGetValue(name, out weight);
                spokes.Add(new GrowthSpoke { Name = name, Points = Growth.PointsOf((moods ? "m:" : "p:") + name), Bytes = weight });
            }
            return spokes;
        }

        // The grown lines' files on this side, "<personality> <mood> wanting <n>" and their overflow, weighed by name.
        private static Dictionary<string, long> GrownBytes(bool moods, string[] names)
        {
            Dictionary<string, long> found = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            Func<string> folder = Expansion.Folder;
            string root = folder == null ? null : folder();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return found;
            try
            {
                foreach (string file in Directory.GetFiles(root, "*.txt"))
                {
                    Match stem = Regex.Match(Path.GetFileNameWithoutExtension(file), @"^(.+) wanting \d+(?:_\d+)?$");
                    if (!stem.Success) continue;
                    string pair = stem.Groups[1].Value;
                    long size = new FileInfo(file).Length;
                    foreach (string name in names)
                    {
                        string clean = Regex.Replace(name, @"[^\w\- ]", "");
                        bool his = moods ? pair.EndsWith(" " + clean, StringComparison.OrdinalIgnoreCase) : pair.StartsWith(clean + " ", StringComparison.OrdinalIgnoreCase);
                        if (!his) continue;
                        long had;
                        found.TryGetValue(name, out had);
                        found[name] = had + size;
                    }
                }
            }
            catch (Exception caughtOne) { Brain.Caught(caughtOne); }
            return found;
        }

        private sealed class GrowthSheet : ModSheet
        {
            internal const int Wide = 760, Tall = 540, ListTop = 116, ListTall = 396, Right = LeftColumn.Side, RadarTop = 140;
            private readonly Key[] parts = new Key[2];
            private readonly Head listHead;
            private readonly Label pickedName;
            private readonly ScrollPanel listBox = new ScrollPanel { BackColor = Ground };
            private readonly SlimBar listBar = new SlimBar();
            private readonly GrowthList list = new GrowthList();
            private readonly GrowthRadar radar = new GrowthRadar();
            private bool moods;

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                LeftColumn.Frame(e.Graphics, listBox, Zoom);
            }

            internal GrowthSheet(Mainframe owner, float zoom) : base(owner, zoom, "Growth", "GROWTH >", Wide, Tall)
            {
                Words("", 28, 52, Wide - 56, 18, Small(), Faint);
                parts[0] = Make("Personalities", 200, 17, 120, 24, delegate { ShowPart(false); });
                parts[1] = Make("Moods", 326, 17, 120, 24, delegate { ShowPart(true); });
                listHead = Heading(this, "PERSONALITIES", new Point(Z(24), Z(84)), Type(10f * zoom, true), White, LeftColumn.Rim - 24);
                LeftColumn.Place(listBox, listBar, ListTop, ListTall, Zoom);
                listBox.Scrolled += delegate { listBar.Invalidate(); };
                list.Font = Type(9.5f * zoom);
                list.Zoom = zoom;
                list.Picked += delegate { Picked(); };
                listBox.Controls.Add(list);
                Controls.Add(listBox);
                listBar.Watch(listBox);
                Controls.Add(listBar);
                pickedName = Words("", Right, 114, Wide - Right - 28, 20, Type(10f * zoom, true), Rose);
                radar.Bounds = Box(Right, RadarTop, Wide - Right - 28, Tall - RadarTop - 28);
                radar.Font = Type(10f * zoom, true);
                radar.AccessibleName = "Growth radar";
                Controls.Add(radar);
                Frame.Fit(this);
                ResumeLayout(false);
                ShowPart(false);
            }

            internal void ShowPart(bool showMoods)
            {
                moods = showMoods;
                for (int i = 0; i < parts.Length; i++)
                {
                    bool lit = (i == 1) == moods;
                    parts[i].Accent = lit ? Cyan : Rose;
                    parts[i].Status = lit ? "Shown" : null;
                    parts[i].Invalidate();
                }
                listHead.Text = moods ? "MOODS" : "PERSONALITIES";
                listHead.Invalidate();
                List<GrowthSpoke> spokes = GrowthSpokes(moods);
                list.Spokes = spokes;
                radar.Spokes = spokes;
                list.Picked0 = spokes.Count > 0 ? 0 : -1;
                list.Size = new Size(Math.Max(1, listBox.ClientSize.Width), Math.Max(listBox.ClientSize.Height, spokes.Count * list.Row));
                list.Invalidate();
                Picked();
            }

            private void Picked()
            {
                int at = list.Picked0;
                radar.Picked = at;
                pickedName.Text = at >= 0 && at < list.Spokes.Count ? list.Spokes[at].Name : "";
                radar.Invalidate();
                list.Invalidate();
            }

            internal string Shown
            {
                get
                {
                    List<string> said = new List<string>();
                    foreach (GrowthSpoke one in list.Spokes) said.Add(one.Name + " " + one.Points.ToString("0.##", CultureInfo.InvariantCulture) + " " + one.Bytes);
                    return (moods ? "moods: " : "personalities: ") + string.Join(", ", said.ToArray());
                }
            }

            internal void Pick(int at) { list.Picked0 = at; Picked(); }
        }

        // Down the left: number, name, points; the picked row outlined.
        private sealed class GrowthList : Control
        {
            internal List<GrowthSpoke> Spokes = new List<GrowthSpoke>();
            internal int Picked0 = -1;
            internal float Zoom = 1;
            internal event Action Picked;
            internal int Row { get { return (int)Math.Round(28 * Zoom); } }

            internal GrowthList()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                Cursor = Cursors.Hand;
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                int at = e.Y / Math.Max(1, Row);
                if (at < 0 || at >= Spokes.Count) return;
                Picked0 = at;
                if (Picked != null) Picked();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(Ground);
                int row = Row, pad = (int)Math.Round(8 * Zoom);
                for (int i = 0; i < Spokes.Count; i++)
                {
                    Rectangle band = new Rectangle(0, i * row, Width - 1, row - (int)Math.Round(2 * Zoom));
                    if (i == Picked0)
                    {
                        using (Brush fill = new SolidBrush(Neon.Field)) g.FillRectangle(fill, band);
                        using (Pen edge = new Pen(Rose)) g.DrawRectangle(edge, band);
                    }
                    TextFormatFlags middle = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
                    TextRenderer.DrawText(g, (i + 1).ToString(CultureInfo.InvariantCulture), Font, new Rectangle(pad, band.Top, (int)Math.Round(26 * Zoom), band.Height), Cyan, middle);
                    TextRenderer.DrawText(g, Spokes[i].Name, Font, new Rectangle(pad + (int)Math.Round(30 * Zoom), band.Top, Width - pad * 2 - (int)Math.Round(90 * Zoom), band.Height), Rose, middle);
                    TextRenderer.DrawText(g, Spokes[i].Points.ToString("0.##", CultureInfo.InvariantCulture), Font, new Rectangle(Width - pad - (int)Math.Round(60 * Zoom), band.Top, (int)Math.Round(60 * Zoom), band.Height), Faint, middle | TextFormatFlags.Right);
                }
            }
        }

        // The radar: rings and spokes in her edge colour, the growth points filled, each spoke's grown lines as a line out
        // from the centre, only numbers round it.
        private sealed class GrowthRadar : Control
        {
            internal List<GrowthSpoke> Spokes = new List<GrowthSpoke>();
            internal int Picked = -1;

            internal GrowthRadar()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            }

            private PointF At(PointF centre, double radius, int i, int count)
            {
                double angle = -Math.PI / 2 + 2 * Math.PI * i / Math.Max(1, count);
                return new PointF(centre.X + (float)(radius * Math.Cos(angle)), centre.Y + (float)(radius * Math.Sin(angle)));
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(Ground);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int count = Spokes.Count;
                if (count == 0) return;
                float label = Font.Height * 1.6f;
                PointF centre = new PointF(Width / 2f, Height / 2f);
                double radius = Math.Max(10, Math.Min(Width, Height) / 2.0 - label);
                double mostPoints = 0, mostBytes = 0;
                foreach (GrowthSpoke one in Spokes) { mostPoints = Math.Max(mostPoints, one.Points); mostBytes = Math.Max(mostBytes, one.Bytes); }
                using (Pen grid = new Pen(Neon.Edge))
                {
                    for (int ring = 1; ring <= 4; ring++)
                    {
                        if (count < 3) { float r = (float)(radius * ring / 4); g.DrawEllipse(grid, centre.X - r, centre.Y - r, 2 * r, 2 * r); continue; }
                        PointF[] around = new PointF[count];
                        for (int i = 0; i < count; i++) around[i] = At(centre, radius * ring / 4, i, count);
                        g.DrawPolygon(grid, around);
                    }
                    for (int i = 0; i < count; i++) g.DrawLine(grid, centre, At(centre, radius, i, count));
                }
                // Each spoke's grown lines: a line out from the centre as long as its share of the heaviest.
                using (Pen grown = new Pen(Cyan, Math.Max(2f, Font.Height / 8f)))
                    for (int i = 0; i < count; i++)
                        if (mostBytes > 0 && Spokes[i].Bytes > 0) g.DrawLine(grown, centre, At(centre, radius * Spokes[i].Bytes / mostBytes, i, count));
                // Her growth points, filled.
                PointF[] shape = new PointF[count];
                for (int i = 0; i < count; i++) shape[i] = At(centre, mostPoints > 0 ? radius * Spokes[i].Points / mostPoints : 0, i, count);
                if (count >= 3 && mostPoints > 0)
                    using (Brush fill = new SolidBrush(Color.FromArgb(70, Rose))) g.FillPolygon(fill, shape);
                using (Pen edge = new Pen(Rose, Math.Max(1.5f, Font.Height / 12f)))
                {
                    if (count >= 3 && mostPoints > 0) g.DrawPolygon(edge, shape);
                    float dot = Math.Max(3f, Font.Height / 4f);
                    using (Brush mark = new SolidBrush(Rose))
                        foreach (PointF one in shape) g.FillEllipse(mark, one.X - dot, one.Y - dot, 2 * dot, 2 * dot);
                }
                for (int i = 0; i < count; i++)
                {
                    PointF at = At(centre, radius + label * 0.55, i, count);
                    string number = (i + 1).ToString(CultureInfo.InvariantCulture);
                    Size size = TextRenderer.MeasureText(number, Font);
                    TextRenderer.DrawText(g, number, Font, new Point((int)(at.X - size.Width / 2f), (int)(at.Y - size.Height / 2f)), i == Picked ? Rose : Cyan);
                }
            }
        }

#if CHECKS
        // Growth on stand-in rows and grown files: the plain side lists its own names with its points and the bytes of
        // their lines, the other side's points never show, Moods shows moods, a pick names the spoke; pictured at 100%.
        internal void SmokeGrowthRadar(List<string> checks, string output)
        {
            List<string> wrong = new List<string>();
            Func<List<string>> rowsWas = Growth.Rows;
            Func<string> folderWas = Expansion.Folder;
            Func<bool, string[]> namesWas = GrowthNames;
            string root = Path.Combine(output, "growth-lines");
            Directory.CreateDirectory(root);
            List<string> rows = new List<string>
            {
                "plain\tp:Caregiver\t42", "plain\tp:Welcome Home\t88", "plain\tp:Devoted\t61", "plain\tp:Emo\t17", "plain\tp:Bratty\t30",
                "plain\tm:Sleepy\t12", "plain\tm:Hyper\t5", "plain\tm:Moody\t20", "senpai\tp:Caregiver\t999", "senpai\tm:Sleepy\t777"
            };
            File.WriteAllText(Path.Combine(root, "Welcome Home calm wanting 3.txt"), new string('x', 4000));
            File.WriteAllText(Path.Combine(root, "Welcome Home Sleepy wanting 2_1.txt"), new string('x', 1000));
            File.WriteAllText(Path.Combine(root, "Devoted Moody wanting 5.txt"), new string('x', 2500));
            try
            {
                Growth.Rows = delegate { return rows; };
                Expansion.Folder = delegate { return root; };
                GrowthNames = delegate(bool moods) { return moods ? new[] { "Sleepy", "Hyper", "Moody" } : new[] { "Caregiver", "Welcome Home", "Devoted", "Emo", "Bratty" }; };
                OpenGrowth();
                Application.DoEvents();
                string people = growthSheet.Shown;
                if (people != "personalities: Caregiver 42 0, Welcome Home 88 5000, Devoted 61 2500, Emo 17 0, Bratty 30 0") wrong.Add("personalities: " + people);
                growthSheet.Pick(1);
                Application.DoEvents();
                using (Bitmap shot = new Bitmap(growthSheet.Width, growthSheet.Height))
                {
                    growthSheet.DrawToBitmap(shot, new Rectangle(Point.Empty, shot.Size));
                    using (Bitmap cut = Outlined(shot, growthSheet)) cut.Save(Path.Combine(output, "growth-radar.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                string crowded = growthSheet.Crowded();
                if (crowded != null) wrong.Add("the window is crowded: " + crowded);
                growthSheet.ShowPart(true);
                Application.DoEvents();
                string moodsShown = growthSheet.Shown;
                if (moodsShown != "moods: Sleepy 12 1000, Hyper 5 0, Moody 20 2500") wrong.Add("moods: " + moodsShown);
                if (moodsShown.Contains("777") || people.Contains("999")) wrong.Add("the other side's points showed");
                using (Bitmap shot = new Bitmap(growthSheet.Width, growthSheet.Height))
                {
                    growthSheet.DrawToBitmap(shot, new Rectangle(Point.Empty, shot.Size));
                    using (Bitmap cut = Outlined(shot, growthSheet)) cut.Save(Path.Combine(output, "growth-radar-moods.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                bool tile = Tools().Exists(tool => tool.Id == "growth" && tool.Section == "capri" && tool.Icon == "radar");
                if (!tile) wrong.Add("no Growth tile in CAPRI with the radar icon");
            }
            finally
            {
                if (growthSheet != null && !growthSheet.IsDisposed) growthSheet.Close();
                Growth.Rows = rowsWas;
                Expansion.Folder = folderWas;
                GrowthNames = namesWas;
            }
            if (wrong.Count > 0) throw new Exception("Growth went wrong: " + string.Join("; ", wrong.ToArray()));
            checks.Add("PASS Growth (381): a Growth tile with a radar icon in CAPRI; its window lists her personalities, or her moods, by number, name and points on her side only, the other side's points never shown; the radar fills her growth points and draws each spoke's grown lines by their files' size, only numbers round it; uncrowded (growth-radar.png, growth-radar-moods.png)");
        }
#endif
    }
}
