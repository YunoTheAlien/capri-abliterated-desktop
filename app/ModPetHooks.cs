using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CapriDesktop
{
    // API 9: a pet game as a mod. She keeps feeding and hunger and tells it each feed; it prices her food, fills her
    // Tamagotchi menu and opens its own window. What it adds goes when it stops.
    internal sealed partial class Companion
    {
        // Each feed as it lands, to the mods listening.
        private void ModsFed(string act)
        {
            if (!ModsListen(host => host.fed != null)) return;
            Post(delegate { ModsFire(delegate(ModHost host) { if (host.fed != null) host.fed(act); }); });
        }

        // Her needs moved, to the mods listening.
        private void ModsStats()
        {
            if (!ModsListen(host => host.statsChanged != null)) return;
            Post(delegate { ModsFire(delegate(ModHost host) { if (host.statsChanged != null) host.statsChanged(); }); });
        }

        // Hours since she was fed, time at a desk counting for less; -1 when she never has been.
        private double SinceFed
        {
            get
            {
                double fed = Since(preferences.FedAt);
                return fed < 0 ? -1 : fed * Working.Rate(Working.Pet, Working.Now(SimTime.UtcNow));
            }
        }

        private string HungerWord { get { double fed = SinceFed; return fed >= 6 ? "hungry" : fed >= 3 ? "peckish" : "fed"; } }

        private sealed partial class ModHost : ICapriPetHost
        {
            internal Action<string> fed;
            internal Action statsChanged;
            private ModMenu petMenu;

            ICapriPetHost ICapriHost.Pet { get { return this; } }

            double ICapriPetHost.SinceFed { get { Need("pet"); return capri.FromWindow(() => capri.SinceFed); } }

            string ICapriPetHost.Hunger { get { Need("pet"); return capri.FromWindow(() => capri.HungerWord); } }

            event Action<string> ICapriPetHost.Fed { add { Need("pet"); fed += value; } remove { fed -= value; } }

            int ICapriPetHost.Stat(string need)
            {
                Need("pet");
                if (Array.IndexOf(CapriDesktop.PetStats.Names, need) < 0) throw new ArgumentException(need + " isn't one of her needs.");
                return capri.FromWindow(() => CapriDesktop.PetStats.Level(need));
            }

            event Action ICapriPetHost.StatsChanged { add { Need("pet"); statsChanged += value; } remove { statsChanged -= value; } }

            ICapriMenu ICapriPetHost.Menu
            {
                get
                {
                    Need("pet");
                    if (petMenu != null) return petMenu;
                    ToolStripMenuItem hers = capri.FromWindow(() => capri.tamagotchiItem);
                    if (hers == null) throw new InvalidOperationException("Her Tamagotchi menu isn't made yet.");
                    petMenu = new ModMenu(this, hers);
                    return petMenu;
                }
            }

            int ICapriActs.Worth(string act)
            {
                Need("mood");
                if (CapriDesktop.Acts.Find(act) == null) throw new ArgumentException(act + " isn't one of her acts.");
                return capri.FromWindow(() => capri.Worth(act));
            }

            string ICapriActs.Line(string act, bool liked)
            {
                Need("mood");
                if (CapriDesktop.Acts.Find(act) == null) throw new ArgumentException(act + " isn't one of her acts.");
                return capri.FromWindow(() => capri.GiftLine(act, liked)) ?? "";
            }

            string ICapriActs.Pool(string heading)
            {
                Need("mood");
                string named = Clean(heading, 60);
                return named.Length == 0 ? "" : Sayings.From(named) ?? "";
            }

            void ICapriUi.ShowOptions()
            {
                if (over) return;
                capri.OnWindow(delegate { if (!over) capri.mainframe.OpenModOptions(id); });
            }

            // Its items in her Tamagotchi menu, and its feed listener.
            private void LetGoPet()
            {
                fed = null;
                if (petMenu != null) petMenu.Clear();
            }
        }

        // API 9 by a test mod: its items and a dressed list in her Tamagotchi menu, a price in its colour, a feed heard,
        // her worth and lines read, its window opened; found as her pet, gone with it, refused without the permission.
#if CHECKS
        private void SmokeModPet(List<string> checks, string output)
        {
            Mods mods = ModList;
            Func<ModFile, string, bool> askWas = mainframe.ModAsk;
            mainframe.ModAsk = delegate { return true; };
            long fedWas = preferences.FedAt, treatedWas = preferences.TreatedAt;
            Func<string, string, string, string> write = delegate(string id, string permissions, string body)
            {
                string file = System.IO.Path.Combine(output, id + Mods.CodeExtension);
                System.IO.Directory.CreateDirectory(output);
                System.IO.File.WriteAllText(file, "// capri-mod: 1\n// id: " + id + "\n// name: " + id + "\n// version: 1\n// author: Smoke\n// description: A test.\n// requires: api 9\n"
                    + (permissions == null ? "" : "// permissions: " + permissions + "\n") + "using System; using System.IO; using System.Drawing; using System.Windows.Forms; using CapriDesktop;\n" + body);
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
                // The mod's items: everything in her Tamagotchi menu that isn't one of hers.
                Func<int> modItems = delegate
                {
                    int count = 0;
                    foreach (ToolStripItem one in tamagotchiItem.DropDownItems)
                        if (one != pointsLine && one != pointsRule && one != feedItem) count++;
                    return count;
                };
                string body = "public class M : ICapriMod, ICapriPet { ICapriHost c; int paid;\n"
                    + "void Mark(string n, string t) { File.WriteAllText(Path.Combine(c.Folder, n), t); }\n"
                    + "public string Heading { get { return \"Points: \" + (50 - paid); } } public Color HeadingColour { get { return Color.Gold; } }\n"
                    + "public int Cost(string food) { return food == \"A snack\" ? 10 : 0; } public bool Pay(string food) { paid += Cost(food); return paid <= 50; } public bool CanPay(string food) { return paid + Cost(food) <= 50; } public bool Feeding { get { return true; } }\n"
                    + "public void Start(ICapriHost h) { c = h; ICapriMenu m = h.Pet.Menu; m.Add(\"Pet window\", () => h.Ui.ShowOptions());\n"
                    + "ICapriMenu inner = m.AddMenu(\"Pet gifts\"); ICapriMenuItem one = inner.Add(\"Pet flowers\", () => Mark(\"given\", \"x\")); one.Note = \"100\"; one.Colour = Color.Gold;\n"
                    + "h.Pet.Fed += f => Mark(\"fed\", f + \" \" + h.Pet.Hunger + \" \" + (h.Pet.SinceFed >= 0));\n"
                    + "h.Ui.SetOptions(\"Pet options\", () => new Panel { Size = new Size(200, 40) });\n"
                    + "Mark(\"acts\", h.Acts.Worth(\"flowers\") + \"|\" + (h.Acts.Line(\"flowers\", true).Length > 0) + \"|\" + (h.Acts.Pool(\"gift remembered\").Length > 0)); }\n"
                    + "public bool Heard(string w) { return false; } public void Stop() { } }";
                if (!mainframe.ImportMod(write("pet-hooks", "pet, mood", body))) throw new Exception("An API 9 pet mod was refused: " + mainframe.ModStatusText);
                string data = System.IO.Path.Combine(mods.HomeOf("pet-hooks"), "pet-hooks-data");
                Func<string, string> mark = delegate(string n) { string f = System.IO.Path.Combine(data, n); return System.IO.File.Exists(f) ? System.IO.File.ReadAllText(f) : null; };
                Func<string, ToolStripMenuItem> find = delegate(string text)
                {
                    foreach (ToolStripItem one in tamagotchiItem.DropDownItems) if (one.Text == text) return one as ToolStripMenuItem;
                    return null;
                };
                Func<ToolStripItem, int> order = delegate(ToolStripItem one) { return tamagotchiItem.DropDownItems.IndexOf(one); };
                until(() => mark("acts") != null && find("Pet gifts") != null && find("Pet gifts").DropDownItems.Count == 1);
                ToolStripMenuItem window = find("Pet window"), inner = find("Pet gifts");
                ToolStripMenuItem flowers = inner == null || inner.DropDownItems.Count == 0 ? null : inner.DropDownItems[0] as ToolStripMenuItem;
                ToolStripDropDownMenu innerDrop = inner == null ? null : inner.DropDown as ToolStripDropDownMenu;
                ShowTamagotchi();
                int line = PetItemsAt;
                bool placed = window != null && inner != null && modItems() == 2
                    && order(window) > order(feedItem) && order(inner) == order(window) + 1 && order(inner) < line;
                bool dressed = innerDrop != null && innerDrop.Renderer == menu.Renderer && innerDrop.BackColor == menu.BackColor;
                bool priced = flowers != null && flowers.ShortcutKeyDisplayString == "100" && flowers.ShowShortcutKeys && flowers.ForeColor.ToArgb() == Color.Gold.ToArgb();
                bool acts = mark("acts") == Worth("flowers") + "|True|True";
                ICapriPet pet = mods.Pet;
                bool found = pet != null && pet.Heading == "Points: 50" && pet.Cost("A snack") == 10 && pet.Pay("A snack") && pet.Heading == "Points: 40";
                Feed("snack");
                bool heard = until(() => mark("fed") == "snack fed True");
                if (window != null) window.PerformClick();
                bool opened = until(() => mainframe.ModOptionsTitle == "Pet options");
                mainframe.CloseModOptions();
                if (!placed || !dressed || !priced || !acts || !found || !heard || !opened)
                    throw new Exception("An API 9 pet mod's parts didn't take hold: in her Tamagotchi menu " + placed + ", its list dressed " + dressed + ", priced " + priced
                        + ", acts " + acts + " (" + mark("acts") + "), found as her pet " + found + ", a feed heard " + heard + " (" + mark("fed") + "), its window " + opened);
                checks.Add("PASS API 9: a pet mod puts items in her Tamagotchi menu under Feed her, with a list dressed as hers when made and a price on the right in its colour, hears a feed with her hunger, reads her worth and line for a gift and a pool's line, opens its own window from its item, and is found as her pet with its price and pay");

                mods.Remove(mods.All.Find(mod => mod.File.Id == "pet-hooks"));
                bool gone = until(() => modItems() == 0 && find("Pet window") == null) && mods.Pet == null;
                if (!gone) throw new Exception("Removing an API 9 pet mod left its items in her Tamagotchi menu, or it was still her pet.");
                if (mainframe.ImportMod(write("pet-nope", null, "public class M : ICapriMod { public void Start(ICapriHost h) { h.Pet.Menu.Add(\"x\", null); } public bool Heard(string w) { return false; } public void Stop() { } }"))
                    && mainframe.ModStatusText.IndexOf("pet permission", StringComparison.Ordinal) < 0)
                    throw new Exception("A mod reached her Tamagotchi menu without the pet permission: " + mainframe.ModStatusText);
                checks.Add("PASS API 9: removing the pet mod takes its items with it and it is no longer her pet; her Tamagotchi menu needs the pet permission");
            }
            finally
            {
                mainframe.ModAsk = askWas;
                foreach (Mod left in mods.All.ToArray()) mods.Remove(left);
                preferences.FedAt = fedWas;
                preferences.TreatedAt = treatedWas;
                SavePreferences();
                ShowFeedMenu();
                // Her own code mods it stopped, laid again and running, so no check after it finds them gone.
                ModsBack();
            }
        }
#endif

        // mods/capri-tamagotchi as it ships: laid in off once; old settings move into its store and turn it on; it starts
        // as her pet with those points, fills her Tamagotchi menu, pays for a gift and counts it, free with points off.
#if CHECKS
        private void SmokeTamagotchiMod(List<string> checks, string output)
        {
            string file = null;
            for (System.IO.DirectoryInfo up = new System.IO.DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); up != null && file == null; up = up.Parent)
                foreach (string here in new[] { System.IO.Path.Combine(up.FullName, "mods", TamagotchiMod + Mods.CodeExtension), System.IO.Path.Combine(up.FullName, "CapriSourceFiles", "mods", TamagotchiMod + Mods.CodeExtension) })
                    if (file == null && System.IO.File.Exists(here)) file = here;
            if (file == null) { checks.Add("LIMIT the Tamagotchi mod: mods/capri-tamagotchi isn't beside this build, so it wasn't imported"); return; }
            Mods mods = ModList;
            Func<ModFile, string, bool> askWas = mainframe.ModAsk;
            mainframe.ModAsk = delegate { return true; };
            Preferences prefsWas = preferences;
            Func<Func<bool>, bool> until = delegate(Func<bool> done)
            {
                for (int i = 0; i < 120 && !done(); i++) { Application.DoEvents(); System.Threading.Thread.Sleep(25); }
                return done();
            };
            string fresh = System.IO.Path.Combine(output, "laid");
            try
            {
                // Laid in off on a new install; once turned on, a later lay leaves it on.
                if (System.IO.Directory.Exists(fresh)) System.IO.Directory.Delete(fresh, true);
                System.IO.Directory.CreateDirectory(fresh);
                Mods.Stock(fresh);
                string offFile = System.IO.Path.Combine(fresh, "mods", "off.txt");
                Func<bool> laidOff = delegate { return System.IO.File.Exists(offFile) && Array.IndexOf(System.IO.File.ReadAllLines(offFile), TamagotchiMod) >= 0; };
                bool laid = System.IO.File.Exists(System.IO.Path.Combine(Mods.HomeIn(System.IO.Path.Combine(fresh, "mods"), TamagotchiMod), TamagotchiMod + Mods.CodeExtension)) && laidOff();
                // The built-in game's old settings move into its store, turn it on and are cleared.
                preferences = new Preferences { Points = 321, Gifts = "trinket:2", NoGifts = true, TamagotchiPlugin = true };
                MovePetGame(fresh);
                Preferences moved = preferences;
                preferences = prefsWas;
                string store = System.IO.Path.Combine(Mods.HomeIn(System.IO.Path.Combine(fresh, "mods"), TamagotchiMod), TamagotchiMod + "-data");
                ModStore kept = new ModStore(delegate { return store; }, TamagotchiMod);
                bool carried = kept.Number("points", 0) == 321 && kept.Get("gifts", "") == "trinket:2" && kept.Flag("no gifts", false) && !laidOff()
                    && moved.Points == 0 && moved.Gifts == null && !moved.NoGifts && !moved.TamagotchiPlugin;
                Mods.Stock(fresh);
                bool stays = !laidOff();
                if (!laid || !carried || !stays)
                    throw new Exception("The Tamagotchi mod wasn't laid in off, or the old game didn't move into it: laid off " + laid + ", moved and turned on " + carried + " (" + string.Join(", ", kept.Keys) + "), left on by a later lay " + stays);

                foreach (Mod old in mods.All.ToArray()) mods.Remove(old);
                string data = System.IO.Path.Combine(mods.HomeOf(TamagotchiMod), TamagotchiMod + "-data");
                if (System.IO.Directory.Exists(data)) System.IO.Directory.Delete(data, true);
                // Its store as the move left it, with gift giving back on so its menu can be checked.
                kept.Remove("no gifts");
                System.IO.Directory.CreateDirectory(data);
                System.IO.File.Copy(System.IO.Path.Combine(store, "store.json"), System.IO.Path.Combine(data, "store.json"));
                // The copy this build carries and lays in, rather than one found up the folders, which can be another branch's.
                string carriedFile = System.IO.Path.Combine(fresh, "mods", TamagotchiMod + Mods.CodeExtension);
                if (System.IO.File.Exists(carriedFile)) file = carriedFile;
                if (!mainframe.ImportMod(file)) throw new Exception("The Tamagotchi mod was refused: " + mainframe.ModStatusText);
                Func<string, ToolStripMenuItem> find = delegate(string text)
                {
                    foreach (ToolStripItem one in tamagotchiItem.DropDownItems) if (one.Text == text) return one as ToolStripMenuItem;
                    return null;
                };
                until(() => mods.Pet != null && find("Give gift") != null);
                ShowTamagotchi();
                ICapriPet pet = mods.Pet;
                ToolStripMenuItem gifts = find("Give gift");
                if (pet == null || gifts == null || gifts.DropDownItems.Count != 5)
                    throw new Exception("The Tamagotchi mod didn't start as her pet with five gifts: pet " + (pet != null) + ", gifts " + (gifts == null ? 0 : gifts.DropDownItems.Count));
                // Her order: the points, Feed her, then what the pet game adds (Wash her, Give gift).
                ToolStripItemCollection order = tamagotchiItem.DropDownItems;
                string said = "";
                // Feed her carries her hunger in brackets when she has one, "Feed her (peckish)"; the order is what is checked.
                foreach (ToolStripItem one in order) if (one.Available) said += (said.Length == 0 ? "" : " / ") + (one is ToolStripSeparator ? "-" : System.Text.RegularExpressions.Regex.Replace(one.Text, @"^(Feed her) \([^)]*\)$", "$1"));
                if (said != "Points: 321 / - / Feed her / Wash her / Give gift")
                    throw new Exception("Her Tamagotchi menu isn't points, Feed her, Wash her and Give gift: " + said);
                // The points the move gave it, a snack paid from them.
                bool survived = pet.Heading == "Points: 321" && pet.Cost("A snack") == 10 && pet.CanPay("A snack") && pet.Pay("A snack") && pet.Heading == "Points: 311" && pet.Feeding;
                ModHost host = Array.Find(LiveHosts(), one => one.Id == TamagotchiMod);
                ((ICapriHost)host).Store.Set("points", 200.0);
                // As her menu opening tells it, without a menu on the screen.
                if (host.menuOpening != null) host.menuOpening();
                ToolStripMenuItem trinket = gifts.DropDownItems[0] as ToolStripMenuItem;
                bool priced = until(() => trinket.ShortcutKeyDisplayString == "60" && trinket.Enabled);
                int worth = Worth("trinket");
                trinket.PerformClick();
                bool given = until(() => pet.Heading == "Points: 140") && ((ICapriHost)host).Store.Get("gifts", "") == "trinket:3";
                // The Mainframe strip: its points on the heading, each ring's list what fills its need; her right-click menu
                // without the Tamagotchi item (374, 375).
                string head = mainframe.StripHeadText, food = mainframe.RingListFor("fullness"), presents = mainframe.RingListFor("comfort"), wash = mainframe.RingListFor("cleanliness"), rest = mainframe.RingListFor("energy"), social = mainframe.RingListFor("social");
                ShowTamagotchi();
                string onMenu = tamagotchiItem.Available ? "shown" : "none";
                string fedTip = mainframe.NeedFacts("fullness"), giftTip = mainframe.NeedFacts("comfort");
                bool menuShort = !tamagotchiItem.Available && (fedTip.Contains(". Fed ") || fedTip.Contains(". Not fed yet.")) && giftTip.Contains(". Given: trinket 3.");
                string after = "";
                foreach (ToolStripItem one in tamagotchiItem.DropDownItems) if (one.Available) after += (after.Length == 0 ? "" : " / ") + (one is ToolStripSeparator ? "-" : one.Text);
                bool ringed = menuShort && after.EndsWith(" / Wash her / Give gift", StringComparison.Ordinal) && head == "TAMAGOTCHI   Points: 140" && food.Contains("A snack") && presents == "Trinket, Flowers, Plushie, Gift, Pet rock" && wash == "Wash her"
                    && (rest.Contains("Go to sleep") || rest.Contains("Wake up")) && social == "Head pats";
                if (!ringed) throw new Exception("The strip's heading or a ring's list is wrong, or Tamagotchi is still on her right-click menu: " + head + " | " + food + " | " + presents + " | " + wash + " | " + rest + " | " + social + " | her menu's Tamagotchi " + onMenu + " | its items " + after + " | tips " + fedTip + " / " + giftTip);
                bool paid = pet.Pay("A proper meal") && pet.Heading == "Points: 115";
                // The Tamagotchi page's Wash button washes her through the pet game, paid like its menu item.
                mainframe.PetPress(2);
                if (!until(() => pet.Heading == "Points: 100")) throw new Exception("The Tamagotchi page's Wash didn't wash her through the pet game: " + pet.Heading);
                ((ICapriHost)host).Store.Set("points", 20.0);
                bool shortOf = !pet.CanPay("A proper meal") && !pet.Pay("A proper meal") && pet.Heading == "Points: 20";
                ((ICapriHost)host).Store.Set("no points", true);
                bool free = pet.Heading == "" && pet.Cost("A proper meal") == 0 && pet.CanPay("A proper meal");
                // Its parts' switches in its Toggles row: a flip reaches its store and is kept with her settings.
                Func<string, ToolStripMenuItem> part = delegate(string named)
                {
                    ToolStripMenuItem row;
                    if (!modFolds.TryGetValue(TamagotchiMod, out row)) return null;
                    foreach (ToolStripItem one in row.DropDownItems) if (one.Text == named) return one as ToolStripMenuItem;
                    return null;
                };
                bool rows = until(() => part("Feeding") != null && part("Gift giving") != null && part("Points") != null) && part("Feeding").Checked && part("Gift giving").Checked;
                ToolStripMenuItem giving = part("Gift giving");
                bool flips = false;
                if (giving != null)
                {
                    giving.PerformClick();
                    flips = !giving.Checked && ((ICapriHost)host).Store.Flag("no gifts", false) && ModToggle(TamagotchiMod, null, "Gift giving") == "off";
                    giving.PerformClick();
                    flips = flips && giving.Checked && !((ICapriHost)host).Store.Flag("no gifts", false) && ModToggle(TamagotchiMod, null, "Gift giving") == "on";
                }
                if (preferences.ModToggles != null) preferences.ModToggles.RemoveAll(one => one != null && one.StartsWith(TamagotchiMod + "\t", StringComparison.Ordinal));
                bool opened = rows && flips && find("Tamagotchi...") == null && !HasModOptions(TamagotchiMod);
                if (!survived || !priced || !given || !paid || !shortOf || !free || !opened)
                    throw new Exception("The Tamagotchi mod went wrong: the moved points kept and spent " + survived + " (" + pet.Heading + "), gifts priced " + priced + ", a trinket given and counted " + given + " (worth " + worth + ")"
                        + ", a meal paid " + paid + ", a meal it can't pay for refused " + shortOf + ", free with points off " + free + ", its Toggles row (Feeding, Gift giving, Points; a flip kept) and no window " + rows + " / " + flips + " / " + opened);
                checks.Add("PASS the Tamagotchi mod as it ships: laid in off the first time only; her Tamagotchi menu reads points, Feed her, Wash her, Give gift, with no window item; the built-in game's points, gifts and parts move into its store once, turn it on and clear the old settings; it starts as her pet with those points and five gifts in her Tamagotchi menu, its Feeding, Gift giving and Points switches in its Toggles row, a flip reaching its store and kept with her settings, prices her food from its points, washes her from the Tamagotchi page's Wash button, pays for a trinket and counts it, says it can't pay for a meal when short, and is free with points off; the Mainframe strip's heading carries its points and each ring lists what fills its need (food, gifts, a wash, her rest, a pat), the food and gift rings' tips say when she was fed and what she was given, and her right-click menu has no Tamagotchi part");
            }
            finally
            {
                preferences = prefsWas;
                mainframe.ModAsk = askWas;
                foreach (Mod left in mods.All.ToArray()) mods.Remove(left);
                // Her own code mods it stopped, laid again and running, so no check after it finds them gone.
                ModsBack();
            }
        }
#endif
    }
}
