using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Challenge.Models;
using Challenge.Services;

namespace Challenge.ViewModels
{
    public sealed class MainViewModel : ValidatableViewModelBase
    {
        /// <summary>First date for which the Frankfurter API (European Central Bank) publishes rates.</summary>
        public static readonly DateTime EarliestAvailableDate = new DateTime(1999, 1, 4);

        public const decimal MaxAmount = 1000000000000m;

        private const string DefaultSourceCurrency = "EUR";
        private const string DefaultTargetCurrency = "USD";
        private const int DefaultHistoryDays = 30;
        private static readonly TimeSpan DefaultRefreshDelay = TimeSpan.FromMilliseconds(300);

        private readonly IExchangeRateService _exchangeRateService;
        private readonly TimeSpan _refreshDelay;
        private readonly RelayCommand _retryCommand;

        private CancellationTokenSource _refreshCancellation;
        private bool _isInitialized;

        // Rates of the last successful query. Kept separately from the displayed values so that a change of
        // the amount can be applied instantly without querying the API again.
        private ExchangeRate _latestRate;
        private IReadOnlyList<ExchangeRate> _historicRates = Array.Empty<ExchangeRate>();
        private string _ratesTargetCurrency;

        private string _amountText = "1";
        private decimal? _amount;
        private Currency _selectedSourceCurrency;
        private Currency _selectedTargetCurrency;
        private DateTime? _startDate;
        private DateTime? _endDate;
        private decimal? _latestConvertedAmount;
        private DateTime? _latestRateDate;
        private IReadOnlyList<HistoricConversion> _historicConversions = Array.Empty<HistoricConversion>();
        private bool _isBusy;
        private string _errorMessage;

        public MainViewModel(IExchangeRateService exchangeRateService)
            : this(exchangeRateService, DefaultRefreshDelay)
        {
        }

        /// <param name="refreshDelay">
        /// Debounce delay before querying the API, so that e.g. clicking through dates does not fire a request per click.
        /// </param>
        public MainViewModel(IExchangeRateService exchangeRateService, TimeSpan refreshDelay)
        {
            _exchangeRateService = exchangeRateService ?? throw new ArgumentNullException(nameof(exchangeRateService));
            _refreshDelay = refreshDelay;
            _retryCommand = new RelayCommand(Retry, () => !IsBusy);

            _endDate = DateTime.Today;
            _startDate = DateTime.Today.AddDays(-DefaultHistoryDays);

            ValidateAmount();
            ValidateDates();
        }

        public ObservableCollection<Currency> Currencies { get; } = new ObservableCollection<Currency>();

        public ICommand RetryCommand => _retryCommand;

        public DateTime MinDate => EarliestAvailableDate;

        public DateTime MaxDate => DateTime.Today;

        public string AmountText
        {
            get => _amountText;
            set
            {
                if (SetProperty(ref _amountText, value))
                {
                    ValidateAmount();
                    // The amount does not influence the rates, so the results are recalculated locally.
                    UpdateConvertedAmounts();
                }
            }
        }

        public Currency SelectedSourceCurrency
        {
            get => _selectedSourceCurrency;
            set
            {
                if (SetProperty(ref _selectedSourceCurrency, value))
                {
                    ValidateCurrencies();
                    ScheduleRefresh();
                }
            }
        }

        public Currency SelectedTargetCurrency
        {
            get => _selectedTargetCurrency;
            set
            {
                if (SetProperty(ref _selectedTargetCurrency, value))
                {
                    ValidateCurrencies();
                    ScheduleRefresh();
                }
            }
        }

        public DateTime? StartDate
        {
            get => _startDate;
            set
            {
                if (SetProperty(ref _startDate, value?.Date))
                {
                    ValidateDates();
                    ScheduleRefresh();
                }
            }
        }

        public DateTime? EndDate
        {
            get => _endDate;
            set
            {
                if (SetProperty(ref _endDate, value?.Date))
                {
                    ValidateDates();
                    ScheduleRefresh();
                }
            }
        }

