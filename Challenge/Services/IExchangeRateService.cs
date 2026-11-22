using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Challenge.Models;

namespace Challenge.Services
{
    public interface IExchangeRateService
    {
        Task<IReadOnlyList<Currency>> GetCurrenciesAsync(CancellationToken cancellationToken);

        Task<ExchangeRate> GetLatestRateAsync(string sourceCurrency, string targetCurrency, CancellationToken cancellationToken);

        Task<IReadOnlyList<ExchangeRate>> GetHistoricRatesAsync(
            string sourceCurrency,
            string targetCurrency,
            DateTime startDate,
            DateTime endDate,
            CancellationToken cancellationToken);
    }
}
