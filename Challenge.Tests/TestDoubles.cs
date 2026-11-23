using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Challenge.Models;
using Challenge.Services;

namespace Challenge.Tests
{
    /// <summary>
    /// In-memory <see cref="IExchangeRateService"/> that counts calls, can fail, be slow, or be completed manually by the test.
    /// </summary>
    internal sealed class FakeExchangeRateService : IExchangeRateService
    {
        private static readonly Dictionary<string, decimal> KnownRates = new Dictionary<string, decimal>
        {
            ["USD"] = 1.1m,
            ["GBP"] = 0.86m,
            ["JPY"] = 160m,
        };

        private int _currenciesCalls;
        private int _latestCalls;
        private int _historicCalls;

        public IReadOnlyList<Currency> Currencies { get; set; } = new[]
        {
            new Currency("EUR", "Euro"),
            new Currency("GBP", "British Pound"),
            new Currency("JPY", "Japanese Yen"),
            new Currency("USD", "United States Dollar"),
        };

        /// <summary>When set, <see cref="GetCurrenciesAsync"/> fails with this exception.</summary>
        public Exception CurrenciesFailure { get; set; }

        /// <summary>When set, every rate query fails with this exception.</summary>
        public Exception RatesFailure { get; set; }

        /// <summary>When set, historic queries return this list instead of the generated default.</summary>
        public IReadOnlyList<ExchangeRate> HistoricRatesOverride { get; set; }

        public TimeSpan Delay { get; set; }

        /// <summary>When true, rate queries stay pending until the test completes them (they also ignore cancellation).</summary>
        public bool ManualCompletion { get; set; }

        public ConcurrentQueue<PendingCall<ExchangeRate>> PendingLatest { get; } = new ConcurrentQueue<PendingCall<ExchangeRate>>();

        public ConcurrentQueue<PendingCall<IReadOnlyList<ExchangeRate>>> PendingHistoric { get; } =
            new ConcurrentQueue<PendingCall<IReadOnlyList<ExchangeRate>>>();

        public int CurrenciesCalls => Volatile.Read(ref _currenciesCalls);

        public int LatestCalls => Volatile.Read(ref _latestCalls);

        public int HistoricCalls => Volatile.Read(ref _historicCalls);

        public string LastSource { get; private set; }

        public string LastTarget { get; private set; }

        public DateTime LastStartDate { get; private set; }

        public DateTime LastEndDate { get; private set; }

        public static decimal RateFor(string targetCurrency)
        {
            return KnownRates.TryGetValue(targetCurrency, out var rate)
                ? rate
                : targetCurrency.Aggregate(0m, (sum, c) => sum * 31 + c);
        }

        /// <summary>Default historic data: two days before today, rate + 0.1 and rate + 0.2.</summary>
        public static IReadOnlyList<ExchangeRate> DefaultHistoricRates(string targetCurrency)
        {
            var rate = RateFor(targetCurrency);
            return new[]
            {
                new ExchangeRate(DateTime.Today.AddDays(-2), rate + 0.1m),
                new ExchangeRate(DateTime.Today.AddDays(-1), rate + 0.2m),
            };
        }

        public Task<IReadOnlyList<Currency>> GetCurrenciesAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _currenciesCalls);
            return CurrenciesFailure != null
                ? Failed<IReadOnlyList<Currency>>(CurrenciesFailure)
                : Task.FromResult(Currencies);
        }

        public Task<ExchangeRate> GetLatestRateAsync(string sourceCurrency, string targetCurrency, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _latestCalls);
            LastSource = sourceCurrency;
            LastTarget = targetCurrency;

            if (ManualCompletion)
            {
                var pending = new PendingCall<ExchangeRate>(targetCurrency);
                PendingLatest.Enqueue(pending);
                return pending.Task;
            }

            return Respond(() => new ExchangeRate(DateTime.Today, RateFor(targetCurrency)));
        }

        public Task<IReadOnlyList<ExchangeRate>> GetHistoricRatesAsync(
            string sourceCurrency, string targetCurrency, DateTime startDate, DateTime endDate, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _historicCalls);
            LastStartDate = startDate;
            LastEndDate = endDate;

            if (ManualCompletion)
            {
                var pending = new PendingCall<IReadOnlyList<ExchangeRate>>(targetCurrency);
                PendingHistoric.Enqueue(pending);
                return pending.Task;
            }

            return Respond(() => HistoricRatesOverride ?? DefaultHistoricRates(targetCurrency));
        }

        private async Task<T> Respond<T>(Func<T> result)
        {
            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay).ConfigureAwait(false);
            }

            if (RatesFailure != null)
            {
                throw RatesFailure;
            }

            return result();
        }

        private static Task<T> Failed<T>(Exception exception)
        {
            var source = new TaskCompletionSource<T>();
            source.SetException(exception);
            return source.Task;
        }
    }

    internal sealed class PendingCall<T>
    {
        private readonly TaskCompletionSource<T> _completion = new TaskCompletionSource<T>();

        public PendingCall(string targetCurrency)
        {
            TargetCurrency = targetCurrency;
        }

        public string TargetCurrency { get; }

        public Task<T> Task => _completion.Task;

        public void Complete(T result) => _completion.SetResult(result);
    }

    /// <summary>
    /// <see cref="HttpMessageHandler"/> whose behaviour is defined by the test; records the last requested URI.
    /// </summary>
    internal sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        {
            _send = send;
        }

        public Uri LastRequestUri { get; private set; }

        public int RequestCount { get; private set; }

        public static FakeHttpMessageHandler Returning(HttpStatusCode statusCode, string content)
        {
            return new FakeHttpMessageHandler((request, token) =>
                Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(content) }));
        }

        /// <summary>Never answers; only completes when the request is cancelled (by timeout or by the caller).</summary>
        public static FakeHttpMessageHandler Hanging()
        {
            return new FakeHttpMessageHandler(async (request, token) =>
            {
                await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
                throw new InvalidOperationException("unreachable");
            });
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            RequestCount++;
            return _send(request, cancellationToken);
        }
    }
}
