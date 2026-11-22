using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace CapriDesktop
{
    // Capri's own starter plug-ins (Email, Notion, Linear), recorded in keys\plugins\starters.txt as "id hash" lines,
    // gated like the sign-ins: only a plug-in on it, unchanged, may name one of her own servers (Plugins.BuiltIns).
    internal sealed partial class Mods
    {
        // The record's folder; the checks set their own.
        internal static string StartersFolder;
        // Laid in at her start; the checks make their own.
        internal static bool WithStarters;
        internal const string MailStarter = "email";

        // id, name, what it does, its server, its permissions, the tools that ask every time.
        private static readonly string[][] Starters =
        {
            new[] { MailStarter, "Email", "Checks, searches and reads your email, and writes mail for you, asking before every send. Works with Gmail, iCloud, Yahoo, Fastmail and others through an app password.", "\"mail\":{\"type\":\"capri\",\"builtin\":\"mail\"}", "\"accounts\",\"send\",\"network\"" },
            new[] { "notion", "Notion", "Searches, reads and edits your Notion pages and databases. You sign in to Notion in your browser.", "\"notion\":{\"type\":\"http\",\"url\":\"https://mcp.notion.com/mcp\"}", "\"accounts\",\"network\"" },
            new[] { "linear", "Linear", "Finds, makes and updates your Linear issues and projects. You sign in to Linear in your browser.", "\"linear\":{\"type\":\"http\",\"url\":\"https://mcp.linear.app/mcp\"}", "\"accounts\",\"network\"" }
        };

        // Each starter is laid in once, on install or update, never approved by her: off until they turn it on, and an
        // off one starts and asks nothing. One already made, kept or removed is never made again.
        internal void AddStarters()
        {
            Dictionary<string, string> made = StartersMade();
            bool wrote = false;
            foreach (string[] starter in Starters)
            {
                string id = starter[0], file = Path.Combine(PluginFolder, id + PluginExtension);
                if (made.ContainsKey(id) || File.Exists(file)) continue;
                try
                {
                    Directory.CreateDirectory(PluginFolder);
                    string manifest = "{\"name\":\"" + id + "\",\"displayName\":\"" + starter[1] + "\",\"version\":\"1.0.0\",\"author\":{\"name\":\"Capri\"},\"description\":" + PluginJson.Quote(starter[2])
                        + ",\"capri\":{\"permissions\":[" + starter[4] + "]}}";
                    string servers = "{\"mcpServers\":{" + starter[3] + "}}";
                    byte[] all = StarterZip(manifest, servers);
                    File.WriteAllBytes(file, all);
                    made[id] = HashOf(all);
                    wrote = true;
                }
                catch (IOException error) { Brain.Log("mods", -1, error.Message); }
                catch (UnauthorizedAccessException error) { Brain.Log("mods", -1, error.Message); }
            }
            if (!wrote) return;
            try { WriteStarters(made); }
            catch (IOException error) { Brain.Log("mods", -1, error.Message); }
            catch (UnauthorizedAccessException error) { Brain.Log("mods", -1, error.Message); }
        }

        // The same bytes every time: fixed entry times, so its hash is the one recorded.
        private static byte[] StarterZip(string manifest, string servers)
        {
            using (MemoryStream bytes = new MemoryStream())
            {
                using (ZipArchive zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
                    foreach (string[] entry in new[] { new[] { ".claude-plugin/plugin.json", manifest }, new[] { ".mcp.json", servers } })
                    {
                        ZipArchiveEntry made = zip.CreateEntry(entry[0]);
                        made.LastWriteTime = new DateTimeOffset(2026, 11, 22, 0, 0, 0, TimeSpan.Zero);
                        using (Stream into = made.Open())
                        {
                            byte[] text = new UTF8Encoding(false).GetBytes(entry[1]);
                            into.Write(text, 0, text.Length);
                        }
                    }
                return bytes.ToArray();
            }
        }

        // A starter's row, while it is off: what turning it on asks of them.
        internal static string StarterAbout(PluginInfo plugin)
        {
            if (plugin == null || !OwnMade(plugin.Id, plugin.Hash)) return null;
            if (Array.Exists(plugin.Servers, one => one.BuiltIn == "mail")) return "Turn it on, then give her an app password.";
            if (Array.Exists(plugin.Servers, one => one.Online)) return "Turn it on, then sign in, in your browser.";
            return null;
        }

        private static string StartersFile
        {
            get
            {
                string at = StartersFolder ?? McpSignIn.Home;
                return at == null ? null : Path.Combine(at, "starters.txt");
            }
        }

        internal static Dictionary<string, string> StartersMade()
        {
            Dictionary<string, string> made = new Dictionary<string, string>(StringComparer.Ordinal);
            string file = StartersFile;
            try
            {
                if (file == null || !File.Exists(file)) return made;
                foreach (string line in File.ReadAllLines(file))
                {
                    string[] parts = line.Trim().Split(' ');
                    if (parts.Length == 2) made[parts[0]] = parts[1];
                }
            }
            catch (IOException error) { Brain.Log("mods", -1, error.Message); }
            catch (UnauthorizedAccessException error) { Brain.Log("mods", -1, error.Message); }
            return made;
        }

        private static void WriteStarters(Dictionary<string, string> made)
        {
            string file = StartersFile;
            if (file == null) return;
            List<string> lines = new List<string>();
            foreach (KeyValuePair<string, string> pair in made) lines.Add(pair.Key + " " + pair.Value);
            McpSignIn.Gate(Path.GetDirectoryName(file));
            File.WriteAllLines(file, lines.ToArray());
        }

        // The mail setup's fields read into an account: the servers she knows for an address, or the ones typed.
        internal static MailAccount MailFromForm(string address, string password, string incoming, string outgoing, out string problem)
        {
            address = (address ?? "").Trim();
            problem = MailAccount.Unsupported(address);
            if (problem != null) return null;
            MailAccount account = MailAccount.ForAddress(address);
            if (!account.Known)
            {
                int port;
                string host = Host(incoming, 993, out port);
                account.ImapHost = host;
                account.ImapPort = port;
                host = Host(outgoing, 587, out port);
                account.SmtpHost = host;
                account.SmtpPort = port;
                account.SmtpImplicitTls = port == 465;
                account.Password = password ?? "";
            }
            else account.Password = (password ?? "").Replace(" ", "");
            return account;
        }

        private static string Host(string typed, int usual, out int port)
        {
            string text = (typed ?? "").Trim();
            port = usual;
            int colon = text.LastIndexOf(':');
            int given;
            if (colon > 0 && int.TryParse(text.Substring(colon + 1), out given) && given > 0 && given < 65536) { port = given; text = text.Substring(0, colon); }
            return text;
        }

        internal static bool NeedsHosts(string address)
        {
            address = (address ?? "").Trim();
            return address.IndexOf('@') > 0 && MailAccount.Unsupported(address) == null && !MailAccount.ForAddress(address).Known;
        }

        internal static string MailSetupHelp(string address)
        {
            string domain = MailAccount.Domain(address ?? "");
            if (domain == "gmail.com" || domain == "googlemail.com") return "Gmail: turn on 2-Step Verification, then make an app password at myaccount.google.com/apppasswords.";
            if (domain == "icloud.com" || domain == "me.com" || domain == "mac.com") return "iCloud: make an app-specific password at account.apple.com, under Sign-In and Security.";
            if (domain.StartsWith("yahoo.", StringComparison.Ordinal) || domain == "aol.com") return "Yahoo and AOL: make an app password under Account Security in your account settings.";
            if (domain == "fastmail.com" || domain == "fastmail.fm") return "Fastmail: make an app password under Settings, Privacy & Security.";
            return "Make an app password in your email account's security settings. Capri never uses your normal password.";
        }

        // A fresh install gets all three, none approved, each on her record as hers; an update lays in nothing again, keeps one
        // they turned on and never brings back one they took out.
        internal static void CheckStarters(List<string> checks, string output)
        {
            string data = Path.Combine(output, "starters-" + Guid.NewGuid().ToString("N").Substring(0, 6)), startersWas = StartersFolder, keptWas = ModDirectory.KeptFolder;
            try
            {
                Directory.CreateDirectory(data);
                StartersFolder = Path.Combine(data, "keys");
                Mods mods = new Mods(data);
                mods.AddStarters();
                List<PluginInfo> fresh = mods.InstalledPlugins();
                PluginInfo email = fresh.Find(one => one.Id == MailStarter);
                if (fresh.Count != 3 || email == null || !fresh.Exists(one => one.Id == "notion") || !fresh.Exists(one => one.Id == "linear"))
                    throw new Exception("A fresh install didn't get the three starters: " + string.Join(", ", fresh.ConvertAll(one => one.Id).ToArray()));
                foreach (PluginInfo one in fresh)
                    if (!OwnMade(one.Id, one.Hash) || mods.Approved().ContainsKey(ApprovalOf(one.Id)) || StarterAbout(one) == null)
                        throw new Exception("A starter wasn't laid in as hers and off: " + one.Id + ", hers " + OwnMade(one.Id, one.Hash) + ", approved " + mods.Approved().ContainsKey(ApprovalOf(one.Id)));
                if (email.Servers.Length != 1 || email.Servers[0].BuiltIn != "mail" || !Array.Exists(email.Reaches, one => one.Contains("asks you every time before it sends one")))
                    throw new Exception("Email didn't keep her mail tools: " + string.Join(" / ", email.Reaches));
                DateTime written = File.GetLastWriteTimeUtc(Path.Combine(mods.PluginFolder, "notion" + PluginExtension));
                mods.Approve(ApprovalOf("notion"), fresh.Find(one => one.Id == "notion").Hash);
                mods.RemovePlugin("linear");
                mods.AddStarters();
                List<PluginInfo> updated = mods.InstalledPlugins();
                PluginInfo notion = updated.Find(one => one.Id == "notion");
                if (updated.Exists(one => one.Id == "linear") || notion == null || notion.Changed || File.GetLastWriteTimeUtc(Path.Combine(mods.PluginFolder, "notion" + PluginExtension)) != written || !mods.Approved().ContainsKey(ApprovalOf("notion")))
                    throw new Exception("An update brought back a starter they took out, or touched one they turned on: " + string.Join(", ", updated.ConvertAll(one => one.Id + (one.Changed ? " (off)" : " (on)")).ToArray()));
                checks.Add("PASS her starters: a fresh install gets Email, Notion and Linear, none approved and each on her record as hers, Email with her mail tools; an update lays in nothing again, keeps one they turned on as it was and never brings back one they took out");
            }
            finally
            {
                StartersFolder = startersWas;
                ModDirectory.KeptFolder = keptWas;
                try { Directory.Delete(data, true); } catch (Exception) { }
            }
        }

        // A plug-in Capri made herself, unchanged since.
        internal static bool OwnMade(string id, string hash)
        {
            string made;
            return id != null && hash != null && StartersMade().TryGetValue(id, out made) && made == hash;
        }
    }
}
