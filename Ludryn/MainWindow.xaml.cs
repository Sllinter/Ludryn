using Ludryn.Models;
using Ludryn.Services;
using Ludryn.Views;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Windows.System;
using Windows.Gaming.Input;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;

namespace Ludryn;

public sealed partial class MainWindow : Window
{
    private readonly MockDataService _dataService = new();
    private readonly Button[] _tabButtons;
    private readonly DispatcherTimer _gamepadTimer = new();
    private GamepadButtons _previousButtons;
    private bool _leftTriggerWasPressed;
    private bool _rightTriggerWasPressed;
    private bool _steamGridDbMessageShown;
    private DateTime _lastDirectionalMove = DateTime.MinValue;
    private DateTime _lastBatteryUpdate = DateTime.MinValue;
    private int _selectedTabIndex;
    private readonly List<Button> _optionsButtons = [];
    private int _optionsFocusedIndex;
    private int _optionsColumns = 1;
    private Game? _optionsGame;
    private Action? _optionsRefreshAfterChange;
    private Action? _optionsRemoved;
    private bool _optionsBusy;
    private bool IsOptionsPanelOpen => OptionsSideLayer.Visibility == Visibility.Visible;
    private readonly List<Button> _profileButtons = [];
    private readonly List<string> _profilePasswordSequence = [];
    private int _profileFocusedIndex;
    private bool _profileCapturingPassword;
    private bool _profileCreatingPassword;
    private bool IsProfilePanelOpen => ProfileSideLayer.Visibility == Visibility.Visible;

    public IntPtr WindowHandle => WindowNative.GetWindowHandle(this);

    public MainWindow()
    {
        InitializeComponent();
        _tabButtons = [HomeTabButton, LibraryTabButton, EmulatorsTabButton, SettingsTabButton];
        ExtendsContentIntoTitleBar = true;
        Fullscreen();
        ContentFrame.Navigated += ContentFrame_Navigated;
        ConfigureGamepadPolling();
        UpdateClock();
        NavigateToTab(AppSettingsService.StartupPageIndex);
    }

    public void OpenGame(Game game)
    {
        ContentFrame.Navigate(typeof(GameDetailPage), new GameDetailNavigation(game, _dataService));
    }

    public void OpenGameOptionsPanel(Game game, Action? refreshAfterChange = null, Action? removed = null)
    {
        LudrynLogger.Log("options", $"Open panel. Game={game.Title}; Id={game.Id}");
        _optionsGame = game;
        _optionsRefreshAfterChange = refreshAfterChange;
        _optionsRemoved = removed;
        _optionsBusy = false;
        OptionsSideLayer.Visibility = Visibility.Visible;
        ShowGameOptionsMenu();
    }

    public void OpenLibrary(string? platform = null)
    {
        _selectedTabIndex = 1;
        UpdateTabVisuals();
        ContentFrame.Navigate(typeof(LibraryPage), new LibraryNavigation(_dataService, platform));
    }

    public void OpenEmulatorSettings()
    {
        _selectedTabIndex = 2;
        UpdateTabVisuals();
        ContentFrame.Navigate(typeof(EmulatorSettingsPage), _dataService);
    }

    public void OpenAddEmulatorSetup()
    {
        _selectedTabIndex = 2;
        UpdateTabVisuals();
        ContentFrame.Navigate(typeof(AddEmulatorSetupPage), _dataService);
    }

    public void OpenAddRomDirectorySetup()
    {
        _selectedTabIndex = 2;
        UpdateTabVisuals();
        ContentFrame.Navigate(typeof(AddRomDirectorySetupPage), _dataService);
    }

    public void GoBack()
    {
        if (ContentFrame.CanGoBack)
        {
            ContentFrame.GoBack();
        }
        else
        {
            NavigateToTab(_selectedTabIndex);
        }
    }

