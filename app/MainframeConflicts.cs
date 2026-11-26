using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Conflicts: her collections, their mods' order, and who wins each part they share.
    internal sealed partial class Mainframe
    {
        private ConflictsSheet conflictsSheet;

        internal void OpenConflicts()
        {
            if (conflictsSheet == null || conflictsSheet.IsDisposed) conflictsSheet = new ConflictsSheet(this, SheetZoom(ConflictsSheet.Wide, ConflictsSheet.Tall));
            if (!conflictsSheet.Visible) conflictsSheet.Show(this);
            conflictsSheet.Activate();
        }

        internal void CloseConflicts() { if (conflictsSheet != null && !conflictsSheet.IsDisposed) conflictsSheet.Close(); }

        // One window, two parts: the other part opens where this one stood, and this one is put away.
        internal void ShowModPart(bool conflicts, Form from)
        {
            Point at = from != null && !from.IsDisposed && from.Visible ? from.Location : Point.Empty;
            if (conflicts) OpenConflicts(); else OpenModHelper();
            Form shown = conflicts ? (Form)conflictsSheet : modHelper;
            if (shown != null && !shown.IsDisposed && at != Point.Empty) shown.Location = at;
            if (from != null && !from.IsDisposed && from != shown) from.Hide();
        }

#if CHECKS
        internal string ModPartShown
        {
            get
            {
                bool maker = modHelper != null && !modHelper.IsDisposed && modHelper.Visible, clashes = conflictsSheet != null && !conflictsSheet.IsDisposed && conflictsSheet.Visible;
                return (maker ? "Mod maker" : "") + (maker && clashes ? " + " : "") + (clashes ? "Conflicts" : "");
            }
        }

        internal void PressModPart(bool conflicts)
        {
            Form from = conflicts ? (Form)modHelper : conflictsSheet;
            if (from == null || from.IsDisposed) return;
            foreach (Control one in from.Controls)
                if (one is Key && one.Text == (conflicts ? "Conflicts" : "Mod maker") && one.Top < from.ClientSize.Height / 8) { ((Key)one).PerformClick(); return; }
        }

        internal Point ModPartAt(bool conflicts) { Form one = conflicts ? (Form)conflictsSheet : modHelper; return one == null || one.IsDisposed ? Point.Empty : one.Location; }
#endif

        // Laid again after a change elsewhere (a mod turned on or off, a part edited and saved).
        internal void ConflictsChanged() { if (conflictsSheet != null && !conflictsSheet.IsDisposed) conflictsSheet.Fill(); }

#if CHECKS
        private ConflictsSheet Conflicts { get { OpenConflicts(); return conflictsSheet; } }
        internal string ConflictsCollections { get { return Conflicts.CollectionsShown; } }
        internal string ConflictsOrder { get { return Conflicts.OrderShown; } }
        internal string ConflictsClashes(string mod) { Conflicts.PickMod(mod); return Conflicts.ClashesShown; }
        internal void ConflictsDrag(string what, string onto, bool below) { Conflicts.DragOrder(what, onto, below); }
        internal void ConflictsPickCollection(string name) { Conflicts.PickCollection(name); }
        internal void ConflictsUse() { Conflicts.Use(); }
        internal void ConflictsCopy() { Conflicts.Copy(); }
        internal string ConflictsSave() { Conflicts.SaveNow(); return Conflicts.SavedNote; }
        internal string ConflictsCrowdedNow { get { return Conflicts.Crowded(); } }

        internal void ConflictsPicture(string path)
        {
            ConflictsSheet sheet = Conflicts;
            using (Bitmap picture = new Bitmap(sheet.Width, sheet.Height))
            {
                sheet.DrawToBitmap(picture, new Rectangle(Point.Empty, picture.Size));
                picture.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
#endif

        private sealed class ConflictsSheet : ModSheet
        {
            internal const int Wide = 760, Tall = 626, LeftWide = 250, RightLeft = 300, RightWide = 432;
            private readonly CollectionList collections;
            private readonly OrderList order;
            private readonly Label orderHeading, inUseName, clashHeading, clashFor, notice;
            private readonly ScrollPanel clashBox;
            private readonly SlimBar clashBar = new SlimBar();
            private readonly Key copyKey, useKey;
            private readonly ContextMenuStrip pickMenu;
            private readonly Clock.Ticker noticeTimer = new Clock.Ticker("conflicts notice");
            private string pickedMod;

            internal ConflictsSheet(Mainframe owner, float zoom) : base(owner, zoom, "Mod maker", "MOD MAKER >", Wide, Tall)
            {
                Pair(true);
                Tucks(null);
                Words("COLLECTIONS", 32, 64, LeftWide, 20, Type(10f * Zoom, true), Hot);
                collections = new CollectionList(Zoom) { Font = Font, Location = new Point(Z(28), Z(84)) };
                collections.Width = Z(LeftWide);
                collections.Picked += delegate { Place(); };
                collections.Renaming += Rename;
                collections.Removing += delegate(string name) { Frame.capri.RemoveCollection(name); Fill(); };
                Controls.Add(collections);
                copyKey = Make("Copy", 28, 300, 80, 26, delegate { string made = Frame.capri.CopyCollection(collections.PickedName); collections.PickedName = made; Fill(); });
                copyKey.AccessibleName = "Copy the picked collection";
                useKey = Make("Use", 118, 300, 80, 26, delegate { Frame.capri.UseCollection(collections.PickedName); Fill(); });
                useKey.AccessibleName = "Use the picked collection";

                orderHeading = Words("ORDER", RightLeft + 4, 64, 180, 20, Type(10f * Zoom, true), Hot);
                inUseName = Words("", RightLeft + 200, 64, RightWide - 200, 20, Small(), Cyan);
                inUseName.TextAlign = ContentAlignment.MiddleRight;
                order = new OrderList(Zoom) { Font = Font, Location = new Point(Z(RightLeft), Z(84)) };
                order.Width = Z(RightWide);
                order.Picked += delegate(string id) { pickedMod = id; FillClashes(); };
                order.Moved += delegate(List<string> ids) { Frame.capri.SetCollectionOrder(Frame.capri.CollectionInUse, ids); Fill(); };
                Controls.Add(order);
                clashHeading = Words("CLASHES", RightLeft + 4, 260, 180, 20, Type(10f * Zoom, true), Hot);
                clashFor = Words("", RightLeft + 200, 260, RightWide - 200, 20, Small(), Cyan);
                clashFor.TextAlign = ContentAlignment.MiddleRight;
                clashBox = new ScrollPanel { Bounds = Box(RightLeft, 284, RightWide, 270), BackColor = Ground };
                clashBox.Scrolled += delegate { clashBar.Invalidate(); };
                Controls.Add(clashBox);
                clashBar.BackColor = Ground;
                clashBar.Watch(clashBox);
                Controls.Add(clashBar);
                pickMenu = owner.Dropdown();
                pickMenu.AccessibleName = "Which mod wins this part";

                Key save = Make("Save collection", 28, 576, 160, 28, delegate { Save(); });
                save.Accent = Cyan;
                save.AccessibleName = "Save the collection in use as one combined file";
                notice = Words("", 196, 582, 360, 18, Small(), Cyan);
                noticeTimer.Interval = 6000;
                noticeTimer.Tick += delegate { noticeTimer.Stop(); notice.Text = ""; };
                Disposed += delegate { noticeTimer.Dispose(); pickMenu.Dispose(); };
                Fill();
                ResumeLayout(false);
            }

            private Companion Capri { get { return Frame.capri; } }

            internal void Fill()
            {
                string use = Capri.CollectionInUse;
                List<KeyValuePair<string, List<string>>> rows = new List<KeyValuePair<string, List<string>>>();
                foreach (string name in Capri.CollectionNames)
                    rows.Add(new KeyValuePair<string, List<string>>(name, Layers(name)));
                collections.Fill(rows, use);
                inUseName.Text = use;
                List<FileSlots.Clash> all = FileSlots.Clashes();
                List<KeyValuePair<string, string>> layers = Capri.CollectionLayers(use);
                List<OrderList.Row> made = new List<OrderList.Row>();
                foreach (KeyValuePair<string, string> layer in layers)
                {
                    KeyValuePair<string, string> one = layer;
                    made.Add(new OrderList.Row { Id = one.Key, Name = one.Value, Clashes = all.FindAll(clash => clash.Mods.Exists(mod => mod.Key == one.Key)).Count });
                }
                if (pickedMod == null || !made.Exists(row => row.Id == pickedMod))
                {
                    OrderList.Row first = made.Find(row => row.Clashes > 0);
                    pickedMod = first != null ? first.Id : made.Count > 0 ? made[0].Id : null;
                }
                order.Fill(made, pickedMod);
                Place();
                FillClashes();
            }

            // Core, Your Own, then the mods a collection lays, for the faint list under the one picked.
            private List<string> Layers(string name)
            {
                List<string> names = new List<string> { "Core" };
                if (name == Companion.StockCollection) return names;
                names.Add("Your Own");
                foreach (KeyValuePair<string, string> layer in Capri.CollectionLayers(name)) names.Add(layer.Value);
                return names;
            }

            // The keys under the collections, and CLASHES under the order, follow the lists' heights.
            private void Place()
            {
                copyKey.Top = collections.Bottom + Z(10);
                useKey.Top = copyKey.Top;
                bool stock = collections.PickedName == Companion.StockCollection;
                useKey.Enabled = collections.PickedName != Capri.CollectionInUse;
                copyKey.Enabled = true;
                clashHeading.Top = order.Bottom + Z(16);
                clashFor.Top = clashHeading.Top;
                clashBox.Top = clashHeading.Bottom + Z(6);
                clashBox.Height = Math.Max(Z(60), Z(560) - clashBox.Top);
                clashBar.Bounds = new Rectangle(clashBox.Right + Z(4), clashBox.Top, Z(SlimBar.Thin), clashBox.Height);
                if (stock) { }
            }

            private void FillClashes()
            {
                clashBox.SuspendLayout();
                foreach (Control old in new List<Control>(Children(clashBox))) { clashBox.Controls.Remove(old); old.Dispose(); }
                clashBox.AutoScrollPosition = Point.Empty;
                string use = Capri.CollectionInUse;
                List<FileSlots.Clash> mine = pickedMod == null ? new List<FileSlots.Clash>() : FileSlots.Clashes().FindAll(clash => clash.Mods.Exists(mod => mod.Key == pickedMod));
                KeyValuePair<string, string> named = Capri.CollectionLayers(use).Find(layer => layer.Key == pickedMod);
                clashFor.Text = named.Key == null ? "" : named.Value;
                int y = 0;
                foreach (FileSlots.Clash clash in mine)
                {
                    FileSlots.Clash one = clash;
                    Control row = new ClashRow { Bounds = new Rectangle(0, Z(y), Z(RightWide - 8), Z(46)), BackColor = Field };
                    Label slot = new Note { Text = one.Slot, Font = Font, ForeColor = White, BackColor = Field, AutoSize = false, AutoEllipsis = true, UseMnemonic = false, Bounds = new Rectangle(Z(10), Z(4), Z(RightWide - 200), Z(20)) };
                    Label count = new Note { Text = one.Mods.Count + " mods", Font = Small(), ForeColor = Faint, BackColor = Field, AutoSize = false, UseMnemonic = false, Bounds = new Rectangle(Z(10), Z(24), Z(120), Z(18)) };
                    Key pick = new Key { Text = WinnerName(one), Font = Type(9f * Zoom, true), Chevron = true, Bounds = new Rectangle(Z(RightWide - 190), Z(10), Z(170), Z(26)) };
                    pick.AccessibleName = one.Slot + ": " + WinnerName(one);
                    pick.Click += delegate { ShowPicks(pick, one); };
                    row.Controls.Add(slot);
                    row.Controls.Add(count);
                    row.Controls.Add(pick);
                    clashBox.Controls.Add(row);
                    y += 52;
                }
                clashBox.ResumeLayout();
                clashBar.Invalidate();
            }

            private static string WinnerName(FileSlots.Clash clash)
            {
                if (clash.Pick == FileSlots.MergePick) return "Merge";
                if (clash.Pick == FileSlots.EditPick) return "Edited";
                KeyValuePair<string, string> won = clash.Mods.Find(mod => mod.Key == clash.Winner);
                return won.Key == null ? "" : won.Value;
            }

            // Every mod that touches the part, the winner ticked; Merge where both sides join; Edit in MD editor always.
            private void ShowPicks(Key under, FileSlots.Clash clash)
            {
                foreach (ToolStripItem old in new List<ToolStripItem>(Items(pickMenu))) { pickMenu.Items.Remove(old); old.Dispose(); }
                string use = Capri.CollectionInUse;
                foreach (KeyValuePair<string, string> mod in clash.Mods)
                {
                    string id = mod.Key;
                    pickMenu.Items.Add(Choice(mod.Value, clash.Pick != FileSlots.MergePick && clash.Pick != FileSlots.EditPick && clash.Winner == id, delegate { Pick(use, clash.Slot, id); }));
                }
                pickMenu.Items.Add(new ToolStripSeparator());
                if (clash.Joins) pickMenu.Items.Add(Choice("Merge", clash.Pick == FileSlots.MergePick, delegate { Pick(use, clash.Slot, FileSlots.MergePick); }));
                pickMenu.Items.Add(Choice("Edit in MD editor", clash.Pick == FileSlots.EditPick, delegate { EditPart(use, clash.Slot); }));
                pickMenu.Show(under, new Point(0, under.Height));
            }

            internal void Pick(string collection, string part, string pick)
            {
                if (collection == Companion.StockCollection) return;
                Capri.SetPartPick(collection, part, pick);
                Fill();
            }

            internal void EditPart(string collection, string part)
            {
                if (collection == Companion.StockCollection) return;
                string file = Capri.EditPart(collection, part);
                Capri.OpenPart(file);
                Fill();
            }

            private void Save()
            {
                string file = null;
                try { file = Capri.SaveCollection(); }
                catch (System.IO.IOException error) { Says("Couldn't save it: " + error.Message); return; }
                catch (UnauthorizedAccessException error) { Says("Couldn't save it: " + error.Message); return; }
                Says(file == null ? "Stock is her core: nothing to save." : "Saved as " + System.IO.Path.GetFileName(file) + ".");
            }

            private void Says(string said)
            {
                noticeTimer.Stop();
                notice.Text = said;
                noticeTimer.Start();
            }

            internal string SavedNote { get { return notice.Text; } }

            // The pen: the collection's name becomes a box to type in; Enter keeps it, Esc puts it back.
            private void Rename(string name, Rectangle row)
            {
                TextBox box = new TextBox { Text = name, Font = Font, BorderStyle = BorderStyle.None, BackColor = Field, ForeColor = Cyan, Bounds = new Rectangle(collections.Left + row.Left + Z(12), collections.Top + row.Top + Z(4), Z(170), row.Height - Z(6)), MaxLength = 40 };
                box.AccessibleName = "Collection name";
                bool done = false;
                Action keep = delegate
                {
                    if (done) return;
                    done = true;
                    string named = box.Text.Trim();
                    Controls.Remove(box);
                    BeginInvoke((Action)delegate { box.Dispose(); });
                    if (named.Length > 0 && named != name && Capri.RenameCollection(name, named)) collections.PickedName = named;
                    Fill();
                };
                box.KeyDown += delegate(object sender, KeyEventArgs e)
                {
                    if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; keep(); }
                    else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; box.Text = name; keep(); }
                };
                box.Leave += delegate { keep(); };
                Controls.Add(box);
                box.BringToFront();
                box.Focus();
                box.SelectAll();
            }

            private static List<Control> Children(Control within)
            {
                List<Control> all = new List<Control>();
                foreach (Control one in within.Controls) all.Add(one);
                return all;
            }

            private static List<ToolStripItem> Items(ToolStrip strip)
            {
                List<ToolStripItem> all = new List<ToolStripItem>();
                foreach (ToolStripItem one in strip.Items) all.Add(one);
                return all;
            }

            // For the checks.
            internal string CollectionsShown { get { return collections.Shown; } }
            internal string OrderShown { get { return order.Shown; } }
            internal string ClashesShown
            {
                get
                {
                    List<string> said = new List<string>();
                    foreach (Control row in clashBox.Controls)
                        foreach (Control inner in row.Controls) if (inner is Key) said.Add(inner.AccessibleName);
                    return string.Join(" | ", said.ToArray());
                }
            }
            internal void PickCollection(string name) { collections.PickedName = name; Place(); }
            internal void PickMod(string id) { pickedMod = id; order.Fill(null, id); FillClashes(); }
            internal void DragOrder(string what, string onto, bool below) { order.DropBy(what, onto, below); }
            internal void Use() { useKey.PerformClick(); }
            internal void Copy() { copyKey.PerformClick(); }
            internal void SaveNow() { Save(); }
            internal void RenameNow(string name, string to) { if (Capri.RenameCollection(name, to)) collections.PickedName = to; Fill(); }
        }

        private sealed class ClashRow : System.Windows.Forms.Panel
        {
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                using (Pen rim = new Pen(Edge, 1f)) e.Graphics.DrawRectangle(rim, 0, 0, Width - 1, Height - 1);
            }
        }

        // Her collections, one framed row each as Toggles' sections are drawn: the one in use with a cyan bar and its name in
        // cyan, the picked one with its layers listed faint under it; a pen and an X on every one but Stock.
        private sealed class CollectionList : Control
        {
            private sealed class Line { internal string Name; internal List<string> Layers; internal Rectangle Bounds; }
            private readonly float zoom;
            private readonly List<Line> lines = new List<Line>();
            private string inUse;
            private string picked;
            internal event Action Picked;
            internal event Action<string, Rectangle> Renaming;
            internal event Action<string> Removing;
            private const int RowTall = 24, Gap = 6, LayerTall = 18;

            internal CollectionList(float zoomed)
            {
                zoom = zoomed;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true);
                TabStop = true;
                BackColor = Neon.Ground;
                AccessibleName = "Collections";
            }

            private int Z(int value) { return (int)Math.Round(value * zoom); }

            internal string PickedName
            {
                get { return picked ?? inUse; }
                set { picked = value; Lay(); if (Picked != null) Picked(); }
            }

            private List<KeyValuePair<string, List<string>>> rows = new List<KeyValuePair<string, List<string>>>();

            internal void Fill(List<KeyValuePair<string, List<string>>> made, string use)
            {
                rows = made;
                inUse = use;
                if (picked == null || !made.Exists(row => row.Key == picked)) picked = use;
                Lay();
            }

            private void Lay()
            {
                lines.Clear();
                int y = 0;
                foreach (KeyValuePair<string, List<string>> row in rows)
                {
                    Line line = new Line { Name = row.Key, Layers = row.Value, Bounds = new Rectangle(Z(6), y, Width - Z(6), Z(RowTall)) };
                    lines.Add(line);
                    y += Z(RowTall) + Z(Gap);
                    if (row.Key == PickedName) y += row.Value.Count * Z(LayerTall) + Z(Gap);
                }
                Height = Math.Max(Z(RowTall), y);
                Invalidate();
            }

            private static bool Fixed(Line line) { return line.Name == Companion.StockCollection; }

            private Rectangle Mark(Line line, int fromRight) { return new Rectangle(line.Bounds.Right - Z(fromRight) - Z(10), line.Bounds.Top + (line.Bounds.Height - Z(10)) / 2, Z(10), Z(10)); }

            private static Rectangle Near(Rectangle mark, int by) { mark.Inflate(by, by); return mark; }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                Line line = lines.Find(one => one.Bounds.Contains(e.Location));
                if (line == null || e.Button != MouseButtons.Left) return;
                Focus();
                if (!Fixed(line) && Near(Mark(line, 30), Z(4)).Contains(e.Location)) { if (Renaming != null) Renaming(line.Name, line.Bounds); return; }
                if (!Fixed(line) && Near(Mark(line, 10), Z(4)).Contains(e.Location)) { if (Removing != null) Removing(line.Name); return; }
                PickedName = line.Name;
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                Line over = lines.Find(one => one.Bounds.Contains(e.Location));
                Cursor = over != null ? Cursors.Hand : Cursors.Default;
            }

            internal string Shown
            {
                get
                {
                    List<string> said = new List<string>();
                    foreach (Line line in lines) said.Add((line.Name == inUse ? "*" : "") + line.Name + (line.Name == PickedName ? " (" + string.Join(", ", line.Layers.ToArray()) + ")" : ""));
                    return string.Join(" | ", said.ToArray());
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Neon.Ground);
                float thin = Math.Max(1f, zoom);
                foreach (Line one in lines)
                {
                    Rectangle box = one.Bounds;
                    using (GraphicsPath edge = Neon.Cut(new RectangleF(box.X + thin / 2, box.Y + thin / 2, box.Width - thin, box.Height - thin), 0, 0, Z(6), 0))
                    using (Brush fill = new SolidBrush(Neon.Field))
                    using (Pen rim = new Pen(Neon.Edge, thin))
                    {
                        g.FillPath(fill, edge);
                        g.DrawPath(rim, edge);
                    }
                    bool used = one.Name == inUse;
                    if (used) using (Brush bar = new SolidBrush(Neon.Cyan)) g.FillRectangle(bar, 0, box.Top + Z(3), Z(3), box.Height - Z(6));
                    TextRenderer.DrawText(g, one.Name, Font, new Rectangle(box.X + Z(8), box.Y, box.Width - Z(52), box.Height), used ? Neon.Cyan : Neon.Rose, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    if (!Fixed(one))
                        using (Pen ink = new Pen(Neon.Hot, 1.5f * zoom))
                        {
                            Rectangle pen = Mark(one, 30), cross = Mark(one, 10);
                            g.DrawLine(ink, cross.Left, cross.Top, cross.Right, cross.Bottom);
                            g.DrawLine(ink, cross.Left, cross.Bottom, cross.Right, cross.Top);
                            float w = pen.Width / 10f;
                            g.DrawPolygon(ink, new[]
                            {
                                new PointF(pen.Left, pen.Bottom), new PointF(pen.Left + 1.2f * w, pen.Bottom - 3.6f * w), new PointF(pen.Left + 7.6f * w, pen.Top + 0.4f * w),
                                new PointF(pen.Right, pen.Top + 2.8f * w), new PointF(pen.Left + 3.6f * w, pen.Bottom - 1.2f * w)
                            });
                        }
                    if (one.Name == PickedName)
                    {
                        int y = box.Bottom + Z(Gap);
                        using (Font small = Look.Mono(8f * zoom, FontStyle.Regular, GraphicsUnit.Point))
                            foreach (string layer in one.Layers)
                            {
                                TextRenderer.DrawText(g, layer, small, new Rectangle(box.X + Z(24), y, box.Width - Z(24), Z(LayerTall)), Neon.Faint, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                                y += Z(LayerTall);
                            }
                    }
                }
                if (Focused)
                {
                    Line held = lines.Find(one => one.Name == PickedName);
                    if (held != null) using (Pen mark = new Pen(Neon.Cyan, thin)) g.DrawRectangle(mark, held.Bounds.X, held.Bounds.Y, held.Bounds.Width - thin, held.Bounds.Height - thin);
                }
            }

            protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
            protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        }

        // The order a collection lays its mods in: Core and Your Own first and fixed, then each mod with its grip, its place
        // and how many parts it shares, dragged to a new place; the picked one in cyan with a bar beside it.
        private sealed class OrderList : Control
        {
            internal sealed class Row { internal string Id, Name; internal int Clashes; }
            private sealed class Line { internal Row Row; internal int Place; internal Rectangle Bounds; }
            private readonly float zoom;
            private readonly List<Line> lines = new List<Line>();
            private List<Row> rows = new List<Row>();
            private string picked;
            internal event Action<string> Picked;
            internal event Action<List<string>> Moved;
            private Line dragging;
            private Point pressedAt;
            private bool moved;
            private int dropY = -1;
            private Line dropOnto;
            private bool dropBelow;
            private const int RowTall = 26, Gap = 6;

            internal OrderList(float zoomed)
            {
                zoom = zoomed;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true);
                TabStop = true;
                BackColor = Neon.Ground;
                AccessibleName = "Load order: drag a mod to change where it is laid";
            }

            private int Z(int value) { return (int)Math.Round(value * zoom); }

            internal void Fill(List<Row> made, string pick)
            {
                if (made != null) rows = made;
                picked = pick;
                lines.Clear();
                int y = 0, place = 1;
                foreach (string fixedName in new[] { "Core", "Your Own" })
                {
                    lines.Add(new Line { Row = new Row { Name = fixedName }, Place = place++, Bounds = new Rectangle(Z(6), y, Width - Z(6), Z(RowTall)) });
                    y += Z(RowTall) + Z(Gap);
                }
                foreach (Row row in rows)
                {
                    lines.Add(new Line { Row = row, Place = place++, Bounds = new Rectangle(Z(6), y, Width - Z(6), Z(RowTall)) });
                    y += Z(RowTall) + Z(Gap);
                }
                Height = Math.Max(Z(RowTall), y);
                Invalidate();
            }

            private static bool Fixed(Line line) { return line.Row.Id == null; }

            private Rectangle Grip(Line line) { return new Rectangle(line.Bounds.Left, line.Bounds.Top, Z(18), line.Bounds.Height); }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                Line line = lines.Find(one => one.Bounds.Contains(e.Location));
                if (line == null || e.Button != MouseButtons.Left || Fixed(line)) return;
                Focus();
                picked = line.Row.Id;
                if (Picked != null) Picked(picked);
                dragging = line;
                pressedAt = e.Location;
                moved = false;
                Capture = true;
                Invalidate();
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                Line over = lines.Find(one => one.Bounds.Contains(e.Location));
                Cursor = dragging != null ? Cursors.SizeAll : over != null && !Fixed(over) ? Cursors.Hand : Cursors.Default;
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

            private void Aim(int y)
            {
                dropOnto = null;
                foreach (Line line in lines)
                {
                    if (Fixed(line)) continue;
                    if (y < line.Bounds.Top - Z(Gap) || y >= line.Bounds.Bottom + Z(Gap)) continue;
                    dropOnto = line;
                    dropBelow = y >= line.Bounds.Top + line.Bounds.Height / 2;
                }
                dropY = dropOnto == null ? -1 : dropBelow ? dropOnto.Bounds.Bottom + Z(Gap) / 2 : dropOnto.Bounds.Top - Z(Gap) / 2;
            }

            private void Drop(Line what, Line onto, bool below)
            {
                if (what == onto) return;
                List<Row> now = new List<Row>(rows);
                now.Remove(what.Row);
                int at = now.IndexOf(onto.Row) + (below ? 1 : 0);
                now.Insert(Math.Max(0, Math.Min(now.Count, at)), what.Row);
                rows = now;
                if (Moved != null) Moved(now.ConvertAll(row => row.Id));
            }

            // For the checks: a drag of the row named onto another, without the mouse.
            internal void DropBy(string what, string onto, bool below)
            {
                Line from = lines.Find(one => !Fixed(one) && (one.Row.Id == what || one.Row.Name == what)), to = lines.Find(one => one != from && !Fixed(one) && (one.Row.Id == onto || one.Row.Name == onto));
                if (from != null && to != null) Drop(from, to, below);
            }

            internal string Shown
            {
                get
                {
                    List<string> said = new List<string>();
                    foreach (Line line in lines) said.Add(line.Place + " " + line.Row.Name + (line.Row.Clashes > 0 ? " (" + line.Row.Clashes + ")" : ""));
                    return string.Join(" | ", said.ToArray());
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Neon.Ground);
                float thin = Math.Max(1f, zoom);
                using (Font mono = Look.Mono(8.5f * zoom, FontStyle.Regular, GraphicsUnit.Point))
                    foreach (Line one in lines)
                    {
                        Rectangle box = one.Bounds;
                        using (GraphicsPath edge = Neon.Cut(new RectangleF(box.X + thin / 2, box.Y + thin / 2, box.Width - thin, box.Height - thin), 0, 0, Z(6), 0))
                        using (Brush fill = new SolidBrush(Neon.Field))
                        using (Pen rim = new Pen(Neon.Edge, thin))
                        {
                            g.FillPath(fill, edge);
                            g.DrawPath(rim, edge);
                        }
                        bool fixedRow = Fixed(one), lit = !fixedRow && one.Row.Id == picked;
                        if (lit) using (Brush bar = new SolidBrush(Neon.Cyan)) g.FillRectangle(bar, 0, box.Top + Z(3), Z(3), box.Height - Z(6));
                        if (!fixedRow) Dots(g, Grip(one));
                        int at = box.X + (fixedRow ? Z(8) : Z(20));
                        TextRenderer.DrawText(g, one.Place.ToString(), mono, new Rectangle(at, box.Y, Z(18), box.Height), Neon.Faint, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                        TextRenderer.DrawText(g, one.Row.Name, Font, new Rectangle(at + Z(20), box.Y, box.Width - Z(80), box.Height), fixedRow ? Neon.Faint : lit ? Neon.Cyan : Neon.Rose, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                        if (one.Row.Clashes > 0) TextRenderer.DrawText(g, one.Row.Clashes.ToString(), mono, new Rectangle(box.Right - Z(40), box.Y, Z(30), box.Height), Neon.Rose, TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPrefix);
                    }
                if (dropY >= 0) using (Brush mark = new SolidBrush(Neon.Cyan)) g.FillRectangle(mark, 0, dropY - thin, Width, 2 * thin);
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
