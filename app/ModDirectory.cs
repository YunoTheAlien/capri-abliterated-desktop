using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
#if !CROSS
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
#endif
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    internal sealed class ModListing
    {
        internal string Folder, FileName, Sha, PreviewSha;
        // The directory it came from: ours, or Lachy's, whose mods may not work with this build.
        internal ModSource Source;
        internal bool Ours { get { return Source != null && Source.Ours; } }
        internal string Commit;
        // The README in its folder, as the tree spells it (readme.md and README.md are different files on GitHub), or null.
        internal string ReadmeName;
        internal string Name = "", Author = "", About = "";
        // A checked download of this listing, kept from looking for updates so getting it doesn't download it again.
        internal string Fetched;
        internal long Size;
        internal int Downloads, Votes;
        internal double Rating;
        // A colour set, like Colors: Bubblegum: a settings mod the index files with skins, so it shows under Skins (Lachy).
        internal bool Colours;
        internal string Title
        {
            get
            {
                if (Name.Length > 0) return Name;
                string[] words = Folder.Split(new[] { '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < words.Length; i++) words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
                return string.Join(" ", words);
            }
        }
        internal string Kind
        {
            get
            {
                string extension = Path.GetExtension(FileName);
                return string.Equals(extension, Mods.CodeExtension, StringComparison.OrdinalIgnoreCase) ? "RUNS CODE." : string.Equals(extension, Mods.SkinExtension, StringComparison.OrdinalIgnoreCase) ? "SKIN." : string.Equals(extension, Mods.SkillExtension, StringComparison.OrdinalIgnoreCase) ? "SKILL." : Colours ? "COLOURS." : "SETTINGS.";
            }
        }
        internal string Id { get { return Path.GetFileNameWithoutExtension(FileName); } }
        internal int KindIndex
        {
            get
            {
                string extension = Path.GetExtension(FileName);
                return string.Equals(extension, Mods.CodeExtension, StringComparison.OrdinalIgnoreCase) ? 2 : string.Equals(extension, Mods.SkinExtension, StringComparison.OrdinalIgnoreCase) ? 3 : string.Equals(extension, Mods.SkillExtension, StringComparison.OrdinalIgnoreCase) ? 4 : Colours ? 3 : 1;
            }
        }
    }

    // One mods directory on GitHub, in Lachy's layout: mods/<folder>/<file>, README.md and preview.png, with index.json and
    // tree.json at the top. Each keeps its own last list.
    internal sealed class ModSource
    {
        internal readonly string Owner, Repo, Name, Kept;
        internal readonly bool Ours;
        // Off until the repository is there, so nobody sees an error for a directory that doesn't exist yet.
        internal bool Live;

        internal ModSource(string owner, string repo, string name, bool ours, string kept)
        {
            Owner = owner; Repo = repo; Name = name; Ours = ours; Kept = kept;
        }

        internal string Page { get { return "https://github.com/" + Owner + "/" + Repo; } }
        internal string Api { get { return "https://api.github.com/repos/" + Owner + "/" + Repo + "/git/"; } }
        internal string RawRoot { get { return "https://raw.githubusercontent.com/" + Owner + "/" + Repo + "/"; } }
        internal string Raw { get { return RawRoot + "HEAD/"; } }
        internal string KeptFolder { get { return Kept == null ? ModDirectory.KeptFolder : Path.Combine(ModDirectory.KeptFolder, Kept); } }
    }

    internal sealed class ModStats
    {
        internal int Downloads, Votes;
        internal double Rating;
    }

    internal static class ModDirectory
    {
        internal const string Page = "https://github.com/lachydotmcg/capri-mods";
        internal static readonly string[] Kinds = { "All", "Settings", "Code", "Skins", "Skills" };
        internal static readonly string[] Sorts = { "A to Z", "Downloads", "Top rated" };
        private const string Api = "https://api.github.com/repos/lachydotmcg/capri-mods/git/";
        private const long Largest = 96L * 1024 * 1024;
        private const long LargestList = 16L * 1024 * 1024;
        private const long LargestPreview = 2L * 1024 * 1024;
        private const long LargestIndex = 2L * 1024 * 1024;
        private const long LargestReadme = 256L * 1024;
        private const int WidestPreview = 4096, PreviewWide = 444;
        private const string RawRoot = "https://raw.githubusercontent.com/lachydotmcg/capri-mods/";
        private const string Raw = RawRoot + "HEAD/";
        internal const string IndexPage = Raw + "index.json";
        // The directory's own list, as a plain file, read before GitHub's API. The API allows sixty an hour on a
        // network without a login, and a shared network runs out of that, which reads as the directory being empty.
        internal const string TreePage = Raw + "tree.json";
        internal const string TreeApi = Api + "trees/HEAD?recursive=1";
        // The last list that arrived whole, so a failed refresh shows that one, dated, rather than nothing.
        internal static string KeptFolder = Path.Combine(Path.GetTempPath(), "capri-mod-directory");
        internal const string StatsPage = "https://capri-reactions.lachydotmcg.workers.dev/mods";
        private static readonly byte[] PngStart = { 137, 80, 78, 71, 13, 10, 26, 10 };
        private static readonly Regex PreviewFile = new Regex(@"^mods/([A-Za-z0-9][A-Za-z0-9._-]{0,63})/preview\.png$");
        private static readonly Regex ReadmeFile = new Regex(@"^mods/([A-Za-z0-9][A-Za-z0-9._-]{0,63})/((?i:README\.md))$");
        // The README lines View has read this session, by folder. Reads finish on worker threads, so it's locked.
        private static readonly Dictionary<string, List<string>> Readmes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        internal static string PreviewFolder = Path.Combine(Path.GetTempPath(), "capri-mod-previews");
        internal static Func<string, string, byte[]> Post = Push;
        private static readonly Regex Listed = new Regex(@"^mods/([A-Za-z0-9][A-Za-z0-9._-]{0,63})/([A-Za-z0-9][A-Za-z0-9._-]{0,63}\.capri-(?:mod|code|skin|skill))$");
        private static readonly Regex Blob = new Regex(@"^[0-9a-f]{40}$");
        internal static Func<string, long, byte[]> Get = delegate(string url, long most) { return Fetch(url, most, 15000, 30000); };

        // Ours first, then Lachy's. Ours waits for its repository; his keeps the cache he always had.
        internal static readonly ModSource Yuno = new ModSource("YunoTheAlien", "capri-mods", "Yuno's", true, "yuno") { Live = false };
        internal static readonly ModSource Lachy = new ModSource("lachydotmcg", "capri-mods", "Lachy's", false, null) { Live = true };
        internal static Func<ModSource[]> Sources = delegate { return new[] { Yuno, Lachy }; };
        internal const string HisWarning = "From Lachy's directory: it may not work with this build.";

        // His mods are marked his only once ours is listed beside them.
        internal static bool Mixed { get { int live = 0; foreach (ModSource one in Sources()) if (one.Live) live++; return live > 1; } }

        internal static bool Marked(ModListing listing) { return listing != null && !listing.Ours && Mixed; }
        // View's README waits 4 seconds at most, so a slow connection gets the mod's description instead of a long wait.
        internal static Func<string, long, byte[]> GetSoon = delegate(string url, long most) { return Fetch(url, most, 4000, 4000); };

        [DataContract]
        private sealed class Tree
        {
            [DataMember(Name = "tree")] public TreeEntry[] Entries;
            [DataMember(Name = "commit")] public string Commit;
        }

        [DataContract]
        private sealed class TreeEntry
        {
            [DataMember(Name = "path")] public string Path;
            [DataMember(Name = "type")] public string Type;
            [DataMember(Name = "sha")] public string Sha;
            [DataMember(Name = "size")] public long Size;
        }

        [DataContract]
        private sealed class IndexBody
        {
            [DataMember(Name = "format")] public int Format;
            [DataMember(Name = "mods")] public IndexEntry[] Mods;
        }

        [DataContract]
        private sealed class IndexEntry
        {
            [DataMember(Name = "folder")] public string Folder;
            [DataMember(Name = "file")] public string File;
            [DataMember(Name = "name")] public string Name;
            [DataMember(Name = "author")] public string Author;
            [DataMember(Name = "about")] public string About;
            [DataMember(Name = "kind")] public string Kind;
        }

        [DataContract]
        private sealed class StatsBody
        {
            [DataMember(Name = "mods")] public StatsEntry[] Mods;
        }

        [DataContract]
        private sealed class StatsEntry
        {
            [DataMember(Name = "folder")] public string Folder;
            [DataMember(Name = "downloads")] public int Downloads;
            [DataMember(Name = "rating")] public double Rating;
            [DataMember(Name = "votes")] public int Votes;
        }

        internal static List<ModListing> Browse()
        {
            string trouble;
            return Browse(out trouble);
        }

        // For an update check, where an old list is no answer: the failure is thrown rather than covered.
        internal static List<ModListing> BrowseNow()
        {
            string trouble;
            List<ModListing> found = Browse(out trouble);
            if (trouble != null) throw new ModException(trouble);
            return found;
        }

        // Every live directory, ours first: a mod both list shows ours. trouble says why a list is old or missing, and
        // only when every directory failed is nothing shown.
        internal static List<ModListing> Browse(out string trouble)
        {
            trouble = null;
            List<ModListing> found = new List<ModListing>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> troubles = new List<string>();
            Exception last = null;
            int read = 0;
            ModSource[] sources = Sources();
            int live = 0;
            foreach (ModSource source in sources) if (source.Live) live++;
            foreach (ModSource source in sources)
            {
                if (!source.Live) continue;
                List<ModListing> listed;
                string old;
                try { listed = Browse(source, out old); }
                catch (ModException error) { last = error; troubles.Add(live > 1 ? source.Name + " directory: " + error.Message : error.Message); continue; }
                read++;
                if (old != null) troubles.Add(live > 1 ? source.Name + " directory: " + old : old);
                foreach (ModListing listing in listed)
                {
                    if (!seen.Add(listing.Folder)) continue;
                    listing.Source = source;
                    found.Add(listing);
                }
            }
            if (read == 0 && last != null) throw new ModException(string.Join(" ", troubles.ToArray()));
            if (troubles.Count > 0) trouble = string.Join(" ", troubles.ToArray());
            found.Sort(delegate(ModListing a, ModListing b) { return Named(a, b); });
            return found;
        }

        // One directory: the plain file, then the API, then the last one kept. old says why a kept one is showing.
        private static List<ModListing> Browse(ModSource source, out string trouble)
        {
            trouble = null;
            byte[] tree = null;
            List<ModListing> found = null;
            Exception failed = null;
            foreach (string page in new[] { source.Raw + "tree.json", source.Api + "trees/HEAD?recursive=1" })
            {
                try
                {
                    tree = Get(page, LargestList);
                    found = Parse(tree);
                    break;
                }
                catch (Exception error)
                {
                    tree = null;
                    failed = error;
                }
            }
            byte[] index = null;
            if (found != null)
            {
                try { index = Get(source.Raw + "index.json", LargestIndex); }
                catch (Exception) { }
                Keep(source.KeptFolder, tree, index);
            }
            else
            {
                DateTime when;
                if (!Kept(source.KeptFolder, out tree, out index, out when)) throw new ModException(Trouble(failed));
                try { found = Parse(tree); }
                catch (ModException) { throw new ModException(Trouble(failed)); }
                trouble = Trouble(failed) + " Showing the list from " + Ago(when) + ".";
            }
            try { ReadIndex(found, index); }
            catch (Exception) { }
            return found;
        }

        private static void Keep(string kept, byte[] tree, byte[] index)
        {
            try
            {
                Directory.CreateDirectory(kept);
                Save(Path.Combine(kept, "tree.json"), tree);
                if (index != null && index.Length <= LargestIndex) Save(Path.Combine(kept, "index.json"), index);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void Save(string path, byte[] bytes)
        {
            string fresh = path + ".new";
            File.WriteAllBytes(fresh, bytes);
            if (File.Exists(path)) File.Replace(fresh, path, null);
            else File.Move(fresh, path);
        }

        private static bool Kept(string kept, out byte[] tree, out byte[] index, out DateTime when)
        {
            tree = index = null;
            when = DateTime.MinValue;
            try
            {
                string treeFile = Path.Combine(kept, "tree.json"), indexFile = Path.Combine(kept, "index.json");
                if (!File.Exists(treeFile) || new FileInfo(treeFile).Length > LargestList) return false;
                tree = File.ReadAllBytes(treeFile);
                when = File.GetLastWriteTime(treeFile);
                if (File.Exists(indexFile) && new FileInfo(indexFile).Length <= LargestIndex) index = File.ReadAllBytes(indexFile);
                return true;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            tree = index = null;
            return false;
        }

        internal static string Ago(DateTime when)
        {
            TimeSpan gone = DateTime.Now - when;
            if (gone < TimeSpan.FromMinutes(2)) return "a moment ago";
            if (gone < TimeSpan.FromHours(2)) return (int)gone.TotalMinutes + " minutes ago";
            if (gone < TimeSpan.FromDays(2)) return (int)gone.TotalHours + " hours ago";
            return (int)gone.TotalDays + " days ago";
        }

        // Why the directory could not be read, in words: GitHub's hourly limit with when it resets, an answer
        // GitHub gave, a timeout, or no connection.
        internal static string Trouble(Exception error)
        {
            if (error == null) return "Couldn't reach the mods directory.";
            if (error is ModException) return error.Message;
            WebException web = error as WebException;
            HttpWebResponse answer = web != null ? web.Response as HttpWebResponse : null;
            if (answer != null)
            {
                int code = (int)answer.StatusCode;
                string left = answer.Headers["x-ratelimit-remaining"], reset = answer.Headers["x-ratelimit-reset"];
                if (code == 429 || code == 403 && left == "0")
                {
                    long at;
                    string again = long.TryParse(reset, NumberStyles.None, CultureInfo.InvariantCulture, out at) && at > 0 && at < 32503680000L
                        ? " It resets at " + DateTimeOffset.FromUnixTimeSeconds(at).ToLocalTime().ToString("t", CultureInfo.CurrentCulture) + "." : " It resets within the hour.";
                    return "GitHub's hourly limit for the mods directory is used up on your network." + again;
                }
                return "The mods directory couldn't be reached: GitHub answered " + code + " " + answer.StatusDescription + ".";
            }
            if (web != null && web.Status == WebExceptionStatus.Timeout) return "The mods directory took too long to answer.";
            if (web != null && (web.Status == WebExceptionStatus.NameResolutionFailure || web.Status == WebExceptionStatus.ConnectFailure || web.Status == WebExceptionStatus.ProxyNameResolutionFailure))
                return "Couldn't reach GitHub for the mods directory. Check your internet connection.";
            return "Couldn't reach the mods directory: " + error.Message;
        }

        private static void ReadIndex(List<ModListing> listings, byte[] json)
        {
            if (json == null || json.Length > LargestIndex) return;
            IndexBody index;
            using (MemoryStream body = new MemoryStream(json)) index = (IndexBody)new DataContractJsonSerializer(typeof(IndexBody)).ReadObject(body);
            if (index == null || index.Format != 1) return;
            Dictionary<string, ModListing> listed = new Dictionary<string, ModListing>(StringComparer.Ordinal);
            foreach (ModListing listing in listings) listed[listing.Folder + "/" + listing.FileName] = listing;
            foreach (IndexEntry entry in index.Mods ?? new IndexEntry[0])
            {
                if (entry == null || entry.Folder == null || entry.File == null) continue;
                ModListing listing;
                if (!listed.TryGetValue(entry.Folder + "/" + entry.File, out listing)) continue;
                listing.Name = Plain(entry.Name, 120);
                listing.Author = Plain(entry.Author, 80);
                listing.About = Plain(entry.About, 600);
                // Only a settings mod can move, and only to Skins: the index never makes code or a skill look like something else.
                listing.Colours = entry.Kind == "skin" && string.Equals(Path.GetExtension(listing.FileName), Mods.Extension, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static string Plain(string value, int most)
        {
            string line = Regex.Replace(value ?? "", @"[\p{Cc}\p{Cf}\s]+", " ").Trim();
            if (line.Length > most) line = line.Substring(0, char.IsHighSurrogate(line[most - 1]) ? most - 1 : most).TrimEnd();
            return line;
        }

        internal static List<ModListing> Parse(byte[] json)
        {
            Tree tree;
            try
            {
                using (MemoryStream body = new MemoryStream(json)) tree = (Tree)new DataContractJsonSerializer(typeof(Tree)).ReadObject(body);
            }
            catch (SerializationException) { throw new ModException("The mods directory sent something I can't read."); }
            List<ModListing> found = new List<ModListing>();
            string commit = tree.Commit != null && Blob.IsMatch(tree.Commit) ? tree.Commit : null;
            Dictionary<string, string> previews = new Dictionary<string, string>();
            Dictionary<string, string> readmes = new Dictionary<string, string>();
            foreach (TreeEntry entry in tree.Entries ?? new TreeEntry[0])
            {
                if (entry == null || entry.Type != "blob" || entry.Path == null || entry.Sha == null || entry.Size <= 0 || entry.Size > LargestPreview || !Blob.IsMatch(entry.Sha)) continue;
                Match picture = PreviewFile.Match(entry.Path);
                if (picture.Success && !picture.Groups[1].Value.Contains("..")) previews[picture.Groups[1].Value] = entry.Sha;
            }
            foreach (TreeEntry entry in tree.Entries ?? new TreeEntry[0])
            {
                if (entry == null || entry.Type != "blob" || entry.Path == null || entry.Sha == null || entry.Size <= 0 || entry.Size > LargestReadme || !Blob.IsMatch(entry.Sha)) continue;
                Match readme = ReadmeFile.Match(entry.Path);
                if (!readme.Success || readme.Groups[1].Value.Contains("..")) continue;
                string folder = readme.Groups[1].Value, name = readme.Groups[2].Value;
                if (!readmes.ContainsKey(folder) || name == "README.md") readmes[folder] = name;
            }
            foreach (TreeEntry entry in tree.Entries ?? new TreeEntry[0])
            {
                if (entry == null || entry.Type != "blob" || entry.Path == null || entry.Sha == null || entry.Size <= 0 || entry.Size > Largest || !Blob.IsMatch(entry.Sha)) continue;
                Match match = Listed.Match(entry.Path);
                if (!match.Success || match.Groups[1].Value.Contains("..") || match.Groups[2].Value.Contains("..")) continue;
                string previewSha, readmeName;
                previews.TryGetValue(match.Groups[1].Value, out previewSha);
                readmes.TryGetValue(match.Groups[1].Value, out readmeName);
                found.Add(new ModListing { Folder = match.Groups[1].Value, FileName = match.Groups[2].Value, Sha = entry.Sha, Size = entry.Size, PreviewSha = previewSha, ReadmeName = readmeName, Commit = commit });
            }
            found.Sort(delegate(ModListing a, ModListing b) { return Named(a, b); });
            return found;
        }

        // The first useful lines of a mod's README for View, from raw.githubusercontent.com (no rate limit), once a
        // session. Null with no README; a failed read keeps nothing, so View tries again; a README with nothing useful
        // keeps no lines.
        internal static List<string> Readme(ModListing listing)
        {
            if (listing.ReadmeName == null) return null;
            List<string> kept = ReadmeKept(listing);
            if (kept != null) return kept;
            List<string> lines = ReadmeLines(Encoding.UTF8.GetString(GetSoon(RawAt(listing) + "mods/" + listing.Folder + "/" + listing.ReadmeName, LargestReadme)));
            lock (Readmes)
            {
                if (Readmes.TryGetValue(listing.Folder, out kept)) return kept;
                Readmes[listing.Folder] = lines;
            }
            return lines;
        }

        internal static List<string> ReadmeKept(ModListing listing)
        {
            lock (Readmes)
            {
                List<string> kept;
                return Readmes.TryGetValue(listing.Folder, out kept) ? kept : null;
            }
        }

        internal static void ForgetReadmes()
        {
            lock (Readmes) Readmes.Clear();
        }

        // A README's opening as at most 5 plain lines and 400 characters: paragraphs and items one line each, headings
        // only with a line under them. Badges, pictures, HTML, code, tables, its title and the template's Kind and Made
        // by lines are dropped.
        internal static List<string> ReadmeLines(string text)
        {
            List<string> lines = new List<string>();
            text = text ?? "";
            if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);
            string paragraph = "", heading = null;
            bool comment = false, titleSeen = false;
            char fenced = '\0';
            int fenceLength = 0;
            using (StringReader read = new StringReader(text))
            {
                string source;
                while ((source = read.ReadLine()) != null)
                {
                    // Only a line's start is read, so one giant line can't stall the patterns below.
                    if (source.Length > 1000) source = source.Substring(0, 1000);
                    bool skip = fenced != '\0';
                    if (skip)
                    {
                        // Code ends at a fence like the one that opened it, in a quote too.
                        Match end = Regex.Match(source, @"^\s*(?:>\s*)*(`{3,}|~{3,})\s*$");
                        if (end.Success && end.Groups[1].Value[0] == fenced && end.Groups[1].Length >= fenceLength) fenced = '\0';
                    }
                    else
                    {
                        StringBuilder outside = new StringBuilder();
                        int at = 0;
                        while (at < source.Length)
                        {
                            int mark = source.IndexOf(comment ? "-->" : "<!--", at, StringComparison.Ordinal);
                            if (mark < 0)
                            {
                                if (!comment) outside.Append(source, at, source.Length - at);
                                break;
                            }
                            if (!comment) outside.Append(source, at, mark - at);
                            at = mark + (comment ? 3 : 4);
                            comment = !comment;
                        }
                        source = Regex.Replace(outside.ToString(), @"^\s*(?:>\s*)+", "");
                        Match fence = Regex.Match(source, @"^\s*(`{3,}|~{3,})");
                        if (fence.Success)
                        {
                            fenced = fence.Groups[1].Value[0];
                            fenceLength = fence.Groups[1].Length;
                            // A "<!--" after the fence is code, not the start of a comment.
                            comment = false;
                            skip = true;
                        }
                    }
                    if (skip) source = "";
                    Match item = Regex.Match(source, @"^\s*(?:([-*+])|(\d+)[.)])\s+");
                    string prefix = "";
                    if (item.Success)
                    {
                        source = Regex.Replace(source.Substring(item.Length), @"^\[[ xX]\]\s*", "");
                        prefix = item.Groups[1].Success ? "\u2022 " : item.Groups[2].Value + ". ";
                    }
                    Match head = Regex.Match(source, @"^\s*(#{1,6})(?:\s+|$)(.*)$");
                    if (head.Success) source = Regex.Replace(head.Groups[2].Value, @"\s+#+\s*$", "");
                    skip = skip || Regex.IsMatch(source, @"^\s*\||^\s*\[[^\]]+\]:|^(?:\s*[-=*_]){3,}\s*$");
                    string clean = "";
                    if (!skip)
                    {
                        // With its links, addresses and tags taken out, a line needs words of its own: badges, pictures and
                        // lines like [Website](..) | [Discord](..) don't have any.
                        string words = ReadmeLinks(source, false);
                        words = Regex.Replace(words, @"<?https?://[^\s<>]+>?|</?[A-Za-z][^>]*>", "", RegexOptions.IgnoreCase);
                        clean = ReadmeText(source);
                        skip = !Regex.IsMatch(words, @"[\p{L}\p{N}]") || !Regex.IsMatch(clean, @"[\p{L}\p{N}]")
                            || (item.Success && Regex.IsMatch(clean, @"^(?:Kind|Made by)\s*:", RegexOptions.IgnoreCase));
                    }
                    if ((skip || item.Success || head.Success) && paragraph.Length > 0)
                    {
                        if (!KeepReadme(lines, heading, paragraph)) return lines;
                        paragraph = "";
                        heading = null;
                    }
                    if (skip)
                    {
                        // A heading with no words, or only a link, still ends the section before it.
                        if (head.Success) heading = null;
                        continue;
                    }
                    if (head.Success)
                    {
                        // Its first # heading before any line is the README's title, the mod's name, which the window shows.
                        bool title = head.Groups[1].Length == 1 && !titleSeen && lines.Count == 0;
                        if (head.Groups[1].Length == 1) titleSeen = true;
                        heading = title ? null : clean;
                        continue;
                    }
                    paragraph += (paragraph.Length > 0 ? " " : prefix) + clean;
                    // It can't fit whole anyway, so a giant paragraph isn't read any further.
                    if (paragraph.Length > 400)
                    {
                        KeepReadme(lines, heading, paragraph);
                        return lines;
                    }
                }
            }
            if (paragraph.Length > 0) KeepReadme(lines, heading, paragraph);
            return lines;
        }

        private static string ReadmeLinks(string text, bool words)
        {
            text = Regex.Replace(text, @"!\[[^\]]*\](?:\((?:[^()]|\([^()]*\))*\)|\[[^\]]*\])", "");
            return Regex.Replace(text, @"\[([^\]]*)\](?:\((?:[^()]|\([^()]*\))*\)|\[[^\]]*\])", words ? "$1" : "");
        }

        private static string ReadmeText(string text)
        {
            text = ReadmeLinks(text, true);
            text = Regex.Replace(text, @"<(https?://[^<>\s]+)>", "$1", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<br\b[^>]*>", " ", RegexOptions.IgnoreCase);
            // Only tags: "1 < 2 and 3 > 2" stays as it is.
            text = Regex.Replace(text, @"</?[A-Za-z][^>]*>", "");
            text = WebUtility.HtmlDecode(text).Replace("**", "").Replace("__", "").Replace("~~", "").Replace("`", "");
            text = Regex.Replace(text, @"(?<![\w*])[*_](\S(?:.*?\S)?)[*_](?![\w*])", "$1");
            text = Regex.Replace(text, @"\\([\p{P}\p{S}])", "$1");
            text = Regex.Replace(text, @"\p{Cf}", "");
            return Regex.Replace(text, @"[\p{Cc}\s]+", " ").Trim();
        }

        // Adds a finished paragraph or item, with the heading above it, and says whether there's room for more. A line that
        // doesn't fit is cut after a word and ends in "...", unless fewer than 40 characters are left; a heading only goes in
        // with its line.
        private static bool KeepReadme(List<string> lines, string heading, string text)
        {
            int left = 400;
            foreach (string line in lines) left -= line.Length;
            if (heading != null)
            {
                int under = left - heading.Length;
                if (lines.Count >= 4 || under <= 0 || (text.Length > under && under < 40)) return false;
                lines.Add(heading);
                left = under;
            }
            if (text.Length > left)
            {
                if (left >= 40)
                {
                    string cut = text.Substring(0, left - 3);
                    int space = cut.LastIndexOf(' ');
                    if (space > 0) cut = cut.Substring(0, space);
                    cut = cut.TrimEnd();
                    if (cut.Length > 0 && char.IsHighSurrogate(cut[cut.Length - 1])) cut = cut.Substring(0, cut.Length - 1);
                    lines.Add(cut + "...");
                }
                return false;
            }
            lines.Add(text);
            return lines.Count < 5 && text.Length < left;
        }

        private static int Named(ModListing a, ModListing b)
        {
            return StringComparer.OrdinalIgnoreCase.Compare(a.Folder + "/" + a.FileName, b.Folder + "/" + b.FileName);
        }

        internal static List<ModListing> Filter(List<ModListing> listings, string search, int kind)
        {
            string[] words = (search ?? "").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return listings.FindAll(delegate(ModListing listing)
            {
                if (kind != 0 && listing.KindIndex != kind) return false;
                string named = listing.Folder + " " + listing.FileName + " " + listing.Title + " " + listing.Author + " " + listing.About;
                foreach (string word in words) if (named.IndexOf(word, StringComparison.OrdinalIgnoreCase) < 0) return false;
                return true;
            });
        }

        internal static List<ModListing> Sort(List<ModListing> listings, int order)
        {
            if (order <= 0 || order >= Sorts.Length) return listings;
            List<ModListing> sorted = new List<ModListing>(listings);
            if (order == 1) sorted.Sort(delegate(ModListing a, ModListing b)
            {
                int by = b.Downloads.CompareTo(a.Downloads);
                return by != 0 ? by : Named(a, b);
            });
            else sorted.Sort(delegate(ModListing a, ModListing b)
            {
                int by = (a.Votes > 0 ? 0 : 1).CompareTo(b.Votes > 0 ? 0 : 1);
                if (by == 0) by = b.Rating.CompareTo(a.Rating);
                if (by == 0) by = b.Votes.CompareTo(a.Votes);
                return by != 0 ? by : Named(a, b);
            });
            return sorted;
        }

        // Directory listings for installed mods it has a different file for: same id and kind, different git
        // fingerprint. Settings and skill mods compare without a byte order mark; mods not in the directory or
        // unreadable are left out.
        internal static List<ModListing> Updates(List<ModListing> listings, List<Mod> installed)
        {
            List<ModListing> found = new List<ModListing>();
            byte[] mark = { 0xEF, 0xBB, 0xBF };
            foreach (Mod mod in installed)
            {
                if (mod == null || mod.File == null || mod.Path == null) continue;
                ModListing listing = listings.Find(delegate(ModListing each)
                {
                    return string.Equals(each.Id, mod.File.Id, StringComparison.OrdinalIgnoreCase) && string.Equals(Path.GetExtension(each.FileName), Path.GetExtension(mod.Path), StringComparison.OrdinalIgnoreCase);
                });
                if (listing == null) continue;
                byte[] bytes;
                try
                {
                    if (!File.Exists(mod.Path) || new FileInfo(mod.Path).Length > Largest) continue;
                    bytes = File.ReadAllBytes(mod.Path);
                }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                if (BlobSha(bytes) == listing.Sha) continue;
                bool text = !string.Equals(Path.GetExtension(mod.Path), Mods.CodeExtension, StringComparison.OrdinalIgnoreCase) && !string.Equals(Path.GetExtension(mod.Path), Mods.SkinExtension, StringComparison.OrdinalIgnoreCase);
                if (text && listing.Size == bytes.Length + mark.Length)
                {
                    byte[] marked = new byte[bytes.Length + mark.Length];
                    Array.Copy(mark, marked, mark.Length);
                    Array.Copy(bytes, 0, marked, mark.Length, bytes.Length);
                    if (BlobSha(marked) == listing.Sha) continue;
                }
                found.Add(listing);
            }
            found.Sort(Named);
            return found;
        }

        // Of those listings, the ones really newer: each downloaded and checked, dropped unless its version beats the
        // installed copy's, as when someone's own mod shares an id. With no version either side, a different file
        // counts as newer. Kept ones hold their download.
        internal static List<ModListing> Newer(List<ModListing> candidates, List<Mod> installed, string under)
        {
            List<ModListing> newer = new List<ModListing>();
            foreach (ModListing listing in candidates)
            {
                Mod mine = installed.Find(mod => mod != null && mod.File != null && string.Equals(mod.File.Id, listing.Id, StringComparison.OrdinalIgnoreCase));
                string file;
                try { file = Download(listing, under); }
                catch (Exception error)
                {
                    if (!(error is ModException || error is IOException || error is UnauthorizedAccessException || error is WebException)) throw;
                    // Getting it will try again and say what went wrong.
                    newer.Add(listing);
                    continue;
                }
                int? order = Compare(Mods.VersionIn(file), mine != null ? mine.File.Version : null);
                if (order.HasValue && order.Value <= 0)
                {
                    Clean(file);
                    continue;
                }
                listing.Fetched = file;
                newer.Add(listing);
            }
            return newer;
        }

        // Which of two versions like 1.10.2 is higher, by their numbers in order, or null when either has no numbers.
        internal static int? Compare(string theirs, string mine)
        {
            MatchCollection a = Regex.Matches(theirs ?? "", @"\d+"), b = Regex.Matches(mine ?? "", @"\d+");
            if (a.Count == 0 || b.Count == 0) return null;
            for (int i = 0; i < Math.Max(a.Count, b.Count); i++)
            {
                decimal x = i < a.Count ? decimal.Parse(a[i].Value.Length > 20 ? a[i].Value.Substring(0, 20) : a[i].Value, CultureInfo.InvariantCulture) : 0;
                decimal y = i < b.Count ? decimal.Parse(b[i].Value.Length > 20 ? b[i].Value.Substring(0, 20) : b[i].Value, CultureInfo.InvariantCulture) : 0;
                if (x != y) return x > y ? 1 : -1;
            }
            return 0;
        }

        // Deletes the downloads kept for listings that won't be used now.
        internal static void Forget(List<ModListing> listings)
        {
            if (listings == null) return;
            foreach (ModListing listing in listings)
                if (listing.Fetched != null)
                {
                    Clean(listing.Fetched);
                    listing.Fetched = null;
                }
        }

        // "Galaxy", "Galaxy and Ember", "Galaxy, Ember and 3 more".
        internal static string Names(List<ModListing> listings)
        {
            List<string> titles = listings.ConvertAll(listing => listing.Title);
            if (titles.Count <= 2) return string.Join(" and ", titles.ToArray());
            if (titles.Count == 3) return titles[0] + ", " + titles[1] + " and " + titles[2];
            return titles[0] + ", " + titles[1] + " and " + (titles.Count - 2) + " more";
        }

        // Files fetched from the commit the list was made at, so a mod changed since cannot arrive in place of
        // the one that was listed. The newest when the list says no commit.
        private static string RawAt(ModListing listing)
        {
            ModSource from = listing.Source ?? Lachy;
            return listing.Commit != null ? from.RawRoot + listing.Commit + "/" : from.Raw;
        }

        // The size and git hash the list gave. A copy that differs is fetched again from the API, and refused
        // if that one differs too.
        private static bool Matches(byte[] bytes, ModListing listing)
        {
            return bytes != null && bytes.Length == listing.Size && BlobSha(bytes) == listing.Sha;
        }

        internal static string Download(ModListing listing, string under)
        {
            byte[] bytes = null;
            try { bytes = Get(RawAt(listing) + "mods/" + listing.Folder + "/" + listing.FileName, Largest); }
            catch (Exception) { }
            if (!Matches(bytes, listing)) bytes = Get((listing.Source ?? Lachy).Api + "blobs/" + listing.Sha, Largest);
            if (!Matches(bytes, listing)) throw new ModException(listing.FileName + " didn't match what the directory listed, so it wasn't installed.");
            string folder = Path.Combine(under, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, Path.GetFileName(listing.FileName));
            File.WriteAllBytes(file, bytes);
            return file;
        }

        internal static byte[] Preview(ModListing listing, string cache)
        {
            if (listing.PreviewSha == null) return null;
            string kept = Path.Combine(cache, listing.PreviewSha + ".png");
            try
            {
                if (File.Exists(kept))
                {
                    byte[] saved = File.ReadAllBytes(kept);
                    if (saved.Length <= LargestPreview && BlobSha(saved) == listing.PreviewSha) return saved;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            byte[] bytes = Get(RawAt(listing) + "mods/" + listing.Folder + "/preview.png", LargestPreview);
            if (BlobSha(bytes) != listing.PreviewSha || !IsPng(bytes)) throw new ModException(listing.Folder + "'s preview didn't match what the directory listed.");
            try
            {
                Directory.CreateDirectory(cache);
                File.WriteAllBytes(kept, bytes);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return bytes;
        }

        internal static bool IsPng(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 24) return false;
            for (int i = 0; i < PngStart.Length; i++) if (bytes[i] != PngStart[i]) return false;
            return true;
        }

        internal static long BigEndian(byte[] bytes, int at)
        {
            return ((long)bytes[at] << 24) | ((long)bytes[at + 1] << 16) | ((long)bytes[at + 2] << 8) | bytes[at + 3];
        }

#if !CROSS
        internal static Image Picture(byte[] bytes)
        {
            if (!IsPng(bytes)) return null;
            long wide = BigEndian(bytes, 16), tall = BigEndian(bytes, 20);
            if (wide < 1 || tall < 1 || wide > WidestPreview || tall > WidestPreview) return null;
            try
            {
                using (MemoryStream body = new MemoryStream(bytes))
                using (Image read = Image.FromStream(body))
                {
                    int width = Math.Min(PreviewWide, read.Width), height = Math.Max(1, (int)Math.Round(read.Height * (double)width / read.Width));
                    Bitmap small = new Bitmap(width, height);
                    using (Graphics paint = Graphics.FromImage(small))
                    {
                        paint.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        paint.DrawImage(read, 0, 0, width, height);
                    }
                    return small;
                }
            }
            catch (ArgumentException) { return null; }
            catch (ExternalException) { return null; }
            catch (OutOfMemoryException) { return null; }
        }
#else
        internal static Avalonia.Media.Imaging.Bitmap Picture(byte[] bytes)
        {
            if (!IsPng(bytes)) return null;
            long wide = BigEndian(bytes, 16), tall = BigEndian(bytes, 20);
            if (wide < 1 || tall < 1 || wide > WidestPreview || tall > WidestPreview) return null;
            try
            {
                using (MemoryStream body = new MemoryStream(bytes))
                    return Avalonia.Media.Imaging.Bitmap.DecodeToWidth(body, (int)Math.Min(PreviewWide, wide));
            }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
            catch (NotSupportedException) { return null; }
        }
#endif

        // Two stand-in directories, ours and his: ours listed first and a folder both have shown as ours, his marked his,
        // each fetched from its own address; one down still lists the other, saying which failed; both down shows
        // nothing; and ours stays unasked while it isn't live.
        internal static void SourcesCheck(List<string> checks, string folder)
        {
            Func<string, long, byte[]> getWas = Get;
            Func<ModSource[]> sourcesWas = Sources;
            string keptWas = KeptFolder;
            ModSource ours = new ModSource("ours-test", "capri-mods", "Yuno's", true, "ours") { Live = true };
            ModSource his = new ModSource("his-test", "capri-mods", "Lachy's", false, null) { Live = true };
            Func<string[], string> tree = delegate(string[] folders)
            {
                List<string> rows = new List<string>();
                foreach (string one in folders) rows.Add("{\"path\":\"mods/" + one + "/" + one + ".capri-mod\",\"type\":\"blob\",\"sha\":\"" + new string('a', 40) + "\",\"size\":10}");
                return "{\"format\":1,\"tree\":[" + string.Join(",", rows.ToArray()) + "]}";
            };
            bool oursDown = false, hisDown = false;
            List<string> asked = new List<string>();
            try
            {
                KeptFolder = Path.Combine(folder, "kept-" + Guid.NewGuid().ToString("N").Substring(0, 6));
                Sources = delegate { return new[] { ours, his }; };
                Get = delegate(string url, long most)
                {
                    lock (asked) asked.Add(url);
                    bool isOurs = url.StartsWith(ours.Raw, StringComparison.Ordinal) || url.StartsWith(ours.Api, StringComparison.Ordinal);
                    if (isOurs ? oursDown : hisDown) throw new System.Net.WebException("stand-in");
                    if (!url.EndsWith("tree.json", StringComparison.Ordinal)) throw new System.Net.WebException("stand-in");
                    return Encoding.UTF8.GetBytes(isOurs ? tree(new[] { "shared", "ours-only" }) : tree(new[] { "shared", "his-only" }));
                };
                string trouble;
                List<ModListing> both = Browse(out trouble);
                ModListing shared = both.Find(one => one.Folder == "shared"), hisOnly = both.Find(one => one.Folder == "his-only"), oursOnly = both.Find(one => one.Folder == "ours-only");
                bool merged = both.Count == 3 && trouble == null && shared != null && shared.Ours && oursOnly != null && oursOnly.Ours && hisOnly != null && !hisOnly.Ours
                    && RawAt(shared).StartsWith(ours.RawRoot, StringComparison.Ordinal) && RawAt(hisOnly).StartsWith(his.RawRoot, StringComparison.Ordinal) && Marked(hisOnly) && !Marked(shared);
                // His cache kept from the run above is wiped, so his being down now shows nothing of his.
                try { Directory.Delete(KeptFolder, true); } catch (IOException) { }
                hisDown = true;
                List<ModListing> oursAlone = Browse(out trouble);
                bool oneDown = oursAlone.Count == 2 && oursAlone.TrueForAll(one => one.Ours) && trouble != null && trouble.StartsWith("Lachy's directory:", StringComparison.Ordinal);
                try { Directory.Delete(KeptFolder, true); } catch (IOException) { }
                oursDown = true;
                bool allDown = false;
                try { Browse(out trouble); }
                catch (ModException) { allDown = true; }
                oursDown = hisDown = false;
                ours.Live = false;
                lock (asked) asked.Clear();
                Browse(out trouble);
                bool unasked;
                lock (asked) unasked = !asked.Exists(url => url.StartsWith(ours.Raw, StringComparison.Ordinal) || url.StartsWith(ours.Api, StringComparison.Ordinal));
                if (!merged || !oneDown || !allDown || !unasked)
                    throw new Exception("The two mods directories went wrong: ours first with a shared folder shown as ours and each fetched from its own address " + merged + ", one down still lists the other and says which " + oneDown + ", both down shows nothing " + allDown + ", ours unasked while it isn't live " + unasked);
                checks.Add("PASS Browse mods reads two directories, ours first: a folder both list shows ours, his are marked his and fetched from his address; one down still lists the other and says which failed, both down shows nothing, and ours isn't asked while it isn't live");
            }
            finally
            {
                Get = getWas;
                Sources = sourcesWas;
                KeptFolder = keptWas;
            }
        }

        internal static Dictionary<string, ModStats> Stats()
        {
            StatsBody body;
            try
            {
                using (MemoryStream read = new MemoryStream(Get(StatsPage, LargestPreview))) body = (StatsBody)new DataContractJsonSerializer(typeof(StatsBody)).ReadObject(read);
            }
            catch (SerializationException) { throw new ModException("The mods directory's numbers couldn't be read."); }
            Dictionary<string, ModStats> found = new Dictionary<string, ModStats>(StringComparer.OrdinalIgnoreCase);
            foreach (StatsEntry entry in body.Mods ?? new StatsEntry[0])
                if (entry != null && entry.Folder != null) found[entry.Folder] = new ModStats { Downloads = Math.Max(0, entry.Downloads), Rating = Math.Max(0, Math.Min(5, entry.Rating)), Votes = Math.Max(0, entry.Votes) };
            return found;
        }

        internal static void Apply(List<ModListing> listings, Dictionary<string, ModStats> stats)
        {
            foreach (ModListing listing in listings)
            {
                ModStats found;
                if (!stats.TryGetValue(listing.Folder, out found)) continue;
                listing.Downloads = found.Downloads;
                listing.Rating = found.Rating;
                listing.Votes = found.Votes;
            }
        }

        internal static string RatingLine(ModListing listing)
        {
            return listing.Votes == 0 ? "No ratings" : listing.Rating.ToString("0.0", CultureInfo.InvariantCulture) + " (" + listing.Votes + ")";
        }

        internal static string DownloadLine(ModListing listing)
        {
            string count = listing.Downloads >= 1000000 ? (listing.Downloads / 1000000.0).ToString("0.#", CultureInfo.InvariantCulture) + "M" : listing.Downloads >= 1000 ? (listing.Downloads / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + "K" : listing.Downloads.ToString(CultureInfo.InvariantCulture);
            return count + (listing.Downloads == 1 ? " download" : " downloads");
        }

        internal static void CountDownload(string folder)
        {
            Post(StatsPage + "/download", "{\"folder\":\"" + folder + "\"}");
        }

        internal static ModStats Rate(string folder, int stars)
        {
            byte[] answer = Post(StatsPage + "/rate", "{\"folder\":\"" + folder + "\",\"stars\":" + stars + "}");
            StatsEntry entry;
            try
            {
                using (MemoryStream read = new MemoryStream(answer)) entry = (StatsEntry)new DataContractJsonSerializer(typeof(StatsEntry)).ReadObject(read);
            }
            catch (SerializationException) { throw new ModException("Your rating's answer couldn't be read."); }
            return new ModStats { Downloads = Math.Max(0, entry.Downloads), Rating = Math.Max(0, Math.Min(5, entry.Rating)), Votes = Math.Max(0, entry.Votes) };
        }

        internal static string PublishSteps(Mod mod)
        {
            string id = mod.File.Id, file = Path.GetFileName(mod.Path);
            return "1. Press Fork capri-mods and make your own copy on GitHub. You'll need a free account.\n\n"
                + "2. In your copy, add a folder called mods/" + id + "/.\n\n"
                + "3. Put in " + file + " and a short README.md that says what it does and who made it.\n\n"
                + "4. Add a picture called preview.png, around 16:9. It's what people see in the mods window.\n\n"
                + "5. Open a pull request to capri-mods. We check some mods ourselves, but can't promise any is safe or still works after "
                + "others update it; Capri flags what she catches when it is installed or run."
                + (mod.Code ? "\n\nThis is a code mod, so Capri will ask people before running it." : "");
        }

        internal static string BlobSha(byte[] bytes)
        {
            byte[] head = Encoding.ASCII.GetBytes("blob " + bytes.Length + "\0");
            using (SHA1 sha = SHA1.Create())
            {
                sha.TransformBlock(head, 0, head.Length, null, 0);
                sha.TransformFinalBlock(bytes, 0, bytes.Length);
                return BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private static byte[] Fetch(string url, long most, int wait, int readWait)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "Capri-Desktop";
            request.Accept = url.Contains("/blobs/") ? "application/vnd.github.raw+json" : "application/vnd.github+json";
            request.Timeout = wait;
            request.ReadWriteTimeout = readWait;
            using (WebResponse response = request.GetResponse())
            using (Stream body = response.GetResponseStream())
            using (MemoryStream kept = new MemoryStream())
            {
                byte[] chunk = new byte[81920];
                int read;
                while ((read = body.Read(chunk, 0, chunk.Length)) > 0)
                {
                    if (kept.Length + read > most) throw new ModException("That's too big to come from the mods directory.");
                    kept.Write(chunk, 0, read);
                }
                return kept.ToArray();
            }
        }

        private static byte[] Push(string url, string json)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.UserAgent = "Capri-Desktop";
            request.ContentType = "application/json";
            request.Timeout = 15000;
            request.ReadWriteTimeout = 15000;
            byte[] sent = Encoding.UTF8.GetBytes(json);
            using (Stream body = request.GetRequestStream()) body.Write(sent, 0, sent.Length);
            using (WebResponse response = request.GetResponse())
            using (Stream body = response.GetResponseStream())
            using (MemoryStream kept = new MemoryStream())
            {
                body.CopyTo(kept);
                return kept.ToArray();
            }
        }

        internal static void Clean(string file)
        {
            try { Directory.Delete(Path.GetDirectoryName(file), true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
