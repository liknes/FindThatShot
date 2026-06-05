// Find That Shot - organize and search a large local video archive.
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Ingve Moss Liknes <findthatshot@ingve.no>
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace VideoArchiveManager.App.Helpers.Converters;

/// <summary>
/// Maps a heatmap cell's normalized intensity (0..1, a month's clip count over
/// the busiest month) to a fill brush derived from the app accent. Empty months
/// (intensity == 0) render transparent so the cell shows the inset background;
/// non-empty months step through GitHub-contributions-style opacity buckets so
/// busier months read brighter. Accent-based, so it works in any theme.
/// </summary>
public sealed class HeatIntensityToBrushConverter : IValueConverter
{
    // App accent (warm amber, App.Accent.Color) — kept in sync with Colors.xaml.
    private static readonly Color Accent = Color.FromRgb(0xF5, 0xA6, 0x23);

    // Four non-empty buckets so even a "1 of many" month stays visible.
    private static readonly double[] BucketOpacities = { 0.22, 0.45, 0.70, 1.0 };

    private static readonly SolidColorBrush Empty = CreateFrozen(Colors.Transparent);
    private static readonly SolidColorBrush[] Buckets =
        BucketOpacities.Select(o => CreateFrozen(WithAlpha(Accent, o))).ToArray();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var intensity = value switch
        {
            double d => d,
            float f => f,
            _ => 0d
        };

        if (intensity <= 0) return Empty;

        intensity = Math.Clamp(intensity, 0d, 1d);
        var index = (int)Math.Ceiling(intensity * Buckets.Length) - 1;
        index = Math.Clamp(index, 0, Buckets.Length - 1);
        return Buckets[index];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static Color WithAlpha(Color color, double opacity)
        => Color.FromArgb((byte)Math.Round(Math.Clamp(opacity, 0d, 1d) * 255), color.R, color.G, color.B);

    private static SolidColorBrush CreateFrozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
