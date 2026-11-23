using System;
using System.Collections.Generic;
using System.Linq;
using Challenge.Caching;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Challenge.Tests
{
    [TestClass]
    public class TimeBasedEvictionStrategyTests
    {
        private static readonly DateTime Now = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        [TestMethod]
        public void SelectsOnlyEntriesOlderThanMaxAge()
        {
            var strategy = new TimeBasedEvictionStrategy(TimeSpan.FromMinutes(10));
            var entries = new[]
            {
                new CacheEntryInfo<string>("expired", Now.AddMinutes(-11), 1),
                new CacheEntryInfo<string>("exactlyMaxAge", Now.AddMinutes(-10), 2),
                new CacheEntryInfo<string>("fresh", Now.AddMinutes(-1), 3),
            };

            var evicted = strategy.SelectKeysToEvict(entries, Now).ToList();

            CollectionAssert.AreEqual(new[] { "expired" }, evicted);
        }

        [TestMethod]
        public void SelectsAllEntriesWhenAllAreExpired()
        {
            var strategy = new TimeBasedEvictionStrategy(TimeSpan.FromMinutes(1));
            var entries = Enumerable.Range(0, 5).Select(i => new CacheEntryInfo<int>(i, Now.AddMinutes(-2 - i), i)).ToList();

            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3, 4 }, strategy.SelectKeysToEvict(entries, Now).ToList());
        }

        [TestMethod]
        public void SelectsNothingForEmptyCache()
        {
            var strategy = new TimeBasedEvictionStrategy(TimeSpan.FromMinutes(1));

            Assert.AreEqual(0, strategy.SelectKeysToEvict(new List<CacheEntryInfo<int>>(), Now).Count());
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(-1)]
        public void Constructor_WithNonPositiveMaxAge_Throws(int minutes)
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TimeBasedEvictionStrategy(TimeSpan.FromMinutes(minutes)));
        }
    }

    [TestClass]
    public class SizeBasedEvictionStrategyTests
    {
        private static readonly DateTime Now = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        [TestMethod]
        public void SelectsNothingWhileAtOrBelowMaxElements()
        {
            var strategy = new SizeBasedEvictionStrategy(3);
            var entries = Enumerable.Range(0, 3).Select(i => new CacheEntryInfo<int>(i, Now.AddSeconds(i), i)).ToList();

            Assert.AreEqual(0, strategy.SelectKeysToEvict(entries, Now).Count());
        }

        [TestMethod]
        public void SelectsOldestEntryWhenMaxElementsIsExceededByOne()
        {
            var strategy = new SizeBasedEvictionStrategy(2);
            var entries = new[]
            {
                new CacheEntryInfo<string>("newest", Now, 3),
                new CacheEntryInfo<string>("oldest", Now.AddMinutes(-5), 1),
                new CacheEntryInfo<string>("middle", Now.AddMinutes(-1), 2),
            };

            CollectionAssert.AreEqual(new[] { "oldest" }, strategy.SelectKeysToEvict(entries, Now).ToList());
        }

        [TestMethod]
        public void UsesInsertionOrderWhenTimestampsAreEqual()
        {
            var strategy = new SizeBasedEvictionStrategy(1);
            var entries = new[]
            {
                new CacheEntryInfo<string>("second", Now, 2),
                new CacheEntryInfo<string>("first", Now, 1),
            };

            CollectionAssert.AreEqual(new[] { "first" }, strategy.SelectKeysToEvict(entries, Now).ToList());
        }

        [TestMethod]
        public void SelectsAllExcessEntriesOldestFirst()
        {
            var strategy = new SizeBasedEvictionStrategy(2);
            var entries = Enumerable.Range(0, 5).Select(i => new CacheEntryInfo<int>(i, Now.AddSeconds(i), i)).ToList();

            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, strategy.SelectKeysToEvict(entries, Now).ToList());
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(-1)]
        public void Constructor_WithNonPositiveMaxElements_Throws(int maxElements)
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SizeBasedEvictionStrategy(maxElements));
        }
    }
}
