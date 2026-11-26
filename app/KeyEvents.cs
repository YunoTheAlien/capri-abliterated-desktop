using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Her mouse-button keys as Windows reports them (Raw Input), only while one is bound. A button going down or up calls
    // Changed; a move or the wheel doesn't. The keyboard is never listened to.
    internal sealed class KeyEvents : NativeWindow, IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Device { internal ushort Page, Usage; internal uint Flags; internal IntPtr Target; }

        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterRawInputDevices(Device[] devices, uint count, uint size);
        [DllImport("user32.dll")] private static extern uint GetRawInputData(IntPtr input, uint command, IntPtr data, ref uint size, uint headerSize);

        private const int Input = 0x00FF;
        private const uint Sink = 0x00000100, Remove = 0x00000001, RidInput = 0x10000003;
        private const ushort Generic = 1, Mouse = 2;

        internal Action Changed;
        private bool mouse;
        // Button changes seen, for the checks.
        internal int Seen;

        internal KeyEvents()
        {
            // A message-only window: never shown, never on the taskbar, there only to be told.
            CreateHandle(new CreateParams { Parent = new IntPtr(-3) });
        }

        internal bool Listening { get { return mouse; } }

        // The mouse while a key of hers is on a mouse button; otherwise nothing.
        internal void Listen(bool toMouse)
        {
            if (Handle == IntPtr.Zero) return;
            if (toMouse != mouse) Register(Mouse, toMouse);
            mouse = toMouse;
        }

        private void Register(ushort usage, bool on)
        {
            Device[] one = { new Device { Page = Generic, Usage = usage, Flags = on ? Sink : Remove, Target = on ? Handle : IntPtr.Zero } };
            RegisterRawInputDevices(one, 1, (uint)Marshal.SizeOf(typeof(Device)));
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == Input && Changed != null && Matters(message.LParam))
            {
                Seen++;
                Changed();
            }
            base.WndProc(ref message);
        }

        // A mouse button going down or up, not the mouse only moving or its wheel.
        private static bool Matters(IntPtr input)
        {
            uint header = (uint)(8 + 2 * IntPtr.Size), size = 0;
            if (GetRawInputData(input, RidInput, IntPtr.Zero, ref size, header) != 0 || size == 0) return false;
            IntPtr data = Marshal.AllocHGlobal((int)size);
            try
            {
                if (GetRawInputData(input, RidInput, data, ref size, header) != size) return false;
                int type = Marshal.ReadInt32(data);
                return Counts(type, type == 0 ? (ushort)Marshal.ReadInt16(data, (int)header + 4) : (ushort)0);
            }
            finally { Marshal.FreeHGlobal(data); }
        }

        // From what Windows reported: a mouse (type 0) button going down or up counts; the mouse only moving or its wheel,
        // or any other device, doesn't. Nothing of it is kept.
        internal static bool Counts(int type, ushort buttons) { return type == 0 && (buttons & 0x03FF) != 0; }

        public void Dispose()
        {
            Listen(false);
            if (Handle != IntPtr.Zero) DestroyHandle();
        }
    }
}
