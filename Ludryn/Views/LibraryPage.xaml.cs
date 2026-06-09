using Ludryn.Models;
using Ludryn.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;

namespace Ludryn.Views;

public sealed partial class LibraryPage : Page, IGamepadFocusablePage, IGamepadHintProvider
{
    private const double CardOuterWidth = 228;
    private static int s_lastFocusedGameIndex;
    private static int s_lastFocusedFilterIndex;
    private static string s_lastActiveFilter = "Todas";
    private static string s_lastActiveSort = "Recentes";

    private MockDataService? _dataService;
    private List<Game> _games = [];
    private Button[] _filterButtons = [];
    private Button[] _sortButtons = [];
    private Button[] _addButtons = [];
    private string _activeFilter = "Todas";
    private string _activeSort = "Recentes";
    private int _focusedGameIndex;
    private int _focusedFilterIndex;
    private int _focusedSortIndex;
    private int _focusedAddIndex;
    private bool _sortOverlayOpen;
    private bool _addOverlayOpen;
    private bool _programBrowserOpen;
    private bool _searchInputActive;
    private bool _addingAsStore;
    private List<ComputerProgramEntry> _allPrograms = [];
    private List<ComputerProgramEntry> _visiblePrograms = [];
    private int _programFocusIndex;
    private string _programSearchQuery = string.Empty;
    private bool _restoringSelection;
    private int _dataVersion = -1;
    private int _focusRequestVersion;

    public bool IsSortOverlayOpen => _sortOverlayOpen;
    public bool IsProgramBrowserOpen => _programBrowserOpen;
    public bool IsProgramSearchActive => _searchInputActive;

    public GamepadHints GetGamepadHints()
    {
        if (_searchInputActive)
        {
            return new GamepadHints(Back: "Fechar teclado");
        }

        if (_programBrowserOpen)
        {
            return new GamepadHints("Adicionar", null, "Pesquisar", "Voltar");
        }

        if (_sortOverlayOpen)
        {
            return new GamepadHints("Confirmar", "Fechar ordenacao", null, "Voltar");
        }

        if (_addOverlayOpen)
        {
            return new GamepadHints("Confirmar", null, "Fechar", "Voltar");
        }

        return new GamepadHints(
            _activeFilter == "Lojas" ? "Abrir app" : "Abrir jogo",
            "Ordenar",
            "Adicionar",
            "Voltar");
    }

    public LibraryPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        _filterButtons = [AllFilterButton, FavoritesFilterButton, StoresFilterButton];
        _sortButtons = [SortRecentButton, SortMostPlayedButton, SortAlphabeticalButton, SortAddedButton, SortPlatformButton];
        _addButtons = [AddComputerGameButton, AddAppButton];
        _focusedGameIndex = s_lastFocusedGameIndex;
        _focusedFilterIndex = s_lastFocusedFilterIndex;
        _activeFilter = s_lastActiveFilter;
        _activeSort = s_lastActiveSort;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var navigation = (LibraryNavigation)e.Parameter;
        if (_dataService is not null &&
            ReferenceEquals(_dataService, navigation.DataService) &&
            navigation.Platform is null &&
            _dataVersion == _dataService.LibraryVersion &&
            _games.Count > 0)
        {
            _restoringSelection = true;
            return;
        }

