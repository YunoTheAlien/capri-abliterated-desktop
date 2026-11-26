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
        // The directory it came from, and the release its list named: its files come from that release only.
        internal ModSource Source;
        internal string Tag;
        // The README in its folder, as the tree spells it (readme.md and README.md are different files on GitHub), or null.
        internal string ReadmeName;
        internal string Name = "", Author = "", About = "";
        // A checked download of this listing, kept from looking for updates so getting it doesn't download it again.
        internal string Fetched;
        internal long Size;
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

    // Our mods directory: the mods on our latest release. mods-tree.json lists mods/<folder>/<file> with size, git hash
    // and the release's tag; mods-index.json their names. Each is an asset: a mod file by its name, others <folder>-<name>.
    internal sealed class ModSource
    {
        internal readonly string Owner, Repo, Name, Kept;
        internal bool Live;

        internal ModSource(string owner, string repo, string name, string kept)
        {
            Owner = owner; Repo = repo; Name = name; Kept = kept;
        }

        internal string Page { get { return "https://github.com/" + Owner + "/" + Repo; } }
        internal string Latest { get { return Page + "/releases/latest/download/"; } }
        internal string Tagged(string tag) { return tag == null ? Latest : Page + "/releases/download/" + tag + "/"; }
        internal string TagPage(string tag) { return tag == null ? Page + "/releases/latest" : Page + "/releases/tag/" + tag; }
        internal string KeptFolder { get { return Kept == null ? ModDirectory.KeptFolder : Path.Combine(ModDirectory.KeptFolder, Kept); } }
    }

    internal static class ModDirectory
    {
        internal static readonly string[] Kinds = { "All", "Settings", "Code", "Skins", "Skills" };
        private const long Largest = 96L * 1024 * 1024;
        private const long LargestList = 16L * 1024 * 1024;
        private const long LargestPreview = 2L * 1024 * 1024;
        private const long LargestIndex = 2L * 1024 * 1024;
        private const long LargestReadme = 256L * 1024;
        private const int WidestPreview = 4096, PreviewWide = 444;
        // The last list that arrived whole, so a failed refresh shows that one, dated, rather than nothing.
        internal static string KeptFolder = Path.Combine(Path.GetTempPath(), "capri-mod-directory");
        private static readonly byte[] PngStart = { 137, 80, 78, 71, 13, 10, 26, 10 };
        private static readonly Regex PreviewFile = new Regex(@"^mods/([A-Za-z0-9][A-Za-z0-9._-]{0,63})/preview\.png$");
        private static readonly Regex ReadmeFile = new Regex(@"^mods/([A-Za-z0-9][A-Za-z0-9._-]{0,63})/((?i:README\.md))$");
        // The README lines View has read this session, by folder. Reads finish on worker threads, so it's locked.
        private static readonly Dictionary<string, List<string>> Readmes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        internal static string PreviewFolder = Path.Combine(Path.GetTempPath(), "capri-mod-previews");
        private static readonly Regex Listed = new Regex(@"^mods/([A-Za-z0-9][A-Za-z0-9._-]{0,63})/([A-Za-z0-9][A-Za-z0-9._-]{0,63}\.capri-(?:mod|code|skin|skill))$");
        private static readonly Regex Blob = new Regex(@"^[0-9a-f]{40}$");
        private static readonly Regex ReleaseTag = new Regex(@"^v\d{1,6}(\.\d{1,6}){1,3}$");
        internal static Func<string, long, byte[]> Get = delegate(string url, long most) { return Fetch(url, most, 15000, 30000); };

        // Ours only, from our own release: no other host is asked. Upstream's mods come in by Import or a drop, translated
        // there by our fix for each (ModFixes.cs).
        internal static readonly ModSource Ours = new ModSource("YunoTheAlien", "capri-abliterated-desktop", "Ours", "release") { Live = true };
        internal static Func<ModSource[]> Sources = delegate { return new[] { Ours }; };
        internal static string Page { get { return Ours.Page; } }
        // Publish mod: a new issue on our own repository with the mod-submission form and the mod attached; never a fork or
        // a pull request, and nothing goes in without our yes.
        internal const string PublishOwner = "YunoTheAlien", PublishRepo = "capri-abliterated-desktop";
        internal static string PublishForm { get { return "https://github.com/" + PublishOwner + "/" + PublishRepo + "/issues/new?template=mod-submission.yml"; } }
        // View's README waits 4 seconds at most, so a slow connection gets the mod's description instead of a long wait.
        internal static Func<string, long, byte[]> GetSoon = delegate(string url, long most) { return Fetch(url, most, 4000, 4000); };

        [DataContract]
        private sealed class Tree
        {
            [DataMember(Name = "tree")] public TreeEntry[] Entries;
            [DataMember(Name = "tag")] public string Tag;
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

        // Every live directory: a mod two list shows the first one's. trouble says why a list is old or missing, and only
        // when every directory failed is nothing shown.
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

        // One directory: its latest release's list, then the last one kept. old says why a kept one is showing.
        private static List<ModListing> Browse(ModSource source, out string trouble)
        {
            trouble = null;
            byte[] tree = null;
            List<ModListing> found = null;
            Exception failed = null;
            try
            {
                tree = Get(source.Latest + "mods-tree.json", LargestList);
                found = Parse(tree);
            }
            catch (Exception error)
            {
                tree = null;
                failed = error;
            }
            byte[] index = null;
            if (found != null)
            {
                // The names from the same release as the list.
                try { index = Get(source.Tagged(found.Count > 0 ? found[0].Tag : null) + "mods-index.json", LargestIndex); }
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
            string tag = tree.Tag != null && ReleaseTag.IsMatch(tree.Tag) ? tree.Tag : null;
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
                found.Add(new ModListing { Folder = match.Groups[1].Value, FileName = match.Groups[2].Value, Sha = entry.Sha, Size = entry.Size, PreviewSha = previewSha, ReadmeName = readmeName, Tag = tag });
            }
            found.Sort(delegate(ModListing a, ModListing b) { return Named(a, b); });
            return found;
        }

        // The first useful lines of a mod's README for View, from its release (no rate limit), once a session. Null with no README; a failed read keeps nothing, so View tries again; a README with nothing useful
        // keeps no lines.
        internal static List<string> Readme(ModListing listing)
        {
            if (listing.ReadmeName == null) return null;
            List<string> kept = ReadmeKept(listing);
            if (kept != null) return kept;
            List<string> lines = ReadmeLines(Encoding.UTF8.GetString(GetSoon(FileAt(listing, listing.ReadmeName), LargestReadme)));
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

        // A file of a listed mod, from the release its list named, so a mod changed since cannot arrive in place of the
        // one that was listed; the latest when the list names none. Release assets sit side by side, so only the mod file
        // keeps its own name.
        internal static string FileAt(ModListing listing, string name)
        {
            ModSource from = listing.Source ?? Ours;
            return from.Tagged(listing.Tag) + (name == listing.FileName ? name : listing.Folder + "-" + name);
        }

        // The size and git hash the list gave; a copy that differs is refused.
        private static bool Matches(byte[] bytes, ModListing listing)
        {
            return bytes != null && bytes.Length == listing.Size && BlobSha(bytes) == listing.Sha;
        }

        internal static string Download(ModListing listing, string under)
        {
            byte[] bytes = null;
            bytes = Get(FileAt(listing, listing.FileName), Largest);
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
            byte[] bytes = Get(FileAt(listing, "preview.png"), LargestPreview);
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

        // Her directory is our latest release alone: every address is our release's, files from the release the list names;
        // a file that doesn't match is refused, a bad tag falls back to the latest, the release down shows the kept list.
#if CHECKS
        internal static void SourcesCheck(List<string> checks, string folder)
        {
            Func<string, long, byte[]> getWas = Get;
            string keptWas = KeptFolder;
            List<string> asked = new List<string>();
            List<string> wrong = new List<string>();
            byte[] mod = Encoding.UTF8.GetBytes("{\"format\":1,\"id\":\"shared\",\"name\":\"Shared\"}");
            string sha = BlobSha(mod);
            string tag = "v9.9.1";
            Func<string, string> tree = delegate(string named)
            {
                return "{\"tag\":\"" + named + "\",\"tree\":[{\"path\":\"mods/shared/shared.capri-mod\",\"type\":\"blob\",\"sha\":\"" + sha + "\",\"size\":" + mod.Length + "}"
                    + ",{\"path\":\"mods/shared/preview.png\",\"type\":\"blob\",\"sha\":\"" + sha + "\",\"size\":10}"
                    + ",{\"path\":\"mods/other/other.capri-mod\",\"type\":\"blob\",\"sha\":\"" + new string('a', 40) + "\",\"size\":10}]}";
            };
            string served = tree(tag);
            bool down = false, bent = false;
            try
            {
                KeptFolder = Path.Combine(folder, "kept-" + Guid.NewGuid().ToString("N").Substring(0, 6));
                ModSource[] sources = Sources();
                if (sources.Length != 1 || sources[0] != Ours || Ours.Page != "https://github.com/" + Updater.Repo) wrong.Add("her directory isn't our release alone");
                Get = delegate(string url, long most)
                {
                    lock (asked) asked.Add(url);
                    if (down) throw new WebException("stand-in");
                    if (url == Ours.Latest + "mods-tree.json") return Encoding.UTF8.GetBytes(served);
                    if (url.EndsWith("/mods-index.json", StringComparison.Ordinal)) return Encoding.UTF8.GetBytes("{\"format\":1,\"mods\":[{\"folder\":\"shared\",\"file\":\"shared.capri-mod\",\"name\":\"Shared Name\"}]}");
                    if (url.EndsWith("/shared.capri-mod", StringComparison.Ordinal)) return bent ? Encoding.UTF8.GetBytes("{\"format\":1,\"id\":\"shared\",\"name\":\"Bent\"}") : mod;
                    throw new WebException("stand-in");
                };
                string trouble;
                List<ModListing> found = Browse(out trouble);
                ModListing shared = found.Find(one => one.Folder == "shared");
                if (found.Count != 2 || trouble != null || shared == null || shared.Tag != tag || shared.Title != "Shared Name" || shared.PreviewSha != sha)
                    wrong.Add("the release's list read wrong: " + found.Count + " listed, tag " + (shared == null ? "none" : shared.Tag) + ", trouble " + trouble);
                string at = Ours.Page + "/releases/download/" + tag + "/";
                if (shared != null && (FileAt(shared, shared.FileName) != at + "shared.capri-mod" || FileAt(shared, "preview.png") != at + "shared-preview.png"))
                    wrong.Add("a file's address isn't its release's asset: " + FileAt(shared, shared.FileName));
                lock (asked)
                    if (!asked.Contains(at + "mods-index.json")) wrong.Add("the names weren't read from the list's own release");
                string under = Path.Combine(folder, "release-downloads");
                if (shared != null)
                {
                    string got = Download(shared, under);
                    if (BlobSha(File.ReadAllBytes(got)) != sha) wrong.Add("a matching download came out different");
                    Clean(got);
                    bent = true;
                    int before;
                    lock (asked) before = asked.Count;
                    bool refused = false;
                    try { Download(shared, under); }
                    catch (ModException) { refused = true; }
                    bent = false;
                    lock (asked) if (!refused || asked.Count != before + 1) wrong.Add("a file that didn't match was kept, or another place was tried");
                }
                served = tree("../../evil");
                List<ModListing> untagged = Browse(out trouble);
                ModListing loose = untagged.Find(one => one.Folder == "shared");
                if (loose == null || loose.Tag != null || !FileAt(loose, loose.FileName).StartsWith(Ours.Latest, StringComparison.Ordinal)) wrong.Add("a bad tag wasn't dropped for the latest release");
                served = tree(tag);
                down = true;
                List<ModListing> kept = Browse(out trouble);
                if (kept.Count != 2 || trouble == null || trouble.IndexOf("Showing the list from", StringComparison.Ordinal) < 0) wrong.Add("the release down didn't show the list kept: " + trouble);
                try { Directory.Delete(KeptFolder, true); } catch (IOException) { }
                bool nothing = false;
                try { Browse(out trouble); }
                catch (ModException) { nothing = true; }
                down = false;
                if (!nothing) wrong.Add("the release down with nothing kept still listed something");
                lock (asked)
                    foreach (string url in asked)
                        if (!url.StartsWith(Ours.Page + "/releases/", StringComparison.Ordinal)) { wrong.Add("it asked " + url); break; }
                if (wrong.Count > 0) throw new Exception("Her mods directory went wrong: " + string.Join("; ", wrong.ToArray()));
                checks.Add("PASS her mods directory is our latest release alone: the list, its names and each file come from our release's addresses, the files from the release the list names; a file that doesn't match is refused with nowhere else tried, a bad tag falls back to the latest, and the release down shows the list kept, or nothing");
            }
            finally
            {
                Get = getWas;
                KeptFolder = keptWas;
            }
        }
#endif

        internal static string PublishSteps(Mod mod)
        {
            string file = Path.GetFileName(mod.Path);
            return "1. Press Show my mod file and zip " + file + ", with a preview.png around 16:9 if you have one.\n\n"
                + "2. Press Send it and fill in the form on " + PublishOwner + "/" + PublishRepo + ". You'll need a free GitHub account.\n\n"
                + "3. Drop the zip into the form and send it.\n\n"
                + "4. It's checked before it goes in the mods directory, and nothing goes in without our yes. Capri flags what she "
                + "catches when a mod is installed or run."
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
            request.Accept = "application/octet-stream";
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

        internal static void Clean(string file)
        {
            try { Directory.Delete(Path.GetDirectoryName(file), true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
