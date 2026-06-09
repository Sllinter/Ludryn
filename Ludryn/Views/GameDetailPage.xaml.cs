using Ludryn.Models;
using Ludryn.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;

namespace Ludryn.Views;

public sealed partial class GameDetailPage : Page, IGamepadFocusablePage, IGamepadHintProvider
{
    private Game? _game;
    private MockDataService? _dataService;
    private readonly GameLaunchService _launchService = new();
    private readonly DispatcherTimer _launchMonitorTimer = new();
    private DateTime _launchStartedAt;
    private bool _launchPending;
    private bool _gameRunning;

    public GameDetailPage()
    {
        InitializeComponent();
        _launchMonitorTimer.Interval = TimeSpan.FromSeconds(2);
        _launchMonitorTimer.Tick += LaunchMonitorTimer_Tick;
        Loaded += (_, _) =>
        {
            PlayButton.Focus(FocusState.Programmatic);
            QueueCoverPositionUpdate();
        };
        SizeChanged += (_, _) => QueueCoverPositionUpdate();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var navigation = (GameDetailNavigation)e.Parameter;
        _game = navigation.Game;
        _dataService = navigation.DataService;
        _launchPending = false;
        _gameRunning = false;

        UpdateArtworkViews();
        CoverTitle.Text = _game.Title;
        PlatformText.Text = GetPlatformLabel(_game.Platform);
        DetailPlatformIcon.Source = PlatformIconService.GetIcon(_game.Platform);
        UpdateLaunchButtonState();
        QueueCoverPositionUpdate();
        _ = CheckRunningStateAsync();
        _ = LoadArtworkAsync();
    }

