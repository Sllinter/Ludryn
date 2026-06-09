using Ludryn.Models;
using Microsoft.Win32;

namespace Ludryn.Services;

public static class SteamLibraryScanner
{
    public static IReadOnlyList<Game> ScanInstalledGames()
    {
        var steamPath = FindSteamPath();
        if (steamPath is null)
        {
            return [];
        }

        var libraryPaths = GetLibraryPaths(steamPath).Distinct(StringComparer.OrdinalIgnoreCase);
        var games = new List<Game>();
        foreach (var libraryPath in libraryPaths)
        {
            var steamAppsPath = Path.Combine(libraryPath, "steamapps");
            if (!Directory.Exists(steamAppsPath))
            {
                continue;
            }

            foreach (var manifestPath in Directory.EnumerateFiles(steamAppsPath, "appmanifest_*.acf"))
            {
                var game = ParseManifest(manifestPath);
                if (game is not null)
                {
                    games.Add(game);
                }
            }
        }

        return games
            .GroupBy(g => g.SteamAppId)
            .Select(g => g.First())
            .OrderByDescending(g => g.LastPlayed)
            .ThenBy(g => g.Title)
            .ToList();
    }

    private static string? FindSteamPath()
    {
        var registryPaths = new[]
        {
            @"HKEY_CURRENT_USER\Software\Valve\Steam",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam"
        };

        foreach (var registryPath in registryPaths)
        {
            var installPath = Registry.GetValue(registryPath, "SteamPath", null) as string
                ?? Registry.GetValue(registryPath, "InstallPath", null) as string;
            if (!string.IsNullOrWhiteSpace(installPath) && Directory.Exists(installPath))
            {
                return installPath;
            }
        }

        var defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        return Directory.Exists(defaultPath) ? defaultPath : null;
    }

    private static IEnumerable<string> GetLibraryPaths(string steamPath)
    {
        yield return steamPath;

        var libraryFoldersPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(libraryFoldersPath))
        {
            yield break;
        }

        foreach (var line in File.ReadLines(libraryFoldersPath))
        {
            var value = ReadVdfStringValue(line, "path")?.Replace(@"\\", @"\");
            if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value))
            {
                yield return value;
            }
        }
    }

    private static Game? ParseManifest(string manifestPath)
    {
        try
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in File.ReadLines(manifestPath))
            {
                ReadVdfPair(line, values);
            }

            if (!values.TryGetValue("appid", out var appIdText) || !int.TryParse(appIdText, out var appId))
            {
                return null;
            }

            if (!values.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var playMinutes = values.TryGetValue("playtime_forever", out var playtimeText) && int.TryParse(playtimeText, out var minutes)
                ? minutes
                : 0;

            var lastUpdated = values.TryGetValue("LastUpdated", out var updatedText) && long.TryParse(updatedText, out var unixTime)
                ? DateTimeOffset.FromUnixTimeSeconds(unixTime).LocalDateTime
                : File.GetLastWriteTime(manifestPath);

            var installDir = values.TryGetValue("installdir", out var installDirText) ? installDirText : string.Empty;
            var installPath = string.IsNullOrWhiteSpace(installDir)
                ? string.Empty
                : Path.Combine(Path.GetDirectoryName(manifestPath) ?? string.Empty, "common", installDir);

            return CreateSteamGame(appId, name, FormatPlayTime(playMinutes), lastUpdated, installPath);
        }
        catch
        {
            return null;
        }
    }

    private static Game CreateSteamGame(int appId, string title, string playTime, DateTime lastPlayed, string installPath)
    {
        var cover = PlaceholderArtGenerator.ColorFromTitle(title);
        var accent = PlaceholderArtGenerator.AccentFrom(cover);
        var game = new Game
        {
            Id = $"steam-{appId}",
            Title = title,
            Platform = "Steam",
            SelectedLauncher = "Steam",
            SteamAppId = appId,
            CoverArtColor = cover,
            AccentColor = accent,
            PlayTime = playTime,
            LastPlayed = lastPlayed
        };
        game.Installations.Add(new GameInstallation
        {
            Launcher = "Steam",
            LaunchId = appId.ToString(),
            InstallPath = installPath,
            IsDetected = true
        });
        return game;
    }

    private static string FormatPlayTime(int minutes)
    {
        if (minutes <= 0)
        {
            return "0h 00min";
        }

        return $"{minutes / 60}h {minutes % 60:00}min";
    }

    private static string? ReadVdfStringValue(string line, string key)
    {
        var values = ExtractQuotedValues(line);
        return values.Count >= 2 && string.Equals(values[0], key, StringComparison.OrdinalIgnoreCase)
            ? values[1]
            : null;
    }

    private static void ReadVdfPair(string line, IDictionary<string, string> values)
    {
        var pair = ExtractQuotedValues(line);
        if (pair.Count >= 2)
        {
            values[pair[0]] = pair[1];
        }
    }

    private static List<string> ExtractQuotedValues(string line)
    {
        var values = new List<string>();
        var start = -1;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] != '"')
            {
                continue;
            }

            if (start < 0)
            {
                start = i + 1;
            }
            else
            {
                values.Add(line[start..i]);
                start = -1;
            }
        }

        return values;
    }
}
