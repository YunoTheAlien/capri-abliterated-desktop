# Door 2's sound, mixed like door 1's: static and heartbeat from the start, siren at SIREN_IN, heartbeat looped to the
# end, siren ending SIREN_GAP before it, static once at half level. Beeps are played by the mod (BeepLevel).
# Run: python mix-door2.py <folder holding the layers> <Door2Intro folder>
import os
import sys
import numpy as np
import soundfile as sf
from scipy.signal import resample_poly

layers, out = sys.argv[1], sys.argv[2]
RATE = 48000
FADE = 0.8
# The intro's length in the mod: Filled + EndAt.
END = 21.6
SIREN_IN = 1.71
SIREN_GAP = 2.0
SIREN = 0.88
# Half door 1's 0.675.
STATIC = 0.3375
BEAT = 1.0
ALERT_LEVEL = 0.22
CROSS = 0.25
# Door 2 plays its sound at this volume, as door 1 does.
PLAYED_AT = 0.35


def load(name):
    data, rate = sf.read(os.path.join(layers, name), dtype="float64", always_2d=True)
    if rate != RATE:
        data = resample_poly(data, RATE, rate, axis=0)
    if data.shape[1] == 1:
        data = np.repeat(data, 2, axis=1)
    return data[:, :2]


def looped(clip, seconds):
    # The clip again and again to the length, each join crossfaded over CROSS.
    want, cross = int(seconds * RATE), int(CROSS * RATE)
    made = clip.copy()
    while len(made) < want:
        ramp = np.linspace(0, 1, cross)[:, None]
        join = made[-cross:] * (1 - ramp) + clip[:cross] * ramp
        made = np.concatenate([made[:-cross], join, clip[cross:]])
    return made[:want]


def faded(clip):
    fade = int(FADE * RATE)
    clip[-fade:] *= np.linspace(1, 0, fade)[:, None]
    return clip


size = int(END * RATE)
siren_end = END - SIREN_GAP
siren = faded(looped(load("siren-head.mp3"), siren_end - SIREN_IN) * SIREN)
static = faded(load("fuzzy-static.mp3")[: int(siren_end * RATE)] * STATIC)
beat = looped(load("spatial-heartbeat.mp3"), END) * BEAT
edge = int(0.01 * RATE)
beat[-edge:] *= np.linspace(1, 0, edge)[:, None]

mix = np.zeros((size, 2))
mix[: len(beat)] += beat
mix[: len(static)] += static
start = int(SIREN_IN * RATE)
mix[start:start + len(siren)] += siren

peak = np.abs(mix).max()
scale = 0.98 / peak if peak > 0.98 else 1.0
mix *= scale
sf.write(os.path.join(out, "door2.wav"), mix, RATE, subtype="PCM_16")

for n in (1, 2, 3):
    sf.write(os.path.join(out, "warning-%d.wav" % n), load("nerv-alert-%d.wav" % n), RATE, subtype="PCM_16")
print("door2.wav", round(size / RATE, 2), "s, siren", SIREN_IN, "to", siren_end, "s, scale", round(scale, 4))
print("beep level", round(PLAYED_AT * ALERT_LEVEL * scale, 4))
