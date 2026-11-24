# Third-party notices

## Husk mode

Took inspiration from Mochiiwuff's work when it was under an MIT Licence (capri-desktop 0.4.3, commit f839580), exclusively for husk mode.

## Speech models

Her own ears run [NVIDIA Parakeet TDT 0.6B v2](https://huggingface.co/nvidia/parakeet-tdt-0.6b-v2) for English and Parakeet TDT 0.6B Ultra for her other 24 languages: Ultra's weights are by Moondream, trained from [nvidia/parakeet-tdt-0.6b-v3](https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3). Both are licensed [CC-BY-4.0](https://creativecommons.org/licenses/by/4.0/), and the int8 ONNX exports are by sherpa-onnx (Apache-2.0). Neither NVIDIA nor Moondream endorses Capri.

Her free voice is [Kokoro-82M](https://huggingface.co/hexgrad/Kokoro-82M) by hexgrad (Apache-2.0), run through sherpa-onnx (Apache-2.0). Her LQ voice is k2-fsa's int8 conversion, kokoro-int8-multi-lang-v1_0 from the sherpa-onnx tts-models release (Apache-2.0); her Mixed voice is made from the same model with ONNX Runtime's quantisation (MIT).

## QR codes

Capri Remote's pairing QR code (app/RemoteQr.cs) is ported from Project Nayuki's [QR Code generator library](https://www.nayuki.io/page/qr-code-generator-library), Copyright (c) Project Nayuki, under the [MIT License](https://opensource.org/licenses/MIT). The licence text is in the file's header.

## Arch v0.8.6.1 native adaptations

The click animation is by **mixmix312352**, reused from the pinned upstream click strip. The stand/orientation, editor PanelArt and CapriMainframe-Bold font are reused from lachydotmcg/capri-desktop v0.8.6.1 under the existing project terms. Preserve upstream LICENSE.md and notices. Original Mochiiwuff source attribution remains https://github.com/mochiiwuff/capri-desktop. No generated visual media was added.

The Linux native font bootstrap adapts SkiaSharp’s MIT-licensed LibraryLoader isolation flag from PR2247, merge `482e6ee2913a08a7cad76520ccf5fbce97c7c23b`. Its source provenance and MIT license are retained in `docs/provenance/SkiaSharp`; packaged HarfBuzzSharp and SkiaSharp dependency pins are unchanged.

## Prank sounds

Three of her prank clips in sounds/pranks are from Valve's Portal games: glados_goodbye.mp3 and glados_surprise.mp3 are GLaDOS voice lines, and portal_stillalive.mp3 is from "Still Alive" by Jonathan Coulton, written for Portal. They remain the property of their rights holders, and neither Valve nor Jonathan Coulton endorses Capri.

## Word list

Her recall leaves out a match on common English words alone using the level 10 list of [SCOWL](http://wordlist.aspell.net/) (Spell Checker Oriented Word Lists, release 2020.12.07), in assets/words/common-words.txt. Copyright 2000-2018 by Kevin Atkinson, with the copyrights of its sources; the full notice is in assets/words/SCOWL-Copyright.txt. "Permission to use, copy, modify, distribute and sell these word lists, the associated scripts, the output created from the scripts, and its documentation for any purpose is hereby granted without fee, provided that the above copyright notice appears in all copies and that both that copyright notice and this permission notice appear in supporting documentation. Kevin Atkinson makes no representations about the suitability of this array for any purpose. It is provided "as is" without express or implied warranty."

## Spout2

Transparent streaming sends her through [Spout2](https://github.com/leadedge/Spout2) when OBS's Spout2 plugin is installed:
SpoutLibrary.dll from release 2.007.018 (native/spout), by Lynn Jarvis, under the BSD 2-Clause License:

```
BSD 2-Clause License

Copyright (c) 2020, Lynn Jarvis
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```
