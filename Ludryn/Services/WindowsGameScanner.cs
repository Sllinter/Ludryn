using Ludryn.Models;
using Windows.Management.Deployment;

namespace Ludryn.Services;

public static class WindowsGameScanner
{
    public static IReadOnlyList<Game> ScanInstalledGames() =>
        ScanXboxGamesFolders()
            .Concat(ScanStoreGamePackages())
            .Concat(ScanKnownStorePackages())
            .GroupBy(g => NormalizeTitle(g.Title))
            .Select(g => g.First())
            .OrderByDescending(g => g.LastPlayed)
            .ThenBy(g => g.Title)
            .ToList();

    private static IEnumerable<Game> ScanXboxGamesFolders()
    {
        foreach (var root in GetXboxGamesRoots())
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                var title = CleanTitle(Path.GetFileName(directory));
                var contentPath = Directory.Exists(Path.Combine(directory, "Content"))
                    ? Path.Combine(directory, "Content")
                    : directory;
                var executable = FindLikelyGameExecutable(contentPath);

                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(executable))
                {
                    continue;
                }

                yield return CreateWindowsGame(
                    $"xbox-{NormalizeTitle(title)}",
                    title,
                    executable,
                    contentPath,
                    launchId: executable);
            }
        }
    }

    private static IEnumerable<Game> ScanStoreGamePackages()
    {
        PackageManager packageManager;
        try
        {
            packageManager = new PackageManager();
        }
        catch
        {
            yield break;
        }

        IEnumerable<Windows.ApplicationModel.Package> packages;
        try
        {
            packages = packageManager.FindPackagesForUser(string.Empty).ToList();
        }
        catch
        {
            yield break;
        }

        foreach (var package in packages)
        {
            Game? game = null;
            try
            {
                if (package.IsFramework || package.IsResourcePackage || package.IsBundle)
                {
                    continue;
                }

                var packageName = package.Id.Name;
                var title = ResolvePackageTitle(package);
                if (!IsLikelyWindowsGame(packageName, title))
                {
                    continue;
                }

                var installPath = TryGetInstalledPath(package);
                var appUserModelId = GetAppUserModelId(package);
                if (string.IsNullOrWhiteSpace(appUserModelId))
                {
                    appUserModelId = GetKnownAppUserModelId(packageName);
                    if (string.IsNullOrWhiteSpace(appUserModelId))
                    {
                        continue;
                    }
                }

                game = CreateWindowsGame(
                    $"windows-{NormalizeTitle(packageName)}",
                    CleanTitle(title),
                    appUserModelId,
                    installPath,
                    launchId: appUserModelId);
            }
            catch
            {
                // Some Store packages are not inspectable by desktop apps.
            }

            if (game is not null)
            {
                yield return game;
            }
        }
    }

    private static IEnumerable<Game> ScanKnownStorePackages()
    {
        var localPackages = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Packages");

        var minecraftPackagePath = Path.Combine(localPackages, "MICROSOFT.MINECRAFTUWP_8wekyb3d8bbwe");
        if (Directory.Exists(minecraftPackagePath))
        {
            yield return CreateWindowsGame(
                "windows-minecraftforwindows",
                "Minecraft for Windows",
                "MICROSOFT.MINECRAFTUWP_8wekyb3d8bbwe!Game",
                minecraftPackagePath,
                launchId: "MICROSOFT.MINECRAFTUWP_8wekyb3d8bbwe!Game");
        }
    }

    private static IEnumerable<string> GetXboxGamesRoots()
    {
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
        {
            yield return Path.Combine(drive.RootDirectory.FullName, "XboxGames");
        }
    }

    private static string ResolvePackageTitle(Windows.ApplicationModel.Package package)
    {
        if (IsMinecraftPackage(package.Id.Name))
        {
            return "Minecraft for Windows";
        }

        try
        {
            var entries = package.GetAppListEntriesAsync().AsTask().GetAwaiter().GetResult();
            var displayName = entries.FirstOrDefault()?.DisplayInfo.DisplayName;
            if (!string.IsNullOrWhiteSpace(displayName) && !displayName.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
            {
                return displayName;
            }
        }
        catch
        {
            // Package display name remains the fallback.
        }

        return string.IsNullOrWhiteSpace(package.DisplayName) || package.DisplayName.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)
            ? package.Id.Name
            : package.DisplayName;
    }

    private static string GetAppUserModelId(Windows.ApplicationModel.Package package)
    {
        try
        {
            var entries = package.GetAppListEntriesAsync().AsTask().GetAwaiter().GetResult();
            var appUserModelId = entries.FirstOrDefault()?.AppUserModelId;
            if (!string.IsNullOrWhiteSpace(appUserModelId))
            {
                return appUserModelId;
            }
        }
        catch
        {
            // Known Store packages can still be launched with stable AUMIDs.
        }

        return GetKnownAppUserModelId(package.Id.Name);
    }

    private static string TryGetInstalledPath(Windows.ApplicationModel.Package package)
    {
        try
        {
            return package.InstalledLocation?.Path ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsLikelyWindowsGame(string packageName, string title)
    {
        if (IsMinecraftPackage(packageName))
        {
            return true;
        }

        var combined = $"{packageName} {title}";
        if (ContainsAny(combined,
            "Minecraft",
            "Forza",
            "Halo",
            "FlightSimulator",
            "MicrosoftSolitaire",
            "Age of Empires",
            "Sea of Thieves",
            "State of Decay",
            "Gears",
            "Psychonauts",
            "Game"))
        {
            return !ContainsAny(combined,
                "GamingServices",
                "GamingApp",
                "XboxApp",
                "XboxGamingOverlay",
                "XboxIdentityProvider",
                "GameBar",
                "GameInput",
                "AppInstaller");
        }

        return false;
    }

    private static string GetKnownAppUserModelId(string packageName)
    {
        if (IsMinecraftPackage(packageName))
        {
            return "MICROSOFT.MINECRAFTUWP_8wekyb3d8bbwe!Game";
        }

        return string.Empty;
    }

    private static bool IsMinecraftPackage(string packageName) =>
        packageName.Equals("MICROSOFT.MINECRAFTUWP", StringComparison.OrdinalIgnoreCase) ||
        packageName.Contains("Minecraft", StringComparison.OrdinalIgnoreCase);

    private static Game CreateWindowsGame(string id, string title, string launchTarget, string installPath, string launchId)
    {
        var cover = PlaceholderArtGenerator.ColorFromTitle(title);
        var accent = PlaceholderArtGenerator.AccentFrom(cover);
        var game = new Game
        {
            Id = id,
            Title = title,
            Platform = "Xbox",
            SelectedLauncher = "Xbox",
            CoverArtColor = cover,
            AccentColor = accent,
            PlayTime = "0h 00min",
            LastPlayed = Directory.Exists(installPath) ? Directory.GetLastWriteTime(installPath) : DateTime.Now
        };

        game.Installations.Add(new GameInstallation
        {
            Launcher = "Xbox",
            LaunchId = launchId,
            InstallPath = installPath,
            IsDetected = true
        });
        return game;
    }

    private static string FindLikelyGameExecutable(string installPath)
    {
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
        return name.Contains("gamelaunchhelper") ||
            name.Contains("install") ||
            name.Contains("setup") ||
            name.Contains("redist") ||
            name.Contains("crash") ||
            name.Contains("helper") ||
            name.Contains("unins");
    }

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static string CleanTitle(string title) =>
        title.Replace("_", " ").Replace("-", " ").Trim();

    private static string NormalizeTitle(string title) =>
        new(title.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
}
