using System.Text;

namespace PdnWin.Core.Monitoring;

/// <summary>
/// Turns an AX.25 information field into display lines. From packet-net/packet-term-tui.
/// </summary>
/// <remarks>
/// Packet data is CR-terminated (0x0D), and a node's menu or help text
/// arrives as one information field with CRs *inside* it. Those embedded
/// terminators are real line breaks and have to render as such - mapping
/// them to a placeholder character (which is what happens if you only
/// filter for printability) turns a BBS menu into one unreadable run of
/// text. CR, LF and CRLF are all accepted as the break; the trailing one
/// is dropped rather than producing an empty final line. Anything else
/// outside printable ASCII becomes '.' so a stray byte can't tear the
/// pane's layout.
/// </remarks>
public static class ReceivedText
{
    /// <summary>
    /// One information field's worth of text: its display
    /// <paramref name="Lines"/>, and whether the field ran out
    /// mid-line.
    /// </summary>
    /// <param name="Lines">The lines the field carried.</param>
    /// <param name="Incomplete">
    /// <c>true</c> when the field ended on something other than a line
    /// terminator, i.e. the peer's line continues into the next frame. A
    /// line longer than the link's PACLEN is segmented across frames, so
    /// this is how a 300-character BBS line is put back together instead
    /// of being shown as two.
    /// </param>
    public readonly record struct Chunk(IReadOnlyList<string> Lines, bool Incomplete);

    /// <summary>
    /// Split <paramref name="info"/> into display lines, reporting
    /// whether the last one is still open.
    /// </summary>
    public static Chunk Split(ReadOnlySpan<byte> info)
        => new(ToLines(info), info.Length > 0 && info[^1] is not (0x0D or 0x0A));

    /// <summary>
    /// Split <paramref name="info"/> into display lines. Returns an empty
    /// list for a field that holds nothing but line terminators.
    /// </summary>
    public static IReadOnlyList<string> ToLines(ReadOnlySpan<byte> info)
    {
        int end = info.Length;
        while (end > 0 && (info[end - 1] == 0x0D || info[end - 1] == 0x0A)) end--;
        if (end == 0) return [];

        var lines = new List<string>();
        var sb = new StringBuilder(end);
        for (int i = 0; i < end; i++)
        {
            byte b = info[i];
            if (b is 0x0D or 0x0A)
            {
                // CRLF is one break, not two.
                if (b == 0x0D && i + 1 < end && info[i + 1] == 0x0A) i++;
                lines.Add(sb.ToString());
                sb.Clear();
                continue;
            }
            sb.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
        }
        lines.Add(sb.ToString());
        return lines;
    }
}
