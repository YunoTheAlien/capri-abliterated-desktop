using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace CapriDesktop
{
    // Windows' own "Listen to this device" on a mic or the cable's far end plays it out of a speaker all the time, so a screen
    // share or recording carries it as if a mic were left on. She only reads it and says so; she never changes a device.
    internal static class ListenThrough
    {
        internal sealed class Found
        {
            internal string Mic, Target;
        }

        private const string Audio = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\";
        private const string Listen = "{24dbb0fc-9311-4b3d-9cf0-18ff155639d4},1", ListenTo = "{24dbb0fc-9311-4b3d-9cf0-18ff155639d4},0";
        private const string FullName = "{a45c254e-df1c-4efd-8020-67d146a850e0},14", Description = "{a45c254e-df1c-4efd-8020-67d146a850e0},2", Card = "{b3f8fa53-0004-438e-9003-51a46e139bfc},6";

        // The active recording devices Windows plays out, read from the registry; swapped by the checks.
        internal static Func<List<Found>> Read = ReadWindows;

        private static List<Found> ReadWindows()
        {
            List<Found> found = new List<Found>();
            using (RegistryKey capture = Registry.LocalMachine.OpenSubKey(Audio + "Capture"))
            {
                if (capture == null) return found;
                foreach (string id in capture.GetSubKeyNames())
                    try
                    {
                        using (RegistryKey device = capture.OpenSubKey(id))
                        using (RegistryKey said = device == null ? null : device.OpenSubKey("Properties"))
                        {
                            object state = device == null ? null : device.GetValue("DeviceState");
                            if (said == null || !(state is int) || (int)state != 1 || !On(said.GetValue(Listen) as byte[])) continue;
                            found.Add(new Found { Mic = Named(said) ?? "a recording device", Target = Played(said.GetValue(ListenTo) as string) });
                        }
                    }
                    catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
            return found;
        }

        // A stored VARIANT_BOOL: its type, 11, then its value eight bytes in, nonzero for on.
        internal static bool On(byte[] value) { return value != null && value.Length >= 10 && value[0] == 11 && value[1] == 0 && (value[8] != 0 || value[9] != 0); }

        // A device's name as Windows shows it, like "Microphone (RODECaster Pro II Main Stereo)".
        private static string Named(RegistryKey said)
        {
            string full = said.GetValue(FullName) as string;
            if (!string.IsNullOrEmpty(full)) return full;
            string what = said.GetValue(Description) as string, card = said.GetValue(Card) as string;
            if (string.IsNullOrEmpty(what)) return card;
            return string.IsNullOrEmpty(card) ? what : what + " (" + card + ")";
        }

        // The speaker it plays to, from an id like "{0.0.0.00000000}.{guid}", or Windows' default when none is named.
        private static string Played(string target)
        {
            if (string.IsNullOrEmpty(target)) return "your default speakers";
            int dot = target.LastIndexOf('.');
            string id = dot >= 0 ? target.Substring(dot + 1) : target;
            try
            {
                using (RegistryKey said = Registry.LocalMachine.OpenSubKey(Audio + @"Render\" + id + @"\Properties"))
                    return said == null ? "another output" : Named(said) ?? "another output";
            }
            catch (Exception) { return "another output"; }
        }

        internal static string Line(Found found)
        {
            return "Windows plays " + found.Mic + " out of " + found.Target + " (Listen to this device), so screen shares and recordings carry it. Turn it off: Sound settings > Recording > " + found.Mic + " > Properties > Listen.";
        }

        // Each device said once a run.
        private static readonly HashSet<string> told = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // The line for the first device not yet said this run, or null; every one found is logged once.
        internal static string Tell()
        {
            List<Found> found;
            try { found = Read(); }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); return null; }
            string first = null;
            lock (told)
                foreach (Found one in found)
                {
                    if (!told.Add(one.Mic + " > " + one.Target)) continue;
                    CapriDesktop.Brain.Log("mic", -1, "Listen to this device is on: " + one.Mic + " plays out of " + one.Target + ".");
                    if (first == null) first = Line(one);
                }
            return first;
        }

        // Forgets what was said, for the checks.
        internal static void Forget() { lock (told) told.Clear(); }
    }
}