        public decimal? LatestConvertedAmount
        {
            get => _latestConvertedAmount;
            private set => SetProperty(ref _latestConvertedAmount, value);
        }

        public DateTime? LatestRateDate
        {
            get => _latestRateDate;
            private set => SetProperty(ref _latestRateDate, value);
        }

        public IReadOnlyList<HistoricConversion> HistoricConversions
        {
            get => _historicConversions;
            private set => SetProperty(ref _historicConversions, value);
        }

        /// <summary>True if rates were loaded but none were published in the selected time frame (e.g. a weekend).</summary>
        public bool HasNoHistoricData => _amount.HasValue && _latestRate != null && _historicRates.Count == 0;

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    _retryCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            private set
            {
                if (SetProperty(ref _errorMessage, value))
                {
                    OnPropertyChanged(nameof(HasErrorMessage));
                }
            }
        }

        public bool HasErrorMessage => !string.IsNullOrEmpty(ErrorMessage);

        /// <summary>
        /// Task of the most recently scheduled refresh. Allows tests to await the asynchronous update.
        /// </summary>
        internal Task PendingRefresh { get; private set; } = Task.CompletedTask;

        /// <summary>
        /// Loads the available currencies, selects default currencies and triggers the first query.
        /// </summary>
        public async Task InitializeAsync()
        {
            IsBusy = true;
            ErrorMessage = null;
            IReadOnlyList<Currency> currencies;

            try
            {
                currencies = await _exchangeRateService.GetCurrenciesAsync(CancellationToken.None);
            }
            catch (ExchangeRateServiceException ex)
            {
                ErrorMessage = $"Could not load the list of currencies. {ex.Message}";
                return;
            }
            finally
            {
                IsBusy = false;
            }

            Currencies.Clear();
            foreach (var currency in currencies)
            {
                Currencies.Add(currency);
            }

            SelectedSourceCurrency = FindCurrency(DefaultSourceCurrency) ?? Currencies.FirstOrDefault();
            SelectedTargetCurrency = FindCurrency(DefaultTargetCurrency)
                                     ?? Currencies.FirstOrDefault(currency => currency != SelectedSourceCurrency);

            _isInitialized = true;
            ScheduleRefresh();
        }

        private void Retry()
        {
            if (_isInitialized)
            {
                ScheduleRefresh();
            }
            else
            {
                _ = InitializeAsync();
            }
        }

        /// <summary>
        /// Cancels any running query and starts a new one for the current selection.
        /// </summary>
        private void ScheduleRefresh()
        {
            if (!_isInitialized)
            {
                return;
            }

            // Superseded queries are cancelled so that a slow, outdated response can never overwrite newer results.
            _refreshCancellation?.Cancel();
            _refreshCancellation = new CancellationTokenSource();
            PendingRefresh = RefreshAsync(_refreshCancellation.Token);
        }

        private async Task RefreshAsync(CancellationToken cancellationToken)
        {
            if (!CanQueryRates())
            {
                ClearRates();
                ErrorMessage = null;
                IsBusy = false;
                return;
            }

            var sourceCurrency = SelectedSourceCurrency.Code;
            var targetCurrency = SelectedTargetCurrency.Code;
            var startDate = StartDate.Value;
            var endDate = EndDate.Value;

            IsBusy = true;
            ErrorMessage = null;

            try
            {
                if (_refreshDelay > TimeSpan.Zero)
                {
                    await Task.Delay(_refreshDelay, cancellationToken);
                }

                // Both requests run concurrently; awaiting them does not block the UI thread.
                var latestTask = _exchangeRateService.GetLatestRateAsync(sourceCurrency, targetCurrency, cancellationToken);
                var historicTask = _exchangeRateService.GetHistoricRatesAsync(
                    sourceCurrency, targetCurrency, startDate, endDate, cancellationToken);

                await Task.WhenAll(latestTask, historicTask);
                cancellationToken.ThrowIfCancellationRequested();

                _latestRate = latestTask.Result;
                _historicRates = historicTask.Result;
                _ratesTargetCurrency = targetCurrency;
                UpdateConvertedAmounts();
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                // A newer query has been started and owns the UI state now.
                return;
            }
            catch (ExchangeRateServiceException ex)
            {
                ClearRates();
                ErrorMessage = ex.Message;
            }
            catch (Exception ex)
            {
                // Last line of defence: this task is not awaited by the UI, so an exception would otherwise go unnoticed.
                ClearRates();
                ErrorMessage = $"An unexpected error occurred: {ex.Message}";
            }

            IsBusy = false;
        }