        _dataService = navigation.DataService;
        _dataVersion = _dataService.LibraryVersion;
        _restoringSelection = s_lastFocusedGameIndex > 0;
        ApplyFilter(navigation.Platform ?? s_lastActiveFilter);
    }

    private void ApplyFilter(string? platform)
    {
        if (_dataService is null)
        {
            return;
        }

        _activeFilter = string.IsNullOrWhiteSpace(platform) || platform == "Instalados" ? "Todas" : platform;
        _dataVersion = _dataService.LibraryVersion;
        _focusedFilterIndex = Math.Max(0, Array.FindIndex(_filterButtons, b => (b.Tag as string) == _activeFilter));
        s_lastActiveFilter = _activeFilter;
        s_lastActiveSort = _activeSort;

        var games = _activeFilter switch
        {
            "Todas" => _dataService.GetAllGames().ToList(),
            "Favoritos" => _dataService.GetAllGames().Where(g => g.IsFavorite).ToList(),
            "Lojas" => _dataService.GetStoreEntries().ToList(),
            _ => _dataService.GetGamesByPlatform(_activeFilter).ToList()
        };
        _games = ApplySort(games);

        GamesGrid.ItemsSource = _games;
        GamesGrid.Margin = _activeFilter == "Lojas" ? new Thickness(0, 42, 0, 0) : new Thickness(0);
        EmptyStatePanel.Visibility = _games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        GamesGrid.Visibility = _games.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        LibrarySubtitle.Text = $"{GetFilterTitle(_activeFilter)}  -  {_games.Count} itens";
        _focusedGameIndex = Math.Clamp(s_lastFocusedGameIndex, 0, Math.Max(0, _games.Count - 1));
        UpdateFilterVisuals();
        UpdateSortVisuals();

        if (_games.Count > 0)
        {
            SetSelectedGame(_games[_focusedGameIndex]);
        }

        if (_activeFilter == "Lojas")
        {
            _ = RefreshCustomStoreIconsAsync();
        }

        _ = LoadArtworkAsync();
    }

    private async Task RefreshCustomStoreIconsAsync()
    {
        foreach (var store in _games.Where(game =>
                     game.IsStoreEntry &&
                     game.Id.StartsWith("custom-store-", StringComparison.OrdinalIgnoreCase) &&
                     File.Exists(game.StoreExecutablePath)))
        {
            var icon = await ApplicationIconService.GetIconAsync(store.StoreExecutablePath);
            if (icon is null)
            {
                continue;
            }

            store.PlatformIcon = icon;
            StoreLauncherService.SaveCustomStoreIcon(
                store.Id,
                ApplicationIconService.GetCachedIconPath(store.StoreExecutablePath));
        }
    }

    private static string GetFilterTitle(string filter) => filter switch
    {
        "Todas" => "Todos os jogos",
        "Favoritos" => "Favoritos",
        "Lojas" => "Lojas",
        _ => filter
    };

    private List<Game> ApplySort(List<Game> games) => _activeSort switch
    {
        "MaisJogados" => games.OrderByDescending(g => ParsePlayedHours(g.PlayTime)).ThenBy(g => g.Title).ToList(),
        "AZ" => games.OrderBy(g => g.Title).ToList(),
        "Adicionados" => games.AsEnumerable().Reverse().ToList(),
        "Plataforma" => games.OrderBy(g => g.Platform).ThenBy(g => g.Title).ToList(),
        _ => games.OrderByDescending(g => g.LastPlayed).ThenBy(g => g.Title).ToList()
    };

    private static double ParsePlayedHours(string playTime)
    {
        var hourPart = playTime.Split('h')[0].Trim();
        return double.TryParse(hourPart, out var hours) ? hours : 0;
    }

    private static Game CreateLibraryEntry(string id, string title, string platform, string playTime)
    {
        var cover = PlaceholderArtGenerator.ColorFromTitle(title);
        var accent = PlaceholderArtGenerator.AccentFrom(cover);
        return new Game
        {
            Id = id,
            Title = title,
            Platform = platform,
            CoverArtColor = cover,
            AccentColor = accent,
            PlayTime = playTime,
            LastPlayed = DateTime.Now,
            CoverArt = PlaceholderArtGenerator.CreateCover(title, cover),
            HeroArt = PlaceholderArtGenerator.CreateHero(title, cover, accent),
            PlatformIcon = PlatformIconService.GetIcon(platform == "Loja" ? title : platform),
            IsPlatformEntry = platform == "Loja"
        };
    }

    private void FilterButton_Click(object sender, RoutedEventArgs e)
    {
        var index = Array.IndexOf(_filterButtons, sender);
        if (index >= 0)
        {
            _focusedFilterIndex = index;
            s_lastFocusedFilterIndex = _focusedFilterIndex;
            s_lastFocusedGameIndex = 0;
            ApplyFilter((_filterButtons[index].Tag as string) ?? "Todas");
            FocusGameAtIndex(0);
        }
    }

    public void MoveFilter(bool previous)
    {
        var offset = previous ? -1 : 1;
        _focusedFilterIndex = (_focusedFilterIndex + offset + _filterButtons.Length) % _filterButtons.Length;
        s_lastFocusedFilterIndex = _focusedFilterIndex;
        s_lastFocusedGameIndex = 0;
        ApplyFilter((_filterButtons[_focusedFilterIndex].Tag as string) ?? "Todas");
        FocusGameAtIndex(0);
    }

    private void GameCard_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement slot && slot.DataContext is Game game)
        {
            var focusedIndex = _games.IndexOf(game);
            if (focusedIndex >= 0)
            {
                _focusedGameIndex = focusedIndex;
                s_lastFocusedGameIndex = focusedIndex;
            }

            var card = FocusUtilities.FindDescendantByName(slot, "LibraryGameCard") ?? slot;
            GameCardSelection.Apply(card, selected: true);
            SetSelectedGame(game);
        }
    }

    private void GamesGrid_GotFocus(object sender, RoutedEventArgs e)
    {
        if (_games.Count > 0)
        {
            SetSelectedGame(_games[_focusedGameIndex]);
        }
    }

    private void GameCard_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement slot)
        {
            var card = FocusUtilities.FindDescendantByName(slot, "LibraryGameCard") ?? slot;
            GameCardSelection.Apply(card, selected: false);
        }
    }

    private void SetSelectedGame(Game game)
    {
        BackgroundArt.Source = game.HeroArt;
    }

    private async Task LoadArtworkAsync()
    {
        if (_dataService is null || _games.Count == 0 || _activeFilter == "Lojas")
        {
            return;
        }

        await _dataService.LoadArtworkAsync(_games, DispatcherQueue, updatedGame =>
        {
            if (_focusedGameIndex >= 0 && _focusedGameIndex < _games.Count && _games[_focusedGameIndex] == updatedGame)
            {
                SetSelectedGame(updatedGame);
            }
        });
    }

    private void GamesGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Game game)
        {
            _ = OpenOrConfigureStoreAsync(game);
            if (!game.IsStoreEntry && App.MainWindow() is MainWindow window)
            {
                window.OpenGame(game);
            }
        }
    }

    public void FocusInitialElement()
    {
        FocusGameAtIndex(_focusedGameIndex);
    }

    public bool HandleGamepadMove(FocusNavigationDirection direction)
    {
        if (_searchInputActive)
        {
            if (direction == FocusNavigationDirection.Down)
            {
                CloseSearchInput();
            }

            return true;
        }

        if (_programBrowserOpen)
        {
            MoveProgramFocus(direction);
            return true;
        }

        if (_sortOverlayOpen)
        {
            MoveSortFocus(direction);
            return true;
        }

        if (_addOverlayOpen)
        {
            MoveAddFocus(direction);
            return true;
        }

        MoveGameFocus(direction);
        return true;
    }

    private void MoveGameFocus(FocusNavigationDirection direction)
    {
        var columns = GetGridColumnCount();
        var targetIndex = direction switch
        {
            FocusNavigationDirection.Left => _focusedGameIndex % columns == 0 ? _focusedGameIndex : _focusedGameIndex - 1,
            FocusNavigationDirection.Right => IsLastColumnOrLastItem(_focusedGameIndex, columns) ? _focusedGameIndex : _focusedGameIndex + 1,
            FocusNavigationDirection.Up => _focusedGameIndex < columns ? _focusedGameIndex : _focusedGameIndex - columns,
            FocusNavigationDirection.Down => _focusedGameIndex + columns >= _games.Count ? _focusedGameIndex : _focusedGameIndex + columns,
            _ => _focusedGameIndex
        };

        FocusGameAtIndex(targetIndex);
    }

    private bool IsLastColumnOrLastItem(int index, int columns) =>
        index >= _games.Count - 1 || index % columns == columns - 1;

    private int GetGridColumnCount()
    {
        var itemWidth = _activeFilter == "Lojas" ? 238d : CardOuterWidth;
        return Math.Max(1, (int)Math.Floor(GamesGrid.ActualWidth / itemWidth));
    }

    private void UpdateFilterVisuals()
    {
        for (var i = 0; i < _filterButtons.Length; i++)
        {
            var selected = i == _focusedFilterIndex;
            _filterButtons[i].BorderBrush = new SolidColorBrush(selected ? Color.FromArgb(255, 229, 83, 255) : Color.FromArgb(102, 255, 255, 255));
            _filterButtons[i].Background = new SolidColorBrush(selected ? Color.FromArgb(94, 95, 28, 111) : Color.FromArgb(170, 23, 21, 26));
        }
    }

    private void UpdateSortVisuals()
    {
        for (var i = 0; i < _sortButtons.Length; i++)
        {
            var selected = i == _focusedSortIndex;
            _sortButtons[i].BorderBrush = new SolidColorBrush(selected ? Color.FromArgb(255, 229, 83, 255) : Color.FromArgb(102, 255, 255, 255));
            _sortButtons[i].Background = new SolidColorBrush(selected ? Color.FromArgb(94, 95, 28, 111) : Color.FromArgb(170, 23, 21, 26));
        }

        CurrentSortText.Text = GetSortTitle(_activeSort);
    }

    private static string GetSortTitle(string sort) => sort switch
    {
        "MaisJogados" => "Mais jogados",
        "AZ" => "A-Z",
        "Adicionados" => "Adicionados recentemente",
        "Plataforma" => "Por plataforma",
        _ => "Mais recentes"
    };

    public bool HandleGamepadBack()
    {
        if (_searchInputActive)
        {
            CloseSearchInput();
            return true;
        }

        if (_programBrowserOpen)
        {
            CloseProgramBrowser();
            return true;
        }

        if (_sortOverlayOpen)
        {
            CloseSortOverlay();
            return true;
        }

        if (_addOverlayOpen)
        {
            CloseAddOverlay();
            return true;
        }

        return false;
    }

    public bool HandleGamepadX()
    {
        if (_searchInputActive)
        {
            return true;
        }

        if (_programBrowserOpen)
        {
            return true;
        }

        if (_sortOverlayOpen)
        {
            CloseSortOverlay();
        }
        else
        {
            OpenSortOverlay();
        }

        return true;
    }

    public bool HandleGamepadY()
    {
        if (_searchInputActive)
        {
            return true;
        }

        if (_programBrowserOpen)
        {
            OpenWindowsSearchKeyboard();
            return true;
        }

        if (_sortOverlayOpen)
        {
            return true;
        }

        if (_addOverlayOpen)
        {
            CloseAddOverlay();
        }
        else
        {
            OpenAddOverlay();
        }

        return true;
    }

    public bool HandleGamepadOptions()
    {
        _ = ShowSelectedGameOptionsAsync();
        return true;
    }

    public bool HandleGamepadAccept(FrameworkElement focusedElement)
    {
        if (_searchInputActive)
        {
            return true;
        }

        if (_programBrowserOpen)
        {
            AcceptProgramSelection();
            return true;
        }

        if (_sortOverlayOpen)
        {
            ApplySortSelection(_focusedSortIndex);
            return true;
        }

        if (_addOverlayOpen)
        {
            ApplyAddSelection(_focusedAddIndex);
            return true;
        }

        if (_games.Count > 0)
        {
            var game = _games[_focusedGameIndex];
            if (game.IsStoreEntry)
            {
                _ = OpenOrConfigureStoreAsync(game);
            }
            else if (App.MainWindow() is MainWindow window)
            {
                window.OpenGame(game);
            }

            return true;
        }

        return false;
    }

    private void SortButton_Click(object sender, RoutedEventArgs e)
    {
        var index = Array.IndexOf(_sortButtons, sender);
        if (index >= 0)
        {
            ApplySortSelection(index);
        }
    }

    private void OpenSortOverlay()
    {
        _sortOverlayOpen = true;
        SortOverlay.Visibility = Visibility.Visible;
        _focusedSortIndex = Math.Max(0, Array.FindIndex(_sortButtons, b => (b.Tag as string) == _activeSort));
        UpdateSortVisuals();
    }

    private void CloseSortOverlay()
    {
        _sortOverlayOpen = false;
        SortOverlay.Visibility = Visibility.Collapsed;
        FocusGameAtIndex(_focusedGameIndex);
    }

    private void MoveSortFocus(FocusNavigationDirection direction)
    {
        var previousIndex = _focusedSortIndex;
        if (direction == FocusNavigationDirection.Up)
        {
            _focusedSortIndex = Math.Max(0, _focusedSortIndex - 1);
        }
        else if (direction == FocusNavigationDirection.Down)
        {
            _focusedSortIndex = Math.Min(_sortButtons.Length - 1, _focusedSortIndex + 1);
        }

        if (_focusedSortIndex != previousIndex)
        {
            UpdateSortVisuals();
        }
    }

    private void ApplySortSelection(int index)
    {
        _focusedSortIndex = Math.Clamp(index, 0, _sortButtons.Length - 1);
        _activeSort = (_sortButtons[_focusedSortIndex].Tag as string) ?? "Recentes";
        CloseSortOverlay();
        ApplyFilter(_activeFilter);
        FocusGameAtIndex(0);
    }

    private void OpenAddOverlay()
    {
        _addOverlayOpen = true;
        AddOverlay.Visibility = Visibility.Visible;
        _focusedAddIndex = 0;
        UpdateAddVisuals();
    }

    private void CloseAddOverlay()
    {
        _addOverlayOpen = false;
        AddOverlay.Visibility = Visibility.Collapsed;
        FocusGameAtIndex(_focusedGameIndex);
    }

    private void MoveAddFocus(FocusNavigationDirection direction)
    {
        var previousIndex = _focusedAddIndex;
        if (direction == FocusNavigationDirection.Up)
        {
            _focusedAddIndex = Math.Max(0, _focusedAddIndex - 1);
        }
        else if (direction == FocusNavigationDirection.Down)
        {
            _focusedAddIndex = Math.Min(_addButtons.Length - 1, _focusedAddIndex + 1);
        }

        if (_focusedAddIndex != previousIndex)
        {
            UpdateAddVisuals();
        }
    }

    private void UpdateAddVisuals()
    {
        for (var i = 0; i < _addButtons.Length; i++)
        {
            var selected = i == _focusedAddIndex;
            _addButtons[i].BorderBrush = new SolidColorBrush(selected ? Color.FromArgb(255, 229, 83, 255) : Color.FromArgb(102, 255, 255, 255));
            _addButtons[i].Background = new SolidColorBrush(selected ? Color.FromArgb(94, 95, 28, 111) : Color.FromArgb(170, 23, 21, 26));
            _addButtons[i].BorderThickness = selected ? new Thickness(2) : new Thickness(1);
        }
    }

    private void ApplyAddSelection(int index)
    {
        _addingAsStore = index == 1;
        CloseAddOverlay();
        _ = OpenProgramBrowserAsync();
    }

    private void AddComputerGame_Click(object sender, RoutedEventArgs e)
    {
        _focusedAddIndex = 0;
        ApplyAddSelection(_focusedAddIndex);
    }

    private void AddApp_Click(object sender, RoutedEventArgs e)
    {
        _focusedAddIndex = 1;
        ApplyAddSelection(_focusedAddIndex);
    }

    private async Task BrowseComputerAsync(bool asStore)
    {
        if (_dataService is not { } dataService || App.MainWindow() is not MainWindow window)
        {
            return;
        }

        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.ComputerFolder
        };
        picker.FileTypeFilter.Add(".exe");
        picker.FileTypeFilter.Add(".lnk");
        picker.FileTypeFilter.Add(".url");
        picker.FileTypeFilter.Add(".appref-ms");
        InitializeWithWindow.Initialize(picker, window.WindowHandle);

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        var title = Path.GetFileNameWithoutExtension(file.Name);
        Game entry;
        if (asStore)
        {
            entry = dataService.AddCustomStore(title, file.Path);
            entry.PlatformIcon = await ApplicationIconService.GetIconAsync(file.Path);
            StoreLauncherService.SaveCustomStoreIcon(entry.Id, ApplicationIconService.GetCachedIconPath(file.Path));
            s_lastActiveFilter = "Lojas";
            _activeFilter = "Lojas";
        }
        else
        {
            entry = dataService.AddCustomGame(title, file.Path);
            s_lastActiveFilter = "Todas";
            _activeFilter = "Todas";
        }

        ApplyFilter(_activeFilter);
        FocusGameAtIndex(_games.Count - 1);
        await dataService.LoadArtworkAsync([entry], DispatcherQueue);
    }

    private void BrowseComputer_Click(object sender, RoutedEventArgs e) => _ = BrowseComputerFromBrowserAsync();

    private async Task BrowseComputerFromBrowserAsync()
    {
        var asStore = _addingAsStore;
        CloseProgramBrowser();
        await BrowseComputerAsync(asStore);
    }

    private async Task OpenProgramBrowserAsync()
    {
        _programBrowserOpen = true;
        ProgramBrowserOverlay.Visibility = Visibility.Visible;
        ProgramBrowserTitle.Text = _addingAsStore ? "Adicionar app" : "Adicionar jogo do computador";
        ProgramSearchBox.Text = _programSearchQuery;
        BrowseComputerButton.Focus(FocusState.Programmatic);

        _allPrograms = await Task.Run(() => ComputerProgramScanner.Scan().ToList());

        ApplyProgramSearch();
        _programFocusIndex = _visiblePrograms.Count > 0 ? 0 : BrowseProgramIndex;
        FocusProgramSelection();
    }

    private void CloseProgramBrowser()
    {
        _programBrowserOpen = false;
        _searchInputActive = false;
        ProgramBrowserOverlay.Visibility = Visibility.Collapsed;
        FocusGameAtIndex(_focusedGameIndex);
    }

    private void ApplyProgramSearch()
    {
        _visiblePrograms = string.IsNullOrWhiteSpace(_programSearchQuery)
            ? _allPrograms.ToList()
            : _allPrograms.Where(program =>
                program.Title.Contains(_programSearchQuery, StringComparison.CurrentCultureIgnoreCase))
                .ToList();
        ProgramGrid.ItemsSource = _visiblePrograms;
        _programFocusIndex = Math.Clamp(_programFocusIndex, 0, _visiblePrograms.Count);
    }

    private int BrowseProgramIndex => _visiblePrograms.Count;
    private int SearchProgramIndex => _visiblePrograms.Count + 1;

    private void MoveProgramFocus(FocusNavigationDirection direction)
    {
        const int columns = 5;
        var browseIndex = BrowseProgramIndex;
        var searchIndex = SearchProgramIndex;
        int target;

        if (_programFocusIndex == searchIndex)
        {
            target = direction switch
            {
                FocusNavigationDirection.Right => browseIndex,
                FocusNavigationDirection.Down when _visiblePrograms.Count > 0 => 0,
                _ => searchIndex
            };
        }
        else if (_programFocusIndex == browseIndex)
        {
            target = direction switch
            {
                FocusNavigationDirection.Left => searchIndex,
                FocusNavigationDirection.Down when _visiblePrograms.Count > 0 =>
                    Math.Min(columns - 1, _visiblePrograms.Count - 1),
                _ => browseIndex
            };
        }
        else
        {
            target = direction switch
            {
                FocusNavigationDirection.Left when _programFocusIndex % columns > 0 => _programFocusIndex - 1,
                FocusNavigationDirection.Right when _programFocusIndex + 1 < _visiblePrograms.Count &&
                                                    _programFocusIndex % columns < columns - 1 => _programFocusIndex + 1,
                FocusNavigationDirection.Up when _programFocusIndex < columns =>
                    _programFocusIndex == columns - 1 ? browseIndex : searchIndex,
                FocusNavigationDirection.Up => _programFocusIndex - columns,
                FocusNavigationDirection.Down when _programFocusIndex + columns < _visiblePrograms.Count =>
                    _programFocusIndex + columns,
                _ => _programFocusIndex
            };
        }

        _programFocusIndex = target;
        FocusProgramSelection();
    }

    private void FocusProgramSelection()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateProgramNavigationVisuals();

            if (_programFocusIndex == SearchProgramIndex)
            {
                ProgramGrid.SelectedIndex = -1;
                ProgramSearchBox.Focus(FocusState.Programmatic);
                return;
            }

            if (_programFocusIndex == BrowseProgramIndex)
            {
                ProgramGrid.SelectedIndex = -1;
                BrowseComputerButton.Focus(FocusState.Programmatic);
                return;
            }

            var index = _programFocusIndex;
            if (index < 0 || index >= _visiblePrograms.Count)
            {
                return;
            }

            var item = _visiblePrograms[index];
            ProgramGrid.SelectedIndex = index;
            ProgramGrid.ScrollIntoView(item);
            DispatcherQueue.TryEnqueue(() =>
            {
                ProgramGrid.UpdateLayout();
                if (ProgramGrid.ContainerFromIndex(index) is Control container)
                {
                    container.Focus(FocusState.Programmatic);
                }
            });
        });
    }

    private void AcceptProgramSelection()
    {
        if (_programFocusIndex == SearchProgramIndex)
        {
            OpenWindowsSearchKeyboard();
            return;
        }

        if (_programFocusIndex == BrowseProgramIndex)
        {
            _ = BrowseComputerFromBrowserAsync();
            return;
        }

        var index = _programFocusIndex;
        if (index >= 0 && index < _visiblePrograms.Count)
        {
            _ = AddProgramEntryAsync(_visiblePrograms[index]);
        }
    }

    private void ProgramGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ComputerProgramEntry entry)
        {
            _ = AddProgramEntryAsync(entry);
        }
    }

    private async Task AddProgramEntryAsync(ComputerProgramEntry program)
    {
        if (_dataService is null)
        {
            return;
        }

        Game entry;
        if (_addingAsStore)
        {
            entry = _dataService.AddCustomStore(program.Title, program.ExecutablePath);
            entry.PlatformIcon = await ApplicationIconService.GetIconAsync(program.ExecutablePath);
            StoreLauncherService.SaveCustomStoreIcon(
                entry.Id,
                ApplicationIconService.GetCachedIconPath(program.ExecutablePath));
            _activeFilter = "Lojas";
            s_lastActiveFilter = "Lojas";
        }
        else
        {
            entry = _dataService.AddCustomGame(program.Title, program.ExecutablePath, program.Arguments);
            _activeFilter = "Todas";
            s_lastActiveFilter = "Todas";
        }

        CloseProgramBrowser();
        ApplyFilter(_activeFilter);
        var index = _games.FindIndex(game => game == entry);
        FocusGameAtIndex(index >= 0 ? index : Math.Max(0, _games.Count - 1));
        await _dataService.LoadArtworkAsync([entry], DispatcherQueue);
    }

    private void OpenWindowsSearchKeyboard()
    {
        _searchInputActive = true;
        _programFocusIndex = SearchProgramIndex;
        UpdateProgramNavigationVisuals();
        ProgramSearchBox.Focus(FocusState.Programmatic);
        ProgramSearchBox.Select(ProgramSearchBox.Text.Length, 0);
        if (App.MainWindow() is MainWindow window)
        {
            WindowsTouchKeyboardService.Show(window.WindowHandle);
        }
    }

    private void CloseSearchInput()
    {
        _searchInputActive = false;
        ApplyProgramSearch();
        _programFocusIndex = _visiblePrograms.Count > 0 ? 0 : BrowseProgramIndex;
        FocusProgramSelection();
    }

    private void ProgramSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _programSearchQuery = ProgramSearchBox.Text.Trim();
        if (!_programBrowserOpen)
        {
            return;
        }

        ApplyProgramSearch();
        _programFocusIndex = _visiblePrograms.Count > 0 ? 0 : BrowseProgramIndex;
    }

    private void UpdateProgramNavigationVisuals()
    {
        var searchSelected = _programFocusIndex == SearchProgramIndex;
        ProgramSearchSelectionBorder.BorderBrush = new SolidColorBrush(
            searchSelected ? Color.FromArgb(255, 229, 83, 255) : Color.FromArgb(69, 255, 255, 255));
        ProgramSearchSelectionBorder.BorderThickness = searchSelected ? new Thickness(2.5) : new Thickness(1);
        ProgramSearchSelectionBorder.Background = new SolidColorBrush(
            searchSelected ? Color.FromArgb(120, 78, 30, 94) : Color.FromArgb(197, 18, 18, 24));

        var browseSelected = _programFocusIndex == BrowseProgramIndex;
        BrowseComputerButton.BorderBrush = new SolidColorBrush(
            browseSelected ? Color.FromArgb(255, 229, 83, 255) : Color.FromArgb(69, 255, 255, 255));
        BrowseComputerButton.BorderThickness = browseSelected ? new Thickness(2.5) : new Thickness(1);
        BrowseComputerButton.Background = new SolidColorBrush(
            browseSelected ? Color.FromArgb(120, 78, 30, 94) : Color.FromArgb(197, 18, 18, 24));
    }

    private async Task OpenOrConfigureStoreAsync(Game game)
    {
        if (!game.IsStoreEntry)
        {
            return;
        }

        if (StoreLauncherService.Launch(game))
        {
            _dataService?.RecordGameLaunched(game);
            s_lastFocusedGameIndex = 0;
            ApplyFilter("Lojas");
            FocusGameAtIndex(0);
            return;
        }

        await PickStoreExecutableAsync(game);
    }

    private async Task PickStoreExecutableAsync(Game store)
    {
        if (_dataService is not { } dataService || App.MainWindow() is not MainWindow window)
        {
            return;
        }

        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.ComputerFolder
        };
        picker.FileTypeFilter.Add(".exe");
        InitializeWithWindow.Initialize(picker, window.WindowHandle);

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        dataService.SetStoreExecutable(store, file.Path);
        ApplyFilter(_activeFilter);
        FocusGameAtIndex(_focusedGameIndex);
    }

    private Task ShowSelectedGameOptionsAsync()
    {
        if (_dataService is null || _games.Count == 0 || App.MainWindow() is not MainWindow window)
        {
            return Task.CompletedTask;
        }

        var focusedGame = FocusUtilities.TryGetFocusedElement(this, out var focusedElement)
            ? focusedElement.DataContext as Game
            : null;
        var game = focusedGame is not null && _games.Contains(focusedGame)
            ? focusedGame
            : _games[Math.Clamp(_focusedGameIndex, 0, _games.Count - 1)];
        _focusedGameIndex = _games.IndexOf(game);
        s_lastFocusedGameIndex = _focusedGameIndex;

        if (game.IsPlatformEntry && !game.IsStoreEntry)
        {
            return Task.CompletedTask;
        }

        window.OpenGameOptionsPanel(
            game,
            refreshAfterChange: () =>
            {
                ApplyFilter(_activeFilter);
                FocusGameAtIndex(_focusedGameIndex);
            },
            removed: () =>
            {
                ApplyFilter(_activeFilter);
                _focusedGameIndex = Math.Clamp(_focusedGameIndex, 0, Math.Max(0, _games.Count - 1));
                FocusGameAtIndex(_focusedGameIndex);
            });
        return Task.CompletedTask;
    }

    private void FocusGameAtIndex(int index)
    {
        if (_games.Count == 0)
        {
            _focusedGameIndex = 0;
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

            var game = _games[_focusedGameIndex];
            var existingContainer = GamesGrid.ContainerFromIndex(_focusedGameIndex) as FrameworkElement;
            var needsScroll = _restoringSelection || !IsFullyVisible(existingContainer, GamesGrid);
            if (needsScroll && _restoringSelection)
            {
                GamesGrid.ScrollIntoView(game, ScrollIntoViewAlignment.Leading);
                _restoringSelection = false;
            }
            else if (needsScroll)
            {
                GamesGrid.ScrollIntoView(game);
            }
            if (needsScroll)
            {
                GamesGrid.UpdateLayout();
            }

            if (GamesGrid.ContainerFromIndex(_focusedGameIndex) is Control card)
            {
                var cardElement = FocusUtilities.FindDescendantByName(card, "LibraryGameCard");
                var slotElement = FocusUtilities.FindDescendantByName(card, "LibraryGameSlot");
                FrameworkElement focusTarget = slotElement is not null ? slotElement : card;
                focusTarget.Focus(FocusState.Programmatic);
            }

            SetSelectedGame(game);
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
            var position = element.TransformToVisual(viewport).TransformPoint(new Windows.Foundation.Point(0, 0));
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
}
