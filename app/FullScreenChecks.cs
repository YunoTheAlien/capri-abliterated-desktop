using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

#if CHECKS
namespace CapriDesktop
{
    // None of her windows goes full screen on a double-click on its title bar.
    // Each is opened alone on the test screen and sent the double-click as a message; its size and
    // place stay as they were.
    internal sealed partial class Mainframe
    {
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wide, IntPtr data);

        internal void CheckNoFullScreen(List<string> checks)
        {
            List<KeyValuePair<string, Func<Form>>> windows = new List<KeyValuePair<string, Func<Form>>>
            {
                new KeyValuePair<string, Func<Form>>("Provider", delegate { return new ProviderSheet(this, 1f); }),
                new KeyValuePair<string, Func<Form>>("Thinking", delegate { return new ThinkingSheet(this, 1f); }),
                new KeyValuePair<string, Func<Form>>("Server", delegate { return new ServerSheet(this, 1f); }),
                new KeyValuePair<string, Func<Form>>("SECURITY", delegate { return new SecuritySheet(this, 1f); }),
                new KeyValuePair<string, Func<Form>>("Extras", delegate { return new ExtrasSheet(this, 1f); }),
                new KeyValuePair<string, Func<Form>>("Voice settings", delegate { return new LayersSheet(this, 1f); }),
                new KeyValuePair<string, Func<Form>>("Mix", delegate { return new MixSheet(this, 1f); }),
                new KeyValuePair<string, Func<Form>>("Custom colours", delegate { return new ColourSheet(this, 1f); }),
                new KeyValuePair<string, Func<Form>>("Toggles", delegate { return new ToggleSheet(this, 1f, capri.TogglesMenu); }),
                new KeyValuePair<string, Func<Form>>("Make a mod", delegate { return new ModHelperSheet(this, 1f); }),
                new KeyValuePair<string, Func<Form>>("Streaming", delegate { return new StreamingSheet(this, 1f, capri.StreamMenu, null); }),
                new KeyValuePair<string, Func<Form>>("Her GIFs", delegate { return new GifSheet(this, 1f); }),
                new KeyValuePair<string, Func<Form>>("Her memory", delegate { return new MemorySheet(this, 1f); }),
                new KeyValuePair<string, Func<Form>>("Mods directory", delegate { return new ModsSheet(this, 1f); }),
                new KeyValuePair<string, Func<Form>>("Remote", delegate { return new RemoteSheet(this, 1f, false); }),
                new KeyValuePair<string, Func<Form>>("Resources", delegate { return new ResourcesSheet(this, 1f, false); }),
                new KeyValuePair<string, Func<Form>>("Set up Email", delegate { return new MailSheet(this, 1f); }),
                new KeyValuePair<string, Func<Form>>("Security overrides", delegate { return new OverridesSheet(this, 1f, "Check", "check", new List<string[]>(), null); }),
                new KeyValuePair<string, Func<Form>>("Logs", delegate { return LogPartForCheck(); }),
                new KeyValuePair<string, Func<Form>>("File editor", delegate { return new FileEditor(); }),
                new KeyValuePair<string, Func<Form>>("Hint card", delegate { return new HintCard(); }),
                new KeyValuePair<string, Func<Form>>("Type box", delegate { return new TypeBox(); }),
            };
            List<string> grew = new List<string>(), opened = new List<string>();
            // The Mainframe itself first, as it is.
            Rectangle area = Screen.FromControl(this).WorkingArea;
            FormWindowState stateWas = WindowState;
            try
            {
                if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
                string moved = DoubleClicked(this);
                opened.Add("Mainframe");
                if (moved != null) grew.Add("Mainframe " + moved);
            }
            finally { if (WindowState != stateWas) WindowState = stateWas; }
            foreach (KeyValuePair<string, Func<Form>> one in windows)
            {
                Form made = null;
                try
                {
                    made = one.Value();
                    made.StartPosition = FormStartPosition.Manual;
                    made.Location = new Point(area.Left + 40, area.Top + 40);
                    made.Show(this);
                    Application.DoEvents();
                    string moved = DoubleClicked(made);
                    opened.Add(one.Key);
                    if (moved != null) grew.Add(one.Key + " " + moved);
                }
                catch (Exception error) { grew.Add(one.Key + " couldn't be opened: " + error.Message); }
                finally { if (made != null && !made.IsDisposed) { made.Close(); made.Dispose(); } }
            }
            if (grew.Count > 0) throw new Exception("A double-click on a title bar changed a window: " + string.Join("; ", grew.ToArray()));
            checks.Add("PASS a double-click on the title bar leaves each window as it was, none full screen: " + string.Join(", ", opened.ToArray()));
        }

        // The double-click sent to its title bar, near its top left: what changed, or null when nothing did.
        private static string DoubleClicked(Form window)
        {
            Rectangle before = window.Bounds;
            FormWindowState state = window.WindowState;
            Point at = window.PointToScreen(new Point(Math.Min(window.ClientSize.Width / 2, 120), 12));
            SendMessage(window.Handle, 0x00A3, new IntPtr(2), new IntPtr((at.Y << 16) | (at.X & 0xFFFF)));
            Application.DoEvents();
            string changed = window.WindowState != state ? "went " + window.WindowState : window.Bounds != before ? "moved from " + before + " to " + window.Bounds : null;
            if (window.WindowState == FormWindowState.Maximized) window.WindowState = FormWindowState.Normal;
            if (window.Bounds != before) window.Bounds = before;
            return changed;
        }
    }
}
#endif
