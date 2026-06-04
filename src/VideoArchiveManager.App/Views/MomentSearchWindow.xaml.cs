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
