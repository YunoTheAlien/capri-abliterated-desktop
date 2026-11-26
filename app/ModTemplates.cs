using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CapriDesktop
{
    // The mods she carries as templates, laid as loose files into mods\templates (an app's into pluginsApps\templates) so
    // they can be seen, never read as mods: copied up a folder, one becomes a mod that starts off. Laid again whenever one
    // differs from what she carries.
    internal static class ModTemplates
    {
        internal const string FolderName = "templates";
        private const string Prefix = "Capri.Template.";
        // Her mods folder; the templates go in its templates folder and pluginsApps' own.
        internal static string Kept;

        // Where a template sits: an app's or a plug-in's in pluginsApps\templates, the rest in mods\templates; a file
        // in a template's own folder (its parts) beside the rest.
        internal static string Home(string name, string text)
        {
            if (name.IndexOf('/') >= 0) return Path.Combine(Kept, FolderName);
            string type = Mods.TypeOf(name, text);
            bool app = type.StartsWith(Mods.PluginsApps, StringComparison.OrdinalIgnoreCase);
            return app ? Path.Combine(Kept, Mods.PluginsApps, FolderName) : Path.Combine(Kept, FolderName);
        }

        // Each template she carries, by file name.
        internal static Dictionary<string, string> Carried()
        {
            Dictionary<string, string> found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in typeof(ModTemplates).Assembly.GetManifestResourceNames())
            {
                if (!name.StartsWith(Prefix, StringComparison.Ordinal)) continue;
                using (Stream built = typeof(ModTemplates).Assembly.GetManifestResourceStream(name))
                using (StreamReader reader = new StreamReader(built, Encoding.UTF8)) found[name.Substring(Prefix.Length)] = reader.ReadToEnd();
            }
            return found;
        }

        // Null when every template is laid, else why one isn't.
        internal static string Lay()
        {
            if (string.IsNullOrEmpty(Kept)) return "Her mods folder isn't ready yet.";
            try
            {
                foreach (KeyValuePair<string, string> one in Carried())
                {
                    string place = Home(one.Key, one.Value);
                    string path = Path.Combine(place, one.Key.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    if (!File.Exists(path) || File.ReadAllText(path) != one.Value) Tucked.Write(path, one.Value, new UTF8Encoding(false));
                }
                return null;
            }
            catch (IOException error) { return "Couldn't lay the mod templates: " + error.Message; }
            catch (UnauthorizedAccessException error) { return "Couldn't lay the mod templates: " + error.Message; }
        }
    }
}
