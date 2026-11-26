#if CHECKS
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace CapriDesktop
{
    // The MD editor pictured on every tab at 100 and 150%, and every control's font listed by family, size and style, a font
    // already let go marked: compared with ci/editor-fonts.txt, the fonts it shipped with.
    internal sealed partial class Companion
    {
        internal void SmokeEditorFonts(List<string> checks, string output)
        {
            string folder = Path.Combine(output, "editor-fonts");
            Directory.CreateDirectory(folder);
            List<string> listed = new List<string>();
            try
            {
                foreach (int percent in new[] { 100, 150 })
                {
                    mainframe.LayoutScale(96 * percent / 100, 100);
                    HashSet<Form> had = new HashSet<Form>();
                    foreach (Form one in Application.OpenForms) had.Add(one);
                    OpenFiles("me.md");
                    for (int i = 0; i < 40; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                    Form opened = null;
                    foreach (Form one in Application.OpenForms) if (!had.Contains(one) && one.Visible) opened = one;
                    if (opened == null) throw new Exception("The MD editor opened no window at " + percent + "%.");
                    int tabs = fileEditor == null ? 0 : fileEditor.Choices.Length;
                    for (int tab = 0; tab < Math.Max(1, tabs); tab++)
                    {
                        if (tabs > 0) fileEditor.Turn(tab);
                        for (int i = 0; i < 10; i++) Application.DoEvents();
                        Snap(opened, Path.Combine(folder, "editor-" + percent + "-tab" + (tab + 1).ToString("00") + ".png"));
                    }
                    // Its frame and its page are windows of their own: every one it opened.
                    foreach (Form one in Application.OpenForms) if (!had.Contains(one)) FontsOf(one, percent + "%/" + one.GetType().Name, listed);
                    // Laid out again, as a file set switched, a screen's scale or the tab column's wheel does: the same fonts.
                    foreach (string key in new[] { "Personality files", "Main files" })
                    {
                        foreach (Form one in Application.OpenForms) if (!had.Contains(one)) { Button found = KeyIn(one, key); if (found != null) found.PerformClick(); }
                        for (int i = 0; i < 20; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(5); }
                    }
                    Snap(opened, Path.Combine(folder, "editor-" + percent + "-again.png"));
                    foreach (Form one in Application.OpenForms) if (!had.Contains(one)) FontsOf(one, percent + "% again/" + one.GetType().Name, listed);
                    opened.Close();
                    FileEditor panel = fileEditor;
                    if (panel != null && panel.Guarding) panel.GuardDiscard();
                    System.Diagnostics.Stopwatch waited = System.Diagnostics.Stopwatch.StartNew();
                    while (panel != null && !panel.Over && waited.ElapsedMilliseconds < 8000) { Application.DoEvents(); System.Threading.Thread.Sleep(5); }
                }
            }
            finally { mainframe.LayoutScale(0, 0); }
            File.WriteAllLines(Path.Combine(folder, "editor-fonts.txt"), listed.ToArray());
            // Every control's font as it shipped (ci/editor-fonts.txt), on its first layout and again after another.
            string shipped = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "..", "..", "ci", "editor-fonts.txt"));
            List<string> wrong = new List<string>();
            foreach (string line in listed) if (line.EndsWith("| let go", StringComparison.Ordinal)) wrong.Add(line);
            if (File.Exists(shipped))
            {
                List<string> wanted = new List<string>(File.ReadAllLines(shipped));
                foreach (string line in listed)
                {
                    string first = line.Replace("% again/", "%/");
                    if (!wanted.Contains(first)) wrong.Add("not as shipped: " + line);
                }
            }
            if (wrong.Count > 0) throw new Exception("The MD editor's fonts aren't the ones it shipped with: " + string.Join("; ", wrong.GetRange(0, Math.Min(8, wrong.Count)).ToArray()) + (wrong.Count > 8 ? " and " + (wrong.Count - 8) + " more" : ""));
            checks.Add("PASS the MD editor's fonts on every tab at 100 and 150%, laid out once and again, are the ones it shipped with, none let go (" + listed.Count + " controls; pictures in " + folder + ")");
        }

        private static Button KeyIn(Control under, string words)
        {
            foreach (Control one in under.Controls)
            {
                Button key = one as Button;
                if (key != null && key.Text == words && key.Visible && key.Enabled) return key;
                Button inner = KeyIn(one, words);
                if (inner != null) return inner;
            }
            return null;
        }

        // Each control with words as scale/type: words | family size style, or "let go" for a font already freed.
        private static void FontsOf(Control under, string path, List<string> listed)
        {
            foreach (Control one in under.Controls)
            {
                string font;
                try { one.Font.GetHeight(); font = one.Font.Name + " " + Math.Round(one.Font.Size, 1) + " " + one.Font.Style; }
                catch (ArgumentException) { font = "let go"; }
                string words = (one is TextBoxBase ? "" : one.Text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
                listed.Add(path + "/" + one.GetType().Name + ": " + (words.Length > 40 ? words.Substring(0, 40) : words) + " | " + font);
                FontsOf(one, path + "/" + one.GetType().Name, listed);
            }
        }
    }
}
#endif
