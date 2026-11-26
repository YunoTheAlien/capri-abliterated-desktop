using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // Upstream's store mods, translated on import: none of their code is carried, only our fix for each exact file by its
    // SHA-256 (assets/mod-fixes.json). Another version comes in untested, a held or set aside one is refused.
    internal static class ModFixes
    {
        [DataContract]
        internal sealed class Step
        {
            [DataMember(Name = "op")] internal string Op;
            [DataMember(Name = "find", EmitDefaultValue = false)] internal string Find;
            [DataMember(Name = "with", EmitDefaultValue = false)] internal string With;
            [DataMember(Name = "count", EmitDefaultValue = false)] internal int Count;
            [DataMember(Name = "from", EmitDefaultValue = false)] internal string From;
            [DataMember(Name = "to", EmitDefaultValue = false)] internal string To;
            [DataMember(Name = "name", EmitDefaultValue = false)] internal string Name;
            [DataMember(Name = "set", EmitDefaultValue = false)] internal string Set;
            [DataMember(Name = "allow", EmitDefaultValue = false)] internal string[] Allow;
        }

        [DataContract]
        internal sealed class Fix
        {
            [DataMember(Name = "file")] internal string File;
            [DataMember(Name = "sha256")] internal string Sha256;
            [DataMember(Name = "version", EmitDefaultValue = false)] internal string Version;
            [DataMember(Name = "verdict")] internal string Verdict;
            [DataMember(Name = "why", EmitDefaultValue = false)] internal string Why;
            [DataMember(Name = "result", EmitDefaultValue = false)] internal string Result;
            [DataMember(Name = "steps", EmitDefaultValue = false)] internal Step[] Steps;
            [DataMember(Name = "toggles", EmitDefaultValue = false)] internal string[] Toggles;
            // Its settings moved to her Toggles by our own steps, so it comes out other than the adaption layer's file.
            [DataMember(Name = "rewritten", EmitDefaultValue = false)] internal bool Rewritten;
        }

        [DataContract]
        private sealed class Book
        {
            [DataMember(Name = "format")] internal int Format;
            [DataMember(Name = "mods")] internal Dictionary<string, Fix> Mods;
            [DataMember(Name = "his")] internal string[] His;
        }

        // What happened to a file: the file to import (the translated copy, or the one given) and how it came in.
        internal sealed class Outcome
        {
            internal string File, Mark, Folder;
            internal bool Translated;
        }

        internal const string TranslatedMark = "Translated for this build.", UntestedMark = "Untested on this build.";
        private static Dictionary<string, Fix> book;
        private static Regex servers;
        // For the checks: a book of their own in place of hers.
        internal static Func<Dictionary<string, Fix>> Loaded = Carried;

        private static Dictionary<string, Fix> Carried()
        {
            if (book != null) return book;
            Dictionary<string, Fix> read = new Dictionary<string, Fix>(StringComparer.Ordinal);
            try
            {
                using (Stream carried = typeof(ModFixes).Assembly.GetManifestResourceStream("Capri.ModFixes.json"))
                    if (carried != null)
                    {
                        Book whole = (Book)new DataContractJsonSerializer(typeof(Book), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true }).ReadObject(carried);
                        if (whole != null && whole.Mods != null) read = new Dictionary<string, Fix>(whole.Mods, StringComparer.Ordinal);
                        if (whole != null && whole.His != null && whole.His.Length > 0) servers = new Regex(string.Join("|", whole.His), RegexOptions.IgnoreCase);
                    }
            }
            catch (Exception caughtOne) { Brain.Caught(caughtOne); }
            book = read;
            return book;
        }

        // Matching nothing when the book carries no addresses.
        private static Regex CarriedServers()
        {
            Carried();
            return servers ?? new Regex("(?!)");
        }

        internal static string Sha256(byte[] bytes)
        {
            using (SHA256 hashing = SHA256.Create())
            {
                StringBuilder hex = new StringBuilder(64);
                foreach (byte one in hashing.ComputeHash(bytes)) hex.Append(one.ToString("x2"));
                return hex.ToString();
            }
        }

        // Our fix for this file, or what we know of its mod: by its hash first, then by upstream's file name for another version.
        internal static Outcome Apply(string file)
        {
            Outcome outcome = new Outcome { File = file };
            byte[] bytes;
            try { bytes = File.ReadAllBytes(file); }
            catch (IOException) { return outcome; }
            catch (UnauthorizedAccessException) { return outcome; }
            string hash = Sha256(bytes), name = Path.GetFileName(file);
            Dictionary<string, Fix> all = Loaded();
            KeyValuePair<string, Fix> exact = default(KeyValuePair<string, Fix>), named = default(KeyValuePair<string, Fix>);
            foreach (KeyValuePair<string, Fix> one in all)
            {
                if (string.Equals(one.Value.Sha256, hash, StringComparison.OrdinalIgnoreCase)) { exact = one; break; }
                if (named.Value == null && string.Equals(one.Value.File, name, StringComparison.OrdinalIgnoreCase)) named = one;
            }
            Fix known = exact.Value ?? named.Value;
            if (known == null) return outcome;
            // Set aside, held or ours already: refused whatever the version, since its reason is about what the mod does.
            if (known.Verdict == "held") throw new ModException("Held for now: " + known.Why);
            if (known.Verdict == "rejected") throw new ModException("Not for this build: " + known.Why);
            // Ours under upstream's file name is ours: only upstream's exact file is turned away for our own copy.
            if (known.Verdict == "superseded" || known.Verdict == "shipped")
            {
                if (exact.Value != null) throw new ModException("This build has its own copy of " + known.File + " already, kept in place of this one.");
                return outcome;
            }
            // Another version than the fix was made for: in as it is, untested.
            if (exact.Value == null)
            {
                Match his = HisServer().Match(Encoding.UTF8.GetString(bytes));
                if (his.Success) throw new ModException("Not for this build: this version of " + known.File + " reaches " + his.Value + ".");
                outcome.Mark = UntestedMark + " Our fix is for " + (known.Version ?? "another version") + ".";
                return outcome;
            }
            outcome.Translated = true;
            outcome.Mark = TranslatedMark;
            if (known.Steps == null || known.Steps.Length == 0) return outcome;
            string text = Encoding.UTF8.GetString(bytes);
            string newline = text.Contains("\r\n") ? "\r\n" : "\n";
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");
            foreach (Step step in known.Steps) text = Run(step, text, exact.Key);
            byte[] made = new UTF8Encoding(false).GetBytes(text.Replace("\n", newline));
            if (known.Result != null && !string.Equals(Sha256(made), known.Result, StringComparison.OrdinalIgnoreCase))
                throw new ModException("Our fix for " + known.File + " didn't come out as checked, so it wasn't imported.");
            outcome.Folder = Path.Combine(Path.GetTempPath(), "capri-fixed-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outcome.Folder);
            outcome.File = Path.Combine(outcome.Folder, name);
            File.WriteAllBytes(outcome.File, made);
            return outcome;
        }

        // Whether our fix book turns this file away (held, set aside, or ours already), as its import would.
        internal static bool Refuses(string file)
        {
            try { Done(Apply(file)); return false; }
            catch (ModException) { return true; }
        }

        // The translated copy's folder, gone once it is imported.
        internal static void Done(Outcome outcome)
        {
            if (outcome == null || outcome.Folder == null) return;
            try { Directory.Delete(outcome.Folder, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static string Run(Step step, string text, string mod)
        {
            switch (step.Op)
            {
                case "replace":
                    {
                        int found = Count(text, step.Find);
                        if (step.Count > 0 && found != step.Count) throw Broke(mod, step, found);
                        return text.Replace(step.Find, step.With ?? "");
                    }
                case "regex":
                    return new Regex(step.Find).Replace(text, (step.With ?? "").Replace("$", "$$"));
                case "cut":
                    {
                        int from = text.IndexOf(step.From, StringComparison.Ordinal);
                        int to = from < 0 ? -1 : text.IndexOf(step.To, from, StringComparison.Ordinal);
                        if (from < 0 || to < 0) throw Broke(mod, step, 0);
                        return text.Substring(0, from) + (step.With ?? "") + text.Substring(to);
                    }
                case "base64-const": return Unpacked(text, step, mod);
                case "drop-his-lines": return WithoutHisLines(text, step, mod);
                case "comment-links": return CommentLinks(text, step.Allow ?? new string[0]);
                case "permissions": return Permissions(text, step.Set);
                default: throw new ModException("Our fix for " + mod + " has a step this build doesn't know (" + step.Op + "), so it wasn't imported.");
            }
        }

        private static int Count(string text, string find)
        {
            int count = 0;
            for (int at = text.IndexOf(find, StringComparison.Ordinal); at >= 0; at = text.IndexOf(find, at + find.Length, StringComparison.Ordinal)) count++;
            return count;
        }

        private static ModException Broke(string mod, Step step, int found)
        {
            return new ModException("Our fix for " + mod + " didn't find its place (" + step.Op + ", " + found + " found), so nothing was changed or imported.");
        }

        // A verbatim string constant written in base64, written back as plain text.
        private static string Unpacked(string text, Step step, string mod)
        {
            Match packed = new Regex("const string " + Regex.Escape(step.Name) + " = @\"([^\"]*)\";").Match(text);
            if (!packed.Success) throw Broke(mod, step, 0);
            string plain = Encoding.UTF8.GetString(Convert.FromBase64String(Regex.Replace(packed.Groups[1].Value, @"\s", "")));
            return text.Substring(0, packed.Index) + "const string " + step.Name + " = @\"" + plain.Replace("\"", "\"\"") + "\";" + text.Substring(packed.Index + packed.Length);
        }

        // His addresses, carried in the book from ci/audit-servers.py's list so the two never differ.
        internal static Func<Regex> HisServer = CarriedServers;

        private static string WithoutHisLines(string text, Step step, string mod)
        {
            string opening = "const string " + step.Name + " = @\"";
            int start = text.IndexOf(opening, StringComparison.Ordinal);
            if (start < 0) throw Broke(mod, step, 0);
            start += opening.Length;
            int end = text.IndexOf("\";", start, StringComparison.Ordinal);
            List<string> kept = new List<string>();
            foreach (string row in text.Substring(start, end - start).Split('\n')) if (!HisServer().IsMatch(row)) kept.Add(row);
            return text.Substring(0, start) + string.Join("\n", kept.ToArray()) + text.Substring(end);
        }

        private static readonly Regex Link = new Regex(@"[a-z][a-z0-9+.-]*://[A-Za-z0-9._:~%/?#@!$&()*+,;=-]+", RegexOptions.IgnoreCase);

        private static string HostOf(string url)
        {
            string rest = url.Substring(url.IndexOf("://", StringComparison.Ordinal) + 3);
            rest = rest.Substring(rest.LastIndexOf('@') + 1);
            int cut = rest.IndexOfAny(new[] { '/', '?', '#', ':' });
            return (cut < 0 ? rest : rest.Substring(0, cut)).ToLowerInvariant();
        }

        // Links in comments go, but for the hosts a mod is let reach; strings are left as they are.
        private static string CommentLinks(string text, string[] allow)
        {
            MatchEvaluator scrub = delegate(Match one) { return Array.IndexOf(allow, HostOf(one.Value)) >= 0 ? one.Value : "[link removed]"; };
            StringBuilder made = new StringBuilder(text.Length);
            int i = 0, n = text.Length;
            while (i < n)
            {
                char c = text[i];
                if (c == '"' || c == '\'')
                {
                    bool verbatim = c == '"' && i > 0 && text[i - 1] == '@';
                    int j = i + 1;
                    while (j < n)
                    {
                        char d = text[j];
                        if (verbatim)
                        {
                            if (d == '"') { if (j + 1 < n && text[j + 1] == '"') { j += 2; continue; } j++; break; }
                        }
                        else
                        {
                            if (d == '\\') { j += 2; continue; }
                            if (d == c || d == '\n') { j++; break; }
                        }
                        j++;
                    }
                    j = Math.Min(j, n);
                    made.Append(text, i, j - i);
                    i = j;
                    continue;
                }
                if (string.CompareOrdinal(text, i, "//", 0, 2) == 0)
                {
                    int j = text.IndexOf('\n', i);
                    if (j < 0) j = n;
                    made.Append(Link.Replace(text.Substring(i, j - i), scrub));
                    i = j;
                    continue;
                }
                if (string.CompareOrdinal(text, i, "/*", 0, 2) == 0)
                {
                    int j = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    j = j < 0 ? n : j + 2;
                    made.Append(Link.Replace(text.Substring(i, j - i), scrub));
                    i = j;
                    continue;
                }
                made.Append(c);
                i++;
            }
            return made.ToString();
        }

        private static readonly Regex HeaderLine = new Regex(@"^//\s*([a-z-]+)\s*:\s*(.*?)\s*$"), PermissionsLine = new Regex(@"^\s*//\s*permissions\s*:\s*(.*?)\s*$");

        // The header's permissions line set to what the mod's calls need: older lines dropped, the new one at the
        // header's end, before any blank lines closing it.
        private static string Permissions(string text, string set)
        {
            string[] lines = text.Split('\n');
            int i = 0;
            while (i < lines.Length)
            {
                string line = lines[i].Trim().TrimStart('\uFEFF');
                if (line.Length > 0 && !HeaderLine.IsMatch(line)) break;
                i++;
            }
            List<string> kept = new List<string>();
            for (int h = 0; h < i; h++) if (!PermissionsLine.IsMatch(lines[h])) kept.Add(lines[h]);
            if (!string.IsNullOrEmpty(set))
            {
                int at = kept.Count;
                while (at > 0 && kept[at - 1].Trim().Length == 0) at--;
                kept.Insert(at, "// permissions: " + set);
            }
            for (int r = i; r < lines.Length; r++) kept.Add(lines[r]);
            return string.Join("\n", kept.ToArray());
        }
    }

#if CHECKS
    internal sealed partial class Companion
    {
        // Upstream's store mods through the importer: the fix book read, each step on a stand-in, and with their mods' snapshot
        // on this PC each of the 71 translated byte for byte as the adaption layer made it, compiled and in.
        private void SmokeModFixes(List<string> checks, string output)
        {
            List<string> wrong = new List<string>();
            Dictionary<string, ModFixes.Fix> book = ModFixes.Loaded();
            Dictionary<string, int> verdicts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (ModFixes.Fix one in book.Values) verdicts[one.Verdict] = (verdicts.ContainsKey(one.Verdict) ? verdicts[one.Verdict] : 0) + 1;
            Func<string, int> counted = delegate(string verdict) { return verdicts.ContainsKey(verdict) ? verdicts[verdict] : 0; };
            if (ModFixes.HisServer().ToString() == "(?!)") wrong.Add("the fix book carries none of his addresses");
            if (book.Count != 113 || counted("adapted") != 71 || counted("held") != 2 || counted("rejected") != 27 || counted("superseded") != 5 || counted("shipped") != 8)
                wrong.Add("the fix book: " + book.Count + " mods, " + counted("adapted") + " adapted, " + counted("held") + " held, " + counted("rejected") + " set aside");
            string room = Path.Combine(output, "mod-fixes");
            Directory.CreateDirectory(room);
            Func<Dictionary<string, ModFixes.Fix>> loadedWas = ModFixes.Loaded;
            try
            {
                // A stand-in upstream mod and our fix for it.
                string his = "// capri-mod: 1\n// id: fix-check\n// name: Fix check\n// version: 1.0.0\n// see https://example.com/a for more\n\nusing System;\npublic class FixCheck { string a = \"https://example.com/b\"; int Old() { return 1; } }\n";
                string mine = "// capri-mod: 1\n// id: fix-check\n// name: Fix check\n// version: 1.0.0\n// permissions: chat\n// see [link removed] for more\n\nusing System;\npublic class FixCheck { string a = \"https://example.com/b\"; int New() { return 1; } }\n";
                string hisFile = Path.Combine(room, "fix-check.capri-code");
                File.WriteAllText(hisFile, his, new UTF8Encoding(false));
                ModFixes.Fix fix = new ModFixes.Fix
                {
                    File = "fix-check.capri-code", Sha256 = ModFixes.Sha256(File.ReadAllBytes(hisFile)), Version = "1.0.0", Verdict = "adapted",
                    Result = ModFixes.Sha256(new UTF8Encoding(false).GetBytes(mine)),
                    Steps = new[]
                    {
                        new ModFixes.Step { Op = "replace", Find = "int Old()", With = "int New()", Count = 1 },
                        new ModFixes.Step { Op = "comment-links", Allow = new string[0] },
                        new ModFixes.Step { Op = "permissions", Set = "chat" }
                    }
                };
                Dictionary<string, ModFixes.Fix> standIn = new Dictionary<string, ModFixes.Fix>(StringComparer.Ordinal)
                {
                    { "fix-check", fix },
                    { "held-check", new ModFixes.Fix { File = "held-check.capri-code", Sha256 = "00", Verdict = "held", Why = "it reaches her brain directly." } },
                    { "ours-check", new ModFixes.Fix { File = "ours-check.capri-mod", Sha256 = "00", Verdict = "shipped", Why = "ours." } }
                };
                ModFixes.Loaded = delegate { return standIn; };
                ModFixes.Outcome came = ModFixes.Apply(hisFile);
                string made = came.File == hisFile ? null : File.ReadAllText(came.File);
                bool translated = came.Translated && made == mine && came.Mark == ModFixes.TranslatedMark;
                ModFixes.Done(came);
                if (!translated || Directory.Exists(came.Folder)) wrong.Add("the stand-in wasn't translated as its fix says: " + (made ?? "untouched"));
                // One byte changed: another version, in untouched and untested.
                string other = Path.Combine(room, "other", "fix-check.capri-code");
                Directory.CreateDirectory(Path.GetDirectoryName(other));
                File.WriteAllText(other, his.Replace("1.0.0", "1.0.1"), new UTF8Encoding(false));
                came = ModFixes.Apply(other);
                if (came.File != other || came.Translated || came.Mark == null || !came.Mark.StartsWith(ModFixes.UntestedMark, StringComparison.Ordinal)) wrong.Add("another version was changed or not marked untested: " + came.Mark);
                // Another version naming one of upstream's addresses is refused (a stand-in address in place of theirs).
                Func<Regex> serversWas = ModFixes.HisServer;
                ModFixes.HisServer = delegate { return new Regex(@"stand-in-server\.example", RegexOptions.IgnoreCase); };
                File.WriteAllText(other, his.Replace("example.com/a", "stand-in-server.example"), new UTF8Encoding(false));
                bool workerRefused = false;
                try { ModFixes.Apply(other); } catch (ModException) { workerRefused = true; }
                finally { ModFixes.HisServer = serversWas; }
                if (!workerRefused) wrong.Add("another version naming his address came in");
                // A step that doesn't find its place stops the import with nothing written.
                fix.Steps[0].Count = 2;
                string[] before = Directory.GetDirectories(Path.GetTempPath(), "capri-fixed-*");
                bool stopped = false;
                try { ModFixes.Apply(hisFile); } catch (ModException) { stopped = true; }
                if (!stopped || Directory.GetDirectories(Path.GetTempPath(), "capri-fixed-*").Length != before.Length) wrong.Add("a step that missed its place didn't stop cleanly");
                fix.Steps[0].Count = 1;
                // Held: refused. Ours under upstream's name: in as it is. A mod we know nothing of: in as before.
                string held = Path.Combine(room, "held-check.capri-code"), ours = Path.Combine(room, "ours-check.capri-mod"), unknown = Path.Combine(room, "nobody.capri-mod");
                foreach (string one in new[] { held, ours, unknown }) File.WriteAllText(one, "{}");
                bool heldRefused = false;
                try { ModFixes.Apply(held); } catch (ModException refused) { heldRefused = refused.Message.Contains("reaches her brain directly"); }
                ModFixes.Outcome oursCame = ModFixes.Apply(ours), unknownCame = ModFixes.Apply(unknown);
                if (!heldRefused || oursCame.File != ours || oursCame.Mark != null || unknownCame.File != unknown || unknownCame.Mark != null)
                    wrong.Add("held refused " + heldRefused + ", ours " + (oursCame.Mark ?? "unmarked") + ", unknown " + (unknownCame.Mark ?? "unmarked"));
            }
            finally { ModFixes.Loaded = loadedWas; }
            // His mods' snapshot, when this PC has it (never shipped, never in git).
            string snapshot = null, adapted = null;
            for (DirectoryInfo up = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); up != null && snapshot == null; up = up.Parent)
            {
                string found = Path.Combine(up.FullName, "agents", "mods-survey", "lachydotmcg-capri-mods-0ba7a7d", "mods");
                if (Directory.Exists(found)) { snapshot = found; adapted = Path.Combine(up.FullName, "Inbox", "Saph_Mods_main", "adapted"); }
            }
            int translatedCount = 0, sameCount = 0, rewrittenCount = 0, imported = 0, refusedCount = 0;
            if (snapshot != null)
            {
                // Compiled here, not imported: a second mods list would rewrite her zones and slots while she runs.
                {
                    foreach (KeyValuePair<string, ModFixes.Fix> one in book)
                    {
                        string his = Path.Combine(snapshot, one.Key, one.Value.File);
                        if (!File.Exists(his)) { wrong.Add("not in the snapshot: " + one.Key); continue; }
                        if (one.Value.Verdict != "adapted")
                        {
                            bool refused = false;
                            try { ModFixes.Outcome came = ModFixes.Apply(his); ModFixes.Done(came); } catch (ModException) { refused = true; }
                            if (refused) refusedCount++; else wrong.Add(one.Key + " (" + one.Value.Verdict + ") wasn't refused");
                            continue;
                        }
                        ModFixes.Outcome made = null;
                        try
                        {
                            made = ModFixes.Apply(his);
                            if (!made.Translated) { wrong.Add(one.Key + " wasn't translated"); continue; }
                            translatedCount++;
                            string theirs = adapted == null ? null : Path.Combine(adapted, one.Key, one.Value.File);
                            if (one.Value.Rewritten) rewrittenCount++;
                            else if (theirs != null && File.Exists(theirs) && ModFixes.Sha256(File.ReadAllBytes(theirs)) == ModFixes.Sha256(File.ReadAllBytes(made.File))) sameCount++;
                            else wrong.Add(one.Key + " came out other than the adaption layer's file");
                            if (one.Value.File.EndsWith(Mods.CodeExtension, StringComparison.OrdinalIgnoreCase))
                            {
                                string broke = Mods.CompilesForChecks(File.ReadAllBytes(made.File));
                                if (broke == null) imported++; else wrong.Add(one.Key + " didn't compile: " + broke);
                            }
                        }
                        catch (ModException refused) { wrong.Add(one.Key + ": " + refused.Message); }
                        finally { ModFixes.Done(made); }
                    }
                }
            }
            if (wrong.Count > 0) throw new Exception("Lachy's store mods went wrong through the importer: " + string.Join("; ", wrong.ToArray()));
            checks.Add("PASS his store mods through the importer: our fix book holds 113 (71 to translate, 2 held, 27 set aside, 13 ours already); a fix runs only on the exact file it was made for and comes out as checked, another version comes in untouched and marked untested unless it names his servers, a step that misses its place stops with nothing written, a held one is refused with its reason, and ours or an unknown mod come in as before");
            if (snapshot == null) checks.Add("LIMIT his store mods' snapshot isn't on this PC (agents\\mods-survey), so the 71 weren't run through the importer here");
            else checks.Add("PASS each of his 71 store mods we translate came out as checked: " + sameCount + " byte for byte as the adaption layer made them and " + rewrittenCount + " with their settings moved to her Toggles, of " + translatedCount + "; the code ones compiled with their calls read (" + imported + "), and the 42 others were refused (" + refusedCount + ")");
        }
    }

    internal sealed partial class Mods
    {
        // A code mod read, compiled and its calls listed, nothing of it started: null when it builds, else why not.
        internal static string CompilesForChecks(byte[] file)
        {
            try
            {
                string text = Words(file);
                ReadCode(text);
                CompiledCalls(Build(text));
                return null;
            }
            catch (ModException broke) { return broke.Message; }
        }
    }
#endif
}
