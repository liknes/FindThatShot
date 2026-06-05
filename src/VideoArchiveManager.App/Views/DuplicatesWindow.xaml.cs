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
using VideoArchiveManager.App.ViewModels;

namespace VideoArchiveManager.App.Views;

/// <summary>
/// Non-modal duplicate finder. Surfaces sets of catalog entries that share the
/// same metadata fingerprint (exact file size + duration + resolution) so the
/// user can prune redundant copies from the catalog. Removal only ever forgets
/// catalog rows + their cached thumbnails — no source video file is read,
/// moved, or deleted.
/// </summary>
public partial class DuplicatesWindow : Window
{
    private readonly DuplicatesViewModel _viewModel;

    public DuplicatesWindow(DuplicatesViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        Loaded += async (_, _) => await _viewModel.LoadCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Raised after the user removes duplicate catalog entries so the owner can
    /// refresh the main grid. Forwarded straight from the view model.
    /// </summary>
    public event EventHandler? CatalogChanged
    {
        add => _viewModel.CatalogChanged += value;
        remove => _viewModel.CatalogChanged -= value;
    }
}
