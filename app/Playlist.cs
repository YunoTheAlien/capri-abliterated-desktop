using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // Her playlist from folders on this PC, in playlist.json: readable by hand, UTF-8 with no BOM. Folders first, then
    // added songs, then removed ones; removing only lists a path and never touches the file.
    internal sealed class Playlist
    {
        internal const string FileName = "playlist.json";

        // So it opens in her own editor as a tab, like her other files.
        internal static string Kept;
        private static readonly object writing = new object();

        internal static WrittenNote OpenNote() { return new WrittenNote(Kept, "", writing); }

        // What MCI will open through winmm. Anything else in the folder is skipped rather than failed on.
        private static readonly string[] Kinds = { ".mp3", ".wav", ".wma", ".m4a", ".aac", ".mid", ".midi" };

        private readonly List<string> folders = new List<string>();
        private readonly List<string> added = new List<string>();
        private readonly List<string> removed = new List<string>();
        private readonly List<string> loved = new List<string>();
        private readonly object gate = new object();
        private string path;
        internal bool Shuffle;
        // "off", "one" or "all", worded the way her Tune already words it.
        internal string Repeat = "off";

        internal Playlist(string file) { path = file; Read(); }

        internal string Where { get { return path; } }

        internal List<string> Folders { get { lock (gate) return new List<string>(folders); } }
        internal List<string> Removed { get { lock (gate) return new List<string>(removed); } }
        internal List<string> Loved { get { lock (gate) return new List<string>(loved); } }
        internal int AddedCount { get { lock (gate) return added.Count; } }

        // Every song the folders and the added list offer, in order, with the removed ones left out. Read fresh each time so
        // a folder filled while she runs is picked up without restarting her.
        internal List<string> Songs()
        {
            List<string> songs = new List<string>();
            HashSet<string> gone = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> roots;
            List<string> ones;
            lock (gate)
            {
                foreach (string each in removed) gone.Add(Settled(each));
                roots = new List<string>(folders);
                ones = new List<string>(added);
            }
            foreach (string folder in roots)
            {
                string[] found;
                try { found = Directory.Exists(folder) ? Directory.GetFiles(folder, "*", SearchOption.AllDirectories) : new string[0]; }
                catch (Exception) { continue; }
                Array.Sort(found, StringComparer.OrdinalIgnoreCase);
                foreach (string file in found) if (Playable(file) && !gone.Contains(Settled(file)) && !songs.Contains(file)) songs.Add(file);
            }
            foreach (string one in ones)
                if (Playable(one) && !gone.Contains(Settled(one)) && !songs.Contains(one) && FileThere(one)) songs.Add(one);
            return songs;
        }

        private static bool FileThere(string file) { try { return File.Exists(file); } catch (Exception) { return false; } }

        internal static bool Playable(string file)
        {
            if (string.IsNullOrEmpty(file)) return false;
            string kind = Path.GetExtension(file);
            foreach (string each in Kinds) if (string.Equals(kind, each, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // Paths are compared with their case and their slashes folded, so the same song written two ways still matches.
        private static string Settled(string file)
        {
            string plain = (file ?? "").Trim().Trim('"').Replace('/', '\\');
            while (plain.EndsWith("\\", StringComparison.Ordinal)) plain = plain.Substring(0, plain.Length - 1);
            try { plain = Path.GetFullPath(plain); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            return plain.ToLowerInvariant();
        }

        internal bool Loves(string file)
        {
            if (string.IsNullOrEmpty(file)) return false;
            string want = Settled(file);
            lock (gate) foreach (string each in loved) if (Settled(each) == want) return true;
            return false;
        }

        // A favourite is only a mark against the path. Nothing is moved, copied or written near the drive, and a
        // favourite that is later taken off the playlist simply stops coming up: the mark is left alone so putting
        // the song back brings its heart back with it.
        internal bool Love(string file, bool on)
        {
            string want = (file ?? "").Trim().Trim('"');
            if (want.Length == 0) return false;
            try { want = Path.GetFullPath(want); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            lock (gate)
            {
                bool held = false;
                foreach (string each in loved) if (Settled(each) == Settled(want)) held = true;
                if (on == held) return false;
                if (on) loved.Add(want);
                else loved.RemoveAll(each => Settled(each) == Settled(want));
            }
            Write();
            return true;
        }

        internal string AddFolder(string folder)
        {
            string want = (folder ?? "").Trim().Trim('"');
            if (want.Length == 0) return "Which folder?";
            try { want = Path.GetFullPath(want); } catch (Exception) { return "That isn't a folder path."; }
            // Handed one song rather than a folder, take the song: it is the obvious thing meant and saves a second command.
            if (!Directory.Exists(want) && FileThere(want)) return AddSong(want);
            if (!Directory.Exists(want)) return "There's no folder at " + want + ". Nothing was created.";
            lock (gate)
            {
                foreach (string each in folders) if (Settled(each) == Settled(want)) return "That folder's already in the playlist.";
                folders.Add(want);
            }
            Write();
            int count = Songs().Count;
            return "Added " + want + ". That's " + count + " song" + (count == 1 ? "" : "s") + " now.";
        }

        internal string AddSong(string file)
        {
            string want = (file ?? "").Trim().Trim('"');
            if (want.Length == 0) return "Which song?";
            try { want = Path.GetFullPath(want); } catch (Exception) { return "That isn't a file path."; }
            if (!FileThere(want)) return "There's no file at " + want + ".";
            if (!Playable(want)) return "I can't play that kind of file.";
            lock (gate)
            {
                // Coming back after being removed takes it off the bottom list rather than adding it twice.
                removed.RemoveAll(each => Settled(each) == Settled(want));
                bool held = false;
                foreach (string each in added) if (Settled(each) == Settled(want)) held = true;
                if (!held) added.Add(want);
            }
            Write();
            return "Added " + Name(want) + ".";
        }

        // The song goes on the bottom list and nowhere near the drive.
        internal string Remove(string file)
        {
            string want = (file ?? "").Trim().Trim('"');
            if (want.Length == 0) return "Nothing's playing.";
            lock (gate)
            {
                added.RemoveAll(each => Settled(each) == Settled(want));
                foreach (string each in removed) if (Settled(each) == Settled(want)) return Name(want) + " was already off the playlist.";
                removed.Add(want);
            }
            Write();
            return "Took " + Name(want) + " off the playlist. The file's still on your drive.";
        }

        internal static string Name(string file)
        {
            try { return Path.GetFileNameWithoutExtension(file); }
            catch (Exception) { return file; }
        }

        // Written by hand rather than through a serializer, because the layout is the point: folders, then added, then the
        // removed ones at the bottom, each one on its own line so the file can be edited in any text editor.
        internal void Write()
        {
            if (path == null) return;
            List<string> roots, ones, gone, dear;
            lock (gate) { roots = new List<string>(folders); ones = new List<string>(added); gone = new List<string>(removed); dear = new List<string>(loved); }
            StringBuilder text = new StringBuilder();
            text.Append("{").Append(Environment.NewLine);
            text.Append("  \"_\": \"Capri's playlist. Folders are scanned top to bottom. Songs under added are played after them.\",").Append(Environment.NewLine);
            text.Append("  \"__\": \"Anything under removed is skipped. Taking a song off the playlist never deletes it from the drive.\",").Append(Environment.NewLine);
            text.Append("  \"____\": \"Songs under loved are favourites: on shuffle they are dealt through the list so one comes up every few tracks.\",").Append(Environment.NewLine);
            text.Append("  \"___\": \"Edit this file by hand if you like. Keep it UTF-8 with no byte order mark.\",").Append(Environment.NewLine);
            text.Append("  \"shuffle\": ").Append(Shuffle ? "true" : "false").Append(",").Append(Environment.NewLine);
            text.Append("  \"repeat\": \"").Append(Repeat).Append("\",").Append(Environment.NewLine);
            Lines(text, "playlist", roots, true);
            Lines(text, "added", ones, true);
            Lines(text, "removed", gone, true);
            Lines(text, "loved", dear, false);
            text.Append("}").Append(Environment.NewLine);
            try
            {
                string folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) Directory.CreateDirectory(folder);
                // No byte order mark, ever: her own reader refuses one, and track names need the rest of UTF-8 intact.
                File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        private static void Lines(StringBuilder text, string name, List<string> items, bool comma)
        {
            text.Append("  \"").Append(name).Append("\": [");
            if (items.Count == 0) { text.Append("]").Append(comma ? "," : "").Append(Environment.NewLine); return; }
            text.Append(Environment.NewLine);
            for (int i = 0; i < items.Count; i++)
            {
                text.Append("    \"").Append(Escaped(items[i])).Append("\"");
                if (i < items.Count - 1) text.Append(",");
                text.Append(Environment.NewLine);
            }
            text.Append("  ]").Append(comma ? "," : "").Append(Environment.NewLine);
        }

        private static string Escaped(string text)
        {
            StringBuilder made = new StringBuilder();
            foreach (char letter in text ?? "")
            {
                if (letter == '\\') made.Append("\\\\");
                else if (letter == '"') made.Append("\\\"");
                else if (letter == '\n') made.Append("\\n");
                else if (letter == '\r') made.Append("\\r");
                else if (letter == '\t') made.Append("\\t");
                else if (letter < ' ') made.Append("\\u").Append(((int)letter).ToString("x4", CultureInfo.InvariantCulture));
                else made.Append(letter);
            }
            return made.ToString();
        }

        // Read by hand too, so a file someone has edited and left slightly untidy still loads. A byte order mark is stepped
        // over rather than refused, because an editor may well have added one.
        internal void Read()
        {
            if (path == null || !FileThere(path)) return;
            string text;
            try { text = File.ReadAllText(path, new UTF8Encoding(false)); }
            catch (Exception) { return; }
            if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
            lock (gate)
            {
                folders.Clear(); added.Clear(); removed.Clear(); loved.Clear();
                Fill(text, "playlist", folders);
                Fill(text, "added", added);
                Fill(text, "removed", removed);
                Fill(text, "loved", loved);
                Shuffle = Regex.IsMatch(text ?? "", @"""shuffle""\s*:\s*true", RegexOptions.IgnoreCase);
                Match how = Regex.Match(text ?? "", @"""repeat""\s*:\s*""(off|one|all)""", RegexOptions.IgnoreCase);
                Repeat = how.Success ? how.Groups[1].Value.ToLowerInvariant() : "off";
            }
        }

        private static void Fill(string text, string name, List<string> into)
        {
            Match block = Regex.Match(text ?? "", "\"" + name + "\"\\s*:\\s*\\[(.*?)\\]", RegexOptions.Singleline);
            if (!block.Success) return;
            foreach (Match one in Regex.Matches(block.Groups[1].Value, "\"((?:[^\"\\\\]|\\\\.)*)\""))
            {
                string got = Unescaped(one.Groups[1].Value);
                if (got.Length > 0 && !into.Contains(got)) into.Add(got);
            }
        }

        private static string Unescaped(string text)
        {
            return Regex.Replace(text ?? "", @"\\(u[0-9a-fA-F]{4}|.)", delegate(Match found)
            {
                string what = found.Groups[1].Value;
                if (what.StartsWith("u", StringComparison.OrdinalIgnoreCase)) return ((char)int.Parse(what.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString();
                if (what == "n") return "\n";
                if (what == "r") return "\r";
                if (what == "t") return "\t";
                return what;
            });
        }
    }

    // One song at a time through WPF's MediaPlayer (MCI's mp3 device won't load in 64-bit), on its own STA thread with a
    // message loop, since WinForms doesn't pump WPF's queue. Without Windows Media Player, which that needs, it plays
    // through Media Foundation's engine (EngineTune).
    internal static class FolderTunes
    {
        private static readonly object gate = new object();
        private static System.Windows.Threading.Dispatcher loop;
        private static System.Windows.Media.MediaPlayer player;
        private static string playing;
        private static volatile bool ended;
        private static volatile bool failed;
        private static EngineTune engine;

        internal static string Playing { get { lock (gate) return playing; } }

        // The song playing through Media Foundation, or null when WPF's player has it.
        private static EngineTune Engine { get { lock (gate) return engine; } }

        // For the checks: which way the song playing went.
        internal static bool ByEngine { get { return Engine != null; } }

        private static bool EngineStart(string file)
        {
            EngineTune made;
            try { made = EngineTune.Play(file, Loudness); }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); return false; }
            lock (gate) engine = made;
            // It opens asynchronously too, so a file it can't read says so through Failed a moment later.
            for (int waited = 0; waited < 40 && !made.Failed; waited++)
            {
                if (made.Duration > 0) break;
                System.Threading.Thread.Sleep(50);
            }
            if (made.Failed) { Stop(); return false; }
            lock (gate) playing = file;
            return true;
        }

        private static bool Ready()
        {
            lock (gate)
            {
                if (loop != null) return true;
                System.Threading.ManualResetEvent up = new System.Threading.ManualResetEvent(false);
                System.Threading.Thread spinner = new System.Threading.Thread(delegate()
                {
                    try
                    {
                        loop = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                        player = new System.Windows.Media.MediaPlayer();
                        player.MediaEnded += delegate { ended = true; };
                        player.MediaFailed += delegate { failed = true; ended = true; };
                        up.Set();
                        System.Windows.Threading.Dispatcher.Run();
                    }
                    catch (Exception) { up.Set(); }
                });
                spinner.IsBackground = true;
                spinner.SetApartmentState(System.Threading.ApartmentState.STA);
                spinner.Name = "Capri playlist";
                spinner.Start();
                up.WaitOne(5000);
                return loop != null && player != null;
            }
        }

        private static void OnLoop(Action what)
        {
            if (!Ready()) return;
            try { loop.Invoke(what); }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        internal static bool Start(string file)
        {
            if (Engine != null) Stop();
            ended = false;
            failed = false;
            lock (gate) paused = false;
            if (EngineTune.PlayerMissing()) return EngineStart(file);
            if (!Ready()) return false;
            double want = Loudness;
            OnLoop(delegate { try { player.Volume = want; } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); } });
            bool opened = false;
            OnLoop(delegate
            {
                try
                {
                    player.Stop();
                    player.Open(new Uri(file));
                    player.Play();
                    opened = true;
                }
                catch (Exception) { opened = false; }
            });
            if (!opened) return false;
            // Media Foundation opens asynchronously, so a file it cannot read reports through MediaFailed a moment later.
            for (int waited = 0; waited < 40 && !failed; waited++)
            {
                if (Length > 0) break;
                System.Threading.Thread.Sleep(50);
            }
            if (failed) { Stop(); return false; }
            lock (gate) playing = file;
            return true;
        }

        // Where the song has got to and how long it is: the one reading that can't drift, so her player shows real
        // progress, and a position unmoved between reads means she is held.
        internal static double At
        {
            get
            {
                EngineTune own = Engine;
                if (own != null) return own.Position;
                double found = 0;
                OnLoop(delegate
                {
                    try { found = player.Position.TotalSeconds; }
                    catch (Exception) { found = 0; }
                });
                return found < 0 ? 0 : found;
            }
        }

        internal static double Seconds { get { return Length; } }

        private static double Length
        {
            get
            {
                EngineTune own = Engine;
                if (own != null) return own.Duration;
                double found = 0;
                OnLoop(delegate
                {
                    try { found = player.NaturalDuration.HasTimeSpan ? player.NaturalDuration.TimeSpan.TotalSeconds : 0; }
                    catch (Exception) { found = 0; }
                });
                return found;
            }
        }

        // To a place in the song, in seconds from its start.
        internal static void Seek(double seconds)
        {
            EngineTune own = Engine;
            if (own != null) { own.Seek(seconds); return; }
            OnLoop(delegate { try { player.Position = TimeSpan.FromSeconds(seconds); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); } });
        }

        internal static void Stop()
        {
            EngineTune own;
            lock (gate) { own = engine; engine = null; }
            if (own != null) own.Dispose();
            else
                OnLoop(delegate
                {
                    try { player.Stop(); player.Close(); }
                    catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                });
            lock (gate) { playing = null; paused = false; }
            ended = false;
        }

        private static double loud = 0.7;

        // How loud her own songs are, 0 to 1. Starts below full because it was reported as too loud, and there was
        // nothing on her player to turn it down with.
        internal static double Loudness
        {
            get { lock (gate) return loud; }
            set
            {
                double want = value < 0 ? 0 : value > 1 ? 1 : value;
                lock (gate) loud = want;
                EngineTune own = Engine;
                if (own != null) { own.Volume = want; return; }
                OnLoop(delegate { try { player.Volume = want; } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); } });
            }
        }

        private static bool paused;

        internal static void Pause()
        {
            EngineTune own = Engine;
            if (own != null) { own.Pause(); lock (gate) paused = true; return; }
            OnLoop(delegate { try { player.Pause(); lock (gate) paused = true; } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); } });
        }

        internal static void Resume()
        {
            EngineTune own = Engine;
            if (own != null) { own.Resume(); lock (gate) paused = false; return; }
            OnLoop(delegate { try { player.Play(); lock (gate) paused = false; } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); } });
        }

        private static bool Finished { get { EngineTune own = Engine; return own != null ? own.Ended : ended; } }

        // Held, rather than stopped or finished. Mode keeps saying "playing" for a paused song on purpose: the next
        // song check reads it, and a paused song must not count as one that ended and move the playlist on.
        internal static bool Paused { get { bool done = Finished; lock (gate) return playing != null && paused && !done; } }

        // What the next-song check reads. Worded like MCI's answer so nothing above had to change.
        internal static string Mode
        {
            get
            {
                lock (gate) if (playing == null) return "";
                if (Finished) return "stopped";
                return "playing";
            }
        }

        internal static bool Busy { get { return Mode == "playing"; } }
    }

    // A mod's sound that is not a WAV, by the playlist's route: its own STA thread with a dispatcher, since WinForms
    // does not pump WPF's queue. One player per sound, kept until it ends so it is not collected mid-play.
    internal static class ModTunes
    {
        private static readonly object gate = new object();
        private static readonly System.Collections.Generic.List<System.Windows.Media.MediaPlayer> playing = new System.Collections.Generic.List<System.Windows.Media.MediaPlayer>();
        private static System.Windows.Threading.Dispatcher loop;

        private static bool Ready()
        {
            lock (gate)
            {
                if (loop != null) return true;
                System.Threading.ManualResetEvent up = new System.Threading.ManualResetEvent(false);
                System.Threading.Thread spinner = new System.Threading.Thread(delegate()
                {
                    try
                    {
                        loop = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                        up.Set();
                        System.Windows.Threading.Dispatcher.Run();
                    }
                    catch (Exception) { up.Set(); }
                });
                spinner.IsBackground = true;
                spinner.SetApartmentState(System.Threading.ApartmentState.STA);
                spinner.Name = "Capri mod sounds";
                spinner.Start();
                up.WaitOne(5000);
                return loop != null;
            }
        }

        // For the checks: the sounds still held, both ways.
        internal static int Held { get { lock (gate) return playing.Count + engines.Count; } }

        private static readonly System.Collections.Generic.List<EngineTune> engines = new System.Collections.Generic.List<EngineTune>();

        internal static bool Play(string file, double volume)
        {
            if (EngineTune.PlayerMissing())
            {
                EngineTune one;
                try { one = EngineTune.Play(file, volume); }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); return false; }
                lock (gate) engines.Add(one);
                one.Done += delegate { lock (gate) engines.Remove(one); one.Dispose(); };
                // One that was over before Done was listened for.
                if (one.Ended) { lock (gate) engines.Remove(one); one.Dispose(); return !one.Failed; }
                return true;
            }
            if (!Ready()) return false;
            try
            {
                loop.BeginInvoke(new Action(delegate
                {
                    try
                    {
                        System.Windows.Media.MediaPlayer one = new System.Windows.Media.MediaPlayer();
                        EventHandler done = delegate { try { one.Close(); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); } lock (gate) playing.Remove(one); };
                        one.MediaEnded += done;
                        one.MediaFailed += delegate { done(null, EventArgs.Empty); };
                        lock (gate) playing.Add(one);
                        one.Volume = volume;
                        one.Open(new Uri(file));
                        one.Play();
                    }
                    catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                }));
                return true;
            }
            catch (Exception) { return false; }
        }
    }
}

