using Packet.Core;

namespace PdnWin.Core.Sessions;

/// <summary>What a line typed with no session selected means.</summary>
public abstract record InputCommand
{
    /// <summary>Connect to a station.</summary>
    public sealed record Connect(Callsign Remote) : InputCommand;

    /// <summary>Send an unproto (UI) line.</summary>
    public sealed record Unproto(Callsign Destination, IReadOnlyList<Callsign> Via, string Text) : InputCommand;

    /// <summary>Not understood; <see cref="Message"/> says why.</summary>
    public sealed record Invalid(string Message) : InputCommand;

    /// <summary>
    /// Parses what the operator typed in the console (no session selected), in the TNC dialect
    /// packet operators already know:
    /// <c>C GB7RDG</c> or <c>CONNECT GB7RDG</c> connects;
    /// <c>U CQ via GB7RDG hello</c> style unproto is <c>U DEST[,DIGI...] text</c>.
    /// </summary>
    public static InputCommand Parse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        string[] words = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return new Invalid("type C CALLSIGN to connect, or U DEST text for unproto");
        }

        string verb = words[0].ToUpperInvariant();
        string rest = words.Length > 1 ? words[1].Trim() : string.Empty;
        switch (verb)
        {
            case "C" or "CONNECT":
                string[] target = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (target.Length == 0 || !Callsign.TryParse(target[0].ToUpperInvariant(), out Callsign remote))
                {
                    return new Invalid("connect to whom? C CALLSIGN[-SSID]");
                }

                if (target.Length > 1)
                {
                    return new Invalid("connecting via digipeaters is not supported yet; connect direct");
                }

                return new Connect(remote);

            case "U" or "UNPROTO":
                string[] parts = rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                {
                    return new Invalid("U DEST[,DIGI,...] text");
                }

                string[] path = parts[0].ToUpperInvariant().Split(',', StringSplitOptions.RemoveEmptyEntries);
                var calls = new List<Callsign>(path.Length);
                foreach (string call in path)
                {
                    if (!Callsign.TryParse(call, out Callsign parsed))
                    {
                        return new Invalid($"'{call}' is not a callsign");
                    }

                    calls.Add(parsed);
                }

                return new Unproto(calls[0], calls.Skip(1).ToList(), parts[1]);

            default:
                return new Invalid($"'{words[0]}' is not a command here: C CALLSIGN connects, U DEST text sends unproto");
        }
    }
}
