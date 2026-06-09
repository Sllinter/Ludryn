using Ludryn.Models;
using Microsoft.Win32;
using System.Diagnostics;
using System.Text.Json;

namespace Ludryn.Services;

public static class StoreLauncherService
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Ludryn",
        "store-launchers.json");

    private static readonly StoreLauncherConfig Config = Load();

    public static IReadOnlyList<Game> GetBuiltInStores() =>
        new[]
        {
            CreateStore("store-steam", "Steam", iconPath: GetBuiltInIconPath("store-steam")),
            CreateStore("store-epic", "Epic Games", iconPath: GetBuiltInIconPath("store-epic")),
            CreateStore("store-gog", "GOG", iconPath: GetBuiltInIconPath("store-gog")),
            CreateStore("store-ubisoft", "Ubisoft Connect", iconPath: GetBuiltInIconPath("store-ubisoft")),
            CreateStore("store-ea", "EA Play", iconPath: GetBuiltInIconPath("store-ea"))
        }
        .Where(store => !Config.HiddenBuiltInStoreIds.Contains(store.Id, StringComparer.OrdinalIgnoreCase))
        .ToList();

    public static IReadOnlyList<Game> GetCustomStores() =>
        Config.CustomStores.Select(custom => CreateStore(
            custom.Id,
            custom.Title,
            custom.ExecutablePath,
            custom.IconPath)).ToList();

    public static Game CreateCustomStore(string title, string executablePath)
    {
        var id = $"custom-store-{Guid.NewGuid():N}";
        SaveManualExecutable(title, executablePath);
        Config.CustomStores.Add(new CustomStoreConfig
        {
            Id = id,
            Title = title,
            ExecutablePath = executablePath
        });
        Save();
        return CreateStore(id, title, executablePath);
    }

    public static void SaveStoreIcon(string id, string iconPath)
    {
        var customStore = Config.CustomStores.FirstOrDefault(store =>
            string.Equals(store.Id, id, StringComparison.OrdinalIgnoreCase));
        if (customStore is not null)
        {
            customStore.IconPath = iconPath;
            Save();
            return;
        }

        Config.BuiltInIconPaths[id] = iconPath;
        Save();
    }

    public static void SaveCustomStoreIcon(string id, string iconPath) =>
        SaveStoreIcon(id, iconPath);

    public static void RemoveStore(string id)
    {
        var removedCustomStore = Config.CustomStores.RemoveAll(store =>
            string.Equals(store.Id, id, StringComparison.OrdinalIgnoreCase));

        if (removedCustomStore == 0 &&
            id.StartsWith("store-", StringComparison.OrdinalIgnoreCase) &&
            !Config.HiddenBuiltInStoreIds.Contains(id, StringComparer.OrdinalIgnoreCase))
        {
            Config.HiddenBuiltInStoreIds.Add(id);
        }

        Save();
    }

    public static void RemoveCustomStore(string id) => RemoveStore(id);

    public static void SaveManualExecutable(string title, string executablePath)
    {
        Config.ManualExecutables[Normalize(title)] = executablePath;
        Save();
    }

    public static bool Launch(Game store)
    {
        if (!store.IsStoreEntry || !File.Exists(store.StoreExecutablePath))
        {
            return false;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = store.StoreExecutablePath,
            Arguments = store.StoreLaunchArguments,
            UseShellExecute = true
        };

        Process.Start(startInfo);
        return true;
    }

    private static Game CreateStore(
        string id,
        string title,
        string? executableOverride = null,
        string? iconPath = null)
    {
        var executablePath = !string.IsNullOrWhiteSpace(executableOverride) && File.Exists(executableOverride)
            ? executableOverride
            : FindExecutable(title);
        var store = new Game
        {
            Id = id,
            Title = title,
            Platform = "Loja",
            PlayTime = string.Empty,
            LastPlayed = DateTime.MinValue,
            IsPlatformEntry = true,
            IsStoreEntry = true,
            StoreExecutablePath = executablePath,
            StoreLaunchArguments = title == "Steam" ? "-start steam://open/bigpicture" : string.Empty,
            PlatformIcon = ApplicationIconService.LoadCachedIcon(iconPath ?? string.Empty)
                ?? PlatformIconService.GetIcon(title)
        };

        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            store.Installations.Add(new GameInstallation
            {
                Launcher = title,
                LaunchId = executablePath,
                InstallPath = Path.GetDirectoryName(executablePath) ?? string.Empty,
                IsDetected = true
            });
        }

        store.SelectedLauncher = title;
        return store;
    }

    private static string FindExecutable(string title)
    {
        if (Config.ManualExecutables.TryGetValue(Normalize(title), out var manualPath) && File.Exists(manualPath))
        {
            return manualPath;
        }

        return title switch
        {
            "Steam" => FirstExisting(
                ReadRegistryString(@"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", "steam.exe"),
                ReadRegistryString(@"SOFTWARE\Valve\Steam", "InstallPath", "steam.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steam.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam", "steam.exe")),
            "Epic Games" => FirstExisting(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Epic Games", "Launcher", "Portal", "Binaries", "Win64", "EpicGamesLauncher.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Epic Games", "Launcher", "Portal", "Binaries", "Win64", "EpicGamesLauncher.exe")),
            "GOG" => FirstExisting(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "GOG Galaxy", "GalaxyClient.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GOG Galaxy", "GalaxyClient.exe")),
            "Ubisoft Connect" => FirstExisting(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Ubisoft", "Ubisoft Game Launcher", "UbisoftConnect.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ubisoft", "Ubisoft Game Launcher", "UbisoftConnect.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Ubisoft", "Ubisoft Game Launcher", "upc.exe")),
            "EA Play" => FirstExisting(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Electronic Arts", "EA Desktop", "EA Desktop", "EALauncher.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Electronic Arts", "EA Desktop", "EA Desktop", "EADesktop.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Origin", "Origin.exe")),
            _ => string.Empty
        };
    }

    private static string ReadRegistryString(string subKeyName, string valueName, string executableName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(subKeyName) ?? Registry.CurrentUser.OpenSubKey(subKeyName);
            var directory = key?.GetValue(valueName)?.ToString();
            return string.IsNullOrWhiteSpace(directory) ? string.Empty : Path.Combine(directory, executableName);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string FirstExisting(params string[] paths) =>
        paths.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path)) ?? string.Empty;

    private static string GetBuiltInIconPath(string id) =>
        Config.BuiltInIconPaths.TryGetValue(id, out var iconPath) ? iconPath : string.Empty;

    private static StoreLauncherConfig Load()
    {
        try
        {
            return File.Exists(ConfigPath)
                ? JsonSerializer.Deserialize<StoreLauncherConfig>(File.ReadAllText(ConfigPath)) ?? new StoreLauncherConfig()
                : new StoreLauncherConfig();
        }
        catch
        {
            return new StoreLauncherConfig();
        }
    }

    private static void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string Normalize(string title) =>
        new(title.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
}

public sealed class StoreLauncherConfig
{
    public Dictionary<string, string> ManualExecutables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> BuiltInIconPaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> HiddenBuiltInStoreIds { get; set; } = [];
    public List<CustomStoreConfig> CustomStores { get; set; } = [];
}

public sealed class CustomStoreConfig
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string IconPath { get; set; } = string.Empty;
}
