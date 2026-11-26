using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CapriDesktop
{
    // A .tar.bz2 made in her own code, for the checks that need a voice, ears or wake word pack on a Windows without
    // tar.exe. Plain ustar, then bzip2 with one block per 800 KB, made so her own Bunzip and Windows' tar both read it.
    internal static class TarWriter
    {
        // Everything under root/inside, named from root, as tar -cjf archive -C root inside makes it.
        internal static void TarBz2(string root, string inside, string archive)
        {
            byte[] tar;
            using (MemoryStream made = new MemoryStream())
            {
                Add(made, root, inside);
                made.Write(new byte[1024], 0, 1024);
                tar = made.ToArray();
            }
            File.WriteAllBytes(archive, Bzip2(tar));
        }

        private static void Add(Stream into, string root, string relative)
        {
            string full = Path.Combine(root, relative);
            string name = relative.Replace('\\', '/');
            if (Directory.Exists(full))
            {
                Header(into, name.TrimEnd('/') + "/", 0, '5');
                List<string> inside = new List<string>(Directory.GetFileSystemEntries(full));
                inside.Sort(StringComparer.Ordinal);
                foreach (string one in inside) Add(into, root, Path.Combine(relative, Path.GetFileName(one)));
                return;
            }
            byte[] data = File.ReadAllBytes(full);
            Header(into, name, data.Length, '0');
            into.Write(data, 0, data.Length);
            int pad = (512 - data.Length % 512) % 512;
            into.Write(new byte[pad], 0, pad);
        }

        private static void Header(Stream into, string name, long size, char kind)
        {
            byte[] header = new byte[512];
            // A name over 100 bytes is split at a slash: the folders into ustar's prefix (155), the rest in the name.
            string prefix = "";
            if (Encoding.UTF8.GetByteCount(name) > 100)
            {
                int cut = name.TrimEnd('/').LastIndexOf('/');
                while (cut > 0 && (Encoding.UTF8.GetByteCount(name.Substring(cut + 1)) > 100 || Encoding.UTF8.GetByteCount(name.Substring(0, cut)) > 155)) cut = name.LastIndexOf('/', cut - 1);
                if (cut <= 0) throw new IOException("A name too long for a plain tar: " + name);
                prefix = name.Substring(0, cut);
                name = name.Substring(cut + 1);
            }
            byte[] named = Encoding.UTF8.GetBytes(name);
            Array.Copy(named, header, named.Length);
            byte[] before = Encoding.UTF8.GetBytes(prefix);
            Array.Copy(before, 0, header, 345, before.Length);
            Put(header, 100, kind == '5' ? "0000755" : "0000644");
            Put(header, 108, "0000000");
            Put(header, 116, "0000000");
            Put(header, 124, Convert.ToString(size, 8).PadLeft(11, '0'));
            Put(header, 136, Convert.ToString(1700000000L, 8).PadLeft(11, '0'));
            for (int i = 148; i < 156; i++) header[i] = (byte)' ';
            header[156] = (byte)kind;
            Put(header, 257, "ustar");
            header[263] = (byte)'0';
            header[264] = (byte)'0';
            int sum = 0;
            foreach (byte one in header) sum += one;
            Put(header, 148, Convert.ToString(sum, 8).PadLeft(6, '0'));
            header[155] = (byte)' ';
            into.Write(header, 0, 512);
        }

        private static void Put(byte[] header, int at, string text)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            Array.Copy(bytes, 0, header, at, bytes.Length);
        }

        // ---------- bzip2 ----------

        private static readonly uint[] Crcs = MakeCrcs();

        private static uint[] MakeCrcs()
        {
            uint[] table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i << 24;
                for (int k = 0; k < 8; k++) c = (c & 0x80000000u) != 0 ? (c << 1) ^ 0x04c11db7u : c << 1;
                table[i] = c;
            }
            return table;
        }

        private sealed class BitWriter
        {
            private readonly MemoryStream to = new MemoryStream();
            private uint held;
            private int count;

            internal void Write(int bits, uint value)
            {
                for (int i = bits - 1; i >= 0; i--)
                {
                    held = (held << 1) | ((value >> i) & 1u);
                    if (++count == 8) { to.WriteByte((byte)held); held = 0; count = 0; }
                }
            }

            internal byte[] Done()
            {
                if (count > 0) { to.WriteByte((byte)(held << (8 - count))); held = 0; count = 0; }
                return to.ToArray();
            }
        }

        internal static byte[] Bzip2(byte[] data)
        {
            BitWriter bits = new BitWriter();
            bits.Write(8, 'B'); bits.Write(8, 'Z'); bits.Write(8, 'h'); bits.Write(8, '9');
            uint combined = 0;
            const int BlockSize = 800000;
            for (int at = 0; at < data.Length; at += BlockSize)
            {
                int length = Math.Min(BlockSize, data.Length - at);
                uint crc = 0xffffffffu;
                for (int i = at; i < at + length; i++) crc = (crc << 8) ^ Crcs[(crc >> 24) ^ data[i]];
                crc = ~crc;
                combined = ((combined << 1) | (combined >> 31)) ^ crc;
                Block(bits, data, at, length, crc);
            }
            bits.Write(24, 0x177245); bits.Write(24, 0x385090);
            bits.Write(16, combined >> 16); bits.Write(16, combined & 0xffff);
            return bits.Done();
        }

        private static void Block(BitWriter bits, byte[] data, int at, int length, uint crc)
        {
            // Runs of four or more equal bytes: four of them, then how many more (up to 251).
            List<byte> rle = new List<byte>(length);
            for (int i = at; i < at + length;)
            {
                byte value = data[i];
                int run = 1;
                while (i + run < at + length && data[i + run] == value && run < 255) run++;
                if (run >= 4) { for (int k = 0; k < 4; k++) rle.Add(value); rle.Add((byte)(run - 4)); }
                else for (int k = 0; k < run; k++) rle.Add(value);
                i += run;
            }
            byte[] block = rle.ToArray();
            int n = block.Length;

            // Burrows-Wheeler: every rotation sorted; the last column, and where the original sits.
            int[] rotations = Rotations(block);
            int origin = 0;
            byte[] last = new byte[n];
            for (int i = 0; i < n; i++)
            {
                if (rotations[i] == 0) origin = i;
                last[i] = block[(rotations[i] + n - 1) % n];
            }

            // Move-to-front over the bytes in use, with runs of the front one as RUNA and RUNB.
            bool[] used = new bool[256];
            foreach (byte one in block) used[one] = true;
            byte[] index = new byte[256];
            List<byte> front = new List<byte>();
            for (int i = 0; i < 256; i++) if (used[i]) { index[i] = (byte)front.Count; front.Add((byte)front.Count); }
            int inUse = front.Count, end = inUse + 1, alphabet = inUse + 2;
            List<int> symbols = new List<int>();
            int zeros = 0;
            Action flush = delegate
            {
                for (int r = zeros; r > 0;)
                {
                    if ((r & 1) == 1) { symbols.Add(0); r = (r - 1) / 2; }
                    else { symbols.Add(1); r = (r - 2) / 2; }
                }
                zeros = 0;
            };
            foreach (byte one in last)
            {
                byte wanted = index[one];
                int k = front.IndexOf(wanted);
                if (k == 0) { zeros++; continue; }
                flush();
                front.RemoveAt(k);
                front.Insert(0, wanted);
                symbols.Add(k + 1);
            }
            flush();
            symbols.Add(end);

            int[] frequency = new int[alphabet];
            foreach (int one in symbols) frequency[one]++;
            int[] lengths = Lengths(frequency, 17);
            int[] codes = new int[alphabet];
            int min = 32, max = 0;
            foreach (int l in lengths) { if (l < min) min = l; if (l > max) max = l; }
            int vec = 0;
            for (int l = min; l <= max; l++)
            {
                for (int s = 0; s < alphabet; s++) if (lengths[s] == l) codes[s] = vec++;
                vec <<= 1;
            }

            bits.Write(24, 0x314159); bits.Write(24, 0x265359);
            bits.Write(16, crc >> 16); bits.Write(16, crc & 0xffff);
            bits.Write(1, 0);
            bits.Write(24, (uint)origin);
            uint rows = 0;
            for (int i = 0; i < 16; i++) for (int j = 0; j < 16; j++) if (used[i * 16 + j]) rows |= 0x8000u >> i;
            bits.Write(16, rows);
            for (int i = 0; i < 16; i++)
            {
                if ((rows & (0x8000u >> i)) == 0) continue;
                uint row = 0;
                for (int j = 0; j < 16; j++) if (used[i * 16 + j]) row |= 0x8000u >> j;
                bits.Write(16, row);
            }
            // Two tables, the least bzip2 allows, the same one twice, and every group of 50 on the first.
            const int Tables = 2;
            int selectors = (symbols.Count + 49) / 50;
            bits.Write(3, Tables);
            bits.Write(15, (uint)selectors);
            for (int i = 0; i < selectors; i++) bits.Write(1, 0);
            for (int t = 0; t < Tables; t++)
            {
                int current = lengths[0];
                bits.Write(5, (uint)current);
                for (int s = 0; s < alphabet; s++)
                {
                    while (current < lengths[s]) { bits.Write(2, 2); current++; }
                    while (current > lengths[s]) { bits.Write(2, 3); current--; }
                    bits.Write(1, 0);
                }
            }
            foreach (int one in symbols) bits.Write(lengths[one], (uint)codes[one]);
        }

        // Every rotation of the block in order, by doubling: sorted on their first k bytes, then 2k, with counting sorts,
        // until each has a rank of its own or k passes the length. Equal rotations stay together, which is all bzip2 needs.
        private static int[] Rotations(byte[] block)
        {
            int n = block.Length;
            int[] order = new int[n], rank = new int[n], next = new int[n], second = new int[n];
            int[] count = new int[Math.Max(256, n) + 1];
            for (int i = 0; i < n; i++) count[block[i] + 1]++;
            for (int i = 1; i <= 256; i++) count[i] += count[i - 1];
            for (int i = 0; i < n; i++) order[count[block[i]]++] = i;
            for (int i = 0; i < n; i++) rank[i] = block[i];
            for (int k = 1; k < n; k <<= 1)
            {
                // Ordered by the second half first: a rotation's second half is the one k further on.
                for (int i = 0; i < n; i++) second[i] = (order[i] - k + n) % n;
                int most = 0;
                for (int i = 0; i < n; i++) most = Math.Max(most, rank[i]);
                Array.Clear(count, 0, count.Length);
                for (int i = 0; i < n; i++) count[rank[i] + 1]++;
                for (int i = 1; i <= most + 1; i++) count[i] += count[i - 1];
                for (int i = 0; i < n; i++) order[count[rank[second[i]]]++] = second[i];
                next[order[0]] = 0;
                int classes = 1;
                for (int i = 1; i < n; i++)
                {
                    int a = order[i - 1], b = order[i];
                    if (rank[a] != rank[b] || rank[(a + k) % n] != rank[(b + k) % n]) classes++;
                    next[b] = classes - 1;
                }
                int[] swap = rank; rank = next; next = swap;
                if (classes == n) break;
            }
            return order;
        }

        // Huffman code lengths no longer than most: a symbol never used still gets one, as bzip2 needs a length for each.
        private static int[] Lengths(int[] frequency, int most)
        {
            int count = frequency.Length;
            long[] weight = new long[count];
            for (int i = 0; i < count; i++) weight[i] = frequency[i] + 1;
            while (true)
            {
                List<KeyValuePair<long, List<int>>> nodes = new List<KeyValuePair<long, List<int>>>();
                for (int i = 0; i < count; i++) nodes.Add(new KeyValuePair<long, List<int>>(weight[i], new List<int> { i }));
                int[] depth = new int[count];
                while (nodes.Count > 1)
                {
                    nodes.Sort(delegate(KeyValuePair<long, List<int>> a, KeyValuePair<long, List<int>> b) { return a.Key.CompareTo(b.Key); });
                    KeyValuePair<long, List<int>> x = nodes[0], y = nodes[1];
                    nodes.RemoveRange(0, 2);
                    List<int> joined = new List<int>(x.Value);
                    joined.AddRange(y.Value);
                    foreach (int one in joined) depth[one]++;
                    nodes.Add(new KeyValuePair<long, List<int>>(x.Key + y.Key, joined));
                }
                int deepest = 0;
                foreach (int d in depth) deepest = Math.Max(deepest, d);
                if (deepest <= most) return depth;
                for (int i = 0; i < count; i++) weight[i] = weight[i] / 2 + 1;
            }
        }
    }
}
