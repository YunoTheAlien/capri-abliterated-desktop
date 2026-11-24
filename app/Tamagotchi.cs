using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CapriDesktop
{
    // A little pet game and her own mod item, off until turned on in Toggles or Mods: points she earns buy food and gifts
    // under Right-click > Tamagotchi. Off, Feed her is free. She is never harmed, only snippier when hungry.
    internal sealed partial class Companion
    {
        // A point a minute while she runs, hidden or locked included. A working day of six to eight hours open earns about
        // 420, so 999 takes two to three such days; someone home more reaches it sooner, and it stops there.
        internal const int MostPoints = 999;
        private const double PointSeconds = 60;
        // Saved every so many points rather than every point, so settings.json is not written once a minute.
        private const int SaveEvery = 10;

        private ToolStripMenuItem tamagotchiItem, pointsLine, giftItem, nuggiesItem, tamagotchiToggle, feedingToggle, giftsToggle, pointsToggle;
        private ToolStripSeparator pointsRule;
        private double pointTickAt = -1;
        private int pointsUnsaved, feedsSinceNudge;

        // What she can be given, top to bottom, from the act list.
        private static readonly Acts.Act[] Gifts = Acts.Of("gift");

        // The food only Emo and Bratty are offered, in their own lower case.
        internal const string Nuggies = "chimpken nuggies";

        // The cost of each food, scaled so a working day's points keep her fed with plenty over.
        private static int FoodCost(string named)
        {
            foreach (Acts.Act food in Acts.Of("food"))
                foreach (Acts.Offer offer in food.Offers) if (offer.Label == named) return offer.Cost;
            return 10;
        }

        private static Acts.Offer GiftOffer(string act)
        {
            foreach (Acts.Act gift in Gifts) if (gift.Name == act) return gift.Offers[0];
            return null;
        }

        private static int GiftCost(string act)
        {
            Acts.Offer offer = GiftOffer(act);
            return offer == null ? 0 : offer.Cost;
        }

        private static string GiftName(string act)
        {
            Acts.Offer offer = GiftOffer(act);
            return offer == null ? act : offer.Said;
        }

        internal bool GiftsOn { get { return TamagotchiOn && !preferences.NoGifts; } }

        // Her hunger nag: her personality's own when it has them (Emo and Bratty ask for nuggies), else the shared lines.
        private string[] HungerPool(bool hungry)
        {
            string[] shared = hungry ? HungryLines : PeckishLines, hers = null;
            try { hers = Personalities.Lines(hungry ? "hungry" : "peckish", shared); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            return hers != null && hers.Length > 0 ? hers : shared;
        }

        // Nuggies are on the menu only while she is Emo or Bratty underneath.
        private static bool NuggiesFor(string under) { return under == "Emo" || under == "Bratty"; }

        internal bool TamagotchiOn { get { return preferences.TamagotchiPlugin; } }

        // The one switch, from Toggles and from her row in Mods. Her points stay whichever way it goes.
        internal void SetTamagotchi(bool on)
        {
            if (preferences.TamagotchiPlugin == on) return;
            preferences.TamagotchiPlugin = on;
            pointTickAt = -1;
            SavePreferences();
            if (mainframe != null) mainframe.Sync();
        }

        // Each part under it counts only while the whole is on.
        internal bool FeedingOn { get { return !TamagotchiOn || !preferences.NoFeeding; } }
        internal bool PointsOn { get { return TamagotchiOn && !preferences.NoPoints; } }
        internal int Points { get { return Math.Max(0, Math.Min(MostPoints, preferences.Points)); } }

        // Called on her tick: a point for each minute she has run since the last one, never past the cap.
        private void EarnPoints(double now)
        {
            if (!PointsOn) { pointTickAt = -1; return; }
            if (pointTickAt < 0) { pointTickAt = now; return; }
            if (now - pointTickAt < PointSeconds) return;
            int earned = (int)((now - pointTickAt) / PointSeconds);
            pointTickAt += earned * PointSeconds;
            AddPoints(earned);
        }

        private void AddPoints(int earned)
        {
            int had = Points, now = Math.Min(MostPoints, had + Math.Max(0, earned));
            if (now == had) return;
            preferences.Points = now;
            pointsUnsaved += now - had;
            if (pointsUnsaved >= SaveEvery || now == MostPoints) { pointsUnsaved = 0; SavePreferences(); }
        }

        // Spends the cost if points are on and there are enough; false when there aren't. Free with points off.
        private bool Spend(int cost)
        {
            if (!PointsOn) return true;
            if (Points < cost) return false;
            preferences.Points = Points - cost;
            pointsUnsaved = 0;
            SavePreferences();
            return true;
        }

        // The cost sits in the menu's shortcut column, so every price lines up on the right whatever the name's length.
        private static void Costed(ToolStripMenuItem item, string named, int cost, bool shown)
        {
            item.Text = named;
            item.ShortcutKeyDisplayString = shown ? cost.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
            item.ShowShortcutKeys = shown;
        }

        // Her right-click menu's Tamagotchi: built once, filled as the menu opens.
        private void MakeTamagotchi()
        {
            tamagotchiItem = new ToolStripMenuItem("Tamagotchi");
            pointsLine = new ToolStripMenuItem("Points: 0") { ToolTipText = "A point for each minute she runs, up to " + MostPoints };
            pointsRule = new ToolStripSeparator();
            tamagotchiItem.DropDownItems.Add(pointsLine);
            tamagotchiItem.DropDownItems.Add(pointsRule);
            giftItem = new ToolStripMenuItem("Give gift") { ToolTipText = "Something that isn't food. She remembers what you give her" };
            foreach (Acts.Act gift in Gifts)
            {
                string act = gift.Name;
                giftItem.DropDownItems.Add(new ToolStripMenuItem(gift.Offers[0].Label, null, delegate { Give(act); }) { Tag = act });
            }
            tamagotchiItem.DropDownItems.Add(giftItem);
            nuggiesItem = new ToolStripMenuItem(Nuggies, null, delegate { FeedPaid("nuggies", Nuggies); }) { Tag = Nuggies };
            feedItem.DropDownItems.Add(nuggiesItem);
            LikeMenu(tamagotchiItem);
            LikeMenu(giftItem);
            LikeMenu(feedItem);
            // Food in price order, cheapest first, as every price list is for now.
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

        // Where Feed her sits, what it costs and what can be afforded, as her menu opens.
        private void ShowTamagotchi()
        {
            if (tamagotchiItem == null || feedItem == null) return;
            bool on = TamagotchiOn;
            ToolStripItemCollection top = menu.Items;
            // Feed her lives under Tamagotchi while it's on, and back on her menu as it was while it's off.
            if (on && feedItem.Owner != tamagotchiItem.DropDown)
            {
                int at = top.IndexOf(feedItem);
                top.Remove(feedItem);
                if (top.IndexOf(tamagotchiItem) < 0) top.Insert(Math.Max(0, at), tamagotchiItem);
                tamagotchiItem.DropDownItems.Insert(tamagotchiItem.DropDownItems.IndexOf(giftItem), feedItem);
            }
            else if (!on && feedItem.Owner == tamagotchiItem.DropDown)
            {
                int at = top.IndexOf(tamagotchiItem);
                tamagotchiItem.DropDownItems.Remove(feedItem);
                top.Remove(tamagotchiItem);
                top.Insert(Math.Max(0, at), feedItem);
            }
            tamagotchiItem.Visible = on;
            pointsLine.Visible = pointsRule.Visible = PointsOn;
            pointsLine.Text = "Points: " + Points;
            pointsLine.ForeColor = PointsColour;
            feedItem.Visible = FeedingOn;
            foreach (ToolStripItem one in feedItem.DropDownItems)
            {
                ToolStripMenuItem food = one as ToolStripMenuItem;
                string named = food == null ? null : food.Tag as string;
                if (named == null) continue;
                int cost = FoodCost(named);
                Costed(food, named, cost, PointsOn);
                food.Enabled = !PointsOn || Points >= cost;
            }
            nuggiesItem.Visible = NuggiesFor(Personalities.Under);
            giftItem.Visible = GiftsOn;
            foreach (ToolStripItem one in giftItem.DropDownItems)
            {
                ToolStripMenuItem gift = one as ToolStripMenuItem;
                string act = gift == null ? null : gift.Tag as string;
                if (act == null) continue;
                int cost = GiftCost(act);
                Costed(gift, GiftOffer(act).Label, cost, PointsOn);
                gift.Enabled = !PointsOn || Points >= cost;
            }
        }

        // ---------- Gifts ----------

        // Given something: paid for, counted, scored by her personality and mood (one she dislikes counts against you),
        // her forwardness nudged up a step for one she likes when it is set to drift, and her line for it.
        private void Give(string act)
        {
            try
            {
                if (!GiftsOn || !Spend(GiftCost(act))) return;
                Counted(act, GiftCount(act) + 1);
                Sayings.Rested(GiftGivenKey, 0);
                SavePreferences();
                Did(act);
                bool liked = Worth(act) > 0;
                if (liked) ForwardSteps(1);
                string line = GiftLine(act, liked);
                heard = "a gift";
                bool bubbleOnly = InChat || Hushed || FullScreen();
                Brain.Log("gift", -1, act + " | " + (string.IsNullOrEmpty(line) ? "no line" : "\"" + line + "\"") + " | given " + GiftCount(act));
                Remark(heard, line, bubbleOnly, null, false, "> sys: ");
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // Her line for a gift: one she likes from her personality's own and the "likes it" pool in lines.md, one she
        // doesn't from the "dislikes it" pool, since her own lines for a gift are glad ones.
        private string GiftLine(string act, bool liked)
        {
            string[] hers = liked ? Personalities.Lines(act, new string[0]) : new string[0];
            return Sayings.For(act, liked, hers) ?? Lines.Pick(hers);
        }

        // Her forwardness, while it is set to drift, goes up by steps: a step a gift, and a step every third feed.
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

        // What she has been given, as "trinket:2;flowers:1" in settings.json.
        private int GiftCount(string act)
        {
            foreach (string part in (preferences.Gifts ?? "").Split(';'))
            {
                int colon = part.IndexOf(':'), much;
                if (colon > 0 && part.Substring(0, colon) == act && int.TryParse(part.Substring(colon + 1), out much)) return much;
            }
            return 0;
        }

        private void Counted(string act, int much)
        {
            List<string> kept = new List<string>();
            foreach (Acts.Act gift in Gifts)
            {
                string one = gift.Name;
                int have = one == act ? much : GiftCount(one);
                if (have > 0) kept.Add(one + ":" + have);
            }
            preferences.Gifts = string.Join(";", kept.ToArray());
        }

        // Her favourite of what she has been given: the one she likes most, then the one given most; never one she
        // doesn't like.
        internal string Favourite()
        {
            string best = null;
            int bestWorth = int.MinValue, bestCount = 0;
            foreach (Acts.Act gift in Gifts)
            {
                string act = gift.Name;
                int count = GiftCount(act);
                if (count == 0) continue;
                int worth = Worth(act);
                if (worth <= 0) continue;
                if (worth > bestWorth || worth == bestWorth && count > bestCount) { best = act; bestWorth = worth; bestCount = count; }
            }
            return best;
        }

        // Kept when a gift is given, so a memory of one never comes straight after it.
        internal const string GiftGivenKey = "sys:gift given";
        internal const double GiftSettleHours = 6;

        // Now and then, at most once a day and not within six hours of a gift, she mentions her favourite thing they gave her.
        private string GiftMemoryLine()
        {
            if (!GiftsOn) return null;
            string favourite = Favourite();
            if (favourite == null || Sayings.HoursSince(GiftGivenKey) < GiftSettleHours || random.Next(100) >= 4 || !Sayings.Rested("sys:gift memory", 24)) return null;
            string line = Sayings.From("gift remembered");
            return string.IsNullOrEmpty(line) ? null : line.Replace("%gift%", GiftName(favourite));
        }

        // The colour her points and costs are drawn in: gold, one of the colours they can change in Custom colours.
        private static Color PointsColour { get { return Neon.Points; } }

        // Toggles > Tamagotchi: the whole of it, and each part, on and off.
        private ToolStripMenuItem MakeTamagotchiToggles()
        {
            ToolStripMenuItem holder = new ToolStripMenuItem("Tamagotchi") { ToolTipText = "Points, feeding and gifts as a little pet game. Off, Feed her is free and on her menu as before" };
            tamagotchiToggle = new ToolStripMenuItem("Tamagotchi", null, delegate { SetTamagotchi(!TamagotchiOn); });
            feedingToggle = new ToolStripMenuItem("Feeding", null, delegate { preferences.NoFeeding = !preferences.NoFeeding; SavePreferences(); });
            feedingToggle.ToolTipText = "Off, there's nothing to feed her and she never says she's hungry";
            pointsToggle = new ToolStripMenuItem("Points", null, delegate { preferences.NoPoints = !preferences.NoPoints; pointTickAt = -1; SavePreferences(); });
            pointsToggle.ToolTipText = "Off, everything is free and there's no counter";
            giftsToggle = new ToolStripMenuItem("Gift giving", null, delegate { preferences.NoGifts = !preferences.NoGifts; SavePreferences(); });
            giftsToggle.ToolTipText = "Off, there's no Give gift and she doesn't bring up what you gave her";
            holder.DropDownItems.Add(tamagotchiToggle);
            holder.DropDownItems.Add(new ToolStripSeparator());
            holder.DropDownItems.Add(feedingToggle);
            holder.DropDownItems.Add(giftsToggle);
            holder.DropDownItems.Add(pointsToggle);
            holder.DropDownOpening += delegate
            {
                tamagotchiToggle.Checked = TamagotchiOn;
                feedingToggle.Checked = !preferences.NoFeeding;
                giftsToggle.Checked = !preferences.NoGifts;
                pointsToggle.Checked = !preferences.NoPoints;
                feedingToggle.Enabled = giftsToggle.Enabled = pointsToggle.Enabled = TamagotchiOn;
            };
            LikeMenu(holder);
            return holder;
        }

        // A food from her menu: paid for first when points are on.
        private void FeedPaid(string act, string named)
        {
            if (TamagotchiOn && !Spend(FoodCost(named))) return;
            Feed(act);
            if (TamagotchiOn) FedForward();
        }

        // ---------- Checks ----------

        // Gifts in order under Feed her with their costs: giving one pays, counts and says a line, nudging forwardness
        // only while it drifts; the favourite comes back at most daily, never within six hours of a gift; nuggies only for Emo and Bratty; Gift giving
        // off hides it all.
        private void SmokeTamagotchiGifts(List<string> checks)
        {
            string giftsWas = preferences.Gifts, chosenWas = Personalities.Chosen;
            bool giftsOffWas = preferences.NoGifts;
            int overrideWas = Personalities.SpecialInterestOverride;
            try
            {
                preferences.Gifts = null;
                preferences.NoGifts = preferences.NoFeeding = preferences.NoPoints = false;
                preferences.TamagotchiPlugin = true;
                preferences.Points = 500;
                ShowTamagotchi();
                List<string> order = new List<string>();
                foreach (ToolStripItem one in giftItem.DropDownItems) order.Add(one.Text + "   " + ((one as ToolStripMenuItem) == null ? "" : ((ToolStripMenuItem)one).ShortcutKeyDisplayString));
                int feedAt = tamagotchiItem.DropDownItems.IndexOf(feedItem), giftAt = tamagotchiItem.DropDownItems.IndexOf(giftItem);
                if (string.Join(" | ", order.ToArray()) != "Trinket   60 | Flowers   100 | Plushie   140 | Gift   180 | Pet rock   30" || feedAt < 0 || giftAt != feedAt + 1)
                    throw new Exception("Give gift wasn't Trinket, Flowers, Plushie, Gift, Pet rock with their costs under Feed her: " + string.Join(" | ", order.ToArray()));
                preferences.Points = 120;
                ShowTamagotchi();
                bool greyed = !((ToolStripMenuItem)giftItem.DropDownItems[2]).Enabled && ((ToolStripMenuItem)giftItem.DropDownItems[1]).Enabled;

                Personalities.SpecialInterestOverride = Personalities.Drifting;
                int before = Dynamics.SpecialInterestDrift;
                preferences.Points = 500;
                long fedWas = preferences.FedAt;
                Give("flowers");
                int after = Dynamics.SpecialInterestDrift;
                bool nudged = after == Math.Min(Math.Max(Dynamics.SpecialInterestLow, Dynamics.SpecialInterestHigh), before + 1);
                Personalities.SpecialInterestOverride = 4;
                Give("trinket");
                bool heldStill = Personalities.SpecialInterest >= 0 && Dynamics.SpecialInterestDrift == after;
                if (Points != 500 - 100 - 60 || GiftCount("flowers") != 1 || GiftCount("trinket") != 1 || preferences.FedAt != fedWas || !greyed || !nudged || !heldStill || LoadPreferences().Gifts != preferences.Gifts)
                    throw new Exception("Giving went wrong: points " + Points + ", counted " + preferences.Gifts + ", food reset " + (preferences.FedAt != fedWas) + ", greyed " + greyed + ", forwardness " + before + " to " + after + " (nudged " + nudged + ", held " + heldStill + ")");
                string favourite = Favourite();
                string soon = null;
                for (int tries = 0; tries < 400 && soon == null; tries++) soon = GiftMemoryLine();
                string memory = null, again = null;
                TimeSpan offsetWas = SimTime.Offset;
                SimTime.Advance(TimeSpan.FromHours(GiftSettleHours + 1));
                try
                {
                    for (int tries = 0; tries < 400 && memory == null; tries++) memory = GiftMemoryLine();
                    for (int tries = 0; tries < 400 && again == null; tries++) again = GiftMemoryLine();
                }
                finally { SimTime.Reset(); SimTime.Advance(offsetWas); }
                if (soon != null || favourite == null || memory == null || memory.IndexOf(GiftName(favourite), StringComparison.Ordinal) < 0 || memory.Contains("%gift%") || again != null)
                    throw new Exception("Her favourite gift wasn't remembered once a day, and never within hours of a gift: right after " + (soon ?? "none") + " / " + favourite + " / " + memory + " / " + again);
                checks.Add("PASS Give gift: Trinket 60, Flowers 100, Plushie 140, Gift 180 under Feed her, greyed when short; giving takes its cost, is counted in settings.json, never feeds her, says her line and nudges her forwardness a step only while it drifts; her favourite gift comes back at most once a day");

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
                bool fed = preferences.FedAt > 0 && Points == 500 - 160 - 15;
                preferences.NoGifts = true;
                ShowTamagotchi();
                bool hidden = !giftItem.Available;
                preferences.NoGifts = false;
                if (!emo || caregiver || !bratty || !fed || !hidden)
                    throw new Exception("Nuggies or Gift giving went wrong: Emo " + emo + ", Caregiver " + caregiver + ", Bratty " + bratty + ", fed " + fed + ", Gift giving off hides it " + hidden);
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
                checks.Add("PASS chimpken nuggies are on Feed her only while she is Emo or Bratty, cost 15 and feed her; Gift giving off hides Give gift; every pack ships its gift sections and likes, Emo and Bratty their nuggies, and lines.md the shared gift pools");
                checks.Add("PASS Emo and Bratty ask for nuggies when hungry, in their own peckish and hungry lines, and joke about stolen nuggies; the villain origin line is under their nuggies; chimpken is the only spelling in any pack or lines.md; Caregiver keeps the shared hunger lines");
                checks.Add(attached ? "PASS Emo and Bratty on the second side keep every nuggies line, in their own copies there, all spelled chimpken"
                    : "LIMIT Emo and Bratty on the second side: nothing attached in this build to check against");
            }
            finally
            {
                preferences.Gifts = giftsWas;
                preferences.NoGifts = giftsOffWas;
                Personalities.Chosen = chosenWas;
                Personalities.SpecialInterestOverride = overrideWas;
            }
        }

        // Her own mod item: off on a new install and on settings from before it was one, points kept; her row in Mods and
        // Toggles are one switch.
        private void SmokeTamagotchiItem(List<string> checks)
        {
            byte[] older = System.Text.Encoding.UTF8.GetBytes("{\"NoTamagotchi\":false,\"Points\":321}");
            Preferences carried = (Preferences)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Preferences)).ReadObject(new System.IO.MemoryStream(older));
            bool startsOff = !new Preferences().TamagotchiPlugin && !carried.TamagotchiPlugin && carried.Points == 321;
            preferences.TamagotchiPlugin = false;
            preferences.Points = 321;
            ShowTamagotchi();
            bool offMenu = feedItem.Owner == menu && menu.Items.IndexOf(tamagotchiItem) < 0;
            string about = mainframe.ModRowAbout(Mainframe.TamagotchiItem) ?? "";
            bool row = about.StartsWith("HER OWN", StringComparison.Ordinal) && mainframe.SwitchModRow(Mainframe.TamagotchiItem) && TamagotchiOn && LoadPreferences().TamagotchiPlugin;
            ShowTamagotchi();
            bool onMenu = feedItem.Owner == tamagotchiItem.DropDown && menu.Items.IndexOf(tamagotchiItem) >= 0;
            tamagotchiToggle.PerformClick();
            bool toggled = !TamagotchiOn && !LoadPreferences().TamagotchiPlugin && Points == 321;
            if (!startsOff || !offMenu || !row || !onMenu || !toggled)
                throw new Exception("Tamagotchi as her own mod item went wrong: starts off with points kept " + startsOff + ", Feed her on her menu while off " + offMenu + ", her row in Mods turns it on " + row + " (" + about + "), its menu back " + onMenu + ", Toggles is the same switch " + toggled);
            checks.Add("PASS Tamagotchi is her own mod item: off on a new install and on settings from before, points kept; her row in Mods and Toggles are one saved switch, and Feed her moves with it");
        }

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

        // ---------- Check ----------

        // Points come a minute at a time to 999; food shows its cost, greys out when unaffordable and is paid for;
        // Tamagotchi off puts a free Feed her back; Feeding off removes the menu and hunger lines; Points off makes all
        // free; points live in settings.json.
        private void SmokeTamagotchi(List<string> checks)
        {
            int pointsWas = preferences.Points;
            bool tamaWas = preferences.TamagotchiPlugin, feedWas = preferences.NoFeeding, freeWas = preferences.NoPoints;
            long fedWas = preferences.FedAt, treatedWas = preferences.TreatedAt;
            try
            {
                preferences.NoFeeding = preferences.NoPoints = false;
                preferences.TamagotchiPlugin = true;
                preferences.Points = 0;
                pointTickAt = -1;
                EarnPoints(1000);
                EarnPoints(1000 + 59);
                int none = Points;
                EarnPoints(1000 + 61);
                int one = Points;
                EarnPoints(1000 + 61 + 600);
                int eleven = Points;
                preferences.Points = 995;
                pointTickAt = 5000;
                EarnPoints(5000 + 600);
                if (none != 0 || one != 1 || eleven != 11 || Points != MostPoints) throw new Exception("Points weren't a minute each and capped at " + MostPoints + ": " + none + ", " + one + ", " + eleven + ", " + Points);
                if (LoadPreferences().Points != MostPoints) throw new Exception("Reaching the cap wasn't saved in settings.json.");

                preferences.Points = 15;
                ShowTamagotchi();
                ToolStripMenuItem snack = null, meal = null;
                foreach (ToolStripItem item in feedItem.DropDownItems)
                {
                    if ((item.Tag as string) == "A snack") snack = item as ToolStripMenuItem;
                    if ((item.Tag as string) == "A proper meal") meal = item as ToolStripMenuItem;
                }
                if (feedItem.Owner != tamagotchiItem.DropDown || !tamagotchiItem.Available || pointsLine.Text != "Points: 15" || snack == null || meal == null
                    || snack.Text != "A snack" || snack.ShortcutKeyDisplayString != "10" || !snack.Enabled || meal.Text != "A proper meal" || meal.ShortcutKeyDisplayString != "25" || meal.Enabled || pointsLine.ForeColor != PointsColour)
                    throw new Exception("Tamagotchi's menu was wrong: " + pointsLine.Text + ", " + (snack == null ? "no snack" : snack.Text + " " + snack.ShortcutKeyDisplayString + " " + snack.Enabled) + ", " + (meal == null ? "no meal" : meal.Text + " " + meal.ShortcutKeyDisplayString + " " + meal.Enabled));
                FeedPaid("snack", "A snack");
                if (Points != 5) throw new Exception("A snack didn't cost its 10 points: " + Points);
                checks.Add("PASS Tamagotchi points: one a minute while she runs, capped at " + MostPoints + " and saved; Feed her sits under Tamagotchi under the points line, each food with its cost, greyed when it can't be afforded, and a snack takes its 10");

                preferences.NoPoints = true;
                ShowTamagotchi();
                FeedPaid("meal", "A proper meal");
                bool freeFood = Points == 5 && snack.Text == "A snack" && meal.Enabled && !pointsLine.Available;
                preferences.NoPoints = false;
                preferences.NoFeeding = true;
                ShowTamagotchi();
                preferences.FedAt = SimTime.UtcNow.AddHours(-8).Ticks;
                hungerSaidAt = -1;
                bool noFeeding = !feedItem.Available && HungerLine(clock.Elapsed.TotalSeconds, false) == null;
                preferences.NoFeeding = false;
                preferences.TamagotchiPlugin = false;
                ShowTamagotchi();
                bool backAsItWas = feedItem.Owner == menu && menu.Items.IndexOf(tamagotchiItem) < 0 && snack.Text == "A snack" && meal.Enabled;
                hungerSaidAt = -1;
                bool nagsBack = HungerLine(clock.Elapsed.TotalSeconds, false) != null;
                if (!freeFood || !noFeeding || !backAsItWas || !nagsBack)
                    throw new Exception("A Tamagotchi part didn't turn off as it should: points off free " + freeFood + ", feeding off " + noFeeding + ", Tamagotchi off back as it was " + backAsItWas + ", hunger with it off " + nagsBack);
                checks.Add("PASS Toggles > Tamagotchi: Points off makes food free with no counter; Feeding off takes Feed her and her hunger lines away; Tamagotchi off puts Feed her back on her menu, free, and her hunger lines with it");

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
                SmokeTamagotchiGifts(checks);
                SmokeTamagotchiItem(checks);
            }
            finally
            {
                preferences.TamagotchiPlugin = tamaWas;
                preferences.NoFeeding = feedWas;
                preferences.NoPoints = freeWas;
                preferences.Points = pointsWas;
                preferences.FedAt = fedWas;
                preferences.TreatedAt = treatedWas;
                pointTickAt = -1;
                hungerSaidAt = -1;
                ShowTamagotchi();
                SavePreferences();
                // Nothing left in flight: a gift line queued behind her voice landed in the next section's checks.
                ownLines.Clear();
                for (int i = 0; i < 100 && LineBusy; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
                ownLines.Clear();
                if (talkState != TalkState.Off) CloseTalk();
                heard = "";
            }
        }
    }
}
