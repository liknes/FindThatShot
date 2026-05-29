using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace VideoArchiveManager.App.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version is null
            ? "Version unknown"
            : $"Version {version.Major}.{version.Minor}.{version.Build}";

        var noticesPath = TryFindNoticesPath();
        NoticesPathText.Text = noticesPath is null
            ? "Full third-party license texts ship in THIRD-PARTY-NOTICES.md next to the application."
            : $"Full third-party license texts: {noticesPath}";
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        var url = e.Uri?.ToString();
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            e.Handled = true;
        }
        catch
        {
            // Launching a browser can fail on locked-down systems; ignore silently.
        }
    }

    private void ViewNotices_Click(object sender, RoutedEventArgs e)
    {
        var path = TryFindNoticesPath();
        if (path is null)
        {
            MessageBox.Show(
                this,
                "THIRD-PARTY-NOTICES.md could not be found next to the application. " +
                "You can read it online at https://github.com/liknes/FindThatShot/blob/main/THIRD-PARTY-NOTICES.md.",
                "Notices not found",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Could not open {path}: {ex.Message}",
                "Open notices",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static string? TryFindNoticesPath()
    {
        // The publish step copies THIRD-PARTY-NOTICES.md next to the .exe. In dev builds it
        // ends up in the build output via the linked None item in the .csproj. Fall back to
        // walking up a couple of folders so the dialog works when running from `dotnet run`.
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "THIRD-PARTY-NOTICES.md"),
            Path.Combine(baseDir, "..", "THIRD-PARTY-NOTICES.md"),
            Path.Combine(baseDir, "..", "..", "THIRD-PARTY-NOTICES.md"),
            Path.Combine(baseDir, "..", "..", "..", "THIRD-PARTY-NOTICES.md"),
            Path.Combine(baseDir, "..", "..", "..", "..", "THIRD-PARTY-NOTICES.md"),
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "THIRD-PARTY-NOTICES.md"),
        };
        foreach (var candidate in candidates)
        {
            var resolved = Path.GetFullPath(candidate);
            if (File.Exists(resolved)) return resolved;
        }
        return null;
    }
}
