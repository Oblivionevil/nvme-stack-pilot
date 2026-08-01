using Microsoft.Win32;
using NvmeDriverSwitch.Models;
using NvmeDriverSwitch.Services;
using Xunit;

namespace NvmeDriverSwitch.Tests
{
    public sealed class ConfigurationValidationTests
    {
        [Theory]
        [InlineData(RegistryValueKind.String, "1")]
        [InlineData(RegistryValueKind.DWord, 0)]
        [InlineData(RegistryValueKind.QWord, 1L)]
        public void NativeOverride_RejectsWrongKindOrValue(RegistryValueKind kind, object value)
        {
            Assert.False(FeatureOverrideService.IsExactDwordOne(kind, value));
        }

        [Fact]
        public void NativeOverride_AcceptsOnlyDwordOne()
        {
            Assert.True(FeatureOverrideService.IsExactDwordOne(RegistryValueKind.DWord, 1));
        }

        [Theory]
        [InlineData(RegistryValueKind.DWord, "Service", true)]
        [InlineData(RegistryValueKind.String, "", true)]
        [InlineData(RegistryValueKind.String, "Service", false)]
        public void SafeBoot_RejectsMissingWrongKindOrWrongValue(
            RegistryValueKind kind, object value, bool exists)
        {
            Assert.False(SafeBootService.IsValidServiceValue(kind, value, exists));
        }

        [Fact]
        public void SafeBoot_AcceptsStringService()
        {
            Assert.True(SafeBootService.IsValidServiceValue(
                RegistryValueKind.String, "Service", true));
        }

        [Theory]
        [InlineData(OverrideRole.Minimal, false)]
        [InlineData(OverrideRole.Missing, false)]
        [InlineData(OverrideRole.Unknown, false)]
        [InlineData(OverrideRole.Legacy, true)]
        public void OnlyKnownLegacyOverridesAreRemovable(OverrideRole role, bool expected)
        {
            Assert.Equal(expected, new OverrideEntry { Role = role }.IsRemovable);
        }
    }
}