    private void QueueCoverPositionUpdate()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            DetailLayout.UpdateLayout();
            UpdateCoverPosition();
        });
    }

    private void UpdateCoverPosition()
    {
        if (PageRoot.ActualWidth <= 0 || PlayButton.ActualWidth <= 0 || CoverContainer.ActualWidth <= 0)
        {
            return;
        }

        var playButtonLeft = PlayButton.TransformToVisual(PageRoot).TransformPoint(new Point(0, 0)).X;
        var targetCenter = playButtonLeft / 2d;
        var targetLeft = targetCenter - (CoverContainer.ActualWidth / 2d);
        var layoutLeft = DetailLayout.Padding.Left;
        var maxLeft = Math.Max(layoutLeft, playButtonLeft - CoverContainer.ActualWidth - 48);
        var clampedLeft = Math.Clamp(targetLeft, 64, maxLeft);
        CoverContainer.Margin = new Thickness(clampedLeft - layoutLeft, 0, 0, 0);
    }

    private static string GetPlatformLabel(string platform) => platform switch
    {
        "PCSX2" => "PS2",
        "Yuzu" => "Nintendo Switch",
        "RPCS3" => "PS3",
        "Dolphin" => "Wii",
        "Cemu" => "Wii U",
        "RetroArch" => "Retro",
        "Steam" or "Epic" or "GOG" or "Ubisoft Connect" or "EA Play" or "Xbox" => "PC",
        _ => platform
    };

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_game is null || _launchPending || _gameRunning)
        {
            return;
        }

        try
        {
            if (!_launchService.Launch(_game))
            {
                PlayButtonText.Text = "Nao disponivel";
                return;
            }

            _dataService?.RecordGameLaunched(_game);
            _launchPending = true;
            _launchStartedAt = DateTime.Now;
            UpdateLaunchButtonState();
            _launchMonitorTimer.Start();
        }
        catch
        {
            PlayButtonText.Text = "Falha ao iniciar";
        }
    }

    private void LaunchMonitorTimer_Tick(object? sender, object e)
    {
        if (_game is null)
        {
            _launchMonitorTimer.Stop();
            return;
        }

        var isRunning = _launchService.IsRunning(_game);
        if (isRunning)
        {
            _launchPending = false;
            _gameRunning = true;
            UpdateLaunchButtonState();
            return;
        }

        if (_gameRunning)
        {
            _gameRunning = false;
            _launchMonitorTimer.Stop();
            UpdateLaunchButtonState();
            return;
        }

        if (_launchPending && (DateTime.Now - _launchStartedAt).TotalSeconds > 45)
        {
            _launchPending = false;
            _launchMonitorTimer.Stop();
            UpdateLaunchButtonState();
        }
    }

    private async Task CheckRunningStateAsync()
    {
        if (_game is null)
        {
            return;
        }

        var game = _game;
        var isRunning = await Task.Run(() => _launchService.IsRunning(game));
        if (_game != game)
        {
            return;
        }

        _gameRunning = isRunning;
        UpdateLaunchButtonState();
        if (_gameRunning)
        {
            _launchMonitorTimer.Start();
        }
    }

    private void UpdateLaunchButtonState()
    {
        PlayButtonText.Text = (_launchPending, _gameRunning) switch
        {
            (true, _) => "Iniciando jogo",
            (_, true) => "Jogo aberto",
            _ => "Iniciar jogo"
        };
        App.MainWindow()?.RefreshGamepadHints();
    }

    private void UpdateArtworkViews()
    {
        if (_game is null)
        {
            return;
        }

        HeroArt.Source = _game.HeroArt;
        CoverArt.Source = _game.CoverArt;
        CoverTitle.Visibility = _game.HasRealCoverArt ? Visibility.Collapsed : Visibility.Visible;

        if (_game.LogoArt is not null)
        {
            TitleLogo.Source = _game.LogoArt;
            TitleLogo.Visibility = Visibility.Visible;
            TitleText.Visibility = Visibility.Collapsed;
        }
        else
        {
            TitleLogo.Source = null;
            TitleLogo.Visibility = Visibility.Collapsed;
            TitleText.Visibility = Visibility.Visible;
            TitleText.Text = _game.Title;
        }
    }

    public void FocusInitialElement()
    {
        PlayButton.Focus(FocusState.Programmatic);
    }

    public bool HandleGamepadMove(FocusNavigationDirection direction)
    {
        PlayButton.Focus(FocusState.Programmatic);
        return true;
    }

    public bool HandleGamepadBack()
    {
        if (App.MainWindow() is MainWindow window)
        {
            window.GoBack();
            return true;
        }

        return false;
    }

    public bool HandleGamepadX()
    {
        _ = ShowGameOptionsAsync();
        return true;
    }

    public bool HandleGamepadY() => false;
    public GamepadHints GetGamepadHints() => new(
        (_launchPending, _gameRunning) switch
        {
            (true, _) => "Iniciando jogo",
            (_, true) => "Jogo aberto",
            _ => "Iniciar jogo"
        },
        "Opcoes",
        null,
        "Voltar");
    public bool HandleGamepadOptions()
    {
        _ = ShowGameOptionsAsync();
        return true;
    }

    public bool HandleGamepadAccept(FrameworkElement focusedElement)
    {
        PlayButton_Click(this, new RoutedEventArgs());
        return true;
    }

    private async Task LoadArtworkAsync()
    {
        if (_dataService is null || _game is null)
        {
            return;
        }

        await _dataService.LoadArtworkAsync([_game], DispatcherQueue, updatedGame =>
        {
            if (_game == updatedGame)
            {
                UpdateArtworkViews();
            }
        });
    }

    private Task ShowGameOptionsAsync()
    {
        if (_dataService is null || _game is null || App.MainWindow() is not MainWindow window)
        {
            return Task.CompletedTask;
        }

        window.OpenGameOptionsPanel(
            _game,
            refreshAfterChange: () =>
            {
                UpdateArtworkViews();
            },
            removed: () =>
            {
                if (App.MainWindow() is MainWindow window)
                {
                    window.GoBack();
                }
            });
        return Task.CompletedTask;
    }
}
