using Ludryn.Models;
using System.Security.Cryptography;
using System.Text;

namespace Ludryn.Services;

public static class ManualGameDirectoryScanner
{
    private static readonly string[] IgnoredNames =
    [
        "unins", "uninstall", "setup", "install", "crash", "report", "redist",
        "vc_redist", "dxsetup", "launcher", "helper", "service"
    ];

    public static IReadOnlyList<Game> Scan()
    {
        var games = new List<Game>();
        foreach (var root in AppSettingsService.GameDirectories.Where(Directory.Exists))
        {
            try
            {
                games.AddRange(ScanRoot(root));
            }
            catch (Exception ex)
            {
                LudrynLogger.Error("library", $"Falha ao escanear pasta manual: {root}", ex);
            }
        }

        return games
            .GroupBy(game => game.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static IEnumerable<Game> ScanRoot(string root)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            MaxRecursionDepth = 2,
            IgnoreInaccessible = true
        };

        foreach (var executable in Directory.EnumerateFiles(root, "*.exe", options)
                     .Where(IsProbableGameExecutable))
        {
            var title = GetTitle(executable, root);
            var id = $"manual-{CreateStableId(executable)}";
            var cover = PlaceholderArtGenerator.ColorFromTitle(title);
            var game = new Game
            {
                Id = id,
                Title = title,
                Platform = "PC",
                SelectedLauncher = "PC",
                CoverArtColor = cover,
                AccentColor = PlaceholderArtGenerator.AccentFrom(cover),
                LastPlayed = File.GetLastWriteTime(executable)
            };
            game.Installations.Add(new GameInstallation
            {
                Launcher = "PC",
                LaunchId = executable,
                InstallPath = Path.GetDirectoryName(executable) ?? root,
                IsDetected = true
            });
            yield return game;
        }
    }

    private static bool IsProbableGameExecutable(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return !IgnoredNames.Any(ignored => name.Contains(ignored, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetTitle(string executable, string root)
    {
        var directory = Path.GetDirectoryName(executable);
        var title = string.Equals(directory?.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(executable)
            : new DirectoryInfo(directory!).Name;
        return title.Replace('_', ' ').Trim();
    }

    private static string CreateStableId(string path)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToLowerInvariant()));
        return Convert.ToHexString(bytes)[..20].ToLowerInvariant();
    }
}
