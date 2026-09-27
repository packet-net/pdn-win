using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace PdnWin.Ava;

/// <summary>
/// The pdn-soundmodem station page's palette (waterfall.html :root), for code that draws. The same
/// colours are resources in Styles/Theme.axaml for markup.
/// </summary>
internal static class Palette
{
    public static readonly Color BgColor = Color.Parse("#0A0D11");
    public static readonly Color PanelColor = Color.Parse("#11161D");
    public static readonly Color Panel2Color = Color.Parse("#0C1016");
    public static readonly Color EdgeColor = Color.Parse("#1E2632");
    public static readonly Color EdgeHiColor = Color.Parse("#2A3441");
    public static readonly Color InkColor = Color.Parse("#D9E2EA");
    public static readonly Color MutedColor = Color.Parse("#7C8894");
    public static readonly Color AmberColor = Color.Parse("#F5A83C");
    public static readonly Color AmberDimColor = Color.Parse("#B97F2E");
    public static readonly Color TraceColor = Color.Parse("#56C8DD");

    public static readonly IImmutableSolidColorBrush Bg = new ImmutableSolidColorBrush(BgColor);
    public static readonly IImmutableSolidColorBrush Panel = new ImmutableSolidColorBrush(PanelColor);
    public static readonly IImmutableSolidColorBrush Panel2 = new ImmutableSolidColorBrush(Panel2Color);
    public static readonly IImmutableSolidColorBrush Edge = new ImmutableSolidColorBrush(EdgeColor);
    public static readonly IImmutableSolidColorBrush EdgeHi = new ImmutableSolidColorBrush(EdgeHiColor);
    public static readonly IImmutableSolidColorBrush Ink = new ImmutableSolidColorBrush(InkColor);
    public static readonly IImmutableSolidColorBrush Muted = new ImmutableSolidColorBrush(MutedColor);
    public static readonly IImmutableSolidColorBrush Amber = new ImmutableSolidColorBrush(AmberColor);
    public static readonly IImmutableSolidColorBrush AmberDim = new ImmutableSolidColorBrush(AmberDimColor);
    public static readonly IImmutableSolidColorBrush Trace = new ImmutableSolidColorBrush(TraceColor);
    public static readonly IImmutableSolidColorBrush HoverFill = new ImmutableSolidColorBrush(Color.Parse("#1A212B"));

    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas, DejaVu Sans Mono, Liberation Mono, monospace");
}
