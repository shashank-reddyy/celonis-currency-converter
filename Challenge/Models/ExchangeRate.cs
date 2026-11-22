using System;

namespace Challenge.Models
{
    /// <summary>
    /// The value of one unit of a source currency in a target currency on a given date.
    /// </summary>
    public sealed class ExchangeRate
    {
        public ExchangeRate(DateTime date, decimal rate)
        {
            Date = date.Date;
            Rate = rate;
        }

        public DateTime Date { get; }

        public decimal Rate { get; }
    }
}
