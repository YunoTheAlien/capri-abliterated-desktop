using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Forms;

namespace CapriDesktop
{
    // "say <words>" says exactly their words, and "read this aloud" reads the text in a window: what is selected, else the
    // file the window has open from the top, else the line the caret is on, or all of it when they ask for all of it.
    internal static class Aloud
    {
        // What "read ... aloud" asked for: the selection (or else the caret's line), the line itself, or the whole text, and
        // the window named, if one was.
        internal sealed class Ask
        {
            internal bool Whole, Line;
            internal string App;
            // Only what is selected: no caret line, no whole text.
            internal bool Picked;
        }

        private const RegexOptions Loose = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        private static readonly Regex Wake = new Regex(@"^\s*(?:(?:hey|hi|ok(?:ay)?|yo)\s+)?capri\s*[,:;!.]*\s*", Loose);

        // "say <words>" in the languages her routing knows, the words in group "w". Each pattern is anchored at both ends.
        private static readonly Regex[] Says =
        {
            new Regex(@"^(?:(?:can|could|would|will)\s+you\s+(?:please\s+)?|please\s+|now\s+|just\s+)?(?:say|repeat\s+after\s+me|say\s+after\s+me)\s*[:,]?\s+(?<w>.+)$", Loose),
            new Regex(@"^(?:(?:puedes|podr[ií]as)\s+)?(?:d[ií]|decir|repite\s+(?:despu[eé]s\s+de\s+m[ií]|conmigo))\s*[:,]?\s+(?<w>.+)$", Loose),
            new Regex(@"^(?:(?:peux-tu|pourrais-tu|tu\s+peux)\s+)?(?:dis|dire|r[eé]p[eè]te\s+apr[eè]s\s+moi)\s*[:,]?\s+(?<w>.+)$", Loose),
            new Regex(@"^(?:kannst\s+du\s+)?(?:sag|sage|sprich\s+mir\s+nach)\s*[:,]?\s+(?<w>.+?)$", Loose),
            new Regex(@"^(?:(?:pode|podes|voc[eê]\s+pode)\s+)?(?:diga|diz|dizer|fala|repita\s+depois\s+de\s+mim)\s*[:,]?\s+(?<w>.+)$", Loose),
            new Regex(@"^(?:puoi\s+)?(?:di'|d[iì]|dire|ripeti\s+dopo\s+di\s+me)\s*[:,]?\s+(?<w>.+)$", Loose),
            new Regex(@"^(?:mo[zż]esz\s+)?(?:powiedz|powt[oó]rz\s+za\s+mn[aą])\s*[:,]?\s+(?<w>.+)$", Loose),
            new Regex(@"^(?:kun\s+je\s+)?(?:zeg|zeg\s+me\s+na)\s*[:,]?\s+(?<w>.+)$", Loose),
            new Regex(@"^(?:можешь\s+|можеш\s+)?(?:скажи|повтори\s+за\s+мной|повтори\s+за\s+мною)\s*[:,]?\s+(?<w>.+)$", Loose),
            new Regex(@"^(?:[şs]unu\s+s[oö]yle|benden\s+sonra\s+tekrarla)\s*[:,]?\s+(?<w>.+)$", Loose),
            new Regex(@"^(?:(?:bisakah\s+kamu|tolong)\s+)?(?:katakan|bilang|ucapkan|ulangi\s+setelah\s+aku)\s*[:,]?\s+(?<w>.+)$", Loose),
            new Regex(@"^[「『](?<w>.+)[」』]\s*(?:って|と)\s*(?:言って|いって)(?:ください|くれる|くれない)?[？?。!！]*$", Loose),
            new Regex(@"^(?:请)?(?:跟我说|跟我念|跟我读|说)\s*[:：]\s*(?<w>.+)$", Loose),
            new Regex(@"^[""“](?<w>.+)[""”]\s*(?:이?라고)\s*말해(?:줘|봐|주세요)?[?？.!]*$", Loose),
        };

        // Not theirs to have said word for word: a message for someone else, her last line again, or something she is to
        // think up herself.
        private static readonly Regex NotWords = new Regex(
            @"^(?:that|it|this)(?:\s+again)?[\s.!?]*$|^(?:that|it|this|the\s+last\s+(?:one|line|thing))\s+again\b|^(?:something|anything|sth|some\s+words|a\s+few\s+words|more|less|nothing|your\s+name|what\b|why\b|how\b|who\b|whatever|algo|quelque\s+chose|etwas|qualcosa|alguma\s+coisa|co[sś]|iets|что-нибудь|что-то|щось|bir\s+[sş]ey|sesuatu)(?:\s|$|[.!?])"
            + @"|^(?:hi|hello|hey|bye|goodbye|good\s?night|good\s+morning|gn|gm|sorry|thanks|thank\s+you|congrats|congratulations|happy\s+birthday|hola|salut|bonjour|hallo|cze[sś][cć]|привет)\s+(?:to|for|a|à|zu|an|para|per|dla|tegen|для)\s+"
            + @"|\b(?:on|in|into|to|over)\s+(?:the\s+|my\s+|our\s+)?(?:discord|chat|call|server|channel|dms?|twitch|stream|whatsapp|telegram|messenger|slack|teams|vc|voice\s+chat|lobby|game\s+chat|team\s+chat)\b"
            + @"|^(?:the\s+(?:selected|highlighted)\s+text|the\s+(?:current\s+)?line|the\s+selection|all\s+of\s+it|the\s+whole\s+(?:thing|text|document|page|file)|everything\s+(?:in|on)\b)",
            Loose);

        // The words of a "say" they meant word for word, or null. Quoted words are always theirs.
        internal static string Words(string said)
        {
            string text = Wake.Replace((said ?? "").Trim(), "").Trim();
            foreach (Regex say in Says)
            {
                Match found = say.Match(text);
                if (!found.Success) continue;
                string words = found.Groups["w"].Value.Trim();
                Match quoted = Regex.Match(words, @"^[""“”'‘](.+)[""“”'’][.!?]?$");
                if (quoted.Success) return quoted.Groups[1].Value.Trim();
                // Unquoted, a polite ending is theirs to the request, not part of the words.
                words = Regex.Replace(words, @"[\s,]+(?:please|for\s+me|por\s+favor|s'il\s+te\s+pla[iî]t|bitte|per\s+favore|prosz[eę]|пожалуйста|tolong)[\s.!?]*$", "", Loose).Trim();
                if (words.Length == 0 || NotWords.IsMatch(words)) return null;
                return words;
            }
            return null;
        }

