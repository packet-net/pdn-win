namespace PdnWin.Core.Stations.SoundModem;

/// <summary>
/// The soundmodem modes that work through an FM radio's microphone and speaker, which is what a
/// handheld on an AIOC offers. The 9600 port modes (fsk9600, c4fsk*) need a flat data socket and
/// the HF modes need SSB, so neither is offered for this kind of station yet.
/// </summary>
public static class FmModes
{
    /// <summary>The default: 1200 baud AFSK, what APRS and most VHF packet runs.</summary>
    public const string Default = "afsk1200";

    /// <summary>Each mode with a one-line description for the picker.</summary>
    public static IReadOnlyList<(string Mode, string Description)> All { get; } =
    [
        ("afsk1200", "1200 AFSK, AX.25 - VHF packet and APRS"),
        ("afsk1200-multi", "1200 AFSK, AX.25 - with an offset-diversity receive bank"),
        ("afsk1200-il2p", "1200 AFSK, IL2P+CRC - NinoTNC mode 0111, Dire Wolf IL2P"),
        ("afsk1200-fx25", "1200 AFSK, AX.25 + FX.25 FEC - Dire Wolf FX.25"),
        ("afsk1200-fx25rx", "1200 AFSK, AX.25, FX.25 on receive only"),
        ("qpsk3600", "3600 QPSK, IL2P+CRC - NinoTNC; 5 kHz deviation, a 25 kHz channel"),
    ];

    /// <summary>The mode names alone.</summary>
    public static IReadOnlyList<string> Names { get; } = All.Select(m => m.Mode).ToList();
}
