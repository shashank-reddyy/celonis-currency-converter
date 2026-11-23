using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Challenge.Models;
using Challenge.Services;
using Challenge.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Challenge.Tests
{
    [TestClass]
    public class MainViewModelTests
    {
        private static readonly DateTime Today = DateTime.Today;

        private CultureInfo _originalCulture;
        private FakeExchangeRateService _service;
        private MainViewModel _viewModel;

        [TestInitialize]
        public void SetUp()
        {
            // Amount parsing uses the current culture; make the tests independent of the machine's regional settings.
            _originalCulture = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            _service = new FakeExchangeRateService();
        }

        [TestCleanup]
        public void TearDown()
        {
            Thread.CurrentThread.CurrentCulture = _originalCulture;
        }

        // ------------------------------------------------------------------ initialization

        [TestMethod]
        public async Task Initialize_LoadsCurrenciesAndSelectsDefaults()
        {
            await InitializeAsync();

            Assert.AreEqual(4, _viewModel.Currencies.Count);
            Assert.AreEqual("EUR", _viewModel.SelectedSourceCurrency.Code);
            Assert.AreEqual("USD", _viewModel.SelectedTargetCurrency.Code);
            Assert.AreEqual(Today.AddDays(-30), _viewModel.StartDate);
            Assert.AreEqual(Today, _viewModel.EndDate);
            Assert.AreEqual("1", _viewModel.AmountText);
        }

        [TestMethod]
        public async Task Initialize_ShowsLatestConversionAndHistoricRatesNewestFirst()
        {
            await InitializeAsync();

            Assert.AreEqual(1.1m, _viewModel.LatestConvertedAmount);
            Assert.AreEqual(Today, _viewModel.LatestRateDate);
            CollectionAssert.AreEqual(new[] { Today.AddDays(-1), Today.AddDays(-2) }, _viewModel.HistoricConversions.Select(c => c.Date).ToList());
            CollectionAssert.AreEqual(new[] { 1.3m, 1.2m }, _viewModel.HistoricConversions.Select(c => c.ConvertedAmount).ToList());
            Assert.IsTrue(_viewModel.HistoricConversions.All(c => c.TargetCurrency == "USD"));
            Assert.IsFalse(_viewModel.IsBusy);
            Assert.IsFalse(_viewModel.HasErrorMessage);
            Assert.IsFalse(_viewModel.HasNoHistoricData);
        }

        [TestMethod]
        public async Task Initialize_QueriesTheDefaultTimeFrame()
        {
            await InitializeAsync();

            Assert.AreEqual(1, _service.LatestCalls);
            Assert.AreEqual(1, _service.HistoricCalls);
            Assert.AreEqual("EUR", _service.LastSource);
            Assert.AreEqual("USD", _service.LastTarget);
            Assert.AreEqual(Today.AddDays(-30), _service.LastStartDate);
            Assert.AreEqual(Today, _service.LastEndDate);
        }

        [TestMethod]
        public async Task Initialize_WhenCurrenciesCannotBeLoaded_ShowsErrorAndRetryRecovers()
        {
            _service.CurrenciesFailure = new ExchangeRateServiceException("The service is down.");
            await InitializeAsync();

            StringAssert.Contains(_viewModel.ErrorMessage, "Could not load the list of currencies");
            Assert.AreEqual(0, _viewModel.Currencies.Count);
            Assert.AreEqual(0, _service.LatestCalls);
            Assert.IsTrue(_viewModel.RetryCommand.CanExecute(null));

            _service.CurrenciesFailure = null;
            _viewModel.RetryCommand.Execute(null);
            await _viewModel.PendingRefresh;

            Assert.AreEqual(4, _viewModel.Currencies.Count);
            Assert.AreEqual(1.1m, _viewModel.LatestConvertedAmount);
            Assert.IsFalse(_viewModel.HasErrorMessage);
        }

        // ------------------------------------------------------------------ amount

        [TestMethod]
        public async Task ChangingAmount_RecalculatesLocallyWithoutQueryingAgain()
        {
            await InitializeAsync();

            _viewModel.AmountText = "10";

            Assert.AreEqual(11m, _viewModel.LatestConvertedAmount);
            CollectionAssert.AreEqual(new[] { 13m, 12m }, _viewModel.HistoricConversions.Select(c => c.ConvertedAmount).ToList());
            Assert.AreEqual(1, _service.LatestCalls);
            Assert.AreEqual(1, _service.HistoricCalls);
        }

        [TestMethod]
        public async Task Amount_WithDecimalsAndThousandsSeparator_IsParsed()
        {
            await InitializeAsync();

            _viewModel.AmountText = "1,000.5";

            Assert.IsFalse(_viewModel.HasErrorsFor(nameof(MainViewModel.AmountText)));
            Assert.AreEqual(1100.55m, _viewModel.LatestConvertedAmount);
        }

        [TestMethod]
        [DataRow("", DisplayName = "Empty")]
        [DataRow("   ", DisplayName = "Whitespace")]
        [DataRow("abc", DisplayName = "Not a number")]
        [DataRow("12abc", DisplayName = "Partially numeric")]
        [DataRow("0", DisplayName = "Zero")]
        [DataRow("-5", DisplayName = "Negative")]
        [DataRow("1000000000001", DisplayName = "Above maximum")]
        public async Task InvalidAmount_IsReportedAndClearsConvertedValues(string amount)
        {
            await InitializeAsync();

            _viewModel.AmountText = amount;

            Assert.IsTrue(_viewModel.HasErrorsFor(nameof(MainViewModel.AmountText)));
            Assert.IsTrue(_viewModel.GetErrors(nameof(MainViewModel.AmountText)).Cast<string>().Any());
            Assert.IsTrue(_viewModel.AllErrors.Count > 0);
            Assert.IsNull(_viewModel.LatestConvertedAmount);
            Assert.AreEqual(0, _viewModel.HistoricConversions.Count);
            Assert.IsFalse(_viewModel.HasNoHistoricData, "An invalid amount must not be reported as 'no data published'.");
        }

        [TestMethod]
        public async Task CorrectingAnInvalidAmount_RestoresResultsWithoutQueryingAgain()
        {
            await InitializeAsync();
            _viewModel.AmountText = "abc";

            _viewModel.AmountText = "2";

            Assert.IsFalse(_viewModel.HasErrors);
            Assert.AreEqual(2.2m, _viewModel.LatestConvertedAmount);
            Assert.AreEqual(2, _viewModel.HistoricConversions.Count);
            Assert.AreEqual(1, _service.LatestCalls);
        }

        // ------------------------------------------------------------------ currencies

        [TestMethod]
        public async Task ChangingTargetCurrency_QueriesRatesForTheNewCurrency()
        {
            await InitializeAsync();

            _viewModel.SelectedTargetCurrency = Currency("GBP");
            await _viewModel.PendingRefresh;

            Assert.AreEqual(2, _service.LatestCalls);
            Assert.AreEqual("GBP", _service.LastTarget);
            Assert.AreEqual(0.86m, _viewModel.LatestConvertedAmount);
            Assert.IsTrue(_viewModel.HistoricConversions.All(c => c.TargetCurrency == "GBP"));
        }

        [TestMethod]
        public async Task ChangingSourceCurrency_QueriesRatesForTheNewCurrency()
        {
            await InitializeAsync();

            _viewModel.SelectedSourceCurrency = Currency("GBP");
            await _viewModel.PendingRefresh;

            Assert.AreEqual(2, _service.LatestCalls);
            Assert.AreEqual("GBP", _service.LastSource);
        }

        [TestMethod]
        public async Task SameSourceAndTargetCurrency_IsRejectedWithoutQuerying()
        {
            await InitializeAsync();

            _viewModel.SelectedTargetCurrency = Currency("EUR");
            await _viewModel.PendingRefresh;

            Assert.IsTrue(_viewModel.HasErrorsFor(nameof(MainViewModel.SelectedTargetCurrency)));
            Assert.AreEqual(1, _service.LatestCalls);
            Assert.IsNull(_viewModel.LatestConvertedAmount);
            Assert.AreEqual(0, _viewModel.HistoricConversions.Count);
        }

        [TestMethod]
        public async Task ResolvingSameCurrencyConflict_ClearsErrorAndQueriesAgain()
        {
            await InitializeAsync();
            _viewModel.SelectedTargetCurrency = Currency("EUR");
            await _viewModel.PendingRefresh;

            _viewModel.SelectedSourceCurrency = Currency("USD");
            await _viewModel.PendingRefresh;

            Assert.IsFalse(_viewModel.HasErrors);
            Assert.AreEqual(2, _service.LatestCalls);
            Assert.AreEqual(FakeExchangeRateService.RateFor("EUR"), _viewModel.LatestConvertedAmount);
        }

        [TestMethod]
        public async Task ClearingACurrency_IsReportedAsMissing()
        {
            await InitializeAsync();

            _viewModel.SelectedSourceCurrency = null;
            await _viewModel.PendingRefresh;

            Assert.IsTrue(_viewModel.HasErrorsFor(nameof(MainViewModel.SelectedSourceCurrency)));
            Assert.AreEqual(1, _service.LatestCalls);
        }

        // ------------------------------------------------------------------ dates

        [TestMethod]
        public async Task ChangingTheTimeFrame_QueriesTheNewRange()
        {
            await InitializeAsync();

            _viewModel.StartDate = Today.AddDays(-10);
            await _viewModel.PendingRefresh;
            _viewModel.EndDate = Today.AddDays(-5);
            await _viewModel.PendingRefresh;

            Assert.AreEqual(3, _service.HistoricCalls);
            Assert.AreEqual(Today.AddDays(-10), _service.LastStartDate);
            Assert.AreEqual(Today.AddDays(-5), _service.LastEndDate);
        }

        [TestMethod]
        public async Task StartDateAfterEndDate_IsRejectedWithoutQuerying_AndFixingItQueriesAgain()
        {
            await InitializeAsync();
            _viewModel.EndDate = Today.AddDays(-20);
            await _viewModel.PendingRefresh;
            var callsBefore = _service.HistoricCalls;

            _viewModel.StartDate = Today.AddDays(-10);
            await _viewModel.PendingRefresh;

            Assert.IsTrue(_viewModel.HasErrorsFor(nameof(MainViewModel.EndDate)));
            Assert.AreEqual(callsBefore, _service.HistoricCalls);
            Assert.AreEqual(0, _viewModel.HistoricConversions.Count);

            _viewModel.EndDate = Today;
            await _viewModel.PendingRefresh;

            Assert.IsFalse(_viewModel.HasErrors);
            Assert.AreEqual(callsBefore + 1, _service.HistoricCalls);
        }

        [TestMethod]
        public async Task FutureDates_AreRejected()
        {
            await InitializeAsync();

            _viewModel.EndDate = Today.AddDays(1);
            _viewModel.StartDate = Today.AddDays(1);

            Assert.IsTrue(_viewModel.HasErrorsFor(nameof(MainViewModel.EndDate)));
            Assert.IsTrue(_viewModel.HasErrorsFor(nameof(MainViewModel.StartDate)));
        }

        [TestMethod]
        public async Task DateBeforeFirstAvailableRate_IsRejected()
        {
            await InitializeAsync();

            _viewModel.StartDate = MainViewModel.EarliestAvailableDate.AddDays(-1);

            Assert.IsTrue(_viewModel.HasErrorsFor(nameof(MainViewModel.StartDate)));
        }

        [TestMethod]
        public async Task ClearedDate_IsReportedAsMissing()
        {
            await InitializeAsync();

            _viewModel.StartDate = null;

            Assert.IsTrue(_viewModel.HasErrorsFor(nameof(MainViewModel.StartDate)));
        }

        [TestMethod]
        public async Task TimeFrameWithoutPublishedRates_ShowsNoDataHint()
        {
            _service.HistoricRatesOverride = Array.Empty<ExchangeRate>();

            await InitializeAsync();

            Assert.IsTrue(_viewModel.HasNoHistoricData);
            Assert.AreEqual(0, _viewModel.HistoricConversions.Count);
            Assert.AreEqual(1.1m, _viewModel.LatestConvertedAmount);
        }

        // ------------------------------------------------------------------ errors

        [TestMethod]
        public async Task ServiceFailure_ShowsErrorClearsResultsAndRetryRecovers()
        {
            await InitializeAsync();
            _service.RatesFailure = new ExchangeRateServiceException("The service is down.");

            _viewModel.SelectedTargetCurrency = Currency("GBP");
            await _viewModel.PendingRefresh;

            Assert.AreEqual("The service is down.", _viewModel.ErrorMessage);
            Assert.IsTrue(_viewModel.HasErrorMessage);
            Assert.IsNull(_viewModel.LatestConvertedAmount);
            Assert.AreEqual(0, _viewModel.HistoricConversions.Count);
            Assert.IsFalse(_viewModel.IsBusy);

            _service.RatesFailure = null;
            _viewModel.RetryCommand.Execute(null);
            await _viewModel.PendingRefresh;

            Assert.IsFalse(_viewModel.HasErrorMessage);
            Assert.AreEqual(0.86m, _viewModel.LatestConvertedAmount);
        }

        [TestMethod]
        public async Task UnexpectedException_IsShownInsteadOfBeingLost()
        {
            await InitializeAsync();
            _service.RatesFailure = new InvalidOperationException("boom");

            _viewModel.SelectedTargetCurrency = Currency("GBP");
            await _viewModel.PendingRefresh;

            StringAssert.Contains(_viewModel.ErrorMessage, "boom");
            Assert.IsFalse(_viewModel.IsBusy);
        }

        // ------------------------------------------------------------------ async behaviour

        [TestMethod]
        public async Task RapidChanges_AreDebouncedIntoOneRequestForTheLastSelection()
        {
            await InitializeAsync(TimeSpan.FromMilliseconds(200));

            _viewModel.SelectedTargetCurrency = Currency("GBP");
            _viewModel.SelectedTargetCurrency = Currency("JPY");
            _viewModel.StartDate = Today.AddDays(-7);
            await _viewModel.PendingRefresh;

            Assert.AreEqual(2, _service.LatestCalls, "Only the initial query and one query for the final selection expected.");
            Assert.AreEqual("JPY", _service.LastTarget);
            Assert.AreEqual(Today.AddDays(-7), _service.LastStartDate);
        }

        [TestMethod]
        public async Task OutdatedResponse_ArrivingLate_DoesNotOverwriteNewerResults()
        {
            await InitializeAsync();
            _service.ManualCompletion = true;

            _viewModel.SelectedTargetCurrency = Currency("GBP");
            var outdatedRefresh = _viewModel.PendingRefresh;
            _viewModel.SelectedTargetCurrency = Currency("JPY");
            var currentRefresh = _viewModel.PendingRefresh;

            var latest = _service.PendingLatest.ToArray();
            var historic = _service.PendingHistoric.ToArray();
            Assert.AreEqual("GBP", latest[0].TargetCurrency);
            Assert.AreEqual("JPY", latest[1].TargetCurrency);

            // The newer request finishes first, then the outdated one arrives late.
            latest[1].Complete(new ExchangeRate(Today, 160m));
            historic[1].Complete(FakeExchangeRateService.DefaultHistoricRates("JPY"));
            await currentRefresh;
            latest[0].Complete(new ExchangeRate(Today, 0.86m));
            historic[0].Complete(FakeExchangeRateService.DefaultHistoricRates("GBP"));
            await outdatedRefresh;

            Assert.AreEqual(160m, _viewModel.LatestConvertedAmount);
            Assert.IsTrue(_viewModel.HistoricConversions.All(c => c.TargetCurrency == "JPY"));
            Assert.IsFalse(_viewModel.IsBusy);
        }

        [TestMethod]
        public async Task WhileLoading_IsBusyIsSetAndRetryIsDisabled()
        {
            await InitializeAsync();
            _service.ManualCompletion = true;
            var canExecuteChanges = 0;
            _viewModel.RetryCommand.CanExecuteChanged += (sender, args) => canExecuteChanges++;

            _viewModel.SelectedTargetCurrency = Currency("GBP");

            Assert.IsTrue(_viewModel.IsBusy);
            Assert.IsFalse(_viewModel.RetryCommand.CanExecute(null));

            _service.PendingLatest.Single().Complete(new ExchangeRate(Today, 0.86m));
            _service.PendingHistoric.Single().Complete(FakeExchangeRateService.DefaultHistoricRates("GBP"));
            await _viewModel.PendingRefresh;

            Assert.IsFalse(_viewModel.IsBusy);
            Assert.IsTrue(_viewModel.RetryCommand.CanExecute(null));
            Assert.AreEqual(2, canExecuteChanges);
        }

        // ------------------------------------------------------------------ binding notifications

        [TestMethod]
        public async Task ChangingAmount_NotifiesTheBoundOutputProperties()
        {
            await InitializeAsync();
            var changed = new List<string>();
            _viewModel.PropertyChanged += (sender, args) => changed.Add(args.PropertyName);

            _viewModel.AmountText = "5";

            CollectionAssert.IsSubsetOf(
                new[] { nameof(MainViewModel.AmountText), nameof(MainViewModel.LatestConvertedAmount), nameof(MainViewModel.HistoricConversions) },
                changed);
        }

        [TestMethod]
        public async Task InvalidInput_RaisesErrorsChangedForTheProperty()
        {
            await InitializeAsync();
            var changed = new List<string>();
            ((INotifyDataErrorInfo)_viewModel).ErrorsChanged += (sender, args) => changed.Add(args.PropertyName);

            _viewModel.AmountText = "abc";
            _viewModel.AmountText = "1";

            CollectionAssert.AreEqual(new[] { nameof(MainViewModel.AmountText), nameof(MainViewModel.AmountText) }, changed);
            Assert.IsFalse(_viewModel.HasErrors);
        }

        [TestMethod]
        public void Constructor_WithoutService_Throws()
        {
            Assert.ThrowsExactly<ArgumentNullException>(() => new MainViewModel(null));
        }

        private async Task InitializeAsync(TimeSpan? refreshDelay = null)
        {
            _viewModel = new MainViewModel(_service, refreshDelay ?? TimeSpan.Zero);
            await _viewModel.InitializeAsync();
            await _viewModel.PendingRefresh;
        }

        private Currency Currency(string code) => _viewModel.Currencies.Single(currency => currency.Code == code);
    }
}
