using System;
using System.Runtime.InteropServices;

namespace CapriDesktop
{
    // Avalonia's X11 clipboard only looks up the atoms it needs ("CLIPBOARD", "UTF8_STRING", ...) if the X server
    // already has them; it never makes them. On an X server where no other program has used the clipboard yet (a bare
    // window manager with Capri started first at login, or a fresh test display) they don't exist, so every copy
    // fails with BadAtom on XSetSelectionOwner: "Copy error details" says it copied and nothing pastes, and typed
    // symbols never arrive. Found 19 Nov on a fresh Xvfb; GNOME, KDE and XFCE make them at login, so there it's a
    // no-op. Making them before Avalonia starts costs one short X connection.
    internal static class ClipboardAtoms
    {
        private const string Library = "libX11.so.6";
        [DllImport(Library)] private static extern int XInitThreads();
        [DllImport(Library)] private static extern IntPtr XOpenDisplay(IntPtr name);
        [DllImport(Library)] private static extern IntPtr XInternAtom(IntPtr display, string name, bool onlyIfExists);
        [DllImport(Library)] private static extern int XCloseDisplay(IntPtr display);

        internal static readonly string[] Names = { "CLIPBOARD", "TARGETS", "UTF8_STRING", "TEXT", "MULTIPLE", "INCR", "text/plain", "text/plain;charset=utf-8", "CLIPBOARD_MANAGER", "SAVE_TARGETS" };

        // True when each atom exists on the X server afterwards; false when there's no X display to ask.
        internal static bool Ensure()
        {
            if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))) return false;
            try
            {
                // Avalonia calls this too; it has to come before any other Xlib call in a program that uses threads.
                XInitThreads();
                IntPtr display = XOpenDisplay(IntPtr.Zero);
                if (display == IntPtr.Zero) return false;
                try
                {
                    bool all = true;
                    foreach (string name in Names) all &= XInternAtom(display, name, false) != IntPtr.Zero;
                    return all;
                }
                finally { XCloseDisplay(display); }
            }
            catch (Exception) { return false; }
        }
    }
}
