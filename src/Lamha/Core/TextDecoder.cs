using System.Text;

namespace Lamha.Core;

public static class TextDecoder
{
    static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    static TextDecoder() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static (string Text, string EncodingName) Decode(byte[] b, int length)
    {
        if (length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return (Encoding.UTF8.GetString(b, 3, length - 3), "UTF-8");
        if (length >= 4 && b[0] == 0xFF && b[1] == 0xFE && b[2] == 0 && b[3] == 0) return (Encoding.UTF32.GetString(b, 4, length - 4), "UTF-32");
        if (length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return (Encoding.Unicode.GetString(b, 2, length - 2), "UTF-16");
        if (length >= 2 && b[0] == 0xFE && b[1] == 0xFF) return (Encoding.BigEndianUnicode.GetString(b, 2, length - 2), "UTF-16");

        if (LooksLikeUtf16(b, length, out bool bigEndian))
            return ((bigEndian ? Encoding.BigEndianUnicode : Encoding.Unicode).GetString(b, 0, length), "UTF-16");

        try { return (StrictUtf8.GetString(b, 0, TrimIncompleteUtf8(b, length)), "UTF-8"); }
        catch (DecoderFallbackException) { }

        // Not Unicode: Windows-1256 (Arabic) and 1252 (French) share the byte range. Arabic text is mostly
        // high bytes, while French accents are sparse among ASCII letters.
        int high = 0, letters = 0;
        for (int i = 0; i < length; i++)
        {
            byte c = b[i];
            if (c >= 0x80) { high++; letters++; }
            else if (c is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z') letters++;
        }
        int cp = letters > 0 && high * 100 / letters >= 40 ? 1256 : 1252;
        return (Encoding.GetEncoding(cp).GetString(b, 0, length), $"Windows-{cp}");
    }

    static bool LooksLikeUtf16(byte[] b, int length, out bool bigEndian)
    {
        int n = Math.Min(length, 4096) & ~1, oddNuls = 0, evenNuls = 0;
        for (int i = 0; i < n; i += 2)
        {
            if (b[i] == 0) evenNuls++;
            if (b[i + 1] == 0) oddNuls++;
        }
        bigEndian = evenNuls > oddNuls;
        return n >= 2 && Math.Max(evenNuls, oddNuls) * 4 > n;
    }

    // A read capped at N bytes can cut the last character in half; that must not fail UTF-8 validation.
    static int TrimIncompleteUtf8(byte[] b, int length)
    {
        for (int back = 1; back <= 3 && back <= length; back++)
        {
            byte c = b[length - back];
            if (c < 0x80) return length;
            if (c >= 0xC0)
            {
                int need = c >= 0xF0 ? 4 : c >= 0xE0 ? 3 : 2;
                return need > back ? length - back : length;
            }
        }
        return length;
    }

    public static bool IsMostlyRtl(string text)
    {
        int rtl = 0, ltr = 0;
        foreach (char c in text.AsSpan(0, Math.Min(text.Length, 8000)))
        {
            if (c is >= '֐' and <= 'ࣿ' or >= 'יִ' and <= '﷿' or >= 'ﹰ' and <= '﻿') rtl++;
            else if (char.IsLetter(c)) ltr++;
        }
        return rtl > 0 && rtl >= ltr;
    }
}
