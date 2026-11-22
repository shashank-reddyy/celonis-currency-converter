using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Challenge.Caching;
using Challenge.Models;

namespace Challenge.Services
{
    /// <summary>
    /// Decorator that answers repeated historic rate queries from a cache instead of querying the inner service again.
    /// </summary>
    public sealed class CachingExchangeRateService : IExchangeRateService
    {
        private readonly IExchangeRateService _inner;

        // Key: (source, target, start, end). Value tuples compare by value, so equal queries hit the same entry.
        private readonly QueryCache<(string Source, string Target, DateTime Start, DateTime End), IReadOnlyList<ExchangeRate>> _historicRatesCache;

        public CachingExchangeRateService(IExchangeRateService inner, ICacheEvictionStrategy evictionStrategy)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _historicRatesCache = new QueryCache<(string, string, DateTime, DateTime), IReadOnlyList<ExchangeRate>>(evictionStrategy);
        }

        public Task<IReadOnlyList<Currency>> GetCurrenciesAsync(CancellationToken cancellationToken)
        {
            return _inner.GetCurrenciesAsync(cancellationToken);
        }

        public Task<ExchangeRate> GetLatestRateAsync(string sourceCurrency, string targetCurrency, CancellationToken cancellationToken)
        {
            // The latest rate changes over time and is therefore always requested from the API.
            return _inner.GetLatestRateAsync(sourceCurrency, targetCurrency, cancellationToken);
        }

        public async Task<IReadOnlyList<ExchangeRate>> GetHistoricRatesAsync(
            string sourceCurrency,
            string targetCurrency,
            DateTime startDate,
            DateTime endDate,
            CancellationToken cancellationToken)
        {
            var key = (sourceCurrency, targetCurrency, startDate.Date, endDate.Date);
            if (_historicRatesCache.TryGet(key, out var cachedRates))
            {
                return cachedRates;
            }

            var rates = await _inner.GetHistoricRatesAsync(sourceCurrency, targetCurrency, startDate, endDate, cancellationToken)
                .ConfigureAwait(false);

            // Only successful results reach this point, so failures are never cached.
            _historicRatesCache.Set(key, rates);
            return rates;
        }
    }
}
