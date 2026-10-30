using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.Views;

public partial class DiscardContactLearningWindow : Window
{
    private readonly TestViewModel _test;
    private readonly DiscardContactLearner _learner;
    private readonly object _gate = new();
    private Task _startup = Task.CompletedTask;
    private bool _active, _ownsSession, _closing, _allowClose, _accepted;
    public int FirstIo { get; private set; }
    public int SecondIo { get; private set; }

    public DiscardContactLearningWindow(TestViewModel test, int stableFrames = 2)
    {
        InitializeComponent(); _test = test; _learner = new(stableFrames);
        Loaded += (_, _) => _startup = StartAsync();
        Closing += Window_Closing;
    }

    private async Task StartAsync()
    {
        try
        {
            await _test.StartTopologyLearningAsync();
            _ownsSession = true;
            if (_closing) return;
            if (!_test.IsBoardConnected || _test.IsDeviceFault)
                throw new InvalidOperationException(TesterConnectionNotice.Message);
            lock (_gate) _active = true;
            _test.BoardFrameActivity += OnFrame;
            _test.PropertyChanged += OnDeviceChanged;
            ApplySnapshot(_learner.Current);
        }
        catch (Exception ex)
        {
            AsyncFileLogService.Current.Error($"DISCARD LEARN startup: {ex}");
            LearningMessageText.Text = ex.Message;
        }
    }

    private void OnFrame(object? sender, ScanFrame frame)
    {
        DiscardLearningSnapshot state;
        lock (_gate)
        {
            if (!_active) return;
            state = !_test.IsBoardConnected || _test.IsDeviceFault
                ? new(TesterConnectionNotice.Message) : _learner.Observe(frame, _test.BoardCapacity);
            if (state.Completed) _active = false;
        }
        // Only transitions dispatch work; never add another FTDI owner/reader.
        if (ReferenceEquals(state, _lastDispatched)) return;
        _lastDispatched = state;
        Dispatcher.BeginInvoke(() => { if (!_closing) ApplySnapshot(state); }, DispatcherPriority.Background);
    }
    private DiscardLearningSnapshot? _lastDispatched;
    private void OnDeviceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TestViewModel.IsBoardConnected) or nameof(TestViewModel.IsDeviceFault))
            Dispatcher.BeginInvoke(() =>
            {
                if (!_closing && (!_test.IsBoardConnected || _test.IsDeviceFault))
                    LearningMessageText.Text = TesterConnectionNotice.Message;
            });
    }

    private void ApplySnapshot(DiscardLearningSnapshot state)
    {
        LearningMessageText.Text = state.Message;
        LearnedIoText.Text = state.FirstIo > 0 ? $"IO {state.FirstIo} / {state.SecondIo}" : "IO — / —";
        if (state.Completed && _ownsSession)
        {
            if (!_test.IsBoardConnected || _test.IsDeviceFault)
            {
                LearningMessageText.Text = TesterConnectionNotice.Message;
                return;
            }
            FirstIo = state.FirstIo; SecondIo = state.SecondIo; _accepted = true;
            Close();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        lock (_gate) _active = false;
        _test.BoardFrameActivity -= OnFrame;
        _test.PropertyChanged -= OnDeviceChanged;
        try
        {
            await _startup;
            if (_ownsSession) await _test.StopTopologyLearningAsync();
        }
        catch (Exception ex) { AsyncFileLogService.Current.Error($"DISCARD LEARN restore: {ex}"); _accepted = false; }
        _allowClose = true;
        DialogResult = _accepted;
    }
}