        private const string SelectionWords = @"this|that|it|the\s+(?:selected|highlighted)\s+(?:text|bit|part|words)|the\s+selection|what(?:'s|\s+is)\s+(?:selected|highlighted)|what\s+i\s+(?:selected|highlighted)|the\s+text|esto|eso|lo\s+seleccionado|ça|ceci|cela|la\s+s[eé]lection|das|dies|die\s+auswahl|isso|isto|a\s+sele[cç][aã]o|questo|quello|la\s+selezione|to|zaznaczenie|dit|dat|de\s+selectie|это|выделенное|це|виділене|bunu|se[cç]ili\s+metni|ini|itu";
        private const string LineWords = @"(?:the|this|that)\s+(?:current\s+)?line|la\s+l[ií]nea|la\s+ligne|die\s+zeile|a\s+linha|la\s+riga|t[eę]\s+lini[eę]|lini[eę]|de\s+regel|строку|рядок|sat[ıi]r[ıi]|baris\s+ini";
        private const string WholeWords = @"all\s+of\s+it|everything|all\s+the\s+text|the\s+whole\s+(?:thing|text|document|page|file|note)|the\s+(?:entire\s+)?document|todo|tout|alles|tudo|tutto|wszystko|alles|всё|все|hepsini|semuanya";
        private const string AloudWords = @"(?:out\s+)?(?:aloud|out\s+loud)|en\s+voz\s+alta|[aà]\s+voix\s+haute|em\s+voz\s+alta|ad\s+alta\s+voce|na\s+g[lł]os|вслух|вголос|sesli";

        private static readonly Regex[] Reads =
        {
            new Regex(@"^(?:(?:can|could|would|will)\s+you\s+)?(?:please\s+)?(?<verb>read(?:\s+out)?|say|speak)\s+(?:me\s+|out\s+)?(?<obj>" + LineWords + "|" + WholeWords + "|" + SelectionWords + @")(?<aloud>\s+(?:" + AloudWords + @"))?(?:\s+(?:in|from|on)\s+(?:the\s+|my\s+)?(?<app>[^?.!]+?))?(?<aloud2>\s+(?:" + AloudWords + @"))?(?:\s+for\s+me)?(?:\s+please)?[\s?.!]*$", Loose),
            new Regex(@"^(?:(?:puedes|peux-tu|kannst\s+du|pode|puoi|mo[zż]esz|kun\s+je|можешь|можеш)\s+)?(?<verb>l[eé]e(?:me)?|leer|lis|lire|lies|lese|leia|l[eê]|leggi|przeczytaj|lees|прочитай|прочти)\s+(?<obj>" + LineWords + "|" + WholeWords + "|" + SelectionWords + @")(?<aloud>\s+(?:" + AloudWords + @"|vor))?(?:\s+(?:en|dans|in|no|na|em|w|в|у)\s+(?<app>[^?.!]+?))?(?<aloud2>\s+(?:" + AloudWords + @"|vor))?[\s?.!]*$", Loose),
            new Regex(@"^(?<obj>" + LineWords + "|" + WholeWords + "|" + SelectionWords + @")\s+(?<aloud>sesli)\s+(?<verb>oku)[\s?.!]*$", Loose),
            new Regex(@"^(?<verb>bacakan)\s+(?<obj>" + LineWords + "|" + WholeWords + "|" + SelectionWords + @")(?:\s+(?:di)\s+(?<app>[^?.!]+?))?(?<aloud>)[\s?.!]*$", Loose),
            new Regex(@"^(?<obj>これ|選択した(?:テキスト|部分)|この行|全部)を?(?<verb>読み上げて)(?<aloud>)(?:ください)?[？?。]*$", Loose),
            new Regex(@"^(?:把)?(?<obj>这个|这段|这行|选中的(?:文字)?|全部)(?<verb>读出来|朗读)(?<aloud>)[？?。]*$", Loose),
            new Regex(@"^(?<obj>이거|이것|이\s*줄|선택한\s*(?:거|텍스트)|전부)\s*(?<verb>읽어\s*줘|읽어\s*주세요)(?<aloud>)[?？.]*$", Loose),
        };

        // What a "read this aloud" asked for, or null for anything else. "read this" alone is left to her brain unless it
        // says aloud, names a line, all of it, the selection or a window, or is in a language where the verb is aloud.
        internal static Ask Reading(string said)
        {
            string text = Wake.Replace((said ?? "").Trim(), "").Trim();
            for (int i = 0; i < Reads.Length; i++)
            {
                Match found = Reads[i].Match(text);
                if (!found.Success) continue;
                string obj = found.Groups["obj"].Value.Trim().ToLowerInvariant();
                bool line = Regex.IsMatch(obj, "^(?:" + LineWords + "|この行|这行|이\\s*줄)$", Loose);
                bool whole = Regex.IsMatch(obj, "^(?:" + WholeWords + "|全部|전부)$", Loose);
                bool bare = Regex.IsMatch(obj, @"^(?:this|that|it|the\s+text|to|das|dies|ini|itu)$", Loose);
                bool aloud = found.Groups["aloud"].Value.Trim().Length > 0 || found.Groups["aloud2"].Success && found.Groups["aloud2"].Value.Trim().Length > 0;
                bool app = found.Groups["app"].Success && found.Groups["app"].Value.Trim().Length > 0;
                string verb = found.Groups["verb"].Value.ToLowerInvariant();
                bool own = i >= 2;
                if (i == 0 && verb == "say" && bare) continue;
                if (i <= 1 && !own && !aloud && !app && bare) continue;
                if (i <= 1 && !aloud && !app && !line && !whole && !Regex.IsMatch(obj, "selec|highlight|auswahl|zaznacz|выдел|виділ", Loose) && verb != "say") continue;
                return new Ask { Whole = whole, Line = line, App = app ? found.Groups["app"].Value.Trim() : null };
            }
            return null;
        }

