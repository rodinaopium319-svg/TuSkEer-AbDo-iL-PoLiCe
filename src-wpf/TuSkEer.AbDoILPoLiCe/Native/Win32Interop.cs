using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace TuSkEer.AbDoILPoLiCe.Native;

internal static class Win32Interop
{
    // Keyboard Hook Constants
    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;
    public const uint LLKHF_INJECTED = 0x00000010;
    public const uint LLKHF_LOWER_IL_INJECTED = 0x00000002;

    // SendInput Constants
    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_UNICODE = 0x0004;
    public const uint KEYEVENTF_SCANCODE = 0x0008;

    public const ushort VK_SHIFT = 0x10;
    public const ushort VK_CONTROL = 0x11;
    public const ushort VK_MENU = 0x12; // Alt
    public const ushort VK_LSHIFT = 0xA0;
    public const ushort VK_RSHIFT = 0xA1;
    public const ushort VK_LCONTROL = 0xA2;
    public const ushort VK_RCONTROL = 0xA3;
    public const ushort VK_LMENU = 0xA4;
    public const ushort VK_RMENU = 0xA5;
    public const ushort VK_RETURN = 0x0D;
    public const ushort VK_V = 0x56;

    // Window Display Affinity
    public const uint WDA_NONE = 0x00000000;
    public const uint WDA_MONITOR = 0x00000001;
    public const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    // DWM Attributes (Windows 10 / Windows 11)
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    public const int DWMWCP_ROUND = 2;
    public const int DWMSBT_AUTO = 0;
    public const int DWMSBT_NONE = 1;
    public const int DWMSBT_MAINWINDOW = 2;      // Mica
    public const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic

    // Message loop
    public const uint WM_QUIT = 0x0012;

    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    public static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint pdwAffinity);

    [DllImport("user32.dll")]
    public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll")]
    public static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    private static readonly IntPtr EngineExtraInfo = new(0x5455534B); // 'TUSK' tag

    public static bool IsKeyDownAsync(int vKey) => (GetAsyncKeyState(vKey) & 0x8000) != 0;

    public static void ReleaseActiveModifiers()
    {
        var keys = new ushort[] { VK_LCONTROL, VK_RCONTROL, VK_CONTROL, VK_LMENU, VK_RMENU, VK_MENU, VK_LSHIFT, VK_RSHIFT, VK_SHIFT };
        Span<ushort> pressed = stackalloc ushort[keys.Length];
        int count = 0;

        foreach (var vk in keys)
        {
            if (IsKeyDownAsync(vk))
            {
                pressed[count++] = vk;
            }
        }

        if (count == 0) return;

        var inputs = new INPUT[count];
        for (int i = 0; i < count; i++)
        {
            inputs[i] = CreateVirtualKeyInput(pressed[i], keyUp: true);
        }

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    public static void SendUnicodeTextElement(string textElement)
    {
        if (string.IsNullOrEmpty(textElement)) return;

        var inputs = new INPUT[textElement.Length * 2];
        int idx = 0;
        foreach (char ch in textElement)
        {
            inputs[idx++] = CreateUnicodeInput(ch, keyUp: false);
            inputs[idx++] = CreateUnicodeInput(ch, keyUp: true);
        }

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    public static void SendUnicodeBatch(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        // Chunk into reasonable SendInput batches (512 chars = 1024 events per syscall)
        const int maxCharsPerBatch = 512;
        int offset = 0;
        int cbSize = Marshal.SizeOf<INPUT>();

        while (offset < text.Length)
        {
            int count = Math.Min(maxCharsPerBatch, text.Length - offset);
            var inputs = new INPUT[count * 2];
            int idx = 0;
            for (int i = 0; i < count; i++)
            {
                char ch = text[offset + i];
                inputs[idx++] = CreateUnicodeInput(ch, keyUp: false);
                inputs[idx++] = CreateUnicodeInput(ch, keyUp: true);
            }

            SendInput((uint)inputs.Length, inputs, cbSize);
            offset += count;
        }
    }

    public static void SendEnterKey()
    {
        ushort scan = (ushort)MapVirtualKey(VK_RETURN, 0);
        var inputs = new[]
        {
            new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new INPUTUNION
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = VK_RETURN,
                        wScan = scan,
                        dwFlags = 0,
                        dwExtraInfo = EngineExtraInfo
                    }
                }
            },
            new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new INPUTUNION
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = VK_RETURN,
                        wScan = scan,
                        dwFlags = KEYEVENTF_KEYUP,
                        dwExtraInfo = EngineExtraInfo
                    }
                }
            }
        };

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    public static void SendCtrlV()
    {
        ReleaseActiveModifiers();
        var inputs = new[]
        {
            CreateVirtualKeyInput(VK_CONTROL, keyUp: false),
            CreateVirtualKeyInput(VK_V, keyUp: false),
            CreateVirtualKeyInput(VK_V, keyUp: true),
            CreateVirtualKeyInput(VK_CONTROL, keyUp: true)
        };

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static INPUT CreateUnicodeInput(char ch, bool keyUp)
    {
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new INPUTUNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = ch,
                    dwFlags = KEYEVENTF_UNICODE | (keyUp ? KEYEVENTF_KEYUP : 0),
                    dwExtraInfo = EngineExtraInfo
                }
            }
        };
    }

    private static INPUT CreateVirtualKeyInput(ushort vk, bool keyUp)
    {
        ushort scan = (ushort)MapVirtualKey(vk, 0);
        uint flags = keyUp ? KEYEVENTF_KEYUP : 0;
        if (vk is VK_RCONTROL or VK_RMENU)
        {
            flags |= KEYEVENTF_EXTENDEDKEY;
        }

        return new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new INPUTUNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = scan,
                    dwFlags = flags,
                    dwExtraInfo = EngineExtraInfo
                }
            }
        };
    }

    public static void ApplyWindowChromeEffects(IntPtr hwnd, Models.BackdropMaterialMode mode)
    {
        if (hwnd == IntPtr.Zero) return;

        try
        {
            int darkMode = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

            int roundCorners = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref roundCorners, sizeof(int));

            int backdrop = mode switch
            {
                Models.BackdropMaterialMode.Mica => DWMSBT_MAINWINDOW,
                Models.BackdropMaterialMode.Acrylic => DWMSBT_TRANSIENTWINDOW,
                _ => DWMSBT_NONE
            };

            int hr = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
            if (hr != 0 && mode == Models.BackdropMaterialMode.Acrylic)
            {
                // Fallback for Windows 10 Acrylic blur-behind
                EnableWin10Acrylic(hwnd);
            }
        }
        catch
        {
            // Ignore on older OS builds that do not support DWM attributes
        }
    }

    private static void EnableWin10Acrylic(IntPtr hwnd)
    {
        var accent = new AccentPolicy
        {
            AccentState = 4, // ACCENT_ENABLE_ACRYLICBLURBEHIND
            AccentFlags = 2,
            GradientColor = 0xCC090F24 // Deep midnight blue translucent tint (AABBGGRR)
        };

        int size = Marshal.SizeOf(accent);
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = 19, // WCA_ACCENT_POLICY
                SizeOfData = size,
                Data = ptr
            };
            SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}
