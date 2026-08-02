using NvmeDriverSwitch.Infrastructure;

namespace NvmeDriverSwitch.Models
{
    public enum OverrideRole
    {
        /// <summary>Gehoert zum Minimalsatz und ist korrekt gesetzt.</summary>
        Minimal,

        /// <summary>Gehoert zum Minimalsatz, fehlt aber oder hat den falschen Wert.</summary>
        Missing,

        /// <summary>Bekannter alter NVMe-Wert, der gezielt entfernt werden darf.</summary>
        Legacy,

        /// <summary>Unbekannter oder plattformspezifischer Wert; bleibt schreibgeschuetzt.</summary>
        Unknown
    }

    public sealed class OverrideEntry
    {
        public string Name { get; set; }

        /// <summary>Rohdaten als Text, "-" wenn der Wert fehlt.</summary>
        public string Data { get; set; }

        /// <summary>Registry-Datentyp als lesbarer REG_*-Name.</summary>
        public string Kind { get; set; }

        public OverrideRole Role { get; set; }

        /// <summary>Erlaeuterung, warum der Wert diese Rolle hat.</summary>
        public string Note { get; set; }

        public bool IsRemovable
        {
            get { return Role == OverrideRole.Legacy; }
        }

        public string RoleText
        {
            get
            {
                switch (Role)
                {
                    case OverrideRole.Minimal: return LocalizedStrings.Get("RoleMinimal");
                    case OverrideRole.Missing: return LocalizedStrings.Get("RoleMissing");
                    case OverrideRole.Legacy: return LocalizedStrings.Get("RoleLegacy");
                    default: return LocalizedStrings.Get("RoleUnknown");
                }
            }
        }
    }
}