        // "shut up", "stop talking", "silence", "hush", "enough": her voice stops mid-word and she says nothing back.
        // Only the whole utterance, give or take her name, "ok", "please" and punctuation, in the languages her routing
        // knows.
        private static readonly Regex Hush = new Regex(
            @"^(?:(?:ok(?:ay)?|oh|hey|just|please|pls|god|jeez|omg|seriously|dude|capri)[\s,.!-]+)*"
            + @"(?:shut\s*up|stop\s+(?:talking|speaking|it|that|now)|stop|silence|hush|sh+|shush|quiet|be\s+quiet|zip\s+it|enough|that'?s\s+enough|stop\s+stop|pipe\s+down|cut\s+it\s+out"
            + @"|c[aá]llate|calla|silencio|basta|para\s+de\s+hablar|tais[\s-]toi|chut|[cç]a\s+suffit|arr[eê]te\s+de\s+parler|halt\s+die\s+klappe|sei\s+still|ruhe|still|genug|h[öo]r\s+auf\s+zu\s+reden"
            + @"|cala\s+a\s+boca|cala[\s-]te|sil[eê]ncio|chega|para\s+de\s+falar|stai\s+zitt[oa]|zitt[oa]|silenzio|smettila\s+di\s+parlare|zamknij\s+si[eę]|cicho|cisza|do[sś][cć]|przesta[nń]\s+m[oó]wi[cć]"
            + @"|hou\s+je\s+mond|stil|stilte|genoeg|stop\s+met\s+praten|заткнись|замолчи|молчи|тихо|тишина|хватит|замовкни|тиша|досить|годі|sus|sessiz\s+ol|yeter|diam|diamlah|cukup|berhenti\s+bicara"
            + @"|黙って|静かに|うるさい|もういい|闭嘴|安静|别说了|够了|닥쳐|조용히\s*해|그만)"
            + @"(?:[\s,.!-]+(?:please|pls|capri|now|already|right\s+now|for\s+real))*[\s.!?。！？]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // A bare "stop", with only her name, "ok" or "please" round it. With a question of hers waiting it answers the question
        // ("say stop to stop"), and only hushes her while she is saying something.
        private static readonly Regex Stop = new Regex(
            @"^(?:(?:ok(?:ay)?|oh|hey|just|please|pls|capri)[\s,.!-]+)*stop(?:[\s,.!-]+(?:please|pls|capri|now|already|right\s+now))*[\s.!?]*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal static bool BareStop(string said) { return Stop.IsMatch((said ?? "").Trim()); }

        // "carry on", "keep reading", "continue reading": back to where reading a file stopped. The whole of what they said.
        private static readonly Regex Resume = new Regex(
            @"^(?:(?:hey|hi|ok(?:ay)?)\s+)?(?:capri\s*[,:;!.]*\s*)?(?:ok(?:ay)?\s*,?\s*)?(?:please\s+)?(?:carry\s+on(?:\s+reading)?|keep\s+(?:on\s+)?reading|continue\s+reading|go\s+on\s+reading|resume\s+reading|read\s+on|sigue\s+leyendo|continue\s+(?:à|a)\s+lire|lies\s+weiter|continua\s+(?:a\s+)?(?:ler|leggere)|czytaj\s+dalej|lees\s+verder|читай\s+дальше|продолжай\s+читать|okumaya\s+devam\s+et|lanjut(?:kan)?\s+baca|続きを読んで|继续读|계속\s*읽어\s*줘)(?:\s+please)?[\s?.!。？]*$", Loose);

        internal static bool CarryOn(string said) { return Resume.IsMatch((said ?? "").Trim()); }

        // ---------- The file behind a window ----------

        // The kinds she reads aloud whole from Files.Open: text, Word, Excel and PDFs (Windows' own renderer, and OCR for
        // scanned pages). Pictures, web pages and code stay with what the window shows.
        private static readonly string[] Readable = { ".txt", ".md", ".markdown", ".log", ".csv", ".tsv", ".json", ".xml", ".ini", ".yaml", ".yml", ".pdf", ".docx", ".xlsx" };
        private static readonly string[] Browsers = { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "iexplore", "arc" };

        // Tests swap it, so no check reads a real window's file or their Recent folder.
        internal static Func<IntPtr, string> FileOf = FileBehind;

        // The file a window has open, or null: a browser's address bar when it shows a file on this PC (file:///), else the
        // name in the window's title found as written or in their Recent folder. A page from the web has none.
        private static string FileBehind(IntPtr window)
        {
            if (window == IntPtr.Zero) return null;
            string program = "";
            try { using (Process running = Process.GetProcessById(Owner(window))) program = running.ProcessName.ToLowerInvariant(); }
            catch (Exception) { }
            if (Array.IndexOf(Browsers, program) >= 0)
            {
                string address = Address(window);
                if (address == null) return null;
                return Kept(LocalFile(address));
            }
            return Kept(Titled(Title(window), Environment.GetFolderPath(Environment.SpecialFolder.Recent)));
        }

        private static string Kept(string path)
        {
            if (path == null || !File.Exists(path)) return null;
            return Array.IndexOf(Readable, Path.GetExtension(path).ToLowerInvariant()) >= 0 ? path : null;
        }

        // A browser's address bar: the first one-line text box near its top that holds an address.
        private static string Address(IntPtr window)
        {
            try
            {
                AutomationElement root = AutomationElement.FromHandle(window);
                Condition box = new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit), new PropertyCondition(AutomationElement.IsValuePatternAvailableProperty, true));
                AutomationElementCollection boxes = root.FindAll(TreeScope.Descendants, box);
                for (int i = 0; i < boxes.Count && i < 6; i++)
                {
                    string value = ((ValuePattern)boxes[i].GetCurrentPattern(ValuePattern.Pattern)).Current.Value ?? "";
                    if (value.IndexOf('\n') < 0 && value.Length < 2048 && (Regex.IsMatch(value, @"^(?:file|https?)://", Loose) || Regex.IsMatch(value, @"^[A-Za-z]:[\\/]") || Regex.IsMatch(value, @"^[\w-]+(?:\.[\w-]+)+(?:/|$)"))) return value.Trim();
                }
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            return null;
        }

        // An address as a path on this PC: file:///C:/a%20b.pdf, or C:/a b.pdf as some browsers show it. Null for the web.
        internal static string LocalFile(string address)
        {
            string value = (address ?? "").Trim();
            if (value.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                Uri uri;
                return Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.IsFile ? uri.LocalPath : null;
            }
            if (Regex.IsMatch(value, @"^[A-Za-z]:[\\/]")) return Uri.UnescapeDataString(value).Replace('/', '\\');
            return null;
        }

        // The file a title names: "notes.txt - Notepad", "*notes.txt - Notepad", "Report - Word". As written when it is a
        // whole path, else its shortcut in their Recent folder (Windows keeps one for each file they open), newest first.
        internal static string Titled(string title, string recent)
        {
            string name = Regex.Replace((title ?? "").Trim(), @"^[*•●]\s*", "");
            int dash = name.LastIndexOf(" - ", StringComparison.Ordinal);
            if (dash > 0) name = name.Substring(0, dash).Trim();
            name = Regex.Replace(name, @"\s*[\[(](?:read-only|compatibility mode|protected view|lecture seule|nur lesen)[\])]\s*$", "", Loose).Trim();
            if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return null;
            if (Path.IsPathRooted(name)) return File.Exists(name) ? name : null;
            if (string.IsNullOrEmpty(recent) || !Directory.Exists(recent) || name.IndexOfAny(new[] { '*', '?', '\\', '/', ':' }) >= 0) return null;
            List<FileInfo> links = new List<FileInfo>();
            try
            {
                foreach (string pattern in new[] { name + ".lnk", name + ".*.lnk" })
                    foreach (string link in Directory.GetFiles(recent, pattern)) links.Add(new FileInfo(link));
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); return null; }
            links.Sort(delegate(FileInfo one, FileInfo other) { return other.LastWriteTimeUtc.CompareTo(one.LastWriteTimeUtc); });
            foreach (FileInfo link in links)
            {
                string target = Target(link.FullName);
                if (target != null && File.Exists(target)) return target;
            }
            return null;
        }

