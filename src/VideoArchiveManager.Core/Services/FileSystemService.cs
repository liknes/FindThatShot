using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace VideoArchiveManager.Core.Services;

public class FileSystemService : IFileSystemService
{
    private readonly ILogger<FileSystemService> _logger;

    public FileSystemService(ILogger<FileSystemService> logger)
    {
        _logger = logger;
    }

    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public IEnumerable<string> EnumerateVideoFiles(string rootPath, IReadOnlyList<string> extensions, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            yield break;
        }

        var extensionSet = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        IEnumerable<string> enumerator;
        try
        {
            enumerator = Directory.EnumerateFiles(rootPath, "*", options);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate files at {Path}", rootPath);
            yield break;
        }

        foreach (var path in enumerator)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ext = Path.GetExtension(path);
            if (!string.IsNullOrEmpty(ext) && extensionSet.Contains(ext))
            {
                yield return path;
            }
        }
    }

    public void OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

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
            _logger.LogWarning(ex, "Failed to open folder {Path}", path);
        }
    }

    public void RevealInExplorer(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;

        try
        {
            if (File.Exists(filePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{filePath}\"",
                    UseShellExecute = true
                });
            }
            else
            {
                var folder = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                {
                    OpenFolder(folder);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reveal {FilePath}", filePath);
        }
    }

    public void OpenWithDefaultPlayer(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open {FilePath} with default player", filePath);
        }
    }
}
