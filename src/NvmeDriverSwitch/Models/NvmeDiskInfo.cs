using System;
using NvmeDriverSwitch.Infrastructure;

namespace NvmeDriverSwitch.Models
{
    /// <summary>Ein Datenträger unterhalb eines NVMe-Controllers.</summary>
    public sealed class NvmeDiskInfo
    {
        public string InstanceId { get; set; }

        public string FriendlyName { get; set; }

        /// <summary>Bindender Kernel-Dienst: "disk" (klassisch) oder "nvmedisk" (nativ).</summary>
        public string Service { get; set; }

        public long SizeBytes { get; set; }

        public bool IsBootDisk { get; set; }

        public int? DiskNumber { get; set; }

        public StackMode Mode
        {
            get
            {
                return string.Equals(Service, "nvmedisk", StringComparison.OrdinalIgnoreCase)
                    ? StackMode.Native
                    : StackMode.Classic;
            }
        }

        public bool IsNative
        {
            get { return Mode == StackMode.Native; }
        }

        public string DriverFile
        {
            get { return Mode == StackMode.Native ? "nvmedisk.sys" : "disk.sys"; }
        }

        public string BootText
        {
            get { return IsBootDisk ? LocalizedStrings.Get("BootText") : ""; }
        }

        public string SizeText
        {
            get
            {
                if (SizeBytes <= 0) return "-";
                double gb = SizeBytes / 1000d / 1000d / 1000d;
                if (gb >= 1000d)
                    return LocalizedStrings.Format("SizeTbFormat", (gb / 1000d).ToString("0.##"));
                return LocalizedStrings.Format("SizeGbFormat", gb.ToString("0"));
            }
        }

        public string DiskNumberText
        {
            get { return DiskNumber.HasValue ? LocalizedStrings.Format("DiskNumberFormat", DiskNumber.Value) : "-"; }
        }
    }
}
