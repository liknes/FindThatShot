using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media.Imaging;

namespace VideoArchiveManager.App.Helpers;

public static class ThumbnailLoader
{
    private const int CardWidthPixels = 320;
    private static readonly ConcurrentDictionary<string, BitmapImage> Cache = new();

    public static BitmapImage? Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        if (Cache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.DecodePixelWidth = CardWidthPixels;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            Cache[path] = bitmap;
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public static BitmapImage? LoadLarge(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.DecodePixelWidth = 800;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public static void Invalidate(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        Cache.TryRemove(path, out _);
    }
}
