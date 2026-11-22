using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO.MemoryMappedFiles;
using System.Threading;

namespace CapriDesktop
{
    // Capri as a camera: OBS's virtual camera filter reads NV12 frames from the OBSVirtualCamVideo queue, so we write
    // it and OBS never runs. Header per obs-studio's shared-memory-queue.c (80 bytes: write_idx 0, read_idx 4, state 8,
    // offsets 12, type 24, cx 28, cy 32, interval 40), then three 32-byte-aligned NV12 slots.
    internal static class VirtualCamera
    {
        private const string Mapping = "OBSVirtualCamVideo";
        private const int Slots = 3;
        private const int SlotHeader = 32;
        private const int HeaderSize = 80;

        private const uint StateStarting = 1;
        private const uint StateReady = 2;
        private const uint StateStopping = 3;

        private const int OffWriteIdx = 0;
        private const int OffReadIdx = 4;
        private const int OffState = 8;
        private const int OffOffsets = 12;
        private const int OffType = 24;
        private const int OffCx = 28;
        private const int OffCy = 32;
        private const int OffInterval = 40;

        private static readonly object gate = new object();

        private static MemoryMappedFile map;
        private static MemoryMappedViewAccessor view;
        private static Thread pump;
        private static volatile bool running;
        // Capri Camera, her own camera device, when it is installed: the same picture goes to it instead of OBS's queue,
        // so OBS itself can take her as a video source, which it cannot do with its own virtual camera.
        private static CapriCamera own;

        internal static bool OwnReady { get { try { return CapriCamera.State == "ready"; } catch (Exception) { return false; } } }

        // The camera name to pick in Discord or OBS while she is on.
        internal static string DeviceName { get { return own != null ? "Capri Camera" : "OBS Virtual Camera"; } }

        private static int width, height, rate;
        private static readonly int[] slotAt = new int[Slots];
        private static uint written;

        // The mapping's own base address. The frame is converted straight into shared memory, so nothing the size of a
        // frame is ever copied twice, and nothing goes through the accessor's bounds-checked marshalling at 30fps.
        private static unsafe byte* shared;
        private static bool held;

        // Everything that makes up one picture of her, with each screen rectangle: her body and every overlay with it,
        // since her bubble, clones and emotes are separate windows that would otherwise never reach the camera.
        private sealed class Shot : IDisposable
        {
            internal readonly List<Bitmap> Parts = new List<Bitmap>();
            internal readonly List<Rectangle> Where = new List<Rectangle>();
            internal Rectangle Body;

            internal Rectangle All()
            {
                Rectangle all = Body;
                for (int part = 0; part < Where.Count; part++)
                    all = all.IsEmpty ? Where[part] : Rectangle.Union(all, Where[part]);
                return all;
            }

            public void Dispose()
            {
                foreach (Bitmap one in Parts) try { if (one != null) one.Dispose(); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                Parts.Clear();
                Where.Clear();
            }
        }

        private static Shot latest;      // the last finished picture, owned by us
        private static Shot building;    // the one the UI thread is filling in
        private static Color behind = Color.FromArgb(0, 177, 64);
        private static bool transparent;

        // The piece of the desktop the camera is looking at, eased toward where it should be so the shot does not jump
        // every time the bubble appears and disappears.
        private static RectangleF watching = RectangleF.Empty;

        internal static bool On { get { return running; } }

        // True when Capri Camera is installed or OBS's filter is registered, which is what decides whether this is
        // available at all.
        internal static bool Available { get { return OwnReady || ObsAvailable; } }

        internal static bool ObsAvailable
        {
            get
            {
                try
                {
                    using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                        @"SOFTWARE\Classes\CLSID\{860BB310-5D01-11d0-BD3B-00A0C911CE86}\Instance\{A3FCE0F5-3493-419F-958A-ABA1250EC20B}"))
                        return key != null;
                }
                catch (Exception) { return false; }
            }
        }

