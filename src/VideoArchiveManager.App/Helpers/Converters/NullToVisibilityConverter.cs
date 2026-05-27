using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VideoArchiveManager.App.Helpers.Converters;

public class NullToVisibilityConverter : IValueConverter
{
    public Visibility WhenNull { get; set; } = Visibility.Collapsed;
    public Visibility WhenNotNull { get; set; } = Visibility.Visible;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null ? WhenNull : WhenNotNull;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
