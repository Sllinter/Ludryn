using Ludryn.Models;
using Ludryn.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;

namespace Ludryn.Views;

public static class GameOptionsDialog
{
    private static int s_openDialogCount;
    private static ContentDialog? s_activeDialog;
    private static bool s_activeDialogCanClose = true;
    private static readonly List<Button> s_modalButtons = [];
    private static int s_focusedButtonIndex;
    private static int s_buttonColumns = 1;

    public static bool IsOpen => s_openDialogCount > 0;

    public static bool CloseActiveDialog()
    {
        if (s_activeDialog is null)
        {
            return false;
        }

        if (!s_activeDialogCanClose)
        {
            return true;
        }

        TryHideDialog(s_activeDialog);
        return true;
    }

    public static bool HandleMove(FocusNavigationDirection direction)
    {
        if (s_modalButtons.Count == 0)
        {
            return false;
        }

        var nextIndex = direction switch
        {
            FocusNavigationDirection.Left => s_focusedButtonIndex % s_buttonColumns == 0 ? s_focusedButtonIndex : s_focusedButtonIndex - 1,
            FocusNavigationDirection.Right => s_focusedButtonIndex % s_buttonColumns == s_buttonColumns - 1 ? s_focusedButtonIndex : s_focusedButtonIndex + 1,
            FocusNavigationDirection.Up => Math.Max(0, s_focusedButtonIndex - s_buttonColumns),
            FocusNavigationDirection.Down => Math.Min(s_modalButtons.Count - 1, s_focusedButtonIndex + s_buttonColumns),
            _ => s_focusedButtonIndex
        };

        FocusButton(nextIndex);
        return true;
    }

    public static bool HandleAccept()
    {
        if (s_modalButtons.Count == 0)
        {
            return false;
        }

        var index = Math.Clamp(s_focusedButtonIndex, 0, s_modalButtons.Count - 1);
        if (s_modalButtons[index].Tag is Action action)
        {
            action();
            return true;
        }

        return false;
    }

    public static async Task ShowAsync(
        XamlRoot xamlRoot,
        MockDataService dataService,
        Game game,
        Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue,
        Action? refreshAfterChange = null,
        Action? removed = null)
    {
        var action = await ShowMainMenuAsync(xamlRoot, game);
        switch (action)
        {
            case GameOptionAction.ToggleFavorite:
                dataService.ToggleFavorite(game);
                refreshAfterChange?.Invoke();
                break;
            case GameOptionAction.SelectLauncher:
                await ShowLauncherPickerAsync(xamlRoot, dataService, game, refreshAfterChange);
                break;
            case GameOptionAction.ChangeCover:
                await ShowArtworkPickerAsync(xamlRoot, dataService, game, ArtworkKind.Cover, dispatcherQueue, refreshAfterChange);
                break;
            case GameOptionAction.ChangeHero:
                await ShowArtworkPickerAsync(xamlRoot, dataService, game, ArtworkKind.Hero, dispatcherQueue, refreshAfterChange);
                break;
            case GameOptionAction.ChangeLogo:
                await ShowArtworkPickerAsync(xamlRoot, dataService, game, ArtworkKind.Logo, dispatcherQueue, refreshAfterChange);
                break;
            case GameOptionAction.RemoveFromLibrary:
                dataService.RemoveGame(game);
                removed?.Invoke();
                break;
        }
    }

    private static async Task<GameOptionAction> ShowMainMenuAsync(XamlRoot xamlRoot, Game game)
    {
        var selectedAction = GameOptionAction.None;
        var favoriteText = game.IsFavorite ? "Remover dos favoritos" : "Adicionar aos favoritos";
        var panel = new StackPanel { Spacing = 12, Width = 420 };
        panel.Children.Add(CreateMenuButton(favoriteText, () => selectedAction = GameOptionAction.ToggleFavorite));
        if (game.HasMultipleLaunchers)
        {
            panel.Children.Add(CreateMenuButton($"Launcher: {game.SelectedLauncher}", () => selectedAction = GameOptionAction.SelectLauncher));
        }

        panel.Children.Add(CreateMenuButton("Trocar banner", () => selectedAction = GameOptionAction.ChangeCover));
        panel.Children.Add(CreateMenuButton("Trocar imagem de fundo", () => selectedAction = GameOptionAction.ChangeHero));
        panel.Children.Add(CreateMenuButton("Trocar letreiro", () => selectedAction = GameOptionAction.ChangeLogo));
        panel.Children.Add(CreateMenuButton("Remover da biblioteca", () => selectedAction = GameOptionAction.RemoveFromLibrary));

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = game.Title,
            Content = panel,
            CloseButtonText = "Voltar",
            DefaultButton = ContentDialogButton.Close
        };

