#if CHECKS
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CapriDesktop
{
    // Test build only: her own Import and approval for each mod file given, one report line each, then out, for a run over
    // a folder of other people's mods on a test PC. Code mods are compiled and approved, not started; they run at her next start.
    internal static class ModImportRun
    {
        private static readonly string[] Kinds = { Mods.CodeExtension, Mods.SkinExtension, Mods.SkillExtension, Mods.Extension };

        // --import-mod <file or folder> <data folder> [report file]. 0 when every file came in, 1 when one didn't, 2 on bad use.
        internal static int ImportModsForTest(string[] args)
        {
            if (args.Length < 3 || args.Length > 4)
            {
                Console.Error.WriteLine("Use: Capri.exe --import-mod <mod file or folder> <data folder> [report file]");
                return 2;
            }
            string from = Path.GetFullPath(args[1]), data = Path.GetFullPath(args[2]);
            string report = args.Length == 4 ? Path.GetFullPath(args[3]) : Path.Combine(data, "import-report.txt");
            List<string> files = new List<string>();
            if (File.Exists(from)) files.Add(from);
            else if (Directory.Exists(from))
            {
                foreach (string one in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
                    if (Array.IndexOf(Kinds, Path.GetExtension(one).ToLowerInvariant()) >= 0 || Mods.IsPluginFile(one)) files.Add(one);
                files.Sort(StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                Console.Error.WriteLine("No mod file or folder at " + from);
                return 2;
            }
            Directory.CreateDirectory(data);
            Brain.LogPath = Path.Combine(data, "errors.txt");
            // As their "load untested mods" switch would: upstream's mods are not on her checked list.
            Mods.Untested = true;
            Mods list = new Mods(data);
            // Her files set up as at her start, so a pack that puts its own copy of one of them in can be imported.
            FileSlots.Setup(list);
            List<string> lines = new List<string> { "file\tid\tkind\timported\twhy or what it asked" };
            int bad = 0;
            foreach (string file in files)
            {
                string kind = Mods.IsPluginFile(file) ? "plug-in" : Path.GetExtension(file).TrimStart('.').Replace("capri-", "");
                string id = "", asked = "";
                bool came = false;
                try
                {
                    if (kind == "plug-in") throw new ModException("A plug-in goes in through the Mods page's own plug-in import, not this one.");
                    HashSet<string> keepOff = new HashSet<string>(StringComparer.Ordinal);
                    if (kind == "code")
                    {
                        id = Mods.ReadCode(Mods.Words(File.ReadAllBytes(file))).Id;
                        keepOff.Add(id);
                    }
                    Mod made = list.Import(file, delegate(ModFile read, string hash)
                    {
                        asked = "permissions: " + string.Join(", ", read.Permissions ?? new string[0]) + "; reaches: " + string.Join(" | ", read.Reaches ?? new string[0]);
                        return true;
                    }, kind == "mod" ? null : keepOff);
                    if (made == null) throw new ModException("Import gave nothing back.");
                    id = made.File.Id;
                    came = true;
                }
                catch (ModException refused) { asked = refused.Message; }
                catch (Exception broke) { asked = broke.GetType().Name + ": " + broke.Message; }
                if (!came) bad++;
                lines.Add(Path.GetFileName(file) + "\t" + id + "\t" + kind + "\t" + (came ? "yes" : "no") + "\t" + asked.Replace('\t', ' ').Replace("\r", " ").Replace("\n", " "));
            }
            Directory.CreateDirectory(Path.GetDirectoryName(report));
            File.WriteAllLines(report, lines.ToArray(), new UTF8Encoding(false));
            Console.WriteLine((files.Count - bad) + " of " + files.Count + " imported; report in " + report);
            return bad == 0 ? 0 : 1;
        }
    }
}
#endif