    private void Fullscreen()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Up)
        {
            TryMoveFocusSafely(FocusNavigationDirection.Up);
            e.Handled = true;
        }
        else if (e.Key is VirtualKey.Down)
        {
            TryMoveFocusSafely(FocusNavigationDirection.Down);
            e.Handled = true;
        }
        else if (e.Key is VirtualKey.Left)
        {
            TryMoveFocusSafely(FocusNavigationDirection.Left);
            e.Handled = true;
        }
        else if (e.Key is VirtualKey.Right)
        {
            TryMoveFocusSafely(FocusNavigationDirection.Right);
            e.Handled = true;
        }
    }

    private void HomeTab_Click(object sender, RoutedEventArgs e) => NavigateToTab(0);
    private void LibraryTab_Click(object sender, RoutedEventArgs e) => NavigateToTab(1);
    private void EmulatorsTab_Click(object sender, RoutedEventArgs e) => NavigateToTab(2);
    private void SettingsTab_Click(object sender, RoutedEventArgs e) => NavigateToTab(3);

    private void NavigateToTab(int index, bool? movingForward = null)
    {
        index = Math.Clamp(index, 0, _tabButtons.Length - 1);
        var currentPageType = ContentFrame.Content?.GetType();
        var targetPageType = GetTabPageType(index);
        if (_selectedTabIndex == index && currentPageType == targetPageType)
        {
            return;
        }

        var previousIndex = _selectedTabIndex;
        _selectedTabIndex = index;
        UpdateTabVisuals();

        object parameter = index switch
        {
            0 => _dataService,
            1 => new LibraryNavigation(_dataService, null),
            2 => _dataService,
            _ => _dataService
        };

        var forward = movingForward ?? index > previousIndex;
        var transition = new SlideNavigationTransitionInfo
        {
            Effect = forward
                ? SlideNavigationTransitionEffect.FromRight
                : SlideNavigationTransitionEffect.FromLeft
        };

        ContentFrame.Navigate(targetPageType, parameter, transition);
    }

    private static Type GetTabPageType(int index) => index switch
    {
        0 => typeof(HomePage),
        1 => typeof(LibraryPage),
        2 => typeof(EmulatorsPage),
        _ => typeof(SettingsPage)
    };

    private void ContentFrame_Navigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        UpdateSelectedTabFromCurrentPage();
        RefreshGamepadHints();
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (ContentFrame.Content is IGamepadFocusablePage focusablePage)
            {
                focusablePage.FocusInitialElement();
            }
            else
            {
                RootGrid.Focus(FocusState.Programmatic);
            }

            ShowSteamGridDbConfigMessageIfNeeded();
        });
    }

    public void RefreshGamepadHints()
    {
        var hints = ContentFrame.Content is IGamepadHintProvider provider
            ? provider.GetGamepadHints()
            : new GamepadHints(
                Accept: "Selecionar",
                Back: ContentFrame.Content is HomePage ? null : "Voltar");

        SetGamepadHint(AcceptHint, AcceptHintText, hints.Accept);
        SetGamepadHint(XHint, XHintText, hints.X);
        SetGamepadHint(YHint, YHintText, hints.Y);
        SetGamepadHint(BackHint, BackHintText, hints.Back);
    }

    private static void SetGamepadHint(FrameworkElement container, TextBlock label, string? text)
    {
        var visible = !string.IsNullOrWhiteSpace(text);
        container.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (visible)
        {
            label.Text = text!;
        }
    }

    private async void ShowSteamGridDbConfigMessageIfNeeded()
    {
        if (_steamGridDbMessageShown || string.IsNullOrWhiteSpace(_dataService.SteamGridDbMessage) || RootGrid.XamlRoot is null)
        {
            return;
        }

        _steamGridDbMessageShown = true;
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "Conectar SteamGridDB",
            Content = "Para carregar capas, fundos e letreiros, conecte sua API Key oficial do SteamGridDB.\n\n1. Crie ou acesse sua conta no SteamGridDB.\n2. Abra a página de API.\n3. Copie sua API Key.\n4. No Ludryn, abra Configurações > SteamGridDB e cole a chave.",
            PrimaryButtonText = "Abrir página da API",
            CloseButtonText = "Depois"
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await Launcher.LaunchUriAsync(new Uri(SteamGridDbService.ApiSettingsUrl));
        }
    }

    private void ConfigureGamepadPolling()
    {
        _gamepadTimer.Interval = TimeSpan.FromMilliseconds(16);
        _gamepadTimer.Tick += (_, _) =>
        {
            PollGamepad();
            UpdateClock();
        };
        _gamepadTimer.Start();
    }

    private void UpdateClock()
    {
        ClockText.Text = DateTime.Now.ToString("HH:mm");
    }

    private void UpdateControllerBattery(Gamepad? gamepad, bool force = false)
    {
        if (!force && (DateTime.Now - _lastBatteryUpdate).TotalSeconds < 5)
        {
            return;
        }

        _lastBatteryUpdate = DateTime.Now;

        if (gamepad is null)
        {
            ControllerBatteryText.Text = "--";
            return;
        }

        try
        {
            var report = gamepad.TryGetBatteryReport();
            var remaining = report.RemainingCapacityInMilliwattHours;
            var full = report.FullChargeCapacityInMilliwattHours;
            if (remaining is null || full is null || full.Value <= 0)
            {
                ControllerBatteryText.Text = "--";
                return;
            }

            var percentage = Math.Clamp((int)Math.Round(remaining.Value * 100d / full.Value), 0, 100);
            ControllerBatteryText.Text = percentage >= 100 ? "100" : percentage.ToString();
        }
        catch
        {
            ControllerBatteryText.Text = "--";
        }
    }

    private void PollGamepad()
    {
        var gamepad = Gamepad.Gamepads.FirstOrDefault();
        if (gamepad is null)
        {
            _previousButtons = GamepadButtons.None;
            UpdateControllerBattery(null);
            return;
        }

        UpdateControllerBattery(gamepad);
        var reading = gamepad.GetCurrentReading();
        var buttons = reading.Buttons;
        var leftTriggerPressed = reading.LeftTrigger > 0.55;
        var rightTriggerPressed = reading.RightTrigger > 0.55;

        if (IsOptionsPanelOpen)
        {
            if (WasPressed(buttons, GamepadButtons.A))
            {
                AcceptOptionsPanel();
            }
            else if (WasPressed(buttons, GamepadButtons.B) || WasPressed(buttons, GamepadButtons.Menu))
            {
                CloseOptionsPanel();
            }
            else
            {
                MoveFocusFromGamepad(reading);
            }

            _previousButtons = buttons;
            _leftTriggerWasPressed = leftTriggerPressed;
            _rightTriggerWasPressed = rightTriggerPressed;
            return;
        }

        if (IsProfilePanelOpen)
        {
            HandleProfilePanelInput(buttons, reading);
            _previousButtons = buttons;
            _leftTriggerWasPressed = leftTriggerPressed;
            _rightTriggerWasPressed = rightTriggerPressed;
            return;
        }

        if (ContentFrame.Content is LibraryPage { IsProgramBrowserOpen: true } programBrowser)
        {
            if (programBrowser.IsProgramSearchActive)
            {
                if (WasPressed(buttons, GamepadButtons.B))
                {
                    programBrowser.HandleGamepadBack();
                }
                else
                {
                    MoveFocusFromGamepad(reading);
                }
            }
            else if (WasPressed(buttons, GamepadButtons.A))
            {
                programBrowser.HandleGamepadAccept(RootGrid);
            }
            else if (WasPressed(buttons, GamepadButtons.B) || WasPressed(buttons, GamepadButtons.Menu))
            {
                programBrowser.HandleGamepadBack();
            }
            else if (WasPressed(buttons, GamepadButtons.X))
            {
                programBrowser.HandleGamepadX();
            }
            else if (WasPressed(buttons, GamepadButtons.Y))
            {
                programBrowser.HandleGamepadY();
            }
            else
            {
                MoveFocusFromGamepad(reading);
            }

            RefreshGamepadHints();
            _previousButtons = buttons;
            _leftTriggerWasPressed = leftTriggerPressed;
            _rightTriggerWasPressed = rightTriggerPressed;
            return;
        }

        if (WasPressed(buttons, GamepadButtons.View))
        {
            OpenProfilePanel();
        }
        else if (WasPressed(buttons, GamepadButtons.LeftShoulder))
        {
            NavigateToTab(
                (_selectedTabIndex + _tabButtons.Length - 1) % _tabButtons.Length,
                movingForward: false);
        }
        else if (WasPressed(buttons, GamepadButtons.RightShoulder))
        {
            NavigateToTab(
                (_selectedTabIndex + 1) % _tabButtons.Length,
                movingForward: true);
        }
        else if (WasPressed(buttons, GamepadButtons.A))
        {
            InvokeFocusedElement();
            RefreshGamepadHints();
        }
        else if (WasPressed(buttons, GamepadButtons.B))
        {
            GoBackFromGamepad();
            RefreshGamepadHints();
        }
        else if (WasPressed(buttons, GamepadButtons.X) && ContentFrame.Content is IGamepadFocusablePage xPage)
        {
            xPage.HandleGamepadX();
            RefreshGamepadHints();
        }
        else if (WasPressed(buttons, GamepadButtons.Y) && ContentFrame.Content is IGamepadFocusablePage yPage)
        {
            yPage.HandleGamepadY();
            RefreshGamepadHints();
        }
        else if (WasPressed(buttons, GamepadButtons.Menu) && ContentFrame.Content is IGamepadFocusablePage optionsPage)
        {
            optionsPage.HandleGamepadOptions();
            RefreshGamepadHints();
        }
        else if (leftTriggerPressed && !_leftTriggerWasPressed && ContentFrame.Content is LibraryPage libraryForLeftTrigger)
        {
            libraryForLeftTrigger.MoveFilter(previous: true);
        }
        else if (rightTriggerPressed && !_rightTriggerWasPressed && ContentFrame.Content is LibraryPage libraryForRightTrigger)
        {
            libraryForRightTrigger.MoveFilter(previous: false);
        }
        else if (leftTriggerPressed && !_leftTriggerWasPressed && ContentFrame.Content is AddEmulatorSetupPage addEmulatorForLeftTrigger)
        {
            addEmulatorForLeftTrigger.MovePlatform(previous: true);
        }
        else if (rightTriggerPressed && !_rightTriggerWasPressed && ContentFrame.Content is AddEmulatorSetupPage addEmulatorForRightTrigger)
        {
            addEmulatorForRightTrigger.MovePlatform(previous: false);
        }
        else if (leftTriggerPressed && !_leftTriggerWasPressed && ContentFrame.Content is AddRomDirectorySetupPage addRomForLeftTrigger)
        {
            addRomForLeftTrigger.MovePlatform(previous: true);
        }
        else if (rightTriggerPressed && !_rightTriggerWasPressed && ContentFrame.Content is AddRomDirectorySetupPage addRomForRightTrigger)
        {
            addRomForRightTrigger.MovePlatform(previous: false);
        }

        MoveFocusFromGamepad(reading);
        _previousButtons = buttons;
        _leftTriggerWasPressed = leftTriggerPressed;
        _rightTriggerWasPressed = rightTriggerPressed;
    }

    private bool WasPressed(GamepadButtons currentButtons, GamepadButtons button) =>
        currentButtons.HasFlag(button) && !_previousButtons.HasFlag(button);

    private void MoveFocusFromGamepad(GamepadReading reading)
    {
        var direction = GetDirection(reading);
        if (direction is null)
        {
            return;
        }

        var fastLibraryNavigation = ContentFrame.Content is LibraryPage library &&
            (library.IsSortOverlayOpen || library.IsProgramBrowserOpen);
        var moveInterval = fastLibraryNavigation ? 70 : 140;
        if ((DateTime.Now - _lastDirectionalMove).TotalMilliseconds < moveInterval)
        {
            return;
        }

        if (IsOptionsPanelOpen)
        {
            MoveOptionsPanel(direction.Value);
            _lastDirectionalMove = DateTime.Now;
            return;
        }

        if (ContentFrame.Content is IGamepadFocusablePage page && page.HandleGamepadMove(direction.Value))
        {
            _lastDirectionalMove = DateTime.Now;
            return;
        }

        if (TryMoveFocusSafely(direction.Value))
        {
            _lastDirectionalMove = DateTime.Now;
        }
    }

    private bool TryMoveFocusSafely(FocusNavigationDirection direction)
    {
        if (!IsElementReadyForFocus(RootGrid))
        {
            return false;
        }

        FocusUtilities.TryGetFocusedElement(RootGrid, out var focusedElement);
        if (focusedElement is not null && !IsElementReadyForFocus(focusedElement))
        {
            FocusCurrentPageInitialElement();
            FocusUtilities.TryGetFocusedElement(RootGrid, out focusedElement);
            if (focusedElement is not null && !IsElementReadyForFocus(focusedElement))
            {
                return false;
            }
        }

        try
        {
            var moved = FocusManager.TryMoveFocus(direction);
            FocusUtilities.TryGetFocusedElement(RootGrid, out var target);
            return moved && IsElementReadyForFocus(target);
        }
        catch (COMException)
        {
            FocusCurrentPageInitialElement();
            return false;
        }
        catch (InvalidOperationException)
        {
            FocusCurrentPageInitialElement();
            return false;
        }
    }

    private static bool IsElementReadyForFocus(FrameworkElement? element)
    {
        if (element is null || !element.IsLoaded || element.XamlRoot is null || element.Visibility != Visibility.Visible)
        {
            return false;
        }

        var parent = VisualTreeHelper.GetParent(element) as FrameworkElement;
        while (parent is not null)
        {
            if (!parent.IsLoaded || parent.Visibility != Visibility.Visible)
            {
                return false;
            }

            parent = VisualTreeHelper.GetParent(parent) as FrameworkElement;
        }

        return true;
    }

    private static FocusNavigationDirection? GetDirection(GamepadReading reading)
    {
        const double stickThreshold = 0.35;
        var buttons = reading.Buttons;

        if (buttons.HasFlag(GamepadButtons.DPadUp) || reading.LeftThumbstickY > stickThreshold)
        {
            return FocusNavigationDirection.Up;
        }

        if (buttons.HasFlag(GamepadButtons.DPadDown) || reading.LeftThumbstickY < -stickThreshold)
        {
            return FocusNavigationDirection.Down;
        }

        if (buttons.HasFlag(GamepadButtons.DPadLeft) || reading.LeftThumbstickX < -stickThreshold)
        {
            return FocusNavigationDirection.Left;
        }

        if (buttons.HasFlag(GamepadButtons.DPadRight) || reading.LeftThumbstickX > stickThreshold)
        {
            return FocusNavigationDirection.Right;
        }

        return null;
    }

    private void InvokeFocusedElement()
    {
        if (IsOptionsPanelOpen)
        {
            AcceptOptionsPanel();
            return;
        }

        if (!FocusUtilities.TryGetFocusedElement(RootGrid, out var focusedElement))
        {
            FocusCurrentPageInitialElement();
            return;
        }

        if (!IsOptionsPanelOpen && ContentFrame.Content is IGamepadFocusablePage page && page.HandleGamepadAccept(focusedElement))
        {
            return;
        }

        var current = focusedElement;
        while (current is not null)
        {
            var peer = FrameworkElementAutomationPeer.FromElement(current) ?? FrameworkElementAutomationPeer.CreatePeerForElement(current);
            if (peer?.GetPattern(PatternInterface.Invoke) is IInvokeProvider invokeProvider)
            {
                invokeProvider.Invoke();
                return;
            }

            current = VisualTreeHelper.GetParent(current) as FrameworkElement;
        }
    }

    private void GoBackFromGamepad()
    {
        if (CloseOptionsPanel())
        {
            return;
        }

        if (ContentFrame.Content is IGamepadFocusablePage page && page.HandleGamepadBack())
        {
            return;
        }

        if (ContentFrame.Content is HomePage)
        {
            return;
        }

        GoBack();
    }

    private void FocusCurrentPageInitialElement()
    {
        if (ContentFrame.Content is IGamepadFocusablePage focusablePage)
        {
            focusablePage.FocusInitialElement();
        }
    }

    private void ShowGameOptionsMenu()
    {
        if (_optionsGame is null)
        {
            return;
        }

        OptionsPanelTitle.Text = _optionsGame.Title;
        OptionsPanelSubtitle.Text = "A Selecionar   B Voltar";
        OptionsPanelContent.Children.Clear();
        _optionsButtons.Clear();
        _optionsColumns = 1;

        if (_optionsGame.IsStoreEntry)
        {
            AddOptionsButton("Buscar ícone", () => RunOptionTask(() => ShowArtworkOptionsAsync(ArtworkKind.Icon)));
            AddOptionsButton("Remover app", () =>
            {
                if (_optionsGame is null)
                {
                    return;
                }

                _dataService.RemoveGame(_optionsGame);
                var removed = _optionsRemoved;
                CloseOptionsPanel();
                removed?.Invoke();
            });
            FocusOptionsButton(0);
            return;
        }

        AddOptionsButton(_optionsGame.IsFavorite ? "Remover dos favoritos" : "Adicionar aos favoritos", () =>
        {
            if (_optionsGame is null)
            {
                return;
            }

            _dataService.ToggleFavorite(_optionsGame);
            _optionsRefreshAfterChange?.Invoke();
            ShowGameOptionsMenu();
        });

        if (_optionsGame.HasMultipleLaunchers)
        {
            AddOptionsButton($"Launcher: {_optionsGame.SelectedLauncher}", ShowLauncherOptions);
        }

        AddOptionsButton("Trocar banner", () => ShowArtworkSourceOptions(ArtworkKind.Cover));
        AddOptionsButton("Trocar imagem de fundo", () => ShowArtworkSourceOptions(ArtworkKind.Hero));
        AddOptionsButton("Trocar letreiro", () => ShowArtworkSourceOptions(ArtworkKind.Logo));
        AddOptionsButton("Remover da biblioteca", () =>
        {
            if (_optionsGame is null)
            {
                return;
            }

            _dataService.RemoveGame(_optionsGame);
            var removed = _optionsRemoved;
            CloseOptionsPanel();
            removed?.Invoke();
        });
        AddOptionsButton(
            _dataService.IsPrivateGame(_optionsGame)
                ? "Tornar jogo visível no perfil principal"
                : "Privar jogo no perfil secundário",
            () =>
            {
                if (_optionsGame is null)
                {
                    return;
                }

                var makePrivate = !_dataService.IsPrivateGame(_optionsGame);
                _dataService.SetPrivateGame(_optionsGame, makePrivate);
                var refresh = _optionsRefreshAfterChange;
                var removed = _optionsRemoved;
                CloseOptionsPanel();
                refresh?.Invoke();
                if (makePrivate && !_dataService.IsSecondaryProfile)
                {
                    removed?.Invoke();
                }
            });

        FocusOptionsButton(0);
    }

    private void ShowArtworkSourceOptions(ArtworkKind kind)
    {
        OptionsPanelTitle.Text = kind switch
        {
            ArtworkKind.Cover => "Trocar banner",
            ArtworkKind.Hero => "Trocar imagem de fundo",
            ArtworkKind.Logo => "Trocar letreiro",
            ArtworkKind.Icon => "Buscar ícone",
            _ => "Trocar arte"
        };
        OptionsPanelSubtitle.Text = "A Selecionar   B Voltar";
        OptionsPanelContent.Children.Clear();
        _optionsButtons.Clear();
        _optionsColumns = 1;

        AddOptionsButton("Pesquisar com SteamGridDB", () => RunOptionTask(() => ShowArtworkOptionsAsync(kind)));
        AddOptionsButton("Escolher do computador", () => RunOptionTask(() => PickLocalArtworkAsync(kind)));
        FocusOptionsButton(0);
    }

    private async Task PickLocalArtworkAsync(ArtworkKind kind)
    {
        if (_optionsGame is null || _optionsBusy)
        {
            return;
        }

        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.Thumbnail,
            SuggestedStartLocation = PickerLocationId.PicturesLibrary
        };
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".webp");
        InitializeWithWindow.Initialize(picker, WindowHandle);

        _optionsBusy = true;
        try
        {
            var file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                _optionsBusy = false;
                ShowArtworkSourceOptions(kind);
                return;
            }

            ShowOptionsLoading(kind);
            var applied = await _dataService.ApplyLocalArtworkAsync(_optionsGame, kind, file.Path, DispatcherQueue);
            _optionsBusy = false;
            if (applied)
            {
                _optionsRefreshAfterChange?.Invoke();
                ShowGameOptionsMenu();
            }
            else
            {
                ShowOptionsMessage("Não foi possível usar a imagem", "Escolha uma imagem PNG, JPG, JPEG ou WEBP válida.");
            }
        }
        catch (Exception ex)
        {
            _optionsBusy = false;
            LogOptions($"Local artwork failed. Kind={kind}; Error={ex}");
            ShowOptionsMessage("Não foi possível usar a imagem", ex.Message);
        }
    }

    private void ShowLauncherOptions()
    {
        if (_optionsGame is null)
        {
            return;
        }

        OptionsPanelTitle.Text = "Escolher launcher";
        OptionsPanelSubtitle.Text = "A Confirmar   B Voltar";
        OptionsPanelContent.Children.Clear();
        _optionsButtons.Clear();
        _optionsColumns = 1;

        foreach (var launcher in _optionsGame.Installations.Select(i => i.Launcher).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var selected = string.Equals(launcher, _optionsGame.SelectedLauncher, StringComparison.OrdinalIgnoreCase);
            AddOptionsButton(selected ? $"{launcher}  -  selecionado" : launcher, () =>
            {
                if (_optionsGame is null)
                {
                    return;
                }

                _dataService.SelectLauncher(_optionsGame, launcher);
                _optionsRefreshAfterChange?.Invoke();
                ShowGameOptionsMenu();
            });
        }

        FocusOptionsButton(0);
    }

    private async Task ShowArtworkOptionsAsync(ArtworkKind kind)
    {
        if (_optionsGame is null || _optionsBusy)
        {
            LudrynLogger.Log("options", $"Artwork search ignored. Kind={kind}; HasGame={_optionsGame is not null}; Busy={_optionsBusy}");
            return;
        }

        if (!_dataService.HasSteamGridDb)
        {
            LudrynLogger.Log("options", $"Artwork search blocked without SteamGridDB key. Kind={kind}; Game={_optionsGame.Title}");
            ShowOptionsMessage("SteamGridDB desconectado", "Conecte uma API Key em Configurações para buscar artes.");
            return;
        }

        _optionsBusy = true;
        ShowOptionsLoading(kind);
        var game = _optionsGame;

        try
        {
            LogOptions($"Artwork search started. Kind={kind}; Game={game.Title}; Id={game.Id}");
            var options = await _dataService.GetArtworkOptionsAsync(game, kind, 96).WaitAsync(TimeSpan.FromSeconds(28));
            LogOptions($"Artwork search finished. Kind={kind}; Game={game.Title}; Count={options.Count}");

            if (!IsOptionsPanelOpen || _optionsGame != game)
            {
                LogOptions($"Artwork search discarded because panel changed. Kind={kind}; Game={game.Title}; PanelOpen={IsOptionsPanelOpen}");
                _optionsBusy = false;
                return;
            }

            _optionsBusy = false;
            if (options.Count == 0)
            {
                ShowOptionsMessage("Nenhuma arte encontrada", "O SteamGridDB não retornou imagens disponíveis para esse jogo.");
                return;
            }

            ShowArtworkSelection(kind, options);
        }
        catch (TimeoutException ex)
        {
            LogOptions($"Artwork search timeout. Kind={kind}; Game={game.Title}; Error={ex}");
            _optionsBusy = false;
            ShowOptionsMessage("Busca demorou demais", "O SteamGridDB demorou para responder. Tente novamente em alguns instantes.");
        }
        catch (Exception ex)
        {
            LogOptions($"Artwork search failed. Kind={kind}; Game={game.Title}; Error={ex}");
            _optionsBusy = false;
            ShowOptionsMessage("Busca de imagens falhou", ex.Message);
        }
    }

    private void ShowOptionsLoading(ArtworkKind kind)
    {
        LogOptions($"Loading UI shown. Kind={kind}; Game={_optionsGame?.Title ?? "none"}");
        OptionsPanelTitle.Text = kind switch
        {
            ArtworkKind.Cover => "Buscando banners",
            ArtworkKind.Hero => "Buscando fundos",
            ArtworkKind.Logo => "Buscando letreiros",
            ArtworkKind.Icon => "Buscando ícones",
            _ => "Buscando imagens"
        };
        OptionsPanelSubtitle.Text = "Aguarde...";
        OptionsPanelContent.Children.Clear();
        _optionsButtons.Clear();

        OptionsPanelContent.Children.Add(new TextBlock
        {
            Text = "Buscando imagens no SteamGridDB...",
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = 22,
            TextWrapping = TextWrapping.WrapWholeWords,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 34, 0, 10)
        });
        OptionsPanelContent.Children.Add(new TextBlock
        {
            Text = "Aguarde. A Ludryn vai mostrar os resultados nesta lateral.",
            Foreground = new SolidColorBrush(Color.FromArgb(185, 255, 255, 255)),
            FontSize = 16,
            TextWrapping = TextWrapping.WrapWholeWords,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        LogOptions($"Loading UI completed. Kind={kind}; Game={_optionsGame?.Title ?? "none"}");
    }

    private void ShowOptionsMessage(string title, string message)
    {
        LogOptions($"Message UI shown. Title={title}; Message={message}");
        OptionsPanelTitle.Text = title;
        OptionsPanelSubtitle.Text = "B Voltar";
        OptionsPanelContent.Children.Clear();
        _optionsButtons.Clear();
        OptionsPanelContent.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = 18,
            TextWrapping = TextWrapping.WrapWholeWords
        });
        AddOptionsButton("Voltar", ShowGameOptionsMenu);
        FocusOptionsButton(0);
    }

    private void ShowArtworkSelection(ArtworkKind kind, IReadOnlyList<SteamGridDbImageOption> options)
    {
        LogOptions($"Artwork selection UI shown. Kind={kind}; Count={options.Count}; Game={_optionsGame?.Title ?? "none"}");
        OptionsPanelTitle.Text = kind switch
        {
            ArtworkKind.Cover => "Escolha o banner",
            ArtworkKind.Hero => "Escolha o fundo",
            ArtworkKind.Logo => "Escolha o letreiro",
            ArtworkKind.Icon => "Escolha o ícone",
            _ => "Escolha a arte"
        };
        OptionsPanelSubtitle.Text = "A Selecionar   B Voltar";
        OptionsPanelContent.Children.Clear();
        _optionsButtons.Clear();
        _optionsColumns = 3;

        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        OptionsPanelContent.Children.Add(grid);

        for (var i = 0; i < options.Count; i++)
        {
            if (i % _optionsColumns == 0)
            {
                grid.RowDefinitions.Add(new RowDefinition());
            }

            var option = options[i];
            LogOptions($"Artwork option rendered. Kind={kind}; Index={i}; Size={option.Width}x{option.Height}; Url={option.Url}");
            var button = CreateArtworkButton(option, () => _ = ApplyArtworkOptionAsync(kind, option.Url));
            Grid.SetColumn(button, i % _optionsColumns);
            Grid.SetRow(button, i / _optionsColumns);
            grid.Children.Add(button);
            _optionsButtons.Add(button);
        }

        FocusOptionsButton(0);
    }

    private async Task ApplyArtworkOptionAsync(ArtworkKind kind, string imageUrl)
    {
        if (_optionsGame is null || _optionsBusy)
        {
            LogOptions($"Artwork apply ignored. Kind={kind}; HasGame={_optionsGame is not null}; Busy={_optionsBusy}; Url={imageUrl}");
            return;
        }

        _optionsBusy = true;
        ShowOptionsLoading(kind);
        LogOptions($"Artwork apply started. Kind={kind}; Game={_optionsGame.Title}; Url={imageUrl}");
        var applied = await _dataService.ApplyArtworkSelectionAsync(_optionsGame, kind, imageUrl, DispatcherQueue);
        LogOptions($"Artwork apply finished. Kind={kind}; Game={_optionsGame.Title}; Applied={applied}");
        _optionsBusy = false;
        if (applied)
        {
            _optionsRefreshAfterChange?.Invoke();
            ShowGameOptionsMenu();
        }
        else
        {
            ShowOptionsMessage("Não foi possível trocar a arte", "Tente novamente em alguns instantes.");
        }
    }

    private void AddOptionsButton(string text, Action action)
    {
        var button = new Button
        {
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 62,
            FontSize = 18,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
            Background = new SolidColorBrush(Color.FromArgb(215, 22, 18, 28)),
            Tag = action
        };
        button.Click += (_, _) => QueueOptionAction(action);
        OptionsPanelContent.Children.Add(button);
        _optionsButtons.Add(button);
    }

    private Button CreateArtworkButton(SteamGridDbImageOption option, Action action)
    {
        var (width, height) = GetOptionPreviewSize(option);
        var button = new Button
        {
            Width = 286,
            Height = 260,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
            Background = new SolidColorBrush(Color.FromArgb(215, 18, 18, 24)),
            Tag = action,
            Content = new StackPanel
            {
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new Border
                    {
                        Width = 258,
                        Height = 202,
                        Background = new SolidColorBrush(Color.FromArgb(255, 10, 10, 14)),
                        Child = new Image
                        {
                            Source = new BitmapImage(new Uri(option.DisplayUrl)),
                            Stretch = Stretch.Uniform,
                            Width = width,
                            Height = height,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    },
                    new TextBlock
                    {
                        Text = option.Width > 0 && option.Height > 0 ? $"{option.Width} x {option.Height}" : "SteamGridDB",
                        Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                        FontSize = 13,
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                }
            }
        };
        button.Click += (_, _) => QueueOptionAction(action);
        return button;
    }

    private void QueueOptionAction(Action action)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                LogOptions($"Queued option action failed. Error={ex}");
                _optionsBusy = false;
                ShowOptionsMessage("Ação falhou", ex.Message);
            }
        });
    }

    private void RunOptionTask(Func<Task> action)
    {
        _ = RunOptionTaskAsync(action);
    }

    private async Task RunOptionTaskAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            LogOptions($"Queued option task failed. Error={ex}");
            _optionsBusy = false;
            ShowOptionsMessage("Ação falhou", ex.Message);
        }
    }

    private static (double Width, double Height) GetOptionPreviewSize(SteamGridDbImageOption option)
    {
        const double maxWidth = 248;
        const double maxHeight = 190;
        var width = option.Width > 0 ? option.Width : 600;
        var height = option.Height > 0 ? option.Height : 900;
        var scale = Math.Min(maxWidth / width, maxHeight / height);
        return (Math.Max(56, width * scale), Math.Max(56, height * scale));
    }

    private bool CloseOptionsPanel()
    {
        if (!IsOptionsPanelOpen || _optionsBusy)
        {
            return IsOptionsPanelOpen;
        }

        OptionsSideLayer.Visibility = Visibility.Collapsed;
        OptionsPanelContent.Children.Clear();
        _optionsButtons.Clear();
        _optionsGame = null;
        _optionsRefreshAfterChange = null;
        _optionsRemoved = null;
        _optionsFocusedIndex = 0;
        _optionsColumns = 1;
        FocusCurrentPageInitialElement();
        return true;
    }

    private static void LogOptions(string message)
    {
        LudrynLogger.Log("options", message);
    }

    private void AcceptOptionsPanel()
    {
        if (_optionsBusy || _optionsButtons.Count == 0)
        {
            LogOptions($"Accept ignored. Busy={_optionsBusy}; ButtonCount={_optionsButtons.Count}; FocusedIndex={_optionsFocusedIndex}");
            return;
        }

        var index = Math.Clamp(_optionsFocusedIndex, 0, _optionsButtons.Count - 1);
        LogOptions($"Accept button. Index={index}; Content={_optionsButtons[index].Content}");
        if (_optionsButtons[index].Tag is Action action)
        {
            QueueOptionAction(action);
        }
    }

    private void MoveOptionsPanel(FocusNavigationDirection direction)
    {
        if (_optionsBusy || _optionsButtons.Count == 0)
        {
            return;
        }

        var nextIndex = direction switch
        {
            FocusNavigationDirection.Left => _optionsFocusedIndex % _optionsColumns == 0 ? _optionsFocusedIndex : _optionsFocusedIndex - 1,
            FocusNavigationDirection.Right => _optionsFocusedIndex % _optionsColumns == _optionsColumns - 1 ? _optionsFocusedIndex : Math.Min(_optionsButtons.Count - 1, _optionsFocusedIndex + 1),
            FocusNavigationDirection.Up => Math.Max(0, _optionsFocusedIndex - _optionsColumns),
            FocusNavigationDirection.Down => Math.Min(_optionsButtons.Count - 1, _optionsFocusedIndex + _optionsColumns),
            _ => _optionsFocusedIndex
        };

        FocusOptionsButton(nextIndex);
    }

    private void FocusOptionsButton(int index)
    {
        if (_optionsButtons.Count == 0)
        {
            return;
        }

        _optionsFocusedIndex = Math.Clamp(index, 0, _optionsButtons.Count - 1);
        for (var i = 0; i < _optionsButtons.Count; i++)
        {
            var selected = i == _optionsFocusedIndex;
            _optionsButtons[i].BorderThickness = selected ? new Thickness(2.5) : new Thickness(1);
            _optionsButtons[i].BorderBrush = new SolidColorBrush(selected ? Color.FromArgb(255, 229, 83, 255) : Color.FromArgb(90, 255, 255, 255));
            _optionsButtons[i].Background = new SolidColorBrush(selected ? Color.FromArgb(235, 56, 25, 70) : Color.FromArgb(215, 18, 18, 24));
        }

        _optionsButtons[_optionsFocusedIndex].Focus(FocusState.Programmatic);
        ScrollFocusedOptionIntoView(_optionsButtons[_optionsFocusedIndex]);
    }

    private void OpenProfilePanel()
    {
        CloseOptionsPanel();
        ProfileSideLayer.Visibility = Visibility.Visible;
        _profileCapturingPassword = false;
        _profilePasswordSequence.Clear();
        ShowProfileMenu();
    }

    private void ShowProfileMenu()
    {
        _profileButtons.Clear();
        ProfilePanelContent.Children.Clear();
        ProfilePanelTitle.Text = _dataService.IsSecondaryProfile ? "Perfil secundário" : "Perfil principal";
        ProfilePanelSubtitle.Text = _dataService.IsSecondaryProfile
            ? "Personalizações e jogos privados estão ativos."
            : "A Selecionar   B Voltar";
        ProfilePanelHint.Text = "View abre ou fecha este menu";

        AddProfileButton(
            _dataService.IsSecondaryProfile ? "Voltar ao perfil principal" : "Alternar perfil",
            BeginProfileSwitch);
        FocusProfileButton(0);
    }

    private void BeginProfileSwitch()
    {
        if (_dataService.IsSecondaryProfile)
        {
            CompleteProfileSwitch(LudrynProfile.Main);
            return;
        }

        if (_dataService.SecondaryProfileHasPassword)
        {
            BeginPasswordCapture(creating: false);
            return;
        }

        _profileButtons.Clear();
        ProfilePanelContent.Children.Clear();
        ProfilePanelTitle.Text = "Proteger perfil?";
        ProfilePanelSubtitle.Text = "Você pode criar uma senha com quatro botões do controle.";
        AddProfileButton("Definir senha", () => BeginPasswordCapture(creating: true));
        AddProfileButton("Entrar sem senha", () => CompleteProfileSwitch(LudrynProfile.Secondary));
        FocusProfileButton(0);
    }

    private void BeginPasswordCapture(bool creating)
    {
        _profileCreatingPassword = creating;
        _profileCapturingPassword = true;
        _profilePasswordSequence.Clear();
        _profileButtons.Clear();
        ProfilePanelContent.Children.Clear();
        ProfilePanelTitle.Text = creating ? "Criar sequência" : "Digite a sequência";
        ProfilePanelSubtitle.Text = "Pressione quatro botões. B também pode fazer parte da senha.";
        ProfilePanelHint.Text = "View cancela";
        UpdatePasswordSequenceVisual();
    }

    private void UpdatePasswordSequenceVisual()
    {
        ProfilePanelContent.Children.Clear();
        ProfilePanelContent.Children.Add(new TextBlock
        {
            Text = string.Join("   ", Enumerable.Range(0, 4)
                .Select(index => index < _profilePasswordSequence.Count ? "●" : "○")),
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            FontSize = 42,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 70, 0, 0)
        });
    }

    private void HandleProfilePanelInput(GamepadButtons buttons, GamepadReading reading)
    {
        if (WasPressed(buttons, GamepadButtons.View))
        {
            CloseProfilePanel();
            return;
        }

        if (_profileCapturingPassword)
        {
            var token = GetPressedPasswordToken(buttons);
            if (token is not null)
            {
                _profilePasswordSequence.Add(token);
                UpdatePasswordSequenceVisual();
                if (_profilePasswordSequence.Count == 4)
                {
                    FinishPasswordCapture();
                }
            }

            return;
        }

        if (WasPressed(buttons, GamepadButtons.A))
        {
            if (_profileButtons.Count > 0)
            {
                (_profileButtons[_profileFocusedIndex].Tag as Action)?.Invoke();
            }
        }
        else if (WasPressed(buttons, GamepadButtons.B))
        {
            CloseProfilePanel();
        }
        else
        {
            var direction = GetDirection(reading);
            if (direction is not null &&
                (DateTime.Now - _lastDirectionalMove).TotalMilliseconds >= 140)
            {
                if (direction == FocusNavigationDirection.Up)
                {
                    FocusProfileButton(Math.Max(0, _profileFocusedIndex - 1));
                }
                else if (direction == FocusNavigationDirection.Down)
                {
                    FocusProfileButton(Math.Min(_profileButtons.Count - 1, _profileFocusedIndex + 1));
                }

                _lastDirectionalMove = DateTime.Now;
            }
        }
    }

    private string? GetPressedPasswordToken(GamepadButtons buttons)
    {
        (GamepadButtons Button, string Token)[] allowed =
        [
            (GamepadButtons.A, "A"),
            (GamepadButtons.B, "B"),
            (GamepadButtons.X, "X"),
            (GamepadButtons.Y, "Y"),
            (GamepadButtons.DPadUp, "UP"),
            (GamepadButtons.DPadDown, "DOWN"),
            (GamepadButtons.DPadLeft, "LEFT"),
            (GamepadButtons.DPadRight, "RIGHT"),
            (GamepadButtons.LeftShoulder, "LB"),
            (GamepadButtons.RightShoulder, "RB")
        ];

        return allowed.FirstOrDefault(item => WasPressed(buttons, item.Button)).Token;
    }

    private void FinishPasswordCapture()
    {
        _profileCapturingPassword = false;
        if (_profileCreatingPassword)
        {
            _dataService.SetSecondaryProfilePassword(_profilePasswordSequence);
            CompleteProfileSwitch(LudrynProfile.Secondary);
            return;
        }

        if (_dataService.VerifySecondaryProfilePassword(_profilePasswordSequence))
        {
            CompleteProfileSwitch(LudrynProfile.Secondary);
            return;
        }

        _profileButtons.Clear();
        ProfilePanelContent.Children.Clear();
        ProfilePanelTitle.Text = "Sequência incorreta";
        ProfilePanelSubtitle.Text = "A sequência não corresponde à senha do perfil.";
        AddProfileButton("Tentar novamente", () => BeginPasswordCapture(creating: false));
        AddProfileButton("Voltar", ShowProfileMenu);
        FocusProfileButton(0);
    }

    private void CompleteProfileSwitch(LudrynProfile profile)
    {
        _dataService.SwitchProfile(profile);
        CloseProfilePanel();
        ReloadSelectedTab();
    }

    private void ReloadSelectedTab()
    {
        ContentFrame.Content = null;
        NavigateToTab(_selectedTabIndex);
    }

    private void AddProfileButton(string text, Action action)
    {
        var button = new Button
        {
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 68,
            FontSize = 19,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
            Background = new SolidColorBrush(Color.FromArgb(215, 22, 18, 28)),
            Tag = action
        };
        button.Click += (_, _) => action();
        ProfilePanelContent.Children.Add(button);
        _profileButtons.Add(button);
    }

    private void FocusProfileButton(int index)
    {
        if (_profileButtons.Count == 0)
        {
            return;
        }

        _profileFocusedIndex = Math.Clamp(index, 0, _profileButtons.Count - 1);
        for (var i = 0; i < _profileButtons.Count; i++)
        {
            var selected = i == _profileFocusedIndex;
            _profileButtons[i].BorderBrush = new SolidColorBrush(selected
                ? Color.FromArgb(255, 214, 76, 239)
                : Color.FromArgb(90, 255, 255, 255));
            _profileButtons[i].Background = new SolidColorBrush(selected
                ? Color.FromArgb(220, 78, 31, 92)
                : Color.FromArgb(215, 22, 18, 28));
        }

        _profileButtons[_profileFocusedIndex].Focus(FocusState.Programmatic);
    }

    private bool CloseProfilePanel()
    {
        if (!IsProfilePanelOpen)
        {
            return false;
        }

        ProfileSideLayer.Visibility = Visibility.Collapsed;
        ProfilePanelContent.Children.Clear();
        _profileButtons.Clear();
        _profilePasswordSequence.Clear();
        _profileCapturingPassword = false;
        FocusCurrentPageInitialElement();
        return true;
    }

    private void ScrollFocusedOptionIntoView(FrameworkElement focusedElement)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                OptionsPanelScrollViewer.UpdateLayout();
                OptionsPanelContent.UpdateLayout();
                focusedElement.UpdateLayout();

                var bounds = focusedElement
                    .TransformToVisual(OptionsPanelContent)
                    .TransformBounds(new Windows.Foundation.Rect(0, 0, focusedElement.ActualWidth, focusedElement.ActualHeight));

                var currentOffset = OptionsPanelScrollViewer.VerticalOffset;
                var viewportHeight = OptionsPanelScrollViewer.ViewportHeight;
                var targetOffset = currentOffset;
                const double margin = 18;

                if (bounds.Top - margin < currentOffset)
                {
                    targetOffset = Math.Max(0, bounds.Top - margin);
                }
                else if (bounds.Bottom + margin > currentOffset + viewportHeight)
                {
                    targetOffset = Math.Max(0, bounds.Bottom + margin - viewportHeight);
                }

                if (Math.Abs(targetOffset - currentOffset) > 0.5)
                {
                    OptionsPanelScrollViewer.ChangeView(null, targetOffset, null, disableAnimation: false);
                    LogOptions($"Options scroll moved. FocusedIndex={_optionsFocusedIndex}; Offset={targetOffset:0.##}");
                }
            }
            catch (Exception ex)
            {
                LogOptions($"Options scroll failed. FocusedIndex={_optionsFocusedIndex}; Error={ex}");
            }
        });
    }

    private void UpdateTabVisuals()
    {
        for (var i = 0; i < _tabButtons.Length; i++)
        {
            var selected = i == _selectedTabIndex;
            _tabButtons[i].Foreground = new SolidColorBrush(selected ? Colors.White : Windows.UI.Color.FromArgb(153, 255, 255, 255));
            _tabButtons[i].BorderBrush = new SolidColorBrush(selected ? Windows.UI.Color.FromArgb(210, 191, 91, 255) : Colors.Transparent);
            _tabButtons[i].Background = new SolidColorBrush(selected ? Windows.UI.Color.FromArgb(105, 108, 45, 128) : Colors.Transparent);
        }
    }

    private void UpdateSelectedTabFromCurrentPage()
    {
        var index = ContentFrame.Content switch
        {
            HomePage => 0,
            LibraryPage => 1,
            EmulatorsPage => 2,
            EmulatorSettingsPage => 2,
            AddEmulatorSetupPage => 2,
            AddRomDirectorySetupPage => 2,
            SettingsPage => 3,
            _ => _selectedTabIndex
        };

        if (index != _selectedTabIndex)
        {
            _selectedTabIndex = index;
            UpdateTabVisuals();
        }
    }
}
