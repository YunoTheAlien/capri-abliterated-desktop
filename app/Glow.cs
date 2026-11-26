using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace CapriDesktop
{
    // What her beat pulse draws besides her: her outline's glow, and the check that she and the Mainframe's edge pulse
    // together.
    internal sealed partial class Companion
    {
        // Her outline glow: a thin band of light round her edge, under her finished frame, from a 3-4 chamfer distance
        // a few milliseconds a frame. Solid for RimSolid px and fading over RimFade more, at 100% and size 560.
        private const float RimSolid = 2f, RimFade = 2f;
        private int[] rimDistance;

        // How bright her outline glows on this frame, 0 to 1: lit a little between beats and brightest on them, or held
        // for Steady glow.
        private float OutlineLevel()
        {
            if (!Pulsing) return 0f;
            if (SteadyGlow) return 0.75f;
            return PulseGain * (0.3f + 0.7f * BeatLevel()) * PulseAura[PulseStrength];
        }

        // pixels: her frame, premultiplied blue, green, red and alpha, top-down rows with no padding.
        private unsafe void Rim(IntPtr pixels, int width, int height)
        {
            float level = OutlineLevel();
            if (level <= 0.01f || width < 3 || height < 3) return;
            // Scaled with her size from 4 px at size 560 as well as with the screen, and never under 2 px in all.
            float s = Math.Max(2f / (RimSolid + RimFade), DeviceDpi / 96f * preferences.Size / 560f);
            int solid = (int)Math.Round(RimSolid * s * 3), reach = solid + Math.Max(3, (int)Math.Round(RimFade * s * 3));
            int count = width * height;
            if (rimDistance == null || rimDistance.Length < count) rimDistance = new int[count];
            int[] far = rimDistance;
            int* image = (int*)pixels.ToPointer();
            const int Away = 1 << 20;
            // Her body's box, and the glow's reach round it: nothing further out is lit, so nothing further out is worked
            // over. On a box with no graphics card the whole canvas cost a frame twice as long.
            int top = height, bottom = -1, left = width, right = -1;
            for (int y = 0; y < height; y++)
                for (int x = 0, at = y * width; x < width; x++, at++)
                    if (((uint)image[at] >> 24) >= 128) { if (y < top) top = y; bottom = y; if (x < left) left = x; if (x > right) right = x; }
            if (bottom < 0) return;
            int pad = reach / 3 + 2;
            int y0 = Math.Max(0, top - pad), y1 = Math.Min(height - 1, bottom + pad), x0 = Math.Max(0, left - pad), x1 = Math.Min(width - 1, right + pad);
            fixed (int* distance = far)
            {
                for (int y = y0; y <= y1; y++)
                    for (int x = x0, at = y * width + x0; x <= x1; x++, at++)
                    {
                        int d = ((uint)image[at] >> 24) >= 128 ? 0 : Away;
                        if (d != 0)
                        {
                            if (x > x0) d = Math.Min(d, distance[at - 1] + 3);
                            if (y > y0)
                            {
                                d = Math.Min(d, distance[at - width] + 3);
                                if (x > x0) d = Math.Min(d, distance[at - width - 1] + 4);
                                if (x < x1) d = Math.Min(d, distance[at - width + 1] + 4);
                            }
                        }
                        distance[at] = d;
                    }
                Color colour = Neon.Hot;
                int most = (int)Math.Round(230 * Math.Min(1f, level));
                for (int y = y1; y >= y0; y--)
                    for (int x = x1, at = y * width + x1; x >= x0; x--, at--)
                    {
                        int d = distance[at];
                        if (d == 0) continue;
                        if (x < x1) d = Math.Min(d, distance[at + 1] + 3);
                        if (y < y1)
                        {
                            d = Math.Min(d, distance[at + width] + 3);
                            if (x < x1) d = Math.Min(d, distance[at + width + 1] + 4);
                            if (x > x0) d = Math.Min(d, distance[at + width - 1] + 4);
                        }
                        distance[at] = d;
                        if (d >= reach) continue;
                        int glow = d <= solid ? most : most * (reach - d) / (reach - solid);
                        uint pixel = (uint)image[at];
                        int under = (int)(255 - (pixel >> 24));
                        int add = glow * under / 255;
                        if (add <= 0) continue;
                        int a = (int)(pixel >> 24) + add;
                        int r = (int)((pixel >> 16) & 255) + colour.R * add / 255;
                        int g = (int)((pixel >> 8) & 255) + colour.G * add / 255;
                        int b = (int)(pixel & 255) + colour.B * add / 255;
                        image[at] = (int)((uint)Math.Min(255, a) << 24 | (uint)Math.Min(255, r) << 16 | (uint)Math.Min(255, g) << 8 | (uint)Math.Min(255, b));
                    }
            }
        }

        // Her pulse and the Mainframe's edge glow peak within 50 ms of the same beat at every strength; her swell shows
        // and her outline glows in drawn frames; Glow, Steady, the cost at size 560, and Motion or the switch off
        // removing it all.
#if CHECKS
        private void SmokeBeatPulse(List<string> checks)
        {
            ThrowCheck(checks, delegate(List<string> list, List<short[]> sounds)
            {
                Rectangle work = Displays.Home.WorkingArea;
                StandForThrow(new Point(work.Left + work.Width / 3, work.Top + work.Height / 2));
                bool pulseWas = preferences.BeatPulse, framedWas = mainframe.Visible;
                FormWindowState stateWas = mainframe.WindowState;
                int strengthWas = preferences.BeatPulseStrength;
                try
                {
                    if (!framedWas) mainframe.Show();
                    if (mainframe.WindowState == FormWindowState.Minimized) mainframe.WindowState = FormWindowState.Normal;
                    string[] names = { "Soft", "Medium", "Strong" };
                    float[] least = { 0.04f, 0.06f, 0.10f };
                    List<string> found = new List<string>();
                    float lastSwell = 0;
                    int lastLit = 0;
                    for (int strength = -1; strength <= 1; strength++)
                    {
                        SetBeatPulse(strength);
                        string name = names[strength + 1];
                        // A beat locked at 120: every half second from t0, followed frame by frame in 5 ms steps.
                        double t0 = clock.Elapsed.TotalSeconds + 10, beat = 0.5;
                        groove.Pretend(120, t0);
                        Play(dance);
                        double pulseAt = 0, glowAt = 0;
                        float pulseMost = 0;
                        int glowMost = -1;
                        for (double t = t0; t <= t0 + 1.25; t += 0.005)
                        {
                            FollowBeat(t);
                            GlowWithBeat();
                            if (t < t0 + 0.75) continue;
                            float pulse = Pulse();
                            int lit = mainframe.BeatGlowShown;
                            if (pulse > pulseMost) { pulseMost = pulse; pulseAt = t; }
                            if (lit > glowMost) { glowMost = lit; glowAt = t; }
                        }
                        double gap = Math.Abs(pulseAt - glowAt) * 1000;
                        // "A few pixels" at the Mainframe's own scale, which grows with a tall screen's Auto size as
                        // well as with DPI: 8 px at Auto 150% is the same glow as 5 px at 100%.
                        if (mainframe.BeatGlowReach > Math.Ceiling(6 * mainframe.BeatGlowUnit))
                            throw new Exception(name + ": the Mainframe's edge glow reaches " + mainframe.BeatGlowReach + " px out, more than a few pixels.");
                        if (gap > 50 || Math.Abs(pulseAt - (t0 + 2 * beat)) > 0.05 || glowMost <= 0)
                            throw new Exception(name + ": her pulse peaked at " + ((pulseAt - t0) * 1000).ToString("0") + " ms and the Mainframe's glow at " + ((glowAt - t0) * 1000).ToString("0") + " ms (alpha " + glowMost + "), beat at 1000 ms; the Mainframe " + (mainframe.Visible ? "shown" : "hidden") + " " + mainframe.WindowState + ", " + mainframe.BeatGlowState + ".");
                        // Her frame at the top of the beat against the same frame with the pulse off: how much taller she is.
                        dancePos = 0;
                        nodGain = 1;
                        danceBeat = beat;
                        int[] plain = Spans(false), pulsed = Spans(true);
                        float swell = (pulsed[1] - pulsed[0]) / (float)Math.Max(1, plain[1] - plain[0]) - 1;
                        if (swell < least[strength + 1] || swell <= lastSwell || glowMost <= lastLit)
                            throw new Exception(name + ": she swelled " + (swell * 100).ToString("0.0") + "% (" + (plain[1] - plain[0]) + " to " + (pulsed[1] - pulsed[0]) + " px) with the glow at alpha " + glowMost + ", wanted at least " + (least[strength + 1] * 100).ToString("0") + "% and more than the strength before.");
                        int[] rim = Outside(dance);
                        if (rim[0] < 100) throw new Exception(name + ": her outline didn't glow on the beat: " + rim[0] + " pixels round her.");
                        lastSwell = swell;
                        lastLit = glowMost;
                        found.Add(name + " " + (swell * 100).ToString("0.0") + "% taller, edge glow " + mainframe.BeatGlowReach + " px wide (rows " + plain[0] + "-" + plain[1] + " to " + pulsed[0] + "-" + pulsed[1] + ", size " + preferences.Size + ", window " + Height + ", pulse " + Pulse().ToString("0.000") + "), glow alpha " + glowMost + ", outline glow " + rim[0] + " px, peaks " + gap.ToString("0") + " ms apart");
                    }
                    // Glow: her outline lights on the beat with no swell or hop, brighter on the beat than between, in her menu
                    // edge's colour, standing, flipped and sitting.
                    SetBeatPulse(2);
                    Play(dance);
                    dancePos = 0;
                    nodGain = 1;
                    danceBeat = 0.5;
                    GlowWithBeat();
                    int glowLit = mainframe.BeatGlowShown;
                    int[] onBeat = Outside(dance);
                    dancePos = 0.5;
                    int[] offBeat = Outside(dance);
                    dancePos = 0;
                    facingLeft = true;
                    int[] flipped = Outside(dance);
                    facingLeft = false;
                    bool still = Pulse() == 1f && Hop() == 0f;
                    Play(perchDance);
                    dancePos = 0;
                    nodGain = 1;
                    danceBeat = 0.5;
                    int[] sitting = Outside(perchDance);
                    Color hot = Neon.Hot;
                    int colourOff = Math.Abs(onBeat[2] - hot.R) + Math.Abs(onBeat[3] - hot.G) + Math.Abs(onBeat[4] - hot.B);
                    bool glowOnly = still && onBeat[0] > 100 && onBeat[1] > offBeat[1] * 1.3 && flipped[0] > 100 && sitting[0] > 100 && colourOff < 60 && glowLit > 0;
                    // Steady glow: lit the same all through a beat while the music plays, with no beat found. Her breathing moves
                    // a band this thin by a fifth between two frames; the beat moves Glow's by several times.
                    SetBeatPulse(3);
                    Play(dance);
                    nodGain = 0;
                    danceBeat = 0;
                    dancePos = 0;
                    GlowWithBeat();
                    int steadyLit = mainframe.BeatGlowShown;
                    int[] held = Outside(dance);
                    dancePos = 0.5;
                    GlowWithBeat();
                    int[] heldLater = Outside(dance);
                    // Per pixel of her outline, since a dance frame can lengthen or shorten the outline itself.
                    double heldEach = held[1] / (double)Math.Max(1, held[0]), laterEach = heldLater[1] / (double)Math.Max(1, heldLater[0]);
                    bool steadyGlow = Pulse() == 1f && Hop() == 0f && held[0] > 100 && Math.Abs(heldEach - laterEach) <= heldEach / 5 && steadyLit > 0 && steadyLit == mainframe.BeatGlowShown;
                    if (!glowOnly || !steadyGlow)
                        throw new Exception("The glow looks went wrong: Glow " + glowOnly + " (still " + still + ", outline " + onBeat[0] + " px on the beat, light " + onBeat[1] + " on the beat and " + offBeat[1] + " between, flipped " + flipped[0] + ", sitting " + sitting[0] + ", colour " + onBeat[2] + "," + onBeat[3] + "," + onBeat[4] + " against " + hot.R + "," + hot.G + "," + hot.B + ", Mainframe " + glowLit + "), Steady glow " + steadyGlow + " (outline " + held[0] + " and " + heldLater[0] + " px, light " + held[1] + " and " + heldLater[1] + ", Mainframe " + steadyLit + " and " + mainframe.BeatGlowShown + ")");
                    // What a frame costs with her outline glowing at size 560, against the same with the pulse off.
                    int sizeWas = preferences.Size;
                    bool musicHeld = musicOn;
                    double plainMs, glowMs;
                    try
                    {
                        preferences.Size = 560;
                        Size = CanvasSize(560);
                        bodyCells.Clear();
                        musicOn = true;
                        groove.Pretend(120, clock.Elapsed.TotalSeconds);
                        Func<int, double> cost = delegate(int look)
                        {
                            SetBeatPulse(look);
                            Play(dance);
                            nodGain = 1;
                            RenderFrame();
                            System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();
                            for (int i = 0; i < 60; i++) RenderFrame();
                            return timer.Elapsed.TotalMilliseconds / 60;
                        };
                        plainMs = cost(-2);
                        SetBeatPulse(2);
                        Play(dance);
                        nodGain = 1;
                        glowMs = cost(2);
                    }
                    finally
                    {
                        musicOn = musicHeld;
                        preferences.Size = sizeWas;
                        Size = CanvasSize(sizeWas);
                        bodyCells.Clear();
                        groove.Pretend(0, 0);
                    }
                    if (glowMs > plainMs + 8) throw new Exception("Her outline glow costs too much at size 560: " + glowMs.ToString("0.0") + " ms a frame against " + plainMs.ToString("0.0") + " ms without it.");
                    found.Add("Glow keeps her size with her outline lit, " + onBeat[0] + " px round her in her menu edge's colour (off by " + colourOff + "), " + (onBeat[1] * 100 / Math.Max(1, offBeat[1]) - 100) + "% brighter on the beat, flipped and sitting too; Steady glow holds it through a beat with no beat found; at size 560 a frame costs " + glowMs.ToString("0.0") + " ms with the glow and " + plainMs.ToString("0.0") + " ms without");
                    // A lock lost mid-song: she keeps the beat she had while the music is on, and lets go once it stops.
                    SetBeatPulse(0);
                    bool musicWas = musicOn;
                    double coastFrom = clock.Elapsed.TotalSeconds + 10;
                    float coastGain, stoppedGain;
                    double coastPeak = 0;
                    try
                    {
                        musicOn = true;
                        groove.Pretend(120, coastFrom);
                        Play(dance);
                        for (double t = coastFrom; t <= coastFrom + 1; t += 0.005) FollowBeat(t);
                        groove.Pretend(0, 0);
                        float coastMost = 0;
                        for (double t = coastFrom + 1.005; t <= coastFrom + 4.25; t += 0.005)
                        {
                            FollowBeat(t);
                            if (t < coastFrom + 3.75) continue;
                            if (Pulse() > coastMost) { coastMost = Pulse(); coastPeak = t; }
                        }
                        coastGain = (float)nodGain;
                        musicOn = false;
                        for (double t = coastFrom + 4.255; t <= coastFrom + 5.5; t += 0.005) FollowBeat(t);
                        stoppedGain = (float)nodGain;
                    }
                    finally { musicOn = musicWas; }
                    double coastOff = Math.Abs(coastPeak - (coastFrom + 4)) * 1000;
                    if (coastGain < 0.99f || coastOff > 20 || stoppedGain > 0 || Pulsing)
                        throw new Exception("The pulse didn't coast through a lost lock: gain " + coastGain + " three seconds on, its beat " + coastOff.ToString("0") + " ms off, gain " + stoppedGain + " once the music stopped");
                    found.Add("a lock lost mid-song coasts 3 s on the last beat, " + coastOff.ToString("0") + " ms off, and lets go once the music stops");
                    // Another app's music: the kick drives her swell at once and falls away, and the Mainframe's edge with it.
                    string framedBeforeKick = (mainframe.Visible ? "shown" : "hidden") + " " + mainframe.WindowState;
                    if (!mainframe.Visible) mainframe.Show();
                    if (mainframe.WindowState == FormWindowState.Minimized) mainframe.WindowState = FormWindowState.Normal;
                    groove.Pretend(0, 0);
                    Play(dance);
                    double kickAt = clock.Elapsed.TotalSeconds + 20;
                    groove.PretendKick(1, kickAt);
                    FollowBeat(kickAt);
                    bool kickPulses = Pulsing;
                    float onKick = BeatLevel();
                    GlowWithBeat();
                    int edgeOnKick = mainframe.BeatGlowShown;
                    FollowBeat(kickAt + 0.3);
                    float afterKick = BeatLevel();
                    GlowWithBeat();
                    int edgeAfter = mainframe.BeatGlowShown;
                    groove.PretendKick(0, -10);
                    FollowBeat(kickAt + 0.31);
                    bool kickGone = !Pulsing;
                    if (!kickPulses || onKick < 0.95f || afterKick > 0.2f || edgeOnKick <= edgeAfter || !kickGone)
                        throw new Exception("The kick didn't drive her pulse: pulsing " + kickPulses + ", level " + onKick + " on the kick and " + afterKick + " 300 ms on, Mainframe " + edgeOnKick + " then " + edgeAfter + " (it was " + framedBeforeKick + " after her own song), gone with no kick or lock " + kickGone);
                    found.Add("another app's kick takes her swell to " + onKick.ToString("0.00") + " at once and " + afterKick.ToString("0.00") + " 300 ms on, the Mainframe's edge from " + edgeOnKick + " to " + edgeAfter + ", and with no kick and no beat found she stops");
                    // A kick draws her at once: the beat thread's word puts a frame up ahead of her frame timer.
                    // Waited on the frame itself, up to KickDrawLimit, so a busy run's slow message queue isn't taken for a missed kick.
                    const int KickDrawLimit = 3000;
                    Play(dance);
                    int framesBefore = kickFrames;
                    groove.PretendKick(1, clock.Elapsed.TotalSeconds);
                    Action kicked = groove.Kicked;
                    double kickToldAt = clock.Elapsed.TotalSeconds;
                    if (kicked != null) kicked();
                    System.Diagnostics.Stopwatch kickWait = System.Diagnostics.Stopwatch.StartNew();
                    while (kickFrames == framesBefore && kickWait.ElapsedMilliseconds < KickDrawLimit) { Application.DoEvents(); System.Threading.Thread.Sleep(1); }
                    groove.PretendKick(0, -10);
                    if (kickFrames == framesBefore) throw new Exception("A kick didn't draw her: no frame within " + KickDrawLimit + " ms of the beat thread's word (pulse " + preferences.BeatPulse + ", " + Doing + ")");
                    found.Add("a kick starts her frame " + ((kickFrameAt - kickToldAt) * 1000).ToString("0") + " ms after the beat thread reads it, ahead of her frame timer");
                    // A song from her own playlist counts as music, so she dances to it: it plays inside Capri, where
                    // Windows' media sessions never see it. A short silent one, played at nothing.
                    string quiet = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "capri-pulse-" + Guid.NewGuid().ToString("N") + ".wav");
                    double loudWas = FolderTunes.Loudness;
                    int queuedWas = queuedAt;
                    bool ownSong = false, ownPaused = true, ownStopped = true, ownStarted = false;
                    try
                    {
                        using (System.IO.BinaryWriter wav = new System.IO.BinaryWriter(System.IO.File.Create(quiet)))
                        {
                            int samples = 8000 * 3;
                            wav.Write(new[] { 'R', 'I', 'F', 'F' }); wav.Write(36 + samples * 2); wav.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                            wav.Write(16); wav.Write((short)1); wav.Write((short)1); wav.Write(8000); wav.Write(16000); wav.Write((short)2); wav.Write((short)16);
                            wav.Write(new[] { 'd', 'a', 't', 'a' }); wav.Write(samples * 2); wav.Write(new byte[samples * 2]);
                        }
                        FolderTunes.Loudness = 0;
                        queuedAt = 0;
                        // Windows' sessions, hers included, are read at most about once a second, so each look waits that out.
                        ownStarted = FolderTunes.Start(quiet);
                        Func<bool, bool> reads = delegate(bool want)
                        {
                            bool got = !want;
                            for (int look = 0; look < 12 && got != want; look++) { System.Threading.Thread.Sleep(250); got = MusicPlaying(); }
                            return got;
                        };
                        if (ownStarted)
                        {
                            ownSong = reads(true);
                            FolderTunes.Pause();
                            ownPaused = reads(false);
                            FolderTunes.Resume();
                        }
                        FolderTunes.Stop();
                        ownStopped = reads(false);
                    }
                    finally
                    {
                        FolderTunes.Stop();
                        FolderTunes.Loudness = loudWas;
                        queuedAt = queuedWas;
                        try { System.IO.File.Delete(quiet); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
                    }
                    // A PC without Windows' media playback (an N edition, a bare test box) can't play her playlist at all, so there
                    // is nothing to count; that is said, not failed.
                    if (!ownStarted) list.Add("LIMIT her own playlist couldn't play on this PC (no Windows media playback), so it wasn't checked as music");
                    else if (!ownSong || ownPaused || ownStopped) throw new Exception("Her own playlist's song didn't count as music: playing " + ownSong + ", paused " + ownPaused + ", stopped " + ownStopped);
                    // Motion off stops both, and so does the switch.
                    preferences.Motion = false;
                    GlowWithBeat();
                    bool stillOff = Pulse() == 1f && mainframe.BeatGlowShown == 0;
                    preferences.Motion = true;
                    SetBeatPulse(-2);
                    GlowWithBeat();
                    bool switchedOff = Pulse() == 1f && mainframe.BeatGlowShown == 0 && !preferences.BeatPulse;
                    if (!stillOff || !switchedOff) throw new Exception("Her pulse or the Mainframe's glow kept going: Motion off " + stillOff + ", switch off " + switchedOff);
                    list.Add("PASS her beat pulse and the Mainframe's edge glow peak together on a beat locked at 120 BPM, standing anywhere, and her swell is measured in a drawn frame: " + string.Join("; ", found.ToArray()) + "; a song from her own playlist counts as music to dance to, and not once paused or stopped; Motion off and the switch off each take both away");
                }
                finally
                {
                    preferences.BeatPulse = pulseWas;
                    preferences.BeatPulseStrength = strengthWas;
                    groove.Pretend(0, 0);
                    Play(idle);
                    GlowWithBeat();
                    if (mainframe.WindowState != stateWas) mainframe.WindowState = stateWas;
                    if (!framedWas) mainframe.Hide();
                }
            });
        }
#endif

        // Her player after a close: Play brings it back and carries on the song, Show music player only shows it, and the
        // kept song comes back by the 30 minute rule; she pulses to music from her Play menu and the TOOLS tile alike.
#if CHECKS
        private void SmokePlayerBack(List<string> checks, string output)
        {
            string folder = System.IO.Path.Combine(output, "player-back");
            System.IO.Directory.CreateDirectory(folder);
            string[] songs = { System.IO.Path.Combine(folder, "first.wav"), System.IO.Path.Combine(folder, "second.wav") };
            foreach (string song in songs)
                using (System.IO.BinaryWriter wav = new System.IO.BinaryWriter(System.IO.File.Create(song)))
                {
                    int rate = 8000, samples = rate * 20;
                    wav.Write(new[] { 'R', 'I', 'F', 'F' }); wav.Write(36 + samples * 2); wav.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                    wav.Write(16); wav.Write((short)1); wav.Write((short)1); wav.Write(rate); wav.Write(rate * 2); wav.Write((short)2); wav.Write((short)16);
                    wav.Write(new[] { 'd', 'a', 't', 'a' }); wav.Write(samples * 2); wav.Write(new byte[samples * 2]);
                }
            Playlist listWas = playlist;
            List<string> queuedWas = queued;
            int queuedAtWas = queuedAt;
            double nextWas = nextSongAt, loudWas = FolderTunes.Loudness;
            string keptWas = preferences.KeptSong;
            double keptAtWas = preferences.KeptAt;
            long keptWhenWas = preferences.KeptWhen;
            bool closedWas = playerClosed, askedWas = playerAsked, pulseWas = preferences.BeatPulse, framedWas = mainframe.Visible, noPlayerWas = preferences.NoPlayer, keepsWas = preferences.PlayerCloseKeepsMusic;
            double focusWas = focusUntil;
            Func<bool> musicWas = MusicPlaying;
            List<string> wrong = new List<string>();
            Func<bool> shown = delegate { Tuned(PlaylistTune(), clock.Elapsed.TotalSeconds, Tunes.Now); return player.Visible; };
            try
            {
                FolderTunes.Loudness = 0;
                preferences.NoPlayer = false;
                preferences.PlayerCloseKeepsMusic = false;
                playlist = new Playlist(System.IO.Path.Combine(folder, "playlist.json"));
                playlist.AddFolder(folder);
                preferences.KeptSong = null;
                if (ContinuePlaylist() != "Playing now." || FolderTunes.Playing == null)
                {
                    checks.Add("LIMIT her player after a close: her playlist couldn't play here (no Windows media playback)");
                    return;
                }
                string first = FolderTunes.Playing;
                System.Threading.Thread.Sleep(1200);
                if (!shown()) wrong.Add("the player didn't come up as her playlist played");

                // Closed while the song plays, as it starts: the music stops, and the song and its place are kept and saved.
                player.Press(PlayerPart.Close);
                Preferences saved = LoadPreferences();
                if (player.Visible || !playerClosed) wrong.Add("the close button didn't put the player away");
                if (preferences.KeptSong != first || preferences.KeptAt < 0.5 || Math.Abs(preferences.KeptWhen - EpochNow) > 5 || saved.KeptSong != first)
                    wrong.Add("the close didn't keep the song: " + preferences.KeptSong + " at " + preferences.KeptAt.ToString("0.0") + " s, " + (EpochNow - preferences.KeptWhen) + " s ago, saved " + saved.KeptSong);
                if (FolderTunes.Playing != null) wrong.Add("closing the player didn't stop the music, as it does by default");

                // Play from her menu after it: the player comes back and the same song carries on where it was.
                double kept = preferences.KeptAt;
                PlayMyMusic();
                CloseTalk();
                System.Threading.Thread.Sleep(300);
                if (FolderTunes.Playing != first || FolderTunes.At + 0.6 < kept || !shown()) wrong.Add("Play after a close didn't carry the song on or left the player away: " + Playlist.Name(FolderTunes.Playing ?? "nothing") + " at " + FolderTunes.At.ToString("0.0") + " s, kept at " + kept.ToString("0.0") + " s, player " + player.Visible);

                // Her Play menu's Stop, then Resume: the song stops and is kept, then carries on.
                StopOrResume();
                bool stopped = MusicStopped && FolderTunes.Playing == null && preferences.KeptSong == first;
                kept = preferences.KeptAt;
                StopOrResume();
                System.Threading.Thread.Sleep(300);
                if (!stopped || FolderTunes.Playing != first || FolderTunes.At + 0.6 < kept) wrong.Add("Stop then Resume: stopped " + stopped + ", then " + Playlist.Name(FolderTunes.Playing ?? "nothing") + " at " + FolderTunes.At.ToString("0.0") + " s, kept at " + kept.ToString("0.0") + " s");

                // Keep playing when closed, Music's part in Toggles and nowhere else: the close leaves the music going, and
                // Play carries it on untouched.
                bool inPlayerMenu = false;
                foreach (ToolStripItem one in playerMenu.Items) if (one.Text == "Keep playing when closed") inPlayerMenu = true;
                mainframe.PictureToggles().Dispose();
                mainframe.PickToggleRow("Music");
                string togglesShown = mainframe.TogglesText ?? "";
                mainframe.PressToggle("Keep playing when closed");
                mainframe.CloseToggles();
                if (inPlayerMenu || togglesShown.IndexOf("{Music} | Keep playing when closed off", StringComparison.Ordinal) < 0)
                    wrong.Add("Keep playing when closed isn't Music's part in Toggles, off, alone: player menu " + inPlayerMenu + ", Toggles " + togglesShown);
                bool keepsSaved = LoadPreferences().PlayerCloseKeepsMusic;
                player.Press(PlayerPart.Close);
                double before = FolderTunes.At;
                if (!keepsSaved || FolderTunes.Playing != first) wrong.Add("with Keep playing when closed on, closing the player stopped the music (saved " + keepsSaved + ")");
                PlayMyMusic();
                CloseTalk();
                if (FolderTunes.Playing != first || FolderTunes.At + 0.2 < before || !shown()) wrong.Add("Play with the song still going restarted it or left the player away: " + FolderTunes.At.ToString("0.0") + " s from " + before.ToString("0.0") + " s, player " + player.Visible);

                // Show music player while it plays: the window only, nothing restarted.
                player.Press(PlayerPart.Close);
                before = FolderTunes.At;
                ShowMusicPlayer();
                if (FolderTunes.Playing != first || FolderTunes.At + 0.2 < before || !shown()) wrong.Add("Show music player touched the music or didn't show the player: " + FolderTunes.At.ToString("0.0") + " s from " + before.ToString("0.0") + " s, player " + player.Visible);

                // Nothing playing: Show music player shows the kept song, paused, and nothing starts.
                player.Press(PlayerPart.Close);
                FolderTunes.Stop();
                queuedAt = -1;
                preferences.KeptSong = first;
                preferences.KeptAt = 6;
                preferences.KeptWhen = EpochNow - 60;
                ShowMusicPlayer();
                bool up = shown();
                System.Threading.Thread.Sleep(400);
                if (!up || FolderTunes.Playing != null || player.Tune == null || player.Tune.Title != Playlist.Name(first) || player.Tune.Playing)
                    wrong.Add("Show music player with nothing playing: player " + up + ", playing " + (FolderTunes.Playing ?? "nothing") + ", shown " + (player.Tune == null ? "no song" : player.Tune.Title));

                // Play a minute after the close: the kept song, where it was.
                player.Press(PlayerPart.Close);
                ContinuePlaylist();
                System.Threading.Thread.Sleep(300);
                double resumed = FolderTunes.At;
                if (FolderTunes.Playing != first || resumed < 5.5) wrong.Add("Play under 30 minutes on didn't carry on the kept song: " + Playlist.Name(FolderTunes.Playing ?? "nothing") + " at " + resumed.ToString("0.0") + " s, kept at 6 s");

                // Play 31 minutes after: the same song from its start.
                FolderTunes.Stop();
                queuedAt = -1;
                preferences.KeptAt = 6;
                preferences.KeptWhen = EpochNow - KeptFor - 60;
                ContinuePlaylist();
                System.Threading.Thread.Sleep(300);
                double restarted = FolderTunes.At;
                if (FolderTunes.Playing != first || restarted > 3) wrong.Add("Play 31 minutes on didn't start the kept song from the top: " + Playlist.Name(FolderTunes.Playing ?? "nothing") + " at " + restarted.ToString("0.0") + " s");

                // She dances and pulses to it from her Play menu and from the TOOLS tile.
                if (!framedWas) mainframe.Show();
                preferences.BeatPulse = true;
                MusicPlaying = delegate { return true; };
                Mainframe.Tool tile = mainframe.Tools().Find(one => one.Id == "music");
                ToolStripMenuItem fromMenu = null;
                foreach (ToolStripItem one in mainframe.MiniChoices().Items) if (one.Text == "Music") fromMenu = one as ToolStripMenuItem;
                foreach (string way in new[] { "her Play menu", "the TOOLS tile", "her Play menu during Focus time" })
                {
                    Play(idle);
                    CloseTalk();
                    // Focus time keeps her talk quiet, not her dancing.
                    focusUntil = way.EndsWith("Focus time", StringComparison.Ordinal) ? clock.Elapsed.TotalSeconds + 600 : focusWas;
                    if (way.StartsWith("her Play menu", StringComparison.Ordinal)) { if (fromMenu != null) fromMenu.PerformClick(); else wrong.Add("her Play menu has no Music"); }
                    else if (tile != null && tile.Open != null) tile.Open(); else wrong.Add("TOOLS has no Music tile");
                    CloseTalk();
                    musicOn = true;
                    CheckMusic(clock.Elapsed.TotalSeconds);
                    // A beat locked at 120, followed frame by frame as SmokeBeatPulse does, so her tempo lock eases in.
                    double t0 = clock.Elapsed.TotalSeconds + 10;
                    groove.Pretend(120, t0);
                    for (double t = t0; t <= t0 + 1.25; t += 0.005) FollowBeat(t);
                    if (clip != dance && clip != perchDance) wrong.Add("music from " + way + " didn't get her dancing: " + Doing);
                    else if (!Pulsing) wrong.Add("music from " + way + " didn't pulse: gain " + nodGain.ToString("0.00") + ", beat " + danceBeat.ToString("0.00") + ", hidden " + hidden + ", docked " + docked);
                }
            }
            finally
            {
                MusicPlaying = musicWas;
                musicOn = false;
                groove.Pretend(0, 0);
                FolderTunes.Stop();
                FolderTunes.Loudness = loudWas;
                playlist = listWas;
                queued = queuedWas;
                queuedAt = queuedAtWas;
                nextSongAt = nextWas;
                preferences.KeptSong = keptWas;
                preferences.KeptAt = keptAtWas;
                preferences.KeptWhen = keptWhenWas;
                preferences.BeatPulse = pulseWas;
                preferences.NoPlayer = noPlayerWas;
                preferences.PlayerCloseKeepsMusic = keepsWas;
                focusUntil = focusWas;
                playerClosed = closedWas;
                playerAsked = askedWas;
                HidePlayer();
                CloseTalk();
                Play(seat != null ? perch : idle);
                if (!framedWas) mainframe.Hide();
                SavePreferences();
            }
            if (wrong.Count > 0) throw new Exception("Her player after a close went wrong: " + string.Join("; ", wrong.ToArray()));
            checks.Add("PASS her player after a close: the close stops the music and keeps the song and its place in her settings, and Play brings the player back with the song carried on; her Play menu's Stop keeps the song and Resume carries it on; with Keep playing when closed on (saved) the close leaves the music going; Show music player only shows it, and with nothing playing shows the kept song paused and starts nothing; Play under 30 minutes on carries on the kept song where it was and 31 minutes on starts it from the top; she dances and pulses to music from her Play menu and the TOOLS tile alike, and during Focus time");
        }

        // Her own song on her player's bar: it can seek while it plays, and a seek from the bar moves the song there; a
        // place past its end stops short of it.
        private void SmokePlayerSeek(List<string> checks, string output)
        {
            string folder = System.IO.Path.Combine(output, "player-seek");
            System.IO.Directory.CreateDirectory(folder);
            string song = System.IO.Path.Combine(folder, "long.wav");
            using (System.IO.BinaryWriter wav = new System.IO.BinaryWriter(System.IO.File.Create(song)))
            {
                int rate = 8000, samples = rate * 40;
                wav.Write(new[] { 'R', 'I', 'F', 'F' }); wav.Write(36 + samples * 2); wav.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                wav.Write(16); wav.Write((short)1); wav.Write((short)1); wav.Write(rate); wav.Write(rate * 2); wav.Write((short)2); wav.Write((short)16);
                wav.Write(new[] { 'd', 'a', 't', 'a' }); wav.Write(samples * 2); wav.Write(new byte[samples * 2]);
            }
            Playlist listWas = playlist;
            double loudWas = FolderTunes.Loudness, nextWas = nextSongAt;
            bool noPlayerWas = preferences.NoPlayer, framedWas = mainframe.Visible;
            string keptWas = preferences.KeptSong;
            List<string> wrong = new List<string>();
            try
            {
                FolderTunes.Loudness = 0;
                preferences.NoPlayer = false;
                playlist = new Playlist(System.IO.Path.Combine(folder, "playlist.json"));
                playlist.AddFolder(folder);
                if (ContinuePlaylist() != "Playing now." || FolderTunes.Playing == null) { checks.Add("LIMIT her player's seek: her playlist couldn't play here (no Windows media playback)"); return; }
                Func<double, bool> until = delegate(double seconds)
                {
                    for (int i = 0; i < 40; i++) { if (FolderTunes.Seconds > 0) break; System.Threading.Thread.Sleep(50); }
                    Tuned(PlaylistTune(), clock.Elapsed.TotalSeconds, Tunes.Now);
                    return player.Tune != null;
                };
                if (!until(0) || !player.Tune.CanSeek || player.Tune.Length < 35) wrong.Add("her own song can't seek on the bar: " + (player.Tune == null ? "no song shown" : "can seek " + player.Tune.CanSeek + ", " + player.Tune.Length.ToString("0.0") + " s long"));
                else
                {
                    player.SeekTo(0.5);
                    System.Threading.Thread.Sleep(500);
                    double at = FolderTunes.At;
                    if (at < 19 || at > 23) wrong.Add("a seek to half way put the song at " + at.ToString("0.0") + " s of " + FolderTunes.Seconds.ToString("0.0"));
                    PlayerCommand("seek:9999");
                    System.Threading.Thread.Sleep(300);
                    if (FolderTunes.Playing == null && FolderTunes.At > FolderTunes.Seconds) wrong.Add("a seek past the end went past it");
                    PlayerCommand("seek:NaN");
                }
            }
            finally
            {
                FolderTunes.Stop();
                FolderTunes.Loudness = loudWas;
                playlist = listWas;
                nextSongAt = nextWas;
                preferences.NoPlayer = noPlayerWas;
                preferences.KeptSong = keptWas;
                HidePlayer();
                CloseTalk();
                if (!framedWas) mainframe.Hide();
            }
            if (wrong.Count > 0) throw new Exception("Her player's seek went wrong: " + string.Join("; ", wrong.ToArray()));
            checks.Add("PASS her player's bar seeks her own song: it can seek while a song with a length plays, a seek to half way puts it there, and a place past the end or not a number is held short or dropped");
        }
#endif

        // A frame drawn now as this clip, with her pulse on or off, her outline glow included as her frames get it.
        private Bitmap Drawn(bool pulsed, Clip shownAs)
        {
            bool was = preferences.BeatPulse;
            preferences.BeatPulse = pulsed;
            try
            {
                Bitmap picture = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
                using (Graphics graphics = Graphics.FromImage(picture)) Draw(graphics, shownAs, 0);
                if (Pulsing)
                {
                    BitmapData open = picture.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
                    try { Rim(open.Scan0, Width, Height); }
                    finally { picture.UnlockBits(open); }
                }
                return picture;
            }
            finally { preferences.BeatPulse = was; }
        }

        // The top and bottom rows of her solid pixels in a frame drawn now, with her pulse on or off.
        private int[] Spans(bool pulsed)
        {
            using (Bitmap picture = Drawn(pulsed, dance))
            {
                int top = -1, bottom = -1;
                for (int y = 0; y < picture.Height; y++)
                    for (int x = 0; x < picture.Width; x++)
                        if (picture.GetPixel(x, y).A >= 250) { if (top < 0) top = y; bottom = y; break; }
                return new[] { top, bottom };
            }
        }

        // What her pulse draws round her and not on her: how many pixels, their light added up, and their mean colour.
        private int[] Outside(Clip shownAs)
        {
            using (Bitmap bare = Drawn(false, shownAs))
            using (Bitmap lit = Drawn(true, shownAs))
            {
                int count = 0, light = 0;
                long red = 0, green = 0, blue = 0;
                for (int y = 0; y < lit.Height; y++)
                    for (int x = 0; x < lit.Width; x++)
                    {
                        if (bare.GetPixel(x, y).A != 0) continue;
                        Color pixel = lit.GetPixel(x, y);
                        if (pixel.A == 0) continue;
                        count++;
                        light += pixel.A;
                        red += pixel.R; green += pixel.G; blue += pixel.B;
                    }
                return new[] { count, light, count == 0 ? 0 : (int)(red / count), count == 0 ? 0 : (int)(green / count), count == 0 ? 0 : (int)(blue / count) };
            }
        }
    }

    // The Mainframe's edge light: a soft glow just outside its clipped edge that brightens on each beat of her pulse.
    // Its own click-through window owned by the Mainframe, so it moves and stacks with it, up only while her pulse
    // runs.
    internal sealed partial class Mainframe
    {
        private Halo halo;

        // on: her pulse is running. gain: how far into the song she is, 0 to 1. level: how hard this frame's beat lands,
        // 0 to 1. strength: -1 Soft, 0 Medium, 1 Strong.
        internal void BeatGlow(bool on, float gain, float level, int strength)
        {
            if (!on || !Visible || WindowState == FormWindowState.Minimized || !IsHandleCreated || IsDisposed)
            {
                if (halo != null && !halo.IsDisposed && halo.Visible) halo.Hide();
                return;
            }
            if (halo == null || halo.IsDisposed) halo = new Halo();
            // Lit a little between beats and brightest on them. Subtle at every strength: a soft edge light, not a flash.
            float most = strength < 0 ? 0.45f : strength > 0 ? 1f : 0.7f;
            float lit = gain * (0.25f + 0.75f * Math.Max(0, Math.Min(1, level))) * most;
            halo.Glow(this, Unit(), (byte)Math.Round(255 * Math.Max(0, Math.Min(1, lit))), strength);
        }

        internal string BeatGlowState { get { return halo == null ? "no glow window" : halo.IsDisposed ? "glow window disposed" : (halo.Visible ? "glow shown" : "glow hidden") + " at " + halo.Bounds + " lit " + halo.Lit; } }

        // The Mainframe's own scale, which the edge glow's reach is drawn at.
        internal float BeatGlowUnit { get { return Unit(); } }

        // How far the edge glow reaches out from the Mainframe, in pixels.
        internal int BeatGlowReach { get { return halo == null || halo.IsDisposed ? 0 : halo.Reached; } }

        // How bright the edge glow is right now, 0 while it is down.
        internal byte BeatGlowShown { get { return halo != null && !halo.IsDisposed && halo.Visible ? halo.Lit : (byte)0; } }

        private sealed class Halo : Form
        {
            private Native.Surface surface;
            private Size drawnSize;
            private float drawnScale;
            private Color drawnColour;
            private int drawnStrength;
            internal byte Lit;
            internal int Reached;

            internal Halo()
            {
                Text = "Mainframe glow";
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.None;
            }

            protected override bool ShowWithoutActivation { get { return true; } }

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams value = base.CreateParams;
                    value.ExStyle |= 0x00080000 | 0x00000080 | 0x08000000 | 0x00000020;
                    return value;
                }
            }

            // Every click goes through to what is under it, and nothing makes it the active window.
            protected override void WndProc(ref Message message)
            {
                if (message.Msg == 0x0084) { message.Result = new IntPtr(-1); return; }
                if (message.Msg == 0x0021) { message.Result = new IntPtr(3); return; }
                base.WndProc(ref message);
            }

            // How far the glow reaches out from the Mainframe's edge: a few pixels, an edge light rather than a halo.
            private static int Reach(float s, int strength) { return (int)Math.Ceiling((strength > 0 ? 5 : 4) * s); }

            internal void Glow(Form window, float s, byte lit, int strength)
            {
                int reach = Reach(s, strength);
                Rectangle bounds = Rectangle.Inflate(window.Bounds, reach, reach);
                Color colour = Neon.Hot;
                if (surface == null || drawnSize != bounds.Size || drawnScale != s || drawnColour != colour || drawnStrength != strength)
                    Draw(window.Size, bounds.Size, s, reach, colour, strength);
                if (Owner != window) Owner = window;
                if (!Visible) { Bounds = bounds; Show(); }
                surface.Show(Handle, bounds.Location, lit);
                Lit = lit;
                Reached = reach;
            }

            // Drawn once at full strength, then shown brighter or dimmer as a whole on each frame. Outside the Mainframe's
            // own clipped shape only: inside it is clear, so the Mainframe shows through untouched.
            private void Draw(Size window, Size size, float s, int reach, Color colour, int strength)
            {
                if (surface != null) surface.Dispose();
                surface = new Native.Surface(size.Width, size.Height);
                using (Graphics graphics = Graphics.FromImage(surface.Image))
                using (GraphicsPath edge = Neon.Cut(new RectangleF(reach, reach, window.Width, window.Height), 16 * s, 6 * s, 16 * s, 6 * s))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.CompositingMode = CompositingMode.SourceOver;
                    using (Region inside = new Region(edge))
                    {
                        graphics.ExcludeClip(inside);
                        // Strokes from wide and faint to narrow, so the light is strongest at the edge and fades out.
                        int each = strength < 0 ? 45 : strength > 0 ? 75 : 60;
                        for (int i = reach; i >= 1; i--)
                            using (Pen soft = new Pen(Color.FromArgb(each, colour), i * 2f) { LineJoin = LineJoin.Round })
                                graphics.DrawPath(soft, edge);
                        using (Pen strip = new Pen(Color.FromArgb(230, colour), Math.Max(1.5f, 1.5f * s)) { LineJoin = LineJoin.Round })
                            graphics.DrawPath(strip, edge);
                    }
                }
                drawnSize = size;
                drawnScale = s;
                drawnColour = colour;
                drawnStrength = strength;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && surface != null) { surface.Dispose(); surface = null; }
                base.Dispose(disposing);
            }
        }
    }
}
