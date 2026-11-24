using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace CapriDesktop
{
    // FOCUS / QUIET SETTINGS, from its key in CAPRI: While working, the Quiet timer's hours, quiet on calls and in videos and the
    // call windows, then the apps she has seen, the Added apps and what While working slows.
    internal sealed partial class Mainframe
    {
        private WorkSheet workSheet;

        internal void OpenWorkApps()
        {
            if (workSheet != null && !workSheet.IsDisposed && workSheet.Zoom != zoom) workSheet.Close();
            WorkSheetMade();
            if (!workSheet.Visible) workSheet.Show(this);
            workSheet.Activate();
        }

        // The window, made but not shown, when its controls are wanted before it opens.
        private void WorkSheetMade()
        {
            if (workSheet == null || workSheet.IsDisposed) workSheet = new WorkSheet(this, zoom);
            SyncQuiet();
        }

        internal void CloseWorkApps() { if (workSheet != null && !workSheet.IsDisposed) workSheet.Close(); }

        // A mod made its first timer, or one came or went: an open window shows it.
        internal void RefreshWorkApps() { if (workSheet != null && !workSheet.IsDisposed) workSheet.FillAll(); }

        // The rows What it slows shows in the open window, for the checks.
        internal List<string> WorkRowsShown { get { return workSheet == null || workSheet.IsDisposed ? new List<string>() : workSheet.Rows(); } }

        // The window driven as they'd use it at one size, their work apps and holds put back after. Null when it all went
        // right, else what didn't, with the size.
        internal string WorkAppsAt(float zoomAt, string picture)
        {
            string appsWas = capri.WorkAppsSaid, holdsWas = Working.HoldsSaid;
            CloseWorkApps();
            try
            {
                using (WorkSheet drawn = new WorkSheet(this, zoomAt))
                {
                    workSheet = drawn;
                    drawn.Show(this);
                    try
                    {
                        string problem = drawn.Drive(picture);
                        return problem == null ? null : problem + " (" + drawn.ClientSize.Width + " x " + drawn.ClientSize.Height + " at " + zoomAt + ")";
                    }
                    finally { drawn.Close(); workSheet = null; }
                }
            }
            finally
            {
                capri.SetWorkApps(appsWas == null ? null : appsWas.Split('\n'));
                Working.HoldsSaid = holdsWas;
                capri.SaveWorkHolds();
            }
        }

        private sealed class WorkSheet : ModSheet
        {
            internal const int Wide = 880, Tall = 744;
            private const int ListTop = 314, ListTall = 342, SeenWide = 246, AddedWide = 214, SlowsWide = 258, AddedAt = 344, SlowsAt = 588;
            private readonly Entry search = new Entry();
            private readonly ScrollPanel seen, added, slows;
            private readonly SlimBar seenBar = new SlimBar(), addedBar = new SlimBar(), slowsBar = new SlimBar();
            private readonly Key addKey, offKey, allOffKey, browseKey;
            private readonly Label notice;
            private readonly ContextMenuStrip holdMenu;
            private readonly System.Windows.Forms.Timer noticeTimer = new System.Windows.Forms.Timer();
            private readonly HashSet<string> open = new HashSet<string>(StringComparer.Ordinal);
            private string pickedSeen, pickedAdded;

            internal WorkSheet(Mainframe owner, float zoom) : base(owner, zoom, FocusLabel, FocusLabel.ToUpperInvariant() + " >", Wide, Tall)
            {
                BuildSettings();
                Words("Focus slows her while an Added app is in front. At \u221E it's on whatever is in front.", 28, 238, 824, 18, Small(), Faint);
                // The one search sits over the first list, and the three lists' titles line up under it.
                search.Bounds = Box(28, 260, 252, 26);
                Words("Apps she has seen", 28, 292, 252, 18, Type(10f * Zoom, true), Rose);
                Words("Added", AddedAt, 292, 220, 18, Type(10f * Zoom, true), Rose);
                Words("What it slows", SlowsAt, 292, 264, 18, Type(10f * Zoom, true), Rose);
                search.Box.Font = Type(9.5f * Zoom);
                search.Cue = "Search";
                search.Box.AccessibleName = "Search the apps she has seen and the Added apps";
                // One search filters both lists.
                search.Box.TextChanged += delegate { FillSeen(); FillAdded(); };
                search.Box.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.Handled = e.SuppressKeyPress = true; AddFirstFound(); } };
                Controls.Add(search);
                seen = List(28, ListTop, SeenWide, ListTall, seenBar);
                added = List(AddedAt, ListTop, AddedWide, ListTall, addedBar);
                slows = List(SlowsAt, ListTop, SlowsWide, ListTall, slowsBar);
                addKey = Make(">", 292, 414, 40, 28, delegate { AddPicked(); });
                addKey.AccessibleName = "Add the picked app";
                offKey = Make("<", 292, 450, 40, 28, delegate { TakePicked(); });
                offKey.AccessibleName = "Take the picked app off";
                allOffKey = Make("<<", 292, 486, 40, 28, delegate { TakeAll(); });
                allOffKey.AccessibleName = "Take every app off";
                browseKey = Make("Browse for an .exe", 28, 664, 252, 26, delegate { Browse(); });
                browseKey.AccessibleName = "Browse for a program to add, kept by its name";
                notice = Words("", 28, 700, 824, 36, Small(), Cyan);
                holdMenu = owner.Dropdown();
                holdMenu.AccessibleName = "What While working does";
                noticeTimer.Interval = 8000;
                noticeTimer.Tick += delegate { Says("", false); };
                FormClosed += delegate { noticeTimer.Stop(); };
                Disposed += delegate { noticeTimer.Dispose(); holdMenu.Dispose(); };
                FillAll();
                // Its switches, sliders and boxes show hers from the start.
                Frame.SyncQuiet();
                ResumeLayout(false);
            }

            // Two sections across the top, each with its own switch, working alone or together: Focus on the left with
            // While working, Quiet on the right with its hours, calls, videos and call windows. Saved as before.
            private void BuildSettings()
            {
                Mainframe main = Frame;
                main.focusBox = Switch("Focus mode", 28, 50, delegate { if (!main.syncing) main.capri.SetFocus(main.focusBox.Checked); });
                main.focusBox.AccessibleName = "Focus mode, this section's switch. While an app you added is in front, her timers and chatter slow by While working";
                main.quietBox = Switch("Quiet timer", 470, 50, delegate { if (!main.syncing) main.capri.SetQuiet(main.quietBox.Checked); });
                main.quietBox.AccessibleName = "Quiet timer, this section's switch. She stops talking on her own for a while and only answers when you talk to her";
                main.whileWorking = Setting("While working", WhileWorkingNames, 28, 80, "While working: her timers and random talk stretch out while a work app or site is in front, so you're interrupted less. A multiplier on her usual timings");
                main.whileWorkingName = new Readout(main.whileWorking);
                main.whileWorking.Changed += delegate { if (!main.syncing) main.whileWorkingName.Text = WhileWorkingNames[main.whileWorking.Value]; };
                main.whileWorking.Released += delegate { if (!main.syncing) main.capri.SetWhileWorking(main.whileWorking.Value + Working.Least); };
                main.quietMin = Setting("Quiet min", QuietMinNames, 470, 80, "Quiet min: the shortest the Quiet timer lasts, from 1 to 8 hours");
                main.quietMinName = new Readout(main.quietMin);
                main.quietMax = Setting("Quiet max", QuietMaxNames, 470, 110, "Quiet max: the longest the Quiet timer lasts, at least an hour over Quiet min, or until you turn it off");
                main.quietMaxName = new Readout(main.quietMax);
                // Max never sits under min + 1: raising min pushes max up, and max stops at min + 1 on the way down.
                main.quietMin.Changed += delegate
                {
                    if (main.syncing) return;
                    main.quietMinName.Text = QuietMinNames[main.quietMin.Value];
                    if (main.quietMax.Value < main.quietMin.Value) { main.quietMax.Value = main.quietMin.Value; main.quietMaxName.Text = QuietMaxNames[main.quietMax.Value]; }
                };
                main.quietMax.Changed += delegate
                {
                    if (main.syncing) return;
                    if (main.quietMax.Value < main.quietMin.Value) main.quietMax.Value = main.quietMin.Value;
                    main.quietMaxName.Text = QuietMaxNames[main.quietMax.Value];
                };
                foreach (Slider hours in new[] { main.quietMin, main.quietMax })
                    hours.Released += delegate { if (!main.syncing) main.capri.SetQuietHours(main.quietMin.Value + 1, MaxHoursAt(main.quietMax.Value)); };
                main.quietCalls = Switch("Quiet on calls", 470, 140, delegate { if (!main.syncing) main.capri.SetQuietCalls(main.quietCalls.Checked); });
                main.quietCalls.AccessibleName = "Quiet on calls. She goes quiet on her own during a call in a window named under Call windows";
                main.quietVideos = Switch("Quiet in videos", 470, 170, delegate { if (!main.syncing) main.capri.SetQuietVideos(main.quietVideos.Checked); });
                main.quietVideos.AccessibleName = "Quiet in videos. She goes quiet on her own while a video fills the screen";
                Named("Call windows", 470, 200);
                main.callApps = new Entry();
                main.callApps.Bounds = Box(598, 203, 180, 22);
                main.callApps.Box.Font = Type(9f * Zoom);
                main.callApps.Cue = "Window titles, comma separated";
                main.callApps.Box.AccessibleName = "Call windows: titles a window has only during a call, comma separated";
                main.callApps.Box.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; main.UseCallApps(); } };
                Controls.Add(main.callApps);
                Make("Save", 784, 203, 68, 22, delegate { main.UseCallApps(); }).AccessibleName = "Save the call windows";
            }

            private Label Named(string name, int x, int y) { return Words(name, x, y + 6, 124, 20, Type(9.5f * Zoom), Rose); }

            private Slider Setting(string name, string[] steps, int x, int y, string spoken)
            {
                Words(name, x, y + 6, 150, 20, Type(9.5f * Zoom), Rose);
                Slider slider = new Slider(steps);
                slider.Font = Type(9.5f * Zoom);
                slider.Bounds = Box(x + 156, y + 3, 172, 22);
                slider.AccessibleName = spoken;
                Controls.Add(slider);
                return slider;
            }

            private Toggle Switch(string name, int x, int y, EventHandler changed)
            {
                Toggle toggle = new Toggle();
                toggle.Text = name;
                toggle.Font = Type(9.5f * Zoom);
                toggle.Bounds = Box(x, y + 3, 382, 22);
                toggle.CheckedChanged += changed;
                Controls.Add(toggle);
                return toggle;
            }

            private ScrollPanel List(int x, int y, int wide, int tall, SlimBar bar)
            {
                ScrollPanel list = new ScrollPanel { Bounds = Box(x, y, wide, tall), BackColor = Ground };
                list.Scrolled += delegate { bar.Invalidate(); };
                Controls.Add(list);
                bar.Bounds = new Rectangle(Z(x + wide + 4), Z(y), Z(SlimBar.Thin), Z(tall));
                bar.BackColor = Ground;
                bar.Watch(list);
                Controls.Add(bar);
                return list;
            }

            private void Says(string text, bool problem)
            {
                noticeTimer.Stop();
                notice.Text = text ?? "";
                notice.ForeColor = problem ? Rose : Cyan;
                if (!string.IsNullOrEmpty(text)) noticeTimer.Start();
            }

            internal void FillAll()
            {
                Frame.capri.KeepWorkMods();
                FillSeen();
                FillAdded();
                FillSlows();
            }

            private static void Clear(ScrollPanel list)
            {
                List<Control> old = new List<Control>();
                foreach (Control one in list.Controls) old.Add(one);
                foreach (Control one in old)
                {
                    Font own = one.Font;
                    list.Controls.Remove(one);
                    one.Dispose();
                    if (!ReferenceEquals(own, list.Font)) own.Dispose();
                }
                list.AutoScrollPosition = Point.Empty;
            }

            private static bool Has(string[] names, string name) { return Array.Exists(names, one => string.Equals(one, name, StringComparison.OrdinalIgnoreCase)); }

            // The apps she has seen and her own that were taken off, each once and not already added, most used first.
            internal List<string> Seen()
            {
                string[] now = Working.Apps;
                List<string> all = new List<string>();
                foreach (KeyValuePair<string, double> one in Favourites.Programs())
                {
                    string name = Working.Plain(one.Key);
                    if (name.Length > 0 && !Has(now, name) && !Has(all.ToArray(), name) && !Working.Is(new Sight { Program = name })) all.Add(name);
                }
                return all;
            }

            private string Typed { get { return search.Box.Text.Trim(); } }

            private List<string> Found()
            {
                string typed = Typed;
                return Seen().FindAll(name => typed.Length == 0 || name.IndexOf(typed, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            // Searching opens every kind with a match.
            private void FillSeen()
            {
                seen.SuspendLayout();
                Clear(seen);
                List<string> found = Found();
                bool searching = Typed.Length > 0;
                int y = 0;
                foreach (string kind in Working.Kinds)
                {
                    List<string> these = found.FindAll(name => Working.KindOf(name) == kind);
                    if (these.Count == 0) continue;
                    these.Sort(StringComparer.OrdinalIgnoreCase);
                    bool shown = searching || open.Contains(kind);
                    FoldHead head = new FoldHead { Text = kind.ToUpperInvariant() + " (" + these.Count + ")", Font = Type(10f * Zoom, true), Bounds = new Rectangle(0, Z(y), Z(SeenWide), Z(27)), Open = shown };
                    head.AccessibleName = kind + ", " + these.Count + (shown ? ", open" : ", folded");
                    string folding = kind;
                    head.Click += delegate { if (!open.Remove(folding)) open.Add(folding); FillSeen(); };
                    seen.Controls.Add(head);
                    y += 31;
                    if (!shown) continue;
                    foreach (string name in these)
                    {
                        string app = name;
                        AppRow row = new AppRow(app) { Font = Type(9.5f * Zoom), Bounds = new Rectangle(Z(6), Z(y), Z(SeenWide - 6), Z(26)), Chosen = app == pickedSeen };
                        row.AccessibleName = app + ", seen. Enter adds it";
                        row.Click += delegate { PickSeen(app); };
                        row.DoubleClick += delegate { PickSeen(app); AddPicked(); };
                        row.Chose += delegate { PickSeen(app); AddPicked(); };
                        seen.Controls.Add(row);
                        y += 28;
                    }
                    y += 6;
                }
                if (found.Count == 0)
                {
                    Label none = new Note { Text = searching ? "Nothing she has seen is called that. Browse for its .exe below." : "Nothing left to add.", Font = Small(), ForeColor = Faint, BackColor = Ground, Bounds = new Rectangle(Z(6), 0, Z(SeenWide - 12), Z(40)), UseMnemonic = false };
                    seen.Controls.Add(none);
                }
                seen.ResumeLayout();
                seenBar.Sync();
            }

            private void FillAdded()
            {
                added.SuspendLayout();
                Clear(added);
                string typed = Typed;
                List<string> now = new List<string>(Working.Apps).FindAll(name => typed.Length == 0 || name.IndexOf(typed, StringComparison.OrdinalIgnoreCase) >= 0);
                now.Sort(StringComparer.OrdinalIgnoreCase);
                int y = 0;
                foreach (string name in now)
                {
                    string app = name;
                    AppRow row = new AppRow(app) { Font = Type(9.5f * Zoom), Bounds = new Rectangle(0, Z(y), Z(AddedWide), Z(26)), Chosen = app == pickedAdded };
                    row.AccessibleName = app + ", added. Enter takes it off";
                    row.Click += delegate { PickAdded(app); };
                    row.DoubleClick += delegate { PickAdded(app); TakePicked(); };
                    row.Chose += delegate { PickAdded(app); TakePicked(); };
                    added.Controls.Add(row);
                    y += 28;
                }
                if (now.Count == 0)
                    added.Controls.Add(new Note { Text = typed.Length > 0 ? "No added app is called that." : "None. Only \u221E slows her now.", Font = Small(), ForeColor = Faint, BackColor = Ground, Bounds = new Rectangle(0, 0, Z(AddedWide), Z(40)), UseMnemonic = false });
                added.ResumeLayout();
                addedBar.Sync();
            }

            // Hers first, then each mod that has made a timer that waits.
            private void FillSlows()
            {
                slows.SuspendLayout();
                Clear(slows);
                int y = 0;
                y = Slow(Working.Chatter, "Her chatter", "Pop-ins, jokes, feeling ignored", y);
                y = Slow(Working.Pet, "Tamagotchi", "Hunger and points", y);
                y = Slow(Working.CheckIns, "Check-ins", "Water, stretch, eyes", y);
                y = Slow(Working.Wandering, "Her wandering", "Walks, perches, screen hops", y);
                List<string> mods = Working.Mods();
                y += 6;
                Label heading = new Note { Text = "MODS", Font = Type(9.5f * Zoom, true), ForeColor = Rose, BackColor = Ground, Bounds = new Rectangle(0, Z(y), Z(SlowsWide), Z(20)), UseMnemonic = false };
                slows.Controls.Add(heading);
                y += 22;
                if (mods.Count == 0)
                    slows.Controls.Add(new Note { Text = "No mod has made a timer yet.", Font = Small(), ForeColor = Faint, BackColor = Ground, Bounds = new Rectangle(0, Z(y), Z(SlowsWide), Z(20)), UseMnemonic = false });
                foreach (string id in mods)
                {
                    Mod mod = Frame.capri.ModList == null ? null : Frame.capri.ModList.All.Find(each => each.File.Id == id);
                    y = Slow(Working.ModMark + id, mod == null ? id : mod.File.Name, "Its After and Every timers", y);
                }
                slows.ResumeLayout();
                slowsBar.Sync();
            }

            // A row's choices: check-ins can only be paused or left, and her wandering locked or left.
            internal static Working.Hold[] Choices(string row)
            {
                if (row == Working.CheckIns) return new[] { Working.Hold.Leave, Working.Hold.Pause };
                if (row == Working.Wandering) return new[] { Working.Hold.Pause, Working.Hold.Leave };
                return new[] { Working.Hold.Extend, Working.Hold.Pause, Working.Hold.Leave };
            }

            internal static string Said(string row, Working.Hold hold) { return row == Working.Wandering && hold == Working.Hold.Pause ? "Lock" : hold.ToString(); }

            private int Slow(string row, string name, string note, int y)
            {
                Label named = new Note { Text = name, Font = Type(9.5f * Zoom), ForeColor = Rose, BackColor = Ground, AutoEllipsis = true, UseMnemonic = false, Bounds = new Rectangle(0, Z(y), Z(SlowsWide - 108), Z(22)), TextAlign = ContentAlignment.MiddleLeft };
                Label about = new Note { Text = note, Font = Look.Mono(8f * Zoom, FontStyle.Regular, GraphicsUnit.Point), ForeColor = Faint, BackColor = Ground, AutoEllipsis = true, UseMnemonic = false, Bounds = new Rectangle(0, Z(y + 22), Z(SlowsWide), Z(16)) };
                Key choice = new Key { Text = Said(row, Working.HoldOf(row)), Chevron = true, Font = Type(9f * Zoom, true), Bounds = new Rectangle(Z(SlowsWide - 100), Z(y), Z(100), Z(22)) };
                choice.AccessibleName = name + ": " + choice.Text + ". " + note;
                choice.Tag = row;
                choice.Click += delegate { ShowHolds(choice, row, name); };
                slows.Controls.Add(named);
                slows.Controls.Add(about);
                slows.Controls.Add(choice);
                return y + 44;
            }

            private void ShowHolds(Key choice, string row, string name)
            {
                foreach (ToolStripItem old in new List<ToolStripItem>(ItemsOf(holdMenu))) { holdMenu.Items.Remove(old); old.Dispose(); }
                Working.Hold now = Working.HoldOf(row);
                foreach (Working.Hold hold in Choices(row))
                {
                    Working.Hold picked = hold;
                    holdMenu.Items.Add(Choice(Said(row, hold), hold == now, delegate { SetHold(row, name, picked); }));
                }
                holdMenu.Show(choice, new Point(0, choice.Height));
            }

            private static List<ToolStripItem> ItemsOf(ToolStrip strip)
            {
                List<ToolStripItem> all = new List<ToolStripItem>();
                foreach (ToolStripItem one in strip.Items) all.Add(one);
                return all;
            }

            internal void SetHold(string row, string name, Working.Hold hold)
            {
                Frame.capri.SetWorkHold(row, hold);
                FillSlows();
                Says(name + ": " + Said(row, hold) + (hold == Working.Hold.Extend ? ", by While working's times." : hold == Working.Hold.Leave ? ", at its own pace." : ", while a work app is in front."), false);
            }

            private void PickSeen(string app) { pickedSeen = app; pickedAdded = null; Repick(); }

            private void PickAdded(string app) { pickedAdded = app; pickedSeen = null; Repick(); }

            private void Repick()
            {
                foreach (ScrollPanel list in new[] { seen, added })
                    foreach (Control one in list.Controls) { AppRow row = one as AppRow; if (row != null) row.Chosen = row.Text == (list == seen ? pickedSeen : pickedAdded); }
            }

            private void Use(List<string> apps, string said)
            {
                Frame.capri.SetWorkApps(apps.ToArray());
                FillSeen();
                FillAdded();
                Says(said, false);
            }

            internal void AddPicked()
            {
                if (pickedSeen == null) { Says("Pick an app on the left first.", true); return; }
                string app = pickedSeen;
                pickedSeen = null;
                List<string> now = new List<string>(Working.Apps);
                now.Add(app);
                pickedAdded = app;
                Use(now, app + " is a work app now.");
            }

            // Enter in the search adds the first app it found.
            private void AddFirstFound()
            {
                List<string> found = Found();
                if (found.Count == 0) { Says("Nothing she has seen is called that. Browse for its .exe below.", true); return; }
                pickedSeen = found[0];
                AddPicked();
            }

            internal void TakePicked()
            {
                if (pickedAdded == null) { Says("Pick an added app first.", true); return; }
                string app = pickedAdded;
                pickedAdded = null;
                List<string> now = new List<string>(Working.Apps);
                now.RemoveAll(one => string.Equals(one, app, StringComparison.OrdinalIgnoreCase));
                pickedSeen = app;
                Use(now, app + " isn't a work app now.");
            }

            internal void TakeAll()
            {
                pickedAdded = pickedSeen = null;
                Use(new List<string>(), "Every app is off. Only \u221E slows her now.");
            }

            private void Browse()
            {
                using (OpenFileDialog pick = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe", Title = "A program to add to work apps" })
                    if (pick.ShowDialog(this) == DialogResult.OK) AddExe(pick.FileName);
            }

            // Kept by its name only, "program" for program.exe, so every copy of it counts.
            internal void AddExe(string path)
            {
                string app = Working.Plain(Path.GetFileName(path ?? ""));
                if (app.Length == 0) return;
                List<string> now = new List<string>(Working.Apps);
                if (Has(now.ToArray(), app)) { Says(app + " is already a work app.", false); return; }
                now.Add(app);
                pickedAdded = app;
                Use(now, app + " is a work app now, any copy of it.");
            }

            internal List<string> Rows()
            {
                List<string> rows = new List<string>();
                foreach (Control one in slows.Controls) if (one is Key && one.Tag is string) rows.Add((string)one.Tag);
                return rows;
            }

            private List<string> NamesIn(ScrollPanel list)
            {
                List<string> names = new List<string>();
                foreach (Control one in list.Controls) if (one is AppRow) names.Add(one.Text);
                return names;
            }

            // Null when each row's words fit it.
            private string RowsCrowded()
            {
                foreach (ScrollPanel list in new[] { seen, added, slows })
                    foreach (Control one in list.Controls)
                    {
                        Key key = one as Key;
                        if (key != null && TextRenderer.MeasureText(key.Text, key.Font).Width + Z(26) > key.Width) return "doesn't fit: " + key.Text;
                        Label label = one as Label;
                        if (label != null && label.AutoEllipsis && TextRenderer.MeasureText(label.Text, label.Font).Width > label.Width) return "doesn't fit: " + label.Text;
                        FoldHead fold = one as FoldHead;
                        if (fold != null && TextRenderer.MeasureText(fold.Text, fold.Font).Width > fold.Width - Z(44)) return "doesn't fit: " + fold.Text;
                    }
                return null;
            }

            // A 200-app Added list walked by Down, by the wheel and by the bar a page at a time: every row comes into
            // view each way. Null when it did.
            internal string ScrollCheck()
            {
                string[] was = Working.Apps;
                try
                {
                    List<string> many = new List<string>();
                    for (int i = 0; i < 200; i++) many.Add("app" + i.ToString("000"));
                    Working.Apps = many.ToArray();
                    search.Box.Text = "";
                    FillAdded();
                    List<AppRow> rows = new List<AppRow>();
                    foreach (Control one in added.Controls) { AppRow row = one as AppRow; if (row != null) rows.Add(row); }
                    rows.Sort((x, y) => x.Top.CompareTo(y.Top));
                    if (rows.Count != 200) return "the 200-app list showed " + rows.Count + " rows";
                    Func<AppRow, bool> shown = row => row.Bottom > 0 && row.Top < added.ClientSize.Height;
                    HashSet<string> keyed = new HashSet<string>(), wheeled = new HashSet<string>(), barred = new HashSet<string>();
                    added.AutoScrollPosition = Point.Empty;
                    AppRow at = rows[0];
                    at.Focus();
                    for (int i = 0; i < 260 && at != null; i++)
                    {
                        if (shown(at) && at.Bottom <= added.ClientSize.Height && at.Top >= 0) keyed.Add(at.Text);
                        at = at.Step(1);
                    }
                    added.AutoScrollPosition = Point.Empty;
                    for (int i = 0; i < 400; i++)
                    {
                        foreach (AppRow row in rows) if (shown(row)) wheeled.Add(row.Text);
                        int before = -added.AutoScrollPosition.Y;
                        Native.SendMessage(added.Handle, 0x020A, new IntPtr(-120 << 16), IntPtr.Zero);
                        Application.DoEvents();
                        if (-added.AutoScrollPosition.Y == before) break;
                    }
                    added.AutoScrollPosition = Point.Empty;
                    addedBar.Sync();
                    for (int i = 0; i < 400; i++)
                    {
                        foreach (AppRow row in rows) if (shown(row)) barred.Add(row.Text);
                        int before = -added.AutoScrollPosition.Y;
                        addedBar.Page(true);
                        if (-added.AutoScrollPosition.Y == before) break;
                    }
                    if (keyed.Count != 200 || wheeled.Count != 200 || barred.Count != 200)
                        return "a 200-app list skipped rows: Down reached " + keyed.Count + ", the wheel " + wheeled.Count + ", the bar " + barred.Count;
                    return null;
                }
                finally { Working.Apps = was; FillAdded(); }
            }

            // Driven as they'd use it: the two sections' switches, what she has seen (and only that) in folds by kind A to Z,
            // one search over both lists, > adding, < and << taking off, and an .exe kept by its name.
            internal string Drive(string picture)
            {
                TakeAll();
                Mainframe main = Frame;
                if (main.whileWorking == null || main.whileWorking.Parent != this || main.callApps.Parent != this || main.quietCalls.Parent != this || main.focusBox.Parent != this || main.quietBox.Parent != this
                    || main.WhileWorkingShown != main.capri.WhileWorking || main.quietCalls.Checked != main.capri.QuietOnCalls || main.focusBox.Checked != main.capri.FocusOn || main.quietBox.Checked != main.capri.QuietTimed)
                    return "Focus's and quiet's settings aren't in their window, or don't show hers.";
                main.capri.SetFocus(false);
                bool focusOff = !main.focusBox.Checked && !main.focusSwitch.Checked && !Working.On;
                main.capri.SetFocus(true);
                if (!focusOff || !main.focusBox.Checked || !Working.On) return "Focus mode's switch didn't turn focus off and on, both switches following.";
                string scrolled = ScrollCheck();
                if (scrolled != null) return scrolled;
                string crowded = Crowded() ?? RowsCrowded();
                if (crowded != null) return crowded;
                List<string> left = Seen();
                if (!left.Contains("Spotify") || !left.Contains("Code") || left.Contains("excel") || Working.KindOf("Spotify") != Working.Music || Working.KindOf("SpaceGame") != Working.Games || Working.KindOf("brave") != Working.Browsers
                    || Working.KindOf("notepad") != Working.Editors || Working.KindOf("discord") != Working.Calls || Working.KindOf("toolbox") != Working.Other || Working.KindOf("Code") != Working.Editors)
                    return "The apps she has seen aren't listed by kind, or one she hasn't seen is: " + string.Join(", ", left.ToArray());
                if (Working.Is(new Sight { Program = "Code.exe" }) || NamesIn(added).Count != 0) return "Her own work apps counted, or were added, without them adding any.";
                foreach (string kind in Working.Kinds) open.Add(kind);
                FillSeen();
                if (picture != null) Picture(picture);
                open.Clear();
                search.Box.Text = "code";
                PickSeen("Code");
                AddPicked();
                search.Box.Text = "spot";
                List<string> found = NamesIn(seen);
                if (found.Count != 1 || found[0] != "Spotify" || NamesIn(added).Count != 0) return "The search didn't find only Spotify, or left the Added list unfiltered: " + string.Join(", ", found.ToArray());
                PickSeen("Spotify");
                AddPicked();
                if (!Has(Working.Apps, "Spotify") || !Working.Is(new Sight { Program = "Spotify.exe" }) || NamesIn(seen).Contains("Spotify") || !NamesIn(added).Contains("Spotify"))
                    return "> didn't add Spotify as a work app.";
                search.Box.Text = "";
                List<string> listed = NamesIn(added), sorted = new List<string>(listed);
                sorted.Sort(StringComparer.OrdinalIgnoreCase);
                if (listed.Count != 2 || string.Join(",", listed.ToArray()) != string.Join(",", sorted.ToArray())) return "The Added list isn't A to Z: " + string.Join(", ", listed.ToArray());
                PickAdded("Code");
                TakePicked();
                if (Has(Working.Apps, "Code") || Working.Is(new Sight { Program = "Code.exe" }) || !Seen().Contains("Code"))
                    return "< didn't take Code off, or it didn't go back to the left.";
                AddExe(@"C:\Tools\Program.exe");
                if (!Has(Working.Apps, "Program") || !Working.Is(new Sight { Program = "program.exe" }) || NamesIn(added).Exists(one => one.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
                    return "An .exe wasn't kept by its name: " + string.Join(", ", Working.Apps);
                if (Frame.capri.WorkAppsSaid == null || Frame.capri.WorkAppsSaid.IndexOf("Program", StringComparison.Ordinal) < 0) return "The work apps weren't kept in her settings.";
                TakeAll();
                if (Working.Apps.Length != 0 || Working.Is(new Sight { Program = "Spotify" }) || NamesIn(added).Count != 0 || Frame.capri.WorkAppsSaid != null) return "<< didn't take every app off.";
                if (Choices(Working.CheckIns).Length != 2 || Said(Working.Wandering, Working.HoldOf(Working.Wandering)) != "Lock" || Working.HoldOf(Working.CheckIns) != Working.Hold.Leave || Working.HoldOf(Working.Chatter) != Working.Hold.Extend)
                    return "Hers don't start as Extend, Leave for check-ins and Lock for her wandering.";
                Key chatter = null;
                foreach (Control one in slows.Controls) if (one is Key && (string)one.Tag == Working.Chatter) chatter = (Key)one;
                if (chatter == null) return "Her chatter has no row in What it slows.";
                ShowHolds(chatter, Working.Chatter, "Her chatter");
                int items = holdMenu.Items.Count;
                holdMenu.Items[1].PerformClick();
                holdMenu.Close();
                if (items != 3 || Working.HoldOf(Working.Chatter) != Working.Hold.Pause || (Frame.capri.WorkHoldsSaid ?? "").IndexOf("chatter=Pause", StringComparison.Ordinal) < 0)
                    return "Her chatter's dropdown didn't set Pause and keep it: " + Frame.capri.WorkHoldsSaid;
                SetHold(Working.Chatter, "Her chatter", Working.Hold.Extend);
                return Crowded() ?? RowsCrowded();
            }
        }

        // An app's row: its name in setting-name red on her clipped plate, lit while picked; Enter moves it across.
        private sealed class AppRow : Control
        {
            private bool chosen;

            internal AppRow(string name)
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                Text = name;
                BackColor = Ground;
                Cursor = Cursors.Hand;
                TabStop = true;
                AccessibleRole = AccessibleRole.ListItem;
            }

            internal event EventHandler Chose;

            internal bool Chosen { get { return chosen; } set { if (chosen != value) { chosen = value; Invalidate(); } } }

            protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Up || keyData == Keys.Down || base.IsInputKey(keyData); }

            // Up and Down go to the row before or after in its list, scrolled into view, so every row can be reached.
            internal AppRow Step(int by)
            {
                if (Parent == null) return null;
                List<AppRow> rows = new List<AppRow>();
                foreach (Control one in Parent.Controls) { AppRow row = one as AppRow; if (row != null) rows.Add(row); }
                rows.Sort((x, y) => x.Top.CompareTo(y.Top));
                int at = rows.IndexOf(this) + by;
                if (at < 0 || at >= rows.Count) return null;
                AppRow next = rows[at];
                ScrollableControl list = Parent as ScrollableControl;
                if (list != null) list.ScrollControlIntoView(next);
                next.Focus();
                ScrollPanel panel = Parent as ScrollPanel;
                if (panel != null) panel.Moved();
                return next;
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up) { e.Handled = true; Step(e.KeyCode == Keys.Down ? 1 : -1); return; }
                if (e.KeyCode == Keys.Enter && Chose != null) { e.SuppressKeyPress = true; Chose(this, EventArgs.Empty); return; }
                if (e.KeyCode == Keys.Space) { e.SuppressKeyPress = true; OnClick(EventArgs.Empty); }
                base.OnKeyDown(e);
            }

            protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }

            protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                float s = ScaleOf(this);
                Graphics graphics = e.Graphics;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (Brush ground = new SolidBrush(Ground)) graphics.FillRectangle(ground, ClientRectangle);
                float line = Math.Max(1f, s);
                RectangleF plate = new RectangleF(line / 2, line / 2, Width - line, Height - line);
                using (GraphicsPath path = Cut(plate, 0, 0, 5 * s, 0))
                {
                    using (Brush back = new SolidBrush(chosen ? Hover : Ground)) graphics.FillPath(back, path);
                    if (chosen || (Focused && ShowFocusCues)) using (Pen rim = new Pen(chosen ? Hot : Edge, line)) graphics.DrawPath(rim, path);
                }
                TextRenderer.DrawText(graphics, Text, Font, new Rectangle((int)Math.Round(10 * s), 0, Width - (int)Math.Round(16 * s), Height), Rose, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
        }
    }

    internal sealed partial class Companion
    {
        internal string WorkAppsSaid { get { return preferences.WorkApps; } }
        internal string WorkHoldsSaid { get { return preferences.WorkHolds; } }

        // While working's app list and what it slows: each row's clock at Extend, Pause and Leave, her hunger by the
        // Tamagotchi row, every row stopped at infinity, the window at three sizes, and a mod's timer by its row.
        private void SmokeWorkApps(List<string> checks, string output)
        {
            string keptWas = Favourites.Kept, appsWas = preferences.WorkApps, holdsWas = Working.HoldsSaid;
            Func<Sight> aheadWas = Working.Ahead;
            Func<DateTime> realWas = Working.Real;
            int timesWas = preferences.WhileWorking;
            long fedWas = preferences.FedAt;
            string kept = Path.Combine(output, "work-apps-used.txt");
            Sight work = new Sight { Program = "Code.exe" }, play = new Sight { Program = "SpaceGame.exe" };
            Action<Sight> front = delegate(Sight sight) { Working.Ahead = delegate { return sight; }; Working.Forget(); Working.Now(SimTime.UtcNow); };
            try
            {
                File.WriteAllText(kept, "");
                Favourites.Kept = kept;
                Favourites.Reload();
                DateTime at = DateTime.UtcNow;
                foreach (string one in new[] { "Spotify.exe", "brave.exe", "notepad.exe", "Discord.exe", "toolbox.exe", "Code.exe", "SpaceGame.exe" }) { Favourites.Seen(one, at); at = at.AddMinutes(10); }
                Favourites.Play("SpaceGame.exe");
                Favourites.Save();
                Favourites.Reload();
                if (!Favourites.Played("SpaceGame") || Working.KindOf("SpaceGame") != Working.Games || Working.KindOf("idea2023") != Working.Editors || Working.KindOf("winword") != Working.Office)
                    throw new Exception("A game seen in front wasn't kept as one, or her own work apps aren't under their kinds.");

                DateTime now = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);
                Working.Real = delegate { return now; };
                Working.HoldsSaid = null;
                // Only an app they added counts.
                SetWorkApps(new[] { "Code" });
                SetWhileWorking(5);
                front(work);
                Func<string, double> clock = Working.Clock;
                double chatter = clock(Working.Chatter), pet = clock(Working.Pet), care = clock(Working.CheckIns), roam = clock(Working.Wandering);
                now = now.AddSeconds(100);
                bool stretched = Math.Abs(clock(Working.Chatter) - chatter - 20) < 1e-6 && Math.Abs(clock(Working.Pet) - pet - 20) < 1e-6
                    && Math.Abs(clock(Working.CheckIns) - care - 100) < 1e-6 && Math.Abs(clock(Working.Wandering) - roam) < 1e-6;
                front(play);
                chatter = clock(Working.Chatter);
                roam = clock(Working.Wandering);
                now = now.AddSeconds(100);
                bool free = Math.Abs(clock(Working.Chatter) - chatter - 100) < 1e-6 && Math.Abs(clock(Working.Wandering) - roam - 100) < 1e-6 && Working.Rate(Working.CheckIns, false) == 1;
                if (!stretched || !free)
                    throw new Exception("While working's rows didn't count at their own rates, x5 at work and their pace off it: " + stretched + ", " + free);
                front(work);
                SetWorkHold(Working.Chatter, Working.Hold.Pause);
                SetWorkHold(Working.CheckIns, Working.Hold.Pause);
                SetWorkHold(Working.Wandering, Working.Hold.Leave);
                bool held = Working.Rate(Working.Chatter, true) == 0 && double.IsInfinity(ChatterStretch) && Working.Rate(Working.CheckIns, true) == 0 && Working.Rate(Working.Wandering, true) == 1
                    && (preferences.WorkHolds ?? "").IndexOf("check-ins=Pause", StringComparison.Ordinal) >= 0;
                int add;
                preferences.FedAt = SimTime.UtcNow.AddHours(-4).Ticks;
                string extended = Hunger("snack", out add);
                SetWorkHold(Working.Pet, Working.Hold.Leave);
                string left = Hunger("snack", out add);
                SetWorkHold(Working.Pet, Working.Hold.Pause);
                string paused = Hunger("snack", out add);
                if (!held || extended != null || left != "peckish" || paused != null)
                    throw new Exception("A row's Pause or Leave didn't hold, or her hunger didn't follow the Tamagotchi row: " + held + ", " + extended + ", " + left + ", " + paused);
                SetWhileWorking(Working.Never);
                front(play);
                double before = clock(Working.CheckIns);
                now = now.AddSeconds(100);
                bool stopped = Math.Abs(clock(Working.CheckIns) - before) < 1e-6 && Working.Rate(Working.Wandering, Working.Now(SimTime.UtcNow)) == 0 && Working.Rate(Working.ModMark + "any", false) == 0;
                if (!stopped) throw new Exception("At infinity every row didn't stop, whatever is in front.");
                checks.Add("PASS While working's rows: at x5 with a work app in front her chatter and Tamagotchi count a fifth, check-ins keep their pace and her wandering is locked; Pause and Leave hold per row and are kept; hunger follows the Tamagotchi row; at infinity every row stops whatever is in front, check-ins and mods' timers included");
            }
            finally
            {
                Working.Real = realWas;
                preferences.FedAt = fedWas;
            }

            try
            {
                if (mainframe == null) { checks.Add("LIMIT the work apps window: no Mainframe here"); return; }
                Working.HoldsSaid = null;
                SetWhileWorking(Working.Stock);
                float machine = mainframe.Px(1000) / 1000f;
                List<string> wrongs = new List<string>();
                System.Windows.Forms.FormWindowState stateWas = mainframe.WindowState;
                bool shownWas = mainframe.Visible;
                try
                {
                    mainframe.Restore();
                    foreach (float zoom in new[] { 1f, 1.5f, 2f })
                    {
                        string wrong = mainframe.WorkAppsAt(zoom / machine, Path.Combine(output, "focus-options-" + (int)(zoom * 100) + ".png"));
                        if (wrong != null) wrongs.Add((int)(zoom * 100) + "% " + wrong);
                    }
                }
                finally
                {
                    mainframe.WindowState = stateWas;
                    if (!shownWas) mainframe.Hide();
                }
                if (wrongs.Count > 0) throw new Exception("The " + Mainframe.FocusLabel + " window went wrong: " + string.Join(" / ", wrongs.ToArray()));
                checks.Add("PASS " + Mainframe.FocusLabel + " from its key in CAPRI, at 100%, 150% and 200%: a Focus section (its switch, While working) and a Quiet section (its switch, the Quiet timer's hours, quiet on calls and in videos, call windows), showing hers, Focus mode's switch and CAPRI's following each other; the Added list starts empty and her own work apps don't count; only apps she has seen are listed, by kind, A to Z; one search filters both lists; a 200-app list reaches every row by Down, the wheel and the bar; > adds, < takes off and back to the left, an .exe is kept by its name, << takes all off, and a row's dropdown sets its hold, all kept in her settings (pictures focus-options-*.png)");
                SmokeWorkTimer(checks, output, work, play, front);
            }
            finally
            {
                Working.Ahead = aheadWas;
                Working.Forget();
                Favourites.Kept = keptWas;
                Favourites.Reload();
                SetWorkApps(appsWas == null ? null : appsWas.Split('\n'));
                Working.HoldsSaid = holdsWas;
                SaveWorkHolds();
                preferences.WhileWorking = timesWas;
                Working.Times = WhileWorking;
                SavePreferences();
            }
        }

        // A code mod's Every timer: listed at Extend, paused and left by its row while a work app is in front, slower at
        // x10, at its pace with nothing of work in front, and its row gone with the mod.
        private void SmokeWorkTimer(List<string> checks, string output, Sight work, Sight play, Action<Sight> front)
        {
            Mods mods = ModList;
            Func<ModFile, string, bool> askWas = mainframe.ModAsk;
            mainframe.ModAsk = delegate { return true; };
            const string id = "work-timer";
            string row = Working.ModMark + id;
            string ticks = Path.Combine(Path.Combine(mods.Folder, id + "-data"), "ticks");
            Func<int> count = delegate { try { return File.Exists(ticks) ? File.ReadAllText(ticks).Length : 0; } catch (IOException) { return 0; } };
            Action<double> wait = delegate(double seconds)
            {
                System.Diagnostics.Stopwatch took = System.Diagnostics.Stopwatch.StartNew();
                while (took.Elapsed.TotalSeconds < seconds) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
            };
            Func<double, int> over = delegate(double seconds) { int from = count(); wait(seconds); return count() - from; };
            try
            {
                string file = Path.Combine(output, id + Mods.CodeExtension);
                File.WriteAllText(file, "// capri-mod: 1\n// id: " + id + "\n// name: Work Timer\n// version: 1\n// author: Smoke\n// description: A test.\n"
                    + "using System; using System.IO; using CapriDesktop;\n"
                    + "public class M : ICapriMod { ICapriHost c; public void Start(ICapriHost h) { c = h; h.Timers.Every(0.1, () => File.AppendAllText(Path.Combine(c.Folder, \"ticks\"), \"x\")); }"
                    + " public bool Heard(string w) { return false; } public void Stop() { } }");
                front(play);
                if (!mainframe.ImportMod(file)) throw new Exception("The timer mod was refused: " + mainframe.ModStatusText);
                wait(0.8);
                mainframe.OpenWorkApps();
                List<string> rows = mainframe.WorkRowsShown;
                mainframe.CloseWorkApps();
                if (count() < 2 || !Working.Mods().Contains(id) || Working.HoldOf(row) != Working.Hold.Extend || (preferences.WorkHolds ?? "").IndexOf(row + "=Extend", StringComparison.Ordinal) < 0 || !rows.Contains(row))
                    throw new Exception("A mod's timer didn't list it in What it slows at Extend: " + count() + " ticks, " + preferences.WorkHolds + ", rows " + string.Join(", ", rows.ToArray()));
                front(work);
                SetWorkHold(row, Working.Hold.Pause);
                wait(0.3);
                int paused = over(1.0);
                SetWorkHold(row, Working.Hold.Leave);
                wait(1.1);
                int kept = over(1.0);
                SetWhileWorking(10);
                SetWorkHold(row, Working.Hold.Extend);
                wait(0.3);
                int stretched = over(1.0);
                SetWorkHold(row, Working.Hold.Pause);
                front(play);
                wait(1.1);
                int off = over(1.0);
                if (paused > 1 || kept < 4 || stretched > 3 || stretched >= kept || off < 4)
                    throw new Exception("A mod's timer didn't follow its row: paused " + paused + ", left " + kept + ", x10 " + stretched + ", off work " + off + " ticks a second");
                Mod mod = mods.All.Find(each => each.File.Id == id);
                if (mod != null) mods.Remove(mod);
                wait(0.2);
                KeepWorkMods();
                if (Working.Mods().Contains(id) || (preferences.WorkHolds ?? "").IndexOf(row, StringComparison.Ordinal) >= 0) throw new Exception("A removed mod's row stayed in What it slows.");
                checks.Add("PASS a mod's capri.Timers: its first Every lists it in What it slows at Extend; with a work app in front Pause stops it (" + paused + " a second), Leave keeps its pace (" + kept + "), Extend at x10 slows it (" + stretched + "), with no work app in front it runs (" + off + "); removing the mod takes its row");
            }
            finally
            {
                mainframe.ModAsk = askWas;
                Mod left = mods.All.Find(each => each.File.Id == id);
                if (left != null) mods.Remove(left);
                KeepWorkMods();
            }
        }
    }
}
