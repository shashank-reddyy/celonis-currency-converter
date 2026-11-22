using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Challenge.Models;
using Challenge.Services;
using Newtonsoft.Json;

﻿namespace Challenge
{
    /// <summary>
    /// Talks to the Frankfurter API (https://frankfurter.dev) over HTTPS and maps its JSON responses to domain models.
    /// </summary>
    public class ConversionController : IExchangeRateService, IDisposable
    {
        private const string Host = "api.frankfurter.dev";
        private const string DateFormat = "yyyy-MM-dd";
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;

        public ConversionController()
            : this(new HttpClient { Timeout = DefaultTimeout }, ownsHttpClient: true)
        {
        }

        public ConversionController(HttpClient httpClient)
            : this(httpClient, ownsHttpClient: false)
        {
        }

        private ConversionController(HttpClient httpClient, bool ownsHttpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _ownsHttpClient = ownsHttpClient;
        }

        public async Task<IReadOnlyList<Currency>> GetCurrenciesAsync(CancellationToken cancellationToken)
        {
            var currencies = await GetAsync<Dictionary<string, string>>("/v1/currencies", cancellationToken)
                .ConfigureAwait(false);

            return currencies
                .Select(pair => new Currency(pair.Key, pair.Value))
                .OrderBy(currency => currency.Code, StringComparer.Ordinal)
                .ToList();
        }

        public async Task<ExchangeRate> GetLatestRateAsync(string sourceCurrency, string targetCurrency, CancellationToken cancellationToken)
        {
            ValidateCurrencyPair(sourceCurrency, targetCurrency);

            var response = await GetAsync<LatestRatesResponse>(
                    $"/v1/latest?{BuildCurrencyQuery(sourceCurrency, targetCurrency)}",
                    cancellationToken)
                .ConfigureAwait(false);

            return new ExchangeRate(ParseDate(response.Date), GetRate(response.Rates, targetCurrency));
        }

        public async Task<IReadOnlyList<ExchangeRate>> GetHistoricRatesAsync(
            string sourceCurrency,
            string targetCurrency,
            DateTime startDate,
            DateTime endDate,
            CancellationToken cancellationToken)
        {
            ValidateCurrencyPair(sourceCurrency, targetCurrency);
            if (startDate.Date > endDate.Date)
            {
                throw new ArgumentException("The start date must not be after the end date.", nameof(startDate));
            }

            var range = $"{FormatDate(startDate)}..{FormatDate(endDate)}";
            var response = await GetAsync<TimeSeriesResponse>(
                    $"/v1/{range}?{BuildCurrencyQuery(sourceCurrency, targetCurrency)}",
                    cancellationToken)
                .ConfigureAwait(false);

            // The API returns the last rate published *before* the start date if the start date is not a
            // business day (e.g. a weekend), so only dates inside the requested time frame are kept.
            return (response.Rates ?? new Dictionary<string, Dictionary<string, decimal>>())
                .Select(pair => new ExchangeRate(ParseDate(pair.Key), GetRate(pair.Value, targetCurrency)))
                .Where(rate => rate.Date >= startDate.Date && rate.Date <= endDate.Date)
                .OrderBy(rate => rate.Date)
                .ToList();
        }

        public void Dispose()
        {
            if (_ownsHttpClient)
            {
                _httpClient.Dispose();
            }
        }

        private async Task<T> GetAsync<T>(string pathAndQuery, CancellationToken cancellationToken)
            where T : class
        {
            var requestUri = new Uri($"https://{Host}{pathAndQuery}");

            try
            {
                using (var response = await _httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false))
                {
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new ExchangeRateServiceException(
                            $"The exchange rate service returned an error ({(int)response.StatusCode} {response.ReasonPhrase}).");
                    }

                    return JsonConvert.DeserializeObject<T>(body)
                           ?? throw new ExchangeRateServiceException("The exchange rate service returned an empty response.");
                }
            }
            catch (HttpRequestException ex)
            {
                throw new ExchangeRateServiceException(
                    "Could not reach the exchange rate service. Please check your internet connection.", ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // HttpClient reports a timeout as a TaskCanceledException without our token being cancelled.
                throw new ExchangeRateServiceException("The exchange rate service did not respond in time.", ex);
            }
            catch (JsonException ex)
            {
                throw new ExchangeRateServiceException("The exchange rate service returned an unexpected response.", ex);
            }
        }

        private static string BuildCurrencyQuery(string sourceCurrency, string targetCurrency)
        {
            return $"base={Uri.EscapeDataString(sourceCurrency)}&symbols={Uri.EscapeDataString(targetCurrency)}";
        }

        private static void ValidateCurrencyPair(string sourceCurrency, string targetCurrency)
        {
            if (string.IsNullOrWhiteSpace(sourceCurrency))
            {
                throw new ArgumentException("A source currency is required.", nameof(sourceCurrency));
            }

            if (string.IsNullOrWhiteSpace(targetCurrency))
            {
                throw new ArgumentException("A target currency is required.", nameof(targetCurrency));
            }
        }

        private static decimal GetRate(IDictionary<string, decimal> rates, string currency)
        {
            if (rates == null || !rates.TryGetValue(currency, out var rate))
            {
                throw new ExchangeRateServiceException($"The exchange rate service returned no rate for {currency}.");
            }

            return rate;
        }

        private static string FormatDate(DateTime date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);

        private static DateTime ParseDate(string value)
        {
            if (!DateTime.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                throw new ExchangeRateServiceException($"The exchange rate service returned an invalid date '{value}'.");
            }

            return date;
        }
    }
}
