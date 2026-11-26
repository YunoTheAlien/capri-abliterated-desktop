#if CHECKS
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Pictures of what a change to her panels touches (Toggles with every fold open, each lit key's side, Panels) and
    // every control in her open windows listed, to compare with an older build's run.
    internal sealed partial class Companion
    {
        internal void SmokeAreaShots(List<string> checks, string output)
        {
            string folder = Path.Combine(output, "areas");
            Directory.CreateDirectory(folder);
            bool shownWas = mainframe.Visible;
            FormWindowState stateWas = mainframe.WindowState;
            int pictures;
            List<string> listed = new List<string>();
            try
            {
                mainframe.Restore();
                mainframe.ShowSettings(true);
                Application.DoEvents();
                pictures = mainframe.AreaShots(folder, listed);
                foreach (Form open in Application.OpenForms) Census(open, open.GetType().Name, listed);
            }
            finally
            {
                mainframe.CloseToggles();
                mainframe.ShowSettings(false);
                if (mainframe.WindowState != stateWas) mainframe.WindowState = stateWas;
                if (!shownWas && mainframe.Visible) mainframe.Hide();
            }
            File.WriteAllLines(Path.Combine(folder, "controls.txt"), listed.ToArray());
            checks.Add("PASS area shots: " + pictures + " pictures and " + listed.Count + " controls listed in " + folder);
        }

        // Each control as window/parent/type: words, so two runs' lists can be compared line by line.
        private static void Census(Control control, string path, List<string> listed)
        {
            foreach (Control one in control.Controls)
            {
                string words = !string.IsNullOrEmpty(one.Text) ? one.Text : one.AccessibleName ?? "";
                if (!string.IsNullOrEmpty(one.Text) && !string.IsNullOrEmpty(one.AccessibleName) && one.AccessibleName != one.Text) words += " [" + one.AccessibleName + "]";
                listed.Add(path + "/" + one.GetType().Name + ": " + words.Replace("\r", " ").Replace("\n", " ") + (one.Visible ? "" : " (hidden)"));
                Census(one, path + "/" + one.GetType().Name, listed);
            }
        }
    }

    internal sealed partial class Mainframe
    {
        // Toggles with every fold open, page by page; then each lit key's side, page by page; then Panels.
        internal int AreaShots(string folder, List<string> listed)
        {
            OpenToggles();
            toggleSheet.UnfoldAll();
            Application.DoEvents();
            int made = toggleSheet.Pages(Path.Combine(folder, "toggles"));
            List<string> keys = toggleSheet.LitKeys();
            for (int i = 0; i < keys.Count; i++)
            {
                if (!toggleSheet.PressKey(keys[i])) continue;
                Application.DoEvents();
                made += toggleSheet.Pages(Path.Combine(folder, "side-" + (i + 1).ToString("00") + "-" + ShotName(keys[i])));
                listed.Add("side " + (i + 1) + ": " + keys[i]);
            }
            PicturePanels(Path.Combine(folder, "panels.png"));
            return made + 1;
        }

        private static string ShotName(string words)
        {
            char[] kept = (words ?? "").ToCharArray();
            for (int i = 0; i < kept.Length; i++) if (!char.IsLetterOrDigit(kept[i])) kept[i] = '-';
            string plain = new string(kept);
            return plain.Length > 40 ? plain.Substring(0, 40) : plain;
        }

        private sealed partial class ToggleSheet
        {
            // The window pictured at each scroll of its list, top to bottom.
            internal int Pages(string stem)
            {
                int pages = 0, step = Math.Max(40, list.ClientSize.Height - 20), most = Math.Max(0, list.DisplayRectangle.Height - list.ClientSize.Height);
                for (int y = 0; ; y += step)
                {
                    list.AutoScrollPosition = new Point(0, Math.Min(y, most));
                    Application.DoEvents();
                    using (Bitmap shot = new Bitmap(Width, Height))
                    {
                        DrawToBitmap(shot, new Rectangle(Point.Empty, shot.Size));
                        shot.Save(stem + "-" + (++pages).ToString("00") + ".png", System.Drawing.Imaging.ImageFormat.Png);
                    }
                    if (y >= most) break;
                }
                list.AutoScrollPosition = Point.Empty;
                return pages;
            }

            // Every lit key in the list by its words, top to bottom.
            internal List<string> LitKeys()
            {
                List<Control> parts = new List<Control>(Each(list.Controls));
                parts.Sort(delegate(Control a, Control b) { return a.Top != b.Top ? a.Top.CompareTo(b.Top) : a.Left.CompareTo(b.Left); });
                List<string> keys = new List<string>();
                foreach (Control one in parts)
                {
                    Button key = one as Button;
                    if (key != null && key.Enabled && key.Visible && !string.IsNullOrEmpty(key.AccessibleName)) keys.Add(key.AccessibleName);
                }
                return keys;
            }

            internal bool PressKey(string named)
            {
                foreach (Control one in Each(list.Controls))
                {
                    Button key = one as Button;
                    if (key == null || !key.Enabled || key.AccessibleName != named) continue;
                    key.PerformClick();
                    return true;
                }
                return false;
            }
        }
    }
}
#endif
