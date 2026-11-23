using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Challenge.Caching;
using Challenge.Models;
using Challenge.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Challenge.Tests
{
    [TestClass]
    public class CachingExchangeRateServiceTests
    {
        private static readonly DateTime StartDate = new DateTime(2024, 1, 1);
        private static readonly DateTime EndDate = new DateTime(2024, 1, 31);

        private FakeExchangeRateService _api;

        [TestInitialize]
        public void SetUp()
        {
            _api = new FakeExchangeRateService();
        }

        [TestMethod]
        public async Task RepeatedHistoricQuery_IsAnsweredFromCache()
        {
            var service = CreateService(new SizeBasedEvictionStrategy(10));

            var first = await Historic(service, "EUR", "USD", StartDate, EndDate);
            var second = await Historic(service, "EUR", "USD", StartDate, EndDate);

            Assert.AreEqual(1, _api.HistoricCalls);
            Assert.AreSame(first, second);
        }

        [TestMethod]
        public async Task EachQueryParameter_IsPartOfTheCacheKey()
        {
            var service = CreateService(new SizeBasedEvictionStrategy(10));

            await Historic(service, "EUR", "USD", StartDate, EndDate);
            await Historic(service, "GBP", "USD", StartDate, EndDate);
            await Historic(service, "EUR", "JPY", StartDate, EndDate);
            await Historic(service, "EUR", "USD", StartDate.AddDays(1), EndDate);
            await Historic(service, "EUR", "USD", StartDate, EndDate.AddDays(-1));
            await Historic(service, "EUR", "USD", StartDate, EndDate);

            Assert.AreEqual(5, _api.HistoricCalls);
        }

        [TestMethod]
        public async Task TimeOfDay_IsIgnoredInTheCacheKey()
        {
            var service = CreateService(new SizeBasedEvictionStrategy(10));

            await Historic(service, "EUR", "USD", StartDate, EndDate);
            await Historic(service, "EUR", "USD", StartDate.AddHours(9), EndDate.AddHours(17));

            Assert.AreEqual(1, _api.HistoricCalls);
        }

        [TestMethod]
        public async Task FailedQuery_IsNotCached()
        {
            var service = CreateService(new SizeBasedEvictionStrategy(10));
            _api.RatesFailure = new ExchangeRateServiceException("offline");
            await Assert.ThrowsExactlyAsync<ExchangeRateServiceException>(() => Historic(service, "EUR", "USD", StartDate, EndDate));

            _api.RatesFailure = null;
            await Historic(service, "EUR", "USD", StartDate, EndDate);

            Assert.AreEqual(2, _api.HistoricCalls);
        }

        [TestMethod]
        public async Task LatestRate_IsNeverCached()
        {
            var service = CreateService(new SizeBasedEvictionStrategy(10));

            await service.GetLatestRateAsync("EUR", "USD", CancellationToken.None);
            await service.GetLatestRateAsync("EUR", "USD", CancellationToken.None);

            Assert.AreEqual(2, _api.LatestCalls);
        }

        [TestMethod]
        public async Task Currencies_ArePassedThrough()
        {
            var service = CreateService(new SizeBasedEvictionStrategy(10));

            var currencies = await service.GetCurrenciesAsync(CancellationToken.None);

            Assert.AreSame(_api.Currencies, currencies);
        }

        [TestMethod]
        public async Task SizeBased_EvictedQuery_IsRequestedAgain()
        {
            var service = CreateService(new SizeBasedEvictionStrategy(2));
            await Historic(service, "EUR", "USD", StartDate, EndDate);
            await Historic(service, "EUR", "GBP", StartDate, EndDate);
            await Historic(service, "EUR", "JPY", StartDate, EndDate);

            await Historic(service, "EUR", "GBP", StartDate, EndDate);
            await Historic(service, "EUR", "JPY", StartDate, EndDate);
            Assert.AreEqual(3, _api.HistoricCalls, "The two newest queries should still be cached.");

            await Historic(service, "EUR", "USD", StartDate, EndDate);
            Assert.AreEqual(4, _api.HistoricCalls, "The oldest query should have been evicted.");
        }

        [TestMethod]
        public async Task TimeBased_ExpiredQuery_IsRequestedAgainAfterNextUpdate()
        {
            var service = CreateService(new TimeBasedEvictionStrategy(TimeSpan.FromMilliseconds(100)));
            await Historic(service, "EUR", "USD", StartDate, EndDate);

            await Task.Delay(300);
            await Historic(service, "EUR", "GBP", StartDate, EndDate);
            await Historic(service, "EUR", "USD", StartDate, EndDate);

            Assert.AreEqual(3, _api.HistoricCalls);
        }

        [TestMethod]
        public async Task ConcurrentCallers_EachGetTheResultForTheirOwnQuery()
        {
            _api.Delay = TimeSpan.FromMilliseconds(20);
            var service = CreateService(new SizeBasedEvictionStrategy(100));
            var targets = Enumerable.Range(0, 10).Select(i => "T" + i).ToArray();

            Func<Task<bool[]>> wave = () => Task.WhenAll(Enumerable.Range(0, 300).Select(i => Task.Run(async () =>
            {
                var target = targets[i % targets.Length];
                var rates = await service.GetHistoricRatesAsync("EUR", target, StartDate, EndDate, CancellationToken.None);
                return rates.All(rate => rate.Rate > FakeExchangeRateService.RateFor(target)
                                         && rate.Rate <= FakeExchangeRateService.RateFor(target) + 0.2m);
            })));

            var firstWave = await wave();
            var callsAfterFirstWave = _api.HistoricCalls;
            var secondWave = await wave();

            Assert.IsTrue(firstWave.Concat(secondWave).All(correct => correct), "Every caller must receive the rates of its own query.");
            Assert.AreEqual(callsAfterFirstWave, _api.HistoricCalls, "Once cached, concurrent callers must not reach the API.");
        }

        private CachingExchangeRateService CreateService(ICacheEvictionStrategy strategy)
        {
            return new CachingExchangeRateService(_api, strategy);
        }

        private static Task<IReadOnlyList<ExchangeRate>> Historic(
            IExchangeRateService service, string source, string target, DateTime start, DateTime end)
        {
            return service.GetHistoricRatesAsync(source, target, start, end, CancellationToken.None);
        }
    }
}
