using System;
using NvmeDriverSwitch.Models;
using NvmeDriverSwitch.Services;
using Xunit;

namespace NvmeDriverSwitch.Tests
{
    public sealed class RebootMarkerTests
    {
        [Fact]
        public void MarkerRoundTripsAsSingleRegistryValue()
        {
            var original = new PendingChange
            {
                WrittenUtc = new DateTime(2026, 8, 1, 12, 34, 56, DateTimeKind.Utc),
                TargetMode = StackMode.Native,
                ConfigurationFingerprint = "abc|def/ghi="
            };

            var restored = RebootService.Deserialize(RebootService.Serialize(original));

            Assert.NotNull(restored);
            Assert.Equal(original.WrittenUtc, restored.WrittenUtc);
            Assert.Equal(original.TargetMode, restored.TargetMode);
            Assert.Equal(original.ConfigurationFingerprint, restored.ConfigurationFingerprint);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("v1|broken")]
        [InlineData("v2|0|0|YQ==")]
        [InlineData("v1|0|0|!")]
        public void InvalidMarkerIsRejected(string raw)
        {
            Assert.Null(RebootService.Deserialize(raw));
        }
    }
}
