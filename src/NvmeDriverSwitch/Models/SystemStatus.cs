using System;
using System.Collections.Generic;

namespace NvmeDriverSwitch.Models
{
    /// <summary>Vollstaendige Momentaufnahme: Registry-Vorgabe, Laufzeit und das Verdikt daraus.</summary>
    public sealed class SystemStatus
    {
        public SystemStatus()
        {
            Overrides = new List<OverrideEntry>();
            Controllers = new List<NvmeControllerInfo>();
            PreflightIssues = new List<PreflightIssue>();
        }

        /// <summary>Was die Registry vorgibt.</summary>
        public StackMode ConfiguredMode { get; set; }

        /// <summary>Ob mindestens einer der drei NVMe-Minimalwerte vorhanden ist.</summary>
        public bool HasAnyMinimalOverride { get; set; }

        /// <summary>Exakter Fingerprint aus Existenz, Registry-Typ und Daten der Minimalwerte.</summary>
        public string ConfigurationFingerprint { get; set; }

        /// <summary>Was gerade laeuft.</summary>
        public StackMode RuntimeMode { get; set; }

        public Verdict Verdict { get; set; }

        public List<OverrideEntry> Overrides { get; private set; }

        public bool SafeBootMinimalPresent { get; set; }

        public bool SafeBootNetworkPresent { get; set; }

        /// <summary>Von Windows angelegter Klassen-Eintrag. Wird nur gelesen.</summary>
        public bool SafeBootClassGuidPresent { get; set; }

        public List<NvmeControllerInfo> Controllers { get; private set; }

        /// <summary>Geraete der Klasse NvmeDisk, die aktuell nicht angeschlossen sind (Phantome).</summary>
        public int GhostNvmeDiskCount { get; set; }

        public List<PreflightIssue> PreflightIssues { get; private set; }

        public string WindowsBuild { get; set; }

        public DateTime? LastBootUtc { get; set; }

        public DateTime? LastWriteUtc { get; set; }

        public PendingChange PendingChange { get; set; }
    }
}
