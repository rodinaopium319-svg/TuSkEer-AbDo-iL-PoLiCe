using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Windows.Input;

namespace TuSkEer.AbDoILPoLiCe.Models;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4
}

public sealed class HotkeyBinding : IEquatable<HotkeyBinding>
{
    public int VirtualKey { get; set; }
    public HotkeyModifiers Modifiers { get; set; }

    public HotkeyBinding()
    {
    }

    public HotkeyBinding(int virtualKey, HotkeyModifiers modifiers = HotkeyModifiers.None)
    {
        VirtualKey = virtualKey;
        Modifiers = modifiers;
    }

    [JsonIgnore]
    public bool IsEmpty => VirtualKey <= 0;

    [JsonIgnore]
    public string DisplayString => Format(VirtualKey, Modifiers);

    public HotkeyBinding Clone() => new(VirtualKey, Modifiers);

    public bool Matches(int virtualKey, HotkeyModifiers modifiers)
    {
        if (IsEmpty) return false;
        return VirtualKey == virtualKey && Modifiers == modifiers;
    }

    public bool Equals(HotkeyBinding? other)
    {
        if (other is null) return false;
        if (IsEmpty && other.IsEmpty) return true;
        return VirtualKey == other.VirtualKey && Modifiers == other.Modifiers;
    }

    public override bool Equals(object? obj) => Equals(obj as HotkeyBinding);

    public override int GetHashCode() => IsEmpty ? 0 : HashCode.Combine(VirtualKey, Modifiers);

    public override string ToString() => DisplayString;

    public static string Format(int virtualKey, HotkeyModifiers modifiers)
    {
        if (virtualKey <= 0) return "None";

        var parts = new List<string>(4);
        if (modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        parts.Add(VirtualKeyToName(virtualKey));
        return string.Join(" + ", parts);
    }

    public static bool IsModifierVirtualKey(int vk) => vk is
        0x10 or 0xA0 or 0xA1 or // Shift, LShift, RShift
        0x11 or 0xA2 or 0xA3 or // Ctrl, LCtrl, RCtrl
        0x12 or 0xA4 or 0xA5 or // Alt, LAlt, RAlt
        0x5B or 0x5C;           // LWin, RWin

    public static bool IsSupportedKey(int vk)
    {
        if (IsModifierVirtualKey(vk)) return false;
        if (vk is >= 0x30 and <= 0x39) return true; // 0-9
        if (vk is >= 0x41 and <= 0x5A) return true; // A-Z
        if (vk is >= 0x70 and <= 0x87) return true; // F1-F24
        if (vk is >= 0x60 and <= 0x6F) return true; // NumPad0-9 + operators
        if (vk is >= 0x21 and <= 0x2E) return true; // PgUp, PgDn, End, Home, Arrows, Print, Ins, Del
        if (vk is 0x20 or 0x09 or 0x14 or 0x90 or 0x91 or 0x13) return true; // Space, Tab, Caps, Num, Scroll, Pause
        if (vk is >= 0xBA and <= 0xC0 or >= 0xDB and <= 0xDF or 0xE2) return true; // OEM punctuation keys
        return false;
    }

    public static string VirtualKeyToName(int vk)
    {
        if (vk is >= 0x30 and <= 0x39) return ((char)vk).ToString();
        if (vk is >= 0x41 and <= 0x5A) return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87) return $"F{vk - 0x6F}";
        if (vk is >= 0x60 and <= 0x69) return $"NumPad {vk - 0x60}";

        return vk switch
        {
            0x6A => "NumPad *",
            0x6B => "NumPad +",
            0x6C => "NumPad Enter",
            0x6D => "NumPad -",
            0x6E => "NumPad .",
            0x6F => "NumPad /",
            0x21 => "Page Up",
            0x22 => "Page Down",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2C => "Print Screen",
            0x2D => "Insert",
            0x2E => "Delete",
            0x20 => "Space",
            0x09 => "Tab",
            0x13 => "Pause",
            0x14 => "Caps Lock",
            0x90 => "Num Lock",
            0x91 => "Scroll Lock",
            0xBA => ";",
            0xBB => "=",
            0xBC => ",",
            0xBD => "-",
            0xBE => ".",
            0xBF => "/",
            0xC0 => "`",
            0xDB => "[",
            0xDC => "\\",
            0xDD => "]",
            0xDE => "'",
            _ => KeyInterop.KeyFromVirtualKey(vk).ToString()
        };
    }
}
