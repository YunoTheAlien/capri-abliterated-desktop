using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace CapriDesktop
{
    // API 6: a code mod's acts and act maths, its slots and its own switches in Toggles. Everything it adds goes when
    // it stops.
    internal sealed partial class Companion
    {
        private ToolStripMenuItem togglesMenu;
        // Her Screensaver group in Toggles: a screensaver mod's switches and sliders go here, its looks being her rows.
        private ToolStripMenuItem saverMenu;

        // Her switches, off her menu, which the Toggles window is built from.
        internal ToolStripMenuItem TogglesMenu { get { return togglesMenu; } }

        // Each act as it lands, to the mods listening, with what it was worth.
        private void ModsActed(string act, int worth)
        {
            if (!ModsListen(host => host.acted != null)) return;
            Post(delegate { ModsFire(delegate(ModHost host) { if (host.acted != null) host.acted(act, worth); }); });
        }

        // API 6 driven by a code mod: an act defined and heard, an add and a multiplier within limits, a slot, a switch
        // in Toggles, her mood read; all gone with the mod, and refused without the permission.
#if CHECKS
        private void SmokeModActs(List<string> checks, string output)
        {
            Mods mods = ModList;
            Func<ModFile, string, bool> askWas = mainframe.ModAsk;
            mainframe.ModAsk = delegate { return true; };
            Func<string, string, string, string> write = delegate(string id, string permissions, string body)
            {
                string file = System.IO.Path.Combine(output, id + Mods.CodeExtension);
                System.IO.File.WriteAllText(file, "// capri-mod: 1\n// id: " + id + "\n// name: " + id + "\n// version: 1\n// author: Smoke\n// description: A test.\n// requires: api 6\n"
                    + (permissions == null ? "" : "// permissions: " + permissions + "\n") + "using System; using System.IO; using CapriDesktop;\n" + body);
                return file;
            };
            Func<Func<bool>, bool> until = delegate(Func<bool> done)
            {
                for (int i = 0; i < 120 && !done(); i++) { Application.DoEvents(); System.Threading.Thread.Sleep(25); }
                return done();
            };
            try
            {
                foreach (Mod old in mods.All.ToArray()) mods.Remove(old);
                int pettedWas = Personalities.Likes("petted"), thrownWas = Personalities.Likes("thrown");
                string[] openersWas = Sayings.Pool("openers warm");
                // Counted with her Sec: folds made, as the Toggles window makes them.
                SectionsChanged();
                int togglesWas = togglesMenu.DropDownItems.Count;
                string body = "public class M : ICapriMod { ICapriHost c;\n"
                    + "void Mark(string n, string t) { File.WriteAllText(Path.Combine(c.Folder, n), t); }\n"
                    + "public void Start(ICapriHost h) { c = h;\n"
                    + "h.Acts.Define(\"coffee\", \"food\", 2);\n"
                    + "h.Mood.Add(\"Owl warmth\", \"petted\", 1);\n"
                    + "h.Mood.Multiply(\"Half throws\", \"thrown\", 0.5);\n"
                    + "try { h.Mood.Add(\"Too much\", \"petted\", 5); } catch (ArgumentOutOfRangeException) { Mark(\"limit\", \"x\"); }\n"
                    + "h.Slots.Override(\"lines.md > openers warm\", \"Mod opener.\");\n"
                    + "h.Toggles.Add(\"Owl switch\", true, on => Mark(\"toggle\", on.ToString()));\n"
                    + "h.Acts.Happened += (a, w) => { if (a == \"coffee\") Mark(\"acted\", a + \" \" + w); };\n"
                    + "Mark(\"mood\", h.Mood.Willing + \"|\" + h.Mood.Today); }\n"
                    + "public bool Heard(string w) { return false; } public void Stop() { } }";
                if (!mainframe.ImportMod(write("act-hooks", "mood, slots", body))) throw new Exception("An API 6 mod was refused: " + mainframe.ModStatusText);
                string data = System.IO.Path.Combine(mods.HomeOf("act-hooks"), "act-hooks-data");
                Func<string, string> mark = delegate(string n) { string f = System.IO.Path.Combine(data, n); return System.IO.File.Exists(f) ? System.IO.File.ReadAllText(f) : null; };
                until(() => mark("mood") != null && Sayings.Pool("openers warm") != null && Sayings.Pool("openers warm").Length == 1);
                Acts.Act coffee = Acts.Find("coffee");
                int thrownNow = Personalities.Likes("thrown");
                bool added = coffee != null && coffee.Like == 2 && Personalities.Likes("petted") == Math.Min(5, pettedWas + 1)
                    && thrownNow == (int)Math.Round(thrownWas * 0.5, MidpointRounding.AwayFromZero) && mark("limit") == "x" && mark("mood") == Mood.Willing + "|" + (Personalities.Attitude ?? "");
                string[] openers = Sayings.Pool("openers warm");
                bool slot = openers != null && openers.Length == 1 && openers[0] == "Mod opener." && FileSlots.Holder("lines.md > openers warm") == "act-hooks";
                // A mod's switches sit under its name in a Sec: fold of the Toggles window, Uncategorised until it's moved.
                mainframe.OpenToggles();
                mainframe.UnfoldToggles(SecPrefix + UncategorisedName);
                bool switched = (mainframe.TogglesText ?? "").Contains("Owl switch on");
                mainframe.PressToggle("Owl switch");
                switched = switched && until(() => mark("toggle") == "False");
                mainframe.CloseToggles();
                Did("coffee");
                bool heard = until(() => mark("acted") == "coffee " + Worth("coffee"));
                if (!added || !slot || !switched || !heard)
                    throw new Exception("An API 6 mod's parts didn't take hold: acts and mood " + added + ", slot " + slot + ", switch " + switched + ", Happened " + heard);
                checks.Add("PASS API 6: a code mod defines an act and does it with Happened hearing what it was worth, adds a term and a multiplier to her acts within their limits (one past 3 refused), reads how she feels, takes a slot the way a pack does, and puts a switch in Toggles");

                mods.Remove(mods.All.Find(mod => mod.File.Id == "act-hooks"));
                until(() => Sayings.Pool("openers warm") != null && openersWas != null && Sayings.Pool("openers warm").Length == openersWas.Length);
                bool gone = Acts.Find("coffee") == null && Personalities.Likes("petted") == pettedWas && Personalities.Likes("thrown") == thrownWas
                    && Sayings.Pool("openers warm").Length == openersWas.Length && togglesMenu.DropDownItems.Count == togglesWas;
                if (!gone) throw new Exception("Removing an API 6 mod left its act, terms, slot or switch behind.");
                if (mainframe.ImportMod(write("act-nope", null, "public class M : ICapriMod { public void Start(ICapriHost h) { h.Mood.Add(\"x\", \"petted\", 1); } public bool Heard(string w) { return false; } public void Stop() { } }"))
                    && mainframe.ModStatusText.IndexOf("mood permission", StringComparison.Ordinal) < 0)
                    throw new Exception("A mod used the act maths without the mood permission: " + mainframe.ModStatusText);
                checks.Add("PASS API 6: removing the mod takes its act, terms, slot and switch with it, and the act maths needs the mood permission");
            }
            finally
            {
                mainframe.ModAsk = askWas;
                foreach (Mod left in mods.All.ToArray()) mods.Remove(left);
                // Her own code mods it stopped, laid again and running, so no check after it finds them gone.
                ModsBack();
            }
        }

        // Who: a term naming personalities and moods counts only while she is one of them, from a code mod and from a pack;
        // one naming nobody counts for everyone, as before.
        private void SmokeTermWho(List<string> checks)
        {
            List<string> wrong = new List<string>();
            if (!Acts.For(null, "Emo", "") || !Acts.For(new[] { "Emo" }, "emo", "") || Acts.For(new[] { "Emo" }, "Sassy", "") || Acts.For(new[] { "mood sleepy" }, "Sleepy", "")
                || !Acts.For(new[] { "mood sleepy" }, "Emo", "Sleepy") || !Acts.For(new[] { "Sleepy" }, "Emo", "sleepy") || Acts.For(new[] { "Emo", "mood smug" }, "Bratty", "Sleepy"))
                wrong.Add("who matched the wrong personality or mood");
            const string mod = "who-check";
            int pettedWas = Personalities.Likes("petted");
            string under = Personalities.Under;
            try
            {
                Acts.Term(mod, "For her", "petted", 1, 1, new[] { under });
                Acts.Term(mod, "For nobody", "petted", 3, 1, new[] { "nobody-here", "mood nobody-here" });
                Acts.Term(mod, "Halved for nobody", "petted", 0, 0.5, new[] { "nobody-here" });
                int now = Personalities.Likes("petted");
                if (now != Math.Max(-5, Math.Min(5, pettedWas + 1))) wrong.Add("pats were " + now + " with a term for " + under + " and two for nobody, from " + pettedWas);
                if (Acts.Terms("petted", false, "nobody-else", "").Exists(one => one.Key == "For her") || !Acts.Terms("petted").Exists(one => one.Key == "For nobody"))
                    wrong.Add("a term counted for someone it doesn't name, or the full list lost one");
                if (Acts.Multipliers("petted", false, under, "").Count != 0 || Acts.Multipliers("petted").Count != 1) wrong.Add("a multiplier for nobody counted for her");
            }
            finally { Acts.DropMod(mod); }
            if (Personalities.Likes("petted") != pettedWas) wrong.Add("the terms didn't go with the mod");
            ModFile pack = Mods.Read("{\"format\": 2, \"id\": \"who-pack\", \"name\": \"Who pack\", \"adds\": {\"mood\": [{\"act\": \"petted\", \"add\": 1, \"who\": [\" Emo \", \"mood  sleepy\", \"emo\"]}, {\"act\": \"thrown\", \"add\": -1}]}}");
            ModTerm first = pack.Adds.Mood[0], second = pack.Adds.Mood[1];
            if (first.Who == null || first.Who.Length != 2 || first.Who[0] != "Emo" || first.Who[1] != "mood sleepy" || second.Who != null)
                wrong.Add("a pack's who read as " + (first.Who == null ? "none" : string.Join("|", first.Who)) + " and " + (second.Who == null ? "none" : string.Join("|", second.Who)));
            string many = string.Join(", ", Enumerable.Range(0, 30).Select(n => "\"p" + n + "\"").ToArray());
            try { Mods.Read("{\"format\": 2, \"id\": \"who-many\", \"name\": \"Who many\", \"adds\": {\"mood\": [{\"act\": \"petted\", \"add\": 1, \"who\": [" + many + "]}]}}"); wrong.Add("a who of 30 names was read"); }
            catch (ModException) { }
            if (wrong.Count > 0) throw new Exception("Who went wrong: " + string.Join("; ", wrong.ToArray()));
            checks.Add("PASS Who: a mood term or multiplier naming personalities and moods (\"mood sleepy\" a mood only) counts only while she is one of them, one naming nobody for everyone; a pack's who reads tidied, its doubles dropped, 30 names refused; all go with the mod");
        }
#endif

        // mods/templates as a modder copies it: the pack reads with every override one of her slots, and its act, terms,
        // names and overrides land; the switch mod compiles and its switch halves a pat while on. All gone after.
#if CHECKS
        private void SmokeTemplate(List<string> checks, string output)
        {
            string folder = null;
            for (System.IO.DirectoryInfo up = new System.IO.DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); up != null && folder == null; up = up.Parent)
                foreach (string here in new[] { System.IO.Path.Combine(up.FullName, "mods", ModTemplates.FolderName), System.IO.Path.Combine(up.FullName, "CapriSourceFiles", "mods", ModTemplates.FolderName) })
                    if (folder == null && System.IO.Directory.Exists(here)) folder = here;
            if (folder == null) { checks.Add("LIMIT the template pack: mods/templates isn't beside this build, so it wasn't imported"); return; }
            string pack = System.IO.Path.Combine(folder, "night-owl" + Mods.Extension), code = System.IO.Path.Combine(folder, "night-owl-switch" + Mods.CodeExtension);
            Mods mods = ModList;
            Func<ModFile, string, bool> askWas = mainframe.ModAsk;
            mainframe.ModAsk = delegate { return true; };
            Func<Func<bool>, bool> until = delegate(Func<bool> done)
            {
                for (int i = 0; i < 120 && !done(); i++) { Application.DoEvents(); System.Threading.Thread.Sleep(25); }
                return done();
            };
            try
            {
                foreach (Mod old in mods.All.ToArray()) mods.Remove(old);
                ModFile read = Mods.Read(System.IO.File.ReadAllText(pack));
                foreach (ModOverride one in read.Overrides)
                    if (FileSlots.Find(one.Slot) == null) throw new Exception("The template takes over " + one.Slot + ", which isn't one of her slots.");
                foreach (string part in System.IO.Directory.GetFiles(System.IO.Path.Combine(folder, "parts"), "*.md"))
                    if (System.IO.File.ReadAllText(part).IndexOf("```", StringComparison.Ordinal) < 0) throw new Exception(System.IO.Path.GetFileName(part) + " has nothing to copy.");
                string[] openersWas = Sayings.Pool("openers warm");
                int pettedWas = Personalities.Likes("petted");
                if (!mainframe.ImportMod(pack)) throw new Exception("The template pack was refused: " + mainframe.ModStatusText);
                string[] openers = Sayings.Pool("openers warm");
                Acts.Act coffee = Acts.Find("coffee");
                bool laid = openers != null && openers.Length == 5 && openers[0] == "Mm. Hi." && FileSlots.Holder("chess.md") == read.Name && FileSlots.Holder("personalities/Sassy.md > How she reacts") == read.Name;
                bool added = coffee != null && coffee.Like == 3 && coffee.Kind == "food" && Personalities.Likes("petted") == Math.Max(-5, pettedWas - 1) && Array.IndexOf(mods.WakeNames, "hey owl") >= 0;
                if (!laid || !added) throw new Exception("The template pack's parts didn't take hold: overrides " + laid + " (openers " + (openers == null ? "none" : openers.Length + ", " + openers[0]) + ", chess.md " + FileSlots.Holder("chess.md") + ", Sassy's numbers " + FileSlots.Holder("personalities/Sassy.md > How she reacts") + "), act, term and names " + added);
                checks.Add("PASS the template pack, mods/templates/night-owl.capri-mod: every override is one of her slots and lays (a heading, a whole file and Sassy's numbers), its act, mood terms and wake names land, and each part's own file has something to copy");

                SectionsChanged();
                int pettedPack = Personalities.Likes("petted"), togglesWas = togglesMenu.DropDownItems.Count;
                if (!mainframe.ImportMod(code)) throw new Exception("The template's switch mod was refused: " + mainframe.ModStatusText);
                ToolStripMenuItem toggle = null;
                // A mod's switches sit under its name in a Sec: fold of Toggles.
                until(delegate
                {
                    foreach (ToolStripItem one in togglesMenu.DropDownItems)
                    {
                        ToolStripMenuItem group = one as ToolStripMenuItem;
                        if (group == null) continue;
                        if (group.Text == "Sleepy pats") toggle = group;
                        foreach (ToolStripItem inside in group.DropDownItems)
                        {
                            if (inside.Text == "Sleepy pats") toggle = inside as ToolStripMenuItem;
                            ToolStripMenuItem part = inside as ToolStripMenuItem;
                            if (part != null && Equals(part.Tag, Mainframe.GroupTag)) foreach (ToolStripItem deeper in part.DropDownItems) if (deeper.Text == "Sleepy pats") toggle = deeper as ToolStripMenuItem;
                        }
                    }
                    return toggle != null;
                });
                bool off = toggle != null && !toggle.Checked;
                if (toggle != null) toggle.PerformClick();
                bool halved = until(() => Personalities.Likes("petted") == (int)Math.Round(pettedPack * 0.5, MidpointRounding.AwayFromZero));
                if (toggle != null) toggle.PerformClick();
                bool back = until(() => Personalities.Likes("petted") == pettedPack);
                if (!off || !halved || !back) throw new Exception("The template's switch didn't work: in Toggles and off " + off + ", halves a pat " + halved + ", back when off " + back);
                mods.Remove(mods.All.Find(mod => mod.File.Id == "night-owl-switch"));
                mods.Remove(mods.All.Find(mod => mod.File.Id == read.Id));
                until(() => Sayings.Pool("openers warm") != null && openersWas != null && Sayings.Pool("openers warm").Length == openersWas.Length);
                if (Acts.Find("coffee") != null || Personalities.Likes("petted") != pettedWas || togglesMenu.DropDownItems.Count != togglesWas || FileSlots.Holder("chess.md") != null)
                    throw new Exception("Removing the template left its act, terms, switch or slots behind.");
                checks.Add("PASS the template's switch mod, night-owl-switch.capri-code, compiles and puts Sleepy pats in Toggles, off; on, a pat counts for half, off it counts as before; removing both leaves her as she was");
            }
            finally
            {
                mainframe.ModAsk = askWas;
                foreach (Mod left in mods.All.ToArray()) mods.Remove(left);
                // Her own code mods it stopped, laid again and running, so no check after it finds them gone.
                ModsBack();
            }
        }
#endif

        private sealed partial class ModHost : ICapriActs, ICapriMood, ICapriSlots, ICapriToggles
        {
            internal Action<string, int> acted;
            private readonly List<ToolStripItem> toggles = new List<ToolStripItem>();
#if CHECKS
            // Its Toggles rows, for a try run to show and work each one.
            internal ToolStripItem[] ToggleRows { get { return toggles.ToArray(); } }
#endif
            private readonly List<string> slotsHeld = new List<string>();

            ICapriActs ICapriHost.Acts { get { return this; } }
            ICapriMood ICapriHost.Mood { get { return this; } }
            ICapriSlots ICapriHost.Slots { get { return this; } }
            ICapriToggles ICapriHost.Toggles { get { return this; } }

            void ICapriActs.Define(string act, string kind, int like) { Need("mood"); CapriDesktop.Acts.Define(id, act, kind, like); }

            void ICapriActs.Did(string act)
            {
                Need("mood");
                if (CapriDesktop.Acts.Find(act) == null) throw new ArgumentException(act + " isn't one of her acts.");
                capri.OnWindow(delegate { if (over) return; capri.Did(act); capri.PetGave(act); });
            }

            event Action<string, int> ICapriActs.Happened { add { Need("mood"); acted += value; } remove { acted -= value; } }

            string[] ICapriActs.List { get { Need("mood"); return CapriDesktop.Acts.Names(); } }

            void ICapriMood.Add(string term, string act, int value) { Need("mood"); CapriDesktop.Acts.Term(id, term, act, value, 1); }

            void ICapriMood.Multiply(string term, string act, double value) { Need("mood"); CapriDesktop.Acts.Term(id, term, act, 0, value); }

            void ICapriMood.Add(string term, string act, int value, string[] who) { Need("mood"); CapriDesktop.Acts.Term(id, term, act, value, 1, who); }

            void ICapriMood.Multiply(string term, string act, double value, string[] who) { Need("mood"); CapriDesktop.Acts.Term(id, term, act, 0, value, who); }

            void ICapriMood.Remove(string term) { Need("mood"); CapriDesktop.Acts.Unterm(id, term); }

            int ICapriMood.Warmth { get { Need("mood"); return CapriDesktop.Mood.Warmth; } }
            int ICapriMood.Upset { get { Need("mood"); return CapriDesktop.Mood.Upset; } }
            int ICapriMood.Willing { get { Need("mood"); return CapriDesktop.Mood.Willing; } }
            string ICapriMood.Today { get { Need("mood"); return Personalities.Attitude ?? ""; } }

            // Held the way a pack holds it: on its Mods row and through the conflict box. False when it isn't a slot.
            bool ICapriSlots.Override(string slot, string text)
            {
                Need("slots");
                string named = (slot ?? "").Trim();
                int at = named.IndexOf(" > ", StringComparison.Ordinal);
                if (named.Length == 0 || !FileSlots.Knows(at < 0 ? named : named.Substring(0, at))) return false;
                if ((text ?? "").Length > Mods.LongestOverride) throw new ArgumentException("An override is " + Mods.LongestOverride + " characters at most.");
                string said = text ?? "";
                capri.OnWindow(delegate
                {
                    Mod mine = Mine();
                    if (over || mine == null) return;
                    List<ModOverride> now = new List<ModOverride>(mine.File.Overrides ?? new ModOverride[0]);
                    now.RemoveAll(one => string.Equals(one.Slot, named, StringComparison.OrdinalIgnoreCase));
                    now.Add(new ModOverride { Slot = named, Text = said, Whole = at < 0 });
                    mine.File.Overrides = now.ToArray();
                    if (!slotsHeld.Contains(named)) slotsHeld.Add(named);
                    Relaid(true);
                });
                return true;
            }

            void ICapriSlots.Release(string slot)
            {
                Need("slots");
                string named = (slot ?? "").Trim();
                capri.OnWindow(delegate
                {
                    Mod mine = Mine();
                    if (mine == null || mine.File.Overrides == null) return;
                    List<ModOverride> now = new List<ModOverride>(mine.File.Overrides);
                    if (now.RemoveAll(one => string.Equals(one.Slot, named, StringComparison.OrdinalIgnoreCase)) == 0) return;
                    mine.File.Overrides = now.ToArray();
                    slotsHeld.Remove(named);
                    Relaid(false);
                });
            }

            string ICapriSlots.Holder(string slot) { Need("slots"); return FileSlots.Holder((slot ?? "").Trim()); }

            void ICapriToggles.Add(string label, bool on, Action<bool> changed) { Switch(null, label, on, changed); }

            void ICapriToggles.Add(string item, string label, bool on, Action<bool> changed) { Switch(item, label, on, changed); }

            void ICapriToggles.AddSlider(string label, string[] steps, int at, Action<int> changed) { Slider(null, label, steps, at, changed); }

            void ICapriToggles.AddSlider(string item, string label, string[] steps, int at, Action<int> changed) { Slider(item, label, steps, at, changed); }

            // A switch: with no item, one of the mod's own rows in its group (an item later settings can belong to); with an
            // item, a setting in that item's side area.
            private void Switch(string item, string label, bool on, Action<bool> changed)
            {
                string named = Clean(label, 40), under = item == null ? null : Clean(item, 40);
                if (named.Length == 0 || changed == null) throw new ArgumentException("A switch needs a name and what to do.");
                capri.OnWindow(delegate
                {
                    ToolStripItemCollection into = Into(under, false);
                    if (into == null) return;
                    string saved = capri.ModToggle(id, under, named);
                    bool now = saved == null ? on : saved == "on";
                    ToolStripMenuItem row = new ToolStripMenuItem(named) { Checked = now, ToolTipText = name };
                    row.Click += delegate
                    {
                        row.Checked = !row.Checked;
                        capri.SetModToggle(id, under, named, row.Checked ? "on" : "off");
                        try { changed(row.Checked); } catch (Exception error) { Failed(error); }
                    };
                    into.Add(row);
                    toggles.Add(row);
                    if (now != on) try { changed(now); } catch (Exception error) { Failed(error); }
                });
            }

            // A slider: its steps as items tagged so the Toggles window draws them as one slider row. With no item it is one
            // for the whole group, at the top above the items; with an item, in that item's side area.
            private void Slider(string item, string label, string[] steps, int at, Action<int> changed)
            {
                string named = Clean(label, 40), under = item == null ? null : Clean(item, 40);
                List<string> kept = new List<string>();
                if (steps != null) foreach (string step in steps) { string one = Clean(step, 24); if (one.Length > 0) kept.Add(one); }
                if (named.Length == 0 || changed == null || kept.Count < 2 || kept.Count > 21) throw new ArgumentException("A slider needs a name, two to 21 steps and what to do.");
                capri.OnWindow(delegate
                {
                    ToolStripItemCollection into = Into(under, true);
                    if (into == null) return;
                    int start = Math.Max(0, Math.Min(kept.Count - 1, at));
                    string saved = capri.ModToggle(id, under, named);
                    int now = saved != null && kept.IndexOf(saved) >= 0 ? kept.IndexOf(saved) : start;
                    // Above the group's items, after any setting for the whole group already there.
                    int place = under == null ? Wide(into) : into.Count;
                    List<ToolStripMenuItem> items = new List<ToolStripMenuItem>();
                    for (int i = 0; i < kept.Count; i++)
                    {
                        int step = i;
                        ToolStripMenuItem one = new ToolStripMenuItem(kept[i]) { Checked = i == now, ToolTipText = named, Tag = Mainframe.StepTag + named + ":" + i };
                        one.Click += delegate
                        {
                            foreach (ToolStripMenuItem each in items) each.Checked = each == one;
                            capri.SetModToggle(id, under, named, kept[step]);
                            try { changed(step); } catch (Exception error) { Failed(error); }
                        };
                        items.Add(one);
                        into.Insert(place + i, one);
                        toggles.Add(one);
                    }
                    if (now != start) try { changed(now); } catch (Exception error) { Failed(error); }
                });
            }

            // Where the group's own settings end: after her wait and any slider already at the top.
            private static int Wide(ToolStripItemCollection items)
            {
                int at = 0;
                while (at < items.Count && items[at].Tag is string && (((string)items[at].Tag).StartsWith("after:", StringComparison.Ordinal) || ((string)items[at].Tag).StartsWith("savers:", StringComparison.Ordinal) || ((string)items[at].Tag).StartsWith(Mainframe.StepTag, StringComparison.Ordinal))) at++;
                return at;
            }

            // Set before the mod starts: a screensaver mod's group is her own Screensaver group, and its items her looks.
            internal bool Draws;

            // The group a setting goes in, or the side area of the item it names: one of the mod's own switches, or one of her
            // looks for a screensaver mod. Null, with the mod told, when the item is none of those.
            private ToolStripItemCollection Into(string item, bool slider)
            {
                ToolStripMenuItem group = Group();
                if (group == null) return null;
                if (item == null) return group.DropDownItems;
                foreach (ToolStripItem one in group.DropDownItems)
                {
                    ToolStripMenuItem row = one as ToolStripMenuItem;
                    string tag = row == null ? null : row.Tag as string;
                    if (row == null || (tag != null && (tag.StartsWith("after:", StringComparison.Ordinal) || tag.StartsWith(Mainframe.StepTag, StringComparison.Ordinal)))) continue;
                    if (string.Equals(row.Text, item, StringComparison.OrdinalIgnoreCase) || string.Equals(tag, "look:" + item, StringComparison.OrdinalIgnoreCase)) return row.DropDownItems;
                }
                Failed(new ArgumentException("No item named " + item + " to put " + (slider ? "a slider" : "a switch") + " under: add its switch first."));
                return null;
            }

            // The mod's own group in the Toggles window, made with its first switch or slider; her Screensaver group for a
            // screensaver mod.
            private ToolStripMenuItem fold;

            private ToolStripMenuItem Group()
            {
                if (over || capri.togglesMenu == null) return null;
                if (Draws && capri.saverMenu != null) return capri.saverMenu;
                if (Introduces && capri.introsMenu != null) return capri.introsMenu;
                ToolStripMenuItem titled = capri.FoldTitled(joining);
                if (titled != null) return titled;
                if (fold != null) return fold;
                Mod mine = Mine();
                fold = new ToolStripMenuItem(mine != null && !string.IsNullOrEmpty(mine.File.Name) ? mine.File.Name : name) { Tag = Mainframe.GroupTag };
                toggles.Add(fold);
                capri.ModFold(id, fold, joining);
                return fold;
            }

            // Set before the mod starts: an intro mod's switches join her Intros group.
            internal bool Introduces;
            // The group or section a mod asked to join by its title, before its first switch. API 10.
            private string joining;

            void ICapriToggles.Join(string title)
            {
                if (fold != null) throw new InvalidOperationException("Join before adding a switch: its switches are already in a group of their own.");
                joining = Clean(title ?? "", 40);
            }

            private Mod Mine() { return capri.ModList == null ? null : capri.ModList.All.Find(mod => mod.File.Id == id); }

            // Her packs laid again with this mod's slots, asking about any it now shares.
            private void Relaid(bool ask)
            {
                capri.ModList.Reapply();
                if (capri.mainframe == null) return;
                capri.mainframe.FillMods();
                if (ask) capri.mainframe.AskConflicts();
            }

            // Its acts, terms, slots and switches, gone as it stops.
            private void LetGo()
            {
                CapriDesktop.Acts.DropMod(id);
                acted = null;
                capri.OnWindow(delegate
                {
                    foreach (ToolStripItem one in toggles) { ToolStrip held = one.Owner; if (held != null) held.Items.Remove(one); one.Dispose(); }
                    toggles.Clear();
                    if (fold != null) capri.ModFold(id, null);
                    fold = null;
                    Mod mine = Mine();
                    if (mine != null && slotsHeld.Count > 0 && mine.File.Overrides != null)
                    {
                        mine.File.Overrides = Array.FindAll(mine.File.Overrides, one => !slotsHeld.Contains(one.Slot));
                        slotsHeld.Clear();
                        Relaid(false);
                    }
                });
            }
        }
    }
}
