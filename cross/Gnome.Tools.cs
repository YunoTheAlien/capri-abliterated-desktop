using System;
using System.Collections.Generic;

namespace CapriDesktop
{
    // Her window tool's side of the GNOME helper (cross/Gnome.cs): kept apart so the desktop checks can build the bridge
    // and her GNOME desk without the rest of her hands.
    internal static partial class GnomeBridge
    {
        // Her window tool's list: apps' own windows, not hers, named by their process like on X11.
        internal static List<OpenWindow> Windows()
        {
            List<OpenWindow> found = new List<OpenWindow>();
            foreach (KWinWindow window in Stack())
            {
                if (window.Pid == Environment.ProcessId || !window.Normal) continue;
                string name = Limbs.ProcessName(window.Pid);
                if (name.Length == 0) name = window.Class.Substring(window.Class.LastIndexOf('.') + 1).ToLowerInvariant();
                found.Add(new OpenWindow(new IntPtr(window.Id), window.Title, name));
            }
            return found;
        }

        internal static IntPtr Front()
        {
            foreach (KWinWindow window in Stack()) if (window.Active) return new IntPtr(window.Id);
            return IntPtr.Zero;
        }

        // Close waits until GNOME says it's gone (an app can ask to save first); close_tab switches, then presses Ctrl+W.
        internal static bool Steer(OpenWindow window, string action)
        {
            long id = window.Handle.ToInt64();
            if (action == "close")
            {
                if (!Act(id, "close")) return false;
                for (int i = 0; i < 10; i++)
                {
                    if (Get(id) == null) return true;
                    System.Threading.Thread.Sleep(200);
                }
                return false;
            }
            if (!Act(id, action == "minimize" ? "minimize" : "switch_to")) return false;
            if (action != "close_tab") return true;
            System.Threading.Thread.Sleep(150);
            return Fingers.Problem == null && Fingers.Keys("ctrl+w");
        }
    }
}
