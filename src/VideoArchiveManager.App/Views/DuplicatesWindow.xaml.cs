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
