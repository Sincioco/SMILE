using System.Windows.Input;

namespace SMILE.Desktop;

public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;
    private readonly Action<Exception>? _onError;

    public RelayCommand(
        Action execute,
        Func<bool>? canExecute = null,
        Action<Exception>? onError = null)
    {
        _execute = execute;
        _canExecute = canExecute;
        _onError = onError;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        try
        {
            return _canExecute?.Invoke() ?? true;
        }
        catch (Exception ex)
        {
            ReportError(ex);
            return false;
        }
    }

    public void Execute(object? parameter)
    {
        try
        {
            if (!CanExecute(parameter))
            {
                return;
            }

            _execute();
        }
        catch (OperationCanceledException)
        {
            // Cancellation is a normal control-flow signal for long commands.
            // It should not surface as an unhandled WPF command exception.
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }

    public void RaiseCanExecuteChanged()
    {
        try
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }

    private void ReportError(Exception exception)
    {
        try
        {
            _onError?.Invoke(exception);
        }
        catch
        {
            // Command error reporting must never become the second exception
            // that tears down the UI.
        }
    }
}

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private readonly Action<Exception>? _onError;
    private bool _isRunning;

    public AsyncRelayCommand(
        Func<Task> execute,
        Func<bool>? canExecute = null,
        Action<Exception>? onError = null)
    {
        _execute = execute;
        _canExecute = canExecute;
        _onError = onError;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        try
        {
            return !_isRunning && (_canExecute?.Invoke() ?? true);
        }
        catch (Exception ex)
        {
            ReportError(ex);
            return false;
        }
    }

    public async void Execute(object? parameter)
    {
        try
        {
            await ExecuteCoreAsync(parameter).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is expected for long-running commands.
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }

    private async Task ExecuteCoreAsync(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _isRunning = true;
        RaiseCanExecuteChanged();

        try
        {
            await _execute().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException)
            {
                // Async commands are frequently cancelled by the user pressing
                // Cancel. The view-model decides what status text to show.
                return;
            }

            ReportError(ex);
        }
        finally
        {
            _isRunning = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged()
    {
        try
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }

    private void ReportError(Exception exception)
    {
        try
        {
            _onError?.Invoke(exception);
        }
        catch
        {
            // Async command failures have already happened. If the reporting
            // callback also fails, swallowing here keeps WPF alive.
        }
    }
}
