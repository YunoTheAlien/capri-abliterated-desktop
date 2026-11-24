using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
#if !CROSS
using System.CodeDom.Compiler;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.CSharp;
#endif

namespace CapriDesktop
{
    // Code mods' files, approvals, starting and stopping, shared by both apps. Each app compiles them its own way: Windows with
    // its own C# compiler, Mac and Linux with Roslyn (cross/ModCompiler.cs).
    internal sealed partial class Mods
    {
        internal const string CodeExtension = ".capri-code";
        // A quarter of a megabyte of source was about six thousand lines, which a real mod can pass. Raised
        // now that source nobody can read is said outright rather than guessed from its size: the cap was
        // doing two jobs and only one of them was its own.
        private const int LargestCode = 1024 * 1024;
        private const string ApprovedFile = "approved.txt";
        private static readonly Regex HeaderLine = new Regex(@"^//\s*([a-z-]+)\s*:\s*(.*?)\s*$");

        // Code mods Capri's makers have checked: each by id and the SHA-256 of the exact file, line ends read as LF.
        // Built in, so nothing on the PC can add to it; the mods this build carries are on it as carried.
        private static readonly string[,] CheckedList =
        {
            { "auto-game-hologram", "636a671f75e6513991a883497acf908f569c0345e92190297468ebfc16077447" },
            { "capri-bezel-led", "d46da1632e1f63466cf5c7c80eae4743166bcf07ee9980e3d9ff790c4ed46a0b" },
            { "hologram-speech-glass", "d0ad7c2932d5f4ec6461b6846d22bd42182417916743d4756a60b31e5e76fd13" },
        };

        // What a checked mod's question and row say, and why an unchecked one is off while untested mods are.
        internal const string CheckedNote = "Checked by Capri's makers. Not a promise it's safe.";
        internal const string UncheckedNote = "Not tested by Capri's makers.";
        internal const string UncheckedOff = "not checked. Turn on Load untested mods";

        // Whether code mods off the list may ask to run: Load untested mods in Mods, kept in her settings. Her checks run with it
        // on, as they bring in mods of their own, except the check that is about it.
        internal static bool UntestedForChecks;
        internal static volatile bool Untested;
        internal static bool LoadUntested { get { return UntestedForChecks || Untested; } }

        private static Dictionary<string, string> carriedHashes;

        // A code mod's cog, kept beside the plug-ins' under its own id: the Runs row stops it at a level where it is crossed.
        // A tick or / lets it run, as SECURITY stops no code mod at any level.
        internal const string RunsRow = "runs";
        internal static string CogId(string id) { return "mod:" + id; }
        internal static bool StoppedHere(string id) { return PluginSettings.Now(CogId(id), PluginSettings.Run, RunsRow) == PluginSettings.Denied; }

        // SECURITY moved, or a cog's Runs row changed: each code mod that is on and stopped at this level stops, and each one
        // that is on and no longer stopped starts again.
        internal void Resecured(Action<Action> onWindow)
        {
            List<Mod> starting = new List<Mod>();
            foreach (Mod mod in All)
            {
                if (!mod.Code || !mod.On) continue;
                bool stopped = StoppedHere(mod.File.Id);
                if (stopped && running.ContainsKey(mod.File.Id)) StopOne(mod.File.Id);
                else if (!stopped && !running.ContainsKey(mod.File.Id)) starting.Add(mod);
            }
            if (starting.Count > 0) StartSome(starting, onWindow);
        }

        // Whether this exact file of this mod is on the checked list.
        internal static bool OnCheckedList(string id, byte[] file)
        {
            if (string.IsNullOrEmpty(id) || file == null) return false;
            string hash = CheckedHash(file);
            for (int i = 0; i < CheckedList.GetLength(0); i++)
                if (CheckedList[i, 0] == id && string.Equals(CheckedList[i, 1], hash, StringComparison.OrdinalIgnoreCase)) return true;
            Dictionary<string, string> carried = carriedHashes;
            if (carried == null)
            {
                carried = new Dictionary<string, string>(StringComparer.Ordinal);
                try
                {
                    Assembly carrying = typeof(Mods).Assembly;
                    foreach (string name in carrying.GetManifestResourceNames())
                    {
                        if (!name.StartsWith(CarriedPrefix, StringComparison.Ordinal) && !name.StartsWith(CheckedPrefix, StringComparison.Ordinal)) continue;
                        byte[] bytes;
                        using (Stream from = carrying.GetManifestResourceStream(name))
                        using (MemoryStream copy = new MemoryStream()) { from.CopyTo(copy); bytes = copy.ToArray(); }
                        try { carried[ReadCode(Words(bytes)).Id] = CheckedHash(bytes); }
                        catch (ModException) { }
                    }
                }
                catch (Exception caughtOne) { Brain.Caught(caughtOne); }
                carriedHashes = carried;
            }
            string own;
            return carried.TryGetValue(id, out own) && string.Equals(own, hash, StringComparison.OrdinalIgnoreCase);
        }

        // Mods carried only to be checked against, never laid in.
        private const string CheckedPrefix = "Capri.Checked.";

        // A file's hash for the list: its bytes with each CR LF read as LF.
        internal static string CheckedHash(byte[] file)
        {
            using (MemoryStream plain = new MemoryStream(file.Length))
            {
                for (int i = 0; i < file.Length; i++)
                    if (!(file[i] == 13 && i + 1 < file.Length && file[i + 1] == 10)) plain.WriteByte(file[i]);
                return HashOf(plain.ToArray());
            }
        }

        // Refused before anyone is asked: a mod off the list while untested mods are off.
        private static void RefuseUnchecked(ModFile read)
        {
            if (LoadUntested || read.Checked) return;
            throw new ModException((read.Name ?? read.Id) + " isn't on Capri's checked list, so it can't run while Load untested mods is off.");
        }

        // What a code mod reaches for, named so somebody approving it sees that without reading the source. Not a
        // block, since approval is the boundary: it is for a mod whose behaviour is packed, encrypted or fetched at run
        // time.
        internal const string Loads = "loads code while it runs", Packs = "unpacks hidden text", Contacts = "contacts ";
        internal const string ChangesSecurity = "changes Capri's security";
        internal const string WritesFiles = "writes or deletes files", Pieces = "builds names out of pieces, so this list may not see what they name";

        private static readonly string[,] Reaches =
        {
            // AppDomain only when it loads or runs something: its GetData and SetData are a slot mods share, nothing more.
            { Loads, @"Assembly\.Load|AssemblyLoadContext|AppDomain\.(?:CurrentDomain\.)?(?:Load|ExecuteAssembly|CreateInstance|DefineDynamicAssembly|CreateDomain)|ExecuteAssembly|CreateInstanceAndUnwrap|Reflection\.Emit|DynamicMethod|CompileAssembly|CodeProvider|CSharpCompilation" },
            { Packs, @"FromBase64String|Aes\.|AesManaged|Rijndael|TripleDES|DESCryptoServiceProvider|GZipStream|DeflateStream|BrotliStream" },
            // Windows' own lock on saved keys and cookies, which is how a mod would read one of hers back out.
            { "unlocks saved secrets", @"ProtectedData|CryptUnprotectData|\.dpapi\b" },
            // Where her plug-ins' sign-ins are kept: no mod has a reason to name it.
            { "reads plug-in sign-ins", @"McpSignIn|signin\.json|keys[\\/]+plugins" },
            // Her security overrides, the mods and plug-ins switched on, what was approved and the crash guard: only her own
            // windows change these.
            { ChangesSecurity, @"PluginSettings|keys[\\/]+overrides|""keys""\s*,\s*""overrides""|approved\.txt|approved-what\.txt|guard\.txt|plugins-on\.txt|Security\.(?:Pick|Set|Load)\b" },
            { "calls Windows directly", @"DllImport|LibraryImport|Marshal\.|GetProcAddress|VirtualAlloc" },
            { "starts programs", @"Process\.Start|ProcessStartInfo|cmd\.exe|powershell" },
            // Writing anywhere: a wipe of their documents must never read as nothing at all.
            { WritesFiles, @"File\.(?:Write\w*|Delete|Move|Copy|Create\w*|Append\w*|Replace|SetAttributes)\s*\(|Directory\.(?:Delete|Move)\s*\(|new\s+(?:System\.IO\.)?(?:FileStream|StreamWriter|FileInfo)\s*\(" },
            // Starting itself with Windows: the Run key, the Startup folder or a scheduled task.
            { "makes itself start with Windows", @"CurrentVersion\\{1,2}Run|SpecialFolder\.Startup\b|schtasks|TaskScheduler|\\{1,2}Start Menu\\{1,2}Programs\\{1,2}Startup" },
            // Anything above can be reached by its name in a string instead, which none of those rows can see:
            // Type.GetType("System.Diagnostics.Process").GetMethod("Start"). Named so a mod that calls by name
            // says it does, whatever it calls.
            { "calls code by its name", @"Type\.GetType\s*\(|\.GetMethod\s*\(|\.InvokeMember\s*\(|Activator\.CreateInstance|\.GetProperty\s*\(|\.GetField\s*\(" },
            { "uses the network", @"HttpClient|WebClient|WebRequest|TcpClient|UdpClient|Socket\(|ClientWebSocket" },
            { "reaches outside her folder", @"SpecialFolder|Registry|DriveInfo|GetTempPath|GetEnvironmentVariable" },
            { "reads the keyboard", @"GetAsyncKeyState|SetWindowsHookEx|keybd_event|SendKeys" },
            { "adds a Settings panel", @"\.AddPanel\s*\(" },
            // What her brain is told, which is the one a readout of permissions never used to mention. A mod
            // can ask her brain and can claim a pool of her lines, and both change what she is told or says.
            { "changes what her brain is told", @"ICapriBrain|ICapriChat|Zones\.|ChatMessage|AddTool|AddContext|\.Sending\b|\.Replying\b|ToolUsing|Personality\.(?:Add|Remove|Clear)\b|system prompt" },
        };

        // Every outside address in the source. "uses the network" says that it does; this says where, which
        // is the difference between a warning and a readout. Her own and the loopback are left out: those
        // are a mod talking to her rather than to somebody else.
        internal static string[] Addresses(string text)
        {
            List<string> found = new List<string>();
            if (string.IsNullOrEmpty(text)) return found.ToArray();
            // Three ways a mod can name somewhere: a full address, a bare host in a string, and a numeric
            // address for a mod that skips names altogether. The whole address is kept rather than the host
            // alone, so the readout says what it would fetch and not only who from.
            string[] ways =
            {
                @"[a-z][a-z0-9+.-]*://[A-Za-z0-9._:~%/?#@!$&()*+,;=-]+",
                @"\x22(?:[A-Za-z0-9-]+\.)+(?:com|net|org|io|dev|ai|co|me|app|cloud|xyz|ru|cn|info|biz|site|online|top|gg|tv|ly|sh)(?::[0-9]{1,5})?(?:/[^\x22]*)?\x22",
                @"\x22(?:[0-9]{1,3}\.){3}[0-9]{1,3}(?::[0-9]{1,5})?\x22",
            };
            foreach (string way in ways)
                foreach (Match one in Regex.Matches(text, way, RegexOptions.IgnoreCase))
                {
                    string said = one.Value.Trim('"').Trim();
                    string low = said.ToLowerInvariant();
                    if (low.Contains("localhost") || low.Contains("127.0.0.1") || low.StartsWith("0.0.0.0")) continue;
                    if (low.Contains("schemas.microsoft.com") || low.Contains("www.w3.org")) continue;
                    if (said.Length >= 4 && !found.Contains(said)) found.Add(said);
                }
            return found.ToArray();
        }

        // The site an address is on, lowercased: "https://EVIL.com/x.exe" and "evil.com/y" are both evil.com.
        internal static string Site(string address)
        {
            string rest = address ?? "";
            int scheme = rest.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0) rest = rest.Substring(scheme + 3);
            int at = rest.LastIndexOf('@', Math.Max(0, Math.Min(rest.Length - 1, IndexOfAny(rest, "/?#"))));
            if (at >= 0) rest = rest.Substring(at + 1);
            int end = IndexOfAny(rest, "/?#:");
            return (end < 0 ? rest : rest.Substring(0, end)).ToLowerInvariant();
        }

        private static int IndexOfAny(string text, string any)
        {
            int found = text.IndexOfAny(any.ToCharArray());
            return found < 0 ? text.Length : found;
        }

        // Source with comments taken out and strings left in, for her reach and address checks: a comment can't run,
        // but a string can be handed to something that runs it.
        internal static string Uncommented(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            StringBuilder kept = new StringBuilder(text.Length);
            for (int at = 0; at < text.Length; at++)
            {
                char one = text[at];
                if (one == '"' || one == '\'')
                {
                    bool verbatim = one == '"' && at > 0 && text[at - 1] == '@';
                    kept.Append(one);
                    for (at++; at < text.Length; at++)
                    {
                        char inside = text[at];
                        kept.Append(inside);
                        if (verbatim)
                        {
                            if (inside == '"')
                            {
                                if (at + 1 < text.Length && text[at + 1] == '"') { kept.Append(text[++at]); continue; }
                                break;
                            }
                            continue;
                        }
                        if (inside == '\\' && at + 1 < text.Length) { kept.Append(text[++at]); continue; }
                        if (inside == one || inside == '\n') break;
                    }
                    continue;
                }
                if (one == '/' && at + 1 < text.Length && text[at + 1] == '/')
                {
                    while (at < text.Length && text[at] != '\n') at++;
                    kept.Append('\n');
                    continue;
                }
                if (one == '/' && at + 1 < text.Length && text[at + 1] == '*')
                {
                    int opened = at;
                    for (at += 2; at < text.Length && !(text[at] == '/' && text[at - 1] == '*' && at - 1 > opened + 1); at++) { }
                    kept.Append(' ');
                    continue;
                }
                kept.Append(one);
            }
            return kept.ToString();
        }

        // Minified source laid back out so a person can read it before approving: exact, not a guess, since braces,
        // semicolons and strings delimit everything. Strings, chars and comments are copied through untouched.
        internal static string Tidied(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            StringBuilder made = new StringBuilder();
            int deep = 0;
            bool fresh = true;
            for (int at = 0; at < text.Length; at++)
            {
                char one = text[at];

                // Anything quoted goes through as it is.
                if (one == '"' || one == '\'')
                {
                    bool verbatim = one == '"' && at > 0 && text[at - 1] == '@';
                    if (fresh) { Indent(made, deep); fresh = false; }
                    made.Append(one);
                    for (at++; at < text.Length; at++)
                    {
                        char inside = text[at];
                        made.Append(inside);
                        if (verbatim)
                        {
                            if (inside == '"')
                            {
                                if (at + 1 < text.Length && text[at + 1] == '"') { made.Append(text[++at]); continue; }
                                break;
                            }
                            continue;
                        }
                        if (inside == '\\' && at + 1 < text.Length) { made.Append(text[++at]); continue; }
                        if (inside == one) break;
                    }
                    continue;
                }

                // A comment to the end of its line, or a block comment, both as they are.
                if (one == '/' && at + 1 < text.Length && text[at + 1] == '/')
                {
                    if (fresh) { Indent(made, deep); fresh = false; }
                    while (at < text.Length && text[at] != '\n') made.Append(text[at++]);
                    made.Append(Environment.NewLine);
                    fresh = true;
                    continue;
                }
                if (one == '/' && at + 1 < text.Length && text[at + 1] == '*')
                {
                    if (fresh) { Indent(made, deep); fresh = false; }
                    made.Append(one);
                    for (at++; at < text.Length; at++)
                    {
                        made.Append(text[at]);
                        if (text[at] == '/' && text[at - 1] == '*') break;
                    }
                    made.Append(Environment.NewLine);
                    fresh = true;
                    continue;
                }

                if (one == '\n' || one == '\r') { if (!fresh) { made.Append(Environment.NewLine); fresh = true; } continue; }
                if ((one == ' ' || one == '\t') && fresh) continue;

                if (one == '}')
                {
                    if (!fresh) { made.Append(Environment.NewLine); }
                    deep = deep > 0 ? deep - 1 : 0;
                    Indent(made, deep);
                    made.Append(one);
                    made.Append(Environment.NewLine);
                    fresh = true;
                    continue;
                }

                if (fresh) { Indent(made, deep); fresh = false; }
                made.Append(one);

                if (one == '{') { deep++; made.Append(Environment.NewLine); fresh = true; continue; }
                if (one == ';') { made.Append(Environment.NewLine); fresh = true; continue; }
            }
            return made.ToString();
        }

        private static void Indent(StringBuilder made, int deep)
        {
            for (int one = 0; one < deep; one++) made.Append("    ");
        }

        // Whether a person can actually read the source: minified, one-line or long base64 code isn't disclosed by
        // naming what it reaches for. Said rather than refused, so the person deciding knows.
        internal static string Unreadable(string text)
        {
            bool packed;
            return Unreadable(text, out packed);
        }

        // packed: the reason is a long run of encoded data rather than a layout, which no tidying can open.
        internal static string Unreadable(string text, out bool packed)
        {
            packed = false;
            if (string.IsNullOrEmpty(text)) return null;
            // Packed data first: a long run of it is also a long line, and read as one it would be offered a
            // tidy copy that could never open it.
            Match run = Regex.Match(text, @"[A-Za-z0-9+/]{800,}={0,2}");
            packed = run.Success;
            if (run.Success) return "a " + run.Value.Length + " character packed blob";
            string[] rows = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            int longest = 0, code = 0;
            foreach (string row in rows)
            {
                if (row.Trim().Length > 0) code++;
                if (row.Length > longest) longest = row.Length;
            }
            if (longest > 600) return "one line of " + longest + " characters";
            if (code <= 3 && text.Length > 4000) return "all on " + code + " line(s)";
            int spaces = 0;
            foreach (char one in text) if (one == ' ' || one == '\t' || one == '\n') spaces++;
            if (text.Length > 4000 && spaces * 20 < text.Length) return "minified";
            return null;
        }

        // The list for one mod's source, in the order above, each named once.
        // "// assets: door.png, knock.wav": up to 40 file names, each a plain name of up to 80 characters.
        internal static string[] ReadAssets(string value)
        {
            List<string> names = new List<string>();
            foreach (string one in (value ?? "").Split(','))
            {
                string name = one.Trim();
                if (name.Length == 0) continue;
                if (name.Length > 80 || name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0) throw new ModException("An assets line names files by name only, up to 80 characters each: " + name);
                if (!names.Contains(name)) names.Add(name);
            }
            if (names.Count > 40) throw new ModException("An assets line names up to 40 files.");
            return names.ToArray();
        }

        internal static string[] Reaching(string text)
        {
            List<string> found = new List<string>();
            if (string.IsNullOrEmpty(text)) return found.ToArray();
            // Code and strings only, blind to case. A packed payload sits in a string, so strings are read; a
            // comment cannot run, so it is not. Case is ignored because Windows ignores it in "CMD.EXE".
            string code = Uncommented(text);
            for (int at = 0; at < Reaches.GetLength(0); at++)
                if (Regex.IsMatch(code, Reaches[at, 1], RegexOptions.IgnoreCase)) found.Add(Reaches[at, 0]);
            if (Pieced(code)) found.Add(Pieces);
            return found.ToArray();
        }

