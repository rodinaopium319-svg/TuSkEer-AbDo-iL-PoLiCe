using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using TuSkEer.AbDoILPoLiCe.Models;
using TuSkEer.AbDoILPoLiCe.Native;

namespace TuSkEer.AbDoILPoLiCe.Services;

public enum GlobalHotkeyActionKind
{
    ScriptTrigger = 0,
    PauseResume = 1,
    Stop = 2
}

public sealed record HotkeyTriggeredEvent(
    GlobalHotkeyActionKind Kind,
    long? ScriptId,
    HotkeyBinding Binding,
    long HookTimestampTicks);

public sealed class GlobalKeyboardHookService : IDisposable
{
    private readonly Win32Interop.LowLevelKeyboardProc _proc;
    private readonly ManualResetEventSlim _hookReadyEvent = new(false);
    private readonly object _tableLock = new();

    private Dictionary<(int Vk, HotkeyModifiers Mods), (GlobalHotkeyActionKind Kind, long? ScriptId)> _bindings = new();
    private Thread? _hookThread;
    private uint _hookThreadId;
    private IntPtr _hookHandle;
    private volatile bool _isRecording;
    private volatile bool _disposed;
    private Action<HotkeyBinding?>? _recordCallback;

    // Debounce key-repeat auto-fire while holding down a hotkey
    private readonly ConcurrentDictionary<(int Vk, HotkeyModifiers Mods), long> _lastTriggerTicks = new();
    private static readonly long RepeatDebounceTicks = Stopwatch.Frequency / 4; // 250ms debounce for physical key hold

    public event Action<HotkeyTriggeredEvent>? HotkeyTriggered;

    public bool IsHookActive => _hookHandle != IntPtr.Zero;
    public bool IsRecording => _isRecording;

    public GlobalKeyboardHookService()
    {
        _proc = HookCallback;
    }

    public void Start()
    {
        if (_hookThread is not null) return;

        _hookThread = new Thread(HookMessageLoop)
        {
            Name = "TuSkEer.GlobalKeyboardHook",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };
        _hookThread.Start();
        _hookReadyEvent.Wait(TimeSpan.FromSeconds(2));
    }

    public void UpdateBindings(
        IEnumerable<ScriptItem> scripts,
        HotkeyBinding pauseResumeHotkey,
        HotkeyBinding stopHotkey)
    {
        var next = new Dictionary<(int Vk, HotkeyModifiers Mods), (GlobalHotkeyActionKind, long?)>();

        if (pauseResumeHotkey is { IsEmpty: false })
        {
            next[(pauseResumeHotkey.VirtualKey, pauseResumeHotkey.Modifiers)] = (GlobalHotkeyActionKind.PauseResume, null);
        }

        if (stopHotkey is { IsEmpty: false })
        {
            next[(stopHotkey.VirtualKey, stopHotkey.Modifiers)] = (GlobalHotkeyActionKind.Stop, null);
        }

        foreach (var script in scripts)
        {
            if (!script.IsEnabled || script.Hotkey.IsEmpty) continue;
            var key = (script.Hotkey.VirtualKey, script.Hotkey.Modifiers);
            next.TryAdd(key, (GlobalHotkeyActionKind.ScriptTrigger, script.Id));
        }

        lock (_tableLock)
        {
            _bindings = next;
        }
    }

    public void BeginRecordShortcut(Action<HotkeyBinding?> onCaptured)
    {
        _recordCallback = onCaptured;
        _isRecording = true;
    }

    public void CancelRecording()
    {
        _isRecording = false;
        var cb = Interlocked.Exchange(ref _recordCallback, null);
        cb?.Invoke(null);
    }

