using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Her menus, buttons and labels in the picked language: built-in lines plus languages\gui-<code>.txt over them,
    // English, a tab, then the translation. Anything missing goes to her brain and shows in English until it's back.
    internal static class Gui
    {
        private static readonly object gate = new object();
        private static readonly Dictionary<string, Dictionary<string, string>> tables = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        internal static string T(string english)
        {
            string language = Brain.Language;
            if (language == null || string.IsNullOrWhiteSpace(english)) return english;
            string found;
            Dictionary<string, string> table = Table(TableCode(language));
            if (table.TryGetValue(english, out found) && found.Length > 0) return found;
            found = FromCodex(english, TableCode(language));
            if (found != null) return found;
            string shaped = Shaped(english, table, language);
            if (shaped != null) return shaped;
            try { return Said.Learn(english) ?? english; }
            catch (Exception) { return english; }
        }

        // A label's plain form, decoration off: " >" after a submenu, "..." after a key, and numbers as a {n} slot, so
        // "MODS >" reads the row for "Mods" and "90 min" the row for "{n} min". One row serves every way it shows.
        internal static string Core(string english)
        {
            string core = (english ?? "").Trim();
            core = Regex.Replace(core, @"(\s*>|\s*\.\.\.|\s*…)$", "");
            core = Regex.Replace(core, @"\d+(?:[.,]\d+)?", "{n}");
            return core.Trim();
        }

        // Each table read again without minding case, made the first time a label needs it.
        private static readonly Dictionary<Dictionary<string, string>, Dictionary<string, string>> folded = new Dictionary<Dictionary<string, string>, Dictionary<string, string>>();

        private static readonly Regex Decoration = new Regex(@"(\s*>|\s*\.\.\.|\s*…)$");

        // A label with no row of its own, read through its plain form: the decoration put back after, numbers back
        // in their slots in order, and a label shown in capitals given in capitals. Null when the plain form has no
        // row either.
        private static string Shaped(string english, Dictionary<string, string> table, string language)
        {
            string trimmed = english.Trim();
            Match tail = Decoration.Match(trimmed);
            string after = tail.Success ? tail.Value : "";
            string core = Core(trimmed);
            List<string> numbers = new List<string>();
            foreach (Match number in Regex.Matches(tail.Success ? trimmed.Substring(0, tail.Index) : trimmed, @"\d+(?:[.,]\d+)?")) numbers.Add(number.Value);
            string found = null;
            if (!table.TryGetValue(core, out found) || found.Length == 0)
            {
                found = null;
                Dictionary<string, string> anyCase;
                lock (gate)
                {
                    if (!folded.TryGetValue(table, out anyCase))
                    {
                        anyCase = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (KeyValuePair<string, string> row in table) if (row.Value.Length > 0 && !anyCase.ContainsKey(row.Key)) anyCase[row.Key] = row.Value;
                        folded[table] = anyCase;
                    }
                }
                anyCase.TryGetValue(core, out found);
            }
            if (found == null || (core == trimmed && after.Length == 0 && found == english)) return null;
            int at = 0;
            string filled = Regex.Replace(found, @"\{n\}", delegate(Match slot) { return at < numbers.Count ? numbers[at++] : slot.Value; });
            bool capitals = Regex.IsMatch(core, @"\p{L}") && core == core.ToUpperInvariant() && core != core.ToLowerInvariant();
            if (capitals)
            {
                System.Globalization.CultureInfo culture = System.Globalization.CultureInfo.InvariantCulture;
                try { culture = new System.Globalization.CultureInfo(TableCode(language)); } catch (Exception) { }
                filled = filled.ToUpper(culture);
            }
            return filled + after;
        }

        // The table a language reads. Brazilian Portuguese shares pt with Portugal for her voice and speech, but its
        // menus are written its own way, so it has a table of its own.
        internal static string TableCode(string language)
        {
            return language == "Brazilian Portuguese" ? "pt-BR" : Brain.LanguageCode(language);
        }

        // Labels from the codex, one table a language, English, a tab, then the translation. Read only while the switch
        // is live and never written to her tables or folder, so they exist only where the codex is.
        internal static Dictionary<string, string> CodexTables;
        private static readonly Dictionary<string, Dictionary<string, string>> codexRead = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        private static string FromCodex(string english, string code)
        {
            Dictionary<string, string> tables = CodexTables;
            if (tables == null || !Brain.SenpaiLive) return null;
            Dictionary<string, string> table;
            lock (gate)
            {
                if (!codexRead.TryGetValue(code ?? "", out table))
                {
                    table = new Dictionary<string, string>(StringComparer.Ordinal);
                    string text;
                    if (tables.TryGetValue(code ?? "", out text)) Fill(table, text);
                    codexRead[code ?? ""] = table;
                }
            }
            string found;
            return table.TryGetValue(english, out found) && found.Length > 0 ? found : null;
        }

        // Read again next time, for a file someone has just edited.
        internal static void Forget() { lock (gate) { tables.Clear(); folded.Clear(); codexRead.Clear(); } }

        private static Dictionary<string, string> Table(string code)
        {
            lock (gate)
            {
                Dictionary<string, string> table;
                if (tables.TryGetValue(code ?? "", out table)) return table;
                table = new Dictionary<string, string>(StringComparer.Ordinal);
                try
                {
                    using (Stream built = typeof(Gui).Assembly.GetManifestResourceStream("Capri.Gui." + code + ".txt"))
                        if (built != null) using (StreamReader read = new StreamReader(built, Encoding.UTF8)) Fill(table, read.ReadToEnd());
                }
                catch (Exception caughtOne) { Brain.Caught(caughtOne); }
                try
                {
                    string theirs = string.IsNullOrEmpty(Said.Folder) ? null : Path.Combine(Said.Folder, "languages", "gui-" + code + ".txt");
                    if (theirs != null && File.Exists(theirs)) Fill(table, File.ReadAllText(theirs, Encoding.UTF8));
                }
                catch (Exception caughtOne) { Brain.Caught(caughtOne); }
                tables[code ?? ""] = table;
                return table;
            }
        }

        private static void Fill(Dictionary<string, string> table, string text)
        {
            foreach (string row in text.Replace("\r\n", "\n").Split('\n'))
            {
                int tab = row.IndexOf('\t');
                if (tab <= 0 || row.StartsWith("#", StringComparison.Ordinal)) continue;
                table[row.Substring(0, tab)] = row.Substring(tab + 1).Trim();
            }
        }

        // ---------- Menus ----------
        // Translated on each open, since some items change their own text. The English each item last showed is kept,
        // so code-set text is read as new and nothing is translated twice.

        private sealed class Seen
        {
            internal string English, Shown, TipEnglish, TipShown, Language;
            internal bool Hooked;
            internal Action Apply;
        }

        private static readonly ConditionalWeakTable<ToolStripItem, Seen> seen = new ConditionalWeakTable<ToolStripItem, Seen>();

        // ---------- Windows ----------
        // Every label, check box and button in a window, translated once and again when the code sets its text; text
        // boxes and her drawn keys are left alone. Safe to call often; a kept window follows a language change.

        private static readonly ConditionalWeakTable<Control, Seen> seenControls = new ConditionalWeakTable<Control, Seen>();

        private static string windowsIn;

        // Every window she has made, hidden kept ones too, while a language is picked, and once more after it is
        // cleared so the English comes back.
        internal static void Windows()
        {
            string language = Brain.Language;
            if (language == null && windowsIn == null) return;
            windowsIn = language;
            List<Form> open = new List<Form>();
            foreach (Form one in Application.OpenForms) open.Add(one);
            foreach (Form one in open) Window(one);
        }

        internal static void Window(Control root)
        {
            if (root == null || root.IsDisposed) return;
            foreach (Control child in root.Controls) Window(child);
            if (root is TextBoxBase || root is ComboBox || root is Form || root.GetType().Name == "Key") return;
            Seen was;
            if (seenControls.TryGetValue(root, out was))
            {
                if (was.Apply != null && was.Language != Brain.Language) was.Apply();
                return;
            }
            was = seenControls.GetOrCreateValue(root);
            was.Hooked = true;
            Control control = root;
            Action apply = delegate
            {
                if (control.IsDisposed || string.IsNullOrEmpty(control.Text)) return;
                string language = Brain.Language;
                if (control.Text == was.Shown && language == was.Language) return;
                // Text that is not the last one shown was set by the code: it is the new English.
                if (control.Text != was.Shown) was.English = control.Text;
                was.Language = language;
                was.Shown = T(was.English);
                if (was.Shown != control.Text) control.Text = was.Shown;
            };
            was.Apply = apply;
            control.TextChanged += delegate { apply(); };
            control.ControlAdded += delegate(object sender, ControlEventArgs added) { Window(added.Control); };
            apply();
        }

        internal static void Translate(ToolStripItemCollection items)
        {
            if (items == null) return;
            foreach (ToolStripItem item in items)
            {
                if (item == null || item is ToolStripSeparator) continue;
                Seen was = seen.GetOrCreateValue(item);
                if (item.Text != was.Shown) was.English = item.Text;
                was.Shown = T(was.English);
                if (item.Text != was.Shown) item.Text = was.Shown;
                if (!string.IsNullOrEmpty(item.ToolTipText) && item.ToolTipText != was.TipShown)
                {
                    was.TipEnglish = item.ToolTipText;
                    was.TipShown = T(was.TipEnglish);
                    item.ToolTipText = was.TipShown;
                }
                ToolStripMenuItem holder = item as ToolStripMenuItem;
                if (holder != null && !was.Hooked)
                {
                    was.Hooked = true;
                    holder.DropDownOpening += delegate { Translate(holder.DropDownItems); };
                }
            }
        }
    }

    internal sealed partial class Companion
    {
        // Her GUI in another language: a menu read in French says the French built in for it, a label no file has
        // stays English, the English comes back when the language is cleared, and a key draws without failing.
        internal void CheckGuiTranslation(List<string> checks)
        {
            string was = Brain.Language;
            try
            {
                // Brazil gets its own menus while voice and speech still take Portuguese as pt.
                if (Brain.SystemLanguage(new System.Globalization.CultureInfo("pt-BR")) != "Brazilian Portuguese"
                    || Brain.SystemLanguage(new System.Globalization.CultureInfo("pt-PT")) != "Portuguese"
                    || Brain.LanguageCode("Brazilian Portuguese") != "pt" || Gui.TableCode("Brazilian Portuguese") != "pt-BR" || Gui.TableCode("Portuguese") != "pt")
                    throw new Exception("Brazilian Portuguese is not told apart from Portugal's for her menus, or its voice code moved.");
                // The maker never collects a value of this PC as a label: the tables ship in her exe and public source.
                string[] pcValues = { @"D:\Users\Someone\Videos\Capri Clips", "Input: Voice Chat Mic (Some Mixer)", "Ollama · gemma4:e4b-it-qat", "gemma4:e4b-it-qat", "CAPRI  /  0.9.426", "Folder of " + Environment.UserName };
                string[] labels = { "Mic: default", "Output: none yet", "Refresh", "Saved providers", "Mainframe: Ctrl + Alt + M (taken)" };
                foreach (string value in pcValues) if (!ThisPc(value)) throw new Exception("The GUI maker would collect a value of this PC as a label: " + value);
                foreach (string label in labels) if (ThisPc(label)) throw new Exception("The GUI maker would skip a real label: " + label);
                string tables = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath) ?? "", "..", "CapriSourceFiles", "languages");
                if (Directory.Exists(tables))
                    foreach (string file in Directory.GetFiles(tables, "gui-*.txt"))
                        foreach (string row in File.ReadAllLines(file, System.Text.Encoding.UTF8))
                        {
                            int tab = row.IndexOf('\t');
                            if (tab > 0 && !row.StartsWith("#", StringComparison.Ordinal) && ThisPc(row.Substring(0, tab)))
                                throw new Exception(Path.GetFileName(file) + " holds a value of this PC: " + row.Substring(0, tab));
                        }
                checks.Add("PASS the GUI maker never collects a folder, a device, a model, her version or the PC's names as a label, and no table holds one");
                Gui.Forget();
                Brain.Language = "French";
                if (Gui.T("Hide Capri") == "Hide Capri")
                {
                    checks.Add("LIMIT GUI translation: this build carries no French file, so there was nothing to read");
                    return;
                }
                using (ContextMenuStrip test = new ContextMenuStrip())
                {
                    ToolStripMenuItem hide = new ToolStripMenuItem("Hide Capri"), none = new ToolStripMenuItem("Zz no file has this zz");
                    test.Items.Add(hide);
                    test.Items.Add(none);
                    Gui.Translate(test.Items);
                    if (hide.Text == "Hide Capri") throw new Exception("A menu item in French stayed in English.");
                    if (none.Text != "Zz no file has this zz") throw new Exception("A label no file has changed: " + none.Text);
                    string french = hide.Text;
                    Gui.Translate(test.Items);
                    if (hide.Text != french) throw new Exception("A translated item was translated again: " + hide.Text);
                    Brain.Language = null;
                    Gui.Translate(test.Items);
                    if (hide.Text != "Hide Capri") throw new Exception("Clearing the language left the item as " + hide.Text);
                }
                // A label in a window, and a label whose code sets new English later.
                Brain.Language = "French";
                using (Panel window = new Panel())
                {
                    Label label = new Label { Text = "Hide Capri" };
                    window.Controls.Add(label);
                    Gui.Window(window);
                    if (label.Text == "Hide Capri") throw new Exception("A label in a window stayed in English.");
                    label.Text = "Zz no file has this zz";
                    if (label.Text != "Zz no file has this zz") throw new Exception("New English a label was given changed: " + label.Text);
                    label.Text = "Hide Capri";
                    if (label.Text == "Hide Capri") throw new Exception("New English a label was given was not translated.");
                }
                checks.Add("PASS GUI translation: a menu reads her built-in French, a label no file has stays English, nothing is translated twice, clearing the language brings the English back, and a window's labels follow, new text included");
                // One row for every way a label is shown: decoration put back, capitals made from it, numbers in
                // their slots.
                if (Gui.Core("MODS >") != "MODS" || Gui.Core("Play...") != "Play" || Gui.Core("90 min") != "{n} min" || Gui.Core("Double (2x)") != "Double ({n}x)")
                    throw new Exception("A label's plain form is wrong: " + Gui.Core("MODS >") + " / " + Gui.Core("Play...") + " / " + Gui.Core("90 min") + " / " + Gui.Core("Double (2x)"));
                string plainHide = Gui.T("Hide Capri");
                string dotted = Gui.T("Hide Capri..."), loud = Gui.T("HIDE CAPRI >");
                if (dotted != plainHide + "..." || loud != plainHide.ToUpper(new System.Globalization.CultureInfo("fr")) + " >")
                    throw new Exception("A decorated label did not read its plain row: " + dotted + " / " + loud);
                string theirs = string.IsNullOrEmpty(Said.Folder) ? null : Path.Combine(Said.Folder, "languages");
                if (theirs != null)
                {
                    Directory.CreateDirectory(theirs);
                    string slotFile = Path.Combine(theirs, "gui-fr.txt");
                    string slotWas = File.Exists(slotFile) ? File.ReadAllText(slotFile) : null;
                    try
                    {
                        File.WriteAllText(slotFile, "Zz {n} slots and {n} more\tZz {n} cases et {n} de plus\n", new UTF8Encoding(false));
                        Gui.Forget();
                        string slotted = Gui.T("Zz 3 slots and 12 more");
                        if (slotted != "Zz 3 cases et 12 de plus") throw new Exception("Numbers did not go back in their slots: " + slotted);
                    }
                    finally
                    {
                        if (slotWas == null) File.Delete(slotFile); else File.WriteAllText(slotFile, slotWas, new UTF8Encoding(false));
                        Gui.Forget();
                    }
                }
                // A label from the codex's tables reads only while the switch is live: off, it stays English.
                Dictionary<string, string> codexWas = Gui.CodexTables;
                bool switchWas = Brain.SenpaiOn, airWas = Brain.OnAir;
                try
                {
                    Gui.CodexTables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "fr", "Zz codex only label\tZz étiquette du codex\n" } };
                    Gui.Forget();
                    Brain.SenpaiOn = false;
                    string off = Gui.T("Zz codex only label");
                    Brain.SenpaiOn = true;
                    Brain.OnAir = false;
                    string on = Gui.T("Zz codex only label");
                    if (off != "Zz codex only label" || on != "Zz étiquette du codex")
                        throw new Exception("A codex label read wrong: with the switch off " + off + ", with it live " + on);
                }
                finally
                {
                    Gui.CodexTables = codexWas;
                    Brain.SenpaiOn = switchWas;
                    Brain.OnAir = airWas;
                    Gui.Forget();
                }
                checks.Add("PASS a label from the codex's own tables shows in their language only while the switch is live, and stays English with it off");
                // Sealed, a table is in the codex and nowhere readable in it; opened, it is back whole.
                byte[] sealedPack = Codex.Sealed(new Dictionary<string, string> { { "gui/fr", "Zz codex only label\tZz étiquette du codex\n" } });
                Dictionary<string, string> opened = Codex.Unsealed(sealedPack);
                bool plain = Encoding.UTF8.GetString(sealedPack).Contains("étiquette") || Encoding.Unicode.GetString(sealedPack).Contains("étiquette");
                if (opened == null || !opened.ContainsKey("gui/fr") || !opened["gui/fr"].Contains("Zz étiquette du codex") || plain)
                    throw new Exception("A label table did not survive the codex's seal, or could be read inside it: opened " + (opened != null) + ", readable " + plain);
                checks.Add("PASS a label table sealed into the codex comes back whole when opened and cannot be read in the sealed file");
                // A language from the adapted tables reads its own row too.
                Brain.Language = "German";
                Gui.Forget();
                if (Gui.T("Hide Capri") == "Hide Capri") throw new Exception("German, from the adapted tables, did not read its row.");
                Brain.Language = "French";
                Gui.Forget();
                checks.Add("PASS one row serves every way a label is shown: \"...\" and \" >\" put back after, capitals made from the plain row, and numbers back in their {n} slots");
            }
            finally
            {
                Brain.Language = was;
                Gui.Forget();
            }
        }
    }
}
