using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TuSkEer.AbDoILPoLiCe.Models;

namespace TuSkEer.AbDoILPoLiCe.Services;

public sealed class ScriptQueueScheduler : IDisposable
{
    private readonly TypingEngine _typingEngine;
    private readonly object _stateLock = new();

    // Suspended parent scripts (LIFO stack so interrupted Script A resumes right after interrupting Script B finishes)
    private readonly Stack<QueuedScriptSession> _suspendedStack = new();

    // Deterministic priority queue of incoming scripts waiting to run before resuming suspended parents
    private readonly List<QueuedScriptSession> _pendingQueue = new();

    private QueuedScriptSession? _activeSession;
    private CancellationTokenSource _stopCts = new();
    private TaskCompletionSource<bool>? _pauseResumeSignal;
    private Task? _workerTask;

    private EngineRunState _runState = EngineRunState.Ready;
    private string _statusMessage = "Ready to type...";
    private double _lastHookLatencyMs;
    private double _lastSentenceDurationMs;
    private long _sequenceCounter;
    private bool _pauseAfterCurrentSentence;
    private bool _preemptAfterCurrentSentence;
    private bool _disposed;

    public event Action<EngineTelemetrySnapshot>? TelemetryChanged;

    public ScriptQueueScheduler(TypingEngine typingEngine)
    {
        _typingEngine = typingEngine;
    }

    public EngineTelemetrySnapshot GetSnapshot()
    {
        lock (_stateLock)
        {
            return BuildSnapshotLocked();
        }
    }

    public void EnqueueOrSwitch(ScriptItem script, long hookTimestampTicks = 0, int priority = 10)
    {
        if (_disposed) return;

        var sentences = ScriptItem.SplitSentences(script.Content);
        if (sentences.Length == 0)
        {
            UpdateStatus(EngineRunState.Ready, $"Script \"{script.Name}\" has no sentences to send.");
            return;
        }

        double hookLatencyMs = hookTimestampTicks > 0
            ? Math.Max(0, (Stopwatch.GetTimestamp() - hookTimestampTicks) * 1000.0 / Stopwatch.Frequency)
            : 0;

        lock (_stateLock)
        {
            _lastHookLatencyMs = hookLatencyMs;

            // Prevent duplicate queuing if the exact same script is already active or pending
            if (_activeSession is not null && _activeSession.ScriptId == script.Id && !_preemptAfterCurrentSentence)
            {
                if (_runState == EngineRunState.Paused)
                {
                    ResumeLocked();
                }
                return;
            }

            if (_pendingQueue.Any(q => q.ScriptId == script.Id))
            {
                return;
            }

            var newSession = new QueuedScriptSession
            {
                ScriptId = script.Id,
                ScriptName = script.Name,
                Sentences = sentences,
                NextSentenceIndex = 0,
                SequenceNumber = Interlocked.Increment(ref _sequenceCounter),
                Priority = priority,
                EnqueuedAtUtc = DateTime.UtcNow,
                IsSuspended = false
            };

            if (_activeSession is null)
            {
                // Idle: start immediately
                _activeSession = newSession;
                _pauseAfterCurrentSentence = false;
                _preemptAfterCurrentSentence = false;
                _runState = EngineRunState.Typing;
                _statusMessage = $"Typing {newSession.ScriptName}...";
                EnsureWorkerStartedLocked();
            }
            else if (_runState == EngineRunState.Paused)
            {
                // Active script is already paused cleanly at a sentence boundary -> suspend it and run new script now
                if (_activeSession.NextSentenceIndex < _activeSession.TotalSentences)
                {
                    _activeSession.IsSuspended = true;
                    _suspendedStack.Push(_activeSession);
                }

                _activeSession = newSession;
                _pauseAfterCurrentSentence = false;
                _preemptAfterCurrentSentence = false;
                _runState = EngineRunState.Typing;
                _statusMessage = $"Switched to {newSession.ScriptName}...";
                ResumeLocked();
            }
            else
            {
                // Script A is currently typing a sentence -> finish current sentence, send Enter, suspend A, start B!
                InsertPendingByPriorityLocked(newSession);
                _preemptAfterCurrentSentence = true;
                _runState = EngineRunState.Switching;
                _statusMessage = $"Finishing sentence, then switching to {newSession.ScriptName}...";
            }

            PublishTelemetryLocked();
        }
    }

