using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Toggles' Sections side: each section with its grip, pen and X, its apps under it, Add at the bottom. A drag moves a
    // section, or an app within or across sections, the cyan line showing where, as panels move on Settings.
    internal sealed partial class Mainframe
    {
        private sealed partial class ToggleSheet
        {
            private bool sectionsShown;
            private SectionList sectionList;
            // The row picked on the Sections side, kept across the side being made again after a move.
            private string sectionPicked;

            internal void PickSections()
            {
                picked = null;
                sectionsShown = true;
                foreach (Control one in list.Controls)
                {
                    OptionsKey key = one as OptionsKey;
                    bool lit = key != null && key.Item != null && Equals(key.Item.Tag, SectionsTag);
                    if (key != null && key.Picked != lit) { key.Picked = lit; key.Invalidate(); }
                }
                FillSide();
            }

            private void SectionsSide()
            {
                int y = ListTop, wide = Wide - SideLeft - 16;
                Label heading = new Note { Text = "SECTIONS", Font = Type(10f * Zoom, true), ForeColor = Hot, BackColor = Ground, AutoSize = false, Bounds = Box(SideLeft, y, wide, 22) };
                Controls.Add(heading);
                side.Add(heading);
                y += 32;
                sectionList = new SectionList(Frame.capri, Zoom) { Font = Font, Location = new Point(Z(SideLeft), Z(y)) };
                sectionList.Width = Z(wide);
                sectionList.Fill();
                sectionList.Renaming += Rename;
                sectionList.Picked = sectionPicked;
                sectionList.PickedChanged += delegate { sectionPicked = sectionList.Picked; };
                bool focused = sectionPicked != null;
                Controls.Add(sectionList);
                if (focused) BeginInvoke((Action)delegate { if (sectionList != null && !sectionList.IsDisposed) sectionList.Focus(); });
                side.Add(sectionList);
                Key add = new Key { Text = "Add", Font = Font, Bounds = new Rectangle(Z(SideLeft), sectionList.Bottom + Z(8), Z(80), Z(26)) };
                add.AccessibleName = "Add a section";
                add.Click += delegate
                {
                    List<KeyValuePair<string, string>> all = Frame.capri.Sections;
                    string id = Frame.capri.NewSectionId();
                    all.Add(new KeyValuePair<string, string>(id, "New section"));
                    Frame.capri.SaveSections(all);
                    if (sectionList != null && !sectionList.IsDisposed) sectionList.BeginRename(id);
                };
                Controls.Add(add);
                side.Add(add);
            }

            // The pen: the section's name becomes a box to type in; Enter keeps it, Esc puts it back.
            private void Rename(string id, Rectangle row)
            {
                string was = Frame.capri.SectionName(id);
                TextBox box = new TextBox { Text = was, Font = Font, BorderStyle = BorderStyle.None, BackColor = Neon.Field, ForeColor = Cyan, Bounds = new Rectangle(sectionList.Left + row.Left + Z(20), sectionList.Top + row.Top + Z(4), Z(180), row.Height - Z(6)), MaxLength = 24 };
                box.AccessibleName = "Section name";
                bool done = false;
                Action keep = delegate
                {
                    if (done) return;
                    done = true;
                    string named = box.Text.Trim();
                    Controls.Remove(box);
                    side.Remove(box);
                    BeginInvoke((Action)delegate { box.Dispose(); });
                    if (named.Length == 0 || named == was) { if (sectionList != null && !sectionList.IsDisposed) sectionList.Invalidate(); return; }
                    List<KeyValuePair<string, string>> all = Frame.capri.Sections;
                    int at = all.FindIndex(one => one.Key == id);
                    if (at < 0) return;
                    all[at] = new KeyValuePair<string, string>(id, named);
                    Frame.capri.SaveSections(all);
                };
                box.KeyDown += delegate(object sender, KeyEventArgs e)
                {
                    if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; keep(); }
                    else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; box.Text = was; keep(); }
                };
                box.Leave += delegate { keep(); };
                Controls.Add(box);
                side.Add(box);
                box.BringToFront();
                box.Focus();
                box.SelectAll();
            }

            // For the checks: the side as text, "[Fun] Music Chess | [Homelab] | ...", and a drag done by hand.
            internal string SectionsShown { get { return sectionList == null || sectionList.IsDisposed ? null : sectionList.Shown; } }

            internal void DragSection(string what, string onto, bool below) { if (sectionList != null && !sectionList.IsDisposed) sectionList.DropBy(what, onto, below); }

            internal void PressSection(string id, bool remove) { if (sectionList != null && !sectionList.IsDisposed) sectionList.Press(id, remove); }

            internal void ShiftSection(string id, int by) { if (sectionList != null && !sectionList.IsDisposed) sectionList.ShiftBy(id, by); }
        }

        internal string SectionsText { get { return toggleSheet != null && !toggleSheet.IsDisposed ? toggleSheet.SectionsShown : null; } }

        internal void OpenSections() { OpenToggles(); toggleSheet.PickSections(); }

        internal void DragSection(string what, string onto, bool below) { if (toggleSheet != null && !toggleSheet.IsDisposed) toggleSheet.DragSection(what, onto, below); }

        internal void PressSection(string id, bool remove) { if (toggleSheet != null && !toggleSheet.IsDisposed) toggleSheet.PressSection(id, remove); }

        // For the checks: Ctrl and an arrow on a row of the Sections side ("s:fun" for a section).
        internal void ShiftSection(string id, int by) { if (toggleSheet != null && !toggleSheet.IsDisposed) toggleSheet.ShiftSection(id, by); }

        // The list itself, drawn as one control: a row per section and per app, in their order.
        private sealed class SectionList : Control
        {
            private sealed class Line { internal bool Section; internal string Id, Name, Owner; internal Rectangle Bounds; }

            // The row picked, by its id ("s:" a section's, an app's own): Up and Down pick, Ctrl with them moves it, as
            // Ctrl and an arrow move a tile on TOOLS.
            internal string Picked;
            internal event Action PickedChanged;

            private static string KeyOf(Line line) { return (line.Section ? "s:" : "") + line.Id; }

            private Line PickedLine { get { return Picked == null ? null : lines.Find(one => KeyOf(one) == Picked); } }

            private void PickLine(Line line)
            {
                Picked = line == null ? null : KeyOf(line);
                if (PickedChanged != null) PickedChanged();
                Invalidate();
            }

            protected override bool IsInputKey(Keys keyData)
            {
                Keys key = keyData & Keys.KeyCode;
                return key == Keys.Up || key == Keys.Down || base.IsInputKey(keyData);
            }

            protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); if (PickedLine == null && lines.Count > 0) PickLine(lines[0]); Invalidate(); }

            protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                Line line = PickedLine;
                if (line == null || (e.KeyCode != Keys.Up && e.KeyCode != Keys.Down)) return;
                e.Handled = true;
                int by = e.KeyCode == Keys.Up ? -1 : 1, at = lines.IndexOf(line);
                if (!e.Control) { if (at + by >= 0 && at + by < lines.Count) PickLine(lines[at + by]); return; }
                Shift(line, by);
            }

            // Ctrl and an arrow: a section past its neighbour; an app past the one beside it, or into the next or previous
            // section at its near end.
            private void Shift(Line line, int by)
            {
                if (Fixed(line)) return;
                if (line.Section)
                {
                    List<Line> sections = lines.FindAll(one => one.Section && !Fixed(one));
                    int at = sections.IndexOf(line);
                    if (at + by < 0 || at + by >= sections.Count) return;
                    Drop(line, sections[at + by], by > 0);
                    return;
                }
                List<Line> heads = lines.FindAll(one => one.Section);
                List<Line> mine = lines.FindAll(one => !one.Section && one.Owner == line.Owner);
                int place = mine.IndexOf(line), section = heads.FindIndex(one => one.Id == line.Owner);
                if (place + by >= 0 && place + by < mine.Count) { Drop(line, mine[place + by], by > 0); return; }
                if (section + by < 0 || section + by >= heads.Count) return;
                Line next = heads[section + by];
                if (by > 0) capri.MoveApp(line.Id, next.Id, FirstIn(next.Id, line.Id));
                else capri.MoveApp(line.Id, next.Id, null);
            }

            internal void ShiftBy(string id, int by)
            {
                Line line = lines.Find(one => one.Id == id || KeyOf(one) == id);
                if (line != null) { PickLine(line); Shift(line, by); }
            }

            private readonly Companion capri;
            private readonly float zoom;
            private readonly List<Line> lines = new List<Line>();
            private Line dragging;
            private Point pressedAt;
            private bool moved;
            private int dropY = -1;
            private Line dropOnto;
            private bool dropBelow;
            internal event Action<string, Rectangle> Renaming;
            private const int SectionTall = 24, AppTall = 22, Gap = 4, Indent = 20;

            internal SectionList(Companion owner, float zoomed)
            {
                capri = owner;
                zoom = zoomed;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true);
                TabStop = true;
                BackColor = Neon.Ground;
                AccessibleName = "Sections and their apps: drag to order or move";
            }

            private int Z(int value) { return (int)Math.Round(value * zoom); }

            internal void Fill()
            {
                lines.Clear();
                List<Companion.SectionApp> apps = capri.SectionApps();
                List<KeyValuePair<string, string>> sections = capri.Sections;
                sections.Add(new KeyValuePair<string, string>(Companion.Uncategorised, Companion.UncategorisedName));
                int y = 0;
                foreach (KeyValuePair<string, string> section in sections)
                {
                    lines.Add(new Line { Section = true, Id = section.Key, Name = section.Value, Bounds = new Rectangle(0, y, Width, Z(SectionTall)) });
                    y += Z(SectionTall) + Z(Gap);
                    foreach (Companion.SectionApp app in apps)
                    {
                        if (app.Section != section.Key) continue;
                        lines.Add(new Line { Id = app.Id, Name = app.Name, Owner = section.Key, Bounds = new Rectangle(Z(Indent), y, Width - Z(Indent), Z(AppTall)) });
                        y += Z(AppTall);
                    }
                    y += Z(Gap) * 2;
                }
                Height = Math.Max(Z(SectionTall), y);
                Invalidate();
            }

            internal string Shown
            {
                get
                {
                    List<string> said = new List<string>();
                    foreach (Line line in lines) said.Add(line.Section ? "[" + line.Name + "]" : line.Name);
                    return string.Join(" ", said.ToArray());
                }
            }

            private bool Fixed(Line line) { return line.Section && line.Id == Companion.Uncategorised; }

            // Her marks, as her close X is drawn: 10 px square, centred on the row's middle, the X 10 px in from the row's
            // right edge and the pen 10 px before it. Each takes a click a little round it.
            private Rectangle Mark(Line line, int fromRight) { return new Rectangle(line.Bounds.Right - Z(fromRight) - Z(10), line.Bounds.Top + (line.Bounds.Height - Z(10)) / 2, Z(10), Z(10)); }

            private Rectangle Cross(Line line) { return Mark(line, 10); }

            private Rectangle Pen(Line line) { return Mark(line, 30); }

            private static Rectangle Near(Rectangle mark, int by) { mark.Inflate(by, by); return mark; }

            private Rectangle Grip(Line line) { return new Rectangle(line.Bounds.Left, line.Bounds.Top, Z(18), line.Bounds.Height); }

            internal void BeginRename(string id)
            {
                Line line = lines.Find(one => one.Section && one.Id == id);
                if (line != null && Renaming != null) Renaming(id, line.Bounds);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                Line line = lines.Find(one => one.Bounds.Contains(e.Location));
                if (line == null || e.Button != MouseButtons.Left) return;
                Focus();
                PickLine(line);
                if (line.Section && !Fixed(line) && Near(Pen(line), Z(4)).Contains(e.Location)) { BeginRename(line.Id); return; }
                if (line.Section && !Fixed(line) && Near(Cross(line), Z(4)).Contains(e.Location)) { capri.RemoveSection(line.Id); return; }
                if (Fixed(line)) return;
                dragging = line;
                pressedAt = e.Location;
                moved = false;
                Capture = true;
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                Line over = lines.Find(one => one.Bounds.Contains(e.Location));
                Cursor = dragging != null ? Cursors.SizeAll : over != null && !Fixed(over) && (Grip(over).Contains(e.Location) || over.Section && (Near(Pen(over), Z(4)).Contains(e.Location) || Near(Cross(over), Z(4)).Contains(e.Location))) ? Cursors.Hand : Cursors.Default;
                if (dragging == null) return;
                if (!moved && Math.Abs(e.Y - pressedAt.Y) < SystemInformation.DragSize.Height) return;
                moved = true;
                Aim(e.Y);
                Invalidate();
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                Capture = false;
                Line was = dragging;
                dragging = null;
                if (was != null && moved && dropOnto != null) Drop(was, dropOnto, dropBelow);
                dropY = -1;
                dropOnto = null;
                Invalidate();
            }

            // Where a drag at y lands: before or after the line under it.
            private void Aim(int y)
            {
                dropOnto = null;
                foreach (Line line in lines)
                {
                    if (dragging.Section && !line.Section) continue;
                    if (y < line.Bounds.Top - Z(Gap) || y >= line.Bounds.Bottom + Z(Gap)) continue;
                    dropOnto = line;
                    dropBelow = y >= line.Bounds.Top + line.Bounds.Height / 2;
                }
                if (dropOnto == null) { dropY = -1; return; }
                // A section never goes under Uncategorised; an app dropped on a section's name row goes into it.
                if (dragging.Section && Fixed(dropOnto)) dropBelow = false;
                dropY = dropOnto.Section && !dragging.Section ? dropOnto.Bounds.Bottom + 1 : dropBelow ? dropOnto.Bounds.Bottom + 1 : dropOnto.Bounds.Top - 2;
            }

            private void Drop(Line what, Line onto, bool below)
            {
                if (what == onto) return;
                if (what.Section)
                {
                    List<KeyValuePair<string, string>> all = capri.Sections;
                    KeyValuePair<string, string> moving = all.Find(one => one.Key == what.Id);
                    all.RemoveAll(one => one.Key == what.Id);
                    int at = Fixed(onto) ? all.Count : all.FindIndex(one => one.Key == onto.Id) + (below ? 1 : 0);
                    all.Insert(Math.Max(0, Math.Min(all.Count, at)), moving);
                    capri.SaveSections(all);
                    return;
                }
                if (onto.Section) { capri.MoveApp(what.Id, onto.Id, FirstIn(onto.Id, what.Id)); return; }
                string before = below ? NextIn(onto) : onto.Id;
                capri.MoveApp(what.Id, onto.Owner, before == what.Id ? NextIn(lines.Find(one => one.Id == what.Id && !one.Section)) : before);
            }

            private string FirstIn(string section, string not)
            {
                Line first = lines.Find(one => !one.Section && one.Owner == section && one.Id != not);
                return first == null ? null : first.Id;
            }

            private string NextIn(Line line)
            {
                int at = lines.IndexOf(line);
                return at >= 0 && at + 1 < lines.Count && !lines[at + 1].Section ? lines[at + 1].Id : null;
            }

            // For the checks: a drag of the line named onto another, without the mouse.
            internal void DropBy(string what, string onto, bool below)
            {
                Line from = lines.Find(one => one.Name == what || one.Id == what), to = lines.Find(one => one != from && (one.Name == onto || one.Id == onto));
                if (from != null && to != null) Drop(from, to, below);
            }

            internal void Press(string id, bool remove)
            {
                Line line = lines.Find(one => one.Section && one.Id == id);
                if (line == null || Fixed(line)) return;
                if (remove) capri.RemoveSection(id); else BeginRename(id);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Neon.Ground);
                float line = Math.Max(1f, zoom);
                foreach (Line one in lines)
                {
                    Rectangle box = one.Bounds;
                    if (one.Section)
                    {
                        using (GraphicsPath edge = Neon.Cut(new RectangleF(box.X + line / 2, box.Y + line / 2, box.Width - line, box.Height - line), 0, 0, Z(6), 0))
                        using (Brush fill = new SolidBrush(Neon.Field))
                        using (Pen rim = new Pen(Neon.Edge, line))
                        {
                            g.FillPath(fill, edge);
                            g.DrawPath(rim, edge);
                        }
                    }
                    if (!Fixed(one)) Dots(g, Grip(one));
                    TextRenderer.DrawText(g, one.Name, Font, new Rectangle(box.X + Z(20), box.Y, box.Width - Z(64), box.Height), one.Section ? Neon.Cyan : Neon.Rose, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    if (one.Section && !Fixed(one))
                    {
                        using (Pen ink = new Pen(Neon.Hot, 1.5f * zoom))
                        {
                            Rectangle pen = Pen(one), cross = Cross(one);
                            g.DrawLine(ink, cross.Left, cross.Top, cross.Right, cross.Bottom);
                            g.DrawLine(ink, cross.Left, cross.Bottom, cross.Right, cross.Top);
                            // The pen: one outline, its body slanted up to the right and its tip at the bottom left.
                            float w = pen.Width / 10f;
                            g.DrawPolygon(ink, new[]
                            {
                                new PointF(pen.Left, pen.Bottom), new PointF(pen.Left + 1.2f * w, pen.Bottom - 3.6f * w), new PointF(pen.Left + 7.6f * w, pen.Top + 0.4f * w),
                                new PointF(pen.Right, pen.Top + 2.8f * w), new PointF(pen.Left + 3.6f * w, pen.Bottom - 1.2f * w)
                            });
                        }
                    }
                }
                // The picked row, while the list has the keyboard: a thin cyan line under it, as her keyboard mark.
                Line held = Focused ? PickedLine : null;
                if (held != null)
                    using (Pen mark = new Pen(Neon.Cyan, line))
                        g.DrawRectangle(mark, held.Bounds.X, held.Bounds.Y, held.Bounds.Width - line, held.Bounds.Height - line);
                if (dropY >= 0)
                    using (Brush mark = new SolidBrush(Neon.Cyan))
                        g.FillRectangle(mark, 0, dropY - line, Width, 2 * line);
            }

            private void Dots(Graphics g, Rectangle at)
            {
                float dot = Math.Max(1.5f, 2 * zoom), x = at.X + Z(6), y = at.Y + at.Height / 2f - 3 * dot;
                using (Brush ink = new SolidBrush(Neon.Faint))
                    for (int col = 0; col < 2; col++)
                        for (int row = 0; row < 3; row++)
                            g.FillRectangle(ink, x + col * 2 * dot, y + row * 2 * dot, dot, dot);
            }
        }
    }
}
