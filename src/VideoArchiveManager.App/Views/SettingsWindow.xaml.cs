using System.Windows;
using ModernWpf.Controls;
using VideoArchiveManager.App.ViewModels;

namespace VideoArchiveManager.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Saved += () => { DialogResult = true; Close(); };
        viewModel.Cancelled += () => { DialogResult = false; Close(); };
    }

    // Shows the content pane whose name matches the selected nav item's Tag and
    // hides the rest. Keeping all four panes resident (toggling Visibility)
    // rather than swapping a Frame keeps every field bound to the single
    // SettingsViewModel, so the global Save commits all panes at once and an
    // unsaved edit on a pane survives navigating away and back.
    private void SettingsNav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;
        var tag = item.Tag as string;

        PageGeneral.Visibility = tag == "general" ? Visibility.Visible : Visibility.Collapsed;
        PageStorage.Visibility = tag == "storage" ? Visibility.Visible : Visibility.Collapsed;
        PagePlayback.Visibility = tag == "playback" ? Visibility.Visible : Visibility.Collapsed;
        PageReview.Visibility = tag == "review" ? Visibility.Visible : Visibility.Collapsed;
    }
}
