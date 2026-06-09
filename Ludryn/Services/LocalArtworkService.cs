using Ludryn.Models;

namespace Ludryn.Services;

public static class LocalArtworkService
{
    private static readonly string CacheRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Ludryn",
        "ImageCache");

    public static async Task<string?> ImportAsync(string gameId, ArtworkKind kind, string sourcePath)
    {
        if (!File.Exists(sourcePath))
        {
            return null;
        }

        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".webp"))
        {
            return null;
        }

        var folder = GetFolder(kind);
        Directory.CreateDirectory(folder);
        var safeKey = SanitizeCacheKey(gameId);

        foreach (var existingPath in Directory.EnumerateFiles(folder)
                     .Where(path => IsCacheFileForKey(path, safeKey)))
        {
            TryDelete(existingPath);
        }

        var destinationPath = Path.Combine(
            folder,
            $"{safeKey}-selected-local-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Guid.NewGuid():N}{extension}");

        await using var source = File.OpenRead(sourcePath);
        await using var destination = File.Create(destinationPath);
        await source.CopyToAsync(destination);
        return destinationPath;
    }

    public static CachedGameArt GetCachedGameArt(Game game)
    {
        var safeKey = SanitizeCacheKey(game.Id);
        return new CachedGameArt(
            FindLatest("covers", safeKey),
            FindLatest("heroes", safeKey),
            FindLatest("logos", safeKey),
            null,
            null,
            null);
    }

    private static string GetFolder(ArtworkKind kind) =>
        Path.Combine(CacheRoot, kind switch
        {
            ArtworkKind.Cover => "covers",
            ArtworkKind.Hero => "heroes",
            ArtworkKind.Logo => "logos",
            _ => "covers"
        });

    private static string? FindLatest(string folderName, string safeKey)
    {
        var folder = Path.Combine(CacheRoot, folderName);
        if (!Directory.Exists(folder))
        {
            return null;
        }

        return Directory.EnumerateFiles(folder)
            .Where(path => IsCacheFileForKey(path, safeKey))
            .Where(path => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static bool IsCacheFileForKey(string path, string safeKey)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        return string.Equals(fileName, safeKey, StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith($"{safeKey}-selected-", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, $"{safeKey}-selected", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith($"{safeKey}-selected-local-", StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeCacheKey(string key)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            key = key.Replace(invalid, '-');
        }

        return key;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }
}