        private bool CanQueryRates()
        {
            return _isInitialized
                   && SelectedSourceCurrency != null
                   && SelectedTargetCurrency != null
                   && !HasErrorsFor(nameof(SelectedSourceCurrency))
                   && !HasErrorsFor(nameof(SelectedTargetCurrency))
                   && !HasErrorsFor(nameof(StartDate))
                   && !HasErrorsFor(nameof(EndDate));
        }

        private void ClearRates()
        {
            _latestRate = null;
            _historicRates = Array.Empty<ExchangeRate>();
            _ratesTargetCurrency = null;
            UpdateConvertedAmounts();
        }

        private void UpdateConvertedAmounts()
        {
            var amount = _amount;
            LatestRateDate = _latestRate?.Date;
            LatestConvertedAmount = amount.HasValue && _latestRate != null ? amount.Value * _latestRate.Rate : (decimal?)null;

            HistoricConversions = amount.HasValue
                ? _historicRates
                    .OrderByDescending(rate => rate.Date)
                    .Select(rate => new HistoricConversion(rate.Date, amount.Value * rate.Rate, _ratesTargetCurrency))
                    .ToList()
                : (IReadOnlyList<HistoricConversion>)Array.Empty<HistoricConversion>();

            OnPropertyChanged(nameof(HasNoHistoricData));
        }

        private void ValidateAmount()
        {
            string error = null;
            decimal? amount = null;

            if (string.IsNullOrWhiteSpace(AmountText))
            {
                error = "Please enter an amount.";
            }
            else if (!decimal.TryParse(AmountText, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed))
            {
                error = "Please enter a valid number.";
            }
            else if (parsed <= 0)
            {
                error = "The amount must be greater than zero.";
            }
            else if (parsed > MaxAmount)
            {
                error = $"The amount must not exceed {MaxAmount.ToString("N0", CultureInfo.CurrentCulture)}.";
            }
            else
            {
                amount = parsed;
            }

            _amount = amount;
            SetErrors(nameof(AmountText), error);
        }

        private void ValidateCurrencies()
        {
            SetErrors(nameof(SelectedSourceCurrency), SelectedSourceCurrency == null ? "Please select a source currency." : null);

            string targetError = null;
            if (SelectedTargetCurrency == null)
            {
                targetError = "Please select a target currency.";
            }
            else if (SelectedSourceCurrency != null && SelectedSourceCurrency.Code == SelectedTargetCurrency.Code)
            {
                targetError = "Please select a target currency different from the source currency.";
            }

            SetErrors(nameof(SelectedTargetCurrency), targetError);
        }

        private void ValidateDates()
        {
            var today = DateTime.Today;
            SetErrors(nameof(StartDate), ValidateDate(StartDate, "start", today));

            var endDateError = ValidateDate(EndDate, "end", today);
            if (endDateError == null && StartDate.HasValue && StartDate.Value > EndDate.Value)
            {
                endDateError = "The end date must not be before the start date.";
            }

            SetErrors(nameof(EndDate), endDateError);
        }

        private static string ValidateDate(DateTime? date, string name, DateTime today)
        {
            if (!date.HasValue)
            {
                return $"Please select a {name} date.";
            }

            if (date.Value < EarliestAvailableDate)
            {
                return $"Rates are only available from {EarliestAvailableDate.ToString("d", CultureInfo.CurrentCulture)} onwards.";
            }

            return date.Value > today ? $"The {name} date must not be in the future." : null;
        }

        private Currency FindCurrency(string code)
        {
            return Currencies.FirstOrDefault(currency => currency.Code == code);
        }
    }
}
