using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Threading;
using Avalonia.Threading;

namespace CapriDesktop
{
    internal sealed partial class Settings
    {
        // The PipeWire sink her voice plays on, picked under Mainframe > VOICE > Her speaker; null follows the system default.
        [DataMember(EmitDefaultValue = false)] internal string SpeakerNode;
    }

    // Mainframe > VOICE > Mic (with Test) and Her speaker, as on Windows. The mic is the one saved for Listen for Capri
    // (MicrophoneNode and its fingerprint), so TALK, the name listener and the test all hear the same one; Default leaves
    // TALK on the system's default. Her speaker is where her voice plays. Both lists come from PipeWire's pw-dump.
    internal sealed partial class Pet
    {
        private string[] micNodes = { null }, micPrints = { null }, micNames = { "Default" };
        private string[] speakerNodes = { null }, speakerNames = { "Default" };
        private int deviceListing;
        private Process micTest;
        private int micTestTicket;

        internal string[] MainframeMics { get { return micNames; } }
        internal string[] MainframeSpeakers { get { return speakerNames; } }
        internal int MainframeMicIndex { get { return Math.Max(0, Array.IndexOf(micNodes, Saved.MicrophoneNode)); } }
        internal int MainframeSpeakerIndex { get { return Math.Max(0, Array.IndexOf(speakerNodes, Saved.SpeakerNode)); } }
        internal string MainframeMicLabel { get { return "Mic: " + (Saved.MicrophoneNode == null ? "default" : Named(micNodes, micNames, Saved.MicrophoneNode)); } }
        internal string MainframeSpeakerLabel { get { return "Her speaker: " + (Saved.SpeakerNode == null ? "Default" : Named(speakerNodes, speakerNames, Saved.SpeakerNode)); } }
        // 0 to 1 while the mic test runs, else -1.
        internal double MicLevel { get; private set; } = -1;
        internal bool MicTesting { get { return micTest != null; } }

        // A saved device that isn't plugged in now keeps its place and says so, as Windows' "(missing)" does.
        private static string Named(string[] nodes, string[] names, string node)
        {
            int at = Array.IndexOf(nodes, node);
            return at > 0 ? names[at] : node + " (missing)";
        }

        private void ApplyAudioDevices()
        {
            Ears.Device = Saved.MicrophoneNode;
            SpeechAudio.Device = Saved.SpeakerNode;
        }

