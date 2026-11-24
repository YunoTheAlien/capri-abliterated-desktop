using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace CapriDesktop
{
    [DataContract]
    internal sealed class ModFile
    {
        [DataMember(Name = "format")] public int Format;
        [DataMember(Name = "id")] public string Id;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "version")] public string Version;
        [DataMember(Name = "author")] public string Author;
        [DataMember(Name = "description")] public string Description;
        [DataMember(Name = "wake_names")] public string[] WakeNames;
        [DataMember(Name = "heard_as")] public string[] HeardAs;
        [DataMember(Name = "sounds_like")] public bool SoundsLike;
        [DataMember(Name = "hearing_log")] public bool HearingLog;
        [DataMember(Name = "colors")] public ModColors Colors;
        [DataMember(Name = "accessory")] public bool Accessory;
        // What accessories were called before they had a name of their own.
        [DataMember(Name = "overlay")] public bool Overlay;
        // Personalities and moods the mod brings, each a name and the text of its .md file. One marked senpai is
        // only written with the switch at the bottom of sys.md on, into the senpai folder, like her own.
        [DataMember(Name = "personalities")] public ModPack[] Personalities;
        [DataMember(Name = "moods")] public ModPack[] Moods;
        // Format 2, a drop-in pack: slots it takes over, acts and mood terms it adds, and a name with wake names.
        [DataMember(Name = "overrides")] public ModOverride[] Overrides;
        [DataMember(Name = "adds")] public ModAdds Adds;
        [DataMember(Name = "names")] public ModNames Names;
        internal bool Intro;
        internal string[] Permissions = new string[0];
        // A code mod's // requires: line: the mod API it needs, and other mods that must be installed and on.
        internal int NeedsApi;
        // A code mod that names colours of its own through Ui.Colour, said on its approval and its Mods row so people know.
        internal bool Colours;
        internal string[] Requires = new string[0];
        internal string[] Games;
        // The programs a skill is for, so it is offered when one of them is in front.
        internal string[] Apps = new string[0];
        internal string Know;
        // What a code mod reaches for, read off its source when the file is read. Shown where it is approved.
        internal string[] Reaches = new string[0];
        // On Capri's checked list, as this exact file (CodeMods.cs).
        internal bool Checked;
        // Every outside address in its code, whole. Reaches names only each site; these go under it.
        internal string[] Places = new string[0];
        // Why its source cannot be read, or null; that source laid back out when it can be; and whether that
        // is worth a second question before it runs.
        internal string Unreadable;
        // The source itself, kept only when it can't be read, for her brain to lay out if they ask (ModReadout).
        internal string Source;
        internal string Tidy;
        internal bool AskTwice;
        // The files it carries packed into its code, from its "// assets:" line, so their packing isn't read as hidden text.
        internal string[] Assets = new string[0];
        // Pools of hers this mod says instead, by zone name. Read apart from the rest, since a JSON object
        // with names of its own does not fit a contract with fixed fields.
        internal Dictionary<string, string[]> Zones;
    }

    [DataContract]
    internal sealed class ModPack
    {
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "senpai")] public bool Senpai;
        [DataMember(Name = "text")] public string Text;
    }

    // One slot a pack takes over: a heading ("lines.md > openers warm") or, with whole, a file. Senpai parts wait for
    // the switch.
    [DataContract]
    internal sealed class ModOverride
    {
        [DataMember(Name = "slot")] public string Slot;
        [DataMember(Name = "text")] public string Text;
        [DataMember(Name = "whole")] public bool Whole;
        [DataMember(Name = "senpai")] public bool Senpai;
    }

    [DataContract]
    internal sealed class ModAdds
    {
        [DataMember(Name = "acts")] public ModAct[] Acts;
        [DataMember(Name = "mood")] public ModTerm[] Mood;
    }

    // A new act: its name, kind and the like she starts with.
    [DataContract]
    internal sealed class ModAct
    {
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "kind")] public string Kind;
        [DataMember(Name = "like")] public int Like;
    }

    // A term added to an act's worth while the pack is on, named after the pack in her mood log.
    [DataContract]
    internal sealed class ModTerm
    {
        [DataMember(Name = "act")] public string Act;
        [DataMember(Name = "add")] public int Add;
    }

    [DataContract]
    internal sealed class ModNames
    {
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "wake")] public string[] Wake;
    }

    [DataContract]
    internal sealed class ModColors
    {
        [DataMember(Name = "hair")] public string Hair;
        [DataMember(Name = "accent")] public string Accent;
        [DataMember(Name = "eyes")] public string Eyes;
        [DataMember(Name = "edges")] public string Edges;
        [DataMember(Name = "body")] public string Body;
        internal string[] AsSlots() { return new[] { Hair, Accent, Eyes, Edges, Body }; }
    }

    internal sealed class Mod
    {
        internal ModFile File;
        internal string Path;
        internal bool On;
        // Own: a skill she was taught here, kept in her own skills folder rather than installed as a mod.
        internal bool Code, Changed, Skin, Skill, Own;
        // One of her own items listed as a mod, like her screensaver: its switch is hers, and it has no file to remove.
        internal bool Hers;
        internal string Hash;
        // A skill a plug-in carries: the plug-in's id and name, the skill's folder, and the other files in it read_skill opens.
        internal string Owner, From, Folder;
        internal string[] Extra = new string[0];
        // A plug-in on its own row in Mods > INSTALLED: the row's name, its cog and Remove are the plug-in's.
        internal PluginInfo Plugin;
    }

    internal sealed class ModException : Exception
    {
        internal ModException(string message) : base(message) { }
    }

    internal sealed partial class Mods
    {
        internal const string Extension = ".capri-mod";
        // Skills, code mods and skins are format 1. A settings mod's JSON can be format 2, a drop-in pack with overrides,
        // adds and names; a format 1 one reads as it always has.
        internal const int Format = 1, PackFormat = 2;
        private const int Largest = 256 * 1024;
        private const int MostNames = 20;
        private const int MostSpellings = 60;
        private const int LogLines = 300;
        private const string LogFile = "hearing-log.txt";
        private const string OffFile = "off.txt";
        private static readonly Regex GoodId = new Regex(@"^[a-z0-9][a-z0-9-]{0,47}$");
        private static readonly Regex GoodName = new Regex(@"^[\p{L}\p{M}\p{N}]+(?:[ '-][\p{L}\p{M}\p{N}]+)*$");
        private readonly string folder;
        private volatile Regex called;
        private volatile string note = "";
        // What running code mods add to her personality, after her own rules.
        internal volatile string ModNotes = "";
        // What running code mods tell her fresh each message, like the weather or a timer's time left.
        internal Func<string> ModContext;
        private volatile SoundsLike sounds;
        private volatile bool logging;

        internal Mods(string data)
        {
            folder = Path.Combine(data, "mods");
            ownFolder = Path.Combine(data, OwnFolderName);
            // The directory's last good list lives with her data, where a temp-folder clean cannot take it.
            ModDirectory.KeptFolder = Path.Combine(data, "mod-directory");
            if (WithStarters) AddStarters();
            All = new List<Mod>();
            Problems = new List<string>();
            Hands.AlsoHeard = delegate(string words)
            {
                Regex names = called;
                SoundsLike like = sounds;
                return names != null && names.IsMatch(words ?? "") || like != null && like.Hears(words);
            };
            Hands.AlsoCalled = delegate
            {
                Func<string> context = ModContext;
                return note + ModNotes + (context == null ? "" : context());
            };
            Load();
        }

        internal string Folder { get { return folder; } }
        internal List<Mod> All { get; private set; }
        internal List<string> Problems { get; private set; }

        internal string[] WakeNames
        {
            get
            {
                List<string> names = new List<string>();
                foreach (Mod mod in All)
                    if (mod.On)
                        foreach (string name in mod.File.WakeNames ?? new string[0])
                            if (!names.Exists(known => string.Equals(known, name, StringComparison.OrdinalIgnoreCase))) names.Add(name);
                return names.ToArray();
            }
        }

        internal void Load()
        {
            All.Clear();
            Problems.Clear();
            HashSet<string> off;
            string[] files, own = new string[0];
            try
            {
                off = Off();
                files = new string[0];
                own = OwnFiles();
                if (Directory.Exists(folder))
                {
                    List<string> found = new List<string>(Directory.GetFiles(folder, "*" + Extension));
                    found.AddRange(Directory.GetFiles(folder, "*" + CodeExtension));
                    found.AddRange(Directory.GetFiles(folder, "*" + SkinExtension));
                    found.AddRange(Directory.GetFiles(folder, "*" + SkillExtension));
                    found.Sort(StringComparer.OrdinalIgnoreCase);
                    files = found.ToArray();
                }
            }
            catch (Exception error)
            {
                if (!(error is IOException || error is UnauthorizedAccessException)) throw;
                Problems.Add("the mods folder: " + error.Message);
                Brain.Log("mods", -1, Problems[0]);
                off = new HashSet<string>();
                files = new string[0];
            }
            {
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                foreach (string file in files)
                {
                    try
                    {
                        if (string.Equals(Path.GetExtension(file), CodeExtension, StringComparison.OrdinalIgnoreCase)) { All.Add(LoadCode(file, off)); continue; }
                        if (string.Equals(Path.GetExtension(file), SkinExtension, StringComparison.OrdinalIgnoreCase)) { All.Add(LoadSkin(file, off)); continue; }
                        if (string.Equals(Path.GetExtension(file), SkillExtension, StringComparison.OrdinalIgnoreCase)) { All.Add(LoadSkill(file, off)); continue; }
                        ModFile read = Read(File.ReadAllText(file));
                        if (!string.Equals(read.Id, Path.GetFileNameWithoutExtension(file), StringComparison.OrdinalIgnoreCase)) throw new ModException("its file name doesn't match its id, " + read.Id + ".");
                        All.Add(new Mod { File = read, Path = file, On = !off.Contains(read.Id) });
                    }
                    catch (Exception error)
                    {
                        if (!(error is ModException || error is IOException || error is UnauthorizedAccessException)) throw;
                        string problem = Path.GetFileName(file) + ": " + error.Message;
                        Problems.Add(problem);
                        Brain.Log("mods", -1, problem);
                    }
                }
                // Skills she was taught here, after the installed ones.
                foreach (string file in own)
                {
                    try
                    {
                        Mod mine = LoadOwn(file, off);
                        if (mine != null) All.Add(mine);
                    }
                    catch (Exception error)
                    {
                        if (!(error is ModException || error is IOException || error is UnauthorizedAccessException)) throw;
                        string problem = Path.GetFileName(file) + ": " + error.Message;
                        Problems.Add(problem);
                        Brain.Log("mods", -1, problem);
                    }
                }
            }
            plugged = PluginSkills();
            Apply();
        }

        internal Mod Import(string file, Func<ModFile, string, bool> ask, HashSet<string> keepOff = null)
        {
            if (string.Equals(Path.GetExtension(file), CodeExtension, StringComparison.OrdinalIgnoreCase)) return ImportCode(file, ask, keepOff);
            if (string.Equals(Path.GetExtension(file), SkinExtension, StringComparison.OrdinalIgnoreCase)) return ImportSkin(file, keepOff == null);
            if (string.Equals(Path.GetExtension(file), SkillExtension, StringComparison.OrdinalIgnoreCase)) return ImportSkill(file);
            string text;
            try
            {
                if (!string.Equals(Path.GetExtension(file), Extension, StringComparison.OrdinalIgnoreCase)) throw new ModException("That isn't a " + Extension + ", " + CodeExtension + ", " + SkinExtension + " or " + SkillExtension + " file.");
                if (new FileInfo(file).Length > Largest) throw new ModException("That mod is too big. A mod is a small settings file.");
                text = File.ReadAllText(file);
            }
            catch (IOException error) { throw new ModException("Couldn't read that file: " + error.Message); }
            catch (UnauthorizedAccessException error) { throw new ModException("Couldn't read that file: " + error.Message); }
            ModFile read = Read(text);
            string home = Path.Combine(folder, read.Id + Extension);
            try
            {
                Directory.CreateDirectory(folder);
                string writing = home + ".new";
                File.WriteAllText(writing, text, new UTF8Encoding(false));
                if (File.Exists(home)) File.Delete(home);
                File.Move(writing, home);
                HashSet<string> off = Off();
                bool changed = off.Remove(read.Id);
                if (read.Colors != null) { OnlyColours(read.Id, off); changed = true; }
                if (changed) WriteOff(off);
            }
            catch (IOException error) { throw new ModException("Couldn't save that mod: " + error.Message); }
            catch (UnauthorizedAccessException error) { throw new ModException("Couldn't save that mod: " + error.Message); }
            Load();
            return All.Find(mod => mod.File.Id == read.Id);
        }

        // A newer copy from the mods directory, through the normal import so a code mod still asks; every mod is then
        // left on or off as it was, since an import would switch skins and colour sets.
        internal Mod Update(string file, Func<ModFile, string, bool> ask)
        {
            HashSet<string> before = Off();
            Mod made = Import(file, ask, before);
            if (made == null) return null;
            string id = made.File.Id;
            if (Off().SetEquals(before)) return made;
            WriteOff(before);
            Load();
            Reskin();
            return All.Find(mod => mod.File.Id == id);
        }

        internal bool Set(Mod mod, bool on, Func<ModFile, string, bool> ask)
        {
            if (mod.Code) return SetCode(mod, on, ask);
            if (mod.Skin) { SetSkin(mod, on); return true; }
            HashSet<string> off = Off();
            if (on)
            {
                off.Remove(mod.File.Id);
                ModGuard.Forget(mod.File.Id);
                if (mod.File.Colors != null)
                {
                    OnlyColours(mod.File.Id, off);
                    foreach (Mod other in All) if (other != mod && other.File.Colors != null) other.On = false;
                }
            }
            else off.Add(mod.File.Id);
            WriteOff(off);
            mod.On = on;
            Apply();
            return true;
        }

        // A mod's own saved data, the "<id>-data" folder a code mod keeps between runs. Removing the mod asks about it;
        // turning one off never does, so turning it back on finds everything as it was.
        internal string DataOf(Mod mod) { return System.IO.Path.Combine(folder, mod.File.Id + "-data"); }

        internal bool HasData(Mod mod) { return Directory.Exists(DataOf(mod)); }

        internal void DropData(Mod mod)
        {
            string data = DataOf(mod);
            if (Directory.Exists(data)) Directory.Delete(data, true);
        }

        internal void Remove(Mod mod)
        {
            if (mod.Code) RemoveCode(mod);
            if (mod.Skin) RemoveSkin(mod);
            File.Delete(mod.Path);
            HashSet<string> off = Off();
            if (off.Remove(mod.File.Id)) WriteOff(off);
            Load();
        }

        private const int MostZones = 40, MostZoneLines = 400, LongestZoneLine = 400;

        // The zones a mod claims, read straight from its text. A JSON object whose names are the zones does
        // not fit a contract with fixed fields, so it is read the way her other loose JSON is.
        internal static Dictionary<string, string[]> ReadZones(string text)
        {
            Dictionary<string, string[]> found = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(text) || text.IndexOf("\"zones\"", StringComparison.OrdinalIgnoreCase) < 0) return found;
            XmlDocument whole = new XmlDocument();
            using (XmlDictionaryReader reader = JsonReaderWriterFactory.CreateJsonReader(Encoding.UTF8.GetBytes(text), XmlDictionaryReaderQuotas.Max)) whole.Load(reader);
            XmlNode zones = whole.SelectSingleNode("/root/zones");
            if (zones == null) return found;
            if (zones.Attributes == null || zones.Attributes["type"] == null || zones.Attributes["type"].Value != "object")
                throw new ModException("zones has to be a list of zone names, each with its lines.");
            foreach (XmlNode one in zones.ChildNodes)
            {
                // A JSON name that isn't a legal XML name becomes an element called item carrying the real name in an
                // attribute, as every zone name does with its space and colon.
                string named = (one.Attributes != null && one.Attributes["item"] != null
                    ? one.Attributes["item"].Value
                    : XmlConvert.DecodeName(one.LocalName) ?? "").Trim();
                if (named.Length == 0) continue;
                if (one.Attributes == null || one.Attributes["type"] == null || one.Attributes["type"].Value != "array")
                    throw new ModException("The zone " + named + " needs a list of lines.");
                List<string> lines = new List<string>();
                foreach (XmlNode row in one.ChildNodes)
                {
                    string said = (row.InnerText ?? "").Trim();
                    if (said.Length == 0) continue;
                    if (said.Length > LongestZoneLine) throw new ModException("A line in the zone " + named + " is too long. " + LongestZoneLine + " characters at most.");
                    lines.Add(said);
                }
                if (lines.Count == 0) continue;
                if (lines.Count > MostZoneLines) throw new ModException("The zone " + named + " has too many lines. " + MostZoneLines + " at most.");
                found[named] = lines.ToArray();
                if (found.Count > MostZones) throw new ModException("A mod can take over " + MostZones + " zones at most.");
            }
            return found;
        }

        internal static ModFile Read(string text)
        {
            ModFile read = ReadJson(text);
            read.Zones = ReadZones(text);
            CheckDetails(read, PackFormat);
            read.WakeNames = read.WakeNames ?? new string[0];
            read.HeardAs = read.HeardAs ?? new string[0];
            CheckPack(read);
            if (read.WakeNames.Length == 0 && read.HeardAs.Length == 0 && !read.SoundsLike && !read.HearingLog && read.Colors == null && (read.Zones == null || read.Zones.Count == 0)
                && (read.Personalities == null || read.Personalities.Length == 0) && (read.Moods == null || read.Moods.Length == 0)
                && read.Overrides.Length == 0 && read.Adds.Acts.Length == 0 && read.Adds.Mood.Length == 0)
                throw new ModException("That mod doesn't change anything. It needs wake_names, heard_as, sounds_like, hearing_log, colors, zones, personalities, moods, overrides or adds.");
            if (read.Colors != null) CheckColours(read.Colors);
            if (read.WakeNames.Length > MostNames) throw new ModException("A mod can add up to " + MostNames + " names.");
            if (read.HeardAs.Length > MostSpellings) throw new ModException("A mod can add up to " + MostSpellings + " spellings.");
            CheckNames(read.WakeNames);
            CheckNames(read.HeardAs);
            return read;
        }

        private static ModFile ReadJson(string text)
        {
            ModFile read;
            try
            {
                using (MemoryStream body = new MemoryStream(Encoding.UTF8.GetBytes((text ?? "").TrimStart('﻿'))))
                    read = (ModFile)new DataContractJsonSerializer(typeof(ModFile)).ReadObject(body);
            }
            catch (SerializationException) { throw new ModException("That file isn't valid mod JSON."); }
            if (read == null) throw new ModException("That file isn't valid mod JSON.");
            return read;
        }

        internal const int MostOverrides = 60, LongestOverride = 20000, MostAdds = 40, MostTerm = 3;

        // A pack's parts, each checked against her slots and acts, with the pack's names joining its wake names.
        private static void CheckPack(ModFile read)
        {
            read.Overrides = read.Overrides ?? new ModOverride[0];
            read.Adds = read.Adds ?? new ModAdds();
            read.Adds.Acts = read.Adds.Acts ?? new ModAct[0];
            read.Adds.Mood = read.Adds.Mood ?? new ModTerm[0];
            if (read.Overrides.Length > MostOverrides) throw new ModException("A pack can take over " + MostOverrides + " slots at most.");
            if (read.Adds.Acts.Length + read.Adds.Mood.Length > MostAdds) throw new ModException("A pack can add " + MostAdds + " acts and mood terms at most.");
            List<string> named = new List<string>();
            foreach (ModOverride one in read.Overrides)
            {
                string slot = (one == null ? "" : one.Slot ?? "").Trim();
                if (slot.Length == 0) throw new ModException("An override needs a slot, like \"lines.md > openers warm\". slots.md in the mods folder lists them.");
                if ((one.Text ?? "").Length > LongestOverride) throw new ModException("The override for " + slot + " is too long. " + LongestOverride + " characters at most.");
                bool heading = slot.IndexOf(" > ", StringComparison.Ordinal) > 0;
                if (one.Whole == heading) throw new ModException(one.Whole ? "\"whole\" takes a whole file, so " + slot + " should be just its file name." : slot + " is a file: add \"whole\": true to take it all, or name a heading.");
                if (named.Contains(slot.ToLowerInvariant())) throw new ModException("The pack takes over " + slot + " twice.");
                string file = heading ? slot.Substring(0, slot.IndexOf(" > ", StringComparison.Ordinal)).Trim() : slot;
                if (!FileSlots.Knows(file)) throw new ModException(file + " isn't one of her files. slots.md in the mods folder lists them.");
                named.Add(slot.ToLowerInvariant());
                one.Slot = slot;
            }
            foreach (ModAct one in read.Adds.Acts)
            {
                if (one == null || string.IsNullOrWhiteSpace(one.Name) || !GoodId.IsMatch(one.Name.Replace(' ', '-'))) throw new ModException("An added act needs a name of lowercase letters, numbers and dashes.");
                if (CapriDesktop.Acts.Find(one.Name) != null && !CapriDesktop.Acts.Added(one.Name)) throw new ModException(one.Name + " is one of her own acts; add to it with a mood term instead.");
                if (one.Like < -5 || one.Like > 5) throw new ModException("An act's like is from -5 to 5.");
                one.Kind = string.IsNullOrWhiteSpace(one.Kind) ? "other" : one.Kind.Trim().ToLowerInvariant();
            }
            foreach (ModTerm one in read.Adds.Mood)
                if (one == null || string.IsNullOrWhiteSpace(one.Act) || Math.Abs(one.Add) > MostTerm) throw new ModException("A mood term needs an act and an add from -" + MostTerm + " to " + MostTerm + ".");
            if (read.Names != null && read.Names.Wake != null)
            {
                List<string> wake = new List<string>(read.WakeNames ?? new string[0]);
                foreach (string one in read.Names.Wake) if (!wake.Contains(one)) wake.Add(one);
                read.WakeNames = wake.ToArray();
            }
        }

        private static void CheckNames(string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                string name = (names[i] ?? "").Trim();
                if (name.Length < 2 || name.Length > 32 || !GoodName.IsMatch(name)) throw new ModException("\"" + names[i] + "\" can't be a name. Use 2 to 32 letters or numbers, with single spaces, dashes or apostrophes between them.");
                names[i] = name;
            }
        }

        private static void CheckDetails(ModFile read, int newest = Format)
        {
            if (read.Format < 1 || read.Format > newest) throw new ModException("That mod needs format " + read.Format + ", and this Capri reads " + (newest == 1 ? "format 1" : "up to format " + newest) + ".");
            if (read.Id == null || !GoodId.IsMatch(read.Id)) throw new ModException("A mod's id must be lowercase letters, numbers and dashes.");
            if (string.IsNullOrWhiteSpace(read.Name) || read.Name.Length > 40) throw new ModException("A mod needs a name of up to 40 characters.");
            if (read.Version != null && read.Version.Length > 20) throw new ModException("A mod's version can be up to 20 characters.");
            if (read.Author != null && read.Author.Length > 40) throw new ModException("A mod's author can be up to 40 characters.");
            // A description past 200 characters is cut at a word to fit where it shows, rather than the mod refused.
            if (read.Description != null && read.Description.Length > 200)
            {
                string cut = read.Description.Substring(0, 197);
                int space = cut.LastIndexOf(' ');
                read.Description = (space > 150 ? cut.Substring(0, space) : cut).TrimEnd(' ', ',', ';', ':', '.') + "...";
            }
        }

        internal static string Pattern(string[] names)
        {
            string[] longestFirst = (string[])names.Clone();
            Array.Sort(longestFirst, (one, other) => other.Length.CompareTo(one.Length));
            return @"(?<![\p{L}\p{M}\p{N}_])(?:" + string.Join("|", Array.ConvertAll(longestFirst, delegate(string name) { return Regex.Escape(name).Replace(@"\ ", @"\s+"); })) + @")(?![\p{L}\p{M}\p{N}_])";
        }

        // Laid again after a choice in the conflict box.
        internal void Reapply() { Apply(); }

        private void Apply()
        {
            string[] names = WakeNames;
            List<string> exact = new List<string>(names);
            List<string> soundsLike = new List<string>(names);
            soundsLike.Add("Capri");
            bool wantSounds = false, wantLog = false;
            foreach (Mod mod in All)
            {
                if (!mod.On) continue;
                exact.AddRange(mod.File.HeardAs ?? new string[0]);
                wantSounds |= mod.File.SoundsLike;
                wantLog |= mod.File.HearingLog;
            }
            // The pools a mod has taken over. Rebuilt whole from the mods that are on, so turning one off is
            // enough to give her own lines back and nothing can be left behind.
            Zones.DropAll();
            foreach (Mod mod in All) if (mod.On && mod.File.Zones != null && mod.File.Zones.Count > 0) Zones.Claim(mod.File.Name ?? mod.File.Id, mod.File.Zones);
            Zones.Write();
            // Packs' slots, acts and mood terms, rebuilt whole from the packs that are on, in the order they're listed.
            List<KeyValuePair<string, ModFile>> packs = new List<KeyValuePair<string, ModFile>>();
            List<Mod> packMods = new List<Mod>();
            foreach (Mod mod in All)
                if (mod.On && (!mod.Code || mod.File.Overrides != null && mod.File.Overrides.Length > 0)) { packs.Add(new KeyValuePair<string, ModFile>(mod.File.Name ?? mod.File.Id, mod.File)); packMods.Add(mod); }
            FileSlots.Hold(packMods);
            CapriDesktop.Acts.Hold(packs);
            FileSlots.Write();
            called = exact.Count == 0 ? null : new Regex(Pattern(exact.ToArray()), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            sounds = wantSounds ? new SoundsLike(soundsLike) : null;
            logging = wantLog;
            if (!wantLog) DeleteLog();
            ApplySkin();
            LayPacks();
            SetSkills();
            note = names.Length == 0 ? "" : "\nThey can also call you " + Listed(names) + ". Those are nicknames for you, so answer to them like your own name.";
        }

        // ---------- Personalities and moods from mods ----------

        private const string LaidFile = "laid-packs.txt";
        private const int LongestPack = 200000, LongestPackName = 40;

        // Packs the active mods bring, written into her folders and taken away again when a mod stops bringing them.
        // laid-packs.txt notes what was written, so edited or older files are left alone; senpai ones only with the
        // switch on.
        internal void LayPacks()
        {
            try
            {
                string ledger = Path.Combine(folder, LaidFile);
                Dictionary<string, string> laid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (File.Exists(ledger))
                    foreach (string row in File.ReadAllLines(ledger))
                    {
                        int tab = row.IndexOf('\t');
                        if (tab > 0) laid[row.Substring(tab + 1)] = row.Substring(0, tab);
                    }
                Dictionary<string, string> wanted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                HashSet<string> senpai = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Mod mod in All)
                {
                    if (!mod.On) continue;
                    Want(wanted, senpai, mod.File.Personalities, true);
                    Want(wanted, senpai, mod.File.Moods, false);
                }
                bool changed = false;
                foreach (KeyValuePair<string, string> one in new List<KeyValuePair<string, string>>(laid))
                {
                    if (wanted.ContainsKey(one.Key)) continue;
                    try { if (File.Exists(one.Key) && Fingerprint(File.ReadAllText(one.Key)) == one.Value) File.Delete(one.Key); }
                    catch (IOException) { continue; }
                    catch (UnauthorizedAccessException) { continue; }
                    laid.Remove(one.Key);
                    changed = true;
                }
                foreach (KeyValuePair<string, string> one in wanted)
                {
                    if (senpai.Contains(one.Key) && !Brain.SenpaiOn) continue;
                    string print = Fingerprint(one.Value), had;
                    bool ours = laid.TryGetValue(one.Key, out had);
                    try
                    {
                        if (File.Exists(one.Key) && (!ours || had == print || Fingerprint(File.ReadAllText(one.Key)) != had)) continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(one.Key));
                        File.WriteAllText(one.Key, one.Value, new UTF8Encoding(false));
                        laid[one.Key] = print;
                        changed = true;
                    }
                    catch (IOException error) { Brain.Log("mods", -1, error.Message); }
                    catch (UnauthorizedAccessException error) { Brain.Log("mods", -1, error.Message); }
                }
                if (!changed) return;
                List<string> rows = new List<string>();
                foreach (KeyValuePair<string, string> one in laid) rows.Add(one.Value + "\t" + one.Key);
                Directory.CreateDirectory(folder);
                File.WriteAllLines(ledger, rows.ToArray());
                Personalities.Forget();
            }
            catch (Exception caughtOne) { Brain.Caught(caughtOne); }
        }

        private static void Want(Dictionary<string, string> wanted, HashSet<string> senpai, ModPack[] packs, bool personality)
        {
            if (packs == null) return;
            foreach (ModPack pack in packs)
            {
                if (pack == null || string.IsNullOrWhiteSpace(pack.Name) || string.IsNullOrWhiteSpace(pack.Text) || pack.Text.Length > LongestPack) continue;
                string name = Regex.Replace(pack.Name.Trim(), @"[^\p{L}\p{N} _-]", "").Trim();
                if (name.Length == 0 || name.Length > LongestPackName) continue;
                string path = personality
                    ? (pack.Senpai ? Personalities.SenpaiPathOf(name) : Personalities.PlainPathOf(name))
                    : (pack.Senpai ? Dynamics.SenpaiPathOf(name) : Dynamics.PlainPathOf(name));
                if (string.IsNullOrEmpty(path) || wanted.ContainsKey(path)) continue;
                // Her packs open with their name as a title, which is how a file in the senpai folder is known.
                string text = pack.Text.Replace("\r\n", "\n").Trim();
                if (!text.StartsWith("# ", StringComparison.Ordinal)) text = "# " + name + "\n\n" + text;
                wanted[path] = text.Replace("\n", Environment.NewLine) + Environment.NewLine;
                if (pack.Senpai) senpai.Add(path);
            }
        }

        private static string Fingerprint(string text)
        {
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] sum = sha.ComputeHash(Encoding.UTF8.GetBytes((text ?? "").Replace("\r\n", "\n")));
                StringBuilder hex = new StringBuilder();
                foreach (byte b in sum) hex.Append(b.ToString("x2"));
                return hex.ToString();
            }
        }

        internal string LogPath { get { return Path.Combine(folder, LogFile); } }

        internal Action<string, bool> HeardSpeech;

        internal void LogHearing(string heard, bool forHer)
        {
            Action<string, bool> tell = HeardSpeech;
            if (tell != null) tell(heard, forHer);
            if (!logging) return;
            try
            {
                File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss") + (forHer ? " | FOR HER | " : " | ignored | ") + heard + Environment.NewLine);
                if (new FileInfo(LogPath).Length < LogLines * 120) return;
                string[] lines = File.ReadAllLines(LogPath);
                if (lines.Length <= LogLines) return;
                string[] kept = new string[LogLines];
                Array.Copy(lines, lines.Length - LogLines, kept, 0, LogLines);
                File.WriteAllLines(LogPath, kept);
            }
            catch (IOException error) { Brain.Log("mods", -1, error.Message); }
            catch (UnauthorizedAccessException error) { Brain.Log("mods", -1, error.Message); }
        }

        private void DeleteLog()
        {
            try { if (File.Exists(LogPath)) File.Delete(LogPath); }
            catch (IOException error) { Brain.Log("mods", -1, error.Message); }
            catch (UnauthorizedAccessException error) { Brain.Log("mods", -1, error.Message); }
        }

        internal static string Listed(string[] names)
        {
            if (names.Length < 2) return string.Join("", names);
            string[] front = new string[names.Length - 1];
            Array.Copy(names, front, front.Length);
            return string.Join(", ", front) + " or " + names[names.Length - 1];
        }

        private HashSet<string> Off()
        {
            string file = Path.Combine(folder, OffFile);
            return new HashSet<string>(File.Exists(file) ? File.ReadAllLines(file) : new string[0]);
        }

        private void WriteOff(HashSet<string> off)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllLines(Path.Combine(folder, OffFile), off);
            // In her settings.json's mods block, so it is written with her other settings (on close, before an update).
            SettingsFile.Changed();
        }
    }
}
