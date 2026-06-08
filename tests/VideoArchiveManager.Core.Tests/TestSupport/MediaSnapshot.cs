// Find That Shot - organize and search a large local video archive.
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Ingve Moss Liknes <findthatshot@ingve.no>
using System.Security.Cryptography;

namespace VideoArchiveManager.Core.Tests.TestSupport;

// Records a content + metadata fingerprint of every file in a directory tree so
// a test can prove an operation left the original media byte-for-byte untouched.
// This is the workhorse behind the #1 safety invariant: snapshot -> run op ->
// snapshot -> assert no media file changed, moved, or disappeared.
public sealed class MediaSnapshot
{
    public sealed record FileFingerprint(string Sha256, long Length, DateTime LastWriteUtc);

    private readonly Dictionary<string, FileFingerprint> _files;

    private MediaSnapshot(Dictionary<string, FileFingerprint> files) => _files = files;

    public IReadOnlyDictionary<string, FileFingerprint> Files => _files;

    public static MediaSnapshot Capture(string rootPath)
    {
        var map = new Dictionary<string, FileFingerprint>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(rootPath))
        {
            foreach (var path in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
            {
                var info = new FileInfo(path);
                map[path] = new FileFingerprint(HashFile(path), info.Length, info.LastWriteTimeUtc);
            }
        }
        return new MediaSnapshot(map);
    }

    private static string HashFile(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    // Returns a human-readable list of differences (added / removed / changed
    // files) between this snapshot and a freshly-captured one of the same root.
    // An empty list means the tree is provably unchanged.
    public IReadOnlyList<string> DiffAgainstCurrent(string rootPath)
    {
        var now = Capture(rootPath);
        var diffs = new List<string>();

        foreach (var (path, before) in _files)
        {
            if (!now._files.TryGetValue(path, out var after))
            {
                diffs.Add($"REMOVED: {path}");
                continue;
            }
            if (before.Sha256 != after.Sha256)
                diffs.Add($"CONTENT CHANGED: {path}");
            else if (before.Length != after.Length)
                diffs.Add($"LENGTH CHANGED: {path}");
            else if (before.LastWriteUtc != after.LastWriteUtc)
                diffs.Add($"MTIME CHANGED: {path}");
        }

        foreach (var path in now._files.Keys)
        {
            if (!_files.ContainsKey(path))
                diffs.Add($"ADDED: {path}");
        }

        return diffs;
    }
}
