using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using TuSkEer.AbDoILPoLiCe.Models;
using TuSkEer.AbDoILPoLiCe.Native;
using TuSkEer.AbDoILPoLiCe.Services;
using TuSkEer.AbDoILPoLiCe.Storage;

namespace TuSkEer.AbDoILPoLiCe.ViewModels;

public enum NavigationTab
{
    Dashboard = 0,
    ScriptsLibrary = 1,
    ScriptEditor = 2,
    KeyboardShortcuts = 3,
    TypingEngine = 4,
    Settings = 5,
    About = 6
}

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly SqliteRepository _repository;
    private readonly GlobalKeyboardHookService _hookService;
    private readonly TypingEngine _typingEngine;
    private readonly ScriptQueueScheduler _scheduler;
    private readonly CaptureExclusionService _captureService;
    private readonly Dispatcher _dispatcher;

    private readonly DispatcherTimer _autoSaveTimer;
    private readonly DispatcherTimer _settingsSaveTimer;

    private Window? _hostWindow;
    private AppSettings _settings = new();
    private NavigationTab _activeTab = NavigationTab.ScriptEditor;
    private ScriptItem? _selectedScript;
    private string _searchQuery = string.Empty;
    private bool _filterEnabledOnly;
    private bool _isLoadingEditor;
    private bool _hasUnsavedEditorChanges;
    private string _saveBannerText = "All changes saved to SQLite";

    // Shortcut recording state
    private bool _isRecordingHotkey;
    private string _recordingTargetLabel = string.Empty;
    private string _recordingPromptText = "Press any key or combination (Esc to cancel)...";
    private HotkeyBinding? _pendingRecordedHotkey;
    private string _shortcutConflictWarning = string.Empty;
    private Action<HotkeyBinding>? _pendingApplyCallback;

    // Live Telemetry
    private EngineTelemetrySnapshot _telemetry = new();
    private string _captureStatusNote = "Standard visibility (SetWindowDisplayAffinity disabled).";
    private string _dataOperationStatus = string.Empty;
    private bool _isEmojiPickerOpen;
    private Dictionary<long, ScriptItem> _enabledScriptsById = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ScriptItem> Scripts { get; } = new();
    public ICollectionView FilteredScripts { get; }
    public ObservableCollection<QueuedScriptSession> QueueItems { get; } = new();

    public IReadOnlyList<string> QuickEmojis { get; } = new[]
    {
        "🔥", "❤️", "⚡", "👑", "🚀", "✨", "💎", "🎯", "🛡️", "✅",
        "😂", "😎", "🤝", "💯", "🎉", "🌹", "🦅", "⭐", "💥", "🔔"
    };

    public MainViewModel(
        SqliteRepository repository,
        GlobalKeyboardHookService hookService,
        TypingEngine typingEngine,
        ScriptQueueScheduler scheduler,
        CaptureExclusionService captureService)
    {
        _repository = repository;
        _hookService = hookService;
        _typingEngine = typingEngine;
        _scheduler = scheduler;
        _captureService = captureService;
        _dispatcher = Application.Current.Dispatcher;

        FilteredScripts = CollectionViewSource.GetDefaultView(Scripts);
        FilteredScripts.Filter = FilterScriptPredicate;

        _autoSaveTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(550)
        };
        _autoSaveTimer.Tick += async (_, _) =>
        {
            _autoSaveTimer.Stop();
            await SaveSelectedScriptAsync();
        };

        _settingsSaveTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };
        _settingsSaveTimer.Tick += async (_, _) =>
        {
            _settingsSaveTimer.Stop();
            await _repository.SaveSettingsAsync(_settings);
        };

        _scheduler.TelemetryChanged += OnSchedulerTelemetryChanged;
        _hookService.HotkeyTriggered += OnGlobalHotkeyTriggered;

        // Commands
        NavigateCommand = new RelayCommand(p =>
        {
            if (p is NavigationTab tab) ActiveTab = tab;
            else if (p is string s && Enum.TryParse<NavigationTab>(s, true, out var parsed)) ActiveTab = parsed;
        });

        AddScriptCommand = new RelayCommand(async () => await AddNewScriptAsync());
        SaveScriptCommand = new RelayCommand(async () => await SaveSelectedScriptAsync());
        DuplicateScriptCommand = new RelayCommand(async p => await DuplicateScriptAsync(p as ScriptItem ?? SelectedScript));
        DeleteScriptCommand = new RelayCommand(async p => await DeleteScriptAsync(p as ScriptItem ?? SelectedScript));
        OpenScriptInEditorCommand = new RelayCommand(p =>
        {
            if (p is ScriptItem item)
            {
                SelectedScript = item;
                ActiveTab = NavigationTab.ScriptEditor;
            }
        });

        StartSelectedScriptCommand = new RelayCommand(StartSelectedOrFirstScript);
        RunSpecificScriptCommand = new RelayCommand(p =>
        {
            if (p is ScriptItem item) _scheduler.EnqueueOrSwitch(item);
        });
        PauseResumeCommand = new RelayCommand(() => _scheduler.TogglePauseResume());
        StopAllCommand = new RelayCommand(() => _scheduler.StopAll());

        RecordSelectedScriptHotkeyCommand = new RelayCommand(() =>
        {
            if (SelectedScript is null) return;
            BeginRecording($"Script: {SelectedScript.Name}", binding => ApplyScriptHotkey(SelectedScript, binding));
        });

        RecordScriptRowHotkeyCommand = new RelayCommand(p =>
        {
            if (p is ScriptItem item)
            {
                BeginRecording($"Script: {item.Name}", binding => ApplyScriptHotkey(item, binding));
            }
        });

        ClearScriptHotkeyCommand = new RelayCommand(async p =>
        {
            var target = p as ScriptItem ?? SelectedScript;
            if (target is null) return;
            target.Hotkey = new HotkeyBinding();
            target.NotifyHotkeyChanged();
            if (SelectedScript == target)
            {
                OnPropertyChanged(nameof(SelectedScriptHotkeyDisplay));
            }
            await _repository.UpsertScriptAsync(target);
            RefreshHookBindings();
            ShortcutConflictWarning = string.Empty;
            OnPropertyChanged(nameof(HasShortcutConflict));
        });

        RecordPauseHotkeyCommand = new RelayCommand(() =>
            BeginRecording("Global Pause / Resume", ApplyPauseHotkey));

        RecordStopHotkeyCommand = new RelayCommand(() =>
            BeginRecording("Global Stop", ApplyStopHotkey));

        RecordSpeedUpHotkeyCommand = new RelayCommand(() =>
            BeginRecording("Speed +5 ms", ApplySpeedUpHotkey));

        RecordSpeedDownHotkeyCommand = new RelayCommand(() =>
            BeginRecording("Speed -5 ms", ApplySpeedDownHotkey));

        ConfirmRecordedHotkeyCommand = new RelayCommand(ConfirmRecordedHotkey, () => _pendingRecordedHotkey is not null);
        CancelRecordingHotkeyCommand = new RelayCommand(CancelRecordingHotkey);

        InsertEmojiToAppendCommand = new RelayCommand(p =>
        {
            if (p is string emoji)
            {
                AppendContent = (AppendContent ?? string.Empty) + emoji;
                IsEmojiPickerOpen = false;
            }
        });

        InsertEmojiToEditorCommand = new RelayCommand(p =>
        {
            if (p is string emoji && SelectedScript is not null)
            {
                SelectedScript.Content += emoji;
                ScheduleAutoSave();
            }
        });

        ToggleEmojiPickerCommand = new RelayCommand(() => IsEmojiPickerOpen = !IsEmojiPickerOpen);

        ExportJsonCommand = new RelayCommand(async () => await ExportJsonAsync());
        ImportJsonCommand = new RelayCommand(async () => await ImportJsonAsync());
        BackupDatabaseCommand = new RelayCommand(async () => await BackupDatabaseAsync());
        RestoreDatabaseCommand = new RelayCommand(async () => await RestoreDatabaseAsync());

        ToggleScriptEnabledCommand = new RelayCommand(async p =>
        {
            if (p is ScriptItem item)
            {
                await _repository.UpsertScriptAsync(item);
                RefreshHookBindings();
            }
        });
    }

    public ICommand NavigateCommand { get; }
    public ICommand AddScriptCommand { get; }
    public ICommand SaveScriptCommand { get; }
    public ICommand DuplicateScriptCommand { get; }
    public ICommand DeleteScriptCommand { get; }
    public ICommand OpenScriptInEditorCommand { get; }
    public ICommand StartSelectedScriptCommand { get; }
    public ICommand RunSpecificScriptCommand { get; }
    public ICommand PauseResumeCommand { get; }
    public ICommand StopAllCommand { get; }
    public ICommand RecordSelectedScriptHotkeyCommand { get; }
    public ICommand RecordScriptRowHotkeyCommand { get; }
    public ICommand ClearScriptHotkeyCommand { get; }
    public ICommand RecordPauseHotkeyCommand { get; }
    public ICommand RecordStopHotkeyCommand { get; }

    public ICommand RecordSpeedUpHotkeyCommand { get; }

    public ICommand RecordSpeedDownHotkeyCommand { get; }
    public ICommand ConfirmRecordedHotkeyCommand { get; }
    public ICommand CancelRecordingHotkeyCommand { get; }
    public ICommand InsertEmojiToAppendCommand { get; }
    public ICommand InsertEmojiToEditorCommand { get; }
    public ICommand ToggleEmojiPickerCommand { get; }
    public ICommand ExportJsonCommand { get; }
    public ICommand ImportJsonCommand { get; }
    public ICommand BackupDatabaseCommand { get; }
    public ICommand RestoreDatabaseCommand { get; }
    public ICommand ToggleScriptEnabledCommand { get; }

    public async Task InitializeAsync(Window hostWindow)
    {
        _hostWindow = hostWindow;
        await _repository.InitializeAsync();

        _settings = await _repository.LoadSettingsAsync();
        _typingEngine.ApplyLiveSettings(_settings);

        var loadedScripts = await _repository.GetAllScriptsAsync();
        Scripts.Clear();
        foreach (var s in loadedScripts)
        {
            Scripts.Add(s);
        }

        SelectedScript = Scripts.FirstOrDefault();
        RefreshHookBindings();
        _hookService.Start();

        ApplyWindowBackdrop();
        ApplyCaptureExclusion();

        Telemetry = _scheduler.GetSnapshot();
        NotifySettingsPropertiesChanged();
        OnPropertyChanged(nameof(TotalScriptsCount));
        OnPropertyChanged(nameof(EnabledScriptsCount));
        OnPropertyChanged(nameof(HookStatusText));
        OnPropertyChanged(nameof(DatabasePath));
    }

    public NavigationTab ActiveTab
    {
        get => _activeTab;
        set => SetField(ref _activeTab, value);
    }

    public ScriptItem? SelectedScript
    {
        get => _selectedScript;
        set
        {
            if (SetField(ref _selectedScript, value))
            {
                _isLoadingEditor = true;
                OnPropertyChanged(nameof(EditorName));
                OnPropertyChanged(nameof(EditorContent));
                OnPropertyChanged(nameof(EditorCategory));
                OnPropertyChanged(nameof(EditorIsEnabled));
                OnPropertyChanged(nameof(SelectedScriptHotkeyDisplay));
                OnPropertyChanged(nameof(SelectedScriptSentenceCount));
                OnPropertyChanged(nameof(SelectedScriptCharacterCount));
                _isLoadingEditor = false;
                HasUnsavedEditorChanges = false;
            }
        }
    }

    public string EditorName
    {
        get => SelectedScript?.Name ?? string.Empty;
        set
        {
            if (SelectedScript is null || _isLoadingEditor) return;
            if (SelectedScript.Name != value)
            {
                SelectedScript.Name = value;
                OnPropertyChanged();
                ScheduleAutoSave();
            }
        }
    }

    public string EditorContent
    {
        get => SelectedScript?.Content ?? string.Empty;
        set
        {
            if (SelectedScript is null || _isLoadingEditor) return;
            if (SelectedScript.Content != value)
            {
                SelectedScript.Content = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedScriptSentenceCount));
                OnPropertyChanged(nameof(SelectedScriptCharacterCount));
                ScheduleAutoSave();
            }
        }
    }

    public string EditorCategory
    {
        get => SelectedScript?.Category ?? "General";
        set
        {
            if (SelectedScript is null || _isLoadingEditor) return;
            if (SelectedScript.Category != value)
            {
                SelectedScript.Category = value;
                OnPropertyChanged();
                ScheduleAutoSave();
            }
        }
    }

    public bool EditorIsEnabled
    {
        get => SelectedScript?.IsEnabled ?? false;
        set
        {
            if (SelectedScript is null || _isLoadingEditor) return;
            if (SelectedScript.IsEnabled != value)
            {
                SelectedScript.IsEnabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EnabledScriptsCount));
                ScheduleAutoSave();
                RefreshHookBindings();
            }
        }
    }

    public string SelectedScriptHotkeyDisplay => SelectedScript?.HotkeyDisplay ?? "None";
    public int SelectedScriptSentenceCount => SelectedScript?.SentenceCount ?? 0;
    public int SelectedScriptCharacterCount => SelectedScript?.CharacterCount ?? 0;

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetField(ref _searchQuery, value))
            {
                FilteredScripts.Refresh();
            }
        }
    }

    public bool FilterEnabledOnly
    {
        get => _filterEnabledOnly;
        set
        {
            if (SetField(ref _filterEnabledOnly, value))
            {
                FilteredScripts.Refresh();
            }
        }
    }

    public int TotalScriptsCount => Scripts.Count;
    public int EnabledScriptsCount => Scripts.Count(s => s.IsEnabled);
    public string HookStatusText => _hookService.IsHookActive ? "Global Hook Active" : "Hook Inactive";
    public string DatabasePath => _repository.DatabasePath;

    public bool HasUnsavedEditorChanges
    {
        get => _hasUnsavedEditorChanges;
        private set => SetField(ref _hasUnsavedEditorChanges, value);
    }

    public string SaveBannerText
    {
        get => _saveBannerText;
        private set => SetField(ref _saveBannerText, value);
    }

    // Live Typing Settings
    public int TypingDelayMs
    {
        get => _settings.TypingDelayMs;
        set
        {
            int clamped = Math.Clamp(value, 0, 1000);
            if (_settings.TypingDelayMs != clamped)
            {
                _settings.TypingDelayMs = clamped;
                _typingEngine.ApplyLiveSettings(_settings);
                OnPropertyChanged();
                OnPropertyChanged(nameof(TypingSpeedLabel));
                ScheduleSettingsSave();
            }
        }
    }

    public string TypingSpeedLabel => _settings.TypingDelayMs == 0
        ? "0 ms (Max Speed)"
        : $"{_settings.TypingDelayMs} ms";

    public bool RepeatFastSendEnabled
    {
        get => _settings.RepeatFastSendEnabled;
        set
        {
            if (_settings.RepeatFastSendEnabled != value)
            {
                _settings.RepeatFastSendEnabled = value;
                _typingEngine.ApplyLiveSettings(_settings);
                OnPropertyChanged();
                ScheduleSettingsSave();
            }
        }
    }

    public int RepeatThreshold
    {
        get => _settings.RepeatThreshold;
        set
        {
            int clamped = Math.Clamp(value, 2, 100);
            if (_settings.RepeatThreshold != clamped)
            {
                _settings.RepeatThreshold = clamped;
                _typingEngine.ApplyLiveSettings(_settings);
                OnPropertyChanged();
                ScheduleSettingsSave();
            }
        }
    }

    public bool AppendEnabled
    {
        get => _settings.AppendEnabled;
        set
        {
            if (_settings.AppendEnabled != value)
            {
                _settings.AppendEnabled = value;
                _typingEngine.ApplyLiveSettings(_settings);
                OnPropertyChanged();
                ScheduleSettingsSave();
            }
        }
    }

    public string AppendContent
    {
        get => _settings.AppendContent;
        set
        {
            if (_settings.AppendContent != value)
            {
                _settings.AppendContent = value ?? string.Empty;
                _typingEngine.ApplyLiveSettings(_settings);
                OnPropertyChanged();
                ScheduleSettingsSave();
            }
        }
    }

    public bool UseClipboardPasteMode
    {
        get => _settings.AppendPasteMode == AppendPasteMode.ClipboardPreserve;
        set
        {
            var next = value ? AppendPasteMode.ClipboardPreserve : AppendPasteMode.UnicodeSendInput;
            if (_settings.AppendPasteMode != next)
            {
                _settings.AppendPasteMode = next;
                _typingEngine.ApplyLiveSettings(_settings);
                OnPropertyChanged();
                OnPropertyChanged(nameof(AppendModeLabel));
                ScheduleSettingsSave();
            }
        }
    }

    public string AppendModeLabel => _settings.AppendPasteMode == AppendPasteMode.ClipboardPreserve
        ? "Direct Paste (Clipboard Preserved)"
        : "Fast Unicode SendInput (No Clipboard)";

    public string PauseResumeHotkeyDisplay => _settings.PauseResumeHotkey.DisplayString;
    public string StopHotkeyDisplay => _settings.StopHotkey.DisplayString;

    public string SpeedUpHotkeyDisplay => _settings.SpeedUpHotkey.DisplayString;

    public string SpeedDownHotkeyDisplay => _settings.SpeedDownHotkey.DisplayString;

    public bool ExcludeFromCapture
    {
        get => _settings.ExcludeFromCapture;
        set
        {
            if (_settings.ExcludeFromCapture != value)
            {
                _settings.ExcludeFromCapture = value;
                OnPropertyChanged();
                ApplyCaptureExclusion();
                ScheduleSettingsSave();
            }
        }
    }

    public bool MinimizeToTrayOnClose
    {
        get => _settings.MinimizeToTrayOnClose;
        set
        {
            if (_settings.MinimizeToTrayOnClose != value)
            {
                _settings.MinimizeToTrayOnClose = value;
                OnPropertyChanged();
                ScheduleSettingsSave();
            }
        }
    }

    public bool EnableBackgroundParticles
    {
        get => _settings.EnableBackgroundParticles;
        set
        {
            if (_settings.EnableBackgroundParticles != value)
            {
                _settings.EnableBackgroundParticles = value;
                OnPropertyChanged();
                ScheduleSettingsSave();
            }
        }
    }

    public BackdropMaterialMode BackdropMaterial
    {
        get => _settings.BackdropMaterial;
        set
        {
            if (_settings.BackdropMaterial != value)
            {
                _settings.BackdropMaterial = value;
                OnPropertyChanged();
                ApplyWindowBackdrop();
                ScheduleSettingsSave();
            }
        }
    }

    public string CaptureStatusNote
    {
        get => _captureStatusNote;
        private set => SetField(ref _captureStatusNote, value);
    }

    public string DataOperationStatus
    {
        get => _dataOperationStatus;
        private set => SetField(ref _dataOperationStatus, value);
    }

    public bool IsEmojiPickerOpen
    {
        get => _isEmojiPickerOpen;
        set => SetField(ref _isEmojiPickerOpen, value);
    }

    // Recording modal state
    public bool IsRecordingHotkey
    {
        get => _isRecordingHotkey;
        private set => SetField(ref _isRecordingHotkey, value);
    }

    public string RecordingTargetLabel
    {
        get => _recordingTargetLabel;
        private set => SetField(ref _recordingTargetLabel, value);
    }

    public string RecordingPromptText
    {
        get => _recordingPromptText;
        private set => SetField(ref _recordingPromptText, value);
    }

    public string ShortcutConflictWarning
    {
        get => _shortcutConflictWarning;
        private set => SetField(ref _shortcutConflictWarning, value);
    }

    public bool HasShortcutConflict => !string.IsNullOrEmpty(ShortcutConflictWarning);

    // Telemetry state
    public EngineTelemetrySnapshot Telemetry
    {
        get => _telemetry;
        private set
        {
            if (SetField(ref _telemetry, value))
            {
                OnPropertyChanged(nameof(PauseButtonText));
                OnPropertyChanged(nameof(IsEnginePaused));
                OnPropertyChanged(nameof(IsEngineRunning));
                OnPropertyChanged(nameof(QueueCount));
            }
        }
    }

    public string PauseButtonText => Telemetry.State is EngineRunState.Paused or EngineRunState.Pausing ? "Resume" : "Pause";
    public bool IsEnginePaused => Telemetry.State == EngineRunState.Paused;
    public bool IsEngineRunning => Telemetry.State is EngineRunState.Typing or EngineRunState.Pausing or EngineRunState.Switching;
    public int QueueCount => QueueItems.Count;

    private void OnSchedulerTelemetryChanged(EngineTelemetrySnapshot snapshot)
    {
        _dispatcher.BeginInvoke(DispatcherPriority.DataBind, () =>
        {
            Telemetry = snapshot;
            QueueItems.Clear();
            foreach (var item in snapshot.QueueSnapshot)
            {
                QueueItems.Add(item);
            }
            OnPropertyChanged(nameof(QueueCount));
        });
    }

    private void OnGlobalHotkeyTriggered(HotkeyTriggeredEvent evt)
    {
        switch (evt.Kind)
        {
            case GlobalHotkeyActionKind.PauseResume:
                _scheduler.TogglePauseResume();
                break;

            case GlobalHotkeyActionKind.Stop:
                _scheduler.StopAll();
                break;

            case GlobalHotkeyActionKind.SpeedUp:
                TypingDelayMs = _settings.TypingDelayMs + 5;
                break;

            case GlobalHotkeyActionKind.SpeedDown:
                TypingDelayMs = _settings.TypingDelayMs - 5;
                break;

            case GlobalHotkeyActionKind.ScriptTrigger when evt.ScriptId.HasValue:
                var lookup = Volatile.Read(ref _enabledScriptsById);
                if (lookup.TryGetValue(evt.ScriptId.Value, out var script) && script.IsEnabled)
                {
                    _scheduler.EnqueueOrSwitch(script, evt.HookTimestampTicks);
                }
                break;
        }
    }

    private void StartSelectedOrFirstScript()
    {
        var target = SelectedScript ?? Scripts.FirstOrDefault(s => s.IsEnabled);
        if (target is not null)
        {
            _scheduler.EnqueueOrSwitch(target);
        }
    }

    private async Task AddNewScriptAsync()
    {
        int nextNum = Scripts.Count + 1;
        var item = new ScriptItem
        {
            Name = $"New Script {nextNum}",
            Content = "Type your first sentence here...\nAnd your second sentence here.",
            Category = "Custom",
            IsEnabled = true,
            SortOrder = nextNum
        };

        await _repository.UpsertScriptAsync(item);
        Scripts.Add(item);
        SelectedScript = item;
        ActiveTab = NavigationTab.ScriptEditor;
        OnPropertyChanged(nameof(TotalScriptsCount));
        OnPropertyChanged(nameof(EnabledScriptsCount));
        SaveBannerText = $"Created \"{item.Name}\"";
    }

    private async Task SaveSelectedScriptAsync()
    {
        if (SelectedScript is null) return;
        if (string.IsNullOrWhiteSpace(SelectedScript.Name))
        {
            SelectedScript.Name = "Untitled Script";
            OnPropertyChanged(nameof(EditorName));
        }

        await _repository.UpsertScriptAsync(SelectedScript);
        HasUnsavedEditorChanges = false;
        SaveBannerText = $"Saved \"{SelectedScript.Name}\" at {DateTime.Now:HH:mm:ss}";
        RefreshHookBindings();
    }

    private async Task DuplicateScriptAsync(ScriptItem? source)
    {
        if (source is null) return;

        var copy = new ScriptItem
        {
            Name = $"{source.Name} (Copy)",
            Content = source.Content,
            Hotkey = new HotkeyBinding(), // Avoid duplicate hotkey conflict
            Category = source.Category,
            IsEnabled = source.IsEnabled,
            SortOrder = Scripts.Count + 1
        };

        await _repository.UpsertScriptAsync(copy);
        Scripts.Add(copy);
        SelectedScript = copy;
        OnPropertyChanged(nameof(TotalScriptsCount));
        OnPropertyChanged(nameof(EnabledScriptsCount));
        SaveBannerText = $"Duplicated \"{source.Name}\" (assign a new shortcut if needed)";
    }

    private async Task DeleteScriptAsync(ScriptItem? target)
    {
        if (target is null) return;

        await _repository.DeleteScriptAsync(target.Id);
        int idx = Scripts.IndexOf(target);
        Scripts.Remove(target);

        if (SelectedScript == target)
        {
            SelectedScript = Scripts.Count > 0 ? Scripts[Math.Clamp(idx - 1, 0, Scripts.Count - 1)] : null;
        }

        RefreshHookBindings();
        OnPropertyChanged(nameof(TotalScriptsCount));
        OnPropertyChanged(nameof(EnabledScriptsCount));
        SaveBannerText = $"Deleted \"{target.Name}\"";
    }

    private void BeginRecording(string targetLabel, Action<HotkeyBinding> applyCallback)
    {
        _pendingApplyCallback = applyCallback;
        _pendingRecordedHotkey = null;
        RecordingTargetLabel = targetLabel;
        RecordingPromptText = "Press any key or combination (e.g. F1, A, 5, Ctrl+Shift+1)... Esc to cancel";
        ShortcutConflictWarning = string.Empty;
        IsRecordingHotkey = true;

        _hookService.BeginRecordShortcut(captured =>
        {
            _dispatcher.Invoke(() =>
            {
                if (captured is null)
                {
                    IsRecordingHotkey = false;
                    return;
                }

                _pendingRecordedHotkey = captured;
                RecordingPromptText = $"Detected: {captured.DisplayString}";
                CheckConflictForCandidate(captured, targetLabel);
                RelayCommand.RaiseCanExecuteChanged();
            });
        });
    }

    private void CheckConflictForCandidate(HotkeyBinding candidate, string targetLabel)
    {
        var conflicts = new List<string>();

        if (!string.Equals(targetLabel, "Global Pause / Resume", StringComparison.Ordinal) &&
            candidate.Equals(_settings.PauseResumeHotkey))
        {
            conflicts.Add("Global Pause/Resume");
        }

        if (!string.Equals(targetLabel, "Global Stop", StringComparison.Ordinal) &&
            candidate.Equals(_settings.StopHotkey) ||
            candidate.Equals(_settings.SpeedUpHotkey) ||
            candidate.Equals(_settings.SpeedDownHotkey))
        {
            conflicts.Add("Global Stop");
        }

        foreach (var s in Scripts)
        {
            if (targetLabel == $"Script: {s.Name}") continue;
            if (!s.Hotkey.IsEmpty && s.Hotkey.Equals(candidate))
            {
                conflicts.Add($"Script \"{s.Name}\"");
            }
        }

        ShortcutConflictWarning = conflicts.Count > 0
            ? $"Conflict detected: {candidate.DisplayString} is already assigned to {string.Join(", ", conflicts)}."
            : string.Empty;
        OnPropertyChanged(nameof(HasShortcutConflict));
    }

    private void ConfirmRecordedHotkey()
    {
        if (_pendingRecordedHotkey is null || _pendingApplyCallback is null) return;
        if (HasShortcutConflict) return; // Require resolving duplicate conflict first

        var binding = _pendingRecordedHotkey;
        var callback = _pendingApplyCallback;
        IsRecordingHotkey = false;
        _pendingRecordedHotkey = null;
        _pendingApplyCallback = null;
        callback(binding);
    }

    private void CancelRecordingHotkey()
    {
        _hookService.CancelRecording();
        IsRecordingHotkey = false;
        _pendingRecordedHotkey = null;
        _pendingApplyCallback = null;
        ShortcutConflictWarning = string.Empty;
        OnPropertyChanged(nameof(HasShortcutConflict));
    }

    private async void ApplyScriptHotkey(ScriptItem target, HotkeyBinding binding)
    {
        try
        {
            target.Hotkey = binding;
            target.NotifyHotkeyChanged();
            if (SelectedScript == target)
            {
                OnPropertyChanged(nameof(SelectedScriptHotkeyDisplay));
            }

            await _repository.UpsertScriptAsync(target);
            RefreshHookBindings();
            SaveBannerText = $"Assigned {binding.DisplayString} to \"{target.Name}\"";
        }
        catch (Exception ex)
        {
            SaveBannerText = $"Failed to save shortcut: {ex.Message}";
        }
    }

    private void ApplyPauseHotkey(HotkeyBinding binding)
    {
        _settings.PauseResumeHotkey = binding;
        OnPropertyChanged(nameof(PauseResumeHotkeyDisplay));
        RefreshHookBindings();
        ScheduleSettingsSave();
    }

    private void ApplyStopHotkey(HotkeyBinding binding)
    {
        _settings.StopHotkey = binding;
        OnPropertyChanged(nameof(StopHotkeyDisplay));
        RefreshHookBindings();
        ScheduleSettingsSave();
    }

    private void ApplySpeedUpHotkey(HotkeyBinding binding)
    {
        _settings.SpeedUpHotkey = binding;
        OnPropertyChanged(nameof(SpeedUpHotkeyDisplay));
        RefreshHookBindings();
        ScheduleSettingsSave();
    }

    private void ApplySpeedDownHotkey(HotkeyBinding binding)
    {
        _settings.SpeedDownHotkey = binding;
        OnPropertyChanged(nameof(SpeedDownHotkeyDisplay));
        RefreshHookBindings();
        ScheduleSettingsSave();
    }

    private void RefreshHookBindings()
    {
        var snapshotMap = Scripts.Where(s => s.IsEnabled).ToDictionary(s => s.Id);
        Volatile.Write(ref _enabledScriptsById, snapshotMap);
        _hookService.UpdateBindings(Scripts, _settings.PauseResumeHotkey, _settings.StopHotkey,
            _settings.SpeedUpHotkey, _settings.SpeedDownHotkey);
        OnPropertyChanged(nameof(HookStatusText));
    }

    private bool FilterScriptPredicate(object obj)
    {
        if (obj is not ScriptItem item) return false;
        if (FilterEnabledOnly && !item.IsEnabled) return false;
        if (string.IsNullOrWhiteSpace(SearchQuery)) return true;

        return item.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
               item.Category.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
               item.HotkeyDisplay.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase);
    }

    private void ScheduleAutoSave()
    {
        HasUnsavedEditorChanges = true;
        SaveBannerText = "Editing...";
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    private void ScheduleSettingsSave()
    {
        _settingsSaveTimer.Stop();
        _settingsSaveTimer.Start();
    }

    private void ApplyWindowBackdrop()
    {
        if (_hostWindow is null) return;
        var hwnd = new System.Windows.Interop.WindowInteropHelper(_hostWindow).Handle;
        if (hwnd != IntPtr.Zero)
        {
            Win32Interop.ApplyWindowChromeEffects(hwnd, _settings.BackdropMaterial);
        }
    }

    private void ApplyCaptureExclusion()
    {
        if (_hostWindow is null) return;
        var (_, message) = _captureService.ApplyCaptureExclusion(_hostWindow, _settings.ExcludeFromCapture);
        CaptureStatusNote = message;
    }

    private async Task ExportJsonAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export Scripts to JSON",
            Filter = "JSON Files (*.json)|*.json",
            FileName = $"TuSkEer_Scripts_{DateTime.Now:yyyyMMdd_HHmm}.json"
        };

        if (dialog.ShowDialog() == true)
        {
            await _repository.ExportScriptsToJsonAsync(dialog.FileName);
            DataOperationStatus = $"Exported {Scripts.Count} scripts to {dialog.FileName}";
        }
    }

    private async Task ImportJsonAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import Scripts from JSON",
            Filter = "JSON Files (*.json)|*.json"
        };

        if (dialog.ShowDialog() == true)
        {
            int count = await _repository.ImportScriptsFromJsonAsync(dialog.FileName, ImportConflictMode.CreateCopy);
            var refreshed = await _repository.GetAllScriptsAsync();
            Scripts.Clear();
            foreach (var s in refreshed) Scripts.Add(s);
            SelectedScript ??= Scripts.FirstOrDefault();
            RefreshHookBindings();
            OnPropertyChanged(nameof(TotalScriptsCount));
            OnPropertyChanged(nameof(EnabledScriptsCount));
            DataOperationStatus = $"Imported {count} scripts successfully.";
        }
    }

    private async Task BackupDatabaseAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Backup SQLite Database",
            Filter = "SQLite Database (*.db)|*.db",
            FileName = $"TuSkEer_Backup_{DateTime.Now:yyyyMMdd_HHmm}.db"
        };

        if (dialog.ShowDialog() == true)
        {
            await _repository.BackupDatabaseAsync(dialog.FileName);
            DataOperationStatus = $"Database backed up to {dialog.FileName}";
        }
    }

    private async Task RestoreDatabaseAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Restore SQLite Database Backup",
            Filter = "SQLite Database (*.db)|*.db"
        };

        if (dialog.ShowDialog() == true)
        {
            await _repository.RestoreDatabaseAsync(dialog.FileName);
            var refreshed = await _repository.GetAllScriptsAsync();
            Scripts.Clear();
            foreach (var s in refreshed) Scripts.Add(s);
            SelectedScript = Scripts.FirstOrDefault();
            _settings = await _repository.LoadSettingsAsync();
            _typingEngine.ApplyLiveSettings(_settings);
            RefreshHookBindings();
            NotifySettingsPropertiesChanged();
            OnPropertyChanged(nameof(TotalScriptsCount));
            OnPropertyChanged(nameof(EnabledScriptsCount));
            DataOperationStatus = $"Database restored from {dialog.FileName}";
        }
    }

    private void NotifySettingsPropertiesChanged()
    {
        OnPropertyChanged(nameof(TypingDelayMs));
        OnPropertyChanged(nameof(TypingSpeedLabel));
        OnPropertyChanged(nameof(RepeatFastSendEnabled));
        OnPropertyChanged(nameof(RepeatThreshold));
        OnPropertyChanged(nameof(AppendEnabled));
        OnPropertyChanged(nameof(AppendContent));
        OnPropertyChanged(nameof(UseClipboardPasteMode));
        OnPropertyChanged(nameof(AppendModeLabel));
        OnPropertyChanged(nameof(PauseResumeHotkeyDisplay));
        OnPropertyChanged(nameof(StopHotkeyDisplay));
        OnPropertyChanged(nameof(SpeedUpHotkeyDisplay));
        OnPropertyChanged(nameof(SpeedDownHotkeyDisplay));
        OnPropertyChanged(nameof(ExcludeFromCapture));
        OnPropertyChanged(nameof(MinimizeToTrayOnClose));
        OnPropertyChanged(nameof(EnableBackgroundParticles));
        OnPropertyChanged(nameof(BackdropMaterial));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose()
    {
        _autoSaveTimer.Stop();
        _settingsSaveTimer.Stop();
        _scheduler.TelemetryChanged -= OnSchedulerTelemetryChanged;
        _hookService.HotkeyTriggered -= OnGlobalHotkeyTriggered;
    }
}
