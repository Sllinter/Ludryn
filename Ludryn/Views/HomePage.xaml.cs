using Ludryn.Models;
using Ludryn.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace Ludryn.Views;

public sealed partial class HomePage : Page, IGamepadFocusablePage, IGamepadHintProvider
{
    private const double RecentCardStride = 372;
    private static int s_lastFocusedIndex;

    private MockDataService? _dataService;
    private FrameworkElement? _selectedCardVisual;
    private int _focusedIndex;
    private bool _restoringSelection;
    private int _dataVersion = -1;
    private int _focusRequestVersion;

    public IReadOnlyList<Game> RecentGames { get; private set; } = [];

    public HomePage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        Loaded += (_, _) => QueueInitialFocus();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (_dataService is not null &&
            ReferenceEquals(_dataService, e.Parameter) &&
            _dataVersion == _dataService.LibraryVersion &&
            RecentGames.Count > 0)
        {
            _dataService.ArtworkProgressChanged -= DataService_ArtworkProgressChanged;
            _dataService.ArtworkProgressChanged += DataService_ArtworkProgressChanged;
            _restoringSelection = true;
            SetHero(RecentGames[Math.Clamp(_focusedIndex, 0, RecentGames.Count - 1)]);
            UpdateArtworkProgressText();
            QueueInitialFocus();
            return;
        }

        if (_dataService is not null)
        {
            _dataService.ArtworkProgressChanged -= DataService_ArtworkProgressChanged;
        }

        _dataService = (MockDataService)e.Parameter;
        _dataService.ArtworkProgressChanged += DataService_ArtworkProgressChanged;
        _dataVersion = _dataService.LibraryVersion;
        var recentGames = _dataService.GetRecentGames(20);
        RecentGames = recentGames.ToList();
        RecentList.ItemsSource = RecentGames;
        EmptyStatePanel.Visibility = RecentGames.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentList.Visibility = RecentGames.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        _focusedIndex = Math.Clamp(s_lastFocusedIndex, 0, Math.Max(0, RecentGames.Count - 1));
        _restoringSelection = _focusedIndex > 0;
        if (RecentGames.Count > 0)
        {
            SetHero(RecentGames[_focusedIndex]);
        }

        UpdateArtworkProgressText();
        QueueInitialFocus();
        _ = LoadArtworkAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        if (_dataService is not null)
        {
            _dataService.ArtworkProgressChanged -= DataService_ArtworkProgressChanged;
        }

