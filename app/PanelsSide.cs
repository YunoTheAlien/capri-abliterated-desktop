using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Toggles' Panels: each Settings panel in the Sec: folds' band, its rows under it. A grip moves a row, Hide and Show
    // take it off and back, Default puts a panel back; their own categories are made and named here.
    internal sealed partial class Mainframe
    {
        internal const string PanelsRow = "toggles:panels";

        private sealed partial class ToggleSheet
        {
            private bool panelsShown;
            private PanelsList panelsList;

            internal void PickPanels()
            {
                picked = null;
                sectionsShown = false;
                pickedSettings = null;
                panelsShown = true;
                foreach (Control one in list.Controls)
                {
                    OptionsKey key = one as OptionsKey;
                    bool lit = key != null && key.Item != null && key.Item.Name == PanelsRow;
                    if (key != null && key.Picked != lit) { key.Picked = lit; key.Invalidate(); }
                }
                FillSide();
            }

            private void PanelsSide()
            {
                int y = ListTop, wide = Wide - SideLeft - 16;
                Label heading = new Note { Text = "PANELS", Font = Type(10f * Zoom, true), ForeColor = Hot, BackColor = Ground, AutoSize = false, Bounds = Box(SideLeft, y, wide, 22) };
                Controls.Add(heading);
                side.Add(heading);
                y += 32;
                // The list scrolls in its own room above its two keys, her slim bar beside it, as the left column does.
                ScrollPanel room = new ScrollPanel { BackColor = Ground, Bounds = Box(SideLeft, y, wide - 10, Tall - y - 28 - 34) };
                SlimBar roomBar = new SlimBar { BackColor = Ground, Bounds = new Rectangle(room.Right + Z(3), room.Top + Z(2), Z(SlimBar.Thin), room.Height - Z(4)) };
                panelsList = new PanelsList(Frame, Zoom) { Font = Font, Location = Point.Empty };
                panelsList.Width = room.Width - Z(2);
                panelsList.Fill();
                panelsList.Renaming += RenamePanel;
                room.Controls.Add(panelsList);
                room.Scrolled += delegate { roomBar.Invalidate(); };
                Controls.Add(room);
                side.Add(room);
                roomBar.Watch(room);
                Controls.Add(roomBar);
                side.Add(roomBar);
                Key add = Make("New category", SideLeft, 0, 120, 22, delegate
                {
                    string name = "New category";
                    for (int n = 2; Frame.AddOwnPanel(name) != null && n < 50; n++) name = "New category " + n;
                    if (panelsList != null && !panelsList.IsDisposed) { panelsList.Fill(); panelsList.BeginRename(Companion.OwnPrefix + name); }
                });
                add.Top = room.Bottom + Z(10);
                add.AccessibleName = "New category";
                side.Add(add);
                Key share = Make("Save as layout mod", SideLeft + 128, 0, 150, 22, delegate { Frame.capri.SaveLayoutMod(null); });
                share.Top = add.Top;
                share.AccessibleName = "Save as layout mod";
                side.Add(share);
            }

            private void RenamePanel(string id, Rectangle row)
            {
                string was = id.Substring(Companion.OwnPrefix.Length);
                Point at = PointToClient(panelsList.PointToScreen(new Point(row.Left, row.Top)));
                TextBox box = new TextBox { Text = was, Font = Font, BorderStyle = BorderStyle.None, BackColor = Neon.Field, ForeColor = White, Bounds = new Rectangle(at.X + Z(20), at.Y + Z(5), Z(180), row.Height - Z(8)), MaxLength = 24 };
                box.AccessibleName = "Category name";
                bool done = false;
                Action keep = delegate
                {
                    if (done) return;
                    done = true;
                    string named = box.Text.Trim();
                    Controls.Remove(box);
                    side.Remove(box);
                    BeginInvoke((Action)delegate { box.Dispose(); });
                    if (named.Length > 0 && named != was) Frame.RenameOwnPanel(id, named);
                    if (panelsList != null && !panelsList.IsDisposed) panelsList.Fill();
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

            // For the checks: the side as text, "[VOICE] Volume Sounds (hidden) Hearing | ...", and its presses by name.
            internal string PanelsShown { get { return panelsList == null || panelsList.IsDisposed ? null : panelsList.Shown; } }

            internal PanelsList PanelsListNow { get { return panelsList; } }
        }

        internal string PanelsText { get { return toggleSheet != null && !toggleSheet.IsDisposed ? toggleSheet.PanelsShown : null; } }

        internal void OpenPanels() { OpenToggles(); toggleSheet.UnfoldAll(); toggleSheet.PickPanels(); if (toggleSheet.PanelsListNow != null) toggleSheet.PanelsListNow.OpenAll(); }

        // The list, one control: each panel a band as the Sec: folds draw theirs, its rows under it while it is open.
        internal sealed class PanelsList : Control
        {
            private sealed class Line { internal bool Panel, Own, Hidden, Movable; internal string Id, Name, Owner; internal Rectangle Bounds; }

            private readonly Mainframe frame;
            private readonly float zoom;
            private readonly List<Line> lines = new List<Line>();
            private readonly List<Control> keys = new List<Control>();
            // Each panel starts folded, as the Sec: folds do; one opened stays open while the side is up.
            private readonly HashSet<string> closed = new HashSet<string>(StringComparer.Ordinal);
            private bool filled;

            internal void OpenAll() { closed.Clear(); Fill(); }
            private Line dragging, dropOnto;
            private Point pressedAt;
            private bool moved, dropBelow;
            private int dropY = -1;
            internal event Action<string, Rectangle> Renaming;
            private const int Band = 27, RowTall = 26, Gap = 6, Indent = 20;

            internal PanelsList(Mainframe owner, float zoomed)
            {
                frame = owner;
                zoom = zoomed;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true);
                BackColor = Neon.Ground;
                AccessibleName = "Settings panels and their rows: drag a row to move it";
            }

            private int Z(int value) { return (int)Math.Round(value * zoom); }

            internal void Fill()
            {
                foreach (Control one in keys) { Controls.Remove(one); one.Dispose(); }
                keys.Clear();
                lines.Clear();
                int y = 0;
                List<PanelRowsInfo> panels = frame.PanelRowsNow();
                if (!filled) { filled = true; foreach (PanelRowsInfo one in panels) closed.Add(one.Id); }
                foreach (PanelRowsInfo panel in panels)
                {
                    Line band = new Line { Panel = true, Own = panel.Own, Id = panel.Id, Name = panel.Name, Bounds = new Rectangle(0, y, Width, Z(Band)) };
                    lines.Add(band);
                    if (!panel.Own)
                    {
                        string id = panel.Id;
                        Key reset = new Key { Text = "Default", Font = Font, Bounds = new Rectangle(Width - Z(44) - Z(64), y + (Z(Band) - Z(22)) / 2, Z(64), Z(22)) };
                        reset.AccessibleName = panel.Name + ": Default";
                        reset.Click += delegate { frame.DefaultPanel(id); Fill(); };
                        Controls.Add(reset);
                        keys.Add(reset);
                    }
                    y += Z(Band) + Z(Gap);
                    if (closed.Contains(panel.Id)) continue;
                    foreach (RowInfo row in panel.Rows)
                    {
                        if (!row.Movable) continue;
                        Line line = new Line { Id = row.Key, Name = row.Name, Owner = panel.Id, Hidden = row.Hidden, Movable = true, Bounds = new Rectangle(Z(Indent), y, Width - Z(Indent), Z(RowTall)) };
                        lines.Add(line);
                        string key = row.Key;
                        bool hidden = row.Hidden;
                        Key flip = new Key { Text = hidden ? "Show" : "Hide", Font = Font, Bounds = new Rectangle(Width - Z(64), y + (Z(RowTall) - Z(22)) / 2, Z(64), Z(22)) };
                        if (hidden) flip.Accent = Neon.Cyan;
                        flip.AccessibleName = row.Name + ": " + flip.Text;
                        flip.Click += delegate { frame.HideRow(key, !hidden); Fill(); };
                        Controls.Add(flip);
                        keys.Add(flip);
                        y += Z(RowTall);
                    }
                    y += Z(Gap);
                }
                Height = Math.Max(Z(Band), y);
                Invalidate();
            }

            internal string Shown
            {
                get
                {
                    List<string> said = new List<string>();
                    foreach (Line line in lines) said.Add(line.Panel ? "[" + line.Name + "]" : line.Name + (line.Hidden ? " (hidden)" : ""));
                    return string.Join(" ", said.ToArray());
                }
            }

            private Rectangle Mark(Line line, int fromRight) { return new Rectangle(line.Bounds.Right - Z(fromRight) - Z(10), line.Bounds.Top + (line.Bounds.Height - Z(10)) / 2, Z(10), Z(10)); }

            private Rectangle Cross(Line line) { return Mark(line, 44); }

            private Rectangle Pen(Line line) { return Mark(line, 64); }

            private static Rectangle Near(Rectangle mark, int by) { mark.Inflate(by, by); return mark; }

            private Rectangle Grip(Line line) { return new Rectangle(line.Bounds.Left, line.Bounds.Top, Z(18), line.Bounds.Height); }

            internal void BeginRename(string id)
            {
                Line line = lines.Find(one => one.Panel && one.Id == id);
                if (line != null && Renaming != null) Renaming(id, line.Bounds);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                Line line = lines.Find(one => one.Bounds.Contains(e.Location));
                if (line == null || e.Button != MouseButtons.Left) return;
                Focus();
                if (line.Panel)
                {
                    if (line.Own && Near(Pen(line), Z(4)).Contains(e.Location)) { BeginRename(line.Id); return; }
                    if (line.Own && Near(Cross(line), Z(4)).Contains(e.Location)) { frame.RemoveOwnPanel(line.Id); Fill(); return; }
                    // The band's fold mark, as the Sec: folds have: open shows its rows, closed hides them here.
                    if (!closed.Remove(line.Id)) closed.Add(line.Id);
                    Fill();
                    return;
                }
                dragging = line;
                pressedAt = e.Location;
                moved = false;
                Capture = true;
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                Line over = lines.Find(one => one.Bounds.Contains(e.Location));
                Cursor = dragging != null ? Cursors.SizeAll : over != null && (over.Panel || Grip(over).Contains(e.Location)) ? Cursors.Hand : Cursors.Default;
                Hover(dragging == null ? over : null);
                if (dragging == null) return;
                if (!moved && Math.Abs(e.Y - pressedAt.Y) < SystemInformation.DragSize.Height) return;
                moved = true;
                Aim(e.Y);
                Invalidate();
            }

            // A name cut to fit gives its whole words in the tip while the pointer is on its line.
            private Line hovered;

            private void Hover(Line over)
            {
                if (over == hovered) return;
                hovered = over;
                Mainframe.CutWords.Leave(this);
                if (over == null || !Cut(over)) return;
                Mainframe.CutWords.Enter(this, over.Name);
                Mainframe.CutWords.AlsoCut(this, true);
            }

            private bool Cut(Line one)
            {
                string shown = one.Panel ? one.Name.ToUpperInvariant() : one.Name;
                return TextRenderer.MeasureText(shown, Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width > Words(one).Width;
            }

            // Where a line's name is drawn: one line at the row's height, cut with "..." when long.
            private Rectangle Words(Line one)
            {
                Rectangle box = one.Bounds;
                return one.Panel ? new Rectangle(box.X + Z(14), box.Y, box.Width - Z(140), box.Height) : new Rectangle(box.X + Z(20), box.Y, box.Width - Z(96), box.Height);
            }

            protected override void OnMouseLeave(EventArgs e) { Hover(null); base.OnMouseLeave(e); }

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

            private void Aim(int y)
            {
                dropOnto = null;
                foreach (Line line in lines)
                {
                    if (y < line.Bounds.Top - Z(Gap) || y >= line.Bounds.Bottom + Z(Gap)) continue;
                    dropOnto = line;
                    dropBelow = y >= line.Bounds.Top + line.Bounds.Height / 2;
                }
                dropY = dropOnto == null ? -1 : dropOnto.Panel ? dropOnto.Bounds.Bottom + 1 : dropBelow ? dropOnto.Bounds.Bottom + 1 : dropOnto.Bounds.Top - 2;
            }

            private void Drop(Line what, Line onto, bool below)
            {
                if (what == onto) return;
                if (onto.Panel) { frame.MoveRow(what.Id, onto.Id, FirstIn(onto.Id, what.Id)); Fill(); return; }
                string before = below ? NextIn(onto) : onto.Id;
                if (before == what.Id) before = NextIn(what);
                frame.MoveRow(what.Id, onto.Owner, before);
                Fill();
            }

            private string FirstIn(string panel, string not)
            {
                Line first = lines.Find(one => !one.Panel && one.Owner == panel && one.Id != not);
                return first == null ? null : first.Id;
            }

            private string NextIn(Line line)
            {
                int at = lines.IndexOf(line);
                return at >= 0 && at + 1 < lines.Count && !lines[at + 1].Panel ? lines[at + 1].Id : null;
            }

            // For the checks: a drag of the row named onto another row or a panel, without the mouse.
            internal void DropBy(string what, string onto, bool below)
            {
                Line from = lines.Find(one => !one.Panel && (one.Name == what || one.Id == what)), to = lines.Find(one => one != from && (one.Name == onto || one.Id == onto));
                if (from != null && to != null) Drop(from, to, below);
            }

            internal void Press(string named)
            {
                foreach (Control one in keys) if (one.AccessibleName == named) { ((Key)one).PerformClick(); return; }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Neon.Ground);
                foreach (Line one in lines)
                {
                    Rectangle box = one.Bounds;
                    if (one.Panel)
                    {
                        // The Sec: folds' band, red bar and caps, with its fold mark filled while open.
                        DrawBand(g, new RectangleF(box.X, box.Y, box.Width, box.Height), zoom);
                        TextRenderer.DrawText(g, one.Name.ToUpperInvariant(), Font, Words(one), Neon.White, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                        Mainframe.Drop(g, box.Right - Z(24), box.Y + box.Height / 2f, zoom, Neon.Cyan, !closed.Contains(one.Id));
                        if (one.Own)
                            using (Pen ink = new Pen(Neon.Hot, 1.5f * zoom))
                            {
                                Rectangle pen = Pen(one), cross = Cross(one);
                                g.DrawLine(ink, cross.Left, cross.Top, cross.Right, cross.Bottom);
                                g.DrawLine(ink, cross.Left, cross.Bottom, cross.Right, cross.Top);
                                float w = pen.Width / 10f;
                                g.DrawPolygon(ink, new[] { new PointF(pen.Left, pen.Bottom), new PointF(pen.Left + 1.2f * w, pen.Bottom - 3.6f * w), new PointF(pen.Left + 7.6f * w, pen.Top + 0.4f * w), new PointF(pen.Right, pen.Top + 2.8f * w), new PointF(pen.Left + 3.6f * w, pen.Bottom - 1.2f * w) });
                            }
                        continue;
                    }
                    Dots(g, Grip(one));
                    Rectangle words = Words(one);
                    TextRenderer.DrawText(g, one.Name, Font, words, one.Hidden ? Neon.Faint : Neon.Rose, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    if (one.Hidden)
                    {
                        int wide = Math.Min(words.Width, TextRenderer.MeasureText(one.Name, Font).Width);
                        using (Pen strike = new Pen(Neon.Faint, Math.Max(1f, zoom))) g.DrawLine(strike, words.X, box.Y + box.Height / 2, words.X + wide, box.Y + box.Height / 2);
                    }
                }
                if (dropY >= 0)
                    using (Brush mark = new SolidBrush(Neon.Cyan))
                        g.FillRectangle(mark, 0, dropY - Math.Max(1f, zoom), Width, 2 * Math.Max(1f, zoom));
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
