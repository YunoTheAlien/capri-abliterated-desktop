using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Her side of a pet game, which is a mod (capri-tamagotchi, laid in off). With one running she gets hungry and it
    // prices her food; without one, Feed her is a free kindness like a headpat and nothing is kept.
    internal sealed partial class Companion
    {
        internal const string TamagotchiMod = "capri-tamagotchi";

        private ToolStripMenuItem tamagotchiItem, pointsLine, nuggiesItem;
        private ToolStripSeparator pointsRule;
        private int feedsSinceNudge;
        // For the checks: a pet game without a mod.
        private ICapriPet petStandIn;

        // The food only Emo and Bratty are offered, in their own lower case.
        internal const string Nuggies = "chimpken nuggies";

        // The pet game running now, or null.
        private ICapriPet RunningPet { get { return petStandIn ?? (ModList == null ? null : ModList.Pet); } }

        // A question to the pet game; its own fault counts as no answer.
        private T AskPet<T>(Func<ICapriPet, T> ask, T otherwise)
        {
            ICapriPet pet = RunningPet;
            if (pet == null) return otherwise;
            try { return ask(pet); }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); return otherwise; }
        }

        // Her hunger counts only while a pet game runs with its feeding on.
        internal bool HungerOn { get { return AskPet(pet => pet.Feeding, false); } }

        // Feed her is on her menu unless a pet game has turned its feeding off.
        internal bool FeedingOn { get { return RunningPet == null || HungerOn; } }

        // Food in the act list's order of cost, so her menu reads cheapest first.
        private static int FoodCost(string named)
        {
            foreach (Acts.Act food in Acts.Of("food"))
                foreach (Acts.Offer offer in food.Offers) if (offer.Label == named) return offer.Cost;
            return 10;
        }

        // Her hunger nag: her personality's own when it has them (Emo and Bratty ask for nuggies), else the shared lines.
        private string[] HungerPool(bool hungry)
        {
            string[] shared = hungry ? HungryLines : PeckishLines, hers = null;
            try { hers = Personalities.Lines(hungry ? "hungry" : "peckish", shared); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            return hers != null && hers.Length > 0 ? hers : shared;
        }

        // Nuggies are on the menu only while she is Emo or Bratty underneath.
        private static bool NuggiesFor(string under) { return under == "Emo" || under == "Bratty"; }

        // The price sits in the menu's shortcut column, so every price lines up on the right whatever the name's length.
        private static void Costed(ToolStripMenuItem item, string named, int cost)
        {
            item.Text = named;
            item.ShortcutKeyDisplayString = cost > 0 ? cost.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
            item.ShowShortcutKeys = cost > 0;
        }

        // Her right-click menu's Tamagotchi: the pet game's slot, built once, filled as the menu opens.
        private void MakeTamagotchi()
        {
            tamagotchiItem = new ToolStripMenuItem("Tamagotchi");
            pointsLine = new ToolStripMenuItem("");
            pointsRule = new ToolStripSeparator();
            tamagotchiItem.DropDownItems.Add(pointsLine);
            tamagotchiItem.DropDownItems.Add(pointsRule);
            nuggiesItem = new ToolStripMenuItem(Nuggies, null, delegate { FeedPaid("nuggies", Nuggies); }) { Tag = Nuggies };
            feedItem.DropDownItems.Add(nuggiesItem);
            LikeMenu(tamagotchiItem);
            LikeMenu(feedItem);
            List<ToolStripItem> foods = new List<ToolStripItem>();
            foreach (ToolStripItem one in feedItem.DropDownItems) foods.Add(one);
            List<ToolStripItem> sorted = new List<ToolStripItem>(foods);
            sorted.Sort(delegate(ToolStripItem a, ToolStripItem b)
            {
                string x = a.Tag as string, y = b.Tag as string;
                int byCost = (x == null ? int.MaxValue : FoodCost(x)).CompareTo(y == null ? int.MaxValue : FoodCost(y));
                return byCost != 0 ? byCost : foods.IndexOf(a).CompareTo(foods.IndexOf(b));
            });
            feedItem.DropDownItems.Clear();
            foreach (ToolStripItem one in sorted) feedItem.DropDownItems.Add(one);
        }

        // Where Feed her sits and what each food costs, as her menu opens.
        private void ShowTamagotchi()
        {
            if (tamagotchiItem == null || feedItem == null) return;
            bool on = RunningPet != null;
            // Feed her's list as her menu layout put it, her top list in stock.
            ToolStripItemCollection top = feedItem.Owner != null && feedItem.Owner != tamagotchiItem.DropDown ? feedItem.Owner.Items : tamagotchiItem.Owner != null ? tamagotchiItem.Owner.Items : menu.Items;
            // Feed her lives under Tamagotchi while a pet game runs, and back on her menu as it was without one.
            if (on && feedItem.Owner != tamagotchiItem.DropDown)
            {
                int at = top.IndexOf(feedItem);
                top.Remove(feedItem);
                if (top.IndexOf(tamagotchiItem) < 0) top.Insert(Math.Max(0, at), tamagotchiItem);
                tamagotchiItem.DropDownItems.Insert(tamagotchiItem.DropDownItems.IndexOf(pointsRule) + 1, feedItem);
            }
            else if (!on && feedItem.Owner == tamagotchiItem.DropDown)
            {
                int at = top.IndexOf(tamagotchiItem);
                tamagotchiItem.DropDownItems.Remove(feedItem);
                top.Remove(tamagotchiItem);
                top.Insert(Math.Max(0, at), feedItem);
            }
            // Never on her right-click menu: its points, Feed her and the pet game's items come up from the Mainframe strip's
            // rings and the Tamagotchi page's buttons (375). Without a pet game Feed her is on her menu as before.
            tamagotchiItem.Visible = false;
            string heading = on ? AskPet(pet => pet.Heading, "") ?? "" : "";
            pointsLine.Visible = pointsRule.Visible = heading.Length > 0;
            pointsLine.Text = heading;
            pointsLine.ForeColor = AskPet(pet => pet.HeadingColour, Neon.Points);
            feedItem.Visible = FeedingOn;
            foreach (ToolStripItem one in feedItem.DropDownItems)
            {
                ToolStripMenuItem food = one as ToolStripMenuItem;
                string named = food == null ? null : food.Tag as string;
                if (named == null) continue;
                Costed(food, named, on ? AskPet(pet => pet.Cost(named), 0) : 0);
                food.Enabled = !on || AskPet(pet => pet.CanPay(named), true);
            }
            nuggiesItem.Visible = NuggiesFor(Personalities.Under);
        }

        // Her rest items as her state names them: lie down or sit up, sleep or wake.
        private void RestLabels()
        {
            lieItem.Text = lying ? "Sit up" : "Lie down";
            lieItem.Enabled = lying || (clip == perch && LieWay() != null);
            sleepItem.Text = napAsked || Asleep ? "Wake up" : "Go to sleep";
        }

        // The old Tamagotchi window's lines, now in the food and gift rings' tips: when she was fed; what she was given and
        // her favourite, from the Tamagotchi mod's store. Empty without a pet game.
        internal string PetFacts(string need)
        {
            if (RunningPet == null) return "";
            if (need == "fullness")
            {
                double hours = SinceFed;
                if (hours < 0) return "Not fed yet.";
                return "Fed " + (hours < 1 ? "under an hour" : Math.Round(hours, 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + " hours") + " ago, " + HungerWord + ".";
            }
            if (need != "comfort" || ModList == null || ModList.PetId != TamagotchiMod) return "";
            ModHost host = Array.Find(LiveHosts(), one => one.Id == TamagotchiMod);
            string gifts = host == null ? "" : ((ICapriHost)host).Store.Get("gifts", "") ?? "";
            List<string> said = new List<string>();
            string favourite = null;
            int bestWorth = 0, bestCount = 0;
            foreach (string part in gifts.Split(';'))
            {
                int colon = part.LastIndexOf(':'), count;
                if (colon <= 0 || !int.TryParse(part.Substring(colon + 1), out count) || count <= 0) continue;
                string act = part.Substring(0, colon);
                said.Add(act + " " + count);
                int worth = Worth(act);
                if (worth > 0 && (worth > bestWorth || worth == bestWorth && count > bestCount)) { favourite = act; bestWorth = worth; bestCount = count; }
            }
            if (said.Count == 0) return "Nothing given yet.";
            return "Given: " + string.Join(", ", said.ToArray()) + "." + (favourite == null ? "" : " Her favourite: " + favourite + ".");
        }

        // The pet game's heading, its points, while one runs; empty without.
        internal string PetHeading { get { return RunningPet != null ? AskPet(pet => pet.Heading, "") ?? "" : ""; } }

        // Where a pet game's own items go in her Tamagotchi menu: last, so they follow Feed her.
        internal int PetItemsAt { get { return tamagotchiItem.DropDownItems.Count; } }

        // A food from her menu: paid to the pet game first when one runs, and free without one.
        private void FeedPaid(string act, string named)
        {
            bool pet = RunningPet != null;
            if (pet && !AskPet(game => game.Pay(named), false)) return;
            Feed(act);
            if (pet) FedForward();
        }

        // Her forwardness, while it is set to drift and a pet game runs, goes up by steps: a step for a gift she likes,
        // and a step every third feed.
        private void ForwardSteps(int steps)
        {
            if (Personalities.SpecialInterestOverride == Personalities.Drifting) Dynamics.NudgeForward(steps);
        }

        private void FedForward()
        {
            if (++feedsSinceNudge < 3) return;
            feedsSinceNudge = 0;
            ForwardSteps(1);
        }

        // Her line for a gift, for a pet game: one she likes from her personality's own and the "likes it" pool in lines.md,
        // one she doesn't from the "dislikes it" pool, since her own lines for a gift are glad ones.
        private string GiftLine(string act, bool liked)
        {
            string[] hers = liked ? Personalities.Lines(act, new string[0]) : new string[0];
            string line = Sayings.For(act, liked, hers) ?? Lines.Pick(hers);
            // A gift is never food, so given while she is hungry it may get the meme for it instead.
            Acts.Act listed = Acts.Find(act);
            if (listed != null && listed.Kind == "gift" && HungerWord == "hungry") line = MemeInContext("you give her a gift while hungry") ?? line;
            return line;
        }

        // A mod's act as it lands: a gift she likes, from a pet game, nudges her forwardness a step.
        internal void PetGave(string act)
        {
            Acts.Act listed = Acts.Find(act);
            if (RunningPet == null || listed == null || listed.Kind != "gift" || Worth(act) <= 0) return;
            ForwardSteps(1);
        }

        // Once, from settings that held the built-in game: its points, gifts and parts go to the mod's own store, the mod
        // is turned on if the game was, and the old settings are cleared. A value the store already has is kept.
        private void MovePetGame(string data)
        {
            Preferences was = preferences;
            if (!was.TamagotchiPlugin && was.Points <= 0 && string.IsNullOrEmpty(was.Gifts) && !was.NoFeeding && !was.NoPoints && !was.NoGifts) return;
            try
            {
                string folder = Path.Combine(Mods.HomeIn(Path.Combine(data, "mods"), TamagotchiMod), TamagotchiMod + "-data");
                Directory.CreateDirectory(folder);
                ModStore store = new ModStore(delegate { return folder; }, TamagotchiMod);
                if (was.Points > 0 && store.Get("points") == null) store.Set("points", (double)Math.Min(999, was.Points));
                if (!string.IsNullOrEmpty(was.Gifts) && store.Get("gifts") == null) store.Set("gifts", was.Gifts);
                if (was.NoFeeding && store.Get("no feeding") == null) store.Set("no feeding", true);
                if (was.NoPoints && store.Get("no points") == null) store.Set("no points", true);
                if (was.NoGifts && store.Get("no gifts") == null) store.Set("no gifts", true);
                if (was.TamagotchiPlugin) Mods.TurnOnLaid(data, TamagotchiMod);
                Brain.Log("pet", -1, "Her Tamagotchi moved into its mod: " + was.Points + " points, gifts " + (was.Gifts ?? "none") + (was.TamagotchiPlugin ? ", turned on." : ", left off."));
                was.TamagotchiPlugin = was.NoFeeding = was.NoPoints = was.NoGifts = false;
                was.Points = 0;
                was.Gifts = null;
                SavePreferences();
            }
            catch (IOException caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            catch (UnauthorizedAccessException caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // ---------- Checks ----------

#if CHECKS
        // A pet game for the checks: food at its act list's price from 50 points, feeding on or off.
        private sealed class StandInPet : ICapriPet
        {
            internal int Left = 50;
            internal bool Fed = true;
            public string Heading { get { return "Points: " + Left; } }
            public Color HeadingColour { get { return Color.Gold; } }
            public int Cost(string food) { return FoodCost(food); }
            public bool Pay(string food) { if (!CanPay(food)) return false; Left -= Cost(food); return true; }
            public bool CanPay(string food) { return Left >= Cost(food); }
            public bool Feeding { get { return Fed; } }
        }

        // With no pet game a day passes and nothing of hunger shows: no nag, no plain worth added, a plain label, Feed her
        // free on her menu and no fed time kept. With one, all of it comes back and food is paid to it.
        private void SmokePetOff(List<string> checks)
        {
            long fedWas = preferences.FedAt, treatedWas = preferences.TreatedAt;
            ICapriPet standInWas = petStandIn;
            try
            {
                petStandIn = null;
                if (ModList != null && ModList.Pet != null) throw new Exception("A pet game mod was running before the pet game check.");
                preferences.FedAt = SimTime.UtcNow.AddHours(-30).Ticks;
                hungerSaidAt = -1;
                ShowTamagotchi();
                ShowFeedMenu();
                int add;
                string nag = HungerLine(clock.Elapsed.TotalSeconds, false), worth = Hunger("snack", out add);
                bool label = feedItem.Text == "Feed her", onMenu = feedItem.Owner == menu && feedItem.Available && menu.Items.IndexOf(tamagotchiItem) < 0;
                ToolStripMenuItem snack = null;
                foreach (ToolStripItem one in feedItem.DropDownItems) if ((one.Tag as string) == "A snack") snack = one as ToolStripMenuItem;
                bool free = snack != null && snack.ShortcutKeyDisplayString == null && snack.Enabled;
                preferences.FedAt = 0;
                FeedPaid("snack", "A snack");
                bool kept = preferences.FedAt != 0;
                Func<bool> petRow = delegate
                {
                    if (mainframe == null) return false;
                    mainframe.OpenWorkApps();
                    bool shown = mainframe.WorkRowsShown.Contains(Working.Pet);
                    mainframe.CloseWorkApps();
                    return shown;
                };
                bool rowGone = !petRow();
                if (nag != null || worth != null || add != 0 || !label || !onMenu || !free || kept || !rowGone)
                    throw new Exception("With no pet game, hunger still showed: a nag " + (nag ?? "none") + ", worth " + (worth ?? "none") + " +" + add + ", label " + feedItem.Text + ", Feed her on her menu " + onMenu + ", free " + free
                        + ", fed time kept " + kept + ", While working's Tamagotchi row gone " + rowGone);
                checks.Add("PASS with no pet game, a day without food brings no hunger: no nag, nothing added to a food's worth, a plain Feed her on her menu, free, no fed time kept and no Tamagotchi row in While working");

                StandInPet pet = new StandInPet();
                petStandIn = pet;
                preferences.FedAt = SimTime.UtcNow.AddHours(-30).Ticks;
                hungerSaidAt = -1;
                ShowTamagotchi();
                ShowFeedMenu();
                nag = HungerLine(clock.Elapsed.TotalSeconds, false);
                worth = Hunger("snack", out add);
                bool hungryLabel = feedItem.Text == "Feed her (hungry)", underIt = feedItem.Owner == tamagotchiItem.DropDown && pointsLine.Text == "Points: 50" && pointsLine.Available;
                bool priced = snack != null && snack.ShortcutKeyDisplayString == FoodCost("A snack").ToString(System.Globalization.CultureInfo.InvariantCulture);
                FeedPaid("snack", "A snack");
                bool paid = pet.Left == 50 - FoodCost("A snack") && preferences.FedAt > 0;
                pet.Left = 0;
                ShowTamagotchi();
                bool greyed = !snack.Enabled;
                long fedThen = preferences.FedAt;
                FeedPaid("snack", "A snack");
                bool refused = greyed && preferences.FedAt == fedThen;
                bool rowBack = mainframe == null || petRow();
                pet.Fed = false;
                ShowTamagotchi();
                bool feedingOff = !feedItem.Available && !HungerOn;
                if (nag == null || worth != "hungry" || add != 2 || !hungryLabel || !underIt || !priced || !paid || !refused || !feedingOff || !rowBack)
                    throw new Exception("With a pet game, hunger didn't come back: a nag " + (nag ?? "none") + ", worth " + (worth ?? "none") + " +" + add + ", label " + feedItem.Text + ", under Tamagotchi with its heading " + underIt
                        + ", priced " + priced + ", paid " + paid + ", greyed and refused when short " + refused + ", its feeding off hides Feed her " + feedingOff + ", While working's row back " + rowBack);
                checks.Add("PASS with a pet game, hunger comes back: her nag, a hungry food worth 2 more and Feed her (hungry), under Tamagotchi with its heading; food is priced and paid to it, greyed and refused when it can't be, While working's Tamagotchi row is back, and its Feeding off hides Feed her");
            }
            finally
            {
                petStandIn = standInWas;
                preferences.FedAt = fedWas;
                preferences.TreatedAt = treatedWas;
                hungerSaidAt = -1;
                ShowTamagotchi();
                ShowFeedMenu();
                SavePreferences();
                ownLines.Clear();
                for (int i = 0; i < 100 && LineBusy; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
                ownLines.Clear();
                heard = "";
            }
        }
#endif

        // Hungry as Emo and as Bratty, her nag comes from her own lines and some ask for nuggies; as Caregiver it is the
        // shared lines, none about nuggies. Null when right.
        private string HungerAsks()
        {
            foreach (string named in new[] { "Emo", "Bratty", "Caregiver" })
            {
                Personalities.Chosen = named;
                bool own = named != "Caregiver";
                foreach (bool hungry in new[] { false, true })
                {
                    string[] pool = HungerPool(hungry), shared = hungry ? HungryLines : PeckishLines;
                    bool nuggies = false;
                    foreach (string line in pool) if (line.IndexOf("nuggie", StringComparison.OrdinalIgnoreCase) >= 0) nuggies = true;
                    if (own && (pool == shared || !nuggies)) return named + " " + (hungry ? "hungry" : "peckish") + " lines aren't her own nuggies asks: " + string.Join(" / ", pool);
                    if (!own && nuggies) return named + " asked for nuggies: " + string.Join(" / ", pool);
                }
                preferences.FedAt = SimTime.UtcNow.AddHours(-8).Ticks;
                hungerSaidAt = -1;
                string said = HungerLine(clock.Elapsed.TotalSeconds, false);
                if (said == null) return named + " said nothing when hungry";
                if (own)
                {
                    bool hers = false;
                    foreach (string line in HungerPool(true))
                        if (said.IndexOf(line.Trim().TrimEnd('.', '!', '?').Substring(0, Math.Min(12, line.Trim().Length)), StringComparison.OrdinalIgnoreCase) >= 0) hers = true;
                    if (!hers) return named + " said a hunger line not her own: " + said;
                }
            }
            return null;
        }

        // Nuggies only for Emo and Bratty, every pack's gift sections and nuggies lines, and her hunger asks, with a pet
        // game running; peckish while switching between work and anything else, she asks once.
#if CHECKS
        private void SmokeTamagotchi(List<string> checks)
        {
            long fedWas = preferences.FedAt, treatedWas = preferences.TreatedAt;
            string chosenWas = Personalities.Chosen;
            ICapriPet standInWas = petStandIn;
            try
            {
                petStandIn = new StandInPet { Left = 500 };
                Personalities.Chosen = "Emo";
                ShowTamagotchi();
                bool emo = nuggiesItem.Available;
                Personalities.Chosen = "Caregiver";
                ShowTamagotchi();
                bool caregiver = nuggiesItem.Available;
                Personalities.Chosen = "Bratty";
                ShowTamagotchi();
                bool bratty = nuggiesItem.Available;
                preferences.FedAt = 0;
                FeedPaid("nuggies", Nuggies);
                bool fed = preferences.FedAt > 0 && ((StandInPet)petStandIn).Left == 500 - FoodCost(Nuggies);
                if (!emo || caregiver || !bratty || !fed)
                    throw new Exception("Nuggies went wrong: Emo " + emo + ", Caregiver " + caregiver + ", Bratty " + bratty + ", fed and paid " + fed);
                string packs = Personalities.GiftSections();
                if (packs != null) throw new Exception(packs);
                string asks = HungerAsks();
                if (asks != null) throw new Exception(asks);
                bool attached;
                string second = Personalities.SecondSideNuggies(out attached);
                if (second != null) throw new Exception(second);
                string shared = Sayings.Text;
                if (shared.IndexOf(Personalities.OtherSpelling, StringComparison.OrdinalIgnoreCase) >= 0) throw new Exception("lines.md spells it " + Personalities.OtherSpelling);
                foreach (string pool in new[] { "## trinket likes it", "## flowers likes it", "## plushie likes it", "## gift likes it", "## gift remembered" })
                    if (shared.IndexOf(pool, StringComparison.Ordinal) < 0) throw new Exception("lines.md has no " + pool);
                checks.Add("PASS chimpken nuggies are on Feed her only while she is Emo or Bratty, and feed her paid to the pet game; every pack ships its gift sections and likes, Emo and Bratty their nuggies, and lines.md the shared gift pools");
                checks.Add("PASS Emo and Bratty ask for nuggies when hungry, in their own peckish and hungry lines, and joke about stolen nuggies; the villain origin line is under their nuggies; chimpken is the only spelling in any pack or lines.md; Caregiver keeps the shared hunger lines");
                checks.Add(attached ? "PASS Emo and Bratty on the second side keep every nuggies line, in their own copies there, all spelled chimpken"
                    : "LIMIT Emo and Bratty on the second side: nothing attached in this build to check against");

                // Peckish and switching between work and anything else: one ask, not one per switch. A feed starts over.
                double at = clock.Elapsed.TotalSeconds;
                preferences.FedAt = SimTime.UtcNow.AddHours(-4).Ticks;
                hungerSaidAt = -1;
                bool first = HungerLine(at, false) != null;
                int again = 0;
                for (int flip = 1; flip <= 12; flip++) if (HungerLine(at + flip * 15, flip % 2 == 1) != null) again++;
                preferences.FedAt = SimTime.UtcNow.AddHours(-4).AddSeconds(1).Ticks;
                bool afterFeed = HungerLine(at + 200, false) != null;
                if (!first || again != 0 || !afterFeed)
                    throw new Exception("Her hunger ask didn't hold across work switches: first " + first + ", asked again " + again + " times in 12 switches, after a feed " + afterFeed);
                checks.Add("PASS peckish while switching between work and anything else, she asks once, not once per switch; only a feed lets her ask again before three hours");
                SmokePetOff(checks);
            }
            finally
            {
                petStandIn = standInWas;
                Personalities.Chosen = chosenWas;
                preferences.FedAt = fedWas;
                preferences.TreatedAt = treatedWas;
                hungerSaidAt = -1;
                ShowTamagotchi();
                SavePreferences();
                ownLines.Clear();
                for (int i = 0; i < 100 && LineBusy; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
                ownLines.Clear();
                if (talkState != TalkState.Off) CloseTalk();
                heard = "";
            }
        }
#endif
    }
}
