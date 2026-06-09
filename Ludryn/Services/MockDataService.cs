using Ludryn.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Ludryn.Services;

public sealed class MockDataService
{
    private readonly List<Game> _games = [];
    private readonly List<Game> _detectedGames = [];
    private readonly List<Game> _customGames = [];
    private readonly List<Game> _customStores = [];
    private readonly EmulatorConfigService _emulatorConfigService = new();
    private readonly ProfileService _profileService = new();
    private readonly LibraryPreferencesService _preferencesService;
    private readonly HashSet<string> _artworkReadyIds = [];
    private readonly HashSet<string> _artworkAttemptedIds = [];
    private readonly HashSet<string> _artworkLoadingIds = [];
    private readonly object _artworkLock = new();
    private SteamGridDbService? _steamGridDbService;
    private DateTime _lastLibraryScan = DateTime.MinValue;
    private string _lastInstallSignature = string.Empty;

    public MockDataService()
    {
        _preferencesService = new LibraryPreferencesService(_profileService);
        _customStores.AddRange(StoreLauncherService.GetCustomStores());

        SteamGridDbMessage = SteamGridDbService.TryReadApiKey(out var message) is not null
            ? string.Empty
            : message;

        if (string.IsNullOrEmpty(SteamGridDbMessage))
        {
            var apiKey = SteamGridDbService.TryReadApiKey(out _);
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                _steamGridDbService = new SteamGridDbService(apiKey);
            }
        }

