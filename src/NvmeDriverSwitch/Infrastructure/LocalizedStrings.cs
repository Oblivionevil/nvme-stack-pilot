using System.Globalization;
using System.Resources;

namespace NvmeDriverSwitch.Infrastructure
{
    /// <summary>
    /// Zugriff auf die UI-Ressourcen. Die Kultur wird von Windows (OS-Sprache) bestimmt;
    /// deutsche Systeme erhalten die .de-Satellitenressourcen, alle anderen die neutrale
    /// englische Ressource.
    /// </summary>
    public static class LocalizedStrings
    {
        private static readonly ResourceManager Manager =
            new ResourceManager("NvmeDriverSwitch.Properties.Resources", typeof(LocalizedStrings).Assembly);

        public static string Get(string key)
        {
            return Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
        }

        public static string Format(string key, params object[] args)
        {
            return string.Format(CultureInfo.CurrentUICulture, Get(key), args);
        }
    }
}
