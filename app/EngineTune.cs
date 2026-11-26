using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace CapriDesktop
{
    // One song or sound through Media Foundation's media engine, for a Windows without Windows Media Player, which WPF's
    // MediaPlayer needs. Called by vtable slot, as ClearFrame does, and made on a pool thread so any thread may call it.
    internal sealed class EngineTune : IDisposable
    {
        // Windows Media Player's own library; a Windows that left the player out has none.
        internal static Func<bool> PlayerMissing = delegate { return !File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wmp.dll")); };

        private static readonly object started = new object();
        private static bool up;
        private IntPtr engine;
        private readonly Heard heard;

        private EngineTune() { heard = new Heard(this); }

        internal bool Failed { get { return heard.Failed; } }

        // Ended, or failed; either way nothing more will play.
        internal bool Ended
        {
            get
            {
                if (heard.Failed || heard.Ended) return true;
                IntPtr at = engine;
                return at != IntPtr.Zero && Slot<BoolFn>(at, 27)(at) != 0;
            }
        }

        // Raised once, on a pool thread, when it ends or fails.
        internal event Action Done;

        internal double Position { get { IntPtr at = engine; return at == IntPtr.Zero ? 0 : Seconds(Slot<TimeFn>(at, 16)(at)); } }

        internal double Duration { get { IntPtr at = engine; return at == IntPtr.Zero ? 0 : Seconds(Slot<TimeFn>(at, 19)(at)); } }

        internal double Volume { set { IntPtr at = engine; if (at != IntPtr.Zero) Slot<SetTimeFn>(at, 37)(at, value < 0 ? 0 : value > 1 ? 1 : value); } }

        internal void Seek(double seconds) { IntPtr at = engine; if (at != IntPtr.Zero) Slot<SetTimeFn>(at, 17)(at, seconds < 0 ? 0 : seconds); }

        internal void Pause() { IntPtr at = engine; if (at != IntPtr.Zero) Slot<PlainFn>(at, 33)(at); }

        internal void Resume() { IntPtr at = engine; if (at != IntPtr.Zero) Slot<PlainFn>(at, 32)(at); }

        // Starts the file playing at this volume. Throws when the engine can't be made, so the caller says it couldn't play.
        internal static EngineTune Play(string file, double volume)
        {
            EngineTune made = new EngineTune();
            Exception problem = null;
            ManualResetEvent done = new ManualResetEvent(false);
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { made.Make(new Uri(file).AbsoluteUri, volume); }
                catch (Exception error) { problem = error; }
                finally { done.Set(); }
            });
            if (!done.WaitOne(10000)) problem = new TimeoutException("Media Foundation took too long to start.");
            done.Close();
            if (problem != null) { made.Dispose(); throw problem is ExternalException || problem is TimeoutException || problem is DllNotFoundException ? problem : new ExternalException("Media Foundation couldn't play it: " + problem.Message); }
            return made;
        }

        private void Make(string url, double volume)
        {
            lock (started)
                if (!up) { Check(MFStartup(0x20070, 0)); up = true; }
            Guid factoryClass = FactoryClass, factoryId = FactoryId, callback = CallbackKey;
            IntPtr factory = IntPtr.Zero, attributes = IntPtr.Zero, told = IntPtr.Zero;
            try
            {
                Check(CoCreateInstance(ref factoryClass, IntPtr.Zero, 1, ref factoryId, out factory));
                Check(MFCreateAttributes(out attributes, 1));
                told = Marshal.GetComInterfaceForObject(heard, typeof(IMFMediaEngineNotify));
                Check(Slot<SetUnknownFn>(attributes, 27)(attributes, ref callback, told));
                IntPtr made;
                // Audio only: no window or video device is needed.
                Check(Slot<CreateFn>(factory, 3)(factory, 1, attributes, out made));
                engine = made;
            }
            finally
            {
                if (told != IntPtr.Zero) Marshal.Release(told);
                if (attributes != IntPtr.Zero) Marshal.Release(attributes);
                if (factory != IntPtr.Zero) Marshal.Release(factory);
            }
            Volume = volume;
            IntPtr named = Marshal.StringToBSTR(url);
            try { Check(Slot<SourceFn>(engine, 6)(engine, named)); }
            finally { Marshal.FreeBSTR(named); }
            Check(Slot<PlainFn>(engine, 32)(engine));
        }

        public void Dispose()
        {
            IntPtr at = Interlocked.Exchange(ref engine, IntPtr.Zero);
            if (at == IntPtr.Zero) return;
            try { Slot<PlainFn>(at, 42)(at); }
            finally { Marshal.Release(at); }
        }

        private void Finished()
        {
            Action told = Done;
            if (told != null) ThreadPool.QueueUserWorkItem(delegate { told(); });
        }

        // The engine's news: 5 is an error, 19 the end. Heard on its own threads, so it only notes them.
        [ComVisible(true)]
        private sealed class Heard : IMFMediaEngineNotify
        {
            private readonly EngineTune tune;
            private int finished;
            internal volatile bool Ended, Failed;

            internal Heard(EngineTune tune) { this.tune = tune; }

            public int EventNotify(int happened, IntPtr first, int second)
            {
                if (happened == 5) Failed = true;
                else if (happened == 19) Ended = true;
                else return 0;
                if (Interlocked.Exchange(ref finished, 1) == 0) tune.Finished();
                return 0;
            }
        }

        private static double Seconds(double value) { return double.IsNaN(value) || double.IsInfinity(value) || value < 0 ? 0 : value; }

        private static void Check(int result) { if (result < 0) throw new ExternalException("Media Foundation failed", result); }

        private static T Slot<T>(IntPtr instance, int slot) where T : class
        {
            return (T)(object)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size), typeof(T));
        }

        private static readonly Guid FactoryClass = new Guid("b44392da-499b-446b-a4cb-005fead0e6d5");
        private static readonly Guid FactoryId = new Guid("4d645ace-26aa-4688-9be1-df3516990b93");
        private static readonly Guid CallbackKey = new Guid("c60381b8-83a4-41f8-a3d0-de05076849a9");

        [ComImport, Guid("fee7c112-e776-42b5-9bbf-0048524e2bd5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IMFMediaEngineNotify
        {
            [PreserveSig] int EventNotify(int happened, IntPtr first, int second);
        }

        private delegate int PlainFn(IntPtr self);
        private delegate int BoolFn(IntPtr self);
        private delegate double TimeFn(IntPtr self);
        private delegate int SetTimeFn(IntPtr self, double value);
        private delegate int SourceFn(IntPtr self, IntPtr url);
        private delegate int CreateFn(IntPtr self, int flags, IntPtr attributes, out IntPtr engine);
        private delegate int SetUnknownFn(IntPtr self, ref Guid key, IntPtr value);

        [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFStartup(int version, int flags);
        [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFCreateAttributes(out IntPtr attributes, int size);
        [DllImport("ole32.dll", ExactSpelling = true)] private static extern int CoCreateInstance(ref Guid type, IntPtr outer, int context, ref Guid id, out IntPtr made);
    }
}

namespace CapriDesktop
{
    internal sealed partial class Companion
    {
        // With Windows Media Player taken as missing: her song plays through Media Foundation, at its length, moves on,
        // holds while paused, ends, stops; a file that isn't a song is refused; a mod's sound plays and is let go.
#if CHECKS
        private void SmokeEngineTunes(System.Collections.Generic.List<string> checks, string output)
        {
            Func<bool> missingWas = EngineTune.PlayerMissing;
            double loudWas = FolderTunes.Loudness;
            Directory.CreateDirectory(output);
            string song = Path.Combine(output, "engine-tune.wav"), broken = Path.Combine(output, "engine-broken.mp3");
            using (BinaryWriter wav = new BinaryWriter(File.Create(song)))
            {
                int rate = 8000, samples = rate * 2;
                wav.Write(new[] { 'R', 'I', 'F', 'F' }); wav.Write(36 + samples * 2); wav.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                wav.Write(16); wav.Write((short)1); wav.Write((short)1); wav.Write(rate); wav.Write(rate * 2); wav.Write((short)2); wav.Write((short)16);
                wav.Write(new[] { 'd', 'a', 't', 'a' }); wav.Write(samples * 2);
                for (int i = 0; i < samples; i++) wav.Write((short)(Math.Sin(i * 2 * Math.PI * 440 / rate) * 8000));
            }
            File.WriteAllText(broken, "This isn't a song.");
            EngineTune.PlayerMissing = delegate { return true; };
            try
            {
                FolderTunes.Loudness = 0;
                if (!FolderTunes.Start(song))
                {
                    checks.Add("LIMIT her songs through Media Foundation: its media engine couldn't play here (a Windows N without Media Foundation)");
                    return;
                }
                bool engine = FolderTunes.ByEngine;
                double length = FolderTunes.Seconds;
                Thread.Sleep(600);
                double moved = FolderTunes.At;
                FolderTunes.Pause();
                Thread.Sleep(150);
                double held = FolderTunes.At;
                Thread.Sleep(400);
                double still = FolderTunes.At;
                bool paused = FolderTunes.Paused && FolderTunes.Mode == "playing";
                FolderTunes.Resume();
                for (int waited = 0; waited < 80 && FolderTunes.Mode != "stopped"; waited++) Thread.Sleep(50);
                bool ended = FolderTunes.Mode == "stopped";
                FolderTunes.Stop();
                bool stopped = FolderTunes.Mode == "" && !FolderTunes.ByEngine;
                bool refused = !FolderTunes.Start(broken) && !FolderTunes.ByEngine;
                bool sounded = ModTunes.Play(song, 0);
                for (int waited = 0; waited < 80 && ModTunes.Held > 0; waited++) Thread.Sleep(50);
                bool letGo = ModTunes.Held == 0;
                if (!engine || length < 1.8 || length > 2.2 || moved < 0.2 || !paused || Math.Abs(still - held) > 0.05 || !ended || !stopped || !refused || !sounded || !letGo)
                    throw new Exception("Her songs through Media Foundation went wrong: by the engine " + engine + ", length " + length + ", moved to " + moved + ", paused " + paused + " (" + held + " then " + still + "), ended " + ended + ", stopped " + stopped + ", a broken file refused " + refused + ", a mod's sound played " + sounded + " and let go " + letGo);
                checks.Add("PASS with Windows Media Player missing, her song plays through Media Foundation's engine: its length read, moving, held while paused, ended, stopped; a file that isn't a song is refused; a mod's sound plays and is let go");
            }
            finally
            {
                FolderTunes.Stop();
                EngineTune.PlayerMissing = missingWas;
                FolderTunes.Loudness = loudWas;
                try { File.Delete(song); File.Delete(broken); } catch (IOException) { }
            }
        }
#endif
    }
}