    public void TogglePauseResume()
    {
        if (_disposed) return;

        lock (_stateLock)
        {
            if (_activeSession is null)
            {
                return;
            }

            if (_runState == EngineRunState.Paused)
            {
                ResumeLocked();
                PublishTelemetryLocked();
                return;
            }

            if (_runState is EngineRunState.Typing or EngineRunState.Switching)
            {
                _pauseAfterCurrentSentence = true;
                _runState = EngineRunState.Pausing;
                _statusMessage = $"Pausing after current sentence ({_activeSession.ScriptName})...";
                PublishTelemetryLocked();
            }
            else if (_runState == EngineRunState.Pausing)
            {
                // Cancel pending pause before sentence completes
                _pauseAfterCurrentSentence = false;
                _runState = EngineRunState.Typing;
                _statusMessage = $"Typing {_activeSession.ScriptName}...";
                PublishTelemetryLocked();
            }
        }
    }

    public void StopAll()
    {
        if (_disposed) return;

        lock (_stateLock)
        {
            _pauseAfterCurrentSentence = false;
            _preemptAfterCurrentSentence = false;
            _activeSession = null;
            _suspendedStack.Clear();
            _pendingQueue.Clear();

            try
            {
                _stopCts.Cancel();
            }
            catch
            {
            }

            _pauseResumeSignal?.TrySetResult(false);
            _pauseResumeSignal = null;

            _stopCts.Dispose();
            _stopCts = new CancellationTokenSource();

            _runState = EngineRunState.Stopped;
            _statusMessage = "Stopped. Queue cleared.";
            PublishTelemetryLocked();

            _runState = EngineRunState.Ready;
            _statusMessage = "Ready to type...";
            PublishTelemetryLocked();
        }
    }

    private void InsertPendingByPriorityLocked(QueuedScriptSession session)
    {
        _pendingQueue.Add(session);
        _pendingQueue.Sort((a, b) =>
        {
            int cmp = b.Priority.CompareTo(a.Priority);
            return cmp != 0 ? cmp : a.SequenceNumber.CompareTo(b.SequenceNumber);
        });
    }

    private void ResumeLocked()
    {
        _pauseAfterCurrentSentence = false;
        if (_activeSession is not null)
        {
            _runState = EngineRunState.Typing;
            _statusMessage = $"Resuming {_activeSession.ScriptName} (sentence {_activeSession.NextSentenceIndex + 1}/{_activeSession.TotalSentences})...";
        }
        _pauseResumeSignal?.TrySetResult(true);
        _pauseResumeSignal = null;
    }

    private void EnsureWorkerStartedLocked()
    {
        if (_workerTask is { IsCompleted: false }) return;

        var token = _stopCts.Token;
        _workerTask = Task.Run(() => WorkerLoopAsync(token), token);
    }

    private async Task WorkerLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            QueuedScriptSession? session;
            string sentenceToType;

            lock (_stateLock)
            {
                session = _activeSession;
                if (session is null)
                {
                    if (!TryPromoteNextSessionLocked())
                    {
                        _runState = EngineRunState.Ready;
                        _statusMessage = "Ready to type...";
                        PublishTelemetryLocked();
                        return;
                    }
                    session = _activeSession!;
                }

                if (session.NextSentenceIndex >= session.TotalSentences)
                {
                    _activeSession = null;
                    continue;
                }

                sentenceToType = session.Sentences[session.NextSentenceIndex];
                _runState = _pauseAfterCurrentSentence
                    ? EngineRunState.Pausing
                    : (_preemptAfterCurrentSentence ? EngineRunState.Switching : EngineRunState.Typing);
                _statusMessage = $"Typing {session.ScriptName} ({session.NextSentenceIndex + 1}/{session.TotalSentences})...";
                PublishTelemetryLocked();
            }

