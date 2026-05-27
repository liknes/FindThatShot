using System.Windows;
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
}
