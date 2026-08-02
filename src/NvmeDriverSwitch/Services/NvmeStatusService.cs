using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management;
using Microsoft.Win32;
using NvmeDriverSwitch.Infrastructure;
using NvmeDriverSwitch.Models;

namespace NvmeDriverSwitch.Services
{
    /// <summary>
    /// Ermittelt den tatsaechlich laufenden Zustand: welche Controller da sind, welcher
    /// Treiber die Datentraeger bedient und ob der native Stack gerade aktiv ist.
    /// </summary>
    public sealed class NvmeStatusService
    {
        private const string ScsiAdapterClassGuid = "{4d36e97b-e325-11ce-bfc1-08002be10318}";
        private const string NvmeDiskClassGuid = "{75416e63-5912-4dfa-ae8f-3efaccaffb14}";

        private sealed class PnpRecord
        {
            public string Name;
            public string Service;
            public uint ErrorCode;
        }

        /// <summary>
        /// Der native Stack laeuft genau dann, wenn mindestens ein Geraet der Klasse NvmeDisk
        /// praesent ist. Win32_PnPEntity listet nur angeschlossene Geraete - getrennte
        /// Phantome aus frueheren Umschaltungen tauchen hier korrekt nicht auf.
        /// </summary>
        public StackMode GetRuntimeMode()
        {
            using (var searcher = new ManagementObjectSearcher(
                "SELECT DeviceID FROM Win32_PnPEntity WHERE ClassGuid='" + NvmeDiskClassGuid + "'"))
            using (var results = searcher.Get())
            {
                foreach (var o in results)
                {
                    o.Dispose();
                    return StackMode.Native;
                }
            }
            return StackMode.Classic;
        }

        public List<NvmeControllerInfo> GetControllers()
        {
            var pnp = ReadAllPnpDevices();
            var diskSizes = ReadDiskDrives();
            var bootDiskIndices = ReadBootDiskIndices();

            var controllers = new List<NvmeControllerInfo>();

            foreach (var kvp in pnp)
            {
                var rec = kvp.Value;
                if (rec.Service == null) continue;
                if (rec.Service.IndexOf("nvme", StringComparison.OrdinalIgnoreCase) < 0) continue;
                // Nur Controller (SCSIAdapter), nicht die Datentraeger selbst.
                if (!kvp.Key.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase)) continue;

                var ctrl = new NvmeControllerInfo
                {
                    InstanceId = kvp.Key,
                    FriendlyName = rec.Name,
                    Service = rec.Service,
                    ConfigManagerErrorCode = rec.ErrorCode
                };

                foreach (var childId in GetChildDeviceIds(kvp.Key))
                {
                    PnpRecord child;
                    if (!pnp.TryGetValue(childId.ToUpperInvariant(), out child))
                        continue;

                    var disk = new NvmeDiskInfo
                    {
                        InstanceId = childId,
                        FriendlyName = child.Name,
                        Service = child.Service
                    };

                    DiskDriveRecord dd;
                    if (diskSizes.TryGetValue(childId.ToUpperInvariant(), out dd))
                    {
                        disk.SizeBytes = dd.Size;
                        disk.DiskNumber = dd.Index;
                        disk.IsBootDisk = bootDiskIndices.Contains(dd.Index);
                    }

                    ctrl.Disks.Add(disk);
                }

                controllers.Add(ctrl);
            }

            controllers.Sort((a, b) => string.Compare(a.FriendlyName, b.FriendlyName, StringComparison.OrdinalIgnoreCase));
            return controllers;
        }

