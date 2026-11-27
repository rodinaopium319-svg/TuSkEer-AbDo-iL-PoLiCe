using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TuSkEer.AbDoILPoLiCe.Models;
using TuSkEer.AbDoILPoLiCe.Native;

namespace TuSkEer.AbDoILPoLiCe.Services;

public sealed class TypingEngine
{
    private readonly ClipboardService _clipboardService;

    private int _typingDelayMs = 35;
    private volatile bool _repeatFastSendEnabled = true;
    private int _repeatThreshold = 5;
    private volatile bool _appendEnabled;
    private volatile string _appendContent = string.Empty;
    private int _appendPasteMode = (int)AppendPasteMode.ClipboardPreserve;
    private int _clipboardRestoreDelayMs = 35;

    private long _totalCharactersTyped;
    private long _totalSentencesSent;

    public TypingEngine(ClipboardService clipboardService)
    {
        _clipboardService = clipboardService;
    }

    public int CurrentDelayMs => Volatile.Read(ref _typingDelayMs);
    public long TotalCharactersTyped => Interlocked.Read(ref _totalCharactersTyped);
    public long TotalSentencesSent => Interlocked.Read(ref _totalSentencesSent);

    public void ApplyLiveSettings(AppSettings settings)
    {
        Volatile.Write(ref _typingDelayMs, Math.Clamp(settings.TypingDelayMs, 0, 1000));
        _repeatFastSendEnabled = settings.RepeatFastSendEnabled;
        Volatile.Write(ref _repeatThreshold, Math.Clamp(settings.RepeatThreshold, 2, 100));
        _appendEnabled = settings.AppendEnabled;
        _appendContent = settings.AppendContent ?? string.Empty;
        Volatile.Write(ref _appendPasteMode, (int)settings.AppendPasteMode);
        Volatile.Write(ref _clipboardRestoreDelayMs, Math.Clamp(settings.ClipboardRestoreDelayMs, 15, 250));
    }

    public async Task<double> TypeSentenceAndSendAsync(string sentence, CancellationToken cancellationToken)
    {
        long startTicks = Stopwatch.GetTimestamp();

        // Release any physical modifier keys from the triggering hotkey so SendInput stays clean
        Win32Interop.ReleaseActiveModifiers();

        var elements = SegmentTextElements(sentence);
        int count = elements.Count;
        int index = 0;

        while (index < count)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool repeatEnabled = _repeatFastSendEnabled;
            int threshold = Math.Max(2, Volatile.Read(ref _repeatThreshold));

            if (repeatEnabled)
            {
                string current = elements[index];
                int runEnd = index + 1;
                while (runEnd < count && string.Equals(elements[runEnd], current, StringComparison.Ordinal))
                {
                    runEnd++;
                }

                int runLength = runEnd - index;
                if (runLength >= threshold)
                {
                    var sb = new StringBuilder(current.Length * runLength);
                    for (int k = 0; k < runLength; k++)
                    {
                        sb.Append(current);
                    }

                    Win32Interop.SendUnicodeBatch(sb.ToString());
                    Interlocked.Add(ref _totalCharactersTyped, runLength);
                    index = runEnd;

                    int postRunDelay = Volatile.Read(ref _typingDelayMs);
                    if (postRunDelay > 0 && index < count)
                    {
                        await WaitPrecisionDelayAsync(postRunDelay, cancellationToken);
                    }
                    continue;
                }
            }

            // Standard character-by-character / grapheme typing
            Win32Interop.SendUnicodeTextElement(elements[index]);
            Interlocked.Increment(ref _totalCharactersTyped);
            index++;

            int delayMs = Volatile.Read(ref _typingDelayMs);
            if (delayMs > 0 && index < count)
            {
                await WaitPrecisionDelayAsync(delayMs, cancellationToken);
            }
        }

        // Optional APPEND CONTENT at end of sentence
        if (_appendEnabled)
        {
            string appendText = _appendContent;
            if (!string.IsNullOrEmpty(appendText))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pasteMode = (AppendPasteMode)Volatile.Read(ref _appendPasteMode);
                if (pasteMode == AppendPasteMode.ClipboardPreserve)
                {
                    int restoreDelay = Volatile.Read(ref _clipboardRestoreDelayMs);
                    await _clipboardService.PasteWithPreserveAsync(appendText, restoreDelay, cancellationToken);
                }
                else
                {
                    Win32Interop.SendUnicodeBatch(appendText);
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Send completed sentence immediately via Enter
        Win32Interop.SendEnterKey();
        Interlocked.Increment(ref _totalSentencesSent);

        long endTicks = Stopwatch.GetTimestamp();
        return (endTicks - startTicks) * 1000.0 / Stopwatch.Frequency;
    }

    private static List<string> SegmentTextElements(string text)
    {
        var list = new List<string>(text.Length);
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            list.Add(enumerator.GetTextElement());
        }
        return list;
    }

    private static async Task WaitPrecisionDelayAsync(int delayMs, CancellationToken cancellationToken)
    {
        if (delayMs <= 0) return;

        long targetTicks = Stopwatch.GetTimestamp() + (long)(delayMs * (Stopwatch.Frequency / 1000.0));

        // Windows Task.Delay relies on the ~15.6ms system timer tick; yield asynchronously only for the coarse part
        if (delayMs > 18)
        {
            await Task.Delay(delayMs - 15, cancellationToken).ConfigureAwait(false);
        }

        // High-resolution tail wait for sub-millisecond accuracy without freezing the UI thread
        while (Stopwatch.GetTimestamp() < targetTicks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long remainingTicks = targetTicks - Stopwatch.GetTimestamp();
            double remainingMs = remainingTicks * 1000.0 / Stopwatch.Frequency;

            if (remainingMs > 2.0)
            {
                Thread.Sleep(1);
            }
            else
            {
                Thread.SpinWait(64);
            }
        }
    }
}
