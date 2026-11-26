using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace CapriDesktop
{
    // Her built-in files: her code's own first, then Capri.exe's, where her art, fonts, sounds and camera live so that file
    // changes only when one of them does. Built as one file, both are the same.
    internal sealed class OwnFiles
    {
        internal static readonly OwnFiles Both = new OwnFiles();
        private static readonly Assembly[] From = Find();

        private static Assembly[] Find()
        {
            Assembly core = typeof(OwnFiles).Assembly, entry = Assembly.GetEntryAssembly();
            return entry == null || entry == core ? new[] { core } : new[] { core, entry };
        }

        internal Stream GetManifestResourceStream(string name)
        {
            foreach (Assembly one in From)
            {
                Stream found = one.GetManifestResourceStream(name);
                if (found != null) return found;
            }
            return null;
        }

        internal ManifestResourceInfo GetManifestResourceInfo(string name)
        {
            foreach (Assembly one in From)
            {
                ManifestResourceInfo found = one.GetManifestResourceInfo(name);
                if (found != null) return found;
            }
            return null;
        }

        internal string[] GetManifestResourceNames()
        {
            List<string> all = new List<string>();
            foreach (Assembly one in From) all.AddRange(one.GetManifestResourceNames());
            return all.ToArray();
        }
    }
}