        /// <summary>
        /// Zaehlt NvmeDisk-Geraete, die in der Registry stehen, aber nicht angeschlossen sind.
        /// Diese Phantome belegen, dass der native Stack auf dem System schon einmal lief.
        /// </summary>
        public int GetGhostNvmeDiskCount()
        {
            int total = 0;
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var enumRoot = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\NVME", false))
                {
                    if (enumRoot == null) return 0;

                    foreach (var deviceId in enumRoot.GetSubKeyNames())
                    {
                        using (var deviceKey = enumRoot.OpenSubKey(deviceId, false))
                        {
                            if (deviceKey == null) continue;
                            foreach (var instanceId in deviceKey.GetSubKeyNames())
                            {
                                using (var instanceKey = deviceKey.OpenSubKey(instanceId, false))
                                {
                                    if (instanceKey == null) continue;
                                    var classGuid = instanceKey.GetValue("ClassGUID") as string;
                                    if (classGuid != null &&
                                        classGuid.Equals(NvmeDiskClassGuid, StringComparison.OrdinalIgnoreCase))
                                    {
                                        total++;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                return 0;
            }

            int present = CountPresentNvmeDisks();
            return Math.Max(0, total - present);
        }

        public string GetWindowsBuild()
        {
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", false))
                {
                    if (key == null) return LocalizedStrings.Get("Unknown");
                    var display = key.GetValue("DisplayVersion") as string;
                    var build = key.GetValue("CurrentBuild") as string;
                    var ubr = key.GetValue("UBR");
                    var edition = key.GetValue("EditionID") as string;

                    return LocalizedStrings.Format("WindowsBuildFormat", display, build, ubr, edition);
                }
            }
            catch (Exception)
            {
                return LocalizedStrings.Get("Unknown");
            }
        }

        public DateTime? GetLastBootUtc()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT LastBootUpTime FROM Win32_OperatingSystem"))
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject o in results)
                    {
                        using (o)
                        {
                            var raw = o["LastBootUpTime"] as string;
                            if (string.IsNullOrEmpty(raw)) continue;
                            return ManagementDateTimeConverter.ToDateTime(raw).ToUniversalTime();
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            return null;
        }

        // ---- Hilfsmittel ----

        private static int CountPresentNvmeDisks()
        {
            int n = 0;
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT DeviceID FROM Win32_PnPEntity WHERE ClassGuid='" + NvmeDiskClassGuid + "'"))
                using (var results = searcher.Get())
                {
                    foreach (var o in results)
                    {
                        n++;
                        o.Dispose();
                    }
                }
            }
            catch (Exception)
            {
            }
            return n;
        }

        private static Dictionary<string, PnpRecord> ReadAllPnpDevices()
        {
            var map = new Dictionary<string, PnpRecord>(StringComparer.OrdinalIgnoreCase);

            using (var searcher = new ManagementObjectSearcher(
                "SELECT DeviceID, Name, Service, ConfigManagerErrorCode FROM Win32_PnPEntity"))
            using (var results = searcher.Get())
            {
                foreach (ManagementObject o in results)
                {
                    using (o)
                    {
                        var id = o["DeviceID"] as string;
                        if (string.IsNullOrEmpty(id)) continue;

                        uint err = 0;
                        var rawErr = o["ConfigManagerErrorCode"];
                        if (rawErr != null)
                        {
                            try { err = Convert.ToUInt32(rawErr); } catch { }
                        }

                        map[id.ToUpperInvariant()] = new PnpRecord
                        {
                            Name = o["Name"] as string,
                            Service = o["Service"] as string,
                            ErrorCode = err
                        };
                    }
                }
            }

            return map;
        }

        private struct DiskDriveRecord
        {
            public long Size;
            public int Index;
        }

        private static Dictionary<string, DiskDriveRecord> ReadDiskDrives()
        {
            var map = new Dictionary<string, DiskDriveRecord>(StringComparer.OrdinalIgnoreCase);

            using (var searcher = new ManagementObjectSearcher("SELECT PNPDeviceID, Size, Index FROM Win32_DiskDrive"))
            using (var results = searcher.Get())
            {
                foreach (ManagementObject o in results)
                {
                    using (o)
                    {
                        var id = o["PNPDeviceID"] as string;
                        if (string.IsNullOrEmpty(id)) continue;

                        long size = 0;
                        try { if (o["Size"] != null) size = Convert.ToInt64(o["Size"]); } catch { }

                        int index = -1;
                        try { if (o["Index"] != null) index = Convert.ToInt32(o["Index"]); } catch { }

                        map[id.ToUpperInvariant()] = new DiskDriveRecord { Size = size, Index = index };
                    }
                }
            }

            return map;
        }

        private static HashSet<int> ReadBootDiskIndices()
        {
            var set = new HashSet<int>();
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT DiskIndex, BootPartition FROM Win32_DiskPartition WHERE BootPartition=TRUE"))
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject o in results)
                    {
                        using (o)
                        {
                            if (o["DiskIndex"] == null) continue;
                            set.Add(Convert.ToInt32(o["DiskIndex"]));
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            return set;
        }

        /// <summary>Kind-Devnodes eines Geraets. WMI kennt diese Beziehung nicht, CfgMgr32 schon.</summary>
        private static List<string> GetChildDeviceIds(string parentInstanceId)
        {
            var children = new List<string>();

            uint parent;
            if (NativeMethods.CM_Locate_DevNodeW(out parent, parentInstanceId, NativeMethods.CM_LOCATE_DEVNODE_NORMAL)
                != NativeMethods.CR_SUCCESS)
            {
                return children;
            }

            uint child;
            if (NativeMethods.CM_Get_Child(out child, parent, 0) != NativeMethods.CR_SUCCESS)
                return children;

            while (true)
            {
                var id = NativeMethods.GetDeviceId(child);
                if (!string.IsNullOrEmpty(id))
                    children.Add(id);

                uint sibling;
                if (NativeMethods.CM_Get_Sibling(out sibling, child, 0) != NativeMethods.CR_SUCCESS)
                    break;

                child = sibling;
            }

            return children;
        }
    }
}