        // "Pro" + "cess.Start": a name only whole once its pieces are joined, which is how a name gets past a list like the
        // one above. Literals joined with + are put together and read again; a piece that already names it alone is not this.
        private static readonly Regex Joined = new Regex(@"""(?:[^""\\\r\n]|\\.){1,40}""(?:\s*\+\s*""(?:[^""\\\r\n]|\\.){1,40}"")+");
        private static readonly Regex Named = new Regex(@"Process|ProcessStartInfo|Assembly|Registry|FromBase64|Invoke|kernel32|user32|ntdll|cmd\.exe|powershell|ProtectedData|Activator|GetMethod|Load|Delete", RegexOptions.IgnoreCase);

        private static bool Pieced(string code)
        {
            foreach (Match chain in Joined.Matches(code))
            {
                List<string> parts = new List<string>();
                foreach (Match part in Regex.Matches(chain.Value, @"""((?:[^""\\\r\n]|\\.)*)""")) parts.Add(part.Groups[1].Value);
                string whole = string.Join("", parts.ToArray());
                Match name = Named.Match(whole);
                if (!name.Success) continue;
                bool alone = false;
                foreach (string part in parts) if (part.IndexOf(name.Value, StringComparison.OrdinalIgnoreCase) >= 0) alone = true;
                if (!alone) return true;
            }
            return false;
        }

        // That the list says what the source does, including the shape this is for: a mod that looks empty
        // and unpacks itself.
        internal static void ReachCheck(System.Collections.Generic.List<string> checks)
        {
            string header = "// capri-mod: 1\n// id: " + "quiet" + "\n// name: test\n// version: 1\n// author: Smoke\n// description: A test.\n";
            string packed = header + "using System;\nclass M { void Go() { var b = Convert.FromBase64String(\"...\"); "
                + "System.Reflection.Assembly.Load(b); } }";
            string plain = "// capri-mod: 1\n// id: " + "tidy" + "\n// name: test\n// version: 1\n// author: Smoke\n// description: A test.\n" + "using System;\nclass M { void Go() { Console.Write(\"hello\"); } }";
            string[] packedSays = Reaching(packed), plainSays = Reaching(plain);
            if (Array.IndexOf(packedSays, "loads code while it runs") < 0 || Array.IndexOf(packedSays, "unpacks hidden text") < 0)
                throw new Exception("A mod that unpacks and loads code was not named as doing either: " + string.Join(", ", packedSays));
            if (plainSays.Length != 0)
                throw new Exception("A mod that does nothing of the sort was named anyway: " + string.Join(", ", plainSays));
            // And the pair that has no fair use is refused outright rather than listed.
            string refused = null;
            try { ReadCode(packed); }
            catch (ModException stopped) { refused = stopped.Message; }
            if (refused == null || refused.IndexOf("not what would run", StringComparison.Ordinal) < 0)
                throw new Exception("A mod that unpacks hidden text and loads code was approved anyway: " + (refused ?? "no refusal"));
            try { ReadCode(plain); }
            catch (ModException fine) { throw new Exception("An ordinary mod was refused: " + fine.Message); }

            // A comment cannot run, so prose in one names nothing; a string can, in any case.
            string talks = "// capri-mod: 1\n// id: talks\n// name: test\n// version: 1\n// author: Smoke\n// description: A test.\n"
                + "using System;\n// Unlike process.start, this never opens cmd or powershell.\n/* Process.Start is not used. */\nclass M { void Go() { Console.Write(\"hi\"); } }";
            string shouts = "// capri-mod: 1\n// id: shouts\n// name: test\n// version: 1\n// author: Smoke\n// description: A test.\n"
                + "using System;\nclass M { string a = \"CMD.EXE\", b = \"https://EVIL.com/x.exe\", c = \"https://github.com/one/a\", d = \"https://github.com/two/b\"; }";
            string[] talkSays = Reaching(talks);
            if (talkSays.Length != 0) throw new Exception("Words in a comment were read as what the mod does: " + string.Join(", ", talkSays));
            ModFile shouted = ReadCode(shouts);
            if (Array.IndexOf(shouted.Reaches, "starts programs") < 0) throw new Exception("\"CMD.EXE\" in a string was missed because of its case.");
            // Code that cannot be read is asked about twice: laid out badly, with a readable copy; packed, only when
            // it could also run what it carries.
            string head = "// capri-mod: 1\n// id: hidden\n// name: test\n// version: 1\n// author: Smoke\n// description: A test.\n";
            string squashed = head + "using System;class M{" + new string('x', 0) + string.Concat(System.Linq.Enumerable.Repeat("void A(){int a=1;a++;}", 60)) + "}";
            string blob = new string('A', 900);
            string picture = head + "using System;\nclass M { string art = \"" + blob + "\"; }";
            string runner = head + "using System;\nclass M { string art = \"" + blob + "\"; void Go() { System.Diagnostics.Process.Start(\"x\"); } }";
            ModFile squashedRead = ReadCode(squashed), pictureRead = ReadCode(picture), runnerRead = ReadCode(runner);
            if (!squashedRead.AskTwice || squashedRead.Tidy == null || Unclear(squashedRead).IndexOf("-readable.cs", StringComparison.Ordinal) < 0)
                throw new Exception("Code on one long line was not asked about twice with a readable copy.");
            if (pictureRead.AskTwice || pictureRead.Unreadable == null) throw new Exception("Packed data in a mod that runs nothing was asked about twice, or not noticed.");
            if (!runnerRead.AskTwice || runnerRead.Tidy != null) throw new Exception("Packed data in a mod that starts programs was not asked about twice.");
            checks.Add("PASS code that cannot be read gets a second question: laid out badly, with a readable copy saved to read first; packed, only when the mod could also run what it carries");
            string named = "using System;\nclass M { void Go() { Type.GetType(\"System.Diagnostics.Process\").GetMethod(\"Start\", new[] { typeof(string) }).Invoke(null, new object[] { \"notepad\" }); } }";
            if (Array.IndexOf(Reaching(named), "calls code by its name") < 0) throw new Exception("Starting a program by its name through reflection was not named.");
            string contacts = Array.Find(shouted.Reaches, one => one.StartsWith(Contacts, StringComparison.Ordinal)) ?? "";
            if (contacts != Contacts + "evil.com, github.com" || shouted.Places.Length != 3)
                throw new Exception("Addresses were not one line per site with every address kept: [" + contacts + "] and " + shouted.Places.Length + " addresses");
            string asked = Consent(shouted, "0");
            if (asked.IndexOf("contacts github.com\n        https://github.com/one/a\n        https://github.com/two/b", StringComparison.Ordinal) < 0)
                throw new Exception("The question did not list each address under its site: " + asked);
            checks.Add("PASS a code mod's reach is read from its code and strings, blind to case, and not from its comments: \"CMD.EXE\" in a string is named, \"process.start\" in a comment is not; the mods list says each site once and the question lists every address under its site");

            checks.Add("PASS a code mod that both unpacks hidden text and loads code while it runs is refused, since the source somebody reads is then not what runs; the other six reaches are named and left to them");
            checks.Add("PASS a code mod says what it reaches for before it is approved: loading code while it runs, unpacking hidden text, calling Windows directly, starting programs, using the network, reaching outside her folder, reading the keyboard. A mod whose source reads as nothing because the payload is packed is the case it exists for");

            // Stage 4: writing files, starting with Windows and names built from pieces are named; AppDomain's shared slot
            // is not loading code; and the compiled code is read for the calls its source hides behind an alias.
            Func<string, string[]> scan = delegate(string body) { return Reaching(header + "using System;\nclass M { void Go() { " + body + " } }"); };
            if (Array.IndexOf(scan("System.IO.File.Delete(\"x\");"), WritesFiles) < 0 || Array.IndexOf(scan("new System.IO.StreamWriter(\"x\");"), WritesFiles) < 0)
                throw new Exception("Writing or deleting a file wasn't named.");
            if (Array.IndexOf(scan("string k = @\"Software\\Microsoft\\Windows\\CurrentVersion\\Run\";"), "makes itself start with Windows") < 0)
                throw new Exception("Starting with Windows from the Run key wasn't named.");
            if (Array.IndexOf(scan("string p = \"Pro\" + \"cess.Start\";"), Pieces) < 0 || Array.IndexOf(scan("string p = \"Process\" + \".Start\";"), Pieces) >= 0
                || Array.IndexOf(scan("string p = \"Hello \" + \"there\";"), Pieces) >= 0)
                throw new Exception("A name built from pieces was missed, or a whole one or plain words were taken for one.");
            if (Array.IndexOf(scan("object s = AppDomain.CurrentDomain.GetData(\"k\"); AppDomain.CurrentDomain.SetData(\"k\", 1);"), Loads) >= 0
                || Array.IndexOf(scan("AppDomain.CurrentDomain.Load(new byte[0]);"), Loads) < 0)
                throw new Exception("AppDomain's shared slot was taken for loading code, or a real load was missed.");
#if !CROSS
            string aliased = header + "using System; using CapriDesktop; using F = System.IO.File; using P = System.Diagnostics.Process;\n"
                + "public class M : ICapriMod { public void Start(ICapriHost h) { F.Delete(\"nothing-here\"); P.Start(\"nothing-here\"); } public bool Heard(string w) { return false; } public void Stop() { } }";
            string[] sourceSays = Reaching(aliased), compiledSays = CompiledCalls(Build(aliased));
            if (Array.IndexOf(sourceSays, WritesFiles) >= 0 || Array.IndexOf(compiledSays, WritesFiles) < 0 || Array.IndexOf(compiledSays, "starts programs") < 0)
                throw new Exception("The compiled code's calls behind an alias weren't found: source " + string.Join(", ", sourceSays) + "; compiled " + string.Join(", ", compiledSays));
#endif
            // Stage 3: art carried as packed text and named on an assets line is said as that and asks nothing twice; the
            // same blob unnamed still asks; and naming assets never lets a mod that unpacks and loads code through.
            string artBlob = new string('Q', 3000);
            string art = "// capri-mod: 1\n// id: art\n// name: Art\n// version: 1\n// author: Smoke\n// description: A test.\n// assets: door.png, knock.wav\n"
                + "using System;\nclass M { byte[] Door() { return Convert.FromBase64String(\"" + artBlob + "\"); } }";
            ModFile withAssets = ReadCode(art), withoutAssets = ReadCode(art.Replace("// assets: door.png, knock.wav\n", ""));
            if (withAssets.Assets.Length != 2 || Array.IndexOf(withAssets.Reaches, Packs) >= 0 || withAssets.AskTwice || withAssets.Unreadable != null
                || !Array.Exists(withAssets.Reaches, each => each.StartsWith("carries 2 files", StringComparison.Ordinal)))
                throw new Exception("Art named on an assets line was still read as hidden text: " + string.Join(" | ", withAssets.Reaches));
            if (withoutAssets.Unreadable == null || Array.IndexOf(withoutAssets.Reaches, Packs) < 0)
                throw new Exception("The same packed blob with no assets line wasn't said as hidden text.");
            string sneaky = null;
            try { ReadCode(art.Replace("return Convert.FromBase64String", "System.Reflection.Assembly.Load(Convert.FromBase64String(\"x\")); return Convert.FromBase64String")); }
            catch (ModException stopped) { sneaky = stopped.Message; }
            if (sneaky == null) throw new Exception("Naming assets let a mod that unpacks and loads code through.");
            checks.Add("PASS stage 3: art carried packed and named on an assets line is said as that and asks nothing twice, the same blob unnamed still asks, and naming assets never lets a mod that unpacks and loads code through");
            checks.Add("PASS stage 4: writing or deleting files, starting with Windows and names built from pieces are named; AppDomain's shared slot isn't taken for loading code; and the compiled code is read for calls its source hides behind an alias, before anyone is asked and without running any of it");
        }
        private readonly Dictionary<string, ICapriMod> running = new Dictionary<string, ICapriMod>();
        private readonly Dictionary<string, ICapriHost> hosts = new Dictionary<string, ICapriHost>();
        internal Func<string, ICapriHost> HostFor;

        internal int Running { get { return running.Count; } }
        internal bool WantsIntro { get { return All.Exists(mod => mod.Code && mod.On && mod.File.Intro); } }
        internal bool CodeSettled { get; private set; }

        // Mac and Linux refuse a mod that needs Windows-only parts before asking to run it, and let go of a stopped mod's code.
        // Windows has nothing to do for either, so these calls compile away there.
        partial void CheckPlatform(string text);
        partial void Unload(ICapriMod mod);

#if !CROSS
        // The intro she starts with: one at random from every intro mod with something switched on; null when none has,
        // so she arrives the ordinary way.
        internal ICapriIntro Intro
        {
            get
            {
                List<ICapriIntro> on = new List<ICapriIntro>();
                foreach (ICapriMod mod in running.Values)
                {
                    ICapriIntro intro = mod as ICapriIntro;
                    if (intro == null) continue;
                    double seconds = 0;
                    try { seconds = intro.Seconds; }
                    catch (Exception caughtOne) { Brain.Caught(caughtOne); }
                    if (seconds > 0) on.Add(intro);
                }
                return on.Count == 0 ? null : on[introPick.Next(on.Count)];
            }
        }

        private readonly Random introPick = new Random();

        // One mod's own intro, for a mod that asks to play itself.
        // The id a running mod was started under, or null.
        internal string IdOf(object mod)
        {
            foreach (KeyValuePair<string, ICapriMod> one in running) if (ReferenceEquals(one.Value, mod)) return one.Key;
            return null;
        }

        internal ICapriIntro IntroOf(string id)
        {
            ICapriMod mod;
            return running.TryGetValue(id ?? "", out mod) ? mod as ICapriIntro : null;
        }

        // Every code mod running now, for her log: its id, its class and whether it is an intro, with the names it gives.
        internal string RunningNow()
        {
            List<string> said = new List<string>();
            foreach (KeyValuePair<string, ICapriMod> entry in running)
            {
                string names = "";
                ICapriIntroNames listed = entry.Value as ICapriIntroNames;
                try { if (listed != null && listed.Intros != null) names = " " + string.Join("/", listed.Intros); }
                catch (Exception caughtOne) { Brain.Caught(caughtOne); }
                said.Add(entry.Key + " " + entry.Value.GetType().Name + (entry.Value is ICapriIntro ? " intro" + names : ""));
            }
            return said.Count == 0 ? "none" : string.Join(", ", said.ToArray());
        }

        // The screensaver mod running now (capri-screensaver, or another that draws one), or null.
        internal ICapriScreensaver Screensaver
        {
            get
            {
                foreach (KeyValuePair<string, ICapriMod> entry in running)
                {
                    ICapriScreensaver saver = entry.Value as ICapriScreensaver;
                    if (saver != null) return saver;
                }
                return null;
            }
        }

        // Every running intro mod by its name, for her test menu.
        internal List<KeyValuePair<string, ICapriMod>> IntroMods
        {
            get
            {
                List<KeyValuePair<string, ICapriMod>> found = new List<KeyValuePair<string, ICapriMod>>();
                foreach (KeyValuePair<string, ICapriMod> entry in running)
                {
                    if (!(entry.Value is ICapriIntro)) continue;
                    Mod listed = All.Find(delegate(Mod one) { return one.File.Id == entry.Key; });
                    found.Add(new KeyValuePair<string, ICapriMod>(listed != null && !string.IsNullOrEmpty(listed.File.Name) ? listed.File.Name : entry.Key, entry.Value));
                }
                return found;
            }
        }
#endif

        internal static ModFile ReadCode(string text)
        {
            ModFile read = new ModFile { WakeNames = new string[0] };
            bool marked = false;
            foreach (string raw in (text ?? "").TrimStart('﻿').Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                Match field = HeaderLine.Match(line);
                if (!field.Success) break;
                string value = field.Groups[2].Value;
                switch (field.Groups[1].Value)
                {
                    case "capri-mod": marked = true; int.TryParse(value, out read.Format); break;
                    case "id": read.Id = value; break;
                    case "name": read.Name = value; break;
                    case "version": read.Version = value; break;
                    case "author": read.Author = value; break;
                    case "description": read.Description = value; break;
                    case "intro": read.Intro = value == "yes"; break;
                    case "permissions": read.Permissions = ReadPermissions(value); break;
                    case "requires": ReadRequires(value, read); break;
                    case "assets": read.Assets = ReadAssets(value); break;
                }
            }
            if (!marked) throw new ModException("A code mod starts with a // capri-mod: 1 line.");
            CheckDetails(read);
            read.Reaches = Reaching(text);
            read.Colours = System.Text.RegularExpressions.Regex.IsMatch(text, @"\.Colour\s*\(");
            // Where it would reach: each site once on the short line, every whole address kept for the question, which
            // lists them under their site.
            string[] where = Addresses(Uncommented(text));
            read.Places = where;
            if (where.Length > 0)
            {
                List<string> sites = new List<string>();
                foreach (string one in where) { string site = Site(one); if (site.Length > 0 && !sites.Contains(site)) sites.Add(site); }
                List<string> saying = new List<string>(read.Reaches);
                // Short: a small box, and three sites is already the point made. The rest is a count.
                string said = string.Join(", ", sites.Count > 3 ? sites.GetRange(0, 3).ToArray() : sites.ToArray());
                if (sites.Count > 3) said += " and " + (sites.Count - 3) + " more";
                saying.Add(Contacts + said);
                read.Reaches = saying.ToArray();
            }
            // Unpacking hidden text and loading code at run time, together, is a packed assembly and nothing
            // else. The source somebody approves is then not the code that runs, which is the whole point of
            // reading it. Refused rather than disclosed: the other six are things a fair mod may do.
            if (Array.IndexOf(read.Reaches, Packs) >= 0 && Array.IndexOf(read.Reaches, Loads) >= 0)
                throw new ModException((read.Name ?? read.Id) + " unpacks hidden text and loads code while it runs. Together those mean the source you are reading is not what would run, so it cannot be approved here. A mod needing both has to say why and be put in by hand.");
            // Source nobody can read is said rather than refused. The approval is the boundary here as it is
            // everywhere else: being told the source cannot be read is what somebody needs to decide with,
            // and deciding to run it anyway is theirs to make.
            bool packed;
            string hidden = Unreadable(text, out packed);
            read.Unreadable = hidden;
            read.Source = hidden != null ? text : null;
            // Laid back out when it is only a layout problem, so it can be read before the second question.
            read.Tidy = hidden != null && !packed ? Tidied(text) : null;
            // A second question when it cannot be read and either nothing can open it up or it could run what
            // it carries. Packed data in a mod that never starts anything is a picture or a sound, usually.
            read.AskTwice = hidden != null && (!packed || Array.IndexOf(read.Reaches, "starts programs") >= 0
                || Array.IndexOf(read.Reaches, "calls Windows directly") >= 0 || Array.IndexOf(read.Reaches, "calls code by its name") >= 0);
            // Files it says it carries packed: their packing is said as that, not as hidden text, and asks nothing twice.
            // Decided after the refusal above, so naming assets never lets a mod that unpacks and loads code through.
            if (read.Assets.Length > 0)
            {
                List<string> saying = new List<string>(read.Reaches);
                saying.Remove(Packs);
                saying.Insert(0, "carries " + read.Assets.Length + " file" + (read.Assets.Length == 1 ? "" : "s") + " of its own packed into its code, named on its assets line: " + string.Join(", ", read.Assets));
                read.Reaches = saying.ToArray();
                if (packed) { hidden = null; read.Unreadable = null; read.AskTwice = false; }
            }
            if (hidden != null)
            {
                List<string> saying = new List<string>(read.Reaches);
                saying.Insert(0, "not readable source: " + hidden);
                read.Reaches = saying.ToArray();
            }
            if (read.NeedsApi > Api) throw new ModException((read.Name ?? read.Id) + " needs a newer Capri: it's written for mod API " + read.NeedsApi + ", and this Capri has " + Api + ".");
            return read;
        }

        // "// requires: api 3, focus-timer": the mod API it needs, and the mods that must be installed and on before it starts.
        private static void ReadRequires(string value, ModFile read)
        {
            List<string> mods = new List<string>();
            foreach (string part in Regex.Split(value ?? "", @"\s*,\s*"))
            {
                string wanted = part.Trim().ToLowerInvariant();
                if (wanted.Length == 0) continue;
                Match api = Regex.Match(wanted, @"^api\s*(\d{1,3})$");
                if (api.Success) { read.NeedsApi = Math.Max(read.NeedsApi, int.Parse(api.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)); continue; }
                if (!Regex.IsMatch(wanted, @"^[a-z0-9][a-z0-9-]{0,47}$")) throw new ModException("\"" + part.Trim() + "\" in // requires: isn't a mod id or an api number, like api 3.");
                if (!mods.Contains(wanted)) mods.Add(wanted);
            }
            read.Requires = mods.ToArray();
        }

        // The mods this one needs that aren't installed and on, by name, or null when it has them all.
        private string MissingRequired(string id)
        {
            Mod mod = All.Find(each => each.File.Id == id);
            if (mod == null || mod.File.Requires == null) return null;
            List<string> missing = new List<string>();
            foreach (string wanted in mod.File.Requires)
                if (!All.Exists(each => each.File.Id == wanted && each.On || each.File.Id == wanted && running.ContainsKey(wanted))) missing.Add(wanted);
            return missing.Count == 0 ? null : string.Join(", ", missing.ToArray());
        }

        internal static string HashOf(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        internal static string Words(byte[] bytes) { return new UTF8Encoding(false).GetString(bytes); }

        private Mod LoadCode(string file, HashSet<string> off)
        {
            byte[] bytes = File.ReadAllBytes(file);
            string text = Words(bytes);
            ModFile read = ReadCode(text);
            if (!string.Equals(read.Id, Path.GetFileNameWithoutExtension(file), StringComparison.OrdinalIgnoreCase)) throw new ModException("its file name doesn't match its id, " + read.Id + ".");
            string hash = HashOf(bytes), approved;
            bool same = Approved().TryGetValue(read.Id, out approved) && approved == hash;
            read.Checked = OnCheckedList(read.Id, bytes);
            // One dropped into the folder, not approved yet, is refused here if it can't run on this platform.
            if (!same) CheckPlatform(text);
            return new Mod { File = read, Path = file, Code = true, Hash = hash, Changed = !same, On = same && !off.Contains(read.Id) };
        }

        private Mod ImportCode(string file, Func<ModFile, string, bool> ask, HashSet<string> keepOff = null)
        {
            byte[] bytes;
            try
            {
                if (new FileInfo(file).Length > LargestCode) throw new ModException("That code mod is too big.");
                bytes = File.ReadAllBytes(file);
            }
            catch (IOException error) { throw new ModException("Couldn't read that file: " + error.Message); }
            catch (UnauthorizedAccessException error) { throw new ModException("Couldn't read that file: " + error.Message); }
            string text = Words(bytes), hash = HashOf(bytes);
            ModFile read = ReadCode(text);
            CheckPlatform(text);
            bool stay = keepOff != null && keepOff.Contains(read.Id);
            read.Checked = OnCheckedList(read.Id, bytes);
            RefuseUnchecked(read);
            // A mod on the checked list needs no question, the list being what was asked, unless it was approved here as another
            // file: then it asks, and the question says what changed.
            bool listed = read.Checked && !ApprovedAsOther(read.Id, hash);
            ApprovedWhatPath = Path.Combine(folder, ApprovedWhatFile);
#if !CROSS
            // Built before anyone is asked, so what the compiled code calls is in the question, and code that won't build
            // is refused first. Nothing of it runs until Make, after the yes.
            Type built = Build(text);
            Compiled(read, built);
            if (!listed && !ask(read, hash)) return null;
            ICapriMod made = Make(built);
#else
            if (!listed && !ask(read, hash)) return null;
            ICapriMod made = Compile(text);
#endif
            string home = Path.Combine(folder, read.Id + CodeExtension);
            try
            {
                Directory.CreateDirectory(folder);
                string writing = home + ".new";
                File.WriteAllBytes(writing, bytes);
                StopOne(read.Id);
                if (File.Exists(home)) File.Delete(home);
                File.Move(writing, home);
                Approve(read.Id, hash);
                RememberApproved(read);
                HashSet<string> off = Off();
                if (!stay && off.Remove(read.Id)) WriteOff(off);
            }
            catch (IOException error) { Unload(made); throw new ModException("Couldn't save that mod: " + error.Message); }
            catch (UnauthorizedAccessException error) { Unload(made); throw new ModException("Couldn't save that mod: " + error.Message); }
            Load();
            if (stay || StoppedHere(read.Id)) { if (!stay) Unload(made); return All.Find(mod => mod.File.Id == read.Id); }
            try { StartOne(read.Id, made); }
            catch (ModException error)
            {
                // Saved and approved, but a Start that failed left it not running, so it's off in the list until they turn it on.
                HashSet<string> stopped = Off();
                if (stopped.Add(read.Id)) WriteOff(stopped);
                Load();
                throw new ModException(error.Message.TrimEnd('.') + ". It's saved, and off until you turn it on again.");
            }
            return All.Find(mod => mod.File.Id == read.Id);
        }

        private bool SetCode(Mod mod, bool on, Func<ModFile, string, bool> ask)
        {
            HashSet<string> off = Off();
            if (!on)
            {
                StopOne(mod.File.Id);
                off.Add(mod.File.Id);
                WriteOff(off);
                mod.On = false;
                return true;
            }
            if (StoppedHere(mod.File.Id)) throw new ModException((mod.File.Name ?? mod.File.Id) + "'s security overrides stop it at this SECURITY level. Its cog can change that.");
            byte[] bytes = File.ReadAllBytes(mod.Path);
            string hash = HashOf(bytes), approved;
            mod.File.Checked = OnCheckedList(mod.File.Id, bytes);
            RefuseUnchecked(mod.File);
            // Approved here as this exact file, or never approved here and on the checked list. One approved as another file asks,
            // checked or not.
            bool same = Approved().TryGetValue(mod.File.Id, out approved) ? approved == hash : mod.File.Checked;
            CheckPlatform(Words(bytes));
            ApprovedWhatPath = Path.Combine(folder, ApprovedWhatFile);
#if !CROSS
            Type built = Build(Words(bytes));
            Compiled(mod.File, built);
            if (!same && !ask(mod.File, hash)) return false;
            ICapriMod made = Make(built);
#else
            if (!same && !ask(mod.File, hash)) return false;
            ICapriMod made = Compile(Words(bytes));
#endif
            StartOne(mod.File.Id, made);
            if (!same) { Approve(mod.File.Id, hash); RememberApproved(mod.File); }
            if (off.Remove(mod.File.Id)) WriteOff(off);
            ModGuard.Forget(mod.File.Id);
            mod.Hash = hash;
            mod.Changed = false;
            mod.On = true;
            return true;
        }

        private void RemoveCode(Mod mod)
        {
            StopOne(mod.File.Id);
            Dictionary<string, string> approved = Approved();
            if (approved.Remove(mod.File.Id)) WriteApproved(approved);
        }

        // Approved here, but as another file than this one.
        private bool ApprovedAsOther(string id, string hash)
        {
            string approved;
            return Approved().TryGetValue(id, out approved) && approved != hash;
        }

        private Dictionary<string, string> Approved() { return ApprovedAt(Path.Combine(folder, ApprovedFile)); }

        // What was approved, as signed by her own windows. Changed or unsigned outside her, none of it counts: every code mod and
        // plug-in is unapproved and asks again, the file starts empty, and she warns them.
        internal static Dictionary<string, string> ApprovedAt(string file)
        {
            Dictionary<string, string> approved = new Dictionary<string, string>();
            bool good;
            List<string> lines = PluginSettings.ReadSigned(file, out good);
            if (!good)
            {
                PluginSettings.WriteSigned(file, new List<string>());
                PluginSettings.Refused(file, "The list of mods and plug-ins you approved was changed outside Capri, so she isn't using it: each code mod and plug-in asks again before it runs.");
                return approved;
            }
            foreach (string line in lines)
            {
                string[] parts = line.Split(' ');
                if (parts.Length == 2) approved[parts[0]] = parts[1];
            }
            return approved;
        }

        internal static void WriteApprovedAt(string file, Dictionary<string, string> approved)
        {
            List<string> lines = new List<string>();
            foreach (KeyValuePair<string, string> entry in approved) lines.Add(entry.Key + " " + entry.Value);
            PluginSettings.WriteSigned(file, lines);
        }

        // For checks: an approval written as her own windows write one.
        internal void ApproveForChecks(string id, string hash) { Approve(id, hash); }

        // What each mod was when it was approved: its version, what her scan saw and what it asked for, kept beside approved.txt
        // so the question for a changed copy can say what changed. Its own file, since older builds read approved.txt as
        // exactly an id and a hash per line.
        internal const string ApprovedWhatFile = "approved-what.txt";
        internal static string ApprovedWhatPath;

        private void RememberApproved(ModFile read)
        {
            ApprovedWhatPath = Path.Combine(folder, ApprovedWhatFile);
            Dictionary<string, string> kept = WhatApproved();
            kept[read.Id] = (read.Version ?? "").Replace('\t', ' ') + "\t" + string.Join("|", read.Reaches ?? new string[0]).Replace('\t', ' ')
                + "\t" + string.Join(",", read.Permissions ?? new string[0]);
            List<string> lines = new List<string>();
            foreach (KeyValuePair<string, string> one in kept) lines.Add(one.Key + "\t" + one.Value);
            try { Directory.CreateDirectory(folder); File.WriteAllLines(ApprovedWhatPath, lines.ToArray()); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        internal static Dictionary<string, string> WhatApproved()
        {
            Dictionary<string, string> kept = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                if (ApprovedWhatPath != null && File.Exists(ApprovedWhatPath))
                    foreach (string line in File.ReadAllLines(ApprovedWhatPath))
                    {
                        int tab = line.IndexOf('\t');
                        if (tab > 0) kept[line.Substring(0, tab)] = line.Substring(tab + 1);
                    }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return kept;
        }

        // What changed in a mod since it was approved, one line each, or "" when it was never approved here.
        internal static string ChangedSince(ModFile now)
        {
            string was;
            if (now == null || now.Id == null || !WhatApproved().TryGetValue(now.Id, out was)) return "";
            string[] parts = was.Split('\t');
            string version = parts.Length > 0 ? parts[0] : "";
            List<string> reachedThen = new List<string>(parts.Length > 1 && parts[1].Length > 0 ? parts[1].Split('|') : new string[0]);
            List<string> askedThen = new List<string>(parts.Length > 2 && parts[2].Length > 0 ? parts[2].Split(',') : new string[0]);
            StringBuilder said = new StringBuilder();
            // Each change marked with the group it belongs to, as the rows behind the handle are.
            foreach (string asked in now.Permissions ?? new string[0]) if (!askedThen.Contains(asked)) said.Append("\n[[").Append(GroupOf(asked)).Append("]]new permission: ").Append(asked);
            foreach (string asked in askedThen) if (Array.IndexOf(now.Permissions ?? new string[0], asked) < 0) said.Append("\n[[").Append(GroupOf(asked)).Append("]]no longer asks: ").Append(asked);
            foreach (string reach in now.Reaches ?? new string[0]) if (!reachedThen.Contains(reach)) said.Append("\n[[").Append(GroupOf(reach.StartsWith(Contacts, StringComparison.Ordinal) ? "uses the network" : reach)).Append("]]new: ").Append(reach);
            foreach (string reach in reachedThen) if (Array.IndexOf(now.Reaches ?? new string[0], reach) < 0) said.Append("\n[[").Append(GroupOf(reach.StartsWith(Contacts, StringComparison.Ordinal) ? "uses the network" : reach)).Append("]]gone: ").Append(reach);
            return "Changed since you approved " + (version.Length == 0 ? "it" : "version " + version) + ":"
                + (said.Length == 0 ? "\n- nothing her scan shows; only the file itself" : said.ToString())
                + "\n- its fingerprint is different, as it is for any change\n\n";
        }

        private void Approve(string id, string hash)
        {
            Dictionary<string, string> approved = Approved();
            approved[id] = hash;
            WriteApproved(approved);
        }

        private void WriteApproved(Dictionary<string, string> approved)
        {
            Directory.CreateDirectory(folder);
            WriteApprovedAt(Path.Combine(folder, ApprovedFile), approved);
            // In her settings.json's mods block, so it is written with her other settings (on close, before an update).
            SettingsFile.Changed();
        }

#if !CROSS
        // The doors, both carried in her exe, each with its own sound and pictures. Door 2 is the front one: played once
        // on the first start that has it, whatever the intro setting says. The door is an option set in sys.md.
        internal const string DoorId = "door-intro", FrontId = "door2-intro";
        private const string CarriedPrefix = "Capri.Mod.", StockedFile = "stocked.txt";
        private static readonly Dictionary<string, string> ArtPrefixes = new Dictionary<string, string>
        {
            { DoorId, "Capri.DoorArt." }, { FrontId, "Capri.Door2Art." }
        };

        // Each door's pack number. A pack that gains files gets the next number, and an install that has an older one
        // is laid the new files beside what it has: nothing already there is replaced or taken away.
        private static readonly Dictionary<string, int> PackOf = new Dictionary<string, int>
        {
            { DoorId, 1 }, { FrontId, 3 }
        };

        // The mods she carries, laid in and approved, once per version: one they deleted stays deleted until a newer
        // one ships, and one they wrote is left alone. The build a mod is compiled against is named in a compile
        // failure.
        private static string Against()
        {
            try
            {
                string path = typeof(ICapriMod).Assembly.Location;
                return "Capri " + System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileVersion + " at " + path;
            }
            catch (Exception) { return "this Capri"; }
        }

        // Startup Intros is carried to be checked against, and laid in too, so every install has all its intros without
        // fetching it from the directory.
        private static bool LaidToo(string name) { return name == CheckedPrefix + "startup-intros" + CodeExtension; }

        // This build's own copy of a mod it carries, or null for a mod it does not carry.
        internal static byte[] CarriedBytes(string id)
        {
            try
            {
                Assembly own = typeof(Mods).Assembly;
                foreach (string name in own.GetManifestResourceNames())
                {
                    if (!name.StartsWith(CarriedPrefix, StringComparison.Ordinal)) continue;
                    byte[] bytes;
                    using (Stream from = own.GetManifestResourceStream(name))
                    using (MemoryStream copy = new MemoryStream()) { from.CopyTo(copy); bytes = copy.ToArray(); }
                    if (ReadCode(Words(bytes)).Id == id) return bytes;
                }
            }
            catch (Exception caughtOne) { Brain.Caught(caughtOne); }
            return null;
        }

        internal static void Stock(string data)
        {
            string folder = Path.Combine(data, "mods");
            try
            {
                Assembly own = typeof(Mods).Assembly;
                foreach (string name in own.GetManifestResourceNames())
                {
                    if (!name.StartsWith(CarriedPrefix, StringComparison.Ordinal) && !LaidToo(name)) continue;
                    byte[] bytes;
                    using (Stream from = own.GetManifestResourceStream(name))
                    using (MemoryStream copy = new MemoryStream())
                    {
                        from.CopyTo(copy);
                        bytes = copy.ToArray();
                    }
                    ModFile carried = ReadCode(Words(bytes));
                    Dictionary<string, string> stocked = Pairs(Path.Combine(folder, StockedFile));
                    // A door's sound and pop-up pictures in its own folder, laid once and never over an existing file.
                    // Copies an earlier build laid in her shared folders move to the first door.
                    string artPrefix, packWas;
                    int pack, packHad;
                    if (!PackOf.TryGetValue(carried.Id, out pack)) pack = 1;
                    if (!stocked.TryGetValue(carried.Id + "+pack", out packWas) || !int.TryParse(packWas, out packHad)) packHad = 0;
                    if (ArtPrefixes.TryGetValue(carried.Id, out artPrefix) && packHad < pack)
                    {
                        LayDoorArt(own, data, carried.Id, artPrefix);
                        stocked[carried.Id + "+pack"] = pack.ToString();
                        Directory.CreateDirectory(folder);
                        WritePairs(Path.Combine(folder, StockedFile), stocked);
                    }
                    string was;
                    string home = Path.Combine(folder, carried.Id + CodeExtension);
                    bool lay = true;
                    if (File.Exists(home))
                    {
                        try { lay = Newer(carried.Version, ReadCode(Words(File.ReadAllBytes(home))).Version); }
                        catch (ModException) { lay = true; }
                        // Same version, but neither this build's file nor on the checked list: an earlier build's copy
                        // of a mod changed under the same number. Hers goes in, and the old one is kept in the mod's
                        // data folder.
                        if (!lay)
                        {
                            byte[] onDisk = File.ReadAllBytes(home);
                            if (CheckedHash(onDisk) != CheckedHash(bytes) && !OnCheckedList(carried.Id, onDisk))
                            {
                                string kept = Path.Combine(folder, carried.Id + "-data");
                                Directory.CreateDirectory(kept);
                                File.WriteAllBytes(Path.Combine(kept, "replaced-" + carried.Version + ".txt"), onDisk);
                                Brain.Log("mods", -1, carried.Id + " " + carried.Version + " on disk was not this build's and not on the checked list; kept as " + carried.Id + "-data\\replaced-" + carried.Version + ".txt.");
                                lay = true;
                            }
                        }
                    }
                    // Already stocked at this version is skipped, unless the copy on disk is older: an older build that
                    // could not compile this one laid its own back, and this build takes its own copy back again.
                    if (stocked.TryGetValue(carried.Id, out was) && !Newer(carried.Version, was) && !(lay && File.Exists(home))) continue;
                    Directory.CreateDirectory(folder);
                    if (lay)
                    {
                        File.WriteAllBytes(home, bytes);
                        Dictionary<string, string> approved = ApprovedAt(Path.Combine(folder, ApprovedFile));
                        approved[carried.Id] = HashOf(bytes);
                        WriteApprovedAt(Path.Combine(folder, ApprovedFile), approved);
                        Brain.Log("mods", -1, "Laid in " + carried.Id + " " + carried.Version + ", approved.");
                    }
                    stocked[carried.Id] = carried.Version;
                    WritePairs(Path.Combine(folder, StockedFile), stocked);
                }
            }
            catch (Exception caughtOne) { Brain.Caught(caughtOne); }
        }

        private static void LayDoorArt(Assembly own, string data, string id, string prefix)
        {
            foreach (string name in own.GetManifestResourceNames())
            {
                if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
                string file = name.Substring(prefix.Length);
                bool sound = file.EndsWith(".wav", StringComparison.OrdinalIgnoreCase);
                string place = Path.Combine(DoorFolder(data, id), sound ? "sounds" : "art");
                string to = Path.Combine(place, file);
                string was = sound ? Path.Combine(data, "sounds", "intro", file) : Path.Combine(data, "assets", "graphics", file);
                try
                {
                    Directory.CreateDirectory(place);
                    if (id == DoorId && File.Exists(was))
                    {
                        if (!File.Exists(to)) File.Move(was, to);
                        else File.Delete(was);
                        Brain.Log("mods", -1, "Moved the door's " + file + " into its own folder.");
                        TidyEmpty(Path.GetDirectoryName(was), data);
                        continue;
                    }
                    if (File.Exists(to)) continue;
                    using (Stream from = own.GetManifestResourceStream(name))
                    using (FileStream into = File.Create(to)) from.CopyTo(into);
                    Brain.Log("mods", -1, "Laid in the door's " + file + ".");
                }
                catch (IOException caughtOne) { Brain.Caught(caughtOne); }
                catch (UnauthorizedAccessException caughtOne) { Brain.Caught(caughtOne); }
            }
        }

        // A door's own folder, the same one its code is handed as its data folder.
        internal static string DoorFolder(string data, string id) { return Path.Combine(data, "mods", id + "-data"); }

        // A folder the door's files left, taken away once it is empty, and its parent too, never above her data folder.
        private static void TidyEmpty(string folder, string data)
        {
            try
            {
                for (int up = 0; up < 2 && !string.IsNullOrEmpty(folder); up++)
                {
                    string full = Path.GetFullPath(folder).TrimEnd('\\');
                    if (string.Equals(full, Path.GetFullPath(data).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return;
                    if (!Directory.Exists(full) || Directory.GetFileSystemEntries(full).Length > 0) return;
                    Directory.Delete(full);
                    folder = Path.GetDirectoryName(full);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static Dictionary<string, string> Pairs(string file)
        {
            Dictionary<string, string> pairs = new Dictionary<string, string>();
            if (File.Exists(file))
                foreach (string line in File.ReadAllLines(file))
                {
                    string[] parts = line.Split(' ');
                    if (parts.Length == 2) pairs[parts[0]] = parts[1];
                }
            return pairs;
        }

        private static void WritePairs(string file, Dictionary<string, string> pairs)
        {
            List<string> lines = new List<string>();
            foreach (KeyValuePair<string, string> entry in pairs) lines.Add(entry.Key + " " + entry.Value);
            File.WriteAllLines(file, lines);
        }

        private static bool Newer(string version, string than)
        {
            System.Version a, b;
            if (!System.Version.TryParse(version ?? "", out a)) return false;
            if (!System.Version.TryParse(than ?? "", out b)) return true;
            return a > b;
        }

        // Door 2 is on and has never played here.
        internal bool DoorFirst
        {
            get
            {
                if (!All.Exists(mod => mod.Code && mod.On && mod.File.Id == FrontId)) return false;
                try { return !File.Exists(Path.Combine(folder, FrontId + "-data", "shown")); }
                catch (Exception caughtOne) { Brain.Caught(caughtOne); return false; }
            }
        }

        // Door 2 when this start is its first, else the usual pick.
        internal ICapriIntro StartIntro { get { return DoorFirst ? IntroOf(FrontId) : Intro; } }
#endif

#if !CROSS
        private static ICapriMod Compile(string text) { return Make(Build(text)); }

        // Its one ICapriMod made, which runs the mod's own code for the first time: only after it is approved.
        private static ICapriMod Make(Type mod)
        {
            try { return (ICapriMod)Activator.CreateInstance(mod); }
            catch (Exception error) { throw new ModException("That mod failed while loading: " + Inner(error).Message); }
        }

        // What the compiled code really calls, whatever the source called it: each call in each method is read off its IL
        // and named the way her list names it. Nothing of the mod runs to read it. Only adds to what the list already says.
        private static readonly string[,] Calls =
        {
            { "System.Diagnostics.Process::Start", "starts programs" },
            { "System.Reflection.Assembly::Load", Loads },
            { "System.AppDomain::Load", Loads },
            { "System.AppDomain::ExecuteAssembly", Loads },
            { "System.Reflection.Emit.", Loads },
            { "System.Convert::FromBase64", Packs },
            { "System.IO.Compression.", Packs },
            { "System.Security.Cryptography.ProtectedData::Unprotect", "unlocks saved secrets" },
            { "System.Runtime.InteropServices.Marshal::", "calls Windows directly" },
            { "System.Reflection.MethodBase::Invoke", "calls code by its name" },
            { "System.Type::GetMethod", "calls code by its name" },
            { "System.Type::InvokeMember", "calls code by its name" },
            { "System.Type::GetType", "calls code by its name" },
            { "System.Activator::CreateInstance", "calls code by its name" },
            { "System.Net.", "uses the network" },
            { "System.IO.File::Write", WritesFiles },
            { "System.IO.File::Delete", WritesFiles },
            { "System.IO.File::Move", WritesFiles },
            { "System.IO.File::Copy", WritesFiles },
            { "System.IO.File::Create", WritesFiles },
            { "System.IO.File::Append", WritesFiles },
            { "System.IO.File::Replace", WritesFiles },
            { "System.IO.Directory::Delete", WritesFiles },
            { "System.IO.Directory::Move", WritesFiles },
            { "System.IO.FileInfo::Delete", WritesFiles },
            { "System.IO.FileStream::.ctor", WritesFiles },
            { "System.IO.StreamWriter::.ctor", WritesFiles },
            { "Microsoft.Win32.Registry", "reaches outside her folder" },
        };

        internal static string[] CompiledCalls(Type built)
        {
            List<string> found = new List<string>();
            Module module = built.Module;
            foreach (Type type in built.Assembly.GetTypes())
                foreach (MethodBase method in Members(type))
                {
                    byte[] il;
                    try { MethodBody body = method.GetMethodBody(); if (body == null) continue; il = body.GetILAsByteArray(); }
                    catch (Exception) { continue; }
                    for (int at = 0; at + 4 < il.Length; at++)
                    {
                        // call, callvirt and newobj, each followed by a method token: a MethodDef, MemberRef or MethodSpec.
                        if (il[at] != 0x28 && il[at] != 0x6F && il[at] != 0x73) continue;
                        int token = BitConverter.ToInt32(il, at + 1), table = (token >> 24) & 0xFF;
                        if (table != 0x06 && table != 0x0A && table != 0x2B) continue;
                        MethodBase called;
                        try { called = module.ResolveMethod(token, type.IsGenericType ? type.GetGenericArguments() : null, method.IsGenericMethod ? method.GetGenericArguments() : null); }
                        catch (Exception) { continue; }
                        if (called == null) continue;
                        string name = called.DeclaringType == null ? called.Name : called.DeclaringType.FullName + "::" + called.Name;
                        string label = (called.Attributes & MethodAttributes.PinvokeImpl) != 0 ? "calls Windows directly" : null;
                        for (int each = 0; label == null && each < Calls.GetLength(0); each++)
                            if (name.StartsWith(Calls[each, 0], StringComparison.Ordinal)) label = Calls[each, 1];
                        if (label != null && !found.Contains(label)) found.Add(label);
                        at += 4;
                    }
                }
            return found.ToArray();
        }

        private static IEnumerable<MethodBase> Members(Type type)
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (MethodInfo method in type.GetMethods(all)) yield return method;
            foreach (ConstructorInfo made in type.GetConstructors(all)) yield return made;
        }

        // What the compiled code calls, added to what the source said, before anyone is asked.
        private static void Compiled(ModFile read, Type built)
        {
            List<string> said = new List<string>(read.Reaches ?? new string[0]);
            foreach (string call in CompiledCalls(built))
                if (!said.Contains(call) && !(call == "uses the network" && said.Exists(each => each.StartsWith(Contacts, StringComparison.Ordinal)))) said.Add(call);
            read.Reaches = said.ToArray();
        }

        private static Type Build(string text)
        {
            CompilerResults built;
            List<Type> mods = new List<Type>();
            // A missing or blocked compiler, or a mod that won't load, fails that mod: at startup this runs on a worker thread,
            // where any other error would end her.
            try
            {
                using (CSharpCodeProvider compiler = new CSharpCodeProvider())
                {
                    CompilerParameters options = new CompilerParameters { GenerateInMemory = true, GenerateExecutable = false, CompilerOptions = "/optimize+" };
                    options.ReferencedAssemblies.AddRange(new[] { "System.dll", "System.Core.dll", "System.Drawing.dll", "System.Windows.Forms.dll", typeof(ICapriMod).Assembly.Location });
                    built = compiler.CompileAssemblyFromSource(options, text);
                }
                foreach (CompilerError error in built.Errors)
                    if (!error.IsWarning) throw new ModException("That code didn't compile, line " + error.Line + ": " + error.ErrorText + " (against " + Against() + ")");
                foreach (Type type in built.CompiledAssembly.GetTypes())
                    if (typeof(ICapriMod).IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface) mods.Add(type);
            }
            catch (System.Runtime.InteropServices.ExternalException error) { throw new ModException("Couldn't compile that mod: " + error.Message); }
            catch (InvalidOperationException error) { throw new ModException("Couldn't compile that mod: " + error.Message); }
            catch (ArgumentException error) { throw new ModException("Couldn't compile that mod: " + error.Message); }
            catch (ReflectionTypeLoadException error) { throw new ModException("Couldn't load that mod: " + error.Message); }
            catch (TypeLoadException error) { throw new ModException("Couldn't load that mod: " + error.Message); }
            catch (BadImageFormatException error) { throw new ModException("Couldn't load that mod: " + error.Message); }
            catch (System.Security.SecurityException error) { throw new ModException("Couldn't load that mod: " + error.Message); }
            // The compiler writes temp files, so a blocked temp folder or an antivirus lock fails here too.
            catch (IOException error) { throw new ModException("Couldn't compile that mod: " + error.Message); }
            catch (UnauthorizedAccessException error) { throw new ModException("Couldn't compile that mod: " + error.Message); }
            if (mods.Count != 1) throw new ModException("A code mod needs exactly one class that implements ICapriMod. That one has " + mods.Count + ".");
            return mods[0];
        }
#endif

        private static Exception Inner(Exception error) { return error is TargetInvocationException && error.InnerException != null ? error.InnerException : error; }

        private void StartOne(string id, ICapriMod made)
        {
            StopOne(id);
            string missing = MissingRequired(id);
            if (missing != null) { Unload(made); throw new ModException("That mod needs " + missing + " installed and on first."); }
            ICapriHost host = HostFor(id);
            ModGuard.Loaded(made.GetType().Assembly, id);
            try { made.Start(host); }
            catch (Exception error)
            {
                Close(host);
                Unload(made);
                Brain.Log("mod " + id, -1, Inner(error).ToString());
                throw new ModException("That mod failed while starting: " + Inner(error).Message);
            }
            running[id] = made;
            hosts[id] = host;
            Brain.Log("mods", -1, "Started " + id + " (" + made.GetType().Name + (made is ICapriIntro ? ", an intro" : "") + ").");
        }

        private void StopOne(string id)
        {
            ICapriMod mod;
            if (!running.TryGetValue(id, out mod)) return;
            running.Remove(id);
            try { mod.Stop(); }
            catch (Exception error) { Brain.Log("mod " + id, -1, Inner(error).ToString()); }
            ICapriHost host;
            if (hosts.TryGetValue(id, out host)) { hosts.Remove(id); Close(host); }
            Unload(mod);
        }

        // A stopped mod's hooks, timers, menu items and notes go with its host.
        private static void Close(ICapriHost host)
        {
            IDisposable done = host as IDisposable;
            if (done == null) return;
            try { done.Dispose(); }
            catch (Exception error) { Brain.Log("mods", -1, error.ToString()); }
        }

        internal void StopCode()
        {
            foreach (string id in new List<string>(running.Keys)) StopOne(id);
        }

        internal void StartCode(Action<Action> onWindow)
        {
            ModGuard.Folder(folder);
            List<Mod> wanted = ModGuard.NoneThisRun ? new List<Mod>() : All.FindAll(mod => mod.Code && mod.On && !StoppedHere(mod.File.Id));
            CodeSettled = wanted.Count == 0;
            if (wanted.Count == 0) return;
            StartSome(wanted, onWindow);
        }

        // Still in her list as the same approved file, on, and not stopped at this SECURITY level.
        private bool StillWanted(Mod starting)
        {
            Mod now = All.Find(one => one.File.Id == starting.File.Id);
            return now != null && now.Code && now.On && now.Hash == starting.Hash && !StoppedHere(starting.File.Id);
        }

        // For checks: a mod removed while it compiled to start, its start then let through. True if it ran anyway.
        internal bool RunsAfterRemove(Mod mod)
        {
            StopOne(mod.File.Id);
            List<Action> later = new List<Action>();
            StartSome(new List<Mod> { mod }, delegate(Action work) { lock (later) later.Add(work); });
            for (int waited = 0; waited < 600; waited++) { lock (later) if (later.Count >= 2) break; Thread.Sleep(50); }
            Remove(mod);
            Action[] run;
            lock (later) run = later.ToArray();
            foreach (Action one in run) one();
            return running.ContainsKey(mod.File.Id);
        }

        private void StartSome(List<Mod> wanted, Action<Action> onWindow)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                foreach (Mod mod in wanted)
                {
                    Mod starting = mod;
                    ModGuard.Starting(starting.File.Id);
                    try
                    {
                        byte[] bytes = File.ReadAllBytes(starting.Path);
                        if (HashOf(bytes) != starting.Hash) throw new ModException("it changed after it was approved.");
                        ICapriMod made;
                        try { made = Compile(Words(bytes)); }
                        catch (ModException failed)
                        {
                            // A mod she carries that will not compile here was laid by a newer build: hers goes back in
                            // its place, approved as her own carried mods are, and is tried once more.
                            byte[] own = CarriedBytes(starting.File.Id);
                            if (own == null || HashOf(own) == HashOf(bytes)) throw;
                            File.WriteAllBytes(starting.Path, own);
                            string approvedPath = Path.Combine(Path.GetDirectoryName(starting.Path), ApprovedFile);
                            Dictionary<string, string> approved = ApprovedAt(approvedPath);
                            approved[starting.File.Id] = HashOf(own);
                            WriteApprovedAt(approvedPath, approved);
                            starting.Hash = HashOf(own);
                            made = Compile(Words(own));
                            Brain.Log("mods", -1, starting.File.Id + " could not compile here (" + failed.Message + "), so this build's own copy was laid back in and started.");
                        }
                        onWindow(delegate
                        {
                            // Removed, switched off or changed while it compiled: it doesn't start.
                            if (!StillWanted(starting))
                            {
                                Unload(made);
                                Brain.Log("mods", -1, starting.File.Id + " was removed or switched off while it was starting, so it wasn't started.");
                            }
                            else
                            {
                                try { StartOne(starting.File.Id, made); }
                                catch (ModException error) { StartFailed(starting, error); }
                            }
                            ModGuard.Started(starting.File.Id);
                        });
                    }
                    catch (Exception error)
                    {
                        if (!(error is ModException || error is IOException || error is UnauthorizedAccessException)) throw;
                        Exception failed = error;
                        onWindow(delegate { StartFailed(starting, failed); ModGuard.Started(starting.File.Id); });
                    }
                }
                onWindow(delegate { CodeSettled = true; });
            });
        }

        // At her start, before any code mod runs: the ones the guard names are switched off and kept off, with the reason
        // on her Mods page. Returns her line about it, or null. With --no-mods every mod is off for this run, nothing saved.
        internal string GuardAtStart()
        {
            ModGuard.Folder(folder);
            if (LoadUntested) ModGuard.ForgetUnchecked();
            if (ModGuard.NoneThisRun)
            {
                foreach (Mod mod in All) mod.On = false;
                return "Every mod is off for this run, as asked. They come back as they were next time.";
            }
            List<string> named = new List<string>(), untested = UncheckedOffNow();
            List<string> on = new List<string>();
            foreach (Mod mod in All) if (mod.Code && mod.On) on.Add(mod.File.Id);
            List<KeyValuePair<string, string>> off = ModGuard.AtStart(on);
            foreach (KeyValuePair<string, string> one in off)
            {
                Mod mod = All.Find(each => each.File.Id == one.Key);
                if (mod == null || !mod.On) continue;
                SwitchOff(mod, one.Value);
                named.Add((mod.File.Name ?? mod.File.Id) + " (" + one.Value + ")");
            }
            string said = untested.Count == 0 ? "" : "I switched off " + string.Join(", ", untested.ToArray()) + ": " + (untested.Count == 1 ? "it isn't" : "they aren't")
                + " on Capri's checked list. Turn on Load untested mods in Mods to use " + (untested.Count == 1 ? "it" : "them") + ".";
            if (named.Count > 0) said += (said.Length == 0 ? "" : " ") + "I switched off " + string.Join(", ", named.ToArray()) + ". Turn " + (named.Count == 1 ? "it" : "them") + " back on in Mods if you trust " + (named.Count == 1 ? "it" : "them") + ".";
            return said.Length == 0 ? null : said;
        }

        // Code mods off the checked list stay off while untested mods are: switched off with the reason on their row,
        // nothing of them deleted. At her start, and when the switch in Mods is turned off. Their names.
        internal List<string> UncheckedOffNow()
        {
            List<string> untested = new List<string>();
            if (LoadUntested) return untested;
            foreach (Mod mod in All)
            {
                if (!mod.Code || !mod.On) continue;
                byte[] file;
                try { file = File.ReadAllBytes(mod.Path); }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                if (OnCheckedList(mod.File.Id, file)) continue;
                SwitchOff(mod, UncheckedOff);
                untested.Add(mod.File.Name ?? mod.File.Id);
            }
            return untested;
        }

        // Off and kept off, with why, whatever it was doing. Her own carried mods too: one that breaks her is no different.
        internal void SwitchOff(Mod mod, string reason)
        {
            if (mod.Code) StopOne(mod.File.Id);
            HashSet<string> off = Off();
            off.Add(mod.File.Id);
            WriteOff(off);
            mod.On = false;
            ModGuard.Why(mod.File.Id, reason);
            Brain.Log("mods", -1, (mod.File.Name ?? mod.File.Id) + " was switched off: " + reason + ".");
        }

        private void StartFailed(Mod mod, Exception error)
        {
            mod.On = false;
            string problem = Path.GetFileName(mod.Path) + ": " + error.Message;
            Problems.Add(problem);
            Brain.Log("mods", -1, problem);
        }

        internal bool Heard(string words)
        {
            bool taken = false;
            foreach (KeyValuePair<string, ICapriMod> entry in new List<KeyValuePair<string, ICapriMod>>(running))
            {
                try { if (entry.Value.Heard(words)) taken = true; }
                catch (Exception error) { Brain.Log("mod " + entry.Key, -1, Inner(error).ToString()); }
            }
            return taken;
        }
    }

    // Which mod broke her: a mod starting is written down first; a crash or freeze names the mod on the stack, and
    // throwing hooks are counted. Next start those are switched off; with no mod named after repeated crashes, all code
    // mods are.
    internal static class ModGuard
    {
        internal const string GuardFile = "guard.txt", WhyFile = "switched-off.txt";
        internal const int MostThrows = 5, Unexplained = 2;
        internal const double ThrowSpell = 60;
        private static string folder;
        private static readonly Dictionary<Assembly, string> assemblies = new Dictionary<Assembly, string>();
        private static readonly Dictionary<string, Queue<DateTime>> throws = new Dictionary<string, Queue<DateTime>>(StringComparer.Ordinal);
        private static readonly object gate = new object();
        private static volatile string stuckIn;
        // "--no-mods": every mod off for this one run, nothing saved.
        internal static volatile bool NoneThisRun;
        // Her last run ended without her quitting.
        internal static bool ClosedBadly;

        internal static void Args(string[] args) { NoneThisRun = Array.IndexOf(args ?? new string[0], "--no-mods") >= 0; }

        internal static void Folder(string mods) { folder = mods; }

        internal static void Loaded(Assembly code, string id)
        {
            if (code == null || id == null) return;
            lock (gate) assemblies[code] = id;
        }

        // The mod whose compiled code is in this stack, nearest first, or null.
        internal static string In(System.Diagnostics.StackTrace trace)
        {
            if (trace == null) return null;
            lock (gate)
            {
                if (assemblies.Count == 0) return null;
                for (int i = 0; i < trace.FrameCount; i++)
                {
                    MethodBase method = trace.GetFrame(i).GetMethod();
                    string id;
                    if (method != null && method.DeclaringType != null && assemblies.TryGetValue(method.DeclaringType.Assembly, out id)) return id;
                }
            }
            return null;
        }

        internal static string In(Exception error)
        {
            for (Exception at = error; at != null; at = at.InnerException)
            {
                string id = In(new System.Diagnostics.StackTrace(at, false));
                if (id != null) return id;
            }
            return null;
        }

        // Called from her crash handler: the mod on the crash's stack, written down for her next start.
        internal static void CrashedIn(Exception error, string what)
        {
            try { string id = In(error); if (id != null) Blame(id, "it crashed her: " + what); }
            catch (Exception) { }
        }

        // Called while her window is stuck: the mod on the stuck stack, kept until she is ended for it.
        internal static void Saw(System.Diagnostics.StackTrace trace) { try { stuckIn = In(trace); } catch (Exception) { } }

        // Called when her watchdog ends or replaces her for a freeze.
        internal static void BlameStuck() { string id = stuckIn; if (id != null) Blame(id, "it froze her"); }

        internal static void Starting(string id) { Line("starting\t" + id); }

        internal static void Started(string id) { Drop("starting\t" + id); }

        internal static void Blame(string id, string reason)
        {
            Line("blame\t" + id + "\t" + (reason ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '));
        }

        // A hook threw. True once a mod has thrown MostThrows times inside ThrowSpell seconds; its count starts again.
        internal static bool Threw(string id)
        {
            DateTime now = DateTime.UtcNow;
            lock (gate)
            {
                Queue<DateTime> times;
                if (!throws.TryGetValue(id, out times)) throws[id] = times = new Queue<DateTime>();
                times.Enqueue(now);
                while (times.Count > 0 && (now - times.Peek()).TotalSeconds > ThrowSpell) times.Dequeue();
                if (times.Count < MostThrows) return false;
                times.Clear();
                return true;
            }
        }

        // At her start: which code mods to switch off, and why. Clears what it read, and keeps count of closes nobody
        // could be named for.
        internal static List<KeyValuePair<string, string>> AtStart(ICollection<string> codeOn)
        {
            List<KeyValuePair<string, string>> off = new List<KeyValuePair<string, string>>();
            if (folder == null) return off;
            string path = Path.Combine(folder, GuardFile);
            int unexplained = 0;
            try
            {
                if (File.Exists(path))
                    foreach (string line in File.ReadAllLines(path))
                    {
                        string[] parts = line.Split('\t');
                        if (parts.Length >= 2 && parts[0] == "starting" && ClosedBadly) Add(off, parts[1], "she stopped while it was starting");
                        else if (parts.Length >= 3 && parts[0] == "blame") Add(off, parts[1], parts[2]);
                        else if (parts.Length >= 2 && parts[0] == "unexplained") int.TryParse(parts[1], out unexplained);
                    }
                bool any = false;
                foreach (string id in codeOn) { any = true; break; }
                if (ClosedBadly && off.Count == 0 && any)
                {
                    unexplained++;
                    if (unexplained >= Unexplained)
                    {
                        foreach (string id in codeOn) Add(off, id, "she closed unexpectedly " + unexplained + " times in a row with mods on, and no one mod could be named");
                        unexplained = 0;
                    }
                }
                else if (!ClosedBadly) unexplained = 0;
                Directory.CreateDirectory(folder);
                File.WriteAllText(path, unexplained > 0 ? "unexplained\t" + unexplained + Environment.NewLine : "");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return off;
        }

        private static void Add(List<KeyValuePair<string, string>> off, string id, string reason)
        {
            if (string.IsNullOrEmpty(id) || off.Exists(each => each.Key == id)) return;
            off.Add(new KeyValuePair<string, string>(id, reason));
        }

        // Why each mod was switched off, for her Mods page; a mod turned back on is taken off the list.
        internal static void Why(string id, string reason)
        {
            Dictionary<string, string> why = Reasons();
            why[id] = reason;
            WriteReasons(why);
        }

        internal static void Forget(string id)
        {
            Dictionary<string, string> why = Reasons();
            if (why.Remove(id)) WriteReasons(why);
        }

        // Load untested mods turned on: the mods switched off for it lose that reason. They stay off until turned on in their
        // rows, each asking first.
        internal static void ForgetUnchecked()
        {
            Dictionary<string, string> why = Reasons();
            List<string> gone = new List<string>();
            foreach (KeyValuePair<string, string> one in why) if (one.Value == Mods.UncheckedOff) gone.Add(one.Key);
            foreach (string id in gone) why.Remove(id);
            if (gone.Count > 0) WriteReasons(why);
        }

        internal static Dictionary<string, string> Reasons()
        {
            Dictionary<string, string> why = new Dictionary<string, string>(StringComparer.Ordinal);
            if (folder == null) return why;
            try
            {
                string path = Path.Combine(folder, WhyFile);
                if (File.Exists(path))
                    foreach (string line in File.ReadAllLines(path))
                    {
                        int tab = line.IndexOf('\t');
                        if (tab > 0) why[line.Substring(0, tab)] = line.Substring(tab + 1);
                    }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return why;
        }

        private static void WriteReasons(Dictionary<string, string> why)
        {
            if (folder == null) return;
            List<string> lines = new List<string>();
            foreach (KeyValuePair<string, string> one in why) lines.Add(one.Key + "\t" + one.Value);
            try { Directory.CreateDirectory(folder); File.WriteAllLines(Path.Combine(folder, WhyFile), lines.ToArray()); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void Line(string line)
        {
            if (folder == null) return;
            try { lock (gate) { Directory.CreateDirectory(folder); File.AppendAllText(Path.Combine(folder, GuardFile), line + Environment.NewLine); } }
            catch (Exception) { }
        }

        private static void Drop(string line)
        {
            if (folder == null) return;
            try
            {
                lock (gate)
                {
                    string path = Path.Combine(folder, GuardFile);
                    if (!File.Exists(path)) return;
                    List<string> kept = new List<string>(File.ReadAllLines(path));
                    if (kept.Remove(line)) File.WriteAllLines(path, kept.ToArray());
                }
            }
            catch (Exception) { }
        }
    }

#if !CROSS
    internal sealed partial class Companion
    {
        private const double LongestIntro = 25, IntroWait = 8;
        private IntroScreen introScreen;

        // An intro is due on this start: she is held for it, unseen, rather than drawn first.
        private bool IntroDue { get { return !testing && !instant && preferences.Motion && !(preferences.NoIntro && !ModList.DoorFirst) && !hidden && ModList.WantsIntro; } }

        private bool HoldForIntro(Action arrival, bool spawn)
        {
            if (!IntroDue || FullScreenDisplay) return false;
            if (spawn) SpawnInMiddle();
            introSpawn = spawn;
            // Door 2's first showing waits for her mods to start rather than a clock, so a slow network at start can't
            // make it give up. DoorCap only stops mods that never settle from holding her for good.
            WaitForIntro(arrival, ModList.DoorFirst ? DoorCap : IntroWait);
            return true;
        }

        private const double DoorCap = 120;
        // She was put in the middle for this start's intro, so she is put there again as it starts.
        private bool introSpawn;

        // An intro is showing, her arrival is held for one, or her start hasn't yet decided whether one plays, so
        // nothing she says shows over it. Gives way after a minute and a half, and never holds her checks.
        internal bool IntroShowing(double now) { return introScreen != null || now < veilUntil || (!introDecided && !testing && now < IntroDecidedBy); }

        private bool introDecided;
        private const double IntroDecidedBy = 90;

        // An intro's own hold on her, apart from the veil her emotes use: a chair or a hand-off veils her too, and her
        // answers come through those.
        private bool introHeld;

        // What holds her bubble, the words over her head and her typing box: an intro on screen or held for, or her start
        // not yet knowing whether one plays. An emote's veil does not.
        internal bool IntroPlaying(double now) { return introScreen != null || (introHeld && now < veilUntil) || (!introDecided && !testing && now < IntroDecidedBy); }

        // An intro that ends as it starts and draws nothing, for checking where one is played.
        private sealed class StubIntro : ICapriIntro
        {
            public double Seconds { get { return 0; } }
            public void Begin(Bitmap desktop, Rectangle screen, Rectangle her) { }
            public void Draw(Graphics graphics, Rectangle screen, Rectangle her, double at) { }
        }

        // A test run keeps her and any intro on its own screen, whatever screen was saved, and a save made before she
        // is placed writes no place.
        private void SmokeIntroScreen(List<string> checks)
        {
            Rectangle home = Displays.Home.Bounds, primary = Screen.PrimaryScreen.Bounds;
            Point anchorWas = anchor, locationWas = Location;
            bool hadPosition = preferences.HasPosition;
            int xWas = preferences.X, yWas = preferences.Y;
            try
            {
                Point elsewhere = home == primary ? new Point(home.X + 40, home.Y + 40) : new Point(primary.X + 40, primary.Y + 40);
                preferences.HasPosition = true;
                preferences.X = elsewhere.X;
                preferences.Y = elsewhere.Y;
                Point start = StartSpot();
                if (!home.Contains(new Point(start.X + Width / 2, start.Y + Height / 2))) throw new Exception("A test run would start her off its own screen: " + start + " for " + home);
                // Only her anchor is put elsewhere, never her window, so nothing of hers shows on their screen.
                anchor = elsewhere;
                IntroWasOn = Rectangle.Empty;
                IntroLikeStart(new StubIntro());
                if (IntroWasOn != home) throw new Exception("A test run's intro was given " + IntroWasOn + ", not its own screen " + home);
                placed = false;
                preferences.HasPosition = false;
                SaveNow();
                bool stamped = preferences.HasPosition;
                placed = true;
                if (stamped) throw new Exception("A save before she was placed wrote a place for her.");
                SmokeRun.EscCheck(checks);
                checks.Add("PASS a test run keeps her and any intro on its own screen: a place saved on another screen starts her on hers, an intro is given hers even with her last placed elsewhere, and a save before she is placed writes no place");
            }
            finally
            {
                placed = true;
                preferences.HasPosition = hadPosition;
                preferences.X = xWas;
                preferences.Y = yWas;
                anchor = anchorWas;
                Location = locationWas;
                SavePreferences();
            }
        }

        // While an intro plays nothing of hers shows: a remark, words over her head and her typing box all wait, and come
        // once it is over.
        private void SmokeIntroGate(List<string> checks)
        {
            double veilWas = veilUntil;
            bool heldWas = introHeld;
            try
            {
                CloseTalk();
                Say(null);
                if (typer.Visible) typer.Hide();
                // A chair's veil alone holds nothing back.
                veilUntil = clock.Elapsed.TotalSeconds + 30;
                introHeld = false;
                Say("Through an emote.");
                bool through = speech == "Through an emote.";
                Say(null);
                introHeld = true;
                Remark("", "Held until the intro is over.", true);
                Say("Over her head.");
                OpenTyper();
                bool held = through && talkState == TalkState.Off && speech == null && !typer.Visible;
                veilUntil = -1;
                introHeld = false;
                DateTime until = DateTime.UtcNow.AddSeconds(6);
                while (DateTime.UtcNow < until && (speech == null || talkState == TalkState.Off))
                {
                    System.Windows.Forms.Application.DoEvents();
                    System.Threading.Thread.Sleep(50);
                }
                bool after = speech != null && talkState != TalkState.Off;
                if (!held || !after)
                    throw new Exception("Something of hers showed over an intro, or never came after it: held " + held + ", came after " + after + " (words " + (speech ?? "none") + ", bubble " + talkState + ")");
                checks.Add("PASS while an intro plays nothing of hers shows: a remark, words over her head and her typing box wait, and the lines come once it is over; an emote's veil, like her chair, holds none of it");
            }
            finally
            {
                veilUntil = veilWas;
                introHeld = heldWas;
                Say(null);
                CloseTalk();
            }
        }

        // The intro owns the screen and the speakers until it ends: what any other mod says, plays or cues meanwhile
        // waits, and goes through the moment it is over. The mod playing the intro is never held.
        private string introId;
        private readonly List<Action> afterIntro = new List<Action>();

        internal bool HeldForIntro(string modId) { return IntroShowing(clock.Elapsed.TotalSeconds) && modId != introId; }

        // On her window's thread.
        internal void WhenIntroLets(string modId, Action action)
        {
            if (HeldForIntro(modId)) { lock (afterIntro) afterIntro.Add(action); return; }
            action();
        }

        private void IntroOver()
        {
            List<Action> held;
            lock (afterIntro) { held = new List<Action>(afterIntro); afterIntro.Clear(); }
            foreach (Action one in held)
            {
                try { one(); }
                catch (Exception caughtOne) { Brain.Caught(caughtOne); }
            }
        }

        // An intro played after start, the way a start plays it: she is out of sight in the middle of the screen she is on,
        // and arrives when it ends. Played over her where she stood, the door opened with her already in front of it.
        internal void IntroLikeStart(ICapriIntro intro)
        {
            if (intro == null || introScreen != null) return;
            SpawnInMiddle();
            veilUntil = clock.Elapsed.TotalSeconds + LongestIntro + 5;
            introHeld = true;
            Play(idle);
            RenderFrame();
            RunIntro(intro, delegate { veilUntil = 0; introHeld = false; drawnClip = null; });
        }

        // Waits until the intro is running or her code mods have all started, and gives up after patience.
        private void WaitForIntro(Action arrival, double patience)
        {
            veilUntil = clock.Elapsed.TotalSeconds + patience + LongestIntro + 5;
            introHeld = true;
            Play(idle);
            RenderFrame();
            Action release = delegate
            {
                veilUntil = 0;
                introHeld = false;
                drawnClip = null;
                arrival();
                IntroOver();
            };
            double gaveUpAt = clock.Elapsed.TotalSeconds + patience;
            System.Windows.Forms.Timer wait = new System.Windows.Forms.Timer { Interval = 50 };
            wait.Tick += delegate
            {
                if (IsDisposed) { wait.Dispose(); return; }
                ICapriIntro intro = ModList.StartIntro;
                bool out_ = clock.Elapsed.TotalSeconds >= gaveUpAt;
                if (intro == null && !ModList.CodeSettled && !out_) return;
                wait.Dispose();
                if (intro == null) release();
                else
                {
                    // Back in the middle as it starts, not only as the wait began: the intro is drawn where she stands,
                    // and once a first start drew Door 2 low and to the left after something moved her while it waited.
                    if (introSpawn) SpawnInMiddle();
                    RunIntro(intro, release);
                }
            };
            wait.Start();
        }

        // The screen the last intro was given, for the check that a test run keeps it on its own screen.
        internal Rectangle IntroWasOn;

        private void RunIntro(ICapriIntro intro, Action done)
        {
            Rectangle body = BodyOnScreen(anchor, Body);
            Rectangle screen = Screen.FromRectangle(body).Bounds;
            IntroWasOn = screen;
            body.Offset(-screen.X, -screen.Y);
            double length = LongestIntro;
            try { length = Math.Max(0, Math.Min(LongestIntro, intro.Seconds)); }
            catch (Exception error) { Brain.Log("mod intro", -1, error.ToString()); length = 0; }
            introId = ModList.IdOf(intro);
            double startedAt = clock.Elapsed.TotalSeconds;
            Brain.Log("intro", -1, (introId ?? "an intro") + " playing for " + length.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s, " + startedAt.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s after her start, on " + screen.Width + "x" + screen.Height + " at " + screen.X + "," + screen.Y + ", her middle at " + (body.X + body.Width / 2) + "," + (body.Y + body.Height / 2) + " on it.");
            Action finished = delegate
            {
                Brain.Log("intro", -1, (introId ?? "the intro") + " over at " + clock.Elapsed.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s after her start.");
                introScreen = null;
                introId = null;
                if (done != null) done();
                IntroOver();
            };
            if (length <= 0) { finished(); return; }
            // Her sound out opened before the intro starts its sound, not by its first sound: opening it then held the
            // intro's sound back behind its picture on a first start.
            OpenEffects();
            introScreen = new IntroScreen(intro, screen, body, length, finished, delegate
            {
                Brain.Log("intro", -1, (introId ?? "the intro") + " stopped with Esc at " + clock.Elapsed.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s after her start.");
                try { effects.Clear("intro stopped with Esc"); } catch (Exception caughtOne) { Brain.Caught(caughtOne); }
            });
            introScreen.Begin();
        }

        private sealed class IntroScreen : Form
        {
            private readonly ICapriIntro intro;
            private readonly Rectangle her;
            private readonly double length;
            private readonly Action finished, skipped;
            private readonly System.Diagnostics.Stopwatch ran = new System.Diagnostics.Stopwatch();
            private readonly System.Windows.Forms.Timer beat = new System.Windows.Forms.Timer { Interval = 15 };
            // Her frame timer starves while a window repaints nonstop, taking the Esc check and the end with it, so
            // this runs on its own thread and ends the intro with a posted message, which goes ahead of repaints.
            private System.Threading.Timer guard;
            private int forced;
            internal const double Overdue = 3;
            internal static bool StarveForTest;
            // Esc held on the keyboard; her checks hold it here instead of on the real key.
            internal static Func<bool> EscHeld = delegate { return (Native.GetAsyncKeyState(0x1B) & 0x8000) != 0; };
            private Native.Surface surface;
            private Bitmap desktop;
            private volatile bool over;

            internal IntroScreen(ICapriIntro mod, Rectangle screen, Rectangle body, double seconds, Action done, Action stopped)
            {
                intro = mod;
                her = body;
                length = seconds;
                finished = done;
                skipped = stopped;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                Bounds = screen;
                beat.Tick += delegate { Frame(); };
            }

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams value = base.CreateParams;
                    value.ExStyle |= 0x00080000 | 0x00000080 | 0x08000000 | 0x00000020 | 0x00000008;
                    return value;
                }
            }

            protected override bool ShowWithoutActivation { get { return true; } }

            internal void Begin()
            {
                try
                {
                    surface = new Native.Surface(Width, Height);
                    desktop = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                    try { using (Graphics grab = Graphics.FromImage(desktop)) grab.CopyFromScreen(Bounds.Location, Point.Empty, Bounds.Size); }
                    catch (System.ComponentModel.Win32Exception) { using (Graphics grab = Graphics.FromImage(desktop)) grab.Clear(Color.Black); }
                    intro.Begin(desktop, new Rectangle(0, 0, Width, Height), her);
                    Show();
                }
                catch (Exception error)
                {
                    Brain.Log("mod intro", -1, error.ToString());
                    End();
                    return;
                }
                ran.Start();
                guard = new System.Threading.Timer(Guard, null, 50, 50);
                if (StarveForTest) return;
                beat.Start();
                Frame();
            }

            private void Guard(object state)
            {
                if (over || !IsHandleCreated) return;
                bool escape = EscHeld();
                if (!escape && ran.Elapsed.TotalSeconds < length + Overdue) return;
                if (System.Threading.Interlocked.Exchange(ref forced, 1) != 0) return;
                try { BeginInvoke((Action)delegate { Stop(escape); }); }
                catch (InvalidOperationException) { }
            }

            // Esc, or the intro still up past its length: the intro closes its own windows, then this one goes.
            private void Stop(bool escape)
            {
                if (over) return;
                ICapriIntroStops stops = intro as ICapriIntroStops;
                try { if (stops != null) stops.StopIntro(); } catch (Exception error) { Brain.Log("mod intro", -1, error.ToString()); }
                if (escape) { if (skipped != null) skipped(); }
                else Brain.Log("intro", -1, "The intro was still up " + Overdue + " s past its length, so it was closed.");
                End();
            }

            private void Frame()
            {
                if (over) return;
                double at = ran.Elapsed.TotalSeconds;
                if (at >= length) { End(); return; }
                // Esc is the one way out of an intro: it plays over everything, takes no clicks, and the first one
                // plays once. The intro closes its own windows, and her sound for it is cut.
                if (EscHeld()) { Stop(true); return; }
                try
                {
                    using (Graphics graphics = Graphics.FromImage(surface.Image))
                    {
                        graphics.Clear(Color.Transparent);
                        intro.Draw(graphics, new Rectangle(0, 0, Width, Height), her, at);
                    }
                    surface.Show(Handle, Location);
                }
                catch (Exception error)
                {
                    Brain.Log("mod intro", -1, error.ToString());
                    End();
                }
            }

            private void End()
            {
                if (over) return;
                over = true;
                if (guard != null) guard.Dispose();
                beat.Dispose();
                Close();
                Dispose();
                if (surface != null) surface.Dispose();
                if (desktop != null) desktop.Dispose();
                finished();
            }
        }

        private sealed class TestIntro : ICapriIntro, ICapriIntroStops
        {
            internal int Drawn, Stopped;
            internal bool Throws;
            public void StopIntro() { Stopped++; }
            public double Seconds { get { return 0.3; } }
            public void Begin(Bitmap desktop, Rectangle screen, Rectangle her) { }
            public void Draw(Graphics graphics, Rectangle screen, Rectangle her, double at)
            {
                Drawn++;
                if (Throws) throw new Exception("boom");
            }
        }

        private void CheckIntros(List<string> checks, string output)
        {
            Func<Func<bool>, bool> until = delegate(Func<bool> done)
            {
                for (int i = 0; i < 200 && !done(); i++) { Application.DoEvents(); Thread.Sleep(25); }
                return done();
            };
            foreach (bool throws in new[] { false, true })
            {
                TestIntro stub = new TestIntro { Throws = throws };
                bool ended = false;
                RunIntro(stub, delegate { ended = true; });
                if (!until(() => ended) || stub.Drawn == 0 || introScreen != null) throw new Exception("An intro that " + (throws ? "throws" : "runs") + " did not end cleanly.");
            }
            checks.Add("PASS mod intros: an intro plays and ends, and one that throws still ends");
            // Her frame timer starved, the way a window repainting without pause starves it: the guard still ends the
            // intro once it is past its length, and the intro is told to close its own windows.
            TestIntro starved = new TestIntro();
            bool starvedEnded = false;
            IntroScreen.StarveForTest = true;
            System.Diagnostics.Stopwatch starvedFor = System.Diagnostics.Stopwatch.StartNew();
            try { RunIntro(starved, delegate { starvedEnded = true; }); }
            finally { IntroScreen.StarveForTest = false; }
            if (!until(() => starvedEnded) || introScreen != null || starved.Stopped != 1 || starvedFor.Elapsed.TotalSeconds > starved.Seconds + IntroScreen.Overdue + 2)
                throw new Exception("An intro whose frames stopped stayed up: ended " + starvedEnded + ", told to stop " + starved.Stopped + ", after " + starvedFor.Elapsed.TotalSeconds.ToString("0.0") + " s.");
            checks.Add("PASS mod intros: one whose frame timer is starved still closes " + IntroScreen.Overdue + " s past its length, from a timer of its own, and is told to close its windows");
            // Esc with the frame timer starved: the guard sees the key, stops it at once, and the intro closes its own windows.
            TestIntro escaped = new TestIntro();
            bool escapedEnded = false;
            Func<bool> escWas = IntroScreen.EscHeld;
            IntroScreen.StarveForTest = true;
            System.Diagnostics.Stopwatch escapedFor = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                RunIntro(escaped, delegate { escapedEnded = true; });
                IntroScreen.EscHeld = delegate { return true; };
                until(() => escapedEnded);
            }
            finally { IntroScreen.StarveForTest = false; IntroScreen.EscHeld = escWas; }
            if (!escapedEnded || introScreen != null || escaped.Stopped != 1 || escapedFor.Elapsed.TotalSeconds > 1.5)
                throw new Exception("Esc did not stop an intro whose frames stopped: ended " + escapedEnded + ", told to stop " + escaped.Stopped + ", after " + escapedFor.Elapsed.TotalSeconds.ToString("0.0") + " s.");
            checks.Add("PASS mod intros: Esc stops an intro at once even with its frame timer starved, and the intro is told to close its windows");
            bool arrived = false;
            Clip clipWas = clip;
            bool settledWas = ModList.CodeSettled;
            WaitForIntro(delegate { arrived = true; }, 0.2);
            bool veilHeld = clock.Elapsed.TotalSeconds < veilUntil;
            if (!veilHeld || !until(() => arrived) || clock.Elapsed.TotalSeconds < veilUntil) throw new Exception("Waiting for an intro that never comes did not bring her in.");
            Play(clipWas);
            checks.Add("PASS mod intros: if no intro turns up she's held off screen, then still arrives");
            // A remark made while an intro holds the screen waits for it to end, then is said.
            veilUntil = clock.Elapsed.TotalSeconds + 1.0;
            int askedBefore = asking;
            Remark("news", "A remark held for the intro.", true);
            bool heldBack = asking == askedBefore;
            if (!heldBack || !until(() => asking != askedBefore) || clock.Elapsed.TotalSeconds < veilUntil)
                throw new Exception("A remark was said over an intro instead of after it: held " + heldBack + ".");
            veilUntil = 0;
            // And a tray balloon the same way.
            veilUntil = clock.Elapsed.TotalSeconds + 1.0;
            int balloonsWere = balloonsShown;
            Balloon(500, "Held for the intro", "A balloon held for the intro.", ToolTipIcon.Info);
            bool balloonHeld = balloonsShown == balloonsWere;
            if (!balloonHeld || !until(() => balloonsShown > balloonsWere) || clock.Elapsed.TotalSeconds < veilUntil)
                throw new Exception("A tray balloon showed over an intro instead of after it: held " + balloonHeld + ".");
            veilUntil = 0;
            checks.Add("PASS mod intros: a remark made during an intro waits for it to end, then is said, and so does a tray balloon");
            string real = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "..", "..", "mods", "startup-intros" + Mods.CodeExtension));
            if (!File.Exists(real)) { checks.Add("LIMIT mod intros: the Startup Intros file isn't beside this build, so its frames weren't drawn"); return; }
            Mod mod = ModList.Import(real, delegate { return true; });
            ICapriIntro intros = mod == null ? null : ModList.IntroOf(mod.File.Id);
            if (mod == null || intros == null || !ModList.WantsIntro) throw new Exception("Startup Intros did not start as an intro.");
            Size size = new Size(1920, 1080);
            Rectangle spot = new Rectangle(860, 420, 200, 300);
            string[] picks = { "hijack", "shatter", "wake" };
            List<string> timing = new List<string>();
            foreach (string pick in picks)
            {
                if (!ModList.Heard("intro " + pick)) throw new Exception("Startup Intros didn't take intro " + pick);
                const int across = 5, down = 3, cellW = 384, cellH = 216;
                double seconds = intros.Seconds, worst = 0, worstAt = 0;
                using (Bitmap fake = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
                using (Bitmap sheet = new Bitmap(across * cellW, down * cellH))
                using (Graphics paper = Graphics.FromImage(sheet))
                using (Bitmap frame = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
                using (Graphics pen = Graphics.FromImage(frame))
                {
                    using (Graphics wall = Graphics.FromImage(fake)) wall.Clear(Color.FromArgb(46, 58, 74));
                    intros.Begin(fake, new Rectangle(Point.Empty, size), spot);
                    for (int i = 0; i < across * down; i++)
                    {
                        double at = seconds * i / (across * down - 1) * 0.999;
                        pen.Clear(Color.Transparent);
                        System.Diagnostics.Stopwatch took = System.Diagnostics.Stopwatch.StartNew();
                        intros.Draw(pen, new Rectangle(Point.Empty, size), spot, at);
                        if (i > 0 && took.Elapsed.TotalMilliseconds > worst) { worst = took.Elapsed.TotalMilliseconds; worstAt = at; }
                        Rectangle cell = new Rectangle(i % across * cellW, i / across * cellH, cellW, cellH);
                        using (Brush desktop = new SolidBrush(Color.FromArgb(46, 58, 74))) paper.FillRectangle(desktop, cell);
                        paper.DrawImage(frame, cell);
                        using (Font label = new Font("Consolas", 11f)) paper.DrawString(at.ToString("0.00") + "s", label, Brushes.Yellow, cell.X + 3, cell.Y + 3);
                    }
                    // A frame caught by a garbage collection or a busy PC (83 ms once) is drawn again, so only a frame
                    // that's slow every time fails. Her frames depend only on the time, and each sound plays once.
                    if (worst > 60)
                    {
                        pen.Clear(Color.Transparent);
                        System.Diagnostics.Stopwatch again = System.Diagnostics.Stopwatch.StartNew();
                        intros.Draw(pen, new Rectangle(Point.Empty, size), spot, worstAt);
                        worst = Math.Min(worst, again.Elapsed.TotalMilliseconds);
                    }
                    sheet.Save(Path.Combine(output, "intro-" + pick + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                timing.Add(pick + " " + seconds.ToString("0.0") + " s, slowest frame " + worst.ToString("0") + " ms");
                if (worst > 60) throw new Exception("An intro frame is too slow: " + timing[timing.Count - 1]);
            }
            for (int i = 0; i < 40; i++) { Application.DoEvents(); Thread.Sleep(10); }
            bool previewed = until(() => introScreen == null) || until(() => introScreen == null);
            ModList.Remove(mod);
            if (!previewed || ModList.Running != 0) throw new Exception("Startup Intros did not clean up.");
            checks.Add("PASS mod intros: all three draw at 1920x1080 (" + string.Join("; ", timing) + ")");
        }

        internal void CheckCodeMods(List<string> checks, string output)
        {
            Mods mods = ModList;
            int asked = 0;
            bool answer = false;
            Func<ModFile, string, bool> askWas = mainframe.ModAsk;
            mainframe.ModAsk = delegate { asked++; return answer; };
            Func<string, string, string> sample = delegate(string name, string body)
            {
                string file = Path.Combine(output, name);
                File.WriteAllText(file, body);
                return file;
            };
            Func<string, string, string> code = delegate(string id, string body)
            {
                return "// capri-mod: 1\n// id: " + id + "\n// name: " + id + "\n// version: 1\n// author: Smoke\n// description: A test.\nusing System; using System.IO; using CapriDesktop;\n" + body;
            };
            try
            {
                foreach (Mod old in mods.All.ToArray()) mods.Remove(old);
                string body = "public class M : ICapriMod { ICapriHost c; public void Start(ICapriHost h) { c = h; File.AppendAllText(Path.Combine(h.Folder, \"started\"), \"x\"); } public bool Heard(string w) { if (w != \"mod check\") return false; c.Say(\"Code mod online.\"); return true; } public void Stop() { File.WriteAllText(Path.Combine(c.Folder, \"stopped\"), \"x\"); } }";
                string file = sample("hello" + Mods.CodeExtension, code("hello", body));
                string started = Path.Combine(mods.Folder, "hello-data", "started"), stopped = Path.Combine(mods.Folder, "hello-data", "stopped");
                bool imported = mainframe.ImportMod(file);
                if (imported || asked != 1 || mods.All.Count != 0 || mods.Running != 0 || File.Exists(started))
                    throw new Exception("A code mod ran without being approved: imported " + imported + ", asked " + asked + ", installed " + string.Join(", ", mods.All.ConvertAll(one => one.File.Id).ToArray()) + ", running " + mods.Running + ", started " + File.Exists(started));
                checks.Add("PASS code mods: saying no leaves nothing installed and runs nothing");
                // A mod removed while it compiles to start never runs: a carried mod starting at her start, then every
                // mod removed, left one running with nothing behind it.
                answer = true;
                if (!mainframe.ImportMod(sample("race" + Mods.CodeExtension, code("race", body)))) throw new Exception("A code mod for the start race was refused: " + mainframe.ModStatusText);
                Mod racing = mods.All.Find(one => one.File.Id == "race");
                if (racing == null || mods.RunsAfterRemove(racing) || mods.Running != 0 || mods.All.Count != 0)
                    throw new Exception("A mod removed while it was starting ran anyway: running " + mods.Running + ", installed " + string.Join(", ", mods.All.ConvertAll(one => one.File.Id).ToArray()));
                checks.Add("PASS code mods: one removed while it compiles to start doesn't start when its start comes round");
                answer = false;
                answer = true;
                if (!mainframe.ImportMod(file)) throw new Exception("An approved code mod was refused: " + mainframe.ModStatusText);
                if (mods.Running != 1 || !File.Exists(started) || !mods.All[0].Code || !mods.All[0].On) throw new Exception("An approved code mod did not start.");
                if (mods.Heard("something else")) throw new Exception("A mod took a line it didn't want.");
                string replyWas = reply;
                Hear("mod check", "mod check", "> you: ");
                for (int i = 0; i < 20 && reply != "Code mod online."; i++) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(50); }
                if (reply != "Code mod online.") throw new Exception("A code mod's line wasn't said. Reply: " + replyWas + " -> " + reply);
                checks.Add("PASS code mods: approved code compiles, starts, hears what they say and speaks through her");
                mods.StopCode();
                File.AppendAllText(mods.All[0].Path, "\n// changed");
                mods.Load();
                if (!mods.All[0].Changed || mods.All[0].On) throw new Exception("A changed code mod kept its approval.");
                int startsWere = File.ReadAllText(started).Length;
                bool started2 = false;
                mods.StartCode(delegate(Action act) { act(); started2 = true; });
                Thread.Sleep(300);
                if (started2 || mods.Running != 0 || File.ReadAllText(started).Length != startsWere) throw new Exception("A changed code mod ran.");
                answer = false;
                if (mainframe.SwitchModForTest(0) || mods.Running != 0 || mods.All[0].On) throw new Exception("A changed code mod turned on after they said no.");
                answer = true;
                if (!mainframe.SwitchModForTest(0) || mods.Running != 1 || !mods.All[0].On || mods.All[0].Changed) throw new Exception("Approving a changed code mod did not run it.");
                checks.Add("PASS code mods: any edit needs approving again, and until then it stays off");
                mods.StopCode();
                mods.Load();
                List<Action> queued = new List<Action>();
                mods.StartCode(delegate(Action act) { lock (queued) queued.Add(act); });
                for (int i = 0; i < 600; i++)
                {
                    lock (queued) if (queued.Count >= 2) break;
                    Thread.Sleep(50);
                }
                lock (queued)
                {
                    if (queued.Count < 2) throw new Exception("An approved code mod did not compile at start.");
                    foreach (Action act in queued) act();
                }
                if (!mods.CodeSettled) throw new Exception("Code mods never said they'd settled.");
                if (mods.Running != 1) throw new Exception("An approved code mod did not come back after a restart: " + string.Join(" | ", mods.Problems));
                if (!mainframe.SwitchModForTest(0) || mods.Running != 0 || !File.Exists(stopped)) throw new Exception("Turning a code mod off did not stop it.");
                mods.Load();
                if (mods.All[0].On || mods.All[0].Changed) throw new Exception("Off did not last, or looked like an edit.");
                mods.Remove(mods.All[0]);
                checks.Add("PASS code mods: approved ones start with her, and off stops them and stays off");
                string[,] bad =
                {
                    { "broken", "public class M : ICapriMod { nope }", "compile" },
                    { "none", "public class M { }", "exactly one" },
                    { "twoclasses", "public class A : ICapriMod { public void Start(ICapriHost h) { } public bool Heard(string w) { return false; } public void Stop() { } } public class B : A { }", "exactly one" },
                    { "throws", "public class M : ICapriMod { public void Start(ICapriHost h) { throw new Exception(\"boom\"); } public bool Heard(string w) { return false; } public void Stop() { } }", "boom" },
                    { "maker", "public class M : ICapriMod { public M() { throw new Exception(\"bang\"); } public void Start(ICapriHost h) { } public bool Heard(string w) { return false; } public void Stop() { } }", "bang" }
                };
                for (int i = 0; i < bad.GetLength(0); i++)
                {
                    bool let = mainframe.ImportMod(sample(bad[i, 0] + Mods.CodeExtension, code(bad[i, 0], bad[i, 1])));
                    if (bad[i, 0] == "throws")
                    {
                        if (let || mods.Running != 0 || mainframe.ModStatusText.IndexOf(bad[i, 2], StringComparison.Ordinal) < 0 || mods.All.Count != 1 || mods.All[0].On || !mainframe.ModStatusText.EndsWith("It's saved, and off until you turn it on again.", StringComparison.Ordinal) || !mainframe.ModStatusHovers) throw new Exception("A mod that throws at start was let run, or still showed On: " + mainframe.ModStatusText);
                        if (Brain.LogPath != null && (!File.Exists(Brain.LogPath) || File.ReadAllText(Brain.LogPath).IndexOf("off until you turn it on again", StringComparison.Ordinal) < 0)) throw new Exception("A mod that failed to start left no line in errors.txt.");
                        checks.Add("PASS code mods: one whose Start fails is saved but shows off and says so, its reason goes to errors.txt, and the Mods status line takes the mouse so a long error shows in full on hover");
                        foreach (Mod left in mods.All.ToArray()) mods.Remove(left);
                    }
                    else if (let || mods.All.Count != 0 || mainframe.ModStatusText.IndexOf(bad[i, 2], StringComparison.Ordinal) < 0) throw new Exception("A bad code mod was let in: " + bad[i, 0] + ": " + mainframe.ModStatusText);
                }
                string thrower = code("thrower", "public class M : ICapriMod { public void Start(ICapriHost h) { } public bool Heard(string w) { throw new Exception(\"boom\"); } public void Stop() { throw new Exception(\"boom\"); } }");
                if (!mainframe.ImportMod(sample("thrower" + Mods.CodeExtension, thrower))) throw new Exception("A mod that throws later was refused.");
                if (mods.Heard("anything") || mods.All.Count != 1) throw new Exception("A throwing mod took a line.");
                mods.Remove(mods.All[0]);
                if (mods.Running != 0) throw new Exception("A throwing mod could not be removed.");
                if (mainframe.ImportMod(sample("noheader" + Mods.CodeExtension, "using System;"))) throw new Exception("A file with no details was let in.");
                checks.Add("PASS code mods: broken code is refused with its reason, and a mod that throws can't take her down");
                CheckModHooks(checks, output);
                CheckIntros(checks, output);
                CheckModGuard(checks, sample, code);
            }
            finally
            {
                mainframe.ModAsk = askWas;
                mods.StopCode();
                foreach (Mod left in mods.All.ToArray()) mods.Remove(left);
            }
        }

        // The guard that switches off a mod that breaks her, and the checked list: with untested mods off, a mod off
        // the list is refused or switched off with its reason; with them on it is asked about; a listed one never is.
        private void CheckTrust(List<string> checks, Func<string, string, string> sample)
        {
            Mods mods = ModList;
            bool untestedWas = Mods.UntestedForChecks;
            Func<ModFile, string, bool> askWas = mainframe.ModAsk;
            int asked = 0;
            string lastAsked = null;
            mainframe.ModAsk = delegate(ModFile read, string hash) { asked++; lastAsked = Mods.Consent(read, hash); return true; };
            try
            {
                // Each listed fork's hash is its file's in the source this build came from, so a fork changed without a
                // new hash on the list fails here. Read from the nearest source tree up from the exe.
                string forks = null;
                for (DirectoryInfo up = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); up != null && forks == null; up = up.Parent)
                    foreach (string here in new[] { Path.Combine(up.FullName, "mods", "forks"), Path.Combine(up.FullName, "CapriSourceFiles", "mods", "forks") })
                        if (forks == null && Directory.Exists(here)) forks = here;
                if (forks != null)
                    foreach (string file in Directory.GetFiles(forks, "*" + Mods.CodeExtension))
                    {
                        byte[] bytes = File.ReadAllBytes(file);
                        string forkId = Mods.ReadCode(Mods.Words(bytes)).Id;
                        if (!Mods.OnCheckedList(forkId, bytes)) throw new Exception(Path.GetFileName(file) + " isn't on the checked list as it is in the source.");
                        // The same file saved with the other line ends is the same file.
                        List<byte> other = new List<byte>();
                        for (int at = 0; at < bytes.Length; at++)
                        {
                            if (bytes[at] == 13 && at + 1 < bytes.Length && bytes[at + 1] == 10) continue;
                            if (bytes[at] == 10) other.Add(13);
                            other.Add(bytes[at]);
                        }
                        if (!Mods.OnCheckedList(forkId, other.ToArray())) throw new Exception(Path.GetFileName(file) + " with its line ends changed isn't on the checked list.");
                    }
                foreach (string door in new[] { Mods.DoorId, Mods.FrontId })
                {
                    byte[] bytes = Mods.CarriedBytes(door);
                    if (bytes == null || !Mods.OnCheckedList(door, bytes) || Mods.OnCheckedList(door, Encoding.UTF8.GetBytes("other"))) throw new Exception(door + " isn't on the checked list as this build carries it, or another copy of it is.");
                }
                // Startup Intros and grammar are carried only to be checked against, at what this build carries. The
                // public build leaves grammar out, as it was never published, so only one this exe carries is held to it.
                List<string> carriedHere = new List<string>(typeof(Mods).Assembly.GetManifestResourceNames());
                foreach (string carriedOnly in new[] { "startup-intros", "grammar" })
                {
                    string source = forks == null ? null : Path.Combine(Path.GetDirectoryName(forks), carriedOnly + Mods.CodeExtension);
                    bool carries = carriedHere.Contains("Capri.Checked." + carriedOnly + Mods.CodeExtension);
                    if (!carries && carriedOnly == "startup-intros") throw new Exception("This build doesn't carry Startup Intros to check against.");
                    if (carries && source != null && File.Exists(source) && !Mods.OnCheckedList(carriedOnly, File.ReadAllBytes(source))) throw new Exception(carriedOnly + " as the source has it isn't on the checked list.");
                    if (Mods.OnCheckedList(carriedOnly, Encoding.UTF8.GetBytes("other"))) throw new Exception("Another copy of " + carriedOnly + " is on the checked list.");
                }

                string body = "// capri-mod: 1\n// id: untested\n// name: Untested\n// version: 1.0.0\n// author: Smoke\n// description: A test.\n// permissions: chat\n"
                    + "using System; using CapriDesktop;\npublic class M : ICapriMod { public void Start(ICapriHost h) { } public bool Heard(string w) { return false; } public void Stop() { } }";
                Mods.UntestedForChecks = false;
                if (Mods.Untested) throw new Exception("Untested mods start on; they should start off.");
                if (mainframe.ImportMod(sample("untested" + Mods.CodeExtension, body)) || asked != 0 || mainframe.ModStatusText.IndexOf("checked list", StringComparison.Ordinal) < 0)
                    throw new Exception("A mod off the checked list wasn't refused before asking: asked " + asked + ", " + mainframe.ModStatusText);

                Mods.UntestedForChecks = true;
                if (!mainframe.ImportMod(sample("untested" + Mods.CodeExtension, body)) || asked != 1 || lastAsked.IndexOf(Mods.UncheckedNote, StringComparison.Ordinal) < 0)
                    throw new Exception("With untested mods on, a mod off the list wasn't asked about as untested: " + mainframe.ModStatusText);
                Mod untested = mods.All.Find(each => each.File.Id == "untested");
                if (untested == null || !untested.On) throw new Exception("The untested mod didn't run with untested mods on.");

                Mods.UntestedForChecks = false;
                string said = mods.GuardAtStart();
                Dictionary<string, string> reasons = ModGuard.Reasons();
                string reason;
                if (untested.On || !reasons.TryGetValue("untested", out reason) || reason != Mods.UncheckedOff || !File.Exists(untested.Path) || said == null || said.IndexOf("checked list", StringComparison.Ordinal) < 0)
                    throw new Exception("A mod off the list wasn't switched off at her start with its reason, or was deleted: " + (said ?? "nothing said"));
                string row = mainframe.PictureMods(Path.Combine(Path.GetDirectoryName(sample("trust-out.txt", "")), "mods-switched-off.png"), "untested");
                if (row == null || !row.StartsWith("SWITCHED OFF: " + Mods.UncheckedOff, StringComparison.Ordinal)) throw new Exception("The switched-off mod's row doesn't say why: " + (row ?? "no row"));
                bool refused = false;
                try { refused = !mods.Set(untested, true, mainframe.ModAsk); }
                catch (ModException) { refused = true; }
                if (!refused || untested.On) throw new Exception("A mod off the list was turned back on with untested mods off.");
                CheckUntestedSwitch(checks, mods, untested, sample);
                mods.Remove(untested);

                // A listed one is never asked, with untested mods off: the speech glass fork, as the source has it, goes
                // straight in, and comes out again.
                string glass = forks == null ? null : Path.Combine(forks, "hologram-speech-glass" + Mods.CodeExtension);
                int before = asked;
                Mods.UntestedForChecks = false;
                if (glass != null && File.Exists(glass) && mods.All.Find(each => each.File.Id == "hologram-speech-glass") == null)
                {
                    try
                    {
                        if (!mainframe.ImportMod(glass) || asked != before) throw new Exception("A mod on the checked list was asked about, or refused: " + mainframe.ModStatusText);
                    }
                    finally
                    {
                        Mod listed = mods.All.Find(each => each.File.Id == "hologram-speech-glass");
                        if (listed != null) mods.Remove(listed);
                    }
                }
                CheckChangedChecked(checks, mods, forks);
                CheckOverridesKept(checks, sample);
                checks.Add("PASS the checked list: the forks are on it as their source files are and the doors as this build carries them; with Load untested mods no, as sys.md ships, a mod off it is refused before anyone is asked, one already on is switched off at her start with its reason on its row and nothing deleted, and it can't be turned back on; with it yes the mod is asked about as not tested; a listed mod is never asked");
            }
            finally
            {
                Mods.UntestedForChecks = untestedWas;
                mainframe.ModAsk = askWas;
                Mod left = mods.All.Find(each => each.File.Id == "untested");
                if (left != null) mods.Remove(left);
            }
        }

        // Only her own windows change her security: overrides are signed in a gated folder, one changed outside her is
        // dropped with a warning, a mod naming any of it is flagged High, and nothing a mod can switch is a security
        // setting.
        private void CheckOverridesKept(List<string> checks, Func<string, string, string> sample)
        {
            string settingsWas = PluginSettings.Settings;
            Func<bool> sideWas = PluginSettings.SecondSide;
            Action<string> tamperedWas = PluginSettings.Tampered;
            string folder = Path.Combine(Path.GetDirectoryName(sample("overrides-out.txt", "")), "overrides-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            try
            {
                string warned = null;
                PluginSettings.Tampered = delegate(string said) { warned = said; };
                PluginSettings.SecondSide = delegate { return false; };
                // One from before signing, in the side's own folder: moved in once, signed, and the old one gone.
                string old = Path.Combine(folder, Personalities.PlainName, PluginSettings.FileName);
                Directory.CreateDirectory(Path.GetDirectoryName(old));
                File.WriteAllText(old, "tool\tsmoke-old\tread\t" + Security.Lockdown + "\tallow\r\n");
                PluginSettings.Settings = folder;
                PluginSettings.Reseal();
                bool moved = PluginSettings.Of("smoke-old", PluginSettings.Tool, "read", Security.Lockdown) == PluginSettings.Allowed && !File.Exists(old)
                    && File.ReadAllText(PluginSettings.Path).Contains(PluginSettings.Signed) && warned == null;
                bool gated = true;
                try { gated = McpSignIn.Gated(PluginSettings.Folder); }
                catch (PlatformNotSupportedException) { }
                // Her cog's write is kept and read back after the key is opened again from its sealed file.
                PluginSettings.Set("smoke-kept", PluginSettings.Tool, "write", Security.Trusted, PluginSettings.Denied);
                PluginSettings.Reseal();
                bool kept = PluginSettings.Of("smoke-kept", PluginSettings.Tool, "write", Security.Trusted) == PluginSettings.Denied && warned == null;
                // Changed outside her: a cross turned into a tick by hand is not used, every cell is back to /, and she says so.
                string path = PluginSettings.Path;
                File.WriteAllText(path, File.ReadAllText(path).Replace("\tdeny", "\tallow"));
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));
                bool refused = PluginSettings.Of("smoke-kept", PluginSettings.Tool, "write", Security.Trusted) == PluginSettings.Follows
                    && PluginSettings.Of("smoke-old", PluginSettings.Tool, "read", Security.Lockdown) == PluginSettings.Follows
                    && warned != null && warned.IndexOf("changed outside Capri", StringComparison.Ordinal) >= 0;
                // A file with its signing line taken off is refused the same way.
                warned = null;
                PluginSettings.Set("smoke-kept", PluginSettings.Tool, "write", Security.Trusted, PluginSettings.Denied);
                List<string> rows = new List<string>(File.ReadAllLines(path));
                rows.RemoveAll(row => row.StartsWith(PluginSettings.Signed, StringComparison.Ordinal));
                File.WriteAllLines(path, rows.ToArray());
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(10));
                bool unsigned = PluginSettings.Of("smoke-kept", PluginSettings.Tool, "write", Security.Trusted) == PluginSettings.Follows && warned != null;
                // Her settings.json's copy: read out as text and put back signed, the import being their own choice.
                warned = null;
                PluginSettings.Set("smoke-kept", PluginSettings.Tool, "write", Security.Guarded, PluginSettings.Allowed);
                string snapshot = PluginSettings.Snapshot();
                PluginSettings.Set("smoke-kept", PluginSettings.Tool, "write", Security.Guarded, PluginSettings.Follows);
                PluginSettings.Restore(snapshot);
                PluginSettings.Reseal();
                bool restored = PluginSettings.Of("smoke-kept", PluginSettings.Tool, "write", Security.Guarded) == PluginSettings.Allowed && warned == null;
                // approved.txt: one from before signing is taken once and signed; after that a hand-made approval isn't used, every
                // approval goes, and she warns them.
                warned = null;
                string approvals = Path.Combine(folder, "mods", "approved.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(approvals));
                File.WriteAllText(approvals, "smoke-door abc\r\nplugin:smoke-plug def\r\n");
                bool takenOnce = Mods.ApprovedAt(approvals).Count == 2 && File.ReadAllText(approvals).Contains(PluginSettings.Signed) && warned == null;
                Dictionary<string, string> mine = Mods.ApprovedAt(approvals);
                mine["smoke-new"] = "123";
                Mods.WriteApprovedAt(approvals, mine);
                bool signedKept = Mods.ApprovedAt(approvals).Count == 3 && warned == null;
                File.WriteAllText(approvals, File.ReadAllText(approvals).Replace("smoke-door abc", "smoke-door fff"));
                bool handRefused = Mods.ApprovedAt(approvals).Count == 0 && warned != null && warned.IndexOf("asks again", StringComparison.Ordinal) >= 0;
                warned = null;
                File.WriteAllText(approvals, "smoke-door abc\r\n");
                bool unsignedRefused = Mods.ApprovedAt(approvals).Count == 0 && warned != null;
                bool approvalsKept = takenOnce && signedKept && handRefused && unsignedRefused;
                // Her mouse in a job: a press or key aimed at her Mainframe or a window it opened is refused before anything
                // moves, and her other windows and everyone else's are left alone.
                Func<Point, IntPtr> windowWas = Computer.WindowAt;
                string handsOff, elsewhere;
                bool mainframeKnown, ownedKnown;
                try
                {
                    mainframeKnown = Computer.HerMainframe(mainframe.Handle) && !Computer.HerMainframe(IntPtr.Zero) && !Computer.HerMainframe(Handle);
                    using (Form owned = new Form { ShowInTaskbar = false, Opacity = 0 })
                    {
                        owned.Show(mainframe);
                        ownedKnown = Computer.HerMainframe(owned.Handle);
                        owned.Close();
                    }
                    Computer.WindowAt = delegate { return mainframe.Handle; };
                    handsOff = Computer.Member("left_click", new ToolArguments(), null);
                    Computer.WindowAt = delegate { return IntPtr.Zero; };
                    elsewhere = Computer.Member("cursor_position", new ToolArguments(), null);
                }
                finally { Computer.WindowAt = windowWas; }
                bool mouseKept = mainframeKnown && ownedKnown && handsOff == Computer.HandsOff && elsewhere != Computer.HandsOff;
                // A mod naming any of it is High before anyone says yes.
                bool flagged = true;
                foreach (string named in new[] { "File.WriteAllText(Path.Combine(root, \"keys\", \"overrides\", \"plain.txt\"), x);", "string f = \"keys/overrides/plain.txt\";", "PluginSettings.Set(a, b, c, 0, 1);",
                    "File.AppendAllText(Path.Combine(mods, \"approved.txt\"), line);", "File.WriteAllText(\"plugins-on.txt\", id);", "Security.Pick(3);" })
                    if (Array.IndexOf(Mods.Reaching(named), Mods.ChangesSecurity) < 0) flagged = false;
                flagged = flagged && Mods.GroupOf(Mods.ChangesSecurity) == 'H';
                // Nothing a mod's Settings.Turn or SetLevel, or her own set tool, can reach is a security setting.
                List<string> reachable = new List<string>();
                for (int i = 0; i < Hands.OwnSwitches.GetLength(0); i++) reachable.Add(Hands.OwnSwitches[i, 0]);
                for (int i = 0; i < Hands.OwnLevels.GetLength(0); i++) reachable.Add(Hands.OwnLevels[i, 0]);
                string reaching = reachable.Find(name => System.Text.RegularExpressions.Regex.IsMatch(name, "secur|lockdown|guarded|trusted|blackwall|override|untested|approv|plug|mods?_|_mods?$|permission", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
                if (!moved || !gated || !kept || !refused || !unsigned || !restored || !flagged || reaching != null || !approvalsKept || !mouseKept)
                    throw new Exception("Her security overrides could be changed outside her windows: moved in once and signed " + moved + ", folder gated " + gated + ", kept " + kept + ", a changed file refused with a warning " + refused
                        + ", an unsigned one refused " + unsigned + ", settings.json's copy put back signed " + restored + ", flagged High in a mod " + flagged + ", a security setting a mod or her set tool can switch " + (reaching ?? "none")
                        + ", approved.txt: one from before taken once " + takenOnce + ", her own write kept " + signedKept + ", a hand edit refused " + handRefused + ", an unsigned one refused " + unsignedRefused
                        + ", her mouse kept off her Mainframe " + mouseKept);
                checks.Add("PASS only her own windows change her security: the overrides are kept signed in a gated folder, a file changed or unsigned outside her is not used and every cell goes back to / with a warning, one from before signing moves in once, approved.txt is signed the same way (one from before taken once, a hand edit or unsigned one leaves every mod and plug-in asking again, with a warning), her mouse in a job refuses her Mainframe and every window it opened, settings.json's copy goes back signed, a mod naming the overrides, approved.txt, plugins-on.txt or SECURITY is flagged High, and no setting a mod or her set tool can switch is a security one");
            }
            finally
            {
                PluginSettings.Settings = settingsWas;
                PluginSettings.SecondSide = sideWas;
                PluginSettings.Tampered = tamperedWas;
                PluginSettings.Reseal();
            }
        }

        // A listed mod approved here as another file asks before it runs, saying what changed: the list only stands in
        // for a mod never approved here. No leaves it off; Yes runs it and records this file.
        private void CheckChangedChecked(List<string> checks, Mods mods, string forks)
        {
            const string id = "hologram-speech-glass";
            string glass = forks == null ? null : Path.Combine(forks, id + Mods.CodeExtension);
            if (glass == null || !File.Exists(glass) || mods.All.Find(each => each.File.Id == id) != null)
            {
                checks.Add("SKIP a changed checked mod asks again: no speech glass fork in the source beside this build");
                return;
            }
            Func<ModFile, string, bool> askWas = mainframe.ModAsk;
            bool untestedWas = Mods.UntestedForChecks;
            int asked = 0;
            bool answer = false;
            string question = "";
            mainframe.ModAsk = delegate(ModFile read, string hash) { asked++; question = Mods.Consent(read, hash); return answer; };
            Func<Mod> current = delegate { return mods.All.Find(each => each.File.Id == id); };
            string approvedFile = Path.Combine(mods.Folder, "approved.txt"), other = new string('0', 64);
            Func<string> recorded = delegate
            {
                foreach (string row in File.ReadAllLines(approvedFile)) if (row.StartsWith(id + " ", StringComparison.Ordinal)) return row.Substring(id.Length + 1);
                return null;
            };
            try
            {
                Mods.UntestedForChecks = true;
                if (!mainframe.ImportMod(glass) || asked != 0 || current() == null) throw new Exception("The checked fork wasn't taken in unasked: " + mainframe.ModStatusText);
                string own = recorded();
                // Off, then its record made another file's, the way an older approval of it reads.
                Action approvedAsOther = delegate
                {
                    mods.Set(current(), false, mainframe.ModAsk);
                    mods.ApproveForChecks(id, other);
                    mods.Load();
                    mainframe.FillMods();
                };
                List<string> wrong = new List<string>();
                Action<string, bool, Action> route = delegate(string name, bool untested, Action turnOn)
                {
                    approvedAsOther();
                    Mods.UntestedForChecks = untested;
                    int was = asked;
                    answer = false;
                    question = "";
                    turnOn();
                    Mod now = current();
                    bool once = asked == was + 1, says = question.IndexOf("Changed since you approved", StringComparison.Ordinal) >= 0;
                    bool off = now != null && !now.On && now.Changed, kept = recorded() == other;
                    if (!once || !says || !off || !kept) wrong.Add(name + ": asked " + (asked - was) + ", says what changed " + says + ", stayed off " + off + ", its record kept " + kept);
                    Mods.UntestedForChecks = true;
                };
                route("row switch", true, delegate { mainframe.SwitchModForTest(id); });
                route("row switch with untested mods off", false, delegate { mainframe.SwitchModForTest(id); });
                route("import", true, delegate { mainframe.ImportMod(glass); });
                route("update", true, delegate { mainframe.UpdateMod(glass, id); });
                route("dropped into the folder", true, delegate { mainframe.DropForTest(current().Path); });
                // Yes runs it and records this file.
                approvedAsOther();
                answer = true;
                int before = asked;
                bool yes = mainframe.SwitchModForTest(id) && asked == before + 1 && current().On && !current().Changed && recorded() == own;
                if (wrong.Count > 0 || !yes)
                    throw new Exception("A checked mod approved as another file ran without asking what changed: " + string.Join("; ", wrong.ToArray()) + (yes ? "" : "; Yes didn't run it and record it"));
                checks.Add("PASS a mod on the checked list that was approved as another file asks before it runs, from the row switch with untested mods on or off, import, an update and a drop into the folder, saying what changed; No leaves it off with its record as it was, Yes runs it and records the file");
            }
            finally
            {
                Mods.UntestedForChecks = untestedWas;
                mainframe.ModAsk = askWas;
                Mod left = current();
                if (left != null) mods.Remove(left);
            }
        }

        // The switch in Mods > MODS keeps this in her settings: off switches off a running mod not on the checked list,
        // with its reason. An older sys.md or settings.json hands its answer over once, from this side's file.
        private void CheckUntestedSwitch(List<string> checks, Mods mods, Mod untested, Func<string, string, string> sample)
        {
            bool forChecksWas = Mods.UntestedForChecks, keptWas = preferences.LoadUntested;
            try
            {
                Mods.UntestedForChecks = true;
                if (!mods.Set(untested, true, mainframe.ModAsk) || !untested.On) throw new Exception("The untested mod didn't go back on with untested mods on.");
                Mods.UntestedForChecks = false;
                SetUntested(false);
                mainframe.FillMods();
                bool shownOff = !mainframe.UntestedOn;
                mainframe.TurnUntested(true);
                bool on = preferences.LoadUntested && Mods.Untested && untested.On && LoadPreferences().LoadUntested;
                mainframe.TurnUntested(false);
                Dictionary<string, string> reasons = ModGuard.Reasons();
                string reason;
                bool off = !preferences.LoadUntested && !Mods.Untested && !untested.On && reasons.TryGetValue("untested", out reason) && reason == Mods.UncheckedOff && File.Exists(untested.Path);
                // On again: the reason leaves its row and the status line, and the mod stays off until it is turned on, asking.
                string offStatus = mainframe.ModStatusText;
                mainframe.TurnUntested(true);
                string rowOn = mainframe.PictureMods(Path.Combine(Path.GetDirectoryName(sample("trust-out.txt", "")), "mods-untested-on.png"), "untested");
                Func<bool> cleared = delegate { return !ModGuard.Reasons().ContainsKey("untested") && !untested.On; };
                bool clickCleared = cleared() && rowOn != null && !rowOn.StartsWith("SWITCHED OFF", StringComparison.Ordinal)
                    && offStatus.IndexOf("Switched off", StringComparison.Ordinal) >= 0 && mainframe.ModStatusText.IndexOf("Switched off", StringComparison.Ordinal) < 0;
                mainframe.TurnUntested(false);
                ModGuard.Why("untested", Mods.UncheckedOff);
                // A sys.md from before, with its section saying yes and a line of theirs elsewhere.
                string newline = Environment.NewLine, fresh = SysLines.Text;
                bool shipsNone = fresh.IndexOf(SysLines.UntestedKey, StringComparison.OrdinalIgnoreCase) < 0 && fresh.IndexOf(SysLines.UntestedHeading, StringComparison.Ordinal) < 0;
                string section = string.Join(newline, new[] { SysLines.UntestedHeading, "", ">! no: only code mods on Capri's checked list run. yes: any code mod can ask to run, yours too.",
                    ">! A mod switched off for this says so in Mainframe > Mods. Nothing of it is deleted.", "", SysLines.UntestedKey + ":   yes", "", "" });
                string old = fresh.Replace("## Never watch these", section + "## Never watch these");
                string path = sample("sys-before-the-switch.md", old);
                File.WriteAllText(path, old, new UTF8Encoding(false));
                MoveUntested(path);
                string after = File.ReadAllText(path);
                bool moved = preferences.LoadUntested && Mods.Untested && after == fresh;
                bool moveCleared = cleared();
                ModGuard.Why("untested", Mods.UncheckedOff);
                SetUntested(false);
                MoveUntested(path);
                bool once = !preferences.LoadUntested && File.ReadAllText(path) == fresh;
                // A settings.json from before: this side's yes goes to her setting, the other side's is not used, nothing is
                // listed as missing.
                string folder = Path.Combine(Path.GetDirectoryName(path), "moved-" + Guid.NewGuid().ToString("N").Substring(0, 6));
                string side = Brain.SenpaiOn ? Personalities.SenpaiName : Personalities.PlainName, other = Brain.SenpaiOn ? Personalities.PlainName : Personalities.SenpaiName;
                foreach (string one in new[] { side, other })
                {
                    Directory.CreateDirectory(Path.Combine(folder, one));
                    File.WriteAllText(Path.Combine(folder, one, SysLines.FileName), fresh, new UTF8Encoding(false));
                }
                SettingsFile.Dump dump = new SettingsFile.Dump { Files = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>() };
                dump.Files[other + "/" + SysLines.FileName] = new Dictionary<string, Dictionary<string, string>> { { "Untested mods", new Dictionary<string, string> { { SysLines.UntestedKey, "yes" } } } };
                int set;
                List<string> missing = SettingsFile.Import(dump, folder, out set);
                bool otherIgnored = !preferences.LoadUntested;
                dump.Files.Clear();
                dump.Files[side + "/" + SysLines.FileName] = new Dictionary<string, Dictionary<string, string>> { { "Untested mods", new Dictionary<string, string> { { SysLines.UntestedKey, "yes" } } } };
                missing.AddRange(SettingsFile.Import(dump, folder, out set));
                bool routed = preferences.LoadUntested && Mods.Untested && File.ReadAllText(Path.Combine(folder, side, SysLines.FileName)) == fresh;
                bool importCleared = cleared();
                // On at her start, from a settings.json edited while she was closed.
                ModGuard.Why("untested", Mods.UncheckedOff);
                mods.GuardAtStart();
                bool startCleared = cleared();
                mainframe.FillMods();
                if (!shownOff || !on || !off || !shipsNone || !moved || !once || !otherIgnored || !routed || missing.Count != 0)
                    throw new Exception("Load untested mods didn't move from sys.md to its switch cleanly: shown off " + shownOff + ", on " + on + ", off " + off + ", fresh sys.md without it " + shipsNone
                        + ", taken from an old sys.md " + moved + ", only once " + once + ", the other side's ignored " + otherIgnored + ", routed from settings.json " + routed + ", missing " + string.Join(", ", missing.ToArray()));
                if (!clickCleared || !moveCleared || !importCleared || !startCleared)
                    throw new Exception("Turning Load untested mods on left a mod marked switched off for it, or turned it on: by a click " + clickCleared + " (row " + (rowOn ?? "none") + ", status " + mainframe.ModStatusText
                        + "), from an old sys.md " + moveCleared + ", from a settings.json " + importCleared + ", at her start " + startCleared);
                List<string> shots = mainframe.PictureUntested(Path.GetDirectoryName(path));
                checks.Add("PASS turning Load untested mods on, by a click, an old sys.md, a settings.json or at her start, takes the switched-off reason off each mod it was on, and the status line with it; the mods stay off until turned on, each asking");
                checks.Add("PASS Load untested mods lives in her settings, not sys.md: the switch in Mods > MODS keeps it, off switches off a running mod off the checked list with its reason; a fresh sys.md has no line for it, an old one hands its answer over once and loses only that section, and a settings.json from before routes this side's answer to the switch (pictures: " + string.Join(", ", shots.ToArray()) + ")");
            }
            finally
            {
                Mods.UntestedForChecks = forChecksWas;
                SetUntested(keptWas);
                mainframe.FillMods();
            }
        }

        private void CheckModGuard(List<string> checks, Func<string, string, string> sample, Func<string, string, string> code)
        {
            Mods mods = ModList;
            ModGuard.Folder(mods.Folder);
            bool badlyWas = ModGuard.ClosedBadly, noneWas = ModGuard.NoneThisRun;
            string guard = Path.Combine(mods.Folder, ModGuard.GuardFile), why = Path.Combine(mods.Folder, ModGuard.WhyFile);
            Func<Func<bool>, bool> until = delegate(Func<bool> done)
            {
                for (int i = 0; i < 120 && !done(); i++) { Application.DoEvents(); Thread.Sleep(25); }
                return done();
            };
            try
            {
                for (int i = 1; i < ModGuard.MostThrows; i++) if (ModGuard.Threw("counted")) throw new Exception("A mod was counted out after " + i + " throws.");
                if (!ModGuard.Threw("counted") || ModGuard.Threw("counted")) throw new Exception("A mod's throws were not counted out at " + ModGuard.MostThrows + ", or its count did not start again.");
                checks.Add("PASS mod guard: a mod's hook throws are counted, it is counted out at " + ModGuard.MostThrows + " inside a minute, and its count starts again");

                MethodInfo compile = typeof(Mods).GetMethod("Compile", BindingFlags.NonPublic | BindingFlags.Static);
                ICapriMod crasher = (ICapriMod)compile.Invoke(null, new object[] { code("crasher", "public class M : ICapriMod { public void Start(ICapriHost h) { } public bool Heard(string w) { throw new InvalidOperationException(\"crash\"); } public void Stop() { } }") });
                ModGuard.Loaded(crasher.GetType().Assembly, "crasher");
                string named = null;
                try { crasher.Heard("go"); }
                catch (Exception thrown) { named = ModGuard.In(thrown); }
                if (named != "crasher") throw new Exception("A crash in a mod's code didn't name the mod: " + (named ?? "nobody"));
                checks.Add("PASS mod guard: a crash in a mod's code names that mod from the crash's own stack");

                Func<bool, string[], List<KeyValuePair<string, string>>> start = delegate(bool badly, string[] on)
                {
                    ModGuard.ClosedBadly = badly;
                    return ModGuard.AtStart(new List<string>(on));
                };
                File.WriteAllText(guard, "starting\talpha" + Environment.NewLine);
                List<KeyValuePair<string, string>> off = start(true, new[] { "alpha", "beta" });
                if (off.Count != 1 || off[0].Key != "alpha" || off[0].Value.IndexOf("starting", StringComparison.Ordinal) < 0) throw new Exception("A mod that was starting when she stopped wasn't named.");
                File.WriteAllText(guard, "starting\talpha" + Environment.NewLine);
                if (start(false, new[] { "alpha" }).Count != 0) throw new Exception("A mod was named after a clean close.");
                File.WriteAllText(guard, "blame\tbeta\tit froze her" + Environment.NewLine);
                off = start(true, new[] { "alpha", "beta" });
                if (off.Count != 1 || off[0].Key != "beta" || off[0].Value != "it froze her") throw new Exception("A mod blamed for a freeze wasn't named with why.");
                File.WriteAllText(guard, "");
                if (start(true, new[] { "alpha", "beta" }).Count != 0) throw new Exception("One unexplained close switched mods off.");
                off = start(true, new[] { "alpha", "beta" });
                if (off.Count != 2) throw new Exception("Two unexplained closes in a row with mods on didn't switch them all off: " + off.Count);
                start(true, new string[0]);
                if (start(false, new[] { "alpha" }).Count != 0 || File.ReadAllText(guard).Length != 0) throw new Exception("A clean close didn't start the count again.");
                checks.Add("PASS mod guard: after a bad close the mod that was starting or was blamed is named with why, a clean close names nobody, and " + ModGuard.Unexplained + " unexplained closes in a row with mods on switch them all off");

                string body = "public class M : ICapriMod { public void Start(ICapriHost h) { } public bool Heard(string w) { return false; } public void Stop() { } }";
                if (!mainframe.ImportMod(sample("guarded" + Mods.CodeExtension, code("guarded", body)))) throw new Exception("The guard's test mod was refused: " + mainframe.ModStatusText);
                Mod guarded = mods.All.Find(each => each.File.Id == "guarded");
                ModGuard.ClosedBadly = true;
                File.WriteAllText(guard, "blame\tguarded\tit crashed her: InvalidOperationException: crash" + Environment.NewLine);
                string said = mods.GuardAtStart();
                if (said == null || said.IndexOf("guarded", StringComparison.Ordinal) < 0 || guarded.On || mods.Running != 0
                    || !File.ReadAllText(Path.Combine(mods.Folder, "off.txt")).Contains("guarded") || !ModGuard.Reasons().ContainsKey("guarded"))
                    throw new Exception("A mod that crashed her wasn't switched off and kept off with why: " + (said ?? "nothing said"));
                if (!mods.Set(guarded, true, delegate { return true; }) || !guarded.On || ModGuard.Reasons().ContainsKey("guarded")) throw new Exception("Turning a switched-off mod back on kept its reason.");
                checks.Add("PASS mod guard: at her start a mod that crashed her is switched off, kept off and named in her line with why, and turning it back on clears that");

                mods.Remove(guarded);
                string thrower = "public class M : ICapriMod { public void Start(ICapriHost h) { h.Timers.Every(0.05, delegate { throw new InvalidOperationException(\"again\"); }); } public bool Heard(string w) { return false; } public void Stop() { } }";
                if (!mainframe.ImportMod(sample("thrower" + Mods.CodeExtension, code("thrower", thrower)))) throw new Exception("The throwing test mod was refused: " + mainframe.ModStatusText);
                Mod throwing = mods.All.Find(each => each.File.Id == "thrower");
                string why2;
                if (!until(() => !throwing.On) || mods.Running != 0 || !ModGuard.Reasons().TryGetValue("thrower", out why2) || why2.IndexOf("failed", StringComparison.Ordinal) < 0)
                    throw new Exception("A mod whose hooks kept throwing was not switched off.");
                CloseTalk();
                checks.Add("PASS mod guard: a mod whose hooks keep throwing is switched off with a note and stays off");
                mods.Remove(throwing);

                if (!mainframe.ImportMod(sample("guarded" + Mods.CodeExtension, code("guarded", body)))) throw new Exception("The guard's test mod was refused again.");
                mods.StopCode();
                string offWas = File.ReadAllText(Path.Combine(mods.Folder, "off.txt"));
                ModGuard.NoneThisRun = true;
                string none = mods.GuardAtStart();
                bool started = false;
                mods.StartCode(delegate(Action act) { act(); started = true; });
                if (none == null || mods.Running != 0 || started || mods.All.Exists(each => each.On) || File.ReadAllText(Path.Combine(mods.Folder, "off.txt")) != offWas)
                    throw new Exception("--no-mods started a mod or saved them off.");
                checks.Add("PASS mod guard: --no-mods starts no mod and leaves what's saved as it was");
                ModGuard.NoneThisRun = false;

                // Stage 2: what was approved is kept beside approved.txt, and the question for a changed copy says what changed.
                string first = "// capri-mod: 1\n// id: changing\n// name: changing\n// version: 1.2.0\n// author: Smoke\n// description: A test.\n// permissions: chat\n"
                    + "using System; using CapriDesktop;\npublic class M : ICapriMod { public void Start(ICapriHost h) { } public bool Heard(string w) { return false; } public void Stop() { } }";
                if (!mainframe.ImportMod(sample("changing" + Mods.CodeExtension, first))) throw new Exception("The changing test mod was refused: " + mainframe.ModStatusText);
                string second = first.Replace("1.2.0", "1.3.0").Replace("// permissions: chat", "// permissions: chat, memory")
                    .Replace("public void Stop() { }", "public void Stop() { } void Go() { new System.Net.WebClient().DownloadString(\"https://api.example-weather.com/v1/now\"); }");
                ModFile changed = Mods.ReadCode(second);
                string whatChanged = Mods.ChangedSince(changed), consent = Mods.Consent(changed, "abc");
                if (whatChanged.IndexOf("Changed since you approved version 1.2.0", StringComparison.Ordinal) < 0 || whatChanged.IndexOf("new permission: memory", StringComparison.Ordinal) < 0
                    || whatChanged.IndexOf("new: contacts api.example-weather.com", StringComparison.Ordinal) < 0 || consent.IndexOf(whatChanged, StringComparison.Ordinal) < 0
                    || Mods.ChangedSince(Mods.ReadCode(first.Replace("id: changing", "id: neverseen"))).Length != 0)
                    throw new Exception("The question for a changed mod didn't say what changed: " + whatChanged);
                mods.Remove(mods.All.Find(each => each.File.Id == "changing"));
                checks.Add("PASS stage 2: what a mod was when approved is kept beside approved.txt, and the question for a changed copy names the version it was approved at, each new permission, each new or gone thing her scan sees, and that the fingerprint changed");

                // Stage 1: rows grouped by how serious, counted on the question's face, behind a handle that is open when anything is High.
                string[,] table = { { "chat", "H" }, { "transcripts", "H" }, { "screen", "H" }, { "voice", "H" }, { "tools", "H" }, { "hotkeys", "H" }, { "memory", "M" }, { "brain", "M" }, { "personality", "M" },
                    { "settings", "M" }, { "system", "M" }, { "mood", "M" }, { "slots", "M" }, { "skins", "L" }, { "music", "L" }, { "starts programs", "H" }, { "reads the keyboard", "H" }, { "calls Windows directly", "H" }, { "unlocks saved secrets", "H" }, { "reads plug-in sign-ins", "H" }, { Mods.ChangesSecurity, "H" },
                    { "calls code by its name", "M" }, { Mods.WritesFiles, "M" }, { "reaches outside her folder", "M" }, { "uses the network", "M" }, { Mods.Pieces, "M" }, { "changes what her brain is told", "L" } };
                for (int i = 0; i < table.GetLength(0); i++)
                    if (Mods.GroupOf(table[i, 0]) != table[i, 1][0]) throw new Exception("\"" + table[i, 0] + "\" is in group " + Mods.GroupOf(table[i, 0]) + ", not " + table[i, 1]);
                if (Mods.PermissionWords.GetLength(0) != 15) throw new Exception("A permission was added or taken away without a group in this check.");
                Func<ModFile, string> counted = delegate(ModFile mod)
                {
                    string made = Mods.Grouped(mod);
                    Dictionary<char, int> rows = new Dictionary<char, int>();
                    Marked.Line face = null;
                    foreach (Marked.Line line in Marked.Parse(made))
                    {
                        if (line.Kind == 'c') face = line;
                        if ("HML".IndexOf(line.Kind) >= 0) { int had; rows[line.Kind] = rows.TryGetValue(line.Kind, out had) ? had + 1 : 1; }
                    }
                    string want = "", got = "";
                    foreach (char group in "HML") { int n; if (rows.TryGetValue(group, out n)) want += group + " " + n + (group == 'H' ? " high" : group == 'M' ? " medium" : " low") + ";"; }
                    if (face != null) foreach (KeyValuePair<char, string> count in face.Counts) got += count.Key + " " + count.Value + ";";
                    if (want != got || Marked.Plain(made).IndexOf("[[", StringComparison.Ordinal) >= 0) throw new Exception("Her face counts don't match her rows (" + got + " for " + want + "): " + made);
                    return made;
                };
                string grouped = counted(changed);
                int network = 0;
                for (int at = grouped.IndexOf("uses the network", StringComparison.Ordinal); at >= 0; at = grouped.IndexOf("uses the network", at + 1, StringComparison.Ordinal)) network++;
                if (grouped.IndexOf("[[f+]]", StringComparison.Ordinal) < 0 || grouped.IndexOf("[[l]]", StringComparison.Ordinal) >= 0 || network != 1
                    || grouped.IndexOf("[[M]]uses the network:\n    contacts api.example-weather.com\n        https://api.example-weather.com/v1/now", StringComparison.Ordinal) < 0 || grouped.IndexOf("(0)", StringComparison.Ordinal) >= 0)
                    throw new Exception("A mod asking for chat wasn't open with its network once and its addresses under it: " + grouped);
                string quiet = counted(new ModFile { Name = "Quiet Skin", Permissions = new[] { "skins" } });
                if (quiet.IndexOf("[[f-]]", StringComparison.Ordinal) < 0 || quiet.IndexOf("[[h]]", StringComparison.Ordinal) >= 0 || quiet.IndexOf("What her scan could see in Quiet Skin's code (0): none of the things on her list.", StringComparison.Ordinal) < 0)
                    throw new Exception("A mod with only a Low row wasn't shut, or didn't say her scan saw nothing: " + quiet);
                string empty = counted(new ModFile { Name = "Empty" });
                if (empty.IndexOf("[[", StringComparison.Ordinal) >= 0 || empty.IndexOf("It asks for none of Capri's permissions.", StringComparison.Ordinal) < 0 || empty.IndexOf("Empty's code (0)", StringComparison.Ordinal) < 0)
                    throw new Exception("A mod with nothing to show didn't say so in plain words: " + empty);
                CheckTrust(checks, sample);
                checks.Add("PASS stage 1: every permission and scan row is in the group the preview table gives it; the counts on the question's face match the rows behind its handle, which is open when anything is High and shut otherwise; a group with nothing in it is left out; the network is one row with its sites and addresses under it; a scan that saw nothing says (0) and that it isn't proof; and the plain copy carries no markup");
            }
            finally
            {
                ModGuard.ClosedBadly = badlyWas;
                ModGuard.NoneThisRun = noneWas;
                try { File.Delete(guard); File.Delete(why); } catch (IOException) { }
            }
        }

        // The directory's mods as a copy beside her repo has them, each imported and turned on in a test folder of its own.
        // One that goes online is never run here; one that starts programs or reaches outside her folder runs on the test box only.
        private void SmokeDirectoryMods(List<string> checks, string output)
        {
            string found = null;
            for (DirectoryInfo up = new DirectoryInfo(Application.StartupPath); up != null && found == null; up = up.Parent)
            {
                string survey = Path.Combine(up.FullName, "agents", "mods-survey");
                if (!Directory.Exists(survey)) continue;
                foreach (string copy in Directory.GetDirectories(survey))
                    if (Directory.Exists(Path.Combine(copy, "mods"))) found = Path.Combine(copy, "mods");
            }
            if (found == null) { checks.Add("SKIP directory mods: no copy of the mods directory in agents/mods-survey on this PC"); return; }
            string[] online = { "uses the network", "contacts " }, machine = { "starts programs", "reaches outside her folder", "makes itself start with Windows", "unlocks saved secrets" };
            Mods was = ModList;
            Func<string, bool> heardWas = Hands.AlsoHeard;
            Func<string> calledWas = Hands.AlsoCalled;
            string keptWas = ModDirectory.KeptFolder;
            StringBuilder rows = new StringBuilder("mod\tkind\tresult\treason\n");
            int works = 0, breaks = 0, held = 0;
            string table = Path.Combine(output, "directory-mods.tsv");
            if (!File.Exists(table)) File.WriteAllText(table, rows.ToString(), new UTF8Encoding(false));
            // Each mod is a section of its own, so its time is its own and one that hangs is passed over when the run picks up.
            foreach (string folder in Directory.GetDirectories(found))
                foreach (string file in Directory.GetFiles(folder))
                {
                    string kind = Path.GetExtension(file).ToLowerInvariant();
                    if (kind != Mods.CodeExtension && kind != Mods.SkinExtension && kind != Mods.SkillExtension && kind != Mods.Extension) continue;
                    string name = Path.GetFileName(folder);
                    Section(checks, "DirectoryMod-" + name, delegate
                    {
                        string result = "works", reason = "", logged = "";
                        Mods own = new Mods(Path.Combine(output, "directory-mods", name));
                        own.HostFor = was.HostFor; own.HeardSpeech = was.HeardSpeech; own.SkinChanged = was.SkinChanged; own.PluginsChanged = was.PluginsChanged;
                        long logAt = File.Exists(Brain.LogPath) ? new FileInfo(Brain.LogPath).Length : 0;
                        ModList = own;
                        try
                        {
                            string limit = null;
                            Mod made = own.Import(file, delegate(ModFile read, string hash)
                            {
                                string reaches = string.Join("; ", read.Reaches ?? new string[0]);
                                foreach (string one in online) if (reaches.IndexOf(one, StringComparison.Ordinal) >= 0) limit = "goes online, so it never runs in a check";
                                if (limit == null && !Mainframe.RealInputOn)
                                    foreach (string one in machine) if (reaches.IndexOf(one, StringComparison.Ordinal) >= 0) limit = one + ", so it runs on the test box only (" + Mainframe.RealInputVariable + "=1)";
                                return limit == null;
                            });
                            System.Diagnostics.Stopwatch settle = System.Diagnostics.Stopwatch.StartNew();
                            while (settle.ElapsedMilliseconds < 1000) { Application.DoEvents(); Thread.Sleep(15); }
                            logged = LogSince(logAt, name);
                            if (limit != null) { result = "limit"; reason = limit; }
                            else if (made == null || !made.On) { result = "breaks"; reason = "imported but not on"; }
                            else if (logged.Length > 0 && Regex.IsMatch(logged, @"couldn't|could not|failed|exception|error|not found|missing|null", RegexOptions.IgnoreCase)) { result = "breaks"; reason = "its log: " + logged; }
                            else reason = logged.Length > 0 ? "on; its log: " + logged : "on, nothing in the log";
                        }
                        catch (ModException error) { result = "breaks"; reason = error.Message; }
                        finally
                        {
                            foreach (Mod left in new List<Mod>(own.All))
                                try { own.Remove(left); } catch (ModException error) { reason += " (removing it: " + error.Message + ")"; }
                            ModList = was;
                            Hands.AlsoHeard = heardWas;
                            Hands.AlsoCalled = calledWas;
                            ModDirectory.KeptFolder = keptWas;
                        }
                        if (result == "works") works++;
                        else if (result == "limit") held++;
                        else breaks++;
                        if (result == "breaks") checks.Add("FAIL directory mod " + name + ": " + reason);
                        File.AppendAllText(table, name + "\t" + kind.Substring(7) + "\t" + result + "\t" + reason.Replace('\t', ' ').Replace('\n', ' ') + "\n", new UTF8Encoding(false));
                    });
                }
            was.Load();
            if (breaks == 0) checks.Add("PASS directory mods: " + works + " imported and turned on each in its own test folder with nothing wrong in the log (directory-mods.tsv)");
            if (held > 0) checks.Add("LIMIT directory mods: " + held + " not run here, one going online never and one reaching past her folder on the test box only (directory-mods.tsv)");
        }

        // What her log gained since a point, for one mod: its own lines, joined.
        private static string LogSince(long from, string id)
        {
            try
            {
                if (!File.Exists(Brain.LogPath)) return "";
                using (FileStream read = new FileStream(Brain.LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    read.Seek(Math.Min(from, read.Length), SeekOrigin.Begin);
                    List<string> lines = new List<string>();
                    using (StreamReader text = new StreamReader(read))
                        for (string line = text.ReadLine(); line != null; line = text.ReadLine())
                            if (line.IndexOf("mod " + id, StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("mods", StringComparison.Ordinal) >= 0) lines.Add(line.Trim());
                    return string.Join(" | ", lines.ToArray());
                }
            }
            catch (IOException) { return ""; }
        }
    }
#endif
}
