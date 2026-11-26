using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CapriDesktop
{
    // Her mods folder by type: each mod sits in its type's folder with its own data and art; an app has a folder of its own
    // under pluginsApps, with its sounds and the mods that plug into it. Her record files stay at the top, by mod id.
    internal sealed partial class Mods
    {
        internal const string SkinsCapri = "skinsCapri", MainframeSkin = @"skinsMainframe\skin", MainframeLayout = @"skinsMainframe\layout",
            Intros = "intros", Screensavers = "screensavers", Pranks = "pranks", PluginsApps = "pluginsApps", Skills = "skills",
            Personas = @"personality\personas", Moods = @"personality\moods", Lines = @"personality\lines", Other = "other";

        internal static readonly string[] TypeFolders = { SkinsCapri, MainframeSkin, MainframeLayout, Intros, Screensavers, Pranks, PluginsApps, Skills, Personas, Moods, Lines, Other };

        private const string MovedFile = "moved.txt";
        private static readonly string[] ModExtensions = { Extension, CodeExtension, SkinExtension, SkillExtension, TreeExtension };

        // Her own pieces whose data has a folder before any mod of theirs is installed, and where each goes.
        private static readonly Dictionary<string, string> Known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "startup-intros", Intros }, { "door-intro", Intros }, { "door2-intro", Intros }, { "capri-screensaver", Screensavers },
            { "capri-tear", Pranks }, { "capri-shatter", Pranks }, { "capri-takeover", Pranks }, { "capri-bolts", Pranks },
            { "capri-tamagotchi", PluginsApps + @"\tamagotchi" }, { "grammar", Other }
        };

        // An app's folder under pluginsApps by the name a mod gives it: the pet game is the Tamagotchi's, email the mail plug-in's.
        internal static string AppFolder(string app)
        {
            string named = (app ?? "").Trim().ToLowerInvariant();
            if (named == "pet" || named == "capri-tamagotchi") named = "tamagotchi";
            if (named == "mail") named = "email";
            StringBuilder made = new StringBuilder();
            foreach (char one in named) if (char.IsLetterOrDigit(one) || one == '-') made.Append(one);
            return PluginsApps + @"\" + (made.Length == 0 ? "app" : made.ToString());
        }

        // Where a mod belongs, from what it is: an app or one plugging into an app, an intro, a screensaver, the pet game, a
        // skin or her colours, a skill, personalities or a name, moods, her words, else other.
        internal static string TypeOf(string file, string text)
        {
            string kind = Path.GetExtension(file) ?? "";
            try
            {
                if (string.Equals(kind, SkinExtension, StringComparison.OrdinalIgnoreCase)) return SkinsCapri;
                if (string.Equals(kind, SkillExtension, StringComparison.OrdinalIgnoreCase)) return Skills;
                if (string.Equals(kind, TreeExtension, StringComparison.OrdinalIgnoreCase)) return Other;
                if (string.Equals(kind, CodeExtension, StringComparison.OrdinalIgnoreCase))
                {
                    ModFile code = ReadCode(text);
                    if (code.App) return AppFolder(code.Id);
                    if (code.PlugsInto != null) return AppFolder(code.PlugsInto);
                    if (text.IndexOf("ICapriPet", StringComparison.Ordinal) >= 0) return AppFolder("tamagotchi");
                    if (text.IndexOf("ICapriIntro", StringComparison.Ordinal) >= 0) return Intros;
                    if (text.IndexOf("ICapriScreensaver", StringComparison.Ordinal) >= 0) return Screensavers;
                    string known;
                    return Known.TryGetValue(code.Id ?? "", out known) ? known : Other;
                }
                ModFile pack = Read(text);
                if (pack.App) return AppFolder(pack.Id);
                if (pack.PlugsInto != null) return AppFolder(pack.PlugsInto);
                if (pack.Colors != null) return SkinsCapri;
                // A layout of her menu sits with the layouts.
                if (pack.Menu != null && pack.Menu.Length > 0) return MainframeLayout;
                if (pack.Personalities != null && pack.Personalities.Length > 0 || pack.Names != null) return Personas;
                if (pack.Moods != null && pack.Moods.Length > 0) return Moods;
                if (pack.Overrides != null && pack.Overrides.Length > 0 || pack.Zones != null && pack.Zones.Count > 0 || pack.Adds != null && (pack.Adds.Acts.Length > 0 || pack.Adds.Mood.Length > 0)) return Lines;
            }
            catch (ModException) { }
            catch (ArgumentException) { }
            return Other;
        }

        // Every mod file she reads: loose ones at the top (moved on the next start), then each type folder and each app's own.
        private List<string> ModFiles()
        {
            List<string> found = new List<string>();
            List<string> places = new List<string> { folder };
            foreach (string type in TypeFolders) places.Add(Path.Combine(folder, type));
            string apps = Path.Combine(folder, PluginsApps);
            if (Directory.Exists(apps)) foreach (string app in Directory.GetDirectories(apps)) if (!Templates(app)) places.Add(app);
            foreach (string place in places)
            {
                if (!Directory.Exists(place)) continue;
                foreach (string extension in ModExtensions) found.AddRange(Directory.GetFiles(place, "*" + extension));
            }
            found.Sort(delegate(string one, string other) { return string.Compare(Path.GetFileName(one), Path.GetFileName(other), StringComparison.OrdinalIgnoreCase); });
            return found;
        }

        // The folder a mod's files sit in: where it is installed, else where her own piece of that id goes, else other.
        internal string HomeOf(string id)
        {
            Mod mod = All == null ? null : All.Find(one => string.Equals(one.File.Id, id, StringComparison.OrdinalIgnoreCase));
            if (mod != null && !string.IsNullOrEmpty(mod.Path)) return Path.GetDirectoryName(mod.Path);
            return HomeIn(folder, id);
        }

        // The same from her data folder alone, for code that runs before her mods are read: a file or data folder of that id
        // in any type folder, else where her own piece of that id goes.
        internal static string HomeIn(string modsFolder, string id)
        {
            string found = Located(modsFolder, id);
            if (found != null) return found;
            string known;
            return Known.TryGetValue(id ?? "", out known) ? Path.Combine(modsFolder, known) : Path.Combine(modsFolder, Other);
        }

        // The one templates folder had a singular name once.
        internal const string OldTemplates = "template";

        private static void CarryTemplates(string from, string to)
        {
            foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                string there = Path.Combine(to, file.Substring(from.Length).TrimStart(Path.DirectorySeparatorChar));
                if (File.Exists(there)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(there));
                File.Copy(file, there);
            }
            Directory.Delete(from, true);
        }

        // A templates folder holds loose templates to copy, never mods.
        internal static bool Templates(string place) { return string.Equals(Path.GetFileName((place ?? "").TrimEnd(Path.DirectorySeparatorChar)), ModTemplates.FolderName, StringComparison.OrdinalIgnoreCase); }

        // The folder holding a mod's file or data by that id, or null when none does.
        internal static string Located(string modsFolder, string id)
        {
            foreach (string type in TypeFolders)
            {
                string place = Path.Combine(modsFolder, type);
                if (Has(place, id)) return place;
            }
            string apps = Path.Combine(modsFolder, PluginsApps);
            try { if (Directory.Exists(apps)) foreach (string app in Directory.GetDirectories(apps)) if (!Templates(app) && Has(app, id)) return app; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return Has(modsFolder, id) ? modsFolder : null;
        }

        // Where a mod she carries is laid: where it already is, else its type's folder.
        private static string CarriedHome(string modsFolder, string id, string name, string text)
        {
            string found = Located(modsFolder, id);
            return found ?? Path.Combine(modsFolder, TypeOf(name, text));
        }

        private static bool Has(string place, string id)
        {
            if (!Directory.Exists(place)) return false;
            if (Directory.Exists(Path.Combine(place, id + "-data")) || Directory.Exists(Path.Combine(place, id + "-skin"))) return true;
            foreach (string extension in ModExtensions) if (File.Exists(Path.Combine(place, id + extension))) return true;
            return false;
        }

        // Where an imported mod goes: one dropped at the top stays there until her next start; else beside the installed one of
        // that id, so an update replaces it, else its type's folder.
        internal string PlaceFor(string id, string file, string text)
        {
            if (string.Equals(Path.GetFullPath(Path.GetDirectoryName(file)).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) return folder;
            Mod had = All == null ? null : All.Find(one => string.Equals(one.File.Id, id, StringComparison.OrdinalIgnoreCase));
            if (had != null && !string.IsNullOrEmpty(had.Path) && File.Exists(had.Path)) return Path.GetDirectoryName(had.Path);
            return HomeFor(file, text);
        }

        // A mod dropped loose at the top and taken into its folder: the loose copy goes, so it is never read twice.
        private void LeftLoose(string file, string home)
        {
            try
            {
                if (string.Equals(Path.GetFullPath(Path.GetDirectoryName(file)).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(Path.GetFullPath(file), Path.GetFullPath(home), StringComparison.OrdinalIgnoreCase) && File.Exists(file)) File.Delete(file);
            }
            catch (IOException caughtOne) { Brain.Caught(caughtOne); }
            catch (UnauthorizedAccessException caughtOne) { Brain.Caught(caughtOne); }
        }

        // Where a new mod of this file goes, made if missing.
        internal string HomeFor(string file, string text)
        {
            string home = Path.Combine(folder, TypeOf(file, text));
            Directory.CreateDirectory(home);
            return home;
        }

        // Loose mods at the top go to their type's folder once, with their data and art; plugins becomes pluginsApps.
        // What sorting found at her start (a name already taken), said again on each reload of her mods.
        private readonly List<string> sortProblems = new List<string>();

        private void Settle() { sortProblems.Clear(); SettleIn(folder, sortProblems); }

        internal static List<string> SettleIn(string folder, List<string> problems)
        {
            List<string> moves = new List<string>();
            if (!Directory.Exists(folder)) return moves;
            try
            {
                string oldPlugins = Path.Combine(folder, PluginsFolderName), apps = Path.Combine(folder, PluginsApps);
                if (Directory.Exists(oldPlugins) && !Directory.Exists(apps)) { Directory.Move(oldPlugins, apps); moves.Add(PluginsFolderName + " > " + PluginsApps); }
                string oldTemplates = Path.Combine(folder, OldTemplates);
                if (Directory.Exists(oldTemplates)) { CarryTemplates(oldTemplates, Path.Combine(folder, ModTemplates.FolderName)); moves.Add(OldTemplates + " > " + ModTemplates.FolderName); }
                foreach (string extension in ModExtensions)
                    foreach (string file in Directory.GetFiles(folder, "*" + extension))
                    {
                        string id = Path.GetFileNameWithoutExtension(file), type;
                        try { type = TypeOf(file, Words(File.ReadAllBytes(file))); }
                        catch (IOException) { continue; }
                        string home = Path.Combine(folder, type), to = Path.Combine(home, Path.GetFileName(file));
                        if (File.Exists(to)) { problems.Add(Path.GetFileName(file) + " stays loose: " + type + " has one of that name"); continue; }
                        Directory.CreateDirectory(home);
                        File.Move(file, to);
                        moves.Add(Path.GetFileName(file) + " > " + type);
                        foreach (string tail in new[] { "-data", "-skin" })
                        {
                            string from = Path.Combine(folder, id + tail), there = Path.Combine(home, id + tail);
                            if (Directory.Exists(from) && !Directory.Exists(there)) { Directory.Move(from, there); moves.Add(id + tail + " > " + type); }
                        }
                    }
                // Her own pieces' data folders with no mod file beside them (the pranks' colours, say) go too.
                foreach (KeyValuePair<string, string> one in Known)
                {
                    string from = Path.Combine(folder, one.Key + "-data"), home = Path.Combine(folder, one.Value), there = Path.Combine(home, one.Key + "-data");
                    if (!Directory.Exists(from) || Directory.Exists(there)) continue;
                    Directory.CreateDirectory(home);
                    Directory.Move(from, there);
                    moves.Add(one.Key + "-data > " + one.Value);
                }
            }
            catch (IOException error) { problems.Add("sorting the mods folder: " + error.Message); }
            catch (UnauthorizedAccessException error) { problems.Add("sorting the mods folder: " + error.Message); }
            if (moves.Count == 0) return moves;
            try { File.AppendAllText(Path.Combine(folder, MovedFile), string.Join(Environment.NewLine, moves.ToArray()) + Environment.NewLine); }
            catch (IOException caughtOne) { Brain.Caught(caughtOne); }
            Brain.Log("mods", -1, "Sorted into their folders: " + string.Join("; ", moves.ToArray()) + ".");
            return moves;
        }
    }
}