            try
            {
                double elapsedMs = await _typingEngine.TypeSentenceAndSendAsync(sentenceToType, token).ConfigureAwait(false);
                lock (_stateLock)
                {
                    _lastSentenceDurationMs = elapsedMs;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                lock (_stateLock)
                {
                    _activeSession = null;
                    _suspendedStack.Clear();
                    _pendingQueue.Clear();
                    _runState = EngineRunState.Ready;
                    _statusMessage = $"Engine error: {ex.Message}";
                    PublishTelemetryLocked();
                }
                return;
            }

            TaskCompletionSource<bool>? pauseWaiter = null;

            lock (_stateLock)
            {
                if (token.IsCancellationRequested || _activeSession is null)
                {
                    return;
                }

                // Advance exact sentence position after Enter was sent
                _activeSession.NextSentenceIndex++;

                bool finishedCurrentScript = _activeSession.NextSentenceIndex >= _activeSession.TotalSentences;

                // Priority Preemption: suspend Script A and start Script B immediately
                if (_preemptAfterCurrentSentence && _pendingQueue.Count > 0)
                {
                    if (!finishedCurrentScript)
                    {
                        _activeSession.IsSuspended = true;
                        _suspendedStack.Push(_activeSession);
                    }

                    var nextScript = _pendingQueue[0];
                    _pendingQueue.RemoveAt(0);
                    nextScript.IsSuspended = false;
                    _activeSession = nextScript;
                    _preemptAfterCurrentSentence = _pendingQueue.Count > 0;
                    _runState = EngineRunState.Typing;
                    _statusMessage = $"Running priority script {nextScript.ScriptName}...";
                    PublishTelemetryLocked();
                    continue;
                }

                _preemptAfterCurrentSentence = false;

                if (finishedCurrentScript)
                {
                    string completedName = _activeSession.ScriptName;
                    _activeSession = null;
                    if (TryPromoteNextSessionLocked())
                    {
                        _runState = EngineRunState.Typing;
                        _statusMessage = $"Completed {completedName}. Resuming {_activeSession!.ScriptName}...";
                        PublishTelemetryLocked();
                        continue;
                    }
                    else
                    {
                        _runState = EngineRunState.Ready;
                        _statusMessage = $"Completed {completedName}. Ready to type...";
                        PublishTelemetryLocked();
                        return;
                    }
                }

                // Pause cleanly at sentence boundary if requested
                if (_pauseAfterCurrentSentence)
                {
                    _pauseAfterCurrentSentence = false;
                    _runState = EngineRunState.Paused;
                    _statusMessage = $"Paused {_activeSession.ScriptName} at sentence {_activeSession.NextSentenceIndex + 1}/{_activeSession.TotalSentences}";
                    _pauseResumeSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    pauseWaiter = _pauseResumeSignal;
                    PublishTelemetryLocked();
                }
                else
                {
                    PublishTelemetryLocked();
                }
            }

            if (pauseWaiter is not null)
            {
                try
                {
                    bool shouldContinue = await pauseWaiter.Task.WaitAsync(token).ConfigureAwait(false);
                    if (!shouldContinue) return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private bool TryPromoteNextSessionLocked()
    {
        if (_pendingQueue.Count > 0)
        {
            var next = _pendingQueue[0];
            _pendingQueue.RemoveAt(0);
            next.IsSuspended = false;
            _activeSession = next;
            return true;
        }

        if (_suspendedStack.Count > 0)
        {
            var resumed = _suspendedStack.Pop();
            resumed.IsSuspended = false;
            _activeSession = resumed;
            return true;
        }

        return false;
    }

    private void UpdateStatus(EngineRunState state, string message)
    {
        lock (_stateLock)
        {
            _runState = state;
            _statusMessage = message;
            PublishTelemetryLocked();
        }
    }

    private void PublishTelemetryLocked()
    {
        var snapshot = BuildSnapshotLocked();
        TelemetryChanged?.Invoke(snapshot);
    }

    private EngineTelemetrySnapshot BuildSnapshotLocked()
    {
        var queueList = new List<QueuedScriptSession>(1 + _pendingQueue.Count + _suspendedStack.Count);
        if (_activeSession is not null)
        {
            queueList.Add(CloneSession(_activeSession));
        }

        foreach (var pending in _pendingQueue)
        {
            queueList.Add(CloneSession(pending));
        }

        foreach (var suspended in _suspendedStack)
        {
            queueList.Add(CloneSession(suspended));
        }

        string preview = string.Empty;
        if (_activeSession is not null && _activeSession.NextSentenceIndex < _activeSession.TotalSentences)
        {
            preview = _activeSession.Sentences[_activeSession.NextSentenceIndex];
        }

        return new EngineTelemetrySnapshot
        {
            State = _runState,
            StatusMessage = _statusMessage,
            ActiveScriptId = _activeSession?.ScriptId,
            ActiveScriptName = _activeSession?.ScriptName ?? "None",
            CurrentSentenceNumber = _activeSession is not null
                ? Math.Min(_activeSession.NextSentenceIndex + 1, _activeSession.TotalSentences)
                : 0,
            TotalSentences = _activeSession?.TotalSentences ?? 0,
            CurrentSentencePreview = preview,
            CurrentDelayMs = _typingEngine.CurrentDelayMs,
            LastHookLatencyMs = _lastHookLatencyMs,
            LastSentenceDurationMs = _lastSentenceDurationMs,
            TotalCharactersTyped = _typingEngine.TotalCharactersTyped,
            TotalSentencesSent = _typingEngine.TotalSentencesSent,
            QueueSnapshot = queueList
        };
    }

    private static QueuedScriptSession CloneSession(QueuedScriptSession s) => new()
    {
        ScriptId = s.ScriptId,
        ScriptName = s.ScriptName,
        Sentences = s.Sentences,
        NextSentenceIndex = s.NextSentenceIndex,
        SequenceNumber = s.SequenceNumber,
        Priority = s.Priority,
        EnqueuedAtUtc = s.EnqueuedAtUtc,
        IsSuspended = s.IsSuspended
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _stopCts.Cancel();
            _pauseResumeSignal?.TrySetResult(false);
            _stopCts.Dispose();
        }
        catch
        {
        }
    }
}
