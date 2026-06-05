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
using ModernWpf.Controls;
using VideoArchiveManager.App.ViewModels;

namespace VideoArchiveManager.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Saved += () => { DialogResult = true; Close(); };
        viewModel.Cancelled += () => { DialogResult = false; Close(); };
    }

    // Shows the content pane whose name matches the selected nav item's Tag and
    // hides the rest. Keeping all four panes resident (toggling Visibility)
    // rather than swapping a Frame keeps every field bound to the single
    // SettingsViewModel, so the global Save commits all panes at once and an
    // unsaved edit on a pane survives navigating away and back.
    private void SettingsNav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;
        var tag = item.Tag as string;

        PageGeneral.Visibility = tag == "general" ? Visibility.Visible : Visibility.Collapsed;
        PageStorage.Visibility = tag == "storage" ? Visibility.Visible : Visibility.Collapsed;
        PagePlayback.Visibility = tag == "playback" ? Visibility.Visible : Visibility.Collapsed;
        PageReview.Visibility = tag == "review" ? Visibility.Visible : Visibility.Collapsed;
        PageAi.Visibility = tag == "ai" ? Visibility.Visible : Visibility.Collapsed;
    }
}
