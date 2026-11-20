using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Avalonia;
using Avalonia.Platform;
using SkiaSharp;

namespace CapriDesktop
{
    internal sealed partial class Settings
    {
        // The monitor Watch my screen looks at, picked under Mainframe > Settings > WATCH > Screens; null watches all screens.
        [DataMember(EmitDefaultValue = false)] internal string WatchScreen;
    }

    // One monitor in Screens' list, as on Windows: kept by its id, numbered and named by its size.
    internal sealed class WatchScreen
    {
        internal readonly string Id;
        internal readonly int Number;
        internal readonly PixelRect Bounds;
        internal string Name { get { return "Monitor " + Number + " (" + Bounds.Width + " x " + Bounds.Height + ")"; } }

        internal WatchScreen(string id, int number, PixelRect bounds) { Id = id; Number = number; Bounds = bounds; }
    }

    // Mainframe > Settings > WATCH > Screens, Windows' "Screens to watch". All screens looks at the whole desktop as before;
    // a picked monitor crops each glance to it. A picked monitor that's unplugged stays saved with a (missing) mark and
    // one notice, and she watches all screens until it's back.
    internal sealed partial class Pet
    {
        internal const string ScreenMissing = "That monitor is gone. I'm watching all screens until it's back.";
        // Stand-ins for the smoke test's made-up monitors and notices.
        internal Func<List<WatchScreen>> WatchDisplays;
        internal Action<string> ScreenNotice;
        private List<WatchScreen> watchScreens = new List<WatchScreen>();
        private WatchScreen watchPick;
        private string screenMissing;

        internal string WatchScreenId { get { return Saved.WatchScreen; } }
        internal List<WatchScreen> WatchScreens { get { return new List<WatchScreen>(watchScreens); } }
        internal string WatchScreenName
        {
            get
            {
                if (string.IsNullOrEmpty(Saved.WatchScreen)) return null;
                WatchScreen picked = WatchedScreen();
                return picked != null ? picked.Name : (watchPick != null && SameScreen(watchPick.Id, Saved.WatchScreen) ? watchPick.Name : Saved.WatchScreen) + " (missing)";
            }
        }

        // Screens' choices: All screens, each monitor, then a missing pick greyed at the end, as Windows' menu has it.
        internal string[] MainframeScreens
        {
            get
            {
                var names = new List<string> { "All screens" };
                foreach (WatchScreen screen in watchScreens) names.Add(screen.Name);
                if (ScreenIsMissing) names.Add(WatchScreenName);
                return names.ToArray();
            }
        }
        internal int MainframeScreenIndex
        {
            get
            {
                if (string.IsNullOrEmpty(Saved.WatchScreen)) return 0;
                int at = watchScreens.FindIndex(screen => SameScreen(screen.Id, Saved.WatchScreen));
                return at >= 0 ? at + 1 : watchScreens.Count + 1;
            }
        }
        internal bool[] MainframeScreensGreyed
        {
            get
            {
                bool[] greyed = new bool[MainframeScreens.Length];
                if (ScreenIsMissing) greyed[greyed.Length - 1] = true;
                return greyed;
            }
        }
        private bool ScreenIsMissing { get { return !string.IsNullOrEmpty(Saved.WatchScreen) && WatchedScreen() == null; } }

        private static bool SameScreen(string a, string b) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }

        // The monitors as the desktop reports them. Avalonia's display name (the X output, like HDMI-1) is the id, so a
        // monitor that moves or changes size keeps its pick; one without a name falls back to its place in the list.
        private List<WatchScreen> ReadWatchScreens()
        {
            var found = new List<WatchScreen>();
            IReadOnlyList<Screen> all = Screens.All;
            for (int i = 0; i < all.Count; i++)
            {
                string name = all[i].DisplayName;
                string id = string.IsNullOrWhiteSpace(name) ? "Monitor " + (i + 1) : name.Trim();
                // Two outputs reported under one name (a bare X server calls each one "screen") get their place added.
                if (found.Exists(screen => SameScreen(screen.Id, id))) id += " #" + (i + 1);
                found.Add(new WatchScreen(id, i + 1, all[i].Bounds));
            }
            return found;
        }

