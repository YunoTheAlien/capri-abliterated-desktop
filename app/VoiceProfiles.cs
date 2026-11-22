using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // A voice per personality and mood, one file each: voices\voiceProfile_<name>.json, easy to copy, share or delete
    // back to stock. Numbers are offsets from her stock voice, so the VOICE sliders still mean what they say.
    internal sealed class VoiceProfile
    {
        // Consts, not Stock's own fields. Stock is a VoiceProfile, so building it runs these initialisers, and
        // reading Stock from inside them reads it before it exists: the type initialiser throws and every use of
        // the class after that fails with it. Reported from another machine as a crash on quit.
        internal const int StockPitch = 2, StockSpeed = 100, StockVolume = 100;

        internal int Pitch = StockPitch;
        internal int Speed = StockSpeed;
        internal int Volume = StockVolume;
        internal int Robot, Glitch;

        internal static readonly VoiceProfile Stock = new VoiceProfile(StockPitch, StockSpeed, StockVolume, 0, 0);

        internal VoiceProfile() { }

        internal VoiceProfile(int pitch, int speed, int volume, int robot, int glitch)
        {
            Pitch = pitch; Speed = speed; Volume = volume; Robot = robot; Glitch = glitch;
        }

        internal bool SameAsStock
        {
            get { return Pitch == StockPitch && Speed == StockSpeed && Volume == StockVolume && Robot == 0 && Glitch == 0; }
        }
    }

    internal static class VoiceProfiles
    {
        internal const string FolderName = "voices";
        private static readonly object gate = new object();
        private static readonly Dictionary<string, VoiceProfile> loaded = new Dictionary<string, VoiceProfile>(StringComparer.OrdinalIgnoreCase);

        internal static string Folder;

        // Where each starts, so nobody invents fourteen voices first; pitch stays between -1 and +4, since lower stops
        // sounding like her. Profiles for packs not in this build come from what attached, else her stock voice.
        internal static Func<string, VoiceProfile> SenpaiVoice;

        private static readonly Dictionary<string, VoiceProfile> Starting = new Dictionary<string, VoiceProfile>(StringComparer.OrdinalIgnoreCase)
        {
            // personalities, soft to harsh
            { "Caregiver",    new VoiceProfile(2,  96, 100, 0, 0) },
            { "Welcome Home", new VoiceProfile(2, 102, 100, 0, 0) },
            { "Devoted",      new VoiceProfile(2,  98,  96, 0, 0) },
            { "Emo",          new VoiceProfile(0,  92,  92, 0, 6) },
            { "Bratty",       new VoiceProfile(3, 112, 102, 0, 0) },
            { "Sassy",        new VoiceProfile(2, 108, 100, 0, 0) },
            { "Feral",        new VoiceProfile(4, 118, 104, 0, 8) },
            // moods, which sit on top of whoever she is
            { "Sleepy",       new VoiceProfile(1,  86,  92, 0, 0) },
            { "Hyper",        new VoiceProfile(4, 124, 104, 0, 0) },
            { "Melancholy",   new VoiceProfile(1,  92,  92, 0, 0) },
            { "Furious",      new VoiceProfile(2, 112, 106, 0, 0) },
            { "Playful",      new VoiceProfile(3, 110, 102, 0, 0) },
            { "Mischievous",  new VoiceProfile(3, 108, 100, 0, 0) },
            { "Affectionate", new VoiceProfile(2,  96, 100, 0, 0) },
            { "Clingy",       new VoiceProfile(3,  98,  98, 0, 0) },
            { "Needy",        new VoiceProfile(3,  98,  96, 0, 0) },
            { "Touchy",       new VoiceProfile(2,  96,  98, 0, 0) },
            { "Smug",         new VoiceProfile(1, 100, 100, 0, 0) },
            { "Moody",        new VoiceProfile(1,  94,  96, 0, 0) },
            { "Jealous",      new VoiceProfile(2, 104,  98, 0, 0) },
            { "Focused",      new VoiceProfile(1,  98,  96, 0, 0) },
        };

        private static string PathOf(string named)
        {
            if (string.IsNullOrEmpty(Folder) || string.IsNullOrEmpty(named)) return null;
            foreach (char bad in Path.GetInvalidFileNameChars()) if (named.IndexOf(bad) >= 0) return null;
            return Path.Combine(Folder, "voiceProfile_" + named + ".json");
        }

        // Theirs if they have saved one, else where that name starts, else her stock voice.
        internal static VoiceProfile For(string named)
        {
            if (string.IsNullOrEmpty(named)) return VoiceProfile.Stock;
            lock (gate)
            {
                VoiceProfile had;
                if (loaded.TryGetValue(named, out had)) return had;
            }
            VoiceProfile found = Read(named) ?? Where(named);
            lock (gate) loaded[named] = found;
            return found;
        }

        internal static VoiceProfile Where(string named)
        {
            VoiceProfile had;
            if (Starting.TryGetValue(named ?? "", out had)) return had;
            if (SenpaiVoice != null)
            {
                try { VoiceProfile theirs = SenpaiVoice(named ?? ""); if (theirs != null) return theirs; }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
            return VoiceProfile.Stock;
        }

        // Written by hand rather than through a serializer, because the file is meant to be readable and editable.
        internal static void Save(string named, VoiceProfile profile)
        {
            string path = PathOf(named);
            if (path == null || profile == null) return;
            try
            {
                Directory.CreateDirectory(Folder);
                StringBuilder made = new StringBuilder();
                made.Append("{").Append(Environment.NewLine);
                made.Append("  \"_\": \"Her voice as ").Append(named).Append(". Measured against her stock voice, so pitch 2 changes nothing.\",").Append(Environment.NewLine);
                made.Append("  \"__\": \"Delete this file to go back to the one she ships with.\",").Append(Environment.NewLine);
                Row(made, "pitch", profile.Pitch, true);
                Row(made, "speed", profile.Speed, true);
                Row(made, "volume", profile.Volume, true);
                Row(made, "robot", profile.Robot, true);
                Row(made, "glitch", profile.Glitch, false);
                made.Append("}").Append(Environment.NewLine);
                File.WriteAllText(path, made.ToString(), new UTF8Encoding(false));
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            lock (gate) loaded[named] = profile;
        }

        private static void Row(StringBuilder made, string name, int value, bool comma)
        {
            made.Append("  \"").Append(name).Append("\": ").Append(value.ToString(CultureInfo.InvariantCulture));
            made.Append(comma ? "," : "").Append(Environment.NewLine);
        }

        internal static void Forget(string named)
        {
            string path = PathOf(named);
            try { if (path != null && File.Exists(path)) File.Delete(path); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            lock (gate) loaded.Remove(named ?? "");
        }

        internal static void Reread() { lock (gate) loaded.Clear(); }

        private static VoiceProfile Read(string named)
        {
            string path = PathOf(named);
            try
            {
                if (path == null || !File.Exists(path)) return null;
                string text = File.ReadAllText(path);
                VoiceProfile start = Where(named);
                return new VoiceProfile(
                    Number(text, "pitch", start.Pitch), Number(text, "speed", start.Speed),
                    Number(text, "volume", start.Volume), Number(text, "robot", start.Robot),
                    Number(text, "glitch", start.Glitch));
            }
            catch (Exception) { return null; }
        }

        private static int Number(string text, string name, int fallback)
        {
            Match found = Regex.Match(text ?? "", "\"" + name + "\"\\s*:\\s*(-?\\d{1,4})", RegexOptions.IgnoreCase);
            int got;
            return found.Success && int.TryParse(found.Groups[1].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out got) ? got : fallback;
        }
    }

    // One line's feeling from a cue before it: "(joyful) You came back." For voices that don't act cues, it shifts that
    // line's pitch, pace and loudness only, pitch by three semitones at most.
    internal static class VoiceFeel
    {
        // cue, pitch in semitones, pace as a percentage, loudness as a percentage.
        private static readonly object[,] Moves =
        {
            { "excited", 2, 112, 104 }, { "thrilled", 2, 114, 105 }, { "elated", 2, 112, 104 },
            { "hyper", 3, 122, 106 }, { "giddy", 3, 118, 104 }, { "eager", 2, 110, 103 },
            { "happy", 2, 106, 102 }, { "joyful", 2, 108, 103 }, { "delighted", 2, 107, 103 },
            { "laughing", 2, 108, 103 }, { "giggling", 2, 110, 102 }, { "chuckling", 1, 104, 100 },
            { "playful", 2, 106, 102 }, { "cheeky", 2, 105, 102 }, { "mischievous", 2, 104, 101 },
            { "surprised", 3, 116, 106 }, { "gasping", 3, 118, 106 },
            { "sad", -2, 90, 96 }, { "unhappy", -2, 92, 96 }, { "disappointed", -1, 92, 96 },
            { "mournful", -2, 86, 93 }, { "wistful", -1, 90, 95 }, { "lonely", -2, 88, 93 },
            { "depressed", -2, 85, 91 }, { "resigned", -1, 90, 94 }, { "bored", -1, 96, 96 },
            { "sleepy", -1, 84, 94 }, { "tired", -1, 86, 95 }, { "yawning", -1, 82, 93 },
            { "sighing", -1, 92, 95 }, { "sobbing", 1, 92, 96 }, { "crying loudly", 1, 96, 108 },
            { "wanting", 1, 92, 97 }, { "needy", 1, 94, 97 }, { "longing", 0, 90, 95 },
            { "breathless", 1, 104, 95 }, { "tender", 1, 94, 97 }, { "affectionate", 1, 96, 99 },
            { "loving", 1, 95, 99 }, { "flirty", 1, 96, 99 }, { "sweetly", 1, 97, 99 },
            { "warmly", 1, 97, 100 }, { "gently", 0, 94, 96 }, { "softly", 0, 94, 88 },
            { "whispering", 0, 94, 70 }, { "soft tone", 0, 95, 82 }, { "quietly", 0, 96, 78 },
            { "shouting", 1, 108, 128 }, { "screaming", 2, 112, 132 }, { "loudly", 1, 104, 122 },
            { "angry", -1, 108, 112 }, { "furious", -1, 112, 118 }, { "frustrated", -1, 106, 108 },
            { "upset", 1, 104, 104 }, { "disgusted", -1, 98, 102 }, { "contemptuous", -1, 96, 102 },
            { "nervous", 1, 110, 97 }, { "anxious", 1, 110, 97 }, { "worried", 1, 104, 98 },
            { "scared", 2, 112, 98 }, { "embarrassed", 1, 102, 94 }, { "flustered", 2, 110, 97 },
            { "sarcastic", -1, 96, 100 }, { "smug", -1, 97, 101 }, { "teasing", 0, 98, 101 },
            { "deadpan", -1, 96, 99 }, { "mocking", 0, 100, 101 }, { "indifferent", -1, 97, 97 },
            { "proud", 0, 102, 106 }, { "confident", 0, 102, 105 }, { "determined", 0, 104, 106 },
            { "curious", 1, 104, 100 }, { "hopeful", 1, 103, 100 }, { "grateful", 1, 99, 100 },
            { "calm", 0, 95, 98 }, { "relaxed", 0, 94, 98 },
            { "satisfied", 0, 97, 100 }, { "optimistic", 1, 104, 101 }, { "pessimistic", -1, 94, 97 },
            { "confused", 1, 97, 99 }, { "uncertain", 0, 95, 97 }, { "doubtful", -1, 96, 98 },
            { "guilty", -1, 94, 95 }, { "ashamed", -1, 92, 93 }, { "regretful", -1, 92, 95 },
            { "jealous", -1, 100, 101 }, { "envious", -1, 99, 100 }, { "nostalgic", 0, 92, 97 },
            { "empathetic", 0, 96, 98 }, { "sympathetic", 0, 96, 98 }, { "compassionate", 0, 95, 98 },
            { "moved", 0, 94, 97 }, { "disdainful", -1, 96, 101 }, { "annoyed", -1, 102, 103 },
            { "hysterical", 3, 124, 118 }, { "in a hurry tone", 1, 124, 102 }, { "panting", 2, 112, 96 },
            { "groaning", -1, 90, 98 }, { "snoring", -2, 78, 92 }, { "sniffling", 1, 94, 94 },
            { "scoffing", -1, 100, 102 }, { "snorting", 0, 104, 103 }, { "pouting", 1, 96, 97 },
            { "humming", 0, 96, 90 }, { "singing", 1, 100, 104 }, { "cheerful", 2, 106, 103 },
            { "dramatic", 1, 96, 108 }, { "shy", 1, 94, 88 }, { "evil", -2, 94, 98 }
        };

        // "extremely excited" and "a bit sad" are the same cue, further on or less far. Anything after the feeling
        // itself, like "excited and a little breathless", is ignored rather than guessed at.
        private static readonly string[] More = { "extremely", "really", "very", "super", "so" };
        private static readonly string[] Less = { "slightly", "a bit", "a little", "kind of", "kinda", "quite" };

        // Another written form of a cue above, read only when the cue itself is not in the table: a brain
        // writes "[whispers]" or "[playfully]" as readily as "[whispering]" or "[playful]".
        private static readonly string[,] Forms =
        {
            { "whisper", "whispering" }, { "whispers", "whispering" }, { "laugh", "laughing" },
            { "laughs", "laughing" }, { "giggle", "giggling" }, { "giggles", "giggling" },
            { "chuckle", "chuckling" }, { "chuckles", "chuckling" }, { "sigh", "sighing" },
            { "sighs", "sighing" }, { "sob", "sobbing" }, { "sobs", "sobbing" },
            { "cries", "sobbing" }, { "crying", "sobbing" }, { "gasp", "gasping" },
            { "gasps", "gasping" }, { "yawn", "yawning" }, { "yawns", "yawning" },
            { "groan", "groaning" }, { "groans", "groaning" }, { "snort", "snorting" },
            { "snorts", "snorting" }, { "scoff", "scoffing" }, { "scoffs", "scoffing" },
            { "pout", "pouting" }, { "pouts", "pouting" }, { "sniffle", "sniffling" },
            { "sniffles", "sniffling" }, { "hum", "humming" }, { "hums", "humming" },
            { "sing", "singing" }, { "sings", "singing" }, { "mock", "mocking" },
            { "mockingly", "mocking" }, { "playfully", "playful" }, { "teasingly", "teasing" },
            { "smugly", "smug" }, { "mischievously", "mischievous" }, { "shyly", "shy" },
            { "cheerfully", "cheerful" }, { "dramatically", "dramatic" }, { "flirtatious", "flirty" },
            { "flirtatiously", "flirty" }, { "warm", "warmly" }, { "gentle", "gently" }
        };

        internal static bool Shift(string cue, out int pitch, out int pace, out int loud)
        {
            pitch = 0;
            pace = 100;
            loud = 100;
            if (string.IsNullOrEmpty(cue)) return false;
            string said = cue.Trim().ToLowerInvariant();
            double strength = 1;
            foreach (string word in More) if (said.StartsWith(word + " ", StringComparison.Ordinal)) { strength = 1.5; said = said.Substring(word.Length + 1); break; }
            if (strength == 1)
                foreach (string word in Less) if (said.StartsWith(word + " ", StringComparison.Ordinal)) { strength = 0.5; said = said.Substring(word.Length + 1); break; }

            int found = Row(said);
            if (found < 0)
                for (int at = 0; at < Forms.GetLength(0); at++)
                    if (said.Equals(Forms[at, 0], StringComparison.Ordinal) || said.StartsWith(Forms[at, 0] + " ", StringComparison.Ordinal))
                    { found = Row(Forms[at, 1]); break; }
            if (found < 0) return false;
            pitch = (int)Math.Round((int)Moves[found, 1] * strength);
            pace = 100 + (int)Math.Round(((int)Moves[found, 2] - 100) * strength);
            loud = 100 + (int)Math.Round(((int)Moves[found, 3] - 100) * strength);
            pitch = Math.Max(-3, Math.Min(3, pitch));
            pace = Math.Max(70, Math.Min(140, pace));
            loud = Math.Max(55, Math.Min(140, loud));
            return pitch != 0 || pace != 100 || loud != 100;
        }

        private static int Row(string said)
        {
            for (int at = 0; at < Moves.GetLength(0); at++)
            {
                string name = (string)Moves[at, 0];
                if (said.Equals(name, StringComparison.Ordinal) || said.StartsWith(name + " ", StringComparison.Ordinal)) return at;
            }
            return -1;
        }
    }
}
