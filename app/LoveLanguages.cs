using System;
using System.Collections.Generic;

namespace CapriDesktop
{
    // Her love languages on the Topics page: two or three of the five, strongest first, kept per personality with her
    // topics. A personality and a mood lean her picks; one not listed here, the other side's own among them, picks freely.
    // Both sides have them.
    internal static class LoveLanguages
    {
        internal const string Title = "HER LOVE LANGUAGES", TitleYours = "YOUR LOVE LANGUAGES";

        // The five, each with what it means to her.
        internal static readonly string[,] Five =
        {
            { "Words of affirmation", "kind words, praise, being told" },
            { "Quality time", "time together, full attention" },
            { "Receiving gifts", "small gifts, being thought of" },
            { "Acts of service", "doing things for each other" },
            { "Physical touch", "hugs, closeness, holding hands" }
        };

        // Kept beside her topics under these keys: her picks in order, and the ones added by hand.
        internal const string Key = "love:", ByHand = "lovehand:";

        // How much each leans to each, by the order of Five; a mood's lean adds to her personality's.
        private static readonly Dictionary<string, int[]> leans = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "Caregiver", new[] { 1, 2, 0, 3, 1 } },
            { "Welcome Home", new[] { 1, 2, 0, 3, 1 } },
            { "Devoted", new[] { 2, 3, 0, 1, 2 } },
            { "Emo", new[] { 2, 3, 0, 0, 1 } },
            { "Bratty", new[] { 2, 1, 3, 0, 1 } },
            { "Sassy", new[] { 2, 2, 1, 0, 0 } },
            { "Feral", new[] { 0, 2, 0, 0, 3 } },
            { "Genki", new[] { 1, 3, 1, 0, 2 } },
            { "Kuudere", new[] { 0, 1, 0, 3, 0 } },
            { "Dandere", new[] { 2, 2, 2, 0, 0 } },
            { "Bookworm", new[] { 2, 2, 2, 0, 0 } },
            { "Mentor", new[] { 2, 2, 0, 2, 0 } },
            { "Free spirit", new[] { 0, 3, 0, 0, 1 } },
            { "Needy", new[] { 1, 2, 0, 0, 2 } },
            { "Playful", new[] { 0, 2, 1, 0, 1 } },
            { "Affectionate", new[] { 2, 1, 0, 0, 2 } },
            { "Mischievous", new[] { 0, 1, 1, 0, 0 } },
            { "Melancholy", new[] { 2, 1, 0, 0, 0 } }
        };

        // For the checks: a pick instead of a die.
        internal static Random Dice = new Random();

        internal static string Gloss(string name)
        {
            for (int i = 0; i < Five.GetLength(0); i++) if (string.Equals(Five[i, 0], name, StringComparison.OrdinalIgnoreCase)) return Five[i, 1];
            return null;
        }

        // A typed word to one of the five: its name or any word of it ("touch", "gifts"); null when none.
        internal static string Match(string typed)
        {
            string words = (typed ?? "").Trim();
            if (words.Length == 0) return null;
            for (int i = 0; i < Five.GetLength(0); i++) if (string.Equals(Five[i, 0], words, StringComparison.OrdinalIgnoreCase)) return Five[i, 0];
            for (int i = 0; i < Five.GetLength(0); i++)
                foreach (string part in Five[i, 0].Split(' '))
                    if (part.Length > 2 && (part.StartsWith(words, StringComparison.OrdinalIgnoreCase) || words.StartsWith(part, StringComparison.OrdinalIgnoreCase))) return Five[i, 0];
            if (words.StartsWith("serv", StringComparison.OrdinalIgnoreCase) || words.StartsWith("help", StringComparison.OrdinalIgnoreCase)) return "Acts of service";
            return null;
        }

        // Hers as this personality: kept once picked, so they stay hers.
        internal static string[] Hers(string personality)
        {
            if (string.IsNullOrEmpty(personality)) return new string[0];
            string[] kept = Topics.Saved(Key + personality);
            if (kept != null && kept.Length > 0) return kept;
            kept = Roll(personality, null, new List<string>());
            Topics.Save(Key + personality, kept);
            return kept;
        }

        // Picked again for a personality and mood: the ones added by hand stay first, the rest fresh.
        internal static string[] Reroll(string personality, string mood)
        {
            if (string.IsNullOrEmpty(personality)) return new string[0];
            string[] picked = Roll(personality, mood, new List<string>(Topics.Saved(ByHand + personality) ?? new string[0]));
            Topics.Save(Key + personality, picked);
            return picked;
        }

        internal static void Add(string personality, string name)
        {
            string one = Match(name);
            if (one == null || string.IsNullOrEmpty(personality)) return;
            List<string> mine = new List<string>(Hers(personality));
            if (!mine.Contains(one)) { mine.Add(one); Topics.Save(Key + personality, mine.ToArray()); }
            List<string> hand = new List<string>(Topics.Saved(ByHand + personality) ?? new string[0]);
            if (!hand.Contains(one)) { hand.Add(one); Topics.Save(ByHand + personality, hand.ToArray()); }
        }

        internal static void Remove(string personality, string name)
        {
            if (string.IsNullOrEmpty(personality)) return;
            List<string> mine = new List<string>(Hers(personality));
            mine.RemoveAll(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase));
            Topics.Save(Key + personality, mine.ToArray());
            List<string> hand = new List<string>(Topics.Saved(ByHand + personality) ?? new string[0]);
            if (hand.RemoveAll(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase)) > 0) Topics.Save(ByHand + personality, hand.ToArray());
        }

        // Two or three by weight, none twice: one each, and three more for each step of lean.
        private static string[] Roll(string personality, string mood, List<string> kept)
        {
            int[] weight = { 1, 1, 1, 1, 1 };
            foreach (string named in new[] { personality, mood })
            {
                int[] lean;
                if (named != null && leans.TryGetValue(named, out lean)) for (int i = 0; i < 5; i++) weight[i] += 3 * lean[i];
            }
            List<string> picked = new List<string>(kept);
            int many = Math.Max(picked.Count, 2 + Dice.Next(2));
            while (picked.Count < many)
            {
                int total = 0;
                for (int i = 0; i < 5; i++) if (!picked.Contains(Five[i, 0])) total += weight[i];
                if (total == 0) break;
                int roll = Dice.Next(total), at = 0;
                for (; at < 5; at++)
                {
                    if (picked.Contains(Five[at, 0])) continue;
                    roll -= weight[at];
                    if (roll < 0) break;
                }
                picked.Add(Five[Math.Min(at, 4), 0]);
            }
            return picked.ToArray();
        }

        // Her turn note: how she most shows and feels love, strongest first.
        internal static string Note(string personality)
        {
            string[] hers = Hers(personality);
            if (hers.Length == 0) return "";
            List<string> said = new List<string>();
            foreach (string one in hers) said.Add(one.ToLowerInvariant() + " (" + Gloss(one) + ")");
            return "\nYour love languages, strongest first: " + string.Join(", ", said.ToArray()) + ". Show care that way when it fits, and feel it most when they do.";
        }

