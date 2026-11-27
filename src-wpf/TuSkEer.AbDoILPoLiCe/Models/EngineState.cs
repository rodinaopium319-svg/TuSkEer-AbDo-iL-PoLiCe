using System;
using System.Collections.Generic;

namespace TuSkEer.AbDoILPoLiCe.Models;

public enum EngineRunState
{
    Ready = 0,
    Typing = 1,
    Pausing = 2,
    Paused = 3,
    Switching = 4,
    Stopped = 5
}

public sealed class QueuedScriptSession
{
    public long ScriptId { get; set; }
    public string ScriptName { get; set; } = string.Empty;
    public string[] Sentences { get; set; } = Array.Empty<string>();
    public int NextSentenceIndex { get; set; }
    public long SequenceNumber { get; set; }
    public int Priority { get; set; }
    public DateTime EnqueuedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsSuspended { get; set; }

    public int TotalSentences => Sentences.Length;
    public int RemainingSentences => Math.Max(0, Sentences.Length - NextSentenceIndex);
    public string ProgressLabel => $"{Math.Min(NextSentenceIndex + (IsSuspended ? 0 : 1), TotalSentences)}/{TotalSentences}";
}

public sealed class EngineTelemetrySnapshot
{
    public EngineRunState State { get; init; } = EngineRunState.Ready;
    public string StatusMessage { get; init; } = "Ready to type...";
    public long? ActiveScriptId { get; init; }
    public string ActiveScriptName { get; init; } = "None";
    public int CurrentSentenceNumber { get; init; }
    public int TotalSentences { get; init; }
    public string CurrentSentencePreview { get; init; } = string.Empty;
    public int CurrentDelayMs { get; init; }
    public double LastHookLatencyMs { get; init; }
    public double LastSentenceDurationMs { get; init; }
    public long TotalCharactersTyped { get; init; }
    public long TotalSentencesSent { get; init; }
    public IReadOnlyList<QueuedScriptSession> QueueSnapshot { get; init; } = Array.Empty<QueuedScriptSession>();
}
