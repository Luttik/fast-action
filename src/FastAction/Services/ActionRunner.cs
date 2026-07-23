using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using FastAction.Models;

namespace FastAction.Services;

public sealed class ActionRunner
{
    public bool TryRunShell(ActionConfig action, out string? error)
    {
        error = null;
        if (!string.Equals(action.Type, "shell", StringComparison.OrdinalIgnoreCase))
        {
            error = "Action is not a shell action.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(action.Command))
        {
            error = "Shell action is missing a command.";
            return false;
        }

        try
        {
            var command = ResolveCommand(action.Command.Trim());
            var startInfo = new ProcessStartInfo
            {
                FileName = command,
                UseShellExecute = true,
            };

            if (action.Args is { Count: > 0 })
            {
                // ArgumentList cannot be used together with UseShellExecute.
                startInfo.Arguments = JoinArguments(action.Args);
            }

            if (!string.IsNullOrWhiteSpace(action.WorkingDirectory))
            {
                startInfo.WorkingDirectory = action.WorkingDirectory;
            }

            var process = Process.Start(startInfo);
            if (process is null && !IsUriCommand(command))
            {
                error = $"Failed to start '{command}'.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Debug.WriteLine($"Failed to run action: {ex}");
            return false;
        }
    }

    public bool TryRunHotkey(ActionConfig action, out string? error)
    {
        error = null;
        if (!string.Equals(action.Type, "hotkey", StringComparison.OrdinalIgnoreCase))
        {
            error = "Action is not a hotkey action.";
            return false;
        }

        var config = new HotkeyConfig
        {
            Modifiers = action.Modifiers ?? [],
            Key = action.Key ?? string.Empty,
        };

        if (!HotkeyService.TryParseHotkey(config, out var modifiers, out var virtualKey))
        {
            error = "Invalid hotkey — use modifiers (Alt/Ctrl/Shift/Win) and a key like S, Space, or F5.";
            return false;
        }

        try
        {
            SendChord(modifiers, (ushort)virtualKey);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Debug.WriteLine($"Failed to send hotkey: {ex}");
            return false;
        }
    }

    private static void SendChord(uint modifiers, ushort virtualKey)
    {
        const uint modAlt = 0x0001;
        const uint modCtrl = 0x0002;
        const uint modShift = 0x0004;
        const uint modWin = 0x0008;

        var downs = new List<ushort>(4);
        if ((modifiers & modCtrl) != 0)
        {
            downs.Add(0x11); // VK_CONTROL
        }

        if ((modifiers & modAlt) != 0)
        {
            downs.Add(0x12); // VK_MENU
        }

        if ((modifiers & modShift) != 0)
        {
            downs.Add(0x10); // VK_SHIFT
        }

        if ((modifiers & modWin) != 0)
        {
            downs.Add(0x5B); // VK_LWIN
        }

        var inputs = new List<Input>(downs.Count * 2 + 2);
        foreach (var vk in downs)
        {
            inputs.Add(KeyInput(vk, keyUp: false));
        }

        inputs.Add(KeyInput(virtualKey, keyUp: false));
        inputs.Add(KeyInput(virtualKey, keyUp: true));

        for (var i = downs.Count - 1; i >= 0; i--)
        {
            inputs.Add(KeyInput(downs[i], keyUp: true));
        }

        var sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Input>());
        if (sent != inputs.Count)
        {
            throw new InvalidOperationException($"SendInput sent {sent}/{inputs.Count} events.");
        }
    }

    private static Input KeyInput(ushort virtualKey, bool keyUp)
    {
        const uint inputKeyboard = 1;
        const uint keyeventfKeyup = 0x0002;
        const uint keyeventfExtendedkey = 0x0001;

        var flags = keyUp ? keyeventfKeyup : 0u;
        if (IsExtendedKey(virtualKey))
        {
            flags |= keyeventfExtendedkey;
        }

        var scan = (ushort)MapVirtualKey(virtualKey, 0);
        return new Input
        {
            Type = inputKeyboard,
            U = new InputUnion
            {
                Ki = new KeybdInput
                {
                    WVk = virtualKey,
                    WScan = scan,
                    DwFlags = flags,
                    Time = 0,
                    DwExtraInfo = IntPtr.Zero,
                },
            },
        };
    }

    private static bool IsExtendedKey(ushort virtualKey) =>
        virtualKey is 0x5B or 0x5C // Win
            or 0x21 or 0x22 or 0x23 or 0x24 // PgUp/PgDn/End/Home
            or 0x25 or 0x26 or 0x27 or 0x28 // arrows
            or 0x2D or 0x2E; // Insert/Delete

    private static bool IsUriCommand(string command) =>
        command.Contains(':', StringComparison.Ordinal)
        && Uri.TryCreate(command, UriKind.Absolute, out var uri)
        && uri.Scheme is not "file";

    private static string JoinArguments(IEnumerable<string> args)
    {
        var sb = new StringBuilder();
        foreach (var arg in args)
        {
            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            if (arg.Length == 0)
            {
                sb.Append("\"\"");
                continue;
            }

            var needsQuotes = arg.Any(char.IsWhiteSpace) || arg.Contains('"');
            if (!needsQuotes)
            {
                sb.Append(arg);
                continue;
            }

            sb.Append('"');
            sb.Append(arg.Replace("\"", "\\\"", StringComparison.Ordinal));
            sb.Append('"');
        }

        return sb.ToString();
    }

    private static string ResolveCommand(string command)
    {
        if (IsUriCommand(command) || Path.IsPathRooted(command) && File.Exists(command))
        {
            return command;
        }

        if (File.Exists(command))
        {
            return Path.GetFullPath(command);
        }

        foreach (var candidate in ExpandCommandCandidates(command))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        // Known app aliases when PATH shims are missing (e.g. tray apps with a slim env).
        if (command.Equals("code", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var path in KnownEditorPaths())
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }

        if (command.Equals("cursor", StringComparison.OrdinalIgnoreCase))
        {
            var cursor = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                "cursor",
                "Cursor.exe");
            if (File.Exists(cursor))
            {
                return cursor;
            }
        }

        return command;
    }

    private static IEnumerable<string> ExpandCommandCandidates(string command)
    {
        var pathExt = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var hasExtension = pathExt.Any(ext =>
            command.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

        var directories = new List<string> { Environment.CurrentDirectory };
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        directories.AddRange(
            pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim().Trim('"')));

        foreach (var dir in directories.Where(d => !string.IsNullOrWhiteSpace(d)))
        {
            var direct = Path.Combine(dir, command);
            yield return direct;
            if (hasExtension)
            {
                continue;
            }

            foreach (var ext in pathExt)
            {
                yield return direct + ext;
            }
        }
    }

    private static IEnumerable<string> KnownEditorPaths()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        yield return Path.Combine(local, "Programs", "Microsoft VS Code", "Code.exe");
        yield return Path.Combine(local, "Programs", "Microsoft VS Code Insiders", "Code - Insiders.exe");
        yield return Path.Combine(programFiles, "Microsoft VS Code", "Code.exe");
        yield return Path.Combine(local, "Programs", "cursor", "Cursor.exe");
        yield return Path.Combine(local, "Programs", "cursor", "resources", "app", "bin", "cursor.cmd");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mi;

        [FieldOffset(0)]
        public KeybdInput Ki;

        [FieldOffset(0)]
        public HardwareInput Hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint DwFlags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeybdInput
    {
        public ushort WVk;
        public ushort WScan;
        public uint DwFlags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput
    {
        public uint UMsg;
        public ushort WParamL;
        public ushort WParamH;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);
}
