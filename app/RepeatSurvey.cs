using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace CapriDesktop
{
    internal sealed partial class Companion
    {
#if CHECKS
        // Every line pool she holds now, packs and both sides included: where it is kept, its name and how many lines. Its
        // sizes only, never its lines, go to pools.tsv.
        private void SmokePoolSurvey(List<string> checks, string output)
        {
            List<string> rows = new List<string> { "holder\tpool\tlines" };
            Sayings.Pool("");
            BindingFlags all = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            Type[] types;
            try { types = typeof(Companion).Assembly.GetTypes(); }
            catch (ReflectionTypeLoadException partly) { types = Array.FindAll(partly.Types, one => one != null); }
            foreach (Type type in types)
                foreach (FieldInfo field in type.GetFields(all))
                {
                    if (field.IsLiteral || type.ContainsGenericParameters) continue;
                    object held;
                    try { held = field.GetValue(null); } catch (Exception) { continue; }
                    if (held == null) continue;
                    if (held is LineFile)
                    {
                        MethodInfo fresh = typeof(LineFile).GetMethod("Fresh", BindingFlags.Instance | BindingFlags.NonPublic);
                        try { if (fresh != null) fresh.Invoke(held, null); } catch (Exception caughtOne) { Brain.Caught(caughtOne); }
                        FieldInfo loaded = typeof(LineFile).GetField("loaded", BindingFlags.Instance | BindingFlags.NonPublic);
                        held = loaded == null ? null : loaded.GetValue(held);
                        if (held == null) continue;
                    }
                    IDictionary map = held as IDictionary;
                    if (map == null) continue;
                    foreach (DictionaryEntry one in map)
                    {
                        string[] lines = one.Value as string[];
                        if (lines == null || !(one.Key is string)) continue;
                        rows.Add(type.Name + "." + field.Name + "\t" + ((string)one.Key).Replace("\t", " ") + "\t" + lines.Length);
                    }
                }
            File.WriteAllLines(Path.Combine(output, "pools.tsv"), rows.ToArray());
            checks.Add("PASS pool survey: " + (rows.Count - 1) + " pools written to pools.tsv (sizes only)");
        }

        // Four hours on her own, a minute at a time, each pool at half again its real rate: a pool with lines to spare that
        // says a core twice fails; a smaller one is named. Counts in long-session.md.
        private void SmokeLongSession(List<string> checks, string output)
        {
            Action back = Sayings.HeldSaid();
            TimeSpan offsetWas = SimTime.Offset;
            Random dice = new Random(7);
            List<KeyValuePair<string, KeyValuePair<double, Func<string>>>> pools = new List<KeyValuePair<string, KeyValuePair<double, Func<string>>>>();
            Dictionary<string, string[]> cores = new Dictionary<string, string[]>();
            Action<string, double, string[], Func<string>> pool = delegate(string name, double perHour, string[] lines, Func<string> fire)
            {
                pools.Add(new KeyValuePair<string, KeyValuePair<double, Func<string>>>(name, new KeyValuePair<double, Func<string>>(perHour * 1.5, fire)));
                cores[name] = lines ?? new string[0];
            };
            pool("missed you", 1.0, Personalities.Lines("missed you", AloneLines), delegate { return Says("ignored", "missed you", AloneLines); });
            pool("uppies", 0.41, Personalities.Lines("uppies", UppiesGotLines), delegate { return Says("uppies", "uppies", UppiesGotLines); });
            pool("praised", 0.3, Sayings.Pool("praised likes it"), delegate { return Says("praised", "praised", HighFiveLines); });
            pool("head pats", 0.58, null, delegate { return Touch.Line("When you touch her head", 5); });
            pool("pop-ins", 0.17, null, delegate { return Moments.Pick(Moments.PopIn, new string[0]); });
            pool("funny", 0.76, null, delegate { return FunnyLine(); });
            pool("water", 0.28, null, delegate { return careLines.Pick("Water"); });
            if (Brain.SenpaiLive) pool("openers", 1.38, null, delegate { return FlirtLine(); });
            Dictionary<string, List<string>> said = new Dictionary<string, List<string>>();
            Dictionary<string, int> repeats = new Dictionary<string, int>();
            try
            {
                for (int minute = 0; minute < 240; minute++)
                {
                    SimTime.Advance(TimeSpan.FromMinutes(1));
                    foreach (KeyValuePair<string, KeyValuePair<double, Func<string>>> one in pools)
                    {
                        if (dice.NextDouble() >= one.Value.Key / 60) continue;
                        string line = null;
                        try { line = one.Value.Value(); } catch (Exception caughtOne) { Brain.Caught(caughtOne); }
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        string key = CoreKey(line, cores[one.Key]);
                        if (!said.ContainsKey(one.Key)) { said[one.Key] = new List<string>(); repeats[one.Key] = 0; }
                        if (said[one.Key].Contains(key)) repeats[one.Key]++;
                        said[one.Key].Add(key);
                    }
                }
            }
            finally
            {
                SimTime.Reset();
                SimTime.Advance(offsetWas);
                back();
            }
            List<string> rows = new List<string> { "| Pool | Fired | Lines | Repeats |", "|---|---|---|---|" }, wrong = new List<string>(), small = new List<string>();
            foreach (KeyValuePair<string, List<string>> one in said)
            {
                int lines = cores[one.Key].Length;
                rows.Add("| " + one.Key + " | " + one.Value.Count + " | " + (lines > 0 ? lines.ToString() : "-") + " | " + repeats[one.Key] + " |");
                if (repeats[one.Key] <= 1) continue;
                if (lines > 0 && lines < one.Value.Count) small.Add(one.Key + " (" + lines + " lines, " + one.Value.Count + " fires)");
                else wrong.Add(one.Key + " said a core again " + repeats[one.Key] + " times in " + one.Value.Count);
            }
            try { File.WriteAllLines(Path.Combine(output, "long-session.md"), rows.ToArray()); } catch (IOException caughtOne) { Brain.Caught(caughtOne); }
            if (wrong.Count > 0) throw new Exception("Four hours on her own repeated: " + string.Join("; ", wrong.ToArray()) + " (long-session.md)");
            checks.Add("PASS four hours on her own, every pool at half again its real rate: no pool says a core twice while it has lines to spare (long-session.md)");
            if (small.Count > 0) checks.Add("LIMIT pools smaller than four hours of their fires, for more lines: " + string.Join(", ", small.ToArray()));
        }

        // A line's core: the pool line inside it (by a stretch from its middle), or the line itself when it has none.
        private static string CoreKey(string line, string[] lines)
        {
            string plain = Plain(line);
            foreach (string one in lines)
            {
                string core = Plain(one);
                if (core.Length < 8) continue;
                string middle = core.Substring(core.Length / 4, Math.Min(14, core.Length / 2));
                if (plain.Contains(middle)) return core;
            }
            return plain;
        }

        private static string Plain(string text)
        {
            System.Text.StringBuilder kept = new System.Text.StringBuilder();
            foreach (char each in text ?? "") if (char.IsLetterOrDigit(each)) kept.Append(char.ToLowerInvariant(each));
            return kept.ToString();
        }
#endif
    }
}
