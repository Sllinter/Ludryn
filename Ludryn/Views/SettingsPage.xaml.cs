using Ludryn.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace Ludryn.Views;

public sealed partial class SettingsPage : Page, IGamepadFocusablePage, IGamepadHintProvider
{
    private readonly Button[] _categoryButtons;
    private readonly List<Button> _actionButtons = [];
    private MockDataService? _dataService;
    private int _categoryIndex;
    private int _actionIndex;
    private bool _editingApiKey;
    private bool _isCategoryNavigation = true;

    public SettingsPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        _categoryButtons = [GeneralCategoryButton, LibraryCategoryButton, SteamGridDbCategoryButton, AboutCategoryButton];
        ShowCategory(0);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _dataService = e.Parameter as MockDataService;
        SteamGridDbApiKeyBox.Password = SteamGridDbService.TryReadApiKey(out _) ?? string.Empty;
        AppVersionText.Text = GetAppVersion();
        UpdateApiStatus();
        RefreshGameDirectories();
        _isCategoryNavigation = true;
        _actionIndex = 0;
        ShowCategory(0);
    }

    public void FocusInitialElement()
    {
        _isCategoryNavigation = true;
        FocusCategory(0);
    }

    public bool HandleGamepadMove(FocusNavigationDirection direction)
    {
        if (_editingApiKey)
        {
            return true;
        }

        if (_isCategoryNavigation)
        {
            if (direction == FocusNavigationDirection.Up)
            {
                FocusCategory(Math.Max(0, _categoryIndex - 1));
            }
            else if (direction == FocusNavigationDirection.Down)
            {
                FocusCategory(Math.Min(_categoryButtons.Length - 1, _categoryIndex + 1));
            }
            else if (direction == FocusNavigationDirection.Right)
            {
                EnterCategory();
            }

            return true;
        }

        if (_categoryIndex == 2)
        {
            if (direction == FocusNavigationDirection.Left)
            {
                if (_actionIndex == 0)
                {
                    ReturnToCategories();
                }
                else
                {
                    FocusAction(_actionIndex - 1);
                }
            }
            else if (direction == FocusNavigationDirection.Right)
            {
                FocusAction(Math.Min(_actionButtons.Count - 1, _actionIndex + 1));
            }
            return true;
        }

        if (direction == FocusNavigationDirection.Up)
        {
            FocusAction(Math.Max(0, _actionIndex - 1));
        }
        else if (direction == FocusNavigationDirection.Down)
        {
            FocusAction(Math.Min(_actionButtons.Count - 1, _actionIndex + 1));
        }
        else if (direction == FocusNavigationDirection.Left)
        {
            ReturnToCategories();
        }

        return true;
    }

    public bool HandleGamepadAccept(FrameworkElement focusedElement)
    {
        if (_editingApiKey)
        {
            return true;
        }

        if (_isCategoryNavigation)
        {
            EnterCategory();
            return true;
        }

        if (_actionButtons.Count == 0)
        {
            return true;
        }

        var button = _actionButtons[Math.Clamp(_actionIndex, 0, _actionButtons.Count - 1)];
        if (button == StartupPageButton)
        {
            StartupPage_Click(button, new RoutedEventArgs());
        }
        else if (button == ApiKeyEditButton)
        {
            ApiKeyEdit_Click(button, new RoutedEventArgs());
        }
        else if (button == RevealApiKeyButton)
        {
            RevealApiKey_Click(button, new RoutedEventArgs());
        }
        else if (button == PasteApiKeyButton)
        {
            PasteApiKey_Click(button, new RoutedEventArgs());
        }
        else if (button == CreateApiKeyButton)
        {
            CreateApiKey_Click(button, new RoutedEventArgs());
        }
        else if (button == AddGameDirectoryButton)
        {
            AddGameDirectory_Click(button, new RoutedEventArgs());
        }
        else if (button.Tag is string)
        {
            RemoveGameDirectory_Click(button, new RoutedEventArgs());
        }

        return true;
    }

    public bool HandleGamepadBack()
    {
        if (_editingApiKey)
        {
            FinishApiKeyEditing();
            return true;
        }

        if (!_isCategoryNavigation)
        {
            ReturnToCategories();
            return true;
        }

        return false;
    }

    public bool HandleGamepadX() => false;
    public bool HandleGamepadY() => false;
    public bool HandleGamepadOptions() => false;
    public GamepadHints GetGamepadHints() => new("Selecionar", Back: "Voltar");

    private void Category_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string value } && int.TryParse(value, out var index))
        {
            _isCategoryNavigation = true;
            ShowCategory(index);
            FocusCategory(index);
        }
    }

    private void ShowCategory(int index)
    {
        _categoryIndex = Math.Clamp(index, 0, _categoryButtons.Length - 1);
        GeneralPanel.Visibility = _categoryIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        LibraryPanel.Visibility = _categoryIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        SteamGridDbPanel.Visibility = _categoryIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        AboutPanel.Visibility = _categoryIndex == 3 ? Visibility.Visible : Visibility.Collapsed;

        _actionButtons.Clear();
        if (_categoryIndex == 0)
        {
            _actionButtons.Add(StartupPageButton);
            UpdateStartupPageText();
        }
        else if (_categoryIndex == 1)
        {
            _actionButtons.Add(AddGameDirectoryButton);
            _actionButtons.AddRange(GameDirectoriesPanel.Children.OfType<Button>());
        }
        else if (_categoryIndex == 2)
        {
            _actionButtons.AddRange([ApiKeyEditButton, RevealApiKeyButton, PasteApiKeyButton, CreateApiKeyButton]);
        }

        _actionIndex = Math.Clamp(_actionIndex, 0, Math.Max(0, _actionButtons.Count - 1));
        UpdateCategoryVisuals();
        UpdateActionVisuals();
    }

    private static string GetAppVersion()
    {
        try
        {
            var version = Windows.ApplicationModel.Package.Current.Id.Version;
            return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }
        catch
        {
            var version = typeof(App).Assembly.GetName().Version;
            return version is null
                ? "Desconhecida"
                : $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }
    }

    private void StartupPage_Click(object sender, RoutedEventArgs e)
    {
        AppSettingsService.StartupPageIndex = (AppSettingsService.StartupPageIndex + 1) % 4;
        UpdateStartupPageText();
    }

    private void UpdateStartupPageText()
    {
        StartupPageText.Text = AppSettingsService.StartupPageIndex switch
        {
            0 => "Home",
            1 => "Biblioteca",
            2 => "Emuladores",
            _ => "Configurações"
        };
    }

    private async void AddGameDirectory_Click(object sender, RoutedEventArgs e)
    {
        if (_dataService is null || App.MainWindow() is not MainWindow window)
        {
            return;
        }

        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, window.WindowHandle);
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null)
        {
            return;
        }

        _dataService.AddGameDirectory(folder.Path);
        RefreshGameDirectories();
        ShowCategory(1);
        FocusAction(0);
    }

    private void RefreshGameDirectories()
    {
        GameDirectoriesPanel.Children.Clear();
        var directories = _dataService?.GetConfiguredGameDirectories() ?? [];
        if (directories.Count == 0)
        {
            GameDirectoriesPanel.Children.Add(new TextBlock
            {
                Text = "Nenhuma pasta adicional configurada.",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(170, 255, 255, 255)),
                FontSize = 17,
                Margin = new Thickness(12)
            });
            return;
        }

        foreach (var path in directories)
        {
            var button = new Button
            {
                Height = 76,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Style = (Style)Application.Current.Resources["SettingsRowButtonStyle"],
                Content = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(),
                        new ColumnDefinition { Width = GridLength.Auto }
                    },
                    Children =
                    {
                        new TextBlock
                        {
                            Text = path,
                            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                            FontSize = 17,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            VerticalAlignment = VerticalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = "Remover",
                            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 235, 91, 110)),
                            FontSize = 16,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    }
                },
                Tag = path
            };
            Grid.SetColumn((FrameworkElement)((Grid)button.Content).Children[1], 1);
            button.Click += RemoveGameDirectory_Click;
            GameDirectoriesPanel.Children.Add(button);
        }
    }

    private void RemoveGameDirectory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string path } || _dataService is null)
        {
            return;
        }

        _dataService.RemoveGameDirectory(path);
        RefreshGameDirectories();
        ShowCategory(1);
        FocusAction(Math.Min(_actionIndex, _actionButtons.Count - 1));
    }

    private void ApiKeyEdit_Click(object sender, RoutedEventArgs e)
    {
        _editingApiKey = true;
        SteamGridDbApiKeyBox.IsHitTestVisible = true;
        SteamGridDbApiKeyBox.Focus(FocusState.Programmatic);
        SteamGridDbApiKeyBox.SelectAll();
        if (App.MainWindow() is MainWindow window)
        {
            WindowsTouchKeyboardService.Show(window.WindowHandle);
        }
    }

    private void SteamGridDbApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_editingApiKey)
        {
            SteamGridDbStatusText.Text = "A chave será salva ao concluir a edição.";
        }
    }

    private void SteamGridDbApiKeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_editingApiKey)
        {
            FinishApiKeyEditing();
        }
    }

    private void FinishApiKeyEditing()
    {
        _editingApiKey = false;
        SteamGridDbApiKeyBox.IsHitTestVisible = false;
        SaveApiKey();
        FocusAction(0);
    }

    private void RevealApiKey_Click(object sender, RoutedEventArgs e)
    {
        var reveal = SteamGridDbApiKeyBox.PasswordRevealMode == PasswordRevealMode.Hidden;
        SteamGridDbApiKeyBox.PasswordRevealMode = reveal ? PasswordRevealMode.Visible : PasswordRevealMode.Hidden;
        RevealApiKeyIcon.Glyph = reveal ? "\uE7B3" : "\uE890";
    }

    private async void PasteApiKey_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var content = Clipboard.GetContent();
            if (content.Contains(StandardDataFormats.Text))
            {
                SteamGridDbApiKeyBox.Password = (await content.GetTextAsync()).Trim();
                SaveApiKey();
            }
        }
        catch (Exception ex)
        {
            SteamGridDbStatusText.Text = $"Não foi possível colar: {ex.Message}";
        }
    }

    private async void CreateApiKey_Click(object sender, RoutedEventArgs e) =>
        await Launcher.LaunchUriAsync(new Uri(SteamGridDbService.ApiSettingsUrl));

    private void SaveApiKey()
    {
        var apiKey = SteamGridDbApiKeyBox.Password.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            SteamGridDbStatusText.Text = "Cole ou digite uma chave válida.";
            return;
        }

        _dataService?.SaveSteamGridDbApiKey(apiKey);
        UpdateApiStatus();
    }

    private void UpdateApiStatus()
    {
        SteamGridDbStatusText.Text = _dataService?.HasSteamGridDb == true
            ? "API Key conectada. As artes serão carregadas automaticamente."
            : "Nenhuma API Key conectada.";
    }

    private void FocusCategory(int index)
    {
        _isCategoryNavigation = true;
        ShowCategory(index);
        _categoryButtons[_categoryIndex].Focus(FocusState.Programmatic);
        UpdateCategoryVisuals();
        UpdateActionVisuals();
    }

    private void FocusAction(int index)
    {
        if (_actionButtons.Count == 0)
        {
            return;
        }

        _isCategoryNavigation = false;
        _actionIndex = Math.Clamp(index, 0, _actionButtons.Count - 1);
        _actionButtons[_actionIndex].Focus(FocusState.Programmatic);
        _actionButtons[_actionIndex].StartBringIntoView();
        UpdateCategoryVisuals();
        UpdateActionVisuals();
    }

    private void EnterCategory()
    {
        if (_actionButtons.Count == 0)
        {
            return;
        }

        FocusAction(0);
    }

    private void ReturnToCategories()
    {
        _isCategoryNavigation = true;
        _actionIndex = 0;
        _categoryButtons[_categoryIndex].Focus(FocusState.Programmatic);
        UpdateCategoryVisuals();
        UpdateActionVisuals();
    }

    private void UpdateCategoryVisuals()
    {
        for (var i = 0; i < _categoryButtons.Length; i++)
        {
            var selected = i == _categoryIndex;
            var active = selected && _isCategoryNavigation;
            _categoryButtons[i].Foreground = new SolidColorBrush(selected
                ? Microsoft.UI.Colors.White
                : Windows.UI.Color.FromArgb(170, 255, 255, 255));
            _categoryButtons[i].Background = new SolidColorBrush(selected
                ? Windows.UI.Color.FromArgb(active ? (byte)132 : (byte)72, 108, 45, 128)
                : Microsoft.UI.Colors.Transparent);
            _categoryButtons[i].BorderBrush = new SolidColorBrush(selected
                ? Windows.UI.Color.FromArgb(active ? (byte)255 : (byte)150, 204, 82, 235)
                : Microsoft.UI.Colors.Transparent);
            _categoryButtons[i].BorderThickness = active ? new Thickness(2) : new Thickness(1);
        }
    }

    private void UpdateActionVisuals()
    {
        for (var i = 0; i < _actionButtons.Count; i++)
        {
            var selected = !_isCategoryNavigation && i == _actionIndex;
            _actionButtons[i].Background = new SolidColorBrush(selected
                ? Windows.UI.Color.FromArgb(132, 108, 45, 128)
                : Microsoft.UI.Colors.Transparent);
            _actionButtons[i].BorderBrush = new SolidColorBrush(selected
                ? Windows.UI.Color.FromArgb(255, 204, 82, 235)
                : Windows.UI.Color.FromArgb(34, 255, 255, 255));
            _actionButtons[i].BorderThickness = selected
                ? new Thickness(2)
                : new Thickness(0, 0, 0, 1);
        }
    }
}
