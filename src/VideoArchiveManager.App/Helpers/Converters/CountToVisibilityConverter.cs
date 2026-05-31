using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VideoArchiveManager.App.Helpers.Converters;

/// <summary>
/// Maps an integer count (or any <see cref="ICollection"/> / <see cref="IEnumerable"/>)
/// to a <see cref="Visibility"/>. Used by empty-state hosts to show themselves when
/// a bound collection is empty (or when the bound count equals zero).
///
/// <para>
/// Set <see cref="ShowWhenZero"/> to <c>true</c> (the default) for the empty-state
/// element itself, or to <c>false</c> for the "real content" element that should
/// hide while the empty state is up.
/// </para>
/// </summary>
public class CountToVisibilityConverter : IValueConverter
{
    public bool ShowWhenZero { get; set; } = true;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isZero = value switch
        {
            null => true,
            int i => i == 0,
            ICollection c => c.Count == 0,
            IEnumerable e => !e.Cast<object>().Any(),
            _ => false,
        };
        return isZero == ShowWhenZero ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
