using System;
using NvmeDriverSwitch.Models;

namespace NvmeDriverSwitch.Services
{
    internal static class StatusJudge
    {
        public static Verdict Judge(SystemStatus status)
        {
            if (status == null) throw new ArgumentNullException("status");

            if (status.ConfiguredMode == status.RuntimeMode)
                return Verdict.InSync;

            var marker = status.PendingChange;
            bool markerMatches = marker != null &&
                marker.TargetMode == status.ConfiguredMode &&
                string.Equals(marker.ConfigurationFingerprint, status.ConfigurationFingerprint,
                    StringComparison.Ordinal);

            if (!markerMatches)
                return Verdict.UntrackedMismatch;

            if (status.LastBootUtc.HasValue && marker.WrittenUtc < status.LastBootUtc.Value)
                return Verdict.IgnoredByWindows;

            return Verdict.RebootRequired;
        }
    }
}
