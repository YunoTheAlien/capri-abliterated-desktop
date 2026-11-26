using System;
using System.Drawing;

namespace CapriDesktop
{
    internal sealed partial class Mainframe
    {
        // A tree's [entry]: her text box where the grid puts it, its cue the row's words once its place words are taken out
        // (the row's name when there are none), and the row's name for a screen reader.
        private Entry TreeEntry(PanelTree.Row row, TreeWiring wire, Rectangle at)
        {
            int x, y, w, h;
            string cue = Placed(row.Words, out x, out y, out w, out h);
            Entry entry = Input(at.X, at.Y, at.Width, at.Height);
            entry.Cue = cue.Length > 0 ? cue : row.Name;
            entry.Box.AccessibleName = row.Name;
            if (row.Flags.Contains("hidden")) entry.Visible = false;
            return entry;
        }
    }
}
