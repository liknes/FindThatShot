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
namespace VideoArchiveManager.Core.Services;

// Thrown when a delete/overwrite would touch something that looks like an
// original media file, or a path that escapes the app's own cache. It is a
// "should be impossible" signal: hitting it means a code path tried to do
// something the app's #1 safety invariant forbids.
public sealed class MediaSafetyException : Exception
{
    public MediaSafetyException(string message) : base(message) { }
}

// Centralised, last-line-of-defence guard for the single most important
// invariant in the app: ORIGINAL MEDIA FILES ARE NEVER DELETED OR OVERWRITTEN.
//
// Disk-mutating code (currently only the thumbnail cache cleanup) routes its
// File.Delete calls through EnsureSafeToDelete so that, no matter how a path is
// constructed upstream, the app physically refuses to delete:
//   * anything that has a known media file extension, or
//   * anything that does not resolve inside an explicitly-allowed cache root.
//
// This is intentionally independent of user settings (it carries its own media
// extension list) so a misconfigured or malicious settings value can't widen
// what counts as "safe".
public static class MediaSafetyGuard
{
    // Superset of the app's default SupportedExtensions plus other common
    // container/sidecar formats. Kept deliberately broad: the guard's job is to
    // err on the side of NEVER deleting something that could be source footage.
    private static readonly HashSet<string> ProtectedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Video containers
        ".mp4", ".mov", ".mxf", ".avi", ".mkv", ".m4v", ".mpg", ".mpeg",
        ".wmv", ".flv", ".webm", ".m2ts", ".mts", ".ts", ".3gp", ".insv",
        ".lrv", ".braw", ".r3d", ".dng",
        // Companion files the app reads next to footage
        ".srt",
        // The app's own portable metadata sidecar
        ".json",
    };

    public static bool IsProtectedMedia(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var ext = Path.GetExtension(path);
        return !string.IsNullOrEmpty(ext) && ProtectedExtensions.Contains(ext);
    }

    // Returns true when fullPath resolves at or below allowedRootFull.
    public static bool IsWithin(string fullPath, string allowedRootFull)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || string.IsNullOrWhiteSpace(allowedRootFull))
            return false;

        string p, root;
        try
        {
            p = Path.GetFullPath(fullPath);
            root = Path.GetFullPath(allowedRootFull);
        }
        catch
        {
            return false;
        }

        var rootWithSep = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        return string.Equals(p, root, StringComparison.OrdinalIgnoreCase)
            || p.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase);
    }

    // Throws MediaSafetyException unless candidatePath is safe to delete:
    // it must resolve inside allowedCacheRoot AND must not carry a protected
    // media extension. Call this immediately before any File.Delete that the
    // app performs on a path derived (even indirectly) from catalog data.
    public static void EnsureSafeToDelete(string candidatePath, string allowedCacheRoot)
    {
        if (string.IsNullOrWhiteSpace(candidatePath))
            throw new MediaSafetyException("Refusing to delete an empty path.");

        if (IsProtectedMedia(candidatePath))
            throw new MediaSafetyException(
                $"Refusing to delete '{candidatePath}': it has a protected media/sidecar extension.");

        if (!IsWithin(candidatePath, allowedCacheRoot))
            throw new MediaSafetyException(
                $"Refusing to delete '{candidatePath}': it resolves outside the app cache directory '{allowedCacheRoot}'.");
    }
}
