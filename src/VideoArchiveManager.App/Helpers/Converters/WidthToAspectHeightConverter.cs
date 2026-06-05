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
