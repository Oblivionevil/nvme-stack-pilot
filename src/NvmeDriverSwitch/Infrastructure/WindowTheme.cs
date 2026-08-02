using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace NvmeDriverSwitch.Infrastructure
{
    /// <summary>
    /// Sorgt dafuer, dass die Titelleiste der Anwendung der Hell/Dunkel-Einstellung
    /// von Windows folgt (AppsUseLightTheme, mit SystemUsesLightTheme als Fallback).
    /// </summary>
    internal static class WindowTheme
    {
        private const int DwmwaUseImmersiveDarkMode = 20;
        private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;

        private const string PersonalizeKey =
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string AppsUseLightTheme = "AppsUseLightTheme";
        private const string SystemUsesLightTheme = "SystemUsesLightTheme";

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        /// <summary>Wendet die aktuelle Windows-Themeneinstellung auf die Titelleiste an.</summary>
        public static void ApplyTo(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;

            int useDarkMode = IsDarkTheme() ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref useDarkMode, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeBefore20H1, ref useDarkMode, sizeof(int));
        }

        /// <summary>Ermittelt, ob Windows im Dunkelmodus ist.</summary>
        public static bool IsDarkTheme()
        {
            try
            {
                var value = Registry.GetValue(PersonalizeKey, AppsUseLightTheme, null)
                            ?? Registry.GetValue(PersonalizeKey, SystemUsesLightTheme, 1);
                return value is int appsUseLightTheme && appsUseLightTheme == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
