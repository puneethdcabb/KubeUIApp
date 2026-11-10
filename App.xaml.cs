using System;
using System.Configuration;
using System.Data;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using KubeUIApp.Services;

namespace KubeUIApp;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        AppLogger.LogInfo("App.OnStartup", $"KubeShift Manager started. Log file: {AppLogger.LogFilePath}");
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLogger.LogError("App.DispatcherUnhandledException", "Unhandled UI dispatcher exception occurred.", e.Exception);
        MessageBox.Show(
            $"An unexpected error occurred:\n{e.Exception.Message}\n\nDetails have been written to:\n{AppLogger.LogFilePath}",
            "Unexpected Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            AppLogger.LogError("App.DomainUnhandledException", $"Unhandled AppDomain exception (IsTerminating={e.IsTerminating}).", ex);
        }
        else
        {
            AppLogger.LogError("App.DomainUnhandledException", $"Unhandled non-Exception object: {e.ExceptionObject}");
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLogger.LogError("App.UnobservedTaskException", "Unobserved background task exception occurred.", e.Exception);
        e.SetObserved();
    }
}

