using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Her six need rings above CAPRI whenever the Tamagotchi pet game is on, under a divider and a heading with its points,
    // where the tiles' room stops; a ring clicked opens what fills its need (374). Off, the tiles reach CAPRI as before.
    internal sealed partial class Mainframe
    {
        // The strip's room above CAPRI: its heading and divider, then the rings.
        private const int StripRoom = 92, StripRingsBelow = 26;
        private readonly List<PetRingControl> stripRings = new List<PetRingControl>();
        private readonly List<KeyValuePair<Control, int>> stripLeft = new List<KeyValuePair<Control, int>>();
        private Control stripHead;
        // The heading's points kept current while the strip shows.
        private readonly Clock.Ticker stripClock = new Clock.Ticker("ring strip") { Interval = 5000 };
        // For the checks: the strip shown or not whatever the pet game is doing.
        internal Func<bool> RingStripFor;

        private bool RingStripShown
        {
            get
            {
                Func<bool> standIn = RingStripFor;
                if (standIn != null) return standIn();
                return capri.Pet != null;
            }
        }

        private int StripTop { get { return CapriTop - StripRoom; } }

        // Where the tiles' room ends: the strip's divider while it shows, else CAPRI.
        private int ToolsBottom { get { return RingStripShown ? StripTop : CapriTop; } }

        private void BuildRingStrip()
        {
            int from = Controls.Count;
            stripHead = Section("TAMAGOTCHI", 0, StripTop);
            int gap = (328 - PetStats.Names.Length * PetRing) / Math.Max(1, PetStats.Names.Length - 1);
            for (int i = 0; i < PetStats.Names.Length; i++)
            {
                string need = PetStats.Names[i];
                PetRingControl ring = new PetRingControl { Need = need, Bounds = new Rectangle(28 + i * (PetRing + gap), StripTop + StripRingsBelow, PetRing, PetRing) };
                ring.AccessibleName = Cap(need);
                ring.Click += delegate { RingChoices(ring); };
                stripRings.Add(ring);
                Controls.Add(ring);
            }
            for (int i = from; i < Controls.Count; i++)
            {
                Control control = Controls[i];
                stripLeft.Add(new KeyValuePair<Control, int>(control, control.Left));
                toolTop[control] = control.Top;
                control.Left += Far;
            }
            PetStats.Changed += delegate { if (!petShown && RingStripShown && IsHandleCreated && !IsDisposed) BeginInvoke(new MethodInvoker(FillRingStrip)); };
            stripClock.Tick += delegate { if (Visible && !petShown && RingStripShown) StripHeading(); };
            stripClock.Start();
            Disposed += delegate { stripClock.Dispose(); };
        }

        // TAMAGOTCHI and the pet game's points beside it, as its heading in her menu has them.
        private void StripHeading()
        {
            if (stripHead == null) return;
            string points = capri.PetHeading;
            string text = points.Length == 0 ? "TAMAGOTCHI" : "TAMAGOTCHI   " + points;
            if (stripHead.Text == text) return;
            stripHead.Text = text;
            stripHead.Invalidate();
        }

        // What fills a ring's need, under it, as the Tamagotchi page's tiles open theirs: food, gifts, her games, a wash,
        // her rest, a pat.
        private void RingChoices(PetRingControl ring)
        {
            Point under = new Point(0, ring.Height);
            if (ring.Need == "stimulation") { MiniChoices().Show(ring, under); return; }
            List<ToolStripItem> items = RingItems(ring.Need);
            if (items.Count > 0) ListOf(items, ring, under);
        }

        private List<ToolStripItem> RingItems(string need)
        {
            List<ToolStripItem> items = capri.NeedItems(need);
            if (need == "social") items.Add(new ToolStripMenuItem("Head pats", null, delegate { capri.PatFromPet(); }));
            return items;
        }

        // For the checks: the heading as it reads, and a ring's choices as its list would show them.
        internal string StripHeadText { get { StripHeading(); return stripHead == null ? null : stripHead.Text; } }

        internal string RingListFor(string need)
        {
            if (need == "stimulation") return "games";
            List<string> said = new List<string>();
            foreach (ToolStripItem one in RingItems(need)) if (one.Available && !(one is ToolStripSeparator)) said.Add(one.Text);
            return string.Join(", ", said.ToArray());
        }

        // The strip at its place above CAPRI, its rings at their levels; hidden with the rest of the front page.
        private void FillRingStrip()
        {
            if (stripHead == null) return;
            StripHeading();
            toolTop[stripHead] = StripTop;
            stripHead.Top = Scaled(StripTop);
            foreach (PetRingControl ring in stripRings)
            {
                toolTop[ring] = StripTop + StripRingsBelow;
                ring.Top = Scaled(StripTop + StripRingsBelow);
                ring.Level = PetStats.Level(ring.Need);
                ring.Low = PetStats.Low(ring.Need);
                ring.Invalidate();
                SettingHint hint;
                if (hints.TryGetValue(ring, out hint)) hint.Exact = NeedFacts(ring.Need);
            }
        }

        private void PlaceRingStrip(bool main)
        {
            bool shown = main && !petShown && RingStripShown;
            foreach (KeyValuePair<Control, int> entry in stripLeft) entry.Key.Left = Scaled(entry.Value) + (shown ? 0 : Far);
            foreach (PetRingControl ring in stripRings) Reachable(ring, shown);
        }

        // For the checks: the strip's rings as they show, or null while it is hidden.
        internal string RingStripText
        {
            get
            {
                if (stripRings.Count == 0 || stripRings[0].Left >= Far / 2) return null;
                List<string> rings = new List<string>();
                foreach (PetRingControl ring in stripRings) rings.Add(ring.Need + " " + ring.Level);
                return string.Join(", ", rings.ToArray());
            }
        }
    }
}
