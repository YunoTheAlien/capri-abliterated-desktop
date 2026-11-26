#if !CROSS
using System;
using System.IO;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Her saved keys, sealed to this PC rather than to this Windows install: AES under a key worked out from the
    // motherboard's serial (else the BIOS UUID, else an id of her own), so a Windows reinstall keeps them.
    internal static class MachineSeal
    {
        // A value sealed this way starts with this; one without it is from Windows' own store and is moved once.
        internal const string Mark = "cm1:";
        private const int Rounds = 200000;
        private static readonly byte[] Salt = Encoding.UTF8.GetBytes("Capri saved keys, sealed to this PC, 1");
        private static readonly object gate = new object();
        private static byte[] cipherKey, macKey;

        // For the checks: what this PC is known by, and her own folder for the id kept when it has none of its own.
        internal static Func<string> Identity = ReadIdentity;
        internal static Func<string> Folder = delegate { return null; };

        // Board serials and UUIDs makers leave unfilled.
        private static readonly string[] Placeholders =
        {
            "", "default string", "to be filled by o.e.m.", "to be filled by oem", "none", "not applicable", "n/a",
            "system serial number", "base board serial number", "serial number", "0", "00000000", "123456789",
            "03000200-0400-0500-0006-000700080009"
        };

        internal static bool Placeholder(string value)
        {
            string plain = (value ?? "").Trim().ToLowerInvariant();
            if (Array.IndexOf(Placeholders, plain) >= 0) return true;
            string digits = plain.Replace("-", "");
            return digits.Length > 0 && (digits.Trim('0').Length == 0 || digits.Trim('f').Length == 0);
        }

        private static string Wmi(string query, string field)
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(query))
                    foreach (ManagementBaseObject found in searcher.Get())
                        using (found) { object value = found[field]; if (value != null) return value.ToString().Trim(); }
            }
            catch (ManagementException) { }
            catch (UnauthorizedAccessException) { }
            catch (System.Runtime.InteropServices.COMException) { }
            return null;
        }

        // The board's serial, else the BIOS UUID, never a network card's address. With both unfilled, an id of her own,
        // made once and kept in her folder.
        private static string ReadIdentity()
        {
            string serial = Wmi("SELECT SerialNumber FROM Win32_BaseBoard", "SerialNumber");
            if (!Placeholder(serial)) return "board:" + serial;
            string uuid = Wmi("SELECT UUID FROM Win32_ComputerSystemProduct", "UUID");
            if (!Placeholder(uuid)) return "uuid:" + uuid.ToUpperInvariant();
            return "own:" + OwnId();
        }

        private static string OwnId()
        {
            string folder = Folder();
            if (string.IsNullOrEmpty(folder)) folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Capri");
            string file = Path.Combine(folder, "machine-id.txt");
            try
            {
                if (File.Exists(file))
                {
                    string kept = File.ReadAllText(file).Trim();
                    if (kept.Length >= 32) return kept;
                }
                byte[] made = new byte[32];
                using (RandomNumberGenerator random = RandomNumberGenerator.Create()) random.GetBytes(made);
                string id = BitConverter.ToString(made).Replace("-", "");
                Directory.CreateDirectory(folder);
                File.WriteAllText(file, id);
                Brain.Log("keys", -1, "This PC's board serial and BIOS UUID are unfilled, so her saved keys are sealed to an id of her own in " + file + ". Keep that file with her settings.");
                return id;
            }
            catch (IOException) { return "unkept"; }
            catch (UnauthorizedAccessException) { return "unkept"; }
        }

        // Worked out once a run: many rounds, so it is slow on purpose.
        private static void Ready()
        {
            lock (gate)
            {
                if (cipherKey != null) return;
                using (Rfc2898DeriveBytes derive = new Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(Identity() ?? ""), Salt, Rounds))
                {
                    byte[] both = derive.GetBytes(64);
                    cipherKey = new byte[32];
                    macKey = new byte[32];
                    Array.Copy(both, 0, cipherKey, 0, 32);
                    Array.Copy(both, 32, macKey, 0, 32);
                    Array.Clear(both, 0, both.Length);
                }
            }
        }

        // For the checks: worked out again, after Identity changes.
        internal static void Forget() { lock (gate) { cipherKey = null; macKey = null; } }

        // AES-256-CBC, then an HMAC-SHA256 over the purpose, the IV and the cipher text, so a changed value or one from
        // another PC reads as nothing rather than as rubbish.
        internal static string Seal(byte[] plain, byte[] purpose)
        {
            Ready();
            using (Aes aes = Aes.Create())
            {
                aes.Key = cipherKey;
                aes.GenerateIV();
                byte[] sealedBytes;
                using (ICryptoTransform encrypt = aes.CreateEncryptor()) sealedBytes = encrypt.TransformFinalBlock(plain, 0, plain.Length);
                byte[] body = new byte[16 + sealedBytes.Length + 32];
                Array.Copy(aes.IV, 0, body, 0, 16);
                Array.Copy(sealedBytes, 0, body, 16, sealedBytes.Length);
                byte[] tag = Tag(purpose, body, 16 + sealedBytes.Length);
                Array.Copy(tag, 0, body, 16 + sealedBytes.Length, 32);
                return Mark + Grouped(Convert.ToBase64String(body));
            }
        }

        // The sealed text in groups of three with a dot between, a mark base64 never uses, so no run of four letters or more
        // shows in a key file whatever the bytes, and no word can be spelled in one by chance.
        internal const int Group = 3;
        internal const char Gap = '.';

        internal static string Grouped(string text)
        {
            StringBuilder made = new StringBuilder(text.Length + text.Length / Group);
            for (int at = 0; at < text.Length; at++)
            {
                if (at > 0 && at % Group == 0) made.Append(Gap);
                made.Append(text[at]);
            }
            return made.ToString();
        }

        internal static byte[] Unseal(string text, byte[] purpose)
        {
            if (string.IsNullOrEmpty(text) || !text.StartsWith(Mark, StringComparison.Ordinal)) return null;
            byte[] body;
            // One sealed before the groups reads the same: it has no dots to take out.
            try { body = Convert.FromBase64String(text.Substring(Mark.Length).Replace(Gap.ToString(), "")); }
            catch (FormatException) { return null; }
            if (body.Length < 16 + 16 + 32) return null;
            Ready();
            byte[] tag = Tag(purpose, body, body.Length - 32);
            int differ = 0;
            for (int i = 0; i < 32; i++) differ |= tag[i] ^ body[body.Length - 32 + i];
            if (differ != 0) return null;
            using (Aes aes = Aes.Create())
            {
                aes.Key = cipherKey;
                byte[] iv = new byte[16];
                Array.Copy(body, 0, iv, 0, 16);
                aes.IV = iv;
                try { using (ICryptoTransform decrypt = aes.CreateDecryptor()) return decrypt.TransformFinalBlock(body, 16, body.Length - 16 - 32); }
                catch (CryptographicException) { return null; }
            }
        }

        private static byte[] Tag(byte[] purpose, byte[] body, int length)
        {
            using (HMACSHA256 mac = new HMACSHA256(macKey))
            {
                byte[] all = new byte[(purpose ?? new byte[0]).Length + length];
                if (purpose != null) Array.Copy(purpose, 0, all, 0, purpose.Length);
                Array.Copy(body, 0, all, all.Length - length, length);
                return mac.ComputeHash(all);
            }
        }

        // A value from Windows' own store, as every saved key was before: read this once so it can be sealed the new way.
        // The only call to that store left in her.
        internal static byte[] FromWindows(string text, byte[] purpose)
        {
            if (string.IsNullOrEmpty(text) || text.StartsWith(Mark, StringComparison.Ordinal)) return null;
            try { return ProtectedData.Unprotect(Convert.FromBase64String(text), purpose, DataProtectionScope.CurrentUser); }
            catch (CryptographicException) { return null; }
            catch (FormatException) { return null; }
        }

        // For the checks: a value made the old way, in Windows' own store, to move.
        internal static string OldForChecks(string plain, byte[] purpose)
        {
            return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), purpose, DataProtectionScope.CurrentUser));
        }

        internal static string OwnIdForChecks() { return OwnId(); }
    }

    internal sealed partial class Companion
    {
        private string keysNotice;

        // Once at start: every key still in Windows' own store is read that once and sealed to this PC. One that can't
        // be read here is let go and named, so they know which to enter again.
        private void MoveKeys()
        {
            System.Collections.Generic.List<string> lost = new System.Collections.Generic.List<string>();
            bool changed = false;
            Func<string, string, string, string> move = delegate(string saved, string name, string place)
            {
                if (!Voice.Unmoved(saved)) return saved;
                changed = true;
                string plain = Voice.Unprotect(saved);
                if (plain != null) return Voice.Protect(plain);
                lost.Add("Your " + name + " couldn't be read on this PC: enter it again in " + place + ".");
                return null;
            };
            preferences.VoiceKey = move(preferences.VoiceKey, "voice key", "Mainframe > Settings > Brain > VOICE");
            preferences.TextKey = move(preferences.TextKey, "brain key", "Mainframe > Settings > Brain > BRAIN");
            preferences.NvidiaKey = move(preferences.NvidiaKey, "NVIDIA key", "Mainframe > Settings > Brain > BRAIN");
            preferences.PaidKey = move(preferences.PaidKey, "MAX INTELLIGENCE key", "Mainframe > Settings > Brain > BRAIN");
            preferences.PaidSpare = move(preferences.PaidSpare, "other MAX INTELLIGENCE key", "Mainframe > Settings > Brain > BRAIN");
            preferences.AsideKey = move(preferences.AsideKey, "voice server key", "Mainframe > Settings > Brain > VOICE");
            preferences.ServerKey = move(preferences.ServerKey, "voice server key", "Mainframe > Settings > Brain > VOICE");
            preferences.BrainProvider = move(preferences.BrainProvider, "provider key", "Mainframe > Settings > Brain > BRAIN");
            preferences.ProviderBook = move(preferences.ProviderBook, "saved providers' keys", "Mainframe > Settings > Brain > BRAIN");
            // The address book's rows: address, name, model and a key sealed on its own.
            if (!string.IsNullOrEmpty(preferences.ProviderList))
            {
                string[] rows = preferences.ProviderList.Split('\n');
                for (int i = 0; i < rows.Length; i++)
                {
                    string[] parts = rows[i].Split('\t');
                    if (parts.Length < 4 || parts[3].Length == 0) continue;
                    parts[3] = move(parts[3], "key for " + parts[0], "Mainframe > Settings > Brain > BRAIN") ?? "";
                    rows[i] = string.Join("\t", parts);
                }
                preferences.ProviderList = string.Join("\n", rows);
            }
            // Remote's room: sealed again, or left for Remote to start a new one as it always has.
            if (preferences.Remote != null && Voice.Unmoved(preferences.Remote.Sealed))
            {
                RemoteState room = RemoteState.Unseal(preferences.Remote.Sealed);
                if (room != null) preferences.Remote.Sealed = room.Seal();
                changed = true;
            }
            if (MoveFiles(lost)) changed = true;
            if (!changed) return;
            SavePreferencesNow();
            Brain.Log("keys", -1, "Her saved keys moved from Windows' own store to a seal of this PC's" + (lost.Count == 0 ? "." : "; " + lost.Count + " couldn't be read."));
            if (lost.Count > 0) keysNotice = string.Join(" ", lost.ToArray());
        }

        // The sealed files outside settings.json: her mail account, plug-ins' sign-ins and their signing key. Each one still in
        // Windows' store is read that once and written sealed to this PC; one that can't be read is named, the key kept aside.
        private bool MoveFiles(System.Collections.Generic.List<string> lost)
        {
            bool moved = false;
            string data = MachineSeal.Folder();
            if (string.IsNullOrEmpty(data)) return false;
            string plugins = Path.Combine(data, "personalSettings", "keys", "plugins");
            try
            {
                string mail = Path.Combine(plugins, "mail", "account.signin.json");
                if (File.Exists(mail))
                {
                    string saved = File.ReadAllText(mail).Trim();
                    if (Voice.Unmoved(saved))
                    {
                        moved = true;
                        string plain = Voice.Unprotect(saved);
                        if (plain != null) File.WriteAllText(mail, Voice.Protect(plain), new UTF8Encoding(false));
                        else lost.Add("Your email's app password couldn't be read on this PC: give it again from Email's cog in Mods.");
                    }
                }
                if (Directory.Exists(plugins))
                    foreach (string file in Directory.GetFiles(plugins, "*.signin.json"))
                    {
                        string text = File.ReadAllText(file), was = text;
                        bool gone = false;
                        text = System.Text.RegularExpressions.Regex.Replace(text, "\"(access|refresh)\"\\s*:\\s*\"([^\"]*)\"", delegate(System.Text.RegularExpressions.Match one)
                        {
                            string held = one.Groups[2].Value.Replace("\\/", "/");
                            if (!Voice.Unmoved(held)) return one.Value;
                            string plain = Voice.Unprotect(held);
                            if (plain == null) { gone = true; return "\"" + one.Groups[1].Value + "\":\"\""; }
                            return "\"" + one.Groups[1].Value + "\":\"" + Voice.Protect(plain) + "\"";
                        });
                        if (text == was) continue;
                        moved = true;
                        File.WriteAllText(file, text, new UTF8Encoding(false));
                        if (gone) lost.Add("Your sign-in for " + Path.GetFileName(file).Replace(".signin.json", "") + " couldn't be read on this PC: sign in again from its cog in Mods.");
                    }
                string seal = Path.Combine(data, "personalSettings", "keys", PluginSettings.FolderName, PluginSettings.SealName);
                if (File.Exists(seal))
                {
                    string saved = File.ReadAllText(seal).Trim();
                    if (Voice.Unmoved(saved))
                    {
                        moved = true;
                        string plain = Voice.Unprotect(saved);
                        if (plain != null) File.WriteAllText(seal, Voice.Protect(plain));
                        else
                        {
                            PluginSettings.KeepAside(seal);
                            lost.Add("Your plug-ins' signing key couldn't be read on this PC, so it was kept aside: plug-ins you approved need turning on again in Mods.");
                        }
                    }
                }
            }
            catch (IOException caughtOne) { Brain.Caught(caughtOne); }
            catch (UnauthorizedAccessException caughtOne) { Brain.Caught(caughtOne); }
            return moved;
        }

        private void ShowKeysNotice()
        {
            string notice = keysNotice;
            keysNotice = null;
            if (notice == null || testing) return;
            balloonPage = null;
            balloonUpdate = false;
            balloonMainframe = true;
            Balloon(15000, "Some saved keys need entering again", notice, ToolTipIcon.Warning);
        }

        // Her seals in groups: 2000 of them never show four letters in a row, a seal from before the groups still reads, and a
        // grouped one reads back to what was sealed.
#if CHECKS
        private void SmokeSealGroups(System.Collections.Generic.List<string> checks)
        {
            System.Diagnostics.Stopwatch took = System.Diagnostics.Stopwatch.StartNew();
            byte[] purpose = Encoding.UTF8.GetBytes("seal groups check");
            Random random = new Random(7);
            System.Text.RegularExpressions.Regex run = new System.Text.RegularExpressions.Regex("[A-Za-z0-9]{4,}");
            int long4 = 0, unread = 0;
            for (int one = 0; one < 2000; one++)
            {
                byte[] key = new byte[random.Next(16, 200)];
                random.NextBytes(key);
                string made = MachineSeal.Seal(key, purpose);
                if (run.IsMatch(made.Substring(MachineSeal.Mark.Length))) long4++;
                byte[] back = MachineSeal.Unseal(made, purpose);
                if (back == null || Convert.ToBase64String(back) != Convert.ToBase64String(key)) unread++;
            }
            string grouped = MachineSeal.Seal(Encoding.UTF8.GetBytes("older key"), purpose);
            string older = MachineSeal.Mark + grouped.Substring(MachineSeal.Mark.Length).Replace(MachineSeal.Gap.ToString(), "");
            byte[] olderBack = MachineSeal.Unseal(older, purpose);
            bool oldReads = olderBack != null && Encoding.UTF8.GetString(olderBack) == "older key";
            string key2 = Voice.Protect("sk-group-test");
            bool voiced = Voice.Unprotect(key2) == "sk-group-test" && !run.IsMatch(key2.Substring(MachineSeal.Mark.Length));
            if (long4 > 0 || unread > 0 || !oldReads || !voiced)
                throw new Exception("Her seals in groups went wrong: " + long4 + " of 2000 showed four letters in a row, " + unread + " didn't read back, a seal from before read " + oldReads + ", a saved key " + voiced);
            checks.Add("PASS her seals are written in groups of " + MachineSeal.Group + ": 2000 never show four letters in a row and all read back, one from before the groups still reads, and a saved key round-trips (" + took.ElapsedMilliseconds + " ms)");
        }
#endif

        // Sealed to this PC: a key and Remote's room round-trip, a changed value or another PC's reads as nothing, a board
        // left unfilled falls to the UUID and then to her own id, and a key from Windows' store moves once, or is named.
#if CHECKS
        private void SmokeMachineSeal(System.Collections.Generic.List<string> checks, string output)
        {
            Func<string> identityWas = MachineSeal.Identity, folderWas = MachineSeal.Folder;
            string nvidiaWas = preferences.NvidiaKey, voiceWas = preferences.VoiceKey, noticeWas = keysNotice;
            try
            {
                MachineSeal.Identity = delegate { return "board:TEST-1234"; };
                MachineSeal.Forget();
                string sealedKey = Voice.Protect("sk-seal-test");
                bool round = sealedKey.StartsWith(MachineSeal.Mark, StringComparison.Ordinal) && Voice.Unprotect(sealedKey) == "sk-seal-test" && sealedKey.IndexOf("sk-seal", StringComparison.Ordinal) < 0;
                char[] bent = sealedKey.ToCharArray();
                // A character in the middle, never a group's dot, padding or the last one whose low bits base64 drops.
                int bend = bent.Length / 2;
                while (bent[bend] == MachineSeal.Gap || bent[bend] == '=') bend++;
                bent[bend] = bent[bend] == 'A' ? 'B' : 'A';
                bool tampered = Voice.Unprotect(new string(bent)) == null;
                RemoteState room = RemoteState.Fresh();
                RemoteState back = RemoteState.Unseal(room.Seal());
                bool remote = back != null && back.Room == room.Room && back.Token == room.Token;
                MachineSeal.Identity = delegate { return "board:ANOTHER-PC"; };
                MachineSeal.Forget();
                bool elsewhere = Voice.Unprotect(sealedKey) == null;
                bool placeholders = MachineSeal.Placeholder("Default string") && MachineSeal.Placeholder("To be filled by O.E.M.") && MachineSeal.Placeholder("")
                    && MachineSeal.Placeholder("03000200-0400-0500-0006-000700080009") && MachineSeal.Placeholder("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF")
                    && MachineSeal.Placeholder("00000000-0000-0000-0000-000000000000") && !MachineSeal.Placeholder("MB-7H2K9QX") && !MachineSeal.Placeholder("4C4C4544-0051-3010-8052-B4C04F4B4E32");
                string folder = Path.Combine(output, "machine-id");
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
                MachineSeal.Folder = delegate { return folder; };
                string first = MachineSeal.OwnIdForChecks(), second = MachineSeal.OwnIdForChecks();
                bool own = first.Length >= 32 && first == second && File.Exists(Path.Combine(folder, "machine-id.txt"));
                // A key from Windows' store moves to a seal of this PC; one that can't be read is let go and named.
                MachineSeal.Identity = delegate { return "board:TEST-1234"; };
                MachineSeal.Forget();
                preferences.NvidiaKey = MachineSeal.OldForChecks("nv-move-test", Voice.EntropyForChecks);
                preferences.VoiceKey = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 });
                keysNotice = null;
                MoveKeys();
                bool moved = preferences.NvidiaKey != null && preferences.NvidiaKey.StartsWith(MachineSeal.Mark, StringComparison.Ordinal) && Voice.Unprotect(preferences.NvidiaKey) == "nv-move-test";
                bool named = preferences.VoiceKey == null && keysNotice == "Your voice key couldn't be read on this PC: enter it again in Mainframe > Settings > Brain > VOICE.";
                // The sealed files: her mail account, a plug-in's sign-in (one token readable, one not) and the plug-ins' key.
                string data = Path.Combine(output, "seal-files");
                if (Directory.Exists(data)) Directory.Delete(data, true);
                string signins = Path.Combine(data, "personalSettings", "keys", "plugins"), keys = Path.Combine(data, "personalSettings", "keys", PluginSettings.FolderName);
                Directory.CreateDirectory(Path.Combine(signins, "mail"));
                Directory.CreateDirectory(keys);
                string mailFile = Path.Combine(signins, "mail", "account.signin.json"), signinFile = Path.Combine(signins, "calendar.signin.json"), sealFile = Path.Combine(keys, PluginSettings.SealName);
                File.WriteAllText(mailFile, MachineSeal.OldForChecks("{\"address\":\"me@example.com\"}", Voice.EntropyForChecks));
                File.WriteAllText(signinFile, "{\"access\":\"" + MachineSeal.OldForChecks("at-move-test", Voice.EntropyForChecks).Replace("/", "\\/") + "\",\"refresh\":\"AQIDBAUGBwgJCgsMDQ4PEA==\"}");
                File.WriteAllText(sealFile, MachineSeal.OldForChecks(Convert.ToBase64String(new byte[32]), Voice.EntropyForChecks));
                MachineSeal.Folder = delegate { return data; };
                keysNotice = null;
                MoveKeys();
                string signin = File.ReadAllText(signinFile);
                System.Text.RegularExpressions.Match access = System.Text.RegularExpressions.Regex.Match(signin, "\"access\":\"([^\"]*)\"");
                bool files = Voice.Unprotect(File.ReadAllText(mailFile).Trim()) == "{\"address\":\"me@example.com\"}" && File.ReadAllText(mailFile).StartsWith(MachineSeal.Mark, StringComparison.Ordinal)
                    && access.Success && Voice.Unprotect(access.Groups[1].Value) == "at-move-test" && signin.Contains("\"refresh\":\"\"")
                    && Voice.Unprotect(File.ReadAllText(sealFile).Trim()) == Convert.ToBase64String(new byte[32])
                    && (keysNotice ?? "").Contains("Your sign-in for calendar couldn't be read on this PC");
                // A signing key that can't be read is kept aside, not written over.
                File.WriteAllText(sealFile, "AQIDBAUGBwgJCgsMDQ4PEA==");
                keysNotice = null;
                MoveKeys();
                bool aside = !File.Exists(sealFile) && Directory.GetFiles(keys, PluginSettings.SealName + ".unreadable-*").Length == 1 && (keysNotice ?? "").Contains("kept aside");
                if (!round || !tampered || !remote || !elsewhere || !placeholders || !own || !moved || !named || !files || !aside)
                    throw new Exception("Keys sealed to this PC went wrong: round trip " + round + ", a changed one refused " + tampered + ", Remote's room " + remote + ", another PC refused " + elsewhere
                        + ", placeholders " + placeholders + ", her own id kept " + own + ", a Windows-store key moved " + moved + ", an unreadable one named " + named + ", the sealed files moved " + files + ", an unreadable signing key kept aside " + aside + " (" + keysNotice + ")");
                checks.Add("PASS saved keys are sealed to this PC: a key and Remote's room round-trip, a changed value or another PC's reads as nothing, unfilled board serials and UUIDs are told apart with her own kept id last, and a key from Windows' store moves once while an unreadable one is named to enter again; her mail account, a plug-in's sign-in and the plug-ins' signing key move too, an unreadable token named and an unreadable signing key kept aside, never written over");
            }
            finally
            {
                MachineSeal.Identity = identityWas;
                MachineSeal.Folder = folderWas;
                MachineSeal.Forget();
                preferences.NvidiaKey = nvidiaWas;
                preferences.VoiceKey = voiceWas;
                keysNotice = noticeWas;
                SavePreferences();
            }
        }
#endif
    }
}
#endif
