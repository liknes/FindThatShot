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
using System.IO.Enumeration;
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

    public IEnumerable<string> EnumerateVideoFiles(
        string rootPath,
        IReadOnlyList<string> extensions,
        IReadOnlyList<string>? excludedFolderNames = null,
        IReadOnlyList<string>? excludedFileNamePatterns = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            yield break;
        }

        var extensionSet = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
        var folderExcludes = BuildSet(excludedFolderNames);
        var filePatterns = NormalizePatterns(excludedFileNamePatterns);

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
            if (string.IsNullOrEmpty(ext) || !extensionSet.Contains(ext))
            {
                continue;
            }

            if (folderExcludes.Count > 0 && IsUnderExcludedFolder(rootPath, path, folderExcludes))
            {
                continue;
            }

            if (filePatterns.Count > 0 && MatchesAnyPattern(Path.GetFileName(path), filePatterns))
            {
                continue;
            }

            yield return path;
        }
    }

    private static HashSet<string> BuildSet(IReadOnlyList<string>? values)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (values is null) return set;
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
            {
                set.Add(v.Trim());
            }
        }
        return set;
    }

    private static List<string> NormalizePatterns(IReadOnlyList<string>? values)
    {
        var list = new List<string>();
        if (values is null) return list;
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
            {
                list.Add(v.Trim());
            }
        }
        return list;
    }

    private static bool IsUnderExcludedFolder(string rootPath, string filePath, HashSet<string> excludedFolderNames)
    {
        var folder = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(folder)) return false;

        string relative;
        try
        {
            relative = Path.GetRelativePath(rootPath, folder);
        }
        catch
        {
            relative = folder;
        }

        if (string.IsNullOrEmpty(relative) || relative == ".") return false;

        var segments = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            if (excludedFolderNames.Contains(segment))
            {
                return true;
            }
        }
        return false;
    }

    private static bool MatchesAnyPattern(string fileName, List<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            if (FileSystemName.MatchesSimpleExpression(pattern, fileName, ignoreCase: true))
            {
                return true;
            }
        }
        return false;
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
