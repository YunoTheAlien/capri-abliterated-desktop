#if CHECKS
using System;
using System.Collections.Generic;

namespace CapriDesktop
{
    // Her six needs: an hour runs each down by her pack's Her needs with her mood's added, a repeated act fills less, each low
    // mark changes what it names, a Your Own rate is used, and with no pet game nothing is kept.
    internal sealed partial class Companion
    {
        private void SmokePetStats(List<string> checks)
        {
            Func<List<string>> loadWas = PetStats.Load;
            Action<List<string>> storeWas = PetStats.Store;
            Func<bool> runningWas = PetStats.Running, restingWas = PetStats.Resting, idleWas = PetStats.Idle;
            DateTime startedWas = PetStats.StartedAt;
            string chosenWas = Personalities.Chosen, attitudeWas = Personalities.Attitude;
            List<string> rows = new List<string>();
            bool running = true;
            try
            {
                PetStats.Load = delegate { return rows; };
                PetStats.Store = delegate(List<string> made) { rows = made; };
                PetStats.Running = delegate { return running; };
                PetStats.Resting = delegate { return false; };
                bool idle = false;
                PetStats.Idle = delegate { return idle; };
                PetStats.StartedAt = DateTime.MinValue;
                List<string> wrong = new List<string>();

                // Rates from the files: Devoted is the most social, Feral the least, a Clingy mood adds its 4.
                Personalities.Chosen = "Devoted"; Personalities.Attitude = "";
                double devoted = PetStats.Rate("social falls");
                Personalities.Chosen = "Feral";
                double feral = PetStats.Rate("social falls");
                Personalities.Attitude = "Clingy";
                double clingy = PetStats.Rate("social falls");
                if (devoted != 9 || feral != 4 || clingy != 8) wrong.Add("social falls read " + devoted + " / " + feral + " / " + clingy + ", wanted 9 / 4 / 8");

                // An hour runs each need down by its rate.
                Personalities.Attitude = "";
                DateTime start = new DateTime(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);
                foreach (string need in PetStats.Names) rows.Add(need + "=80");
                PetStats.Tick(start);
                PetStats.Tick(start.AddHours(1));
                foreach (string need in PetStats.Names)
                {
                    int want = (int)Math.Round(80 - PetStats.Rate(need + " falls"));
                    if (PetStats.Level(need) != want) wrong.Add(need + " after an hour " + PetStats.Level(need) + ", wanted " + want);
                }

                // The same act twice fills less the second time.
                rows.Clear();
                foreach (string need in PetStats.Names) rows.Add(need + "=20");
                Did("praised");
                int once = PetStats.Level("social");
                Did("praised");
                int twice = PetStats.Level("social");
                if (once <= 20 || twice - once >= once - 20) wrong.Add("praise filled social " + (once - 20) + " then " + (twice - once));

                // Low marks: Clingy weighs up, she wants attention sooner, sulks sooner, and a full need changes nothing.
                rows.Clear();
                foreach (string need in PetStats.Names) rows.Add(need + "=10");
                if (PetStats.MoodWeight("Clingy") <= 1 || PetStats.MoodWeight("Sleepy") <= 1 || PetStats.Sooner() >= 1 || PetStats.Sulkier(6) >= 6 || !PetStats.Low("energy"))
                    wrong.Add("low needs didn't change her: Clingy x" + PetStats.MoodWeight("Clingy") + ", sooner x" + PetStats.Sooner() + ", sulk at " + PetStats.Sulkier(6));
                rows.Clear();
                foreach (string need in PetStats.Names) rows.Add(need + "=90");
                if (PetStats.MoodWeight("Clingy") != 1 || PetStats.Sooner() != 1 || PetStats.Sulkier(6) != 6) wrong.Add("full needs still changed her");

                // What helps: a need's list has her acts, the one worth most first.
                List<string> helps = PetStats.Helps("social");
                if (helps.Count == 0 || !helps.Contains("praised")) wrong.Add("what helps social: " + string.Join(", ", helps.ToArray()));

                // A Your Own rate is used, and Defaults takes it back: set on whoever is read now, after the ticks above, which on
                // a slow box can let a mood roll in between.
                Personalities.Chosen = "Feral"; Personalities.Attitude = "";
                string readFrom = Personalities.Under;
                double before = PetStats.Rate("social falls");
                SetYourOwn(readFrom, Personalities.NeedsHeading, "social falls", "20");
                double own = PetStats.Rate("social falls");
                ResetYourOwn(readFrom);
                double after = PetStats.Rate("social falls");
                if (own != 20 || after != before) wrong.Add("a Your Own social falls read " + own + " then " + after + " (from " + before + "; read from " + readFrom + ", mood \"" + Personalities.Attitude + "\")");

                // Eight hours closed: her energy comes back slowly, the others fall only a little, a share of their running rate.
                rows.Clear();
                foreach (string need in PetStats.Names) rows.Add(need + "=50");
                PetStats.StartedAt = DateTime.MinValue;
                PetStats.Tick(start);
                PetStats.StartedAt = start.AddHours(8);
                PetStats.Tick(start.AddHours(8));
                int energyClosed = PetStats.Level("energy");
                foreach (string need in PetStats.Names)
                {
                    if (need == "energy") continue;
                    double drop = 50 - PetStats.Level(need), runningDrop = PetStats.Rate(need + " falls") * 8;
                    if (drop <= 0 || drop > runningDrop / 2) wrong.Add(need + " fell " + drop + " in eight hours closed; running it would fall " + runningDrop);
                }
                if (energyClosed <= 50) wrong.Add("energy after eight hours closed " + energyClosed + ", wanted more than 50");
                // Nobody about while she runs: energy comes back slowly; someone about, it falls as before.
                PetStats.StartedAt = DateTime.MinValue;
                rows.Clear();
                foreach (string need in PetStats.Names) rows.Add(need + "=50");
                idle = true;
                PetStats.Tick(start);
                PetStats.Tick(start.AddHours(2));
                int energyIdle = PetStats.Level("energy");
                idle = false;
                PetStats.Tick(start.AddHours(4));
                if (energyIdle <= 50 || PetStats.Level("energy") >= energyIdle) wrong.Add("energy with nobody about went " + energyIdle + ", then with someone about " + PetStats.Level("energy"));

                // A slider at 3x fills its own need three times as fast and drains it as before; energy's fills while resting.
                int[] ratesWas = preferences.PetRates;
                try
                {
                    preferences.PetRates = new[] { 3, 3, 3, 3, 3, 3 };
                    PetStats.StartedAt = DateTime.MinValue;
                    rows.Clear();
                    foreach (string need in PetStats.Names) rows.Add(need + "=80");
                    PetStats.Tick(start);
                    PetStats.Tick(start.AddHours(1));
                    foreach (string need in PetStats.Names)
                    {
                        int want = (int)Math.Round(80 - PetStats.Rate(need + " falls"));
                        if (PetStats.Level(need) != want) wrong.Add(need + " at 3x drained " + PetStats.Level(need) + " after an hour, wanted " + want + " (drains don't follow the slider)");
                    }
                    rows.Clear();
                    foreach (string need in PetStats.Names) rows.Add(need + "=10");
                    PetStats.Raise("stimulation", 1);
                    int raised = PetStats.Level("stimulation");
                    if (raised != (int)Math.Round(10 + 3 * PetStats.Rate("needs gain"))) wrong.Add("stimulation at 3x raised to " + raised + ", wanted " + (10 + 3 * PetStats.Rate("needs gain")));
                    idle = true;
                    PetStats.StartedAt = DateTime.MinValue;
                    PetStats.Tick(start);
                    PetStats.Tick(start.AddHours(1));
                    idle = false;
                    if (PetStats.Level("energy") != 85) wrong.Add("energy at 3x after an hour resting " + PetStats.Level("energy") + ", wanted 85 (10 + 3 x 25)");
                }
                finally { preferences.PetRates = ratesWas; }

                // Every pack: energy drains 2 an hour (two days from full) and comes back 25 an hour resting (four hours from
                // empty); an ordinary liked act fills a fifth of a need.
                foreach (string pack in new[] { Personalities.Stock, "Caregiver", "Welcome Home", "Devoted", "Emo", "Bratty", "Sassy", "Feral" })
                {
                    double falls = Personalities.StockNeed(pack, "energy falls", -1), rests = Personalities.StockNeed(pack, "energy rests", -1), gain = Personalities.StockNeed(pack, "needs gain", -1);
                    if (falls != 2 || rests != 25 || gain < 17) wrong.Add(pack + ": energy falls " + falls + ", rests " + rests + ", needs gain " + gain + "; wanted 2, 25 and 17 or more");
                }
                Personalities.Chosen = "Feral"; Personalities.Attitude = "";
                PetStats.StartedAt = DateTime.MinValue;
                rows.Clear();
                foreach (string need in PetStats.Names) rows.Add(need + "=0");
                idle = true;
                PetStats.Tick(start);
                PetStats.Tick(start.AddHours(4));
                idle = false;
                if (PetStats.Level("energy") != 100) wrong.Add("energy after four hours resting from empty " + PetStats.Level("energy") + ", wanted 100");

                // Once: a Your Own energy rests, energy falls or needs gain at its pack's old stock goes, so the new stock is
                // read; one they set themselves stays.
                bool carriedWas = preferences.PetFillCarried;
                string who = Personalities.Under;
                try
                {
                    SetYourOwn(who, Personalities.NeedsHeading, "energy rests", "8");
                    SetYourOwn(who, Personalities.NeedsHeading, "energy falls", "6");
                    preferences.PetFillCarried = false;
                    DropOldEnergyRests();
                    double restsOld = PetStats.Rate("energy rests"), fallsOld = PetStats.Rate("energy falls");
                    SetYourOwn(who, Personalities.NeedsHeading, "energy rests", "3");
                    preferences.PetFillCarried = false;
                    DropOldEnergyRests();
                    double fromOwn = PetStats.Rate("energy rests");
                    if (restsOld != 25 || fallsOld != 2 || fromOwn != 3) wrong.Add("carried once: an old 8 read " + restsOld + " (wanted 25), an old 6 falls read " + fallsOld + " (wanted 2), their own 3 read " + fromOwn);
                }
                finally { SetYourOwn(who, Personalities.NeedsHeading, "energy rests", null); SetYourOwn(who, Personalities.NeedsHeading, "energy falls", null); preferences.PetFillCarried = carriedWas; }

                // No pet game: nothing moves.
                running = false;
                rows.Clear();
                foreach (string need in PetStats.Names) rows.Add(need + "=50");
                Did("praised");
                PetStats.Tick(start.AddHours(5));
                if (PetStats.Level("social") != 50 || PetStats.Low("social") || PetStats.MoodWeight("Clingy") != 1) wrong.Add("with no pet game her needs still moved");

                if (wrong.Count > 0) throw new Exception("Her needs went wrong: " + string.Join("; ", wrong.ToArray()));
                checks.Add("PASS her six needs: rates read from each pack's Her needs with her mood's added (Devoted 9, Feral 4, Feral Clingy 8), an hour runs each down by its rate, a repeated praise fills less, low needs weigh Clingy and Sleepy up, bring her asks and sulks sooner, what helps lists her acts worth most first, a Your Own rate is used, a Toggles rate at 3x fills each need three times as fast and drains it as before, energy drains 2 an hour and comes back 25 an hour resting in every pack (empty to full in four hours), needs gain 17 or more, an old untouched stock carried once, and with no pet game nothing moves");
            }
            finally
            {
                PetStats.Load = loadWas; PetStats.Store = storeWas; PetStats.Running = runningWas; PetStats.Resting = restingWas;
                PetStats.Idle = idleWas; PetStats.StartedAt = startedWas;
                Personalities.Chosen = chosenWas; Personalities.Attitude = attitudeWas;
            }
        }
    }
}
#endif
