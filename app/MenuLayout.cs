using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Her right-click menu laid out from a tree file (moddingLogic 8.3, root "menu"). The same items move between lists,
    // so every opening, tick and translation keeps working; her title, Quit Capri and the items she shows when they apply
    // stay where she puts them.
    internal sealed partial class Companion
    {
        internal const string MenuRoot = "menu", GatherFolder = "pluginsApps", MenuMainframe = "Open Capri Mainframe";

        // Each item's stock English words, taken at build before any translation or opening changes them; where each sat;
        // the lists she fills as they open, by name; and the words two items share.
        private readonly Dictionary<ToolStripItem, string> menuWords = new Dictionary<ToolStripItem, string>();
        private readonly Dictionary<ToolStripItem, ToolStripMenuItem> menuParent = new Dictionary<ToolStripItem, ToolStripMenuItem>();
        private readonly List<ToolStripItem> stockTop = new List<ToolStripItem>();
        private readonly Dictionary<ToolStripMenuItem, List<ToolStripItem>> stockKids = new Dictionary<ToolStripMenuItem, List<ToolStripItem>>();
        private readonly Dictionary<ToolStripMenuItem, string> menuLists = new Dictionary<ToolStripMenuItem, string>();
        private readonly HashSet<string> sharedWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private ToolStripMenuItem gatherItem;
        // The layout last laid, so it is laid again only when it changes.
        private string menuLaid = "";
        // Where layouts come from: the mods' packs that lay her menu, in load order, and one more laid last (her checks').
        internal Func<List<KeyValuePair<string, string>>> MenuLayoutPacks;
        internal Func<string> MenuLayoutText;
        // Lines a layout skipped, for her checks.
        internal readonly List<string> MenuLayoutSkipped = new List<string>();

        // Called once her menu is built, before it is dressed.
        private void RecordMenu(Dictionary<ToolStripMenuItem, string> lists)
        {
            foreach (KeyValuePair<ToolStripMenuItem, string> one in lists) if (one.Key != null) menuLists[one.Key] = one.Value;
            Dictionary<string, int> seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            // Her title and its line first, the line and Quit Capri last: fixed, never in the file.
            for (int at = 2; at < menu.Items.Count - 2; at++)
            {
                stockTop.Add(menu.Items[at]);
                RecordItem(menu.Items[at], null, seen);
            }
            foreach (KeyValuePair<string, int> one in seen) if (one.Value > 1) sharedWords.Add(one.Key);
        }

        private void RecordItem(ToolStripItem item, ToolStripMenuItem parent, Dictionary<string, int> seen)
        {
            if (parent != null) menuParent[item] = parent;
            if (item is ToolStripSeparator) return;
            menuWords[item] = item.Text ?? "";
            int count;
            seen[item.Text ?? ""] = seen.TryGetValue(item.Text ?? "", out count) ? count + 1 : 1;
            ToolStripMenuItem folder = item as ToolStripMenuItem;
            if (folder == null || !folder.HasDropDownItems || menuLists.ContainsKey(folder)) return;
            List<ToolStripItem> kids = new List<ToolStripItem>();
            foreach (ToolStripItem kid in folder.DropDownItems) { kids.Add(kid); RecordItem(kid, folder, seen); }
            stockKids[folder] = kids;
        }

        // How a line names an item: its words, its parent's where two share them, and its list where she fills one.
        private string MenuLine(ToolStripItem item)
        {
            if (item is ToolStripSeparator) return "[line]";
            string words = menuWords[item];
            ToolStripMenuItem folder = item as ToolStripMenuItem;
            if (folder != null && Equals(folder.Tag, Mainframe.MenuLook.Title)) return "[title] " + words;
            string list;
            if (folder != null && menuLists.TryGetValue(folder, out list)) return words + " (list: " + list + ")";
            ToolStripMenuItem parent;
            if (sharedWords.Contains(words) && menuParent.TryGetValue(item, out parent)) return words + " (from: " + menuWords[parent] + ")";
            return words;
        }

        // Her stock menu written out in the tree form, from what she built.
        internal string StockMenuTree()
        {
            StringBuilder made = new StringBuilder();
            made.Append("// Her right-click menu as she ships it, in the tree form of moddingLogic 8.3.\n");
            made.Append("// One item a line, a dash per level; move a line to move its item, \" -=\" hides it.\n");
            made.Append(MenuRoot).Append('\n');
            foreach (ToolStripItem item in stockTop) TreeLines(made, item, 1);
            return made.ToString();
        }

        private void TreeLines(StringBuilder made, ToolStripItem item, int depth)
        {
            made.Append(new string('-', depth)).Append(MenuLine(item)).Append('\n');
            ToolStripMenuItem folder = item as ToolStripMenuItem;
            List<ToolStripItem> kids;
            if (folder != null && stockKids.TryGetValue(folder, out kids)) foreach (ToolStripItem kid in kids) TreeLines(made, kid, depth + 1);
        }

        // The layouts read as her menu opens; laid again only when one changed.
        private void LayMenuIfChanged()
        {
            List<KeyValuePair<string, string>> layouts = new List<KeyValuePair<string, string>>();
            try
            {
                Func<List<KeyValuePair<string, string>>> packs = MenuLayoutPacks;
                if (packs != null) layouts.AddRange(packs());
                Func<string> source = MenuLayoutText;
                string text = source == null ? null : source();
                if (!string.IsNullOrWhiteSpace(text)) layouts.Add(new KeyValuePair<string, string>("menu layout", text));
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            if (LaidKey(layouts) == menuLaid) return;
            LayMenus(layouts);
        }

        private static string LaidKey(List<KeyValuePair<string, string>> layouts)
        {
            StringBuilder made = new StringBuilder();
            foreach (KeyValuePair<string, string> one in layouts) made.Append(one.Key).Append('\u0001').Append(one.Value).Append('\u0002');
            return made.ToString();
        }

        private sealed class MenuNode
        {
            internal ToolStripItem Item;
            internal bool Line, Gather;
            internal int Row;
            internal readonly List<MenuNode> Kids = new List<MenuNode>();
        }

        private static readonly Regex Reference = new Regex(@"\s*\((\w+)\s*:\s*([^)]*)\)\s*$");

        // Her menu put back as built, then the layout laid over it. Null or empty text is stock.
        internal void LayMenu(string text, string file)
        {
            List<KeyValuePair<string, string>> one = new List<KeyValuePair<string, string>>();
            if (!string.IsNullOrWhiteSpace(text)) one.Add(new KeyValuePair<string, string>(file, text));
            LayMenus(one);
        }

        // Her menu put back as built, then each layout over it in order: the later lays flat over the earlier.
        internal void LayMenus(List<KeyValuePair<string, string>> layouts)
        {
            menuLaid = LaidKey(layouts);
            MenuLayoutSkipped.Clear();
            RestoreMenu();
            foreach (KeyValuePair<string, string> one in layouts) LayOne(one.Value, one.Key);
            // A folder a move emptied goes; one it fills shows again.
            foreach (KeyValuePair<ToolStripMenuItem, List<ToolStripItem>> one in stockKids)
                if (one.Key.DropDownItems.Count == 0 && one.Key.Available) { one.Key.Available = false; emptied.Add(one.Key); }
            if (gatherItem != null) gatherItem.Available = gatherItem.DropDownItems.Count > 0 && gatherItem.Owner != null;
            DressDropDowns(menu.Items);
        }

        private void LayOne(string text, string file)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            Func<int, string, bool> skip = delegate(int row, string why) { string said = file + " line " + row + ": " + why; MenuLayoutSkipped.Add(said); Brain.Log("menu layout", -1, said); return false; };
            MenuNode root = new MenuNode();
            List<MenuNode> path = new List<MenuNode> { root };
            HashSet<ToolStripItem> named = new HashSet<ToolStripItem>(), hidden = new HashSet<ToolStripItem>();
            bool rooted = false;
            string[] rows = text.Replace("\r\n", "\n").Split('\n');
            for (int at = 0; at < rows.Length; at++)
            {
                int row = at + 1;
                string line = rows[at].Trim();
                if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal)) continue;
                if (!rooted) { rooted = true; if (string.Equals(line, MenuRoot, StringComparison.OrdinalIgnoreCase)) continue; skip(row, "the file doesn't start with \"menu\", so nothing was laid"); return; }
                int depth = 0;
                while (depth < line.Length && line[depth] == '-') depth++;
                string body = line.Substring(depth).Trim();
                if (depth == 0 || body.Length == 0) { skip(row, "no dash for its level"); continue; }
                if (depth > path.Count) { skip(row, "more than one level deeper than the line above, read as one deeper"); depth = path.Count; }
                bool hide = body.EndsWith("-=", StringComparison.Ordinal);
                if (hide) body = body.Substring(0, body.Length - 2).Trim();
                if (body.EndsWith("+=", StringComparison.Ordinal)) body = body.Substring(0, body.Length - 2).Trim();
                MenuNode node = new MenuNode { Row = row };
                if (body == "[line]") node.Line = true;
                else
                {
                    string from = null;
                    Match reference = Reference.Match(body);
                    if (reference.Success)
                    {
                        string kind = reference.Groups[1].Value.ToLowerInvariant();
                        if (kind == "from") from = reference.Groups[2].Value.Trim();
                        else if (kind != "list") { skip(row, "(" + kind + ": ...) isn't a reference a layout takes"); continue; }
                        body = body.Substring(0, reference.Index).Trim();
                    }
                    if (body.StartsWith("[title]", StringComparison.Ordinal)) body = body.Substring(7).Trim();
                    else if (body.StartsWith("[", StringComparison.Ordinal)) { skip(row, body + " isn't a piece a menu has"); continue; }
                    if (string.Equals(body, GatherFolder, StringComparison.OrdinalIgnoreCase)) { node.Gather = true; node.Item = Gather(); }
                    else node.Item = MenuItemNamed(body, from);
                    if (node.Item == null) { skip(row, "\"" + body + "\" matches nothing in her menu"); continue; }
                    if (named.Contains(node.Item)) { skip(row, "\"" + body + "\" is named twice; the first counts"); continue; }
                    if (hide && menuWords.ContainsKey(node.Item) && menuWords[node.Item] == MenuMainframe) { skip(row, MenuMainframe + " can't be hidden"); hide = false; }
                    named.Add(node.Item);
                    if (hide) { hidden.Add(node.Item); continue; }
                }
                path.RemoveRange(depth, path.Count - depth);
                MenuNode parent = path[path.Count - 1];
                if (parent != root && !parent.Gather && !(parent.Item is ToolStripMenuItem && stockKids.ContainsKey((ToolStripMenuItem)parent.Item)))
                {
                    skip(row, "\"" + menuWords[parent.Item] + "\" " + (menuLists.ContainsKey(parent.Item as ToolStripMenuItem ?? new ToolStripMenuItem()) ? "is a list she fills" : "is an action") + ", so it takes no items; this one stays in its own place");
                    if (node.Item != null) named.Remove(node.Item);
                    continue;
                }
                parent.Kids.Add(node);
                path.Add(node);
            }
            foreach (ToolStripItem gone in hidden) Detach(gone);
            Lay(root, null, named, hidden);
        }

        // A node's items in order, then what its list holds now that the file didn't name anywhere.
        private void Lay(MenuNode node, ToolStripMenuItem folder, HashSet<ToolStripItem> named, HashSet<ToolStripItem> hidden)
        {
            List<ToolStripItem> order = new List<ToolStripItem>();
            foreach (MenuNode kid in node.Kids) order.Add(kid.Line ? new ToolStripSeparator() : kid.Item);
            List<ToolStripItem> stock = new List<ToolStripItem>();
            if (folder == null) { for (int at = 2; at < menu.Items.Count - 2; at++) stock.Add(menu.Items[at]); }
            else foreach (ToolStripItem one in folder.DropDownItems) stock.Add(one);
            bool lines = node.Kids.Exists(kid => kid.Line);
            foreach (ToolStripItem one in stock)
            {
                if (named.Contains(one) || hidden.Contains(one)) continue;
                // Where the file draws its own lines, her stock ones in that list go.
                if (one is ToolStripSeparator && lines) continue;
                order.Add(one);
            }
            if (node.Kids.Count > 0 || folder == null)
            {
                ToolStripItemCollection into = folder == null ? menu.Items : folder.DropDownItems;
                if (lines) foreach (ToolStripItem one in stock) if (one is ToolStripSeparator) Detach(one);
                foreach (ToolStripItem one in order) if (one.Owner != null) one.Owner.Items.Remove(one);
                if (folder == null) { for (int at = 0; at < order.Count; at++) into.Insert(2 + at, order[at]); }
                else foreach (ToolStripItem one in order) into.Add(one);
            }
            foreach (MenuNode kid in node.Kids) if (!kid.Line && kid.Kids.Count > 0) Lay(kid, (ToolStripMenuItem)kid.Item, named, hidden);
        }

        private static void Detach(ToolStripItem item) { if (item.Owner != null) item.Owner.Items.Remove(item); }

        // The one folder a layout may make: pluginsApps, gathering apps' and plug-ins' items.
        private ToolStripMenuItem Gather()
        {
            if (gatherItem != null) return gatherItem;
            gatherItem = new ToolStripMenuItem(GatherFolder);
            LikeMenu(gatherItem);
            return gatherItem;
        }

        private ToolStripItem MenuItemNamed(string words, string from)
        {
            ToolStripItem found = null;
            foreach (KeyValuePair<ToolStripItem, string> one in menuWords)
            {
                if (!string.Equals(one.Value, words, StringComparison.OrdinalIgnoreCase)) continue;
                ToolStripMenuItem parent;
                if (from != null && (!menuParent.TryGetValue(one.Key, out parent) || !string.Equals(menuWords[parent], from, StringComparison.OrdinalIgnoreCase))) continue;
                if (found != null && from == null) return found;
                found = one.Key;
            }
            return found;
        }

        // Every item back in the list and place it was built in.
        // Only what a layout moved is moved back, so a list no layout touched keeps everything its opening set on it.
        private void RestoreMenu()
        {
            if (gatherItem != null && (gatherItem.Owner != null || gatherItem.DropDownItems.Count > 0)) { Detach(gatherItem); gatherItem.DropDownItems.Clear(); }
            foreach (KeyValuePair<ToolStripMenuItem, List<ToolStripItem>> one in stockKids)
            {
                if (emptied.Remove(one.Key)) one.Key.Available = true;
                if (AsBuilt(MenuEach(one.Key.DropDownItems), one.Value)) continue;
                foreach (ToolStripItem kid in new List<ToolStripItem>(MenuEach(one.Key.DropDownItems))) if (kid is ToolStripSeparator && !one.Value.Contains(kid)) one.Key.DropDownItems.Remove(kid);
                foreach (ToolStripItem kid in one.Value) { Detach(kid); one.Key.DropDownItems.Add(kid); }
            }
            List<ToolStripItem> top = new List<ToolStripItem>();
            for (int at = 2; at < menu.Items.Count - 2; at++) top.Add(menu.Items[at]);
            if (AsBuilt(top.FindAll(one => stockTop.Contains(one) || one is ToolStripSeparator), stockTop)) return;
            foreach (ToolStripItem one in top) if (one is ToolStripSeparator && !stockTop.Contains(one)) menu.Items.Remove(one);
            for (int at = 0; at < stockTop.Count; at++) { Detach(stockTop[at]); menu.Items.Insert(2 + at, stockTop[at]); }
        }

        // Folders a layout emptied and hid, shown again when it goes.
        private readonly HashSet<ToolStripMenuItem> emptied = new HashSet<ToolStripMenuItem>();

        private static bool AsBuilt(IEnumerable<ToolStripItem> now, List<ToolStripItem> built)
        {
            List<ToolStripItem> held = new List<ToolStripItem>(now);
            if (held.Count != built.Count) return false;
            for (int at = 0; at < held.Count; at++) if (held[at] != built[at]) return false;
            return true;
        }

        private static IEnumerable<ToolStripItem> MenuEach(ToolStripItemCollection items) { foreach (ToolStripItem one in items) yield return one; }

        // Her menu as it stands, in the tree form, for her checks.
        internal string MenuNow()
        {
            StringBuilder made = new StringBuilder();
            for (int at = 2; at < menu.Items.Count - 2; at++) NowLines(made, menu.Items[at], 1);
            return made.ToString();
        }

        private void NowLines(StringBuilder made, ToolStripItem item, int depth)
        {
            if (!item.Available && !(item is ToolStripSeparator)) return;
            string words;
            made.Append(new string('-', depth)).Append(item is ToolStripSeparator ? "[line]" : menuWords.TryGetValue(item, out words) ? words : item.Text).Append('\n');
            ToolStripMenuItem folder = item as ToolStripMenuItem;
            if (folder == null || menuLists.ContainsKey(folder) || !(stockKids.ContainsKey(folder) || folder == gatherItem)) return;
            foreach (ToolStripItem kid in folder.DropDownItems) NowLines(made, kid, depth + 1);
        }

