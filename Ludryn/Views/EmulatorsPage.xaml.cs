using Ludryn.Models;
using Ludryn.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using System.Runtime.CompilerServices;
using Windows.Foundation;
using Windows.UI;

namespace Ludryn.Views;

public sealed partial class EmulatorsPage : Page, IGamepadFocusablePage, IGamepadHintProvider
{
    private static readonly string[] AdditionalPlatforms =
    [
        "GameCube",
        "PlayStation 1",
        "NES",
        "Game Boy",
        "Game Boy Color",
        "Game Boy Advance",
        "Super Nintendo",
        "Nintendo DS",
        "Nintendo 3DS",
        "Mega Drive",
        "Master System",
        "Game Gear",
        "Sega Saturn",
        "Dreamcast",
        "PSP",
        "Arcade"
    ];

    private sealed class CardSelectionState
    {
        public bool? Selected { get; set; }
        public Storyboard? Storyboard { get; set; }
    }

    private static readonly ConditionalWeakTable<FrameworkElement, CardSelectionState> CardSelectionStates = new();
    private static int s_lastFocusedSystemIndex;
    private static int s_lastFocusedGameIndex;

    private MockDataService? _dataService;
    private List<Game> _games = [];
    private Button[] _systemButtons = [];
    private readonly Dictionary<string, TextBlock> _systemCountTexts = new(StringComparer.OrdinalIgnoreCase);
    private FrameworkElement? _selectedCardVisual;
    private int _focusedSystemIndex;
    private int _focusedGameIndex;
    private bool _restoringSelection;
    private int _dataVersion = -1;
    private int _focusRequestVersion;
    private Button[] _systemSortButtons = [];
    private int _focusedSystemSortIndex;
    private bool _systemSortOpen;
    private string _systemSort = "Recent";