    private void HookMessageLoop()
    {
        _hookThreadId = Win32Interop.GetCurrentThreadId();
        using var currentProcess = Process.GetCurrentProcess();
        using var mainModule = currentProcess.MainModule;
        IntPtr moduleHandle = Win32Interop.GetModuleHandle(mainModule?.ModuleName);

        _hookHandle = Win32Interop.SetWindowsHookEx(Win32Interop.WH_KEYBOARD_LL, _proc, moduleHandle, 0);
        _hookReadyEvent.Set();

        if (_hookHandle == IntPtr.Zero) return;

        while (Win32Interop.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            Win32Interop.TranslateMessage(ref msg);
            Win32Interop.DispatchMessage(ref msg);
        }

        if (_hookHandle != IntPtr.Zero)
        {
            Win32Interop.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && !_disposed)
        {
            int msg = wParam.ToInt32();
            bool isKeyDown = msg == Win32Interop.WM_KEYDOWN || msg == Win32Interop.WM_SYSKEYDOWN;
            bool isKeyUp = msg == Win32Interop.WM_KEYUP || msg == Win32Interop.WM_SYSKEYUP;

            if (isKeyDown || isKeyUp)
            {
                var info = Marshal.PtrToStructure<Win32Interop.KBDLLHOOKSTRUCT>(lParam);

                // Ignore our own SendInput keystrokes immediately (zero self-triggering)
                if ((info.flags & Win32Interop.LLKHF_INJECTED) != 0 ||
                    (info.flags & Win32Interop.LLKHF_LOWER_IL_INJECTED) != 0)
                {
                    return Win32Interop.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
                }

                int vk = (int)info.vkCode;
                var mods = ReadCurrentModifiers();

                if (isKeyUp)
                {
                    _lastTriggerTicks.TryRemove((vk, mods), out _);
                    return Win32Interop.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
                }

                // Recording Mode intercepts the next non-modifier key cleanly
                if (_isRecording)
                {
                    if (vk == 0x1B) // ESC cancels recording
                    {
                        CancelRecording();
                        return new IntPtr(1);
                    }

                    if (!HotkeyBinding.IsModifierVirtualKey(vk) && HotkeyBinding.IsSupportedKey(vk))
                    {
                        _isRecording = false;
                        var captured = new HotkeyBinding(vk, mods);
                        var cb = Interlocked.Exchange(ref _recordCallback, null);
                        ThreadPool.UnsafeQueueUserWorkItem(_ => cb?.Invoke(captured), null);
                        return new IntPtr(1);
                    }

                    return Win32Interop.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
                }

                // Fast dictionary lookup without blocking the hook thread
                (GlobalHotkeyActionKind Kind, long? ScriptId) match;
                bool found;
                lock (_tableLock)
                {
                    found = _bindings.TryGetValue((vk, mods), out match);
                }

                if (found)
                {
                    long now = Stopwatch.GetTimestamp();
                    var dictKey = (vk, mods);
                    if (_lastTriggerTicks.TryGetValue(dictKey, out long lastTicks) && (now - lastTicks) < RepeatDebounceTicks)
                    {
                        return new IntPtr(1);
                    }

                    _lastTriggerTicks[dictKey] = now;
                    var evt = new HotkeyTriggeredEvent(match.Kind, match.ScriptId, new HotkeyBinding(vk, mods), now);

                    // Dispatch asynchronously off the hook callback so Windows never unhooks due to latency
                    ThreadPool.UnsafeQueueUserWorkItem(state =>
                    {
                        var payload = (HotkeyTriggeredEvent)state!;
                        HotkeyTriggered?.Invoke(payload);
                    }, evt);

                    // Suppress matched global hotkey from leaking into foreground app
                    return new IntPtr(1);
                }
            }
        }

        return Win32Interop.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private static HotkeyModifiers ReadCurrentModifiers()
    {
        HotkeyModifiers mods = HotkeyModifiers.None;
        if (Win32Interop.IsKeyDownAsync(Win32Interop.VK_CONTROL)) mods |= HotkeyModifiers.Ctrl;
        if (Win32Interop.IsKeyDownAsync(Win32Interop.VK_MENU)) mods |= HotkeyModifiers.Alt;
        if (Win32Interop.IsKeyDownAsync(Win32Interop.VK_SHIFT)) mods |= HotkeyModifiers.Shift;
        return mods;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _isRecording = false;

        if (_hookThreadId != 0)
        {
            Win32Interop.PostThreadMessage(_hookThreadId, Win32Interop.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        _hookThread?.Join(500);
        _hookReadyEvent.Dispose();
    }
}
