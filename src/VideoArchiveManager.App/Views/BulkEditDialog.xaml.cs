using System.Windows;
using VideoArchiveManager.App.ViewModels;

namespace VideoArchiveManager.App.Views;

public partial class BulkEditDialog : Window
{
    public BulkEditDialog(BulkEditViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Completed += () => { DialogResult = true; Close(); };
        viewModel.Cancelled += () => { DialogResult = false; Close(); };
    }
}
