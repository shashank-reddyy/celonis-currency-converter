using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Challenge.Caching;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Challenge.Tests
{
    [TestClass]
    public class QueryCacheTests
    {
        [TestMethod]
        public void TryGet_OnEmptyCache_ReturnsFalse()
        {
            var cache = new QueryCache<string, int>(new SizeBasedEvictionStrategy(10));

            Assert.IsFalse(cache.TryGet("missing", out var value));
            Assert.AreEqual(0, value);
        }

        [TestMethod]
        public void Set_ThenTryGet_ReturnsStoredValue()
        {
            var cache = new QueryCache<string, int>(new SizeBasedEvictionStrategy(10));

            cache.Set("a", 42);

            Assert.IsTrue(cache.TryGet("a", out var value));
            Assert.AreEqual(42, value);
            Assert.AreEqual(1, cache.Count);
        }

        [TestMethod]
        public void Set_WithExistingKey_ReplacesValue()
        {
            var cache = new QueryCache<string, int>(new SizeBasedEvictionStrategy(10));

            cache.Set("a", 1);
            cache.Set("a", 2);

            Assert.IsTrue(cache.TryGet("a", out var value));
            Assert.AreEqual(2, value);
            Assert.AreEqual(1, cache.Count);
        }

        [TestMethod]
        public void SizeBased_RemovesOldestElementOnUpdate()
        {
            var cache = new QueryCache<string, int>(new SizeBasedEvictionStrategy(2));

            cache.Set("a", 1);
            cache.Set("b", 2);
            cache.Set("c", 3);

            Assert.AreEqual(2, cache.Count);
            Assert.IsFalse(cache.TryGet("a", out _));
            Assert.IsTrue(cache.TryGet("b", out _));
            Assert.IsTrue(cache.TryGet("c", out _));
        }

        [TestMethod]
        public void SizeBased_NeverHoldsMoreThanMaxElements()
        {
            var cache = new QueryCache<int, int>(new SizeBasedEvictionStrategy(5));

            for (var i = 0; i < 100; i++)
            {
                cache.Set(i, i);
                Assert.IsTrue(cache.Count <= 5);
            }

            CollectionAssert.AreEqual(new[] { 95, 96, 97, 98, 99 }, Enumerable.Range(0, 100).Where(i => cache.TryGet(i, out _)).ToList());
        }

        [TestMethod]
        public void SizeBased_UpdatingAnElementMakesItTheNewest()
        {
            var cache = new QueryCache<string, int>(new SizeBasedEvictionStrategy(2));

            cache.Set("a", 1);
            cache.Set("b", 2);
            cache.Set("a", 10);
            cache.Set("c", 3);

            Assert.IsFalse(cache.TryGet("b", out _));
            Assert.IsTrue(cache.TryGet("a", out var a));
            Assert.AreEqual(10, a);
        }

        [TestMethod]
        public void TimeBased_KeepsElementsYoungerThanMaxAge()
        {
            var cache = new QueryCache<string, int>(new TimeBasedEvictionStrategy(TimeSpan.FromMinutes(10)));

            cache.Set("a", 1);
            cache.Set("b", 2);

            Assert.AreEqual(2, cache.Count);
        }

        [TestMethod]
        public void TimeBased_RemovesExpiredElementsOnNextUpdate()
        {
            var cache = new QueryCache<string, int>(new TimeBasedEvictionStrategy(TimeSpan.FromMilliseconds(100)));
            cache.Set("old1", 1);
            cache.Set("old2", 2);

            Thread.Sleep(300);
            cache.Set("new", 3);

            Assert.AreEqual(1, cache.Count);
            Assert.IsFalse(cache.TryGet("old1", out _));
            Assert.IsFalse(cache.TryGet("old2", out _));
            Assert.IsTrue(cache.TryGet("new", out _));
        }

        [TestMethod]
        public void TimeBased_CleansUpOnUpdateOnly()
        {
            // As specified: the cache is cleaned up when it is updated, not when it is read.
            var cache = new QueryCache<string, int>(new TimeBasedEvictionStrategy(TimeSpan.FromMilliseconds(100)));
            cache.Set("a", 1);

            Thread.Sleep(300);

            Assert.IsTrue(cache.TryGet("a", out _));
        }

        [TestMethod]
        public void Constructor_WithoutStrategy_Throws()
        {
            Assert.ThrowsExactly<ArgumentNullException>(() => new QueryCache<string, int>(null));
        }

        [TestMethod]
        public void ConcurrentReadsAndWrites_SizeBased_KeepCacheConsistent()
        {
            const int maxElements = 50;
            var cache = new QueryCache<int, int>(new SizeBasedEvictionStrategy(maxElements));

            var result = ConcurrencyStress.Run(cache, threads: 16, operationsPerThread: 10000);

            Assert.AreEqual(0, result.Exceptions, result.FirstException);
            Assert.AreEqual(0, result.CorruptedReads, "A read returned a value that was never written for its key.");
            Assert.IsTrue(result.MaxObservedCount <= maxElements, $"Count reached {result.MaxObservedCount}.");
            Assert.AreEqual(maxElements, cache.Count);
            Assert.IsTrue(result.Hits > 0, "The stress test should read cached values concurrently.");
        }

        [TestMethod]
        public void ConcurrentReadsAndWrites_TimeBased_KeepCacheConsistent()
        {
            var cache = new QueryCache<int, int>(new TimeBasedEvictionStrategy(TimeSpan.FromMilliseconds(50)));

            var result = ConcurrencyStress.Run(cache, threads: 16, operationsPerThread: 5000);

            Assert.AreEqual(0, result.Exceptions, result.FirstException);
            Assert.AreEqual(0, result.CorruptedReads);

            Thread.Sleep(200);
            cache.Set(-1, -10);
            Assert.AreEqual(1, cache.Count, "One update after MAX_AGE should remove every expired element.");
        }
    }

    /// <summary>
    /// Hammers a cache from many threads that start at the same moment. Every key is always written with the value
    /// key * 10, so any other value read back means the cache's internal state was corrupted.
    /// </summary>
    internal static class ConcurrencyStress
    {
        public static Result Run(QueryCache<int, int> cache, int threads, int operationsPerThread)
        {
            var result = new Result();
            var start = new ManualResetEventSlim(false);
            var workers = Enumerable.Range(0, threads).Select(seed => new Thread(() =>
            {
                var random = new Random(seed);
                start.Wait();
                for (var i = 0; i < operationsPerThread; i++)
                {
                    var key = random.Next(500);
                    try
                    {
                        var operation = random.Next(100);
                        if (operation < 45)
                        {
                            cache.Set(key, key * 10);
                        }
                        else if (operation < 95)
                        {
                            if (cache.TryGet(key, out var value))
                            {
                                Interlocked.Increment(ref result.Hits);
                                if (value != key * 10)
                                {
                                    Interlocked.Increment(ref result.CorruptedReads);
                                }
                            }
                        }
                        else
                        {
                            var count = cache.Count;
                            lock (result)
                            {
                                result.MaxObservedCount = Math.Max(result.MaxObservedCount, count);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref result.Exceptions);
                        lock (result)
                        {
                            result.FirstException = result.FirstException ?? ex.ToString();
                        }
                    }
                }
            })
            { IsBackground = true }).ToList();

            workers.ForEach(worker => worker.Start());
            start.Set();
            workers.ForEach(worker => Assert.IsTrue(worker.Join(TimeSpan.FromSeconds(30)), "Worker thread did not finish (possible deadlock)."));
            return result;
        }

        public sealed class Result
        {
            public int Exceptions;
            public int CorruptedReads;
            public int Hits;
            public int MaxObservedCount;
            public string FirstException;
        }
    }
}
