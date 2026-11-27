using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using TuSkEer.AbDoILPoLiCe.Services;
using TuSkEer.AbDoILPoLiCe.Storage;
using TuSkEer.AbDoILPoLiCe.ViewModels;

namespace TuSkEer.AbDoILPoLiCe;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private GlobalKeyboardHookService? _hookService;
    private ScriptQueueScheduler? _scheduler;
    private SystemTrayService? _trayService;
    private MainViewModel? _viewModel;
    private MainWindow? _mainWindow;
    private bool _isExiting;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, "TuSkEer_AbDo_iL_PoLiCe_SingleInstance_Mutex", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "TuSkEer AbDo iL PoLiCe is already running in the Windows System Tray.",
                "TuSkEer AbDo iL PoLiCe",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown(0);
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            args.SetObserved();
        };

        var repository = new SqliteRepository();
        var clipboardService = new ClipboardService();
        var typingEngine = new TypingEngine(clipboardService);
        _scheduler = new ScriptQueueScheduler(typingEngine);
        _hookService = new GlobalKeyboardHookService();
        var captureService = new CaptureExclusionService();

        _viewModel = new MainViewModel(
            repository,
            _hookService,
            typingEngine,
            _scheduler,
            captureService);

        _mainWindow = new MainWindow(_viewModel, () => _isExiting);
        _trayService = new SystemTrayService();

        _trayService.OpenRequested += ShowMainWindow;
        _trayService.PauseResumeRequested += () => _scheduler.TogglePauseResume();
        _trayService.StopRequested += () => _scheduler.StopAll();
        _trayService.ExitRequested += ExitApplicationCompletely;

        _scheduler.TelemetryChanged += snapshot =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                _trayService?.UpdatePauseLabel(snapshot.State == Models.EngineRunState.Paused);
            });
        };

        _mainWindow.Show();
        await _viewModel.InitializeAsync(_mainWindow);
    }

    private void ShowMainWindow()
    {
        Dispatcher.Invoke(() =>
        {
            if (_mainWindow is null) return;
            if (!_mainWindow.IsVisible)
            {
                _mainWindow.Show();
            }

            if (_mainWindow.WindowState == WindowState.Minimized)
            {
                _mainWindow.WindowState = WindowState.Normal;
            }

            _mainWindow.Activate();
            _mainWindow.Topmost = true;
            _mainWindow.Topmost = false;
            _mainWindow.Focus();
        });
    }

    public void ExitApplicationCompletely()
    {
        Dispatcher.Invoke(() =>
        {
            _isExiting = true;
            _scheduler?.StopAll();
            _hookService?.Dispose();
            _scheduler?.Dispose();
            _viewModel?.Dispose();
            _trayService?.Dispose();
            _mainWindow?.Close();
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
            Shutdown(0);
        });
    }
}
