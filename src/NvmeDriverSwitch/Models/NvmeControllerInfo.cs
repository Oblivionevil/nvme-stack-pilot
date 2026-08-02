using System.Collections.Generic;
using NvmeDriverSwitch.Infrastructure;

namespace NvmeDriverSwitch.Models
{
    /// <summary>Ein NVMe-Controller (PCI-Geraet) samt seiner Kind-Datentraeger.</summary>
    public sealed class NvmeControllerInfo
    {
        public NvmeControllerInfo()
        {
            Disks = new List<NvmeDiskInfo>();
        }

        public string InstanceId { get; set; }

        public string FriendlyName { get; set; }

        /// <summary>"stornvme" (Microsoft) oder z. B. "secnvme" (Samsung).</summary>
        public string Service { get; set; }

        public uint ConfigManagerErrorCode { get; set; }

        public List<NvmeDiskInfo> Disks { get; private set; }

        public bool HasDisks
        {
            get { return Disks.Count > 0; }
        }

        public bool IsHealthy
        {
            get { return ConfigManagerErrorCode == 0; }
        }

        public string StatusText
        {
            get
            {
                return IsHealthy
                    ? LocalizedStrings.Get("StatusOk")
                    : LocalizedStrings.Format("ProblemCodeFormat", ConfigManagerErrorCode);
            }
        }

        /// <summary>
        /// Gruppenüberschrift im Datenträger-Grid. Mehrere Controller tragen denselben
        /// Anzeigenamen, deshalb hängt das letzte Segment der Instanz-ID als Unterscheidung an.
        /// </summary>
        public string Header
        {
            get
            {
                var tail = InstanceId;
                if (!string.IsNullOrEmpty(tail))
                {
                    int cut = tail.LastIndexOf('\\');
                    if (cut >= 0 && cut < tail.Length - 1) tail = tail.Substring(cut + 1);
                }
                return FriendlyName + "  (" + Service + ")   ·   " + tail;
            }
        }
    }
}
