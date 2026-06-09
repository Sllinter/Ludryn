using Ludryn.Models;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ludryn.Services;

public enum LudrynProfile
{
    Main,
    Secondary
}

public sealed class ProfileService
{
    private static readonly string ProfilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Ludryn",
        "profiles.json");

    private readonly ProfileStore _store = Load();

    public LudrynProfile CurrentProfile { get; private set; } = LudrynProfile.Main;
    public bool HasPassword => !string.IsNullOrWhiteSpace(_store.SecondaryPasswordHash);
    public bool IsSecondary => CurrentProfile == LudrynProfile.Secondary;

    public void SwitchTo(LudrynProfile profile) => CurrentProfile = profile;

    public bool IsVisible(Game game) =>
        IsSecondary || !_store.PrivateGameIds.Contains(game.Id);

    public bool IsPrivate(Game game) => _store.PrivateGameIds.Contains(game.Id);

    public void SetPrivate(Game game, bool isPrivate)
    {
        if (isPrivate)
        {
            _store.PrivateGameIds.Add(game.Id);
        }
        else
        {
            _store.PrivateGameIds.Remove(game.Id);
        }

        Save();
    }

    public void SetPassword(IReadOnlyList<string> sequence)
    {
        _store.SecondaryPasswordHash = HashSequence(sequence);
        Save();
    }

    public bool VerifyPassword(IReadOnlyList<string> sequence)
    {
        try
        {
            return HasPassword && CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(_store.SecondaryPasswordHash),
                Convert.FromHexString(HashSequence(sequence)));
        }
        catch
        {
            return false;
        }
    }

    public string GetArtworkCacheKey(Game game) =>
        IsSecondary ? $"{game.Id}-secondary-profile" : game.Id;

    public void ApplyArtwork(Game game)
    {
        var mainArt = LocalArtworkService.GetCachedGameArt(game);
        var selectedArt = mainArt;

        if (IsSecondary)
        {
            var secondaryGame = new Game { Id = GetArtworkCacheKey(game) };
            var secondaryArt = LocalArtworkService.GetCachedGameArt(secondaryGame);
            selectedArt = new CachedGameArt(
                secondaryArt.CoverPath ?? mainArt.CoverPath,
                secondaryArt.HeroPath ?? mainArt.HeroPath,
                secondaryArt.LogoPath ?? mainArt.LogoPath,
                null,
                null,
                null);
        }

        game.CoverArt = !string.IsNullOrWhiteSpace(selectedArt.CoverPath)
            ? new BitmapImage(new Uri(selectedArt.CoverPath))
            : PlaceholderArtGenerator.CreateCover(game.Title, game.CoverArtColor);
        game.CoverArtUrl = selectedArt.CoverPath;
        game.HasRealCoverArt = !string.IsNullOrWhiteSpace(selectedArt.CoverPath);

        game.HeroArt = !string.IsNullOrWhiteSpace(selectedArt.HeroPath)
            ? new BitmapImage(new Uri(selectedArt.HeroPath))
            : PlaceholderArtGenerator.CreateHero(game.Title, game.CoverArtColor, game.AccentColor);
        game.HeroArtUrl = selectedArt.HeroPath;

        game.LogoArt = !string.IsNullOrWhiteSpace(selectedArt.LogoPath)
            ? new BitmapImage(new Uri(selectedArt.LogoPath))
            : null;
        game.LogoArtUrl = selectedArt.LogoPath;
    }

    private static string HashSequence(IEnumerable<string> sequence)
    {
        var value = string.Join("|", sequence.Select(item => item.Trim().ToUpperInvariant()));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static ProfileStore Load()
    {
        try
        {
            return File.Exists(ProfilePath)
                ? JsonSerializer.Deserialize<ProfileStore>(File.ReadAllText(ProfilePath)) ?? new()
                : new();
        }
        catch
        {
            return new();
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath)!);
        File.WriteAllText(ProfilePath, JsonSerializer.Serialize(_store, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }
}

public sealed class ProfileStore
{
    public string SecondaryPasswordHash { get; set; } = string.Empty;
    public HashSet<string> PrivateGameIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
