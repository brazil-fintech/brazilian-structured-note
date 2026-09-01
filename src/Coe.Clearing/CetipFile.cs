using System.Globalization;
using Coe.Core.Text;

namespace Coe.Clearing;

/// <summary>
/// One file ready to be sent to B3, as the <em>ENVIAR ARQUIVOS</em> manual describes it: a
/// header line followed by the record lines of that layout.
/// </summary>
/// <param name="Layout">The manual's section and title, e.g. "4.8.1 Registro COE".</param>
/// <param name="Operation">The operation code the header carries, e.g. <c>0001</c> or <c>FLUX</c>.</param>
/// <param name="FileName">A suggested name; the operational one is agreed with B3 per participant.</param>
/// <param name="Lines">Header first, then the records.</param>
public sealed record CetipFile(string Layout, string Operation, string FileName, IReadOnlyList<string> Lines)
{
    /// <summary>
    /// The file as text. CETIP reads CRLF-terminated lines, including after the last one — a
    /// final record without its terminator is the classic reason an upload arrives one record
    /// short.
    /// </summary>
    public string Content => string.Concat(Lines.Select(line => line + "\r\n"));

    /// <summary>
    /// The bytes to upload. Single-byte encoded, because the layouts count characters and a
    /// UTF-8 "ç" in a commercial name would shift every field after it.
    /// </summary>
    public byte[] ToBytes() => Windows1252.Encode(Content);

    /// <summary>Records, not counting the header.</summary>
    public int RecordCount => Math.Max(0, Lines.Count - 1);

    internal static string Name(string operation, string participant, DateOnly date)
    {
        var cleaned = new string(participant.Where(char.IsLetterOrDigit).ToArray());
        var stamp = date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return $"COE_{operation}_{(cleaned.Length == 0 ? "PARTICIPANTE" : cleaned)}_{stamp}.txt";
    }
}
