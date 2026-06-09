using Ludryn.Models;
using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace Ludryn.Services;

public static class ThirdPartyStoreScanner
{
    private static readonly string[] RegistryRoots =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    ];

    public static IReadOnlyList<Game> ScanInstalledGames() =>
        ScanGogGames()
            .Concat(ScanRegistryStoreGames("Ubisoft Connect", IsUbisoftEntry))
            .Concat(ScanRegistryStoreGames("EA Play", IsEaEntry))
            .GroupBy(g => $"{g.SelectedLauncher}:{NormalizeTitle(g.Title)}")
            .Select(g => g.First())
            .OrderByDescending(g => g.LastPlayed)
            .ThenBy(g => g.Title)
            .ToList();

    private static IEnumerable<Game> ScanGogGames()
    {
        foreach (var game in ScanGogRegistryGames())
        {
            yield return game;
        }

        foreach (var game in ScanRegistryStoreGames("GOG", IsGogEntry))
        {
            yield return game;
        }
    }

    private static IEnumerable<Game> ScanGogRegistryGames()
    {
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            foreach (var baseKeyName in new[] { @"SOFTWARE\GOG.com\Games", @"SOFTWARE\WOW6432Node\GOG.com\Games" })
            {
                using var baseKey = hive.OpenSubKey(baseKeyName);
                if (baseKey is null)
                {
                    continue;
                }

                foreach (var gameId in baseKey.GetSubKeyNames())
                {
                    using var gameKey = baseKey.OpenSubKey(gameId);
                    if (gameKey is null)
                    {
                        continue;
                    }

                    var title = ReadString(gameKey, "gameName");
                    var installPath = ReadString(gameKey, "path");
                    var launchCommand = ReadString(gameKey, "launchCommand");
                    var executable = ExtractExecutablePath(launchCommand);
                    if (string.IsNullOrWhiteSpace(executable))
                    {
                        executable = FindLikelyExecutable(installPath);
                    }

                    if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(installPath))
                    {
                        yield return CreateStoreGame("GOG", $"gog-{gameId}", title, gameId, installPath, executable);
                    }
                }
            }
        }
    }

    private static IEnumerable<Game> ScanRegistryStoreGames(string launcher, Func<RegistryKey, bool> matches)
    {
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            foreach (var rootName in RegistryRoots)
            {
                using var root = hive.OpenSubKey(rootName);
                if (root is null)
                {
                    continue;
                }

                foreach (var subKeyName in root.GetSubKeyNames())
                {
                    using var key = root.OpenSubKey(subKeyName);
                    if (key is null || !matches(key))
                    {
                        continue;
                    }

                    var title = CleanTitle(ReadString(key, "DisplayName"));
                    var installPath = ReadString(key, "InstallLocation");
                    if (string.IsNullOrWhiteSpace(installPath))
                    {
                        installPath = TryGetDirectory(ReadString(key, "DisplayIcon"));
                    }

                    var executable = ExtractExecutablePath(ReadString(key, "DisplayIcon"));
                    if (string.IsNullOrWhiteSpace(executable))
                    {
                        executable = FindLikelyExecutable(installPath);
                    }

                    if (ShouldSkip(title, installPath))
                    {
                        continue;
                    }

                    yield return CreateStoreGame(launcher, $"{NormalizeTitle(launcher)}-{NormalizeTitle(title)}", title, executable, installPath, executable);
                }
            }
        }
    }

    private static Game CreateStoreGame(string launcher, string id, string title, string launchId, string installPath, string executablePath)
    {
        var cover = PlaceholderArtGenerator.ColorFromTitle(title);
        var accent = PlaceholderArtGenerator.AccentFrom(cover);
        var game = new Game
        {
            Id = id,
            Title = title,
            Platform = launcher,
            SelectedLauncher = launcher,
            CoverArtColor = cover,
            AccentColor = accent,
            PlayTime = "0h 00min",
            LastPlayed = Directory.Exists(installPath) ? Directory.GetLastWriteTime(installPath) : DateTime.Now
        };

        game.Installations.Add(new GameInstallation
        {
            Launcher = launcher,
            LaunchId = string.IsNullOrWhiteSpace(executablePath) ? launchId : executablePath,
            InstallPath = installPath,
            IsDetected = true
        });
        return game;
    }

    private static bool IsGogEntry(RegistryKey key) =>
        ContainsAny(ReadString(key, "Publisher"), "GOG", "CD Projekt") ||
        ContainsAny(ReadString(key, "DisplayName"), "GOG.com");

    private static bool IsUbisoftEntry(RegistryKey key) =>
        ContainsAny(ReadString(key, "Publisher"), "Ubisoft") &&
        !ContainsAny(ReadString(key, "DisplayName"), "Ubisoft Connect", "Ubisoft Game Launcher");

    private static bool IsEaEntry(RegistryKey key)
    {
        var publisher = ReadString(key, "Publisher");
        var name = ReadString(key, "DisplayName");
        return ContainsAny(publisher, "Electronic Arts", "EA Swiss", "EA Digital") &&
            !ContainsAny(name, "EA app", "EA Desktop", "Origin");
    }

    private static bool ShouldSkip(string title, string installPath) =>
        string.IsNullOrWhiteSpace(title) ||
        title.Length < 2 ||
        title.Contains("redistributable", StringComparison.OrdinalIgnoreCase) ||
        title.Contains("launcher", StringComparison.OrdinalIgnoreCase) ||
        title.Contains("GOG Galaxy", StringComparison.OrdinalIgnoreCase) ||
        title.Equals("GOG", StringComparison.OrdinalIgnoreCase) ||
        title.Equals("GOG.com", StringComparison.OrdinalIgnoreCase) ||
        title.Contains("GOG.com Galaxy", StringComparison.OrdinalIgnoreCase) ||
        string.IsNullOrWhiteSpace(installPath);

    private static string FindLikelyExecutable(string installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
        {
            return string.Empty;
        }

        try
        {
            return Directory.EnumerateFiles(installPath, "*.exe", SearchOption.AllDirectories)
                .Where(path => !IsIgnoredExecutable(path))
                .OrderBy(path => path.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar))
                .ThenByDescending(path => new FileInfo(path).Length)
                .FirstOrDefault() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsIgnoredExecutable(string path)
    {
        var name = Path.GetFileName(path).ToLowerInvariant();
        return name.Contains("unins") ||
            name.Contains("uninstall") ||
            name.Contains("setup") ||
            name.Contains("redist") ||
            name.Contains("crash") ||
            name.Contains("report") ||
            name.Contains("helper") ||
            name.Contains("launcher") ||
            path.Contains(@"\__Installer\", StringComparison.OrdinalIgnoreCase) ||
            path.Contains(@"\Support\", StringComparison.OrdinalIgnoreCase) ||
            path.Contains(@"\Redist\", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractExecutablePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var quoted = Regex.Match(value, "\"([^\"]+\\.exe)\"");
        if (quoted.Success && File.Exists(quoted.Groups[1].Value))
        {
            return quoted.Groups[1].Value;
        }

        var cleaned = value.Split(',')[0].Trim().Trim('"');
        return cleaned.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(cleaned)
            ? cleaned
            : string.Empty;
    }

    private static string TryGetDirectory(string value)
    {
        var executable = ExtractExecutablePath(value);
        return string.IsNullOrWhiteSpace(executable) ? string.Empty : Path.GetDirectoryName(executable) ?? string.Empty;
    }

    private static string ReadString(RegistryKey key, string name) =>
        key.GetValue(name)?.ToString() ?? string.Empty;

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static string CleanTitle(string title) =>
        Regex.Replace(title, @"\s*\((TM|R|C)\)\s*", " ", RegexOptions.IgnoreCase).Trim();

    private static string NormalizeTitle(string title) =>
        new(title.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
}
