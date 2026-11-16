// Siemens documentation access, ported from siemens-docs-mcp (MIT, Copyright (c) 2026 Łukasz Czarnacki).
// See THIRD_PARTY_NOTICES.md.
using System;
using System.Collections.Generic;
using System.Linq;

namespace TiaOpennessMcp.Docs
{
    /// <summary>Small text helpers shared by the docs tools.</summary>
    public static class DocsText
    {
        /// <summary>Python-style slice: clamps both ends instead of throwing.</summary>
        public static string Slice(string s, int start, int end = int.MaxValue)
        {
            if (start < 0) start = 0;
            if (end > s.Length) end = s.Length;
            return end <= start ? "" : s.Substring(start, end - start);
        }

        /// <summary>Python str.rfind(ch, start, end): last index of ch in s[start:end], or -1.</summary>
        public static int RFind(string s, char ch, int start, int end)
        {
            if (start < 0) start = 0;
            if (end > s.Length) end = s.Length;
            for (int i = end - 1; i >= start; i--)
                if (s[i] == ch) return i;
            return -1;
        }

        /// <summary>Collapses runs of whitespace to one space and trims.</summary>
        public static string CollapseWhitespace(string text)
        {
            return string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }

        /// <summary>Similar strings from <paramref name="possibilities"/>, best first (like Python difflib.get_close_matches).</summary>
        public static List<string> CloseMatches(string word, IEnumerable<string> possibilities, int n = 3, double cutoff = 0.6)
        {
            return possibilities
                .Select(p => (Score: Ratio(word, p), Value: p))
                .Where(x => x.Score >= cutoff)
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Value, StringComparer.Ordinal)
                .Take(n)
                .Select(x => x.Value)
                .ToList();
        }

        /// <summary>Ratcliff/Obershelp similarity 2*M/T, the measure difflib.SequenceMatcher.ratio() uses.</summary>
        public static double Ratio(string a, string b)
        {
            int total = a.Length + b.Length;
            return total == 0 ? 1.0 : 2.0 * Matches(a, 0, a.Length, b, 0, b.Length) / total;
        }

        private static int Matches(string a, int alo, int ahi, string b, int blo, int bhi)
        {
            if (alo >= ahi || blo >= bhi) return 0;
            int bestI = alo, bestJ = blo, bestSize = 0;
            var lengths = new int[bhi - blo + 1];
            for (int i = alo; i < ahi; i++)
            {
                var next = new int[bhi - blo + 1];
                for (int j = blo; j < bhi; j++)
                {
                    if (a[i] != b[j]) continue;
                    int k = lengths[j - blo] + 1;
                    next[j - blo + 1] = k;
                    if (k > bestSize)
                    {
                        bestI = i - k + 1;
                        bestJ = j - k + 1;
                        bestSize = k;
                    }
                }
                lengths = next;
            }
            if (bestSize == 0) return 0;
            return bestSize
                   + Matches(a, alo, bestI, b, blo, bestJ)
                   + Matches(a, bestI + bestSize, ahi, b, bestJ + bestSize, bhi);
        }
    }
}
