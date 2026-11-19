using System.IO;
using Microsoft.Win32;

namespace BattaryChecker;

/// <summary>Start with Windows via the per-user Run key. The registry is the only source of truth.</summary>
public static class Autostart
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "BattaryChecker";

    static string Command => $"\"{Environment.ProcessPath}\"";

    public static bool IsEnabled(string keyPath = RunKey)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(ValueName) is string value
            && string.Equals(value, Command, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>False when Windows refused (a policy locking the Run key): the caller tells the user.</summary>
    public static bool Set(bool enabled, string keyPath = RunKey)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(keyPath);
            if (enabled)
                key.SetValue(ValueName, Command);
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return false;
        }
    }
}
