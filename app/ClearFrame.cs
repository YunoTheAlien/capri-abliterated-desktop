using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CapriDesktop
{
    // Her capture window drawn through Direct3D 11 with her alpha, so OBS's Game Capture with "Allow Transparency" takes
    // her with nothing behind her, as it does VTube Studio. COM through its vtables, as Frames.cs does.
    internal sealed class ClearFrame : IDisposable
    {
        private IntPtr swap, device, context, back, staging;
        private int wide, high;
        private readonly IntPtr window;

        internal ClearFrame(IntPtr window) { this.window = window; }

        // For the checks: acts as a PC whose Direct3D won't start, as a machine with no graphics card's can't.
        internal static bool Refused;

        internal Size Size { get { return new Size(wide, high); } }

        // Made for this size, or again when the window changed size. Throws when Direct3D can't start.
        private void Ready(int width, int height)
        {
            if (swap != IntPtr.Zero && width == wide && height == high) return;
            Drop();
            SwapDesc desc = new SwapDesc
            {
                Width = (uint)width, Height = (uint)height, RateNum = 0, RateDen = 1, Format = Bgra,
                SampleCount = 1, Usage = RenderTargetOutput, BufferCount = 1, Window = window, Windowed = 1, Effect = 0
            };
            int level;
            if (Refused) throw new ExternalException("Direct3D failed", unchecked((int)0x887A0004));
            Check(D3D11CreateDeviceAndSwapChain(IntPtr.Zero, Hardware, IntPtr.Zero, BgraSupport, IntPtr.Zero, 0, SdkVersion, ref desc, out swap, out device, out level, out context));
            Guid texture = TextureId;
            Check(Method<GetBufferFn>(swap, 9)(swap, 0, ref texture, out back));
            wide = width;
            high = height;
        }

        // Her picture, premultiplied, into the back buffer at the window's size. present: shown now; a check reads the
        // buffer first.
        internal void Show(Bitmap picture, bool present = true)
        {
            if (Refused) throw new ExternalException("Direct3D failed", unchecked((int)0x887A0004));
            Ready(picture.Width, picture.Height);
            BitmapData bits = picture.LockBits(new Rectangle(Point.Empty, picture.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try { Method<UpdateFn>(context, 48)(context, back, 0, IntPtr.Zero, bits.Scan0, (uint)bits.Stride, 0); }
            finally { picture.UnlockBits(bits); }
            if (present) Check(Method<PresentFn>(swap, 8)(swap, 0, 0));
        }

        // For the checks: one pixel of the back buffer, as blue, green, red and alpha.
        internal Color Pixel(int x, int y)
        {
            if (swap == IntPtr.Zero || device == IntPtr.Zero) return Color.Empty;
            if (staging == IntPtr.Zero)
            {
                TexDesc desc = new TexDesc { Width = (uint)wide, Height = (uint)high, MipLevels = 1, ArraySize = 1, Format = Bgra, SampleCount = 1, Usage = Staging, CpuAccess = CpuRead };
                Check(Method<CreateTextureFn>(device, 5)(device, ref desc, IntPtr.Zero, out staging));
            }
            Method<CopyFn>(context, 47)(context, staging, back);
            Mapped mapped;
            Check(Method<MapFn>(context, 14)(context, staging, 0, MapRead, 0, out mapped));
            try
            {
                int at = y * (int)mapped.RowPitch + x * 4;
                return Color.FromArgb(Marshal.ReadByte(mapped.Data, at + 3), Marshal.ReadByte(mapped.Data, at + 2), Marshal.ReadByte(mapped.Data, at + 1), Marshal.ReadByte(mapped.Data, at));
            }
            finally { Method<UnmapFn>(context, 15)(context, staging, 0); }
        }

        public void Dispose() { Drop(); }

        private void Drop()
        {
            Release(ref staging);
            Release(ref back);
            Release(ref context);
            Release(ref device);
            Release(ref swap);
            wide = high = 0;
        }

        private static void Release(ref IntPtr value) { if (value != IntPtr.Zero) { Marshal.Release(value); value = IntPtr.Zero; } }

        private static void Check(int result) { if (result < 0) throw new ExternalException("Direct3D failed", result); }

        private static T Method<T>(IntPtr instance, int slot) where T : class
        {
            return (T)(object)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size), typeof(T));
        }

        private const uint Bgra = 87, RenderTargetOutput = 0x20, BgraSupport = 0x20, SdkVersion = 7, Staging = 3, CpuRead = 0x20000;
        private const int Hardware = 1, MapRead = 1;
        private static readonly Guid TextureId = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

        [StructLayout(LayoutKind.Sequential)]
        private struct SwapDesc
        {
            public uint Width, Height, RateNum, RateDen, Format, Scanline, Scaling, SampleCount, SampleQuality, Usage, BufferCount;
            public IntPtr Window;
            public int Windowed;
            public uint Effect, Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TexDesc
        {
            public uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CpuAccess, MiscFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Mapped { public IntPtr Data; public uint RowPitch, DepthPitch; }

        private delegate int GetBufferFn(IntPtr self, uint index, ref Guid id, out IntPtr surface);
        private delegate int PresentFn(IntPtr self, uint sync, uint flags);
        private delegate void UpdateFn(IntPtr self, IntPtr target, uint sub, IntPtr box, IntPtr data, uint rowPitch, uint depthPitch);
        private delegate int CreateTextureFn(IntPtr self, ref TexDesc desc, IntPtr initial, out IntPtr texture);
        private delegate void CopyFn(IntPtr self, IntPtr target, IntPtr source);
        private delegate int MapFn(IntPtr self, IntPtr resource, uint sub, int type, uint flags, out Mapped mapped);
        private delegate void UnmapFn(IntPtr self, IntPtr resource, uint sub);

        [DllImport("d3d11.dll")]
        private static extern int D3D11CreateDeviceAndSwapChain(IntPtr adapter, int driver, IntPtr software, uint flags, IntPtr levels, uint levelCount, uint sdk, ref SwapDesc desc, out IntPtr swap, out IntPtr device, out int level, out IntPtr context);
    }
}

namespace CapriDesktop
{
    internal sealed partial class Companion
    {
        // Transparent: first in the colours, it opens the capture window held, drawn through Direct3D with her alpha (a
        // clear corner stays clear, she stays solid), a colour picked puts her back on it, and closing lets her go.
#if CHECKS
        private void SmokeTransparent(System.Collections.Generic.List<string> checks)
        {
            bool clearWas = streamClear, lockedWas = streamLocked;
            Color colourWas = backdropColour;
            if (streamWindow != null && !streamWindow.IsDisposed) SetStreamWindow();
            try
            {
                int at = -1;
                for (int i = 0; i < backdropItem.DropDownItems.Count; i++) if (backdropItem.DropDownItems[i].Text == "Transparent") { at = i; break; }
                int firstColour = -1;
                for (int i = 0; i < backdropItem.DropDownItems.Count; i++) if (backdropItem.DropDownItems[i].Tag is Color) { firstColour = i; break; }
                if (at < 0 || firstColour != at + 1) throw new Exception("Transparent isn't first in the colours: at " + at + ", the first colour at " + firstColour);
                streamLocked = false;
                SetTransparent();
                bool opened = streamWindow != null && !streamWindow.IsDisposed && streamWindow.Clear && streamLocked;
                // Her alpha read from a window of the check's own, which her own frames don't reach.
                Color corner, middle;
                using (StreamWindow own = new StreamWindow(Color.Black))
                using (Bitmap her = new Bitmap(100, 100, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    own.Location = new Point(-4000, -4000);
                    own.Show();
                    own.Clear = true;
                    using (Graphics paint = Graphics.FromImage(her)) { paint.Clear(Color.Transparent); paint.FillRectangle(Brushes.Red, 30, 30, 40, 40); }
                    own.Took(her, new Rectangle(0, 0, 100, 100), null);
                    if (!own.Clear) throw new Exception("Transparent couldn't draw through Direct3D on this PC; see the stream line in the log.");
                    own.ShowClear(false);
                    Size size = own.ClientSize;
                    corner = own.ClearPixel(2, 2);
                    middle = own.ClearPixel(size.Width / 2, size.Height / 2);
                    own.Close();
                }
                bool alpha = corner.A == 0 && middle.A == 255 && middle.R > 200 && middle.G < 40;
                // On a colour she is still drawn through Direct3D, the colour behind her, so Game Capture takes her too.
                Color onColour;
                using (StreamWindow own = new StreamWindow(Color.FromArgb(0, 177, 64)))
                using (Bitmap her = new Bitmap(100, 100, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    own.Location = new Point(-4000, -4000);
                    own.Show();
                    using (Graphics paint = Graphics.FromImage(her)) { paint.Clear(Color.Transparent); paint.FillRectangle(Brushes.Red, 30, 30, 40, 40); }
                    own.Took(her, new Rectangle(0, 0, 100, 100), null);
                    own.ShowClear(false);
                    onColour = own.Drawn3D ? own.ClearPixel(2, 2) : Color.Empty;
                    own.Close();
                }
                bool colour3D = onColour.A == 255 && onColour.G > 150 && onColour.R < 40;
                SetBackdrop(Color.FromArgb(0, 177, 64));
                bool coloured = !streamClear && !streamWindow.Clear && streamWindow.BackColor.ToArgb() == Color.FromArgb(0, 177, 64).ToArgb();
                SetStreamWindow();
                bool letGo = !streamLocked;
                if (!opened || !alpha || !coloured || !letGo || !colour3D)
                    throw new Exception("Transparent went wrong: opened held and clear " + opened + ", alpha kept (corner " + corner + ", middle " + middle + ") " + alpha + ", a colour puts her back on it " + coloured + ", closing lets her go " + letGo + ", on a colour still through Direct3D (" + onColour + ") " + colour3D);
                checks.Add("PASS Transparent sits first in the Streaming window's colours; picked, it opens the capture window held and draws her through Direct3D with her alpha (a clear corner reads alpha 0, she reads solid); a colour puts her back on it, still drawn through Direct3D with the colour behind her so Game Capture takes her on any setting, and closing the window lets her go");
            }
            finally
            {
                if (streamWindow != null && !streamWindow.IsDisposed) SetStreamWindow();
                backdropColour = colourWas;
                Clear(clearWas);
                streamLocked = lockedWas;
                StreamLabel();
            }
        }
#endif
    }
}
