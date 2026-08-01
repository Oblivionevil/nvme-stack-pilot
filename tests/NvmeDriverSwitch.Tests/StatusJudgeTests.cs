using System;
using NvmeDriverSwitch.Models;
using NvmeDriverSwitch.Services;
using Xunit;

namespace NvmeDriverSwitch.Tests
{
    public sealed class StatusJudgeTests
    {
        private static readonly DateTime BootUtc =
            new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void MatchingRuntimeAndConfigurationAreInSync()
        {
            var status = NewStatus();
            status.RuntimeMode = StackMode.Native;
            status.ConfiguredMode = StackMode.Native;

            Assert.Equal(Verdict.InSync, StatusJudge.Judge(status));
        }

        [Fact]
        public void MismatchWithoutMarkerIsNotAttributedToOldAppWrite()
        {
            var status = NewStatus();

            Assert.Equal(Verdict.UntrackedMismatch, StatusJudge.Judge(status));
        }

        [Fact]
        public void WrongFingerprintDoesNotCountAsMatchingMarker()
        {
            var status = NewStatus();
            status.PendingChange = Marker(BootUtc.AddMinutes(-5), "old-fingerprint");

            Assert.Equal(Verdict.UntrackedMismatch, StatusJudge.Judge(status));
        }

        [Fact]
        public void WrongTargetModeDoesNotCountAsMatchingMarker()
        {
            var status = NewStatus();
            status.PendingChange = Marker(BootUtc.AddMinutes(-5), status.ConfigurationFingerprint);
            status.PendingChange.TargetMode = StackMode.Classic;

            Assert.Equal(Verdict.UntrackedMismatch, StatusJudge.Judge(status));
        }

        [Fact]
        public void MatchingWriteAfterBootRequiresReboot()
        {
            var status = NewStatus();
            status.PendingChange = Marker(BootUtc.AddMinutes(5), status.ConfigurationFingerprint);

            Assert.Equal(Verdict.RebootRequired, StatusJudge.Judge(status));
        }

        [Fact]
        public void MatchingWriteBeforeBootWasIgnoredByWindows()
        {
            var status = NewStatus();
            status.PendingChange = Marker(BootUtc.AddMinutes(-5), status.ConfigurationFingerprint);

            Assert.Equal(Verdict.IgnoredByWindows, StatusJudge.Judge(status));
        }

        private static SystemStatus NewStatus()
        {
            return new SystemStatus
            {
                ConfiguredMode = StackMode.Native,
                RuntimeMode = StackMode.Classic,
                ConfigurationFingerprint = "current-fingerprint",
                LastBootUtc = BootUtc
            };
        }

        private static PendingChange Marker(DateTime writtenUtc, string fingerprint)
        {
            return new PendingChange
            {
                WrittenUtc = writtenUtc,
                TargetMode = StackMode.Native,
                ConfigurationFingerprint = fingerprint
            };
        }
    }
}