        SyncInstalledLibraries(force: true);
    }

    public string SteamGridDbMessage { get; }
    public bool HasSteamGridDb => _steamGridDbService is not null;
    public LudrynProfile CurrentProfile => _profileService.CurrentProfile;
    public bool IsSecondaryProfile => _profileService.IsSecondary;
    public bool SecondaryProfileHasPassword => _profileService.HasPassword;
    public int LibraryVersion { get; private set; }
    public event EventHandler? ArtworkProgressChanged;
    public int ArtworkLoadedCount
    {
        get
        {
            lock (_artworkLock)
            {
                return _artworkReadyIds.Count;
            }
        }
    }

    public int ArtworkTotalCount => GetAllGames().Count(g => !g.IsPlatformEntry);

    public IReadOnlyList<Game> GetAllGames()
    {
        return _games
            .Concat(_detectedGames)
            .Concat(_customGames)
            .Select(PrepareGameVisuals)
            .Where(g => !_preferencesService.IsHidden(g))
            .Where(_profileService.IsVisible)
            .ToList();
    }
    public IReadOnlyList<Game> GetRecentGames(int count = 8) => GetAllGames().OrderByDescending(g => g.LastPlayed).Take(count).ToList();
    public IReadOnlyList<string> GetPlatforms() => GetAllGames().Select(g => g.Platform).Distinct().OrderBy(p => p).ToList();
    public IReadOnlyList<Game> GetGamesByPlatform(string platform) =>
        GetAllGames().Where(g =>
            g.Platform == platform ||
            g.Installations.Any(i => string.Equals(i.Launcher, platform, StringComparison.OrdinalIgnoreCase))).ToList();
    public IReadOnlyList<Game> GetCustomStores() => _customStores;
    public IReadOnlyList<Game> GetStoreEntries() =>
        StoreLauncherService.GetBuiltInStores()
            .Concat(_customStores)
            .Select(store =>
            {
                _preferencesService.Apply(store);
                return store;
            })
            .ToList();
    public IReadOnlyList<EmulatorEntry> GetConfiguredEmulators() => _emulatorConfigService.Emulators;
    public IReadOnlyList<RomDirectoryEntry> GetConfiguredRomDirectories() => _emulatorConfigService.RomDirectories;
    public IReadOnlyList<string> GetConfiguredGameDirectories() => AppSettingsService.GameDirectories;
    public IReadOnlyList<Game> PreviewRomDirectory(string path, string platform) =>
        _emulatorConfigService.PreviewRomDirectory(path, platform);

    public void RefreshInstalledSteamGames() => SyncInstalledLibraries(force: true);
    public void AddEmulator(string name, string executablePath, string platform)
    {
        _emulatorConfigService.AddEmulator(name, executablePath, platform);
        SyncInstalledLibraries(force: true);
    }

    public void RemoveEmulator(EmulatorEntry emulator)
    {
        _emulatorConfigService.RemoveEmulator(emulator);
        SyncInstalledLibraries(force: true);
    }

    public void AddRomDirectory(string path, string platform)
    {
        _emulatorConfigService.AddRomDirectory(path, platform);
        SyncInstalledLibraries(force: true);
    }

    public void RemoveRomDirectory(RomDirectoryEntry directory)
    {
        _emulatorConfigService.RemoveRomDirectory(directory);
        SyncInstalledLibraries(force: true);
    }

    public void AddGameDirectory(string path)
    {
        AppSettingsService.AddGameDirectory(path);
        SyncInstalledLibraries(force: true);
    }

    public void RemoveGameDirectory(string path)
    {
        AppSettingsService.RemoveGameDirectory(path);
        SyncInstalledLibraries(force: true);
    }

    public void ToggleFavorite(Game game)
    {
        game.IsFavorite = !game.IsFavorite;
        _preferencesService.SetFavorite(game, game.IsFavorite);
        LibraryVersion++;
    }

    public bool IsPrivateGame(Game game) => _profileService.IsPrivate(game);

    public void SetPrivateGame(Game game, bool isPrivate)
    {
        _profileService.SetPrivate(game, isPrivate);
        LibraryVersion++;
    }

    public void SetSecondaryProfilePassword(IReadOnlyList<string> sequence) =>
        _profileService.SetPassword(sequence);

    public bool VerifySecondaryProfilePassword(IReadOnlyList<string> sequence) =>
        _profileService.VerifyPassword(sequence);

    public void SwitchProfile(LudrynProfile profile)
    {
        _profileService.SwitchTo(profile);
        foreach (var game in GetRawGames())
        {
            _preferencesService.Apply(game);
            _profileService.ApplyArtwork(game);
        }

        LibraryVersion++;
    }

    public void SelectLauncher(Game game, string launcher)
    {
        if (!game.Installations.Any(i => string.Equals(i.Launcher, launcher, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        game.SelectedLauncher = launcher;
        _preferencesService.SetSelectedLauncher(game, launcher);
        LibraryVersion++;
    }

    public void RecordGameLaunched(Game game)
    {
        var launchedAt = DateTime.Now;
        game.LastPlayed = launchedAt;
        _preferencesService.SetLastPlayed(game, launchedAt);
        _preferencesService.IncrementLaunchCount(game);
        LibraryVersion++;
    }

    public async Task<IReadOnlyList<SteamGridDbImageOption>> GetArtworkOptionsAsync(Game game, ArtworkKind kind, int count = 48)
    {
        var service = _steamGridDbService;
        if (service is null)
        {
            LudrynLogger.Log("artwork", $"Options ignored without SteamGridDB service. Game={game.Title}; Kind={kind}");
            return [];
        }

        LudrynLogger.Log("artwork", $"Options requested. Game={game.Title}; Id={game.Id}; Kind={kind}; Count={count}; SteamAppId={game.SteamAppId?.ToString() ?? "none"}");
        var options = await service.GetArtworkOptionsAsync(game, kind, count);
        LudrynLogger.Log("artwork", $"Options returned. Game={game.Title}; Kind={kind}; Count={options.Count}");
        return options;
    }

    public async Task<bool> ApplyArtworkSelectionAsync(Game game, ArtworkKind kind, string imageUrl, DispatcherQueue dispatcherQueue)
    {
        var service = _steamGridDbService;
        if (service is null)
        {
            LudrynLogger.Log("artwork", $"Apply ignored without SteamGridDB service. Game={game.Title}; Kind={kind}; Url={imageUrl}");
            return false;
        }

        LudrynLogger.Log("artwork", $"Apply requested. Game={game.Title}; Id={game.Id}; Kind={kind}; Url={imageUrl}");
        var selectedArt = await service.ReplaceCachedArtworkAsync(
            _profileService.GetArtworkCacheKey(game),
            kind,
            imageUrl);
        if (string.IsNullOrWhiteSpace(selectedArt.Path))
        {
            LudrynLogger.Log("artwork", $"Apply failed without cached path. Game={game.Title}; Kind={kind}; Url={imageUrl}");
            return false;
        }

        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (kind == ArtworkKind.Icon)
                {
                    game.PlatformIcon = new BitmapImage(new Uri(selectedArt.Path));
                    StoreLauncherService.SaveStoreIcon(game.Id, selectedArt.Path);
                }
                else
                {
                    _profileService.ApplyArtwork(game);
                }

                applied.SetResult();
            }
            catch (Exception ex)
            {
                LudrynLogger.Error("artwork", $"Apply UI update failed. Game={game.Title}; Kind={kind}; Path={selectedArt.Path}", ex);
                applied.SetException(ex);
            }
        }))
        {
            LudrynLogger.Log("artwork", $"Apply failed because DispatcherQueue refused enqueue. Game={game.Title}; Kind={kind}; Path={selectedArt.Path}");
            return false;
        }

        await applied.Task;
        LudrynLogger.Log("artwork", $"Apply completed. Game={game.Title}; Kind={kind}; Path={selectedArt.Path}");
        return true;
    }

    public async Task<bool> ApplyLocalArtworkAsync(Game game, ArtworkKind kind, string sourcePath, DispatcherQueue dispatcherQueue)
    {
        LudrynLogger.Log("artwork", $"Local artwork requested. Game={game.Title}; Kind={kind}; Source={sourcePath}");
        var cachedPath = await LocalArtworkService.ImportAsync(
            _profileService.GetArtworkCacheKey(game),
            kind,
            sourcePath);
        if (string.IsNullOrWhiteSpace(cachedPath))
        {
            return false;
        }

        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                _profileService.ApplyArtwork(game);

                applied.SetResult();
            }
            catch (Exception ex)
            {
                applied.SetException(ex);
            }
        }))
        {
            return false;
        }

        await applied.Task;
        LudrynLogger.Log("artwork", $"Local artwork applied. Game={game.Title}; Kind={kind}; Path={cachedPath}");
        return true;
    }

    public async Task LoadArtworkAsync(IEnumerable<Game> games, DispatcherQueue dispatcherQueue, Action<Game>? gameUpdated = null, CancellationToken cancellationToken = default)
    {
        var service = _steamGridDbService;
        var distinctGames = games
            .Where(g => !g.IsPlatformEntry)
            .DistinctBy(g => g.Id)
            .ToList();

        foreach (var game in distinctGames)
        {
            var cachedArt = LocalArtworkService.GetCachedGameArt(game);
            if (cachedArt.HasAnyArt)
            {
                await ApplyCachedArtAsync(game, cachedArt, dispatcherQueue, gameUpdated);
                MarkArtworkAttempted(game.Id, ready: true);
            }
        }

        if (service is null)
        {
            NotifyArtworkProgressChanged();
            return;
        }

        foreach (var game in distinctGames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsArtworkAttempted(game.Id))
            {
                continue;
            }

            var cachedArt = service.GetCachedGameArt(game);
            if (cachedArt.HasAnyArt)
            {
                await ApplyCachedArtAsync(game, cachedArt, dispatcherQueue, gameUpdated);
                MarkArtworkAttempted(game.Id, ready: true);
            }
        }

        var queue = distinctGames
            .Where(TryReserveArtworkLoad)
            .ToList();

        foreach (var game in queue)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hasAnyArt = false;

            try
            {
                var art = await service.EnsureGameArtCachedAsync(game);
                hasAnyArt = art.HasAnyArt;
                dispatcherQueue.TryEnqueue(() =>
                {
                    var updated = false;

                    if (!string.IsNullOrWhiteSpace(art.CoverPath))
                    {
                        game.CoverArt = new BitmapImage(new Uri(art.CoverPath));
                        game.CoverArtUrl = art.CoverUrl ?? art.CoverPath;
                        game.HasRealCoverArt = true;
                        updated = true;
                    }

                    if (!string.IsNullOrWhiteSpace(art.HeroPath))
                    {
                        game.HeroArt = new BitmapImage(new Uri(art.HeroPath));
                        game.HeroArtUrl = art.HeroUrl ?? art.HeroPath;
                        updated = true;
                    }

                    if (!string.IsNullOrWhiteSpace(art.LogoPath))
                    {
                        game.LogoArt = new BitmapImage(new Uri(art.LogoPath));
                        game.LogoArtUrl = art.LogoUrl ?? art.LogoPath;
                        updated = true;
                    }

                    if (updated)
                    {
                        if (_profileService.IsSecondary)
                        {
                            _profileService.ApplyArtwork(game);
                        }

                        gameUpdated?.Invoke(game);
                    }
                });
            }
            catch
            {
                // Placeholder art remains the fallback for network/API/cache errors.
            }
            finally
            {
                MarkArtworkAttempted(game.Id, hasAnyArt);
            }

            await Task.Delay(90, cancellationToken);
        }
    }

    private bool IsArtworkAttempted(string gameId)
    {
        lock (_artworkLock)
        {
            return _artworkAttemptedIds.Contains(gameId);
        }
    }

    private async Task ApplyCachedArtAsync(Game game, CachedGameArt art, DispatcherQueue dispatcherQueue, Action<Game>? gameUpdated)
    {
        var applied = new TaskCompletionSource();
        if (!dispatcherQueue.TryEnqueue(() =>
        {
            var updated = false;

            if (!string.IsNullOrWhiteSpace(art.CoverPath))
            {
                game.CoverArt = new BitmapImage(new Uri(art.CoverPath));
                game.CoverArtUrl = art.CoverUrl ?? art.CoverPath;
                game.HasRealCoverArt = true;
                updated = true;
            }

            if (!string.IsNullOrWhiteSpace(art.HeroPath))
            {
                game.HeroArt = new BitmapImage(new Uri(art.HeroPath));
                game.HeroArtUrl = art.HeroUrl ?? art.HeroPath;
                updated = true;
            }

            if (!string.IsNullOrWhiteSpace(art.LogoPath))
            {
                game.LogoArt = new BitmapImage(new Uri(art.LogoPath));
                game.LogoArtUrl = art.LogoUrl ?? art.LogoPath;
                updated = true;
            }

            if (updated)
            {
                if (_profileService.IsSecondary)
                {
                    _profileService.ApplyArtwork(game);
                }

                gameUpdated?.Invoke(game);
            }

            applied.SetResult();
        }))
        {
            return;
        }

        await applied.Task;
    }

    public void SaveSteamGridDbApiKey(string apiKey)
    {
        SteamGridDbService.SaveApiKey(apiKey);
        _steamGridDbService = new SteamGridDbService(apiKey);
        lock (_artworkLock)
        {
            _artworkReadyIds.Clear();
            _artworkAttemptedIds.Clear();
            _artworkLoadingIds.Clear();
        }

        NotifyArtworkProgressChanged();
    }

    public Game AddCustomGame(string title, string executablePath = "", string arguments = "", string platform = "PC")
    {
        var game = Create($"custom-game-{Guid.NewGuid():N}", title, platform, "Adicionado", 0);
        game.Installations.Clear();
        game.Installations.Add(new GameInstallation
        {
            Launcher = platform,
            LaunchId = executablePath,
            InstallPath = Path.GetDirectoryName(executablePath) ?? string.Empty,
            LaunchArguments = arguments,
            IsDetected = false
        });
        _customGames.Add(game);
        LibraryVersion++;
        NotifyArtworkProgressChanged();
        return game;
    }

    public Game AddCustomStore(string title, string executablePath)
    {
        var store = StoreLauncherService.CreateCustomStore(title, executablePath);
        _customStores.Add(store);
        LibraryVersion++;
        NotifyArtworkProgressChanged();
        return store;
    }

    public void SetStoreExecutable(Game store, string executablePath)
    {
        if (!store.IsStoreEntry)
        {
            return;
        }

        StoreLauncherService.SaveManualExecutable(store.Title, executablePath);
        store.StoreExecutablePath = executablePath;
        store.Installations.Clear();
        store.Installations.Add(new GameInstallation
        {
            Launcher = store.Title,
            LaunchId = executablePath,
            InstallPath = Path.GetDirectoryName(executablePath) ?? string.Empty,
            IsDetected = false
        });
        LibraryVersion++;
    }

    public void RemoveGame(Game game)
    {
        _preferencesService.Hide(game);
        var removedStore = false;
        if (game.IsStoreEntry)
        {
            StoreLauncherService.RemoveStore(game.Id);
            removedStore = true;
        }

        var removed = RemoveMatchingGame(_games, game) ||
                      RemoveMatchingGame(_detectedGames, game) ||
                      RemoveMatchingGame(_customGames, game) ||
                      _customStores.Remove(game) ||
                      removedStore;
        _steamGridDbService?.DeleteCachedArt(game.Id);
        if (removed)
        {
            lock (_artworkLock)
            {
                _artworkReadyIds.Remove(game.Id);
                _artworkAttemptedIds.Remove(game.Id);
                _artworkLoadingIds.Remove(game.Id);
            }

            LibraryVersion++;
            NotifyArtworkProgressChanged();
        }
    }

    private static bool RemoveMatchingGame(List<Game> games, Game game)
    {
        var removed = false;
        for (var i = games.Count - 1; i >= 0; i--)
        {
            if (IsSameGame(games[i], game))
            {
                games.RemoveAt(i);
                removed = true;
            }
        }

        return removed;
    }

    private bool TryReserveArtworkLoad(Game game)
    {
        lock (_artworkLock)
        {
            if (_artworkAttemptedIds.Contains(game.Id) || _artworkLoadingIds.Contains(game.Id))
            {
                return false;
            }

            _artworkLoadingIds.Add(game.Id);
            return true;
        }
    }

    private void MarkArtworkAttempted(string gameId, bool ready)
    {
        lock (_artworkLock)
        {
            _artworkLoadingIds.Remove(gameId);
            if (ready)
            {
                _artworkAttemptedIds.Add(gameId);
                _artworkReadyIds.Add(gameId);
            }
        }

        NotifyArtworkProgressChanged();
    }

    private void NotifyArtworkProgressChanged() =>
        ArtworkProgressChanged?.Invoke(this, EventArgs.Empty);

    private void SyncInstalledLibraries(bool force = false)
    {
        if (!force && (DateTime.Now - _lastLibraryScan).TotalSeconds < 20)
        {
            return;
        }

        _lastLibraryScan = DateTime.Now;
        RemoveDetectedInstallations(_games);
        RemoveDetectedInstallations(_detectedGames);
        RemoveDetectedInstallations(_customGames);

        var scannedGames = SteamLibraryScanner.ScanInstalledGames()
            .Concat(EpicLibraryScanner.ScanInstalledGames())
            .Concat(ThirdPartyStoreScanner.ScanInstalledGames())
            .Concat(WindowsGameScanner.ScanInstalledGames())
            .Concat(ManualGameDirectoryScanner.Scan())
            .Concat(_emulatorConfigService.ScanRomGames())
            .ToList();

        var mergedDetectedGames = new List<Game>();
        foreach (var scannedGame in scannedGames)
        {
            var target = FindMatchingGame(scannedGame, _games)
                ?? FindMatchingGame(scannedGame, _customGames)
                ?? FindMatchingGame(scannedGame, _detectedGames)
                ?? FindMatchingGame(scannedGame, mergedDetectedGames);

            if (target is not null)
            {
                MergeInstallation(target, scannedGame);
                _preferencesService.Apply(target);
                if (!_games.Contains(target) &&
                    !_customGames.Contains(target) &&
                    !mergedDetectedGames.Contains(target))
                {
                    mergedDetectedGames.Add(target);
                }

                continue;
            }

            _preferencesService.Apply(scannedGame);
            mergedDetectedGames.Add(scannedGame);
        }

        mergedDetectedGames.RemoveAll(_preferencesService.IsHidden);

        var newSignature = BuildDetectedSignature(_games.Concat(_customGames).Concat(mergedDetectedGames));
        if (_lastInstallSignature == newSignature)
        {
            return;
        }

        var removedIds = _detectedGames.Select(g => g.Id).Except(mergedDetectedGames.Select(g => g.Id)).ToList();
        foreach (var removedId in removedIds)
        {
            lock (_artworkLock)
            {
                _artworkReadyIds.Remove(removedId);
                _artworkAttemptedIds.Remove(removedId);
                _artworkLoadingIds.Remove(removedId);
            }
        }

        _detectedGames.Clear();
        _detectedGames.AddRange(mergedDetectedGames);
        ApplyPreferences(_games);
        ApplyPreferences(_customGames);
        _lastInstallSignature = newSignature;
        LibraryVersion++;
        NotifyArtworkProgressChanged();
    }

    private void ApplyPreferences(IEnumerable<Game> games)
    {
        foreach (var game in games)
        {
            _preferencesService.Apply(game);
        }
    }

    private IEnumerable<Game> GetRawGames() =>
        _games.Concat(_detectedGames).Concat(_customGames);

    private static Game PrepareGameVisuals(Game game)
    {
        if (game.CoverArt is null)
        {
            game.CoverArt = PlaceholderArtGenerator.CreateCover(game.Title, game.CoverArtColor);
        }

        if (game.HeroArt is null)
        {
            game.HeroArt = PlaceholderArtGenerator.CreateHero(game.Title, game.CoverArtColor, game.AccentColor);
        }

        if (IsEmulatorPlatform(game.Platform))
        {
            game.PlatformIcon = PlatformIconService.GetIcon(game.Platform);
        }
        else if (game.PlatformIcon is null)
        {
            game.PlatformIcon = PlatformIconService.GetIcon(game.SelectedLauncher);
        }

        return game;
    }

    private static void RemoveDetectedInstallations(IEnumerable<Game> games)
    {
        foreach (var game in games)
        {
            game.Installations.RemoveAll(i => i.IsDetected);
        }
    }

    private static Game? FindMatchingGame(Game scannedGame, IEnumerable<Game> games) =>
        games.FirstOrDefault(game => IsSameGame(game, scannedGame));

    private static bool IsSameGame(Game first, Game second)
    {
        if (first.SteamAppId is not null && second.SteamAppId is not null && first.SteamAppId == second.SteamAppId)
        {
            return true;
        }

        return NormalizeTitle(first.Title) == NormalizeTitle(second.Title);
    }

    private static void MergeInstallation(Game target, Game scannedGame)
    {
        foreach (var installation in scannedGame.Installations)
        {
            if (!target.Installations.Any(i => string.Equals(i.Launcher, installation.Launcher, StringComparison.OrdinalIgnoreCase)))
            {
                target.Installations.Add(installation);
            }
        }

        if (target.SteamAppId is null && scannedGame.SteamAppId is not null)
        {
            target.SteamAppId = scannedGame.SteamAppId;
        }

        if (IsEmulatorPlatform(scannedGame.Platform) && !IsEmulatorPlatform(target.Platform))
        {
            target.Platform = scannedGame.Platform;
        }

        if (IsEmulatorPlatform(scannedGame.Platform))
        {
            target.PlatformIcon = PlatformIconService.GetIcon(scannedGame.Platform);
        }

        if (target.LastPlayed < scannedGame.LastPlayed)
        {
            target.LastPlayed = scannedGame.LastPlayed;
        }
        if (target.AddedAt == default || (scannedGame.AddedAt != default && target.AddedAt < scannedGame.AddedAt))
        {
            target.AddedAt = scannedGame.AddedAt;
        }
    }

    private static bool IsEmulatorPlatform(string platform) =>
        platform is "Nintendo Switch"
            or "Wii"
            or "Wii U"
            or "GameCube"
            or "NES"
            or "Game Boy"
            or "Game Boy Color"
            or "Nintendo DS"
            or "Nintendo 3DS"
            or "Mega Drive"
            or "Genesis"
            or "Master System"
            or "Game Gear"
            or "Sega Saturn"
            or "Dreamcast"
            or "Arcade"
            or "Nintendo 64"
            or "Super Nintendo"
            or "Game Boy Advance"
            or "PlayStation"
            or "PlayStation 1"
            or "PlayStation 2"
            or "PlayStation 3"
            or "PSP"
            or "PS1"
            or "PS2"
            or "PS3"
            or "GBA"
            or "SNES"
            or "N64";

    private static string BuildDetectedSignature(IEnumerable<Game> games) =>
        string.Join("|", games
            .Select(g => $"{g.Id}:{string.Join(",", g.Installations.Select(i => i.Launcher).Order())}")
            .Order());

    private static string NormalizeTitle(string title) =>
        new(title.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static Game Create(string id, string title, string platform, string playTime, int daysAgo, int? steamAppId = null)
    {
        var cover = PlaceholderArtGenerator.ColorFromTitle(title);
        var accent = PlaceholderArtGenerator.AccentFrom(cover);
        var game = new Game
        {
            Id = id,
            Title = title,
            Platform = platform,
            SteamAppId = steamAppId,
            CoverArtColor = cover,
            AccentColor = accent,
            PlayTime = playTime,
            LastPlayed = DateTime.Now.AddDays(daysAgo),
            CoverArt = PlaceholderArtGenerator.CreateCover(title, cover),
            HeroArt = PlaceholderArtGenerator.CreateHero(title, cover, accent),
            PlatformIcon = PlatformIconService.GetIcon(platform)
        };
        game.Installations.Add(new GameInstallation
        {
            Launcher = platform,
            LaunchId = steamAppId?.ToString() ?? id,
            IsDetected = false
        });
        game.SelectedLauncher = platform;
        return game;
    }
}