        foreach (var button in panel.Children.OfType<Button>())
        {
            var selected = (Action)button.Tag;
            Action selectAndClose = () =>
            {
                selected();
                dialog.Hide();
            };
            button.Tag = selectAndClose;
            button.Click += (_, _) => selectAndClose();
        }

        var buttons = panel.Children.OfType<Button>().ToList();
        dialog.Opened += (_, _) => SetModalButtons(buttons, 1);
        await ShowTrackedAsync(dialog);
        return selectedAction;
    }

    private static async Task ShowLauncherPickerAsync(XamlRoot xamlRoot, MockDataService dataService, Game game, Action? refreshAfterChange)
    {
        string? selectedLauncher = null;
        var panel = new StackPanel { Spacing = 12, Width = 420 };
        foreach (var launcher in game.Installations.Select(i => i.Launcher).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var label = string.Equals(launcher, game.SelectedLauncher, StringComparison.OrdinalIgnoreCase)
                ? $"{launcher}  -  selecionado"
                : launcher;
            panel.Children.Add(CreateMenuButton(label, () => selectedLauncher = launcher));
        }

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "Escolher launcher",
            Content = panel,
            CloseButtonText = "Voltar",
            DefaultButton = ContentDialogButton.Close
        };

        foreach (var button in panel.Children.OfType<Button>())
        {
            var selected = (Action)button.Tag;
            Action selectAndClose = () =>
            {
                selected();
                dialog.Hide();
            };
            button.Tag = selectAndClose;
            button.Click += (_, _) => selectAndClose();
        }

        var buttons = panel.Children.OfType<Button>().ToList();
        dialog.Opened += (_, _) => SetModalButtons(buttons, 1);
        await ShowTrackedAsync(dialog);

        if (!string.IsNullOrWhiteSpace(selectedLauncher))
        {
            dataService.SelectLauncher(game, selectedLauncher);
            refreshAfterChange?.Invoke();
        }
    }

    private static Button CreateMenuButton(string text, Action selected)
    {
        var button = new Button
        {
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 58,
            FontSize = 18,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(92, 255, 255, 255)),
            Background = new SolidColorBrush(Color.FromArgb(205, 22, 18, 28))
        };
        button.Tag = selected;
        return button;
    }

    private static async Task ShowArtworkPickerAsync(
        XamlRoot xamlRoot,
        MockDataService dataService,
        Game game,
        ArtworkKind kind,
        Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue,
        Action? refreshAfterChange)
    {
        if (!dataService.HasSteamGridDb)
        {
            await ShowMessageAsync(xamlRoot, "SteamGridDB desconectado", "Conecte uma API Key nas Configuracoes para buscar artes.");
            return;
        }

        await Task.Delay(120);

        IReadOnlyList<SteamGridDbImageOption> options;
        try
        {
            options = await ShowArtworkLoadingAsync(
                xamlRoot,
                GetLoadingText(kind),
                () => dataService.GetArtworkOptionsAsync(game, kind));
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(xamlRoot, "Busca de imagens falhou", ex.Message);
            return;
        }

        if (options.Count == 0)
        {
            await ShowMessageAsync(xamlRoot, "Nenhuma arte encontrada", "O SteamGridDB nao retornou imagens para esse jogo.");
            return;
        }

        var selectedUrl = await ShowImageSelectionAsync(xamlRoot, options, GetArtworkPickerTitle(kind));
        if (string.IsNullOrWhiteSpace(selectedUrl))
        {
            return;
        }

        var applied = await dataService.ApplyArtworkSelectionAsync(game, kind, selectedUrl, dispatcherQueue);
        if (applied)
        {
            refreshAfterChange?.Invoke();
        }
        else
        {
            await ShowMessageAsync(xamlRoot, "Nao foi possivel trocar a arte", "Tente novamente em alguns instantes.");
        }
    }

    private static string GetArtworkPickerTitle(ArtworkKind kind) => kind switch
    {
        ArtworkKind.Cover => "Escolha o banner",
        ArtworkKind.Hero => "Escolha a imagem de fundo",
        ArtworkKind.Logo => "Escolha o letreiro",
        _ => "Escolha a arte"
    };

    private static string GetLoadingText(ArtworkKind kind) => kind switch
    {
        ArtworkKind.Cover => "Buscando banners",
        ArtworkKind.Hero => "Buscando imagens de fundo",
        ArtworkKind.Logo => "Buscando letreiros",
        _ => "Buscando imagens"
    };

    private static async Task<IReadOnlyList<SteamGridDbImageOption>> ShowArtworkLoadingAsync(
        XamlRoot xamlRoot,
        string loadingText,
        Func<Task<IReadOnlyList<SteamGridDbImageOption>>> load)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = loadingText,
            Content = CreateLoadingContent(loadingText),
            CloseButtonText = string.Empty,
            DefaultButton = ContentDialogButton.None
        };
        dialog.Resources["ContentDialogMinWidth"] = 520d;
        dialog.Resources["ContentDialogMaxWidth"] = 560d;

        var dialogTask = ShowTrackedAsync(dialog, canCloseWithBack: false);
        await Task.Delay(300);

        try
        {
            return await load();
        }
        finally
        {
            TryHideDialog(dialog);
            await dialogTask;
        }
    }

    private static StackPanel CreateLoadingContent(string text) =>
        new()
        {
            Width = 420,
            Spacing = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                new ProgressRing
                {
                    IsActive = true,
                    Width = 54,
                    Height = 54,
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                new TextBlock
                {
                    Text = "Buscando opções disponíveis",
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
                    FontSize = 24,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                new TextBlock
                {
                    Text = "Aguarde enquanto a Ludryn consulta o SteamGridDB.",
                    Foreground = new SolidColorBrush(Color.FromArgb(185, 255, 255, 255)),
                    FontSize = 15,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.WrapWholeWords
                }
            }
        };

    private static async Task<string?> ShowImageSelectionAsync(XamlRoot xamlRoot, IReadOnlyList<SteamGridDbImageOption> options, string title)
    {
        string? selectedUrl = null;
        var content = new StackPanel
        {
            Width = 1040,
            Spacing = 18
        };
        content.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
            FontSize = 28,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        content.Children.Add(new TextBlock
        {
            Text = "Use o direcional para escolher uma imagem. A seleciona, B volta.",
            Foreground = new SolidColorBrush(Color.FromArgb(185, 255, 255, 255)),
            FontSize = 16
        });

        var grid = new Grid
        {
            Width = 990,
            Height = 620,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        content.Children.Add(grid);

        for (var i = 0; i < 3; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
        }

        for (var i = 0; i < 2; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition());
        }

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Content = content,
            CloseButtonText = "Voltar",
            DefaultButton = ContentDialogButton.Close,
            FullSizeDesired = true
        };
        dialog.Resources["ContentDialogMaxWidth"] = 1120d;
        dialog.Resources["ContentDialogMaxHeight"] = 900d;
        dialog.Resources["ContentDialogMinWidth"] = 1120d;

        var buttons = new List<Button>();
        for (var i = 0; i < options.Count; i++)
        {
            var option = options[i];
            var (previewWidth, previewHeight) = GetPreviewSize(option);
            var imageFrame = new Border
            {
                Width = 280,
                Height = 230,
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromArgb(255, 14, 14, 18)),
                Child = new Image
                {
                    Source = new BitmapImage(new Uri(option.DisplayUrl)),
                    Stretch = Stretch.Uniform,
                    Width = previewWidth,
                    Height = previewHeight,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            var button = new Button
            {
                Padding = new Thickness(0),
                Margin = new Thickness(12),
                Width = 305,
                Height = 285,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromArgb(218, 18, 18, 24)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Content = new StackPanel
                {
                    Spacing = 10,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        imageFrame,
                        new TextBlock
                        {
                            Text = option.Width > 0 && option.Height > 0 ? $"{option.Width} x {option.Height}" : "Imagem SteamGridDB",
                            Foreground = new SolidColorBrush(Color.FromArgb(185, 255, 255, 255)),
                            FontSize = 14,
                            HorizontalAlignment = HorizontalAlignment.Center,
                        }
                    }
                }
            };

            Action selectImage = () =>
            {
                selectedUrl = option.Url;
                dialog.Hide();
            };
            button.Tag = selectImage;
            button.Click += (_, _) => selectImage();

            Grid.SetColumn(button, i % 3);
            Grid.SetRow(button, i / 3);
            grid.Children.Add(button);
            buttons.Add(button);
        }

        dialog.Opened += (_, _) => SetModalButtons(buttons, 3);
        await ShowTrackedAsync(dialog);
        return selectedUrl;
    }

    private static async Task ShowMessageAsync(XamlRoot xamlRoot, string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = "OK"
        };

        await ShowTrackedAsync(dialog);
    }

    private static async Task ShowTrackedAsync(ContentDialog dialog, bool canCloseWithBack = true)
    {
        s_openDialogCount++;
        s_activeDialog = dialog;
        s_activeDialogCanClose = canCloseWithBack;
        try
        {
            await dialog.ShowAsync();
        }
        catch (ArgumentException)
        {
            // WinUI can throw if a gamepad back action races a dialog transition.
        }
        finally
        {
            s_openDialogCount = Math.Max(0, s_openDialogCount - 1);
            if (s_activeDialog == dialog)
            {
                s_activeDialog = null;
                s_activeDialogCanClose = true;
            }

            ClearModalButtons();
        }
    }

    private static void TryHideDialog(ContentDialog dialog)
    {
        try
        {
            dialog.Hide();
        }
        catch (ArgumentException)
        {
            // The dialog may still be entering/leaving the visual tree.
        }
    }

    private static void SetModalButtons(IReadOnlyList<Button> buttons, int columns)
    {
        s_modalButtons.Clear();
        s_modalButtons.AddRange(buttons);
        s_buttonColumns = Math.Max(1, columns);
        FocusButton(0);
    }

    private static void ClearModalButtons()
    {
        s_modalButtons.Clear();
        s_focusedButtonIndex = 0;
        s_buttonColumns = 1;
    }

    private static void FocusButton(int index)
    {
        if (s_modalButtons.Count == 0)
        {
            return;
        }

        s_focusedButtonIndex = Math.Clamp(index, 0, s_modalButtons.Count - 1);
        for (var i = 0; i < s_modalButtons.Count; i++)
        {
            var selected = i == s_focusedButtonIndex;
            s_modalButtons[i].BorderThickness = selected ? new Thickness(2.5) : new Thickness(1);
            s_modalButtons[i].BorderBrush = new SolidColorBrush(selected ? Color.FromArgb(255, 229, 83, 255) : Color.FromArgb(80, 255, 255, 255));
            s_modalButtons[i].Background = new SolidColorBrush(selected ? Color.FromArgb(235, 56, 25, 70) : Color.FromArgb(218, 18, 18, 24));
        }

        var button = s_modalButtons[s_focusedButtonIndex];
        button.DispatcherQueue.TryEnqueue(() => button.Focus(FocusState.Programmatic));
    }

    private static (double Width, double Height) GetPreviewSize(SteamGridDbImageOption option)
    {
        const double maxWidth = 290;
        const double maxHeight = 205;
        var width = option.Width > 0 ? option.Width : 600;
        var height = option.Height > 0 ? option.Height : 900;
        var scale = Math.Min(maxWidth / width, maxHeight / height);
        return (Math.Max(80, width * scale), Math.Max(80, height * scale));
    }

    private enum GameOptionAction
    {
        None,
        ToggleFavorite,
        SelectLauncher,
        ChangeCover,
        ChangeHero,
        ChangeLogo,
        RemoveFromLibrary
    }

}
