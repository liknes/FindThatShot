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