        // Where a shortcut points, through Windows' own Shell object; nothing added to her.
        private static string Target(string link)
        {
            object shell = null, shortcut = null;
            try
            {
                Type type = Type.GetTypeFromProgID("WScript.Shell");
                if (type == null) return null;
                shell = Activator.CreateInstance(type);
                shortcut = type.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { link });
                string target = shortcut.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, shortcut, null) as string;
                return string.IsNullOrEmpty(target) ? null : target;
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); return null; }
            finally
            {
                if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.ReleaseComObject(shortcut);
                if (shell != null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
            }
        }

        // Where reading a file has got to: the part of it Files.Open gives (a PDF's page, an offset into the text) and the
        // part of that being read.
        internal sealed class Place
        {
            internal string Path;
            internal int Page = 1;
            internal long Offset;
            internal int Part;
            internal Place Copy() { return (Place)MemberwiseClone(); }
        }

        // One piece of a file as text to read, with where the next piece starts; null and why when it can't be read.
        internal static string Piece(Place at, out Place next, out string problem)
        {
            next = null;
            problem = null;
            bool pdf = string.Equals(System.IO.Path.GetExtension(at.Path), ".pdf", StringComparison.OrdinalIgnoreCase);
            ReadFile read = Files.Open(at.Path, null, pdf ? (int?)at.Page : null, at.Offset);
            if (!read.Readable || read.Text == null) { problem = read.Line ?? "I couldn't read " + System.IO.Path.GetFileName(at.Path) + "."; return null; }
            if (read.NextOffset.HasValue) next = new Place { Path = at.Path, Page = at.Page, Offset = read.NextOffset.Value };
            else if (read.NextPage.HasValue) next = new Place { Path = at.Path, Page = read.NextPage.Value };
            return Clean(Regex.Replace(read.Text, @"(?m)^Page \d+:[ \t]*\r?$", ""));
        }

        internal static bool Hushed(string said)
        {
            string text = (said ?? "").Trim();
            if (text.Length == 0 || text.IndexOf('"') >= 0 || text.IndexOf('\u201c') >= 0) return false;
            return Hush.IsMatch(text);
        }

        // Read aloud in parts of about this many characters, each ending on a sentence where it can.
        internal const int PartLength = 280;

        internal static List<string> Parts(string text)
        {
            List<string> parts = new List<string>();
            StringBuilder part = new StringBuilder();
            foreach (string sentence in Regex.Split(Regex.Replace(text ?? "", @"\s+", " ").Trim(), @"(?<=[.!?…。！？])\s+"))
            {
                string rest = sentence;
                while (rest.Length > PartLength)
                {
                    int cut = rest.LastIndexOfAny(new[] { ',', ';', ' ' }, PartLength);
                    if (cut < PartLength / 2) cut = PartLength;
                    if (part.Length > 0) { parts.Add(part.ToString().Trim()); part.Clear(); }
                    parts.Add(rest.Substring(0, cut + (cut < rest.Length && rest[cut] != ' ' ? 1 : 0)).Trim());
                    rest = rest.Substring(Math.Min(rest.Length, cut + 1)).Trim();
                }
                if (part.Length > 0 && part.Length + rest.Length + 1 > PartLength) { parts.Add(part.ToString().Trim()); part.Clear(); }
                if (rest.Length > 0) part.Append(part.Length > 0 ? " " : "").Append(rest);
            }
            if (part.ToString().Trim().Length > 0) parts.Add(part.ToString().Trim());
            return parts;
        }

        // ---------- Reading a window ----------

        [DllImport("user32.dll")] private static extern IntPtr GetTopWindow(IntPtr parent);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int most);
        [DllImport("user32.dll")] private static extern int GetWindowThreadProcessId(IntPtr window, out int process);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);

        // A name they say for an app, and the program it is.
        private static readonly Dictionary<string, string> Programs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "word", "winword" }, { "ms word", "winword" }, { "microsoft word", "winword" }, { "notepad plus plus", "notepad++" },
            { "vs code", "code" }, { "vscode", "code" }, { "visual studio code", "code" }, { "edge", "msedge" }, { "libreoffice", "soffice" },
            { "the browser", "" }, { "my browser", "" }
        };

        // Tests swap these, so no check reads a real window of the PC it runs on or its clipboard.
        internal static Func<IntPtr> FrontWindow = Front;
        internal static Func<string, IntPtr> NamedWindow = Named;

        // The window in front that isn't hers: the top visible, titled, uncloaked window of another program.
        private static IntPtr Front()
        {
            int own = Process.GetCurrentProcess().Id;
            for (IntPtr window = GetTopWindow(IntPtr.Zero); window != IntPtr.Zero; window = GetWindow(window, 2))
                if (Ordinary(window) && Owner(window) != own) return window;
            return IntPtr.Zero;
        }

        // The front-most window whose program or title has that name.
        private static IntPtr Named(string name)
        {
            string want = Regex.Replace((name ?? "").Trim().ToLowerInvariant(), @"^(?:the|my)\s+|\s+(?:window|app)$", "");
            if (want.Length == 0) return Front();
            string program;
            if (Programs.TryGetValue(want, out program) && program.Length == 0) return Front();
            int own = Process.GetCurrentProcess().Id;
            for (IntPtr window = GetTopWindow(IntPtr.Zero); window != IntPtr.Zero; window = GetWindow(window, 2))
            {
                if (!Ordinary(window)) continue;
                int process = Owner(window);
                string programName = "";
                try { using (Process running = Process.GetProcessById(process)) programName = running.ProcessName.ToLowerInvariant(); }
                catch (Exception) { }
                if (programName == (program ?? want).ToLowerInvariant() || programName.Contains(want.Replace(" ", "")) || Title(window).ToLowerInvariant().Contains(want)) return window;
            }
            return IntPtr.Zero;
        }

        private static int Owner(IntPtr window) { int process; GetWindowThreadProcessId(window, out process); return process; }

        internal static string Title(IntPtr window)
        {
            StringBuilder title = new StringBuilder(512);
            GetWindowText(window, title, title.Capacity);
            return title.ToString();
        }

        private static bool Ordinary(IntPtr window)
        {
            if (!IsWindowVisible(window) || Title(window).Trim().Length == 0) return false;
            if ((GetWindowLong(window, -20) & 0x80) != 0 || GetWindow(window, 4) != IntPtr.Zero) return false;
            int cloaked;
            return DwmGetWindowAttribute(window, 14, out cloaked, 4) != 0 || cloaked == 0;
        }

        // Its text through UI Automation: the selection, else the caret's line (or all when asked), from the focused
        // control, else its document or first text box; a control without a text pattern gives its value. Null when
        // there is nothing.
        internal static string Read(IntPtr window, Ask ask)
        {
            if (window == IntPtr.Zero) return null;
            try
            {
                AutomationElement root = AutomationElement.FromHandle(window);
                Condition texty = new PropertyCondition(AutomationElement.IsTextPatternAvailableProperty, true);
                AutomationElement holder = root.FindFirst(TreeScope.Subtree, new AndCondition(texty, new PropertyCondition(AutomationElement.HasKeyboardFocusProperty, true)))
                    ?? root.FindFirst(TreeScope.Subtree, new AndCondition(texty, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document)))
                    ?? root.FindFirst(TreeScope.Subtree, texty);
                if (holder != null)
                {
                    TextPattern text = (TextPattern)holder.GetCurrentPattern(TextPattern.Pattern);
                    if (ask.Whole) return Clean(text.DocumentRange.GetText(-1));
                    TextPatternRange[] picked = text.GetSelection();
                    if (!ask.Line)
                    {
                        StringBuilder selected = new StringBuilder();
                        foreach (TextPatternRange range in picked) selected.Append(range.GetText(-1)).Append(' ');
                        if (Clean(selected.ToString()) != null) return Clean(selected.ToString());
                    }
                    if (ask.Picked) return null;
                    if (picked.Length > 0)
                    {
                        TextPatternRange line = picked[0].Clone();
                        line.ExpandToEnclosingUnit(TextUnit.Line);
                        string caret = Clean(line.GetText(-1));
                        if (caret != null) return caret;
                    }
                    return null;
                }
                if (!ask.Whole || ask.Picked) return null;
                AutomationElement valued = root.FindFirst(TreeScope.Subtree, new PropertyCondition(AutomationElement.IsValuePatternAvailableProperty, true));
                return valued == null ? null : Clean(((ValuePattern)valued.GetCurrentPattern(ValuePattern.Pattern)).Current.Value);
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); return null; }
        }

        private static string Clean(string text)
        {
            string kept = Regex.Replace(text ?? "", @"[\u0000-\u0008\u000B\u000C\u000E-\u001F￼]", "").Trim();
            return kept.Length == 0 ? null : kept;
        }
    }

    // Saying their words and reading aloud, on her window's thread, a part at a time.
    internal sealed partial class Companion
    {
        private List<string> aloudParts;
        private int aloudTicket = -1, aloudBarged;
        // The file being read aloud and where in it, the piece after this one, and whether it is being fetched.
        private Aloud.Place aloudPlace, aloudNext;
        private bool aloudFetching;
        // Where each file stopped, for "carry on", and the last one stopped.
        private readonly Dictionary<string, Aloud.Place> aloudKept = new Dictionary<string, Aloud.Place>(StringComparer.OrdinalIgnoreCase);
        private string aloudLastFile;
        private System.Windows.Forms.Timer aloudTimer;
        // How many times she was told to hush, for her checks.
        private int hushes;

        // The words she last said for a "say", as she was given them to say.
        private string aloudSaid;
        // The clipboard, read last when a window has nothing to read. Tests swap it.
        internal Func<string> AloudClipboard;

        // "say <words>" and "read this aloud", before anything else hears them. True when one was taken.
        private bool AloudHeard(int ticket, string input)
        {
            // Told to hush: her voice has already stopped; her queue and any reading go, and she says nothing back. A
            // bare "stop" while she waits on an answer and is silent is that answer.
            bool answering = meeting.Asking || Probes.PendingKey != null || Security.Holding != null || (job != null && job.Asking);
            if (Aloud.Hushed(input) && !(answering && !Voicing && Aloud.BareStop(input)))
            {
                Brain.Transcribe("You", input);
                StopAloud();
                ownLines.Clear();
                voicePreview = null;
                BargeIn("told to hush");
                CloseTalk();
                hushes++;
                return true;
            }
            // "carry on": the file she was reading, from the part she was on when it stopped.
            Aloud.Place kept;
            if (Aloud.CarryOn(input) && aloudLastFile != null && aloudKept.TryGetValue(aloudLastFile, out kept))
            {
                Brain.Transcribe("You", input);
                StopAloud();
                ReadFileAloud(ticket, kept.Copy());
                return true;
            }
            string words = Aloud.Words(input);
            Aloud.Ask reading = words == null ? Aloud.Reading(input) : null;
            if (words == null && reading == null) return false;
            Brain.Transcribe("You", input);
            if (words != null)
            {
                // Her words are theirs exactly: not put into her language, not dressed. Only what may not go out on a stream
                // comes out.
                StopAloud();
                aloudSaid = TwitchSafe(words);
                Answer(ticket, aloudSaid, TalkState.Talking, false, true);
                return true;
            }
            talkState = TalkState.Thinking;
            UpdateAnimation();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string read = null, problem = null, file = null;
                try { read = AloudText(reading, out problem, out file); }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); problem = "I couldn't read that."; }
                Post(delegate
                {
                    if (ticket != asking) return;
                    if (file != null) { StopAloud(); ReadFileAloud(ticket, new Aloud.Place { Path = file }); return; }
                    if (read == null) { Answer(ticket, problem, TalkState.Talking); return; }
                    ReadAloud(ticket, read);
                });
            });
            return true;
        }

        // The text to read, the file to read from the top, or null with why. The selection first; else a file the
        // window has open, read whole; else the caret's line or the window's text; then the clipboard.
        private string AloudText(Aloud.Ask reading, out string problem) { string file; return AloudText(reading, out problem, out file); }

        private string AloudText(Aloud.Ask reading, out string problem, out string file)
        {
            problem = null;
            file = null;
            IntPtr window = reading.App != null ? Aloud.NamedWindow(reading.App) : Aloud.FrontWindow();
            if (reading.App != null && window == IntPtr.Zero) { problem = "I can't find " + reading.App + " open."; return null; }
            if (!reading.Line)
            {
                string picked = reading.Whole ? null : Aloud.Read(window, new Aloud.Ask { Picked = true });
                if (picked != null) return picked;
                file = Aloud.FileOf(window);
                if (file != null) return null;
            }
            string text = Aloud.Read(window, reading);
            if (text != null) return text;
            string clip = null;
            Func<string> clipboard = AloudClipboard;
            if (clipboard != null) clip = clipboard();
            else Invoke(new Action(delegate { try { if (Clipboard.ContainsText()) clip = Clipboard.GetText(); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); } }));
            if (!string.IsNullOrWhiteSpace(clip)) return clip.Trim();
            string named = window == IntPtr.Zero ? "the window in front" : Aloud.Title(window);
            problem = reading.Whole ? "There's no text I can read in " + named + ", and nothing on the clipboard." : "There's nothing selected in " + named + " and no line to read, and nothing on the clipboard.";
            return null;
        }

        // The first part now, the rest each once she has finished the one before. Anything they say, Esc or "stop" ends it.
        private void ReadAloud(int ticket, string text)
        {
            List<string> parts = Aloud.Parts(text);
            if (parts.Count == 0) return;
            StopAloud();
            aloudTicket = ticket;
            aloudBarged = bargeIns;
            aloudParts = parts.Count > 1 ? parts.GetRange(1, parts.Count - 1) : null;
            Answer(ticket, TwitchSafe(parts[0]), TalkState.Talking, false, true);
            if (aloudParts == null) return;
            if (aloudTimer == null)
            {
                aloudTimer = new System.Windows.Forms.Timer { Interval = 250 };
                aloudTimer.Tick += delegate { AloudNext(); };
            }
            aloudTimer.Start();
        }

        // The next part, once the one before is shown and said. False when reading has ended.
        internal bool AloudNext()
        {
            if (aloudParts == null || asking != aloudTicket || bargeIns != aloudBarged) { StopAloud(); return false; }
            if (aloudFetching || LineBusy || replyDone < 0 || clock.Elapsed.TotalSeconds - replyDone < 0.6) return true;
            if (aloudParts.Count == 0)
            {
                // This piece of the file is done: the next one, from its first part.
                Aloud.Place next = aloudNext, was = aloudPlace;
                if (next == null || was == null) { StopAloud(); return false; }
                aloudPlace = next;
                aloudNext = null;
                FetchPiece(next, delegate(List<string> parts, string problem)
                {
                    if (aloudPlace != next || aloudParts == null) return;
                    if (parts == null) { aloudKept.Remove(next.Path); aloudPlace = null; aloudParts = null; if (aloudTimer != null) aloudTimer.Stop(); return; }
                    aloudParts = parts;
                    next.Part = -1;
                });
                return true;
            }
            ReadPart(aloudTicket, false);
            return true;
        }

        // A file from where it is at: its piece read off her window's thread, then its parts in turn, each piece after the
        // one before. Its place is kept when it stops, for "carry on", and let go when it ends.
        private void ReadFileAloud(int ticket, Aloud.Place at)
        {
            aloudPlace = at;
            aloudLastFile = at.Path;
            aloudFetching = true;
            aloudTicket = ticket;
            aloudBarged = bargeIns;
            talkState = TalkState.Thinking;
            UpdateAnimation();
            FetchPiece(at, delegate(List<string> parts, string problem)
            {
                if (ticket != asking || aloudPlace != at) return;
                if (parts == null) { aloudPlace = null; Answer(ticket, problem, TalkState.Talking); return; }
                at.Part = Math.Min(at.Part, Math.Max(0, parts.Count - 1));
                aloudParts = parts.GetRange(at.Part, parts.Count - at.Part);
                ReadPart(ticket, true);
            });
        }

        private void FetchPiece(Aloud.Place at, Action<List<string>, string> done)
        {
            aloudFetching = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string text = null, problem = null;
                Aloud.Place next = null;
                try { text = Aloud.Piece(at, out next, out problem); }
                catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); problem = "I couldn't read " + Path.GetFileName(at.Path) + "."; }
                Post(delegate
                {
                    aloudFetching = false;
                    if (aloudPlace == at) aloudNext = next;
                    List<string> parts = text == null ? null : Aloud.Parts(text);
                    if (parts != null && parts.Count == 0) parts = null;
                    done(parts, problem ?? (parts == null ? "There's no text I can read in " + Path.GetFileName(at.Path) + "." : null));
                });
            });
        }

        // Says the next part: the first straight away, later ones from AloudNext.
        private void ReadPart(int ticket, bool first)
        {
            string part = aloudParts[0];
            aloudParts.RemoveAt(0);
            if (!first) aloudTicket = ++asking;
            else aloudTicket = ticket;
            if (aloudPlace != null && !first) aloudPlace.Part++;
            Answer(aloudTicket, TwitchSafe(part), TalkState.Talking, false, true);
            if (aloudParts.Count == 0 && (aloudPlace == null || aloudNext == null)) aloudParts = null;
            if (aloudTimer == null)
            {
                aloudTimer = new System.Windows.Forms.Timer { Interval = 250 };
                aloudTimer.Tick += delegate { AloudNext(); };
            }
            if (aloudParts != null) aloudTimer.Start();
            else if (aloudPlace != null) { aloudKept.Remove(aloudPlace.Path); aloudPlace = null; aloudNext = null; }
        }

        private void StopAloud()
        {
            // A file stopped part way keeps its place: the part being read when it stopped is read again on "carry on".
            if (aloudPlace != null) { aloudKept[aloudPlace.Path] = aloudPlace.Copy(); aloudLastFile = aloudPlace.Path; }
            aloudPlace = null;
            aloudNext = null;
            aloudParts = null;
            if (aloudTimer != null) aloudTimer.Stop();
        }

        internal int AloudLeft { get { return aloudParts == null ? 0 : aloudParts.Count; } }

        private void SmokeAloudFile(List<string> checks, string output)
        {
            // "carry on" and the like, in her languages; not the asks that only look like it.
            foreach (string resume in new[] { "carry on", "Capri, carry on reading", "keep reading", "continue reading please", "read on", "sigue leyendo", "continue à lire", "lies weiter", "czytaj dalej", "продолжай читать", "続きを読んで", "继续读" })
                if (!Aloud.CarryOn(resume)) throw new Exception("\"" + resume + "\" didn't carry on reading.");
            foreach (string notResume in new[] { "carry on playing", "keep playing", "continue", "read this aloud", "carry on with the job" })
                if (Aloud.CarryOn(notResume)) throw new Exception("\"" + notResume + "\" was taken as carry on reading.");
            // Addresses and titles to the file behind them.
            string folder = Path.Combine(output, "aloud-file-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(folder);
            string spaced = Path.Combine(folder, "my notes.txt");
            File.WriteAllText(spaced, "x");
            string fromUri = Aloud.LocalFile(new Uri(spaced).AbsoluteUri), fromBare = Aloud.LocalFile(spaced.Replace('\\', '/'));
            if (fromUri != spaced || fromBare != spaced || Aloud.LocalFile("https://example.com/a.pdf") != null)
                throw new Exception("An address wasn't read as the file on this PC: " + fromUri + " / " + fromBare);
            if (Aloud.Titled("*" + spaced + " - Notepad", null) != spaced || Aloud.Titled("my notes.txt - Notepad", Path.Combine(folder, "no recent")) != null)
                throw new Exception("A title wasn't read as its file, or a bare name was found with no Recent folder");
            checks.Add("PASS \"carry on\" and \"keep reading\" in her languages resume reading, \"carry on playing\" does not; a browser's file:/// or C:/ address and a title with a whole path are the file on this PC, a web address is not");

            // A file read whole in pieces, stopped part way and carried on from that part.
            StringBuilder text = new StringBuilder();
            // Longer than one piece Files.Open gives (Files.Excerpt), so it is read in two.
            for (int i = 0; i < 800; i++) text.Append("Line number " + i + " of the long file, said in whole sentences. ");
            string path = Path.Combine(folder, "long.txt");
            File.WriteAllText(path, text.ToString());
            Func<IntPtr, string> fileWas = Aloud.FileOf;
            Func<IntPtr> frontWas = Aloud.FrontWindow;
            try
            {
                Aloud.FileOf = delegate { return path; };
                Aloud.FrontWindow = delegate { return IntPtr.Zero; };
                string why, file;
                string got = AloudText(Aloud.Reading("read this aloud"), out why, out file);
                if (got != null || file != path) throw new Exception("With nothing selected the open file wasn't taken: " + (got ?? why) + ", " + file);
                Aloud.Place piece = new Aloud.Place { Path = path }, next;
                string first = Aloud.Piece(piece, out next, out why);
                string second = next == null ? null : Aloud.Piece(next, out next, out why);
                if (first == null || !first.StartsWith("Line number 0 ", StringComparison.Ordinal) || first.Contains("Line number 799 ") || second == null || !second.Contains("Line number 799 "))
                    throw new Exception("The file wasn't read in pieces from the top to the end: first " + (first == null ? why : first.Length + " chars") + ", second " + (second == null ? "none" : second.Length + " chars"));
                int ticket = ++asking;
                ReadFileAloud(ticket, new Aloud.Place { Path = path });
                DateTime until = DateTime.UtcNow.AddSeconds(10);
                while (aloudFetching && DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(10); }
                if (aloudPlace == null || aloudParts == null) throw new Exception("Reading the file didn't start");
                // Two parts said, then stopped by something they said.
                aloudParts.RemoveAt(0);
                aloudPlace.Part = 2;
                asking++;
                AloudNext();
                Aloud.Place stopped;
                if (!aloudKept.TryGetValue(path, out stopped) || stopped.Part != 2 || aloudLastFile != path) throw new Exception("Its place wasn't kept when it stopped");
                if (!AloudHeard(++asking, "carry on")) throw new Exception("\"carry on\" wasn't taken with a file stopped part way");
                until = DateTime.UtcNow.AddSeconds(10);
                while (aloudFetching && DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(10); }
                List<string> all = Aloud.Parts(first);
                if (aloudPlace == null || aloudPlace.Path != path || AloudLeft != all.Count - 3) throw new Exception("\"carry on\" didn't resume at the part it stopped on: " + AloudLeft + " left of " + all.Count);
                StopAloud();
            }
            finally
            {
                Aloud.FileOf = fileWas; Aloud.FrontWindow = frontWas; StopAloud(); aloudKept.Clear(); aloudLastFile = null;
                try { Directory.Delete(folder, true); } catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            }
            checks.Add("PASS with nothing selected the file a window has open is read whole from the top, a piece at a time to its end; stopped part way it keeps its place, and \"carry on\" reads again from the part it stopped on");
        }

        private void SmokeAloud(List<string> checks)
        {
            // Told to hush, in her languages; not the asks that only look like it.
            foreach (string hush in new[] { "shut up", "Capri, shut up!", "stop talking", "stop", "ok stop", "silence", "hush", "shhh", "quiet", "be quiet please", "zip it", "enough", "that's enough", "shut up already",
                "cállate", "tais-toi", "halt die Klappe", "cala a boca", "stai zitto", "zamknij się", "hou je mond", "заткнись", "замовкни", "sus", "diam", "黙って", "闭嘴", "닥쳐" })
                if (!Aloud.Hushed(hush)) throw new Exception("\"" + hush + "\" didn't hush her.");
            foreach (string notHush in new[] { "stop the music", "stop the timer", "shut up and play music", "silence my phone", "tell him to shut up", "be quiet for a bit", "I need some quiet", "\"shut up\" she said", "stop watching", "say shut up" })
                if (Aloud.Hushed(notHush)) throw new Exception("\"" + notHush + "\" was taken as telling her to hush.");
            foreach (string stop in new[] { "stop", "ok stop", "stop please", "Capri, stop." })
                if (!Aloud.BareStop(stop)) throw new Exception("\"" + stop + "\" wasn't a bare stop for a waiting question.");
            foreach (string notStop in new[] { "stop talking", "shut up", "stop the music", "stop asking me", "hush" })
                if (Aloud.BareStop(notStop)) throw new Exception("\"" + notStop + "\" was taken as a bare stop.");
            int hushedBefore = hushes, bargedBefore = bargeIns;
            ownLines.Enqueue(delegate { });
            if (!AloudHeard(++asking, "shut up") || hushes != hushedBefore + 1 || bargeIns != bargedBefore + 1 || ownLines.Count != 0 || Hands.QuietAsked("shut up"))
                throw new Exception("\"shut up\" didn't stop her voice and drop her queued lines with no reply.");
            if (!VoiceStop("shut up") || !VoiceStop("stop talking") || VoiceStop("stop the music")) throw new Exception("A hush said over her voice isn't let through her ears while she talks.");
            checks.Add("PASS \"shut up\", \"stop talking\", \"silence\", \"hush\", \"enough\" and the like, in her languages, stop her voice and her queued lines with no brain and no reply, and get through her ears while she talks; \"stop the music\", \"shut up and play music\", \"silence my phone\", focus asks and quoted words do not");

            // Saying their words.
            string[,] says =
            {
                { "say hello world", "hello world" }, { "Capri, say \"I am the night.\"", "I am the night." }, { "can you say good morning Night City", "good morning Night City" },
                { "repeat after me: the cake is a lie", "the cake is a lie" }, { "say I'm on my way, please", "I'm on my way" }, { "say hi", "hi" },
                { "di buenos días", "buenos días" }, { "dis bonjour tout le monde", "bonjour tout le monde" }, { "sag guten Morgen", "guten Morgen" },
                { "diga bom dia", "bom dia" }, { "powiedz dzień dobry", "dzień dobry" }, { "zeg goedemorgen", "goedemorgen" }, { "скажи привет мир", "привет мир" },
                { "şunu söyle: merhaba dünya", "merhaba dünya" }, { "katakan selamat pagi", "selamat pagi" }, { "「おはよう」って言って", "おはよう" },
                { "跟我说：你好世界", "你好世界" }, { "\"안녕하세요\"라고 말해줘", "안녕하세요" }
            };
            for (int i = 0; i < says.GetLength(0); i++)
                if (Aloud.Words(says[i, 0]) != says[i, 1]) throw new Exception("\"" + says[i, 0] + "\" didn't give her exactly their words: " + (Aloud.Words(says[i, 0]) ?? "nothing"));
            foreach (string notSay in new[] { "say hi to Sam on Discord", "say hello to everyone in the call", "say that again", "say it again", "what did you say", "say something funny", "say something", "say what you think", "tell me a joke", "say the line in notepad", "say this", "di algo", "dis quelque chose", "sag etwas" })
                if (Aloud.Words(notSay) != null) throw new Exception("\"" + notSay + "\" was taken as words to say: " + Aloud.Words(notSay));
            checks.Add("PASS \"say <words>\", \"say \\\"<words>\\\"\", \"can you say\" and \"repeat after me\" give exactly their words in English, Spanish, French, German, Portuguese, Polish, Dutch, Russian, Turkish, Indonesian, Japanese, Chinese and Korean; \"say hi to Sam on Discord\", \"say that again\", \"what did you say\" and \"say something\" are left alone");

            // Their words come out as they are, through Answer, with no dressing and not put in her language.
            string languageWas = Brain.Language;
            try
            {
                Brain.Language = "French";
                int ticket = ++asking;
                if (!AloudHeard(ticket, "say the quick brown fox") || aloudSaid != "the quick brown fox")
                    throw new Exception("Her answer to \"say the quick brown fox\" wasn't exactly the words: " + aloudSaid);
            }
            finally { Brain.Language = languageWas; }
            checks.Add("PASS her reply to \"say <words>\" is exactly the words, with no opener, tail or pet name, and not put into her language");

            // Reading aloud: what was asked for.
            object[,] reads =
            {
                { "read this aloud", false, false, null }, { "can you read this aloud for me?", false, false, null }, { "read the line in notepad aloud", false, true, "notepad" },
                { "say the line in notepad", false, true, "notepad" }, { "read the whole thing aloud", true, false, null }, { "read all of it in word out loud", true, false, "word" },
                { "read the selected text", false, false, null }, { "lee esto en voz alta", false, false, null }, { "lis la ligne dans notepad à voix haute", false, true, "notepad" },
                { "lies das vor", false, false, null }, { "прочитай это вслух", false, false, null }, { "bunu sesli oku", false, false, null }, { "これを読み上げて", false, false, null },
                { "把这个读出来", false, false, null }, { "이거 읽어줘", false, false, null }
            };
            for (int i = 0; i < reads.GetLength(0); i++)
            {
                Aloud.Ask ask = Aloud.Reading((string)reads[i, 0]);
                if (ask == null || ask.Whole != (bool)reads[i, 1] || ask.Line != (bool)reads[i, 2] || ask.App != (string)reads[i, 3])
                    throw new Exception("\"" + reads[i, 0] + "\" wasn't read as asked: " + (ask == null ? "not at all" : "whole " + ask.Whole + ", line " + ask.Line + ", app " + ask.App));
            }
            foreach (string notRead in new[] { "read this article and tell me what you think", "read it again", "say that again", "read my mind", "read this", "say this", "can you read" })
                if (Aloud.Reading(notRead) != null) throw new Exception("\"" + notRead + "\" was taken as reading aloud.");
            checks.Add("PASS \"read this aloud\", \"read the line in notepad aloud\", \"say the line in notepad\" and \"read the whole thing\" are read as the selection, the line or all of it, from the window named, in the languages her routing knows; \"read this\" alone and \"read it again\" are left alone");

            // A real text box: the selection, the caret's line, all of it, then the clipboard when there is nothing.
            Func<IntPtr> frontWas = Aloud.FrontWindow;
            Func<string, IntPtr> namedWas = Aloud.NamedWindow;
            Func<string> clipWas = AloudClipboard;
            Func<IntPtr, string> fileOfWas = Aloud.FileOf;
            Aloud.FileOf = delegate { return null; };
            using (Form pad = new Form { Text = "Zorbq Pad", ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(-3000, -3000), Size = new System.Drawing.Size(500, 300) })
            using (TextBox page = new TextBox { Multiline = true, Dock = DockStyle.Fill, Text = "First line here.\r\nThe second line, with the caret.\r\nThird line last." })
            {
                pad.Controls.Add(page);
                pad.Show();
                Aloud.FrontWindow = delegate { return pad.Handle; };
                Aloud.NamedWindow = delegate(string name) { return name.IndexOf("zorbq", StringComparison.OrdinalIgnoreCase) >= 0 ? pad.Handle : IntPtr.Zero; };
                string clip = null;
                AloudClipboard = delegate { return clip; };
                Func<Aloud.Ask, string> read = delegate(Aloud.Ask ask)
                {
                    string got = null, why = null;
                    Thread reader = new Thread(delegate() { got = AloudText(ask, out why); }) { IsBackground = true };
                    reader.SetApartmentState(ApartmentState.MTA);
                    reader.Start();
                    DateTime until = DateTime.UtcNow.AddSeconds(10);
                    while (reader.IsAlive && DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(10); }
                    return got ?? "(" + why + ")";
                };
                try
                {
                    page.Focus();
                    page.Select(page.Text.IndexOf("second", StringComparison.Ordinal), 6);
                    string selected = read(Aloud.Reading("read this aloud"));
                    page.Select(page.Text.IndexOf("caret", StringComparison.Ordinal), 0);
                    string line = read(Aloud.Reading("read the line in zorbq pad aloud"));
                    string whole = read(Aloud.Reading("read the whole thing aloud"));
                    if (selected != "second" || line != "The second line, with the caret." || whole.Replace("\r", "") != "First line here.\nThe second line, with the caret.\nThird line last.")
                        throw new Exception("A text box wasn't read as asked: selected \"" + selected + "\", line \"" + line + "\", whole \"" + whole + "\"");
                    string missing = read(Aloud.Reading("read the line in nowhere app aloud"));
                    page.Text = "";
                    clip = "From the clipboard.";
                    string clipped = read(Aloud.Reading("read this aloud"));
                    clip = null;
                    string none = read(Aloud.Reading("read this aloud"));
                    if (missing != "(I can't find nowhere app open.)" || clipped != "From the clipboard." || !none.StartsWith("(There's nothing selected in Zorbq Pad", StringComparison.Ordinal))
                        throw new Exception("A missing window, the clipboard or nothing at all was handled wrong: " + missing + " / " + clipped + " / " + none);
                }
                finally { Aloud.FrontWindow = frontWas; Aloud.NamedWindow = namedWas; AloudClipboard = clipWas; Aloud.FileOf = fileOfWas; pad.Hide(); }
            }
            checks.Add("PASS a text box is read by UI Automation: the selection first, the caret's line with nothing selected, all of it when asked, the window named; the clipboard is last, and a window that isn't open or has nothing says so");

            // Long text in parts, and anything they say ends it.
            string longText = "";
            for (int i = 0; i < 12; i++) longText += "Sentence number " + i + " is here to make the text long enough to need several parts when she reads it out. ";
            List<string> parts = Aloud.Parts(longText);
            if (parts.Count < 3 || parts.Exists(part => part.Length > Aloud.PartLength) || string.Join(" ", parts.ToArray()) != longText.Trim())
                throw new Exception("Long text didn't split into whole parts: " + parts.Count);
            int readTicket = ++asking;
            ReadAloud(readTicket, longText);
            int left = AloudLeft;
            asking++;
            bool stopped = !AloudNext() && AloudLeft == 0;
            readTicket = ++asking;
            ReadAloud(readTicket, longText);
            bargeIns++;
            bool barged = !AloudNext() && AloudLeft == 0;
            if (left != parts.Count - 1 || !stopped || !barged) throw new Exception("Reading aloud didn't wait in parts or didn't stop: " + left + " left, stopped " + stopped + ", stop or Esc " + barged);
            checks.Add("PASS long text is read in parts of whole sentences, each after the one before, and anything they say, \"stop\" or Esc ends it");
            SmokeAloudFile(checks, Path.GetTempPath());
        }
    }
}
