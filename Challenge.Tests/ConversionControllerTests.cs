using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Challenge.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Challenge.Tests
{
    [TestClass]
    public class ConversionControllerTests
    {
        private const string LatestJson = @"{""amount"":1.0,""base"":""USD"",""date"":""2024-01-10"",""rates"":{""EUR"":0.91358}}";

        [TestMethod]
        public async Task GetCurrenciesAsync_ReturnsCurrenciesSortedByCode()
        {
            var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK, @"{""USD"":""United States Dollar"",""AUD"":""Australian Dollar"",""EUR"":""Euro""}");

            using (var controller = new ConversionController(new HttpClient(handler)))
            {
                var currencies = await controller.GetCurrenciesAsync(CancellationToken.None);

                Assert.AreEqual("https://api.frankfurter.dev/v1/currencies", handler.LastRequestUri.ToString());
                CollectionAssert.AreEqual(new[] { "AUD", "EUR", "USD" }, currencies.Select(c => c.Code).ToList());
                Assert.AreEqual("Euro", currencies[1].Name);
            }
        }

        [TestMethod]
        public async Task GetLatestRateAsync_RequestsCurrencyPairAndParsesDateAndRate()
        {
            var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK, LatestJson);

            using (var controller = new ConversionController(new HttpClient(handler)))
            {
                var rate = await controller.GetLatestRateAsync("USD", "EUR", CancellationToken.None);

                Assert.AreEqual("https://api.frankfurter.dev/v1/latest?base=USD&symbols=EUR", handler.LastRequestUri.ToString());
                Assert.AreEqual(new DateTime(2024, 1, 10), rate.Date);
                Assert.AreEqual(0.91358m, rate.Rate);
            }
        }

        [TestMethod]
        public async Task GetLatestRateAsync_WhenTargetRateIsMissing_Throws()
        {
            var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK, LatestJson);

            using (var controller = new ConversionController(new HttpClient(handler)))
            {
                await Assert.ThrowsExactlyAsync<ExchangeRateServiceException>(
                    () => controller.GetLatestRateAsync("USD", "GBP", CancellationToken.None));
            }
        }

        [TestMethod]
        public async Task GetHistoricRatesAsync_ReturnsRatesInsideTimeFrameOrderedByDate()
        {
            // For a holiday start date the API also returns the last business day before it (2023-12-29).
            const string json = @"{""amount"":1.0,""base"":""USD"",""start_date"":""2023-12-29"",""end_date"":""2024-01-03"",
                ""rates"":{""2024-01-03"":{""EUR"":0.91583},""2023-12-29"":{""EUR"":0.90498},""2024-01-02"":{""EUR"":0.91274}}}";
            var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK, json);

            using (var controller = new ConversionController(new HttpClient(handler)))
            {
                var rates = await controller.GetHistoricRatesAsync(
                    "USD", "EUR", new DateTime(2024, 1, 1), new DateTime(2024, 1, 3), CancellationToken.None);

                Assert.AreEqual("https://api.frankfurter.dev/v1/2024-01-01..2024-01-03?base=USD&symbols=EUR", handler.LastRequestUri.ToString());
                CollectionAssert.AreEqual(new[] { new DateTime(2024, 1, 2), new DateTime(2024, 1, 3) }, rates.Select(r => r.Date).ToList());
                CollectionAssert.AreEqual(new[] { 0.91274m, 0.91583m }, rates.Select(r => r.Rate).ToList());
            }
        }

        [TestMethod]
        public async Task GetHistoricRatesAsync_WhenNoRatesWerePublished_ReturnsEmptyList()
        {
            var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK, @"{""amount"":1.0,""base"":""USD"",""rates"":{}}");

            using (var controller = new ConversionController(new HttpClient(handler)))
            {
                var rates = await controller.GetHistoricRatesAsync(
                    "USD", "EUR", new DateTime(2024, 1, 6), new DateTime(2024, 1, 7), CancellationToken.None);

                Assert.AreEqual(0, rates.Count);
            }
        }

        [TestMethod]
        public async Task GetHistoricRatesAsync_WhenStartIsAfterEnd_ThrowsWithoutSendingRequest()
        {
            var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK, "{}");

            using (var controller = new ConversionController(new HttpClient(handler)))
            {
                await Assert.ThrowsExactlyAsync<ArgumentException>(() => controller.GetHistoricRatesAsync(
                    "USD", "EUR", new DateTime(2024, 1, 10), new DateTime(2024, 1, 1), CancellationToken.None));

                Assert.AreEqual(0, handler.RequestCount);
            }
        }

        [TestMethod]
        [DataRow((string)null, "EUR")]
        [DataRow("", "EUR")]
        [DataRow("USD", " ")]
        public async Task GetLatestRateAsync_WithMissingCurrency_ThrowsArgumentException(string source, string target)
        {
            using (var controller = new ConversionController(new HttpClient(FakeHttpMessageHandler.Returning(HttpStatusCode.OK, LatestJson))))
            {
                await Assert.ThrowsExactlyAsync<ArgumentException>(() => controller.GetLatestRateAsync(source, target, CancellationToken.None));
            }
        }

        [TestMethod]
        public async Task ErrorStatusCode_IsReportedAsExchangeRateServiceException()
        {
            var handler = FakeHttpMessageHandler.Returning((HttpStatusCode)422, @"{""message"":""invalid currency pair""}");

            using (var controller = new ConversionController(new HttpClient(handler)))
            {
                var exception = await Assert.ThrowsExactlyAsync<ExchangeRateServiceException>(
                    () => controller.GetLatestRateAsync("USD", "USD", CancellationToken.None));

                StringAssert.Contains(exception.Message, "422");
            }
        }

        [TestMethod]
        public async Task InvalidJson_IsReportedAsExchangeRateServiceException()
        {
            var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK, "<html>not json</html>");

            using (var controller = new ConversionController(new HttpClient(handler)))
            {
                await Assert.ThrowsExactlyAsync<ExchangeRateServiceException>(() => controller.GetCurrenciesAsync(CancellationToken.None));
            }
        }

        [TestMethod]
        public async Task InvalidDateInResponse_IsReportedAsExchangeRateServiceException()
        {
            var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK, @"{""date"":""10/01/2024"",""rates"":{""EUR"":0.9}}");

            using (var controller = new ConversionController(new HttpClient(handler)))
            {
                await Assert.ThrowsExactlyAsync<ExchangeRateServiceException>(() => controller.GetLatestRateAsync("USD", "EUR", CancellationToken.None));
            }
        }

        [TestMethod]
        public async Task NetworkFailure_IsReportedAsExchangeRateServiceException()
        {
            var handler = new FakeHttpMessageHandler((request, token) => throw new HttpRequestException("No such host is known."));

            using (var controller = new ConversionController(new HttpClient(handler)))
            {
                var exception = await Assert.ThrowsExactlyAsync<ExchangeRateServiceException>(
                    () => controller.GetLatestRateAsync("USD", "EUR", CancellationToken.None));

                Assert.IsInstanceOfType(exception.InnerException, typeof(HttpRequestException));
            }
        }

        [TestMethod]
        public async Task Timeout_IsReportedAsExchangeRateServiceException()
        {
            var httpClient = new HttpClient(FakeHttpMessageHandler.Hanging()) { Timeout = TimeSpan.FromMilliseconds(100) };

            using (var controller = new ConversionController(httpClient))
            {
                var exception = await Assert.ThrowsExactlyAsync<ExchangeRateServiceException>(
                    () => controller.GetLatestRateAsync("USD", "EUR", CancellationToken.None));

                StringAssert.Contains(exception.Message, "did not respond in time");
            }
        }

        [TestMethod]
        public async Task CancellationByCaller_IsNotTranslatedIntoServiceError()
        {
            var httpClient = new HttpClient(FakeHttpMessageHandler.Hanging()) { Timeout = TimeSpan.FromMinutes(1) };

            using (var controller = new ConversionController(httpClient))
            using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50)))
            {
                await Assert.ThrowsAsync<OperationCanceledException>(() => controller.GetLatestRateAsync("USD", "EUR", cancellation.Token));
            }
        }

        [TestMethod]
        public async Task Dispose_DoesNotDisposeExternallyProvidedHttpClient()
        {
            var httpClient = new HttpClient(FakeHttpMessageHandler.Returning(HttpStatusCode.OK, LatestJson));

            new ConversionController(httpClient).Dispose();

            var response = await httpClient.GetAsync("https://example.test/");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
