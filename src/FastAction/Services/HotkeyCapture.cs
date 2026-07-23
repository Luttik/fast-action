using System.Diagnostics;
using System.Runtime.InteropServices;
using FastAction.Models;
using Microsoft.UI.Dispatching;

namespace FastAction.Services;

/// <summary>
/// Temporary low-level keyboard hook that captures one chord (modifiers + key).
/// </summary>
public sealed class HotkeyCapture : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeydown = 0x0100;
    private const int WmSyskeydown = 0x0104;
    private const int LlkhfUp = 0x80;
    private const int LlkhfInjected = 0x10;

    private readonly DispatcherQueue _dispatcherQueue;
    private readonly LowLevelKeyboardProc _hookProc;
    private readonly GCHandle _hookProcHandle;
    private IntPtr _hookId = IntPtr.Zero;
    private bool _listening;
    private bool _ctrl;
    private bool _alt;
    private bool _shift;
    private bool _win;

    public event EventHandler<HotkeyConfig>? Captured;
    public event EventHandler? Cancelled;

    public bool IsListening => _listening;

    public HotkeyCapture(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;
        _hookProc = HookCallback;
        _hookProcHandle = GCHandle.Alloc(_hookProc);
    }

    public void Start()
    {
        if (_listening)
        {
            return;
        }

        _ctrl = _alt = _shift = _win = false;
        using var process = Process.GetCurrentProcess();
        var moduleName = process.MainModule?.ModuleName;
        _hookId = SetWindowsHookEx(
            WhKeyboardLl,
            _hookProc,
            string.IsNullOrEmpty(moduleName) ? GetModuleHandle(null) : GetModuleHandle(moduleName),
            0);
        _listening = _hookId != IntPtr.Zero;
        if (!_listening)
        {
            Debug.WriteLine($"HotkeyCapture SetWindowsHookEx failed: {Marshal.GetLastWin32Error()}");
        }
    }

    public void Stop()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }

        _listening = false;
        _ctrl = _alt = _shift = _win = false;
    }

    public void Dispose()
    {
        Stop();
        if (_hookProcHandle.IsAllocated)
        {
            _hookProcHandle.Free();
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || !_listening)
        {
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        var msg = wParam.ToInt32();
        var info = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
        if ((info.Flags & LlkhfInjected) != 0)
        {
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        var isUp = (info.Flags & LlkhfUp) != 0;
        var vk = info.VkCode;

        // Track modifiers ourselves — swallowed keys never update GetAsyncKeyState.
        if (IsModifier(vk))
        {
            SetModifier(vk, down: !isUp);
            return (IntPtr)1;
        }

        if (isUp || (msg != WmKeydown && msg != WmSyskeydown))
        {
            return (IntPtr)1;
        }

        if (vk == 0x1B) // Escape cancels (only when no other key; Esc alone)
        {
            Stop();
            _dispatcherQueue.TryEnqueue(() => Cancelled?.Invoke(this, EventArgs.Empty));
            return (IntPtr)1;
        }

        if (!TryNameKey(vk, out var keyName))
        {
            return (IntPtr)1;
        }

        var config = new HotkeyConfig
        {
            Modifiers = CurrentModifiers(),
            Key = keyName,
        };

        Stop();
        _dispatcherQueue.TryEnqueue(() => Captured?.Invoke(this, config));
        return (IntPtr)1;
    }

    private void SetModifier(uint vk, bool down)
    {
        switch (vk)
        {
            case 0x10 or 0xA0 or 0xA1:
                _shift = down;
                break;
            case 0x11 or 0xA2 or 0xA3:
                _ctrl = down;
                break;
            case 0x12 or 0xA4 or 0xA5:
                _alt = down;
                break;
            case 0x5B or 0x5C:
                _win = down;
                break;
        }
    }

    private List<string> CurrentModifiers()
    {
        var mods = new List<string>(4);
        if (_ctrl)
        {
            mods.Add("Ctrl");
        }

        if (_alt)
        {
            mods.Add("Alt");
        }

        if (_shift)
        {
            mods.Add("Shift");
        }

        if (_win)
        {
            mods.Add("Win");
        }

        return mods;
    }

    private static bool IsModifier(uint vk) =>
        vk is 0x10 or 0xA0 or 0xA1 // Shift
            or 0x11 or 0xA2 or 0xA3 // Ctrl
            or 0x12 or 0xA4 or 0xA5 // Alt
            or 0x5B or 0x5C; // Win

    public static string FormatChord(HotkeyConfig? config)
    {
        if (config is null || string.IsNullOrWhiteSpace(config.Key))
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var modifier in config.Modifiers ?? [])
        {
            var normalized = NormalizeModifierName(modifier);
            if (normalized is not null && !parts.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                parts.Add(normalized);
            }
        }

        parts.Add(config.Key.Trim());
        return string.Join(" + ", parts);
    }

    private static string? NormalizeModifierName(string modifier) =>
        modifier.Trim().ToLowerInvariant() switch
        {
            "alt" => "Alt",
            "ctrl" or "control" => "Ctrl",
            "shift" => "Shift",
            "win" or "windows" or "cmd" => "Win",
            _ => null,
        };

    private static bool TryNameKey(uint vk, out string name)
    {
        name = string.Empty;
        if (vk is >= 'A' and <= 'Z')
        {
            name = ((char)vk).ToString();
            return true;
        }

        if (vk is >= '0' and <= '9')
        {
            name = ((char)vk).ToString();
            return true;
        }

        name = vk switch
        {
            0x20 => "Space",
            0x09 => "Tab",
            0x0D => "Enter",
            0x1B => "Esc",
            0x70 => "F1",
            0x71 => "F2",
            0x72 => "F3",
            0x73 => "F4",
            0x74 => "F5",
            0x75 => "F6",
            0x76 => "F7",
            0x77 => "F8",
            0x78 => "F9",
            0x79 => "F10",
            0x7A => "F11",
            0x7B => "F12",
            _ => string.Empty,
        };
        return name.Length > 0;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public int Flags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
