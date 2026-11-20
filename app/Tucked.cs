using System;
using System.IO;

namespace CapriDesktop
{
    // Her senpai files, marked hidden on disk.
    //
    // They are already absent from a censored install: nothing senpai is written until the switch in sys.md is
    // turned on. Once it is on they exist, and somebody looking through her folder finds them. Hidden does not
    // stop anyone who wants to look, and it is not meant to: most people never turn hidden files on, so this
    // is the difference between finding them by accident and going to find them.
    //
    // Windows refuses to overwrite a file that already carries the attribute, which is a trap rather than a
    // feature: her top ups rewrite these on every start, and a write that throws would leave the file as it
    // was for ever. So every write to one goes through Free first, and Away puts the attribute back after.
    internal static class Tucked
    {
        // Clear the attribute so a write can land. Safe on a file that is not hidden and on one that is not
        // there yet, which is most of the times it is called.
        internal static void Free(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                FileAttributes had = File.GetAttributes(path);
                if ((had & FileAttributes.Hidden) != 0) File.SetAttributes(path, had & ~FileAttributes.Hidden);
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // Hide it again. Never fails loudly: a file that cannot be hidden is still a file she can read, and
        // the alternative is her falling over on a drive that does not do attributes.
        internal static void Away(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return;
                if (File.Exists(path))
                {
                    FileAttributes had = File.GetAttributes(path);
                    if ((had & FileAttributes.Hidden) == 0) File.SetAttributes(path, had | FileAttributes.Hidden);
                    return;
                }
                if (Directory.Exists(path))
                {
                    DirectoryInfo folder = new DirectoryInfo(path);
                    if ((folder.Attributes & FileAttributes.Hidden) == 0) folder.Attributes |= FileAttributes.Hidden;
                }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
        }

        // Write to one of them whatever its attribute says, and leave it hidden afterwards.
        internal static void Write(string path, string text, System.Text.Encoding how)
        {
            Free(path);
            File.WriteAllText(path, text, how);
            Away(path);
        }
    }
}
