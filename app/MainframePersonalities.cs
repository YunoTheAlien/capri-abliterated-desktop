using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Mainframe
    {
        private PersonalitySheet personalitySheet;

        // Personality settings: every setting of her personalities, moods and Dynamic shown as rows; a change is kept in the
        // Your Own layer.
        internal void OpenPersonalitySettings()
        {
            if (personalitySheet != null && !personalitySheet.IsDisposed && personalitySheet.Zoom != zoom) { personalitySheet.Dispose(); personalitySheet = null; }
            if (personalitySheet == null || personalitySheet.IsDisposed) personalitySheet = Kept(new PersonalitySheet(this, SheetZoom(PersonalitySheet.Wide, PersonalitySheet.Tall)));
            if (!personalitySheet.Visible) personalitySheet.Show(this);
            personalitySheet.Activate();
        }

#if CHECKS
        // For the checks: pick a row of the list by its name ("" for her baseline) and picture the window.
        internal void PicturePersonalitySettings(string who, string path)
        {
            OpenPersonalitySettings();
            personalitySheet.Pick(who);
            Application.DoEvents();
            using (Bitmap shot = new Bitmap(personalitySheet.Width, personalitySheet.Height))
            {
                personalitySheet.DrawToBitmap(shot, new Rectangle(Point.Empty, shot.Size));
                using (Bitmap cut = Outlined(shot, personalitySheet)) cut.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        // The row names on its right side now, one a line, for the checks.
        internal string PersonalitySettingsRows { get { return personalitySheet == null || personalitySheet.IsDisposed ? "" : personalitySheet.Rows; } }
        internal string PersonalitySettingsValues { get { return personalitySheet == null || personalitySheet.IsDisposed ? "" : personalitySheet.RowValues; } }

        internal void ClosePersonalitySettings() { if (personalitySheet != null && !personalitySheet.IsDisposed) personalitySheet.Close(); }

        // For the checks: each row picked in turn, a wrong line wherever the right box's bar shows and the box doesn't
        // overflow, or the other way round; null when every one agrees.
        internal string PersonalityBarsWrong(string[] who, string folder)
        {
            List<string> wrong = new List<string>();
            foreach (string one in who)
            {
                PicturePersonalitySettings(one, System.IO.Path.Combine(folder, "personality-" + (one.Length == 0 ? "baseline" : one.ToLowerInvariant().Replace(' ', '-')) + ".png"));
                string said = personalitySheet.BarSays();
                if (said != null) wrong.Add((one.Length == 0 ? "Her baseline" : one) + ": " + said);
            }
            return wrong.Count == 0 ? null : string.Join("; ", wrong.ToArray());
        }
#endif

        private sealed class PersonalitySheet : Form, LookWindow
        {
            // The list in the one left column her windows share (LeftColumn), its bar inside the frame; the settings on the right.
            internal const int Wide = 1000, Tall = 620, Picks = LeftColumn.Wide + 10, Right = LeftColumn.Side, Panel = Wide - Right - 44, Top = 110;
            internal readonly float Zoom;
            private readonly Mainframe owner;
            private readonly ScrollPanel whoBox = new ScrollPanel(), setBox = new ScrollPanel();
            private readonly SlimBar whoBar = new SlimBar(), setBar = new SlimBar();
            private readonly List<WhoRow> whoRows = new List<WhoRow>();
            private readonly HashSet<string> folded = new HashSet<string>(StringComparer.Ordinal);
            private readonly List<string> rows = new List<string>();
            // Null for her baseline, else a personality or mood by name; Dynamic is its own page.
            private string picked;
            private bool pickedMood;

            // What each setting is and how far it goes, where the code has one: its stock value, its lowest and highest,
            // and its step. A setting a file holds that isn't here still shows, with a range from its own value.
            private static readonly Dictionary<string, double[]> Known = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "flirt", new double[] { 2, 0, 10, 1 } }, { "cringe", new double[] { 0, 0, 100, 5 } },
                { "sulks after", new double[] { 3, 0, 10, 1 } }, { "sulks at upset", new double[] { 6, 0, 20, 1 } },
                { "sulk alone", new double[] { 300, 0, 900, 30 } }, { "ignored dodge", new double[] { 25, 0, 100, 5 } },
                { "kind returns", new double[] { 1, 0.2, 2, 0.01 } }, { "mean scale", new double[] { 1, 0.15, 2, 0.01 } },
                { "drift push", new double[] { 0, -1, 1, 0.05 } }, { "like fades", new double[] { 0.6, 0, 1, 0.05 } },
                { "like least", new double[] { 0.4, 0, 1, 0.05 } }, { "dislike grows", new double[] { 0.35, 0, 1, 0.05 } },
                { "dislike most", new double[] { 2.5, 1, 5, 0.25 } }, { "willing pull", new double[] { 0.05, 0, 0.2, 0.01 } },
                { "willing slows", new double[] { 0.1, 0, 0.2, 0.01 } }, { "prank minutes", new double[] { 6, 2, 60, 1 } },
                { "gives up at", new double[] { 0, 0, 120, 5 } }, { "gives up minutes", new double[] { 45, 0, 240, 5 } },
                { "rotate min hours", new double[] { 4, 0.25, 24, 0.25 } }, { "rotate max hours", new double[] { 8, 0.25, 24, 0.25 } },
                { "forward min", new double[] { 2, 0, 10, 1 } }, { "forward max", new double[] { 8, 0, 10, 1 } },
                { "mood share", new double[] { 50, 0, 100, 5 } }, { "forget after", new double[] { 0, 0, 600, 10 } },
                { SceneOften, new double[] { SceneOftenStock, 0, 100, 5 } }, { SceneMinutes, new double[] { SceneMinutesStock, 1, 15, 1 } }
            };

            // Her scenes' two settings, kept with the personality's own settings.
            internal const string Scenes = "Scenes", SceneOften = "how often", SceneMinutes = "runtime minutes", SceneOn = "scenes on";
            internal const double SceneOftenStock = 25, SceneMinutesStock = 8;
            internal const string SceneNote = "She can start a scene that involves her interests or desires for a while, starts with her asking you.";

            internal PersonalitySheet(Mainframe owner, float zoom)
            {
                SuspendLayout();
                this.owner = owner;
                Zoom = zoom;
                Text = "Personality settings";
                Icon = owner.Icon;
                ShowInTaskbar = false;
                FormBorderStyle = FormBorderStyle.None;
                Neon.NeverFullScreen(this);
                StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.None;
                Font = Type(9.5f * zoom);
                BackColor = Ground;
                ForeColor = White;
                DoubleBuffered = true;
                KeyPreview = true;
                ClientSize = new Size(Z(Wide), Z(Tall));
                Heading(this, "PERSONALITY SETTINGS >", new Point(Z(24), Z(15)), Display(15f * zoom), Rose, 0);
                Key shut = new Key();
                shut.Bounds = Box(Wide - 54, 20, 26, 22);
                shut.Glyph = 2;
                shut.AccessibleName = "Close";
                shut.Click += delegate { Close(); };
                Controls.Add(shut);
                Caption("", 28, 52, Small, Faint, this);
                // Personality settings and Her topics are one window: a key each on the title's line, the part showing lit.
                parts[0] = Make("Personality", 260, 17, 110, 24, delegate { ShowPart(false); });
                parts[1] = Make("Topics", 376, 17, 110, 24, delegate { ShowPart(true); });
                parts[1].AccessibleName = "Her topics: what each personality of hers is into";
                pageKeys.Add(Make("Hear her", Wide - 248, 78, 100, 22, delegate { HearPicked(); }));
                pageKeys.Add(Make("Defaults", Wide - 128, 78, 100, 22, delegate { owner.capri.ResetYourOwn(picked); Fill(); }));
                capriHead = Heading(this, "CAPRI >", new Point(Z(24), Z(84)), Type(10f * zoom, true), White, Picks - 90);
                // Topics' own line, at the right edge: a personality and a mood to roll by, then Reroll Capri.
                rollKey = Make("Reroll Capri", Wide - 28 - 160, 78, 160, 22, delegate { Reroll(); });
                rollKey.AccessibleName = "Reroll Capri: her topics picked again for the personality and mood chosen";
                moodMenu = owner.Dropdown();
                moodKey = Make("", Wide - 28 - 160 - 8 - 170, 78, 170, 22, delegate { moodMenu.Show(moodKey, new Point(0, moodKey.Height)); });
                moodKey.Chevron = true;
                moodKey.AccessibleName = "The mood to roll her topics by";
                whoMenu = owner.Dropdown();
                whoKey = Make("", Wide - 28 - 160 - 8 - 170 - 8 - 170, 78, 170, 22, delegate { whoMenu.Show(whoKey, new Point(0, whoKey.Height)); });
                whoKey.Chevron = true;
                whoKey.AccessibleName = "The personality to roll her topics by";
                rollKeys.AddRange(new[] { whoKey, moodKey, rollKey });
                Disposed += delegate { whoMenu.Dispose(); moodMenu.Dispose(); };
                FillRollMenus();
                // Ends on the column's frame.
                pageKeys.Add(Make("Default all", LeftColumn.Rim - 88, 78, 88, 22, delegate { owner.capri.ResetYourOwn(null); Fill(); }));
                ShowPart(false);
                Boxed(whoBox, whoBar, Box(28, Top, Picks, Tall - Top - 28));
                Boxed(setBox, setBar, Box(Right + 4, Top, Panel, Tall - Top - 28));
                BuildWho();
                owner.Fit(this);
                ResumeLayout(false);
                Pick("");
            }

            private Font Small { get { return Look.Mono(8.5f * Zoom, FontStyle.Regular, GraphicsUnit.Point); } }

            // Which part shows: her settings, or Her topics for the one picked. The settings page's own keys go on Her topics.
            private bool topicsShown;
            private Head capriHead;
            private Key whoKey, moodKey, rollKey;
            private ContextMenuStrip whoMenu, moodMenu;
            private readonly List<Key> rollKeys = new List<Key>();
            private string rollWho, rollMood;

            // The side's own personalities and moods, the ones the list on the left shows.
            private void FillRollMenus()
            {
                if (rollWho == null) rollWho = Personalities.Under ?? Personalities.Chosen ?? "";
                if (rollWho == Personalities.Dynamic || rollWho == Personalities.Stock) rollWho = "";
                whoMenu.Items.Clear();
                whoMenu.Items.Add(Choice(RandomPick, rollWho == RandomPick, delegate { rollWho = RandomPick; FillRollMenus(); }));
                foreach (string named in Personalities.Names)
                {
                    if (named == Personalities.Stock || named == Personalities.Dynamic) continue;
                    string one = named;
                    if (rollWho.Length == 0) rollWho = one;
                    whoMenu.Items.Add(Choice(one, one == rollWho, delegate { rollWho = one; FillRollMenus(); }));
                }
                moodMenu.Items.Clear();
                moodMenu.Items.Add(Choice("No mood", string.IsNullOrEmpty(rollMood), delegate { rollMood = null; FillRollMenus(); }));
                foreach (string named in Dynamics.Allowed) { string one = named; moodMenu.Items.Add(Choice(one, one == rollMood, delegate { rollMood = one; FillRollMenus(); })); }
                whoKey.Text = rollWho;
                moodKey.Text = rollWho == RandomPick ? RandomPick : string.IsNullOrEmpty(rollMood) ? "No mood" : rollMood;
                moodKey.Enabled = rollWho != RandomPick;
            }

            // Her topics, love languages and, on the side that has it, her own set, picked again for the personality and
            // mood chosen. Random: she picks both from the side's own lists and becomes them.
            internal const string RandomPick = "Random";
            internal static Random Dice = new Random();

            private void Reroll()
            {
                if (string.IsNullOrEmpty(rollWho)) return;
                string who = rollWho, mood = rollMood;
                if (who == RandomPick)
                {
                    List<string> names = new List<string>(Personalities.Names);
                    names.RemoveAll(n => n == Personalities.Stock || n == Personalities.Dynamic);
                    List<string> moods = Dynamics.Allowed;
                    if (names.Count == 0) return;
                    who = names[Dice.Next(names.Count)];
                    int at = Dice.Next(moods.Count + 1);
                    mood = at == moods.Count ? null : moods[at];
                }
                Topics.Reroll(who, mood);
                LoveLanguages.Reroll(who, mood);
                OwnSet.Reroll(who, mood);
                if (rollWho == RandomPick)
                {
                    owner.capri.PickPersonality(who);
                    Dynamics.Hold(mood ?? "");
                    owner.ShowPersonality();
                    Rolled = who + (mood == null ? "" : " | " + mood);
                }
                if (picked != who) Pick(who); else Fill();
            }

            // What Random last landed on, for the checks.
            internal string Rolled { get; private set; }

            internal void RerollRandom() { rollWho = RandomPick; FillRollMenus(); Reroll(); }
            private readonly Key[] parts = new Key[2];
            private readonly List<Key> pageKeys = new List<Key>();

            internal bool TopicsShown { get { return topicsShown; } }

            internal void ShowPart(bool topics)
            {
                topicsShown = topics;
                for (int i = 0; i < parts.Length; i++)
                {
                    if (parts[i] == null) continue;
                    bool lit = (i == 1) == topics;
                    parts[i].Accent = lit ? Cyan : Rose;
                    parts[i].Status = lit ? "Shown" : null;
                    parts[i].Invalidate();
                }
                foreach (Key one in pageKeys) one.Visible = !topics;
                foreach (Key one in rollKeys) one.Visible = topics;
                // The list again, without the moods on the Topics page; a mood picked there goes back to her baseline.
                BuildWho();
                if (topics && pickedMood) { picked = ""; pickedMood = false; }
                foreach (WhoRow one in whoRows) one.Ticked = string.Equals((string)one.Tag, picked ?? "", StringComparison.OrdinalIgnoreCase);
                // The CAPRI band runs the column's width where Default all isn't on its line.
                if (capriHead != null) { capriHead.Span = topics ? Picks + 18 : Picks - 90; Invalidate(); }
                if (picked != null) Fill();
            }

            // Topics: hers (interests, love languages, her set) then theirs the same, each a fold of name, %, this week and
            // Remove with an add row. Her baseline and Dynamic show the personality she is in now.
            private void TopicsPage(int wide)
            {
                List<string> hers = new List<string>();
                string who = picked.Length > 0 && picked != Personalities.Dynamic && !pickedMood ? picked : Personalities.Under ?? Personalities.Chosen ?? "";
                bool personality = who.Length > 0 && who != Personalities.Dynamic;
                // Her topics are one set whoever she is; the personality she is in leans the first picks.
                if (personality) hers.AddRange(Topics.Hers(who, null));
                else { string[] kept = Topics.Saved(Topics.Shared); if (kept != null) hers.AddRange(kept); }
                int y = 0;
                topicFolds.Clear();
                Random start = new Random();
                List<string> theirs = new List<string>(Topics.TheirFlagged());
                {
                    TopicFold("HER TOPICS", hers, Pct(hers, delegate(string one) { return Fields.Level(Fields.Topic, Topics.Shared, one, 40 + start.Next(31)); }), wide, ref y, delegate(string one)
                    {
                        List<string> left = new List<string>(Topics.Saved(Topics.Shared) ?? new string[0]);
                        left.RemoveAll(t => string.Equals(t, one, StringComparison.OrdinalIgnoreCase));
                        Topics.Save(Topics.Shared, left.ToArray());
                        List<string> hand = new List<string>(Topics.Saved(Topics.ByHand + Topics.Shared) ?? new string[0]);
                        if (hand.RemoveAll(t => string.Equals(t, one, StringComparison.OrdinalIgnoreCase)) > 0) Topics.Save(Topics.ByHand + Topics.Shared, hand.ToArray());
                    }, delegate(string one)
                    {
                        one = Canon(one);
                        List<string> mine = new List<string>(Topics.Saved(Topics.Shared) ?? hers.ToArray());
                        if (!mine.Exists(t => string.Equals(t, one, StringComparison.OrdinalIgnoreCase))) mine.Add(one);
                        Topics.Save(Topics.Shared, mine.ToArray());
                        Topics.AddedByHand(who, one);
                        Fields.In(Fields.Topic, Topics.Shared, one, 60);
                    }, "A topic of hers", delegate(string one) { return Fields.Interactions(Fields.Topic, Topics.Shared, one); }, TopicNames(), delegate(string one, double level) { Fields.Set(Fields.Topic, Topics.Shared, Canon(one), level); });
                }
                if (personality)
                {
                    // Her love languages, both sides: strongest first, the first one furthest in.
                    List<string> loves = new List<string>(LoveLanguages.Hers(who));
                    y += 10;
                    TopicFold(LoveLanguages.Title, loves, Pct(loves, delegate(string one) { return Fields.Level(Fields.Love, who, one, Math.Max(30, 80 - 20 * loves.IndexOf(one))); }), wide, ref y,
                        delegate(string one) { LoveLanguages.Remove(who, one); }, delegate(string one) { LoveLanguages.Add(who, one); string named = LoveLanguages.Match(one); if (named != null) Fields.In(Fields.Love, who, named, 60); },
                        "A love language for " + who, delegate(string one) { return Fields.Interactions(Fields.Love, who, one); }, LoveNames(),
                        delegate(string one, double level) { string named = LoveLanguages.Match(one); if (named != null) Fields.Set(Fields.Love, who, named, level); });
                }
                // Her pet names: the words of hers she likes hearing, how often each was said to her this week, no %.
                if (personality)
                {
                    List<string> names = PetNames(who);
                    y += 10;
                    TopicFold("HER PET NAMES", names, Pct(names, delegate(string one) { return -1; }), wide, ref y,
                        delegate(string one) { Fields.Out(Fields.Word, who, one); }, delegate(string one) { Fields.In(Fields.Word, who, one.ToLowerInvariant(), 0); },
                        "A pet name for " + who, delegate(string one) { return Fields.Interactions(Fields.Word, who, one); }, AllPetNames());
                }
                // Her set, on her other side only: its title and names are the side's data.
                if (personality && OwnSet.Shown)
                {
                    List<string> set = new List<string>();
                    List<Topics.Topic> levels = new List<Topics.Topic>();
                    foreach (KeyValuePair<string, double> one in OwnSet.Hers(who)) { set.Add(one.Key); levels.Add(new Topics.Topic { Name = one.Key, Heading = Percent(one.Value) }); }
                    y += 10;
                    TopicFold(OwnSet.Title, set, levels, wide, ref y, delegate(string one) { OwnSet.Remove(who, one); }, delegate(string one) { OwnSet.Add(who, one); }, "Add one for " + who, delegate(string one) { return OwnSet.Interactions(who, one); }, OwnSet.Items, delegate(string one, double level) { OwnSet.Set(who, one, level); });
                }
                y += 10;
                TopicFold("YOUR TOPICS", theirs, Pct(theirs, delegate(string one) { return Fields.Level(Fields.Topic, Fields.Yours, one, 30); }), wide, ref y, Topics.Unflag,
                    delegate(string one) { one = Canon(one); Topics.AddTheirs(one); Fields.In(Fields.Topic, Fields.Yours, one, 60); }, "A topic of yours", delegate(string one) { return Fields.Interactions(Fields.Topic, Fields.Yours, one); }, TopicNames(), delegate(string one, double level) { Fields.Set(Fields.Topic, Fields.Yours, Canon(one), level); });
                List<string> given = Fields.Shown(Fields.Love, Fields.Yours);
                y += 10;
                TopicFold(LoveLanguages.TitleYours, given, Pct(given, delegate(string one) { return Fields.Level(Fields.Love, Fields.Yours, one, 0); }), wide, ref y,
                    delegate(string one) { Fields.Out(Fields.Love, Fields.Yours, one); }, delegate(string one) { string named = LoveLanguages.Match(one); if (named != null) Fields.In(Fields.Love, Fields.Yours, named, 60); },
                    "A love language of yours", delegate(string one) { return Fields.Interactions(Fields.Love, Fields.Yours, one); }, LoveNames(),
                    delegate(string one, double level) { string named = LoveLanguages.Match(one); if (named != null) Fields.Set(Fields.Love, Fields.Yours, named, level); });
                if (!OwnSet.Shown || OwnSet.YoursTitle.Length == 0) return;
                List<string> theirSet = new List<string>();
                List<Topics.Topic> theirLevels = new List<Topics.Topic>();
                foreach (KeyValuePair<string, double> one in OwnSet.Hers(OwnSet.Yours)) { theirSet.Add(one.Key); theirLevels.Add(new Topics.Topic { Name = one.Key, Heading = Percent(one.Value) }); }
                y += 10;
                TopicFold(OwnSet.YoursTitle, theirSet, theirLevels, wide, ref y, delegate(string one) { OwnSet.Remove(OwnSet.Yours, one); }, delegate(string one) { OwnSet.Add(OwnSet.Yours, one); }, "One of yours", delegate(string one) { return OwnSet.Interactions(OwnSet.Yours, one); }, OwnSet.Items, delegate(string one, double level) { OwnSet.Set(OwnSet.Yours, one, level); });
            }

            // A typed topic as her list spells it, when it is one of hers.
            private static string Canon(string typed)
            {
                Topics.Topic known = Topics.All().Find(t => string.Equals(t.Name, typed, StringComparison.OrdinalIgnoreCase));
                return known != null ? known.Name : typed;
            }

            private static string[] TopicNames() { return Topics.All().ConvertAll(t => t.Name).ToArray(); }

            private static string[] LoveNames()
            {
                string[] names = new string[LoveLanguages.Five.GetLength(0)];
                for (int i = 0; i < names.Length; i++) names[i] = LoveLanguages.Five[i, 0];
                return names;
            }

            private static string Percent(double level) { return level < 0 ? "" : Math.Round(level).ToString(CultureInfo.InvariantCulture) + "%"; }

            // The words a personality's file says she likes hearing (worth above nought), less any taken out, and any added.
            private static List<string> PetNames(string who)
            {
                List<string> names = new List<string>();
                List<KeyValuePair<string, string>> liked = Find(Sections(TextOf(who)), Personalities.WordsHeading);
                if (liked != null)
                    foreach (KeyValuePair<string, string> one in liked)
                    {
                        double worth;
                        if (one.Value != null && double.TryParse(one.Value.TrimStart('+'), NumberStyles.Float, CultureInfo.InvariantCulture, out worth) && worth > 0
                            && !Fields.IsOut(Fields.Word, who, one.Key.ToLowerInvariant()) && !names.Contains(one.Key.ToLowerInvariant())) names.Add(one.Key.ToLowerInvariant());
                    }
                foreach (string one in Fields.Added(Fields.Word, who)) if (!names.Contains(one)) names.Add(one);
                return names;
            }

            // Every personality's liked words, for the add row to fill in.
            private static string[] AllPetNames()
            {
                List<string> all = new List<string>();
                foreach (string named in Personalities.Names)
                {
                    if (named == Personalities.Stock || named == Personalities.Dynamic) continue;
                    List<KeyValuePair<string, string>> liked = Find(Sections(TextOf(named)), Personalities.WordsHeading);
                    if (liked != null) foreach (KeyValuePair<string, string> one in liked) if (!all.Contains(one.Key.ToLowerInvariant())) all.Add(one.Key.ToLowerInvariant());
                }
                return all.ToArray();
            }

            private static string TextOf(string named)
            {
                try
                {
                    string path = Personalities.PathOf(named);
                    return path != null && File.Exists(path) ? FileSlots.ReadText(path) : Personalities.Written(named);
                }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); return ""; }
            }

            // Each name with its % where the fold's middle column reads it.
            private static List<Topics.Topic> Pct(List<string> names, Func<string, double> level)
            {
                List<Topics.Topic> made = new List<Topics.Topic>();
                foreach (string one in names) made.Add(new Topics.Topic { Name = one, Heading = Percent(level(one)) });
                return made;
            }

            // With counts: its % and what was done for it this week stand in its middle column, side by side.
            private void TopicFold(string title, List<string> names, List<Topics.Topic> all, int wide, ref int y, Action<string> remove, Action<string> add, string cue, Func<string, int> counts = null, string[] suggest = null, Action<string, double> set = null)
            {
                FoldHead head = new FoldHead { Text = title + "  (" + names.Count + ")", Font = Type(9.5f * Zoom, true), Bounds = new Rectangle(0, Z(y), Z(wide), Z(25)), Open = true };
                setBox.Controls.Add(head);
                y += 30;
                foreach (string name in names)
                {
                    string one = name;
                    Topics.Topic found = all.Find(t => string.Equals(t.Name, one, StringComparison.OrdinalIgnoreCase));
                    rows.Add(one);
                    setBox.Controls.Add(new Note { Text = one, Font = Font, ForeColor = Rose, BackColor = Ground, AutoSize = false, UseMnemonic = false, Bounds = new Rectangle(Z(2), Z(y + 3), Z(176), Z(18)), Cuts = true });
                    int middle = Math.Max(40, wide - 88 - 8 - 186);
                    setBox.Controls.Add(new Note { Text = found == null ? "" : found.Heading, Font = Font, ForeColor = Cyan, BackColor = Ground, AutoSize = false, UseMnemonic = false, TextAlign = ContentAlignment.MiddleLeft, Bounds = new Rectangle(Z(186), Z(y + 3), Z(counts == null ? middle : 56), Z(18)), Cuts = true });
                    if (counts != null)
                        setBox.Controls.Add(new Note { Text = counts(one) + " this week", Font = Font, ForeColor = Rose, BackColor = Ground, AutoSize = false, UseMnemonic = false, TextAlign = ContentAlignment.MiddleLeft, Bounds = new Rectangle(Z(186 + 64), Z(y + 3), Z(Math.Max(40, middle - 64)), Z(18)), Cuts = true });
                    Key off = new Key { Text = "Remove", Font = Type(9f * Zoom, true), Bounds = new Rectangle(Z(wide - 88), Z(y), Z(88), Z(22)) };
                    off.AccessibleName = "Remove " + one + " from " + title.ToLowerInvariant();
                    off.Click += delegate { remove(one); Fill(); };
                    setBox.Controls.Add(off);
                    y += 28;
                }
                topicFolds.Add(title + (add != null ? " + add" : ""));
                if (add == null) return;
                // Her add row, as Her memory has it: a box and an Add key.
                Entry adding = new Entry { Bounds = new Rectangle(Z(2), Z(y), Z(wide - 88 - 10), Z(24)), Cue = cue };
                adding.Box.AccessibleName = cue;
                // The names it knows fill in as they type, the rest marked; typing on or Backspace keeps their own words.
                if (suggest != null && suggest.Length > 0)
                {
                    adding.Box.AutoCompleteCustomSource.AddRange(suggest);
                    adding.Box.AutoCompleteSource = AutoCompleteSource.CustomSource;
                    adding.Box.AutoCompleteMode = AutoCompleteMode.Append;
                }
                Key plus = new Key { Text = "Add", Font = Type(9f * Zoom, true), Bounds = new Rectangle(Z(wide - 88), Z(y), Z(88), Z(24)) };
                Action adds = delegate
                {
                    string words = System.Text.RegularExpressions.Regex.Replace(adding.Box.Text ?? "", @"\s+", " ").Trim();
                    if (words.Length == 0) return;
                    // A % after the name sets how far into it: "Concerts 80%".
                    double level = -1;
                    System.Text.RegularExpressions.Match part = System.Text.RegularExpressions.Regex.Match(words, @"^(.+?)\s+(\d{1,3})\s*%?$");
                    if (set != null && part.Success) { words = part.Groups[1].Value.Trim(); level = Math.Min(100, int.Parse(part.Groups[2].Value, CultureInfo.InvariantCulture)); }
                    Topics.Topic known = all.Find(t => string.Equals(t.Name, words, StringComparison.OrdinalIgnoreCase));
                    add(known != null ? known.Name : words);
                    if (level >= 0) set(known != null ? known.Name : words, level);
                    Fill();
                };
                plus.Click += delegate { adds(); };
                adding.Box.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; adds(); } };
                setBox.Controls.Add(adding);
                setBox.Controls.Add(plus);
                y += 30;
            }

            private int Z(int value) { return (int)Math.Round(value * Zoom); }

            private Rectangle Box(int x, int y, int width, int height) { return new Rectangle(Z(x), Z(y), Z(width), Z(height)); }

            // A box that scrolls on its own, her slim bar inside its frame.
            private void Boxed(ScrollPanel panel, SlimBar bar, Rectangle bounds)
            {
                panel.BackColor = Ground;
                panel.AutoScroll = true;
                panel.Bounds = new Rectangle(bounds.X, bounds.Y, bounds.Width - Z(10), bounds.Height);
                Controls.Add(panel);
                bar.Bounds = new Rectangle(panel.Right + Z(3), panel.Top + Z(2), Z(SlimBar.Thin), panel.Height - Z(4));
                bar.BackColor = Ground;
                bar.Watch(panel);
                panel.Scrolled += delegate { bar.Invalidate(); };
                Controls.Add(bar);
                bar.BringToFront();
            }

            private Label Caption(string text, int x, int y, Font font, Color colour, Control into)
            {
                Label label = new Note { Text = text, Font = font, ForeColor = colour, BackColor = Ground, AutoSize = true, Location = new Point(Z(x), Z(y)) };
                label.UseMnemonic = false;
                if (colour == Rose) ((Note)label).Flush = true;
                into.Controls.Add(label);
                return label;
            }

            private Key Make(string text, int x, int y, int width, int height, EventHandler click)
            {
                Key made = new Key { Text = text, Font = Type(9f * Zoom, true), Bounds = Box(x, y, width, height) };
                made.Click += click;
                Controls.Add(made);
                return made;
            }

            // Her baseline, Dynamic, then every personality and every mood there are, as her own lists name them. Moods have
            // nothing on the Topics page, so it lists the rest.
            private void BuildWho()
            {
                whoRows.Clear();
                while (whoBox.Controls.Count > 0) whoBox.Controls[0].Dispose();
                int y = 0;
                AddWho("Her baseline", "", false, ref y);
                AddWho("Dynamic", Personalities.Dynamic, false, ref y);
                Divider("PERSONALITIES", ref y);
                foreach (string named in Personalities.Names)
                    if (named != Personalities.Stock && named != Personalities.Dynamic) AddWho(named, named, false, ref y);
                if (topicsShown) return;
                Divider("MOODS", ref y);
                foreach (string named in Dynamics.Allowed) AddWho(named, named, true, ref y);
            }

            private void AddWho(string shown, string named, bool mood, ref int y)
            {
                WhoRow row = new WhoRow { Text = shown, Font = Font, Mood = mood, Tag = named, Bounds = new Rectangle(0, Z(y), Z(Picks - 24), Z(22)) };
                row.AccessibleName = shown + ": its settings on the right";
                row.Tick += delegate { Pick(named); };
                row.Only += delegate { Pick(named); };
                whoBox.Controls.Add(row);
                whoRows.Add(row);
                y += 24;
            }

            private void Divider(string text, ref int y)
            {
                y += 6;
                whoBox.Controls.Add(new Note { Text = text, Font = Look.Mono(8f * Zoom, FontStyle.Regular, GraphicsUnit.Point), ForeColor = Faint, BackColor = Ground, AutoSize = false, Bounds = new Rectangle(Z(2), Z(y), Z(Picks - 30), Z(14)) });
                y += 16;
            }

            // The right side for one row of the list: "" for her baseline.
            internal void Pick(string named)
            {
                picked = named ?? "";
                pickedMood = false;
                foreach (WhoRow one in whoRows)
                {
                    one.Ticked = string.Equals((string)one.Tag, picked, StringComparison.OrdinalIgnoreCase);
                    if (one.Ticked) pickedMood = one.Mood;
                    one.Invalidate();
                }
                Fill();
            }

            // "## heading" sections of a file, each line kept: "key: value", or a plain item with no value.
            private static List<KeyValuePair<string, List<KeyValuePair<string, string>>>> Sections(string text)
            {
                List<KeyValuePair<string, List<KeyValuePair<string, string>>>> found = new List<KeyValuePair<string, List<KeyValuePair<string, string>>>>();
                List<KeyValuePair<string, string>> under = null;
                foreach (string raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("## ", StringComparison.Ordinal))
                    {
                        // A heading met again (her stock, then a mod's) is the same section, a later value over an earlier one.
                        string heading = line.Substring(3).Trim();
                        KeyValuePair<string, List<KeyValuePair<string, string>>> had = found.Find(one => string.Equals(one.Key, heading, StringComparison.OrdinalIgnoreCase));
                        if (had.Key != null) { under = had.Value; continue; }
                        under = new List<KeyValuePair<string, string>>();
                        found.Add(new KeyValuePair<string, List<KeyValuePair<string, string>>>(heading, under));
                        continue;
                    }
                    if (under == null || line.Length == 0 || line.StartsWith(">", StringComparison.Ordinal) || line.StartsWith("#", StringComparison.Ordinal)) continue;
                    int colon = line.LastIndexOf(':');
                    KeyValuePair<string, string> item = colon > 0 ? new KeyValuePair<string, string>(line.Substring(0, colon).Trim(), line.Substring(colon + 1).Trim()) : new KeyValuePair<string, string>(line, null);
                    int same = item.Value == null ? -1 : under.FindIndex(one => one.Value != null && string.Equals(one.Key, item.Key, StringComparison.OrdinalIgnoreCase));
                    if (same >= 0) under[same] = item; else under.Add(item);
                }
                return found;
            }

            private string FileText()
            {
                try
                {
                    // As she reads it: her words with the side's stock settings, the mods' over them; their own is shown on top.
                    if (picked == Personalities.Dynamic)
                        return Dynamics.Kept != null && File.Exists(Dynamics.Kept) ? FileSlots.ReadText(Dynamics.Kept) : Dynamics.Settings;
                    if (pickedMood)
                    {
                        string mood = Dynamics.PathOf(picked);
                        return mood != null && File.Exists(mood) ? FileSlots.ReadText(mood) : Dynamics.MoodText(picked);
                    }
                    string path = Personalities.PathOf(picked);
                    return path != null && File.Exists(path) ? FileSlots.ReadText(path) : Personalities.Written(picked);
                }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); return ""; }
            }

