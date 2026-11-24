using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace CapriDesktop
{
    // MODS CONFLICT: two packs that take over the same slots, one pair at a time. Each file has Keep for either pack, and
    // its headings open below it so one can come from each; Keep all takes every slot from one. Done keeps the choice.
    internal sealed partial class Mainframe
    {
        private ConflictSheet conflictSheet;

        // The first pair still to be asked about, or nothing when every contest has been chosen.
        internal bool AskConflicts()
        {
            List<FileSlots.Contest> open = FileSlots.Contests().FindAll(one => !one.Picked);
            if (open.Count == 0) { if (conflictSheet != null && !conflictSheet.IsDisposed) conflictSheet.Close(); return false; }
            string first = open[0].First, second = open[0].Second;
            List<FileSlots.Contest> pair = FileSlots.Contests().FindAll(one => one.First == first && one.Second == second);
            if (conflictSheet != null && !conflictSheet.IsDisposed) conflictSheet.Close();
            conflictSheet = new ConflictSheet(this, zoom, first, second, pair);
            conflictSheet.Show(this);
            conflictSheet.Activate();
            return true;
        }

        internal string ConflictShown { get { return conflictSheet == null || conflictSheet.IsDisposed || !conflictSheet.Visible ? null : conflictSheet.Heading; } }

        // For the checks: keep one pack's slot, or all of one pack's, then Done.
        internal void ConflictKeep(string slot, bool first) { if (conflictSheet != null && !conflictSheet.IsDisposed) conflictSheet.Keep(slot, first); }
        internal void ConflictKeepAll(bool first) { if (conflictSheet != null && !conflictSheet.IsDisposed) conflictSheet.KeepAll(first); }
        internal void ConflictDone() { if (conflictSheet != null && !conflictSheet.IsDisposed) conflictSheet.Done(); }
        internal string ConflictCrowded { get { return conflictSheet == null || conflictSheet.IsDisposed ? null : conflictSheet.Crowded(); } }

        // What a pack holds and what it gave up, for its row in Mods.
        internal static string PackHolds(Mod mod)
        {
            if (mod == null || mod.Code || mod.File.Overrides == null || mod.File.Overrides.Length == 0) return "";
            string name = mod.File.Name ?? mod.File.Id;
            List<FileSlots.Contest> lost = FileSlots.GaveUp(name);
            StringBuilder said = new StringBuilder(" Holds " + FileSlots.HeldBy(name) + (FileSlots.HeldBy(name) == 1 ? " slot" : " slots"));
            if (lost.Count > 0) said.Append("; gave " + lost.Count + " to " + (lost[0].First == name ? lost[0].Second : lost[0].First));
            return said.Append(".").ToString();
        }

        private sealed class ConflictSheet : ModSheet
        {
            internal const int Wide = 640, Tall = 520;
            private const int ListTop = 110, ListTall = 300, ListWide = 578;
            private readonly string first, second;
            private readonly List<FileSlots.Contest> pair;
            private readonly Dictionary<string, bool> keepFirst = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> open = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private readonly ScrollPanel list;
            private readonly SlimBar bar = new SlimBar();
            internal readonly string Heading;

            internal ConflictSheet(Mainframe owner, float zoom, string first, string second, List<FileSlots.Contest> pair) : base(owner, zoom, "Mods conflict", "MODS CONFLICT >", Wide, Tall)
            {
                this.first = first;
                this.second = second;
                this.pair = pair;
                foreach (FileSlots.Contest one in pair) keepFirst[one.Slot] = one.Keeper == first;
                Heading = first + " conflicts with " + second;
                Words(Heading, 28, 52, 584, 22, Type(11f * Zoom, true), Rose);
                Words("Both take over the slots below. Pick which one keeps each; the other keeps the rest of what it brings.", 28, 78, 584, 28, Small(), Faint);
                list = new ScrollPanel { Bounds = Box(28, ListTop, ListWide, ListTall), BackColor = Ground };
                list.Scrolled += delegate { bar.Invalidate(); };
                Controls.Add(list);
                bar.Bounds = new Rectangle(Z(28 + ListWide + 4), Z(ListTop), Z(SlimBar.Thin), Z(ListTall));
                bar.BackColor = Ground;
                bar.Watch(list);
                Controls.Add(bar);
                Key allFirst = Make("Keep all from " + Short(first), 28, 424, 284, 28, delegate { KeepAll(true); });
                allFirst.AccessibleName = "Keep every slot from " + first;
                Key allSecond = Make("Keep all from " + Short(second), 328, 424, 284, 28, delegate { KeepAll(false); });
                allSecond.AccessibleName = "Keep every slot from " + second;
                Key done = Make("Done", 492, 470, 120, 30, delegate { Done(); });
                done.AccessibleName = "Keep these choices";
                Words("Asked again only if either pack changes.", 28, 476, 440, 20, Small(), Faint);
                Fill();
                ResumeLayout(false);
            }

            private static string Short(string name) { return name.Length > 16 ? name.Substring(0, 15) + "." : name; }

            private static string FileOf(string slot)
            {
                int at = slot.IndexOf(" > ", StringComparison.Ordinal);
                return at < 0 ? slot : slot.Substring(0, at);
            }

            // One row a file; a file whose row is opened shows each of its headings under it.
            private void Fill()
            {
                list.SuspendLayout();
                List<Control> old = new List<Control>();
                foreach (Control one in list.Controls) old.Add(one);
                foreach (Control one in old) { list.Controls.Remove(one); one.Dispose(); }
                list.AutoScrollPosition = Point.Empty;
                List<string> files = new List<string>();
                foreach (FileSlots.Contest one in pair) if (!files.Contains(FileOf(one.Slot))) files.Add(FileOf(one.Slot));
                int y = 0;
                foreach (string file in files)
                {
                    List<FileSlots.Contest> here = pair.FindAll(one => FileOf(one.Slot) == file);
                    bool headings = here.Count > 1 || here[0].Slot != file;
                    int firsts = here.FindAll(one => keepFirst[one.Slot]).Count;
                    Row(file, 0, y, firsts == here.Count ? (bool?)true : firsts == 0 ? (bool?)false : null, delegate(bool pick) { foreach (FileSlots.Contest one in here) keepFirst[one.Slot] = pick; Fill(); });
                    if (headings)
                    {
                        string named = file;
                        Key fold = new Key { Text = open.Contains(file) ? "Hide headings" : "Headings", Font = Type(8.5f * Zoom, true), Bounds = new Rectangle(Z(ListWide - 112), Z(y + 30), Z(106), Z(22)) };
                        fold.AccessibleName = (open.Contains(file) ? "Hide the headings of " : "Show the headings of ") + file;
                        fold.Click += delegate { if (!open.Remove(named)) open.Add(named); Fill(); };
                        list.Controls.Add(fold);
                        y += 56;
                    }
                    else y += 34;
                    if (headings && open.Contains(file))
                        foreach (FileSlots.Contest one in here)
                        {
                            string slot = one.Slot;
                            Row(slot.Substring(file.Length).TrimStart(' ', '>'), 18, y, keepFirst[slot], delegate(bool pick) { keepFirst[slot] = pick; Fill(); });
                            y += 32;
                        }
                    y += 4;
                }
                list.ResumeLayout();
                bar.Sync();
            }

            private void Row(string named, int indent, int y, bool? kept, Action<bool> keep)
            {
                Label label = new Note { Text = named, Font = Type(9.5f * Zoom), ForeColor = Rose, BackColor = Ground, AutoEllipsis = true, UseMnemonic = false, Bounds = new Rectangle(Z(indent), Z(y + 3), Z(ListWide - indent - 300), Z(22)) };
                list.Controls.Add(label);
                Key a = new Key { Text = "Keep " + Short(first), Font = Type(8.5f * Zoom, true), Bounds = new Rectangle(Z(ListWide - 292), Z(y), Z(142), Z(26)), Picked = kept == true };
                Key b = new Key { Text = "Keep " + Short(second), Font = Type(8.5f * Zoom, true), Bounds = new Rectangle(Z(ListWide - 146), Z(y), Z(142), Z(26)), Picked = kept == false };
                a.AccessibleName = named + ": keep " + first + (kept == true ? ", chosen" : "");
                b.AccessibleName = named + ": keep " + second + (kept == false ? ", chosen" : "");
                a.Click += delegate { keep(true); };
                b.Click += delegate { keep(false); };
                list.Controls.Add(a);
                list.Controls.Add(b);
            }

            internal void Keep(string slot, bool pickFirst)
            {
                foreach (FileSlots.Contest one in pair)
                    if (string.Equals(one.Slot, slot, StringComparison.OrdinalIgnoreCase) || string.Equals(FileOf(one.Slot), slot, StringComparison.OrdinalIgnoreCase)) keepFirst[one.Slot] = pickFirst;
                Fill();
            }

            internal void KeepAll(bool pickFirst)
            {
                foreach (FileSlots.Contest one in pair) keepFirst[one.Slot] = pickFirst;
                Fill();
            }

            // Every slot's choice kept, her packs laid again, and the next pair asked about if there is one.
            internal void Done()
            {
                foreach (FileSlots.Contest one in pair) FileSlots.Keep(one.Key, keepFirst[one.Slot] ? one.FirstId : one.SecondId);
                Frame.capri.ModList.Reapply();
                Frame.FillMods();
                Close();
                Frame.BeginInvoke((MethodInvoker)delegate { Frame.AskConflicts(); });
            }
        }
    }

    internal sealed partial class Companion
    {
        // A pack pair's choice by its key, kept in her settings as "key<tab>id" lines.
        internal void WirePackPicks()
        {
            FileSlots.PickOf = delegate(string key)
            {
                foreach (string line in (preferences.PackPicks ?? "").Split('\n'))
                {
                    int tab = line.LastIndexOf('\t');
                    if (tab > 0 && line.Substring(0, tab) == key) return line.Substring(tab + 1).Trim();
                }
                return null;
            };
            FileSlots.Keep = delegate(string key, string id)
            {
                List<string> kept = new List<string>();
                foreach (string line in (preferences.PackPicks ?? "").Split('\n'))
                    if (line.Trim().Length > 0 && !line.StartsWith(key + "\t", StringComparison.Ordinal)) kept.Add(line);
                kept.Add(key + "\t" + id);
                preferences.PackPicks = string.Join("\n", kept.ToArray());
                SavePreferences();
            };
        }
    }

#if !CROSS
    internal sealed partial class Companion
    {
        // Two packs on the same slots: the box asks once, a file row taking its headings and one heading kept from each,
        // the choice kept per pair, each pack's row saying what it holds and gave up, asked again when one changes, Keep
        // all, and adds from both counting.
        private void SmokeConflicts(List<string> checks, string output)
        {
            Mods mods = ModList;
            Func<ModFile, string, bool> askWas = mainframe.ModAsk;
            mainframe.ModAsk = delegate { return true; };
            string picksWas = preferences.PackPicks;
            Func<string, string, string, string> write = delegate(string id, string name, string body)
            {
                string file = Path.Combine(output, id + Mods.Extension);
                File.WriteAllText(file, "{\"format\":2,\"id\":\"" + id + "\",\"name\":\"" + name + "\",\"version\":\"1\",\"author\":\"Smoke\",\"description\":\"A test.\"," + body + "}");
                return file;
            };
            Func<string, string> slots = delegate(string word)
            {
                return "\"overrides\":[{\"slot\":\"lines.md > openers warm\",\"text\":\"" + word + " opener.\"},{\"slot\":\"check-ins.md > Water\",\"text\":\"- " + word + " water.\"},"
                    + "{\"slot\":\"check-ins.md > Stretch\",\"text\":\"- " + word + " stretch.\"}],\"adds\":{\"mood\":[{\"act\":\"petted\",\"add\":1}]}";
            };
            try
            {
                preferences.PackPicks = null;
                foreach (Mod old in mods.All.ToArray()) mods.Remove(old);
                if (!mainframe.ImportMod(write("night-owl", "Night Owl", slots("Owl")))) throw new Exception("The first pack was refused: " + mainframe.ModStatusText);
                if (mainframe.ConflictShown != null) throw new Exception("One pack alone was asked about a conflict.");
                string sleepy = slots("Cat").Replace("\"overrides\":[", "\"overrides\":[{\"slot\":\"lines.md > tails warm\",\"text\":\"purr.\"},");
                if (!mainframe.ImportMod(write("sleepy-cat", "Sleepy Cat", sleepy))) throw new Exception("The second pack was refused: " + mainframe.ModStatusText);
                string shown = mainframe.ConflictShown;
                int open = FileSlots.Contests().FindAll(one => !one.Picked).Count;
                bool laterHolds = FileSlots.Holder("lines.md > openers warm") == "Sleepy Cat";
                if (shown != "Night Owl conflicts with Sleepy Cat" || open != 3 || !laterHolds || mainframe.ConflictCrowded != null)
                    throw new Exception("The conflict box didn't come up for the two packs: " + shown + ", " + open + " open, later holds " + laterHolds + ", " + mainframe.ConflictCrowded);
                mainframe.ConflictKeep("lines.md > openers warm", true);
                mainframe.ConflictKeep("check-ins.md", false);
                mainframe.ConflictDone();
                for (int i = 0; i < 10; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
                Mod owl = mods.All.Find(mod => mod.File.Id == "night-owl"), cat = mods.All.Find(mod => mod.File.Id == "sleepy-cat");
                bool kept = FileSlots.Holder("lines.md > openers warm") == "Night Owl" && FileSlots.Holder("check-ins.md > Water") == "Sleepy Cat" && FileSlots.Holder("check-ins.md > Stretch") == "Sleepy Cat"
                    && FileSlots.Holder("lines.md > tails warm") == "Sleepy Cat" && FileSlots.Contests().TrueForAll(one => one.Picked) && mainframe.ConflictShown == null
                    && (LoadPreferences().PackPicks ?? "").Split('\n').Length == 3;
                string owlRow = Mainframe.PackHolds(owl), catRow = Mainframe.PackHolds(cat);
                bool rows = owlRow.Contains("Holds 1 slot; gave 2 to Sleepy Cat.") && catRow.Contains("Holds 3 slots; gave 1 to Night Owl.");
                int terms = Acts.Terms("petted").Count;
                if (!kept || !rows || terms != 2) throw new Exception("The choices didn't hold, the rows didn't say what each gave up, or the adds didn't both count: kept " + kept + ", rows " + owlRow + " / " + catRow + ", terms " + terms);
                mods.Reapply();
                if (!FileSlots.Contests().TrueForAll(one => one.Picked) || mainframe.AskConflicts()) throw new Exception("The same two packs were asked about again.");
                checks.Add("PASS two packs on the same slots: the box asks once, a file row takes its headings and a heading can be kept from each, the choice is kept in her settings per pair, each pack's row says what it holds and gave up, and both packs' mood terms count");

                mods.Remove(owl);
                if (!mainframe.ImportMod(write("night-owl", "Night Owl", slots("Owlet")))) throw new Exception("The changed first pack was refused.");
                // The box names the two in the order her mods are listed.
                string shownAgain = mainframe.ConflictShown;
                bool owlFirst = shownAgain == "Night Owl conflicts with Sleepy Cat";
                bool again = (owlFirst || shownAgain == "Sleepy Cat conflicts with Night Owl") && FileSlots.Contests().Exists(one => !one.Picked);
                mainframe.ConflictKeepAll(owlFirst);
                mainframe.ConflictDone();
                for (int i = 0; i < 10; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
                bool all = FileSlots.Holder("lines.md > openers warm") == "Night Owl" && FileSlots.Holder("check-ins.md > Water") == "Night Owl" && FileSlots.Holder("check-ins.md > Stretch") == "Night Owl";
                if (!again || !all) throw new Exception("A changed pack wasn't asked about again, or Keep all didn't take every slot: " + again + ", " + all + "; shown " + shownAgain + "; " + string.Join(" | ", FileSlots.Contests().ConvertAll(one => one.Slot + " " + one.First + "/" + one.Second + " " + one.Picked + " " + one.Keeper).ToArray()));
                checks.Add("PASS a pack that changes is asked about again, and Keep all takes every contested slot from one pack");
            }
            finally
            {
                foreach (Mod left in mods.All.ToArray()) mods.Remove(left);
                if (mainframe.ConflictShown != null) mainframe.ConflictDone();
                preferences.PackPicks = picksWas;
                SavePreferences();
                mainframe.ModAsk = askWas;
            }
        }
    }
#endif
}
