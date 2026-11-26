using System;
using System.Drawing;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Setting up her Email plug-in: their address and an app password, and the servers for an address she doesn't know.
    // Opened from Email's cog, and when Email is turned on with no account. Connect signs in to both servers before
    // anything is kept; what is kept is sealed (MailAccount).
    internal sealed partial class Mainframe
    {
        private MailSheet mailSheet;

        internal string MailSheetTitle { get { return mailSheet == null || mailSheet.IsDisposed ? null : mailSheet.Text; } }

        internal void OpenMailSetup()
        {
            if (mailSheet != null && !mailSheet.IsDisposed) { mailSheet.Activate(); return; }
            mailSheet = new MailSheet(this, SheetZoom(MailSheet.Wide, MailSheet.Tall));
            mailSheet.Show(this);
            mailSheet.Activate();
        }

        internal void CloseMailSetup() { if (mailSheet != null && !mailSheet.IsDisposed) mailSheet.Close(); }

        // For checks: the window drawn at a zoom, with an address typed, whether anything in it is crowded, and its picture.
        internal string MailSetupAt(float zoom, string address, string picture)
        {
            using (MailSheet drawn = new MailSheet(this, zoom))
            {
                drawn.Inline = true;
                drawn.TypeAddress(address);
                drawn.Show(this);
                Application.DoEvents();
                string crowded = drawn.Crowded();
                if (picture != null) drawn.Picture(picture);
                drawn.Close();
                return crowded;
            }
        }

        // The setup at 100, 150 and 200%: for an address she knows (no server rows) and one she doesn't (both shown).
#if CHECKS
        internal void CheckMailSetup(System.Collections.Generic.List<string> checks, string folder)
        {
            System.Collections.Generic.List<string> shots = new System.Collections.Generic.List<string>();
            foreach (int percent in new[] { 100, 150, 200 })
                foreach (string address in new[] { "you@gmail.com", "you@example.org" })
                {
                    string picture = System.IO.Path.Combine(folder, "mail-setup-" + (address.EndsWith("gmail.com", StringComparison.Ordinal) ? "known" : "servers") + "-" + percent + ".png");
                    string crowded = MailSetupAt(SheetZoom(MailSheet.Wide, MailSheet.Tall) * percent / 100f, address, picture);
                    if (crowded != null) throw new Exception("Set up Email is crowded at " + percent + "% for " + address + ": " + crowded);
                    shots.Add(System.IO.Path.GetFileName(picture));
                }
            checks.Add("PASS Set up Email fits at 100, 150 and 200%, with and without the server rows (pictures: " + string.Join(", ", shots.ToArray()) + ")");
        }
#endif

        // Her starters laid in as on a fresh install: Email's row off with what turning it on asks, its cog with Set up email, and
        // pictures of both. They are taken out again after.
#if CHECKS
        internal void CheckStarterRows(System.Collections.Generic.List<string> checks, string folder)
        {
            Mods mods = capri.ModList;
            FormWindowState wasState = WindowState;
            mods.AddStarters();
            // The starters are plug-ins, so they are listed under Plug-ins.
            bool pickWas = capri.ModsPlugins;
            try
            {
                Restore();
                ShowMods(true);
                PickPlugins(true);
                int at = modShown == null ? -1 : modShown.FindIndex(one => one.Plugin != null && one.Plugin.Id == Mods.MailStarter);
                if (at < 0) throw new Exception("Her Email starter isn't in Mods after it was laid in.");
                modFirst = at / ModRows * ModRows;
                FillMods();
                Application.DoEvents();
                Mod email = modShown[at];
                string row = modAbouts[at - modFirst].Text, switched = modSwitches[at - modFirst].Text;
                string page = System.IO.Path.Combine(folder, "starters-email-row.png");
                using (Bitmap shot = new Bitmap(Width, Height))
                {
                    DrawToBitmap(shot, new Rectangle(Point.Empty, Size));
                    shot.Save(page, System.Drawing.Imaging.ImageFormat.Png);
                }
                if (email.On || switched != "Off" || !row.StartsWith("STARTER, off. Turn it on, then give her an app password.", StringComparison.Ordinal))
                    throw new Exception("Her Email starter's row didn't show it off with what turning it on asks: " + switched + " / " + row);
                string key, crowded;
                using (OverridesSheet cog = CogFor(email, 1f))
                {
                    cog.Show(this);
                    Application.DoEvents();
                    key = cog.SignInText;
                    crowded = cog.Crowded();
                    cog.Picture(System.IO.Path.Combine(folder, "starters-email-cog.png"));
                    cog.Close();
                }
                if (key != (MailTools.Ready ? "Change email" : "Set up email") || crowded != null) throw new Exception("Email's cog didn't offer its setup where Sign in sits: " + (key ?? "no key") + (crowded == null ? "" : ", " + crowded));
                checks.Add("PASS her Email starter in Mods: off, its row saying to turn it on then give her an app password, and its cog offering Set up email where Sign in sits (pictures: starters-email-row.png, starters-email-cog.png)");
            }
            finally
            {
                foreach (string id in new[] { Mods.MailStarter, "notion", "linear" }) mods.RemovePlugin(id);
                capri.ModsPlugins = pickWas;
                ShowMods(false);
                if (WindowState != wasState) WindowState = wasState;
            }
        }
#endif

        private sealed class MailSheet : ModSheet
        {
            internal const int Wide = 620, Tall = 380;
            private readonly Entry address, password, incoming, outgoing;
            private readonly Label help, incomingName, outgoingName, status;
            private readonly Key connect, forget;

            internal MailSheet(Mainframe owner, float zoom) : base(owner, zoom, "Set up Email", "SET UP EMAIL >", Wide, Tall)
            {
                MailAccount saved = MailTools.Account;
                Font name = Type(9f * zoom, true);
                Words("She checks, searches and reads your mail with an app password, and asks you every time before she sends one.", 24, 58, 572, 36, Small(), Faint);
                Words("Email address", 24, 108, 160, 22, name, White);
                address = Field(190, 104, "you@example.com", "Email address");
                Words("App password", 24, 142, 160, 22, name, White);
                password = Field(190, 138, saved != null && saved.Password.Length > 0 ? "kept, sealed on this PC" : "an app password, not your usual one", "App password");
                password.Box.UseSystemPasswordChar = true;
                help = Words("", 190, 170, 406, 34, Small(), Faint);
                incomingName = Words("Incoming (IMAP)", 24, 214, 160, 22, name, White);
                incoming = Field(190, 210, "imap.example.com:993", "Incoming mail server");
                outgoingName = Words("Outgoing (SMTP)", 24, 248, 160, 22, name, White);
                outgoing = Field(190, 244, "smtp.example.com:465", "Outgoing mail server");
                status = Words(saved != null && saved.Password.Length > 0 ? "Connected as " + saved.Address + "." : "", 24, 284, 572, 34, Small(), Faint);
                connect = Make("Connect", 24, 328, 150, 28, delegate { Connect(); });
                connect.Accent = Cyan;
                forget = Make("Forget account", 184, 328, 150, 28, delegate { Forget(); });
                forget.Accent = Rose;
                forget.Visible = saved != null;
                if (saved != null)
                {
                    address.Box.Text = saved.Address;
                    incoming.Box.Text = saved.ImapHost + ":" + saved.ImapPort;
                    outgoing.Box.Text = saved.SmtpHost + ":" + saved.SmtpPort;
                }
                address.Box.TextChanged += delegate { Shown(); };
                Shown();
                Frame.Fit(this);
            }

            private Entry Field(int x, int y, string cue, string named)
            {
                Entry entry = new Entry();
                entry.Bounds = Box(x, y, Wide - 24 - x, 28);
                entry.Box.Font = Type(9.5f * Zoom);
                entry.Cue = cue;
                entry.Box.AccessibleName = named;
                Controls.Add(entry);
                return entry;
            }

            // For checks: an address typed in.
            internal void TypeAddress(string typed) { address.Box.Text = typed ?? ""; }

            // The help line for the address, and the server rows only for an address she has no servers for.
            private void Shown()
            {
                string note = MailAccount.Unsupported(address.Box.Text);
                help.Text = note ?? Mods.MailSetupHelp(address.Box.Text);
                help.ForeColor = note != null ? Rose : Faint;
                bool hosts = Mods.NeedsHosts(address.Box.Text);
                incomingName.Visible = incoming.Visible = outgoingName.Visible = outgoing.Visible = hosts;
            }

            private void Connect()
            {
                string problem;
                MailAccount account = Mods.MailFromForm(address.Box.Text, password.Box.Text, incoming.Box.Text, outgoing.Box.Text, out problem);
                if (account == null) { Say(problem, true); return; }
                connect.Enabled = false;
                connect.Text = "Connecting...";
                Say("Signing in to " + account.Provider + "...", false);
                Run(delegate
                {
                    string trouble = MailTools.Test(account);
                    if (trouble == null)
                    {
                        try { account.Save(MailTools.Folder); MailTools.Account = account; }
                        catch (Exception error) { trouble = "Connected, but couldn't keep it: " + error.Message; }
                    }
                    Back(delegate
                    {
                        connect.Enabled = true;
                        connect.Text = "Connect";
                        password.Box.Text = "";
                        Say(trouble ?? "Connected as " + account.Address + ". She can use your email now, and asks before she sends.", trouble != null);
                        forget.Visible = MailTools.Account != null;
                    });
                });
            }

            private void Forget()
            {
                MailTools.Forget();
                forget.Visible = false;
                Say("She forgot that account. Nothing of it is left on this PC.", false);
            }

            private void Say(string text, bool bad)
            {
                status.Text = text ?? "";
                status.ForeColor = bad ? Rose : text != null && text.StartsWith("Connected", StringComparison.Ordinal) ? Cyan : Faint;
            }
        }
    }
}
