using System;
using System.Configuration;
using Challenge.Caching;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Challenge.Tests
{
    [TestClass]
    public class CacheSettingsTests
    {
        [TestMethod]
        [DataRow("TimeBased")]
        [DataRow("timebased")]
        [DataRow("TIMEBASED")]
        public void TimeBased_IsParsedCaseInsensitivelyAndCreatesStrategyWithMaxAge(string value)
        {
            var settings = CacheSettings.Create(value, maxAgeMinutes: 15, maxElements: 0);

            var strategy = settings.CreateEvictionStrategy();

            Assert.AreEqual(CacheEvictionStrategyType.TimeBased, settings.Strategy);
            Assert.IsInstanceOfType(strategy, typeof(TimeBasedEvictionStrategy));
            Assert.AreEqual(TimeSpan.FromMinutes(15), ((TimeBasedEvictionStrategy)strategy).MaxAge);
        }

        [TestMethod]
        public void SizeBased_CreatesStrategyWithMaxElements()
        {
            var settings = CacheSettings.Create("SizeBased", maxAgeMinutes: 0, maxElements: 5);

            var strategy = settings.CreateEvictionStrategy();

            Assert.AreEqual(CacheEvictionStrategyType.SizeBased, settings.Strategy);
            Assert.IsInstanceOfType(strategy, typeof(SizeBasedEvictionStrategy));
            Assert.AreEqual(5, ((SizeBasedEvictionStrategy)strategy).MaxElements);
        }

        [TestMethod]
        [DataRow("Unknown", 10, 10, DisplayName = "Unknown strategy name")]
        [DataRow("", 10, 10, DisplayName = "Empty strategy name")]
        [DataRow((string)null, 10, 10, DisplayName = "Missing strategy name")]
        [DataRow("7", 10, 10, DisplayName = "Numeric value that is not a defined strategy")]
        [DataRow("TimeBased", 0, 10, DisplayName = "TimeBased with MAX_AGE = 0")]
        [DataRow("TimeBased", -5, 10, DisplayName = "TimeBased with negative MAX_AGE")]
        [DataRow("SizeBased", 10, 0, DisplayName = "SizeBased with MAX_ELEMENTS = 0")]
        [DataRow("SizeBased", 10, -1, DisplayName = "SizeBased with negative MAX_ELEMENTS")]
        public void InvalidSettings_ThrowConfigurationError(string strategy, int maxAgeMinutes, int maxElements)
        {
            Assert.ThrowsExactly<ConfigurationErrorsException>(() => CacheSettings.Create(strategy, maxAgeMinutes, maxElements));
        }

        [TestMethod]
        public void OnlyTheParameterOfTheSelectedStrategyIsValidated()
        {
            CacheSettings.Create("TimeBased", maxAgeMinutes: 10, maxElements: 0);
            CacheSettings.Create("SizeBased", maxAgeMinutes: 0, maxElements: 10);
        }

        [TestMethod]
        public void FromApplicationSettings_ReadsTheShippedDefaults()
        {
            var settings = CacheSettings.FromApplicationSettings();

            Assert.AreEqual(CacheEvictionStrategyType.TimeBased, settings.Strategy);
            Assert.AreEqual(10, settings.MaxAgeMinutes);
            Assert.AreEqual(20, settings.MaxElements);
        }
    }
}
