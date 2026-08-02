using System;
using System.Collections.Generic;
using System.Management;
using System.ServiceProcess;
using NvmeDriverSwitch.Infrastructure;
using NvmeDriverSwitch.Models;

namespace NvmeDriverSwitch.Services
{
    /// <summary>
    /// Prueft Umstaende, unter denen ein Umschalten des NVMe-Stacks Aerger macht.
    /// Blockiert nichts - die Entscheidung bleibt beim Anwender.
    /// </summary>
    public sealed class PreflightService
    {
        private static readonly string[] RaidServices = { "iaStorVD", "iaStorAC", "iaStorA", "iaStorV", "vmd" };

        public List<PreflightIssue> Run()
        {
            var issues = new List<PreflightIssue>();

            CheckBitLocker(issues);
            CheckRaidControllers(issues);
            CheckVeraCrypt(issues);
            CheckStorageSpaces(issues);

            return issues;
        }

        private static void CheckBitLocker(List<PreflightIssue> issues)
        {
            try
            {
                var scope = new ManagementScope(@"\\.\root\CIMV2\Security\MicrosoftVolumeEncryption");
                scope.Connect();

                using (var searcher = new ManagementObjectSearcher(scope,
                    new ObjectQuery("SELECT DriveLetter, ProtectionStatus FROM Win32_EncryptableVolume")))
                using (var results = searcher.Get())
                {
                    var protectedDrives = new List<string>();

                    foreach (ManagementObject o in results)
                    {
                        using (o)
                        {
                            if (o["ProtectionStatus"] == null) continue;
                            if (Convert.ToUInt32(o["ProtectionStatus"]) == 0) continue;

                            var letter = o["DriveLetter"] as string;
                            protectedDrives.Add(string.IsNullOrEmpty(letter)
                                ? LocalizedStrings.Get("NoDriveLetter")
                                : letter);
                        }
                    }

                    if (protectedDrives.Count > 0)
                    {
                        issues.Add(new PreflightIssue(PreflightSeverity.Warning,
                            LocalizedStrings.Format("BitLockerTitleFormat",
                                string.Join(", ", protectedDrives.ToArray())),
                            LocalizedStrings.Get("BitLockerDetail")));
                    }
                }
            }
            catch (Exception ex)
            {
                AddUnknown(issues, "BitLocker", ex);
            }
        }

        private static void CheckRaidControllers(List<PreflightIssue> issues)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Name, Service FROM Win32_PnPEntity"))
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject o in results)
                    {
                        using (o)
                        {
                            var service = o["Service"] as string;
                            if (string.IsNullOrEmpty(service)) continue;

                            foreach (var raid in RaidServices)
                            {
                                if (!service.Equals(raid, StringComparison.OrdinalIgnoreCase)) continue;

                                issues.Add(new PreflightIssue(PreflightSeverity.Warning,
                                    LocalizedStrings.Format("RaidTitleFormat", service),
                                    LocalizedStrings.Format("RaidDetailFormat",
                                        o["Name"] as string ?? LocalizedStrings.Get("UnnamedDevice"))));
                                return;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AddUnknown(issues, "Intel RST / VMD", ex);
            }
        }

        private static void CheckVeraCrypt(List<PreflightIssue> issues)
        {
            try
            {
                foreach (var svc in ServiceController.GetServices())
                {
                    using (svc)
                    {
                        if (!svc.ServiceName.StartsWith("veracrypt", StringComparison.OrdinalIgnoreCase)) continue;

                        issues.Add(new PreflightIssue(PreflightSeverity.Warning,
                            LocalizedStrings.Get("VeraCryptTitle"),
                            LocalizedStrings.Get("VeraCryptDetail")));
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                AddUnknown(issues, "VeraCrypt", ex);
            }
        }

        private static void CheckStorageSpaces(List<PreflightIssue> issues)
        {
            try
            {
                var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
                scope.Connect();

                using (var searcher = new ManagementObjectSearcher(scope,
                    new ObjectQuery("SELECT FriendlyName FROM MSFT_StoragePool WHERE IsPrimordial=FALSE")))
                using (var results = searcher.Get())
                {
                    var pools = new List<string>();
                    foreach (ManagementObject o in results)
                    {
                        using (o)
                        {
                            pools.Add(o["FriendlyName"] as string ?? LocalizedStrings.Get("UnnamedPool"));
                        }
                    }

                    if (pools.Count > 0)
                    {
                        issues.Add(new PreflightIssue(PreflightSeverity.Warning,
                            LocalizedStrings.Format("StorageSpacesTitleFormat",
                                string.Join(", ", pools.ToArray())),
                            LocalizedStrings.Get("StorageSpacesDetail")));
                    }
                }
            }
            catch (Exception ex)
            {
                AddUnknown(issues, "Storage Spaces", ex);
            }
        }

        private static void AddUnknown(List<PreflightIssue> issues, string checkName, Exception ex)
        {
            var detail = LocalizedStrings.Get("PreflightUnknownDetail");
            if (ex != null && !string.IsNullOrWhiteSpace(ex.Message))
                detail += " " + LocalizedStrings.Format("TechnicalHintFormat", ex.Message);

            issues.Add(new PreflightIssue(PreflightSeverity.Warning,
                LocalizedStrings.Format("PreflightCheckFailedFormat", checkName), detail));
        }
    }
}
