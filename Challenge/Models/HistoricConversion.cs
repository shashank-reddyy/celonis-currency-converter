using System;

namespace Challenge.Models
{
    /// <summary>
    /// One row of the historic data list: the user-entered amount converted with the rate of a specific date.
    /// </summary>
    public sealed class HistoricConversion
    {
        public HistoricConversion(DateTime date, decimal convertedAmount, string targetCurrency)
        {
            Date = date;
            ConvertedAmount = convertedAmount;
            TargetCurrency = targetCurrency;
        }

        public DateTime Date { get; }

        public decimal ConvertedAmount { get; }

        public string TargetCurrency { get; }
    }
}
