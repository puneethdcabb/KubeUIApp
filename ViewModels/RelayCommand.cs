using System;
using System.Threading.Tasks;
using System.Windows.Input;
using KubeUIApp.Services;

namespace KubeUIApp.ViewModels
{
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Predicate<object?>? _canExecute;

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
            : this(_ => execute(), canExecute != null ? _ => canExecute() : null)
        {
        }

        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute(parameter);

        public void Execute(object? parameter)
        {
            try
            {
                _execute(parameter);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("RelayCommand.Execute", "Error executing synchronous command.", ex);
                throw;
            }
        }
    }

    public class AsyncRelayCommand : ICommand
    {
        private readonly Func<object?, Task> _execute;
        private readonly Predicate<object?>? _canExecute;
        private bool _isExecuting;

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public AsyncRelayCommand(Func<object?, Task> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
            : this(_ => execute(), canExecute != null ? _ => canExecute() : null)
        {
        }

        public bool CanExecute(object? parameter)
        {
            return !_isExecuting && (_canExecute == null || _canExecute(parameter));
        }

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter)) return;
            try
            {
                _isExecuting = true;
                CommandManager.InvalidateRequerySuggested();
                await _execute(parameter);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("AsyncRelayCommand.Execute", "Unhandled exception in async command execution.", ex);
            }
            finally
            {
                _isExecuting = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }
}
