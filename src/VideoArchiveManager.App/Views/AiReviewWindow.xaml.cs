using System.Windows;
using System.Windows.Controls;
using VideoArchiveManager.App.ViewModels;

namespace VideoArchiveManager.App.Views;

/// <summary>
/// Non-modal review queue for AI-generated tag suggestions. Each pending
/// suggestion is an accept/reject chip; accepting promotes it to a real tag,
/// rejecting remembers the dismissal. Source video files are never touched.
/// </summary>
public partial class AiReviewWindow : Window
{
    private readonly AiReviewViewModel _viewModel;

    public AiReviewWindow(AiReviewViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        Loaded += async (_, _) => await _viewModel.LoadCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Raised after a suggestion is accepted (which creates a real tag) so the
    /// owner can refresh the main grid / sidebar tag list.
    /// </summary>
    public event EventHandler? TagsChanged
    {
        add => _viewModel.TagsChanged += value;
        remove => _viewModel.TagsChanged -= value;
    }

    /// <summary>
    /// Raised when the user clicks a suggestion's tag to verify it: the owner
    /// (MainWindow) selects the parent clip and seeks the player to the tag's
    /// best-scoring frame.
    /// </summary>
    public event EventHandler<(int VideoItemId, double Seconds)>? JumpRequested
    {
        add => _viewModel.JumpRequested += value;
        remove => _viewModel.JumpRequested -= value;
    }

    // The chip's hover preview is generated lazily the first time its tooltip
    // opens, so opening the window never extracts frames up front.
    private void Chip_ToolTipOpening(object sender, ToolTipEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AiReviewChipViewModel chip })
        {
            _ = chip.EnsurePreviewAsync();
        }
    }
}