#if CHECKS
            // Every zone of every personality and mood on this side, each setting with its value, and whether the page
            // showed it before settings-only and shows it now, as a list to look over before anything goes.
            internal string Zones()
            {
                System.Text.StringBuilder said = new System.Text.StringBuilder();
                List<string> who = new List<string> { "" };
                foreach (string named in Personalities.Names) if (named != Personalities.Stock && named != Personalities.Dynamic) who.Add(named);
                int people = who.Count;
                who.AddRange(Dynamics.Allowed);
                for (int at = 0; at < who.Count; at++)
                {
                    picked = who[at];
                    pickedMood = at >= people;
                    List<KeyValuePair<string, List<KeyValuePair<string, string>>>> sections = Sections(FileText());
                    said.Append("\n### ").Append(picked.Length == 0 ? "Her baseline" : picked).Append(pickedMood ? " (mood)" : "").Append("\n\n");
                    foreach (KeyValuePair<string, List<KeyValuePair<string, string>>> part in sections)
                    {
                        bool before = part.Key == "How she reacts" || part.Key == "What she likes" || WantedBefore(part.Value);
                        bool now = part.Key == "How she reacts" || part.Key == "What she likes" || ((part.Key == Personalities.NeedsHeading || part.Key == Personalities.SwitchHeading) && Wanted(part.Value));
                        List<KeyValuePair<string, string>> valued = part.Value.FindAll(one => one.Value != null);
                        int words = part.Value.Count - valued.Count;
                        if (!before && !now) continue;
                        said.Append("1. ").Append(part.Key).Append(": ").Append(before ? "shown" : "not shown").Append(" before, ").Append(now ? "shown" : "not shown").Append(" now");
                        if (words > 0) said.Append("; ").Append(words).Append(" lines of words with no value");
                        said.Append("\n");
                        foreach (KeyValuePair<string, string> one in valued)
                            said.Append("   1. ").Append(one.Key).Append(": ").Append(one.Value).Append(Settable(one.Value) ? "" : " (words, no slider)").Append("\n");
                    }
                }
                picked = "";
                pickedMood = false;
                return said.ToString();
            }

            // Every personality's and mood's settings as the page shows them, a line each: "name: setting|setting|...".
            internal string SettingsList()
            {
                System.Text.StringBuilder said = new System.Text.StringBuilder();
                List<string> who = new List<string> { "" };
                foreach (string named in Personalities.Names) if (named != Personalities.Stock && named != Personalities.Dynamic) who.Add(named);
                who.AddRange(Dynamics.Allowed);
                foreach (string one in who)
                {
                    Pick(one);
                    said.Append(one.Length == 0 ? "Her baseline" : one).Append(": ").Append(string.Join("|", rows.ToArray())).Append("\n");
                }
                Pick("");
                return said.ToString();
            }

            // The rule the page had before settings-only, kept for that list.
            private static bool WantedBefore(List<KeyValuePair<string, string>> lines)
            {
                if (lines.Count == 0) return false;
                foreach (KeyValuePair<string, string> one in lines) if (one.Value != null) return true;
                return lines.Count <= 40 && lines.TrueForAll(one => one.Key.Split(' ').Length <= 3);
            }

            internal string BarSays()
            {
                bool over = setBox.DisplayRectangle.Height > setBox.ClientSize.Height + 1;
                return over == setBar.Visible ? null : "overflows " + over + ", bar shown " + setBar.Visible;
            }
