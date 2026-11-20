using System;
using System.Collections.Generic;
using System.IO;

namespace CapriDesktop
{
    // One provider in her address book: an OpenAI-compatible server or service, its key (or none) and the model it
    // last used. Named as Windows names them: "This PC :1234" for one on this computer, otherwise its host.
    internal sealed class BookEntry
    {
        internal string Address, Key, Model;
        internal string Name { get { try { Uri at = new Uri(Address); return at.IsLoopback ? "This PC :" + at.Port : at.Host; } catch (Exception) { return Address; } } }
    }

    // Saved providers, danielxxomg's custom providers (upstream c25d4e8) sized to this Pet, as Windows built it on
    // 19 Nov: every provider that connects is kept with its key and model, picked again from a list in the provider
    // area (Mainframe > Settings > BRAIN, by the local server address), and Stop using it takes it out. Kept the
    // way her provider key already is: a file only they can read (0600), beside brain-server.
    internal sealed partial class Pet
    {
        private string ProviderBookFile { get { return Path.Combine(Program.Smoke ?? folder, "brain-providers"); } }

        // The book, one line each: address, key, model, separated by a tab. The provider saved before the book existed
        // is in it from the start.
        internal List<BookEntry> ProviderBook()
        {
            var book = new List<BookEntry>();
            string text = null;
            try { if (File.Exists(ProviderBookFile)) text = File.ReadAllText(ProviderBookFile); }
            catch (Exception error) { Brain.Log("providers", -1, "Couldn't read the saved providers: " + error.Message); }
            foreach (string row in (text ?? "").Replace("\r", "").Split('\n'))
            {
                string[] parts = row.Split('\t');
                if (parts.Length < 3) continue;
                string address = Brain.ServerAddress(parts[0]);
                string key = parts[1].Trim().Length == 0 ? null : parts[1].Trim();
                if (address == null || book.Exists(one => one.Address == address)) continue;
                // A key only ever goes to its own https address, or one on this computer.
                if (key != null && !Brain.ServerKeySafe(address)) key = null;
                book.Add(new BookEntry { Address = address, Key = key, Model = parts[2].Trim().Length == 0 ? null : parts[2].Trim() });
            }
            // Only before the book exists: afterwards each provider is put in when it connects, so new ones go at the end.
            string savedAddress, savedKey;
            if (text == null && ReadServerPair(ServerPairFile, out savedAddress, out savedKey))
            {
                savedAddress = Brain.ServerAddress(savedAddress);
                if (savedAddress != null && !book.Exists(one => one.Address == savedAddress))
                    book.Insert(0, new BookEntry { Address = savedAddress, Key = savedKey != null && Brain.ServerKeySafe(savedAddress) ? savedKey : null, Model = Saved.ServerModel });
            }
            return book;
        }

        private bool KeepBook(List<BookEntry> book)
        {
            var text = new System.Text.StringBuilder();
            foreach (BookEntry one in book)
                text.Append(one.Address).Append('\t').Append(one.Key ?? "").Append('\t').Append(one.Model ?? "").Append('\n');
            return WriteOwnerOnly(ProviderBookFile, book.Count == 0 ? null : text.ToString(), "the saved providers");
        }

        // Written or updated whenever a provider connects or its model changes, so the book is always what worked.
        internal void Booked(string address, string key, string model)
        {
            if (address == null) return;
            List<BookEntry> book = ProviderBook();
            BookEntry had = book.Find(one => one.Address == address);
            if (had == null) book.Add(new BookEntry { Address = address, Key = key, Model = model });
            else { had.Key = key; had.Model = model ?? had.Model; }
            KeepBook(book);
        }

        internal void Unbook(string address)
        {
            if (address == null) return;
            List<BookEntry> book = ProviderBook();
            if (book.RemoveAll(one => one.Address == address) > 0) KeepBook(book);
        }

        internal void UnkeyBook(string address)
        {
            List<BookEntry> book = ProviderBook();
            BookEntry had = book.Find(one => one.Address == address);
            if (had == null || had.Key == null) return;
            had.Key = null;
            KeepBook(book);
        }

        // Picked from the book: connected the usual way, with the model it last used.
        internal void UseBooked(string address)
        {
            BookEntry one = ProviderBook().Find(entry => entry.Address == address);
            if (one != null) UseServer(one.Address, one.Key, one.Model);
        }

        // Mainframe's Saved providers list: each one by name, the one in use picked; "None saved" while it's empty.
        internal string[] MainframeProviders
        {
            get
            {
                List<BookEntry> book = ProviderBook();
                if (book.Count == 0) return new[] { "None saved" };
                var names = new string[book.Count];
                for (int i = 0; i < book.Count; i++) names[i] = book[i].Name + (book[i].Key != null ? " (key)" : "");
                return names;
            }
        }

        internal int MainframeProviderIndex { get { return Math.Max(0, ProviderBook().FindIndex(one => one.Address == Brain.Server)); } }

        internal bool MainframeHasProviders { get { return ProviderBook().Count > 0; } }

        internal void PickProvider(int index)
        {
            List<BookEntry> book = ProviderBook();
            if (index < 0 || index >= book.Count || book[index].Address == Brain.Server) return;
            UseBooked(book[index].Address);
        }

        // Every saved provider key, for Brain.Scrubbed to keep out of errors.txt.
        private IEnumerable<string> BookKeys()
        {
            List<BookEntry> book;
            try { book = ProviderBook(); }
            catch (Exception) { yield break; }
            foreach (BookEntry one in book) if (one.Key != null) yield return one.Key;
        }

        // Written whole and then moved into place, readable and writable by them only; null text removes it.
        private static bool WriteOwnerOnly(string path, string text, string what)
        {
            string temp = path + ".new";
            try
            {
                if (text == null)
                {
                    File.Delete(path);
                    try { File.Delete(temp); } catch (Exception) { }
                    return true;
                }
                FileStreamOptions options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                using (FileStream stream = new FileStream(temp, options))
                {
                    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(stream.SafeFileHandle, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                    using (StreamWriter writer = new StreamWriter(stream)) writer.Write(text);
                }
                File.Move(temp, path, true);
                return true;
            }
            catch (Exception error)
            {
                try { File.Delete(temp); } catch (Exception) { }
                Brain.Log(path, -1, "Couldn't save " + what + ": " + error.Message);
                return false;
            }
        }
    }
}
