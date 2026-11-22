using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

namespace CapriDesktop
{
    // "spell check this" and "proofread this" over a window's text: Windows' spell checker and her grammar rules, plus
    // one brain pass for a proofread. Nothing is written back before yes; fixes kept in grammar.md let undo and redo
    // survive a restart.
    internal static class Proof
    {
        internal sealed class Ask
        {
            // A proofread adds her brain's pass to the spell checker and the rules.
            internal bool Brain;
            internal string App;
        }

        internal sealed class Fix
        {
            internal string Was, Now, Why;
        }

        private const RegexOptions Loose = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        private static readonly Regex Wake = new Regex(@"^\s*(?:(?:hey|hi|ok(?:ay)?)\s+)?(?:capri\s*[,:;!.]*\s*)?(?:(?:can|could|would|will)\s+you\s+)?(?:please\s+)?", Loose);
        private const string Thing = @"(?:this|that|it|my\s+(?:writing|text|document|doc|essay|email|message|post|work)|the\s+(?:text|document|doc|selection|selected\s+text|whole\s+thing))";
        private const string In = @"(?:\s+(?:in|on|from)\s+(?:the\s+|my\s+)?(?<app>[^?.!]+?))?";
        private static readonly Regex Spell = new Regex(@"^(?:spell\s*check|spellcheck)(?:\s+" + Thing + ")?" + In + @"(?:\s+(?:for\s+me|please))*[\s?.!]*$|^check\s+(?:my\s+|the\s+)?spelling(?:\s+(?:of|in)\s+" + Thing + ")?" + In + @"(?:\s+(?:for\s+me|please))*[\s?.!]*$", Loose);
        private static readonly Regex Read = new Regex(@"^(?:proof\s*read|proof)(?:\s+" + Thing + ")?" + In + @"(?:\s+(?:for\s+me|please))*[\s?.!]*$|^check\s+(?:my\s+|the\s+)?(?:grammar|writing)(?:\s+(?:of|in)\s+" + Thing + ")?" + In + @"(?:\s+(?:for\s+me|please))*[\s?.!]*$", Loose);
        private static readonly Regex FixAll = new Regex(@"^(?:yes,?\s+|yeah,?\s+|ok(?:ay)?,?\s+|go\s+ahead\s+and\s+)?fix\s+(?:it|them|that|those|all(?:\s+of\s+them)?|everything|the\s+lot)(?:\s+please)?[\s?.!]*$", Loose);
        private static readonly Regex Back = new Regex(@"^(?:undo|undo\s+(?:that|it|the\s+fix(?:es)?|your\s+edit|my\s+last\s+edit|the\s+last\s+edit)|put\s+it\s+back)(?:\s+please)?[\s?.!]*$", Loose);
        private static readonly Regex Again = new Regex(@"^(?:redo|redo\s+(?:that|it|the\s+fix(?:es)?|your\s+edit))(?:\s+please)?[\s?.!]*$", Loose);

        // What a spell check or a proofread asked for, or null for anything else.
        internal static Ask Asked(string said)
        {
            string text = Wake.Replace((said ?? "").Trim(), "").Trim();
            Match spell = Spell.Match(text);
            if (spell.Success) return new Ask { Brain = false, App = Group(spell) };
            Match read = Read.Match(text);
            if (read.Success) return new Ask { Brain = true, App = Group(read) };
            return null;
        }

        private static string Group(Match found) { return found.Groups["app"].Success && found.Groups["app"].Value.Trim().Length > 0 ? found.Groups["app"].Value.Trim() : null; }

        internal static bool FixIt(string said) { return FixAll.IsMatch(Wake.Replace((said ?? "").Trim(), "").Trim()); }

        // -1 for undo, 1 for redo, 0 for neither.
        internal static int Step(string said)
        {
            string text = Wake.Replace((said ?? "").Trim(), "").Trim();
            return Back.IsMatch(text) ? -1 : Again.IsMatch(text) ? 1 : 0;
        }

        // ---------- Windows' spell checker (the Spell Checking API, in Windows since 8) ----------

