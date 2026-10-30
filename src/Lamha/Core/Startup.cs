using Microsoft.Win32;

namespace Lamha.Core;

/// <summary>"Start with Windows". The Run key itself is the setting, so the app and the installer can never disagree.</summary>
public static class Startup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", Name = "Lamha";

    public static bool Enabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(Name) is not null;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(Name, $"\"{Environment.ProcessPath}\" --tray");
            else key.DeleteValue(Name, throwOnMissingValue: false);
        }
    }
}
