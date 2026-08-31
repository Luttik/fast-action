using Microsoft.Win32;

namespace FastAction.Services;

/// <summary>
/// Registers/unregisters the app to launch at Windows sign-in via the
/// per-user Run key. Value name must match installer/FastAction.iss.
/// The app is unpackaged, so this (rather than the MSIX StartupTask API)
/// is the mechanism available to it after install.
/// </summary>
public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "FastAction";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var value = key?.GetValue(ValueName) as string;
        return value is not null
            && string.Equals(value.Trim('"'), GetExecutablePath(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Idempotently syncs the Run key with the desired state and the current executable path.</summary>
    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Could not open the startup registry key.");

        if (enabled)
        {
            key.SetValue(ValueName, $"\"{GetExecutablePath()}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    private static string GetExecutablePath() =>
        Environment.ProcessPath
        ?? throw new InvalidOperationException("Could not determine the current executable path.");
}
