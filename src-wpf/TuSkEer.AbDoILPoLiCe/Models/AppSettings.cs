using System;

namespace TuSkEer.AbDoILPoLiCe.Models;

public enum AppendPasteMode
{
    ClipboardPreserve = 0,
    UnicodeSendInput = 1
}

public enum BackdropMaterialMode
{
    Mica = 0,
    Acrylic = 1,
    MidnightGlass = 2
}

public sealed class AppSettings
{
    public int TypingDelayMs { get; set; } = 35;

    public bool RepeatFastSendEnabled { get; set; } = true;

    public int RepeatThreshold { get; set; } = 5;

    public bool AppendEnabled { get; set; } = false;

    public string AppendContent { get; set; } = " 🔥❤️";

    public AppendPasteMode AppendPasteMode { get; set; } = AppendPasteMode.ClipboardPreserve;

    public HotkeyBinding PauseResumeHotkey { get; set; } = new(0x71, HotkeyModifiers.None); // F2

    public HotkeyBinding StopHotkey { get; set; } = new(0x72, HotkeyModifiers.None); // F3

    public HotkeyBinding SpeedUpHotkey { get; set; } = new(); // unassigned by default; raises typing delay +5 ms

    public HotkeyBinding SpeedDownHotkey { get; set; } = new(); // unassigned by default; lowers typing delay -5 ms

    public bool ExcludeFromCapture { get; set; } = false;

    public bool MinimizeToTrayOnClose { get; set; } = true;

    public bool EnableBackgroundParticles { get; set; } = true;

    public BackdropMaterialMode BackdropMaterial { get; set; } = BackdropMaterialMode.Acrylic;

    public int ClipboardRestoreDelayMs { get; set; } = 35;

    public AppSettings Clone() => new()
    {
        TypingDelayMs = Math.Clamp(TypingDelayMs, 0, 1000),
        RepeatFastSendEnabled = RepeatFastSendEnabled,
        RepeatThreshold = Math.Clamp(RepeatThreshold, 2, 100),
        AppendEnabled = AppendEnabled,
        AppendContent = AppendContent ?? string.Empty,
        AppendPasteMode = AppendPasteMode,
        PauseResumeHotkey = PauseResumeHotkey?.Clone() ?? new HotkeyBinding(),
        StopHotkey = StopHotkey?.Clone() ?? new HotkeyBinding(),
        SpeedUpHotkey = SpeedUpHotkey?.Clone() ?? new HotkeyBinding(),
        SpeedDownHotkey = SpeedDownHotkey?.Clone() ?? new HotkeyBinding(),
        ExcludeFromCapture = ExcludeFromCapture,
        MinimizeToTrayOnClose = MinimizeToTrayOnClose,
        EnableBackgroundParticles = EnableBackgroundParticles,
        BackdropMaterial = BackdropMaterial,
        ClipboardRestoreDelayMs = Math.Clamp(ClipboardRestoreDelayMs, 15, 250)
    };
}