        private static int Align32(int value) { return (value + 31) & ~31; }

        // Starts the device. Returns null when she is live, or a sentence saying why not.
        internal static unsafe string Start(int wide, int tall, int fps, Color fill, bool clear)
        {
            lock (gate)
            {
                if (running) return null;
                if (!Available) return "No virtual camera driver is registered. Install OBS Studio once and this works without ever opening it.";

                // NV12 is 2x2 subsampled, so both sides have to be even.
                width = Math.Max(160, wide & ~1);
                height = Math.Max(120, tall & ~1);
                rate = Math.Max(5, Math.Min(60, fps));
                behind = fill;
                transparent = clear;

                if (OwnReady)
                {
                    // Her own camera takes 24-bit frames with sides a multiple of 4.
                    width &= ~3;
                    height &= ~3;
                    own = new CapriCamera();
                    written = 0;
                    running = true;
                    pump = new Thread(Pump);
                    pump.IsBackground = true;
                    pump.Name = "Capri virtual camera";
                    pump.Start();
                    Brain.Log("camera", -1, "Capri Camera on at " + width + "x" + height + " " + rate + "fps. Pick \"Capri Camera\" in Discord or OBS.");
                    return null;
                }

                int frameSize = width * height * 3 / 2;
                int size = Align32(HeaderSize);
                for (int slot = 0; slot < Slots; slot++)
                {
                    slotAt[slot] = size;
                    size = Align32(size + SlotHeader + frameSize);
                }

                try
                {
                    // Deliberately CreateNew: if something already owns the queue, OBS is running and it is not ours
                    // to take over.
                    map = MemoryMappedFile.CreateNew(Mapping, size, MemoryMappedFileAccess.ReadWrite);
                    view = map.CreateViewAccessor(0, size, MemoryMappedFileAccess.ReadWrite);
                    byte* at = null;
                    view.SafeMemoryMappedViewHandle.AcquirePointer(ref at);
                    if (at == null) throw new InvalidOperationException("the shared frame buffer could not be addressed");
                    shared = at;
                    held = true;
                }
                catch (Exception bad)
                {
                    Shut();
                    if (bad is System.IO.IOException)
                        return "OBS already has the virtual camera. Close OBS, or stop its virtual camera, then try again.";
                    return "The virtual camera would not start: " + bad.Message;
                }

                written = 0;

                view.Write(OffWriteIdx, (uint)0);
                view.Write(OffReadIdx, (uint)0);
                view.Write(OffState, StateStarting);
                for (int slot = 0; slot < Slots; slot++) view.Write(OffOffsets + slot * 4, (uint)slotAt[slot]);
                view.Write(OffType, (uint)0);
                view.Write(OffCx, (uint)width);
                view.Write(OffCy, (uint)height);
                view.Write(OffInterval, (ulong)(10000000L / rate));

                running = true;
                pump = new Thread(Pump);
                pump.IsBackground = true;
                pump.Name = "Capri virtual camera";
                pump.Start();

                Brain.Log("camera", -1, "Virtual camera on at " + width + "x" + height + " " + rate + "fps. Pick \"OBS Virtual Camera\" in Discord.");
                return null;
            }
        }

