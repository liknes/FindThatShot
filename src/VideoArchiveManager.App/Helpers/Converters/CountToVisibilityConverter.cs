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
