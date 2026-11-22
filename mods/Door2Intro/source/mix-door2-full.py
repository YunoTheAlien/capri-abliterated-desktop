# Door 2's whole sound in one file, beeps included, on the scene's clock with mix-door2.py's layers and levels; seeded,
# so the same layers make the same file.
# Run from the folder holding _SOUNDS: python _SOUNDS\mix-door2-full.py
import os
import random
import numpy as np
import soundfile as sf
from scipy.signal import resample_poly

layers = os.path.join("Capri", "sounds", "intro", "layers")
out = os.path.join("_SOUNDS", "door2-full.wav")
RATE = 48000
FADE = 0.8
END = 21.6
SIREN_IN, SIREN_END = 1.71, 19.6
SIREN, STATIC, BEAT = 0.88, 0.3375, 1.0
# The door starts to open: the fill is done at 10.0 and the opening starts 0.1 after (Filled + OpenGap in the mod).
BEAT_FROM = 10.1
ALERTS = ("nerv-alert-1.wav", "nerv-alert-2.wav", "nerv-alert-3.wav")
ALERT_LEVEL = 0.22
BEEP_FROM, BEEP_GAP, BEEP_SPREAD = 6.0, 1.4, 0.2
BEEP_FADE_FROM, BEEP_GONE = 17.5, 21.3
CROSS = 0.25


def load(name):
    data, rate = sf.read(os.path.join(layers, name), dtype="float64", always_2d=True)
    if rate != RATE:
        data = resample_poly(data, RATE, rate, axis=0)
    if data.shape[1] == 1:
        data = np.repeat(data, 2, axis=1)
    return data[:, :2]


def looped(clip, seconds):
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
mix = np.zeros((size, 2))

beat = looped(load("spatial-heartbeat.mp3"), END - BEAT_FROM) * BEAT
edge = int(0.01 * RATE)
beat[-edge:] *= np.linspace(1, 0, edge)[:, None]
mix[int(BEAT_FROM * RATE):int(BEAT_FROM * RATE) + len(beat)] += beat

static = faded(load("fuzzy-static.mp3")[: int(SIREN_END * RATE)] * STATIC)
mix[: len(static)] += static

siren = faded(looped(load("siren-head.mp3"), SIREN_END - SIREN_IN) * SIREN)
start = int(SIREN_IN * RATE)
mix[start:start + len(siren)] += siren

luck = random.Random(11)
clips = [load(name) * ALERT_LEVEL for name in ALERTS]
beeps = []
at = BEEP_FROM
while at < BEEP_GONE:
    level = 1 - min(1.0, max(0.0, (at - BEEP_FADE_FROM) / (BEEP_GONE - BEEP_FADE_FROM)))
    if level <= 0.034:
        break
    clip = clips[luck.randrange(len(clips))] * level
    begin = int(at * RATE)
    piece = clip[: max(0, size - begin)]
    mix[begin:begin + len(piece)] += piece
    beeps.append(round(at, 2))
    at += BEEP_GAP + luck.uniform(0, BEEP_SPREAD)

peak = np.abs(mix).max()
scale = 0.98 / peak if peak > 0.98 else 1.0
mix *= scale
sf.write(out, mix, RATE, subtype="PCM_16")
print("door2-full.wav", round(size / RATE, 2), "s, scale", round(scale, 4))
print("beeps at", beeps)
