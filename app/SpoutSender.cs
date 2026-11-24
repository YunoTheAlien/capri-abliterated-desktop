using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CapriDesktop
{
    // Transparent through Spout2: her picture with its alpha, sent as "Capri" for OBS's Spout2 plugin when it's here.
    // SpoutLibrary.dll (2.007.018, BSD 2-Clause) rides in her exe; its interface is called by vtable slot.
    internal sealed class SpoutSender : IDisposable
    {
        internal const string Name = "Capri";
        private const string Resource = "Capri.Spout.SpoutLibrary.dll";

        // The calls made on the library; a check swaps in a stand-in.
        internal interface ICalls
        {
            bool Open();
            void Name(string name);
            bool Send(IntPtr pixels, int width, int height);
            bool Ready();
            void Release();
            void Close();
        }

        internal static Func<ICalls> Calls = delegate { return new Library(); };

        // Whether OBS's Spout2 plugin is on this PC; a check swaps it.
        internal static Func<bool> PluginFound = FindPlugin;

        private ICalls calls;

        private SpoutSender(ICalls calls) { this.calls = calls; }

        // A sender, or null when there is no plugin, no 64-bit Windows under her, or the library won't start: then the
        // Direct3D route is all there is.
        internal static SpoutSender Start(out string why)
        {
            why = null;
            try
            {
                if (!PluginFound()) { why = "OBS's Spout2 plugin isn't on this PC"; return null; }
                ICalls made = Calls();
                if (made == null || !made.Open()) { why = "Spout couldn't start"; return null; }
                made.Name(Name);
                return new SpoutSender(made);
            }
            catch (Exception error)
            {
                if (!(error is DllNotFoundException || error is BadImageFormatException || error is EntryPointNotFoundException || error is IOException || error is UnauthorizedAccessException || error is ExternalException)) throw;
                why = "Spout couldn't start: " + error.Message;
                return null;
            }
        }

        internal bool Sending { get { return calls != null && calls.Ready(); } }

        // Her picture with straight alpha, top row first, at its own size.
        internal bool Send(Bitmap picture)
        {
            if (calls == null || picture == null) return false;
            BitmapData bits = picture.LockBits(new Rectangle(Point.Empty, picture.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try { return calls.Send(bits.Scan0, picture.Width, picture.Height); }
            finally { picture.UnlockBits(bits); }
        }

        public void Dispose()
        {
            ICalls had = calls;
            calls = null;
            if (had == null) return;
            try { had.Release(); had.Close(); }
            catch (Exception caughtOne) { Brain.Caught(caughtOne); }
        }

        // OBS's Spout2 plugin, by its file: in OBS's own plugin folder or the shared one.
        private static bool FindPlugin()
        {
            foreach (string folder in ObsFolders())
                if (File.Exists(Path.Combine(folder, "obs-plugins", "64bit", "win-spout.dll"))) return true;
            string shared = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "obs-studio", "plugins", "win-spout", "bin", "64bit", "win-spout.dll");
            return File.Exists(shared);
        }

        private static System.Collections.Generic.List<string> ObsFolders()
        {
            System.Collections.Generic.List<string> found = new System.Collections.Generic.List<string>();
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (RegistryKey obs = root.OpenSubKey(@"SOFTWARE\OBS Studio"))
                    {
                        string path = obs == null ? null : obs.GetValue(null) as string;
                        if (!string.IsNullOrEmpty(path) && !found.Contains(path)) found.Add(path);
                    }
                }
                catch (Exception caughtOne) { Brain.Caught(caughtOne); }
            }
            string usual = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "obs-studio");
            if (!found.Contains(usual)) found.Add(usual);
            return found;
        }

        // SpoutLibrary.dll as built into her, laid once beside her exe and loaded from there.
        private sealed class Library : ICalls
        {
            private IntPtr spout;

            public bool Open()
            {
                if (IntPtr.Size != 8) return false;
                string path = Lay();
                if (path == null || LoadLibrary(path) == IntPtr.Zero) return false;
                spout = GetSpout();
                return spout != IntPtr.Zero && Method<CreateFn>(98)(spout, IntPtr.Zero);
            }

            public void Name(string name) { Method<NameFn>(0)(spout, name); }

            public bool Send(IntPtr pixels, int width, int height) { return Method<SendFn>(5)(spout, pixels, (uint)width, (uint)height, BgraFormat, false); }

            public bool Ready() { return spout != IntPtr.Zero && Method<ReadyFn>(6)(spout); }

            public void Release() { if (spout != IntPtr.Zero) Method<ReleaseFn>(2)(spout, 0); }

            // The library's own object lives as long as she does; it is only its window and context that go.
            public void Close() { if (spout != IntPtr.Zero) Method<CloseFn>(99)(spout); }

            private T Method<T>(int slot) where T : class
            {
                return (T)(object)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(spout), slot * IntPtr.Size), typeof(T));
            }

            private static string Lay()
            {
                using (Stream from = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource))
                {
                    if (from == null) return null;
                    byte[] bytes = new byte[from.Length];
                    int read = 0;
                    while (read < bytes.Length) { int got = from.Read(bytes, read, bytes.Length - read); if (got <= 0) break; read += got; }
                    // Beside the exe that runs, so a test build never writes into her install; LocalAppData when that
                    // folder can't be written, as in a Store package.
                    foreach (string folder in new[] { Path.Combine(Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath), "spout"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Capri", "spout") })
                    {
                        string path = Path.Combine(folder, "SpoutLibrary.dll");
                        try
                        {
                            if (!File.Exists(path) || !Same(File.ReadAllBytes(path), bytes))
                            {
                                Directory.CreateDirectory(folder);
                                File.WriteAllBytes(path, bytes);
                            }
                            return path;
                        }
                        catch (UnauthorizedAccessException) { }
                        catch (IOException) { }
                    }
                    return null;
                }
            }

            private static bool Same(byte[] one, byte[] two)
            {
                if (one.Length != two.Length) return false;
                for (int i = 0; i < one.Length; i++) if (one[i] != two[i]) return false;
                return true;
            }

            private const uint BgraFormat = 0x80E1;

            [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate void NameFn(IntPtr self, [MarshalAs(UnmanagedType.LPStr)] string name);
            [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate void ReleaseFn(IntPtr self, uint wait);
            [UnmanagedFunctionPointer(CallingConvention.ThisCall)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool SendFn(IntPtr self, IntPtr pixels, uint width, uint height, uint format, [MarshalAs(UnmanagedType.I1)] bool invert);
            [UnmanagedFunctionPointer(CallingConvention.ThisCall)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool ReadyFn(IntPtr self);
            [UnmanagedFunctionPointer(CallingConvention.ThisCall)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool CreateFn(IntPtr self, IntPtr window);
            [UnmanagedFunctionPointer(CallingConvention.ThisCall)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool CloseFn(IntPtr self);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibrary(string path);
            [DllImport("SpoutLibrary.dll")] private static extern IntPtr GetSpout();
        }
    }
}

namespace CapriDesktop
{
    internal sealed partial class Companion
    {
        // With the plugin, a sender named Capri on a stand-in takes her frames and a colour stops it; without it,
        // Direct3D alone. Then the real SpoutLibrary opens a sender, takes a frame and says it is up.
        private void SmokeSpout(System.Collections.Generic.List<string> checks)
        {
            Func<bool> foundWas = SpoutSender.PluginFound;
            Func<SpoutSender.ICalls> callsWas = SpoutSender.Calls;
            bool clearWas = streamClear, lockedWas = streamLocked;
            Color colourWas = backdropColour;
            if (streamWindow != null && !streamWindow.IsDisposed) SetStreamWindow();
            try
            {
                Stand stand = new Stand();
                SpoutSender.PluginFound = delegate { return true; };
                SpoutSender.Calls = delegate { return stand; };
                SetTransparent();
                using (Bitmap her = new Bitmap(80, 60, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                    streamWindow.Took(her, new Rectangle(0, 0, 80, 60), null);
                bool started = streamWindow.Spout != null && stand.Named == SpoutSender.Name && stand.Sent > 0 && stand.Wide >= 80 && stand.High >= 60 && ClearLine == SpoutSaid && streamWindow.Clear;
                SetBackdrop(Color.FromArgb(0, 177, 64));
                bool stopped = stand.Released && stand.Closed && streamWindow.Spout == null;
                SetStreamWindow();
                SpoutSender.PluginFound = delegate { return false; };
                SetTransparent();
                bool fallback = streamWindow.Spout == null && streamWindow.Clear && ClearLine == ClearSaid;
                SetStreamWindow();
                if (!started || !stopped || !fallback)
                    throw new Exception("Transparent through Spout went wrong: started and sending at her size " + started + ", stopped by a colour " + stopped + ", Direct3D alone with no plugin " + fallback);
                checks.Add("PASS Transparent with OBS's Spout2 plugin found starts a sender named Capri, sends her frames, at least her own size, and says to add a Spout2 source; a colour stops it; with no plugin it is the Direct3D window alone, saying Game Capture");

                if (IntPtr.Size != 8) { checks.Add("LIMIT the real SpoutLibrary: this run is 32-bit, and Spout ships 64-bit only"); return; }
                SpoutSender.Calls = callsWas;
                SpoutSender.PluginFound = delegate { return true; };
                string why;
                using (SpoutSender real = SpoutSender.Start(out why))
                {
                    if (real == null) throw new Exception("The real SpoutLibrary didn't start: " + why);
                    bool sent;
                    using (Bitmap her = new Bitmap(64, 64, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                    {
                        using (Graphics paint = Graphics.FromImage(her)) { paint.Clear(Color.Transparent); paint.FillEllipse(Brushes.Magenta, 8, 8, 48, 48); }
                        sent = real.Send(her);
                    }
                    if (!sent || !real.Sending) throw new Exception("The real SpoutLibrary took no frame: sent " + sent + ", up " + real.Sending);
                }
                checks.Add("PASS the real SpoutLibrary (2.007.018) laid from her exe opens a sender, takes a 64 x 64 frame with alpha and reports it up, then closes");
            }
            finally
            {
                SpoutSender.PluginFound = foundWas;
                SpoutSender.Calls = callsWas;
                if (streamWindow != null && !streamWindow.IsDisposed) SetStreamWindow();
                backdropColour = colourWas;
                Clear(clearWas);
                streamLocked = lockedWas;
                StreamLabel();
            }
        }

        private sealed class Stand : SpoutSender.ICalls
        {
            internal string Named;
            internal int Sent, Wide, High;
            internal bool Released, Closed;
            public bool Open() { return true; }
            public void Name(string name) { Named = name; }
            public bool Send(IntPtr pixels, int width, int height) { Sent++; Wide = width; High = height; return true; }
            public bool Ready() { return Sent > 0; }
            public void Release() { Released = true; }
            public void Close() { Closed = true; }
        }
    }
}
