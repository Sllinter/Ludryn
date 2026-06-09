using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Windows.UI;

namespace Ludryn.Models;

public class Game : INotifyPropertyChanged
{
    private ImageSource? _coverArt;
    private ImageSource? _heroArt;
    private ImageSource? _logoArt;
    private bool _hasRealCoverArt;
    private bool _isFavorite;
    private string _platform = string.Empty;
    private string _selectedLauncher = string.Empty;
    private BitmapImage? _platformIcon;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Platform
    {
        get => _platform;
        set
        {
            if (SetProperty(ref _platform, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LibraryPlatformIcon)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LibraryPlatformName)));
            }
        }
    }
    public int? SteamAppId { get; set; }
    public Color CoverArtColor { get; set; }
    public Color AccentColor { get; set; }
    public string PlayTime { get; set; } = string.Empty;
    public DateTime LastPlayed { get; set; }
    public DateTime AddedAt { get; set; }
    public int LaunchCount { get; set; }

    public ImageSource? CoverArt
    {
        get => _coverArt;
        set => SetProperty(ref _coverArt, value);
    }

    public ImageSource? HeroArt
    {
        get => _heroArt;
        set => SetProperty(ref _heroArt, value);
    }

    public ImageSource? LogoArt
    {
        get => _logoArt;
        set
        {
            if (SetProperty(ref _logoArt, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LogoArtVisibility)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TitleVisibility)));
            }
        }
    }

    public bool HasRealCoverArt
    {
        get => _hasRealCoverArt;
        set
        {
            if (SetProperty(ref _hasRealCoverArt, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CardTitleVisibility)));
            }
        }
    }

    public string? CoverArtUrl { get; set; }
    public string? HeroArtUrl { get; set; }
    public string? LogoArtUrl { get; set; }
    public BitmapImage? PlatformIcon
    {
        get => _platformIcon;
        set
        {
            if (SetProperty(ref _platformIcon, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LibraryPlatformIcon)));
            }
        }
    }
    public BitmapImage? LibraryPlatformIcon =>
        PlatformIcon ?? Services.PlatformIconService.GetIcon(IsPlatformEntry ? Title : Platform);
    public string LibraryPlatformName => IsPlatformEntry ? Title : Platform;
    public bool IsStoreEntry { get; set; }
    public string StoreExecutablePath { get; set; } = string.Empty;
    public string StoreLaunchArguments { get; set; } = string.Empty;
    public bool StoreIsFound => IsStoreEntry && !string.IsNullOrWhiteSpace(StoreExecutablePath) && File.Exists(StoreExecutablePath);
    public string StoreStatusText => IsStoreEntry ? (StoreIsFound ? "Abrir loja" : "Não encontrado") : string.Empty;
    public List<GameInstallation> Installations { get; } = [];
    public bool IsPlatformEntry { get; set; }
    public bool IsFavorite
    {
        get => _isFavorite;
        set => SetProperty(ref _isFavorite, value);
    }

    public string SelectedLauncher
    {
        get => string.IsNullOrWhiteSpace(_selectedLauncher) ? Platform : _selectedLauncher;
        set
        {
            if (SetProperty(ref _selectedLauncher, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PlatformIcon)));
            }
        }
    }

    public bool HasMultipleLaunchers => Installations.Select(i => i.Launcher).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
    public Visibility PlatformEntryVisibility => IsPlatformEntry ? Visibility.Visible : Visibility.Collapsed;
    public Visibility StoreEntryVisibility => IsStoreEntry ? Visibility.Visible : Visibility.Collapsed;
    public Visibility GameEntryVisibility => IsStoreEntry ? Visibility.Collapsed : Visibility.Visible;
    public double LibrarySlotWidth => IsStoreEntry ? 238 : 228;
    public double LibrarySlotHeight => IsStoreEntry ? 286 : 370;
    public double LibraryCardWidth => IsStoreEntry ? 190 : 200;
    public double LibraryCardHeight => IsStoreEntry ? 260 : 300;
    public double StoreIconSize => Title switch
    {
        "Steam" => 150,
        "Epic Games" => 142,
        "GOG" => 132,
        "Ubisoft Connect" => 116,
        "EA Play" => 130,
        _ => 132
    };
    public Visibility LogoArtVisibility => LogoArt is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility TitleVisibility => LogoArt is null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CardTitleVisibility => HasRealCoverArt ? Visibility.Collapsed : Visibility.Visible;

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
