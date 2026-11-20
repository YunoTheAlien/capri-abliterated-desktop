using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace CapriDesktop
{
    internal static class SpeechAudio
    {
        private static readonly object gate = new object();
        private static Process playing;
        private static int generation, preparing = -1;
        private static long microphoneAfter;
        internal static event Action<string> Failed;
        // The PipeWire node name of the speaker picked under VOICE (Saved.SpeakerNode), or null for the system's default.
        internal static string Device;
        // pacat when PulseAudio's tools are installed, else PipeWire's own pw-play: Ubuntu 26.04 ships PipeWire without
        // pulseaudio-utils, so pacat alone meant she couldn't be heard on a stock install.
        internal static string[] PlayArguments(string tool, int rate, string device)
        {
            bool pipe = Path.GetFileName(tool) == "pw-play";
            var args = pipe ? new List<string> { "--raw", "--format=s16", "--channels=1", "--rate=" + rate }
                : new List<string> { "--playback", "--raw", "--format=s16le", "--channels=1", "--rate=" + rate, "--client-name=Capri", "--stream-name=Capri voice", "--latency-msec=40", "--process-time-msec=20" };
            if (!string.IsNullOrEmpty(device)) args.Add((pipe ? "--target=" : "--device=") + device);
            if (pipe) args.Add("-");
            return args.ToArray();
        }
        // Subscribers revoke microphone authority immediately; no IPC/device wait belongs in this callback.
        internal static event Action BeforePlayback;
        internal static bool Busy { get { lock (gate) return preparing >= 0 || playing != null; } }
        internal static bool MicrophoneHeld { get { lock (gate) return preparing >= 0 || playing != null || Stopwatch.GetTimestamp() < microphoneAfter; } }
        private static void HoldTail() { microphoneAfter = Math.Max(microphoneAfter, Stopwatch.GetTimestamp() + (long)(0.6 * Stopwatch.Frequency)); }
        internal static ProcessStartInfo Start(string tool, IEnumerable<string> args)
        {
            var info = new ProcessStartInfo(tool) { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true };
            foreach (string arg in args) info.ArgumentList.Add(arg);
            return info;
        }
        internal static void Play(byte[] wav)
        {
            int expected;
            lock (gate) { Stop(); expected = generation; preparing = expected; }
            try
            {
                // Hold is reserved before the gate callback, including slow decoding/device startup.
                BeforePlayback?.Invoke();
                int rate; short[] samples = Voice.Samples(wav, out rate);
                if (samples.Length == 0) throw new VoiceException("That voice returned no PCM audio.");
                byte[] bytes = new byte[samples.Length * 2]; Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
                string tool = Ears.Find("pacat") ?? Ears.Find("pw-play");
                if (tool == null) { Failed?.Invoke("Audio playback failed: install pacat (PulseAudio tools) or pw-play (PipeWire)."); return; }
                var info = Start(tool, PlayArguments(tool, rate, Device)); info.RedirectStandardInput = true;
                Process process; int ticket;
                lock (gate)
                {
                    if (expected != generation) return;
                    ticket = generation;
                    try { process = Process.Start(info); playing = process; preparing = -1; }
                    catch (Exception error) { Failed?.Invoke("Audio playback failed: " + error.Message); return; }
                }
                Task.Run(async () =>
                {
                    try
                    {
                        var error = process.StandardError.ReadToEndAsync();
                        await process.StandardInput.BaseStream.WriteAsync(bytes); process.StandardInput.Close();
                        await process.WaitForExitAsync();
                        if (process.ExitCode != 0) throw new VoiceException(Voice.Short(await error));
                    }
                    catch (Exception error) { lock (gate) { if (ticket == generation) Failed?.Invoke("Audio playback failed: " + error.Message); } }
                    finally { lock (gate) { if (playing == process) { playing = null; HoldTail(); } } process.Dispose(); }
                });
            }
            finally
            {
                lock (gate) if (preparing == expected) { preparing = -1; HoldTail(); }
            }
        }
        internal static void Stop()
        {
            lock (gate)
            {
                generation++;
                if (preparing >= 0 || playing != null) HoldTail();
                preparing = -1;
                if (playing == null) return;
                try { if (!playing.HasExited) playing.Kill(true); } catch (InvalidOperationException) { }
                playing = null;
            }
        }
    }
}
