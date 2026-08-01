namespace NvmeDriverSwitch.Models
{
    /// <summary>Welcher NVMe-Treiberpfad gemeint ist.</summary>
    public enum StackMode
    {
        /// <summary>stornvme.sys -> SCSI-Uebersetzung -> disk.sys</summary>
        Classic,

        /// <summary>nvmedisk.sys, Geraeteklasse NvmeDisk</summary>
        Native
    }

    /// <summary>Ergebnis des Abgleichs zwischen Registry-Vorgabe und laufendem Zustand.</summary>
    public enum Verdict
    {
        /// <summary>Konfiguration und Laufzeit stimmen ueberein.</summary>
        InSync,

        /// <summary>Registry wurde geaendert, der Neustart steht noch aus.</summary>
        RebootRequired,

        /// <summary>Abweichung ohne passenden, von dieser App geschriebenen Zustandsmarker.</summary>
        UntrackedMismatch,

        /// <summary>Nativ konfiguriert, Neustart lag dazwischen - Windows hat die Overrides ignoriert.</summary>
        IgnoredByWindows
    }
}
