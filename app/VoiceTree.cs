using System;
using System.Collections.Generic;

namespace CapriDesktop
{
    // VOICE on Settings from its trees, on her grid, in two parts: her Hearing row and her hidden voice sliders are her code
    // between them, and the mix rows under Her voice in your mic stay her code after.
    internal sealed partial class Mainframe
    {
        internal const string VoiceTree = "settings\n-VOICE\n--Voice settings [buttonSmall] x28 w328 h22 (command: voice settings)\n"
            + "--Volume [sliderRow] (setting: volume)\n--Sounds [sliderRow] (setting: sounds)\n--Typing sound [switch] (command: typing)\n"
            + "--Listen for Capri [switch] (command: listen capri)\n--Listen window [sliderRow] y312 (setting: listen window)\n";

        internal const string VoiceTalkTree = "settings\n-VOICE\n--I speak [dropdown] y335 English (command: languages)\n"
            + "--She answers in [dropdown] " + LanguagePair.SameName + " (command: answers)\n--Free voice in games [switch] y394 (command: hd games)\n";

        // Call mode's name holds brackets, which a tree line can't, so it stays her code between these two.
        internal const string VoiceMixTree = "settings\n-VOICE\n--Her voice in your mic [switch] y450 (command: voice in mic)\n";

        // What VOICE's rows reach: the calls her rows made before.
        private void VoiceWiring(Dictionary<string, Action> commands, Dictionary<string, string[]> steps, Dictionary<string, Action<int>> levels)
        {
            commands["voice settings"] = delegate { OpenLayers(); };
            commands["typing"] = delegate { capri.ToggleTyping(); };
            commands["listen capri"] = delegate { capri.ToggleListenCapri(); };
            commands["languages"] = delegate { ShowLanguages(); };
            commands["answers"] = delegate { ShowAnswers(); };
            commands["hd games"] = delegate { capri.ToggleHdGameRest(); };
            commands["voice in mic"] = delegate { capri.ToggleVoiceInMic(); };
            steps["volume"] = VolumeNames;
            steps["sounds"] = SoundNames;
            steps["listen window"] = ListenWindowNames;
            levels["volume"] = delegate(int at) { volumeName.Text = VolumeNames[at]; capri.SetVolume(Volumes[at]); };
            levels["sounds"] = delegate(int at) { soundsName.Text = SoundNames[at]; capri.SetSoundLevel(at * 10); };
            levels["listen window"] = delegate(int at) { listenWindowName.Text = ListenWindowNames[at]; capri.SetListenWindow(Companion.ListenWindows[at]); };
        }
    }
}
