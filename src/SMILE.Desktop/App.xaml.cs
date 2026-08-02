using System.Windows;
using System.Windows.Threading;

namespace SMILE.Desktop;

public partial class App : Application
{
    private readonly IAppErrorReporter _errorReporter = AppErrorReporter.Shared;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledDomainException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        base.OnStartup(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (DesktopExceptionPolicy.IsFatal(e.Exception))
        {
            return;
        }

        try
        {
            string details = _errorReporter.Report("WPF dispatcher", e.Exception, stage: "UI Thread");
            if (MainWindow?.DataContext is MainWindowViewModel viewModel)
            {
                viewModel.HandleGlobalException("WPF dispatcher", e.Exception, details);
            }
        }
        catch
        {
            // Exception handling must not recurse. The diagnostic safety net is
            // intentionally best-effort so a recoverable UI exception does not
            // become an exception-reporting loop.
        }

        e.Handled = true;
    }

    private void OnUnhandledDomainException(object sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            if (e.ExceptionObject is Exception exception)
            {
                _errorReporter.Report("AppDomain unhandled exception", exception, stage: "Process Termination");
            }
        }
        catch
        {
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        try
        {
            _errorReporter.Report("Unobserved task", e.Exception, stage: "Task Finalizer");
            e.SetObserved();
        }
        catch
        {
        }
    }
}
