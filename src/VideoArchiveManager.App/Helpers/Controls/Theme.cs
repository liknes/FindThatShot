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
using System.Windows;

namespace VideoArchiveManager.App.Helpers.Controls;

/// <summary>
/// Attached properties used by control templates throughout the app.
///
/// <para>
/// The <see cref="IconProperty"/> attached property carries a Segoe Fluent
/// glyph string (e.g. resources keyed <c>Icon.Play</c>) onto an element so a
/// shared <see cref="System.Windows.Controls.Button"/> template can render an
/// icon next to its content without the call site having to compose
/// <c>StackPanel + TextBlock + ContentPresenter</c> manually.
/// </para>
///
/// <para>
/// Kept as attached props on a single static class instead of building bespoke
/// user controls so existing buttons can adopt the new look with a single
/// XAML attribute (<c>helpers:Theme.Icon="{StaticResource Icon.Play}"</c>)
/// rather than a structural rewrite of every call site.
/// </para>
/// </summary>
public static class Theme
{
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.RegisterAttached(
            "Icon",
            typeof(string),
            typeof(Theme),
            new PropertyMetadata(null));

    public static void SetIcon(DependencyObject element, string? value)
        => element.SetValue(IconProperty, value);

    public static string? GetIcon(DependencyObject element)
        => (string?)element.GetValue(IconProperty);

    public static readonly DependencyProperty IconSizeProperty =
        DependencyProperty.RegisterAttached(
            "IconSize",
            typeof(double),
            typeof(Theme),
            new PropertyMetadata(14.0));

    public static void SetIconSize(DependencyObject element, double value)
        => element.SetValue(IconSizeProperty, value);

    public static double GetIconSize(DependencyObject element)
        => (double)element.GetValue(IconSizeProperty);
}
