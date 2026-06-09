using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ludryn.Services;

public sealed class SteamGridDbService
{
    private const string BaseUrl = "https://www.steamgriddb.com/api/v2/";
    public const string ApiSettingsUrl = "https://www.steamgriddb.com/profile/preferences/api";

    private readonly HttpClient _httpClient;
    private readonly HttpClient _imageHttpClient;
    private readonly string _apiKey;
    private readonly string _cacheRoot;

    public SteamGridDbService(string apiKey)
    {
        _apiKey = apiKey;
        _httpClient = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _imageHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        _cacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ludryn", "ImageCache");
        Directory.CreateDirectory(Path.Combine(_cacheRoot, "covers"));
        Directory.CreateDirectory(Path.Combine(_cacheRoot, "heroes"));
        Directory.CreateDirectory(Path.Combine(_cacheRoot, "logos"));
        Directory.CreateDirectory(Path.Combine(_cacheRoot, "icons"));
        Directory.CreateDirectory(Path.Combine(_cacheRoot, "icon-previews"));
    }

    public static string? TryReadApiKey(out string message)
    {
        var path = GetWritableConfigPath();

        if (!File.Exists(path))
        {
            message = "Para carregar artes reais dos jogos, conecte sua API Key oficial do SteamGridDB em Configurações.";
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var apiKey = document.RootElement.GetProperty("SteamGridDB").GetProperty("ApiKey").GetString();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                message = "Nenhuma API Key do SteamGridDB foi configurada.";
                return null;
            }

            message = string.Empty;
            return apiKey;
        }
        catch (Exception ex)
        {
            message = $"Não foi possível ler a configuração do SteamGridDB: {ex.Message}";
            return null;
        }
    }

    public static void SaveApiKey(string apiKey)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(GetWritableConfigPath())!);
        var json = $$"""
        {
          "SteamGridDB": {
            "ApiKey": "{{JsonEncodedText.Encode(apiKey).ToString()}}"
          }
        }
        """;
        File.WriteAllText(GetWritableConfigPath(), json);
    }

    private static string GetWritableConfigPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ludryn", "appsettings.json");

    public async Task<int?> FindGameIdAsync(string gameName)
    {
        var response = await GetJsonAsync($"/search/autocomplete/{Uri.EscapeDataString(gameName)}");
        if (!TryGetDataArray(response, out var data) || data.GetArrayLength() == 0)
        {
            return null;
        }

        return data[0].GetProperty("id").GetInt32();
    }

    public async Task<string?> GetCoverArtUrlAsync(int gameId)
    {
        var response = await GetJsonAsync($"/grids/game/{gameId}?dimensions=600x900,342x482,600x900");
        return await GetFirstImageUrlAsync(response);
    }

    public async Task<IReadOnlyList<SteamGridDbImageOption>> GetCoverArtOptionsAsync(int gameId, int count = 6)
    {
        var response = await GetJsonAsync($"/grids/game/{gameId}");
        return await GetImageOptionsAsync(response, count);
    }

    public async Task<string?> GetHeroArtUrlAsync(int gameId)
    {
        var response = await GetJsonAsync($"/heroes/game/{gameId}");
        return await GetFirstImageUrlAsync(response);
    }

    public async Task<IReadOnlyList<SteamGridDbImageOption>> GetHeroArtOptionsAsync(int gameId, int count = 6)
    {
        var response = await GetJsonAsync($"/heroes/game/{gameId}");
        return await GetImageOptionsAsync(response, count);
    }

    public async Task<string?> GetLogoArtUrlAsync(int gameId)
    {
        var response = await GetJsonAsync($"/logos/game/{gameId}");
        return await GetFirstImageUrlAsync(response);
    }

    public async Task<IReadOnlyList<SteamGridDbImageOption>> GetLogoArtOptionsAsync(int gameId, int count = 6)
    {
        var response = await GetJsonAsync($"/logos/game/{gameId}");
        return await GetImageOptionsAsync(response, count);
    }

    public async Task<IReadOnlyList<SteamGridDbImageOption>> GetIconOptionsAsync(int gameId, int count = 6)
    {
        var response = await GetJsonAsync($"/icons/game/{gameId}");
        var options = await GetImageOptionsAsync(response, count);
        return await PrepareIconPreviewsAsync(options);
    }

    public async Task<string?> GetCoverBySteamIdAsync(int steamAppId)
    {
        var response = await GetJsonAsync($"/grids/steam/{steamAppId}?dimensions=600x900,342x482,600x900");
        return await GetFirstImageUrlAsync(response);
    }

    public async Task<IReadOnlyList<SteamGridDbImageOption>> GetCoverOptionsBySteamIdAsync(int steamAppId, int count = 6)
    {
        var response = await GetJsonAsync($"/grids/steam/{steamAppId}");
        return await GetImageOptionsAsync(response, count);
    }

    public async Task<BitmapImage?> GetCoverBitmapAsync(string cacheKey, string gameTitle, int? steamAppId)
    {
        var cachePath = GetCachePath("covers", cacheKey);
        if (File.Exists(cachePath))
        {
            return LoadBitmap(cachePath);
        }

        string? imageUrl = null;
        int? gameId = null;

        if (steamAppId is not null)
        {
            imageUrl = await GetCoverBySteamIdAsync(steamAppId.Value);
            gameId = steamAppId.Value;
        }

        if (imageUrl is null)
        {
            gameId = await FindGameIdAsync(gameTitle);
            if (gameId is null)
            {
                return null;
            }

            imageUrl = await GetCoverArtUrlAsync(gameId.Value);
        }

        return imageUrl is null ? null : await DownloadAndLoadAsync(imageUrl, cachePath);
    }

    public async Task<BitmapImage?> GetHeroBitmapAsync(string cacheKey, string gameTitle)
    {
        var cachePath = GetCachePath("heroes", cacheKey);
        if (File.Exists(cachePath))
        {
            return LoadBitmap(cachePath);
        }

        var gameId = await FindGameIdAsync(gameTitle);
        if (gameId is null)
        {
            return null;
        }

        var imageUrl = await GetHeroArtUrlAsync(gameId.Value);
        return imageUrl is null ? null : await DownloadAndLoadAsync(imageUrl, cachePath);
    }

    public async Task LoadGameArtAsync(IEnumerable<Models.Game> games, DispatcherQueue dispatcherQueue, Action<Models.Game>? gameUpdated = null, CancellationToken cancellationToken = default)
    {
        foreach (var game in games.Where(g => !g.IsPlatformEntry))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var cover = await GetCoverBitmapAsync(SanitizeCacheKey(game.Id), game.Title, game.SteamAppId);
                var hero = await GetHeroBitmapAsync(SanitizeCacheKey(game.Id), game.Title);
                if (cover is null && hero is null)
                {
                    continue;
                }

                dispatcherQueue.TryEnqueue(() =>
                {
                    if (cover is not null)
                    {
                        game.CoverArt = cover;
                        game.CoverArtUrl = cover.UriSource?.ToString();
                    }

                    if (hero is not null)
                    {
                        game.HeroArt = hero;
                        game.HeroArtUrl = hero.UriSource?.ToString();
                    }

                    gameUpdated?.Invoke(game);
                });
            }
            catch
            {
                // Placeholder art remains the fallback for network/API/cache errors.
            }

            await Task.Delay(120, cancellationToken);
        }
    }

    public async Task<CachedGameArt> EnsureGameArtCachedAsync(Models.Game game)
    {
        var cacheKey = SanitizeCacheKey(game.Id);
        var cachedArt = GetCachedGameArt(game);
        if (cachedArt.HasAnyArt)
        {
            return cachedArt;
        }

        var gameId = await FindGameIdAsync(game.Title);
        var cover = await EnsureCoverCachedAsync(cacheKey, game.Title, game.SteamAppId, gameId);
        var hero = await EnsureHeroCachedAsync(cacheKey, gameId);
        var logo = await EnsureLogoCachedAsync(cacheKey, gameId);
        return new CachedGameArt(cover.Path, hero.Path, logo.Path, cover.Url, hero.Url, logo.Url);
    }

    public CachedGameArt GetCachedGameArt(Models.Game game)
    {
        var cacheKey = SanitizeCacheKey(game.Id);
        return new CachedGameArt(
            FindExistingCachePath("covers", cacheKey),
            FindExistingCachePath("heroes", cacheKey),
            FindExistingCachePath("logos", cacheKey),
            null,
            null,
            null);
    }

    public void DeleteCachedArt(string cacheKey)
    {
        DeleteIfExists(GetCachePath("covers", SanitizeCacheKey(cacheKey)));
        DeleteIfExists(GetCachePath("heroes", SanitizeCacheKey(cacheKey)));
        DeleteIfExists(GetCachePath("logos", SanitizeCacheKey(cacheKey)));
        DeleteMatchingCacheFiles("covers", SanitizeCacheKey(cacheKey));
        DeleteMatchingCacheFiles("heroes", SanitizeCacheKey(cacheKey));
        DeleteMatchingCacheFiles("logos", SanitizeCacheKey(cacheKey));
    }

    public async Task<IReadOnlyList<SteamGridDbImageOption>> GetArtworkOptionsAsync(Models.Game game, ArtworkKind kind, int count = 6)
    {
        if (kind == ArtworkKind.Cover && game.SteamAppId is not null)
        {
            var steamOptions = await GetCoverOptionsBySteamIdAsync(game.SteamAppId.Value, count);
            if (steamOptions.Count > 0)
            {
                return steamOptions;
            }
        }

        var gameId = await FindGameIdAsync(game.Title);
        if (gameId is null)
        {
            return [];
        }

        return kind switch
        {
            ArtworkKind.Cover => await GetCoverArtOptionsAsync(gameId.Value, count),
            ArtworkKind.Hero => await GetHeroArtOptionsAsync(gameId.Value, count),
            ArtworkKind.Logo => await GetLogoArtOptionsAsync(gameId.Value, count),
            ArtworkKind.Icon => await GetIconOptionsAsync(gameId.Value, count),
            _ => []
        };
    }

    public async Task<CachedArtFile> ReplaceCachedArtworkAsync(string cacheKey, ArtworkKind kind, string imageUrl)
    {
        var safeKey = SanitizeCacheKey(cacheKey);
        var folder = kind switch
        {
            ArtworkKind.Cover => "covers",
            ArtworkKind.Hero => "heroes",
            ArtworkKind.Logo => "logos",
            ArtworkKind.Icon => "icons",
            _ => "covers"
        };
        DeleteMatchingCacheFiles(folder, safeKey);
        var cachePath = kind == ArtworkKind.Icon && IsIconUrl(imageUrl)
            ? Path.Combine(_cacheRoot, folder, $"{safeKey}-selected-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Guid.NewGuid():N}.png")
            : GetSelectedCachePath(folder, safeKey, imageUrl);
        if (kind == ArtworkKind.Icon && IsIconUrl(imageUrl))
        {
            await DownloadIconAsPngAsync(imageUrl, cachePath);
        }
        else
        {
            await DownloadAsync(imageUrl, cachePath);
        }

        return new CachedArtFile(cachePath, imageUrl);
    }

    private async Task<IReadOnlyList<SteamGridDbImageOption>> PrepareIconPreviewsAsync(
        IReadOnlyList<SteamGridDbImageOption> options)
    {
        using var throttle = new SemaphoreSlim(6);
        var tasks = options.Select(async option =>
        {
            if (!IsIconUrl(option.Url))
            {
                return option;
            }

            await throttle.WaitAsync();
            try
            {
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(option.Url)));
                var previewPath = Path.Combine(_cacheRoot, "icon-previews", $"{hash}.png");
                if (!File.Exists(previewPath))
                {
                    await DownloadIconAsPngAsync(option.Url, previewPath);
                }

                return option with
                {
                    PreviewUrl = previewPath,
                    Width = option.Width > 0 ? option.Width : 256,
                    Height = option.Height > 0 ? option.Height : 256
                };
            }
            catch (Exception ex)
            {
                LudrynLogger.Error("steamgriddb", $"ICO preview conversion failed: {option.Url}", ex);
                return option;
            }
            finally
            {
                throttle.Release();
            }
        });

        return await Task.WhenAll(tasks);
    }

    private async Task DownloadIconAsPngAsync(string imageUrl, string destinationPath)
    {
        if (File.Exists(destinationPath))
        {
            return;
        }

        var bytes = await _imageHttpClient.GetByteArrayAsync(imageUrl);
        await Task.Run(() =>
        {
            using var stream = new MemoryStream(bytes);
            using var icon = new System.Drawing.Icon(stream, 256, 256);
            using var bitmap = icon.ToBitmap();
            bitmap.Save(destinationPath, ImageFormat.Png);
        });
        LudrynLogger.Log("steamgriddb", $"SGDB ICO converted to PNG: {destinationPath} ({bytes.Length} bytes)");
    }

    private static bool IsIconUrl(string imageUrl) =>
        Path.GetExtension(new Uri(imageUrl).AbsolutePath).Equals(".ico", StringComparison.OrdinalIgnoreCase);

    private async Task<JsonDocument?> GetJsonAsync(string path)
    {
        var relativePath = path.TrimStart('/');
        Log($"SGDB Request: {new Uri(_httpClient.BaseAddress!, relativePath)}");

        using var response = await _httpClient.GetAsync(relativePath);
        var responseBody = await response.Content.ReadAsStringAsync();
        Log($"SGDB Response: {responseBody}");

        if (!response.IsSuccessStatusCode)
        {
            Log($"SGDB HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
            return null;
        }

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(responseBody);
        }
        catch (JsonException ex)
        {
            Log($"SGDB JSON parse failed: {ex.Message}");
            Log($"SGDB Response Body Before Parse Failure: {responseBody}");
            return null;
        }
    }

    private static void Log(string message)
    {
        LudrynLogger.Log("steamgriddb", message);
    }

    private async Task<string?> GetFirstImageUrlAsync(JsonDocument? document)
    {
        return (await GetImageOptionsAsync(document, 1)).FirstOrDefault()?.Url;
    }

    private async Task<IReadOnlyList<SteamGridDbImageOption>> GetImageOptionsAsync(JsonDocument? document, int count)
    {
        if (!TryGetDataArray(document, out var data) || data.GetArrayLength() == 0)
        {
            return [];
        }

        var candidates = new List<(int Index, SteamGridDbImageOption Option)>();
        var index = 0;
        foreach (var item in data.EnumerateArray())
        {
            if (item.TryGetProperty("url", out var url) && !string.IsNullOrWhiteSpace(url.GetString()))
            {
                var width = TryGetInt(item, "width");
                var height = TryGetInt(item, "height");
                var imageUrl = url.GetString()!;
                candidates.Add((index, new SteamGridDbImageOption(imageUrl, width, height)));
            }

            index++;
            if (candidates.Count >= Math.Max(count * 6, 24))
            {
                break;
            }
        }

        return candidates
            .OrderBy(candidate => candidate.Index)
            .Select(candidate => candidate.Option)
            .Take(count)
            .ToList();
    }

    private static int TryGetInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value)
            ? value
            : 0;
    }

    private static bool TryGetDataArray(JsonDocument? document, out JsonElement data)
    {
        data = default;
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!document.RootElement.TryGetProperty("data", out data) || data.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        if (document.RootElement.TryGetProperty("success", out var success) &&
            success.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        return true;
    }

    private async Task<BitmapImage?> DownloadAndLoadAsync(string imageUrl, string cachePath)
    {
        await DownloadAsync(imageUrl, cachePath);
        return LoadBitmap(cachePath);
    }

    private async Task<CachedArtFile> EnsureCoverCachedAsync(string cacheKey, string gameTitle, int? steamAppId, int? gameId)
    {
        var cachedPath = FindExistingCachePath("covers", cacheKey);
        if (cachedPath is not null)
        {
            return new CachedArtFile(cachedPath, null);
        }

        string? imageUrl = null;
        if (steamAppId is not null)
        {
            imageUrl = await GetCoverBySteamIdAsync(steamAppId.Value);
        }

        if (imageUrl is null)
        {
            if (gameId is not null)
            {
                imageUrl = await GetCoverArtUrlAsync(gameId.Value);
            }
        }

        if (imageUrl is null)
        {
            return new CachedArtFile(null, null);
        }

        var cachePath = GetCachePath("covers", cacheKey, imageUrl);
        await DownloadAsync(imageUrl, cachePath);
        return new CachedArtFile(cachePath, imageUrl);
    }

    private async Task<CachedArtFile> EnsureHeroCachedAsync(string cacheKey, int? gameId)
    {
        var cachedPath = FindExistingCachePath("heroes", cacheKey);
        if (cachedPath is not null)
        {
            return new CachedArtFile(cachedPath, null);
        }

        if (gameId is null)
        {
            return new CachedArtFile(null, null);
        }

        var imageUrl = await GetHeroArtUrlAsync(gameId.Value);
        if (imageUrl is null)
        {
            return new CachedArtFile(null, null);
        }

        var cachePath = GetCachePath("heroes", cacheKey, imageUrl);
        await DownloadAsync(imageUrl, cachePath);
        return new CachedArtFile(cachePath, imageUrl);
    }

    private async Task<CachedArtFile> EnsureLogoCachedAsync(string cacheKey, int? gameId)
    {
        var cachedPath = FindExistingCachePath("logos", cacheKey);
        if (cachedPath is not null)
        {
            return new CachedArtFile(cachedPath, null);
        }

        if (gameId is null)
        {
            return new CachedArtFile(null, null);
        }

        var imageUrl = await GetLogoArtUrlAsync(gameId.Value);
        if (imageUrl is null)
        {
            return new CachedArtFile(null, null);
        }

        var cachePath = GetCachePath("logos", cacheKey, imageUrl);
        await DownloadAsync(imageUrl, cachePath);
        return new CachedArtFile(cachePath, imageUrl);
    }

    private async Task DownloadAsync(string imageUrl, string cachePath)
    {
        if (File.Exists(cachePath))
        {
            return;
        }

        byte[] bytes;
        try
        {
            bytes = await _imageHttpClient.GetByteArrayAsync(imageUrl);
        }
        catch (HttpRequestException ex)
        {
            LudrynLogger.Error("steamgriddb", $"SGDB Image Download Failed: {imageUrl}", ex);
            throw;
        }

        await File.WriteAllBytesAsync(cachePath, bytes);
        LudrynLogger.Log("steamgriddb", $"SGDB Cache Saved: {cachePath} ({bytes.Length} bytes)");
    }

    private static BitmapImage LoadBitmap(string path) =>
        new(new Uri(path));

    private string GetCachePath(string kind, string cacheKey) =>
        Path.Combine(_cacheRoot, kind, $"{cacheKey}.jpg");

    private string GetCachePath(string kind, string cacheKey, string imageUrl) =>
        Path.Combine(_cacheRoot, kind, $"{cacheKey}{GetImageExtension(imageUrl)}");

    private string GetSelectedCachePath(string kind, string cacheKey, string imageUrl) =>
        Path.Combine(_cacheRoot, kind, $"{cacheKey}-selected-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Guid.NewGuid():N}{GetImageExtension(imageUrl)}");

    private string? FindExistingCachePath(string kind, string cacheKey)
    {
        var folder = Path.Combine(_cacheRoot, kind);
        var selectedPath = FindCompatibleCacheFile(folder, $"{cacheKey}-selected-*.*");
        if (selectedPath is not null)
        {
            return selectedPath;
        }

        var legacyExactSelectedPath = FindCompatibleCacheFile(folder, $"{cacheKey}-selected.*");
        if (legacyExactSelectedPath is not null)
        {
            return legacyExactSelectedPath;
        }

        var legacySelectedPath = FindCompatibleCacheFile(folder, $"{cacheKey}-*.*");
        if (legacySelectedPath is not null)
        {
            return legacySelectedPath;
        }

        return FindCompatibleCacheFile(folder, $"{cacheKey}.*");
    }

    private string? FindCompatibleCacheFile(string folder, string pattern)
    {
        if (!Directory.Exists(folder))
        {
            return null;
        }

        foreach (var path in Directory.EnumerateFiles(folder, pattern).OrderByDescending(File.GetLastWriteTimeUtc))
        {
            if (IsExtensionCompatibleWithContent(path))
            {
                return path;
            }

            DeleteIfExists(path);
        }

        return null;
    }

    private void DeleteMatchingCacheFiles(string kind, string cacheKey)
    {
        var folder = Path.Combine(_cacheRoot, kind);
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(folder)
                     .Where(path => IsCacheFileForKey(path, cacheKey)))
        {
            DeleteIfExists(path);
        }
    }

    private static bool IsCacheFileForKey(string path, string cacheKey)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        return string.Equals(fileName, cacheKey, StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith($"{cacheKey}-selected-", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, $"{cacheKey}-selected", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith($"{cacheKey}-selected-local-", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetImageExtension(string imageUrl)
    {
        var extension = Path.GetExtension(new Uri(imageUrl).AbsolutePath).ToLowerInvariant();
        return extension is ".png" or ".jpg" or ".jpeg" or ".webp" ? extension : ".jpg";
    }

    private static bool IsExtensionCompatibleWithContent(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
            {
                return false;
            }

            var extension = Path.GetExtension(path).ToLowerInvariant();
            Span<byte> header = stackalloc byte[12];
            using var stream = File.OpenRead(path);
            var read = stream.Read(header);

            var isPng = read >= 8 &&
                header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47;
            var isJpeg = read >= 3 &&
                header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
            var isWebp = read >= 12 &&
                header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 &&
                header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50;

            var hasCompatibleHeader = extension switch
            {
                ".png" => isPng,
                ".jpg" or ".jpeg" => isJpeg,
                ".webp" => isWebp,
                _ => false
            };

            if (!hasCompatibleHeader)
            {
                return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static string SanitizeCacheKey(string key)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            key = key.Replace(invalid, '-');
        }

        return key;
    }
}

public sealed record CachedGameArt(string? CoverPath, string? HeroPath, string? LogoPath, string? CoverUrl, string? HeroUrl, string? LogoUrl)
{
    public bool HasAnyArt => CoverPath is not null || HeroPath is not null || LogoPath is not null;
}

public sealed record CachedArtFile(string? Path, string? Url);

public sealed record SteamGridDbImageOption(string Url, int Width, int Height)
{
    public string DisplayUrl => PreviewUrl ?? Url;
    public string? PreviewUrl { get; init; }
}

public enum ArtworkKind
{
    Cover,
    Hero,
    Logo,
    Icon
}