        base.OnNavigatedFrom(e);
    }

    private void SetHero(Game game)
    {
        HeroBackground.Source = game.HeroArt;
        if (game.LogoArt is not null)
        {
            HeroLogo.Source = game.LogoArt;
            HeroLogo.Visibility = Visibility.Visible;
            HeroTitle.Visibility = Visibility.Collapsed;
        }
        else
        {
            HeroLogo.Source = null;
            HeroLogo.Visibility = Visibility.Collapsed;
            HeroTitle.Visibility = Visibility.Visible;
            HeroTitle.Text = game.Title;
        }

        HeroPlatformIcon.Source = PlatformIconService.GetIcon(game.Platform);
        HeroPlatformName.Text = GetPlatformName(game.Platform);
    }

    private static string GetPlatformName(string platform) => platform switch
    {
        "Yuzu" => "Nintendo Switch",
        "PCSX2" => "PlayStation 2",
        "RPCS3" => "PlayStation 3",
        "Dolphin" => "Wii",
        "Cemu" => "Wii U",
        _ => platform
    };

    private void Game_GotFocus(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is Game game)
        {
            SetSelectedCardVisual((FrameworkElement)sender);
            SetHero(game);
        }
    }

    private void Game_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement card && _selectedCardVisual != card)
        {
            GameCardSelection.Apply(card, selected: false);
        }
    }

    private void Game_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is Game game && App.MainWindow() is MainWindow window)
        {
            window.OpenGame(game);
        }
    }

    public void FocusInitialElement()
    {
        QueueInitialFocus();
    }

    public bool HandleGamepadMove(FocusNavigationDirection direction)
    {
        switch (direction)
        {
            case FocusNavigationDirection.Left:
                FocusHomeItemAtIndex(Math.Max(0, _focusedIndex - 1));
                return true;
            case FocusNavigationDirection.Right:
                FocusHomeItemAtIndex(Math.Min(RecentGames.Count - 1, _focusedIndex + 1));
                return true;
            case FocusNavigationDirection.Up:
            case FocusNavigationDirection.Down:
                FocusHomeItemAtIndex(_focusedIndex);
                return true;
            default:
                return false;
        }
    }

    public bool HandleGamepadBack() => false;
    public bool HandleGamepadX() => false;
    public bool HandleGamepadY() => false;
    public GamepadHints GetGamepadHints() => new("Abrir jogo");
    public bool HandleGamepadOptions()
    {
        _ = ShowSelectedGameOptionsAsync();
        return true;
    }

    public bool HandleGamepadAccept(FrameworkElement focusedElement)
    {
        var selectedGame = GetSelectedGame();
        if (selectedGame is not null && App.MainWindow() is MainWindow window)
        {
            window.OpenGame(selectedGame);
            return true;
        }

        return false;
    }

    private Game? GetSelectedGame()
    {
        return _focusedIndex >= 0 && _focusedIndex < RecentGames.Count ? RecentGames[_focusedIndex] : null;
    }

    private Task ShowSelectedGameOptionsAsync()
    {
        if (_dataService is null || GetSelectedGame() is not { } game || App.MainWindow() is not MainWindow window)
        {
            return Task.CompletedTask;
        }

        window.OpenGameOptionsPanel(
            game,
            refreshAfterChange: () =>
            {
                SetHero(game);
                FocusHomeItemAtIndex(_focusedIndex);
            },
            removed: () =>
            {
                RecentGames = _dataService.GetRecentGames(20).ToList();
                RecentList.ItemsSource = RecentGames;
                _focusedIndex = Math.Clamp(_focusedIndex, 0, Math.Max(0, RecentGames.Count - 1));
                FocusHomeItemAtIndex(_focusedIndex);
            });
        return Task.CompletedTask;
    }

    private void FocusHomeItemAtIndex(int index)
    {
        if (RecentGames.Count == 0)
        {
            return;
        }

        _focusedIndex = Math.Clamp(index, 0, RecentGames.Count - 1);
        s_lastFocusedIndex = _focusedIndex;
        FocusRecentAtIndex(_focusedIndex);
    }

    private void FocusRecentAtIndex(int index)
    {
        if (RecentGames.Count == 0)
        {
            return;
        }

        var safeIndex = Math.Clamp(index, 0, RecentGames.Count - 1);
        RecentList.UpdateLayout();
        MoveRecentViewportToIndex(safeIndex);
        RecentList.UpdateLayout();
        if (RecentList.ContainerFromIndex(safeIndex) is Control selectedCard)
        {
            var cardButton = FocusUtilities.FindDescendant<Button>(selectedCard);
            FrameworkElement focusTarget = cardButton is not null ? cardButton : selectedCard;
            focusTarget.Focus(FocusState.Programmatic);
            SetSelectedCardVisual(focusTarget);
            SetHero(RecentGames[safeIndex]);
        }
    }

    private void MoveRecentViewportToIndex(int index)
    {
        var scrollViewer = FocusUtilities.FindDescendant<ScrollViewer>(RecentList);
        if (scrollViewer is null)
        {
            RecentList.ScrollIntoView(RecentGames[index]);
            return;
        }

        var viewportWidth = scrollViewer.ViewportWidth > 0 ? scrollViewer.ViewportWidth : RecentList.ActualWidth;
        var visibleSlots = Math.Max(1, (int)Math.Floor(viewportWidth / RecentCardStride));
        var firstVisibleIndex = Math.Max(0, index - Math.Max(0, visibleSlots / 2));
        var targetOffset = firstVisibleIndex * RecentCardStride;
        var maxOffset = Math.Max(0, RecentGames.Count * RecentCardStride - viewportWidth);
        scrollViewer.ChangeView(Math.Clamp(targetOffset, 0, maxOffset), null, null, _restoringSelection);
        _restoringSelection = false;
    }

    private async void QueueInitialFocus()
    {
        if (RecentGames.Count == 0)
        {
            return;
        }

        var requestVersion = ++_focusRequestVersion;
        await Task.Delay(48);
        if (requestVersion != _focusRequestVersion)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (requestVersion == _focusRequestVersion)
            {
                FocusHomeItemAtIndex(_focusedIndex);
            }
        });
    }

    private void SetSelectedCardVisual(FrameworkElement card)
    {
        if (_selectedCardVisual is not null && _selectedCardVisual != card)
        {
            GameCardSelection.Apply(_selectedCardVisual, selected: false);
        }

        _selectedCardVisual = card;
        GameCardSelection.Apply(card, selected: true);
    }

    private async Task LoadArtworkAsync()
    {
        if (_dataService is null || RecentGames.Count == 0)
        {
            return;
        }

        await _dataService.LoadArtworkAsync(_dataService.GetAllGames(), DispatcherQueue, updatedGame =>
        {
            if (GetSelectedGame() == updatedGame)
            {
                SetHero(updatedGame);
            }
        });
    }

    private void DataService_ArtworkProgressChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(UpdateArtworkProgressText);
    }

    private void UpdateArtworkProgressText()
    {
        if (_dataService is null)
        {
            ArtworkProgressText.Text = "0/0 carregados";
            return;
        }

        ArtworkProgressText.Text = _dataService.HasSteamGridDb
            ? $"{_dataService.ArtworkLoadedCount}/{_dataService.ArtworkTotalCount} carregados"
            : "SteamGridDB desconectado";
    }
}
