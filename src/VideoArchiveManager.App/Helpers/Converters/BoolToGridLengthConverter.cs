using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VideoArchiveManager.App.Helpers.Converters;

/// <summary>
/// Maps a <see cref="bool"/> onto a <see cref="GridLength"/> so a row's
/// height can flip between star-sized (proportional) and Auto (fits content)
/// based on a view-model flag.
///
/// <para>
/// Used by the rail's outer Grid in <c>MainWindow.xaml</c> to give expanded
/// sidebar panels (<c>FOLDERS</c> / <c>TAGS</c> / <c>CAMERAS</c>) a
/// proportional share of the available rail height while collapsed panels
/// shrink to a single header row. When two panels are collapsed, the third
/// gets all the freed space — the actual "Lightroom feel" the user is after.
/// </para>
///
/// <para>
/// <c>StarFactor</c> defaults to 1, so when multiple rows are bound through
/// this converter and all are <c>true</c>, they share the available space
/// equally. Set per-row to weight one panel heavier than others.
/// </para>
/// </summary>
public class BoolToGridLengthConverter : IValueConverter
{
    /// <summary>
    /// Star multiplier emitted when the source bool is <c>true</c>.
    /// </summary>
    public double StarFactor { get; set; } = 1d;

    /// <summary>
    /// When <c>true</c>, the converter inverts: <c>false</c> → star,
    /// <c>true</c> → Auto. Useful when the underlying flag is named
    /// "IsCollapsed" rather than "IsExpanded".
    /// </summary>
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var on = value is bool bv && bv;
        if (Invert) on = !on;
        return on
            ? new GridLength(StarFactor, GridUnitType.Star)
            : GridLength.Auto;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
