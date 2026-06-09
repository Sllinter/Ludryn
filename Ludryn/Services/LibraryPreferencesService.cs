using Ludryn.Models;
using System.Text.Json;

namespace Ludryn.Services;

public sealed class LibraryPreferencesService
{
    private static readonly string PreferencesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Ludryn",
        "library-preferences.json");
    private static readonly string SecondaryPreferencesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Ludryn",
        "library-preferences-secondary.json");

    private readonly ProfileService _profileService;
    private readonly LibraryPreferencesStore _mainStore;
    private readonly LibraryPreferencesStore _secondaryStore;

    public LibraryPreferencesService(ProfileService profileService)
    {
        _profileService = profileService;
        _mainStore = Load(PreferencesPath);
        _secondaryStore = Load(SecondaryPreferencesPath);
    }

    private LibraryPreferencesStore ActiveStore =>
        _profileService.IsSecondary ? _secondaryStore : _mainStore;

    public void Apply(Game game)
    {
        var key = GetKey(game);
        var store = ActiveStore;
        game.IsFavorite = store.FavoriteGameKeys.Contains(key);

        if (store.LastPlayedGames.TryGetValue(key, out var lastPlayed))
        {
            game.LastPlayed = lastPlayed;
        }
        if (store.LaunchCounts.TryGetValue(key, out var launchCount))
        {
            game.LaunchCount = launchCount;
        }

        if (store.SelectedLaunchers.TryGetValue(key, out var launcher) &&
            game.Installations.Any(i => string.Equals(i.Launcher, launcher, StringComparison.OrdinalIgnoreCase)))
        {
            game.SelectedLauncher = launcher;
        }
        else if (game.Installations.Count > 0 &&
            !game.Installations.Any(i => string.Equals(i.Launcher, game.SelectedLauncher, StringComparison.OrdinalIgnoreCase)))
        {
            game.SelectedLauncher = game.Installations[0].Launcher;
        }
    }

    public bool IsHidden(Game game) => _mainStore.HiddenGameKeys.Contains(GetKey(game));

    public void SetFavorite(Game game, bool favorite)
    {
        var key = GetKey(game);
        if (favorite)
        {
            ActiveStore.FavoriteGameKeys.Add(key);
        }
        else
        {
            ActiveStore.FavoriteGameKeys.Remove(key);
        }

        Save();
    }

    public void SetSelectedLauncher(Game game, string launcher)
    {
        ActiveStore.SelectedLaunchers[GetKey(game)] = launcher;
        Save();
    }

    public void SetLastPlayed(Game game, DateTime lastPlayed)
    {
        ActiveStore.LastPlayedGames[GetKey(game)] = lastPlayed;
        Save();
    }

    public void IncrementLaunchCount(Game game)
    {
        var key = GetKey(game);
        ActiveStore.LaunchCounts[key] = ActiveStore.LaunchCounts.GetValueOrDefault(key) + 1;
        game.LaunchCount = ActiveStore.LaunchCounts[key];
        Save();
    }

    public void Hide(Game game)
    {
        var key = GetKey(game);
        _mainStore.HiddenGameKeys.Add(key);
        _mainStore.FavoriteGameKeys.Remove(key);
        _mainStore.SelectedLaunchers.Remove(key);
        _mainStore.LastPlayedGames.Remove(key);
        _mainStore.LaunchCounts.Remove(key);
        _secondaryStore.FavoriteGameKeys.Remove(key);
        _secondaryStore.SelectedLaunchers.Remove(key);
        _secondaryStore.LastPlayedGames.Remove(key);
        _secondaryStore.LaunchCounts.Remove(key);
        Save();
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath)!);
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(PreferencesPath, JsonSerializer.Serialize(_mainStore, options));
        File.WriteAllText(SecondaryPreferencesPath, JsonSerializer.Serialize(_secondaryStore, options));
    }

    private static LibraryPreferencesStore Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<LibraryPreferencesStore>(File.ReadAllText(path)) ?? new LibraryPreferencesStore()
                : new LibraryPreferencesStore();
        }
        catch
        {
            return new LibraryPreferencesStore();
        }
    }

    private static string GetKey(Game game) => NormalizeTitle(game.Title);

    private static string NormalizeTitle(string title) =>
        new(title.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
}

public sealed class LibraryPreferencesStore
{
    public HashSet<string> HiddenGameKeys { get; set; } = [];
    public HashSet<string> FavoriteGameKeys { get; set; } = [];
    public Dictionary<string, string> SelectedLaunchers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, DateTime> LastPlayedGames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> LaunchCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
