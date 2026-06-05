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
using VideoArchiveManager.Core.Services;

namespace VideoArchiveManager.App.Views;

/// <summary>
/// Non-modal whole-archive calendar browse window. Renders a year-month heatmap
/// of clip counts by effective shoot date and turns time into a navigation axis:
/// clicking a month lists its clips in the side panel, where "Show in grid" and
/// "Play in app" act on a previewed clip and "Filter grid to this month" scopes
/// the catalog grid to that month. Read-only over the catalog; files untouched.
/// </summary>
public partial class CalendarBrowseWindow : Window
{
    private readonly CalendarBrowseViewModel _viewModel;

    /// <summary>Raised to ask the owner to select a clip in the catalog grid.</summary>
    public event EventHandler<int>? ClipSelected;

    /// <summary>Raised to ask the owner to play a clip in the in-app player.</summary>
    public event EventHandler<int>? PlayRequested;

    /// <summary>
    /// Raised to ask the owner to scope the catalog grid to a date range — from
    /// the "Filter grid to this month" action.
    /// </summary>
    public event EventHandler<(DateTime From, DateTime To)>? FilterToMonthRequested;

    public CalendarBrowseWindow(CalendarBrowseViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        _viewModel.ClipSelected += (_, id) => ClipSelected?.Invoke(this, id);
        _viewModel.PlayRequested += (_, id) => PlayRequested?.Invoke(this, id);
        _viewModel.FilterToMonthRequested += (_, range) => FilterToMonthRequested?.Invoke(this, range);

        Loaded += async (_, _) => await _viewModel.LoadCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Supplies the current grid filter so the window's "current filters" scope
    /// mirrors the catalog grid. Call before <see cref="Window.Show"/>.
    /// </summary>
    public void SetCurrentFilter(SearchQuery? filter) => _viewModel.CurrentFilter = filter;
}
