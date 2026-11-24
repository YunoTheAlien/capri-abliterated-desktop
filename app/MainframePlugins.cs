using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Every row in Mods > INSTALLED has a cog for its security overrides: per SECURITY level follow (/), allowed or
    // stopped, one click to the next (PluginSettings). Runs and added tools have cells; websites are listed. Per side.
    internal sealed partial class Mainframe
    {
        private OverridesSheet plugSheet;
        // The mods and the plug-ins the rows show; the check hands in its own.
        internal Func<List<Mod>> ModsListed;
        internal Func<List<PluginInfo>> PluginsListed;
        internal Func<string, List<PluginTool>> PluginToolsListed;

        // Her installed mods, then her plug-ins, one row each.
        // Her screensaver's id as a mod item, the same as its colours' folder name under ADD-ONS.
        internal const string ScreensaverItem = AddonColours.Screensaver;
        internal const string TamagotchiItem = "her-tamagotchi";

        private List<Mod> Installed()
        {
            Func<List<Mod>> listed = ModsListed;
            List<Mod> rows = new List<Mod>(listed != null ? listed() : capri.ModList.All);
            foreach (PluginInfo plugin in PluginsNow())
                rows.Add(new Mod { File = new ModFile { Id = plugin.Id, Name = plugin.Name ?? plugin.Id, Version = plugin.Version, Author = plugin.Author, Description = plugin.Description }, Path = plugin.Root, Plugin = plugin, On = !plugin.Off, Changed = plugin.Changed });
            // Tamagotchi is her own mod item, last, switched in Toggles as well. Her screensaver is a mod of its own now.
            if (listed == null)
            {
                rows.Add(new Mod { File = new ModFile { Id = TamagotchiItem, Name = "Tamagotchi", Author = "Capri", Description = "Her own pet game" }, On = capri.TamagotchiOn, Hers = true, Path = "" });
            }
            return rows;
        }

        private List<PluginInfo> PluginsNow()
        {
            Func<List<PluginInfo>> listed = PluginsListed;
            try { return listed != null ? listed() : capri.ModList.InstalledPlugins(); }
            catch (IOException error) { ModSays("Couldn't read your plug-ins: " + error.Message, true); return new List<PluginInfo>(); }
        }

        // What a plug-in's row says under its name: that it is a plug-in, where it runs, its tools, who made it and what for.
        internal static string PluginAbout(PluginInfo plugin, int tools, string trouble)
        {
            if (plugin.Changed) return "CHANGED, approve it again. " + (plugin.Description ?? "");
            // One of her starters, off as it was laid in: what turning it on asks of them.
            string starter = plugin.Off ? Mods.StarterAbout(plugin) : null;
            if (starter != null) return ("STARTER, off. " + starter + " " + (plugin.Description ?? "")).Trim();
            if (plugin.Off) return ("PLUG-IN, off. " + (string.IsNullOrEmpty(plugin.Author) ? "" : "by " + plugin.Author + ". ") + (plugin.Description ?? "")).Trim();
            bool online = Array.Exists(plugin.Servers, one => one.Online), local = Array.Exists(plugin.Servers, one => !one.Online && one.BuiltIn == null), own = Array.Exists(plugin.Servers, one => one.BuiltIn != null);
            string where = online && local ? "on this PC and online" : online ? "online" : local ? "on this PC" : own ? "her own tools" : "skills only";
            string count = plugin.Servers.Length == 0 ? "" : trouble == "starting" ? ", starting" : trouble == McpSignInNeeded.Said ? ", sign in" : trouble != null ? ", " + trouble : ", " + tools + (tools == 1 ? " tool" : " tools");
            return ("PLUG-IN, " + where + count + ". " + (string.IsNullOrEmpty(plugin.Author) ? "" : "by " + plugin.Author + ". ") + (plugin.Description ?? "")).Trim();
        }

        private void OpenCog(int row)
        {
            Mod mod = ShownMod(row);
            if (mod != null) OpenCogFor(mod);
        }

        internal void OpenCogFor(Mod mod)
        {
            if (plugSheet != null && !plugSheet.IsDisposed) plugSheet.Close();
            plugSheet = CogFor(mod, 1f);
            plugSheet.Show(this);
            plugSheet.Activate();
        }

        private OverridesSheet CogFor(Mod mod, float scale)
        {
            List<string[]> rows = CogRows(mod);
            return new OverridesSheet(this, SheetZoom(OverridesSheet.Wide, OverridesSheet.TallFor(rows.Count)) * scale, mod.File.Name ?? mod.File.Id, mod.Plugin != null ? mod.Plugin.Id : Mods.CogId(mod.File.Id), rows, mod.Plugin);
        }

        // A row's overrides. A plug-in: its tools, with cells, and the websites it calls, listed. A code mod: Runs and the tools
        // it added, with cells, and the websites its code names, listed. Anything else: nothing to set.
        private List<string[]> CogRows(Mod mod)
        {
            List<string[]> rows = new List<string[]>();
            if (mod.Plugin != null)
            {
                Func<string, List<PluginTool>> toolsListed = PluginToolsListed;
                List<PluginTool> tools = toolsListed != null ? toolsListed(mod.Plugin.Id) : Plugins.ToolsOf(mod.Plugin.Id);
                string trouble = Plugins.Trouble(mod.Plugin.Id);
                rows.Add(new[] { "TOOLS", null, null });
                if (tools.Count == 0) rows.Add(new[] { mod.Plugin.Off ? "Its tools show once it's on." : trouble == "starting" ? "Its tools are still being listed." : trouble == McpSignInNeeded.Said ? "Sign in to see its tools." : trouble ?? "It has no tools.", "", null });
                foreach (PluginTool tool in tools) rows.Add(new[] { tool.Title, PluginSettings.Tool, tool.Tool });
                List<string> sites = PluginSettings.Websites(mod.Plugin.Id);
                if (sites.Count > 0)
                {
                    rows.Add(new[] { "WEBSITES", Listed, null });
                    foreach (string site in sites) rows.Add(new[] { site, Listed, site });
                }
                return rows;
            }
            if (mod.Code)
            {
                string id = Mods.CogId(mod.File.Id);
                rows.Add(new[] { "RUNS", null, null });
                rows.Add(new[] { "Runs", PluginSettings.Run, Mods.RunsRow });
                List<string> tools = new List<string>(capri.ModToolNames(mod.File.Id));
                foreach (string kept in PluginSettings.Named(id, PluginSettings.Tool)) if (!tools.Contains(kept)) tools.Add(kept);
                if (tools.Count > 0)
                {
                    rows.Add(new[] { "TOOLS", null, null });
                    foreach (string tool in tools) rows.Add(new[] { tool.Replace('_', ' '), PluginSettings.Tool, tool });
                }
                List<string> sites = new List<string>();
                foreach (string place in mod.File.Places ?? new string[0]) { string site = Mods.Site(place); if (site.Length > 0 && !sites.Contains(site)) sites.Add(site); }
                if (sites.Count > 0)
                {
                    rows.Add(new[] { "WEBSITES", Listed, null });
                    foreach (string site in sites) rows.Add(new[] { site, Listed, site });
                }
                return rows;
            }
            rows.Add(new[] { "Nothing to set.", "", null });
            return rows;
        }

        // A row that is only read: a heading without column names, or a website a code mod's code names.
        private const string Listed = "listed";

        private void RemovePlugin(PluginInfo plugin)
        {
            if (plugSheet != null && !plugSheet.IsDisposed && plugSheet.PluginId == plugin.Id) plugSheet.Close();
            try
            {
                capri.ModList.RemovePlugin(plugin.Id);
                FillMods();
                ModSays((plugin.Name ?? plugin.Id) + " is removed.", false);
            }
            catch (IOException error) { ModSays("Couldn't remove that plug-in: " + error.Message, true); }
            catch (UnauthorizedAccessException error) { ModSays("Couldn't remove that plug-in: " + error.Message, true); }
        }

        // A row's security overrides: its rows, each a heading, a line or a cell per SECURITY level. One click turns a cell from
        // / to a tick to a cross and back. Paged when there are more rows than fit; Sign in for an online plug-in.
        private sealed class OverridesSheet : ModSheet
        {
            internal const int Wide = 680, Rows = 12, RowTall = 32, RowsTop = 116, Tall = RowsTop + Rows * RowTall + 50;

            // As tall as its rows, up to a page of them.
            internal static int TallFor(int rows) { return rows > Rows ? Tall : RowsTop + rows * RowTall + 24; }
            private static readonly string[] Heads = { "LOCKDOWN", "GUARDED", "TRUSTED", "BLACKWALL", "CUSTOM" };
            // Shorter, for a scale where even the smallest type can't fit the whole word in its column.
            private static readonly string[] Shorter = { "LOCK", "GUARD", "TRUST", "BLACK", "CUSTOM" };
            private const int ColumnsLeft = 300, ColumnWide = 74;
            // The id its cells are kept under: a plug-in's own, or Mods.CogId of a mod's.
            internal readonly string PluginId;
            private readonly PluginInfo plugin;
            // Each row: a heading with column names (kind null), one without (Listed, no name), a plain line (kind ""), a
            // website only listed (Listed, its name), or a run row, tool or website with its kind and name, and cells.
            private readonly List<string[]> rows;
            private readonly Label[] names = new Label[Rows];
            private readonly Label[,] heads = new Label[Rows, 5];
            private readonly Key[,] cells = new Key[Rows, 5];
            private readonly Key back, next, signIn;
            private readonly Label page;
            private int first;

            internal OverridesSheet(Mainframe owner, float zoom, string named, string id, List<string[]> rows, PluginInfo plugin)
                : base(owner, zoom, named + " security overrides", "SECURITY OVERRIDES >", Wide, TallFor(rows.Count))
            {
                PluginId = id;
                this.plugin = plugin;
                this.rows = rows;
                Words(named, 24, 50, 400, 22, Type(11f * zoom, true), White);
                if (rows.Exists(Cells)) Words("/ follows SECURITY for that level.", 24, 84, 400, 20, Small(), Faint);
                // Her Email's account, where an online plug-in's Sign in sits.
                if (plugin != null && Array.Exists(plugin.Servers, one => one.BuiltIn == "mail"))
                {
                    signIn = Make(MailTools.Ready ? "Change email" : "Set up email", 536, 50, 112, 26, delegate { Frame.OpenMailSetup(); });
                    signIn.AccessibleName = MailTools.Ready ? "Change the email account she uses" : "Set up the email account she uses";
                }
                // Sign in only when its server has asked for it, where its tools would be.
                else if (plugin != null && Plugins.Trouble(plugin.Id) == McpSignInNeeded.Said)
                {
                    signIn = Make(Plugins.SignedIn(plugin.Id) ? "Signed in" : "Sign in", 536, 50, 112, 26, delegate { SignIn(); });
                    signIn.AccessibleName = "Sign in to " + (plugin.Name ?? plugin.Id) + " in your browser";
                }
                // Column names at a type their columns can hold: each runs to the next column's edge so rounding never
                // overlaps, and the type steps down until the widest fits, measured at the drawn size.
                Font headType = Small();
                string[] headNames = Heads;
                int room = Z(ColumnWide) - Z(4);
                while (Widest(Heads, headType) > room && headType.SizeInPoints > 6.5f * zoom) headType = Look.Mono(headType.SizeInPoints - 0.5f, FontStyle.Regular, GraphicsUnit.Point);
                if (Widest(Heads, headType) > room) headNames = Shorter;
                for (int i = 0; i < Rows; i++)
                {
                    int row = i, y = RowsTop + RowTall * i;
                    names[i] = Words("", 24, y + 3, 268, 20, Type(9.5f * zoom, true), White);
                    for (int c = 0; c < 5; c++)
                    {
                        int column = c;
                        heads[i, c] = Words(headNames[c], ColumnsLeft + c * ColumnWide, y + 4, ColumnWide, 18, headType, Faint);
                        heads[i, c].Bounds = new Rectangle(Z(ColumnsLeft + c * ColumnWide), Z(y + 4), Z(ColumnsLeft + (c + 1) * ColumnWide) - Z(ColumnsLeft + c * ColumnWide), Z(18));
                        heads[i, c].TextAlign = ContentAlignment.MiddleCenter;
                        cells[i, c] = Make("", ColumnsLeft + c * ColumnWide + (ColumnWide - 30) / 2, y, 30, 22, delegate { Turn(row, column); });
                    }
                }
                int pager = RowsTop + RowTall * Rows + 8;
                back = Make("Back", 24, pager, 60, 22, delegate { first -= Rows; Fill(); });
                page = Words("", 92, pager + 3, 476, 18, Small(), Faint);
                page.TextAlign = ContentAlignment.MiddleCenter;
                next = Make("Next", 596, pager, 60, 22, delegate { first += Rows; Fill(); });
                Fill();
                Frame.Fit(this);
            }

            // A row with a cell per level.
            private static bool Cells(string[] row) { return row != null && (row[1] == PluginSettings.Tool || row[1] == PluginSettings.Site || row[1] == PluginSettings.Run); }

            private void Fill()
            {
                first = Math.Max(0, Math.Min(first, (Math.Max(0, rows.Count - 1) / Rows) * Rows));
                for (int i = 0; i < Rows; i++)
                {
                    string[] row = first + i < rows.Count ? rows[first + i] : null;
                    bool heading = row != null && row[1] == null, item = Cells(row), plain = row != null && row[1] == Listed && row[2] == null;
                    bool site = row != null && (row[1] == PluginSettings.Site || row[1] == Listed && row[2] != null);
                    names[i].Visible = row != null;
                    if (row != null)
                    {
                        names[i].Text = row[0];
                        names[i].ForeColor = heading || plain ? Rose : item || site ? White : Faint;
                        names[i].Font = heading || plain ? Type(10f * Zoom, true) : site ? Look.Mono(9f * Zoom, FontStyle.Regular, GraphicsUnit.Point) : Type(9.5f * Zoom, true);
                        names[i].Width = Z(item || heading ? 268 : 620);
                    }
                    for (int c = 0; c < 5; c++)
                    {
                        heads[i, c].Visible = heading;
                        cells[i, c].Visible = item;
                        if (!item) continue;
                        int how = PluginSettings.Of(PluginId, row[1], row[2], PluginSettings.Levels[c]);
                        cells[i, c].Glyph = how == PluginSettings.Allowed ? 7 : how == PluginSettings.Denied ? 8 : 6;
                        cells[i, c].AccessibleName = row[0] + ", " + Heads[c] + ": " + (how == PluginSettings.Allowed ? "allowed" : how == PluginSettings.Denied ? "denied" : "follows SECURITY");
                        cells[i, c].Invalidate();
                    }
                }
                bool pages = rows.Count > Rows;
                back.Visible = next.Visible = page.Visible = pages;
                back.Enabled = first > 0;
                next.Enabled = first + Rows < rows.Count;
                page.Text = "Page " + (first / Rows + 1) + " of " + ((rows.Count - 1) / Rows + 1);
            }

            private void Turn(int row, int column)
            {
                int at = first + row;
                if (at >= rows.Count || !Cells(rows[at])) return;
                PluginSettings.Cycle(PluginId, rows[at][1], rows[at][2], PluginSettings.Levels[column]);
                Fill();
                if (plugin != null) Plugins.Use(Plugins.Loaded());
                else Frame.CogTurned();
            }

            // Their sign-in, after a plain warning, in their own browser.
            private void SignIn()
            {
                string named = plugin.Name ?? plugin.Id;
                if (!Question.Ask(this, "Sign in to " + named + "? Its server gets a key to your account. Capri keeps it sealed, for this plug-in only.", "Sign in")) return;
                signIn.Enabled = false;
                signIn.Text = "Signing in";
                Plugins.SignIn(PluginId, delegate(string problem)
                {
                    Back(delegate
                    {
                        signIn.Enabled = true;
                        signIn.Text = Plugins.SignedIn(PluginId) ? "Signed in" : "Sign in";
                        if (problem != null) Notice.Tell(this, problem, named, true);
                    });
                });
            }

            internal void TurnFor(int row, int column) { Turn(row, column); }

            // For the check: the key where Sign in sits, or null.
            internal string SignInText { get { return signIn == null ? null : signIn.Text; } }

            // For the check: each row as its name and kind.
            internal string RowsSaid()
            {
                List<string> said = new List<string>();
                foreach (string[] row in rows) said.Add(row[0] + "|" + (row[1] ?? "head"));
                return string.Join("; ", said.ToArray());
            }

            private static int Widest(string[] words, Font type)
            {
                int most = 0;
                foreach (string one in words) most = Math.Max(most, TextRenderer.MeasureText(one, type, Size.Empty, TextFormatFlags.NoPadding).Width);
                return most;
            }

            // For the check: a column name wider than its column, or two that touch. Null when every one fits.
            internal string HeadsCrowded()
            {
                for (int i = 0; i < Rows; i++)
                    for (int c = 0; c < 5; c++)
                    {
                        Label head = heads[i, c];
                        if (TextRenderer.MeasureText(head.Text, head.Font, Size.Empty, TextFormatFlags.NoPadding).Width > head.Width) return head.Text + " is wider than its column at " + head.Width + " px";
                        if (c > 0 && heads[i, c - 1].Bounds.IntersectsWith(head.Bounds)) return heads[i, c - 1].Text + " touches " + head.Text;
                    }
                return null;
            }
        }

        // ---------- Check ----------

        // Mods > INSTALLED's cogs at 100, 150 and 200%: same place, nothing crowded, each opening the right overrides.
        // A click cycles a cell for this side only, and a code mod crossed at this level stops until the cross goes.
        internal void CheckModCogs(List<string> checks, string output)
        {
            Func<List<PluginInfo>> listedWas = PluginsListed;
            Func<List<Mod>> modsWas = ModsListed;
            Func<string, List<PluginTool>> toolsWas = PluginToolsListed;
            Func<ModFile, string, bool> askWas = ModAsk;
            string settingsWas = PluginSettings.Settings;
            Func<bool> sideWas = PluginSettings.SecondSide;
            string folder = Path.Combine(output, "mod-cogs-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            List<string> shots = new List<string>();
            FormWindowState stateWas = WindowState;
            Mod running = null;
            try
            {
                // Drawn where it can be seen: a minimised Mainframe draws as its title bar alone.
                if (!Visible) Show();
                if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
                Directory.CreateDirectory(folder);
                PluginSettings.Settings = folder;
                PluginSettings.SecondSide = delegate { return false; };
                PluginInfo notes = new PluginInfo { Id = "smoke-notes", Name = "Smoke notes", Version = "1.2", Author = "Smoke", Description = "Keeps notes.", Servers = new[] { new PluginServer { Name = "notes", Command = "notes.exe" }, new PluginServer { Name = "web", Url = "https://api.example-notes.invalid/mcp" } } };
                PluginInfo web = new PluginInfo { Id = "smoke-web", Name = "Smoke web search with a long name", Servers = new[] { new PluginServer { Name = "web", Url = "https://example.invalid/mcp" } } };
                PluginInfo skills = new PluginInfo { Id = "smoke-skills", Name = "Smoke skills" };
                PluginsListed = delegate { return new List<PluginInfo> { notes, web, skills }; };
                PluginSettings.Called("smoke-notes", "api.example-notes.invalid");
                PluginSettings.Called("smoke-notes", "files.example-notes.invalid");
                List<PluginTool> tools = new List<PluginTool>();
                foreach (string name in new[] { "read_notes", "write_note", "delete_note", "share_note" })
                    tools.Add(new PluginTool { Plugin = "smoke-notes", PluginName = "Smoke notes", Tool = name, Exposed = "smoke_notes__" + name, Title = name });
                PluginToolsListed = delegate(string id) { return id == "smoke-notes" ? tools : new List<PluginTool>(); };
                // A code mod whose code names a website, the front door as this build carries it, and a skin.
                Mod code = new Mod { File = Mods.ReadCode("// capri-mod: 1\n// id: smoke-weather\n// name: Smoke weather\n// version: 1.0.0\n// author: Smoke\n// description: Says the weather.\n// permissions: chat\n"
                    + "using System; using CapriDesktop;\npublic class M : ICapriMod { string at = \"https://api.example-weather.invalid/today\"; public void Start(ICapriHost h) { } public bool Heard(string w) { return false; } public void Stop() { } }"), Path = "smoke-weather" + Mods.CodeExtension, Code = true, On = true };
                byte[] doorBytes = Mods.CarriedBytes(Mods.FrontId);
                Mod door = doorBytes == null ? null : new Mod { File = Mods.ReadCode(Mods.Words(doorBytes)), Path = Mods.FrontId + Mods.CodeExtension, Code = true, On = true };
                Mod skin = new Mod { File = new ModFile { Id = "smoke-skin", Name = "Smoke skin", Version = "1.0", Author = "Smoke", Description = "A test skin.", WakeNames = new string[0] }, Path = "smoke-skin" + Mods.SkinExtension, Skin = true };
                List<Mod> listed = new List<Mod> { code, skin };
                if (door != null) listed.Insert(1, door);
                ModsListed = delegate { return listed; };
                Mod notesRow = Installed().Find(each => each.Plugin == notes);
                int expected = listed.Count + 3;
                foreach (int percent in new[] { 100, 150, 200 })
                {
                    LayoutScale(96, percent, false);
                    ShowMods(true);
                    modSearch.Box.Text = "";
                    int seen = 0;
                    for (int page = 0; page * ModRows < expected; page++)
                    {
                        modFirst = page * ModRows;
                        FillMods();
                        Application.DoEvents();
                        string crowded = TextFits() ?? Overlap();
                        if (crowded != null) throw new Exception("Mods > INSTALLED is crowded at " + percent + "%, page " + (page + 1) + ": " + crowded);
                        for (int i = 0; i < ModRows; i++)
                        {
                            if (!modNames[i].Visible) continue;
                            seen++;
                            if (!modCogs[i].Visible || modCogs[i].Left != modCogs[0].Left || modCogs[i].Top != modRemoves[i].Top || !modSwitches[i].Visible || modCogs[i].Right > modSwitches[i].Left || modSwitches[i].Right > modRemoves[i].Left)
                                throw new Exception("A row's cog is missing or out of line at " + percent + "%: " + modNames[i].Text);
                        }
                        if (percent == 100 || page == 0) shots.Add(Shot(this, Path.Combine(folder, "mods-cogs-" + percent + "-page" + (page + 1) + ".png")));
                    }
                    if (seen != expected) throw new Exception("Mods > INSTALLED showed " + seen + " rows at " + percent + "%, not " + expected);
                    foreach (Mod row in door == null ? new[] { code, skin, notesRow } : new[] { code, door, skin, notesRow })
                    {
                        using (OverridesSheet sheet = CogFor(row, percent / 100f))
                        {
                            sheet.Show(this);
                            Application.DoEvents();
                            string sheetCrowded = sheet.Crowded() ?? sheet.HeadsCrowded();
                            if (sheetCrowded != null) throw new Exception(row.File.Name + "'s security overrides are crowded at " + percent + "%: " + sheetCrowded);
                            string said = sheet.RowsSaid();
                            bool right = row == code ? said == "RUNS|head; Runs|run; WEBSITES|listed; api.example-weather.invalid|listed"
                                : row == door ? said.StartsWith("RUNS|head; Runs|run", StringComparison.Ordinal)
                                : row == skin ? said == "Nothing to set.|"
                                : said == "TOOLS|head; read_notes|tool; write_note|tool; delete_note|tool; share_note|tool; WEBSITES|listed; api.example-notes.invalid|listed; files.example-notes.invalid|listed";
                            if (!right) throw new Exception(row.File.Name + "'s cog opened the wrong rows: " + said);
                            if (percent == 100 && row == notesRow)
                            {
                                sheet.TurnFor(1, 1);
                                sheet.TurnFor(2, 2);
                                sheet.TurnFor(2, 2);
                                sheet.TurnFor(2, 0);
                                sheet.TurnFor(2, 0);
                                sheet.TurnFor(3, 3);
                                sheet.TurnFor(3, 3);
                                sheet.TurnFor(3, 3);
                            }
                            if (row != door) shots.Add(Shot(sheet, Path.Combine(folder, "cog-" + (row == code ? "code-mod" : row == skin ? "skin" : "plug-in") + "-" + percent + ".png")));
                            sheet.Close();
                        }
                    }
                }
                bool saved = PluginSettings.Of("smoke-notes", PluginSettings.Tool, "read_notes", Security.Guarded) == PluginSettings.Allowed
                    && PluginSettings.Of("smoke-notes", PluginSettings.Tool, "write_note", Security.Trusted) == PluginSettings.Denied
                    && PluginSettings.Of("smoke-notes", PluginSettings.Tool, "write_note", Security.Lockdown) == PluginSettings.Denied
                    && PluginSettings.Of("smoke-notes", PluginSettings.Tool, "delete_note", Security.Blackwall) == PluginSettings.Follows;
                PluginSettings.SecondSide = delegate { return true; };
                bool apart = PluginSettings.Of("smoke-notes", PluginSettings.Tool, "read_notes", Security.Guarded) == PluginSettings.Follows && PluginSettings.Websites("smoke-notes").Count == 0;
                PluginSettings.SecondSide = delegate { return false; };
                if (!saved || !apart) throw new Exception("A cog's security overrides weren't saved, or were read from the other side's file: saved " + saved + ", apart " + apart);

                // A real code mod crossed at the level she is at: it stops, its row says so, it can't be turned on, and it runs
                // again once the cross is taken off.
                ModsListed = modsWas;
                PluginsListed = listedWas;
                Mods mods = capri.ModList;
                ModAsk = delegate { return true; };
                string file = Path.Combine(folder, "cog-runs" + Mods.CodeExtension);
                File.WriteAllText(file, "// capri-mod: 1\n// id: cog-runs\n// name: Cog Runs\n// version: 1.0.0\n// author: Smoke\n// description: A test.\n// permissions: chat\n"
                    + "using System; using CapriDesktop;\npublic class M : ICapriMod { public void Start(ICapriHost h) { } public bool Heard(string w) { return false; } public void Stop() { } }");
                int before = mods.Running;
                if (!ImportMod(file)) throw new Exception("The Runs check's mod was refused: " + ModStatusText);
                running = mods.All.Find(each => each.File.Id == "cog-runs");
                bool ran = mods.Running == before + 1;
                PluginSettings.Set(Mods.CogId("cog-runs"), PluginSettings.Run, Mods.RunsRow, Security.Preset, PluginSettings.Denied);
                CogTurned();
                bool stopped = mods.Running == before && running.On && (ModRowAbout("cog-runs") ?? "").StartsWith("STOPPED AT THIS SECURITY LEVEL", StringComparison.Ordinal);
                mods.Set(running, false, ModAsk);
                bool refused = false;
                try { mods.Set(running, true, ModAsk); }
                catch (ModException) { refused = true; }
                refused = refused && !running.On;
                PluginSettings.Set(Mods.CogId("cog-runs"), PluginSettings.Run, Mods.RunsRow, Security.Preset, PluginSettings.Follows);
                mods.Set(running, true, ModAsk);
                PluginSettings.Set(Mods.CogId("cog-runs"), PluginSettings.Run, Mods.RunsRow, Security.Preset, PluginSettings.Denied);
                CogTurned();
                PluginSettings.Set(Mods.CogId("cog-runs"), PluginSettings.Run, Mods.RunsRow, Security.Preset, PluginSettings.Follows);
                CogTurned();
                for (int i = 0; i < 200 && mods.Running != before + 1; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(25); }
                bool back = mods.Running == before + 1;
                if (!ran || !stopped || !refused || !back)
                    throw new Exception("A code mod's Runs row didn't stop it at this level: ran " + ran + ", stopped and said so " + stopped + ", refused turning on " + refused + ", ran again once the cross went " + back);
                checks.Add("PASS Mods > INSTALLED has a cog on every row, plug-ins among them, in one place on every row and uncrowded at 100, 150 and 200%; a code mod's cog has Runs, its tools and the websites its code names listed, a carried door's has Runs, a plug-in's its tools and websites, a skin's says nothing to set; one click moves a cell from / to a tick to a cross and back, saved for this side only; a code mod crossed at this level stops, says so, can't be turned on there and runs again once the cross goes (pictures: " + string.Join(", ", shots.ToArray()) + ")");
            }
            finally
            {
                PluginsListed = listedWas;
                ModsListed = modsWas;
                PluginToolsListed = toolsWas;
                ModAsk = askWas;
                if (running != null)
                {
                    PluginSettings.Set(Mods.CogId("cog-runs"), PluginSettings.Run, Mods.RunsRow, Security.Preset, PluginSettings.Follows);
                    try { capri.ModList.Remove(running); } catch (Exception) { }
                }
                PluginSettings.Settings = settingsWas;
                PluginSettings.SecondSide = sideWas;
                LayoutScale(0, 0);
                FillMods();
                ShowMods(false);
                if (WindowState != stateWas) WindowState = stateWas;
            }
        }

        private static string Shot(Form form, string path)
        {
            using (Bitmap picture = new Bitmap(Math.Max(1, form.Width), Math.Max(1, form.Height)))
            {
                form.DrawToBitmap(picture, new Rectangle(Point.Empty, picture.Size));
                picture.Save(path, ImageFormat.Png);
            }
            return Path.GetFileName(path);
        }
    }
}
