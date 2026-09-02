using System.Text;

namespace Coe.Core.Text;

/// <summary>
/// The single-byte encoding CETIP reads and writes.
///
/// It is usually called Latin-1, and is not: figure names such as "COE de Credito - CDS com
/// Amortizacao" carry byte 0x96, an en dash in Windows-1252 and an unassigned control character
/// in ISO-8859-1. .NET ships no single-byte code page beyond Latin-1 without pulling in
/// System.Text.Encoding.CodePages, so the thirty-two positions where the two differ are mapped
/// here - the whole of the difference, in a table anyone can check against the code page.
///
/// Both directions matter: B3's exports are decoded on the way in, and every upload file is
/// encoded on the way out. A fixed-width layout counts characters, so one byte per character is
/// not a detail - a single multi-byte character moves every field after it out of position.
/// </summary>
public static class Windows1252
{
    private static readonly char[] HighRange = BuildHighRange();

    /// <summary>Substituted for a character the encoding cannot represent.</summary>
    public const char Replacement = '?';

    private static char[] BuildHighRange()
    {
        // Code points for bytes 0x80 to 0x9F. The five the code page leaves undefined - 0x81,
        // 0x8D, 0x8F, 0x90 and 0x9D - keep the control character Latin-1 decodes them to, so a
        // byte is never silently lost.
        int[] points =
        [
            0x20AC, 0x0081, 0x201A, 0x0192, 0x201E, 0x2026, 0x2020, 0x2021,
            0x02C6, 0x2030, 0x0160, 0x2039, 0x0152, 0x008D, 0x017D, 0x008F,
            0x0090, 0x2018, 0x2019, 0x201C, 0x201D, 0x2022, 0x2013, 0x2014,
            0x02DC, 0x2122, 0x0161, 0x203A, 0x0153, 0x009D, 0x017E, 0x0178
        ];
        return Array.ConvertAll(points, point => (char)point);
    }

    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        var chars = new char[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
        {
            var b = bytes[i];
            chars[i] = b is >= 0x80 and <= 0x9F ? HighRange[b - 0x80] : (char)b;
        }
        return new string(chars);
    }

    /// <summary>
    /// One byte per character. A character outside the encoding becomes
    /// <see cref="Replacement"/> rather than throwing: an unrepresentable character in a
    /// commercial name should cost a question mark in one field, not the whole upload.
    /// </summary>
    public static byte[] Encode(string text)
    {
        var bytes = new byte[text.Length];
        for (var i = 0; i < text.Length; i++) bytes[i] = EncodeChar(text[i]);
        return bytes;
    }

    private static byte EncodeChar(char c)
    {
        if (c <= 0x7F || c is >= (char)0xA0 and <= (char)0xFF) return (byte)c;

        var index = Array.IndexOf(HighRange, c);
        if (index >= 0) return (byte)(0x80 + index);

        // Strip the mark and keep the letter: a character that only exists decomposed is not
        // representable, and losing its accent beats losing the letter under it.
        foreach (var part in c.ToString().Normalize(NormalizationForm.FormD))
            if (part <= 0x7F) return (byte)part;

        return (byte)Replacement;
    }
}
