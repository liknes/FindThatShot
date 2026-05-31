using System.Globalization;
using System.Windows.Data;

namespace VideoArchiveManager.App.Helpers.Converters;

/// <summary>
/// Converts a width (double) into a height by multiplying by an aspect ratio.
/// Used to keep thumbnail tiles at a fixed 16:9 shape as the cards stretch to
/// fill the catalog grid. Default ratio is 9/16 (= 0.5625); pass a different
/// ratio via <see cref="Ratio"/> or as a string converter parameter.
/// </summary>
public class WidthToAspectHeightConverter : IValueConverter
{
    public double Ratio { get; set; } = 9.0 / 16.0;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double w || double.IsNaN(w) || double.IsInfinity(w) || w <= 0)
        {
            return 0.0;
        }

        double ratio = Ratio;
        if (parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
        {
            ratio = parsed;
        }
        else if (parameter is double pd && pd > 0)
        {
            ratio = pd;
        }

        return w * ratio;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
