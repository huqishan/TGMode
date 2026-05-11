using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace ControlLibrary
{
    /// <summary>
    /// 同步命令封装，用于把按钮点击从后台事件迁移到 ViewModel。
    /// </summary>
    public sealed class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Predicate<object?>? _canExecute;
        private readonly Dispatcher? _dispatcher;

        public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
            _dispatcher = CommandThreadingHelper.ResolveDispatcher();
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return _canExecute?.Invoke(parameter) ?? true;
        }

        public void Execute(object? parameter)
        {
            _execute(parameter);
        }

        public void RaiseCanExecuteChanged()
        {
            CommandThreadingHelper.RaiseCanExecuteChangedCore(CanExecuteChanged, _dispatcher, this);
        }
    }

    /// <summary>
    /// 异步命令封装，防止接口测试期间重复触发同一个耗时操作。
    /// </summary>
    public sealed class AsyncRelayCommand : ICommand
    {
        private readonly Func<object?, Task> _executeAsync;
        private readonly Predicate<object?>? _canExecute;
        private readonly Dispatcher? _dispatcher;
        private bool _isExecuting;

        public AsyncRelayCommand(Func<object?, Task> executeAsync, Predicate<object?>? canExecute = null)
        {
            _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
            _canExecute = canExecute;
            _dispatcher = CommandThreadingHelper.ResolveDispatcher();
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return !_isExecuting && (_canExecute?.Invoke(parameter) ?? true);
        }

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter))
            {
                return;
            }

            try
            {
                _isExecuting = true;
                RaiseCanExecuteChanged();
                await _executeAsync(parameter);
            }
            finally
            {
                _isExecuting = false;
                RaiseCanExecuteChanged();
            }
        }

        public void RaiseCanExecuteChanged()
        {
            CommandThreadingHelper.RaiseCanExecuteChangedCore(CanExecuteChanged, _dispatcher, this);
        }
    }

    internal static class CommandThreadingHelper
    {
        internal static Dispatcher? ResolveDispatcher()
        {
            return Application.Current?.Dispatcher
                   ?? Dispatcher.FromThread(System.Threading.Thread.CurrentThread)
                   ?? Dispatcher.CurrentDispatcher;
        }

        internal static void RaiseCanExecuteChangedCore(EventHandler? handler, Dispatcher? dispatcher, object sender)
        {
            if (handler is null)
            {
                return;
            }

            if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished || dispatcher.CheckAccess())
            {
                handler(sender, EventArgs.Empty);
                return;
            }

            _ = dispatcher.BeginInvoke(
                DispatcherPriority.DataBind,
                new Action(() => handler(sender, EventArgs.Empty)));
        }
    }
}
