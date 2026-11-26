using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace CapriDesktop
{
    // "Uninstall yourself", said or typed: her bubble asks, and Yes opens her uninstaller's own window, which removes
    // nothing until its Uninstall button. Their files stay, as Windows' own uninstall keeps them.
    internal sealed partial class Companion
    {
        // Starts the uninstaller with its arguments; a check swaps it so nothing real is ever removed.
        internal Func<string, string, bool> LaunchUninstaller = delegate(string program, string arguments)
        {
            using (Process.Start(new ProcessStartInfo(program, arguments) { UseShellExecute = true })) return true;
        };

        // Words only: her brain has no tool for it, so it never starts on its own.
        private string UninstallTool(ToolArguments args)
        {
            if (InvokeRequired) return (string)Invoke(new Func<ToolArguments, string>(UninstallTool), args);
            string uninstaller;
            string cannot = UninstallerFor(Path.GetDirectoryName(Application.ExecutablePath), CapriSetup.Program.InstallPath, Packaged.Is, out uninstaller);
            if (cannot != null) return cannot;
            AfterTool(delegate { AskUninstall(uninstaller); }, args.Reply, args.Ticket ?? asking);
            return "I'll ask whether to uninstall me once this reply is said.";
        }

        // Her uninstaller beside her in her marked install, or the line saying why there's none to open.
        internal static string UninstallerFor(string folder, string install, bool packaged, out string uninstaller)
        {
            uninstaller = null;
            if (packaged) return "I came from the Microsoft Store, so I'm uninstalled from Windows' Installed apps.";
            string here = Path.GetFullPath(folder).TrimEnd('\\'), home = Path.GetFullPath(install).TrimEnd('\\');
            string program = Path.Combine(here, "Uninstall.exe");
            if (!string.Equals(here, home, StringComparison.OrdinalIgnoreCase) || !File.Exists(Path.Combine(here, ".capri-install")) || !File.Exists(program))
                return "I'm not running from my install, so there's no uninstaller of mine here to open.";
            uninstaller = program;
            return null;
        }

        private void AskUninstall(string uninstaller)
        {
            if (quitting) return;
            if (box.Choosing) { Remark("", "Answer my other question first, then ask me again.", true); return; }
            Remark("", "Uninstall Capri?", true);
            box.Choose(delegate(bool yes)
            {
                if (!yes) { Brain.Log("uninstall", -1, "Asked to uninstall, and No was picked."); return; }
                try
                {
                    LaunchUninstaller(uninstaller, "--uninstall");
                    Brain.Log("uninstall", -1, "Asked to uninstall: opened " + uninstaller + ", which removes nothing until its Uninstall button.");
                }
                catch (Exception error) { Remark("", "I couldn't open my uninstaller: " + error.Message, true); }
            });
        }

        // Her words reach the tool and nothing else does; Yes opens the uninstaller once, No and a lapse open nothing; a
        // Store copy and one away from her install say so instead. The launch is a stand-in.
#if CHECKS
        private void SmokeUninstallYourself(List<string> checks)
        {
            foreach (string said in new[] { "uninstall yourself", "Uninstall yourself.", "capri, uninstall yourself please", "uninstall capri", "Uninstall Capri!" })
            {
                string tool;
                ToolArguments args;
                if (!Hands.Heard(said, out tool, out args) || tool != "uninstall") throw new Exception("\"" + said + "\" didn't reach uninstall: " + tool);
            }
            foreach (string said in new[] { "how do i uninstall capri", "uninstall chrome", "uninstall discord for me", "can you uninstall yourself later", "don't uninstall yourself" })
            {
                string tool;
                ToolArguments args;
                if (Hands.Heard(said, out tool, out args) && tool == "uninstall") throw new Exception("\"" + said + "\" reached uninstall.");
            }
            string folder = Path.Combine(Path.GetTempPath(), "capri-uninstall-check-" + Guid.NewGuid().ToString("N")), other = folder + "-other";
            Func<string, string, bool> launchWas = LaunchUninstaller;
            List<string> launched = new List<string>();
            try
            {
                Directory.CreateDirectory(folder);
                Directory.CreateDirectory(other);
                string uninstaller;
                if (UninstallerFor(folder, folder, true, out uninstaller) == null || uninstaller != null) throw new Exception("A Store copy offered an uninstaller.");
                if (UninstallerFor(folder, folder, false, out uninstaller) == null) throw new Exception("An install with no marker or Uninstall.exe offered an uninstaller.");
                File.WriteAllText(Path.Combine(folder, ".capri-install"), "");
                File.WriteAllText(Path.Combine(folder, "Uninstall.exe"), "");
                if (UninstallerFor(other, folder, false, out uninstaller) == null) throw new Exception("A copy away from her install offered its uninstaller.");
                if (UninstallerFor(folder, folder, false, out uninstaller) != null || uninstaller != Path.Combine(folder, "Uninstall.exe")) throw new Exception("Her marked install didn't offer its uninstaller: " + uninstaller);
                LaunchUninstaller = delegate(string program, string arguments) { launched.Add(program + " " + arguments); return true; };
                foreach (string answer in new[] { "yes", "no", "lapse" })
                {
                    box.Choose(null);
                    AskUninstall(uninstaller);
                    if (!box.Choosing) throw new Exception("Her bubble didn't put up Yes and No for \"Uninstall Capri?\".");
                    if (answer == "lapse") { box.ChoiceSince = DateTime.UtcNow.AddSeconds(-(ChoiceMost + 0.5)); ChoiceLapsed(); }
                    else box.Pick(answer == "yes");
                    if (box.Choosing) throw new Exception("Her Uninstall Capri? ask stayed up after " + answer + ".");
                }
                if (launched.Count != 1 || launched[0] != Path.Combine(folder, "Uninstall.exe") + " --uninstall") throw new Exception("Yes, No and a lapse opened her uninstaller " + launched.Count + " times: " + string.Join(" | ", launched.ToArray()));
                checks.Add("PASS uninstall yourself: her words (never her brain's own pick) ask \"Uninstall Capri?\" with Yes and No; Yes opens her uninstaller's own window once, No and a lapse open nothing; a Store copy and one away from her install say so instead");
            }
            finally
            {
                LaunchUninstaller = launchWas;
                box.Choose(null);
                foreach (string one in new[] { folder, other }) try { if (Directory.Exists(one)) Directory.Delete(one, true); } catch (IOException) { }
            }
        }
#endif
    }
}
