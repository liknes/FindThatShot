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
using System.Windows.Input;
using VideoArchiveManager.App.ViewModels;

namespace VideoArchiveManager.App.Views;

/// <summary>
/// Non-modal moment finder. Searches across every timestamped moment (sub-clip)
/// in the catalog and lets the user jump straight to a shot in the player.
/// Read-only over the catalog; source video files are never touched.
/// </summary>
public partial class MomentSearchWindow : Window
{
    private readonly MomentSearchViewModel _viewModel;

    public MomentSearchWindow(MomentSearchViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        Loaded += async (_, _) => await _viewModel.SearchCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Raised when the user clicks "Jump to" on a result. The owner (MainWindow)
    /// selects the parent clip and seeks the player to the moment's in-point.
    /// </summary>
    public event EventHandler<(int VideoItemId, double StartSeconds)>? JumpRequested
    {
        add => _viewModel.JumpRequested += value;
        remove => _viewModel.JumpRequested -= value;
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (_viewModel.SearchCommand.CanExecute(null))
        {
            _ = _viewModel.SearchCommand.ExecuteAsync(null);
        }
        e.Handled = true;
    }
}
