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
