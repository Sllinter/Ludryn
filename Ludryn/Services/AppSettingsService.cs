using System.Text.Json;

namespace Ludryn.Services;

public static class AppSettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Ludryn",
        "settings.json");

    private static AppSettingsStore _store = Load();

    public static int StartupPageIndex
    {
        get => Math.Clamp(_store.StartupPageIndex, 0, 3);
        set
        {
            _store.StartupPageIndex = Math.Clamp(value, 0, 3);
            Save();
        }
    }

    public static string EmulatorPlatformSort
    {
        get => _store.EmulatorPlatformSort is "MostPlayed" or "RecentlyAdded"
            ? _store.EmulatorPlatformSort
            : "Recent";
        set
        {
            _store.EmulatorPlatformSort = value is "MostPlayed" or "RecentlyAdded" ? value : "Recent";
            Save();
        }
    }

    public static IReadOnlyList<string> GameDirectories => _store.GameDirectories;

    public static void AddGameDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path) ||
            _store.GameDirectories.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        _store.GameDirectories.Add(path);
        Save();
    }

    public static void RemoveGameDirectory(string path)
    {
        _store.GameDirectories.RemoveAll(item =>
            string.Equals(item, path, StringComparison.OrdinalIgnoreCase));
        Save();
    }

    private static AppSettingsStore Load()
    {
        try
        {
            return File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<AppSettingsStore>(File.ReadAllText(SettingsPath)) ?? new()
                : new();
        }
        catch
        {
            return new();
        }
    }

    private static void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(_store, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }
}

public sealed class AppSettingsStore
{
    public int StartupPageIndex { get; set; }
    public List<string> GameDirectories { get; set; } = [];
    public string EmulatorPlatformSort { get; set; } = "Recent";
}
