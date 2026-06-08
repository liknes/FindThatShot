// Find That Shot - organize and search a large local video archive.
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Ingve Moss Liknes <findthatshot@ingve.no>
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;
using VideoArchiveManager.App.Localization;

namespace VideoArchiveManager.App.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        var loc = LocalizationManager.Instance;

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version is null
            ? loc["About_VersionUnknown"]
            : loc.Format("About_VersionFormat", version.Major, version.Minor, version.Build);

        var noticesPath = TryFindNoticesPath();
        NoticesPathText.Text = noticesPath is null
            ? loc["About_NoticesPathFallback"]
            : loc.Format("About_NoticesPathFormat", noticesPath);
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
        var loc = LocalizationManager.Instance;
        var path = TryFindNoticesPath();
        if (path is null)
        {
            MessageBox.Show(
                this,
                loc["About_NoticesNotFound_Body"],
                loc["About_NoticesNotFound_Title"],
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
                loc.Format("About_OpenNoticesFailed", ex.Message),
                loc["About_NoticesNotFound_Title"],
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
