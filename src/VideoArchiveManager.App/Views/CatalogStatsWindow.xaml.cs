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
/// Non-modal, read-only catalog statistics dashboard. Surfaces aggregate
/// metrics (clip counts, total size/runtime, status / rating / resolution /
/// camera / codec / tag / year breakdowns) computed from the catalog database.
/// No source video file is read or modified to build it.
/// </summary>
public partial class CatalogStatsWindow : Window
{
    private readonly CatalogStatsViewModel _viewModel;

    public CatalogStatsWindow(CatalogStatsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        // Kick the first load once the window is up so the spinner shows while
        // the aggregate queries run, rather than blocking the open.
        Loaded += async (_, _) => await _viewModel.LoadCommand.ExecuteAsync(null);
    }
}
