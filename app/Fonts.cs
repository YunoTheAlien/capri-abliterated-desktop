using System;
using System.Collections.Generic;
using System.Drawing;

namespace CapriDesktop
{
    // Every font she draws with, made once per family, size, style and unit and never freed: a font freed while a paint
    // or a menu still holds it fails inside GDI. The cost is one small GDI object per size she has drawn at.
    internal static class Fonts
    {
        private static readonly object gate = new object();
        private static readonly Dictionary<string, Font> held = new Dictionary<string, Font>(StringComparer.Ordinal);

        // Enough that a runaway shows up in her log instead of silently eating handles. Nothing is freed when
        // it is passed: a font in use is worth more than a tidy count, and the line says so once.
        private const int Many = 1200;
        private static bool warned;

        internal static int Count { get { lock (gate) return held.Count; } }

        // A loaded family and a Windows one can share a name, so the key is the name plus the face's em height and
        // ascent, which never change. Not the family object: a Font makes a new one each time.
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
                if (held.TryGetValue(key, out found)) { try { made.Dispose(); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); } return found; }
                held[key] = made;
                if (held.Count > Many && !warned)
                {
                    warned = true;
                    try { Brain.Log("fonts", -1, "She is holding " + held.Count + " fonts, which is more sizes than her windows should ever ask for."); }
                    catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
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