    public EmulatorsPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        RegisterSystemCount("PlayStation 2", Pcsx2CountText);
        RegisterSystemCount("Nintendo Switch", YuzuCountText);
        RegisterSystemCount("Wii", WiiCountText);
        RegisterSystemCount("Wii U", WiiUCountText);
        RegisterSystemCount("Nintendo 64", RetroArchCountText);
        RegisterSystemCount("PlayStation 3", Rpcs3CountText);
        foreach (var platform in AdditionalPlatforms)
        {
            SystemsPanel.Children.Add(CreateSystemButton(platform));
        }
        _systemButtons = SystemsPanel.Children.OfType<Button>().ToArray();
        _systemSortButtons = [SortSystemsRecentButton, SortSystemsMostPlayedButton, SortSystemsAddedButton];
        _systemSort = AppSettingsService.EmulatorPlatformSort;
        _focusedSystemIndex = s_lastFocusedSystemIndex;
        _focusedGameIndex = s_lastFocusedGameIndex;
        Pcsx2Icon.Source = PlatformIconService.GetIcon("PCSX2");
        YuzuIcon.Source = PlatformIconService.GetIcon("Yuzu");
        WiiIcon.Source = PlatformIconService.GetIcon("Wii");
        WiiUIcon.Source = PlatformIconService.GetIcon("Wii U");
        RetroArchIcon.Source = PlatformIconService.GetIcon("RetroArch");
        Rpcs3Icon.Source = PlatformIconService.GetIcon("RPCS3");
    }

    private void RegisterSystemCount(string platform, TextBlock textBlock) =>
        _systemCountTexts[platform] = textBlock;

    private Button CreateSystemButton(string platform)
    {
        var countText = new TextBlock
        {
            Text = "0 jogos",
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            FontSize = 17
        };
        RegisterSystemCount(platform, countText);

        var textPanel = new StackPanel();
        textPanel.Children.Add(new TextBlock { Text = platform, Foreground = new SolidColorBrush(Colors.White), FontSize = 23 });
        textPanel.Children.Add(countText);

        var icon = new Image
        {
            Source = PlatformIconService.GetIcon(platform),
            Width = 52,
            Height = 52,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var grid = new Grid
        {
            Width = 270,
            HorizontalAlignment = HorizontalAlignment.Right,
            ColumnSpacing = 18
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(icon);
        Grid.SetColumn(textPanel, 1);
        grid.Children.Add(textPanel);

        var button = new Button
        {
            Tag = platform,
            Content = grid,
            Style = (Style)Application.Current.Resources["EmulatorSideButtonStyle"]
        };
        button.Click += EmulatorButton_Click;
        return button;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (_dataService is not null &&
            ReferenceEquals(_dataService, e.Parameter) &&
            _dataVersion == _dataService.LibraryVersion &&
            _games.Count > 0)
        {
            _restoringSelection = true;
            return;
        }

        _dataService = (MockDataService)e.Parameter;
        _dataVersion = _dataService.LibraryVersion;
        _restoringSelection = s_lastFocusedGameIndex > 0;
        UpdateSystemCounts();
        ApplySystemOrder();
        SelectSystem(_focusedSystemIndex, focusGames: false, preferredGameIndex: s_lastFocusedGameIndex);
    }

    private void EmulatorButton_Click(object sender, RoutedEventArgs e)
    {
        var index = Array.IndexOf(_systemButtons, sender);
        if (index >= 0)
        {
            SelectSystem(index, focusGames: true);
        }
    }

    private void SelectSystem(int index, bool focusGames, int preferredGameIndex = 0)
    {
        if (_dataService is null)
        {
            return;
        }

        _focusRequestVersion++;
        if (_selectedCardVisual is not null)
        {
            ApplyEmulatorCardSelection(_selectedCardVisual, selected: false);
            _selectedCardVisual = null;
        }

        _dataVersion = _dataService.LibraryVersion;
        UpdateSystemCounts();

        if (!TryGetAvailableSystemIndex(index, out var availableIndex))
        {
            _focusedSystemIndex = 0;
            _focusedGameIndex = 0;
            _games = [];
            EmulatorGamesList.ItemsSource = _games;
            SelectedEmulatorTitle.Text = "Nenhuma plataforma encontrada";
            InfoSystemText.Text = "Sem ROMs\n-";
            InfoSystemIcon.Source = null;
            InfoTotalGamesText.Text = "Total de jogos\n0";
            InfoLastPlayedText.Text = "Título\n-";
            InfoEmulatorText.Text = "Emulador\n-";
            EmptyStatePanel.Visibility = Visibility.Visible;
            BackgroundArt.Source = null;
            UpdateSystemButtonVisuals();
            return;
        }

        _focusedSystemIndex = availableIndex;
        s_lastFocusedSystemIndex = _focusedSystemIndex;
        _focusedGameIndex = Math.Max(0, preferredGameIndex);
        var platform = _systemButtons[_focusedSystemIndex].Tag as string ?? "PlayStation 2";
        _games = _dataService.GetGamesByPlatform(platform).OrderByDescending(g => g.LastPlayed).ToList();
        var lastPlayed = _games.FirstOrDefault();

        EmulatorGamesList.ItemsSource = _games;
        SelectedEmulatorTitle.Text = $"{GetSystemTitle(platform)}  •  {FormatGameCount(_games.Count)}";
        InfoSystemText.Text = GetSystemInfo(platform);
        InfoSystemIcon.Source = PlatformIconService.GetIcon(platform);
        InfoTotalGamesText.Text = $"Total de jogos\n{_games.Count}";
        EmptyStatePanel.Visibility = _games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        InfoLastPlayedText.Text = lastPlayed is not null ? $"Título\n{lastPlayed.Title}" : "Título\n-";
        InfoEmulatorText.Text = $"Emulador\n{GetSelectedGameEmulator(lastPlayed, platform)}";
        BackgroundArt.Source = _games.Count > 0 ? _games[0].HeroArt : null;

        UpdateSystemButtonVisuals();
        BringSelectedSystemIntoView();
        if (focusGames)
        {
            FocusGameAtIndex(_focusedGameIndex);
        }

        _ = LoadArtworkAsync();
    }

    private void UpdateSystemButtonVisuals()
    {
        for (var i = 0; i < _systemButtons.Length; i++)
        {
            var selected = i == _focusedSystemIndex;
            if (_systemButtons[i].Visibility != Visibility.Visible)
            {
                continue;
            }

            _systemButtons[i].BorderBrush = new SolidColorBrush(selected ? Color.FromArgb(255, 204, 82, 235) : Color.FromArgb(53, 255, 255, 255));
            _systemButtons[i].Background = new SolidColorBrush(selected ? Color.FromArgb(96, 95, 28, 111) : Color.FromArgb(154, 16, 20, 23));
        }
    }

    private void BringSelectedSystemIntoView()
    {
        if (_focusedSystemIndex < 0 || _focusedSystemIndex >= _systemButtons.Length)
        {
            return;
        }

        try
        {
            _systemButtons[_focusedSystemIndex].StartBringIntoView(new BringIntoViewOptions
            {
                AnimationDesired = true,
                VerticalAlignmentRatio = 0.5
            });
        }
        catch
        {
        }
    }

    private static string GetSystemTitle(string platform) => platform switch
    {
        "PlayStation 2" => "PlayStation 2",
        "Nintendo Switch" => "Nintendo Switch",
        "Wii" => "Wii",
        "Wii U" => "Wii U",
        "PlayStation 1" => "PlayStation 1",
        "Nintendo 64" => "Nintendo 64",
        "PlayStation 3" => "PlayStation 3",
        _ => platform
    };

    private static string GetSystemInfo(string platform) => platform switch
    {
        "PlayStation 2" => "PlayStation 2\nPS2",
        "Nintendo Switch" => "Nintendo Switch\nNS",
        "Wii" => "Nintendo Wii\nWii",
        "Wii U" => "Nintendo Wii U\nWii U",
        "PlayStation 1" => "PlayStation 1\nPS1",
        "Nintendo 64" => "Nintendo 64\nN64",
        "PlayStation 3" => "PlayStation 3\nPS3",
        _ => platform
    };

    private void Card_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement slot && slot.DataContext is Game game)
        {
            var card = FocusUtilities.FindDescendantByName(slot, "EmulatorGameCard") ?? slot;
            SetSelectedCardVisual(card);
            BackgroundArt.Source = game.HeroArt;
            UpdateSelectedGameInfo(game);
        }
    }

    private void Card_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement slot)
        {
            var card = FocusUtilities.FindDescendantByName(slot, "EmulatorGameCard") ?? slot;
            if (_selectedCardVisual != card)
            {
                ApplyEmulatorCardSelection(card, selected: false);
            }
        }
    }

    private void List_GotFocus(object sender, RoutedEventArgs e)
    {
        if (_games.Count > 0)
        {
            BackgroundArt.Source = _games[_focusedGameIndex].HeroArt;
        }
    }

    private void Game_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Game game && App.MainWindow() is MainWindow window)
        {
            window.OpenGame(game);
        }
    }

    public void FocusInitialElement()
    {
        FocusGameAtIndex(_focusedGameIndex);
    }

    public bool HandleGamepadMove(FocusNavigationDirection direction)
    {
        if (_systemSortOpen)
        {
            if (direction == FocusNavigationDirection.Up)
            {
                FocusSystemSortOption(Math.Max(0, _focusedSystemSortIndex - 1));
            }
            else if (direction == FocusNavigationDirection.Down)
            {
                FocusSystemSortOption(Math.Min(_systemSortButtons.Length - 1, _focusedSystemSortIndex + 1));
            }
            return true;
        }

        switch (direction)
        {
            case FocusNavigationDirection.Up:
                SelectSystem(GetNextAvailableSystemIndex(_focusedSystemIndex, -1), focusGames: true);
                return true;
            case FocusNavigationDirection.Down:
                SelectSystem(GetNextAvailableSystemIndex(_focusedSystemIndex, 1), focusGames: true);
                return true;
            case FocusNavigationDirection.Left:
                FocusGameAtIndex(Math.Max(0, _focusedGameIndex - 1));
                return true;
            case FocusNavigationDirection.Right:
                FocusGameAtIndex(Math.Min(_games.Count - 1, _focusedGameIndex + 1));
                return true;
            default:
                return false;
        }
    }

    public bool HandleGamepadBack()
    {
        if (_systemSortOpen)
        {
            CloseSystemSort();
            return true;
        }
        return false;
    }

    public bool HandleGamepadX()
    {
        if (_systemSortOpen)
        {
            CloseSystemSort();
        }
        else
        {
            OpenSystemSort();
        }
        return true;
    }
    public bool HandleGamepadY()
    {
        if (App.MainWindow() is MainWindow window)
        {
            window.OpenEmulatorSettings();
            return true;
        }

        return false;
    }

    public GamepadHints GetGamepadHints() => _systemSortOpen
        ? new("Confirmar", "Fechar ordenacao", null, "Voltar")
        : new("Abrir jogo", "Ordenar consoles", "Configurar", "Voltar");
    public bool HandleGamepadOptions()
    {
        _ = ShowSelectedGameOptionsAsync();
        return true;
    }

    public bool HandleGamepadAccept(FrameworkElement focusedElement)
    {
        if (_systemSortOpen)
        {
            ApplySystemSortSelection(_focusedSystemSortIndex);
            return true;
        }

        if (_games.Count > 0 && App.MainWindow() is MainWindow window)
        {
            window.OpenGame(_games[_focusedGameIndex]);
            return true;
        }

        return false;
    }

    private void FocusGameAtIndex(int index)
    {
        if (_games.Count == 0)
        {
            return;
        }

        var requestedIndex = Math.Clamp(index, 0, _games.Count - 1);
        var requestedGame = _games[requestedIndex];
        var requestVersion = ++_focusRequestVersion;
        _focusedGameIndex = requestedIndex;
        s_lastFocusedGameIndex = requestedIndex;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (requestVersion != _focusRequestVersion ||
                _games.Count == 0 ||
                requestedIndex >= _games.Count ||
                !ReferenceEquals(_games[requestedIndex], requestedGame))
            {
                return;
            }

            var item = _games[_focusedGameIndex];
            var existingContainer = EmulatorGamesList.ContainerFromIndex(_focusedGameIndex) as FrameworkElement;
            var needsScroll = _restoringSelection || !IsFullyVisible(existingContainer, EmulatorGamesList);
            if (needsScroll && _restoringSelection)
            {
                EmulatorGamesList.ScrollIntoView(item, ScrollIntoViewAlignment.Leading);
                _restoringSelection = false;
            }
            else if (needsScroll)
            {
                EmulatorGamesList.ScrollIntoView(item);
            }
            if (needsScroll)
            {
                EmulatorGamesList.UpdateLayout();
            }

            if (EmulatorGamesList.ContainerFromIndex(_focusedGameIndex) is Control container)
            {
                var card = FocusUtilities.FindDescendantByName(container, "EmulatorGameCard");
                var slot = FocusUtilities.FindDescendantByName(container, "EmulatorGameSlot");
                FrameworkElement visualTarget = card is not null ? card : container;
                FrameworkElement focusTarget = slot is not null ? slot : visualTarget;
                if (visualTarget.RenderTransform is not CompositeTransform)
                {
                    visualTarget.RenderTransform = new CompositeTransform();
                    visualTarget.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
                }

                focusTarget.Focus(FocusState.Programmatic);
            }

            BackgroundArt.Source = item.HeroArt;
            UpdateSelectedGameInfo(item);
        });
    }

    private static bool IsFullyVisible(FrameworkElement? element, FrameworkElement viewport)
    {
        if (element is null || element.ActualWidth <= 0 || viewport.ActualWidth <= 0)
        {
            return false;
        }

        try
        {
            var position = element.TransformToVisual(viewport).TransformPoint(new Point(0, 0));
            return position.X >= 0 &&
                   position.Y >= 0 &&
                   position.X + element.ActualWidth <= viewport.ActualWidth &&
                   position.Y + element.ActualHeight <= viewport.ActualHeight;
        }
        catch
        {
            return false;
        }
    }

    private void UpdateSystemCounts()
    {
        if (_dataService is null)
        {
            return;
        }

        foreach (var button in _systemButtons)
        {
            var platform = button.Tag as string ?? string.Empty;
            var count = CountGames(platform);
            if (_systemCountTexts.TryGetValue(platform, out var countText))
            {
                countText.Text = FormatGameCount(count);
            }
            button.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        UpdateSystemSortText();
    }

    private void ApplySystemOrder()
    {
        if (_dataService is null)
        {
            return;
        }

        var selectedPlatform = _systemButtons.Length > 0 && _focusedSystemIndex >= 0 && _focusedSystemIndex < _systemButtons.Length
            ? _systemButtons[_focusedSystemIndex].Tag as string
            : null;

        var ordered = _systemButtons
            .Select(button =>
            {
                var platform = button.Tag as string ?? string.Empty;
                var games = _dataService.GetGamesByPlatform(platform);
                return new
                {
                    Button = button,
                    Platform = platform,
                    LastPlayed = games.Count > 0 ? games.Max(game => game.LastPlayed) : DateTime.MinValue,
                    AddedAt = games.Count > 0 ? games.Max(game => game.AddedAt) : DateTime.MinValue,
                    LaunchCount = games.Sum(game => game.LaunchCount)
                };
            });

        ordered = _systemSort switch
        {
            "MostPlayed" => ordered.OrderByDescending(item => item.LaunchCount).ThenByDescending(item => item.LastPlayed),
            "RecentlyAdded" => ordered.OrderByDescending(item => item.AddedAt).ThenBy(item => item.Platform),
            _ => ordered.OrderByDescending(item => item.LastPlayed).ThenBy(item => item.Platform)
        };

        var buttons = ordered.Select(item => item.Button).ToArray();
        foreach (var button in buttons)
        {
            SystemsPanel.Children.Remove(button);
            SystemsPanel.Children.Add(button);
        }
        _systemButtons = buttons;

        if (!string.IsNullOrWhiteSpace(selectedPlatform))
        {
            var selectedIndex = Array.FindIndex(_systemButtons, button =>
                string.Equals(button.Tag as string, selectedPlatform, StringComparison.OrdinalIgnoreCase));
            if (selectedIndex >= 0)
            {
                _focusedSystemIndex = selectedIndex;
            }
        }
    }

    private void OpenSystemSort()
    {
        _systemSortOpen = true;
        SystemSortOverlay.Visibility = Visibility.Visible;
        _focusedSystemSortIndex = Math.Max(0, Array.FindIndex(_systemSortButtons,
            button => string.Equals(button.Tag as string, _systemSort, StringComparison.OrdinalIgnoreCase)));
        FocusSystemSortOption(_focusedSystemSortIndex);
        App.MainWindow()?.RefreshGamepadHints();
    }

    private void CloseSystemSort()
    {
        _systemSortOpen = false;
        SystemSortOverlay.Visibility = Visibility.Collapsed;
        FocusGameAtIndex(_focusedGameIndex);
        App.MainWindow()?.RefreshGamepadHints();
    }

    private void FocusSystemSortOption(int index)
    {
        _focusedSystemSortIndex = Math.Clamp(index, 0, _systemSortButtons.Length - 1);
        for (var i = 0; i < _systemSortButtons.Length; i++)
        {
            var selected = i == _focusedSystemSortIndex;
            _systemSortButtons[i].BorderBrush = new SolidColorBrush(selected
                ? Color.FromArgb(255, 229, 83, 255)
                : Color.FromArgb(102, 255, 255, 255));
            _systemSortButtons[i].BorderThickness = selected ? new Thickness(2) : new Thickness(1);
        }
        _systemSortButtons[_focusedSystemSortIndex].Focus(FocusState.Programmatic);
    }

    private void ApplySystemSortSelection(int index)
    {
        _focusedSystemSortIndex = Math.Clamp(index, 0, _systemSortButtons.Length - 1);
        _systemSort = _systemSortButtons[_focusedSystemSortIndex].Tag as string ?? "Recent";
        AppSettingsService.EmulatorPlatformSort = _systemSort;
        ApplySystemOrder();
        CloseSystemSort();
        SelectSystem(_focusedSystemIndex, focusGames: true, preferredGameIndex: _focusedGameIndex);
    }

    private void SystemSortButton_Click(object sender, RoutedEventArgs e)
    {
        var index = Array.IndexOf(_systemSortButtons, sender);
        if (index >= 0)
        {
            ApplySystemSortSelection(index);
        }
    }

    private void UpdateSystemSortText()
    {
        SystemSortText.Text = _systemSort switch
        {
            "MostPlayed" => "Consoles: Mais jogados",
            "RecentlyAdded" => "Consoles: Recentemente adicionados",
            _ => "Consoles: Mais recentes"
        };
    }

    private int CountGames(string platform) =>
        _dataService?.GetGamesByPlatform(platform).Count ?? 0;

    private bool TryGetAvailableSystemIndex(int requestedIndex, out int availableIndex)
    {
        availableIndex = 0;
        var visibleIndexes = _systemButtons
            .Select((button, index) => new { button, index })
            .Where(item => item.button.Visibility == Visibility.Visible)
            .Select(item => item.index)
            .ToList();

        if (visibleIndexes.Count == 0)
        {
            return false;
        }

        availableIndex = visibleIndexes
            .OrderBy(index => Math.Abs(index - requestedIndex))
            .ThenBy(index => index)
            .First();
        return true;
    }

    private int GetNextAvailableSystemIndex(int currentIndex, int direction)
    {
        var visibleIndexes = _systemButtons
            .Select((button, index) => new { button, index })
            .Where(item => item.button.Visibility == Visibility.Visible)
            .Select(item => item.index)
            .ToList();

        if (visibleIndexes.Count == 0)
        {
            return currentIndex;
        }

        var currentVisiblePosition = visibleIndexes.IndexOf(currentIndex);
        if (currentVisiblePosition < 0)
        {
            return visibleIndexes[0];
        }

        var nextPosition = Math.Clamp(currentVisiblePosition + direction, 0, visibleIndexes.Count - 1);
        return visibleIndexes[nextPosition];
    }

    private void UpdateSelectedGameInfo(Game game)
    {
        var platform = _systemButtons.Length > 0 && _focusedSystemIndex >= 0 && _focusedSystemIndex < _systemButtons.Length
            ? _systemButtons[_focusedSystemIndex].Tag as string ?? game.Platform
            : game.Platform;

        InfoLastPlayedText.Text = $"Título\n{game.Title}";
        InfoEmulatorText.Text = $"Emulador\n{GetSelectedGameEmulator(game, platform)}";
    }

    private string GetSelectedGameEmulator(Game? game, string platform)
    {
        if (game is not null && game.Installations.Count > 0)
        {
            if (!string.IsNullOrWhiteSpace(game.SelectedLauncher) &&
                game.Installations.Any(i => string.Equals(i.Launcher, game.SelectedLauncher, StringComparison.OrdinalIgnoreCase)))
            {
                return game.SelectedLauncher;
            }

            return game.Installations[0].Launcher;
        }

        return GetConfiguredEmulatorNames(platform);
    }

    private string GetConfiguredEmulatorNames(string platform)
    {
        if (_dataService is null)
        {
            return "Nenhum configurado";
        }

        var names = _dataService.GetConfiguredEmulators()
            .Where(e => string.Equals(e.Platform, platform, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return names.Count == 0 ? "Nenhum configurado" : string.Join(", ", names);
    }

    private static string FormatGameCount(int count) =>
        count == 1 ? "1 jogo" : $"{count} jogos";

    private void SetSelectedCardVisual(FrameworkElement card)
    {
        if (_selectedCardVisual is not null && _selectedCardVisual != card)
        {
            ApplyEmulatorCardSelection(_selectedCardVisual, selected: false);
        }

        _selectedCardVisual = card;
        ApplyEmulatorCardSelection(card, selected: true);
    }

    private async Task LoadArtworkAsync()
    {
        if (_dataService is null || _games.Count == 0)
        {
            return;
        }

        await _dataService.LoadArtworkAsync(_games, DispatcherQueue, updatedGame =>
        {
            if (_focusedGameIndex >= 0 && _focusedGameIndex < _games.Count && _games[_focusedGameIndex] == updatedGame)
            {
                BackgroundArt.Source = updatedGame.HeroArt;
            }
        });
    }

    private Task ShowSelectedGameOptionsAsync()
    {
        if (_dataService is null || _games.Count == 0 || App.MainWindow() is not MainWindow window)
        {
            return Task.CompletedTask;
        }

        var game = _games[Math.Clamp(_focusedGameIndex, 0, _games.Count - 1)];
        window.OpenGameOptionsPanel(
            game,
            refreshAfterChange: () =>
            {
                BackgroundArt.Source = game.HeroArt;
                FocusGameAtIndex(_focusedGameIndex);
            },
            removed: () =>
            {
                SelectSystem(_focusedSystemIndex, focusGames: true, preferredGameIndex: Math.Clamp(_focusedGameIndex, 0, Math.Max(0, _games.Count - 1)));
            });
        return Task.CompletedTask;
    }

    private static void ApplyEmulatorCardSelection(FrameworkElement card, bool selected)
    {
        var state = CardSelectionStates.GetOrCreateValue(card);
        if (state.Selected == selected)
        {
            return;
        }

        state.Selected = selected;
        state.Storyboard?.Stop();
        Canvas.SetZIndex(card, selected ? 20 : 0);
        card.RenderTransformOrigin = new Point(0.5, 0.5);

        if (card.RenderTransform is not CompositeTransform transform)
        {
            transform = new CompositeTransform();
            card.RenderTransform = transform;
        }

        var storyboard = new Storyboard();
        AddAnimation(storyboard, transform, nameof(CompositeTransform.ScaleX), selected ? 1.097 : 1);
        AddAnimation(storyboard, transform, nameof(CompositeTransform.ScaleY), selected ? 1.097 : 1);
        AddAnimation(storyboard, transform, nameof(CompositeTransform.TranslateY), selected ? -18 : 0);
        state.Storyboard = storyboard;
        storyboard.Begin();

        var border = FocusUtilities.FindDescendant<Border>(card);
        if (border is not null)
        {
            border.BorderThickness = selected ? new Thickness(2.5) : new Thickness(1);
            border.BorderBrush = new SolidColorBrush(selected ? Color.FromArgb(255, 229, 83, 255) : Color.FromArgb(42, 255, 255, 255));
        }
    }

    private static void AddAnimation(Storyboard storyboard, DependencyObject target, string property, double to)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = TimeSpan.FromMilliseconds(190),
            EnableDependentAnimation = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
    }
}
