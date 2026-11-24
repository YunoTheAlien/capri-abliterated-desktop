using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CapriDesktop
{
    // Her files in another language as copies beside them, like sys_fr.md: made from the English, which she reads, with
    // her notes and setting lines in their language. An edit in a copy goes into the English file. Her brain reads
    // personality_fr.md while French is picked.
    internal static class LangCopies
    {
        private static readonly object gate = new object();
        // The files that get copies, each with whether only its top and "## settings" hold settings.
        private static readonly Dictionary<string, bool> known = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        // The picked language's code, or null for English. Swapped by the checks.
        internal static Func<string> Code = delegate { return Brain.Language == null ? null : Brain.LanguageCode(Brain.Language); };

        internal static void Know(string path, bool lineFile)
        {
            if (string.IsNullOrEmpty(path)) return;
            lock (gate) known[path] = lineFile;
        }

        internal static bool Knows(string path) { lock (gate) return !string.IsNullOrEmpty(path) && known.ContainsKey(path); }

        internal static string[] Files { get { lock (gate) return new List<string>(known.Keys).ToArray(); } }

        internal static string CopyOf(string path, string code)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(code) || code == "en") return null;
            return Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + "_" + code + Path.GetExtension(path));
        }

        // The file to show or read for one of hers: its copy while a language is picked and the copy is there.
        internal static string Reading(string path)
        {
            string copy = CopyOf(path, Code());
            return copy != null && File.Exists(copy) ? copy : path;
        }

        // Her editor's note for a file, swapped for its copy in their language when there is one.
        internal static WrittenNote Shown(WrittenNote note)
        {
            if (note == null) return note;
            string reading = Reading(note.Path);
            return reading == note.Path ? note : new WrittenNote(reading, "", Gate(reading));
        }

        private static readonly Dictionary<string, object> gates = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        private static object Gate(string path)
        {
            lock (gates)
            {
                object one;
                if (!gates.TryGetValue(path, out one)) gates[path] = one = new object();
                return one;
            }
        }

        // A copy saved in her editor: its settings and lines go into the English file at once, and the copy is laid again.
        internal static void Saved(string copy)
        {
            string code = Code();
            foreach (string path in Files)
                if (string.Equals(CopyOf(path, code), copy, StringComparison.OrdinalIgnoreCase)) { Lay(path, code); return; }
        }

        // Every known file's copy for this language.
        internal static int LayAll(string code)
        {
            int written = 0;
            foreach (string path in Files) try { if (Lay(path, code)) written++; } catch (Exception caughtOne) { Brain.Caught(caughtOne); }
            return written;
        }

        // The copy made again from the English file: her notes in their language, and each ">!=" line kept from the copy
        // while it still fits its setting. A copy they changed is folded into the English first and kept as a spare. True
        // when the copy was written.
        internal static bool Lay(string path, string code)
        {
            string copy = CopyOf(path, code);
            if (copy == null || !File.Exists(path)) return false;
            bool lineFile;
            lock (gate) if (!known.TryGetValue(path, out lineFile)) return false;
            lock (Gate(copy))
            {
                string[] old = new string[0];
                if (File.Exists(copy))
                {
                    old = Notes.ReadLines(copy);
                    if (!Notes.Untouched(copy))
                    {
                        Fold(copy, path);
                        if (Brain.SenpaiOn || !Gated(old)) Notes.Spare(copy);
                    }
                }
                string made = Made(Notes.ReadLines(path), old, lineFile);
                if (File.Exists(copy) && Notes.Untouched(copy) && Notes.ReadAll(copy) == made) return false;
                Notes.Overwrite(copy, made);
                Notes.Shipped(copy, made);
                return true;
            }
        }

        private static string Made(string[] english, string[] old, bool lineFile)
        {
            Dictionary<string, string> kept = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (int at in Notes.SettingRows(old, lineFile))
            {
                int above = Notes.Above(old, at);
                string one = old[at].Trim();
                if (above >= 0 && old[above].TrimStart().StartsWith(Notes.SettingMark, StringComparison.Ordinal) && Notes.Fits(old[above], one)) kept[one] = old[above].Trim();
            }
            List<string> plain = new List<string>();
            foreach (string row in english) if (!(row ?? "").TrimStart().StartsWith(Notes.SettingMark, StringComparison.Ordinal)) plain.Add(row);
            string[] noted = Notes.InTheirLanguage(plain.ToArray());
            HashSet<int> settings = new HashSet<int>(Notes.SettingRows(noted, lineFile));
            StringBuilder made = new StringBuilder();
            for (int at = 0; at < noted.Length; at++)
            {
                string line;
                if (settings.Contains(at) && kept.TryGetValue(noted[at].Trim(), out line)) made.Append(line).Append(Environment.NewLine);
                made.Append(noted[at]).Append(Environment.NewLine);
            }
            return made.ToString();
        }

        private static bool IsNote(string row) { return (row ?? "").TrimStart().StartsWith(Notes.NoteMark, StringComparison.Ordinal); }

        private static bool Gated(string[] rows)
        {
            foreach (string row in rows) if ((row ?? "").TrimStart().StartsWith("#", StringComparison.Ordinal) && row.IndexOf(LineFile.SenpaiMark, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        // The copy without its notes becomes the English file, which keeps its own notes above the same rows, or under
        // the heading when a row went. The other side's sections stay out with the switch off. True when it changed.
        internal static bool Fold(string copy, string path)
        {
            string[] theirs = Notes.ReadLines(copy), english = Notes.ReadLines(path);
            List<string> body = Body(theirs);
            if (!Brain.SenpaiOn) body = Ungated(body);
            if (Same(body, Body(english))) return false;

            // The English notes, each block (with the blank rows after it) by its section and the row under it.
            Dictionary<string, List<string>> above = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            Dictionary<string, List<string>> top = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            List<string> pending = new List<string>();
            string under = "";
            for (int at = 0; at < english.Length; at++)
            {
                string row = english[at], one = (row ?? "").Trim();
                if (IsNote(row) || pending.Count > 0 && one.Length == 0) { pending.Add(row); continue; }
                if (pending.Count > 0)
                {
                    string key = under + "\n" + one;
                    if (!above.ContainsKey(key)) above[key] = new List<string>();
                    above[key].AddRange(pending);
                    pending = new List<string>();
                }
                if (one.StartsWith("#", StringComparison.Ordinal)) under = one;
            }
            if (pending.Count > 0) { if (!top.ContainsKey(under)) top[under] = new List<string>(); top[under].AddRange(pending); }

            List<string> made = new List<string>();
            HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);
            under = "";
            foreach (string row in body)
            {
                string one = (row ?? "").Trim();
                List<string> notes;
                string key = under + "\n" + one;
                if (one.Length > 0 && !used.Contains(key) && above.TryGetValue(key, out notes)) { made.AddRange(notes); used.Add(key); }
                made.Add(row);
                if (one.StartsWith("#", StringComparison.Ordinal))
                {
                    under = one;
                    // Notes whose row went sit straight under their heading.
                    foreach (KeyValuePair<string, List<string>> left in above)
                        if (left.Key.StartsWith(under + "\n", StringComparison.Ordinal) && !Has(body, left.Key.Substring(under.Length + 1), under)) { made.AddRange(left.Value); used.Add(left.Key); }
                }
            }
            foreach (KeyValuePair<string, List<string>> left in top) if (Has(body, null, left.Key) || left.Key.Length == 0) made.AddRange(left.Value);
            Notes.Put(path, string.Join(Environment.NewLine, made.ToArray()) + Environment.NewLine);
            return true;
        }

        // Whether a section is in the body, and with that row in it when one is named.
        private static bool Has(List<string> body, string row, string heading)
        {
            string under = "";
            foreach (string one in body)
            {
                string trimmed = (one ?? "").Trim();
                if (trimmed.StartsWith("#", StringComparison.Ordinal)) { under = trimmed; if (row == null && under == heading) return true; continue; }
                if (row != null && under == heading && trimmed == row) return true;
            }
            return row == null && heading.Length == 0;
        }

        // A file's rows without its notes and the blank rows straight after each.
        private static List<string> Body(string[] rows)
        {
            List<string> body = new List<string>();
            bool after = false;
            foreach (string row in rows)
            {
                if (IsNote(row)) { after = true; continue; }
                if (after && (row ?? "").Trim().Length == 0) continue;
                after = false;
                body.Add(row);
            }
            return body;
        }

        private static List<string> Ungated(List<string> rows)
        {
            List<string> kept = new List<string>();
            bool gated = false;
            foreach (string row in rows)
            {
                string one = (row ?? "").Trim();
                if (one.StartsWith("#", StringComparison.Ordinal)) gated = one.IndexOf(LineFile.SenpaiMark, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!gated) kept.Add(row);
            }
            return kept;
        }

        private static bool Same(List<string> one, List<string> other)
        {
            return string.Join("\n", one.ToArray()).TrimEnd() == string.Join("\n", other.ToArray()).TrimEnd();
        }

        // ---------- A file written whole in their language ----------

        // personality.md's copy, written whole in their language. Untouched, it follows the English; edited, it is theirs
        // until the English changes, then kept as a spare and made again. An older build's translation in place goes
        // back to English. True when the copy was written.
        internal static bool Whole(string path, string english, string code)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            string now = Notes.ReadAll(path);
            if (Notes.Untouched(path) && Notes.Plain(now) != Notes.Plain(english) && Notes.WholeFile(english))
            {
                // Put in their language in place by an older build: that text is the copy, and the English goes back.
                string moved = CopyOf(path, code);
                if (moved != null && !File.Exists(moved))
                {
                    Notes.Overwrite(moved, now);
                    Notes.Shipped(moved, now);
                    Notes.Shipped(moved + From, english);
                }
                Notes.Overwrite(path, english);
                Notes.Shipped(path, english);
                now = english;
            }
            string copy = CopyOf(path, code);
            if (copy == null || !Notes.WholeFile(english) && !Notes.WholeFile(now)) return false;
            lock (Gate(copy))
            {
                bool there = File.Exists(copy), mine = there && Notes.Untouched(copy), followed = there && Notes.Stamped(copy + From, now);
                if (there && !mine)
                {
                    if (followed) return false;
                    Notes.Spare(copy);
                }
                string made = Notes.WholeInTheirLanguage(now);
                if (there && mine && followed && Notes.ReadAll(copy) == made) return false;
                Notes.Overwrite(copy, made);
                Notes.Shipped(copy, made);
                Notes.Shipped(copy + From, now);
                return true;
            }
        }

        // Beside a whole copy in .shipped: the English it was made from.
        private const string From = ".from";
        // Copies with a stand-in translator: made from the English, kept for a switch back, an edit folded into the
        // English, and personality's copy read by her brain, kept once theirs and made again when the English changes.
        internal static void Check(List<string> checks, string folder)
        {
            Directory.CreateDirectory(folder);
            Func<string> codeWas = Code;
            Func<string, string> translateWas = Notes.Translate;
            string personalityWas = Brain.PersonalityPath;
            string path = Path.Combine(folder, "sys.md"), whole = Path.Combine(folder, "personality.md");
            try
            {
                string code = "fr";
                Code = delegate { return code; };
                Notes.Translate = delegate(string line) { return "[" + code + "] " + line; };
                string english = "# Her settings\n\n>! What these do.\n\n## Speed\n\n>! How fast she talks.\nspeed: 2\n\n## Lines\n\nhello there\n";
                english = english.Replace("\n", Environment.NewLine);
                File.WriteAllText(path, english);
                Notes.Shipped(path, english);
                Know(path, false);

                bool made = Lay(path, "fr");
                string fr = CopyOf(path, "fr"), copied = Notes.ReadAll(fr);
                if (!made || Notes.ReadAll(path) != english || copied.IndexOf(">! [fr] How fast she talks.", StringComparison.Ordinal) < 0 || copied.IndexOf("speed: 2", StringComparison.Ordinal) < 0 || !Notes.Untouched(fr))
                    throw new Exception("The French copy wasn't made from the English with its notes in French and the English untouched: " + copied);
                Notes.Explained(fr, new Dictionary<string, string> { { "speed: 2", "Vitesse: speed: 2 (1 a 5)" } });
                Notes.Shipped(fr, Notes.ReadAll(fr));
                code = "es";
                Lay(path, "es");
                code = "fr";
                bool again = Lay(path, "fr");
                if (again || Notes.ReadAll(fr).IndexOf(">!= Vitesse: speed: 2", StringComparison.Ordinal) < 0 || !File.Exists(CopyOf(path, "es")))
                    throw new Exception("Switching back to French wrote the copy again or lost its setting line: written " + again);
                checks.Add("PASS a language's copies are made from her English file, notes in that language and the English untouched, and kept: back to French after Spanish writes nothing and keeps the line above each setting");

                string edited = Notes.ReadAll(fr).Replace("speed: 2", "speed: 5").Replace("hello there", "hello there" + Environment.NewLine + "bonjour toi");
                File.WriteAllText(fr, edited);
                Saved(fr);
                string folded = Notes.ReadAll(path);
                bool spared = Notes.SparesForTest(fr).Length == 1;
                code = "es";
                Lay(path, "es");
                string es = Notes.ReadAll(CopyOf(path, "es"));
                if (folded.IndexOf("speed: 5", StringComparison.Ordinal) < 0 || folded.IndexOf("bonjour toi", StringComparison.Ordinal) < 0 || folded.IndexOf(">! How fast she talks.", StringComparison.Ordinal) < 0
                    || folded.IndexOf("[fr]", StringComparison.Ordinal) >= 0 || folded.IndexOf(Notes.SettingMark, StringComparison.Ordinal) >= 0 || !spared || es.IndexOf("speed: 5", StringComparison.Ordinal) < 0 || es.IndexOf(">! [es] How fast she talks.", StringComparison.Ordinal) < 0)
                    throw new Exception("An edit in the French copy didn't go into the English with its notes kept, or Spanish didn't follow: " + folded + " / spare " + spared + " / " + es);
                checks.Add("PASS a setting and a line changed in the French copy go into her English file, which keeps its English notes and gets none of the copy's; the edited copy is kept as a spare, and the Spanish copy follows");

                code = "fr";
                string mine = "<!--\n" + Notes.WholeMark + " shown in your language.\n-->\n\n## Her character\n\nYou are Capri. You like rain.\n";
                mine = mine.Replace("\n", Environment.NewLine);
                File.WriteAllText(whole, "[fr] Old translation in place." + Environment.NewLine);
                Notes.Shipped(whole, "[fr] Old translation in place." + Environment.NewLine);
                Whole(whole, mine, "fr");
                string wholeFr = CopyOf(whole, "fr");
                bool movedOut = Notes.ReadAll(whole) == mine && File.Exists(wholeFr) && Notes.ReadAll(wholeFr).IndexOf("[fr] You are Capri.", StringComparison.Ordinal) >= 0;
                File.Delete(wholeFr);
                Whole(whole, mine, "fr");
                bool translated = Notes.ReadAll(wholeFr).IndexOf("[fr] You are Capri.", StringComparison.Ordinal) >= 0;
                Brain.PersonalityPath = whole;
                bool read = Brain.Personality.IndexOf("[fr] You are Capri.", StringComparison.Ordinal) >= 0;
                File.AppendAllText(wholeFr, "Mine now." + Environment.NewLine);
                bool keptTheirs = !Whole(whole, mine, "fr") && Notes.ReadAll(wholeFr).IndexOf("Mine now.", StringComparison.Ordinal) >= 0;
                string changed = mine + "You like snow." + Environment.NewLine;
                File.WriteAllText(whole, changed);
                bool remade = Whole(whole, changed, "fr") && Notes.ReadAll(wholeFr).IndexOf("[fr] You like snow.", StringComparison.Ordinal) >= 0 && Notes.SparesForTest(wholeFr).Length == 1;
                if (!movedOut || !translated || !read || !keptTheirs || !remade)
                    throw new Exception("personality.md's copy went wrong: moved out of an older build's file " + movedOut + ", made in French " + translated + ", read by her brain " + read + ", kept once theirs " + keptTheirs + ", made again with a spare when the English changed " + remade);
                checks.Add("PASS personality.md's copy: an older build's in-place translation goes back to her English with personality_fr.md beside it, a copy is made whole in French and her brain reads it while French is picked, an edited copy stays theirs, and a change to the English keeps theirs as a spare and makes it again");
            }
            finally
            {
                Code = codeWas;
                Notes.Translate = translateWas;
                Brain.PersonalityPath = personalityWas;
                lock (gate) known.Remove(path);
            }
        }
    }
}
