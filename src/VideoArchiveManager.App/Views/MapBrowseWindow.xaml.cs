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
/// Non-modal whole-archive map browse window. Plots every geotagged clip as
/// clustered Leaflet markers and turns location into a navigation axis:
/// clicking a cluster (or "Filter grid to this view") scopes the catalog grid
/// to those clips, and clicking a marker previews the clip with "Show in grid"
/// / "Play in app" actions. Read-only over the catalog; source files untouched.
/// </summary>
public partial class MapBrowseWindow : Window
{
    private readonly MapBrowseViewModel _viewModel;

    /// <summary>Raised to ask the owner to select a clip in the catalog grid.</summary>
    public event EventHandler<int>? ClipSelected;

    /// <summary>Raised to ask the owner to play a clip in the in-app player.</summary>
    public event EventHandler<int>? PlayRequested;

    /// <summary>
    /// Raised to ask the owner to scope the catalog grid to a set of clips —
    /// from a cluster click or the "Filter grid to this view" toolbar action.
    /// </summary>
    public event EventHandler<IReadOnlyList<int>>? FilterToClipsRequested;

    public MapBrowseWindow(MapBrowseViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        // VM -> map control: re-plot whenever a load completes (initial open,
        // scope toggle).
        _viewModel.PointsLoaded += (_, points) => MapView.SetPoints(points);

        // Side-panel buttons -> owner.
        _viewModel.ClipSelected += (_, id) => ClipSelected?.Invoke(this, id);
        _viewModel.PlayRequested += (_, id) => PlayRequested?.Invoke(this, id);

        // Map control interactions.
        MapView.ClipSelected += (_, id) =>
        {
            // Marker click both fills the side preview and selects in the grid.
            _viewModel.ShowPreview(id);
            ClipSelected?.Invoke(this, id);
        };
        MapView.FilterToClipsRequested += (_, ids) => FilterToClipsRequested?.Invoke(this, ids);

        Loaded += async (_, _) => await _viewModel.LoadCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Supplies the current grid filter so the window's "current filters" scope
    /// mirrors the catalog grid. Call before <see cref="Window.Show"/>.
    /// </summary>
    public void SetCurrentFilter(SearchQuery? filter) => _viewModel.CurrentFilter = filter;

    private async void FilterToView_Click(object sender, RoutedEventArgs e)
    {
        var ids = await MapView.GetVisibleIdsAsync();
        if (ids.Count > 0)
        {
            FilterToClipsRequested?.Invoke(this, ids);
        }
    }
}
