using Ludryn.Models;
using System.Text.Json;

namespace Ludryn.Services;

public static class EpicLibraryScanner
{
    public static IReadOnlyList<Game> ScanInstalledGames()
    {
        var manifestsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic",
            "EpicGamesLauncher",
            "Data",
            "Manifests");

        if (!Directory.Exists(manifestsPath))
        {
            return [];
        }

        var games = new List<Game>();
        foreach (var manifestPath in Directory.EnumerateFiles(manifestsPath, "*.item"))
        {
            var game = ParseManifest(manifestPath);
            if (game is not null)
            {
                games.Add(game);
            }
        }

        return games
            .GroupBy(g => NormalizeTitle(g.Title))
            .Select(g => g.First())
            .OrderByDescending(g => g.LastPlayed)
            .ThenBy(g => g.Title)
            .ToList();
    }

    private static Game? ParseManifest(string manifestPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = document.RootElement;
            var title = GetString(root, "DisplayName");
            var appName = GetString(root, "AppName");
            var installLocation = GetString(root, "InstallLocation");

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(appName))
            {
                return null;
            }

            var lastPlayed = File.GetLastWriteTime(manifestPath);
            return CreateEpicGame(appName, title, installLocation, lastPlayed);
        }
        catch
        {
            return null;
        }
    }

    private static Game CreateEpicGame(string appName, string title, string installPath, DateTime lastPlayed)
    {
        var cover = PlaceholderArtGenerator.ColorFromTitle(title);
        var accent = PlaceholderArtGenerator.AccentFrom(cover);
        var game = new Game
        {
            Id = $"epic-{SanitizeId(appName)}",
            Title = title,
            Platform = "Epic",
            SelectedLauncher = "Epic",
            CoverArtColor = cover,
            AccentColor = accent,
            PlayTime = "0h 00min",
            LastPlayed = lastPlayed
        };
        game.Installations.Add(new GameInstallation
        {
            Launcher = "Epic",
            LaunchId = appName,
            InstallPath = installPath,
            IsDetected = true
        });
        return game;
    }

    private static string GetString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static string NormalizeTitle(string title) =>
        new(title.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static string SanitizeId(string id)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            id = id.Replace(invalid, '-');
        }

        return id.ToLowerInvariant();
    }
}
