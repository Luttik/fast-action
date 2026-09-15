using System.Diagnostics;
using System.Runtime.InteropServices;
using FastAction.Models;
using Microsoft.UI.Dispatching;

namespace FastAction.Services;

/// <summary>
/// Global hotkey via WH_KEYBOARD_LL. RegisterHotKey cannot reliably bind
/// Win+Shift+Space (and other Win combos) on modern Windows.
/// </summary>
public sealed class HotkeyService : IDisposable
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

    private uint _requiredModifiers;
    private uint _virtualKey;
    private bool _armed;
    private bool _firedThisPress;

    public event EventHandler? HotkeyPressed;

    public HotkeyService(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;
        _hookProc = HookCallback;
        _hookProcHandle = GCHandle.Alloc(_hookProc);
    }

    public void Apply(HotkeyConfig config)
    {
        Unregister();

        if (!TryParseHotkey(config, out _requiredModifiers, out _virtualKey))
        {
            Debug.WriteLine("Invalid hotkey config; hotkey not registered.");
            return;
        }

        using var process = Process.GetCurrentProcess();
        var moduleName = process.MainModule?.ModuleName;
        _hookId = SetWindowsHookEx(
            WhKeyboardLl,
            _hookProc,
            string.IsNullOrEmpty(moduleName) ? GetModuleHandle(null) : GetModuleHandle(moduleName),
            0);
        _armed = _hookId != IntPtr.Zero;
        if (!_armed)
        {
            Debug.WriteLine($"SetWindowsHookEx failed: {Marshal.GetLastWin32Error()}");
        }
    }

    public void Unregister()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }

        _armed = false;
        _firedThisPress = false;
    }

    public void Dispose()
    {
        Unregister();
        if (_hookProcHandle.IsAllocated)
        {
            _hookProcHandle.Free();
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _armed)
        {
            var msg = wParam.ToInt32();
            var info = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            // Ignore keys we synthesize for hotkey actions (avoids re-trigger loops).
            if ((info.Flags & LlkhfInjected) != 0)
            {
                return CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            var isUp = (info.Flags & LlkhfUp) != 0;

            if (info.VkCode == _virtualKey)
            {
                if (isUp)
                {
                    _firedThisPress = false;
                }
                else if ((msg == WmKeydown || msg == WmSyskeydown) && !_firedThisPress && ModifiersMatch())
                {
                    _firedThisPress = true;
                    _dispatcherQueue.TryEnqueue(() => HotkeyPressed?.Invoke(this, EventArgs.Empty));
                    return (IntPtr)1;
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private bool ModifiersMatch()
    {
        const int vkShift = 0x10;
        const int vkControl = 0x11;
        const int vkMenu = 0x12; // Alt
        const int vkLWin = 0x5B;
        const int vkRWin = 0x5C;

        bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        var wantAlt = (_requiredModifiers & 0x0001) != 0;
        var wantCtrl = (_requiredModifiers & 0x0002) != 0;
        var wantShift = (_requiredModifiers & 0x0004) != 0;
        var wantWin = (_requiredModifiers & 0x0008) != 0;

        var alt = Down(vkMenu);
        var ctrl = Down(vkControl);
        var shift = Down(vkShift);
        var win = Down(vkLWin) || Down(vkRWin);

        return alt == wantAlt
            && ctrl == wantCtrl
            && shift == wantShift
            && win == wantWin;
    }

    public static bool TryParseHotkey(HotkeyConfig? config, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;

        if (config is null)
        {
            return false;
        }

        foreach (var modifier in config.Modifiers ?? [])
        {
            modifiers |= modifier.Trim().ToLowerInvariant() switch
            {
                "alt" => 0x0001,
                "ctrl" or "control" => 0x0002,
                "shift" => 0x0004,
                "win" or "windows" or "cmd" => 0x0008,
                _ => 0u,
            };
        }

        if (string.IsNullOrWhiteSpace(config.Key))
        {
            return false;
        }

        var key = config.Key.Trim();
        if (key.Length == 1)
        {
            var c = char.ToUpperInvariant(key[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                virtualKey = c;
                return true;
            }
        }

        virtualKey = key.ToLowerInvariant() switch
        {
            "space" => 0x20,
            "tab" => 0x09,
            "enter" or "return" => 0x0D,
            "escape" or "esc" => 0x1B,
            "f1" => 0x70,
            "f2" => 0x71,
            "f3" => 0x72,
            "f4" => 0x73,
            "f5" => 0x74,
            "f6" => 0x75,
            "f7" => 0x76,
            "f8" => 0x77,
            "f9" => 0x78,
            "f10" => 0x79,
            "f11" => 0x7A,
            "f12" => 0x7B,
            _ => 0u,
        };

        return virtualKey != 0;
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

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
