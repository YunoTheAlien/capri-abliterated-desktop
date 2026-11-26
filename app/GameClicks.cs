using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CapriDesktop
{
    // The cursor the way Windows is drawing it right now, and its word the moment a cursor hides or shows or another window comes
    // in front (Lachu).
    internal static class GameCursor
    {
        [StructLayout(LayoutKind.Sequential)] private struct Info { internal int Size, Flags; internal IntPtr Handle; internal int X, Y; }
        internal delegate void Changed(IntPtr hook, int kind, IntPtr window, int item, int child, int thread, int time);
        [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref Info info);
        [DllImport("user32.dll")] private static extern IntPtr GetCapture();
        [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(int first, int last, IntPtr module, Changed changed, int process, int thread, int flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);

        internal const int Front = 0x0003, Shown = 0x8002, Gone = 0x8003, Pointer = -9;

        internal struct Reading
        {
            internal bool Read, Hidden;
            internal Point At;
            internal int Flags;
            internal IntPtr Shape;

            public override string ToString() { return (Read ? "flags " + Flags + ", cursor 0x" + Shape.ToInt64().ToString("X") : "no cursor to read") + " at " + At.X + "," + At.Y; }
        }

        // A game can hide its cursor or set none at all, and both count as hidden. Touch and pen hiding it don't.
        internal static Func<Reading> Read = delegate
        {
            Info info = new Info { Size = Marshal.SizeOf(typeof(Info)) };
            Reading seen = new Reading { Read = GetCursorInfo(ref info) };
            seen.At = new Point(info.X, info.Y);
            seen.Flags = info.Flags;
            seen.Shape = info.Handle;
            seen.Hidden = seen.Read && ((info.Flags & 3) == 0 || ((info.Flags & 1) != 0 && info.Handle == IntPtr.Zero));
            return seen;
        };

        // One of her windows holding the mouse, mid-drag or with a menu open, so the cursor's hers wherever it is.
        internal static Func<bool> Held = delegate { return GetCapture() != IntPtr.Zero; };

        internal static IntPtr Watch(int first, int last, Changed changed) { return SetWinEventHook(first, last, IntPtr.Zero, changed, 0, 0, 0); }

        internal static void Stop(IntPtr hook) { if (hook != IntPtr.Zero) UnhookWinEvent(hook); }

        // The program whose window is under a spot on screen, or 0 when nothing is.
        internal static Func<Point, int> ProcessAt = delegate(Point at)
        {
            IntPtr window = Native.GetAncestor(Native.WindowFromPoint(new Native.Point(at.X, at.Y)), 2);
            int process;
            return window == IntPtr.Zero || Native.GetWindowThreadProcessId(window, out process) == 0 ? 0 : process;
        };

        [DllImport("user32.dll")] internal static extern IntPtr GetShellWindow();
    }

    // A game hiding its cursor to aim still moves it over her, and a shot on her opened her menu and lost the game focus. So
    // clicks then go through every window of hers and none takes focus, until the game shows its cursor or leaves the front.
    internal sealed partial class Companion
    {
        private bool gameAim, aimHidden;
        private IntPtr aimFront, cursorHook, frontHook;
        private int aimGame;
        private double aimLooked = double.NegativeInfinity;
        private GameCursor.Changed cursorChanged;
        private static readonly int aimOwn = OwnProcess();

        internal bool GameAiming { get { return gameAim; } }

        private static int OwnProcess() { using (Process self = Process.GetCurrentProcess()) return self.Id; }

        private void WatchGameCursor(bool on)
        {
            GameCursor.Stop(cursorHook);
            GameCursor.Stop(frontHook);
            cursorHook = frontHook = IntPtr.Zero;
            if (!on) return;
            if (cursorChanged == null) cursorChanged = CursorSeen;
            cursorHook = GameCursor.Watch(GameCursor.Shown, GameCursor.Gone, cursorChanged);
            frontHook = GameCursor.Watch(GameCursor.Front, GameCursor.Front, cursorChanged);
        }

        private void CursorSeen(IntPtr hook, int kind, IntPtr window, int item, int child, int thread, int time)
        {
            if (kind != GameCursor.Front && item != GameCursor.Pointer) return;
            if (!quitting && !IsDisposed) CheckGameCursor(clock.Elapsed.TotalSeconds);
        }

        // Windows keeps a cursor per program, so only a reading over the game's own window counts; once clicks go through, the
        // cursor is over the game even where she's drawn, so it keeps reading true.
        private void CheckGameCursor(double now)
        {
            if (!IsHandleCreated) return;
            try
            {
                IntPtr front = Hands.Foreground();
                if (front != aimFront) { aimFront = front; aimLooked = double.NegativeInfinity; aimHidden = false; }
                if (!GameCursor.Held() && !menu.Visible)
                {
                    GameCursor.Reading seen = GameCursor.Read();
                    int under = seen.Read ? GameCursor.ProcessAt(seen.At) : aimOwn;
                    if (under != aimOwn) aimHidden = seen.Hidden && under == GameAt(front, now);
                }
                if (aimHidden && GameAt(front, now) == 0) aimHidden = false;
            }
            catch (Exception) { aimHidden = false; }
            Aim(aimHidden);
        }

        // The program of the game in front, or 0 when what's in front isn't a game. It's looked at again once a second.
        private int GameAt(IntPtr front, double now)
        {
            if (now - aimLooked < 1) return aimGame;
            aimLooked = now;
            int process;
            aimGame = front != IntPtr.Zero && Native.GetWindowThreadProcessId(front, out process) != 0 && process != 0 && process != aimOwn && GameInFront(front) ? process : 0;
            return aimGame;
        }

        private static bool GameInFront(IntPtr front)
        {
            Sight sight = Computer.Glimpse();
            return sight != null && sight.Window == front && Computer.GameWindow(sight);
        }

        private void Aim(bool on)
        {
            if (gameAim == on) return;
            gameAim = on;
            PassThrough();
        }

        // A game in front hiding its cursor over its own window: she lets every click through and takes no focus, and a
        // right click never opens her menu; the cursor showing again, or no game in front, gives her clicks back.
#if CHECKS
        private void SmokeGameAim(List<string> checks)
        {
            Func<IntPtr> foregroundWas = Hands.Foreground;
            Func<Sight> glimpseWas = Computer.Glimpse;
            Func<GameCursor.Reading> readWas = GameCursor.Read;
            Func<Point, int> processWas = GameCursor.ProcessAt;
            Func<bool> heldWas = GameCursor.Held;
            IntPtr game = GameCursor.GetShellWindow();
            int process;
            if (game == IntPtr.Zero || Native.GetWindowThreadProcessId(game, out process) == 0 || process == aimOwn) { checks.Add("LIMIT game aim: no other program's window to stand in for a game"); return; }
            bool hiddenNow = true, gameNow = true;
            try
            {
                Hands.Foreground = delegate { return game; };
                Computer.Glimpse = delegate { return gameNow ? new Sight { Window = game, Title = "Roblox", Program = "RobloxPlayerBeta" } : new Sight { Window = game, Title = "Notes.txt - Notepad", Program = "notepad" }; };
                GameCursor.Read = delegate { return new GameCursor.Reading { Read = true, Hidden = hiddenNow, At = new Point(10, 10), Flags = hiddenNow ? 0 : 1 }; };
                GameCursor.ProcessAt = delegate { return process; };
                GameCursor.Held = delegate { return false; };
                aimFront = IntPtr.Zero;
                CheckGameCursor(clock.Elapsed.TotalSeconds);
                int style = Native.GetWindowLong(Handle, -20);
                bool through = gameAim && (style & 0x20) != 0 && (style & 0x08000000) != 0;
                OnMouseUp(new MouseEventArgs(MouseButtons.Right, 1, 5, 5, 0));
                bool menuShut = !menu.Visible;
                hiddenNow = false;
                CheckGameCursor(clock.Elapsed.TotalSeconds);
                bool shown = !gameAim && ((Native.GetWindowLong(Handle, -20) & 0x20) == 0 || preferences.Hologram);
                hiddenNow = true;
                gameNow = false;
                aimFront = IntPtr.Zero;
                CheckGameCursor(clock.Elapsed.TotalSeconds + 2);
                bool notGame = !gameAim;
                if (!through || !menuShut || !shown || !notGame)
                    throw new Exception("A game hiding its cursor didn't make her click-through, or it stayed: through " + through + ", menu shut " + menuShut + ", back when shown " + shown + ", not for a non-game " + notGame);
                checks.Add("PASS game aim: with a game in front hiding its cursor, every window of hers lets clicks through and takes no focus, and a right click doesn't open her menu; the cursor showing again or no game in front gives her clicks back");
            }
            finally
            {
                Hands.Foreground = foregroundWas;
                Computer.Glimpse = glimpseWas;
                GameCursor.Read = readWas;
                GameCursor.ProcessAt = processWas;
                GameCursor.Held = heldWas;
                aimFront = IntPtr.Zero;
                Aim(false);
                menu.Hide();
            }
        }
#endif
    }
}
