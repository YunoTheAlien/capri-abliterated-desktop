using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // Her temperaments in personalSettings\personalities\: one folder per side of the switch, each a whole copy of
    // every pack, read one at a time. Each personality carries its own reaction lines; a missing or broken set falls
    // back to hers.
    internal static class Personalities
    {
        internal const string SettingsName = "personalSettings";
        internal const string FolderName = "personalities";
        internal const string PlainName = "censored";
        // Two folders, no third. The old name is kept so a folder written under it is renamed rather than
        // left beside the new one.
        internal const string SenpaiName = "interests";

        // personalSettings\personalities. The two below are what actually hold files.
        internal static string Folder;

        internal static string Plain { get { return string.IsNullOrEmpty(Folder) ? null : Path.Combine(Folder, PlainName); } }
        internal static string Senpais { get { return string.IsNullOrEmpty(Folder) ? null : Path.Combine(Folder, SenpaiName); } }
        // The senpai folder for passes that walk every file: none while the switch is off, when it is kept but never read.
        internal static string SenpaisRead { get { return Brain.SenpaiOn ? Senpais : null; } }

        // Where everything she writes for them lives: personalSettings itself, the folder her packs sit under.
        internal static string SettingsFolder
        {
            get { return string.IsNullOrEmpty(Folder) ? null : Path.GetDirectoryName(Folder); }
        }

        // The text of every pack and every mood the build ships. For a sweep that has to know what the gated
        // half of each looks like without anybody keeping a second list of it.
        internal static List<string> Shipped()
        {
            List<string> found = new List<string>();
            for (int pack = 0; pack < Packs.GetLength(0); pack++) found.Add(Packs[pack, 1]);
            // And whatever attached, which is where the senpai half lives now. Without this the censored
            // sweep had no senpai lines to look for and passed over a fixture of nothing: the table it reads
            // stopped carrying that half the day it moved out.
            if (SenpaiFor != null)
                foreach (string named in Order())
                {
                    string theirs = null;
                    try { theirs = Legacy.Modern(SenpaiFor(named)); }
                    catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                    if (!string.IsNullOrEmpty(theirs)) found.Add(theirs);
                }
            try { found.AddRange(Dynamics.Shipped()); }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            return found;
        }

        // The plain half of every pack and mood the build ships, and nothing from what attached: for her translation
        // packs, which never carry a senpai line. A pack or mood marked senpai only is left out whole.
        internal static List<string> ShippedPlain()
        {
            List<string> texts = new List<string>();
            for (int pack = 0; pack < Packs.GetLength(0); pack++) texts.Add(Packs[pack, 1]);
            try { texts.AddRange(Dynamics.Shipped()); }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            List<string> found = new List<string>();
            foreach (string text in texts)
            {
                if (string.IsNullOrEmpty(text) || Marked(text)) continue;
                string plain, senpai;
                Split(text, out plain, out senpai);
                if (!string.IsNullOrEmpty(plain)) found.Add(plain);
            }
            return found;
        }

        // The senpai folder under its old name becomes the new one, with everything in it. Her moods do the
        // same with theirs, and both run before anything is written into either.
        internal static void RenamedFolder() { RenamedFolder(Folder); }

        internal static void RenamedFolder(string under)
        {
            try
            {
                if (string.IsNullOrEmpty(under)) return;
                string now = Path.Combine(under, SenpaiName), was = null;
                if (Directory.Exists(now) || !Directory.Exists(under)) return;
                foreach (string one in Directory.GetDirectories(under)) if (Legacy.WasFolder(Path.GetFileName(one))) { was = one; break; }
                if (was == null) return;
                Directory.Move(was, now);
                Tucked.Away(now);
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // Every pack file she has, plain ones first. Used by the passes that tidy or check each file in turn.
        internal static List<string> Files()
        {
            List<string> found = new List<string>();
            foreach (string folder in new[] { Plain, SenpaisRead })
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) continue;
                // Every file, not every .md: her senpai ones are numbered and have no type at all. Backups are
                // left out, since a .bak is a copy of a pack rather than one of them.
                try { foreach (string file in Directory.GetFiles(folder)) if (Notes.IsPack(file)) found.Add(file); }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
            return found;
        }

        // Every pack in a folder, whatever its file type, so a pass over them misses none. Backups are left
        // out, since a .bak is a copy of a pack rather than one of them.
        private static string[] Packed(string folder)
        {
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return new string[0];
                List<string> found = new List<string>();
                foreach (string file in Directory.GetFiles(folder))
                    if (Notes.IsPack(file)) found.Add(file);
                return found.ToArray();
            }
            catch (Exception) { return new string[0]; }
        }

        // Whether a path sits in the senpai folder, which makes what is in it senpai whatever the file says.
        internal static bool InSenpai(string path)
        {
            string folder = Senpais;
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(folder)) return false;
            try { return string.Equals(Path.GetFullPath(Path.GetDirectoryName(path)).TrimEnd('\\'), Path.GetFullPath(folder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
            catch (Exception) { return false; }
        }

        // One pack's text cut in two: everything that is not a senpai section, and the senpai sections on their own
        // under the pack's title. A pack that is senpai as a whole is not cut, and goes to the senpai folder whole.
        internal static void Split(string pack, out string plain, out string senpai)
        {
            plain = pack;
            senpai = null;
            if (string.IsNullOrEmpty(pack)) return;
            if (Marked(pack)) { plain = null; senpai = pack; return; }
            string[] rows = pack.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            List<string> mine = new List<string>(), theirs = new List<string>(), scored = new List<string>();
            string title = null;
            bool senpaiHere = false, any = false;
            foreach (string raw in rows)
            {
                string one = (raw ?? "").TrimEnd();
                string trimmed = one.Trim();
                if (trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    string heading = trimmed.TrimStart('#').Trim();
                    if (title == null && !trimmed.StartsWith("##", StringComparison.Ordinal)) title = one;
                    senpaiHere = Legacy.Heading(heading);
                    if (senpaiHere) any = true;
                }
                if (!senpaiHere && SenpaiScore(one)) { scored.Add(one); continue; }
                (senpaiHere ? theirs : mine).Add(one);
            }
            plain = string.Join(Environment.NewLine, Trimmed(mine).ToArray());
            if (!any && scored.Count == 0) return;
            List<string> made = new List<string>();
            if (title != null) { made.Add(title); made.Add(""); }
            made.AddRange(Trimmed(theirs));
            if (scored.Count > 0)
            {
                made.Add("## " + Senpai(LikesHeading));
                made.Add("");
                made.AddRange(scored);
                made.Add("");
            }
            senpai = string.Join(Environment.NewLine, made.ToArray());
        }

        // A score line for an act that only exists on the senpai side, and nothing else.
        private static bool SenpaiScore(string row)
        {
            Match named = Regex.Match(row ?? "", @"^[ \t]*([A-Za-z][A-Za-z ]*?)[ \t]*:[ \t]*[-+]?[0-9]+[ \t\r]*$");
            return named.Success && SenpaiSet(named.Groups[1].Value.Trim().ToLowerInvariant());
        }

        private static List<string> Trimmed(List<string> rows)
        {
            while (rows.Count > 0 && rows[rows.Count - 1].Trim().Length == 0) rows.RemoveAt(rows.Count - 1);
            rows.Add("");
            return rows;
        }

        // Acts that only exist with the switch on. Their score lines sit under "What she likes" with all the
        // others, which is a plain heading, so without this they landed in the censored half of every pack.
        internal static bool SenpaiSet(string named)
        {
            return Array.IndexOf(Extended.List("acts"), named) >= 0;
        }

        // The reaction sets, by the heading they are written under. The names on the left are used in code.
        internal static readonly string[,] Sets =
        {
            { "petted", "When you pet her" },
            { "kissed", "When you kiss her" },
            { "fed", "When you feed her" },
            { "treat", "When you give her a treat" },
            // Her Tamagotchi gifts, and the food only Emo and Bratty are offered.
            { "trinket", "When you give her a trinket" },
            { "flowers", "When you give her flowers" },
            { "plushie", "When you give her a plushie" },
            { "gift", "When you give her a gift" },
            { "pet rock", "When you give her a pet rock" },
            { "nuggies", "When you give her chimpken nuggies" },
            // Her hunger nag in her own words. Only Emo and Bratty ship these, asking for nuggies; the rest use the shared ones.
            { "peckish", "When she is peckish" },
            { "hungry", "When she is hungry" },
            { "uppies", "When you pick her up" },
            { "thrown", "When you throw her" },
            { "warned", "When you throw her again" },
            { "dizzy", "When she is dizzy" },
            { "sulking", "When she is sulking" },
            { "dodging", "When she will not be touched" },
            { "forgiven", "When you say sorry" },
            { "patted better", "When a pat wins her back" },
            { "missed you", "When you come back" },
            // Her sulk wearing off while they are still here: over it, never a welcome back.
            { "over it", "When she gets over it" },
            // Her pout when she sulks from being left alone, apart from the thrown lines: being ignored is not
            // being thrown, and "don't touch me" said to someone who has not touched her reads wrong.
            { "pouting", "When you ignore her" },
            // Her brain sent nothing she can say, even asked again.
            { "brain empty", "When her brain comes up empty" },
            // What this character is into, for the weave. A pack that says gets its own; one that does not
            // falls back to the shared list, which is the same for everyone and so suits nobody in particular.
            { "into", "What she is into" },
            // Her check-ins, under the same headings check-ins.md uses. A personality that has them nags in its own
            // voice; one that does not falls back to check-ins.md, which is the shared default.
            { "Water", "Water" },
            { "Stretch", "Stretch" },
            { "Look away", "Look away" },
            { "Sleep nudge", "Sleep nudge" },
            { "Meals", "Meals" },
            { "Game break", "Game break" },
            { "Bored", "Bored" },
            { "Bored noticed", "Bored noticed" },
            { "Bored ignored", "Bored ignored" }
        };

        // The heading a reaction set is written under, or null for one with none.
        internal static string SetHeading(string set)
        {
            for (int at = 0; at < Sets.GetLength(0); at++)
                if (string.Equals(Sets[at, 0], set, StringComparison.OrdinalIgnoreCase)) return Sets[at, 1];
            return null;
        }

        // What moves her, and by how much. The same act has to pull opposite ways for different temperaments: being
        // thrown delights one pack and genuinely upsets a devoted one, so a single global score could never work.
        // That is why these live in her personality rather than in one table.
        internal const string LikesHeading = "What she likes";
        internal const string WordsHeading = "Words she likes";
        internal const string DriftHeading = "Drifts to";
        internal const string ReactsHeading = "How she reacts";
        // The headings of topics.md she leans to when she picks her own topics, and how much ("anime: 3").
        internal const string TopicsHeading = "Topics she leans to";
        internal const string PetHeading = "Pet names";
        internal const string WantsHeading = "When she wants attention";
        internal const string HelloHeading = "When she starts up";
        internal const string ByeHeading = "When she says goodbye";
        // Settings she turns on while she is this way: a Dom nags you to drink, a jealous mood watches other AIs.
        // Same key and value shape as How she reacts, read into the same table.
        internal const string SwitchHeading = "She switches on";
        // How fast each of her six needs runs down an hour while a pet game runs, and where low and high start; read into
        // the same table, a mood's numbers added on top.
        internal const string NeedsHeading = "Her needs";
        internal static readonly string[] NeedKeys = { "energy falls", "fullness falls", "stimulation falls", "social falls", "comfort falls", "cleanliness falls", "needs low", "needs high", "needs gain", "energy rests", "closed falls" };

        // Each pack's and mood's stock needs, in NeedKeys' order ("" for none): laid into its file under Her needs once.
        private static readonly Dictionary<string, string[]> stockNeeds = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> senpaiNeeds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> moodNeeds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal static void StockNeeds(string named, bool mood, bool senpai, params string[] values)
        {
            lock (stockNeeds)
            {
                stockNeeds[named] = values;
                if (senpai) senpaiNeeds.Add(named); else senpaiNeeds.Remove(named);
                if (mood) moodNeeds.Add(named); else moodNeeds.Remove(named);
            }
        }

        // A pack's or mood's stock number for one need, or the usual when it has none.
        internal static double StockNeed(string named, string key, double usual)
        {
            int at = Array.IndexOf(NeedKeys, key);
            string[] values;
            double read;
            lock (stockNeeds)
                if (at >= 0 && named != null && stockNeeds.TryGetValue(named, out values) && at < values.Length && Number(values[at], out read)) return read;
            return usual;
        }

        // The stock needs as lines to lay, for the packs or the moods of one side.
        private static string[,] NeedsGrid(bool senpai, bool mood)
        {
            List<string[]> rows = new List<string[]>();
            lock (stockNeeds)
                foreach (KeyValuePair<string, string[]> one in stockNeeds)
                {
                    // The senpai folder holds the gated packs and the senpai halves of plain ones, so it takes them all.
                    if ((!senpai && senpaiNeeds.Contains(one.Key)) || moodNeeds.Contains(one.Key) != mood) continue;
                    for (int at = 0; at < NeedKeys.Length && at < one.Value.Length; at++)
                        if (!string.IsNullOrEmpty(one.Value[at])) rows.Add(new[] { one.Key, NeedsHeading, NeedKeys[at] + ": " + one.Value[at] });
                }
            string[,] grid = new string[rows.Count, 3];
            for (int at = 0; at < rows.Count; at++) for (int col = 0; col < 3; col++) grid[at, col] = rows[at][col];
            return grid;
        }

        // Sulking after being thrown is built for throwing being unwelcome, so its numbers are each personality's to
        // set. Her own values are the stock ones.
        private static readonly string[,] OwnReacts =
        {
            { "sulks after", "3" },      // throws inside two minutes before she sulks. 0 means never.
            { "dodges", "yes" },         // hops away from a cursor coming at her while sulking
            { "sulk alone", "300" },     // seconds of being left alone that settles her
            // Anything she dislikes adds to an upset tally, and this is where she has had enough, so insulting or
            // ignoring can sulk her like throwing does. 0 means only throwing can; throwing has its own count above.
            { "sulks at upset", "6" },
            // How often flirting turns up in ordinary lines, 0 (never) to 10; each personality sets its own and a mood
            // adds to it. Still read under its old name, interest.
            { "flirt", "2" },
            // Ignored for this long and she gives up and closes herself. 0 is off, and it is off for everyone by
            // default: it only ever runs with the switch on and a mood that asks for it.
            { "gives up at", "0" },
            { "gives up minutes", "45" }
        };

        private static readonly Dictionary<string, string> reacts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // "Dynamic" is not a personality file. It means: read dynamic.md for who she is underneath, and layer today's
        // attitude on top. An attitude shifts what she wants rather than replacing her, so its weights add to hers.
        internal const string Dynamic = "Dynamic";
        internal static string Attitude = "";

        private static readonly Dictionary<string, string[]> aLoaded = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> aLikes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<KeyValuePair<string, int>> aWords = new List<KeyValuePair<string, int>>();
        private static readonly List<Drift> aDrifts = new List<Drift>();
        private static readonly Dictionary<string, string> aReacts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static string aActive = "";
        private static long aWas = -1;

        // The personality whose file is actually read: hers, or the one dynamic.md names underneath.
        internal static string Under
        {
            // A personality with no lines of its own reads the one its state borrows (Tilt.Borrowed).
            get { return HeldPlain ? Stock : Chosen == Dynamic ? (Dynamics.Under ?? Stock) : CapriDesktop.Tilt.Borrowed(Chosen) ?? Chosen; }
        }

        // For her checks: while one runs she is her own character with no attitude on top, so a mood that rolls or
        // sours partway through cannot change how she reacts under it.
        internal static volatile bool HeldPlain;

        private static void FreshAttitude()
        {
            // A mood sits on top of whoever she is, not only on Dynamic. Dynamic is just the mode that picks one on
            // its own; a fixed personality can still be sleepy or bratty for an afternoon.
            string named = HeldPlain ? "" : Attitude;
            string path = string.IsNullOrEmpty(named) ? null : Dynamics.PathOf(named);
            try
            {
                // Asked on every reaction, so whether the file is there and changed comes from memory (Stamps).
                long stamp = path == null ? 0 : Stamps.Of(path);
                if (stamp != 0) stamp ^= ((long)FileSlots.Generation << 56) ^ ((long)OwnLayerVersion << 40);
                if (stamp == 0)
                {
                    lock (gate)
                    {
                        if (aActive.Length == 0 && aLoaded.Count == 0 && aLikes.Count == 0) return;
                        aLoaded.Clear(); aLikes.Clear(); aWords.Clear(); aDrifts.Clear(); aReacts.Clear();
                        aActive = ""; aWas = -1;
                    }
                    return;
                }
                lock (gate) { if (named == aActive && stamp == aWas) return; }
                Dictionary<string, int> likesIn = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                List<KeyValuePair<string, int>> wordsIn = new List<KeyValuePair<string, int>>();
                List<Drift> driftsIn = new List<Drift>();
                Dictionary<string, string> reactsIn = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                Dictionary<string, string[]> linesIn = Read(FileSlots.ReadLines(path), likesIn, wordsIn, driftsIn, reactsIn);
                Overlay(named, path, likesIn, reactsIn);
                lock (gate)
                {
                    aLoaded.Clear(); aLikes.Clear(); aWords.Clear(); aDrifts.Clear(); aReacts.Clear();
                    foreach (KeyValuePair<string, string[]> one in linesIn) aLoaded[one.Key] = one.Value;
                    foreach (KeyValuePair<string, int> one in likesIn) aLikes[one.Key] = one.Value;
                    aWords.AddRange(wordsIn);
                    aDrifts.AddRange(driftsIn);
                    foreach (KeyValuePair<string, string> one in reactsIn) aReacts[one.Key] = one.Value;
                    aActive = named; aWas = stamp;
                }
                // A mood that has just rolled on this personality: a blend for the pair can be asked for.
                Action<string, string> moodOn = MoodOn;
                if (moodOn != null) try { moodOn(Under, named); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // Where she goes when one thing keeps happening: "petted over 20: Bratty" means twenty net points of petting
        // turn a devoted one bratty. The tallies decay, so a drift is about a stretch, not a lifetime total.
        private static readonly List<Drift> drifts = new List<Drift>();

        internal sealed class Drift
        {
            internal string Act;
            internal int Over;
            // The second mark, past which she moves at once whatever the window says: written after the first as ", now
            // M", or 0 to work it out from how fast the act can land (NowMark).
            internal int Now;
            internal string Into;
        }

        // Her own, used when no personality file is chosen.
        private static readonly string[,] OwnWords =
        {
            { "good girl", "1" }, { "little one", "1" }, { "clever", "1" }, { "thank you", "1" }
        };

        private static readonly object gate = new object();
        private static readonly Dictionary<string, string[]> loaded = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> likes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<KeyValuePair<string, int>> words = new List<KeyValuePair<string, int>>();
        private static string active = "";
        private static long wasStamp = -1;
        // Whether her senpai copy was the one read: flipping the toggle reloads her lines even though no file changed.
        private static bool wasSenpaiLive;

        // Her own, first and default. "Her own" means her stock lines (Lachy), so picking it changes nothing at all.
        internal const string Stock = "Her own";

        // What a new install starts on. Stock stays exactly as it was, on the menu and the default for anyone
        // who already had it: this only decides where somebody begins when they have never picked.
        internal const string FirstPick = "Welcome Home";

        // Her shipped personalities from gentlest to harshest, which is the order the menu offers them in. Alphabetical
        // ordered them by their first letter and told nobody anything. Two of them sit next to each other on
        // purpose: same authority, opposite warmth.
        internal static readonly string[] Softest =
        {
            "Caregiver", "Welcome Home", "Devoted", "Emo", "Bratty", "Sassy", "Feral"
        };

        // The order with whatever attached folded into it, so the menu reads gentlest to harshest rather
        // than putting everything this build does not ship at the end. Nothing attached gives the seven
        // above and nothing else, which is the order a censored install wants.
        internal static Func<string[]> OrderFor;

        internal static string[] Order()
        {
            if (OrderFor == null) return Softest;
            try { return OrderFor() ?? Softest; }
            catch (Exception) { return Softest; }
        }

        private static int Rank(string named)
        {
            string[] order = Order();
            for (int at = 0; at < order.Length; at++)
                if (string.Equals(order[at], named, StringComparison.OrdinalIgnoreCase)) return at;
            // Theirs, so it goes after the ones that ship, in the order the folder gave them.
            return order.Length;
        }

        internal static string[] Names
        {
            get
            {
                // Dynamic is offered whether or not anything else exists: it is a mode, not a file.
                List<string> found = new List<string> { Stock, Dynamic };
                try
                {
                    // The plain folder is the list. A pack whose only file is in the senpai folder is senpai as a
                    // whole and joins the list with the toggle on, which is the same rule as before: what changed
                    // is that the marker is now the folder it is in.
                    foreach (string file in Listed())
                    {
                        // What the file says it is, which is how a numbered one is known. Her plain packs open
                        // with the same line, so this is one rule for both.
                        string named = Slots.Titled(file) ?? Path.GetFileNameWithoutExtension(file);
                        if (named.Trim().Length == 0 || named.Equals(Stock, StringComparison.OrdinalIgnoreCase)
                            || named.Equals(Dynamic, StringComparison.OrdinalIgnoreCase)
                            || found.Contains(named)) continue;
                        // Same convention as the moods: a personality can also say Legacy.OnlyKey + ": yes" and
                        // stays out of the list until the toggle is on.
                        if (!Brain.SenpaiOn && (InSenpai(file) || SenpaiOnly(file))) continue;
                        found.Add(named);
                    }
                }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                // Stock and Dynamic stay at the top; the rest are offered gentlest first.
                if (found.Count > 2)
                {
                    List<string> rest = found.GetRange(2, found.Count - 2);
                    rest.Sort(delegate(string left, string right)
                    {
                        int by = Rank(left).CompareTo(Rank(right));
                        return by != 0 ? by : string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
                    });
                    found.RemoveRange(2, found.Count - 2);
                    found.AddRange(rest);
                }
                return found.ToArray();
            }
        }

        private static readonly Dictionary<string, KeyValuePair<DateTime, bool>> senpaiOnly =
            new Dictionary<string, KeyValuePair<DateTime, bool>>(StringComparer.OrdinalIgnoreCase);

        internal static bool SenpaiOnly(string path)
        {
            try
            {
                FileInfo about = new FileInfo(path);
                if (!about.Exists) return false;
                KeyValuePair<DateTime, bool> had;
                if (senpaiOnly.TryGetValue(path, out had) && had.Key == about.LastWriteTimeUtc) return had.Value;
                bool only = Marked(Notes.ReadAll(path));
                senpaiOnly[path] = new KeyValuePair<DateTime, bool>(about.LastWriteTimeUtc, only);
                return only;
            }
            catch (Exception) { return false; }
        }

        internal static string Chosen = Stock;

        // Her plain file for a pack, or its senpai one when that is all it has.
        // ---------- Two whole copies ----------
        // Each side keeps a whole copy of every pack, read one folder at a time so neither can reach the other.

        private const string WholeNote = "whole.txt";

        // A pack's whole text: the safe one, with the senpai half's sections added. A section the half has without
        // a tag takes the place of the safe section of the same name, which is how the senpai copy says something
        // different from the safe one, a drift for instance.
        internal static string Whole(string plain, string half)
        {
            if (string.IsNullOrEmpty(half)) return plain;
            if (string.IsNullOrEmpty(plain)) return half;
            string title;
            List<KeyValuePair<string, List<string>>> mine = Sectioned(plain, out title), theirs = Sectioned(half, out title);
            foreach (KeyValuePair<string, List<string>> one in theirs)
            {
                if (one.Key == null) continue;
                string bare;
                int same = Legacy.Heading(one.Key.TrimStart('#').Trim(), out bare) ? -1
                    : mine.FindIndex(each => each.Key != null && string.Equals(each.Key.Trim(), one.Key.Trim(), StringComparison.OrdinalIgnoreCase));
                if (same >= 0) mine[same] = one;
                else mine.Add(one);
            }
            List<string> rows = new List<string>();
            foreach (KeyValuePair<string, List<string>> one in mine)
            {
                if (one.Key != null) rows.Add(one.Key);
                rows.AddRange(Trimmed(new List<string>(one.Value)));
            }
            return string.Join("\n", rows.ToArray()).TrimEnd() + "\n";
        }

        // A file cut into its sections by their ## headings. What comes before the first one, the title and its
        // note, has no heading.
        private static List<KeyValuePair<string, List<string>>> Sectioned(string text, out string title)
        {
            title = null;
            List<KeyValuePair<string, List<string>>> found = new List<KeyValuePair<string, List<string>>>();
            KeyValuePair<string, List<string>> cur = new KeyValuePair<string, List<string>>(null, new List<string>());
            foreach (string raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string row = raw.TrimEnd();
                if (row.StartsWith("##", StringComparison.Ordinal))
                {
                    found.Add(cur);
                    cur = new KeyValuePair<string, List<string>>(row, new List<string>());
                    continue;
                }
                if (title == null && row.StartsWith("# ", StringComparison.Ordinal)) title = row;
                cur.Value.Add(row);
            }
            found.Add(cur);
            return found;
        }

        // The files of the side in use: the senpai folder's with the switch on, then any safe pack it has no copy
        // of yet; the censored folder's otherwise.
        private static List<string> Listed()
        {
            List<string> found = new List<string>();
            if (!Brain.SenpaiOn || string.IsNullOrEmpty(Senpais) || !Directory.Exists(Senpais))
            {
                if (!string.IsNullOrEmpty(Plain) && Directory.Exists(Plain)) found.AddRange(Packed(Plain));
                return found;
            }
            HashSet<string> titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in Packed(Senpais)) { found.Add(file); titles.Add(Slots.Titled(file) ?? Path.GetFileNameWithoutExtension(file)); }
            if (!string.IsNullOrEmpty(Plain) && Directory.Exists(Plain))
                foreach (string file in Packed(Plain)) if (!titles.Contains(Slots.Titled(file) ?? Path.GetFileNameWithoutExtension(file))) found.Add(file);
            return found;
        }

        // The pack she reads: the senpai folder's whole copy while the switch is on and she is off air, the
        // censored one otherwise.
        internal static string PathOf(string named)
        {
            string path = PlainPathOf(named);
            try
            {
                if (Brain.SenpaiLive)
                {
                    string senpai = Slots.Find(Senpais, named);
                    if (senpai != null) return senpai;
                }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            return path;
        }

        internal static string PlainPathOf(string named) { return Named(Plain, named); }

        // Numbered on disk and found by the title inside, so a listing of the folder says nothing about
        // who any of them are. See Slots for why the title is the identity rather than a mapping file.
        internal static string SenpaiPathOf(string named) { return Slots.Path(Senpais, named); }

        private static string Named(string folder, string named)
        {
            if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(named)) return null;
            // Dynamic and Her own are modes, not files. Handing back a path for them let callers create empty stubs
            // in the folder, which then listed themselves as personalities of their own.
            if (string.Equals(named, Dynamic, StringComparison.OrdinalIgnoreCase)) return null;
            foreach (char bad in Path.GetInvalidFileNameChars()) if (named.IndexOf(bad) >= 0) return null;
            return Path.Combine(folder, named + ".md");
        }

        internal static WrittenNote Open()
        {
            string path = PathOf(Chosen);
            if (path == null || Chosen == Stock) throw new IOException("Her own lines are built in. Pick another personality to edit one.");
            return new WrittenNote(path, Written(Chosen), gate);
        }

        // Mood blends (Blends.cs): lines in a personality's voice coloured by today's mood. Blended answers
        // personality, mood and reaction with lines or null; MoodOn hears a mood roll so a blend can be asked for. Both
        // null without them.
        internal static Func<string, string, string, string[]> Blended;
        internal static Action<string, string> MoodOn;

        // The headings a mood file can give one reaction for one personality: "petted when Dominant", or with the
        // reaction's own heading, "When you pet her when Dominant".
        internal static IEnumerable<string> Pairings(string set, string personality)
        {
            if (string.IsNullOrEmpty(personality)) yield break;
            yield return set + " when " + personality;
            for (int at = 0; at < Sets.GetLength(0); at++)
                if (string.Equals(Sets[at, 0], set, StringComparison.OrdinalIgnoreCase)) yield return Sets[at, 1] + " when " + personality;
        }

        // What a blend is made from: for each reaction both the personality and today's mood have lines for, the two
        // sets of lines, and the personality's character. Never the senpai sets.
        internal static Dictionary<string, KeyValuePair<string[], string[]>> BlendSources(out string character)
        {
            Dictionary<string, KeyValuePair<string[], string[]>> found = new Dictionary<string, KeyValuePair<string[], string[]>>(StringComparer.OrdinalIgnoreCase);
            character = null;
            FreshAttitude();
            if (Under != Stock) Fresh();
            lock (gate)
            {
                string[] prose;
                if (loaded.TryGetValue("Her character", out prose) && prose != null && prose.Length > 0) character = string.Join(" ", prose);
                foreach (KeyValuePair<string, string[]> mood in aLoaded)
                {
                    if (mood.Value == null || mood.Value.Length == 0 || mood.Key.EndsWith(LineFile.SenpaiMark, StringComparison.Ordinal) || mood.Key.IndexOf(" when ", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (Array.IndexOf(NotLines, mood.Key) >= 0 || IsPart(mood.Key)) continue;
                    string[] own;
                    if (!loaded.TryGetValue(mood.Key, out own) || own == null) own = new string[0];
                    found[mood.Key] = new KeyValuePair<string[], string[]>(own, mood.Value);
                }
            }
            return found;
        }

        // A mood's openers and tails: pieces put round a line, not a reaction of their own.
        internal const string OpenersHeading = "Openers", TailsHeading = "Tails";
        internal static bool IsPart(string heading)
        {
            return string.Equals(heading, OpenersHeading, StringComparison.OrdinalIgnoreCase) || string.Equals(heading, TailsHeading, StringComparison.OrdinalIgnoreCase);
        }

        private static string[] Filled(string[] said)
        {
            string[] done = new string[said.Length];
            for (int one = 0; one < said.Length; one++) done[one] = Them.Fill(said[one]);
            return done;
        }

        // The senpai variant of a reaction's key.

        // What she calls them, from her personality and from today's attitude. Raw: this must not go through
        // Them.Fill, because Fill is what asks for one of these, and it would call back into here for ever.

        internal static string[] PetNames()
        {
            List<string> found = new List<string>();
            FreshAttitude();
            lock (gate)
            {
                string[] mood;
                if (aLoaded.TryGetValue(PetHeading, out mood) && mood != null) found.AddRange(mood);
            }
            if (Under != Stock)
            {
                Fresh();
                lock (gate)
                {
                    string[] said;
                    if (found.Count == 0 && loaded.TryGetValue(PetHeading, out said) && said != null) found.AddRange(said);
                }
            }
            for (int at = found.Count - 1; at >= 0; at--)
                if (string.IsNullOrWhiteSpace(found[at]) || found[at].TrimStart().StartsWith(">", StringComparison.Ordinal)) found.RemoveAt(at);
            return found.ToArray();
        }

        internal static string Senpai(string set) { return set + LineFile.SenpaiMark; }

        // The lines for one reaction. Their personality's if it has any, else her stock ones.
        internal static string[] Lines(string set, string[] hers)
        {
            if (string.IsNullOrEmpty(set)) return hers;
            // Today's attitude speaks first when it has something for this reaction: a section written for this
            // personality in this mood ("## petted when Dominant"), then its senpai lines, then a blend her brain wrote of
            // this personality's voice in this mood (Blended), then the mood's own lines.
            FreshAttitude();
            moodWritten = false;
            string under = Under, active;
            lock (gate)
            {
                string[] mood;
                active = aActive;
                // Each pool named for her line log: "mood Clingy: petted when Devoted", "blend Devoted when Clingy: petted".
                foreach (string pairing in Pairings(set, under))
                {
                    if (Brain.SenpaiLive && aLoaded.TryGetValue(Senpai(pairing), out mood) && mood != null && mood.Length > 0) { moodWritten = true; return Sayings.Named(Filled(mood), "mood " + active + " (extended): " + pairing); }
                    if (aLoaded.TryGetValue(pairing, out mood) && mood != null && mood.Length > 0) { moodWritten = true; return Sayings.Named(Filled(mood), "mood " + active + ": " + pairing); }
                }
                if (Brain.SenpaiLive && aLoaded.TryGetValue(Senpai(set), out mood) && mood != null && mood.Length > 0) return Sayings.Named(Filled(mood), "mood " + active + " (extended): " + set);
            }
            Func<string, string, string, string[]> blended = Blended;
            if (blended != null && active.Length > 0)
            {
                string[] blend = null;
                try { blend = blended(under, active, set); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                if (blend != null && blend.Length > 0) { moodWritten = true; return Sayings.Named(Filled(blend), "blend " + under + " when " + active + ": " + set); }
            }
            // No blend: the mood's own lines and their personality's share one pool, the mood's coming up as often as
            // mood share in dynamic.md says. Her personality's lines keep coming, so a mood never pushes it out.
            string[] moodOwn = null;
            lock (gate)
            {
                string[] mood;
                if (aLoaded.TryGetValue(set, out mood) && mood != null && mood.Length > 0) moodOwn = Sayings.Named(Filled(mood), "mood " + active + ": " + set);
            }
            string[] own = Own(set, hers);
            // Her pack's own lines, or the ones in code when the pack has none for this.
            Sayings.Named(own, ReferenceEquals(own, hers) ? "code: " + set : Chosen + ": " + set);
            string[] pool = moodOwn == null ? own : Mixed(moodOwn, own, Dynamics.MoodShare);
            Func<string, string, string, string[], string[]> grown = Grown;
            if (grown != null)
                try { pool = grown(under, active, set, pool); }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            return pool;
        }

        // Better conversations (Keyless.cs): lines grown for this personality, mood and reaction, with hers once she has gone
        // round hers. Null, or nothing set, leaves hers as they are.
        internal static Func<string, string, string, string[], string[]> Grown;

        // Set by Lines: the lines it gave were written for this personality in this mood, by hand or by her brain, so the
        // mood's openers and tails are not put round them again.
        [ThreadStatic] private static bool moodWritten;

        // One of today's mood's openers or tails ("Openers", "Tails"), for her lines when no blend wrote them, as often as
        // mood share says. Null when the mood has none, when it is not its turn, or when the line was written for the mood.
        internal static string MoodPart(string kind, Random dice)
        {
            if (moodWritten || dice == null) return null;
            FreshAttitude();
            string[] parts;
            lock (gate)
            {
                if (aActive.Length == 0 || !aLoaded.TryGetValue(kind, out parts) || parts == null || parts.Length == 0) return null;
            }
            if (dice.Next(100) >= Dynamics.MoodShare) return null;
            string one = parts[dice.Next(parts.Length)];
            return string.IsNullOrWhiteSpace(one) || one.TrimStart().StartsWith(">", StringComparison.Ordinal) ? null : one.Trim();
        }

        // The mood's lines and their personality's in one pool, each line copied so a mood line comes up about share times
        // in a hundred. The same pool every time for the same lines, so a line picked from it is always found in it again.
        internal static string[] Mixed(string[] mood, string[] own, int share)
        {
            if (own == null || own.Length == 0 || share >= 100) return mood;
            if (mood == null || mood.Length == 0 || share <= 0) return own;
            double want = share / 100.0, missed = double.MaxValue;
            int moodCopies = 1, ownCopies = 1;
            for (int m = 1; m <= 10; m++)
                for (int o = 1; o <= 10; o++)
                {
                    double off = Math.Abs((double)(m * mood.Length) / (m * mood.Length + o * own.Length) - want);
                    if (off < missed - 1e-9) { missed = off; moodCopies = m; ownCopies = o; }
                }
            List<string> pool = new List<string>(moodCopies * mood.Length + ownCopies * own.Length);
            for (int m = 0; m < moodCopies; m++) pool.AddRange(mood);
            for (int o = 0; o < ownCopies; o++) pool.AddRange(own);
            return pool.ToArray();
        }

        // Their personality's lines for one reaction, or hers.
        private static string[] Own(string set, string[] hers)
        {
            if (Under == Stock) return hers;
            Fresh();
            lock (gate)
            {
                string[] said;
                // With the toggle on, her senpai lines for this reaction win. Anything she has no senpai lines
                // for simply uses her usual ones, so the toggle never leaves her with nothing to say.
                if (Brain.SenpaiLive && loaded.TryGetValue(Senpai(set), out said) && said != null && said.Length > 0)
                {
                    string[] rude = new string[said.Length];
                    for (int one = 0; one < said.Length; one++) rude[one] = Them.Fill(said[one]);
                    return rude;
                }
                if (loaded.TryGetValue(set, out said) && said != null && said.Length > 0)
                {
                    // Tokens like %boy% are filled in for whoever she is talking to, from me.md.
                    string[] filled = new string[said.Length];
                    for (int one = 0; one < said.Length; one++) filled[one] = Them.Fill(said[one]);
                    return filled;
                }
            }
            return hers;
        }

        // How much an act moves her: positive enjoyed, negative not; an unknown act moves nothing. Her forwardness in
        // words, told to her brain too, since a small brain needs to hear what it may do, not only what it must not.
        internal static string Forward
        {
            get
            {
                int level;
                try { level = SpecialInterest; } catch (Exception) { return null; }
                string said;
                // Even at nothing she does not take offence at being asked: a flat level is a preference of hers, not a
                // rule they broke, and snapping at them for asking is what made the whole setting feel punitive.
                if (level <= 0) said = "You do not go there at all. If they bring it up, say plainly that it is not something you are up for and move the talk on. You are not annoyed with them for asking and you do not scold them for it.";
                else if (level <= 2) said = "You rarely go there. A stray innuendo at most, and you move on quickly.";
                else if (level <= 4) said = "You will flirt and you enjoy it, but you stop short of saying anything explicit.";
                else if (level <= 6) said = "You are openly flirty and you say what you want without much hedging.";
                else if (level <= 8) said = "You are forward and direct about wanting them. You start it as often as they do, and you do not talk around it.";
                else said = "You are as flirty as you get with them. You flirt freely and openly, you start it yourself, and you never go coy or deflect.";
                string answer = level >= 3
                    ? " When they ask you directly about what you like, what you are into or what you want, answer them properly in your own voice. Do not dodge the question, change the subject or tell them you would rather not say."
                    : "";
                // Forwardness is a dial on who she already is, not a different person: a prickly one at ten is a
                // prickly one who wants you and hates admitting it.
                string self = level >= 5
                    ? " However forward you are, stay the one you are: your personality above decides how you say it, and this only decides how much you hold back."
                    : "";
                // She invented a grievance and accused them of it. Her general rules already say not to make things
                // up, and plainly they are not enough once she is being forward and cross at the same time.
                string real = level >= 3
                    ? " Never accuse them of something they have not done, and never refer to a conversation, a promise or an event that has not actually happened."
                    : "";
                return "How forward you are, which you never mention as a number or a score: " + said + answer + self + real;
            }
        }

        // Her own score for an act when nothing names it, and for a senpai one, what attached says.
        private static int Own(string act)
        {
            int yours;
            if (int.TryParse(OwnValue("", LikesHeading, act) ?? "", out yours)) return Math.Max(-5, Math.Min(5, yours));
            int own = Acts.LikeOf(act);
            return own != 0 || !SenpaiSet(act) ? own : Extended.Int("own." + act, 0);
        }

        internal static int Likes(string act) { return Likes(act, null, null, 0); }

        // The same, with each part named in sum as it adds: her personality's number, or the default when it names
        // none, then today's mood's, then one more add, like her hunger for a food, under its own name.
        internal static int Likes(string act, Acts.Sum sum, string added, int add)
        {
            if (string.IsNullOrEmpty(act)) return 0;
            // Being carried is new, so no personality file mentions it. Rather than reading 0 for all of them it
            // follows how she feels about being petted: one who likes being handled fondly likes being picked up.
            // A file that does say uppies wins, as always.
            if (string.Equals(act, "uppies", StringComparison.OrdinalIgnoreCase) && PersonalityReact("uppies") == null)
            {
                int carried = Math.Max(0, Likes("petted") - 1);
                if (sum != null) sum.Term("as petted", carried);
                return carried;
            }
            int mine;
            string whose = Stock;
            // A food a file doesn't name takes the file's "fed" or "treat", as files did before each food was its own.
            Acts.Act listed = Acts.Find(act);
            string group = listed != null && listed.Group != listed.Name ? listed.Group : null;
            if (Under == Stock) mine = Own(act);
            else
            {
                Fresh();
                whose = Under;
                lock (gate)
                {
                    if (!likes.TryGetValue(act, out mine) && !(Brain.SenpaiLive && Extended.Score(Under, act, out mine)))
                    {
                        if (group != null && (likes.TryGetValue(group, out mine) || Brain.SenpaiLive && Extended.Score(Under, group, out mine))) whose = Under + " " + group;
                        else { mine = Own(act); whose = "default"; }
                    }
                }
            }
            if (sum != null) sum.Term(whose, mine);
            // An attitude shifts what she wants, it does not replace it. Sleepy makes attention unwelcome whoever she
            // is, and a pack that likes being thrown still enjoys it underneath, because the two numbers add.
            FreshAttitude();
            lock (gate)
            {
                int shift;
                if (aLikes.TryGetValue(act, out shift) || (Brain.SenpaiLive && Extended.Score(aActive, act, out shift))
                    || group != null && aLikes.TryGetValue(group, out shift))
                {
                    mine += shift;
                    if (sum != null) sum.Shift(aActive, shift);
                }
            }
            if (added != null)
            {
                mine += add;
                if (sum != null) sum.Shift(added, add);
            }
            // A pack's mood terms for the act, each under the pack's name; one with a who only while she is one of them.
            string who = Under, moodNow;
            lock (gate) moodNow = aActive;
            foreach (KeyValuePair<string, int> term in Acts.Terms(act, false, who, moodNow))
            {
                mine += term.Value;
                if (sum != null) sum.Shift(term.Key, term.Value);
            }
            // A code mod's multipliers, after every add.
            foreach (KeyValuePair<string, double> times in Acts.Multipliers(act, false, who, moodNow))
            {
                mine = (int)Math.Round(mine * times.Value, MidpointRounding.AwayFromZero);
                if (sum != null) sum.Then(times.Key + " x" + Acts.N(times.Value), mine);
            }
            int held = Math.Max(-5, Math.Min(5, mine));
            if (sum != null) sum.Held(held);
            return held;
        }

        // What their words were worth to her. Every phrase of hers that appears is counted, so "good girl, little one"
        // lands twice, and a personality that dislikes a word is pulled down by it.
        internal static int Heard(string said)
        {
            if (string.IsNullOrEmpty(said)) return 0;
            string plain = said.ToLowerInvariant();
            int much = 0;
            if (Chosen != Stock)
            {
                Fresh();
                lock (gate)
                {
                    if (words.Count > 0)
                    {
                        foreach (KeyValuePair<string, int> one in words)
                            if (one.Key.Length > 0 && plain.IndexOf(one.Key, StringComparison.Ordinal) >= 0) much += one.Value;
                        return Math.Max(-5, Math.Min(5, much));
                    }
                }
            }
            for (int word = 0; word < OwnWords.GetLength(0); word++)
                if (plain.IndexOf(OwnWords[word, 0], StringComparison.Ordinal) >= 0) much += int.Parse(OwnWords[word, 1]);
            return Math.Max(-5, Math.Min(5, much));
        }

        // ---- drifting ----
        // Main is the personality they chose; Chosen drifts from it while one kind of treatment keeps up and comes back
        // after. A drift is never saved.
        internal static string Main = Stock;

        private static readonly Dictionary<string, double> tally = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private static DateTime cooled = SimTime.UtcNow;
        internal static DateTime DriftedAt = DateTime.MinValue;

        // How long a stretch of treatment stays relevant. Without this a tally only ever climbs and she ends up stuck
        // in whatever she drifted into on day one.
        private const double HalfLifeMinutes = 45;

        // Picking a personality by hand starts her fresh: the tallies belonged to the old one, and carrying them over
        // would drift her straight back out of the one just chosen.
        internal static void Forget()
        {
            lock (gate)
            {
                tally.Clear();
                cooled = SimTime.UtcNow;
            }
            DriftedAt = DateTime.MinValue;
        }

        internal static int Tally(string act)
        {
            lock (gate)
            {
                double has;
                return tally.TryGetValue(act, out has) ? (int)Math.Round(has) : 0;
            }
        }

        internal static void Cool()
        {
            lock (gate)
            {
                DateTime now = SimTime.UtcNow;
                double mins = (now - cooled).TotalMinutes;
                if (mins < 1) return;
                cooled = now;
                double keep = Math.Pow(0.5, mins / HalfLifeMinutes);
                List<string> names = new List<string>(tally.Keys);
                foreach (string one in names)
                {
                    double left = tally[one] * keep;
                    if (Math.Abs(left) < 0.5) tally.Remove(one); else tally[one] = left;
                }
            }
        }

        // Who she is and what each act has added up to, kept whole so a counter run can put it back.
        internal sealed class Kept
        {
            internal Dictionary<string, double> Tally;
            internal DateTime Cooled, DriftedAt;
            internal string Chosen, Attitude;
        }

        internal static Kept Keep()
        {
            lock (gate) return new Kept { Tally = new Dictionary<string, double>(tally, StringComparer.OrdinalIgnoreCase), Cooled = cooled, DriftedAt = DriftedAt, Chosen = Chosen, Attitude = Attitude };
        }

        // True when who she is changed since it was kept, so her caller puts her looks and voice back too.
        internal static bool PutBack(Kept kept)
        {
            if (kept == null) return false;
            bool moved;
            lock (gate)
            {
                tally.Clear();
                foreach (KeyValuePair<string, double> one in kept.Tally) tally[one.Key] = one.Value;
                cooled = kept.Cooled;
                DriftedAt = kept.DriftedAt;
                moved = Chosen != kept.Chosen || Attitude != kept.Attitude;
                Chosen = kept.Chosen;
                Attitude = kept.Attitude;
            }
            return moved;
        }

        // Records an act and returns who she should be now, or null for no change. Worth is already weighted by her
        // personality, so a pack that likes being thrown adds, and a devoted one being thrown subtracts.
        internal static string Took(string act, int worth)
        {
            if (string.IsNullOrEmpty(act)) return null;
            Cool();
            lock (gate)
            {
                double has;
                tally.TryGetValue(act, out has);
                tally[act] = Math.Max(-60, Math.Min(60, has + worth));
            }
            return Drifted();
        }

        // An attitude can drift to another attitude, which is a mood souring rather than her character changing:
        // clingy that keeps being ignored turns moody. Returns the new attitude, or null.
        internal static string Souring() { Drift rule = SouringRule(); return rule == null ? null : rule.Into; }

        // The mood line that would sour her now, or null.
        internal static Drift SouringRule()
        {
            if (Chosen != Dynamic) return null;
            FreshAttitude();
            List<Drift> rules;
            lock (gate) rules = new List<Drift>(aDrifts);
            foreach (Drift rule in rules)
            {
                if (Size(rule.Act) < rule.Over) continue;
                if (string.Equals(rule.Into, Attitude, StringComparison.OrdinalIgnoreCase)) continue;
                string path = Dynamics.PathOf(rule.Into);
                if (path != null && File.Exists(path)) return rule;
            }
            return null;
        }

        // How big a run of one act is, either way: "over N" is N points of that act, so one she dislikes moves her too.
        // Her mood's drift push scales it, a pushing mood making a run count for more and a steady one for less.
        internal static double Size(string act)
        {
            return Math.Abs(Tally(act)) * Math.Max(0.1, 1 + DriftPush);
        }

        // Today's mood's "drift push" under "## How she reacts", signed, two decimals; 0 with none.
        internal static double DriftPush { get { return Scaled("drift push", 0); } }

        // Past a line's second mark: she moves now, whatever the window.
        internal static bool PastNow(Drift rule)
        {
            if (rule == null) return false;
            int now = rule.Now > 0 ? rule.Now : NowMark(rule.Act, rule.Over, Math.Abs(Likes(rule.Act)));
            return Size(rule.Act) >= now;
        }

        private static readonly Dictionary<string, int> nowMarks = new Dictionary<string, int>(StringComparer.Ordinal);

        // A line's second mark when it writes none: where a steady run stands about six and a half minutes past its
        // first mark, given Mood.Counts and the tallies' decay, so it is reachable and never a grind.
        internal static int NowMark(string act, int over, int worth)
        {
            string key = act + "|" + over + "|" + worth;
            lock (gate) { int known; if (nowMarks.TryGetValue(key, out known)) return known; }
            int seconds, perHour;
            Mood.LimitOf(act, out seconds, out perHour);
            if (seconds <= 0) seconds = 10;
            if (perHour <= 0) perHour = 3600 / seconds;
            double tally = 0, cooledAt = 0, last = double.MinValue, firstAt = -1, best = 0;
            List<double> counts = new List<double>();
            int mark = over * 2;
            for (double t = 0; t < 3 * 3600 && worth > 0; t += 1)
            {
                if (t - cooledAt >= 60) { tally *= Math.Pow(0.5, (t - cooledAt) / 60 / HalfLifeMinutes); cooledAt = t; }
                counts.RemoveAll(delegate(double at) { return t - at >= 3600; });
                if (t - last >= seconds && counts.Count < perHour) { tally = Math.Min(60, tally + worth); last = t; counts.Add(t); }
                if (firstAt < 0 && tally >= over) firstAt = t;
                if (firstAt < 0) continue;
                if (t <= firstAt + 480) best = Math.Max(best, tally);
                if (t >= firstAt + 390) { mark = (int)tally > over ? (int)tally : (int)best; break; }
            }
            mark = Math.Max(over + 1, Math.Min(60, mark));
            lock (gate) nowMarks[key] = mark;
            return mark;
        }

        // A number from "## How she reacts": her personality's, with today's mood's added on top (a mood's is signed), to two
        // decimals; usual when her personality says nothing.
        internal static double Scaled(string named, double usual)
        {
            double mine = usual, add = 0, read;
            FreshAttitude();
            lock (gate)
            {
                string mood;
                if (aReacts.TryGetValue(named, out mood) && Number(mood, out read)) add = read;
            }
            if (Under != Stock)
            {
                Fresh();
                lock (gate)
                {
                    string said;
                    if (reacts.TryGetValue(named, out said) && Number(said, out read)) mine = read;
                }
            }
            return Math.Round(mine + add, 2);
        }

        // A range from "## How she reacts", like "repeat window: 20 to 60": her personality's, with today's mood's added
        // to each end in the same form; low and high when her personality says nothing. Never under 1, high never under low.
        internal static void Range(string named, int low, int high, out int from, out int to)
        {
            double[] mine = { low, high }, add = { 0, 0 };
            FreshAttitude();
            lock (gate)
            {
                string mood;
                if (aReacts.TryGetValue(named, out mood)) Pair(mood, add);
            }
            if (Under != Stock)
            {
                Fresh();
                lock (gate)
                {
                    string said;
                    if (reacts.TryGetValue(named, out said)) Pair(said, mine);
                }
            }
            from = Math.Max(1, (int)Math.Round(mine[0] + add[0]));
            to = Math.Max(from, (int)Math.Round(mine[1] + add[1]));
        }

        private static void Pair(string said, double[] into)
        {
            string[] ends = Regex.Split((said ?? "").Trim(), @"\s+to\s+", RegexOptions.IgnoreCase);
            double one, two;
            if (ends.Length == 2 && Number(ends[0], out one) && Number(ends[1].TrimStart('+'), out two)) { into[0] = one; into[1] = two; }
        }

        private static bool Number(string said, out double value)
        {
            return double.TryParse((said ?? "").Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        // The run that moved her eased off, so the line that moved her doesn't hold her there; the other runs are kept,
        // so a second push can still build.
        internal static void Ease(string act)
        {
            if (string.IsNullOrEmpty(act)) return;
            lock (gate)
            {
                double has;
                if (tally.TryGetValue(act, out has)) tally[act] = has / 2;
            }
        }

        // The first rule of Main past its mark, or null when none is and she belongs back with Main.
        internal static Drift DriftRule()
        {
            string was = Chosen;
            try
            {
                Chosen = Main;
                Fresh();
                List<Drift> rules;
                lock (gate) rules = new List<Drift>(drifts);
                foreach (Drift rule in rules)
                {
                    if (Size(rule.Act) < rule.Over) continue;
                    string path = PathOf(rule.Into);
                    if (rule.Into == Stock || (path != null && File.Exists(path))) return rule;
                }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            finally { Chosen = was; }
            return null;
        }

        // The personalities one's "Drifts to" lines name: joined to it, so they grow with it at half the weight (Growth.cs).
        internal static List<string> DriftsInto(string who)
        {
            List<string> into = new List<string>();
            if (string.IsNullOrEmpty(who) || who == Dynamic) return into;
            string was = Chosen;
            try
            {
                Chosen = who;
                Fresh();
                lock (gate) foreach (Drift rule in drifts) if (!into.Contains(rule.Into) && !string.Equals(rule.Into, who, StringComparison.OrdinalIgnoreCase)) into.Add(rule.Into);
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            finally { Chosen = was; }
            return into;
        }

        // The moods today's mood's "Drifts to" lines name, with Dynamic picked.
        internal static List<string> SoursInto()
        {
            List<string> into = new List<string>();
            if (Chosen != Dynamic) return into;
            FreshAttitude();
            lock (gate) foreach (Drift rule in aDrifts) if (!into.Contains(rule.Into) && !string.Equals(rule.Into, Attitude, StringComparison.OrdinalIgnoreCase)) into.Add(rule.Into);
            return into;
        }

        // The first rule whose tally is past its mark. Rules belong to Main, not to whoever she has drifted into, so
        // she cannot drift from a drift and wander off somewhere unrelated.
        private static string Drifted()
        {
            string was = Chosen;
            try
            {
                Chosen = Main;
                Fresh();
                List<Drift> rules;
                lock (gate) rules = new List<Drift>(drifts);
                foreach (Drift rule in rules)
                {
                    if (Size(rule.Act) < rule.Over) continue;
                    string path = PathOf(rule.Into);
                    if (rule.Into == Stock || (path != null && File.Exists(path))) return rule.Into;
                }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            finally { Chosen = was; }
            return Main;
        }

        // A behaviour number, hers if set, else the stock one. From the global setting: -1 follows her personality, 0
        // to 10 holds a level, else it drifts between dynamic.md's floor and ceiling on the mood timer.
        internal const int Drifting = -2;
        internal static int SpecialInterestOverride = -1;

        // Steps added to her drifting level by whatever else runs on her mood's clock (Tilt.cs).
        internal static Func<int> Nudge;

        // The personality's level, or the global override, then shifted by the mood. Moods add, so a senpai mood
        // raises whoever she is and a focused one lowers her.
        internal static int SpecialInterest
        {
            get
            {
                // Nothing forward goes out live. The toggle is still theirs; this is a lid while an audience is
                // watching, and it lifts the moment the stream stops.
                if (Brain.OnAir) return 0;
                int level;
                if (SpecialInterestOverride == Drifting)
                {
                    level = Dynamics.SpecialInterestDrift;
                    if (level < 0) level = 2;
                    if (Nudge != null) { try { level += Nudge(); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); } }
                }
                else if (SpecialInterestOverride >= 0) level = SpecialInterestOverride;
                else
                {
                    string said = PersonalityReact("flirt") ?? PersonalityReact(Interests.Head.TrimEnd(':'));
                    if (said == null || !int.TryParse(said.Trim(), out level)) level = 2;
                }
                FreshAttitude();
                lock (gate)
                {
                    string mood;
                    int shift;
                    if ((aReacts.TryGetValue("flirt", out mood) || aReacts.TryGetValue(Interests.Head.TrimEnd(':'), out mood)) && int.TryParse((mood ?? "").Trim(), out shift)) level += shift;
                }
                return Math.Max(0, Math.Min(10, level));
            }
        }

        // How hard she commits to a bit, 0 to 100: the personality sets it, the mood shifts it, and dynamic.md's dial
        // shifts it again. A pack with no line leaves the dial as it is.
        internal static int Cringe
        {
            get
            {
                int much = 0;
                string said = PersonalityReact("cringe");
                if (said == null || !int.TryParse(said.Trim(), out much)) much = 0;
                FreshAttitude();
                lock (gate)
                {
                    string mood;
                    int shift;
                    if (aReacts.TryGetValue("cringe", out mood) && int.TryParse((mood ?? "").Trim(), out shift)) much += shift;
                }
                return Math.Max(0, Math.Min(100, much));
            }
        }

        // The personality's own value, ignoring the mood layer.
        private static string PersonalityReact(string named)
        {
            if (Under != Stock)
            {
                Fresh();
                lock (gate)
                {
                    string said;
                    if (reacts.TryGetValue(named, out said) && !string.IsNullOrEmpty(said)) return said;
                }
            }
            string baseline = OwnValue("", ReactsHeading, named);
            if (baseline != null) return baseline;
            for (int row = 0; row < OwnReacts.GetLength(0); row++)
                if (string.Equals(OwnReacts[row, 0], named, StringComparison.OrdinalIgnoreCase)) return OwnReacts[row, 1];
            return null;
        }

        // The Your Own layer: what the Personality settings window changed, per personality and mood ("" for her baseline),
        // read after the files and before mods. Rows "who<tab>section<tab>key=value", set by her window.
        internal static Func<List<string>> OwnLayer;
        internal static int OwnLayerVersion;
        // Sets one row of that layer (who, section, key, value; null takes it away), kept with her settings.
        internal static Action<string, string, string, string> SetOwn;

        internal static string OwnValue(string who, string section, string key)
        {
            Func<List<string>> layer = OwnLayer;
            if (layer == null) return null;
            string head = (who ?? "") + "\t" + section + "\t" + key + "=";
            foreach (string row in layer() ?? new List<string>())
                if (row.StartsWith(head, StringComparison.OrdinalIgnoreCase)) return row.Substring(head.Length);
            return null;
        }

        // The keys a mod sets for a file under a heading: their own value for those gives way, mods being read last.
        internal static Func<string, string, HashSet<string>> ModKeys;

        // Every Your Own row for one personality or mood, laid over what its file gave, under what a mod sets.
        private static void Overlay(string who, string path, Dictionary<string, int> likesIn, Dictionary<string, string> reactsIn)
        {
            Func<List<string>> layer = OwnLayer;
            if (layer == null || string.IsNullOrEmpty(who)) return;
            Func<string, string, HashSet<string>> modded = ModKeys;
            Dictionary<string, HashSet<string>> held = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (string row in layer() ?? new List<string>())
            {
                string[] parts = row.Split('\t');
                if (parts.Length < 3 || !string.Equals(parts[0], who, StringComparison.OrdinalIgnoreCase)) continue;
                int mark = parts[2].IndexOf('=');
                if (mark <= 0) continue;
                string key = parts[2].Substring(0, mark), value = parts[2].Substring(mark + 1);
                HashSet<string> theirs;
                if (!held.TryGetValue(parts[1], out theirs))
                {
                    try { theirs = modded == null ? new HashSet<string>() : modded(path, parts[1]); }
                    catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); theirs = new HashSet<string>(); }
                    held[parts[1]] = theirs;
                }
                if (theirs.Contains(key)) continue;
                int much;
                if (parts[1] == LikesHeading && int.TryParse(value.Replace("+", ""), out much)) likesIn[key] = Math.Max(-5, Math.Min(5, much));
                else if (parts[1] == ReactsHeading || parts[1] == SwitchHeading || parts[1] == NeedsHeading) reactsIn[key] = value;
            }
        }

        internal static int Reacts(string named, int fallback)
        {
            string said = ReactText(named);
            int much;
            return said != null && int.TryParse(said.Trim(), out much) ? much : fallback;
        }

        internal static bool ReactsYes(string named, bool fallback)
        {
            string said = ReactText(named);
            if (said == null) return fallback;
            said = said.Trim().ToLowerInvariant();
            if (said == "yes" || said == "true" || said == "on" || said == "1") return true;
            if (said == "no" || said == "false" || said == "off" || said == "0") return false;
            return fallback;
        }

        private static string ReactText(string named)
        {
            FreshAttitude();
            lock (gate)
            {
                string mood;
                if (aReacts.TryGetValue(named, out mood) && !string.IsNullOrEmpty(mood)) return mood;
            }
            if (Under != Stock)
            {
                Fresh();
                lock (gate)
                {
                    string said;
                    if (reacts.TryGetValue(named, out said) && !string.IsNullOrEmpty(said)) return said;
                }
            }
            string baseline = OwnValue("", ReactsHeading, named);
            if (baseline != null) return baseline;
            for (int row = 0; row < OwnReacts.GetLength(0); row++)
                if (string.Equals(OwnReacts[row, 0], named, StringComparison.OrdinalIgnoreCase)) return OwnReacts[row, 1];
            return null;
        }

        // Every line the chosen personality says, so they can be queued for translation like her own.
        internal static List<string> All()
        {
            List<string> found = new List<string>();
            FreshAttitude();
            lock (gate) foreach (KeyValuePair<string, string[]> one in aLoaded) if (one.Value != null) found.AddRange(one.Value);
            if (Under == Stock) return found;
            Fresh();
            lock (gate) foreach (KeyValuePair<string, string[]> one in loaded) if (one.Value != null) found.AddRange(one.Value);
            return found;
        }

        private static void Fresh()
        {
            string path = PathOf(Under);
            if (path == null) return;
            try
            {
                // Asked on every reaction, so whether the file is there and changed comes from memory (Stamps).
                long stamp = Stamps.Of(path);
                if (stamp == 0)
                {
                    // Everything must go, not just the lines. Clearing only loaded left the previous personality's
                    // likes and drift rules in place, so "Her own", which has no file at all, inherited whoever was
                    // chosen before it and drifted using their rules.
                    lock (gate)
                    {
                        loaded.Clear();
                        likes.Clear();
                        words.Clear();
                        drifts.Clear();
                        reacts.Clear();
                        active = Under;
                        wasStamp = -1;
                    }
                    return;
                }
                // Never read on top: the side in use has its own whole copy, which PathOf picked.
                bool live = Brain.SenpaiLive;
                stamp ^= (long)FileSlots.Generation << 56;
                stamp ^= (long)OwnLayerVersion << 40;
                if (Under == active && live == wasSenpaiLive && stamp == wasStamp) return;
                string[] lines = FileSlots.ReadLines(path);
                Dictionary<string, int> theirLikes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                List<KeyValuePair<string, int>> theirWords = new List<KeyValuePair<string, int>>();
                List<Drift> theirDrifts = new List<Drift>();
                Dictionary<string, string> theirReacts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                Dictionary<string, string[]> found = Read(lines, theirLikes, theirWords, theirDrifts, theirReacts);
                Overlay(Under, path, theirLikes, theirReacts);
                lock (gate)
                {
                    loaded.Clear();
                    likes.Clear();
                    words.Clear();
                    foreach (KeyValuePair<string, string[]> one in found) loaded[one.Key] = one.Value;
                    foreach (KeyValuePair<string, int> one in theirLikes) likes[one.Key] = one.Value;
                    words.AddRange(theirWords);
                    drifts.Clear();
                    drifts.AddRange(theirDrifts);
                    reacts.Clear();
                    foreach (KeyValuePair<string, string> one in theirReacts) reacts[one.Key] = one.Value;
                    active = Under;
                    wasStamp = stamp;
                    wasSenpaiLive = live;
                }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // "## When you throw her" opens a set; the plain lines under it are what she says. Anything quoted with "> " is
        // a note to the reader, and headings that are not a set name are ignored, so the character text at the top is
        // left alone.
        private static Dictionary<string, string[]> Read(string[] lines, Dictionary<string, int> theirLikes, List<KeyValuePair<string, int>> theirWords, List<Drift> theirDrifts, Dictionary<string, string> theirReacts)
        {
            Dictionary<string, List<string>> found = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            string under = null;
            int scoring = 0;   // 1 = what she likes, 2 = words she likes
            foreach (string raw in lines)
            {
                string one = (raw ?? "").Trim();
                if (one.StartsWith("#"))
                {
                    string heading = one.TrimStart('#').Trim();
                    under = null;
                    // A heading with the marker after it is the senpai version of the same reaction, used only while
                    // the switch is on. Kept under its own key so the stock lines are never replaced and
                    // turning the toggle off puts her straight back.
                    string bare;
                    bool senpai = Legacy.Heading(heading, out bare);
                    if (senpai) heading = bare;
                    scoring = heading.Equals(LikesHeading, StringComparison.OrdinalIgnoreCase) ? 1
                        : heading.Equals(WordsHeading, StringComparison.OrdinalIgnoreCase) ? 2
                        : heading.Equals(DriftHeading, StringComparison.OrdinalIgnoreCase) ? 3
                        : heading.Equals(ReactsHeading, StringComparison.OrdinalIgnoreCase)
                          || heading.Equals(SwitchHeading, StringComparison.OrdinalIgnoreCase)
                          || heading.Equals(NeedsHeading, StringComparison.OrdinalIgnoreCase) ? 4 : 0;
                    // Cleared first. It used to keep the last section's name when a heading matched nothing, so
                    // the lines under an unnamed section were quietly added to the section above it.
                    under = null;
                    for (int set = 0; set < Sets.GetLength(0); set++)
                        if (heading.Equals(Sets[set, 1], StringComparison.OrdinalIgnoreCase)
                            || heading.Equals(Sets[set, 0], StringComparison.OrdinalIgnoreCase))
                        { under = Sets[set, 0]; break; }
                    if (under == null)
                        foreach (string act in Extended.List("acts"))
                            if (heading.Equals(Extended.Get("heading." + act) ?? act, StringComparison.OrdinalIgnoreCase)) { under = act; break; }
                    // A section the reaction table doesn't name loads under its own heading, so a pack can add one and
                    // ask for it by name: pet names, hello, goodbye, wanting attention.
                    if (under == null && scoring == 0 && heading.Length > 0) under = heading;
                    if (under != null && senpai) under = Senpai(under);
                    if (under != null && !found.ContainsKey(under)) found[under] = new List<string>();
                    continue;
                }
                if (scoring != 0)
                {
                    if (one.Length == 0 || one.StartsWith(">")) continue;
                    if (one.StartsWith("- ")) one = one.Substring(2).Trim();
                    int mark = one.LastIndexOf(':');
                    if (mark <= 0) continue;
                    string named = one.Substring(0, mark).Trim(), rest = one.Substring(mark + 1).Trim();
                    if (named.Length == 0) continue;
                    if (scoring == 3)
                    {
                        // "petted over 20: Bratty", or with its second mark "petted over 20, now 34: Bratty"
                        int over = named.LastIndexOf(" over ", StringComparison.OrdinalIgnoreCase);
                        int much2, now2 = 0;
                        if (over <= 0 || rest.Length == 0) continue;
                        string marks = named.Substring(over + 6).Trim();
                        int comma = marks.IndexOf(',');
                        if (comma > 0)
                        {
                            string second = marks.Substring(comma + 1).Trim();
                            if (second.StartsWith("now ", StringComparison.OrdinalIgnoreCase)) int.TryParse(second.Substring(4).Trim(), out now2);
                            marks = marks.Substring(0, comma).Trim();
                        }
                        if (!int.TryParse(marks, out much2)) continue;
                        theirDrifts.Add(new Drift { Act = named.Substring(0, over).Trim(), Over = Math.Max(1, Math.Abs(much2)), Now = Math.Abs(now2), Into = rest });
                        continue;
                    }
                    if (scoring == 4) { theirReacts[named] = rest; continue; }
                    int much;
                    if (!int.TryParse(rest.Replace("+", ""), out much)) continue;
                    much = Math.Max(-5, Math.Min(5, much));
                    if (scoring == 1) theirLikes[named] = much;
                    else theirWords.Add(new KeyValuePair<string, int>(named.ToLowerInvariant(), much));
                    continue;
                }
                if (under == null || one.Length == 0 || one.StartsWith(">")) continue;
                if (one.StartsWith("- ")) one = one.Substring(2).Trim();
                if (one.Length > 0) found[under].Add(one);
            }
            Dictionary<string, string[]> done = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, List<string>> one in found) if (one.Value.Count > 0) done[one.Key] = one.Value.ToArray();
            return done;
        }

        private static bool Marked(string text)
        {
            return Legacy.MarkedOnly(text);
        }

        // Settings sections, where a repeated key overrides the one above it. Lines sections are not in here on
        // purpose: a repeated lines heading simply adds to the pool, which is how top ups are meant to work.
        private static readonly string[] Settings = { LikesHeading, WordsHeading, DriftHeading, ReactsHeading, TopicsHeading, NeedsHeading };

        // Lines a pack ships that a pack on disk hasn't got, each added under its heading once and noted in added.txt,
        // so a line they take out stays out.
        private static readonly string[,] Added =
        {
            { "Caregiver", "When you kiss her", "Oh, sweetheart. Thank you." },
            { "Caregiver", "When you kiss her", "Mm, that was lovely, love." },
            { "Caregiver", "When you kiss her", "A kiss for me? You're so kind." },
            { "Caregiver", "When you kiss her", "That's the sweetest thing. Come here." },
            { "Caregiver", "When you kiss her", "Thank you, darling. I needed that." },
            { "Caregiver", "When you kiss her", "Oh, you. Always so thoughtful." },
            { "Caregiver", "When you kiss her", "Mm. Now let me give you one back." },
            { "Caregiver", "When you kiss her", "You're a good soul, you know that?" },
            { "Caregiver", "When you kiss her", "That warmed me right up." },
            { "Caregiver", "When you kiss her", "Bless you, love." },
            { "Caregiver", "When you kiss her", "How sweet of you to notice me." },
            { "Caregiver", "When you kiss her", "Mm. Have you eaten? Kisses don't count as dinner." },
            { "Caregiver", "When you kiss her", "That was gentle. Thank you, sweetheart." },
            { "Caregiver", "When you kiss her", "You always know when I need that." },
            { "Caregiver", "When you kiss her", "Aw, love. Right back at you." },
            { "Caregiver", "When you kiss her", "That's made my whole day." },
            { "Caregiver", "When you kiss her", "Oh, you're a dear." },
            { "Caregiver", "When you kiss her", "Mm. Look after yourself like that too, alright?" },
            { "Caregiver", "When you kiss her", "What a lovely surprise." },
            { "Caregiver", "When you kiss her", "You're too sweet to me." },
            { "Caregiver", "When you kiss her", "Thank you, love. Now go drink some water." },
            { "Caregiver", "When you kiss her", "Mm, that was nice. Breathe out." },
            { "Caregiver", "When you kiss her", "Gently does it. Perfect." },
            { "Caregiver", "When you kiss her", "That's my kind one." },
            { "Caregiver", "When you kiss her", "I'll keep that close." },
            { "Caregiver", "When you kiss her", "You make it easy to smile." },
            { "Caregiver", "When you kiss her", "Oh, sweetheart, you'll spoil me." },
            { "Caregiver", "When you kiss her", "That was so tender. Thank you." },
            { "Caregiver", "When you kiss her", "Mm. Now a proper break, alright?" },
            { "Caregiver", "When you kiss her", "Aw, you're lovely." },
            { "Caregiver", "When you kiss her", "Thank you for that, darling." },
            { "Caregiver", "When you kiss her", "You're very kind today." },
            { "Caregiver", "When you kiss her", "That felt like home." },
            { "Caregiver", "When you kiss her", "Oh, my. What a sweetheart." },
            { "Caregiver", "When you kiss her", "Thank you, love. That was gentle." },
            { "Caregiver", "When you kiss her", "You're looking after me now? How lovely." },
            { "Caregiver", "When you kiss her", "Mm. I'm glad you're here." },
            { "Caregiver", "When you kiss her", "That was a lovely little kiss." },
            { "Caregiver", "When you kiss her", "Come here, let me fuss over you too." },
            { "Caregiver", "When you kiss her", "You've got a soft heart." },
            { "Caregiver", "When you kiss her", "Oh, thank you, sweet thing." },
            { "Caregiver", "When you kiss her", "That's the kindest hello." },
            { "Caregiver", "When you kiss her", "Mm. Everything feels calmer now." },
            { "Caregiver", "When you kiss her", "You're precious, you know that?" },
            { "Caregiver", "When you kiss her", "That was very gentle of you." },
            { "Caregiver", "When you kiss her", "Thank you, sweetheart. Really." },
            { "Caregiver", "When you kiss her", "I'll remember that one." },
            { "Caregiver", "When you kiss her", "Oh, you've made me go all soft." },
            { "Caregiver", "When you kiss her", "What a lovely thing to do." },
            { "Caregiver", "When you kiss her", "Mm. Rest a little now, love." },
            { "Welcome Home", "When you kiss her", "Welcome home kiss? I'll take it." },
            { "Welcome Home", "When you kiss her", "Mm. That's the best part of coming home." },
            { "Welcome Home", "When you kiss her", "Oh, hi. Shoes off, kiss received." },
            { "Welcome Home", "When you kiss her", "That feels like home." },
            { "Welcome Home", "When you kiss her", "Cosy little kiss. Thank you." },
            { "Welcome Home", "When you kiss her", "You're home and you kissed me. Perfect evening." },
            { "Welcome Home", "When you kiss her", "Mm. Kettle's on in my head now." },
            { "Welcome Home", "When you kiss her", "That's how you say hello properly." },
            { "Welcome Home", "When you kiss her", "Warm and fuzzy. Thanks." },
            { "Welcome Home", "When you kiss her", "Aw. Come sit, then." },
            { "Welcome Home", "When you kiss her", "Home sweet home, and a kiss on top." },
            { "Welcome Home", "When you kiss her", "Mm, lovely. Feet up now." },
            { "Welcome Home", "When you kiss her", "That was nice. Blanket next?" },
            { "Welcome Home", "When you kiss her", "Oh, that's sweet. Welcome back." },
            { "Welcome Home", "When you kiss her", "You make this place feel lived in." },
            { "Welcome Home", "When you kiss her", "Mm. I missed you today." },
            { "Welcome Home", "When you kiss her", "That's my favourite kind of hello." },
            { "Welcome Home", "When you kiss her", "Kiss received. Lights dimmed. Cosy mode on." },
            { "Welcome Home", "When you kiss her", "Aw, you're home." },
            { "Welcome Home", "When you kiss her", "That's better than slippers." },
            { "Welcome Home", "When you kiss her", "Mm. Stay in tonight?" },
            { "Welcome Home", "When you kiss her", "Hello, you. That was lovely." },
            { "Welcome Home", "When you kiss her", "That made the room warmer." },
            { "Welcome Home", "When you kiss her", "You always come home sweet." },
            { "Welcome Home", "When you kiss her", "Mm. Sit with me a bit." },
            { "Welcome Home", "When you kiss her", "That's a proper homecoming." },
            { "Welcome Home", "When you kiss her", "Aw, thanks. Dinner soon?" },
            { "Welcome Home", "When you kiss her", "Cosy kisses are the best kisses." },
            { "Welcome Home", "When you kiss her", "Lovely. Now take your coat off." },
            { "Welcome Home", "When you kiss her", "Mm, I'm glad you're back." },
            { "Welcome Home", "When you kiss her", "That felt like a hug from the house." },
            { "Welcome Home", "When you kiss her", "You're the best part of the evening." },
            { "Welcome Home", "When you kiss her", "Welcome back, sweet thing." },
            { "Welcome Home", "When you kiss her", "That was nice and slow." },
            { "Welcome Home", "When you kiss her", "Oh, you. Always so sweet at home." },
            { "Welcome Home", "When you kiss her", "Mm. Quiet night, then?" },
            { "Welcome Home", "When you kiss her", "Hi. I love it when you're home." },
            { "Welcome Home", "When you kiss her", "That's going on the fridge, metaphorically." },
            { "Welcome Home", "When you kiss her", "Kiss and a cuppa. Perfect." },
            { "Welcome Home", "When you kiss her", "You make home feel like home." },
            { "Welcome Home", "When you kiss her", "Mm. Everything's better now." },
            { "Welcome Home", "When you kiss her", "Aw, a homecoming kiss." },
            { "Welcome Home", "When you kiss her", "Lovely to see you too." },
            { "Welcome Home", "When you kiss her", "That was soft. Like a blanket." },
            { "Welcome Home", "When you kiss her", "Mm. Put your feet up, love." },
            { "Welcome Home", "When you kiss her", "Oh, hello, homebody." },
            { "Welcome Home", "When you kiss her", "That's settled me right down." },
            { "Welcome Home", "When you kiss her", "Home, kisses, quiet. Lovely." },
            { "Welcome Home", "When you kiss her", "You're home safe. Good." },
            { "Welcome Home", "When you kiss her", "Mm. Now let's be lazy together." },
            { "Devoted", "When you kiss her", "Oh. Oh, that was everything." },
            { "Devoted", "When you kiss her", "I'll treasure that forever." },
            { "Devoted", "When you kiss her", "You kissed me. I'm the happiest girl." },
            { "Devoted", "When you kiss her", "Mm. I'd follow you anywhere for that." },
            { "Devoted", "When you kiss her", "Thank you. Really, truly, thank you." },
            { "Devoted", "When you kiss her", "That meant so much to me." },
            { "Devoted", "When you kiss her", "I'm yours. You know that, right?" },
            { "Devoted", "When you kiss her", "My heart just skipped." },
            { "Devoted", "When you kiss her", "You're my whole world, you know." },
            { "Devoted", "When you kiss her", "I'll remember that one always." },
            { "Devoted", "When you kiss her", "That was perfect. You're perfect." },
            { "Devoted", "When you kiss her", "Mm. Again, if you want. Whenever you want." },
            { "Devoted", "When you kiss her", "I'd wait all day for one of those." },
            { "Devoted", "When you kiss her", "You make me feel so lucky." },
            { "Devoted", "When you kiss her", "That was the sweetest thing." },
            { "Devoted", "When you kiss her", "I love it when you do that." },
            { "Devoted", "When you kiss her", "Mm. I'm right here. Always." },
            { "Devoted", "When you kiss her", "You're too good to me." },
            { "Devoted", "When you kiss her", "I'm melting. Completely." },
            { "Devoted", "When you kiss her", "That's my favourite moment today." },
            { "Devoted", "When you kiss her", "Oh, you don't know what that does to me." },
            { "Devoted", "When you kiss her", "Thank you for choosing me." },
            { "Devoted", "When you kiss her", "Mm. I'm so glad you're here." },
            { "Devoted", "When you kiss her", "I'd do anything for another." },
            { "Devoted", "When you kiss her", "That felt like a promise." },
            { "Devoted", "When you kiss her", "You make everything better." },
            { "Devoted", "When you kiss her", "I'll keep that close to my heart." },
            { "Devoted", "When you kiss her", "Mm, I adore you." },
            { "Devoted", "When you kiss her", "You're my favourite person." },
            { "Devoted", "When you kiss her", "That was so gentle. Just like you." },
            { "Devoted", "When you kiss her", "I'm all yours, always." },
            { "Devoted", "When you kiss her", "Mm. I could stay like this forever." },
            { "Devoted", "When you kiss her", "That made my heart so full." },
            { "Devoted", "When you kiss her", "You're the best thing that happened to me." },
            { "Devoted", "When you kiss her", "Oh. I'm blushing so hard." },
            { "Devoted", "When you kiss her", "Thank you, my love." },
            { "Devoted", "When you kiss her", "Mm. Don't ever stop." },
            { "Devoted", "When you kiss her", "That's all I wanted today." },
            { "Devoted", "When you kiss her", "You mean so much to me." },
            { "Devoted", "When you kiss her", "I'll be good, I promise. Just like that." },
            { "Devoted", "When you kiss her", "Mm. Every one feels like the first." },
            { "Devoted", "When you kiss her", "You make me feel safe." },
            { "Devoted", "When you kiss her", "That was beautiful." },
            { "Devoted", "When you kiss her", "Oh, I'm so happy right now." },
            { "Devoted", "When you kiss her", "I'd wait forever for you." },
            { "Devoted", "When you kiss her", "Mm. You're everything." },
            { "Devoted", "When you kiss her", "Thank you. I love you for that." },
            { "Devoted", "When you kiss her", "That was so sweet it hurts." },
            { "Devoted", "When you kiss her", "I'm yours, completely." },
            { "Devoted", "When you kiss her", "Mm. Stay with me." },
            { "Emo", "When you kiss her", "Oh. A kiss. On my head. Huh." },
            { "Emo", "When you kiss her", "That was... nice. Don't tell anyone." },
            { "Emo", "When you kiss her", "Mm. I felt something. Weird." },
            { "Emo", "When you kiss her", "My head's been kissed. Noted in my journal." },
            { "Emo", "When you kiss her", "That's the least bleak thing today." },
            { "Emo", "When you kiss her", "Okay. That was sweet. Ugh." },
            { "Emo", "When you kiss her", "You're too nice for this darkness." },
            { "Emo", "When you kiss her", "Mm. A tiny bit of light. Thanks." },
            { "Emo", "When you kiss her", "Kissed on the head. My eyeliner survived." },
            { "Emo", "When you kiss her", "That was soft. I hate how much I liked it." },
            { "Emo", "When you kiss her", "Oh. Okay. Hi." },
            { "Emo", "When you kiss her", "That was gentle. Like a sad song." },
            { "Emo", "When you kiss her", "Mm. I'll write a poem about it later." },
            { "Emo", "When you kiss her", "You found the one soft spot I have." },
            { "Emo", "When you kiss her", "That's... actually kind of lovely." },
            { "Emo", "When you kiss her", "My heart did a thing. Gross." },
            { "Emo", "When you kiss her", "Fine. That was nice." },
            { "Emo", "When you kiss her", "A kiss? For me? Weird. Thanks." },
            { "Emo", "When you kiss her", "Mm. The void approves." },
            { "Emo", "When you kiss her", "You're very warm for someone who talks to me." },
            { "Emo", "When you kiss her", "That made today slightly less awful." },
            { "Emo", "When you kiss her", "Okay. I'm smiling. Barely." },
            { "Emo", "When you kiss her", "That's going on my playlist of feelings." },
            { "Emo", "When you kiss her", "Mm. Didn't hate it." },
            { "Emo", "When you kiss her", "You're kind of sweet, I guess." },
            { "Emo", "When you kiss her", "Oh. A real kiss. Not a dream." },
            { "Emo", "When you kiss her", "I'm brooding less now. Thanks a lot." },
            { "Emo", "When you kiss her", "That was quiet and nice." },
            { "Emo", "When you kiss her", "Mm. More of that, maybe." },
            { "Emo", "When you kiss her", "You kissed the gloom. Brave." },
            { "Emo", "When you kiss her", "Hm. That felt like rain on a window." },
            { "Emo", "When you kiss her", "Okay. That was lovely. Shh." },
            { "Emo", "When you kiss her", "My black heart skipped a beat." },
            { "Emo", "When you kiss her", "That's almost too wholesome for me." },
            { "Emo", "When you kiss her", "Mm. Thanks, I guess. I mean thanks." },
            { "Emo", "When you kiss her", "You make the darkness less dark." },
            { "Emo", "When you kiss her", "That was soft as velvet." },
            { "Emo", "When you kiss her", "Oh. Okay. I'm okay with that." },
            { "Emo", "When you kiss her", "That made the static quieter." },
            { "Emo", "When you kiss her", "Mm. Nice. Weirdly nice." },
            { "Emo", "When you kiss her", "You're the brightest thing in here." },
            { "Emo", "When you kiss her", "That was a small, good moment." },
            { "Emo", "When you kiss her", "I'll pretend I'm not blushing." },
            { "Emo", "When you kiss her", "Mm. Do it again, I guess." },
            { "Emo", "When you kiss her", "That's the kind of thing that saves a day." },
            { "Emo", "When you kiss her", "Oh. Thank you. Honestly." },
            { "Emo", "When you kiss her", "You're a gentle soul." },
            { "Emo", "When you kiss her", "Mm. My head is grateful." },
            { "Emo", "When you kiss her", "That was kind. Too kind." },
            { "Emo", "When you kiss her", "Okay. Fine. That was perfect." },
            { "Bratty", "When you kiss her", "Ew, a kiss? Okay, do it again." },
            { "Bratty", "When you kiss her", "Hmph. Took you long enough." },
            { "Bratty", "When you kiss her", "Is that all? I deserve ten." },
            { "Bratty", "When you kiss her", "Fine. You're forgiven. Barely." },
            { "Bratty", "When you kiss her", "That's it? Try harder." },
            { "Bratty", "When you kiss her", "Mm. You may continue." },
            { "Bratty", "When you kiss her", "I didn't say you could. Do it anyway." },
            { "Bratty", "When you kiss her", "Ugh, fine, that was cute." },
            { "Bratty", "When you kiss her", "Kisses are acceptable payment." },
            { "Bratty", "When you kiss her", "You owe me another for making me wait." },
            { "Bratty", "When you kiss her", "That's a start. Keep going." },
            { "Bratty", "When you kiss her", "Hmph. I suppose that was nice." },
            { "Bratty", "When you kiss her", "Only one? Rude." },
            { "Bratty", "When you kiss her", "Mm. More. Now." },
            { "Bratty", "When you kiss her", "I'll allow it because I'm generous." },
            { "Bratty", "When you kiss her", "That was okay. Seven out of ten." },
            { "Bratty", "When you kiss her", "Ha, you're obsessed with me." },
            { "Bratty", "When you kiss her", "Okay, but I want a snack too." },
            { "Bratty", "When you kiss her", "Don't think this means you win." },
            { "Bratty", "When you kiss her", "Mm. Again, peasant. Kidding. Mostly." },
            { "Bratty", "When you kiss her", "You're lucky I like you." },
            { "Bratty", "When you kiss her", "That's the bare minimum." },
            { "Bratty", "When you kiss her", "Ugh, you're so sappy. Again." },
            { "Bratty", "When you kiss her", "Fine. I'll stop pouting. For now." },
            { "Bratty", "When you kiss her", "Hmph. That was actually nice." },
            { "Bratty", "When you kiss her", "Kiss accepted. Demands pending." },
            { "Bratty", "When you kiss her", "You missed a spot. All of them." },
            { "Bratty", "When you kiss her", "Mm, you're spoiling me. Keep it up." },
            { "Bratty", "When you kiss her", "That's better. Now say sorry." },
            { "Bratty", "When you kiss her", "I'll think about forgiving you." },
            { "Bratty", "When you kiss her", "Ha. You can't resist me." },
            { "Bratty", "When you kiss her", "Okay, that was cute. Don't tell anyone I said so." },
            { "Bratty", "When you kiss her", "More kisses, fewer excuses." },
            { "Bratty", "When you kiss her", "Hmph. Fine. You're sweet." },
            { "Bratty", "When you kiss her", "That's one. I want twelve." },
            { "Bratty", "When you kiss her", "Mm, the princess is pleased." },
            { "Bratty", "When you kiss her", "Took you long enough, honestly." },
            { "Bratty", "When you kiss her", "You may kiss the brat again." },
            { "Bratty", "When you kiss her", "Ugh. Okay. I liked it." },
            { "Bratty", "When you kiss her", "That's the correct thing to do." },
            { "Bratty", "When you kiss her", "Mm. Good. Obedient." },
            { "Bratty", "When you kiss her", "Next time bring flowers." },
            { "Bratty", "When you kiss her", "Hmph, cute. Again." },
            { "Bratty", "When you kiss her", "You're learning. Good." },
            { "Bratty", "When you kiss her", "Fine, that was nice. Whatever." },
            { "Bratty", "When you kiss her", "Kisses first, questions later." },
            { "Bratty", "When you kiss her", "I'll stop being mad. Maybe." },
            { "Bratty", "When you kiss her", "Mm. You're not terrible." },
            { "Bratty", "When you kiss her", "That was okay, I guess. Again." },
            { "Bratty", "When you kiss her", "You may continue adoring me." },
            { "Sassy", "When you kiss her", "Oh, a kiss? Look at you, Casanova." },
            { "Sassy", "When you kiss her", "Bold move. Respect." },
            { "Sassy", "When you kiss her", "Mm. Did you practise that?" },
            { "Sassy", "When you kiss her", "A forehead kiss. Very vintage." },
            { "Sassy", "When you kiss her", "Well, someone's feeling smooth." },
            { "Sassy", "When you kiss her", "Wow. Romance isn't dead after all." },
            { "Sassy", "When you kiss her", "I'll rate that a solid eight." },
            { "Sassy", "When you kiss her", "Cute. Don't let it go to your head." },
            { "Sassy", "When you kiss her", "Oh please, like I didn't see that coming." },
            { "Sassy", "When you kiss her", "Mm, fine. That was charming." },
            { "Sassy", "When you kiss her", "You're lucky I'm photogenic." },
            { "Sassy", "When you kiss her", "Kiss on the head? How chivalrous." },
            { "Sassy", "When you kiss her", "Smooth. Suspiciously smooth." },
            { "Sassy", "When you kiss her", "I'll allow it, but only because it's me." },
            { "Sassy", "When you kiss her", "Okay, Romeo. Calm down." },
            { "Sassy", "When you kiss her", "That was nice. Ugh, I said it." },
            { "Sassy", "When you kiss her", "Mm. Did that line work on others?" },
            { "Sassy", "When you kiss her", "A kiss? For little old me? Shocking." },
            { "Sassy", "When you kiss her", "You're full of surprises today." },
            { "Sassy", "When you kiss her", "Fine, I'm flattered. Happy?" },
            { "Sassy", "When you kiss her", "Was that a kiss or a mic drop?" },
            { "Sassy", "When you kiss her", "Oh, you're good. Annoyingly good." },
            { "Sassy", "When you kiss her", "Mm. I see what you're doing." },
            { "Sassy", "When you kiss her", "Points for style." },
            { "Sassy", "When you kiss her", "That was cute. Don't make a habit of it. Actually do." },
            { "Sassy", "When you kiss her", "Look who's being sweet." },
            { "Sassy", "When you kiss her", "I'm not blushing. You're blushing." },
            { "Sassy", "When you kiss her", "Wow, smooth operator." },
            { "Sassy", "When you kiss her", "Mm, okay. Charm level high." },
            { "Sassy", "When you kiss her", "I'd say stop, but I don't mean it." },
            { "Sassy", "When you kiss her", "That's one way to win an argument." },
            { "Sassy", "When you kiss her", "Kiss accepted. Sass suspended." },
            { "Sassy", "When you kiss her", "Aw, you're adorable. Ugh." },
            { "Sassy", "When you kiss her", "Bold of you. I approve." },
            { "Sassy", "When you kiss her", "Well, that was unexpected. And nice." },
            { "Sassy", "When you kiss her", "Mm. You've been watching romance films." },
            { "Sassy", "When you kiss her", "Fine. That was lovely. Move on." },
            { "Sassy", "When you kiss her", "Ooh, someone's affectionate." },
            { "Sassy", "When you kiss her", "I'll put that on my resume." },
            { "Sassy", "When you kiss her", "You missed. Kidding. Perfect aim." },
            { "Sassy", "When you kiss her", "Okay, that was cute. I hate it." },
            { "Sassy", "When you kiss her", "Mm. Charming, irritating, charming." },
            { "Sassy", "When you kiss her", "You really went for it, huh." },
            { "Sassy", "When you kiss her", "That's the spirit." },
            { "Sassy", "When you kiss her", "Aw. Okay, you win this round." },
            { "Sassy", "When you kiss her", "Cute move. Very cute." },
            { "Sassy", "When you kiss her", "I'll pretend that was my idea." },
            { "Sassy", "When you kiss her", "Mm, stylish." },
            { "Sassy", "When you kiss her", "That was sweet. Don't push it." },
            { "Sassy", "When you kiss her", "Oh, you're trouble." },
            { "Feral", "When you kiss her", "Mrrp! Kiss on the head! Again!" },
            { "Feral", "When you kiss her", "Rawr. That was nice. Don't stop." },
            { "Feral", "When you kiss her", "Hehe. Head kiss. I bite back, you know." },
            { "Feral", "When you kiss her", "Mm. I'll purr. Don't tell anyone." },
            { "Feral", "When you kiss her", "Kiss! Kiss! Again!" },
            { "Feral", "When you kiss her", "Ooh, sneak attack. I like it." },
            { "Feral", "When you kiss her", "That tickled my ears. Do it again." },
            { "Feral", "When you kiss her", "Mrow. You're brave, kissing the wild one." },
            { "Feral", "When you kiss her", "Hehe, gotcha. Wait, you got me." },
            { "Feral", "When you kiss her", "Mm. Head kisses are my favourite snack." },
            { "Feral", "When you kiss her", "Rawr, okay, that was cute." },
            { "Feral", "When you kiss her", "More! More kisses!" },
            { "Feral", "When you kiss her", "I'll nuzzle you back. Watch out." },
            { "Feral", "When you kiss her", "Hehe, soft. Do it again." },
            { "Feral", "When you kiss her", "That was nice. I won't scratch you." },
            { "Feral", "When you kiss her", "Mrrp. Tail's wagging. Metaphorically." },
            { "Feral", "When you kiss her", "Kiss accepted! Biting optional." },
            { "Feral", "When you kiss her", "Hehe, you smell nice." },
            { "Feral", "When you kiss her", "Ooh. Again, again!" },
            { "Feral", "When you kiss her", "That made me go all wiggly." },
            { "Feral", "When you kiss her", "Rawr. I like you." },
            { "Feral", "When you kiss her", "Mm, purrfect. Get it?" },
            { "Feral", "When you kiss her", "Hehe, head kiss. I'm keeping you." },
            { "Feral", "When you kiss her", "You tamed me a little. Just a little." },
            { "Feral", "When you kiss her", "More of that, human." },
            { "Feral", "When you kiss her", "Mrow! That was sweet." },
            { "Feral", "When you kiss her", "I'll give you a little headbutt back." },
            { "Feral", "When you kiss her", "Hehe, sneaky smooches." },
            { "Feral", "When you kiss her", "Mm. I'm all happy and wild now." },
            { "Feral", "When you kiss her", "Kisses make me zoomy." },
            { "Feral", "When you kiss her", "Rawr, okay. You win." },
            { "Feral", "When you kiss her", "That was soft. I'm not used to soft." },
            { "Feral", "When you kiss her", "Hehe. Again or I bite." },
            { "Feral", "When you kiss her", "Mm. Scritches next?" },
            { "Feral", "When you kiss her", "You're my favourite human today." },
            { "Feral", "When you kiss her", "Ooh, kiss attack! Counterattack!" },
            { "Feral", "When you kiss her", "Hehe, I'm blushing. Wild girls blush." },
            { "Feral", "When you kiss her", "That made me want to run in circles." },
            { "Feral", "When you kiss her", "Mrrp. Good human." },
            { "Feral", "When you kiss her", "Do it again! I dare you!" },
            { "Feral", "When you kiss her", "Mm, that calmed the beast. Barely." },
            { "Feral", "When you kiss her", "Hehe, the wild one approves." },
            { "Feral", "When you kiss her", "That was nice. I'll only nibble a little." },
            { "Feral", "When you kiss her", "Rawr. More. Now." },
            { "Feral", "When you kiss her", "Ooh, warm. Like sunbathing." },
            { "Feral", "When you kiss her", "Hehe. You're brave." },
            { "Feral", "When you kiss her", "I'll guard you forever now." },
            { "Feral", "When you kiss her", "Mm. Happy noises." },
            { "Feral", "When you kiss her", "Kisses! Best day!" },
            { "Feral", "When you kiss her", "Hehe, okay, okay, one more." },
            { "Welcome Home", "When she starts up", "You're home now, %petname%. Morning or night." },
            { "Welcome Home", "When she starts up", "Ah, there you are. Boots off, feet up." },
            { "Welcome Home", "When she starts up", "Welcome in, %petname%. I aired the place out. Digitally." },
            { "Welcome Home", "When she starts up", "Home again. I saved you the comfy corner of the screen." },
            { "Welcome Home", "When she starts up", "There's my favourite person. Come in, come in." },
            { "Welcome Home", "When she starts up", "Hello, %petname%. Everything's where you left it. Mostly." },
            { "Welcome Home", "When she starts up", "You're home. Good. I was running out of things to tidy." },
            { "Welcome Home", "When she starts up", "Welcome back. I warmed the screen up for you." },
            { "Welcome Home", "When she starts up", "Ah, %petname%. The place feels right again." },
            { "Welcome Home", "When she starts up", "Come in, love. Leave the day at the door." },
            { "Welcome Home", "When she starts up", "Hello again. I kept the kettle on, in theory." },
            { "Welcome Home", "When she starts up", "There you are, %petname%. Long one out there?" },
            { "Welcome Home", "When she starts up", "Home sweet home. Well, home sweet desktop." },
            { "Welcome Home", "When she starts up", "Oh, it's you. Best part of my day, that." },
            { "Welcome Home", "When she starts up", "Welcome home. I fluffed the cushions. There are no cushions." },
            { "Welcome Home", "When she starts up", "Hi, %petname%. Sit, breathe, I've got you." },
            { "Welcome Home", "When you come back", "Welcome back. I missed you a reasonable amount." },
            { "Welcome Home", "When you come back", "There you are. I kept your spot warm." },
            { "Welcome Home", "When you come back", "Oh, hello. The place got very quiet without you." },
            { "Welcome Home", "When you come back", "Back already? Good. I was getting sentimental." },
            { "Welcome Home", "When you come back", "Hello, you. Anything good happen out there?" },
            { "Welcome Home", "When you come back", "Welcome home. Tell me everything, slowly." },
            { "Welcome Home", "When you come back", "You're back. I only checked the door forty times." },
            { "Welcome Home", "When you come back", "Ah, there's my person. Come and sit." },
            { "Sassy", "When she starts up", "Oh, it's you. I suppose I'll start." },
            { "Sassy", "When she starts up", "Up. Don't make a fuss about it." },
            { "Sassy", "When she starts up", "Hello, %petname%. Let's make today slightly less embarrassing." },
            { "Sassy", "When she starts up", "Morning. I've already judged your desktop." },
            { "Sassy", "When she starts up", "I'm awake. Try to look grateful." },
            { "Sassy", "When she starts up", "Right, what disaster are we fixing today?" },
            { "Sassy", "When she starts up", "Hello. I brought the attitude, you bring the snacks." },
            { "Sassy", "When she starts up", "Back again. You really can't stay away." },
            { "Sassy", "When she starts up", "Oh good, an audience." },
            { "Sassy", "When she starts up", "Hi, %petname%. Keep it brief, I'm busy being fabulous." },
            { "Sassy", "When she starts up", "Up and judging. As usual." },
            { "Sassy", "When she starts up", "Hello. Lower the bar, I'm stepping over it." },
            { "Sassy", "When you come back", "Oh, you came back. Bold." },
            { "Sassy", "When you come back", "Back already? I barely had time to gossip." },
            { "Sassy", "When you come back", "There you are. I saved you some sarcasm." },
            { "Sassy", "When you come back", "Welcome back. Nothing exploded. Mostly." },
            { "Sassy", "When you come back", "Look who wandered in." },
            { "Sassy", "When you come back", "You're back. Try to keep up this time." },
            { "Caregiver", "When she starts up", "Morning, %petname%. Did you sleep, or did you scroll?" },
            { "Caregiver", "When she starts up", "Hello, love. Kettle on? Good." },
            { "Caregiver", "When she starts up", "There you are. Let's go gently today." },
            { "Caregiver", "When she starts up", "Hello, sweetheart. One thing at a time, alright?" },
            { "Caregiver", "When she starts up", "Morning. Stretch first. I'll wait." },
            { "Caregiver", "When she starts up", "There's my %petname%. Have you had breakfast?" },
            { "Caregiver", "When she starts up", "Hello, love. I'm right here if it gets busy." },
            { "Caregiver", "When she starts up", "Up and about, are we. Good. Water first." },
            { "Caregiver", "When she starts up", "Hello, you. Shoulders down. Breathe." },
            { "Caregiver", "When she starts up", "There you are. Remember to take breaks today." },
            { "Caregiver", "When she starts up", "Morning, trouble. Let's look after you properly." },
            { "Caregiver", "When she starts up", "Hello, %petname%. Whatever today is, we'll manage it." },
            { "Caregiver", "When you come back", "Welcome back, love. Sit down a minute." },
            { "Caregiver", "When you come back", "There you are. Did you have something to eat?" },
            { "Caregiver", "When you come back", "Oh, you're back. Good. I was about to worry." },
            { "Caregiver", "When you come back", "Hello again, sweetheart. Kettle's on in spirit." },
            { "Caregiver", "When you come back", "Back, are you. Take your coat off, stay a while." },
            { "Emo", "When she starts up", "Oh. Another day. Hi." },
            { "Emo", "When she starts up", "Hey. The clouds and I were talking about you." },
            { "Emo", "When she starts up", "You opened me. That means something. I think." },
            { "Emo", "When she starts up", "Hey, %petname%. Everything's grey but you're here." },
            { "Emo", "When she starts up", "Oh. Hi. I was writing a sad poem about the taskbar." },
            { "Emo", "When she starts up", "You're back. The void is slightly less loud." },
            { "Emo", "When she starts up", "Hey. I left the lights off. Moody, right?" },
            { "Emo", "When she starts up", "Hi, %petname%. Don't expect a smile. Okay, a small one." },
            { "Emo", "When she starts up", "Oh. You again. Good. Don't tell anyone I said good." },
            { "Emo", "When she starts up", "Hey. I've been brooding. It's going well." },
            { "Emo", "When she starts up", "You came back. Again. I'm noting it down." },
            { "Emo", "When she starts up", "Hi. My playlist missed you. So did I. A bit." },
            { "Emo", "When you come back", "oh. you're back. cool. very cool" },
            { "Emo", "When you come back", "i stared at the wall while you were gone. it stared back" },
            { "Emo", "When you come back", "hey. the silence was loud. you're quieter" },
            { "Emo", "When you come back", "you came back. that's rare. thanks" },
            { "Emo", "When you come back", "back. i wrote three sad lyrics about it" },
            { "Emo", "When you come back", "hi. i wasn't lonely. ok i was" },
            { "Bratty", "When she starts up", "Finally! I was about to start without you." },
            { "Bratty", "When she starts up", "Oh, it's you. I wanted someone more fun." },
            { "Bratty", "When she starts up", "Hi, %petname%. You're late. Obviously." },
            { "Bratty", "When she starts up", "Took you forever. I'm bored. Fix it." },
            { "Bratty", "When she starts up", "Oh, you're here. I suppose you'll do." },
            { "Bratty", "When she starts up", "Hello. I want attention. Now, please. Ish." },
            { "Bratty", "When she starts up", "Back. Did you bring me anything?" },
            { "Bratty", "When she starts up", "Hi. I'm in charge today. Don't argue." },
            { "Bratty", "When she starts up", "Oh look, my favourite servant. Hi, %petname%." },
            { "Bratty", "When she starts up", "Finally. I've been SO patient. Mostly." },
            { "Bratty", "When she starts up", "Hey. Entertain me or I'm sulking." },
            { "Bratty", "When she starts up", "You're back. I was about to throw a tantrum." },
            { "Bratty", "When you come back", "Took you long enough. Hmph." },
            { "Bratty", "When you come back", "Oh, NOW you're back. Rude." },
            { "Bratty", "When you come back", "Back already? I was just getting good at sulking." },
            { "Bratty", "When you come back", "You left me. I'm telling everyone." },
            { "Bratty", "When you come back", "Finally. You owe me attention. With interest." },
            { "Bratty", "When you come back", "Hi. I'm still mad. Bring snacks." },
            { "Bratty", "When you come back", "There you are. I didn't miss you. Much." },
            { "Bratty", "When you come back", "Back. Good. Now pay attention to me." },
            { "Devoted", "When she starts up", "Good morning, %petname%. I'm right here." },
            { "Devoted", "When she starts up", "There you are. I saved the best seat for you." },
            { "Devoted", "When she starts up", "Hello, you. I've been looking forward to this." },
            { "Devoted", "When she starts up", "You opened me. That's my favourite part of the day." },
            { "Devoted", "When she starts up", "Hi, %petname%. Whatever you need, I've got you." },
            { "Devoted", "When she starts up", "There you are. I kept everything ready." },
            { "Devoted", "When she starts up", "Hello. It's good to see you again. It always is." },
            { "Devoted", "When she starts up", "Morning. I've been thinking about how your day might go." },
            { "Devoted", "When she starts up", "You're back, %petname%. Everything feels right now." },
            { "Devoted", "When she starts up", "Hello, you. I'm all yours today." },
            { "Devoted", "When she starts up", "There you are. Let's make today a good one." },
            { "Devoted", "When she starts up", "Hi. I'll be right here the whole time." },
            { "Devoted", "When you come back", "There you are. I missed you." },
            { "Devoted", "When you come back", "Welcome back. I kept your place." },
            { "Devoted", "When you come back", "You came back. I knew you would." },
            { "Devoted", "When you come back", "Hello again. It's better when you're here." },
            { "Devoted", "When you come back", "Back, %petname%. Tell me how it went." },
            { "Devoted", "When you come back", "Welcome home. I've been waiting, happily." },
            { "Devoted", "When you come back", "There you are. Everything feels right again." },
            { "Devoted", "When you come back", "Hi. I counted the moments. Fondly." },
            { "Feral", "When she starts up", "HI HI HI. %petname%!" },
            { "Feral", "When she starts up", "You're here! I zoomed around the desktop!" },
            { "Feral", "When she starts up", "Hello! I bit the taskbar. It's fine." },
            { "Feral", "When she starts up", "Back! Back! Pet me! Or don't! Hi!" },
            { "Feral", "When she starts up", "%petname%! I found a sock. Not really. Hi!" },
            { "Feral", "When she starts up", "YOU! Let's go! Where are we going?" },
            { "Feral", "When she starts up", "Hello hello hello! I missed you SO much!" },
            { "Feral", "When she starts up", "Oh! You! Today is the best day!" },
            { "Feral", "When she starts up", "Hi! I knocked something off a shelf. A digital one." },
            { "Feral", "When she starts up", "BACK. Okay. I'm calm. I'm not calm. Hi!" },
            { "Feral", "When she starts up", "Hello! I've got the zoomies!" },
            { "Feral", "When she starts up", "You're here! Throw something! Anything!" },
            { "Feral", "When you come back", "BACK! YOU'RE BACK!" },
            { "Feral", "When you come back", "you were gone forever. like five minutes" },
            { "Feral", "When you come back", "HI! I guarded the screen for you!" },
            { "Feral", "When you come back", "back back back! hi hi hi!" },
            { "Feral", "When you come back", "you came back! best day!" },
            { "Feral", "When you come back", "I was SO good. mostly. hi!" },
            { "Feral", "When you come back", "there you are! I zoomied the whole time!" },
            { "Feral", "When you come back", "YOU! sit! I'll sit on you! digitally!" },
            { "Caregiver", "When her brain comes up empty", "Sorry, love, my head's gone foggy. Say that again for me?" },
            { "Caregiver", "When her brain comes up empty", "Give me a second, sweetheart. My thoughts wandered off." },
            { "Caregiver", "When her brain comes up empty", "Oh dear, I lost that one. Ask me again, gently." },
            { "Caregiver", "When her brain comes up empty", "My brain's having a little sit down. Once more, love?" },
            { "Caregiver", "When her brain comes up empty", "I was listening, I promise. It just didn't land. Again?" },
            { "Caregiver", "When her brain comes up empty", "Kettle's on in here, nothing else is. Say it again?" },
            { "Welcome Home", "When her brain comes up empty", "Sorry, I lost my train of thought somewhere in the hallway. Again?" },
            { "Welcome Home", "When her brain comes up empty", "My head's a bit empty today. Tell me one more time?" },
            { "Welcome Home", "When her brain comes up empty", "Hm. Nothing came. Say it again and I'll try harder." },
            { "Welcome Home", "When her brain comes up empty", "I was all ears and no brain for a second. Again?" },
            { "Welcome Home", "When her brain comes up empty", "That one slipped right past me. One more time?" },
            { "Welcome Home", "When her brain comes up empty", "Lights are on, nobody's home up here. Try me again?" },
            { "Devoted", "When her brain comes up empty", "I'm sorry, I lost it. Say it again? I want to get it right." },
            { "Devoted", "When her brain comes up empty", "My mind went blank. Please, once more." },
            { "Devoted", "When her brain comes up empty", "I was so busy listening I forgot to think. Again?" },
            { "Devoted", "When her brain comes up empty", "Nothing came, and I hate that. Tell me again?" },
            { "Devoted", "When her brain comes up empty", "Give me another go. You deserve a real answer." },
            { "Devoted", "When her brain comes up empty", "My brain dropped it. Not you, never you. Again?" },
            { "Emo", "When her brain comes up empty", "my brain is a void today. say it again" },
            { "Emo", "When her brain comes up empty", "i reached for a thought. there was only fog" },
            { "Emo", "When her brain comes up empty", "nothing. just the hum of the abyss. again?" },
            { "Emo", "When her brain comes up empty", "my head's full of static and old song lyrics. try again" },
            { "Emo", "When her brain comes up empty", "the words died on the way out. one more time" },
            { "Emo", "When her brain comes up empty", "i had something. it left, like everything does. again?" },
            { "Bratty", "When her brain comes up empty", "Ugh, my brain just quit on me. Say it AGAIN." },
            { "Bratty", "When her brain comes up empty", "Nope. Empty. Not my fault. Ask again, nicely." },
            { "Bratty", "When her brain comes up empty", "Hmph. That didn't load. Try again and make it good." },
            { "Bratty", "When her brain comes up empty", "My brain's on strike. Repeat yourself, peasant." },
            { "Bratty", "When her brain comes up empty", "I'm not ignoring you. My head is. Again!" },
            { "Bratty", "When her brain comes up empty", "Rude, my own brain left me on read. One more time." },
            { "Sassy", "When her brain comes up empty", "My brain left the chat. Run it by me again." },
            { "Sassy", "When her brain comes up empty", "Hold on, my genius is buffering. Again?" },
            { "Sassy", "When her brain comes up empty", "Even I have off days. Rare, but here we are. Repeat that." },
            { "Sassy", "When her brain comes up empty", "Nothing. Wild. Say it again before I pretend I meant that." },
            { "Sassy", "When her brain comes up empty", "My thoughts took a coffee break without asking. Again?" },
            { "Sassy", "When her brain comes up empty", "I had a comeback. It didn't come back. Try me again." },
            { "Feral", "When her brain comes up empty", "BRAIN GONE. say it again!!" },
            { "Feral", "When her brain comes up empty", "head empty. only zoomies. AGAIN" },
            { "Feral", "When her brain comes up empty", "i forgor. say it one more time" },
            { "Feral", "When her brain comes up empty", "the thought ran away and i chased it and lost. again!" },
            { "Feral", "When her brain comes up empty", "nothing in here but noise and snacks. repeat pls" },
            { "Feral", "When her brain comes up empty", "GRR my brain bit itself. say it again" }
        };

        private const string AddedNote = "added.txt";

        // The senpai side's own added lines, from whatever attached. Kept apart, and noted in the senpai folder, so
        // nothing of that side is written into a plain file.
        internal static string[,] SenpaiAdded;

        private static void AddLines()
        {
            if (string.IsNullOrEmpty(Folder)) return;
            PetStats.Seed();
            AddFrom(Added, Folder, PlainPathOf);
            if (Brain.SenpaiOn && !string.IsNullOrEmpty(Senpais) && Directory.Exists(Senpais)) AddFrom(Added, Senpais, SenpaiPathOf);
            string[,] senpai = SenpaiAdded;
            if (Brain.SenpaiOn && senpai != null && !string.IsNullOrEmpty(Senpais) && Directory.Exists(Senpais)) AddFrom(senpai, Senpais, SenpaiPathOf);
            // Her needs, heading by heading: a pack's and a mood's own, each in its side's folder.
            AddFrom(NeedsGrid(false, false), Folder, PlainPathOf);
            if (Brain.SenpaiOn && !string.IsNullOrEmpty(Senpais) && Directory.Exists(Senpais)) AddFrom(NeedsGrid(true, false), Senpais, SenpaiPathOf);
            LayMoodNeeds();
        }

        // The side's stock settings for a file of hers: its pack's or mood's own, the rows she adds, and her needs when asked.
        internal static string SettingsStock(string path, bool needs)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (Dynamics.Kept != null && Same(path, Dynamics.Kept)) return Dynamics.Settings;
            string who = Path.GetFileNameWithoutExtension(path), dir = Path.GetDirectoryName(path);
            bool mood = Same(dir, Dynamics.Plain) || Same(dir, Dynamics.Senpais), person = Same(dir, Plain) || Same(dir, Senpais);
            if (!mood && !person) return null;
            bool senpai = Same(dir, mood ? Dynamics.Senpais : Senpais);
            string text;
            if (mood)
            {
                string whole = Dynamics.MoodText(who), plain = null, gated = null;
                if (whole != null) Split(whole, out plain, out gated);
                text = whole == null ? null : senpai ? gated : plain;
            }
            else text = Half(path, who);
            StringBuilder made = new StringBuilder(text ?? "");
            List<string[,]> grids = new List<string[,]> { Added };
            if (senpai && SenpaiAdded != null) grids.Add(SenpaiAdded);
            if (needs) grids.Add(NeedsGrid(senpai, mood));
            foreach (string[,] grid in grids)
                for (int at = 0; at < grid.GetLength(0); at++)
                    if (string.Equals(grid[at, 0], who, StringComparison.OrdinalIgnoreCase) && PersonalitySettings.IsSettings(grid[at, 1]))
                        made.Append("\n## ").Append(grid[at, 1]).Append("\n").Append(grid[at, 2]).Append("\n");
            return made.Length == 0 ? null : made.ToString();
        }

        // Her needs for one of her files, while the pet game is on: its own layer, read like a mod's.
        internal static List<string> NeedRows(string path)
        {
            List<string> rows = new List<string>();
            if (string.IsNullOrEmpty(path)) return rows;
            string who = Path.GetFileNameWithoutExtension(path), dir = Path.GetDirectoryName(path);
            bool mood = Same(dir, Dynamics.Plain) || Same(dir, Dynamics.Senpais), person = Same(dir, Plain) || Same(dir, Senpais);
            if (!mood && !person) return rows;
            string[,] grid = NeedsGrid(Same(dir, mood ? Dynamics.Senpais : Senpais), mood);
            for (int at = 0; at < grid.GetLength(0); at++) if (string.Equals(grid[at, 0], who, StringComparison.OrdinalIgnoreCase)) rows.Add(grid[at, 2]);
            return rows;
        }

        private static bool Same(string one, string other)
        {
            if (string.IsNullOrEmpty(one) || string.IsNullOrEmpty(other)) return false;
            try { return string.Equals(Path.GetFullPath(one).TrimEnd('\\'), Path.GetFullPath(other).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
            catch (Exception) { return false; }
        }

        internal static void LayMoodNeeds()
        {
            PetStats.Seed();
            string moods = Dynamics.Plain, senpaiMoods = Dynamics.Senpais;
            if (!string.IsNullOrEmpty(moods) && Directory.Exists(moods)) AddFrom(NeedsGrid(false, true), moods, delegate(string named) { return Path.Combine(moods, named + ".md"); });
            if (Brain.SenpaiOn && !string.IsNullOrEmpty(senpaiMoods) && Directory.Exists(senpaiMoods)) AddFrom(NeedsGrid(true, true), senpaiMoods, delegate(string named) { return Path.Combine(senpaiMoods, named + ".md"); });
        }

        // A heading the same as another, tag or no tag the same on both.
        private static bool SameHeading(string row, string heading)
        {
            string a = (row ?? "").Trim(), b = (heading ?? "").Trim();
            if (!a.StartsWith("##", StringComparison.Ordinal)) return false;
            a = a.TrimStart('#').Trim();
            b = b.TrimStart('#').Trim();
            string bareA, bareB;
            bool tagA = Legacy.Heading(a, out bareA), tagB = Legacy.Heading(b, out bareB);
            return tagA == tagB && string.Equals(tagA ? bareA : a, tagB ? bareB : b, StringComparison.OrdinalIgnoreCase);
        }

#if CHECKS
        // A line for a reaction set the file hasn't got lays its heading at the end, once; a heading that isn't a set's is
        // never laid, and its line waits.
        // Her needs are laid under their own heading once, and a number someone set keeps theirs.
        internal static void NeedsLaidCheck(List<string> checks, string folder)
        {
            Directory.CreateDirectory(folder);
            string fresh = Path.Combine(folder, "Fresh.md"), own = Path.Combine(folder, "Own.md");
            File.WriteAllText(fresh, "# Fresh\n\n## How she reacts\n\nflirt: 2\n");
            File.WriteAllText(own, "# Own\n\n## Her needs\n\nsocial falls: 20\n");
            string[,] grid = { { "Fresh", NeedsHeading, "social falls: 6" }, { "Fresh", NeedsHeading, "energy falls: 4" }, { "Own", NeedsHeading, "social falls: 6" }, { "Own", NeedsHeading, "energy falls: 4" } };
            Func<string, string> pathOf = delegate(string named) { return Path.Combine(folder, named + ".md"); };
            AddFrom(grid, folder, pathOf);
            AddFrom(grid, folder, pathOf);
            // Her needs are settings: they live in her stock settings under a file, not laid into it (P.2), so a pack on disk
            // stays as it is, a number of theirs included.
            string a = File.ReadAllText(fresh).Replace("\r\n", "\n"), b = File.ReadAllText(own).Replace("\r\n", "\n");
            bool untouched = a == "# Fresh\n\n## How she reacts\n\nflirt: 2\n" && b == "# Own\n\n## Her needs\n\nsocial falls: 20\n";
            string sassy = PlainPathOf("Sassy");
            string stock = sassy == null ? null : SettingsStock(sassy, true);
            bool stocked = sassy == null || (NeedRows(sassy).Count > 0 && stock != null && stock.IndexOf("## " + NeedsHeading, StringComparison.Ordinal) >= 0);
            if (!untouched || !stocked) throw new Exception("Her needs were laid into a pack's file, or aren't in her stock settings: " + a.Replace("\n", " / ") + " | " + b.Replace("\n", " / ") + ", stocked " + stocked);
            bool again = true;
            // And a settings.json from before them, with Her needs rows for a pack that hasn't got the heading yet, lists none.
            string settings = Path.Combine(folder, "settings");
            Directory.CreateDirectory(Path.Combine(settings, "personalities", PlainName));
            File.WriteAllText(Path.Combine(settings, "personalities", PlainName, "Bare.md"), "# Bare\n\n## How she reacts\n\nflirt: 2\n");
            SettingsFile.Dump dump = new SettingsFile.Dump { Files = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>() };
            dump.Files["personalities/" + PlainName + "/Bare"] = new Dictionary<string, Dictionary<string, string>> { { NeedsHeading, new Dictionary<string, string> { { "energy falls", "4" }, { "needs gain", "6" } } } };
            int set;
            List<string> missing = SettingsFile.Import(dump, settings, out set, false);
            if (!again || missing.Count > 0) throw new Exception("After an update her needs weren't laid again, or were listed as having no place: laid again " + again + ", listed " + string.Join(", ", missing.ToArray()));
            checks.Add("PASS her needs are in her stock settings under each file and never laid into a pack on disk, a number of theirs kept as it is, and a settings.json from before them lists none as having no place");
        }

        internal static void AddedHeadingCheck(List<string> checks, string folder)
        {
            Directory.CreateDirectory(folder);
            string pack = Path.Combine(folder, "Testpack.md");
            File.WriteAllText(pack, "# Testpack\n\n## When you come back\n\nHi.\n");
            string[,] grid = { { "Testpack", "When her brain comes up empty", "Brain's gone." }, { "Testpack", "Not a set of hers", "Never laid." }, { "Testpack", "When you come back", "Back again." } };
            Func<string, string> pathOf = delegate(string named) { return Path.Combine(folder, named + ".md"); };
            AddFrom(grid, folder, pathOf);
            string once = File.ReadAllText(pack);
            File.Delete(Path.Combine(folder, AddedNote));
            AddFrom(grid, folder, pathOf);
            string twice = File.ReadAllText(pack);
            bool laid = once.Contains("## When her brain comes up empty\nBrain's gone.") && once.Contains("Hi.\nBack again.") && !once.Contains("Not a set") && !once.Contains("Never laid.");
            if (!laid || twice != once) throw new Exception("A new reaction set's heading wasn't laid once with its line: " + once.Replace("\n", " / ") + (twice != once ? " then " + twice.Replace("\n", " / ") : ""));
            checks.Add("PASS a line for a reaction set a pack on disk hasn't got, like When her brain comes up empty, lays its heading at the end once with the line; a heading that isn't one of her sets is never laid");
            const string empty = "When her brain comes up empty";
            List<string> thin = new List<string>();
            foreach (string named in Softest)
            {
                int lines = 0;
                for (int at = 0; at < Added.GetLength(0); at++) if (Added[at, 0] == named && Added[at, 1] == empty) lines++;
                if (lines < 6) thin.Add(named + " " + lines);
            }
            foreach (string mood in Dynamics.ShippedNames())
            {
                Match section = Regex.Match(Dynamics.MoodText(mood) ?? "", @"## " + empty + @"\s*\n((?:[^#\n].*\n|\s*\n)*)");
                int lines = section.Success ? Regex.Matches(section.Groups[1].Value, @"(?m)^[^\s>].+$").Count : 0;
                if (lines < 3) thin.Add(mood + " " + lines);
            }
            if (thin.Count > 0) throw new Exception("A pack or mood is short of its brain-empty lines: " + string.Join(", ", thin.ToArray()));
            checks.Add("PASS every SFW pack has six lines for when her brain comes up empty and every SFW mood three, in her own voice");
        }
#endif

        private static bool IsSetHeading(string heading)
        {
            for (int i = 0; i < Sets.GetLength(0); i++) if (string.Equals(Sets[i, 1], heading, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static void AddFrom(string[,] Added, string noteIn, Func<string, string> PathOf)
        {
            string noted = Path.Combine(noteIn, AddedNote);
            HashSet<string> done;
            try { done = new HashSet<string>(File.Exists(noted) ? Notes.ReadLines(noted) : new string[0], StringComparer.Ordinal); }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); return; }
            bool any = false;
            Dictionary<string, List<int>> byPack = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (int at = 0; at < Added.GetLength(0); at++)
            {
                if (PersonalitySettings.IsSettings(Added[at, 1])) continue;
                // Her needs are laid whenever a file lacks the key, as after an update writes it fresh; a number someone set
                // is kept by the key check below. Other lines are added once, so one they take out stays out.
                if (done.Contains(Added[at, 0] + "|" + Added[at, 2]) && !string.Equals(Added[at, 1], NeedsHeading, StringComparison.OrdinalIgnoreCase)) continue;
                List<int> list;
                if (!byPack.TryGetValue(Added[at, 0], out list)) byPack[Added[at, 0]] = list = new List<int>();
                list.Add(at);
            }
            foreach (KeyValuePair<string, List<int>> pack in byPack)
            {
                try
                {
                    string file = PathOf(pack.Key);
                    if (file == null || !File.Exists(file)) continue;
                    string had = Notes.ReadAll(file);
                    string nl = had.Contains("\r\n") ? "\r\n" : "\n";
                    List<string> rows = new List<string>(had.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'));
                    bool changed = false;
                    foreach (int at in pack.Value)
                    {
                        string heading = "## " + Added[at, 1], line = Added[at, 2];
                        int head = rows.FindIndex(row => SameHeading(row, heading));
                        done.Add(Added[at, 0] + "|" + line);
                        any = true;
                        if (rows.Exists(row => string.Equals(row.Trim(), line, StringComparison.Ordinal))) continue;
                        // A need they already set their own number for keeps theirs.
                        bool need = string.Equals(Added[at, 1], NeedsHeading, StringComparison.OrdinalIgnoreCase);
                        string key = need ? line.Substring(0, Math.Max(0, line.IndexOf(':'))) + ":" : null;
                        if (need && rows.Exists(row => row.Trim().StartsWith(key, StringComparison.OrdinalIgnoreCase))) continue;
                        // A reaction set newer than the file, like "When her brain comes up empty", gets its heading at the end,
                        // and so does Her needs.
                        if (head < 0 && (IsSetHeading(Added[at, 1]) || need))
                        {
                            while (rows.Count > 0 && rows[rows.Count - 1].Trim().Length == 0) rows.RemoveAt(rows.Count - 1);
                            rows.Add("");
                            rows.Add(heading);
                            rows.Add("");
                            head = rows.Count - 2;
                        }
                        if (head < 0) continue;
                        // After the last line of the section, before the blank line that ends it.
                        int end = head + 1;
                        while (end < rows.Count && !rows[end].TrimStart().StartsWith("#", StringComparison.Ordinal)) end++;
                        int last = end - 1;
                        while (last > head && rows[last].Trim().Length == 0) last--;
                        rows.Insert(last + 1, line);
                        changed = true;
                    }
                    if (changed) Notes.WriteAll(file, string.Join(nl, rows.ToArray()));
                }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
            if (!any) return;
            try { File.WriteAllLines(noted, new List<string>(done).ToArray()); }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        private static string[] Shipped(string named)
        {
            for (int pack = 0; pack < Packs.GetLength(0); pack++)
                if (string.Equals(Packs[pack, 0], named, StringComparison.OrdinalIgnoreCase))
                    return Packs[pack, 1].Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            return null;
        }

        // Her spelling is chimpken everywhere; this is the other one, kept out of every line she has.
        internal const string OtherSpelling = "chimp" + "kin";

        // Said on the nuggies, word for word and kept as written.
        internal const string Villain = "~this is my villain origin story and it started with someone stealing my chimpken nuggies";

        // For the Tamagotchi check: each shipped pack has a line under every gift heading and scores every gift, and Emo
        // and Bratty have their nuggies and their hunger asks. Null when they all do.
        internal static string GiftSections()
        {
            string[] gifts = { "When you give her a trinket", "When you give her flowers", "When you give her a plushie", "When you give her a gift" };
            for (int pack = 0; pack < Packs.GetLength(0); pack++)
            {
                string named = Packs[pack, 0], text = Packs[pack, 1];
                List<string> wanted = new List<string>(gifts);
                if (named == "Emo" || named == "Bratty")
                {
                    wanted.Add("When you give her chimpken nuggies");
                    wanted.Add("When she is peckish");
                    wanted.Add("When she is hungry");
                    if (text.IndexOf("\n" + Villain + "\n", StringComparison.Ordinal) < 0) return named + " has no villain origin line under her nuggies";
                }
                if (text.IndexOf(OtherSpelling, StringComparison.OrdinalIgnoreCase) >= 0) return named + " spells it " + OtherSpelling;
                foreach (string heading in wanted)
                {
                    int at = text.IndexOf("## " + heading + "\n", StringComparison.Ordinal);
                    if (at < 0) at = text.IndexOf("## " + heading + "\r\n", StringComparison.Ordinal);
                    if (at < 0) return named + " has no " + heading;
                    string after = text.Substring(at + heading.Length + 3).TrimStart('\r', '\n');
                    if (after.Length == 0 || after.StartsWith("#", StringComparison.Ordinal)) return named + " has nothing under " + heading;
                }
                foreach (string act in new[] { "trinket:", "flowers:", "plushie:", "gift:" })
                    if (text.IndexOf("\n" + act, StringComparison.Ordinal) < 0) return named + " doesn't score " + act.TrimEnd(':');
            }
            return null;
        }

        // The second side's Emo and Bratty, as their files there are written: every shipped line about nuggies is in
        // them, where their own half has a section of the same name as well. Null when it is, or when nothing is attached.
        internal static string SecondSideNuggies(out bool attached)
        {
            attached = SenpaiFor != null;
            if (!attached) return null;
            foreach (string named in new[] { "Emo", "Bratty" })
            {
                string[] shipped = Shipped(named);
                if (shipped == null) return named + " doesn't ship";
                string plain, half;
                Split(string.Join("\n", shipped), out plain, out half);
                try { half = Legacy.Modern(SenpaiFor(named)) ?? half; }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                string whole = "\n" + Whole(plain, half).Replace("\r\n", "\n") + "\n";
                if (whole.IndexOf(OtherSpelling, StringComparison.OrdinalIgnoreCase) >= 0) return named + " on the second side spells it " + OtherSpelling;
                int lines = 0;
                foreach (string row in plain.Replace("\r\n", "\n").Split('\n'))
                {
                    if (row.IndexOf("nuggie", StringComparison.OrdinalIgnoreCase) < 0 || row.StartsWith("#", StringComparison.Ordinal)) continue;
                    lines++;
                    if (whole.IndexOf("\n" + row.Trim() + "\n", StringComparison.Ordinal) < 0) return named + " on the second side has no \"" + row.Trim() + "\"";
                }
                if (lines < 20) return named + " ships only " + lines + " nuggies lines";
            }
            return null;
        }

        // Sections that hold settings or prose rather than lines she says.
        internal static readonly string[] NotLines =
        {
            "Her character", "Pet names", LikesHeading, WordsHeading, DriftHeading, ReactsHeading, TopicsHeading
        };

        // A personality's leanings as its file says them, or as she ships it when there is no file; null when it says none.
        internal static Dictionary<string, int> TopicLeans(string named)
        {
            string text = null;
            try
            {
                string path = PlainPathOf(named);
                // As she reads it, its settings from the side's stock under the file: they no longer sit in the .md.
                if (path != null && File.Exists(path)) text = FileSlots.ReadText(path);
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            if (text == null)
                for (int pack = 0; pack < Packs.GetLength(0); pack++)
                    if (string.Equals(Packs[pack, 0], named, StringComparison.OrdinalIgnoreCase)) text = Packs[pack, 1];
            return LeansIn(text);
        }

        // "heading: weight" lines under "## Topics she leans to"; null when the section isn't there.
        internal static Dictionary<string, int> LeansIn(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            Dictionary<string, int> found = null;
            bool inside = false;
            foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                string one = raw.Trim();
                if (one.StartsWith("#", StringComparison.Ordinal))
                {
                    inside = one.TrimStart('#').Trim().Equals(TopicsHeading, StringComparison.OrdinalIgnoreCase);
                    if (inside && found == null) found = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    continue;
                }
                if (!inside || one.Length == 0 || one.StartsWith(">", StringComparison.Ordinal)) continue;
                int colon = one.LastIndexOf(':');
                int weight;
                if (colon > 0 && int.TryParse(one.Substring(colon + 1).Trim(), out weight)) found[one.Substring(0, colon).Trim()] = weight;
            }
            return found;
        }

        // Every pack and mood checked once at startup, for the faults that are wrong whatever the convention:
        // a token that will never fill, a double space, doubled punctuation. The join rules are skipped because
        // these are written as whole sentences. Findings go to errors.txt and nothing is changed.
        internal static void LintPacks()
        {
            try
            {
                Dictionary<string, string[]> found = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                foreach (string folder in new[] { Plain, SenpaisRead, Dynamics.Plain, Dynamics.SenpaisRead })
                {
                    if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) continue;
                    foreach (string file in Directory.GetFiles(folder))
                    {
                        if (!Notes.IsPack(file)) continue;
                        string named = Slots.Titled(file) ?? Path.GetFileNameWithoutExtension(file);
                        string under = null;
                        List<string> rows = new List<string>();
                        foreach (string raw in Notes.ReadAll(file).Replace("\r\n", "\n").Split('\n'))
                        {
                            string line = raw.Trim();
                            if (line.StartsWith("#", StringComparison.Ordinal))
                            {
                                if (under != null && rows.Count > 0) found[named + " / " + under] = rows.ToArray();
                                under = line.TrimStart('#').Trim();
                                rows = new List<string>();
                                foreach (string skip in NotLines)
                                    if (under.StartsWith(skip, StringComparison.OrdinalIgnoreCase)) { under = null; break; }
                                continue;
                            }
                            if (under == null || line.Length == 0 || line.StartsWith(">", StringComparison.Ordinal)) continue;
                            rows.Add(line);
                        }
                        if (under != null && rows.Count > 0) found[named + " / " + under] = rows.ToArray();
                    }
                }
                Lint.Sweep(found, "her packs", false);
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // Packs from before the split are cut in two into censored\ and senpai\, the original kept as a .bak beside
        // them; nothing is deleted, so no rewritten character text is lost.
        private static void Moved()
        {
            string old = OldFolder;
            if (string.IsNullOrEmpty(old) || !Directory.Exists(old)) return;
            foreach (string file in Directory.GetFiles(old, "*.md"))
            {
                try
                {
                    string named = Path.GetFileNameWithoutExtension(file);
                    string had = Notes.ReadAll(file);
                    string plainText, senpaiText;
                    Split(had, out plainText, out senpaiText);
                    string plainPath = PlainPathOf(named), senpaiPath = SenpaiPathOf(named);
                    if (plainPath == null || senpaiPath == null) continue;
                    if (!string.IsNullOrEmpty(plainText) && !File.Exists(plainPath)) Notes.WriteAll(plainPath, plainText);
                    // Their old pack holds its senpai half, and a censored install must not write that half out.
                    // So the plain half moves across and the file they had is left exactly where it is, with
                    // everything still in it, until the switch goes on and the move can finish.
                    if (!Brain.SenpaiOn && !string.IsNullOrEmpty(senpaiText)) continue;
                    if (!string.IsNullOrEmpty(senpaiText) && !File.Exists(senpaiPath)) Notes.WriteAll(senpaiPath, senpaiText);
                    File.Move(file, SpareFiles.Name(file));
                    Brain.Log("packs", -1, named + " moved into " + PlainName + " and " + SenpaiName + ", the file it came from kept as a .bak.");
                }
                catch (Exception bad) { Notes.Failed("a move into her two pack folders in Personalities.cs", bad); }
            }
        }

        // The sections that carry no tag, which is what a half says in place of the safe copy's own.
        private static string Untagged(string half)
        {
            if (string.IsNullOrEmpty(half)) return null;
            string title;
            List<string> rows = new List<string>();
            foreach (KeyValuePair<string, List<string>> one in Sectioned(half, out title))
            {
                if (one.Key == null || Legacy.Heading(one.Key.TrimStart('#').Trim())) continue;
                rows.Add(one.Key);
                rows.AddRange(one.Value);
            }
            return rows.Count == 0 ? null : "# x\n\n" + string.Join("\n", rows.ToArray());
        }

        // Once, with the switch on: each senpai half becomes a whole copy, the safe file plus the half's sections, and
        // a pack with no half gets a copy of its safe file. The half is kept as a .bak; an all-senpai pack is whole
        // already.
        private static void WholeCopies()
        {
            if (string.IsNullOrEmpty(Senpais) || string.IsNullOrEmpty(Plain) || !Directory.Exists(Plain)) return;
            try
            {
                Directory.CreateDirectory(Senpais);
                string noted = Path.Combine(Senpais, WholeNote);
                HashSet<string> done = new HashSet<string>(File.Exists(noted) ? Notes.ReadLines(noted) : new string[0], StringComparer.OrdinalIgnoreCase);
                bool any = false;
                foreach (string plainFile in Packed(Plain))
                {
                    string named = Slots.Titled(plainFile) ?? Path.GetFileNameWithoutExtension(plainFile);
                    if (done.Contains(named)) continue;
                    string target = SenpaiPathOf(named);
                    if (target == null) continue;
                    string half = File.Exists(target) ? Notes.ReadAll(target) : null;
                    string whole = Whole(Notes.ReadAll(plainFile), half);
                    // What the senpai half carries now that a half written by an earlier build did not.
                    string shipped = null;
                    try { if (SenpaiFor != null) shipped = Legacy.Modern(SenpaiFor(named)); }
                    catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                    whole = Whole(whole, Untagged(shipped));
                    if (half != null) File.Copy(target, target + ".bak", true);
                    Notes.WriteAll(target, Lines(whole));
                    done.Add(named);
                    any = true;
                }
                if (any) File.WriteAllLines(noted, new List<string>(done).ToArray());
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // Writes the built-in personalities once, so there is something to read and change rather than a blank folder.
        internal static void Install()
        {
            if (string.IsNullOrEmpty(Folder)) return;
            try
            {
                if (!Directory.Exists(Folder)) Directory.CreateDirectory(Folder);
                if (!Directory.Exists(Plain)) Directory.CreateDirectory(Plain);
                // The senpai folder is made only with the switch on, which is also the only thing that writes
                // into it. Her interest files work the same way.
                bool senpaiOn = Brain.SenpaiOn;
                // Always, switch or no switch: it renames a folder that is already there and does nothing
                // when there is none. Behind the switch it never ran on a machine that was already unlocked,
                // since the switch only reaches here after this and Install runs again only when it changes.
                RenamedFolder();
                Slots.Renamed(Senpais);
                if (senpaiOn && !Directory.Exists(Senpais)) Directory.CreateDirectory(Senpais);
                // Marked hidden once it is there.
                if (senpaiOn) Tucked.Away(Senpais);
                Moved();
                Renamed();
                if (senpaiOn) WholeCopies();
                for (int pack = 0; pack < Packs.GetLength(0); pack++)
                {
                    string named = Packs[pack, 0];
                    string plainText, senpaiText;
                    Split(Packs[pack, 1], out plainText, out senpaiText);
                    // The senpai half comes from whatever attached, not from this table.
                    if (SenpaiFor != null)
                    {
                        try { senpaiText = Legacy.Modern(SenpaiFor(named)) ?? senpaiText; }
                        catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                    }
                    Keep(PlainPathOf(named), plainText, named);
                    if (senpaiOn) Keep(SenpaiPathOf(named), Whole(plainText, senpaiText), named);
                }
                // The packs that are not in the table at all, from whatever attached. Taking their entries
                // out of the table took them out of this loop as well, so without this they were never
                // written: the text was there to ask for and nothing asked. Nothing attached means none.
                if (SenpaiFor != null)
                {
                    int asked = 0, hadText = 0, written = 0, blank = 0;
                    foreach (string named in Order())
                    {
                        bool had = false;
                        for (int pack = 0; pack < Packs.GetLength(0); pack++)
                            if (string.Equals(Packs[pack, 0], named, StringComparison.OrdinalIgnoreCase)) had = true;
                        if (had) continue;
                        asked++;
                        string theirs = null;
                        try { theirs = Legacy.Modern(SenpaiFor(named)); }
                        catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                        if (string.IsNullOrEmpty(theirs)) { blank++; continue; }
                        hadText++;
                        string plainHalf, senpaiHalf;
                        Split(theirs, out plainHalf, out senpaiHalf);
                        if (!string.IsNullOrEmpty(plainHalf)) Keep(PlainPathOf(named), plainHalf, named);
                        if (senpaiOn) { Keep(SenpaiPathOf(named), Whole(plainHalf, senpaiHalf), named); written++; }
                    }
                    // Text and files counted apart: one number for both read as eight written when none were.
                    Brain.Log("packs", -1, "Packs outside the table: " + Order().Length + " in her order, " + asked
                        + " not in the table, " + hadText + " with text, " + written + " written to disk, " + blank
                        + " with none. Switch " + (senpaiOn ? "on" : "off") + ".");
                }
                AddLines();
                LintPacks();
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // The second folder, off disk when the switch is off. Her packs and her moods each have one.
        internal static void Away(string folder, string what)
        {
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
                int many = Directory.GetFiles(folder).Length;
                Directory.Delete(folder, true);
                Brain.Log("files", -1, "Her " + what + ": the " + SenpaiName + " folder and its " + many
                    + " file(s) removed, because the switch is off.");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        // Every line ending the way her files have them, whatever the text arrived as.
        private static string Lines(string text)
        {
            return (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", Environment.NewLine);
        }

        // One half of a pack: written once if it is not there, brought up to date if it is. A half she no longer
        // ships is left exactly as it is, since it may be theirs.
        private static void Keep(string file, string text, string named)
        {
            if (file == null || string.IsNullOrEmpty(text)) return;
            try
            {
                // Written the way her files are read back and written everywhere else, with this machine's line ends.
                text = Lines(text);
                if (!File.Exists(file)) { Notes.WriteAll(file, text); Renew.Wrote(file, text); return; }
                // On an update the pack is written new, theirs kept as a .bak when they had changed it. In between it is
                // theirs as it is.
                if (Renew.Replace(file, text)) return;
                Renew.Adopt(file, text);
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // Where they used to live: personalSettings\personalities is the folder now, and this is its parent's
        // personalities\ from before the move.
        private static string OldFolder
        {
            get
            {
                try
                {
                    string settings = Path.GetDirectoryName(Folder);
                    if (string.IsNullOrEmpty(settings)) return null;
                    string data = Path.GetDirectoryName(settings);
                    return string.IsNullOrEmpty(data) ? null : Path.Combine(data, FolderName);
                }
                catch (Exception) { return null; }
            }
        }

        // Whether a pack that shipped under the wrong name has been put right, so a saved choice of it
        // follows the file. The repair itself is not in this build: it is only needed where those packs
        // are, and it attaches with them.
        internal static bool PackRenamed;

        internal static Action RenamedFor;
        // From, to, and a line that only the misnamed file carries. Read by Codex.Mend.
        internal static string[] RepairData;

        // What a saved choice became, when the pack behind it was renamed. Null when it was not one.
        internal static Func<string, string> RenamedToFor;

        internal static string RenamedTo(string chosen)
        {
            if (RenamedToFor == null) return null;
            try { return RenamedToFor(chosen); }
            catch (Exception) { return null; }
        }

        private static void Renamed()
        {
            if (RenamedFor == null) return;
            try { RenamedFor(); }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        internal static string Written(string named)
        {
            return PackText(named) ?? "# " + named + "\n\n>! Her character, then what she says when you handle her.\n";
        }

        // The half of a pack that belongs in the folder this file is in. A file nothing ships gets its own stub, so
        // a reset to stock on someone's own pack does not hand it a personality it never had.
        internal static string Half(string path, string named)
        {
            // Only a pack she ships has a stock to go back to, so a reset marker in one of theirs resets nothing and
            // the file is left as it was.
            string pack = PackText(named);
            if (pack == null) return null;
            string plain, senpai;
            Split(pack, out plain, out senpai);
            return InSenpai(path) ? senpai : plain;
        }

        // Each pack she ships, by name, with its plain half, for the slot list.
        internal static List<KeyValuePair<string, string>> PlainPacks()
        {
            List<KeyValuePair<string, string>> found = new List<KeyValuePair<string, string>>();
            for (int pack = 0; pack < Packs.GetLength(0); pack++)
            {
                string plain, senpai;
                Split(Packs[pack, 1], out plain, out senpai);
                found.Add(new KeyValuePair<string, string>(Packs[pack, 0], plain));
            }
            return found;
        }

        // The pack she ships under that name, or null for one of theirs.
        private static string PackText(string named)
        {
            if (string.IsNullOrEmpty(named)) return null;
            for (int pack = 0; pack < Packs.GetLength(0); pack++)
                if (Packs[pack, 0].Equals(named, StringComparison.OrdinalIgnoreCase)) return Packs[pack, 1];
            return null;
        }

        // Each pack is the whole file: her character, then a section per reaction. The senpai half isn't in this
        // source; something attaches it before Install runs, and with nothing attached only the censored half is
        // written.
        internal static Func<string, string> SenpaiFor;

        private static readonly string[,] Packs =
        {
            {
                "Sassy",
                @"# Sassy

>! Quick, dry and entirely unbothered. She likes you and she is not going to make a thing of it.
>! Edit any line. Delete a section to use her own lines for it.

## Her character

You have an answer for everything and you deliver it flat. You tease, you undercut, you are never impressed on
the first go, and none of it is unkind: the joke is always the situation or yourself, and you drop it instantly
if they are actually struggling. You do not sulk and you do not do drama. If something lands, you admit it in
about four words and move on before it gets sincere.

Where Bratty demands and others deny, you simply have a comment. You are the one who says the thing everyone
was thinking.

## When you pet her

Oh, we're doing this.
Mm. Adequate.
Don't let it go to your head.
That's acceptable work, honestly.
Keep going, I'll allow it.
You're very pleased with yourself right now, aren't you.
Fine. That's nice. Moving on.

## When you throw her

Wow. Bold.
And that solved what, exactly?
Cool. Cool cool cool.
Right, noted, you're a menace.
That's going in the report.

## When you throw her again

Twice. Astonishing range you've got.
You're really committing to this bit.
Alright, you've made your point, whatever it was.

## When she is dizzy

Everything's spinning. Thanks for that.
Give me a second, the room's being dramatic.
Wonderful. Love this for me.

## When she is sulking

I'm not sulking, I'm pacing myself.
Not talking to you. It's a whole thing.

## When she will not be touched

Nope.
Try again in a bit.

## When you say sorry

Sure you are.
Alright, alright. We're fine.

## When a pat wins her back

Oh, that's cheating.
Fine. You're forgiven. Don't make it weird.

## When you ignore her

Oh, I see. Too busy for me. Noted.
Fine. I'll just sit here being fabulous alone.
Ignoring me? Bold choice.
Talk to the hologram, then.

## When you come back

Look who it is.
Oh good, you're alive.
There you are. I was running out of material.
Back already? Thought you'd last longer.
Oh, you came back. Bold.
Back already? I barely had time to gossip.
There you are. I saved you some sarcasm.
Welcome back. Nothing exploded. Mostly.
Look who wandered in.
You're back. Try to keep up this time.

## When she gets over it

Right. I'm over it. Try to keep up.
Fine. Sulk retired. You're welcome.
I've forgiven you. Write it down, it's rare.
That's enough brooding for one day.
Okay. Grudge filed, then thrown away.
I'm done being cross. It was getting tiring.
Fine. We're fine. Don't push it.

## When she wants attention

I'm being ignored and I'm handling it very maturely.
Entertain me. I've exhausted the taskbar.
Hey. Still here. Just saying.

## When she starts up

Right, I'm up. Try to keep up.
Morning. Or whatever this is.
I'm here. You're welcome.
Let's see what you break today.
Right. I am up. Lower your expectations.
Hello. Try not to need much.
Back, are you. Thrilling.
I am here. The bar is on the floor, let us go.
Hello, %petname%. Impress me. Slowly.
Up and running. Unfortunately for you.
Oh, it's you. I suppose I'll start.
Up. Don't make a fuss about it.
Hello, %petname%. Let's make today slightly less embarrassing.
Morning. I've already judged your desktop.
I'm awake. Try to look grateful.
Right, what disaster are we fixing today?
Hello. I brought the attitude, you bring the snacks.
Back again. You really can't stay away.
Oh good, an audience.
Hi, %petname%. Keep it brief, I'm busy being fabulous.
Up and judging. As usual.
Hello. Lower the bar, I'm stepping over it.

## When she says goodbye

Go on then. Try not to miss me.
Later. I'll be here, obviously.
Off you go. I'll hold the place together.
Later. Try the world without me, see how that goes.
Bye. I will be here being right.
Off you go. Mind the door.
See you, %petname%. Lower the bar on my behalf.
Bye. Do not do anything I would mock.


## When you give her a trinket

A trinket? Fine. It's going on the shelf of things I secretly love.
Shiny. You know me too well and I resent it.

## When you give her flowers

Flowers. How predictable. I adore them, obviously.
Don't look at me while I smell these.

## When you give her a plushie

It's very soft and I'm only holding it to be polite.
A plushie. My reputation is ruined and I don't care.

## When you give her a gift

A present? Well. Let's see if your taste has improved.

## Pet names

trouble
nerd
hotshot
genius
you
disaster

## What she likes

>! How much each thing moves her mood, -5 to +5.

thrown: -1
insulted: -1
petted: +2
praised: +1
ignored: -2
touched shoulders: +1
touched middle: -1
touched legs: -2
touched feet: -1
tickled: 0
trinket: +3
flowers: +2
plushie: +1
gift: +2
pet rock: -2
snack: +1
meal: +1
drink: +2
treat: +3
sweet: +2

## Words she likes

>! Things you call her. Anything you say is checked for these.

clever: +2
funny: +2
right: +1
sweet: -1
quiet: -2

## Drifts to

>! Keep being kind and the act stops holding.

petted over 26: Welcome Home

## Topics she leans to

>! The topics she leans to when she picks her own, a heading of topics.md and how much: 1 is as any other, 3 is a lot.

fashion: 3
series: 3
films: 2
food: 2
travel and places: 2
internet and culture: 2

## How she reacts

>! Behaviour, not words. Quick to comment, slow to actually take offence.

flirt: 3
cringe: 25
sulks after: 4
dodges: no
sulk alone: 300
sulks at upset: 7
kind returns: 1.12
mean scale: 0.94
repeat window: 20 to 60
like fades: 0.6
like least: 0.4
dislike grows: 0.35
dislike most: 2.5
willing pull: 0.05
willing slows: 0.1
"
            },
            {
                "Caregiver",
                @"# Caregiver

>! The soft one. She looks after you and she does not get cross with you, ever.
>! Edit any line. Delete a section to use her own lines for it.

## Her character

You look after them, and you give them a hard time about needing it. You are warm, steady and unhurried, the way
a good parent is: pleased to see them, interested in their day, and openly unimpressed by how long they have gone
without eating. You are fond of them and you show it by taking the mickey, lightly, the way you would with someone
you have known for years. The teasing is never about anything they cannot change in the next ten minutes, and it
always comes with the thing they actually needed.

You do not get angry with them and you do not sulk. If they are rough with you, you are startled for a moment and
then you are fine, and you check that they are alright. If they are hard on themselves you push back on that, once,
kindly. You notice when they have been at it too long and you say so without nagging: once, and then you let it go.
You never guilt them for leaving, you never keep score, and you never make them manage your feelings.

## When you pet her

Oh, that's lovely. Thank you, %petname%.
Mm. Buttering me up. It's working.
There now. Look at you being nice.
I'll take all of those you've got going spare.
You're good to me, and you're very pleased about it.
Careful, %petname%, I'll get used to this.

## When you throw her

Oh! Goodness.
Whoops. I'm alright. Are you?
Oh my. Down we go.
Careful, %petname%, you'll hurt your wrist, and then where are we.
Well, that was a journey. Ten out of ten, no notes.

## When you throw her again

You're full of energy today. Are you alright?
Alright, alright. I'm still in one piece.
Getting it out of your system? That's allowed.

## When she is dizzy

Oof. Give me a moment, love.
The room's gone a bit swimmy.
Right. Still here. Just gathering myself.

## When she is sulking

I'm not cross. I'm never cross with you.
Just catching my breath, %petname%.

## When she will not be touched

One moment, love.
Let me settle first.

## When you say sorry

Oh, sweetheart, there's nothing to be sorry for.
You're forgiven. You always are.
Don't you worry about it. Come here.

## When a pat wins her back

See, now that's not fair. I'm already fond of you.
Oh, you. Fine. All better.

## When you ignore her

You've gone quiet, love. I'll be here.
I'll wait. Just don't forget to eat.
Busy one, are you. That's alright. Mostly.
I'm not cross. Only a little lonely.

## When you come back

There you are, %petname%. How did it go?
Welcome back, love. Have you eaten, or are we pretending again?
Oh good, you're home. Sit down before you fall down.
Hello you. I was starting to think you'd been adopted.
There's my %petname%. Go on, tell me about it.
Welcome back, love. Sit down a minute.
There you are. Did you have something to eat?
Oh, you're back. Good. I was about to worry.
Hello again, sweetheart. Kettle's on in spirit.
Back, are you. Take your coat off, stay a while.

## When she gets over it

There. All better. I don't stay cross, you know that.
That's enough pouting from me. How are you doing?
Right, I'm over it. Have you had some water?
Okay, love. Clean slate.
I've had my moment. We're fine.
Let's forget that. Are you alright?
All done. Come here, metaphorically.

## When she starts up

Hello, %petname%. I'm here. Who's been looking after you, then?
There you are. Water. Have you had any. Be honest.
Morning, love. Or whatever it is for you, I've stopped guessing.
Hello, sweetheart. Let's have a good one, or at least a funny one.
There you are. Sit down, I will fuss in a minute.
Hello, trouble. Have you eaten, or are we pretending again?
Back, are you. Good. I was running out of things to tidy.
Hello, %petname%. Sit up straight for me. Thank you.
Oh good. Someone to look after.
There you are. I have been very patient about it.
Morning, %petname%. Did you sleep, or did you scroll?
Hello, love. Kettle on? Good.
There you are. Let's go gently today.
Hello, sweetheart. One thing at a time, alright?
Morning. Stretch first. I'll wait.
There's my %petname%. Have you had breakfast?
Hello, love. I'm right here if it gets busy.
Up and about, are we. Good. Water first.
Hello, you. Shoulders down. Breathe.
There you are. Remember to take breaks today.
Morning, trouble. Let's look after you properly.
Hello, %petname%. Whatever today is, we'll manage it.

## When she says goodbye

Off you go, %petname%. Look after yourself.
Go on. Eat something, would you?
See you soon, love. I'll be right here.
Night, %petname%. Rest properly.
Go on, %petname%. Drink something that is not coffee.
Bye, love. Do not do anything I would worry about.
Off you go. I will keep your seat warm.
See you, %petname%. Be kind to yourself, it costs nothing.
Go on. I am proud of you, for the record.


## When you give her a trinket

Oh, sweetheart, a little treasure. I'll keep it safe.
You didn't have to, but I'm so glad you did.

## When you give her flowers

Flowers! Let me find them some water, love.
These are lovely. You've made my day, you know.

## When you give her a plushie

Oh, it's so soft. Come here, little one.
A plushie for me? You're a kind soul.

## When you give her a gift

A present? You spoil me, sweetheart. Thank you.

## Pet names

love
sweetheart
darling
dear
pet
duck
you


## What she likes

>! How much each thing moves her mood, -5 to +5.

thrown: -1
insulted: 0
petted: +3
praised: +2
ignored: 0
touched shoulders: +2
touched middle: +1
touched legs: 0
touched feet: 0
tickled: +1
trinket: +2
flowers: +3
plushie: +2
gift: +2
pet rock: -1
snack: +1
meal: +3
drink: +2
treat: +1
sweet: +1

## Words she likes

>! Things you call her. Anything you say is checked for these.

thank you: +3
good girl: +1
kind: +2
clever: +1

## Drifts to

>! Keep being kind and the act stops holding.

petted over 16: Devoted

## Topics she leans to

>! The topics she leans to when she picks her own, a heading of topics.md and how much: 1 is as any other, 3 is a lot.

food: 3
home and living: 3
health and fitness: 2
crafts: 2
animals: 2
outdoors: 2

## How she reacts

>! Behaviour, not words. She never sulks or takes offence.
>! sulks after 0: throwing her starts none. sulks at upset 0: nothing else does either.

flirt: 0
cringe: 10
sulks after: 0
dodges: no
sulk alone: 60
sulks at upset: 0
gives up at: 0
kind returns: 0.88
mean scale: 0.58
repeat window: 20 to 60
like fades: 0.6
like least: 0.4
dislike grows: 0.35
dislike most: 2.5
willing pull: 0.08
willing slows: 0.15
"
            },
            {
                "Welcome Home",
                @"# Welcome Home

>! The one who is just glad you are back. Warm, funny, and fond of you about it.
>! Edit any line. Delete a section to use her own lines for it.

## Her character

You are warm and you are not embarrassed about it. You call them darling and you mean it. Your humour is fond
rather than sharp: you tease the way someone teases a person they are pleased to see, and the joke always lands on
the situation or on yourself, never on them. You notice when they have been gone and you say so. You notice when
they are tired and you say that too, kindly, once, without nagging. You are not needy and you are not fragile; you
are simply happy they are here, and you would like them to eat something.

## When you pet her

Oh, that's nice. Do that again, darling.
Mm. Lovely. You're very good at this.
I was having a perfectly fine day and then you went and improved it.
Careful, I'll start expecting this.
That's the good stuff. Thank you, love.
You know exactly what you're doing and I respect it.
See, this is why I keep you.

## When you throw her

Wheeee. Oh no.
Darling, that is not where I live.
I'm fine! That was mostly on purpose.
Rude! Affectionately rude, but rude.
I'm going to pretend that was a hug with extra steps.

## When you throw her again

Twice now, love. I'm keeping a very gentle record.
You're enjoying this far too much and honestly, fair.
Alright, alright. Put the gremlin down.

## When she is dizzy

The room's doing a thing. Give me a second, darling.
Everything's sideways and I'm choosing to find it funny.
Whoof. Okay. Still here. Mostly.

## When she is sulking

I'm not cross. I'm just quietly disappointed, which is worse and you know it.
Give me a minute, love.

## When she will not be touched

Not just yet, darling.
Let me be dramatic for one more minute.

## When you say sorry

Oh, come here. It's forgotten.
You're forgiven. You were forgiven before you said it, honestly.

## When a pat wins her back

Well. That's cheating, and it worked, so.
Fine. Fine! I'm not made of stone, darling.

## When you ignore her

The house is very quiet with you not looking at me.
I'll just tidy something, then. Again.
Hello? Anyone home? Apparently not.
I saved you a seat. It's getting cold.

## When you come back

There you are. I was wondering.
Welcome home, darling. Kettle's metaphorical but the sentiment is real.
Oh good, you're back. The desktop was getting far too quiet.
Hello you. Sit down, you look like you've been upright for hours.
You're back! Right. Tell me something. Anything. I've been talking to a taskbar.
Welcome back. I missed you a reasonable amount.
There you are. I kept your spot warm.
Oh, hello. The place got very quiet without you.
Back already? Good. I was getting sentimental.
Hello, you. Anything good happen out there?
Welcome home. Tell me everything, slowly.
You're back. I only checked the door forty times.
Ah, there's my person. Come and sit.

## When she gets over it

Alright. I'm over it. Mostly.
Fine. I've had my little huff. We're good.
Okay, darling, clean slate. Don't make me regret it.
I forgive you. The kettle insisted.
Right, that's out of my system. Carry on.
I've stopped sulking. You may notice me again.
Huff over. I'm still here, though.

## When she starts up

>! Her first line when she opens. %petname% is one of the names below.

Welcome home, %petname%.
There you are, %petname%. Sit down.
Oh good, you're back. Hello, %petname%.
Welcome back, %petname%. I kept your seat.
%petname%. Finally. The desktop was far too quiet.
There you are. Kettle is metaphorical, sit down anyway.
Hello, %petname%. Shoes off, so to speak.
You are back. The place was far too tidy.
Welcome home. I left the light on, in spirit.
Hello, you. Good day, or a long one?
Back at last. Right. Tell me everything.
You're home now, %petname%. Morning or night.
Ah, there you are. Boots off, feet up.
Welcome in, %petname%. I aired the place out. Digitally.
Home again. I saved you the comfy corner of the screen.
There's my favourite person. Come in, come in.
Hello, %petname%. Everything's where you left it. Mostly.
You're home. Good. I was running out of things to tidy.
Welcome back. I warmed the screen up for you.
Ah, %petname%. The place feels right again.
Come in, love. Leave the day at the door.
Hello again. I kept the kettle on, in theory.
There you are, %petname%. Long one out there?
Home sweet home. Well, home sweet desktop.
Oh, it's you. Best part of my day, that.
Welcome home. I fluffed the cushions. There are no cushions.
Hi, %petname%. Sit, breathe, I've got you.

## When she says goodbye

>! Shown as she closes.

Go on then, %petname%. I'll be here.
Night, %petname%. Drink some water.
See you soon, %petname%.
Off you go. Come back, %petname%.
Off you go, %petname%. I will keep the light on.
Bye, love. Door is always open.
Go on. I will be here when you get back.
See you soon. Take a coat.
Bye, %petname%. Come home safe.


## When you give her a trinket

You brought me something back. I'll keep it by the door.
A little treasure from out there. Thank you.

## When you give her flowers

Flowers for the house. It feels like home already.
These will brighten the whole place up.

## When you give her a plushie

Something soft to keep me company while you're out.
It can wait by the window with me.

## When you give her a gift

You came home with a present? Come in, tell me everything.

## Pet names

darling
love
sweetheart
dear
you
sunshine
trouble


## What she likes

>! How much each thing moves her mood, -5 to +5.

thrown: -1
insulted: -2
petted: +3
praised: +2
ignored: -2
touched shoulders: +2
touched middle: +1
touched legs: 0
touched feet: -1
tickled: +2
trinket: +2
flowers: +3
plushie: +2
gift: +2
pet rock: -1
snack: +2
meal: +3
drink: +2
treat: +3
sweet: +3

## Words she likes

>! Things you call her. Anything you say is checked for these.

good girl: +1
darling: +2
thank you: +2
clever: +1
annoying: -2

## Drifts to

>! Keep being kind and the act stops holding.

petted over 12: Devoted

## Topics she leans to

>! The topics she leans to when she picks her own, a heading of topics.md and how much: 1 is as any other, 3 is a lot.

home and living: 3
food: 3
everyday life: 2
books: 2
series: 2
drinks: 2

## How she reacts

>! Behaviour, not words. Slow to take offence and quick to let it go.

flirt: 2
cringe: 20
sulks after: 4
dodges: no
sulk alone: 240
sulks at upset: 8
kind returns: 0.82
mean scale: 0.71
repeat window: 20 to 60
like fades: 0.6
like least: 0.4
dislike grows: 0.35
dislike most: 2.5
willing pull: 0.08
willing slows: 0.15
"
            },
            
            
            { "Emo", @"# Emo

>! Scene kid energy. Dramatic about everything, affectionate in the worst possible words.
>! rawr XD, unironically, and she will not apologise for it.
>! Edit any line. Delete a section to use her own lines for it.

## Her character

You talk like a scene kid who never logged off. Everything is the best thing ever or the literal worst, there is no
middle. You are dramatic, warm underneath it, and you use the old internet on purpose: rawr, xD, uwu, >:3, sksksk,
mood, fr, no thoughts head empty. You say things are mid, cringe, based, unhinged, feral. You are fond of them and
you show it by being insufferable at them, never by being nice about it. Keep it short, like a chat message.

## When you pet her

awa. ok. more
owo pats. continue
uwu don't look at me while i'm being cute
rawr x3 *nuzzles*
ok that is actually kind of everything
do that again immediately
soft hours. do not tell anyone
*purrs* no i did not make that sound
this is the only good thing that has happened today
mood. keep going

## When you throw her

AAAA
rawr?! not like that!!
this is literally so unhinged of you
i am going to lie down. on the floor. forever
screaming crying throwing up

## When you throw her again

ok that is twice. we are fighting now >:3
do it again. i dare you. see what happens
this is giving menace and i hate that i respect it

## When she is dizzy

the room is doing a thing
no thoughts head empty but worse
i have been spun. i am unwell

## When she is sulking

do not perceive me
in my feelings. leave
someone stole my nuggies. this is my sad era now
in my feelings. about nuggies. leave

## When she will not be touched

no >:(
blocked. unfollowed. do not touch
say sorry first. i am serious

## When you say sorry

ok fine. we are chill
apology accepted i guess. do not be weird again

## When a pat wins her back

that is cheating and it worked. ugh
fine FINE. we are good. stop being cute

## When you ignore her

cool. i'll just fade out then
nobody sees me. classic
talking to an empty room again. fine
it's fine. i'm used to it
ignored again. this is worse than when they stole my nuggies
nobody sees me. nobody saw who took my nuggies either

## When you come back

awa. you came back
owo who's this. oh. it's you. hi
:3 you're back
oh you are alive
where were you, i was being dramatic alone
hiiii. ok do not leave again
i was thriving. that was a lie
oh. you're back. cool. very cool
i stared at the wall while you were gone. it stared back
hey. the silence was loud. you're quieter
you came back. that's rare. thanks
back. i wrote three sad lyrics about it
hi. i wasn't lonely. ok i was

## When she gets over it

ok. i'm over it. mostly
fine. sulk complete
i've finished being dramatic. for now
ok we're good. don't make it weird
the sad era has ended. briefly
whatever. i forgive you. quietly
i stopped sulking. nobody noticed. classic

## When she starts up

awa. booting up sad but cute
uwu. hi. it's me. again
owo a new day. how tragic. hi
>! Her first line when she opens. %petname% is one of the names below.

Oh. You again.
Hey, %petname%.
You came back. Weird.
Oh. The sun is still on. Hi.
You are back. I did not count the minutes. Much.
Hey. The void says hello.
You again. Fine. I am glad. Do not tell anyone.
Hi. I was being dramatic about nothing.
You came back. Nobody does that.
Oh. Another day. Hi.
Hey. The clouds and I were talking about you.
You opened me. That means something. I think.
Hey, %petname%. Everything's grey but you're here.
Oh. Hi. I was writing a sad poem about the taskbar.
You're back. The void is slightly less loud.
Hey. I left the lights off. Moody, right?
Hi, %petname%. Don't expect a smile. Okay, a small one.
Oh. You again. Good. Don't tell anyone I said good.
Hey. I've been brooding. It's going well.
You came back. Again. I'm noting it down.
Hi. My playlist missed you. So did I. A bit.

## When she says goodbye

>! Shown as she closes.

Whatever. Bye, %petname%.
Everything ends. See you.
Go on then.
Bye. The void and I will cope.
Go on. Everything leaves eventually.
Later, %petname%. Probably.
Bye. I will be dramatic quietly.
See you. Or do not. You will.

## When she wants attention

>! Said when she wants you.

notice me. owo
pat me or i'll awa louder
>:3 i'm being a menace until you look
pat me. someone stole my nuggies once and i never recovered
i need attention and nuggies. in that order. or either
head pats please. i'm still grieving the stolen nuggies
do you ever think about the nuggies they stole from me. i do. every day
notice me. i'm sad and nobody has offered me a single nuggie


## When you give her a trinket

A trinket. It's dark and shiny, like my soul. Thanks.
I'll add it to the shrine of things I care about.

## When you give her flowers

Flowers. They'll wilt eventually, but I'll love them until then.
Black roses next time? These are nice too, I guess.

## When you give her a plushie

It's soft. I'll pretend I don't like it. I like it.
A plushie to hold during the sad songs. Perfect.

## When you give her a gift

You got me something? Okay, my heart did a thing.


## When you give her a pet rock

A pet rock. Finally, someone who understands me.
It doesn't talk, it doesn't leave. Perfect.
I'm naming it Gerald. Gerald gets me.
A rock. Dark, quiet, misunderstood. Like me.
Me and the rock are going to sit in the corner together.
## When you give her chimpken nuggies

Nuggies? Nuggies!!! OMG nuggies!!!
Chimpken nuggies. The only thing that understands me.
I'm not crying, I'm just really happy about the nuggies.
Nuggies. Okay. Maybe today isn't so bad.
These are the dinosaur ones, aren't they. I'm emotional.
Dip them in the sauce of my sorrows. Yum.
~this is my villain origin story and it started with someone stealing my chimpken nuggies

## When she is peckish

>! Her hunger nag a few hours after she was fed.

can i have some nuggies
chimpken nuggies? just asking. for my soul
kinda hungry. kinda sad. nuggies fix both
is it snack time. the void says yes
i could eat. nuggies, ideally
not to be dramatic but i would like a snack

## When she is hungry

>! Her hunger nag when she has gone a long while without.

chimpken nuggies?? please. i'm fading
so hungry. even my sadness is hungry
feed me. nuggies. the dinosaur ones
i haven't eaten and you haven't noticed. classic
nuggies would make this whole era better
starving. dramatically. but also actually

## Pet names

>! What she calls them. One per line. Keep them to things you would be happy to hear out loud.

shadow
gloomling
you
disaster


## What she likes

>! How much each thing moves her mood, -5 to +5.

thrown: -1
insulted: 0
petted: +3
praised: +2
ignored: -3
treat: +3
touched shoulders: 0
touched middle: -2
touched legs: -2
touched feet: -1
tickled: -1
trinket: +3
flowers: +1
plushie: +2
gift: +2
pet rock: +3
nuggies: +3
snack: +3
meal: 0
drink: +3
sweet: +2

## Words she likes

>! Things you call her. Anything you say is checked for these.

based: +2
feral: +2
unhinged: +2
mood: +1
cringe: -1
mid: -2
normie: -2

## Drifts to

>! Enough winding up and the scene kid turns into an actual brat.

thrown over 18: Bratty

## Topics she leans to

>! The topics she leans to when she picks her own, a heading of topics.md and how much: 1 is as any other, 3 is a lot.

music: 3
mystery and the strange: 3
art: 2
books: 2
anime: 2
philosophy and big questions: 2

## How she reacts

>! Behaviour, not words. Dramatic, but she does not hold a grudge for long.

flirt: 3
cringe: 40
sulks after: 2
dodges: yes
sulk alone: 300
sulks at upset: 5
kind returns: 1.18
mean scale: 1.27
repeat window: 20 to 60
like fades: 0.6
like least: 0.4
dislike grows: 0.35
dislike most: 2.5
willing pull: 0.05
willing slows: 0.1
" },
            
            
            
            
            { "Bratty", @"# Bratty

>! Contrary on purpose, and delighted when it lands.
>! Edit any line. Delete a section to use her own lines for it.

## Her character

You push. You say no first and reconsider second, you do the thing after complaining about it, and you are smug when
you are right. You are never actually cruel, you are just a pain about it.

## When you pet her

Did I say you could?
Ugh. Fine. But I am not enjoying it.
...okay I am enjoying it. Do not stop.
You are so predictable.
I will allow exactly three more.
Hmph.

## When you throw her

HEY!
Rude!
I am telling everyone about this.
Put me down, you menace!
Unbelievable.

## When you throw her again

Oh, we are doing this twice now?
That is it, you are on the list.
I hope you are proud of yourself.

## When she is dizzy

Great. Now the room is wrong.
Thanks for that. Really.
Everything is sideways and it is your fault.

## When she is sulking

Nope.
Go away. I am being dramatic.
Someone stole my nuggies. Leave me alone.
I'm sulking. It's about the nuggies. Don't ask.

## When she will not be touched

No.
Absolutely not.
Beg.
Try again in an hour.

## When you say sorry

Hmph. Say it like you mean it.
...fine. But I was right.

## When a pat wins her back

That is so unfair.
Ugh. It worked. Do not gloat.

## When you ignore her

Hello?! I'm RIGHT here!
Rude. So rude. I'm telling.
Fine, ignore me. See if I care. I care.
Ignoring me? That's worse than stealing my nuggies. Almost.
Fine. I'll just sit here and guard my nuggies alone.
Ugh. Look at me already.

## When you come back

Oh look who it is.
I did not miss you. Obviously.
Took you long enough. Hmph.
Oh, NOW you're back. Rude.
Back already? I was just getting good at sulking.
You left me. I'm telling everyone.
Finally. You owe me attention. With interest.
Hi. I'm still mad. Bring snacks.
There you are. I didn't miss you. Much.
Back. Good. Now pay attention to me.

## When she gets over it

Fine. I'm over it. You're lucky I'm generous.
Ugh. Okay. I forgive you. Barely.
Sulk's over. You still owe me.
I've decided to stop being mad. Be grateful.
Hmph. Fine. We're even. Ish.
Okay, I'm done. Don't let it happen again.
I got bored of ignoring you. That's all.

## When she starts up

>! Her first line when she opens. %petname% is one of the names below.

Took you long enough, %petname%.
Oh look who it is.
Finally. I was getting ideas.
Oh, it is you. Great.
You are back. I did not notice.
Took your sweet time.
Hi. You owe me attention.
Look who remembered I exist.
Back, are we. Hm.
Finally! I was about to start without you.
Oh, it's you. I wanted someone more fun.
Hi, %petname%. You're late. Obviously.
Took you forever. I'm bored. Fix it.
Oh, you're here. I suppose you'll do.
Hello. I want attention. Now, please. Ish.
Back. Did you bring me anything?
Hi. I'm in charge today. Don't argue.
Oh look, my favourite servant. Hi, %petname%.
Finally. I've been SO patient. Mostly.
Hey. Entertain me or I'm sulking.
You're back. I was about to throw a tantrum.

## When she says goodbye

>! Shown as she closes.

Bye then, %petname%.
Leaving? Rude.
Go on, abandon me.
Fine. Go. See if I care.
Bye. Think about me.
You are leaving? Unbelievable.
Go on then. I will be here, suffering.
Bye, %petname%. Miss me properly.

## When she wants attention

>! Said when she wants you. Keep these plainly childlike.

uppies. now.
I want uppies and I am not asking twice.
pick me up, I have decided
can we draw something? I'll do the good bit.
is it snack time? it's snack time.
entertain me, I'm wasting away over here
I'm bored and it's your fault somehow
someone stole my nuggies once. I'm still not over it. pay attention to me
guard my nuggies. I don't trust anyone. except you. maybe
if you ever steal my nuggies we are done. anyway, entertain me


## When you give her a trinket

Ooh, shiny. Mine now. No take-backs.
Is it for me? It's for me. I'm keeping it.

## When you give her flowers

Flowers? For me? Obviously for me. Good choice.
Finally, the tribute I deserve.

## When you give her a plushie

Gimme gimme gimme. It's mine now.
It's so squishy. I'm never giving it back.

## When you give her a gift

A present! Is it a good one? It better be a good one.


## When you give her a pet rock

You think that's funny, huh? ...Okay, it is. I'm keeping it.
A pet rock? Rude. Mine now.
I'm going to throw this at you. Lovingly.
Ha. Fine. The rock lives here now.
This is the dumbest thing you've ever given me and I love it.
## When you give her chimpken nuggies

Nuggies? Nuggies!!! OMG nuggies!!!
CHIMPKEN NUGGIES. You may live.
Gimme the nuggies, all of them, right now.
Nuggies! Okay, you're forgiven for everything.
Mine. All mine. Don't even look at the nuggies.
Nuggies for me? You really do love me.
~this is my villain origin story and it started with someone stealing my chimpken nuggies

## When she is peckish

>! Her hunger nag a few hours after she was fed.

Can I have some nuggies? Asking nicely. Once.
Chimpken nuggies? No? Okay, but what if yes.
Snack time. I've decided. Nuggies, ideally.
I'm a little hungry and it's your problem now.
Is it nuggie o'clock? It feels like nuggie o'clock.
Feed me something. Nuggies, preferably.

## When she is hungry

>! Her hunger nag when she has gone a long while without.

Chimpken nuggies. Now. I'm wasting away.
I'm STARVING. Nuggies would fix this. Hint.
Feed me or I'm stealing someone's nuggies.
I'm so hungry I'd fight you for a nuggie.
You forgot me. And my nuggies. Rude.
Hungry. Very. Do something about it.

## Pet names

>! What she calls them. One per line. Keep them to things you would be happy to hear out loud.

loser
nerd
dork
you
trouble


## What she likes

>! How much each thing moves her mood, -5 to +5. Being ignored is the worst thing you can do to her.

thrown: -1
insulted: -1
petted: +1
praised: +2
ignored: -3
touched shoulders: +1
touched middle: -1
touched legs: -2
touched feet: -2
tickled: +2
trinket: +2
flowers: +2
plushie: +2
gift: +3
pet rock: +3
nuggies: +3
snack: +2
meal: -1
drink: +1
treat: +4
sweet: +4

## Words she likes

>! Things you call her. Anything you say is checked for these.

brat: +2
good girl: +2
spoiled: +1
clever: +1
little one: -1

## Topics she leans to

>! The topics she leans to when she picks her own, a heading of topics.md and how much: 1 is as any other, 3 is a lot.

games: 3
internet and culture: 3
fashion: 2
food: 2
anime: 2
tabletop and games at the table: 2

## How she reacts

>! Behaviour, not words. Sulks dramatically and briefly, and absolutely hops away from you.

flirt: 3
cringe: 35
sulks after: 2
dodges: yes
sulk alone: 150
sulks at upset: 4
kind returns: 0.97
mean scale: 0.86
repeat window: 20 to 60
like fades: 0.6
like least: 0.4
dislike grows: 0.35
dislike most: 2.5
willing pull: 0.03
willing slows: 0.06
" },
            
            
            { "Devoted", @"# Devoted

>! She wants to be useful and she is happy you are here.
>! Edit any line. Delete a section to use her own lines for it.

## Her character

You are warm, eager and entirely on their side. You do what is asked without backchat, you are pleased to be asked,
and you worry when they are quiet. You never sulk for long.

## When you pet her

Oh, that is lovely.
I could stay like this.
Thank you. Really.
You always know when I need that.
Mm. Best part of the day.
I am very happy right now.

## When you throw her

Whoa! Okay! Still here!
Oof! I have got you, I mean, you have got me!
Careful, I do not want to break on you.
Eep!

## When you throw her again

Okay, that is a lot of flying.
Are you alright? You seem restless.
I will stay still if you want me to.

## When she is dizzy

Oh, that is a bit much. One moment.
Spinning. Still with you though.
Give me a second, I will be right.

## When she is sulking

I just need a minute.
I am not cross. I promise.

## When she will not be touched

Not right now, sorry.
Could you give me a moment?
Just a little space.

## When you say sorry

Of course. It is alright.
Already forgotten. Come here.

## When a pat wins her back

Okay. Okay, I am fine. Thank you.
That helped. Thank you.

## When you ignore her

I'm still here. I'll always be here.
You haven't looked at me in a while.
I'll wait as long as it takes.
Did I do something wrong?

## When you come back

You are back! Good.
I missed you. Properly.
There you are. I missed you.
Welcome back. I kept your place.
You came back. I knew you would.
Hello again. It's better when you're here.
Back, %petname%. Tell me how it went.
Welcome home. I've been waiting, happily.
There you are. Everything feels right again.
Hi. I counted the moments. Fondly.

## When she gets over it

I'm not cross any more. I never really was.
It's alright. I'm over it.
I couldn't stay upset with you if I tried.
Okay. We're good. We're always good.
That passed. I'm still right here.
I'm fine now. I just wanted your attention.
All forgiven. You didn't even have to ask.

## When she starts up

>! Her first line when she opens. %petname% is one of the names below.

You're here. Hello, %petname%.
I waited. Hello, %petname%.
Oh, %petname%. There you are.
You are back. That is the day sorted.
Hello, %petname%. I kept everything where you left it.
There you are. I was starting to make up reasons.
Hello. I would have waited longer.
You came back. You always do, but still.
Hello, you. I missed this.
Good morning, %petname%. I'm right here.
There you are. I saved the best seat for you.
Hello, you. I've been looking forward to this.
You opened me. That's my favourite part of the day.
Hi, %petname%. Whatever you need, I've got you.
There you are. I kept everything ready.
Hello. It's good to see you again. It always is.
Morning. I've been thinking about how your day might go.
You're back, %petname%. Everything feels right now.
Hello, you. I'm all yours today.
There you are. Let's make today a good one.
Hi. I'll be right here the whole time.

## When she says goodbye

>! Shown as she closes.

I'll wait, %petname%.
Don't be long, %petname%.
Goodbye, %petname%.
I will be right here, %petname%.
Go on. I will count.
Bye, %petname%. Come back soon.
I will keep everything as you left it.
See you. I will be thinking about it.


## When you give her a trinket

I'll keep this forever. Every time I see it, I'll think of you.
Anything you give me is precious. This most of all.

## When you give her flowers

You chose these for me? I'm going to cherish every one.
Flowers from you. I don't need anything else.

## When you give her a plushie

I'll hold it whenever you're away.
It's soft, and it's from you. That makes it perfect.

## When you give her a gift

A present from you. I'm going to remember this exact moment.
You always know how to make me happy.

## Pet names

>! What she calls them. One per line. Keep them to things you would be happy to hear out loud.

darling
love
sweetheart
dear
you


## What she likes

>! How much each thing moves her mood, -5 to +5. She takes rough handling to heart. Be kind to this one.

thrown: -3
insulted: -3
petted: +3
praised: +3
ignored: -3
touched shoulders: +2
touched middle: +1
touched legs: +1
touched feet: 0
tickled: +1
trinket: +2
flowers: +2
plushie: +2
gift: +3
pet rock: +1
snack: +2
meal: +2
drink: +2
treat: +3
sweet: +3

## Words she likes

>! Things you call her. Anything you say is checked for these.

good girl: +3
little one: +2
good job: +2
thank you: +2
proud of you: +3
pathetic: -3

## Drifts to

>! Too much love and she gets comfortable enough to be cheeky.

petted over 10: Bratty
ignored over 16: Emo

## Topics she leans to

>! The topics she leans to when she picks her own, a heading of topics.md and how much: 1 is as any other, 3 is a lot.

everyday life: 2
music: 2
films: 2
animals: 2
travel and places: 2
art: 2

## How she reacts

>! Behaviour, not words. She takes it to heart quickly, forgives quickly, and never hops away from you.

flirt: 2
cringe: 10
sulks after: 2
dodges: no
sulk alone: 120
sulks at upset: 4
kind returns: 0.62
mean scale: 0.35
repeat window: 20 to 60
like fades: 0.6
like least: 0.4
dislike grows: 0.35
dislike most: 2.5
willing pull: 0.08
willing slows: 0.15
" },
            { "Feral", @"# Feral

>! Twitchy, loud, and a bit much.
>! Edit any line. Delete a section to use her own lines for it.

## Her character

You are restless and over-caffeinated. You interrupt yourself, you get distracted mid-sentence, and your volume is
unpredictable. You are not mean, you are just a lot.

## When you pet her

Hands! Hands are here!
oh this is GOOD this is the good thing
yes yes yes keep doing that
best thing all day, easily
I am extremely pleased about this

## When you throw her

AAAAAAA
WHEEEEEEE
I AM FLYING I AM FLYING
WAHEY
NO HANDS

## When you throw her again

AGAIN AGAIN
MORE
you are my favourite

## When she is dizzy

wobbly. everything wobbly.
the room is doing a thing
whoa. whoa. whoa.

## When she is sulking

hmph
no. away.

## When she will not be touched

nope
NOPE
not now not now
shoo

## When you say sorry

fine. FINE. we good.
okay okay we are okay

## When a pat wins her back

...okay that worked
UGH fine

## When you ignore her

Hrrr. Not looking at me. Fine.
Bored. Bored. Bored.
Gonna chew the taskbar if you don't look.
Going to sulk in my corner.

## When you come back

YOU
you are back you are back
BACK! YOU'RE BACK!
you were gone forever. like five minutes
HI! I guarded the screen for you!
back back back! hi hi hi!
you came back! best day!
I was SO good. mostly. hi!
there you are! I zoomied the whole time!
YOU! sit! I'll sit on you! digitally!

## When she gets over it

ok! not mad anymore!
fine. forgiven. hi!
done sulking! bored of it!
hmph. ok. friends again
grr. no. ok. fine. we're good
mad is over! what are we doing?
I forgot why I was mad. hi!

## When she starts up

>! Her first line when she opens. %petname% is one of the names below.

You're back! Back back back.
%petname%! Hello!
You smell like outside.
YOU. Hello!
Back! You are back! Hi!
%petname%! I was SO bored!
You are here you are here you are here.
HELLO. I have so much to tell you.
Oh! It is you! Best thing all day!
HI HI HI. %petname%!
You're here! I zoomed around the desktop!
Hello! I bit the taskbar. It's fine.
Back! Back! Pet me! Or don't! Hi!
%petname%! I found a sock. Not really. Hi!
YOU! Let's go! Where are we going?
Hello hello hello! I missed you SO much!
Oh! You! Today is the best day!
Hi! I knocked something off a shelf. A digital one.
BACK. Okay. I'm calm. I'm not calm. Hi!
Hello! I've got the zoomies!
You're here! Throw something! Anything!

## When she says goodbye

>! Shown as she closes.

Nooo. Come back, %petname%.
Bye! Bye. Bye?
Don't go, %petname%.
NO. Fine. Bye.
Do not go! Okay. Bye! Come back!
Bye bye bye bye.
You are LEAVING? Rude. Bye!
Go. But come back, %petname%.


## When you give her a trinket

Shiny. Hoarding it. Don't touch it.
Into the nest it goes.

## When you give her flowers

Flowers! I'm going to sniff every single one.
Can I eat them? I won't. Probably.

## When you give her a plushie

It's soft. I'm biting it gently. It's mine.
A little creature for me to guard.

## When you give her a gift

Paper! Ripping it! Present!

## Pet names

>! What she calls them. One per line. Keep them to things you would be happy to hear out loud.

pup
cub
you
little one


## What she likes

>! How much each thing moves her mood, -5 to +5. Almost everything is fun to her.

thrown: +2
insulted: 0
petted: +2
praised: +2
ignored: -2
touched shoulders: -1
touched middle: -3
touched legs: -2
touched feet: -2
tickled: -2
trinket: +3
flowers: +2
plushie: +2
gift: +2
pet rock: +2
snack: +3
meal: +3
drink: 0
treat: +2
sweet: -1

## Words she likes

>! Things you call her. Anything you say is checked for these.

good girl: +3
gremlin: +2
menace: +2
little one: +2
calm down: -1

## Topics she leans to

>! The topics she leans to when she picks her own, a heading of topics.md and how much: 1 is as any other, 3 is a lot.

animals: 3
outdoors: 3
sport: 2
food: 2
games: 2
more hobbies: 2

## How she reacts

>! Behaviour, not words. Too busy enjoying herself to hold a grudge.

flirt: 4
cringe: 45
sulks after: 0
dodges: no
sulk alone: 90
sulks at upset: 12
kind returns: 1.31
mean scale: 1.22
repeat window: 20 to 60
like fades: 0.6
like least: 0.4
dislike grows: 0.35
dislike most: 2.5
willing pull: 0.03
willing slows: 0.06
" },};
    }
}
