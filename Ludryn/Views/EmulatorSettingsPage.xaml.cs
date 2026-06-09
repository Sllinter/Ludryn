using Ludryn.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace Ludryn.Views;

public sealed partial class EmulatorSettingsPage : Page, IGamepadFocusablePage, IGamepadHintProvider
{
    private MockDataService? _dataService;
    private readonly List<Button> _buttons = [];
    private int _focusedIndex;

    public EmulatorSettingsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => FocusInitialElement();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is MockDataService dataService)
        {
            _dataService = dataService;
        }

        RebuildControls();
    }

    public void FocusInitialElement() => FocusButton(_focusedIndex);

    public bool HandleGamepadMove(FocusNavigationDirection direction)
    {
        switch (direction)
        {
            case FocusNavigationDirection.Left:
            case FocusNavigationDirection.Up:
                FocusButton(Math.Max(0, _focusedIndex - 1));
                return true;
            case FocusNavigationDirection.Right:
            case FocusNavigationDirection.Down:
                FocusButton(Math.Min(_buttons.Count - 1, _focusedIndex + 1));
                return true;
            default:
                return false;
        }
    }

    public bool HandleGamepadBack()
    {
        App.MainWindow()?.GoBack();
        return true;
    }

    public bool HandleGamepadAccept(FrameworkElement focusedElement)
    {
        if (App.MainWindow() is not MainWindow window || _dataService is null)
        {
            return true;
        }

        if (_buttons.Count == 0)
        {
            return true;
        }

        if (_buttons[Math.Clamp(_focusedIndex, 0, _buttons.Count - 1)].Tag is Action action)
        {
            action();
        }

        return true;
    }

    public bool HandleGamepadX() => false;
    public bool HandleGamepadY() => false;
    public bool HandleGamepadOptions() => false;
    public GamepadHints GetGamepadHints() => new("Selecionar / remover", Back: "Voltar");

    private void AddEmulator_Click(object sender, RoutedEventArgs e)
    {
        _focusedIndex = 0;
        HandleGamepadAccept(AddEmulatorButton);
    }

    private void AddRomDirectory_Click(object sender, RoutedEventArgs e)
    {
        _focusedIndex = 1;
        HandleGamepadAccept(AddRomDirectoryButton);
    }

    private void RebuildControls()
    {
        _buttons.Clear();
        ConfiguredEmulatorsPanel.Children.Clear();
        ConfiguredRomDirectoriesPanel.Children.Clear();

        AddManagedButton(AddEmulatorButton, () => App.MainWindow()?.OpenAddEmulatorSetup());
        AddManagedButton(AddRomDirectoryButton, () => App.MainWindow()?.OpenAddRomDirectorySetup());

        if (_dataService is null)
        {
            return;
        }

        EmulatorCountText.Text = $"{_dataService.GetConfiguredEmulators().Count} emulador(es) configurado(s)";
        RomDirectoryCountText.Text = $"{_dataService.GetConfiguredRomDirectories().Count} pasta(s) de ROMs configurada(s)";

        foreach (var emulator in _dataService.GetConfiguredEmulators())
        {
            var button = CreateRemoveButton(
                emulator.Name,
                $"{emulator.Platform}  •  {emulator.ExecutablePath}",
                () =>
                {
                    _dataService.RemoveEmulator(emulator);
                    RebuildControls();
                });
            ConfiguredEmulatorsPanel.Children.Add(button);
            AddManagedButton(button, (Action)button.Tag);
        }

        if (_dataService.GetConfiguredEmulators().Count == 0)
        {
            ConfiguredEmulatorsPanel.Children.Add(CreateEmptyText("Nenhum emulador adicionado."));
        }

        foreach (var directory in _dataService.GetConfiguredRomDirectories())
        {
            var button = CreateRemoveButton(
                directory.Platform,
                directory.Path,
                () =>
                {
                    _dataService.RemoveRomDirectory(directory);
                    RebuildControls();
                });
            ConfiguredRomDirectoriesPanel.Children.Add(button);
            AddManagedButton(button, (Action)button.Tag);
        }

        if (_dataService.GetConfiguredRomDirectories().Count == 0)
        {
            ConfiguredRomDirectoriesPanel.Children.Add(CreateEmptyText("Nenhuma pasta de ROMs adicionada."));
        }

        FocusButton(Math.Clamp(_focusedIndex, 0, Math.Max(0, _buttons.Count - 1)));
    }

    private void AddManagedButton(Button button, Action action)
    {
        button.Tag = action;
        _buttons.Add(button);
    }

    private Button CreateRemoveButton(string title, string subtitle, Action removeAction)
    {
        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 82,
            Padding = new Thickness(18, 12, 18, 12),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(94, 9, 12, 17)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(54, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Tag = removeAction,
            Content = new Grid
            {
                ColumnSpacing = 14,
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = GridLength.Auto }
                },
                Children =
                {
                    new StackPanel
                    {
                        Spacing = 4,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = title,
                                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                                FontSize = 20,
                                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                                TextTrimming = TextTrimming.CharacterEllipsis
                            },
                            new TextBlock
                            {
                                Text = subtitle,
                                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(170, 255, 255, 255)),
                                FontSize = 14,
                                TextTrimming = TextTrimming.CharacterEllipsis
                            }
                        }
                    },
                    new TextBlock
                    {
                        Text = "Remover",
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 238, 102, 120)),
                        FontSize = 15,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            }
        };

        if (button.Content is Grid grid && grid.Children[1] is FrameworkElement removeText)
        {
            Grid.SetColumn(removeText, 1);
        }

        button.Click += (_, _) => removeAction();
        return button;
    }

    private static TextBlock CreateEmptyText(string text) =>
        new()
        {
            Text = text,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(160, 255, 255, 255)),
            FontSize = 16,
            Margin = new Thickness(0, 8, 0, 4)
        };

    private void FocusButton(int index)
    {
        if (_buttons.Count == 0)
        {
            return;
        }

        _focusedIndex = Math.Clamp(index, 0, _buttons.Count - 1);
        _buttons[_focusedIndex].Focus(FocusState.Programmatic);
        UpdateButtonVisuals();
    }

    private void UpdateButtonVisuals()
    {
        for (var i = 0; i < _buttons.Count; i++)
        {
            var selected = i == _focusedIndex;
            _buttons[i].Background = new SolidColorBrush(selected
                ? Windows.UI.Color.FromArgb(118, 105, 36, 128)
                : Windows.UI.Color.FromArgb(94, 9, 12, 17));
            _buttons[i].BorderBrush = new SolidColorBrush(selected
                ? Windows.UI.Color.FromArgb(230, 213, 74, 255)
                : Windows.UI.Color.FromArgb(54, 255, 255, 255));
            _buttons[i].BorderThickness = selected ? new Thickness(2) : new Thickness(1);
        }
    }

}
