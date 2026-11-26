using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Mainframe
    {
        // One left column for windows that list on the left and show the pick on the right (Toggles, Custom colours,
        // Personality settings): x 28, 280 wide, framed with her scroll lane; the right side from 336. Scaled with the window.
        internal static class LeftColumn
        {
            internal const int Left = 28, Wide = 280, Rim = Left + Wide + 12, Side = Rim + 16;

            // The list and its slim bar inside the frame: the bar 3 after the list, in her global colours.
            internal static void Place(ScrollPanel list, SlimBar bar, int top, int tall, float zoom)
            {
                list.Bounds = new Rectangle(Z(Left, zoom), Z(top, zoom), Z(Wide, zoom), Z(tall, zoom));
                bar.Bounds = new Rectangle(list.Right + Z(3, zoom), list.Top + Z(2, zoom), Z(SlimBar.Thin, zoom), list.Height - Z(4, zoom));
                bar.BackColor = Ground;
            }

            // The frame round it, in her edge colour, 4 out from the list and round its bar.
            internal static void Frame(Graphics graphics, Control list, float zoom)
            {
                using (Pen edge = new Pen(Neon.Edge))
                    graphics.DrawRectangle(edge, list.Left - Z(4, zoom), list.Top - Z(4, zoom), list.Width + Z(16, zoom), list.Height + Z(8, zoom));
            }

            private static int Z(int value, float zoom) { return (int)Math.Round(value * zoom); }
        }

        // A window's picture as Windows shows it: what it drew, kept only inside its outline (its Region, the clipped corners
        // cut); a window with no Region is all of it. A picture of the whole rectangle showed its edge and ground past them.
        internal static Bitmap Outlined(Bitmap drawn, Control window)
        {
            Bitmap cut = new Bitmap(drawn.Width, drawn.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics paint = Graphics.FromImage(cut))
            {
                paint.Clear(Color.Transparent);
                if (window != null && window.Region != null) paint.SetClip(window.Region, System.Drawing.Drawing2D.CombineMode.Replace);
                paint.DrawImageUnscaled(drawn, 0, 0);
            }
            return cut;
        }

        // Words too long for their place stay on one line at their row's height, cut with "...", and her tip card gives the
        // whole of them after a moment under the pointer. Nothing wraps and nothing rolls.
        internal static class CutWords
        {
            private const double TipAfter = 0.7;
            private const string Waiting = "cut words tip";
            private static Control over;
            private static string words;
            private static bool tipped, longer;
            internal static Action<Control, string> Tip;
            internal static Action Untip;

            // The pointer came onto these words: if they are cut, the tip comes a moment later.
            internal static void Enter(Control control, string text)
            {
                if (over != control) Stop();
                over = control;
                words = text ?? "";
                tipped = false;
                Clock.After(Waiting, TimeSpan.FromSeconds(TipAfter), delegate
                {
                    if (over != control || control.IsDisposed || !control.Visible || !longer || tipped || Tip == null) return;
                    tipped = true;
                    Tip(control, words);
                });
                control.Invalidate();
            }

            internal static void Leave(Control control) { if (over == control) Stop(); }

            private static void Stop()
            {
                Clock.Stop(Waiting);
                if (tipped && Untip != null) Untip();
                over = null;
                tipped = longer = false;
            }

            // Other words on the same control cut short too, like a colour's note: the tip then gives them whole as well.
            internal static void AlsoCut(Control control, bool cut) { if (control == over && cut) longer = true; }

            // Draws them in room on one line: whole when they fit, cut with "..." when not.
            internal static void Draw(Graphics graphics, Control control, string text, Font font, Rectangle room, Color ink, TextFormatFlags flags)
            {
                text = text ?? "";
                flags |= TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
                flags &= ~(TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
                int spare = TextRenderer.MeasureText(graphics, text, font, Size.Empty, flags | TextFormatFlags.NoPadding).Width - room.Width;
                if (control == over && spare > 1) longer = true;
                TextRenderer.DrawText(graphics, text, font, room, ink, spare > 1 ? flags | TextFormatFlags.EndEllipsis : flags);
            }

#if CHECKS
            // For the checks: whether these words, drawn in this room, are cut and so tipped on hover.
            internal static bool Cut(string text, Font font, int room)
            {
                return TextRenderer.MeasureText(text ?? "", font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width - room > 1;
            }

            internal static Control Over { get { return over; } }

            internal static bool Tipped { get { return tipped; } }
#endif
        }

#if CHECKS
        // The left column in Toggles, Custom colours and Personality settings; Toggles' narrow switches, no slider in its list,
        // the screensaver's wait behind its >; a long name on one line, cut, its words in the tip. Null when right.
        internal string CheckColumns()
        {
            List<string> wrong = new List<string>();
            Func<Form, string, float, string> column = delegate(Form window, string named, float at)
            {
                ScrollPanel left = null;
                foreach (Control one in window.Controls) { ScrollPanel list = one as ScrollPanel; if (list != null && (left == null || list.Left < left.Left)) left = list; }
                if (left == null) return named + " has no list";
                int x = (int)Math.Round(LeftColumn.Left * at), wide = (int)Math.Round(LeftColumn.Wide * at);
                if (Math.Abs(left.Left - x) > 1 || Math.Abs(left.Width - wide) > 1) return named + "'s column is at " + left.Left + ", " + left.Width + " wide, not " + x + ", " + wide;
                Control near = null;
                foreach (Control one in window.Controls)
                    if (one is SlimBar && one.Left >= left.Right && (near == null || one.Left < near.Left)) near = one;
                if (near == null) return named + "'s list has no bar beside it";
                if (near.Left - left.Right > (int)Math.Round(4 * at) + 1) return named + "'s bar sits " + (near.Left - left.Right) + " after its list, outside the frame";
                return null;
            };
            try
            {
                OpenToggles();
                toggleSheet.UnfoldAll();
                Application.DoEvents();
                string problem = column(toggleSheet, "Toggles", toggleSheet.Zoom);
                if (problem != null) wrong.Add(problem);
                foreach (string part in toggleSheet.ColumnShown.Split(new[] { " | " }, StringSplitOptions.None))
                    if (part.StartsWith("pair ", StringComparison.Ordinal) && part != "pair " + Toggle.Narrow) { wrong.Add("a Toggles switch is " + part.Substring(5) + " wide, not the narrow " + Toggle.Narrow); break; }
                foreach (Control one in toggleSheet.ListControls) if (one is Slider) { wrong.Add("a slider is still in the Toggles list: " + one.AccessibleName); break; }
                if (!toggleSheet.OpenSettings("Screensaver")) wrong.Add("the screensaver has no > for its sliders");
                else if (toggleSheet.Shown.IndexOf("{Screensaver} | After: ", StringComparison.Ordinal) < 0) wrong.Add("the screensaver's > doesn't show its wait: " + toggleSheet.Shown);
                // A long name: one line, cut, still under the pointer, and its tip coming with the whole name.
                const string longName = "A mod with a very long name that never fits beside its switch";
                using (Toggle row = new Toggle { Text = longName, Font = toggleSheet.Font, Bounds = new Rectangle(0, 0, (int)Math.Round((LeftColumn.Wide - 40) * toggleSheet.Zoom), (int)Math.Round(22 * toggleSheet.Zoom)), Switch = Toggle.Narrow })
                {
                    toggleSheet.Controls.Add(row);
                    if (!CutWords.Cut(longName, row.Font, row.NameRoom)) wrong.Add("the long name fits, so it can't show cutting");
                    CutWords.Enter(row, longName);
                    using (Bitmap first = new Bitmap(row.Width, row.Height))
                    using (Bitmap later = new Bitmap(row.Width, row.Height))
                    {
                        row.DrawToBitmap(first, new Rectangle(Point.Empty, first.Size));
                        System.Diagnostics.Stopwatch waited = System.Diagnostics.Stopwatch.StartNew();
                        while (waited.ElapsedMilliseconds < 2600) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
                        row.DrawToBitmap(later, new Rectangle(Point.Empty, later.Size));
                        int changed = 0;
                        for (int y = 0; y < first.Height; y++) for (int x = 0; x < row.NameRoom; x++) if (first.GetPixel(x, y) != later.GetPixel(x, y)) changed++;
                        if (changed != 0) wrong.Add("a long name moved under the pointer: " + changed + " pixels changed");
                        if (!CutWords.Tipped) wrong.Add("a long name's tip didn't come under the pointer");
                    }
                    CutWords.Leave(row);
                    toggleSheet.Controls.Remove(row);
                }
                CloseToggles();
                OpenColours();
                Application.DoEvents();
                problem = column(colourSheet, "Custom colours", colourSheet.Zoom);
                if (problem != null) wrong.Add(problem);
                colourSheet.Close();
                OpenPersonalitySettings();
                Application.DoEvents();
                problem = column(personalitySheet, "Personality settings", personalitySheet.Zoom);
                if (problem != null) wrong.Add(problem);
            }
            finally
            {
                CloseToggles();
                if (colourSheet != null && !colourSheet.IsDisposed) colourSheet.Close();
                ClosePersonalitySettings();
            }
            return wrong.Count == 0 ? null : string.Join("; ", wrong.ToArray());
        }
#endif
    }
}
