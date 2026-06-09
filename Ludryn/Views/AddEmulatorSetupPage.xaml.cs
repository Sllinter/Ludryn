using Ludryn.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Ludryn.Views;

public sealed partial class AddEmulatorSetupPage : Page, IGamepadFocusablePage, IGamepadHintProvider
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
    private int _focusedIndex;
    private int _platformIndex;
    private string? _executablePath;
    private bool _isPicking;

    public AddEmulatorSetupPage()
    {
        InitializeComponent();
        _buttons = [PlatformButton, PickExecutableButton, ConfirmButton];
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
        if (_isPicking)
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
        if (!_isPicking)
        {
            App.MainWindow()?.GoBack();
        }

        return true;
    }

    public bool HandleGamepadAccept(FrameworkElement focusedElement)
    {
        if (_isPicking)
        {
            return true;
        }

        if (_focusedIndex == 1)
        {
            _ = PickExecutableAsync();
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
        if (_isPicking)
        {
            return;
        }

        _platformIndex = (_platformIndex + (previous ? -1 : 1) + Platforms.Length) % Platforms.Length;
        UpdateText();
        FocusButton(0);
    }

    private void PickExecutable_Click(object sender, RoutedEventArgs e) => _ = PickExecutableAsync();
    private void Confirm_Click(object sender, RoutedEventArgs e) => Confirm();

    private async Task PickExecutableAsync()
    {
        if (_isPicking || App.MainWindow() is not MainWindow window)
        {
            return;
        }

        _isPicking = true;
        UpdateButtonVisuals();
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.ComputerFolder
        };
        picker.FileTypeFilter.Add(".exe");
        InitializeWithWindow.Initialize(picker, window.WindowHandle);

        try
        {
            var file = await picker.PickSingleFileAsync();
            if (file is not null)
            {
                _executablePath = file.Path;
                UpdateText();
            }
        }
        finally
        {
            _isPicking = false;
            FocusButton(2);
        }
    }

    private void Confirm()
    {
        if (_dataService is null || string.IsNullOrWhiteSpace(_executablePath))
        {
            FocusButton(1);
            return;
        }

        _dataService.AddEmulator(Path.GetFileNameWithoutExtension(_executablePath), _executablePath, CurrentPlatform);
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
            var disabledConfirm = i == 2 && string.IsNullOrWhiteSpace(_executablePath);
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
        ExecutableText.Text = string.IsNullOrWhiteSpace(_executablePath)
            ? "Nenhum arquivo selecionado"
            : _executablePath;
        ConfirmHintText.Text = string.IsNullOrWhiteSpace(_executablePath)
            ? "Selecione um executavel para liberar a confirmacao."
            : $"Adicionar como emulador de {CurrentPlatform}.";
        UpdateButtonVisuals();
    }
}
