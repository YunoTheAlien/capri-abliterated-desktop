using System;
using System.IO;
using System.Text;

namespace CapriDesktop
{
    // MODDING.md, the modding guide she carries, laid into her mods folder and read in her file editor. Hers to read:
    // it is laid again whenever it differs from the one she carries.
    internal static class ModGuide
    {
        internal const string FileName = "MODDING.md";
        private const string Resource = "Capri.Modding.md";
        internal static string Kept;
        private static readonly object writing = new object();

        // The guide as built into her, or null in a copy without it.
        internal static string Text
        {
            get
            {
                using (Stream built = typeof(ModGuide).Assembly.GetManifestResourceStream(Resource))
                {
                    if (built == null) return null;
                    using (StreamReader reader = new StreamReader(built, Encoding.UTF8)) return reader.ReadToEnd();
                }
            }
        }

        // Null when the guide is in her mods folder to open, else why it isn't.
        internal static string Lay()
        {
            if (string.IsNullOrEmpty(Kept)) return "Her mods folder isn't ready yet.";
            string text = Text;
            if (text == null) return "This copy of her has no modding guide.";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Kept));
                if (!File.Exists(Kept) || File.ReadAllText(Kept) != text) Tucked.Write(Kept, text, new UTF8Encoding(false));
                return null;
            }
            catch (IOException error) { return "Couldn't lay the modding guide: " + error.Message; }
            catch (UnauthorizedAccessException error) { return "Couldn't lay the modding guide: " + error.Message; }
        }

        internal static WrittenNote Open() { return new WrittenNote(Kept, "", writing); }
    }
}
