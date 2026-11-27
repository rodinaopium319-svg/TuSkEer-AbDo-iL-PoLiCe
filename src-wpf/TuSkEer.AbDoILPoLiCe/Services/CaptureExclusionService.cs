using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using TuSkEer.AbDoILPoLiCe.Native;

namespace TuSkEer.AbDoILPoLiCe.Services;

public sealed class CaptureExclusionService
{
    public bool IsExcludeFromCaptureSupportedByOs
    {
        get
        {
            // WDA_EXCLUDEFROMCAPTURE requires Windows 10 Version 2004 (build 19041) or Windows 11
            var version = Environment.OSVersion.Version;
            return version.Major > 10 || (version.Major == 10 && version.Build >= 19041);
        }
    }

    public (bool Success, string Message) ApplyCaptureExclusion(Window window, bool exclude)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return (false, "Window handle is not initialized yet.");
        }

        if (!exclude)
        {
            bool cleared = Win32Interop.SetWindowDisplayAffinity(handle, Win32Interop.WDA_NONE);
            return cleared
                ? (true, "Screen capture exclusion is disabled (normal visibility).")
                : (false, $"Unable to clear display affinity (Win32 error {Marshal.GetLastWin32Error()}).");
        }

        if (!IsExcludeFromCaptureSupportedByOs)
        {
            return (false, $"WDA_EXCLUDEFROMCAPTURE requires Windows 10 2004+ (build 19041). Current build: {Environment.OSVersion.Version.Build}.");
        }

        bool ok = Win32Interop.SetWindowDisplayAffinity(handle, Win32Interop.WDA_EXCLUDEFROMCAPTURE);
        if (ok)
        {
            return (true, "WDA_EXCLUDEFROMCAPTURE active. Standard Windows/DXGI capture tools exclude this window; kernel or older capture drivers may vary.");
        }

        int err = Marshal.GetLastWin32Error();
        return (false, $"SetWindowDisplayAffinity failed with Win32 error {err}.");
    }
}
