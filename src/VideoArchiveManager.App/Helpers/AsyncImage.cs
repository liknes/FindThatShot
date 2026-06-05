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
using System.Windows.Controls;

namespace VideoArchiveManager.App.Helpers;

// Attached behavior that loads an Image's source off the UI thread from a file
// path. Designed for virtualized lists (e.g. the AI review queue): only realized
// containers carry an Image, and recycling simply changes the bound path, which
// re-triggers the async load (served instantly from ThumbnailLoader's cache on a
// revisit). Pointing this at a path instead of binding Image.Source to a
// synchronous decode keeps the dispatcher free while hundreds of cards scroll.
public static class AsyncImage
{
    public static readonly DependencyProperty SourcePathProperty =
        DependencyProperty.RegisterAttached(
            "SourcePath",
            typeof(string),
            typeof(AsyncImage),
            new PropertyMetadata(null, OnSourcePathChanged));

    public static void SetSourcePath(DependencyObject element, string? value) =>
        element.SetValue(SourcePathProperty, value);

    public static string? GetSourcePath(DependencyObject element) =>
        (string?)element.GetValue(SourcePathProperty);

    private static async void OnSourcePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Image image) return;

        var path = e.NewValue as string;

        // Clear immediately so a recycled container never flashes the previous
        // clip's thumbnail while the new one decodes.
        image.Source = null;
        if (string.IsNullOrWhiteSpace(path)) return;

        var bitmap = await ThumbnailLoader.LoadAsync(path);

        // The container may have been recycled to a different clip while we were
        // decoding; only apply if this Image is still pointed at the same path.
        if (bitmap is not null && GetSourcePath(image) == path)
        {
            image.Source = bitmap;
        }
    }
}