#if CHECKS
        // Kept once picked, two or three, none twice; a lean pulls; a reroll keeps the ones added by hand; a typed word
        // finds its one; the note names them.
        internal static void Check(List<string> checks)
        {
            Func<string, string[]> savedWas = Topics.Saved;
            Action<string, string[]> saveWas = Topics.Save;
            Random diceWas = Dice;
            try
            {
                Dictionary<string, string[]> kept = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                Topics.Saved = delegate(string who) { string[] had; return kept.TryGetValue(who, out had) ? had : null; };
                Topics.Save = delegate(string who, string[] picked) { kept[who] = picked; };
                Dice = new Random(11);
                string[] first = Hers("Caregiver"), again = Hers("Caregiver");
                bool stays = first.Length >= 2 && first.Length <= 3 && string.Join("|", first) == string.Join("|", again) && new HashSet<string>(first).Count == first.Length;
                int service = 0, tries = 200;
                for (int i = 0; i < tries; i++) if (Array.IndexOf(Reroll("Caregiver", null), "Acts of service") >= 0) service++;
                Add("Caregiver", "touch");
                Remove("Caregiver", "Acts of service");
                Add("Caregiver", "Acts of service");
                string[] rolled = Reroll("Caregiver", "Melancholy");
                bool hand = Array.IndexOf(rolled, "Physical touch") >= 0 && Array.IndexOf(rolled, "Acts of service") >= 0;
                Remove("Caregiver", "Physical touch");
                bool gone = Array.IndexOf(kept[ByHand + "Caregiver"], "Physical touch") < 0 && Array.IndexOf(kept[Key + "Caregiver"], "Physical touch") < 0;
                bool unknown = Hers("Somebody new").Length >= 2 && Match("gifts") == "Receiving gifts" && Match("time") == "Quality time" && Match("pizza") == null;
                string note = Note("Caregiver");
                bool noted = note.IndexOf("love languages", StringComparison.Ordinal) > 0 && note.IndexOf(Gloss(Hers("Caregiver")[0]), StringComparison.Ordinal) > 0;
                if (!stays || service * 10 < tries * 6 || !hand || !gone || !unknown || !noted)
                    throw new Exception("Her love languages went wrong: " + string.Join(", ", first) + ", kept " + stays + ", acts of service in " + service + " of " + tries + ", hand-added kept " + hand + ", unlisted and typed " + unknown + ", note " + noted);
                checks.Add("PASS her love languages: " + first.Length + " kept as picked (" + string.Join(", ", first) + "), a caregiver's lean to acts of service in " + service + " of " + tries + " rerolls, hand-added kept through a reroll and gone once removed, typed words found, her turn note names them");
            }
            finally { Topics.Saved = savedWas; Topics.Save = saveWas; Dice = diceWas; }
        }
#endif
    }
}
