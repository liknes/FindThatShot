using System.Windows;
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
}