#endif

            // Everything for the row picked, built again: each section a fold, each setting a row.
            private void Fill()
            {
                setBox.SuspendLayout();
                foreach (Control old in new List<Control>(Each(setBox.Controls))) { setBox.Controls.Remove(old); old.Dispose(); }
                rows.Clear();
                rowValues.Clear();
                setBox.AutoScrollPosition = Point.Empty;
                int y = 0, wide = Panel - 24;
                if (topicsShown)
                {
                    TopicsPage(wide);
                    setBox.ResumeLayout();
                    setBar.Sync();
                    return;
                }
                string text = picked.Length == 0 ? "" : FileText();
                List<KeyValuePair<string, List<KeyValuePair<string, string>>>> sections = Sections(text);
                if (picked == Personalities.Dynamic)
                {
                    foreach (KeyValuePair<string, List<KeyValuePair<string, string>>> part in sections) Section(part.Key, part.Value, false, ref y, wide);
                    List<KeyValuePair<string, string>> loose = new List<KeyValuePair<string, string>>();
                    foreach (KeyValuePair<string, string> top in Sections("## Dynamic\n" + text.Split(new[] { "\n## " }, StringSplitOptions.None)[0])[0].Value) loose.Add(top);
                    if (loose.Count > 0) Section("Dynamic", loose, false, ref y, wide);
                }
                else
                {
                    // Her scenes first, a personality's own on both sides: a long run of her talking (a poem, a story, history, one
                    // of her interests, on her other side a scene); on or off, how often, how long.
                    if (!pickedMood && picked != Personalities.Dynamic)
                        Section(Scenes, new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>(SceneOften, Number(SceneOftenStock)), new KeyValuePair<string, string>(SceneMinutes, Number(SceneMinutesStock)), new KeyValuePair<string, string>(SceneOn, "yes") }, false, ref y, wide);
                    // A mood shows every setting too, 0 (or off) where it changes nothing, so every row sits in the same place.
                    List<KeyValuePair<string, string>> reacts = Ordered(Find(sections, "How she reacts"), ReactsOrder, true, pickedMood ? (Func<string, string>)MoodNothing : null);
                    Section("How she reacts", reacts, false, ref y, wide);
                    Likes(Find(sections, "What she likes"), ref y, wide);
                    // Settings only: her character, pet names, word lists and lines stay in her file.
                    // Every personality has the same folds with the same settings in the same places; a mood only what it changes.
                    string who = picked;
                    // A mood's needs where its file says none are its stock ones, added to the personality's.
                    List<KeyValuePair<string, string>> needs = Ordered(Find(sections, Personalities.NeedsHeading), Personalities.NeedKeys, true, pickedMood ?
                        (Func<string, string>)delegate(string key) { double v = Personalities.StockNeed(who, key, 0); return (v >= 0 ? "+" : "") + Number(v); } :
                        delegate(string key) { return Number(Personalities.StockNeed(who.Length == 0 ? Personalities.Stock : who, key, Personalities.StockNeed(Personalities.Stock, key, 0))); });
                    if (Wanted(needs)) Section(Personalities.NeedsHeading, needs, false, ref y, wide);
                    List<KeyValuePair<string, string>> switches = Ordered(Find(sections, Personalities.SwitchHeading), SwitchKeys, true, delegate { return "no"; });
                    if (Wanted(switches)) Section(Personalities.SwitchHeading, switches, false, ref y, wide);
                }
                setBox.ResumeLayout();
                setBar.Sync();
            }

            // How she reacts, the same for every personality: each setting there is, the file's value or her stock one, and
            // any others the file adds. A mood shows only what it changes.
            private static readonly string[] ReactsOrder =
            {
                "flirt", "cringe", "sulks after", "sulks at upset", "sulk alone", "dodges", "ignored dodge", "ignored either",
                "kind returns", "mean scale", "drift push", "repeat window", "like fades", "like least", "dislike grows",
                "dislike most", "willing pull", "willing slows", "pranks", "prank minutes", "gives up at", "gives up minutes"
            };

            private static readonly Dictionary<string, string> WordsStock = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "dodges", "yes" }, { "ignored either", "no" }, { "repeat window", "20 to 60" }, { "pranks", "no" }
            };

            // What a mood shows for a setting it doesn't change: off for an on or off one, nothing added for the rest.
            private static string MoodNothing(string key)
            {
                string words;
                if (WordsStock.TryGetValue(key, out words)) return words == "yes" || words == "no" ? "no" : "0 to 0";
                return "+0";
            }

            // What a personality can switch on while she is that way, each shown, off where the file says nothing.
            private static readonly string[] SwitchKeys = { "check-ins", "jealous", "watch my screen" };

            private static List<KeyValuePair<string, string>> Ordered(List<KeyValuePair<string, string>> lines, string[] order, bool every, Func<string, string> stock = null)
            {
                Dictionary<string, string> had = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (lines != null) foreach (KeyValuePair<string, string> one in lines) if (one.Value != null) had[one.Key] = one.Value;
                List<KeyValuePair<string, string>> made = new List<KeyValuePair<string, string>>();
                foreach (string key in order)
                {
                    // Giving up is her other side's only.
                    if (key.StartsWith("gives up", StringComparison.Ordinal) && !Brain.SenpaiLive) continue;
                    string value;
                    double[] spec;
                    if (!had.TryGetValue(key, out value) && every)
                    {
                        if (stock != null) value = stock(key);
                        else if (WordsStock.TryGetValue(key, out value)) { }
                        else if (Known.TryGetValue(key, out spec)) value = Number(spec[0]);
                    }
                    if (value != null) made.Add(new KeyValuePair<string, string>(key, value));
                }
                List<string> rest = new List<string>();
                // The same setting by its other spelling counts as that one; the marker that keeps a pack to her other side is
                // never a setting.
                string spelt;
                if (had.TryGetValue("checkins", out spelt)) { had.Remove("checkins"); if (!had.ContainsKey("check-ins")) had["check-ins"] = spelt; }
                foreach (string key in new List<string>(had.Keys)) if (key.EndsWith(" only", StringComparison.OrdinalIgnoreCase) || key == Legacy.OnlyKey) had.Remove(key);
                foreach (string key in had.Keys) if (Array.FindIndex(order, k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) < 0) rest.Add(key);
                foreach (string key in rest) made.Add(new KeyValuePair<string, string>(key, had[key]));
                // Only what a slider or a switch can set, by name from A to Z.
                made.RemoveAll(one => !Settable(one.Value));
                made.Sort(delegate(KeyValuePair<string, string> a, KeyValuePair<string, string> b) { return string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase); });
                return made;
            }

            // One layout for every row: its name from the left, its value right-aligned before the slider column, the slider
            // across that column to the right edge (a switch's OFF and ON at that edge), 28 a row, 22 high.
            private const int ValueAt = 180, SliderAt = 276;

            // About ten ticks on every slider, whatever its steps, so they all look alike.
            private static int Ticks(int steps) { return Math.Max(1, (int)Math.Round((steps - 1) / 10.0)); }

            private static bool Settable(string value)
            {
                double number;
                return value != null && (double.TryParse(value.TrimStart('+'), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
                    || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "no", StringComparison.OrdinalIgnoreCase)
                    || System.Text.RegularExpressions.Regex.IsMatch(value, @"^\d+ to \d+$"));
            }

            // A section worth a fold: settings with values, never words.
            private static bool Wanted(List<KeyValuePair<string, string>> lines)
            {
                return lines.Exists(one => one.Value != null);
            }

            private static List<KeyValuePair<string, string>> Find(List<KeyValuePair<string, List<KeyValuePair<string, string>>>> sections, string heading)
            {
                foreach (KeyValuePair<string, List<KeyValuePair<string, string>>> part in sections)
                    if (string.Equals(part.Key, heading, StringComparison.OrdinalIgnoreCase)) return new List<KeyValuePair<string, string>>(part.Value);
                return null;
            }

            private static string Number(double value) { return value.ToString("0.##", CultureInfo.InvariantCulture); }

            // What she likes, every act there is by its kind: hers, attached, a pack's, a mod's, the pet game's. A file's own
            // number shows where it gives one; otherwise her own, or for a mood nothing added.
            private void Likes(List<KeyValuePair<string, string>> said, ref int y, int wide)
            {
                Dictionary<string, string> named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (said != null) foreach (KeyValuePair<string, string> one in said) if (one.Value != null) named[one.Key] = one.Value;
                List<string> acts = new List<string>(Acts.Names());
                if (Brain.SenpaiLive) foreach (string one in Extended.List("acts")) if (!acts.Contains(one)) acts.Add(one);
                foreach (string one in named.Keys) if (!acts.Exists(act => string.Equals(act, one, StringComparison.OrdinalIgnoreCase))) acts.Add(one);
                Dictionary<string, List<KeyValuePair<string, string>>> kinds = new Dictionary<string, List<KeyValuePair<string, string>>>(StringComparer.Ordinal);
                List<string> order = new List<string>();
                // Her four places, each the acts of its bands on the side in use: one row sets them all.
                places.Clear();
                string[,] bands = { { "Top", Touch.Shoulders, Touch.Upper }, { "Mid", Touch.Middle, null }, { "Lower", Touch.Lower, Touch.Legs }, { "Feet", Touch.Feet, null } };
                List<KeyValuePair<string, string>> placeRows = new List<KeyValuePair<string, string>>();
                for (int place = 0; place < bands.GetLength(0); place++)
                {
                    List<string> its = new List<string>();
                    for (int band = 1; band <= 2; band++)
                    {
                        string zone = bands[place, band], act = string.IsNullOrEmpty(zone) ? null : Acts.AtZone(zone);
                        if (act != null && !its.Contains(act)) its.Add(act);
                    }
                    if (its.Count == 0) continue;
                    places[bands[place, 0]] = its.ToArray();
                    string first;
                    if (!named.TryGetValue(its[0], out first)) first = pickedMood ? null : Number(Acts.LikeOf(its[0]));
                    placeRows.Add(new KeyValuePair<string, string>(bands[place, 0], first));
                    acts.RemoveAll(one => its.Contains(one));
                }
                if (placeRows.Count > 0) { kinds["Touch"] = new List<KeyValuePair<string, string>>(); order.Add("Touch"); }
                foreach (string act in acts)
                {
                    Acts.Act listed = Acts.Find(act);
                    string kind = listed == null ? (Personalities.SenpaiSet(act) ? "Grab" : "More") : Kind(listed.Kind);
                    if (!kinds.ContainsKey(kind)) { kinds[kind] = new List<KeyValuePair<string, string>>(); order.Add(kind); }
                    string value;
                    if (!named.TryGetValue(act, out value)) value = pickedMood ? null : Number(Acts.LikeOf(act));
                    kinds[kind].Add(new KeyValuePair<string, string>(act, value));
                }
                Heading(setBox, "WHAT SHE LIKES >", new Point(Z(0), Z(y)), Type(10f * Zoom, true), White, wide);
                y += 26;
                if (placeRows.Count > 0) kinds["Touch"].InsertRange(Math.Min(kinds["Touch"].Count, 1), placeRows);
                // Each kind's acts by name from A to Z, the same in every personality.
                foreach (string kind in order)
                {
                    kinds[kind].Sort(delegate(KeyValuePair<string, string> a, KeyValuePair<string, string> b) { return string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase); });
                    Section(kind, kinds[kind], true, ref y, wide);
                }
            }

            private static string Kind(string kind)
            {
                switch (kind)
                {
                    case "touch": return "Touch";
                    case "grab": return "Pick up";
                    case "words": return "Words";
                    case "gift": return "Gifts";
                    case "fed": case "treat": case "nuggies": case "food": return "Food";
                    default: return "More";
                }
            }

            // One fold: its head, then a row per setting, or its plain items as one line of words.
            // The file's section a row belongs to, as her own layer keeps it: What she likes for every act, else the fold's own.
            private string section = "";
            // A place row and the acts it sets (Top, Mid, Lower, Feet).
            private readonly Dictionary<string, string[]> places = new Dictionary<string, string[]>(StringComparer.Ordinal);

            private void Section(string title, List<KeyValuePair<string, string>> lines, bool likes, ref int y, int wide)
            {
                section = likes ? "What she likes" : title;
                if (lines == null || lines.Count == 0) return;
                string key = title;
                bool open = !folded.Contains(key);
                FoldHead head = new FoldHead { Text = title.ToUpperInvariant() + "  (" + lines.Count + ")", Font = Type(9.5f * Zoom, true), Bounds = new Rectangle(0, Z(y), Z(wide), Z(25)), Open = open };
                head.Click += delegate { if (!folded.Remove(key)) folded.Add(key); Fill(); };
                setBox.Controls.Add(head);
                y += 30;
                if (!open) return;
                // Scenes alone carries a line under its fold saying what a scene is.
                if (title == Scenes)
                {
                    Caption(SceneNote, 2, y, Small, Faint, setBox);
                    y += 20;
                }
                List<string> items = new List<string>();
                foreach (KeyValuePair<string, string> one in lines)
                {
                    if (one.Value == null && !likes) continue;
                    Row(one.Key, one.Value, likes, ref y, wide);
                }
                if (items.Count > 0)
                {
                    Label all = Caption(string.Join(", ", items.ToArray()), 2, y, Font, Cyan, setBox);
                    all.AutoSize = false;
                    all.Size = TextRenderer.MeasureText(all.Text, all.Font, new Size(Z(wide - 4), 0), TextFormatFlags.WordBreak);
                    y += (int)Math.Ceiling(all.Height / Zoom) + 8;
                }
                y += 4;
            }

            // A setting as a row: its name, and a slider across its range, or its words where it isn't a number.
            private void Row(string name, string value, bool likes, ref int y, int wide)
            {
                // Hers as you changed it, where you did: shown in her rose, kept over the file.
                string[] targets;
                if (!likes || !places.TryGetValue(name, out targets)) targets = new[] { name };
                string yours = Personalities.OwnValue(picked, section, targets[0]);
                // A key a mod sets shows the mod's value, which is the one she reads.
                Func<string, string, HashSet<string>> modded = Personalities.ModKeys;
                if (yours != null && modded != null && modded(PickedPath(), section).Contains(targets[0])) yours = null;
                if (yours != null) value = yours;
                string kept = section;
                rows.Add(name);
                rowValues.Add(name + "=" + value);
                // On or off: a switch, its name drawn by it, as every on or off setting has.
                if (!likes && (string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "no", StringComparison.OrdinalIgnoreCase)))
                {
                    Toggle flip = new Toggle { Text = Shown(name), Font = Font, Checked = string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase), Bounds = new Rectangle(Z(2), Z(y), Z(wide - 2), Z(22)) };
                    flip.CheckedChanged += delegate { foreach (string key in targets) owner.capri.SetYourOwn(picked, kept, key, flip.Checked ? "yes" : "no"); };
                    setBox.Controls.Add(flip);
                    y += 28;
                    return;
                }
                // A span, "20 to 60": a slider for each end, kept as one.
                System.Text.RegularExpressions.Match span = System.Text.RegularExpressions.Regex.Match(value ?? "", @"^(\d+) to (\d+)$");
                if (!likes && span.Success)
                {
                    string[] ends = { span.Groups[1].Value, span.Groups[2].Value };
                    for (int end = 0; end < 2; end++)
                    {
                        int which = end;
                        int least = pickedMood ? 0 : 5;
                        string[] marks = new string[36];
                        for (int i = 0; i < marks.Length; i++) marks[i] = Number(least + 5 * i);
                        int at0 = Math.Max(0, Math.Min(marks.Length - 1, (int.Parse(ends[end], CultureInfo.InvariantCulture) - least) / 5));
                        Caption(Shown(name) + (end == 0 ? " from" : " to"), 2, y + 3, Font, Rose, setBox);
                        Label endShown = new Note { Text = marks[at0], Font = Font, ForeColor = yours != null ? Rose : Cyan, BackColor = Ground, AutoSize = false, TextAlign = ContentAlignment.MiddleRight, Bounds = new Rectangle(Z(ValueAt), Z(y), Z(SliderAt - ValueAt - 10), Z(22)) };
                        endShown.UseMnemonic = false;
                        setBox.Controls.Add(endShown);
                        Slider endBar = new Slider(marks) { Font = Font, Bounds = new Rectangle(Z(SliderAt), Z(y), Z(wide - SliderAt), Z(22)), Value = at0, Shown = "", TickEvery = Ticks(marks.Length) };
                        endBar.Changed += delegate
                        {
                            ends[which] = marks[endBar.Value];
                            endShown.Text = ends[which];
                            endShown.ForeColor = Rose;
                            endBar.Shown = "";
                            int low = int.Parse(ends[0], CultureInfo.InvariantCulture), high = int.Parse(ends[1], CultureInfo.InvariantCulture);
                            foreach (string key in targets) owner.capri.SetYourOwn(picked, kept, key, Math.Min(low, high) + " to " + Math.Max(low, high));
                        };
                        endBar.AccessibleName = Shown(name) + (end == 0 ? " from" : " to");
                        setBox.Controls.Add(endBar);
                        y += 28;
                    }
                    return;
                }
                Caption(Shown(name), 2, y + 3, Font, Rose, setBox);
                int from = ValueAt;
                string[] steps;
                int at;
                // A yes or no setting, whatever its name: read from its value, so a new one shows as one too.
                bool yes = string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "no", StringComparison.OrdinalIgnoreCase);
                double number;
                bool numeric = double.TryParse((value ?? "").TrimStart('+'), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
                if (likes)
                {
                    steps = new string[11];
                    for (int i = 0; i < 11; i++) steps[i] = (i > 5 ? "+" : "") + (i - 5);
                    at = numeric ? Math.Max(0, Math.Min(10, (int)Math.Round(number) + 5)) : 5;
                }
                else if (yes)
                {
                    steps = new[] { "no", "yes" };
                    at = string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                }
                else if (numeric)
                {
                    double[] spec;
                    if (!Known.TryGetValue(name, out spec)) spec = new double[] { number, Math.Min(0, number), Math.Max(10, Math.Abs(number) * 2), Math.Abs(number) < 2 && number != Math.Round(number) ? 0.05 : 1 };
                    // A mood's signed number adds to the personality's; a plain one replaces it, so only a signed one runs below nought.
                    bool adds = pickedMood && (value.StartsWith("+", StringComparison.Ordinal) || value.StartsWith("-", StringComparison.Ordinal));
                    double low = adds ? -spec[2] : spec[1], high = spec[2], step = spec[3];
                    int count = (int)Math.Round((high - low) / step) + 1;
                    steps = new string[count];
                    for (int i = 0; i < count; i++) steps[i] = (adds && low + i * step > 0 ? "+" : "") + Number(low + i * step);
                    at = Math.Max(0, Math.Min(count - 1, (int)Math.Round((number - low) / step)));
                }
                else
                {
                    // Words with nothing to set: not a row here.
                    rows.Remove(name);
                    return;
                }
                // Its number in a column of its own beside the slider, so the thumb never covers it, the same in every row.
                const int column = SliderAt - ValueAt;
                Label shown = new Note { Text = steps[at], Font = Font, ForeColor = Cyan, BackColor = Ground,
                    AutoSize = false, TextAlign = ContentAlignment.MiddleRight, Bounds = new Rectangle(Z(from), Z(y), Z(column - 10), Z(22)) };
                shown.UseMnemonic = false;
                setBox.Controls.Add(shown);
                Slider bar = new Slider(steps) { Font = Font, Bounds = new Rectangle(Z(from + column), Z(y), Z(wide - from - column), Z(22)), TickEvery = Ticks(steps.Length) };
                bar.Value = at;
                bar.Shown = "";
                if (yours != null) shown.ForeColor = Rose;
                bar.Changed += delegate
                {
                    shown.Text = steps[bar.Value];
                    bar.Shown = "";
                    shown.ForeColor = Rose;
                    foreach (string key in targets) owner.capri.SetYourOwn(picked, kept, key, steps[bar.Value]);
                };
                bar.AccessibleName = Shown(name);
                setBox.Controls.Add(bar);
                y += 28;
            }

            // A line of the one picked, from what it says when petted, in her voice as it is now.
            private void HearPicked()
            {
                List<KeyValuePair<string, string>> pet = picked.Length == 0 || picked == Personalities.Dynamic ? null : Find(Sections(FileText()), "When you pet her");
                string line = pet != null && pet.Count > 0 ? pet[new Random().Next(pet.Count)].Key : null;
                if (line == null) owner.capri.HearVoiceStyle(); else owner.capri.HearLine(line);
            }

            private static string Shown(string name) { return name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name.Substring(1); }

            // The settings rows' names, one a line, and each with its value as shown.
            internal string Rows { get { return string.Join("\n", rows.ToArray()); } }

            // For the checks: the topics page's folds, each with "+ add" when it has an add row.
            private readonly List<string> topicFolds = new List<string>();
            internal string TopicFolds { get { return string.Join(" | ", topicFolds.ToArray()); } }
            private readonly List<string> rowValues = new List<string>();
            internal string RowValues { get { return string.Join("\n", rowValues.ToArray()); } }

            // The file of the row picked, as her reads find it.
            private string PickedPath()
            {
                if (picked == Personalities.Dynamic) return Dynamics.Kept;
                return pickedMood ? Dynamics.PathOf(picked) : Personalities.PathOf(picked);
            }

            private static IEnumerable<Control> Each(Control.ControlCollection controls) { foreach (Control one in controls) yield return one; }

            protected override void OnLoad(EventArgs e)
            {
                base.OnLoad(e);
                Rectangle area = Fits.Area(owner);
                int x = owner.Left + (owner.Width - Width) / 2, y = owner.Top + (int)(48 * ScaleOf(this));
                Location = new Point(Math.Max(area.Left, Math.Min(area.Right - Width, x)), Math.Max(area.Top, Math.Min(area.Bottom - Height, y)));
            }

            protected override void OnSizeChanged(EventArgs e)
            {
                base.OnSizeChanged(e);
                Clip(this, ScaleOf(this));
                Invalidate(true);
            }

            // Her backdrop and edging, and a frame round each of the two boxes in her edge colour.
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                float s = ScaleOf(this);
                Graphics graphics = e.Graphics;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Backdrop(graphics, this, s);
                Edging(graphics, this, s);
                graphics.SmoothingMode = SmoothingMode.None;
                using (Pen edge = new Pen(Neon.Edge))
                    foreach (ScrollPanel box in new[] { whoBox, setBox })
                        graphics.DrawRectangle(edge, box.Left - Z(4), box.Top - Z(4), box.Width + Z(16), box.Height + Z(8));
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { e.Handled = true; Close(); return; }
                base.OnKeyDown(e);
            }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == 0x00A3) { message.Result = IntPtr.Zero; return; }
                base.WndProc(ref message);
                if (message.Msg == 0x0084 && message.Result == new IntPtr(1)) message.Result = new IntPtr(2);
            }
        }
    }
}
