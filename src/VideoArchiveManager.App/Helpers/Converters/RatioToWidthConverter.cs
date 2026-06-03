using System.Globalization;
using System.Windows.Data;

namespace VideoArchiveManager.App.Helpers.Converters;

// Turns (value, max, trackWidth) into the pixel width of a proportional bar's
// filled portion: clamp(value / max, 0, 1) * trackWidth. Used by the catalog
// statistics dashboard so each breakdown bar fills relative to the largest
// bucket in its group. Returns 0 for any degenerate input so layout never
// throws on a not-yet-measured track (ActualWidth == 0 on first pass).
public class RatioToWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is null || values.Length < 3) return 0d;

        var value = ToDouble(values[0]);
        var max = ToDouble(values[1]);
        var trackWidth = ToDouble(values[2]);

        if (max <= 0 || trackWidth <= 0 || double.IsNaN(trackWidth)) return 0d;

        var fraction = Math.Clamp(value / max, 0d, 1d);
        return fraction * trackWidth;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static double ToDouble(object? o) => o switch
    {
        double d => d,
        int i => i,
        long l => l,
        float f => f,
        _ => 0d
    };
}
