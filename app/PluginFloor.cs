using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // The plug-in safety floor, whatever SECURITY says: no saved key reaches a plug-in or leaves in a call (Scrub,
    // Refuses); after plug-in or skill words, other plug-ins, mouse, sending and paying need a fresh yes (Heard,
    // Forces); a start line that downloads or hides code is refused (Hidden).
    internal static class PluginFloor
    {
        internal const string CarriesKey = "it would send one of her saved keys";
        private const RegexOptions Loose = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        internal static readonly Regex SecretName = new Regex(@"(?:^|_)(?:API_?KEYS?|KEYS?|TOKENS?|SECRETS?|PASSWORDS?|PASSWD|PASS|PAT|CREDENTIALS?|COOKIES?)(?:_|$)", Loose);
        private static readonly Regex Fetches = new Regex(@"\b(?:curl|wget|iwr|irm|invoke-webrequest|invoke-restmethod|bitsadmin|start-bitstransfer|certutil|downloadstring|downloadfile|downloaddata|urlopen|urlretrieve)\b", Loose);
        private static readonly Regex Runs = new Regex(@"\|\s*(?:sudo\s+)?(?:sh|bash|zsh|dash|ksh|fish|python[0-9.]*|py|node|perl|ruby|php|iex|pwsh|powershell)\b|\b(?:iex|invoke-expression)\b|\beval\b|\bexec\s*\(|<\(\s*(?:curl|wget)", Loose);
        private static readonly Regex Unpacks = new Regex(@"\bb64decode\b|\bfrombase64string\b|\bbase64\s+(?:-d|--decode)\b|\batob\s*\(|['""]base64['""]\s*\)", Loose);
        private static readonly Regex Shells = new Regex(@"^(?:powershell|pwsh)(?:\.exe)?$", Loose);
        private static readonly object gate = new object();
        private static readonly HashSet<string> heard = new HashSet<string>(StringComparer.Ordinal);
        // Who owns a plug-in tool her brain calls, and whether it only reads; Plugins sets it once tools run (P2).
        internal static Func<string, KeyValuePair<string, bool>> OwnerOf = delegate { return new KeyValuePair<string, bool>(null, false); };

        // Why a server's start line is refused, or null: code hidden in an encoded PowerShell block, or code downloaded or
        // unpacked and run as it starts, so what they approve isn't what would run.
        internal static string Hidden(PluginServer server)
        {
            string line = (server.Command ?? "") + " " + string.Join(" ", server.Args ?? new string[0]);
            if (Shells.IsMatch(Path.GetFileName(server.Command ?? "")) || Regex.IsMatch(line, @"\b(?:powershell|pwsh)(?:\.exe)?\b", Loose))
                foreach (string arg in server.Args ?? new string[0])
                    if (Encoded(arg)) return "runs PowerShell code hidden in an encoded block";
            if (Fetches.IsMatch(line) && Runs.IsMatch(line)) return "downloads code and runs it as it starts";
            if (Unpacks.IsMatch(line) && Runs.IsMatch(line)) return "unpacks hidden code and runs it as it starts";
            return null;
        }

        private static bool Encoded(string arg)
        {
            string flag = (arg ?? "").Trim().ToLowerInvariant();
            if (flag.Length < 2 || flag[0] != '-' && flag[0] != '/') return false;
            flag = flag.TrimStart('-', '/');
            return flag == "e" || flag == "ec" || flag.Length >= 2 && "encodedcommand".StartsWith(flag, StringComparison.Ordinal);
        }

        internal static string And(string[] names)
        {
            if (names.Length < 2) return string.Join("", names);
            return string.Join(", ", names, 0, names.Length - 1) + " and " + names[names.Length - 1];
        }

        // The variables named like keys a server's own lines pull from their PC (${NAME}), so the approval can say so.
        internal static string[] Pulled(PluginServer server)
        {
            List<string> found = new List<string>();
            List<string> places = new List<string> { server.Command, server.Cwd };
            places.AddRange(server.Args ?? new string[0]);
            places.AddRange(server.Env.Values);
            places.AddRange(server.Headers.Values);
            foreach (string place in places)
                foreach (Match one in Regex.Matches(place ?? "", @"\$\{([A-Za-z_][A-Za-z0-9_]*)"))
                {
                    string name = one.Groups[1].Value;
                    if (SecretName.IsMatch(name) && !found.Contains(name)) found.Add(name);
                }
            return found.ToArray();
        }

        // A server's environment with every variable named like a key, or holding one of her saved keys, taken out, but
        // the ones the plug-in names itself. What was taken out.
        internal static List<string> Scrub(StringDictionary env, ICollection<string> named)
        {
            List<string> gone = new List<string>();
            List<string> keys = new List<string>();
            foreach (string key in env.Keys) keys.Add(key);
            foreach (string key in keys)
            {
                if (Named(named, key)) continue;
                string value = env[key];
                if (SecretName.IsMatch(key) || !string.IsNullOrEmpty(value) && HoldsKey(value)) { env.Remove(key); gone.Add(key); }
            }
            return gone;
        }

        private static bool Named(ICollection<string> named, string key)
        {
            if (named == null) return false;
            foreach (string one in named) if (string.Equals(one, key, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // Whether text holds one of her saved keys: her log scrubber would take something out of it.
        internal static Func<string, bool> HoldsKey = delegate(string text) { return !string.IsNullOrEmpty(text) && Brain.Scrubbed(text) != text; };

        // A call refused because its words would carry one of her saved keys out, or null.
        internal static string Refuses(Dictionary<string, object> arguments)
        {
            if (arguments == null) return null;
            return HoldsKey(PluginJson.Write(arguments)) || HoldsKey(Values(arguments)) ? CarriesKey : null;
        }

        private static string Values(object value)
        {
            Dictionary<string, object> map = PluginJson.Map(value);
            if (map != null) { List<string> parts = new List<string>(); foreach (object one in map.Values) parts.Add(Values(one)); return string.Join("\n", parts.ToArray()); }
            List<object> list = PluginJson.List(value);
            if (list != null) { List<string> parts = new List<string>(); foreach (object one in list) parts.Add(Values(one)); return string.Join("\n", parts.ToArray()); }
            return value as string ?? "";
        }

        // Words from a plug-in ("plugin:<id>") or a skill came into this turn.
        internal static void Heard(string from)
        {
            if (string.IsNullOrEmpty(from)) return;
            lock (gate) heard.Add(from);
        }

        // A new turn from them: no plug-in words in it yet.
        internal static void Fresh() { lock (gate) heard.Clear(); }

        internal static string[] HeardFrom { get { lock (gate) return new List<string>(heard).ToArray(); } }

        // Whether this tool needs a fresh yes from them, because a plug-in's or a skill's words are in the turn: another
        // plug-in's tools (its own read-only tools are fine), her mouse and keyboard, sending and paying.
        internal static bool Forces(string tool, string pluginTool, int toggle)
        {
            lock (gate) if (heard.Count == 0) return false;
            if (tool == "plugin")
            {
                KeyValuePair<string, bool> owner = OwnerOf(pluginTool);
                if (owner.Key == null) return true;
                if (owner.Value) return false;
                lock (gate) foreach (string from in heard) if (from != "plugin:" + owner.Key) return true;
                return false;
            }
            return toggle == Security.Mouse || toggle == Security.Sends || toggle == Security.Pays;
        }
    }
}
