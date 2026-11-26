#if CHECKS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace CapriDesktop
{
    // Her rules alone, no brain, over a labelled file of messages: whether each is heard as a tool ask (Hands.Heard, or
    // Hands.Wants as her call path asks), which tool, and how long it took. For measuring a router against them.
    internal static class RouteTest
    {
        // In: "label<TAB>message" a line. Out: label, message, "tool" or "chat", the tool heard, microseconds.
        internal static int Run(string input, string output)
        {
            List<string> rows = new List<string> { "label\tmessage\trules\theard\tmicroseconds" };
            foreach (string line in File.ReadAllLines(input, Encoding.UTF8))
            {
                int tab = line.IndexOf('\t');
                if (tab <= 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                string label = line.Substring(0, tab), said = line.Substring(tab + 1);
                Stopwatch took = Stopwatch.StartNew();
                string tool = null;
                ToolArguments args;
                bool heard = false, wants = false;
                try
                {
                    heard = Hands.Heard(said, out tool, out args);
                    wants = !heard && Hands.Wants(said);
                }
                catch (Exception caughtOne) { tool = "error: " + caughtOne.GetType().Name; }
                took.Stop();
                rows.Add(label + "\t" + said + "\t" + (heard || wants ? "tool" : "chat") + "\t" + (heard ? tool : wants ? "(wants)" : "") + "\t" + (long)(took.Elapsed.TotalMilliseconds * 1000));
            }
            File.WriteAllLines(output, rows.ToArray(), new UTF8Encoding(false));
            return 0;
        }
    }
}
#endif
