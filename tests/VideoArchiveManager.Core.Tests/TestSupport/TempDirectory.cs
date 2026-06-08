// Find That Shot - organize and search a large local video archive.
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Ingve Moss Liknes <findthatshot@ingve.no>
namespace VideoArchiveManager.Core.Tests.TestSupport;

// A throwaway directory under the OS temp folder, deleted on Dispose. Used to
// stand up a fake media tree / cache directory per test without leaking files.
public sealed class TempDirectory : IDisposable
{
    public string Path { get; }

    public TempDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "fts-core-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Combine(params string[] parts)
        => System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());

    // Creates a deterministic "fake media" file with the given relative name and
    // returns its absolute path. The bytes are arbitrary but stable so a hash
    // snapshot is meaningful — these tests never decode the file, they only
    // assert it is left byte-for-byte untouched.
    public string CreateFile(string relativeName, byte[]? contents = null)
    {
        var full = System.IO.Path.Combine(Path, relativeName);
        var dir = System.IO.Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllBytes(full, contents ?? DefaultBytes(relativeName));
        return full;
    }

    private static byte[] DefaultBytes(string seed)
    {
        // Stable pseudo-content derived from the name so each fixture file has
        // distinct, repeatable bytes.
        var rng = new Random(seed.GetHashCode());
        var buffer = new byte[4096];
        rng.NextBytes(buffer);
        return buffer;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; a locked file shouldn't fail the test run.
        }
    }
}
