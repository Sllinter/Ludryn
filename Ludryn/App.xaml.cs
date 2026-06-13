using Ludryn.Services;
using Microsoft.Windows.AppLifecycle;
using Microsoft.UI.Xaml;

namespace Ludryn;

public partial class App : Application
{
    private const string MainInstanceKey = "Ludryn.MainInstance";
    private static Window? _window;
    private AppInstance? _mainInstance;
    private bool _activationPending;

    public App()
    {
        InitializeComponent();
        UnhandledException += App_UnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var currentInstance = AppInstance.GetCurrent();
        _mainInstance = AppInstance.FindOrRegisterForKey(MainInstanceKey);

        if (!_mainInstance.IsCurrent)
        {
            LudrynLogger.Log("app", "Redirecting activation to the existing Ludryn instance.");
            await _mainInstance.RedirectActivationToAsync(currentInstance.GetActivatedEventArgs());
            Exit();
            return;
        }

        _mainInstance.Activated += MainInstance_Activated;
        LudrynLogger.Log("app", "Ludryn launched.");
        _window = new MainWindow();
        _window.Activate();

        if (_activationPending)
        {
            _activationPending = false;
            BringMainWindowToFront();
        }
    }

    public static MainWindow? MainWindow() => _window as MainWindow;

    private void MainInstance_Activated(object? sender, AppActivationArguments args)
    {
        LudrynLogger.Log("app", $"Activation redirected to the main instance. Kind={args.Kind}");

        if (_window is null)
        {
            _activationPending = true;
            return;
        }

        _window.DispatcherQueue.TryEnqueue(BringMainWindowToFront);
    }

    private static void BringMainWindowToFront()
    {
        if (_window is MainWindow mainWindow)
        {
            mainWindow.BringToFront();
        }
    }

    private static void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        LudrynLogger.Error("app", $"Unhandled WinUI exception: {e.Message}", e.Exception);
    }

    private static void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            LudrynLogger.Error("app", "Unhandled AppDomain exception.", exception);
        }
        else
        {
            LudrynLogger.Log("app", $"Unhandled AppDomain exception object: {e.ExceptionObject}");
        }
    }

    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LudrynLogger.Error("app", "Unobserved task exception.", e.Exception);
        e.SetObserved();
    }
}
