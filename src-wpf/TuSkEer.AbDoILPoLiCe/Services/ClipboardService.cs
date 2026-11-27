using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using TuSkEer.AbDoILPoLiCe.Native;

namespace TuSkEer.AbDoILPoLiCe.Services;

public sealed class ClipboardService
{
    private readonly SemaphoreSlim _clipboardGate = new(1, 1);

    public async Task PasteWithPreserveAsync(string contentToPaste, int restoreDelayMs, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(contentToPaste)) return;

        await _clipboardGate.WaitAsync(cancellationToken);
        try
        {
            Dictionary<string, object?>? savedFormats = null;

            await RunOnStaDispatcherAsync(() =>
            {
                savedFormats = CaptureClipboardSnapshot();
                TrySetClipboardText(contentToPaste);
            });

            cancellationToken.ThrowIfCancellationRequested();

            // Send Ctrl+V via SendInput
            Win32Interop.SendCtrlV();

            // Allow target window time to process WM_PASTE before restoring original clipboard
            await Task.Delay(Math.Clamp(restoreDelayMs, 15, 250), cancellationToken);

            if (savedFormats is not null && savedFormats.Count > 0)
            {
                await RunOnStaDispatcherAsync(() => RestoreClipboardSnapshot(savedFormats));
            }
        }
        finally
        {
            _clipboardGate.Release();
        }
    }

    private static Task RunOnStaDispatcherAsync(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            var tcs = new TaskCompletionSource();
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            return tcs.Task;
        }

        if (dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action).Task;
    }

    private static Dictionary<string, object?> CaptureClipboardSnapshot()
    {
        var snapshot = new Dictionary<string, object?>();
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                var dataObj = Clipboard.GetDataObject();
                if (dataObj is null) return snapshot;

                foreach (string format in dataObj.GetFormats(false))
                {
                    // Only capture safe, serializable/in-memory formats
                    if (format == DataFormats.UnicodeText ||
                        format == DataFormats.Text ||
                        format == DataFormats.Html ||
                        format == DataFormats.Rtf ||
                        format == DataFormats.CommaSeparatedValue ||
                        format == DataFormats.FileDrop)
                    {
                        try
                        {
                            object? data = dataObj.GetData(format, false);
                            if (data is not null)
                            {
                                snapshot[format] = data;
                            }
                        }
                        catch
                        {
                            // Skip inaccessible format
                        }
                    }
                }
                return snapshot;
            }
            catch
            {
                Thread.Sleep(10);
            }
        }
        return snapshot;
    }

    private static void TrySetClipboardText(string text)
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                Clipboard.SetText(text, TextDataFormat.UnicodeText);
                return;
            }
            catch
            {
                Thread.Sleep(10);
            }
        }
    }

    private static void RestoreClipboardSnapshot(Dictionary<string, object?> snapshot)
    {
        if (snapshot.Count == 0) return;

        var dataObject = new DataObject();
        foreach (var kvp in snapshot)
        {
            if (kvp.Value is not null)
            {
                dataObject.SetData(kvp.Key, kvp.Value);
            }
        }

        for (int attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(dataObject, true);
                return;
            }
            catch
            {
                Thread.Sleep(10);
            }
        }
    }
}
