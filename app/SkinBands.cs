using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // Her head and shoulder bands for the skin she wears: her own art's measured by hand; a skin's from "chin" and
    // "shoulders" in skin.json, or measured from its standing art, the neck being the first narrow row under the head.
    internal sealed partial class Companion
    {
        private string bandsFor = "";
        private float skinChin = ChinAt, skinShoulders = ShouldersAt;
        // How far under her chin her shoulders end, as on her own art.
        private const float ShoulderDrop = 0.06f;
        private const float NeckUnderChin = 0.02f;

        private string SkinBand(float share)
        {
            string skin = SkinFolder ?? "";
            if (skin != bandsFor)
            {
                bandsFor = skin;
                float chin, shoulders;
                if (skin.Length == 0 || !SkinBands(skin, out chin, out shoulders)) { chin = ChinAt; shoulders = ShouldersAt; }
                skinChin = chin;
                skinShoulders = shoulders;
            }
            return Band(share, skinChin, skinShoulders);
        }

        // A skin's bands: declared in its skin.json, else measured from its idle strip. False leaves hers.
        internal static bool SkinBands(string folder, out float chin, out float shoulders)
        {
            chin = ChinAt;
            shoulders = ShouldersAt;
            try
            {
                string details = Path.Combine(folder, "skin.json");
                if (File.Exists(details))
                {
                    string text = File.ReadAllText(details);
                    Match said = Regex.Match(text, "\"chin\"\\s*:\\s*([0-9.]+)"), down = Regex.Match(text, "\"shoulders\"\\s*:\\s*([0-9.]+)");
                    float declared, lower;
                    if (said.Success && float.TryParse(said.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out declared) && declared > 0.05f && declared < 0.6f)
                    {
                        chin = declared;
                        shoulders = down.Success && float.TryParse(down.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out lower) && lower > declared && lower < 0.7f ? lower : declared + ShoulderDrop;
                        return true;
                    }
                }
                string idle = Path.Combine(folder, "idle.png");
                if (!File.Exists(idle)) return false;
                using (MemoryStream bytes = new MemoryStream(File.ReadAllBytes(idle)))
                using (Bitmap art = new Bitmap(bytes))
                {
                    float neck = Chin(Widths(art));
                    if (neck < 0) return false;
                    chin = neck;
                    shoulders = neck + ShoulderDrop;
                    return true;
                }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); return false; }
        }

        // How much is drawn on each row of a strip, every frame together, as shares of her drawn height (the rows above
        // ArtTop are the empty top of her cell, the same way Zone counts).
        private static int[] Widths(Bitmap art)
        {
            int[] widths = new int[art.Height];
            Rectangle all = new Rectangle(0, 0, art.Width, art.Height);
            BitmapData data = art.LockBits(all, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                byte[] row = new byte[art.Width * 4];
                for (int y = 0; y < art.Height; y++)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                    int drawn = 0;
                    for (int x = 3; x < row.Length; x += 4) if (row[x] >= 128) drawn++;
                    widths[y] = drawn;
                }
            }
            finally { art.UnlockBits(data); }
            return widths;
        }

        // Her chin, as a share of her drawn height: under the widest row of her head, the first row narrower than 85% of
        // it that is narrower than the rows just under it is the foot of her neck. -1 when there is none between 0.12 and
        // 0.45.
        internal static float Chin(int[] widths)
        {
            int tall = widths.Length;
            if (tall < 40) return -1;
            Func<int, float> share = delegate(int y) { return (y - tall * ArtTop) / (tall * (1 - ArtTop)); };
            int widest = 0, step = Math.Max(1, tall / 96);
            for (int y = 0; y < tall; y++)
            {
                float at = share(y);
                if (at < 0.05f) continue;
                if (at > 0.45f) break;
                widest = Math.Max(widest, widths[y]);
                if (at < 0.12f || widths[y] >= widest * 0.85f) continue;
                bool narrowest = true;
                for (int look = y + 1; look <= Math.Min(tall - 1, y + step * 2); look++) if (widths[look] < widths[y]) { narrowest = false; break; }
                if (!narrowest) continue;
                // That row is the foot of her neck, a little under her chin on every art measured (hers 0.32 for a chin at
                // 0.30, Talia's 0.22 for 0.20).
                return (float)Math.Round(at - NeckUnderChin, 3);
            }
            return -1;
        }

        // A skin's bands on her own art, Talia's and the northern lights, read only from where they are kept.
        private void SmokeSkinBands(List<string> checks)
        {
            float chin, shoulders;
            string own = Path.Combine(Path.GetTempPath(), "capri-bands-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(own);
            try
            {
                using (Stream strip = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Capri.Sprite.idle.png"))
                using (FileStream file = File.Create(Path.Combine(own, "idle.png"))) strip.CopyTo(file);
                if (!SkinBands(own, out chin, out shoulders) || Math.Abs(chin - ChinAt) > 0.02f) throw new Exception("Her own art measured as a skin put her chin at " + chin + ", not about " + ChinAt + ".");
                File.WriteAllText(Path.Combine(own, "skin.json"), "{\"format\":1,\"id\":\"bands\",\"name\":\"bands\",\"chin\":0.22,\"shoulders\":0.27}");
                if (!SkinBands(own, out chin, out shoulders) || chin != 0.22f || shoulders != 0.27f) throw new Exception("A skin's declared bands weren't used: " + chin + " " + shoulders);
            }
            finally { try { Directory.Delete(own, true); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); } }
            if (Band(0.25f, 0.22f, 0.27f) != Touch.Shoulders || Band(0.21f, 0.22f, 0.27f) != Touch.Head || Band(0.25f, ChinAt, ShouldersAt) != Touch.Head)
                throw new Exception("The bands didn't follow the skin's chin and shoulders.");
            checks.Add("PASS a skin's head and shoulder bands come from its skin.json or are measured from its idle art, her own art measuring at her chin; a pat under a high-headed skin's chin is her shoulders there and her head on hers");
        }
    }
}
