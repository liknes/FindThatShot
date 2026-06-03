using System.Windows;

namespace VideoArchiveManager.App.Views;

// Minimal name prompt for creating / renaming a saved search. Self-contained
// (no VM) since the only state is a single string; the caller reads
// SearchName after ShowDialog() returns true.
public partial class SaveSearchDialog : Window
{
    public SaveSearchDialog(string? suggestedName = null)
    {
        InitializeComponent();
        NameBox.Text = suggestedName ?? string.Empty;

        // Preselect the suggested text so the user can overwrite it with a
        // single keystroke or accept it with Enter.
        Loaded += (_, _) =>
        {
            NameBox.SelectAll();
            NameBox.Focus();
        };
    }

    public string SearchName => NameBox.Text;

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text)) return;
        DialogResult = true;
        Close();
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
