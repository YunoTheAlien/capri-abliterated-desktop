using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;

namespace CapriDesktop
{
    // Whether a font actually has the letter, and what to draw it in when it does not.
    //
    // A font with no glyph for a character does not fail and does not fall back: GDI+ draws the font's own
    // stand-in, which in most faces is a hollow box, and in a good many is a box with a cross through it. So
    // a Russian device name, a Japanese window title, a Greek filename or an emoji in a message comes out as
    // a row of boxes, on somebody else's machine, in a font that was chosen long before anybody knew what it
    // would be asked to draw.
    //
    // Her own faces are the narrow ones: a display font and an embedded mono, neither of which carries much
    // beyond Latin. That is fine as long as nothing hands them a letter they do not have.
    //
    // The font itself is asked, through GDI, rather than guessed at from the character's block: a family that
    // does carry Cyrillic keeps drawing it, and one that does not is caught whatever the character is. The
    // answer is kept per family and style, since a glyph does not appear or vanish with the size.
    internal static class Glyphs
    {
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern uint GetGlyphIndicesW(IntPtr dc, string text, int count, ushort[] indices, uint flags);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr held);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr held);

        private const uint MarkMissing = 1;
        private const ushort None = 0xFFFF;

        private static readonly object gate = new object();
        private static readonly Dictionary<string, Dictionary<char, bool>> known = new Dictionary<string, Dictionary<char, bool>>(StringComparer.Ordinal);

        // What to try instead, in order. Between them Windows ships cover for nearly everything anybody types:
        // Segoe UI for Europe, Nirmala for India, the CJK faces, and the symbol and emoji faces for the rest.
        private static readonly string[] Instead =
        {
            "Segoe UI", "Nirmala UI", "Myanmar Text", "Leelawadee UI", "Microsoft YaHei", "Yu Gothic UI",
            "Malgun Gothic", "Segoe UI Symbol", "Segoe UI Emoji", "Segoe UI Historic", "Arial Unicode MS"
        };

        // Two faces can share a name, so what is remembered is kept against the shape of the face as well.
        // A glyph does not come and go with the size, so the size is not in it.
        private static string Key(Font font)
        {
            if (font == null) return "";
            FontFamily family = font.FontFamily;
            try
            {
                FontStyle shape = family.IsStyleAvailable(FontStyle.Regular) ? FontStyle.Regular
                    : family.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Italic;
                return family.Name + "/" + family.GetEmHeight(shape) + "/" + family.GetCellAscent(shape)
                    + "|" + (int)font.Style;
            }
            catch (Exception) { return family.Name + "|" + (int)font.Style; }
        }

        // Every letter a font has to draw, one question to GDI for the ones not asked about before.
        internal static bool Draws(Font font, string text)
        {
            if (font == null || string.IsNullOrEmpty(text)) return true;
            foreach (char one in text) if (!Draws(font, one)) return false;
            return true;
        }

        internal static bool Draws(Font font, char one)
        {
            // Space and the control characters are never drawn, so nothing is missing when they have no glyph.
            if (font == null || one < ' ') return true;
            string key = Key(font);
            Dictionary<char, bool> seen;
            lock (gate)
            {
                if (!known.TryGetValue(key, out seen)) known[key] = seen = new Dictionary<char, bool>();
                bool had;
                if (seen.TryGetValue(one, out had)) return had;
            }
            bool has = Asked(font, one);
            lock (gate) seen[one] = has;
            return has;
        }

        private static bool Asked(Font font, char one)
        {
            IntPtr face = IntPtr.Zero;
            try
            {
                using (Bitmap paper = new Bitmap(1, 1))
                using (Graphics graphics = Graphics.FromImage(paper))
                {
                    IntPtr dc = graphics.GetHdc();
                    try
                    {
                        face = font.ToHfont();
                        IntPtr was = SelectObject(dc, face);
                        ushort[] found = new ushort[1];
                        uint got = GetGlyphIndicesW(dc, one.ToString(), 1, found, MarkMissing);
                        SelectObject(dc, was);
                        // GDI could not say, so it is treated as drawable: a wrong box is better than every
                        // string on the machine going through a fallback it does not need.
                        if (got == 0xFFFFFFFF) return true;
                        return found[0] != None;
                    }
                    finally { graphics.ReleaseHdc(dc); }
                }
            }
            catch (Exception) { return true; }
            finally { if (face != IntPtr.Zero) { try { DeleteObject(face); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); } } }
        }

        // The same size and style in a face that has the letter. Her own font back again when nothing else
        // does either, since a box in her own face is no worse than a box in Arial.
        internal static Font Other(Font font, string text)
        {
            if (font == null) return null;
            foreach (string family in Instead)
            {
                Font other = Fonts.Of(family, font.Size, font.Style, font.Unit);
                if (other == null || !string.Equals(other.Name, family, StringComparison.OrdinalIgnoreCase)) continue;
                if (Draws(other, text)) return other;
            }
            return font;
        }

        // The text in runs, each with the font that can draw it. One run for anything her own face handles,
        // which is almost everything almost always, so the common line costs one check and no splitting.
        internal static List<KeyValuePair<string, Font>> Runs(Font font, string text)
        {
            List<KeyValuePair<string, Font>> runs = new List<KeyValuePair<string, Font>>();
            if (string.IsNullOrEmpty(text)) return runs;
            if (font == null || Draws(font, text)) { runs.Add(new KeyValuePair<string, Font>(text, font)); return runs; }
            int from = 0;
            bool had = Draws(font, text[0]);
            for (int at = 1; at <= text.Length; at++)
            {
                bool now = at < text.Length && Draws(font, text[at]);
                if (at < text.Length && now == had) continue;
                string piece = text.Substring(from, at - from);
                runs.Add(new KeyValuePair<string, Font>(piece, had ? font : Other(font, piece)));
                from = at;
                if (at < text.Length) had = now;
            }
            return runs;
        }
    }
}
