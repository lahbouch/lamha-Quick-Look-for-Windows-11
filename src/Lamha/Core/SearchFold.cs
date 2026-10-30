using System.Text;

namespace Lamha.Core;

/// <summary>
/// Makes Arabic text searchable however a PDF stored it. Many PDFs hold presentation forms (U+FE97) rather than base
/// letters (U+062A), and readers do not type tashkeel or kashida, so all of these fold to the plain letters.
/// </summary>
public static class SearchFold
{
    /// <summary>Returns the folded text and, for each folded character, its index in the original.</summary>
    public static (string Text, int[] Map) Fold(string text)
    {
        var sb = new StringBuilder(text.Length);
        var map = new List<int>(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c is 'ـ' or >= 'ً' and <= 'ْ') continue;
            if (c is >= 'ﭐ' and <= '﷿' or >= 'ﹰ' and <= 'ﻼ')
            {
                foreach (char n in c.ToString().Normalize(NormalizationForm.FormKC))
                {
                    if (n is 'ـ' or >= 'ً' and <= 'ْ' or ' ') continue;
                    sb.Append(n);
                    map.Add(i);
                }
            }
            else
            {
                sb.Append(c);
                map.Add(i);
            }
        }
        return (sb.ToString(), map.ToArray());
    }

    public static IEnumerable<(int Start, int Count)> Find(string text, string query)
    {
        var (folded, map) = Fold(text);
        var needle = Fold(query).Text;
        if (needle.Length == 0) yield break;

        foreach (var hit in Scan(folded, map, needle)) yield return hit;

        // Some PDFs store Arabic in visual order, which reaches us letter-reversed.
        var reversed = new string(needle.Reverse().ToArray());
        if (reversed != needle && needle.Any(c => c is >= '\u0600' and <= '\u06FF'))
            foreach (var hit in Scan(folded, map, reversed)) yield return hit;
    }

    static IEnumerable<(int Start, int Count)> Scan(string folded, int[] map, string needle)
    {
        for (int at = folded.IndexOf(needle, StringComparison.OrdinalIgnoreCase); at >= 0;
             at = folded.IndexOf(needle, at + needle.Length, StringComparison.OrdinalIgnoreCase))
        {
            int start = map[at];
            yield return (start, map[at + needle.Length - 1] - start + 1);
        }
    }
}
