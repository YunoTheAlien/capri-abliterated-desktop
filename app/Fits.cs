using System;
using System.Drawing;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Every window of hers fits the screen it opens on: shrunk to the work area, never under a floor. A check sets a
    // stand-in work area, as Windows can't be asked for a smaller monitor.
    internal static class Fits
    {
        // Room left free round a window, in screen pixels, so its edge never sits on the taskbar's.
        internal const int Margin = 16;

        // For checks: the work area and the screen's scale to fit against. Empty and nought are the real screen.
        internal static Rectangle StandIn;
        internal static float StandInScale;

        // The work area a window opening at this point is fitted to.
        internal static Rectangle Area(Point at) { return StandIn.IsEmpty ? Screen.FromPoint(at).WorkingArea : StandIn; }

        internal static Rectangle Area(Control near) { return StandIn.IsEmpty ? Screen.FromControl(near).WorkingArea : StandIn; }

        // The screen's own scale, or the check's.
        internal static float Device(Control on) { return StandInScale > 0 ? StandInScale : on.DeviceDpi / 96f; }

        // The scale a design of this size (100% pixels) takes in the area: at most the one wanted, at least the floor.
        internal static float Scale(Size design, float wanted, Rectangle area, float floor)
        {
            if (design.Width <= 0 || design.Height <= 0 || area.Width <= 0 || area.Height <= 0) return wanted;
            float room = Math.Min((area.Height - Margin) / (float)design.Height, (area.Width - Margin) / (float)design.Width);
            return Math.Max(floor, Math.Min(wanted, room));
        }

        // True when a design at this scale is too big for the area, so its body has to scroll.
        internal static bool Over(Size design, float scale, Rectangle area)
        {
            return design.Height * scale > area.Height - Margin || design.Width * scale > area.Width - Margin;
        }

        // A window of this size in the middle of the area, wholly inside it.
        internal static Point Centred(Size size, Rectangle area)
        {
            return Mainframe.KeptIn(new Rectangle(area.Left + (area.Width - size.Width) / 2, area.Top + (area.Height - size.Height) / 2, size.Width, size.Height), area);
        }

        // Whether a window's bounds lie wholly inside the area, for the checks.
        internal static bool Inside(Rectangle bounds, Rectangle area) { return area.Contains(bounds); }
    }
}
