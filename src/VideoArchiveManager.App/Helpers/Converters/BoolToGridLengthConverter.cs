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