#if CHECKS
        // Capri options' Mute her, Quiet timer and Focus mode: each flips the setting its Toggles switch flips, and each
        // menu's tick follows a flip made in the other.
        private void SmokeOptionsQuick(List<string> checks)
        {
            System.Reflection.MethodInfo show = typeof(ToolStripDropDownItem).GetMethod("OnDropDownShow", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Action<ToolStripMenuItem> opened = delegate(ToolStripMenuItem item) { show.Invoke(item, new object[] { EventArgs.Empty }); };
            List<string> wrong = new List<string>();
            KeyValuePair<ToolStripMenuItem, Func<bool>>[] quick = { new KeyValuePair<ToolStripMenuItem, Func<bool>>(optionsMute, () => MuteOn), new KeyValuePair<ToolStripMenuItem, Func<bool>>(optionsQuiet, () => QuietTimed), new KeyValuePair<ToolStripMenuItem, Func<bool>>(optionsFocus, () => FocusOn) };
            int at = optionsItem.DropDownItems.IndexOf(hologramItem);
            for (int i = 0; i < quick.Length; i++)
                if (optionsItem.DropDownItems.IndexOf(quick[i].Key) != at + 1 + i) wrong.Add(quick[i].Key.Text + " isn't under Hologram in its place");
            foreach (KeyValuePair<ToolStripMenuItem, Func<bool>> one in quick)
            {
                ToolStripMenuItem twin = null;
                foreach (ToolStripItem item in togglesMenu.DropDownItems) if (item.Text == one.Key.Text && item != one.Key) twin = item as ToolStripMenuItem;
                if (twin == null) { wrong.Add("no Toggles switch for " + one.Key.Text); continue; }
                bool was = one.Value();
                try
                {
                    one.Key.PerformClick();
                    opened(togglesMenu);
                    bool flipped = one.Value() == !was && twin.Checked == !was;
                    twin.PerformClick();
                    opened(optionsItem);
                    bool back = one.Value() == was && one.Key.Checked == was;
                    if (!flipped || !back) wrong.Add(one.Key.Text + ": from the menu flipped " + flipped + ", from Toggles back with the tick " + back);
                }
                finally { if (one.Value() != was) one.Key.PerformClick(); }
            }
            if (wrong.Count > 0) throw new Exception("Capri options' quick switches went wrong: " + string.Join("; ", wrong.ToArray()));
            checks.Add("PASS Capri options has Mute her, Quiet timer and Focus mode under Hologram, each flipping the same setting as its Toggles switch, the ticks in step both ways");
        }

        // Her menu laid from a tree file and put back: the stock file read back gives the built menu, a layout moves,
        // hides and gathers, a partial one keeps the rest in stock order, bad lines are skipped and logged, translated
        // words don't matter, and every list stays dressed.
        private void SmokeMenuLayout(List<string> checks, string output)
        {
            List<string> wrong = new List<string>();
            Func<string> sourceWas = MenuLayoutText;
            Func<List<KeyValuePair<string, string>>> packsWas = MenuLayoutPacks;
            string templatesWas = ModTemplates.Kept;
            Dictionary<ToolStripItem, string> textWas = new Dictionary<ToolStripItem, string>();
            try
            {
                MenuLayoutText = null;
                LayMenu(null, "stock");
                string stock = MenuNow(), tree = StockMenuTree();
                System.IO.Directory.CreateDirectory(output);
                System.IO.File.WriteAllText(System.IO.Path.Combine(output, "menu-layout-stock.md"), tree);
                LayMenu(tree, "stock tree");
                string readBack = MenuNow();
                if (readBack != stock || MenuLayoutSkipped.Count > 0)
                {
                    System.IO.File.WriteAllText(System.IO.Path.Combine(output, "menu-stock-built.txt"), stock);
                    System.IO.File.WriteAllText(System.IO.Path.Combine(output, "menu-stock-read-back.txt"), readBack);
                    wrong.Add("the stock tree read back isn't her menu: " + string.Join(" / ", MenuLayoutSkipped.ToArray()));
                }
                if (!tree.Contains("-" + MenuMainframe + "\n") || !tree.Contains("(list: personalities)") || !tree.Contains("-Play\n--")) wrong.Add("the stock tree lacks her pieces: " + tree);

                // Moved to the top, Play above Capri options, Streaming hidden.
                LayMenu("menu\n-Screensavers\n-Play\n-Capri options\n-Streaming -=\n", "layout");
                string laid = MenuNow();
                if (!laid.StartsWith("-Screensavers\n-Play\n", StringComparison.Ordinal) || laid.IndexOf("-Play\n", StringComparison.Ordinal) > laid.IndexOf("-Capri options\n", StringComparison.Ordinal) || laid.Contains("-Streaming\n") || laid.Contains("---Screensavers\n") || MenuLayoutSkipped.Count > 0)
                    wrong.Add("a layout didn't move and hide: " + laid);
                if (Undressed(menu.Items, "") != null) wrong.Add("a list wasn't dressed after a layout: " + Undressed(menu.Items, ""));

                // A partial list: its lines first, then the rest in stock order.
                LayMenu("menu\n-Help\n--Her log\n", "partial");
                string help = MenuNow();
                int helpAt = help.IndexOf("-Help\n", StringComparison.Ordinal);
                if (helpAt < 0 || !help.Substring(helpAt).StartsWith("-Help\n--Her log\n--Commands\n", StringComparison.Ordinal)) wrong.Add("a partial list: " + help);

                // pluginsApps gathers, and is the only folder a layout makes.
                LayMenu("menu\n-pluginsApps\n--Feed her\n--Play\n", "gather");
                string gathered = MenuNow();
                if (!gathered.Contains("-" + GatherFolder + "\n--Feed her\n--Play\n")) wrong.Add("pluginsApps didn't gather: " + gathered);

                // Each bad line skipped with a log line, the rest laid.
                LayMenu("menu\n-No such item\n-Play (window: games)\n-Help\n---Commands\n-Toggles\n--Hologram\n-Play\n-Play\n-Open Capri Mainframe -=\n", "bad");
                string bad = MenuNow();
                string skipped = string.Join(" / ", MenuLayoutSkipped.ToArray());
                foreach (string want in new[] { "matches nothing", "isn't a reference", "more than one level deeper", "is an action", "named twice", "can't be hidden" })
                    if (skipped.IndexOf(want, StringComparison.Ordinal) < 0) wrong.Add("no skip for " + want + ": " + skipped);
                if (!bad.Contains("-" + MenuMainframe + "\n") || bad.Contains("-Toggles\n--Hologram")) wrong.Add("a bad line was laid: " + bad);
                LayMenu("Play\n-Help\n", "unrooted");
                if (MenuNow() != stock || MenuLayoutSkipped.Count != 1) wrong.Add("a file not starting with menu was laid");

                // Her words as translated: the layout still finds them by her stock words.
                foreach (KeyValuePair<ToolStripItem, string> one in menuWords) textWas[one.Key] = one.Key.Text;
                foreach (KeyValuePair<ToolStripItem, string> one in menuWords) one.Key.Text = "es " + one.Value;
                LayMenu("menu\n-Play\n", "translated");
                if (!MenuNow().StartsWith("-Play\n", StringComparison.Ordinal) || MenuLayoutSkipped.Count > 0) wrong.Add("a translated menu wasn't laid");
                foreach (KeyValuePair<ToolStripItem, string> one in textWas) one.Key.Text = one.Value;
                textWas.Clear();

                // Read as her menu opens, and put back when the layout goes.
                string given = "menu\n-Help\n";
                MenuLayoutText = delegate { return given; };
                LayMenuIfChanged();
                bool followed = MenuNow().StartsWith("-Help\n", StringComparison.Ordinal);
                given = null;
                LayMenuIfChanged();
                if (!followed || MenuNow() != stock) wrong.Add("the layout as her menu opens: followed " + followed);

                // Packs lay in load order, the later flat over the earlier; what the later doesn't touch stays the earlier's.
                List<KeyValuePair<string, string>> packs = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("first pack", "menu\n-Play\n-Help -=\n"),
                    new KeyValuePair<string, string>("second pack", "menu\n-Capri options\n--Play\n")
                };
                MenuLayoutPacks = delegate { return packs; };
                LayMenuIfChanged();
                string layered = MenuNow();
                if (layered.Contains("-Help\n") || !layered.StartsWith("-Capri options\n--Play\n", StringComparison.Ordinal) || MenuLayoutSkipped.Count > 0) wrong.Add("packs in load order: " + layered);
                MenuLayoutPacks = delegate { return new List<KeyValuePair<string, string>>(); };
                LayMenuIfChanged();
                if (MenuNow() != stock) wrong.Add("turning the packs off didn't put her menu back");

                // The template she carries reads as a pack with a menu, goes to the layouts' folder, and is laid where mods aren't read.
                string template;
                if (!ModTemplates.Carried().TryGetValue("menu-layout.capri-mod", out template)) wrong.Add("no menu-layout template carried");
                else
                {
                    ModFile read = Mods.Read(template);
                    if (read.Menu == null || read.Menu.Length < 2 || Mods.TypeOf("menu-layout.capri-mod", template) != Mods.MainframeLayout) wrong.Add("the template didn't read as a menu layout pack");
                    LayMenu(string.Join("\n", read.Menu), "template");
                    if (MenuLayoutSkipped.Count > 0 || !MenuNow().Contains("-" + GatherFolder + "\n--Feed her\n")) wrong.Add("the template's lines: " + string.Join(" / ", MenuLayoutSkipped.ToArray()));
                    LayMenu(null, "stock");
                    ModTemplates.Kept = System.IO.Path.Combine(output, "mods");
                    string laidAt = System.IO.Path.Combine(ModTemplates.Kept, ModTemplates.FolderName, "menu-layout.capri-mod");
                    if (ModTemplates.Lay() != null || !System.IO.File.Exists(laidAt) || Array.IndexOf(Mods.TypeFolders, ModTemplates.FolderName) >= 0 || !Mods.Templates(System.IO.Path.GetDirectoryName(laidAt))
                        || !Mods.Templates(System.IO.Path.Combine(ModTemplates.Kept, Mods.PluginsApps, ModTemplates.FolderName)))
                        wrong.Add("the templates weren't laid in mods\\templates, or a templates folder would be read as mods");
                    if (!System.IO.File.Exists(System.IO.Path.Combine(ModTemplates.Kept, ModTemplates.FolderName, "night-owl.capri-mod")) || !System.IO.File.Exists(System.IO.Path.Combine(ModTemplates.Kept, ModTemplates.FolderName, "parts", "1-heading.md")))
                        wrong.Add("the night-owl template pack and its parts weren't laid in mods\\templates");
                    // An older mods\template is carried into templates once, nothing there written over, then gone.
                    string old = System.IO.Path.Combine(ModTemplates.Kept, Mods.OldTemplates);
                    System.IO.Directory.CreateDirectory(System.IO.Path.Combine(old, "parts"));
                    System.IO.File.WriteAllText(System.IO.Path.Combine(old, "theirs.capri-mod"), "theirs");
                    System.IO.File.WriteAllText(System.IO.Path.Combine(old, "parts", "extra.md"), "extra");
                    System.IO.File.WriteAllText(System.IO.Path.Combine(old, "night-owl.capri-mod"), "older");
                    Mods.SettleIn(ModTemplates.Kept, new List<string>());
                    string now = System.IO.Path.Combine(ModTemplates.Kept, ModTemplates.FolderName);
                    if (System.IO.Directory.Exists(old) || !System.IO.File.Exists(System.IO.Path.Combine(now, "theirs.capri-mod")) || !System.IO.File.Exists(System.IO.Path.Combine(now, "parts", "extra.md"))
                        || System.IO.File.ReadAllText(System.IO.Path.Combine(now, "night-owl.capri-mod")) == "older")
                        wrong.Add("an older mods\\template wasn't carried into templates once");
                }

                if (wrong.Count > 0) throw new Exception("Her menu layout went wrong: " + string.Join("; ", wrong.ToArray()));
                checks.Add("PASS her menu laid from a tree file: the stock tree read back is her menu, a layout moves, hides and gathers into pluginsApps, a partial one keeps the rest in stock order, bad lines are skipped with a log line, translated words don't matter, every list stays dressed, and no layout puts her menu back; packs lay in load order, the later over the earlier, and off puts her menu back; the menu-layout template reads as a pack for the layouts' folder and is laid as a loose file in mods\\templates, a folder never read as mods, in pluginsApps either");
            }
            finally
            {
                foreach (KeyValuePair<ToolStripItem, string> one in textWas) one.Key.Text = one.Value;
                MenuLayoutText = sourceWas;
                MenuLayoutPacks = packsWas;
                ModTemplates.Kept = templatesWas;
                LayMenu(null, "stock");
            }
        }
#endif
    }
}
