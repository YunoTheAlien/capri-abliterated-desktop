using System;
using System.Collections.Generic;
using System.Drawing;

namespace CapriDesktop
{
    // Every font she draws with, made once and kept.
    //
    // Her windows rebuild their fonts whenever anything rescales: a DPI change, her zoom, her text size. The
    // old ones were freed when they were replaced, which is correct right up until something is still holding
    // one. Windows answers a freed font with "Parameter is not valid." from inside GDI, which fails a
    // measurement and kills her outright in a paint, and the places that hold one are the places nobody
    // thinks about: a paint already on its way, a menu's own copy, a control measuring itself.
    //
    // Freeing them a layout later rather than immediately only moved the window the fault lives in: a full
    // suite that rescales constantly made it more frequent, not less.
    //
    // So nothing is freed. A font is made once for a family, size, style and unit, and every caller that asks
    // for that same one gets the same object for as long as she runs. Rescaling to a size she has drawn
    // before costs nothing, and no font is ever pulled out from under anything.
    //
    // What this costs: one small GDI object per distinct size she has ever drawn at. Her zoom and text size
    // have a handful of steps each, so the set is bounded by what the interface actually uses and settles
    // within a few rescales. What it buys: the whole class of fault is gone rather than made rarer.
    internal static class Fonts
    {
        private static readonly object gate = new object();
        private static readonly Dictionary<string, Font> held = new Dictionary<string, Font>(StringComparer.Ordinal);

        // Enough that a runaway shows up in her log instead of silently eating handles. Nothing is freed when
        // it is passed: a font in use is worth more than a tidy count, and the line says so once.
        private const int Many = 1200;
        private static bool warned;

        internal static int Count { get { lock (gate) return held.Count; } }

        // A family she loaded herself and one Windows has can carry the same name, and then one key would
        // hand out the wrong face: a narrow embedded one where a full system face was asked for, which draws
        // every letter it has no glyph for as a box.
        //
        // So the name is not the key on its own, the name and the shape of the face are. Two faces that
        // share a name do not share their em height and their ascent, and a face's own numbers do not
        // change, so this stays the same answer for the same family however often it is asked. Keying on
        // the family object instead would have been simpler and wrong: a Font makes its own, so the same
        // family arrives as a different object each time, which would both miss the cache and hold every
        // one of them for ever.
        private static string Tag(FontFamily family)
        {
            try
            {
                FontStyle shape = family.IsStyleAvailable(FontStyle.Regular) ? FontStyle.Regular
                    : family.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Italic;
                return family.Name + "/" + family.GetEmHeight(shape) + "/" + family.GetCellAscent(shape)
                    + "/" + family.GetCellDescent(shape) + "/" + family.GetLineSpacing(shape);
            }
            catch (Exception) { return family.Name; }
        }

        internal static Font Of(FontFamily family, float size, FontStyle style, GraphicsUnit unit)
        {
            if (family == null) return null;
            return Kept(Tag(family) + "|" + Key(size, style, unit), delegate { return new Font(family, size, style, unit); });
        }

        internal static Font Of(string family, float size, FontStyle style, GraphicsUnit unit)
        {
            if (string.IsNullOrEmpty(family)) return null;
            return Kept(family + "|" + Key(size, style, unit), delegate { return new Font(family, size, style, unit); });
        }

        // The same font at a different size, for the places that scale one they were handed.
        internal static Font Like(Font font, float size)
        {
            if (font == null) return null;
            return Of(font.FontFamily, size, font.Style, font.Unit);
        }

        private static string Key(float size, FontStyle style, GraphicsUnit unit)
        {
            // Rounded, so the float noise two different sums of the same zoom produce does not make two fonts.
            return Math.Round(size, 2).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                + "|" + (int)style + "|" + (int)unit;
        }

        private static Font Kept(string key, Func<Font> make)
        {
            lock (gate)
            {
                Font found;
                if (held.TryGetValue(key, out found)) return found;
            }
            Font made;
            try { made = make(); }
            catch (Exception) { return null; }
            lock (gate)
            {
                Font found;
                // Another thread may have made the same one while this was being built. Theirs is the one
                // everybody else already has, so this one goes rather than replacing it.
                if (held.TryGetValue(key, out found)) { try { made.Dispose(); } catch (Exception) { } return found; }
                held[key] = made;
                if (held.Count > Many && !warned)
                {
                    warned = true;
                    try { Brain.Log("fonts", -1, "She is holding " + held.Count + " fonts, which is more sizes than her windows should ever ask for."); }
                    catch (Exception) { }
                }
                return made;
            }
        }

        // Her mono face from the cache, with the same fallback walk her factory does. For the fonts handed
        // to controls, which are the ones nothing may ever free.
        internal static Font Mono(float size, FontStyle style, GraphicsUnit unit)
        {
            foreach (string family in new[] { "Cascadia Mono", "Consolas", "Courier New" })
                if (Real(family, size, style, unit)) return Of(family, size, style, unit);
            return Of(FontFamily.GenericMonospace, size, style, unit);
        }

        // Whether a family can be drawn at all, without making a font to find out and then throwing it away.
        internal static bool Real(string family, float size, FontStyle style, GraphicsUnit unit)
        {
            Font font = Of(family, size, style, unit);
            return font != null && string.Equals(font.Name, family, StringComparison.OrdinalIgnoreCase);
        }
    }
}
