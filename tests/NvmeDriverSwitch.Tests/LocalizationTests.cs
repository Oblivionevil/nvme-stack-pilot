using System.Globalization;
using NvmeDriverSwitch.Infrastructure;
using Xunit;

namespace NvmeDriverSwitch.Tests
{
    public sealed class LocalizationTests
    {
        [Fact]
        public void EnglishResourcesAreTheNeutralFallback()
        {
            var previous = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");

                Assert.Equal("Refresh", LocalizedStrings.Get("RefreshButton"));
                Assert.Equal("Enable native driver", LocalizedStrings.Get("EnableNativeButton"));
            }
            finally
            {
                CultureInfo.CurrentUICulture = previous;
            }
        }

        [Fact]
        public void GermanResourcesAreUsedForGermanWindows()
        {
            var previous = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");

                Assert.Equal("Aktualisieren", LocalizedStrings.Get("RefreshButton"));
                Assert.Equal("Nativen Treiber aktivieren", LocalizedStrings.Get("EnableNativeButton"));
            }
            finally
            {
                CultureInfo.CurrentUICulture = previous;
            }
        }
    }
}