        internal static void Stop()
        {
            Thread ending;
            lock (gate)
            {
                if (!running) return;
                running = false;
                ending = pump;
                // Tell the filter we have gone so it stops showing the last frame forever.
                try { if (view != null) view.Write(OffState, StateStopping); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
            try { if (ending != null) ending.Join(400); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            lock (gate) { Shut(); }
            Brain.Log("camera", -1, "Virtual camera off.");
        }

        private static unsafe void Shut()
        {
            CapriCamera mine = own;
            own = null;
            try { if (mine != null) mine.Dispose(); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            try { if (held && view != null) view.SafeMemoryMappedViewHandle.ReleasePointer(); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            held = false;
            shared = null;
            try { if (view != null) view.Dispose(); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            try { if (map != null) map.Dispose(); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            view = null;
            map = null;
            pump = null;
            watching = RectangleF.Empty;
            Shot had = latest, part = building;
            latest = null;
            building = null;
            try { if (had != null) had.Dispose(); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            try { if (part != null) part.Dispose(); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        internal static void Behind(Color fill, bool clear)
        {
            lock (gate) { behind = fill; transparent = clear; }
        }

        // Her body, and where it sits on the screen. Starts a new picture: call Layer for each overlay that goes with
        // it, then Done. Cheap on the caller: one copy per part, no conversion.
        internal static void Frame(Bitmap body, Rectangle at)
        {
            if (!running) return;
            Shot made = new Shot { Body = at };
            if (!Add(made, body, at)) { made.Dispose(); return; }
            Shot dropped;
            lock (gate) { dropped = building; building = made; }
            if (dropped != null) dropped.Dispose();
        }

        // One more layered window that belongs in the picture: the speech bubble, a clone, an emote.
        internal static void Layer(Bitmap image, Rectangle at)
        {
            if (!running) return;
            lock (gate) { if (building != null) Add(building, image, at); }
        }

        internal static void Done()
        {
            Shot old;
            lock (gate)
            {
                if (building == null) return;
                old = latest;
                latest = building;
                building = null;
            }
            if (old != null) old.Dispose();
        }

        private static bool Add(Shot into, Bitmap image, Rectangle at)
        {
            if (image == null || at.Width <= 0 || at.Height <= 0) return false;
            try
            {
                Bitmap copy = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
                using (Graphics pen = Graphics.FromImage(copy))
                {
                    pen.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    pen.DrawImageUnscaled(image, 0, 0);
                }
                into.Parts.Add(copy);
                into.Where.Add(at);
                return true;
            }
            catch (Exception) { return false; }
        }

        private static unsafe void Pump()
        {
            long tick = System.Diagnostics.Stopwatch.Frequency / rate;
            System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
            long due = 0;

            using (Bitmap canvas = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            using (Graphics pen = Graphics.FromImage(canvas))
            {
                pen.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                pen.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                while (running)
                {
                    long now = clock.ElapsedTicks;
                    if (now < due)
                    {
                        int rest = (int)((due - now) * 1000 / System.Diagnostics.Stopwatch.Frequency);
                        if (rest > 0) Thread.Sleep(Math.Min(rest, 100));
                        continue;
                    }
                    due = now + tick;

                    try
                    {
                        Color fill;
                        lock (gate)
                        {
                            fill = behind;
                            pen.Clear(fill);
                            if (latest != null) Compose(pen, latest);
                        }
                        Push(canvas);
                    }
                    catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                }
            }
        }

        // Draws one picture into the canvas, framed on her. Called with the lock held.
        private static void Compose(Graphics pen, Shot shot)
        {
            Rectangle body = shot.Body;
            if (body.Width <= 0 || body.Height <= 0) return;

            // Always keep a generous box around her, so a shot framed on her body alone does not snap wider the moment
            // she speaks. Then take in anything that still sticks out, like a tall bubble.
            float want = Math.Max(body.Width * 2.6f, body.Height * 2.0f);
            RectangleF around = Box(body, want, want * height / width);
            RectangleF all = shot.All();
            if (!around.Contains(all))
            {
                RectangleF joined = RectangleF.Union(around, all);
                joined.Inflate(body.Width * 0.12f, body.Height * 0.08f);
                float wide = Math.Max(joined.Width, joined.Height * width / height);
                around = Box(Rectangle.Round(joined), wide, wide * height / width);
            }

            // Eased, so the framing drifts rather than snapping between shots.
            if (watching.IsEmpty || watching.Width <= 0) watching = around;
            else
            {
                const float ease = 0.22f;
                watching = new RectangleF(
                    watching.X + (around.X - watching.X) * ease,
                    watching.Y + (around.Y - watching.Y) * ease,
                    watching.Width + (around.Width - watching.Width) * ease,
                    watching.Height + (around.Height - watching.Height) * ease);
            }
            if (watching.Width <= 1 || watching.Height <= 1) return;

            float scale = width / watching.Width;
            for (int part = 0; part < shot.Parts.Count; part++)
            {
                Bitmap one = shot.Parts[part];
                Rectangle spot = shot.Where[part];
                if (one == null) continue;
                pen.DrawImage(one,
                    (spot.X - watching.X) * scale,
                    (spot.Y - watching.Y) * scale,
                    spot.Width * scale,
                    spot.Height * scale);
            }
        }

        // A box of the given size centred on what it is framing.
        private static RectangleF Box(Rectangle on, float wide, float tall)
        {
            return new RectangleF(on.X + on.Width / 2f - wide / 2f, on.Y + on.Height / 2f - tall / 2f, wide, tall);
        }

        private static unsafe void Push(Bitmap canvas)
        {
            CapriCamera mine = own;
            if (mine != null)
            {
                if (!running) return;
                int w = canvas.Width & ~3, h = canvas.Height & ~3;
                if (w < 4 || h < 4) return;
                byte[] bgr = new byte[w * h * 3];
                System.Drawing.Imaging.BitmapData data = canvas.LockBits(new Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                try { for (int y = 0; y < h; y++) System.Runtime.InteropServices.Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), bgr, y * w * 3, w * 3); }
                finally { canvas.UnlockBits(data); }
                mine.SendRaw(bgr, w, h);
                return;
            }
            lock (gate)
            {
                if (!running || shared == null) return;
                uint inc = written + 1;
                int slot = (int)(inc % Slots);
                byte* at = shared + slotAt[slot];
                try
                {
                    // Nanoseconds since she started, which is the clock OBS's own writer uses.
                    *(ulong*)at = (ulong)(System.Diagnostics.Stopwatch.GetTimestamp() * (1000000000.0 / System.Diagnostics.Stopwatch.Frequency));
                    ToNv12(canvas, at + SlotHeader);

                    // The frame has to be all the way down before the index points at it, or the filter can read a
                    // half-written slot and tear.
                    Thread.MemoryBarrier();
                    *(uint*)(shared + OffWriteIdx) = inc;
                    *(uint*)(shared + OffReadIdx) = inc;
                    *(uint*)(shared + OffState) = StateReady;
                    written = inc;
                }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
        }

        // BGRA to NV12, straight into the shared frame slot. Full-rate luma, chroma from the top-left of each 2x2
        // block, which is what every cheap webcam does and is invisible on a cartoon character.
        private static unsafe void ToNv12(Bitmap source, byte* into)
        {
            BitmapData locked = source.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                byte* top = (byte*)locked.Scan0;
                int stride = locked.Stride;
                int luma = width * height;

                for (int y = 0; y < height; y++)
                {
                    byte* line = top + (long)y * stride;
                    byte* yOut = into + (long)y * width;
                    bool evenRow = (y & 1) == 0;
                    byte* uvOut = into + luma + (long)(y >> 1) * width;

                    for (int x = 0; x < width; x++)
                    {
                        byte* pixel = line + (x << 2);
                        int b = pixel[0], g = pixel[1], r = pixel[2];

                        yOut[x] = (byte)(((66 * r + 129 * g + 25 * b + 128) >> 8) + 16);

                        if (evenRow && (x & 1) == 0)
                        {
                            int u = ((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128;
                            int v = ((112 * r - 94 * g - 18 * b + 128) >> 8) + 128;
                            uvOut[x] = (byte)(u < 0 ? 0 : u > 255 ? 255 : u);
                            uvOut[x + 1] = (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
                        }
                    }
                }
            }
            finally { source.UnlockBits(locked); }
        }
    }
}
