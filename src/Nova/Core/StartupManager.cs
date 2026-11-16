using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Nova.Core;

/// <summary>
/// Manages Windows user login startup registration via HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
/// Operates entirely with standard user privileges (no Administrator rights required).
/// </summary>
public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "NovaDesktopCompanion";

    /// <summary>
    /// Checks whether NOVA is configured to launch on Windows user login.
    /// </summary>
    public static bool IsStartupLaunchEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            if (key == null) return false;

            var value = key.GetValue(ValueName) as string;
            return !string.IsNullOrWhiteSpace(value);
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Failed to query startup registry key: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Enables or disables launch at Windows login for the current user.
    /// </summary>
    public static bool SetStartupLaunch(bool enable, string? customExePath = null)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null) return false;

            if (enable)
            {
                string exePath = customExePath ?? Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Nova.exe");
                // Launch in compact mode docked at top center
                string launchCommand = $"\"{exePath}\" --compact";
                key.SetValue(ValueName, launchCommand, RegistryValueKind.String);
                AppLogger.Info($"Registered startup launch command: {launchCommand}");
            }
            else
            {
                if (key.GetValue(ValueName) != null)
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                    AppLogger.Info("Removed startup launch registry entry.");
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Failed to update startup launch configuration (enable={enable})", ex);
            return false;
        }
    }
}

