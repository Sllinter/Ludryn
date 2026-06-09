using Ludryn.Services;
using Microsoft.UI.Xaml;

namespace Ludryn;

public partial class App : Application
{
    private static Window? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += App_UnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        LudrynLogger.Log("app", "Ludryn launched.");
        _window = new MainWindow();
        _window.Activate();
    }

    public static MainWindow? MainWindow() => _window as MainWindow;

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
