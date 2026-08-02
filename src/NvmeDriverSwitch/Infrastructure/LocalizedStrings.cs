using System;
using System.Globalization;
using System.Resources;

namespace NvmeDriverSwitch.Infrastructure
{
    /// <summary>
    /// Zugriff auf die UI-Ressourcen. Die Kultur wird von Windows (OS-Sprache) bestimmt;
    /// deutsche Systeme erhalten die deutsche Ressource, alle anderen die englische.
    /// Beide Ressourcen liegen embedded in der EXE - es werden keine Satelliten-Assemblies
    /// oder sonstige Zusatzdateien benoetigt.
    /// </summary>
    public static class LocalizedStrings
    {
        private static readonly ResourceManager English =
            new ResourceManager("NvmeDriverSwitch.Properties.Resources", typeof(LocalizedStrings).Assembly);

        private static readonly ResourceManager German =
            new ResourceManager("NvmeDriverSwitch.Properties.ResourcesDe", typeof(LocalizedStrings).Assembly);

        private static ResourceManager Current
        {
            get
            {
                return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
                           .Equals("de", StringComparison.OrdinalIgnoreCase)
                    ? German
                    : English;
            }
        }

        public static string Get(string key)
        {
            return Current.GetString(key) ?? key;
        }

        public static string Format(string key, params object[] args)
        {
            return string.Format(CultureInfo.CurrentUICulture, Get(key), args);
        }
    }
}
