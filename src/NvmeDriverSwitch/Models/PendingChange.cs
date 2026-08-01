using System;

namespace NvmeDriverSwitch.Models
{
    /// <summary>Eine von der App geschriebene, noch nicht erfolgreich verifizierte Konfiguration.</summary>
    public sealed class PendingChange
    {
        public DateTime WrittenUtc { get; set; }

        public StackMode TargetMode { get; set; }

        public string ConfigurationFingerprint { get; set; }
    }
}