        internal void ListAudioDevices()
        {
            if (!OperatingSystem.IsLinux() || Program.Smoke != null) return;
            int ticket = ++deviceListing;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    byte[] inventory = ComputerProcess.Bytes("/usr/bin/pw-dump", Array.Empty<string>(), CancellationToken.None, 5000, 4 * 1024 * 1024);
                    var mics = PhysicalMics(inventory);
                    var speakers = AudioOutputs(inventory);
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (ticket != deviceListing) return;
                        micNodes = new string[mics.Count + 1]; micPrints = new string[mics.Count + 1]; micNames = new string[mics.Count + 1];
                        micNames[0] = "Default";
                        for (int i = 0; i < mics.Count; i++) { micNodes[i + 1] = mics[i][0]; micPrints[i + 1] = mics[i][1]; micNames[i + 1] = mics[i][2]; }
                        speakerNodes = new string[speakers.Count + 1]; speakerNames = new string[speakers.Count + 1];
                        speakerNames[0] = "Default";
                        for (int i = 0; i < speakers.Count; i++) { speakerNodes[i + 1] = speakers[i][0]; speakerNames[i + 1] = speakers[i][1]; }
                        NotifySettings();
                    });
                }
                catch (Exception error) when (error is IOException || error is InvalidOperationException || error is JsonException || error is System.ComponentModel.Win32Exception || error is OperationCanceledException) { }
            });
        }

        // The physical mics the name listener accepts, through the same filter it uses: node, fingerprint, description.
        private static List<string[]> PhysicalMics(byte[] inventory)
        {
            var found = new List<string[]>();
            ProcessStartInfo info = ComputerProcess.Start("/usr/bin/python3", new[] { Path.Combine(AppContext.BaseDirectory, "bridge/speech/sources.py") });
            using Process filter = Process.Start(info);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var abort = timeout.Token.Register(() => Bridge.Kill(filter));
            var read = filter.StandardOutput.ReadToEndAsync();
            var errors = filter.StandardError.ReadToEndAsync();
            filter.StandardInput.BaseStream.Write(inventory); filter.StandardInput.Close();
            filter.WaitForExit();
            if (filter.ExitCode != 0) return found;
            using JsonDocument rows = JsonDocument.Parse(read.GetAwaiter().GetResult());
            foreach (JsonElement row in rows.RootElement.EnumerateArray())
            {
                if (found.Count >= 16) break;
                found.Add(new[] { row.GetProperty("node").GetString(), row.GetProperty("fingerprint").GetString(), row.GetProperty("description").GetString() });
            }
            return found;
        }

        // Every output PipeWire offers her voice: node name and the description people see in their sound settings.
        private static List<string[]> AudioOutputs(byte[] inventory)
        {
            var found = new List<string[]>();
            using JsonDocument rows = JsonDocument.Parse(inventory);
            if (rows.RootElement.ValueKind != JsonValueKind.Array) return found;
            foreach (JsonElement row in rows.RootElement.EnumerateArray())
            {
                if (found.Count >= 16) break;
                if (!row.TryGetProperty("type", out JsonElement type) || type.GetString() != "PipeWire:Interface:Node") continue;
                if (!row.TryGetProperty("info", out JsonElement info) || info.ValueKind != JsonValueKind.Object) continue;
                if (!info.TryGetProperty("props", out JsonElement props) || props.ValueKind != JsonValueKind.Object) continue;
                if (!props.TryGetProperty("media.class", out JsonElement kind) || kind.GetString() != "Audio/Sink") continue;
                string name = props.TryGetProperty("node.name", out JsonElement n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                if (string.IsNullOrEmpty(name) || name.StartsWith("-", StringComparison.Ordinal) || name.Length > 256) continue;
                string shown = props.TryGetProperty("node.description", out JsonElement d) && d.ValueKind == JsonValueKind.String && d.GetString().Length > 0 ? d.GetString() : name;
                found.Add(new[] { name, shown });
            }
            return found;
        }

        internal void PickMic(int index)
        {
            if (index < 0 || index >= micNodes.Length) return;
            if (micTest != null) StopMicTest();
            Saved.MicrophoneNode = micNodes[index];
            Saved.MicrophoneFingerprint = micPrints[index];
            Ears.Device = Saved.MicrophoneNode;
            Save();
            // Listen for Capri follows the picked mic; with Default it waits to be given one, as it did before.
            if (OperatingSystem.IsLinux() && Program.Smoke == null && Saved.ListenCapri)
            {
                if (Saved.MicrophoneNode != null) StartNameListening();
                else StopNameListening("Pick a mic under VOICE for Listen for Capri.");
            }
            NotifySettings();
        }

        internal void PickSpeaker(int index)
        {
            if (index < 0 || index >= speakerNodes.Length) return;
            Saved.SpeakerNode = speakerNodes[index];
            SpeechAudio.Device = Saved.SpeakerNode;
            Save();
            NotifySettings();
        }

        // Windows' TestMic: ten seconds of the picked mic's level on its row, and Stop ends it sooner.
        internal void TestMic()
        {
            if (micTest != null) { StopMicTest(); return; }
            string tool = Ears.RecorderTool();
            if (tool == null) { voiceProblem = "Install parec (PulseAudio tools) or pw-record (PipeWire) so I can hear your mic."; NotifySettings(); return; }
            var info = new ProcessStartInfo(tool) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (string arg in Ears.RecordArguments(tool, Saved.MicrophoneNode)) info.ArgumentList.Add(arg);
            Process run;
            try { run = Process.Start(info); }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception || error is InvalidOperationException) { voiceProblem = "The mic test couldn't start: " + error.Message; NotifySettings(); return; }
            if (run == null) return;
            micTest = run;
            int ticket = ++micTestTicket;
            MicLevel = 0;
            NotifySettings();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var buffer = new byte[3200];
                var clock = Stopwatch.StartNew();
                try
                {
                    _ = run.StandardError.ReadToEndAsync();
                    Stream audio = run.StandardOutput.BaseStream;
                    while (clock.Elapsed.TotalSeconds < 10)
                    {
                        int got = 0;
                        while (got < buffer.Length) { int read = audio.Read(buffer, got, buffer.Length - got); if (read <= 0) break; got += read; }
                        if (got < 2) break;
                        int peak = 0;
                        for (int i = 0; i + 1 < got; i += 2) peak = Math.Max(peak, Math.Abs((int)(short)(buffer[i] | buffer[i + 1] << 8)));
                        double level = Math.Min(1, peak / 32768.0);
                        Dispatcher.UIThread.Post(() => { if (ticket != micTestTicket) return; MicLevel = level; NotifySettings(); });
                    }
                }
                catch (Exception error) when (error is IOException || error is ObjectDisposedException || error is InvalidOperationException) { }
                Dispatcher.UIThread.Post(() => { if (ticket == micTestTicket) StopMicTest(); });
            });
        }

        internal void StopMicTest()
        {
            micTestTicket++;
            Process run = micTest;
            micTest = null;
            MicLevel = -1;
            if (run != null)
            {
                try { if (!run.HasExited) run.Kill(); } catch (InvalidOperationException) { }
                run.Dispose();
            }
            NotifySettings();
        }

        // The level as a bar of ten, for the mic row while the test runs.
        internal static string LevelBar(double level)
        {
            int lit = (int)Math.Round(Math.Clamp(level, 0, 1) * 10);
            return "Mic level " + new string('|', lit) + new string('.', 10 - lit);
        }
    }
}
