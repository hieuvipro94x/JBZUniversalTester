using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.Core;

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _run;
    private readonly Func<bool>? _can;
    private readonly Action<Exception>? _onError;
    private readonly bool _disableWhileRunning;
    private bool _busy;

    public AsyncRelayCommand(
        Func<Task> run,
        Func<bool>? can = null,
        Action<Exception>? onError = null,
        bool disableWhileRunning = true)
    {
        _run = run;
        _can = can;
        _onError = onError;
        _disableWhileRunning = disableWhileRunning;
    }

    public bool CanExecute(object? parameter)
    {
        return (!_disableWhileRunning || !_busy) &&
               (_can?.Invoke() ?? true);
    }

    public async void Execute(object? parameter)
    {
        // Execution is always serialized, even when a caller keeps the button's
        // visual state stable during a pending operation.
        if (_busy || !CanExecute(parameter))
        {
            return;
        }

        _busy = true;
        if (_disableWhileRunning) RaiseCanExecuteChanged();

        try
        {
            await _run();
        }
        catch (OperationCanceledException)
        {
            // Hủy thao tác do đóng cửa sổ/đổi mode không phải lỗi ứng dụng.
        }
        catch (Exception ex)
        {
            // SAFE OFFLINE MODE: ICommand.Execute là async void. Nếu exception
            // thoát khỏi đây WPF Dispatcher có thể kết thúc toàn bộ ứng dụng.
            // Chặn tại biên command, ghi log và báo rõ để app tiếp tục chạy.
            AsyncFileLogService.Current.Error($"COMMAND ERROR: {ex}");
            if (_onError is not null)
            {
                _onError(ex);
                return;
            }
            MessageBox.Show(
                string.IsNullOrWhiteSpace(ex.Message)
                    ? "Thao tác không thực hiện được. Hãy kiểm tra kết nối thiết bị và thử lại."
                    : ex.Message,
                ex.Message == TesterConnectionNotice.Message
                    ? TesterConnectionNotice.Message : "Không thể thực hiện thao tác",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _busy = false;
            if (_disableWhileRunning) RaiseCanExecuteChanged();
        }
    }

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(
            this,
            EventArgs.Empty
        );
    }

    public void Changed()
    {
        RaiseCanExecuteChanged();
    }
}
