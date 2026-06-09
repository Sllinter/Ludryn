using Ludryn.Models;
using Ludryn.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Ludryn.Views;

public sealed partial class AddRomDirectorySetupPage : Page, IGamepadFocusablePage, IGamepadHintProvider
{
    private static readonly string[] Platforms =
    [
        "Nintendo Switch",
        "Wii",
        "Wii U",
        "GameCube",
        "PlayStation 2",
        "PlayStation 1",
        "Nintendo 64",
        "PlayStation 3",
        "PSP",
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
        "Arcade"
    ];

    private MockDataService? _dataService;
    private Button[] _buttons = [];
    private List<Game> _previewGames = [];
    private int _focusedIndex;
    private int _platformIndex;
    private string? _folderPath;
    private bool _isPicking;
    private bool _isLoading;

    public AddRomDirectorySetupPage()
    {
        InitializeComponent();
        _buttons = [PlatformButton, PickFolderButton, ConfirmButton];
        Loaded += (_, _) => FocusInitialElement();
    }

    public GamepadHints GetGamepadHints() => new("Selecionar / confirmar", Back: "Voltar");

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _dataService = e.Parameter as MockDataService;
        UpdateText();
    }

    public void FocusInitialElement() => FocusButton(_focusedIndex);

    public bool HandleGamepadMove(FocusNavigationDirection direction)
    {
        if (_isPicking || _isLoading)
        {
            return true;
        }

        switch (direction)
        {
            case FocusNavigationDirection.Up:
                FocusButton(Math.Max(0, _focusedIndex - 1));
                return true;
            case FocusNavigationDirection.Down:
                FocusButton(Math.Min(_buttons.Length - 1, _focusedIndex + 1));
                return true;
            case FocusNavigationDirection.Left:
                MovePlatform(previous: true);
                return true;
            case FocusNavigationDirection.Right:
                MovePlatform(previous: false);
                return true;
            default:
                return false;
        }
    }

    public bool HandleGamepadBack()
    {
        if (!_isPicking && !_isLoading)
        {
            App.MainWindow()?.GoBack();
        }

        return true;
    }

    public bool HandleGamepadAccept(FrameworkElement focusedElement)
    {
        if (_isPicking || _isLoading)
        {
            return true;
        }

        if (_focusedIndex == 1)
        {
            _ = PickFolderAsync();
        }
        else if (_focusedIndex == 2)
        {
            Confirm();
        }

        return true;
    }

    public bool HandleGamepadX() => false;
    public bool HandleGamepadY() => false;
    public bool HandleGamepadOptions() => false;

    public void MovePlatform(bool previous)
    {
        if (_isPicking || _isLoading)
        {
            return;
        }

        _platformIndex = (_platformIndex + (previous ? -1 : 1) + Platforms.Length) % Platforms.Length;
        _previewGames.Clear();
        PreviewGamesList.ItemsSource = null;
        ResultsPanel.Visibility = Visibility.Collapsed;
        UpdateText();
        FocusButton(0);
    }

    private void PickFolder_Click(object sender, RoutedEventArgs e) => _ = PickFolderAsync();
    private void Confirm_Click(object sender, RoutedEventArgs e) => Confirm();

    private async Task PickFolderAsync()
    {
        if (_isPicking || _isLoading || _dataService is null || App.MainWindow() is not MainWindow window)
        {
            return;
        }

        _isPicking = true;
        UpdateButtonVisuals();
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder
        };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, window.WindowHandle);

        try
        {
            var folder = await picker.PickSingleFolderAsync();
            if (folder is null)
            {
                return;
            }

            _folderPath = folder.Path;
            UpdateText();
            await LoadPreviewAsync();
        }
        finally
        {
            _isPicking = false;
            FocusButton(2);
        }
    }

    private async Task LoadPreviewAsync()
    {
        if (_dataService is null || string.IsNullOrWhiteSpace(_folderPath))
        {
            return;
        }

        _isLoading = true;
        LoadingPanel.Visibility = Visibility.Visible;
        ResultsPanel.Visibility = Visibility.Collapsed;
        UpdateButtonVisuals();

        try
        {
            var path = _folderPath;
            var platform = CurrentPlatform;
            _previewGames = await Task.Run(() => _dataService.PreviewRomDirectory(path, platform).ToList());
            PreviewGamesList.ItemsSource = _previewGames.Take(10).ToList();
            ResultsTitleText.Text = _previewGames.Count == 1 ? "1 jogo encontrado" : $"{_previewGames.Count} jogos encontrados";
            ResultsHintText.Text = _previewGames.Count == 0
                ? "Nenhuma ROM reconhecida nessa pasta para os formatos suportados."
                : _previewGames.Count > 10
                    ? "Mostrando os 10 primeiros jogos encontrados."
                    : "Confira os jogos antes de confirmar.";
            ResultsPanel.Visibility = Visibility.Visible;
        }
        finally
        {
            _isLoading = false;
            LoadingPanel.Visibility = Visibility.Collapsed;
            UpdateText();
        }
    }

    private void Confirm()
    {
        if (_dataService is null || string.IsNullOrWhiteSpace(_folderPath) || _previewGames.Count == 0)
        {
            FocusButton(1);
            return;
        }

        _dataService.AddRomDirectory(_folderPath, CurrentPlatform);
        App.MainWindow()?.GoBack();
    }

    private string CurrentPlatform => Platforms[Math.Clamp(_platformIndex, 0, Platforms.Length - 1)];

    private void FocusButton(int index)
    {
        _focusedIndex = Math.Clamp(index, 0, _buttons.Length - 1);
        _buttons[_focusedIndex].Focus(FocusState.Programmatic);
        UpdateButtonVisuals();
    }

    private void UpdateButtonVisuals()
    {
        for (var i = 0; i < _buttons.Length; i++)
        {
            var selected = i == _focusedIndex;
            var disabledConfirm = i == 2 && (string.IsNullOrWhiteSpace(_folderPath) || _previewGames.Count == 0);
            _buttons[i].Background = new SolidColorBrush(selected
                ? Windows.UI.Color.FromArgb(118, 105, 36, 128)
                : Windows.UI.Color.FromArgb(disabledConfirm ? (byte)52 : (byte)94, 9, 12, 17));
            _buttons[i].BorderBrush = new SolidColorBrush(selected
                ? Windows.UI.Color.FromArgb(230, 213, 74, 255)
                : Windows.UI.Color.FromArgb(disabledConfirm ? (byte)34 : (byte)54, 255, 255, 255));
            _buttons[i].BorderThickness = selected ? new Thickness(2) : new Thickness(1);
        }
    }

    private void UpdateText()
    {
        PlatformText.Text = CurrentPlatform;
        FolderText.Text = string.IsNullOrWhiteSpace(_folderPath)
            ? "Nenhuma pasta selecionada"
            : _folderPath;

        ConfirmHintText.Text = string.IsNullOrWhiteSpace(_folderPath)
            ? "Selecione uma pasta para carregar os jogos."
            : _previewGames.Count == 0
                ? "A confirmacao sera liberada quando houver ROMs reconhecidas."
                : $"Adicionar {_previewGames.Count} jogo(s) de {CurrentPlatform}.";
        UpdateButtonVisuals();
    }
}