        private WatchScreen WatchedScreen()
        {
            if (string.IsNullOrEmpty(Saved.WatchScreen)) return null;
            return watchScreens.Find(screen => SameScreen(screen.Id, Saved.WatchScreen));
        }

        internal void RefreshWatchScreens()
        {
            WatchScreen before = WatchedScreen();
            watchScreens = (WatchDisplays ?? ReadWatchScreens)();
            WatchScreen picked = WatchedScreen();
            if (picked != null)
            {
                if (screenMissing != null || (before != null && before.Bounds != picked.Bounds)) ResetWatchScreen();
                watchPick = picked;
            }
            if (!string.IsNullOrEmpty(Saved.WatchScreen) && picked == null)
            {
                if (!SameScreen(screenMissing, Saved.WatchScreen))
                {
                    screenMissing = Saved.WatchScreen;
                    ResetWatchScreen();
                    if (ScreenNotice != null) ScreenNotice(ScreenMissing);
                    else if (Saved.Watch && Talk.Quiet && !away && !Hushed) Blurt("(watch my screen)", ScreenMissing);
                }
            }
            else screenMissing = null;
            NotifySettings();
        }

        internal void PickScreen(int index)
        {
            if (index == 0) ChooseWatchScreen(null);
            else if (index > 0 && index <= watchScreens.Count) ChooseWatchScreen(watchScreens[index - 1].Id);
            // The greyed missing entry keeps the saved pick.
        }

        internal void ChooseWatchScreen(string id)
        {
            Saved.WatchScreen = string.IsNullOrEmpty(id) ? null : id;
            ResetWatchScreen();
            Save();
            RefreshWatchScreens();
        }

        // A new monitor means a new picture: the next glance is a fresh one, and one already on its way is dropped.
        private void ResetWatchScreen()
        {
            watchTicket++;
            lastPrint = null;
            lastGlance = nextGlance = -1;
        }

        // The whole desktop and the picked monitor, read on the UI thread before a glance; null area means all screens.
        internal void WatchArea(out PixelRect desk, out PixelRect? area)
        {
            RefreshWatchScreens();
            desk = default;
            bool first = true;
            foreach (WatchScreen screen in watchScreens) { desk = first ? screen.Bounds : desk.Union(screen.Bounds); first = false; }
            WatchScreen picked = WatchedScreen();
            area = picked == null || first ? null : picked.Bounds;
        }

        // The screenshot tools take the whole desktop, so a picked monitor is cut out of it. The picture has to be the
        // desktop at one scale (a scaled Wayland desktop is fine); anything else, like a single window, is left whole.
        internal static byte[] CropToScreen(byte[] picture, PixelRect desk, PixelRect? area)
        {
            if (area == null || picture == null || picture.Length == 0 || desk.Width <= 0 || desk.Height <= 0) return picture;
            SKBitmap read;
            try { read = SKBitmap.Decode(picture); }
            catch (Exception) { return picture; }
            using (SKBitmap whole = read)
            {
                if (whole == null || whole.Width <= 0 || whole.Height <= 0) return picture;
                double scale = (double)whole.Width / desk.Width;
                if (Math.Abs(whole.Height - desk.Height * scale) > 2) return picture;
                PixelRect pick = area.Value;
                int left = (int)Math.Round((pick.X - desk.X) * scale), top = (int)Math.Round((pick.Y - desk.Y) * scale);
                int right = (int)Math.Round((pick.Right - desk.X) * scale), bottom = (int)Math.Round((pick.Bottom - desk.Y) * scale);
                left = Math.Clamp(left, 0, whole.Width); right = Math.Clamp(right, 0, whole.Width);
                top = Math.Clamp(top, 0, whole.Height); bottom = Math.Clamp(bottom, 0, whole.Height);
                if (right - left < 1 || bottom - top < 1) return picture;
                using (SKBitmap part = new SKBitmap(right - left, bottom - top))
                {
                    if (!whole.ExtractSubset(part, new SKRectI(left, top, right, bottom))) return picture;
                    using (SKImage image = SKImage.FromBitmap(part))
                    using (SKData png = image == null ? null : image.Encode(SKEncodedImageFormat.Png, 100))
                        return png == null ? picture : png.ToArray();
                }
            }
        }
    }
}