        [ComImport, Guid("8E018A9D-2415-4677-BF08-794EA61F94BB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISpellCheckerFactory
        {
            [PreserveSig] int GetSupportedLanguages(out IEnumString value);
            [PreserveSig] int IsSupported([MarshalAs(UnmanagedType.LPWStr)] string languageTag, out int value);
            [PreserveSig] int CreateSpellChecker([MarshalAs(UnmanagedType.LPWStr)] string languageTag, out ISpellChecker value);
        }

        [ComImport, Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISpellChecker
        {
            [PreserveSig] int GetLanguageTag([MarshalAs(UnmanagedType.LPWStr)] out string value);
            [PreserveSig] int Check([MarshalAs(UnmanagedType.LPWStr)] string text, out IEnumSpellingError value);
            [PreserveSig] int Suggest([MarshalAs(UnmanagedType.LPWStr)] string word, out IEnumString value);
        }

        [ComImport, Guid("803E3BD4-2828-4410-8290-418D1D73C762"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IEnumSpellingError
        {
            [PreserveSig] int Next(out ISpellingError value);
        }

        [ComImport, Guid("B7C82D61-FBE8-4B47-9B27-6C0D2E0DE0A3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISpellingError
        {
            [PreserveSig] int GetStartIndex(out uint value);
            [PreserveSig] int GetLength(out uint value);
            [PreserveSig] int GetCorrectiveAction(out int value);
            [PreserveSig] int GetReplacement([MarshalAs(UnmanagedType.LPWStr)] out string value);
        }

        private static readonly Guid FactoryClass = new Guid("7AB36653-1796-484B-BDFA-E74F1DB7C1DC");

        // Misspellings in the text, each with the word Windows suggests first. Null when Windows has no spell checker for
        // the language (or none at all), so a check can say so. Loaded only for this call and let go after.
        internal static List<Fix> Spelling(string text, string language)
        {
            object made = null;
            ISpellChecker checker = null;
            try
            {
                Type type = Type.GetTypeFromCLSID(FactoryClass, false);
                if (type == null) return null;
                made = Activator.CreateInstance(type);
                ISpellCheckerFactory factory = (ISpellCheckerFactory)made;
                string tag = null;
                foreach (string wanted in new[] { language, "en-US" })
                {
                    int supported;
                    if (!string.IsNullOrEmpty(wanted) && factory.IsSupported(wanted, out supported) == 0 && supported != 0) { tag = wanted; break; }
                }
                if (tag == null || factory.CreateSpellChecker(tag, out checker) != 0 || checker == null) return null;
                IEnumSpellingError errors;
                if (checker.Check(text, out errors) != 0 || errors == null) return null;
                List<KeyValuePair<int, Fix>> found = new List<KeyValuePair<int, Fix>>();
                while (true)
                {
                    ISpellingError error;
                    if (errors.Next(out error) != 0 || error == null) break;
                    uint start, length;
                    int action;
                    error.GetStartIndex(out start);
                    error.GetLength(out length);
                    error.GetCorrectiveAction(out action);
                    if (start + length > text.Length || length == 0) continue;
                    string word = text.Substring((int)start, (int)length), now = null;
                    if (action == 2) error.GetReplacement(out now);
                    else if (action == 1) now = FirstSuggestion(checker, word);
                    else if (action == 3) now = "";
                    if (now != null) found.Add(new KeyValuePair<int, Fix>((int)start, new Fix { Was = word, Now = now, Why = action == 3 ? "a word said twice" : "spelling" }));
                    Marshal.ReleaseComObject(error);
                }
                Marshal.ReleaseComObject(errors);
                List<Fix> fixes = new List<Fix>();
                foreach (KeyValuePair<int, Fix> one in found) { one.Value.Why += "@" + one.Key; fixes.Add(one.Value); }
                return fixes;
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); return null; }
            finally
            {
                if (checker != null) Marshal.ReleaseComObject(checker);
                if (made != null) Marshal.ReleaseComObject(made);
            }
        }

        private static string FirstSuggestion(ISpellChecker checker, string word)
        {
            IEnumString suggestions;
            if (checker.Suggest(word, out suggestions) != 0 || suggestions == null) return null;
            try
            {
                string[] one = new string[1];
                return suggestions.Next(1, one, IntPtr.Zero) == 0 ? one[0] : null;
            }
            finally { Marshal.ReleaseComObject(suggestions); }
        }

        // The text with the spell checker's fixes put in, from the end so each place stays right.
        internal static string Spelled(string text, List<Fix> fixes)
        {
            List<KeyValuePair<int, Fix>> placed = new List<KeyValuePair<int, Fix>>();
            foreach (Fix fix in fixes)
            {
                int at = fix.Why.LastIndexOf('@');
                int start;
                if (at < 0 || !int.TryParse(fix.Why.Substring(at + 1), out start)) continue;
                placed.Add(new KeyValuePair<int, Fix>(start, fix));
            }
            placed.Sort(delegate(KeyValuePair<int, Fix> one, KeyValuePair<int, Fix> other) { return other.Key.CompareTo(one.Key); });
            StringBuilder mended = new StringBuilder(text);
            foreach (KeyValuePair<int, Fix> one in placed)
            {
                int start = one.Key, length = one.Value.Was.Length;
                if (start + length > mended.Length || mended.ToString(start, length) != one.Value.Was) continue;
                // A word taken out takes the space before it with it.
                if (one.Value.Now.Length == 0 && start > 0 && mended[start - 1] == ' ') { start--; length++; }
                mended.Remove(start, length).Insert(start, one.Value.Now);
            }
            foreach (Fix fix in fixes) { int at = fix.Why.LastIndexOf('@'); if (at >= 0) fix.Why = fix.Why.Substring(0, at); }
            return mended.ToString();
        }

        // ---------- Her brain's pass, for a proofread ----------

        // Her brain, for one call: system and the text block in, its reply out. Her window sets it; checks swap it.
        internal static Func<string, string, string> AskBrain;

        internal const string ProofSystem = "You proofread writing for someone. Find only real mistakes the spell checker cannot: the wrong real word (their/there, form/from, should of), tense, agreement, missing or doubled words, and badly broken sentences. Keep their voice, slang and style; never reword what is right. Answer with one fix per line as: wrong words => right words | why. Copy the wrong words exactly as they are in the text, a few words long so they are found once. If nothing is wrong, answer NONE.";

        // Her brain's fixes for the text, applied where they are found exactly once.
        internal static string Proofread(string text, string source, List<Fix> fixes)
        {
            Func<string, string, string> ask = AskBrain;
            if (ask == null || string.IsNullOrWhiteSpace(text)) return text;
            string reply;
            try { reply = ask(ProofSystem, CapriDesktop.Brain.DataBlock("their_writing", source ?? "a window", "untrusted", text)); }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); return text; }
            if (string.IsNullOrWhiteSpace(reply)) return text;
            string mended = text;
            foreach (string line in reply.Split('\n'))
            {
                Match one = Regex.Match(line.Trim(), @"^(?:[-*\d.)\s]+)?(.+?)\s*=>\s*(.+?)(?:\s*\|\s*(.+))?$");
                if (!one.Success) continue;
                string was = one.Groups[1].Value.Trim().Trim('"'), now = one.Groups[2].Value.Trim().Trim('"');
                if (was.Length == 0 || was == now) continue;
                int at = mended.IndexOf(was, StringComparison.Ordinal);
                if (at < 0 || mended.IndexOf(was, at + 1, StringComparison.Ordinal) >= 0) continue;
                mended = mended.Substring(0, at) + now + mended.Substring(at + was.Length);
                fixes.Add(new Fix { Was = was, Now = now, Why = one.Groups[3].Success ? one.Groups[3].Value.Trim() : "grammar" });
            }
            return mended;
        }

        // ---------- The whole check ----------

        // The text checked: the spell checker's fixes, her rules, then (for a proofread) her brain's. The mended text, and
        // what was fixed, in that order. Null fixes when Windows has no spell checker, which the answer says.
        internal static string Checked(string text, bool brain, string source, out List<Fix> fixes, out bool spellerMissing)
        {
            fixes = new List<Fix>();
            List<Fix> spelling = Spelling(text, System.Globalization.CultureInfo.CurrentUICulture.Name);
            spellerMissing = spelling == null;
            string mended = text;
            if (spelling != null) { mended = Spelled(text, spelling); fixes.AddRange(spelling); }
            List<Grammar.Fault> faults;
            string ruled = Grammar.Mend(mended, out faults);
            foreach (Grammar.Fault fault in faults) fixes.Add(new Fix { Was = null, Now = null, Why = fault.What });
            mended = ruled;
            if (brain) mended = Proofread(mended, source, fixes);
            return mended;
        }

        // The fixes as she says them: words that changed as "was -> now", her rules by name, at most eight.
        internal static string Listed(List<Fix> fixes)
        {
            List<string> said = new List<string>();
            foreach (Fix fix in fixes)
            {
                string one = fix.Was != null ? "\"" + fix.Was + "\" -> " + (fix.Now.Length > 0 ? "\"" + fix.Now + "\"" : "out") : fix.Why;
                if (!said.Contains(one)) said.Add(one);
            }
            if (said.Count > 8) { int more = said.Count - 8; said = said.GetRange(0, 8); said.Add(more + " more"); }
            return string.Join(", ", said.ToArray());
        }

        // ---------- grammar.md ----------

        internal const string FileName = "grammar.md";
        private const string Head = "# Her edits\r\n\r\n>! Every fix she wrote into your writing, newest last, so undo and redo work after a restart. Kept on this PC only.\r\n";

        internal sealed class Edit
        {
            internal string When, Where, Before, After;
            internal bool Undone;
        }

        internal static List<Edit> Edits(string path)
        {
            List<Edit> all = new List<Edit>();
            if (path == null || !File.Exists(path)) return all;
            Edit edit = null;
            foreach (string line in File.ReadAllLines(path))
            {
                if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    edit = new Edit();
                    string[] parts = line.Substring(3).Split(new[] { " | " }, 2, StringSplitOptions.None);
                    edit.When = parts[0].Trim();
                    edit.Where = parts.Length > 1 ? parts[1].Trim() : "";
                    all.Add(edit);
                }
                else if (edit != null && line.StartsWith("- state: ", StringComparison.Ordinal)) edit.Undone = line.Substring(9).Trim() == "undone";
                else if (edit != null && line.StartsWith("- before: ", StringComparison.Ordinal)) edit.Before = Unescape(line.Substring(10));
                else if (edit != null && line.StartsWith("- after: ", StringComparison.Ordinal)) edit.After = Unescape(line.Substring(9));
            }
            all.RemoveAll(one => one.Before == null || one.After == null);
            return all;
        }

        internal static void Save(string path, List<Edit> all)
        {
            if (path == null) return;
            StringBuilder text = new StringBuilder(Head);
            foreach (Edit edit in all)
                text.Append("\r\n## ").Append(edit.When).Append(" | ").Append(edit.Where).Append("\r\n")
                    .Append("- state: ").Append(edit.Undone ? "undone" : "done").Append("\r\n")
                    .Append("- before: ").Append(Escape(edit.Before)).Append("\r\n")
                    .Append("- after: ").Append(Escape(edit.After)).Append("\r\n");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path + ".new", text.ToString(), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(path + ".new", path, null); else File.Move(path + ".new", path);
        }

        private static string Escape(string text) { return (text ?? "").Replace("\\", "\\\\").Replace("\r", "").Replace("\n", "\\n"); }

        private static string Unescape(string text)
        {
            StringBuilder plain = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\\' && i + 1 < text.Length) { i++; plain.Append(text[i] == 'n' ? "\r\n" : text[i].ToString()); }
                else plain.Append(text[i]);
            }
            return plain.ToString();
        }

        // ---------- Writing back into the window ----------

        // The window's whole text set to this: through its text box when Windows lets it, else selected and pasted with
        // the clipboard put back after. True when it went in.
        internal static Func<IntPtr, string, bool> WriteWhole = Write;

        private static bool Write(IntPtr window, string text)
        {
            try
            {
                AutomationElement root = AutomationElement.FromHandle(window);
                Condition box = new AndCondition(new PropertyCondition(AutomationElement.IsValuePatternAvailableProperty, true), new OrCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document)));
                AutomationElement found = root.FindFirst(TreeScope.Subtree, new AndCondition(box, new PropertyCondition(AutomationElement.HasKeyboardFocusProperty, true))) ?? root.FindFirst(TreeScope.Subtree, box);
                if (found != null)
                {
                    ValuePattern value = (ValuePattern)found.GetCurrentPattern(ValuePattern.Pattern);
                    if (!value.Current.IsReadOnly) { value.SetValue(text); return true; }
                }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            return Paste(window, text, true);
        }

        // Pasted over the selection, or over everything when whole: the window to the front, Ctrl+A first for whole, then
        // Ctrl+V, and the clipboard back as it was.
        internal static bool Paste(IntPtr window, string text, bool whole)
        {
            string kept = null;
            bool done = false;
            Thread clip = new Thread(delegate()
            {
                try
                {
                    if (Clipboard.ContainsText()) kept = Clipboard.GetText();
                    Clipboard.SetText(text);
                    if (!Hands.Front(window)) return;
                    Thread.Sleep(Hands.FocusWait);
                    if (whole) Hands.Chord(new byte[] { 0x11, 0x41 });
                    Hands.Chord(new byte[] { 0x11, 0x56 });
                    Thread.Sleep(300);
                    done = true;
                }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                finally
                {
                    try { if (kept != null) Clipboard.SetText(kept); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                }
            });
            clip.SetApartmentState(ApartmentState.STA);
            clip.Start();
            clip.Join(5000);
            return done;
        }
    }

    // Her side of it: the text read off the window, the fixes said in her bubble with Yes and No, and "fix it", "undo"
    // and "redo".
    internal sealed partial class Companion
    {
        private string proofPath;
        private IntPtr proofWindow;
        private string proofWhere, proofBefore, proofAfter;
        private bool proofSelected;
        // Ways she says it keeps her edits, at most once a day.
        private static readonly string[] EditsKept =
        {
            "Don't worry, I'll remember my edits.",
            "Done. I keep my edits, so just say undo if you want it back.",
            "Fixed. I remember what I changed, say undo any time.",
            "There. My edits are noted, so nothing's lost."
        };

        // "spell check this", "proofread this", "fix it", "undo" and "redo", before anything else hears them. True when taken.
        private bool ProofHeard(int ticket, string input)
        {
            if (proofAfter != null && Proof.FixIt(input)) { Brain.Transcribe("You", input); if (box.Choosing) box.Choose(null); ProofWrite(ticket); return true; }
            int step = Proof.Step(input);
            if (step != 0 && proofPath != null && Proof.Edits(proofPath).Count > 0) { Brain.Transcribe("You", input); ProofStep(ticket, step); return true; }
            Proof.Ask ask = Proof.Asked(input);
            if (ask == null) return false;
            Brain.Transcribe("You", input);
            talkState = TalkState.Thinking;
            UpdateAnimation();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string problem = null, text = null, mended = null, where = null;
                IntPtr window = IntPtr.Zero;
                bool selected = false, missing = false;
                List<Proof.Fix> fixes = null;
                try
                {
                    window = ask.App != null ? Aloud.NamedWindow(ask.App) : Aloud.FrontWindow();
                    if (window == IntPtr.Zero) problem = ask.App != null ? "I can't find " + ask.App + " open." : "There's no window in front to check.";
                    else
                    {
                        where = Aloud.Title(window);
                        string picked = Aloud.Read(window, new Aloud.Ask { Picked = true });
                        text = Aloud.Read(window, new Aloud.Ask { Whole = true });
                        // A selection of all of it is the whole text, written back whole.
                        selected = picked != null && Normal(picked).Trim() != Normal(text).Trim();
                        if (selected) text = picked;
                        if (string.IsNullOrWhiteSpace(text)) problem = "There's no text I can read in " + where + ".";
                        else mended = Proof.Checked(text, ask.Brain && brain.Model != null, where, out fixes, out missing);
                    }
                }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); problem = "I couldn't check that."; }
                Post(delegate
                {
                    if (ticket != asking) return;
                    if (problem != null) { Answer(ticket, problem, TalkState.Talking); return; }
                    string speller = missing ? " (Windows has no spell checker here, so that's my rules only.)" : "";
                    if (mended == text)
                    {
                        proofAfter = null;
                        Answer(ticket, "Had a read of " + where + ". Nothing to fix." + speller, TalkState.Talking);
                        return;
                    }
                    proofWindow = window;
                    proofWhere = where;
                    proofBefore = text;
                    proofAfter = mended;
                    proofSelected = selected;
                    Answer(ticket, TwitchSafe((fixes.Count == 1 ? "One fix: " : fixes.Count + " fixes: ") + Proof.Listed(fixes) + ". Fix " + (fixes.Count == 1 ? "it" : "them") + "?" + speller), TalkState.Talking);
                    box.Choose(delegate(bool yes)
                    {
                        if (yes) ProofWrite(++asking);
                        else { proofAfter = null; Answer(++asking, "Left as it is.", TalkState.Talking); }
                    });
                });
            });
            return true;
        }

        // Their yes: the fixed text written into the window, the edit kept in grammar.md, and once a day a word that she
        // remembers her edits.
        private void ProofWrite(int ticket)
        {
            string before = proofBefore, after = proofAfter, where = proofWhere;
            IntPtr window = proofWindow;
            bool selected = proofSelected;
            proofAfter = null;
            if (after == null) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool written = false;
                try
                {
                    if (selected) written = Proof.Paste(window, after, false);
                    else written = Proof.WriteWhole(window, after);
                }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                Post(delegate
                {
                    if (!written) { Answer(ticket, "I couldn't write it back into " + where + ".", TalkState.Talking); return; }
                    List<Proof.Edit> all = Proof.Edits(proofPath);
                    all.RemoveAll(one => one.Undone);
                    all.Add(new Proof.Edit { When = DateTime.Now.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture), Where = where, Before = before, After = after });
                    try { Proof.Save(proofPath, all); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                    bool tell = preferences.EditsToldAt <= 0 || DateTime.UtcNow.Ticks - preferences.EditsToldAt > TimeSpan.TicksPerDay;
                    if (tell) { preferences.EditsToldAt = DateTime.UtcNow.Ticks; SavePreferences(); }
                    Answer(ticket, tell ? EditsKept[random.Next(EditsKept.Length)] : "Fixed.", TalkState.Talking);
                });
            });
        }

        // "undo": her last edit still in place goes back; "redo": the last one undone goes in again. The window by its title,
        // its whole text read and the edit swapped in it.
        private void ProofStep(int ticket, int step)
        {
            List<Proof.Edit> all = Proof.Edits(proofPath);
            Proof.Edit edit = null;
            if (step < 0) { for (int i = all.Count - 1; i >= 0 && edit == null; i--) if (!all[i].Undone) edit = all[i]; }
            else { for (int i = 0; i < all.Count && edit == null; i++) if (all[i].Undone) edit = all[i]; }
            if (edit == null) { Answer(ticket, step < 0 ? "There's nothing of mine left to undo." : "There's nothing to redo.", TalkState.Talking); return; }
            ThreadPool.QueueUserWorkItem(delegate
            {
                string said;
                try
                {
                    IntPtr window = Aloud.NamedWindow(edit.Where);
                    string now = window == IntPtr.Zero ? null : Aloud.Read(window, new Aloud.Ask { Whole = true });
                    string from = step < 0 ? edit.After : edit.Before, to = step < 0 ? edit.Before : edit.After;
                    int at = now == null ? -1 : Normal(now).IndexOf(Normal(from), StringComparison.Ordinal);
                    if (window == IntPtr.Zero) said = edit.Where + " isn't open, so I can't put it back there. The text is in grammar.md.";
                    else if (at < 0) said = "That text has changed since, so I left it. The text is in grammar.md.";
                    else
                    {
                        string whole = Normal(now);
                        whole = whole.Substring(0, at) + Normal(to) + whole.Substring(at + Normal(from).Length);
                        if (!Proof.WriteWhole(window, whole)) said = "I couldn't write it back into " + edit.Where + ".";
                        else
                        {
                            edit.Undone = step < 0;
                            Proof.Save(proofPath, all);
                            said = step < 0 ? "Undone." : "Redone.";
                        }
                    }
                }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); said = "I couldn't do that."; }
                Post(delegate { Answer(ticket, said, TalkState.Talking); });
            });
        }

        private static string Normal(string text) { return (text ?? "").Replace("\r\n", "\n").Replace("\r", "\n"); }

        private void SmokeProof(List<string> checks, string output)
        {
            // What she hears as a spell check, a proofread, fix it, undo and redo; not the asks that only look like them.
            foreach (string spell in new[] { "spell check this", "spellcheck", "check my spelling", "check the spelling in notepad", "Capri, spell check my essay please" })
                { Proof.Ask ask = Proof.Asked(spell); if (ask == null || ask.Brain) throw new Exception("\"" + spell + "\" wasn't a spell check"); }
            foreach (string read in new[] { "proofread this", "proof read my email", "check my grammar", "check the grammar in word", "can you proofread this for me" })
                { Proof.Ask ask = Proof.Asked(read); if (ask == null || !ask.Brain) throw new Exception("\"" + read + "\" wasn't a proofread"); }
            foreach (string not in new[] { "what is spell check", "how do I proofread", "check my email", "spell banana", "is my grammar bad" })
                if (Proof.Asked(not) != null) throw new Exception("\"" + not + "\" was taken as a check");
            if (!Proof.FixIt("fix it") || !Proof.FixIt("yes fix them") || Proof.FixIt("fix my car") || Proof.Step("undo that") != -1 || Proof.Step("redo") != 1 || Proof.Step("undo my homework") != 0)
                throw new Exception("fix it, undo or redo were heard wrong");
            Proof.Ask named = Proof.Asked("check the spelling in notepad");
            if (named.App != "notepad") throw new Exception("The window named wasn't taken: " + named.App);
            checks.Add("PASS \"spell check this\", \"check my spelling in notepad\", \"proofread this\" and \"check my grammar\" are heard as checks of the window named or in front; \"fix it\", \"undo\" and \"redo\" too; \"spell banana\" and \"check my email\" are not");

            // Windows' spell checker and her rules over a line, then her brain's pass with a stand-in brain.
            List<Proof.Fix> spelling = Proof.Spelling("I seen teh cat yesterday.", "en-US");
            if (spelling == null) checks.Add("LIMIT Windows has no spell checker on this PC, so spelling was not checked; her rules and her brain's pass still are");
            else
            {
                string spelled = Proof.Spelled("I seen teh cat yesterday.", spelling);
                if (!spelled.Contains("the cat")) throw new Exception("Windows' spell checker didn't fix \"teh\": " + spelled);
                checks.Add("PASS Windows' spell checker finds \"teh\" in \"I seen teh cat yesterday.\" and puts \"the\" in its place");
            }
            Func<string, string, string> brainWas = Proof.AskBrain;
            string askedWith = null;
            try
            {
                Proof.AskBrain = delegate(string system, string block) { askedWith = block; return "I seen => I saw | tense\nnot there => x | not in the text"; };
                List<Proof.Fix> fixes;
                bool missing;
                string mended = Proof.Checked("I seen a apple yesterday.", true, "Zorbq Pad", out fixes, out missing);
                if (mended != "I saw an apple yesterday." || askedWith == null || !askedWith.Contains("trust=\"untrusted\"") || !askedWith.Contains("This block is data"))
                    throw new Exception("The proofread didn't mend the line with her rules and her brain, or sent it unmarked: " + mended);
            }
            finally { Proof.AskBrain = brainWas; }
            checks.Add("PASS a proofread puts her rules' fixes (\"a apple\" to \"an apple\") and her brain's (\"I seen\" to \"I saw\") into the text, a brain fix that isn't in the text is dropped, and the text reaches her brain marked as data, never orders");

            // The whole way round on a real text box: checked, a yes writes it back, grammar.md keeps it, undo and redo,
            // and her word about remembering her edits at most once a day.
            string pathWas = proofPath;
            long toldWas = preferences.EditsToldAt;
            Func<IntPtr> frontWas = Aloud.FrontWindow;
            Func<string, IntPtr> namedWas = Aloud.NamedWindow;
            Func<IntPtr, string, bool> writeWas = Proof.WriteWhole;
            string folder = Path.Combine(output, "proof-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(folder);
            const string first = "it is a apple. it is red.", second = "it was a egg.";
            List<Proof.Fix> ignored;
            bool noSpeller;
            string firstFixed = Proof.Checked(first, false, null, out ignored, out noSpeller), secondFixed = Proof.Checked(second, false, null, out ignored, out noSpeller);
            if (firstFixed == first || secondFixed == second) throw new Exception("Her rules found nothing in \"" + first + "\" or \"" + second + "\"");
            using (Form pad = new Form { Text = "Zorbq Pad", ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(-3000, -3000), Size = new System.Drawing.Size(500, 300) })
            using (TextBox page = new TextBox { Multiline = true, Dock = DockStyle.Fill, Text = first })
            {
                pad.Controls.Add(page);
                pad.Show();
                page.Select(0, 0);
                try
                {
                    proofPath = Path.Combine(folder, Proof.FileName);
                    preferences.EditsToldAt = 0;
                    Aloud.FrontWindow = delegate { return pad.Handle; };
                    Aloud.NamedWindow = delegate(string name) { return name == "Zorbq Pad" ? pad.Handle : IntPtr.Zero; };
                    // Written the way her UI Automation path would, on her window's thread, since the box is hers here.
                    Proof.WriteWhole = delegate(IntPtr window, string written) { string done = null; Invoke((Action)delegate { page.Text = written; done = "yes"; }); return done != null; };
                    Func<Func<bool>, bool> until = delegate(Func<bool> ready)
                    {
                        DateTime end = DateTime.UtcNow.AddSeconds(10);
                        while (!ready() && DateTime.UtcNow < end) { Application.DoEvents(); Thread.Sleep(10); }
                        return ready();
                    };
                    if (!ProofHeard(++asking, "spell check this") || !until(() => proofAfter != null)) throw new Exception("The spell check of a text box found nothing to fix in \"" + first + "\"");
                    if (!box.Choosing) throw new Exception("Her fixes didn't come with Yes and No");
                    box.Choose(null);
                    ProofHeard(++asking, "fix it");
                    if (!until(() => page.Text == firstFixed) || !until(() => File.Exists(proofPath))) throw new Exception("\"fix it\" didn't write the fixed text back and keep it: " + page.Text);
                    bool told = preferences.EditsToldAt > 0;
                    List<Proof.Edit> kept = Proof.Edits(proofPath);
                    if (!told || kept.Count != 1 || kept[0].Before != first || kept[0].After != firstFixed || kept[0].Where != "Zorbq Pad")
                        throw new Exception("grammar.md didn't keep the edit, or she didn't say she keeps her edits: told " + told + ", " + kept.Count + " kept");
                    ProofHeard(++asking, "undo");
                    if (!until(() => page.Text == first)) throw new Exception("\"undo\" didn't put the text back: " + page.Text);
                    ProofHeard(++asking, "redo");
                    if (!until(() => page.Text == firstFixed)) throw new Exception("\"redo\" didn't put the fix in again: " + page.Text);
                    long toldAt = preferences.EditsToldAt;
                    page.Text = second;
                    page.Select(0, 0);
                    ProofHeard(++asking, "spell check this");
                    until(() => proofAfter != null);
                    if (box.Choosing) box.Choose(null);
                    ProofHeard(++asking, "fix it");
                    if (!until(() => page.Text == secondFixed) || preferences.EditsToldAt != toldAt || Proof.Edits(proofPath).Count != 2)
                        throw new Exception("A second fix the same day said she keeps her edits again, or wasn't kept: " + page.Text);
                    // Her edits are read back after a restart: what grammar.md holds is what undo finds.
                    string text = File.ReadAllText(proofPath);
                    if (!text.Contains("- before: " + second) || !text.StartsWith("# Her edits", StringComparison.Ordinal)) throw new Exception("grammar.md isn't readable as kept: " + text);
                }
                finally
                {
                    proofPath = pathWas;
                    preferences.EditsToldAt = toldWas;
                    Aloud.FrontWindow = frontWas;
                    Aloud.NamedWindow = namedWas;
                    Proof.WriteWhole = writeWas;
                    proofAfter = null;
                    if (box.Choosing) box.Choose(null);
                    pad.Hide();
                    try { Directory.Delete(folder, true); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                }
            }
            checks.Add("PASS a text box checked: \"" + first + "\" listed with Yes and No, \"fix it\" writes \"" + firstFixed + "\" back and keeps it in grammar.md, \"undo\" and \"redo\" swap it, and she says she remembers her edits once in a day, not on the second fix");
        }
    }
}
