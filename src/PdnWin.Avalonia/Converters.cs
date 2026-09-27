using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using PdnWin.Core.Monitoring;
using PdnWin.Core.Sessions;
using PdnWin.ViewModels;

namespace PdnWin.Ava;

/// <summary>
/// Colours by kind. WPF does this with data triggers; Avalonia has none, so each is a converter.
/// </summary>
internal static class Converters
{
    /// <summary>A monitor header's colour from its frame kind.</summary>
    public static readonly IValueConverter FrameKindBrush = new FuncValueConverter<FrameKind, IBrush>(kind => kind switch
    {
        FrameKind.Information => Brush.Parse("#8CCBF5"),
        FrameKind.Supervisory => Palette.AmberDim,
        FrameKind.Unnumbered => Brush.Parse("#B07DE8"),
        FrameKind.UnnumberedInformation => Brush.Parse("#7BD88F"),
        FrameKind.Reject => Brush.Parse("#F27E7E"),
        _ => Palette.Muted,
    });

    /// <summary>A transcript line's colour: theirs ink, ours amber, notices muted.</summary>
    public static readonly IValueConverter LineKindBrush = new FuncValueConverter<SessionLineKind, IBrush>(kind => kind switch
    {
        SessionLineKind.Sent => Palette.Amber,
        SessionLineKind.Notice => Palette.Muted,
        _ => Palette.Ink,
    });

    /// <summary>Notices are italic.</summary>
    public static readonly IValueConverter LineKindStyle = new FuncValueConverter<SessionLineKind, FontStyle>(kind =>
        kind == SessionLineKind.Notice ? FontStyle.Italic : FontStyle.Normal);

    /// <summary>A session tab's dot: green up, amber in between, grey down.</summary>
    public static readonly IValueConverter LinkStateBrush = new FuncValueConverter<LinkState, IBrush>(state => state switch
    {
        LinkState.Connected => Brush.Parse("#7BD88F"),
        LinkState.Connecting or LinkState.Disconnecting => Palette.Amber,
        _ => Brush.Parse("#3E4855"),
    });

    /// <summary>"12.3 dB".</summary>
    public static readonly IValueConverter Db = new FuncValueConverter<double, string>(db => db.ToString("0.0 'dB'", CultureInfo.InvariantCulture));
}
